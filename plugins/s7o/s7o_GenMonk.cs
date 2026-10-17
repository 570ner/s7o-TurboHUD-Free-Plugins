using System;
using System.Linq;
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
        // Buff upkeep is not navigation: stationary renewal never resets wall recovery.
        private bool _selfDashUpkeep, _dashUpkeepNeedsAttack;
        private int _dashConfirmedGameTick = int.MinValue, _dashUpkeepAttemptGameTick = int.MinValue;
        private uint _dashConfirmedWorld;
        private string _dashDeadlineReason = "none", _mapLandingReason = "none";
        private bool _panicMustAttack;
        private int _panicAttackAfterGameTick = int.MinValue;
        private readonly List<IActor> _lowHealthFields = new List<IActor>(32);
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
        private string _assistMapPlanReason = "none";
        private int _assistExplosionRunStage, _assistExplosionRunAimTick;
        private int _assistExplosionAvailabilityTick = int.MinValue;
        private uint _assistExplosionAvailabilityWorld;
        private int _assistExplosionWalkLeadTicks;
        private bool _assistExplosionWalkCommitted;
        private IWorldCoordinate _assistExplosionRunPoint;
        // Walking hover failures are independent of physically failed Dash landings.
        private readonly List<IWorldCoordinate> _assistEscapeWalkFailures = new List<IWorldCoordinate>(16);
        private IWorldCoordinate _assistEscapeWalkOrigin, _assistEscapeWalkProgressPoint;
        private uint _assistEscapeWalkWorld;
        private int _assistEscapeWalkStartedTick, _assistEscapeWalkProgressTick, _assistEscapeWalkRetryTick;
        private bool _assistEscapeWalkRetryPending;
        private string _assistEscapeWalkReason = "none";

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
        private uint _assistRetargetAcd, _assistRetargetAnn;
        private int _assistRetargetResumeUntil;
        private int _assistRetargetAimGameTick = int.MinValue;
        private bool _assistRetargetRecoverySpent;
        private IWorldCoordinate _assistRetargetOrigin, _assistRetargetTargetPoint;
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
        private readonly List<IWorldCoordinate> _assistTravelHistory = new List<IWorldCoordinate>(64);
        private IWorldCoordinate _assistTravelAnchor;
        private uint _assistTravelWorld;
        private int _assistTravelSampleTick = int.MinValue;
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
        private bool _assistSafetyCombatPending, _assistSafetyCombatOnly;
        private const double AssistVisibleTargetRange = 90.0; // Visible acquisition is independent of one Dash hop.
        private const double AssistDashTravelLimit = 50.0; // Check landing travel independently of target range.
        private const double AssistDoorApproachRange = 70.0; // Discovery range; each confirmed hop still respects the Dash limit.
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
        private bool _assistOtherEliteLeaderPresent;
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
        private struct AssistBlockedTarget
        {
            public float PlayerX, PlayerY, TargetX, TargetY;
            public uint World;
            public bool NoRoute, NoProgress, NavigationTracked;
            public int NavigationSinceTick;
            public float NavigationX, NavigationY, NavigationZ;
        }

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
            (uint)ActorSnoEnum._x1_sniperangel_a,
            (uint)ActorSnoEnum._fallenshaman_d,
            (uint)ActorSnoEnum._ghost_a,
            (uint)ActorSnoEnum._goatman_shaman_b,
            (uint)ActorSnoEnum._fallenshaman_a,
            (uint)ActorSnoEnum._fallenshaman_b,
            (uint)ActorSnoEnum._fallenshaman_c,
            (uint)ActorSnoEnum._ghost_b,
            (uint)ActorSnoEnum._ghost_c,
            (uint)ActorSnoEnum._ghost_d,
            (uint)ActorSnoEnum._x1_squigglet_a,
            (uint)ActorSnoEnum._p73_squigglet_a,
            (uint)ActorSnoEnum._p76_squigglet_a,
            (uint)ActorSnoEnum._p4_setdung_squigglet_a,
            (uint)ActorSnoEnum._triunesummoner_a,
            (uint)ActorSnoEnum._triunesummoner_b,
            (uint)ActorSnoEnum._triunesummoner_c,
            (uint)ActorSnoEnum._triunesummoner_d,
            (uint)ActorSnoEnum._morluspellcaster_a,
            (uint)ActorSnoEnum._morluspellcaster_b,
            (uint)ActorSnoEnum._morluspellcaster_d,
            (uint)ActorSnoEnum._snakeman_caster_a,
            (uint)ActorSnoEnum._snakeman_caster_b,
            (uint)ActorSnoEnum._snakeman_caster_c,
            (uint)ActorSnoEnum._x1_pandext_rockfodder_ranged_a,
            (uint)ActorSnoEnum._p4_sandwasp_a,
            (uint)ActorSnoEnum._quilldemon_fastattack_a,
            (uint)ActorSnoEnum._p73_quilldemon_a,
            (uint)ActorSnoEnum._p76_quilldemon_a,
            (uint)ActorSnoEnum._p76_quilldemon_a_bog,
            (uint)ActorSnoEnum._goatmutant_ranged_a,
            (uint)ActorSnoEnum._goatmutant_ranged_b,
            (uint)ActorSnoEnum._x1_bogfamily_ranged_a,
            (uint)ActorSnoEnum._goatman_ranged_a,
            (uint)ActorSnoEnum._goatman_ranged_b,
            (uint)ActorSnoEnum._goatman_ranged_c,
            (uint)ActorSnoEnum._goatman_shaman_a,
            (uint)ActorSnoEnum._goatman_shaman_c,
            (uint)ActorSnoEnum._goatmutant_shaman_a,
            (uint)ActorSnoEnum._goatmutant_shaman_b,
            (uint)ActorSnoEnum._quilldemon_a,
            (uint)ActorSnoEnum._quilldemon_b,
            (uint)ActorSnoEnum._quilldemon_d,
            (uint)ActorSnoEnum._quilldemon_c,
            (uint)ActorSnoEnum._sandwasp_a,
            (uint)ActorSnoEnum._sandwasp_b,
            (uint)ActorSnoEnum._sandwasp_c,
            (uint)ActorSnoEnum._sandwasp_d,
            (uint)ActorSnoEnum._skeletonarcher_a,
            (uint)ActorSnoEnum._skeletonarcher_b,
            (uint)ActorSnoEnum._skeletonarcher_c,
            (uint)ActorSnoEnum._skeletonarcher_d,
            (uint)ActorSnoEnum._skeletonsummoner_a,
            (uint)ActorSnoEnum._skeletonsummoner_b,
            (uint)ActorSnoEnum._skeletonsummoner_c,
            (uint)ActorSnoEnum._skeletonsummoner_d,
            (uint)ActorSnoEnum._succubus_a,
            (uint)ActorSnoEnum._goatman_shaman_b_unique,
            (uint)ActorSnoEnum._ghost_a_unique_house1000undead,
            (uint)ActorSnoEnum._succubus_b,
            (uint)ActorSnoEnum._ghost_a_unique_chancellor,
            (uint)ActorSnoEnum._ghost_a_unique_chancellor_spawner,
            (uint)ActorSnoEnum._ghost_d_unique01,
            (uint)ActorSnoEnum._fallenshaman_a_unique01,
            (uint)ActorSnoEnum._goatman_shaman_b_unique_mysticwagon,
            (uint)ActorSnoEnum._skeletonarcher_e,
            (uint)ActorSnoEnum._skeletonarcher_f,
            (uint)ActorSnoEnum._skeletonsummoner_e,
            (uint)ActorSnoEnum._quilldemon_a_unique_loothoarderleader,
            (uint)ActorSnoEnum._fallenshaman_a_unique_minipools,
            (uint)ActorSnoEnum._ghost_a_unique_01,
            (uint)ActorSnoEnum._succubus_a_unique_01,
            (uint)ActorSnoEnum._fallenshaman_a_unique01whipple,
            (uint)ActorSnoEnum._skeletonarcher_a_unique_01,
            (uint)ActorSnoEnum._ghost_a_unique_02,
            (uint)ActorSnoEnum._goatman_ranged_a_unique_01,
            (uint)ActorSnoEnum._goatman_shaman_a_unique_01,
            (uint)ActorSnoEnum._succubus_c,
            (uint)ActorSnoEnum._succubus_c_unique_01,
            (uint)ActorSnoEnum._quilldemon_c_unique_01,
            (uint)ActorSnoEnum._skeletonarcher_e_unique_01,
            (uint)ActorSnoEnum._goatmutant_shaman_a_unique_01,
            (uint)ActorSnoEnum._succubus_a_unique_02,
            (uint)ActorSnoEnum._goatmutant_shaman_b_unique_01,
            (uint)ActorSnoEnum._goatmutant_shaman_b_unique_02,
            (uint)ActorSnoEnum._ghost_d_unique_01,
            (uint)ActorSnoEnum._x1_skeletonarcher_westmarch_a,
            (uint)ActorSnoEnum._x1_skeletonarcher_westmarch_ghost_a,
            (uint)ActorSnoEnum._x1_skeletonarcher_fire_a,
            (uint)ActorSnoEnum._x1_tentacle_goatman_shaman_a,
            (uint)ActorSnoEnum._x1_tentacle_goatman_ranged_a,
            (uint)ActorSnoEnum._x1_skeletonarcher_westmarch_unique_a,
            (uint)ActorSnoEnum._goatman_shaman_b_unique_01,
            (uint)ActorSnoEnum._goatman_shaman_c_unique_01,
            (uint)ActorSnoEnum._goatman_shaman_c_unique_02,
            (uint)ActorSnoEnum._fallenshaman_c_unique_01,
            (uint)ActorSnoEnum._p4_ice_porcupine,
            (uint)ActorSnoEnum._p4_ice_goatman_shaman_c,
            (uint)ActorSnoEnum._p4_ice_goatman_ranged_c,
            (uint)ActorSnoEnum._skeletonsummoner_c_unique001,
            (uint)ActorSnoEnum._skeletonarcher_c_unique001,
            (uint)ActorSnoEnum._goatman_ranged_a_unique_04,
            (uint)ActorSnoEnum._goatman_shaman_a_unique_04,
            370u, 4196u, 4197u, 4198u, 276492u, 351023u, 309114u, 294969u,
            418911u, 418918u, 418922u, 418923u, 430947u, 487357u, 488560u,
            5371u, 5372u, 5368u, 5376u, 5382u, 5367u, 5375u, 5381u, 487356u, 488587u
        };
        private static readonly HashSet<string> AssistRangedThreatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Barbed Lurker", "Morlu Incinerator", "Serpent Magus", "Dark Summoner", "Bogan Trapper", "Exarch",
            "Blazing Guardian", "Chilling Construct", "Smoldering Construct", "Charged Construct", "Toxic Construct",
            "Frost Guardian", "Shock Guardian", "Noxious Guardian", "Enraged Phantom", "Enraged Phantasm",
            "Vile Revenant", "Deathly Haunt", "Grim Wraith", "Vengeful Phantasm", "Shadow of Death",
            "Shipwrecked Soul", "Vile Echo", "Moon Clan Impaler", "Skeletal Archer", "Returned Archer"
        };
        private static readonly uint[] AttackIdentityModifiers = { 0u, 0xFFFFFu, uint.MaxValue, 2147483647u };
        // Native HUD circles use 10 yards; keep a half-yard inset.
        private const float AssistCircleLandingRadius = 9.5f;
        private const float AssistCircleTrialReachYards = 10.0f;
        private const float AssistCircleTargetGapYards = AssistCircleLandingRadius + AssistCircleTrialReachYards;
        private const float AssistCircleOuterReachYards = 8.0f;
        // Attack reach, circle coverage and navigation progress are independent contracts.
        private const float AssistAttackReachYards = 8.0f;
        private const float AssistCircleCoverageRadius = 10.0f;
        private const float AssistNavigationRetryYards = 10.0f;
        private const float AssistMoltenAvoidanceRadius = 25.0f; // 18-yard blast plus seven-yard clearance.
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
        { public int Count; public float PlayerX, PlayerY, DoorX, DoorY; public uint World, DetourEliteAcd; }
        private readonly Dictionary<uint, AssistDoorFailure> _assistDoorFailures = new Dictionary<uint, AssistDoorFailure>();
        private IActor _assistDoorTarget;
        private bool _assistRecoveryCombatOnly, _assistDoorCombatPaused;
        private int _assistDoorDashFailures;
        private int _assistDoorStage, _assistDoorSinceTick, _assistDoorAimGameTick;
        private bool _assistDoorDashSpent;
        // Door travel owns a connected near-side route until interaction or recovery.
        // Combat shortcuts must not replace this goal; moving away can be a valid detour.
        private string _assistDoorPathState = "none", _assistDoorPathReason = "none";
        private IWorldCoordinate _assistDoorApproachPoint, _assistDoorWaypoint;
        private double _assistDoorRemaining = double.NaN, _assistDoorBestRemaining = double.NaN;
        private int _assistDoorWalkStage, _assistDoorWalkAimTick;
        private IWorldCoordinate _assistDoorWalkPoint;
        private int _assistRouteWalkStage, _assistRouteWalkAimTick;
        private IWorldCoordinate _assistRouteWalkPoint;
        private string _assistRouteWalkReason = "none";
        private long _assistRouteWalkEpoch;
        private IWorldCoordinate _assistRouteWalkFailedPoint, _assistRouteWalkFailedOrigin;
        private uint _assistRouteWalkFailedWorld;
        private long _assistDoorRouteEpoch;
        private string _assistDoorWalkReason = "none";
        private int _assistDoorProgressGameTick = int.MinValue;
        private float _assistDoorNativeOriginX, _assistDoorNativeOriginY;
        private double _assistDoorNativeMaxProgress;
        private IWorldCoordinate _assistDoorDashWaypoint;
        private double _assistDoorDashRemaining = double.NaN;
        private bool _assistDoorEliteDetour, _assistDoorReturnPending;
        private uint _assistDoorReturnEliteAcd;
        private IWorldCoordinate _assistDoorReturnPoint;
        // Door discovery is not click permission: exact native hover authorizes interaction.
        private int _assistDoorRouteGameTick = int.MinValue;
        private uint _assistDoorRouteTarget;
        private int _assistDoorAimX, _assistDoorAimY;
        private float _assistDoorBeforeHp;
        private float _assistDoorProgressX, _assistDoorProgressY;
        private bool _assistDoorClickAuthorized;
        private string _assistDoorStatus = "none";
        private string _assistDoorLastResult = "none";
        private uint _assistDoorCompletedAcd;
        private int _assistDoorCompletedGameTick = int.MinValue;
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
        private uint _assistHazardReturnWorld;
        private int _assistHazardReturnTick;
        private void RememberAssistHazardReturn(IMonster target)
        {
            if (!IsAssistPositionElite(target)) return;
            if (_assistHazardReturnAcd!=0 && _assistHazardReturnAcd!=target.AcdId
                && _assistHazardReturnWorld==Hud.Game.Me.WorldId
                && unchecked(Environment.TickCount-_assistHazardReturnTick)<30000) return;
            _assistHazardReturnAcd=target.AcdId;
            _assistHazardReturnWorld=Hud.Game.Me.WorldId;
            _assistHazardReturnTick=Environment.TickCount;
        }
        private IMonster ResolveAssistHazardReturn(int now)
        {
            if (_assistHazardReturnAcd==0) return null;
            if (_assistHazardReturnWorld!=Hud.Game.Me.WorldId || unchecked(now-_assistHazardReturnTick)>=30000)
            { _assistHazardReturnAcd=0; return null; }
            // Retain identity through transient visibility loss; never chase stale coordinates.
            foreach(var monster in Hud.Game.Monsters)
                if(monster.AcdId==_assistHazardReturnAcd && !monster.IsAlive)
                { _assistHazardReturnAcd=0; return null; }
            return FindAssistCandidate(_assistHazardReturnAcd);
        }


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
            _dashConfirmedGameTick = _dashUpkeepAttemptGameTick = int.MinValue;
            _dashConfirmedWorld = 0u;
            _panicMustAttack = false; _panicAttackAfterGameTick = int.MinValue;
            _lowHealthFields.Clear();
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

        public void AfterCollect() { AfterCollectCore(); }


        private void AfterCollectCore()
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
            // Required buffs outrank travel retries and secondary generator pulses.
            if (TryAssistDashUpkeep(dash, now)) return;
            if (_assistScopedStandstill != 0 && !IsAssistCorpseOverlap()
                && !ShouldHoldAssistPosition(FindAssistCandidate(_assistTargetAcd))
                && !IsAssistForcedPylonAttackSafe())
                ReleaseAssistScopedStandstill();
            // A corpse is a combat overlap, not an instruction to idle at the same aim.
            // Other unsafe clicks retain the existing stop/release behavior.
            if (s7o_GenMonkInput.Owns(ActionKey.LeftSkill) && !IsLeftClickSafe())
            {
                bool corpseOverlap = IsAssistCorpseOverlap();
                _restoreCursor = false;
                ReleaseAssistAttack();
                if (_pulse == ActionKey.LeftSkill) CancelTarget();
                bool canProbeOverlap = !_assistProbeExhausted
                    && IsAssistEligible(FindAssistCandidate(_assistProbeTargetAcd));
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
            bool forcedPylonAttack = IsAssistForcedPylonAttackSafe();
            if (hovered != null && !attackableMonster && !corpseAttack && !forcedPylonAttack
                && (hovered.IsClickable || hovered.GizmoType == GizmoType.Portal
                    || hovered.GizmoType == GizmoType.BossPortal
                    || (hovered.SnoActor != null && (hovered.SnoActor.Kind == ActorKind.Shrine
                        || hovered.SnoActor.Kind == ActorKind.Portal)))) return false;
            // Around visible unused pylons, frame advancement alone is not click permission.
            return !_pylonNeedsMonsterHover || (attackableMonster && monster.IsSelected) || corpseAttack || forcedPylonAttack;
        }

        private bool IsLeftClickPointSafe(double x, double y)
        {
            if (!_clickUiReadable || !_pylonGuardReadable || x < 0 || y < 0
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
            // Proximity is a placement preference. Actual clicks retain UI/world guards.
            var screen = target.FloorCoordinate.ToScreenCoordinate(true, true);
            if (screen == null || double.IsNaN(screen.X) || double.IsNaN(screen.Y)
                || double.IsInfinity(screen.X) || double.IsInfinity(screen.Y)) return false;
            double aimX = screen.X, aimY = screen.Y;
            if (_assistActive && target.AcdId == _assistProbeTargetAcd
                && (IsAssistPositionElite(target) || _assistNavigationPending || _pylonNeedsMonsterHover) && _assistProbePhase > 0)
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
            bool retargetConfirmed = _pulseIsRetarget && AttackedIdentityMatches(_assistRetargetAcd, _assistRetargetAnn);
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
                if (retargetEnded)
                {
                    _assistRetargetResumeUntil = unchecked(now + 500);
                    ReleaseAssistAttack(ShouldHoldAssistPosition(FindAssistCandidate(_assistRetargetAcd)));
                }
            }
            _nextAttemptTick = unchecked(now + Math.Max(50, _targetForesight
                ? ForesightRetryGapMs : GeneratorRetryGapMs));
            // A self-dash must not restore its aim just because the keypress ended:
            // Diablo can still be consuming the queued cast. AdvanceSelfDash observes it.
            if (_selfDashKey == ActionKey.Unknown && !retargetEnded) RestoreCursor();
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
            _selfDashUpkeep = false;
            _selfDashCircleUpgrade = false;
            _selfDashBuffBefore = null;
            _assistDashLanding = null;
            _assistDashEscapeOnly = false;
            _assistDashLandingWorldId = 0u;
            if (_pulse != ActionKey.Unknown)
                s7o_GenMonkInput.Up(Owner, _pulse);
            _pulse = ActionKey.Unknown;
            _pulseIsMaintenance = _pulseIsRetarget = false;
            _assistRetargetAcd = _assistRetargetAnn = 0u;
            _assistRetargetResumeUntil = 0;
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
                && Hud.Game.Me.FloorCoordinate.XYDistanceTo(monster.FloorCoordinate) <= ((monster.IsElite || monster.Rarity == ActorRarity.RareMinion || IsAssistLeader(monster))
                    ? AssistVisibleTargetRange : Math.Min(AssistVisibleTargetRange, 70.0))
                ;
        }

        private bool IsAssistEligible(IMonster monster)
        {
            return (IsAssistMonsterValid(monster) && IsAssistVisiblePoint(monster.FloorCoordinate)
                && MapAssistTargetOnWalkableFloor(monster));
        }

        private static bool IsAssistShockTower(IMonster monster)
        {
            return monster != null && monster.SnoActor != null
                && (uint)monster.SnoActor.Sno == 322194u;
        }


        private bool _rangedCleanup, _rangedManual;
        private int _lastPanicContactTick=int.MinValue;
        private uint _lastPanicContactTarget;
        private int _rangedStarted, _rangedEmptySince = int.MinValue, _rangedSuppressUntil;
        private uint _rangedWorld, _rangeTrialTarget, _rangeRejectedTarget;
        private int _rangeTrialTick;
        private double _rangeTrialHealth;
        private string _rangeTrialStatus = "none";
        private readonly Dictionary<uint,int> _circleFailedAt = new Dictionary<uint,int>();
        private readonly HashSet<uint> _circleRetried = new HashSet<uint>();

        private string _rangedExitReason="none";

        private readonly Queue<int> _assistMovementCasts = new Queue<int>();
        private readonly Dictionary<uint, int> _assistEnteredDamageCircles = new Dictionary<uint, int>();
        private uint _assistMovementWorld, _assistMovementTarget;
        private int _assistMovementLastTick=int.MinValue, _assistMovementGameTick=int.MinValue;
        private int _assistGeometryCheckTick=int.MinValue;
        private IWorldCoordinate _assistMovementOrigin, _assistMovementTargetPoint;
        private bool _assistMovementWasEscape;
        private string _assistMovementDecision="none";
        private IWorldCoordinate _rangeRejectedPoint, _rangeRejectedTargetPoint;
        private uint _rangeRejectedWorld;
        private int _rangeRejectedAt;

        private void ResetAssistMovementPolicy()
        {
            _assistMovementCasts.Clear(); _assistEnteredDamageCircles.Clear();
            _assistMovementWorld=Hud.Game==null || Hud.Game.Me==null ? 0u : Hud.Game.Me.WorldId;
            _assistMovementLastTick=_assistMovementGameTick=_assistGeometryCheckTick=int.MinValue;
            _assistMovementOrigin=_assistMovementTargetPoint=null;
            _assistMovementTarget=0; _assistMovementWasEscape=false;
            _assistMovementCircle=0;_assistFailedCircleContacts.Clear();_assistCircleRouteRetries.Clear();
            _assistMovementDecision="none";
            _assistCircleReturnElite = _assistCircleReturnCircle = 0u;
            _assistDensityFocusTick = _assistDensityScanTick = int.MinValue;
        }
        private readonly Dictionary<ulong,IWorldCoordinate> _assistFailedCircleContacts=new Dictionary<ulong,IWorldCoordinate>();
        private readonly HashSet<ulong> _assistCircleRouteRetries=new HashSet<ulong>();
        private uint _assistMovementCircle;
        private bool IsAssistFailedCircleContact(uint circle, IMonster target)
        {
            if(circle==0 || target==null)return false;
            ulong key=((ulong)target.AcdId<<32)|circle;
            IWorldCoordinate previous;
            if(!_assistFailedCircleContacts.TryGetValue(key,out previous))return false;
            if(target.FloorCoordinate.XYDistanceTo(previous)>=3)
            { _assistFailedCircleContacts.Remove(key);return false; }
            return true;
        }
        private bool TryReopenAssistCircleRoute(IMonster target,bool canDash)
        {
            AssistBlockedTarget failure;
            if(!canDash || !IsAssistPositionElite(target) || !_assistBlockedTargets.TryGetValue(target.AcdId,out failure)
                || !failure.NoProgress || failure.NoRoute || !RefreshAssistPositionActors())return false;
            IWorldCoordinate point; string reason;
            if(!TryPickAssistCircleUpgrade(target,out point,out reason)
                || AssistBodyGap(point,target)>Math.Min(AssistAttackReachYards,MeleeAssistRange)
                || point.XYDistanceTo(failure.PlayerX,failure.PlayerY)<3)return false;
            uint circle=AssistLandingDamageCircle(point);
            ulong retryKey=((ulong)target.AcdId<<32)|circle;
            if(circle==0 || _assistCircleRouteRetries.Contains(retryKey))return false;
            if(_assistCircleRouteRetries.Count>=64)return false;
            _assistCircleRouteRetries.Add(retryKey);
            _assistBlockedTargets.Remove(target.AcdId);
            return true;
        }

        private bool IsAssistCircleContactRejected(IWorldCoordinate point, IMonster target)
        {
            return target != null && target.AcdId==_rangeRejectedTarget
                && Hud.Game.Me.WorldId==_rangeRejectedWorld && _rangeRejectedPoint!=null
                && _rangeRejectedTargetPoint!=null && unchecked(Environment.TickCount-_rangeRejectedAt)<3000
                && AssistBodyGap(point,target)>Math.Min(AssistAttackReachYards,MeleeAssistRange)
                && target.FloorCoordinate.XYDistanceTo(_rangeRejectedTargetPoint)<3;
        }
        private static int AssistRangedDanger(IMonster monster)
        {
            if(monster==null || monster.SnoActor==null)return 0;
            switch(monster.SnoActor.Sno) {
                case ActorSnoEnum._ghost_a: case ActorSnoEnum._ghost_b:
                case ActorSnoEnum._ghost_c: case ActorSnoEnum._ghost_d:
                case ActorSnoEnum._x1_ghost_a: case ActorSnoEnum._x1_ghost_b:
                case ActorSnoEnum._x1_ghost_dark_a: case ActorSnoEnum._x1_ghost_deathspirit:
                case ActorSnoEnum._p1_lr_ghost_a: case ActorSnoEnum._p1_lr_ghost_b:
                case ActorSnoEnum._p1_lr_ghost_c: case ActorSnoEnum._p1_lr_ghost_d:
                case ActorSnoEnum._p4_ghost_c: case ActorSnoEnum._p73_ghost_c: case ActorSnoEnum._p76_ghost_c:
                case ActorSnoEnum._skeletonmage_fire_a: case ActorSnoEnum._skeletonmage_fire_b:
                case ActorSnoEnum._p73_skeletonmage_fire_a: case ActorSnoEnum._p76_skeletonmage_fire_a:
                case ActorSnoEnum._x1_tentacle_goatman_ranged_a: case ActorSnoEnum._p4_ice_goatman_ranged_c:
                case ActorSnoEnum._goatman_ranged_a: case ActorSnoEnum._goatman_ranged_b:
                case ActorSnoEnum._goatman_ranged_c: case ActorSnoEnum._goatmutant_ranged_a:
                case ActorSnoEnum._goatmutant_ranged_b: case ActorSnoEnum._x1_bogfamily_ranged_a:
                    return 3;
                case ActorSnoEnum._morluspellcaster_a: case ActorSnoEnum._morluspellcaster_b:
                case ActorSnoEnum._morluspellcaster_d: return 2;
            }
            string name=monster.SnoMonster==null ? null : monster.SnoMonster.NameEnglish;
            if(name=="Blazing Guardian" || name=="Smoldering Construct" || name=="Moon Clan Impaler"
                || name=="Blood Clan Spearman" || name=="Enraged Phantom" || name=="Enraged Phantasm"
                || name=="Vile Revenant" || name=="Deathly Haunt" || name=="Grim Wraith"
                || name=="Vengeful Phantasm" || name=="Shadow of Death" || name=="Shipwrecked Soul"
                || name=="Vile Echo")return 3;
            return 1;
        }
        private uint AssistLandingDamageCircle(IWorldCoordinate point)
        {
            if(point==null)return 0;
            foreach(var c in _assistPositionCircles)
                if(c.Kind!=2 && c.Actor!=null && !c.Actor.IsDisabled
                    && c.Actor.WorldId==Hud.Game.Me.WorldId && AssistSameHeight(point,c.Actor.FloorCoordinate)
                    && point.XYDistanceTo(c.Actor.FloorCoordinate)<=AssistCircleLandingRadius)return c.Actor.AcdId;
            return 0;
        }
        private bool AssistMovementEmergency(bool escape)
        {
            if (!escape || AssistShieldPylonActive()) return false;
            // Confirmed blast/core danger is not discretionary positioning.
            return (AssistMoltenEscapeDue() && _assistPositionEscapeReason != "molten-ground")
                || _assistArcaneCoreUnsafe || _assistOrbiterCoreUnsafe;
        }
        private int _assistObstacleAttempts;
        private uint _assistObstacleWorld;
        private IWorldCoordinate _assistObstacleOrigin, _assistObstacleGoal;
        private bool AssistObstacleRecoveryRequired(IMonster target)
        {
            var me=Hud.Game.Me.FloorCoordinate;
            if (_assistObstacleWorld!=Hud.Game.Me.WorldId || _assistObstacleOrigin==null
                || me.XYDistanceTo(_assistObstacleOrigin)>=12)
            { _assistObstacleAttempts=0; _assistObstacleGoal=null; return false; }
            if (target==null || _assistObstacleGoal==null
                || target.FloorCoordinate.XYDistanceTo(_assistObstacleGoal)>18) return false;
            if (HasAssistAttackContact(target) && !MapAssistTargetBlocked(target))
            { _assistObstacleAttempts=0; _assistObstacleGoal=null; return false; }
            return _assistObstacleAttempts>=2;
        }
        private void RecordAssistObstacleAttempt(IMonster target,IWorldCoordinate actual,bool failed)
        {
            if (_assistDashForHazard || _assistDashEscapeOnly || target==null) return;
            if (!failed)
            {
                if (_assistDashTravelObserved>=8 && actual.XYDistanceTo(_assistDashLanding)<=2)
                { _assistObstacleAttempts=0; _assistObstacleGoal=null; }
                return;
            }
            bool same=_assistObstacleOrigin!=null && _assistObstacleGoal!=null
                && _assistObstacleWorld==Hud.Game.Me.WorldId
                && actual.XYDistanceTo(_assistObstacleOrigin)<12
                && target.FloorCoordinate.XYDistanceTo(_assistObstacleGoal)<=18;
            if (!same)
            {
                _assistObstacleAttempts=0; _assistObstacleWorld=Hud.Game.Me.WorldId;
                _assistObstacleOrigin=actual.Offset(0,0,0);
                _assistObstacleGoal=target.FloorCoordinate.Offset(0,0,0);
            }
            _assistObstacleAttempts=Math.Min(2,_assistObstacleAttempts+1);
        }
        private bool AssistUsefulApproachHop(IWorldCoordinate point,IMonster target)
        {
            return point!=null && (Hud.Game.Me.FloorCoordinate.XYDistanceTo(point)>=4
                || (CanAttackAssistFrom(point,target) && !MapAssistAttackBlocked(point,target)));
        }

        private bool AllowAssistMovement(IMonster target, IWorldCoordinate landing, int now, bool escape, bool upkeep, bool circleUpgrade = false)
        {
            _assistMovementDecision = "allowed";
            if (!_assistActive || landing == null) return true;
            if (_assistMovementWorld != Hud.Game.Me.WorldId) ResetAssistMovementPolicy();
            var me = Hud.Game.Me.FloorCoordinate;
            bool emergency = AssistMovementEmergency(escape);
            if (emergency) { _assistMovementDecision = "verified-hazard-escape"; return true; }
            // Raiment renewal is a required combat buff, not optional repositioning.
            if (upkeep) { _assistMovementDecision = "required-dash-buff"; return true; }
            if (!escape && AssistObstacleRecoveryRequired(target))
            { _assistMovementDecision="obstacle-recovery-walk-or-retarget"; return false; }
            while (_assistMovementCasts.Count > 0 && unchecked(now - _assistMovementCasts.Peek()) >= 2000)
                _assistMovementCasts.Dequeue();
            bool contact = target != null && CanAttackAssistFrom(me, target)
                && !MapAssistAttackBlocked(me, target);
            bool landingContact = target != null && CanAttackAssistFrom(landing, target)
                && !MapAssistAttackBlocked(landing, target);
            int since = _assistMovementLastTick == int.MinValue ? int.MaxValue
                : unchecked(now - _assistMovementLastTick);
            bool targetMoved = target != null && target.AcdId == _assistMovementTarget
                && _assistMovementTargetPoint != null
                && target.FloorCoordinate.XYDistanceTo(_assistMovementTargetPoint) >= 3;
            uint circle = AssistLandingDamageCircle(landing);
            // At critical HP, preserve local healing instead of relocating for damage.
            // A safe circle may still restore missing contact; real escapes take priority.
            if (!escape && circle != 0 && _assistHealthFraction <= 0.40
                && !AssistShieldPylonActive()
                && (contact || !landingContact
                    || HasAssistAttackContact(FindAssistCandidate(_assistTargetAcd))))
            {
                _assistMovementDecision = "critical-healing-contact";
                
                return false;
            }
            int enteredAt;
            if (circle != 0 && _assistEnteredDamageCircles.TryGetValue(circle, out enteredAt)
                && unchecked(now - enteredAt) >= 2000) _assistEnteredDamageCircles.Remove(circle);
            bool freshCircleEntry = circleUpgrade && circle != 0 && AssistLandingDamageCircle(me) != circle
                && landingContact && IsAssistPositionHazardFree(landing)
                && (!_assistEnteredDamageCircles.TryGetValue(circle, out enteredAt)
                    || unchecked(now - enteredAt) >= 900);
            bool firstCircle = freshCircleEntry || (circle != 0
                && !_assistEnteredDamageCircles.ContainsKey(circle)
                && landingContact && IsAssistPositionHazardFree(landing));
            // A recently visited circle is not new on each target switch.
            if (circle != 0 && _assistEnteredDamageCircles.TryGetValue(circle,out enteredAt)
                && unchecked(now-enteredAt)<2000) firstCircle=false;
            bool reverse = _assistMovementOrigin != null && since < 2000
                && me.XYDistanceTo(_assistMovementOrigin) > 6
                && landing.XYDistanceTo(_assistMovementOrigin) < 3;
            if (reverse && !targetMoved && !firstCircle
                && (escape || contact || !landingContact))
            { _assistMovementDecision = "reverse-held";  return false; }
            if (_assistMovementWasEscape && reverse && !IsAssistPositionHazardFree(landing))
            { _assistMovementDecision = "hazard-return-held";  return false; }
            if(!escape && firstCircle && !targetMoved
                && (IsAssistFailedCircleContact(circle,target)
                    || (_assistMovementCasts.Count>=2 && since<1200
                        && _assistWotHfLastGameTick<=_assistMovementGameTick)))
            { _assistMovementDecision="circle-contact-recovery";  return false; }
            // Valid new damage positions outrank ordinary movement limits, never true hazards.
            if (firstCircle) { _assistMovementDecision = "new-damage-circle"; return true; }
            if (escape)
            {
                // Ordinary field avoidance cannot interrupt effective healing at healthy HP.
                if (contact && HasAssistAttackContact(target) && _assistHealthFraction >= 0.35)
                { _assistMovementDecision = "attack-instead-of-field-escape";  return false; }
                if (since < 1100 && _assistHealthFraction >= 0.35)
                { _assistMovementDecision = "field-escape-settle";  return false; }
                _assistMovementDecision = "field-escape";
                return true;
            }
            if (!contact)
            {
                // Restoring generator contact is required mobility, never optional spacing.
                // The failed-landing and reversal guards above still prevent repeated bad hops.
                _assistMovementDecision = landingContact ? "restore-generator-contact" : "follow-moving-pack";
                return true;
            }
            // Existing contact is the default survival and damage state. A failed attack
            // acquisition cannot be 'fixed' by repeated discretionary repositioning.
            if (!HasAssistAttackContact(target))
            { _assistMovementDecision = "acquire-generator-contact";  return false; }
            double max = Hud.Game.Me.Stats.ResourceMaxPri;
            if (max > 0 && Hud.Game.Me.Stats.ResourceCurPri < max * 0.35)
            { _assistMovementDecision = "spirit-recovery";  return false; }
            if (since < 1200 || _assistMovementCasts.Count >= 2)
            { _assistMovementDecision = "productive-attack-window";  return false; }
            _assistMovementDecision = "optional-coverage";
            return true;
        }

        private void RecordAssistMovement(IMonster target, IWorldCoordinate landing, int now, bool escape)
        {
            // Called only after native landing confirmation and useful travel.
            var origin = landing.Offset(_assistDashOriginX-landing.X,
                _assistDashOriginY-landing.Y, _assistDashOriginZ-landing.Z);
            
            // Do not retry the same circle/target geometry after an unproductive landing.
            if(!escape && target!=null && target.AcdId==_assistMovementTarget
                && _assistMovementCircle!=0 && _assistMovementTargetPoint!=null
                && _assistWotHfLastGameTick<=_assistMovementGameTick
                && target.FloorCoordinate.XYDistanceTo(_assistMovementTargetPoint)<3
                && AssistBodyGap(origin,target)>Math.Min(AssistAttackReachYards,MeleeAssistRange))
            {
                if(_assistFailedCircleContacts.Count<64)
                    _assistFailedCircleContacts[((ulong)target.AcdId<<32)|_assistMovementCircle]
                        =target.FloorCoordinate.Offset(0,0,0);
            }
            _assistMovementCasts.Enqueue(now);
            while(_assistMovementCasts.Count>8)_assistMovementCasts.Dequeue();
            _assistMovementLastTick=now;_assistMovementGameTick=Hud.Game.CurrentGameTick;
            _assistMovementOrigin=origin;
            _assistMovementTarget=target==null?0u:target.AcdId;
            _assistMovementTargetPoint=target==null?null:target.FloorCoordinate.Offset(0,0,0);
            _assistMovementWasEscape=escape;
            uint circle=AssistLandingDamageCircle(landing);
            _assistMovementCircle=circle;
            if(circle!=0) {
                if(_assistEnteredDamageCircles.Count>=64)_assistEnteredDamageCircles.Clear();
                _assistEnteredDamageCircles[circle]=now;
            }
        }
        // Immediate unblocked coverage, with a conservative 2x damage-circle allowance.
        private double AssistCoverageValue(IWorldCoordinate point, IMonster target)
        {
            if (target==null || !CanAttackAssistFrom(point,target)) return 0;
            int elites,trash; string reason;
            AssistAttackCoverage(point,target,out elites,out trash);
            double multiplier=AssistPositionCirclePriority(point,target,out reason)==2 ? 2.0 : 1.0;
            return (elites*100.0+Math.Min(20,trash))*multiplier;
        }
        private bool AssistCoverageOutweighs(IWorldCoordinate point,IMonster target,
            IWorldCoordinate previous,IMonster previousTarget)
        {
            return AssistCoverageValue(point,target)>AssistCoverageValue(previous,previousTarget)*1.15+1.0;
        }
        private bool AssistCircleLosesCoverage(IWorldCoordinate point,IMonster target,IMonster retained)
        {
            var me=Hud.Game.Me.FloorCoordinate;
            return retained!=null && IsAssistPositionHazardFree(me) && CanAttackAssistFrom(me,retained)
                && !MapAssistAttackBlocked(me,retained) && AssistCoverageOutweighs(me,retained,point,target);
        }

        private int _assistLaneScanTick = int.MinValue;
        private bool AssistAttackLaneContains(IWorldCoordinate point, IMonster aim, IMonster body)
        {
            double ax=aim.FloorCoordinate.X-point.X, ay=aim.FloorCoordinate.Y-point.Y;
            double length=Math.Sqrt(ax*ax+ay*ay); if(length<0.01)return false;
            double dx=body.FloorCoordinate.X-point.X, dy=body.FloorCoordinate.Y-point.Y;
            double along=(dx*ax+dy*ay)/length;
            double reach=Math.Min(AssistAttackReachYards,MeleeAssistRange);
            return along>0 && along<=reach+body.RadiusScaled
                && Math.Abs(dx*ay-dy*ax)/length<=body.RadiusScaled+2.5
                && AssistSameHeight(point,body.FloorCoordinate) && AssistBodyGap(point,body)<=reach
                && !MapAssistAttackBlocked(point,body);
        }
        private IMonster PreferAssistAttackLane(IMonster selected,int now)
        {
            if(selected==null || _rangedCleanup || _assistSafetyCombatOnly || _assistNavigationPending
                || _pulse!=ActionKey.Unknown || _assistWotHfResumePending || !HasAssistAttackContact(selected)
                || !IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate)
                || (_assistLaneScanTick!=int.MinValue && unchecked(now-_assistLaneScanTick)<500))return selected;
            _assistLaneScanTick=now;
            var me=Hud.Game.Me.FloorCoordinate; var best=selected;
            double baseline=AssistCoverageValue(me,selected), score=baseline;
            int tier=AssistTargetTier(selected), checkedCount=0;
            foreach(var candidate in _assistCandidates)
            {
                if(candidate.AcdId==selected.AcdId || AssistTargetTier(candidate)!=tier
                    || !CanAttackAssistFrom(me,candidate) || !AssistAttackLaneContains(me,candidate,selected))continue;
                if(++checkedCount>20)break;
                int x,y; if(!TryGetAssistAim(candidate,out x,out y))continue;
                double coverage=AssistCoverageValue(me,candidate);
                if(coverage>baseline*1.15+1 && coverage>score){best=candidate;score=coverage;}
            }
            return best==selected?selected:RecordAssistSelection(best,tier,"attack-lane");
        }
        private IMonster PreferAssistProductiveContact(IMonster selected,int now)
        {
            if(selected==null || AssistTargetTier(selected)!=(int)AssistPriority.Trash || _rangedCleanup
                || _assistSafetyCombatOnly || AssistReactiveEscapeDue() || AssistMoltenEscapeDue())return selected;
            var me=Hud.Game.Me.FloorCoordinate;
            if(CanAttackAssistFrom(me,selected) && !MapAssistAttackBlocked(me,selected))return selected;
            bool settling=_assistMovementLastTick!=int.MinValue && unchecked(now-_assistMovementLastTick)<1200;
            double max=Hud.Game.Me.Stats.ResourceMaxPri;
            bool resourceLow=max>0 && Hud.Game.Me.Stats.ResourceCurPri<max*0.35;
            if((!settling && !resourceLow) || !IsAssistPositionHazardFree(me))return selected;
            IMonster best=null; double score=double.NegativeInfinity;
            foreach(var candidate in _assistCandidates)
            {
                if(AssistTargetTier(candidate)!=(int)AssistPriority.Trash || !CanAttackAssistFrom(me,candidate)
                    || MapAssistAttackBlocked(me,candidate) || IsAssistRouteSuppressed(candidate))continue;
                int x,y; if(!TryGetAssistAim(candidate,out x,out y))continue;
                double value=AssistCoverageValue(me,candidate)+(candidate.AcdId==_assistTargetAcd?0.5:0);
                if(value>score){score=value;best=candidate;}
            }
            return best==null?selected:RecordAssistSelection(best,AssistTargetTier(best),"productive-local-contact");
        }

        private void AssistAttackCoverage(IWorldCoordinate point,IMonster target,out int elites,out int trash)
        {
            elites=trash=0;
            foreach(var m in _assistCandidates)
            {
                if(!IsAssistMonsterValid(m) || m.WorldId!=target.WorldId
                    || !AssistAttackLaneContains(point,target,m))continue;
                if(IsAssistPositionElite(m))elites++;else trash++;
            }
        }
        private bool TryPickAssistDensityUpgrade(IMonster target, int now, out IWorldCoordinate best)
        {
            best = null;
            if (_assistMovementWorld != Hud.Game.Me.WorldId) ResetAssistMovementPolicy();
            if (target == null || !HasAssistAttackContact(target) || IsAssistHealingRecovery()
                || (_assistGeometryCheckTick != int.MinValue
                    && unchecked(now - _assistGeometryCheckTick) < 500)) return false;
            _assistGeometryCheckTick = now;
            // Attack and Spirit first. Reposition only for a clear pack-coverage gain.
            double max = Hud.Game.Me.Stats.ResourceMaxPri;
            if (max > 0 && Hud.Game.Me.Stats.ResourceCurPri < max * 0.35) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            double oldCoverage=AssistCoverageValue(me,target);
            double bestGain = 0;
            float edge = AssistContactEdge(target);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? edge : target.RadiusScaled
                    + (float)Math.Max(1.0, Math.Min(7.5, MeleeAssistRange - 0.5));
                for (int i = 0; i < AssistEdgeX.Length; i++)
                {
                    var point = target.FloorCoordinate.Offset(AssistEdgeX[i] * radius,
                        AssistEdgeY[i] * radius, 0f);
                    if (point.XYDistanceTo(me) < 3 || !CanAttackAssistFrom(point, target)
                        || MapAssistAttackBlocked(point, target)) continue;
                    int x, y;
                    if (!IsAssistDashPointSafe(point, target, out x, out y)) continue;
                    double coverage=AssistCoverageValue(point,target);
                    if (coverage<=oldCoverage*1.15+1.0) continue;
                    double gain=coverage-oldCoverage
                        - me.XYDistanceTo(point)*0.025;
                    if (gain <= bestGain) continue;
                    best = point; bestGain = gain;
                }
            }
            
            return best != null;
        }
        private bool IsCleanupThreat(IMonster monster)
        {
            return IsAssistMonsterValid(monster) && IsAssistVisiblePoint(monster.FloorCoordinate)
                && IsAssistRangedThreat(monster) && !monster.IsElite && monster.Rarity==ActorRarity.Normal
                && monster.NormalizedXyDistanceToMe<=35f;
        }
        private double AssistSelectionDistance(IMonster monster)
        {
            double distance=monster.NormalizedXyDistanceToMe;
            if (!_rangedCleanup || !IsCleanupThreat(monster)) return distance;
            // Rank only the initial choice. Closing distance never revokes the target.
            return distance + (distance<15 ? 20 : 0);
        }
        private bool IsCleanupCandidate(IMonster monster)
        {
            return IsCleanupThreat(monster) && IsAssistEligible(monster)
                && !IsAssistTargetTemporarilyBlocked(monster) && !IsAssistRouteSuppressed(monster);
        }

        private bool LowHealthFieldEscapeDue()
        {
            return !AssistShieldPylonActive() && !IsAssistLowHealthFieldPointFree(Hud.Game.Me.FloorCoordinate);
        }
        private bool IsAssistFieldBlocking(IActor field)
        {
            if(field==null || field.IsDisabled || field.SnoActor==null || AssistShieldPylonActive())return false;
            switch(field.SnoActor.Sno) {
                case ActorSnoEnum._x1_monsteraffix_frozenpulse_monster: return _assistHealthFraction < 0.75;
                case ActorSnoEnum._x1_monsteraffix_corpsebomber_projectile:
                case ActorSnoEnum._x1_monsteraffix_orbiter_projectile:
                case ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile: return _assistHealthFraction<0.75;
                case ActorSnoEnum._monsteraffix_frozen_iceclusters:
                    // Preserve early protection against buildup damage at critical health.
                    if(_assistHealthFraction<=0.40)return true;
                    if(_assistHealthFraction>=0.75)return false;
                    // Default HUD uses a three-second actor countdown. Avoid assuming
                    // the published four-second spawn delay starts at actor creation.
                    int age=Hud.Game.CurrentGameTick-field.CreatedAtInGameTick;
                    return age<0 || field.CreatedAtInGameTick<=0
                        || age>=120-AssistExplosionWalkingLeadTicks();
                default: return _assistHealthFraction<=0.40
                    || (_assistDamageEscapePending && _assistDamageEscapeTrigger=="low-health-rapid-loss");
            }
        }
        private bool IsAssistFrozenPulsePointFree(IWorldCoordinate point)
        {
            if(AssistShieldPylonActive() || _assistHealthFraction >= 0.75)return true;
            foreach(var field in _lowHealthFields)
                if(!field.IsDisabled && field.SnoActor.Sno==ActorSnoEnum._x1_monsteraffix_frozenpulse_monster
                    && AssistSameHeight(point,field.FloorCoordinate)
                    && point.XYDistanceTo(field.FloorCoordinate)<=LowHealthFieldRadius(field))return false;
            return true;
        }

        // Actual circle occupancy is a positional commitment, not an attack-range claim.
        // This state also recognizes a circle the player entered manually.
        private bool _selfDashCircleUpgrade;


        private uint _assistCircleReturnElite, _assistCircleReturnCircle;
        private int _assistCircleReturnSince;

        private IMonster PreferCurrentDamageCircleContact(IMonster selected, bool canDash, int now)
        {
            uint circle = AssistLandingDamageCircle(Hud.Game.Me.FloorCoordinate);
            if (circle == 0 || circle != _assistCircleReturnCircle || _rangedCleanup
                || IsAssistHealingRecovery() || _assistSafetyCombatOnly
                || AssistMoltenEscapeDue() || AssistReactiveEscapeDue())
            {
                _assistCircleReturnElite = 0u;
                _assistCircleReturnCircle = circle;
            }
            if (selected!=null && selected.AcdId==_assistHazardReturnAcd
                && !_rangedCleanup && !IsAssistHealingRecovery() && !_assistSafetyCombatOnly
                && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue()) return selected;
            bool leader = selected != null && (IsAssistLeader(selected) || selected.Rarity == ActorRarity.Boss);
            // Another leader already hittable from the circle wins without leaving it.
            if (leader && CanAttackFromCurrentDamageCircle(selected))
                _assistCircleReturnElite = 0u;
            if (_assistCircleReturnElite != 0u)
            {
                var remembered = FindAssistCandidate(_assistCircleReturnElite);
                if (!IsAssistEligible(remembered)) _assistCircleReturnElite = 0u;
                else if (unchecked(now - _assistCircleReturnSince) >= 750
                    && !IsAssistTargetTemporarilyBlocked(remembered)
                    && CanEngageAssistPosition(remembered, canDash, now))
                {
                    // Keep the expired deadline until pursuit succeeds; do not rearm
                    // another grace period merely because local trash remains hittable.
                    
                    return remembered;
                }
            }
            if (circle == 0 || CanAttackFromCurrentDamageCircle(selected)
                || HasAssistAttackContact(selected)) return selected;
            IMonster best = null;
            double bestScore = double.MinValue;
            int bestTier = -1, bestDanger = -1;
            int scanned = 0;
            foreach (var candidate in _assistCandidates)
            {
                if (++scanned > 192) break;
                int aimX, aimY;
                if (!IsAssistEligible(candidate) || !CanAttackFromCurrentDamageCircle(candidate)
                    || IsAssistTargetTemporarilyBlocked(candidate)
                    || !TryGetAssistAim(candidate, out aimX, out aimY)) continue;
                int tier = AssistTargetTier(candidate);
                if (tier < 0) continue;
                int danger = _rangedCleanup && tier == (int)AssistPriority.CtrlFocus
                    ? AssistRangedDanger(candidate) : 0;
                if (tier < bestTier || (tier == bestTier && danger < bestDanger)) continue;
                // A reachable elite outranks trash. When no elite can be hit from this
                // circle, a grouped local trash pack is better than chasing out of it.
                double group = tier == (int)AssistPriority.Trash
                    ? Math.Min(12, AssistTrashProfileFor(candidate).Count) : 0;
                double score = tier * 100 + group * 2 - candidate.NormalizedXyDistanceToMe;
                if (selected != null && candidate.AcdId == selected.AcdId) score += 6;
                if (tier == bestTier && danger == bestDanger && score <= bestScore) continue;
                best = candidate; bestScore = score; bestTier = tier; bestDanger = danger;
            }
            if (selected!=null && best!=null && !_rangedCleanup && !IsAssistHealingRecovery()
                && !_assistSafetyCombatOnly && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue())
            {
                IWorldCoordinate landing; string landingReason;
                if (TryPickAssistDashPoint(selected,false,out landing,out landingReason,true)
                    && CanAttackAssistFrom(landing,selected) && !MapAssistAttackBlocked(landing,selected)
                    && AssistCoverageOutweighs(landing,selected,Hud.Game.Me.FloorCoordinate,best)) return selected;
            }
            if (leader && best != null && !IsAssistLeader(best) && best.Rarity != ActorRarity.Boss
                && !_rangedCleanup && !IsAssistHealingRecovery() && !_assistSafetyCombatOnly
                && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue()
                && _assistCircleReturnElite == 0u)
            {
                _assistCircleReturnElite = selected.AcdId;
                _assistCircleReturnCircle = circle;
                _assistCircleReturnSince = now;
                
            }
            return best ?? selected;
        }

        private bool HasDamageCircleBuff()
        {
            // A generic Oculus buff can describe the equipped power, even in town.
            // Require a live damage-circle footprint at the player's actual location.
            var me=Hud.Game.Me.FloorCoordinate;
            foreach(var circle in _assistPositionCircles)
                if(circle.Kind!=2 && circle.Actor!=null && !circle.Actor.IsDisabled
                    && circle.Actor.WorldId==Hud.Game.Me.WorldId
                    && AssistSameHeight(me,circle.Actor.FloorCoordinate)
                    && me.XYDistanceTo(circle.Actor.FloorCoordinate)<=AssistCircleLandingRadius)
                    return true;
            return false;
        }

        private bool IsExtendedCirclePoint(IWorldCoordinate point, IMonster target)
        {
            if (target == null || IsAssistCircleContactRejected(point, target)
                || _assistDamageEscapePending || _assistSafetyCombatOnly || _assistHealthFraction <= 0.40) return false;
            foreach (var circle in _assistPositionCircles)
                if (circle.Kind != 2 && circle.Actor != null && !circle.Actor.IsDisabled
                    && AssistSameHeight(point, circle.Actor.FloorCoordinate)
                    && AssistBodyGap(circle.Actor.FloorCoordinate, target) <= AssistCircleTargetGapYards
                    && point.XYDistanceTo(circle.Actor.FloorCoordinate) <= AssistCircleLandingRadius)
                    return true;
            return false;
        }

        private void ObserveExtendedCircleContact(IMonster target, int now)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            if (target == null || AssistBodyGap(me,target) <= AssistAttackReachYards
                || !IsExtendedCirclePoint(me,target)) { _rangeTrialTarget=0; _rangeTrialStatus="none"; return; }
            if (_rangeTrialTarget != target.AcdId)
            {
                _rangeTrialTarget=target.AcdId; _rangeTrialTick=now;
                _rangeTrialHealth=target.CurHealth; _rangeTrialStatus="trial";
            }
            // Health loss corroborates contact; it is not proof of damage attribution.
            if (HasDamageCircleBuff() && _assistAttackKey == _assistMainKey
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking
                && AttackedIdentityMatches(target.AcdId,target.AnnId)
                && target.CurHealth < _rangeTrialHealth)
            { _rangeTrialTick=now; _rangeTrialHealth=target.CurHealth; _rangeTrialStatus="corroborated"; }
            if (unchecked(now-_rangeTrialTick) >= 500)
            {
                _rangeRejectedTarget=target.AcdId; _rangeRejectedPoint=me.Offset(0,0,0);
                _rangeRejectedTargetPoint=target.FloorCoordinate.Offset(0,0,0);
                _rangeRejectedAt=now; _rangeRejectedWorld=Hud.Game.Me.WorldId;
                _rangeTrialStatus="closer-contact-required";
                _assistPositionReplanPending=true; ReleaseAssistAttack();
            }
        }
        private static bool IsAssistRangedThreat(IMonster monster)
        {
            if (monster == null) return false;
            if (monster.SnoActor != null && (monster.SnoActor.Sno == ActorSnoEnum._fallenshaman_b
                || monster.SnoActor.Sno == ActorSnoEnum._x1_skeletonarcher_westmarch_ghost_a)) return true;
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
            int failedAt;
            if (!_circleRetried.Contains(circle.Actor.AcdId) && _circleFailedAt.TryGetValue(circle.Actor.AcdId,out failedAt)
                && unchecked(Environment.TickCount-failedAt)>=350)
            { _circleRetried.Add(circle.Actor.AcdId); _assistBlockedCircles.Remove(circle.Actor.AcdId); return false; }
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
                _circleFailedAt[_assistDashCircleAcd]=Environment.TickCount;
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
            // Failed travel is not failed combat. Current stationary contact clears
            // stale route state for every target, while walls/range/UI still veto it.
            if (IsAssistStationaryCombat(monster))
            {
                
                _assistBlockedTargets.Remove(monster.AcdId);
                return false;
            }
            var target = monster.FloorCoordinate;
            double px = me.X - blocked.PlayerX, py = me.Y - blocked.PlayerY;
            double tx = target.X - blocked.TargetX, ty = target.Y - blocked.TargetY;
            double resetYards = AssistNavigationRetryYards;
            bool playerReset = px * px + py * py >= resetYards * resetYards
                && (!blocked.NoProgress || !MapAssistTargetBlocked(monster));
            if (Hud.Game.Me.WorldId != blocked.World || playerReset || tx * tx + ty * ty >= (blocked.NoRoute ? 100.0 : 16.0))
            { _assistBlockedTargets.Remove(monster.AcdId); return false; }
            return true; // Retry when geometry changes, not on a periodic Dash spam timer.
        }

        private void BlockAssistNavigationTarget(IMonster target, bool noRoute = false, bool noProgress = false)
        {
            if (_assistBlockedTargets.Count >= 32 && !_assistBlockedTargets.ContainsKey(target.AcdId))
            {
                uint oldest = 0u;
                foreach (var key in _assistBlockedTargets.Keys) { oldest = key; break; }
                _assistBlockedTargets.Remove(oldest);
            }
            var me = Hud.Game.Me.FloorCoordinate;
            AssistBlockedTarget recovery = new AssistBlockedTarget();
            bool retained = IsAssistTargetTemporarilyBlocked(target)
                && _assistBlockedTargets.TryGetValue(target.AcdId, out recovery);
            if (!retained) recovery = new AssistBlockedTarget
            {
                PlayerX = me.X, PlayerY = me.Y, TargetX = target.FloorCoordinate.X,
                TargetY = target.FloorCoordinate.Y, World = Hud.Game.Me.WorldId, NoRoute = noRoute, NoProgress = noProgress
            };
            recovery.NoRoute |= noRoute; recovery.NoProgress |= noProgress;
            _assistBlockedTargets[target.AcdId] = recovery;
            // Ordinary Dash failure preserves native navigation. NoRoute inference
            // or observed NoProgress yields the goal until its geometry changes.
            
            _assistStatus = "navigation-blocked-dash";
        }

        private bool IsAssistRouteSuppressed(IMonster target)
        {
            AssistBlockedTarget failure;
            return (target!=null && _assistBlockedTargets.TryGetValue(target.AcdId,out failure)
                && IsAssistTargetTemporarilyBlocked(target)
                && (failure.NoProgress || (failure.NoRoute && MapAssistStatus()!=null)));
            // Observed stalls survive map removal; map-only inference does not.
        }

        private bool _assistNavigationTravelBlocked;
        private void ResetAssistContactRecovery()
        {
            _assistNavigationTravelBlocked = false;
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

        private void RefreshAssistNavigationProgress(IMonster target, int now)
        {
            AssistBlockedTarget recovery;
            if (!_assistBlockedTargets.TryGetValue(target.AcdId, out recovery))
            {
                // Geometry may already have invalidated the old failure record.
                // Observed movement still refreshes the active navigation clock.
                _assistNavigationSinceTick = now;
                return;
            }
            var me = Hud.Game.Me.FloorCoordinate;
            double dx = me.X - recovery.NavigationX, dy = me.Y - recovery.NavigationY;
            double dz = me.Z - recovery.NavigationZ;
            if (!recovery.NavigationTracked || dx * dx + dy * dy + dz * dz >= 1.0)
            {
                recovery.NavigationTracked = true;
                recovery.NavigationSinceTick = now;
                recovery.NavigationX = me.X; recovery.NavigationY = me.Y; recovery.NavigationZ = me.Z;
            }
            // Input ownership and temporary local combat may reset the active lock,
            // but only real displacement rearms this unresolved goal's watchdog.
            _assistNavigationSinceTick = recovery.NavigationSinceTick;
            _assistBlockedTargets[target.AcdId] = recovery;
        }

        private void BeginAssistNativeNavigation(IMonster target, IWorldCoordinate origin, int now, bool failedTravel = false)
        {
            if (target == null || !IsAssistPositionWorldPoint(origin)) return;
            if (failedTravel) _assistNavigationTravelBlocked = true;
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
            RefreshAssistNavigationProgress(target, now);
            
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
                _assistBlockedTargets.Remove(target.AcdId);
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
                _assistNavigationTravelBlocked = false;
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
                    RefreshAssistNavigationProgress(target, now);
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
                _assistNavigationTravelBlocked = false;
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
                    || !IsAssistStationaryCombat(candidate)) continue;
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

        // Recovery follows the walking route; it never retries a shortcut toward the
        // far target. Each mapped hop still requires a safe landing within one Dash.
        // Movement does not require a distant monster hover. Prefer a validated Dash;
        // otherwise hold safe ground on the connected walk route until main-attack reach.
        // Closed-door footprints stay hard blockers; the door controller owns interaction.
        private bool TryAssistRecoveryRouteMovement(IMonster target, int now)
        {
            if (_selfDashKey != ActionKey.Unknown || _pulse != ActionKey.Unknown
                || s7o_GenMonkInput.IsStandstillDown()
                || (CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                    && !MapAssistTargetBlocked(target))) return false;
            var status = MapAssistStatus();
            if (status == null) return false; // Optional map absent: retain native navigation.
            try
            {
                var me = Hud.Game.Me.FloorCoordinate;
                var goal = _assistMapGoal != null && _assistMapGoalAcd == target.AcdId
                    && _assistMapGoalWorld == target.WorldId && _assistMapTargetPosition != null
                    && _assistMapTargetPosition.XYDistanceTo(target.FloorCoordinate) <= 4f
                    ? _assistMapGoal : target.FloorCoordinate;
                IWorldCoordinate boundaryGoal;
                bool boundary=_assistBoundaryGoals.TryGetValue(target.AcdId,out boundaryGoal) && boundaryGoal!=null;
                if(boundary)goal=boundaryGoal;
                var route = _assistMapGet("path", new object[]{status["epoch"],me.X,me.Y,me.Z,
                    goal.X,goal.Y,goal.Z,0.6f}) as Dictionary<string,object>;
                string state = route != null && route.ContainsKey("state") ? Convert.ToString(route["state"]) : "Unknown";
                _assistMapPlanReason="recovery-route:"+state;
                if (route != null && route.ContainsKey("doorAcd"))
                    _assistMapDoorAcd = Convert.ToUInt32(route["doorAcd"]);
                var xyz = route != null && state == "Ready" && route.ContainsKey("waypoint")
                    ? route["waypoint"] as float[] : null;
                IWorldCoordinate point = xyz != null && xyz.Length == 3
                    ? Hud.Window.CreateWorldCoordinate(xyz[0],xyz[1],xyz[2]) : null;
                // A route worker may be busy or its goal may be occupied. Take a short
                // independently verified walk step now; never guess through a wall/door.
                if (point == null) {
                    var directGoal=boundary?goal:target.FloorCoordinate;
                    double distance=me.XYDistanceTo(directGoal);
                    double travel=Math.Min(12.0,Math.Max(0.0,boundary?distance:distance-target.RadiusScaled-6.0));
                    if(travel<1.5 || distance<0.01)return false;
                    float fraction=(float)(travel/distance);
                    point=me.Offset((directGoal.X-me.X)*fraction,
                        (directGoal.Y-me.Y)*fraction,(directGoal.Z-me.Z)*fraction);
                }
                long epoch=Convert.ToInt64(status["epoch"]);
                bool canDash=AutoDash && CanApproachDash(AssistDoorDashSkill());
                // Keep a useful ground destination stable as the camera/player moves.
                // A fresh Dash may still choose a farther validated route hop immediately.
                if(!canDash && _assistRouteWalkStage!=0 && _assistRouteWalkEpoch==epoch
                    && _assistRouteWalkPoint!=null && me.XYDistanceTo(_assistRouteWalkPoint)>2.5f)
                    point=_assistRouteWalkPoint;
                double hop=me.XYDistanceTo(point);
                if(hop<1.0 || hop>AssistDashTravelLimit-0.5)return false;
                var corridor=_assistMapGet("corridor",new object[]{status["epoch"],me.X,me.Y,me.Z,
                    point.X,point.Y,point.Z,0.6f}) as Dictionary<string,object>;
                if(corridor==null || !corridor.ContainsKey("state")
                    || (corridor["state"] as string)!="ClearCandidate") {
                    if(state=="NoRoute" && me.XYDistanceTo(target.FloorCoordinate)>AssistDashTravelLimit)
                        BlockAssistNavigationTarget(target,true);
                    return false;
                }
                if(!RefreshAssistPositionActors())return false;
                for(int i=1;i<=4;i++)
                    if(!IsAssistPositionHazardFree(me.Offset((point.X-me.X)*i/4f,
                        (point.Y-me.Y)*i/4f,(point.Z-me.Z)*i/4f)))return false;
                var dash=AssistDoorDashSkill();
                int x,y;
                // Dash is independent of hover and may replace an owned ground hold.
                if(AutoDash && CanApproachDash(dash) && hop>=3.0
                    && !IsAssistBlockedApproachDirection(point)
                    && IsAssistDashPointSafe(point,target,out x,out y,false,true)
                    && AllowAssistMovement(target, point, now, false, false)) {
                    ReleaseAssistAttack();CancelTarget();
                    MaintainDash(dash,now,target,point,false,true);
                    if(_selfDashKey!=ActionKey.Unknown) {
                        _assistMapRouteReady=true;_assistPositionReason="recovery-route-hop";
                        _assistStatus="navigation-route-dash";
                        MapAssistFeedback("routed-dash",target.AcdId,point);return true;
                    }
                }
                if(!IsAssistMoltenWalkFree(point))return false;
                // An existing useful keyboard follow is already moving: do not interrupt
                // it for ground aiming. A missing hover instead uses the map directly.
                if(_assistAttackKey!=ActionKey.Unknown && _assistRouteWalkStage==0)return false;
                if(_assistRouteWalkStage!=0 && ElapsedMs(now,_assistNavigationSinceTick)>=500) {
                    _assistRouteWalkFailedPoint=point.Offset(0f,0f,0f);
                    _assistRouteWalkFailedOrigin=me.Offset(0f,0f,0f);_assistRouteWalkFailedWorld=Hud.Game.Me.WorldId;
                    ReleaseAssistAttack();_assistNavigationReacquired=true;
                    _assistNavigationReason="route-walk-no-progress";return false;
                }
                if(_assistRouteWalkFailedPoint!=null && _assistRouteWalkFailedWorld==Hud.Game.Me.WorldId
                    && me.XYDistanceTo(_assistRouteWalkFailedOrigin)<3f
                    && point.XYDistanceTo(_assistRouteWalkFailedPoint)<3f)return false;
                // A newly aimed waypoint is input, not observed travel.
                // Keep the goal watchdog established by native recovery.
                _assistRouteWalkEpoch=epoch;
                string reason;
                if(!WalkAssistGroundWaypoint(point,now,"navigate-route-walk",
                    ref _assistRouteWalkStage,ref _assistRouteWalkAimTick,ref _assistRouteWalkPoint,out reason,true)) {
                    if(_assistRouteWalkStage!=0)ReleaseAssistAttack();
                    _assistRouteWalkReason=reason;return false;
                }
                _assistRouteWalkReason=reason;_assistNavigationReason="grid-ground-follow";
                _assistNavigationReacquired=false;_assistMapRouteReady=true;
                return true;
            }
            catch { return false; }
        }

        private bool YieldAssistStalledGoal(IMonster target)
        {
            
            ReleaseAssistAttack(); CancelTarget();
            BlockAssistNavigationTarget(target, noProgress: true);
            _assistNavigationPending = false;
            _assistNavigationReacquired = true;
            _assistNavigationReason = "no-progress-goal-yield";
            _assistStatus = "unreachable-goal-yield";
            return true; // Next collect selects another target; no retry sleep.
        }

        private bool HandleAssistNativeNavigation(IMonster target, int now)
        {
            if (!_assistNavigationPending || _assistNavigationTargetAcd != target.AcdId) return false;
            IWorldCoordinate boundaryGoal;
            if(!CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate,target)
                && _assistBoundaryGoals.TryGetValue(target.AcdId,out boundaryGoal) && boundaryGoal!=null) {
                if(_assistScopedStandstill!=0)ReleaseAssistScopedStandstill();
                if(TryAssistRecoveryRouteMovement(target,now))return true;
                if(ElapsedMs(now,_assistNavigationSinceTick)>=800)return YieldAssistStalledGoal(target);
                _assistStatus="boundary-route-pending";return true;
            }
            var held = FindAssistCandidate(_assistNavigationHeldAcd);
            bool usefulAttack = held != null && _assistAttackKey != ActionKey.Unknown
                && _assistAttackKey == _assistMainKey
                && s7o_GenMonkInput.Owns(_assistAttackKey) && HasAssistAttackContact(held);
            // Failure watchdog, not an engagement delay. Movement refreshes this
            // clock in AdvanceAssistNavigation; only the main damage attack sustains combat.
            if (!s7o_GenMonkInput.IsStandstillDown() && !usefulAttack
                && ElapsedMs(now, _assistNavigationSinceTick) >= 500)
                return YieldAssistStalledGoal(target);
            if (Hud.Game.Me.FloorCoordinate.XYDistanceTo(target.FloorCoordinate)>AssistDashTravelLimit
                && MapAssistStatus()!=null) {
                if (TryAssistRecoveryRouteMovement(target,now)) return true;
                ReleaseAssistAttack();
                if (IsAssistRouteSuppressed(target)) {
                    _assistNavigationPending=false;_assistNavigationReacquired=true;
                    _assistNavigationReason="no-connected-route";_assistStatus="unreachable-goal-yield";
                } else {
                    _assistNavigationReason="grid-hop-unavailable";_assistStatus="navigate-grid-replan";
                }
                return true; // Never aim a native generator through a far wall.
            }
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
                    // A nearby hit is contact, not travel toward the blocked goal.
                    // Release the alternate generator and let normal selection resume
                    // with the main damage attack; do not replenish recovery forever.
                    return YieldAssistStalledGoal(target);
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
            if (navigation == null) {
                if (TryAssistRecoveryRouteMovement(target, now)) return true;
                _assistStatus = "navigate-no-keyboard-generator"; return true;
            }
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
            // Consume confirmed hover immediately. Ground routing is
            // a fallback for missing hover, never an owner that can hide a usable lock.
            if (!exactHover && TryAssistRecoveryRouteMovement(target, now)) return true;
            if (_assistRouteWalkStage!=0) ReleaseAssistAttack();
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
                    // Full scan, route and local fallback failed: yield this goal,
                    // rather than retaining it indefinitely without an attack.
                    return YieldAssistStalledGoal(target);
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
            if (_selfDashUpkeep)
            {
                _assistDashOutcome = _selfDashObserved ? "upkeep-confirmed" : "upkeep-unconfirmed";
                _assistDashActualSafe = true; // Already occupied; don't rewrite navigation.
                return;
            }
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
            bool blockedContact=liveTarget==null || !CanAttackAssistFrom(actual,liveTarget)
                || MapAssistTargetBlocked(liveTarget);
            bool stalledHop=!_assistDashForHazard && !_assistDashEscapeOnly && blockedContact
                && _assistDashTravelObserved<4f && !verticalProgress;
            bool partialHop=!_assistDashForHazard && !_assistDashEscapeOnly && blockedContact
                && _assistDashTravelRequested>4f && actual.XYDistanceTo(_assistDashLanding)>2f
                && _assistDashTravelObserved<_assistDashTravelRequested*0.75f;
            missedLanding |= stalledHop || partialHop;
            bool failed = _assistDashMovementFailed = !_assistDashLandingConfirmed || littleTravel || missedLanding;
            RecordAssistObstacleAttempt(liveTarget,actual,failed);
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
                // An optional low-health reposition cannot become an endless escape
                // loop. If blocked, hand back to attacks; hard core/blast exits differ.
                bool optionalExit = _assistDamageEscapeTrigger == "low-health-rapid-loss";
                _assistPositionReplanPending = !optionalExit;
                _assistApproachPending = !optionalExit;
                _nextDashAimTick = now;
                return;
            }
            // Physical failure owns recovery even when the landing is unsafe.
            // Hard emergencies can preempt navigation; soft spacing cannot erase it.
            if (failed)
            {
                _assistDashOutcome = littleTravel ? "little-travel" : missedLanding ? "blocked-shortcut" : "unconfirmed";
                BeginAssistNativeNavigation(FindAssistCandidate(_selfDashTargetAcd), actual, now, true);
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
            if (_assistActive || AssistRequested() || ctrlDown || _rangedCleanup)
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
            ObserveAssistTravel();
            _assistCandidates.Clear();
            _assistTrashProfiles.Clear(); _assistTrashMaxValue = 0;
            _assistProjectedBeyondNativeCount = 0;
            _assistOtherEliteLeaderPresent = false;
            foreach (var monster in Hud.Game.AliveMonsters)
                if (IsAssistEligible(monster))
                {
                    _assistCandidates.Add(monster);
                    if (!IsAssistJuggerPackMember(monster)
                        && IsAssistPositionElite(monster))
                        _assistOtherEliteLeaderPresent = true;
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
            Minion = 6, ShockTower = 7, Leader = 8, Boss = 11, CtrlFocus = 12
        }

        private int AssistTargetTier(IMonster monster)
        {
            if (_rangedCleanup && IsCleanupCandidate(monster)) return (int)AssistPriority.CtrlFocus;
            if (monster.Rarity == ActorRarity.Boss) return (int)AssistPriority.Boss;
            if (IsAssistJuggerPackMember(monster))
            {
                // Other elite leaders go first. Leftover minions/trash cannot defer
                // the last Juggernaut; CTRL still overrides this automatic priority.
                if (IsAssistJuggerLeader(monster))
                    return (int)(_assistOtherEliteLeaderPresent ? AssistPriority.JuggerLeader : AssistPriority.Leader);
                return (int)AssistPriority.JuggerMinion;
            }
            if (IsAssistLeader(monster)) return (int)AssistPriority.Leader;
            if (IsAssistShockTower(monster) && monster.NormalizedXyDistanceToMe <= AssistShockTowerPriorityRange)
                return (int)AssistPriority.ShockTower;
            if (monster.Rarity == ActorRarity.RareMinion || monster.IsElite) return (int)AssistPriority.Minion;
            // Ranged cleanup is owned by the panic-dodge event or manual CTRL.
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
            _rangedCleanup=false; _rangedManual=false; _rangedEmptySince=int.MinValue;
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
            int now=Environment.TickCount;
            bool pressed=down&&!_juggerCtrlWasDown; _juggerCtrlWasDown=down;
            bool safe=IsLeftClickSafe() && (_assistActive || AssistRequested());
            if (_rangedCleanup && (Hud.Game.Me.WorldId != _rangedWorld || Hud.Game.Me.IsDead)) ClearJuggerFocus();
            if (pressed && safe)
            {
                if (_rangedCleanup) { _rangedExitReason="manual-cancel"; ClearJuggerFocus(); _rangedSuppressUntil=unchecked(now+5000); }
                else { _rangedExitReason="active"; _rangedCleanup=true; _rangedManual=true; _rangedStarted=now;
                    _rangedWorld=Hud.Game.Me.WorldId; _rangedEmptySince=int.MinValue; }
            }
            if (_rangedCleanup)
            {
                bool present=false, available=false;
                foreach(var monster in Hud.Game.AliveMonsters) {
                    if(!IsCleanupThreat(monster))continue;
                    present=true;
                    if(IsCleanupCandidate(monster)) { available=true;break; }
                }
                if (available) _rangedEmptySince=int.MinValue;
                else if (_rangedEmptySince==int.MinValue) _rangedEmptySince=now;
                if ((_rangedEmptySince!=int.MinValue && unchecked(now-_rangedEmptySince)>=(present ? 2500 : 1000))
                    || (!_rangedManual && (Hud.Game.Players.Count()>1 || unchecked(now-_rangedStarted)>=6000)))
                { _rangedExitReason=present ? "local-threats-unreachable" : "local-threats-gone"; ClearJuggerFocus(); _rangedSuppressUntil=unchecked(now+2000); }
            }
            _juggerFocusPaintAllowed=safe&&_rangedCleanup;
        }

        private void PaintJuggerFocus()
        {

            if (!Enabled || !_juggerFocusPaintAllowed || Hud.Render.UiHidden
                || _juggerFocusBrush == null || _juggerFocusOutlineBrush == null
                || !_rangedCleanup || Hud.Game.Me.IsDead) return;
            var center = Hud.Game.Me.FloorCoordinate;
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
            if (profile.Count >= 3 && profile.Progression >= Math.Max(2.0, _assistTrashMaxValue)) return 2;
            return profile.Value >= AssistHighTrashProgression ? 1 : 0;
        }

        private int AssistNearbyPackWeight(IMonster focus)
        {
            if (focus == null) return 0;
            int weight = 0;
            foreach (var other in _assistCandidates)
                if (other.WorldId == focus.WorldId && AssistSameHeight(other.FloorCoordinate, focus.FloorCoordinate)
                    && focus.FloorCoordinate.XYDistanceTo(other.FloorCoordinate) <= AssistTrashPackRadius)
                    weight += IsAssistPositionElite(other) ? 4 : 1;
            return weight;
        }

        private uint _assistDensityFocusTarget;
        private bool AssistDensityFocusActive(IMonster target)
        {
            return target!=null && target.AcdId==_assistDensityFocusTarget
                && _assistDensityFocusTick!=int.MinValue
                && unchecked(Environment.TickCount-_assistDensityFocusTick)<1200;
        }
        private int _assistDensityFocusTick = int.MinValue, _assistDensityScanTick = int.MinValue;
        private IMonster PreferMovingElitePack(IMonster retained, int now, bool canDash)
        {
            if (retained == null
                || (AssistTargetTier(retained) != (int)AssistPriority.Leader
                    && AssistTargetTier(retained) != (int)AssistPriority.Trash)
                || (_assistDensityFocusTick != int.MinValue
                    && unchecked(now - _assistDensityFocusTick) < 1200)
                || (_assistDensityScanTick != int.MinValue
                    && unchecked(now - _assistDensityScanTick) < 350)) return retained;
            _assistDensityScanTick = now;
            var me = Hud.Game.Me.FloorCoordinate;
            double oldDistance = me.XYDistanceTo(retained.FloorCoordinate);
            int oldWeight = AssistNearbyPackWeight(retained);
            // Preserve attacks in a meaningful pack; leave an isolated elite when the
            // support has dragged a substantially larger same-tier pack forward.
            bool circleHeld=CanAttackFromCurrentDamageCircle(retained);
            if (!circleHeld && HasAssistAttackContact(retained) && oldWeight >= 6) return retained;
            IMonster best = retained;
            int bestWeight = oldWeight;
            foreach (var candidate in _assistCandidates)
            {
                if (candidate.AcdId == retained.AcdId || AssistTargetTier(candidate) != AssistTargetTier(retained)
                    || candidate.NormalizedXyDistanceToMe > NearbyTrashRange
                    || IsAssistTargetTemporarilyBlocked(candidate) || IsAssistRouteSuppressed(candidate)) continue;
                double distance = me.XYDistanceTo(candidate.FloorCoordinate);
                int weight = AssistNearbyPackWeight(candidate);
                if ((!circleHeld && (weight < oldWeight+5 || weight < oldWeight*2))
                    || weight <= bestWeight) continue;
                // A distant unproductive chase is worse than current elite contact.
                if (distance > oldDistance + 24) continue;
                int x, y;
                if (!TryGetAssistAim(candidate, out x, out y)
                    || !CanEngageAssistPosition(candidate, canDash, now)) continue;
                IWorldCoordinate landing; string landingReason;
                if (!TryPickAssistDashPoint(candidate,false,out landing,out landingReason,true)
                    || !CanAttackAssistFrom(landing,candidate) || MapAssistAttackBlocked(landing,candidate)
                    || !AssistCoverageOutweighs(landing,candidate,me,retained)) continue;
                best = candidate; bestWeight = weight;
            }
            if (best != retained)
            {
                _assistDensityFocusTick = now; _assistDensityFocusTarget=best.AcdId;
                
            }
            return best;
        }

        private bool IsAssistUsefulCircleTarget(IMonster target)
        {
            // The circle is a combat location even when the remaining nearby enemies
            // are ordinary trash. Attack the best available local target from it.
            return target != null;
        }

        private bool IsAssistCircleCandidateCompatible(IMonster candidate, IMonster retained)
        {
            if (AssistTargetTier(candidate) != (int)AssistPriority.Trash || retained == null) return true;
            if (CanAttackFromCurrentDamageCircle(retained)) return false;
            var nearby = AssistTrashProfileFor(candidate);
            var previous = AssistTrashProfileFor(retained);
            // A different pack is valid when it gives the circle useful attacks.
            // Avoid crossing the map for a lone negligible target.
            return nearby.Count >= 2 || nearby.Progression >= previous.Progression * 0.5;
        }

        private bool IsAssistSparseTrash(AssistTrashProfile profile)
        {
            return profile.Count<=5 && profile.Value<AssistHighTrashProgression
                && profile.Progression<AssistHighTrashProgression*profile.Count;
        }
        private void ObserveAssistTravel()
        {
            var me=Hud.Game.Me.FloorCoordinate; int now=Environment.TickCount;
            if(me==null)return;
            if(_assistTravelWorld!=Hud.Game.Me.WorldId || (_assistTravelAnchor!=null
                && Math.Abs(me.Z-_assistTravelAnchor.Z)>20))
            {
                _assistTravelWorld=Hud.Game.Me.WorldId; _assistTravelHistory.Clear();
                _assistTravelAnchor=null; _assistTravelHeadingKnown=false; _assistTravelSampleTick=int.MinValue;
            }
            if(_assistTravelSampleTick!=int.MinValue && unchecked(now-_assistTravelSampleTick)<500)return;
            _assistTravelSampleTick=now;
            if(_assistTravelAnchor==null)
            { _assistTravelAnchor=me.Offset(0,0,0); _assistTravelHistory.Add(me.Offset(0,0,0)); return; }
            double dx=me.X-_assistTravelAnchor.X,dy=me.Y-_assistTravelAnchor.Y;
            double distance=Math.Sqrt(dx*dx+dy*dy);
            if(distance<20)return;
            bool visited=IsAssistVisitedCorridor(me,10);
            if(!visited)
            {
                bool combatMove=_assistActive && _assistMovementLastTick!=int.MinValue
                    && unchecked(now-_assistMovementLastTick)<1200
                    && (_assistMovementWasEscape || _assistMovementCircle!=0
                        || _assistPositionReason.IndexOf("density",StringComparison.Ordinal)>=0
                        || _assistPositionReason.IndexOf("spacing",StringComparison.Ordinal)>=0);
                if(!combatMove)
                {
                    _assistTravelHeadingX=dx/distance; _assistTravelHeadingY=dy/distance;
                    _assistTravelHeadingKnown=true;
                }
                if(_assistTravelHistory.Count>=64)_assistTravelHistory.RemoveAt(0);
                _assistTravelHistory.Add(me.Offset(0,0,0));
            }
            _assistTravelAnchor=me.Offset(0,0,0);
        }
        private bool IsAssistVisitedCorridor(IWorldCoordinate point,double radius)
        {
            for(int i=0;i<_assistTravelHistory.Count;i++)
            {
                var end=_assistTravelHistory[i];
                if(point.XYDistanceTo(end)<radius)return true;
                if(i==0)continue;
                var start=_assistTravelHistory[i-1];
                double dx=end.X-start.X,dy=end.Y-start.Y,den=dx*dx+dy*dy;
                if(den<1)continue;
                double t=Math.Max(0,Math.Min(1,((point.X-start.X)*dx+(point.Y-start.Y)*dy)/den));
                double ex=point.X-start.X-t*dx,ey=point.Y-start.Y-t*dy;
                if(ex*ex+ey*ey<radius*radius)return true;
            }
            return false;
        }
        private double AssistForwardPreference(IWorldCoordinate point)
        {
            if(!_assistTravelHeadingKnown)return 1;
            var me=Hud.Game.Me.FloorCoordinate;
            double dx=point.X-me.X,dy=point.Y-me.Y,length=Math.Sqrt(dx*dx+dy*dy);
            if(length<1)return 1;
            double dot=(dx*_assistTravelHeadingX+dy*_assistTravelHeadingY)/length;
            // A bonus, never exclusion: sparse forward packs can beat slightly larger old packs.
            double factor=1+Math.Max(0,dot)*1.2;
            if(dot<0)
                foreach(var old in _assistTravelHistory)
                    if(old.XYDistanceTo(me)>10 && point.XYDistanceTo(old)<14){factor*=0.8;break;}
            return factor;
        }

        private double AssistTrashPriorityScore(IMonster monster, out double valueScore, out double densityScore)
        {
            var profile=AssistTrashProfileFor(monster);
            valueScore=profile.Value; densityScore=profile.Progression;
            bool sparse=_assistTravelHeadingKnown && IsAssistSparseTrash(profile);
            double score=(sparse || AssistTrashClass(profile)==2 ? densityScore : valueScore)
                /(1.0+monster.NormalizedXyDistanceToMe/AssistTrashPackRadius);
            return sparse ? score*AssistForwardPreference(monster.FloorCoordinate) : score;
        }

        private void RememberAssistTrashAttack(IMonster monster)
        {
            // The caller has native attack-animation and attacked-identity confirmation.
            if (monster == null || monster.AcdId != _assistTargetAcd || AssistTargetTier(monster) != 2
                || Hud.Game.Me.FloorCoordinate == null) return;
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

        private bool IsAssistHighValueWithinDash(IMonster monster)
        {
            return monster != null && AssistTrashProgression(monster) >= AssistHighTrashProgression
                && Hud.Game.Me.FloorCoordinate.XYDistanceTo(monster.FloorCoordinate) <= AssistDashTravelLimit;
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
            if(localTrash)_assistAcquisitionCandidates.RemoveAll(m=>m.NormalizedXyDistanceToMe>NearbyTrashRange && !IsAssistHighValueWithinDash(m));
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
                var profile=AssistTrashProfileFor(monster);
                int candidateClass = _assistTravelHeadingKnown && IsAssistSparseTrash(profile) ? 0 : AssistTrashClass(profile);
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
            if (retained == null || AssistDensityFocusActive(retained) || !RefreshAssistPositionActors()) return retained;
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
                if (_assistRecoveryCombatOnly && !IsAssistReachableCombat(candidate)
                    && (MapAssistTargetBlocked(candidate) || IsAssistBlockedApproachDirection(candidate.FloorCoordinate))) continue;
                if (candidate.AcdId == retained.AcdId || AssistTargetTier(candidate) != tier
                    || IsAssistTargetTemporarilyBlocked(candidate)) continue;
                bool circleNear = false;
                foreach (var circle in _assistPositionCircles)
                    if (circle.Kind != 2 && circle.Actor != null && !circle.Actor.IsDisabled
                        && !IsAssistCircleBlocked(circle)
                        && AssistBodyGap(circle.Actor.FloorCoordinate, candidate)
                            <= AssistCircleTargetGapYards)
                    { circleNear = true; break; }
                if (!circleNear || candidate.NormalizedXyDistanceToMe > AssistDashTravelLimit + AssistCircleLandingRadius
                    || ++evaluated > 20) continue;
                if (!IsAssistCircleCandidateCompatible(candidate, retained)) continue;
                int x, y;
                if (!TryGetAssistAim(candidate, out x, out y)) continue;
                int priority = CanAttackFromCurrentDamageCircle(candidate) ? 2 : 0;
                if (priority == 0 && TryPickAssistCircleUpgrade(candidate, out point, out reason))
                    priority = AssistPositionCirclePriority(point, candidate, out reason);
                if (priority < bestPriority || priority <= retainedPriority) continue;
                var candidatePoint=CanAttackFromCurrentDamageCircle(candidate) ? Hud.Game.Me.FloorCoordinate : point;
                if (AssistCircleLosesCoverage(candidatePoint,candidate,retained)) continue;
                double distance = candidate.NormalizedXyDistanceToMe;
                // Prefer fighting the denser group from the damage circle, not the
                // closest single trash monster. The multiplier is the primary priority.
                double pack = tier == (int)AssistPriority.Trash
                    ? Math.Min(20, AssistTrashProfileFor(candidate).Progression * 3
                        + AssistTrashProfileFor(candidate).Count)
                    : Math.Min(15, AssistNearbyPackWeight(candidate) * 1.5);
                double score = distance - pack;
                if (priority == bestPriority && score >= nearest) continue;
                best = candidate; bestPriority = priority; nearest = score;
            }
            return best;
        }

        private bool IsAssistStationaryCombat(IMonster target)
        {
            int x, y;
            // Standstill contact does not execute native follow or select a new landing.
            return IsAssistEligible(target) && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                && !MapAssistTargetBlocked(target) && TryGetAssistAim(target, out x, out y);
        }

        private bool IsAssistHealingRecovery()
        {
            if (!RefreshAssistPositionActors()) return false;
            // Critical health needs local hits even without a recognized hazard actor.
            // Actor presence alone must not keep recovery/circle suppression active.
            return _assistSafetyCombatPending || _panicMustAttack || _assistHealthFraction <= 0.40
                || (_assistHealthFraction < 0.60 && FindAssistCandidate(_assistHazardReturnAcd) != null);
        }

        private IMonster PickAssistAcquisitionContact(bool canDash, int now)
        {
            var retained = FindAssistCandidate(_assistTargetAcd);
            if (retained == null || AssistTargetTier(retained) != (int)AssistPriority.Trash
                || (_assistHitVerified && !_assistNavigationPending)) return null;
            IMonster best = null;
            foreach (var candidate in _assistCandidates)
            {
                int tier = AssistTargetTier(candidate); int x, y;
                // Complete local main-attack acquisition before retrying a failed remote
                // goal. Reachable higher priorities and fresh approaches still preempt.
                if (tier > (int)AssistPriority.Trash && TryGetAssistAim(candidate, out x, out y)
                    && CanEngageAssistPosition(candidate, canDash, now)
                    && (IsAssistStationaryCombat(candidate) || !IsAssistTargetTemporarilyBlocked(candidate))) return null;
                if (tier != (int)AssistPriority.Trash || !IsAssistStationaryCombat(candidate)
                    || IsAssistRouteSuppressed(candidate) || IsAssistTargetTemporarilyBlocked(candidate)) continue;
                if (candidate.AcdId == retained.AcdId) best = candidate;
                else if (best == null || (best.AcdId != retained.AcdId
                    && (candidate.NormalizedXyDistanceToMe < best.NormalizedXyDistanceToMe
                        || (candidate.NormalizedXyDistanceToMe == best.NormalizedXyDistanceToMe && candidate.AcdId < best.AcdId)))) best = candidate;
            }
            return best;
        }

        private IMonster PickAssistSafetyCombat()
        {
            if (!IsAssistHealingRecovery()) return null;
            IMonster best = null; int bestTier = -1;
            foreach (var candidate in _assistCandidates)
            {
                int tier = AssistTargetTier(candidate);
                // Safe actual contact outranks failed travel history and remote elite pursuit.
                if (tier < 0 || !IsAssistStationaryCombat(candidate)) continue;
                bool retained = candidate.AcdId == _assistTargetAcd;
                bool bestRetained = best != null && best.AcdId == _assistTargetAcd;
                if (best == null || tier > bestTier || (tier == bestTier
                    && (retained || (!bestRetained && (candidate.NormalizedXyDistanceToMe < best.NormalizedXyDistanceToMe
                        || (candidate.NormalizedXyDistanceToMe == best.NormalizedXyDistanceToMe && candidate.AcdId < best.AcdId))))))
                { best = candidate; bestTier = tier; }
            }
            return best;
        }

        private IMonster PickAssistSafetyApproach(bool canDash,int now)
        {
            if(!IsAssistHealingRecovery())return null;
            IMonster best=null;
            foreach(var candidate in _assistCandidates)
            {
                int x,y;
                if(AssistTargetTier(candidate)<0 || candidate.NormalizedXyDistanceToMe>25
                    || IsAssistTargetTemporarilyBlocked(candidate) || IsAssistRouteSuppressed(candidate)
                    || !TryGetAssistAim(candidate,out x,out y) || !IsAssistNativeFollowHazardFree(candidate)
                    || !CanEngageAssistPosition(candidate,canDash,now))continue;
                if(best==null || candidate.NormalizedXyDistanceToMe<best.NormalizedXyDistanceToMe)best=candidate;
            }
            return best;
        }

        private IMonster AssistTarget(bool canDash, int now)
        {
            _assistUiBlockedTargets = false;
            _assistPylonBlockedTargets = false;
            _assistHazardBlockedTargets = false;
            _assistAcquisitionCandidates.Clear();
            _assistSafetyCombatOnly = false;
            _assistRecoveryCombatOnly = false;
            var safetyTarget = PickAssistSafetyCombat();
            if (safetyTarget != null)
            {
                _assistSafetyCombatOnly = true;
                return RecordAssistSelection(safetyTarget,AssistTargetTier(safetyTarget),"safety-combat");
            }
            var safetyApproach=PickAssistSafetyApproach(canDash,now);
            if(safetyApproach!=null)
                return RecordAssistSelection(safetyApproach,AssistTargetTier(safetyApproach),"safety-approach");
            var acquisitionContact = PickAssistAcquisitionContact(canDash, now);
            if (acquisitionContact != null)
            {
                var circleTarget = canDash && !AssistMoltenEscapeDue() && !AssistReactiveEscapeDue()
                    ? AssistCircleCombatTarget(acquisitionContact, AssistTargetTier(acquisitionContact))
                    : acquisitionContact;
                return RecordAssistSelection(circleTarget, AssistTargetTier(circleTarget),
                    circleTarget != acquisitionContact ? "damage-circle-target" : "acquisition-contact");
            }
            if (_assistDoorTarget != null || _assistNavigationPending || _assistNavigationReacquired)
                foreach (var candidate in _assistCandidates)
                {
                    int ax, ay;
                    if (AssistTargetTier(candidate) >= 0 && IsAssistReachableCombat(candidate)
                        && !IsAssistRouteSuppressed(candidate) && TryGetAssistAim(candidate,out ax,out ay))
                    { _assistRecoveryCombatOnly = true; break; }
                }
            IMonster retained = FindAssistCandidate(_assistTargetAcd);
            IMonster moltenReturn = ResolveAssistHazardReturn(now);
            IMonster moltenSuspended = null;
            if (retained != null && (AssistTargetTier(retained) < 0 || IsAssistRouteSuppressed(retained))) retained = null;
            if (moltenReturn != null && (AssistTargetTier(moltenReturn) < 0 || IsAssistRouteSuppressed(moltenReturn)))
            { if (IsAssistRouteSuppressed(moltenReturn)) _assistHazardReturnAcd=0u; moltenReturn=null; }
            int aimX, aimY;
            if (retained != null && !TryGetAssistAim(retained, out aimX, out aimY))
            {
                _assistUiBlockedTargets = true; retained = null;
            }
            if (moltenReturn != null && !TryGetAssistAim(moltenReturn, out aimX, out aimY))
            { _assistUiBlockedTargets = true; moltenReturn = null; }
            bool returning = moltenReturn != null && !_rangedCleanup
                && !IsAssistTargetTemporarilyBlocked(moltenReturn)
                && CanEngageAssistPosition(moltenReturn, canDash, now);
            if (returning) retained = moltenReturn;
            if (retained != null && !CanEngageAssistPosition(retained, canDash, now))
            {
                _assistHazardBlockedTargets = true;
                if (IsAssistPositionElite(retained) && _assistPositionReadable
                    && (_assistPositionMolten.Count > 0 || _assistPositionArcane.Count > 0
                        || _assistPositionOrbiter.Count > 0 || _assistPositionElectrified.Count > 0))
                { RememberAssistHazardReturn(retained); moltenReturn = FindAssistCandidate(_assistHazardReturnAcd); }
                else if (AssistMoltenEscapeDue()) moltenSuspended = retained;
                retained = null;
            }
            // Reconsider an isolated trash lock at a bounded native-tick cadence.
            // Dense/elite locks remain sticky; this never inserts an attack wait.
            if(retained!=null&&AssistTargetTier(retained)==(int)AssistPriority.Trash
                && retained.NormalizedXyDistanceToMe>NearbyTrashRange&&HasAssistNearbyTrash()&&!IsAssistHighValueWithinDash(retained))retained=null;
            bool nearbyTrash=HasAssistNearbyTrash();
            if (retained != null && AssistTargetTier(retained) == (int)AssistPriority.Trash
                && !_assistNavigationPending
                && (_assistTrashRecheckGameTick == int.MinValue || Hud.Game.CurrentGameTick - _assistTrashRecheckGameTick >= 15)
                && (AssistTrashClass(AssistTrashProfileFor(retained)) < 2
                    || (_assistTravelHeadingKnown && IsAssistSparseTrash(AssistTrashProfileFor(retained)))))
            { retained = null; _assistTrashRecheckGameTick = Hud.Game.CurrentGameTick; }
            IMonster best = retained;
            int bestTier = retained != null ? AssistTargetTier(retained) : -1;
            bool bestFromCurrentDamageCircle = CanAttackFromCurrentDamageCircle(best);
            foreach (var monster in _assistCandidates)
            {
                // Suspend an unsafe elite, not all combat. Safe minions/trash can fill
                // the danger window; the stored elite regains priority when safe.
                int tier = AssistTargetTier(monster);
                if(tier==(int)AssistPriority.Trash&&nearbyTrash&&monster.NormalizedXyDistanceToMe>NearbyTrashRange&&!IsAssistHighValueWithinDash(monster))continue;
                if (tier < 0) continue;
                if (tier < bestTier) continue;
                bool fromCurrentDamageCircle = CanAttackFromCurrentDamageCircle(monster);
                bool circleUpgrade = tier == bestTier && !AssistDensityFocusActive(retained)
                    && fromCurrentDamageCircle && !bestFromCurrentDamageCircle
                    && (retained == null || tier != (int)AssistPriority.Trash
                        || IsAssistCircleCandidateCompatible(monster, retained));
                bool blockedUpgrade = tier == bestTier && retained != null
                    && (!_assistNavigationPending || _assistNavigationReacquired)
                    && IsAssistTargetTemporarilyBlocked(retained) && !IsAssistTargetTemporarilyBlocked(monster);
                circleUpgrade |= blockedUpgrade;
                int danger = _rangedCleanup && tier == (int)AssistPriority.CtrlFocus ? AssistRangedDanger(monster) : 0;
                int bestDanger = _rangedCleanup && best != null && bestTier == (int)AssistPriority.CtrlFocus ? AssistRangedDanger(best) : 0;
                if (tier == bestTier && danger < bestDanger) continue;
                bool threatUpgrade = tier == bestTier && danger > bestDanger;
                circleUpgrade |= threatUpgrade;
                // Within the established elite tier, preserve the user's useful damage
                // circle before sticky-target preference, distance or alignment geometry.
                if (tier == bestTier && retained != null && !circleUpgrade) continue;
                if (tier == bestTier && tier != (int)AssistPriority.Trash
                    && bestFromCurrentDamageCircle && !fromCurrentDamageCircle) continue;
                if (tier == bestTier && !circleUpgrade && tier != 2 && best != null
                    && (AssistSelectionDistance(monster) > AssistSelectionDistance(best)
                        || (AssistSelectionDistance(monster) == AssistSelectionDistance(best)
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
                    || AssistSelectionDistance(monster) < AssistSelectionDistance(best)
                    || (AssistSelectionDistance(monster) == AssistSelectionDistance(best) && monster.AcdId < best.AcdId))
                {
                    best = monster; bestFromCurrentDamageCircle = fromCurrentDamageCircle;
                    if (circleUpgrade) retained = null;
                }
            }
            if (best != null && moltenReturn != null)
            {
                if (returning && AssistTargetTier(moltenReturn)>=bestTier)
                {
                    if (HasAssistAttackContact(moltenReturn)) _assistHazardReturnAcd=0u;
                    return RecordAssistSelection(moltenReturn,AssistTargetTier(moltenReturn),"hazard-return");
                }
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
            if (retained != null)
            {
                var grouped = PreferMovingElitePack(retained, now, canDash);
                return RecordAssistSelection(grouped, bestTier,
                    grouped != retained ? "moving-elite-pack" : bestTier == (int)AssistPriority.CtrlFocus ? "ctrl-focus" : "retained");
            }
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

        private readonly List<IActor> _assistMoltenTrails=new List<IActor>();
        private readonly List<IMonster> _assistLiveMolten=new List<IMonster>();
        private readonly HashSet<uint> _assistMoltenCaution=new HashSet<uint>();
        private uint _moltenCautionWorld;
        private const float AssistTrailClearance=3.5f;
        private const float AssistAuraClearance=6f; // Center distance: five-yard assumed aura plus margin.
        private string _moltenCautionReason="none";
        private void RefreshAssistMoltenCaution(uint world,IWorldCoordinate me)
        {
            if(_moltenCautionWorld!=world) { _assistMoltenCaution.Clear();_moltenCautionWorld=world; }
            _assistMoltenCaution.RemoveWhere(id=>!_assistLiveMolten.Any(m=>m.AcdId==id));
            if(_assistMoltenCaution.Count==0)_moltenCautionReason="none";
        }
        private bool AssistMoltenTrailContains(IWorldCoordinate point)
        {
            return _assistMoltenTrails.Any(a=>AssistSameHeight(point,a.FloorCoordinate)
                && point.XYDistanceTo(a.FloorCoordinate)<=AssistTrailClearance);
        }
        private bool IsAssistMoltenGroundFree(IWorldCoordinate point)
        {
            if(AssistShieldPylonActive() || _assistMoltenCaution.Count==0)return true;
            if(AssistMoltenTrailContains(point))return false;
            return !_assistLiveMolten.Any(m=>_assistMoltenCaution.Contains(m.AcdId)
                && AssistSameHeight(point,m.FloorCoordinate) && point.XYDistanceTo(m.FloorCoordinate)<AssistAuraClearance);
        }
        private void ObserveAssistMoltenLoss(IWorldCoordinate point,bool healthLoss,bool shieldLoss)
        {
            if(AssistShieldPylonActive() || (!healthLoss && !shieldLoss))return;
            bool trail=AssistMoltenTrailContains(point);
            foreach(var monster in _assistLiveMolten) {
                if(!AssistSameHeight(point,monster.FloorCoordinate)
                    || point.XYDistanceTo(monster.FloorCoordinate)>(trail ? 35 : 5.5))continue;
                _assistMoltenCaution.Add(monster.AcdId);
                if(monster.Pack!=null)foreach(var member in _assistLiveMolten)
                    if(member.Pack==monster.Pack)_assistMoltenCaution.Add(member.AcdId);
            }
            if(_assistMoltenCaution.Count>0)_moltenCautionReason=shieldLoss?"shield-loss-during-molten-exposure":"health-loss-during-molten-exposure";
        }
        private static bool AssistGroundSegmentClear(IWorldCoordinate from,IWorldCoordinate to,IWorldCoordinate center,float radius)
        {
            if(!AssistSameHeight(from,center) && !AssistSameHeight(to,center))return true;
            double dx=to.X-from.X,dy=to.Y-from.Y,length=dx*dx+dy*dy;
            double u=length>0.0001?Math.Max(0,Math.Min(1,((center.X-from.X)*dx+(center.Y-from.Y)*dy)/length)):0;
            double x=from.X+u*dx-center.X,y=from.Y+u*dy-center.Y;
            double start=from.XYDistanceTo(center),end=to.XYDistanceTo(center);
            // Escaping an existing overlap may move outward, never deeper through it.
            return start<=radius ? end>start+0.1 && x*x+y*y>=start*start-0.01 : x*x+y*y>radius*radius;
        }
        private bool IsAssistMoltenWalkFree(IWorldCoordinate to)
        {
            _assistMoltenWalkReason="clear";
            if(AssistShieldPylonActive() || _assistMoltenCaution.Count==0)return true;
            var from=Hud.Game.Me.FloorCoordinate;
            bool strict=true;
            foreach(var trail in _assistMoltenTrails)
                if(!AssistGroundSegmentClear(from,to,trail.FloorCoordinate,AssistTrailClearance)) { strict=false;break; }
            if(strict)foreach(var monster in _assistLiveMolten)
                if(_assistMoltenCaution.Contains(monster.AcdId)
                    && !AssistGroundSegmentClear(from,to,monster.FloorCoordinate,AssistAuraClearance)) { strict=false;break; }
            if(strict)return true;
            // Ordinary travel stays strict. Only escape from an existing overlap can relax.
            double travel=from.XYDistanceTo(to);
            if(IsAssistMoltenGroundFree(from) || !IsAssistMoltenGroundFree(to) || travel<0.5 || travel>12) {
                _assistMoltenWalkReason="molten-overlap-endpoint-or-distance";return false;
            }
            // Never add exposure to a trail/aura which does not already overlap the hero.
            foreach(var trail in _assistMoltenTrails)
                if(AssistSameHeight(from,trail.FloorCoordinate) && from.XYDistanceTo(trail.FloorCoordinate)>AssistTrailClearance
                    && !AssistGroundSegmentClear(from,to,trail.FloorCoordinate,AssistTrailClearance)) {
                    _assistMoltenWalkReason="molten-new-trail-crossing";return false;
                }
            foreach(var monster in _assistLiveMolten)
                if(_assistMoltenCaution.Contains(monster.AcdId) && AssistSameHeight(from,monster.FloorCoordinate)
                    && from.XYDistanceTo(monster.FloorCoordinate)>AssistAuraClearance
                    && !AssistGroundSegmentClear(from,to,monster.FloorCoordinate,AssistAuraClearance)) {
                    _assistMoltenWalkReason="molten-new-aura-crossing";return false;
                }
            double start=AssistMoltenExposureDepth(from);
            int steps=Math.Max(1,(int)Math.Ceiling(travel/0.5));
            for(int i=1;i<=steps;i++) {
                float fraction=(float)i/steps;
                var point=from.Offset((to.X-from.X)*fraction,(to.Y-from.Y)*fraction,(to.Z-from.Z)*fraction);
                if(AssistMoltenExposureDepth(point)>start+0.01) {
                    _assistMoltenWalkReason="molten-overlap-deeper";return false;
                }
            }
            _assistMoltenWalkReason="molten-overlap-exit";return true;
        }
        private string _assistMoltenWalkReason="none";
        private double AssistMoltenExposureDepth(IWorldCoordinate point)
        {
            double depth=0;
            foreach(var trail in _assistMoltenTrails)
                if(AssistSameHeight(point,trail.FloorCoordinate))
                    depth+=Math.Max(0,1-point.XYDistanceTo(trail.FloorCoordinate)/AssistTrailClearance);
            foreach(var monster in _assistLiveMolten)
                if(_assistMoltenCaution.Contains(monster.AcdId) && AssistSameHeight(point,monster.FloorCoordinate))
                    depth+=Math.Max(0,1-point.XYDistanceTo(monster.FloorCoordinate)/AssistAuraClearance);
            return depth; // Geometric overlap proxy, not measured incoming damage.
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
            _assistMoltenTrails.Clear();_assistLiveMolten.Clear();
            _assistPositionArcane.Clear();
            _assistPositionOrbiter.Clear();
            _assistPositionElectrified.Clear();
            _lowHealthFields.Clear();
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
                    if(sno==ActorSnoEnum._monsteraffix_molten_trail && AssistSameHeight(me,actor.FloorCoordinate))
                        _assistMoltenTrails.Add(actor);
                    if (_lowHealthFields.Count < 32 && LowHealthFieldRadius(actor) > 0)
                        _lowHealthFields.Add(actor);
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
                    if(monster!=null && monster.WorldId==world && IsAssistPositionWorldPoint(monster.FloorCoordinate)
                        && me.XYDistanceTo(monster.FloorCoordinate)<=AssistAffixScanRange
                        && HasAssistAffix(monster,MonsterAffix.Molten))_assistLiveMolten.Add(monster);
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
                RefreshAssistMoltenCaution(world,me);
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

        // Shield Pylon suppresses damage avoidance only. Native geometry, failed
        // landings, UI guards and buff upkeep keep their independent contracts.
        private bool AssistShieldPylonActive()
        {
            return AssistShieldPylonSeconds()>1.5;
        }
        private double AssistShieldPylonSeconds()
        {
            try {
                // Native Shield pylon SNO used by the default HUD buff list.
                var buff=Hud.Game.Me.Powers.GetBuff(266254u);
                if(buff==null || !buff.Active || buff.TimeLeftSeconds==null)return 0;
                double remaining=0;
                foreach(double seconds in buff.TimeLeftSeconds)
                    if(!double.IsNaN(seconds) && !double.IsInfinity(seconds))remaining=Math.Max(remaining,seconds);
                return remaining;
            } catch { return 0; }
        }

        // Walking gets 500 ms more warning than Dash, because it covers distance
        // gradually. This changes explosion timing only; attack reach/spacing stay separate.
        // Once a walking exit begins, retain its urgency until clear or Dash takes over.
        private int AssistExplosionWalkingLeadTicks()
        {
            if (_assistExplosionWalkCommitted) return 30;
            int tick = Hud.Game.CurrentGameTick;uint world = Hud.Game.Me.WorldId;
            if (tick != _assistExplosionAvailabilityTick || world != _assistExplosionAvailabilityWorld)
            {
                _assistExplosionAvailabilityTick = tick;_assistExplosionAvailabilityWorld = world;
                _assistExplosionWalkLeadTicks = AutoDash && CanApproachDash(AssistDoorDashSkill()) ? 0 : 30;
            }
            return _assistExplosionWalkLeadTicks;
        }

        private bool IsAssistMoltenBlocking(AssistPositionMolten hazard)
        {
            if (AssistShieldPylonActive()) return false;
            if (hazard.Grotesque)
                return Hud.Game.CurrentGameTick - hazard.StartTick >= Math.Max(0,AssistGrotesqueDodgeTicks-AssistExplosionWalkingLeadTicks())
                    && Hud.Game.CurrentGameTick <= hazard.EndTick;
            if (hazard.Actor == null || hazard.Actor.IsDisabled) return false;
            if (!hazard.CountingDown) return true;
            // Native molten buildup is three seconds at 60 game ticks per second.
            double remaining = 3.0 - (Hud.Game.CurrentGameTick - hazard.Actor.CreatedAtInGameTick) / 60.0;
            return remaining <= AssistMoltenEscapeSeconds + AssistExplosionWalkingLeadTicks()/60.0;
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
            if (!_assistPositionReadable || !IsAssistPositionWorldPoint(point)) return false;
            if (AssistShieldPylonActive()) return true;
            if (!IsAssistFrozenPulsePointFree(point))return false;
            if (!IsAssistMoltenGroundFree(point))return false;
            if (!IsAssistArcanePointCoreFree(point)) return false;
            foreach (var core in _assistPositionOrbiter)
                if (core != null && !core.IsDisabled && AssistSameHeight(point, core.FloorCoordinate) && point.XYDistanceTo(core.FloorCoordinate) <= AssistOrbiterRadius(core)) return false;
            foreach (var hazard in _assistPositionMolten)
                if (IsAssistMoltenBlocking(hazard) && AssistSameHeight(point, hazard.Center)
                    && point.XYDistanceTo(hazard.Center) <= AssistExplosionClearance(hazard))
                    return false;
            // One field predicate owns urgency, landing exclusion and exit selection.
            if (!IsAssistLowHealthFieldPointFree(point)) return false;
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
            if (AssistShieldPylonActive()) return true;
            foreach (var actor in _assistPositionArcane)
                if (actor != null && !actor.IsDisabled && AssistSameHeight(point, actor.FloorCoordinate)
                    && point.XYDistanceTo(actor.FloorCoordinate) <= AssistArcaneCoreAvoidanceRadius)
                    return false;
            return true;
        }

        private bool AssistElectrifiedSpacingDue()
        {
            if (AssistShieldPylonActive()) return false;
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
            if (AssistShieldPylonActive()) return true;
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

        private float AssistContactEdge(IMonster target)
        {
            if(!AssistShieldPylonActive() && _assistMoltenCaution.Contains(target.AcdId))
                return Math.Max(7f,target.RadiusScaled+1.5f);
            return target.RadiusScaled + ((!AssistShieldPylonActive() && (HasAssistAffix(target, MonsterAffix.Electrified)
                || HasAssistAffix(target, MonsterAffix.FireChains))) ? AssistElectrifiedLandingGap : 1.5f);
        }

        private bool AssistReactiveEscapeDue()
        {
            if (AssistShieldPylonActive()) return false;
            _assistArcaneEscapeReason = "none";
            if (!RefreshAssistPositionActors()) return false;
            if (!_assistArcaneCoreUnsafe && !_assistOrbiterCoreUnsafe && !_assistDamageEscapePending && !LowHealthFieldEscapeDue()) return false;
            _assistArcaneEscapeReason = _assistArcaneCoreUnsafe ? "arcane-core" : _assistOrbiterCoreUnsafe ? "orbiter-core"
                : !IsAssistFrozenPulsePointFree(Hud.Game.Me.FloorCoordinate) ? "frozen-pulse" : LowHealthFieldEscapeDue() ? "health-gated-field" : _assistDamageEscapeTrigger;
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
            if (AssistShieldPylonActive()) return 0.0;
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
            ref IWorldCoordinate best, ref int bestPriority, ref double bestScore, bool walkingExit = false)
        {
            if (walkingExit && !IsAssistEscapeWalkPointAllowed(candidate)) return;
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
            double score = clearance + opposite * 2.0 - travel * 0.025
                + AssistSoftHazardPositionScore(candidate, target);
            if (best != null && (priority < bestPriority || (priority == bestPriority && score <= bestScore))) return;
            best = candidate; bestPriority = priority; bestScore = score;
        }

        private bool TryPickAssistReactiveEscapePoint(IMonster target,
            out IWorldCoordinate point, out string reason, out bool escapeOnly, bool walkingExit = false)
        {
            point = null; reason = "none"; escapeOnly = false;
            if (!RefreshAssistPositionActors()) return false;
            if (_rangedCleanup && (target == null || target.Rarity != ActorRarity.Boss) && (_assistDamageEscapeTrigger=="low-health-rapid-loss" || LowHealthFieldEscapeDue()))
            {
                int evaluated=0;
                foreach (var threat in _assistCandidates.Where(IsCleanupCandidate).OrderBy(m=>m.NormalizedXyDistanceToMe))
                {
                    if (++evaluated>4) break;
                    IWorldCoordinate landing; string landingReason;
                    if (!TryPickAssistDashPoint(threat,true,out landing,out landingReason)) continue;
                    int rank=-1; double score=double.MinValue;
                    ConsiderAssistReactiveContact(landing,threat,ref point,ref rank,ref score,walkingExit);
                    if (point!=null) { reason="panic-ranged-contact"; return true; }
                }
            }
            string contactReason;
            IWorldCoordinate contact;
            int bestPriority = -1;
            double contactScore = double.MinValue;
            bool sustainedPressure = !_rangedCleanup && _assistHealthFraction <= 0.40
                && _lastPanicContactTick != int.MinValue && target != null
                && _lastPanicContactTarget == target.AcdId
                && unchecked(Environment.TickCount-_lastPanicContactTick)<2000;
            if (!sustainedPressure && target != null && IsAssistEligible(target))
            {
                if (TryPickAssistDashPoint(target, true, out contact, out contactReason))
                    ConsiderAssistReactiveContact(contact, target, ref point, ref bestPriority, ref contactScore,walkingExit);
                // Sample the outer reachable body edge as well as the existing contact
                // choice: a small core-free hop can still leave us beside the same beam.
                float edge = target.RadiusScaled + (float)Math.Max(1.5,
                    Math.Min(AssistAttackReachYards, MeleeAssistRange) - 1.0);
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistReactiveContact(target.FloorCoordinate.Offset(
                        AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target,
                        ref point, ref bestPriority, ref contactScore,walkingExit);
                if (point != null)
                { reason = _assistArcaneEscapeReason + ":reactive-contact"; return true; }
            }
            // No safe elite contact, or another low-health panic immediately after it:
            // find local healing contact without promoting ranged targets or distant pursuit.
            int localEvaluated=0;
            foreach (var nearby in _assistCandidates.OrderBy(m=>m.NormalizedXyDistanceToMe))
            {
                if (nearby==target || !IsAssistEligible(nearby) || nearby.NormalizedXyDistanceToMe>15f) continue;
                if (++localEvaluated>6) break;
                if (!TryPickAssistDashPoint(nearby,true,out contact,out contactReason)) continue;
                ConsiderAssistReactiveContact(contact,nearby,ref point,ref bestPriority,ref contactScore,walkingExit);
            }
            if (point!=null) { reason="panic-local-healing-contact"; return true; }
            if (walkingExit && TryPickAssistWalkCombatPoint(target,out point,AssistDamageEscapeHopYards + 1.0f))
            { reason = _assistArcaneEscapeReason + ":safe-combat-exit"; return true; }
            var me = Hud.Game.Me.FloorCoordinate;
            double bestScore = double.MaxValue;
            var nearest = _assistArcaneNearestActor;
            if (_assistOrbiterCoreUnsafe && _assistOrbiterNearestActor != null) nearest = _assistOrbiterNearestActor;
            float exitRadius = nearest != null && nearest == _assistOrbiterNearestActor
                ? AssistOrbiterRadius(nearest) + 1.5f : AssistArcaneCoreAvoidanceRadius + 1.5f;
            // A large known field may exceed the usual small reposition radius.
            // Add only eight boundary candidates for the deepest overlapping field.
            IActor fieldExit = null; double overlap = 0;
            if (LowHealthFieldEscapeDue())
                foreach (var field in _lowHealthFields)
                {
                    if (field.IsDisabled || !AssistSameHeight(me, field.FloorCoordinate)) continue;
                    if (!IsAssistFieldBlocking(field))continue;
                    double depth = LowHealthFieldRadius(field) - me.XYDistanceTo(field.FloorCoordinate);
                    if (depth >= overlap) { overlap = depth; fieldExit = field; }
                }
            if (fieldExit != null)
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistEscapePoint(fieldExit.FloorCoordinate.Offset(
                        AssistEdgeX[i] * (float)(LowHealthFieldRadius(fieldExit) + 1),
                        AssistEdgeY[i] * (float)(LowHealthFieldRadius(fieldExit) + 1), 0f),
                        target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f,walkingExit);
            // Bounded exits: eight core-boundary points and sixteen surrounding points.
            for (int i = 0; i < AssistEdgeX.Length; i++)
            {
                if (nearest != null && !nearest.IsDisabled)
                    ConsiderAssistEscapePoint(nearest.FloorCoordinate.Offset(
                        AssistEdgeX[i] * exitRadius,
                        AssistEdgeY[i] * exitRadius, 0f),
                        target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f,walkingExit);
                ConsiderAssistEscapePoint(me.Offset(
                    AssistEdgeX[i] * Math.Max(exitRadius, AssistDamageEscapeHopYards + 1.5f),
                    AssistEdgeY[i] * Math.Max(exitRadius, AssistDamageEscapeHopYards + 1.5f), 0f),
                    target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f,walkingExit);
                ConsiderAssistEscapePoint(me.Offset(
                    AssistEdgeX[i] * (AssistArcaneCoreAvoidanceRadius * 2f + 2f),
                    AssistEdgeY[i] * (AssistArcaneCoreAvoidanceRadius * 2f + 2f), 0f),
                    target, ref point, ref bestScore, AssistDamageEscapeHopYards + 1.0f,walkingExit);
            }
            if (point == null) return false;
            escapeOnly = true; reason = _assistArcaneEscapeReason + ":exit";
            return true;
        }

        private bool AssistMoltenEscapeDue()
        {
            if (AssistShieldPylonActive()) return false;
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
            if(!IsAssistMoltenGroundFree(me)) { _assistPositionEscapeReason="molten-ground";return true; }
            return false;
        }

        private bool IsAssistDashWithinReach(IWorldCoordinate point)
        {
            return IsAssistPositionWorldPoint(point)
                && point.XYDistanceTo(Hud.Game.Me.FloorCoordinate) <= AssistDashTravelLimit;
        }

        private bool IsAssistDashPointSafe(IWorldCoordinate point, IMonster target, out int x, out int y,
            bool escapeOnly = false, bool mapTransit = false, bool planOnly = false, bool upkeep = false)
        {
            x = y = 0;
            try
            {
                bool stationaryUpkeep = upkeep && IsAssistPositionWorldPoint(point)
                    && point.XYDistanceTo(Hud.Game.Me.FloorCoordinate) <= 0.25f
                    && Math.Abs(point.Z - Hud.Game.Me.FloorCoordinate.Z) <= 0.25f;
                if ((!Hud.Window.IsForeground || !_clickUiReadable)) return false;
                // Fatal explosion exits outrank optional Electrified spacing. Actual
                // collision, blast exclusion, Arcane/Orbiter cores and UI guards remain hard.
                bool explosionDue=AssistMoltenEscapeDue();
                bool explosionExit = escapeOnly && explosionDue;
                // Land beyond the trigger boundary so rounding cannot immediately rearm it.
                if(!stationaryUpkeep && explosionDue && IsAssistPositionWorldPoint(point))
                    foreach(var hazard in _assistPositionMolten)
                        if(IsAssistMoltenBlocking(hazard) && AssistSameHeight(point,hazard.Center)
                            && point.XYDistanceTo(hazard.Center)<=AssistExplosionClearance(hazard)+1f)return false;
                if (!stationaryUpkeep && ((!RefreshAssistPositionActors())
                    || (!IsAssistPositionWorldPoint(point)) || ((!planOnly && !IsAssistDashWithinReach(point)))
                    || (IsAssistEscapeLandingBlocked(point))
                    || (IsAssistFailedLandingBlocked(point, target, escapeOnly || _assistDamageEscapePending
                        || AssistElectrifiedSpacingDue() || !IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate)))
                    || ((escapeOnly && !AssistSameHeight(point, Hud.Game.Me.FloorCoordinate)))
                    || (!IsAssistObstaclePointFree(point))
                    || ((point.XYDistanceTo(Hud.Game.Me.FloorCoordinate)>0.25f&&!MapAssistLandingAllowed(point)))
                    || (!IsAssistPositionHazardFree(point))
                    || ((!explosionExit && !IsAssistElectrifiedPointFree(point, target))))) return false;
                if (!stationaryUpkeep && _assistDamageEscapePending && !AssistShieldPylonActive())
                {
                    double hopX = point.X - _assistDamageEscapeOriginX, hopY = point.Y - _assistDamageEscapeOriginY;
                    // Plan one yard beyond the required actual hop, allowing landing tolerance.
                    float plannedHop = AssistDamageEscapeHopYards + 1.0f;
                    if ((hopX * hopX + hopY * hopY < plannedHop * plannedHop)) return false;
                }
                // A captured pure escape has no attack endpoint. Ordinary/contact Dash keeps
                // the retained target and its conservative contact requirement unchanged.
                if (!stationaryUpkeep && !escapeOnly && !mapTransit && ((!IsAssistEligible(target)) || (target.WorldId != _assistPositionWorldId)
                    || (!IsAssistPositionWorldPoint(target.FloorCoordinate))
                    || (Math.Abs(point.Z - target.FloorCoordinate.Z) > 8f)
                    || !AssistPositionFinite(target.RadiusScaled) || target.RadiusScaled < 0f
                    || (!CanAttackAssistFrom(point, target))
                    // Contact requires a clear short ray from landing to target. This
                    // does not require a walkable ray along the Dash across a wall.
                    || (MapAssistAttackBlocked(point, target))
                    || (AssistBodyGap(point, target) < 1.0f) || (AssistChainDistance(point, target) < 3.0))) return false;
                var screen = point.ToScreenCoordinate(true, true);
                if (screen == null || !AssistPositionFinite(screen.X) || !AssistPositionFinite(screen.Y)) return false;
                double cx = Math.Round(screen.X), cy = Math.Round(screen.Y);
                if ((cx < 0 || cy < 0 || cx >= Hud.Window.Size.Width || cy >= Hud.Window.Size.Height)) return false;
                if ((!escapeOnly && _assistMainKey == ActionKey.LeftSkill && !IsLeftClickPointSafe(cx, cy))) return false;
                if ((_hudMenu != null && _hudMenu.IsAutomationLeftClickBlocked((float)cx, (float)cy))) return false;
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
            if (AssistShieldPylonActive()) return true;
            var current = Hud.Game.Me.FloorCoordinate;
            // In attack range we use scoped standstill, so no walk through the target core is planned.
            if (IsAssistPositionHazardFree(current) && CanAttackAssistFrom(current, target)) return true;
            if (!IsAssistOtherCoreFollowFree(target) || !IsAssistArcaneNativeFollowCoreFree(target)) return false;
            var contact=target.FloorCoordinate;
            var origin=Hud.Game.Me.FloorCoordinate;
            double reach=Math.Max(1,Math.Min(8,MeleeAssistRange))+target.RadiusScaled;
            double length=origin.XYDistanceTo(contact);
            if(length>reach)contact=origin.Offset((float)((contact.X-origin.X)*(length-reach)/length),
                (float)((contact.Y-origin.Y)*(length-reach)/length),(float)((contact.Z-origin.Z)*(length-reach)/length));
            else contact=origin;
            if(!IsAssistMoltenWalkFree(contact))return false;
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
            if (_assistRecoveryCombatOnly && !IsAssistReachableCombat(target)
                && (MapAssistTargetBlocked(target) || IsAssistBlockedApproachDirection(target.FloorCoordinate))) return false;
            if ((IsAssistRouteSuppressed(target) && !TryReopenAssistCircleRoute(target,canDash)) || !RefreshAssistPositionActors())
            {
                
                return false;
            }
            // Attacks and executable escape are independent. An unavailable dodge
            // never removes a reachable combat target; escape is dispatched first.
            if (CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)) return true;
            if ((_assistPositionMolten.Count == 0 && _assistPositionArcane.Count == 0
                && _assistPositionOrbiter.Count == 0 && _assistMoltenCaution.Count == 0)
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
                || IsAssistDashPointSafe(target.FloorCoordinate, target, out x, out y, planOnly: true)) return true;
            float edge = AssistContactEdge(target);
            for (int i = 0; i < AssistEdgeX.Length; i++)
                if (IsAssistDashPointSafe(target.FloorCoordinate.Offset(
                    AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target, out x, out y, planOnly: true)) return true;
            
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
                if (AssistBodyGap(center, target) > AssistCircleTargetGapYards) continue;
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
            if (AssistShieldPylonActive()) return double.PositiveInfinity;
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
            if (!AssistShieldPylonActive() && HasAssistAffix(target, MonsterAffix.Electrified))
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
                score += Math.Min(8.0, Math.Sqrt(dx * dx + dy * dy)) * 0.10;
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
                bool contact = CanAttackAssistFrom(point, monster);
                if (!contact && (monster.AcdId == target.AcdId || distance > aimLength + 13.0)) continue;
                double forward = dx * aimX + dy * aimY;
                double lateral = Math.Abs(dx * aimY - dy * aimX);
                bool elite = IsAssistPositionElite(monster);
                // Anticipate followers behind the elite; verified in-range bodies still
                // score higher than projected arrivals outside generator reach.
                if (monster.AcdId != target.AcdId && forward > 0
                    && lateral <= monster.RadiusScaled + 2.5)
                    score += contact ? (elite ? 3.0 : 1.0) : (elite ? 1.0 : 0.35);
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
                - Hud.Game.Me.FloorCoordinate.XYDistanceTo(point) * 0.025
                - (IsNearProtectedPylon(point) ? 2.0 : 0.0)
                + AssistSoftHazardPositionScore(point, target); // Preferences cannot suspend attacks.
            bool compareCoverage=best!=null && CanAttackAssistFrom(best,target) && CanAttackAssistFrom(point,target);
            double coverage=compareCoverage ? AssistCoverageValue(point,target) : 0;
            double previousCoverage=compareCoverage ? AssistCoverageValue(best,target) : 0;
            bool coverageGain=compareCoverage && coverage>previousCoverage*1.15+1.0;
            if (compareCoverage && previousCoverage>coverage*1.15+1.0) return;
            if (!coverageGain && best != null && (priority < bestPriority || (priority == bestPriority && score <= bestScore))) return;
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
                    || IsAssistFailedCircleContact(circle.Actor.AcdId,target)
                    || !IsAssistUsefulCircleTarget(target)
                    || (circle.Kind == 2 && !IsAssistPositionElite(target))) continue;
                var center = circle.Actor.FloorCoordinate;
                if (AssistBodyGap(center, target) > AssistCircleTargetGapYards) continue;
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
            // Wider contact samples serve boundary targets and cautious aura spacing.
            float outer=target.RadiusScaled+(float)Math.Max(1,Math.Min(7.5,MeleeAssistRange-0.5));
            if(outer>edge+1f && (_assistBoundaryGoals.ContainsKey(target.AcdId) || _assistMoltenCaution.Contains(target.AcdId))) for(int i=0;i<AssistEdgeX.Length;i++)
                ConsiderAssistDashPoint(targetPoint.Offset(AssistEdgeX[i]*outer,AssistEdgeY[i]*outer,0f),
                    target,0,0.5,"outer-contact",ref best,ref bestPriority,ref bestScore,ref reason,planOnly);
            ConsiderAssistDashPoint(targetPoint.Offset(0f, 0f, 0f), target, 0, 0.0, "target",
                ref best, ref bestPriority, ref bestScore, ref reason, planOnly);
            if (best == null) return false;
            // Refresh in place when already in an equally useful circle at melee contact.
            // A better damage circle can still outrank blue CDR; hazards always outrank both.
            if (!hazardEscape && currentSafe && currentPriority > 0 && currentPriority >= bestPriority
                && !AssistCoverageOutweighs(best,target,currentPoint,target))
            { best = currentPoint.Offset(0f, 0f, 0f); reason = "keep-current-circle"; }
            if (hazardEscape) reason = "molten-escape:" + reason;
            return true;
        }

        private void ConsiderAssistEscapePoint(IWorldCoordinate point, IMonster target,
            ref IWorldCoordinate best, ref double bestScore, float minimumTravel = 0f, bool walkingExit = false)
        {
            if (walkingExit && !IsAssistEscapeWalkPointAllowed(point)) return;
            int x, y;
            if (!IsAssistDashPointSafe(point, target, out x, out y, true)) {
                
                return;
            }
            double score = Hud.Game.Me.FloorCoordinate.XYDistanceTo(point);
            if (score < minimumTravel) return; // Reactive exits; ordinary molten retains zero.
            if (target != null && IsAssistPositionWorldPoint(target.FloorCoordinate))
                score += point.XYDistanceTo(target.FloorCoordinate) * 0.05;
            foreach(var candidate in _assistCandidates)
                if(IsAssistMonsterValid(candidate) && AssistTargetTier(candidate)>=0
                    && AssistBodyGap(point,candidate)<=Math.Min(AssistAttackReachYards,MeleeAssistRange)
                    && CanAttackAssistFrom(point,candidate) && !MapAssistAttackBlocked(point,candidate))
                { score-=60;break; }
            if (best != null && score >= bestScore) return;
            best = point; bestScore = score;
        }

        private bool TryPickAssistMoltenEscapePoint(IMonster target, out IWorldCoordinate best,
            out string reason, out bool escapeOnly, bool walkingExit = false)
        {
            escapeOnly = false;
            // Continue hitting the retained elite from outside the blast whenever contact exists.
            if (TryPickAssistDashPoint(target, true, out best, out reason)
                && (!walkingExit || IsAssistEscapeWalkPointAllowed(best))) return true;
            if(!walkingExit && AssistMoltenEscapeDue())
            {
                IWorldCoordinate contact=null;double contactScore=double.MaxValue;
                foreach(var candidate in _assistCandidates.Where(m=>IsAssistMonsterValid(m)
                    && AssistTargetTier(m)>=0 && m.NormalizedXyDistanceToMe<=35)
                    .OrderBy(m=>m.NormalizedXyDistanceToMe).Take(8))
                {
                    float radius=candidate.RadiusScaled+6f;
                    for(int i=0;i<AssistEdgeX.Length;i++)
                    {
                        var point=candidate.FloorCoordinate.Offset(AssistEdgeX[i]*radius,AssistEdgeY[i]*radius,0);
                        if(CanAttackAssistFrom(point,candidate) && !MapAssistAttackBlocked(point,candidate))
                            ConsiderAssistEscapePoint(point,target,ref contact,ref contactScore);
                    }
                }
                if(contact!=null)
                { best=contact;reason="safe-target-exit";escapeOnly=true;return true; }
            }
            if (walkingExit && TryPickAssistWalkCombatPoint(target,out best))
            { reason = "safe-combat-exit"; return true; }
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
                float edge = AssistExplosionClearance(hazard) + 2.0f;
                var center = hazard.Center;
                double dx = me.X - center.X, dy = me.Y - center.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 0.01)
                    ConsiderAssistEscapePoint(center.Offset(
                        (float)(dx * edge / distance), (float)(dy * edge / distance), 0f),
                        target, ref best, ref bestScore,0f,walkingExit);
                // Deterministic boundary samples, bounded by the existing native actor cache.
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistEscapePoint(center.Offset(
                        AssistEdgeX[i] * edge, AssistEdgeY[i] * edge, 0f), target, ref best, ref bestScore,0f,walkingExit);
            }
            if(best==null && _assistMoltenCaution.Count>0) {
                foreach(var trail in _assistMoltenTrails.OrderBy(a=>me.XYDistanceTo(a.FloorCoordinate)).Take(8))for(int i=0;i<AssistEdgeX.Length;i++)
                    ConsiderAssistEscapePoint(trail.FloorCoordinate.Offset(AssistEdgeX[i]*(AssistTrailClearance+1),AssistEdgeY[i]*(AssistTrailClearance+1),0),
                        target,ref best,ref bestScore,0f,walkingExit);
                foreach(var monster in _assistLiveMolten.OrderBy(m=>me.XYDistanceTo(m.FloorCoordinate)).Take(8)) {
                    if(!_assistMoltenCaution.Contains(monster.AcdId))continue;
                    for(int i=0;i<AssistEdgeX.Length;i++)
                        ConsiderAssistEscapePoint(monster.FloorCoordinate.Offset(AssistEdgeX[i]*7,AssistEdgeY[i]*7,0),
                            target,ref best,ref bestScore,0f,walkingExit);
                }
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

        private static readonly HashSet<ActorSnoEnum> AssistDoorBlacklist = new HashSet<ActorSnoEnum>
        {
            ActorSnoEnum._cald_merchant_cart,
            ActorSnoEnum._a2dun_cald_exit_gate,
            ActorSnoEnum._a2dunswr_gates_causeway_gates_non_op,
            ActorSnoEnum._a2dun_cald_belial_acid_attack,
            ActorSnoEnum._a2dun_cald_belial_room_gate_a,
            ActorSnoEnum._trout_cultists_summoning_portal_b,
            ActorSnoEnum._caout_target_dummy,
            ActorSnoEnum._start_location_team_0,
            ActorSnoEnum._a3dun_crater_st_demon_chainpylon_fire_azmodan,
            ActorSnoEnum._a3dun_keep_bridge,
            ActorSnoEnum._a3dun_rmpt_frozendoor_a,
            ActorSnoEnum._catapult_a3dunkeep_warmachines_snow_firing,
            ActorSnoEnum._x1_crusader_trebuchet_pending_tar,
            ActorSnoEnum._event_1000monster_portal,
            ActorSnoEnum._a2dun_zolt_sandbridgebase_bossfight,
            ActorSnoEnum._px_highlands_camp_resurgentcult_portal,
            ActorSnoEnum._x1_bog_catacombsportal_beaconloc,
            ActorSnoEnum._x1_malthael_boss_orb_collapse,
            ActorSnoEnum._caout_oasis_mine_entrance_a,
            ActorSnoEnum._trout_leor_painting,
            ActorSnoEnum._a4dun_sigil_room_platform_a,
            ActorSnoEnum._a3dun_rmpt_catapult_follower_event_gate,
            ActorSnoEnum._a1dun_leor_jail_door_superlocked_a_fake,
            ActorSnoEnum._cos_pet_mimic_01,
            ActorSnoEnum._shoulderpads_norm_base_flippy,
            ActorSnoEnum._x1_abattoir_barricade_solid,
            ActorSnoEnum._x1_fortress_floatrubble_a,
            ActorSnoEnum._a3dun_keep_barrel_snow_no_skirt,
            ActorSnoEnum._x1_fortress_crystal_prison_shield,
            ActorSnoEnum._x1_westm_railing_a_01_piece1,
            ActorSnoEnum._x1_pand_hexmaze_corpse,
            ActorSnoEnum._dh_companion_runec,
            ActorSnoEnum._loottype2_tristramvillager_male_c_corpse_01,
            ActorSnoEnum._uber_bossworld3_st_demon_chainpylon_fire_azmodan,
            ActorSnoEnum._trdun_crypt_skeleton_king_throne_parts,
            ActorSnoEnum._double_crane_a_caout_miningevent_chest_minievent,
            ActorSnoEnum._p6_church_bloodchannel_a,
            ActorSnoEnum._a4dun_sigil_tile_invis_wall,
            ActorSnoEnum._p1_tgoblin_gate,
            ActorSnoEnum._p1_tgoblin_vault_door,
            ActorSnoEnum._x1_urzael_soundspawner,
            ActorSnoEnum._x1_urzael_soundspawner_02,
            ActorSnoEnum._x1_urzael_soundspawner_03,
            ActorSnoEnum._x1_urzael_soundspawner_04,
            ActorSnoEnum._x1_westm_ex,
            ActorSnoEnum._trout_cath_entrance_door,
            // Vision scenery: flagged clickable, but never opened in the REV25 log.
            ActorSnoEnum._p76_trout_oldtristram_exit_gate,
            ActorSnoEnum._x1_westm_shacklebar,
            ActorSnoEnum._x1_westm_shacklebar_b,
            ActorSnoEnum._x1_westm_stock,
        };

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

        private bool IsAssistVisionWorld()
        {
            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null) return false;
                if (IsP76WorldSno(me.WorldSno)) return true;
                var area = me.SnoArea;
                string code = area != null ? area.Code : null;
                return !string.IsNullOrEmpty(code) && code.IndexOf("p76", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool IsP76WorldSno(uint sno)
        {
            switch (sno)
            {
                case 488371u: case 488457u: case 488658u: case 488686u: case 488695u:
                case 488725u: case 488760u: case 488769u: case 488786u: case 488792u:
                case 488817u: case 488826u: case 488829u: case 488837u: case 488862u:
                    return true;
                default: return false;
            }
        }

        private bool IsAssistClosedDoor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null || actor.SnoActor.Type != ActorType.Gizmo
                || actor.IsDisabled || actor.IsOperated || AssistDoorBlacklist.Contains(actor.SnoActor.Sno)
                || actor.WorldId != Hud.Game.Me.WorldId || !IsAssistPositionWorldPoint(actor.FloorCoordinate)
                || actor.SnoActor.Kind == ActorKind.Shrine
                || actor.SnoActor.Kind == ActorKind.Portal) return false;
            // Vision layouts contain scenery doors; never dispatch door trips there.
            // Use the same native world/area identity rule as AutoLoot.
            if (IsAssistVisionWorld()) return false;
            if (IsAssistBreakableDoor(actor)) return actor.Hitpoints > 0;
            bool namedDoor = actor.GizmoType == GizmoType.Door || actor.GizmoType == GizmoType.Gate;
            return (namedDoor || IsAssistBridge(actor.SnoActor.Sno))
                && actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Door_Locked, 0u, 0) == 0;
        }

        private int AssistDoorFailureCount(IActor actor)
        {
            AssistDoorFailure failure;
            if (!_assistDoorFailures.TryGetValue(actor.AcdId, out failure)) return 0;
            if (failure.DetourEliteAcd != 0u)
            {
                // Returning to combat must not rearm a failed optional detour.
                if (failure.World == Hud.Game.Me.WorldId
                    && FindAssistCandidate(failure.DetourEliteAcd) != null) return failure.Count;
                _assistDoorFailures.Remove(actor.AcdId); return 0;
            }
            var me = Hud.Game.Me.FloorCoordinate;
            var point = actor.FloorCoordinate;
            if (failure.World != Hud.Game.Me.WorldId
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
            { Count = _assistDoorEliteDetour ? Math.Max(2, count + 1) : count + 1,
                DetourEliteAcd = _assistDoorEliteDetour ? _assistDoorReturnEliteAcd : 0u,
                PlayerX = me.X, PlayerY = me.Y,
                DoorX = point.X, DoorY = point.Y, World = Hud.Game.Me.WorldId };
            var combat=FindAssistCandidate(_assistTargetAcd);
            if (combat!=null && !IsAssistReachableCombat(combat))
            {
                if (_assistDoorFailures[actor.AcdId].Count>=2) BlockAssistDoorGoal(combat);
                else if (MapAssistTargetBlocked(combat)) BlockAssistNavigationTarget(combat);
            }
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
            var screen = actor.FloorCoordinate.ToScreenCoordinate(true, true);
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

        private void BlockAssistDoorGoal(IMonster goal)
        {
            if (goal == null) return;
            BlockAssistNavigationTarget(goal,noProgress:true);
            // Changing IDs in the same failed pack cannot restart the same door chase.
            // Nearby attackable monsters are never part of this exclusion.
            foreach (var candidate in _assistCandidates)
                if (candidate.AcdId != goal.AcdId && candidate.WorldId == goal.WorldId
                    && candidate.FloorCoordinate.XYDistanceTo(goal.FloorCoordinate) <= 20f
                    && !IsAssistReachableCombat(candidate))
                    BlockAssistNavigationTarget(candidate,noProgress:true);
        }

        private bool MapAssistTargetDoorBlocked(IActor door)
        {
            var status=MapAssistStatus();
            if(status==null) return Hud.Game.Me.FloorCoordinate.XYDistanceTo(door.FloorCoordinate)>12f;
            // Missing route geometry permits only a bounded local native attempt.
            return _assistDoorPathState != "AtApproach"
                && Hud.Game.Me.FloorCoordinate.XYDistanceTo(door.FloorCoordinate)>12f;
        }

        private bool IsAssistReachableCombat(IMonster target)
        {
            // Door/native recovery must yield to the same real stationary contact.
            // Follow/landing hazard checks remain on paths that actually move.
            return IsAssistStationaryCombat(target);
        }

        private void CancelAssistDoor()
        {
            if (_assistDoorWalkStage != 0) ReleaseAssistAttack();
            _assistDoorWalkStage = 0; _assistDoorWalkPoint=null; _assistDoorRouteEpoch=0;
            _assistDoorWalkReason="none";
            _assistDoorPathState = _assistDoorPathReason = "none"; _assistDoorApproachPoint = _assistDoorWaypoint = null;
            _assistDoorBestRemaining = _assistDoorRemaining = _assistDoorDashRemaining = double.NaN;
            _assistDoorDashWaypoint = null;
            if (_assistDoorTarget != null && _assistDoorPulseKey != ActionKey.Unknown
                && _pulse == _assistDoorPulseKey && !_pulseIsMaintenance) CancelTarget();
            if (_assistDoorEliteDetour) _assistDoorReturnPending = true;
            _assistDoorEliteDetour = false;
            _assistDoorTarget = null;
            _assistDoorDashSpent = false;
            _assistDoorStage = 0;
            _assistDoorPulseKey = ActionKey.Unknown;
            _assistDoorClickAuthorized = false;
            _assistDoorStatus = "none";
            _assistDoorCombatPaused = false;
            _assistDoorDashFailures = 0;
        }

        // Forward preference applies to housekeeping; failed routes may recover via side doors.
        private bool IsAssistForwardDoor(IActor door, IMonster combatTarget, bool singleDashOnly = false)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            var point = door.FloorCoordinate;
            double dx = point.X - me.X, dy = point.Y - me.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > (singleDashOnly ? AssistDashTravelLimit - 0.25 : AssistDoorApproachRange)) return false;
            if (distance <= 8.0) return true; // Already beside it; no backtracking trip.
            double hx = _assistTravelHeadingX, hy = _assistTravelHeadingY;
            bool headingKnown = _assistTravelHeadingKnown;
            if (combatTarget != null)
            {
                double tx = combatTarget.FloorCoordinate.X - me.X;
                double ty = combatTarget.FloorCoordinate.Y - me.Y;
                double targetDistance = Math.Sqrt(tx * tx + ty * ty);
                if (targetDistance > 8.0)
                { hx = tx / targetDistance; hy = ty / targetDistance; headingKnown = true; }
            }
            return !headingKnown || (dx * hx + dy * hy) / distance >= 0.25;
        }

        private IPlayerSkill AssistDoorDashSkill()
        {
            foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                if (skill != null && skill.SnoPower != null
                    && skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_DashingStrike.Sno) return skill;
            return null;
        }

        // Optional elite detours reserve two casts plus Spirit headroom. Raiment can
        // refund a charge when Spirit funds the cast; ordinary travel keeps its old rules.
        private bool CanReserveAssistDoorReturn(IPlayerSkill dash)
        {
            if (!AutoDash || !CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)) return false;
            double cost = Math.Max(0, dash.ResourceCost);
            bool raiment = Hud.Game.Me.GetSetItemCount(755275u) >= 4;
            if (raiment) cost = Math.Max(cost, 75.0 * (1.0 - Math.Max(0.0,
                Math.Min(1.0, Hud.Game.Me.Stats.ResourceCostReduction))));
            double spirit = Hud.Game.Me.Stats.ResourceCurPri;
            bool funded = spirit >= cost * 2.0 + (cost > 0 ? 25.0 : 0.0);
            return funded && (dash.Charges >= 2 || (raiment && cost > 0));
        }

        private bool TryReturnAssistDoorElite(int now)
        {
            if (!_assistDoorReturnPending) return false;
            // One return attempt; never idle or loop waiting for a unavailable cast.
            _assistDoorReturnPending = false;
            if (!_assistActive || !AssistRequested() || s7o_GenMonkInput.IsStandstillDown()
                || s7o_InputReleaseArbiter.HasPendingRelease)
            { _assistDoorReturnEliteAcd = 0u; _assistDoorReturnPoint = null; return false; }
            var elite = FindAssistCandidate(_assistDoorReturnEliteAcd);
            var point = _assistDoorReturnPoint;
            _assistDoorReturnEliteAcd = 0u; _assistDoorReturnPoint = null;
            if (!IsAssistPositionElite(elite)) return false;
            RememberAssistHazardReturn(elite); // Normal targeting resumes this elite.
            var dash = AssistDoorDashSkill();
            if (!AutoDash || !CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)
                || point == null || Hud.Game.Me.FloorCoordinate.XYDistanceTo(point) < 3f) return false;
            int x, y;
            if (!IsAssistDashPointSafe(point, elite, out x, out y, false, true)) return false;
            ReleaseAssistAttack(); CancelTarget();
            MaintainDash(dash, now, elite, point, false, true);
            if (_selfDashKey == ActionKey.Unknown) return false;
            _assistDoorStatus = _assistStatus = "door-return-dash";
            return true;
        }

        private bool CompleteAssistDoor(IMonster combatTarget,int now)
        {
            var door=_assistDoorTarget;
            if(door==null)return false;
            bool opened=door.WorldId==Hud.Game.Me.WorldId && (door.IsOperated || door.IsDisabled
                || (IsAssistBreakableDoor(door)&&door.Hitpoints<=0));
            _assistDoorFailures.Remove(door.AcdId);
            CancelAssistDoor(); // Release only the owned door pulse, immediately.
            _assistDoorCompletedAcd=door.AcdId;_assistDoorCompletedGameTick=Hud.Game.CurrentGameTick;
            _assistDoorLastResult=opened?"opened-resume":"unavailable-resume";
            if(opened)
            {
                _assistDoorFailures.Clear(); // A newly open passage may unlock another previously unreachable door.
                // Opening changes geometry even if the old navigation lock was reset.
                // A confirmed opening rearms inferred/stalled routes. Physical
                // failed-landing points keep their independent collision guards.
                foreach (var entry in new Dictionary<uint,AssistBlockedTarget>(_assistBlockedTargets))
                    if (entry.Value.NoRoute || entry.Value.NoProgress || entry.Value.NavigationTracked) _assistBlockedTargets.Remove(entry.Key);
                if(combatTarget!=null)
                {
                    ReleaseAssistAttack();ResetAssistContactRecovery();
                    _assistBlockedTargets.Remove(combatTarget.AcdId);
                    _assistFailedLandings.RemoveAll(f=>f.Target==combatTarget.AcdId);
                    _assistDashRetryNeedsAttack=false;
                }
                MapAssistFeedback("door-opened",combatTarget==null?0u:combatTarget.AcdId,door.FloorCoordinate,true);
                ResetMapAssist();
                _nextDashAimTick=now; // Successful opening is not a failed-Dash retry.
            }
            return TryReturnAssistDoorElite(now); // False continues normal combat this update.
        }

        private bool UpdateAssistDoor(IMonster combatTarget, int now, bool canDashNow = false)
        {
            // Confirmation preempts standstill/input guards: an opened door no longer
            // owns input or a closed-door route, even if combat already resumed nearby.
            if (_assistDoorTarget != null && !IsAssistClosedDoor(_assistDoorTarget))
                return CompleteAssistDoor(combatTarget,now);
            if (!_assistActive || !AssistRequested() || s7o_GenMonkInput.IsStandstillDown()
                || s7o_InputReleaseArbiter.HasPendingRelease)
            { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            if (_selfDashKey != ActionKey.Unknown) return _assistDoorTarget != null;
            if (IsAssistReachableCombat(combatTarget))
            {
                if (_assistDoorTarget != null)
                {
                    if (_assistDoorWalkStage != 0) ReleaseAssistAttack();
                    if (_assistDoorPulseKey != ActionKey.Unknown && _pulse == _assistDoorPulseKey) CancelTarget();
                    _assistDoorPulseKey = ActionKey.Unknown; _assistDoorClickAuthorized = false;
                    _assistDoorStage = 1; _assistDoorCombatPaused = true;
                    _assistDoorStatus = "door-paused-combat";
                }
                return false;
            }
            if (_assistDoorCombatPaused)
            {
                _assistDoorCombatPaused = false;
                _assistDoorProgressGameTick = Hud.Game.CurrentGameTick;
                _assistDoorSinceTick = now;
            }
            // Door presence alone cannot preempt a first bounded shortcut attempt.
            if (_assistDoorTarget == null && combatTarget != null && canDashNow
                && !_assistNavigationPending && !IsAssistTargetTemporarilyBlocked(combatTarget)
                && (!CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, combatTarget)
                    || MapAssistTargetBlocked(combatTarget))) return false;
            // Failed door travel yields to actual reachable damage instead of repeatedly
            // pulling the character back from nearby combat toward an unchanged wall.
            if (_assistDoorTarget!=null && AssistDoorFailureCount(_assistDoorTarget)>0
                && combatTarget!=null && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate,combatTarget)
                && !MapAssistTargetBlocked(combatTarget))
            { CancelAssistDoor();return false; }
            // Accessible elites preempt optional door trips, including trips already underway.
            // A mapped blocking door retains recovery priority when the elite is inaccessible.
            if (IsAssistPositionElite(combatTarget) && !IsAssistRouteSuppressed(combatTarget)
                && !MapAssistTargetBlocked(combatTarget)
                && (_assistMapDoorAcd == 0u
                    || CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, combatTarget)))
            {
                CancelAssistDoor();
                _assistDoorReturnPending = false;
                return false;
            }
            if (TryReturnAssistDoorElite(now)) return true;
            if (_assistDoorEliteDetour && (_assistDoorStage == 4
                || (_assistDoorStage == 5 && (_assistDashMovementFailed
                    || !_assistDashLandingConfirmed
                    || Hud.Game.Me.FloorCoordinate.XYDistanceTo(_assistDoorTarget.FloorCoordinate) > 8f))))
            {
                RecordAssistDoorFailure(_assistDoorTarget); CancelAssistDoor();
                return TryReturnAssistDoorElite(now);
            }
            if (_assistDoorStage == 5)
            {
                // A short confirmed Dash somewhere else is not door-route progress.
                // The next hop requires arrival at this route's requested waypoint.
                bool arrived = _assistDoorDashWaypoint != null && _assistDashLandingConfirmed
                    && !_assistDashMovementFailed && Hud.Game.Me.FloorCoordinate.XYDistanceTo(_assistDoorDashWaypoint)<=2.5f;
                _assistDoorDashSpent = !arrived;
                _assistDoorDashFailures = arrived ? 0 : _assistDoorDashFailures + 1;
                if (arrived) _assistDoorDashWaypoint = null;
                _assistDashContinuationPending = _assistPositionReplanPending = false;
                _assistDoorStage = 1; _assistDoorSinceTick = now;
            }
            if (_assistDoorTarget == null)
            {
                if (_pulse != ActionKey.Unknown || (s7o_GenMonkInput.IsDown(ActionKey.LeftSkill)
                    && !s7o_GenMonkInput.Owns(ActionKey.LeftSkill))) return TryReturnAssistDoorElite(now);
                if (!RefreshAssistPositionActors()) return TryReturnAssistDoorElite(now);
                // Recovery must work even when geometry cannot resolve the door.
                // Native door approach has its own progress watchdog and failure cap.
                bool recoverUnmappedDoor = combatTarget != null
                    && (_assistNavigationPending || IsAssistRouteSuppressed(combatTarget))
                    && (!CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, combatTarget)
                        || MapAssistTargetBlocked(combatTarget));
                // Recover the intervening door from the walking route even when
                // physical failure suppresses all further shortcut attempts.
                if (combatTarget != null && !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, combatTarget)
                    && (_assistDoorRouteTarget != combatTarget.AcdId
                        || Hud.Game.CurrentGameTick < _assistDoorRouteGameTick
                        || Hud.Game.CurrentGameTick - _assistDoorRouteGameTick >= 6))
                {
                    _assistDoorRouteTarget = combatTarget.AcdId;
                    _assistDoorRouteGameTick = Hud.Game.CurrentGameTick;
                    var status = MapAssistStatus();
                    if (status != null)
                    {
                        try
                        {
                            var from = Hud.Game.Me.FloorCoordinate;
                            var to = _assistMapGoal != null && _assistMapGoalAcd == combatTarget.AcdId
                                ? _assistMapGoal : combatTarget.FloorCoordinate;
                            var hop = _assistMapGet("dash", new object[]{status["epoch"], from.X, from.Y, from.Z,
                                to.X, to.Y, to.Z, Math.Max(0f, Math.Min(3f, MapWallClearance))}) as Dictionary<string,object>;
                            if (hop != null && hop.ContainsKey("doorAcd"))
                                _assistMapDoorAcd = Convert.ToUInt32(hop["doorAcd"]);
                            var route = _assistMapGet("path", new object[]{status["epoch"], from.X, from.Y, from.Z,
                                to.X, to.Y, to.Z, 0.6f}) as Dictionary<string,object>;
                            if (route != null && route.ContainsKey("doorAcd") && Convert.ToUInt32(route["doorAcd"]) != 0u)
                                _assistMapDoorAcd = Convert.ToUInt32(route["doorAcd"]);
                            // A failed route may not identify its door. Heading is only
                            // an optional detour preference, never a recovery restriction.
                            recoverUnmappedDoor = recoverUnmappedDoor || (_assistMapDoorAcd == 0u && route != null
                                && route.ContainsKey("state") && (route["state"] as string) == "NoRoute");
                        }
                        catch { _assistMapGet = null; _assistMapPlugin = null; }
                    }
                }
                bool eliteFight = IsAssistPositionElite(combatTarget)
                    && !IsAssistRouteSuppressed(combatTarget) && !MapAssistTargetBlocked(combatTarget);
                bool returnReserved = !eliteFight || (canDashNow
                    && CanReserveAssistDoorReturn(AssistDoorDashSkill()));
                IActor best = null;
                bool bestOpenAhead = false;
                var me = Hud.Game.Me.FloorCoordinate;
                foreach (var door in _assistDoors)
                {
                    bool openAhead = canDashNow && returnReserved && IsAssistForwardDoor(door, combatTarget, eliteFight);
                    // Do not walk away from a reachable elite for optional housekeeping.
                    if (eliteFight && door.AcdId != _assistMapDoorAcd) continue;
                    // Travel may use confirmed successive hops; an elite detour stays within one Dash.
                    // Existing mapped/failed-route recovery remains available without Dash.
                    double doorRange = door.AcdId == _assistMapDoorAcd ? AssistPositionScanRange
                        : openAhead ? (eliteFight ? AssistDashTravelLimit : AssistDoorApproachRange)
                        : (_assistNavigationPending || recoverUnmappedDoor) ? AssistDoorApproachRange : 12f;
                    int failures=AssistDoorFailureCount(door);
                    if (!IsAssistClosedDoor(door) || me.XYDistanceTo(door.FloorCoordinate) > doorRange
                        || failures >= 2 || (failures>0 && combatTarget!=null
                            && CanAttackAssistFrom(me,combatTarget) && !MapAssistTargetBlocked(combatTarget))) continue;
                    // Local interaction is independent of heading and Dash readiness.
                    // Failed travel can also try a nearby visible door using native pathing.
                    bool recoveryDoor = !eliteFight && (me.XYDistanceTo(door.FloorCoordinate) <= 12f
                        || (recoverUnmappedDoor && me.XYDistanceTo(door.FloorCoordinate) <= AssistDoorApproachRange));
                    if (combatTarget != null && !openAhead && !recoveryDoor)
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
                    if (best == null || door.CentralXyDistanceToMe < best.CentralXyDistanceToMe)
                    { best = door; bestOpenAhead = openAhead; }
                }
                if (best == null || (!bestOpenAhead && combatTarget != null && MainGeneratorAnimation()
                    && HasAssistAttackContact(combatTarget))) return TryReturnAssistDoorElite(now);
                ReleaseAssistAttack();
                CancelTarget();
                _assistDoorTarget = best;
                _assistDoorStage = 1;
                _assistDoorSinceTick = now;
                _assistDoorBeforeHp = best.Hitpoints;
                _assistDoorProgressGameTick = Hud.Game.CurrentGameTick;
                _assistDoorNativeOriginX = me.X; _assistDoorNativeOriginY = me.Y; _assistDoorNativeMaxProgress = 0;
                if (eliteFight)
                {
                    _assistDoorEliteDetour = true;
                    _assistDoorReturnEliteAcd = combatTarget.AcdId;
                    var origin = Hud.Game.Me.FloorCoordinate;
                    _assistDoorReturnPoint = Hud.Window.CreateWorldCoordinate(origin.X, origin.Y, origin.Z);
                }
            }
            var target = _assistDoorTarget;
            if (!IsAssistClosedDoor(target)) return CompleteAssistDoor(combatTarget,now);
            // Own the route to this blocker before ordinary combat can plan another
            // shortcut to a target behind it. The endpoint is an interaction region,
            // not an offset chosen by which direction the player happens to stand.
            if (_assistDoorStage != 2 && _assistDoorStage != 3)
            {
                PlanAssistDoorApproach(target);
                if (_assistDoorPathState == "Ready" && _assistDoorWaypoint != null)
                {
                    if (_assistDoorStage == 4) { CancelTarget(); _assistDoorPulseKey=ActionKey.Unknown; }
                    if (_assistDoorWalkStage != 0 && Hud.Game.CurrentGameTick-_assistDoorProgressGameTick>=30)
                    {
                        ReleaseAssistAttack();
                        RecordAssistDoorFailure(target);
                        MapAssistFeedback("door-route-no-progress",target.AcdId,Hud.Game.Me.FloorCoordinate,true);
                        _assistDoorDashSpent=true;_assistDoorDashWaypoint=null;
                        _assistDoorStage=1;_assistDoorSinceTick=now;
                        _assistDoorProgressGameTick=Hud.Game.CurrentGameTick;
                        if (AssistDoorFailureCount(target)>=2)
                        { if(combatTarget!=null)BlockAssistDoorGoal(combatTarget);CancelAssistDoor();return false; }
                        _assistDoorStatus="door-progress-yield";
                        return false;
                    }
                    else
                    {
                        if (TryDashToAssistDoor(target,combatTarget,now)) return true;
                        if (WalkAssistDoorWaypoint(now)) return true;
                        if (_assistDoorWalkStage != 0) ReleaseAssistAttack();
                        _assistDoorStage=1;_assistDoorSinceTick=now;
                        if (_assistDoorWalkReason=="unsafe-waypoint-hover" || _assistDoorWalkReason=="unsafe-held-hover"
                            || _assistDoorWalkReason=="unsafe-waypoint")
                        {
                            RecordAssistDoorFailure(target);
                            if (AssistDoorFailureCount(target)>=2)
                            { if(combatTarget!=null)BlockAssistDoorGoal(combatTarget);CancelAssistDoor();return false; }
                        }
                        _assistDoorStatus="door-waypoint-yield";
                        return false; // Local combat may take over this update; keep the route objective.
                    }
                }
                else if (_assistDoorPathState=="Pending"
                    &&Hud.Game.Me.FloorCoordinate.XYDistanceTo(target.FloorCoordinate)>8f
                    &&ElapsedMs(now,_assistDoorSinceTick)<250) {
                    // The bounded route worker is already running. Do not issue a
                    // backwards native chase to the door while its next hop refreshes.
                    _assistDoorStatus=_assistStatus="door-route-planning";
                    return true;
                }
                else if (_assistDoorPathState == "AtApproach")
                {
                    _assistDoorFailures.Remove(target.AcdId);
                    if (_assistDoorWalkStage != 0) ReleaseAssistAttack();
                    if (_assistDoorStage==4) CancelTarget();
                    _assistDoorPulseKey=ActionKey.Unknown;
                    _assistDoorStage=1;_assistDoorSinceTick=now;
                }
                else if (_assistDoorPathState == "NoRoute" || _assistDoorPathState == "Unavailable"
                    || (_assistDoorPathState == "Pending" && ElapsedMs(now,_assistDoorSinceTick)>=250))
                {
                    RecordAssistDoorFailure(target);
                    if (combatTarget != null && !IsAssistReachableCombat(combatTarget))
                        BlockAssistDoorGoal(combatTarget);
                    CancelAssistDoor();
                    return false;
                }
                else if (_assistDoorWalkStage != 0)
                { ReleaseAssistAttack();_assistDoorStage=1;_assistDoorSinceTick=now; }
            }

            if (_assistDoorEliteDetour && _assistDoorStage == 1
                && Hud.Game.Me.FloorCoordinate.XYDistanceTo(target.FloorCoordinate) > 8f)
            {
                RecordAssistDoorFailure(target); CancelAssistDoor();
                return TryReturnAssistDoorElite(now);
            }
            if (_assistDoorStage == 4)
            {
                _assistDoorStatus = _assistStatus = "door-navigate";
                // Hold one acknowledged native target; do not spam clicks or Dash.
                // The timer measures lack of progress, not a delay before action.
                var me = Hud.Game.Me.FloorCoordinate;
                double outward = me.XYDistanceTo(_assistDoorNativeOriginX,_assistDoorNativeOriginY);
                if (outward >= _assistDoorNativeMaxProgress+1.0)
                {
                    _assistDoorNativeMaxProgress=outward;
                    _assistDoorProgressX=me.X;_assistDoorProgressY=me.Y;
                    _assistDoorSinceTick=now;
                    _pulseReleaseTick=unchecked(now+1000);
                }
                int x, y;
                if (!TryGetAssistDoorAim(target, out x, out y)
                    || (_assistDoorPulseKey == ActionKey.LeftSkill && !IsLeftClickSafe()))
                { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
                double bodyGap = Math.Max(0, me.XYDistanceTo(target.FloorCoordinate)
                    - Math.Max(0, Math.Min(3f, target.RadiusScaled)));
                double interactionReach = IsAssistBreakableDoor(target) ? AssistAttackReachYards : 4f;
                if (bodyGap <= interactionReach)
                {
                    CancelTarget();
                    _assistDoorPulseKey = ActionKey.Unknown;
                    _assistDoorClickAuthorized = false;
                    _assistDoorStage = 1;
                    _assistDoorSinceTick = now;
                    _assistDoorBeforeHp = target.Hitpoints;
                    // Reacquire the exact door for its short attack/open pulse.
                }
                else
                {
                    if (_pulse != _assistDoorPulseKey || ElapsedMs(now, _assistDoorSinceTick) >= 500)
                    { RecordAssistDoorFailure(target); CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
                    return true;
                }
            }
            if (_assistDoorStage == 2)
            {
                _assistDoorStatus = "door-click";
                if (_pulse != ActionKey.Unknown) return true;
                _assistDoorClickAuthorized = false;
                if (IsAssistBreakableDoor(target) && target.Hitpoints < _assistDoorBeforeHp)
                { _assistDoorFailures.Remove(target.AcdId); CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
                if (ElapsedMs(now, _assistDoorSinceTick) < 250)
                {
                    // Keep the acknowledgement watchdog, but give combat this update.
                    // No pulse is held, so there is no reason to idle while data settles.
                    _assistDoorStatus="door-confirming";return false;
                }
                RecordAssistDoorFailure(target);
                CancelAssistDoor();
                return TryReturnAssistDoorElite(now);
            }
            if (ElapsedMs(now, _assistDoorSinceTick) >= 250)
            { RecordAssistDoorFailure(target); CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            int aimX, aimY;
            if (!TryGetAssistDoorAim(target, out aimX, out aimY))
            { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            CursorPoint current;
            if (!GetCursorPos(out current)) { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            if (_assistDoorStage == 3
                && (Math.Abs((long)current.X - _assistDoorAimX) > 12
                    || Math.Abs((long)current.Y - _assistDoorAimY) > 12))
            { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            if (_assistDoorStage == 1 || _assistDoorAimX != aimX || _assistDoorAimY != aimY)
            {
                if (!SetCursorPos(aimX, aimY)) { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
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
            double doorGap = Math.Max(0, Hud.Game.Me.FloorCoordinate.XYDistanceTo(target.FloorCoordinate)
                - Math.Max(0, Math.Min(3f, target.RadiusScaled)));
            bool breakable = IsAssistBreakableDoor(target);
            bool approachDoor = doorGap > (breakable ? AssistAttackReachYards : 4f);
            if (approachDoor && _assistDoorEliteDetour)
            { RecordAssistDoorFailure(target); CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            if (approachDoor && MapAssistTargetDoorBlocked(target))
            { RecordAssistDoorFailure(target); CancelAssistDoor(); return false; }
            if (approachDoor && breakable)
            {
                // Keyboard generators permit native pathing without synthetic LMB.
                var navigation = AssistNavigationGenerator(0);
                if (navigation == null) { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
                _assistDoorPulseKey = navigation.Key;
            }
            else _assistDoorPulseKey = breakable ? _assistMainKey : ActionKey.LeftSkill;
            _assistDoorClickAuthorized = _assistDoorPulseKey == ActionKey.LeftSkill;
            if (!StartPulse(_assistDoorPulseKey, now, approachDoor ? 1000 : 50, false))
            { CancelAssistDoor(); return TryReturnAssistDoorElite(now); }
            _assistDoorStage = approachDoor ? 4 : 2;
            _assistDoorSinceTick = now;
            _assistDoorProgressX = _assistDoorNativeOriginX = Hud.Game.Me.FloorCoordinate.X;
            _assistDoorProgressY = _assistDoorNativeOriginY = Hud.Game.Me.FloorCoordinate.Y;
            _assistDoorNativeMaxProgress = 0;
            _assistDoorStatus = _assistStatus = approachDoor ? "door-navigate" : "door-click";
            return true;
        }

        private bool TryDashToAssistDoor(IActor door, IMonster combatTarget, int now)
        {
            if (_assistDoorPathState != "Ready" || (_assistDoorDashSpent && _assistDoorDashFailures >= 1)) return false;
            var dash = AssistDoorDashSkill();
            if (!AutoDash || !CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            // The connected interaction region establishes the side. Dash may skip
            // static walls on the way there; the door-aware query still vetoes doors.
            var landing = _assistDoorWaypoint;
            var status = MapAssistStatus();
            if (_assistDoorApproachPoint != null && status != null)
            {
                try
                {
                    var goal = _assistDoorApproachPoint;
                    var hop = _assistMapGet("dash", new object[]{status["epoch"], me.X, me.Y, me.Z,
                        goal.X, goal.Y, goal.Z, 0.6f}) as Dictionary<string,object>;
                    var xyz = hop != null && hop.ContainsKey("waypoint") ? hop["waypoint"] as float[] : null;
                    if (hop != null && hop.ContainsKey("state") && (hop["state"] as string) == "Ready"
                        && xyz != null && xyz.Length == 3)
                    {
                        var candidate = Hud.Window.CreateWorldCoordinate(xyz[0],xyz[1],xyz[2]);
                        int cx, cy;
                        if (me.XYDistanceTo(candidate) >= 3 && me.XYDistanceTo(candidate) <= AssistDashTravelLimit - 0.25
                            && candidate.XYDistanceTo(goal) + 2 < me.XYDistanceTo(goal)
                            && IsAssistDashPointSafe(candidate,combatTarget,out cx,out cy,false,true)) landing = candidate;
                    }
                }
                catch { _assistMapGet = null; _assistMapPlugin = null; }
            }
            if (landing == null || (_assistDoorDashSpent && _assistDoorDashWaypoint != null
                && landing.XYDistanceTo(_assistDoorDashWaypoint) <= 2.5f)) return false;
            double travel = me.XYDistanceTo(landing);
            if (travel < 4 || travel > AssistDashTravelLimit - 0.25) return false;
            int x,y;
            if (!IsAssistDashPointSafe(landing,combatTarget,out x,out y,false,true)) return false;
            ReleaseAssistAttack(); CancelTarget();
            _assistDoorDashWaypoint = landing.Offset(0f,0f,0f);
            _assistDoorDashRemaining = _assistDoorRemaining;
            MaintainDash(dash,now,combatTarget,landing,false,true);
            if (_selfDashKey == ActionKey.Unknown) return false;
            _assistDoorDashSpent = true;
            _assistDoorStage = 5;
            _assistDoorStatus = _assistStatus = "door-route-dash";
            return true;
        }

        private void PlanAssistDoorApproach(IActor door)
        {
            var previousWaypoint=_assistDoorWaypoint;
            var previousGoal=_assistDoorApproachPoint;
            _assistDoorApproachPoint = _assistDoorWaypoint = null;
            _assistDoorRemaining = double.NaN;
            var status = MapAssistStatus();
            if (status == null) { _assistDoorPathState = "map-unavailable"; _assistDoorPathReason = "provider-unavailable"; return; }
            try
            {
                var me = Hud.Game.Me.FloorCoordinate;
                long epoch=Convert.ToInt64(status["epoch"]);
                float body = Math.Max(0f,Math.Min(3f,door.RadiusScaled));
                float reach = Math.Min(8f,(IsAssistBreakableDoor(door)?AssistAttackReachYards:4f)+body-0.75f);
                // The two-yard preference is not a veto in narrow door corridors.
                var route = _assistMapGet("approach",new object[]{status["epoch"],me.X,me.Y,me.Z,door.AcdId,reach,0.6f})
                    as Dictionary<string,object>;
                _assistDoorPathState = route != null && route.ContainsKey("state") ? route["state"] as string : "map-unavailable";
                if (route == null) return;
                _assistDoorPathReason = route.ContainsKey("reason") ? Convert.ToString(route["reason"]) : "none";
                if(_assistDoorPathState=="Pending"&&previousWaypoint!=null&&_assistDoorRouteEpoch==epoch
                    &&me.XYDistanceTo(previousWaypoint)>1f) {
                    var check=_assistMapGet("corridor",new object[]{epoch,me.X,me.Y,me.Z,
                        previousWaypoint.X,previousWaypoint.Y,previousWaypoint.Z,0.6f}) as Dictionary<string,object>;
                    if(check!=null&&check.ContainsKey("state")&&(check["state"] as string)=="ClearCandidate") {
                        _assistDoorWaypoint=previousWaypoint;_assistDoorApproachPoint=previousGoal;
                        _assistDoorPathState="Ready";_assistDoorPathReason="validated-refresh-hop";
                        return;
                    }
                }
                _assistDoorRouteEpoch=epoch;
                var goal = route.ContainsKey("approachGoal") ? route["approachGoal"] as float[] : null;
                var waypoint = route.ContainsKey("waypoint") ? route["waypoint"] as float[] : null;
                if (goal != null && goal.Length == 3)
                    _assistDoorApproachPoint = Hud.Window.CreateWorldCoordinate(goal[0],goal[1],goal[2]);
                if (waypoint != null && waypoint.Length == 3)
                    _assistDoorWaypoint = Hud.Window.CreateWorldCoordinate(waypoint[0],waypoint[1],waypoint[2]);
                if (route.ContainsKey("remainingDistance")) _assistDoorRemaining = Convert.ToDouble(route["remainingDistance"]);
                if (!double.IsNaN(_assistDoorRemaining)
                    && (double.IsNaN(_assistDoorBestRemaining) || _assistDoorRemaining <= _assistDoorBestRemaining-1.5))
                {
                    _assistDoorBestRemaining = _assistDoorRemaining;
                    _assistDoorProgressGameTick = Hud.Game.CurrentGameTick;
                    // A failed hop stays suppressed until walking makes six yards of
                    // verified route progress. Side-to-side jitter cannot rearm it.
                    if (_assistDoorDashWaypoint != null && !double.IsNaN(_assistDoorDashRemaining)
                        && _assistDoorRemaining <= _assistDoorDashRemaining-6.0)
                        _assistDoorDashWaypoint = null;
                    if (_assistDoorDashWaypoint == null) { _assistDoorDashSpent = false; _assistDoorDashFailures = 0; }
                }
            }
            catch { _assistDoorPathState = "map-unavailable"; _assistDoorPathReason = "query-failed"; }
        }

        private bool WalkAssistDoorWaypoint(int now)
        {
            bool walking=WalkAssistGroundWaypoint(_assistDoorWaypoint,now,"door-route-walk",
                ref _assistDoorWalkStage,ref _assistDoorWalkAimTick,ref _assistDoorWalkPoint,out _assistDoorWalkReason);
            if(walking) {_assistDoorStage=_assistDoorWalkStage==1?6:7;_assistDoorStatus=_assistStatus;}
            return walking;
        }

        // Shared ground-input mechanism; callers keep separate route/door state.
        // Never adopt physical LMB, click an actor/UI, or hold owned standstill while walking.
        private bool WalkAssistGroundWaypoint(IWorldCoordinate point,int now,string label,
            ref int stage,ref int aimTick,ref IWorldCoordinate heldPoint,out string reason,bool combatRoute=false)
        {
            reason="none";
            if (point == null || s7o_InputReleaseArbiter.HasPendingRelease
                || (s7o_GenMonkInput.IsDown(ActionKey.LeftSkill)&&!s7o_GenMonkInput.Owns(ActionKey.LeftSkill)))
            { reason="input-unavailable";return false; }
            var screen = point.ToScreenCoordinate(true,true);
            if (screen == null || !AssistPositionFinite(screen.X) || !AssistPositionFinite(screen.Y)
                || !IsLeftClickPointSafe(screen.X,screen.Y) || !IsAssistPositionHazardFree(point))
            { reason="unsafe-waypoint";return false; }
            int x=(int)Math.Round(screen.X)+Hud.Window.Offset.X,y=(int)Math.Round(screen.Y)+Hud.Window.Offset.Y;
            ReleaseAssistScopedStandstill();
            if (s7o_GenMonkInput.IsStandstillDown())return false;
            CursorPoint cursor;
            if (!GetCursorPos(out cursor))return false;
            bool changed=heldPoint==null||heldPoint.XYDistanceTo(point)>1.25f
                ||Math.Abs(heldPoint.Z-point.Z)>1f;
            if (stage==0 || changed) {
                ReleaseAssistAttack();CancelTarget();
                if (!SetCursorPos(x,y))return false;
                heldPoint=point.Offset(0f,0f,0f);
                _assistAimX=x;_assistAimY=y;_assistCursorOwned=true;
                stage=1;aimTick=Hud.Game.CurrentGameTick;
                _assistAimGameTick=aimTick;_assistAimPending=false;
                reason="new-ground-aim";
            }
            else if(Math.Abs((long)cursor.X-x)>2||Math.Abs((long)cursor.Y-y)>2) {
                // Camera projection changed; the ground destination did not. Track
                // its screen position without repeatedly releasing the owned hold.
                if(!SetCursorPos(x,y))return false;
                _assistAimX=x;_assistAimY=y;_assistCursorOwned=true;
                reason="track-ground-projection";
            }
            if (stage==1) {
                _assistStatus=label+"-aim";
                if(Hud.Game.CurrentGameTick==aimTick)return true;
                // Fresh acknowledgement still gates DOWN. An eligible enemy is useful
                // contact, not an obstruction to movement. Unsafe actors yield at once.
                if(!IsAssistRouteHoverSafe(point,combatRoute) || !IsLeftClickSafe()) {
                    reason="unsafe-waypoint-hover";return false;
                }
                if(!s7o_GenMonkInput.Down(Owner,ActionKey.LeftSkill))return false;
                _assistAttackKey=ActionKey.LeftSkill;_assistAttackStartedTick=now;
                stage=2;reason="ground-held";
            }
            if(!IsAssistRouteHoverSafe(point,combatRoute)||!s7o_GenMonkInput.Owns(ActionKey.LeftSkill)||!IsLeftClickSafe())
            { reason="unsafe-held-hover";return false; }
            _assistStatus=label;
            return true;
        }

        private bool IsAssistRouteHoverSafe(IWorldCoordinate point,bool combatRoute)
        {
            if(Hud.Game.SelectedActor==null)return true;
            var monster=Hud.Game.SelectedActor as IMonster;
            if(!combatRoute || !IsAssistEligible(monster) || !monster.IsSelected
                || monster.FloorCoordinate.XYDistanceTo(point)>Math.Max(6f,monster.RadiusScaled+2f)
                || !IsAssistNativeFollowHazardFree(monster))return false;
            var status=MapAssistStatus();if(status==null)return false;
            try {
                var me=Hud.Game.Me.FloorCoordinate;var to=monster.FloorCoordinate;
                var ray=_assistMapGet("corridor",new object[]{status["epoch"],me.X,me.Y,me.Z,
                    to.X,to.Y,to.Z,0.35f}) as Dictionary<string,object>;
                return ray!=null&&ray.ContainsKey("state")&&(ray["state"] as string)=="ClearCandidate";
            }catch{return false;}
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
                && AssistBodyGap(point, target) <= (IsExtendedCirclePoint(point,target)
                    ? Math.Min(AssistCircleTrialReachYards, MeleeAssistRange + 4.0) : Math.Max(1.0, Math.Min(AssistAttackReachYards, MeleeAssistRange)));
        }

        private bool HasAssistAttackContact(IMonster target)
        {
            return target != null && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking
                && AttackedIdentityMatches(target.AcdId, target.AnnId)
                && !MapAssistTargetBlocked(target);
        }

        private bool IsAssistCircleWithinReach(IWorldCoordinate point, IMonster target)
        {
            if ((!CanAttackAssistFrom(point, target)) || (MapAssistAttackBlocked(point, target))) return false;
            string reason;
            return AssistPositionCirclePriority(point, target, out reason) > 0;
        }

        private bool CanAttackFromCurrentDamageCircle(IMonster target)
        {
            if (!IsAssistEligible(target) || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                || MapAssistAttackBlocked(Hud.Game.Me.FloorCoordinate, target)
                || !RefreshAssistPositionActors()) return false;
            // Evaluate where the hero actually stands, never an imagined circle-edge landing.
            string reason;
            return AssistPositionCirclePriority(Hud.Game.Me.FloorCoordinate, target, out reason) == 2;
        }

        private void ConsiderAssistCirclePoint(AssistPositionCircle circle, IWorldCoordinate point, IMonster target,
            ref IWorldCoordinate best, ref int bestPriority, ref double bestScore, ref string reason)
        {
            if(point.XYDistanceTo(Hud.Game.Me.FloorCoordinate)<1.5f
                || IsAssistFailedCircleContact(circle.Actor.AcdId,target))return;

            
            if ((point.XYDistanceTo(circle.Actor.FloorCoordinate) > AssistCircleLandingRadius + 0.01f)
                || (point.XYDistanceTo(target.FloorCoordinate) < target.RadiusScaled + 1.0f)
                || !IsAssistCircleWithinReach(point, target)) return;
            if (AssistCircleLosesCoverage(point,target,target)) return;
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
            if (!IsAssistEligible(target)
                || !IsAssistUsefulCircleTarget(target) || !RefreshAssistPositionActors()) return false;
            string currentReason;
            var current = Hud.Game.Me.FloorCoordinate;
            int currentPriority = IsAssistPositionHazardFree(current)
                ? AssistPositionCirclePriority(current, target, out currentReason) : 0;
            if ((currentPriority >= 2)) return false;
            double bestScore = double.MinValue;
            int bestPriority = -1;
            foreach (var circle in _assistPositionCircles)
            {
                if ((circle.Actor == null || circle.Actor.IsDisabled || IsAssistCircleBlocked(circle)
                    || (circle.Kind == 2 && !IsAssistPositionElite(target)))) continue;
            
                var center = circle.Actor.FloorCoordinate;
                if (AssistBodyGap(center, target) > AssistCircleTargetGapYards) continue;
                double dx = target.FloorCoordinate.X - center.X, dy = target.FloorCoordinate.Y - center.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double scale = distance > AssistCircleLandingRadius ? AssistCircleLandingRadius / distance : 1.0;
                var point = center.Offset((float)(dx * scale), (float)(dy * scale), 0f);
                // Prefer the target-facing inner edge. Only probe alternatives if that
                // point is blocked or lies inside an Electrified/body exclusion.
                IWorldCoordinate before = best;
                ConsiderAssistCirclePoint(circle, point, target, ref best, ref bestPriority, ref bestScore, ref reason);
                // Score alternate edges too, preserving circle priority.
                for (int i = 0; i < AssistEdgeX.Length; i++)
                    ConsiderAssistCirclePoint(circle, center.Offset(AssistEdgeX[i] * AssistCircleLandingRadius,
                        AssistEdgeY[i] * AssistCircleLandingRadius, 0f), target,
                        ref best, ref bestPriority, ref bestScore, ref reason);
                // A circle next to an elite can have every outer-edge landing inside
                // its body or blocked by terrain. Search its reachable interior too.
                if (best == null)
                {
                    for (int ring = 0; ring < 2; ring++)
                    {
                        float radius = ring == 0 ? 6.5f : 3.5f;
                        for (int i = 0; i < AssistEdgeX.Length; i++)
                            ConsiderAssistCirclePoint(circle, center.Offset(AssistEdgeX[i] * radius,
                                AssistEdgeY[i] * radius, 0f), target,
                                ref best, ref bestPriority, ref bestScore, ref reason);
                        if (best != null) break;
                    }
                }
            }
            
            if (best!=null && AssistNearbyPackWeight(target)>=3)
            {
                IWorldCoordinate alternative; string alternativeReason;
                if (TryPickAssistDashPoint(target,false,out alternative,out alternativeReason,true)
                    && CanAttackAssistFrom(alternative,target) && !MapAssistAttackBlocked(alternative,target)
                    && AssistCoverageOutweighs(alternative,target,best,target)) return false;
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
                && IsAssistEligible(target)
                && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                && !MapAssistTargetBlocked(target);
        }

        private void RefreshAssistEliteProbe(IMonster target)
        {
            if (target.AcdId != _assistProbeTargetAcd)
            {
                _assistProbeTargetAcd = target.AcdId; _assistProbePhase = 0;
                _assistProbeGameTick = Hud.Game.CurrentGameTick;
                _assistProbeLocked = _assistProbeExhausted = false;
                _assistRetargetTriedAcd = 0u;
                _assistRetargetRecoverySpent = false;
                _assistRetargetOrigin = _assistRetargetTargetPoint = null;
                _assistRetargetAttempts = 0; _assistRetargetNextGameTick = int.MinValue;
            }
            if ((!IsAssistPositionElite(target) && !_assistNavigationPending && !_pylonNeedsMonsterHover)
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
                || _pulse != ActionKey.Unknown || _targetKey != ActionKey.Unknown
                || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate,target)
                || MapAssistAttackBlocked(Hud.Game.Me.FloorCoordinate,target)) return false;
            if (AttackedIdentityMatches(target.AcdId, target.AnnId))
            { _assistRetargetAttempts = 0; return false; }
            if (_assistRetargetTriedAcd != target.AcdId)
            { _assistRetargetAttempts = 0; _assistRetargetNextGameTick = int.MinValue; }
            var me = Hud.Game.Me.FloorCoordinate;
            // Changed geometry rearms acquisition; a timer alone never rearms retries.
            if (_assistRetargetOrigin != null && (me.XYDistanceTo(_assistRetargetOrigin) >= 3
                || target.FloorCoordinate.XYDistanceTo(_assistRetargetTargetPoint) >= 3))
            { _assistRetargetAttempts = 0; _assistRetargetRecoverySpent = false; _assistRetargetOrigin = null; }
            if (_assistRetargetAttempts >= 3)
            {
                if (!_assistRetargetRecoverySpent && CanAttackAssistFrom(me,target) && !MapAssistAttackBlocked(me,target))
                {
                    _assistRetargetRecoverySpent = true;
                    _assistProbePhase = 0; _assistProbeLocked = _assistProbeExhausted = false;
                    ReleaseAssistAttack(true); // Only our key; perform one fresh primary acquisition.
                    return true;
                }
                return false;
            }
            if (!_assistProbeLocked || Hud.Game.CurrentGameTick == _assistRetargetNextGameTick
                || Hud.Game.CurrentGameTick == _assistRetargetAimGameTick
                || (_assistRetargetAttempts>0 && _assistWotHfResumePending
                    && ElapsedMs(now,_assistAttackStartedTick)<150)) return false;
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
            // Keep the intended identity through the pulse and primary resume.
            _assistRetargetAcd = target.AcdId; _assistRetargetAnn = target.AnnId;
            _assistRetargetOrigin = Hud.Game.Me.FloorCoordinate.Offset(0,0,0);
            _assistRetargetTargetPoint = target.FloorCoordinate.Offset(0,0,0);
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
            if (!sameWorld) _panicMustAttack = false;
            if (_panicMustAttack && _assistWotHfLastGameTick > _panicAttackAfterGameTick
                && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking) _panicMustAttack = false;
            bool lowHealthLoss = _assistActive && AssistRequested() && health > 0 && health <= 0.40
                && (healthLoss || !IsAssistLowHealthFieldPointFree(position)) && !_panicMustAttack;
            if (!AssistShieldPylonActive() && !_assistDamageEscapePending && _selfDashKey == ActionKey.Unknown
                && !_panicMustAttack && (arcaneLoss || lowHealthLoss))
            {
                _assistDamageEscapePending = true;
                _assistDamageEscapeOriginX = position.X; _assistDamageEscapeOriginY = position.Y;
                _assistDamageEscapeTrigger = lowHealthLoss ? "low-health-rapid-loss"
                    : healthLoss ? "health-loss-near-core" : "shield-loss-near-core";
                _panicMustAttack = true; _panicAttackAfterGameTick = tick;
                // All damage-triggered exits require a fresh WotHF attack before
                // another. Explicit core/explosion deadlines remain independent.
                _assistDamageWindowTick = tick; _assistDamageWindowHealth = health;
            }
            ObserveAssistMoltenLoss(position,healthLoss,shieldLoss);
            _assistDamageEscapeWorld = world; _assistDamageEscapeGameTick = tick;
            _assistPreviousHealth = health; _assistPreviousShield = shield;
            if (AssistShieldPylonActive())
            { _assistDamageEscapePending = false; _panicMustAttack = false; _assistDamageEscapeTrigger = "none"; }
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

        // Imminent explosions and unsafe Arcane/Orbiter cores authorize walking. This owns
        // ground LMB, never a user's held button; safe contact resumes in the same tick.
        private bool IsAssistExplosionWalkRouteFree(IWorldCoordinate point)
        {
            if(!IsAssistMoltenWalkFree(point)) {
                
                return false;
            }
            var status=MapAssistStatus();
            if(status==null)return true; // Preserve optional-map native movement behavior.
            try {
                var me=Hud.Game.Me.FloorCoordinate;
                var ray=_assistMapGet("corridor",new object[]{status["epoch"],me.X,me.Y,me.Z,
                    point.X,point.Y,point.Z,0.35f}) as Dictionary<string,object>;
                bool clear=ray!=null && ray.ContainsKey("state") && (ray["state"] as string)=="ClearCandidate";
                if(!clear) {
                    
                }
                return clear;
            } catch {
                
                return false;
            }
        }

        private void ResetAssistEscapeWalkFailures()
        {
            _assistEscapeWalkFailures.Clear(); _assistEscapeWalkOrigin = null;
            _assistEscapeWalkProgressPoint = null; _assistEscapeWalkRetryPending = false;
        }

        private bool IsAssistEscapeWalkPointAllowed(IWorldCoordinate point)
        {
            if(point==null || _assistEscapeWalkFailures.Count>=16) {
                
                return false;
            }
            foreach(var failed in _assistEscapeWalkFailures)
                if(point.XYDistanceTo(failed)<2.0) {
                    
                    return false;
                }
            return IsAssistExplosionWalkRouteFree(point);
        }

        private bool RejectAssistEscapeWalk(int now)
        {
            if (_assistExplosionRunPoint != null && _assistEscapeWalkFailures.Count < 16)
                _assistEscapeWalkFailures.Add(_assistExplosionRunPoint);
            _assistEscapeWalkRetryTick = now; _assistEscapeWalkRetryPending = true;
            ReleaseAssistAttack();
            _assistStatus = "escape-walk-yield";
            return false; // Existing combat selection gets this update and the retry interval.
        }

        private bool TryPickAssistWalkCombatPoint(IMonster target, out IWorldCoordinate point,
            float minimumTravel = 0f)
        {
            point = null; int bestTier = -1; double bestDistance = double.MaxValue;
            foreach (var candidate in _assistCandidates)
            {
                if (!IsAssistEligible(candidate)) continue;
                int tier = AssistTargetTier(candidate);
                if (tier < 0 || tier < bestTier) continue;
                IWorldCoordinate contact; string reason;
                if (!TryPickAssistDashPoint(candidate,true,out contact,out reason)
                    || !IsAssistEscapeWalkPointAllowed(contact)
                    || !CanAttackAssistFrom(contact,candidate)) continue;
                double distance = Hud.Game.Me.FloorCoordinate.XYDistanceTo(contact);
                if (distance < minimumTravel || (tier == bestTier && distance >= bestDistance)) continue;
                point = contact; bestTier = tier; bestDistance = distance;
            }
            return point != null;
        }

        private bool TryRunAssistExplosionExit(int now)
        {
            if (_selfDashKey != ActionKey.Unknown || s7o_InputReleaseArbiter.HasPendingRelease
                || (s7o_GenMonkInput.IsDown(ActionKey.LeftSkill)
                    && !s7o_GenMonkInput.Owns(ActionKey.LeftSkill))) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            if (_assistEscapeWalkOrigin != null && _assistEscapeWalkWorld != Hud.Game.Me.WorldId
                && _assistExplosionRunStage != 0) ReleaseAssistAttack();
            if (_assistEscapeWalkOrigin == null || _assistEscapeWalkWorld != Hud.Game.Me.WorldId
                || me.XYDistanceTo(_assistEscapeWalkOrigin) >= 3.0)
            {
                ResetAssistEscapeWalkFailures();
                _assistEscapeWalkOrigin = me.Offset(0f,0f,0f);
                _assistEscapeWalkWorld = Hud.Game.Me.WorldId;
            }
            if (_assistExplosionRunStage == 0 && _assistEscapeWalkRetryPending
                && unchecked(now - _assistEscapeWalkRetryTick) < 180) return false;
            var target = FindAssistCandidate(_assistTargetAcd);
            IWorldCoordinate point = _assistExplosionRunPoint;
            if (_assistExplosionRunStage == 0)
            {
                bool pureExit;
                bool found = (_assistArcaneCoreUnsafe || _assistOrbiterCoreUnsafe || LowHealthFieldEscapeDue() || AssistPanicWalkDue())
                    && TryPickAssistReactiveEscapePoint(target,out point,out _assistEscapeWalkReason,out pureExit,true);
                if (!found && !TryPickAssistMoltenEscapePoint(target,out point,out _assistEscapeWalkReason,out pureExit,true))
                    return false;
            }
            int x,y;
            if (point == null || !IsAssistEscapeWalkPointAllowed(point)
                || !IsAssistDashPointSafe(point,target,out x,out y,true))
                return _assistExplosionRunStage != 0 ? RejectAssistEscapeWalk(now) : false;
            if (_assistExplosionRunStage == 0)
            {
                CancelAssistDoor(); ReleaseAssistAttack(); CancelTarget();
                _assistExplosionRunPoint = point;
                _assistEscapeWalkStartedTick = _assistEscapeWalkProgressTick = now;
                _assistEscapeWalkProgressPoint = me.Offset(0f,0f,0f);
                if (IsAssistPositionElite(target)) RememberAssistHazardReturn(target);
            }
            else
            {
                if (_assistEscapeWalkProgressPoint == null || me.XYDistanceTo(_assistEscapeWalkProgressPoint) >= 1.0)
                { _assistEscapeWalkProgressPoint = me.Offset(0f,0f,0f); _assistEscapeWalkProgressTick = now; }
                // Re-aiming must not reset either budget while the player is stationary.
                if ((_assistExplosionRunStage == 1 && unchecked(now - _assistEscapeWalkStartedTick) >= 180)
                    || unchecked(now - _assistEscapeWalkProgressTick) >= 500)
                    return RejectAssistEscapeWalk(now);
            }
            ReleaseAssistScopedStandstill();
            if (s7o_GenMonkInput.IsStandstillDown()) return RejectAssistEscapeWalk(now);
            CursorPoint cursor;
            if (!GetCursorPos(out cursor)) return RejectAssistEscapeWalk(now);
            if (_assistExplosionRunStage == 0 || Math.Abs((long)cursor.X-x)>2 || Math.Abs((long)cursor.Y-y)>2)
            {
                if (s7o_GenMonkInput.Owns(ActionKey.LeftSkill))
                {
                    s7o_GenMonkInput.Up(Owner,ActionKey.LeftSkill);
                    _assistAttackKey = ActionKey.Unknown;
                }
                if (!SetCursorPos(x,y)) return RejectAssistEscapeWalk(now);
                if (_assistExplosionRunStage == 2) _assistEscapeWalkStartedTick = now;
                _assistAimX = x; _assistAimY = y; _assistCursorOwned = true;
                _assistExplosionRunStage = 1; _assistExplosionWalkCommitted = true;
                _assistExplosionRunAimTick = Hud.Game.CurrentGameTick;
                _assistStatus = "explosion-run-aim";
                _assistPositionReason = _assistEscapeWalkReason + ":walk-exit";
                return true;
            }
            if (_assistExplosionRunStage == 1)
            {
                if (Hud.Game.CurrentGameTick == _assistExplosionRunAimTick) return true;
                // A ground move must not become an attack, pylon click or UI interaction.
                if (Hud.Game.SelectedActor != null || !IsLeftClickSafe()) return RejectAssistEscapeWalk(now);
                if (!s7o_GenMonkInput.Down(Owner,ActionKey.LeftSkill)) return RejectAssistEscapeWalk(now);
                _assistAttackKey = ActionKey.LeftSkill; _assistAttackStartedTick = now;
                _assistExplosionRunStage = 2;
                _assistSafetyCombatPending = true;
            }
            if (Hud.Game.SelectedActor != null || !s7o_GenMonkInput.Owns(ActionKey.LeftSkill)
                || !IsLeftClickSafe()) return RejectAssistEscapeWalk(now);
            _assistStatus = "explosion-run";
            return true;
        }

        private bool AssistPanicWalkDue()
        {
            return _assistDamageEscapePending && _assistHealthFraction <= 0.40
                && _assistDamageEscapeTrigger == "low-health-rapid-loss"
                && !AssistShieldPylonActive();
        }

        private bool TryStartAssistHazardEscape(IPlayerSkill dash, int now)
        {
            if (!_assistActive || !AssistRequested()) return false;
            bool arcane = AssistReactiveEscapeDue(), molten = AssistMoltenEscapeDue();
            bool walkingHazard = molten || (arcane && (_assistArcaneCoreUnsafe || _assistOrbiterCoreUnsafe
                || LowHealthFieldEscapeDue() || AssistPanicWalkDue()));
            if (!walkingHazard)
            {
                _assistExplosionWalkCommitted = false; ResetAssistEscapeWalkFailures();
                if (_assistExplosionRunStage != 0) ReleaseAssistAttack();
            }
            if (!arcane && !molten) return false;
            if (!AutoDash || !CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)
                || s7o_InputReleaseArbiter.HasPendingRelease)
            {
                // Fatal explosions and Arcane/Orbiter cores share the guarded ground exit.
                // A missing Dash charge must not suppress a known core escape.
                if (walkingHazard && TryRunAssistExplosionExit(now)) return true;
                if (_assistExplosionRunStage != 0) ReleaseAssistAttack();
                if (_assistDamageEscapeTrigger == "low-health-rapid-loss")
                { _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none"; }
                _assistPositionReason = "escape-unavailable";
                MapAssistFeedback("escape-unavailable",_assistTargetAcd,Hud.Game.Me.FloorCoordinate);
                return false;
            }
            // Preserve an in-progress safe ground exit until a better Dash is actually
            // admitted. Releasing here reset its stage on every game update.
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
                if (walkingHazard && TryRunAssistExplosionExit(now)) return true;
                if (_assistDamageEscapeTrigger == "low-health-rapid-loss")
                { _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none"; }
                _assistPositionReason = "escape-no-safe-landing";
                MapAssistFeedback("escape-no-safe-landing",_assistTargetAcd,Hud.Game.Me.FloorCoordinate);
                return false;
            }
            if (!AllowAssistMovement(target, landing, now, true, false))
            {
                if ((target == null || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate,target))
                    && walkingHazard && TryRunAssistExplosionExit(now)) return true;
                return false;
            }
            // The replacement Dash was validated. End the walking transaction once.
            CancelAssistDoor();
            ReleaseAssistAttack();
            CancelTarget();
            if (IsAssistPositionElite(target)) RememberAssistHazardReturn(target);
            _mainKey = _assistMainKey; _combinationDisplayKey = _mainKey;
            _assistPositionReason = reason;
            _assistPositionWorldX = landing.X; _assistPositionWorldY = landing.Y;
            MapAssistFeedback(arcane?"reactive-escape-request":"explosion-escape-request",
                target==null?0u:target.AcdId,landing);
            MaintainDash(dash, now, target, landing, true);
                    if (_selfDashKey != ActionKey.Unknown && !_rangedCleanup && target != null
                        && _assistDamageEscapeTrigger=="low-health-rapid-loss"
                        && reason.IndexOf(":reactive-contact",StringComparison.Ordinal)>=0)
                    { _lastPanicContactTick=now; _lastPanicContactTarget=target.AcdId; }
            _assistStatus = _selfDashKey != ActionKey.Unknown ? (arcane ? "reactive-dash" : "molten-dash")
                : "escape-input-busy";
            if (_selfDashKey == ActionKey.Unknown && _assistDamageEscapeTrigger == "low-health-rapid-loss")
            { _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none"; }
            return _selfDashKey != ActionKey.Unknown;
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
            target = PreferCurrentDamageCircleContact(target, canDashNow, now);
            var retarget = FindAssistCandidate(_assistRetargetAcd);
            if (!_pulseIsRetarget && !_assistWotHfResumePending && HasAssistAttackContact(retarget))
                _assistRetargetAcd = _assistRetargetAnn = 0u;
            bool retargetActive = _pulseIsRetarget || (_assistRetargetAcd != 0u && !Due(now,_assistRetargetResumeUntil));
            if (retargetActive && IsAssistEligible(retarget) && !_assistSafetyCombatOnly
                && !AssistReactiveEscapeDue() && !AssistMoltenEscapeDue()
                && (target == null || AssistTargetTier(retarget) >= AssistTargetTier(target))
                && CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate,retarget)
                && !MapAssistAttackBlocked(Hud.Game.Me.FloorCoordinate,retarget)
                && IsAssistPositionHazardFree(Hud.Game.Me.FloorCoordinate))
                target = retarget;
            else if (!_pulseIsRetarget) _assistRetargetAcd = _assistRetargetAnn = 0u;
            if (!retargetActive)
            {
                target = PreferAssistProductiveContact(target,now);
                target = PreferAssistAttackLane(target,now);
            }
            ObserveAssistBoundaryContact(target,now);
            ObserveExtendedCircleContact(target,now);
            // Selection owns priority. Dash availability changes travel, not the target tier.
            // Navigation handles failed approaches; a blocked straight ray is not a failed route.
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
            IWorldCoordinate circleLanding = null;
            string circleReason = "none";
            _assistCircleCue = "none"; _assistCircleOpportunityAcd = 0u;
            // A newly usable circle has its own validated landing/failed-circle guard.
            // Travel retry timers must not hide this independent damage opportunity.
            bool circleDash = !retargetActive && !hazardUrgent && canDashNow
                && TryPickAssistCircleUpgrade(target, out circleLanding, out circleReason);
            
            if (circleDash) { _assistCircleCue = circleReason; CancelAssistDoor(); }
            if (!hazardUrgent && !circleDash) {
                if(target==null||target.AcdId!=_assistMapGoalAcd)_assistMapDoorAcd=0;
                // A yielded goal supplies door context only when no combat target
                // is reachable; it cannot reclaim combat selection itself.
                var doorGoal = target ?? (_assistNavigationReacquired
                    ? FindAssistCandidate(_assistNavigationTargetAcd) : null);
                if(UpdateAssistDoor(doorGoal,now,canDashNow))return true;
            }

            IWorldCoordinate spacingLanding = null;
            string spacingReason = "none";
            bool spacingNeeded = target != null && (AssistElectrifiedSpacingDue()
                || AssistChainDistance(Hud.Game.Me.FloorCoordinate, target) < 3.0);
            bool spacingDash = !retargetActive && !hazardUrgent && !circleDash && spacingNeeded && CanAttemptAssistSpacing(target) && canDashNow
                && !_assistNavigationPending && !_assistDashRetryNeedsAttack && Due(now, _nextDashAimTick)
                && TryPickAssistDashPoint(target, false, out spacingLanding, out spacingReason)
                && spacingLanding.XYDistanceTo(Hud.Game.Me.FloorCoordinate) > 1f;
            bool contactCorrection = target != null && canDashNow && AssistContactCorrectionDue(target, now);
            // A maintenance pulse no longer has work to verify after its combat target dies
            // or is replaced. Release our own pulse and acquire the next target this update.
            if (_pulse != ActionKey.Unknown)
            {
                if (target == null || target.AcdId != _assistTargetAcd || (_pulseIsRetarget && hazardUrgent)) CancelTarget();
                // An owned secondary attack can also follow at range. Yield it to a safe
                // Dash approach instead of waiting out its pulse on a hazardous segment.
                else if (_pulseIsMaintenance && s7o_GenMonkInput.Owns(_pulse)
                    && (moltenEscape || circleDash || spacingDash || contactCorrection || (!CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target) && !IsAssistNativeFollowHazardFree(target)))) CancelTarget();
                else { _assistStatus = "secondary"; return true; }
            }
            if (_assistAttackKey != ActionKey.Unknown && ElapsedMs(now, _assistAttackStartedTick) >= 35
                && HasAssistAttackContact(target))
            {
                _assistHitVerified = true;
                if (_assistAttackKey == _assistMainKey) _assistSafetyCombatPending = false;
                // Contact can arrive through the held navigation generator. Hand its
                // owned key back to WotHF immediately instead of keeping it held forever.
                if (_assistNavigationPending || _assistAttackKey != _assistMainKey) ReleaseAssistAttack();
                ResetAssistContactRecovery();
                _assistBlockedTargets.Remove(target.AcdId);
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
            bool approaching = !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                || (!HasAssistAttackContact(target) && MapAssistTargetBlocked(target));
            // Recovery may hold an alternate generator only while a path is needed.
            // In-range contact must return to WotHF, never sit on a navigation skill.
            if (!approaching && _assistNavigationPending)
            { ReleaseAssistAttack(); ResetAssistContactRecovery(); }
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
            IWorldCoordinate shortcutLanding = null;
            string shortcutReason = "none";
            bool shortcutTransit = false;
            // Preserve an active native route until a replacement Dash has passed
            // point/endpoint checks and movement admission. A path request alone
            // must not release the current movement owner.
            if (!moltenEscape && approaching && canDashNow && _assistNavigationPending
                && !_assistNavigationTravelBlocked && MapAssistStatus() != null)
            {
                IWorldCoordinate mapped; string mappedReason; bool transit;
                int sx, sy;
                if (TryPlanAssistApproach(target, out mapped, out mappedReason, out transit)
                    && _assistMapRouteReady && IsAssistDashPointSafe(mapped, target, out sx, out sy,
                        mapTransit: transit))
                {
                    shortcutLanding = mapped;
                    shortcutReason = mappedReason;
                    shortcutTransit = transit;
                    navigationRetry = true;
                }
            }
            if (!moltenEscape && !navigationRetry && approaching && canDashNow
                && _assistNavigationPending && !_assistNavigationTravelBlocked
                && !IsAssistTargetTemporarilyBlocked(target) && Due(now, _nextDashAimTick))
            {
                IWorldCoordinate direct; string directReason;
                if (TryPickAssistDashPoint(target, false, out direct, out directReason)
                    && direct != null && direct.XYDistanceTo(Hud.Game.Me.FloorCoordinate) > 2f
                    && CanAttackAssistFrom(direct, target)
                    && direct.XYDistanceTo(target.FloorCoordinate) + 1f
                        < Hud.Game.Me.FloorCoordinate.XYDistanceTo(target.FloorCoordinate))
                {
                    shortcutLanding = direct;
                    shortcutReason = directReason;
                    navigationRetry = true;
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
            IWorldCoordinate densityLanding = null;
            bool densityDash = !retargetActive && !hazardUrgent && !circleDash && !spacingDash && !approaching
                && !_assistNavigationPending && !_assistSafetyCombatOnly && canDashNow
                && TryPickAssistDensityUpgrade(target, now, out densityLanding);
            bool landingReplan = _assistPositionReplanPending && !_assistNavigationPending;
            if ((approachDash || continuationDash || refreshDash || circleDash || densityDash || spacingDash || moltenEscape || landingReplan || hazardApproach
                    || contactCorrection || navigationRetry)
                && AutoDash && CanApproachDash(dash)
                && (!_assistSafetyCombatOnly || moltenEscape || circleDash || contactCorrection)
                // No fixed delay after a completed Dash or target death. Successful
                // progress can continue immediately; a failed cast enters bounded recovery.
                && (moltenEscape || circleDash || densityDash || landingReplan || navigationRetry || continuationDash
                    || (Due(now, _nextDashAimTick) && (contactCorrection
                        || ((!_assistDashRetryNeedsAttack || approaching) && !_assistNavigationPending)))))
            {
                IWorldCoordinate landing = moltenEscape ? moltenEscapePoint : circleDash ? circleLanding : densityDash ? densityLanding : spacingDash ? spacingLanding : shortcutLanding;
                string positionReason = moltenEscape ? moltenEscapeReason : circleDash ? "circle-cue:" + circleReason
                    : densityDash ? "density-alignment" : spacingDash ? "electrified-spacing:" + spacingReason
                    : shortcutLanding != null ? "navigation-contact:" + shortcutReason : "none";
                _assistPositionReplanPending = false;
                bool mapTransit = shortcutTransit;
                bool planned = moltenEscape || circleDash || densityDash || spacingDash || shortcutLanding != null;
                if (!planned)
                    planned = approaching
                        ? TryPlanAssistApproach(target, out landing, out positionReason, out mapTransit)
                        : TryPickAssistDashPoint(target, false, out landing, out positionReason);
                bool requiredUpkeep = refreshDash && !approaching && !moltenEscape
                    && !circleDash && !densityDash && !spacingDash && !contactCorrection;
                if (planned && AllowAssistMovement(target,landing,now,moltenEscape || moltenEscapeOnly,requiredUpkeep,circleDash))
                {
                    _assistPositionReason = navigationRetry ? "navigation-retry:" + positionReason
                        : continuationDash ? "dash-continue:" + positionReason
                        : contactCorrection ? "contact-redash:" + positionReason
                        : landingReplan ? "landing-replan:" + positionReason : positionReason;
                    _assistPositionWorldX = landing.X; _assistPositionWorldY = landing.Y;
                    ReleaseAssistAttack();
                    CancelTarget();
                    if (moltenEscape && IsAssistPositionElite(target)) RememberAssistHazardReturn(target);
                    MaintainDash(dash, now, target, landing, moltenEscape || moltenEscapeOnly,
                        mapTransit, requiredUpkeep, circleDash);
                    if (_selfDashKey != ActionKey.Unknown && !_rangedCleanup && target != null
                        && _assistDamageEscapeTrigger=="low-health-rapid-loss"
                        && positionReason.IndexOf(":reactive-contact",StringComparison.Ordinal)>=0)
                    { _lastPanicContactTick=now; _lastPanicContactTarget=target.AcdId; }
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
                            : circleDash ? "circle-dash" : densityDash ? "density-dash" : spacingDash ? "electrified-spacing-dash" : contactCorrection ? "contact-redash" : "target-dash";
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
            // Consume the confirmed hover before moving the cursor to a newer projection.
            if (TryAssistEliteRetarget(target, now)) return true;
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
            if (cursorNow.X != (int)x || cursorNow.Y != (int)y)
                _assistRetargetAimGameTick = Hud.Game.CurrentGameTick;
            _assistAimX = (int)x; _assistAimY = (int)y; _assistCursorOwned = true;
            // Every in-range owned attack stands still; native recovery owns walking.
            bool scopedStandstill = IsAssistCorpseOverlap() || canAttackHere;
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
                    && !IsAssistCorpseOverlap() && !IsAssistForcedPylonAttackSafe())
                {
                    _assistStatus = "pylon-hover-guard";
                    if (_assistProbeExhausted && !canAttackHere)
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
            _assistExplosionRunStage = 0; _assistExplosionRunPoint = null;
            _assistDoorWalkStage = 0; _assistDoorWalkPoint=null;
            _assistRouteWalkStage=0;_assistRouteWalkPoint=null;
            if (!keepScopedStandstill) ReleaseAssistScopedStandstill();
        }

        private void EndAssist()
        {
            _assistExplosionWalkCommitted = false; ResetAssistEscapeWalkFailures();
            CancelAssistDoor();
            _assistDoorReturnPending = _assistDoorEliteDetour = false;
            _assistDoorReturnEliteAcd = 0u; _assistDoorReturnPoint = null;
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
            _assistSafetyCombatPending = _assistSafetyCombatOnly = false;
            _assistWotHfResumePending = false;
            _assistDamageEscapePending = false; _assistDamageEscapeGameTick = int.MinValue;
            _assistDamageEscapeWorld = 0u; _assistDamageEscapeTrigger = "none";
            _assistDamageWindowTick = int.MinValue; _assistDamageWindowHealth = 1.0;
            _assistCircleCue = "none"; _assistCircleOpportunityAcd = 0u;
            ResetAssistContactRecovery();
            _assistBlockedTargets.Clear();
            _assistFailedLandings.Clear();
            _assistBlockedCircles.Clear(); _assistDashCircleAcd = 0u;
            _circleFailedAt.Clear(); _circleRetried.Clear(); _rangeTrialTarget=_rangeRejectedTarget=0u;
            ResetAssistMovementPolicy();
            _lastPanicContactTick=int.MinValue; _lastPanicContactTarget=0u;
            _assistTrashRecheckGameTick = int.MinValue;
            _assistTrashProfiles.Clear(); _assistTrashSelectionClass = 0;
            _assistLastTrashAttackAcd = 0u; _assistTrashFanReady = false;
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
            _lowHealthFields.Clear();
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

        private bool TryAssistDashUpkeep(IPlayerSkill dash, int now)
        {
            if (!_assistActive || !AssistRequested() || !AutoDash || !DashDue(dash, now)) return false;
            var target = FindAssistCandidate(_assistTargetAcd);
            if (!IsAssistEligible(target) || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)
                || MapAssistTargetBlocked(target)) return false;
            if (!CanApproachDash(dash) || s7o_GenMonkInput.IsDown(dash.Key)
                || s7o_InputReleaseArbiter.HasPendingRelease) return false;
            if (dash.Key == ActionKey.LeftSkill || dash.Key == ActionKey.RightSkill) return false;
            _dashUpkeepAttemptGameTick = Hud.Game.CurrentGameTick;
            IWorldCoordinate circle = null; string reason = "none";
            // This runs before AssistTarget refreshes its safety-only flag.
            // Required upkeep stays available; optional travel yields to healing.
            bool circleUpgrade = !IsAssistHealingRecovery()
                && TryPickAssistCircleUpgrade(target, out circle, out reason);
            CancelTarget();
            if (circleUpgrade)
            { _assistCircleCue = reason; _assistPositionReason = "upkeep-circle:" + reason; }
            MaintainDash(dash, now, target, circleUpgrade ? circle : Hud.Game.Me.FloorCoordinate, false, false, !circleUpgrade);
            if (_selfDashKey == ActionKey.Unknown) return false;
            _dashUpkeepNeedsAttack = true;
            _assistStatus = "dash-upkeep"; return true;
        }

        private bool IsAssistForcedPylonAttackSafe()
        {
            // Standstill converts world LMB to a directional skill cast. Screen UI
            // remains forbidden. Doors and NPCs keep their separate interaction guards.
            if (!_assistActive || !AssistRequested() || _assistMainKey != ActionKey.LeftSkill
                || !s7o_GenMonkInput.IsStandstillDown() || !_assistCursorOwned
                || Hud.Game.CurrentGameTick <= _assistAimGameTick) return false;
            var target = FindAssistCandidate(_assistTargetAcd);
            if (!IsAssistEligible(target) || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)) return false;
            CursorPoint cursor;
            if (!GetCursorPos(out cursor) || cursor.X != _assistAimX || cursor.Y != _assistAimY) return false;
            var hover = Hud.Game.SelectedActor;
            var monster = hover as IMonster;
            // Overlapping monster bodies can hide the chosen target's hover. Owned
            // standstill still casts the main damage skill; keep the selected elite.
            // Admit only an actual local contact, never native follow or an NPC click.
            bool monsterContact = IsAssistStationaryCombat(monster);
            
            return hover == null || (hover.SnoActor != null && hover.SnoActor.Kind == ActorKind.Shrine)
                || monsterContact || (hover!=null && (hover.GizmoType==GizmoType.Chest
                    || hover.GizmoType==GizmoType.BreakableChest || hover.GizmoType==GizmoType.LoreChest
                    || (hover.SnoActor!=null && hover.SnoActor.Sno==ActorSnoEnum._x1_global_chest_speedkill_elite))
                    && !MapAssistTargetBlocked(target));
        }

        private IWorldCoordinate AssistAttackMapEndpoint(IWorldCoordinate from, IMonster target)
        {
            var center = target.FloorCoordinate;
            // A reachable boss needs contact with its near body edge, not a walkable center.
            // Keep a 0.35-yard inset; intervening walls/doors still invalidate the corridor.
            if (!CanAttackAssistFrom(from, target)) return center;
            double distance = from.XYDistanceTo(center);
            if (distance <= 0.001) return from;
            double trim = Math.Min(distance, Math.Max(0.0, target.RadiusScaled - 0.35));
            double scale = (distance - trim) / distance;
            return from.Offset((float)((center.X - from.X) * scale),
                (float)((center.Y - from.Y) * scale), (float)((center.Z - from.Z) * scale));
        }

        private bool MapAssistVerifiedBossContact(IMonster target)
        {
            if (target.Rarity != ActorRarity.Boss || !CanAttackAssistFrom(Hud.Game.Me.FloorCoordinate, target)) return false;
            var status = MapAssistStatus(); if (status == null) return false;
            try {
                var from = Hud.Game.Me.FloorCoordinate; var to = AssistAttackMapEndpoint(from, target);
                var ray = _assistMapGet("corridor", new object[] { status["epoch"], from.X, from.Y, from.Z,
                    to.X, to.Y, to.Z, 0.35f }) as Dictionary<string, object>;
                return ray != null && ray.ContainsKey("state") && (ray["state"] as string) == "ClearCandidate";
            } catch { return false; }
        }

        private bool MapAssistAttackBlocked(IWorldCoordinate from, IMonster target)
        {
            bool trial;
            return AssistAttackMapBlocked(from,target,out trial);
        }
        private bool IsAssistAttackChest(Dictionary<string,object> obstacle)
        {
            if(obstacle==null || !obstacle.ContainsKey("kind"))return false;
            string kind=obstacle["kind"] as string;
            if(kind=="Chest")return true;
            if(kind=="Door" || kind=="Gate" || kind=="BreakableDoor")return false;
            // Native shrine identity permits a short attack ray, never an unsafe landing/click.
            if(obstacle.ContainsKey("sno"))
            {
                uint sno=Convert.ToUInt32(obstacle["sno"]);
                foreach(var actor in Hud.Game.Actors)
                    if(actor.WorldId==Hud.Game.Me.WorldId && actor.SnoActor!=null
                        && (uint)actor.SnoActor.Sno==sno && actor.SnoActor.Kind==ActorKind.Shrine)return true;
            }
            // The native cursed chest can be reported as a switch while its event runs.
            return obstacle.ContainsKey("sno")
                && Convert.ToUInt32(obstacle["sno"])==(uint)ActorSnoEnum._x1_global_chest_speedkill_elite;
        }
        private bool AssistAttackMapBlocked(IWorldCoordinate from, IMonster target, out bool trial)
        {
            trial=false;
            var status=MapAssistStatus(); if(status==null || target==null)return false;
            try {
                var to=AssistAttackMapEndpoint(from,target);
                var ray=_assistMapGet("corridor",new object[]{status["epoch"],from.X,from.Y,from.Z,to.X,to.Y,to.Z,0.35f}) as Dictionary<string,object>;
                if(ray==null || !ray.ContainsKey("state") || (ray["state"] as string)!="BlockedCandidate")return false;
                // This exception never extends ordinary attack reach or permits a landing in a prop.
                if(AssistBodyGap(from,target)>Math.Min(8.0,MeleeAssistRange)
                    || Math.Abs(from.Z-target.FloorCoordinate.Z)>8f || IsAssistBoundaryRejected(target))return true;
                string reason=ray.ContainsKey("reason")?ray["reason"] as string:null;
                if(reason!="NativeWalkGap" && reason!="NativeWallClearance"
                    && reason!="NativeCollisionBox" && reason!="ObstacleFootprint")return true;
                var segment=_assistMapGet("segment",new object[]{status["epoch"],from.X,from.Y,from.Z,to.X,to.Y,to.Z,0.35f}) as Dictionary<string,object>;
                if(segment==null || !segment.ContainsKey("ok") || !Equals(segment["ok"],true))return true;
                foreach(string key in new[]{"nativeCollisionCandidates","obstacleCandidates"}) {
                    if(!segment.ContainsKey(key))return true;
                    var entries=segment[key] as System.Collections.IEnumerable;
                    if(entries==null)return true;
                    foreach(var entry in entries) {
                        var obstacle=entry as Dictionary<string,object>;
                        if(!IsAssistAttackChest(obstacle))return true;
                    }
                }
                trial=true;return false;
            } catch { return true; }
        }

        private bool IsAssistLowHealthFieldPointFree(IWorldCoordinate point)
        {
            if (AssistShieldPylonActive()) return true;
            foreach (var field in _lowHealthFields)
                if (IsAssistFieldBlocking(field) && AssistSameHeight(point, field.FloorCoordinate)
                    && point.XYDistanceTo(field.FloorCoordinate) <= LowHealthFieldRadius(field)) return false;
            return true;
        }

        private double AssistSoftHazardPositionScore(IWorldCoordinate point, IMonster target)
        {
            if (AssistShieldPylonActive()) return 0.0;
            // Pack endpoints estimate chains, not a native chain hitbox. Prefer their
            // outside without introducing another movement/attack permission gate.
            double chain = AssistChainDistance(point, target);
            double score = chain < 6 ? -(6 - chain) : 0;
            if(AssistMoltenTrailContains(point))score-=8;
            foreach(var molten in _assistLiveMolten) {
                if(!AssistSameHeight(point,molten.FloorCoordinate))continue;
                double distance=point.XYDistanceTo(molten.FloorCoordinate);
                if(distance<7)score-=Math.Min(4,7-distance);
            }
            if (!IsAssistLowHealthFieldPointFree(point)) score -= 4;
            if (target != null && HasAssistAffix(target, MonsterAffix.Molten))
            {
                // Lateral contact avoids blindly following behind a fleeing Molten.
                // This is a positioning preference, not invented fire-trail geometry.
                var me = Hud.Game.Me.FloorCoordinate;
                double ax = me.X - target.FloorCoordinate.X, ay = me.Y - target.FloorCoordinate.Y;
                double bx = point.X - target.FloorCoordinate.X, by = point.Y - target.FloorCoordinate.Y;
                double lengths = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                if (lengths > 0.001) score += Math.Abs(ax * by - ay * bx) / lengths * 2.0;
            }
            return score;
        }

        private static double LowHealthFieldRadius(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return 0;
            // Warning extents from existing overlays; not exact native damage hitboxes.
            switch (actor.SnoActor.Sno)
            {
                case ActorSnoEnum._x1_lr_boss_malletdemon_fallingrocks:
                case ActorSnoEnum._a2dun_zolt_random_fallingrocks_c: return Math.Max(20, actor.RadiusScaled) + 2;
                case ActorSnoEnum._x1_westmarchbrute_leap_telegraph:
                case ActorSnoEnum._x1_westmarchbrute_b_leap_telegraph:
                case ActorSnoEnum._p2_westmarchbrute_leap_telegraph: return Math.Max(15, actor.RadiusScaled) + 2;
                case ActorSnoEnum._x1_adria_geyser_pending:
                case ActorSnoEnum._x1_adria_geyser: return Math.Max(10, actor.RadiusScaled) + 2;
                case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_cold_10foot: return Math.Max(10, actor.RadiusScaled) + 2;
                case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_cold_20foot: return Math.Max(20, actor.RadiusScaled) + 2;
                case ActorSnoEnum._monsteraffix_desecrator_damage_aoe: return 8;
                case ActorSnoEnum._monsteraffix_plagued_endcloud: return 12;
                case ActorSnoEnum._monsteraffix_frozen_iceclusters: return 15.8;
                case ActorSnoEnum._x1_monsteraffix_frozenpulse_monster: return 16;
                case ActorSnoEnum._x1_monsteraffix_corpsebomber_projectile: return 3.5;
                case ActorSnoEnum._x1_monsteraffix_orbiter_projectile:
                case ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile: return 4;
                case ActorSnoEnum._morluspellcaster_meteor_pending:
                case ActorSnoEnum._morluspellcast_meteor_castsphere:
                case ActorSnoEnum._morluspellcaster_meteor_model:
                case ActorSnoEnum._morluspellcaster_meteor_impact: return Math.Max(22, actor.RadiusScaled);
                case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_fire_10foot: return Math.Max(12, actor.RadiusScaled);
                default: return 0; // Unknown pools are diagnosed, never given guessed radii.
            }
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
            _dashDeadlineReason = "none";
            if (dash == null || _selfDashKey != ActionKey.Unknown) return false;
            if (!_assistActive && !Due(now, _nextDashAimTick)) return false; // Preserve manual mouse backoff.
            int tick = Hud.Game.CurrentGameTick;
            if (_assistActive && _dashUpkeepNeedsAttack)
            {
                bool resumed = _pulse == ActionKey.Unknown && _assistAttackKey == _assistMainKey
                    && (AssistWotHfHeartbeatEnabled() ? _assistWotHfLastGameTick > _dashUpkeepAttemptGameTick
                        : tick > _dashUpkeepAttemptGameTick && HasAssistAttackContact(FindAssistCandidate(_assistTargetAcd)));
                if (!resumed) { _dashDeadlineReason = "resume-primary-before-retry"; return false; }
                _dashUpkeepNeedsAttack = false;
            }
            if (_assistActive && tick == _dashUpkeepAttemptGameTick) return false;
            string rune = dash.RuneNameEnglish ?? "";
            bool shortRune = dash.Rune == 4 || rune.IndexOf("Radiance", StringComparison.OrdinalIgnoreCase) >= 0
                || rune.IndexOf("Blinding Speed", StringComparison.OrdinalIgnoreCase) >= 0;
            double raiment = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
            if (Hud.Game.Me.GetSetItemCount(755275u) >= 6 && raiment <= 0.6)
            { _dashDeadlineReason = "raiment-deadline"; return true; }
            if (shortRune)
            {
                var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
                double runeLeft = 0; bool runeTimerSeen = false;
                if (buff != null && buff.TimeLeftSeconds != null && buff.TimeElapsedSeconds != null)
                    for (int i = 0; i < Math.Min(buff.TimeLeftSeconds.Length, buff.TimeElapsedSeconds.Length); i++)
                    {
                        double left = Math.Max(0, buff.TimeLeftSeconds[i]);
                        double duration = left + Math.Max(0, buff.TimeElapsedSeconds[i]);
                        if (duration >= 3.75 && duration <= 4.25)
                        { runeTimerSeen = true; runeLeft = Math.Max(runeLeft, left); }
                    }
                if (runeTimerSeen)
                {
                    _dashDeadlineReason = runeLeft <= 0.6 ? "rune-deadline" : "rune-active";
                    return runeLeft <= 0.6;
                }
                // Native clocks accommodate speed changes. No guessed sub-index mapping.
                if (_dashConfirmedWorld == Hud.Game.Me.WorldId && _dashConfirmedGameTick != int.MinValue
                    && tick >= _dashConfirmedGameTick && tick - _dashConfirmedGameTick < 204) return false;
                if (_dashConfirmedGameTick == int.MinValue && _lastDashTick != int.MinValue
                    && ElapsedMs(now, _lastDashTick) < 3400) return false;
                _dashDeadlineReason = "rune-game-clock"; return true;
            }
            if (_lastDashTick == int.MinValue)
            { _dashDeadlineReason = "initial"; return true; }
            if (Hud.Game.Me.GetSetItemCount(755275u) < 6 && ElapsedMs(now, _lastDashTick) >= 5500 && raiment <= 0.6)
            { _dashDeadlineReason = "non-raiment-cadence"; return true; }
            return false;
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
        private struct AssistGroundCheck
        {
            public float X,Y,Z;
            public string State;
        }
        private readonly Dictionary<uint,AssistGroundCheck> _assistGroundChecks=new Dictionary<uint,AssistGroundCheck>();
        private readonly Dictionary<uint,AssistBlockedTarget> _assistBoundaryRejected=new Dictionary<uint,AssistBlockedTarget>();
        private readonly Dictionary<uint,IWorldCoordinate> _assistBoundaryGoals=new Dictionary<uint,IWorldCoordinate>();
        private int _boundaryGoalTick=int.MinValue, _boundaryTrialTick=int.MinValue;
        private uint _boundaryGoalWorld, _boundaryTrialTarget;
        private double _boundaryTrialHealth;
        private string _boundaryTrialState="none";
        private bool IsAssistBoundaryRejected(IMonster target)
        {
            AssistBlockedTarget entry;
            if(!_assistBoundaryRejected.TryGetValue(target.AcdId,out entry))return false;
            var me=Hud.Game.Me.FloorCoordinate;var p=target.FloorCoordinate;
            if(entry.World!=Hud.Game.Me.WorldId || me.XYDistanceTo(entry.PlayerX,entry.PlayerY)>=4
                || p.XYDistanceTo(entry.TargetX,entry.TargetY)>=2) {
                _assistBoundaryRejected.Remove(target.AcdId);return false;
            }
            return true;
        }
        private bool TryAssistBoundaryGoal(IMonster target, out IWorldCoordinate goal)
        {
            int tick=Hud.Game.CurrentGameTick;uint world=Hud.Game.Me.WorldId;
            if(_boundaryGoalTick!=tick || _boundaryGoalWorld!=world) {
                _assistBoundaryGoals.Clear();_boundaryGoalTick=tick;_boundaryGoalWorld=world;
            }
            if(_assistBoundaryGoals.TryGetValue(target.AcdId,out goal))return goal!=null;
            goal=null;
            if(!IsAssistBoundaryRejected(target)) {
                var me=Hud.Game.Me.FloorCoordinate;
                if(CanAttackAssistFrom(me,target) && !MapAssistAttackBlocked(me,target))goal=me;
                double best=double.MaxValue;
                float radius=target.RadiusScaled+(float)Math.Max(1,Math.Min(7.5,MeleeAssistRange-0.5));
                for(int i=0;goal!=me && i<AssistEdgeX.Length;i++) {
                    var point=target.FloorCoordinate.Offset(AssistEdgeX[i]*radius,AssistEdgeY[i]*radius,0f);
                    double distance=me.XYDistanceTo(point);
                    if(distance>=best || !MapAssistLandingAllowed(point) || MapAssistAttackBlocked(point,target))continue;
                    goal=point;best=distance;
                }
            }
            if(_assistBoundaryGoals.Count<128)_assistBoundaryGoals[target.AcdId]=goal;
            return goal!=null;
        }
        private void ObserveAssistBoundaryContact(IMonster target,int now)
        {
            bool trial;
            var me=Hud.Game.Me.FloorCoordinate;
            if(target==null || !CanAttackAssistFrom(me,target)
                || AssistAttackMapBlocked(me,target,out trial) || !trial) {
                _boundaryTrialTarget=0;_boundaryTrialTick=int.MinValue;_boundaryTrialState="none";return;
            }
            if(_boundaryTrialTarget!=target.AcdId) {
                _boundaryTrialTarget=target.AcdId;_boundaryTrialTick=int.MinValue;
                _boundaryTrialHealth=target.CurHealth;_boundaryTrialState="pending-attack";
            }
            if(_assistAttackKey!=_assistMainKey || _assistMainKey==ActionKey.Unknown)return;
            if(_boundaryTrialTick==int.MinValue) { _boundaryTrialTick=now;_boundaryTrialState="trial"; }
            if(Hud.Game.Me.AnimationState==AcdAnimationState.Attacking
                && AttackedIdentityMatches(target.AcdId,target.AnnId) && target.CurHealth<_boundaryTrialHealth) {
                _boundaryTrialTick=now;_boundaryTrialState="corroborated";
            }
            _boundaryTrialHealth=target.CurHealth;
            if(unchecked(now-_boundaryTrialTick)<1000)return;
            if(_assistBoundaryRejected.Count>=64)_assistBoundaryRejected.Clear();
            _assistBoundaryRejected[target.AcdId]=new AssistBlockedTarget {
                World=Hud.Game.Me.WorldId,PlayerX=me.X,PlayerY=me.Y,
                TargetX=target.FloorCoordinate.X,TargetY=target.FloorCoordinate.Y };
            _assistBoundaryGoals.Remove(target.AcdId);_boundaryTrialState="no-contact";
            _assistPositionReplanPending=true;ReleaseAssistAttack();
        }
        private int _assistGroundCheckTick=int.MinValue;
        private uint _assistGroundCheckWorld;
        private long _assistGroundCheckEpoch;

        // Eligibility tests the target's exact floor, not the ray/path from the player
        // or a preferred wall buffer. Unknown coverage keeps native recovery available.
        // Frame/position-scoped caching never delays re-entry onto walkable ground.
        private bool HasAssistBoundaryGoal(IMonster target)
        { IWorldCoordinate goal;return TryAssistBoundaryGoal(target,out goal); }
        private bool MapAssistTargetOnWalkableFloor(IMonster target)
        {
            var status=MapAssistStatus();
            if(status==null) { _assistGroundChecks.Clear();_assistBoundaryGoals.Clear();return true; }
            try {
                int tick=Hud.Game.CurrentGameTick;uint world=Hud.Game.Me.WorldId;
                long epoch=Convert.ToInt64(status["epoch"]);
                if(tick!=_assistGroundCheckTick||world!=_assistGroundCheckWorld||epoch!=_assistGroundCheckEpoch) {
                    _assistGroundChecks.Clear();_assistBoundaryGoals.Clear();_assistGroundCheckTick=tick;
                    _assistGroundCheckWorld=world;_assistGroundCheckEpoch=epoch;
                }
                var p=target.FloorCoordinate;AssistGroundCheck check;
                if(!_assistGroundChecks.TryGetValue(target.AcdId,out check)
                    ||check.X!=p.X||check.Y!=p.Y||check.Z!=p.Z) {
                    var point=_assistMapGet("point",new object[]{epoch,p.X,p.Y,p.Z}) as Dictionary<string,object>;
                    string state=MapAssistTargetGroundState(point,null);
                    if(state!="BlockedCandidate"&&state!="NoWalkCandidate") {
                        var height=_assistMapGet("height",new object[]{epoch,p.X,p.Y,p.Z}) as Dictionary<string,object>;
                        state=MapAssistTargetGroundState(point,height);
                    }
                    check=new AssistGroundCheck {X=p.X,Y=p.Y,Z=p.Z,State=state};
                    if(_assistGroundChecks.Count>=256&&!_assistGroundChecks.ContainsKey(target.AcdId))_assistGroundChecks.Clear();_assistBoundaryGoals.Clear();
                    _assistGroundChecks[target.AcdId]=check;
                }
                return (check.State!="BlockedCandidate"&&check.State!="NoWalkCandidate")
                    || MapAssistVerifiedBossContact(target) || HasAssistBoundaryGoal(target);
            } catch { return true; } // Missing/older optional bridge must not stall combat.
        }
        private static string MapAssistTargetGroundState(Dictionary<string,object> point,Dictionary<string,object> height)
        {
            string state=point!=null&&point.ContainsKey("state")?point["state"] as string:"Unknown";
            string reason=point!=null&&point.ContainsKey("reason")?point["reason"] as string:null;
            if(state=="BlockedCandidate"||reason=="NativeCollisionBoxCandidate"||reason=="ObstacleFootprintCandidate")
                return "BlockedCandidate";
            if(state=="NoWalkCandidate")return state;
            // Broad NavCells can cover a void/fly-only square. The native fine grid
            // supplies the walk bit. Another elevation is not negative evidence.
            if(height!=null&&height.ContainsKey("ok")&&height["ok"] is bool&&(bool)height["ok"]
                &&height.ContainsKey("state")&&(height["state"] as string)=="NativeHeightSample"
                &&height.ContainsKey("flags")&&height.ContainsKey("referenceZGap")) {
                double gap=Convert.ToDouble(height["referenceZGap"]);
                if(!double.IsNaN(gap)&&!double.IsInfinity(gap)&&gap<=4.0&&gap>=0.0
                    &&(Convert.ToUInt32(height["flags"])&1u)==0)return "NoWalkCandidate";
            }
            return state=="WalkCandidate"?state:"Unknown";
        }

        private void ResetMapAssist()
        { _assistMapDoorAcd=0;_assistMapDashTransit=false;_assistMapRouteReady=false;_assistMapGoal=null;_assistMapGoalAcd=0;_assistGroundChecks.Clear();_assistGroundCheckTick=int.MinValue; }
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
            if((!_assistActive&&!AssistRequested())||!AttachMapAssist())return null;
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
                if (edge==null||!edge.ContainsKey("state")||(edge["state"] as string)!="BlockedCandidate")
                { _mapLandingReason="preferred-clearance"; return true; }
                // Prefer two yards, but allow a smaller verified margin in narrow spaces.
                // Blocked centers, void, and actual prop hitboxes remain forbidden.
                if(state!="WalkCandidate") { _mapLandingReason="unknown-clearance"; return false; }
                var tight=_assistMapGet("corridor",new object[]{status["epoch"],point.X,point.Y,point.Z,
                    point.X,point.Y,point.Z,0.35f}) as Dictionary<string,object>;
                bool allowed=tight!=null&&tight.ContainsKey("state")&&(tight["state"] as string)=="ClearCandidate";
                _mapLandingReason=allowed?"tight-walkable":"blocked-clearance";
                return allowed;
            }catch { return true; }
        }
        private bool MapAssistTargetBlocked(IMonster target)
        { return target!=null && MapAssistAttackBlocked(Hud.Game.Me.FloorCoordinate,target); }
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
            if (!TryPickAssistDashPoint(target, false, out landing, out reason, true))
            {
                if (!IsAssistEligible(target) || MapAssistStatus()==null) return false;
                landing=target.FloorCoordinate.Offset(0,0,0);reason="open-room";
            }
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
        private Dictionary<string,object> MapAssistDashHop(Dictionary<string,object> status,
            IWorldCoordinate from, IWorldCoordinate to)
        {
            float preferred = Math.Max(0f, Math.Min(3f, MapWallClearance));
            var hop = _assistMapGet("dash", new object[]{status["epoch"],from.X,from.Y,from.Z,
                to.X,to.Y,to.Z,preferred}) as Dictionary<string,object>;
            // Prefer wall clearance, but permit narrow verified landings. Actual
            // center/closed-door collisions remain hard in native DashHop.
            if (preferred > 0.35f && hop != null && !hop.ContainsKey("waypoint")
                && hop.ContainsKey("state") && (hop["state"] as string) == "BlockedCandidate")
                hop = _assistMapGet("dash", new object[]{status["epoch"],from.X,from.Y,from.Z,
                    to.X,to.Y,to.Z,0.35f}) as Dictionary<string,object>;
            return hop;
        }

        // A near-side body offset may leave the wall between us and the monster.
        // Probe just beyond its far body edge, never beyond one Dash or through a
        // closed door. A landing must have a clear attack corridor back to the goal.
        private bool TryAssistFarSideMapHop(IMonster target, IWorldCoordinate goal,
            Dictionary<string,object> status, out IWorldCoordinate landing)
        {
            landing=null;
            if (!MapAssistTargetBlocked(target)) return false;
            var me=Hud.Game.Me.FloorCoordinate;
            var center=target.FloorCoordinate;
            if(me.XYDistanceTo(center)>AssistDashTravelLimit+12) return false;
            double best=double.MaxValue;
            // Prefer open ground in the target's room. Contact/hover is acquired after travel.
            // Exact endpoint agreement prevents a clipped near-side waypoint masquerading as crossing.
            for(int ring=0;ring<2;ring++)
            {
                float radius=Math.Max(0,target.RadiusScaled)+(ring==0 ? 4f : 8f);
                for(int i=0;i<AssistEdgeX.Length;i++)
                {
                    var aim=center.Offset(AssistEdgeX[i]*radius,AssistEdgeY[i]*radius,0);
                    double distance=me.XYDistanceTo(aim);
                    if(distance<4 || distance>AssistDashTravelLimit-0.5)continue;
                    var hop=_assistMapGet("dash",new object[]{status["epoch"],me.X,me.Y,me.Z,
                        aim.X,aim.Y,aim.Z,Math.Max(1f,Math.Min(3f,MapWallClearance))}) as Dictionary<string,object>;
                    var xyz=hop!=null&&hop.ContainsKey("waypoint") ? hop["waypoint"] as float[] : null;
                    if(xyz==null || xyz.Length!=3)continue;
                    var point=Hud.Window.CreateWorldCoordinate(xyz[0],xyz[1],xyz[2]);
                    if(point.XYDistanceTo(aim)>1.5 || Math.Abs(point.Z-aim.Z)>2
                        || MapAssistAttackBlocked(point,target))continue;
                    int x,y;
                    if(!IsAssistDashPointSafe(point,target,out x,out y,false,!CanAttackAssistFrom(point,target)))continue;
                    double score=point.XYDistanceTo(center)+distance*0.05;
                    if(score>=best)continue;
                    best=score;landing=point;
                }
            }
            return landing!=null;
        }

        private bool TryAssistAngledMapHop(IMonster target, IWorldCoordinate goal,
            Dictionary<string,object> status, out IWorldCoordinate landing, out bool transit)
        {
            landing = null; transit = false;
            if (_assistNavigationTravelBlocked) return false;
            var me = Hud.Game.Me.FloorCoordinate;
            double dx = goal.X - me.X, dy = goal.Y - me.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 4) return false;
            double step = Math.Min(AssistDashTravelLimit - 0.5, length);
            double before = me.XYDistanceTo(target.FloorCoordinate);
            // Three angle pairs, near the original direction. No random aim offsets.
            for (int i = 0; i < 6; i++)
            {
                double angle = ((i / 2) + 1) * Math.PI / 12 * ((i & 1) == 0 ? 1 : -1);
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                var aim = me.Offset((float)((dx * cos - dy * sin) * step / length),
                    (float)((dx * sin + dy * cos) * step / length),
                    (float)((goal.Z - me.Z) * step / length));
                var answer = MapAssistDashHop(status, me, aim);
                var xyz = answer != null && answer.ContainsKey("waypoint") ? answer["waypoint"] as float[] : null;
                if (xyz == null || xyz.Length != 3) continue;
                var point = Hud.Window.CreateWorldCoordinate(xyz[0], xyz[1], xyz[2]);
                if (!AssistUsefulApproachHop(point,target) || point.XYDistanceTo(target.FloorCoordinate) > before - 3) continue;
                bool intermediate = !CanAttackAssistFrom(point, target);
                int x, y;
                if (!IsAssistDashPointSafe(point, target, out x, out y, false, intermediate)) continue;
                landing = point; transit = intermediate; return true;
            }
            return false;
        }

        private bool IsAssistBlockedApproachDirection(IWorldCoordinate goal)
        {
            var me = Hud.Game.Me.FloorCoordinate;
            double gx = goal.X - me.X, gy = goal.Y - me.Y, gl = Math.Sqrt(gx * gx + gy * gy);
            if (gl < 8) return false;
            foreach (var failure in _assistFailedLandings)
            {
                if (failure.World != Hud.Game.Me.WorldId || me.XYDistanceTo(failure.PlayerX, failure.PlayerY) > 4
                    || Math.Abs(me.Z - failure.PlayerZ) > 4) continue;
                double fx = failure.X - me.X, fy = failure.Y - me.Y, fl = Math.Sqrt(fx * fx + fy * fy);
                if (fl < 8 || gl < fl - 4) continue;
                // A changed target behind the same obstacle is not new navigation progress.
                if ((gx * fx + gy * fy) / (gl * fl) >= 0.94) return true;
            }
            return false;
        }

        private bool TryMapAssistApproach(IMonster target,ref IWorldCoordinate landing,out bool transit)
        {
            transit=false;_assistMapRouteReady=false;_assistMapDoorAcd=0;_assistMapPlanReason="planning";
            var status=MapAssistStatus();
            bool sameBlockedDirection = IsAssistBlockedApproachDirection(landing);
            if(status==null){_assistMapPlanReason="map-unavailable";return !sameBlockedDirection && TryBoundAssistHop(target,ref landing,out transit);}
            try {
                var me=Hud.Game.Me.FloorCoordinate;
                if(_assistMapGoal==null||_assistMapGoalAcd!=target.AcdId||_assistMapGoalWorld!=target.WorldId
                    ||_assistMapTargetPosition==null||_assistMapTargetPosition.XYDistanceTo(target.FloorCoordinate)>4f
                    // A cached contact can be on the wrong side after small target
                    // movement or newly loaded geometry. Use the current valid goal.
                    || (CanAttackAssistFrom(_assistMapGoal,target) && MapAssistAttackBlocked(_assistMapGoal,target))) {
                    _assistMapGoalAcd=target.AcdId;_assistMapGoalWorld=target.WorldId;
                    _assistMapGoal=landing.Offset(0f,0f,0f);_assistMapTargetPosition=target.FloorCoordinate.Offset(0f,0f,0f);
                }
                landing=_assistMapGoal.Offset(0f,0f,0f);
                // Validate the individual Dash before asking for a walking detour.
                // Nearby doors and unresolved walking routes are not shortcut vetoes.
                if (!sameBlockedDirection && !_assistNavigationTravelBlocked && (!_assistNavigationPending || _assistNavigationTargetAcd == target.AcdId)) {
                    IWorldCoordinate beyond;
                    if (TryAssistFarSideMapHop(target,landing,status,out beyond))
                    {
                        landing=beyond;transit=!CanAttackAssistFrom(beyond,target);_assistMapRouteReady=true;
                        _assistMapPlanReason="shortcut-far-side";return true;
                    }
                    var hop = MapAssistDashHop(status, me, landing);
                    var xyz=hop!=null&&hop.ContainsKey("waypoint")?hop["waypoint"] as float[]:null;
                    if(xyz!=null&&xyz.Length==3) {
                        var point=Hud.Window.CreateWorldCoordinate(xyz[0],xyz[1],xyz[2]);
                        bool intermediate=point.XYDistanceTo(landing)>1.5f;
                        int x,y;
                        if(AssistUsefulApproachHop(point,target)
                            && !(CanAttackAssistFrom(point,target) && MapAssistAttackBlocked(point,target))
                            && IsAssistDashPointSafe(point,target,out x,out y,false,intermediate)) {
                            landing=point;transit=intermediate;_assistMapRouteReady=true;_assistMapPlanReason="shortcut-direct";return true;
                        }
                    }
                    else if (hop == null || (hop.ContainsKey("state") && (hop["state"] as string) == "Unknown"))
                    {
                        var bounded = landing.Offset(0f,0f,0f);
                        if (TryBoundAssistHop(target,ref bounded,out transit))
                        { landing=bounded;_assistMapRouteReady=true;_assistMapPlanReason="shortcut-native-probe";return true; }
                    }
                }
                // The straight 50-yard endpoint may lie in the wall even when a
                // nearby angled endpoint clears it. Try bounded, validated shortcuts
                // before waiting for a walking detour. Per-hop door vetoes remain native.
                if (!sameBlockedDirection && !_assistNavigationTravelBlocked
                    && TryAssistAngledMapHop(target, landing, status, out var angled, out var angledTransit))
                { landing = angled; transit = angledTransit; _assistMapRouteReady = true; _assistMapPlanReason="shortcut-angled"; return true; }
                var answer=_assistMapGet("path",new object[]{status["epoch"],me.X,me.Y,me.Z,
                    landing.X,landing.Y,landing.Z,0.6f}) as Dictionary<string,object>;
                if(answer==null||!answer.ContainsKey("state")||(answer["state"] as string)=="Unavailable")
                    return !sameBlockedDirection && TryBoundAssistHop(target,ref landing,out transit);
                if(answer.ContainsKey("doorAcd"))_assistMapDoorAcd=Convert.ToUInt32(answer["doorAcd"]);
                // Pending/no-route uses native navigation immediately, never timed idle.
                if(!answer.ContainsKey("waypoint")){_assistMapPlanReason=_assistMapDoorAcd!=0u?"route-door":"route-no-waypoint";return false;}
                var routeXYZ=answer["waypoint"] as float[];if(routeXYZ==null||routeXYZ.Length!=3)return false;
                var routePoint=Hud.Window.CreateWorldCoordinate(routeXYZ[0],routeXYZ[1],routeXYZ[2]);
                bool routeTransit=routePoint.XYDistanceTo(landing)>1.5f;
                int rx,ry;if(!AssistUsefulApproachHop(routePoint,target) || !IsAssistDashPointSafe(routePoint,target,out rx,out ry,false,routeTransit))return false;
                landing=routePoint;transit=routeTransit;_assistMapRouteReady=true;_assistMapPlanReason="walking-route-hop";return true;
            }catch { _assistMapGet=null;_assistMapPlugin=null;return TryBoundAssistHop(target,ref landing,out transit); }
        }


        private void MaintainDash(IPlayerSkill dash, int now, IMonster approachTarget = null,
            IWorldCoordinate assistLanding = null, bool assistEscapeOnly = false, bool mapTransit = false, bool upkeep = false, bool circleUpgrade = false)
        {
            try
            {
                if (!CanApproachDash(dash)) return;
                if (_assistActive && assistLanding != null
                    && !AllowAssistMovement(approachTarget,assistLanding,now,assistEscapeOnly,upkeep,circleUpgrade)) return;
                if (SelfDash || approachTarget != null || assistEscapeOnly
                    || (_assistActive && mapTransit && assistLanding != null))
                {
                    if (s7o_GenMonkInput.IsDown(dash.Key)
                        || s7o_InputReleaseArbiter.HasPendingRelease) return;
                    var self = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
                    IMonster attackTarget = approachTarget ?? (!_assistActive && _mainKey == ActionKey.LeftSkill
                        && !s7o_GenMonkInput.IsStandstillDown() ? ManualAttackTarget() : null);
                    if (attackTarget != null)
                        self = attackTarget.FloorCoordinate.ToScreenCoordinate(true, true);
                    if ((approachTarget != null || assistEscapeOnly || (_assistActive && mapTransit)) && assistLanding != null)
                    {
                        int landingX, landingY;
                        if (!IsAssistDashPointSafe(assistLanding, approachTarget, out landingX, out landingY, assistEscapeOnly, mapTransit, false, upkeep)) return;
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
                    // Validate the destination before suspending owned combat.
                    if (_assistActive) ReleaseAssistAttack();
                    if (!SetCursorPos(_dashTargetX, _dashTargetY)) return;
                    _selfDashUpkeep = upkeep;
                    _selfDashCircleUpgrade = circleUpgrade;
                    _restoreCursor = true;
                    _selfDashKey = dash.Key;
                    if (_assistActive && assistEscapeOnly) _assistSafetyCombatPending = true;
                    _selfDashApproach = approachTarget != null || (_assistActive && (assistEscapeOnly
                        || (mapTransit && assistLanding != null)));
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
                    _assistDashForHazard = !upkeep && _selfDashApproach && (assistEscapeOnly
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
                    // A moving target can leave the planned melee endpoint before
                    // key-down. Keep a safe progress hop instead of inventing collision
                    // evidence from that reach change and abandoning a valid shortcut.
                    if (_assistActive && !_selfDashUpkeep && !_assistDashEscapeOnly
                        && liveDashTarget != null && !CanAttackAssistFrom(_assistDashLanding, liveDashTarget)
                        && Hud.Game.Me.FloorCoordinate.XYDistanceTo(liveDashTarget.FloorCoordinate)
                            - _assistDashLanding.XYDistanceTo(liveDashTarget.FloorCoordinate) >= 3)
                        _assistMapDashTransit = true;
                    if (!IsAssistDashPointSafe(_assistDashLanding, liveDashTarget, out landingX, out landingY, _assistDashEscapeOnly, _assistMapDashTransit, false, _selfDashUpkeep))
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
                if (_assistActive && _selfDashApproach && _assistDashLanding != null
                    && !AllowAssistMovement(liveDashTarget,_assistDashLanding,now,_assistDashEscapeOnly,_selfDashUpkeep,_selfDashCircleUpgrade))
                { CancelTarget(); _selfDashResult="movement-held"; return; }
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
                // The movement ledger is updated only after fresh native landing evidence.
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
                    if (buffNow.TimeLeftSeconds[i] > Math.Max(0, before - assistTimerAge) + 0.1) observed = true;
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
            if (observed && !_selfDashObserved)
            { _dashConfirmedGameTick = Hud.Game.CurrentGameTick; _dashConfirmedWorld = Hud.Game.Me.WorldId; }
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
                    && !approachInFlight && (_assistDashFreshAnimation || (_selfDashObserved && (_assistDashPositionObserved || !movingDash)))
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
                if (freshLanding && usefulTravel && IsAssistPositionWorldPoint(actualLanding))
                    RecordAssistMovement(liveContact,actualLanding,now,_assistDashEscapeOnly);
                _dashAwaiting = false;
                if (!_selfDashUpkeep && _selfDashTargetAcd == _assistTargetAcd)
                {
                    _assistApproachPending = false;
                    _assistDashRetryNeedsAttack = !freshLanding;
                }
                ObserveAssistDashContact(actualLanding, now);
                var continuingTarget = FindAssistCandidate(_selfDashTargetAcd);
                // A range-capped Dash is useful progress. Continue immediately after its
                // key release and fresh landing; do not assume a fixed 40-yard range.
                _assistDashContinuationPending = !_selfDashUpkeep && freshLanding && !_assistDashMovementFailed && _assistDashActualSafe
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
                if (_assistDashForHazard && _assistDamageEscapeTrigger == "low-health-rapid-loss")
                { _assistDamageEscapePending = false; _assistDamageEscapeTrigger = "none"; }
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
            _dashUpkeepNeedsAttack = false;
            _panicMustAttack = false;
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

