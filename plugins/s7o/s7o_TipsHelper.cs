using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_TipsHelper : BasePlugin, IInGameWorldPainter, IInGameTopPainter, IAfterCollectHandler, INewAreaHandler, ITransparentCollection, IKeyEventHandler, ISkillCooldownHandler
    {
        private const int AncientRank = 1;
        private const int PrimalRank = 2;
        private const int PlayerMarkerSlots = 4;
        private const uint BloodIsPowerSno = 465037u;
        private const uint LandOfTheDeadSno = 465839u;
        private const uint SimulacrumSno = 465350u;
        private const uint BloodRushSno = 454090u;
        private const uint ChannelingPylonBuffSno = 266258u;
        private const int BloodIsPowerCooldownSettleTicks = 60;
        private const int BloodIsPowerStrongCooldownDropTicks = 360;
        private const int BloodIsPowerAreaRebaseTicks = 45;
        private const float BloodIsPowerMaxSaneLossPct = 140f;
        private const float BloodIsPowerTrackedIconConfirmFloorPct = 80f;
        private const int BloodIsPowerReviveRebaseTicks = 45;
        private const int BloodIsPowerBloodRushPairWindowTicks = 45;
        private const int BloodIsPowerBloodRushFreshStartTicks = 30;
        private const int BloodIsPowerBloodRushNoDuplicateTicks = 20;
        private const int BloodIsPowerBloodRushZeroLossDelayTicks = 18;
        private const float BloodIsPowerBloodRushTeleportMinYards = 8f;
        private const float BloodIsPowerBloodRushMinObservedLossPct = 0.75f;
        private const float BloodIsPowerBloodRushCreditMinShortfallPct = 0.50f;
        private const int BloodIsPowerPostProcVisualSuppressTicks = 45;
        private const int BloodIsPowerFreshLossDropConfirmTicks = 30;
        private const float BloodIsPowerFreshLossDropConfirmFloorPct = 0.50f;
        private const double BloodIsPowerNewGreaterRiftMaxPercent = 3.0d;
        private const int BloodIsPowerGreaterRiftEntryCheckTicks = 180;
        private const int BloodIsPowerGreaterRiftStartResetGuardTicks = 120;

        public bool VisualHelpersEnabled { get; set; } = true;

        public bool ShowHealthGlobeDots { get; set; } = true;
        public bool ShowRiftProgressOrbDots { get; set; } = true;
        public bool ShowAncientItems { get; set; } = true;
        public bool ShowPrimalItems { get; set; } = true;
        public bool ShowItemGroundCircles { get; set; } = true;
        public bool ShowItemMinimapMarkers { get; set; } = true;
        public bool ShowItemScreenEdgeArrows { get; set; } = true;
        public bool ShowItemAlertText { get; set; } = true;
        public bool ShowItemAlertTextAbovePlayer { get; set; } = true;
        public bool ShowItemAlertTextNearMinimap { get; set; } = true;
        public bool ShowItemAlertDirectionArrow { get; set; } = true;
        public bool ShowAncientItemArrows { get; set; } = true;
        public bool ShowPrimalItemArrows { get; set; } = true;
        public bool ShowAncientItemAlertText { get; set; } = true;
        public bool ShowPrimalItemAlertText { get; set; } = true;
        public bool ShowPlayerMarkers { get; set; } = true;
        public bool ShowPlayerGroundCircles { get; set; } = true;
        public bool ShowPlayerMinimapDots { get; set; } = true;
        public bool ShowPlayerPortraitMarker { get; set; } = true;
        public bool ShowValleyOfDeathCircle { get; set; } = false;
        public bool ShowPylonProgressMarkers { get; set; } = true;
        public bool ShowPoolTracker { get; set; } = true;
        public bool ShowPoolPartyStatus { get; set; } = true;
        public bool ShowVisitedWaypointMarkers { get; set; } = true;
        public bool ShowBloodIsPowerTracker { get; set; } = true;

        public int PylonProgressMarkerMaxAgeMs { get; set; } = 7200000;
        public float PylonProgressIconSize { get; set; } = 26.0f;
        public float PylonProgressIconGapPx { get; set; } = 6.0f;
        public float PoolStatusLineGapPx { get; set; } = 1.0f;
        public float PoolTopTextYFrac { get; set; } = 0.115f;
        public float PoolListOffsetXPx { get; set; } = 8.0f;
        public float PoolListOffsetYPx { get; set; } = 4.0f;
        public int PoolArrowLifetimeMs { get; set; } = 10000;
        public int PoolArrowFadeMs { get; set; } = 700;
        public int PoolArrowCycleMs { get; set; } = 700;
        public float PoolDirectionArrowDistancePx { get; set; } = 190.0f;
        public float PoolDirectionArrowLengthPx { get; set; } = 52.0f;
        public float PoolDirectionArrowShaftWidthPx { get; set; } = 16.0f;
        public float PoolDirectionArrowHeadLengthPx { get; set; } = 21.0f;
        public float PoolDirectionArrowHeadWidthPx { get; set; } = 36.0f;
        public float PoolDirectionArrowOutlinePx { get; set; } = 6.0f;
        public float PoolDirectionArrowMotionPx { get; set; } = 3.0f;
        public float PoolDirectionArrowMultiOffsetPx { get; set; } = 18.0f;
        public int PoolArrowFlyToPoolMs { get; set; } = 200;
        public int PoolArrowHoldNearPlayerMs { get; set; } = 0;
        public float PoolArrowVisibleTargetOffsetPx { get; set; } = 18.0f;
        public int PoolAreaTransitionSettleMs { get; set; } = 450;
        public int PoolListTransitionFreezeMs { get; set; } = 2500;
        public float PoolSpotMergeDistance { get; set; } = 18.0f;
        public float PoolTakenByPlayerMaxDistance { get; set; } = 320.0f;
        public float PoolVisibleConfirmMaxDistance { get; set; } = 120.0f;
        public int PoolLocalAreaOverrideSettleMs { get; set; } = 650;
        public int PoolWorldTransitionSettleMs { get; set; } = 1500;
        public bool PoolUseAreaRectHints { get; set; } = true;
        public bool PoolSuppressKnownFalseMarkerRects { get; set; } = true;
        public float VisitedWaypointMarkerRadius { get; set; } = 5.0f;
        public float VisitedWaypointMarkerOffsetXPx { get; set; } = 96.0f;

        public int HealthGlobeColorR { get; set; } = 255;
        public int HealthGlobeColorG { get; set; } = 25;
        public int HealthGlobeColorB { get; set; } = 25;
        public int RiftOrbColorR { get; set; } = 185;
        public int RiftOrbColorG { get; set; } = 70;
        public int RiftOrbColorB { get; set; } = 255;
        public int AncientColorR { get; set; } = 185;
        public int AncientColorG { get; set; } = 70;
        public int AncientColorB { get; set; } = 255;
        public int PrimalColorR { get; set; } = 255;
        public int PrimalColorG { get; set; } = 35;
        public int PrimalColorB { get; set; } = 35;
        public int AncientTextColorR { get; set; } = 185;
        public int AncientTextColorG { get; set; } = 70;
        public int AncientTextColorB { get; set; } = 255;
        public int PrimalTextColorR { get; set; } = 255;
        public int PrimalTextColorG { get; set; } = 35;
        public int PrimalTextColorB { get; set; } = 35;
        public int ValleyOfDeathColorR { get; set; } = 100;
        public int ValleyOfDeathColorG { get; set; } = 57;
        public int ValleyOfDeathColorB { get; set; } = 170;

        public int Player1ColorR { get; set; } = 185;
        public int Player1ColorG { get; set; } = 70;
        public int Player1ColorB { get; set; } = 255;
        public int Player2ColorR { get; set; } = 255;
        public int Player2ColorG { get; set; } = 35;
        public int Player2ColorB { get; set; } = 35;
        public int Player3ColorR { get; set; } = 35;
        public int Player3ColorG { get; set; } = 225;
        public int Player3ColorB { get; set; } = 85;
        public int Player4ColorR { get; set; } = 70;
        public int Player4ColorG { get; set; } = 150;
        public int Player4ColorB { get; set; } = 255;

        public float HealthGlobeDotRadius { get; set; } = 0.80f;
        public float RiftOrbDotRadius { get; set; } = 1.05f;
        public float AncientGroundEllipseRadiusX { get; set; } = 34.0f;
        public float AncientGroundEllipseRadiusY { get; set; } = 12.0f;
        public float PrimalGroundEllipseRadiusX { get; set; } = 36.0f;
        public float PrimalGroundEllipseRadiusY { get; set; } = 12.5f;
        public int ItemGroundCirclePulseMs { get; set; } = 620;
        public float ItemGroundCirclePulseScale { get; set; } = 0.16f;
        public int ItemGroundCircleAlpha { get; set; } = 255;
        public int ItemGroundCircleOutlineAlpha { get; set; } = 245;
        public float ItemGroundCircleStrokeWidth { get; set; } = 6.5f;
        public float ItemGroundCircleOutlineWidth { get; set; } = 9.5f;
        public float AncientMinimapTriangleRadius { get; set; } = 13.0f;
        public float PrimalMinimapTriangleRadius { get; set; } = 15.0f;
        public bool ClampItemMinimapMarkers { get; set; } = true;
        public int ItemMarkerMaxAgeMs { get; set; } = 900000;
        public float ItemPickupCleanupDistance { get; set; } = 20.0f;
        public int ItemTownMissingCleanupDelayMs { get; set; } = 350;
        public float ItemOffscreenRestoreMatchDistance { get; set; } = 6.0f;

        public float ItemScreenEdgeArrowRadius { get; set; } = 13.0f;
        public float ItemScreenEdgeArrowMargin { get; set; } = 34.0f;
        public float ItemScreenEdgeInteriorMargin { get; set; } = 44.0f;
        public float ItemScreenEdgeArrowBottomUiReservedFrac { get; set; } = 0.18f;
        public float ItemScreenEdgeArrowBottomUiReservedPixels { get; set; } = 0.0f;

        public int ItemAlertTextHoldMs { get; set; } = 5000;
        public int ItemAlertTextFadeMs { get; set; } = 1500;
        public int ItemAlertTextMaxLines { get; set; } = 4;
        public int ItemAlertTextMaxCharacters { get; set; } = 52;
        public float ItemAlertTextBaseSize { get; set; } = 9.0f;
        public float ItemAlertTextPeakSize { get; set; } = 18.0f;
        public int AncientAlertTextHoldMs { get; set; } = 5000;
        public int PrimalAlertTextHoldMs { get; set; } = 5000;
        public float AncientAlertTextSize { get; set; } = 9.0f;
        public float PrimalAlertTextSize { get; set; } = 9.0f;
        // Minimap toast text stays compact; HUD Menu text-size controls only affect above-player toast text.
        public float ItemAlertMinimapTextSize { get; set; } = 9.0f;
        public int ItemAlertTextPopMs { get; set; } = 560;
        public int ItemAlertTextOutlinePixels { get; set; } = 4;
        public float ItemAlertTextLineHeight { get; set; } = 20.0f;
        public float ItemAlertPlayerXOffset { get; set; } = 0.0f;
        public float ItemAlertPlayerStartYOffset { get; set; } = -115.0f;
        public float ItemAlertPlayerSettledYOffset { get; set; } = -205.0f;
        public float ItemAlertMinimapXOffset { get; set; } = 0.0f;
        public float ItemAlertMinimapStartYFrac { get; set; } = 0.50f;
        public float ItemAlertMinimapSettledYFrac { get; set; } = 0.10f;
        public float ItemAlertMinimapSettledYOffset { get; set; } = 6.0f;
        public float ItemAlertArrowLength { get; set; } = 27.0f;
        public float ItemAlertArrowHeadSize { get; set; } = 10.5f;
        public float ItemAlertArrowYOffset { get; set; } = 30.0f;
        public float ItemAlertArrowPulsePixels { get; set; } = 4.0f;
        public int ItemAlertArrowPulseMs { get; set; } = 420;
        public int ItemAlertArrowAlpha { get; set; } = 255;
        public int ItemAlertArrowHighlightAlpha { get; set; } = 135;
        public int ItemAlertArrowHighlightBoost { get; set; } = 70;

        public bool EnablePlayerMarkerHotkey { get; set; } = true;
        public Key PlayerMarkerHotkeyKey { get; set; } = Key.F7;
        public IKeyEvent PlayerMarkerHotkey { get; private set; }
        public float PlayerCircleScreenRadiusX { get; set; } = 30.0f;
        public float PlayerCircleScreenRadiusY { get; set; } = 8.5f;
        public float PlayerCircleScreenYOffset { get; set; } = 17.0f;
        public int PlayerCircleFillAlpha { get; set; } = 86;
        public int PlayerCircleOutlineAlpha { get; set; } = 225;
        public int PlayerCircleBlackOutlineAlpha { get; set; } = 235;
        public float PlayerCircleOutlineWidth { get; set; } = 2.0f;
        public float PlayerCircleBlackOutlineWidth { get; set; } = 4.0f;
        public float PlayerMinimapDotRadius { get; set; } = 7.0f;
        public bool ClampPlayerMinimapDots { get; set; } = true;
        public float PlayerPortraitDotRadius { get; set; } = 4.5f;
        public float PlayerPortraitOffsetX { get; set; } = 7.0f;
        public float PlayerPortraitOffsetY { get; set; } = 28.0f;
        public float PlayerPortraitArrowLength { get; set; } = 11.5f;
        public float PlayerPortraitArrowHeadSize { get; set; } = 5.2f;
        public float ValleyOfDeathRadiusYards { get; set; } = 15.0f;
        public float ValleyOfDeathTimerSeconds { get; set; } = 15.0f;
        public float ValleyOfDeathLineThickness { get; set; } = 4.0f;
        public int ValleyOfDeathAlpha { get; set; } = 230;
        public int ValleyOfDeathOutlineAlpha { get; set; } = 230;

        public IBrush HealthGlobeBrush { get; private set; }
        public IBrush RiftOrbBrush { get; private set; }
        public IBrush AncientRingBrush { get; private set; }
        public IBrush AncientRingOutlineBrush { get; private set; }
        public IBrush PrimalRingBrush { get; private set; }
        public IBrush PrimalRingOutlineBrush { get; private set; }
        public IBrush AncientMapBrush { get; private set; }
        public IBrush AncientMapOutlineBrush { get; private set; }
        public IBrush PrimalMapBrush { get; private set; }
        public IBrush PrimalMapOutlineBrush { get; private set; }
        public IBrush AncientScreenArrowBrush { get; private set; }
        public IBrush AncientScreenArrowOutlineBrush { get; private set; }
        public IBrush PrimalScreenArrowBrush { get; private set; }
        public IBrush PrimalScreenArrowOutlineBrush { get; private set; }
        public IBrush AncientAlertArrowBrush { get; private set; }
        public IBrush AncientAlertArrowOutlineBrush { get; private set; }
        public IBrush AncientAlertArrowHighlightBrush { get; private set; }
        public IBrush PrimalAlertArrowBrush { get; private set; }
        public IBrush PrimalAlertArrowOutlineBrush { get; private set; }
        public IBrush PrimalAlertArrowHighlightBrush { get; private set; }
        public IFont[] AncientFadeFonts { get; private set; }
        public IFont[] PrimalFadeFonts { get; private set; }
        public IFont[] AncientOutlineFadeFonts { get; private set; }
        public IFont[] PrimalOutlineFadeFonts { get; private set; }
        public IFont[] AncientPopFonts { get; private set; }
        public IFont[] PrimalPopFonts { get; private set; }
        public IFont[] AncientOutlinePopFonts { get; private set; }
        public IFont[] PrimalOutlinePopFonts { get; private set; }
        public IFont[] AncientMinimapFadeFonts { get; private set; }
        public IFont[] PrimalMinimapFadeFonts { get; private set; }
        public IFont[] AncientMinimapOutlineFadeFonts { get; private set; }
        public IFont[] PrimalMinimapOutlineFadeFonts { get; private set; }
        public IFont[] AncientMinimapPopFonts { get; private set; }
        public IFont[] PrimalMinimapPopFonts { get; private set; }
        public IFont[] AncientMinimapOutlinePopFonts { get; private set; }
        public IFont[] PrimalMinimapOutlinePopFonts { get; private set; }
        public IBrush PlayerCircleBlackBrush { get; private set; }
        public IBrush PlayerDotOutlineBrush { get; private set; }
        public IBrush[] PlayerFillBrushes { get; private set; }
        public IBrush[] PlayerOutlineBrushes { get; private set; }
        public IBrush[] PlayerDotBrushes { get; private set; }
        public IBrush ValleyOfDeathBrush { get; private set; }
        public IBrush ValleyOfDeathOutlineBrush { get; private set; }
        public IBrush PylonProgressLineBrush { get; private set; }
        public IBrush PylonProgressLineOutlineBrush { get; private set; }
        public IBrush PoolGroundBrush { get; private set; }
        public IBrush PoolGroundOutlineBrush { get; private set; }
        public IBrush PoolArrowLightBrush { get; private set; }
        public IBrush PoolArrowShadowBrush { get; private set; }
        public IBrush VisitedWaypointBrush { get; private set; }
        public IBrush VisitedWaypointOutlineBrush { get; private set; }
        public IFont PylonProgressTextFont { get; private set; }
        public IFont PoolLabelFont { get; private set; }
        public IFont PoolReadyFont { get; private set; }
        public IFont PoolMissingFont { get; private set; }
        public IFont PoolOutlineFont { get; private set; }
        public IFont VisitedWaypointFont { get; private set; }

        private const int BloodIsPowerColorSteps = 11;
        private IFont[] _bipFonts;
        private IBrush _bipBarBackBrush;
        private IBrush[] _bipBarFillBrushes;
        private IBrush _bipBarBorderBrush;

        private float _bipProgressPct;
        private bool _bipEstimateUncertain;
        private float _bipLastHealth;
        private float _bipLastHealthMax;
        private int[] _bipSkillState;
        private int[] _bipCooldownStartState;
        private int[] _bipCooldownFinishState;
        private bool _bipSkillStateSeeded;
        private int _bipLastPlayerIndex = -1;
        private int _bipPendingProcStartTick;
        private int _bipAreaRebaseUntilTick;
        private float _bipAreaSnapshotPct;
        private bool _bipWasDead;
        private int _bipReviveRebaseUntilTick;
        private float _bipDeathSnapshotPct;
        private bool _bipInactive;
        private string _bipInactiveReason;
        private int _bipLastBloodRushStartTick;
        private int _bipLastConfirmedProcTick;
        private int _bipLastBloodRushCreditStartTick;

        private readonly List<BloodRushCreditCandidate> _bipBloodRushCandidates = new List<BloodRushCreditCandidate>();
        private int _bipLastBloodRushCreditTick;
        private float _bipLastPlayerX;
        private float _bipLastPlayerY;
        private uint _bipLastPlayerWorldId;
        private int _bipLastPlayerPositionTick;
        private bool _bipLastBloodRushChanneling;
        private int _bipLastBloodRushObservedLossTick;
        private float _bipLastBloodRushObservedLossPct;
        private int _bipLastFreshLossTick;
        private float _bipLastFreshLossPct;
        private bool _bipLastInGreaterRift;
        private bool _bipPendingGreaterRiftStartCheck;
        private int _bipGreaterRiftEntryTick;
        private double _bipLastGreaterRiftPercent = -1.0d;
        private int _bipLastGreaterRiftStartResetTick;

        private const ActorSnoEnum ValleyOfDeathActorSno = ActorSnoEnum._dh_markedfordeath_proxyactor;
        private readonly Dictionary<string, ItemMarker> _items = new Dictionary<string, ItemMarker>();
        private readonly List<PlayerMark> _players = new List<PlayerMark>();
        private long _alertSequence;
        private long _latestAbovePlayerSequence;
        private string _resourceSignature;
        private RotatingTriangleShapePainter _trianglePainter;
        private StandardPingRadiusTransformator _mapPulse;
        private GroundTimerDecorator _valleyOfDeathTimer;
        private GroundLabelDecorator _valleyOfDeathLabel;
        private IFont _valleyOfDeathFont;
        private string _valleyOfDeathFontSignature;

        // Pool/pylon/visited-map helpers are rebuilt from native FreeHUD data; marker-only pools are intentionally kept simple for range.
        private readonly Dictionary<string, PylonProgressMark> _pylonProgressMarks = new Dictionary<string, PylonProgressMark>();
        private readonly Dictionary<string, PoolSpot> _poolSpots = new Dictionary<string, PoolSpot>();
        private readonly Dictionary<string, ConsumedPoolSpot> _consumedPoolSpots = new Dictionary<string, ConsumedPoolSpot>();
        private readonly List<string> _poolListOrder = new List<string>();
        private readonly List<string> _poolFrozenListOrder = new List<string>();
        private readonly Dictionary<uint, long> _partyPoolBonusRemaining = new Dictionary<uint, long>();
        private readonly Dictionary<string, VisitedArea> _visitedAreas = new Dictionary<string, VisitedArea>();
        private readonly Dictionary<IUiElement, BountyAct> _actMapFallbackElements = new Dictionary<IUiElement, BountyAct>();
        private double _lastRiftPercent = -1.0d;
        private bool _lastGreaterRiftState;
        private long _poolFirstSeenSequence;
        private readonly Dictionary<string, PoolArrowAlert> _activePoolArrows = new Dictionary<string, PoolArrowAlert>();
        private string _currentPoolAreaKey = string.Empty;
        private string _pendingPoolAreaKey = string.Empty;
        private long _pendingPoolAreaSinceMs;
        private long _currentPoolAreaSinceMs;
        private uint _poolLastPlayerWorldId;
        private uint _poolLastPlayerWorldSno;
        private long _poolWorldChangedSinceMs;
        private long _poolListFrozenUntilMs;
        private long _poolAreaVisitSequence;
        private string _sessionServerIp = string.Empty;
        private int _lastGameTickSeen = -1;

        private static readonly LabelRule[] ExactLabels =
        {
            R("Ring", "ring"), R("Amulet", "amulet"), R("Mighty Belt", "beltbarbarian"), R("Voodoo Mask", "voodoomask"),
            R("Spirit Stone", "spiritstone", "spiritstonemonk"), R("Wizard Hat", "wizardhat"), R("Cloak", "cloak"),
            R("Crusader Shield", "crusadershield"), R("Phylactery", "necromanceroffhand"), R("Belt", "belt", "genericbelt"),
            R("Chest", "genericchestarmor", "chestarmor"), R("Helm", "generichelm", "helm"), R("Shoulders", "shoulders"),
            R("Gloves", "gloves"), R("Bracers", "bracers"), R("Boots", "boots"), R("Pants", "legs"),
            R("Shield", "shield"), R("Quiver", "quiver"), R("Mojo", "mojo"), R("Source", "orb"),
            R("Mighty Weapon", "mightyweapon1h"), R("2H Mighty Weapon", "mightyweapon2h"), R("Flail", "flail", "flail1h"),
            R("2H Flail", "flail2h"), R("Scythe", "scythe", "scythe1h"), R("2H Scythe", "scythe2h"),
            R("Ceremonial Knife", "ceremonialdagger"), R("Fist Weapon", "fistweapon", "battlecestus", "greatertalons", "wristsword"),
            R("Daibo", "combatstaff"), R("Hand Crossbow", "handxbow", "repeatingcrossbow"), R("Crossbow", "crossbow", "ballista"),
            R("Bow", "bow", "hydrabow"), R("2H Axe", "axe2h"), R("Axe", "axe", "flyingaxe"), R("2H Mace", "mace2h"),
            R("Mace", "mace"), R("2H Sword", "sword2h", "colossusblade"), R("Sword", "sword", "championsword"),
            R("Dagger", "dagger", "boneknife", "legendspike"), R("Spear", "spear", "hyperionspear"), R("Polearm", "polearm"),
            R("Staff", "staff", "archonstaff"), R("Wand", "wand", "gravewand", "swirlingcrystal")
        };

        private static readonly LabelRule[] SearchLabels =
        {
            R("Mighty Belt", "mightybelt", "barbbelt", "beltbarbarian", "belt_barbarian"), R("Crusader Shield", "crusadershield", "crusader shield"),
            R("Phylactery", "necromanceroffhand", "necromancer offhand", "phylactery"), R("Voodoo Mask", "voodoomask", "voodoo mask"),
            R("Spirit Stone", "spiritstone", "spirit stone"), R("Wizard Hat", "wizardhat", "wizard hat"), R("Cloak", "cloak"),
            R("Quiver", "quiver"), R("Mojo", "mojo"), R("Source", "source", "wizardorb", "wizard orb", "orb"), R("Shield", "shield"),
            R("2H Mighty Weapon", "mightyweapon2h", "mightyweapon_2h", "2h mighty", "twohandedmighty", "two-handed mighty"),
            R("Mighty Weapon", "mightyweapon1h", "mightyweapon_1h", "mightyweapon", "mighty weapon"),
            R("2H Flail", "flail2h", "flail_2h", "2h flail", "twohandedflail", "two-handed flail"), R("Flail", "flail1h", "flail_1h", "flail"),
            R("2H Scythe", "scythe2h", "scythe_2h", "2h scythe", "twohandedscythe", "two-handed scythe"), R("Scythe", "scythe1h", "scythe_1h", "scythe"),
            R("Ceremonial Knife", "ceremonialdagger", "ceremonial dagger", "ceremonialknife", "ceremonial knife"), R("Fist Weapon", "fistweapon", "fist weapon"),
            R("Daibo", "combatstaff", "daibo"), R("Hand Crossbow", "handxbow", "handcrossbow", "hand crossbow"), R("Crossbow", "crossbow", "xbow"), R("Bow", "bow"),
            R("2H Axe", "axe2h", "axe_2h", "2h axe", "twohandedaxe", "two-handed axe"), R("2H Mace", "mace2h", "mace_2h", "2h mace", "twohandedmace", "two-handed mace"),
            R("2H Sword", "sword2h", "sword_2h", "2h sword", "twohandedsword", "two-handed sword"), R("Polearm", "polearm"), R("Staff", "staff"),
            R("Wand", "wand"), R("Dagger", "dagger"), R("Spear", "spear"), R("Axe", "axe1h", "axe_1h", "onehandedaxe", "one-handed axe", "axe"),
            R("Mace", "mace1h", "mace_1h", "onehandedmace", "one-handed mace", "mace"), R("Sword", "sword1h", "sword_1h", "onehandedsword", "one-handed sword", "sword"),
            R("Helm", "helm", "helmet"), R("Chest", "chestarmor", "chest armor", "chest"), R("Shoulders", "shoulder"), R("Gloves", "glove", "hands"),
            R("Bracers", "bracer"), R("Belt", "belt", "waist"), R("Pants", "pants", "legs"), R("Boots", "boots", "feet"), R("Ring", "ring"), R("Amulet", "amulet", "neck")
        };

        public s7o_TipsHelper()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            SetPlayerMarkerHotkey(PlayerMarkerHotkeyKey);
            RebuildResources(true);
            RegisterVisitedWaypointActFallbackElements();
            ClearRuntime();
        }

        public void SetPlayerMarkerHotkey(Key key)
        {
            if (key == Key.Unknown)
                return;

            PlayerMarkerHotkeyKey = key;
            try
            {
                PlayerMarkerHotkey = Hud != null && Hud.Input != null
                    ? Hud.Input.CreateKeyEvent(true, PlayerMarkerHotkeyKey, false, false, false)
                    : null;
            }
            catch
            {
                PlayerMarkerHotkey = null;
            }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ClearItems();
            ResetBloodIsPowerTrackerForArea(newGame);
            if (newGame && IsNewGameSession())
            {
                _players.Clear();
                ClearSessionTrackers();
            }
        }

        public void AfterCollect()
        {
            if (!IsGameReady())
            {
                ClearItems();
                return;
            }

            UpdateGameSession(false);

            if (!VisualHelpersEnabled)
            {
                ClearItems();
                return;
            }

            RebuildResources(false);
            UpdateBloodIsPowerTracker();
            UpdateItemMarkers();
            PurgePlayerMarks();
            UpdatePylonProgressMarkers();
            UpdatePoolTracker();
            UpdateVisitedAreas();
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!VisualHelpersEnabled || !EnablePlayerMarkerHotkey || keyEvent == null || !keyEvent.IsPressed || !IsGameReady())
                return;

            bool hotkeyMatches = PlayerMarkerHotkey != null
                ? PlayerMarkerHotkey.Matches(keyEvent)
                : keyEvent.Key == PlayerMarkerHotkeyKey;

            if (!hotkeyMatches)
                return;

            IPlayer player;
            if (TryGetHoveredPortraitPlayer(out player))
                TogglePlayerMark(player);
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (!IsGameReady() || !VisualHelpersEnabled)
                return;

            RebuildResources(false);

            if (layer == WorldLayer.Ground)
            {
                PaintPickupDots();
                PaintItemGroundCircles();
                PaintPlayerGroundCircles();
                PaintValleyOfDeathCircles();
            }
            else if (layer == WorldLayer.Map)
            {
                PaintItemMinimapMarkers();
                PaintPlayerMinimapDots();
            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!IsGameReady() || !VisualHelpersEnabled || Hud.Render.UiHidden)
                return;

            if (clipState != ClipState.BeforeClip && clipState != ClipState.AfterClip)
                return;

            RebuildResources(false);

            if (IsFullMapOpen())
            {
                if (clipState == ClipState.AfterClip)
                {
                    UpdateVisitedAreas();
                    PaintVisitedWaypointMarkers();
                }
                else
                    PaintPoolTrackerTop();
                return;
            }

            if (clipState != ClipState.BeforeClip)
                return;

            PaintPylonProgressMarkers();
            PaintPoolTrackerTop();
            PaintPoolWorldStatus();
            PaintPoolDirectionArrows();
            PaintItemScreenEdgeArrows();
            PaintItemAlerts();
            PaintPlayerPortraitMarkers();
            DrawBloodIsPowerTracker();
        }

        public IEnumerable<ITransparent> GetTransparents()
        {
            foreach (var brush in Brushes(HealthGlobeBrush, RiftOrbBrush, AncientRingBrush, AncientRingOutlineBrush, PrimalRingBrush, PrimalRingOutlineBrush,
                AncientMapBrush, AncientMapOutlineBrush, PrimalMapBrush, PrimalMapOutlineBrush, AncientScreenArrowBrush, AncientScreenArrowOutlineBrush,
                PrimalScreenArrowBrush, PrimalScreenArrowOutlineBrush, AncientAlertArrowBrush, AncientAlertArrowOutlineBrush, AncientAlertArrowHighlightBrush,
                PrimalAlertArrowBrush, PrimalAlertArrowOutlineBrush, PrimalAlertArrowHighlightBrush, PlayerCircleBlackBrush, PlayerDotOutlineBrush, ValleyOfDeathBrush, ValleyOfDeathOutlineBrush,
                PylonProgressLineBrush, PylonProgressLineOutlineBrush, PoolGroundBrush, PoolGroundOutlineBrush, PoolArrowLightBrush, PoolArrowShadowBrush, VisitedWaypointBrush, VisitedWaypointOutlineBrush, _bipBarBackBrush, _bipBarBorderBrush))
                yield return brush;
            foreach (var brush in Brushes(_bipBarFillBrushes)) yield return brush;

            foreach (var font in Fonts(new[] { PylonProgressTextFont, PoolLabelFont, PoolReadyFont, PoolMissingFont, PoolOutlineFont, VisitedWaypointFont })) yield return font;
            foreach (var font in Fonts(_bipFonts)) yield return font;

            foreach (var brush in Brushes(PlayerFillBrushes)) yield return brush;
            foreach (var brush in Brushes(PlayerOutlineBrushes)) yield return brush;
            foreach (var brush in Brushes(PlayerDotBrushes)) yield return brush;
            foreach (var font in Fonts(AncientFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientPopFonts)) yield return font;
            foreach (var font in Fonts(PrimalPopFonts)) yield return font;
            foreach (var font in Fonts(AncientOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(PrimalOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapPopFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapPopFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapOutlinePopFonts)) yield return font;
        }

        private void RebuildResources(bool force)
        {
            var sig = BuildResourceSignature();
            if (!force && sig == _resourceSignature)
                return;

            _resourceSignature = sig;
            HealthGlobeBrush = Hud.Render.CreateBrush(235, C(HealthGlobeColorR), C(HealthGlobeColorG), C(HealthGlobeColorB), 0);
            RiftOrbBrush = Hud.Render.CreateBrush(235, C(RiftOrbColorR), C(RiftOrbColorG), C(RiftOrbColorB), 0);
            AncientRingBrush = Hud.Render.CreateBrush(C(ItemGroundCircleAlpha), C(AncientColorR), C(AncientColorG), C(AncientColorB), ItemGroundCircleStrokeWidth);
            AncientRingOutlineBrush = Hud.Render.CreateBrush(C(ItemGroundCircleOutlineAlpha), 0, 0, 0, ItemGroundCircleOutlineWidth);
            PrimalRingBrush = Hud.Render.CreateBrush(C(ItemGroundCircleAlpha), C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), ItemGroundCircleStrokeWidth);
            PrimalRingOutlineBrush = Hud.Render.CreateBrush(C(ItemGroundCircleOutlineAlpha), 0, 0, 0, ItemGroundCircleOutlineWidth);
            AncientMapBrush = Hud.Render.CreateBrush(255, C(AncientColorR), C(AncientColorG), C(AncientColorB), 4);
            AncientMapOutlineBrush = Hud.Render.CreateBrush(180, 0, 0, 0, 1);
            PrimalMapBrush = Hud.Render.CreateBrush(255, C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 4);
            PrimalMapOutlineBrush = Hud.Render.CreateBrush(180, 0, 0, 0, 1);
            AncientScreenArrowBrush = Hud.Render.CreateBrush(255, C(AncientColorR), C(AncientColorG), C(AncientColorB), 4);
            AncientScreenArrowOutlineBrush = Hud.Render.CreateBrush(210, 0, 0, 0, 6.5f);
            PrimalScreenArrowBrush = Hud.Render.CreateBrush(255, C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 4);
            PrimalScreenArrowOutlineBrush = Hud.Render.CreateBrush(210, 0, 0, 0, 6.5f);
            AncientAlertArrowBrush = Hud.Render.CreateBrush(C(ItemAlertArrowAlpha), C(AncientColorR), C(AncientColorG), C(AncientColorB), 0);
            AncientAlertArrowOutlineBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 0);
            AncientAlertArrowHighlightBrush = Hud.Render.CreateBrush(C(ItemAlertArrowHighlightAlpha), C(AncientColorR + ItemAlertArrowHighlightBoost), C(AncientColorG + ItemAlertArrowHighlightBoost), C(AncientColorB + ItemAlertArrowHighlightBoost), 0);
            PrimalAlertArrowBrush = Hud.Render.CreateBrush(C(ItemAlertArrowAlpha), C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 0);
            PrimalAlertArrowOutlineBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 0);
            PrimalAlertArrowHighlightBrush = Hud.Render.CreateBrush(C(ItemAlertArrowHighlightAlpha), C(PrimalColorR + ItemAlertArrowHighlightBoost), C(PrimalColorG + ItemAlertArrowHighlightBoost), C(PrimalColorB + ItemAlertArrowHighlightBoost), 0);
            AncientFadeFonts = CreateFadeFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), AncientAlertTextSize);
            PrimalFadeFonts = CreateFadeFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), PrimalAlertTextSize);
            AncientOutlineFadeFonts = CreateFadeFonts(0, 0, 0, AncientAlertTextSize);
            PrimalOutlineFadeFonts = CreateFadeFonts(0, 0, 0, PrimalAlertTextSize);
            AncientPopFonts = CreatePopFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), AncientAlertTextSize);
            PrimalPopFonts = CreatePopFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), PrimalAlertTextSize);
            AncientOutlinePopFonts = CreatePopFonts(0, 0, 0, AncientAlertTextSize);
            PrimalOutlinePopFonts = CreatePopFonts(0, 0, 0, PrimalAlertTextSize);
            AncientMinimapFadeFonts = CreateFadeFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), ItemAlertMinimapTextSize);
            PrimalMinimapFadeFonts = CreateFadeFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), ItemAlertMinimapTextSize);
            AncientMinimapOutlineFadeFonts = CreateFadeFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PrimalMinimapOutlineFadeFonts = CreateFadeFonts(0, 0, 0, ItemAlertMinimapTextSize);
            AncientMinimapPopFonts = CreatePopFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), ItemAlertMinimapTextSize);
            PrimalMinimapPopFonts = CreatePopFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), ItemAlertMinimapTextSize);
            AncientMinimapOutlinePopFonts = CreatePopFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PrimalMinimapOutlinePopFonts = CreatePopFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PlayerCircleBlackBrush = Hud.Render.CreateBrush(C(PlayerCircleBlackOutlineAlpha), 0, 0, 0, PlayerCircleBlackOutlineWidth);
            PlayerDotOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 0);
            PlayerFillBrushes = CreatePlayerBrushes(PlayerCircleFillAlpha, 0);
            PlayerOutlineBrushes = CreatePlayerBrushes(PlayerCircleOutlineAlpha, PlayerCircleOutlineWidth);
            PlayerDotBrushes = CreatePlayerBrushes(255, 0);
            ValleyOfDeathOutlineBrush = Hud.Render.CreateBrush(C(ValleyOfDeathOutlineAlpha), 0, 0, 0, Math.Max(1.0f, ValleyOfDeathLineThickness + 3.6f));
            ValleyOfDeathBrush = Hud.Render.CreateBrush(C(ValleyOfDeathAlpha), C(ValleyOfDeathColorR), C(ValleyOfDeathColorG), C(ValleyOfDeathColorB), Math.Max(0.5f, ValleyOfDeathLineThickness));
            PylonProgressLineOutlineBrush = Hud.Render.CreateBrush(220, 0, 0, 0, 2.6f);
            PylonProgressLineBrush = Hud.Render.CreateBrush(245, 255, 220, 70, 1.2f);
            PoolGroundOutlineBrush = Hud.Render.CreateBrush(240, 0, 0, 0, 0);
            PoolGroundBrush = Hud.Render.CreateBrush(235, 255, 230, 30, 0);
            PoolArrowLightBrush = Hud.Render.CreateBrush(90, 255, 255, 255, 0);
            PoolArrowShadowBrush = Hud.Render.CreateBrush(105, 95, 75, 0, 0);
            VisitedWaypointOutlineBrush = Hud.Render.CreateBrush(230, 0, 0, 0, 4.2f);
            VisitedWaypointBrush = Hud.Render.CreateBrush(245, 65, 225, 85, 2.2f);
            PylonProgressTextFont = Hud.Render.CreateFont("tahoma", 6.8f, 255, 255, 230, 85, true, false, 255, 0, 0, 0, true);
            PoolLabelFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 230, 35, true, false, 255, 0, 0, 0, true);
            PoolReadyFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 80, 255, 80, true, false, 255, 0, 0, 0, true);
            PoolMissingFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 255, 70, 70, true, false, 255, 0, 0, 0, true);
            PoolOutlineFont = Hud.Render.CreateFont("tahoma", 8.8f, 245, 0, 0, 0, true, false, false);
            VisitedWaypointFont = Hud.Render.CreateFont("tahoma", 7.0f, 255, 210, 255, 210, true, false, 255, 0, 0, 0, true);
            CreateBloodIsPowerVisualResources();
            EnsureBloodIsPowerStateArrays();
            _trianglePainter = new RotatingTriangleShapePainter(Hud);
            _mapPulse = new StandardPingRadiusTransformator(Hud, 333);
        }

        private string BuildResourceSignature()
        {
            return string.Join("|", new[]
            {
                HealthGlobeColorR, HealthGlobeColorG, HealthGlobeColorB, RiftOrbColorR, RiftOrbColorG, RiftOrbColorB,
                AncientColorR, AncientColorG, AncientColorB, PrimalColorR, PrimalColorG, PrimalColorB,
                AncientTextColorR, AncientTextColorG, AncientTextColorB, PrimalTextColorR, PrimalTextColorG, PrimalTextColorB,
                ValleyOfDeathColorR, ValleyOfDeathColorG, ValleyOfDeathColorB, C((int)(ValleyOfDeathLineThickness * 10)), ValleyOfDeathAlpha, ValleyOfDeathOutlineAlpha,
                Player1ColorR, Player1ColorG, Player1ColorB, Player2ColorR, Player2ColorG, Player2ColorB,
                Player3ColorR, Player3ColorG, Player3ColorB, Player4ColorR, Player4ColorG, Player4ColorB,
                PlayerCircleFillAlpha, PlayerCircleOutlineAlpha, PlayerCircleBlackOutlineAlpha, C((int)(PlayerCircleOutlineWidth * 10)), C((int)(PlayerCircleBlackOutlineWidth * 10)),
                ItemGroundCircleAlpha, ItemGroundCircleOutlineAlpha, C((int)(ItemGroundCircleStrokeWidth * 10)), C((int)(ItemGroundCircleOutlineWidth * 10)),
                ItemAlertArrowAlpha, ItemAlertArrowHighlightAlpha, ItemAlertArrowHighlightBoost,
                C((int)(AncientAlertTextSize * 10)), C((int)(PrimalAlertTextSize * 10)), C((int)(ItemAlertMinimapTextSize * 10)),
                AncientAlertTextHoldMs, PrimalAlertTextHoldMs
            }.Select(x => x.ToString()).ToArray());
        }

        private IBrush[] CreatePlayerBrushes(int alpha, float strokeWidth)
        {
            return new[]
            {
                Hud.Render.CreateBrush(C(alpha), C(Player1ColorR), C(Player1ColorG), C(Player1ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player2ColorR), C(Player2ColorG), C(Player2ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player3ColorR), C(Player3ColorG), C(Player3ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player4ColorR), C(Player4ColorG), C(Player4ColorB), strokeWidth)
            };
        }

        private IFont[] CreateFadeFonts(int r, int g, int b, float size)
        {
            var fonts = new IFont[11];
            var fontSize = Math.Max(6.0f, size);
            for (var i = 0; i < fonts.Length; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", fontSize, (int)Math.Round(255.0d * i / 10.0d), r, g, b, true, false, false);
            return fonts;
        }

        private IFont[] CreatePopFonts(int r, int g, int b, float size)
        {
            var count = 31;
            var fonts = new IFont[count];
            var min = Math.Max(6.0f, size);
            var max = Math.Max(min, size * 2.0f);
            for (var i = 0; i < count; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", min + ((max - min) * i / (count - 1)), 255, r, g, b, true, false, false);
            return fonts;
        }

        private void ClearRuntime()
        {
            ClearItems();
            _players.Clear();
            ClearSessionTrackers();
            ResetBloodIsPowerTracker();
            ResetBloodIsPowerGreaterRiftLifecycle();
        }

        private void ClearSessionTrackers()
        {
            _pylonProgressMarks.Clear();
            _poolSpots.Clear();
            _consumedPoolSpots.Clear();
            _poolListOrder.Clear();
            _poolFrozenListOrder.Clear();
            _partyPoolBonusRemaining.Clear();
            _visitedAreas.Clear();
            _activePoolArrows.Clear();
            _poolFirstSeenSequence = 0;
            _currentPoolAreaKey = string.Empty;
            _pendingPoolAreaKey = string.Empty;
            _pendingPoolAreaSinceMs = 0;
            _currentPoolAreaSinceMs = 0;
            _poolListFrozenUntilMs = 0;
            _poolAreaVisitSequence = 0;
            _lastRiftPercent = -1.0d;
            _lastGreaterRiftState = false;
        }

        private void ClearItems()
        {
            _items.Clear();
            _alertSequence = 0;
            _latestAbovePlayerSequence = 0;
        }

        private void RegisterVisitedWaypointActFallbackElements()
        {
            _actMapFallbackElements.Clear();
            try
            {
                AddActMapFallbackElement("Root.NormalLayer.WaypointMap_main.LayoutRoot.OverlayContainer.POI.entry 0.LayoutRoot.Town", BountyAct.A1);
                AddActMapFallbackElement("Root.NormalLayer.WaypointMap_main.LayoutRoot.OverlayContainer.POI.entry 19.LayoutRoot.Town", BountyAct.A2);
                AddActMapFallbackElement("Root.NormalLayer.WaypointMap_main.LayoutRoot.OverlayContainer.POI.entry 30.LayoutRoot.Town", BountyAct.A3);
                AddActMapFallbackElement("Root.NormalLayer.WaypointMap_main.LayoutRoot.OverlayContainer.POI.entry 44.LayoutRoot.Town", BountyAct.A4);
                AddActMapFallbackElement("Root.NormalLayer.WaypointMap_main.LayoutRoot.OverlayContainer.POI.entry 58.LayoutRoot.Town", BountyAct.A5);
            }
            catch { }
        }

        private void AddActMapFallbackElement(string path, BountyAct act)
        {
            try
            {
                var element = Hud.Render.RegisterUiElement(path, null, null);
                if (element != null && !_actMapFallbackElements.ContainsKey(element))
                    _actMapFallbackElements[element] = act;
            }
            catch { }
        }

        private bool IsNewGameSession()
        {
            try
            {
                var ip = GetCurrentServerIp();
                var tick = Hud != null && Hud.Game != null ? Hud.Game.CurrentGameTick : -1;
                if (!string.IsNullOrEmpty(ip))
                {
                    if (string.IsNullOrEmpty(_sessionServerIp))
                    {
                        _sessionServerIp = ip;
                        _lastGameTickSeen = tick;
                        return true;
                    }

                    if (!string.Equals(_sessionServerIp, ip, StringComparison.OrdinalIgnoreCase))
                    {
                        _sessionServerIp = ip;
                        _lastGameTickSeen = tick;
                        return true;
                    }
                }

                if (_lastGameTickSeen > 0 && tick >= 0 && tick + 1000 < _lastGameTickSeen)
                {
                    _lastGameTickSeen = tick;
                    return true;
                }

                if (tick >= 0)
                    _lastGameTickSeen = Math.Max(_lastGameTickSeen, tick);
            }
            catch { }

            return false;
        }

        private void UpdateGameSession(bool force)
        {
            try
            {
                var ip = GetCurrentServerIp();
                var tick = Hud.Game.CurrentGameTick;

                // Do not clear pool/waypoint session state from ordinary loading or area-transfer blips.
                // OnNewArea(newGame:true) owns real session resets; this method only records the latest
                // observed key/tick so same-game rejoin logic still has data without wiping map state.
                if (!string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(_sessionServerIp))
                    _sessionServerIp = ip;

                if (tick >= 0)
                    _lastGameTickSeen = Math.Max(_lastGameTickSeen, tick);
            }
            catch { }
        }

        private string GetCurrentServerIp()
        {
            try { return Hud != null && Hud.Game != null ? (Hud.Game.ServerIpAddress ?? string.Empty) : string.Empty; }
            catch { return string.Empty; }
        }

        private bool IsGameReady()
        {
            return Hud != null && Hud.Game != null && Hud.Game.IsInGame && !Hud.Game.IsLoading && Hud.Game.Me != null;
        }

        private bool IsFullMapOpen()
        {
            return Hud.Game.MapMode == MapMode.WaypointMap || Hud.Game.MapMode == MapMode.ActMap || Hud.Game.MapMode == MapMode.Map;
        }

        private bool IsNecromancer()
        {
            try { return Hud.Game.Me.HeroClassDefinition.HeroClass == HeroClass.Necromancer; }
            catch { return false; }
        }

        private bool HasBloodIsPowerPassive()
        {
            try
            {
                return Hud.Game.Me.Powers.UsedPassives.Any(p =>
                    p != null &&
                    (p.Sno == BloodIsPowerSno || p.Sno == Hud.Sno.SnoPowers.Necromancer_Passive_BloodIsPower.Sno));
            }
            catch { return false; }
        }

        private void EnsureBloodIsPowerStateArrays()
        {
            if (_bipSkillState == null || _bipSkillState.Length < 7)
                _bipSkillState = new int[] { 0, 0, 0, 0, 0, 0, 0 };
            if (_bipCooldownStartState == null || _bipCooldownStartState.Length < 7)
                _bipCooldownStartState = new int[] { 0, 0, 0, 0, 0, 0, 0 };
            if (_bipCooldownFinishState == null || _bipCooldownFinishState.Length < 7)
                _bipCooldownFinishState = new int[] { 0, 0, 0, 0, 0, 0, 0 };
        }

        private bool IsBloodIsPowerTrackedSno(uint sno)
        {
            return sno == LandOfTheDeadSno || sno == SimulacrumSno ||
                   sno == Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno ||
                   sno == Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno;
        }

        private bool SkillMatchesBloodIsPowerSno(IPlayerSkill skill, uint fallbackSno, uint hudSno)
        {
            if (skill == null)
                return false;

            try { if (skill.SnoPower != null && (skill.SnoPower.Sno == fallbackSno || skill.SnoPower.Sno == hudSno)) return true; } catch { }
            try { if (skill.CurrentSnoPower != null && (skill.CurrentSnoPower.Sno == fallbackSno || skill.CurrentSnoPower.Sno == hudSno)) return true; } catch { }
            try { if (skill.OverrideSnoPower != null && (skill.OverrideSnoPower.Sno == fallbackSno || skill.OverrideSnoPower.Sno == hudSno)) return true; } catch { }

            return false;
        }

        private bool IsBloodIsPowerTrackedSkill(IPlayerSkill skill)
        {
            if (skill == null)
                return false;

            try { if (skill.SnoPower != null && IsBloodIsPowerTrackedSno(skill.SnoPower.Sno)) return true; } catch { }
            try { if (skill.CurrentSnoPower != null && IsBloodIsPowerTrackedSno(skill.CurrentSnoPower.Sno)) return true; } catch { }
            try { if (skill.OverrideSnoPower != null && IsBloodIsPowerTrackedSno(skill.OverrideSnoPower.Sno)) return true; } catch { }

            return false;
        }

        private IPlayerSkill GetBloodIsPowerTrackedSkill(uint fallbackSno, uint hudSno)
        {
            try
            {
                var necro = Hud.Game.Me.Powers.UsedNecromancerPowers;
                if (necro != null)
                {
                    if (fallbackSno == LandOfTheDeadSno && necro.LandOfTheDead != null)
                        return necro.LandOfTheDead;
                    if (fallbackSno == SimulacrumSno && necro.Simulacrum != null)
                        return necro.Simulacrum;
                }
            }
            catch { }

            try
            {
                foreach (var skill in Hud.Game.Me.Powers.CurrentSkills)
                    if (SkillMatchesBloodIsPowerSno(skill, fallbackSno, hudSno))
                        return skill;
            }
            catch { }

            return null;
        }

        private List<IPlayerSkill> GetBloodIsPowerTrackedSkills()
        {
            var skills = new List<IPlayerSkill>(2);

            IPlayerSkill lotd = null;
            IPlayerSkill simulacrum = null;

            try { lotd = GetBloodIsPowerTrackedSkill(LandOfTheDeadSno, Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno); } catch { }
            try { simulacrum = GetBloodIsPowerTrackedSkill(SimulacrumSno, Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno); } catch { }

            if (lotd != null)
                skills.Add(lotd);
            if (simulacrum != null && !skills.Any(s => s != null && s.Key == simulacrum.Key))
                skills.Add(simulacrum);

            return skills;
        }

        private int GetBloodIsPowerSkillIndex(IPlayerSkill skill)
        {
            try
            {
                int idx = 1 + (int)skill.Key;
                return idx >= 0 && idx < 7 ? idx : -1;
            }
            catch { return -1; }
        }

        private int GetBloodIsPowerIconCount(IBuff buff, IPlayerSkill skill)
        {
            if (buff == null || buff.IconCounts == null || skill == null)
                return 0;

            int idx = GetBloodIsPowerSkillIndex(skill);
            if (idx < 0 || idx >= buff.IconCounts.Length)
                return 0;

            try { return buff.IconCounts[idx]; }
            catch { return 0; }
        }

        private bool IsBloodIsPowerPendingDisplaySkill(IPlayerSkill skill, IBuff buff)
        {
            if (skill == null || buff == null || buff.IconCounts == null)
                return false;

            try
            {
                if (!IsBloodIsPowerTrackedSkill(skill) || !skill.IsOnCooldown)
                    return false;

                return GetBloodIsPowerIconCount(buff, skill) != 1;
            }
            catch { return false; }
        }

        private bool HasAnyUnconsumedBloodIsPowerDisplayCooldown(IBuff buff)
        {
            if (buff == null || buff.IconCounts == null)
                return false;

            try
            {
                var skills = GetBloodIsPowerTrackedSkills();
                foreach (var skill in skills)
                    if (IsBloodIsPowerPendingDisplaySkill(skill, buff))
                        return true;
            }
            catch { }

            return false;
        }

        

        

        

        

        

        private sealed class BloodIsPowerProcSignals
        {
            public bool PendingDisplay;
            public bool PendingProc;

            public bool ReceivedAny;
            public bool IgnoredLowCooldownDrop;
            public bool CooldownDropConfirmed;

            public int MaxAnyDropTicks;
            public int MaxTrackedDropTicks;

            public string ProcReason = string.Empty;
            public string IconEdgeKey = string.Empty;
            public string DropSkillKey = string.Empty;

            public string AllIconEdgeKeys = string.Empty;
            public string TrackedIconEdgeKey = string.Empty;
            public string NonTrackedIconEdgeKey = string.Empty;

            public bool TrackedIconEdge;
            public bool NonTrackedIconEdge;
        }

        private sealed class BloodRushCreditCandidate
        {
            public int Tick;
            public int StartTick;
            public int ExpireTick;

            public float ExpectedCostPct;
            public float ObservedPct;

            public bool HandlerConfirmed;
            public bool PollConfirmed;
            public bool TeleportConfirmed;
            public bool Credited;
            public bool Completed;
        }


        private bool IsBloodIsPowerBloodRushSkill(IPlayerSkill skill)
        {
            if (skill == null)
                return false;

            try { if (skill.SnoPower != null && skill.SnoPower.Sno == BloodRushSno) return true; } catch { }
            try { if (skill.CurrentSnoPower != null && skill.CurrentSnoPower.Sno == BloodRushSno) return true; } catch { }
            try { if (skill.OverrideSnoPower != null && skill.OverrideSnoPower.Sno == BloodRushSno) return true; } catch { }

            try
            {
                uint hudSno = Hud.Sno.SnoPowers.Necromancer_BloodRush.Sno;
                try { if (skill.SnoPower != null && skill.SnoPower.Sno == hudSno) return true; } catch { }
                try { if (skill.CurrentSnoPower != null && skill.CurrentSnoPower.Sno == hudSno) return true; } catch { }
                try { if (skill.OverrideSnoPower != null && skill.OverrideSnoPower.Sno == hudSno) return true; } catch { }
            }
            catch { }

            return false;
        }

        private IPlayerSkill GetBloodIsPowerBloodRushSkill()
        {
            try
            {
                var necro = Hud.Game.Me.Powers.UsedNecromancerPowers;
                if (necro != null && necro.BloodRush != null)
                    return necro.BloodRush;
            }
            catch { }

            try
            {
                uint hudSno = Hud.Sno.SnoPowers.Necromancer_BloodRush.Sno;
                foreach (var skill in Hud.Game.Me.Powers.CurrentSkills)
                {
                    if (skill == null)
                        continue;

                    try { if (skill.SnoPower != null && (skill.SnoPower.Sno == BloodRushSno || skill.SnoPower.Sno == hudSno)) return skill; } catch { }
                    try { if (skill.CurrentSnoPower != null && (skill.CurrentSnoPower.Sno == BloodRushSno || skill.CurrentSnoPower.Sno == hudSno)) return skill; } catch { }
                    try { if (skill.OverrideSnoPower != null && (skill.OverrideSnoPower.Sno == BloodRushSno || skill.OverrideSnoPower.Sno == hudSno)) return skill; } catch { }
                }
            }
            catch { }

            return null;
        }

        

        private float GetBloodIsPowerBloodRushExpectedCostPct(IPlayerSkill skill)
        {
            if (skill == null)
                return 0f;

            string rune = string.Empty;
            try { rune = skill.RuneNameEnglish ?? string.Empty; } catch { }

            if (rune.IndexOf("Hemostasis", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0f;

            if (rune.IndexOf("Metabolism", StringComparison.OrdinalIgnoreCase) >= 0)
                return 10f;

            return 5f;
        }

        private bool IsBloodIsPowerChannelingPylonActive()
        {
            try
            {
                return Hud.Game.Me != null && Hud.Game.Me.Powers != null &&
                       Hud.Game.Me.Powers.BuffIsActive(ChannelingPylonBuffSno);
            }
            catch
            {
                return false;
            }
        }

        public void OnCooldown(IPlayerSkill playerSkill, bool expired)
        {
            if (expired || playerSkill == null || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return;

            if (!IsBloodIsPowerBloodRushSkill(playerSkill))
                return;

            int tick = Hud.Game.CurrentGameTick;

            int start = 0;
            try { start = playerSkill.CooldownStartTick; } catch { }

            QueueBloodRushCandidate(tick, start, playerSkill, true, false, false);
        }


        private bool IsFreshBloodRushStart(int tick, int start, int finish, bool supportingSignal)
        {
            if (start <= 0 || start >= int.MaxValue / 2)
                return false;

            if (finish > 0 && finish < int.MaxValue / 2 && finish <= start)
                return false;

            if (Math.Abs(tick - start) <= BloodIsPowerBloodRushFreshStartTicks)
                return true;

            return supportingSignal && Math.Abs(tick - start) <= BloodIsPowerBloodRushPairWindowTicks;
        }

        private bool UpdateBloodRushMovementValidator(int tick)
        {
            try
            {
                var me = Hud.Game.Me;
                if (me == null || me.FloorCoordinate == null || !me.FloorCoordinate.IsValid)
                    return false;

                uint worldId = me.WorldId;
                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;

                if (_bipLastPlayerPositionTick <= 0 || _bipLastPlayerWorldId != worldId)
                {
                    _bipLastPlayerX = x;
                    _bipLastPlayerY = y;
                    _bipLastPlayerWorldId = worldId;
                    _bipLastPlayerPositionTick = tick;
                    return false;
                }

                float dx = x - _bipLastPlayerX;
                float dy = y - _bipLastPlayerY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                _bipLastPlayerX = x;
                _bipLastPlayerY = y;
                _bipLastPlayerWorldId = worldId;
                _bipLastPlayerPositionTick = tick;

                if (tick <= _bipAreaRebaseUntilTick || tick <= _bipReviveRebaseUntilTick)
                    return false;

                return dist >= BloodIsPowerBloodRushTeleportMinYards;
            }
            catch
            {
                return false;
            }
        }


        private void QueueBloodRushCandidate(int tick, int start, IPlayerSkill skill, bool handler, bool poll, bool teleport)
        {
            float expected = GetBloodIsPowerBloodRushExpectedCostPct(skill);
            if (expected <= 0f)
                return;

            foreach (var c in _bipBloodRushCandidates)
            {
                bool sameStart = start > 0 && start < int.MaxValue / 2 && c.StartTick == start;
                bool nearbyStartless = (start <= 0 || start >= int.MaxValue / 2) && c.StartTick <= 0 && Math.Abs(tick - c.Tick) <= BloodIsPowerBloodRushNoDuplicateTicks;

                if (!c.Completed && (sameStart || nearbyStartless))
                {
                    c.HandlerConfirmed = c.HandlerConfirmed || handler;
                    c.PollConfirmed = c.PollConfirmed || poll;
                    c.TeleportConfirmed = c.TeleportConfirmed || teleport;
                    return;
                }
            }

            float priorObserved = 0f;
            if (_bipLastBloodRushObservedLossTick > 0 &&
                _bipLastBloodRushObservedLossTick < tick &&
                tick - _bipLastBloodRushObservedLossTick <= BloodIsPowerBloodRushPairWindowTicks)
            {
                priorObserved = _bipLastBloodRushObservedLossPct;
            }

            _bipBloodRushCandidates.Add(new BloodRushCreditCandidate
            {
                Tick = tick,
                StartTick = start,
                ExpireTick = tick + BloodIsPowerBloodRushPairWindowTicks,
                ExpectedCostPct = expected,
                ObservedPct = priorObserved,
                HandlerConfirmed = handler,
                PollConfirmed = poll,
                TeleportConfirmed = teleport
            });
        }

        private float ResolveBloodRushCandidateCredit(int tick, float observedDeltaPct)
        {
            for (int i = _bipBloodRushCandidates.Count - 1; i >= 0; i--)
            {
                var c = _bipBloodRushCandidates[i];

                if (c.Completed)
                {
                    _bipBloodRushCandidates.RemoveAt(i);
                    continue;
                }

                if (_bipProgressPct >= 100f)
                {
                    c.Completed = true;
                    continue;
                }

                if (observedDeltaPct > 0f)
                    c.ObservedPct += observedDeltaPct;

                if (c.ObservedPct >= c.ExpectedCostPct - BloodIsPowerBloodRushCreditMinShortfallPct)
                {
                    c.Completed = true;
                    continue;
                }

                if (observedDeltaPct >= BloodIsPowerBloodRushMinObservedLossPct &&
                    c.ObservedPct < c.ExpectedCostPct)
                {
                    if (c.StartTick > 0 && c.StartTick < int.MaxValue / 2 && c.StartTick == _bipLastBloodRushCreditStartTick)
                    {
                        c.Completed = true;
                        continue;
                    }

                    if (tick - _bipLastBloodRushCreditTick < BloodIsPowerBloodRushNoDuplicateTicks)
                    {
                        c.Completed = true;
                        continue;
                    }

                    float shortfall = c.ExpectedCostPct - c.ObservedPct;
                    if (shortfall >= BloodIsPowerBloodRushCreditMinShortfallPct)
                    {
                        c.Credited = true;
                        c.Completed = true;
                        _bipLastBloodRushCreditTick = tick;

                        if (c.StartTick > 0 && c.StartTick < int.MaxValue / 2)
                            _bipLastBloodRushCreditStartTick = c.StartTick;

                        return shortfall;
                    }
                }

                // Health snapshots can miss Blood Rush completely. Only estimate zero-loss casts
                // from stronger handler/teleport evidence, never from poll-only cooldown noise.
                if (!c.Credited &&
                    c.ObservedPct <= 0f &&
                    !_bipLastBloodRushChanneling &&
                    (c.HandlerConfirmed || c.TeleportConfirmed) &&
                    tick - c.Tick >= BloodIsPowerBloodRushZeroLossDelayTicks)
                {
                    c.Credited = true;
                    c.Completed = true;
                    _bipLastBloodRushCreditTick = tick;

                    if (c.StartTick > 0 && c.StartTick < int.MaxValue / 2)
                        _bipLastBloodRushCreditStartTick = c.StartTick;

                    _bipBloodRushCandidates.RemoveAt(i);
                    return c.ExpectedCostPct;
                }

                if (tick > c.ExpireTick)
                {
                    _bipBloodRushCandidates.RemoveAt(i);
                    continue;
                }
            }

            return 0f;
        }


        private float UpdateBloodIsPowerBloodRushCredit(float observedDeltaPct)
        {
            _bipLastBloodRushChanneling = IsBloodIsPowerChannelingPylonActive();

            int tick = Hud.Game.CurrentGameTick;

            try
            {
                var skill = GetBloodIsPowerBloodRushSkill();
                if (skill == null)
                    return 0f;

                int start = 0;
                int finish = 0;
                try { start = skill.CooldownStartTick; } catch { }
                try { finish = skill.CooldownFinishTick; } catch { }

                bool startChanged =
                    _bipLastBloodRushStartTick > 0 &&
                    start > 0 &&
                    start != _bipLastBloodRushStartTick;

                _bipLastBloodRushStartTick = start;

                bool teleportEdge = UpdateBloodRushMovementValidator(tick);
                bool supportingSignal = teleportEdge || observedDeltaPct >= BloodIsPowerBloodRushMinObservedLossPct;

                if (startChanged && IsFreshBloodRushStart(tick, start, finish, supportingSignal))
                    QueueBloodRushCandidate(tick, start, skill, false, true, false);

                if (teleportEdge)
                    QueueBloodRushCandidate(tick, start, skill, false, false, true);

                if (observedDeltaPct >= BloodIsPowerBloodRushMinObservedLossPct)
                {
                    _bipLastBloodRushObservedLossTick = tick;
                    _bipLastBloodRushObservedLossPct = observedDeltaPct;
                }

                return ResolveBloodRushCandidateCredit(tick, observedDeltaPct);
            }
            catch
            {
                return 0f;
            }
        }


        

        

        private void CreateBloodIsPowerVisualResources()
        {
            int[,] colors =
            {
                { 235, 235, 235 }, // 0%: soft white
                { 245, 242, 205 },
                { 255, 238, 150 },
                { 255, 225, 90  },
                { 255, 205, 40  },
                { 255, 175, 35  },
                { 255, 140, 28  },
                { 255, 105, 24  },
                { 255, 65,  35  }, // 80%+: red family
                { 245, 35,  35  },
                { 230, 20,  20  },
            };

            _bipFonts = new IFont[BloodIsPowerColorSteps];
            _bipBarFillBrushes = new IBrush[BloodIsPowerColorSteps];

            for (int i = 0; i < BloodIsPowerColorSteps; i++)
            {
                int r = C(colors[i, 0]);
                int g = C(colors[i, 1]);
                int b = C(colors[i, 2]);
                _bipFonts[i] = Hud.Render.CreateFont("tahoma", 7.0f, 220, r, g, b, true, false, 185, 0, 0, 0, true);
                _bipBarFillBrushes[i] = Hud.Render.CreateBrush(230, 210, 45, 45, 0);
            }

            _bipBarBackBrush = Hud.Render.CreateBrush(130, 0, 0, 0, 0);
            _bipBarBorderBrush = Hud.Render.CreateBrush(225, 255, 210, 40, 1.5f);
        }

        private int GetBloodIsPowerColorIndex(float pct)
        {
            if (pct < 0f) pct = 0f;
            if (pct > 100f) pct = 100f;
            int idx = (int)Math.Floor((double)(pct / 10f));
            if (idx < 0) return 0;
            if (idx >= BloodIsPowerColorSteps) return BloodIsPowerColorSteps - 1;
            return idx;
        }

        private void SeedBloodIsPowerSkillState(IBuff buff)
        {
            if (_bipSkillStateSeeded)
                return;

            if (buff == null || buff.IconCounts == null)
                return;

            EnsureBloodIsPowerStateArrays();

            try
            {
                foreach (var skill in Hud.Game.Me.Powers.CurrentSkills)
                {
                    if (skill == null)
                        continue;

                    int i = GetBloodIsPowerSkillIndex(skill);
                    if (i < 0 || i >= buff.IconCounts.Length)
                        continue;

                    _bipSkillState[i] = buff.IconCounts[i];
                    try
                    {
                        _bipCooldownStartState[i] = skill.IsOnCooldown ? skill.CooldownStartTick : 0;
                        _bipCooldownFinishState[i] = skill.IsOnCooldown ? skill.CooldownFinishTick : 0;
                    }
                    catch
                    {
                        _bipCooldownStartState[i] = 0;
                        _bipCooldownFinishState[i] = 0;
                    }
                }
            }
            catch { }

            _bipSkillStateSeeded = true;
        }

        private void ResetBloodIsPowerTrackerForArea(bool newGame)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return;

                int tick = Hud.Game.CurrentGameTick;
                int playerIndex = Hud.Game.Me.Index;

                if (newGame)
                {
                    _bipLastPlayerIndex = playerIndex;
                    ResetBloodIsPowerTracker();
                    ResetBloodIsPowerGreaterRiftLifecycle();
                    return;
                }

                // FreeHUD can change Me.Index on normal GR floor transfers. Treat same-game area
                // changes as a short rebase window, not as a BIP reset. The progress snapshot is
                // kept visible while health/IconCounts are reseeded on the new floor.
                _bipLastPlayerIndex = playerIndex;
                _bipAreaSnapshotPct = _bipProgressPct;
                _bipAreaRebaseUntilTick = tick + BloodIsPowerAreaRebaseTicks;
                _bipLastHealth = 0f;
                _bipLastHealthMax = 0f;
                _bipSkillStateSeeded = false;
                _bipLastBloodRushCreditStartTick = 0;
                _bipLastBloodRushCreditTick = 0;
                _bipBloodRushCandidates.Clear();
                _bipLastPlayerWorldId = 0;
                _bipLastPlayerPositionTick = 0;
                _bipLastBloodRushObservedLossTick = 0;
                _bipLastBloodRushObservedLossPct = 0f;
                _bipLastFreshLossTick = 0;
                _bipLastFreshLossPct = 0f;
            }
            catch { }
        }

        private void ResetBloodIsPowerTracker()
        {
            _bipProgressPct = 0f;
            _bipEstimateUncertain = false;
            _bipLastHealth = 0f;
            _bipLastHealthMax = 0f;
            _bipSkillStateSeeded = false;
            _bipPendingProcStartTick = 0;
            _bipAreaRebaseUntilTick = 0;
            _bipAreaSnapshotPct = 0f;
            _bipWasDead = false;
            _bipReviveRebaseUntilTick = 0;
            _bipDeathSnapshotPct = 0f;
            _bipInactive = false;
            _bipInactiveReason = null;
            _bipLastBloodRushStartTick = 0;
            _bipLastConfirmedProcTick = 0;
            _bipLastBloodRushCreditStartTick = 0;
            _bipBloodRushCandidates.Clear();
            _bipLastBloodRushCreditTick = 0;
            _bipLastPlayerX = 0f;
            _bipLastPlayerY = 0f;
            _bipLastPlayerWorldId = 0;
            _bipLastPlayerPositionTick = 0;
            _bipLastBloodRushChanneling = false;
            _bipLastBloodRushObservedLossTick = 0;
            _bipLastBloodRushObservedLossPct = 0f;
            _bipLastFreshLossTick = 0;
            _bipLastFreshLossPct = 0f;

            EnsureBloodIsPowerStateArrays();
            for (int i = 0; i < _bipSkillState.Length; i++)
            {
                _bipSkillState[i] = 0;
                _bipCooldownStartState[i] = 0;
                _bipCooldownFinishState[i] = 0;
            }
        }

        private void ResetBloodIsPowerGreaterRiftLifecycle()
        {
            _bipLastInGreaterRift = false;
            _bipPendingGreaterRiftStartCheck = false;
            _bipGreaterRiftEntryTick = 0;
            _bipLastGreaterRiftPercent = -1.0d;
            _bipLastGreaterRiftStartResetTick = 0;
        }

        private void UpdateBloodIsPowerGreaterRiftLifecycle(int tick)
        {
            bool inGr = IsInGreaterRift();
            double percent = SafeRiftPercent();

            bool validPercent = percent >= 0.0d && percent <= 100.5d;
            bool nearStart = validPercent && percent <= BloodIsPowerNewGreaterRiftMaxPercent;

            bool justEnteredGr = inGr && !_bipLastInGreaterRift;
            bool justLeftGr = !inGr && _bipLastInGreaterRift;

            if (justEnteredGr)
            {
                _bipPendingGreaterRiftStartCheck = true;
                _bipGreaterRiftEntryTick = tick;
            }

            if (justLeftGr)
            {
                _bipPendingGreaterRiftStartCheck = false;
                _bipGreaterRiftEntryTick = 0;
                _bipLastGreaterRiftPercent = -1.0d;
            }

            bool pendingEntryReset =
                inGr &&
                _bipPendingGreaterRiftStartCheck &&
                nearStart &&
                tick <= _bipGreaterRiftEntryTick + BloodIsPowerGreaterRiftEntryCheckTicks;

            bool percentResetInsideGr =
                inGr &&
                nearStart &&
                _bipLastInGreaterRift &&
                _bipLastGreaterRiftPercent > 10.0d &&
                percent + 1.0d < _bipLastGreaterRiftPercent;

            if ((pendingEntryReset || percentResetInsideGr) &&
                tick > _bipLastGreaterRiftStartResetTick + BloodIsPowerGreaterRiftStartResetGuardTicks)
            {
                ResetBloodIsPowerTracker();

                _bipLastGreaterRiftStartResetTick = tick;
                _bipPendingGreaterRiftStartCheck = false;
                _bipGreaterRiftEntryTick = 0;
                _bipLastInGreaterRift = inGr;

                if (validPercent)
                    _bipLastGreaterRiftPercent = percent;

                return;
            }

            if (_bipPendingGreaterRiftStartCheck)
            {
                bool entryWindowExpired =
                    tick > _bipGreaterRiftEntryTick + BloodIsPowerGreaterRiftEntryCheckTicks;

                bool clearlyNotStart =
                    validPercent && percent > BloodIsPowerNewGreaterRiftMaxPercent;

                if (!inGr || entryWindowExpired || clearlyNotStart)
                {
                    _bipPendingGreaterRiftStartCheck = false;
                    _bipGreaterRiftEntryTick = 0;
                }
            }

            _bipLastInGreaterRift = inGr;

            if (validPercent)
                _bipLastGreaterRiftPercent = percent;
        }

        private float GetCurrentHealth(IPlayer me)
        {
            try { return me != null ? me.Defense.HealthCur : 0f; }
            catch { return 0f; }
        }

        private float GetCurrentMaxHealth(IPlayer me)
        {
            try { return me != null ? me.Defense.HealthMax : 0f; }
            catch { return 0f; }
        }

        private float GetBloodIsPowerDisplayPct()
        {
            try
            {
                IBuff buff = null;
                var me = Hud.Game.Me;
                if (me != null)
                    buff = me.Powers.GetBuff(BloodIsPowerSno);
                return GetBloodIsPowerDisplayPct(buff);
            }
            catch
            {
                if (_bipProgressPct < 0f) return 0f;
                if (_bipProgressPct > 100f) return 100f;
                return _bipProgressPct;
            }
        }

        private void PreserveBloodIsPowerDataUnavailable(string reason, int tick)
        {
            _bipLastHealth = 0f;
            _bipLastHealthMax = 0f;
            _bipSkillStateSeeded = false;
        }

        private void PreserveBloodIsPowerOnDeath(int tick)
        {
            if (!_bipWasDead)
            {
                _bipWasDead = true;
                _bipDeathSnapshotPct = GetBloodIsPowerDisplayPct();
            }

            // Death does not reset Blood is Power progress. Only clear volatile baselines that
            // can be invalid while dead; IconCounts/cooldown state will be reseeded on revive.
            _bipLastHealth = 0f;
            _bipLastHealthMax = 0f;
            _bipSkillStateSeeded = false;
        }

        private void RebaseBloodIsPowerAfterRevive(IPlayer me, int tick)
        {
            _bipWasDead = false;
            _bipReviveRebaseUntilTick = tick + BloodIsPowerReviveRebaseTicks;

            _bipLastHealth = GetCurrentHealth(me);
            _bipLastHealthMax = GetCurrentMaxHealth(me);
            _bipSkillStateSeeded = false;
            _bipLastBloodRushCreditStartTick = 0;
            _bipLastBloodRushCreditTick = 0;
            _bipBloodRushCandidates.Clear();
            _bipLastPlayerWorldId = 0;
            _bipLastPlayerPositionTick = 0;
            _bipLastBloodRushObservedLossTick = 0;
            _bipLastBloodRushObservedLossPct = 0f;
            _bipLastFreshLossTick = 0;
            _bipLastFreshLossPct = 0f;
        }

        private void SetBloodIsPowerInactive(string reason, int tick)
        {
            if (!_bipInactive || _bipInactiveReason != reason)
            {
                ResetBloodIsPowerTracker();
                _bipInactive = true;
                _bipInactiveReason = reason;
            }
            else
            {
            }
        }

        private float AddBloodIsPowerHealthLoss(float cur, float max)
        {
            if (cur <= 0f || max <= 0f)
                return 0f;

            if (_bipLastHealth <= 0f || _bipLastHealthMax <= 0f)
                return 0f;

            float denom = _bipLastHealthMax > 0f ? _bipLastHealthMax : max;
            if (denom <= 0f)
                return 0f;

            float diff = _bipLastHealth - cur;
            if (diff <= 0f)
                return 0f;

            float deltaPct = (diff / denom) * 100f;
            if (deltaPct <= 0f || deltaPct > BloodIsPowerMaxSaneLossPct)
                return 0f;

            // Blood is Power can hold one pending global trigger. Once the
            // estimated threshold is reached, additional loss cannot represent
            // a second queued proc, so keep the estimate saturated at 100%.
            _bipProgressPct = Math.Min(100f, Math.Max(0f, _bipProgressPct + deltaPct));

            return deltaPct;
        }

        private bool HasRecentBloodIsPowerFreshLoss(int tick)
        {
            if (_bipLastFreshLossTick <= 0)
                return false;

            if (_bipLastFreshLossPct < BloodIsPowerFreshLossDropConfirmFloorPct)
                return false;

            return tick - _bipLastFreshLossTick <= BloodIsPowerFreshLossDropConfirmTicks;
        }

        private void ResolveBloodIsPowerReceived(int tick, float cur, float max)
        {
            _bipProgressPct = 0f;
            _bipEstimateUncertain = false;
            _bipPendingProcStartTick = 0;
            _bipLastConfirmedProcTick = tick;

            _bipLastHealth = cur;
            _bipLastHealthMax = max;

            _bipBloodRushCandidates.Clear();
        }


        private void ResolveBloodIsPowerOverflow(bool pendingCooldown, int tick)
        {
            if (_bipProgressPct < 100f)
            {
                _bipPendingProcStartTick = 0;
                return;
            }

            // Crossing the estimated threshold is not proof that Blood is Power
            // fired. Preserve the estimate until native IconCounts or cooldown-drop
            // data confirms the proc. The renderer marks the saturated estimate
            // with "?" while a tracked cooldown awaits that confirmation.
            if (pendingCooldown && _bipPendingProcStartTick <= 0)
                _bipPendingProcStartTick = tick;
        }

        private bool IsBloodIsPowerCombatEstimateUncertain(IPlayer me, float observedLossPct)
        {
            if (me == null || observedLossPct < BloodIsPowerFreshLossDropConfirmFloorPct)
                return false;

            try
            {
                // Outside town, a sampled health drop can overlap monster damage,
                // healing, regeneration, shields, and server corrections. FreeHUD
                // exposes only rolling combat rates, not the exact gross loss event
                // stream used by Blood is Power, so treat the number as approximate.
                if (Hud != null && Hud.Game != null && !Hud.Game.IsInTown)
                    return true;

                return me.Defense != null &&
                    me.Defense.CurrentDamageTakenPerSecond > 0.0d;
            }
            catch
            {
                return false;
            }
        }

        // Blood is Power passive/cooldown state reference based on RNN's BloodIsPowerPlugin.
        // TipsHelper tracks health-loss progress globally, then lets native signals validate procs.
        // Low-estimate cooldown drops are accepted only when paired with tracked IconCounts
        // or recent health loss, which covers damage-triggered BIP procs.
        private void CleanupBloodIsPowerTransientSnapshots(int tick)
        {
            if (!_bipWasDead && _bipDeathSnapshotPct > 0f && tick > _bipReviveRebaseUntilTick)
                _bipDeathSnapshotPct = 0f;

            if (_bipAreaSnapshotPct > 0f && tick > _bipAreaRebaseUntilTick)
                _bipAreaSnapshotPct = 0f;
        }

        private BloodIsPowerProcSignals UpdateBloodIsPowerSkillState(IBuff buff, int tick)
        {
            var result = new BloodIsPowerProcSignals();

            if (buff == null || buff.IconCounts == null)
                return result;

            EnsureBloodIsPowerStateArrays();

            var allIconEdges = new List<string>(4);
            var trackedIconEdges = new List<string>(2);
            var nonTrackedIconEdges = new List<string>(4);

            try
            {
                foreach (var skill in Hud.Game.Me.Powers.CurrentSkills)
                {
                    if (skill == null)
                        continue;

                    int i = GetBloodIsPowerSkillIndex(skill);
                    if (i < 0 || i >= buff.IconCounts.Length)
                        continue;

                    bool tracked = IsBloodIsPowerTrackedSkill(skill);
                    string skillKey = "k" + skill.Key;

                    int n = buff.IconCounts[i];
                    int previousIcon = _bipSkillState[i];

                    bool iconEdge = false;
                    if (n != previousIcon)
                    {
                        iconEdge = (n == 1 && previousIcon != 1) || (n > previousIcon && n > 0);

                        if (iconEdge)
                        {
                            allIconEdges.Add(skillKey);

                            if (tracked)
                                trackedIconEdges.Add(skillKey);
                            else
                                nonTrackedIconEdges.Add(skillKey);

                            if (string.IsNullOrEmpty(result.IconEdgeKey))
                                result.IconEdgeKey = skillKey;
                        }

                        _bipSkillState[i] = n;
                    }

                    bool onCooldown = false;
                    int start = 0;
                    int finish = 0;

                    try
                    {
                        onCooldown = skill.IsOnCooldown;
                        if (onCooldown)
                        {
                            start = skill.CooldownStartTick;
                            finish = skill.CooldownFinishTick;
                        }
                    }
                    catch { }

                    if (tracked && onCooldown && n != 1)
                        result.PendingDisplay = true;

                    if (onCooldown && n != 1 && tick - start > BloodIsPowerCooldownSettleTicks)
                        result.PendingProc = true;

                    int previousFinish = _bipCooldownFinishState[i];
                    int drop = previousFinish > 0 && finish > 0 ? previousFinish - finish : 0;

                    if (drop > result.MaxAnyDropTicks)
                        result.MaxAnyDropTicks = drop;

                    if (tracked && drop > result.MaxTrackedDropTicks)
                    {
                        result.MaxTrackedDropTicks = drop;
                        result.DropSkillKey = skillKey;
                    }

                    _bipCooldownStartState[i] = onCooldown ? start : 0;
                    _bipCooldownFinishState[i] = onCooldown ? finish : 0;
                }
            }
            catch { }

            result.AllIconEdgeKeys = string.Join(",", allIconEdges.ToArray());
            result.TrackedIconEdgeKey = string.Join(",", trackedIconEdges.ToArray());
            result.NonTrackedIconEdgeKey = string.Join(",", nonTrackedIconEdges.ToArray());

            result.TrackedIconEdge = trackedIconEdges.Count > 0;
            result.NonTrackedIconEdge = nonTrackedIconEdges.Count > 0;

            bool pending100 = _bipPendingProcStartTick > 0 || _bipProgressPct >= 100f;
            bool strongTrackedDrop = result.MaxTrackedDropTicks >= BloodIsPowerStrongCooldownDropTicks;
            bool freshLossDropConfirm = strongTrackedDrop && HasRecentBloodIsPowerFreshLoss(tick);

            bool trackedHighEstimate = _bipProgressPct >= BloodIsPowerTrackedIconConfirmFloorPct || pending100;

            if (result.TrackedIconEdge || result.NonTrackedIconEdge)
            {
                // This is Blood is Power's own per-action-slot IconCounts array.
                // A positive edge on any equipped skill is native confirmation that
                // the current global BIP proc was applied to at least one cooldown.
                result.ReceivedAny = true;
                result.CooldownDropConfirmed = strongTrackedDrop;
                result.ProcReason = result.TrackedIconEdge
                    ? (strongTrackedDrop ? "tracked-icon+tracked-drop" : "tracked-icon-native")
                    : "skill-icon-native";
            }

            if (!result.ReceivedAny && strongTrackedDrop)
            {
                if (trackedHighEstimate || freshLossDropConfirm)
                {
                    result.CooldownDropConfirmed = true;
                    result.ProcReason = freshLossDropConfirm && !trackedHighEstimate
                        ? "tracked-drop+fresh-loss"
                        : "tracked-drop";
                }
                else
                {
                    result.IgnoredLowCooldownDrop = true;
                    if (string.IsNullOrEmpty(result.ProcReason))
                        result.ProcReason = "ignored-low-drop";
                }
            }

            return result;
        }

        private void UpdateBloodIsPowerTracker()
        {
            if (!ShowBloodIsPowerTracker)
                return;

            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            int tick = Hud.Game.CurrentGameTick;
            var me = Hud.Game.Me;
            if (me == null)
            {
                PreserveBloodIsPowerDataUnavailable("player-null-preserve", tick);
                return;
            }

            if (!IsNecromancer())
            {
                SetBloodIsPowerInactive("not-necromancer", tick);
                return;
            }

            UpdateBloodIsPowerGreaterRiftLifecycle(tick);

            // Passive/power data can be stale while dead. Preserve first; do not wipe BIP.
            if (me.IsDead)
            {
                PreserveBloodIsPowerOnDeath(tick);
                return;
            }

            bool justRevived = _bipWasDead;

            if (!HasBloodIsPowerPassive())
            {
                if (justRevived && _bipReviveRebaseUntilTick <= 0)
                    _bipReviveRebaseUntilTick = tick + BloodIsPowerReviveRebaseTicks;

                if (tick <= _bipReviveRebaseUntilTick)
                {
                    PreserveBloodIsPowerDataUnavailable("passive-wait-after-revive", tick);
                    return;
                }

                SetBloodIsPowerInactive("passive-missing", tick);
                return;
            }

            if (_bipInactive)
            {
                _bipInactive = false;
                _bipInactiveReason = null;
                _bipSkillStateSeeded = false;
                _bipLastHealth = 0f;
                _bipLastHealthMax = 0f;
            }

            if (justRevived)
            {
                RebaseBloodIsPowerAfterRevive(me, tick);
                return;
            }

            IBuff buff = null;
            try { buff = me.Powers.GetBuff(BloodIsPowerSno); } catch { }
            if (buff == null || buff.IconCounts == null)
            {
                PreserveBloodIsPowerDataUnavailable("buff-null-preserve", tick);
                return;
            }

            EnsureBloodIsPowerStateArrays();
            SeedBloodIsPowerSkillState(buff);

            float cur = GetCurrentHealth(me);
            float max = GetCurrentMaxHealth(me);
            if (max <= 0f || cur <= 0f)
            {
                PreserveBloodIsPowerDataUnavailable("health-invalid-preserve", tick);
                return;
            }

            if (tick <= _bipReviveRebaseUntilTick)
            {
                _bipLastHealth = cur;
                _bipLastHealthMax = max;
                SeedBloodIsPowerSkillState(buff);
                return;
            }

            // Blood is Power uses one global cumulative life-loss counter. Life
            // lost while LoTD/Simulacrum are inactive or already consumed still
            // contributes to the next global proc; per-skill IconCounts only decide
            // which currently running cooldowns receive that proc.
            bool trackingEligible =
                HasAnyUnconsumedBloodIsPowerDisplayCooldown(buff);

            float deltaPct = AddBloodIsPowerHealthLoss(cur, max);

            // Enemy damage is sampled as net health movement. Healing and rapid
            // server updates can hide or distort part of that loss, so retain the
            // estimate but mark it as approximate until a native BIP proc resets it.
            if (IsBloodIsPowerCombatEstimateUncertain(me, deltaPct))
                _bipEstimateUncertain = true;

            float bloodRushCreditPct =
                UpdateBloodIsPowerBloodRushCredit(deltaPct);
            if (bloodRushCreditPct > 0f)
            {
                _bipProgressPct = Math.Min(100f, _bipProgressPct + bloodRushCreditPct);
                deltaPct += bloodRushCreditPct;
            }

            if (deltaPct >=
                BloodIsPowerFreshLossDropConfirmFloorPct)
            {
                _bipLastFreshLossTick = tick;
                _bipLastFreshLossPct = deltaPct;
            }

            var bipSignals = UpdateBloodIsPowerSkillState(buff, tick);

            if (bipSignals.ReceivedAny ||
                bipSignals.CooldownDropConfirmed)
            {
                ResolveBloodIsPowerReceived(tick, cur, max);
            }
            else if (trackingEligible)
            {
                ResolveBloodIsPowerOverflow(
                    bipSignals.PendingDisplay,
                    tick);
            }

            CleanupBloodIsPowerTransientSnapshots(tick);

            _bipLastHealth = cur;
            _bipLastHealthMax = max;

            if (_bipAreaRebaseUntilTick > 0 && tick > _bipAreaRebaseUntilTick)
            {
                _bipAreaRebaseUntilTick = 0;
                _bipAreaSnapshotPct = 0f;
            }

            if (_bipReviveRebaseUntilTick > 0 && tick > _bipReviveRebaseUntilTick)
                _bipReviveRebaseUntilTick = 0;
        }

        private float GetBloodIsPowerDisplayPct(IBuff buff)
        {
            float pct = _bipProgressPct;
            if (pct >= 100f && HasAnyUnconsumedBloodIsPowerDisplayCooldown(buff))
                return 100f;

            if (pct < 0f) return 0f;
            if (pct > 100f) return 100f;
            return pct;
        }

        private bool IsBloodIsPowerPendingUnknown(IBuff buff)
        {
            return _bipPendingProcStartTick > 0 &&
                _bipProgressPct >= 100f &&
                HasAnyUnconsumedBloodIsPowerDisplayCooldown(buff);
        }

        private static string GetBloodIsPowerDisplayText(float pct, bool uncertain)
        {
            if (pct > 0f && pct < 1f)
                return uncertain ? "<1%?" : "<1%";

            int rounded = (int)Math.Round((double)pct);
            if (rounded < 1) rounded = 1;
            if (rounded > 100) rounded = 100;
            return rounded.ToString() + "%" + (uncertain ? "?" : string.Empty);
        }

        private void DrawBloodIsPowerTracker()
        {
            if (!ShowBloodIsPowerTracker)
                return;

            if (_bipFonts == null || _bipBarBackBrush == null || _bipBarFillBrushes == null || _bipBarBorderBrush == null)
                return;

            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            if (!IsNecromancer())
                return;

            int tick = Hud.Game.CurrentGameTick;
            if (!HasBloodIsPowerPassive())
                return;

            var trackedSkills = GetBloodIsPowerTrackedSkills();
            if (trackedSkills.Count == 0)
                return;

            IBuff buff = null;
            try { buff = Hud.Game.Me.Powers.GetBuff(BloodIsPowerSno); } catch { }

            bool preserveVisual =
                _bipWasDead ||
                tick <= _bipReviveRebaseUntilTick ||
                tick <= _bipAreaRebaseUntilTick;

            if (buff == null || buff.IconCounts == null)
            {
                if (!preserveVisual)
                    return;
            }

            bool pendingUnknown = !preserveVisual &&
                IsBloodIsPowerPendingUnknown(buff);

            bool estimateUncertain = pendingUnknown || _bipEstimateUncertain;

            float pct = preserveVisual
                ? Math.Max(0f, Math.Min(100f, _bipProgressPct))
                : GetBloodIsPowerDisplayPct(buff);

            // A fresh cooldown with no observed life loss is not an active
            // measurement. Do not present an authoritative-looking 0%.
            if (!pendingUnknown && pct <= 0f)
                return;

            foreach (var skill in trackedSkills)
                DrawBloodIsPowerTrackerOnSkill(
                    skill,
                    buff,
                    pct,
                    preserveVisual,
                    pendingUnknown,
                    estimateUncertain);
        }

        private void DrawBloodIsPowerTrackerOnSkill(
            IPlayerSkill skill,
            IBuff buff,
            float pct,
            bool preserveVisual,
            bool pendingUnknown,
            bool estimateUncertain)
        {
            if (skill == null)
                return;

            bool onCooldown = false;
            try { onCooldown = skill.IsOnCooldown; } catch { }

            if (!onCooldown)
                return;

            int tick = 0;
            try { tick = Hud.Game.CurrentGameTick; } catch { }

            if (!preserveVisual &&
                _bipLastConfirmedProcTick > 0 &&
                tick <= _bipLastConfirmedProcTick + BloodIsPowerPostProcVisualSuppressTicks)
            {
                if (buff == null || buff.IconCounts == null)
                    return;

                int procIdx = 1 + (int)skill.Key;
                if (procIdx >= 0 && procIdx < buff.IconCounts.Length && buff.IconCounts[procIdx] == 1)
                    return;
            }

            if (!preserveVisual)
            {
                if (buff == null || buff.IconCounts == null)
                    return;

                int idx = 1 + (int)skill.Key;
                if (idx < 0 || idx >= buff.IconCounts.Length)
                    return;

                if (buff.IconCounts[idx] == 1)
                    return;
            }

            IUiElement ui = null;
            try { ui = Hud.Render.GetPlayerSkillUiElement(skill.Key); } catch { }
            if (ui == null || ui.Rectangle.Width <= 0f || ui.Rectangle.Height <= 0f)
                return;

            var r = ui.Rectangle;

            float visualPct = pendingUnknown
                ? 99f
                : Math.Max(0f, Math.Min(99f, pct));

            int colorIdx = pendingUnknown
                ? BloodIsPowerColorSteps - 1
                : GetBloodIsPowerColorIndex(visualPct);

            IBrush fillBrush = colorIdx >= 0 && colorIdx < _bipBarFillBrushes.Length ? _bipBarFillBrushes[colorIdx] : null;
            IFont font = colorIdx >= 0 && colorIdx < _bipFonts.Length ? _bipFonts[colorIdx] : null;
            if (fillBrush == null || font == null)
                return;

            float barW = r.Width * 0.88f;
            float barH = Math.Max(7f, r.Height * 0.1875f);
            float barX = r.X + (r.Width - barW) * 0.5f;
            float barY = r.Y + r.Height - barH - 1.5f;

            _bipBarBackBrush.DrawRectangle(barX, barY, barW, barH);
            if (visualPct > 0f)
                fillBrush.DrawRectangle(barX, barY, barW * (visualPct / 100f), barH);
            _bipBarBorderBrush.DrawRectangle(barX, barY, barW, barH);

            string text = GetBloodIsPowerDisplayText(
                pendingUnknown ? 100f : visualPct,
                estimateUncertain);
            var layout = font.GetTextLayout(text);

            float tx = r.X + Math.Max(2f, r.Width * 0.05f);
            float ty = r.Y + Math.Max(1f, r.Height * 0.04f);

            font.DrawText(layout, tx, ty);
        }

        private void PaintPickupDots()
        {
            if (ShowHealthGlobeDots && HealthGlobeBrush != null)
                foreach (var actor in Hud.Game.Actors.Where(IsHealthGlobe))
                    HealthGlobeBrush.DrawWorldEllipse(HealthGlobeDotRadius, -1, actor.FloorCoordinate);

            if (ShowRiftProgressOrbDots && RiftOrbBrush != null)
                foreach (var actor in Hud.Game.Actors.Where(IsRiftOrb))
                    RiftOrbBrush.DrawWorldEllipse(RiftOrbDotRadius, -1, actor.FloorCoordinate);
        }

        private void PaintItemGroundCircles()
        {
            if (!ShowItemGroundCircles)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank))
                    continue;

                if (marker.Rank == PrimalRank)
                    DrawPulsingGroundEllipse(marker, PrimalGroundEllipseRadiusX, PrimalGroundEllipseRadiusY, PrimalRingBrush, PrimalRingOutlineBrush);
                else
                    DrawPulsingGroundEllipse(marker, AncientGroundEllipseRadiusX, AncientGroundEllipseRadiusY, AncientRingBrush, AncientRingOutlineBrush);
            }
        }

        private void DrawPulsingGroundEllipse(ItemMarker marker, float radiusX, float radiusY, IBrush brush, IBrush outline)
        {
            if (marker == null || brush == null)
                return;

            var sc = Hud.Window.WorldToScreenCoordinate(marker.X, marker.Y, marker.Z, true, true);
            if (!IsValid(sc))
                return;

            var pulse = 1.0f + ((float)Math.Sin((Hud.Game.CurrentRealTimeMilliseconds % Math.Max(1, ItemGroundCirclePulseMs)) * Math.PI * 2.0d / Math.Max(1, ItemGroundCirclePulseMs)) * ItemGroundCirclePulseScale);
            var rx = Math.Max(2.0f, radiusX * pulse);
            var ry = Math.Max(2.0f, radiusY * pulse);
            if (outline != null)
                outline.DrawEllipse(sc.X, sc.Y, rx, ry);
            brush.DrawEllipse(sc.X, sc.Y, rx, ry);
        }

        private void PaintValleyOfDeathCircles()
        {
            if (!ShowValleyOfDeathCircle || Hud.Game == null || Hud.Game.Actors == null || ValleyOfDeathBrush == null || ValleyOfDeathOutlineBrush == null)
                return;

            foreach (var actor in Hud.Game.Actors)
            {
                if (actor == null || actor.SnoActor == null || actor.FloorCoordinate == null) continue;
                if (actor.SnoActor.Sno != ValleyOfDeathActorSno) continue;

                float seconds = Math.Max(1.0f, ValleyOfDeathTimerSeconds);
                if (Hud.Game.CurrentGameTick > actor.CreatedAtInGameTick + (int)Math.Round(seconds * 60.0f) + 30)
                    continue;

                ValleyOfDeathOutlineBrush.DrawWorldEllipse(Math.Max(1.0f, ValleyOfDeathRadiusYards), -1, actor.FloorCoordinate);
                ValleyOfDeathBrush.DrawWorldEllipse(Math.Max(1.0f, ValleyOfDeathRadiusYards), -1, actor.FloorCoordinate);
                PaintValleyOfDeathTimer(actor, seconds);
            }
        }

        private void PaintValleyOfDeathTimer(IActor actor, float seconds)
        {
            if (actor == null || actor.FloorCoordinate == null)
                return;

            EnsureValleyOfDeathDecorators(seconds);
            if (_valleyOfDeathTimer == null || _valleyOfDeathLabel == null)
                return;

            _valleyOfDeathTimer.CountDownFrom = seconds;
            _valleyOfDeathLabel.CountDownFrom = seconds;
            _valleyOfDeathTimer.Paint(actor, actor.FloorCoordinate, null);
            _valleyOfDeathLabel.Paint(actor, actor.FloorCoordinate, null);
        }

        private void EnsureValleyOfDeathDecorators(float seconds)
        {
            if (_valleyOfDeathTimer == null)
            {
                _valleyOfDeathTimer = new GroundTimerDecorator(Hud)
                {
                    Radius = 30f,
                    CountDownFrom = seconds,
                    StepCount = 5,
                    BackgroundBrushEmpty = Hud.Render.CreateBrush(55, 18, 20, 22, 0),
                    BackgroundBrushFill = Hud.Render.CreateBrush(90, 64, 64, 64, 0),
                    BorderBrush = Hud.Render.CreateBrush(120, 0, 0, 0, 1.4f),
                };
            }

            if (_valleyOfDeathLabel == null)
            {
                _valleyOfDeathLabel = new GroundLabelDecorator(Hud)
                {
                    CenterBaseLine = true,
                    ForceOnScreen = false,
                    CountDownFrom = seconds,
                };
            }

            string sig = C(ValleyOfDeathColorR).ToString() + ":" + C(ValleyOfDeathColorG).ToString() + ":" + C(ValleyOfDeathColorB).ToString();
            if (_valleyOfDeathFont == null || _valleyOfDeathFontSignature != sig)
            {
                _valleyOfDeathFont = Hud.Render.CreateFont("tahoma", 9f, 185, C(ValleyOfDeathColorR), C(ValleyOfDeathColorG), C(ValleyOfDeathColorB), true, false, 120, 0, 0, 0, true);
                _valleyOfDeathFontSignature = sig;
            }
            _valleyOfDeathLabel.TextFont = _valleyOfDeathFont;
        }

        private void PaintItemMinimapMarkers()
        {
            if (!ShowItemMinimapMarkers || _trianglePainter == null)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank))
                    continue;

                var radius = ((marker.Rank == PrimalRank) ? PrimalMinimapTriangleRadius : AncientMinimapTriangleRadius) * Hud.Render.MinimapScale;
                if (_mapPulse != null)
                    radius = _mapPulse.TransformRadius(radius);

                float x;
                float y;
                Hud.Render.GetMinimapCoordinates(marker.X, marker.Y, out x, out y);
                if (ClampItemMinimapMarkers)
                    ClampToMinimap(ref x, ref y, radius + 2.0f);

                _trianglePainter.Paint(x, y, radius, marker.Rank == PrimalRank ? PrimalMapBrush : AncientMapBrush, marker.Rank == PrimalRank ? PrimalMapOutlineBrush : AncientMapOutlineBrush);
            }
        }

        private void PaintItemScreenEdgeArrows()
        {
            if (!ShowItemScreenEdgeArrows)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank) || !IsRankArrowEnabled(marker.Rank))
                    continue;

                float x;
                float y;
                float angle;
                if (TryGetScreenEdgeArrow(marker, out x, out y, out angle))
                    DrawOutlineTriangle(x, y, ItemScreenEdgeArrowRadius, angle, marker.Rank == PrimalRank ? PrimalScreenArrowBrush : AncientScreenArrowBrush, marker.Rank == PrimalRank ? PrimalScreenArrowOutlineBrush : AncientScreenArrowOutlineBrush);
            }
        }

        private bool TryGetScreenEdgeArrow(ItemMarker marker, out float arrowX, out float arrowY, out float angle)
        {
            arrowX = 0;
            arrowY = 0;
            angle = 0;

            var size = Hud.Window.Size;
            var width = (float)size.Width;
            var height = (float)size.Height;
            if (width <= 100 || height <= 100)
                return false;

            var sc = Hud.Window.WorldToScreenCoordinate(marker.X, marker.Y, marker.Z, true, true);
            if (!IsValid(sc))
                return false;

            var interior = ItemScreenEdgeInteriorMargin;
            var margin = Math.Max(ItemScreenEdgeArrowMargin, ItemScreenEdgeArrowRadius + 4.0f);
            var bottomLimit = GetBottomArrowLimit(height, margin);
            if (sc.X >= interior && sc.X <= width - interior && sc.Y >= interior && sc.Y <= bottomLimit - interior)
                return false;

            var cx = width * 0.5f;
            var cy = height * 0.5f;
            var dx = sc.X - cx;
            var dy = sc.Y - cy;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
                return false;

            var scale = float.MaxValue;
            if (dx > 0) scale = Math.Min(scale, (width - margin - cx) / dx);
            else if (dx < 0) scale = Math.Min(scale, (margin - cx) / dx);
            if (dy > 0) scale = Math.Min(scale, (bottomLimit - cy) / dy);
            else if (dy < 0) scale = Math.Min(scale, (margin - cy) / dy);
            if (scale == float.MaxValue || scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
                return false;

            arrowX = Clamp(cx + (dx * scale), margin, width - margin);
            arrowY = Clamp(cy + (dy * scale), margin, bottomLimit);
            angle = (float)Math.Atan2(dy, dx);
            return true;
        }

        private float GetBottomArrowLimit(float height, float margin)
        {
            var reserved = Math.Max(Math.Max(0.0f, ItemScreenEdgeArrowBottomUiReservedPixels), height * Clamp(ItemScreenEdgeArrowBottomUiReservedFrac, 0.0f, 0.45f));
            return Clamp(height - reserved - Math.Max(0.0f, ItemScreenEdgeArrowRadius * 0.5f), margin, height - margin);
        }

        private void PaintItemAlerts()
        {
            if ((!ShowItemAlertText && !ShowItemAlertDirectionArrow) || ItemAlertTextMaxLines <= 0)
                return;

            var alerts = GetActiveAlerts();
            if (alerts.Count == 0)
                return;

            if (ShowItemAlertTextAbovePlayer && alerts[0].Sequence >= _latestAbovePlayerSequence)
            {
                _latestAbovePlayerSequence = alerts[0].Sequence;
                DrawAbovePlayerAlert(alerts[0]);
            }

            if (ShowItemAlertText && ShowItemAlertTextNearMinimap)
                DrawMinimapAlerts(alerts.Where(a => IsRankTextEnabled(a.Marker.Rank)).ToList());
        }

        private List<ItemAlert> GetActiveAlerts()
        {
            var now = Hud.Game.CurrentRealTimeMilliseconds;
            var result = new List<ItemAlert>();

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank) || !IsRankNotificationEnabled(marker.Rank))
                    continue;

                var elapsed = now - marker.FirstSeenMs;
                var alpha = GetAlertAlphaBucket(marker.Rank, elapsed);
                if (alpha <= 0)
                    continue;

                result.Add(new ItemAlert
                {
                    Marker = marker,
                    Text = BuildAlertText(marker.Name, marker.Label),
                    ElapsedMs = elapsed,
                    Sequence = marker.Sequence,
                    AlphaBucket = alpha,
                    Font = GetAlertFont(marker.Rank, elapsed, alpha, false, false),
                    OutlineFont = GetAlertFont(marker.Rank, elapsed, alpha, true, false),
                    ArrowBrush = marker.Rank == PrimalRank ? PrimalAlertArrowBrush : AncientAlertArrowBrush,
                    ArrowOutlineBrush = marker.Rank == PrimalRank ? PrimalAlertArrowOutlineBrush : AncientAlertArrowOutlineBrush,
                    ArrowHighlightBrush = marker.Rank == PrimalRank ? PrimalAlertArrowHighlightBrush : AncientAlertArrowHighlightBrush
                });
            }

            return result.OrderByDescending(a => a.Sequence).ThenByDescending(a => a.Marker.FirstSeenMs).Take(ItemAlertTextMaxLines).ToList();
        }

        private IFont GetAlertFont(int rank, long elapsed, int alphaBucket, bool outline, bool minimap)
        {
            if (elapsed >= 0 && elapsed <= ItemAlertTextPopMs)
            {
                var pop = outline
                    ? (minimap
                        ? (rank == PrimalRank ? PrimalMinimapOutlinePopFonts : AncientMinimapOutlinePopFonts)
                        : (rank == PrimalRank ? PrimalOutlinePopFonts : AncientOutlinePopFonts))
                    : (minimap
                        ? (rank == PrimalRank ? PrimalMinimapPopFonts : AncientMinimapPopFonts)
                        : (rank == PrimalRank ? PrimalPopFonts : AncientPopFonts));
                if (pop != null && pop.Length > 0)
                    return pop[GetPopFontIndex(elapsed, pop.Length)];
            }

            var fade = outline
                ? (minimap
                    ? (rank == PrimalRank ? PrimalMinimapOutlineFadeFonts : AncientMinimapOutlineFadeFonts)
                    : (rank == PrimalRank ? PrimalOutlineFadeFonts : AncientOutlineFadeFonts))
                : (minimap
                    ? (rank == PrimalRank ? PrimalMinimapFadeFonts : AncientMinimapFadeFonts)
                    : (rank == PrimalRank ? PrimalFadeFonts : AncientFadeFonts));
            if (fade == null || alphaBucket < 0 || alphaBucket >= fade.Length)
                return null;
            return fade[alphaBucket];
        }

        private void DrawAbovePlayerAlert(ItemAlert alert)
        {
            if (alert == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return;

            var sc = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
            if (!IsValid(sc))
                return;

            var y = sc.Y + Lerp(ItemAlertPlayerStartYOffset, ItemAlertPlayerSettledYOffset, GetTravelProgress(alert.ElapsedMs));
            DrawAlertLine(alert, sc.X + ItemAlertPlayerXOffset, y, true, true);
        }

        private void DrawMinimapAlerts(List<ItemAlert> alerts)
        {
            if (alerts == null || alerts.Count == 0)
                return;

            if (Hud.Render.MinimapUiElement == null || !Hud.Render.MinimapUiElement.Visible)
                return;

            var rect = Hud.Render.MinimapUiElement.Rectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var startY = rect.Y + (rect.Height * ItemAlertMinimapStartYFrac);
            var settledY = rect.Y + Math.Max(ItemAlertMinimapSettledYOffset, rect.Height * Clamp(ItemAlertMinimapSettledYFrac, 0.0f, 1.0f));
            var y = Lerp(startY, settledY, GetTravelProgress(alerts[0].ElapsedMs));
            var x = rect.X + (rect.Width * 0.5f) + ItemAlertMinimapXOffset;

            foreach (var alert in alerts)
            {
                y += DrawAlertLine(alert, x, y, true, false, true);
            }
        }

        private float DrawAlertLine(ItemAlert alert, float centerX, float y, bool centered, bool withDirectionArrow, bool minimapText = false)
        {
            if (alert == null)
                return ItemAlertTextLineHeight;

            var font = minimapText ? GetAlertFont(alert.Marker.Rank, alert.ElapsedMs, alert.AlphaBucket, false, true) : alert.Font;
            var outlineFont = minimapText ? GetAlertFont(alert.Marker.Rank, alert.ElapsedMs, alert.AlphaBucket, true, true) : alert.OutlineFont;
            var canDrawText = ShowItemAlertText && IsRankTextEnabled(alert.Marker.Rank) && font != null && !string.IsNullOrEmpty(alert.Text);
            float textCenterX = centerX;
            float textHeight = Math.Max(12.0f, ItemAlertTextLineHeight);

            if (canDrawText)
            {
                var layout = font.GetTextLayout(alert.Text);
                var x = centered ? centerX - (layout.Metrics.Width * 0.5f) : centerX;
                textCenterX = x + (layout.Metrics.Width * 0.5f);
                textHeight = Math.Max(textHeight, layout.Metrics.Height);
                DrawOutlinedText(alert.Text, x, y, font, outlineFont);
            }

            var lineHeight = Math.Max(ItemAlertTextLineHeight, textHeight);
            if (withDirectionArrow && IsRankArrowEnabled(alert.Marker.Rank))
            {
                DrawAlertDirectionArrow(alert, textCenterX, y + textHeight + ItemAlertArrowYOffset);
                lineHeight += ItemAlertArrowYOffset + Math.Max(12.0f, ItemAlertArrowHeadSize * 2.0f);
            }

            return lineHeight;
        }

        private void DrawAlertDirectionArrow(ItemAlert alert, float x, float y)
        {
            if (!ShowItemAlertDirectionArrow || alert == null || alert.ArrowBrush == null || !IsRankArrowEnabled(alert.Marker.Rank))
                return;

            var sc = Hud.Window.WorldToScreenCoordinate(alert.Marker.X, alert.Marker.Y, alert.Marker.Z, true, true);
            if (!IsValid(sc))
                return;

            var angle = (float)Math.Atan2(sc.Y - y, sc.X - x);
            var pulse = Math.Abs(GetPulse(ItemAlertArrowPulseMs, ItemAlertArrowPulsePixels));
            DrawFilledSplitTailArrow(x + ((float)Math.Cos(angle) * pulse), y + ((float)Math.Sin(angle) * pulse), angle, ItemAlertArrowLength + pulse, ItemAlertArrowHeadSize, alert.ArrowBrush, alert.ArrowOutlineBrush, alert.ArrowHighlightBrush);
        }

        private void DrawOutlinedText(string text, float x, float y, IFont font, IFont outline)
        {
            var radius = Math.Max(1, Math.Min(6, ItemAlertTextOutlinePixels));
            if (outline != null)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if ((dx != 0 || dy != 0) && (dx * dx) + (dy * dy) <= (radius * radius) + 2)
                            outline.DrawText(text, x + dx, y + dy, false);
                    }
                }
            }
            font.DrawText(text, x, y, false);
        }

        private void UpdateItemMarkers()
        {
            RemoveDisabledRankMarkers();

            var now = Hud.Game.CurrentRealTimeMilliseconds;
            var live = new HashSet<string>();

            foreach (var item in Hud.Game.Items.Where(IsWatchedItem))
            {
                var key = GetDropKey(item);
                if (string.IsNullOrEmpty(key))
                    continue;

                live.Add(key);
                var liveKey = GetLiveKey(item);
                var coord = item.FloorCoordinate;
                ItemMarker marker;

                if (!_items.TryGetValue(key, out marker))
                {
                    marker = FindReusableMarker(item, key) ?? NewMarker(now);
                }
                else if (ShouldRearmLiveMarker(marker, item, liveKey, now))
                {
                    RearmMarker(marker, now);
                }

                marker.Key = key;
                marker.LiveKey = liveKey;
                marker.Rank = item.AncientRank;
                marker.Sno = item.SnoItem.Sno;
                marker.X = coord.X;
                marker.Y = coord.Y;
                marker.Z = coord.Z;
                marker.Name = GetItemName(item);
                marker.Label = GetItemLabel(item);
                marker.LastSeenMs = now;
                marker.MissingSinceMs = 0;
                marker.MissingNearPlayer = false;
                _items[key] = marker;
            }

            PurgeMissingItems(live, now);
        }

        private ItemMarker NewMarker(long now)
        {
            return new ItemMarker { FirstSeenMs = now, Sequence = ++_alertSequence };
        }

        private void RearmMarker(ItemMarker marker, long now)
        {
            marker.FirstSeenMs = now;
            marker.Sequence = ++_alertSequence;
            marker.MissingSinceMs = 0;
            marker.MissingNearPlayer = false;
        }

        private bool ShouldRearmLiveMarker(ItemMarker marker, IItem item, string liveKey, long now)
        {
            if (marker == null || item == null)
                return false;

            if (marker.MissingSinceMs > 0)
                return marker.MissingNearPlayer;

            if (string.IsNullOrEmpty(liveKey) || string.Equals(marker.LiveKey, liveKey, StringComparison.Ordinal))
                return false;

            return now - marker.LastSeenMs <= 1000 && IsMarkerNearPlayer(marker, ItemPickupCleanupDistance);
        }

        private ItemMarker FindReusableMarker(IItem item, string incomingKey)
        {
            var coord = item.FloorCoordinate;
            var maxDist = Math.Max(0.5f, ItemOffscreenRestoreMatchDistance);
            var maxDistSq = maxDist * maxDist;
            var bestDistSq = float.MaxValue;
            string bestKey = null;

            foreach (var pair in _items)
            {
                var marker = pair.Value;
                if (marker == null || marker.MissingNearPlayer || marker.Rank != item.AncientRank || marker.Sno != item.SnoItem.Sno)
                    continue;

                if (string.Equals(pair.Key, incomingKey, StringComparison.Ordinal))
                {
                    bestKey = pair.Key;
                    break;
                }

                var dx = marker.X - coord.X;
                var dy = marker.Y - coord.Y;
                var distSq = (dx * dx) + (dy * dy);
                if (distSq <= maxDistSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestKey = pair.Key;
                }
            }

            if (string.IsNullOrEmpty(bestKey))
                return null;

            var reused = _items[bestKey];
            _items.Remove(bestKey);
            return reused;
        }

        private void RemoveDisabledRankMarkers()
        {
            if (_items.Count == 0)
                return;

            var remove = new List<string>();
            foreach (var pair in _items)
                if (!IsRankEnabled(pair.Value.Rank))
                    remove.Add(pair.Key);
            foreach (var key in remove)
                _items.Remove(key);
        }

        private void PurgeMissingItems(HashSet<string> live, long now)
        {
            if (_items.Count == 0)
                return;

            var remove = new List<string>();
            foreach (var pair in _items)
            {
                var marker = pair.Value;
                if (marker == null)
                {
                    remove.Add(pair.Key);
                    continue;
                }

                if (live.Contains(pair.Key))
                    continue;

                if (marker.MissingSinceMs <= 0)
                {
                    marker.MissingSinceMs = now;
                    marker.MissingNearPlayer = IsMarkerNearPlayer(marker, ItemPickupCleanupDistance);
                }

                if (ShouldRemoveMissingMarker(marker, now))
                    remove.Add(pair.Key);
            }

            foreach (var key in remove)
                _items.Remove(key);
        }

        private bool ShouldRemoveMissingMarker(ItemMarker marker, long now)
        {
            if (marker == null)
                return true;

            if (marker.MissingNearPlayer)
                return true;

            if (Hud.Game.IsInTown && ItemTownMissingCleanupDelayMs >= 0 && marker.MissingSinceMs > 0 && now - marker.MissingSinceMs >= ItemTownMissingCleanupDelayMs)
                return true;

            return ItemMarkerMaxAgeMs > 0 && now - marker.LastSeenMs > ItemMarkerMaxAgeMs;
        }

        private bool IsMarkerNearPlayer(ItemMarker marker, float distance)
        {
            if (marker == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null || distance <= 0)
                return false;

            var me = Hud.Game.Me.FloorCoordinate;
            var dx = me.X - marker.X;
            var dy = me.Y - marker.Y;
            return (dx * dx) + (dy * dy) <= distance * distance;
        }

        private bool IsRankEnabled(int rank)
        {
            return (rank == AncientRank && ShowAncientItems) || (rank == PrimalRank && ShowPrimalItems);
        }

        private bool IsRankTextEnabled(int rank)
        {
            return rank == PrimalRank ? ShowPrimalItemAlertText : ShowAncientItemAlertText;
        }

        private bool IsRankArrowEnabled(int rank)
        {
            return rank == PrimalRank ? ShowPrimalItemArrows : ShowAncientItemArrows;
        }

        private bool IsRankNotificationEnabled(int rank)
        {
            return (ShowItemAlertText && IsRankTextEnabled(rank)) || (ShowItemAlertDirectionArrow && IsRankArrowEnabled(rank));
        }

        private int GetRankAlertHoldMs(int rank)
        {
            return Math.Max(0, rank == PrimalRank ? PrimalAlertTextHoldMs : AncientAlertTextHoldMs);
        }

        private void PaintPlayerGroundCircles()
        {
            if (!ShowPlayerMarkers || !ShowPlayerGroundCircles || _players.Count == 0 || PlayerFillBrushes == null || PlayerOutlineBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || player.FloorCoordinate == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                var sc = player.FloorCoordinate.ToScreenCoordinate(true, true);
                if (!IsValid(sc))
                    continue;

                var x = sc.X;
                var y = sc.Y + PlayerCircleScreenYOffset;
                var rx = Math.Max(3.0f, PlayerCircleScreenRadiusX);
                var ry = Math.Max(2.0f, PlayerCircleScreenRadiusY);
                if (PlayerCircleBlackBrush != null) PlayerCircleBlackBrush.DrawEllipse(x, y, rx, ry);
                PlayerFillBrushes[mark.Slot].DrawEllipse(x, y, rx, ry);
                PlayerOutlineBrushes[mark.Slot].DrawEllipse(x, y, rx, ry);
            }
        }

        private void PaintPlayerMinimapDots()
        {
            if (!ShowPlayerMarkers || !ShowPlayerMinimapDots || _players.Count == 0 || PlayerDotBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || player.FloorCoordinate == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                float x;
                float y;
                Hud.Render.GetMinimapCoordinates(player.FloorCoordinate.X, player.FloorCoordinate.Y, out x, out y);
                var radius = Math.Max(2.0f, PlayerMinimapDotRadius * Hud.Render.MinimapScale);
                if (ClampPlayerMinimapDots)
                    ClampToMinimap(ref x, ref y, radius + 2.0f);
                DrawDot(x, y, radius, PlayerDotBrushes[mark.Slot]);
            }
        }

        private void PaintPlayerPortraitMarkers()
        {
            if (!ShowPlayerMarkers || !ShowPlayerPortraitMarker || _players.Count == 0 || PlayerDotBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                var portrait = GetVisiblePortrait(player);
                if (portrait == null)
                    continue;

                var rect = portrait.Rectangle;
                if (rect.Width <= 1 || rect.Height <= 1)
                    continue;

                var radius = Math.Max(2.5f, PlayerPortraitDotRadius);
                var x = rect.X + PlayerPortraitOffsetX + radius;
                var y = rect.Y + PlayerPortraitOffsetY + radius;
                float angle;
                bool onScreen;
                if (TryGetPlayerDirection(player, out angle, out onScreen) && !onScreen)
                    DrawFilledSplitTailArrow(x, y, angle, PlayerPortraitArrowLength, PlayerPortraitArrowHeadSize, PlayerDotBrushes[mark.Slot], PlayerDotOutlineBrush, null);
                else
                    DrawDot(x, y, radius, PlayerDotBrushes[mark.Slot]);
            }
        }

        private void DrawDot(float x, float y, float radius, IBrush brush)
        {
            if (PlayerDotOutlineBrush != null)
                PlayerDotOutlineBrush.DrawEllipse(x, y, radius + 2.0f, radius + 2.0f);
            if (brush != null)
                brush.DrawEllipse(x, y, radius, radius);
        }

        private bool TryGetPlayerDirection(IPlayer player, out float angle, out bool onScreen)
        {
            angle = 0;
            onScreen = false;
            if (player == null || player.FloorCoordinate == null)
                return false;

            var target = player.FloorCoordinate.ToScreenCoordinate(true, true);
            if (IsValid(target))
            {
                onScreen = IsPointInsideWindow(target.X, target.Y, ItemScreenEdgeInteriorMargin);
                var origin = Hud.Game.Me != null && Hud.Game.Me.FloorCoordinate != null ? Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true) : null;
                var ox = IsValid(origin) ? origin.X : Hud.Window.Size.Width * 0.5f;
                var oy = IsValid(origin) ? origin.Y : Hud.Window.Size.Height * 0.5f;
                angle = (float)Math.Atan2(target.Y - oy, target.X - ox);
                return true;
            }

            if (Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return false;

            float tx;
            float ty;
            float mx;
            float my;
            Hud.Render.GetMinimapCoordinates(player.FloorCoordinate.X, player.FloorCoordinate.Y, out tx, out ty);
            Hud.Render.GetMinimapCoordinates(Hud.Game.Me.FloorCoordinate.X, Hud.Game.Me.FloorCoordinate.Y, out mx, out my);
            angle = (float)Math.Atan2(ty - my, tx - mx);
            return Math.Abs(tx - mx) > 0.001f || Math.Abs(ty - my) > 0.001f;
        }

        private bool TryGetHoveredPortraitPlayer(out IPlayer hovered)
        {
            hovered = null;
            try
            {
                foreach (var player in Hud.Game.Players.Where(p => p != null && p.IsInGame))
                {
                    var portrait = GetVisiblePortrait(player);
                    if (portrait == null)
                        continue;
                    var r = portrait.Rectangle;
                    if (r.Width > 0 && r.Height > 0 && Hud.Window.CursorInsideRect(r.X, r.Y, r.Width, r.Height))
                    {
                        hovered = player;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private IUiElement GetVisiblePortrait(IPlayer player)
        {
            if (player == null)
                return null;
            var portrait = player.PortraitUiElement;
            try
            {
                if (portrait != null && !portrait.Visible && portrait.ReplacementWhenNotVisible != null)
                    portrait = portrait.ReplacementWhenNotVisible;
            }
            catch { }
            return portrait != null && portrait.Visible ? portrait : null;
        }

        private void TogglePlayerMark(IPlayer player)
        {
            var key = GetPlayerKey(player);
            if (string.IsNullOrEmpty(key))
                return;

            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].Key == key)
                {
                    _players.RemoveAt(i);
                    return;
                }
            }

            var slot = FirstFreePlayerSlot();
            if (slot >= 0)
                _players.Add(new PlayerMark { Key = key, Slot = slot });
        }

        private int FirstFreePlayerSlot()
        {
            for (var slot = 0; slot < PlayerMarkerSlots; slot++)
            {
                var used = false;
                foreach (var mark in _players)
                    if (mark.Slot == slot) { used = true; break; }
                if (!used)
                    return slot;
            }
            return -1;
        }

        private void PurgePlayerMarks()
        {
            for (var i = _players.Count - 1; i >= 0; i--)
                if (FindPlayer(_players[i].Key) == null)
                    _players.RemoveAt(i);
        }

        private IPlayer FindPlayer(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            foreach (var player in Hud.Game.Players)
                if (player != null && player.IsInGame && GetPlayerKey(player) == key)
                    return player;
            return null;
        }

        private string GetPlayerKey(IPlayer player)
        {
            if (player == null)
                return null;
            if (player.HeroId != 0) return "hero:" + player.HeroId;
            if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait)) return "tag:" + player.BattleTagAbovePortrait;
            if (!string.IsNullOrEmpty(player.HeroName)) return "name:" + player.HeroName + ":" + player.Index;
            return "index:" + player.Index;
        }

        private bool IsWatchedItem(IItem item)
        {
            return item != null && item.Location == ItemLocation.Floor && item.FloorCoordinate != null && item.SnoItem != null && item.IsLegendary && item.SnoItem.Kind != ItemKind.craft && IsRankEnabled(item.AncientRank);
        }

        private bool IsHealthGlobe(IActor actor)
        {
            return actor != null && !actor.IsDisabled && actor.SnoActor != null && actor.SnoActor.Kind == ActorKind.HealthGlobe && actor.FloorCoordinate != null;
        }

        private bool IsRiftOrb(IActor actor)
        {
            return actor != null && !actor.IsDisabled && actor.SnoActor != null && actor.SnoActor.Kind == ActorKind.RiftOrb && actor.FloorCoordinate != null;
        }

        private string GetDropKey(IItem item)
        {
            var c = item.FloorCoordinate;
            return "drop:" + item.SnoItem.Sno + ":" + item.AncientRank + ":" + (int)Math.Round(c.X * 10.0f) + ":" + (int)Math.Round(c.Y * 10.0f);
        }

        private string GetLiveKey(IItem item)
        {
            if (!string.IsNullOrEmpty(item.ItemUniqueId)) return "uid:" + item.ItemUniqueId;
            if (item.AnnId != 0) return "ann:" + item.AnnId;
            if (item.AcdId != 0) return "acd:" + item.AcdId;
            return "tick:" + item.CreatedAtInGameTick;
        }

        private string GetItemName(IItem item)
        {
            if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameLocalized)) return item.SnoItem.NameLocalized;
            if (!string.IsNullOrEmpty(item.FullNameLocalized)) return item.FullNameLocalized;
            if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameEnglish)) return item.SnoItem.NameEnglish;
            if (!string.IsNullOrEmpty(item.FullNameEnglish)) return item.FullNameEnglish;
            return "item";
        }

        private string BuildAlertText(string name, string label)
        {
            var suffix = string.IsNullOrEmpty(label) ? string.Empty : " (" + label + ")";
            var text = (string.IsNullOrEmpty(name) ? "item" : name) + suffix;
            if (ItemAlertTextMaxCharacters <= 0 || text.Length <= ItemAlertTextMaxCharacters)
                return text;
            if (ItemAlertTextMaxCharacters <= 3)
                return text.Substring(0, ItemAlertTextMaxCharacters);
            return text.Substring(0, ItemAlertTextMaxCharacters - 3) + "...";
        }

        private string GetItemLabel(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return null;

            var label = LabelFromSnoType(item.SnoItem.SnoItemType);
            if (!string.IsNullOrEmpty(label))
                return label;

            label = LabelFromSearchText(BuildItemSearchText(item.SnoItem));
            if (!string.IsNullOrEmpty(label))
                return label;

            label = LabelFromLocation(item.SnoItem.UsedLocation1);
            return string.IsNullOrEmpty(label) ? LabelFromLocation(item.SnoItem.UsedLocation2) : label;
        }

        private string LabelFromSnoType(ISnoItemType type)
        {
            var guard = 0;
            while (type != null && guard++ < 8)
            {
                var label = ExactLabel(type.Code);
                if (!string.IsNullOrEmpty(label)) return label;
                label = ExactLabel(type.NameEnglish);
                if (!string.IsNullOrEmpty(label)) return label;
                type = type.ParentSnoType;
            }
            return null;
        }

        private string ExactLabel(string value)
        {
            var key = Normalize(value);
            if (string.IsNullOrEmpty(key))
                return null;
            foreach (var rule in ExactLabels)
                if (rule.MatchesExact(key))
                    return rule.Label;
            return null;
        }

        private string LabelFromSearchText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            foreach (var rule in SearchLabels)
                if (rule.Contains(text))
                    return rule.Label;
            return null;
        }

        private string BuildItemSearchText(ISnoItem sno)
        {
            var parts = new List<string>();
            Add(parts, sno.Code);
            Add(parts, sno.MainGroupCode);
            if (sno.GroupCodes != null)
                foreach (var code in sno.GroupCodes) Add(parts, code);

            var type = sno.SnoItemType;
            var guard = 0;
            while (type != null && guard++ < 8)
            {
                Add(parts, type.Code);
                Add(parts, type.NameEnglish);
                type = type.ParentSnoType;
            }
            return string.Join("|", parts.ToArray()).ToLowerInvariant();
        }

        private void Add(List<string> parts, string value)
        {
            if (!string.IsNullOrEmpty(value))
                parts.Add(value);
        }

        private string LabelFromLocation(ItemLocation location)
        {
            switch (location)
            {
                case ItemLocation.Head: return "Helm";
                case ItemLocation.Torso: return "Chest";
                case ItemLocation.Hands: return "Gloves";
                case ItemLocation.Waist: return "Belt";
                case ItemLocation.Feet: return "Boots";
                case ItemLocation.Shoulders: return "Shoulders";
                case ItemLocation.Legs: return "Pants";
                case ItemLocation.Bracers: return "Bracers";
                case ItemLocation.LeftRing:
                case ItemLocation.RightRing: return "Ring";
                case ItemLocation.Neck: return "Amulet";
                case ItemLocation.PetSpecial: return "Follower Token";
                case ItemLocation.PetNeck: return "Follower Amulet";
                case ItemLocation.PetRightRing:
                case ItemLocation.PetLeftRing: return "Follower Ring";
                default: return null;
            }
        }

        private void DrawOutlineTriangle(float x, float y, float radius, float angle, IBrush brush, IBrush outline)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tipX = x + cos * radius;
            var tipY = y + sin * radius;
            var baseX = x - cos * radius * 0.72f;
            var baseY = y - sin * radius * 0.72f;
            var half = radius * 0.72f;
            var leftX = baseX + px * half;
            var leftY = baseY + py * half;
            var rightX = baseX - px * half;
            var rightY = baseY - py * half;
            if (outline != null) DrawTriangleLines(outline, tipX, tipY, leftX, leftY, rightX, rightY);
            if (brush != null) DrawTriangleLines(brush, tipX, tipY, leftX, leftY, rightX, rightY);
        }

        private void DrawTriangleLines(IBrush brush, float tipX, float tipY, float leftX, float leftY, float rightX, float rightY)
        {
            brush.DrawLine(tipX, tipY, leftX, leftY);
            brush.DrawLine(leftX, leftY, rightX, rightY);
            brush.DrawLine(rightX, rightY, tipX, tipY);
        }

        private void DrawFilledSplitTailArrow(float x, float y, float angle, float length, float halfWidth, IBrush brush, IBrush outline, IBrush highlight)
        {
            if (brush == null)
                return;
            if (outline != null)
                FillSplitTailArrow(x, y, angle, length + 6.5f, halfWidth + 3.8f, outline);
            FillSplitTailArrow(x, y, angle, length, halfWidth, brush);
            if (highlight != null)
                FillArrowHighlight(x, y, angle, length, halfWidth, highlight);
        }

        private void FillSplitTailArrow(float x, float y, float angle, float length, float halfWidth, IBrush brush)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tip = new Vector2(x + cos * length * 0.62f, y + sin * length * 0.62f);
            var back1 = new Vector2(x - cos * length * 0.50f + px * halfWidth, y - sin * length * 0.50f + py * halfWidth);
            var notch = new Vector2(x - cos * length * 0.22f, y - sin * length * 0.22f);
            var back2 = new Vector2(x - cos * length * 0.50f - px * halfWidth, y - sin * length * 0.50f - py * halfWidth);
            FillPolygon(brush, tip, back1, notch, back2);
        }

        private void FillArrowHighlight(float x, float y, float angle, float length, float halfWidth, IBrush brush)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tip = new Vector2(x + cos * length * 0.34f, y + sin * length * 0.34f);
            var center = new Vector2(x - cos * length * 0.03f, y - sin * length * 0.03f);
            FillPolygon(brush, tip, new Vector2(center.X + px * halfWidth * 0.42f, center.Y + py * halfWidth * 0.42f), new Vector2(center.X - px * halfWidth * 0.12f, center.Y - py * halfWidth * 0.12f));
        }

        private void FillPolygon(IBrush brush, params Vector2[] points)
        {
            if (brush == null || points == null || points.Length < 3)
                return;
            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(points[0], FigureBegin.Filled);
                    for (var i = 1; i < points.Length; i++)
                        sink.AddLine(points[i]);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                brush.DrawGeometry(geometry);
            }
        }

        private float GetPulse(int ms, float amplitude)
        {
            if (ms <= 0 || Math.Abs(amplitude) < 0.001f)
                return 0.0f;
            return (float)Math.Sin((Hud.Game.CurrentRealTimeMilliseconds % ms) * Math.PI * 2.0d / ms) * amplitude;
        }

        private int GetAlertAlphaBucket(int rank, long elapsed)
        {
            var holdMs = GetRankAlertHoldMs(rank);
            if (elapsed <= holdMs)
                return 10;
            if (ItemAlertTextFadeMs <= 0)
                return 0;
            var fadeElapsed = elapsed - holdMs;
            if (fadeElapsed >= ItemAlertTextFadeMs)
                return 0;
            return Math.Max(1, Math.Min(10, (int)Math.Ceiling((1.0d - ((double)fadeElapsed / ItemAlertTextFadeMs)) * 10.0d)));
        }

        private int GetPopFontIndex(long elapsed, int count)
        {
            if (count <= 1 || ItemAlertTextPopMs <= 0)
                return 0;
            var t = Clamp((float)elapsed / ItemAlertTextPopMs, 0.0f, 1.0f);
            var u = t < 0.5f ? Smooth(t / 0.5f) : 1.0f - Smooth((t - 0.5f) / 0.5f);
            return Math.Max(0, Math.Min(count - 1, (int)Math.Round(u * (count - 1))));
        }

        private float GetTravelProgress(long elapsed)
        {
            if (ItemAlertTextPopMs <= 0 || elapsed >= ItemAlertTextPopMs)
                return 1.0f;
            return Smooth(Clamp((float)elapsed / ItemAlertTextPopMs, 0.0f, 1.0f));
        }

        private float Smooth(float t)
        {
            t = Clamp(t, 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        private float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private void ClampToMinimap(ref float x, ref float y, float margin)
        {
            if (Hud.Render.MinimapUiElement == null || !Hud.Render.MinimapUiElement.Visible)
                return;
            var r = Hud.Render.MinimapUiElement.Rectangle;
            x = Clamp(x, r.X + margin, r.X + r.Width - margin);
            y = Clamp(y, r.Y + margin, r.Y + r.Height - margin);
        }

        private bool IsPointInsideWindow(float x, float y, float margin)
        {
            var size = Hud.Window.Size;
            return x >= margin && x <= size.Width - margin && y >= margin && y <= size.Height - margin;
        }

        private bool IsValid(IScreenCoordinate p)
        {
            return p != null && !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y);
        }

        private float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private int C(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static LabelRule R(string label, params string[] keys)
        {
            return new LabelRule(label, keys);
        }

        private IEnumerable<IBrush> Brushes(params IBrush[] brushes)
        {
            if (brushes == null) yield break;
            foreach (var brush in brushes) if (brush != null) yield return brush;
        }

        private IEnumerable<IFont> Fonts(IFont[] fonts)
        {
            if (fonts == null) yield break;
            foreach (var font in fonts) if (font != null) yield return font;
        }

        private void UpdatePylonProgressMarkers()
        {
            if (!ShowPylonProgressMarkers)
                return;

            bool inGr = IsInGreaterRift();
            var percent = SafeRiftPercent();
            if (!inGr || percent < 0.0d || percent > 100.5d || (!_lastGreaterRiftState && _pylonProgressMarks.Count > 0))
            {
                _pylonProgressMarks.Clear();
                _lastRiftPercent = percent;
                _lastGreaterRiftState = inGr;
                return;
            }

            if (_lastGreaterRiftState && _lastRiftPercent > 10.0d && percent + 1.0d < _lastRiftPercent)
                _pylonProgressMarks.Clear();

            _lastGreaterRiftState = inGr;
            _lastRiftPercent = percent;
            var now = Hud.Game.CurrentRealTimeMilliseconds;

            bool sawMarkerPylon = false;
            try
            {
                if (Hud.Game.Markers != null)
                {
                    foreach (var marker in Hud.Game.Markers)
                    {
                        if (marker == null || !marker.IsPylon || marker.IsUsed)
                            continue;
                        sawMarkerPylon = true;
                        ActorSnoEnum markerSno = marker.SnoActor != null ? marker.SnoActor.Sno : 0;
                        AddPylonProgressMark(BuildPylonKey(marker.Id, marker.WorldId, marker.FloorCoordinate), percent, GetPylonShortName(marker), markerSno, marker.Name, marker.TextureSno, marker.TextureFrameIndex, now);
                    }
                }
            }
            catch { }

            if (!sawMarkerPylon)
            {
                try
                {
                    if (Hud.Game.Shrines != null)
                    {
                        foreach (var shrine in Hud.Game.Shrines)
                        {
                            if (shrine == null || !shrine.IsPylon || shrine.IsDisabled || shrine.IsOperated)
                                continue;
                            ActorSnoEnum shrineSno = shrine.SnoActor != null ? shrine.SnoActor.Sno : 0;
                            string shrineTypeName = string.Empty;
                            try { shrineTypeName = shrine.Type.ToString(); } catch { }
                            AddPylonProgressMark(BuildWorldKey("pylon", shrine.WorldId, shrine.FloorCoordinate), percent, GetPylonShortName(shrine), shrineSno, shrineTypeName, 0, 0, now);
                        }
                    }
                }
                catch { }
            }

            PurgePylonProgressMarks(now);
        }

        private void AddPylonProgressMark(string key, double percent, string label, ActorSnoEnum actorSno, string name, uint textureSno, int frameIndex, long now)
        {
            if (string.IsNullOrEmpty(key) || percent < 0.0d || percent > 100.5d)
                return;

            PylonProgressMark mark;
            if (!_pylonProgressMarks.TryGetValue(key, out mark))
            {
                mark = FindExistingPylonProgressMark(label);
                if (mark == null)
                {
                    mark = new PylonProgressMark { Key = key, Percent = Clamp((float)percent, 0.0f, 100.0f), Label = label ?? "PYL" };
                    _pylonProgressMarks[key] = mark;
                }
            }

            mark.LastSeenMs = now;

            var buffIcon = GetPylonBuffIconTexture(actorSno, name);
            if (buffIcon != null)
            {
                mark.Icon = buffIcon;
                mark.UsesBuffIcon = true;
                return;
            }

            if (textureSno != 0 && !mark.UsesBuffIcon)
            {
                mark.TextureSno = textureSno;
                mark.TextureFrameIndex = frameIndex;
                if (mark.Icon == null)
                {
                    try { mark.Icon = Hud.Texture.GetTexture(textureSno, frameIndex); } catch { mark.Icon = null; }
                }
            }
        }

        private PylonProgressMark FindExistingPylonProgressMark(string label)
        {
            if (string.IsNullOrEmpty(label) || string.Equals(label, "PYL", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var mark in _pylonProgressMarks.Values)
                if (string.Equals(mark.Label, label, StringComparison.OrdinalIgnoreCase))
                    return mark;

            return null;
        }

        private void PurgePylonProgressMarks(long now)
        {
            if (PylonProgressMarkerMaxAgeMs <= 0)
                return;
            var remove = _pylonProgressMarks.Values.Where(m => now - m.LastSeenMs > PylonProgressMarkerMaxAgeMs).Select(m => m.Key).ToList();
            foreach (var key in remove)
                _pylonProgressMarks.Remove(key);
        }

        private void PaintPylonProgressMarkers()
        {
            if (!ShowPylonProgressMarkers || _pylonProgressMarks.Count == 0 || PylonProgressLineBrush == null)
                return;
            if (!IsInGreaterRift())
                return;

            IUiElement bar = null;
            try { bar = Hud.Render.GreaterRiftBarUiElement; } catch { bar = null; }
            if (bar == null || !bar.Visible)
                return;

            var r = bar.Rectangle;
            if (r.Width <= 0 || r.Height <= 0)
                return;

            var iconSize = Math.Max(14.0f, PylonProgressIconSize);
            var gap = Math.Max(2.0f, PylonProgressIconGapPx);
            var iconY = r.Y - iconSize - gap;
            var lineTopY = iconY + iconSize;
            var lineBottomY = r.Y;
            foreach (var mark in _pylonProgressMarks.Values.OrderBy(m => m.Percent))
            {
                var x = r.X + (r.Width * Clamp(mark.Percent / 100.0f, 0.0f, 1.0f));
                if (PylonProgressLineOutlineBrush != null)
                    PylonProgressLineOutlineBrush.DrawLine(x, lineTopY, x, lineBottomY);
                PylonProgressLineBrush.DrawLine(x, lineTopY, x, lineBottomY);

                var iconX = x - iconSize * 0.5f;
                if (mark.Icon != null)
                {
                    mark.Icon.Draw(iconX, iconY, iconSize, iconSize, 0.94f);
                    if (mark.UsesBuffIcon && Hud.Texture.BuffFrameTexture != null)
                        Hud.Texture.BuffFrameTexture.Draw(iconX, iconY, iconSize, iconSize, 0.82f);
                }
                else if (PylonProgressTextFont != null && !string.IsNullOrEmpty(mark.Label))
                {
                    var text = mark.Label;
                    var layout = PylonProgressTextFont.GetTextLayout(text);
                    DrawOutlinedText(text, x - layout.Metrics.Width * 0.5f, iconY + (iconSize - layout.Metrics.Height) * 0.5f, PylonProgressTextFont, PoolOutlineFont);
                }
            }
        }

        private void UpdatePoolTracker()
        {
            if (!ShowPoolTracker || IsInsideNephalemRiftArea())
                return;

            var now = Hud.Game.CurrentRealTimeMilliseconds;
            UpdatePoolWorldTransitionState(now);
            UpdatePoolAreaVisitState(now);
            var suppressPoolRemoval = IsPoolListFrozen(now);
            try
            {
                if (Hud.Game.Shrines != null)
                {
                    foreach (var shrine in Hud.Game.Shrines)
                    {
                        if (shrine == null || !shrine.IsPoolOfReflection)
                            continue;

                        if (shrine.IsDisabled || shrine.IsOperated)
                        {
                            RememberConsumedPoolAt(shrine.WorldId, shrine.FloorCoordinate, now);
                            RemovePoolSpotAt(shrine.WorldId, shrine.FloorCoordinate);
                            continue;
                        }

                        AddPoolSpot(BuildWorldKey("pool", shrine.WorldId, shrine.FloorCoordinate), shrine.WorldId, shrine.FloorCoordinate, now, "SHRINE", GetActorSceneArea(shrine));
                    }
                }
            }
            catch { }

            try
            {
                if (Hud.Game.Markers != null)
                {
                    foreach (var marker in Hud.Game.Markers)
                    {
                        if (marker == null || !marker.IsPoolOfReflection)
                            continue;

                        if (marker.IsUsed)
                        {
                            RememberConsumedPoolAt(marker.WorldId, marker.FloorCoordinate, now);
                            RemovePoolSpotAt(marker.WorldId, marker.FloorCoordinate);
                            continue;
                        }

                        if (IsRememberedConsumedPoolAt(marker.WorldId, marker.FloorCoordinate))
                        {
                            RemovePoolSpotAt(marker.WorldId, marker.FloorCoordinate);
                            continue;
                        }

                        if (IsKnownFalsePoolMarkerRect(marker.WorldId, marker.FloorCoordinate))
                        {
                            RemovePoolSpotAt(marker.WorldId, marker.FloorCoordinate);
                            continue;
                        }

                        if (HasUsedOrDisabledPoolShrineAt(marker.WorldId, marker.FloorCoordinate))
                            continue;

                        AddPoolSpot(BuildPylonKey(marker.Id, marker.WorldId, marker.FloorCoordinate), marker.WorldId, marker.FloorCoordinate, now, "MARKER", null);
                    }
                }
            }
            catch { }

            try
            {
                if (Hud.Game.Actors != null)
                {
                    foreach (var actor in Hud.Game.Actors)
                    {
                        if (!IsPoolActor(actor))
                            continue;

                        if (actor.IsDisabled || actor.IsOperated)
                        {
                            RememberConsumedPoolAt(actor.WorldId, actor.FloorCoordinate, now);
                            RemovePoolSpotAt(actor.WorldId, actor.FloorCoordinate);
                            continue;
                        }

                        AddPoolSpot(BuildWorldKey("poolactor", actor.WorldId, actor.FloorCoordinate), actor.WorldId, actor.FloorCoordinate, now, "ACTOR", GetActorSceneArea(actor));
                    }
                }
            }
            catch { }

            UpdatePartyPoolConsumption();

            if (!suppressPoolRemoval)
                PurgePoolSpots(now);
        }

        private void UpdatePoolWorldTransitionState(long now)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return;

                var worldId = Hud.Game.Me.WorldId;
                var worldSno = Hud.Game.Me.WorldSno;
                if (worldId == 0 && worldSno == 0)
                    return;

                if (_poolLastPlayerWorldId == 0 && _poolLastPlayerWorldSno == 0)
                {
                    _poolLastPlayerWorldId = worldId;
                    _poolLastPlayerWorldSno = worldSno;
                    _poolWorldChangedSinceMs = now;
                    return;
                }

                if (_poolLastPlayerWorldId == worldId && _poolLastPlayerWorldSno == worldSno)
                    return;

                _poolLastPlayerWorldId = worldId;
                _poolLastPlayerWorldSno = worldSno;
                _poolWorldChangedSinceMs = now;
                _pendingPoolAreaKey = string.Empty;
                _pendingPoolAreaSinceMs = 0;
                FreezePoolListForAreaTransition(now);
            }
            catch { }
        }

        private bool IsLocalWorldTransitionSettled(long now)
        {
            var settleMs = Math.Max(0, PoolWorldTransitionSettleMs);
            if (settleMs <= 0 || _poolWorldChangedSinceMs <= 0)
                return true;

            if (now - _poolWorldChangedSinceMs >= settleMs)
                return true;

            return IsLocalPlayerSnoAreaSameAsSceneArea();
        }

        private bool IsLocalPlayerSnoAreaSameAsSceneArea()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return false;

                var playerSnapshot = GetAreaSnapshot(Hud.Game.Me.SnoArea);
                var sceneSnapshot = GetAreaSnapshot(GetActorSceneArea(Hud.Game.Me));
                if (playerSnapshot == null || sceneSnapshot == null)
                    return false;
                if (playerSnapshot.AreaSno == 0 || sceneSnapshot.AreaSno == 0)
                    return false;

                return IsPoolAreaSnapshotSame(playerSnapshot, sceneSnapshot);
            }
            catch { return false; }
        }

        private void UpdatePoolAreaVisitState(long now)
        {
            if (!IsLocalWorldTransitionSettled(now))
            {
                FreezePoolListForAreaTransition(now);
                return;
            }

            var key = GetCurrentPoolAreaKey();
            if (string.IsNullOrEmpty(key))
                return;

            if (string.Equals(key, _currentPoolAreaKey, StringComparison.Ordinal))
            {
                if (_currentPoolAreaSinceMs <= 0)
                    _currentPoolAreaSinceMs = now;
                _pendingPoolAreaKey = string.Empty;
                _pendingPoolAreaSinceMs = 0;
                return;
            }

            var settleMs = Math.Max(0, PoolAreaTransitionSettleMs);
            if (settleMs > 0)
            {
                if (!string.Equals(key, _pendingPoolAreaKey, StringComparison.Ordinal))
                {
                    FreezePoolListForAreaTransition(now);
                    _pendingPoolAreaKey = key;
                    _pendingPoolAreaSinceMs = now;
                    return;
                }

                if (now - _pendingPoolAreaSinceMs < settleMs)
                    return;
            }

            _currentPoolAreaKey = key;
            _currentPoolAreaSinceMs = now;
            _pendingPoolAreaKey = string.Empty;
            _pendingPoolAreaSinceMs = 0;
            _poolAreaVisitSequence++;
            ResetPoolArrowVisibilityState();
        }

        private bool IsPoolListFrozen(long now)
        {
            return _poolListFrozenUntilMs > now;
        }

        private void FreezePoolListForAreaTransition(long now)
        {
            var freezeMs = Math.Max(0, PoolListTransitionFreezeMs);
            if (freezeMs <= 0)
                return;

            var until = now + freezeMs;
            if (_poolListFrozenUntilMs >= until && _poolFrozenListOrder.Count > 0)
                return;

            _poolFrozenListOrder.Clear();
            foreach (var key in _poolListOrder)
                if (!string.IsNullOrEmpty(key) && _poolSpots.ContainsKey(key))
                    _poolFrozenListOrder.Add(key);
            _poolListFrozenUntilMs = until;
        }

        private string GetCurrentPoolAreaKey()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return string.Empty;

                var area = Hud.Game.Me.SnoArea;
                var areaSno = GetCanonicalPoolAreaSno(GetPrimaryAreaSno(area));
                var hostSno = GetCanonicalPoolAreaSno(GetHostAreaSno(area));
                return Hud.Game.Me.WorldId.ToString() + ":" + areaSno.ToString() + ":" + hostSno.ToString();
            }
            catch { return string.Empty; }
        }

        private float GetPoolMergeDistance()
        {
            return Math.Max(4.0f, Math.Min(40.0f, PoolSpotMergeDistance));
        }

        private bool IsPoolActor(IActor actor)
        {
            if (actor == null || actor.FloorCoordinate == null || !actor.FloorCoordinate.IsValid)
                return false;
            try { if (actor.GizmoType == GizmoType.PoolOfReflection) return true; } catch { }
            try { if (actor.SnoActor != null && actor.SnoActor.Sno == ActorSnoEnum._poolofreflection) return true; } catch { }
            return false;
        }

        private void AddPoolSpot(string key, uint worldId, IWorldCoordinate coordinate, long now, string source, ISnoArea sourceArea)
        {
            if (string.IsNullOrEmpty(key) || worldId == 0 || coordinate == null || !coordinate.IsValid)
                return;

            var markerOnly = string.Equals(source, "MARKER", StringComparison.OrdinalIgnoreCase);
            if (markerOnly && IsKnownFalsePoolMarkerRect(worldId, coordinate))
                return;

            var visibleConfirm = IsPoolCoordinateVisibleInCurrentWorld(worldId, coordinate);
            var localCloseConfirm = !markerOnly && IsPoolCoordinateNearLocalPlayer(worldId, coordinate);

            var localCloseSnapshot = localCloseConfirm ? GetAreaSnapshot(GetLocalPlayerAreaForWorld(worldId)) : null;
            var sourceSnapshot = !markerOnly ? GetAreaSnapshot(sourceArea) : null;
            var hintSnapshot = PoolUseAreaRectHints ? GetPoolAreaRectHintSnapshot(worldId, coordinate) : null;
            var visibleSnapshot = visibleConfirm ? GetAreaSnapshot(GetLocalPlayerAreaForWorld(worldId)) : null;
            var fallbackSnapshot = GetAreaSnapshot(FindAreaForWorld(worldId, now));

            PoolSpot spot;
            if (!_poolSpots.TryGetValue(key, out spot))
            {
                foreach (var existing in _poolSpots.Values)
                {
                    if (existing == null || existing.FloorCoordinate == null || existing.WorldId != worldId)
                        continue;

                    if (existing.FloorCoordinate.XYDistanceTo(coordinate) <= GetPoolMergeDistance())
                    {
                        spot = existing;
                        key = existing.Key;
                        break;
                    }
                }
            }

            var created = false;
            if (!_poolSpots.TryGetValue(key, out spot))
            {
                spot = new PoolSpot
                {
                    Key = key,
                    FirstSeenMs = now,
                    FirstSeenOrder = ++_poolFirstSeenSequence
                };

                _poolSpots[key] = spot;
                created = true;

                if (!_poolListOrder.Contains(key))
                    _poolListOrder.Add(key);
            }

            bool confirmHasArea;
            var snapshot = SelectPoolAreaSnapshot(markerOnly, now, localCloseSnapshot, sourceSnapshot, hintSnapshot, visibleSnapshot, fallbackSnapshot, out confirmHasArea);

            spot.WorldId = worldId;
            spot.FloorCoordinate = coordinate;
            spot.LastSeenMs = now;

            var hasResolvedArea = snapshot != null && snapshot.AreaSno != 0;
            confirmHasArea = hasResolvedArea && confirmHasArea;

            var sourceHasArea = sourceSnapshot != null && sourceSnapshot.AreaSno != 0;
            var shouldConfirmLocation = !spot.LocationConfirmed && confirmHasArea;
            var shouldRewriteFromSource = !spot.LocationConfirmed
                && !markerOnly
                && !confirmHasArea
                && sourceHasArea
                && IsPoolAreaSnapshotSame(snapshot, sourceSnapshot);
            var shouldWriteInitialUncertainLocation = !spot.LocationConfirmed
                && (created || string.IsNullOrEmpty(spot.Label) || shouldRewriteFromSource);

            if (snapshot != null && (shouldConfirmLocation || shouldWriteInitialUncertainLocation))
            {
                if (snapshot.AreaSno != 0)
                    spot.AreaSno = snapshot.AreaSno;

                if (snapshot.HostAreaSno != 0)
                    spot.HostAreaSno = snapshot.HostAreaSno;

                if (snapshot.Act != BountyAct.None)
                    spot.Act = snapshot.Act;

                if (!string.IsNullOrEmpty(snapshot.AreaName))
                    spot.AreaName = snapshot.AreaName;

                if (shouldConfirmLocation)
                    spot.LocationConfirmed = true;

                spot.LabelUncertain = !spot.LocationConfirmed;

                if (!IsPoolListFrozen(now) || string.IsNullOrEmpty(spot.Label) || shouldConfirmLocation)
                    spot.Label = BuildPoolLocationLabel(source, spot, spot.LabelUncertain);
            }

            if (_poolAreaVisitSequence > 0
                && string.IsNullOrEmpty(_pendingPoolAreaKey)
                && spot.LastArrowAreaVisitSequence != _poolAreaVisitSequence
                && !string.IsNullOrEmpty(spot.Label)
                && IsPoolSpotInCurrentArea(spot)
                && !IsPoolArrowUiSuppressed())
            {
                spot.LastArrowAreaVisitSequence = _poolAreaVisitSequence;
                ActivatePoolArrow(spot, now);
            }
        }

        private PoolAreaSnapshot SelectPoolAreaSnapshot(bool markerOnly, long now, PoolAreaSnapshot localCloseSnapshot, PoolAreaSnapshot sourceSnapshot, PoolAreaSnapshot hintSnapshot, PoolAreaSnapshot visibleSnapshot, PoolAreaSnapshot fallbackSnapshot, out bool confirmed)
        {
            confirmed = false;

            if (hintSnapshot != null && hintSnapshot.AreaSno != 0)
            {
                confirmed = true;
                return hintSnapshot;
            }

            if (!markerOnly)
            {
                var hasSource = sourceSnapshot != null && sourceSnapshot.AreaSno != 0;
                var hasLocal = localCloseSnapshot != null && localCloseSnapshot.AreaSno != 0;

                if (hasSource && hasLocal)
                {
                    if (IsPoolAreaSnapshotSame(sourceSnapshot, localCloseSnapshot))
                    {
                        confirmed = true;
                        return sourceSnapshot;
                    }

                    if (IsLocalPoolAreaStable(now))
                    {
                        confirmed = true;
                        return localCloseSnapshot;
                    }

                    return sourceSnapshot;
                }

                if (hasSource)
                {
                    confirmed = true;
                    return sourceSnapshot;
                }

                if (hasLocal)
                {
                    confirmed = IsLocalPoolAreaStable(now);
                    return localCloseSnapshot;
                }
            }

            if (visibleSnapshot != null && visibleSnapshot.AreaSno != 0)
            {
                confirmed = IsLocalPoolAreaStable(now);
                return visibleSnapshot;
            }

            if (fallbackSnapshot != null && fallbackSnapshot.AreaSno != 0 && IsLocalPoolAreaStable(now))
                return fallbackSnapshot;

            return null;
        }

        private bool IsPoolAreaSnapshotSame(PoolAreaSnapshot a, PoolAreaSnapshot b)
        {
            if (a == null || b == null)
                return false;

            var aArea = GetCanonicalPoolAreaSno(a.AreaSno);
            var bArea = GetCanonicalPoolAreaSno(b.AreaSno);
            if (aArea == 0 || bArea == 0 || aArea != bArea)
                return false;

            var aHost = GetCanonicalPoolAreaSno(a.HostAreaSno);
            var bHost = GetCanonicalPoolAreaSno(b.HostAreaSno);
            return aHost == 0 || bHost == 0 || aHost == bHost;
        }

        private bool IsLocalPoolAreaStable(long now)
        {
            if (!IsLocalWorldTransitionSettled(now))
                return false;
            if (!string.IsNullOrEmpty(_pendingPoolAreaKey))
                return false;
            if (string.IsNullOrEmpty(_currentPoolAreaKey) || _currentPoolAreaSinceMs <= 0)
                return false;

            var settleMs = Math.Max(0, PoolLocalAreaOverrideSettleMs);
            return settleMs <= 0 || now - _currentPoolAreaSinceMs >= settleMs;
        }

        private PoolAreaSnapshot GetPoolAreaRectHintSnapshot(uint worldId, IWorldCoordinate coordinate)
        {
            if (coordinate == null || !coordinate.IsValid)
                return null;

            var worldSno = GetWorldSnoForWorld(worldId);
            if (worldSno == 0)
                return null;

            var hint = FindPoolAreaRectHint(worldSno, coordinate);
            if (hint == null)
                return null;

            return new PoolAreaSnapshot
            {
                AreaSno = GetCanonicalPoolAreaSno(hint.AreaSno),
                HostAreaSno = GetCanonicalPoolAreaSno(hint.HostAreaSno),
                Act = hint.Act,
                AreaName = hint.AreaName
            };
        }

        private PoolAreaSnapshot GetAreaSnapshot(ISnoArea area)
        {
            if (area == null || GetPrimaryAreaSno(area) == 0)
                return null;

            return new PoolAreaSnapshot
            {
                AreaSno = GetCanonicalPoolAreaSno(GetPrimaryAreaSno(area)),
                HostAreaSno = GetCanonicalPoolAreaSno(GetHostAreaSno(area)),
                Act = GetBountyActFromNumber(area.Act),
                AreaName = GetAreaDisplayName(area)
            };
        }

        private uint GetWorldSnoForWorld(uint worldId)
        {
            if (worldId == 0 || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return 0;

            try
            {
                if (Hud.Game.Me.WorldId == worldId)
                    return Hud.Game.Me.WorldSno;
            }
            catch { }

            return 0;
        }

        private PoolAreaRectHint FindPoolAreaRectHint(uint worldSno, IWorldCoordinate coordinate)
        {
            if (worldSno == 0 || coordinate == null || !PoolUseAreaRectHints)
                return null;

            var x = coordinate.X;
            var y = coordinate.Y;
            for (var i = 0; i < PoolAreaRectHints.Length; i++)
            {
                var hint = PoolAreaRectHints[i];
                if (hint == null || hint.WorldSno != worldSno)
                    continue;

                if (x >= hint.MinX && x <= hint.MaxX && y >= hint.MinY && y <= hint.MaxY)
                    return hint;
            }

            return null;
        }

        private bool IsKnownFalsePoolMarkerRect(uint worldId, IWorldCoordinate coordinate)
        {
            if (!PoolSuppressKnownFalseMarkerRects || worldId == 0 || coordinate == null || !coordinate.IsValid)
                return false;

            if (GetWorldSnoForWorld(worldId) != 70885u)
                return false;

            if (coordinate.X < 480.0f || coordinate.X > 720.0f || coordinate.Y < 3840.0f || coordinate.Y > 4080.0f)
                return false;

            try
            {
                if (Hud.Game.Me != null && Hud.Game.Me.WorldId == worldId)
                {
                    var me = Hud.Game.Me.FloorCoordinate;
                    if (me != null && coordinate.XYDistanceTo(me) <= 1200.0f)
                        return false;
                }
            }
            catch { }

            return true;
        }

        private bool IsPoolCoordinateNearLocalPlayer(uint worldId, IWorldCoordinate coordinate)
        {
            if (worldId == 0 || coordinate == null || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            try
            {
                if (Hud.Game.Me.WorldId != worldId)
                    return false;

                var me = Hud.Game.Me.FloorCoordinate;
                if (me == null)
                    return false;

                var maxDistance = Math.Max(35.0f, PoolVisibleConfirmMaxDistance);
                return coordinate.XYDistanceTo(me) <= maxDistance;
            }
            catch { return false; }
        }

        private bool IsPoolCoordinateVisibleInCurrentWorld(uint worldId, IWorldCoordinate coordinate)
        {
            if (worldId == 0 || coordinate == null || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            try
            {
                if (Hud.Game.Me.WorldId != worldId)
                    return false;

                if (IsFullMapOpen())
                    return false;

                if (!coordinate.IsOnScreen(1))
                    return false;

                var me = Hud.Game.Me.FloorCoordinate;
                if (me != null)
                {
                    var maxDistance = Math.Max(35.0f, PoolVisibleConfirmMaxDistance);
                    if (coordinate.XYDistanceTo(me) > maxDistance)
                        return false;
                }

                return true;
            }
            catch { return false; }
        }

        private bool IsPoolArrowUiSuppressed()
        {
            try
            {
                if (Hud.Inventory == null)
                    return false;
                if (Hud.Inventory.InventoryMainUiElement != null && Hud.Inventory.InventoryMainUiElement.Visible)
                    return true;
                if (Hud.Inventory.StashMainUiElement != null && Hud.Inventory.StashMainUiElement.Visible)
                    return true;
                if (Hud.Inventory.HoveredItem != null)
                    return true;
                var main = Hud.Inventory.GetHoveredItemMainUiElement();
                if (main != null && main.Visible)
                    return true;
                var top = Hud.Inventory.GetHoveredItemTopUiElement();
                if (top != null && top.Visible)
                    return true;
            }
            catch { }
            return false;
        }

        private void RemovePoolArrow(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _activePoolArrows.Remove(key);
        }

        private void RemovePoolSpotAt(uint worldId, IWorldCoordinate coordinate)
        {
            if (worldId == 0 || coordinate == null)
                return;

            var remove = new List<string>();
            foreach (var spot in _poolSpots.Values)
            {
                if (spot == null || spot.FloorCoordinate == null || spot.WorldId != worldId)
                    continue;
                if (spot.FloorCoordinate.XYDistanceTo(coordinate) <= GetPoolMergeDistance())
                    remove.Add(spot.Key);
            }

            foreach (var key in remove)
            {
                _poolSpots.Remove(key);
                _poolListOrder.Remove(key);
                RemovePoolArrow(key);
            }
        }

        private void RememberConsumedPoolAt(uint worldId, IWorldCoordinate coordinate, long now)
        {
            if (worldId == 0 || coordinate == null || !coordinate.IsValid)
                return;

            var key = BuildConsumedPoolKey(worldId, coordinate);
            _consumedPoolSpots[key] = new ConsumedPoolSpot
            {
                WorldId = worldId,
                X = coordinate.X,
                Y = coordinate.Y
            };
        }

        private bool IsRememberedConsumedPoolAt(uint worldId, IWorldCoordinate coordinate)
        {
            if (worldId == 0 || coordinate == null || !coordinate.IsValid || _consumedPoolSpots.Count == 0)
                return false;

            var key = BuildConsumedPoolKey(worldId, coordinate);
            if (_consumedPoolSpots.ContainsKey(key))
                return true;

            var mergeDistance = GetPoolMergeDistance();
            foreach (var spot in _consumedPoolSpots.Values)
            {
                if (spot == null || spot.WorldId != worldId)
                    continue;

                var dx = spot.X - coordinate.X;
                var dy = spot.Y - coordinate.Y;
                if ((dx * dx) + (dy * dy) <= mergeDistance * mergeDistance)
                    return true;
            }

            return false;
        }

        private static string BuildConsumedPoolKey(uint worldId, IWorldCoordinate coordinate)
        {
            if (coordinate == null)
                return worldId.ToString();

            return worldId.ToString() + ":" + ((int)Math.Round(coordinate.X)).ToString() + ":" + ((int)Math.Round(coordinate.Y)).ToString();
        }

        private bool HasUsedOrDisabledPoolShrineAt(uint worldId, IWorldCoordinate coordinate)
        {
            if (worldId == 0 || coordinate == null || Hud.Game == null || Hud.Game.Shrines == null)
                return false;

            try
            {
                foreach (var shrine in Hud.Game.Shrines)
                {
                    if (shrine == null || !shrine.IsPoolOfReflection || shrine.WorldId != worldId || shrine.FloorCoordinate == null)
                        continue;
                    if (shrine.FloorCoordinate.XYDistanceTo(coordinate) > GetPoolMergeDistance())
                        continue;
                    if (shrine.IsDisabled || shrine.IsOperated)
                    {
                        RememberConsumedPoolAt(worldId, coordinate, Hud.Game.CurrentRealTimeMilliseconds);
                        RemovePoolSpotAt(worldId, coordinate);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void UpdatePartyPoolConsumption()
        {
            if (Hud.Game == null || Hud.Game.Players == null || _poolSpots.Count == 0)
                return;

            try
            {
                foreach (var player in Hud.Game.Players)
                {
                    if (player == null || !player.IsInGame)
                        continue;

                    var key = GetPlayerPoolKey(player);
                    if (key == 0)
                        continue;

                    var current = player.BonusPoolRemaining;
                    long previous;
                    if (_partyPoolBonusRemaining.TryGetValue(key, out previous) && current > previous)
                        RemovePoolSpotTakenBy(player);
                    _partyPoolBonusRemaining[key] = current;
                }
            }
            catch { }
        }

        private uint GetPlayerPoolKey(IPlayer player)
        {
            if (player == null)
                return 0;
            try { if (player.HeroId != 0) return player.HeroId; } catch { }
            try { return (uint)(player.Index + 1); } catch { }
            return 0;
        }

        private void RemovePoolSpotTakenBy(IPlayer player)
        {
            if (player == null || _poolSpots.Count == 0)
                return;

            IWorldCoordinate playerCoordinate = null;
            try { playerCoordinate = player.FloorCoordinate; } catch { playerCoordinate = null; }
            if (playerCoordinate == null || !playerCoordinate.IsValid)
                return;

            var playerWorldId = 0u;
            try { playerWorldId = player.WorldId; } catch { playerWorldId = 0u; }
            if (playerWorldId == 0)
                return;

            var maxDistance = Math.Max(80.0f, Math.Min(600.0f, PoolTakenByPlayerMaxDistance));
            var remove = _poolSpots.Values
                .Where(p => p != null
                    && p.FloorCoordinate != null
                    && p.WorldId == playerWorldId
                    && p.FloorCoordinate.XYDistanceTo(playerCoordinate) <= maxDistance)
                .OrderBy(p => p.FloorCoordinate.XYDistanceTo(playerCoordinate))
                .FirstOrDefault();

            if (remove != null && !string.IsNullOrEmpty(remove.Key))
            {
                RememberConsumedPoolAt(remove.WorldId, remove.FloorCoordinate, Hud.Game.CurrentRealTimeMilliseconds);
                _poolSpots.Remove(remove.Key);
                _poolListOrder.Remove(remove.Key);
                RemovePoolArrow(remove.Key);
            }
        }

        private void PurgePoolSpots(long now)
        {
            var remove = new List<string>();
            foreach (var spot in _poolSpots.Values)
            {
                if (spot == null || spot.WorldId == 0 || spot.FloorCoordinate == null || !spot.FloorCoordinate.IsValid)
                    remove.Add(spot != null ? spot.Key : null);
            }
            foreach (var key in remove)
                if (!string.IsNullOrEmpty(key))
                {
                    _poolSpots.Remove(key);
                    _poolListOrder.Remove(key);
                    RemovePoolArrow(key);
                }
        }

        private void PaintPoolTrackerTop()
        {
            if (!ShowPoolTracker || _poolSpots.Count == 0 || PoolLabelFont == null || IsInsideGreaterRiftArea())
                return;

            var spots = GetOrderedPoolSpotsForList().ToList();

            if (spots.Count == 0)
                return;

            float rightX;
            float y;
            GetPoolListAnchor(out rightX, out y);

            foreach (var spot in spots)
            {
                if (string.IsNullOrEmpty(spot.Label))
                    continue;

                y += DrawRightAlignedLine(spot.Label, rightX, y, PoolLabelFont, PoolOutlineFont, 2);
            }
        }

        private IEnumerable<PoolSpot> GetOrderedPoolSpotsForList()
        {
            var now = Hud.Game.CurrentRealTimeMilliseconds;
            var seen = new HashSet<string>();

            if (IsPoolListFrozen(now) && _poolFrozenListOrder.Count > 0)
            {
                foreach (var key in _poolFrozenListOrder.ToList())
                {
                    PoolSpot spot;
                    if (!string.IsNullOrEmpty(key)
                        && _poolSpots.TryGetValue(key, out spot)
                        && spot != null
                        && spot.FloorCoordinate != null)
                    {
                        seen.Add(key);
                        yield return spot;
                    }
                }

                foreach (var key in _poolListOrder.ToList())
                {
                    if (string.IsNullOrEmpty(key) || seen.Contains(key))
                        continue;

                    PoolSpot spot;
                    if (!_poolSpots.TryGetValue(key, out spot) || spot == null || spot.FloorCoordinate == null)
                    {
                        _poolListOrder.Remove(key);
                        continue;
                    }

                    seen.Add(key);
                    yield return spot;
                }

                foreach (var spot in _poolSpots.Values.OrderBy(p => p.FirstSeenOrder <= 0 ? long.MaxValue : p.FirstSeenOrder).ThenBy(p => p.FirstSeenMs))
                {
                    if (spot == null || spot.FloorCoordinate == null || string.IsNullOrEmpty(spot.Key) || seen.Contains(spot.Key))
                        continue;

                    _poolListOrder.Add(spot.Key);
                    seen.Add(spot.Key);
                    yield return spot;
                }

                yield break;
            }

            _poolFrozenListOrder.Clear();

            foreach (var key in _poolListOrder.ToList())
            {
                PoolSpot spot;
                if (string.IsNullOrEmpty(key) || !_poolSpots.TryGetValue(key, out spot) || spot == null || spot.FloorCoordinate == null)
                {
                    _poolListOrder.Remove(key);
                    continue;
                }

                seen.Add(key);
                yield return spot;
            }

            foreach (var spot in _poolSpots.Values.OrderBy(p => p.FirstSeenOrder <= 0 ? long.MaxValue : p.FirstSeenOrder).ThenBy(p => p.FirstSeenMs))
            {
                if (spot == null || spot.FloorCoordinate == null || string.IsNullOrEmpty(spot.Key) || seen.Contains(spot.Key))
                    continue;

                _poolListOrder.Add(spot.Key);
                seen.Add(spot.Key);
                yield return spot;
            }
        }

        private void GetPoolListAnchor(out float rightX, out float y)
        {
            rightX = Hud.Window.Size.Width - (18.0f * Hud.Window.HeightUiRatio);
            y = Hud.Window.Size.Height * Clamp(PoolTopTextYFrac, 0.02f, 0.35f);

            try
            {
                var mini = Hud.Render.MinimapUiElement;
                if (mini != null && mini.Visible)
                {
                    var rect = mini.Rectangle;
                    rightX = rect.Left - (Math.Max(0.0f, PoolListOffsetXPx) * Hud.Window.HeightUiRatio);
                    y = rect.Top + (Math.Max(0.0f, PoolListOffsetYPx) * Hud.Window.HeightUiRatio);
                }
            }
            catch { }
        }

        private void PaintPoolWorldStatus()
        {
            if (!ShowPoolTracker || !ShowPoolPartyStatus || _poolSpots.Count == 0)
                return;

            foreach (var spot in _poolSpots.Values)
            {
                if (spot == null || spot.FloorCoordinate == null || !IsPoolSpotInCurrentArea(spot))
                    continue;

                try
                {
                    if (!spot.FloorCoordinate.IsOnScreen(1))
                        continue;
                }
                catch { continue; }

                var screen = spot.FloorCoordinate.ToScreenCoordinate(true, true);
                if (screen == null || !IsFinite(screen.X) || !IsFinite(screen.Y))
                    continue;

                var status = GetWorldPartyStatus(spot.WorldId);
                var font = status.Ready ? PoolReadyFont : PoolMissingFont;
                var y = screen.Y - (34.0f * Hud.Window.HeightUiRatio);
                foreach (var line in status.Lines)
                    y += DrawCenteredLine(line, screen.X, y + PoolStatusLineGapPx, font, PoolOutlineFont, 2);
            }
        }

        private void PaintPoolDirectionArrows()
        {
            if (IsInsideNephalemRiftArea())
                return;

            DrawActivePoolArrows();
        }

        private void ResetPoolArrowVisibilityState()
        {
            foreach (var arrow in _activePoolArrows.Values)
            {
                if (arrow == null)
                    continue;

                arrow.LastUx = 1.0f;
                arrow.LastUy = 0.0f;
                arrow.TargetWasVisible = false;
                arrow.TargetVisibleSinceMs = 0;
            }
        }

        private void CleanupPoolArrows(long now)
        {
            if (_activePoolArrows.Count == 0)
                return;

            var remove = new List<string>();
            foreach (var kv in _activePoolArrows)
            {
                var arrow = kv.Value;
                if (arrow == null || now > arrow.ExpireMs)
                    remove.Add(kv.Key);
            }

            foreach (var key in remove)
                _activePoolArrows.Remove(key);
        }

        private void ActivatePoolArrow(PoolSpot spot, long now)
        {
            if (spot == null || string.IsNullOrEmpty(spot.Key) || spot.FloorCoordinate == null || !spot.FloorCoordinate.IsValid)
                return;

            try
            {
                if (!IsPoolSpotInCurrentArea(spot))
                    return;
            }
            catch { }

            CleanupPoolArrows(now);

            PoolArrowAlert arrow;
            if (_activePoolArrows.TryGetValue(spot.Key, out arrow) && arrow != null && now <= arrow.ExpireMs)
            {
                arrow.X = spot.FloorCoordinate.X;
                arrow.Y = spot.FloorCoordinate.Y;
                arrow.Z = spot.FloorCoordinate.Z;
                arrow.WorldId = spot.WorldId;
                arrow.AreaSno = spot.AreaSno;
                arrow.HostAreaSno = spot.HostAreaSno;
                return;
            }

            _activePoolArrows[spot.Key] = new PoolArrowAlert
            {
                Key = spot.Key,
                X = spot.FloorCoordinate.X,
                Y = spot.FloorCoordinate.Y,
                Z = spot.FloorCoordinate.Z,
                WorldId = spot.WorldId,
                AreaSno = spot.AreaSno,
                HostAreaSno = spot.HostAreaSno,
                StartMs = now,
                ExpireMs = now + Math.Max(700, PoolArrowLifetimeMs),
                LastUx = 1.0f,
                LastUy = 0.0f,
                TargetWasVisible = false,
                TargetVisibleSinceMs = 0
            };
        }

        private void DrawActivePoolArrows()
        {
            if (!ShowPoolTracker || _activePoolArrows.Count == 0 || PoolGroundBrush == null || PoolGroundOutlineBrush == null)
                return;
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return;

            var now = Hud.Game.CurrentRealTimeMilliseconds;
            CleanupPoolArrows(now);

            if (_activePoolArrows.Count == 0)
                return;

            if (IsPoolArrowUiSuppressed())
                return;

            var arrows = _activePoolArrows.Values
                .Where(a => a != null)
                .OrderBy(a => a.StartMs)
                .ThenBy(a => a.Key)
                .ToList();

            var ui = Hud.Window.HeightUiRatio;
            var spacing = Math.Max(0.0f, PoolDirectionArrowMultiOffsetPx * ui);

            for (var i = 0; i < arrows.Count; i++)
            {
                var laneOffsetPx = (i - ((arrows.Count - 1) * 0.5f)) * spacing;
                DrawActivePoolArrow(arrows[i], now, laneOffsetPx);
            }
        }

        private void DrawActivePoolArrow(PoolArrowAlert arrow, long now, float laneOffsetPx)
        {
            if (arrow == null)
                return;

            try
            {
                if (arrow.WorldId != 0 && arrow.WorldId != Hud.Game.Me.WorldId)
                    return;

                var me = Hud.Game.Me.FloorCoordinate;
                if (me == null || !me.IsValid)
                    return;

                var area = Hud.Game.Me.SnoArea;
                if (!IsPoolAreaMatch(arrow.AreaSno, arrow.HostAreaSno, area))
                    return;

                float dxWorld = arrow.X - me.X;
                float dyWorld = arrow.Y - me.Y;
                float distanceSq = dxWorld * dxWorld + dyWorld * dyWorld;
                if (distanceSq < 4.0f)
                    return;

                float distance = (float)Math.Sqrt(distanceSq);
                float nxWorld = dxWorld / distance;
                float nyWorld = dyWorld / distance;

                var screenMe = me.ToScreenCoordinate(true, true);
                var screenStep = me.Offset(nxWorld * 10.0f, nyWorld * 10.0f, 0).ToScreenCoordinate(true, true);
                if (screenMe == null || screenStep == null)
                    return;

                float ux = screenStep.X - screenMe.X;
                float uy = screenStep.Y - screenMe.Y;
                bool targetOnScreen = false;
                float targetScreenX = 0.0f;
                float targetScreenY = 0.0f;

                try
                {
                    var targetCoordinate = me.Offset(dxWorld, dyWorld, arrow.Z - me.Z);
                    if (targetCoordinate != null && targetCoordinate.IsValid && targetCoordinate.IsOnScreen(1))
                    {
                        var screenTarget = targetCoordinate.ToScreenCoordinate(true, true);
                        if (screenTarget != null)
                        {
                            float sx = screenTarget.X - screenMe.X;
                            float sy = screenTarget.Y - screenMe.Y;
                            float sl = (float)Math.Sqrt(sx * sx + sy * sy);
                            if (sl >= 0.01f)
                            {
                                ux = sx;
                                uy = sy;
                                targetScreenX = screenTarget.X;
                                targetScreenY = screenTarget.Y;
                                targetOnScreen = true;
                            }
                        }
                    }
                }
                catch { }

                float screenLen = (float)Math.Sqrt(ux * ux + uy * uy);
                if (screenLen < 0.01f)
                {
                    ux = arrow.LastUx;
                    uy = arrow.LastUy;
                    screenLen = (float)Math.Sqrt(ux * ux + uy * uy);
                    if (screenLen < 0.01f)
                        return;
                }

                ux /= screenLen;
                uy /= screenLen;
                arrow.LastUx = ux;
                arrow.LastUy = uy;

                if (targetOnScreen != arrow.TargetWasVisible)
                {
                    arrow.TargetWasVisible = targetOnScreen;
                    arrow.TargetVisibleSinceMs = targetOnScreen ? now : 0;
                }

                float px = -uy;
                float py = ux;
                float laneX = px * laneOffsetPx;
                float laneY = py * laneOffsetPx;

                float alpha = 1.0f;
                var remainingMs = arrow.ExpireMs - now;
                if (PoolArrowFadeMs > 0 && remainingMs < PoolArrowFadeMs)
                    alpha = Math.Max(0.0f, Math.Min(1.0f, remainingMs / (float)PoolArrowFadeMs));

                PoolGroundBrush.Opacity = alpha;
                PoolGroundOutlineBrush.Opacity = alpha;
                if (PoolArrowLightBrush != null) PoolArrowLightBrush.Opacity = alpha * 0.70f;
                if (PoolArrowShadowBrush != null) PoolArrowShadowBrush.Opacity = alpha * 0.50f;

                var cycle = Math.Max(150, PoolArrowCycleMs);
                var t = ((now - arrow.StartMs) % cycle) / (float)cycle;
                var pulse = (float)Math.Sin(t * Math.PI * 2.0) * Math.Max(0.0f, PoolDirectionArrowMotionPx);

                float ui = Hud.Window.HeightUiRatio;
                float length = Math.Max(28.0f, PoolDirectionArrowLengthPx * ui);
                float shaftWidth = Math.Max(6.0f, PoolDirectionArrowShaftWidthPx * ui);
                float headLength = Math.Max(10.0f, Math.Min(length - 8.0f, PoolDirectionArrowHeadLengthPx * ui));
                float headWidth = Math.Max(shaftWidth + 12.0f, PoolDirectionArrowHeadWidthPx * ui);
                float outline = Math.Max(0.0f, PoolDirectionArrowOutlinePx * ui);
                float startDistance = Math.Max(70.0f, PoolDirectionArrowDistancePx * ui);

                float playerBaseX = screenMe.X + ux * (startDistance + pulse);
                float playerBaseY = screenMe.Y + uy * (startDistance + pulse);

                float edgeX;
                float edgeY;
                GetScreenEdgePoint(screenMe.X, screenMe.Y, ux, uy, Math.Max(45.0f * ui, length + outline + 14.0f), out edgeX, out edgeY);
                float edgeBaseX = edgeX - ux * (length + Math.Max(8.0f, PoolArrowVisibleTargetOffsetPx * ui) - pulse);
                float edgeBaseY = edgeY - uy * (length + Math.Max(8.0f, PoolArrowVisibleTargetOffsetPx * ui) - pulse);

                float targetBaseX = edgeBaseX;
                float targetBaseY = edgeBaseY;
                if (targetOnScreen)
                {
                    float targetOffset = Math.Max(0.0f, PoolArrowVisibleTargetOffsetPx * ui);
                    float visibleBaseX = targetScreenX - ux * (length + targetOffset - pulse);
                    float visibleBaseY = targetScreenY - uy * (length + targetOffset - pulse);

                    if (arrow.TargetVisibleSinceMs > arrow.StartMs + PoolArrowHoldNearPlayerMs)
                    {
                        float visibleT = 1.0f;
                        int visibleFlyMs = Math.Max(1, PoolArrowFlyToPoolMs);
                        visibleT = Math.Max(0.0f, Math.Min(1.0f, (now - arrow.TargetVisibleSinceMs) / (float)visibleFlyMs));
                        visibleT = visibleT * visibleT * (3.0f - 2.0f * visibleT);
                        targetBaseX = edgeBaseX + (visibleBaseX - edgeBaseX) * visibleT;
                        targetBaseY = edgeBaseY + (visibleBaseY - edgeBaseY) * visibleT;
                    }
                    else
                    {
                        targetBaseX = visibleBaseX;
                        targetBaseY = visibleBaseY;
                    }
                }

                float elapsed = Math.Max(0.0f, now - arrow.StartMs);
                float holdMs = Math.Max(0, PoolArrowHoldNearPlayerMs);
                float flyMs = Math.Max(1, PoolArrowFlyToPoolMs);
                float flyT = elapsed <= holdMs ? 0.0f : Math.Max(0.0f, Math.Min(1.0f, (elapsed - holdMs) / flyMs));
                flyT = flyT * flyT * (3.0f - 2.0f * flyT);

                float baseX = playerBaseX + (targetBaseX - playerBaseX) * flyT + laneX;
                float baseY = playerBaseY + (targetBaseY - playerBaseY) * flyT + laneY;

                float outlineBaseX = baseX - ux * outline;
                float outlineBaseY = baseY - uy * outline;

                if (PoolArrowShadowBrush != null)
                {
                    PoolArrowShadowBrush.Opacity = alpha * 0.28f;
                    DrawSinglePoolArrowGeometry(outlineBaseX + 4.0f, outlineBaseY + 5.0f, ux, uy, px, py,
                        length + outline * 2.5f,
                        shaftWidth + outline * 2.0f,
                        headLength + outline * 2.2f,
                        headWidth + outline * 2.4f,
                        PoolArrowShadowBrush);
                }

                DrawSinglePoolArrowGeometry(outlineBaseX, outlineBaseY, ux, uy, px, py,
                    length + outline * 2.5f,
                    shaftWidth + outline * 2.0f,
                    headLength + outline * 2.2f,
                    headWidth + outline * 2.4f,
                    PoolGroundOutlineBrush);

                DrawSinglePoolArrowGeometry(baseX, baseY, ux, uy, px, py,
                    length,
                    shaftWidth,
                    headLength,
                    headWidth,
                    PoolGroundBrush);

                if (PoolArrowShadowBrush != null)
                {
                    PoolArrowShadowBrush.Opacity = alpha * 0.50f;
                    DrawPoolArrowLowerBevelGeometry(baseX, baseY, ux, uy, px, py, length, shaftWidth, headLength, headWidth, PoolArrowShadowBrush);
                }

                if (PoolArrowLightBrush != null)
                {
                    PoolArrowLightBrush.Opacity = alpha * 0.70f;
                    DrawPoolArrowHighlightGeometry(baseX, baseY, ux, uy, px, py, length, shaftWidth, headLength, headWidth, PoolArrowLightBrush);
                }
            }
            catch { }
        }

        private void GetScreenEdgePoint(float fromX, float fromY, float ux, float uy, float margin, out float edgeX, out float edgeY)
        {
            var size = Hud.Window.Size;
            var minX = margin;
            var minY = margin;
            var maxX = Math.Max(minX, size.Width - margin);
            var maxY = Math.Max(minY, size.Height - margin);

            var best = float.MaxValue;
            if (Math.Abs(ux) > 0.0001f)
            {
                var t1 = (minX - fromX) / ux;
                if (t1 > 0.0f) best = Math.Min(best, t1);
                var t2 = (maxX - fromX) / ux;
                if (t2 > 0.0f) best = Math.Min(best, t2);
            }
            if (Math.Abs(uy) > 0.0001f)
            {
                var t3 = (minY - fromY) / uy;
                if (t3 > 0.0f) best = Math.Min(best, t3);
                var t4 = (maxY - fromY) / uy;
                if (t4 > 0.0f) best = Math.Min(best, t4);
            }

            if (best == float.MaxValue || float.IsNaN(best) || float.IsInfinity(best))
                best = 260.0f * Hud.Window.HeightUiRatio;

            edgeX = Clamp(fromX + ux * best, minX, maxX);
            edgeY = Clamp(fromY + uy * best, minY, maxY);
        }

        private void DrawSinglePoolArrowGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(baseX - px * halfShaft, baseY - py * halfShaft), FigureBegin.Filled);
                    sink.AddLine(new Vector2(baseX + ux * shaftLength - px * halfShaft, baseY + uy * shaftLength - py * halfShaft));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength - px * halfHead, baseY + uy * shaftLength - py * halfHead));
                    sink.AddLine(new Vector2(baseX + ux * length, baseY + uy * length));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * halfHead, baseY + uy * shaftLength + py * halfHead));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * halfShaft, baseY + uy * shaftLength + py * halfShaft));
                    sink.AddLine(new Vector2(baseX + px * halfShaft, baseY + py * halfShaft));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawPoolArrowLowerBevelGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float bevel = Math.Max(4.0f, Math.Min(shaftWidth * 0.30f, 10.0f));
            float inset = Math.Max(2.0f, shaftWidth * 0.10f);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(baseX + px * (halfShaft - inset), baseY + py * (halfShaft - inset)), FigureBegin.Filled);
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset), baseY + uy * shaftLength + py * (halfShaft - inset)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfHead - inset), baseY + uy * shaftLength + py * (halfHead - inset)));
                    sink.AddLine(new Vector2(baseX + ux * (length - Math.Max(8.0f, headLength * 0.18f)) + px * Math.Max(1.5f, bevel * 0.35f), baseY + uy * (length - Math.Max(8.0f, headLength * 0.18f)) + py * Math.Max(1.5f, bevel * 0.35f)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel), baseY + uy * shaftLength + py * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset - bevel), baseY + uy * shaftLength + py * (halfShaft - inset - bevel)));
                    sink.AddLine(new Vector2(baseX + px * (halfShaft - inset - bevel), baseY + py * (halfShaft - inset - bevel)));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawPoolArrowHighlightGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float yTop = -halfShaft + Math.Max(4.0f, shaftWidth * 0.22f);
            float thickness = Math.Max(2.0f, Math.Min(4.0f, shaftWidth * 0.14f));
            float start = Math.Max(6.0f, shaftWidth * 0.35f);
            float end = Math.Max(start + 6.0f, shaftLength - Math.Max(3.0f, headLength * 0.12f));

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    var a = new Vector2(baseX + ux * start + px * yTop, baseY + uy * start + py * yTop);
                    var b = new Vector2(baseX + ux * end + px * yTop, baseY + uy * end + py * yTop);
                    var c = new Vector2(baseX + ux * end + px * (yTop + thickness), baseY + uy * end + py * (yTop + thickness));
                    var d = new Vector2(baseX + ux * start + px * (yTop + thickness), baseY + uy * start + py * (yTop + thickness));
                    sink.BeginFigure(a, FigureBegin.Filled);
                    sink.AddLine(b);
                    sink.AddLine(c);
                    sink.AddLine(d);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }

            float diagStartX = shaftLength + Math.Max(2.0f, headLength * 0.12f);
            float diagEndX = length - Math.Max(8.0f, headLength * 0.16f);
            float diagStartY = -halfHead + Math.Max(6.0f, headWidth * 0.18f);
            float diagEndY = -Math.Max(2.0f, headWidth * 0.06f);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    var a = new Vector2(baseX + ux * diagStartX + px * diagStartY, baseY + uy * diagStartX + py * diagStartY);
                    var b = new Vector2(baseX + ux * diagEndX + px * diagEndY, baseY + uy * diagEndX + py * diagEndY);
                    var c = new Vector2(baseX + ux * diagEndX + px * (diagEndY + thickness), baseY + uy * diagEndX + py * (diagEndY + thickness));
                    var d = new Vector2(baseX + ux * diagStartX + px * (diagStartY + thickness), baseY + uy * diagStartX + py * (diagStartY + thickness));
                    sink.BeginFigure(a, FigureBegin.Filled);
                    sink.AddLine(b);
                    sink.AddLine(c);
                    sink.AddLine(d);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private bool IsPoolSpotInCurrentArea(PoolSpot spot)
        {
            if (spot == null || Hud.Game == null || Hud.Game.Me == null)
                return false;
            if (spot.WorldId != 0 && spot.WorldId != Hud.Game.Me.WorldId)
                return false;
            try { return IsPoolAreaMatch(spot, Hud.Game.Me.SnoArea); } catch { return true; }
        }

        private bool IsPoolAreaMatch(PoolSpot spot, ISnoArea area)
        {
            if (spot == null)
                return false;
            return IsPoolAreaMatch(spot.AreaSno, spot.HostAreaSno, area);
        }

        private bool IsPoolAreaMatch(uint spotAreaSno, uint spotHostAreaSno, ISnoArea area)
        {
            if (area == null)
                return true;

            return IsPoolAreaMatch(spotAreaSno, spotHostAreaSno, GetPrimaryAreaSno(area), GetHostAreaSno(area));
        }

        private bool IsPoolAreaMatch(uint spotAreaSno, uint spotHostAreaSno, uint areaSno, uint hostSno)
        {
            var canonicalSpotAreaSno = GetCanonicalPoolAreaSno(spotAreaSno);
            var canonicalSpotHostAreaSno = GetCanonicalPoolAreaSno(spotHostAreaSno);
            areaSno = GetCanonicalPoolAreaSno(areaSno);
            hostSno = GetCanonicalPoolAreaSno(hostSno);

            if (canonicalSpotAreaSno == 0 && canonicalSpotHostAreaSno == 0)
                return true;
            if (areaSno != 0 && (areaSno == canonicalSpotAreaSno || areaSno == canonicalSpotHostAreaSno))
                return true;
            if (hostSno != 0 && (hostSno == canonicalSpotAreaSno || hostSno == canonicalSpotHostAreaSno))
                return true;
            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private PartyWorldStatus GetWorldPartyStatus(uint worldId)
        {
            var status = new PartyWorldStatus { Ready = false, Lines = new List<string>() };
            if (worldId == 0 || Hud.Game == null || Hud.Game.Players == null)
                return status;

            var missing = new List<string>();
            var count = 0;
            try
            {
                foreach (var player in Hud.Game.Players)
                {
                    if (player == null || !player.IsInGame)
                        continue;
                    count++;
                    if (player.WorldId != worldId)
                        missing.Add(GetPlayerShortName(player));
                }
            }
            catch { }

            if (count <= 0)
                return status;
            if (missing.Count == 0)
            {
                status.Ready = true;
                status.Lines.Add("READY");
            }
            else
            {
                status.Lines.Add("MISSING:");
                status.Lines.AddRange(missing);
            }
            return status;
        }

        private void UpdateVisitedAreas()
        {
            if (!ShowVisitedWaypointMarkers || Hud.Game == null || Hud.Game.Players == null)
                return;

            var now = Hud.Game.CurrentRealTimeMilliseconds;
            try
            {
                foreach (var player in Hud.Game.Players)
                {
                    if (player == null || !player.IsInGame || player.SnoArea == null)
                        continue;
                    AddVisitedArea(player.SnoArea, GetPlayerShortName(player), now);
                }
            }
            catch { }
        }

        private void AddVisitedArea(ISnoArea area, string playerName, long now)
        {
            if (area == null || area.Sno == 0 || area.IsTown)
                return;

            var act = GetBountyActFromNumber(area.Act);
            if (act == BountyAct.None)
                act = GetBountyActFromNumber(Hud.Game.CurrentAct);

            AddVisitedAreaSno(act, area.Sno, area.NameLocalized, playerName, now);
            if (area.HostSnoArea != null && area.HostSnoArea.Sno != 0)
            {
                AddVisitedAreaSno(act, area.HostSnoArea.Sno, area.HostSnoArea.NameLocalized, playerName, now);
            }
            if (area.HostAreaSno != 0)
                AddVisitedAreaSno(act, area.HostAreaSno, area.NameLocalized, playerName, now);

            AddVisitedWaypointTargetsForArea(act, area, playerName, now);
        }

        private void AddVisitedWaypointTargetsForArea(BountyAct act, ISnoArea area, string playerName, long now)
        {
            if (act == BountyAct.None || area == null || Hud.Game == null || Hud.Game.ActMapWaypoints == null)
                return;

            try
            {
                var areaSno = GetPrimaryAreaSno(area);
                var hostSno = GetHostAreaSno(area);
                foreach (var waypoint in Hud.Game.ActMapWaypoints)
                {
                    if (waypoint == null || waypoint.TargetSnoArea == null || waypoint.BountyAct != act)
                        continue;
                    var target = waypoint.TargetSnoArea;
                    var targetSno = GetPrimaryAreaSno(target);
                    var targetHost = GetHostAreaSno(target);
                    if ((areaSno != 0 && (areaSno == targetSno || areaSno == targetHost)) ||
                        (hostSno != 0 && (hostSno == targetSno || hostSno == targetHost)))
                        AddVisitedAreaSno(act, target.Sno, target.NameLocalized, playerName, now);
                }
            }
            catch { }
        }

        private void AddVisitedAreaSno(BountyAct act, uint sno, string name, string playerName, long now)
        {
            if (sno == 0 || act == BountyAct.None)
                return;
            var key = BuildVisitedAreaKey(act, sno);
            VisitedArea area;
            if (!_visitedAreas.TryGetValue(key, out area))
            {
                area = new VisitedArea { Act = act, Sno = sno, Name = name ?? string.Empty };
                _visitedAreas[key] = area;
            }
            area.LastSeenMs = now;
            area.PlayerName = playerName ?? string.Empty;
        }

        private BountyAct GetDisplayedWaypointMapAct()
        {
            BountyAct currentAct;
            try { currentAct = Hud.Game.ActMapCurrentAct; } catch { currentAct = BountyAct.None; }
            if (currentAct != BountyAct.None)
                return currentAct;

            try
            {
                foreach (var pair in _actMapFallbackElements)
                    if (pair.Key != null && pair.Key.Visible)
                        return pair.Value;
            }
            catch { }

            return BountyAct.None;
        }

        private void PaintVisitedWaypointMarkers()
        {
            if (!ShowVisitedWaypointMarkers || _visitedAreas.Count == 0 || Hud.Game == null || Hud.Game.ActMapWaypoints == null || Hud.Render.WorldMapUiElement == null)
                return;

            var map = Hud.Render.WorldMapUiElement;
            if (!map.Visible || (Hud.Render.ActMapUiElement != null && Hud.Render.ActMapUiElement.Visible))
                return;

            var currentAct = GetDisplayedWaypointMapAct();

            try
            {
                foreach (var waypoint in Hud.Game.ActMapWaypoints)
                {
                    if (waypoint == null || waypoint.TargetSnoArea == null)
                        continue;
                    if (currentAct == BountyAct.None || waypoint.BountyAct != currentAct)
                        continue;
                    var waypointAct = waypoint.BountyAct;
                    if (!IsWaypointVisited(waypointAct, waypoint.TargetSnoArea))
                        continue;

                    var x = map.Rectangle.X + (waypoint.CoordinateOnMapUiElement.X * Hud.Window.HeightUiRatio) + (VisitedWaypointMarkerOffsetXPx * Hud.Window.HeightUiRatio);
                    var y = map.Rectangle.Y + (waypoint.CoordinateOnMapUiElement.Y * Hud.Window.HeightUiRatio);
                    var radius = Math.Max(3.0f, VisitedWaypointMarkerRadius * Hud.Window.HeightUiRatio);
                    if (VisitedWaypointOutlineBrush != null)
                        VisitedWaypointOutlineBrush.DrawEllipse(x, y, radius + 2.2f, radius + 2.2f);
                    if (VisitedWaypointBrush != null)
                        VisitedWaypointBrush.DrawEllipse(x, y, radius, radius);
                    if (VisitedWaypointFont != null)
                        DrawCenteredLine("✓", x, y - radius - 8.0f, VisitedWaypointFont, PoolOutlineFont, 1);
                }
            }
            catch { }
        }

        private bool IsWaypointVisited(BountyAct act, ISnoArea area)
        {
            if (area == null || act == BountyAct.None)
                return false;
            if (_visitedAreas.ContainsKey(BuildVisitedAreaKey(act, area.Sno)))
                return true;
            if (area.HostAreaSno != 0 && _visitedAreas.ContainsKey(BuildVisitedAreaKey(act, area.HostAreaSno)))
                return true;
            if (area.HostSnoArea != null && _visitedAreas.ContainsKey(BuildVisitedAreaKey(act, area.HostSnoArea.Sno)))
                return true;
            return false;
        }

        private static string BuildVisitedAreaKey(BountyAct act, uint sno)
        {
            return ((int)act).ToString() + ":" + sno.ToString();
        }

        private string BuildPoolLocationLabel(string prefix, PoolSpot spot)
        {
            return BuildPoolLocationLabel(prefix, spot, spot != null && spot.LabelUncertain);
        }

        private string BuildPoolLocationLabel(string prefix, PoolSpot spot, bool uncertain)
        {
            if (spot == null)
                return "A? - pool";

            var name = ResolvePoolWaypointDisplayName(spot.AreaName);
            if (string.IsNullOrEmpty(name))
                name = "current area";
            var actNumber = GetActNumber(spot.Act);
            if (actNumber <= 0)
                actNumber = Math.Max(1, Hud.Game.CurrentAct);
            return "A" + actNumber.ToString() + " - " + name + (uncertain ? " (?)" : string.Empty);
        }

        private static string ResolvePoolWaypointDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            var alias = GetPoolWaypointAlias(name);
            return string.IsNullOrEmpty(alias) ? name : alias;
        }

        private static string GetPoolWaypointAlias(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            var n = name.Trim();
            if (string.Equals(n, "Sundered Canyon", StringComparison.OrdinalIgnoreCase))
                return "Howling Plateau";
            if (string.Equals(n, "Highlands Crossing", StringComparison.OrdinalIgnoreCase))
                return "Southern Highlands";
            if (string.Equals(n, "The Battlefields", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Battlefields", StringComparison.OrdinalIgnoreCase))
                return "Battlefields";
            if (string.Equals(n, "The Arreat Gate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Arreat Gate", StringComparison.OrdinalIgnoreCase))
                return "Battlefields";
            return string.Empty;
        }

        private static uint GetCanonicalPoolAreaSno(uint areaSno)
        {
            switch (areaSno)
            {
                case 93632u: return 19940u;
                case 19836u: return 19837u;
                case 314782u: return 261758u;
                case 154644u: return 112548u;
                default: return areaSno;
            }
        }

        private ISnoArea GetActorSceneArea(IActor actor)
        {
            if (actor == null)
                return null;

            try
            {
                if (actor.Scene != null && actor.Scene.SnoArea != null && actor.Scene.SnoArea.Sno != 0)
                    return actor.Scene.SnoArea;
            }
            catch { }

            return null;
        }

        private ISnoArea FindAreaForWorld(uint worldId, long now)
        {
            var local = GetLocalPlayerAreaForWorld(worldId);
            if (local != null)
                return IsLocalPoolAreaStable(now) ? local : null;

            try
            {
                if (Hud.Game.Players != null)
                {
                    foreach (var player in Hud.Game.Players)
                    {
                        if (player == null || !player.IsInGame || player.WorldId != worldId || player.SnoArea == null)
                            continue;
                        if (Hud.Game.Me != null && ReferenceEquals(player, Hud.Game.Me))
                            continue;
                        return player.SnoArea;
                    }
                }
            }
            catch { }
            return null;
        }

        private ISnoArea GetLocalPlayerAreaForWorld(uint worldId)
        {
            try
            {
                if (Hud.Game.Me != null && Hud.Game.Me.WorldId == worldId)
                    return Hud.Game.Me.SnoArea;
            }
            catch { }
            return null;
        }

        private static uint GetPrimaryAreaSno(ISnoArea area)
        {
            if (area == null)
                return 0;
            return area.Sno;
        }

        private static uint GetHostAreaSno(ISnoArea area)
        {
            if (area == null)
                return 0;
            if (area.HostSnoArea != null && area.HostSnoArea.Sno != 0)
                return area.HostSnoArea.Sno;
            return area.HostAreaSno;
        }

        private static string GetAreaDisplayName(ISnoArea area)
        {
            if (area == null)
                return string.Empty;
            if (area.HostSnoArea != null && !string.IsNullOrEmpty(area.HostSnoArea.NameLocalized))
                return area.HostSnoArea.NameLocalized;
            return FirstNonEmpty(area.NameLocalized, area.NameEnglish, area.Code);
        }

        private static int GetActNumber(BountyAct act)
        {
            switch (act)
            {
                case BountyAct.A1: return 1;
                case BountyAct.A2: return 2;
                case BountyAct.A3: return 3;
                case BountyAct.A4: return 4;
                case BountyAct.A5: return 5;
                default: return 0;
            }
        }

        private bool IsInsideNephalemRiftArea()
        {
            try { return Hud.Game.SpecialArea == SpecialArea.Rift; }
            catch { return false; }
        }

        private bool IsInsideGreaterRiftArea()
        {
            try { return Hud.Game.SpecialArea == SpecialArea.GreaterRift; }
            catch { return false; }
        }

        private bool IsInGreaterRift()
        {
            try { return Hud.Game.Me.InGreaterRift || Hud.Game.SpecialArea == SpecialArea.GreaterRift; }
            catch { return false; }
        }

        private double SafeRiftPercent()
        {
            try { return Hud.Game.RiftPercentage; } catch { return -1.0d; }
        }

        private static string BuildPylonKey(string id, uint worldId, IWorldCoordinate coordinate)
        {
            if (!string.IsNullOrEmpty(id))
                return "id:" + id;
            return BuildWorldKey("obj", worldId, coordinate);
        }

        private static string BuildWorldKey(string prefix, uint worldId, IWorldCoordinate coordinate)
        {
            if (coordinate == null)
                return prefix + ":" + worldId.ToString();
            return prefix + ":" + worldId.ToString() + ":" + ((int)Math.Round(coordinate.X)).ToString() + ":" + ((int)Math.Round(coordinate.Y)).ToString();
        }

        private string GetPylonShortName(IMarker marker)
        {
            if (marker == null)
                return "PYL";
            return GetPylonShortName(marker.SnoActor != null ? marker.SnoActor.Sno : 0, marker.Name);
        }

        private string GetPylonShortName(IShrine shrine)
        {
            if (shrine == null)
                return "PYL";
            string typeName = string.Empty;
            try { typeName = shrine.Type.ToString(); } catch { }
            return GetPylonShortName(shrine.SnoActor != null ? shrine.SnoActor.Sno : 0, typeName);
        }

        private ITexture GetPylonBuffIconTexture(ActorSnoEnum sno, string name)
        {
            var powerSno = GetPylonBuffPowerSno(sno, name);
            if (powerSno == 0)
                return null;

            try
            {
                var power = Hud.Sno.GetSnoPower(powerSno);
                if (power != null && power.NormalIconTextureId != 0)
                    return Hud.Texture.GetTexture(power.NormalIconTextureId);

                if (power != null && power.Icons != null && power.Icons.Length > 0 && power.Icons[0].TextureId != 0)
                    return Hud.Texture.GetTexture(power.Icons[0].TextureId);
            }
            catch { }

            return null;
        }

        private static uint GetPylonBuffPowerSno(ActorSnoEnum sno, string name)
        {
            switch (sno)
            {
                case ActorSnoEnum._x1_lr_shrine_damage: return 262935u; // Generic_PagesBuffDamage / Power
                case ActorSnoEnum._x1_lr_shrine_electrified: return 403404u; // Generic_PagesBuffElectrifiedTieredRift / Conduit
                case ActorSnoEnum._x1_lr_shrine_electrified_tieredrift: return 403404u; // Conduit
                case ActorSnoEnum._x1_lr_shrine_infinite_casting: return 266258u; // Generic_PagesBuffInfiniteCasting / Channeling
                case ActorSnoEnum._x1_lr_shrine_invulnerable: return 266254u; // Generic_PagesBuffInvulnerable / Shield
                case ActorSnoEnum._x1_lr_shrine_run_speed: return 266271u; // Generic_PagesBuffRunSpeed / Speed
            }

            var n = (name ?? string.Empty).ToLowerInvariant();
            if (n.IndexOf("damage") >= 0 || n.IndexOf("power") >= 0) return 262935u;
            if (n.IndexOf("elect") >= 0 || n.IndexOf("conduit") >= 0) return 403404u;
            if (n.IndexOf("channel") >= 0 || n.IndexOf("infinite") >= 0) return 266258u;
            if (n.IndexOf("invulnerable") >= 0 || n.IndexOf("shield") >= 0) return 266254u;
            if (n.IndexOf("speed") >= 0 || n.IndexOf("run") >= 0) return 266271u;
            return 0u;
        }

        private string GetPylonShortName(ActorSnoEnum sno, string name)
        {
            switch (sno)
            {
                case ActorSnoEnum._x1_lr_shrine_damage: return "POW";
                case ActorSnoEnum._x1_lr_shrine_electrified: return "CON";
                case ActorSnoEnum._x1_lr_shrine_electrified_tieredrift: return "CON";
                case ActorSnoEnum._x1_lr_shrine_infinite_casting: return "CHN";
                case ActorSnoEnum._x1_lr_shrine_invulnerable: return "SHD";
                case ActorSnoEnum._x1_lr_shrine_run_speed: return "SPD";
            }
            var n = (name ?? string.Empty).ToLowerInvariant();
            if (n.IndexOf("damage") >= 0 || n.IndexOf("power") >= 0) return "POW";
            if (n.IndexOf("elect") >= 0 || n.IndexOf("conduit") >= 0) return "CON";
            if (n.IndexOf("channel") >= 0 || n.IndexOf("infinite") >= 0) return "CHN";
            if (n.IndexOf("invulnerable") >= 0 || n.IndexOf("shield") >= 0) return "SHD";
            if (n.IndexOf("speed") >= 0 || n.IndexOf("run") >= 0) return "SPD";
            return "PYL";
        }

        private string GetPlayerShortName(IPlayer player)
        {
            if (player == null)
                return "?";
            try { if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait)) return player.BattleTagAbovePortrait; } catch { }
            try { if (!string.IsNullOrEmpty(player.HeroName)) return player.HeroName; } catch { }
            try { return "P" + (player.Index + 1).ToString(); } catch { }
            return "?";
        }

        private float DrawCenteredLine(string text, float centerX, float y, IFont font, IFont outline, int outlineRadius)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return 0.0f;
            var layout = font.GetTextLayout(text);
            var x = centerX - (layout.Metrics.Width * 0.5f);
            DrawOutlinedText(text, x, y, font, outline, outlineRadius);
            return Math.Max(10.0f, layout.Metrics.Height + 1.0f);
        }

        private float DrawRightAlignedLine(string text, float rightX, float y, IFont font, IFont outline, int outlineRadius)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return 0.0f;
            var layout = font.GetTextLayout(text);
            DrawOutlinedText(text, rightX - layout.Metrics.Width, y, font, outline, outlineRadius);
            return Math.Max(10.0f, layout.Metrics.Height + 1.0f);
        }

        private void DrawOutlinedText(string text, float x, float y, IFont font, IFont outline, int radius)
        {
            radius = Math.Max(1, Math.Min(6, radius));
            if (outline != null)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if ((dx != 0 || dy != 0) && (dx * dx) + (dy * dy) <= (radius * radius) + 2)
                            outline.DrawText(text, x + dx, y + dy, false);
                    }
                }
            }
            if (font != null)
                font.DrawText(text, x, y, false);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;
            foreach (var value in values)
                if (!string.IsNullOrEmpty(value))
                    return value;
            return string.Empty;
        }

        private static BountyAct GetBountyActFromNumber(int act)
        {
            switch (act)
            {
                case 1: return BountyAct.A1;
                case 2: return BountyAct.A2;
                case 3: return BountyAct.A3;
                case 4: return BountyAct.A4;
                case 5: return BountyAct.A5;
                default: return BountyAct.None;
            }
        }

        private class LabelRule
        {
            public readonly string Label;
            private readonly string[] _keys;

            public LabelRule(string label, string[] keys)
            {
                Label = label;
                _keys = keys ?? new string[0];
                for (var i = 0; i < _keys.Length; i++)
                    _keys[i] = _keys[i].Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            }

            public bool MatchesExact(string key)
            {
                foreach (var k in _keys)
                    if (k == key) return true;
                return false;
            }

            public bool Contains(string text)
            {
                var compact = text.Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
                foreach (var k in _keys)
                    if (compact.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }
        }

        private class PylonProgressMark
        {
            public string Key;
            public float Percent;
            public string Label;
            public uint TextureSno;
            public int TextureFrameIndex;
            public ITexture Icon;
            public bool UsesBuffIcon;
            public long LastSeenMs;
        }

        private static readonly PoolAreaRectHint[] PoolAreaRectHints =
        {
            new PoolAreaRectHint(2826u, 480.0f, 240.0f, 720.0f, 480.0f, 19774u, 0u, BountyAct.A1, "Halls of Agony Level 1"),
            new PoolAreaRectHint(2826u, 480.0f, 720.0f, 720.0f, 960.0f, 19774u, 0u, BountyAct.A1, "Halls of Agony Level 1"),
            new PoolAreaRectHint(50582u, 480.0f, 720.0f, 720.0f, 960.0f, 19783u, 0u, BountyAct.A1, "Cathedral Level 2"),
            new PoolAreaRectHint(71150u, 2220.0f, 1680.0f, 2340.0f, 1800.0f, 19954u, 0u, BountyAct.A1, "The Weeping Hollow"),
            new PoolAreaRectHint(71150u, 2220.0f, 1920.0f, 2340.0f, 2040.0f, 19954u, 0u, BountyAct.A1, "The Weeping Hollow"),
            new PoolAreaRectHint(71150u, 1920.0f, 960.0f, 2160.0f, 1200.0f, 19952u, 0u, BountyAct.A1, "Fields of Misery"),
            new PoolAreaRectHint(95804u, 1200.0f, 480.0f, 1440.0f, 720.0f, 69504u, 0u, BountyAct.A3, "Rakkis Crossing"),
            new PoolAreaRectHint(95804u, 4320.0f, 240.0f, 4560.0f, 480.0f, 154644u, 112548u, BountyAct.A3, "Battlefields"),
            new PoolAreaRectHint(95804u, 3840.0f, 240.0f, 4080.0f, 480.0f, 112548u, 0u, BountyAct.A3, "Battlefields"),
            new PoolAreaRectHint(154587u, 240.0f, 720.0f, 480.0f, 960.0f, 154588u, 0u, BountyAct.A1, "Defiled Crypt"),
            new PoolAreaRectHint(409000u, 240.0f, 240.0f, 480.0f, 480.0f, 409001u, 0u, BountyAct.A4, "Besieged Tower Level 1"),
            new PoolAreaRectHint(136415u, 720.0f, 240.0f, 960.0f, 480.0f, 136448u, 0u, BountyAct.A3, "The Keep Depths Level 3"),
            new PoolAreaRectHint(79401u, 1080.0f, 720.0f, 1440.0f, 1080.0f, 80791u, 0u, BountyAct.A3, "Tower of the Damned Level 1"),
            new PoolAreaRectHint(93099u, 3120.0f, 3120.0f, 3360.0f, 3360.0f, 93173u, 0u, BountyAct.A3, "Stonefort"),
            new PoolAreaRectHint(93099u, 4080.0f, 4080.0f, 4320.0f, 4320.0f, 93173u, 0u, BountyAct.A3, "Stonefort"),
            new PoolAreaRectHint(338600u, 1440.0f, 720.0f, 1560.0f, 840.0f, 338602u, 0u, BountyAct.A5, "Battlefields of Eternity"),
            new PoolAreaRectHint(338600u, 960.0f, 480.0f, 1080.0f, 600.0f, 338602u, 0u, BountyAct.A5, "Battlefields of Eternity"),
            new PoolAreaRectHint(460372u, 1080.0f, 240.0f, 1200.0f, 360.0f, 460671u, 0u, BountyAct.A5, "Shrouded Moors"),
            new PoolAreaRectHint(460372u, 600.0f, 1200.0f, 720.0f, 1320.0f, 460671u, 0u, BountyAct.A5, "Shrouded Moors"),
            new PoolAreaRectHint(408254u, 480.0f, 360.0f, 600.0f, 480.0f, 427763u, 0u, BountyAct.A5, "Greyhollow Island"),
            new PoolAreaRectHint(261712u, 1680.0f, 720.0f, 1920.0f, 960.0f, 261758u, 0u, BountyAct.A5, "Westmarch Commons"),
            new PoolAreaRectHint(263494u, 1200.0f, 720.0f, 1440.0f, 960.0f, 263493u, 0u, BountyAct.A5, "Westmarch Heights"),
            new PoolAreaRectHint(283566u, 240.0f, 480.0f, 480.0f, 720.0f, 283567u, 0u, BountyAct.A5, "Ruins of Corvus"),
            new PoolAreaRectHint(283552u, 960.0f, 480.0f, 1200.0f, 720.0f, 283553u, 0u, BountyAct.A5, "Passage to Corvus"),
            new PoolAreaRectHint(283552u, 720.0f, 240.0f, 960.0f, 480.0f, 283553u, 0u, BountyAct.A5, "Passage to Corvus"),
            new PoolAreaRectHint(338600u, 240.0f, 1560.0f, 360.0f, 1680.0f, 367831u, 0u, BountyAct.A5, "The Crag of Eternity"),
            new PoolAreaRectHint(267412u, 960.0f, 1560.0f, 1080.0f, 1680.0f, 258142u, 0u, BountyAct.A5, "Paths of the Drowned"),
            new PoolAreaRectHint(263494u, 720.0f, 960.0f, 960.0f, 1200.0f, 263493u, 0u, BountyAct.A5, "Westmarch Heights"),
            new PoolAreaRectHint(261712u, 1200.0f, 1440.0f, 1440.0f, 1680.0f, 261758u, 0u, BountyAct.A5, "Westmarch Commons"),
            new PoolAreaRectHint(261712u, 720.0f, 720.0f, 960.0f, 960.0f, 314782u, 0u, BountyAct.A5, "Westmarch Commons"),
            new PoolAreaRectHint(428493u, 480.0f, 240.0f, 720.0f, 480.0f, 428494u, 0u, BountyAct.A3, "The Ruins of Sescheron"),
            new PoolAreaRectHint(71150u, 2100.0f, 1680.0f, 2220.0f, 1800.0f, 72712u, 0u, BountyAct.A1, "Cemetery of the Forsaken"),
            new PoolAreaRectHint(71150u, 2340.0f, 4680.0f, 2460.0f, 4800.0f, 93632u, 0u, BountyAct.A1, "Highlands Crossing"),
            new PoolAreaRectHint(70885u, 1440.0f, 480.0f, 1680.0f, 720.0f, 19839u, 0u, BountyAct.A2, "Stinging Winds"),
            new PoolAreaRectHint(70885u, 1680.0f, 720.0f, 1920.0f, 960.0f, 19838u, 0u, BountyAct.A2, "Black Canyon Mines"),
            new PoolAreaRectHint(70885u, 1680.0f, 960.0f, 1920.0f, 1200.0f, 19838u, 0u, BountyAct.A2, "Black Canyon Mines"),
            new PoolAreaRectHint(70885u, 1920.0f, 720.0f, 2160.0f, 960.0f, 19838u, 0u, BountyAct.A2, "Black Canyon Mines"),
            new PoolAreaRectHint(70885u, 1920.0f, 960.0f, 2160.0f, 1200.0f, 19838u, 0u, BountyAct.A2, "Black Canyon Mines"),
            new PoolAreaRectHint(70885u, 2160.0f, 1200.0f, 2400.0f, 1440.0f, 19837u, 0u, BountyAct.A2, "Howling Plateau"),
            new PoolAreaRectHint(70885u, 1440.0f, 960.0f, 1680.0f, 1200.0f, 19835u, 0u, BountyAct.A2, "Road to Alcarnus"),
            new PoolAreaRectHint(70885u, 1440.0f, 1200.0f, 1680.0f, 1440.0f, 19835u, 0u, BountyAct.A2, "Road to Alcarnus"),
            new PoolAreaRectHint(70885u, 960.0f, 3840.0f, 1200.0f, 4080.0f, 53834u, 0u, BountyAct.A2, "Desolate Sands"),
            new PoolAreaRectHint(70885u, 2640.0f, 4560.0f, 2880.0f, 4800.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 2880.0f, 4080.0f, 3120.0f, 4320.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 2880.0f, 4320.0f, 3120.0f, 4560.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 2880.0f, 4560.0f, 3120.0f, 4800.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 3120.0f, 4080.0f, 3360.0f, 4320.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 3120.0f, 4320.0f, 3360.0f, 4560.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 3360.0f, 4080.0f, 3600.0f, 4320.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 3360.0f, 4320.0f, 3600.0f, 4560.0f, 57425u, 0u, BountyAct.A2, "Dahlgur Oasis"),
            new PoolAreaRectHint(70885u, 2400.0f, 1440.0f, 2640.0f, 1680.0f, 19836u, 0u, BountyAct.A2, "Sundered Canyon"),
            new PoolAreaRectHint(70885u, 2640.0f, 1440.0f, 2880.0f, 1680.0f, 19836u, 0u, BountyAct.A2, "Sundered Canyon"),
            new PoolAreaRectHint(50585u, 960.0f, 480.0f, 1200.0f, 720.0f, 19787u, 0u, BountyAct.A1, "The Royal Crypts"),
            new PoolAreaRectHint(180550u, 480.0f, 960.0f, 720.0f, 1200.0f, 78572u, 0u, BountyAct.A1, "Caverns of Araneae"),
            new PoolAreaRectHint(71150u, 1020.0f, 3240.0f, 1260.0f, 3480.0f, 19943u, 0u, BountyAct.A1, "Leoric's Manor Courtyard"),
            new PoolAreaRectHint(50588u, 600.0f, 600.0f, 720.0f, 720.0f, 19791u, 0u, BountyAct.A2, "Sewers of Caldeum"),
            new PoolAreaRectHint(70885u, 960.0f, 960.0f, 1200.0f, 1200.0f, 19839u, 0u, BountyAct.A2, "Stinging Winds"),
            new PoolAreaRectHint(95804u, 1920.0f, 480.0f, 2160.0f, 720.0f, 69504u, 0u, BountyAct.A3, "Rakkis Crossing"),
            new PoolAreaRectHint(95804u, 2160.0f, 480.0f, 2400.0f, 720.0f, 69504u, 0u, BountyAct.A3, "Rakkis Crossing"),
            new PoolAreaRectHint(119641u, 720.0f, 720.0f, 1080.0f, 1080.0f, 119653u, 0u, BountyAct.A3, "Tower of the Cursed Level 1"),
            new PoolAreaRectHint(456029u, 970.0f, 1680.0f, 1210.0f, 1920.0f, 464886u, 0u, BountyAct.A4, "Upper Infernal Fate: Halls of Agony"),
            new PoolAreaRectHint(458965u, 620.0f, 120.0f, 860.0f, 360.0f, 464870u, 0u, BountyAct.A4, "Fractured Fate: The Festering Woods"),
            new PoolAreaRectHint(408254u, 240.0f, 480.0f, 360.0f, 600.0f, 427763u, 0u, BountyAct.A5, "Greyhollow Island"),
            new PoolAreaRectHint(374774u, 720.0f, 480.0f, 960.0f, 720.0f, 374773u, 0u, BountyAct.A5, "Realm of the Banished"),
            new PoolAreaRectHint(374774u, 960.0f, 480.0f, 1200.0f, 720.0f, 374773u, 0u, BountyAct.A5, "Realm of the Banished"),
            new PoolAreaRectHint(261712u, 240.0f, 240.0f, 480.0f, 480.0f, 261758u, 0u, BountyAct.A5, "Westmarch Commons"),
            new PoolAreaRectHint(458965u, 1240.0f, 600.0f, 1360.0f, 720.0f, 464873u, 0u, BountyAct.A4, "Fractured Fate: Battlefields of Eternity"),
            new PoolAreaRectHint(458965u, 240.0f, 590.0f, 480.0f, 830.0f, 464874u, 0u, BountyAct.A4, "Fractured Fate: Keep Depths"),
            new PoolAreaRectHint(2826u, 1200.0f, 240.0f, 1440.0f, 480.0f, 19774u, 0u, BountyAct.A1, "Halls of Agony Level 1"),
            new PoolAreaRectHint(2826u, 1440.0f, 480.0f, 1680.0f, 720.0f, 19774u, 0u, BountyAct.A1, "Halls of Agony Level 1"),
            new PoolAreaRectHint(50582u, 240.0f, 480.0f, 480.0f, 720.0f, 19783u, 0u, BountyAct.A1, "Cathedral Level 2"),
            new PoolAreaRectHint(95804u, 3120.0f, 240.0f, 3360.0f, 480.0f, 112565u, 0u, BountyAct.A3, "Fields of Slaughter"),
            new PoolAreaRectHint(338600u, 720.0f, 360.0f, 960.0f, 600.0f, 338602u, 0u, BountyAct.A5, "Battlefields of Eternity"),
            new PoolAreaRectHint(457461u, 480.0f, 510.0f, 720.0f, 750.0f, 464821u, 0u, BountyAct.A4, "Unbending Fate: The Banished"),
            new PoolAreaRectHint(71150u, 0.0f, 600.0f, 240.0f, 840.0f, 19953u, 0u, BountyAct.A1, "The Festering Woods"),
            new PoolAreaRectHint(71150u, 120.0f, 840.0f, 240.0f, 960.0f, 19953u, 0u, BountyAct.A1, "The Festering Woods"),
            new PoolAreaRectHint(71150u, 1020.0f, 3480.0f, 1260.0f, 3720.0f, 19943u, 0u, BountyAct.A1, "Leoric's Manor Courtyard"),
            new PoolAreaRectHint(180550u, 480.0f, 1200.0f, 720.0f, 1440.0f, 78572u, 0u, BountyAct.A1, "Caverns of Araneae"),
            new PoolAreaRectHint(58983u, 480.0f, 480.0f, 720.0f, 720.0f, 19776u, 0u, BountyAct.A1, "Halls of Agony Level 3"),
            new PoolAreaRectHint(50584u, 480.0f, 480.0f, 720.0f, 720.0f, 19785u, 0u, BountyAct.A1, "Cathedral Level 4"),
            new PoolAreaRectHint(456634u, 480.0f, 960.0f, 720.0f, 1200.0f, 456638u, 0u, BountyAct.A2, "Temple of the Firstborn Level 1"),
            new PoolAreaRectHint(79401u, 720.0f, 720.0f, 1080.0f, 1080.0f, 80791u, 0u, BountyAct.A3, "Tower of the Damned Level 1"),
            new PoolAreaRectHint(458965u, 1220.0f, 240.0f, 1460.0f, 480.0f, 464858u, 0u, BountyAct.A4, "Fractured Fate: Keep Depths"),
            new PoolAreaRectHint(71150u, 1980.0f, 1800.0f, 2100.0f, 1920.0f, 72712u, 0u, BountyAct.A1, "Cemetery of the Forsaken"),
            new PoolAreaRectHint(58982u, 240.0f, 720.0f, 480.0f, 960.0f, 19775u, 0u, BountyAct.A1, "Halls of Agony Level 2"),
            new PoolAreaRectHint(456634u, 720.0f, 720.0f, 960.0f, 960.0f, 456638u, 0u, BountyAct.A2, "Temple of the Firstborn Level 1"),
            new PoolAreaRectHint(95804u, 3360.0f, 480.0f, 3840.0f, 720.0f, 155048u, 0u, BountyAct.A3, "The Bridge of Korsikk"),
            new PoolAreaRectHint(428493u, 720.0f, 240.0f, 960.0f, 480.0f, 428494u, 0u, BountyAct.A3, "The Ruins of Sescheron"),
            new PoolAreaRectHint(428493u, 720.0f, 480.0f, 960.0f, 720.0f, 428494u, 0u, BountyAct.A3, "The Ruins of Sescheron"),
        };

        private class PoolAreaRectHint
        {
            public readonly uint WorldSno;
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;
            public readonly uint AreaSno;
            public readonly uint HostAreaSno;
            public readonly BountyAct Act;
            public readonly string AreaName;

            public PoolAreaRectHint(uint worldSno, float minX, float minY, float maxX, float maxY, uint areaSno, uint hostAreaSno, BountyAct act, string areaName)
            {
                WorldSno = worldSno;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
                AreaSno = areaSno;
                HostAreaSno = hostAreaSno;
                Act = act;
                AreaName = areaName;
            }
        }

        private class PoolAreaSnapshot
        {
            public uint AreaSno;
            public uint HostAreaSno;
            public BountyAct Act;
            public string AreaName;
        }

        private class ConsumedPoolSpot
        {
            public uint WorldId;
            public float X;
            public float Y;
        }

        private class PoolSpot
        {
            public string Key;
            public uint WorldId;
            public uint AreaSno;
            public uint HostAreaSno;
            public BountyAct Act;
            public IWorldCoordinate FloorCoordinate;
            public string AreaName;
            public string Label;
            public bool LabelUncertain;
            public bool LocationConfirmed;
            public long FirstSeenMs;
            public long FirstSeenOrder;
            public long LastSeenMs;
            public long LastArrowAreaVisitSequence;
        }

        private class PoolArrowAlert
        {
            public string Key;
            public float X;
            public float Y;
            public float Z;
            public uint WorldId;
            public uint AreaSno;
            public uint HostAreaSno;
            public long StartMs;
            public long ExpireMs;
            public float LastUx;
            public float LastUy;
            public bool TargetWasVisible;
            public long TargetVisibleSinceMs;
        }

        private class PartyWorldStatus
        {
            public bool Ready;
            public List<string> Lines;
        }

        private class VisitedArea
        {
            public BountyAct Act;
            public uint Sno;
            public string Name;
            public string PlayerName;
            public long LastSeenMs;
        }

        private class ItemMarker
        {
            public string Key;
            public string LiveKey;
            public int Rank;
            public uint Sno;
            public float X;
            public float Y;
            public float Z;
            public long FirstSeenMs;
            public long LastSeenMs;
            public long MissingSinceMs;
            public bool MissingNearPlayer;
            public long Sequence;
            public string Name;
            public string Label;
        }

        private class ItemAlert
        {
            public ItemMarker Marker;
            public string Text;
            public long Sequence;
            public long ElapsedMs;
            public int AlphaBucket;
            public IFont Font;
            public IFont OutlineFont;
            public IBrush ArrowBrush;
            public IBrush ArrowOutlineBrush;
            public IBrush ArrowHighlightBrush;
        }

        private class PlayerMark
        {
            public string Key;
            public int Slot;
        }
    }
}
