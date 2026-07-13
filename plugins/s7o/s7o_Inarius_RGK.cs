using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Inarius RGK helper: AutoSnap, selected-target rings, and Siphon Blood Power Shift automation.
    public class s7o_Inarius_RGK : BasePlugin, IAfterCollectHandler, INewAreaHandler, IInGameWorldPainter
    {
        private const uint InariusSetSno = 740281;
        private const int RequiredSetPieces = 6;

        #region Settings

        public int NormalStackTarget { get; private set; }
        public int PowerStackTarget { get; private set; }

        public bool AutoSnapEnabled = true;
        public bool AutoSiphonEnabled = true;
        public bool LateRefreshAssist = true;
        public bool PrioritizeDebuffedElites = true;
        public bool AggressiveScanMode = false;
        public bool IncludeTrashTargets = true;
        public bool RestoreCursorOnReleaseOrMove = true;
        public int InariusRangeYards = 17;
        public bool ShowRangeIndicator = true;
        public bool ShowTargetLineReticle = true;
        public bool JuggerHotkeyEnabled = true;
        public ushort JuggerHotkeyVirtualKey = 0x20; // Space
        public bool RgAutoSnapSiphonAssist = false;

        public ushort ForceStandstillVirtualKey = 0x10; // Shift
        public ushort Skill1VirtualKey = 0x31;          // 1
        public ushort Skill2VirtualKey = 0x32;          // 2
        public ushort Skill3VirtualKey = 0x33;          // 3
        public ushort Skill4VirtualKey = 0x34;          // 4

        private const int BuildPulseMs = 100;
        private const int LotdBuildPulseMs = 100;
        private const int MaintainPulseMs = 400;
        private const int LateRefreshWindowMsDefault = 800;
        private const int BossLateRefreshPulseMs = 400;
        private const int PowerShiftReadGraceTicks = 24;
        private const int ProactiveRefreshWindowMs = 1200;
        private const int PostMovementRefreshWindowMs = 4000;
        private const int PostLotdRefreshWindowMs = 4000;
        private const int PowerShiftBuffIconIndex = 10;
        private const int FirstEngageSiphonDelayTicks = 0;
        private const int LateRefreshIntentExpireTicks = 60;
        private const int LateRefreshIntentRetryTicks = 4;
        private const int LateRefreshIntentMaxAttempts = 6;
        private const float LateRefreshIntentRefreshGainSeconds = 0.35f;
        private const int PulseDownTicks = 2;
        private const int LateRefreshDownTicks = 4;
        private const int LotdPulseDownTicks = 6;
        private const int BossPulseDownTicks = 6;
        private const int CorpseScanIntervalTicks = 10;
        private const int SiphonAssistPauseTicks = 10;
        private const int CursorRestoreShortEngageTicks = 120; // HUD_MENU AutoSnap restore window (~2s at 60 tps)
        private const int DeadTargetRestoreAnchorTicks = 180;
        private const float CursorRestoreMinMovePxSq = 16f;
        private const int SkipUnhoverableLeaderTicks = 18;
        private const int SkipUnhoverableTrashTicks = 10;
        private const float InariusTargetRangeLeewayYards = 0.0f;
        private const float InariusHardTargetRangeYards = 17.0f;
        private const float InariusStrictCoreRangeYards = 15.0f;
        private const float InariusDistanceSourceVetoLeewayYards = 2.0f;
        private const float InariusHitboxVetoExtraYards = 1.25f;
        private const float RefreshSiphonFallbackRangeYards = 70.0f;
        private const float InariusVeryCloseTargetYards = 10.0f;
        private const float InariusCloseRetargetYards = 10.0f;
        private const float InariusCoreDamageYards = 15.0f;
        private const float InariusHitboxContactYards = 15.0f;
        private const float InariusOrangeBandMaxYards = 16.95f;
        private const float CorpseScanRangeYards = 120f;
        private const float JuggerLockCircleYards = 10f;
        private const float EliteLockCircleYards = 5f;
        private const int JuggerLockDashCount = 28;
        private const int EliteLockDashCount = 24;
        private const float JuggerLockDashFill = 0.46f;
        private const float EliteLockDashFill = 0.42f;
        private const double JuggerLockRotationSeconds = 6.5d;
        private const double EliteLockRotationSeconds = 4.75d;
        private const int NoTargetAnchorPulseMs = 400;
        private const int NoTargetAnchorDownTicks = 2;
        private const int NoTargetAnchorPulsesPerBurst = 2;
        private const int NoTargetAnchorMaxPulses = 3;
        private const int NoTargetAnchorBurstPauseMs = 500;
        private const float InariusEdgePenaltyStart = 15.8f;
        private const float InariusEdgePenaltyMax = 0.85f;
        private const float TrashPenaltyStart = 15.0f;
        private const float TrashEdgePenaltyMax = 2.35f;
        private const float EliteHealthPriorityWeight = 12.0f;
        private const float EliteDebuffPriorityBonus = 5.5f;
        private const float ElitePackDensityRangeYards = 22.0f;
        private const float ElitePackDensityPriorityEach = 0.75f;
        private const float ElitePackDensityPriorityMax = 3.0f;
        private const float EliteNearDistanceWeight = 0.70f;
        private const float EliteFarDistanceWeight = 0.36f;
        private const float LockedEliteStickinessBonus = 4.2f;
        private const float SiphonAnchorTrashPenalty = 8.0f;
        private const float BaselineRadiusBottom = 3.15f;
        private const float BaselineHitboxYards = 0.9f;
        private const float HitboxBonusCap = 2.0f;
        private const uint BoneArmorPowerSnoFallback = 466857u;
        private const uint InariusSaintEnemyPowerSnoFallback = 468602u;
        private const float BaseHeadPx = 65f;
        private const float BaseShoulderPx = 50f;
        private const float BaseChestPx = 35f;
        private const float BaseAbdomenPx = 18f;
        private const float BaseShoulderXPx = 25f;
        private const double BigTrashMinRiftProgression = 0.95d; // occlusion/probe threshold; target scoring uses progression only after strict Inarius range gating
        private const double InariusHighValueTrashProgression = 0.65d;
        private const double InariusMediumValueTrashProgression = 0.45d;
        private const float InariusLastBlueEliteBonus = 22000f;

        private const int AdaptiveBinCount = 8;
        private const int AdaptiveCandidateCapacity = 128;
        private const int ProbeZoneCount = 9;
        private const int MinTicksBetweenMouseMoves = 1;
        private const int NormalScanParkAfterTicks = 16;
        private const int NormalScanParkReprobeTicks = 18;
        private const int StableLockTicks = 16;
        private const int SoftLockTicks = 12;
        private const int ReacquireWindowTicks = 16;
        private const int FailedLeaderRetryWindowTicks = 20;
        private const int AlternateScanWindowTicks = 18;
        private const int SkipLeaderWindowTicks = 10;
        private const float TeleportThresholdSq = 100f;
        private const int PostTeleportForceSnapTicks = 15;
        private const int ForcedInariusRetargetTicks = 18;
        private const int ManualTargetLingerTicks = 180;
        private const int MovementDisengageTicks = 8;
        private const int MovementDisengageAfterCursorOverrideTicks = 4;
        private const int ManualCursorOverridePauseTicks = 18;
        private const int MovementStopSiphonPulseMs = 70;
        private const int MovementStopSiphonDownTicks = 3;
        private const int MovementStopSiphonRetryTicks = 4;
        private const int UiClickGuardRetryTicks = 6;
        private const float HudMenuButtonReferenceWidth = 1920f;
        private const float HudMenuButtonReferenceHeight = 1080f;
        private const float HudMenuButtonDefaultX = 1452f;
        private const float HudMenuButtonDefaultY = 1018f;
        private const float HudMenuButtonDefaultSize = 60f;
        private const float HudMenuButtonGuardPadPx = 8f;
        private const float AutoLootButtonDefaultX = 450f;
        private const float AutoLootButtonDefaultY = 1027f;
        private const float AutoLootButtonDefaultSize = 17f;
        private const float AutoLootButtonGuardPadPx = 14f;
        private const float SiphonUiGuardReferenceWidth = 1920f;
        private const float SiphonUiGuardReferenceHeight = 1080f;

        // Global no-hover guard for the bottom skillbar / skill tooltip region.
        // This prevents autosnap, probe scans, cursor restore, and Siphon pulse anchors
        // from parking the cursor on skills or nearby skillbar buttons.
        private const float SkillbarHoverGuardLeft = 610f;
        private const float SkillbarHoverGuardRight = 1325f;
        private const float SkillbarHoverGuardTop = 875f;
        private const float SkillbarHoverGuardBottom = 1080f;
        private const float SiphonBottomRightGuardLeft = 1768f;
        private const float SiphonBottomRightGuardRight = 1918f;
        private const float SiphonBottomRightGuardTop = 950f;
        private const float SiphonBottomRightGuardBottom = 1080f;

        private const float SiphonBottomCenterGuardLeft = 1000f;
        private const float SiphonBottomCenterGuardRight = 1310f;
        private const float SiphonBottomCenterGuardTop = 930f;

        private const float SiphonBottomLeftGuardRight = 92f;
        private const float SiphonBottomLeftGuardTop = 980f;

        private const float SiphonChatWatchRight = 620f;
        private const float SiphonChatWatchTop = 690f;

        private const float SiphonTopRightPanelGuardLeft = 1840f;
        private const float SiphonTopRightPanelGuardRight = 1912f;
        private const float SiphonTopRightPanelGuardTop = 338f;
        private const float SiphonTopRightPanelGuardBottom = 410f;

        private const float SiphonTopRightIconGuardLeft = 1628f;
        private const float SiphonTopRightIconGuardRight = 1712f;
        private const float SiphonTopRightIconGuardTop = 10f;
        private const float SiphonTopRightIconGuardBottom = 64f;

        private const float SiphonParagonPlusGuardLeft = 895f;
        private const float SiphonParagonPlusGuardRight = 1005f;
        private const float SiphonParagonPlusGuardTop = 850f;
        private const float SiphonParagonPlusGuardBottom = 970f;

        private const float SiphonSocialFlyoutGuardLeft = 1768f;
        private const float SiphonSocialFlyoutGuardRight = 1918f;
        private const float SiphonSocialFlyoutGuardTop = 950f;
        private const float SiphonSocialFlyoutGuardBottom = 1080f;

        private const float SiphonTopLeftPortraitGuardWidth = 245f;
        private const float SiphonTopLeftPortraitGuardHeight = 155f;
        private const int OwnedUiClickCloseWindowTicks = 30;
        private const int OwnedUiClickCloseRetryTicks = 8;
        private const ushort EscapeVirtualKey = 0x1B;
        private const int PostEliteClearPauseMs = 100;
        private const int FastBuildFuseMs = 2500;
        private const int FastBuildFusePulses = 25;
        private const int FastBuildThrottleMs = 1600;
        private const float PlayerMoveIntentThresholdSq = 0.030f;
        private const float UserCursorOverrideThresholdSq = 900f;
        private const int UserCursorOverrideMinTicks = 1;
        private const int UserCursorOverrideMaxTicks = 45;
        private const int BadHoverInvalidateTicks = 2;
        private const int HoverTruthRecentTicks = 90;
        private const uint PowerPylonBuffSno = 262935u; // Generic_PagesBuffDamage

        // Ported from the original LightningMod selector. FreeHUD exposes most of these fields natively,
        // but pack/affix state is not always surfaced consistently for minions and shielding packs.
        private static readonly string[] PackPropNames = { "MonsterPack", "Pack", "RiftMonsterPack", "ElitePack", "MonsterPackInfo" };
        private static readonly string[] PackIdPropNames = { "Id", "PackId", "MonsterPackId", "RiftMonsterPackId", "ElitePackId" };
        private static readonly string[] AffixPropNames = { "AffixSnoList", "Affixes", "AffixList" };
        private static readonly Dictionary<Type, PropertyInfo> PackPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> PackIdPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> AffixListPropCache = new Dictionary<Type, PropertyInfo>(16);

        #endregion

        #region Runtime State

        private uint _snoCorpseLance;
        private uint _snoSiphonBlood;
        private uint _snoBoneArmor;
        private uint _snoInariusSaintEnemy;
        private uint _snoLandOfTheDead;

        private ActionKey _lanceKey = ActionKey.Unknown;
        private ActionKey _siphonKey = ActionKey.Unknown;
        private bool _lanceKeyKnown;
        private bool _siphonKeyKnown;

        private bool _pulseActive;
        private bool _siphonPulseOwned;
        private bool _standstillOwned;
        private bool _pulseWasBuild;
        private int _pulseBuildTarget;
        private int _pulseDownUntilTick;
        private int _nextPulseTick;
        private bool _lateRefreshPulseConsumed;
        private int _siphonAssistUntilTick;
        private int _fastBuildStartTick;
        private int _fastBuildPulseCount;
        private int _fastBuildTarget;
        private int _fastBuildBestStacks;
        private int _fastBuildThrottleUntilTick;
        private int _lastPowerShiftReadTick;
        private int _lastPowerShiftStacks;
        private float _lastPowerShiftTimeLeft;

        private uint _lockedTargetAcdId;
        private int _lockedTargetKeepUntilTick;
        private const int LockedTargetKeepTicks = 600; // ~10s at 60 tps; mirrors LM leader lock persistence.
        private uint _returnToRareAcdId;
        private uint _skipAcdId;
        private int _skipUntilTick;
        private uint _snapPhaseAcdId;
        private int _snapPhase;
        private uint _normalScanParkAcdId;
        private int _normalScanParkStartTick;
        private int _normalScanLastReprobeTick;
        private int _lastMouseMoveTick;
        private uint _forcedInariusSnapAcdId;
        private int _forcedInariusSnapUntilTick;
        private bool _cursorOwned;
        private bool _cursorWasMovedByPlugin;
        private bool _haveSavedCursor;
        private float _savedCursorX;
        private float _savedCursorY;
        private int _engageStartTick;
        private uint _targetRestoreAnchorAcdId;
        private float _targetRestoreAnchorX;
        private float _targetRestoreAnchorY;
        private int _targetRestoreAnchorTick;
        private uint _lastHoverAcdId;
        private int _lastHoverTick;
        private float _lastHoverDx;
        private float _lastHoverDy;

        private readonly float[] _rankedProbeDx = new float[AdaptiveCandidateCapacity];
        private readonly float[] _rankedProbeDy = new float[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeBin = new int[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeZone = new int[AdaptiveCandidateCapacity];
        private readonly float[] _adaptiveBinGood = new float[AdaptiveBinCount];
        private readonly float[] _adaptiveBinBad = new float[AdaptiveBinCount];
        private int _rankedProbeCount;
        private uint _lastSnapAttemptAcd;
        private int _lastSnapAttemptBin = -1;
        private int _lastSnapAttemptZone = -1;
        private int _lastSnapAttemptTick;
        private uint _probeZoneAcdId;
        private readonly int[] _probeZoneLastTryTick = new int[ProbeZoneCount];
        private readonly int[] _probeZoneTryCount = new int[ProbeZoneCount];
        private uint _probeZoneSuccessAcdId;
        private int _lastSuccessProbeZone = -1;
        private int _lastSuccessProbeZoneTick;
        private uint _stableLockAcdId;
        private float _stableLockDx;
        private float _stableLockDy;
        private int _stableLockUntilTick;
        private uint _softLockAcdId;
        private float _softLockDx;
        private float _softLockDy;
        private int _softLockUntilTick;
        private uint _cachedHoverAcdId;
        private float _cachedHoverDx;
        private float _cachedHoverDy;
        private int _cachedHoverUntilTick;
        private int _cachedHoverTryUntilTick;
        private uint _reacquireAcdId;
        private int _reacquireUntilTick;
        private uint _unhoverableAcdId;
        private int _unhoverableUntilTick;
        private uint _lastFailedLeaderAcdId;
        private int _lastFailedLeaderTick;
        private int _lastFailedLeaderCount;
        private uint _alternateScanAcdId;
        private int _alternateScanUntilTick;
        private uint _skipLeaderAcdId;
        private int _skipLeaderUntilTick;

        private uint _solverProfileAcd;
        private float _solverOccLeft, _solverOccRight, _solverOccUp, _solverOccDown;
        private float _solverBigLeft, _solverBigRight, _solverBigUp, _solverBigDown;
        private float _solverBlockerCx, _solverBlockerCy;
        private float _solverBlockerWeightTotal;
        private int _solverBlockerCount;

        private float _lastMeX;
        private float _lastMeY;
        private bool _haveLastMePos;
        private int _teleportDetectedTick;
        private bool _forceMoveWasDown;
        private bool _lastLotdActive;
        private bool _siphonKeyWasDown;
        private bool _lanceWasDown;
        private bool _engageRefreshConsumed;
        private uint _lastSiphonTargetAcdId;
        private uint _manualJuggerLockAcdId;
        private int _manualTargetUntilTick;
        private uint _badHoverAcdId;
        private int _badHoverCount;
        private int _badHoverLastTick;
        private int _movementDisengageUntilTick;
        private int _manualCursorOverrideUntilTick;
        private int _lastMovementStopSiphonTick;
        private float _lastPluginCursorX;
        private float _lastPluginCursorY;
        private int _lastPluginCursorMoveTick;
        private int _lastLiveLeaderCount;
        private int _postEliteClearPauseUntilTick;
        private bool _juggerHotkeyWasDown;
        private IBrush _juggerCircleBrush;
        private IBrush _juggerCircleOutlineBrush;
        private IBrush _eliteCircleBrush;
        private IBrush _eliteCircleOutlineBrush;
        private IBrush _inariusRangeOutlineBrush;
        private IBrush _inariusRangeGreenBrush;
        private IBrush _inariusRangeOrangeBrush;
        private IBrush _inariusRangeRedBrush;
        private IBrush _inariusRangeLineOutlineBrush;
        private IBrush _inariusRangeGreenLineBrush;
        private IBrush _inariusRangeOrangeLineBrush;
        private IBrush _inariusRangeRedLineBrush;
        private IBrush _inariusLockDotInRangeBrush;
        private IBrush _inariusLockDotToleranceBrush;
        private IBrush _inariusLockDotOutOfRangeBrush;
        private IBrush _inariusLockReticleInRangeBrush;
        private IBrush _inariusLockReticleToleranceBrush;
        private IBrush _inariusLockReticleOutOfRangeBrush;
        private uint _targetSwitchRefreshAcdId;
        private bool _lateRefreshIntentActive;
        private bool _lateRefreshIntentFromEngage;
        private int _lateRefreshIntentReadyTick;
        private int _lateRefreshIntentExpireTick;
        private int _lateRefreshIntentAttempts;
        private int _lateRefreshIntentNextAttemptTick;
        private float _lateRefreshIntentStartTimeLeft;
        private int _lateRefreshIntentWindowMs;
        private int _noTargetAnchorPulses;
        private int _noTargetAnchorLastTick;
        private bool _lastPulseWasNoTargetAnchor;

        private int _lastCorpseScanTick;
        private bool _cachedCorpsesAvailable;
        private int _lastSeenGameTick;

        private IUiElement _chatEditLine;
        private int _lastOwnedMouseSiphonPulseTick;
        private int _lastOwnedChatRiskPulseTick;
        private int _lastSiphonPulseTick;
        private int _lastOwnedUiCloseTick;
        private readonly List<IUiElement> _clickDangerUiElements = new List<IUiElement>();

        #endregion

        public s7o_Inarius_RGK()
        {
            Enabled = false;
            Order = 65000;
            NormalStackTarget = 8;
            PowerStackTarget = 6;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            try { _snoCorpseLance = hud.Sno.SnoPowers.Necromancer_CorpseLance.Sno; } catch { _snoCorpseLance = 461650u; }
            try { _snoSiphonBlood = hud.Sno.SnoPowers.Necromancer_SiphonBlood.Sno; } catch { _snoSiphonBlood = 453563u; }
            try { _snoBoneArmor = hud.Sno.SnoPowers.Necromancer_BoneArmor.Sno; } catch { _snoBoneArmor = BoneArmorPowerSnoFallback; }
            try { _snoInariusSaintEnemy = hud.Sno.SnoPowers.Generic_p6SetDungNecroSaintEnmy.Sno; } catch { _snoInariusSaintEnemy = InariusSaintEnemyPowerSnoFallback; }
            try { _snoLandOfTheDead = hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno; } catch { _snoLandOfTheDead = 465839u; }

            try { _chatEditLine = Hud.Render.GetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline"); } catch { }
            RegisterClickDangerUiElements();
            try
            {
                _juggerCircleOutlineBrush = Hud.Render.CreateBrush(235, 0, 0, 0, 7.0f);
                _juggerCircleBrush = Hud.Render.CreateBrush(245, 255, 132, 0, 3.4f);
                _eliteCircleOutlineBrush = Hud.Render.CreateBrush(230, 0, 0, 0, 6.0f);
                _eliteCircleBrush = Hud.Render.CreateBrush(245, 0, 255, 90, 3.0f);
                _inariusRangeOutlineBrush = Hud.Render.CreateBrush(210, 0, 0, 0, 5.2f);
                _inariusRangeGreenBrush = Hud.Render.CreateBrush(235, 0, 235, 70, 3.0f);
                _inariusRangeOrangeBrush = Hud.Render.CreateBrush(235, 255, 150, 0, 3.0f);
                _inariusRangeRedBrush = Hud.Render.CreateBrush(235, 255, 50, 40, 3.0f);
                _inariusRangeLineOutlineBrush = Hud.Render.CreateBrush(205, 0, 0, 0, 5.0f);
                _inariusRangeGreenLineBrush = Hud.Render.CreateBrush(245, 0, 255, 80, 2.4f);
                _inariusRangeOrangeLineBrush = Hud.Render.CreateBrush(245, 255, 165, 0, 2.4f);
                _inariusRangeRedLineBrush = Hud.Render.CreateBrush(245, 255, 55, 45, 2.4f);
                _inariusLockDotInRangeBrush = Hud.Render.CreateBrush(248, 255, 45, 35, 5.0f);
                _inariusLockDotToleranceBrush = Hud.Render.CreateBrush(248, 255, 45, 35, 5.0f);
                _inariusLockDotOutOfRangeBrush = Hud.Render.CreateBrush(248, 255, 45, 35, 5.0f);
                _inariusLockReticleInRangeBrush = Hud.Render.CreateBrush(235, 40, 255, 80, 1.45f);
                _inariusLockReticleToleranceBrush = Hud.Render.CreateBrush(235, 255, 145, 25, 1.45f);
                _inariusLockReticleOutOfRangeBrush = Hud.Render.CreateBrush(235, 255, 45, 35, 1.45f);
            }
            catch { }
        }


        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int inariusRangeYards)
        {
            Configure(normalStacks, powerStacks, AutoSnapEnabled, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, inariusRangeYards, JuggerHotkeyEnabled, JuggerHotkeyVirtualKey, RgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int inariusRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist)
        {
            Configure(normalStacks, powerStacks, AutoSnapEnabled, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, inariusRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int inariusRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist, bool ignoredLegacyOption)
        {
            Configure(normalStacks, powerStacks, autoSnap, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, inariusRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int inariusRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist, bool ignoredLegacyOption, int ignoredLegacyIntervalMs)
        {
            Configure(normalStacks, powerStacks, autoSnap, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, inariusRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int inariusRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist)
        {
            NormalStackTarget = ClampStackTarget(normalStacks);
            PowerStackTarget = ClampStackTarget(powerStacks);
            AutoSnapEnabled = autoSnap;
            AutoSiphonEnabled = autoSiphon;
            LateRefreshAssist = lateRefreshAssist;
            PrioritizeDebuffedElites = prioritizeDebuffedElites;
            AggressiveScanMode = aggressiveScanMode;
            IncludeTrashTargets = includeTrashTargets;
            RestoreCursorOnReleaseOrMove = restoreCursorOnReleaseOrMove;
            InariusRangeYards = ClampRange(inariusRangeYards);
            JuggerHotkeyEnabled = juggerHotkeyEnabled;
            JuggerHotkeyVirtualKey = juggerHotkeyVirtualKey;
            RgAutoSnapSiphonAssist = rgAutoSnapSiphonAssist;
            if (!AutoSnapEnabled)
                ClearAutosnapLockState();
            if (!JuggerHotkeyEnabled || JuggerHotkeyVirtualKey == 0)
                ClearManualJuggerLock();
        }

        public void SetStackTargets(int normal, int power)
        {
            Configure(normal, power, AutoSnapEnabled, AutoSiphonEnabled, LateRefreshAssist, PrioritizeDebuffedElites, AggressiveScanMode, IncludeTrashTargets, RestoreCursorOnReleaseOrMove, InariusRangeYards, JuggerHotkeyEnabled, JuggerHotkeyVirtualKey, RgAutoSnapSiphonAssist);
        }

        public void ForceStopForDisable()
        {
            ResetRuntime(true);
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ResetRuntime(true);
            ClearManualJuggerLock();
        }

        public void AfterCollect()
        {
            try
            {
                int tick = 0;
                try { tick = Hud.Game.CurrentGameTick; } catch { }
                if (tick > 0)
                    TryCloseChatOpenedByOwnedPulse(tick);

                if (!CanRun())
                {
                    ResetRuntime(true);
                    return;
                }

                tick = Hud.Game.CurrentGameTick;
                if (DetectStaleSessionReset(tick))
                    return;

                ProcessPendingCursorRestore(tick);
                UpdatePlayerMotionState(tick);
                UpdatePassiveEliteHoverCache(tick);
                ClearDeadTargetState(tick);

                if (!ResolveSkillKeys())
                {
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                    ResetRuntime(true);
                    return;
                }

                bool releaseWasPulseActive = _pulseActive || _standstillOwned;
                TickPulseReleaseIfNeeded(tick);

                bool lanceHeld = IsActionPhysicallyDown(_lanceKey);
                ProcessJuggerHotkey(tick);
                if (lanceHeld)
                    RefreshManualEliteLock(tick);
                if (lanceHeld && !_lanceWasDown)
                    _movementDisengageUntilTick = 0;
                bool forceMoving = IsForceMoving();

                if (!lanceHeld)
                {
                    DisengageOnLanceRelease(tick, forceMoving, releaseWasPulseActive);
                    return;
                }

                bool movedIntoEngagement = _forceMoveWasDown;
                bool newLanceEngagement = !_lanceWasDown || movedIntoEngagement;
                _lanceWasDown = true;

                if (forceMoving)
                {
                    if (!TryStopMovementWithSiphon(tick) && !_pulseActive)
                    {
                        ReleaseStandstillIfOwned();
                        QueueCursorRestore(tick);
                        ProcessPendingCursorRestore(tick);
                    }

                    _forceMoveWasDown = true;
                    return;
                }
                _forceMoveWasDown = false;

                if (newLanceEngagement)
                {
                    RefreshCursorRestoreWindowForFreshEngagement(tick);
                    ResumeAutosnapControlOnEngagement(tick);
                }

                CorrectOutOfRangeSelectedTarget(tick);

                if (ApplyPostEliteClearPause(tick, newLanceEngagement))
                    return;

                bool manualSiphonDown = _siphonKeyKnown && _siphonKey != _lanceKey && !_siphonPulseOwned && IsActionPhysicallyDown(_siphonKey);
                if (manualSiphonDown)
                {
                    _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);
                    _siphonKeyWasDown = true;
                }
                else if (_siphonKeyWasDown)
                {
                    _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);
                    _siphonKeyWasDown = false;
                }

                if (TryCriticalPowerShiftRefreshPulse(tick))
                    return;

                if (TryRunLateRefreshIntent(tick))
                    return;

                bool delayInitialAutosnapForRefresh = ShouldDelayInitialAutosnapForLateRefresh(newLanceEngagement, manualSiphonDown, movedIntoEngagement);
                if (delayInitialAutosnapForRefresh)
                {
                    QueueLateRefreshIntent(tick, true, movedIntoEngagement);
                    return;
                }

                IMonster target = null;
                IMonster siphonTarget = null;
                IMonster manualTarget = GetManualJuggerLockTarget();
                IMonster bossTarget = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget() : null;
                IMonster forcedTarget = IsAliveTarget(manualTarget) ? manualTarget : bossTarget;

                if (IsAliveTarget(forcedTarget))
                {
                    target = forcedTarget;
                    siphonTarget = target;
                    if (AutoSnapEnabled)
                        TrySnap(target, tick);
                }
                else if (!BossAlive())
                {
                    if (AutoSnapEnabled)
                    {
                        target = AcquireTarget(tick);
                        if (IsAliveTarget(target))
                        {
                            siphonTarget = target;
                            TrySnap(target, tick);
                        }
                        else
                        {
                            TryAcquireHoverForAutoSiphon(tick, out siphonTarget);
                        }
                    }
                    else
                    {
                        ClearAutosnapLockState();
                        siphonTarget = GetManualSiphonTargetFallback();
                    }
                }
                else
                {
                    // RG assist obeys the split toggles: AutoSnap moves only when enabled; AutoSiphon/refresh/pulse still choose a siphon target.
                    ClearAutosnapLockState();
                    siphonTarget = IsAliveTarget(bossTarget) ? bossTarget : GetManualSiphonTargetFallback();
                }

                if (!IsAliveTarget(siphonTarget))
                    siphonTarget = GetManualSiphonTargetFallback();

                if (!IsAliveTarget(target) && !IsAliveTarget(siphonTarget) && AnyEliteOrMinionCandidateExists() )
                {
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                }

                RunAutoSiphon(tick, siphonTarget, newLanceEngagement);
            }
            catch
            {
                ResetRuntime(true);
            }
        }


        private void ResumeAutosnapControlOnEngagement(int tick)
        {
            // A fresh Corpse Lance press is an explicit request to resume autosnap control.
            // This prevents a post-teleport/manual-cursor pause from outliving the user's
            // re-engagement and delaying the first corrective snap.
            _manualCursorOverrideUntilTick = 0;
            _movementDisengageUntilTick = 0;
            _lastMouseMoveTick = 0;

            IMonster target = PickImmediateInariusReengageTarget();
            if (!IsAliveTarget(target))
                return;

            uint acd = GetMonsterAcdId(target);
            if (acd == 0)
                return;

            PrepareImmediateInariusRetarget(target, tick);
            LockTarget(target, tick);
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
            TrySnap(target, tick);
        }

        private IMonster PickImmediateInariusReengageTarget()
        {
            IMonster target = GetManualJuggerLockTarget();
            if (IsInariusImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_lockedTargetAcdId);
            if (IsInariusImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_reacquireAcdId);
            if (IsInariusImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_stableLockAcdId);
            if (IsInariusImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_cachedHoverAcdId);
            if (IsInariusImmediateReengageTarget(target))
                return target;

            try { target = Hud.Game.SelectedMonster2; } catch { target = null; }
            if (IsInariusImmediateReengageTarget(target))
                return target;

            return null;
        }

        private bool IsInariusImmediateReengageTarget(IMonster monster)
        {
            if (!IsAliveTarget(monster) || !IsWithinInariusTargetRange(monster))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            return IsLeader(monster) || IsEliteMinionLike(monster);
        }

        private void DisengageOnLanceRelease(int tick, bool forceMoving, bool pulseWasActiveAtStart)
        {
            bool escapeRelease = forceMoving || _forceMoveWasDown || pulseWasActiveAtStart || _pulseActive || _standstillOwned || tick <= _movementDisengageUntilTick;

            StopPulseNow();

            if (escapeRelease)
            {
                TryRestoreCursorImmediately(tick, true);
            }
            else
            {
                QueueCursorRestore(tick);
                ProcessPendingCursorRestore(tick);
            }

            ResetRuntime(false);
        }

        private bool ApplyPostEliteClearPause(int tick, bool newLanceEngagement)
        {
            int liveLeaders = CountLiveLeaderCandidates();

            if (newLanceEngagement)
            {
                _lastLiveLeaderCount = liveLeaders;
                _postEliteClearPauseUntilTick = 0;
                return false;
            }

            if (_lastLiveLeaderCount > 0 && liveLeaders == 0)
            {
                _postEliteClearPauseUntilTick = Math.Max(_postEliteClearPauseUntilTick, tick + MsToTicks(PostEliteClearPauseMs));
                ClearAutosnapLockState();
                StopPulseNow();
                QueueCursorRestore(tick);
                ProcessPendingCursorRestore(tick);
            }

            _lastLiveLeaderCount = liveLeaders;

            if (tick > _postEliteClearPauseUntilTick)
                return false;

            ClearAutosnapLockState();
            ReleaseStandstillIfOwned();
            QueueCursorRestore(tick);
            ProcessPendingCursorRestore(tick);
            return true;
        }

        private void TryCloseChatOpenedByOwnedPulse(int tick)
        {
            try
            {
                if (_lastOwnedChatRiskPulseTick <= 0)
                    return;

                if (tick - _lastOwnedChatRiskPulseTick > OwnedUiClickCloseWindowTicks)
                    return;

                if (tick - _lastOwnedUiCloseTick < OwnedUiClickCloseRetryTicks)
                    return;

                if (!IsUiVisible(_chatEditLine))
                    return;

                StopPulseNow();
                SendKeyDown(EscapeVirtualKey);
                SendKeyUp(EscapeVirtualKey);
                _lastOwnedUiCloseTick = tick;
            }
            catch { }
        }

        private bool CanRun()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Window == null)
                return false;

            if (!Hud.Window.IsForeground || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.IsInTown)
                return false;

            if (Hud.Game.MapMode != MapMode.Minimap && Hud.Game.MapMode != MapMode.PermaMap)
                return false;

            if (IsUiVisible(_chatEditLine))
                return false;

            try
            {
                if (Hud.Inventory != null && IsUiVisible(Hud.Inventory.InventoryMainUiElement))
                    return false;
            }
            catch { }

            var me = Hud.Game.Me;
            if (me == null || me.IsDead || me.Powers == null || me.HeroClassDefinition == null)
                return false;

            if (me.HeroClassDefinition.HeroClass != HeroClass.Necromancer)
                return false;

            return HasRequiredSet(me);
        }

        private bool HasRequiredSet(IPlayer me)
        {
            try { return me != null && me.GetSetItemCount(InariusSetSno) >= RequiredSetPieces; }
            catch { return false; }
        }

        private bool IsUiVisible(IUiElement element)
        {
            try { return element != null && element.Visible; }
            catch { return false; }
        }


        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Ground || Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            try { DrawInariusRangeIndicator(); } catch { }

            IMonster locked = GetManualJuggerLockTarget();
            if (!IsAliveTarget(locked) || locked.FloorCoordinate == null)
                return;

            try
            {
                DrawManualLockCircle(locked);
            }
            catch { }
        }

        private void DrawInariusRangeIndicator()
        {
            if (!ShowRangeIndicator || Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return;

            if (!IsInariusRangeIndicatorEngaged())
                return;

            IWorldCoordinate me = Hud.Game.Me.FloorCoordinate;
            IMonster target = GetInariusRangeIndicatorTarget();
            bool hasIndicatorTarget = IsAliveForIndicator(target);
            bool inCoreRange = hasIndicatorTarget && IsInsideInariusCoreDamageRadius(target);
            bool inToleranceRange = hasIndicatorTarget && !inCoreRange && IsInsideInariusDamageRadius(target);
            bool drawRed = hasIndicatorTarget && !inCoreRange && !inToleranceRange;

            IBrush circleBrush = _inariusRangeGreenBrush;
            if (inToleranceRange)
                circleBrush = _inariusRangeOrangeBrush;
            else if (drawRed)
                circleBrush = _inariusRangeRedBrush;

            DrawWorldCircle(me, InariusPick(), _inariusRangeOutlineBrush);
            DrawWorldCircle(me, InariusPick(), circleBrush);

            if (!hasIndicatorTarget || target.FloorCoordinate == null)
                return;

            if (!ShowTargetLineReticle)
                return;

            IBrush line = _inariusRangeGreenLineBrush;
            if (inToleranceRange)
                line = _inariusRangeOrangeLineBrush;
            else if (drawRed)
                line = _inariusRangeRedLineBrush;

            if (_inariusRangeLineOutlineBrush != null)
                _inariusRangeLineOutlineBrush.DrawLineWorld(me, target.FloorCoordinate);
            if (line != null)
                line.DrawLineWorld(me, target.FloorCoordinate);

            DrawInariusLockReticle(target, inCoreRange, inToleranceRange);
        }

        private void DrawInariusLockReticle(IMonster target, bool inCoreRange, bool inToleranceRange)
        {
            if (!IsAliveForIndicator(target) || target.FloorCoordinate == null)
                return;

            try
            {
                var screen = target.FloorCoordinate.ToScreenCoordinate();
                float cx = screen.X;
                float cy = screen.Y - 4f;
                float pulse = (float)Math.Sin(Hud.Game.CurrentGameTick * 0.34f) * 1.2f;
                float radius = 5.0f + pulse;
                if (radius < 2.5f) radius = 2.5f;

                IBrush dot = inCoreRange ? _inariusLockDotInRangeBrush : (inToleranceRange ? _inariusLockDotToleranceBrush : _inariusLockDotOutOfRangeBrush);
                IBrush reticle = inCoreRange ? _inariusLockReticleInRangeBrush : (inToleranceRange ? _inariusLockReticleToleranceBrush : _inariusLockReticleOutOfRangeBrush);

                if (dot != null)
                {
                    dot.DrawEllipse(cx, cy, radius, radius);
                    dot.DrawEllipse(cx, cy, radius * 0.72f, radius * 0.72f);
                    dot.DrawEllipse(cx, cy, radius * 0.46f, radius * 0.46f);
                }

                if (reticle != null)
                    reticle.DrawEllipse(cx, cy, radius + 4.2f, radius + 4.2f);
            }
            catch { }
        }

        private bool IsInariusRangeIndicatorEngaged()
        {
            if (!Enabled)
                return false;

            try
            {
                int tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0;
                if (_lanceWasDown || _pulseActive || _lateRefreshIntentActive || tick < _siphonAssistUntilTick)
                    return true;

                return _lanceKeyKnown && IsActionPhysicallyDown(_lanceKey);
            }
            catch { return false; }
        }

        private IMonster GetInariusRangeIndicatorTarget()
        {
            // The indicator is a physical Inarius damage-range validation aid. Use a looser
            // liveness check here than normal targeting so a far/invalid selected monster still
            // turns the player ring red instead of silently falling back to green.
            IMonster manual = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            if (IsAliveForIndicator(manual))
                return manual;

            IMonster locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
            if (IsAliveForIndicator(locked) && IsInsideInariusDamageRadius(locked))
                return locked;

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }
            if (IsAliveForIndicator(selected))
                return selected;

            if (IsAliveForIndicator(locked))
                return locked;

            return null;
        }

        private bool IsAliveForIndicator(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;
            }
            catch { return false; }

            try
            {
                if (monster.FloorCoordinate == null)
                    return false;
            }
            catch { return false; }

            return true;
        }

        private void DrawWorldCircle(IWorldCoordinate center, float radius, IBrush brush)
        {
            if (center == null || brush == null || radius <= 0f)
                return;

            const int segments = 96;
            double full = Math.PI * 2.0d;
            double step = full / segments;
            IWorldCoordinate prev = center.Offset(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                double a = i * step;
                IWorldCoordinate next = center.Offset(radius * (float)Math.Cos(a), radius * (float)Math.Sin(a), 0);
                brush.DrawLineWorld(prev, next);
                prev = next;
            }
        }

        private void DrawManualLockCircle(IMonster monster)
        {
            if (monster == null || monster.FloorCoordinate == null)
                return;

            bool jugger = IsJuggernautPack(monster);
            float radius = jugger ? JuggerLockCircleYards : EliteLockCircleYards;
            int dashCount = jugger ? JuggerLockDashCount : EliteLockDashCount;
            float dashFill = jugger ? JuggerLockDashFill : EliteLockDashFill;
            double secondsPerTurn = Math.Max(1.0d, jugger ? JuggerLockRotationSeconds : EliteLockRotationSeconds);
            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawDashedWorldCircle(monster.FloorCoordinate, radius, dashCount, dashFill, phase, jugger ? _juggerCircleOutlineBrush : _eliteCircleOutlineBrush);
            DrawDashedWorldCircle(monster.FloorCoordinate, radius, dashCount, dashFill, phase, jugger ? _juggerCircleBrush : _eliteCircleBrush);
        }

        private void DrawDashedWorldCircle(IWorldCoordinate center, float radius, int dashCount, float dashFill, double phase, IBrush brush)
        {
            if (center == null || brush == null || radius <= 0f || dashCount <= 0)
                return;

            double full = Math.PI * 2.0d;
            double step = full / dashCount;
            double dashAngle = step * Math.Max(0.10d, Math.Min(0.90d, dashFill));

            for (int i = 0; i < dashCount; i++)
            {
                double a0 = phase + i * step;
                double a1 = a0 + dashAngle;

                var p0 = center.Offset(radius * (float)Math.Cos(a0), radius * (float)Math.Sin(a0), 0);
                var p1 = center.Offset(radius * (float)Math.Cos(a1), radius * (float)Math.Sin(a1), 0);

                brush.DrawLineWorld(p0, p1);
            }
        }

        private bool DetectStaleSessionReset(int tick)
        {
            if (tick <= 0)
            {
                ResetRuntime(true);
                _lastSeenGameTick = 0;
                return true;
            }

            if (_lastSeenGameTick > 0 && tick + 60 < _lastSeenGameTick)
            {
                ResetRuntime(true);
                _lastSeenGameTick = tick;
                return true;
            }

            _lastSeenGameTick = tick;
            return false;
        }

        #region Skill / AutoSiphon

        private bool ResolveSkillKeys()
        {
            if (_lanceKeyKnown && _siphonKeyKnown)
                return true;

            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _lanceKey = ActionKey.Unknown;
            _siphonKey = ActionKey.Unknown;

            try
            {
                var slots = Hud.Game.Me.Powers.SkillSlots;
                if (slots == null)
                    return false;

                for (int i = 0; i < slots.Length; i++)
                {
                    var skill = slots[i];
                    if (skill == null || skill.SnoPower == null)
                        continue;

                    uint sno = skill.SnoPower.Sno;
                    if (sno == _snoCorpseLance)
                    {
                        _lanceKey = skill.Key;
                        _lanceKeyKnown = true;
                    }
                    else if (sno == _snoSiphonBlood)
                    {
                        _siphonKey = skill.Key;
                        _siphonKeyKnown = true;
                    }
                }
            }
            catch { }

            return _lanceKeyKnown && _siphonKeyKnown && _lanceKey != ActionKey.Unknown && _siphonKey != ActionKey.Unknown;
        }

        private bool TryStopMovementWithSiphon(int tick)
        {
            if (!_siphonKeyKnown || _siphonKey == ActionKey.Unknown)
                return false;

            if (!AutoSiphonEnabled && !LateRefreshAssist)
                return false;

            if (_pulseActive)
                return _siphonPulseOwned;

            if (_siphonKey != _lanceKey && IsActionPhysicallyDown(_siphonKey))
                return true;

            if (_lastMovementStopSiphonTick > 0 && tick - _lastMovementStopSiphonTick < MovementStopSiphonRetryTicks)
                return true;

            _nextPulseTick = 0;
            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            if (!PulseSiphon(tick, MsToTicks(MovementStopSiphonPulseMs), MovementStopSiphonDownTicks, false))
                return false;

            _lastMovementStopSiphonTick = tick;
            _movementDisengageUntilTick = 0;
            return true;
        }

        private void RunAutoSiphon(int tick, IMonster currentTarget, bool newLanceEngagement)
        {
            TickPulseReleaseIfNeeded(tick);

            if (_pulseActive)
                return;

            bool fullAuto = AutoSiphonEnabled;
            bool refreshOnly = !fullAuto && LateRefreshAssist;

            if (!fullAuto && !refreshOnly)
            {
                StopPulseNow();
                return;
            }

            if (!_siphonKeyKnown)
            {
                StopPulseNow();
                return;
            }

            if (_siphonKey != _lanceKey && !_siphonPulseOwned && IsActionPhysicallyDown(_siphonKey))
            {
                StopPulseNow();
                return;
            }

            bool lotdActive = IsBuffActive(_snoLandOfTheDead);
            bool lotdJustEnded = _lastLotdActive && !lotdActive;
            _lastLotdActive = lotdActive;
            bool powerActive = IsBuffActive(PowerPylonBuffSno);
            bool corpsesAvailable = lotdActive || CorpsesAvailable(tick);

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            ResetLateRefreshSinglePulseGate(havePowerShift, stacks, timeLeft);

            int activeTarget = ClampStackTarget(powerActive ? PowerStackTarget : NormalStackTarget);
            bool reachedConfiguredTarget = havePowerShift && stacks >= activeTarget;
            bool needBuild = fullAuto && (!havePowerShift || stacks < activeTarget);
            bool inLateRefreshWindow = havePowerShift && stacks > 0 && timeLeft > 0f && (timeLeft * 1000f) <= LateRefreshWindowMsDefault;

            // The Pestilence RGK rhythm is authoritative here: Power Shift build owns the opener.
            // Do not let LotD/corpse availability, late-refresh intent, or strict target reacquire skip
            // the fast stack-build phase. Inarius only changes which anchors are legal: they must be in range.
            if (needBuild)
                ClearLateRefreshIntent();

            IMonster anchor = currentTarget;
            if (!IsValidSiphonAnchorTarget(anchor, true))
            {
                try { anchor = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, tick); } catch { anchor = null; }

                if (!IsValidSiphonAnchorTarget(anchor, true))
                    anchor = GetManualSiphonTargetFallback();
            }

            if (!IsValidSiphonAnchorTarget(anchor, true) && needBuild)
                anchor = PickBuildOrRefreshSiphonAnchorTarget(anchor);

            if (!IsValidSiphonAnchorTarget(anchor, true) && !IsValidBuildOrRefreshSiphonAnchorTarget(anchor))
            {
                if (!needBuild && TryAnchorSiphonWithoutTarget(tick))
                    return;

                StopPulseNow();
                return;
            }

            ClearNoTargetAnchorLimiter(true);

            uint currentTargetAcdId = GetMonsterAcdId(anchor);
            bool targetChanged = currentTargetAcdId != 0 && currentTargetAcdId != _lastSiphonTargetAcdId;
            if (currentTargetAcdId != 0)
                _lastSiphonTargetAcdId = currentTargetAcdId;

            if (fullAuto && !needBuild && lotdJustEnded && havePowerShift && stacks >= activeTarget && timeLeft > 0f && (timeLeft * 1000f) <= PostLotdRefreshWindowMs)
            {
                // LotD ending is a high-risk transition for Power Shift. Pulse directly if possible;
                // fall back to the intent path only if the direct anchor is not ready yet.
                if (TryDirectPowerShiftRefreshPulse(tick, anchor, true))
                    return;

                QueueLateRefreshIntent(tick, true, true);
                if (_lateRefreshIntentActive)
                    return;
            }

            if (fullAuto && !needBuild && ShouldTryDirectProactiveRefresh(tick, currentTargetAcdId, targetChanged, newLanceEngagement, havePowerShift, stacks, timeLeft))
            {
                if (TryDirectPowerShiftRefreshPulse(tick, anchor, true))
                    return;
            }

            if (fullAuto && !needBuild && ShouldDoProactiveRefresh(tick, currentTargetAcdId, targetChanged, newLanceEngagement, havePowerShift, stacks, timeLeft, lotdActive))
                return;

            if (BossAlive() && !RgAutoSnapSiphonAssist && !needBuild)
            {
                if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                    return;

                if ((timeLeft * 1000f) <= LateRefreshWindowMsDefault)
                {
                    PrepareUrgentLateRefreshSnap(anchor, tick);
                    if (!EnsureUrgentRefreshSiphonAnchor(anchor, tick))
                        return;

                    if (PulseSiphon(tick, MsToTicks(BossLateRefreshPulseMs), BossPulseDownTicks, false))
                        _lateRefreshPulseConsumed = true;
                }
                return;
            }

            if (refreshOnly)
            {
                if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                    return;

                if ((timeLeft * 1000f) <= LateRefreshWindowMsDefault)
                {
                    PrepareUrgentLateRefreshSnap(anchor, tick);
                    if (!EnsureUrgentRefreshSiphonAnchor(anchor, tick))
                        return;

                    if (PulseSiphon(tick, MsToTicks(BossLateRefreshPulseMs), BossPulseDownTicks, false))
                        _lateRefreshPulseConsumed = true;
                }
                return;
            }

            bool needRefresh = LateRefreshAssist && !needBuild && havePowerShift && stacks > 0 && timeLeft > 0f && inLateRefreshWindow;
            bool needMaintain = fullAuto && reachedConfiguredTarget && !inLateRefreshWindow && !corpsesAvailable;

            if (needRefresh)
                anchor = PickUrgentInariusRefreshAnchor(anchor, tick);

            bool fastBuildThrottled = needBuild && tick < _fastBuildThrottleUntilTick;
            if (needBuild && !fastBuildThrottled && FastBuildFuseTripped(tick, activeTarget, havePowerShift, stacks))
                fastBuildThrottled = true;

            bool needSafeBuild = needBuild && fastBuildThrottled;

            int intervalTicks;
            int downTicks = PulseDownTicks;

            if (needBuild && !needSafeBuild)
            {
                intervalTicks = MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs);

                // During the fast build phase, hold Siphon through the full cadence window so
                // the user's held Corpse Lance key cannot slip casts between stack-build pulses.
                // TickPulseReleaseIfNeeded still releases early as soon as the configured cap is reached.
                downTicks = Math.Max(1, intervalTicks);
            }
            else if (needSafeBuild)
            {
                intervalTicks = MsToTicks(MaintainPulseMs);
                downTicks = PulseDownTicks;
            }
            else if (needRefresh)
            {
                // Match the Pestilence RGK refresh cadence: inside the late-refresh window,
                // the first/active refresh attempt uses the same quick Siphon cadence as build,
                // while maintain pulses remain at the safe 400 ms cadence.
                intervalTicks = MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs);
                downTicks = GetLateRefreshDownTicks(lotdActive);
                ClearFastBuildGuard();
            }
            else if (needMaintain)
            {
                intervalTicks = MsToTicks(MaintainPulseMs);
                ClearFastBuildGuard();
            }
            else
            {
                ClearFastBuildGuard();
                return;
            }

            _pulseWasBuild = needBuild && !needSafeBuild;
            _pulseBuildTarget = (needBuild && !needSafeBuild) ? activeTarget : 0;

            if (needRefresh)
                PrepareUrgentLateRefreshSnap(anchor, tick);

            bool anchorReady = needRefresh
                ? EnsureUrgentRefreshSiphonAnchor(anchor, tick)
                : needBuild
                    ? EnsureSiphonPulseAnchorForBuildOrRefresh(anchor, tick)
                    : EnsureSiphonPulseAnchor(anchor, tick);

            if (!anchorReady)
                return;

            bool pulsed = PulseSiphon(tick, intervalTicks, downTicks, true);
            if (pulsed && needBuild && !needSafeBuild)
                _fastBuildPulseCount++;

            if (pulsed && needRefresh)
                _lateRefreshPulseConsumed = true;
        }


        private bool FastBuildFuseTripped(int tick, int activeTarget, bool havePowerShift, int stacks)
        {
            if (_fastBuildTarget != activeTarget || _fastBuildStartTick <= 0)
            {
                _fastBuildStartTick = tick;
                _fastBuildPulseCount = 0;
                _fastBuildTarget = activeTarget;
                _fastBuildBestStacks = havePowerShift ? stacks : 0;
                return false;
            }

            if (havePowerShift && stacks >= activeTarget)
                return false;

            if (havePowerShift && stacks > _fastBuildBestStacks)
                _fastBuildBestStacks = stacks;

            bool tripped = unchecked(tick - _fastBuildStartTick) > MsToTicks(FastBuildFuseMs) || _fastBuildPulseCount >= FastBuildFusePulses;
            if (tripped)
                _fastBuildThrottleUntilTick = Math.Max(_fastBuildThrottleUntilTick, tick + MsToTicks(FastBuildThrottleMs));

            return tripped;
        }

        private void ClearFastBuildGuard()
        {
            _fastBuildStartTick = 0;
            _fastBuildPulseCount = 0;
            _fastBuildBestStacks = 0;
            _fastBuildTarget = 0;
            _fastBuildThrottleUntilTick = 0;
        }

        private bool TryAnchorSiphonWithoutTarget(int tick)
        {
            if (!LateRefreshAssist || !_siphonKeyKnown || _pulseActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            if (havePowerShift && stacks > 0 && timeLeft > 0f && (timeLeft * 1000f) > ProactiveRefreshWindowMs)
                return false;

            return PulseNoTargetAnchor(tick);
        }

        private bool PulseNoTargetAnchor(int tick)
        {
            if (NoTargetAnchorExhausted())
                return false;

            int gapTicks = MsToTicks(NoTargetAnchorPulseMs);
            if (_noTargetAnchorPulses > 0 && (_noTargetAnchorPulses % NoTargetAnchorPulsesPerBurst) == 0)
                gapTicks = MsToTicks(NoTargetAnchorBurstPauseMs);

            if (_noTargetAnchorLastTick > 0 && tick - _noTargetAnchorLastTick < gapTicks)
                return false;

            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            if (!PulseSiphon(tick, MsToTicks(NoTargetAnchorPulseMs), NoTargetAnchorDownTicks, false))
                return false;

            _noTargetAnchorPulses++;
            _noTargetAnchorLastTick = tick;
            _lastPulseWasNoTargetAnchor = true;
            return true;
        }

        private bool NoTargetAnchorExhausted()
        {
            return _noTargetAnchorPulses >= NoTargetAnchorMaxPulses;
        }

        private void ClearNoTargetAnchorLimiter(bool clearNoTargetPulseThrottle)
        {
            _noTargetAnchorPulses = 0;
            _noTargetAnchorLastTick = 0;

            if (clearNoTargetPulseThrottle && _lastPulseWasNoTargetAnchor)
                _lastPulseWasNoTargetAnchor = false;
        }

        private IMonster GetManualSiphonTargetFallback()
        {
            try
            {
                if (_lockedTargetAcdId != 0)
                {
                    var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                    if (IsAliveTarget(locked) && IsWithinInariusTargetRange(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                        return locked;
                }
            }
            catch { }

            try
            {
                IMonster strict = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, Hud.Game.CurrentGameTick);
                if (IsAliveTarget(strict) && IsWithinInariusTargetRange(strict))
                    return strict;
            }
            catch { }

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected) && IsValidSiphonAnchorTarget(selected, true) && !AnyValidInariusTargetCandidateExists())
                    return selected;
            }
            catch { }

            return null;
        }

        private bool ShouldDelayInitialAutosnapForLateRefresh(bool newLanceEngagement, bool manualSiphonDown, bool movedIntoEngagement)
        {
            if (!newLanceEngagement || manualSiphonDown || _pulseActive)
                return false;

            // Normal new-engage refresh should be consumed once per held engagement, but a force-move
            // stop is different: every reposition should be allowed to top off Power Shift and stop
            // movement before Corpse Lance resumes.
            if (!movedIntoEngagement && _engageRefreshConsumed)
                return false;

            if (!AutoSiphonEnabled || !LateRefreshAssist || !_siphonKeyKnown)
                return false;

            int stacks;
            float timeLeft;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f)
                return false;

            if (AutoSiphonEnabled && stacks < GetActivePowerShiftTarget())
                return false;

            // Inside the true 800ms danger window, do not queue a polite intent and return. Let
            // RunAutoSiphon execute the direct urgent refresh path in the same tick.
            if ((timeLeft * 1000f) <= LateRefreshWindowMsDefault)
                return false;

            int windowMs = movedIntoEngagement ? PostMovementRefreshWindowMs : ProactiveRefreshWindowMs;
            return (timeLeft * 1000f) <= windowMs;
        }

        private void QueueLateRefreshIntent(int tick, bool fromEngage)
        {
            QueueLateRefreshIntent(tick, fromEngage, false);
        }

        private void QueueLateRefreshIntent(int tick, bool fromEngage, bool postMovementTopOff)
        {
            if (_lateRefreshIntentActive)
                return;

            int stacks;
            float timeLeft;
            int windowMs = postMovementTopOff ? PostMovementRefreshWindowMs : ProactiveRefreshWindowMs;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > windowMs)
                return;

            if (AutoSiphonEnabled && stacks < GetActivePowerShiftTarget())
                return;

            _lateRefreshIntentActive = true;
            _lateRefreshIntentFromEngage = fromEngage;
            _lateRefreshIntentReadyTick = tick + ((fromEngage && !postMovementTopOff) ? FirstEngageSiphonDelayTicks : 0);
            _lateRefreshIntentExpireTick = tick + LateRefreshIntentExpireTicks;
            _lateRefreshIntentAttempts = 0;
            _lateRefreshIntentNextAttemptTick = _lateRefreshIntentReadyTick;
            _lateRefreshIntentStartTimeLeft = timeLeft;
            _lateRefreshIntentWindowMs = windowMs;
        }

        private bool TryCriticalPowerShiftRefreshPulse(int tick)
        {
            return TryDirectPowerShiftRefreshPulse(tick, null, false);
        }

        private bool ShouldTryDirectProactiveRefresh(int tick, uint currentTargetAcdId, bool targetChanged, bool newLanceEngagement, bool havePowerShift, int stacks, float timeLeft)
        {
            if (!LateRefreshAssist || !AutoSiphonEnabled || !_siphonKeyKnown || _pulseActive)
                return false;

            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                return false;

            if (stacks < GetActivePowerShiftTarget())
                return false;

            float timeLeftMs = timeLeft * 1000f;
            if (timeLeftMs > ProactiveRefreshWindowMs)
                return false;

            // The 800ms danger window is always direct; do not wait for the polite intent path.
            if (timeLeftMs <= LateRefreshWindowMsDefault)
                return true;

            // Match Pestilence's 1200ms safety layer, but only on transitions that can waste the
            // remaining buff window: new/re-engage after movement, target switch, or an already queued
            // top-off intent. This avoids max-cap Siphon spam while corpses/LotD are available.
            if (newLanceEngagement || _lateRefreshIntentActive)
                return true;

            return targetChanged && currentTargetAcdId != 0;
        }

        private bool TryDirectPowerShiftRefreshPulse(int tick, IMonster preferredTarget, bool allowProactiveWindow)
        {
            if (!LateRefreshAssist || !_siphonKeyKnown || _pulseActive)
                return false;

            if (!AutoSiphonEnabled && !LateRefreshAssist)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                return false;

            if (AutoSiphonEnabled && stacks < GetActivePowerShiftTarget())
                return false;

            int windowMs = allowProactiveWindow ? ProactiveRefreshWindowMs : LateRefreshWindowMsDefault;
            if ((timeLeft * 1000f) > windowMs)
                return false;

            IMonster anchor = null;
            if (IsValidUrgentRefreshSiphonAnchorTarget(preferredTarget))
                anchor = preferredTarget;

            if (!IsValidUrgentRefreshSiphonAnchorTarget(anchor))
            {
                try { anchor = Hud.Game.SelectedMonster2; } catch { anchor = null; }
            }

            if (!IsValidUrgentRefreshSiphonAnchorTarget(anchor))
                anchor = PickUrgentRefreshSiphonAnchorTarget(preferredTarget, tick);

            if (!IsValidUrgentRefreshSiphonAnchorTarget(anchor))
                return false;

            ClearLateRefreshIntent();
            PrepareUrgentLateRefreshSnap(anchor, tick);

            if (!EnsureUrgentRefreshSiphonAnchor(anchor, tick))
                return false;

            bool lotdActive = IsBuffActive(_snoLandOfTheDead);
            _pulseWasBuild = false;
            _pulseBuildTarget = 0;
            _nextPulseTick = 0;

            bool pulsed = PulseSiphon(tick, MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs), GetLateRefreshDownTicks(lotdActive), true);
            if (pulsed)
            {
                _lateRefreshPulseConsumed = true;
                _engageRefreshConsumed = true;

                uint acd = GetMonsterAcdId(anchor);
                if (acd != 0)
                {
                    _lastSiphonTargetAcdId = acd;
                    _targetSwitchRefreshAcdId = acd;
                }
            }

            return pulsed;
        }

        private bool TryRunLateRefreshIntent(int tick)
        {
            if (!_lateRefreshIntentActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);

            if (AutoSiphonEnabled && havePowerShift && stacks > 0 && stacks < GetActivePowerShiftTarget())
            {
                ClearLateRefreshIntent();
                return false;
            }

            if (havePowerShift && stacks > 0 && timeLeft > 0f && (timeLeft * 1000f) <= LateRefreshWindowMsDefault)
            {
                // The urgent 800ms refresh must not be trapped behind an older polite/top-off intent.
                // Let the direct refresh path own this tick, matching the Pestilence-style immediate
                // Siphon refresh behavior instead of waiting through intent retry state.
                ClearLateRefreshIntent();
                return false;
            }

            int activeIntentWindowMs = _lateRefreshIntentWindowMs > 0 ? _lateRefreshIntentWindowMs : ProactiveRefreshWindowMs;
            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > activeIntentWindowMs || timeLeft > _lateRefreshIntentStartTimeLeft + LateRefreshIntentRefreshGainSeconds)
            {
                if (_lateRefreshIntentFromEngage)
                    _engageRefreshConsumed = true;

                ClearLateRefreshIntent();
                return false;
            }

            if (!AutoSiphonEnabled || !LateRefreshAssist)
            {
                ClearLateRefreshIntent();
                return false;
            }

            IMonster target = FindLateRefreshIntentTarget(tick);
            bool hasTarget = IsAliveTarget(target);
            bool lotdActive = IsBuffActive(_snoLandOfTheDead);

            if (tick > _lateRefreshIntentExpireTick || (!hasTarget && _lateRefreshIntentAttempts >= LateRefreshIntentMaxAttempts))
            {
                if (_lateRefreshIntentFromEngage)
                    _engageRefreshConsumed = true;

                ClearLateRefreshIntent();
                return false;
            }

            if (tick < _lateRefreshIntentReadyTick || _pulseActive)
                return true;

            if (!hasTarget)
            {
                if (tick < _lateRefreshIntentNextAttemptTick)
                    return true;

                if (NoTargetAnchorExhausted())
                {
                    ClearLateRefreshIntent();
                    return false;
                }

                if (PulseNoTargetAnchor(tick))
                    _lateRefreshIntentNextAttemptTick = tick + Math.Max(LateRefreshIntentRetryTicks, MsToTicks(NoTargetAnchorPulseMs));
                else
                    _lateRefreshIntentNextAttemptTick = tick + LateRefreshIntentRetryTicks;

                return true;
            }

            ClearNoTargetAnchorLimiter(true);
            _lateRefreshIntentNextAttemptTick = 0;
            PrepareUrgentLateRefreshSnap(target, tick);

            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            int refreshIntervalTicks = _lateRefreshIntentAttempts <= 0
                ? MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs)
                : MsToTicks(MaintainPulseMs);

            if (!EnsureUrgentRefreshSiphonAnchor(target, tick))
                return true;

            if (PulseSiphon(tick, refreshIntervalTicks, GetLateRefreshDownTicks(lotdActive), true))
            {
                _lateRefreshIntentAttempts++;
                _lateRefreshIntentNextAttemptTick = tick + Math.Max(LateRefreshIntentRetryTicks, refreshIntervalTicks);

                uint acd = GetMonsterAcdId(target);
                if (acd != 0)
                    _lastSiphonTargetAcdId = acd;
            }

            return true;
        }

        private void PrepareUrgentLateRefreshSnap(IMonster target, int tick)
        {
            if (!AutoSnapEnabled || !IsAliveTarget(target))
                return;

            // Late refresh is the one case where normal post-move/scan throttles are too polite.
            // At 800 ms remaining, the next Siphon pulse must get a cursor anchor before Lance
            // animation timing can let Power Shift fall off.
            PrepareImmediateInariusRetarget(target, tick);
            _lastMouseMoveTick = 0;
            _nextPulseTick = 0;
            _manualCursorOverrideUntilTick = 0;
            _movementDisengageUntilTick = 0;
            _siphonAssistUntilTick = 0;
            TrySnap(target, tick);
        }


        private IMonster PickUrgentInariusRefreshAnchor(IMonster currentAnchor, int tick)
        {
            if (IsValidUrgentRefreshSiphonAnchorTarget(currentAnchor))
                return currentAnchor;

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }
            if (IsValidUrgentRefreshSiphonAnchorTarget(selected))
                return selected;

            IMonster target = null;
            try { target = PickForcedInariusCorrectionTarget(tick); } catch { }
            if (IsValidUrgentRefreshSiphonAnchorTarget(target))
                return target;

            target = PickUrgentRefreshSiphonAnchorTarget(currentAnchor, tick);
            if (IsValidUrgentRefreshSiphonAnchorTarget(target))
                return target;

            return currentAnchor;
        }

        private IMonster FindLateRefreshIntentTarget(int tick)
        {
            IMonster target = GetManualJuggerLockTarget();
            if (IsAliveTarget(target)) return target;

            if (RgAutoSnapSiphonAssist)
            {
                target = FindRiftGuardianTarget();
                if (IsAliveTarget(target)) return target;
            }

            try
            {
                target = PickForcedInariusCorrectionTarget(tick);
                if (IsValidUrgentRefreshSiphonAnchorTarget(target)) return target;
            }
            catch { }

            target = GetManualSiphonTargetFallback();
            if (IsAliveTarget(target))
                return target;

            if (AutoSnapEnabled && (!BossAlive() || RgAutoSnapSiphonAssist))
            {
                target = AcquireTarget(tick);
                if (IsAliveTarget(target))
                    return target;

                try
                {
                    target = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters);
                    if (IsAliveTarget(target) && IsWithinInariusTargetRange(target))
                        return target;
                }
                catch { }
            }

            target = PickUrgentRefreshSiphonAnchorTarget(null, tick);
            if (IsValidUrgentRefreshSiphonAnchorTarget(target))
                return target;

            return null;
        }

        private void ClearLateRefreshIntent()
        {
            _lateRefreshIntentActive = false;
            _lateRefreshIntentFromEngage = false;
            _lateRefreshIntentReadyTick = 0;
            _lateRefreshIntentExpireTick = 0;
            _lateRefreshIntentAttempts = 0;
            _lateRefreshIntentNextAttemptTick = 0;
            _lateRefreshIntentStartTimeLeft = 0f;
            _lateRefreshIntentWindowMs = 0;
        }

        private int GetLateRefreshDownTicks(bool lotdActive)
        {
            return lotdActive ? LotdPulseDownTicks : LateRefreshDownTicks;
        }

        private bool TryGetPowerShift(out int stacksOut, out float timeLeftOut)
        {
            stacksOut = 0;
            timeLeftOut = 0f;

            int tick = 0;
            try { tick = Hud.Game.CurrentGameTick; } catch { }

            try
            {
                var buff = Hud.Game.Me.Powers.GetBuff(_snoSiphonBlood);
                if (buff != null && buff.Active)
                {
                    var counts = buff.IconCounts;
                    var times = buff.TimeLeftSeconds;

                    if (counts != null && counts.Length > PowerShiftBuffIconIndex && counts[PowerShiftBuffIconIndex] > 0)
                    {
                        stacksOut = counts[PowerShiftBuffIconIndex];
                        if (times != null && times.Length > PowerShiftBuffIconIndex && times[PowerShiftBuffIconIndex] > 0d)
                            timeLeftOut = (float)times[PowerShiftBuffIconIndex];

                        CachePowerShiftRead(tick, stacksOut, timeLeftOut);
                        return true;
                    }

                    if (counts != null)
                    {
                        for (int i = 0; i < counts.Length; i++)
                        {
                            if (counts[i] > stacksOut)
                                stacksOut = counts[i];
                        }
                    }

                    double best = 0d;
                    if (times != null)
                    {
                        for (int i = 0; i < times.Length; i++)
                        {
                            double t = times[i];
                            if (t > 0d && (best <= 0d || t < best))
                                best = t;
                        }
                    }

                    timeLeftOut = (float)best;
                    if (stacksOut > 0)
                    {
                        CachePowerShiftRead(tick, stacksOut, timeLeftOut);
                        return true;
                    }
                }
            }
            catch { }

            return TryGetCachedPowerShiftRead(tick, out stacksOut, out timeLeftOut);
        }

        private void CachePowerShiftRead(int tick, int stacks, float timeLeft)
        {
            if (tick <= 0 || stacks <= 0)
                return;

            _lastPowerShiftReadTick = tick;
            _lastPowerShiftStacks = stacks;
            _lastPowerShiftTimeLeft = timeLeft > 0f ? timeLeft : 0f;
        }

        private bool TryGetCachedPowerShiftRead(int tick, out int stacks, out float timeLeft)
        {
            stacks = 0;
            timeLeft = 0f;

            if (tick <= 0 || _lastPowerShiftReadTick <= 0 || _lastPowerShiftStacks <= 0)
                return false;

            int elapsedTicks = tick - _lastPowerShiftReadTick;
            if (elapsedTicks < 0 || elapsedTicks > PowerShiftReadGraceTicks)
                return false;

            float estimated = _lastPowerShiftTimeLeft - (elapsedTicks / 60f);
            if (_lastPowerShiftTimeLeft > 0f && estimated <= 0f)
                return false;

            stacks = _lastPowerShiftStacks;
            timeLeft = estimated > 0f ? estimated : _lastPowerShiftTimeLeft;
            return stacks > 0;
        }

        private bool ShouldDoProactiveRefresh(int tick, uint currentTargetAcdId, bool targetChanged, bool newLanceEngagement, bool havePowerShift, int stacks, float timeLeft, bool lotdActive)
        {
            if (!LateRefreshAssist || !havePowerShift || stacks <= 0 || timeLeft <= 0f)
                return false;

            float timeLeftMs = timeLeft * 1000f;
            if (timeLeftMs > ProactiveRefreshWindowMs)
                return false;

            // At or below 800ms, direct urgent refresh must own the tick. Queueing an intent here
            // was the main reason Inarius could watch Power Shift tick down while a valid target was
            // already selected.
            if (timeLeftMs <= LateRefreshWindowMsDefault)
                return false;

            if (newLanceEngagement)
            {
                if (_engageRefreshConsumed)
                    return false;

                QueueLateRefreshIntent(tick, true);
                return _lateRefreshIntentActive;
            }

            if (targetChanged && currentTargetAcdId != 0 && _targetSwitchRefreshAcdId != currentTargetAcdId)
            {
                _targetSwitchRefreshAcdId = currentTargetAcdId;
                QueueLateRefreshIntent(tick, false);
                return _lateRefreshIntentActive;
            }

            return false;
        }

        private uint GetMonsterAcdId(IMonster monster)
        {
            try { return monster != null ? monster.AcdId : 0u; }
            catch { return 0u; }
        }

        private bool IsBuffActive(uint sno)
        {
            try { return Hud.Game.Me.Powers.BuffIsActive(sno); }
            catch { return false; }
        }

        private void ResetLateRefreshSinglePulseGate(bool havePowerShift, int stacks, float timeLeft)
        {
            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > LateRefreshWindowMsDefault)
                _lateRefreshPulseConsumed = false;
        }

        private bool LateRefreshPulseAlreadyConsumed()
        {
            return _lateRefreshPulseConsumed;
        }

        private bool CorpsesAvailable(int tick)
        {
            if (_lastCorpseScanTick > 0 && tick - _lastCorpseScanTick < CorpseScanIntervalTicks)
                return _cachedCorpsesAvailable;

            _lastCorpseScanTick = tick;
            _cachedCorpsesAvailable = false;

            try
            {
                foreach (var actor in Hud.Game.Actors)
                {
                    if (actor == null || actor.SnoActor == null)
                        continue;

                    if (actor.SnoActor.Sno != ActorSnoEnum._p6_necro_corpse_flesh)
                        continue;

                    if (actor.CentralXyDistanceToMe <= CorpseScanRangeYards && actor.IsOnScreen)
                    {
                        _cachedCorpsesAvailable = true;
                        break;
                    }
                }
            }
            catch { }

            return _cachedCorpsesAvailable;
        }

        #endregion

        #region Targeting

        private IMonster AcquireTarget(int tick)
        {
            IMonster forced = GetManualJuggerLockTarget();
            if (IsAliveTarget(forced))
                return forced;

            if (BossAlive())
            {
                IMonster boss = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget() : null;
                if (IsAliveTarget(boss))
                    return boss;

                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
                _returnToRareAcdId = 0;
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
                return null;
            }

            // PickTarget owns the strict Inarius close bucket. Any in-range monster,
            // including low-health trash, must beat every out-of-range elite/minion.
            IMonster picked = PickTarget(tick);
            if (IsAliveTarget(picked))
            {
                IMonster selectedBeforePick = null;
                try { selectedBeforePick = Hud.Game.SelectedMonster2; } catch { }
                ClearOutOfRangeTargetState();
                if (ShouldForceInariusRetarget(selectedBeforePick, picked))
                    PrepareImmediateInariusRetarget(picked, tick);
                LockTarget(picked, tick);
                return picked;
            }

            IMonster closeFallback = null;
            try { closeFallback = PickClosestInariusDamageTarget(Hud.Game.AliveMonsters); } catch { }
            if (IsAliveTarget(closeFallback))
            {
                IMonster selectedBeforeFallback = null;
                try { selectedBeforeFallback = Hud.Game.SelectedMonster2; } catch { }
                ClearOutOfRangeTargetState();
                if (ShouldForceInariusRetarget(selectedBeforeFallback, closeFallback))
                    PrepareImmediateInariusRetarget(closeFallback, tick);
                if (IsLeader(closeFallback) || IsEliteMinionLike(closeFallback))
                    LockTarget(closeFallback, tick);
                return closeFallback;
            }

            // Do not run the Pestilence-style elite recovery here. For Inarius, any legal
            // close target, including trash, must beat far or stale elite/minion state.

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }

            if (RgAutoSnapSiphonAssist && IsBossLike(selected) && IsWithinInariusTargetRange(selected))
            {
                LockTarget(selected, tick);
                return selected;
            }

            if (tick >= _lockedTargetKeepUntilTick)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }
            return null;
        }


        private void ProcessJuggerHotkey(int tick)
        {
            if (!JuggerHotkeyEnabled || JuggerHotkeyVirtualKey == 0)
            {
                _juggerHotkeyWasDown = false;
                return;
            }

            bool down = IsKeyPhysicallyDown(JuggerHotkeyVirtualKey);
            if (down && !_juggerHotkeyWasDown)
                CycleManualEliteLock(tick);

            _juggerHotkeyWasDown = down;
        }

        private void CycleManualEliteLock(int tick)
        {
            List<IMonster> candidates = GetManualEliteCycleCandidates(tick);
            if (candidates.Count <= 0)
            {
                ClearManualJuggerLock();
                return;
            }

            uint currentAcd = _manualJuggerLockAcdId;
            if (currentAcd != 0)
            {
                int currentIndex = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (GetMonsterAcdId(candidates[i]) == currentAcd)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                // Manual cycle is a deterministic selector, not a toggle. Non-Juggernaut elites
                // are ordered first by lowest health, then Juggernauts are appended as the last
                // choices. If a Juggernaut is the only legal in-range elite, cycle directly to it.
                if (currentIndex >= 0)
                {
                    SetManualEliteLock(candidates[(currentIndex + 1) % candidates.Count], tick);
                    return;
                }
            }

            SetManualEliteLock(candidates[0], tick);
        }

        private List<IMonster> GetManualEliteCycleCandidates(int tick)
        {
            List<IMonster> nonJuggerLeaders = new List<IMonster>(8);
            List<IMonster> juggerLeaders = new List<IMonster>(4);
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsManualEliteCycleCandidate(monster, tick))
                        continue;

                    if (IsJuggernautPack(monster))
                        juggerLeaders.Add(monster);
                    else
                        nonJuggerLeaders.Add(monster);
                }
            }
            catch { }

            SortByHealthThenDistance(nonJuggerLeaders);
            SortByHealthThenDistance(juggerLeaders);

            // Non-Jugger elites are the normal cycle targets. Juggernauts are deliberately
            // appended so repeated Space presses reach them last; if they are alone, this
            // still returns them as the first and only legal target.
            nonJuggerLeaders.AddRange(juggerLeaders);
            return nonJuggerLeaders;
        }

        private bool IsManualEliteCycleCandidate(IMonster monster, int tick)
        {
            if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (!IsWithinInariusTargetRange(monster))
                return false;

            return true;
        }

        private void SortByHealthThenDistance(List<IMonster> monsters)
        {
            monsters.Sort(delegate(IMonster a, IMonster b)
            {
                double ah = SafeHitPoints(a);
                double bh = SafeHitPoints(b);
                int cmp = ah.CompareTo(bh);
                if (cmp != 0) return cmp;
                return SafeDistance(a).CompareTo(SafeDistance(b));
            });
        }

        private double SafeHitPoints(IMonster monster)
        {
            try { return monster.CurHealth / Math.Max(1.0d, monster.MaxHealth); } catch { }
            try { return monster.CurHealth; } catch { }
            return 1.0d;
        }

        private void SetManualEliteLock(IMonster monster, int tick)
        {
            if (!IsManualEliteCycleCandidate(monster, tick))
            {
                ClearManualJuggerLock();
                return;
            }

            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
            {
                ClearManualJuggerLock();
                return;
            }

            // A Space-cycle lock must be one visible target. Clear every old hover/reacquire
            // helper before installing the new ACD so stale elite locks cannot draw/steer as
            // if multiple elites were selected.
            uint previousAcd = _manualJuggerLockAcdId;
            if (previousAcd != 0 && previousAcd != acd)
                ClearBadHoverState(previousAcd);

            _manualJuggerLockAcdId = 0;
            _manualTargetUntilTick = 0;
            ClearAutosnapLockState();
            ClearSoftHoverLocks();

            _manualJuggerLockAcdId = acd;
            _manualTargetUntilTick = tick + ManualTargetLingerTicks;
            _lockedTargetAcdId = acd;
            _lockedTargetKeepUntilTick = tick + ManualTargetLingerTicks;
            _returnToRareAcdId = 0;
            _reacquireAcdId = acd;
            _reacquireUntilTick = tick + ReacquireWindowTicks;
            ClearBadHoverState(acd);
        }

        private void RefreshManualEliteLock(int tick)
        {
            if (_manualJuggerLockAcdId == 0)
                return;

            IMonster monster = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster) || !IsWithinInariusTargetRange(monster))
            {
                ClearManualJuggerLock();
                return;
            }

            _manualTargetUntilTick = tick + ManualTargetLingerTicks;
            _lockedTargetAcdId = _manualJuggerLockAcdId;
            _lockedTargetKeepUntilTick = Math.Max(_lockedTargetKeepUntilTick, _manualTargetUntilTick);
        }

        private void ClearManualJuggerLock()
        {
            uint acd = _manualJuggerLockAcdId;
            if (_lockedTargetAcdId == acd)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }
            if (_stableLockAcdId == acd) { _stableLockAcdId = 0; _stableLockUntilTick = 0; }
            if (_softLockAcdId == acd) { _softLockAcdId = 0; _softLockUntilTick = 0; }
            if (_cachedHoverAcdId == acd) { _cachedHoverAcdId = 0; _cachedHoverUntilTick = 0; _cachedHoverTryUntilTick = 0; }
            ClearBadHoverState(acd);

            if (_reacquireAcdId == acd) { _reacquireAcdId = 0; _reacquireUntilTick = 0; }
            if (_alternateScanAcdId == acd) { _alternateScanAcdId = 0; _alternateScanUntilTick = 0; }
            ClearBadHoverState(acd);
            _manualJuggerLockAcdId = 0;
            _manualTargetUntilTick = 0;
        }

        private IMonster GetManualJuggerLockTarget()
        {
            if (_manualJuggerLockAcdId == 0)
                return null;

            int tick = 0;
            try { tick = Hud.Game.CurrentGameTick; } catch { }
            IMonster monster = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            if (!IsManualEliteCycleCandidate(monster, tick))
            {
                ClearManualJuggerLock();
                return null;
            }

            if (_manualTargetUntilTick > 0 && tick > _manualTargetUntilTick && !IsJuggernautPack(monster))
            {
                ClearManualJuggerLock();
                return null;
            }

            return monster;
        }

        private IMonster FindClosestJuggernaut()
        {
            IMonster best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || !IsJuggernautPack(monster) || IsIllusionOrClone(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d < bestDist)
                    {
                        best = monster;
                        bestDist = d;
                    }
                }
            }
            catch { }
            return best;
        }

        private IMonster FindRiftGuardianTarget()
        {
            try { return FindRiftGuardianTarget(Hud.Game.AliveMonsters); }
            catch { return null; }
        }

        private IMonster FindRiftGuardianTarget(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveTarget(monster) || !IsBossLike(monster) || !IsWithinInariusTargetRange(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d < bestDist)
                    {
                        best = monster;
                        bestDist = d;
                    }
                }
            }
            catch { }
            return best;
        }

        private void ClearSoftHoverLocks()
        {
            _stableLockAcdId = 0;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
        }

        private void ClearAutosnapLockState()
        {
            _lockedTargetAcdId = 0;
            _lockedTargetKeepUntilTick = 0;
            _returnToRareAcdId = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _normalScanParkAcdId = 0;
            _normalScanParkStartTick = 0;
            _normalScanLastReprobeTick = 0;
            _stableLockAcdId = 0;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
        }
        private IMonster PickTarget(int tick)
        {
            IEnumerable<IMonster> monsters = null;
            try { monsters = Hud.Game.AliveMonsters; } catch { }
            if (monsters == null)
                return null;

            var scanList = new List<IMonster>();
            try
            {
                foreach (var scanMonster in monsters)
                    if (scanMonster != null) scanList.Add(scanMonster);
            }
            catch { }

            if (scanList.Count <= 0)
                return null;

            // Space-cycle/manual locks are valid only while physically inside the Inarius damage/tolerance ring.
            IMonster forced = GetManualJuggerLockTarget();
            if (IsAliveTarget(forced) && IsWithinInariusTargetRange(forced))
                return forced;

            // Preserve the Pestilence boss-assist split, but do not allow RG/boss autosnap outside the Inarius ring.
            if (BossAlive())
            {
                IMonster boss = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget(scanList) : null;
                if (IsAliveTarget(boss) && IsWithinInariusTargetRange(boss))
                    return boss;

                ClearOutOfRangeTargetState();
                return null;
            }

            // Inarius targeting is category-first inside the whole 17-yard tolerance ring.
            // Orange elites/minions are still eligible and must beat green trash; range only sorts
            // inside a category, then fallback is allowed only after the whole ring is empty.
            IMonster inariusTarget = PickStrictInariusDamageTarget(scanList, tick);
            if (IsAliveTarget(inariusTarget) && IsWithinInariusTargetRange(inariusTarget))
                return inariusTarget;

            ClearOutOfRangeTargetState();

            IMonster fallback = PickNearestVisibleFallbackTarget(scanList, tick);
            if (IsAliveTarget(fallback))
                return fallback;

            return null;
        }



        private IMonster PickNearestVisibleFallbackTarget(IEnumerable<IMonster> monsters, int tick)
        {
            if (monsters == null || AnyAliveMonsterInsideInariusRawRangeExists())
                return null;

            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsValidOutOfRangeFallbackTarget(monster))
                        continue;

                    float distance = StrictInariusDistance(monster);
                    if (IsBossLike(monster) && !RgAutoSnapSiphonAssist)
                        continue;
                    if (!IsBossLike(monster) && !IsLeader(monster) && !IsEliteMinionLike(monster) && !IncludeTrashTargets)
                        continue;

                    // Once the whole Inarius ring is empty, the fallback job is movement/refresh steering,
                    // not progression optimization. Prefer the nearest visible target so the cursor moves
                    // immediately toward the next playable pack instead of chasing a far elite.
                    float hpScore = GetLowHealthRatioScore(monster, 60f);
                    float score = distance * 100f + hpScore;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private bool IsValidOutOfRangeFallbackTarget(IMonster monster)
        {
            if (!IsAliveForScan(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            if (IsWithinInariusTargetRange(monster))
                return false;

            if (StrictInariusDistance(monster) > RefreshSiphonFallbackRangeYards)
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets;
        }

        private bool IsEligibleSelectedTarget(IMonster monster)
        {
            if (!IsAliveTarget(monster))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            if (IsJuggernautPack(monster))
                return SameMonster(monster, GetManualJuggerLockTarget());

            if (IsShieldingActive(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!IsWithinInariusTargetRange(monster))
                return false;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets;
        }

        private bool IsTargetEligibleForLock(IMonster monster, int tick)
        {
            if (!IsAliveTarget(monster) || IsSkipped(monster, tick))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            if (IsJuggernautPack(monster))
                return SameMonster(monster, GetManualJuggerLockTarget());

            if (IsShieldingActive(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!IsWithinInariusTargetRange(monster))
                return false;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets;
        }

        private void LockTarget(IMonster monster, int tick)
        {
            if (monster == null)
                return;

            uint acd = 0;
            try { acd = monster.AcdId; } catch { }

            // Never cache or snap-phase an out-of-range Inarius target. Stale Pestilence-style
            // locks/caches were the source of far elites staying green/selected over close trash.
            if (!IsWithinInariusTargetRange(monster))
            {
                ClearAcdTargetStateIfOutOfRange(acd, true);
                return;
            }

            // Persist leader locks only, and only for legal ring targets.
            if (IsLeader(monster) && (!IsJuggernautPack(monster) || acd == _manualJuggerLockAcdId) && acd != 0)
            {
                _lockedTargetAcdId = acd;
                _lockedTargetKeepUntilTick = Math.Max(_lockedTargetKeepUntilTick, tick + LockedTargetKeepTicks);
            }
            else if (tick >= _lockedTargetKeepUntilTick)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
            }
        }

        private IMonster FindAliveMonsterByAcdId(uint acdId)
        {
            if (acdId == 0)
                return null;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster != null && monster.AcdId == acdId && monster.IsAlive)
                        return monster;
                }
            }
            catch { }

            return null;
        }

        private void ClearDeadTargetState(int tick)
        {
            if (_lockedTargetAcdId != 0 && FindAliveMonsterByAcdId(_lockedTargetAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_lockedTargetAcdId, tick);
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_manualJuggerLockAcdId != 0 && GetManualJuggerLockTarget() == null)
            {
                ReArmCursorRestoreForDeadTarget(_manualJuggerLockAcdId, tick);
                ClearManualJuggerLock();
            }

            if (_returnToRareAcdId != 0 && FindAliveMonsterByAcdId(_returnToRareAcdId) == null)
                _returnToRareAcdId = 0;

            if (_snapPhaseAcdId != 0 && FindAliveMonsterByAcdId(_snapPhaseAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_snapPhaseAcdId, tick);
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
            }

            if (_stableLockAcdId != 0 && FindAliveMonsterByAcdId(_stableLockAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_stableLockAcdId, tick);
                _stableLockAcdId = 0;
                _stableLockUntilTick = 0;
            }

            if (_softLockAcdId != 0 && FindAliveMonsterByAcdId(_softLockAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_softLockAcdId, tick);
                _softLockAcdId = 0;
                _softLockUntilTick = 0;
            }

            if (_cachedHoverAcdId != 0 && FindAliveMonsterByAcdId(_cachedHoverAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_cachedHoverAcdId, tick);
                _cachedHoverAcdId = 0;
                _cachedHoverUntilTick = 0;
                _cachedHoverTryUntilTick = 0;
            }

            if (_lastHoverAcdId != 0 && FindAliveMonsterByAcdId(_lastHoverAcdId) == null)
            {
                ReArmCursorRestoreForDeadTarget(_lastHoverAcdId, tick);
                _lastHoverAcdId = 0;
                _lastHoverTick = 0;
            }

            if (_reacquireAcdId != 0 && FindAliveMonsterByAcdId(_reacquireAcdId) == null)
            {
                _reacquireAcdId = 0;
                _reacquireUntilTick = 0;
            }

            if (_alternateScanAcdId != 0 && FindAliveMonsterByAcdId(_alternateScanAcdId) == null)
            {
                _alternateScanAcdId = 0;
                _alternateScanUntilTick = 0;
            }
        }

        private bool BossAlive()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster != null && monster.IsAlive && IsBossLike(monster))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool AnyEliteOrMinionCandidateExists()
        {
            return AnyElitePackCandidateExists();
        }

        private bool AnyValidLeaderCandidateExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster))
                        continue;
                    if (IsJuggernautPack(monster) && GetMonsterAcdId(monster) != _manualJuggerLockAcdId)
                        continue;
                    if (IsWithinInariusTargetRange(monster))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private int CountLiveLeaderCandidates()
        {
            int count = 0;
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster))
                        continue;
                    if (IsWithinInariusTargetRange(monster))
                        count++;
                }
            }
            catch { }
            return count;
        }

        private bool AnyElitePackCandidateExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster))
                        continue;

                    if (monster.Rarity == ActorRarity.Boss || IsIllusionOrClone(monster))
                        continue;

                    if (!IsElitePackCandidate(monster))
                        continue;

                    if (IsLeader(monster) && IsJuggernautPack(monster))
                        continue;

                    if (IsWithinInariusTargetRange(monster))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private IMonster FindNearestOnScreenPreferred(IEnumerable<IMonster> monsters)
        {
            // Kept as a compatibility wrapper for inherited Pestilence call sites, but Inarius
            // must not prefer elite/minion recovery over nearby trash. Route every caller through
            // the same strict close-bucket arbiter.
            int tick = 0;
            try { tick = Hud != null && Hud.Game != null ? Hud.Game.CurrentGameTick : 0; } catch { }
            return PickStrictInariusDamageTarget(monsters, tick);
        }

        private IMonster GetShieldingRare(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestDist = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveForScan(monster) || !IsLeader(monster) || monster.Rarity != ActorRarity.Rare)
                        continue;

                    if (IsIllusionOrClone(monster) || IsJuggernautPack(monster) || IsInvulnerable(monster) || !IsShieldingActive(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d <= InariusPick() && d < bestDist)
                    {
                        bestDist = d;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private IMonster PickShockTower(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveTarget(monster) || IsLeader(monster) || IsEliteMinionLike(monster) || IsInvulnerable(monster))
                        continue;

                    if (!IsShockTowerThreat(monster))
                        continue;

                    if (!IsWithinInariusTargetRange(monster))
                        continue;

                    float distance = SafeDistance(monster);
                    float score = GetTrashCloseBucketScore(distance) - 4.0f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }


        #endregion

        private void UpdatePlayerMotionState(int tick)
        {
            try
            {
                var me = Hud.Game.Me;
                if (me == null || me.FloorCoordinate == null)
                    return;

                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;

                if (_haveLastMePos)
                {
                    float dx = x - _lastMeX;
                    float dy = y - _lastMeY;
                    float movedSq = dx * dx + dy * dy;
                    if (movedSq >= TeleportThresholdSq)
                    {
                        _teleportDetectedTick = tick;
                        if (_lockedTargetAcdId != 0)
                        {
                            _reacquireAcdId = _lockedTargetAcdId;
                            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + PostTeleportForceSnapTicks);
                        }
                    }
                    else if (movedSq >= PlayerMoveIntentThresholdSq && (_cursorOwned || _lanceWasDown))
                    {
                        _movementDisengageUntilTick = Math.Max(_movementDisengageUntilTick, tick + MovementDisengageTicks);
                    }
                }

                _lastMeX = x;
                _lastMeY = y;
                _haveLastMePos = true;
            }
            catch { }

            DetectUserCursorOverride(tick);
        }

        private void DetectUserCursorOverride(int tick)
        {
            try
            {
                if (_lastPluginCursorMoveTick <= 0)
                    return;

                int age = tick - _lastPluginCursorMoveTick;
                if (age < UserCursorOverrideMinTicks || age > UserCursorOverrideMaxTicks)
                    return;

                float dx = Hud.Window.CursorX - _lastPluginCursorX;
                float dy = Hud.Window.CursorY - _lastPluginCursorY;
                if (dx * dx + dy * dy < UserCursorOverrideThresholdSq)
                    return;

                if (ShouldHoldCursorDuringLance(tick) && IsCursorOnValidSiphonAnchor())
                {
                    RememberCursorRestoreCandidate();
                    return;
                }

                _manualCursorOverrideUntilTick = Math.Max(_manualCursorOverrideUntilTick, tick + ManualCursorOverridePauseTicks);
                _movementDisengageUntilTick = Math.Max(_movementDisengageUntilTick, tick + MovementDisengageAfterCursorOverrideTicks);
                ReleaseCursorOwnershipWithoutRestore();
            }
            catch { }
        }

        private bool IsManualCursorOverrideActive(int tick)
        {
            return _manualCursorOverrideUntilTick > 0 && tick <= _manualCursorOverrideUntilTick;
        }

        private bool ShouldHoldCursorDuringLance(int tick)
        {
            try
            {
                if (!_lanceKeyKnown || _lanceKey == ActionKey.Unknown || !IsActionPhysicallyDown(_lanceKey))
                    return false;

                if (tick <= _movementDisengageUntilTick)
                    return false;

                var manual = GetManualJuggerLockTarget();
                if (IsAliveTarget(manual))
                    return true;

                if (_lockedTargetAcdId != 0)
                {
                    var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                    if (IsAliveTarget(locked) && IsWithinInariusTargetRange(locked) && (IsLeader(locked) || IsBossLike(locked) || IsEliteMinionLike(locked)))
                        return true;
                }

                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected) && IsWithinInariusTargetRange(selected) && (IsLeader(selected) || IsBossLike(selected) || IsEliteMinionLike(selected)))
                    return true;

                return AnyEliteOrMinionCandidateExists();
            }
            catch { return false; }
        }

        private void RememberCursorRestoreCandidate()
        {
            try
            {
                float x = Hud.Window.CursorX;
                float y = Hud.Window.CursorY;
                if (IsHardSafeScreenTarget(x, y))
                {
                    _savedCursorX = x;
                    _savedCursorY = y;
                    _haveSavedCursor = true;
                }
            }
            catch { }
        }

        private void UpdatePassiveEliteHoverCache(int tick)
        {
            try
            {
                var hovered = Hud.Game.SelectedMonster2;
                bool eliteLikeHover = IsLeader(hovered) || IsEliteMinionLike(hovered) || IsBossLike(hovered);
                if (!IsAliveTarget(hovered) || !eliteLikeHover)
                    return;

                if (IsInvulnerable(hovered) || IsIllusionOrClone(hovered))
                    return;

                uint acd = hovered.AcdId;
                if (!IsWithinInariusTargetRange(hovered))
                {
                    ClearAcdTargetStateIfOutOfRange(acd, false);
                    return;
                }

                float sx, sy;
                if (!TryGetMonsterScreen(hovered, out sx, out sy))
                    return;

                float dx = Hud.Window.CursorX - sx;
                float dy = Hud.Window.CursorY - sy;
                if (Math.Abs(dx) > 180f || Math.Abs(dy) > 180f)
                {
                    dx = 0f;
                    dy = 0f;
                }

                _lastHoverAcdId = acd;
                _lastHoverTick = tick;
                _lastHoverDx = dx;
                _lastHoverDy = dy;
                _cachedHoverAcdId = acd;
                _cachedHoverDx = dx;
                _cachedHoverDy = dy;
                _cachedHoverUntilTick = Math.Max(_cachedHoverUntilTick, tick + 120);
                _cachedHoverTryUntilTick = Math.Max(_cachedHoverTryUntilTick, tick + 8);
                _stableLockAcdId = acd;
                _stableLockDx = dx;
                _stableLockDy = dy;
                _stableLockUntilTick = Math.Max(_stableLockUntilTick, tick + 8);
                _softLockAcdId = acd;
                _softLockDx = dx;
                _softLockDy = dy;
                _softLockUntilTick = Math.Max(_softLockUntilTick, tick + 8);
                RememberTargetRestoreAnchor(acd, eliteLikeHover, sx + dx, sy + dy, tick);
                ClearBadHoverState(acd);
                LockTarget(hovered, tick);
            }
            catch { }
        }

        private bool IsForceMoving()
        {
            int tick = 0;
            try { tick = Hud.Game.CurrentGameTick; } catch { }
            return tick > 0 && tick <= _movementDisengageUntilTick;
        }

        #region Target Helpers

        private bool IsWithinInariusReach(float distance, bool onScreen)
        {
            return distance <= InariusPick() + InariusTargetRangeLeewayYards;
        }

        private bool IsWithinInariusTargetRange(IMonster monster)
        {
            if (!IsInsideInariusDamageRadius(monster))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets;
        }
        private bool IsInsideInariusDamageRadius(IMonster monster)
        {
            return IsInsideInariusDamageRadius(monster, InariusPick() + InariusTargetRangeLeewayYards);
        }

        private bool IsInsideInariusDamageRadius(IMonster monster, float maxDistance)
        {
            if (!IsAliveTarget(monster))
                return false;

            // Inarius damage validation is intentionally stricter than Pestilence.  Some actors can
            // report a generous/low CentralXyDistanceToMe while their ground coordinate is visibly
            // outside the Bone Armor ring.  Treat the target as in-range only when every available
            // distance source agrees it is inside the selected Inarius band.
            return IsInsideInariusDistanceBand(monster, maxDistance);
        }

        private bool IsInsideInariusCoreDamageRadius(IMonster monster)
        {
            // Visual core means the monster floor-center is inside the 15-yard ring.
            // Hitbox-only contact outside that ring is shown as orange, not green.
            return IsAliveTarget(monster) && IsInariusCenterInside(monster, InariusStrictCoreRangeYards);
        }


        private float InariusPick()
        {
            float configured = ClampRange(InariusRangeYards);
            return configured > InariusHardTargetRangeYards ? InariusHardTargetRangeYards : configured;
        }

        private bool AnyValidInariusTargetCandidateExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                        continue;

                    if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                        continue;

                    if (!IsWithinInariusTargetRange(monster))
                        continue;

                    if (IsBossLike(monster) || IsLeader(monster) || IsEliteMinionLike(monster) || IncludeTrashTargets)
                        return true;
                }
            }
            catch { }

            return false;
        }
        private bool AnyAliveMonsterInsideInariusRawRangeExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                        continue;

                    if (IsInsideInariusDistanceBand(monster, InariusPick() + InariusTargetRangeLeewayYards))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsInsideInariusPhysicalRange(IMonster monster)
        {
            return IsInsideInariusPhysicalRange(monster, InariusPick());
        }

        private bool IsInsideInariusPhysicalRange(IMonster monster, float maxDistance)
        {
            if (!IsInsideInariusDamageRadius(monster, maxDistance))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            return true;
        }


        private bool AnyInariusPhysicalMonsterInRangeExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsInsideInariusPhysicalRange(monster))
                        continue;

                    // This intentionally ignores IncludeTrashTargets. Even if trash targeting is disabled,
                    // nearby trash means the helper must not steer to a far elite as a green/normal target.
                    if (IsLeader(monster) || IsEliteMinionLike(monster) || !IsBossLike(monster))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private IMonster PickForcedInariusCorrectionTarget(int tick)
        {
            IEnumerable<IMonster> monsters = null;
            try { monsters = Hud.Game.AliveMonsters; } catch { }
            if (monsters == null)
                return null;

            // Hard correction path: do not let transient hover-skip or stale lock state keep
            // Inarius on trash/red targets when a leader/minion is physically in the 17-yard ring.
            // This is only used for hierarchy correction and urgent refresh, not normal scoring.
            IMonster target = PickBestForcedInariusCategoryTargetWithin(monsters, tick, InariusPick(), 0);
            if (IsAliveTarget(target)) return target;

            target = PickBestForcedInariusCategoryTargetWithin(monsters, tick, InariusPick(), 1);
            if (IsAliveTarget(target)) return target;

            target = PickBestForcedInariusCategoryTargetWithin(monsters, tick, InariusPick(), 2);
            if (IsAliveTarget(target)) return target;

            target = PickBestForcedInariusCategoryTargetWithin(monsters, tick, InariusStrictCoreRangeYards, 3);
            if (IsAliveTarget(target)) return target;

            return PickBestForcedInariusCategoryTargetWithin(monsters, tick, InariusPick(), 3);
        }

        private IMonster PickBestForcedInariusCategoryTargetWithin(IEnumerable<IMonster> monsters, int tick, float maxDistance, int category)
        {
            if (monsters == null)
                return null;

            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsValidForcedInariusCorrectionTarget(monster, tick, maxDistance))
                        continue;

                    bool match;
                    if (category == 0)
                        match = PrioritizeDebuffedElites && IsLeader(monster) && HasPriorityInariusEliteDebuff(monster);
                    else if (category == 1)
                        match = IsLeader(monster);
                    else if (category == 2)
                        match = IsEliteMinionLike(monster);
                    else
                        match = IncludeTrashTargets && !IsLeader(monster) && !IsEliteMinionLike(monster) && !IsBossLike(monster);

                    if (!match)
                        continue;

                    float distance = SafeDistance(monster);
                    float score = category <= 1
                        ? GetInariusEliteCategoryScore(monster, distance)
                        : category == 2
                            ? GetInariusMinionCategoryScore(monster, distance)
                            : GetInariusTrashCategoryScore(monster, distance);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private bool IsValidForcedInariusCorrectionTarget(IMonster monster, int tick, float maxDistance)
        {
            if (!IsAliveForScan(monster) || !IsInsideInariusPhysicalRange(monster, maxDistance))
                return false;

            if (monster.Rarity == ActorRarity.Boss || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (IsLeader(monster))
                return !IsShieldingActive(monster);

            if (IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets;
        }

        private IMonster PickStrictInariusDamageTarget(IEnumerable<IMonster> monsters, int tick)
        {
            // Category priority owns the full Inarius tolerance ring.  A 16-yard elite is risky
            // (orange), but it is still a better RGK target than green trash.  Trash only uses
            // the 15-yard core first, then the 17-yard tolerance band if no core trash exists.
            IMonster target = PickBestInariusCategoryTargetWithin(monsters, tick, InariusPick(), 0);
            if (IsAliveTarget(target)) return target;

            target = PickBestInariusCategoryTargetWithin(monsters, tick, InariusPick(), 1);
            if (IsAliveTarget(target)) return target;

            target = PickBestInariusCategoryTargetWithin(monsters, tick, InariusPick(), 2);
            if (IsAliveTarget(target)) return target;

            target = PickBestInariusCategoryTargetWithin(monsters, tick, InariusStrictCoreRangeYards, 3);
            if (IsAliveTarget(target)) return target;

            return PickBestInariusCategoryTargetWithin(monsters, tick, InariusPick(), 3);
        }

        private IMonster PickBestInariusCategoryTargetWithin(IEnumerable<IMonster> monsters, int tick, float maxDistance, int category)
        {
            if (monsters == null)
                return null;

            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsValidInariusDamageTarget(monster, tick, maxDistance))
                        continue;

                    bool match;
                    if (category == 0)
                        match = PrioritizeDebuffedElites && IsLeader(monster) && HasPriorityInariusEliteDebuff(monster);
                    else if (category == 1)
                        match = IsLeader(monster);
                    else if (category == 2)
                        match = IsEliteMinionLike(monster);
                    else
                        match = IncludeTrashTargets && !IsLeader(monster) && !IsEliteMinionLike(monster) && !IsBossLike(monster);

                    if (!match)
                        continue;

                    float distance = SafeDistance(monster);
                    float score = category <= 1
                        ? GetInariusEliteCategoryScore(monster, distance)
                        : category == 2
                            ? GetInariusMinionCategoryScore(monster, distance)
                            : GetInariusTrashCategoryScore(monster, distance);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }
        private bool IsValidInariusDamageTarget(IMonster monster, int tick)
        {
            return IsValidInariusDamageTarget(monster, tick, InariusPick());
        }

        private bool IsValidInariusDamageTarget(IMonster monster, int tick, float maxDistance)
        {
            if (!IsAliveForScan(monster) || !IsInsideInariusPhysicalRange(monster, maxDistance) || IsSkipped(monster, tick))
                return false;

            if (monster.Rarity == ActorRarity.Boss || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (IsLeader(monster))
                return IsValidEligibleLeader(monster, tick) && SafeIsOnScreen(monster);

            if (IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets && SafeIsOnScreen(monster);
        }


        private float GetInariusCloseFirstTargetScore(IMonster monster, float distance)
        {
            if (IsLeader(monster))
                return GetInariusEliteCategoryScore(monster, distance);

            if (IsEliteMinionLike(monster))
                return GetInariusMinionCategoryScore(monster, distance);

            return GetInariusTrashCategoryScore(monster, distance);
        }

        private float GetInariusEliteCategoryScore(IMonster monster, float distance)
        {
            // Elites are kill-confirm targets: prefer the lowest HP leader so blue packs finish
            // quickly for globes/pylon progression, with an extra bonus for the last visible
            // champion of a blue pack.  This intentionally does not use trash progression rules.
            float score = GetLowHealthRatioScore(monster, 100000f);
            score += GetInariusRangeTieBreaker(distance);

            if (PrioritizeDebuffedElites && HasPriorityInariusEliteDebuff(monster))
                score -= 5000f;

            if (IsLastAliveBluePackElite(monster))
                score -= InariusLastBlueEliteBonus;

            try
            {
                if (GetMonsterAcdId(monster) == _lockedTargetAcdId)
                    score -= 120f;
            }
            catch { }

            return score;
        }

        private float GetInariusMinionCategoryScore(IMonster monster, float distance)
        {
            float score = GetLowHealthRatioScore(monster, 100000f);
            score -= (float)Math.Min(2500d, SafeRiftProgression(monster) * 1000d);
            score += GetInariusRangeTieBreaker(distance);
            return score;
        }

        private float GetInariusTrashCategoryScore(IMonster monster, float distance)
        {
            double hp = SafeHitPoints(monster);
            if (hp < 0d) hp = 0d;
            else if (hp > 1d) hp = 1d;

            double progression = SafeRiftProgression(monster);
            if (progression < 0d) progression = 0d;

            bool lotdActive = IsBuffActive(_snoLandOfTheDead);
            bool corpsesAvailable = false;
            try { corpsesAvailable = lotdActive || CorpsesAvailable(Hud.Game.CurrentGameTick); } catch { corpsesAvailable = lotdActive; }

            int valueBand = GetInariusTrashProgressionBand(progression);
            float score;

            if (valueBand >= 2)
            {
                // Meaningful progression trash owns the trash bucket even at high HP.
                // This keeps Mallet/Unburied-style targets from losing to tiny 1-HP scraps.
                score = (float)(-200000d - Math.Min(45000d, progression * 30000d) + (hp * 35000d));
            }
            else if (valueBand == 1)
            {
                // Medium-value trash gets a smaller value-first bias; same-value ties still
                // resolve by HP so corpse generation remains responsive.
                score = (float)(-70000d - Math.Min(18000d, progression * 18000d) + (hp * 45000d));
            }
            else
            {
                // Low/similar progression trash should be killed by HP first, then distance.
                score = (float)(hp * 85000d - Math.Min(2500d, progression * 2500d));
            }

            if (corpsesAvailable && valueBand > 0)
                score -= 18000f;

            score += GetInariusRangeTieBreaker(distance);
            return score;
        }

        private int GetInariusTrashProgressionBand(double progression)
        {
            if (progression >= InariusHighValueTrashProgression)
                return 2;

            if (progression >= InariusMediumValueTrashProgression)
                return 1;

            return 0;
        }

        private float GetLowHealthRatioScore(IMonster monster, float weight)
        {
            double hp = SafeHitPoints(monster);
            if (hp < 0d) hp = 0d;
            else if (hp > 1d) hp = 1d;
            return (float)(hp * Math.Max(0f, weight));
        }

        private float GetInariusRangeTieBreaker(float distance)
        {
            float score = distance * 140f;

            if (distance > InariusCoreDamageYards)
                score += 12000f + ((distance - InariusCoreDamageYards) * 6500f);
            else if (distance > InariusCloseRetargetYards)
                score += 3500f + ((distance - InariusCloseRetargetYards) * 2500f);
            else if (distance > InariusVeryCloseTargetYards)
                score += (distance - InariusVeryCloseTargetYards) * 220f;

            return score;
        }

        private double SafeRiftProgression(IMonster monster)
        {
            try { return monster != null && monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0d; }
            catch { return 0d; }
        }

        private void CorrectOutOfRangeSelectedTarget(int tick)
        {
            if (!AutoSnapEnabled)
                return;

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }

            IMonster replacement = null;
            try { replacement = PickForcedInariusCorrectionTarget(tick); } catch { }
            if (!IsAliveTarget(replacement))
            {
                try { replacement = PickClosestInariusRetarget(Hud.Game.AliveMonsters, InariusPick(), tick); } catch { }
            }

            if (!IsAliveTarget(replacement) || !IsWithinInariusTargetRange(replacement))
                return;

            if (IsAliveForIndicator(selected) && IsSameAcd(selected, GetMonsterAcdId(replacement)) && IsWithinInariusTargetRange(selected))
                return;

            bool selectedInvalidForInarius = IsAliveForIndicator(selected) && !IsWithinInariusTargetRange(selected);
            if (IsAliveForIndicator(selected) && !selectedInvalidForInarius && !ShouldForceInariusRetarget(selected, replacement))
                return;

            ForceImmediateInariusCorrection(replacement, tick);
        }

        private void ForceImmediateInariusCorrection(IMonster replacement, int tick)
        {
            if (!IsAliveTarget(replacement) || !IsWithinInariusTargetRange(replacement))
                return;

            uint acd = GetMonsterAcdId(replacement);
            if (acd == 0)
                return;

            PrepareImmediateInariusRetarget(replacement, tick);
            LockTarget(replacement, tick);
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
            _alternateScanAcdId = acd;
            _alternateScanUntilTick = Math.Max(_alternateScanUntilTick, tick + AlternateScanWindowTicks);
            TrySnap(replacement, tick);
        }

        private bool ShouldForceInariusRetarget(IMonster selected, IMonster replacement)
        {
            if (!IsAliveTarget(replacement) || !IsWithinInariusTargetRange(replacement))
                return false;

            if (!IsAliveForIndicator(selected))
                return true;

            if (IsSameAcd(selected, GetMonsterAcdId(replacement)))
                return !IsWithinInariusTargetRange(selected);

            if (!IsWithinInariusTargetRange(selected))
                return true;

            int selectedRank = GetInariusSelectionRank(selected);
            int replacementRank = GetInariusSelectionRank(replacement);
            if (replacementRank < selectedRank)
                return true;

            if (replacementRank > selectedRank)
                return false;

            // Same-category retargets are intentionally conservative to avoid target churn.
            // The normal scorer can change targets through the usual snap flow; this hard path
            // exists for hierarchy violations such as trash/minion staying selected over elites.
            return false;
        }

        private int GetInariusSelectionRank(IMonster monster)
        {
            if (!IsAliveTarget(monster) || !IsWithinInariusTargetRange(monster))
                return 99;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist ? 0 : 99;

            if (IsLeader(monster))
                return 1;

            if (IsEliteMinionLike(monster))
                return 2;

            return IncludeTrashTargets ? 3 : 99;
        }

        private bool RetargetClosestInariusDamageTarget(int tick)
        {
            IMonster replacement = null;
            try { replacement = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, tick); } catch { }

            if (!IsAliveTarget(replacement))
            {
                try { replacement = PickClosestInariusRetarget(Hud.Game.AliveMonsters, InariusCloseRetargetYards, tick); } catch { }
            }

            if (!IsAliveTarget(replacement) || !IsWithinInariusTargetRange(replacement))
                return false;

            ForceImmediateInariusCorrection(replacement, tick);
            return true;
        }

        private void PrepareImmediateInariusRetarget(IMonster target, int tick)
        {
            PrepareImmediateInariusRetarget(tick);

            uint acd = GetMonsterAcdId(target);
            if (acd != 0)
            {
                _forcedInariusSnapAcdId = acd;
                _forcedInariusSnapUntilTick = tick + ForcedInariusRetargetTicks;
                _reacquireAcdId = acd;
                _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
                _alternateScanAcdId = acd;
                _alternateScanUntilTick = Math.Max(_alternateScanUntilTick, tick + AlternateScanWindowTicks);
                _unhoverableAcdId = 0;
                _unhoverableUntilTick = 0;
                _skipLeaderAcdId = 0;
                _skipLeaderUntilTick = 0;
                if (_lastFailedLeaderAcdId == acd)
                {
                    _lastFailedLeaderAcdId = 0;
                    _lastFailedLeaderCount = 0;
                    _lastFailedLeaderTick = 0;
                }
            }
        }

        private void PrepareImmediateInariusRetarget(int tick)
        {
            ClearOutOfRangeTargetState();
            ClearSoftHoverLocks();
            _normalScanParkAcdId = 0;
            _normalScanParkStartTick = 0;
            _normalScanLastReprobeTick = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _manualCursorOverrideUntilTick = 0;
            _movementDisengageUntilTick = 0;
            _lastMouseMoveTick = 0;
            if (_siphonAssistUntilTick > tick)
                _siphonAssistUntilTick = tick;
        }

        private IMonster PickClosestInariusRetarget(IEnumerable<IMonster> monsters, float maxDistance, int tick)
        {
            if (monsters == null)
                return null;

            IMonster bestDebuffedLeader = null;
            IMonster bestLeader = null;
            IMonster bestMinion = null;
            IMonster bestTrash = null;

            float bestDebuffedLeaderScore = float.MaxValue;
            float bestLeaderScore = float.MaxValue;
            float bestMinionScore = float.MaxValue;
            float bestTrashScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsValidInariusDamageTarget(monster, tick))
                        continue;

                    float distance = SafeDistance(monster);
                    if (distance > maxDistance)
                        continue;

                    if (IsLeader(monster))
                    {
                        float score = GetInariusEliteCategoryScore(monster, distance);
                        if (PrioritizeDebuffedElites && HasPriorityInariusEliteDebuff(monster))
                        {
                            if (score < bestDebuffedLeaderScore) { bestDebuffedLeaderScore = score; bestDebuffedLeader = monster; }
                        }
                        else if (score < bestLeaderScore) { bestLeaderScore = score; bestLeader = monster; }
                        continue;
                    }

                    if (IsEliteMinionLike(monster))
                    {
                        float score = GetInariusMinionCategoryScore(monster, distance);
                        if (score < bestMinionScore) { bestMinionScore = score; bestMinion = monster; }
                        continue;
                    }

                    if (IncludeTrashTargets)
                    {
                        float score = GetInariusTrashCategoryScore(monster, distance);
                        if (score < bestTrashScore) { bestTrashScore = score; bestTrash = monster; }
                    }
                }
            }
            catch { }

            if (bestDebuffedLeader != null) return bestDebuffedLeader;
            if (bestLeader != null) return bestLeader;
            if (bestMinion != null) return bestMinion;
            return bestTrash;
        }

        private float GetLowHealthTargetBonus(IMonster monster, float maxBonus)
        {
            double hp = SafeHitPoints(monster);
            if (hp < 0d) hp = 0d;
            else if (hp > 1d) hp = 1d;

            return (float)((1d - hp) * Math.Max(0f, maxBonus));
        }

        private void ClearOutOfRangeTargetState()
        {
            ClearAcdTargetStateIfOutOfRange(_lockedTargetAcdId, true);
            ClearAcdTargetStateIfOutOfRange(_returnToRareAcdId, false);
            ClearAcdTargetStateIfOutOfRange(_reacquireAcdId, false);
            ClearAcdTargetStateIfOutOfRange(_stableLockAcdId, false);
            ClearAcdTargetStateIfOutOfRange(_softLockAcdId, false);
            ClearAcdTargetStateIfOutOfRange(_cachedHoverAcdId, false);
            ClearAcdTargetStateIfOutOfRange(_alternateScanAcdId, false);
        }

        private void ClearAcdTargetStateIfOutOfRange(uint acd, bool locked)
        {
            if (acd == 0)
                return;

            IMonster monster = FindAliveMonsterByAcdId(acd);
            if (IsWithinInariusTargetRange(monster))
                return;

            if (locked && _lockedTargetAcdId == acd)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_returnToRareAcdId == acd) _returnToRareAcdId = 0;
            if (_reacquireAcdId == acd) { _reacquireAcdId = 0; _reacquireUntilTick = 0; }
            if (_stableLockAcdId == acd) { _stableLockAcdId = 0; _stableLockUntilTick = 0; }
            if (_softLockAcdId == acd) { _softLockAcdId = 0; _softLockUntilTick = 0; }
            if (_cachedHoverAcdId == acd) { _cachedHoverAcdId = 0; _cachedHoverUntilTick = 0; _cachedHoverTryUntilTick = 0; }
            if (_alternateScanAcdId == acd) { _alternateScanAcdId = 0; _alternateScanUntilTick = 0; }
            ClearBadHoverState(acd);
        }

        private IMonster PickClosestInariusDamageTarget(IEnumerable<IMonster> monsters)
        {
            int tick = 0;
            try { tick = Hud != null && Hud.Game != null ? Hud.Game.CurrentGameTick : 0; } catch { }
            return PickStrictInariusDamageTarget(monsters, tick);
        }

        private bool IsAliveForScan(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;

                if (!monster.Attackable && !monster.IsOnScreen)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private bool IsAliveTarget(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive || !monster.IsOnScreen)
                    return false;

                if (monster.Illusion || monster.Invulnerable || monster.Untargetable || monster.Invisible || monster.Hidden || monster.Stealthed)
                    return false;

                if (!monster.Attackable)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private bool IsValidEligibleLeader(IMonster monster, int tick)
        {
            if (!IsAliveTarget(monster) || IsSkipped(monster, tick))
                return false;

            return IsLeader(monster) && !IsIllusionOrClone(monster) && !IsJuggernautPack(monster) && !IsShieldingActive(monster) && !IsInvulnerable(monster);
        }

        private bool IsLeader(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                // Original LM leader logic was Champion/Rare. Keep Unique as a FreeHUD safety net,
                // but boss encounters are handled separately to preserve manual RG control.
                return monster.Rarity == ActorRarity.Rare || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique;
            }
            catch { return false; }
        }

        private bool IsRareMinion(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.Rarity == ActorRarity.RareMinion)
                    return true;

                string rarity = monster.Rarity.ToString();
                if (rarity.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0 && monster.Rarity != ActorRarity.Boss)
                    return true;

                string name = monster.SnoMonster != null ? monster.SnoMonster.NameEnglish : string.Empty;
                return !string.IsNullOrEmpty(name) && name.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0 && monster.Rarity != ActorRarity.Boss;
            }
            catch { return false; }
        }

        private bool IsEliteMinionLike(IMonster monster)
        {
            if (monster == null || IsLeader(monster))
                return false;

            try { if (IsRareMinion(monster)) return true; } catch { }
            try { if (monster.IsElite) return true; } catch { }

            bool b;
            if (TryGetBool(monster, "IsElite", out b) && b) return true;

            var affixes = GetAffixList(GetPackObject(monster));
            if (affixes == null) return false;

            try
            {
                foreach (var ignored in affixes)
                    return true;
            }
            catch { }

            return false;
        }

        private bool IsElitePackCandidate(IMonster monster)
        {
            if (monster == null) return false;
            try
            {
                if (IsLeader(monster) || IsEliteMinionLike(monster)) return true;
                if (monster.IsElite && monster.Rarity != ActorRarity.Boss) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsElite", out b) && b) return true;
            return false;
        }

        private bool IsBossLike(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;

                if (monster.Rarity == ActorRarity.Boss)
                    return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsBoss", out b) && b) return true;
            if (TryGetBool(monster, "Boss", out b) && b) return true;
            if (TryGetBool(monster, "IsRiftGuardian", out b) && b) return true;
            if (TryGetBool(monster, "RiftGuardian", out b) && b) return true;

            try
            {
                var snoMonster = monster.SnoMonster;
                if (snoMonster != null)
                {
                    if (TryGetBool(snoMonster, "IsBoss", out b) && b) return true;
                    if (TryGetBool(snoMonster, "Boss", out b) && b) return true;
                    if (TryGetBool(snoMonster, "IsRiftGuardian", out b) && b) return true;
                    if (TryGetBool(snoMonster, "RiftGuardian", out b) && b) return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsInvulnerable(IMonster monster)
        {
            if (monster == null) return true;
            try
            {
                if (!monster.IsAlive) return true;
                if (monster.Invulnerable || monster.Untargetable || monster.Invisible || monster.Hidden || monster.Stealthed) return true;
                if (monster.IsOnScreen && !monster.Attackable) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "Invulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsInvulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsShielded", out b) && b) return true;
            if (TryGetBool(monster, "Shielded", out b) && b) return true;
            if (TryGetBool(monster, "HasShield", out b) && b) return true;
            if (TryGetBool(monster, "Untargetable", out b) && b) return true;
            if (TryGetBool(monster, "IsUntargetable", out b) && b) return true;
            if (TryGetBool(monster, "IsImmune", out b) && b) return true;
            return false;
        }

        private bool IsIllusionOrClone(IMonster monster)
        {
            if (monster == null) return false;

            try { if (monster.Illusion) return true; } catch { }

            bool b;
            if (TryGetBool(monster, "IsIllusion", out b) && b) return true;
            if (TryGetBool(monster, "Illusion", out b) && b) return true;
            if (TryGetBool(monster, "IsClone", out b) && b) return true;
            if (TryGetBool(monster, "Clone", out b) && b) return true;
            return false;
        }

        private bool IsShieldingActive(IMonster monster)
        {
            if (monster == null || !HasAffix(monster, MonsterAffix.Shielding)) return false;

            try
            {
                if (monster.Invulnerable || monster.Untargetable) return true;
                if (monster.IsOnScreen && !monster.Attackable) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsShielding", out b) && b) return true;
            if (TryGetBool(monster, "Shielding", out b) && b) return true;
            if (TryGetBool(monster, "ShieldingActive", out b) && b) return true;
            if (TryGetBool(monster, "HasShielding", out b) && b) return true;
            if (TryGetBool(monster, "IsShielded", out b) && b) return true;
            if (TryGetBool(monster, "Shielded", out b) && b) return true;
            if (TryGetBool(monster, "HasShield", out b) && b) return true;
            if (TryGetBool(monster, "Invulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsInvulnerable", out b) && b) return true;
            return IsInvulnerable(monster);
        }

        private bool IsJuggernautPack(IMonster monster)
        {
            // Treat Juggernaut as a leader-only autosnap exclusion. FreeHUD pack-affix
            // fallback can make minions look like Juggernauts too; skipping those minions
            // is what made the plugin stand down around Jugger packs.
            return IsLeader(monster) && HasAffix(monster, MonsterAffix.Juggernaut);
        }

        private bool HasAffix(IMonster monster, MonsterAffix affix)
        {
            if (monster == null) return false;

            try
            {
                var direct = monster.AffixSnoList;
                if (direct != null)
                {
                    foreach (var a in direct)
                    {
                        if (a == null) continue;
                        try { if (a.Affix == affix) return true; } catch { }
                        try { if ((int)a.Id == (int)affix) return true; } catch { }
                        try
                        {
                            var name = a.NameEnglish;
                            if (!string.IsNullOrEmpty(name) && string.Equals(name, affix.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                var list = GetAffixList(GetPackObject(monster));
                if (list == null) return false;

                foreach (var a in list)
                {
                    if (a == null) continue;
                    if (a is MonsterAffix && (MonsterAffix)a == affix) return true;
                    if (a is int && (int)a == (int)affix) return true;
                    if (a is uint && (uint)a == (uint)(int)affix) return true;
                    var snoAffix = a as ISnoMonsterAffix;
                    if (snoAffix != null)
                    {
                        try { if (snoAffix.Affix == affix) return true; } catch { }
                        try { if ((int)snoAffix.Id == (int)affix) return true; } catch { }
                    }
                    if (string.Equals(a.ToString(), affix.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            return false;
        }

        private bool HasAnySiphonDebuff(IMonster monster)
        {
            try
            {
                if (monster == null || !monster.IsAlive)
                    return false;

                return monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_9_Visual_Effect_D, _snoSiphonBlood, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_11_Visual_Effect_D, _snoSiphonBlood, 0) == 1;
            }
            catch { return false; }
        }

        private bool HasInariusDamageDebuff(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;
            }
            catch { return false; }

            // Native probe path for the Inarius/Bone Armor damage marker. This is deliberately
            // conservative and limited to likely visual-effect slots so it cannot dominate runtime
            // if the game does not expose the 6-piece tornado hit as a monster buff.
            return HasCommonPowerBuffVisualEffect(monster, _snoBoneArmor)
                || HasCommonPowerBuffVisualEffect(monster, _snoInariusSaintEnemy);
        }

        private bool HasPriorityInariusEliteDebuff(IMonster monster)
        {
            return HasAnySiphonDebuff(monster) || HasInariusDamageDebuff(monster);
        }

        private bool HasCommonPowerBuffVisualEffect(IMonster monster, uint sno)
        {
            if (monster == null || sno == 0u)
                return false;

            try
            {
                return monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_None, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_A, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_B, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_C, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_5_Visual_Effect_E, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_9_Visual_Effect_D, sno, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_11_Visual_Effect_D, sno, 0) == 1;
            }
            catch { return false; }
        }

        private bool IsShockTowerThreat(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.SnoActor != null && monster.SnoActor.Sno == ActorSnoEnum._x1_pand_ext_ordnance_tower_shock_a)
                    return true;
            }
            catch { }

            try
            {
                string name = monster.SnoActor != null ? monster.SnoActor.NameEnglish : string.Empty;
                if (string.IsNullOrEmpty(name) && monster.SnoMonster != null)
                    name = monster.SnoMonster.NameEnglish;

                return !string.IsNullOrEmpty(name)
                    && name.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool SameMonster(IMonster a, IMonster b)
        {
            if (a == null || b == null)
                return false;

            try { return a.AcdId != 0 && a.AcdId == b.AcdId; }
            catch { return object.ReferenceEquals(a, b); }
        }

        private object GetPackObject(IMonster monster)
        {
            if (monster == null) return null;

            try
            {
                var nativePack = monster.Pack;
                if (nativePack != null) return nativePack;
            }
            catch { }

            try
            {
                var mt = monster.GetType();
                PropertyInfo pi;
                if (!PackPropCache.TryGetValue(mt, out pi))
                {
                    pi = null;
                    foreach (var name in PackPropNames)
                    {
                        pi = mt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    PackPropCache[mt] = pi;
                }
                return pi == null ? null : pi.GetValue(monster, null);
            }
            catch { return null; }
        }

        private int GetPackId(object pack)
        {
            if (pack == null) return 0;
            try
            {
                var pt = pack.GetType();
                PropertyInfo pi;
                if (!PackIdPropCache.TryGetValue(pt, out pi))
                {
                    pi = null;
                    foreach (var name in PackIdPropNames)
                    {
                        pi = pt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    PackIdPropCache[pt] = pi;
                }

                var v = pi == null ? null : pi.GetValue(pack, null);
                if (v == null) return 0;
                if (v is int) return (int)v;
                if (v is uint) return unchecked((int)(uint)v);
                if (v is long) return (int)(long)v;
                int parsed;
                return int.TryParse(v.ToString(), out parsed) ? parsed : 0;
            }
            catch { return 0; }
        }

        private bool SamePack(IMonster monster, object pack)
        {
            if (monster == null || pack == null) return false;
            var mp = GetPackObject(monster);
            if (mp == null) return false;
            if (ReferenceEquals(mp, pack)) return true;
            try { if (mp.Equals(pack) || pack.Equals(mp)) return true; } catch { }
            int a = GetPackId(mp);
            int b = GetPackId(pack);
            return a != 0 && a == b;
        }

        private IEnumerable GetAffixList(object pack)
        {
            if (pack == null) return null;

            try
            {
                var nativePack = pack as IMonsterPack;
                if (nativePack != null) return nativePack.AffixSnoList;
            }
            catch { }

            try
            {
                var pt = pack.GetType();
                PropertyInfo pi;
                if (!AffixListPropCache.TryGetValue(pt, out pi))
                {
                    pi = null;
                    foreach (var name in AffixPropNames)
                    {
                        pi = pt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    AffixListPropCache[pt] = pi;
                }
                return pi == null ? null : pi.GetValue(pack, null) as IEnumerable;
            }
            catch { return null; }
        }

        private bool TryGetBool(object obj, string name, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrEmpty(name)) return false;
            try
            {
                var pi = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi == null || pi.PropertyType != typeof(bool)) return false;
                value = (bool)pi.GetValue(obj, null);
                return true;
            }
            catch { return false; }
        }

        private float SafeDistance(IMonster monster)
        {
            return StrictInariusDistance(monster);
        }


        private bool IsInsideInariusDistanceBand(IMonster monster, float maxDistance)
        {
            if (monster == null)
                return false;

            // Core-band checks intentionally use the monster's floor-center so the visual ring
            // only turns green when the target center is inside the drawn 15-yard circle.
            if (maxDistance <= InariusStrictCoreRangeYards + 0.01f)
                return IsInariusCenterInside(monster, maxDistance);

            // Normal Inarius targeting should follow the native floor hitbox circles shown by F11:
            // accept the monster when its RadiusBottom outline overlaps the 15-yard Bone Armor ring.
            // This keeps true edge-overlap elites targetable while rejecting targets whose center is
            // merely within an arbitrary 17-yard tolerance but whose hitbox does not touch the ring.
            return IsInariusHitboxOverlappingCoreRing(monster);
        }

        private float StrictInariusDistance(IMonster monster)
        {
            return InariusEffectiveHitboxDistance(monster);
        }

        private float InariusEffectiveHitboxDistance(IMonster monster)
        {
            if (monster == null)
                return float.MaxValue;

            float center = BestInariusHitboxCenterDistance(monster);
            float edge = InariusHitboxEdgeDistance(monster);

            if (IsUsableDistance(center) && center <= InariusStrictCoreRangeYards)
                return center;

            if (IsUsableDistance(edge) && edge <= InariusHitboxContactYards)
            {
                // Contact via hitbox overlap is the orange band.  Keep the returned value inside
                // the 15-17 range for existing scoring/brush logic without pretending the center
                // itself is inside the green core circle.
                if (IsUsableDistance(center))
                    return Math.Min(InariusOrangeBandMaxYards, InariusStrictCoreRangeYards + Math.Max(0.05f, center - InariusStrictCoreRangeYards));
                return InariusStrictCoreRangeYards + 0.5f;
            }

            if (IsUsableDistance(center))
                return center;

            float normalized = SafeNormalizedDistance(monster);
            if (IsUsableDistance(normalized))
                return normalized;

            float central = SafeCentralDistance(monster);
            if (IsUsableDistance(central))
                return central;

            return float.MaxValue;
        }

        private bool IsInariusCenterInside(IMonster monster, float maxCenterDistance)
        {
            float center = BestInariusHitboxCenterDistance(monster);
            return IsUsableDistance(center) && center <= maxCenterDistance;
        }

        private bool IsInariusHitboxOverlappingCoreRing(IMonster monster)
        {
            float edge = InariusHitboxEdgeDistance(monster);
            if (IsUsableDistance(edge))
                return edge <= InariusHitboxContactYards;

            // Fallback only when RadiusBottom/FloorCoordinate is unavailable.
            float normalized = SafeNormalizedDistance(monster);
            return IsUsableDistance(normalized) && normalized <= InariusHitboxContactYards;
        }

        private float InariusHitboxEdgeDistance(IMonster monster)
        {
            float center = BestInariusHitboxCenterDistance(monster);
            if (!IsUsableDistance(center))
                return float.MaxValue;

            float radius = SafeNativeRadiusBottom(monster);
            if (radius > 0f)
                return Math.Max(0f, center - radius);

            float normalized = SafeNormalizedDistance(monster);
            return IsUsableDistance(normalized) ? normalized : float.MaxValue;
        }

        private float BestInariusHitboxCenterDistance(IMonster monster)
        {
            // F11 RadiusBottom circles are floor-coordinate circles.  Prefer FloorCoordinate distance
            // over CentralXyDistance so overlap tests match the native debug overlay.
            float floor = SafeFloorDistance(monster);
            if (IsUsableDistance(floor))
                return floor;

            float central = SafeCentralDistance(monster);
            if (IsUsableDistance(central))
                return central;

            return float.MaxValue;
        }

        private float BestInariusCenterDistance(IMonster monster)
        {
            return BestInariusHitboxCenterDistance(monster);
        }

        private float SafeCentralDistance(IMonster monster)
        {
            try { return monster != null ? (float)monster.CentralXyDistanceToMe : float.MaxValue; }
            catch { return float.MaxValue; }
        }

        private float SafeNormalizedDistance(IMonster monster)
        {
            try { return monster != null ? (float)monster.NormalizedXyDistanceToMe : float.MaxValue; }
            catch { return float.MaxValue; }
        }

        private float SafeFloorDistance(IMonster monster)
        {
            try
            {
                var me = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.FloorCoordinate : null;
                if (me != null && monster != null && monster.FloorCoordinate != null)
                    return (float)monster.FloorCoordinate.XYDistanceTo(me);
            }
            catch { }

            return float.MaxValue;
        }

        private float SafeNativeRadiusBottom(IMonster monster)
        {
            try
            {
                float radius = monster != null ? monster.RadiusBottom : 0f;
                return radius > 0f && radius < 60f ? radius : 0f;
            }
            catch { return 0f; }
        }

        private float SafeRadiusBottom(IMonster monster)
        {
            float radius = SafeNativeRadiusBottom(monster);
            return radius > 0f ? radius : BaselineRadiusBottom;
        }

        private static bool IsUsableDistance(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value < float.MaxValue;
        }

        private bool SafeIsOnScreen(IMonster monster)
        {
            try { return monster != null && monster.IsOnScreen; }
            catch { return false; }
        }

        private bool IsSkipped(IMonster monster, int tick)
        {
            try { return monster != null && _skipAcdId != 0 && monster.AcdId == _skipAcdId && tick < _skipUntilTick; }
            catch { return false; }
        }

        private float GetElitePriorityScore(IMonster monster, float distance, bool onScreen, IEnumerable<IMonster> monsters = null)
        {
            float score = GetEliteDistanceScore(distance);

            try
            {
                double hp = SafeHitPoints(monster);
                if (hp >= 0.0d && hp <= 1.5d)
                    score += (float)Math.Min(EliteHealthPriorityWeight, hp * EliteHealthPriorityWeight);
            }
            catch { }

            try
            {
                int density = CountNearbyElitePriority(monster, monsters);
                if (density > 0)
                    score -= Math.Min(ElitePackDensityPriorityMax, density * ElitePackDensityPriorityEach);
            }
            catch { }

            try
            {
                if (PrioritizeDebuffedElites && HasPriorityInariusEliteDebuff(monster))
                    score -= EliteDebuffPriorityBonus;
            }
            catch { }

            try
            {
                if (GetMonsterAcdId(monster) == _lockedTargetAcdId)
                    score -= LockedEliteStickinessBonus;
            }
            catch { }

            if (!onScreen)
                score += 3.0f;

            return score;
        }

        private float GetEliteDistanceScore(float distance)
        {
            if (distance <= InariusEdgePenaltyStart)
                return distance * EliteNearDistanceWeight;

            float denom = Math.Max(0.1f, InariusPick() - InariusEdgePenaltyStart);
            float t = (distance - InariusEdgePenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            return (InariusEdgePenaltyStart * EliteNearDistanceWeight)
                + ((distance - InariusEdgePenaltyStart) * EliteFarDistanceWeight)
                + (t * InariusEdgePenaltyMax);
        }

        private int CountNearbyElitePriority(IMonster monster, IEnumerable<IMonster> monsters)
        {
            if (monster == null || monsters == null)
                return 0;

            int count = 0;
            try
            {
                foreach (var other in monsters)
                {
                    if (other == null || SameMonster(other, monster) || !IsAliveForScan(other))
                        continue;

                    if (!(IsLeader(other) || IsEliteMinionLike(other)))
                        continue;

                    if (IsIllusionOrClone(other) || IsInvulnerable(other) || IsJuggernautPack(other))
                        continue;

                    if (!IsWithinInariusTargetRange(other))
                        continue;

                    if (SafeWorldDistance(monster, other) <= ElitePackDensityRangeYards)
                    {
                        count++;
                        if (count >= 5)
                            break;
                    }
                }
            }
            catch { }

            return count;
        }

        private bool IsLastAliveBluePackElite(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.Rarity != ActorRarity.Champion)
                    return false;

                object pack = GetPackObject(monster);
                if (pack == null)
                    return false;

                int aliveChampionsInRing = 0;
                foreach (var other in Hud.Game.AliveMonsters)
                {
                    if (other == null || !IsAliveForScan(other))
                        continue;

                    if (other.Rarity != ActorRarity.Champion || IsIllusionOrClone(other) || IsInvulnerable(other))
                        continue;

                    if (!SamePack(other, pack))
                        continue;

                    if (!IsInsideInariusDistanceBand(other, InariusPick()))
                        continue;

                    aliveChampionsInRing++;
                    if (aliveChampionsInRing > 1)
                        return false;
                }

                return aliveChampionsInRing == 1;
            }
            catch { return false; }
        }

        private float SafeWorldDistance(IMonster a, IMonster b)
        {
            try
            {
                if (a == null || b == null || a.FloorCoordinate == null || b.FloorCoordinate == null)
                    return float.MaxValue;

                float dx = a.FloorCoordinate.X - b.FloorCoordinate.X;
                float dy = a.FloorCoordinate.Y - b.FloorCoordinate.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
            catch { return float.MaxValue; }
        }

        private float GetCloseBucketScore(float distance)
        {
            if (distance <= InariusEdgePenaltyStart)
                return distance;

            float denom = Math.Max(0.1f, InariusPick() - InariusEdgePenaltyStart);
            float t = (distance - InariusEdgePenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return distance + (t * InariusEdgePenaltyMax);
        }

        private float GetTrashCloseBucketScore(float distance)
        {
            if (distance <= TrashPenaltyStart)
                return distance;

            float denom = Math.Max(0.1f, InariusPick() - TrashPenaltyStart);
            float t = (distance - TrashPenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return distance + (t * TrashEdgePenaltyMax);
        }

        private IMonster ChoosePositionFirstLeader(IMonster nearest, float nearestScore, IMonster debuffed, float debuffedScore)
        {
            if (!PrioritizeDebuffedElites || debuffed == null)
                return nearest;

            if (nearest == null || debuffedScore <= nearestScore + 2.0f)
                return debuffed;

            return nearest;
        }

        private float GetTrashPriorityScore(IMonster monster, float distance)
        {
            // Inarius trash selection is range-first and corpse-generation-first. Progression value is
            // deliberately ignored here so a high-value mob can never pull the cursor toward the edge
            // or outside the Bone Armor/Inarius damage ring.
            float score = GetTrashCloseBucketScore(distance);
            score -= GetLowHealthTargetBonus(monster, 2.25f);
            return score;
        }


        #endregion

        #region Aiming

        private void AimAt(IMonster monster, int tick)
        {
            TrySnap(monster, tick);
        }

        private bool TryAcquireHoverForAutoSiphon(int tick, out IMonster target)
        {
            target = null;

            if (!AutoSnapEnabled)
                return false;

            if (_lockedTargetAcdId != 0)
            {
                var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsAliveTarget(locked) && IsWithinInariusTargetRange(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                {
                    target = locked;
                    TrySnap(locked, tick);
                    return true;
                }
            }

            if (_reacquireAcdId != 0 && tick < _reacquireUntilTick)
            {
                var reacquire = FindAliveMonsterByAcdId(_reacquireAcdId);
                if (IsAliveTarget(reacquire) && IsWithinInariusTargetRange(reacquire) && !IsJuggernautPack(reacquire) && !IsInvulnerable(reacquire) && !IsIllusionOrClone(reacquire))
                {
                    target = reacquire;
                    TrySnap(reacquire, tick);
                    return true;
                }
            }

            IMonster preferred = null;
            try { preferred = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, tick); } catch { }
            if (IsAliveTarget(preferred) && IsWithinInariusTargetRange(preferred))
            {
                target = preferred;
                TrySnap(preferred, tick);
                return true;
            }

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected) && !IsInvulnerable(selected) && !IsIllusionOrClone(selected))
                {
                    if (IsWithinInariusTargetRange(selected) && (IsBossLike(selected) || IsLeader(selected) || (!AnyValidLeaderCandidateExists() && (IsEliteMinionLike(selected) || IncludeTrashTargets))))
                    {
                        target = selected;
                        if (!IsJuggernautPack(selected) && IsLeader(selected))
                            LockTarget(selected, tick);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }


        private bool IsActuallySelectedTarget(uint acd)
        {
            if (acd == 0)
                return false;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                return selected != null && selected.IsAlive && selected.AcdId == acd;
            }
            catch { return false; }
        }

        private bool TryMoveCachedHover(uint acd, float x, float y, int tick, bool force)
        {
            if (acd == 0)
                return false;

            if (_cachedHoverAcdId == acd && tick < _cachedHoverUntilTick)
            {
                float px = x + _cachedHoverDx;
                float py = y + _cachedHoverDy;
                if (IsHardSafeScreenTarget(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, true, px, py, tick);
                    return true;
                }
            }

            if (force && _stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                float px = x + _stableLockDx;
                float py = y + _stableLockDy;
                if (IsHardSafeScreenTarget(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, true, px, py, tick);
                    return true;
                }
            }

            return false;
        }
        private void TrySnap(IMonster monster, int tick)
        {
            if (!AutoSnapEnabled || monster == null || !monster.IsAlive)
                return;

            bool inInariusRange = IsWithinInariusTargetRange(monster);
            if (!inInariusRange)
            {
                // Far steering is allowed only when the whole Inarius ring is empty. This is the
                // Pestilence-like fallback that prevents idle standing and Power Shift drops between
                // packs, but it can never compete with any monster inside the Inarius ring.
                if (AnyAliveMonsterInsideInariusRawRangeExists())
                {
                    ClearOutOfRangeTargetState();
                    RetargetClosestInariusDamageTarget(tick);
                    return;
                }

                if (!IsValidOutOfRangeFallbackTarget(monster))
                    return;
            }

            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
                return;

            bool forcedSnap = acd == _forcedInariusSnapAcdId && tick < _forcedInariusSnapUntilTick;

            if (_normalScanParkAcdId != acd)
            {
                _normalScanParkAcdId = acd;
                _normalScanParkStartTick = tick;
                _normalScanLastReprobeTick = 0;
            }

            bool reacquireQuick = acd == _reacquireAcdId && tick < _reacquireUntilTick;
            bool alternateScan = acd == _alternateScanAcdId && tick < _alternateScanUntilTick;
            bool leaderLike = IsLeader(monster) || IsBossLike(monster);
            bool eliteLike = leaderLike || IsEliteMinionLike(monster);

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }
            bool selectedSame = eliteLike && IsSameAcd(selected, acd);

            int minTicks = forcedSnap
                ? 0
                : (reacquireQuick || alternateScan || AggressiveScanMode || selectedSame || _stableLockAcdId == acd || _cachedHoverAcdId == acd)
                    ? 1
                    : MinTicksBetweenMouseMoves;
            if (!forcedSnap && (_pulseActive || tick < _siphonAssistUntilTick) && !selectedSame && _stableLockAcdId != acd && _cachedHoverAcdId != acd)
                minTicks = Math.Max(1, minTicks + 1);

            if (!forcedSnap && _lastMouseMoveTick > 0 && tick - _lastMouseMoveTick < minTicks)
                return;

            float x, y;
            if (!TryGetMonsterScreen(monster, out x, out y))
                return;

            bool cheapFarTarget = !inInariusRange || (SafeDistance(monster) > InariusPick() + 2f && !SafeIsOnScreen(monster));

            if (selectedSame)
            {
                float currentDx = Hud.Window.CursorX - x;
                float currentDy = Hud.Window.CursorY - y;
                if (Math.Abs(currentDx) > 180f || Math.Abs(currentDy) > 180f)
                {
                    currentDx = _lastHoverDx;
                    currentDy = _lastHoverDy;
                }

                float dx, dy;
                GetTightTrackingOffset(monster, acd, currentDx, currentDy, out dx, out dy);
                RegisterHoverSuccess(acd, tick, dx, dy, _lastSnapAttemptBin);
                _snapPhase = 0;
                if (SafeMouseMove(x + dx, y + dy, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, eliteLike, x + dx, y + dy, tick);
                }
                return;
            }

            bool badRecentHover = IsBadRecentHover(acd, selected, tick);
            if (badRecentHover)
            {
                RegisterBadHover(acd, tick);
                if (_badHoverAcdId == acd && _badHoverCount >= BadHoverInvalidateTicks)
                    InvalidateHoverPoint(acd, true);
            }
            else
            {
                ClearBadHoverState(acd);
            }

            if (!forcedSnap && _unhoverableUntilTick > tick && acd == _unhoverableAcdId && SafeDistance(monster) <= InariusPick() + 3f)
                return;

            if (!eliteLike)
            {
                // Inarius must be allowed to snap to nearby trash. The Pestilence-era guard
                // that refused trash while any elite/minion existed keeps the cursor stuck on
                // edge or red targets, which is wrong for the Inarius damage ring.
                if (SafeMouseMove(x, y, tick))
                {
                    _lastMouseMoveTick = tick;
                    _snapPhase = 0;
                    RememberTargetRestoreAnchor(acd, false, x, y, tick);
                }
                return;
            }

            bool canTrustLastPoint = !badRecentHover || (_badHoverAcdId == acd && _badHoverCount < BadHoverInvalidateTicks);
            bool pulseOrAssist = _pulseActive || tick < _siphonAssistUntilTick;

            if (canTrustLastPoint && (pulseOrAssist || reacquireQuick) && TryMoveCachedHover(acd, x, y, tick, true))
                return;

            if (canTrustLastPoint && _stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                if (SafeMouseMove(x + _stableLockDx, y + _stableLockDy, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, eliteLike, x + _stableLockDx, y + _stableLockDy, tick);
                }
                return;
            }

            bool softWindow = pulseOrAssist || reacquireQuick;
            if (canTrustLastPoint && _softLockAcdId == acd && tick < _softLockUntilTick && softWindow)
            {
                if (SafeMouseMove(x + _softLockDx, y + _softLockDy, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, eliteLike, x + _softLockDx, y + _softLockDy, tick);
                }
                return;
            }

            // A verified hover can be very brief. Reuse the fresh cache immediately before scanning
            // so a one-tick elite highlight becomes the next snap point instead of being lost.
            if (canTrustLastPoint && _cachedHoverAcdId == acd && tick < _cachedHoverTryUntilTick
                && TryMoveCachedHover(acd, x, y, tick, false))
                return;

            if (ShouldParkNormalScan(acd, tick, reacquireQuick, alternateScan, pulseOrAssist)
                && TryMoveNormalScanPark(monster, x, y, acd, tick))
                return;

            BuildAdaptiveProbeOrder(monster, x, y, cheapFarTarget, alternateScan, tick);

            int maxProbes = _rankedProbeCount;
            if (maxProbes <= 0)
            {
                if (SafeMouseMove(x, y, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, eliteLike, x, y, tick);
                }
                return;
            }

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
            }

            int probesThisTick = forcedSnap
                ? Math.Min(maxProbes, 24)
                : AggressiveScanMode
                    ? Math.Min(maxProbes, 24)
                    : ((alternateScan || reacquireQuick) ? Math.Min(maxProbes, 18) : Math.Min(maxProbes, 8));

            int start = Math.Max(0, Math.Min(_snapPhase, maxProbes - 1));
            for (int i = 0; i < probesThisTick; i++)
            {
                int idx = (start + i) % maxProbes;
                float dx = _rankedProbeDx[idx];
                float dy = _rankedProbeDy[idx];
                float px = x + dx;
                float py = y + dy;

                if (!IsHardSafeScreenTarget(px, py))
                    continue;

                int bin = _rankedProbeBin[idx];
                int zone = _rankedProbeZone[idx];
                _lastHoverDx = dx;
                _lastHoverDy = dy;
                _lastSnapAttemptAcd = acd;
                _lastSnapAttemptBin = bin;
                _lastSnapAttemptZone = zone;
                _lastSnapAttemptTick = tick;
                if (bin >= 0 && bin < AdaptiveBinCount)
                    RecordAdaptiveAttempt(bin);
                RecordProbeZoneAttempt(acd, zone, tick);

                if (SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    RememberTargetRestoreAnchor(acd, eliteLike, px, py, tick);
                    _snapPhase = idx + 1;
                    if (_snapPhase >= maxProbes)
                        _snapPhase = 0;
                    return;
                }
            }

            _alternateScanAcdId = acd;
            _alternateScanUntilTick = tick + AlternateScanWindowTicks;
            _snapPhase = 0;

            if (SafeDistance(monster) <= InariusPick() + 3f)
            {
                if (leaderLike)
                {
                    if (_lastFailedLeaderAcdId == acd && tick - _lastFailedLeaderTick <= FailedLeaderRetryWindowTicks)
                        _lastFailedLeaderCount++;
                    else
                    {
                        _lastFailedLeaderAcdId = acd;
                        _lastFailedLeaderCount = 1;
                    }

                    _lastFailedLeaderTick = tick;
                    if (_lastFailedLeaderCount >= 3 && _manualJuggerLockAcdId != acd)
                    {
                        _skipLeaderAcdId = acd;
                        _skipLeaderUntilTick = tick + SkipLeaderWindowTicks;
                        if (_lockedTargetAcdId == acd) _lockedTargetAcdId = 0;
                    }
                }

                _unhoverableAcdId = acd;
                _unhoverableUntilTick = tick + (leaderLike ? 4 : 16);
            }
        }



        private bool ShouldParkNormalScan(uint acd, int tick, bool reacquireQuick, bool alternateScan, bool pulseOrAssist)
        {
            if (AggressiveScanMode || acd == 0 || reacquireQuick || alternateScan)
                return false;

            if (_stableLockAcdId == acd || _cachedHoverAcdId == acd || _softLockAcdId == acd)
                return false;

            if (_normalScanParkAcdId != acd)
                return false;

            if (tick - _normalScanParkStartTick < NormalScanParkAfterTicks)
                return false;

            // Normal scan gets a short probe burst, then parks on radius-bottom/lower-core.
            // A brief re-probe window keeps it from getting permanently stuck if blockers move.
            if (!pulseOrAssist && _normalScanLastReprobeTick > 0 && tick - _normalScanLastReprobeTick < NormalScanParkReprobeTicks)
                return true;

            if (_normalScanLastReprobeTick == 0 || tick - _normalScanLastReprobeTick >= NormalScanParkReprobeTicks)
            {
                _normalScanLastReprobeTick = tick;
                return false;
            }

            return true;
        }

        private bool TryMoveNormalScanPark(IMonster monster, float x, float y, uint acd, int tick)
        {
            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;

            float scale = Math.Max(0.55f, Math.Min(rb / BaselineRadiusBottom, 2.6f));
            float footX = Math.Max(8f, BaseShoulderXPx * 0.30f) * scale;
            float lowerCoreY = -(Math.Max(5f, BaseAbdomenPx * 0.24f) * scale);

            if (TryMoveNormalScanParkPoint(monster, x, y, acd, 0f, 0f, tick))
                return true;
            if (TryMoveNormalScanParkPoint(monster, x, y, acd, 0f, lowerCoreY, tick))
                return true;
            if (TryMoveNormalScanParkPoint(monster, x, y, acd, -footX, 0f, tick))
                return true;
            if (TryMoveNormalScanParkPoint(monster, x, y, acd, footX, 0f, tick))
                return true;

            return false;
        }

        private bool TryMoveNormalScanParkPoint(IMonster monster, float x, float y, uint acd, float dx, float dy, int tick)
        {
            float px = x + dx;
            float py = y + dy;
            if (!IsHardSafeScreenTarget(px, py))
                return false;

            _lastHoverDx = dx;
            _lastHoverDy = dy;
            _lastSnapAttemptAcd = acd;
            _lastSnapAttemptBin = 0;
            _lastSnapAttemptZone = GetProbeZone(monster, dx, dy);
            _lastSnapAttemptTick = tick;

            if (SafeMouseMove(px, py, tick))
            {
                _lastMouseMoveTick = tick;
                RememberTargetRestoreAnchor(acd, true, px, py, tick);
                return true;
            }

            return false;
        }

        private void GetTightTrackingOffset(IMonster monster, uint acd, float currentDx, float currentDy, out float dx, out float dy)
        {
            dx = currentDx;
            dy = currentDy;

            if (_stableLockAcdId == acd && _stableLockUntilTick > 0)
            {
                dx = _stableLockDx;
                dy = _stableLockDy;
            }
            else if (_cachedHoverAcdId == acd && _cachedHoverUntilTick > 0)
            {
                dx = _cachedHoverDx;
                dy = _cachedHoverDy;
            }

            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;

            float scale = Math.Max(0.55f, Math.Min(rb / BaselineRadiusBottom, 2.6f));
            float lowerCoreY = -(Math.Max(6f, BaseAbdomenPx * 0.55f) * scale);
            float maxX = Math.Max(18f, BaseShoulderXPx * 0.78f * scale);
            float minY = -(BaseHeadPx * 0.95f * scale);
            float maxY = Math.Max(8f, 6f * scale);

            dx = ClampFloat(dx, -maxX, maxX);
            dy = ClampFloat(dy, minY, maxY);

            // Keep a proven hover offset, but bias extreme edge/head/feet points back toward the
            // lower core so a moving elite is followed instead of leaving the cursor parked on air/trash.
            if (Math.Abs(dx) > maxX * 0.82f)
                dx *= 0.84f;

            if (dy < minY * 0.82f || dy > maxY * 0.45f)
                dy = dy * 0.78f + lowerCoreY * 0.22f;
        }

        private float ClampFloat(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private bool IsSameAcd(IMonster monster, uint acd)
        {
            return IsAliveTarget(monster) && GetMonsterAcdId(monster) == acd;
        }

        private bool IsBadRecentHover(uint acd, IMonster selected, int tick)
        {
            if (acd == 0)
                return false;

            if (IsSameAcd(selected, acd))
                return false;

            // Final stabilization guard: when the current target is an elite leader, selected trash/minion/none
            // is a bad hover even before the older recent-hover window is satisfied. This prevents a stale edge
            // point from being protected while a valid leader still exists.
            if (IsWrongLeaderHover(acd, selected))
                return true;

            if (_lastHoverAcdId != acd || tick - _lastHoverTick > HoverTruthRecentTicks)
                return false;

            if (!IsAliveTarget(selected))
                return true;

            // Any different hover target is bad for a manual/current elite lock. Do not protect the old point;
            // advance the probe sequence and let SelectedMonster2 prove a new lock.
            return true;
        }

        private bool IsWrongLeaderHover(uint acd, IMonster selected)
        {
            if (acd == 0)
                return false;

            IMonster target = FindAliveMonsterByAcdId(acd);
            if (!IsLeader(target) && !IsBossLike(target))
                return false;

            if (!IsAliveTarget(selected))
                return true;

            if (IsBossLike(selected) || IsLeader(selected))
                return false;

            return true;
        }

        private void RegisterBadHover(uint acd, int tick)
        {
            if (_badHoverAcdId == acd && tick - _badHoverLastTick <= 4)
                _badHoverCount = Math.Min(99, _badHoverCount + 1);
            else
            {
                _badHoverAcdId = acd;
                _badHoverCount = 1;
            }
            _badHoverLastTick = tick;
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
        }

        private void ClearBadHoverState(uint acd)
        {
            if (acd == 0 || _badHoverAcdId == acd)
            {
                _badHoverAcdId = 0;
                _badHoverCount = 0;
                _badHoverLastTick = 0;
            }
        }

        private void InvalidateHoverPoint(uint acd, bool advanceProbe)
        {
            if (_stableLockAcdId == acd) { _stableLockAcdId = 0; _stableLockUntilTick = 0; }
            if (_softLockAcdId == acd) { _softLockAcdId = 0; _softLockUntilTick = 0; }
            if (_cachedHoverAcdId == acd) { _cachedHoverAcdId = 0; _cachedHoverUntilTick = 0; _cachedHoverTryUntilTick = 0; }
            if (_lastSnapAttemptAcd == acd && _lastSnapAttemptBin >= 0 && _lastSnapAttemptBin < AdaptiveBinCount)
                _adaptiveBinBad[_lastSnapAttemptBin] = Math.Min(20f, _adaptiveBinBad[_lastSnapAttemptBin] + 0.65f);
            if (advanceProbe && _snapPhaseAcdId == acd)
                _snapPhase = (_snapPhase + 1) % Math.Max(1, _rankedProbeCount);
        }

        private void RegisterHoverSuccess(uint acd, int tick, float dx, float dy, int bin)
        {
            _lastHoverAcdId = acd;
            _lastHoverTick = tick;
            _cachedHoverAcdId = acd;
            _cachedHoverDx = dx;
            _cachedHoverDy = dy;
            _cachedHoverUntilTick = tick + 300;
            _cachedHoverTryUntilTick = tick + 12;
            _stableLockAcdId = acd;
            _stableLockDx = dx;
            _stableLockDy = dy;
            _stableLockUntilTick = tick + StableLockTicks;
            _softLockAcdId = acd;
            _softLockDx = dx;
            _softLockDy = dy;
            _softLockUntilTick = tick + SoftLockTicks;

            if (bin >= 0 && bin < AdaptiveBinCount)
            {
                _adaptiveBinGood[bin] = Math.Min(20f, _adaptiveBinGood[bin] + 1.0f);
                _adaptiveBinBad[bin] = Math.Max(0f, _adaptiveBinBad[bin] - 0.25f);
            }

            if (_lastSnapAttemptAcd == acd && _lastSnapAttemptZone >= 0)
                RecordProbeZoneSuccess(acd, _lastSnapAttemptZone, tick);

            if (_reacquireAcdId == acd) { _reacquireAcdId = 0; _reacquireUntilTick = 0; }
            if (_alternateScanAcdId == acd) { _alternateScanAcdId = 0; _alternateScanUntilTick = 0; }
            if (_skipLeaderAcdId == acd) { _skipLeaderAcdId = 0; _skipLeaderUntilTick = 0; }
            if (_lastFailedLeaderAcdId == acd) { _lastFailedLeaderAcdId = 0; _lastFailedLeaderCount = 0; }
            if (_unhoverableAcdId == acd) { _unhoverableAcdId = 0; _unhoverableUntilTick = 0; }
        }

        private void BuildAdaptiveProbeOrder(IMonster monster, float fx, float fy, bool cheapFarTarget, bool alternateScan, int tick)
        {
            _rankedProbeCount = 0;

            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;
            float scale = Math.Max(0.55f, Math.Min(rb / BaselineRadiusBottom, 2.8f));

            bool smallTarget = scale <= 0.92f;
            bool wideTarget = scale >= 1.45f;

            float headHighY = -(BaseHeadPx * scale);
            float headLowY = -(BaseHeadPx * 0.84f * scale);
            float shoulderY = -(BaseShoulderPx * 0.98f * scale);
            float chestTopY = -(BaseChestPx * scale);
            float abdomenY = -(BaseAbdomenPx * scale);
            float torsoMidY = (chestTopY + abdomenY) * 0.5f;
            float hipY = -(Math.Max(6f, BaseAbdomenPx * 0.35f) * scale);
            float kneeY = -(Math.Max(3f, BaseAbdomenPx * 0.20f) * scale);

            float headTriX = Math.Max(8f, BaseShoulderXPx * 0.45f) * scale;
            float shoulderX = BaseShoulderXPx * scale;
            float torsoEdgeX = Math.Max(14f, BaseShoulderXPx * 0.55f) * scale;
            float hipX = Math.Max(10f, BaseShoulderXPx * 0.40f) * scale;
            float footX = Math.Max(8f, BaseShoulderXPx * 0.30f) * scale;

            if (smallTarget)
            {
                headTriX *= 0.82f;
                shoulderX *= 0.84f;
                torsoEdgeX *= 0.80f;
                hipX *= 0.80f;
                footX *= 0.82f;
                headHighY *= 0.90f;
                headLowY *= 0.92f;
            }
            else if (wideTarget)
            {
                headTriX *= 1.06f;
                shoulderX *= 1.34f;
                torsoEdgeX *= 1.46f;
                hipX *= 1.36f;
                footX *= 1.18f;
                headHighY *= 1.06f;
                headLowY *= 1.02f;
                shoulderY *= 0.98f;
                chestTopY *= 0.98f;
                abdomenY *= 0.96f;
                torsoMidY = (chestTopY + abdomenY) * 0.5f;
                hipY *= 0.96f;
                kneeY *= 0.96f;
            }

            if (cheapFarTarget)
            {
                headTriX *= 0.88f;
                shoulderX *= 0.90f;
                torsoEdgeX *= 0.92f;
                hipX *= 0.90f;
                footX *= 0.88f;
            }

            BuildSolverBlockerProfile(monster, fx, fy);

            float upper = _solverOccUp + _solverBigUp * 0.90f;
            float lower = _solverOccDown + _solverBigDown * 1.15f;
            float left = _solverOccLeft + _solverBigLeft * 1.05f;
            float right = _solverOccRight + _solverBigRight * 1.05f;
            bool upperBlocked = upper > lower + 0.75f;
            bool lowerBlocked = lower > upper + 0.55f;
            bool leftCleaner = left <= right;
            bool heavyBlockers = (_solverBigDown + _solverBigLeft + _solverBigRight + _solverBigUp) > 1.00f || _solverBlockerWeightTotal > 4.00f;

            uint acd = GetMonsterAcdId(monster);
            float cleanSign = leftCleaner ? -1f : 1f;
            float dirtySign = -cleanSign;

            // First pass: sample a wide vertical hitbox cross before the full feet oval.
            // The bottom center is the safest native anchor, then core/head/shoulders are tried early
            // so tall elites and rat-blocked bosses can be locked without waiting through a long foot sweep.
            float floorOvalX = Math.Max(10f, BaseShoulderXPx * 0.72f) * scale;
            float floorOvalY = Math.Max(4f, 6.5f * scale);
            float tallHeadY = headHighY - Math.Max(10f, (wideTarget ? 20f : 12f) * scale);

            AddRankedProbeCandidate(0f, 0f, 0, monster, fx, fy, cheapFarTarget);             // radius-bottom center

            if (_cachedHoverAcdId == acd && _cachedHoverUntilTick > 0)
                AddMicroProbeCluster(_cachedHoverDx, _cachedHoverDy, Math.Max(2f, 3.5f * scale), Math.Max(2f, 3.0f * scale), 7, monster, fx, fy, cheapFarTarget);

            AddRankedProbeCandidate(0f, torsoMidY, 1, monster, fx, fy, cheapFarTarget);      // center mass
            AddRankedProbeCandidate(0f, abdomenY, 1, monster, fx, fy, cheapFarTarget);       // lower core
            AddRankedProbeCandidate(0f, headLowY, 2, monster, fx, fy, cheapFarTarget);       // head / upper body
            AddRankedProbeCandidate(0f, headHighY, 3, monster, fx, fy, cheapFarTarget);      // top/head
            AddRankedProbeCandidate(0f, tallHeadY, 5, monster, fx, fy, cheapFarTarget);      // high head / tall elite fallback

            AddRankedProbeCandidate(cleanSign * shoulderX, shoulderY, 3, monster, fx, fy, cheapFarTarget);    // cleaner arm/shoulder
            AddRankedProbeCandidate(dirtySign * shoulderX, shoulderY, 3, monster, fx, fy, cheapFarTarget);    // opposite arm/shoulder
            AddRankedProbeCandidate(cleanSign * headTriX, headHighY, 5, monster, fx, fy, cheapFarTarget);     // upper corner
            AddRankedProbeCandidate(dirtySign * headTriX, headHighY, 5, monster, fx, fy, cheapFarTarget);     // upper corner
            AddRankedProbeCandidate(cleanSign * torsoEdgeX, torsoMidY, 2, monster, fx, fy, cheapFarTarget);   // side torso
            AddRankedProbeCandidate(dirtySign * torsoEdgeX, torsoMidY, 2, monster, fx, fy, cheapFarTarget);   // side torso

            // Then sweep the radius-bottom oval as the stable fallback/park region.
            AddRadiusBottomSweep(floorOvalX, floorOvalY, monster, fx, fy, cheapFarTarget, cleanSign);

            AddRankedProbeCandidate(cleanSign * hipX, kneeY, 4, monster, fx, fy, cheapFarTarget);             // lower leg
            AddRankedProbeCandidate(dirtySign * hipX, kneeY, 4, monster, fx, fy, cheapFarTarget);             // lower leg

            if (lowerBlocked)
            {
                AddRankedProbeCandidate(0f, headHighY, 3, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(cleanSign * shoulderX * 1.10f, shoulderY - Math.Max(2f, 3f * scale), 5, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(dirtySign * shoulderX * 1.10f, shoulderY - Math.Max(2f, 3f * scale), 5, monster, fx, fy, cheapFarTarget);
            }
            else if (upperBlocked)
            {
                AddRankedProbeCandidate(cleanSign * footX, 0f, 0, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(dirtySign * footX, 0f, 0, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(0f, hipY, 4, monster, fx, fy, cheapFarTarget);
            }
            else
            {
                AddRankedProbeCandidate(0f, headHighY, 3, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(0f, hipY, 4, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(cleanSign * footX, 0f, 0, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(dirtySign * footX, 0f, 0, monster, fx, fy, cheapFarTarget);
            }

            if (!cheapFarTarget)
            {
                float gridX = torsoEdgeX * (wideTarget ? 1.18f : 0.98f);
                float innerX = torsoEdgeX * (wideTarget ? 0.86f : 0.74f);
                float aboveHeadY = headHighY - Math.Max(3f, 5f * scale);
                float belowFeetY = Math.Max(3f, 5f * scale);
                float upperDiagY = shoulderY - Math.Max(6f, 8f * scale);
                float lowerDiagY = hipY + Math.Max(2f, 3f * scale);

                AddRankedProbeCandidate(-gridX, headLowY, 5, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(0f, headHighY, 5, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(gridX, headLowY, 5, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-innerX, chestTopY, 2, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(innerX, chestTopY, 2, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-gridX, hipY, 4, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(0f, hipY, 4, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(gridX, hipY, 4, monster, fx, fy, cheapFarTarget);

                AddRankedProbeCandidate(-shoulderX * 1.18f, upperDiagY, 6, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(shoulderX * 1.18f, upperDiagY, 6, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-hipX * 1.08f, lowerDiagY, 6, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(hipX * 1.08f, lowerDiagY, 6, monster, fx, fy, cheapFarTarget);

                if (heavyBlockers || wideTarget || alternateScan || AggressiveScanMode)
                {
                    AddRankedProbeCandidate(-gridX * 1.14f, aboveHeadY, 6, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(0f, aboveHeadY, 6, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(gridX * 1.14f, aboveHeadY, 6, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(-footX * 1.18f, belowFeetY, 6, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(0f, belowFeetY, 6, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(footX * 1.18f, belowFeetY, 6, monster, fx, fy, cheapFarTarget);
                }
            }

            // Final fallback: the old compact core/torso sweep that already felt stable.
            float lowerCoreY = -(Math.Max(6f, BaseAbdomenPx * 0.35f) * scale);
            float coreX = Math.Max(10f, BaseShoulderXPx * 0.38f) * scale;
            float flankX = Math.Max(16f, BaseShoulderXPx * 0.60f) * scale;
            AddRankedProbeCandidate(0f, lowerCoreY, 1, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, abdomenY, 1, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(cleanSign * coreX, lowerCoreY, 2, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dirtySign * coreX, lowerCoreY, 2, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(cleanSign * flankX, abdomenY, 4, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dirtySign * flankX, abdomenY, 4, monster, fx, fy, cheapFarTarget);

            AddNonRepeatingJitterProbes(acd, tick, floorOvalX, floorOvalY, torsoEdgeX, headLowY, abdomenY, monster, fx, fy, cheapFarTarget);
        }

        private void AddRadiusBottomSweep(float rx, float ry, IMonster monster, float fx, float fy, bool cheapFarTarget, float preferredSign)
        {
            float otherSign = -preferredSign;
            AddRankedProbeCandidate(0f, 0f, 0, monster, fx, fy, cheapFarTarget);                       // bottom center
            AddRankedProbeCandidate(preferredSign * rx, 0f, 0, monster, fx, fy, cheapFarTarget);        // cleaner bottom edge
            AddRankedProbeCandidate(otherSign * rx, 0f, 0, monster, fx, fy, cheapFarTarget);            // opposite bottom edge
            AddRankedProbeCandidate(0f, -ry, 0, monster, fx, fy, cheapFarTarget);                       // upper arc
            AddRankedProbeCandidate(0f, ry, 0, monster, fx, fy, cheapFarTarget);                        // lower arc
            AddRankedProbeCandidate(preferredSign * rx * 0.72f, -ry * 0.72f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(otherSign * rx * 0.72f, -ry * 0.72f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(preferredSign * rx * 0.72f, ry * 0.72f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(otherSign * rx * 0.72f, ry * 0.72f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(preferredSign * rx * 0.38f, -ry * 1.10f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(otherSign * rx * 0.38f, -ry * 1.10f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(preferredSign * rx * 0.38f, ry * 1.10f, 0, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(otherSign * rx * 0.38f, ry * 1.10f, 0, monster, fx, fy, cheapFarTarget);
        }

        private void AddNonRepeatingJitterProbes(uint acd, int tick, float floorX, float floorY, float bodyX, float headY, float bodyY, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            if (cheapFarTarget && !AggressiveScanMode)
                return;

            int seed = unchecked((int)(acd * 1103515245u + (uint)(tick / 3) * 12345u + (uint)(_snapPhase * 97)));
            float baseStep = 5f + Math.Abs(seed % 7); // 5-11 px fresh nudge range
            float ringX = Math.Max(floorX, bodyX * 0.65f);
            float ringY = Math.Max(floorY, Math.Abs(headY) * 0.18f);

            for (int i = 0; i < 10; i++)
            {
                seed = unchecked(seed * 1664525 + 1013904223);
                int slot = (seed >> 24) & 15;
                float sx = ((slot & 1) == 0 ? -1f : 1f) * (baseStep + ((slot >> 1) & 3) * 2f);
                float sy = ((slot & 8) == 0 ? -1f : 1f) * (baseStep * 0.7f + ((slot >> 2) & 1) * 3f);
                switch (i % 5)
                {
                    case 0: AddRankedProbeCandidate(sx, sy, 6, monster, fx, fy, cheapFarTarget); break;
                    case 1: AddRankedProbeCandidate(ringX * 0.75f + sx, bodyY + sy, 6, monster, fx, fy, cheapFarTarget); break;
                    case 2: AddRankedProbeCandidate(-ringX * 0.75f + sx, bodyY + sy, 6, monster, fx, fy, cheapFarTarget); break;
                    case 3: AddRankedProbeCandidate(sx * 0.5f, headY + sy, 6, monster, fx, fy, cheapFarTarget); break;
                    default: AddRankedProbeCandidate(sx * 0.5f, floorY + sy, 6, monster, fx, fy, cheapFarTarget); break;
                }
            }
        }

        private void AddMicroProbeCluster(float dx, float dy, float jitterX, float jitterY, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            AddRankedProbeCandidate(dx, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx - jitterX, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx + jitterX, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx, dy - jitterY, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx, dy + jitterY, bin, monster, fx, fy, cheapFarTarget);
        }
        private void AddRankedProbeCandidate(float dx, float dy, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            if (_rankedProbeCount >= AdaptiveCandidateCapacity)
                return;

            if (!IsHardSafeScreenTarget(fx + dx, fy + dy))
                return;

            int safeBin = Math.Max(0, Math.Min(AdaptiveBinCount - 1, bin));
            int zone = GetProbeZone(monster, dx, dy);

            for (int i = 0; i < _rankedProbeCount; i++)
            {
                float ex = _rankedProbeDx[i] - dx;
                float ey = _rankedProbeDy[i] - dy;
                if ((ex * ex) + (ey * ey) <= 16f)
                    return;
            }

            _rankedProbeDx[_rankedProbeCount] = dx;
            _rankedProbeDy[_rankedProbeCount] = dy;
            _rankedProbeBin[_rankedProbeCount] = safeBin;
            _rankedProbeZone[_rankedProbeCount] = zone;
            _rankedProbeCount++;
        }


        private void BuildSolverBlockerProfile(IMonster target, float fx, float fy)
        {
            _solverProfileAcd = 0;
            _solverOccLeft = _solverOccRight = _solverOccUp = _solverOccDown = 0f;
            _solverBigLeft = _solverBigRight = _solverBigUp = _solverBigDown = 0f;
            _solverBlockerCx = _solverBlockerCy = 0f;
            _solverBlockerWeightTotal = 0f;
            _solverBlockerCount = 0;

            if (target == null)
                return;

            uint targetAcd = 0;
            try { targetAcd = target.AcdId; } catch { }
            if (targetAcd == 0)
                return;

            _solverProfileAcd = targetAcd;

            try
            {
                foreach (var other in Hud.Game.AliveMonsters)
                {
                    if (other == null || !other.IsAlive || !other.IsOnScreen || SameMonster(other, target))
                        continue;

                    float ox, oy;
                    if (!TryGetMonsterScreen(other, out ox, out oy))
                        continue;

                    float relx = ox - fx;
                    float rely = oy - fy;
                    float dist = (float)Math.Sqrt(relx * relx + rely * rely);

                    float rb = 0f;
                    try { rb = (float)other.RadiusBottom; } catch { }
                    if (rb <= 0f) rb = BaselineRadiusBottom;
                    float bodyScale = Math.Max(0.60f, Math.Min(2.60f, rb / BaselineRadiusBottom));

                    float range = 34f + (bodyScale * 18f);
                    bool bigTrash = IsLargeOccludingTrash(other);
                    if (bigTrash) range += 18f;
                    if (IsLeader(other) || IsEliteMinionLike(other)) range += 10f;
                    if (dist > range) continue;

                    float proximity = 1.32f - Math.Min(1f, dist / range);
                    if (proximity <= 0f) continue;

                    float occ = proximity * GetAdaptiveBlockerWeight(other);
                    if (relx < 0f) _solverOccLeft += occ; else _solverOccRight += occ;
                    if (rely < 0f) _solverOccUp += occ; else _solverOccDown += occ;

                    if (bigTrash)
                    {
                        float bigOcc = occ * 1.10f;
                        if (relx < 0f) _solverBigLeft += bigOcc; else _solverBigRight += bigOcc;
                        if (rely < 0f) _solverBigUp += bigOcc; else _solverBigDown += bigOcc;
                    }

                    _solverBlockerCx += ox * occ;
                    _solverBlockerCy += oy * occ;
                    _solverBlockerWeightTotal += occ;
                    _solverBlockerCount++;
                }
            }
            catch { }

            if (_solverBlockerWeightTotal > 0.01f)
            {
                _solverBlockerCx /= _solverBlockerWeightTotal;
                _solverBlockerCy /= _solverBlockerWeightTotal;
            }
        }

        private float GetAdaptiveBlockerWeight(IMonster blocker)
        {
            if (blocker == null) return 1f;

            float weight = 1f;
            if (IsLeader(blocker) || IsEliteMinionLike(blocker)) weight += 0.65f;
            if (IsLargeOccludingTrash(blocker)) weight += 0.95f;

            float rb = 0f;
            try { rb = (float)blocker.RadiusBottom; } catch { }
            if (rb > 0f)
            {
                float body = Math.Max(0f, Math.Min(1.25f, (rb / BaselineRadiusBottom) - 0.95f));
                weight += body * 0.55f;
            }

            double prog = 0d;
            try { prog = blocker.SnoMonster != null ? blocker.SnoMonster.RiftProgression : 0d; } catch { }
            if (prog >= 0.80d) weight += 0.18f;

            return weight;
        }

        private bool IsLargeOccludingTrash(IMonster monster)
        {
            if (monster == null || IsLeader(monster) || IsEliteMinionLike(monster))
                return false;

            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb >= BaselineRadiusBottom * 1.35f)
                return true;

            double prog = 0d;
            try { prog = monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0d; } catch { }
            if (prog >= BigTrashMinRiftProgression)
                return true;

            try
            {
                string name = monster.SnoMonster != null ? monster.SnoMonster.NameEnglish : string.Empty;
                return !string.IsNullOrEmpty(name)
                    && (name.IndexOf("Grotesque", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Mallet", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Executioner", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Punisher", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        private bool TryGetMonsterScreen(IMonster monster, out float x, out float y)
        {
            x = 0f;
            y = 0f;

            try
            {
                var sc = monster.FloorCoordinate.ToScreenCoordinate();
                if (sc != null)
                {
                    x = sc.X;
                    y = sc.Y;
                    return true;
                }
            }
            catch { }

            try
            {
                var sc = monster.ScreenCoordinate;
                if (sc != null)
                {
                    x = sc.X;
                    y = sc.Y;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void RecordAdaptiveAttempt(int bin)
        {
            if (bin < 0 || bin >= AdaptiveBinCount)
                return;

            _adaptiveBinBad[bin] = Math.Min(20f, _adaptiveBinBad[bin] + 0.10f);
        }

        private int GetProbeZone(IMonster monster, float dx, float dy)
        {
            float rb = 0f;
            try { rb = monster != null ? (float)monster.RadiusBottom : 0f; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;
            float scale = Math.Max(0.5f, Math.Min(rb / BaselineRadiusBottom, 3.0f));
            float headLowY = -(BaseHeadPx * 0.84f * scale);
            float chestTopY = -(BaseChestPx * scale);
            float abdomenY = -(BaseAbdomenPx * scale);
            float torsoMidY = (chestTopY + abdomenY) * 0.5f;
            return GetProbeZone(dx, dy, scale, torsoMidY, headLowY);
        }

        private int GetProbeZone(float dx, float dy, float scale, float torsoMidY, float headLowY)
        {
            float centerHalfX = Math.Max(8f, BaseShoulderXPx * 0.42f * scale);
            int col = dx < -centerHalfX ? 0 : (dx > centerHalfX ? 2 : 1);

            float topCut = (headLowY + torsoMidY) * 0.5f;
            float bottomCut = torsoMidY + Math.Max(6f, BaseAbdomenPx * 0.42f * scale);
            int row = dy <= topCut ? 0 : (dy >= bottomCut ? 2 : 1);

            return row * 3 + col;
        }

        private void ClearProbeZoneState()
        {
            _probeZoneAcdId = 0;
            _probeZoneSuccessAcdId = 0;
            _lastSuccessProbeZone = -1;
            _lastSuccessProbeZoneTick = 0;
            for (int i = 0; i < ProbeZoneCount; i++)
            {
                _probeZoneLastTryTick[i] = 0;
                _probeZoneTryCount[i] = 0;
            }
        }

        private void EnsureProbeZoneState(uint acd)
        {
            if (_probeZoneAcdId == acd)
                return;

            _probeZoneAcdId = acd;
            for (int i = 0; i < ProbeZoneCount; i++)
            {
                _probeZoneLastTryTick[i] = 0;
                _probeZoneTryCount[i] = 0;
            }
        }

        private float GetProbeZoneCoverageBonus(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return 0f;

            EnsureProbeZoneState(acd);

            int last = _probeZoneLastTryTick[zone];
            int age = last > 0 ? tick - last : 9999;
            int tries = _probeZoneTryCount[zone];
            float bonus = 0f;

            if (last == 0)
                bonus += 0.95f;
            else if (age >= 10)
                bonus += 0.55f;
            else if (age >= 5)
                bonus += 0.25f;

            bonus -= Math.Min(1.10f, tries * 0.16f);

            if (_probeZoneSuccessAcdId == acd && _lastSuccessProbeZone >= 0)
            {
                if (zone == _lastSuccessProbeZone)
                    bonus += 0.80f;
                else if (ProbeZonesAdjacent(zone, _lastSuccessProbeZone))
                    bonus += 0.32f;
            }

            return bonus;
        }

        private bool ProbeZonesAdjacent(int a, int b)
        {
            if (a < 0 || b < 0) return false;
            int ar = a / 3, ac = a % 3;
            int br = b / 3, bc = b % 3;
            return Math.Abs(ar - br) <= 1 && Math.Abs(ac - bc) <= 1;
        }

        private void RecordProbeZoneAttempt(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return;

            EnsureProbeZoneState(acd);
            _probeZoneLastTryTick[zone] = tick;
            _probeZoneTryCount[zone] = Math.Min(99, _probeZoneTryCount[zone] + 1);
        }

        private void RecordProbeZoneSuccess(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return;

            _probeZoneSuccessAcdId = acd;
            _lastSuccessProbeZone = zone;
            _lastSuccessProbeZoneTick = tick;
        }

        private void GetProbeOffset(IMonster monster, int phase, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;

            float radius = BaselineRadiusBottom;
            try { radius = monster.RadiusBottom > 0f ? monster.RadiusBottom : BaselineRadiusBottom; } catch { }
            float scale = Math.Max(0.65f, Math.Min(radius / BaselineRadiusBottom, 2.8f));

            switch (phase % 16)
            {
                case 0: dx = 0f; dy = -16f * scale; break;
                case 1: dx = 0f; dy = -32f * scale; break;
                case 2: dx = -13f * scale; dy = -20f * scale; break;
                case 3: dx = 13f * scale; dy = -20f * scale; break;
                case 4: dx = 0f; dy = -48f * scale; break;
                case 5: dx = -20f * scale; dy = -34f * scale; break;
                case 6: dx = 20f * scale; dy = -34f * scale; break;
                case 7: dx = 0f; dy = 0f; break;
                case 8: dx = -28f * scale; dy = -12f * scale; break;
                case 9: dx = 28f * scale; dy = -12f * scale; break;
                case 10: dx = -16f * scale; dy = -52f * scale; break;
                case 11: dx = 16f * scale; dy = -52f * scale; break;
                case 12: dx = 0f; dy = 10f * scale; break;
                case 13: dx = -36f * scale; dy = -28f * scale; break;
                case 14: dx = 36f * scale; dy = -28f * scale; break;
                default: dx = 0f; dy = -64f * scale; break;
            }
        }

        private bool SafeMouseMove(float x, float y, int tick)
        {
            try
            {
                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;

                if (w <= 0f) w = 1920f;
                if (h <= 0f) h = 1080f;

                if (x < 0f) x = 0f;
                if (y < 0f) y = 0f;
                if (x > w - 1f) x = w - 1f;
                if (y > h - 1f) y = h - 1f;

                if (!IsHardSafeScreenTarget(x, y) || tick <= _movementDisengageUntilTick || IsManualCursorOverrideActive(tick))
                    return false;

                BeginCursorOwnershipIfNeeded(tick);
                SetCursorPos((int)Math.Round((double)x), (int)Math.Round((double)y));
                _lastPluginCursorX = x;
                _lastPluginCursorY = y;
                _lastPluginCursorMoveTick = tick;
                _cursorWasMovedByPlugin = true;
                return true;
            }
            catch { return false; }
        }

        private void BeginCursorOwnershipIfNeeded(int tick)
        {
            if (_cursorOwned)
                return;

            _cursorOwned = true;
            _cursorWasMovedByPlugin = false;
            _engageStartTick = tick;

            try
            {
                _savedCursorX = Hud.Window.CursorX;
                _savedCursorY = Hud.Window.CursorY;
                _haveSavedCursor = true;
            }
            catch
            {
                _savedCursorX = 0f;
                _savedCursorY = 0f;
                _haveSavedCursor = false;
            }
        }

        private void RememberTargetRestoreAnchor(uint acd, bool eliteLike, float x, float y, int tick)
        {
            if (!eliteLike || acd == 0 || !RestoreCursorOnReleaseOrMove)
                return;

            try
            {
                if (!IsHardSafeScreenTarget(x, y))
                    return;

                _targetRestoreAnchorAcdId = acd;
                _targetRestoreAnchorX = x;
                _targetRestoreAnchorY = y;
                _targetRestoreAnchorTick = tick;
            }
            catch { }
        }

        private bool ReArmCursorRestoreForDeadTarget(uint acd, int tick)
        {
            if (acd == 0 || acd != _targetRestoreAnchorAcdId)
                return false;

            if (!RestoreCursorOnReleaseOrMove || !_cursorOwned || !_cursorWasMovedByPlugin)
                return false;

            if (_targetRestoreAnchorTick <= 0 || tick - _targetRestoreAnchorTick > DeadTargetRestoreAnchorTicks)
                return false;

            if (!IsHardSafeScreenTarget(_targetRestoreAnchorX, _targetRestoreAnchorY))
                return false;

            _savedCursorX = _targetRestoreAnchorX;
            _savedCursorY = _targetRestoreAnchorY;
            _haveSavedCursor = true;
            _engageStartTick = tick;
            _pendingRestoreTick = 0;
            return true;
        }

        private bool TryRestoreCursorImmediately(int tick, bool force)
        {
            if (!RestoreCursorOnReleaseOrMove || !_cursorOwned || !_cursorWasMovedByPlugin || !_haveSavedCursor)
            {
                ReleaseCursorOwnershipWithoutRestore();
                return false;
            }

            if (!force)
            {
                int engagedTicks = _engageStartTick > 0 ? Math.Max(0, tick - _engageStartTick) : int.MaxValue;
                if (engagedTicks > CursorRestoreShortEngageTicks)
                {
                    ReleaseCursorOwnershipWithoutRestore();
                    return false;
                }
            }

            float targetX = _savedCursorX;
            float targetY = _savedCursorY;

            try
            {
                if (IsHardSafeScreenTarget(targetX, targetY))
                    SetCursorPos((int)Math.Round((double)targetX), (int)Math.Round((double)targetY));
            }
            catch { }

            ReleaseCursorOwnershipWithoutRestore();
            return true;
        }

        private void QueueCursorRestore(int tick)
        {
            if (!RestoreCursorOnReleaseOrMove || !_cursorOwned || !_cursorWasMovedByPlugin)
                return;

            int engagedTicks = _engageStartTick > 0 ? Math.Max(0, tick - _engageStartTick) : int.MaxValue;
            if (engagedTicks > CursorRestoreShortEngageTicks || !_haveSavedCursor)
            {
                ReleaseCursorOwnershipWithoutRestore();
                return;
            }

            float targetX = _savedCursorX;
            float targetY = _savedCursorY;

            try
            {
                float dx = targetX - Hud.Window.CursorX;
                float dy = targetY - Hud.Window.CursorY;
                if ((dx * dx + dy * dy) < CursorRestoreMinMovePxSq)
                {
                    ReleaseCursorOwnershipWithoutRestore();
                    return;
                }
            }
            catch { }

            _pendingRestoreX = targetX;
            _pendingRestoreY = targetY;
            _pendingRestoreTick = tick;
        }

        private void RefreshCursorRestoreWindowForFreshEngagement(int tick)
        {
            if (!RestoreCursorOnReleaseOrMove)
                return;

            // Keep the original pre-snap cursor anchor across rapid short taps.
            // Reset only the short-engage timer so separate quick presses do not accumulate into long-press behavior.
            if (_cursorOwned && _cursorWasMovedByPlugin && _haveSavedCursor)
            {
                _pendingRestoreTick = 0;
                _engageStartTick = tick;
            }
        }

        private void ReleaseCursorOwnershipWithoutRestore()
        {
            _pendingRestoreTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _engageStartTick = 0;
        }

        private float _pendingRestoreX;
        private float _pendingRestoreY;
        private int _pendingRestoreTick;

        private void ProcessPendingCursorRestore(int tick)
        {
            if (_pendingRestoreTick <= 0 || tick < _pendingRestoreTick)
                return;

            try
            {
                if (Hud != null && Hud.Window != null && Hud.Window.IsForeground && IsHardSafeScreenTarget(_pendingRestoreX, _pendingRestoreY))
                    SetCursorPos((int)Math.Round((double)_pendingRestoreX), (int)Math.Round((double)_pendingRestoreY));
            }
            catch { }

            _pendingRestoreTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _engageStartTick = 0;
        }

        private bool IsHardSafeScreenTarget(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;

                if (x < 0f || y < 0f || x > w || y > h)
                    return false;

                if (IsInsideGlobalNoHoverUiGuard(x, y))
                    return false;

                if (IsInsideNativeHardUiElement(x, y))
                    return false;

                return true;
            }
            catch { return true; }
        }

        private bool IsPreferredAutoSnapScreenTarget(float x, float y)
        {
            return IsHardSafeScreenTarget(x, y);
        }

        private bool IsSafeScreenTarget(float x, float y)
        {
            return IsHardSafeScreenTarget(x, y);
        }

        private bool IsInsidePlayerPortraitFace(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Players == null)
                    return false;

                foreach (var player in Hud.Game.Players)
                {
                    try
                    {
                        if (player == null || !player.IsInGame)
                            continue;

                        var ui = player.PortraitUiElement;
                        if (ui == null || !ui.Visible)
                            continue;

                        RectangleF r = ui.Rectangle;
                        if (r.Width <= 0f || r.Height <= 0f)
                            continue;

                        if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                            return true;
                    }
                    catch { }
                }

                return false;
            }
            catch { return false; }
        }


        private bool IsInsideNativeHardUiElement(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Render == null)
                    return false;

                int ix = (int)Math.Round((double)x);
                int iy = (int)Math.Round((double)y);

                if (IsInsideNativeActionUiElement(ActionKey.LeftSkill, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.RightSkill, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill1, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill2, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill3, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill4, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Heal, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.TownPortal, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Inventory, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.SkillsWindow, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.ParagonWindow, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Map, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.WaypointMap, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Social, ix, iy)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Close, ix, iy)) return true;

                if (IsInsideVisibleUiElement(Hud.Render.MinimapUiElement, ix, iy)) return true;
                if (IsInsideVisibleUiElement(Hud.Render.MonsterHpBarUiElement, ix, iy)) return true;
                if (IsInsideVisibleUiElement(Hud.Render.NephalemRiftBarUiElement, ix, iy)) return true;
                if (IsInsideVisibleUiElement(Hud.Render.GreaterRiftBarUiElement, ix, iy)) return true;
                if (IsInsideVisibleUiElement(Hud.Render.ChallengeRiftBarUiElement, ix, iy)) return true;

                var buffs = Hud.Render.BuffBarUiElements;
                if (buffs != null)
                {
                    foreach (var ui in buffs)
                    {
                        if (IsInsideVisibleUiElement(ui, ix, iy))
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void RegisterClickDangerUiElements()
        {
            _clickDangerUiElements.Clear();

            RegisterClickDangerUiElement("Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect");
            RegisterClickDangerUiElement("Root.NormalLayer.game_notify_dialog_backgroundScreen.dialog_new_paragon_button");
            RegisterClickDangerUiElement("Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList");
            RegisterClickDangerUiElement("Root.TopLayer.follower_swap");
            RegisterClickDangerUiElement("Root.NormalLayer.BattleNetProfile_main.LayoutRoot.OverlayContainer");
            RegisterClickDangerUiElement("Root.NormalLayer.BattleNetLeaderboard_main.LayoutRoot.OverlayContainer");
            RegisterClickDangerUiElement("Root.NormalLayer.BattleNetAchievements_main.LayoutRoot.OverlayContainer");
            RegisterClickDangerUiElement("Root.NormalLayer.BattleNetStore_main.LayoutRoot.OverlayContainer");
            RegisterClickDangerUiElement("Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd.button_resumeGame");
            RegisterClickDangerUiElement("Root.NormalLayer.Guild_main.LayoutRoot.OverlayContainer");
            RegisterClickDangerUiElement("Root.TopLayer.BattleNetSocialDialogs_main.LayoutRoot.DialogWriteNote.DialogWriteNoteTitle");
            RegisterClickDangerUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline");
        }

        private void RegisterClickDangerUiElement(string path)
        {
            try
            {
                IUiElement element = Hud.Render.RegisterUiElement(path, null, null);
                if (element != null)
                    _clickDangerUiElements.Add(element);
            }
            catch { }
        }

        private bool IsMouseSiphonAction()
        {
            return _siphonKey == ActionKey.LeftSkill || _siphonKey == ActionKey.RightSkill;
        }

        private bool IsCursorOverClickDangerUi()
        {
            try
            {
                if (Hud == null || Hud.Window == null)
                    return true;

                int x = (int)Math.Round((double)Hud.Window.CursorX);
                int y = (int)Math.Round((double)Hud.Window.CursorY);

                return IsInsideSiphonClickDangerUi(x, y);
            }
            catch { return true; }
        }

        private bool IsCursorInsideSiphonChatCloseWatchArea()
        {
            try
            {
                if (Hud == null || Hud.Window == null)
                    return false;

                float x = (float)Hud.Window.CursorX;
                float y = (float)Hud.Window.CursorY;
                return IsInsideSiphonChatCloseWatchArea(x, y);
            }
            catch { return false; }
        }

        private bool IsInsideSiphonClickDangerUi(int x, int y)
        {
            try
            {
                if (IsInsideGlobalNoHoverUiGuard(x, y)) return true;
                if (IsInsideSiphonBottomCenterMenuGuard(x, y)) return true;

                if (IsInsideNativeActionUiElement(ActionKey.LeftSkill, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.RightSkill, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill1, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill2, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill3, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Skill4, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Heal, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.TownPortal, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Inventory, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.SkillsWindow, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.ParagonWindow, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Map, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.WaypointMap, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Social, x, y)) return true;
                if (IsInsideNativeActionUiElement(ActionKey.Close, x, y)) return true;

                try
                {
                    if (Hud.Inventory != null && IsInsideVisibleUiElement(Hud.Inventory.FollowerMainUiElement, x, y))
                        return true;
                }
                catch { }

                if (IsInsideVisibleUiElement(_chatEditLine, x, y))
                    return true;

                if (IsInsideVisibleUiElement(Hud.Render.ParagonLevelUpSplashTextUiElement, x, y))
                    return true;

                for (int i = 0; i < _clickDangerUiElements.Count; i++)
                {
                    if (IsInsideVisibleUiElement(_clickDangerUiElements[i], x, y))
                        return true;
                }
            }
            catch { return true; }

            return false;
        }

        private bool IsInsideNativeActionUiElement(ActionKey key, int x, int y)
        {
            try { return IsInsideVisibleUiElement(Hud.Render.GetPlayerSkillUiElement(key), x, y); }
            catch { return false; }
        }

        private bool IsInsideVisibleUiElement(IUiElement element, int x, int y)
        {
            try { return element != null && element.Visible && element.CoordinateInsideRectangle(x, y); }
            catch { return false; }
        }

        private bool IsInsideScaledSiphonRect(float x, float y, float left, float top, float right, float bottom)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                return x >= (left * sx) && x <= (right * sx) && y >= (top * sy) && y <= (bottom * sy);
            }
            catch { return false; }
        }

        private bool IsInsideSkillbarHoverGuard(float x, float y)
        {
            return IsInsideScaledSiphonRect(
                x, y,
                SkillbarHoverGuardLeft,
                SkillbarHoverGuardTop,
                SkillbarHoverGuardRight,
                SkillbarHoverGuardBottom);
        }

        private bool IsInsideGlobalNoHoverUiGuard(float x, float y)
        {
            try
            {
                int ix = (int)Math.Round((double)x);
                int iy = (int)Math.Round((double)y);

                // Bottom skillbar / skill tooltip region.
                if (IsInsideSkillbarHoverGuard(x, y)) return true;

                // Custom plugin buttons.
                if (IsInsideHudMenuDefaultButton(ix, iy)) return true;
                if (IsInsideAutoLootIndicatorButton(ix, iy)) return true;

                // Tightened REV72 fallback masks.
                if (IsInsideSiphonBottomRightMenuGuard(x, y)) return true;
                if (IsInsideSiphonBottomLeftMenuGuard(x, y)) return true;
                if (IsInsideSiphonTopRightPanelGuard(x, y)) return true;
                if (IsInsideSiphonTopRightIconGuard(x, y)) return true;
                if (IsInsideSiphonSocialFlyoutGuard(x, y)) return true;
                if (IsInsideSiphonParagonPlusGuard(x, y)) return true;

                // Portrait/profile/context-menu regions.
                if (IsInsidePlayerPortraitFace(x, y)) return true;
                if (IsInsideSiphonTopLeftPortraitGuard(x, y)) return true;
            }
            catch { return true; }

            return false;
        }

        private bool IsInsideSiphonBottomRightMenuGuard(float x, float y)
        {
            return IsInsideScaledSiphonRect(
                x, y,
                SiphonBottomRightGuardLeft,
                SiphonBottomRightGuardTop,
                SiphonBottomRightGuardRight,
                SiphonBottomRightGuardBottom);
        }

        private bool IsInsideSiphonBottomCenterMenuGuard(float x, float y)
        {
            return false;
        }

        private bool IsInsideSiphonBottomLeftMenuGuard(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                return x <= (SiphonBottomLeftGuardRight * sx) && y >= (SiphonBottomLeftGuardTop * sy);
            }
            catch { return false; }
        }

        private bool IsInsideSiphonChatCloseWatchArea(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                return x <= (SiphonChatWatchRight * sx) && y >= (SiphonChatWatchTop * sy);
            }
            catch { return false; }
        }

        private bool IsInsideSiphonTopRightPanelGuard(float x, float y)
        {
            return IsInsideScaledSiphonRect(
                x, y,
                SiphonTopRightPanelGuardLeft,
                SiphonTopRightPanelGuardTop,
                SiphonTopRightPanelGuardRight,
                SiphonTopRightPanelGuardBottom);
        }

        private bool IsInsideSiphonTopRightIconGuard(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                return x >= (SiphonTopRightIconGuardLeft * sx)
                    && x <= (SiphonTopRightIconGuardRight * sx)
                    && y >= (SiphonTopRightIconGuardTop * sy)
                    && y <= (SiphonTopRightIconGuardBottom * sy);
            }
            catch { return false; }
        }

        private bool IsInsideSiphonParagonPlusGuard(float x, float y)
        {
            try
            {
                bool paragonReady = false;
                try { paragonReady = Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.ParagonPointsAvailableTotal > 0; } catch { }
                try { paragonReady = paragonReady || IsUiVisible(Hud.Render.ParagonLevelUpSplashTextUiElement); } catch { }

                return paragonReady && IsInsideScaledSiphonRect(
                    x, y,
                    SiphonParagonPlusGuardLeft,
                    SiphonParagonPlusGuardTop,
                    SiphonParagonPlusGuardRight,
                    SiphonParagonPlusGuardBottom);
            }
            catch { return false; }
        }

        private bool IsInsideSiphonSocialFlyoutGuard(float x, float y)
        {
            return IsInsideScaledSiphonRect(
                x, y,
                SiphonSocialFlyoutGuardLeft,
                SiphonSocialFlyoutGuardTop,
                SiphonSocialFlyoutGuardRight,
                SiphonSocialFlyoutGuardBottom);
        }

        private bool IsInsideSiphonTopLeftPortraitGuard(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                return x <= (SiphonTopLeftPortraitGuardWidth * sx) && y <= (SiphonTopLeftPortraitGuardHeight * sy);
            }
            catch { return false; }
        }

        private bool IsInsideAutoLootIndicatorButton(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / SiphonUiGuardReferenceWidth;
                float sy = Hud.Window.Size.Height / SiphonUiGuardReferenceHeight;
                float scale = Math.Min(sx, sy);
                float size = AutoLootButtonDefaultSize * scale;
                float cx = Hud.Window.Size.Width * (AutoLootButtonDefaultX / SiphonUiGuardReferenceWidth);
                float cy = Hud.Window.Size.Height * (AutoLootButtonDefaultY / SiphonUiGuardReferenceHeight);
                float half = size * 0.5f + AutoLootButtonGuardPadPx;

                return x >= cx - half && x <= cx + half && y >= cy - half && y <= cy + half;
            }
            catch { return false; }
        }

        private bool IsInsideHudMenuDefaultButton(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / HudMenuButtonReferenceWidth;
                float sy = Hud.Window.Size.Height / HudMenuButtonReferenceHeight;
                float pad = HudMenuButtonGuardPadPx;

                float left = (HudMenuButtonDefaultX * sx) - pad;
                float top = (HudMenuButtonDefaultY * sy) - pad;
                float right = ((HudMenuButtonDefaultX + HudMenuButtonDefaultSize) * sx) + pad;
                float bottom = ((HudMenuButtonDefaultY + HudMenuButtonDefaultSize) * sy) + pad;

                return x >= left && x <= right && y >= top && y <= bottom;
            }
            catch { return false; }
        }

        #endregion

        #region Input

        private bool EnsureSiphonPulseAnchorForBuildOrRefresh(IMonster preferredTarget, int tick)
        {
            if (!IsMouseSiphonAction())
                return true;

            if (IsManualCursorOverrideActive(tick))
                return IsCursorOnValidBuildOrRefreshSiphonAnchor() && !IsCursorOverClickDangerUi();

            if (!IsCursorOverClickDangerUi() && IsCursorOnValidBuildOrRefreshSiphonAnchor())
            {
                try
                {
                    var selected = Hud.Game.SelectedMonster2;
                    if (!ShouldForceInariusRetarget(selected, preferredTarget))
                        return true;
                }
                catch { return true; }
            }

            IMonster anchor = PickBuildOrRefreshSiphonAnchorTarget(preferredTarget);
            if (!IsAliveTarget(anchor))
                return false;

            return TryMoveToSafeBuildOrRefreshSiphonPulseTarget(anchor, tick);
        }

        private bool EnsureUrgentRefreshSiphonAnchor(IMonster preferredTarget, int tick)
        {
            if (!IsMouseSiphonAction())
                return true;

            if (IsManualCursorOverrideActive(tick))
            {
                if (IsCursorOnValidUrgentRefreshSiphonAnchor() && !IsCursorOverClickDangerUi())
                    return true;

                // Urgent Power Shift refresh is allowed to reclaim cursor ownership after movement.
                _manualCursorOverrideUntilTick = 0;
                _movementDisengageUntilTick = 0;
            }

            if (!IsCursorOverClickDangerUi() && IsCursorOnValidUrgentRefreshSiphonAnchor())
                return true;

            IMonster anchor = PickUrgentRefreshSiphonAnchorTarget(preferredTarget, tick);
            if (!IsAliveTarget(anchor))
                return false;

            return TryMoveToSafeUrgentRefreshSiphonPulseTarget(anchor, tick);
        }

        private bool IsCursorOnValidUrgentRefreshSiphonAnchor()
        {
            try
            {
                var selected = Hud.Game.SelectedMonster2;
                return IsValidUrgentRefreshSiphonAnchorTarget(selected);
            }
            catch { return false; }
        }

        private bool IsValidUrgentRefreshSiphonAnchorTarget(IMonster monster)
        {
            if (IsValidSiphonAnchorTarget(monster, true))
                return true;

            if (!IsAliveTarget(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            return StrictInariusDistance(monster) <= RefreshSiphonFallbackRangeYards;
        }

        private IMonster PickUrgentRefreshSiphonAnchorTarget(IMonster preferredTarget, int tick)
        {
            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsValidUrgentRefreshSiphonAnchorTarget(selected))
                    return selected;
            }
            catch { }

            if (IsValidUrgentRefreshSiphonAnchorTarget(preferredTarget))
                return preferredTarget;

            IMonster target = null;
            try { target = PickForcedInariusCorrectionTarget(tick); } catch { }
            if (IsValidUrgentRefreshSiphonAnchorTarget(target))
                return target;

            return FindNearestUrgentRefreshSiphonAnchor();
        }

        private IMonster FindNearestUrgentRefreshSiphonAnchor()
        {
            IMonster best = null;
            float bestDistance = float.MaxValue;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsValidUrgentRefreshSiphonAnchorTarget(monster))
                        continue;

                    float d = StrictInariusDistance(monster);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private bool TryMoveToSafeUrgentRefreshSiphonPulseTarget(IMonster target, int tick)
        {
            try
            {
                if (!IsValidUrgentRefreshSiphonAnchorTarget(target))
                    return false;

                float x, y;
                if (!TryGetMonsterScreen(target, out x, out y))
                    return false;

                uint acd = GetMonsterAcdId(target);
                bool eliteLike = IsLeader(target) || IsBossLike(target) || IsEliteMinionLike(target);

                float[] dx = { 0f, -12f, 12f, 0f, 0f, -24f, 24f, -36f, 36f };
                float[] dy = { 0f, -8f, -8f, -18f, 12f, -12f, -12f, -24f, -24f };

                for (int i = 0; i < dx.Length; i++)
                {
                    float px = x + dx[i];
                    float py = y + dy[i];
                    int ix = (int)Math.Round((double)px);
                    int iy = (int)Math.Round((double)py);

                    if (IsInsideSiphonClickDangerUi(ix, iy))
                        continue;

                    if (SafeMouseMove(px, py, tick))
                    {
                        _lastMouseMoveTick = tick;
                        RememberTargetRestoreAnchor(acd, eliteLike, px, py, tick);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool IsCursorOnValidBuildOrRefreshSiphonAnchor()
        {
            try
            {
                var selected = Hud.Game.SelectedMonster2;
                return IsValidBuildOrRefreshSiphonAnchorTarget(selected);
            }
            catch { return false; }
        }

        private bool IsValidBuildOrRefreshSiphonAnchorTarget(IMonster monster)
        {
            if (IsValidSiphonAnchorTarget(monster, true))
                return true;

            if (!IsAliveTarget(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            // Normal Inarius damage targeting owns the cursor whenever anything is inside the ring.
            // Far anchors are allowed only as a Siphon Blood stack/refresh fallback so Power Shift
            // does not drop when the player is between packs or slightly outside the damage radius.
            if (AnyAliveMonsterInsideInariusRawRangeExists())
                return false;

            return StrictInariusDistance(monster) <= RefreshSiphonFallbackRangeYards;
        }

        private IMonster PickBuildOrRefreshSiphonAnchorTarget(IMonster preferredTarget)
        {
            IMonster strict = null;
            try { strict = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, Hud.Game.CurrentGameTick); } catch { }
            if (IsValidBuildOrRefreshSiphonAnchorTarget(strict))
                return strict;

            if (IsValidBuildOrRefreshSiphonAnchorTarget(preferredTarget))
                return preferredTarget;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsValidBuildOrRefreshSiphonAnchorTarget(selected))
                    return selected;
            }
            catch { }

            return FindNearestBuildOrRefreshSiphonAnchor();
        }

        private IMonster FindNearestBuildOrRefreshSiphonAnchor()
        {
            if (AnyAliveMonsterInsideInariusRawRangeExists())
                return null;

            IMonster best = null;
            float bestDistance = float.MaxValue;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsValidBuildOrRefreshSiphonAnchorTarget(monster))
                        continue;

                    float d = StrictInariusDistance(monster);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private bool TryMoveToSafeBuildOrRefreshSiphonPulseTarget(IMonster target, int tick)
        {
            try
            {
                if (!IsValidBuildOrRefreshSiphonAnchorTarget(target))
                    return false;

                float x, y;
                if (!TryGetMonsterScreen(target, out x, out y))
                    return false;

                uint acd = GetMonsterAcdId(target);
                bool eliteLike = IsLeader(target) || IsBossLike(target) || IsEliteMinionLike(target);

                float[] dx = { 0f, -12f, 12f, 0f, 0f, -24f, 24f, -36f, 36f };
                float[] dy = { 0f, -8f, -8f, -18f, 12f, -12f, -12f, -24f, -24f };

                for (int i = 0; i < dx.Length; i++)
                {
                    float px = x + dx[i];
                    float py = y + dy[i];
                    int ix = (int)Math.Round((double)px);
                    int iy = (int)Math.Round((double)py);

                    if (IsInsideSiphonClickDangerUi(ix, iy))
                        continue;

                    if (SafeMouseMove(px, py, tick))
                    {
                        _lastMouseMoveTick = tick;
                        RememberTargetRestoreAnchor(acd, eliteLike, px, py, tick);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool EnsureSiphonPulseAnchor(IMonster preferredTarget, int tick)
        {
            if (!IsMouseSiphonAction())
                return true;

            if (IsManualCursorOverrideActive(tick))
                return IsCursorOnValidSiphonAnchor() && !IsCursorOverClickDangerUi();

            if (!IsCursorOverClickDangerUi() && IsCursorOnValidSiphonAnchor())
                return true;

            IMonster anchor = PickSiphonAnchorTarget(preferredTarget);
            if (!IsAliveTarget(anchor))
                return false;

            return TryMoveToSafeSiphonPulseTarget(anchor, tick);
        }

        private bool IsCursorOnValidSiphonAnchor()
        {
            try
            {
                var selected = Hud.Game.SelectedMonster2;
                return IsValidSiphonAnchorTarget(selected, true);
            }
            catch { return false; }
        }

        private bool IsValidSiphonAnchorTarget(IMonster monster, bool allowTrash)
        {
            if (!IsAliveTarget(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (IsJuggernautPack(monster) && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            if (!IsWithinInariusTargetRange(monster))
                return false;

            if (IsBossLike(monster) || IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return allowTrash && IncludeTrashTargets;
        }

        private IMonster PickSiphonAnchorTarget(IMonster preferredTarget)
        {
            IMonster strict = null;
            try { strict = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, Hud.Game.CurrentGameTick); } catch { }
            if (IsValidSiphonAnchorTarget(strict, true))
                return strict;

            if (IsValidSiphonAnchorTarget(preferredTarget, true))
                return preferredTarget;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsValidSiphonAnchorTarget(selected, true))
                    return selected;
            }
            catch { }

            return null;
        }

        private bool TryMoveToSafeSiphonPulseTarget(int tick)
        {
            return TryMoveToSafeSiphonPulseTarget(PickSiphonAnchorTarget(GetSafeSiphonPulseTarget()), tick);
        }

        private bool TryMoveToSafeSiphonPulseTarget(IMonster target, int tick)
        {
            try
            {
                if (!IsValidSiphonAnchorTarget(target, true))
                    return false;

                float x, y;
                if (!TryGetMonsterScreen(target, out x, out y))
                    return false;

                uint acd = GetMonsterAcdId(target);
                bool eliteLike = IsLeader(target) || IsBossLike(target) || IsEliteMinionLike(target);

                float[] dx = { 0f, -12f, 12f, 0f, 0f, -24f, 24f, -36f, 36f };
                float[] dy = { 0f, -8f, -8f, -18f, 12f, -12f, -12f, -24f, -24f };

                for (int i = 0; i < dx.Length; i++)
                {
                    float px = x + dx[i];
                    float py = y + dy[i];
                    int ix = (int)Math.Round((double)px);
                    int iy = (int)Math.Round((double)py);

                    if (IsInsideSiphonClickDangerUi(ix, iy))
                        continue;

                    if (SafeMouseMove(px, py, tick))
                    {
                        _lastMouseMoveTick = tick;
                        RememberTargetRestoreAnchor(acd, eliteLike, px, py, tick);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private IMonster GetSafeSiphonPulseTarget()
        {
            IMonster target = null;

            try
            {
                target = PickStrictInariusDamageTarget(Hud.Game.AliveMonsters, Hud.Game.CurrentGameTick);
                if (IsValidSiphonAnchorTarget(target, true))
                    return target;
            }
            catch { }

            if (_lastSiphonTargetAcdId != 0)
                target = FindAliveMonsterByAcdId(_lastSiphonTargetAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_lockedTargetAcdId != 0)
                target = FindAliveMonsterByAcdId(_lockedTargetAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_manualJuggerLockAcdId != 0)
                target = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_stableLockAcdId != 0)
                target = FindAliveMonsterByAcdId(_stableLockAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_cachedHoverAcdId != 0)
                target = FindAliveMonsterByAcdId(_cachedHoverAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_lastHoverAcdId != 0)
                target = FindAliveMonsterByAcdId(_lastHoverAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_reacquireAcdId != 0)
                target = FindAliveMonsterByAcdId(_reacquireAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            if (_snapPhaseAcdId != 0)
                target = FindAliveMonsterByAcdId(_snapPhaseAcdId);
            if (IsValidSiphonAnchorTarget(target, true)) return target;

            return GetManualSiphonTargetFallback();
        }

        private bool PulseSiphon(int tick, int intervalTicks, int downTicks, bool allowEarlyRelease)
        {
            if (_pulseActive)
                return false;

            if (_nextPulseTick > 0 && tick < _nextPulseTick)
                return false;

            // Caller-selected cadence owns safety, like Pestilence RGK. Maintain/no-target paths still
            // pass 400 ms intervals, while fast build and late-refresh attempts may use the quick cadence.
            int safetyGapTicks = Math.Max(1, intervalTicks);
            if (_lastSiphonPulseTick > 0 && unchecked(tick - _lastSiphonPulseTick) < safetyGapTicks)
                return false;

            if (IsMouseSiphonAction() && IsManualCursorOverrideActive(tick) && !IsCursorOnValidSiphonAnchor())
            {
                _nextPulseTick = tick + UiClickGuardRetryTicks;
                return false;
            }

            if (IsMouseSiphonAction() && IsCursorOverClickDangerUi())
            {
                if (!TryMoveToSafeSiphonPulseTarget(tick) || IsCursorOverClickDangerUi())
                {
                    _nextPulseTick = tick + UiClickGuardRetryTicks;
                    return false;
                }
            }

            _nextPulseTick = tick + Math.Max(1, intervalTicks);
            _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);

            try
            {
                BeginStandstillIfNeeded();
                SendActionDown(_siphonKey);
                if (IsMouseSiphonAction())
                {
                    _lastOwnedMouseSiphonPulseTick = tick;
                    if (IsCursorInsideSiphonChatCloseWatchArea())
                        _lastOwnedChatRiskPulseTick = tick;
                }

                _siphonPulseOwned = true;
                _pulseActive = true;
                _pulseDownUntilTick = tick + Math.Max(1, downTicks);
                _lastSiphonPulseTick = tick;

                if (!allowEarlyRelease)
                    _pulseWasBuild = false;

                return true;
            }
            catch
            {
                StopPulseNow();
                return false;
            }
        }

        private void TickPulseReleaseIfNeeded(int tick)
        {
            if (!_pulseActive)
                return;

            if (IsMouseSiphonAction() && IsCursorOverClickDangerUi())
            {
                ReleasePulseInput();
                _nextPulseTick = tick + UiClickGuardRetryTicks;
                return;
            }

            if (_pulseWasBuild && _pulseBuildTarget > 0)
            {
                int stacks;
                float timeLeft;
                if (TryGetPowerShift(out stacks, out timeLeft) && stacks >= _pulseBuildTarget)
                {
                    ReleasePulseInput();
                    return;
                }
            }

            if (tick >= _pulseDownUntilTick)
                ReleasePulseInput();
        }

        private void ReleasePulseInput()
        {
            try
            {
                if (_siphonPulseOwned && _siphonKeyKnown)
                    SendActionUp(_siphonKey);
            }
            catch { }

            ReleaseStandstillIfOwned();

            _pulseActive = false;
            _siphonPulseOwned = false;
            _pulseDownUntilTick = 0;
            _pulseWasBuild = false;
            _pulseBuildTarget = 0;
        }

        private void StopPulseNow()
        {
            ReleasePulseInput();
            _nextPulseTick = 0;
        }

        private void ResetRuntime(bool releaseInput)
        {
            if (releaseInput)
                StopPulseNow();

            _lockedTargetAcdId = 0;
            _lockedTargetKeepUntilTick = 0;
            _returnToRareAcdId = 0;
            _skipAcdId = 0;
            _skipUntilTick = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _normalScanParkAcdId = 0;
            _normalScanParkStartTick = 0;
            _normalScanLastReprobeTick = 0;
            _lastMouseMoveTick = 0;
            _forcedInariusSnapAcdId = 0;
            _forcedInariusSnapUntilTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _savedCursorX = 0f;
            _savedCursorY = 0f;
            _engageStartTick = 0;
            _targetRestoreAnchorAcdId = 0;
            _targetRestoreAnchorX = 0f;
            _targetRestoreAnchorY = 0f;
            _targetRestoreAnchorTick = 0;
            _pendingRestoreTick = 0;
            _pendingRestoreX = 0f;
            _pendingRestoreY = 0f;
            _lastHoverAcdId = 0;
            _lastHoverTick = 0;
            _lastHoverDx = 0f;
            _lastHoverDy = 0f;
            _rankedProbeCount = 0;
            _lastSnapAttemptAcd = 0;
            _lastSnapAttemptBin = -1;
            _lastSnapAttemptZone = -1;
            _lastSnapAttemptTick = 0;
            ClearProbeZoneState();
            _stableLockAcdId = 0;
            _stableLockDx = 0f;
            _stableLockDy = 0f;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockDx = 0f;
            _softLockDy = 0f;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverDx = 0f;
            _cachedHoverDy = 0f;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _unhoverableAcdId = 0;
            _unhoverableUntilTick = 0;
            _lastFailedLeaderAcdId = 0;
            _lastFailedLeaderTick = 0;
            _lastFailedLeaderCount = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
            _skipLeaderAcdId = 0;
            _skipLeaderUntilTick = 0;
            _badHoverAcdId = 0;
            _badHoverCount = 0;
            _badHoverLastTick = 0;
            _movementDisengageUntilTick = 0;
            _manualCursorOverrideUntilTick = 0;
            _lastMovementStopSiphonTick = 0;
            _lastPluginCursorX = 0f;
            _lastPluginCursorY = 0f;
            _lastPluginCursorMoveTick = 0;
            _lastLiveLeaderCount = 0;
            _postEliteClearPauseUntilTick = 0;
            _solverProfileAcd = 0;
            _solverOccLeft = _solverOccRight = _solverOccUp = _solverOccDown = 0f;
            _solverBigLeft = _solverBigRight = _solverBigUp = _solverBigDown = 0f;
            _solverBlockerCx = _solverBlockerCy = 0f;
            _solverBlockerWeightTotal = 0f;
            _solverBlockerCount = 0;
            _forceMoveWasDown = false;
            _lastLotdActive = false;
            _siphonKeyWasDown = false;
            _lanceWasDown = false;
            _engageRefreshConsumed = false;
            _lastSiphonTargetAcdId = 0;
            _targetSwitchRefreshAcdId = 0;
            ClearLateRefreshIntent();
            ClearFastBuildGuard();
            _fastBuildThrottleUntilTick = 0;
            ClearNoTargetAnchorLimiter(false);
            _lastPulseWasNoTargetAnchor = false;
            _lastOwnedMouseSiphonPulseTick = 0;
            _lastOwnedChatRiskPulseTick = 0;
            _lastSiphonPulseTick = 0;
            _lastPowerShiftReadTick = 0;
            _lastPowerShiftStacks = 0;
            _lastPowerShiftTimeLeft = 0f;
            _lastOwnedUiCloseTick = 0;
            _lastCorpseScanTick = 0;
            _cachedCorpsesAvailable = false;
            _lateRefreshPulseConsumed = false;
            _siphonAssistUntilTick = 0;
            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _lanceKey = ActionKey.Unknown;
            _siphonKey = ActionKey.Unknown;
        }

        private bool IsActionPhysicallyDown(ActionKey key)
        {
            ushort vk = VirtualKeyForAction(key);
            return vk != 0 && IsKeyPhysicallyDown(vk);
        }

        private ushort VirtualKeyForAction(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0x01;
                case ActionKey.RightSkill: return 0x02;
                case ActionKey.Skill1: return Skill1VirtualKey;
                case ActionKey.Skill2: return Skill2VirtualKey;
                case ActionKey.Skill3: return Skill3VirtualKey;
                case ActionKey.Skill4: return Skill4VirtualKey;
                default: return 0;
            }
        }

        private void BeginStandstillIfNeeded()
        {
            if (ForceStandstillVirtualKey == 0)
                return;

            if (IsKeyPhysicallyDown(ForceStandstillVirtualKey))
                return;

            SendKeyDown(ForceStandstillVirtualKey);
            _standstillOwned = true;
        }

        private void ReleaseStandstillIfOwned()
        {
            if (!_standstillOwned)
                return;

            SendKeyUp(ForceStandstillVirtualKey);
            _standstillOwned = false;
        }

        private void SendActionDown(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill:
                    SendMouse(LeftDown);
                    break;
                case ActionKey.RightSkill:
                    SendMouse(RightDown);
                    break;
                case ActionKey.Skill1:
                case ActionKey.Skill2:
                case ActionKey.Skill3:
                case ActionKey.Skill4:
                    SendKeyDown(VirtualKeyForAction(key));
                    break;
            }
        }

        private void SendActionUp(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill:
                    SendMouse(LeftUp);
                    break;
                case ActionKey.RightSkill:
                    SendMouse(RightUp);
                    break;
                case ActionKey.Skill1:
                case ActionKey.Skill2:
                case ActionKey.Skill3:
                case ActionKey.Skill4:
                    SendKeyUp(VirtualKeyForAction(key));
                    break;
            }
        }

        private static void SendKeyDown(ushort virtualKey)
        {
            SendKey(virtualKey, false);
        }

        private static void SendKeyUp(ushort virtualKey)
        {
            SendKey(virtualKey, true);
        }

        private static void SendKey(ushort virtualKey, bool keyUp)
        {
            if (virtualKey == 0)
                return;

            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;
            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        private static void SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;
            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        #endregion

        #region Small Utilities / Win32

        private int MsToTicks(int ms)
        {
            if (ms <= 0)
                return 1;

            return Math.Max(1, (int)Math.Round(ms / 1000.0 * 60.0));
        }

        private int ClampStackTarget(int value)
        {
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }

        private int GetActivePowerShiftTarget()
        {
            return ClampStackTarget(IsBuffActive(PowerPylonBuffSno) ? PowerStackTarget : NormalStackTarget);
        }

        private int ClampRange(int value)
        {
            if (value < 15) return 15;
            if (value > 17) return 17;
            return value;
        }

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint RightDown = 0x0008;
        private const uint RightUp = 0x0010;
        private const uint KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        private static bool IsKeyPhysicallyDown(ushort virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        #endregion
    }
}
