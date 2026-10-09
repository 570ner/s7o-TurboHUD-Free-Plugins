using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using MouseButtons = System.Windows.Forms.MouseButtons;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // v1.5.0: acknowledge cursor restore without treating a stale native sample as failure.
    // Optional Sanctified Shadow Strafe module.
    // Sanctified Strafe auto-casts the last non-channeled Hatred spender, so Impale
    // is primed/re-primed before Strafe. Both modes refresh Focus with the generator;
    // Combat additionally directs manual Impales through the same owned aim/restore job.
    // F3 ownership is Shadow 6-piece only and remains independent of DHStrafe state.
    // Speed refreshes Focus near expiry; Combat adds targeted Impale at a 500 ms cadence.
    public class s7o_Impale : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler, IMouseClickHandler, IS7oAutoLootInputHandoff
    {
        private const uint ShadowSetSno = 916931u;
        private const uint GoDSetSno = 791249u;
        private const uint StrafeSno = 134030u;
        private const uint ImpaleSno = 131366u;
        private const uint RapidFireSno = 131192u;
        private const uint BolasSno = 77552u;
        private const uint GrenadesSno = 86610u;
        private const uint FocusBuffSno = 359583u;
        private const int FocusBuffIconIndex = 1; // Same index as PlayerBottomBuffListPlugin.
        private const string Owner = "Impale";
        private const int PrimeVerifyMs = 450;
        private const int PrimeRetryMs = 250;
        private const int PrimeBackoffMs = 1000;

        public Key ToggleHotkey = Key.F3;
        public Key ModeHotkey = Key.F2;
        public bool RequireShadowSet = true;
        public bool AutoAimBolas = true; // Optional queued aim/cast/restore; other generators are unchanged.
        public int SpeedGeneratorIntervalMs = 500; // Retry/fallback cadence; active Focus suppresses unnecessary pulses.
        public int CombatGeneratorIntervalMs = 500; // Retained public setting: now the Combat Impale interval.
        public int SkillPulseHoldMs = 8;
        public int AutoLootPauseMs = 300; // Same bounded pickup pause as DHStrafe.
        public bool PauseNearUnoperatedPylon = true;
        public float PylonPauseRange = 8f;
        public bool PauseNearPortal = true;
        public float PortalPauseRange = 8f;
        public float InteractableHoverRange = 8f;
        public int PortalArrivalEscapeMinMs = 2000; // Arrival arming window, not a movement delay.
        public bool ShowStatusText = true;
        public float StatusTextCenterXFrac = 0.50f;
        public float StatusTextYFrac = 0.58f;
        public float StatusTextYOffsetPx = 1.0f;

        private IKeyEvent _toggleEvent;
        private IKeyEvent _modeEvent;
        private bool _running;
        private bool _combat;
        private bool _needsImpalePrime;
        private bool _primePending;
        private int _primeStartedTick;
        private int _nextPrimeTick;
        private int _primeAttempts;
        private int _interactionPauseUntilTick;
        private bool _manualInteractionPending, _manualInteractionSawAnimation;
        private int _manualInteractionApproachUntilTick;
        private int _manualInteractionStartGameTick;
        private AcdAnimationState _manualInteractionPreAnimation;
        private int _autoLootPauseUntilTick;
        private int _autoLootHandoffSerial;
        private IUiElement _chatEditLine;
        private IUiElement _urshiGemPane;
        private IUiElement _paragonPane;
        private ActionKey _heldStrafe = ActionKey.Unknown;
        private ActionKey _pulse = ActionKey.Unknown;
        private ushort _ownedStandstill;
        private int _pulseReleaseTick;
        private int _nextGeneratorTick, _nextCombatImpaleTick;
        private uint _combatTargetAcd;
        private string _settingsPath;
        private s7o_HUD_MENU _hudMenu;
        private readonly List<IUiElement> _leftClickUiElements = new List<IUiElement>();
        private readonly List<RectangleF> _leftClickUiRects = new List<RectangleF>();
        private bool _leftClickUiReadable;
        private bool _pylonPauseActive, _portalPauseActive, _portalArrivalEscapeActive;
        private string _inputPauseReason = "idle";
        private bool _areaResumePending;
        private uint _trackedPortalWorldId, _trackedPortalAnnId, _trackedPortalAcdId;
        private float _trackedPortalX, _trackedPortalY;
        private int _trackedPortalLastSeenTick = int.MinValue;
        private int _trackedPortalArrivalTick = int.MinValue;
        private bool _trackedPortalClearedRange, _trackedPortalArmed;
        private const int PortalIdentityRetentionMs = 750;
        // ZDH Helper's click mask, uniformly scaled and anchored at the same edge/center.
        private static readonly RectangleF[] ClickGuardRects1920x1080 = {
            new RectangleF(116f, 11f, 76f, 71f),
            new RectangleF(34f, 57f, 58f, 61f),
            new RectangleF(871f, 2f, 179f, 21f),
            new RectangleF(1644f, 23f, 60f, 26f),
            new RectangleF(1816f, 120f, 25f, 15f),
            new RectangleF(1863f, 363f, 31f, 29f),
            new RectangleF(8f, 973f, 85f, 80f),
            new RectangleF(315f, 893f, 1289f, 187f),
            new RectangleF(1754f, 961f, 157f, 83f),
            new RectangleF(0f, 0f, 245f, 155f) // Pestilence's follower/portrait-side guard.
        };
        private enum AimedSkillJob { Bolas, CombatImpale }
        private AimedSkillJob _aimedSkillJob;
        private ActionKey _aimedSkillKey;
        private uint _aimedSkillSno;
        // Existing _bolas cursor fields/stages form one shared transaction for both jobs.
        private enum BolasStage { Idle, Lease, Aim, Hold, PostInputSettle, Restore, RestoreSettle }
        // Input/settle defaults copied from ZDH Helper.
        private const int BolasPreviewMs = 31, BolasHoldMs = 35, BolasPostInputMs = 24;
        private const int BolasMinimumLeaseMs = 105, BolasPauseAckMs = 80;
        private const int BolasPreInputLimitMs = 200, BolasPostInputLimitMs = 320;
        private BolasStage _bolasAimStage;
        private int _bolasAimStartedTick, _bolasDueTick, _bolasInputTick, _bolasResumeTick;
        private uint _bolasAimActor;
        // All cursor fields are desktop pixels, including negative monitor origins.
        private int _bolasSavedX, _bolasSavedY, _bolasAimX, _bolasAimY;
        private int _bolasRestoreX, _bolasRestoreY, _bolasReferenceX, _bolasReferenceY;
        private int _bolasDeltaX, _bolasDeltaY, _bolasSyntheticFromX, _bolasSyntheticFromY;
        private bool _bolasCursorOwned, _bolasSyntheticPending, _bolasRestorePrepared;
        private bool _bolasInputSent, _bolasSawCastAnimation, _bolasRestoreWriteSent;
        private bool _bolasRestoreConfirmed, _bolasRestoreRescueAttempted;
        private int _bolasRestoreStartedTick = int.MinValue, _bolasRestoreWriteGameTick = int.MinValue;
        private bool _bolasRestoreDesktopConfirmed, _bolasRestoreAwaitingNative;
        private AnimSnoEnum _bolasPreInputAnimation;
        private int _aimedPreAnimationStartTick, _aimedInputGameTick;
        private string _bolasEndReason = "idle";
        private static readonly ActionKey[] MouseUiActions = {
            ActionKey.LeftSkill, ActionKey.RightSkill, ActionKey.Skill1, ActionKey.Skill2,
            ActionKey.Skill3, ActionKey.Skill4, ActionKey.Heal, ActionKey.TownPortal,
            ActionKey.Inventory, ActionKey.SkillsWindow, ActionKey.ParagonWindow,
            ActionKey.Map, ActionKey.WaypointMap, ActionKey.Social, ActionKey.Close
        };
        private IFont _statusFont;
        private IFont _runningFont;
        private IFont _combatFont;

        public s7o_Impale()
        {
            Enabled = true;
            Order = 21010;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            s7o_ImpaleInputContext.Hud = hud;
            s7o_ImpaleInputContext.CanPressLeftSkill = IsLeftClickSafe;
            s7o_ImpaleInputContext.CanPressLeftSkillAt = IsLeftClickDesktopPointSafe;
            foreach (var key in MouseUiActions)
                try
                {
                    var ui = Hud.Render.GetPlayerSkillUiElement(key);
                    if (ui != null && !_leftClickUiElements.Contains(ui)) _leftClickUiElements.Add(ui);
                }
                catch { }
            try { AddClickUi(Hud.Render.ParagonLevelUpSplashTextUiElement); } catch { }
            try { if (Hud.Inventory != null) AddClickUi(Hud.Inventory.FollowerMainUiElement); } catch { }
            foreach (string path in new[] {
                "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList",
                "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect",
                "Root.NormalLayer.game_notify_dialog_backgroundScreen.dialog_new_paragon_button",
                "Root.NormalLayer.BattleNetProfile_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetLeaderboard_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetAchievements_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetStore_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.Guild_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd",
                "Root.NormalLayer.conversation_dialog_main",
                "Root.TopLayer.follower_swap",
                "Root.TopLayer.BattleNetSocialDialogs_main.LayoutRoot.DialogWriteNote"
            })
                try
                {
                    var ui = Hud.Render.RegisterUiElement(path, null, null);
                    if (ui != null && !_leftClickUiElements.Contains(ui)) _leftClickUiElements.Add(ui);
                }
                catch { }
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "plugins", "s7o", "settings", "s7o_Impale.ini");
            try
            {
                if (File.Exists(_settingsPath))
                    foreach (string line in File.ReadAllLines(_settingsPath))
                        if (line.StartsWith("AutoAimBolas=", StringComparison.OrdinalIgnoreCase))
                        {
                            bool value;
                            if (bool.TryParse(line.Substring(13), out value)) AutoAimBolas = value;
                        }
            }
            catch { }
            _chatEditLine = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);
            _urshiGemPane = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.riftReward_dialog.LayoutRoot.gemUpgradePane", null, null);
            _paragonPane = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect", null, null);
            _toggleEvent = Hud.Input.CreateKeyEvent(true, ToggleHotkey, false, false, false);
            _modeEvent = Hud.Input.CreateKeyEvent(true, ModeHotkey, false, false, false);
            _statusFont = Hud.Render.CreateFont("tahoma", 8, 255, 220, 190, 80, true, false, 255, 0, 0, 0, true);
            _runningFont = Hud.Render.CreateFont("tahoma", 8, 255, 80, 255, 120, true, false, 255, 0, 0, 0, true);
            _combatFont = Hud.Render.CreateFont("tahoma", 8, 255, 255, 80, 80, true, false, 255, 0, 0, 0, true);
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            _manualInteractionPending = false;
            if (newGame || (area != null && area.IsTown))
            { Stop(); return; }

            // Match DHStrafe's floor handoff: release our old inputs/cursor, but keep
            // F3 and F2 mode armed. Re-acquire from fresh skills after loading ends.
            int now = Environment.TickCount;
            CancelPulse("new area");
            ReleaseStrafe();
            ResetPortalApproachState();
            _pylonPauseActive = false;
            _primePending = false;
            _primeAttempts = 0;
            _primeStartedTick = 0;
            _nextPrimeTick = _nextGeneratorTick = _nextCombatImpaleTick = now;
            _combatTargetAcd = 0u;
            _interactionPauseUntilTick = _autoLootPauseUntilTick = now;
            _areaResumePending = _running && area != null;
            _inputPauseReason = _running ? "world transition" : "idle";
        }
        public void ForceStopForDisable() { Stop(); }

        // AutoLoot calls this before saving or moving its own cursor. Return our
        // aim first, release only our inputs, and leave F3 armed for automatic resume.
        public void PauseForAutoLootPickup()
        {
            if (!Enabled || !_running) return;
            int now = Environment.TickCount;
            _autoLootPauseUntilTick = unchecked(now + Math.Max(50, Math.Min(1000, AutoLootPauseMs)));
            _autoLootHandoffSerial = unchecked(_autoLootHandoffSerial + 1);
            _inputPauseReason = "AutoLoot pickup";
            CancelPulse("AutoLoot pickup");
            ReleaseStrafe();
            _primePending = false;
        }

        public void StopForAutoLootUrshiHandoff() { Stop(); }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || !_running || (button != MouseButtons.Left && button != MouseButtons.Right)
                || s7o_ImpaleInput.OwnsMouseButton(button)) return false;

            // A new user click can replace/cancel a previous interaction approach.
            int now = Environment.TickCount;
            if (_manualInteractionPending) _interactionPauseUntilTick = now;
            _manualInteractionPending = false;
            if (button != MouseButtons.Left) return false;

            IPlayerSkill strafe, impale, generator;
            if (!CanRun(out strafe, out impale, out generator) || !IsLeftClickSafe()) return false;
            var actor = Hud.Game.SelectedActor;
            if (!IsClickedWorldInteractable(actor)) return false;

            _manualInteractionPending = true;
            _manualInteractionSawAnimation = false;
            _manualInteractionStartGameTick = Hud.Game.CurrentGameTick;
            _manualInteractionPreAnimation = Hud.Game.Me.AnimationState;
            // This is an abandoned-approach watchdog, not a mandatory wait.
            _manualInteractionApproachUntilTick = unchecked(now + 5000);
            _interactionPauseUntilTick = unchecked(now + 300); // Existing interaction acknowledgement window.
            _inputPauseReason = "clicked world interaction";
            CancelPulse(_inputPauseReason);
            ReleaseStrafe();
            _primePending = false;
            return false; // Pass the original click through; never release the user's LMB.
        }

        public bool MouseUp(MouseButtons button) { return false; }

        private static bool IsClickedWorldInteractable(IActor actor)
        {
            if (actor == null || actor.SnoActor == null || !actor.IsOnScreen || !actor.IsClickable
                || actor.IsDisabled || actor.IsOperated
                || actor is IItem || actor is IPlayer || actor.GizmoType == GizmoType.Item) return false;
            var monster = actor as IMonster;
            if (monster != null && monster.Attackable) return false;
            ActorKind kind = actor.SnoActor.Kind;
            return kind != ActorKind.Follower && kind != ActorKind.Player && kind != ActorKind.Skill
                && kind != ActorKind.Avoid && kind != ActorKind.Gold && kind != ActorKind.HealthGlobe
                && kind != ActorKind.PowerGlobe && kind != ActorKind.RiftOrb;
        }

        private bool MaintainManualInteraction(int now)
        {
            if (!_manualInteractionPending) return false;
            var state = Hud.Game.Me.AnimationState;
            bool animating = state == AcdAnimationState.Gizmo || state == AcdAnimationState.FloatConversation
                || state == AcdAnimationState.CastingPortal || state == AcdAnimationState.Casting
                || state == AcdAnimationState.Channeling;
            if (animating)
            {
                // Do not count the outgoing Strafe sample as a completed bounty interaction.
                if (Hud.Game.CurrentGameTick != _manualInteractionStartGameTick
                    && (state != _manualInteractionPreAnimation || state == AcdAnimationState.Gizmo
                        || state == AcdAnimationState.FloatConversation || state == AcdAnimationState.CastingPortal))
                    _manualInteractionSawAnimation = true;
                return true;
            }
            if (_manualInteractionSawAnimation)
            {
                _interactionPauseUntilTick = now;
                _manualInteractionPending = false;
                return false; // Animation finished: resume even if the player is now moving.
            }
            bool approaching = state == AcdAnimationState.Running && !Due(now, _manualInteractionApproachUntilTick);
            if (approaching || !Due(now, _interactionPauseUntilTick)) return true;
            _manualInteractionPending = false;
            return false;
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed) return;
            // Paragon allocation owns these hotkeys while its panel is open.
            if (IsVisible(_paragonPane)) return;

            if (_toggleEvent != null && _toggleEvent.Matches(keyEvent))
            {
                if (_running)
                {
                    Stop();
                    return;
                }

                IPlayerSkill strafe;
                IPlayerSkill impale;
                IPlayerSkill generator;
                if (!CanRun(out strafe, out impale, out generator)) return;

                _running = true;
                _combat = false;
                _needsImpalePrime = true;
                _combatTargetAcd = 0u;
                _nextGeneratorTick = _nextCombatImpaleTick = Environment.TickCount;
                _nextPrimeTick = _nextGeneratorTick;
                _interactionPauseUntilTick = _nextGeneratorTick;
                _autoLootPauseUntilTick = _nextGeneratorTick;
                return;
            }

            if (_running && _modeEvent != null && _modeEvent.Matches(keyEvent)
                && Hud.Game != null && !Hud.Game.IsInTown && !InputUiBlocked())
            {
                _combat = !_combat;
                _nextGeneratorTick = _nextCombatImpaleTick = Environment.TickCount;
            }
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            if (Hud != null && Hud.Window != null && Hud.Window.IsForeground)
                s7o_InputReleaseArbiter.RetryPending(now);

            // Yield our inputs to Paragon allocation, keeping F3 armed for resume.
            if (IsVisible(_paragonPane))
            {
                _inputPauseReason = "Paragon menu";
                CancelPulse(_inputPauseReason);
                ReleaseStrafe();
                _primePending = false;
                return;
            }

            // A failed restore keeps its destination for foreground recovery. Never
            // start another aim or resume Strafe with our old synthetic aim left behind.
            if (_bolasAimStage == BolasStage.Idle && _bolasCursorOwned)
            {
                if (RestoreBolasCursor()) ClearBolasCursor();
                return;
            }
            RefreshClickUiRects();
            UpdatePortalInteractionState(now);
            _pylonPauseActive = PauseNearUnoperatedPylon && IsUnoperatedPylonNearby(PylonPauseRange);
            ObserveBolasAnimation();
            FinishPulse(now);
            if (!_running) return;

            // FreeHUD can publish no hero or an empty skill snapshot before OnNewArea.
            // These are pauses, not a request to disarm F3. Never inject through them.
            if (Enabled && IsTransitionSnapshot())
            {
                _inputPauseReason = "world transition";
                CancelPulse(_inputPauseReason);
                ReleaseStrafe();
                _primePending = false;
                return;
            }

            IPlayerSkill strafe;
            IPlayerSkill impale;
            IPlayerSkill generator;
            if (!Enabled || !CanRun(out strafe, out impale, out generator))
            {
                Stop();
                return;
            }

            _areaResumePending = false;

            // Never re-acquire Strafe, standstill or generator input during a pickup.
            if (!Due(now, _autoLootPauseUntilTick))
            { _inputPauseReason = "AutoLoot pickup"; return; }

            // Match DHStrafe: interaction zones take precedence over queued casts.
            // An item hover still does not request a pickup or an interaction pause.
            bool manualInteraction = MaintainManualInteraction(now);
            bool hoveredInteraction = IsHoveringUrshi() || IsHoveringInteractable();
            if (hoveredInteraction) _interactionPauseUntilTick = unchecked(now + 300);
            if (manualInteraction || _pylonPauseActive || _portalPauseActive || !Due(now, _interactionPauseUntilTick))
            {
                _inputPauseReason = manualInteraction ? "clicked world interaction" : _pylonPauseActive ? "pylon nearby"
                    : _portalPauseActive ? "portal nearby" : "hovered interaction";
                CancelPulse(_inputPauseReason);
                ReleaseStrafe();
                _primePending = false;
                return;
            }

            // A newly arrived/start-inside portal is movement-only until it has been
            // cleared once. Do not stop the hero on the entrance or aim Bolas there.
            if (_portalArrivalEscapeActive)
            {
                _inputPauseReason = "portal arrival movement";
                CancelPulse(_inputPauseReason);
                _primePending = false;
                if (_heldStrafe == ActionKey.Unknown && s7o_ImpaleInput.Down(Owner, strafe.Key))
                    _heldStrafe = strafe.Key;
                return;
            }
            _inputPauseReason = "running";

            bool aimedSkillFinished = false;
            if (_bolasAimStage != BolasStage.Idle)
            {
                if (AdvanceBolasAim(_aimedSkillJob == AimedSkillJob.CombatImpale ? impale : generator, now)) return;
                aimedSkillFinished = true;
            }

            // Observe while the pulse is still held: a short cast animation may finish
            // before the release frame. Successful input injection alone is not a cast.
            if (_needsImpalePrime && _primePending && IsImpaleAnimation())
            {
                _needsImpalePrime = false;
                _primePending = false;
                _primeAttempts = 0;
            }
            if (_pulse != ActionKey.Unknown) return;

            // Generator input never changes Sanctified Strafe's selected spender. If the
            // player uses another non-channeled Hatred spender, re-prime Impale once after
            // that input releases. Combat's aimed Impale jobs already select that spender.
            if (IsAlternateHatredSpenderDown(impale, strafe, generator))
            {
                _needsImpalePrime = true;
                _primePending = false;
                _primeAttempts = 0;
                _nextPrimeTick = now;
                ReleaseStrafe();
                return;
            }

            if (_needsImpalePrime)
            {
                MaintainImpalePrime(impale, generator, now);
                return;
            }

            if (_heldStrafe == ActionKey.Unknown && s7o_ImpaleInput.Down(Owner, strafe.Key))
                _heldStrafe = strafe.Key;
            if (_heldStrafe == ActionKey.Unknown) return;

            // Hand back one real update before starting another owned cursor transaction.
            if (aimedSkillFinished) return;
            MaintainGenerator(generator, now);
            if (_bolasAimStage != BolasStage.Idle || _pulse != ActionKey.Unknown) return;
            if (_combat) MaintainCombatImpale(impale, now);
        }

        private void MaintainGenerator(IPlayerSkill generator, int now, bool resourceRecovery = false)
        {
            if (!Due(now, _nextGeneratorTick)) return;
            if (!resourceRecovery && FocusTimeLeft() > 0.5) return;
            if (!HasGeneratorTarget(generator)) return;
            int interval = Math.Max(50, SpeedGeneratorIntervalMs);
            if (s7o_ImpaleInput.IsDown(generator.Key))
            { _nextGeneratorTick = unchecked(now + interval); return; }
            if (AutoAimBolas && generator.SnoPower.Sno == BolasSno)
            {
                if (BeginBolasAim(generator, now)) return;
                // Neither mode falls back to a blind pulse after a failed target aim.
                return;
            }
            if (StartSkillPulse(generator.Key, now))
                _nextGeneratorTick = unchecked(now + interval);
            else
                _nextGeneratorTick = unchecked(now + 50);
        }

        private bool HasGeneratorTarget(IPlayerSkill generator)
        {
            // Reuse the same live, attackable, on-screen and UI-safe target checks as Bolas aim.
            foreach (var monster in Hud.Game.AliveMonsters)
            {
                int x, y;
                if (TryBolasPoint(monster, generator.Key, out x, out y)) return true;
            }
            return false;
        }

        private double FocusTimeLeft()
        {
            var buff = Hud.Game.Me.Powers.GetBuff(FocusBuffSno);
            if (buff == null || buff.TimeLeftSeconds == null
                || buff.TimeLeftSeconds.Length <= FocusBuffIconIndex) return 0.0;
            return Math.Max(0.0, buff.TimeLeftSeconds[FocusBuffIconIndex]);
        }

        public void SetAutoAimBolas(bool enabled)
        {
            AutoAimBolas = enabled;
            if (_running && _bolasAimStage != BolasStage.Idle && _aimedSkillJob == AimedSkillJob.Bolas)
                CancelBolasAim("aim setting changed", Environment.TickCount);
            else if (_bolasAimStage == BolasStage.Idle) CancelPulse();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, "AutoAimBolas=" + enabled + Environment.NewLine);
            }
            catch { }
        }

        private bool TryBolasPoint(IMonster monster, ActionKey key, out int x, out int y, bool projectedCombat = false)
        {
            x = y = 0;
            if (monster == null || !monster.IsAlive || (!projectedCombat && (!monster.Attackable || !monster.IsOnScreen))
                || monster.Illusion || monster.Invulnerable || monster.Untargetable || monster.Stealthed
                || monster.Invisible || monster.Hidden || monster.WorldId != Hud.Game.Me.WorldId
                || monster.FloorCoordinate == null) return false;
            var point = monster.FloorCoordinate.ToScreenCoordinate(true, true);
            if (point == null || double.IsNaN(point.X) || double.IsNaN(point.Y)
                || double.IsInfinity(point.X) || double.IsInfinity(point.Y)) return false;
            double cx = Math.Round(point.X), cy = Math.Round(point.Y);
            if (cx < 0 || cy < 0 || cx >= Hud.Window.Size.Width || cy >= Hud.Window.Size.Height
                || (key == ActionKey.LeftSkill && !IsLeftClickPointSafe(cx, cy))) return false;
            long sx = (long)cx + Hud.Window.Offset.X, sy = (long)cy + Hud.Window.Offset.Y;
            if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
            x = (int)sx; y = (int)sy;
            return true;
        }

        private bool BeginBolasAim(IPlayerSkill generator, int now)
        { return BeginAimedSkill(generator, now, AimedSkillJob.Bolas); }

        private void MaintainCombatImpale(IPlayerSkill impale, int now)
        {
            if (!Due(now, _nextCombatImpaleTick) || impale.IsOnCooldown
                || s7o_ImpaleInput.IsDown(impale.Key)
                || Hud.Game.Me.Stats.ResourceCurPri + 0.1f < Math.Max(0f, impale.ResourceCost)) return;
            BeginAimedSkill(impale, now, AimedSkillJob.CombatImpale);
        }

        private static bool RangedHasAffix(IMonster monster, MonsterAffix affix)
        {
            if (monster.AffixSnoList != null)
                foreach (var entry in monster.AffixSnoList)
                    if (entry != null && entry.Affix == affix) return true;
            if (monster.Pack != null && monster.Pack.AffixSnoList != null)
                foreach (var entry in monster.Pack.AffixSnoList)
                    if (entry != null && entry.Affix == affix) return true;
            return false;
        }

        private static int CombatImpaleTargetTier(IMonster monster)
        {
            if (monster.Rarity == ActorRarity.Boss) return 5;
            bool leader = monster.Rarity == ActorRarity.Rare || monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Unique;
            bool minion = monster.Rarity == ActorRarity.RareMinion;
            if (leader || minion)
                return RangedHasAffix(monster, MonsterAffix.Juggernaut) ? (leader ? 2 : 1) : (leader ? 4 : 3);
            return 0;
        }

        private bool BeginAimedSkill(IPlayerSkill generator, int now, AimedSkillJob job)
        {
            // A manual LMB hold keeps its aim and native target lock.
            if (s7o_InputReleaseArbiter.HasPendingRelease
                || (s7o_ImpaleInput.IsDown(ActionKey.LeftSkill) && _heldStrafe != ActionKey.LeftSkill)) return false;
            IMonster best = null;
            int bestTier = -1, aimX = 0, aimY = 0;
            foreach (var monster in Hud.Game.AliveMonsters)
            {
                if (monster == null) continue;
                int tier = job == AimedSkillJob.CombatImpale ? CombatImpaleTargetTier(monster)
                    : monster.Rarity == ActorRarity.Boss || monster.Rarity == ActorRarity.Rare
                        || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique ? 2
                        : monster.Rarity == ActorRarity.RareMinion ? 1 : 0;
                int x, y;
                if (!TryBolasPoint(monster, generator.Key, out x, out y, job == AimedSkillJob.CombatImpale)) continue;
                bool retained = job == AimedSkillJob.CombatImpale && monster.AcdId == _combatTargetAcd;
                bool bestRetained = best != null && job == AimedSkillJob.CombatImpale && best.AcdId == _combatTargetAcd;
                if (best != null && (tier < bestTier || (tier == bestTier && (bestRetained
                    || (!retained && (monster.NormalizedXyDistanceToMe > best.NormalizedXyDistanceToMe
                        || (monster.NormalizedXyDistanceToMe == best.NormalizedXyDistanceToMe && monster.AcdId >= best.AcdId))))))) continue;
                best = monster; bestTier = tier; aimX = x; aimY = y;
            }
            CursorPoint cursor;
            if (best == null || !GetCursorPos(out cursor)
                || !IsLeftClickPointSafe((long)cursor.X - Hud.Window.Offset.X,
                    (long)cursor.Y - Hud.Window.Offset.Y)) return false;
            ReleaseStrafe();
            if (s7o_InputReleaseArbiter.HasPendingRelease || !EnsureStandstill()) return false;
            // No cursor ownership until native movement has stopped.
            _aimedSkillJob = job; _aimedSkillKey = generator.Key; _aimedSkillSno = generator.SnoPower.Sno;
            if (job == AimedSkillJob.CombatImpale) _combatTargetAcd = best.AcdId;
            _bolasAimActor = best.AcdId;
            _bolasAimX = aimX; _bolasAimY = aimY;
            _bolasAimStartedTick = now;
            _bolasDueTick = unchecked(now + BolasPauseAckMs);
            _bolasInputSent = _bolasSawCastAnimation = false;
            _bolasInputTick = _bolasResumeTick = 0;
            _bolasRestoreWriteSent = _bolasRestoreConfirmed = _bolasRestoreRescueAttempted = false;
            _bolasRestoreDesktopConfirmed = _bolasRestoreAwaitingNative = false;
            _bolasRestoreStartedTick = _bolasRestoreWriteGameTick = int.MinValue;
            _bolasRestorePrepared = _bolasCursorOwned = _bolasSyntheticPending = false;
            _bolasDeltaX = _bolasDeltaY = 0;
            _bolasEndReason = "pending";
            _bolasAimStage = BolasStage.Lease;
            return true;
        }

        private void ObserveBolasAnimation()
        {
            if (!_bolasInputSent || _bolasAimStage == BolasStage.Idle
                || _bolasAimStage == BolasStage.Restore || _bolasAimStage == BolasStage.RestoreSettle
                || Hud == null || Hud.Game == null || Hud.Game.Me == null) return;
            var state = Hud.Game.Me.AnimationState;
            if ((_aimedSkillJob == AimedSkillJob.CombatImpale
                    ? IsImpaleAnimation() && (Hud.Game.Me.Animation != _bolasPreInputAnimation
                        || (Hud.Game.Me.LoopingAnimationStartTick > _aimedPreAnimationStartTick
                            && Hud.Game.Me.LoopingAnimationStartTick >= _aimedInputGameTick))
                    : Hud.Game.Me.Animation != _bolasPreInputAnimation)
                && (state == AcdAnimationState.Attacking || state == AcdAnimationState.Casting))
                _bolasSawCastAnimation = true;
        }

        private bool AdvanceBolasAim(IPlayerSkill generator, int now)
        {
            if ((generator == null || generator.SnoPower == null || generator.Key != _aimedSkillKey || generator.SnoPower.Sno != _aimedSkillSno
                    || (_aimedSkillJob == AimedSkillJob.Bolas && !AutoAimBolas)
                    || (_aimedSkillJob == AimedSkillJob.CombatImpale && !_combat))
                && _bolasAimStage != BolasStage.Restore && _bolasAimStage != BolasStage.RestoreSettle)
            { CancelBolasAim("aim disabled/loadout changed", now); return true; }
            int age = unchecked(now - (_bolasInputSent ? _bolasInputTick : _bolasAimStartedTick));
            if (_bolasAimStage != BolasStage.Restore && _bolasAimStage != BolasStage.RestoreSettle
                && age > (_bolasInputSent ? BolasPostInputLimitMs : BolasPreInputLimitMs))
            { CancelBolasAim("transaction watchdog", now); return true; }
            if (_bolasAimStage == BolasStage.Lease)
            {
                if (s7o_InputReleaseArbiter.HasPendingRelease
                    || !s7o_ImpaleInput.IsVirtualKeyDown(StandstillKey())
                    || Hud.Game.Me.AnimationState == AcdAnimationState.Running)
                {
                    if (Due(now, _bolasDueTick))
                    { CancelBolasAim("movement stop timeout", now); }
                    return true;
                }
                int x, y;
                CursorPoint cursor;
                if (!TryBolasPoint(FindBolasTarget(), generator.Key, out x, out y, _aimedSkillJob == AimedSkillJob.CombatImpale)
                    || !GetCursorPos(out cursor)
                    || !IsLeftClickPointSafe((long)cursor.X - Hud.Window.Offset.X,
                        (long)cursor.Y - Hud.Window.Offset.Y))
                { CancelBolasAim("target/UI before aim", now); return true; }
                _bolasSavedX = Hud.Window.CursorX + Hud.Window.Offset.X;
                _bolasSavedY = Hud.Window.CursorY + Hud.Window.Offset.Y;
                _bolasReferenceX = _bolasSavedX; _bolasReferenceY = _bolasSavedY;
                _bolasAimX = x; _bolasAimY = y;
                if (!WriteBolasAim())
                { CancelBolasAim("aim write failed", now); return true; }
                _bolasCursorOwned = true;
                _bolasAimStage = BolasStage.Aim;
                _bolasDueTick = unchecked(now + BolasPreviewMs);
                return true;
            }
            if (_bolasAimStage == BolasStage.Aim)
            {
                if (!Due(now, _bolasDueTick)) return true;
                CaptureBolasCursorIntent();
                if (Hud.Game.Me.AnimationState == AcdAnimationState.Running
                    || !s7o_ImpaleInput.IsVirtualKeyDown(StandstillKey())
                    || s7o_ImpaleInput.IsDown(generator.Key)
                    || (_aimedSkillJob == AimedSkillJob.CombatImpale
                        && (generator.IsOnCooldown || Hud.Game.Me.Stats.ResourceCurPri + 0.1f < Math.Max(0f, generator.ResourceCost))))
                { CancelBolasAim("readiness lost/player input", now); return true; }
                int x, y;
                if (!TryBolasPoint(FindBolasTarget(), generator.Key, out x, out y, _aimedSkillJob == AimedSkillJob.CombatImpale)
                    || (generator.Key == ActionKey.LeftSkill
                        && !IsLeftClickPointSafe((long)_bolasAimX - Hud.Window.Offset.X,
                            (long)_bolasAimY - Hud.Window.Offset.Y)))
                { CancelBolasAim("target lost/UI", now); return true; }
                if (_aimedSkillJob == AimedSkillJob.CombatImpale)
                {
                    var hovered = Hud.Game.SelectedActor as IMonster;
                    if (generator.Key == ActionKey.LeftSkill
                        && (hovered == null || !hovered.IsAlive || hovered.AcdId != _bolasAimActor
                            || BolasCursorDistance(x, y, _bolasAimX, _bolasAimY) > 10))
                    { CancelBolasAim("LMB target acknowledgement lost", now); return true; }
                    // Keyboard Impale can use the current projection in the ordered batch.
                    // Mouse Impale additionally requires a native acknowledged preview.
                    _bolasAimX = x; _bolasAimY = y;
                }
                _bolasPreInputAnimation = Hud.Game.Me.Animation;
                _aimedPreAnimationStartTick = Hud.Game.Me.LoopingAnimationStartTick;
                _aimedInputGameTick = Hud.Game.CurrentGameTick;
                // Reassert the exact previewed aim and skill-down as one ordered batch.
                ArmBolasSyntheticWrite(_bolasAimX, _bolasAimY);
                if (!s7o_ImpaleInput.DownAt(Owner, generator.Key, _bolasAimX, _bolasAimY))
                { CancelBolasAim("aim/input batch failed", now); return true; }
                _pulse = generator.Key;
                _pulseReleaseTick = unchecked(now + (_aimedSkillJob == AimedSkillJob.CombatImpale ? 55 : BolasHoldMs));
                _bolasInputSent = true;
                _bolasInputTick = now;
                _bolasAimStage = BolasStage.Hold;
                return true;
            }
            if (_bolasAimStage == BolasStage.Hold)
            {
                CaptureBolasCursorIntent();
                if (_pulse != ActionKey.Unknown || s7o_InputReleaseArbiter.HasPendingRelease) return true;
                if (_bolasSawCastAnimation) BeginBolasRestore(now);
                else
                {
                    _bolasAimStage = BolasStage.PostInputSettle;
                    _bolasDueTick = unchecked(now + BolasPostInputMs);
                }
                return true;
            }
            if (_bolasAimStage == BolasStage.PostInputSettle)
            {
                CaptureBolasCursorIntent();
                if (_bolasSawCastAnimation || Due(now, _bolasDueTick)) BeginBolasRestore(now);
                return true;
            }
            if (_bolasAimStage == BolasStage.Restore)
            {
                if (s7o_InputReleaseArbiter.HasPendingRelease) return true;
                // Observe a following collection, matching ZDH's restore stage.
                long x = (long)Hud.Window.CursorX + Hud.Window.Offset.X;
                long y = (long)Hud.Window.CursorY + Hud.Window.Offset.Y;
                int restoreDistance = BolasCursorDistance(x, y, _bolasRestoreX, _bolasRestoreY);
                int aimDistance = BolasCursorDistance(x, y, _bolasAimX, _bolasAimY);
                _bolasRestoreConfirmed = _bolasRestoreWriteSent && restoreDistance <= 10;
                bool residue = _bolasRestoreWriteSent && restoreDistance >= 240
                    && (long)aimDistance * 2 + 40 < restoreDistance;
                CursorPoint desktop;
                bool desktopReadable = GetCursorPos(out desktop);
                _bolasRestoreDesktopConfirmed = _bolasRestoreWriteSent && desktopReadable
                    && BolasCursorDistance(desktop.X, desktop.Y, _bolasRestoreX, _bolasRestoreY) <= 10;
                _bolasRestoreAwaitingNative = residue && !_bolasRestoreConfirmed;
                if (_bolasRestoreAwaitingNative)
                {
                    // Windows can already be restored while the native cursor still echoes
                    // our aim. Keep movement paused until that echo clears; do not warp again
                    // or disarm F3 merely because the next native sample is late.
                    if (unchecked(now - _bolasRestoreStartedTick) >= BolasPostInputLimitMs)
                    { _bolasEndReason = "restore acknowledgement timeout"; Stop(); return true; }
                    if (Hud.Game.CurrentGameTick == _bolasRestoreWriteGameTick || !desktopReadable)
                        return true;
                    int desktopRestoreDistance = BolasCursorDistance(desktop.X, desktop.Y,
                        _bolasRestoreX, _bolasRestoreY);
                    int desktopAimDistance = BolasCursorDistance(desktop.X, desktop.Y,
                        _bolasAimX, _bolasAimY);
                    bool desktopResidue = desktopRestoreDistance >= 240
                        && (long)desktopAimDistance * 2 + 40 < desktopRestoreDistance;
                    if (desktopResidue && !_bolasRestoreRescueAttempted)
                    {
                        // Retry once only when both cursor sources still show synthetic aim.
                        _bolasRestoreRescueAttempted = true;
                        if (!RestoreBolasCursor()) { _bolasEndReason = "restore write failed"; Stop(); }
                    }
                    return true;
                }
                // Fresh steering away from synthetic aim is not overridden with another warp.
                ClearBolasCursor();
                _bolasAimStage = BolasStage.RestoreSettle;
            }
            if (_bolasAimStage == BolasStage.RestoreSettle)
            {
                if (s7o_InputReleaseArbiter.HasPendingRelease
                    || !Due(now, unchecked(_bolasAimStartedTick + BolasMinimumLeaseMs))) return true;
                ReleaseStandstill();
                _bolasAimStage = BolasStage.Idle;
                _bolasResumeTick = now;
                if (_bolasEndReason == "pending")
                    _bolasEndReason = _bolasSawCastAnimation ? "animation observed/handback" : "settled/handback";
                // A full movement interval follows handback; disabled auto-aim keeps old cadence.
                SkipBolasCadence(now);
                return false;
            }
            return true;
        }

        private IMonster FindBolasTarget()
        {
            foreach (var monster in Hud.Game.AliveMonsters)
                if (monster.AcdId == _bolasAimActor) return monster;
            return null;
        }

        private void SkipBolasCadence(int now)
        {
            // Cadence follows handback; keep generator and spender deadlines independent.
            if (_aimedSkillJob == AimedSkillJob.CombatImpale)
                _nextCombatImpaleTick = unchecked(now + Math.Max(50, CombatGeneratorIntervalMs));
            else _nextGeneratorTick = unchecked(now + Math.Max(50, SpeedGeneratorIntervalMs));
        }

        private static int BolasCursorDistance(long x, long y, long tx, long ty)
        {
            double dx = x - tx, dy = y - ty;
            return (int)Math.Min(int.MaxValue, Math.Round(Math.Sqrt(dx * dx + dy * dy)));
        }

        private void ArmBolasSyntheticWrite(int x, int y)
        {
            _bolasSyntheticFromX = Hud.Window.CursorX + Hud.Window.Offset.X;
            _bolasSyntheticFromY = Hud.Window.CursorY + Hud.Window.Offset.Y;
            _bolasReferenceX = x; _bolasReferenceY = y;
            _bolasSyntheticPending = true;
        }

        private bool WriteBolasAim()
        {
            ArmBolasSyntheticWrite(_bolasAimX, _bolasAimY);
            return s7o_ImpaleInput.MoveCursorAbsolute(_bolasAimX, _bolasAimY);
        }

        private void AddBolasCursorDelta(int dx, int dy)
        {
            _bolasDeltaX = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)_bolasDeltaX + dx));
            _bolasDeltaY = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)_bolasDeltaY + dy));
        }

        private void CaptureBolasCursorIntent()
        {
            if (!_bolasCursorOwned || _bolasRestorePrepared) return;
            int x = Hud.Window.CursorX + Hud.Window.Offset.X;
            int y = Hud.Window.CursorY + Hud.Window.Offset.Y;
            if (_bolasSyntheticPending)
            {
                int targetDistance = BolasCursorDistance(x, y, _bolasReferenceX, _bolasReferenceY);
                int fromDistance = BolasCursorDistance(x, y, _bolasSyntheticFromX, _bolasSyntheticFromY);
                if (targetDistance <= 14)
                {
                    _bolasSyntheticPending = false;
                    _bolasReferenceX = x; _bolasReferenceY = y;
                    return;
                }
                if (fromDistance <= 14) return;
                if (targetDistance + 14 < fromDistance)
                {
                    AddBolasCursorDelta(x - _bolasReferenceX, y - _bolasReferenceY);
                    _bolasSyntheticPending = false;
                    _bolasReferenceX = x; _bolasReferenceY = y;
                    return;
                }
                AddBolasCursorDelta(x - _bolasSyntheticFromX, y - _bolasSyntheticFromY);
                _bolasSyntheticFromX = x; _bolasSyntheticFromY = y;
                return;
            }
            AddBolasCursorDelta(x - _bolasReferenceX, y - _bolasReferenceY);
            _bolasReferenceX = x; _bolasReferenceY = y;
        }

        private void CancelBolasAim(string reason, int now)
        {
            if (_pulse != ActionKey.Unknown)
            {
                s7o_ImpaleInput.Up(Owner, _pulse);
                _pulse = ActionKey.Unknown;
            }
            _bolasEndReason = reason;
            if (_bolasCursorOwned) BeginBolasRestore(now);
            else
            {
                ReleaseStandstill();
                _bolasAimStage = BolasStage.Idle;
                _bolasResumeTick = now;
                SkipBolasCadence(now);
            }
        }

        private void BeginBolasRestore(int now)
        {
            CaptureBolasCursorIntent();
            if (!RestoreBolasCursor())
            { _bolasEndReason = "restore write failed"; Stop(); return; }
            _bolasAimStage = BolasStage.Restore;
        }

        private bool RestoreBolasCursor()
        {
            if (!_bolasCursorOwned) return true;
            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground) return false;
            if (!_bolasRestorePrepared)
            {
                CaptureBolasCursorIntent();
                long x = (long)_bolasSavedX + _bolasDeltaX, y = (long)_bolasSavedY + _bolasDeltaY;
                long left = Hud.Window.Offset.X, top = Hud.Window.Offset.Y;
                _bolasRestoreX = (int)Math.Max(left, Math.Min(left + Math.Max(0, Hud.Window.Size.Width - 1), x));
                _bolasRestoreY = (int)Math.Max(top, Math.Min(top + Math.Max(0, Hud.Window.Size.Height - 1), y));
                _bolasRestorePrepared = true;
                _bolasRestoreStartedTick = Environment.TickCount;
            }
            _bolasRestoreWriteSent = s7o_ImpaleInput.MoveCursorAbsolute(_bolasRestoreX, _bolasRestoreY);
            if (_bolasRestoreWriteSent)
                _bolasRestoreWriteGameTick = Hud.Game != null ? Hud.Game.CurrentGameTick : int.MinValue;
            return _bolasRestoreWriteSent;
        }

        private void ClearBolasCursor()
        {
            _bolasCursorOwned = _bolasSyntheticPending = _bolasRestorePrepared = false;
            _bolasRestoreAwaitingNative = false;
        }

        private void AddClickUi(IUiElement element)
        {
            if (element != null && !_leftClickUiElements.Contains(element)) _leftClickUiElements.Add(element);
        }

        private void RefreshClickUiRects()
        {
            _leftClickUiReadable = false;
            _leftClickUiRects.Clear();
            try
            {
                if (_hudMenu == null)
                    foreach (var plugin in Hud.AllPlugins)
                        if (plugin is s7o_HUD_MENU) { _hudMenu = (s7o_HUD_MENU)plugin; break; }
                if (!Hud.Render.UiHidden)
                {
                    // Copy native bounds once per collection, as GenMonk does.
                    foreach (var ui in _leftClickUiElements)
                        if (ui.Visible && ui.Rectangle.Width > 0 && ui.Rectangle.Height > 0)
                            _leftClickUiRects.Add(ui.Rectangle);
                    foreach (var player in Hud.Game.Players)
                        if (player != null && player.IsInGame && player.PortraitUiElement != null
                            && player.PortraitUiElement.Visible)
                            _leftClickUiRects.Add(player.PortraitUiElement.Rectangle);
                    var size = Hud.Window.Size;
                    if (size.Width <= 0 || size.Height <= 0) return;
                    float scale = Math.Min(size.Width / 1920f, size.Height / 1080f);
                    float extraX = size.Width - 1920f * scale, extraY = size.Height - 1080f * scale;
                    foreach (var source in ClickGuardRects1920x1080)
                    {
                        float centerX = source.Left + source.Width * 0.5f;
                        float centerY = source.Top + source.Height * 0.5f;
                        float offsetX = centerX < 640f ? 0f : centerX > 1280f ? extraX : extraX * 0.5f;
                        float offsetY = centerY < 360f ? 0f : centerY > 720f ? extraY : extraY * 0.5f;
                        _leftClickUiRects.Add(new RectangleF(source.Left * scale + offsetX,
                            source.Top * scale + offsetY, source.Width * scale, source.Height * scale));
                    }
                }
                _leftClickUiReadable = true;
            }
            catch { } // Unknown UI state must not permit a synthetic left click.
        }

        private bool IsLeftClickPointSafe(double x, double y)
        {
            if (!_leftClickUiReadable || double.IsNaN(x) || double.IsNaN(y)
                || x < 0 || y < 0 || x >= Hud.Window.Size.Width || y >= Hud.Window.Size.Height) return false;
            if (_hudMenu != null && _hudMenu.IsAutomationLeftClickBlocked((float)x, (float)y)) return false;
            foreach (var rect in _leftClickUiRects)
                if (x >= rect.Left - 2 && x <= rect.Right + 2
                    && y >= rect.Top - 2 && y <= rect.Bottom + 2) return false;
            return true;
        }

        private bool IsLeftClickDesktopPointSafe(int x, int y)
        {
            try
            {
                return Hud.Window.IsForeground && IsLeftClickPointSafe((long)x - Hud.Window.Offset.X,
                    (long)y - Hud.Window.Offset.Y);
            }
            catch { return false; }
        }

        private bool IsLeftClickSafe()
        {
            try
            {
                // Fallback pulses must not click a world follower either.
                var actor = Hud.Game.SelectedActor;
                if (actor != null && actor.SnoActor != null && actor.SnoActor.Kind == ActorKind.Follower)
                    return false;
                CursorPoint cursor;
                return Hud.Window.IsForeground && GetCursorPos(out cursor)
                    && IsLeftClickPointSafe((long)cursor.X - Hud.Window.Offset.X,
                        (long)cursor.Y - Hud.Window.Offset.Y);
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);

        private bool IsImpaleAnimation()
        {
            var animation = Hud.Game.Me.Animation;
            return animation == AnimSnoEnum._demonhunter_female_cast_impale_01
                || animation == AnimSnoEnum._demonhunter_male_cast_impale_01;
        }

        private void MaintainImpalePrime(IPlayerSkill impale, IPlayerSkill generator, int now)
        {
            ReleaseStrafe();
            if (_primePending)
            {
                if (unchecked(now - _primeStartedTick) < PrimeVerifyMs) return;
                _primePending = false;
                _nextPrimeTick = unchecked(now + (_primeAttempts >= 3 ? PrimeBackoffMs : PrimeRetryMs));
                if (_primeAttempts >= 3) _primeAttempts = 0;
            }

            // A failed opening cast must not leave the macro waiting with no way to
            // recover Hatred. Preserve the selected Speed/Combat generator cadence.
            if (Hud.Game.Me.Stats.ResourceCurPri + 0.1f < Math.Max(0f, impale.ResourceCost))
            {
                MaintainGenerator(generator, now, true);
                return;
            }
            if (!Due(now, _nextPrimeTick)) return;

            bool userHolding = s7o_ImpaleInput.IsDown(impale.Key);
            if (!userHolding && !StartSkillPulse(impale.Key, now, 55)) return;
            _primePending = true;
            _primeStartedTick = now;
            _primeAttempts++;
        }

        private bool InputUiBlocked()
        {
            return IsVisible(_chatEditLine) || IsVisible(_urshiGemPane) || IsVisible(_paragonPane)
                || IsVisible(Hud.Render.WorldMapUiElement)
                || IsVisible(Hud.Render.ActMapUiElement);
        }

        private static bool IsVisible(IUiElement element)
        {
            if (element == null) return false;
            try { element.Refresh(); return element.Visible; }
            catch { return true; } // Yield inputs if the interaction UI cannot be read.
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!Enabled || !ShowStatusText || clipState != ClipState.AfterClip || Hud == null
                || Hud.Game == null || Hud.Game.Me == null || Hud.Window == null
                || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.IsInTown)
                return;

            IPlayerSkill strafe;
            IPlayerSkill impale;
            IPlayerSkill generator;
            if (!CanAdvertise(out strafe, out impale, out generator)) return;

            string text;
            IFont font;
            if (_running && _combat)
            {
                text = "Combat: " + ModeHotkey + " = Speed | " + ToggleHotkey + " = Stop";
                font = _combatFont;
            }
            else if (_running)
            {
                text = "Speed: " + ModeHotkey + " = Combat | " + ToggleHotkey + " = Stop";
                font = _runningFont;
            }
            else
            {
                text = ToggleHotkey + " = Strafe";
                font = _statusFont;
            }

            if (font == null) return;
            text = s7o_Localization.Display(text);
            var layout = font.GetTextLayout(text);
            float x = Hud.Window.Size.Width * StatusTextCenterXFrac - layout.Metrics.Width / 2.0f;
            float y = Hud.Window.Size.Height * StatusTextYFrac + StatusTextYOffsetPx;
            font.DrawText(text, x, y, true);
        }

        private bool CanRun(out IPlayerSkill strafe, out IPlayerSkill impale, out IPlayerSkill generator)
        {
            strafe = null;
            impale = null;
            generator = null;
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Window == null || !Hud.Game.IsInGame
                    || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.IsInTown
                    || !Hud.Window.IsForeground || Hud.Game.Me == null || Hud.Game.Me.IsDead
                    || Hud.Game.Me.HeroClassDefinition == null
                    || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter
                    || (Hud.Inventory != null && Hud.Inventory.InventoryMainUiElement != null
                        && Hud.Inventory.InventoryMainUiElement.Visible)
                    || InputUiBlocked())
                    return false;

                // F3 ownership is build-exclusive without referencing DHStrafe itself.
                if (Hud.Game.Me.GetSetItemCount(GoDSetSno) >= 4)
                    return false;
                if (RequireShadowSet && Hud.Game.Me.GetSetItemCount(ShadowSetSno) < 6)
                    return false;

                return TryGetSkills(out strafe, out impale, out generator);
            }
            catch { return false; }
        }

        private bool CanAdvertise(out IPlayerSkill strafe, out IPlayerSkill impale, out IPlayerSkill generator)
        {
            strafe = null;
            impale = null;
            generator = null;
            try
            {
                if (Hud.Game.Me.HeroClassDefinition == null
                    || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.DemonHunter
                    || Hud.Game.Me.GetSetItemCount(GoDSetSno) >= 4
                    || (RequireShadowSet && Hud.Game.Me.GetSetItemCount(ShadowSetSno) < 6))
                    return false;
                return TryGetSkills(out strafe, out impale, out generator);
            }
            catch { return false; }
        }

        private bool TryGetSkills(out IPlayerSkill strafe, out IPlayerSkill impale, out IPlayerSkill generator)
        {
            strafe = null;
            impale = null;
            generator = null;
            IPlayerSkill grenades = null;
            try
            {
                foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                {
                    if (skill == null || skill.SnoPower == null) continue;
                    uint sno = skill.SnoPower.Sno;
                    if (sno == StrafeSno) strafe = skill;
                    else if (sno == ImpaleSno) impale = skill;
                    else if (sno == BolasSno) generator = skill;
                    else if (sno == GrenadesSno) grenades = skill;
                }
            }
            catch { return false; }

            // Shadow requires a melee weapon; Bolas and Grenades are the compatible generators.
            if (generator == null) generator = grenades;
            return strafe != null && impale != null && generator != null;
        }


        private bool IsAlternateHatredSpenderDown(IPlayerSkill impale, IPlayerSkill strafe, IPlayerSkill generator)
        {
            try
            {
                foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                {
                    if (skill == null || skill.SnoPower == null || skill.Key == ActionKey.Unknown) continue;
                    uint sno = skill.SnoPower.Sno;
                    if (sno == ImpaleSno || sno == StrafeSno || sno == RapidFireSno
                        || skill == generator || skill == impale || skill == strafe)
                        continue;

                    // Sanctified Strafe tracks non-channeled Hatred spenders. Use FreeHUD's
                    // native rune resource metadata instead of maintaining a brittle SNO list.
                    var types = skill.SnoPower.ResourceCostTypeByRune;
                    int rune = skill.Rune;
                    if (skill.ResourceCost <= 0f || types == null || rune < 0 || rune >= types.Length
                        || types[rune] != PowerResourceCostType.primary)
                        continue;

                    if (s7o_ImpaleInput.IsDown(skill.Key)) return true;
                }
            }
            catch { }
            return false;
        }

        private bool StartSkillPulse(ActionKey key, int now, int holdMs = 0)
        {
            if (!EnsureStandstill()) return false;
            if (!s7o_ImpaleInput.Down(Owner, key))
            {
                // An aimed transaction releases standstill after cursor handback.
                if (_bolasAimStage == BolasStage.Idle) ReleaseStandstill();
                return false;
            }

            _pulse = key;
            _pulseReleaseTick = unchecked(now + (holdMs > 0
                ? Math.Min(80, holdMs) : Math.Max(1, Math.Min(40, SkillPulseHoldMs))));
            return true;
        }

        private void FinishPulse(int now)
        {
            if (_pulse == ActionKey.Unknown || !Due(now, _pulseReleaseTick)) return;
            s7o_ImpaleInput.Up(Owner, _pulse);
            _pulse = ActionKey.Unknown;
            if (_bolasAimStage == BolasStage.Idle) ReleaseStandstill();
        }

        private void CancelPulse(string reason = "cancelled")
        {
            if (_pulse != ActionKey.Unknown)
            {
                s7o_ImpaleInput.Up(Owner, _pulse);
                _pulse = ActionKey.Unknown;
            }
            if (RestoreBolasCursor()) ClearBolasCursor();
            if (_bolasAimStage != BolasStage.Idle && _bolasEndReason == "pending") _bolasEndReason = reason;
            _bolasAimStage = BolasStage.Idle;
            _bolasResumeTick = Environment.TickCount;
            ReleaseStandstill();
        }

        private ushort StandstillKey()
        {
            return s7o_ImpaleInput.StandstillVirtualKey(0x10);
        }

        private bool EnsureStandstill()
        {
            if (_ownedStandstill != 0) return true;
            ushort key = StandstillKey();
            if (key == 0) return false;
            if (s7o_ImpaleInput.IsVirtualKeyDown(key)) return true;
            if (!s7o_ImpaleInput.DownVirtualKey(Owner, key)) return false;
            _ownedStandstill = key;
            return true;
        }

        private void ReleaseStandstill()
        {
            if (_ownedStandstill == 0) return;
            s7o_ImpaleInput.UpVirtualKey(Owner, _ownedStandstill);
            _ownedStandstill = 0;
        }

        private void ReleaseStrafe()
        {
            if (_heldStrafe == ActionKey.Unknown) return;
            s7o_ImpaleInput.Up(Owner, _heldStrafe);
            _heldStrafe = ActionKey.Unknown;
        }

        private bool IsUnoperatedPylonNearby(float range)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null
                    || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Shrines == null) return false;
                float limit = Math.Max(0, range);
                foreach (var shrine in Hud.Game.Shrines)
                    if (shrine != null && shrine.IsPylon && !shrine.IsDisabled && !shrine.IsOperated
                        && shrine.FloorCoordinate != null
                        && Hud.Game.Me.FloorCoordinate.XYDistanceTo(shrine.FloorCoordinate) <= limit) return true;
                return false;
            }
            catch { return false; }
        }

        private void UpdatePortalInteractionState(int now)
        {
            _portalPauseActive = false;
            _portalArrivalEscapeActive = false;

            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null
                    || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Portals == null)
                    return;

                if (!Hud.Game.IsInGame || Hud.Game.IsInTown)
                {
                    ResetPortalApproachState();
                    return;
                }

                IPortal nearest = null;
                float nearestDistance = float.MaxValue;
                foreach (var portal in Hud.Game.Portals)
                {
                    // The same arrival/approach distinction applies to Vision, Vault and Rift
                    // portals. Ignore actors retained from the previous world snapshot.
                    if (portal == null || portal.FloorCoordinate == null || portal.IsDisabled
                        || portal.WorldId != Hud.Game.Me.WorldId) continue;
                    float distanceToPortal = Hud.Game.Me.FloorCoordinate.XYDistanceTo(portal.FloorCoordinate);
                    if (distanceToPortal < nearestDistance)
                    { nearest = portal; nearestDistance = distanceToPortal; }
                }

                if (nearest == null)
                {
                    // Once an armed/approached portal disappears, immediately hand ownership
                    // back to movement while briefly retaining identity. A one-frame actor flicker
                    // can then restore the original pause if the same portal comes back.
                    if (_trackedPortalLastSeenTick != int.MinValue
                        && unchecked(now - _trackedPortalLastSeenTick) < PortalIdentityRetentionMs)
                    {
                        _portalArrivalEscapeActive = PauseNearPortal && _trackedPortalArmed;
                        return;
                    }

                    ResetPortalApproachState();
                    return;
                }

                float distance = Hud.Game.Me.FloorCoordinate.XYDistanceTo(nearest.FloorCoordinate);
                float pauseRange = Math.Max(0, PortalPauseRange);

                if (!MatchesTrackedPortal(nearest))
                {
                    _trackedPortalWorldId = nearest.WorldId;
                    _trackedPortalAnnId = nearest.AnnId;
                    _trackedPortalAcdId = nearest.AcdId;
                    _trackedPortalX = nearest.FloorCoordinate.X;
                    _trackedPortalY = nearest.FloorCoordinate.Y;
                    _trackedPortalArmed = distance > pauseRange;
                    _trackedPortalArrivalTick = _trackedPortalArmed ? int.MinValue : now;
                    _trackedPortalClearedRange = _trackedPortalArmed;
                }
                else if (!_trackedPortalArmed)
                {
                    if (distance > pauseRange) _trackedPortalClearedRange = true;
                    if (_trackedPortalClearedRange
                        && (_trackedPortalArrivalTick == int.MinValue
                            || unchecked(now - _trackedPortalArrivalTick) >= Math.Max(0, PortalArrivalEscapeMinMs)))
                        _trackedPortalArmed = true;
                }

                _trackedPortalLastSeenTick = now;
                bool insideRange = distance <= pauseRange;
                _portalPauseActive = PauseNearPortal
                    && _trackedPortalArmed && insideRange;
                _portalArrivalEscapeActive = PauseNearPortal
                    && !_trackedPortalArmed && insideRange;
            }
            catch
            {
                _portalPauseActive = false;
            }
        }

        private bool MatchesTrackedPortal(IPortal portal)
        {
            if (portal == null || portal.WorldId != _trackedPortalWorldId) return false;
            if (_trackedPortalAnnId != 0 && portal.AnnId != 0 && portal.AnnId == _trackedPortalAnnId)
                return true;
            if (_trackedPortalAcdId != 0 && portal.AcdId != 0 && portal.AcdId == _trackedPortalAcdId)
                return true;
            if (portal.FloorCoordinate == null) return false;
            float dx = portal.FloorCoordinate.X - _trackedPortalX;
            float dy = portal.FloorCoordinate.Y - _trackedPortalY;
            return dx * dx + dy * dy <= 4.0f;
        }

        private void ResetPortalApproachState()
        {
            _trackedPortalWorldId = 0;
            _trackedPortalAnnId = 0;
            _trackedPortalAcdId = 0;
            _trackedPortalX = 0;
            _trackedPortalY = 0;
            _trackedPortalLastSeenTick = int.MinValue;
            _trackedPortalArrivalTick = int.MinValue;
            _trackedPortalClearedRange = false;
            _trackedPortalArmed = false;
            _portalPauseActive = false;
            _portalArrivalEscapeActive = false;
        }

        private bool IsTransitionSnapshot()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Window == null
                    || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null)
                    return true;
                // OnNewArea's payload can precede the native town flag by one snapshot.
                if (_areaResumePending && Hud.Game.IsInTown) return true;
                if (Hud.Game.Me.IsDead || Hud.Game.IsInTown) return false;
                var powers = Hud.Game.Me.Powers;
                if (powers == null || powers.UsedSkills == null) return true;
                foreach (var skill in powers.UsedSkills)
                    if (skill != null && skill.SnoPower != null) return false;
                return true;
            }
            catch { return true; }
        }

        private bool IsHoveringUrshi()
        {
            try
            {
                var actor = Hud.Game.SelectedActor;
                return actor != null && actor.NormalizedXyDistanceToMe <= 10f
                    && actor.SnoActor != null
                    && actor.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem;
            }
            catch { return false; }
        }

        private bool IsHoveringInteractable()
        {
            try
            {
                var actor = Hud.Game.SelectedActor;
                if (actor == null || actor.SnoActor == null
                    || actor.CentralXyDistanceToMe > Math.Max(0, InteractableHoverRange))
                    return false;
                bool portal = actor.SnoActor.Kind == ActorKind.Portal
                    || actor.GizmoType == GizmoType.Portal || actor.GizmoType == GizmoType.BossPortal;
                // A synthetic/native hover on the arrival portal must not defeat escape.
                // A user's LMB interaction still yields; no physical hold is released.
                bool manualLeft = _pulse != ActionKey.LeftSkill && _heldStrafe != ActionKey.LeftSkill
                    && s7o_ImpaleInput.IsDown(ActionKey.LeftSkill);
                if (portal && _portalArrivalEscapeActive && !manualLeft) return false;
                if (portal) return !actor.IsDisabled;
                if (actor.IsDisabled || actor.IsOperated) return false;
                if (actor.SnoActor.Kind == ActorKind.Shrine && Hud.Game.Shrines != null)
                    foreach (var shrine in Hud.Game.Shrines)
                        if (shrine != null && shrine.AcdId == actor.AcdId)
                            return shrine.IsPylon && !shrine.IsDisabled && !shrine.IsOperated;

                // GizmoType.Chest also covers corpses/loose stones/racks. Only native
                // chest kinds (or chest-specific native codes) request this pause.
                string code = actor.SnoActor.Code ?? string.Empty;
                if (actor.SnoActor.Kind == ActorKind.ArmorRack
                    || actor.SnoActor.Kind == ActorKind.WeaponRack
                    || actor.SnoActor.Kind == ActorKind.DeadBody
                    || code.IndexOf("rockpile", StringComparison.OrdinalIgnoreCase) >= 0
                    || code.IndexOf("rock_pile", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                return actor.SnoActor.Kind == ActorKind.ChestNormal
                    || actor.SnoActor.Kind == ActorKind.Chest
                    || (actor.GizmoType == GizmoType.Chest
                        && code.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        private void Stop()
        {
            _manualInteractionPending = false;
            CancelPulse();
            ReleaseStrafe();
            _running = false;
            _areaResumePending = false;
            _combat = false;
            _needsImpalePrime = false;
            _primePending = false;
            _primeStartedTick = 0;
            _nextPrimeTick = Environment.TickCount;
            _primeAttempts = 0;
            _interactionPauseUntilTick = Environment.TickCount;
            _autoLootPauseUntilTick = _interactionPauseUntilTick;
            _nextGeneratorTick = _nextCombatImpaleTick = 0;
            _combatTargetAcd = 0u;
            _pylonPauseActive = false;
            _inputPauseReason = "idle";
            ResetPortalApproachState();
        }

        private static bool Due(int now, int target) { return unchecked(now - target) >= 0; }
    }

    internal static class s7o_ImpaleInput
    {
        private static readonly Dictionary<ActionKey, int> OwnedActions = new Dictionary<ActionKey, int>();
        private static readonly HashSet<ushort> OwnedVirtualKeys = new HashSet<ushort>();
        private static readonly int InputSize = Marshal.SizeOf(typeof(Input));
        private static s7o_AutoSkill _autoSkill;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public Data U; }
        [StructLayout(LayoutKind.Explicit)]
        private struct Data
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public IntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyInput
        {
            public ushort VirtualKey, ScanCode;
            public uint Flags, Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        private static s7o_AutoSkill ResolveAutoSkill()
        {
            if (_autoSkill != null) return _autoSkill;
            try
            {
                var hud = s7o_ImpaleInputContext.Hud;
                if (hud == null) return null;
                foreach (var plugin in hud.AllPlugins)
                {
                    var autoSkill = plugin as s7o_AutoSkill;
                    if (autoSkill == null) continue;
                    _autoSkill = autoSkill;
                    return autoSkill;
                }
            }
            catch { }
            return null;
        }

        public static ushort StandstillVirtualKey(ushort fallback)
        {
            var autoSkill = ResolveAutoSkill();
            return autoSkill != null && autoSkill.ForceStandstillVirtualKey != 0
                ? autoSkill.ForceStandstillVirtualKey : fallback;
        }

        public static bool IsDown(ActionKey key)
        {
            int code = VirtualKey(key);
            return code != 0 && (GetAsyncKeyState(code) & 0x8000) != 0;
        }

        public static bool OwnsMouseButton(MouseButtons button)
        {
            int code = button == MouseButtons.Left ? 0x01 : button == MouseButtons.Right ? 0x02 : 0;
            if (code == 0) return false;
            if (OwnedVirtualKeys.Contains((ushort)code)) return true;
            foreach (int ownedCode in OwnedActions.Values)
                if (ownedCode == code) return true;
            return false;
        }

        public static bool IsVirtualKeyDown(ushort code)
        {
            return code != 0 && (GetAsyncKeyState(code) & 0x8000) != 0;
        }

        public static bool DownVirtualKey(string owner, ushort code) { return SendVirtualKey(owner, code, false); }
        public static bool UpVirtualKey(string owner, ushort code) { return SendVirtualKey(owner, code, true); }

        private static bool SendVirtualKey(string owner, ushort code, bool up)
        {
            if (code == 0) return false;
            if (up && !OwnedVirtualKeys.Contains(code)) return true;
            if (!up && IsVirtualKeyDown(code)) return false;

            Input input = new Input();
            input.Type = 1u;
            input.U.Keyboard.VirtualKey = code;
            input.U.Keyboard.Flags = up ? 2u : 0u;
            Input[] packet = new[] { input };
            Func<bool> emit = () => SendInput(1u, packet, InputSize) == 1u;

            if (up)
            {
                bool released = s7o_InputReleaseArbiter.Up(owner, code, emit);
                OwnedVirtualKeys.Remove(code);
                return released;
            }

            bool acquired = s7o_InputReleaseArbiter.Down(owner, code, emit);
            if (acquired) OwnedVirtualKeys.Add(code);
            return acquired;
        }

        public static bool Down(string owner, ActionKey key) { return Send(owner, key, false); }
        public static bool DownAt(string owner, ActionKey key, int screenX, int screenY)
        { return Send(owner, key, false, true, screenX, screenY); }

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        private static bool TryBuildAbsoluteMove(int screenX, int screenY, out Input input)
        {
            input = new Input();
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 1 || height <= 1) return false;
            input.Type = 0u;
            input.U.Mouse.Dx = (int)Math.Round(Math.Max(0.0, Math.Min(65535.0,
                ((long)screenX - left) * 65535.0 / (width - 1))));
            input.U.Mouse.Dy = (int)Math.Round(Math.Max(0.0, Math.Min(65535.0,
                ((long)screenY - top) * 65535.0 / (height - 1))));
            input.U.Mouse.Flags = 0x0001u | 0x8000u | 0x4000u; // MOVE | ABSOLUTE | VIRTUALDESK
            return true;
        }

        public static bool MoveCursorAbsolute(int screenX, int screenY)
        {
            Input move;
            if (!TryBuildAbsoluteMove(screenX, screenY, out move)) return false;
            return SendInput(1u, new[] { move }, InputSize) == 1u;
        }
        public static bool Up(string owner, ActionKey key) { return Send(owner, key, true); }

        private static int VirtualKey(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0x01;
                case ActionKey.RightSkill: return 0x02;
                case ActionKey.Skill1: return BoundKey(ActionKey.Skill1, 0x31);
                case ActionKey.Skill2: return BoundKey(ActionKey.Skill2, 0x32);
                case ActionKey.Skill3: return BoundKey(ActionKey.Skill3, 0x33);
                case ActionKey.Skill4: return BoundKey(ActionKey.Skill4, 0x34);
                default: return 0;
            }
        }

        private static int BoundKey(ActionKey key, int fallback)
        {
            try
            {
                var autoSkill = ResolveAutoSkill();
                if (autoSkill == null) return fallback;
                ushort value = autoSkill.GetCastVirtualKey(key);
                return value == 0 ? fallback : value;
            }
            catch { return fallback; }
        }

        private static bool Send(string owner, ActionKey key, bool up, bool aimed = false, int screenX = 0, int screenY = 0)
        {
            int code;
            if (up)
            {
                if (!OwnedActions.TryGetValue(key, out code)) return true;
            }
            else
            {
                code = VirtualKey(key);
            }

            if (code == 0) return false;
            if (!up && IsDown(key)) return false;
            if (!up && key == ActionKey.LeftSkill)
            {
                // An atomic move+DOWN must validate its destination, not a stale pre-move cursor.
                bool safe = aimed
                    ? s7o_ImpaleInputContext.CanPressLeftSkillAt != null
                        && s7o_ImpaleInputContext.CanPressLeftSkillAt(screenX, screenY)
                    : s7o_ImpaleInputContext.CanPressLeftSkill != null
                        && s7o_ImpaleInputContext.CanPressLeftSkill();
                if (!safe) return false;
            }

            Input input = new Input();
            bool mouse = key == ActionKey.LeftSkill || key == ActionKey.RightSkill;
            input.Type = mouse ? 0u : 1u;
            if (mouse)
                input.U.Mouse.Flags = key == ActionKey.LeftSkill
                    ? (up ? 0x0004u : 0x0002u) : (up ? 0x0010u : 0x0008u);
            else
            {
                input.U.Keyboard.VirtualKey = (ushort)code;
                input.U.Keyboard.Flags = up ? 2u : 0u;
            }

            int identity = mouse ? (key == ActionKey.LeftSkill ? 0x10001 : 0x10002) : code;
            Input move = new Input();
            if (aimed && (up || !TryBuildAbsoluteMove(screenX, screenY, out move))) return false;
            Input[] packet = aimed ? new[] { move, input } : new[] { input };
            uint count = (uint)packet.Length;
            Func<bool> emit = () => SendInput(count, packet, InputSize) == count;

            if (up)
            {
                bool released = s7o_InputReleaseArbiter.Up(owner, identity, emit);
                OwnedActions.Remove(key); // Arbiter owns retries after a failed UP.
                return released;
            }

            bool acquired = s7o_InputReleaseArbiter.Down(owner, identity, emit);
            if (acquired) OwnedActions[key] = code;
            return acquired;
        }
    }

    internal static class s7o_ImpaleInputContext
    {
        public static IController Hud;
        public static Func<bool> CanPressLeftSkill;
        public static Func<int, int, bool> CanPressLeftSkillAt;
    }
}
