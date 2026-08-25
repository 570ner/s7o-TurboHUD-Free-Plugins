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
    // Keep GoD Hungering Arrow Combat cadence independent from zDH Entangling Shot.
    // zDH Entangle is injected independently while Strafe remains held. Combat support casts and
    // direct primary pulses share one soft action clock, while native Momentum confirmation owns
    // the separate hard refresh deadline.
    public class s7o_DHStrafePrimaryPlugin : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler
    {
        private static bool _zdhMacroRunning;
        private static bool _zdhHighFrequencyMode;
        private static int _zdhLastPrimaryFireTick = int.MinValue;
        private static int _zdhLastMomentumRefreshTick = int.MinValue;
        private static double _zdhLastObservedMomentumTimeLeft = -1.0;
        private static bool _zdhPylonPauseActive;
        private static int _zdhMomentumStacks;
        private static int _zdhMomentumTargetStacks = 20;
        private static bool _zdhMomentumBuildActive;
        private static bool _zdhCombatPrimaryMaintenanceDue;
        private static bool _zdhCombatMomentumRefreshDue;
        private static bool _zdhCombatMomentumRefreshInputDue;
        private static bool _zdhPrimaryTransactionPending;
        private static uint _zdhPrimarySno;
        private static int _zdhLastCombatActionTick = int.MinValue;
        private static int _zdhMomentumDeadlineAnchorTick = int.MinValue;
        // At 20 stacks, a successful Entangle is visible as a native time-left rise even though
        // the count cannot increase. Keep the threshold above frame jitter and below that rise.
        private const double MomentumRefreshDetectRiseSeconds = 0.50;
        public static bool IsMacroRunningForZdh { get { return _zdhMacroRunning; } }
        public static bool IsHighFrequencyModeForZdh { get { return _zdhHighFrequencyMode; } }
        public static bool IsManualDebuffHoldActiveForZdh
        {
            get
            {
                return _zdhMacroRunning && _zdhHighFrequencyMode && IsEntanglingPrimaryForZdh
                    && s7o_ZDH_HelperState.Enabled
                    && s7o_DHStrafePrimaryInput.IsVirtualKeyDown(0x11); // VK_CONTROL
            }
        }
        public static int MomentumStacksForZdh { get { return _zdhMomentumStacks; } }
        public static int MomentumTargetStacksForZdh { get { return _zdhMomentumTargetStacks; } }
        // For Entangling Shot this means a real <20-stack recovery input window is open now.
        // Scheduler ownership must use IsCombatMomentumLaneReservedForZdh instead: a retry
        // cooldown is not permission for ordinary support. Helper's only exception is its
        // separately bounded Combat/new-pack opening before a preventive at-cap refresh.
        public static bool IsCombatPrimaryMaintenanceDueForZdh { get { return _zdhCombatPrimaryMaintenanceDue; } }
        // Routine at-cap refresh is separate from deficit recovery. The default three-second
        // trigger leaves retry margin before the roughly six-second first-stack decay point.
        // True only when the confirmed refresh is due and the bounded retry window is open now.
        public static bool IsCombatMomentumRefreshInputDueForZdh { get { return _zdhCombatMomentumRefreshInputDue; } }
        // Level-triggered Helper handoff. The claim remains asserted across every retry cooldown
        // until native Momentum proves success. Helper may defer an at-cap claim for its bounded
        // opening pipeline, but a <20 recovery and an in-flight input remain strict. The claim is
        // derived from authoritative state, so it cannot become a stale latch.
        public static bool IsCombatMomentumLaneReservedForZdh
        {
            get
            {
                return _zdhMacroRunning && _zdhHighFrequencyMode && IsEntanglingPrimaryForZdh
                    && !IsManualDebuffHoldActiveForZdh
                    && (_zdhMomentumStacks < Math.Max(1, _zdhMomentumTargetStacks)
                        || _zdhCombatMomentumRefreshDue);
            }
        }
        // Speed-mode ownership mirrors the actual rebuild threshold used by MaybeFirePrimary.
        // This prevents Helper from yielding at 19/20 when DHStrafe itself has no Primary work.
        public static bool IsSpeedMomentumBuildActiveForZdh
        {
            get
            {
                return _zdhMacroRunning && !_zdhHighFrequencyMode && IsEntanglingPrimaryForZdh
                    && _zdhMomentumBuildActive;
            }
        }
        public static bool IsPrimaryTransactionPendingForZdh { get { return _zdhPrimaryTransactionPending; } }
        public static bool IsEntanglingPrimaryForZdh { get { return _zdhPrimarySno == 361936; } }
        public static int PrimaryQuietAgeForZdh(int now)
        {
            return _zdhLastPrimaryFireTick == int.MinValue
                ? int.MaxValue
                : Math.Max(0, unchecked(now - _zdhLastPrimaryFireTick));
        }

        public static int MomentumRefreshAgeForZdh(int now)
        {
            int anchor = _zdhLastMomentumRefreshTick != int.MinValue
                ? _zdhLastMomentumRefreshTick : _zdhMomentumDeadlineAnchorTick;
            return anchor == int.MinValue
                ? int.MaxValue
                : Math.Max(0, unchecked(now - anchor));
        }

        public static int CombatActionQuietAgeForZdh(int now)
        {
            return _zdhLastCombatActionTick == int.MinValue
                ? int.MaxValue
                : Math.Max(0, unchecked(now - _zdhLastCombatActionTick));
        }

        // Completed normal support advances the shared Combat action clock. CTRL-held manual
        // Entangle uses Helper's separate filler clock and does not consume this lane.
        public static void NotifySupportActionCompletedForZdh(int now)
        {
            if (_zdhMacroRunning)
                _zdhLastCombatActionTick = now;
        }

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

        public bool HoldStrafeContinuously = true;

        // ── Timings ────────────────────────────────────────────────
        // Keep normal GoD Combat behavior independent from zDH support cadence.
        // Hungering Arrow / non-Entangling Combat uses the 140 ms GoD cadence.
        // zDH Entangling Shot uses an acknowledged short-pulse transaction. At cap, only the
        // confirmed-refresh deadline is authoritative so Strafe and support keep the lane between refreshes.
        public int PrimaryNormalDelayMs = 500;
        public int PrimaryCombatMaintenanceDelayMs = 140;
        public int StrafeCheckDelayMs = 50;
        public int KeyPressHoldMs = 8;
        // zDH Entangling Shot uses short acknowledged pulses rather than one long blind hold.
        // Short pulses plus a brief retry gap create clean input edges while the native attack
        // animation and Momentum buff remain the acceptance and success authorities.
        public int ZdhPrimaryShiftLeadMs = 16;
        public int ZdhPrimaryPulseHoldMs = 30;
        public int ZdhPrimaryPulseRetryGapMs = 40;
        public int ZdhPrimaryMomentumConfirmWaitMs = 240;
        public int ZdhPrimaryMaxPulseAttempts = 4;
        public int ZdhPrimaryTransactionMaxMs = 320;
        public int ZdhPrimaryFailedTransactionRetryMs = 80;
        public int ZdhMomentumConfirmedBuildGapMs = 80;
        public int ZdhMomentumMaintenanceIntervalMs = 3000;
        // Time-left is evidence only; it never controls the desired stack count or interval.
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
        public int MomentumTargetStacks = 20;
        public int MomentumSpeedRefreshStacks = 18;
        // MomentumTargetStacks is the stack cap. Combat mode preserves 20/20 so switching
        // to Speed mode never needs a dangerous pre-build before an emergency escape.
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
        public bool BlockPrimaryOnClickableActor = true;
        public bool PauseForAutoLootPickups = true;
        public int AutoLootPauseMs = 300;
        public bool PauseNearUnoperatedPylon = true;
        public float PylonPauseRange = 15f;
        public bool PauseNearPortal = true;
        public float PortalPauseRange = 15f;
        // Use a slightly wider Strafe pause than primary suppression so nearby
        // interactables remain comfortable to click while the macro is armed.
        public float StrafeClickableActorBlockDistance = 8.0f;
        public float PrimaryClickableActorBlockDistance = 10.0f;

        // ── UI ───────────────────────────────────────────────────
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
            BetweenAttempts,
            Casting
        }

        private TownPortalStage _townPortalStage = TownPortalStage.Idle;
        private int _townPortalNextTick;
        private int _townPortalAttempt;
        private ActionKey _townPortalPrimaryActionKey = ActionKey.Unknown;
        private bool _townPortalPrimaryStandstillHeld;

        private bool _running;
        private bool _highFrequencyMode;
        private bool _lastAreaWasRift;
        private bool _temporarilyPaused;
        private bool _zdhPortalPauseActive;
        private int _autoLootPauseUntilTick;
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
        private bool _manualStandstillOwned;

        private ActionKey _pendingPrimaryActionKey = ActionKey.Unknown;
        private int _pendingPrimaryUpTick;
        private bool _pendingPrimaryStandstillHeld;

        public enum ZdhPrimaryStage
        {
            Idle,
            ShiftLead,
            PressHold,
            RetryGap,
            AwaitMomentum
        }

        private ZdhPrimaryStage _zdhPrimaryStage = ZdhPrimaryStage.Idle;
        private ActionKey _zdhPrimaryActionKey = ActionKey.Unknown;
        private int _zdhPrimaryTransactionStartTick = int.MinValue;
        private int _zdhPrimaryStageDueTick = int.MinValue;
        private int _zdhPrimaryAcceptedTick = int.MinValue;
        private int _zdhPrimaryPulseAttempts;
        private bool _zdhPrimaryShiftOwned;

        private string _lastStatus = "ready";

        private IUiElement _chatEditLine;
        private IUiElement _bossBattleRequestBox;
        private IUiElement _bossBattleOpenBox;

        private IFont _statusFont;
        private IFont _runningFont;
        private IFont _highFont;


        public s7o_DHStrafePrimaryPlugin()
        {
            Enabled = true;
            // Helper plans and claims support work first; DHStrafe fills only a lane that remains
            // free in the same collection frame.
            Order = 21000;
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
            _lastAreaWasRift = IsRiftArea(Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.SnoArea : null)
                || IsCurrentRiftArea();
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            int now = Environment.TickCount;
            // SpecialArea can lag the new-area callback (and can still describe the previous
            // area while leaving a Rift). The SNO area code is delivered with OnNewArea itself,
            // so use the native generated-Rift area identity for the entry edge.
            bool currentAreaIsRift = IsRiftArea(area);
            bool enteringRift = currentAreaIsRift && !_lastAreaWasRift;
            _lastAreaWasRift = currentAreaIsRift;

            FinishPendingPrimaryPress(now, true);
            CancelTownPortalSequence(now, "new area");
            StopStrafeHold();
            ReleaseManualStandstill();
            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _zdhLastPrimaryFireTick = int.MinValue;
            _zdhLastMomentumRefreshTick = int.MinValue;
            _zdhLastCombatActionTick = now;
            _zdhMomentumDeadlineAnchorTick = now;
            _zdhLastObservedMomentumTimeLeft = -1.0;
            _zdhMomentumStacks = 0;
            _zdhMomentumBuildActive = false;
            _zdhCombatPrimaryMaintenanceDue = false;
            _zdhCombatMomentumRefreshDue = false;
            _zdhCombatMomentumRefreshInputDue = false;
            ResetZdhPrimaryTransactionState();
            _actMapRecentlyVisibleUntilTick = 0;
            _worldMapRecentlyVisibleUntilTick = 0;
            _nextBuildRefreshTick = 0;
            _autoLootPauseUntilTick = 0;
            _zdhPylonPauseActive = false;
            _zdhPortalPauseActive = false;

            // A fresh Rift/Greater Rift always begins in Speed mode so Momentum can be rebuilt
            // immediately. Ordinary floor transitions inside the same rift preserve F2 mode.
            if (newGame || enteringRift)
            {
                _highFrequencyMode = false;
                _zdhHighFrequencyMode = false;
            }

            if (newGame)
            {
                _running = false;
                _zdhMacroRunning = false;
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

        }

        public void ForceStopForDisable()
        {
            try { StopMacro("disabled"); }
            catch { }
        }

        public void StopForAutoLootUrshiHandoff()
        {
            try { StopMacro("Urshi"); }
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
                    // F2 is an explicit control handoff. Do not let a Primary/Shift transaction
                    // started under the previous mode delay emergency Speed movement or the next
                    // Combat opening. Only inputs owned by DHStrafe are released here.
                    FinishPendingPrimaryPress(now, true);
                    ReleaseManualStandstill();
                    _highFrequencyMode = !_highFrequencyMode;
                    _zdhHighFrequencyMode = _highFrequencyMode;
                    _zdhLastCombatActionTick = now;
                    _lastStatus = _highFrequencyMode ? "mode: combat" : "mode: speed";
                }

                return;
            }
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            // Returning to town definitively ends the current rift-entry context. Re-entering
            // any Rift/Greater Rift afterward must rebuild Momentum from Speed mode.
            if (Hud != null && Hud.Game != null && Hud.Game.IsInTown)
                _lastAreaWasRift = false;
            _zdhHighFrequencyMode = _highFrequencyMode;

            FinishPendingPrimaryPress(now, false);
            AdvanceTownPortalSequence(now);

            RefreshBuildStateIfNeeded(now, false);
            RefreshMomentumStateForZdh(now);
            TrackRecentlyVisibleMaps(now);
            _zdhPylonPauseActive = PauseNearUnoperatedPylon && IsUnoperatedPylonNearby(PylonPauseRange);
            _zdhPortalPauseActive = PauseNearPortal && IsPortalInteractionNearby(PortalPauseRange);

            ProcessPendingStartRequest(now);

            if (_running && PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                FinishPendingPrimaryPress(now, true);
                CancelTownPortalSequence(now, "windows key");
                StopStrafeHold();
                ReleaseManualStandstill();

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
            {
                ReleaseManualStandstill();
                return;
            }

            // Interaction zones are authoritative over both manual CTRL support and Helper
            // cast leases. Release every DHStrafe-owned input before the user clicks a pylon
            // or portal so synthetic Shift/Primary/Strafe cannot block the interaction.
            if (_zdhPylonPauseActive || _zdhPortalPauseActive)
            {
                FinishPendingPrimaryPress(now, true);
                StopStrafeHold();
                ReleaseManualStandstill();
                _temporarilyPaused = true;
                _lastStatus = _zdhPylonPauseActive ? "paused: pylon nearby" : "paused";
                return;
            }

            bool manualDebuffHold = IsManualDebuffHoldActiveForZdh;
            bool helperPauseRequested = s7o_ZDH_Helper.IsDhStrafePauseRequested(now);
            if (manualDebuffHold)
            {
                FinishPendingPrimaryPress(now, true);
                StopStrafeHold();
                EnsureManualStandstill();
            }
            else if (!helperPauseRequested)
            {
                ReleaseManualStandstill();
            }

            if (helperPauseRequested)
            {
                FinishPendingPrimaryPress(now, true);
                StopStrafeHold();
                s7o_ZDH_Helper.ConfirmDhStrafePaused(now);
                _temporarilyPaused = true;
                _lastStatus = "paused: ZDH cast";
                return;
            }

            if (_autoLootPauseUntilTick != 0 && TickReached(now, _autoLootPauseUntilTick))
                _autoLootPauseUntilTick = 0;

            if (TickNotExpired(now, _autoLootPauseUntilTick))
            {
                FinishPendingPrimaryPress(now, true);
                StopStrafeHold();
                _temporarilyPaused = true;
                _lastStatus = "paused";
                return;
            }

            string reason;
            if (!IsValidRuntimeContext(out reason))
            {
                string fastReason = null;
                bool allowFastTransitionRuntime = IsFastTransitionStartWindowActive(now)
                    && CanFastStartAfterTransition(out fastReason);

                if (!allowFastTransitionRuntime)
                {
                    if (reason == "dead")
                    {
                        // Release owned Primary/Shift input before clearing transaction state.
                        // Resetting first could forget Shift ownership and leave standstill held
                        // across death/revive. Rebuild is allowed immediately after revival.
                        FinishPendingPrimaryPress(now, true);
                        _nextPrimaryFireTick = 0;
                        _zdhMomentumStacks = 0;
                        _zdhMomentumBuildActive = true;
                        _zdhCombatPrimaryMaintenanceDue = false;
                        _zdhCombatMomentumRefreshDue = false;
                        _zdhCombatMomentumRefreshInputDue = false;
                        _zdhLastMomentumRefreshTick = int.MinValue;
                        _zdhLastCombatActionTick = now;
                        _zdhMomentumDeadlineAnchorTick = now;
                        _zdhLastObservedMomentumTimeLeft = -1.0;
                    }
                    else if (reason == "casting/ghosted")
                    {
                        // RefreshMomentumStateForZdh() already sampled the real buff before this
                        // runtime gate. Preserve that sample through transient Transform/cast frames
                        // instead of publishing a false 0/20 deficit to ZDH Helper.
                        _nextPrimaryFireTick = 0;
                    }

                    // Transient UI/foreground blockers release Strafe,
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

            // Context is valid and no pause condition is active.
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
                }
                else
                {
                    StopMacro(buildStopReason);
                    return;
                }
            }

            if (manualDebuffHold)
            {
                _temporarilyPaused = false;
                return;
            }

            if (s7o_ZDH_Helper.IsManualDebuffMovementRequested(now))
            {
                FinishPendingPrimaryPress(now, true);
                MaintainStrafe(now);
                return;
            }

            // Keep one acknowledged zDH Primary transaction atomic from Helper. Rapid retry
            // pulses occur only inside this bounded state machine; once native Momentum confirms
            // or the transaction fails, the normal support scheduler immediately regains access.
            if (_zdhPrimaryTransactionPending)
            {
                AdvanceZdhPrimaryTransaction(now);
                if (_zdhPrimaryTransactionPending) return;
            }

            MaintainStrafe(now);
            if (s7o_ZDH_Helper.IsDhStrafePrimarySuppressed(now))
            {
                FinishPendingPrimaryPress(now, true);
                return;
            }
            MaybeFirePrimary(now);
        }

        public void PauseForAutoLootPickup()
        {
            if (!Enabled || !_running || !PauseForAutoLootPickups)
                return;

            int now = Environment.TickCount;
            _autoLootPauseUntilTick = unchecked(now + Math.Max(50, Math.Min(1000, AutoLootPauseMs)));
            FinishPendingPrimaryPress(now, true);
            StopStrafeHold();
            _temporarilyPaused = true;
            _lastStatus = "paused";
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

            if (RequireStrafeEquipped && !HasEffectiveStrafe())
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
                    text = "Combat: " + FireModeHotkey + " = Speed | " + ToggleHotkey + " = Stop";
                    font = _highFont;
                }
                else if (GetEffectiveSetItemCount() >= 4)
                {
                    text = "Speed: " + FireModeHotkey + " = Combat | " + ToggleHotkey + " = Stop";
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

            text = s7o_Localization.Display(text);
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
            int now = Environment.TickCount;
            RefreshBuildStateIfNeeded(now, true);

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

                return false;
            }

            _pendingStartUntilTick = 0;
            _lastStartBlockedReason = string.Empty;

            // Defensive lifecycle boundary: a new start must never inherit an owned Primary,
            // synthetic standstill, or stale Strafe hold from an interrupted prior state.
            FinishPendingPrimaryPress(now, true);
            StopStrafeHold();

            _running = true;
            _zdhMacroRunning = true;
            _zdhHighFrequencyMode = _highFrequencyMode;
            _temporarilyPaused = false;
            _autoLootPauseUntilTick = 0;
            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _zdhLastPrimaryFireTick = int.MinValue;
            _zdhLastMomentumRefreshTick = int.MinValue;
            _zdhLastCombatActionTick = now;
            _zdhMomentumDeadlineAnchorTick = now;
            _zdhLastObservedMomentumTimeLeft = -1.0;
            _zdhCombatPrimaryMaintenanceDue = false;
            _zdhCombatMomentumRefreshDue = false;
            _zdhCombatMomentumRefreshInputDue = false;
            ResetZdhPrimaryTransactionState();
            _lastStatus = GetEffectiveSetItemCount() >= 4
                ? (_highFrequencyMode ? "running fast attack" : "running movement")
                : "running strafe only";

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

        }

        private void ProcessPendingStartRequest(int now)
        {
            if (_pendingStartUntilTick == 0)
                return;

            if (TickReached(now, _pendingStartUntilTick))
            {
                _pendingStartUntilTick = 0;
                _lastStatus = "ready";
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

            CancelTownPortalSequence(now, "stop macro");
            FinishPendingPrimaryPress(now, true);
            StopStrafeHold();
            ReleaseManualStandstill();

            _running = false;
            _zdhMacroRunning = false;
            _temporarilyPaused = false;
            _autoLootPauseUntilTick = 0;
            _pendingStartUntilTick = 0;
            _lastStartBlockedReason = string.Empty;
            _nextStrafeCheckTick = 0;
            _nextPrimaryFireTick = 0;
            _lastPrimaryFireTick = 0;
            _zdhLastPrimaryFireTick = int.MinValue;
            _zdhLastMomentumRefreshTick = int.MinValue;
            _zdhLastCombatActionTick = int.MinValue;
            _zdhMomentumDeadlineAnchorTick = int.MinValue;
            _zdhLastObservedMomentumTimeLeft = -1.0;
            _zdhMomentumStacks = 0;
            _zdhMomentumBuildActive = false;
            _zdhCombatPrimaryMaintenanceDue = false;
            _zdhCombatMomentumRefreshDue = false;
            _zdhCombatMomentumRefreshInputDue = false;
            ResetZdhPrimaryTransactionState();
            _actMapRecentlyVisibleUntilTick = 0;
            _worldMapRecentlyVisibleUntilTick = 0;
            _zdhPylonPauseActive = false;
            _zdhPortalPauseActive = false;
            _lastStatus = string.IsNullOrEmpty(reason) ? "stopped" : reason;

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

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
                return;

            var powers = Hud.Game.Me.Powers;
            var dh = powers.UsedDemonHunterPowers;
            IPlayerSkill nextStrafe = dh == null ? null : dh.Strafe;
            IPlayerSkill nextPrimary = null;
            uint nextPrimarySno = 0;
            bool nextStrafeEquipped = false;
            int validSkillCount = 0;

            foreach (var skill in powers.UsedSkills)
            {
                if (skill == null || skill.SnoPower == null)
                    continue;

                validSkillCount++;
                uint sno = skill.SnoPower.Sno;

                if (sno == 134030)
                    nextStrafeEquipped = true;

                switch (sno)
                {
                    case 129215:
                        nextPrimary = dh == null ? null : dh.HungeringArrow;
                        nextPrimarySno = 129215;
                        break;
                    case 361936:
                        nextPrimary = dh == null ? null : dh.EntanglingShot;
                        nextPrimarySno = 361936;
                        break;
                    case 77552:
                        nextPrimary = dh == null ? null : dh.Bolas;
                        nextPrimarySno = 77552;
                        break;
                    case 377450:
                        nextPrimary = dh == null ? null : dh.EvasiveFire;
                        nextPrimarySno = 377450;
                        break;
                    case 86610:
                        nextPrimary = dh == null ? null : dh.Grenades;
                        nextPrimarySno = 86610;
                        break;
                }
            }

            int now = Environment.TickCount;

            // FreeHUD can briefly publish an empty UsedSkills collection during transitions.
            // Preserve the last good live snapshot while its bounded cache is still valid rather
            // than publishing a false "Strafe not equipped" state to F3/start/status logic.
            if (validSkillCount == 0 && IsCachedSkillValid(now))
                return;

            int nextSetItemCount;
            try { nextSetItemCount = Hud.Game.Me.GetSetItemCount(791249); }
            catch { nextSetItemCount = 0; }

            _skillStrafe = nextStrafe;
            _skillPrimary = nextPrimary;
            _primarySno = nextPrimarySno;
            _setItemCount = nextSetItemCount;
            _strafeEquipped = nextStrafeEquipped;

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

        }

        private bool ShouldStopForBuildChange(out string reason)
        {
            reason = null;

            if (RequireDemonHunter && (Hud.Game.Me.HeroClassDefinition == null || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter))
            {
                reason = "not Demon Hunter";
                return true;
            }

            if (RequireStrafeEquipped && !HasEffectiveStrafe())
            {
                reason = "Strafe not equipped";
                return true;
            }

            if (GetEffectiveSetItemCount() >= 4 && !HasEffectivePrimary())
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

            if (RequireStrafeEquipped && !HasEffectiveStrafe())
            {
                reason = "Strafe not equipped";
                return false;
            }

            if (GetEffectiveSetItemCount() >= 4 && !HasEffectivePrimary())
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

        private bool HasEffectiveStrafe()
        {
            return GetStrafeActionKey() != ActionKey.Unknown;
        }

        private bool HasEffectivePrimary()
        {
            return GetPrimaryActionKey() != ActionKey.Unknown;
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

            if (Hud.Game.Me.Powers != null
                && Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno))
            {
                reason = "casting/ghosted";
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
            ReleaseManualStandstill();
            var strafeKey = GetStrafeActionKey();

            if (strafeKey == ActionKey.Unknown)
                return;

            if (SendActionDown(strafeKey))
            {
                _heldStrafeActionKey = strafeKey;
                _strafeHeld = true;

                // Left-skill Strafe must not inherit standstill from a preceding input.
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

        private void RefreshMomentumStateForZdh(int now)
        {
            _zdhPrimarySno = GetEffectivePrimarySno();
            int target = Math.Max(1, MomentumTargetStacks);
            int refresh = Math.Max(1, Math.Min(target, MomentumSpeedRefreshStacks));
            int previousStacks = Math.Max(0, _zdhMomentumStacks);
            int stacks;
            double timeLeft;
            GetMomentumSample(out stacks, out timeLeft);
            bool entanglingPrimary = IsEntanglingPrimaryForZdh;

            // Stack count is the policy authority. A first sample after start/area/reset is only
            // observation; it cannot prove a refresh. Time-left is used only as native confirmation
            // that a later Entangle really refreshed Momentum while the visible count stayed at 20.
            bool havePreviousMomentumSample = _zdhLastObservedMomentumTimeLeft >= 0.0;
            bool stackIncreased = havePreviousMomentumSample && stacks > previousStacks;
            bool timerRefreshed = entanglingPrimary
                && havePreviousMomentumSample
                && timeLeft > 0.0
                && timeLeft >= _zdhLastObservedMomentumTimeLeft
                    + MomentumRefreshDetectRiseSeconds;
            if (_running && entanglingPrimary && (stackIncreased || timerRefreshed))
            {
                _zdhLastMomentumRefreshTick = now;
                _zdhMomentumDeadlineAnchorTick = now;
            }

            if (_running && _zdhMomentumDeadlineAnchorTick == int.MinValue)
                _zdhMomentumDeadlineAnchorTick = now;

            _zdhLastObservedMomentumTimeLeft = timeLeft;

            bool combatBuild = _running && _highFrequencyMode && stacks < target;
            bool speedBuild = _running && !_highFrequencyMode && stacks <= refresh;
            bool inputWindowReady = TickReached(now, _nextPrimaryFireTick);

            // "Refresh due" remains visible until native Momentum proves success. Input-due is
            // narrower and reports only the bounded retry cadence. Outside Helper's bounded
            // opening exception, ordinary support must not consume these retry gaps.
            bool atCapRefreshDue = _running && _highFrequencyMode && entanglingPrimary
                && stacks >= target
                && MomentumRefreshAgeForZdh(now) >= Math.Max(1000, ZdhMomentumMaintenanceIntervalMs);

            _zdhMomentumStacks = Math.Max(0, stacks);
            _zdhMomentumTargetStacks = target;
            _zdhMomentumBuildActive = combatBuild || speedBuild;
            _zdhCombatPrimaryMaintenanceDue = _running && _highFrequencyMode && inputWindowReady
                && (entanglingPrimary
                    ? combatBuild
                    : !combatBuild && _zdhLastPrimaryFireTick != int.MinValue);
            _zdhCombatMomentumRefreshDue = atCapRefreshDue;
            _zdhCombatMomentumRefreshInputDue = atCapRefreshDue && inputWindowReady;
        }

        private void GetMomentumSample(out int stacks, out double timeLeft)
        {
            stacks = 0;
            timeLeft = 0.0;
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                return;

            var buff = Hud.Game.Me.Powers.GetBuff(MomentumBuffSno);
            if (buff == null) return;
            int index = MomentumBuffIconIndex;
            if (buff.IconCounts != null && index >= 0 && index < buff.IconCounts.Length)
                stacks = Math.Max(0, buff.IconCounts[index]);
            if (buff.TimeLeftSeconds != null && index >= 0 && index < buff.TimeLeftSeconds.Length)
                timeLeft = Math.Max(0.0, buff.TimeLeftSeconds[index]);
        }

        private int GetCombatPrimaryMaintenanceDelayMs()
        {
            return Math.Max(5, PrimaryCombatMaintenanceDelayMs);
        }

        private void MaybeFirePrimary(int now)
        {
            var primaryKey = GetPrimaryActionKey();

            if (primaryKey == ActionKey.Unknown)
            {
                return;
            }

            if (_pendingPrimaryActionKey != ActionKey.Unknown || _zdhPrimaryTransactionPending)
            {
                return;
            }

            if (RequireGoD4ForPrimary && GetEffectiveSetItemCount() < 4)
            {
                return;
            }

            int momentumTarget = Math.Max(1, _zdhMomentumTargetStacks);
            int momentumRefresh = Math.Max(1, Math.Min(momentumTarget, MomentumSpeedRefreshStacks));
            int momentumStacks = Math.Max(0, _zdhMomentumStacks);
            bool entanglingPrimary = IsEntanglingPrimaryForZdh;
            bool combatMomentumBuild = _highFrequencyMode && momentumStacks < momentumTarget;
            bool combatMomentumRefresh = _highFrequencyMode && entanglingPrimary
                && !combatMomentumBuild && _zdhCombatMomentumRefreshDue;
            bool speedMomentumBuild = !_highFrequencyMode && momentumStacks <= momentumRefresh;
            bool speedRefresh = !_highFrequencyMode && momentumStacks <= momentumRefresh;

            if (_highFrequencyMode && entanglingPrimary && !combatMomentumBuild
                && !combatMomentumRefresh)
            {
                _nextPrimaryFireTick = now + 10;
                return;
            }

            if (!_highFrequencyMode && !speedRefresh)
            {
                _nextPrimaryFireTick = now + 10;
                return;
            }

            if (!TickReached(now, _nextPrimaryFireTick))
            {
                return;
            }

            if (entanglingPrimary && !IsEntanglingPrimaryLaunchReady())
            {
                return;
            }

            if (BlockPrimaryOnClickableActor && IsHoverValidActor(PrimaryClickableActorBlockDistance))
            {
                _nextPrimaryFireTick = now + Math.Max(10, ZdhPrimaryFailedTransactionRetryMs);
                return;
            }

            if (entanglingPrimary)
            {
                if (BeginZdhPrimaryTransaction(primaryKey, now))
                {
                    _zdhCombatPrimaryMaintenanceDue = false;
                    _zdhCombatMomentumRefreshInputDue = false;
                }
                else
                {
                    _nextPrimaryFireTick = now + Math.Max(10, ZdhPrimaryFailedTransactionRetryMs);
                }
                return;
            }

            int delay = Math.Max(5, _highFrequencyMode
                ? GetCombatPrimaryMaintenanceDelayMs()
                : PrimaryNormalDelayMs);
            if (DoActionAutoShift(primaryKey, now))
            {
                _lastPrimaryFireTick = now;
                _zdhLastPrimaryFireTick = now;
                _nextPrimaryFireTick = now + delay;
            }
            else
            {
                _nextPrimaryFireTick = now + 50;
            }
        }

        private bool BeginZdhPrimaryTransaction(ActionKey actionKey, int now)
        {
            if (actionKey == ActionKey.Unknown || _zdhPrimaryTransactionPending
                || _pendingPrimaryActionKey != ActionKey.Unknown
                || IsActionPhysicallyDown(actionKey))
                return false;

            if (_strafeHeld && actionKey == _heldStrafeActionKey)
                return false;

            _zdhPrimaryTransactionPending = true;
            _zdhPrimaryStage = ZdhPrimaryStage.ShiftLead;
            _zdhPrimaryActionKey = actionKey;
            _zdhPrimaryTransactionStartTick = now;
            _zdhPrimaryStageDueTick = unchecked(now + Math.Max(0, ZdhPrimaryShiftLeadMs));
            _zdhPrimaryAcceptedTick = int.MinValue;
            _zdhPrimaryPulseAttempts = 0;
            _zdhPrimaryShiftOwned = false;

            if (!EnsureZdhPrimaryShiftDown())
            {
                ResetZdhPrimaryTransactionState();
                return false;
            }

            if (Math.Max(0, ZdhPrimaryShiftLeadMs) == 0)
            {
                if (StartZdhPrimaryPulse(now)) return true;
                CompleteZdhPrimaryTransaction(now, false);
                return false;
            }
            return true;
        }

        private void AdvanceZdhPrimaryTransaction(int now)
        {
            if (!_zdhPrimaryTransactionPending)
                return;

            // Catch a pulse whose short hold expired after the first release pass this frame.
            FinishPendingPrimaryPress(now, false);
            if (!_zdhPrimaryTransactionPending)
                return;

            if (_zdhLastMomentumRefreshTick != int.MinValue
                && _zdhPrimaryTransactionStartTick != int.MinValue
                && unchecked(_zdhLastMomentumRefreshTick - _zdhPrimaryTransactionStartTick) >= 0)
            {
                CompleteZdhPrimaryTransaction(now, true);
                return;
            }

            bool freshAttack = Hud != null && Hud.Game != null && Hud.Game.Me != null
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking;
            if (freshAttack && _zdhPrimaryPulseAttempts > 0
                && _zdhPrimaryAcceptedTick == int.MinValue)
            {
                _zdhPrimaryAcceptedTick = now;
                _zdhPrimaryStage = ZdhPrimaryStage.AwaitMomentum;
                _zdhPrimaryStageDueTick = unchecked(now
                    + Math.Max(120, ZdhPrimaryMomentumConfirmWaitMs));
                // Keep the accepted pulse down only until its normal short hold expires. If the
                // attack was observed after pulse-up, release our standstill ownership now.
                if (_pendingPrimaryActionKey == ActionKey.Unknown)
                    ReleaseZdhPrimaryShift();
                return;
            }

            if (_zdhPrimaryAcceptedTick == int.MinValue
                && _zdhPrimaryTransactionStartTick != int.MinValue
                && Elapsed(_zdhPrimaryTransactionStartTick, now)
                    >= Math.Max(120, ZdhPrimaryTransactionMaxMs))
            {
                CompleteZdhPrimaryTransaction(now, false);
                return;
            }

            switch (_zdhPrimaryStage)
            {
                case ZdhPrimaryStage.ShiftLead:
                    if (!TickReached(now, _zdhPrimaryStageDueTick)) return;
                    if (!IsEntanglingPrimaryLaunchReady())
                    {
                        return;
                    }
                    if (!StartZdhPrimaryPulse(now))
                        CompleteZdhPrimaryTransaction(now, false);
                    return;

                case ZdhPrimaryStage.PressHold:
                    return;

                case ZdhPrimaryStage.RetryGap:
                    if (!TickReached(now, _zdhPrimaryStageDueTick)) return;
                    if (_zdhPrimaryPulseAttempts >= Math.Max(1, ZdhPrimaryMaxPulseAttempts))
                    {
                        CompleteZdhPrimaryTransaction(now, false);
                        return;
                    }
                    if (!IsEntanglingPrimaryLaunchReady())
                    {
                        return;
                    }
                    if (!StartZdhPrimaryPulse(now))
                        CompleteZdhPrimaryTransaction(now, false);
                    return;

                case ZdhPrimaryStage.AwaitMomentum:
                    if (TickReached(now, _zdhPrimaryStageDueTick))
                        CompleteZdhPrimaryTransaction(now, false);
                    return;
            }
        }

        private bool StartZdhPrimaryPulse(int now)
        {
            if (_zdhPrimaryActionKey == ActionKey.Unknown
                || _pendingPrimaryActionKey != ActionKey.Unknown
                || !EnsureZdhPrimaryShiftDown())
                return false;

            _zdhPrimaryPulseAttempts++;
            if (!SendActionDown(_zdhPrimaryActionKey))
            {
                return false;
            }

            _pendingPrimaryActionKey = _zdhPrimaryActionKey;
            _pendingPrimaryUpTick = unchecked(now + Math.Max(8, ZdhPrimaryPulseHoldMs));
            _zdhPrimaryStage = ZdhPrimaryStage.PressHold;
            _zdhPrimaryStageDueTick = _pendingPrimaryUpTick;
            _lastPrimaryFireTick = now;
            _zdhLastPrimaryFireTick = now;
            if (_highFrequencyMode)
                _zdhLastCombatActionTick = now;
            return true;
        }

        private bool EnsureZdhPrimaryShiftDown()
        {
            if (ForceStandstillVirtualKey == 0
                || s7o_DHStrafePrimaryInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                return true;
            if (!s7o_DHStrafePrimaryInput.KeyDown(ForceStandstillVirtualKey))
                return false;
            _zdhPrimaryShiftOwned = true;
            return true;
        }

        private void ReleaseZdhPrimaryShift()
        {
            if (_zdhPrimaryShiftOwned && ForceStandstillVirtualKey != 0)
                s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
            _zdhPrimaryShiftOwned = false;
        }

        private void ReleaseZdhPrimaryPhysicalInputs()
        {
            if (_pendingPrimaryActionKey != ActionKey.Unknown)
            {
                SendActionUp(_pendingPrimaryActionKey);
                _pendingPrimaryActionKey = ActionKey.Unknown;
                _pendingPrimaryUpTick = 0;
            }
            ReleaseZdhPrimaryShift();
        }

        private void CompleteZdhPrimaryTransaction(int now, bool success)
        {
            ReleaseZdhPrimaryPhysicalInputs();
            ResetZdhPrimaryTransactionState();

            if (success)
            {
                int target = Math.Max(1, _zdhMomentumTargetStacks);
                _nextPrimaryFireTick = unchecked(now + (_zdhMomentumStacks < target
                    ? Math.Max(10, ZdhMomentumConfirmedBuildGapMs) : 10));
            }
            else
            {
                _nextPrimaryFireTick = unchecked(now
                    + Math.Max(10, ZdhPrimaryFailedTransactionRetryMs));
            }
        }

        private void ResetZdhPrimaryTransactionState()
        {
            _zdhPrimaryTransactionPending = false;
            _zdhPrimaryStage = ZdhPrimaryStage.Idle;
            _zdhPrimaryActionKey = ActionKey.Unknown;
            _zdhPrimaryTransactionStartTick = int.MinValue;
            _zdhPrimaryStageDueTick = int.MinValue;
            _zdhPrimaryAcceptedTick = int.MinValue;
            _zdhPrimaryPulseAttempts = 0;
            _zdhPrimaryShiftOwned = false;
        }

        private bool IsEntanglingPrimaryLaunchReady()
        {
            if (!_strafeHeld || !IsStrafeBuffActive()
                || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            AcdAnimationState animation = Hud.Game.Me.AnimationState;
            return animation != AcdAnimationState.Attacking
                && animation != AcdAnimationState.Casting
                && animation != AcdAnimationState.Transform
                && animation != AcdAnimationState.CastingPortal;
        }

        private void EnsureManualStandstill()
        {
            if (_manualStandstillOwned || ForceStandstillVirtualKey == 0) return;
            if (s7o_DHStrafePrimaryInput.IsVirtualKeyDown(ForceStandstillVirtualKey)) return;
            _manualStandstillOwned = s7o_DHStrafePrimaryInput.KeyDown(ForceStandstillVirtualKey);
        }

        private void ReleaseManualStandstill()
        {
            if (_manualStandstillOwned && ForceStandstillVirtualKey != 0)
                s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
            _manualStandstillOwned = false;
        }

        private bool IsUnoperatedPylonNearby(float range)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null
                    || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Shrines == null) return false;
                float limit = Math.Max(0, range);
                return Hud.Game.Shrines.Any(shrine => shrine != null && shrine.IsPylon
                    && !shrine.IsDisabled && !shrine.IsOperated && shrine.FloorCoordinate != null
                    && Hud.Game.Me.FloorCoordinate.XYDistanceTo(shrine.FloorCoordinate) <= limit);
            }
            catch { return false; }
        }

        private bool IsPortalInteractionNearby(float range)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null
                    || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Portals == null) return false;

                float limit = Math.Max(0, range);
                // Portal interaction safety is distance-first. ActorAvailable/IsClickable can
                // depend on the cursor already being over a narrow interaction hotspot, which is
                // precisely what autosnap can prevent. Hud.Game.Portals already identifies portal
                // actors, so proximity to their world coordinate is the reliable pause authority.
                return Hud.Game.Portals.Any(portal => portal != null && portal.FloorCoordinate != null
                    && Hud.Game.Me.FloorCoordinate.XYDistanceTo(portal.FloorCoordinate) <= limit);
            }
            catch { return false; }
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

        private bool IsActionPhysicallyDown(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(0x01);
                case ActionKey.RightSkill: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(0x02);
                case ActionKey.Skill1: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(Skill1VirtualKey);
                case ActionKey.Skill2: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(Skill2VirtualKey);
                case ActionKey.Skill3: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(Skill3VirtualKey);
                case ActionKey.Skill4: return s7o_DHStrafePrimaryInput.IsVirtualKeyDown(Skill4VirtualKey);
                default: return false;
            }
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
            if (actionKey == ActionKey.Unknown || _zdhPrimaryTransactionPending)
                return false;

            if (_pendingPrimaryActionKey != ActionKey.Unknown)
                return false;

            if (_strafeHeld && actionKey == _heldStrafeActionKey)
                return false;

            bool standstillDown = false;
            if (ForceStandstillVirtualKey != 0
                && !s7o_DHStrafePrimaryInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                standstillDown = s7o_DHStrafePrimaryInput.KeyDown(ForceStandstillVirtualKey);

            bool actionDown = SendActionDown(actionKey);
            if (!actionDown)
            {
                if (standstillDown && ForceStandstillVirtualKey != 0)
                    s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
                return false;
            }

            _pendingPrimaryActionKey = actionKey;
            _pendingPrimaryUpTick = unchecked(now + Math.Max(1, KeyPressHoldMs));
            _pendingPrimaryStandstillHeld = standstillDown;
            return true;
        }

        private void FinishPendingPrimaryPress(int now, bool force)
        {
            if (force && _zdhPrimaryTransactionPending)
            {
                ReleaseZdhPrimaryPhysicalInputs();
                ResetZdhPrimaryTransactionState();
                _nextPrimaryFireTick = unchecked(now
                    + Math.Max(10, ZdhPrimaryFailedTransactionRetryMs));
                return;
            }

            if (_pendingPrimaryActionKey == ActionKey.Unknown)
                return;

            if (!force && !TickReached(now, _pendingPrimaryUpTick))
                return;

            ActionKey releasedKey = _pendingPrimaryActionKey;
            SendActionUp(releasedKey);
            _pendingPrimaryActionKey = ActionKey.Unknown;
            _pendingPrimaryUpTick = 0;

            if (_zdhPrimaryTransactionPending)
            {
                if (_zdhPrimaryStage == ZdhPrimaryStage.PressHold)
                {
                    _zdhPrimaryStage = ZdhPrimaryStage.RetryGap;
                    _zdhPrimaryStageDueTick = unchecked(now
                        + Math.Max(8, ZdhPrimaryPulseRetryGapMs));
                }
                else if (_zdhPrimaryStage == ZdhPrimaryStage.AwaitMomentum)
                {
                    ReleaseZdhPrimaryShift();
                }
                return;
            }

            if (_pendingPrimaryStandstillHeld && ForceStandstillVirtualKey != 0)
                s7o_DHStrafePrimaryInput.KeyUp(ForceStandstillVirtualKey);
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

            _townPortalStage = TownPortalStage.PrePrimarySettle;
            _townPortalNextTick = now + Math.Max(0, TownPortalPrePrimarySettleMs);

            _lastStatus = "paused: town portal";
        }

        private void CancelTownPortalSequence(int now, string reason)
        {
            ReleaseTownPortalSequenceInputs();

            _townPortalStage = TownPortalStage.Idle;
            _townPortalNextTick = 0;
            _townPortalAttempt = 0;
            _townPortalPrimaryActionKey = ActionKey.Unknown;
            _townPortalPrimaryStandstillHeld = false;
        }

        private void ReleaseTownPortalSequenceInputs()
        {
            try { s7o_DHStrafePrimaryInput.KeyUp(GetTownPortalVirtualKey()); } catch { }

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

                    return;
                }

                case TownPortalStage.PortalKeyUp:
                {
                    ushort vk = GetTownPortalVirtualKey();
                    s7o_DHStrafePrimaryInput.KeyUp(vk);

                    _townPortalStage = TownPortalStage.DetectCast;
                    _townPortalNextTick = now + Math.Max(25, TownPortalDetectCastMs);

                    return;
                }

                case TownPortalStage.DetectCast:
                {
                    if (IsCastingTownPortal())
                    {
                        _townPortalStage = TownPortalStage.Casting;
                        _townPortalNextTick = now + Math.Max(10, TownPortalCastingPollMs);
                        return;
                    }

                    _townPortalStage = TownPortalStage.BetweenAttempts;
                    _townPortalNextTick = now + Math.Max(0, TownPortalBetweenAttemptsMs);
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

        private static bool IsRiftArea(ISnoArea area)
        {
            try
            {
                // Native generated Nephalem/Greater Rift floor area codes. Unlike
                // Hud.Game.SpecialArea, this identity belongs to the new OnNewArea payload and
                // therefore is not one transition behind. Follow HostSnoArea as a small safety
                // net for any child/sub-area hosted by the generated Rift floor.
                ISnoArea current = area;
                for (int depth = 0; current != null && depth < 3; depth++)
                {
                    string code = current.Code;
                    if (!string.IsNullOrEmpty(code)
                        && code.StartsWith("X1_LR_Level_", StringComparison.OrdinalIgnoreCase))
                        return true;

                    current = current.HostSnoArea;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsCurrentRiftArea()
        {
            return Hud != null && Hud.Game != null
                && (Hud.Game.SpecialArea == SpecialArea.Rift
                    || Hud.Game.SpecialArea == SpecialArea.GreaterRift);
        }

        private static bool TickReached(int now, int targetTick)
        {
            return targetTick == 0 || unchecked(now - targetTick) >= 0;
        }

        private static bool TickNotExpired(int now, int untilTick)
        {
            return untilTick != 0 && unchecked(untilTick - now) > 0;
        }

        private static int Elapsed(int then, int now)
        {
            return then == int.MinValue ? int.MaxValue : Math.Max(0, unchecked(now - then));
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
            p.TownPortalBetweenAttemptsMs = 65;
            p.TownPortalCastingPollMs = 25;

            p.PrimaryNormalDelayMs = 500;
            p.PrimaryCombatMaintenanceDelayMs = 140;
            p.StrafeCheckDelayMs = 50;
            p.KeyPressHoldMs = 8;
            p.RecentMapBlockMs = 0;

            p.MomentumTargetStacks = 20;
            p.MomentumSpeedRefreshStacks = 18;
            p.ZdhPrimaryShiftLeadMs = 16;
            p.ZdhPrimaryPulseHoldMs = 30;
            p.ZdhPrimaryPulseRetryGapMs = 40;
            p.ZdhPrimaryMomentumConfirmWaitMs = 240;
            p.ZdhPrimaryMaxPulseAttempts = 4;
            p.ZdhPrimaryTransactionMaxMs = 320;
            p.ZdhPrimaryFailedTransactionRetryMs = 80;
            p.ZdhMomentumConfirmedBuildGapMs = 80;
            p.ZdhMomentumMaintenanceIntervalMs = 3000;
            p.MomentumBuffSno = 484289;
            p.MomentumBuffIconIndex = 10;

            p.StrafeClickableActorBlockDistance = 8.0f;
            p.PrimaryClickableActorBlockDistance = 10.0f;
            p.PauseForAutoLootPickups = true;
            p.AutoLootPauseMs = 300;
            p.PauseNearUnoperatedPylon = true;
            p.PylonPauseRange = 15f;

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
        }
    }
}
