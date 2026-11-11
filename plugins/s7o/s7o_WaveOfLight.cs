using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Hold Wave of Light for manual aim, bell bursts and Dash upkeep.
    // Hold AssistKey for bell attacks, avoidance and circle moves.
    // AutoLoot supplies the shared pickup handoff contracts.
    public class s7o_WaveOfLightCyclone : BasePlugin, IAfterCollectHandler, IS7oAutoLootInputHandoff, IS7oAutoLootPickupGuard
    {
        public Keys AssistKey = Keys.Space;
        public int CycloneHoldMs = 800; // Timed continuous hold; Epiphany preempts when available.
        public bool DashToCircles = true;
        public bool SpreadUnpullableBells = true;
        public bool RelayDistantTrash = true;
        public bool HoldPositionDuringHoly = true;
        public Keys ForceMoveKey = Keys.None; // Set to your in-game Force Move binding. None uses verified empty ground.
        public float CircleSearchYards = 70f;
        public float DefensiveDashHealth = .75f;
        public bool MaintainDashBuff = true;
        public bool MaintainEpiphany = true;
        public bool AvoidDanger = true;
        public bool HealInPanic = true;
        public Keys PotionKey = Keys.Q;

        // Match these to your in-game keyboard skill slots if you rebound them.
        // Wave of Light also supports either mouse skill slot.
        public Keys Skill1Key = Keys.D1;
        public Keys Skill2Key = Keys.D2;
        public Keys Skill3Key = Keys.D3;
        public Keys Skill4Key = Keys.D4;
        public Keys StandstillKey = Keys.ShiftKey; // Only pressed for a skill assigned to left-click.

        private const string InputOwner = "WaveOfLightCyclone";
        private const int BellThreshold = 4, DashPulseMs = 45, MovementPulseMs = 55, CircleScanMs = 35;
        private const int BurstYieldMs = 140;
        private const int DashTapMs = 32, DashObservationMs = 350, DashChainGuardMs = 260;
        private const float CircleRadius = 10f, DashRange = 50f, EliteNear = 12f;
        private const float WaveReach = 40f, StageMaxRange = 70f;
        private int _lastEliteApproach = int.MinValue, _lastCursorConflict = int.MinValue;
        private uint _stageCircle, _stageElite, _stageLastCircle, _stageLastElite;
        private CircleStage _stagePhase;
        private int _stageBursts, _stageStarted = int.MinValue, _stageLastAt = int.MinValue;
        private IWorldCoordinate _stageHome;
        private readonly HashSet<uint> _bells = new HashSet<uint>();
        private readonly HashSet<uint> _previousBells = new HashSet<uint>();
        private int _bellSpawnSerial, _bellAimSerial = int.MinValue, _bellAimSlot;
        private int _bellAimAt = int.MinValue, _bellAimRecoveryUntil = int.MinValue;
        private uint _bellAimTarget;
        private IWorldCoordinate _bellAimPosition, _bellAimFocus, _bellAimOrigin;
        private uint _recoveryBellTarget, _misplacedBellTarget;
        private int _misplacedBellCount, _lastMisplacedBell = int.MinValue, _lastBellRecoveryTry = int.MinValue;
        private IWorldCoordinate _recoveryBellOrigin, _recoveryBellFocus;
        private bool _recoveryDetonationSent;
        private int _bellRecoveryUntil = int.MinValue;
        private bool _autoLootYieldPending, _waitForAssistRelease;
        private int _autoLootYieldTick;
        private s7o_AutoLoot _autoLoot;
#if WOL_DIAGNOSTIC
        private int _lastBellPlacementLog = int.MinValue, _lastBellCircleRejectLog = int.MinValue;
        private int _lastBellFallbackLog = int.MinValue;
        private string _lastBellFallbackReason;
#endif
        private readonly HashSet<uint> _ownSummoners = new HashSet<uint>();
        private readonly object _inputLock = new object();
        private IUiElement[] _blockingUi;
        private Func<string, bool> _otherInputOwner;
        private Func<string, int, Func<bool>, bool> _claimInput, _releaseInputClaim;
        private const int MovementClaim = -7108;
        private bool _movementClaimed;
        private Action<int> _retryInputRelease;
        private readonly List<KeyValuePair<IActor, int>> _circleCandidates = new List<KeyValuePair<IActor, int>>(12);
        private uint _pursuitId;
        private int _pursuitHops, _pursuitLast, _lastWalk = int.MinValue, _lastRouteLog = int.MinValue;
        private IWorldCoordinate _walkOrigin;
        private Aim _cycloneSelfAim;
        private volatile Aim _releasedCycloneAim;
        private int _collectionTick;
        private volatile bool _ghosted;
        private int _shoulderTry = int.MinValue;
        private bool _usefulCircleSeen;
        private uint _circleRecoveryId, _lastCircleLandingId;
        private int _circleRecoveryUntil, _lastCircleLandingAt = int.MinValue;
        private IWorldCoordinate _lastCircleLandingCenter;
#if WOL_DIAGNOSTIC
        private int _lastCircleHoldLog = int.MinValue;
#endif
        private bool _ownsAutoCyclone;
        private Delegate _autoCycloneWrapper;
        private int _autoCycloneBindAt = int.MinValue;
        public bool CoordinateAutoSkillCyclone = true;
        private static readonly Func<bool> ClaimMarker = () => true;
        private uint _world, _hero;
        private int _lastDash = int.MinValue, _lastCircleCheck = int.MinValue, _pressedKey, _inputSerial;
        private Aim _dashAim, _activeAim, _dashFlight;
        private bool _holdActive;
        private int _standstillOwned;
#if WOL_DIAGNOSTIC
        private string _circleStatus = "not checked";
#endif
        private readonly List<Body> _pack = new List<Body>(160);
        private int _packTick = int.MinValue, _burstStarted = int.MinValue;
        private int _burstKey, _burstHoldMs, _burstYieldUntil = int.MinValue;
        private int _failedCircleUntil;
        private bool _assistHeld;
        private int _wavePress, _waveSerial, _waveStandstill, _waveHeartbeat;
        private System.Threading.Timer _waveWatch;
        private IntPtr _waveWindow;
        private int _epiphanyKey, _epiphanySerial, _epiphanyStandstill, _lastEpiphany = int.MinValue;
#if WOL_DIAGNOSTIC
        private string _lastWaveState;
        private int _lastWaveLog, _waveStateSince, _lastStatus;
        private int _burstEffectSamples, _burstEffectBuffRefreshes, _burstEffectAnimStarts, _burstEffectSpiritDrops;
        private int _burstLastAnimationTick;
        private double _burstLastBuffTime;
        private float _burstLastSpirit, _burstCycloneCost;
        private bool _burstBuffSeen;
        private string _watchdogRelease;
        private bool _epiphanyConfirmationPending;
#endif
        private Aim _relayHome;
        private bool _relayAtStage, _relayPulled;
        private int _lastRelay = int.MinValue, _relayStarted;
        private uint _failedCircle, _lastCircleWalkId;
        private int _lastCircleWalkAt = int.MinValue;
        private bool _hasSnap;
        private Cursor _snapOriginal, _lastSnap;
        private IntPtr _snapWindow;
        private int _lastEscape = int.MinValue, _lastDashBuffTry = int.MinValue, _lastDashPress = int.MinValue;
        private bool _panicRecovering;
        private int _healthSampleAt, _panicUntil;
        private float _healthBaseline;
        private int _lastPotionTry = int.MinValue;
        private int _lastSerenityTry = int.MinValue;
#if WOL_DIAGNOSTIC
        private int _lastSerenityCoverageLog = int.MinValue;
#endif
        private bool _serenityPending;
        private uint _postCirclePullId;
        private int _postCirclePullUntil = int.MinValue;
        private IPlugin _mapOwner;
        private Func<string, object[], object> _mapQuery;
        private object _mapEpoch;
        private int _mapTick = int.MinValue, _mapBindAt = int.MinValue;
        private uint _mapWorld;
        private readonly List<LineSample> _lineSamples = new List<LineSample>(24);
        private int _lastObstructionTry = int.MinValue, _lastObstructionLog = int.MinValue;
        private uint _lastObstructedElite, _obstructionRouteTarget;
        private IWorldCoordinate _obstructionRouteGoal, _obstructionRouteFocus;
        private int _obstructionRouteAt = int.MinValue;
        private int _lineObstaclesTick = int.MinValue;
        private readonly List<IActor> _lineObstacles = new List<IActor>(16);

        private struct LineSample
        {
            internal uint World;
            internal IWorldCoordinate From, To;
            internal int CheckedAt;
            internal bool Blocked;
            internal string Reason;
        }

        public s7o_WaveOfLightCyclone() { Enabled = true; Order = -100; }

        public override void Load(IController hud)
        {
            base.Load(hud);
            // The standalone also works without the pack. If present, respect its input owner.
            var arbiter = GetType().Assembly.GetType("Turbo.Plugins.s7o.s7o_InputReleaseArbiter");
            var ownerCheck = arbiter?.GetMethod("HasOtherOwner", new[] { typeof(string) });
            if (ownerCheck?.ReturnType == typeof(bool))
                _otherInputOwner = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), ownerCheck);
            var claim = arbiter?.GetMethod("Down", new[] { typeof(string), typeof(int), typeof(Func<bool>) });
            var release = arbiter?.GetMethod("Up", new[] { typeof(string), typeof(int), typeof(Func<bool>) });
            if (claim != null && release != null && _otherInputOwner != null)
            {
                _claimInput = (Func<string, int, Func<bool>, bool>)Delegate.CreateDelegate(
                    typeof(Func<string, int, Func<bool>, bool>), claim);
                _releaseInputClaim = (Func<string, int, Func<bool>, bool>)Delegate.CreateDelegate(
                    typeof(Func<string, int, Func<bool>, bool>), release);
            }
            var retryRelease = arbiter?.GetMethod("RetryPending", new[] { typeof(int) });
            if (retryRelease != null)
                _retryInputRelease = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), retryRelease);
            Trace("REV45 loaded: bounded bell recovery and AutoLoot input handoff; shared claim=" + (_claimInput != null));
            _blockingUi = new[] {
                "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline",
                "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList",
                "Root.NormalLayer.vendor_dialog_mainPage",
                "Root.NormalLayer.shop_dialog_mainPage.panel",
                "Root.NormalLayer.conversation_dialog_main",
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd",
                "Root.TopLayer.scripted_sequence",
            }.Select(path => Hud.Render.RegisterUiElement(path, null, null)).ToArray();
        }

        public bool CanYieldForAutoLootPickup
        {
            get
            {
                if (!Enabled || Hud?.Game?.Me == null) return true;
                if (_dashAim != null || _dashFlight != null || _activeAim != null
                    || _cycloneSelfAim != null || _releasedCycloneAim != null || _walkOrigin != null
                    || _burstStarted != int.MinValue || _serenityPending
                    || Volatile.Read(ref _epiphanyKey) != 0) return false;
                var me = Hud.Game.Me;
                return !_assistHeld || !Held(AssistKey) || me.FloorCoordinate == null
                    || DangerAt(me.FloorCoordinate, landing: false) == null
                        && !(Age(_panicUntil, Environment.TickCount) > 0
                            && me.Defense != null && me.Defense.HealthMax > 0
                            && me.Defense.HealthCur / me.Defense.HealthMax <= .40f);
            }
        }

        public void PauseForAutoLootPickup()
        {
            if (!Enabled || Hud?.Game == null) return;
            // AutoLoot calls this before saving or moving the cursor.
            StopWave();
            ReleaseSnap();
            _autoLoot = Hud.GetPlugin<s7o_AutoLoot>();
            _autoLootYieldPending = true;
            _autoLootYieldTick = Hud.Game.CurrentGameTick;
            ReleaseFinishedClaim();
            Trace("AutoLoot pickup owns cursor");
        }

        public void StopForAutoLootUrshiHandoff()
        {
            CancelInput();
            _waitForAssistRelease = true;
        }

        public void AfterCollect()
        {
#if WOL_DIAGNOSTIC
            var watchdogRelease = Interlocked.Exchange(ref _watchdogRelease, null);
            if (watchdogRelease != null) Trace("Wave watchdog release: " + watchdogRelease);
#endif
            int inputNow = Environment.TickCount;
            if (Hud.Window.IsForeground) _retryInputRelease?.Invoke(inputNow);
            var dashPulse = _activeAim;
            if (dashPulse != null && dashPulse.SharedDashKey != 0 && Age(inputNow, dashPulse.DashReleaseDue) >= 0)
                ReleaseInput(Volatile.Read(ref _inputSerial));
            var game = Hud.Game;
            var me = game.Me;
            int tick = game.CurrentGameTick;
            Volatile.Write(ref _collectionTick, tick);
            bool ghostBuff = me?.Powers?.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno) == true;
            if (ghostBuff && !_ghosted)
            {
                _ghosted = true;
                Trace("automation suspended: revival ghost");
            }
            FinishReleasedCyclone(tick);
            _ownsAutoCyclone = false;
            if (_cycloneSelfAim != null && Age(Environment.TickCount, _cycloneSelfAim.Started) >= 130)
                CancelCycloneAim();
            if (!Enabled || !game.IsInGame || game.IsLoading || game.IsPaused || game.IsInTown
                || !Hud.Window.IsForeground || game.MapMode != MapMode.Minimap
                || Hud.Render.UiHidden || Hud.Render.MinimapUiElement?.Visible != true
                || Hud.Inventory?.InventoryMainUiElement?.Visible == true
                || Hud.Inventory?.StashMainUiElement?.Visible == true
                || me == null || me.IsDead || me.IsDeadSafeCheck || ghostBuff || me.IsInTown || !me.HasValidActor
                || me.HeroClassDefinition?.HeroClass != HeroClass.Monk || me.Powers == null || me.Stats == null
                || me.Powers.SkillOverrideActive || me.Powers.Stunned || me.Powers.Frozen
                || me.AnimationState == AcdAnimationState.CastingPortal
                || Held(Keys.ControlKey) || Held(Keys.Menu) || Held(Keys.LWin) || Held(Keys.RWin)
                || _blockingUi.Any(ui => ui?.Visible == true))
            {
                ReportHudGate(game, me);
                CancelInput();
                return;
            }

            if (_ghosted)
            {
                _ghosted = false;
                _bells.Clear();
                _previousBells.Clear();
                ResetBellAim();
                ClearBellRecovery();
                _bellAimRecoveryUntil = int.MinValue;
                _packTick = _lastCircleCheck = int.MinValue;
                ClearDanger();
                Trace("automation resumed: revival ghost cleared");
            }

            var power = Hud.Sno.SnoPowers;
            var wave = me.Powers.GetUsedSkill(power.Monk_WaveOfLight);
            var cyclone = me.Powers.GetUsedSkill(power.Monk_CycloneStrike);
            SampleCycloneEffects(me);
            var waveKey = wave == null ? Keys.None : Binding(wave.Key);
            var cycloneKey = cyclone == null ? Keys.None : Binding(cyclone.Key);
            bool assistKeyHeld = AssistKey != Keys.None && AssistKey != waveKey && Held(AssistKey);
            if (!assistKeyHeld) _waitForAssistRelease = false;
            bool assist = assistKeyHeld && !_waitForAssistRelease;
            if (!assist && _assistHeld)
            {
                ReportWave("Space released", me, wave);
                LeaveAssist();
            }
            if (waveKey == Keys.None)
            {
                ReportWave("Wave of Light unavailable", me, wave);
                CancelInput();
                return;
            }

            if (_world != me.WorldId || _hero != me.HeroId)
            {
                CancelInput();
                _world = me.WorldId;
                _hero = me.HeroId;
                _packTick = int.MinValue;
                _lineSamples.Clear();
                _lineObstacles.Clear();
                _lineObstaclesTick = int.MinValue;
                _lastObstructedElite = _obstructionRouteTarget = 0;
                _obstructionRouteGoal = _obstructionRouteFocus = null;
                _obstructionRouteAt = _lastObstructionTry = int.MinValue;
                _failedCircle = 0;
                ClearStage();
                _stageLastAt = int.MinValue;
                _stageLastCircle = _stageLastElite = _lastCircleWalkId = 0;
                _lastCircleWalkAt = int.MinValue;
                _lastRelay = _lastEliteApproach = _lastEpiphany = _lastCursorConflict = int.MinValue;
                _bells.Clear();
                _previousBells.Clear();
                ResetBellAim();
                ClearBellRecovery();
                _lastWalk = _shoulderTry = int.MinValue;
                _pursuitId = _circleRecoveryId = _lastCircleLandingId = 0;
                _lastCircleLandingAt = int.MinValue;
                _lastCircleLandingCenter = null;
                _pursuitHops = 0;
                _usefulCircleSeen = false;
                _lastDash = _lastCircleCheck = _lastEscape = _lastDashBuffTry = _lastDashPress = int.MinValue;
                _lastSerenityTry = _postCirclePullUntil = int.MinValue;
                _postCirclePullId = 0;
                _serenityPending = false;
                ClearDanger();
            }
            _ownsAutoCyclone = CoordinateAutoSkillCyclone && cycloneKey != Keys.None
                && cycloneKey != waveKey && cycloneKey != AssistKey;
            if (_ownsAutoCyclone) BindAutoSkillCyclone(Environment.TickCount);
            bool manual = !assist && cyclone != null && Held(waveKey);
            if (!assist && !manual && _holdActive) CancelInput();
            ReleaseFinishedClaim();
            if (_autoLootYieldPending)
            {
                bool urgent = assist && me.FloorCoordinate != null
                    && (DangerAt(me.FloorCoordinate, landing: false) != null
                        || Age(_panicUntil, Environment.TickCount) > 0
                            && me.Defense != null && me.Defense.HealthMax > 0
                            && me.Defense.HealthCur / me.Defense.HealthMax <= .40f);
                if (urgent && _autoLoot != null)
                    _autoLoot.CancelPendingPickupInput();
                else if (_autoLoot != null && _autoLoot.Enabled && _autoLoot.HasPendingPickupInput
                    || game.CurrentGameTick == _autoLootYieldTick) return;
                _autoLootYieldPending = false;
            }
            TryEpiphany(me, power.Monk_Epiphany, waveKey, cycloneKey);
            if (!assist && !manual)
            {
                if (cyclone != null && Volatile.Read(ref _epiphanyKey) == 0)
                    TryShoulders(game, me, cyclone, cycloneKey, waveKey, Environment.TickCount);
                return;
            }
            if (!_holdActive || (assist && !_assistHeld))
            {
                _holdActive = true;
                _lastCircleCheck = int.MinValue;
            }
            _assistHeld = assist;
            if (cyclone != null) RunHeld(game, me, waveKey, cyclone, cycloneKey, assist);
            ReleaseFinishedClaim();
            if (assist && Volatile.Read(ref _epiphanyKey) == 0) ServiceWave(game, me, wave, waveKey, cyclone != null && cycloneKey != Keys.None
                && cycloneKey != waveKey && cycloneKey != AssistKey);
        }

        private void RunHeld(IGameController game, IPlayer me, Keys waveKey, IPlayerSkill cyclone,
            Keys cycloneKey, bool assist)
        {
            int now = Environment.TickCount;
            var point = me.FloorCoordinate;
            if (point == null) return;
            if (_walkOrigin != null && (point.XYDistanceTo(_walkOrigin) > 2 || Age(now, _lastWalk) >= 225))
            {
                Trace("walk progress=" + point.XYDistanceTo(_walkOrigin).ToString("F1"));
                _walkOrigin = null;
            }
            if (assist) RefreshPack(game, me, point);
            var dash = MaintainDashBuff || assist
                ? me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_DashingStrike) : null;
            var dashKey = dash == null ? Keys.None : Binding(dash.Key);
            // A charged Dash can be available independently of the native Spirit cost.
            bool dashReady = dash != null && dashKey != Keys.None && dashKey != waveKey
                && dashKey != cycloneKey && dashKey != AssistKey && !Held(dashKey) && dash.Charges > 0 && !dash.IsOnCooldown;
            int pullRange = cyclone.Rune == 1 ? 34 : 24;
            CollectBells(game, me, pullRange);
            bool recoveringBells = assist && BellNeedsRecovery(point, ClosestElite(point));
            bool bellCountReady = _bells.Count >= BellThreshold && cycloneKey != Keys.None
                && cycloneKey != waveKey && cycloneKey != AssistKey;
            bool recoveryBurst = recoveringBells && !_recoveryDetonationSent
                && cycloneKey != Keys.None && cycloneKey != waveKey && cycloneKey != AssistKey
                && _bells.Count > 0 && CanCast(me, cyclone);
            if (assist && (bellCountReady || recoveryBurst) && !BurstYielding(now)) StopWave();
            float health = me.Defense != null && me.Defense.HealthMax > 0
                ? me.Defense.HealthCur / me.Defense.HealthMax : 1f;
            if (assist) UpdateDanger(game, me, health, now);
            if (_serenityPending)
            {
                if (_serenityProtected)
                {
                    _serenityPending = false;
                    ReportSerenity(me, "buff confirmed; maintain combat position");
                }
                else if (Age(now, _lastSerenityTry) < 110) return; // Briefly allow the buff to appear.
                else
                {
                    _serenityPending = false;
                    Trace("proactive Serenity unconfirmed; normal avoidance restored");
                }
            }
            if (assist && TryProactiveSerenity(game, me, point, pullRange, waveKey, cycloneKey, dashKey, now)) return;
            bool holy = me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements.Sno, 4);
            string danger = assist ? DangerAt(point, landing: false) : null;
            bool panic = assist && AvoidDanger && Age(_panicUntil, now) > 0 && health <= .40f;
            if (_panicRecovering && health >= .60f && danger == null) _panicRecovering = false;
            if (_dashFlight != null)
            {
                var flight = _dashFlight;
                bool reached = flight.CircleId != 0
                    ? point.XYDistanceTo(flight.CircleCenter) <= CircleRadius - .5f
                    : point.XYDistanceTo(flight.Landing) <= (flight.Escape || flight.RelayOut || flight.RelayReturn ? 4f : 8f);
                // A primary-circle landing is complete even if the optional overlap was missed.
                bool overlapReached = flight.OverlapCenter == null
                    || point.XYDistanceTo(flight.OverlapCenter) <= CircleRadius - .5f;
                float moved = point.XYDistanceTo(flight.From);
                bool freshDash = game.CurrentGameTick != flight.Tick
                    && me.LoopingAnimationStartTick > flight.AnimationStart
                    && Convert.ToString(me.Animation).IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0;
                flight.AnimationObserved |= freshDash;
                flight.ChargeObserved |= dash != null && dash.Charges < flight.ChargesBefore;
                bool progressed = moved > 1.5f
                    && point.XYDistanceTo(flight.Landing) + 1f < flight.From.XYDistanceTo(flight.Landing);
                flight.DashAccepted |= flight.AnimationObserved || flight.ChargeObserved || progressed;
                if (flight.DashAccepted && (reached || point.XYDistanceTo(flight.From) > 1.5f)
                    && ReferenceEquals(_activeAim, flight))
                    ReleaseInput(Volatile.Read(ref _inputSerial));
                reached &= flight.DashAccepted;
                int deadline = flight.DashAccepted ? 260 : DashObservationMs;
                if (reached || Age(now, flight.SentAt) >= deadline)
                {
                    if (ReferenceEquals(_activeAim, flight)) ReleaseInput(Volatile.Read(ref _inputSerial));
                    _dashFlight = null;
                    Trace((reached ? flight.Reason + " landing observed" : flight.Reason + " landing unconfirmed")
                        + " landingGap=" + point.XYDistanceTo(flight.Landing).ToString("F1")
                        + " moved=" + moved.ToString("F1") + " accepted=" + flight.DashAccepted);
                    ReportDashSignals(flight, dash, me);
                    if (reached && !overlapReached)
                        Trace("Dash primary circle reached; overlap not reached (no extra Dash)");
                    if (reached && flight.CircleId != 0 && _circleRecoveryId == flight.CircleId)
                        _circleRecoveryId = 0;
                    if (reached && flight.CircleId != 0 && flight.CircleCenter != null
                        && !flight.Escape && !flight.StageOut && !flight.StageReturn)
                    {
                        _lastCircleLandingId = flight.CircleId;
                        _lastCircleLandingCenter = flight.CircleCenter;
                        _lastCircleLandingAt = now;
                        _postCirclePullId = flight.CircleId;
                        _postCirclePullUntil = unchecked(now + 320);
                    }
                    if (reached && flight.PursuitId != 0)
                    {
                        _pursuitId = flight.PursuitId;
                        _pursuitHops++;
                        _pursuitLast = now;
                    }
                    if (flight.EliteId != 0)
                        Trace("elite approach landing closeElites=" + CountPack(point, pullRange).CloseElites
                            + " actor=" + flight.EliteId);
                    if (reached && flight.Escape)
                    {
                        _lastDash = int.MinValue;
                        _lastCircleCheck = int.MinValue;
                    }
                    if (flight.RelayOut)
                    {
                        _relayAtStage = reached;
                        if (!reached) _relayHome = null;
                    }
                    if (flight.StageOut && _stagePhase == CircleStage.Outbound)
                    {
                        if (reached && point.XYDistanceTo(flight.Landing) <= 4f)
                        { _stagePhase = CircleStage.Attacking; Trace("circle attack stage landed"); }
                        else { Trace("circle attack stage not reached"); ClearStage(); }
                    }
                    if (flight.StageReturn && _stagePhase == CircleStage.Returning)
                    {
                        Trace(reached ? "circle attack stage returned" : "circle attack stage return unconfirmed");
                        ClearStage();
                        if (reached) _lastCircleCheck = int.MinValue;
                    }
                    else if (!reached && flight.CircleId != 0)
                    {
                        // A late movement/charge response must never trigger an immediate second Dash.
                        bool retrySafe = !flight.DashAccepted && moved <= 1.5f && !flight.AnimationObserved
                            && dash != null && dash.Charges == flight.ChargesBefore;
                        if (retrySafe && !flight.Retried && !flight.Escape && !flight.RelayReturn && dashReady
                            && danger == null && !panic && !_panicRecovering
                            && TryRetryCircle(game, point, flight, pullRange, now)) return;
                        if (!retrySafe) Trace("circle Dash retry suppressed: movement/charge/animation observed");
                        SetCircleRecovery(flight, now);
                    }
                }
            }
            if (_dashFlight != null || _releasedCycloneAim != null) return;
            bool escapeDue = danger != null || panic;
            if (escapeDue)
            {
                _relayHome = null;
                ClearStage();
                if (_dashAim != null && !_dashAim.Escape) CancelDashAim();
            }
            if (_walkOrigin != null && !escapeDue) return;
            int pressed = Volatile.Read(ref _pressedKey);
            if (escapeDue && _dashAim == null && !InputBusy()
                && (pressed == 0 || pressed == (int)cycloneKey)
                && (_lastEscape == int.MinValue || Age(now, _lastEscape) >= (danger != null ? 240 : 1100)))
            {
                var landing = PickEscape(game, me, pullRange, panic && danger == null, health <= .40f, dashReady, out var safeCircle);
                if (landing != null)
                {
                    // Keep an active burst unless an escape movement is actually queued.
                    if (dashReady && QueueDashAim(game, landing, danger ?? "low-health rapid loss", true, now))
                    {
                        if (safeCircle != null)
                        {
                            _dashAim.CircleId = safeCircle.AcdId;
                            _dashAim.CircleCenter = safeCircle.FloorCoordinate.Offset(0, 0, 0);
                        }
                        return;
                    }
                    if (_walkOrigin == null && (_lastWalk == int.MinValue || Age(now, _lastWalk) >= 225))
                    {
                        var waypoint = MapWaypoint("path", point, landing, now);
                        if (waypoint != null && DangerAt(waypoint, landing: true) == null
                            && QueueDashAim(game, waypoint, "avoidance walk", true, now))
                        {
                            _dashAim.Walk = true;
                            _dashAim.Goal = landing;
                            _lastEscape = now;
                            return;
                        }
                    }
                }
            }
            if (assist && HealInPanic && health > 0 && health <= .40f && (escapeDue || _panicRecovering)
                && _dashAim == null && (pressed == 0 || pressed == (int)cycloneKey)
                && TryPanicPotion(me, waveKey, cycloneKey, dashKey, now)) return;
            // Let defensive movement run above, but do not start an offensive action during Epiphany's key pulse.
            if (Volatile.Read(ref _epiphanyKey) != 0) return;
            pressed = Volatile.Read(ref _pressedKey);
            if (_burstStarted != int.MinValue)
            {
                if (pressed == (int)cycloneKey)
                {
                    // The release task owns the timed hold. This is only a bounded failsafe.
                    if (Age(now, _burstStarted) < _burstHoldMs + 250) return;
                    ReleaseInput(Volatile.Read(ref _inputSerial));
                    pressed = Volatile.Read(ref _pressedKey);
                }
                if (pressed != 0 || _releasedCycloneAim != null) return;

                Trace("bell burst ended after " + Age(now, _burstStarted) + "ms remaining=" + _bells.Count
                    + " heldMs=" + _burstHoldMs + " reason=continuous hold released");
                ReportCycloneEffects();
                if (_bells.Count >= BellThreshold) _burstYieldUntil = unchecked(now + BurstYieldMs);
                _burstStarted = int.MinValue;
                _burstKey = 0;
                _burstHoldMs = 0;
                if (assist && _stagePhase == CircleStage.Attacking) _stageBursts++;
            }
            if (pressed != 0) return;

            bool batchReady = bellCountReady;
            bool burstReady = batchReady && !BurstYielding(now) && CanCast(me, cyclone);
            ReportCombat(assist, me, dash, holy, now);

            if (_dashAim != null)
            {
                var aim = _dashAim;
                if (aim.Walk)
                {
                    AdvanceWalk(game, point, aim, now);
                    return;
                }
                bool stillAiming = StillAiming(aim);
                if (!dashReady || !stillAiming || Age(now, aim.Started) > 220
                    || (assist && DangerAt(aim.Landing, landing: true) != null)
                    || (!aim.Upkeep && (point.XYDistanceTo(aim.Landing) > DashRange || !MapAllows(aim.Landing, now)))
                    || (aim.Escape ? !escapeDue : aim.Upkeep
                        ? (assist ? !HasEnemyNear(game, aim.Landing, pullRange) : ManualMovementActive(me))
                        : aim.RelayOut ? _relayHome == null
                            || (_relayHome.CircleId != 0 && !IsLiveCircle(game, _relayHome.CircleId))
                            || !RelayStageClear(aim.Landing, pullRange)
                            || RelayTrash(aim.Landing, _relayHome.Landing, pullRange) < 2
                        : aim.RelayReturn ? aim.CircleId != 0 && !IsLiveCircle(game, aim.CircleId)
                        : aim.Transit ? !TransitStillUseful(game, point, aim)
                        : aim.StageOut ? !StageOutboundStillUseful(game, aim)
                        : aim.StageReturn ? !IsLiveCircle(game, aim.CircleId)
                        : aim.EliteId != 0 ? !EliteApproachStillUseful(point, aim)
                        : !CircleStillUseful(game, point, aim, pullRange)))
                {
                    string why = !dashReady ? "dash unavailable" : !stillAiming ? "cursor changed" : "landing no longer valid";
                    if (!stillAiming)
                    {
                        _lastCursorConflict = now;
                        ReportCursorConflict(aim);
                    }
                    SetCircleStatus(why);
                    Trace("Dash canceled for " + aim.Reason + ": " + why);
                    if (aim.RelayOut) _relayHome = null;
                    if (aim.StageOut || aim.StageReturn) ClearStage();
                    CancelDashAim();
                }
                else if (game.CurrentGameTick != aim.Tick && !InputBusy())
                {
                    int animationStart = me.LoopingAnimationStartTick;
                    if (!aim.Escape && aim.ReleasedWave && Age(now, aim.Started) < 35) return;
                    if (aim.ReleasedWaveKey != 0 && Held((Keys)aim.ReleasedWaveKey)) return;
                    if (!aim.Escape && _lastDashPress != int.MinValue
                        && Age(now, _lastDashPress) < DashChainGuardMs) return;
                    if (!DashAimReady(game, aim)) return;
                    int holdMs = DashTapMs;
                    aim.HoldMs = holdMs;
                    if (BeginInput(dashKey, holdMs, aim, ownDash: true))
                    {
                        aim.From = point.Offset(0, 0, 0);
                        aim.ChargesBefore = dash.Charges;
                        aim.AnimationStart = animationStart;
                        aim.Tick = game.CurrentGameTick;
                        _lastDashPress = aim.SentAt = now;
                        if (aim.Escape)
                        {
                            _lastEscape = now;
                            if (health <= .40f) _panicRecovering = true;
                            _panicUntil = now;
                        }
                        else if (!aim.Upkeep) _lastDash = now;
                        _dashFlight = aim;
                        if (aim.RelayReturn) _relayHome = null;
                        _dashAim = null;
                        Trace("Dash pressed for " + aim.Reason + " holdMs=" + holdMs
                            + " animation=" + me.AnimationState + " releaseWaitMs=" + Age(now, aim.Started)
                            + " charges=" + aim.ChargesBefore + " travel=" + point.XYDistanceTo(aim.Landing).ToString("F1"));
                    }
                    else
                    {
                        SetCircleStatus("Dash press declined");
                        Trace("Dash press declined for " + aim.Reason);
                        if (aim.RelayOut) _relayHome = null;
                        if (aim.StageOut || aim.StageReturn) ClearStage();
                        SetCircleRecovery(aim, now);
                        CancelDashAim();
                    }
                }
                return;
            }

            if (assist && _relayHome != null && AdvanceRelay(game, me, cyclone, cycloneKey, pullRange, dashReady,
                holy && HoldPositionDuringHoly, now)) return;

            if (assist && _stagePhase != CircleStage.None && AdvanceCircleStage(game, point, pullRange, dashReady, now)) return;

            // Repeated misplaced spawns override circle scoring, but never hazard evasion.
            if (recoveringBells)
            {
                if (recoveryBurst && !BurstYielding(now)
                    && StartBellBurst(game, cycloneKey, now, holy, assist))
                {
                    _recoveryDetonationSent = true;
                    Trace("bell recovery Cyclone triggered below standard bell threshold: count=" + _bells.Count);
                    return;
                }
                if (!escapeDue && !_panicRecovering && _stagePhase == CircleStage.None
                    && _dashFlight == null && !InputBusy()
                    && (_lastBellRecoveryTry == int.MinValue || Age(now, _lastBellRecoveryTry) >= 300))
                {
                    _lastBellRecoveryTry = now;
                    var elite = ClosestElite(point);
                    if (elite.Id != 0)
                    {
                        var goal = EliteGroundGoal(point, elite);
                        if (point.XYDistanceTo(goal) >= 8f)
                        {
                            var plan = new Aim { Landing = goal, Goal = goal, EliteId = elite.Id,
                                PursuitId = elite.Id, Reason = "misplaced bell approach" };
                            if (TryTravel(game, point, plan, dashReady, now))
                            {
                                _lastEliteApproach = now;
                                return;
                            }
                        }
                        // Change angle if direct approach is unsafe or unavailable.
                        float dx = elite.X - point.X, dy = elite.Y - point.Y;
                        float length = Math.Max(1f, point.XYDistanceTo(elite.Point));
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var flank = point.Offset(side * dy * 9f / length,
                                -side * dx * 9f / length, 0);
                            var plan = new Aim { Landing = flank, Goal = flank,
                                EliteId = elite.Id, PursuitId = elite.Id, Reason = "misplaced bell flank" };
                            if (!TryTravel(game, point, plan, dashReady, now)) continue;
                            _lastEliteApproach = now;
                            return;
                        }
                    }
                }
                // If no recovery move was queued, continue the ordinary bell cycle
                // with self aim. Failed navigation must not create an idle owner.
            }

            if (assist && _postCirclePullId != 0 && !escapeDue && !recoveringBells)
            {
                if (TryCircleArrivalPull(game, me, point, cyclone, cycloneKey, pullRange, now)) return;
            }

            if (assist && !recoveringBells && CanCheckCircle(!escapeDue, escapeDue, now)
                && TryAimCircle(game, point, pullRange, now, cyclone, cycloneKey, dashReady,
                    releaseBurst: bellCountReady)) return;

            if (bellCountReady && !BurstYielding(now) && (assist || burstReady))
            {
                if (burstReady) StartBellBurst(game, cycloneKey, now, holy, assist);
                return;
            }

            if (assist && _stagePhase == CircleStage.None && !escapeDue && !_panicRecovering
                && _dashFlight == null && TryResolveObstructedFire(game, point, pullRange, dashReady, now)) return;

            if (assist && _stagePhase == CircleStage.None && !escapeDue && dashReady && dash.Charges >= 2
                && TryStartCircleStage(game, point, pullRange, now)) return;

            if (assist && !escapeDue && !_panicRecovering && _stagePhase == CircleStage.None
                && _dashFlight == null && !InputBusy()
                && (_lastEliteApproach == int.MinValue || Age(now, _lastEliteApproach) >= 300))
            {
                var elite = ClosestElite(point);
                if (elite.Id != 0 && point.XYDistanceTo(elite.Point) - elite.Radius > EliteNear
                    && !HoldRangedCircle(game, point, pullRange))
                {
                    var goal = EliteGroundGoal(point, elite);
                    var plan = new Aim { Landing = goal, Goal = goal, EliteId = elite.Id,
                        PursuitId = elite.Id, Reason = "elite approach" };
                    if (TryTravel(game, point, plan, dashReady, now))
                    {
                        _lastEliteApproach = now;
                        return;
                    }
                }
            }

            bool defensiveDash = health <= DefensiveDashHealth || (assist && HasDashThreat(point));
            if (assist && defensiveDash && MaintainDashBuff && !escapeDue && dashReady
                && DangerAt(point, landing: true) == null && DashBuffDue(me, dash, now, assist: false))
            {
                _lastDashBuffTry = now;
                if (QueueDashAim(game, point, "defensive Dash buff upkeep", false, now, upkeep: true)) return;
            }

            if (assist && RelayDistantTrash && !_usefulCircleSeen && !(holy && HoldPositionDuringHoly) && !batchReady && !escapeDue && !_panicRecovering
                && _stagePhase == CircleStage.None && _dashFlight == null && dashReady && dash.Charges >= 2
                && (_lastRelay == int.MinValue || Age(now, _lastRelay) >= 6000)
                && TryStartRelay(game, me, cyclone, cycloneKey, pullRange, now)) return;

            if (!assist && MaintainDashBuff && dashReady && !ManualMovementActive(me)
                && DashBuffDue(me, dash, now, assist: false))
            {
                _lastDashBuffTry = now;
                if (QueueDashAim(game, point, "Dash buff upkeep", false, now, upkeep: true)) return;
            }
            TryShoulders(game, me, cyclone, cycloneKey, waveKey, now);
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void BeginCycloneEffects(IPlayer me)
        {
#if WOL_DIAGNOSTIC
            _burstEffectSamples = _burstEffectBuffRefreshes = _burstEffectAnimStarts = _burstEffectSpiritDrops = 0;
            _burstLastAnimationTick = me.LoopingAnimationStartTick;
            _burstLastSpirit = me.Stats.ResourceCurPri;
            _burstCycloneCost = me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_CycloneStrike)?.GetResourceRequirement() ?? 0f;
            var buff = me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_CycloneStrike.Sno);
            _burstLastBuffTime = ConventionLeft(buff, 0, 6);
            _burstBuffSeen = _burstLastBuffTime > 0;
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void SampleCycloneEffects(IPlayer me)
        {
#if WOL_DIAGNOSTIC
            if (_burstStarted == int.MinValue) return;
            _burstEffectSamples++;
            double remaining = ConventionLeft(me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_CycloneStrike.Sno), 0, 6);
            if (remaining > 0) _burstBuffSeen = true;
            if (remaining > _burstLastBuffTime + .045) _burstEffectBuffRefreshes++;
            _burstLastBuffTime = remaining;
            if (me.LoopingAnimationStartTick > _burstLastAnimationTick)
            {
                _burstEffectAnimStarts++;
                _burstLastAnimationTick = me.LoopingAnimationStartTick;
            }
            float spirit = me.Stats.ResourceCurPri;
            if (spirit < _burstLastSpirit - Math.Max(5f, _burstCycloneCost * .4f))
                _burstEffectSpiritDrops++;
            _burstLastSpirit = spirit;
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportCycloneEffects()
        {
#if WOL_DIAGNOSTIC
            Trace("Cyclone effects observed buffRefresh=" + _burstEffectBuffRefreshes
                + " animStarts=" + _burstEffectAnimStarts + " resourceDrops=" + _burstEffectSpiritDrops
                + " samples=" + _burstEffectSamples + " buffSeen=" + _burstBuffSeen
                + " skillCost=" + _burstCycloneCost.ToString("F1")
                + " (indirect signals, not a count of confirmed player/mimic casts)");
#endif
        }

        private bool BurstYielding(int now) => _burstYieldUntil != int.MinValue
            && Age(_burstYieldUntil, now) > 0;

        private bool StartBellBurst(IGameController game, Keys key, int now, bool holy, bool assist)
        {
            if (BurstYielding(now)) return false;
            bool epiphany = game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Epiphany.Sno);
            int holdMs = Math.Max(550, Math.Min(1100, CycloneHoldMs));
            if (!(assist ? CastCycloneAtFeet(game, key, holdMs, now, "bell burst")
                : BeginCycloneInput(key, holdMs, null))) return false;
            _burstStarted = now;
            _burstKey = (int)key;
            _burstHoldMs = holdMs;
            BeginCycloneEffects(game.Me);
            Trace("Cyclone continuous burst started, bells=" + _bells.Count + " holdMs=" + holdMs
                + " epiphany=" + epiphany + " phase=" + (holy ? "Holy" : "normal")
                + " mode=" + (assist ? "space" : "manual"));
            return true;
        }

        private static bool CanCast(IPlayer me, IPlayerSkill skill)
        {
            float cost = skill.GetResourceRequirement();
            return !skill.IsOnCooldown && cost >= 0 && me.Stats.ResourceCurPri >= cost;
        }

        private struct Body
        {
            internal uint Id;
            internal int Tier;
            internal float X, Y, Z, Distance, Radius;
            internal IWorldCoordinate Point;
            internal bool Pullable;
        }

        private struct Coverage
        {
            internal int Elites, CloseElites, RangedElites, StageElites, Trash, PullElites, PullTrash;
        }

        private void RefreshPack(IGameController game, IPlayer me, IWorldCoordinate origin)
        {
            if (_packTick == game.CurrentGameTick) return;
            _packTick = game.CurrentGameTick;
            _pack.Clear();
            if (game.AliveMonsters == null) return;
            foreach (var monster in game.AliveMonsters)
            {
                if (monster == null || monster.WorldId != me.WorldId || !monster.IsAlive
                    || !monster.Attackable || monster.Untargetable || monster.Illusion) continue;
                var at = monster.FloorCoordinate;
                if (at == null || !(Math.Abs(at.Z - origin.Z) <= 8f)) continue;
                float distance = at.XYDistanceTo(origin);
                if (float.IsNaN(distance) || float.IsInfinity(distance)) continue;
                int tier = monster.Rarity == ActorRarity.Boss ? 2
                    : (monster.IsElite || monster.Rarity == ActorRarity.Unique) && monster.Rarity != ActorRarity.RareMinion ? 1 : 0;
                if (tier == 0 && (distance > 85 || _pack.Count >= 160)) continue;
                float radius = monster.RadiusScaled;
                if (float.IsNaN(radius) || radius < 0 || radius > 30) radius = 0;
                _pack.Add(new Body { Id = monster.AcdId, Tier = tier, Point = at, Radius = radius,
                    X = at.X, Y = at.Y, Z = at.Z, Distance = distance,
                    Pullable = tier == 1 && PullableElite(monster) });
            }
        }

        private static bool PullableElite(IMonster monster)
        {
            var affixes = monster.AffixSnoList;
            if (affixes != null)
                foreach (var affix in affixes)
                    if (affix != null && affix.Affix == MonsterAffix.Juggernaut) return false;
            var pack = monster.Pack;
            if (pack?.AffixSnoList != null)
                foreach (var affix in pack.AffixSnoList)
                    if (affix != null && affix.Affix == MonsterAffix.Juggernaut) return false;
            return true;
        }

        private Coverage CountPack(IWorldCoordinate point, int radius)
        {
            var coverage = new Coverage();
            int limit = radius * radius;
            foreach (var body in _pack)
            {
                float dx = point.X - body.X, dy = point.Y - body.Y;
                float gapSquared = dx * dx + dy * dy;
                if (Math.Abs(point.Z - body.Z) > 8f) continue;
                if (body.Tier > 0)
                {
                    float close = EliteNear + body.Radius;
                    bool closeEnough = gapSquared <= close * close;
                    if (closeEnough) coverage.CloseElites++;
                    if (closeEnough || gapSquared <= limit) coverage.Elites++;
                    if (!closeEnough && body.Pullable && gapSquared <= limit) coverage.PullElites++;
                    float waveRadius = WaveReach + body.Radius;
                    float stageRadius = StageMaxRange + body.Radius;
                    if (gapSquared <= waveRadius * waveRadius) coverage.RangedElites++;
                    else if (body.Pullable && gapSquared <= stageRadius * stageRadius) coverage.StageElites++;
                }
                else if (gapSquared <= limit)
                {
                    coverage.Trash++;
                    if (gapSquared > 121f) coverage.PullTrash++;
                }
            }
            return coverage;
        }

        private bool SetSelfAim(Cursor aimed)
        {
            if (!GetCursorPos(out var current) || !Hud.Window.IsForeground) return false;
            var window = GetForegroundWindow();
            if (!_hasSnap || _snapWindow != window || !NearCursor(current, _lastSnap, 12))
            {
                _snapOriginal = current;
                _snapWindow = window;
            }
            if (window == IntPtr.Zero || (!NearCursor(current, aimed, 2) && !SetCursorPos(aimed.X, aimed.Y))) return false;
            _lastSnap = NearCursor(current, aimed, 2) ? current : aimed;
            _hasSnap = true;
            return true;
        }

        private void ServiceWave(IGameController game, IPlayer me, IPlayerSkill wave, Keys waveKey, bool canCycleBells)
        {
            string blocked = null;
            if (!Held(AssistKey)) blocked = "Space released";
            else if (_dashAim != null || _activeAim != null || _dashFlight != null)
                blocked = "movement owns cursor";
            else if (_cycloneSelfAim != null) blocked = "Cyclone self aim";
            else if (_walkOrigin != null) blocked = "walk handoff";
            else if (_burstStarted != int.MinValue) blocked = "Cyclone pulse burst";
            else if (_serenityPending || Volatile.Read(ref _pressedKey) != 0) blocked = "skill pulse owns input";
            else if (InputBusy()) blocked = "another plugin owns input";
            else if (me.FloorCoordinate == null) blocked = "no player coordinate";
            else if (canCycleBells && _bells.Count >= BellThreshold && !BurstYielding(Environment.TickCount))
                blocked = "four bells ready, Cyclone next";
            else if (_releasedCycloneAim == null) blocked = AimAtSelf(game, me, wave, waveKey);

            if (blocked != null)
            {
                StopWave();
                ReportWave(blocked, me, wave);
                return;
            }

            string state = HoldWave(waveKey);
            if (_bellAimTarget != 0 && _bellAimAt == int.MinValue
                && Volatile.Read(ref _wavePress) == (int)waveKey && CanCast(me, wave))
                _bellAimAt = Environment.TickCount;
            ReportWave(state, me, wave);
        }

        private bool BeginCycloneInput(Keys key, int holdMs, Aim aim)
        {
            return BeginInput(key, holdMs, aim);
        }

        private bool CastCycloneAtFeet(IGameController game, Keys key, int holdMs, int now, string reason)
        {
            if (key == Keys.None || _releasedCycloneAim != null || InputBusy() || Volatile.Read(ref _pressedKey) != 0
                || !ClaimMovement()) return false;
            StopWave();
            if (_cycloneSelfAim == null)
            {
                var origin = game.Me.FloorCoordinate;
                if (origin == null || !TryProject(origin, out var feet) || !GetCursorPos(out var original)) return false;
                if (!NearCursor(original, feet, 2) && !SetCursorPos(feet.X, feet.Y)) return false;
                _cycloneSelfAim = new Aim { Original = original, Aimed = feet,
                    Landing = origin.Offset(0, 0, 0), Window = GetForegroundWindow(), Started = now,
                    Tick = game.CurrentGameTick, Reason = reason, IsCycloneAim = true };
                return false; // Wait for a fresh HUD selection before casting with Epiphany.
            }
            if (_cycloneSelfAim.Tick == game.CurrentGameTick) return false;

            var aim = _cycloneSelfAim;
            if (!StillAiming(aim))
            {
                CancelCycloneAim();
                return false;
            }
            if (!BeginCycloneInput(key, holdMs, aim))
            {
                CancelCycloneAim();
                return false;
            }
            _cycloneSelfAim = null;
#if WOL_DIAGNOSTIC
            var hovered = game.SelectedActor;
            var hoveredPoint = hovered?.FloorCoordinate;
            Trace("Cyclone dispatched purpose=" + reason + " aimMs=" + Age(now, aim.Started)
                + " hoveredActor=" + (hovered?.AcdId ?? 0)
                + " hoverGap=" + (hoveredPoint == null ? -1f : game.Me.FloorCoordinate.XYDistanceTo(hoveredPoint)).ToString("F1")
                + " epiphany=" + game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Epiphany.Sno));
#endif
            return true;
        }

        private void FinishReleasedCyclone(int tick)
        {
            var aim = _releasedCycloneAim;
            if (aim == null || aim.Tick == tick) return;
            _releasedCycloneAim = null;
            RestoreAim(aim);
        }

        private void CancelCycloneAim()
        {
            RestoreAim(_cycloneSelfAim);
            _cycloneSelfAim = null;
        }

        private bool TryCircleArrivalPull(IGameController game, IPlayer me, IWorldCoordinate point,
            IPlayerSkill cyclone, Keys cycloneKey, int range, int now)
        {
            if (Age(_postCirclePullUntil, now) <= 0 || !IsLiveCircle(game, _postCirclePullId)
                || _bells.Count >= BellThreshold || _burstStarted != int.MinValue
                || _dashAim != null || _dashFlight != null || _stagePhase != CircleStage.None
                || InputBusy() || _serenityPending)
            { _postCirclePullId = 0; return false; }

            var coverage = CountPack(point, range);
            float cycloneCost = Math.Max(0f, cyclone.GetResourceRequirement());
            float waveCost = Math.Max(0f, me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_WaveOfLight)?.GetResourceRequirement() ?? 0f);
            // A setup pull must not consume the Spirit reserved for the next bell rotation.
            if ((coverage.PullElites == 0 && coverage.PullTrash < 4)
                || me.Stats.ResourceCurPri < waveCost + 3f * cycloneCost || !CanCast(me, cyclone))
            { _postCirclePullId = 0; return false; }

            if (CastCycloneAtFeet(game, cycloneKey, DashPulseMs, now, "circle arrival pull"))
            {
                Trace("circle arrival pull dispatched elites=" + coverage.PullElites
                    + " trash=" + coverage.PullTrash + " spirit=" + me.Stats.ResourceCurPri.ToString("F0"));
                _postCirclePullId = 0;
                return true;
            }
            if (_cycloneSelfAim != null) return true; // The cursor is being verified on the next HUD tick.
            _postCirclePullId = 0;
            return false;
        }

        private bool OccupiesCombatCircle(IGameController game, IWorldCoordinate point, int pullRange)
        {
            var focus = ClosestElite(point);
            if (!CoverageEnough(CircleCoverage(point, pullRange), NearElite(point))
                || !CanDamageUnpullable(point, focus, pullRange) || game.Actors == null) return false;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.IsDisabled || actor.WorldId != game.Me.WorldId
                    || actor.FloorCoordinate == null || actor.AcdId == 0) continue;
                if (Math.Abs(actor.FloorCoordinate.Z - point.Z) <= 8f
                    && point.XYDistanceTo(actor.FloorCoordinate) <= CircleRadius - .5f
                    && CircleKind(actor) != 0) return true;
            }
            return false;
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportSerenity(IPlayer me, string state)
        {
#if WOL_DIAGNOSTIC
            var counts = me.Powers.GetBuff(Hud.Sno.SnoPowers.SquirtsNecklace.Sno)?.IconCounts;
            Trace("proactive Serenity " + state + " SquirtStacks="
                + (counts == null || counts.Length == 0 ? -1 : counts.Max()));
#endif
        }

        private bool TryProactiveSerenity(IGameController game, IPlayer me, IWorldCoordinate point,
            int pullRange, Keys waveKey, Keys cycloneKey, Keys dashKey, int now)
        {
            if (!AvoidDanger || _shielded || _serenityPending || InputBusy()
                || _dashFlight != null || _dashAim?.Escape == true || _activeAim?.Escape == true
                || Volatile.Read(ref _epiphanyKey) != 0
                || (_lastSerenityTry != int.MinValue && Age(now, _lastSerenityTry) < 500)) return false;

            string threat = null;
            foreach (var hazard in _hazards)
                if (hazard.Imminent && (hazard.Reason == "Molten" || hazard.Reason == "Grotesque")
                    && InsideHazard(point, hazard, 0f))
                { threat = hazard.Reason; break; }
            if (threat == null) return false;

            var power = Hud.Sno.SnoPowers.Monk_Serenity;
            var skill = me.Powers.GetUsedSkill(power);
            if (skill == null || skill.IsOnCooldown || skill.BuffIsActive
                || !OccupiesCombatCircle(game, point, pullRange)) return false;
            var key = Binding(skill.Key);
            if (key == Keys.None || key == AssistKey || key == waveKey || key == cycloneKey
                || key == dashKey || Held(key)) return false;

            StopWave();
            if (Volatile.Read(ref _pressedKey) != 0) ReleaseInput(Volatile.Read(ref _inputSerial));
            if (_dashAim != null) CancelDashAim();
            if (_cycloneSelfAim != null) CancelCycloneAim();
            if (!BeginInput(key, DashPulseMs, null)) return false;
            if (_burstStarted != int.MinValue)
            {
                ReportCycloneEffects();
                _burstStarted = int.MinValue;
                _burstKey = _burstHoldMs = 0;
                _burstYieldUntil = int.MinValue;
            }
            _lastSerenityTry = now;
            _serenityPending = true;
            ReportSerenity(me, "pressed threat=" + threat + " circleOccupied=true"
                + " spirit=" + me.Stats.ResourceCurPri.ToString("F0"));
            return true;
        }

        private void TryShoulders(IGameController game, IPlayer me, IPlayerSkill cyclone, Keys key, Keys waveKey, int now)
        {
            if (key == Keys.None || key == waveKey || key == AssistKey || _dashAim != null || _dashFlight != null
                || _burstStarted != int.MinValue
                || (_cycloneSelfAim != null && _cycloneSelfAim.Reason != "Lefebvre upkeep")
                || (_shoulderTry != int.MinValue && Age(now, _shoulderTry) < 350) || !CanCast(me, cyclone)) return;
            var sno = Hud.Sno.SnoPowers;
            if (!me.Powers.BuffIsActive(sno.LefebvresSoliloquy.Sno, 0)) return;
            var buff = me.Powers.GetBuff(sno.Monk_CycloneStrike.Sno);
            if (ConventionLeft(buff, 0, 6) > .55) return;
            if (_assistHeld ? CastCycloneAtFeet(game, key, DashPulseMs, now, "Lefebvre upkeep")
                : BeginCycloneInput(key, DashPulseMs, null)) _shoulderTry = now;
        }

        private bool ManualMovementActive(IPlayer me)
        {
            return me.AnimationState == AcdAnimationState.Running || Held(ForceMoveKey)
                || Held(Keys.LButton) && Hud.Game.SelectedActor == null;
        }

        private void BindAutoSkillCyclone(int now)
        {
            if (_autoCycloneWrapper != null || (_autoCycloneBindAt != int.MinValue && Age(now, _autoCycloneBindAt) < 2000)) return;
            _autoCycloneBindAt = now;
            var plugin = Hud.AllPlugins.FirstOrDefault(item => item.Enabled && item.GetType().Name == "s7o_AutoSkill");
            if (plugin == null) return;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            try
            {
                var profiles = plugin.GetType().GetField("_conditionalProfiles", flags)?.GetValue(plugin) as System.Collections.IEnumerable;
                if (profiles == null) return;
                foreach (var profile in profiles)
                {
                    var type = profile?.GetType();
                    if (!Equals(type?.GetField("Code")?.GetValue(profile), "Skill_MonkCycloneStrike")) continue;
                    var field = type.GetField("ShouldCast");
                    var original = field?.GetValue(profile) as Delegate;
                    if (original == null || !field.FieldType.IsGenericType
                        || field.FieldType.GetGenericTypeDefinition() != typeof(Func<,>)
                        || field.FieldType.GetGenericArguments()[1] != typeof(bool)) return;
                    var factory = GetType().GetMethod(nameof(WrapCyclonePolicy), flags);
                    var wrapped = (Delegate)factory.MakeGenericMethod(field.FieldType.GetGenericArguments()[0])
                        .Invoke(this, new object[] { original });
                    field.SetValue(profile, wrapped);
                    _autoCycloneWrapper = wrapped;
                    Trace("AutoSkill Cyclone handed off for equipped Wave of Light");
                    return;
                }
            }
            catch { } // Optional bridge. The standalone retains its own input checks.
        }

        private Func<T, bool> WrapCyclonePolicy<T>(Func<T, bool> original)
        {
            return ctx =>
            {
                if (Enabled && CoordinateAutoSkillCyclone && _ownsAutoCyclone && Hud.Window.IsForeground)
                {
                    var powers = Hud.Game.Me?.Powers;
                    var sno = Hud.Sno.SnoPowers;
                    if (powers?.GetUsedSkill(sno.Monk_WaveOfLight) != null
                        && powers.GetUsedSkill(sno.Monk_CycloneStrike) != null) return false;
                }
                return original(ctx);
            };
        }

        private bool HasDashThreat(IWorldCoordinate origin)
        {
            if (_shielded) return false;
            int arcane = 0;
            foreach (var hazard in _hazards)
            {
                float gap = origin.XYDistanceTo(hazard.Point);
                if (hazard.Reason == "Arcane core" && gap <= 28 && ++arcane >= 2) return true;
                if ((hazard.Reason.IndexOf("frozen", StringComparison.OrdinalIgnoreCase) >= 0
                    || hazard.Reason.IndexOf("chains", StringComparison.OrdinalIgnoreCase) >= 0
                    || hazard.Reason.IndexOf("grotesque", StringComparison.OrdinalIgnoreCase) >= 0
                    || hazard.Reason.IndexOf("molten", StringComparison.OrdinalIgnoreCase) >= 0)
                    && gap <= hazard.Radius + 7) return true;
            }
            return false;
        }

        private void ResetBellAim()
        {
            _bellAimTarget = 0;
            _bellAimPosition = _bellAimFocus = _bellAimOrigin = null;
            _bellAimSerial = int.MinValue;
            _bellAimAt = int.MinValue;
            _bellAimSlot = 0;
        }

        private void ClearBellRecovery()
        {
            _recoveryBellTarget = _misplacedBellTarget = 0;
            _recoveryBellOrigin = _recoveryBellFocus = null;
            _misplacedBellCount = 0;
            _lastMisplacedBell = _lastBellRecoveryTry = int.MinValue;
            _recoveryDetonationSent = false;
            _bellRecoveryUntil = int.MinValue;
        }

        private bool BellNeedsRecovery(IWorldCoordinate origin, Body focus)
        {
            if (_recoveryBellTarget == 0) return false;
            if (origin == null || focus.Id != _recoveryBellTarget || _recoveryBellOrigin == null
                || _recoveryBellFocus == null || origin.XYDistanceTo(_recoveryBellOrigin) >= 8f
                || focus.Point.XYDistanceTo(_recoveryBellFocus) >= 8f
                || _bellRecoveryUntil == int.MinValue || Age(Environment.TickCount, _bellRecoveryUntil) >= 0)
            {
                ClearBellRecovery();
                return false;
            }
            return true;
        }

        private void BeginBellRecovery(uint target, IWorldCoordinate origin, IWorldCoordinate focus,
            string reason)
        {
            if (target == 0 || origin == null || focus == null) return;
            _recoveryBellTarget = target;
            _bellRecoveryUntil = unchecked(Environment.TickCount + 1000);
            _recoveryBellOrigin = origin.Offset(0, 0, 0);
            _recoveryBellFocus = focus.Offset(0, 0, 0);
            _recoveryDetonationSent = _burstStarted != int.MinValue;
            ResetBellAim();
            StopWave();
            Trace("bell placement recovery target=" + target + " reason=" + reason);
        }

        private struct BellReach
        {
            internal int Slots, Reachable;
            internal float Nearest;
        }

        private bool HasOpenBellLane(IWorldCoordinate origin, Body focus, int pullRange, int now)
        {
            int slots = focus.Radius > 7f ? 1 : 4;
            for (int i = 0; i < slots; i++)
            {
                var bell = BellSlotPoint(origin, focus, i);
                if (origin.XYDistanceTo(bell) <= pullRange - 4f
                    && !ProjectileLineBlocked(origin, bell, now, out _)
                    && !ProjectileLineBlocked(bell, focus.Point, now, out _)) return true;
            }
            return false;
        }

        // Native geometry candidates are advisory; unknown coverage never blocks attacks.
        private bool TryResolveObstructedFire(IGameController game, IWorldCoordinate origin,
            int pullRange, bool dashReady, int now)
        {
            var focus = ClosestElite(origin);
            if (focus.Id == 0 || origin.XYDistanceTo(focus.Point) > WaveReach + focus.Radius
                || !ProjectileLineBlocked(origin, focus.Point, now, out var reason)
                || ((NeedsDirectBellContact(focus) || HoldRangedCircle(game, origin, pullRange))
                    && HasOpenBellLane(origin, focus, pullRange, now))) return false;
            StopWave();
            if (focus.Id != _lastObstructedElite || _lastObstructionLog == int.MinValue
                || Age(now, _lastObstructionLog) >= 1200)
            {
                _lastObstructedElite = focus.Id;
                _lastObstructionLog = now;
                Trace("WOL line obstructed elite=" + focus.Id + " reason=" + reason
                    + " gap=" + origin.XYDistanceTo(focus.Point).ToString("F1"));
            }
            if (_lastObstructionTry != int.MinValue && Age(now, _lastObstructionTry) < 300) return true;
            _lastObstructionTry = now;
            // Reuse one route destination: changing it resets MapViewer's asynchronous path search.
            if (_obstructionRouteTarget != focus.Id || _obstructionRouteGoal == null
                || Age(now, _obstructionRouteAt) >= 1600
                || _obstructionRouteFocus.XYDistanceTo(focus.Point) > 5f)
            {
                _obstructionRouteTarget = focus.Id;
                _obstructionRouteAt = now;
                _obstructionRouteGoal = null;
                _obstructionRouteFocus = focus.Point.Offset(0, 0, 0);
                IActor ring = CurrentEliteCircle(game, origin, focus);
                if (ring != null)
                {
                    var center = ring.FloorCoordinate;
                    float dx = focus.X - center.X, dy = focus.Y - center.Y;
                    float length = Math.Max(1f, center.XYDistanceTo(focus.Point));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rim = center.Offset(side * -dy * 6f / length, side * dx * 6f / length, 0);
                        if (rim.XYDistanceTo(origin) >= 3f && MapAllows(rim, now)
                            && !ProjectileLineBlocked(rim, focus.Point, now, out _))
                        { _obstructionRouteGoal = rim; break; }
                    }
                }
                if (_obstructionRouteGoal == null)
                {
                    float length = Math.Max(1f, origin.XYDistanceTo(focus.Point));
                    float stand = Math.Max(1f, Math.Min(length - 1f, focus.Radius + 3f));
                    _obstructionRouteGoal = focus.Point.Offset((origin.X - focus.X) * stand / length,
                        (origin.Y - focus.Y) * stand / length, 0);
                    if (_obstructionRouteGoal.XYDistanceTo(origin) < 3f)
                        _obstructionRouteGoal = focus.Point.Offset((focus.Y - origin.Y) * 7f / length,
                            (origin.X - focus.X) * 7f / length, 0);
                }
                Trace("WOL obstruction route goal=" + _obstructionRouteGoal.X.ToString("F1")
                    + "," + _obstructionRouteGoal.Y.ToString("F1") + " target=" + focus.Id);
            }
            var plan = new Aim { Landing = _obstructionRouteGoal, Goal = _obstructionRouteGoal,
                EliteId = focus.Id, PursuitId = focus.Id, Reason = "blocked WOL approach" };
            TryTravel(game, origin, plan, dashReady, now);
            return true;
        }

        private static IWorldCoordinate BellSlotPoint(IWorldCoordinate origin, Body focus, int slot)
        {
            float distance = origin.XYDistanceTo(focus.Point);
            float vx = distance < 1f ? 1f : (origin.X - focus.X) / distance;
            float vy = distance < 1f ? 0f : (origin.Y - focus.Y) / distance;
            float radius = Math.Max(4f, focus.Radius + 2f);
            float dx = slot == 0 ? vx : slot == 1 ? -vy : slot == 2 ? -vx : vy;
            float dy = slot == 0 ? vy : slot == 1 ? vx : slot == 2 ? -vy : -vx;
            return focus.Point.Offset(dx * radius, dy * radius, 0);
        }

        // Evaluate prospective circle landings geometrically. Projection is checked
        // only after moving, since future targets can currently be off-screen.
        private static BellReach BellReachFrom(IWorldCoordinate origin, Body focus, int pullRange)
        {
            var reach = new BellReach { Slots = focus.Radius > 7f ? 1 : 4, Nearest = float.MaxValue };
            if (origin == null || focus.Id == 0
                || Math.Abs(origin.Z - focus.Z) > 8f) return reach;
            for (int slot = 0; slot < reach.Slots; slot++)
            {
                float distance = origin.XYDistanceTo(BellSlotPoint(origin, focus, slot));
                if (distance < reach.Nearest) reach.Nearest = distance;
                if (distance <= pullRange - 4f) reach.Reachable++;
            }
            return reach;
        }

        // Sanctified bells spawn at the cursor target. Skip unreachable pattern
        // positions; keep the first feasible slot within Cyclone's safety margin.
        private IWorldCoordinate BellAttackPoint(IGameController game, IWorldCoordinate origin,
            int pullRange, bool canObserveCast)
        {
            var focus = ClosestElite(origin);
            if (!SpreadUnpullableBells || focus.Id == 0
                || Math.Abs(origin.Z - focus.Z) > 8f)
            {
                ResetBellAim();
                return null;
            }
            if (!NeedsDirectBellContact(focus) && !HoldRangedCircle(game, origin, pullRange))
            {
                ReportBellFallback("ordinary elite without occupied combat circle", focus, origin, pullRange);
                ResetBellAim();
                return null;
            }
            int now = Environment.TickCount;
            if (BellNeedsRecovery(origin, focus)) return null;
            if (_bellAimRecoveryUntil != int.MinValue && Age(_bellAimRecoveryUntil, now) > 0)
                return null;
            bool sameCast = _bellAimTarget == focus.Id && _bellAimSerial == _bellSpawnSerial;
            // The clock only measures time when an eligible WOL hold could create a bell.
            if (!canObserveCast) _bellAimAt = int.MinValue;
            if (sameCast && _bellAimAt != int.MinValue && Age(now, _bellAimAt) > 900)
            {
                Trace("bell placement produced no new native actor after WOL hold; temporary self-aim fallback");
                _bellAimRecoveryUntil = unchecked(now + 1200);
                ResetBellAim();
                return null;
            }
            if (sameCast && _bellAimPosition != null
                && origin.XYDistanceTo(_bellAimPosition) <= pullRange - 4f
                && focus.Point.XYDistanceTo(_bellAimFocus) < 2f
                && !ProjectileLineBlocked(origin, _bellAimPosition, now, out _)
                && !ProjectileLineBlocked(_bellAimPosition, focus.Point, now, out _))
                return _bellAimPosition;

            int slots = focus.Radius > 7f ? 1 : 4;
            int firstSlot = _bellAimTarget != focus.Id ? 0
                : _bellAimSerial != _bellSpawnSerial ? (_bellAimSlot + 1) % slots : _bellAimSlot;
            IWorldCoordinate proposed = null;
            int selectedSlot = -1;
            for (int i = 0; i < slots; i++)
            {
                int slot = (firstSlot + i) % slots;
                var candidate = BellSlotPoint(origin, focus, slot);
                if (origin.XYDistanceTo(candidate) > pullRange - 4f || !TryProject(candidate, out _)
                    || ProjectileLineBlocked(origin, candidate, now, out _)
                    || ProjectileLineBlocked(candidate, focus.Point, now, out _)) continue;
                proposed = candidate;
                selectedSlot = slot;
                break;
            }
            if (proposed == null)
            {
                ReportBellFallback("no reachable projected bell slot", focus, origin, pullRange);
                ResetBellAim();
                return null;
            }
            _bellAimTarget = focus.Id;
            _bellAimFocus = focus.Point.Offset(0, 0, 0);
            _bellAimPosition = proposed;
            _bellAimOrigin = origin.Offset(0, 0, 0);
            bool changedPlacement = !sameCast;
            _bellAimSlot = selectedSlot;
            _bellAimSerial = _bellSpawnSerial;
            if (changedPlacement) _bellAimAt = int.MinValue;
            if (changedPlacement) Trace("bell aim target=" + focus.Id + " slot=" + selectedSlot
                + " fromHero=" + origin.XYDistanceTo(proposed).ToString("F1")
                + " fromElite=" + proposed.XYDistanceTo(focus.Point).ToString("F1"));
            return proposed;
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportBellFallback(string reason, Body focus, IWorldCoordinate origin, int range)
        {
#if WOL_DIAGNOSTIC
            int now = Environment.TickCount;
            if (reason == _lastBellFallbackReason && _lastBellFallbackLog != int.MinValue
                && Age(now, _lastBellFallbackLog) < 1200) return;
            _lastBellFallbackReason = reason;
            _lastBellFallbackLog = now;
            var reach = BellReachFrom(origin, focus, range);
            Trace("bell self-aim reason=" + reason + " focus=" + focus.Id
                + " tier=" + focus.Tier + " pullable=" + focus.Pullable
                + " gap=" + (focus.Id == 0 ? -1f : origin.XYDistanceTo(focus.Point)).ToString("F1")
                + " slots=" + reach.Reachable + "/" + reach.Slots + " max=" + (range - 4));
#endif
        }

        private string AimAtSelf(IGameController game, IPlayer me, IPlayerSkill wave, Keys waveKey)
        {
            var feet = me.FloorCoordinate;
            if (feet == null) return "self projection failed";
            var cyclone = me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_CycloneStrike);
            var target = BellAttackPoint(game, feet, cyclone?.Rune == 1 ? 34 : 24, CanCast(me, wave));
            var focus = ClosestElite(feet);
            if (focus.Id != 0 && ProjectileLineBlocked(feet, target ?? focus.Point,
                Environment.TickCount, out _) && target == null)
                return "WOL line blocked by wall or closed door";
            if (!TryProject(target ?? feet, out var aimed))
            {
                if (target == null || !TryProject(feet, out aimed)) return "self projection failed";
                ReportBellFallback("selected bell aim left screen", ClosestElite(feet), feet,
                    cyclone?.Rune == 1 ? 34 : 24);
                ResetBellAim(); // Preserve the original self-aim when a projected target leaves the screen.
            }
            if (!SetSelfAim(aimed)) return "self cursor move failed";
            // Left-slot Wave must not activate a pylon, portal or another clickable actor.
            var actor = game.SelectedActor;
            if (waveKey == Keys.LButton && actor != null && actor.AcdId != me.AcdId
                && actor.IsClickable && !(actor is IMonster)) return "left click would interact";
            return null;
        }

        private bool CanCheckCircle(bool dashReady, bool escapeDue, int now)
        {
            return _assistHeld && DashToCircles && dashReady && !escapeDue && !_panicRecovering
                && !CursorConflictCooling(now)
                && _dashAim == null && _dashFlight == null && _relayHome == null && _stagePhase == CircleStage.None
                && (_lastCircleCheck == int.MinValue || Age(now, _lastCircleCheck) >= CircleScanMs);
        }

        private bool CircleStillUseful(IGameController game, IWorldCoordinate origin, Aim aim, int range)
        {
            return origin.XYDistanceTo(aim.Landing) <= DashRange && IsLiveCircle(game, aim.CircleId)
                && CircleKeepsEliteContact(origin, aim.Landing, range)
                && CoverageEnough(CircleCoverage(aim.Landing, range), NearElite(origin));
        }

        private static bool IsLiveCircle(IGameController game, uint id)
        {
            return id != 0 && game.Actors != null && game.Actors.Any(actor => actor != null && !actor.IsDisabled
                && actor.WorldId == game.Me.WorldId && actor.AcdId == id);
        }

        private bool NearElite(IWorldCoordinate point) => _pack.Any(body => body.Tier > 0
            && body.Point.XYDistanceTo(point) <= 48f);

        // Wave damage and Implosion pull use distinct reach; staging is evaluated separately.
        private Coverage CircleCoverage(IWorldCoordinate point, int range, bool requireProjection = false)
        {
            return CountPack(point, range);
        }

        private static bool CoverageEnough(Coverage coverage, bool eliteFight)
        {
            return coverage.RangedElites > 0 || coverage.StageElites > 0
                || coverage.PullElites > 0 || coverage.Trash >= (eliteFight ? 8 : 4);
        }

        private float EliteEdgeGap(IWorldCoordinate point)
        {
            var elite = ClosestElite(point);
            return elite.Id == 0 ? float.MaxValue
                : Math.Max(0f, point.XYDistanceTo(elite.Point) - elite.Radius);
        }

        private static bool NeedsDirectBellContact(Body elite) => elite.Id != 0
            && (elite.Tier == 2 || !elite.Pullable);

        private bool CanDamageUnpullable(IWorldCoordinate point, Body focus, int pullRange)
        {
            return !NeedsDirectBellContact(focus) || (SpreadUnpullableBells
                ? BellReachFrom(point, focus, pullRange).Reachable > 0
                : Math.Max(0f, point.XYDistanceTo(focus.Point) - focus.Radius) <= EliteNear + 4f);
        }

        private bool CircleKeepsEliteContact(IWorldCoordinate origin, IWorldCoordinate landing, int pullRange)
        {
            var focus = ClosestElite(origin);
            if (NeedsDirectBellContact(focus))
                return CanDamageUnpullable(landing, focus, pullRange);
            float gapBefore = EliteEdgeGap(origin);
            if (gapBefore > WaveReach) return true;
            float gapAfter = EliteEdgeGap(landing);
            return gapAfter <= WaveReach || (gapAfter <= StageMaxRange && gapAfter <= gapBefore + 25f);
        }

        private IActor OccupiedCombatOculus(IGameController game, IWorldCoordinate origin, int pullRange)
        {
            var focus = ClosestElite(origin);
            if (!DashToCircles || game.Actors == null || DangerAt(origin, landing: true) != null
                || !CanDamageUnpullable(origin, focus, pullRange)
                || !CoverageEnough(CircleCoverage(origin, pullRange), NearElite(origin))) return null;
            return game.Actors.FirstOrDefault(actor => actor?.SnoActor != null && !actor.IsDisabled
                && actor.WorldId == game.Me.WorldId && actor.AcdId != 0 && actor.FloorCoordinate != null
                && Math.Abs(actor.FloorCoordinate.Z - origin.Z) <= 8f
                && origin.XYDistanceTo(actor.FloorCoordinate) <= CircleRadius - .5f
                && CircleKind(actor) == 3);
        }

        private bool HoldRangedCircle(IGameController game, IWorldCoordinate origin, int range)
        {
            if (OccupiedCombatOculus(game, origin, range) != null) return true;
            var elite = ClosestElite(origin);
            if (!CanDamageUnpullable(origin, elite, range)) return false;
            if (!DashToCircles || game.Actors == null || DangerAt(origin, landing: true) != null) return false;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.WorldId != game.Me.WorldId || actor.IsDisabled
                    || actor.FloorCoordinate == null || actor.AcdId == 0) continue;
                var center = actor.FloorCoordinate;
                if (Math.Abs(center.Z - origin.Z) > 8 || center.XYDistanceTo(origin) > CircleRadius - .5f
                    || CircleKind(actor) == 0) continue;
                var coverage = CircleCoverage(origin, range);
                return coverage.RangedElites > 0 || coverage.StageElites > 0
                    || coverage.PullElites > 0;
            }
            return false;
        }

        private static double CircleValue(Coverage coverage, int kind)
        {
            // Count all attackable elites; staging-only targets cannot receive damage yet.
            double pack = 12 * coverage.Elites + 8 * coverage.RangedElites
                + 4 * coverage.CloseElites + 6 * coverage.StageElites
                + Math.Min(coverage.Trash, 24);
            return pack * (kind == 3 ? 1.85 : kind == 2 ? 1.50 : kind == 1 ? 1.20
                : kind == 4 ? 1.05 : 1.0);
        }

        private static int CirclePriority(int kind) => kind == 3 ? 4 : kind == 2 ? 3 : kind == 1 ? 2 : kind == 4 ? 1 : 0;

        private static bool BetterCircle(int kind, double score, int previousKind, double previousScore)
        {
            if (previousKind == 0) return true;
            int rank = CirclePriority(kind), previousRank = CirclePriority(previousKind);
            // Prefer a better color when coverage is comparable, not over a vastly larger pack.
            return rank > previousRank ? score >= previousScore * .9
                : rank < previousRank ? score > previousScore * 1.3 : score > previousScore;
        }

        private bool TryAimCircle(IGameController game, IWorldCoordinate origin, int pullRange, int now,
            IPlayerSkill cyclone, Keys cycloneKey, bool dashReady, bool releaseBurst = false)
        {
            _lastCircleCheck = now;
            _usefulCircleSeen = false;
            if (InputBusy() || game.Actors == null || _pack.Count == 0) return false;
            var occupiedOculus = OccupiedCombatOculus(game, origin, pullRange);
            if (occupiedOculus != null) _usefulCircleSeen = true;
            bool eliteFight = NearElite(origin);
            var baseline = CircleCoverage(origin, pullRange, requireProjection: true);
            // Ordinary ground already has offensive value: never compare a circle to zero DPS.
            double currentScore = CircleValue(baseline, 0), bestScore = double.MinValue;
            var primaryFocus = ClosestElite(origin);
            var primaryReach = BellReachFrom(origin, primaryFocus, pullRange);
            // Reward playable bell angles, not just enemies inside a geometric radius.
            // One reachable angle on a large boss is worth the same as four on a normal elite.
            if (primaryFocus.Id != 0 && primaryReach.Slots > 0)
                currentScore += 24.0 * primaryReach.Reachable / primaryReach.Slots;
            IActor bestCircle = null;
            IWorldCoordinate best = null;
            int bestKind = 0, detected = 0, useful = 0, contactRejected = 0, unpullableRejected = 0;
            bool unpullableFocus = NeedsDirectBellContact(ClosestElite(origin));
            float spirit = game.Me.Stats.ResourceCurPri;
            float recovery = Math.Max(0f, Math.Min(1f, (200f - spirit) / 110f));
            var ally = game.Me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_MysticAlly);
            bool airAllyCooling = ally != null && ally.Rune == 3 && ally.IsOnCooldown
                && ally.CooldownFinishTick > game.CurrentGameTick;
            var serenity = game.Me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_Serenity);
            bool serenityCooling = serenity != null && serenity.IsOnCooldown && !serenity.BuffIsActive;
            bool cooldownUseful = airAllyCooling || serenityCooling;
            double greenBonus = 45.0 * recovery;
            double blueBonus = cooldownUseful ? 55.0 * recovery : 0.0;
            _circleCandidates.Clear();
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.WorldId != game.Me.WorldId || actor.IsDisabled
                    || actor.FloorCoordinate == null || actor.AcdId == 0) continue;
                int kind = CircleKind(actor);
                var center = actor.FloorCoordinate;
                if (kind == 0 || Math.Abs(center.Z - origin.Z) > 8 || origin.XYDistanceTo(center) > CircleSearchYards) continue;
                _circleCandidates.Add(new KeyValuePair<IActor, int>(actor, kind));
            }
            // A confirmed landing deserves a short offensive window; do not immediately
            // abandon it for another circle unless recovery or coverage warrants the move.
            bool recentLanding = _lastCircleLandingId != 0 && _lastCircleLandingCenter != null
                && _lastCircleLandingAt != int.MinValue && Age(now, _lastCircleLandingAt) < 1500
                && origin.XYDistanceTo(_lastCircleLandingCenter) <= CircleRadius - .5f
                && CoverageEnough(baseline, eliteFight)
                && DangerAt(origin, landing: true) == null
                && CanDamageUnpullable(origin, primaryFocus, pullRange);
            int landedKind = 0;
            if (recentLanding)
            {
                foreach (var ring in _circleCandidates)
                    if (ring.Key.AcdId == _lastCircleLandingId) { landedKind = ring.Value; break; }
                recentLanding = landedKind != 0;
                if (recentLanding)
                    currentScore = Math.Max(currentScore, CombatCircleScore(baseline, landedKind,
                        origin, pullRange, greenBonus, blueBonus));
            }
            foreach (var ring in _circleCandidates)
            {
                var actor = ring.Key;
                int kind = ring.Value;
                var center = actor.FloorCoordinate;
                bool inside = origin.XYDistanceTo(center) <= CircleRadius - .5f;
                detected++;
                if (inside && DangerAt(origin, landing: true) == null)
                {
                    if (CoverageEnough(baseline, eliteFight))
                    {
                        double value = CombatCircleScore(baseline, kind, origin, pullRange,
                            greenBonus, blueBonus);
                        if (value > currentScore) currentScore = value;
                        _usefulCircleSeen = true;
                    }
                }
                if ((_failedCircle == actor.AcdId && Age(_failedCircleUntil, now) > 0)
                    || (_lastCircleWalkId == actor.AcdId && _lastCircleWalkAt != int.MinValue
                        && Age(now, _lastCircleWalkAt) < 350)) continue;
                // Try the center and two points inside the rim. The near rim can
                // reach a ring whose center is just beyond one Dash.
                var contactElite = ClosestElite(center);
                var focus = contactElite.Id != 0 ? contactElite.Point : ClosestPackPoint(center);
                // Evaluate nearby intersections for all circle colors, not only Oculus.
                int total = 3 + _circleCandidates.Count;
                for (int i = inside ? 1 : 0; i < total; i++)
                {
                    IWorldCoordinate point;
                    if (i < 3)
                    {
                        point = i == 0 ? center : InsideRim(center, i == 1 ? origin : focus);
                        if (i != 0 && ReferenceEquals(point, center)) continue;
                    }
                    else
                    {
                        var other = _circleCandidates[i - 3];
                        var otherCenter = other.Key.FloorCoordinate;
                        if (other.Key.AcdId == actor.AcdId || other.Value == kind
                            || Math.Abs(center.Z - otherCenter.Z) > 3
                            || center.XYDistanceTo(otherCenter) > 2 * (CircleRadius - 1f)) continue;
                        point = center.Offset((otherCenter.X - center.X) * .5f, (otherCenter.Y - center.Y) * .5f, 0);
                    }
                    float travel = point.XYDistanceTo(origin);
                    if (travel < 2f || DangerAt(point, landing: true) != null) continue;
                    // A nearby elite and occupied Oculus are worth keeping. Only a safe
                    // overlap inside that same Oculus justifies another circle move.
                    bool urgentRecovery = recovery >= .45f && travel <= 38f
                        && (kind == 4 || (kind == 1 && cooldownUseful));
                    if (occupiedOculus != null && !urgentRecovery
                        && (point.XYDistanceTo(occupiedOculus.FloorCoordinate) > CircleRadius - .5f
                            || !HasCircleOverlap(point, kind))) continue;
                    if (inside && !HasCircleOverlap(point, kind)
                        && EliteEdgeGap(origin) - EliteEdgeGap(point) < 3f) continue;
                    var coverage = CircleCoverage(point, pullRange);
                    if (!CoverageEnough(coverage, eliteFight)) continue;
                    if (!CircleKeepsEliteContact(origin, point, pullRange))
                    {
                        contactRejected++;
                        if (unpullableFocus) unpullableRejected++;
#if WOL_DIAGNOSTIC
                        if (unpullableFocus && (_lastBellCircleRejectLog == int.MinValue
                            || Age(now, _lastBellCircleRejectLog) > 1200))
                        {
                            _lastBellCircleRejectLog = now;
                            var reach = BellReachFrom(point, ClosestElite(origin), pullRange);
                            Trace("circle rejected id=" + actor.AcdId + " type=" + CircleName(kind)
                                + " reason=no bell within Cyclone safety radius"
                                + " bossGap=" + EliteEdgeGap(point).ToString("F1")
                                + " nearestBell=" + (reach.Nearest == float.MaxValue ? "none" : reach.Nearest.ToString("F1"))
                                + " safeMax=" + (pullRange - 4f));
                        }
#endif
                        continue;
                    }
                    useful++;
                    double value = CombatCircleScore(coverage, kind, point, pullRange,
                        greenBonus, blueBonus) - travel * .30;
                    if (primaryFocus.Id != 0 && primaryReach.Slots > 0)
                    {
                        var reachable = BellReachFrom(point, primaryFocus, pullRange);
                        value += 24.0 * reachable.Reachable / reachable.Slots;
                    }
                    bool criticalRecoveryOverride = recovery >= .75f && (kind == 4 || (kind == 1 && cooldownUseful))
                        && landedKind != 1 && landedKind != 4;
                    if (recentLanding && kind != landedKind && !criticalRecoveryOverride
                        && point.XYDistanceTo(_lastCircleLandingCenter) > CircleRadius - .5f
                        && value < currentScore * 1.55)
                    {
#if WOL_DIAGNOSTIC
                        if (value >= currentScore && (_lastCircleHoldLog == int.MinValue
                            || Age(now, _lastCircleHoldLog) >= 700))
                        {
                            _lastCircleHoldLog = now;
                            Trace("circle switch held from=" + _lastCircleLandingId + " to=" + actor.AcdId
                                + " kind=" + CircleName(kind) + " elapsedMs=" + Age(now, _lastCircleLandingAt)
                                + " spirit=" + spirit.ToString("F0") + " score=" + value.ToString("F1")
                                + " occupied=" + currentScore.ToString("F1"));
                        }
#endif
                        continue;
                    }
                    if (_pursuitId == actor.AcdId && _pursuitHops > 0 && Age(now, _pursuitLast) < 1800) value += 3;
                    if (CircleRecovering(actor.AcdId, now)) value += 3;
                    if (value <= bestScore || !MapAllows(point, now)) continue;
                    best = point;
                    bestCircle = actor;
                    bestScore = value;
                    bestKind = kind;
                }
            }

            // Bosses can profit from a reachable multiplier without gaining melee contact.
            double requiredScore = unpullableFocus ? currentScore * 1.05 + (currentScore > 0 ? .5 : 0)
                : currentScore * 1.18 + (currentScore > 0 ? 2 : 0);
            bool recoveryMove = best != null && recovery >= .45f && origin.XYDistanceTo(best) <= 38f
                && (bestKind == 4 || (bestKind == 1 && cooldownUseful));
            if (recoveryMove)
                requiredScore = Math.Min(requiredScore, currentScore * (1.10 - .48 * recovery)
                    + (currentScore > 0 ? 2 : 0));
            // Do not bounce between recovery circles immediately after a verified landing.
            if (recoveryMove && _pursuitId != 0 && _pursuitId != bestCircle.AcdId
                && _pursuitLast != int.MinValue && Age(now, _pursuitLast) < 1200
                && _circleCandidates.Any(ring => ring.Key.AcdId == _pursuitId
                    && (ring.Value == 1 || ring.Value == 4)
                    && origin.XYDistanceTo(ring.Key.FloorCoordinate) <= CircleRadius - .5f))
                requiredScore = Math.Max(requiredScore, currentScore * 1.10 + 2);
            bool densityHold = best != null && bestScore < requiredScore;
#if WOL_DIAGNOSTIC
            var candidateFocus = ClosestElite(origin);
            var candidateReach = best == null || candidateFocus.Id == 0
                ? default(BellReach) : BellReachFrom(best, candidateFocus, pullRange);
            SetCircleStatus("seen=" + detected + " useful=" + useful + " contactRejected=" + contactRejected
                + " unpullableRejected=" + unpullableRejected + " coverageHold=" + densityHold
                + " current=" + currentScore.ToString("F1") + " candidate="
                + (best == null ? "none" : bestScore.ToString("F1"))
                + " kind=" + (best == null ? "none" : CircleName(bestKind))
                + " bellSlots=" + candidateReach.Reachable + "/" + candidateReach.Slots
                + " recovery=" + recovery.ToString("F2") + " spirit=" + spirit.ToString("F0")
                + " cooldownUseful=" + cooldownUseful + " recoveryMove=" + recoveryMove
                + " required=" + requiredScore.ToString("F1") + " supportBonus="
                + (bestKind == 4 ? greenBonus : bestKind == 1 ? blueBonus : 0).ToString("F1"));
#endif
            var focusLine = ClosestElite(origin);
            if (best != null && focusLine.Id != 0)
            {
                bool currentBlocked = ProjectileLineBlocked(origin, focusLine.Point, now, out _);
                bool circleBlocked = ProjectileLineBlocked(best, focusLine.Point, now, out _)
                    && !HasOpenBellLane(best, focusLine, pullRange, now);
                if (circleBlocked && !currentBlocked) return false;
                var bestReach = BellReachFrom(best, focusLine, pullRange);
                bool valuableRing = (bestKind == 3 || bestKind == 2)
                    && occupiedOculus == null && best.XYDistanceTo(focusLine.Point) <= 12f + focusLine.Radius
                    && bestReach.Reachable >= Math.Min(3, bestReach.Slots)
                    && bestScore >= currentScore * .85;
                if (!circleBlocked && ((currentBlocked && bestScore >= currentScore * .65) || valuableRing))
                    requiredScore = Math.Min(requiredScore, bestScore);
            }
            bool contactUpgrade = bestCircle != null
                && origin.XYDistanceTo(bestCircle.FloorCoordinate) <= CircleRadius - .5f
                && EliteEdgeGap(origin) > WaveReach && EliteEdgeGap(best) <= WaveReach
                && EliteEdgeGap(origin) - EliteEdgeGap(best) >= 3f;
            if (best == null || (!contactUpgrade && bestScore < requiredScore)) return false;
            var plan = new Aim
            {
                Landing = best.Offset(0, 0, 0), Goal = best.Offset(0, 0, 0), CircleId = bestCircle.AcdId,
                PursuitId = bestCircle.AcdId, Started = now,
                CircleCenter = bestCircle.FloorCoordinate.Offset(0, 0, 0), Reason = CircleName(bestKind),
                OverlapCenter = OtherCircleCenter(best, bestKind),
            };
            _usefulCircleSeen = true;
#if WOL_DIAGNOSTIC
            var chosen = CircleCoverage(best, pullRange);
            var bellFocus = ClosestElite(origin);
            var bellReach = bellFocus.Id != 0
                ? BellReachFrom(best, bellFocus, pullRange) : default(BellReach);
            Trace("circle selected=" + plan.CircleId + " type=" + CircleName(bestKind)
                + " focusGap=" + EliteEdgeGap(best).ToString("F1")
                + " bellMin=" + (bellReach.Reachable > 0 ? bellReach.Nearest.ToString("F1") : "none")
                + " bellSlots=" + bellReach.Reachable + "/" + bellReach.Slots
                + " elites=" + chosen.Elites + " ranged=" + chosen.RangedElites
                + " staged=" + chosen.StageElites + " pullElites=" + chosen.PullElites
                + " trash=" + chosen.Trash + " travel=" + origin.XYDistanceTo(best).ToString("F1")
                + " overlap=" + HasCircleOverlap(best, bestKind)
                + " eliteGap=" + EliteEdgeGap(best).ToString("F1") + " score=" + bestScore.ToString("F1")
                + " baseline=" + currentScore.ToString("F1")
                + " recovery=" + recovery.ToString("F2") + " spirit=" + spirit.ToString("F0")
                + " cooldownUseful=" + cooldownUseful + " recoveryMove=" + recoveryMove
                + " required=" + requiredScore.ToString("F1") + " supportBonus="
                + (bestKind == 4 ? greenBonus : bestKind == 1 ? blueBonus : 0).ToString("F1"));
#endif
            bool recovering = CircleRecovering(plan.CircleId, now);
            if (recovering && _mapEpoch != null) dashReady = false;
            if (TryTravel(game, origin, plan, dashReady, now))
            {
                if (_dashAim?.Walk == true) { _lastCircleWalkId = plan.CircleId; _lastCircleWalkAt = now; }
                return true;
            }
            if (_releasedCycloneAim != null) return true;
            _failedCircle = plan.CircleId;
            _failedCircleUntil = unchecked(now + 260);
            return false;
        }

        private double CombatCircleScore(Coverage coverage, int kind, IWorldCoordinate point,
            int pullRange, double greenBonus = 0, double blueBonus = 0)
        {
            double score = CircleScoreWithOverlap(coverage, kind, point)
                + (kind == 4 ? greenBonus : kind == 1 ? blueBonus : 0);
            var elite = ClosestElite(point);
            if (elite.Id == 0) return score;
            if (!NeedsDirectBellContact(elite))
                return score - Math.Max(0f, EliteEdgeGap(point) - WaveReach) * .20;
            var reach = BellReachFrom(point, elite, pullRange);
            if (reach.Reachable == 0) return double.MinValue;
            // Penalize lost blast angles and bells close to the reach limit, not boss range itself.
            float safeLimit = pullRange - 4f;
            return score - (reach.Slots - reach.Reachable) * .75
                - Math.Max(0f, reach.Nearest - (safeLimit - 7f)) * .35;
        }

        private double CircleScoreWithOverlap(Coverage coverage, int kind, IWorldCoordinate point)
        {
            double score = CircleValue(coverage, kind);
            return HasCircleOverlap(point, kind) ? score + Math.Max(8, score * .35) : score;
        }

        private bool HasCircleOverlap(IWorldCoordinate point, int kind) => OtherCircleCenter(point, kind) != null;

        private IWorldCoordinate OtherCircleCenter(IWorldCoordinate point, int kind)
        {
            foreach (var other in _circleCandidates)
            {
                if ((kind == 3) == (other.Value == 3)) continue;
                var center = other.Key.FloorCoordinate;
                if (Math.Abs(point.Z - center.Z) <= 3 && point.XYDistanceTo(center) <= CircleRadius - 1f)
                    return center.Offset(0, 0, 0);
            }
            return null;
        }

        private bool TryRetryCircle(IGameController game, IWorldCoordinate origin, Aim failed, int range, int now)
        {
            if (!IsLiveCircle(game, failed.CircleId)) return false;
            var center = failed.CircleCenter;
            for (int i = 0; i < 3; i++)
            {
                var alternate = i == 0 ? center : InsideRim(center, i == 1 ? origin : ClosestPackPoint(center));
                if (alternate.XYDistanceTo(failed.Landing) < 3 || !ValidDashLanding(origin, alternate, now)
                    || !CircleKeepsEliteContact(origin, alternate, range)
                    || !CoverageEnough(CircleCoverage(alternate, range), NearElite(origin))) continue;
                var plan = new Aim { Landing = alternate, CircleCenter = center, CircleId = failed.CircleId,
                    Reason = failed.Reason + " alternate rim" };
                if (!QueueCircleDash(game, plan, now)) return false;
                _dashAim.Retried = true;
                Trace("circle retry id=" + failed.CircleId);
                return true;
            }
            return false;
        }

        private static string CircleName(int kind) => kind == 3 ? "Oculus" : kind == 2 ? "Triune damage"
            : kind == 4 ? "Triune resource cost" : "Triune cooldown";

        private bool QueueCircleDash(IGameController game, Aim plan, int now)
        {
            if (!QueueDashAim(game, plan.Landing, plan.Reason, false, now)) return false;
            _dashAim.CircleId = plan.CircleId;
            _dashAim.CircleCenter = plan.CircleCenter;
            _dashAim.OverlapCenter = plan.OverlapCenter;
            _dashAim.PursuitId = plan.PursuitId;
            _dashAim.StageOut = plan.StageOut;
            _dashAim.StageReturn = plan.StageReturn;
            return true;
        }

        private IWorldCoordinate ClosestPackPoint(IWorldCoordinate center)
        {
            Body best = default(Body);
            float distance = float.MaxValue;
            foreach (var body in _pack)
            {
                if (body.Tier < best.Tier) continue;
                float gap = center.XYDistanceTo(body.Point);
                if (body.Tier == best.Tier && gap >= distance) continue;
                best = body;
                distance = gap;
            }
            return best.Point;
        }

        private static IWorldCoordinate InsideRim(IWorldCoordinate center, IWorldCoordinate toward)
        {
            if (toward == null) return center;
            float distance = center.XYDistanceTo(toward);
            if (distance < 1f) return center;
            float factor = Math.Min(1, 6.5f / distance);
            return center.Offset((toward.X - center.X) * factor, (toward.Y - center.Y) * factor, 0);
        }

        private Body ClosestElite(IWorldCoordinate point)
        {
            Body focus = default(Body);
            float nearest = float.MaxValue;
            foreach (var body in _pack)
            {
                if (body.Tier == 0 || body.Id == 0 || Math.Abs(body.Z - point.Z) > 8f) continue;
                float gap = Math.Max(0f, point.XYDistanceTo(body.Point) - body.Radius);
                // An attackable Rift Guardian takes precedence over pylon-spawned elites.
                if (focus.Id != 0 && (body.Tier < focus.Tier
                    || (body.Tier == focus.Tier && gap >= nearest))) continue;
                focus = body;
                nearest = gap;
            }
            return focus;
        }

        private static IWorldCoordinate EliteGroundGoal(IWorldCoordinate origin, Body elite)
        {
            float gap = Math.Max(1, origin.XYDistanceTo(elite.Point));
            float stand = elite.Radius + 8;
            return elite.Point.Offset((origin.X - elite.X) * stand / gap, (origin.Y - elite.Y) * stand / gap, 0);
        }

        private void SetCircleRecovery(Aim aim, int now)
        {
            uint circle = aim.CircleId != 0 ? aim.CircleId : aim.TransitCircle;
            if (circle == 0 || aim.Escape) return;
            _circleRecoveryId = circle;
            _circleRecoveryUntil = unchecked(now + 1150);
            _lastCircleCheck = int.MinValue;
            Trace("circle fallback id=" + circle + " method=walk");
        }

        private bool CircleRecovering(uint circle, int now) => circle != 0 && _circleRecoveryId == circle
            && Age(_circleRecoveryUntil, now) > 0;

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportCycloneAvailability(IPlayer me, IPlayerSkill cyclone)
        {
#if WOL_DIAGNOSTIC
            if (cyclone == null || CanCast(me, cyclone)) return;
            Trace("Cyclone blocked spirit=" + me.Stats.ResourceCurPri.ToString("F0")
                + " cost=" + cyclone.GetResourceRequirement().ToString("F0")
                + " cooldown=" + cyclone.IsOnCooldown);
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportDashSignals(Aim aim, IPlayerSkill dash, IPlayer me)
        {
#if WOL_DIAGNOSTIC
            Trace("Dash result " + aim.Reason + " requested=" + aim.From.XYDistanceTo(aim.Landing).ToString("F1")
                + " keyUpMs=" + (aim.ReleaseAt == 0 ? -1 : Age(aim.ReleaseAt, aim.SentAt)) + " keyUpSent=" + aim.ReleaseSent
                + " animationObserved=" + aim.AnimationObserved + " chargeObserved=" + aim.ChargeObserved
                + " charges=" + aim.ChargesBefore + "->" + (dash?.Charges ?? 0)
                + " observationMs=" + Age(Environment.TickCount, aim.SentAt)
                + " animationNow=" + me.AnimationState);
#endif
        }

        private bool TryTravel(IGameController game, IWorldCoordinate origin, Aim plan, bool dashReady, int now)
        {
            bool canHop = PursuitCanHop(plan.PursuitId, now);
            float distance = origin.XYDistanceTo(plan.Landing);
            if (dashReady && (canHop || plan.CircleId != 0)
                && ValidDashLanding(origin, plan.Landing, now) && DashRouteClear(origin, plan.Landing, now))
            {
                if (QueueCircleDash(game, plan, now))
                {
                    _dashAim.EliteId = plan.EliteId;
                    return true;
                }
            }
            if (dashReady && canHop)
            {
                var step = MapWaypoint("dash", origin, plan.Landing, now);
                if (step == null && distance > DashRange && _mapEpoch == null)
                {
                    float ratio = 46f / distance;
                    step = origin.Offset((plan.Landing.X - origin.X) * ratio, (plan.Landing.Y - origin.Y) * ratio, 0);
                }
                if (step != null && step.XYDistanceTo(plan.Landing) + 4 < distance
                    && ValidDashLanding(origin, step, now) && QueueDashAim(game, step, plan.Reason + " transit", false, now))
                {
                    CopyTransitGoal(_dashAim, plan);
                    return true;
                }
            }

            if (_lastWalk != int.MinValue && Age(now, _lastWalk) < 225) return false;
            var waypoint = MapWaypoint("path", origin, plan.Landing, now);
            if (waypoint == null || DangerAt(waypoint, landing: true) != null
                || !QueueDashAim(game, waypoint, plan.Reason + " walk", false, now)) return false;
            _dashAim.Walk = true;
            CopyTransitGoal(_dashAim, plan);
            return true;
        }

        private bool PursuitCanHop(uint id, int now)
        {
            if (_pursuitId != id || Age(now, _pursuitLast) > 1800)
            {
                _pursuitId = id;
                _pursuitHops = 0;
            }
            return _pursuitHops < 2;
        }

        private static void CopyTransitGoal(Aim aim, Aim plan)
        {
            aim.Goal = plan.Landing;
            aim.PursuitId = plan.PursuitId;
            aim.EliteId = plan.EliteId;
            aim.TransitCircle = plan.CircleId;
            aim.Transit = true;
            aim.StageOut = plan.StageOut;
            aim.StageReturn = plan.StageReturn;
        }

        private bool TransitStillUseful(IGameController game, IWorldCoordinate origin, Aim aim)
        {
            return aim.Goal != null && (aim.TransitCircle == 0 || IsLiveCircle(game, aim.TransitCircle))
                && (aim.EliteId == 0 || _pack.Exists(body => body.Id == aim.EliteId))
                && origin.XYDistanceTo(aim.Landing) > 1f;
        }

        private void AdvanceWalk(IGameController game, IWorldCoordinate origin, Aim aim, int now)
        {
            if (!StillAiming(aim) || Age(now, aim.Started) >= 130 || !TransitStillUseful(game, origin, aim)
                || DangerAt(aim.Landing, landing: true) != null)
            {
                CancelDashAim();
                return;
            }
            if (game.CurrentGameTick == aim.Tick || InputBusy()) return;
            var key = ForceMoveKey;
            var selected = game.SelectedActor;
            if (key == Keys.None)
            {
                if (selected != null && selected.AcdId != game.Me.AcdId)
                {
                    _lastWalk = now;
                    CancelDashAim();
                    return;
                }
                key = Keys.LButton;
            }
            if (key == AssistKey || !ReprojectAim(aim) || !BeginInput(key, 120, aim, moveOnly: true))
            {
                _lastWalk = now;
                CancelDashAim();
                return;
            }
            _lastWalk = now;
            _walkOrigin = origin.Offset(0, 0, 0);
            _dashAim = null;
            Trace(aim.Reason + " waypoint dispatched");
        }

        private IDictionary<string, object> MapMotion(string operation, IWorldCoordinate origin, IWorldCoordinate goal, int now)
        {
            MapAllows(goal, now);
            if (_mapEpoch == null || _mapQuery == null) return null;
            try
            {
                return _mapQuery(operation, new[] { _mapEpoch, origin.X, origin.Y, origin.Z,
                    goal.X, goal.Y, goal.Z, .6f }) as IDictionary<string, object>;
            }
            catch { return null; }
        }

        private bool ProjectileLineBlocked(IWorldCoordinate from, IWorldCoordinate to, int now, out string reason)
        {
            reason = null;
            if (from == null || to == null || Math.Abs(from.Z - to.Z) > 8f) return false;
            if (LiveProjectileBarrier(from, to, out reason)) return true;
            if (from.XYDistanceTo(to) < 3f) return false;
            foreach (var sample in _lineSamples)
                if (sample.World == _world && Age(now, sample.CheckedAt) < 450
                    && sample.From.XYDistanceTo(from) < 2f && sample.To.XYDistanceTo(to) < 2f
                    && Math.Abs(sample.From.Z - from.Z) < 2f && Math.Abs(sample.To.Z - to.Z) < 2f)
                { reason = sample.Reason; return sample.Blocked; }
            var answer = MapMotion("corridor", from, to, now);
            bool blocked = answer != null && answer.TryGetValue("state", out var state)
                && Equals(state, "BlockedCandidate") && answer.TryGetValue("reason", out var why)
                && (Equals(why, "NativeWalkGap") || Equals(why, "NativeWallClearance")
                    || Equals(why, "NativeElevationStep"));
            // Collision-box candidates also include chests and other non-wall props.
            if (!blocked && answer != null && answer.TryGetValue("reason", out var kind)
                && Equals(kind, "NativeCollisionBox"))
            {
                var segment = MapMotion("segment", from, to, now);
                if (segment != null && segment.TryGetValue("nativeCollisionCandidates", out var found)
                    && found is object[] boxes)
                    foreach (var box in boxes)
                        if (box is IDictionary<string, object> item && item.TryGetValue("kind", out var name))
                        {
                            var label = Convert.ToString(name);
                            if (label.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                                || label.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0
                                || label.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0)
                            { blocked = true; break; }
                        }
            }
            if (blocked) reason = Convert.ToString(answer["reason"]);
            if (_lineSamples.Count >= 24) _lineSamples.RemoveAt(0);
            _lineSamples.Add(new LineSample { World = _world, From = from.Offset(0, 0, 0),
                To = to.Offset(0, 0, 0), CheckedAt = now, Blocked = blocked, Reason = reason });
            return blocked;
        }

        private bool LiveProjectileBarrier(IWorldCoordinate from, IWorldCoordinate to, out string reason)
        {
            reason = null;
            var game = Hud.Game;
            if (_lineObstaclesTick != game.CurrentGameTick)
            {
                _lineObstaclesTick = game.CurrentGameTick;
                _lineObstacles.Clear();
                if (game.Actors != null)
                    foreach (var actor in game.Actors)
                        if (actor?.SnoActor != null && actor.WorldId == game.Me.WorldId
                            && !actor.IsDisabled && !actor.IsOperated && actor.FloorCoordinate != null
                            && (actor.SnoActor.Sno == ActorSnoEnum._monsteraffix_waller_wall
                                || actor.GizmoType == GizmoType.Door || actor.GizmoType == GizmoType.Gate
                                || actor.GizmoType == GizmoType.BreakableDoor))
                            _lineObstacles.Add(actor);
            }
            float dx = to.X - from.X, dy = to.Y - from.Y, length2 = dx * dx + dy * dy;
            if (length2 < 4f) return false;
            foreach (var actor in _lineObstacles)
            {
                bool waller = actor.SnoActor.Sno == ActorSnoEnum._monsteraffix_waller_wall;
                bool door = !waller;
                var pos = actor.CollisionCoordinate;
                if (pos == null || !pos.IsValid) pos = actor.FloorCoordinate;
                if (Math.Abs(pos.Z - from.Z) > 4f) continue;
                float t = ((pos.X - from.X) * dx + (pos.Y - from.Y) * dy) / length2;
                if (t < .05f || t > .95f) continue;
                float x = from.X + t * dx - pos.X, y = from.Y + t * dy - pos.Y;
                float radius = actor.RadiusBottom;
                if (float.IsNaN(radius) || radius < .5f || radius > 8f) radius = door ? 2.5f : 2f;
                radius = Math.Min(5f, radius + .3f);
                if (x * x + y * y > radius * radius) continue;
                reason = door ? "closed door" : "active Waller wall";
                return true;
            }
            return false;
        }

        private bool DashRouteClear(IWorldCoordinate origin, IWorldCoordinate goal, int now)
        {
            // Offensive Dash must not bypass an observed wall/door just because map routing is incomplete.
            if (ProjectileLineBlocked(origin, goal, now, out _)) return false;
            var answer = MapMotion("dash", origin, goal, now);
            if (answer == null) return _mapEpoch == null;
            if (answer.TryGetValue("state", out var state) && Equals(state, "Unknown")) return true;
            if (answer.TryGetValue("waypoint", out var value) && value is float[] xyz && xyz.Length >= 3)
                return goal.XYDistanceTo(xyz[0], xyz[1]) <= 3;
            return false;
        }

        private IWorldCoordinate MapWaypoint(string operation, IWorldCoordinate origin, IWorldCoordinate goal, int now)
        {
            var answer = MapMotion(operation, origin, goal, now);
            if (answer == null) return null;
            if (answer.TryGetValue("requiresDoor", out var requiresDoor) && Equals(requiresDoor, true)) return null;
            if (answer.TryGetValue("ok", out var ok) && Equals(ok, true)
                && answer.TryGetValue("state", out var state) && Equals(state, "Ready")
                && answer.TryGetValue("waypoint", out var value) && value is float[] xyz && xyz.Length >= 3)
                return Hud.Window.CreateWorldCoordinate(xyz[0], xyz[1], xyz[2]);
            ReportRouteWait(operation, answer, now);
            return null;
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportRouteWait(string operation, IDictionary<string, object> answer, int now)
        {
            if (_lastRouteLog != int.MinValue && Age(now, _lastRouteLog) < 500) return;
            _lastRouteLog = now;
            answer.TryGetValue("state", out var state);
            answer.TryGetValue("reason", out var reason);
            Trace("route " + operation + " state=" + state + " reason=" + reason);
        }

        private bool EliteApproachStillUseful(IWorldCoordinate origin, Aim aim)
        {
            if (aim.Reason == "misplaced bell flank")
                return _pack.Exists(body => body.Id == aim.EliteId)
                    && origin.XYDistanceTo(aim.Landing) > 2f;
            if (aim.Reason == "blocked WOL approach")
                return origin.XYDistanceTo(aim.Landing) > 2f
                    && _pack.Any(body => body.Id == aim.EliteId
                        && ProjectileLineBlocked(origin, body.Point, Environment.TickCount, out _));
            return origin.XYDistanceTo(aim.Landing) <= DashRange
                && _pack.Any(body => body.Id == aim.EliteId
                    && origin.XYDistanceTo(body.Point) - body.Radius > EliteNear
                    && (aim.Landing.XYDistanceTo(body.Point) - body.Radius <= EliteNear
                        || aim.Landing.XYDistanceTo(body.Point) + 10f < origin.XYDistanceTo(body.Point)));
        }

        private bool ValidDashLanding(IWorldCoordinate origin, IWorldCoordinate landing, int now)
        {
            float distance = origin.XYDistanceTo(landing);
            return distance >= 2f && distance <= DashRange && Math.Abs(origin.Z - landing.Z) <= 8f
                && DangerAt(landing, landing: true) == null && TryProject(landing, out _) && MapAllows(landing, now);
        }

        private bool RelayStageClear(IWorldCoordinate stage, int range)
        {
            return !_pack.Any(body => body.Tier == 1 && Math.Abs(stage.Z - body.Z) <= 8f
                && body.Point.XYDistanceTo(stage) <= range + body.Radius);
        }

        private int RelayTrash(IWorldCoordinate stage, IWorldCoordinate home, int range)
        {
            return _pack.Count(body => body.Tier == 0 && Math.Abs(body.Z - stage.Z) <= 8f
                && body.Point.XYDistanceTo(home) > range + 2 && body.Point.XYDistanceTo(stage) <= range);
        }

        private static float RelayStageDistance(float trashDistance, float eliteRadius, int range)
        {
            return Math.Max(range + eliteRadius + 1, trashDistance - (range - 4));
        }

        private static bool RelayCanReturn(float stageToHome, int range)
        {
            // Leave one yard of tolerance for where the staged trash actually lands.
            return stageToHome >= 2 && stageToHome <= range - 1;
        }

        private IActor CurrentEliteCircle(IGameController game, IWorldCoordinate origin, Body elite)
        {
            if (!DashToCircles || game.Actors == null) return null;
            IActor best = null;
            int bestKind = 0;
            double bestValue = 0;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.IsDisabled || actor.WorldId != game.Me.WorldId
                    || actor.AcdId == 0 || actor.FloorCoordinate == null) continue;
                var center = actor.FloorCoordinate;
                if (Math.Abs(center.Z - origin.Z) > 8 || center.XYDistanceTo(origin) > CircleRadius - .5f
                    || center.XYDistanceTo(elite.Point) - elite.Radius > EliteNear + 7) continue;
                int kind = CircleKind(actor);
                if (kind == 0) continue;
                double value = CircleValue(CountPack(center, 24), kind);
                if (!BetterCircle(kind, value, bestKind, bestValue)) continue;
                best = actor;
                bestKind = kind;
                bestValue = value;
            }
            return best;
        }

        private bool TryStartRelay(IGameController game, IPlayer me, IPlayerSkill cyclone, Keys cycloneKey, int range, int now)
        {
            if (InputBusy() || cycloneKey == Keys.None || cycloneKey == AssistKey || !CanCast(me, cyclone)
                || me.Stats.ResourceCurPri < 2 * cyclone.GetResourceRequirement()) return false;
            var origin = me.FloorCoordinate;
            if (DangerAt(origin, landing: true) != null) return false;
            var elite = ClosestElite(origin);
            if (elite.Id == 0 || origin.XYDistanceTo(elite.Point) - elite.Radius > EliteNear) return false;
            var circle = CurrentEliteCircle(game, origin, elite);
            IWorldCoordinate bestStage = null, bestHome = null;
            int bestCount = 2, considered = 0;
            foreach (var body in _pack)
            {
                if (body.Tier != 0) continue;
                float distance = elite.Point.XYDistanceTo(body.Point);
                if (distance <= range + 6 || distance > 2 * range + 2) continue;
                if (++considered > 24) break;
                float stand = RelayStageDistance(distance, elite.Radius, range);
                if (stand > range + 6) continue;
                float x = (body.X - elite.X) / distance, y = (body.Y - elite.Y) / distance;
                var stage = elite.Point.Offset(x * stand, y * stand, 0);
                float returnGap = elite.Radius + 10;
                var home = circle == null
                    ? elite.Point.Offset(x * returnGap, y * returnGap, 0)
                    : InsideRim(circle.FloorCoordinate, stage);
                if (home.XYDistanceTo(elite.Point) - elite.Radius > EliteNear
                    || !RelayCanReturn(stage.XYDistanceTo(home), range)) continue;
                int count = RelayTrash(stage, home, range);
                if (count <= bestCount || !RelayStageClear(stage, range)
                    || !ValidDashLanding(origin, stage, now) || !ValidDashLanding(stage, home, now)) continue;
                bestCount = count;
                bestStage = stage;
                bestHome = home;
            }
            if (bestStage == null || !QueueDashAim(game, bestStage, "trash relay outbound", false, now)) return false;
            _dashAim.RelayOut = true;
            _lastRelay = _relayStarted = now;
            _relayAtStage = _relayPulled = false;
            _relayHome = new Aim
            {
                Landing = bestHome.Offset(0, 0, 0), CircleId = circle?.AcdId ?? 0,
                CircleCenter = circle?.FloorCoordinate?.Offset(0, 0, 0),
                Reason = circle == null ? "trash relay return beside elite" : "trash relay return to circle"
            };
            Trace("trash relay planned actor=" + elite.Id + " remoteTrash=" + bestCount + " pullRange=" + range
                + " stageFromElite=" + bestStage.XYDistanceTo(elite.Point).ToString("F1")
                + " stageToHome=" + bestStage.XYDistanceTo(bestHome).ToString("F1"));
            return true;
        }

        private bool AdvanceRelay(IGameController game, IPlayer me, IPlayerSkill cyclone, Keys cycloneKey,
            int range, bool dashReady, bool holdCyclone, int now)
        {
            var home = _relayHome;
            if (Age(now, _relayStarted) >= 1150 || (home.CircleId != 0 && !IsLiveCircle(game, home.CircleId))
                || DangerAt(home.Landing, landing: true) != null || !MapAllows(home.Landing, now))
            {
                _relayHome = null;
                Trace("trash relay ended: return unavailable or expired");
                return false;
            }
            if (!_relayAtStage) return _dashFlight != null;
            if (!_relayPulled)
            {
                if (!holdCyclone && cycloneKey != Keys.None && CanCast(me, cyclone)
                    && RelayStageClear(me.FloorCoordinate, range)
                    && RelayTrash(me.FloorCoordinate, home.Landing, range) >= 2
                    && CastCycloneAtFeet(game, cycloneKey, DashPulseMs, now, "relay pull"))
                {
                    _relayPulled = true;
                    _burstStarted = int.MinValue;
                    Trace("Cyclone trash relay staging pull");
                    return true;
                }
                if (_cycloneSelfAim != null) return true;
                _relayPulled = true;
                Trace("trash relay staging pull skipped; returning");
            }
            if (!dashReady || !QueueCircleDash(game, home, now)) return true;
            _dashAim.RelayReturn = true;
            return true;
        }

        private static double ConventionLeft(IBuff buff, int index, double limit = 4.5)
        {
            var left = buff?.TimeLeftSeconds;
            return left != null && index < left.Length && left[index] > 0 && left[index] <= limit ? left[index] : 0;
        }

        private bool CursorConflictCooling(int now) => _lastCursorConflict != int.MinValue
            && Age(now, _lastCursorConflict) < 320;

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportCursorConflict(Aim aim)
        {
#if WOL_DIAGNOSTIC
            ReadAimDrift(aim, out var drift);
            Trace("Dash cursor drift reason=" + aim.Reason + " dx=" + drift.X + " dy=" + drift.Y);
#endif
        }

        private bool QueueDashAim(IGameController game, IWorldCoordinate point, string reason, bool escape,
            int now, bool upkeep = false)
        {
            if (!escape && CursorConflictCooling(now)) return false;
            if (_releasedCycloneAim != null || InputBusy() || !TryProject(point, out var aimed)
                || !GetCursorPos(out var original)) return false;
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || !Hud.Window.IsForeground || !ClaimMovement()) return false;
            bool yieldingBurst = _burstStarted != int.MinValue;
            if (yieldingBurst && Volatile.Read(ref _pressedKey) != 0)
            {
                ReleaseInput(Volatile.Read(ref _inputSerial));
                if (_releasedCycloneAim != null) return false;
            }
            CancelCycloneAim();
            if (!GetCursorPos(out original)) return false;
            int releasedWaveKey = Volatile.Read(ref _wavePress);
            StopWave();
            if (!SetCursorPos(aimed.X, aimed.Y)) return false;
            Trace("Dash cursor queued: " + reason);
            _dashAim = new Aim
            {
                Landing = point.Offset(0, 0, 0), Original = original, Aimed = aimed, Window = window,
                Tick = game.CurrentGameTick, Started = now, Reason = reason, Escape = escape, Upkeep = upkeep,
                ReleasedWave = releasedWaveKey != 0, ReleasedWaveKey = releasedWaveKey,
            };
            if (yieldingBurst)
            {
                _burstStarted = int.MinValue;
                Trace("bell burst yielded to " + reason);
            }
            return true;
        }

        private bool TryPanicPotion(IPlayer me, Keys waveKey, Keys cycloneKey, Keys dashKey, int now)
        {
            var potion = me.Powers.HealthPotionSkill;
            if (potion == null || potion.IsOnCooldown || PotionKey == Keys.None
                || PotionKey == waveKey || PotionKey == cycloneKey || PotionKey == dashKey
                || Held(PotionKey) || InputBusy()
                || (_lastPotionTry != int.MinValue && Age(now, _lastPotionTry) < 1200)) return false;
            ReleaseInput(Volatile.Read(ref _inputSerial));
            if (!BeginInput(PotionKey, DashPulseMs, null)) return false;
            _lastPotionTry = now;
            Trace("panic potion pressed");
            return true;
        }

        private bool MapAllows(IWorldCoordinate point, int now)
        {
            try
            {
                if ((_mapOwner == null || !_mapOwner.Enabled)
                    && (_mapBindAt == int.MinValue || Age(now, _mapBindAt) >= 5000))
                {
                    _mapBindAt = now;
                    _mapOwner = null;
                    _mapQuery = null;
                    foreach (var plugin in Hud.AllPlugins)
                    {
                        if (!plugin.Enabled || plugin.GetType().Name != "s7o_MapViewer"
                            || !(plugin is IEnumerable<KeyValuePair<string, Func<string, object[], object>>> exports)) continue;
                        foreach (var item in exports)
                            if (item.Key == "s7o.MapViewer.v1" && item.Value != null)
                            {
                                _mapOwner = plugin;
                                _mapQuery = item.Value;
                                _mapTick = int.MinValue;
                                break;
                            }
                        if (_mapOwner != null) break;
                    }
                }
                if (_mapOwner == null || !_mapOwner.Enabled || _mapQuery == null) return true;
                var game = Hud.Game;
                int tick = game.CurrentGameTick;
                uint world = game.Me.WorldId;
                if (_mapTick != tick || _mapWorld != world)
                {
                    _mapTick = tick;
                    _mapWorld = world;
                    _mapEpoch = null;
                    var status = _mapQuery("player", null) as IDictionary<string, object>;
                    if (status != null && status.TryGetValue("ok", out var ok) && Equals(ok, true)
                        && status.TryGetValue("worldId", out var id) && Convert.ToUInt32(id) == world
                        && status.TryGetValue("epoch", out var epoch)) _mapEpoch = epoch;
                }
                if (_mapEpoch == null) return true;
                var answer = _mapQuery("point", new[] { _mapEpoch, point.X, point.Y, point.Z }) as IDictionary<string, object>;
                return answer == null || !answer.TryGetValue("state", out var state)
                    || (!Equals(state, "BlockedCandidate") && !Equals(state, "NoWalkCandidate"));
            }
            catch { return true; } // A missing map reader must leave native Dash available.
        }

        private bool DashBuffDue(IPlayer me, IPlayerSkill dash, int now, bool assist)
        {
            if (dash.Charges <= (assist ? 1 : 0) || (_lastDashBuffTry != int.MinValue && Age(now, _lastDashBuffTry) < 900))
                return false;
            string rune = dash.RuneNameEnglish ?? "";
            bool fourSecondRune = dash.Rune == 4 || rune.IndexOf("Radiance", StringComparison.OrdinalIgnoreCase) >= 0
                || rune.IndexOf("Blinding Speed", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!fourSecondRune) return false;
            var buff = me.Powers.GetBuff(dash.SnoPower.Sno);
            var left = buff?.TimeLeftSeconds;
            var elapsed = buff?.TimeElapsedSeconds;
            double remaining = 0;
            bool found = false;
            if (left != null && elapsed != null)
                for (int i = 0; i < Math.Min(left.Length, elapsed.Length); i++)
                {
                    double duration = left[i] + Math.Max(0, elapsed[i]);
                    if (duration < 3.75 || duration > 4.25 || double.IsNaN(duration)) continue;
                    found = true;
                    remaining = Math.Max(remaining, left[i]);
                }
            return found ? remaining <= .6 : _lastDashPress == int.MinValue || Age(now, _lastDashPress) >= 3400;
        }

        private static bool HasEnemyNear(IGameController game, IWorldCoordinate point, int range)
        {
            return game.AliveMonsters != null && game.AliveMonsters.Any(monster => monster != null
                && monster.WorldId == game.Me.WorldId && monster.IsAlive && monster.Attackable
                && monster.FloorCoordinate != null && Math.Abs(monster.FloorCoordinate.Z - point.Z) <= 8f
                && monster.FloorCoordinate.XYDistanceTo(point) <= range);
        }

        private enum CircleStage { None, Outbound, Attacking, Returning }

        private void ClearStage()
        {
            _stagePhase = CircleStage.None;
            _stageBursts = 0;
            _stageCircle = _stageElite = 0;
            _stageHome = null;
            _stageStarted = int.MinValue;
        }

        private bool StageOutboundStillUseful(IGameController game, Aim aim)
        {
            return _stagePhase == CircleStage.Outbound && IsLiveCircle(game, _stageCircle)
                && _pack.Any(body => body.Id == _stageElite && body.Pullable)
                && aim.Landing != null;
        }

        // Dash only when the active circle is too far for Wave but a regular
        // bell/Cyclone rotation can pull a mobile elite toward it.
        private bool TryStartCircleStage(IGameController game, IWorldCoordinate origin, int pullRange, int now)
        {
            if (!DashToCircles || game.Actors == null || InputBusy() || _dashFlight != null
                || _dashAim != null || _relayHome != null || _walkOrigin != null
                || DangerAt(origin, landing: true) != null) return false;
            var coverage = CircleCoverage(origin, pullRange);
            if (ClosestElite(origin).Tier == 2 || coverage.RangedElites > 0
                || coverage.StageElites == 0) return false;
            IActor ring = null;
            int bestRank = 0;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.IsDisabled || actor.WorldId != game.Me.WorldId
                    || actor.AcdId == 0 || actor.FloorCoordinate == null
                    || origin.XYDistanceTo(actor.FloorCoordinate) > CircleRadius - .5f) continue;
                int rank = CirclePriority(CircleKind(actor));
                if (rank > bestRank) { ring = actor; bestRank = rank; }
            }
            if (ring == null) return false;
            Body elite = default(Body);
            float bestGap = float.MaxValue;
            foreach (var body in _pack)
            {
                if (!body.Pullable || body.Id == 0 || Math.Abs(origin.Z - body.Z) > 8f) continue;
                float gap = Math.Max(0f, origin.XYDistanceTo(body.Point) - body.Radius);
                if (gap <= WaveReach || gap > StageMaxRange || gap >= bestGap
                    || (_stageLastCircle == ring.AcdId && _stageLastElite == body.Id
                        && _stageLastAt != int.MinValue && Age(now, _stageLastAt) < 3000)) continue;
                elite = body;
                bestGap = gap;
            }
            if (elite.Id == 0) return false;
            float distance = origin.XYDistanceTo(elite.Point);
            float stand = pullRange - 3f + elite.Radius;
            var stage = elite.Point.Offset((origin.X - elite.X) * stand / distance,
                (origin.Y - elite.Y) * stand / distance, 0);
            if (!ValidDashLanding(origin, stage, now) || !DashRouteClear(origin, stage, now)
                || !ValidDashLanding(stage, origin, now) || !DashRouteClear(stage, origin, now)) return false;
            if (!QueueDashAim(game, stage, "circle attack stage", false, now)) return false;
            _dashAim.StageOut = true;
            _stageHome = origin.Offset(0, 0, 0);
            _stageCircle = _stageLastCircle = ring.AcdId;
            _stageElite = _stageLastElite = elite.Id;
            _stageLastAt = _stageStarted = now;
            _stageBursts = 0;
            _stagePhase = CircleStage.Outbound;
            Trace("circle attack stage elite=" + elite.Id + " gap=" + bestGap.ToString("F1")
                + " outbound=" + origin.XYDistanceTo(stage).ToString("F1")
                + " pullRange=" + pullRange);
            return true;
        }

        private bool AdvanceCircleStage(IGameController game, IWorldCoordinate origin, int pullRange,
            bool dashReady, int now)
        {
            if (_stagePhase == CircleStage.None) return false;
            if (!IsLiveCircle(game, _stageCircle) || _stageHome == null
                || (_stageStarted != int.MinValue && Age(now, _stageStarted) > 2500))
            {
                ClearStage();
                return false;
            }
            if (_stagePhase == CircleStage.Outbound || _stagePhase == CircleStage.Returning) return true; // Wait for verified Dash/route completion.
            if (_stagePhase != CircleStage.Attacking || _stageBursts == 0 || _burstStarted != int.MinValue) return false;

            Body target = default(Body);
            foreach (var body in _pack) if (body.Id == _stageElite) { target = body; break; }
            float homeGap = target.Id == 0 ? 0f : Math.Max(0f, _stageHome.XYDistanceTo(target.Point) - target.Radius);
            if (target.Id != 0 && homeGap > WaveReach && _stageBursts < 2) return false;
            if (target.Id != 0 && homeGap > WaveReach)
            {
                Trace("circle attack stage did not bring elite into Wave reach; resume combat");
                _failedCircle = _stageCircle;
                _failedCircleUntil = unchecked(now + 1800);
                ClearStage();
                return false;
            }
            if (!dashReady || InputBusy()) return false; // Continue attacking while Dash is unavailable.
            if (DangerAt(_stageHome, landing: true) != null || !MapAllows(_stageHome, now))
            {
                ClearStage();
                return false;
            }
            var ring = game.Actors?.FirstOrDefault(actor => actor != null && actor.AcdId == _stageCircle
                && actor.WorldId == game.Me.WorldId && !actor.IsDisabled);
            if (ring?.FloorCoordinate == null) { ClearStage(); return false; }
            var home = new Aim { Landing = _stageHome, CircleCenter = ring.FloorCoordinate,
                CircleId = _stageCircle, PursuitId = _stageCircle, StageReturn = true,
                Reason = "circle attack return" };
            if (!ValidDashLanding(origin, _stageHome, now) || !DashRouteClear(origin, _stageHome, now)
                || !QueueCircleDash(game, home, now)) return false;
            _stagePhase = CircleStage.Returning;
            Trace("circle attack return queued homeGap=" + homeGap.ToString("F1"));
            return true;
        }

        private int CircleKind(IActor actor)
        {
            var sno = actor.SnoActor.Sno;
            if (sno == ActorSnoEnum._generic_proxy)
            {
                var attr = Hud.Sno.Attributes;
                if (actor.GetAttributeValueAsInt(attr.Power_Buff_1_Visual_Effect_None, Hud.Sno.SnoPowers.OculusRing.Sno, 0) == 1)
                    return 3;
                if (actor.GetAttributeValueAsInt(attr.Power_Buff_1_Visual_Effect_None, 488071u, 0) == 1)
                    return 2;
                if (actor.GetAttributeValueAsInt(attr.Power_Buff_7_Visual_Effect_None, 488071u, 0) == 1)
                    return 1;
                if (actor.GetAttributeValueAsInt(attr.Power_Buff_6_Visual_Effect_None, 488071u, 0) == 1)
                    return 4;
            }
            if (sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_purple
                || sno == ActorSnoEnum._p75_itempassive_unique_ring_017_dome_purple_red) return 2;
            return sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_blue ? 1
                : sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_teal ? 4 : 0;
        }

        private bool TryProject(IWorldCoordinate point, out Cursor result)
        {
            result = default(Cursor);
            var screen = point.ToScreenCoordinate(true, true);
            if (screen == null || float.IsNaN(screen.X) || float.IsNaN(screen.Y)
                || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y)) return false;
            int x = (int)Math.Round(screen.X), y = (int)Math.Round(screen.Y);
            if (!Hud.Window.GroundRectangle.Contains(x, y)
                || Hud.Render.MinimapUiElement.Rectangle.Contains(x, y)) return false;
            var offset = Hud.Window.Offset;
            result = new Cursor { X = x + offset.X, Y = y + offset.Y };
            return true;
        }

        private static bool IsGrotesque(ActorSnoEnum sno)
        {
            switch (sno)
            {
                case ActorSnoEnum._corpulent_a:
                case ActorSnoEnum._corpulent_b:
                case ActorSnoEnum._corpulent_c:
                case ActorSnoEnum._corpulent_d:
                case ActorSnoEnum._corpulent_a_unique_01:
                case ActorSnoEnum._corpulent_a_unique_02:
                case ActorSnoEnum._corpulent_a_unique_03:
                case ActorSnoEnum._corpulent_b_unique_01:
                case ActorSnoEnum._corpulent_c_oasisambush_unique:
                case ActorSnoEnum._corpulent_d_cultistsurvivor_unique:
                case ActorSnoEnum._corpulent_d_unique_spec_01:
                case ActorSnoEnum._corpulent_frost_a: return true;
                default: return false;
            }
        }

        private sealed class TimedHazard
        {
            internal IWorldCoordinate Point;
            internal int Start, Seen;
        }

        private struct Hazard
        {
            internal IWorldCoordinate Point;
            internal float Radius;
            internal bool Imminent;
            internal string Reason;
        }

        private readonly Dictionary<uint, TimedHazard> _corpses = new Dictionary<uint, TimedHazard>();
        private readonly Dictionary<uint, TimedHazard> _molten = new Dictionary<uint, TimedHazard>();
        private readonly List<Hazard> _hazards = new List<Hazard>();
        private readonly List<Hazard> _frozen = new List<Hazard>();
        private readonly List<IActor> _moltenFx = new List<IActor>();
        private int _hazardTick = int.MinValue;
        private float _hazardHealth = 1f;
        private bool _shielded, _serenityProtected;

        private void ClearDanger()
        {
            _corpses.Clear();
            _molten.Clear();
            _hazards.Clear();
            _frozen.Clear();
            _hazardTick = int.MinValue;
            _shielded = _serenityProtected = false;
            _healthBaseline = 0;
            _panicRecovering = false;
            _serenityPending = false;
            _postCirclePullId = 0;
        }

        private void UpdateDanger(IGameController game, IPlayer me, float health, int now)
        {
            _hazardHealth = health;
            if (!AvoidDanger) return;
            var shield = me.Powers.GetBuff(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable.Sno);
            var serenity = me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_Serenity.Sno);
            bool serenitySafe = serenity?.Active == true && serenity.TimeLeftSeconds != null
                && serenity.TimeLeftSeconds.Any(left => left > 1.5);
            if (serenitySafe != _serenityProtected)
                Trace(serenitySafe ? "Serenity confirmed: hazard evasion deferred"
                    : "Serenity protection margin reached: hazard evasion restored");
            _serenityProtected = serenitySafe;
            _shielded = (shield?.Active == true && shield.TimeLeftSeconds != null
                && shield.TimeLeftSeconds.Any(left => left > 1.5)) || serenitySafe;
            if (_shielded) _panicUntil = now;
            if (_healthBaseline <= 0 || health >= _healthBaseline || Age(now, _healthSampleAt) > 220)
            {
                _healthBaseline = health;
                _healthSampleAt = now;
            }
            if (!_shielded && health > 0 && health <= .40f && _healthBaseline - health >= .05f
                && (_lastEscape == int.MinValue || Age(now, _lastEscape) >= 1100))
            {
                _panicUntil = unchecked(now + 350);
                _healthBaseline = health;
                _healthSampleAt = now;
            }

            int tick = game.CurrentGameTick;
            if (tick == _hazardTick) return;
            if (_hazardTick != int.MinValue && (long)_hazardTick - tick > 60)
            {
                _molten.Clear();
                _corpses.Clear();
            }
            _hazardTick = tick;
            _hazards.Clear();
            _frozen.Clear();
            _moltenFx.Clear();
            foreach (uint key in _molten.Where(p => (long)tick - p.Value.Seen > 240).Select(p => p.Key).ToArray())
                _molten.Remove(key);
            foreach (uint key in _corpses.Where(p => (long)tick - p.Value.Seen > 600).Select(p => p.Key).ToArray())
                _corpses.Remove(key);
            if (game.Actors != null)
                foreach (var actor in game.Actors)
                {
                    if (actor?.SnoActor == null || actor.IsDisabled || actor.WorldId != me.WorldId
                        || actor.FloorCoordinate == null || actor.CentralXyDistanceToMe > 85) continue;
                    var point = actor.FloorCoordinate;
                    switch (actor.SnoActor.Sno)
                    {
                        case ActorSnoEnum._monsteraffix_molten_deathstart_proxy:
                            if (_molten.Count < 64 || _molten.ContainsKey(actor.AcdId))
                                _molten[actor.AcdId] = new TimedHazard { Point = point.Offset(0, 0, 0), Seen = tick,
                                    Start = actor.CreatedAtInGameTick > 0 && actor.CreatedAtInGameTick <= tick
                                        ? actor.CreatedAtInGameTick : tick - 138 };
                            break;
                        case ActorSnoEnum._monsteraffix_molten_deathexplosion_proxy:
                        case ActorSnoEnum._monsteraffix_molten_firering:
                            _moltenFx.Add(actor);
                            break;
                        case ActorSnoEnum._monsteraffix_frozen_iceclusters:
                            int age = actor.CreatedAtInGameTick > 0 ? tick - actor.CreatedAtInGameTick : 156;
                            if (age < 195) _frozen.Add(new Hazard { Point = point, Radius = 15.8f, Imminent = age >= 156 });
                            break;
                        case ActorSnoEnum._x1_monsteraffix_frozenpulse_monster:
                            AddHazard(point, 16, health <= .75f, "Frozen Pulse");
                            break;
                        case ActorSnoEnum._skeletonmage_fire_groundpool:
                        case ActorSnoEnum._skeletonmage_fire_groundimpact:
                            if (health <= .75f) AddHazard(point, 8, true, "ground fire");
                            break;
                        case ActorSnoEnum._monsteraffix_desecrator_damage_aoe:
                            if (health <= .40f) AddHazard(point, 8, true, "Desecrator");
                            break;
                        case ActorSnoEnum._monsteraffix_plagued_endcloud:
                            if (health <= .40f) AddHazard(point, 12, true, "Plagued");
                            break;
                        case ActorSnoEnum._monsteraffix_arcaneenchanted_petsweep:
                        case ActorSnoEnum._monsteraffix_arcaneenchanted_petsweep_reverse:
                        case ActorSnoEnum._arcaneenchanteddummy_spawn:
                        case ActorSnoEnum._monsteraffix_avenger_arcaneenchanted_petsweep:
                        case ActorSnoEnum._monsteraffix_avenger_arcaneenchanted_petsweep_reverse:
                            AddHazard(point, 6.5f, true, "Arcane core");
                            break;
                    }
                }
            foreach (var warning in _molten.Values)
            {
                int age = tick - warning.Start;
                if (age < 180) AddHazard(warning.Point, 18, age >= 138, "Molten");
            }
            foreach (var actor in _moltenFx)
            {
                var point = actor.FloorCoordinate;
                bool matched = _molten.Values.Any(warning => Math.Abs(point.Z - warning.Point.Z) <= 8f
                    && point.XYDistanceTo(warning.Point) <= 1f && actor.CreatedAtInGameTick >= warning.Start
                    && actor.CreatedAtInGameTick <= warning.Start + 240);
                if (!matched) AddHazard(point, 18, true, "Molten impact");
            }
            if (game.Monsters != null)
                foreach (var monster in game.Monsters)
                {
                    if (monster?.SnoActor == null || monster.Illusion || monster.WorldId != me.WorldId
                        || !IsGrotesque(monster.SnoActor.Sno) || monster.FloorCoordinate == null
                        || monster.CentralXyDistanceToMe > 85) continue;
                    if (monster.IsAlive) { _corpses.Remove(monster.AcdId); continue; }
                    if (!_corpses.TryGetValue(monster.AcdId, out var corpse))
                    {
                        if (_corpses.Count >= 128) continue;
                        corpse = new TimedHazard { Start = tick, Point = monster.FloorCoordinate.Offset(0, 0, 0) };
                        _corpses.Add(monster.AcdId, corpse);
                    }
                    corpse.Seen = tick;
                }
            foreach (var corpse in _corpses.Values)
            {
                int age = tick - corpse.Start;
                if (age <= 90) AddHazard(corpse.Point, 25, age >= 48, "Grotesque");
            }
#if WOL_DIAGNOSTIC
            if (_serenityProtected && me.FloorCoordinate != null
                && (_lastSerenityCoverageLog == int.MinValue || Age(now, _lastSerenityCoverageLog) >= 750))
                foreach (var hazard in _hazards)
                    if (hazard.Imminent && InsideHazard(me.FloorCoordinate, hazard, 0f))
                    {
                        _lastSerenityCoverageLog = now;
                        int range = me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Monk_CycloneStrike)?.Rune == 1 ? 34 : 24;
                        ReportSerenity(me, "covering imminent " + hazard.Reason
                            + " circleOccupied=" + OccupiesCombatCircle(game, me.FloorCoordinate, range));
                        break;
                    }
#endif
        }

        private void AddHazard(IWorldCoordinate point, float radius, bool imminent, string reason)
        {
            _hazards.Add(new Hazard { Point = point, Radius = radius, Imminent = imminent, Reason = reason });
        }

        private static bool InsideHazard(IWorldCoordinate point, Hazard hazard, float margin)
        {
            return Math.Abs(point.Z - hazard.Point.Z) <= 8f
                && point.XYDistanceTo(hazard.Point) <= hazard.Radius + margin;
        }

        private string DangerAt(IWorldCoordinate point, bool landing)
        {
            if (!AvoidDanger || _shielded || point == null) return null;
            float margin = landing ? .5f : 0f;
            foreach (var hazard in _hazards)
                if ((landing || hazard.Imminent) && InsideHazard(point, hazard, margin)) return hazard.Reason;
            int overlap = 0;
            bool imminent = false;
            foreach (var warning in _frozen)
                if (InsideHazard(point, warning, margin))
                { overlap++; imminent |= warning.Imminent; }
            if (overlap > 0 && _hazardHealth <= .40f) return "Frozen";
            return overlap >= 3 && (landing || imminent) ? "Frozen overlap" : null;
        }

        private static readonly float[] EscapeDistances = { 5, 8, 12, 16, 22, 28, 34 };
        private static readonly float[] EscapeX = Enumerable.Range(0, 12)
            .Select(i => (float)Math.Cos(i * Math.PI / 6)).ToArray();
        private static readonly float[] EscapeY = Enumerable.Range(0, 12)
            .Select(i => (float)Math.Sin(i * Math.PI / 6)).ToArray();

        private IWorldCoordinate PickEscape(IGameController game, IPlayer me, int pullRange, bool panic, bool seekGlobe,
            bool dashReady, out IActor safeCircle)
        {
            safeCircle = null;
            var origin = me.FloorCoordinate;
            var hostile = game.AliveMonsters?.Where(monster => monster != null && monster.WorldId == me.WorldId
                && monster.IsAlive && monster.Attackable && monster.FloorCoordinate != null
                && Math.Abs(monster.FloorCoordinate.Z - origin.Z) <= 8f && monster.CentralXyDistanceToMe <= 30)
                .OrderBy(monster => monster.CentralXyDistanceToMe).FirstOrDefault()?.FloorCoordinate;
            if (panic && hostile == null) return null;
            int now = Environment.TickCount;
            IWorldCoordinate best = null;
            double bestScore = double.MaxValue;
            foreach (float distance in EscapeDistances)
            {
                if (distance > bestScore + 3 || (panic && distance > 16)) break;
                for (int i = 0; i < EscapeX.Length; i++)
                {
                    var point = origin.Offset(EscapeX[i] * distance, EscapeY[i] * distance, 0);
                    if (DangerAt(point, landing: true) != null || !TryProject(point, out _)) continue;
                    double gap = hostile == null ? 0 : point.XYDistanceTo(hostile);
                    if (panic && gap < origin.XYDistanceTo(hostile) + 3) continue;
                    double score = distance + Math.Max(0, gap - pullRange);
                    if (score - 3 >= bestScore) continue;
                    var pack = CountPack(point, pullRange);
                    if (pack.Elites > 0) score -= Math.Min(3, pack.PullTrash * .4);
                    if (score >= bestScore || !MapAllows(point, now)) continue;
                    best = point;
                    bestScore = score;
                }
            }
            if (seekGlobe && HealInPanic && game.Actors != null)
            {
                // Globes collect by proximity. A detour is allowed only if the landing
                // is safe, close to the normal escape, and still leaves room to fight.
                IWorldCoordinate globe = null;
                double bestGlobe = Math.Min(22, best == null ? 22 : origin.XYDistanceTo(best) + 5);
                foreach (var actor in game.Actors)
                {
                    if (actor?.SnoActor?.Kind != ActorKind.HealthGlobe || actor.WorldId != me.WorldId
                        || actor.IsDisabled || actor.FloorCoordinate == null) continue;
                    var point = actor.FloorCoordinate;
                    double distance = origin.XYDistanceTo(point);
                    if (distance <= 2 || distance >= bestGlobe || Math.Abs(point.Z - origin.Z) > 8
                        || (hostile != null && (point.XYDistanceTo(hostile) > pullRange
                            || (panic && point.XYDistanceTo(hostile) < origin.XYDistanceTo(hostile) + 3)))
                        || DangerAt(point, landing: true) != null || !TryProject(point, out _)
                        || !MapAllows(point, now)) continue;
                    globe = point;
                    bestGlobe = distance;
                }
                if (globe != null) return globe;
            }
            if (!DashToCircles || game.Actors == null) return best;
            int bestKind = 0;
            double bestCircleScore = 0;
            float reach = dashReady ? (panic ? 20f : 34f)
                : Math.Min(12f, best == null ? 12f : origin.XYDistanceTo(best) + 3f);
            bool eliteFight = NearElite(origin);
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.AcdId == 0 || actor.WorldId != me.WorldId
                    || actor.IsDisabled || actor.FloorCoordinate == null) continue;
                int kind = CircleKind(actor);
                if (kind == 0) continue;
                var landing = InsideRim(actor.FloorCoordinate, origin);
                if (origin.XYDistanceTo(landing) > reach || !ValidDashLanding(origin, landing, now)
                    || (!panic && !CircleKeepsEliteContact(origin, landing, pullRange))
                    || (panic && hostile != null && landing.XYDistanceTo(hostile) < origin.XYDistanceTo(hostile) + 3))
                    continue;
                var coverage = CircleCoverage(landing, pullRange);
                if (!CoverageEnough(coverage, eliteFight)) continue;
                double value = CombatCircleScore(coverage, kind, landing, pullRange);
                if (value == double.MinValue) value = CircleValue(coverage, kind) * .5; // Safety first during panic.
                value -= origin.XYDistanceTo(landing) * .35;
                if (!BetterCircle(kind, value, bestKind, bestCircleScore)) continue;
                best = landing;
                bestKind = kind;
                bestCircleScore = value;
                safeCircle = actor;
            }
            return best;
        }

        private void CollectBells(IGameController game, IPlayer me, int radius)
        {
            _bells.Clear();
            _ownSummoners.Clear();
            if (me.SummonerId != 0) _ownSummoners.Add(me.SummonerId);
            if (game.Actors == null) return;

            // When another Monk is present, require ownership. A lone Monk can use
            // the nearby actors even if the client omits the clone's ownership.
            bool anotherMonk = game.NumberOfPlayersInGame > 1 && (game.Players == null
                || game.Players.Any(player => player != null && !player.IsMe
                    && player.HeroClassDefinition?.HeroClass == HeroClass.Monk));
            if (anotherMonk && me.SummonerId != 0)
                foreach (var actor in game.Actors)
                {
                    if (actor?.SnoActor == null || actor.WorldId != me.WorldId || actor.SummonerId == 0
                        || actor.SummonerAcdDynamicId != me.SummonerId) continue;
                    var sno = actor.SnoActor.Sno;
                    if (sno == ActorSnoEnum._p43_mimic_legendary_monk_male
                        || sno == ActorSnoEnum._p43_mimic_legendary_monk_female)
                        _ownSummoners.Add(actor.SummonerId);
                }

            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.WorldId != me.WorldId || !IsBell(actor.SnoActor.Sno)
                    || actor.IsDisabled || actor.IsOperated || actor.FloorCoordinate == null
                    || actor.CentralXyDistanceToMe > radius || actor.ZDistanceToMeAbsolute > 8
                    || (anotherMonk && !_ownSummoners.Contains(actor.SummonerAcdDynamicId))) continue;
                uint id = actor.AnnId != 0 ? actor.AnnId : actor.AcdId;
                if (id != 0) _bells.Add(id);
            }
            bool newBell = false;
            foreach (var id in _bells)
                if (!_previousBells.Contains(id)) { newBell = true; break; }
            if (newBell)
            {
                _bellSpawnSerial++;
                ReportBellGeometry(game);
                CheckBellPlacement(game, me);
            }
            _previousBells.Clear();
            foreach (var id in _bells) _previousBells.Add(id);
        }

        private void CheckBellPlacement(IGameController game, IPlayer me)
        {
            if (!_assistHeld || _bellAimTarget == 0 || _bellAimOrigin == null || _bellAimPosition == null
                || _bellAimFocus == null || _bellAimAt == int.MinValue
                || Age(Environment.TickCount, _bellAimAt) > 1200
                || _bellAimOrigin.XYDistanceTo(_bellAimPosition) < 17f) return;
            bool any = false, nearTarget = false, nearOrigin = true;
            float nearestElite = float.MaxValue;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.FloorCoordinate == null
                    || actor.WorldId != me.WorldId || !IsBell(actor.SnoActor.Sno)
                    || actor.IsDisabled || actor.IsOperated) continue;
                uint id = actor.AnnId != 0 ? actor.AnnId : actor.AcdId;
                if (id == 0 || !_bells.Contains(id) || _previousBells.Contains(id)) continue;
                any = true;
                float targetGap = actor.FloorCoordinate.XYDistanceTo(_bellAimPosition);
                float eliteGap = actor.FloorCoordinate.XYDistanceTo(_bellAimFocus);
                if (targetGap <= 10f) nearTarget = true;
                if (actor.FloorCoordinate.XYDistanceTo(_bellAimOrigin) > 22f) nearOrigin = false;
                nearestElite = Math.Min(nearestElite, eliteGap);
            }
            if (!any) return;
            if (nearTarget)
            {
                _misplacedBellTarget = 0;
                _misplacedBellCount = 0;
                return;
            }
            float wantedEliteGap = _bellAimPosition.XYDistanceTo(_bellAimFocus);
            if (!nearOrigin || nearestElite <= wantedEliteGap + 15f) return;
            int now = Environment.TickCount;
            if (_misplacedBellTarget != _bellAimTarget || _lastMisplacedBell == int.MinValue
                || Age(now, _lastMisplacedBell) > 2000)
                _misplacedBellCount = 0;
            _misplacedBellTarget = _bellAimTarget;
            _lastMisplacedBell = now;
            _misplacedBellCount++;
            Trace("bell placement mismatch target=" + _bellAimTarget
                + " misses=" + _misplacedBellCount + " actualElite=" + nearestElite.ToString("F1")
                + " requestedElite=" + wantedEliteGap.ToString("F1"));
            if (_misplacedBellCount >= 2)
                BeginBellRecovery(_bellAimTarget, _bellAimOrigin, _bellAimFocus,
                    "repeated native bells appeared near hero instead of aimed target");
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportBellGeometry(IGameController game)
        {
#if WOL_DIAGNOSTIC
            if (_bellAimTarget == 0 || _bellAimFocus == null || game.Actors == null) return;
            int now = Environment.TickCount;
            if (_lastBellPlacementLog != int.MinValue && Age(now, _lastBellPlacementLog) < 120) return;
            _lastBellPlacementLog = now;
            float nearest = float.MaxValue, farthestHero = 0f;
            int count = 0;
            foreach (var actor in game.Actors)
            {
                if (actor?.SnoActor == null || actor.FloorCoordinate == null
                    || !IsBell(actor.SnoActor.Sno) || actor.IsDisabled) continue;
                uint id = actor.AnnId != 0 ? actor.AnnId : actor.AcdId;
                if (id == 0 || !_bells.Contains(id) || _previousBells.Contains(id)) continue;
                count++;
                nearest = Math.Min(nearest, actor.FloorCoordinate.XYDistanceTo(_bellAimFocus));
                if (game.Me?.FloorCoordinate != null)
                    farthestHero = Math.Max(farthestHero, actor.FloorCoordinate.XYDistanceTo(game.Me.FloorCoordinate));
            }
            if (count == 0) return;
            Trace("bell geometry target=" + _bellAimTarget + " slot=" + _bellAimSlot
                + " new=" + count + " closestToElite=" + nearest.ToString("F1")
                + " aimElite=" + (_bellAimPosition?.XYDistanceTo(_bellAimFocus) ?? -1).ToString("F1")
                + " farthestNewBellToHero=" + farthestHero.ToString("F1"));
#endif
        }

        private static bool IsBell(ActorSnoEnum sno)
        {
            switch (sno)
            {
                case ActorSnoEnum._p74_monk_bell_waveoflight:
                case ActorSnoEnum._p74_monk_bell_waveoflight_runea:
                case ActorSnoEnum._p74_monk_bell_waveoflight_runeb:
                case ActorSnoEnum._p74_monk_bell_waveoflight_runec:
                case ActorSnoEnum._p74_monk_bell_waveoflight_runed:
                case ActorSnoEnum._p74_monk_bell_waveoflight_runee:
                    return true;
                default:
                    return false;
            }
        }

        private Keys Binding(ActionKey slot)
        {
            switch (slot)
            {
                case ActionKey.LeftSkill: return Keys.LButton;
                case ActionKey.RightSkill: return Keys.RButton;
                case ActionKey.Skill1: return Skill1Key;
                case ActionKey.Skill2: return Skill2Key;
                case ActionKey.Skill3: return Skill3Key;
                case ActionKey.Skill4: return Skill4Key;
                default: return Keys.None;
            }
        }

        private static bool Held(Keys key) => key != Keys.None && (GetAsyncKeyState((int)key) & 0x8000) != 0;
        private static int Age(int now, int then) => unchecked(now - then);

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void Trace(string text)
        {
#if WOL_DIAGNOSTIC
            try { Hud.TextLog.Log("s7o_WaveOfLightCyclone_REV45_DIAGNOSTIC", text); }
            catch { } // Logging never owns input.
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void SetCircleStatus(string status)
        {
#if WOL_DIAGNOSTIC
            _circleStatus = status;
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportCombat(bool assist, IPlayer me, IPlayerSkill dash, bool holy, int now)
        {
#if WOL_DIAGNOSTIC
            if (Age(now, _lastStatus) < 1000) return;
            _lastStatus = now;
            var coe = me.Powers.GetBuff(Hud.Sno.SnoPowers.ConventionOfElements.Sno);
            Trace("combat mode=" + (assist ? "space" : "manual") + " tick=" + Hud.Game.CurrentGameTick
                + " bells=" + _bells.Count + " burstHeldMs=" + (_burstStarted == int.MinValue ? 0 : Age(Environment.TickCount, _burstStarted)) + " charges=" + (dash?.Charges ?? 0)
                + " dashCooldown=" + (dash?.IsOnCooldown ?? false) + " spirit=" + me.Stats.ResourceCurPri.ToString("F0")
                + " coe=" + (holy ? "Holy" : "normal")
                + " coeLeft=" + (holy ? ConventionLeft(coe, 4) : 0).ToString("F2")
                + " focus=" + ClosestElite(me.FloorCoordinate).Id
                + " focusTier=" + ClosestElite(me.FloorCoordinate).Tier
                + " focusGap=" + EliteEdgeGap(me.FloorCoordinate).ToString("F1")
                + " circle=" + _circleStatus);
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportHudGate(IGameController game, IPlayer me)
        {
#if WOL_DIAGNOSTIC
            if (!_holdActive && !Held(AssistKey)) return;
            ReportWave("HUD blocked game=" + game.IsInGame + " paused=" + game.IsPaused + " loading=" + game.IsLoading
                + " town=" + game.IsInTown + " foreground=" + Hud.Window.IsForeground + " map=" + game.MapMode
                + " panel=" + _blockingUi.Any(ui => ui?.Visible == true)
                + " inventory=" + (Hud.Inventory?.InventoryMainUiElement?.Visible ?? false)
                + " stash=" + (Hud.Inventory?.StashMainUiElement?.Visible ?? false)
                + " override=" + (me?.Powers?.SkillOverrideActive ?? false) + " dead=" + (me?.IsDeadSafeCheck ?? false)
                + " stunned=" + (me?.Powers?.Stunned ?? false) + " frozen=" + (me?.Powers?.Frozen ?? false), me, null);
#endif
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportWave(string state, IPlayer me, IPlayerSkill wave)
        {
#if WOL_DIAGNOSTIC
            if (!_assistHeld && !_holdActive && !Held(AssistKey)) return;
            int now = Environment.TickCount;
            bool changed = !string.Equals(state, _lastWaveState, StringComparison.Ordinal);
            if (!changed && Age(now, _lastWaveLog) < 750) return;
            int previousMs = _lastWaveState == null ? 0 : Age(now, _waveStateSince);
            if (changed) _waveStateSince = now;
            _lastWaveLog = now;
            _lastWaveState = state;
            var selected = Hud.Game.SelectedActor;
            Trace("wave state=" + state + " previousMs=" + (changed ? previousMs : 0)
                + " tick=" + Hud.Game.CurrentGameTick + " ownedKey=" + (Keys)Volatile.Read(ref _wavePress)
                + " slot=" + (wave?.Key ?? ActionKey.Unknown) + " spirit=" + (me?.Stats?.ResourceCurPri ?? 0).ToString("F0")
                + " waveCost=" + (wave?.GetResourceRequirement() ?? 0).ToString("F0")
                + " animation=" + me?.AnimationState + " bells=" + _bells.Count
                + " selected=" + (selected?.SnoActor?.Sno.ToString() ?? "none")
                + " action=" + (_dashAim?.Reason ?? _activeAim?.Reason ?? ((Keys)Volatile.Read(ref _pressedKey)).ToString())
                + " flight=" + (_dashFlight?.Reason ?? "none"));
#endif
        }

        private bool InputBusy() => _otherInputOwner?.Invoke(InputOwner) == true;

        // The pack arbiter runs on the HUD thread. Reserve movement there and keep the
        // timed key release independent so it still runs if collection stalls.
        private bool ClaimMovement()
        {
            if (InputBusy()) return false;
            if (_movementClaimed || _claimInput == null) return true;
            _movementClaimed = _claimInput(InputOwner, MovementClaim, ClaimMarker);
            return _movementClaimed;
        }

        private void ReleaseFinishedClaim()
        {
            if (!_movementClaimed || _dashAim != null || _activeAim != null
                || _dashFlight != null || _cycloneSelfAim != null || _releasedCycloneAim != null) return;
            _releaseInputClaim(InputOwner, MovementClaim, ClaimMarker);
            _movementClaimed = false;
        }

        private string HoldWave(Keys key)
        {
            lock (_inputLock)
            {
                int vk = (int)key;
                if (!Held(AssistKey) || !Hud.Window.IsForeground)
                {
                    StopWave();
                    return "Space or focus lost";
                }
                if (vk <= 0 || vk > 254 || vk == _pressedKey || vk == _epiphanyKey) return "Wave binding blocked";
                if (_wavePress != 0)
                {
                    if (_wavePress == vk && Held(key))
                    {
                        _waveHeartbeat = Environment.TickCount;
                        return "held at self";
                    }
                    Trace("Wave hold interrupted outside its owner; rearming");
                    StopWave();
                }
                if (Held(key)) return "manual Wave key already held";
                var window = GetForegroundWindow();
                if (window == IntPtr.Zero) return "no foreground window";
                if (key == Keys.LButton)
                {
                    if (StandstillKey == Keys.None) return "left Wave needs standstill binding";
                    if (!Held(StandstillKey))
                    {
                        int modifier = (int)StandstillKey;
                        if (modifier < 8 || modifier > 254 || !SendKey(modifier, false))
                            return "standstill press failed";
                        _waveStandstill = modifier;
                    }
                }
                if (!SendKey(vk, false))
                {
                    ReleaseWaveStandstill();
                    return "Wave press failed";
                }
                _wavePress = vk;
                _waveHeartbeat = Environment.TickCount;
                _waveWindow = window;
                int serial = ++_waveSerial;
                _waveWatch = new System.Threading.Timer(CheckWaveHold, serial, 65, 65);
                Trace("Wave key down " + key);
                return "held at self";
            }
        }

        private void CheckWaveHold(object state)
        {
            lock (_inputLock)
            {
                if (_wavePress == 0 || (int)state != _waveSerial) return;
                string reason = !Held(AssistKey) ? "Space released"
                    : GetForegroundWindow() != _waveWindow ? "foreground changed"
                    : Age(Environment.TickCount, _waveHeartbeat) >= 400 ? "collector heartbeat expired" : null;
                if (reason == null) return;
                StopWave(log: false);
#if WOL_DIAGNOSTIC
                Volatile.Write(ref _watchdogRelease, reason);
#endif
            }
        }

        private void StopWave(bool log = true)
        {
            lock (_inputLock)
            {
                if (_wavePress == 0) return;
#if WOL_DIAGNOSTIC
                var key = (Keys)_wavePress;
#endif
                try { if (!SendKey(_wavePress, true)) SendKey(_wavePress, true); }
                finally
                {
                    _wavePress = 0;
                    _bellAimAt = int.MinValue;
                    _waveWatch?.Dispose();
                    _waveWatch = null;
                    ReleaseWaveStandstill();
                }
#if WOL_DIAGNOSTIC
                if (log) Trace("Wave key up " + key);
#endif
            }
        }

        private void ReleaseWaveStandstill()
        {
            if (_waveStandstill == 0) return;
            if (!SendKey(_waveStandstill, true)) SendKey(_waveStandstill, true);
            _waveStandstill = 0;
        }

        private void LeaveAssist()
        {
            _assistHeld = false;
            _obstructionRouteTarget = 0;
            _obstructionRouteGoal = _obstructionRouteFocus = null;
            _obstructionRouteAt = _lastObstructionTry = int.MinValue;
            ClearBellRecovery();
            ResetBellAim();
            _bellAimRecoveryUntil = int.MinValue;
            _relayHome = null;
            ClearStage();
            _walkOrigin = null;
            _usefulCircleSeen = false;
            CancelCycloneAim();
            _panicRecovering = false;
            _serenityPending = false;
            _postCirclePullId = 0;
            _panicUntil = Environment.TickCount;
            _healthBaseline = 0;
            StopWave();
            CancelDashAim();
            _dashFlight = null;
            if (_activeAim != null || _burstStarted != int.MinValue)
                ReleaseInput(Volatile.Read(ref _inputSerial));
            _burstStarted = int.MinValue;
            _burstKey = 0;
            _burstHoldMs = 0;
            _burstYieldUntil = int.MinValue;
            ReleaseSnap();
            ReleaseFinishedClaim();
        }

        private void TryEpiphany(IPlayer me, ISnoPower power, Keys waveKey, Keys cycloneKey)
        {
            int now = Environment.TickCount;
            if (!MaintainEpiphany || InputBusy()
                || (_lastEpiphany != int.MinValue && Age(now, _lastEpiphany) < 220)) return;
            var skill = me.Powers.GetUsedSkill(power);
            ReportEpiphany(me, skill, power);
            if (skill == null || skill.IsOnCooldown) return;
            var key = Binding(skill.Key);
            if (key == Keys.None || key == AssistKey || key == waveKey || key == cycloneKey) return;
            lock (_inputLock)
            {
                if (_epiphanyKey != 0 || _pressedKey == (int)key || Held(key) || !Hud.Window.IsForeground) return;
                if (key == Keys.LButton)
                {
                    var actor = Hud.Game.SelectedActor;
                    if (actor?.IsClickable == true && !(actor is IMonster)) return;
                    if (StandstillKey == Keys.None) return;
                }
                // An escape already underway takes priority; normal attacks and offensive movement do not.
                if (_dashAim?.Escape == true || _dashFlight?.Escape == true || _activeAim?.Escape == true)
                    return;
                bool interrupted = _wavePress != 0 || _pressedKey != 0 || _dashAim != null
                    || _cycloneSelfAim != null || _burstStarted != int.MinValue;
                StopWave();
                if (_pressedKey != 0) ReleaseInput(_inputSerial);
                if (_dashAim != null) CancelDashAim();
                if (_cycloneSelfAim != null) CancelCycloneAim();
                if (_burstStarted != int.MinValue)
                {
                    ReportCycloneEffects();
                    _burstStarted = int.MinValue;
                    _burstKey = _burstHoldMs = 0;
                    _burstYieldUntil = int.MinValue;
                }
                if (interrupted) Trace("Epiphany preempted offensive action");
                if (key == Keys.LButton && !Held(StandstillKey))
                {
                    if (!SendKey((int)StandstillKey, false)) return;
                    _epiphanyStandstill = (int)StandstillKey;
                }
                _lastEpiphany = now;
                if (!SendKey((int)key, false))
                {
                    ReleaseEpiphanyStandstill();
                    Trace("Epiphany press failed");
                    return;
                }
                _epiphanyKey = (int)key;
                int serial = ++_epiphanySerial;
                Task.Delay(DashPulseMs).ContinueWith(_ => ReleaseEpiphany(serial), TaskScheduler.Default);
#if WOL_DIAGNOSTIC
                _epiphanyConfirmationPending = true;
#endif
                Trace("Epiphany pressed off cooldown, buffActive=" + me.Powers.BuffIsActive(power.Sno)
                    + " mode=" + (Held(AssistKey) ? "space" : Held(waveKey) ? "manual" : "idle"));
            }
        }

        [System.Diagnostics.Conditional("WOL_DIAGNOSTIC")]
        private void ReportEpiphany(IPlayer me, IPlayerSkill skill, ISnoPower power)
        {
#if WOL_DIAGNOSTIC
            if (!_epiphanyConfirmationPending) return;
            _epiphanyConfirmationPending = false;
            Trace("Epiphany after input cooldown=" + (skill?.IsOnCooldown ?? false)
                + " buffActive=" + me.Powers.BuffIsActive(power.Sno));
#endif
        }

        private void ReleaseEpiphany(int serial)
        {
            lock (_inputLock)
            {
                if (_epiphanyKey == 0 || serial != _epiphanySerial) return;
                if (!SendKey(_epiphanyKey, true)) SendKey(_epiphanyKey, true);
                _epiphanyKey = 0;
                ReleaseEpiphanyStandstill();
            }
        }

        private void ReleaseEpiphanyStandstill()
        {
            if (_epiphanyStandstill == 0) return;
            if (!SendKey(_epiphanyStandstill, true)) SendKey(_epiphanyStandstill, true);
            _epiphanyStandstill = 0;
        }

        private bool BeginInput(Keys key, int holdMs, Aim aim, bool moveOnly = false, bool ownDash = false)
        {
            int vk = (int)key;
            lock (_inputLock)
            {
                if (_pressedKey != 0 || vk == _wavePress || vk == _epiphanyKey || vk <= 0 || vk > 254 || Held(key) || !Hud.Window.IsForeground
                    || InputBusy() || (aim != null && !StillAiming(aim))) return false;
                if (key == Keys.LButton && !moveOnly)
                {
                    // A force-standstill left skill must never become a pickup or pylon click.
                    var actor = Hud.Game.SelectedActor;
                    if (actor != null && !(actor is IMonster) && actor.IsClickable) return false;
                    if (StandstillKey == Keys.None) return false;
                    if (!Held(StandstillKey))
                    {
                        int modifier = (int)StandstillKey;
                        if (modifier < 8 || modifier > 254 || !SendKey(modifier, false)) return false;
                        _standstillOwned = modifier;
                    }
                }
                int identity = ownDash && _claimInput != null
                    ? (key == Keys.LButton ? 0x10001 : key == Keys.RButton ? 0x10002 : vk) : 0;
                bool sent = identity != 0 ? _claimInput(InputOwner, identity, () => SendKey(vk, false)) : SendKey(vk, false);
                if (!sent)
                {
                    ReleaseStandstill();
                    return false;
                }
                if (identity != 0)
                {
                    aim.SharedDashKey = identity;
                    aim.DashReleaseDue = unchecked(Environment.TickCount + holdMs);
                }
                _activeAim = aim;
                Volatile.Write(ref _pressedKey, vk);
                int serial = ++_inputSerial;
                if (identity == 0)
                    Task.Delay(holdMs).ContinueWith(_ => ReleaseInput(serial), TaskScheduler.Default);
                return true;
            }
        }

        private void ReleaseInput(int serial)
        {
            lock (_inputLock)
            {
                if (_pressedKey == 0 || serial != _inputSerial) return;
                try
                {
                    int key = _pressedKey;
                    int identity = _activeAim?.SharedDashKey ?? 0;
                    bool released = identity != 0 ? _releaseInputClaim(InputOwner, identity, () => SendKey(key, true))
                        : SendKey(key, true) || SendKey(key, true);
#if WOL_DIAGNOSTIC
                    if (_activeAim != null)
                    {
                        _activeAim.ReleaseAt = Environment.TickCount;
                        _activeAim.ReleaseSent = released;
                    }
#endif
                }
                finally
                {
                    ReleaseStandstill();
                    if (_activeAim?.IsCycloneAim == true)
                    {
                        _activeAim.Tick = Volatile.Read(ref _collectionTick);
                        _releasedCycloneAim = _activeAim;
                    }
                    else RestoreAim(_activeAim);
                    _activeAim = null;
                    Volatile.Write(ref _pressedKey, 0);
                }
            }
        }

        private void ReleaseStandstill()
        {
            if (_standstillOwned == 0) return;
            if (!SendKey(_standstillOwned, true)) SendKey(_standstillOwned, true);
            _standstillOwned = 0;
        }

        private void CancelInput()
        {
            _autoLootYieldPending = false;
            _holdActive = false;
            LeaveAssist();
            ReleaseEpiphany(Volatile.Read(ref _epiphanySerial));
            _burstStarted = int.MinValue;
            _burstKey = 0;
            _burstHoldMs = 0;
            _burstYieldUntil = int.MinValue;
            _healthBaseline = 0;
            _serenityPending = false;
            _postCirclePullId = 0;
            _panicUntil = Environment.TickCount;
            _dashFlight = null;
            CancelDashAim();
            ReleaseInput(Volatile.Read(ref _inputSerial));
            ReleaseSnap();
            ReleaseFinishedClaim();
        }

        private void ReleaseSnap()
        {
            if (!_ghosted && _hasSnap && _snapWindow == GetForegroundWindow() && GetCursorPos(out var cursor)
                && NearCursor(cursor, _lastSnap, 12))
                SetCursorPos(_snapOriginal.X + cursor.X - _lastSnap.X, _snapOriginal.Y + cursor.Y - _lastSnap.Y);
            _hasSnap = false;
        }

        private static bool NearCursor(Cursor a, Cursor b, int tolerance)
        {
            return Math.Abs((long)a.X - b.X) <= tolerance && Math.Abs((long)a.Y - b.Y) <= tolerance;
        }

        private void CancelDashAim()
        {
            RestoreAim(_dashAim);
            _dashAim = null;
        }

        private static bool ReadAimDrift(Aim aim, out Cursor drift)
        {
            drift = default(Cursor);
            if (aim.Window != GetForegroundWindow() || !GetCursorPos(out var cursor)) return false;
            drift.X = cursor.X - aim.Aimed.X;
            drift.Y = cursor.Y - aim.Aimed.Y;
            return Math.Abs(drift.X) <= 12 && Math.Abs(drift.Y) <= 12;
        }

        private static bool StillAiming(Aim aim) => ReadAimDrift(aim, out _);

        private bool DashAimReady(IGameController game, Aim aim)
        {
            if (!TryProject(aim.Landing, out var projected) || !ReadAimDrift(aim, out var drift)) return false;
            if (NearCursor(projected, aim.Aimed, 8)) return true;
            if (!SetCursorPos(projected.X, projected.Y)) return false;
            aim.Original.X += drift.X;
            aim.Original.Y += drift.Y;
            aim.Aimed = projected;
            aim.Tick = game.CurrentGameTick;
            Trace("Dash aim refreshed before press");
            return false;
        }

        private bool ReprojectAim(Aim aim)
        {
            if (!ReadAimDrift(aim, out var drift) || !TryProject(aim.Landing, out var projected)) return false;
            if (projected.X == aim.Aimed.X && projected.Y == aim.Aimed.Y) return true;
            if (!SetCursorPos(projected.X, projected.Y)) return false;
            aim.Original.X += drift.X;
            aim.Original.Y += drift.Y;
            aim.Aimed = projected;
            return true;
        }

        private void RestoreAim(Aim aim)
        {
            if (!_ghosted && aim != null && ReadAimDrift(aim, out var drift))
                SetCursorPos(aim.Original.X + drift.X, aim.Original.Y + drift.Y);
        }

        private sealed class Aim
        {
            internal IWorldCoordinate Landing, CircleCenter, OverlapCenter, From, Goal;
            internal uint CircleId, EliteId, PursuitId, TransitCircle;
#if WOL_DIAGNOSTIC
            internal int ReleaseAt;
            internal bool ReleaseSent;
#endif
            internal Cursor Original, Aimed;
            internal IntPtr Window;
            internal int Tick, Started, SentAt, AnimationStart, HoldMs;
            internal int SharedDashKey, DashReleaseDue, ReleasedWaveKey;
            internal double ChargesBefore;
            internal string Reason;
            internal bool Escape, Upkeep, RelayOut, RelayReturn, StageOut, StageReturn, Retried, DashAccepted, Transit, Walk;
            internal bool AnimationObserved, ChargeObserved, ReleasedWave, IsCycloneAim;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Cursor { public int X, Y; }

        private static bool SendKey(int key, bool up)
        {
            var input = new Input
            {
                Type = 1,
                Data = new InputData
                {
                    Keyboard = new KeyboardInput { VirtualKey = (ushort)key, Flags = up ? 2u : 0u }
                }
            };
            if (key == (int)Keys.LButton || key == (int)Keys.RButton)
            {
                input.Type = 0;
                input.Data = new InputData
                {
                    Mouse = new MouseInput { Flags = key == (int)Keys.LButton ? (up ? 4u : 2u) : (up ? 16u : 8u) }
                };
            }
            return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) == 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public InputData Data; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse; // Union size and alignment on x86 and x64.
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Cursor point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    }
}
