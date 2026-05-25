using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Standalone FREEHUD Demon Hunter Strafe + primary macro.
    // F3 toggles Strafe macro. F2 toggles attack / movement primary mode while running.
    // Uses a local raw input helper and local buff/clickable actor checks.
    public class s7o_DHStrafePrimaryPlugin : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler
    {
        // ============================================================
        // USER SETTINGS
        // ============================================================

        // ── Hotkeys ────────────────────────────────────────────────
        public Key ToggleHotkey = Key.F3;
        // F2 toggles primary-fire mode.
        // F4 is reserved by FREEHUD for hiding the HUD interface, so do not use F4 here.
        public Key FireModeHotkey = Key.F2;

        // T is Diablo's default Town Portal key.
        // When pressed while the macro is running, the macro releases inputs and starts
        // a non-blocking town portal sequence. If the cast is interrupted, it resumes.
        public Key TownPortalHotkey = Key.T;

        // ── Town Portal sequence ───────────────────────────────────
        // When T is pressed while the macro is running, the macro releases Strafe/primary,
        // optionally fires one primary pulse to break Strafe animation, then tries to cast
        // town portal up to TownPortalAttempts times.
        //
        // This is intentionally non-blocking and state-driven.
        public int TownPortalPrePrimarySettleMs = 35;
        public int TownPortalAfterPrimarySettleMs = 120;
        public int TownPortalAttempts = 3;
        public int TownPortalKeyPressHoldMs = 35;
        public int TownPortalDetectCastMs = 180;

        // Between failed attempts, tap force-move briefly to clear stale portal/animation state.
        // This only works if ForceMoveVirtualKey is configured. If it is 0, the reset tap is skipped.
        public bool TownPortalUseForceMoveReset = true;
        public int TownPortalForceMoveTapMs = 20;
        public int TownPortalBetweenAttemptsMs = 65;

        // Safety poll while waiting for portal cast/interruption/town transition.
        public int TownPortalCastingPollMs = 25;

        // ── Skill key bindings ─────────────────────────────────────
        // FREEHUD cannot read the game's actual keybind layer.
        // These Windows virtual-key codes must match the user's Diablo III keybinds.
        // Defaults are standard Diablo III binds: 1, 2, 3, 4, and Shift.
        public ushort Skill1VirtualKey = 0x31; // 1
        public ushort Skill2VirtualKey = 0x32; // 2
        public ushort Skill3VirtualKey = 0x33; // 3
        public ushort Skill4VirtualKey = 0x34; // 4
        public ushort ForceStandstillVirtualKey = 0x10; // Shift

        // Optional suppression key for Force Move. Leave 0 if not used.
        // Example: Space = 0x20, A = 0x41, XButton cannot be represented here.
        public ushort ForceMoveVirtualKey = 0;

        public bool HoldStrafeContinuously = true;

        // ── Timings ────────────────────────────────────────────────
        // LightningMod fires primary pulses on a 30ms gate. Keep attack mode aligned.
        public int PrimaryNormalDelayMs = 35;
        public int PrimaryHighFrequencyDelayMs = 30;
        public int StrafeCheckDelayMs = 50;
        public int KeyPressHoldMs = 8;
        // Set to 0 so entering a new area does not create a delayed restart window.
        // Increase only if map-transition UI causes accidental key input.
        public int RecentMapBlockMs = 0;

        // If F3 is pressed slightly before the new area is fully valid,
        // remember the request briefly and start as soon as context/build data is ready.
        public int StartRequestAfterTransitionMs = 2500;

        // Allows F3 pressed immediately after a floor/town transition to start as soon as
        // the minimal safe game context is available, without waiting for paint/UI render readiness.
        public bool EnableFastTransitionStart = true;

        // Time window after any non-new-game area transition where a relaxed start path is allowed.
        public int FastTransitionStartWindowMs = 4000;

        // If skill collection is briefly stale after transition, cached Strafe/primary action keys
        // may be used during this window.
        public int CachedSkillStartGraceMs = 4000;

        // ── GoD / Momentum behavior ────────────────────────────────
        public double MomentumRefreshSeconds = 21.0;
        public int NoMonsterStackRefreshThreshold = 16;
        public uint MomentumBuffSno = 484289;
        public int MomentumBuffIconIndex = 10;

        // ── Behavior toggles ───────────────────────────────────────
        // Safety: do not keep sending game inputs while Windows key shortcuts are active.
        public bool PauseWhileWindowsKeyHeld = true;

        // If Diablo/FREEHUD loses foreground, fully stop and release all held inputs.
        public bool StopOnForegroundLost = true;

        public bool RequireDemonHunter = true;
        public bool RequireStrafeEquipped = true;
        public bool RequireGoD4ForPrimary = true;
        public bool DisableInTown = true;
        public bool StopOnInventoryOpen = true;
        public bool StopOnBlockingUi = true;
        public bool BlockPrimaryOnClickableActor = true;
        // Original Lightning used 5 yards for the main Strafe pause and 10 yards
        // for primary-fire suppression. 8 yards is a practical FREEHUD default
        // for comfortably interacting with pylons, shrines, chests, doors, portals, etc.
        public float StrafeClickableActorBlockDistance = 8.0f;
        public float PrimaryClickableActorBlockDistance = 10.0f;

        // ── UI/debug ───────────────────────────────────────────────
        public bool ShowStatusText = true;

        // Compact status text location.
        // Draw near lower-center, below the player buff icons and away from monster health bars.
        public float StatusTextCenterXFrac = 0.50f;
        // 0.58 places it just below the center buff icons on 1080p layouts.
        // Increase slightly if it still overlaps buff stack counts.
        // Lower number = higher on screen; suggested tuning range: 0.54f to 0.58f.
        public float StatusTextYFrac = 0.58f;

        // Extra pixel offset applied after StatusTextYFrac.
        // Positive moves text down. Negative moves text up.
        public float StatusTextYOffsetPx = 1.0f;

        public bool DebugLogEnabled = false;

        // ============================================================
        // Runtime state
        // ============================================================

        private IKeyEvent _toggleKeyEvent;
        private IKeyEvent _fireModeKeyEvent;
        private IKeyEvent _townPortalKeyEvent;
        private Key _boundToggleHotkey;
        private Key _boundFireModeHotkey;
        private Key _boundTownPortalHotkey;

        private enum TownPortalStage
        {
            Idle,
            PrePrimarySettle,
            PrimaryDown,
            PrimaryUp,
            AfterPrimarySettle,
            PortalKeyDown,
            PortalKeyUp,
            DetectCast,
            ForceMoveDown,
            ForceMoveUp,
            BetweenAttempts,
            Casting
        }

        private TownPortalStage _townPortalStage = TownPortalStage.Idle;
        private int _townPortalNextTick;
        private int _townPortalAttempt;
        private ActionKey _townPortalPrimaryActionKey = ActionKey.Unknown;
        private bool _townPortalPrimaryStandstillHeld;
        private ushort _townPortalForceMoveVk;

        private bool _running;
        private bool _highFrequencyMode;
        private bool _temporarilyPaused;
        private int _pendingStartUntilTick;
        private string _lastStartBlockedReason = string.Empty;

        private int _fastTransitionStartUntilTick;
        private int _cachedSkillValidUntilTick;

        private ActionKey _cachedStrafeActionKey = ActionKey.Unknown;
        private ActionKey _cachedPrimaryActionKey = ActionKey.Unknown;
        private uint _cachedPrimarySno;
        private int _cachedSetItemCount;

        private IPlayerSkill _skillStrafe;
        private IPlayerSkill _skillPrimary;
        private uint _primarySno;
        private int _setItemCount;
        private bool _strafeEquipped;

        private int _nextBuildRefreshTick;
        private const int BuildRefreshIdleMs = 500;
        private const int BuildRefreshRunningMs = 100;

        private int _nextStrafeCheckTick;
        private int _nextPrimaryFireTick;
        private int _lastPrimaryFireTick;
        private int _actMapRecentlyVisibleUntilTick;
        private int _worldMapRecentlyVisibleUntilTick;

        private bool _strafeHeld;
        private ActionKey _heldStrafeActionKey;


        private ActionKey _pendingPrimaryActionKey = ActionKey.Unknown;
        private int _pendingPrimaryUpTick;
        private bool _pendingPrimaryStandstillHeld;

        private string _lastStatus = "ready";
        private string _lastRuntimeBlockSignature = string.Empty;
        private int _nextRuntimeBlockDebugTick;
        private string _lastBuildStateSignature = string.Empty;

        private IUiElement _chatEditLine;
        private IUiElement _bossBattleRequestBox;
        private IUiElement _bossBattleOpenBox;

        private IFont _statusFont;
        private IFont _runningFont;
        private IFont _highFont;

        public s7o_DHStrafePrimaryPlugin()
        {
            Enabled = true;
            _heldStrafeActionKey = ActionKey.Unknown;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            EnsureKeyEventsCurrent();

            _chatEditLine = SafeGetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline");
            _bossBattleRequestBox = SafeRegisterOrGetUiElement("Root.NormalLayer.boss_join_party_main.LayoutRoot.Background.buttons");
            _bossBattleOpenBox = SafeRegisterOrGetUiElement("Root.NormalLayer.boss_enter_main.stack.wrapper");

            _statusFont = Hud.Render.CreateFont("tahoma", 8, 255, 220, 190, 80, true, false, 255, 0, 0, 0, true);
            _runningFont = Hud.Render.CreateFont("tahoma", 8, 255, 80, 255, 120, true, false, 255, 0, 0, 0, true);
            _highFont = Hud.Render.CreateFont("tahoma", 8, 255, 255, 80, 80, true, false, 255, 0, 0, 0, true);
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            int now = Environment.TickCount;

            DebugLogState("new-area-enter", null, newGame, area);

            FinishPendingPrimaryPress(now, true);
            CancelTownPortalSequence(now, "new area");
            StopStrafeHold();

            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _actMapRecentlyVisibleUntilTick = 0;
            _worldMapRecentlyVisibleUntilTick = 0;
            _nextBuildRefreshTick = 0;

            if (newGame)
            {
                _running = false;
                _temporarilyPaused = false;
                _pendingStartUntilTick = 0;
                _lastStartBlockedReason = string.Empty;
                _fastTransitionStartUntilTick = 0;
                _cachedSkillValidUntilTick = 0;
                _cachedStrafeActionKey = ActionKey.Unknown;
                _cachedPrimaryActionKey = ActionKey.Unknown;
                _cachedPrimarySno = 0;
                _cachedSetItemCount = 0;
                _lastStatus = "new game";
            }
            else
            {
                // Normal floor/map transition: keep the macro armed and preserve
                // the selected F2 mode so it can resume when context becomes valid.
                _fastTransitionStartUntilTick = now + Math.Max(250, FastTransitionStartWindowMs);
                _temporarilyPaused = _running;
                _lastStatus = _running ? "paused: new area" : "new area";
            }

            DebugLogState("new-area-exit", null, newGame, area);
        }

        public void ForceStopForDisable()
        {
            try { StopMacro("disabled"); }
            catch { }
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed)
                return;

            EnsureKeyEventsCurrent();
            int now = Environment.TickCount;
            RefreshBuildStateIfNeeded(now, true);

            if (_townPortalKeyEvent != null && _townPortalKeyEvent.Matches(keyEvent))
            {
                if (_running)
                {
                    // If a sequence is already active, do not stack another one.
                    if (!IsTownPortalSequenceActive())
                        BeginTownPortalSequence(now);

                    DebugLog("town portal key handled by sequence");
                }

                return;
            }

            if (_toggleKeyEvent != null && _toggleKeyEvent.Matches(keyEvent))
            {
                if (_running)
                {
                    _pendingStartUntilTick = 0;
                    StopMacro("manual stop");
                }
                else
                {
                    RequestStartMacro(now);
                }

                return;
            }

            if (_fireModeKeyEvent != null && _fireModeKeyEvent.Matches(keyEvent))
            {
                if (GetEffectiveSetItemCount() >= 4)
                {
                    _highFrequencyMode = !_highFrequencyMode;
                    _lastStatus = _highFrequencyMode ? "mode: fast attack" : "mode: movement";
                    DebugLog("mode: " + _lastStatus);
                }

                return;
            }
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;

            FinishPendingPrimaryPress(now, false);
            AdvanceTownPortalSequence(now);

            RefreshBuildStateIfNeeded(now, false);
            TrackRecentlyVisibleMaps(now);

            ProcessPendingStartRequest(now);

            if (_running && PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                FinishPendingPrimaryPress(now, true);
                CancelTownPortalSequence(now, "windows key");
                StopStrafeHold();

                if (StopOnForegroundLost && Hud != null && Hud.Window != null && !Hud.Window.IsForeground)
                {
                    StopMacro("not foreground");
                    return;
                }

                _temporarilyPaused = true;
                _lastStatus = "paused: windows key";
                return;
            }

            if (!_running)
                return;

            string reason;
            if (!IsValidRuntimeContext(out reason))
            {
                string fastReason = null;
                bool allowFastTransitionRuntime = IsFastTransitionStartWindowActive(now)
                    && CanFastStartAfterTransition(out fastReason);

                DebugLogRuntimeBlock(now, reason, allowFastTransitionRuntime, fastReason);

                if (!allowFastTransitionRuntime)
                {
                    // Match the original intent: temporary UI/foreground blockers release Strafe,
                    // but leave the macro armed so it can resume when the blocker disappears.
                    FinishPendingPrimaryPress(now, true);
                    StopStrafeHold();

                    if (reason == "in town"
                        || (StopOnForegroundLost && reason == "not foreground"))
                    {
                        StopMacro(reason);
                    }
                    else
                    {
                        _temporarilyPaused = true;
                        _lastStatus = "paused: " + reason;
                    }

                    return;
                }
            }

            if (IsTownPortalSequenceActive())
            {
                _temporarilyPaused = true;
                _lastStatus = "paused: town portal";
                return;
            }

            // Context is valid and no temporary pause is active.
            // Clear stale pause text from transitions so the status color matches the actual mode.
            if (_temporarilyPaused || (!string.IsNullOrEmpty(_lastStatus) && _lastStatus.StartsWith("paused:", StringComparison.OrdinalIgnoreCase)))
            {
                _temporarilyPaused = false;
                _lastStatus = GetEffectiveSetItemCount() >= 4
                    ? (_highFrequencyMode ? "running fast attack" : "running movement")
                    : "running strafe only";
            }

            string buildStopReason;
            if (ShouldStopForBuildChange(out buildStopReason))
            {
                if (IsFastTransitionStartWindowActive(now) && IsCachedSkillValid(now))
                {
                    _lastStatus = "initializing skills";
                    DebugLogState("build-stop-suppressed", buildStopReason, null, null);
                }
                else
                {
                    DebugLogState("build-stop-before-stop", buildStopReason, null, null);
                    StopMacro(buildStopReason);
                    return;
                }
            }

            MaintainStrafe(now);
            MaybeFirePrimary(now);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!ShowStatusText || clipState != ClipState.AfterClip)
                return;

            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Window == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused)
                return;

            if (DisableInTown && Hud.Game.IsInTown)
                return;

            RefreshBuildStateIfNeeded(Environment.TickCount, false);

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
                return;

            if (RequireStrafeEquipped && !_strafeEquipped)
                return;

            string text;
            IFont font;

            if (_running && (_temporarilyPaused || IsTownPortalSequenceActive()))
            {
                text = string.IsNullOrEmpty(_lastStatus) ? "Paused" : FirstUpper(_lastStatus);
                font = _statusFont;
            }
            else if (_running)
            {
                if (GetEffectiveSetItemCount() >= 4 && _highFrequencyMode)
                {
                    text = "Attack: " + FireModeHotkey + " = Move | " + ToggleHotkey + " = Stop";
                    font = _highFont;
                }
                else if (GetEffectiveSetItemCount() >= 4)
                {
                    text = "Move: " + FireModeHotkey + " = Attack | " + ToggleHotkey + " = Stop";
                    font = _runningFont;
                }
                else
                {
                    text = "Strafe: " + ToggleHotkey + " = Stop";
                    font = _runningFont;
                }
            }
            else
            {
                text = ToggleHotkey + " = Strafe";
                font = _statusFont;
            }

            if (font == null)
                return;

            var layout = font.GetTextLayout(text);

            float x = Hud.Window.Size.Width * StatusTextCenterXFrac - (layout.Metrics.Width / 2.0f);
            float y = (Hud.Window.Size.Height * StatusTextYFrac) + StatusTextYOffsetPx;

            font.DrawText(text, x, y, true);
        }

        private static string FirstUpper(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (text.Length == 1)
                return text.ToUpperInvariant();

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private void EnsureKeyEventsCurrent()
        {
            if (Hud == null || Hud.Input == null)
                return;

            if (_toggleKeyEvent == null || _boundToggleHotkey != ToggleHotkey)
            {
                _toggleKeyEvent = Hud.Input.CreateKeyEvent(true, ToggleHotkey, false, false, false);
                _boundToggleHotkey = ToggleHotkey;
            }

            if (_fireModeKeyEvent == null || _boundFireModeHotkey != FireModeHotkey)
            {
                _fireModeKeyEvent = Hud.Input.CreateKeyEvent(true, FireModeHotkey, false, false, false);
                _boundFireModeHotkey = FireModeHotkey;
            }

            if (_townPortalKeyEvent == null || _boundTownPortalHotkey != TownPortalHotkey)
            {
                _townPortalKeyEvent = Hud.Input.CreateKeyEvent(true, TownPortalHotkey, false, false, false);
                _boundTownPortalHotkey = TownPortalHotkey;
            }
        }

        private bool TryStartMacro()
        {
            RefreshBuildStateIfNeeded(Environment.TickCount, true);

            string reason;
            bool normalStart = CanStart(out reason);
            bool fastStart = false;

            if (!normalStart)
                fastStart = CanFastStartAfterTransition(out reason);

            if (!normalStart && !fastStart)
            {
                StopStrafeHold();

                _lastStartBlockedReason = reason ?? string.Empty;
                _lastStatus = _lastStartBlockedReason;

                DebugLog("start blocked: " + _lastStartBlockedReason);
                return false;
            }

            _pendingStartUntilTick = 0;
            _lastStartBlockedReason = string.Empty;

            _running = true;
            _temporarilyPaused = false;
            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _lastStatus = GetEffectiveSetItemCount() >= 4
                ? (_highFrequencyMode ? "running fast attack" : "running movement")
                : "running strafe only";

            if (fastStart)
                DebugLog("started via fast transition path");

            DebugLogState("started", _lastStatus, null, null);
            return true;
        }

        private void RequestStartMacro(int now)
        {
            _pendingStartUntilTick = 0;

            if (TryStartMacro())
                return;

            if (!CanQueueStartAfterTransition(_lastStartBlockedReason))
                return;

            _pendingStartUntilTick = now + Math.Max(250, StartRequestAfterTransitionMs);
            _lastStatus = "waiting for area";

            DebugLog("start request queued: " + _lastStartBlockedReason);
        }

        private void ProcessPendingStartRequest(int now)
        {
            if (_pendingStartUntilTick == 0)
                return;

            if (TickReached(now, _pendingStartUntilTick))
            {
                _pendingStartUntilTick = 0;
                _lastStatus = "ready";
                DebugLog("start request expired");
                return;
            }

            // Do not auto-start while Windows key is held.
            if (PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                _lastStatus = "waiting: windows key";
                return;
            }

            if (TryStartMacro())
                return;

            if (!CanQueueStartAfterTransition(_lastStartBlockedReason))
            {
                _pendingStartUntilTick = 0;
                DebugLog("start request cancelled: " + _lastStartBlockedReason);
            }
        }

        private bool CanQueueStartAfterTransition(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return false;

            // Never queue start in town or unsafe/non-build contexts.
            if (reason == "in town"
                || reason == "dead"
                || reason == "not Demon Hunter"
                || reason == "not foreground"
                || reason == "windows key")
            {
                return false;
            }

            // These can be transient immediately after a zone transition.
            return reason == "not in game"
                || reason == "loading"
                || reason == "paused"
                || reason == "hud unavailable"
                || reason == "player unavailable"
                || reason == "minimap hidden"
                || reason == "act map recently open"
                || reason == "world map recently open"
                || reason == "world map open"
                || reason == "blocking UI"
                || reason == "inventory open"
                || reason == "chat open"
                || reason == "Strafe not equipped"
                || reason == "primary skill not equipped";
        }

        private void StopMacro(string reason)
        {
            int now = Environment.TickCount;

            DebugLogState("stop-before-reset", reason, null, null);

            CancelTownPortalSequence(now, "stop macro");
            FinishPendingPrimaryPress(now, true);
            StopStrafeHold();

            _running = false;
            _temporarilyPaused = false;
            _pendingStartUntilTick = 0;
            _lastStartBlockedReason = string.Empty;
            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _actMapRecentlyVisibleUntilTick = 0;
            _worldMapRecentlyVisibleUntilTick = 0;
            _lastStatus = string.IsNullOrEmpty(reason) ? "stopped" : reason;

            DebugLog("stopped: " + _lastStatus);
        }

        private void RefreshBuildStateIfNeeded(int now, bool force)
        {
            if (!force && !TickReached(now, _nextBuildRefreshTick))
                return;

            RefreshBuildState();

            int interval = _running ? BuildRefreshRunningMs : BuildRefreshIdleMs;
            _nextBuildRefreshTick = now + Math.Max(50, interval);
        }

        private void RefreshBuildState()
        {
            // During GR floor transitions FreeHUD can briefly report IsInGame=false or expose an
            // empty Powers snapshot before OnNewArea fires. Do not wipe the last known build
            // state in that transient window, or the running macro can stop on a false
            // "Strafe not equipped" result before the normal new-area pause/resume path runs.
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null
                || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused)
                return;

            _skillStrafe = null;
            _skillPrimary = null;
            _primarySno = 0;
            _setItemCount = 0;
            _strafeEquipped = false;

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
                return;

            var powers = Hud.Game.Me.Powers;
            var dh = powers.UsedDemonHunterPowers;

            _skillStrafe = dh == null ? null : dh.Strafe;

            foreach (var skill in powers.UsedSkills)
            {
                if (skill == null || skill.SnoPower == null)
                    continue;

                uint sno = skill.SnoPower.Sno;

                if (sno == 134030)
                    _strafeEquipped = true;

                switch (sno)
                {
                    case 129215:
                        _skillPrimary = dh == null ? null : dh.HungeringArrow;
                        _primarySno = 129215;
                        break;
                    case 361936:
                        _skillPrimary = dh == null ? null : dh.EntanglingShot;
                        _primarySno = 361936;
                        break;
                    case 77552:
                        _skillPrimary = dh == null ? null : dh.Bolas;
                        _primarySno = 77552;
                        break;
                    case 377450:
                        _skillPrimary = dh == null ? null : dh.EvasiveFire;
                        _primarySno = 377450;
                        break;
                    case 86610:
                        _skillPrimary = dh == null ? null : dh.Grenades;
                        _primarySno = 86610;
                        break;
                }
            }

            try { _setItemCount = Hud.Game.Me.GetSetItemCount(791249); }
            catch { _setItemCount = 0; }

            int now = Environment.TickCount;

            if (_skillStrafe != null && _skillStrafe.Key != ActionKey.Unknown)
            {
                _cachedStrafeActionKey = _skillStrafe.Key;
                _cachedSkillValidUntilTick = now + Math.Max(250, CachedSkillStartGraceMs);
            }

            if (_skillPrimary != null && _skillPrimary.Key != ActionKey.Unknown)
            {
                _cachedPrimaryActionKey = _skillPrimary.Key;
                _cachedPrimarySno = _primarySno;
                _cachedSetItemCount = _setItemCount;
                _cachedSkillValidUntilTick = now + Math.Max(250, CachedSkillStartGraceMs);
            }

            if (_setItemCount > 0)
                _cachedSetItemCount = _setItemCount;

            DebugLogBuildStateIfChanged("refresh-build");
        }

        private bool ShouldStopForBuildChange(out string reason)
        {
            reason = null;

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
            {
                reason = "not Demon Hunter";
                return true;
            }

            if (RequireStrafeEquipped && (!_strafeEquipped || _skillStrafe == null))
            {
                reason = "Strafe not equipped";
                return true;
            }

            if (_setItemCount >= 4 && _skillPrimary == null)
            {
                reason = "primary skill not equipped";
                return true;
            }

            return false;
        }

        private bool CanStart(out string reason)
        {
            reason = null;

            if (!IsValidRuntimeContext(out reason))
                return false;

            if (PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                reason = "windows key";
                return false;
            }

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
            {
                reason = "not Demon Hunter";
                return false;
            }

            if (RequireStrafeEquipped && (!_strafeEquipped || _skillStrafe == null))
            {
                reason = "Strafe not equipped";
                return false;
            }

            if (_setItemCount >= 4 && _skillPrimary == null)
            {
                reason = "primary skill not equipped";
                return false;
            }

            return true;
        }

        private bool IsCachedSkillValid(int now)
        {
            return _cachedSkillValidUntilTick != 0 && !TickReached(now, _cachedSkillValidUntilTick);
        }

        private ActionKey GetStrafeActionKey()
        {
            int now = Environment.TickCount;

            if (_skillStrafe != null && _skillStrafe.Key != ActionKey.Unknown)
                return _skillStrafe.Key;

            if (IsCachedSkillValid(now))
                return _cachedStrafeActionKey;

            return ActionKey.Unknown;
        }

        private ActionKey GetPrimaryActionKey()
        {
            int now = Environment.TickCount;

            if (_skillPrimary != null && _skillPrimary.Key != ActionKey.Unknown)
                return _skillPrimary.Key;

            if (IsCachedSkillValid(now))
                return _cachedPrimaryActionKey;

            return ActionKey.Unknown;
        }

        private int GetEffectiveSetItemCount()
        {
            return _setItemCount > 0 ? _setItemCount : _cachedSetItemCount;
        }

        private uint GetEffectivePrimarySno()
        {
            return _primarySno != 0 ? _primarySno : _cachedPrimarySno;
        }

        private bool IsFastTransitionStartWindowActive(int now)
        {
            return EnableFastTransitionStart
                && _fastTransitionStartUntilTick != 0
                && !TickReached(now, _fastTransitionStartUntilTick);
        }

        private bool CanFastStartAfterTransition(out string reason)
        {
            reason = null;

            if (!EnableFastTransitionStart)
            {
                reason = "fast start disabled";
                return false;
            }

            int now = Environment.TickCount;

            if (!IsFastTransitionStartWindowActive(now))
            {
                reason = "fast start window expired";
                return false;
            }

            if (!Enabled)
            {
                reason = "plugin disabled";
                return false;
            }

            if (Hud == null || Hud.Game == null || Hud.Window == null)
            {
                reason = "hud unavailable";
                return false;
            }

            if (!Hud.Window.IsForeground)
            {
                reason = "not foreground";
                return false;
            }

            if (PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                reason = "windows key";
                return false;
            }

            if (!Hud.Game.IsInGame)
            {
                reason = "not in game";
                return false;
            }

            if (Hud.Game.IsLoading)
            {
                reason = "loading";
                return false;
            }

            if (Hud.Game.IsPaused)
            {
                reason = "paused";
                return false;
            }

            if (Hud.Game.Me == null)
            {
                reason = "player unavailable";
                return false;
            }

            if (Hud.Game.Me.IsDead)
            {
                reason = "dead";
                return false;
            }

            if (DisableInTown && Hud.Game.IsInTown)
            {
                reason = "in town";
                return false;
            }

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
            {
                reason = "not Demon Hunter";
                return false;
            }

            var strafeKey = GetStrafeActionKey();
            if (RequireStrafeEquipped && strafeKey == ActionKey.Unknown)
            {
                reason = "Strafe not equipped";
                return false;
            }

            if (GetEffectiveSetItemCount() >= 4 && GetPrimaryActionKey() == ActionKey.Unknown)
            {
                reason = "primary skill not equipped";
                return false;
            }

            return true;
        }

        private bool IsValidRuntimeContext(out string reason)
        {
            reason = null;

            if (Hud == null || Hud.Game == null || Hud.Window == null || Hud.Render == null)
            {
                reason = "hud unavailable";
                return false;
            }

            int now = Environment.TickCount;

            if (!Hud.Game.IsInGame) { reason = "not in game"; return false; }
            if (Hud.Game.IsLoading) { reason = "loading"; return false; }
            if (Hud.Game.IsPaused) { reason = "paused"; return false; }
            if (DisableInTown && Hud.Game.IsInTown) { reason = "in town"; return false; }
            if (Hud.Game.Me == null) { reason = "player unavailable"; return false; }
            if (Hud.Game.Me.IsDead) { reason = "dead"; return false; }
            if (!Hud.Window.IsForeground) { reason = "not foreground"; return false; }

            if (StopOnInventoryOpen && Hud.Inventory != null && IsVisible(Hud.Inventory.InventoryMainUiElement))
            {
                reason = "inventory open";
                return false;
            }

            if (_chatEditLine != null && IsVisible(_chatEditLine))
            {
                reason = "chat open";
                return false;
            }

            if (IsVisible(Hud.Render.WorldMapUiElement))
            {
                reason = "world map open";
                return false;
            }

            if (TickNotExpired(now, _actMapRecentlyVisibleUntilTick))
            {
                reason = "act map recently open";
                return false;
            }

            if (TickNotExpired(now, _worldMapRecentlyVisibleUntilTick))
            {
                reason = "world map recently open";
                return false;
            }

            if (StopOnBlockingUi && IsAnyBlockingUiElementVisibleSafe())
            {
                reason = "blocking UI";
                return false;
            }

            if (Hud.Render.MinimapUiElement != null && !IsVisible(Hud.Render.MinimapUiElement))
            {
                reason = "minimap hidden";
                return false;
            }

            if (!CursorInsideGameWindow())
            {
                reason = "cursor outside game window";
                return false;
            }

            if (IsCastingTownPortal())
            {
                reason = "town portal";
                return false;
            }

            if (Hud.Game.Me.Powers != null &&
                (Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyAllWithCast.Sno)
                || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCast.Sno)
                || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCastLegendary.Sno)
                || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_AxeOperateGizmo.Sno)
                || Hud.Game.Me.AnimationState == AcdAnimationState.Transform
                || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno)))
            {
                reason = "casting/ghosted";
                return false;
            }

            if (BlockPrimaryOnClickableActor && IsHoverValidActor(StrafeClickableActorBlockDistance))
            {
                reason = "clickable actor";
                return false;
            }

            if (IsCursorInsideUi(_bossBattleRequestBox) || IsCursorInsideUi(_bossBattleOpenBox))
            {
                reason = "boss dialog";
                return false;
            }

            return true;
        }

        private void MaintainStrafe(int now)
        {
            var strafeKey = GetStrafeActionKey();

            if (!HoldStrafeContinuously || strafeKey == ActionKey.Unknown)
                return;

            if (!TickReached(now, _nextStrafeCheckTick))
                return;

            _nextStrafeCheckTick = now + Math.Max(10, StrafeCheckDelayMs);

            if (IsStrafeBuffActive() && _strafeHeld)
                return;

            StopStrafeHold();
            StartStrafeHold();
        }

        private void StartStrafeHold()
        {
            var strafeKey = GetStrafeActionKey();

            if (strafeKey == ActionKey.Unknown)
                return;

            if (SendActionDown(strafeKey))
            {
                _heldStrafeActionKey = strafeKey;
                _strafeHeld = true;

                // Original Lightning call released standstill after starting left-skill Strafe.
                if (strafeKey == ActionKey.LeftSkill && ForceStandstillVirtualKey != 0)
                    s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
            }
        }

        private void StopStrafeHold()
        {
            if (!_strafeHeld)
                return;

            var keyToRelease = _heldStrafeActionKey;
            if (keyToRelease == ActionKey.Unknown && _skillStrafe != null)
                keyToRelease = _skillStrafe.Key;

            if (keyToRelease != ActionKey.Unknown)
                SendActionUp(keyToRelease);

            _strafeHeld = false;
            _heldStrafeActionKey = ActionKey.Unknown;
        }

        private bool IsStrafeBuffActive()
        {
            return Hud != null
                && Hud.Game != null
                && Hud.Game.Me != null
                && Hud.Game.Me.Powers != null
                && Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.DemonHunter_Strafe.Sno, 0);
        }

        private void MaybeFirePrimary(int now)
        {
            var primaryKey = GetPrimaryActionKey();

            if (primaryKey == ActionKey.Unknown)
                return;

            if (_pendingPrimaryActionKey != ActionKey.Unknown)
                return;

            if (RequireGoD4ForPrimary && GetEffectiveSetItemCount() < 4)
                return;

            int delay = Math.Max(5, _highFrequencyMode ? PrimaryHighFrequencyDelayMs : PrimaryNormalDelayMs);

            if (!TickReached(now, _nextPrimaryFireTick))
                return;

            if (BlockPrimaryOnClickableActor && IsHoverValidActor(PrimaryClickableActorBlockDistance))
            {
                _nextPrimaryFireTick = now + delay;
                return;
            }

            if (IsForceMoveHeld())
            {
                _nextPrimaryFireTick = now + delay;
                return;
            }

            bool momentumWantsPrimary = ShouldFirePrimaryNormalMode();

            if (!_highFrequencyMode && !momentumWantsPrimary)
            {
                _nextPrimaryFireTick = now + 10;
                return;
            }

            if (DoActionAutoShift(primaryKey, now))
            {
                _lastPrimaryFireTick = now;
                _nextPrimaryFireTick = now + delay;

            }
            else
            {
                _nextPrimaryFireTick = now + 50;
            }
        }

        private bool ShouldFirePrimaryNormalMode()
        {
            if (GetEffectiveSetItemCount() < 4)
                return false;

            if (GetOnScreenMonsterCount() == 0)
                return GetBuffCount(MomentumBuffSno, MomentumBuffIconIndex) <= NoMonsterStackRefreshThreshold;

            return GetBuffLeftTime(MomentumBuffSno, MomentumBuffIconIndex) <= MomentumRefreshSeconds;
        }

        private int GetBuffCount(uint sno, int index)
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                return 0;

            var buff = Hud.Game.Me.Powers.GetBuff(sno);
            if (buff == null || buff.IconCounts == null || index < 0 || index >= buff.IconCounts.Length)
                return 0;

            return buff.IconCounts[index];
        }

        private double GetBuffLeftTime(uint sno, int index)
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                return 0;

            var buff = Hud.Game.Me.Powers.GetBuff(sno);
            if (buff == null || buff.TimeLeftSeconds == null || index < 0 || index >= buff.TimeLeftSeconds.Length)
                return 0;

            return buff.TimeLeftSeconds[index];
        }

        private int GetOnScreenMonsterCount()
        {
            try
            {
                return Hud.Game.AliveMonsters.Count(m => m != null && m.IsAlive && m.IsOnScreen && !m.Hidden && !m.Invisible && !m.Untargetable);
            }
            catch
            {
                return 0;
            }
        }

        private bool IsHoverValidActor(float distance)
        {
            if (Hud == null || Hud.Game == null)
                return false;

            var actor = Hud.Game.SelectedActor;
            if (actor == null || actor.SnoActor == null)
                return false;

            if (actor.NormalizedXyDistanceToMe > distance)
                return false;

            return actor.SnoActor.Kind == ActorKind.Shrine
                || actor.SnoActor.Kind == ActorKind.Portal
                || actor.SnoActor.Kind == ActorKind.Waypoint
                || actor.SnoActor.Kind == ActorKind.CursedEvent
                || actor.SnoActor.Kind == ActorKind.ChestNormal
                || actor.SnoActor.Kind == ActorKind.Chest
                || actor.SnoActor.Kind == ActorKind.WeaponRack
                || actor.SnoActor.Kind == ActorKind.ArmorRack
                || actor.SnoActor.Kind == ActorKind.QuestActivate
                || actor.GizmoType == GizmoType.Door
                || actor.GizmoType == GizmoType.Headstone
                || actor.GizmoType == GizmoType.Portal
                || actor.GizmoType == GizmoType.Waypoint
                || actor.GizmoType == GizmoType.Chest
                || actor.GizmoType == GizmoType.BossPortal;
        }

        private bool SendActionDown(ActionKey actionKey)
        {
            switch (actionKey)
            {
                case ActionKey.LeftSkill:
                    return s7o_DHStrafePrimaryInput.MouseDownLeft();
                case ActionKey.RightSkill:
                    return s7o_DHStrafePrimaryInput.MouseDownRight();
                case ActionKey.Skill1:
                    return s7o_DHStrafePrimaryInput.KeyDown(Skill1VirtualKey);
                case ActionKey.Skill2:
                    return s7o_DHStrafePrimaryInput.KeyDown(Skill2VirtualKey);
                case ActionKey.Skill3:
                    return s7o_DHStrafePrimaryInput.KeyDown(Skill3VirtualKey);
                case ActionKey.Skill4:
                    return s7o_DHStrafePrimaryInput.KeyDown(Skill4VirtualKey);
                default:
                    return false;
            }
        }

        private bool SendActionUp(ActionKey actionKey)
        {
            switch (actionKey)
            {
                case ActionKey.LeftSkill:
                    return s7o_DHStrafePrimaryInput.MouseUpLeft();
                case ActionKey.RightSkill:
                    return s7o_DHStrafePrimaryInput.MouseUpRight();
                case ActionKey.Skill1:
                    return s7o_DHStrafePrimaryInput.KeyUp(Skill1VirtualKey);
                case ActionKey.Skill2:
                    return s7o_DHStrafePrimaryInput.KeyUp(Skill2VirtualKey);
                case ActionKey.Skill3:
                    return s7o_DHStrafePrimaryInput.KeyUp(Skill3VirtualKey);
                case ActionKey.Skill4:
                    return s7o_DHStrafePrimaryInput.KeyUp(Skill4VirtualKey);
                default:
                    return false;
            }
        }

        private bool DoActionAutoShift(ActionKey actionKey, int now)
        {
            if (actionKey == ActionKey.Unknown)
                return false;

            if (_pendingPrimaryActionKey != ActionKey.Unknown)
                return false;

            // Avoid releasing the held Strafe key if someone configured primary and Strafe
            // onto the same action slot.
            if (_strafeHeld && actionKey == _heldStrafeActionKey)
                return false;

            bool standstillDown = false;

            if (ForceStandstillVirtualKey != 0)
                standstillDown = s7o_DHStrafePrimaryInput.KeyDown(ForceStandstillVirtualKey);

            bool actionDown = SendActionDown(actionKey);

            if (!actionDown)
            {
                if (standstillDown && ForceStandstillVirtualKey != 0)
                    s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);

                return false;
            }

            _pendingPrimaryActionKey = actionKey;
            _pendingPrimaryUpTick = now + Math.Max(1, KeyPressHoldMs);
            _pendingPrimaryStandstillHeld = standstillDown;

            return true;
        }

        private void FinishPendingPrimaryPress(int now, bool force)
        {
            if (_pendingPrimaryActionKey == ActionKey.Unknown)
                return;

            if (!force && !TickReached(now, _pendingPrimaryUpTick))
                return;

            SendActionUp(_pendingPrimaryActionKey);

            if (_pendingPrimaryStandstillHeld && ForceStandstillVirtualKey != 0)
                s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);

            _pendingPrimaryActionKey = ActionKey.Unknown;
            _pendingPrimaryUpTick = 0;
            _pendingPrimaryStandstillHeld = false;
        }

        private ushort GetTownPortalVirtualKey()
        {
            switch (TownPortalHotkey)
            {
                case Key.T: return 0x54;
                case Key.F1: return 0x70;
                case Key.F2: return 0x71;
                case Key.F3: return 0x72;
                case Key.F5: return 0x74;
                case Key.F6: return 0x75;
                case Key.F7: return 0x76;
                case Key.F8: return 0x77;
                case Key.F9: return 0x78;
                case Key.F10: return 0x79;
                case Key.F11: return 0x7A;
                case Key.F12: return 0x7B;
                default: return 0x54;
            }
        }

        private bool IsTownPortalSequenceActive()
        {
            return _townPortalStage != TownPortalStage.Idle;
        }

        private void BeginTownPortalSequence(int now)
        {
            FinishPendingPrimaryPress(now, true);
            StopStrafeHold();

            // Release standstill in case the macro was holding it for primary actions.
            if (ForceStandstillVirtualKey != 0)
                s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);

            _townPortalAttempt = 0;
            _townPortalPrimaryActionKey = ActionKey.Unknown;
            _townPortalPrimaryStandstillHeld = false;
            _townPortalForceMoveVk = ForceMoveVirtualKey;

            _townPortalStage = TownPortalStage.PrePrimarySettle;
            _townPortalNextTick = now + Math.Max(0, TownPortalPrePrimarySettleMs);

            _lastStatus = "paused: town portal";
            DebugLog("town portal sequence started");
        }

        private void CancelTownPortalSequence(int now, string reason)
        {
            ReleaseTownPortalSequenceInputs();

            _townPortalStage = TownPortalStage.Idle;
            _townPortalNextTick = 0;
            _townPortalAttempt = 0;
            _townPortalPrimaryActionKey = ActionKey.Unknown;
            _townPortalPrimaryStandstillHeld = false;
            _townPortalForceMoveVk = 0;

            if (!string.IsNullOrEmpty(reason))
                DebugLog("town portal sequence cancelled: " + reason);
        }

        private void ReleaseTownPortalSequenceInputs()
        {
            try { s7o_DHStrafePrimaryInput.KeyUp(GetTownPortalVirtualKey()); } catch { }

            if (_townPortalForceMoveVk != 0)
            {
                try { s7o_DHStrafePrimaryInput.KeyUp(_townPortalForceMoveVk); } catch { }
            }

            if (_townPortalPrimaryActionKey != ActionKey.Unknown)
            {
                try { SendActionUp(_townPortalPrimaryActionKey); } catch { }
            }

            if (_townPortalPrimaryStandstillHeld && ForceStandstillVirtualKey != 0)
            {
                try { s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey); } catch { }
            }
        }

        private void AdvanceTownPortalSequence(int now)
        {
            if (_townPortalStage == TownPortalStage.Idle)
                return;

            // If the portal cast is active, stay paused and wait.
            if (IsCastingTownPortal())
            {
                _townPortalStage = TownPortalStage.Casting;
                _townPortalNextTick = now + Math.Max(10, TownPortalCastingPollMs);
                _lastStatus = "paused: town portal";
                return;
            }

            // If we were casting and it stopped outside town, it was interrupted/cancelled.
            // Resume macro immediately.
            if (_townPortalStage == TownPortalStage.Casting)
            {
                CancelTownPortalSequence(now, "portal interrupted or cancelled");
                _lastStatus = _running ? "portal interrupted; resuming" : "portal interrupted";
                return;
            }

            if (!TickReached(now, _townPortalNextTick))
                return;

            switch (_townPortalStage)
            {
                case TownPortalStage.PrePrimarySettle:
                {
                    // Fire one quick primary pulse to break Strafe animation, if a primary exists.
                    // If not available, skip directly to T attempts.
                    var primaryKey = GetPrimaryActionKey();

                    if (primaryKey != ActionKey.Unknown)
                    {
                        _townPortalPrimaryActionKey = primaryKey;

                        bool standstillDown = false;
                        if (ForceStandstillVirtualKey != 0)
                            standstillDown = s7o_DHStrafePrimaryInput.KeyDown(ForceStandstillVirtualKey);

                        bool actionDown = SendActionDown(_townPortalPrimaryActionKey);

                        if (actionDown)
                        {
                            _townPortalPrimaryStandstillHeld = standstillDown;
                            _townPortalStage = TownPortalStage.PrimaryUp;
                            _townPortalNextTick = now + Math.Max(5, KeyPressHoldMs);
                            DebugLog("town portal sequence primary pulse down");
                            return;
                        }

                        if (standstillDown && ForceStandstillVirtualKey != 0)
                            s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
                    }

                    _townPortalStage = TownPortalStage.AfterPrimarySettle;
                    _townPortalNextTick = now + Math.Max(0, TownPortalAfterPrimarySettleMs);
                    return;
                }

                case TownPortalStage.PrimaryUp:
                {
                    if (_townPortalPrimaryActionKey != ActionKey.Unknown)
                        SendActionUp(_townPortalPrimaryActionKey);

                    if (_townPortalPrimaryStandstillHeld && ForceStandstillVirtualKey != 0)
                        s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);

                    _townPortalPrimaryActionKey = ActionKey.Unknown;
                    _townPortalPrimaryStandstillHeld = false;

                    _townPortalStage = TownPortalStage.AfterPrimarySettle;
                    _townPortalNextTick = now + Math.Max(0, TownPortalAfterPrimarySettleMs);
                    DebugLog("town portal sequence primary pulse up");
                    return;
                }

                case TownPortalStage.AfterPrimarySettle:
                case TownPortalStage.BetweenAttempts:
                {
                    if (_townPortalAttempt >= Math.Max(1, TownPortalAttempts))
                    {
                        CancelTownPortalSequence(now, "portal attempts exhausted");
                        _lastStatus = _running ? "portal failed; resuming" : "portal failed";
                        return;
                    }

                    _townPortalAttempt++;

                    ushort vk = GetTownPortalVirtualKey();
                    s7o_DHStrafePrimaryInput.KeyDown(vk);

                    _townPortalStage = TownPortalStage.PortalKeyUp;
                    _townPortalNextTick = now + Math.Max(5, TownPortalKeyPressHoldMs);

                    DebugLog("town portal T down attempt " + _townPortalAttempt);
                    return;
                }

                case TownPortalStage.PortalKeyUp:
                {
                    ushort vk = GetTownPortalVirtualKey();
                    s7o_DHStrafePrimaryInput.KeyUp(vk);

                    _townPortalStage = TownPortalStage.DetectCast;
                    _townPortalNextTick = now + Math.Max(25, TownPortalDetectCastMs);

                    DebugLog("town portal T up attempt " + _townPortalAttempt);
                    return;
                }

                case TownPortalStage.DetectCast:
                {
                    if (IsCastingTownPortal())
                    {
                        _townPortalStage = TownPortalStage.Casting;
                        _townPortalNextTick = now + Math.Max(10, TownPortalCastingPollMs);
                        DebugLog("town portal casting detected");
                        return;
                    }

                    if (TownPortalUseForceMoveReset && _townPortalForceMoveVk != 0)
                    {
                        s7o_DHStrafePrimaryInput.KeyDown(_townPortalForceMoveVk);

                        _townPortalStage = TownPortalStage.ForceMoveUp;
                        _townPortalNextTick = now + Math.Max(5, TownPortalForceMoveTapMs);

                        DebugLog("town portal force-move reset down");
                        return;
                    }

                    _townPortalStage = TownPortalStage.BetweenAttempts;
                    _townPortalNextTick = now + Math.Max(0, TownPortalBetweenAttemptsMs);
                    return;
                }

                case TownPortalStage.ForceMoveUp:
                {
                    if (_townPortalForceMoveVk != 0)
                        s7o_DHStrafePrimaryInput.KeyUp(_townPortalForceMoveVk);

                    _townPortalStage = TownPortalStage.BetweenAttempts;
                    _townPortalNextTick = now + Math.Max(0, TownPortalBetweenAttemptsMs);

                    DebugLog("town portal force-move reset up");
                    return;
                }
            }
        }

        private bool IsCastingTownPortal()
        {
            return Hud != null
                && Hud.Game != null
                && Hud.Game.Me != null
                && Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal;
        }

        private bool IsForceMoveHeld()
        {
            if (ForceMoveVirtualKey == 0)
                return false;

            return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(ForceMoveVirtualKey);
        }

        private bool IsWindowsKeyDown()
        {
            // VK_LWIN = 0x5B, VK_RWIN = 0x5C
            return s7o_DHStrafePrimaryInput.IsVirtualKeyDown((ushort)0x5B)
                || s7o_DHStrafePrimaryInput.IsVirtualKeyDown((ushort)0x5C);
        }

        private void TrackRecentlyVisibleMaps(int now)
        {
            if (Hud == null || Hud.Render == null)
                return;

            int ms = Math.Max(0, RecentMapBlockMs);

            if (IsVisible(Hud.Render.ActMapUiElement))
                _actMapRecentlyVisibleUntilTick = now + ms;

            if (IsVisible(Hud.Render.WorldMapUiElement))
                _worldMapRecentlyVisibleUntilTick = now + ms;
        }

        private IUiElement SafeGetUiElement(string path)
        {
            try { return Hud.Render.GetUiElement(path); }
            catch { return null; }
        }

        private IUiElement SafeRegisterOrGetUiElement(string path)
        {
            try { return Hud.Render.RegisterUiElement(path, null, null); }
            catch
            {
                try { return Hud.Render.GetUiElement(path); }
                catch { return null; }
            }
        }

        private bool IsVisible(IUiElement ui)
        {
            if (ui == null)
                return false;

            try
            {
                ui.Refresh();
                return ui.Visible;
            }
            catch
            {
                return false;
            }
        }

        private bool IsCursorInsideUi(IUiElement ui)
        {
            if (ui == null || !IsVisible(ui))
                return false;

            try
            {
                var r = ui.Rectangle;
                return Hud.Window.CursorInsideRect(r.Left, r.Top, r.Width, r.Height);
            }
            catch
            {
                return false;
            }
        }

        private bool CursorInsideGameWindow()
        {
            try
            {
                return Hud.Window.CursorX >= 0
                    && Hud.Window.CursorY >= 0
                    && Hud.Window.CursorX <= Hud.Window.Size.Width
                    && Hud.Window.CursorY <= Hud.Window.Size.Height;
            }
            catch
            {
                return true;
            }
        }

        private bool IsAnyBlockingUiElementVisibleSafe()
        {
            // FREEHUD's public IRenderController does not expose this member.
            // Some forks do, so use reflection without taking a compile-time dependency.
            try
            {
                var render = Hud.Render;
                if (render == null)
                    return false;

                var prop = render.GetType().GetProperty("IsAnyBlockingUiElementVisible");
                if (prop == null || prop.PropertyType != typeof(bool))
                    return false;

                return (bool)prop.GetValue(render, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool TickReached(int now, int targetTick)
        {
            return targetTick == 0 || unchecked(now - targetTick) >= 0;
        }

        private static bool TickNotExpired(int now, int untilTick)
        {
            return untilTick != 0 && unchecked(untilTick - now) > 0;
        }

        private void DebugLogRuntimeBlock(int now, string reason, bool allowFastTransitionRuntime, string fastReason)
        {
            if (!DebugLogEnabled)
                return;

            string signature = (reason ?? "null") + "|" + allowFastTransitionRuntime + "|" + (fastReason ?? "null");
            if (signature == _lastRuntimeBlockSignature && !TickReached(now, _nextRuntimeBlockDebugTick))
                return;

            _lastRuntimeBlockSignature = signature;
            _nextRuntimeBlockDebugTick = now + 500;

            DebugLogState("runtime-block", "reason=" + (reason ?? "null") + "; fastAllowed=" + allowFastTransitionRuntime + "; fastReason=" + (fastReason ?? "null"), null, null);
        }

        private void DebugLogBuildStateIfChanged(string phase)
        {
            if (!DebugLogEnabled)
                return;

            string signature = "strafeEquipped=" + _strafeEquipped
                + "; strafeSkill=" + (_skillStrafe != null)
                + "; strafeKey=" + GetSkillKeyText(_skillStrafe)
                + "; primarySkill=" + (_skillPrimary != null)
                + "; primaryKey=" + GetSkillKeyText(_skillPrimary)
                + "; primarySno=" + _primarySno
                + "; set=" + _setItemCount
                + "; cachedStrafe=" + _cachedStrafeActionKey
                + "; cachedPrimary=" + _cachedPrimaryActionKey
                + "; cachedPrimarySno=" + _cachedPrimarySno
                + "; cachedSet=" + _cachedSetItemCount
                + "; cachedMs=" + RemainingMs(_cachedSkillValidUntilTick);

            if (signature == _lastBuildStateSignature)
                return;

            _lastBuildStateSignature = signature;
            DebugLog("DIAG " + phase + ": " + signature);
        }

        private string GetSkillKeyText(IPlayerSkill skill)
        {
            if (skill == null)
                return "null";

            try { return skill.Key.ToString(); }
            catch { return "?"; }
        }

        private void DebugLogState(string phase, string reason, bool? newGame, ISnoArea area)
        {
            if (!DebugLogEnabled)
                return;

            try
            {
                int now = Environment.TickCount;
                var sb = new System.Text.StringBuilder(512);
                sb.Append("DIAG ").Append(phase);

                if (newGame.HasValue)
                    sb.Append(" newGame=").Append(newGame.Value);

                if (!string.IsNullOrEmpty(reason))
                    sb.Append(" reason=").Append(reason);

                sb.Append(" | running=").Append(_running)
                    .Append(" temp=").Append(_temporarilyPaused)
                    .Append(" status=").Append(_lastStatus ?? "null")
                    .Append(" high=").Append(_highFrequencyMode)
                    .Append(" strafeHeld=").Append(_strafeHeld)
                    .Append(" heldStrafe=").Append(_heldStrafeActionKey)
                    .Append(" pendingPrimary=").Append(_pendingPrimaryActionKey)
                    .Append(" pendingStartMs=").Append(RemainingMs(_pendingStartUntilTick))
                    .Append(" fastMs=").Append(RemainingMs(_fastTransitionStartUntilTick))
                    .Append(" cachedMs=").Append(RemainingMs(_cachedSkillValidUntilTick))
                    .Append(" townStage=").Append(_townPortalStage);

                sb.Append(" | build strafeEquipped=").Append(_strafeEquipped)
                    .Append(" strafeSkill=").Append(_skillStrafe != null)
                    .Append(" strafeKey=").Append(GetSkillKeyText(_skillStrafe))
                    .Append(" primarySkill=").Append(_skillPrimary != null)
                    .Append(" primaryKey=").Append(GetSkillKeyText(_skillPrimary))
                    .Append(" primarySno=").Append(_primarySno)
                    .Append(" set=").Append(_setItemCount)
                    .Append(" cachedStrafe=").Append(_cachedStrafeActionKey)
                    .Append(" cachedPrimary=").Append(_cachedPrimaryActionKey)
                    .Append(" cachedSet=").Append(_cachedSetItemCount);

                sb.Append(" | game isInGame=").Append(SafeBool("IsInGame"))
                    .Append(" loading=").Append(SafeBool("IsLoading"))
                    .Append(" paused=").Append(SafeBool("IsPaused"))
                    .Append(" town=").Append(SafeBool("IsInTown"))
                    .Append(" foreground=").Append(SafeWindowForeground())
                    .Append(" meNull=").Append(SafeMeNull())
                    .Append(" dead=").Append(SafeMeDead())
                    .Append(" anim=").Append(SafeMeAnimationState())
                    .Append(" chat=").Append(IsVisible(_chatEditLine))
                    .Append(" inv=").Append(SafeInventoryVisible())
                    .Append(" worldMap=").Append(SafeWorldMapVisible())
                    .Append(" minimapVisible=").Append(SafeMinimapVisible())
                    .Append(" cursorInWindow=").Append(SafeCursorInsideGameWindow());

                if (area != null)
                    sb.Append(" | area=").Append(SafeAreaText(area));

                DebugLog(sb.ToString());
            }
            catch
            {
                DebugLog("DIAG " + phase + " state unavailable");
            }
        }

        private int RemainingMs(int untilTick)
        {
            if (untilTick == 0)
                return 0;

            int remain = unchecked(untilTick - Environment.TickCount);
            return remain > 0 ? remain : 0;
        }

        private string SafeBool(string propertyName)
        {
            try
            {
                if (Hud == null || Hud.Game == null)
                    return "?";

                var prop = Hud.Game.GetType().GetProperty(propertyName);
                if (prop == null)
                    return "?";

                object value = prop.GetValue(Hud.Game, null);
                return value == null ? "null" : value.ToString();
            }
            catch { return "?"; }
        }

        private string SafeWindowForeground()
        {
            try { return Hud != null && Hud.Window != null ? Hud.Window.IsForeground.ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeMeNull()
        {
            try { return Hud == null || Hud.Game == null || Hud.Game.Me == null ? "True" : "False"; }
            catch { return "?"; }
        }

        private string SafeMeDead()
        {
            try { return Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.IsDead.ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeMeAnimationState()
        {
            try { return Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.AnimationState.ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeInventoryVisible()
        {
            try { return Hud != null && Hud.Inventory != null ? IsVisible(Hud.Inventory.InventoryMainUiElement).ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeWorldMapVisible()
        {
            try { return Hud != null && Hud.Render != null ? IsVisible(Hud.Render.WorldMapUiElement).ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeMinimapVisible()
        {
            try { return Hud != null && Hud.Render != null ? IsVisible(Hud.Render.MinimapUiElement).ToString() : "?"; }
            catch { return "?"; }
        }

        private string SafeCursorInsideGameWindow()
        {
            try { return CursorInsideGameWindow().ToString(); }
            catch { return "?"; }
        }

        private string SafeAreaText(ISnoArea area)
        {
            if (area == null)
                return "null";

            try
            {
                return "type=" + area.GetType().Name
                    + "; str=" + SafeObjectText(area)
                    + "; sno=" + SafePropertyText(area, "Sno")
                    + "; code=" + SafePropertyText(area, "Code")
                    + "; name=" + SafePropertyText(area, "NameLocalized")
                    + "; name2=" + SafePropertyText(area, "Name");
            }
            catch { return "unavailable"; }
        }

        private string SafeObjectText(object value)
        {
            try { return value == null ? "null" : value.ToString(); }
            catch { return "?"; }
        }

        private string SafePropertyText(object value, string propertyName)
        {
            try
            {
                if (value == null)
                    return "null";

                var prop = value.GetType().GetProperty(propertyName);
                if (prop == null)
                    return "missing";

                object propValue = prop.GetValue(value, null);
                return propValue == null ? "null" : propValue.ToString();
            }
            catch { return "?"; }
        }

        private void DebugLog(string message)
        {
            if (!DebugLogEnabled || Hud == null || Hud.TextLog == null)
                return;

            try { Hud.TextLog.Log("s7o_DHStrafePrimary", message, true, true); }
            catch { }
        }
    }

    internal static class s7o_DHStrafePrimaryInput
    {
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;

        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseRightDown = 0x0008;
        private const uint MouseRightUp = 0x0010;
        private const uint KeyboardKeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

        public static bool IsVirtualKeyDown(ushort virtualKey)
        {
            if (virtualKey == 0)
                return false;

            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        public static bool KeyDown(ushort virtualKey)
        {
            return SendKey(virtualKey, false);
        }

        public static bool KeyUp(ushort virtualKey)
        {
            return SendKey(virtualKey, true);
        }

        public static bool MouseDownLeft()
        {
            return SendMouse(MouseLeftDown);
        }

        public static bool MouseUpLeft()
        {
            return SendMouse(MouseLeftUp);
        }

        public static bool MouseDownRight()
        {
            return SendMouse(MouseRightDown);
        }

        public static bool MouseUpRight()
        {
            return SendMouse(MouseRightUp);
        }

        private static bool SendKey(ushort virtualKey, bool keyUp)
        {
            if (virtualKey == 0)
                return false;

            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.ScanCode = 0;
            input[0].U.Keyboard.Flags = keyUp ? KeyboardKeyUp : 0;
            input[0].U.Keyboard.Time = 0;
            input[0].U.Keyboard.ExtraInfo = IntPtr.Zero;

            return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
        }

        private static bool SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Dx = 0;
            input[0].U.Mouse.Dy = 0;
            input[0].U.Mouse.MouseData = 0;
            input[0].U.Mouse.Flags = flags;
            input[0].U.Mouse.Time = 0;
            input[0].U.Mouse.ExtraInfo = IntPtr.Zero;

            return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
        }
    }

    public class s7o_DHStrafePrimaryCustomizer : BasePlugin, ICustomizer
    {
        public override void Load(IController hud)
        {
            base.Load(hud);
            Enabled = true;
        }

        public void Customize()
        {
            var p = Hud.GetPlugin<s7o_DHStrafePrimaryPlugin>();
            if (p == null)
                return;

            p.Enabled = true;

            p.ToggleHotkey = Key.F3;
            p.FireModeHotkey = Key.F2;
            p.TownPortalHotkey = Key.T;
            p.TownPortalPrePrimarySettleMs = 35;
            p.TownPortalAfterPrimarySettleMs = 120;
            p.TownPortalAttempts = 3;
            p.TownPortalKeyPressHoldMs = 35;
            p.TownPortalDetectCastMs = 180;
            p.TownPortalUseForceMoveReset = true;
            p.TownPortalForceMoveTapMs = 20;
            p.TownPortalBetweenAttemptsMs = 65;
            p.TownPortalCastingPollMs = 25;

            p.PrimaryNormalDelayMs = 35;
            p.PrimaryHighFrequencyDelayMs = 30;
            p.StrafeCheckDelayMs = 50;
            p.KeyPressHoldMs = 8;
            p.RecentMapBlockMs = 0;

            p.MomentumRefreshSeconds = 21.0;
            p.NoMonsterStackRefreshThreshold = 16;
            p.MomentumBuffSno = 484289;
            p.MomentumBuffIconIndex = 10;

            p.StrafeClickableActorBlockDistance = 8.0f;
            p.PrimaryClickableActorBlockDistance = 10.0f;

            p.StatusTextCenterXFrac = 0.50f;
            p.StatusTextYFrac = 0.58f;
            p.StatusTextYOffsetPx = 1.0f;
            p.StartRequestAfterTransitionMs = 2500;
            p.EnableFastTransitionStart = true;
            p.FastTransitionStartWindowMs = 4000;
            p.CachedSkillStartGraceMs = 4000;
            p.PauseWhileWindowsKeyHeld = true;
            p.StopOnForegroundLost = true;

            // If Diablo III keybinds were changed from defaults, edit these values.
            p.Skill1VirtualKey = 0x31;
            p.Skill2VirtualKey = 0x32;
            p.Skill3VirtualKey = 0x33;
            p.Skill4VirtualKey = 0x34;
            p.ForceStandstillVirtualKey = 0x10;
            p.ForceMoveVirtualKey = 0;
        }
    }
}
