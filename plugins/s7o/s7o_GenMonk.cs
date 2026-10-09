using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Assist order: urgent safety, established elite tiers, useful local trash packs,
    // high-value single trash, then stragglers. Circle placement follows that combat choice.
    // Attack reach, circle coverage, acquisition and navigation progress are independent.
    // A failed destination stays blocked until geometry changes, even if landing is unsafe.
    // Keyboard navigation owns a native path lock; a long-reach hit is not WotHF contact.
    // Preserve verified generator/Foresight casts and WotHF's short maintenance heartbeat.
    // Space owns its attacks. Never release/reclick a physically held user LMB or Shift.
    // Synthetic LMB/assist aim retain native UI, pylon and interaction guards.
    // Optional map routes only Space approaches; absence/errors retain native recovery.
    // Manual play steers freely; only Self-Dash temporarily owns and restores its cursor.
    public class s7o_GenMonk : BasePlugin, IAfterCollectHandler, INewAreaHandler, IInGameTopPainter, IS7oAutoLootInputHandoff, IS7oAutoLootPickupGuard
    {
        public bool AutoDash = true;
        public bool SelfDash = true;
        public bool ShowCombinationCount = true;
        public bool RequireRaimentSet = true;
        // Hold this unbound key for optional targeting/attacks; releasing it ends the assist.
        public bool MeleeAssistEnabled = true;
        public ushort MeleeAssistVirtualKey = 0x20; // Space, unbound in Diablo.
        public double MeleeAssistRange = 8.0;
        public float MapWallClearance=2f; // Terrain/prop clearance for routes and landings; independent of attack reach.
        public double NearbyTrashRange = 30.0; // Local trash precedes distant packs; elite tiers stay independent.
        public int AssistApproachRetryMs = 800;
        // Primary generators can be ignored while WotHF is inside a fast attack animation.
        // Hold the injected generator through roughly one high-APS attack window, but release
        // immediately once the intended buff refresh proves the cast registered.
        public int GeneratorPulseMs = 250;
        public int GeneratorPulseMinMs = 35;
        public int DashPulseMs = 55;
        public int GeneratorRetryGapMs = 250;
        // Foresight needs a real third hit; WotHF's animation cannot verify that stage.
        // The current main generator remains manual; refresh the other due contributions
        // first, then allow Deadly Reach to complete its own verified attack sequence.
        public bool RequireHundredFistsMain = false; // Optional strict seasonal-only mode.
        // Combination Strike lasts 10 seconds. Refresh only near expiry, never continuously.
        public double CombinationRefreshAtSeconds = 2.0;
        public double ForesightRefreshAtSeconds = 2.0;
        // Automation is allowed only during a real generator attack with a nearby target.
        public double AttackTargetRange = 12.0;
        public int ForesightRetryGapMs = 350;
        private const string Owner = "GenMonk";
        private ActionKey _mainKey = ActionKey.Unknown;
        private ActionKey _candidateKey = ActionKey.Unknown;
        private int _candidateSinceTick;
        private ActionKey _pulse = ActionKey.Unknown;
        private ActionKey _targetKey = ActionKey.Unknown;
        private uint _targetSno;
        private int _targetIcon;
        private double _targetBefore;
        private int _attempts;
        private int _pulseStartedTick;
        private int _pulseReleaseTick;
        private bool _pulseIsMaintenance;
        private int _nextAttemptTick;
        private bool _targetForesight;
        private bool _hasCombinationStrike;
        private double _targetForesightBefore;
        private readonly Dictionary<uint, int> _retryAfter = new Dictionary<uint, int>();
        // Space-assist progress belongs to the generator, not the monster that just died.
        private readonly Dictionary<uint, int> _assistMaintenanceAttempts = new Dictionary<uint, int>();
        private uint _lastAssistMaintenanceSno;
        private readonly List<IPlayerSkill> _generators = new List<IPlayerSkill>(4);
        private int _lastDashTick = int.MinValue;
        private bool _dashAwaiting;
        private bool _restoreCursor;
        private ActionKey _selfDashKey = ActionKey.Unknown;
        private int _selfDashAimTick, _selfDashGameTick;
        private int _selfDashCastTick = int.MinValue;
        private int _selfDashPreviousDashTick = int.MinValue;
        private string _selfDashAnimationBeforePress;
        private int _selfDashAnimationStartBeforePress, _selfDashPressGameTick;
        private double _selfDashRaimentBefore;
        private double[] _selfDashBuffBefore;
        private int _selfDashSerial;
        private string _selfDashResult = "none";
        private int _cursorX, _cursorY, _dashTargetX, _dashTargetY;
        private string _settingsPath;
        private IFont _combinationFont;
        private IUiElement _combinationIcon;
        private IUiElement _chatEditLine;
        private readonly List<IUiElement> _clickUiElements = new List<IUiElement>();
        private readonly List<RectangleF> _clickUiRects = new List<RectangleF>();
        private s7o_HUD_MENU _hudMenu;
        private bool _clickUiReadable;
        // World distances do not depend on screen resolution, zoom or monitor offsets.
        private const double PylonAvoidanceRangeYards = 20.0;
        private readonly List<IShrine> _protectedPylons = new List<IShrine>();
        private bool _pylonGuardReadable, _pylonBlocksPlayer, _pylonNeedsMonsterHover;
        private bool _assistPylonBlockedTargets;
        private static readonly ActionKey[] ClickUiActions = {
            ActionKey.LeftSkill, ActionKey.RightSkill, ActionKey.Skill1, ActionKey.Skill2,
            ActionKey.Skill3, ActionKey.Skill4, ActionKey.Heal, ActionKey.TownPortal,
            ActionKey.Inventory, ActionKey.SkillsWindow, ActionKey.ParagonWindow,
            ActionKey.Map, ActionKey.WaypointMap, ActionKey.Social, ActionKey.Close
        };
        private ActionKey _combinationDisplayKey = ActionKey.Unknown;
        private int _combinationDrawCount;
        private int _nextDashAimTick;
        private bool _assistActive, _assistEnding;
        private ActionKey _assistAttackKey = ActionKey.Unknown;
        private ActionKey _assistMainKey = ActionKey.Unknown;
        private uint _assistTargetAcd;
        private uint _assistTargetAnn;
        private int _assistAimTick, _assistAimGameTick;
        private int _assistCursorX, _assistCursorY, _assistAimX, _assistAimY;
        private bool _assistCursorOwned;
        private bool _assistAimPending, _assistCaptureSuppressed, _assistWaitForRelease;
        private bool _assistHitVerified;
        // Scope the modifier to a corpse overlap or a useful circle within attack reach.
        // Never acquire an already held user key; the shared arbiter owns failed releases.
        private ushort _assistScopedStandstill;
        private int _assistScopedStandstillGameTick = int.MinValue;
        // Elite probes are a single bounded native-hover sweep, never a pixel hitbox claim.
        private int _assistRetargetAttempts;
        private int _assistRetargetNextGameTick = int.MinValue;
        private uint _assistProbeTargetAcd, _assistRetargetTriedAcd;
        private int _assistProbePhase, _assistProbeGameTick = int.MinValue;
        private bool _assistProbeLocked, _assistProbeExhausted, _pulseIsRetarget;
        private const int AssistProbeCount = 10;
        // Native game time remains meaningful when OpenSpeedy changes wall-clock cadence.
        private int _assistWotHfLastGameTick = int.MinValue, _assistWotHfObservationGameTick = int.MinValue;
        private bool _assistWotHfResumePending;
        private int _assistWotHfResumeGameTick = int.MinValue, _assistWotHfYields;
        private const int AssistWotHfRefreshTicks = 42; // Request at 0.7 game seconds.
        private const int AssistWotHfForesightLimitTicks = 54; // Finish a verified third hit within the remaining margin.
        private bool _assistUiBlockedTargets, _assistHazardBlockedTargets;
        private int _assistAttackStartedTick;
        private int _assistIdleSinceTick = int.MinValue;
        private int _assistRecoveries;
        private string _assistStatus = "off";
        private bool _selfDashAimIsMonster, _selfDashObserved;
        private uint _selfDashTargetAcd;
        private bool _selfDashApproach, _assistWasApproaching, _assistApproachPending;
        private bool _assistDashRetryNeedsAttack;
        private int _lastApproachDashTick = int.MinValue;
        private uint _lastApproachDashTargetAcd;
        // Reused native snapshot for target selection and the assist landing planner.
        private readonly List<IMonster> _assistCandidates = new List<IMonster>(192);
        private readonly List<IMonster> _assistAcquisitionCandidates = new List<IMonster>(128);
        private readonly List<IMonster> _assistTrashNear = new List<IMonster>(16);
        private readonly List<IMonster> _assistTrashValue = new List<IMonster>(16);
        private readonly List<IMonster> _assistTrashShortlist = new List<IMonster>(48);
        private readonly Dictionary<long, AssistTrashCell> _assistTrashCells = new Dictionary<long, AssistTrashCell>();
        private readonly List<AssistTrashCell> _assistTrashDenseCells = new List<AssistTrashCell>(16);
        private readonly List<double> _assistTrashScores = new List<double>(48);
        private readonly List<double> _assistTrashValues = new List<double>(48);
        private readonly List<double> _assistTrashDensityValues = new List<double>(48);
        private const double AssistTrashPackRadius = 14.0, AssistHighTrashProgression = 0.60;
        private struct AssistTrashProfile { public double Value, Progression; public int Count; }
        private readonly Dictionary<uint, AssistTrashProfile> _assistTrashProfiles = new Dictionary<uint, AssistTrashProfile>(64);
        private double _assistTrashMaxValue;
        private int _assistTrashSelectionClass;
        private bool _assistTravelHeadingKnown;
        private double _assistTravelHeadingX, _assistTravelHeadingY;
        private string _assistSelectionReason = "none";
        private int _assistSelectionTier = -1;
        private double _assistSelectionScore, _assistSelectionValue, _assistSelectionDensity;
        private bool _assistSelectionFan;
        private uint _juggerFocusAcd;
        private IMonster _juggerFocusMonster;
        private bool _juggerCtrlWasDown, _juggerFocusPaintAllowed;
        private IBrush _juggerFocusOutlineBrush, _juggerFocusBrush;
        private uint _assistLastTrashAttackAcd;
        private double _assistTrashAnchorX, _assistTrashAnchorY;
        private double _assistTrashBearingX, _assistTrashBearingY;
        private bool _assistTrashFanReady, _assistTrashPreferLeft = true;
        // Assist-only contact recovery; observations are client/native state, not server proof.
        private bool _assistCorrectiveDashSpent, _assistNavigationPending, _assistNavigationRetrySpent;
        private int _assistRunningSinceTick = int.MinValue, _assistNavigationSinceTick = int.MinValue;
        private int _assistNavigationStartedTick = int.MinValue;
        private int _assistNavigationPositionGameTick = int.MinValue;
        private float _assistNavigationProgressX, _assistNavigationProgressY, _assistNavigationProgressZ, _assistNavigationOriginZ;
        private double _assistNavigationProgressYards;
        private bool _assistNavigationReacquired, _assistNavigationFinalDashSpent;
        private bool _assistNavigationHoverConfirmed;
        private string _assistNavigationReason = "none";
        private uint _assistNavigationTargetAcd;
        // Recovery goal and acknowledged keyboard lock are separate. A bounded local
        // fallback must not replace the retained elite or chase a distant minion.
        private uint _assistNavigationHeldAcd, _assistNavigationHeldAnn, _assistNavigationIdentityAtDown;
        private uint _assistNavigationProbeAcd;
        private int _assistNavigationLockGameTick = int.MinValue;
        private bool _assistNavigationFallbackSpent;
        private int _assistNavigationFailedKeys, _assistNavigationRecoveries;
        private int _assistNavigationMotionGameTick = int.MinValue;
        private float _assistDashOriginX, _assistDashOriginY, _assistDashOriginZ, _assistDashTravelRequested;
        private float _assistDashVerticalRequested, _assistDashVerticalObserved, _assistDashLastPositionZ;
        private float _assistDashTravelObserved, _assistNavigationOriginX, _assistNavigationOriginY;
        private double _assistNavigationGoalDistance;
        private int _assistAimRefreshGameTick = int.MinValue;
        private int _assistCorrectiveDashCount, _assistNavigationFallbacks;
        private bool _assistPanicActive, _assistDashForHazard;
        private const double AssistVisibleTargetRange = 90.0; // Visible acquisition is independent of one Dash hop.
        private const double AssistDashTravelLimit = 50.0; // Check landing travel independently of target range.
        private const double AssistPositionScanRange = 125.0; // Hazard/circle discovery must not shrink with acquisition.
        private const double AssistAffixScanRange = 110.0;
        private const double AssistShockTowerPriorityRange = 30.0;
        private bool _assistDashContinuationPending;
        // A released key is not proof of a landed Dash. Keep one transaction until
        // fresh animation/travel evidence completes it, or its failure watchdog expires.
        private bool _assistDashFreshAnimation, _assistDashPositionObserved, _assistDashLandingConfirmed;
        private bool _assistDashActualSafe, _assistDashSafetyAssessed, _assistDashMovementFailed;
        private string _assistDashSafetyReason = "not-assessed";
        private int _assistDashReleasedGameTick = int.MinValue;
        private float _assistDashResourceBefore, _assistDashResourceRequired;
        private int _assistDashChargesBefore;
        private int _assistDashPositionGameTick = int.MinValue;
        private float _assistDashLastPositionX, _assistDashLastPositionY;
        private bool _assistDashPositionSettled;
        private uint _assistDashArcaneAtPress;
        private string _assistDashOutcome = "none";
        private bool _assistOtherElitePackPresent;
        private s7o_AutoLoot _autoLoot;
        private bool _autoLootYieldPending, _autoLootHazardWaiting;
        private int _autoLootYieldGameTick;
        private int _autoLootHandoffSerial, _autoLootUrgentPreemptions;
        private float _assistDashTargetDistanceBefore;
        private int _assistDashContinuationCount, _assistProjectedBeyondNativeCount;
        private readonly List<IWorldCoordinate> _assistBlockedEscapeLandings = new List<IWorldCoordinate>(16);
        private float _assistEscapeBlockOriginX, _assistEscapeBlockOriginY;
        private uint _assistEscapeBlockWorld;
        private double _assistHealthFraction = 1.0, _assistIncomingDamage;
        // Reactive positioning shares the existing assist-only Dash transaction.
        // Health loss identifies urgency, not the damaging affix or a guaranteed safe path.
        private bool _assistDamageEscapePending;
        private int _assistDamageEscapeGameTick = int.MinValue;
        private uint _assistDamageEscapeWorld;
        private double _assistPreviousHealth = 1.0, _assistPreviousShield;
        private float _assistDamageEscapeOriginX, _assistDamageEscapeOriginY;
        private string _assistDamageEscapeTrigger = "none";
        private const float AssistDamageEscapeHopYards = 8.0f;
        private int _assistDamageWindowTick = int.MinValue;
        private double _assistDamageWindowHealth = 1.0;
        private const int AssistDamageWindowGameTicks = 15; // A quarter second of native game time.
        private readonly List<IActor> _assistPositionOrbiter = new List<IActor>(16);
        private readonly List<IMonster> _assistPositionElectrified = new List<IMonster>(16);
        private IActor _assistOrbiterNearestActor;
        private bool _assistOrbiterCoreUnsafe, _assistElectrifiedUnsafe;
        private const int AssistOrbiterCacheLimit = 32;
        // A conservative exclusion around native focal actors, not a claimed damage hitbox.
        private const float AssistOrbiterMinimumCoreRadius = 4.0f;
        private const float AssistElectrifiedClearance = 6.0f, AssistElectrifiedLandingGap = 7.0f;
        private const float AssistDamageCircleElectrifiedClearance = 4.0f;
        private const double AssistElectrifiedRepositionHealth = 0.50;
        private string _assistCircleCue = "none";
        private uint _assistCircleOpportunityAcd;
        private struct AssistBlockedTarget { public float PlayerX, PlayerY, TargetX, TargetY; public uint World; }

        private uint _assistDashCircleAcd;
        private readonly Dictionary<uint, AssistBlockedTarget> _assistBlockedCircles = new Dictionary<uint, AssistBlockedTarget>();
        private int _assistTrashRecheckGameTick = int.MinValue;

        private readonly Dictionary<uint, AssistBlockedTarget> _assistBlockedTargets = new Dictionary<uint, AssistBlockedTarget>();
        // Failed destinations have their own geometry contract, independent of attack reach.
        private const float AssistFailedLandingResetYards = 10f, AssistFailedLandingRadius = 2f;
        private struct AssistFailedLanding
        { public float X, Y, Z, PlayerX, PlayerY, PlayerZ, TargetX, TargetY, TargetZ; public uint World, Target; }
        private readonly List<AssistFailedLanding> _assistFailedLandings = new List<AssistFailedLanding>(32);

        private bool IsAssistFailedLandingBlocked(IWorldCoordinate point, IMonster target, bool allowNewEscape = false)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            bool blocked = false;
            for (int i = _assistFailedLandings.Count - 1; i >= 0; i--)
            {
                var failure = _assistFailedLandings[i];
                bool changed = failure.World != Hud.Game.Me.WorldId
                    || me.XYDistanceTo(failure.PlayerX, failure.PlayerY) >= AssistFailedLandingResetYards
                    || Math.Abs(me.Z - failure.PlayerZ) >= 4f
                    || (target != null && target.AcdId == failure.Target
                        && (target.FloorCoordinate.XYDistanceTo(failure.TargetX, failure.TargetY) >= 4f
                            || Math.Abs(target.FloorCoordinate.Z - failure.TargetZ) >= 4f));
                if (changed) { _assistFailedLandings.RemoveAt(i); continue; }
                if (Math.Abs(point.Z - failure.Z) <= 2f
                    && point.XYDistanceTo(failure.X, failure.Y) <= AssistFailedLandingRadius) blocked = true;
            }
            // Saturation suspends ordinary approach. Danger can still try a new exit,
            // but neither budget permits repeating a known failed destination.
            return blocked || (!allowNewEscape && _assistFailedLandings.Count >= 32);
        }

        private void RememberAssistFailedLanding(IWorldCoordinate point, IMonster target, IWorldCoordinate actual)
        {
            if (IsAssistFailedLandingBlocked(point, target, true)) return;
            if (_assistFailedLandings.Count >= 32)
            { BlockAssistEscapeLanding(point, actual); return; }
            _assistFailedLandings.Add(new AssistFailedLanding {
                X = point.X, Y = point.Y, Z = point.Z, PlayerX = actual.X, PlayerY = actual.Y, PlayerZ = actual.Z,
                World = Hud.Game.Me.WorldId, Target = target != null ? target.AcdId : 0u,
                TargetX = target != null ? target.FloorCoordinate.X : 0f,
                TargetY = target != null ? target.FloorCoordinate.Y : 0f, TargetZ = target != null ? target.FloorCoordinate.Z : 0f
            });
        }
        // Actor IDs and English names verified against the supplied SDK/native Monsters strings.
        private static readonly HashSet<uint> AssistRangedThreatActors = new HashSet<uint>
        {
            370u, 4196u, 4197u, 4198u, 276492u, 351023u, 309114u, 294969u,
            418911u, 418918u, 418922u, 418923u, 430947u, 487357u, 488560u,
            5371u, 5372u, 5368u, 5376u, 5382u, 5367u, 5375u, 5381u, 487356u, 488587u
        };
        private static readonly HashSet<string> AssistRangedThreatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Blazing Guardian", "Chilling Construct", "Smoldering Construct", "Charged Construct", "Toxic Construct",
            "Frost Guardian", "Shock Guardian", "Noxious Guardian", "Enraged Phantom", "Enraged Phantasm",
            "Vile Revenant", "Deathly Haunt", "Grim Wraith", "Vengeful Phantasm", "Shadow of Death",
            "Shipwrecked Soul", "Vile Echo"
        };
        private static readonly uint[] AttackIdentityModifiers = { 0u, 0xFFFFFu, uint.MaxValue, 2147483647u };
        // Native HUD circles use 10 yards; land one yard inside their drawn edge.
        private const float AssistCircleLandingRadius = 9.0f;
        private const float AssistCircleOuterReachYards = 8.0f;
        // Attack reach, circle coverage and navigation progress are independent contracts.
        private const float AssistAttackReachYards = 8.0f;
        private const float AssistCircleCoverageRadius = 10.0f;
        private const float AssistNavigationRetryYards = 10.0f;
        private const float AssistMoltenAvoidanceRadius = 23.0f; // 18-yard blast plus five-yard clearance.
        private const float AssistGrotesqueAvoidanceRadius = 25.0f; // 20-yard blast plus five-yard clearance.
        private const int AssistGrotesqueDodgeTicks = 48; // 800 ms at 60 native game ticks/second.
        private const int AssistGrotesqueFuseTicks = 90; // Requested 1.5-second warning; independent of Molten.
        private const float AssistMoltenEscapeSeconds = 1.0f;
        private const int AssistCircleCacheLimit = 32, AssistMoltenCacheLimit = 64;
        // Native Arcane core warning is 6 yards. Keep half a yard clear of its edge.
        private const float AssistArcaneCoreAvoidanceRadius = 6.5f;
        private const int AssistArcaneCacheLimit = 64;
        private readonly List<IActor> _assistPositionArcane = new List<IActor>(32);
        private readonly List<IActor> _assistDoors = new List<IActor>(16);
        private struct AssistDoorFailure
        { public int Count; public float PlayerX, PlayerY, DoorX, DoorY; public uint World; }
        private readonly Dictionary<uint, AssistDoorFailure> _assistDoorFailures = new Dictionary<uint, AssistDoorFailure>();
        private IActor _assistDoorTarget;
        private int _assistDoorStage, _assistDoorSinceTick, _assistDoorAimGameTick;
        private int _assistDoorAimX, _assistDoorAimY;
        private float _assistDoorBeforeHp;
        private bool _assistDoorClickAuthorized;
        private string _assistDoorStatus = "none";
        private ActionKey _assistDoorPulseKey = ActionKey.Unknown;
        private IActor _assistArcaneNearestActor;
        private uint _assistArcaneNearestSno, _assistArcaneNearestAcd;
        private int _assistPositionArcaneCount;
        private bool _assistArcaneCoreUnsafe;
        private double _assistArcaneNearestDistance = double.NaN;
        private float _assistArcaneCollisionDeltaX = float.NaN, _assistArcaneCollisionDeltaY = float.NaN;
        private string _assistArcaneEscapeReason = "none";
        private struct AssistPositionCircle { public IActor Actor; public int Kind; }
        // Explosion geometry is shared by selection, landing, follow and escape.
        // Corpse timers use observed death, not actor creation (which predates death).
        private struct AssistPositionMolten
        {
            public IActor Actor; public bool CountingDown, Grotesque;
            public IWorldCoordinate Position; public int StartTick, EndTick;
            public IWorldCoordinate Center { get { return Position ?? Actor.FloorCoordinate; } }
        }
        private sealed class AssistCorpseObservation
        {
            public IActor Actor; public IWorldCoordinate Position;
            public int LastSeenTick, DeathTick = int.MinValue, EndTick;
        }
        private readonly Dictionary<uint, AssistCorpseObservation> _assistCorpseObservations
            = new Dictionary<uint, AssistCorpseObservation>();
        private readonly List<uint> _assistCorpsePrune = new List<uint>(16);
        private uint _assistCorpseWorld;
        private int _assistCorpseTick = int.MinValue;
        private uint _assistSpacingAttemptAcd;
        private float _assistSpacingTargetX, _assistSpacingTargetY;
        private readonly List<AssistPositionCircle> _assistPositionCircles = new List<AssistPositionCircle>(16);
        private readonly List<AssistPositionMolten> _assistPositionMolten = new List<AssistPositionMolten>(32);
        private int _assistPositionGameTick = int.MinValue;
        private uint _assistPositionWorldId;
        private bool _assistPositionReadable, _assistPositionReplanPending;
        private IWorldCoordinate _assistDashLanding;
        private bool _assistDashEscapeOnly;
        private uint _assistDashLandingWorldId;
        private uint _assistHazardReturnAcd;
        private string _assistPositionReason = "none", _assistPositionEscapeReason = "none";
        private int _assistPositionCircleCount, _assistPositionHazardCount;
        private float _assistPositionWorldX = float.NaN, _assistPositionWorldY = float.NaN;
        private float _assistPositionActualX = float.NaN, _assistPositionActualY = float.NaN;
        private static readonly float[] AssistEdgeX = { 1f, 0.70710678f, 0f, -0.70710678f, -1f, -0.70710678f, 0f, 0.70710678f };
        private static readonly float[] AssistEdgeY = { 0f, 0.70710678f, 1f, 0.70710678f, 0f, -0.70710678f, -1f, -0.70710678f };


        public s7o_GenMonk() { Enabled = true; Order = 21011; }

        public override void Load(IController hud)
        {
            base.Load(hud);
            s7o_GenMonkInputContext.Hud = hud;
            s7o_GenMonkInputContext.CanPressLeftSkill = IsLeftClickSafe;
            RegisterClickUi();
            _chatEditLine = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);
            _combinationFont = Hud.Render.CreateFont("tahoma", 9, 255, 255, 255, 255,
                false, false, 255, 0, 0, 0, true);
            _juggerFocusOutlineBrush = Hud.Render.CreateBrush(235, 0, 0, 0, 6.0f);
            _juggerFocusBrush = Hud.Render.CreateBrush(245, 255, 132, 0, 3.2f);
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "plugins", "s7o", "settings", "s7o_GenMonk.ini");
            try
            {
                string loadPath = _settingsPath;
                if (!File.Exists(loadPath))
                {
                    string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "plugins", "s7o", "settings", "s7o_SanctifiedMonk.ini");
                    if (File.Exists(legacyPath)) loadPath = legacyPath;
                }
                if (File.Exists(loadPath))
                    foreach (string line in File.ReadAllLines(loadPath))
                    {
                        string[] pair = line.Split(new[] { '=' }, 2);
                        ushort assistKey;
                        if (pair.Length == 2 && pair[0] == "MeleeAssistVirtualKey"
                            && ushort.TryParse(pair[1], out assistKey) && assistKey != 0)
                        { MeleeAssistVirtualKey = assistKey; continue; }
                        bool value;
                        if (pair.Length != 2 || !bool.TryParse(pair[1], out value)) continue;
                        if (pair[0] == "AutoDash") AutoDash = value;
                        if (pair[0] == "SelfDash") SelfDash = value;
                        if (pair[0] == "MeleeAssistEnabled") MeleeAssistEnabled = value;
                    }
            }
            catch { }
        }

        public void SetAutoDash(bool value) { AutoDash = value; SaveOptions(); }
        public void SetSelfDash(bool value) { SelfDash = value; SaveOptions(); }
        public void SetMeleeAssistEnabled(bool value)
        {
            MeleeAssistEnabled = value;
            if (!value) ReleaseAssistAttack();
            SaveOptions();
        }
        public void SetMeleeAssistVirtualKey(ushort value)
        {
            if (value == 0) return;
            MeleeAssistVirtualKey = value;
            _assistWaitForRelease = true;
            ReleaseAssistAttack();
            SaveOptions();
        }
        public void SetMeleeAssistCapture(bool value)
        {
            _assistCaptureSuppressed = value;
            _assistWaitForRelease = true;
            if (value) ReleaseAssistAttack();
        }

        private void SaveOptions()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, "AutoDash=" + AutoDash + Environment.NewLine
                    + "SelfDash=" + SelfDash + Environment.NewLine
                    + "MeleeAssistEnabled=" + MeleeAssistEnabled + Environment.NewLine
                    + "MeleeAssistVirtualKey=" + MeleeAssistVirtualKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + Environment.NewLine);
            }
            catch { }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ClearJuggerFocus();
            ResetMapAssist();
            _assistCandidates.Clear();
            _assistTrashFanReady = false;
            _assistTrashPreferLeft = true;
            _assistLastTrashAttackAcd = 0u;
            Stop();
            if (newGame) _combinationDisplayKey = ActionKey.Unknown;
        }
        public void ForceStopForDisable() { Stop(); }

        public bool CanYieldForAutoLootPickup
        {
            get
            {
                if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null) return true;
                // Ordinary attacks yield; an already aimed/submitted Dash keeps ownership
                // only through its bounded transaction. Imminent hazards outrank loot.
                if (_selfDashKey != ActionKey.Unknown) return false;
                if (!_assistActive || !AssistRequested()) return true;
                return !AssistReactiveEscapeDue() && !AssistMoltenEscapeDue();
            }
        }

        public void PauseForAutoLootPickup()
        {
            // Called synchronously before AutoLoot captures or moves its cursor.
            // Release only our inputs and complete our restore before handing it over.
            if (!Enabled || Hud == null || Hud.Game == null) return;
            if (_autoLoot == null) _autoLoot = Hud.GetPlugin<s7o_AutoLoot>();
            CancelAssistDoor();
            ReleaseAssistAttack();
            CancelTarget();
            CursorPoint cursor;
            if (_assistCursorOwned && Hud.Window.IsForeground && GetCursorPos(out cursor)
                && Math.Abs((long)cursor.X - _assistAimX) <= 128
                && Math.Abs((long)cursor.Y - _assistAimY) <= 128)
                SetCursorPos(_assistCursorX, _assistCursorY);
            _assistCursorOwned = false;
            _autoLootYieldPending = true;
            _autoLootYieldGameTick = Hud.Game.CurrentGameTick;
            _autoLootHandoffSerial++;
            _assistStatus = "loot-handoff";
        }

        public void StopForAutoLootUrshiHandoff()
        {
            Stop();
            // Urshi owns the UI. A still-held assist key cannot reopen combat there.
            _assistWaitForRelease = true;
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            if (Hud.Window.IsForeground) s7o_InputReleaseArbiter.RetryPending(now);
            if (!Enabled || !CanRun()) { PauseJuggerFocus(); Stop(); return; }
            RefreshClickUiRects();
            RefreshPylonGuard();
            if (_autoLootYieldPending)
            {
                bool pendingInput = _autoLoot != null && _autoLoot.Enabled && _autoLoot.HasPendingPickupInput;
                _autoLootHazardWaiting = _assistActive && AssistRequested()
                    && (AssistReactiveEscapeDue() || AssistMoltenEscapeDue());
                if (_autoLootHazardWaiting && _autoLoot != null)
                {
                    // A normal restore is brief, but a resync can exceed the Molten
                    // deadline. Finish AutoLoot's owned restore/release synchronously;
                    // the generic guard prevents it reclaiming input during this exit.
                    _autoLoot.CancelPendingPickupInput();
                    _autoLootUrgentPreemptions++;
                }
                else if (pendingInput || Hud.Game.CurrentGameTick == _autoLootYieldGameTick)
                { _assistStatus = "loot-handoff"; return; }
                _autoLootYieldPending = _autoLootHazardWaiting = false;
                _nextDashAimTick = now;
                _assistAimPending = true;
            }
            try { RefreshAssistTargeting(); }
            catch
            {
                _assistCandidates.Clear();
                ClearJuggerFocus();
                Stop();
                return;
            }
            ObserveAssistWotHf();
            FinishPulse(now);
            // Observe completed effects before target changes or a secondary animation
            // can discard the pending verification. This never creates a new input.
            if (_pulse == ActionKey.Unknown && TargetEffectsConfirmed()) CancelTarget();
            if (_assistActive && !AssistRequested())
            {
                ReleaseAssistAttack();
                _assistEnding = true;
                // A submitted Dash must finish its aim transaction before the final restore.
                if (_selfDashKey == ActionKey.Unknown || _selfDashCastTick == int.MinValue)
                {
                    CancelTarget();
                    EndAssist();
                    return;
                }
            }
            if (_selfDashKey != ActionKey.Unknown)
            {
                bool assistDash = _selfDashApproach && _assistActive;
                AdvanceSelfDash(now);
                if (_assistEnding && _selfDashKey == ActionKey.Unknown)
                { EndAssist(); return; }
                if (_selfDashKey != ActionKey.Unknown || !assistDash) return;
                // The assist transaction finished: acquire/attack in this same update.
                // Manual Dash retains its separate confirmation/restore behavior.
            }

            _hasCombinationStrike = HasCombinationStrikePassive();
            if (!_hasCombinationStrike && _targetKey != ActionKey.Unknown && !_targetForesight)
                CancelTarget();
            _generators.Clear();
            IPlayerSkill dash = null;
            try
            {
                foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                {
                    if (skill == null || skill.SnoPower == null) continue;
                    if (GeneratorIcon(skill) != 0) _generators.Add(skill);
                    if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_DashingStrike.Sno) dash = skill;
                }
            }
            catch { Stop(); return; }

            // Escape is independent of target acquisition and outranks corpse/UI hover,
            // door handling, rune pulses and a no-target idle state. Key release still stops it.
            if (TryStartAssistHazardEscape(dash, now)) return;
            if (_assistScopedStandstill != 0 && !IsAssistCorpseOverlap()
                && !ShouldHoldAssistPosition(FindAssistCandidate(_assistTargetAcd)))
                ReleaseAssistScopedStandstill();
            // A corpse is a combat overlap, not an instruction to idle at the same aim.
            // Other unsafe clicks retain the existing stop/release behavior.
            if (s7o_GenMonkInput.Owns(ActionKey.LeftSkill) && !IsLeftClickSafe())
            {
                bool corpseOverlap = IsAssistCorpseOverlap();
                _restoreCursor = false;
                ReleaseAssistAttack();
                if (_pulse == ActionKey.LeftSkill) CancelTarget();
                bool canProbeOverlap = !_pylonBlocksPlayer && !_assistProbeExhausted
                    && IsAssistPositionElite(FindAssistCandidate(_assistProbeTargetAcd));
                if (!corpseOverlap && !canProbeOverlap)
                {
                    _assistStatus = _pylonBlocksPlayer ? "pylon-blocked" : "click-blocked";
                    return;
                }
            }

            // A longer rune sequence must still yield to a required Dash refresh or
            // release its own input immediately when nearby combat disappears.
            if (_pulseIsMaintenance && _targetForesight
                && (!HasAttackableTargetNearby()
                    || (AutoDash && CanApproachDash(dash) && DashDue(dash, now))))
                CancelTarget();

            try { if (UpdateMeleeAssist(dash, now)) return; }
            catch { Stop(); return; }

            IPlayerSkill held = FindHeldGenerator(_generators);
            if (held == null) { Stop(); return; }
            if (RequireHundredFistsMain
                && held.SnoPower.Sno != Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
            {
                CancelTarget();
                _mainKey = ActionKey.Unknown;
                _candidateKey = ActionKey.Unknown;
                return;
            }
            if (_candidateKey != held.Key)
            {
                _candidateKey = held.Key;
                _candidateSinceTick = now;
                CancelTarget();
                return;
            }
            if ((uint)(now - _candidateSinceTick) < 75u) return;
            if (_mainKey != held.Key && _targetKey != ActionKey.Unknown) CancelTarget();
            _mainKey = held.Key;
            _combinationDisplayKey = held.Key;

            // LMB can mean either move or attack. Never infer combat from the physical button
            // alone. Keep the WotHF guard unchanged; other generators must also have just
            // refreshed their own Combination Strike timer while actually attacking.
            bool hundredFistsMain = held.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno;
            bool hundredFistsAttack = hundredFistsMain && HundredFistsAttackWindow();
            bool generatorAttack = hundredFistsMain ? hundredFistsAttack : OtherGeneratorAttackWindow(held);
            // Generator pulses never steer the cursor or attempt to reacquire a monster.
            if (_pulse != ActionKey.Unknown) return;

            if (!generatorAttack)
            {
                // Assist resumes its own primary after a secondary cast. That cast's
                // lingering animation is not a failed attempt: keep verification/retries
                // until WotHF resumes. UpdateMeleeAssist still cancels invalid targets.
                if (!_assistActive) CancelTarget();
                _dashAwaiting = false;
                return;
            }
            if (!HasAttackableTargetNearby())
            {
                CancelTarget();
                _dashAwaiting = false;
                return;
            }

            // An owned primary must actually resume after Dash/secondary maintenance.
            // A held key or the previous generator's timer is not that confirmation.
            if (_assistActive && AssistWotHfHeartbeatEnabled() && _assistWotHfResumePending) return;

            // Dash is the damage prerequisite. Service it before any generator setup/retry.
            if (_dashAwaiting && (uint)(now - _lastDashTick) >= 220u)
            {
                double raiment = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
                if (raiment > 1.0 || (uint)(now - _lastDashTick) >= 700u)
                    _dashAwaiting = false;
            }
            if (!_assistActive && AutoDash && CanApproachDash(dash) && !_dashAwaiting && DashDue(dash, now))
            {
                CancelTarget();
                MaintainDash(dash, now);
                if (_selfDashKey != ActionKey.Unknown || _pulse != ActionKey.Unknown) return;
            }

            if (_targetKey != ActionKey.Unknown)
            {
                if (_targetKey == held.Key) { CancelTarget(); return; }
                if (TargetEffectsConfirmed()) { CancelTarget(); return; }
                if (!Due(now, _nextAttemptTick)) return;
                int limit = _targetForesight ? 3 : 4;
                if (_attempts >= limit)
                {
                    _retryAfter[_targetSno] = unchecked(now + 1200);
                    _assistMaintenanceAttempts.Remove(_targetSno);
                    CancelTarget();
                    return;
                }
                // Space suspends only its owned primary; a user's held input is untouched.
                // The selected generator must prove its own buff, not an inferred combo stage.
                if (StartMaintenancePulse(_targetKey, now)) _attempts++;
                else _nextAttemptTick = unchecked(now + 90);
                return;
            }

            SelectMaintenanceTarget(held, now);
        }

        private bool SelectMaintenanceTarget(IPlayerSkill held, int now)
        {
            int count = _generators.Count;
            int start = 0;
            if (_assistActive)
                for (int i = 0; i < count; i++)
                    if (_generators[i].SnoPower.Sno == _lastAssistMaintenanceSno)
                    { start = (i + 1) % count; break; }

            // Confirm the other due contributions first; Deadly Reach/Foresight goes last.
            // Within that priority, retain REV2's fair assist rotation across target changes.
            for (int pass = 0; pass < 2; pass++)
                for (int offset = 0; offset < count; offset++)
                {
                    var generator = _generators[(start + offset) % count];
                    if (generator.Key == held.Key) continue;
                    bool foresight = IsForesight(generator);
                    if (foresight != (pass == 1)) continue;
                    uint sno = generator.SnoPower.Sno;
                    double foresightLeft = foresight ? ForesightLeft() : 0.0;
                    bool comboDue = _hasCombinationStrike
                        && BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno,
                            GeneratorIcon(generator)) <= CombinationRefreshAtSeconds;
                    bool runeDue = foresight && foresightLeft <= Math.Max(0.25, ForesightRefreshAtSeconds);
                    if (!comboDue && !runeDue)
                    {
                        _assistMaintenanceAttempts.Remove(sno);
                        continue;
                    }
                    int retry;
                    if (_retryAfter.TryGetValue(sno, out retry) && !Due(now, retry)) continue;
                    SelectTarget(generator, now, foresight, foresightLeft);
                    return true;
                }
            return false;
        }

        private void SelectTarget(IPlayerSkill generator, int now, bool foresight, double foresightBefore)
        {
            if (generator == null || generator.Key == ActionKey.Unknown) return;
            _targetKey = generator.Key;
            _targetSno = generator.SnoPower.Sno;
            _targetIcon = GeneratorIcon(generator);
            _targetBefore = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, _targetIcon);
            _targetForesight = foresight;
            _targetForesightBefore = foresightBefore;
            _attempts = 0;
            if (_assistActive) _assistMaintenanceAttempts.TryGetValue(_targetSno, out _attempts);
            _nextAttemptTick = now;
        }

        private bool HasCombinationStrikePassive()
        {
            try
            {
                var powers = Hud.Game.Me.Powers;
                if (powers == null || powers.UsedPassives == null) return false;
                uint sno = Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno;
                foreach (var passive in powers.UsedPassives)
                    if (passive != null && passive.Sno == sno) return true;
            }
            catch { }
            return false;
        }

        private bool IsForesight(IPlayerSkill skill)
        {
            // LightningMOD v7.6's MonkDeadlyReachPlugin verifies rune 0 as Foresight.
            return skill != null && skill.SnoPower != null
                && skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno
                && skill.Rune == 0;
        }

        private double ForesightLeft()
        {
            try
            {
                // LightningMOD's native handler reads Deadly Reach buff icon 1 for Foresight.
                var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno);
                return buff != null && buff.TimeLeftSeconds != null && buff.TimeLeftSeconds.Length > 1
                    ? Math.Max(0, buff.TimeLeftSeconds[1]) : 0;
            }
            catch { return 0; }
        }

        private bool HundredFistsAttackWindow()
        {
            try
            {
                if (_mainKey == ActionKey.Unknown || !s7o_GenMonkInput.IsDown(_mainKey)) return false;
                string animation = Hud.Game.Me.Animation.ToString();
                // LightningMOD v7.6 uses this same native animation family as its guard.
                // Do not fall back to generic Attacking or LMB-down: both also occur while moving.
                return !string.IsNullOrEmpty(animation)
                    && animation.IndexOf("rapidstrikes", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool OtherGeneratorAttackWindow(IPlayerSkill held)
        {
            // A held mouse can also be movement, and a different skill can briefly occupy
            // the attack animation. Require this held generator's own fresh 10-second buff
            // as evidence of recent attacks; never treat idle/running/dashing as combat.
            if (held == null || !s7o_GenMonkInput.IsDown(held.Key)
                || Hud.Game.Me.AnimationState != AcdAnimationState.Attacking) return false;
            string animation = Hud.Game.Me.Animation.ToString();
            if (animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno,
                GeneratorIcon(held)) >= 9.0;
        }

        private bool HasAttackableTargetNearby()
        {
            try
            {
                double range = Math.Max(8.0, AttackTargetRange);
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster == null || (!monster.Attackable
                        && !(_assistActive && IsAssistEligible(monster)))) continue;
                    if (monster.NormalizedXyDistanceToMe <= range) return true;
                }
            }
            catch { }
            return false;
        }

        private IPlayerSkill FindHeldGenerator(List<IPlayerSkill> generators)
        {
            // Keep the sustained user input, including Shift+LMB, ahead of our pulses.
            foreach (var skill in generators)
                if (skill.Key == _mainKey && skill.Key != _pulse && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            foreach (var skill in generators)
                if (skill.Key == ActionKey.LeftSkill && skill.Key != _pulse
                    && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            foreach (var skill in generators)
                if (skill.Key != _pulse && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            return null;
        }

        private bool CanRun()
        {
            try
            {
                return Hud.Game.IsInGame && !Hud.Game.IsLoading && !Hud.Game.IsPaused
                    && !Hud.Game.IsInTown && Hud.Window.IsForeground
                    && !InputUiBlocked()
                    && Hud.Game.Me != null && !Hud.Game.Me.IsDead
                    && (Hud.Inventory == null || Hud.Inventory.InventoryMainUiElement == null
                        || !Hud.Inventory.InventoryMainUiElement.Visible)
                    && Hud.Game.Me.HeroClassDefinition != null
                    && Hud.Game.Me.HeroClassDefinition.HeroClass == HeroClass.Monk
                    && (!RequireRaimentSet || Hud.Game.Me.GetSetItemCount(755275u) >= 6);
            }
            catch { return false; }
        }

        private bool InputUiBlocked()
        {
            return IsInputUiVisible(_chatEditLine) || IsInputUiVisible(Hud.Render.WorldMapUiElement)
                || IsInputUiVisible(Hud.Render.ActMapUiElement);
        }

        private static bool IsInputUiVisible(IUiElement element)
        {
            if (element == null) return false;
            try { element.Refresh(); return element.Visible; }
            catch { return true; }
        }

        private void RegisterClickUi()
        {
            // Reuse Pestilence's native controls, registering once rather than scanning per target.
            foreach (var key in ClickUiActions)
                try { AddClickUi(Hud.Render.GetPlayerSkillUiElement(key)); } catch { }
            AddClickUi(Hud.Render.ParagonLevelUpSplashTextUiElement);
            if (Hud.Inventory != null) AddClickUi(Hud.Inventory.FollowerMainUiElement);
            foreach (string path in new[] {
                "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect",
                "Root.NormalLayer.game_notify_dialog_backgroundScreen.dialog_new_paragon_button",
                "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList",
                "Root.TopLayer.follower_swap",
                "Root.NormalLayer.BattleNetProfile_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetLeaderboard_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetAchievements_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetStore_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.Guild_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd",
                "Root.NormalLayer.conversation_dialog_main",
                "Root.TopLayer.BattleNetSocialDialogs_main.LayoutRoot.DialogWriteNote"
            })
                try { AddClickUi(Hud.Render.RegisterUiElement(path, null, null)); } catch { }
        }

        private void AddClickUi(IUiElement element)
        {
            if (element != null && !_clickUiElements.Contains(element)) _clickUiElements.Add(element);
        }

        private void RefreshClickUiRects()
        {
            _clickUiReadable = false;
            _clickUiRects.Clear();
            try
            {
                if (_hudMenu == null)
                    foreach (var plugin in Hud.AllPlugins)
                        if (plugin is s7o_HUD_MENU) { _hudMenu = (s7o_HUD_MENU)plugin; break; }
                if (!Hud.Render.UiHidden)
                {
                    // Native UI objects are refreshed by collection; copy their current bounds once.
                    foreach (var element in _clickUiElements)
                        if (element.Visible && element.Rectangle.Width > 0 && element.Rectangle.Height > 0)
                            _clickUiRects.Add(element.Rectangle);
                    foreach (var player in Hud.Game.Players)
                        if (player != null && player.IsInGame && player.PortraitUiElement != null
                            && player.PortraitUiElement.Visible)
                            _clickUiRects.Add(player.PortraitUiElement.Rectangle);
                    // Small fallbacks for buttons whose native ActionKey element may be absent.
                    // Anchor to center/edges with height-based scale; do not stretch across ultrawide.
                    float w = Hud.Window.Size.Width, h = Hud.Window.Size.Height, s = h / 1080f;
                    _clickUiRects.Add(new RectangleF(w / 2f - 350f * s, h - 205f * s, 715f * s, 205f * s));
                    _clickUiRects.Add(new RectangleF(0, h - 107f * s, 93f * s, 107f * s));
                    _clickUiRects.Add(new RectangleF(w - 166f * s, h - 130f * s, 166f * s, 130f * s));
                    _clickUiRects.Add(new RectangleF(w - 80f * s, 338f * s, 72f * s, 72f * s));
                    _clickUiRects.Add(new RectangleF(w - 292f * s, 10f * s, 84f * s, 54f * s));
                }
                _clickUiReadable = true;
            }
            catch { } // A failed read must not permit a synthetic left click.
        }

        private void RefreshPylonGuard()
        {
            _pylonGuardReadable = false;
            _pylonBlocksPlayer = _pylonNeedsMonsterHover = false;
            _protectedPylons.Clear();
            try
            {
                if (Hud.Game.Shrines == null || Hud.Game.Me.FloorCoordinate == null) return;
                foreach (var pylon in Hud.Game.Shrines)
                {
                    if (pylon == null || !pylon.IsPylon || pylon.IsDisabled || pylon.IsOperated
                        || pylon.FloorCoordinate == null) continue;
                    _protectedPylons.Add(pylon);
                    if (pylon.IsOnScreen || ((_assistActive || AssistRequested())
                        && IsAssistVisiblePoint(pylon.FloorCoordinate))) _pylonNeedsMonsterHover = true;
                    if (Hud.Game.Me.FloorCoordinate.XYDistanceTo(pylon.FloorCoordinate)
                        <= PylonAvoidanceRangeYards) _pylonBlocksPlayer = true;
                }
                _pylonGuardReadable = true;
            }
            catch { } // Missing world safety data must not permit our synthetic LMB.
        }

        private bool IsNearProtectedPylon(IWorldCoordinate point)
        {
            if (!_pylonGuardReadable || point == null) return true;
            foreach (var pylon in _protectedPylons)
                if (point.XYDistanceTo(pylon.FloorCoordinate) <= PylonAvoidanceRangeYards) return true;
            return false;
        }

        private bool IsWorldHoverSafeForLeftClick()
        {
            var hovered = Hud.Game.SelectedActor;
            if (IsAssistDoorHoverAuthorized(hovered)) return true;
            var monster = hovered as IMonster;
            bool attackableMonster = monster != null && monster.IsAlive
                && (monster.Attackable || (_assistActive && IsAssistEligible(monster)))
                && !monster.Invulnerable && !monster.Untargetable;
            // Force-attack through a native corpse only while aiming at a live, reachable
            // assist target. UI bounds and pylon zones are still checked independently.
            bool corpseAttack = IsAssistCorpseOverlap() && s7o_GenMonkInput.IsStandstillDown();
            // UI bounds cannot protect a pylon, portal or clickable NPC under the cursor.
            if (hovered != null && !attackableMonster && !corpseAttack
                && (hovered.IsClickable || hovered.GizmoType == GizmoType.Portal
                    || hovered.GizmoType == GizmoType.BossPortal
                    || (hovered.SnoActor != null && (hovered.SnoActor.Kind == ActorKind.Shrine
                        || hovered.SnoActor.Kind == ActorKind.Portal)))) return false;
            // Around visible unused pylons, frame advancement alone is not click permission.
            return !_pylonNeedsMonsterHover || (attackableMonster && monster.IsSelected) || corpseAttack;
        }

        private bool IsLeftClickPointSafe(double x, double y)
        {
            if (!_clickUiReadable || !_pylonGuardReadable || _pylonBlocksPlayer || x < 0 || y < 0
                || x >= Hud.Window.Size.Width || y >= Hud.Window.Size.Height
                || double.IsNaN(x) || double.IsNaN(y)) return false;
            if (_hudMenu != null && _hudMenu.IsAutomationLeftClickBlocked((float)x, (float)y)) return false;
            foreach (var rect in _clickUiRects)
                if (x >= rect.Left - 2 && x <= rect.Right + 2
                    && y >= rect.Top - 2 && y <= rect.Bottom + 2) return false;
            return true;
        }

        private bool IsLeftClickSafe()
        {
            try
            {
                CursorPoint cursor;
                if (!Hud.Window.IsForeground || !GetCursorPos(out cursor)) return false;
                return IsLeftClickPointSafe((long)cursor.X - Hud.Window.Offset.X,
                    (long)cursor.Y - Hud.Window.Offset.Y) && IsWorldHoverSafeForLeftClick();
            }
            catch { return false; }
        }

        private bool TryGetAssistAim(IMonster target, out int x, out int y)
        {
            x = y = 0;
            // Do not dash/follow a target into a pylon zone and then rearm owned LMB there.
            // Keyboard-bound generators retain their existing targeting behavior.
            if (_assistMainKey == ActionKey.LeftSkill
                && (_pylonBlocksPlayer || IsNearProtectedPylon(target.FloorCoordinate)))
            { _assistPylonBlockedTargets = true; return false; }
            var screen = target.FloorCoordinate.ToScreenCoordinate(true, true);
            if (screen == null || double.IsNaN(screen.X) || double.IsNaN(screen.Y)
                || double.IsInfinity(screen.X) || double.IsInfinity(screen.Y)) return false;
            double aimX = screen.X, aimY = screen.Y;
            if (_assistActive && target.AcdId == _assistProbeTargetAcd
                && (IsAssistPositionElite(target) || _assistNavigationPending) && _assistProbePhase > 0)
            {
                // Same body/side concept as Pestilence RGK, projected for this camera.
                var native = target.ScreenCoordinate;
                if (native != null && AssistPositionFinite(native.X) && AssistPositionFinite(native.Y))
                {
                    double bodyX = screen.X - native.X, bodyY = screen.Y - native.Y;
                    double radiusX = 8.0, radiusY = 6.0;
                    float radius = Math.Max(0.5f, target.RadiusBottom);
                    for (int i = 0; i < 4; i++)
                    {
                        var edge = target.FloorCoordinate.Offset(
                            i < 2 ? (i == 0 ? radius : -radius) : 0f,
                            i >= 2 ? (i == 2 ? radius : -radius) : 0f, 0f).ToScreenCoordinate(true, true);
                        if (edge == null || !AssistPositionFinite(edge.X) || !AssistPositionFinite(edge.Y)) continue;
                        radiusX = Math.Max(radiusX, Math.Abs(edge.X - screen.X));
                        radiusY = Math.Max(radiusY, Math.Abs(edge.Y - screen.Y));
                    }
                    if (bodyX * bodyX + bodyY * bodyY < 196.0)
                    { bodyX = 0.0; bodyY = Math.Max(28.0, radiusY * 2.4); }
                    double body = 0.0, side = 0.0;
                    switch (_assistProbePhase)
                    {
                        case 2: body = -0.32; break;
                        case 3: body = 0.40; break;
                        case 4: body = -0.56; break;
                        case 5: body = 0.72; break;
                        case 6: body = -0.08; side = 0.85; break;
                        case 7: body = -0.08; side = -0.85; break;
                        case 8: body = 0.40; side = 0.85; break;
                        case 9: body = 0.40; side = -0.85; break;
                    }
                    aimX = native.X + bodyX * body + radiusX * side;
                    aimY = native.Y + bodyY * body;
                }
            }
            double cx = Math.Round(aimX), cy = Math.Round(aimY);
            // IsOnScreen can still project outside the client; SetCursorPos would clamp that point.
            bool safe = cx >= 0 && cy >= 0 && cx < Hud.Window.Size.Width && cy < Hud.Window.Size.Height
                && (_assistMainKey != ActionKey.LeftSkill || IsLeftClickPointSafe(cx, cy));
            if (!safe && _assistProbePhase > 0 && target.AcdId == _assistProbeTargetAcd)
            {
                // Target acquisition also calls this method. One unsafe probe must not
                // discard a target whose original floor aim remains safe.
                cx = Math.Round(screen.X); cy = Math.Round(screen.Y);
                safe = cx >= 0 && cy >= 0 && cx < Hud.Window.Size.Width && cy < Hud.Window.Size.Height
                    && (_assistMainKey != ActionKey.LeftSkill || IsLeftClickPointSafe(cx, cy));
            }
            if (!safe) return false;
            long sx = (long)cx + Hud.Window.Offset.X, sy = (long)cy + Hud.Window.Offset.Y;
            if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
            x = (int)sx; y = (int)sy;
            return true;
        }

        private int GeneratorIcon(IPlayerSkill skill)
        {
            uint sno = skill.SnoPower.Sno;
            if (sno == Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno) return 2;
            if (sno == Hud.Sno.SnoPowers.Monk_CripplingWave.Sno) return 3;
            if (sno == Hud.Sno.SnoPowers.Monk_FistsOfThunder.Sno) return 4;
            if (sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno) return 5;
            return 0;
        }

        private double BuffLeft(uint sno, int icon)
        {
            try
            {
                var buff = Hud.Game.Me.Powers.GetBuff(sno);
                return buff != null && buff.TimeLeftSeconds != null
                    && icon >= 0 && icon < buff.TimeLeftSeconds.Length
                    ? Math.Max(0, buff.TimeLeftSeconds[icon]) : 0;
            }
            catch { return 0; }
        }

        private bool StartMaintenancePulse(ActionKey key, int now)
        {
            // Only the hotkey assist owns its primary input. Suspend it while the secondary
            // cast executes, then UpdateMeleeAssist sends a fresh primary DOWN at its target.
            // A user's manual held generator is never released here.
            if (key == ActionKey.LeftSkill && !IsLeftClickSafe())
            {
                // A blocked secondary LMB must not repeatedly interrupt a safe keyboard primary.
                _retryAfter[_targetSno] = unchecked(now + 1200);
                CancelTarget();
                return false;
            }
            if (_assistActive) ReleaseAssistAttack(ShouldHoldAssistPosition(FindAssistCandidate(_assistTargetAcd)));
            if (!StartPulse(key, now, MaintenancePulseBudgetMs(), true)) return false;
            if (_assistActive)
            {
                _lastAssistMaintenanceSno = _targetSno;
                _assistMaintenanceAttempts[_targetSno] = _attempts + 1;
            }
            return true;
        }

        private int MaintenancePulseBudgetMs()
        {
            int singleHitBudget = Math.Max(80, GeneratorPulseMs);
            if (!_targetForesight
                || _targetForesightBefore > Math.Max(0.25, ForesightRefreshAtSeconds)) return singleHitBudget;
            double attackSpeed = 1.0;
            try
            {
                double value = Hud.Game.Me.Offense.AttackSpeed;
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
                    attackSpeed = value;
            }
            catch { }
            // This is a maximum key-hold budget, not a mandatory wait. The native rune
            // timer releases it immediately on success; target loss, stop and Dash also abort it.
            // Allow three attacks plus the existing transition allowance, with a hard watchdog.
            return (int)Math.Min(2500.0, Math.Max(singleHitBudget,
                Math.Ceiling(3000.0 / Math.Max(0.5, attackSpeed)) + singleHitBudget));
        }

        private bool StartPulse(ActionKey key, int now, int holdMs, bool maintenance)
        {
            if (!s7o_GenMonkInput.Down(Owner, key)) return false;
            _pulse = key;
            _pulseStartedTick = now;
            _pulseReleaseTick = unchecked(now + Math.Max(1, holdMs));
            _pulseIsMaintenance = maintenance;
            return true;
        }

        private bool MaintenanceEffectConfirmed(int now)
        {
            if (!_pulseIsMaintenance || _targetKey == ActionKey.Unknown) return false;
            if (ElapsedMs(now, _pulseStartedTick) < Math.Max(1, GeneratorPulseMinMs)) return false;
            return TargetEffectsConfirmed();
        }

        private bool TargetEffectsConfirmed()
        {
            if (_targetKey == ActionKey.Unknown) return false;
            double comboLeft = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, _targetIcon);
            if (_targetForesight)
            {
                double foresightLeft = ForesightLeft();
                bool foresightWasDue = _targetForesightBefore <= Math.Max(0.25, ForesightRefreshAtSeconds);
                bool comboWasDue = _hasCombinationStrike && _targetBefore <= CombinationRefreshAtSeconds;
                bool foresightConfirmed = !foresightWasDue
                    || (foresightLeft >= 4.0 && foresightLeft > _targetForesightBefore + 1.0);
                bool comboConfirmed = !comboWasDue
                    || (comboLeft >= 4.0 && comboLeft > _targetBefore + 1.0);
                return foresightConfirmed && comboConfirmed;
            }

            return comboLeft >= 5.0 && comboLeft > _targetBefore + 1.0;
        }

        private void FinishPulse(int now)
        {
            if (_pulse == ActionKey.Unknown) return;

            bool effectConfirmed = _pulseIsMaintenance && MaintenanceEffectConfirmed(now);
            bool heartbeatYield = _pulseIsMaintenance && !effectConfirmed && _assistActive
                && AssistWotHfHeartbeatEnabled() && _assistWotHfLastGameTick != int.MinValue
                && Hud.Game.CurrentGameTick - _assistWotHfLastGameTick >= (_targetForesight
                    ? AssistWotHfForesightLimitTicks : AssistWotHfRefreshTicks);
            bool retargetConfirmed = _pulseIsRetarget && AttackedIdentityMatches(_assistTargetAcd, _assistTargetAnn);
            bool release = Due(now, _pulseReleaseTick) || effectConfirmed || heartbeatYield || retargetConfirmed;
            if (!release) return;

            s7o_GenMonkInput.Up(Owner, _pulse);
            bool maintenanceEnded = _pulseIsMaintenance, retargetEnded = _pulseIsRetarget;
            _pulse = ActionKey.Unknown;
            _pulseIsMaintenance = _pulseIsRetarget = false;
            if (_assistActive && (maintenanceEnded || retargetEnded))
            {
                _assistWotHfResumePending = true;
                if (heartbeatYield) _assistWotHfYields++;
                if (retargetEnded) ReleaseAssistAttack(ShouldHoldAssistPosition(FindAssistCandidate(_assistTargetAcd)));
            }
            _nextAttemptTick = unchecked(now + Math.Max(50, _targetForesight
                ? ForesightRetryGapMs : GeneratorRetryGapMs));
            // A self-dash must not restore its aim just because the keypress ended:
            // Diablo can still be consuming the queued cast. AdvanceSelfDash observes it.
            if (_selfDashKey == ActionKey.Unknown) RestoreCursor();
        }

        public int ActiveCombinationCount()
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null) return 0;
            var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno);
            if (buff == null || !buff.Active || buff.TimeLeftSeconds == null) return 0;
            int count = 0;
            for (int icon = 2; icon <= 5; icon++)
                if (icon < buff.TimeLeftSeconds.Length && buff.TimeLeftSeconds[icon] > 0) count++;
            return count;
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip) return;
            PaintJuggerFocus();
            _combinationDrawCount = 0;
            _combinationIcon = null;
            if (!ShowCombinationCount || Hud.Render.UiHidden
                || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null
                || Hud.Game.Me.HeroClassDefinition == null
                || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.Monk) return;
            int count = ActiveCombinationCount();
            if (count == 0) return;
            var skill = CombinationDisplaySkill();
            if (skill == null) return;
            var skillUi = Hud.Render.GetPlayerSkillUiElement(skill.Key);
            if (skillUi == null || !skillUi.Visible) return;
            var skillBox = skillUi.Rectangle;
            if (skillBox.Width <= 0 || skillBox.Height <= 0) return;
            // Draw only text on the actual skill button; its rectangle follows UI scale,
            // monitor layout and skill reassignment. Never steer the cursor while painting.
            var layout = _combinationFont.GetTextLayout(count.ToString());
            _combinationFont.DrawText(layout,
                skillBox.Right - skillBox.Width / 8.0f - layout.Metrics.Width,
                skillBox.Bottom - layout.Metrics.Height - skillBox.Width / 15.0f);
            _combinationIcon = skillUi;
            _combinationDrawCount = count;
        }

        private IPlayerSkill CombinationDisplaySkill()
        {
            IPlayerSkill held = null, previous = null;
            foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if (skill == null || skill.SnoPower == null || GeneratorIcon(skill) == 0) continue;
                if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno) return skill;
                if (skill.Key == _combinationDisplayKey) previous = skill;
                if (skill.Key != _pulse && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key))
                    if (held == null || skill.Key == _mainKey || skill.Key == ActionKey.LeftSkill) held = skill;
            }
            if (held != null) _combinationDisplayKey = held.Key;
            // Keep the last manual generator's count visible while its buffs wind down.
            return held ?? previous;
        }

        private void CancelTarget()
        {
            if (_assistActive && TargetEffectsConfirmed()) _assistMaintenanceAttempts.Remove(_targetSno);
            if (_selfDashKey != ActionKey.Unknown)
            {
                if (_selfDashApproach && _selfDashCastTick != int.MinValue)
                {
                    _assistDashOutcome = "cancelled-after-press";
                }
                _selfDashResult = "cancelled";
                // A moving mouse must not cause repeated aim/restore attempts every frame.
                _nextDashAimTick = unchecked(Environment.TickCount + 250);
            }
            _selfDashKey = ActionKey.Unknown;
            _selfDashCastTick = int.MinValue;
            _selfDashBuffBefore = null;
            _assistDashLanding = null;
            _assistDashEscapeOnly = false;
            _assistDashLandingWorldId = 0u;
            if (_pulse != ActionKey.Unknown)
                s7o_GenMonkInput.Up(Owner, _pulse);
            _pulse = ActionKey.Unknown;
            _pulseIsMaintenance = _pulseIsRetarget = false;
            RestoreCursor();
            _targetKey = ActionKey.Unknown;
            _targetSno = 0;
            _targetForesight = false;
            _targetForesightBefore = 0;
            _attempts = 0;
        }

        private bool AssistRequested()
        {
            if (_assistCaptureSuppressed) return false;
            if (_assistWaitForRelease)
            {
                if (s7o_GenMonkInput.IsVirtualKeyDown(MeleeAssistVirtualKey)) return false;
                _assistWaitForRelease = false;
            }
            return MeleeAssistEnabled && s7o_GenMonkInput.IsVirtualKeyDown(MeleeAssistVirtualKey);
        }

        private bool IsMeleeTarget(IMonster monster, double range)
        {
            return monster != null && monster.IsAlive && monster.Attackable && monster.IsOnScreen
                && !monster.Illusion && !monster.Invulnerable && !monster.Untargetable
                && !monster.Hidden && !monster.Invisible && monster.FloorCoordinate != null
                && monster.NormalizedXyDistanceToMe <= range && monster.ZDistanceToMeAbsolute <= 8;
        }

        private uint AttackedIdentity()
        {
            foreach (uint modifier in AttackIdentityModifiers)
            {
                try
                {
                    uint identity = Hud.Game.Me.GetAttributeValueAsUInt(Hud.Sno.Attributes.Last_ACD_Attacked, modifier, 0u);
                    if (identity != 0u && identity != uint.MaxValue) return identity;
                }
                catch { }
            }
            return 0u;
        }

        private bool AttackedIdentityMatches(uint acd, uint ann)
        {
            uint identity = AttackedIdentity();
            if (identity == 0u) return false;
            if (identity == acd || (ann != 0u && identity == ann)) return true;
            foreach (var monster in Hud.Game.AliveMonsters)
                if (monster != null && monster.AcdId == acd && monster.AnnId == identity) return true;
            return false;
        }

        private IMonster ManualAttackTarget()
        {
            uint identity = AttackedIdentity();
            double range = Math.Max(1.0, Math.Min(15.0, AttackTargetRange));
            foreach (var monster in Hud.Game.AliveMonsters)
                if (IsMeleeTarget(monster, range) && identity != 0u
                    && (monster.AcdId == identity || monster.AnnId == identity)) return monster;
            // Only fall back to an actually highlighted melee monster during a real attack.
            var selected = Hud.Game.SelectedActor as IMonster;
            return IsMeleeTarget(selected, range) && selected.IsSelected ? selected : null;
        }

        private bool MainGeneratorAnimation()
        {
            if (Hud.Game.Me.AnimationState != AcdAnimationState.Attacking) return false;
            string animation = Hud.Game.Me.Animation.ToString();
            foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if (skill == null || skill.Key != _mainKey || skill.SnoPower == null) continue;
                if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
                    return HundredFistsAttackWindow();
                int icon = GeneratorIcon(skill);
                return icon != 0 && animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) < 0
                    && BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, icon) >= 9.0;
            }
            return false;
        }

        private bool IsAssistVisiblePoint(IWorldCoordinate point)
        {
            if (!IsAssistPositionWorldPoint(point)) return false;
            // ZoomAware patches this native projection. Native IsOnScreen/Attackable can
            // still use the stock camera bounds; no dependency on the optional zoom plugin.
            var screen = point.ToScreenCoordinate(true, true);
            return screen != null && AssistPositionFinite(screen.X) && AssistPositionFinite(screen.Y)
                && screen.X >= 0 && screen.Y >= 0
                && screen.X < Hud.Window.Size.Width && screen.Y < Hud.Window.Size.Height;
        }

        private bool IsAssistMonsterValid(IMonster monster)
        {
            // Attackable includes native IsOnScreen in FreeHUD. Keep its hard exclusions,
            // then use the current projection separately for the held assist only.
            return monster != null && monster.IsAlive && !monster.Illusion
                && !monster.Invulnerable && !monster.Untargetable && !monster.Hidden
                && !monster.Invisible && !monster.Stealthed && monster.WorldId == Hud.Game.Me.WorldId
                && IsAssistPositionWorldPoint(monster.FloorCoordinate)
                && Hud.Game.Me.FloorCoordinate.XYDistanceTo(monster.FloorCoordinate) <= AssistVisibleTargetRange;
        }

        private bool IsAssistEligible(IMonster monster)
        {
            return IsAssistMonsterValid(monster) && IsAssistVisiblePoint(monster.FloorCoordinate);
        }

        private static bool IsAssistShockTower(IMonster monster)
        {
            return monster != null && monster.SnoActor != null
                && (uint)monster.SnoActor.Sno == 322194u;
        }

        private static bool IsAssistRangedThreat(IMonster monster)
        {
            if (monster == null) return false;
            if (monster.SnoActor != null && AssistRangedThreatActors.Contains((uint)monster.SnoActor.Sno)) return true;
            return monster.SnoMonster != null && monster.SnoMonster.NameEnglish != null
                && AssistRangedThreatNames.Contains(monster.SnoMonster.NameEnglish);
        }

        private void RefreshAssistPanic()
        {
            var defense = Hud.Game.Me.Defense;
            _assistHealthFraction = defense != null && defense.HealthMax > 0
                ? defense.HealthCur / defense.HealthMax : 1.0;
            _assistIncomingDamage = defense != null ? Math.Max(0, defense.CurrentDamageTakenPerSecond) : 0;
            // Incoming damage is aggregate: this does not claim which monster hit the player.
            _assistPanicActive = _assistHealthFraction < 0.75 && _assistIncomingDamage > 0;
        }

        private bool IsAssistCircleBlocked(AssistPositionCircle circle)
        {
            if (circle.Actor == null) return true;
            AssistBlockedTarget blocked;
            if (!_assistBlockedCircles.TryGetValue(circle.Actor.AcdId, out blocked)) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            var center = circle.Actor.FloorCoordinate;
            double dx = me.X - blocked.PlayerX, dy = me.Y - blocked.PlayerY;
            double cx = center.X - blocked.TargetX, cy = center.Y - blocked.TargetY;
            if (Hud.Game.Me.WorldId != blocked.World || dx * dx + dy * dy >= 100.0 || cx * cx + cy * cy >= 4.0)
            { _assistBlockedCircles.Remove(circle.Actor.AcdId); return false; }
            return true;
        }

        private void BlockAssistDashCircle()
        {
            if (_assistDashCircleAcd == 0u) return;
            foreach (var circle in _assistPositionCircles)
            {
                if (circle.Actor == null || circle.Actor.AcdId != _assistDashCircleAcd) continue;
                if (_assistBlockedCircles.Count >= AssistCircleCacheLimit && !_assistBlockedCircles.ContainsKey(_assistDashCircleAcd))
                {
                    uint oldest = 0u;
                    foreach (var key in _assistBlockedCircles.Keys) { oldest = key; break; }
                    _assistBlockedCircles.Remove(oldest);
                }
                _assistBlockedCircles[_assistDashCircleAcd] = new AssistBlockedTarget
                {
                    PlayerX = _assistDashOriginX, PlayerY = _assistDashOriginY,
                    TargetX = circle.Actor.FloorCoordinate.X, TargetY = circle.Actor.FloorCoordinate.Y,
                    World = Hud.Game.Me.WorldId
                };
                return;
            }
        }

        private bool IsAssistTargetTemporarilyBlocked(IMonster monster)
        {
            AssistBlockedTarget blocked;
            if (!_assistBlockedTargets.TryGetValue(monster.AcdId, out blocked)) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            var target = monster.FloorCoordinate;
            double px = me.X - blocked.PlayerX, py = me.Y - blocked.PlayerY;
            double tx = target.X - blocked.TargetX, ty = target.Y - blocked.TargetY;
            if (Hud.Game.Me.WorldId != blocked.World || px * px + py * py >= AssistNavigationRetryYards * AssistNavigationRetryYards || tx * tx + ty * ty >= 16.0)
            { _assistBlockedTargets.Remove(monster.AcdId); return false; }
            return true; // Retry when geometry changes, not on a periodic Dash spam timer.
        }

        private void BlockAssistNavigationTarget(IMonster target)
        {
            if (_assistBlockedTargets.Count >= 32 && !_assistBlockedTargets.ContainsKey(target.AcdId))
            {
                uint oldest = 0u;
                foreach (var key in _assistBlockedTargets.Keys) { oldest = key; break; }
                _assistBlockedTargets.Remove(oldest);
            }
            var me = Hud.Game.Me.FloorCoordinate;
            _assistBlockedTargets[target.AcdId] = new AssistBlockedTarget
            {
                PlayerX = me.X, PlayerY = me.Y, TargetX = target.FloorCoordinate.X,
                TargetY = target.FloorCoordinate.Y, World = Hud.Game.Me.WorldId
            };
            // Failed geometry suppresses Dash, not the target's native navigation.
            // Removing the last candidate would leave no owner able to recover movement.
            _assistStatus = "navigation-blocked-dash";
        }



        private void ResetAssistContactRecovery()
        {
            _assistCorrectiveDashSpent = false;
            _assistDashContinuationPending = false;
            _assistNavigationPending = _assistNavigationRetrySpent = false;
            _assistNavigationTargetAcd = 0u;
            _assistNavigationSinceTick = _assistRunningSinceTick = _assistNavigationStartedTick = int.MinValue;
            _assistNavigationPositionGameTick = int.MinValue;
            _assistNavigationProgressYards = 0;
            _assistNavigationReacquired = _assistNavigationFinalDashSpent = false;
            _assistNavigationHoverConfirmed = false; _assistNavigationReason = "none";
            _assistNavigationHeldAcd = _assistNavigationHeldAnn = _assistNavigationProbeAcd = 0u;
            _assistNavigationLockGameTick = _assistNavigationMotionGameTick = int.MinValue;
            _assistNavigationFallbackSpent = false;
            _assistNavigationFailedKeys = 0;
        }

        private bool AssistContactCorrectionDue(IMonster target, int now)
        {
            if (target == null || target.AcdId != _assistTargetAcd || _assistNavigationPending) return false;
            bool ownInput = _assistAttackKey != ActionKey.Unknown
                || (_pulseIsMaintenance && s7o_GenMonkInput.Owns(_pulse)
                    && ElapsedMs(now, _pulseStartedTick) >= 35);
            bool running = ownInput && Hud.Game.Me.AnimationState == AcdAnimationState.Running;
            if (!running || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target))
            { _assistRunningSinceTick = int.MinValue; return false; }
            if (_assistRunningSinceTick == int.MinValue) _assistRunningSinceTick = now;
            // An in-range attack owns standstill. Persistent Running is a contact
            // failure, not permission to keep chasing or endlessly redashing.
            if (ElapsedMs(now, _assistRunningSinceTick) < 150) return false;
            if (_assistCorrectiveDashSpent)
            {
                BeginAssistNativeNavigation(target, Hud.Game.Me.FloorCoordinate, now);
                return false;
            }
            return true;
        }

        private void BeginAssistNativeNavigation(IMonster target, IWorldCoordinate origin, int now)
        {
            if (target == null || !IsAssistPositionWorldPoint(origin)) return;
            if (_assistNavigationPending && _assistNavigationTargetAcd == target.AcdId) return;
            ReleaseAssistAttack(); // Releases owned standstill too; physical Shift is untouched.
            _assistNavigationPending = true;
            _assistNavigationTargetAcd = target.AcdId;
            _assistNavigationOriginX = _assistNavigationProgressX = origin.X;
            _assistNavigationOriginY = _assistNavigationProgressY = origin.Y;
            _assistNavigationOriginZ = _assistNavigationProgressZ = origin.Z;
            _assistNavigationGoalDistance = origin.XYDistanceTo(target.FloorCoordinate);
            _assistNavigationProgressYards = 0;
            _assistNavigationReacquired = false;
            _assistNavigationPositionGameTick = _assistNavigationMotionGameTick = Hud.Game.CurrentGameTick;
            _assistNavigationFailedKeys = 0;
            _assistNavigationStartedTick = _assistNavigationSinceTick = now;
            _assistNavigationFallbacks++;
            _assistNavigationHoverConfirmed = false; _assistNavigationReason = "failed-approach";
            _assistNavigationHeldAcd = _assistNavigationHeldAnn = _assistNavigationProbeAcd = 0u;
            _assistNavigationFallbackSpent = false;
            _assistProbeTargetAcd = 0u; // Begin a bounded native hover acquisition for this recovery.

            _assistDashRetryNeedsAttack = true;
            _assistDashContinuationPending = _assistPositionReplanPending = false;
            BlockAssistNavigationTarget(target);
        }

        private bool AdvanceAssistNavigation(IMonster target, int now, bool canDash, out bool retryDash)
        {
            retryDash = false;
            if (!_assistNavigationPending || _assistNavigationTargetAcd != target.AcdId) return true;
            // Acknowledged navigation hands back at the same reach used by stationary attacks.
            // Do not require the pathing generator to walk inside an Electrified exclusion.
            bool acknowledgedContact = _assistNavigationHoverConfirmed && _assistNavigationHeldAcd == target.AcdId
                && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target) && !MapAssistTargetBlocked(target);
            if ((HasAssistAttackContact(target) || acknowledgedContact) && !MapAssistTargetBlocked(target))
            {
                ReleaseAssistAttack();
                ResetAssistContactRecovery();
                _assistDashRetryNeedsAttack = false;
                return true;
            }
            uint navigationIdentity = AttackedIdentity();
            bool nativeContact = _assistAttackKey != ActionKey.Unknown
                && s7o_GenMonkInput.Owns(_assistAttackKey)
                && Hud.Game.CurrentGameTick != _assistNavigationLockGameTick
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking
                && AttackedIdentityMatches(target.AcdId, target.AnnId)
                && (navigationIdentity != _assistNavigationIdentityAtDown || _assistNavigationProgressYards >= 1.0);
            if (nativeContact && canDash && !_assistNavigationFinalDashSpent)
            {
                // A long-reach navigation hit is not WotHF contact. Try one different
                // safe landing; failed-destination memory still rejects the old wall point.
                ReleaseAssistAttack();
                _assistNavigationFinalDashSpent = true;
                _assistNavigationPending = false;
                _assistNavigationReason = "final-contact-dash";
                retryDash = true;
                return true;
            }
            var me = Hud.Game.Me.FloorCoordinate;
            int tick = Hud.Game.CurrentGameTick;
            double dx = me.X - _assistNavigationOriginX, dy = me.Y - _assistNavigationOriginY;
            double stepX = me.X - _assistNavigationProgressX, stepY = me.Y - _assistNavigationProgressY;
            double stepZ = me.Z - _assistNavigationProgressZ;
            double step = Math.Sqrt(stepX * stepX + stepY * stepY + stepZ * stepZ);
            if (tick != _assistNavigationPositionGameTick)
            {
                _assistNavigationPositionGameTick = tick;
                // Accumulate real displacement, not sub-yard animation jitter.
                if (step >= 1.0)
                {
                    _assistNavigationProgressYards += step;
                    _assistNavigationProgressX = me.X; _assistNavigationProgressY = me.Y; _assistNavigationProgressZ = me.Z;
                    _assistNavigationSinceTick = now;
                    _assistNavigationMotionGameTick = tick;
                    _assistNavigationFailedKeys = 0; // New position permits previously ineffective keys.
                }
            }
            double goalDistance = me.XYDistanceTo(target.FloorCoordinate);
            if (_assistNavigationProgressYards >= AssistNavigationRetryYards
                && dx * dx + dy * dy + (me.Z - _assistNavigationOriginZ) * (me.Z - _assistNavigationOriginZ) >= 25.0
                && goalDistance <= _assistNavigationGoalDistance + 1.0)
            {
                // A useful detour permits another Dash; a new failure resumes navigation.
                ReleaseAssistAttack();
                _assistNavigationPending = false;
                _assistNavigationRetrySpent = false;
                _assistCorrectiveDashSpent = false;
                _assistBlockedTargets.Remove(target.AcdId);
                retryDash = true;
                return true;
            }
            bool heldKeyboard = AssistNavigationKeyBit(_assistAttackKey) != 0
                && s7o_GenMonkInput.Owns(_assistAttackKey) && s7o_GenMonkInput.IsDown(_assistAttackKey);
            if (heldKeyboard && !s7o_GenMonkInput.IsStandstillDown())
            {
                // Failure evidence only: moving pathing and confirmed melee hand back above.
                // There is no sleep/delay before Dash, target changes or successful attacks.
                int since = Math.Max(_assistNavigationLockGameTick, _assistNavigationMotionGameTick);
                int noMotionTicks = Math.Max(0, tick - since);
                bool idle = Hud.Game.Me.AnimationState == AcdAnimationState.Idle
                    || Hud.Game.Me.AnimationState == AcdAnimationState.NotAnimating;
                int budget = idle ? 8 : AssistNavigationMotionBudgetTicks();
                if (noMotionTicks >= budget)
                    RecoverAssistNavigation(target, idle ? "idle-keyboard-reacquire" : "no-motion-keyboard-reacquire");
            }
            return true;
        }

        private IMonster AssistNavigationLocalFallback(IMonster goal, IMonster hovered)
        {
            IMonster best = null;
            int bestTier = -1;
            foreach (var candidate in _assistCandidates)
            {
                if (candidate.AcdId == goal.AcdId
                    || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, candidate)
                    || !IsAssistNativeFollowHazardFree(candidate)) continue;
                int tier = AssistTargetTier(candidate);
                if (tier < 0 || tier < bestTier) continue;
                int x, y;
                if (!TryGetAssistAim(candidate, out x, out y)) continue;
                if (tier > bestTier || best == null || candidate.AcdId == (hovered != null ? hovered.AcdId : 0u)
                    || (best.AcdId != (hovered != null ? hovered.AcdId : 0u)
                        && candidate.NormalizedXyDistanceToMe < best.NormalizedXyDistanceToMe))
                { best = candidate; bestTier = tier; }
            }
            return best; // Local damage only; the navigation goal/elite remains retained.
        }

        private static int AssistNavigationKeyBit(ActionKey key)
        {
            int value = (int)key;
            return value >= (int)ActionKey.Skill1 && value <= (int)ActionKey.Skill4 ? 1 << (value - (int)ActionKey.Skill1) : 0;
        }

        private int AssistNavigationMotionBudgetTicks()
        {
            double speed = Hud.Game.Me.Offense != null ? Hud.Game.Me.Offense.AttackSpeed : 1.0;
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) speed = 1.0;
            // One attack opportunity plus three update ticks, capped as a failure watchdog.
            return (int)Math.Max(12.0, Math.Min(36.0, Math.Ceiling(60.0 / speed) + 3.0));
        }

        private IPlayerSkill AssistNavigationGenerator(int failedKeys)
        {
            IPlayerSkill best = null;
            int bestRank = int.MaxValue;
            foreach (var generator in _generators)
            {
                int bit = AssistNavigationKeyBit(generator.Key);
                if (bit == 0 || (failedKeys & bit) != 0 || s7o_GenMonkInput.IsDown(generator.Key)) continue;
                uint sno = generator.SnoPower.Sno;
                int rank = sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno ? 0
                    : sno == Hud.Sno.SnoPowers.Monk_FistsOfThunder.Sno ? 1
                    : sno == Hud.Sno.SnoPowers.Monk_CripplingWave.Sno ? 2 : 3;
                if (rank < bestRank) { best = generator; bestRank = rank; }
            }
            return best;
        }

        private void RecoverAssistNavigation(IMonster target, string reason)
        {
            _assistNavigationFailedKeys |= AssistNavigationKeyBit(_assistAttackKey);
            ReleaseAssistAttack(); // Never release physical LMB or the user's standstill.
            _assistNavigationHeldAcd = _assistNavigationHeldAnn = _assistNavigationProbeAcd = 0u;
            _assistNavigationHoverConfirmed = false;
            _assistProbeTargetAcd = 0u;
            _assistProbeLocked = _assistProbeExhausted = false; _assistProbePhase = 0;
            _assistNavigationRecoveries++;
            _assistNavigationReacquired = true; // Selection may use an unblocked elite peer.
            BlockAssistNavigationTarget(target);
            _assistNavigationReason = reason;
            // Try another keyboard generator first. After the available keys fail, one
            // reachable damage fallback may run without replacing/chasing away from the goal.
            if (AssistNavigationGenerator(_assistNavigationFailedKeys) == null && !_assistNavigationFallbackSpent)
            {
                var local = AssistNavigationLocalFallback(target, Hud.Game.SelectedActor as IMonster);
                if (local != null)
                {
                    _assistNavigationFallbackSpent = true;
                    _assistNavigationProbeAcd = local.AcdId;
                    _assistNavigationFailedKeys = 0;
                    _assistNavigationReason = "no-motion-local-fallback";
                }
            }
            // Preserve blocked landing/final-Dash budgets; a rearmed key is not movement.
        }

        private bool HandleAssistNativeNavigation(IMonster target, int now)
        {
            if (!_assistNavigationPending || _assistNavigationTargetAcd != target.AcdId) return false;
            if (!RefreshAssistPositionActors() || !IsAssistNativeFollowHazardFree(target))
            {
                ReleaseAssistAttack();
                _assistNavigationPending = false;
                _assistPositionReplanPending = true;
                _assistNavigationReason = "unsafe-follow";
                _assistStatus = "navigation-unsafe-follow";
                return true;
            }
            ReleaseAssistScopedStandstill();
            if (_assistAttackKey != ActionKey.Unknown
                && _assistAttackKey != ActionKey.LeftSkill && _assistAttackKey != ActionKey.RightSkill
                && s7o_GenMonkInput.Owns(_assistAttackKey) && s7o_GenMonkInput.IsDown(_assistAttackKey))
            {
                IMonster locked = FindAssistCandidate(_assistNavigationHeldAcd);
                uint identity = AttackedIdentity();
                bool attacking = Hud.Game.Me.AnimationState == AcdAnimationState.Attacking;
                // Last_ACD_Attacked can be stale during Running or an earlier animation.
                // Reject only a fresh changed identity/new attack, never useful native pathing.
                bool freshWrongAttack = attacking && Hud.Game.CurrentGameTick != _assistNavigationLockGameTick
                    && identity != 0u && identity != uint.MaxValue
                    && identity != _assistNavigationIdentityAtDown
                    && !AttackedIdentityMatches(_assistNavigationHeldAcd, _assistNavigationHeldAnn);
                bool localContact = locked != null && locked.AcdId != target.AcdId
                    && Hud.Game.CurrentGameTick != _assistNavigationLockGameTick
                    && HasAssistAttackContact(locked);
                if (locked != null && !freshWrongAttack && !localContact)
                {
                    _assistStatus = s7o_GenMonkInput.IsStandstillDown() ? "navigate-user-standstill"
                        : locked.AcdId == target.AcdId ? "navigate-keyboard" : "navigate-local-fallback";
                    return true;
                }
                ReleaseAssistAttack(); // Only this assist's key; physical LMB/Shift are never released.
                _assistNavigationHeldAcd = _assistNavigationHeldAnn = _assistNavigationProbeAcd = 0u;
                _assistNavigationHoverConfirmed = false;
                _assistNavigationReason = freshWrongAttack ? "wrong-attack-reacquire"
                    : localContact ? "local-contact-reacquire-goal" : "held-target-lost";
                if (localContact)
                {
                    // A confirmed hit is useful progress: another full goal probe may
                    // use one local hit again. A miss never replenishes this budget.
                    _assistNavigationFallbackSpent = false;
                    _assistNavigationSinceTick = now;
                }
                _assistProbeTargetAcd = 0u;
            }
            IPlayerSkill navigation = AssistNavigationGenerator(_assistNavigationFailedKeys);
            if (navigation == null && _assistNavigationFailedKeys != 0)
            {
                // All available keys had an ineffective hold. Reacquire one instead of
                // permanently idling; blocked Dash points remain suppressed until progress.
                _assistNavigationFailedKeys = 0;
                navigation = AssistNavigationGenerator(0);
            }
            if (navigation == null) { _assistStatus = "navigate-no-keyboard-generator"; return true; }
            IMonster probe = _assistNavigationProbeAcd != 0u ? FindAssistCandidate(_assistNavigationProbeAcd) : target;
            if (probe == null)
            { _assistNavigationProbeAcd = 0u; _assistProbeTargetAcd = 0u; probe = target; }
            RefreshAssistEliteProbe(probe);
            CursorPoint cursor;
            if (!GetCursorPos(out cursor)) { _assistStatus = "navigate-cursor-failed"; return true; }
            var hovered = Hud.Game.SelectedActor as IMonster;
            bool freshAim = _assistCursorOwned && !_assistAimPending
                && cursor.X == _assistAimX && cursor.Y == _assistAimY
                && Hud.Game.CurrentGameTick != _assistAimGameTick;
            bool exactHover = freshAim && hovered != null && hovered.IsAlive
                && hovered.IsSelected && hovered.AcdId == probe.AcdId;
            if (!exactHover && freshAim && _assistProbeExhausted)
            {
                // One finite alternative after a body/side scan: attack a verified local
                // candidate while retaining the original goal. Never follow a far minion.
                if (!_assistNavigationFallbackSpent)
                {
                    _assistNavigationFallbackSpent = true;
                    var local = AssistNavigationLocalFallback(target, hovered);
                    if (local != null)
                    {
                        _assistNavigationProbeAcd = local.AcdId;
                        _assistNavigationReason = "probe-local-fallback";
                        _assistProbeTargetAcd = 0u;
                        probe = local;
                        exactHover = hovered != null && hovered.IsAlive && hovered.IsSelected && hovered.AcdId == local.AcdId;
                        if (!exactHover) RefreshAssistEliteProbe(probe);
                    }
                }
                if (!exactHover && _assistProbeExhausted)
                {
                    // Permit another unblocked peer in the same priority tier next collect.
                    // Keep a sole goal and its spent scan; never send an unverified DOWN.
                    _assistNavigationReacquired = true;
                    BlockAssistNavigationTarget(target);
                    _assistNavigationReason = "probe-exhausted";
                    _assistStatus = "navigate-hover-unavailable";
                    return true;
                }
            }
            int x, y;
            if (exactHover)
            {
                // Consume the acknowledged position before camera reprojection. A fresh
                // user cursor move instead requires another native acknowledgement.
                x = _assistAimX; y = _assistAimY;
            }
            else
            {
                if (!TryGetAssistAim(probe, out x, out y))
                { _assistStatus = "navigate-ui-blocked"; return true; }
                if (cursor.X != x || cursor.Y != y || _assistAimPending)
                {
                    if (!SetCursorPos(x, y)) { _assistStatus = "navigate-cursor-failed"; return true; }
                    _assistAimGameTick = Hud.Game.CurrentGameTick;
                    _assistAimPending = false;
                    _assistAimX = x; _assistAimY = y; _assistCursorOwned = true;
                    _assistStatus = "navigate-aim";
                    return true;
                }
                _assistStatus = "navigate-probe";
                return true; // No DOWN on an unacknowledged/wrong target.
            }
            ReleaseAssistAttack();
            if (!s7o_GenMonkInput.Down(Owner, navigation.Key))
            { _assistStatus = "navigate-input-busy"; return true; }
            _assistAimPending = false;
            _assistAimX = x; _assistAimY = y; _assistCursorOwned = true;
            _assistNavigationHeldAcd = probe.AcdId; _assistNavigationHeldAnn = probe.AnnId;
            _assistNavigationIdentityAtDown = AttackedIdentity();
            _assistNavigationLockGameTick = Hud.Game.CurrentGameTick;
            _assistNavigationHoverConfirmed = true;
            _assistNavigationReason = probe.AcdId == target.AcdId ? "fresh-hover-keyboard" : "fresh-local-hover-keyboard";
            _assistAttackKey = navigation.Key; _assistAttackStartedTick = now;
            _assistStatus = probe.AcdId == target.AcdId ? "navigate-keyboard" : "navigate-local-fallback";
            return true;
        }

        private bool IsAssistEscapeLandingBlocked(IWorldCoordinate point)
        {
            if (_assistBlockedEscapeLandings.Count == 0) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            double dx = me.X - _assistEscapeBlockOriginX, dy = me.Y - _assistEscapeBlockOriginY;
            if (_assistEscapeBlockWorld != Hud.Game.Me.WorldId || dx * dx + dy * dy >= 9.0)
            { _assistBlockedEscapeLandings.Clear(); return false; }
            // Saturation is a failed-geometry budget, not permission to repeat the
            // unrecorded seventeenth exit indefinitely. Geometry change clears it above.
            if (_assistBlockedEscapeLandings.Count >= 16) return true;
            foreach (var blocked in _assistBlockedEscapeLandings)
                if (point.XYDistanceTo(blocked) <= 2.0f) return true;
            return false;
        }

        private void BlockAssistEscapeLanding(IWorldCoordinate point, IWorldCoordinate actual)
        {
            if (IsAssistEscapeLandingBlocked(point)) return;
            if (_assistBlockedEscapeLandings.Count == 0)
            {
                _assistEscapeBlockOriginX = actual.X; _assistEscapeBlockOriginY = actual.Y;
                _assistEscapeBlockWorld = Hud.Game.Me.WorldId;
            }
            if (_assistBlockedEscapeLandings.Count < 16)
                _assistBlockedEscapeLandings.Add(point.Offset(0f, 0f, 0f));
        }

        private void ObserveAssistDashContact(IWorldCoordinate actual, int now)
        {
            if (!_selfDashApproach) return;
            if (!_assistActive || !IsAssistPositionWorldPoint(actual) || _assistDashLanding == null)
            { _assistDashOutcome = "unassessed-landing"; return; }
            double dx = actual.X - _assistDashOriginX, dy = actual.Y - _assistDashOriginY;
            _assistDashTravelObserved = (float)Math.Sqrt(dx * dx + dy * dy);
            _assistDashVerticalObserved = Math.Abs(actual.Z - _assistDashOriginZ);
            bool readable = RefreshAssistPositionActors();
            bool hazardFree = readable && IsAssistPositionHazardFree(actual);
            bool electricalFree = readable && IsAssistElectrifiedPointFree(actual, FindAssistCandidate(_selfDashTargetAcd));
            _assistDashSafetyAssessed = true;
            _assistDashActualSafe = hazardFree && !_assistDamageEscapePending;
            _assistDashSafetyReason = !readable ? "unreadable" : !hazardFree ? "hazard"
                : _assistDamageEscapePending ? "damage-escape" : !electricalFree ? "electrified-preference" : "safe";
            // Short exits are just as critical as long ones. Buff refresh with no fresh
            // landing evidence, or a partial landing still inside a core/blast, is failure.
            bool verticalProgress = _assistDashVerticalRequested > 0.75f
                && _assistDashVerticalRequested - Math.Abs(actual.Z - _assistDashLanding.Z) >= 1f;
            bool littleTravel = (_assistDashTravelRequested > 0.75f || _assistDashVerticalRequested > 0.75f)
                && !verticalProgress && (_assistDashTravelRequested <= 0.75f
                    || _assistDashTravelObserved < Math.Min(2.0f, _assistDashTravelRequested * 0.25f));
            // Settled partial travel against a wall is not arrival: stop repeating
            // that shortcut and let a grid detour/native navigation own recovery.
            var liveTarget = FindAssistCandidate(_selfDashTargetAcd);
            bool missedLanding = !_assistDashForHazard && !_assistDashEscapeOnly
                && _assistDashTravelRequested > 4f && actual.XYDistanceTo(_assistDashLanding) > 4f
                && (liveTarget == null || !CanAttackAssistFrom(actual, liveTarget) || MapAssistTargetBlocked(liveTarget));
            bool failed = _assistDashMovementFailed = !_assistDashLandingConfirmed || littleTravel || missedLanding;
            MapAssistFeedback(failed?"dash-blocked":"dash-landed",_selfDashTargetAcd,actual,failed);
            if (failed)
            {
                // Movement failure survives an unsafe landing; danger cannot erase geometry.
                RememberAssistFailedLanding(_assistDashLanding, FindAssistCandidate(_selfDashTargetAcd), actual);
                BlockAssistDashCircle();
            }
            if (_assistDashForHazard || _assistDashEscapeOnly)
            {
                if (!failed && _assistDashActualSafe) { _assistDashOutcome = "safe-exit"; return; }
                BlockAssistEscapeLanding(_assistDashLanding, actual);
                _assistDashOutcome = !_assistDashActualSafe ? "unsafe-exit" : "failed-exit";
                _selfDashResult = "blocked-escape-landing";
                _assistPositionReplanPending = true;
                _assistApproachPending = true;
                _nextDashAimTick = now;
                return;
            }
            // Physical failure owns recovery even when the landing is unsafe.
            // Hard emergencies can preempt navigation; soft spacing cannot erase it.
            if (failed)
            {
                _assistDashOutcome = littleTravel ? "little-travel" : missedLanding ? "blocked-shortcut" : "unconfirmed";
                BeginAssistNativeNavigation(FindAssistCandidate(_selfDashTargetAcd), actual, now);
                return;
            }
            if (!_assistDashActualSafe)
            {
                _assistDashOutcome = "unsafe-landing";
                _assistPositionReplanPending = true;
                _nextDashAimTick = now;
                return;
            }
            _assistDashOutcome = "safe-landing";
        }

        private void RefreshAssistTargeting()
        {
            RefreshAssistPanic();
            bool ctrlDown = s7o_GenMonkInput.IsVirtualKeyDown(0x11);
            if (_assistActive || AssistRequested() || ctrlDown || _juggerFocusAcd != 0u)
            {
                RefreshAssistCandidates();
                UpdateJuggerFocus(ctrlDown);
                return;
            }
            // Manual generator use does not pay for a full assist candidate snapshot.
            _assistCandidates.Clear();
            _juggerCtrlWasDown = ctrlDown;
            _juggerFocusPaintAllowed = false;
        }

        private void RefreshAssistCandidates()
        {
            _assistCandidates.Clear();
            _assistTrashProfiles.Clear(); _assistTrashMaxValue = 0;
            _assistProjectedBeyondNativeCount = 0;
            _assistOtherElitePackPresent = false;
            foreach (var monster in Hud.Game.AliveMonsters)
                if (IsAssistEligible(monster))
                {
                    _assistCandidates.Add(monster);
                    if (!IsAssistJuggerPackMember(monster)
                        && (IsAssistPositionElite(monster) || monster.Rarity == ActorRarity.RareMinion || monster.IsElite))
                        _assistOtherElitePackPresent = true;
                    if (!monster.IsOnScreen) _assistProjectedBeyondNativeCount++;
                }
            foreach (var monster in _assistCandidates)
                if (AssistTargetTier(monster) == (int)AssistPriority.Trash)
                    _assistTrashMaxValue = Math.Max(_assistTrashMaxValue, AssistTrashProgression(monster));
        }

        private IMonster FindAssistCandidate(uint acd)
        {
            if (acd == 0u) return null;
            foreach (var monster in _assistCandidates)
                if (monster.AcdId == acd) return monster;
            return null;
        }

        private static bool IsAssistLeader(IMonster monster)
        {
            return monster != null && (monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique);
        }

        private static bool HasAssistAffix(IMonster monster, MonsterAffix affix)
        {
            if (monster == null) return false;
            var direct = monster.AffixSnoList;
            if (direct != null)
                foreach (var entry in direct)
                    if (entry != null && entry.Affix == affix) return true;
            var pack = monster.Pack;
            if (pack != null && pack.AffixSnoList != null)
                foreach (var entry in pack.AffixSnoList)
                    if (entry != null && entry.Affix == affix) return true;
            return false;
        }

        private static bool IsAssistJuggerLeader(IMonster monster)
        {
            return IsAssistLeader(monster) && IsAssistJuggerPackMember(monster);
        }

        private static bool IsAssistJuggerPackMember(IMonster monster)
        {
            // Native pack membership/affixes identify the whole pack; never guess by distance.
            return HasAssistAffix(monster, MonsterAffix.Juggernaut);
        }

        private enum AssistPriority
        {
            Trash = 2, PanicRanged = 3, JuggerMinion = 4, JuggerLeader = 5,
            Minion = 6, ShockTower = 7, Leader = 8, Boss = 9, CtrlFocus = 10
        }

        private int AssistTargetTier(IMonster monster)
        {
            if (monster.AcdId == _juggerFocusAcd && IsAssistJuggerLeader(monster)) return (int)AssistPriority.CtrlFocus;
            if (monster.Rarity == ActorRarity.Boss) return (int)AssistPriority.Boss;
            if (IsAssistJuggerPackMember(monster))
                return (int)(IsAssistJuggerLeader(monster) ? AssistPriority.JuggerLeader : AssistPriority.JuggerMinion);
            if (IsAssistLeader(monster)) return (int)AssistPriority.Leader;
            if (IsAssistShockTower(monster) && monster.NormalizedXyDistanceToMe <= AssistShockTowerPriorityRange)
                return (int)AssistPriority.ShockTower;
            if (monster.Rarity == ActorRarity.RareMinion || monster.IsElite) return (int)AssistPriority.Minion;
            if (_assistPanicActive && IsAssistRangedThreat(monster)) return (int)AssistPriority.PanicRanged;
            return (int)AssistPriority.Trash;
        }

        private bool IsJuggerFocusCandidate(IMonster monster)
        {
            if (!IsAssistEligible(monster) || monster.AcdId == 0u || !IsAssistJuggerLeader(monster)
                || _pylonBlocksPlayer || IsNearProtectedPylon(monster.FloorCoordinate)) return false;
            int x, y;
            return TryGetAssistAim(monster, out x, out y)
                && IsLeftClickPointSafe((long)x - Hud.Window.Offset.X, (long)y - Hud.Window.Offset.Y);
        }

        private void ClearJuggerFocus()
        {
            _juggerFocusAcd = 0u;
            _juggerFocusMonster = null;
            _juggerFocusPaintAllowed = false;
        }

        private void PauseJuggerFocus()
        {
            // A CTRL held in a blocked UI must not become a fresh edge when combat resumes.
            _juggerCtrlWasDown = s7o_GenMonkInput.IsVirtualKeyDown(0x11);
            _juggerFocusPaintAllowed = false;
            _assistCandidates.Clear();
            try
            {
                if (!Enabled || Hud.Game.Me == null || Hud.Game.Me.IsDead
                    || (_juggerFocusMonster != null && !IsAssistEligible(_juggerFocusMonster)))
                    ClearJuggerFocus();
            }
            catch { ClearJuggerFocus(); }
        }

        private void UpdateJuggerFocus(bool down)
        {
            // CTRL, rising edge only.
            bool pressed = down && !_juggerCtrlWasDown;
            _juggerCtrlWasDown = down;
            var focused = FindAssistCandidate(_juggerFocusAcd);
            if (!IsJuggerFocusCandidate(focused)) ClearJuggerFocus();
            else _juggerFocusMonster = focused;

            _juggerFocusPaintAllowed = false;
            if (!pressed && _juggerFocusMonster == null) return;
            // Only mark in the same safe combat context as the macro; never move the cursor.
            bool safe = IsLeftClickSafe();
            if (pressed && safe)
            {
                var hovered = !_assistCursorOwned ? Hud.Game.SelectedActor as IMonster : null;
                if (IsJuggerFocusCandidate(hovered))
                {
                    if (hovered.AcdId == _juggerFocusAcd) ClearJuggerFocus();
                    else { _juggerFocusAcd = hovered.AcdId; _juggerFocusMonster = hovered; }
                }
                else if (_juggerFocusAcd != 0u) ClearJuggerFocus();
                else
                {
                    IMonster nearest = null;
                    foreach (var monster in _assistCandidates)
                    {
                        if (!IsAssistJuggerLeader(monster)) continue;
                        if (nearest != null && (monster.NormalizedXyDistanceToMe > nearest.NormalizedXyDistanceToMe
                            || (monster.NormalizedXyDistanceToMe == nearest.NormalizedXyDistanceToMe
                                && monster.AcdId >= nearest.AcdId))) continue;
                        if (IsJuggerFocusCandidate(monster)) nearest = monster;
                    }
                    if (nearest != null) { _juggerFocusAcd = nearest.AcdId; _juggerFocusMonster = nearest; }
                }
            }
            _juggerFocusPaintAllowed = safe && _juggerFocusMonster != null;
        }

        private void PaintJuggerFocus()
        {
            var monster = _juggerFocusMonster;
            if (!Enabled || !_juggerFocusPaintAllowed || Hud.Render.UiHidden
                || _juggerFocusBrush == null || _juggerFocusOutlineBrush == null
                || !IsAssistEligible(monster) || monster.AcdId != _juggerFocusAcd) return;
            var center = monster.FloorCoordinate;
            const int segments = 28;
            const float radius = 10f;
            double step = Math.PI * 2.0 / segments;
            double phase = Hud.Game.CurrentGameTick / (6.5 * 60.0) * Math.PI * 2.0;
            for (int i = 0; i < segments; i++)
            {
                double start = phase + i * step;
                double end = start + step * 0.46;
                var first = center.Offset(radius * (float)Math.Cos(start), radius * (float)Math.Sin(start), 0);
                var last = center.Offset(radius * (float)Math.Cos(end), radius * (float)Math.Sin(end), 0);
                _juggerFocusOutlineBrush.DrawLineWorld(first, last);
                _juggerFocusBrush.DrawLineWorld(first, last);
            }
        }

        private static double AssistTrashProgression(IMonster monster)
        {
            double value = monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0.0;
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);
        }

        private static void AddBoundedTrashCandidate(List<IMonster> list, IMonster monster,
            int capacity, bool byProgression)
        {
            double progression = byProgression ? AssistTrashProgression(monster) : 0.0;
            int index = 0;
            while (index < list.Count)
            {
                var other = list[index];
                double otherProgression = byProgression ? AssistTrashProgression(other) : 0.0;
                if ((byProgression && progression > otherProgression)
                    || ((!byProgression || progression == otherProgression)
                        && (monster.NormalizedXyDistanceToMe < other.NormalizedXyDistanceToMe
                            || (monster.NormalizedXyDistanceToMe == other.NormalizedXyDistanceToMe
                                && monster.AcdId < other.AcdId)))) break;
                index++;
            }
            if (index >= capacity) return;
            list.Insert(index, monster);
            if (list.Count > capacity) list.RemoveAt(capacity);
        }

        private struct AssistTrashCell
        {
            public double Weight;
            public int Count;
            public IMonster Representative;
            public double RepresentativeCenterDistanceSquared;
        }

        private void AccumulateAssistTrashCell(IMonster monster)
        {
            var point = monster.FloorCoordinate;
            const double cellSize = 14.0;
            int cellX = (int)Math.Floor(point.X / cellSize);
            int cellY = (int)Math.Floor(point.Y / cellSize);
            long key = ((long)cellX << 32) | (uint)cellY;
            AssistTrashCell cell;
            _assistTrashCells.TryGetValue(key, out cell);
            cell.Weight += Math.Max(0.05, Math.Min(2.0, AssistTrashProgression(monster)));
            cell.Count++;
            double dx = point.X - (cellX + 0.5) * cellSize;
            double dy = point.Y - (cellY + 0.5) * cellSize;
            double centerDistanceSquared = dx * dx + dy * dy;
            if (cell.Representative == null || centerDistanceSquared < cell.RepresentativeCenterDistanceSquared
                || (centerDistanceSquared == cell.RepresentativeCenterDistanceSquared
                    && monster.AcdId < cell.Representative.AcdId))
            {
                cell.Representative = monster;
                cell.RepresentativeCenterDistanceSquared = centerDistanceSquared;
            }
            _assistTrashCells[key] = cell;
        }

        private void AddBoundedDenseTrashCell(AssistTrashCell cell)
        {
            int index = 0;
            while (index < _assistTrashDenseCells.Count)
            {
                var other = _assistTrashDenseCells[index];
                if (cell.Weight > other.Weight
                    || (cell.Weight == other.Weight && cell.Count > other.Count)
                    || (cell.Weight == other.Weight && cell.Count == other.Count
                        && (cell.Representative.NormalizedXyDistanceToMe < other.Representative.NormalizedXyDistanceToMe
                            || (cell.Representative.NormalizedXyDistanceToMe == other.Representative.NormalizedXyDistanceToMe
                                && cell.Representative.AcdId < other.Representative.AcdId)))) break;
                index++;
            }
            if (index >= 16) return;
            _assistTrashDenseCells.Insert(index, cell);
            if (_assistTrashDenseCells.Count > 16) _assistTrashDenseCells.RemoveAt(16);
        }

        private AssistTrashProfile AssistTrashProfileFor(IMonster monster)
        {
            AssistTrashProfile profile;
            if (_assistTrashProfiles.TryGetValue(monster.AcdId, out profile)) return profile;
            profile.Value = AssistTrashProgression(monster);
            profile.Progression = 0; profile.Count = 0;
            foreach (var other in _assistCandidates)
            {
                if (!AssistSameHeight(monster.FloorCoordinate, other.FloorCoordinate)
                    || AssistTargetTier(other) != (int)AssistPriority.Trash
                    || monster.FloorCoordinate.XYDistanceTo(other.FloorCoordinate) > AssistTrashPackRadius) continue;
                profile.Progression += AssistTrashProgression(other);
                if (++profile.Count >= 64) break;
            }
            _assistTrashProfiles[monster.AcdId] = profile;
            return profile;
        }

        private int AssistTrashClass(AssistTrashProfile profile)
        {
            // A lone valuable body is not density. Real combined progression must
            // justify the pack before individual value or circle preference is applied.
            if (profile.Count >= 3 && profile.Progression >= Math.Max(AssistHighTrashProgression, _assistTrashMaxValue)) return 2;
            return profile.Value >= AssistHighTrashProgression ? 1 : 0;
        }

        private bool IsAssistUsefulCircleTarget(IMonster target)
        {
            if (target == null) return false;
            return AssistTargetTier(target) != (int)AssistPriority.Trash
                || AssistTrashClass(AssistTrashProfileFor(target)) > 0;
        }

        private bool IsAssistCircleCandidateCompatible(IMonster candidate, IMonster retained)
        {
            if (AssistTargetTier(candidate) != (int)AssistPriority.Trash) return true;
            var candidatePack = AssistTrashProfileFor(candidate);
            var retainedPack = AssistTrashProfileFor(retained);
            int candidateClass = AssistTrashClass(candidatePack), retainedClass = AssistTrashClass(retainedPack);
            // Select combat first. Circle retargeting may improve this local pack,
            // never send combat back to a separate pack or a lone small leftover.
            if (candidateClass == 0 || candidateClass < retainedClass
                || candidate.FloorCoordinate.XYDistanceTo(retained.FloorCoordinate) > AssistTrashPackRadius) return false;
            return retainedClass != 1 || candidateClass == 2 || candidatePack.Value >= retainedPack.Value;
        }

        private double AssistTrashPriorityScore(IMonster monster, out double valueScore, out double densityScore)
        {
            var profile = AssistTrashProfileFor(monster);
            valueScore = profile.Value; densityScore = profile.Progression;
            double score = (AssistTrashClass(profile) == 2 ? densityScore : valueScore)
                / (1.0 + monster.NormalizedXyDistanceToMe / AssistTrashPackRadius);
            if (_assistTravelHeadingKnown)
            {
                var me = Hud.Game.Me.FloorCoordinate;
                double dx = monster.FloorCoordinate.X - me.X, dy = monster.FloorCoordinate.Y - me.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 0.01)
                    score *= 1.0 + 0.03 * Math.Max(0.0, (dx * _assistTravelHeadingX + dy * _assistTravelHeadingY) / distance);
            }
            return score; // At most 3% among peers; never overrides the pack/value hierarchy.
        }

        private void RememberAssistTrashAttack(IMonster monster)
        {
            // The caller has native attack-animation and attacked-identity confirmation.
            if (monster == null || monster.AcdId != _assistTargetAcd || AssistTargetTier(monster) != 2
                || Hud.Game.Me.FloorCoordinate == null) return;
            if (_assistLastTrashAttackAcd != 0u && monster.AcdId != _assistLastTrashAttackAcd)
            {
                double travelX = monster.FloorCoordinate.X - _assistTrashAnchorX;
                double travelY = monster.FloorCoordinate.Y - _assistTrashAnchorY;
                double travel = Math.Sqrt(travelX * travelX + travelY * travelY);
                if (travel >= AssistTrashPackRadius)
                { _assistTravelHeadingKnown = true; _assistTravelHeadingX = travelX / travel; _assistTravelHeadingY = travelY / travel; }
            }
            double dx = monster.FloorCoordinate.X - Hud.Game.Me.FloorCoordinate.X;
            double dy = monster.FloorCoordinate.Y - Hud.Game.Me.FloorCoordinate.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.01) return;
            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            _assistLastTrashAttackAcd = monster.AcdId;
            _assistTrashAnchorX = monster.FloorCoordinate.X;
            _assistTrashAnchorY = monster.FloorCoordinate.Y;
            _assistTrashBearingX = dx * inverseLength;
            _assistTrashBearingY = dy * inverseLength;
            _assistTrashFanReady = true;
        }

        private bool HasAssistNearbyTrash()
        {
            foreach(var monster in _assistCandidates)
                if(AssistTargetTier(monster)==(int)AssistPriority.Trash
                    && monster.NormalizedXyDistanceToMe<=NearbyTrashRange)return true;
            return false;
        }
        private IMonster PickAssistTrashTarget(bool allowFan)
        {
            _assistTrashNear.Clear();
            _assistTrashValue.Clear();
            _assistTrashShortlist.Clear();
            _assistTrashCells.Clear();
            _assistTrashDenseCells.Clear();
            _assistTrashScores.Clear();
            _assistTrashValues.Clear();
            _assistTrashDensityValues.Clear();
            // Distance is an acquisition boundary, not a density-score tweak.
            bool localTrash=HasAssistNearbyTrash();
            if(localTrash)_assistAcquisitionCandidates.RemoveAll(m=>m.NormalizedXyDistanceToMe>NearbyTrashRange);
            // Nominate near, valuable and broad dense-cell representatives only on acquisition.
            foreach (var monster in _assistAcquisitionCandidates)
            {
                AddBoundedTrashCandidate(_assistTrashNear, monster, 16, false);
                AddBoundedTrashCandidate(_assistTrashValue, monster, 16, true);
                AccumulateAssistTrashCell(monster);
            }
            foreach (var cell in _assistTrashCells.Values) AddBoundedDenseTrashCell(cell);
            foreach (var monster in _assistTrashNear) _assistTrashShortlist.Add(monster);
            foreach (var monster in _assistTrashValue)
                if (!_assistTrashShortlist.Contains(monster)) _assistTrashShortlist.Add(monster);
            foreach (var cell in _assistTrashDenseCells)
                if (!_assistTrashShortlist.Contains(cell.Representative)) _assistTrashShortlist.Add(cell.Representative);
            IMonster best = null;
            double bestScore = double.NegativeInfinity, bestValue = 0.0;
            int bestClass = -1;
            foreach (var monster in _assistTrashShortlist)
            {
                double value, density;
                double score = AssistTrashPriorityScore(monster, out value, out density);
                _assistTrashScores.Add(score);
                _assistTrashValues.Add(value);
                _assistTrashDensityValues.Add(density);
                int candidateClass = AssistTrashClass(AssistTrashProfileFor(monster));
                if (best == null || candidateClass > bestClass || (candidateClass == bestClass
                    && (score > bestScore || (score == bestScore && monster.AcdId < best.AcdId))))
                { best = monster; bestClass = candidateClass; bestScore = score; bestValue = value; }
            }
            double chosenPackProgress = best != null ? AssistTrashProfileFor(best).Progression : 0.0;
            if (best != null && bestClass == 2)
            {
                var packCenter = best.FloorCoordinate;
                // Select the pack first, then its valuable member, not a distant body.
                foreach (var member in _assistAcquisitionCandidates)
                {
                    if (!AssistSameHeight(member.FloorCoordinate, packCenter)
                        || member.FloorCoordinate.XYDistanceTo(packCenter) > AssistTrashPackRadius) continue;
                    double value = AssistTrashProgression(member);
                    if (value > bestValue || (value == bestValue
                        && member.NormalizedXyDistanceToMe < best.NormalizedXyDistanceToMe))
                    { best = member; bestValue = value; }
                }
            }
            bool usedFan = false;
            if (allowFan && best != null && bestClass == 0)
            {
                IMonster preferred = null;
                double preferredScore = double.NegativeInfinity;
                double scoreTolerance = Math.Max(0.1, Math.Abs(bestScore)) * 0.05;
                double valueTolerance = Math.Max(0.1, Math.Abs(bestValue)) * 0.05;
                for (int i = 0; i < _assistTrashShortlist.Count; i++)
                {
                    var monster = _assistTrashShortlist[i];
                    if (bestScore - _assistTrashScores[i] > scoreTolerance
                        || Math.Abs(bestValue - _assistTrashValues[i]) > valueTolerance) continue;
                    double dx = monster.FloorCoordinate.X - _assistTrashAnchorX;
                    double dy = monster.FloorCoordinate.Y - _assistTrashAnchorY;
                    if (dx * dx + dy * dy > 12.0 * 12.0) continue;
                    double side = _assistTrashBearingX * dy - _assistTrashBearingY * dx;
                    if (Math.Abs(side) <= 0.05 || (side > 0.0) != _assistTrashPreferLeft) continue;
                    double score = _assistTrashScores[i];
                    if (preferred == null || score > preferredScore
                        || (score == preferredScore && monster.AcdId < preferred.AcdId))
                    { preferred = monster; preferredScore = score; }
                }
                if (preferred != null)
                { usedFan = preferred.AcdId != best.AcdId; best = preferred; }
                _assistTrashPreferLeft = !_assistTrashPreferLeft;
                _assistTrashFanReady = false;
            }
            _assistTrashSelectionClass = bestClass;
            _assistSelectionReason = usedFan ? "trash-fan-tie" : bestClass == 2 ? "trash-density-pack" : bestClass == 1 ? "trash-high-value" : "trash-stragglers";
            _assistSelectionTier = 2;
            _assistSelectionFan = usedFan;
            for (int i = 0; i < _assistTrashShortlist.Count; i++)
                if (best != null && _assistTrashShortlist[i].AcdId == best.AcdId)
                {
                    _assistSelectionScore = _assistTrashScores[i];
                    _assistSelectionValue = _assistTrashValues[i];
                    _assistSelectionDensity = _assistTrashDensityValues[i];
                    break;
                }
            if (bestClass == 2) { _assistSelectionDensity = chosenPackProgress; _assistSelectionValue = bestValue; _assistSelectionScore = bestScore; }
            return best;
        }

        private IMonster RecordAssistSelection(IMonster monster, int tier, string reason)
        {
            // Direct diagnostics only. Retained trash keeps its acquisition scores.
            _assistSelectionTier = monster != null ? tier : -1;
            _assistSelectionReason = monster != null ? reason : "none";
            _assistSelectionFan = false;
            if (monster == null || tier != 2)
            {
                _assistSelectionScore = _assistSelectionValue = _assistSelectionDensity = 0.0;
                _assistTrashSelectionClass = 0;
            }
            return monster;
        }


        private IMonster AssistCircleCombatTarget(IMonster retained, int tier)
        {
            if (retained == null || _assistNavigationPending || _assistDashRetryNeedsAttack
                || !RefreshAssistPositionActors()) return retained;
            if (CanAttackFromCurrentDamageCircle(retained)) return retained;
            IWorldCoordinate point;
            string reason;
            int retainedPriority = 0;
            if (TryPickAssistCircleUpgrade(retained, out point, out reason))
                retainedPriority = AssistPositionCirclePriority(point, retained, out reason);
            if (retainedPriority >= 2) return retained;
            IMonster best = retained;
            int bestPriority = retainedPriority;
            double nearest = double.MaxValue;
            int evaluated = 0;
            foreach (var candidate in _assistCandidates)
            {
                if (candidate.AcdId == retained.AcdId || AssistTargetTier(candidate) != tier
                    || IsAssistTargetTemporarilyBlocked(candidate)) continue;
                bool circleNear = false;
                foreach (var circle in _assistPositionCircles)
                    if (circle.Kind != 2 && circle.Actor != null && !circle.Actor.IsDisabled
                        && !IsAssistCircleBlocked(circle)
                        && AssistBodyGap(circle.Actor.FloorCoordinate, candidate)
                            <= AssistCircleLandingRadius + AssistAttackReachYards)
                    { circleNear = true; break; }
                if(AssistTargetTier(candidate)==(int)AssistPriority.Trash&&candidate.NormalizedXyDistanceToMe>NearbyTrashRange&&HasAssistNearbyTrash())continue;
                if (!circleNear || ++evaluated > 16) continue;
                if (!IsAssistCircleCandidateCompatible(candidate, retained)) continue;
                int x, y;
                if (!TryGetAssistAim(candidate, out x, out y)) continue;
                int priority = CanAttackFromCurrentDamageCircle(candidate) ? 2 : 0;
                if (priority == 0 && TryPickAssistCircleUpgrade(candidate, out point, out reason))
                    priority = AssistPositionCirclePriority(point, candidate, out reason);
                if (priority < bestPriority || priority <= retainedPriority) continue;
                double distance = candidate.NormalizedXyDistanceToMe;
                if (priority == bestPriority && distance >= nearest) continue;
                best = candidate; bestPriority = priority; nearest = distance;
            }
            return best;
        }

        private IMonster AssistTarget(bool canDash, int now)
        {
            _assistUiBlockedTargets = false;
            _assistPylonBlockedTargets = false;
            _assistHazardBlockedTargets = false;
            _assistAcquisitionCandidates.Clear();
            IMonster retained = FindAssistCandidate(_assistTargetAcd);
            IMonster moltenReturn = FindAssistCandidate(_assistHazardReturnAcd);
            IMonster moltenSuspended = null;
            if (moltenReturn == null || !IsAssistPositionElite(moltenReturn))
            { _assistHazardReturnAcd = 0u; moltenReturn = null; }
            // A different chosen elite owns combat now; do not return to an older elite later.
            else if (retained != null && retained.AcdId != moltenReturn.AcdId && IsAssistPositionElite(retained))
            { _assistHazardReturnAcd = 0u; moltenReturn = null; }
            if (retained != null && AssistTargetTier(retained) < 0) retained = null;
            if (moltenReturn != null && AssistTargetTier(moltenReturn) < 0)
            { _assistHazardReturnAcd = 0u; moltenReturn = null; }
            int aimX, aimY;
            if (retained != null && !TryGetAssistAim(retained, out aimX, out aimY))
            {
                _assistUiBlockedTargets = true; retained = null;
            }
            if (moltenReturn != null && !TryGetAssistAim(moltenReturn, out aimX, out aimY))
            { _assistUiBlockedTargets = true; _assistHazardReturnAcd = 0u; moltenReturn = null; }
            bool returning = moltenReturn != null && CanEngageAssistPosition(moltenReturn, canDash, now);
            if (returning) retained = moltenReturn;
            if (retained != null && !CanEngageAssistPosition(retained, canDash, now))
            {
                _assistHazardBlockedTargets = true;
                if (IsAssistPositionElite(retained) && _assistPositionReadable
                    && (_assistPositionMolten.Count > 0 || _assistPositionArcane.Count > 0
                        || _assistPositionOrbiter.Count > 0 || _assistPositionElectrified.Count > 0))
                { _assistHazardReturnAcd = retained.AcdId; moltenReturn = retained; }
                else if (AssistMoltenEscapeDue()) moltenSuspended = retained;
                retained = null;
            }
            // Reconsider an isolated trash lock at a bounded native-tick cadence.
            // Dense/elite locks remain sticky; this never inserts an attack wait.
            if(retained!=null&&AssistTargetTier(retained)==(int)AssistPriority.Trash
                && retained.NormalizedXyDistanceToMe>NearbyTrashRange&&HasAssistNearbyTrash())retained=null;
            bool nearbyTrash=HasAssistNearbyTrash();
            if (retained != null && AssistTargetTier(retained) == (int)AssistPriority.Trash
                && !_assistNavigationPending
                && (_assistTrashRecheckGameTick == int.MinValue || Hud.Game.CurrentGameTick - _assistTrashRecheckGameTick >= 15)
                && AssistTrashClass(AssistTrashProfileFor(retained)) < 2)
            { retained = null; _assistTrashRecheckGameTick = Hud.Game.CurrentGameTick; }
            IMonster best = retained;
            int bestTier = retained != null ? AssistTargetTier(retained) : -1;
            bool bestFromCurrentDamageCircle = CanAttackFromCurrentDamageCircle(best);
            foreach (var monster in _assistCandidates)
            {
                // Suspend an unsafe elite, not all combat. Safe minions/trash can fill
                // the danger window; the stored elite regains priority when safe.
                int tier = AssistTargetTier(monster);
                if(tier==(int)AssistPriority.Trash&&nearbyTrash&&monster.NormalizedXyDistanceToMe>NearbyTrashRange)continue;
                if (tier < 0) continue;
                if (tier < bestTier) continue;
                bool fromCurrentDamageCircle = CanAttackFromCurrentDamageCircle(monster);
                bool circleUpgrade = tier == bestTier && fromCurrentDamageCircle && !bestFromCurrentDamageCircle
                    && (retained == null || tier != (int)AssistPriority.Trash
                        || IsAssistCircleCandidateCompatible(monster, retained));
                bool blockedUpgrade = tier == bestTier && retained != null
                    && (!_assistNavigationPending || _assistNavigationReacquired)
                    && IsAssistTargetTemporarilyBlocked(retained) && !IsAssistTargetTemporarilyBlocked(monster);
                circleUpgrade |= blockedUpgrade;
                // Within the established elite tier, preserve the user's useful damage
                // circle before sticky-target preference, distance or alignment geometry.
                if (tier == bestTier && retained != null && !circleUpgrade) continue;
                if (tier == bestTier && tier != (int)AssistPriority.Trash
                    && bestFromCurrentDamageCircle && !fromCurrentDamageCircle) continue;
                if (tier == bestTier && !circleUpgrade && tier != 2 && best != null
                    && (monster.NormalizedXyDistanceToMe > best.NormalizedXyDistanceToMe
                        || (monster.NormalizedXyDistanceToMe == best.NormalizedXyDistanceToMe
                            && monster.AcdId >= best.AcdId))) continue;
                if (!TryGetAssistAim(monster, out aimX, out aimY))
                { _assistUiBlockedTargets = true; continue; }
                if (!CanEngageAssistPosition(monster, canDash, now))
                { _assistHazardBlockedTargets = true; continue; }
                if (tier > bestTier)
                {
                    bestTier = tier; best = null; retained = null;
                    bestFromCurrentDamageCircle = false;
                    _assistAcquisitionCandidates.Clear();
                }
                if (tier == 2) _assistAcquisitionCandidates.Add(monster);
                if (best == null || circleUpgrade
                    || monster.NormalizedXyDistanceToMe < best.NormalizedXyDistanceToMe
                    || (monster.NormalizedXyDistanceToMe == best.NormalizedXyDistanceToMe && monster.AcdId < best.AcdId))
                {
                    best = monster; bestFromCurrentDamageCircle = fromCurrentDamageCircle;
                    if (circleUpgrade) retained = null;
                }
            }
            if (best != null && moltenReturn != null)
            {
                _assistHazardReturnAcd = 0u;
                if (best.AcdId == moltenReturn.AcdId && returning)
                    return RecordAssistSelection(best, bestTier, "hazard-return");
            }
            bool allowFan = _assistTrashFanReady && _assistTargetAcd != 0u
                && _assistLastTrashAttackAcd == _assistTargetAcd;
            if (bestTier == (int)AssistPriority.Trash && retained == null && best != null)
                best = PickAssistTrashTarget(allowFan); // Pack choice precedes circle retargeting.
            if (canDash && best != null && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue())
            {
                var circleTarget = AssistCircleCombatTarget(best, bestTier);
                if (circleTarget != best) return RecordAssistSelection(circleTarget, bestTier, "damage-circle-target");
            }
            if (retained != null) return RecordAssistSelection(retained, bestTier,
                bestTier == (int)AssistPriority.CtrlFocus ? "ctrl-focus" : "retained");
            if (best == null && moltenReturn != null)
                return RecordAssistSelection(moltenReturn, AssistTargetTier(moltenReturn), "hazard-suspended");
            if (best == null && moltenSuspended != null)
                return RecordAssistSelection(moltenSuspended, AssistTargetTier(moltenSuspended), "hazard-suspended");
            if (bestTier != 2) return RecordAssistSelection(best, bestTier,
                bestFromCurrentDamageCircle ? "damage-circle-target" : ((AssistPriority)bestTier).ToString());
            return best;
        }

        private static bool AssistPositionFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsAssistPositionWorldPoint(IWorldCoordinate point)
        {
            return point != null && point.IsValid && AssistPositionFinite(point.X)
                && AssistPositionFinite(point.Y) && AssistPositionFinite(point.Z);
        }

        private bool RefreshAssistPositionActors()
        {
            if (!_assistActive || !AssistRequested()) return false;
            int tick = Hud.Game.CurrentGameTick;
            uint world = Hud.Game.Me.WorldId;
            if (tick == _assistPositionGameTick && world == _assistPositionWorldId)
                return _assistPositionReadable;
            _assistPositionGameTick = tick;
            _assistPositionWorldId = world;
            _assistPositionReadable = false;
            _assistDoors.Clear();
            _assistPositionCircles.Clear();
            _assistPositionMolten.Clear();
            _assistPositionArcane.Clear();
            _assistPositionOrbiter.Clear();
            _assistPositionElectrified.Clear();
            _assistOrbiterNearestActor = null;
            _assistOrbiterCoreUnsafe = _assistElectrifiedUnsafe = false;
            _assistPositionArcaneCount = 0;
            _assistArcaneCoreUnsafe = false;
            _assistArcaneNearestActor = null;
            _assistArcaneNearestSno = _assistArcaneNearestAcd = 0u;
            _assistArcaneNearestDistance = double.NaN;
            _assistArcaneCollisionDeltaX = _assistArcaneCollisionDeltaY = float.NaN;
            _assistPositionCircleCount = _assistPositionHazardCount = 0;
            try
            {
                var me = Hud.Game.Me.FloorCoordinate;
                if (!IsAssistPositionWorldPoint(me) || Hud.Game.Actors == null) return false;
                PruneAssistObstacles(me, tick, world);
                if (Hud.Game.Shrines != null)
                    foreach (var shrine in Hud.Game.Shrines)
                        if (shrine != null && shrine.IsPylon && !shrine.IsDisabled
                            && shrine.WorldId == world && shrine.SnoActor != null
                            && IsAssistPositionWorldPoint(shrine.FloorCoordinate)) RememberAssistObstacle(shrine, tick);
                foreach (var actor in Hud.Game.Actors)
                {
                    if (actor == null || actor.SnoActor == null || actor.IsDisabled
                        || actor.WorldId != world || !IsAssistPositionWorldPoint(actor.FloorCoordinate)
                        || me.XYDistanceTo(actor.FloorCoordinate) > AssistPositionScanRange) continue;
                    RememberAssistObstacle(actor, tick);
                    var sno = actor.SnoActor.Sno;
                    if (_assistDoors.Count < 32 && IsAssistClosedDoor(actor)) _assistDoors.Add(actor);
                    if (sno == ActorSnoEnum._monsteraffix_arcaneenchanted_petsweep
                        || sno == ActorSnoEnum._monsteraffix_arcaneenchanted_petsweep_reverse
                        || sno == ActorSnoEnum._arcaneenchanteddummy_spawn
                        || sno == ActorSnoEnum._monsteraffix_avenger_arcaneenchanted_petsweep
                        || sno == ActorSnoEnum._monsteraffix_avenger_arcaneenchanted_petsweep_reverse
                        || sno == ActorSnoEnum._x1_monsteraffix_avenger_arcaneenchanted_dummyspawn)
                    {
                        if (_assistPositionArcane.Count >= AssistArcaneCacheLimit) return false;
                        _assistPositionArcane.Add(actor);
                        _assistPositionArcaneCount = _assistPositionArcane.Count;
                        continue;
                    }
                    if (sno == ActorSnoEnum._x1_monsteraffix_orbiter_focalpoint
                        || sno == ActorSnoEnum._x1_monsteraffix_orbiter_projectile_focus
                        || sno == ActorSnoEnum._x1_monsteraffix_avenger_orbiter_focalpoint
                        || sno == ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile_focus)
                    {
                        if (_assistPositionOrbiter.Count >= AssistOrbiterCacheLimit) return false;
                        _assistPositionOrbiter.Add(actor);
                        double orbiterDistance = me.XYDistanceTo(actor.FloorCoordinate);
                        if (!AssistSameHeight(me, actor.FloorCoordinate)) continue;
                        if (orbiterDistance <= AssistOrbiterRadius(actor)) _assistOrbiterCoreUnsafe = true;
                        if (_assistOrbiterNearestActor == null
                            || orbiterDistance < me.XYDistanceTo(_assistOrbiterNearestActor.FloorCoordinate))
                            _assistOrbiterNearestActor = actor;
                        continue;
                    }
                    bool buildup = sno == ActorSnoEnum._monsteraffix_molten_deathstart_proxy;
                    if (buildup || sno == ActorSnoEnum._monsteraffix_molten_deathexplosion_proxy
                        || sno == ActorSnoEnum._monsteraffix_molten_firering)
                    {
                        // An overflow cannot silently drop a hazard from destination checks.
                        if (_assistPositionMolten.Count >= AssistMoltenCacheLimit) return false;
                        _assistPositionMolten.Add(new AssistPositionMolten { Actor = actor, CountingDown = buildup });
                        _assistPositionHazardCount = _assistPositionMolten.Count;
                        continue;
                    }
                    // Circle/door opportunity retains its existing current-floor contract.
                    if (!AssistSameHeight(me, actor.FloorCoordinate)) continue;
                    int kind = -1;
                    if (sno == ActorSnoEnum._generic_proxy)
                    {
                        if (actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None,
                            Hud.Sno.SnoPowers.OculusRing.Sno, 0) == 1) kind = 0;
                        else if (actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 488071u, 0) == 1) kind = 1;
                        else if (actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_None, 488071u, 0) == 1) kind = 2;
                    }
                    else if (sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_purple
                        || sno == ActorSnoEnum._p75_itempassive_unique_ring_017_dome_purple_red) kind = 1;
                    else if (sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_blue) kind = 2;
                    if (kind >= 0 && _assistPositionCircles.Count < AssistCircleCacheLimit)
                    {
                        _assistPositionCircles.Add(new AssistPositionCircle { Actor = actor, Kind = kind });
                        _assistPositionCircleCount = _assistPositionCircles.Count;
                    }
                }
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster == null || (!monster.IsElite && monster.Rarity != ActorRarity.RareMinion)
                        || monster.WorldId != world || !IsAssistPositionWorldPoint(monster.FloorCoordinate)
                        || me.XYDistanceTo(monster.FloorCoordinate) > AssistAffixScanRange
                        || !HasAssistAffix(monster, MonsterAffix.Electrified)) continue;
                    if (_assistPositionElectrified.Count >= 32) return false;
                    _assistPositionElectrified.Add(monster);
                    if (AssistSameHeight(me, monster.FloorCoordinate)
                        && me.XYDistanceTo(monster.FloorCoordinate) < monster.RadiusScaled + AssistElectrifiedClearance)
                        _assistElectrifiedUnsafe = true;
                }
                PruneAssistObstacles(me, tick, world);
                if (!RefreshAssistCorpseExplosions(me, world, tick)) return false;
                CaptureAssistArcaneState(me);
                ObserveAssistDamageEscape(me, tick, world);
                _assistPositionReadable = true;
                return true;
            }
            catch { return false; }
        }

        private static bool IsAssistGrotesqueSno(ActorSnoEnum sno)
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

        private bool RefreshAssistCorpseExplosions(IWorldCoordinate me, uint world, int tick)
        {
            // Small accelerated-clock rollbacks must not restart an observed death fuse.
            if (world != _assistCorpseWorld || (long)_assistCorpseTick - tick > 60L)
            { _assistCorpseObservations.Clear(); _assistSpacingAttemptAcd = 0u; }
            _assistCorpseWorld = world; _assistCorpseTick = tick;
            _assistCorpsePrune.Clear();
            foreach (var pair in _assistCorpseObservations)
                if (tick - pair.Value.LastSeenTick > 600) _assistCorpsePrune.Add(pair.Key);
            foreach (uint id in _assistCorpsePrune) _assistCorpseObservations.Remove(id);
            foreach (var monster in Hud.Game.Monsters)
            {
                if (monster == null || monster.Illusion || monster.WorldId != world || monster.SnoActor == null
                    || !IsAssistGrotesqueSno(monster.SnoActor.Sno)
                    || !IsAssistPositionWorldPoint(monster.FloorCoordinate)
                    || me.XYDistanceTo(monster.FloorCoordinate) > AssistPositionScanRange) continue;
                AssistCorpseObservation entry;
                if (!_assistCorpseObservations.TryGetValue(monster.AcdId, out entry))
                {
                    if (_assistCorpseObservations.Count >= 128) return false;
                    entry = new AssistCorpseObservation { LastSeenTick = int.MinValue };
                    _assistCorpseObservations.Add(monster.AcdId, entry);
                }
                if (monster.IsAlive)
                { entry.DeathTick = int.MinValue; entry.Position = null; }
                if (!monster.IsAlive && entry.DeathTick == int.MinValue)
                {
                    // Grotesque has its own short death warning: dodge at 800 ms.
                    // Do not inherit Molten's three-second fuse or its smaller radius.
                    entry.DeathTick = tick;
                    entry.EndTick = tick + AssistGrotesqueFuseTicks;
                    entry.Position = monster.FloorCoordinate.Offset(0f, 0f, 0f);
                }
                entry.Actor = monster; entry.LastSeenTick = tick;
            }
            foreach (var entry in _assistCorpseObservations.Values)
            {
                if (entry.DeathTick == int.MinValue || tick > entry.EndTick) continue;
                if (_assistPositionMolten.Count >= AssistMoltenCacheLimit) return false;
                _assistPositionMolten.Add(new AssistPositionMolten { Actor = entry.Actor,
                    Position = entry.Position, CountingDown = true, Grotesque = true,
                    StartTick = entry.DeathTick, EndTick = entry.EndTick });
            }
            _assistPositionHazardCount = _assistPositionMolten.Count;
            return true;
        }

        // One family-specific clearance serves triggers, landings and follow checks.
        // Never shrink one explosion's buffer by changing the other's blast radius.
        private static float AssistExplosionClearance(AssistPositionMolten hazard)
        {
            return hazard.Grotesque ? AssistGrotesqueAvoidanceRadius : AssistMoltenAvoidanceRadius;
        }

        private bool IsAssistMoltenBlocking(AssistPositionMolten hazard)
        {
            if (hazard.Grotesque)
                return Hud.Game.CurrentGameTick - hazard.StartTick >= AssistGrotesqueDodgeTicks
                    && Hud.Game.CurrentGameTick <= hazard.EndTick;
            if (hazard.Actor == null || hazard.Actor.IsDisabled) return false;
            if (!hazard.CountingDown) return true;
            // Native molten buildup is three seconds at 60 game ticks per second.
            double remaining = 3.0 - (Hud.Game.CurrentGameTick - hazard.Actor.CreatedAtInGameTick) / 60.0;
            return remaining <= AssistMoltenEscapeSeconds;
        }

        // Acquisition, attack height and Dash travel are separate contracts. An approach
        // may cross floors; hazards/physical footprints belong to the queried floor.
        private static bool AssistSameHeight(IWorldCoordinate a, IWorldCoordinate b)
        { return a != null && b != null && Math.Abs(a.Z - b.Z) <= 8f; }

        private struct AssistObstacle
        { public uint Acd, World; public float X, Y, Z, Radius; public int SeenTick; }
        private readonly List<AssistObstacle> _assistObstacles = new List<AssistObstacle>(32);
        private uint _assistObstacleRejectedAcd;

        private void RememberAssistObstacle(IActor actor, int tick)
        {
            var shrine = actor as IShrine;
            string code = actor.SnoActor.Code;
            bool chest = (actor.GizmoType == GizmoType.Chest || actor.GizmoType == GizmoType.BreakableChest)
                && actor.SnoActor.Kind != ActorKind.DeadBody
                && (code == null || code.IndexOf("corpse", StringComparison.OrdinalIgnoreCase) < 0);
            if (!chest && (shrine == null || !shrine.IsPylon)) return;
            var point = actor.FloorCoordinate;
            if (point.XYDistanceTo(Hud.Game.Me.FloorCoordinate) > 60f) return;
            float radius = actor.RadiusBottom > 0f ? actor.RadiusBottom : actor.RadiusScaled;
            if (!AssistPositionFinite(radius) || radius <= 0f || radius > 20f) return;
            var obstacle = new AssistObstacle { Acd = actor.AcdId, World = actor.WorldId,
                X = point.X, Y = point.Y, Z = point.Z, Radius = radius, SeenTick = tick };
            int replace = -1;
            double farthest = -1;
            for (int i = 0; i < _assistObstacles.Count; i++)
            {
                if (_assistObstacles[i].Acd == actor.AcdId && _assistObstacles[i].World == actor.WorldId)
                { _assistObstacles[i] = obstacle; return; }
                double distance = Hud.Game.Me.FloorCoordinate.XYDistanceTo(_assistObstacles[i].X, _assistObstacles[i].Y);
                if (distance > farthest) { farthest = distance; replace = i; }
            }
            if (_assistObstacles.Count < 32) _assistObstacles.Add(obstacle);
            else if (replace >= 0 && point.XYDistanceTo(Hud.Game.Me.FloorCoordinate) < farthest)
                _assistObstacles[replace] = obstacle;
        }

        private void PruneAssistObstacles(IWorldCoordinate me, int tick, uint world)
        {
            for (int i = _assistObstacles.Count - 1; i >= 0; i--)
                if (_assistObstacles[i].World != world || tick < _assistObstacles[i].SeenTick
                    || tick - _assistObstacles[i].SeenTick > 600
                    || me.XYDistanceTo(_assistObstacles[i].X, _assistObstacles[i].Y) > 60f)
                    _assistObstacles.RemoveAt(i);
        }

        private bool IsAssistObstaclePointFree(IWorldCoordinate point)
        {
            _assistObstacleRejectedAcd = 0u;
            var me = Hud.Game.Me.FloorCoordinate;
            // A buff-refresh self Dash does not introduce a new collision endpoint.
            if (AssistSameHeight(point, me) && point.XYDistanceTo(me) <= 0.25f) return true;
            foreach (var obstacle in _assistObstacles)
            {
                if (obstacle.World != Hud.Game.Me.WorldId || Math.Abs(point.Z - obstacle.Z) > 8f) continue;
                if (point.XYDistanceTo(obstacle.X, obstacle.Y) > obstacle.Radius + Math.Max(0f, Hud.Game.Me.RadiusScaled)) continue;
                _assistObstacleRejectedAcd = obstacle.Acd;
                return false;
            }
            return true;
        }

        private bool IsAssistPositionHazardFree(IWorldCoordinate point)
        {
            if (!_assistPositionReadable || !IsAssistPositionWorldPoint(point)
                || !IsAssistArcanePointCoreFree(point)) return false;
            foreach (var core in _assistPositionOrbiter)
                if (core != null && !core.IsDisabled && AssistSameHeight(point, core.FloorCoordinate) && point.XYDistanceTo(core.FloorCoordinate) <= AssistOrbiterRadius(core)) return false;
            foreach (var hazard in _assistPositionMolten)
                if (IsAssistMoltenBlocking(hazard) && AssistSameHeight(point, hazard.Center)
                    && point.XYDistanceTo(hazard.Center) <= AssistExplosionClearance(hazard))
                    return false;
            return true;
        }

        // Arcane exposes core actors in FreeHUD, but no verified native beam angle.
        // Core safety is conservative; collision deltas are diagnostics, not an orientation.
        private void CaptureAssistArcaneState(IWorldCoordinate me)
        {
            _assistArcaneNearestActor = null;
            _assistArcaneNearestSno = _assistArcaneNearestAcd = 0u;
            _assistArcaneNearestDistance = double.NaN;
            _assistArcaneCollisionDeltaX = _assistArcaneCollisionDeltaY = float.NaN;
            _assistArcaneCoreUnsafe = false;
            double bestDistance = double.MaxValue;
            foreach (var actor in _assistPositionArcane)
            {
                if (actor == null || actor.IsDisabled || !AssistSameHeight(me, actor.FloorCoordinate)) continue;
                double distance = me.XYDistanceTo(actor.FloorCoordinate);
                if (distance <= AssistArcaneCoreAvoidanceRadius) _assistArcaneCoreUnsafe = true;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                _assistArcaneNearestActor = actor;
                _assistArcaneNearestSno = (uint)actor.SnoActor.Sno;
                _assistArcaneNearestAcd = actor.AcdId;
                _assistArcaneNearestDistance = distance;
                var collision = actor.CollisionCoordinate;
                if (IsAssistPositionWorldPoint(collision))
                {
                    _assistArcaneCollisionDeltaX = collision.X - actor.FloorCoordinate.X;
                    _assistArcaneCollisionDeltaY = collision.Y - actor.FloorCoordinate.Y;
                }
                else _assistArcaneCollisionDeltaX = _assistArcaneCollisionDeltaY = float.NaN;
            }
        }

        private bool IsAssistArcanePointCoreFree(IWorldCoordinate point)
        {
            if (!IsAssistPositionWorldPoint(point)) return false;
            foreach (var actor in _assistPositionArcane)
                if (actor != null && !actor.IsDisabled && AssistSameHeight(point, actor.FloorCoordinate)
                    && point.XYDistanceTo(actor.FloorCoordinate) <= AssistArcaneCoreAvoidanceRadius)
                    return false;
            return true;
        }

        private bool AssistElectrifiedSpacingDue()
        {
            // Six-yard proximity is observed at any health; only low health may interrupt attacks.
            return _assistHealthFraction < AssistElectrifiedRepositionHealth && _assistElectrifiedUnsafe;
        }

        private float AssistElectrifiedClearanceAt(IWorldCoordinate point, IMonster target)
        {
            // Evaluate the proposed point, not the hero's current position. This permits
            // entry from outside a damage circle and uses the same rule after landing.
            // Priority 2 means a useful Oculus/purple circle with the target in attack reach.
            string reason;
            return _assistHealthFraction >= AssistElectrifiedRepositionHealth
                && target != null && target.IsAlive && target.WorldId == _assistPositionWorldId
                && AssistPositionCirclePriority(point, target, out reason) == 2
                ? AssistDamageCircleElectrifiedClearance : AssistElectrifiedClearance;
        }

        private bool IsAssistElectrifiedPointFree(IWorldCoordinate point, IMonster target)
        {
            float clearance = AssistElectrifiedClearanceAt(point, target);
            // Apply the one clearance to every nearby Electrified body, including minions.
            foreach (var monster in _assistPositionElectrified)
                if (monster != null && monster.IsAlive && AssistSameHeight(point, monster.FloorCoordinate)
                    && point.XYDistanceTo(monster.FloorCoordinate) < monster.RadiusScaled + clearance) return false;
            return true;
        }

        private float AssistOrbiterRadius(IActor actor)
        {
            float radius = actor != null && AssistPositionFinite(actor.RadiusScaled) ? Math.Max(0f, actor.RadiusScaled) : 0f;
            return Math.Max(AssistOrbiterMinimumCoreRadius, radius + Hud.Game.Me.RadiusScaled + 1.0f);
        }

        private static float AssistContactEdge(IMonster target)
        {
            return target.RadiusScaled + ((HasAssistAffix(target, MonsterAffix.Electrified)
                || HasAssistAffix(target, MonsterAffix.FireChains)) ? AssistElectrifiedLandingGap : 1.5f);
        }

        private bool AssistReactiveEscapeDue()
        {
            _assistArcaneEscapeReason = "none";
            if (!RefreshAssistPositionActors()) return false;
            if (!_assistArcaneCoreUnsafe && !_assistOrbiterCoreUnsafe && !_assistDamageEscapePending) return false;
            _assistArcaneEscapeReason = _assistArcaneCoreUnsafe ? "arcane-core" : _assistOrbiterCoreUnsafe ? "orbiter-core"
                : _assistDamageEscapeTrigger;
            return true;
        }

        private bool IsAssistArcaneNativeFollowCoreFree(IMonster target)
        {
            if (_assistPositionArcane.Count == 0) return true;
            var me = Hud.Game.Me.FloorCoordinate;
            if (!IsAssistArcanePointCoreFree(me)) return false;
            var end = target.FloorCoordinate;
            double dx = end.X - me.X, dy = end.Y - me.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= target.RadiusScaled + 2.0f) return true;
            double contactScale = Math.Max(0.0, distance - target.RadiusScaled - Math.Max(1.0, Math.Min(AssistAttackReachYards, MeleeAssistRange))) / distance;
            dx *= contactScale; dy *= contactScale;
            double lengthSquared = dx * dx + dy * dy;
            foreach (var actor in _assistPositionArcane)
            {
                if (actor == null || actor.IsDisabled) continue;
                var center = actor.FloorCoordinate;
                double along = lengthSquared > 0.0001
                    ? ((center.X - me.X) * dx + (center.Y - me.Y) * dy) / lengthSquared : 0;
                along = Math.Max(0.0, Math.Min(1.0, along));
                if (Math.Abs(center.Z - (me.Z + (target.FloorCoordinate.Z - me.Z) * contactScale * along)) > 8.0) continue;
                double hx = me.X + along * dx - center.X, hy = me.Y + along * dy - center.Y;
                if (hx * hx + hy * hy <= AssistArcaneCoreAvoidanceRadius * AssistArcaneCoreAvoidanceRadius)
                    return false;
            }
            return true;
        }

        private bool IsAssistOtherCoreFollowFree(IMonster target)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            if (!IsAssistPositionHazardFree(me)) return false;
            double dx = target.FloorCoordinate.X - me.X, dy = target.FloorCoordinate.Y - me.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double scale = distance > 0.001 ? Math.Max(0.0, distance - target.RadiusScaled - Math.Max(1.0, Math.Min(AssistAttackReachYards, MeleeAssistRange))) / distance : 0.0;
            dx *= scale; dy *= scale;
            double lengthSquared = dx * dx + dy * dy;
            foreach (var core in _assistPositionOrbiter)
            {
                if (core == null || core.IsDisabled) continue;
                var center = core.FloorCoordinate;
                double along = lengthSquared > 0.0001 ? ((center.X - me.X) * dx + (center.Y - me.Y) * dy) / lengthSquared : 0.0;
                along = Math.Max(0.0, Math.Min(1.0, along));
                if (Math.Abs(center.Z - (me.Z + (target.FloorCoordinate.Z - me.Z) * scale * along)) > 8.0) continue;
                double hx = me.X + along * dx - center.X, hy = me.Y + along * dy - center.Y;
                double radius = AssistOrbiterRadius(core);
                if (hx * hx + hy * hy <= radius * radius) return false;
            }
            // Electrical spacing must not veto native obstacle recovery.
            return true;
        }

        private double AssistArcanePositionScore(IWorldCoordinate point)
        {
            double score = 0;
            foreach (var actor in _assistPositionArcane)
            {
                if (actor == null || actor.IsDisabled || !AssistSameHeight(point, actor.FloorCoordinate)) continue;
                double distance = point.XYDistanceTo(actor.FloorCoordinate);
                // Prefer more clearance where contact permits. This is not a beam hit test.
                if (distance < 12.0) score -= (12.0 - distance) * 0.25;
            }
            return score;
        }

        private void ConsiderAssistReactiveContact(IWorldCoordinate candidate, IMonster target,
            ref IWorldCoordinate best, ref int bestPriority, ref double bestScore)
        {
            int x, y;
            if (!IsAssistDashPointSafe(candidate, target, out x, out y)) return;
            var me = Hud.Game.Me.FloorCoordinate;
            double travel = me.XYDistanceTo(candidate);
            if (travel < AssistDamageEscapeHopYards + 1.0f) return;
            string circleReason;
            int priority = AssistPositionCirclePriority(candidate, target, out circleReason);
            double clearance = 25.0;
            foreach (var core in _assistPositionArcane)
                if (core != null && !core.IsDisabled && AssistSameHeight(candidate, core.FloorCoordinate))
                    clearance = Math.Min(clearance, candidate.XYDistanceTo(core.FloorCoordinate) - AssistArcaneCoreAvoidanceRadius);
            foreach (var core in _assistPositionOrbiter)
                if (core != null && !core.IsDisabled && AssistSameHeight(candidate, core.FloorCoordinate))
                    clearance = Math.Min(clearance, candidate.XYDistanceTo(core.FloorCoordinate) - AssistOrbiterRadius(core));
            double fromX = me.X - target.FloorCoordinate.X, fromY = me.Y - target.FloorCoordinate.Y;
            double toX = candidate.X - target.FloorCoordinate.X, toY = candidate.Y - target.FloorCoordinate.Y;
            double lengths = Math.Sqrt((fromX * fromX + fromY * fromY) * (toX * toX + toY * toY));
            double opposite = lengths > 0.001 ? -(fromX * toX + fromY * toY) / lengths : 0.0;
            // Reactive-only ranking: useful circles, core clearance, then opposite-side
            // contact. No invented rotating-beam angle or change to ordinary Dash scoring.
            double score = clearance + opposite * 2.0 - travel * 0.025;
            if (best != null && (priority < bestPriority || (priority == bestPriority && score <= bestScore))) return;
            best = candidate; bestPriority = priority; bestScore = score;
        }

        private bool TryPickAssistReactiveEscapePoint(IMonster target,
            out IWorldCoordinate point, out string reason, out bool escapeOnly)
        {
            point = null; reason = "none"; escapeOnly = false;
            if (!RefreshAssistPositionActors()) return false;
            string contactReason;
            IWorldCoordinate contact;
            int bestPriority = -1;
            double contactScore = double.MinValue;
            if (target != null && IsAssistEligible(target))
            {
                if (TryPickAssistDashPoint(target, true, out contact, out contactReason))
                    ConsiderAssistReactiveContact(contact, target, ref point, ref bestPriority, ref contactScore);
                // Sample the outer reachable body edge as well as the existing contact
                // choice: a small core-free hop can still leave us beside the same beam.
                float edge = target.RadiusScaled + (float)Math.Max(1.5,
                    Math.Min(AssistAttackReachYards, MeleeAssistRange) - 1.0);
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistReactiveContact(target.FloorCoordinate.Offset(
                        AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target,
                        ref point, ref bestPriority, ref contactScore);
                if (point != null)
                { reason = _assistArcaneEscapeReason + ":reactive-contact"; return true; }
            }
            var me = Hud.Game.Me.FloorCoordinate;
            double bestScore = double.MaxValue;
            var nearest = _assistArcaneNearestActor;
            if (_assistOrbiterCoreUnsafe && _assistOrbiterNearestActor != null) nearest = _assistOrbiterNearestActor;
            float exitRadius = nearest != null && nearest == _assistOrbiterNearestActor
                ? AssistOrbiterRadius(nearest) + 1.5f : AssistArcaneCoreAvoidanceRadius + 1.5f;
            // Bounded exits: eight core-boundary points and sixteen surrounding points.
            for (int i = 0; i < AssistEdgeX.Length; i++)
            {
                if (nearest != null && !nearest.IsDisabled)
                    ConsiderAssistEscapePoint(nearest.FloorCoordinate.Offset(
                        AssistEdgeX[i] * exitRadius,
                        AssistEdgeY[i] * exitRadius, 0f),
                        target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f);
                ConsiderAssistEscapePoint(me.Offset(
                    AssistEdgeX[i] * Math.Max(exitRadius, AssistDamageEscapeHopYards + 1.5f),
                    AssistEdgeY[i] * Math.Max(exitRadius, AssistDamageEscapeHopYards + 1.5f), 0f),
                    target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f);
                ConsiderAssistEscapePoint(me.Offset(
                    AssistEdgeX[i] * (AssistArcaneCoreAvoidanceRadius * 2f + 2f),
                    AssistEdgeY[i] * (AssistArcaneCoreAvoidanceRadius * 2f + 2f), 0f),
                    target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f);
            }
            if (point == null) return false;
            escapeOnly = true; reason = _assistArcaneEscapeReason + ":exit";
            return true;
        }

        private bool AssistMoltenEscapeDue()
        {
            _assistPositionEscapeReason = "none";
            if (!RefreshAssistPositionActors()) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            foreach (var hazard in _assistPositionMolten)
            {
                if (!IsAssistMoltenBlocking(hazard) || !AssistSameHeight(me, hazard.Center)
                    || me.XYDistanceTo(hazard.Center) > AssistExplosionClearance(hazard)) continue;
                _assistPositionEscapeReason = hazard.Grotesque ? "grotesque-imminent"
                    : hazard.CountingDown ? "molten-imminent" : "active-explosion";
                return true;
            }
            return false;
        }

        private bool IsAssistDashWithinReach(IWorldCoordinate point)
        {
            return IsAssistPositionWorldPoint(point)
                && point.XYDistanceTo(Hud.Game.Me.FloorCoordinate) <= AssistDashTravelLimit;
        }

        private bool IsAssistDashPointSafe(IWorldCoordinate point, IMonster target, out int x, out int y,
            bool escapeOnly = false, bool mapTransit = false, bool planOnly = false)
        {
            x = y = 0;
            try
            {
                if (!RefreshAssistPositionActors() || !Hud.Window.IsForeground || !_clickUiReadable
                    || !IsAssistPositionWorldPoint(point) || (!planOnly && !IsAssistDashWithinReach(point))
                    || IsAssistEscapeLandingBlocked(point)
                    || IsAssistFailedLandingBlocked(point, target, escapeOnly || _assistDamageEscapePending
                        || AssistElectrifiedSpacingDue() || !IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate))
                    || (escapeOnly && !AssistSameHeight(point, Hud.Game.Me.FloorCoordinate))
                    || !IsAssistObstaclePointFree(point)
                    || (point.XYDistanceTo(Hud.Game.Me.FloorCoordinate)>0.25f&&!MapAssistLandingAllowed(point))
                    || !IsAssistPositionHazardFree(point) || !IsAssistElectrifiedPointFree(point, target)) return false;
                if (_assistDamageEscapePending)
                {
                    double hopX = point.X - _assistDamageEscapeOriginX, hopY = point.Y - _assistDamageEscapeOriginY;
                    // Plan one yard beyond the required actual hop, allowing landing tolerance.
                    float plannedHop = AssistDamageEscapeHopYards + 1.0f;
                    if (hopX * hopX + hopY * hopY < plannedHop * plannedHop) return false;
                }
                // A captured pure escape has no attack endpoint. Ordinary/contact Dash keeps
                // the retained target and its conservative contact requirement unchanged.
                if (!escapeOnly && !mapTransit && (!IsAssistEligible(target) || target.WorldId != _assistPositionWorldId
                    || !IsAssistPositionWorldPoint(target.FloorCoordinate)
                    || Math.Abs(point.Z - target.FloorCoordinate.Z) > 8f
                    || !AssistPositionFinite(target.RadiusScaled) || target.RadiusScaled < 0f
                    || !CanAttackAssistFrom(point, target)
                    || AssistBodyGap(point, target) < 1.0f || AssistChainDistance(point, target) < 3.0)) return false;
                if (!escapeOnly && _assistMainKey == ActionKey.LeftSkill
                    && (_pylonBlocksPlayer || IsNearProtectedPylon(point))) return false;
                var screen = point.ToScreenCoordinate(true, true);
                if (screen == null || !AssistPositionFinite(screen.X) || !AssistPositionFinite(screen.Y)) return false;
                double cx = Math.Round(screen.X), cy = Math.Round(screen.Y);
                if (cx < 0 || cy < 0 || cx >= Hud.Window.Size.Width || cy >= Hud.Window.Size.Height) return false;
                if (!escapeOnly && _assistMainKey == ActionKey.LeftSkill && !IsLeftClickPointSafe(cx, cy)) return false;
                if (_hudMenu != null && _hudMenu.IsAutomationLeftClickBlocked((float)cx, (float)cy)) return false;
                foreach (var rect in _clickUiRects)
                    if (cx >= rect.Left - 2 && cx <= rect.Right + 2
                        && cy >= rect.Top - 2 && cy <= rect.Bottom + 2) return false;
                long sx = (long)cx + Hud.Window.Offset.X, sy = (long)cy + Hud.Window.Offset.Y;
                if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
                x = (int)sx; y = (int)sy;
                return true;
            }
            catch { return false; }
        }

        private bool IsAssistNativeFollowHazardFree(IMonster target)
        {
            if (!RefreshAssistPositionActors() || !IsAssistEligible(target)) return false;
            var current = Hud.Game.Me.FloorCoordinate;
            // In attack range we use scoped standstill, so no walk through the target core is planned.
            if (IsAssistPositionHazardFree(current) && CanAttackAssistFrom(current, target)) return true;
            if (!IsAssistOtherCoreFollowFree(target) || !IsAssistArcaneNativeFollowCoreFree(target)) return false;
            if (_assistPositionMolten.Count == 0) return true;
            var me = Hud.Game.Me.FloorCoordinate;
            var end = target.FloorCoordinate;
            // Keep engaging during the native buildup until the requested final one-second escape margin.
            // Native active explosion/firering proxies remain authoritative after the countdown.
            double dx = end.X - me.X, dy = end.Y - me.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= target.RadiusScaled + 2.0f) return !AssistMoltenEscapeDue();
            // Check only the approach to near-side contact, not through the target's center.
            // This is a conservative straight segment, not a navigation/pathfinding API.
            double contactScale = Math.Max(0.0, distance - target.RadiusScaled - Math.Max(1.0, Math.Min(AssistAttackReachYards, MeleeAssistRange))) / distance;
            dx *= contactScale; dy *= contactScale;
            double lengthSquared = dx * dx + dy * dy;
            foreach (var hazard in _assistPositionMolten)
            {
                if (!IsAssistMoltenBlocking(hazard)) continue;
                var center = hazard.Center;
                double along = lengthSquared > 0.0001
                    ? ((center.X - me.X) * dx + (center.Y - me.Y) * dy) / lengthSquared : 0;
                along = Math.Max(0.0, Math.Min(1.0, along));
                if (Math.Abs(center.Z - (me.Z + (target.FloorCoordinate.Z - me.Z) * contactScale * along)) > 8.0) continue;
                double hx = me.X + along * dx - center.X, hy = me.Y + along * dy - center.Y;
                float clearance = AssistExplosionClearance(hazard);
                if (hx * hx + hy * hy <= clearance * clearance)
                    return false;
            }
            return true;
        }

        private bool CanEngageAssistPosition(IMonster target, bool canDash, int now)
        {
            if (!RefreshAssistPositionActors())
            {
                return false;
            }
            // Attacks and executable escape are independent. An unavailable dodge
            // never removes a reachable combat target; escape is dispatched first.
            if (CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)) return true;
            if ((_assistPositionMolten.Count == 0 && _assistPositionArcane.Count == 0
                && _assistPositionOrbiter.Count == 0)
                || IsAssistNativeFollowHazardFree(target)) return true;
            // A retained target with no safe approach must yield immediately. New targets
            // reset the old aim backoff; a failed retained Dash still keeps its recovery guard.
            if (!canDash)
            {
                return false;
            }
            if (target.AcdId == _assistTargetAcd
                && (_assistDashRetryNeedsAttack || !Due(now, _nextDashAimTick))
                && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue())
            {
                return false;
            }
            int x, y;
            if (IsAssistDashPointSafe(Hud.Game.Me.FloorCoordinate, target, out x, out y)
                || IsAssistDashPointSafe(target.FloorCoordinate, target, out x, out y)) return true;
            float edge = AssistContactEdge(target);
            for (int i = 0; i < AssistEdgeX.Length; i++)
                if (IsAssistDashPointSafe(target.FloorCoordinate.Offset(
                    AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target, out x, out y)) return true;
            return false;
        }

        private int AssistPositionCirclePriority(IWorldCoordinate point, IMonster target, out string reason)
        {
            int priority = 0;
            reason = null;
            if (!CanAttackAssistFrom(point, target) || !IsAssistUsefulCircleTarget(target)) return 0;
            foreach (var circle in _assistPositionCircles)
            {
                if (circle.Actor == null || circle.Actor.IsDisabled
                    || (circle.Kind == 2 && !IsAssistPositionElite(target))) continue;
                var center = circle.Actor.FloorCoordinate;
                if (Math.Abs(center.Z - point.Z) > 8f
                    || center.XYDistanceTo(point) > AssistCircleCoverageRadius) continue;
                int candidate = circle.Kind == 2 ? 1 : 2;
                if (candidate <= priority) continue;
                priority = candidate;
                reason = circle.Kind == 0 ? "oculus" : circle.Kind == 1 ? "triune-damage" : "triune-cdr";
            }
            return priority;
        }

        private static bool IsAssistPositionElite(IMonster monster)
        {
            // Position semantics stay independent of the target selector's focus/Jugger tiers.
            return monster != null && (monster.Rarity == ActorRarity.Boss || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique
                || (monster.IsElite && monster.Rarity != ActorRarity.RareMinion));
        }

        private double AssistChainDistance(IWorldCoordinate point, IMonster target)
        {
            if (target == null || target.Pack == null || !HasAssistAffix(target, MonsterAffix.FireChains)
                || target.Pack.MonstersAlive == null) return double.PositiveInfinity;
            var start = target.FloorCoordinate;
            double nearest = double.PositiveInfinity;
            int count = 0;
            foreach (var partner in target.Pack.MonstersAlive)
            {
                if (++count > 16) break;
                if (!IsAssistMonsterValid(partner) || partner.AcdId == target.AcdId
                    || partner.WorldId != target.WorldId || !IsAssistLeader(partner)
                    || !IsAssistPositionWorldPoint(partner.FloorCoordinate)) continue;
                var end = partner.FloorCoordinate;
                double dx = end.X - start.X, dy = end.Y - start.Y;
                double length = dx * dx + dy * dy;
                double along = length > 0.0001 ? ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length : 0;
                along = Math.Max(0.0, Math.Min(1.0, along));
                double px = point.X - start.X - along * dx, py = point.Y - start.Y - along * dy;
                nearest = Math.Min(nearest, Math.Sqrt(px * px + py * py));
            }
            return nearest; // Exposed pack endpoints, not a verified beam width or navmesh.
        }

        private double AssistPackPositionScore(IWorldCoordinate point, IMonster target)
        {
            double score = 0;
            if (HasAssistAffix(target, MonsterAffix.Electrified))
                score -= Math.Abs(AssistBodyGap(point, target) - AssistElectrifiedLandingGap) * 2.0;
            if (!IsAssistPositionElite(target)) return score;
            double x = 0, y = 0;
            int count = 0;
            foreach (var monster in _assistCandidates)
            {
                if (!IsAssistPositionElite(monster) || !AssistSameHeight(point, monster.FloorCoordinate)
                    || monster.FloorCoordinate.XYDistanceTo(target.FloorCoordinate) > 22f) continue;
                x += monster.FloorCoordinate.X; y += monster.FloorCoordinate.Y;
                if (++count >= 16) break;
            }
            if (count > 1)
            {
                double dx = point.X - x / count, dy = point.Y - y / count;
                score -= Math.Sqrt(dx * dx + dy * dy) * 0.15;
            }
            return score; // Rank safe lateral/pack points; never override a useful damage circle.
        }

        private double AssistPositionGeometryScore(IWorldCoordinate point, IMonster target)
        {
            double aimX = target.FloorCoordinate.X - point.X, aimY = target.FloorCoordinate.Y - point.Y;
            double aimLength = Math.Sqrt(aimX * aimX + aimY * aimY);
            if (aimLength < 0.01) return -1.0;
            aimX /= aimLength; aimY /= aimLength;
            bool eliteFocus = IsAssistPositionElite(target);
            double score = 0;
            foreach (var monster in _assistCandidates)
            {
                // Candidates already passed projected visibility; do not reproject the
                // entire crowd for every contact sample in the bounded landing planner.
                if (!IsAssistMonsterValid(monster) || monster.WorldId != _assistPositionWorldId
                    || !IsAssistPositionWorldPoint(monster.FloorCoordinate)
                    || !AssistPositionFinite(monster.RadiusScaled) || monster.RadiusScaled < 0f) continue;
                double dx = monster.FloorCoordinate.X - point.X, dy = monster.FloorCoordinate.Y - point.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (!CanAttackAssistFrom(point, monster)) continue;
                double forward = dx * aimX + dy * aimY;
                double lateral = Math.Abs(dx * aimY - dy * aimX);
                bool elite = IsAssistPositionElite(monster);
                // A short widening wedge is an alignment heuristic, not a native cone angle.
                if (monster.AcdId != target.AcdId && forward > 0
                    && lateral <= monster.RadiusScaled + 1.5 + forward * 0.4
                    && (!eliteFocus || elite)) score += elite ? 3.0 : 1.0;
                // Prefer looking into the group from its edge over landing among surrounding mobs.
                if (distance <= monster.RadiusScaled + 3.0 && forward <= 0) score -= elite ? 3.0 : 1.5;
                if (distance <= monster.RadiusScaled + 1.0) score -= 0.5;
            }
            return score;
        }

        private void ConsiderAssistDashPoint(IWorldCoordinate point, IMonster target, int priority,
            double preference, string reason, ref IWorldCoordinate best, ref int bestPriority,
            ref double bestScore, ref string bestReason, bool planOnly = false)
        {
            int x, y;
            if (!IsAssistDashPointSafe(point, target, out x, out y, false, false, planOnly)) return;
            string circleReason;
            int circlePriority = AssistPositionCirclePriority(point, target, out circleReason);
            if (circlePriority > priority) { priority = circlePriority; reason = circleReason; }
            double score = preference + AssistPositionGeometryScore(point, target)
                + AssistPackPositionScore(point, target) + AssistArcanePositionScore(point)
                - Hud.Game.Me.FloorCoordinate.XYDistanceTo(point) * 0.025;
            if (AssistElectrifiedSpacingDue() && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target))
            {
                var me = Hud.Game.Me.FloorCoordinate;
                double ax = me.X - target.FloorCoordinate.X, ay = me.Y - target.FloorCoordinate.Y;
                double bx = point.X - target.FloorCoordinate.X, by = point.Y - target.FloorCoordinate.Y;
                double dot = ax * bx + ay * by;
                if (dot <= 0) score += 2.0; // Behind the enemy, toward the pack rather than retreat.
                else if (Math.Abs(ax * by - ay * bx) > dot) score += 1.0; // Lateral alternative.
            }
            if (best != null && (priority < bestPriority || (priority == bestPriority && score <= bestScore))) return;
            best = point; bestPriority = priority; bestScore = score; bestReason = reason;
        }

        private bool TryPickAssistDashPoint(IMonster target, bool hazardEscape,
            out IWorldCoordinate best, out string reason, bool planOnly = false)
        {
            best = null; reason = "none";
            if (!RefreshAssistPositionActors() || !IsAssistEligible(target)
                || !AssistPositionFinite(target.RadiusScaled) || target.RadiusScaled < 0f) return false;
            int bestPriority = -1;
            double bestScore = double.MinValue;
            var targetPoint = target.FloorCoordinate;
            var currentPoint = Hud.Game.Me.FloorCoordinate;
            int currentX, currentY;
            bool currentSafe = IsAssistDashPointSafe(currentPoint, target, out currentX, out currentY);
            string currentCircleReason;
            int currentPriority = currentSafe
                ? AssistPositionCirclePriority(currentPoint, target, out currentCircleReason) : 0;
            ConsiderAssistDashPoint(currentPoint, target, 0, 1.0, "current-contact",
                ref best, ref bestPriority, ref bestScore, ref reason, planOnly);
            foreach (var circle in _assistPositionCircles)
            {
                if (circle.Actor == null || circle.Actor.IsDisabled || IsAssistCircleBlocked(circle)
                    || !IsAssistUsefulCircleTarget(target)
                    || (circle.Kind == 2 && !IsAssistPositionElite(target))) continue;
                var center = circle.Actor.FloorCoordinate;
                double dx = targetPoint.X - center.X, dy = targetPoint.Y - center.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double scale = length > AssistCircleLandingRadius ? AssistCircleLandingRadius / length : 1.0;
                var point = center.Offset((float)(dx * scale), (float)(dy * scale), 0f);
                if (!IsAssistCircleWithinReach(point, target)) continue;
                // Actor presence is authoritative; the estimated seven-second age only ranks freshness.
                double age = Math.Max(0.0, (Hud.Game.CurrentGameTick - circle.Actor.CreatedAtInGameTick) / 60.0);
                double freshness = Math.Max(0.0, 7.0 - age) * 0.01;
                ConsiderAssistDashPoint(point, target, circle.Kind == 2 ? 1 : 2, freshness,
                    circle.Kind == 0 ? "oculus" : circle.Kind == 1 ? "triune-damage" : "triune-cdr",
                    ref best, ref bestPriority, ref bestScore, ref reason, planOnly);
            }
            // Eight fixed contact points suffice; keep ownership on the retained target.
            float edge = AssistContactEdge(target);
            for (int i = 0; i < AssistEdgeX.Length; i++)
                ConsiderAssistDashPoint(targetPoint.Offset(AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f),
                    target, 0, 1.0, IsAssistPositionElite(target) ? "elite-edge" : "trash-edge",
                    ref best, ref bestPriority, ref bestScore, ref reason, planOnly);
            ConsiderAssistDashPoint(targetPoint.Offset(0f, 0f, 0f), target, 0, 0.0, "target",
                ref best, ref bestPriority, ref bestScore, ref reason, planOnly);
            if (best == null) return false;
            // Refresh in place when already in an equally useful circle at melee contact.
            // A better damage circle can still outrank blue CDR; hazards always outrank both.
            if (!hazardEscape && currentSafe && currentPriority > 0 && currentPriority >= bestPriority)
            { best = currentPoint.Offset(0f, 0f, 0f); reason = "keep-current-circle"; }
            if (hazardEscape) reason = "molten-escape:" + reason;
            return true;
        }

        private void ConsiderAssistEscapePoint(IWorldCoordinate point, IMonster target,
            ref IWorldCoordinate best, ref double bestScore, float minimumTravel = 0f)
        {
            int x, y;
            if (!IsAssistDashPointSafe(point, target, out x, out y, true)) return;
            double score = Hud.Game.Me.FloorCoordinate.XYDistanceTo(point);
            if (score < minimumTravel) return; // Reactive exits; ordinary molten retains zero.
            if (target != null && IsAssistPositionWorldPoint(target.FloorCoordinate))
                score += point.XYDistanceTo(target.FloorCoordinate) * 0.05;
            if (best != null && score >= bestScore) return;
            best = point; bestScore = score;
        }

        private bool TryPickAssistMoltenEscapePoint(IMonster target, out IWorldCoordinate best,
            out string reason, out bool escapeOnly)
        {
            escapeOnly = false;
            // Continue hitting the retained elite from outside the blast whenever contact exists.
            if (TryPickAssistDashPoint(target, true, out best, out reason)) return true;
            best = null; reason = "none";
            if (!AssistMoltenEscapeDue()) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            double bestScore = double.MaxValue;
            foreach (var hazard in _assistPositionMolten)
            {
                if (!IsAssistMoltenBlocking(hazard) || !AssistSameHeight(me, hazard.Center)
                    || me.XYDistanceTo(hazard.Center) > AssistExplosionClearance(hazard)) continue;
                // The small landing margin keeps rounded coordinates beyond the
                // exclusion boundary; all overlapping explosions are checked again.
                float edge = AssistExplosionClearance(hazard) + 1.0f;
                var center = hazard.Center;
                double dx = me.X - center.X, dy = me.Y - center.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 0.01)
                    ConsiderAssistEscapePoint(center.Offset(
                        (float)(dx * edge / distance), (float)(dy * edge / distance), 0f),
                        target, ref best, ref bestScore);
                // Deterministic boundary samples, bounded by the existing native actor cache.
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistEscapePoint(center.Offset(
                        AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target, ref best, ref bestScore);
            }
            if (best == null) return false;
            escapeOnly = true;
            reason = _assistPositionEscapeReason + ":pure-exit";
            return true;
        }

        private static bool IsAssistBridge(ActorSnoEnum sno)
        {
            return sno == ActorSnoEnum._x1_westm_bridge
                || sno == ActorSnoEnum._a3dun_keep_siegetowerdoor_a
                || sno == ActorSnoEnum._x1_westm_bridge_scoundrel
                || sno == ActorSnoEnum._a3dun_keep_bridge_icy
                || sno == ActorSnoEnum._a3dun_keep_bridge_switch
                || sno == ActorSnoEnum._a3dun_keep_bridge_switch_b;
        }

        private static bool IsAssistBreakableDoor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null || actor.SnoActor.Type != ActorType.Gizmo) return false;
            var sno = actor.SnoActor.Sno;
            return actor.GizmoType == GizmoType.BreakableDoor
                || sno == ActorSnoEnum._a3dun_keep_door_destructable
                || sno == ActorSnoEnum._p4_ruins_frost_breakable_door
                || sno == ActorSnoEnum._trdun_cath_wooddoor_a_barricaded
                || sno == ActorSnoEnum._a1dun_leor_jail_door_breakable_a
                || sno == ActorSnoEnum._p1_cesspools_door_breakable
                || sno == ActorSnoEnum._cemetary_gate_trout_wilderness_no_lock;
        }

        private bool IsAssistClosedDoor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null || actor.SnoActor.Type != ActorType.Gizmo
                || actor.IsDisabled || actor.IsOperated || !IsAssistVisiblePoint(actor.FloorCoordinate)
                || actor.WorldId != Hud.Game.Me.WorldId || !IsAssistPositionWorldPoint(actor.FloorCoordinate)
                || actor.ZDistanceToMeAbsolute > 8 || actor.SnoActor.Kind == ActorKind.Shrine
                || actor.SnoActor.Kind == ActorKind.Portal) return false;
            if (IsAssistBreakableDoor(actor)) return actor.Hitpoints > 0;
            string code = actor.SnoActor.Code;
            bool namedDoor = actor.GizmoType == GizmoType.Door && code != null
                && code.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0;
            return (namedDoor || IsAssistBridge(actor.SnoActor.Sno)) && actor.IsClickable
                && actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Door_Locked, 0u, 0) == 0;
        }

        private int AssistDoorFailureCount(IActor actor)
        {
            AssistDoorFailure failure;
            if (!_assistDoorFailures.TryGetValue(actor.AcdId, out failure)) return 0;
            var me = Hud.Game.Me.FloorCoordinate;
            var point = actor.FloorCoordinate;
            if (failure.World != Hud.Game.Me.WorldId
                || me.XYDistanceTo(failure.PlayerX, failure.PlayerY) >= 4f
                || point.XYDistanceTo(failure.DoorX, failure.DoorY) >= 2f)
            { _assistDoorFailures.Remove(actor.AcdId); return 0; }
            return failure.Count;
        }

        private void RecordAssistDoorFailure(IActor actor)
        {
            if (actor == null) return;
            int count = AssistDoorFailureCount(actor);
            if (_assistDoorFailures.Count >= 32 && !_assistDoorFailures.ContainsKey(actor.AcdId))
            {
                uint oldest = 0u;
                foreach (var key in _assistDoorFailures.Keys) { oldest = key; break; }
                _assistDoorFailures.Remove(oldest);
            }
            var me = Hud.Game.Me.FloorCoordinate;
            var point = actor.FloorCoordinate;
            _assistDoorFailures[actor.AcdId] = new AssistDoorFailure
            { Count = count + 1, PlayerX = me.X, PlayerY = me.Y,
                DoorX = point.X, DoorY = point.Y, World = Hud.Game.Me.WorldId };
        }

        private bool IsAssistDoorPathSafe(IActor actor)
        {
            if (!RefreshAssistPositionActors() || !IsAssistClosedDoor(actor)) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            var point = actor.FloorCoordinate;
            if (!IsAssistPositionHazardFree(me) || !IsAssistPositionHazardFree(point)
                || IsNearProtectedPylon(point)) return false;
            // Conservative samples guard this short approach; they are not pathfinding.
            for (int i = 1; i < 4; i++)
                if (!IsAssistPositionHazardFree(me.Offset(
                    (point.X - me.X) * i / 4f, (point.Y - me.Y) * i / 4f, 0f))) return false;
            return true;
        }

        private bool TryGetAssistDoorAim(IActor actor, out int x, out int y)
        {
            x = y = 0;
            if (!IsAssistDoorPathSafe(actor)) return false;
            var screen = actor.ScreenCoordinate;
            if (screen == null || !AssistPositionFinite(screen.X) || !AssistPositionFinite(screen.Y)) return false;
            double cx = Math.Round(screen.X), cy = Math.Round(screen.Y);
            if (!IsLeftClickPointSafe(cx, cy)) return false;
            long sx = (long)cx + Hud.Window.Offset.X, sy = (long)cy + Hud.Window.Offset.Y;
            if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
            x = (int)sx; y = (int)sy;
            return true;
        }

        private bool IsAssistDoorHoverAuthorized(IActor hovered)
        {
            return _assistDoorClickAuthorized && _assistActive && AssistRequested()
                && !s7o_GenMonkInput.IsStandstillDown() && hovered != null
                && _assistDoorTarget != null && hovered.AcdId == _assistDoorTarget.AcdId
                && hovered.IsSelected && IsAssistClosedDoor(hovered);
        }

        private void CancelAssistDoor()
        {
            if (_assistDoorTarget != null && _assistDoorPulseKey != ActionKey.Unknown
                && _pulse == _assistDoorPulseKey && !_pulseIsMaintenance) CancelTarget();
            _assistDoorTarget = null;
            _assistDoorStage = 0;
            _assistDoorPulseKey = ActionKey.Unknown;
            _assistDoorClickAuthorized = false;
            _assistDoorStatus = "none";
        }

        private bool UpdateAssistDoor(IMonster combatTarget, int now)
        {
            if (!_assistActive || !AssistRequested() || s7o_GenMonkInput.IsStandstillDown()
                || _selfDashKey != ActionKey.Unknown || s7o_InputReleaseArbiter.HasPendingRelease)
            { CancelAssistDoor(); return false; }
            if (_assistDoorTarget == null)
            {
                if (_pulse != ActionKey.Unknown || (s7o_GenMonkInput.IsDown(ActionKey.LeftSkill)
                    && !s7o_GenMonkInput.Owns(ActionKey.LeftSkill))
                    || (combatTarget != null && MainGeneratorAnimation()
                        && AttackedIdentityMatches(combatTarget.AcdId, combatTarget.AnnId))) return false;
                if (!RefreshAssistPositionActors()) return false;
                IActor best = null;
                var me = Hud.Game.Me.FloorCoordinate;
                foreach (var door in _assistDoors)
                {
                    if (!IsAssistClosedDoor(door) || me.XYDistanceTo(door.FloorCoordinate) > 12f
                        || AssistDoorFailureCount(door) >= 2) continue;
                    if (combatTarget != null)
                    {
                        var end = combatTarget.FloorCoordinate;
                        double dx = end.X - me.X, dy = end.Y - me.Y;
                        double lengthSquared = dx * dx + dy * dy;
                        if (lengthSquared < 4.0) continue;
                        double along = ((door.FloorCoordinate.X - me.X) * dx
                            + (door.FloorCoordinate.Y - me.Y) * dy) / lengthSquared;
                        if (door.AcdId!=_assistMapDoorAcd&&(along <= 0 || along >= 1)) continue;
                        double px = me.X + along * dx - door.FloorCoordinate.X;
                        double py = me.Y + along * dy - door.FloorCoordinate.Y;
                        double corridor = Math.Max(2.0, Math.Min(5.0, door.RadiusScaled + 1.5));
                        if (door.AcdId!=_assistMapDoorAcd&&px * px + py * py > corridor * corridor) continue;
                    }
                    int x, y;
                    if (!TryGetAssistDoorAim(door, out x, out y)) continue;
                    if (best == null || door.CentralXyDistanceToMe < best.CentralXyDistanceToMe) best = door;
                }
                if (best == null) return false;
                ReleaseAssistAttack();
                CancelTarget();
                _assistDoorTarget = best;
                _assistDoorStage = 1;
                _assistDoorSinceTick = now;
                _assistDoorBeforeHp = best.Hitpoints;
            }
            var target = _assistDoorTarget;
            if (!IsAssistClosedDoor(target))
            { _assistDoorFailures.Remove(target.AcdId); CancelAssistDoor(); return false; }
            if (_assistDoorStage == 2)
            {
                _assistDoorStatus = "door-click";
                if (_pulse != ActionKey.Unknown) return true;
                _assistDoorClickAuthorized = false;
                if (IsAssistBreakableDoor(target) && target.Hitpoints < _assistDoorBeforeHp)
                { _assistDoorFailures.Remove(target.AcdId); CancelAssistDoor(); return false; }
                if (ElapsedMs(now, _assistDoorSinceTick) < 250) return true;
                RecordAssistDoorFailure(target);
                CancelAssistDoor();
                return false;
            }
            if (ElapsedMs(now, _assistDoorSinceTick) >= 250)
            { RecordAssistDoorFailure(target); CancelAssistDoor(); return false; }
            int aimX, aimY;
            if (!TryGetAssistDoorAim(target, out aimX, out aimY))
            { CancelAssistDoor(); return false; }
            CursorPoint current;
            if (!GetCursorPos(out current)) { CancelAssistDoor(); return false; }
            if (_assistDoorStage == 3
                && (Math.Abs((long)current.X - _assistDoorAimX) > 12
                    || Math.Abs((long)current.Y - _assistDoorAimY) > 12))
            { CancelAssistDoor(); return false; }
            if (_assistDoorStage == 1 || _assistDoorAimX != aimX || _assistDoorAimY != aimY)
            {
                if (!SetCursorPos(aimX, aimY)) { CancelAssistDoor(); return false; }
                _assistDoorAimX = _assistAimX = aimX; _assistDoorAimY = _assistAimY = aimY;
                _assistCursorOwned = true;
                _assistDoorAimGameTick = Hud.Game.CurrentGameTick;
                _assistDoorStage = 3;
                _assistDoorStatus = _assistStatus = "door-aim";
                return true;
            }
            var hovered = Hud.Game.SelectedActor;
            if (Hud.Game.CurrentGameTick == _assistDoorAimGameTick || hovered == null
                || hovered.AcdId != target.AcdId || !hovered.IsSelected) return true;
            _assistDoorPulseKey = IsAssistBreakableDoor(target) ? _assistMainKey : ActionKey.LeftSkill;
            _assistDoorClickAuthorized = _assistDoorPulseKey == ActionKey.LeftSkill;
            if (!StartPulse(_assistDoorPulseKey, now, 50, false))
            { CancelAssistDoor(); return false; }
            _assistDoorStage = 2;
            _assistDoorSinceTick = now;
            _assistDoorStatus = _assistStatus = "door-click";
            return true;
        }

        private bool IsAssistCorpseOverlap()
        {
            if (!_assistActive || !AssistRequested() || _assistMainKey != ActionKey.LeftSkill
                || _assistTargetAcd == 0u || _assistDoorTarget != null) return false;
            var hovered = Hud.Game.SelectedActor;
            if (hovered == null || hovered.SnoActor == null) return false;
            // Some lootable corpse gizmos use Chest classification. Match their native
            // actor code only within chest gizmos; never broaden permission to other objects.
            string code = hovered.SnoActor.Code;
            bool corpse = hovered.SnoActor.Kind == ActorKind.DeadBody
                || ((hovered.GizmoType == GizmoType.Chest || hovered.GizmoType == GizmoType.BreakableChest)
                    && code != null && code.IndexOf("corpse", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!corpse) return false;
            var target = FindAssistCandidate(_assistTargetAcd);
            if (!IsAssistEligible(target) || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target))
                return false;
            CursorPoint cursor;
            return _assistCursorOwned && GetCursorPos(out cursor)
                && Math.Abs((long)cursor.X - _assistAimX) <= 12 && Math.Abs((long)cursor.Y - _assistAimY) <= 12;
        }

        private bool AssistWotHfHeartbeatEnabled()
        {
            if (!_assistActive || !AssistRequested()) return false;
            var powers = Hud.Game.Me.Powers;
            if (powers == null || powers.UsedSkills == null) return false;
            IPlayerSkill main = null;
            foreach (var skill in powers.UsedSkills)
                if (skill != null && skill.Key == _assistMainKey) { main = skill; break; }
            if (main == null || main.SnoPower == null
                || main.SnoPower.Sno != Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno) return false;
            var theme = powers.GetBuff(487708u);
            var attack = powers.GetBuff(487707u);
            return (theme != null && theme.Active) || (attack != null && attack.Active);
        }

        private void ObserveAssistWotHf()
        {
            int tick = Hud.Game.CurrentGameTick;
            if (tick == _assistWotHfObservationGameTick) return;
            _assistWotHfObservationGameTick = tick;
            if (!AssistWotHfHeartbeatEnabled() || _pulse != ActionKey.Unknown
                || _selfDashKey != ActionKey.Unknown || _assistAttackKey != _assistMainKey
                || !s7o_GenMonkInput.IsDown(_assistMainKey)
                || Hud.Game.Me.AnimationState != AcdAnimationState.Attacking) return;
            string animation = Hud.Game.Me.Animation.ToString();
            if (animation.IndexOf("rapidstrikes", StringComparison.OrdinalIgnoreCase) < 0
                || tick <= _assistWotHfResumeGameTick) return;
            // A fresh exact generator animation is a heartbeat proxy, not proof of 350 stacks.
            _assistWotHfLastGameTick = tick;
            _assistWotHfResumePending = false;
        }

        private double AssistBodyGap(IWorldCoordinate point, IMonster target)
        {
            if (!IsAssistPositionWorldPoint(point) || target == null
                || !IsAssistPositionWorldPoint(target.FloorCoordinate)
                || !AssistPositionFinite(target.RadiusScaled) || target.RadiusScaled < 0f)
                return double.PositiveInfinity;
            return Math.Max(0.0, point.XYDistanceTo(target.FloorCoordinate) - target.RadiusScaled);
        }

        private bool CanAttackAssistFrom(IWorldCoordinate point, IMonster target)
        {
            // RadiusScaled estimates the body edge; do not add an assumed player radius.
            return target != null && IsAssistPositionWorldPoint(point)
                && IsAssistPositionWorldPoint(target.FloorCoordinate)
                && Math.Abs(point.Z - target.FloorCoordinate.Z) <= 8f
                && AssistBodyGap(point, target) <= Math.Max(1.0, Math.Min(AssistAttackReachYards, MeleeAssistRange));
        }

        private bool HasAssistAttackContact(IMonster target)
        {
            return target != null && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking
                && AttackedIdentityMatches(target.AcdId, target.AnnId);
        }

        private bool IsAssistCircleWithinReach(IWorldCoordinate point, IMonster target)
        {
            if (!CanAttackAssistFrom(point, target)) return false;
            string reason;
            return AssistPositionCirclePriority(point, target, out reason) > 0;
        }

        private bool CanAttackFromCurrentDamageCircle(IMonster target)
        {
            if (!IsAssistEligible(target) || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                || !RefreshAssistPositionActors()) return false;
            // Evaluate where the hero actually stands, never an imagined circle-edge landing.
            string reason;
            return AssistPositionCirclePriority(Hud.Game.Me.FloorCoordinate, target, out reason) == 2;
        }

        private void ConsiderAssistCirclePoint(AssistPositionCircle circle, IWorldCoordinate point, IMonster target,
            ref IWorldCoordinate best, ref int bestPriority, ref double bestScore, ref string reason)
        {
            if (point.XYDistanceTo(circle.Actor.FloorCoordinate) > AssistCircleLandingRadius + 0.01f
                || point.XYDistanceTo(target.FloorCoordinate) < target.RadiusScaled + 1.0f
                || !IsAssistCircleWithinReach(point, target)) return;
            IWorldCoordinate before = best;
            ConsiderAssistDashPoint(point, target, circle.Kind == 2 ? 1 : 2, 0.0,
                circle.Kind == 0 ? "oculus" : circle.Kind == 1 ? "triune-damage" : "triune-cdr",
                ref best, ref bestPriority, ref bestScore, ref reason);
            if (best != before) _assistCircleOpportunityAcd = circle.Actor.AcdId;
        }

        private bool TryPickAssistCircleUpgrade(IMonster target, out IWorldCoordinate best, out string reason)
        {
            best = null; reason = "none";
            _assistCircleOpportunityAcd = 0u;
            if (_assistNavigationPending || !IsAssistEligible(target)
                || !IsAssistUsefulCircleTarget(target) || !RefreshAssistPositionActors()) return false;
            string currentReason;
            var current = Hud.Game.Me.FloorCoordinate;
            int currentPriority = IsAssistPositionHazardFree(current)
                ? AssistPositionCirclePriority(current, target, out currentReason) : 0;
            if (currentPriority >= 2) return false;
            double bestScore = double.MinValue;
            int bestPriority = -1;
            foreach (var circle in _assistPositionCircles)
            {
                if (circle.Actor == null || circle.Actor.IsDisabled || IsAssistCircleBlocked(circle)
                    || (circle.Kind == 2 && !IsAssistPositionElite(target))) continue;
                var center = circle.Actor.FloorCoordinate;
                double dx = target.FloorCoordinate.X - center.X, dy = target.FloorCoordinate.Y - center.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double scale = distance > AssistCircleLandingRadius ? AssistCircleLandingRadius / distance : 1.0;
                var point = center.Offset((float)(dx * scale), (float)(dy * scale), 0f);
                // Prefer the target-facing inner edge. Only probe alternatives if that
                // point is blocked or lies inside an Electrified/body exclusion.
                IWorldCoordinate before = best;
                ConsiderAssistCirclePoint(circle, point, target, ref best, ref bestPriority, ref bestScore, ref reason);
                if (best != before) continue;
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistCirclePoint(circle, center.Offset(AssistEdgeX[i] * AssistCircleLandingRadius,
                        AssistEdgeY[i] * AssistCircleLandingRadius, 0f), target,
                        ref best, ref bestPriority, ref bestScore, ref reason);
            }
            return best != null && bestPriority > currentPriority;
        }

        private bool ShouldHoldAssistCircle(IMonster target)
        {
            return _assistActive && AssistRequested() && !_assistNavigationPending
                && RefreshAssistPositionActors() && IsAssistEligible(target)
                && IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate)
                && IsAssistCircleWithinReach(Hud.Game.Me.FloorCoordinate, target);
        }

        private bool ShouldHoldAssistPosition(IMonster target)
        {
            // Standstill belongs to an in-range attack, never to native path recovery.
            return _assistActive && AssistRequested() && !_assistNavigationPending
                && IsAssistEligible(target) && RefreshAssistPositionActors()
                && IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate)
                && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target);
        }

        private void RefreshAssistEliteProbe(IMonster target)
        {
            if (target.AcdId != _assistProbeTargetAcd)
            {
                _assistProbeTargetAcd = target.AcdId; _assistProbePhase = 0;
                _assistProbeGameTick = Hud.Game.CurrentGameTick;
                _assistProbeLocked = _assistProbeExhausted = false;
                _assistRetargetTriedAcd = 0u;
                _assistRetargetAttempts = 0; _assistRetargetNextGameTick = int.MinValue;
            }
            if ((!IsAssistPositionElite(target) && !_assistNavigationPending)
                || (!_assistNavigationPending && !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target))) return;
            int tick = Hud.Game.CurrentGameTick;
            if (tick == _assistProbeGameTick || !_assistCursorOwned
                || (_assistNavigationPending && (_assistAimPending || tick == _assistAimGameTick))) return;
            _assistProbeGameTick = tick;
            CursorPoint cursor;
            if (!GetCursorPos(out cursor) || cursor.X != _assistAimX || cursor.Y != _assistAimY) return;
            var hovered = Hud.Game.SelectedActor as IMonster;
            if (hovered != null && hovered.IsAlive && hovered.IsSelected && hovered.AcdId == target.AcdId)
            { _assistProbeLocked = true; return; }
            if (_assistProbeLocked) { _assistProbeLocked = false; _assistProbeExhausted = false; }
            if (_assistProbeExhausted) return;
            if (++_assistProbePhase >= AssistProbeCount)
            { _assistProbePhase = 0; _assistProbeExhausted = true; }
        }



        private bool TryAssistEliteRetarget(IMonster target, int now)
        {
            if (!IsAssistPositionElite(target) || _assistAttackKey == ActionKey.Unknown
                || _pulse != ActionKey.Unknown || _targetKey != ActionKey.Unknown) return false;
            if (AttackedIdentityMatches(target.AcdId, target.AnnId))
            { _assistRetargetAttempts = 0; return false; }
            if (_assistRetargetTriedAcd != target.AcdId)
            { _assistRetargetAttempts = 0; _assistRetargetNextGameTick = int.MinValue; }
            if (!_assistProbeLocked || _assistRetargetAttempts >= 3
                || Hud.Game.CurrentGameTick == _assistRetargetNextGameTick) return false;
            var hovered = Hud.Game.SelectedActor as IMonster;
            if (hovered == null || !hovered.IsAlive || !hovered.IsSelected || hovered.AcdId != target.AcdId) return false;
            IPlayerSkill alternate = null;
            foreach (var generator in _generators)
            {
                if (generator.Key == _assistMainKey || generator.Key == ActionKey.LeftSkill
                    || generator.Key == ActionKey.RightSkill || s7o_GenMonkInput.IsDown(generator.Key)) continue;
                if (alternate == null || generator.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_CripplingWave.Sno)
                    alternate = generator;
            }
            if (alternate == null || !StartPulse(alternate.Key, now, Math.Max(80, GeneratorPulseMs), false)) return false;
            // Hover is acquisition only. Completion requires the actual attacked identity.
            // StartPulse releases only assist-owned input; no physical LMB UP is sent.
            _assistRetargetTriedAcd = target.AcdId; _assistRetargetAttempts++;
            _assistRetargetNextGameTick = Hud.Game.CurrentGameTick;
            _pulseIsRetarget = true;
            _assistStatus = "elite-retarget";
            return true;
        }

        private void ObserveAssistDamageEscape(IWorldCoordinate position, int tick, uint world)
        {
            if (tick == _assistDamageEscapeGameTick && world == _assistDamageEscapeWorld) return;
            var defense = Hud.Game.Me.Defense;
            double health = defense != null && defense.HealthMax > 0 ? defense.HealthCur / defense.HealthMax : 1.0;
            double shield = defense != null ? Math.Max(0, defense.CurShield) : 0.0;
            bool sameWorld = world == _assistDamageEscapeWorld && _assistDamageEscapeGameTick != int.MinValue;
            bool inDamageWindow = sameWorld && _assistDamageWindowTick != int.MinValue
                && tick >= _assistDamageWindowTick && tick - _assistDamageWindowTick <= AssistDamageWindowGameTicks;
            // Keep the existing loss thresholds, but include small hits accumulated in
            // the short native window. Aggregate DPS can be zero during real damage.
            bool healthLoss = sameWorld && (_assistPreviousHealth - health >= 0.05
                || (inDamageWindow && _assistDamageWindowHealth - health >= 0.05));
            bool shieldLoss = sameWorld && _assistPreviousShield > 0.0
                && shield < _assistPreviousShield * 0.98
                && (shield > 0.0 || (defense != null && defense.CurrentDamageTakenPerSecond > 0.0));
            // A partial shield decrease is observable even without DPS. A drop to zero
            // alone is ambiguous with expiry, so it still needs damage corroboration.
            bool resetWindow = !sameWorld || _assistDamageWindowTick == int.MinValue || tick < _assistDamageWindowTick;
            if (resetWindow) { _assistDamageWindowTick = tick; _assistDamageWindowHealth = health; }
            if (tick - _assistDamageWindowTick >= AssistDamageWindowGameTicks)
            { _assistDamageWindowTick = tick; _assistDamageWindowHealth = health; }
            if (!sameWorld)
            { _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none"; }
            else if (_assistDamageEscapePending)
            {
                double dx = position.X - _assistDamageEscapeOriginX, dy = position.Y - _assistDamageEscapeOriginY;
                if (dx * dx + dy * dy >= AssistDamageEscapeHopYards * AssistDamageEscapeHopYards)
                {
                    _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none";
                    _assistDamageWindowTick = tick; _assistDamageWindowHealth = health;
                    healthLoss = sameWorld && _assistPreviousHealth - health >= 0.05;
                }
            }
            // Damage alone must not interrupt attacks. React only when a known
            // Arcane core is nearby; explicit explosion/core avoidance is independent.
            bool arcaneLoss = _assistArcaneNearestActor != null && _assistArcaneNearestDistance <= 25.0 && (healthLoss || shieldLoss);
            if (!_assistDamageEscapePending && _selfDashKey == ActionKey.Unknown
                && arcaneLoss)
            {
                _assistDamageEscapePending = true;
                _assistDamageEscapeOriginX = position.X; _assistDamageEscapeOriginY = position.Y;
                _assistDamageEscapeTrigger = healthLoss ? "health-loss-near-core" : "shield-loss-near-core";
                _assistDamageWindowTick = tick; _assistDamageWindowHealth = health;
            }
            _assistDamageEscapeWorld = world; _assistDamageEscapeGameTick = tick;
            _assistPreviousHealth = health; _assistPreviousShield = shield;
        }

        private bool EnsureAssistScopedStandstill()
        {
            ushort key = s7o_GenMonkInput.StandstillVirtualKey();
            if (_assistScopedStandstill != 0)
            {
                // Let Diablo consume the modifier before a new LMB edge. This waits for
                // native frame advancement, not a fixed pause or repeated click budget.
                if (_assistScopedStandstill == key)
                {
                    if (Hud.Game.CurrentGameTick == _assistScopedStandstillGameTick) return false;
                    if (s7o_GenMonkInput.IsVirtualKeyDown(key)) return true;
                }
                ReleaseAssistScopedStandstill();
            }
            if (key == 0 || key == 0x01 || key == 0x02 || s7o_InputReleaseArbiter.HasPendingRelease) return false;
            if (s7o_GenMonkInput.IsVirtualKeyDown(key)) return true; // Already held: never own/release it.
            if (!s7o_GenMonkInput.SendStandstill(Owner, key, false)) return false;
            _assistScopedStandstill = key;
            _assistScopedStandstillGameTick = Hud.Game.CurrentGameTick;
            // A configured CTRL standstill is not a user request to toggle Juggernaut focus.
            if (key == 0x11 || key == 0xA2 || key == 0xA3) _juggerCtrlWasDown = true;
            return false; // The next fresh frame can accept the force-attack LMB edge.
        }

        private void ReleaseAssistScopedStandstill()
        {
            if (_assistScopedStandstill == 0) return;
            s7o_GenMonkInput.SendStandstill(Owner, _assistScopedStandstill, true);
            _assistScopedStandstill = 0; // Failed UP remains in the shared arbiter retry queue.
            _assistScopedStandstillGameTick = int.MinValue;
        }

        private bool TryStartAssistHazardEscape(IPlayerSkill dash, int now)
        {
            if (!_assistActive || !AssistRequested()) return false;
            bool arcane = AssistReactiveEscapeDue(), molten = AssistMoltenEscapeDue();
            if (!arcane && !molten) return false;
            if (!AutoDash || !CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)
                || s7o_InputReleaseArbiter.HasPendingRelease)
            {
                // No executable escape: keep an available stationary attack/recovery.
                _assistPositionReason = "escape-unavailable";
                MapAssistFeedback("escape-unavailable",_assistTargetAcd,Hud.Game.Me.FloorCoordinate);
                return false;
            }
            // A living elite may improve the landing choice, but is never required to exit.
            var target = FindAssistCandidate(_assistTargetAcd);
            IWorldCoordinate landing; string reason; bool escapeOnly;
            bool found = arcane
                ? TryPickAssistReactiveEscapePoint(target, out landing, out reason, out escapeOnly)
                : TryPickAssistMoltenEscapePoint(target, out landing, out reason, out escapeOnly);
            // Overlapping Arcane/Molten may need the wider Molten boundary samples.
            if (!found && molten)
                found = TryPickAssistMoltenEscapePoint(target, out landing, out reason, out escapeOnly);
            if (!found)
            {
                _assistPositionReason = "escape-no-safe-landing";
                MapAssistFeedback("escape-no-safe-landing",_assistTargetAcd,Hud.Game.Me.FloorCoordinate);
                return false;
            }
            CancelAssistDoor();
            ReleaseAssistAttack();
            CancelTarget();
            if (IsAssistPositionElite(target)) _assistHazardReturnAcd = target.AcdId;
            _mainKey = _assistMainKey; _combinationDisplayKey = _mainKey;
            _assistPositionReason = reason;
            _assistPositionWorldX = landing.X; _assistPositionWorldY = landing.Y;
            MapAssistFeedback(arcane?"reactive-escape-request":"explosion-escape-request",
                target==null?0u:target.AcdId,landing);
            MaintainDash(dash, now, target, landing, true);
            _assistStatus = _selfDashKey != ActionKey.Unknown ? (arcane ? "reactive-dash" : "molten-dash")
                : "escape-input-busy";
            return true;
        }

        private bool CanAttemptAssistSpacing(IMonster target)
        {
            if (target == null || _assistAttackKey != _assistMainKey || !HasAssistAttackContact(target)) return false;
            double dx = target.FloorCoordinate.X - _assistSpacingTargetX;
            double dy = target.FloorCoordinate.Y - _assistSpacingTargetY;
            return target.AcdId != _assistSpacingAttemptAcd || dx * dx + dy * dy >= 9.0;
        }

        private bool UpdateMeleeAssist(IPlayerSkill dash, int now)
        {
            if (!AssistRequested()) return false;
            if (!_assistActive)
            {
                // Do not adopt a mouse/key the user was already holding.
                foreach (var generator in _generators)
                    if (s7o_GenMonkInput.IsDown(generator.Key)) return false;
                IPlayerSkill primary = null;
                foreach (var generator in _generators)
                    if (primary == null || generator.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
                        primary = generator;
                CursorPoint cursor;
                if (primary == null || !GetCursorPos(out cursor)) return true;
                CancelTarget();
                _assistMainKey = primary.Key;
                _assistCursorX = cursor.X; _assistCursorY = cursor.Y;
                _assistActive = true; _assistEnding = false;
                _assistTargetAcd = 0u;
                _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
                _assistStatus = "acquire";
            }
            bool canDashNow = AutoDash && CanApproachDash(dash)
                && !s7o_GenMonkInput.IsDown(dash.Key) && !s7o_InputReleaseArbiter.HasPendingRelease;
            var target = AssistTarget(canDashNow, now);
            IWorldCoordinate moltenEscapePoint = null;
            string moltenEscapeReason = "none";
            bool moltenEscapeOnly = false;
            bool arcaneEscapeDue = AssistReactiveEscapeDue();
            bool hazardUrgent = arcaneEscapeDue || AssistMoltenEscapeDue();
            bool moltenEscape = canDashNow && ((arcaneEscapeDue
                    && TryPickAssistReactiveEscapePoint(target, out moltenEscapePoint, out moltenEscapeReason, out moltenEscapeOnly))
                || (AssistMoltenEscapeDue()
                    && TryPickAssistMoltenEscapePoint(target, out moltenEscapePoint, out moltenEscapeReason, out moltenEscapeOnly)));
            if (moltenEscape || arcaneEscapeDue || AssistMoltenEscapeDue()) CancelAssistDoor();
            else {
                if(target==null||target.AcdId!=_assistMapGoalAcd)_assistMapDoorAcd=0;
                if(UpdateAssistDoor(target,now))return true;
            }
            IWorldCoordinate circleLanding = null;
            string circleReason = "none";
            _assistCircleCue = "none"; _assistCircleOpportunityAcd = 0u;
            bool circleDash = !hazardUrgent && canDashNow && !_assistNavigationPending
                && !_assistDashRetryNeedsAttack && Due(now, _nextDashAimTick)
                && TryPickAssistCircleUpgrade(target, out circleLanding, out circleReason);
            if (circleDash) _assistCircleCue = circleReason;
            IWorldCoordinate spacingLanding = null;
            string spacingReason = "none";
            bool spacingNeeded = target != null && (AssistElectrifiedSpacingDue()
                || AssistChainDistance(Hud.Game.Me.FloorCoordinate, target) < 3.0);
            bool spacingDash = !hazardUrgent && !circleDash && spacingNeeded && CanAttemptAssistSpacing(target) && canDashNow
                && !_assistNavigationPending && !_assistDashRetryNeedsAttack && Due(now, _nextDashAimTick)
                && TryPickAssistDashPoint(target, false, out spacingLanding, out spacingReason)
                && spacingLanding.XYDistanceTo(Hud.Game.Me.FloorCoordinate) > 1f;
            bool contactCorrection = target != null && canDashNow && AssistContactCorrectionDue(target, now);
            // A maintenance pulse no longer has work to verify after its combat target dies
            // or is replaced. Release our own pulse and acquire the next target this update.
            if (_pulse != ActionKey.Unknown)
            {
                if (target == null || target.AcdId != _assistTargetAcd) CancelTarget();
                // An owned secondary attack can also follow at range. Yield it to a safe
                // Dash approach instead of waiting out its pulse on a hazardous segment.
                else if (_pulseIsMaintenance && s7o_GenMonkInput.Owns(_pulse)
                    && (moltenEscape || circleDash || spacingDash || contactCorrection || !IsAssistNativeFollowHazardFree(target))) CancelTarget();
                else { _assistStatus = "secondary"; return true; }
            }
            if (_assistAttackKey != ActionKey.Unknown && ElapsedMs(now, _assistAttackStartedTick) >= 35
                && HasAssistAttackContact(target))
            {
                _assistHitVerified = true;
                // Contact can arrive through the held navigation generator. Hand its
                // owned key back to WotHF immediately instead of keeping it held forever.
                if (_assistNavigationPending || _assistAttackKey != _assistMainKey) ReleaseAssistAttack();
                ResetAssistContactRecovery();
                RememberAssistTrashAttack(target);
                _assistDashRetryNeedsAttack = false;
                _assistApproachPending = false; // Already attacking: no late approach Dash is needed.
            }
            if (target == null)
            {
                ReleaseAssistAttack();
                CancelTarget();
                _assistTargetAcd = 0u;
                _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
                if (moltenEscape)
                {
                    _mainKey = _assistMainKey;
                    _combinationDisplayKey = _mainKey;
                    _assistPositionReason = moltenEscapeReason;
                    _assistPositionWorldX = moltenEscapePoint.X;
                    _assistPositionWorldY = moltenEscapePoint.Y;
                    MaintainDash(dash, now, null, moltenEscapePoint, true);
                    if (_selfDashKey != ActionKey.Unknown)
                    { _assistStatus = arcaneEscapeDue ? "reactive-dash" : "molten-dash"; return true; }
                }
                _assistStatus = _assistPylonBlockedTargets ? "pylon-blocked"
                    : _assistUiBlockedTargets ? "ui-blocked"
                    : _assistHazardBlockedTargets ? "hazard-follow-blocked" : "no-target";
                return true;
            }
            if (target.AcdId != _assistTargetAcd)
            {
                ReleaseAssistAttack();
                CancelTarget();
                _assistTargetAcd = target.AcdId; _assistTargetAnn = target.AnnId;
                ResetAssistContactRecovery();
                _assistAimTick = now;
                _assistAimGameTick = Hud.Game.CurrentGameTick;
                _assistAimPending = true;
                _assistHitVerified = false;
                _assistApproachPending = true;
                _assistDashRetryNeedsAttack = false;
                _nextDashAimTick = now; // A previous target's cancelled aim must not delay this target.
            }
            _mainKey = _assistMainKey;
            _combinationDisplayKey = _mainKey;
            double meleeRange = AssistAttackReachYards;
            bool approaching = !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target) || MapAssistTargetBlocked(target);
            if (approaching && !_assistWasApproaching)
            {
                // A teleport/knockback can invalidate native LMB follow. Reacquire using
                // only the assist-owned attack, even when the small gap will be walked.
                ReleaseAssistAttack();
                CancelTarget();
            }
            _assistWasApproaching = approaching;
            // Approach every target category, including a new target inside attack range.
            // Once a Dash or native attack engages it, do not keep Dashing at a settled target.
            if (target.NormalizedXyDistanceToMe <= 0) _assistApproachPending = false;
            bool navigationRetry = false;
            // Hazard escape retains the elite before navigation may abandon a blocked chase.
            // A ready mapped detour can preempt a pending native follow; no ten-yard
            // walk requirement applies to a new verified waypoint around a corner.
            if(!moltenEscape&&approaching&&canDashNow&&_assistNavigationPending&&MapAssistStatus()!=null) {
                IWorldCoordinate mapped;string mappedReason;bool transit;
                if(TryPlanAssistApproach(target,out mapped,out mappedReason,out transit)&&_assistMapRouteReady) {
                    ReleaseAssistAttack();ResetAssistContactRecovery();
                    _assistBlockedTargets.Remove(target.AcdId);_nextDashAimTick=now;
                    navigationRetry=true;
                }
            }
            if (!moltenEscape&&!navigationRetry) AdvanceAssistNavigation(target, now, canDashNow, out navigationRetry);
            if (!approaching) _assistDashContinuationPending = false;
            bool continuationDash = _assistDashContinuationPending && !_assistNavigationPending;
            bool approachDash = approaching && !_assistNavigationPending
                && !IsAssistTargetTemporarilyBlocked(target) && !_assistCorrectiveDashSpent;
            bool hazardApproach = canDashNow && !_assistNavigationPending
                && !IsAssistTargetTemporarilyBlocked(target) && !IsAssistNativeFollowHazardFree(target);
            bool refreshDash = dash != null && DashDue(dash, now) && !_assistNavigationPending;
            bool landingReplan = _assistPositionReplanPending && !_assistNavigationPending;
            if ((approachDash || continuationDash || refreshDash || circleDash || spacingDash || moltenEscape || landingReplan || hazardApproach
                    || contactCorrection || navigationRetry)
                && AutoDash && CanApproachDash(dash)
                // No fixed delay after a completed Dash or target death. Successful
                // progress can continue immediately; a failed cast enters bounded recovery.
                && (moltenEscape || circleDash || landingReplan || navigationRetry || continuationDash
                    || (Due(now, _nextDashAimTick) && (contactCorrection
                        || ((!_assistDashRetryNeedsAttack || approaching) && !_assistNavigationPending)))))
            {
                IWorldCoordinate landing = moltenEscape ? moltenEscapePoint : circleDash ? circleLanding : spacingDash ? spacingLanding : null;
                string positionReason = moltenEscape ? moltenEscapeReason : circleDash ? "circle-cue:" + circleReason
                    : spacingDash ? "electrified-spacing:" + spacingReason : "none";
                _assistPositionReplanPending = false;
                bool mapTransit = false;
                bool planned = moltenEscape || circleDash || spacingDash;
                if (!planned)
                    planned = approaching
                        ? TryPlanAssistApproach(target, out landing, out positionReason, out mapTransit)
                        : TryPickAssistDashPoint(target, false, out landing, out positionReason);
                if (planned)
                {
                    _assistPositionReason = navigationRetry ? "navigation-retry:" + positionReason
                        : continuationDash ? "dash-continue:" + positionReason
                        : contactCorrection ? "contact-redash:" + positionReason
                        : landingReplan ? "landing-replan:" + positionReason : positionReason;
                    _assistPositionWorldX = landing.X; _assistPositionWorldY = landing.Y;
                    ReleaseAssistAttack();
                    CancelTarget();
                    if (moltenEscape && IsAssistPositionElite(target)) _assistHazardReturnAcd = target.AcdId;
                    MaintainDash(dash, now, target, landing, moltenEscape || moltenEscapeOnly, mapTransit);
                    if (_selfDashKey != ActionKey.Unknown)
                    {
                        if (spacingDash)
                        {
                            _assistSpacingAttemptAcd = target.AcdId;
                            _assistSpacingTargetX = target.FloorCoordinate.X;
                            _assistSpacingTargetY = target.FloorCoordinate.Y;
                        }
                        MapAssistFeedback(mapTransit?"routed-dash":"approach-dash",target.AcdId,landing);
                        _lastApproachDashTick = now;
                        _lastApproachDashTargetAcd = target.AcdId;
                        if (contactCorrection) { _assistCorrectiveDashSpent = true; _assistCorrectiveDashCount++; }
                        if (continuationDash) _assistDashContinuationCount++;
                        _assistStatus = moltenEscape ? (arcaneEscapeDue ? "reactive-dash" : "molten-dash")
                            : navigationRetry ? "navigation-redash" : continuationDash ? "dash-continue"
                            : circleDash ? "circle-dash" : spacingDash ? "electrified-spacing-dash" : contactCorrection ? "contact-redash" : "target-dash";
                        return true;
                    }
                }
            }
            // A failed Dash owns keyboard navigation until contact, useful movement or danger.
            if (!moltenEscape && _assistNavigationPending && HandleAssistNativeNavigation(target, now)) return true;
            // A rejected/unavailable landing must never fall through to a hazardous walk.
            // Selection already tries another safe target; recheck after any state transition.
            bool canAttackHere = CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target);
            if (!RefreshAssistPositionActors() || (!canAttackHere && !IsAssistNativeFollowHazardFree(target)))
            {
                ReleaseAssistAttack();
                CancelTarget();
                _assistPositionReason = "unsafe-follow-blocked";
                _assistStatus = "hazard-follow-blocked";
                return true;
            }
            if (approaching && !moltenEscape)
            {
                BeginAssistNativeNavigation(target, Hud.Game.Me.FloorCoordinate, now);
                if (HandleAssistNativeNavigation(target, now)) return true;
            }
            // A held DOWN can lose native attack continuity after a cast. Recover only the
            // assist-owned primary, with a bounded idle grace; never reclick manual input.
            if (_assistAttackKey != ActionKey.Unknown)
            {
                // Native LMB follow can also keep running beside an already reachable target.
                // Recover after the same bounded grace, but allow normal running at range.
                bool closeFollow = Hud.Game.Me.AnimationState == AcdAnimationState.Running
                    && target.NormalizedXyDistanceToMe <= Math.Min(2.0, meleeRange);
                bool idle = (Hud.Game.Me.AnimationState == AcdAnimationState.Idle || closeFollow)
                    && ElapsedMs(now, _assistAttackStartedTick) >= 150;
                if (idle && _assistIdleSinceTick == int.MinValue) _assistIdleSinceTick = now;
                if (!idle) _assistIdleSinceTick = int.MinValue;
                if (!s7o_GenMonkInput.IsDown(_assistAttackKey)
                    || (idle && ElapsedMs(now, _assistIdleSinceTick) >= 250))
                {
                    ReleaseAssistAttack();
                    CancelTarget();
                    _assistHitVerified = false;
                    _assistApproachPending = true;
                    // Rearming owned LMB is not proof that a failed Dash recovered.
                    if (!_assistCorrectiveDashSpent && !_assistNavigationRetrySpent)
                        _assistDashRetryNeedsAttack = false;
                    _assistRecoveries++;
                    _assistStatus = closeFollow ? "rearm-follow" : "rearm-idle";
                    return true;
                }
            }
            int x, y;
            // Recheck before moving a held LMB; a control/overlay may have appeared since selection.
            RefreshAssistEliteProbe(target);
            if (!TryGetAssistAim(target, out x, out y))
            {
                // Reject this probe, then use the original safe floor aim this update.
                // Do not turn one out-of-bounds body point into a permanent combat stall.
                _assistProbePhase = 0; _assistProbeLocked = false; _assistProbeExhausted = true;
                if (!TryGetAssistAim(target, out x, out y))
                { _assistStatus = "ui-blocked"; ReleaseAssistAttack(); return true; }
            }
            CursorPoint cursorNow;
            if (!GetCursorPos(out cursorNow)
                || ((cursorNow.X != (int)x || cursorNow.Y != (int)y) && !SetCursorPos((int)x, (int)y)))
            { _assistStatus = "cursor-failed"; ReleaseAssistAttack(); return true; }
            _assistAimX = (int)x; _assistAimY = (int)y; _assistCursorOwned = true;
            bool scopedStandstill = IsAssistCorpseOverlap() || ShouldHoldAssistPosition(target)
                || (canAttackHere && !IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate));
            if (scopedStandstill && !EnsureAssistScopedStandstill())
            { _assistStatus = _assistScopedStandstill != 0 ? "scoped-standstill" : "scoped-input-busy"; return true; }
            if (_assistAttackKey == ActionKey.Unknown)
            {
                _assistStatus = "aim";
                var hovered = Hud.Game.SelectedActor as IMonster;
                // Reuse an aim Diablo already consumed at this unchanged cursor position.
                // Otherwise wait only for the mouse move to reach the next game update;
                // there is no additional fixed 20 ms delay and no indefinite hover gate.
                bool aimReady = hovered != null && hovered.AcdId == target.AcdId
                    && cursorNow.X == _assistAimX && cursorNow.Y == _assistAimY
                    && (!_pylonNeedsMonsterHover || hovered.IsSelected);
                if (_assistAimPending)
                {
                    _assistAimPending = false;
                    _assistAimTick = now; _assistAimGameTick = Hud.Game.CurrentGameTick;
                    if (!aimReady) return true;
                }
                if (!aimReady && Hud.Game.CurrentGameTick == _assistAimGameTick) return true;
                if (_assistMainKey == ActionKey.LeftSkill && _pylonNeedsMonsterHover && !aimReady
                    && !IsAssistCorpseOverlap())
                {
                    _assistStatus = "pylon-hover-guard";
                    if (_assistProbeExhausted)
                    {
                        BeginAssistNativeNavigation(target, Hud.Game.Me.FloorCoordinate, now);
                        HandleAssistNativeNavigation(target, now);
                    }
                    return true;
                }
                if (!s7o_GenMonkInput.Down(Owner, _assistMainKey))
                { ReleaseAssistScopedStandstill(); _assistStatus = "input-busy"; return true; }
                _assistAttackKey = _assistMainKey;
                _assistAttackStartedTick = now;
                _assistIdleSinceTick = int.MinValue;
                _candidateKey = _mainKey; _candidateSinceTick = now;
                _assistWotHfResumePending = true;
                _assistWotHfResumeGameTick = Hud.Game.CurrentGameTick;
            }
            if (TryAssistEliteRetarget(target, now)) return true;
            _assistStatus = _assistNavigationPending ? "navigate" : approaching ? "follow" : "attack";
            if (approaching || _assistNavigationPending) return true; // Native owned attack follows; generator maintenance waits for melee.
            return false; // Existing verified generator maintenance handles buffs unchanged.
        }

        private void ReleaseAssistAttack(bool keepScopedStandstill = false)
        {
            _assistAimPending = true;
            _assistIdleSinceTick = int.MinValue;
            if (_assistAttackKey != ActionKey.Unknown)
                s7o_GenMonkInput.Up(Owner, _assistAttackKey);
            _assistAttackKey = ActionKey.Unknown;
            if (!keepScopedStandstill) ReleaseAssistScopedStandstill();
        }

        private void EndAssist()
        {
            CancelAssistDoor();
            _assistDoors.Clear();
            _assistDoorFailures.Clear();
            ReleaseAssistAttack();
            CursorPoint current;
            if (_assistCursorOwned && Hud.Window.IsForeground && GetCursorPos(out current)
                && Math.Abs((long)current.X - _assistAimX) <= 128
                && Math.Abs((long)current.Y - _assistAimY) <= 128)
                SetCursorPos(_assistCursorX, _assistCursorY);
            _assistActive = _assistEnding = _assistCursorOwned = false;
            _assistAimPending = false;
            _assistTargetAcd = _assistTargetAnn = 0u;
            _assistMainKey = ActionKey.Unknown;
            _assistHitVerified = false;
            _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
            _lastApproachDashTargetAcd = 0u;
            _lastApproachDashTick = int.MinValue;
            _assistStatus = "off";
            _assistProbeTargetAcd = _assistRetargetTriedAcd = 0u;
            _assistRetargetAttempts = 0; _assistRetargetNextGameTick = int.MinValue;
            _assistProbePhase = 0; _assistProbeGameTick = int.MinValue;
            _assistProbeLocked = _assistProbeExhausted = false;
            _assistWotHfLastGameTick = _assistWotHfObservationGameTick = _assistWotHfResumeGameTick = int.MinValue;
            _assistWotHfResumePending = false;
            _assistDamageEscapePending = false; _assistDamageEscapeGameTick = int.MinValue;
            _assistDamageEscapeWorld = 0u; _assistDamageEscapeTrigger = "none";
            _assistDamageWindowTick = int.MinValue; _assistDamageWindowHealth = 1.0;
            _assistCircleCue = "none"; _assistCircleOpportunityAcd = 0u;
            ResetAssistContactRecovery();
            _assistBlockedTargets.Clear();
            _assistFailedLandings.Clear();
            _assistBlockedCircles.Clear(); _assistDashCircleAcd = 0u;
            _assistTrashRecheckGameTick = int.MinValue;
            _assistTrashProfiles.Clear(); _assistTrashSelectionClass = 0;
            _assistTravelHeadingKnown = false; _assistLastTrashAttackAcd = 0u; _assistTrashFanReady = false;
            _assistBlockedEscapeLandings.Clear();
            _assistDashLanding = null;
            _assistDashEscapeOnly = false;
            _assistDashLandingWorldId = 0u;
            _assistHazardReturnAcd = 0u;
            _assistPositionReplanPending = false;
            _assistPositionGameTick = int.MinValue;
            _assistPositionReadable = false;
            _assistPositionCircles.Clear();
            _assistPositionMolten.Clear();
            _assistPositionArcane.Clear();
            _assistPositionOrbiter.Clear();
            _assistPositionElectrified.Clear();
            _assistOrbiterNearestActor = null;
            _assistOrbiterCoreUnsafe = _assistElectrifiedUnsafe = false;
            _assistPositionArcaneCount = 0;
            _assistArcaneCoreUnsafe = false;
            _assistArcaneNearestActor = null;
            _assistArcaneNearestSno = _assistArcaneNearestAcd = 0u;
            _assistArcaneNearestDistance = double.NaN;
            _assistArcaneCollisionDeltaX = _assistArcaneCollisionDeltaY = float.NaN;
            _assistArcaneEscapeReason = "none";
            _assistPositionCircleCount = _assistPositionHazardCount = 0;
            _assistPositionEscapeReason = "none";
        }

        private bool CanApproachDash(IPlayerSkill dash)
        {
            try
            {
                // Native Raiment can spend a charge below its Spirit cost. Preserve
                // that working fallback. Empty charges cannot cast even when the
                // separate cooldown flag is false, as observed late in REV8.
                return dash != null && !dash.IsOnCooldown && dash.Charges > 0;
            }
            catch { return false; }
        }

        private bool DashDue(IPlayerSkill dash, int now)
        {
            if (!Due(now, _nextDashAimTick)) return false;
            bool radiance = (dash.RuneNameEnglish != null
                && dash.RuneNameEnglish.IndexOf("Radiance", StringComparison.OrdinalIgnoreCase) >= 0)
                || dash.Rune == 4;
            int interval = radiance ? 3500 : 5500;
            if (_lastDashTick == int.MinValue) return true;
            // With Raiment equipped, a missing damage buff overrides the normal cadence
            // after giving the previous cast time to appear in collected game data.
            double raiment = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
            if ((_assistActive ? _selfDashKey == ActionKey.Unknown && !_dashAwaiting
                    : (uint)(now - _lastDashTick) >= 700u) && Hud.Game.Me.GetSetItemCount(755275u) >= 6
                && raiment <= 0.6) return true;
            if ((uint)(now - _lastDashTick) < (uint)interval) return false;
            return radiance || raiment <= 0.6;
        }

        // Optional BCL bridge; deletion leaves the native keyboard navigation intact.
        // Route planning changes only Space approaches, never manual attack ownership.
        private Func<string,object[],object> _assistMapGet;
        private IPlugin _assistMapPlugin;
        private int _assistMapAttachTick=int.MinValue;
        private uint _assistMapDoorAcd;
        private bool _assistMapDashTransit,_assistMapRouteReady;
        private uint _assistMapGoalAcd,_assistMapGoalWorld;
        private IWorldCoordinate _assistMapGoal,_assistMapTargetPosition;
        private void ResetMapAssist()
        { _assistMapDoorAcd=0;_assistMapDashTransit=false;_assistMapRouteReady=false;_assistMapGoal=null;_assistMapGoalAcd=0; }
        private bool AttachMapAssist()
        {
            if(_assistMapGet!=null&&_assistMapPlugin!=null&&_assistMapPlugin.Enabled)return true;
            _assistMapGet=null;_assistMapPlugin=null;
            int tick=Hud.Game.CurrentGameTick;
            if(_assistMapAttachTick!=int.MinValue&&tick>=_assistMapAttachTick&&tick-_assistMapAttachTick<60)return false;
            _assistMapAttachTick=tick;
            foreach(var plugin in Hud.AllPlugins) {
                if(!plugin.Enabled)continue;
                var exports=plugin as IEnumerable<KeyValuePair<string,Func<string,object[],object>>>;
                if(exports==null)continue;
                foreach(var export in exports)if(export.Key=="s7o.MapViewer.v1") {
                    _assistMapGet=export.Value;_assistMapPlugin=plugin;return true;
                }
            }
            return false;
        }
        private Dictionary<string,object> MapAssistStatus()
        {
            if(!_assistActive||!AttachMapAssist())return null;
            try {
                var status=_assistMapGet("player",null) as Dictionary<string,object>;
                if(status==null||!status.ContainsKey("ok")||!(status["ok"] is bool)||(bool)status["ok"]==false
                    ||Convert.ToUInt32(status["worldId"])!=Hud.Game.Me.WorldId)return null;
                return status;
            }catch { _assistMapGet=null;_assistMapPlugin=null;return null; }
        }
        private bool MapAssistLandingAllowed(IWorldCoordinate point)
        {
            var status=MapAssistStatus();if(status==null)return true;
            try {
                var answer=_assistMapGet("point",new object[]{status["epoch"],point.X,point.Y,point.Z}) as Dictionary<string,object>;
                if(answer==null)return true;
                string state=answer.ContainsKey("state")?answer["state"] as string:null;
                if(state=="BlockedCandidate"||state=="NoWalkCandidate")return false;
                // Full landing clearance is independent of attack reach and source-wall escape.
                // A short routed hop must not inherit the route origin's tapered margin.
                float radius=Math.Max(0f,Math.Min(3f,MapWallClearance));
                var edge=_assistMapGet("corridor",new object[]{status["epoch"],point.X,point.Y,point.Z,
                    point.X,point.Y,point.Z,radius}) as Dictionary<string,object>;
                return edge==null||!edge.ContainsKey("state")||(edge["state"] as string)!="BlockedCandidate";
            }catch { return true; }
        }
        private bool MapAssistTargetBlocked(IMonster target)
        {
            if(target==null||target.FloorCoordinate==null)return false;
            var status=MapAssistStatus();if(status==null)return false;
            try {
                var from=Hud.Game.Me.FloorCoordinate;var to=target.FloorCoordinate;
                var ray=_assistMapGet("corridor",new object[]{status["epoch"],from.X,from.Y,from.Z,to.X,to.Y,to.Z,0.35f}) as Dictionary<string,object>;
                return ray!=null&&ray.ContainsKey("state")&&(ray["state"] as string)=="BlockedCandidate";
            }catch {_assistMapGet=null;_assistMapPlugin=null;return false;}
        }
        private void MapAssistFeedback(string action,uint target,IWorldCoordinate point,bool reset=false)
        {
            var status=MapAssistStatus();if(status==null||point==null)return;
            try {
                _assistMapGet("feedback",new object[]{status["epoch"],"GenMonk",action,target,point.X,point.Y,point.Z});
                if(reset){_assistMapGet("resetpath",new object[]{status["epoch"]});_assistMapGoal=null;}
            }catch {_assistMapGet=null;_assistMapPlugin=null;}
        }
        // Combat destination planning never authorizes an overrange cast. Only the
        // execution waypoint passes the 50-yard gate; acquisition stays independent.
        private bool TryPlanAssistApproach(IMonster target, out IWorldCoordinate landing,
            out string reason, out bool transit)
        {
            transit = false;
            if (!TryPickAssistDashPoint(target, false, out landing, out reason, true)) return false;
            if (!TryMapAssistApproach(target, ref landing, out transit)) return false;
            if (transit) reason = "approach-hop:" + reason;
            return true;
        }
        private bool TryBoundAssistHop(IMonster target, ref IWorldCoordinate landing, out bool transit)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            double distance = me.XYDistanceTo(landing);
            transit = distance > AssistDashTravelLimit;
            if (transit) {
                // Margin prevents diagonal float rounding from exceeding 50 yards.
                double scale = (AssistDashTravelLimit - 0.5) / distance;
                landing = me.Offset((float)((landing.X-me.X)*scale),
                    (float)((landing.Y-me.Y)*scale), (float)((landing.Z-me.Z)*scale));
            }
            int x,y;
            return IsAssistDashPointSafe(landing,target,out x,out y,false,transit);
        }
        private bool TryMapAssistApproach(IMonster target,ref IWorldCoordinate landing,out bool transit)
        {
            transit=false;_assistMapRouteReady=false;_assistMapDoorAcd=0;
            var status=MapAssistStatus();
            if(status==null)return TryBoundAssistHop(target,ref landing,out transit);
            try {
                var me=Hud.Game.Me.FloorCoordinate;
                if(_assistMapGoal==null||_assistMapGoalAcd!=target.AcdId||_assistMapGoalWorld!=target.WorldId
                    ||_assistMapTargetPosition==null||_assistMapTargetPosition.XYDistanceTo(target.FloorCoordinate)>4f) {
                    _assistMapGoalAcd=target.AcdId;_assistMapGoalWorld=target.WorldId;
                    _assistMapGoal=landing.Offset(0f,0f,0f);_assistMapTargetPosition=target.FloorCoordinate.Offset(0f,0f,0f);
                }
                landing=_assistMapGoal.Offset(0f,0f,0f);
                // Static walls/pits may be skipped if the buffered landing clears them.
                // Doors veto crossing. A failed shortcut yields to a walkable detour,
                // never another attempt through the same wall during native recovery.
                if (!_assistNavigationPending && !IsAssistTargetTemporarilyBlocked(target)) {
                    var hop=_assistMapGet("dash",new object[]{status["epoch"],me.X,me.Y,me.Z,
                        landing.X,landing.Y,landing.Z,Math.Max(0f,Math.Min(3f,MapWallClearance))}) as Dictionary<string,object>;
                    var xyz=hop!=null&&hop.ContainsKey("waypoint")?hop["waypoint"] as float[]:null;
                    if(xyz!=null&&xyz.Length==3) {
                        var point=Hud.Window.CreateWorldCoordinate(xyz[0],xyz[1],xyz[2]);
                        bool intermediate=point.XYDistanceTo(landing)>1.5f;
                        int x,y;
                        if(IsAssistDashPointSafe(point,target,out x,out y,false,intermediate)) {
                            landing=point;transit=intermediate;return true;
                        }
                    }
                }
                var answer=_assistMapGet("path",new object[]{status["epoch"],me.X,me.Y,me.Z,
                    landing.X,landing.Y,landing.Z,Math.Max(0f,Math.Min(3f,MapWallClearance))}) as Dictionary<string,object>;
                if(answer==null||!answer.ContainsKey("state")||(answer["state"] as string)=="Unavailable")
                    return TryBoundAssistHop(target,ref landing,out transit);
                if(answer.ContainsKey("doorAcd"))_assistMapDoorAcd=Convert.ToUInt32(answer["doorAcd"]);
                // Pending/no-route uses native navigation immediately, never timed idle.
                if(!answer.ContainsKey("waypoint"))return false;
                var routeXYZ=answer["waypoint"] as float[];if(routeXYZ==null||routeXYZ.Length!=3)return false;
                var routePoint=Hud.Window.CreateWorldCoordinate(routeXYZ[0],routeXYZ[1],routeXYZ[2]);
                bool routeTransit=routePoint.XYDistanceTo(landing)>1.5f;
                int rx,ry;if(!IsAssistDashPointSafe(routePoint,target,out rx,out ry,false,routeTransit))return false;
                landing=routePoint;transit=routeTransit;_assistMapRouteReady=true;return true;
            }catch { _assistMapGet=null;_assistMapPlugin=null;return TryBoundAssistHop(target,ref landing,out transit); }
        }


        private void MaintainDash(IPlayerSkill dash, int now, IMonster approachTarget = null,
            IWorldCoordinate assistLanding = null, bool assistEscapeOnly = false, bool mapTransit = false)
        {
            try
            {
                if (!CanApproachDash(dash)) return;
                if (SelfDash || approachTarget != null || assistEscapeOnly)
                {
                    if (s7o_GenMonkInput.IsDown(dash.Key)
                        || s7o_InputReleaseArbiter.HasPendingRelease) return;
                    // Own attacks can be suspended. Never send UP for the user's held attack.
                    if (_assistActive) ReleaseAssistAttack();
                    var self = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
                    IMonster attackTarget = approachTarget ?? (!_assistActive && _mainKey == ActionKey.LeftSkill
                        && !s7o_GenMonkInput.IsStandstillDown() ? ManualAttackTarget() : null);
                    if (attackTarget != null)
                        self = attackTarget.FloorCoordinate.ToScreenCoordinate(true, true);
                    if ((approachTarget != null || assistEscapeOnly) && assistLanding != null)
                    {
                        int landingX, landingY;
                        if (!IsAssistDashPointSafe(assistLanding, approachTarget, out landingX, out landingY, assistEscapeOnly, mapTransit)) return;
                        self = assistLanding.ToScreenCoordinate(true, true);
                    }
                    if (self == null || double.IsNaN(self.X) || double.IsNaN(self.Y)
                        || double.IsInfinity(self.X) || double.IsInfinity(self.Y)) return;

                    CursorPoint current;
                    if (!GetCursorPos(out current)) return;

                    // Projected game coordinates are client-relative; retain the window offset
                    // so negative desktop coordinates on additional monitors remain valid.
                    var offset = Hud.Window.Offset;
                    long targetX = (long)Math.Round(self.X) + offset.X;
                    long targetY = (long)Math.Round(self.Y) + offset.Y;
                    if (targetX < int.MinValue || targetX > int.MaxValue
                        || targetY < int.MinValue || targetY > int.MaxValue) return;

                    _cursorX = current.X;
                    _cursorY = current.Y;
                    _dashTargetX = (int)targetX;
                    _dashTargetY = (int)targetY;
                    if (!SetCursorPos(_dashTargetX, _dashTargetY)) return;
                    _restoreCursor = true;
                    _selfDashKey = dash.Key;
                    _selfDashApproach = approachTarget != null || (_assistActive && assistEscapeOnly);
                    _assistDashLanding = _selfDashApproach && assistLanding != null ? assistLanding.Offset(0f, 0f, 0f) : null;
                    _assistDashCircleAcd = 0u;
                    if (_assistDashLanding != null)
                        foreach (var circle in _assistPositionCircles)
                            if (circle.Actor != null && !circle.Actor.IsDisabled
                                && _assistDashLanding.XYDistanceTo(circle.Actor.FloorCoordinate) <= AssistCircleLandingRadius)
                            { _assistDashCircleAcd = circle.Actor.AcdId; break; }
                    _assistDashEscapeOnly = _selfDashApproach && _assistDashLanding != null && assistEscapeOnly;
                    _assistMapDashTransit=mapTransit;
                    _assistDashLandingWorldId = _assistDashLanding != null ? Hud.Game.Me.WorldId : 0u;
                    if (_assistDashLanding != null) _assistPositionActualX = _assistPositionActualY = float.NaN;
                    _selfDashAimIsMonster = attackTarget != null && !_selfDashApproach;
                    _selfDashTargetAcd = attackTarget != null ? attackTarget.AcdId : 0u;
                    _selfDashObserved = false;
                    _assistDashFreshAnimation = _assistDashPositionObserved = _assistDashLandingConfirmed = false;
                    _assistDashActualSafe = _assistDashSafetyAssessed = _assistDashMovementFailed = false;
                    _assistDashSafetyReason = "not-assessed";
                    _assistDashReleasedGameTick = int.MinValue;
                    _assistDashOutcome = "aim";
                    _selfDashSerial++;
                    _selfDashResult = "aim";
                    _selfDashAimTick = now;
                    _selfDashGameTick = Hud.Game.CurrentGameTick;
                    _assistAimRefreshGameTick = int.MinValue;
                    _assistRunningSinceTick = int.MinValue;
                    _assistDashForHazard = _selfDashApproach && (assistEscapeOnly
                        || AssistMoltenEscapeDue() || AssistReactiveEscapeDue());
                    if (_selfDashApproach && _assistDashLanding != null)
                    {
                        var origin = Hud.Game.Me.FloorCoordinate;
                        _assistDashOriginX = origin.X; _assistDashOriginY = origin.Y; _assistDashOriginZ = origin.Z;
                        _assistDashContinuationPending = false;
                        _assistDashTargetDistanceBefore = approachTarget != null
                            ? origin.XYDistanceTo(approachTarget.FloorCoordinate) : float.NaN;
                        _assistDashTravelRequested = origin.XYDistanceTo(_assistDashLanding);
                        _assistDashVerticalRequested = Math.Abs(origin.Z - _assistDashLanding.Z);
                        _assistDashVerticalObserved = 0f;
                        _assistDashTravelObserved = float.NaN;
                    }
                    _selfDashRaimentBefore = _selfDashApproach
                        ? BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2) : 0;
                    _selfDashCastTick = int.MinValue;
                    var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
                    _selfDashBuffBefore = buff != null && buff.TimeLeftSeconds != null
                        ? (double[])buff.TimeLeftSeconds.Clone() : new double[0];
                    return;
                }

                if (StartPulse(dash.Key, now, Math.Max(20, DashPulseMs), false))
                {
                    // Dash interrupts WotHF; wait for a fresh manual attack before maintenance.
                    // Do not move onto a monster to rebuild the native LMB lock.
                    _lastDashTick = now;
                    _dashAwaiting = true;
                }
                else
                {
                    RestoreCursor();
                }
            }
            catch
            {
                _selfDashKey = ActionKey.Unknown;
                RestoreCursor();
            }
        }

        private void AdvanceSelfDash(int now)
        {
            CursorPoint current;
            bool attackRequested = _assistActive ? AssistRequested() || _assistEnding
                : s7o_GenMonkInput.IsDown(_mainKey);
            if (!AutoDash || (!SelfDash && !_selfDashApproach) || !attackRequested
                || !GetCursorPos(out current))
            {
                CancelTarget(); // Preserve mouse movement; RestoreCursor will not overwrite it.
                return;
            }

            if (_selfDashCastTick == int.MinValue)
            {
                IMonster liveDashTarget = null;
                if (_selfDashTargetAcd != 0u)
                {
                    foreach (var monster in Hud.Game.AliveMonsters)
                        if (monster != null && monster.AcdId == _selfDashTargetAcd
                            && (_selfDashApproach ? IsAssistEligible(monster)
                                : IsMeleeTarget(monster, double.MaxValue))) { liveDashTarget = monster; break; }
                    if (liveDashTarget == null && !_assistDashEscapeOnly) { CancelTarget(); return; }
                }
                if (Math.Abs((long)current.X - _dashTargetX) > 12
                    || Math.Abs((long)current.Y - _dashTargetY) > 12)
                {
                    CancelTarget();
                    _selfDashResult = "cancelled-user-mouse";
                    return;
                }
                if (_selfDashApproach && _assistDashLanding != null)
                {
                    if (_assistDashLandingWorldId != Hud.Game.Me.WorldId) { CancelTarget(); return; }
                    int landingX, landingY;
                    if (!IsAssistDashPointSafe(_assistDashLanding, liveDashTarget, out landingX, out landingY, _assistDashEscapeOnly, _assistMapDashTransit))
                    {
                        CancelTarget();
                        _selfDashResult = "cancelled-landing";
                        // Native target/camera movement or a changed hazard is not user mouse
                        // intervention. Replan immediately; retain the hard aim watchdog.
                        _nextDashAimTick = now;
                        _assistPositionReplanPending = true;
                        return;
                    }
                    if (Math.Abs((long)landingX - _dashTargetX) > 12 || Math.Abs((long)landingY - _dashTargetY) > 12)
                    {
                        // The user-mouse guard already passed. Reproject our still-valid world
                        // landing when the camera moves instead of cancelling into native running.
                        if (!SetCursorPos(landingX, landingY))
                        { CancelTarget(); _selfDashResult = "cancelled-cursor"; return; }
                        _dashTargetX = landingX; _dashTargetY = landingY;
                        _assistAimRefreshGameTick = Hud.Game.CurrentGameTick;
                        _selfDashResult = "aim-reprojected";
                    }
                }
                // Let Diablo consume the mouse move before it receives the key press.
                if (ElapsedMs(now, _selfDashAimTick) > 250)
                { CancelTarget(); _selfDashResult = "cancelled-aim-watchdog"; return; }
                if ((!_selfDashApproach && ElapsedMs(now, _selfDashAimTick) < 20)
                    || Hud.Game.CurrentGameTick == _selfDashGameTick
                    || (_selfDashApproach && Hud.Game.CurrentGameTick == _assistAimRefreshGameTick)) return;
                // A same-update DOWN/UP is not a reliable game-frame keypress. Reuse the
                // bounded pulse/cleanup path, keeping aim until the cast is observed.
                IPlayerSkill pressedDash = null;
                foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                    if (skill != null && skill.Key == _selfDashKey) { pressedDash = skill; break; }
                // Native availability may change while Diablo consumes the cursor move.
                if (!CanApproachDash(pressedDash))
                { CancelTarget(); _selfDashResult = "cancelled-unavailable"; return; }
                _selfDashAnimationBeforePress = Hud.Game.Me.Animation.ToString();
                _selfDashAnimationStartBeforePress = Hud.Game.Me.LoopingAnimationStartTick;
                _selfDashPressGameTick = Hud.Game.CurrentGameTick;
                if (_selfDashApproach)
                {
                    // Aim/camera collection can include the previous cast's delayed timer.
                    // Snapshot this press only after aim has reached a fresh game update.
                    var origin = Hud.Game.Me.FloorCoordinate;
                    _assistDashOriginX = origin.X; _assistDashOriginY = origin.Y; _assistDashOriginZ = origin.Z;
                    _assistDashTravelRequested = _assistDashLanding != null ? origin.XYDistanceTo(_assistDashLanding) : 0f;
                    _assistDashVerticalRequested = _assistDashLanding != null ? Math.Abs(origin.Z - _assistDashLanding.Z) : 0f;
                    _assistDashVerticalObserved = 0f;
                    _assistDashPositionGameTick = Hud.Game.CurrentGameTick;
                    _assistDashLastPositionX = origin.X; _assistDashLastPositionY = origin.Y; _assistDashLastPositionZ = origin.Z;
                    _assistDashPositionSettled = false;
                    _assistDashArcaneAtPress = _assistArcaneNearestAcd;
                    _assistDashResourceBefore = Hud.Game.Me.Stats.ResourceCurPri;
                    _assistDashResourceRequired = 0f; _assistDashChargesBefore = 0;
                    foreach (var usedSkill in Hud.Game.Me.Powers.UsedSkills)
                        if (usedSkill != null && usedSkill.Key == _selfDashKey)
                        { _assistDashResourceRequired = usedSkill.GetResourceRequirement(); _assistDashChargesBefore = usedSkill.Charges; break; }
                    _selfDashRaimentBefore = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
                    var pressBuff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
                    _selfDashBuffBefore = pressBuff != null && pressBuff.TimeLeftSeconds != null
                        ? (double[])pressBuff.TimeLeftSeconds.Clone() : new double[0];
                    _assistDashOutcome = "pressed";
                }
                if (!StartPulse(_selfDashKey, now, Math.Max(20, Math.Min(80, DashPulseMs)), false))
                { CancelTarget(); return; }
                _selfDashCastTick = now;
                _selfDashResult = "press";
                _selfDashPreviousDashTick = _lastDashTick;
                _lastDashTick = now;
                _dashAwaiting = true;
                return;
            }

            // Manual Dash retains confirmation before restoring the user's cursor.
            // Assist Dash separates owned key release from fresh cast/landing evidence.
            string animation = Hud.Game.Me.Animation.ToString();
            // A retained end animation from an earlier Dash is not evidence for this press.
            bool observed = animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0
                && (!string.Equals(animation, _selfDashAnimationBeforePress, StringComparison.Ordinal)
                    || (_selfDashApproach
                        && Hud.Game.Me.LoopingAnimationStartTick > _selfDashAnimationStartBeforePress
                        && Hud.Game.Me.LoopingAnimationStartTick >= _selfDashPressGameTick));
            // Fast recasts can reuse the same animation and refresh by less than one second.
            // Compare the known six-second Raiment timer with its decay on the game clock.
            // Other Dash buff indices retain their existing conservative confirmation rule.
            double assistTimerAge = _selfDashApproach
                ? Math.Max(0, Hud.Game.CurrentGameTick - _selfDashPressGameTick) / 60.0 : 0;
            if (_selfDashApproach
                && BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2)
                    > Math.Max(0, _selfDashRaimentBefore - assistTimerAge) + 0.1) observed = true;
            var buffNow = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
            if (buffNow != null && buffNow.TimeLeftSeconds != null)
                for (int i = 0; i < buffNow.TimeLeftSeconds.Length; i++)
                {
                    double before = _selfDashBuffBefore != null && i < _selfDashBuffBefore.Length
                        ? _selfDashBuffBefore[i] : 0;
                    if (buffNow.TimeLeftSeconds[i] > before + 1.0) observed = true;
                }
            if (_selfDashApproach && Hud.Game.CurrentGameTick > _selfDashPressGameTick)
            {
                bool freshAnimation = animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0
                    && (!string.Equals(animation, _selfDashAnimationBeforePress, StringComparison.Ordinal)
                        || (Hud.Game.Me.LoopingAnimationStartTick > _selfDashAnimationStartBeforePress
                            && Hud.Game.Me.LoopingAnimationStartTick >= _selfDashPressGameTick));
                _assistDashFreshAnimation |= freshAnimation;
                var position = Hud.Game.Me.FloorCoordinate;
                if (IsAssistPositionWorldPoint(position))
                {
                    double movedX = position.X - _assistDashOriginX, movedY = position.Y - _assistDashOriginY;
                    double movedZ = position.Z - _assistDashOriginZ;
                    _assistDashPositionObserved |= movedX * movedX + movedY * movedY + movedZ * movedZ >= 0.0625;
                    if (_assistDashPositionGameTick != Hud.Game.CurrentGameTick)
                    {
                        double stepX = position.X - _assistDashLastPositionX, stepY = position.Y - _assistDashLastPositionY;
                        double stepZ = position.Z - _assistDashLastPositionZ;
                        _assistDashPositionSettled = _assistDashPositionObserved && stepX * stepX + stepY * stepY + stepZ * stepZ < 0.0625;
                        _assistDashLastPositionX = position.X; _assistDashLastPositionY = position.Y; _assistDashLastPositionZ = position.Z;
                        _assistDashPositionGameTick = Hud.Game.CurrentGameTick;
                    }
                }
            }
            _selfDashObserved |= observed;
            bool approachInFlight = _selfDashApproach
                && Hud.Game.Me.AnimationState != AcdAnimationState.Idle
                && Hud.Game.Me.AnimationState != AcdAnimationState.Running
                && animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0
                && animation.IndexOf("_end", StringComparison.OrdinalIgnoreCase) < 0;
            if (_selfDashApproach && _assistActive)
            {
                bool movingDash = _assistDashTravelRequested > 0.75f || _assistDashVerticalRequested > 0.75f;
                var position = Hud.Game.Me.FloorCoordinate;
                bool worldPosition = IsAssistPositionWorldPoint(position);
                double actualTravel = worldPosition ? position.XYDistanceTo(_assistDashOriginX, _assistDashOriginY) : 0;
                var liveContact = FindAssistCandidate(_selfDashTargetAcd);
                bool atPlannedLanding = worldPosition && _assistDashLanding != null
                    && position.XYDistanceTo(_assistDashLanding) <= Math.Max(0.5f, Hud.Game.Me.RadiusScaled)
                    && Math.Abs(position.Z - _assistDashLanding.Z) <= 2f;
                bool verticalProgress = _assistDashVerticalRequested > 0.75f && worldPosition && _assistDashLanding != null
                    && _assistDashVerticalRequested - Math.Abs(position.Z - _assistDashLanding.Z) >= 1f;
                bool usefulTravel = (_assistDashTravelRequested > 0.75f
                    && actualTravel >= Math.Min(2.0f, _assistDashTravelRequested * 0.25f)) || verticalProgress;
                bool reachableContact = worldPosition && liveContact != null
                    && CanAttackAssistFrom(position, liveContact)
                    && RefreshAssistPositionActors() && IsAssistPositionHazardFree(position);
                // Native end can precede landing coordinates. Release our key on that
                // event, but do not classify tiny early movement as a failed Dash.
                bool nativeEnded = _assistDashFreshAnimation && !approachInFlight;
                bool freshLanding = Hud.Game.CurrentGameTick > _selfDashPressGameTick
                    && !approachInFlight && (_assistDashFreshAnimation || (_selfDashObserved && _assistDashPositionObserved))
                    && (!movingDash || atPlannedLanding || (usefulTravel && (_assistDashPositionSettled || reachableContact)));
                if ((nativeEnded || freshLanding) && _pulse != ActionKey.Unknown)
                {
                    _pulseReleaseTick = now;
                    FinishPulse(now);
                }
                if (_pulse != ActionKey.Unknown) return;
                if (_assistDashReleasedGameTick == int.MinValue) _assistDashReleasedGameTick = Hud.Game.CurrentGameTick;
                bool watchdog = ElapsedMs(now, _selfDashCastTick) >= 350;
                if (s7o_InputReleaseArbiter.HasPendingRelease)
                {
                    if (!watchdog) return;
                    _assistDashLandingConfirmed = false;
                    var releasedPosition = Hud.Game.Me.FloorCoordinate;
                    if (IsAssistPositionWorldPoint(releasedPosition))
                    { _assistPositionActualX = releasedPosition.X; _assistPositionActualY = releasedPosition.Y; }
                    ObserveAssistDashContact(releasedPosition, now);
                    _selfDashResult = "release-pending";
                    _assistDashOutcome = "release-pending:" + _assistDashOutcome;
                    _lastDashTick = _selfDashPreviousDashTick;
                    _selfDashKey = ActionKey.Unknown; _selfDashBuffBefore = null;
                    RestoreCursor();
                    return; // Owned UP continues through the shared release arbiter.
                }
                if (!freshLanding && !watchdog)
                {
                    _selfDashResult = approachInFlight ? "in-flight" : "await-native-position";
                    return; // Event-driven: leave on the first observed completion, never wait a fixed gap.
                }
                _assistDashLandingConfirmed = freshLanding;
                _selfDashResult = freshLanding ? "confirmed" : "landing-watchdog";
                var actualLanding = Hud.Game.Me.FloorCoordinate;
                if (IsAssistPositionWorldPoint(actualLanding))
                { _assistPositionActualX = actualLanding.X; _assistPositionActualY = actualLanding.Y; }
                _dashAwaiting = false;
                if (_selfDashTargetAcd == _assistTargetAcd)
                {
                    _assistApproachPending = false;
                    _assistDashRetryNeedsAttack = !freshLanding;
                }
                ObserveAssistDashContact(actualLanding, now);
                var continuingTarget = FindAssistCandidate(_selfDashTargetAcd);
                // A range-capped Dash is useful progress. Continue immediately after its
                // key release and fresh landing; do not assume a fixed 40-yard range.
                _assistDashContinuationPending = freshLanding && _assistDashActualSafe
                    && !_assistDashForHazard && !_assistDashEscapeOnly && !_assistNavigationPending
                    && continuingTarget != null && IsAssistEligible(continuingTarget)
                    && _selfDashTargetAcd == _assistTargetAcd
                    && _assistDashTravelObserved >= 2f && IsAssistPositionWorldPoint(actualLanding)
                    && _assistDashTargetDistanceBefore - actualLanding.XYDistanceTo(continuingTarget.FloorCoordinate) >= 0.5f
                    && !CanAttackAssistFrom(actualLanding, continuingTarget);
                if (_assistDashContinuationPending)
                { _assistApproachPending = true; _nextDashAimTick = now; }
                if (freshLanding && _assistDashActualSafe && _assistDashForHazard)
                {
                    ResetAssistContactRecovery();
                    _assistApproachPending = true;
                    _lastApproachDashTargetAcd = 0u;
                    _nextDashAimTick = now;
                }
                if (!freshLanding) _lastDashTick = _selfDashPreviousDashTick;
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                _assistWotHfResumePending = true;
                _assistProbeTargetAcd = 0u; // Revalidate native elite hover after camera/landing change.
                RestoreCursor();
                return;
            }
            bool manualAttackResumed = _selfDashAimIsMonster && MainGeneratorAnimation()
                && AttackedIdentityMatches(_selfDashTargetAcd, 0u);
            // Do not retain the cursor indefinitely if any shared release remains pending.
            // The arbiter continues retrying independently of this dash state.
            if (s7o_InputReleaseArbiter.HasPendingRelease && ElapsedMs(now, _selfDashCastTick) >= 350)
            {
                _selfDashResult = "release-pending";
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                RestoreCursor();
                return;
            }
            // Wait for release too: the shared arbiter retries any failed synthetic UP.
            if (_pulse == ActionKey.Unknown && !s7o_InputReleaseArbiter.HasPendingRelease
                && ((_selfDashObserved && (!_selfDashAimIsMonster || manualAttackResumed))
                    || ElapsedMs(now, _selfDashCastTick) >= (_selfDashAimIsMonster ? 700 : 350)))
            {
                _selfDashResult = _selfDashObserved
                    ? (_selfDashAimIsMonster && !manualAttackResumed ? "resume-timeout" : "confirmed")
                    : "timeout";
                if (!_selfDashObserved)
                {
                    // An unconfirmed manual press must not postpone the next required refresh
                    // for a full buff interval. Retain the existing manual aim backoff.
                    _lastDashTick = _selfDashPreviousDashTick;
                    _dashAwaiting = false;
                    _nextDashAimTick = unchecked(now + 250);
                }
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            if (!_restoreCursor) return;
            CursorPoint current;
            if (Hud != null && Hud.Window != null && Hud.Window.IsForeground
                && GetCursorPos(out current)
                && Math.Abs((long)current.X - _dashTargetX) <= 128
                && Math.Abs((long)current.Y - _dashTargetY) <= 128)
            {
                // Preserve relative mouse movement during the brief self-target transaction.
                // Returning to the old point discards fine steering; leaving it near the hero
                // after a small movement makes the cursor appear to snap toward combat.
                long x = (long)_cursorX + current.X - _dashTargetX;
                long y = (long)_cursorY + current.Y - _dashTargetY;
                if (x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue)
                    SetCursorPos((int)x, (int)y);
            }
            _restoreCursor = false;
        }

        private void Stop()
        {
            _assistMaintenanceAttempts.Clear();
            _lastAssistMaintenanceSno = 0u;
            _autoLootYieldPending = _autoLootHazardWaiting = false;
            ReleaseAssistAttack();
            CancelTarget();
            EndAssist();
            _mainKey = ActionKey.Unknown;
            _candidateKey = ActionKey.Unknown;
            _dashAwaiting = false;
            RestoreCursor();
        }

        private static int ElapsedMs(int now, int then)
        {
            if (then == int.MinValue) return int.MaxValue;
            return unchecked((int)(uint)(now - then));
        }

        private static bool Due(int now, int target) { return unchecked(now - target) >= 0; }
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);
    }

    internal static class s7o_GenMonkInput
    {
        private static readonly Dictionary<ActionKey, int> OwnedActions = new Dictionary<ActionKey, int>();
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
                var hud = s7o_GenMonkInputContext.Hud;
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

        public static bool IsDown(ActionKey key)
        {
            int code = VirtualKey(key);
            return code != 0 && (GetAsyncKeyState(code) & 0x8000) != 0;
        }

        public static bool IsVirtualKeyDown(ushort key)
        {
            return key != 0 && (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        public static bool IsStandstillDown()
        {
            var autoSkill = ResolveAutoSkill();
            return IsVirtualKeyDown(autoSkill != null ? autoSkill.ForceStandstillVirtualKey : (ushort)0x10);
        }

        public static ushort StandstillVirtualKey()
        {
            var autoSkill = ResolveAutoSkill();
            return autoSkill != null ? autoSkill.ForceStandstillVirtualKey : (ushort)0x10;
        }

        public static bool SendStandstill(string owner, ushort code, bool up)
        {
            if (code == 0 || code == 0x01 || code == 0x02) return false;
            Input input = new Input();
            input.Type = 1u; input.U.Keyboard.VirtualKey = code;
            uint extended = code == 0xA3 || code == 0xA5 ? 1u : 0u;
            input.U.Keyboard.Flags = extended | (up ? 2u : 0u);
            Input[] packet = new[] { input };
            Func<bool> emit = () => SendInput(1u, packet, InputSize) == 1u;
            return up ? s7o_InputReleaseArbiter.Up(owner, code, emit)
                : s7o_InputReleaseArbiter.Down(owner, code, emit);
        }

        public static bool Owns(ActionKey key) { return OwnedActions.ContainsKey(key); }
        public static bool Down(string owner, ActionKey key) { return Send(owner, key, false); }
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

        private static bool Send(string owner, ActionKey key, bool up)
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
            if (!up && IsDown(key)) return false; // The user's held input keeps ownership.
            // Final boundary: also covers a secondary generator or Dash equipped on LMB.
            if (!up && key == ActionKey.LeftSkill
                && (s7o_GenMonkInputContext.CanPressLeftSkill == null
                    || !s7o_GenMonkInputContext.CanPressLeftSkill())) return false;

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
            Input[] packet = new[] { input };
            Func<bool> emit = () => SendInput(1u, packet, InputSize) == 1u;

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

    internal static class s7o_GenMonkInputContext
    {
        public static IController Hud;
        public static Func<bool> CanPressLeftSkill;
    }
}

