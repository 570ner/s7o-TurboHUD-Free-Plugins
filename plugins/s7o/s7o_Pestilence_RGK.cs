using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // REV28 promotes a confirmed remote Necromancer Siphon primary to the first eligible elite target and exposes its red ring through the existing HUD siphon visual toggle.
    public class s7o_Pestilence_RGK : BasePlugin, IAfterCollectHandler, INewAreaHandler, IInGameWorldPainter
    {
        private const uint PestilenceSetSno = 740282;
        private const int RequiredSetPieces = 6;

        #region Settings

        public int NormalStackTarget { get; private set; }
        public int PowerStackTarget { get; private set; }

        public bool AutoSnapEnabled = true;
        public bool AutoSiphonEnabled = true;
        public bool LateRefreshAssist = true;
        public bool PrioritizeDebuffedElites = true;
        public bool RemoteNecromancerTargetAssist = true;
        public bool ShowRemoteSiphonPrimaryTargetRing = false;
        public bool AggressiveScanMode = false;
        public bool IncludeTrashTargets = true;
        public bool RestoreCursorOnReleaseOrMove = true;
        public int PestilenceRangeYards = 70;
        public bool JuggerHotkeyEnabled = true;
        public ushort JuggerHotkeyVirtualKey = 0x20; // Space
        public bool RgAutoSnapSiphonAssist = false;
        public bool ShowTargetLineReticle = true;
        public bool ZeiCircleEnabled = false;
        public int ZeiCircleColorIndex = 5;
        public int ZeiCircleTone = 2;
        public float ZeiCircleThickness = 2.4f;

        public ushort ForceStandstillVirtualKey = 0x10; // Shift
        public ushort Skill1VirtualKey = 0x31;          // 1
        public ushort Skill2VirtualKey = 0x32;          // 2
        public ushort Skill3VirtualKey = 0x33;          // 3
        public ushort Skill4VirtualKey = 0x34;          // 4

        private const int BuildPulseMs = 100;
        private const int LotdBuildPulseMs = 70;
        private const int MaintainPulseMs = 400;
        private const int LateRefreshWindowMsDefault = 800;
        private const int BossLateRefreshPulseMs = 800;
        private const int ProactiveRefreshWindowMs = 1200;
        private const int RicochetLateRefreshWindowMs = 1100;
        private const int RicochetProactiveRefreshWindowMs = 1500;
        private const int RicochetLateRefreshDownTicks = 6;
        private const int RicochetFirstEngageSiphonDelayTicks = 0;
        private const int PowerShiftBuffIconIndex = 10;
        private const int FirstEngageSiphonDelayTicks = 2;
        private const int LateRefreshIntentExpireTicks = 30;
        private const int LateRefreshIntentRetryTicks = 4;
        private const int LateRefreshIntentMaxAttempts = 4;
        private const float LateRefreshIntentRefreshGainSeconds = 0.35f;
        private const int PulseDownTicks = 2;
        private const int LateRefreshDownTicks = 4;
        private const int LotdPulseDownTicks = 6;
        private const int BossPulseDownTicks = 6;
        private const int CorpseScanIntervalTicks = 10;
        private const int SiphonAssistPauseTicks = 10;
        private const int OpeningSiphonPulseMs = 70;
        private const int OpeningSiphonDownTicks = 1;
        private const int OpeningSiphonRetryWindowTicks = 8;
        private const int OpeningSiphonTapIntentFenceTicks = 12;
        private const int VerifiedHoverHoldTicks = 12;
        private const int VerifiedHoverRetryIntervalTicks = 2;
        private const int VerifiedHoverMaxRetries = 4;
        private const int RecentLeaderPriorityLatchTicks = 6;
        private const int LeaderReacquireWindowTicks = 30;
        private const int CursorRestoreShortEngageTicks = 120; // HUD_MENU AutoSnap restore window (~2s at 60 tps)
        private const float CursorRestoreMinMovePxSq = 16f;
        private const float PestilenceTargetRangeLeewayYards = 2.0f;
        private const float CorpseScanRangeYards = 120f;
        private const float JuggerLockCircleYards = 10f;
        private const float EliteLockCircleYards = 5f;
        private const int JuggerLockDashCount = 28;
        private const int EliteLockDashCount = 24;
        private const float JuggerLockDashFill = 0.46f;
        private const float EliteLockDashFill = 0.42f;
        private const double JuggerLockRotationSeconds = 6.5d;
        private const double EliteLockRotationSeconds = 4.75d;
        private const int NoTargetAnchorPulseMs = 100;
        private const int NoTargetAnchorDownTicks = 2;
        private const int NoTargetAnchorPulsesPerBurst = 3;
        private const int NoTargetAnchorMaxPulses = 6;
        private const int NoTargetAnchorBurstPauseMs = 500;
        private const float PestilenceEdgePenaltyStart = 15.8f;
        private const float PestilenceEdgePenaltyMax = 0.85f;
        private const float TrashPenaltyStart = 15.0f;
        private const float TrashEdgePenaltyMax = 2.35f;
        private const float EliteHealthPriorityWeight = 12.0f;
        private const float EliteDebuffPriorityBonus = 5.5f;
        private const float EliteMultiTargetMaxSiphonPriorityBonus = 11.0f;
        private const float EliteDebuffedFinishPriorityBonusMax = 5.0f;
        private const float ElitePackDensityRangeYards = 22.0f;
        private const float ElitePackDensityPriorityEach = 1.0f;
        private const float ElitePackDensityPriorityMax = 5.0f;
        private const float EliteNearDistanceWeight = 0.28f;
        private const float EliteFarDistanceWeight = 0.14f;
        private const float LockedEliteStickinessBonus = 6.0f;
        private const float EngagementFocusBaseBonus = 8.0f;
        private const float EngagementFocusDurationBonusMax = 5.0f;
        private const int EngagementFocusDurationRampTicks = 360;
        private const int EngagementFocusLingerTicks = 120;
        private const int RemoteSiphonTargetConfirmSamples = 2;
        private const int RemoteSiphonTargetLingerTicks = 18;
        private const int DeadTargetHandoffTicks = 1;
        private const float SiphonAnchorTrashPenalty = 8.0f;
        private const float EliteLastBluePackFinishBonus = 7.0f;
        private const float EliteLowHpYellowFinishBonus = 4.0f;
        private const float EliteSwitchForceMargin = 6.0f;
        private const int ForcedRetargetTicks = 10;
        private const float BaselineRadiusBottom = 3.15f;
        private const float BaselineHitboxYards = 0.9f;
        private const float HitboxBonusCap = 2.0f;
        private const float BaseHeadPx = 65f;
        private const float BaseChestPx = 35f;
        private const float BaseAbdomenPx = 18f;
        private const float BaseShoulderXPx = 25f;
        private const double PriorityTrashMinRiftProgression = 0.70d;
        private const double PriorityTrashMinProgressionGap = 0.30d;
        private const float PriorityTrashMaxDistanceYards = 50.0f;
        private const int TrashAcquireFailTicks = 30;
        private const int TrashAcquireSkipTicks = 60;
        private const double BigTrashMinRiftProgression = PriorityTrashMinRiftProgression; // compatibility for existing occlusion/probe code

        private const int AdaptiveBinCount = 8;
        private const int AdaptiveCandidateCapacity = 128;
        private const int ProbeZoneCount = 9;
        private const int MinTicksBetweenMouseMoves = 1;
        private const int NormalScanProbeIntervalTicks = 4;
        private const int NormalScanCyclePauseTicks = 12;
        private const int PendingPulseHoverTicks = 36;
        private const float CursorNoOpTolerancePxSq = 2.25f;
        private const float AdjacentEliteHoverMaxScreenPxSq = 36100f;
        private const float AdjacentEliteHoverMaxWorldYards = 22.0f;
        private const int StableLockTicks = 36;
        private const int SoftLockTicks = 12;
        private const int ReacquireWindowTicks = 16;
        private const int AlternateScanWindowTicks = 18;
        private const float TeleportThresholdSq = 100f;
        private const int PostTeleportForceSnapTicks = 15;
        private const int PostTeleportSelectionInvalidateTicks = 90;
        private const int ManualTargetLingerTicks = 180;
        private const int MovementDisengageTicks = 8;
        private const int MovementDisengageAfterCursorOverrideTicks = 4;
        private const int ManualCursorOverridePauseTicks = 18;
        private const int LockedHoverManualOverridePauseTicks = 6;
        private const int LeaderNormalReprobeTicks = 8;
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

        // Conservative click guard for the bottom skillbar / skill tooltip region.
        // Autosnap hover and cursor restoration may cross this area because they do not
        // send a new click; Siphon pulse placement remains blocked here.
        private const float SkillbarClickGuardLeft = 610f;
        private const float SkillbarClickGuardRight = 1325f;
        private const float SkillbarClickGuardTop = 875f;
        private const float SkillbarClickGuardBottom = 1080f;
        private const float SiphonBottomRightGuardLeft = 1768f;
        private const float SiphonBottomRightGuardRight = 1918f;
        private const float SiphonBottomRightGuardTop = 950f;
        private const float SiphonBottomRightGuardBottom = 1080f;


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
        private const float PlayerMoveIntentThresholdSq = 0.030f;
        private const float UserCursorOverrideThresholdSq = 900f;
        private const int UserCursorOverrideMinTicks = 1;
        private const int UserCursorOverrideMaxTicks = 45;
        private const int BadHoverInvalidateTicks = 2;
        private const int LeaderBadHoverInvalidateTicks = 6;
        private const int LeaderPackCoreRetryTicks = 8;
        private const int HoverTruthRecentTicks = 90;
        private const int LearnedHoverProfileCapacity = 96;
        private const int RecentProbePointCapacity = 48;
        private const int RecentProbePointAvoidTicks = 18;
        private const int NormalRecentProbePointAvoidTicks = 90;
        private const float RecentProbePointAvoidPxSq = 100f;
        private const uint PowerPylonBuffSno = 262935u; // Generic_PagesBuffDamage

        // Reflection fallback for pack and affix data not consistently exposed by FreeHUD.
        private static readonly string[] PackPropNames = { "MonsterPack", "Pack", "RiftMonsterPack", "ElitePack", "MonsterPackInfo" };
        private static readonly string[] PackIdPropNames = { "Id", "PackId", "MonsterPackId", "RiftMonsterPackId", "ElitePackId" };
        private static readonly string[] AffixPropNames = { "AffixSnoList", "Affixes", "AffixList" };
        private static readonly Dictionary<Type, PropertyInfo> PackPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> PackIdPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> AffixListPropCache = new Dictionary<Type, PropertyInfo>(16);
        private static readonly uint[] IdentityAttributeModifiers = { 0u, 0xFFFFFu, 0xFFFFFFFFu, 2147483647u };

        #endregion

        #region Runtime State

        private uint _snoCorpseLance;
        private uint _snoSiphonBlood;
        private uint _snoLandOfTheDead;

        private ActionKey _lanceKey = ActionKey.Unknown;
        private ActionKey _siphonKey = ActionKey.Unknown;
        private bool _lanceKeyKnown;
        private bool _siphonKeyKnown;
        private bool _corpseLanceRuneKnown;
        private byte _corpseLanceRune;
        private bool _corpseLanceRicochet;
        private string _corpseLanceRuneNameEnglish;

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

        private uint _lockedTargetAcdId;
        private int _lockedTargetKeepUntilTick;
        private const int LockedTargetKeepTicks = 600; // ~10s at 60 tps; mirrors LM leader lock persistence.
        private uint _returnToRareAcdId;
        private uint _skipAcdId;
        private int _skipUntilTick;
        private uint _trashAcquireAcdId;
        private int _trashAcquireStartTick;
        private uint _snapPhaseAcdId;
        private int _snapPhase;
        private int _normalScanPauseUntilTick;
        private int _lastMouseMoveTick;
        private bool _cursorOwned;
        private bool _cursorWasMovedByPlugin;
        private bool _haveSavedCursor;
        private bool _cursorRestoreSuppressedForEngagement;
        private float _savedCursorX;
        private float _savedCursorY;
        private int _engageStartTick;
        private uint _cursorRestoreSegmentTargetAcdId;
        private bool _cursorRestoreSegmentLongHoldEligible;
        private uint _lastHoverAcdId;
        private int _lastHoverTick;
        private float _lastHoverDx;
        private float _lastHoverDy;

        private readonly float[] _rankedProbeDx = new float[AdaptiveCandidateCapacity];
        private readonly float[] _rankedProbeDy = new float[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeBin = new int[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeZone = new int[AdaptiveCandidateCapacity];
        private readonly float[] _rankedProbeScore = new float[AdaptiveCandidateCapacity];
        private readonly float[] _adaptiveBinGood = new float[AdaptiveBinCount];
        private readonly float[] _adaptiveBinBad = new float[AdaptiveBinCount];
        private uint _adaptiveModelSno;
        private readonly Dictionary<uint, LearnedHoverProfile> _learnedHoverProfiles = new Dictionary<uint, LearnedHoverProfile>(LearnedHoverProfileCapacity);
        private readonly float[] _recentProbePointX = new float[RecentProbePointCapacity];
        private readonly float[] _recentProbePointY = new float[RecentProbePointCapacity];
        private readonly int[] _recentProbePointTick = new int[RecentProbePointCapacity];
        private int _recentProbePointNext;
        private int _rankedProbeCount;
        private uint _lastSnapAttemptAcd;
        private int _lastSnapAttemptBin = -1;
        private int _lastSnapAttemptZone = -1;
        private int _lastSnapAttemptTick;
        private int _lastEvaluatedProbeTick;
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
        private uint _alternateScanAcdId;
        private int _alternateScanUntilTick;
        private uint _pendingPulseHoverAcdId;
        private float _pendingPulseHoverDx;
        private float _pendingPulseHoverDy;
        private int _pendingPulseHoverUntilTick;
        private uint _verifiedHoverAcdId;
        private float _verifiedHoverScreenX;
        private float _verifiedHoverScreenY;
        private float _verifiedHoverTargetScreenX;
        private float _verifiedHoverTargetScreenY;
        private bool _verifiedHoverHasTargetAnchor;
        private int _verifiedHoverUntilTick;
        private int _verifiedHoverLastTryTick;
        private int _verifiedHoverRetryCount;
        private int _lastPassiveHoverCaptureTick;
        private uint _lastPassiveHoverCaptureAcdId;
        private uint _recentPriorityLeaderAcdId;
        private int _recentPriorityLeaderUntilTick;
        private uint _engagementFocusAcdId;
        private int _engagementFocusStartTick;
        private int _engagementFocusUntilTick;
        private uint _remoteSiphonCandidateAcdId;
        private int _remoteSiphonCandidateSamples;
        private uint _remoteSiphonTargetAcdId;
        private int _remoteSiphonTargetUntilTick;
        private int _siphonAffectedTargetCount;
        private int _deadTargetHandoffUntilTick;

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
        private uint _postTeleportReacquireAcdId;
        private int _postTeleportReacquireUntilTick;
        private int _postTeleportReacquireMoveTick;
        private bool _movementEscapeLatched;
        private bool _openingSiphonPending;
        private int _openingSiphonExpireTick;
        private bool _openingSiphonHandoffPending;
        private int _openingSiphonHandoffUntilTick;
        private bool _openingSiphonStartedWhileMoving;
        private int _lastMovementStopSiphonTick;
        private bool _siphonKeyWasDown;
        private bool _lanceWasDown;
        private bool _engageRefreshConsumed;
        private uint _lastSiphonTargetAcdId;
        private uint _manualJuggerLockAcdId;
        private int _manualTargetUntilTick;
        private uint _badHoverAcdId;
        private int _badHoverCount;
        private int _badHoverLastTick;
        private uint _leaderPackCoreRetryAcdId;
        private int _leaderPackCoreRetryTick;
        private uint _forcedSnapAcdId;
        private int _forcedSnapUntilTick;
        private int _movementDisengageUntilTick;
        private int _manualCursorOverrideUntilTick;
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
        private IBrush _targetLineOutlineBrush;
        private IBrush _targetLineBrush;
        private IBrush _targetReticleDotBrush;
        private IBrush _targetReticleOutlineBrush;
        private IBrush _zeiCircleBrush;
        private IBrush _zeiCircleOutlineBrush;
        private IBrush _remoteSiphonPrimaryBrush;
        private IBrush _remoteSiphonPrimaryOutlineBrush;
        private int _zeiCircleCachedColor = int.MinValue;
        private int _zeiCircleCachedTone = int.MinValue;
        private float _zeiCircleCachedThickness = -1f;
        private uint _targetSwitchRefreshAcdId;
        private bool _lateRefreshIntentActive;
        private bool _lateRefreshIntentFromEngage;
        private int _lateRefreshIntentReadyTick;
        private int _lateRefreshIntentExpireTick;
        private int _lateRefreshIntentAttempts;
        private int _lateRefreshIntentNextAttemptTick;
        private float _lateRefreshIntentStartTimeLeft;
        private int _noTargetAnchorPulses;
        private int _noTargetAnchorLastTick;
        private bool _lastPulseWasNoTargetAnchor;

        private int _lastCorpseScanTick;
        private bool _cachedCorpsesAvailable;
        private int _lastSeenGameTick;

        private IUiElement _chatEditLine;
        private int _lastOwnedMouseSiphonPulseTick;
        private int _lastOwnedChatRiskPulseTick;
        private int _lastOwnedUiCloseTick;
        private readonly List<IUiElement> _clickDangerUiElements = new List<IUiElement>();

        #endregion

        public s7o_Pestilence_RGK()
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
            try { _snoLandOfTheDead = hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno; } catch { _snoLandOfTheDead = 465839u; }

            try { _chatEditLine = Hud.Render.GetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline"); } catch { }
            RegisterClickDangerUiElements();
            try
            {
                _juggerCircleOutlineBrush = Hud.Render.CreateBrush(235, 0, 0, 0, 7.0f);
                _juggerCircleBrush = Hud.Render.CreateBrush(245, 255, 132, 0, 3.4f);
                _eliteCircleOutlineBrush = Hud.Render.CreateBrush(230, 0, 0, 0, 6.0f);
                _eliteCircleBrush = Hud.Render.CreateBrush(245, 0, 255, 90, 3.0f);
                _targetLineOutlineBrush = Hud.Render.CreateBrush(205, 0, 0, 0, 5.0f);
                _targetLineBrush = Hud.Render.CreateBrush(245, 40, 255, 80, 2.4f);
                _targetReticleDotBrush = Hud.Render.CreateBrush(248, 255, 45, 35, 5.0f);
                _targetReticleOutlineBrush = Hud.Render.CreateBrush(235, 40, 255, 80, 1.45f);
                _remoteSiphonPrimaryOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 4.8f);
                _remoteSiphonPrimaryBrush = Hud.Render.CreateBrush(250, 255, 35, 35, 3.5f);
                RebuildZeiCircleBrushes();
            }
            catch { }
        }

        public void ConfigureZeiCircle(bool enabled, int colorIndex, int tone, float thickness)
        {
            ZeiCircleEnabled = enabled;
            ZeiCircleColorIndex = ClampInt(colorIndex, 0, 7);
            ZeiCircleTone = ClampInt(tone, 0, 10);
            ZeiCircleThickness = ClampFloat(thickness, 0.5f, 8.0f);
            RebuildZeiCircleBrushes();
        }

        private void RebuildZeiCircleBrushes()
        {
            if (Hud == null || Hud.Render == null)
                return;

            if (_zeiCircleBrush != null
                && _zeiCircleCachedColor == ZeiCircleColorIndex
                && _zeiCircleCachedTone == ZeiCircleTone
                && Math.Abs(_zeiCircleCachedThickness - ZeiCircleThickness) < 0.01f)
                return;

            Color c = GetTonedPaletteColor(ZeiCircleColorIndex, ZeiCircleTone);
            float line = ClampFloat(ZeiCircleThickness, 0.5f, 8.0f);
            _zeiCircleOutlineBrush = Hud.Render.CreateBrush(190, 0, 0, 0, line + 2.0f);
            _zeiCircleBrush = Hud.Render.CreateBrush(220, c.R, c.G, c.B, line);
            _zeiCircleCachedColor = ZeiCircleColorIndex;
            _zeiCircleCachedTone = ZeiCircleTone;
            _zeiCircleCachedThickness = ZeiCircleThickness;
        }

        private Color GetTonedPaletteColor(int colorIndex, int tone)
        {
            int[,] palette = new int[,]
            {
                { 220,  48,  42 },
                { 255, 142,  36 },
                { 255, 214,  42 },
                {  60, 220,  70 },
                {  50, 160, 255 },
                { 150,  85, 255 },
                { 235, 235, 235 },
                {  15,  18,  20 }
            };

            int idx = ClampInt(colorIndex, 0, 7);
            int t = ClampInt(tone, 0, 10);
            float factor = 0.45f + t * 0.11f;
            return Color.FromArgb(255,
                ClampByte((int)(palette[idx, 0] * factor)),
                ClampByte((int)(palette[idx, 1] * factor)),
                ClampByte((int)(palette[idx, 2] * factor)));
        }

        private byte ClampByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }


        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards)
        {
            Configure(normalStacks, powerStacks, AutoSnapEnabled, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, pestilenceRangeYards, JuggerHotkeyEnabled, JuggerHotkeyVirtualKey, RgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist)
        {
            Configure(normalStacks, powerStacks, AutoSnapEnabled, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, pestilenceRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist, bool ignoredLegacyOption)
        {
            Configure(normalStacks, powerStacks, autoSnap, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, pestilenceRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist, bool ignoredLegacyOption, int ignoredLegacyIntervalMs)
        {
            Configure(normalStacks, powerStacks, autoSnap, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, pestilenceRangeYards, juggerHotkeyEnabled, juggerHotkeyVirtualKey, rgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSnap, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist)
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
            PestilenceRangeYards = ClampRange(pestilenceRangeYards);
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
            Configure(normal, power, AutoSnapEnabled, AutoSiphonEnabled, LateRefreshAssist, PrioritizeDebuffedElites, AggressiveScanMode, IncludeTrashTargets, RestoreCursorOnReleaseOrMove, PestilenceRangeYards, JuggerHotkeyEnabled, JuggerHotkeyVirtualKey, RgAutoSnapSiphonAssist);
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

                if (!ResolveSkillKeys())
                {
                    ReleaseCursorOwnershipWithoutRestore();
                    ResetRuntime(true);
                    return;
                }

                bool lanceHeld = IsActionPhysicallyDown(_lanceKey);
                ProcessJuggerHotkey(tick);

                // Keep the passive red remote-primary ring current even while the local
                // player is not engaging or has AutoSnap disabled. Active AutoSnap
                // refreshes the same state inside PickTarget without a duplicate scan.
                if (ShowRemoteSiphonPrimaryTargetRing && (!lanceHeld || !AutoSnapEnabled))
                    RefreshTargetCoordinationState(tick, CaptureAliveMonsters());

                // Release is the highest-priority edge. Do not run movement intent,
                // hover learning, target cleanup, or another autosnap pass before
                // returning the cursor and releasing owned input.
                if (!lanceHeld)
                {
                    bool engagementActive = _lanceWasDown || _cursorOwned
                        || _pulseActive || _standstillOwned
                        || _openingSiphonPending || _openingSiphonHandoffPending;
                    if (engagementActive)
                        DisengageOnLanceRelease(tick);
                    else
                    {
                        UpdatePassiveEliteHoverCache(tick);
                        ClearDeadTargetState(tick);
                    }
                    return;
                }

                UpdatePlayerMotionState(tick);
                UpdatePassiveEliteHoverCache(tick);
                ClearDeadTargetState(tick);
                TickPulseReleaseIfNeeded(tick);
                RefreshManualEliteLock(tick);

                bool freshManualEngagement = !_lanceWasDown;
                bool openingMovementIntentAtPress = IsMovementDisengageActive() || IsPlayerRunning();
                if (freshManualEngagement)
                {
                    _cursorRestoreSuppressedForEngagement = false;
                    _movementEscapeLatched = false;
                    _movementDisengageUntilTick = 0;
                    _manualCursorOverrideUntilTick = 0;
                    CaptureFreshManualEngagementFocus(tick);
                    _openingSiphonPending = true;
                    _openingSiphonExpireTick = tick + OpeningSiphonRetryWindowTicks;
                    _openingSiphonHandoffPending = false;
                    _openingSiphonStartedWhileMoving = openingMovementIntentAtPress;
                    RefreshCursorRestoreWindowForFreshEngagement(tick);
                }

                bool newLanceEngagement = freshManualEngagement;
                _lanceWasDown = true;

                if (tick < _deadTargetHandoffUntilTick)
                {
                    CancelForcedAutosnap();
                    return;
                }

                if (_openingSiphonPending)
                {
                    if (TryOpeningSiphonPulse(tick))
                    {
                        _openingSiphonHandoffPending = true;
                        _openingSiphonHandoffUntilTick = tick + OpeningSiphonTapIntentFenceTicks;
                        UpdatePassiveEliteHoverCache(tick);
                        return;
                    }

                    if (_openingSiphonPending)
                        return;
                }

                if (_openingSiphonHandoffPending)
                {
                    if (_pulseActive || (tick < _openingSiphonHandoffUntilTick && !OpeningSiphonHandoffConfirmed()))
                    {
                        UpdatePassiveEliteHoverCache(tick);
                        return;
                    }

                    if (_openingSiphonStartedWhileMoving && IsPlayerRunning())
                    {
                        DisengageAutosnapForUserMovement(tick);
                        _movementEscapeLatched = true;
                        return;
                    }

                    _openingSiphonHandoffPending = false;
                    _openingSiphonHandoffUntilTick = 0;
                    _openingSiphonStartedWhileMoving = false;
                    _movementDisengageUntilTick = 0;
                    _manualCursorOverrideUntilTick = 0;
                    newLanceEngagement = true;
                }

                // Input priority: an opening movement-stop pulse gets a deliberate tap-intent fence;
                // after handoff, running still escapes autosnap and manual steering still wins.
                bool movementDisengageActive = IsMovementDisengageActive();
                bool manualCursorLease = IsManualCursorOverrideActive(tick);
                bool userMovementEscape = movementDisengageActive && IsPlayerRunning();

                if (userMovementEscape)
                {
                    DisengageAutosnapForUserMovement(tick);
                    _movementEscapeLatched = true;
                    return;
                }

                if (_movementEscapeLatched)
                {
                    StopPulseNow();
                    return;
                }

                if (manualCursorLease)
                {
                    PauseAutosnapForManualCursor();
                    return;
                }

                if (movementDisengageActive)
                {
                    StopPulseNow();
                    return;
                }

                if (newLanceEngagement)
                    ResumeAutosnapControlOnEngagement(tick);

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

                if (TryRunLateRefreshIntent(tick))
                    return;

                bool delayInitialAutosnapForRefresh = ShouldDelayInitialAutosnapForLateRefresh(newLanceEngagement, manualSiphonDown);
                if (delayInitialAutosnapForRefresh)
                {
                    QueueLateRefreshIntent(tick, true);
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
                    ClearAutosnapLockState();
                    siphonTarget = IsAliveTarget(bossTarget) ? bossTarget : GetManualSiphonTargetFallback();
                }

                if (!IsAliveTarget(siphonTarget))
                    siphonTarget = GetManualSiphonTargetFallback();

                if (!IsAliveTarget(target) && !IsAliveTarget(siphonTarget) && AnyEliteOrMinionCandidateExists())
                    CancelForcedAutosnap();

                RunAutoSiphon(tick, siphonTarget, newLanceEngagement);

                // Capture hover truth created by cursor movement later in this collection.
                UpdatePassiveEliteHoverCache(tick);
            }
            catch
            {
                ResetRuntime(true);
            }
        }


        private void DisengageAutosnapForUserMovement(int tick)
        {
            StopPulseNow();
            CancelForcedAutosnap();

            // Running can be caused by the cursor position autosnap currently owns.
            // Keep the per-target restore anchor intact so a brief release immediately
            // returns control to the direction the player expects. Explicit manual
            // cursor steering still cancels restoration in PauseAutosnapForManualCursor().
            _lastPluginCursorX = 0f;
            _lastPluginCursorY = 0f;
            _lastPluginCursorMoveTick = 0;
        }

        private void PauseAutosnapForManualCursor()
        {
            StopPulseNow();
            CancelForcedAutosnap();
            SuppressCursorRestoreForEngagement();
            _lastPluginCursorX = 0f;
            _lastPluginCursorY = 0f;
            _lastPluginCursorMoveTick = 0;
        }

        private void CancelForcedAutosnap()
        {
            _forcedSnapAcdId = 0;
            _forcedSnapUntilTick = 0;
            _lastMouseMoveTick = 0;
        }

        private void ResumeAutosnapControlOnEngagement(int tick)
        {
            // A fresh press explicitly rearms autosnap after movement or manual steering.
            _manualCursorOverrideUntilTick = 0;
            _movementDisengageUntilTick = 0;
            _lastMouseMoveTick = 0;

            IMonster target = PickImmediateReengageTarget();
            if (!IsAliveTarget(target))
                return;

            uint acd = GetMonsterAcdId(target);
            if (acd == 0)
                return;

            PrepareImmediateRetarget(target, tick);
            LockTarget(target, tick);
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
            TrySnap(target, tick);
        }

        private IMonster PickImmediateReengageTarget()
        {
            IMonster target = GetManualJuggerLockTarget();
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            target = GetEngagementFocusTarget(SafeGameTick());
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_lockedTargetAcdId);
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_reacquireAcdId);
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_stableLockAcdId);
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_cachedHoverAcdId);
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            try { target = Hud.Game.SelectedMonster2; } catch { target = null; }
            if (IsPestilenceImmediateReengageTarget(target))
                return target;

            return null;
        }

        private bool IsPestilenceImmediateReengageTarget(IMonster monster)
        {
            if (!IsAliveTarget(monster) || !IsWithinPestilenceTargetRange(monster))
                return false;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist;

            return IsLeader(monster) || IsEliteMinionLike(monster);
        }

        private void DisengageOnLanceRelease(int tick)
        {
            StopPulseNow();

            // No post-release intent window: brief presses restore immediately,
            // long elite-target holds stay parked, and all plugin input ownership
            // is cleared before this collection returns.
            TryRestoreCursorImmediately(tick);
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
                CancelForcedAutosnap();
            }

            _lastLiveLeaderCount = liveLeaders;

            if (tick > _postEliteClearPauseUntilTick)
                return false;

            ClearAutosnapLockState();
            ReleaseStandstillIfOwned();
            CancelForcedAutosnap();
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

                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
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
            try { return me != null && me.GetSetItemCount(PestilenceSetSno) >= RequiredSetPieces; }
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

            // Rendering passively captures hover truth created after AfterCollect.
            try
            {
                if (CanRun())
                    UpdatePassiveEliteHoverCache(Hud.Game.CurrentGameTick);
            }
            catch { }

            try { DrawZeiCircle(); } catch { }
            try { DrawTargetLineReticle(); } catch { }
            try { DrawRemoteSiphonPrimaryTargetRing(); } catch { }

            IMonster locked = GetManualJuggerLockTarget();
            if (!IsAliveTarget(locked) || locked.FloorCoordinate == null)
                return;

            try
            {
                DrawManualLockCircle(locked);
            }
            catch { }
        }

        private void DrawTargetLineReticle()
        {
            if (!ShowTargetLineReticle || Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return;

            if (!IsTargetLineReticleEngaged())
                return;

            IMonster target = GetTargetLineReticleTarget();
            if (!IsAliveTarget(target) || target.FloorCoordinate == null)
                return;

            IWorldCoordinate me = Hud.Game.Me.FloorCoordinate;
            if (_targetLineOutlineBrush != null)
                _targetLineOutlineBrush.DrawLineWorld(me, target.FloorCoordinate);
            if (_targetLineBrush != null)
                _targetLineBrush.DrawLineWorld(me, target.FloorCoordinate);

            DrawTargetReticleDot(target);
        }

        private bool IsTargetLineReticleEngaged()
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

        private IMonster GetTargetLineReticleTarget()
        {
            IMonster manual = GetManualJuggerLockTarget();
            if (IsAliveTarget(manual))
                return manual;

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }
            if (IsAliveTarget(selected))
                return selected;

            IMonster target = FindAliveMonsterByAcdId(_lastSiphonTargetAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_lockedTargetAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_stableLockAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_cachedHoverAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_lastHoverAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_reacquireAcdId);
            if (IsAliveTarget(target))
                return target;

            target = FindAliveMonsterByAcdId(_snapPhaseAcdId);
            if (IsAliveTarget(target))
                return target;

            return null;
        }

        private void DrawTargetReticleDot(IMonster target)
        {
            if (!IsAliveTarget(target) || target.FloorCoordinate == null)
                return;

            try
            {
                var screen = target.FloorCoordinate.ToScreenCoordinate();
                float cx = screen.X;
                float cy = screen.Y - 4f;
                float pulse = (float)Math.Sin(Hud.Game.CurrentGameTick * 0.34f) * 1.2f;
                float radius = 5.0f + pulse;
                if (radius < 2.5f) radius = 2.5f;

                if (_targetReticleDotBrush != null)
                {
                    _targetReticleDotBrush.DrawEllipse(cx, cy, radius, radius);
                    _targetReticleDotBrush.DrawEllipse(cx, cy, radius * 0.72f, radius * 0.72f);
                    _targetReticleDotBrush.DrawEllipse(cx, cy, radius * 0.46f, radius * 0.46f);
                }

                if (_targetReticleOutlineBrush != null)
                    _targetReticleOutlineBrush.DrawEllipse(cx, cy, radius + 4.2f, radius + 4.2f);
            }
            catch { }
        }

        private void DrawRemoteSiphonPrimaryTargetRing()
        {
            if (!ShowRemoteSiphonPrimaryTargetRing || Hud == null || Hud.Game == null)
                return;

            int tick = SafeGameTick();
            IMonster target = GetConfirmedRemoteSiphonPrimaryTarget(tick);
            if (!IsAliveTarget(target) || target.FloorCoordinate == null)
                return;

            float pulse = (float)Math.Sin(tick * 0.22f) * 2.2f;
            DrawDottedScreenEllipse(target.FloorCoordinate, 58f + pulse, 18, tick * 0.50f, 3.9f);
            DrawDottedScreenEllipse(target.FloorCoordinate, 70f - pulse * 0.35f, 22, -tick * 0.38f, 3.5f);
        }

        private void DrawDottedScreenEllipse(IWorldCoordinate center, float radius, int dots, float rotation, float dotRadius)
        {
            if (center == null || dots <= 0 || _remoteSiphonPrimaryBrush == null)
                return;

            IScreenCoordinate screen = center.ToScreenCoordinate();
            for (int i = 0; i < dots; i++)
            {
                float angle = rotation + (float)(Math.PI * 2.0d * i / dots);
                float x = screen.X + (float)Math.Cos(angle) * radius;
                float y = screen.Y + (float)Math.Sin(angle) * radius * 0.74f;

                if (_remoteSiphonPrimaryOutlineBrush != null)
                    _remoteSiphonPrimaryOutlineBrush.DrawEllipse(x, y, dotRadius + 2.0f, dotRadius + 2.0f);

                _remoteSiphonPrimaryBrush.DrawEllipse(x, y, dotRadius, dotRadius);
            }
        }

        private void DrawZeiCircle()
        {
            if (!ZeiCircleEnabled || Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return;

            RebuildZeiCircleBrushes();
            DrawWorldCircle(Hud.Game.Me.FloorCoordinate, 50.0f, _zeiCircleOutlineBrush);
            DrawWorldCircle(Hud.Game.Me.FloorCoordinate, 50.0f, _zeiCircleBrush);
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
            if (_lanceKeyKnown && _siphonKeyKnown && _corpseLanceRuneKnown)
                return true;

            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _corpseLanceRuneKnown = false;
            _corpseLanceRune = 0;
            _corpseLanceRicochet = false;
            _corpseLanceRuneNameEnglish = null;
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
                        _corpseLanceRuneKnown = true;

                        try { _corpseLanceRune = skill.Rune; } catch { _corpseLanceRune = 0; }
                        try { _corpseLanceRuneNameEnglish = skill.RuneNameEnglish; } catch { _corpseLanceRuneNameEnglish = null; }

                        string runeNameLocalized = null;
                        try { runeNameLocalized = skill.RuneNameLocalized; } catch { runeNameLocalized = null; }

                        _corpseLanceRicochet =
                            (!string.IsNullOrEmpty(_corpseLanceRuneNameEnglish) && _corpseLanceRuneNameEnglish.IndexOf("Ricochet", StringComparison.OrdinalIgnoreCase) >= 0)
                            || (!string.IsNullOrEmpty(runeNameLocalized) && runeNameLocalized.IndexOf("Ricochet", StringComparison.OrdinalIgnoreCase) >= 0);
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

        private bool TryOpeningSiphonPulse(int tick)
        {
            if (!_openingSiphonPending)
                return false;

            if (tick > _openingSiphonExpireTick)
            {
                _openingSiphonPending = false;
                _openingSiphonExpireTick = 0;
                return false;
            }

            if (!_siphonKeyKnown || _siphonKey == ActionKey.Unknown || (!AutoSiphonEnabled && !LateRefreshAssist))
            {
                _openingSiphonPending = false;
                _openingSiphonExpireTick = 0;
                return false;
            }

            if (_pulseActive)
            {
                if (!_siphonPulseOwned)
                    return false;

                _openingSiphonPending = false;
                _openingSiphonExpireTick = 0;
                return true;
            }

            if (_siphonKey != _lanceKey && IsActionPhysicallyDown(_siphonKey))
            {
                _openingSiphonPending = false;
                _openingSiphonExpireTick = 0;
                return true;
            }

            _nextPulseTick = 0;
            _pulseWasBuild = false;
            _pulseBuildTarget = 0;
            int previousSiphonAssistUntilTick = _siphonAssistUntilTick;

            if (!TryPrepareOpeningSiphonAnchor(tick))
                return false;

            if (!PulseSiphon(tick, MsToTicks(OpeningSiphonPulseMs), OpeningSiphonDownTicks, false))
                return false;

            // The one-tick opener stops movement or refreshes Power Shift without extending input ownership.
            _siphonAssistUntilTick = Math.Max(previousSiphonAssistUntilTick, tick + OpeningSiphonDownTicks + 1);
            _lastMovementStopSiphonTick = tick;
            _openingSiphonPending = false;
            _openingSiphonExpireTick = 0;
            return true;
        }

        private bool TryPrepareOpeningSiphonAnchor(int tick)
        {
            if (!IsMouseSiphonAction())
                return true;

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }

            if (!IsCursorOverClickDangerUi() && IsValidSiphonAnchorTarget(selected, true))
            {
                RememberEngagementFocus(selected, tick);
                _lastSiphonTargetAcdId = GetMonsterAcdId(selected);
                return true;
            }

            IMonster anchor = null;
            try { anchor = PickTarget(tick); } catch { }
            if (!IsValidSiphonAnchorTarget(anchor, true))
                anchor = PickSiphonAnchorTarget(GetSafeSiphonPulseTarget());

            if (IsAliveTarget(anchor))
            {
                if (!IsCursorOverClickDangerUi() && SameMonster(selected, anchor))
                {
                    _lastSiphonTargetAcdId = GetMonsterAcdId(anchor);
                    return true;
                }

                if (TryMoveToSafeSiphonPulseTarget(anchor, tick))
                {
                    _lastSiphonTargetAcdId = GetMonsterAcdId(anchor);
                    return true;
                }
            }

            return !IsCursorOverClickDangerUi();
        }

        private bool OpeningSiphonHandoffConfirmed()
        {
            if (_pulseActive)
                return false;

            if (!IsMouseSiphonAction())
                return true;

            try
            {
                IMonster selected = Hud.Game.SelectedMonster2;
                return IsAliveTarget(selected)
                    && _lastSiphonTargetAcdId != 0
                    && GetMonsterAcdId(selected) == _lastSiphonTargetAcdId;
            }
            catch { return false; }
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

            if (!IsAliveTarget(currentTarget))
            {
                if (TryAnchorSiphonWithoutTarget(tick))
                    return;

                StopPulseNow();
                return;
            }

            ClearNoTargetAnchorLimiter(true);

            if (_siphonKey != _lanceKey && !_siphonPulseOwned && IsActionPhysicallyDown(_siphonKey))
            {
                StopPulseNow();
                return;
            }

            bool lotdActive = IsBuffActive(_snoLandOfTheDead);
            bool powerActive = IsBuffActive(PowerPylonBuffSno);
            bool corpsesAvailable = lotdActive || CorpsesAvailable(tick);

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            ResetLateRefreshSinglePulseGate(havePowerShift, stacks, timeLeft);

            uint currentTargetAcdId = GetMonsterAcdId(currentTarget);
            bool targetChanged = currentTargetAcdId != 0 && currentTargetAcdId != _lastSiphonTargetAcdId;
            if (currentTargetAcdId != 0)
                _lastSiphonTargetAcdId = currentTargetAcdId;

            if (fullAuto && ShouldDoProactiveRefresh(tick, currentTargetAcdId, targetChanged, newLanceEngagement, havePowerShift, stacks, timeLeft, lotdActive))
                return;

            if (BossAlive() && !RgAutoSnapSiphonAssist)
            {
                if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                    return;


                if ((timeLeft * 1000f) <= GetActiveLateRefreshWindowMs())
                {
                    if (!EnsureSiphonPulseAnchor(currentTarget, tick))
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


                if ((timeLeft * 1000f) <= GetActiveLateRefreshWindowMs())
                {
                    if (!EnsureSiphonPulseAnchor(currentTarget, tick))
                        return;

                    if (PulseSiphon(tick, MsToTicks(BossLateRefreshPulseMs), BossPulseDownTicks, false))
                        _lateRefreshPulseConsumed = true;
                }
                return;
            }

            int activeTarget = ClampStackTarget(powerActive ? PowerStackTarget : NormalStackTarget);
            bool reachedConfiguredTarget = havePowerShift && stacks >= activeTarget;

            // Fast cadence is only for the configured HUD Menu target.
            // If the user wants 10 stacks, they set 10; do not silently escalate lower targets to 10.
            bool needBuild = fullAuto && (!havePowerShift || stacks < activeTarget);
            bool needRefresh = LateRefreshAssist && havePowerShift && stacks > 0 && timeLeft > 0f && corpsesAvailable && (timeLeft * 1000f) <= GetActiveLateRefreshWindowMs();
            bool needMaintain = fullAuto && havePowerShift && reachedConfiguredTarget && !corpsesAvailable;

            if (needBuild && FastBuildFuseTripped(tick, activeTarget, havePowerShift, stacks))
            {
                needBuild = false;
                needMaintain = havePowerShift && stacks > 0;
            }


            int intervalTicks;
            int downTicks = PulseDownTicks;

            if (needBuild)
            {
                intervalTicks = MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs);
                downTicks = lotdActive ? LotdPulseDownTicks : PulseDownTicks;

                if (havePowerShift && stacks >= activeTarget - 1)
                    downTicks = lotdActive ? Math.Max(1, Math.Min(LotdPulseDownTicks, 3)) : 1;
            }
            else if (needRefresh)
            {
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

            _pulseWasBuild = needBuild;
            _pulseBuildTarget = needBuild ? activeTarget : 0;

            if (!EnsureSiphonPulseAnchor(currentTarget, tick))
                return;

            bool pulsed = PulseSiphon(tick, intervalTicks, downTicks, true);
            if (pulsed && needBuild)
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
                return false;
            }

            if (havePowerShift && stacks >= activeTarget)
                return false;

            return unchecked(tick - _fastBuildStartTick) > MsToTicks(4000) || _fastBuildPulseCount > 45;
        }

        private void ClearFastBuildGuard()
        {
            _fastBuildStartTick = 0;
            _fastBuildPulseCount = 0;
            _fastBuildTarget = 0;
        }

        private bool TryAnchorSiphonWithoutTarget(int tick)
        {
            if (!LateRefreshAssist || !_siphonKeyKnown || _pulseActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            if (havePowerShift && stacks > 0 && timeLeft > 0f && (timeLeft * 1000f) > GetActiveProactiveRefreshWindowMs())
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
            {
                _nextPulseTick = 0;
                _lastPulseWasNoTargetAnchor = false;
            }
        }

        private IMonster GetManualSiphonTargetFallback()
        {
            IMonster focus = GetEngagementFocusTarget(SafeGameTick());
            if (IsAliveTarget(focus))
                return focus;

            try
            {
                if (_lockedTargetAcdId != 0)
                {
                    var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                    if (IsAliveTarget(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                        return locked;
                }
            }
            catch { }

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected))
                {
                    if (IsBossLike(selected))
                        return selected;

                    if (!IsInvulnerable(selected) && !IsIllusionOrClone(selected))
                    {
                        if (IsLeader(selected))
                            return selected;

                        if (!AnyValidLeaderCandidateExists())
                        {
                            if (IsEliteMinionLike(selected))
                                return selected;

                            if (IncludeTrashTargets && SafeDistance(selected) <= PestiPick() + HitboxRangeBonus(selected))
                                return selected;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private bool IsCorpseLanceRicochetRune()
        {
            return _corpseLanceRicochet;
        }

        private int GetActiveLateRefreshWindowMs()
        {
            return IsCorpseLanceRicochetRune() ? RicochetLateRefreshWindowMs : LateRefreshWindowMsDefault;
        }

        private int GetActiveProactiveRefreshWindowMs()
        {
            return IsCorpseLanceRicochetRune() ? RicochetProactiveRefreshWindowMs : ProactiveRefreshWindowMs;
        }

        private int GetFirstEngageSiphonDelayTicks()
        {
            return IsCorpseLanceRicochetRune() ? RicochetFirstEngageSiphonDelayTicks : FirstEngageSiphonDelayTicks;
        }

        private bool ShouldDelayInitialAutosnapForLateRefresh(bool newLanceEngagement, bool manualSiphonDown)
        {
            if (!newLanceEngagement || manualSiphonDown || _engageRefreshConsumed || _pulseActive)
                return false;

            if (!AutoSiphonEnabled || !LateRefreshAssist || !_siphonKeyKnown)
                return false;

            int stacks;
            float timeLeft;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f)
                return false;

            float timeLeftMs = timeLeft * 1000f;
            if (IsCorpseLanceRicochetRune() && timeLeftMs <= GetActiveLateRefreshWindowMs())
                return false;

            return timeLeftMs <= GetActiveProactiveRefreshWindowMs();
        }

        private void QueueLateRefreshIntent(int tick, bool fromEngage)
        {
            if (_lateRefreshIntentActive)
                return;

            int stacks;
            float timeLeft;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > GetActiveProactiveRefreshWindowMs())
                return;

            _lateRefreshIntentActive = true;
            _lateRefreshIntentFromEngage = fromEngage;
            _lateRefreshIntentReadyTick = tick + (fromEngage ? GetFirstEngageSiphonDelayTicks() : 0);
            _lateRefreshIntentExpireTick = tick + LateRefreshIntentExpireTicks;
            _lateRefreshIntentAttempts = 0;
            _lateRefreshIntentNextAttemptTick = _lateRefreshIntentReadyTick;
            _lateRefreshIntentStartTimeLeft = timeLeft;
        }

        private bool TryRunLateRefreshIntent(int tick)
        {
            if (!_lateRefreshIntentActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);

            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > GetActiveProactiveRefreshWindowMs() || timeLeft > _lateRefreshIntentStartTimeLeft + LateRefreshIntentRefreshGainSeconds)
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
            if (AutoSnapEnabled)
                TrySnap(target, tick);

            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            int refreshIntervalTicks = _lateRefreshIntentAttempts <= 0
                ? MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs)
                : MsToTicks(MaintainPulseMs);

            if (!EnsureSiphonPulseAnchor(target, tick))
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

        private IMonster FindLateRefreshIntentTarget(int tick)
        {
            IMonster target = GetManualJuggerLockTarget();
            if (IsAliveTarget(target)) return target;

            if (RgAutoSnapSiphonAssist)
            {
                target = FindRiftGuardianTarget();
                if (IsAliveTarget(target)) return target;
            }

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
                    if (IsAliveTarget(target))
                        return target;
                }
                catch { }
            }

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
        }

        private int GetLateRefreshDownTicks(bool lotdActive)
        {
            if (lotdActive)
                return LotdPulseDownTicks;

            return IsCorpseLanceRicochetRune() ? RicochetLateRefreshDownTicks : LateRefreshDownTicks;
        }

        private bool TryGetPowerShift(out int stacksOut, out float timeLeftOut)
        {
            stacksOut = 0;
            timeLeftOut = 0f;

            try
            {
                var buff = Hud.Game.Me.Powers.GetBuff(_snoSiphonBlood);
                if (buff == null || !buff.Active)
                    return false;

                var counts = buff.IconCounts;
                var times = buff.TimeLeftSeconds;

                if (counts != null && counts.Length > PowerShiftBuffIconIndex && counts[PowerShiftBuffIconIndex] > 0)
                {
                    stacksOut = counts[PowerShiftBuffIconIndex];
                    if (times != null && times.Length > PowerShiftBuffIconIndex && times[PowerShiftBuffIconIndex] > 0d)
                        timeLeftOut = (float)times[PowerShiftBuffIconIndex];

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
                return stacksOut > 0;
            }
            catch { return false; }
        }

        private bool ShouldDoProactiveRefresh(int tick, uint currentTargetAcdId, bool targetChanged, bool newLanceEngagement, bool havePowerShift, int stacks, float timeLeft, bool lotdActive)
        {
            if (!LateRefreshAssist || !havePowerShift || stacks <= 0 || timeLeft <= 0f)
                return false;

            float timeLeftMs = timeLeft * 1000f;
            if (timeLeftMs > GetActiveProactiveRefreshWindowMs())
                return false;

            if (IsCorpseLanceRicochetRune() && timeLeftMs <= GetActiveLateRefreshWindowMs())
                return false;

            if (newLanceEngagement)
            {
                if (_engageRefreshConsumed)
                    return false;

                QueueLateRefreshIntent(tick, true);
                return _lateRefreshIntentActive;
            }

            if (!_lateRefreshIntentActive && (timeLeft * 1000f) <= GetActiveLateRefreshWindowMs())
            {
                QueueLateRefreshIntent(tick, false);
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
            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > GetActiveLateRefreshWindowMs())
                _lateRefreshPulseConsumed = false;
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

            IMonster pendingHover = GetPendingPulseHoverTarget(tick);
            if (IsAliveTarget(pendingHover))
            {
                PrepareImmediateRetarget(pendingHover, tick);
                return pendingHover;
            }

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

            // Resolve leaders and elite-pack members before trash.
            IMonster picked = PickTarget(tick);
            if (IsAliveTarget(picked))
            {
                IMonster selectedBeforePick = null;
                try { selectedBeforePick = Hud.Game.SelectedMonster2; } catch { }
                if (ShouldForceTargetTurnover(selectedBeforePick, picked, tick))
                    PrepareImmediateRetarget(picked, tick);
                LockTarget(picked, tick);
                return picked;
            }

            if (AnyElitePackCandidateExists())
            {
                // Recover to a visible elite-like target instead of falling through to trash.
                IMonster preferred = null;
                try { preferred = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters); } catch { }
                if (IsAliveTarget(preferred) && (IsLeader(preferred) || IsEliteMinionLike(preferred)))
                {
                    IMonster selectedBeforePreferred = null;
                    try { selectedBeforePreferred = Hud.Game.SelectedMonster2; } catch { }
                    if (ShouldForceTargetTurnover(selectedBeforePreferred, preferred, tick))
                        PrepareImmediateRetarget(preferred, tick);
                    LockTarget(preferred, tick);
                    return preferred;
                }
            }

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }

            if (IsBossLike(selected))
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

                // If only Juggernaut candidates remain, Space toggles the manual lock off so the player
                // can ignore the Juggernaut and attack trash/minions manually.
                if (currentIndex >= 0 && AllManualCandidatesAreJuggernauts(candidates) && currentIndex >= candidates.Count - 1)
                {
                    ClearManualJuggerLock();
                    return;
                }

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
            nonJuggerLeaders.AddRange(juggerLeaders);
            return nonJuggerLeaders;
        }

        private bool IsManualEliteCycleCandidate(IMonster monster, int tick)
        {
            if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster))
                return false;

            if (!SafeIsOnScreen(monster))
                return false;

            if (!IsWithinPestilenceTargetRange(monster))
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

        private bool AllManualCandidatesAreJuggernauts(List<IMonster> candidates)
        {
            if (candidates == null || candidates.Count <= 0)
                return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!IsJuggernautPack(candidates[i]))
                    return false;
            }
            return true;
        }

        private void SetManualEliteLock(IMonster monster, int tick)
        {
            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
            {
                ClearManualJuggerLock();
                return;
            }

            _manualJuggerLockAcdId = acd;
            _manualTargetUntilTick = tick + ManualTargetLingerTicks;
            _lockedTargetAcdId = acd;
            _lockedTargetKeepUntilTick = tick + ManualTargetLingerTicks;
            _returnToRareAcdId = 0;
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + ReacquireWindowTicks);
            ClearSoftHoverLocks();
            ClearBadHoverState(acd);
        }

        private void RefreshManualEliteLock(int tick)
        {
            if (_manualJuggerLockAcdId == 0)
                return;

            IMonster monster = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster) || !IsWithinPestilenceTargetRange(monster))
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
            if (_leaderPackCoreRetryAcdId == acd)
            {
                _leaderPackCoreRetryAcdId = 0;
                _leaderPackCoreRetryTick = 0;
            }
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
            if (!IsAliveForScan(monster) || !IsLeader(monster) || IsIllusionOrClone(monster) || IsInvulnerable(monster))
                return null;

            if (!IsWithinPestilenceTargetRange(monster))
                return null;

            if (_manualTargetUntilTick > 0 && tick > _manualTargetUntilTick && !IsJuggernautPack(monster))
            {
                ClearManualJuggerLock();
                return null;
            }

            return monster;
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
                    if (!IsAliveTarget(monster) || !IsBossLike(monster))
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
            _normalScanPauseUntilTick = 0;
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
            ClearPendingPulseHover();
            ClearVerifiedHoverPoint();
        }

        private List<IMonster> CaptureAliveMonsters()
        {
            var result = new List<IMonster>();
            try
            {
                foreach (IMonster monster in Hud.Game.AliveMonsters)
                {
                    if (monster != null)
                        result.Add(monster);
                }
            }
            catch { }

            return result;
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

            RefreshTargetCoordinationState(tick, scanList);

            IMonster forced = GetManualJuggerLockTarget();
            if (IsAliveTarget(forced))
                return forced;

            if (BossAlive())
            {
                IMonster boss = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget(monsters) : null;
                return IsAliveTarget(boss) ? boss : null;
            }

            // A confirmed remote Siphon primary is direct party-target evidence.
            // It outranks normal same-category health/distance/debuff scoring, while
            // retaining all eligibility, range, shielding, illusion, and Juggernaut gates.
            IMonster remotePrimary = GetConfirmedRemoteSiphonPrimaryTarget(tick);
            if (remotePrimary != null)
                return remotePrimary;

            IMonster shieldingRare = GetShieldingRare(monsters);
            bool peelMode = IsAliveTarget(shieldingRare) && shieldingRare.CentralXyDistanceToMe <= PestiPick() && IsShieldingActive(shieldingRare);
            object peelPack = peelMode ? GetPackObject(shieldingRare) : null;
            if (peelMode)
                _returnToRareAcdId = shieldingRare.AcdId;

            // Nearby Juggernaut leaders disable automatic steering while preserving manual Siphon.

            IMonster returnRare = FindAliveMonsterByAcdId(_returnToRareAcdId);
            if (!peelMode && IsValidEligibleLeader(returnRare, tick) && !IsShieldingActive(returnRare))
                return returnRare;

            IMonster bestLeaderClose = null;
            IMonster bestLeaderAny = null;
            IMonster bestDebuffedLeaderClose = null;
            IMonster bestDebuffedLeaderAny = null;
            IMonster bestPeelMinion = null;
            IMonster bestMinionClose = null;
            IMonster bestTrashClose = null;
            IMonster bestNearestAny = null;
            IMonster bestOnScreenAny = null;

            float bestLeaderCloseScore = float.MaxValue;
            float bestLeaderAnyDist = float.MaxValue;
            float bestDebuffedLeaderCloseScore = float.MaxValue;
            float bestDebuffedLeaderAnyDist = float.MaxValue;
            float bestPeelMinionDist = float.MaxValue;
            float bestMinionCloseDist = float.MaxValue;
            float bestTrashCloseScore = float.MaxValue;
            float bestNearestAnyDist = float.MaxValue;
            float bestOnScreenAnyDist = float.MaxValue;
            bool anyLeader = false;
            bool anyElitePackMember = false;

            IMonster selectedLeader = null;
            try { selectedLeader = Hud.Game.SelectedMonster2; } catch { }
            if (IsValidEligibleLeader(selectedLeader, tick) && IsWithinPestilenceTargetRange(selectedLeader))
            {
                float selectedScore = GetElitePriorityScore(selectedLeader, SafeDistance(selectedLeader), SafeIsOnScreen(selectedLeader), scanList);
                bestLeaderClose = selectedLeader;
                bestLeaderCloseScore = selectedScore;
                bestLeaderAny = selectedLeader;
                bestLeaderAnyDist = selectedScore;
                if (PrioritizeDebuffedElites && HasAnySiphonDebuff(selectedLeader))
                {
                    bestDebuffedLeaderClose = selectedLeader;
                    bestDebuffedLeaderCloseScore = selectedScore;
                    bestDebuffedLeaderAny = selectedLeader;
                    bestDebuffedLeaderAnyDist = selectedScore;
                }
                anyLeader = true;
            }

            foreach (var monster in scanList)
            {
                if (!IsAliveForScan(monster))
                    continue;

                if (monster.Rarity == ActorRarity.Boss)
                    continue;

                float distance = SafeDistance(monster);
                bool onScreen = SafeIsOnScreen(monster);
                bool inPestRange = IsWithinPestilenceTargetRange(monster);

                if (!inPestRange && !IsBossLike(monster))
                    continue;

                // A temporarily invalid elite-pack member still blocks trash fallback.
                if (IsElitePackCandidate(monster))
                    anyElitePackMember = true;

                if (IsSkipped(monster, tick))
                {
                    if (IsLeader(monster) && !IsIllusionOrClone(monster))
                        anyLeader = true;
                    continue;
                }

                // Record live leaders before temporary invulnerable, Juggernaut, or shielding filters.
                if (IsLeader(monster))
                {
                    if (IsIllusionOrClone(monster))
                        continue;

                    if (IsJuggernautPack(monster))
                        continue;

                    anyLeader = true;

                    if (!onScreen || IsShieldingActive(monster) || IsInvulnerable(monster))
                        continue;

                    if (distance < bestNearestAnyDist)
                    {
                        bestNearestAnyDist = distance;
                        bestNearestAny = monster;
                    }

                    if (onScreen && distance < bestOnScreenAnyDist)
                    {
                        bestOnScreenAnyDist = distance;
                        bestOnScreenAny = monster;
                    }

                    float closeScore = GetElitePriorityScore(monster, distance, onScreen, scanList);
                    if (IsPestilenceReachOrOnScreen(distance, onScreen))
                    {
                        if (closeScore < bestLeaderCloseScore)
                        {
                            bestLeaderCloseScore = closeScore;
                            bestLeaderClose = monster;
                        }

                        if (PrioritizeDebuffedElites && HasAnySiphonDebuff(monster) && closeScore < bestDebuffedLeaderCloseScore)
                        {
                            bestDebuffedLeaderCloseScore = closeScore;
                            bestDebuffedLeaderClose = monster;
                        }
                    }

                    if (closeScore < bestLeaderAnyDist)
                    {
                        bestLeaderAnyDist = closeScore;
                        bestLeaderAny = monster;
                    }

                    if (PrioritizeDebuffedElites && HasAnySiphonDebuff(monster) && closeScore < bestDebuffedLeaderAnyDist)
                    {
                        bestDebuffedLeaderAnyDist = closeScore;
                        bestDebuffedLeaderAny = monster;
                    }

                    continue;
                }

                if (IsInvulnerable(monster) || IsIllusionOrClone(monster) || IsJuggernautPack(monster))
                    continue;

                if (onScreen && distance < bestOnScreenAnyDist)
                {
                    bestOnScreenAnyDist = distance;
                    bestOnScreenAny = monster;
                }

                if (IsEliteMinionLike(monster) && distance < bestNearestAnyDist)
                {
                    bestNearestAnyDist = distance;
                    bestNearestAny = monster;
                }

                if (IsEliteMinionLike(monster))
                {
                    if (peelMode && SamePack(monster, peelPack) && distance < bestPeelMinionDist)
                    {
                        bestPeelMinionDist = distance;
                        bestPeelMinion = monster;
                    }

                    if (IsPestilenceReachOrOnScreen(distance, onScreen) && distance < bestMinionCloseDist)
                    {
                        bestMinionCloseDist = distance;
                        bestMinionClose = monster;
                    }

                    continue;
                }

                if (IncludeTrashTargets && onScreen)
                {
                    float bonus = HitboxRangeBonus(monster);
                    if (distance <= PestiPick() + bonus)
                    {
                        float score = GetTrashPriorityScore(monster, distance);
                        if (score < bestTrashCloseScore)
                        {
                            bestTrashCloseScore = score;
                            bestTrashClose = monster;
                        }
                    }
                }
            }

            if (peelMode)
            {
                if (bestLeaderClose != null && !SameMonster(bestLeaderClose, shieldingRare))
                    return bestLeaderClose;

                if (bestPeelMinion != null) return bestPeelMinion;
                if (bestMinionClose != null) return bestMinionClose;
                if (bestTrashClose != null) return bestTrashClose;
                return null;
            }

            if (bestLeaderClose != null)
            {
                IMonster preferredClose = ChoosePositionFirstLeader(bestLeaderClose, bestLeaderCloseScore, bestDebuffedLeaderClose, bestDebuffedLeaderCloseScore);
                IMonster locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsValidEligibleLeader(locked, tick) && IsWithinPestilenceTargetRange(locked))
                {
                    float lockedScore = GetElitePriorityScore(locked, SafeDistance(locked), SafeIsOnScreen(locked), scanList);
                    float preferredScore = preferredClose == bestDebuffedLeaderClose ? bestDebuffedLeaderCloseScore : bestLeaderCloseScore;
                    return lockedScore - preferredScore > EliteSwitchForceMargin ? preferredClose : locked;
                }

                return preferredClose;
            }

            if (bestLeaderAny != null && IsWithinPestilenceTargetRange(bestLeaderAny))
            {
                IMonster preferredAny = ChoosePositionFirstLeader(bestLeaderAny, bestLeaderAnyDist, bestDebuffedLeaderAny, bestDebuffedLeaderAnyDist);
                IMonster locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);

                if (IsValidEligibleLeader(locked, tick) && IsWithinPestilenceTargetRange(locked))
                {
                    float lockedScore = GetElitePriorityScore(locked, SafeDistance(locked), SafeIsOnScreen(locked), scanList);
                    float preferredScore = preferredAny == bestDebuffedLeaderAny ? bestDebuffedLeaderAnyDist : bestLeaderAnyDist;
                    return lockedScore - preferredScore > EliteSwitchForceMargin ? preferredAny : locked;
                }

                return IsWithinPestilenceTargetRange(preferredAny) ? preferredAny : null;
            }

            if (anyLeader)
                return null;

            if (bestMinionClose != null) return bestMinionClose;

            // Elite-pack members outrank all trash until the pack is resolved.
            if (anyElitePackMember)
                return bestNearestAny;

            IMonster shockTower = PickShockTower(monsters, tick);
            if (shockTower != null) return shockTower;

            if (IncludeTrashTargets)
            {
                // Progression overrides proximity only when the configured gap is meaningful.
                IMonster priorityTrash = PickProgressionGapTrash(monsters, bestTrashClose, tick);
                if (priorityTrash != null)
                    return priorityTrash;

                if (bestTrashClose != null) return bestTrashClose;
                if (bestOnScreenAny != null) return bestOnScreenAny;
            }

            return bestNearestAny;
        }

        private void RefreshTargetCoordinationState(int tick, List<IMonster> monsters)
        {
            _siphonAffectedTargetCount = 0;
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    IMonster monster = monsters[i];
                    if (IsAliveTarget(monster) && IsWithinPestilenceTargetRange(monster) && GetSiphonDebuffTier(monster) > 0)
                        _siphonAffectedTargetCount++;
                }
            }

            if (!RemoteNecromancerTargetAssist || monsters == null)
            {
                ClearRemoteSiphonTarget();
                return;
            }

            IMonster best = null;
            float bestScore = float.MaxValue;
            try
            {
                foreach (IPlayer player in Hud.Game.Players)
                {
                    if (!IsRemoteSiphonNecromancer(player))
                        continue;

                    uint identity = ReadIdentityAttribute(player, Hud.Sno.Attributes.Last_ACD_Attacked);
                    IMonster candidate = FindMonsterByIdentity(monsters, identity);
                    if (!IsValidEligibleLeader(candidate, tick) || !IsWithinPestilenceTargetRange(candidate) || !HasAnySiphonDebuff(candidate))
                        continue;

                    float score = GetRemoteTargetPreferenceScore(candidate, monsters);
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
            }
            catch { }

            uint bestAcd = GetMonsterAcdId(best);
            if (bestAcd != 0)
            {
                if (_remoteSiphonCandidateAcdId == bestAcd)
                    _remoteSiphonCandidateSamples = Math.Min(99, _remoteSiphonCandidateSamples + 1);
                else
                {
                    _remoteSiphonCandidateAcdId = bestAcd;
                    _remoteSiphonCandidateSamples = 1;
                }

                if (_remoteSiphonCandidateSamples >= RemoteSiphonTargetConfirmSamples)
                {
                    _remoteSiphonTargetAcdId = bestAcd;
                    _remoteSiphonTargetUntilTick = tick + RemoteSiphonTargetLingerTicks;
                }
            }
            else
            {
                _remoteSiphonCandidateAcdId = 0;
                _remoteSiphonCandidateSamples = 0;
            }

            if (_remoteSiphonTargetAcdId != 0)
            {
                IMonster latched = FindAliveMonsterByAcdId(_remoteSiphonTargetAcdId);
                if (tick > _remoteSiphonTargetUntilTick || !IsValidEligibleLeader(latched, tick) || !IsWithinPestilenceTargetRange(latched))
                {
                    _remoteSiphonTargetAcdId = 0;
                    _remoteSiphonTargetUntilTick = 0;
                }
            }
        }

        private bool IsRemoteSiphonNecromancer(IPlayer player)
        {
            try
            {
                if (player == null || player.IsMe || !player.IsInGame || player.HeroClassDefinition == null
                    || player.HeroClassDefinition.HeroClass != HeroClass.Necromancer || player.Powers == null)
                    return false;

                if (player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Necromancer_SiphonBlood) == null)
                    return false;

                string animation = player.Animation.ToString();
                return player.AnimationState == AcdAnimationState.Channeling
                    && !string.IsNullOrEmpty(animation)
                    && animation.IndexOf("bloodsiphon", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private uint ReadIdentityAttribute(IActor actor, IAttribute attribute)
        {
            if (actor == null || attribute == null)
                return 0u;

            for (int i = 0; i < IdentityAttributeModifiers.Length; i++)
            {
                try
                {
                    uint value = actor.GetAttributeValueAsUInt(attribute, IdentityAttributeModifiers[i], 0u);
                    if (value != 0u && value != uint.MaxValue)
                        return value;
                }
                catch { }
            }

            return 0u;
        }

        private IMonster FindMonsterByIdentity(List<IMonster> monsters, uint identity)
        {
            if (identity == 0u || monsters == null)
                return null;

            for (int i = 0; i < monsters.Count; i++)
            {
                IMonster monster = monsters[i];
                if (monster == null)
                    continue;

                try
                {
                    if (monster.AcdId == identity || monster.AnnId == identity)
                        return monster;
                }
                catch { }
            }

            return null;
        }

        private float GetRemoteTargetPreferenceScore(IMonster monster, List<IMonster> monsters)
        {
            float score = (float)Math.Max(0.0d, Math.Min(1.0d, SafeHitPoints(monster))) * 20.0f;
            score -= Math.Min(6.0f, CountNearbyElitePriority(monster, monsters) * 1.25f);
            if (GetSiphonDebuffTier(monster) >= 2 && _siphonAffectedTargetCount >= 2)
                score -= 3.0f;
            return score;
        }

        private IMonster GetConfirmedRemoteSiphonPrimaryTarget(int tick)
        {
            if (!RemoteNecromancerTargetAssist || _remoteSiphonTargetAcdId == 0
                || tick <= 0 || tick > _remoteSiphonTargetUntilTick)
            {
                return null;
            }

            IMonster target = FindAliveMonsterByAcdId(_remoteSiphonTargetAcdId);
            if (!IsValidEligibleLeader(target, tick) || !IsWithinPestilenceTargetRange(target)
                || !HasAnySiphonDebuff(target))
            {
                return null;
            }

            return target;
        }

        private void ClearRemoteSiphonTarget()
        {
            _remoteSiphonCandidateAcdId = 0;
            _remoteSiphonCandidateSamples = 0;
            _remoteSiphonTargetAcdId = 0;
            _remoteSiphonTargetUntilTick = 0;
        }

        private bool IsTargetEligibleForLock(IMonster monster, int tick)
        {
            if (!IsAliveTarget(monster) || IsSkipped(monster, tick))
                return false;

            if (IsBossLike(monster))
                return true;

            if (IsJuggernautPack(monster))
                return SameMonster(monster, GetManualJuggerLockTarget());

            if (IsShieldingActive(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets && SafeDistance(monster) <= PestiPick() + HitboxRangeBonus(monster);
        }

        private void LockTarget(IMonster monster, int tick)
        {
            if (monster == null)
                return;

            uint acd = 0;
            try { acd = monster.AcdId; } catch { }

            // Persist only active-engagement leader hovers or explicit manual Juggernaut locks.
            bool verifiedHover = acd != 0 && IsActuallySelectedTarget(acd);
            bool explicitManualLock = acd != 0 && acd == _manualJuggerLockAcdId;
            bool confirmsRequestedFocus = verifiedHover && _lanceWasDown
                && (_engagementFocusAcdId == 0 || acd == _engagementFocusAcdId || acd == _snapPhaseAcdId || acd == _forcedSnapAcdId);
            if (IsLeader(monster) && (!IsJuggernautPack(monster) || explicitManualLock) && (confirmsRequestedFocus || explicitManualLock))
            {
                _lockedTargetAcdId = acd;
                _lockedTargetKeepUntilTick = Math.Max(_lockedTargetKeepUntilTick, tick + LockedTargetKeepTicks);
                RememberEngagementFocus(monster, tick);
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
                ArmDeadTargetHandoff(tick);
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_manualJuggerLockAcdId != 0 && GetManualJuggerLockTarget() == null)
            {
                ArmDeadTargetHandoff(tick);
                ClearManualJuggerLock();
            }

            if (_engagementFocusAcdId != 0 && FindAliveMonsterByAcdId(_engagementFocusAcdId) == null)
            {
                ArmDeadTargetHandoff(tick);
                ClearEngagementFocus();
            }

            if (_returnToRareAcdId != 0 && FindAliveMonsterByAcdId(_returnToRareAcdId) == null)
                _returnToRareAcdId = 0;

            if (_pendingPulseHoverAcdId != 0 && FindAliveMonsterByAcdId(_pendingPulseHoverAcdId) == null)
                ClearPendingPulseHover();

            if (_verifiedHoverAcdId != 0 && FindAliveMonsterByAcdId(_verifiedHoverAcdId) == null)
                ClearVerifiedHoverPoint();

            if (_snapPhaseAcdId != 0 && FindAliveMonsterByAcdId(_snapPhaseAcdId) == null)
            {
                ArmDeadTargetHandoff(tick);
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
            }

            if (_stableLockAcdId != 0 && FindAliveMonsterByAcdId(_stableLockAcdId) == null)
            {
                _stableLockAcdId = 0;
                _stableLockUntilTick = 0;
            }

            if (_softLockAcdId != 0 && FindAliveMonsterByAcdId(_softLockAcdId) == null)
            {
                _softLockAcdId = 0;
                _softLockUntilTick = 0;
            }

            if (_cachedHoverAcdId != 0 && FindAliveMonsterByAcdId(_cachedHoverAcdId) == null)
            {
                _cachedHoverAcdId = 0;
                _cachedHoverUntilTick = 0;
                _cachedHoverTryUntilTick = 0;
            }

            if (_lastHoverAcdId != 0 && FindAliveMonsterByAcdId(_lastHoverAcdId) == null)
            {
                _lastHoverAcdId = 0;
                _lastHoverTick = 0;
            }

            if (_postTeleportReacquireAcdId != 0 && FindAliveMonsterByAcdId(_postTeleportReacquireAcdId) == null)
                ClearPostTeleportReacquire();

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

        private void CaptureFreshManualEngagementFocus(int tick)
        {
            try
            {
                IMonster selected = Hud.Game.SelectedMonster2;
                if (IsValidEligibleLeader(selected, tick) && IsWithinPestilenceTargetRange(selected))
                    RememberEngagementFocus(selected, tick);
            }
            catch { }
        }

        private void RememberEngagementFocus(IMonster monster, int tick)
        {
            if (!IsValidEligibleLeader(monster, tick) || !IsWithinPestilenceTargetRange(monster))
                return;

            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
                return;

            if (_engagementFocusAcdId != acd)
            {
                _engagementFocusAcdId = acd;
                _engagementFocusStartTick = tick;
            }

            _engagementFocusUntilTick = tick + EngagementFocusLingerTicks;
        }

        private IMonster GetEngagementFocusTarget(int tick)
        {
            if (_engagementFocusAcdId == 0 || tick <= 0 || tick > _engagementFocusUntilTick)
            {
                ClearEngagementFocus();
                return null;
            }

            IMonster target = FindAliveMonsterByAcdId(_engagementFocusAcdId);
            if (!IsValidEligibleLeader(target, tick) || !IsWithinPestilenceTargetRange(target))
            {
                ClearEngagementFocus();
                return null;
            }

            return target;
        }

        private float GetEngagementFocusBonus(IMonster monster, int tick)
        {
            if (_engagementFocusAcdId == 0 || tick <= 0 || tick > _engagementFocusUntilTick)
                return 0f;

            if (GetMonsterAcdId(monster) != _engagementFocusAcdId)
                return 0f;

            int age = Math.Max(0, tick - _engagementFocusStartTick);
            float duration = Math.Min(1f, age / (float)Math.Max(1, EngagementFocusDurationRampTicks));
            return EngagementFocusBaseBonus + duration * EngagementFocusDurationBonusMax;
        }

        private void ClearEngagementFocus()
        {
            _engagementFocusAcdId = 0;
            _engagementFocusStartTick = 0;
            _engagementFocusUntilTick = 0;
        }

        private void ArmDeadTargetHandoff(int tick)
        {
            if (_lanceWasDown || _cursorOwned)
                _deadTargetHandoffUntilTick = Math.Max(_deadTargetHandoffUntilTick, tick + DeadTargetHandoffTicks);
        }

        private int SafeGameTick()
        {
            try { return Hud != null && Hud.Game != null ? Hud.Game.CurrentGameTick : 0; }
            catch { return 0; }
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
                    if (IsWithinPestilenceTargetRange(monster))
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
                    if (IsWithinPestilenceTargetRange(monster))
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

                    if (IsWithinPestilenceTargetRange(monster))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private IMonster FindNearestOnScreenPreferred(IEnumerable<IMonster> monsters)
        {
            if (monsters == null) return null;

            IMonster bestLeader = null;
            float bestLeaderDist = float.MaxValue;
            IMonster bestMinion = null;
            float bestMinionDist = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveForScan(monster) || !SafeIsOnScreen(monster)) continue;
                    if (monster.Rarity == ActorRarity.Boss) continue;
                    if (IsInvulnerable(monster) || IsJuggernautPack(monster) || IsIllusionOrClone(monster)) continue;
                    if (!IsWithinPestilenceTargetRange(monster)) continue;

                    float d = SafeDistance(monster);
                    if (IsLeader(monster))
                    {
                        if (d < bestLeaderDist) { bestLeaderDist = d; bestLeader = monster; }
                    }
                    else if (IsEliteMinionLike(monster))
                    {
                        if (d < bestMinionDist) { bestMinionDist = d; bestMinion = monster; }
                    }
                }
            }
            catch { }

            return bestLeader ?? bestMinion;
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
                    if (d <= PestiPick() && d < bestDist)
                    {
                        bestDist = d;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private double GetMonsterRiftProgression(IMonster monster)
        {
            try { return monster != null && monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0d; }
            catch { return 0d; }
        }

        private bool IsPriorityTrashRangeCandidate(IMonster monster)
        {
            if (!IsAliveTarget(monster) || IsLeader(monster) || IsEliteMinionLike(monster))
                return false;

            if (IsInvulnerable(monster) || IsIllusionOrClone(monster) || IsJuggernautPack(monster) || IsShockTowerThreat(monster))
                return false;

            return SafeDistance(monster) <= Math.Min(PestiPick(), PriorityTrashMaxDistanceYards)
                && GetMonsterRiftProgression(monster) >= PriorityTrashMinRiftProgression;
        }

        private IMonster PickProgressionGapTrash(IEnumerable<IMonster> monsters, IMonster closeFallback, int tick)
        {
            if (monsters == null)
                return null;

            double fallbackProgression = GetMonsterRiftProgression(closeFallback);
            IMonster best = null;
            double bestProgression = double.NegativeInfinity;
            float bestRadius = -1f;
            float bestDistance = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (IsSkipped(monster, tick) || !IsPriorityTrashRangeCandidate(monster))
                        continue;

                    float distance = SafeDistance(monster);
                    double progression = GetMonsterRiftProgression(monster);

                    float radius = 0f;
                    try { radius = monster.RadiusBottom; } catch { }

                    bool better =
                        progression > bestProgression + 0.02d
                        || (Math.Abs(progression - bestProgression) <= 0.02d && radius > bestRadius + 0.20f)
                        || (Math.Abs(progression - bestProgression) <= 0.02d && Math.Abs(radius - bestRadius) <= 0.20f && distance < bestDistance);

                    if (better)
                    {
                        best = monster;
                        bestProgression = progression;
                        bestRadius = radius;
                        bestDistance = distance;
                    }
                }
            }
            catch { }

            if (best == null)
                return null;

            return bestProgression - fallbackProgression >= PriorityTrashMinProgressionGap ? best : null;
        }

        private IMonster PickShockTower(IEnumerable<IMonster> monsters, int tick)
        {
            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (IsSkipped(monster, tick)
                        || !IsAliveTarget(monster)
                        || IsLeader(monster)
                        || IsEliteMinionLike(monster)
                        || IsInvulnerable(monster))
                    {
                        continue;
                    }

                    if (!IsShockTowerThreat(monster))
                        continue;

                    float distance = SafeDistance(monster);
                    if (distance > PestiPick() + HitboxRangeBonus(monster))
                        continue;

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
                        ArmPostTeleportReacquire(tick);
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

        private void ArmPostTeleportReacquire(int tick)
        {
            _teleportDetectedTick = tick;

            uint acd = GetBestPostTeleportReacquireAcd();
            if (acd == 0)
            {
                ClearPostTeleportReacquire();
                return;
            }

            _postTeleportReacquireAcdId = acd;
            _postTeleportReacquireUntilTick = tick + PostTeleportSelectionInvalidateTicks;
            _postTeleportReacquireMoveTick = 0;
            _reacquireAcdId = acd;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + PostTeleportForceSnapTicks);
            _lastMouseMoveTick = 0;
            _normalScanPauseUntilTick = 0;
            _snapPhaseAcdId = acd;
            _snapPhase = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
            _badHoverAcdId = 0;
            _badHoverCount = 0;
            _badHoverLastTick = 0;
            ResetRecentProbePoints();
        }

        private uint GetBestPostTeleportReacquireAcd()
        {
            uint acd = FirstAliveTargetAcd(_manualJuggerLockAcdId);
            if (acd != 0) return acd;
            acd = FirstAliveTargetAcd(_lockedTargetAcdId);
            if (acd != 0) return acd;
            acd = FirstAliveTargetAcd(_reacquireAcdId);
            if (acd != 0) return acd;
            acd = FirstAliveTargetAcd(_verifiedHoverAcdId);
            if (acd != 0) return acd;
            acd = FirstAliveTargetAcd(_stableLockAcdId);
            if (acd != 0) return acd;
            return FirstAliveTargetAcd(_cachedHoverAcdId);
        }

        private uint FirstAliveTargetAcd(uint acd)
        {
            return acd != 0 && FindAliveMonsterByAcdId(acd) != null ? acd : 0;
        }

        private bool IsPostTeleportSelectionStale(uint acd, int tick)
        {
            if (acd == 0 || _postTeleportReacquireAcdId != acd)
                return false;

            if (tick > _postTeleportReacquireUntilTick || FindAliveMonsterByAcdId(acd) == null)
            {
                ClearPostTeleportReacquire();
                return false;
            }

            return _postTeleportReacquireMoveTick <= 0 || tick <= _postTeleportReacquireMoveTick;
        }

        private bool TryMovePostTeleportReacquire(IMonster target, float x, float y, uint acd, int tick)
        {
            if (!IsPostTeleportSelectionStale(acd, tick))
                return false;

            float pointX = x;
            float pointY = y;

            if (_verifiedHoverAcdId == acd && _verifiedHoverHasTargetAnchor)
            {
                pointX = x + (_verifiedHoverScreenX - _verifiedHoverTargetScreenX);
                pointY = y + (_verifiedHoverScreenY - _verifiedHoverTargetScreenY);
            }
            else if (_cachedHoverAcdId == acd)
            {
                pointX = x + _cachedHoverDx;
                pointY = y + _cachedHoverDy;
            }
            else if (_stableLockAcdId == acd)
            {
                pointX = x + _stableLockDx;
                pointY = y + _stableLockDy;
            }
            else
            {
                ProbeGeometry geometry;
                float dx, dy;
                if (TryBuildProbeGeometry(target, x, y, out geometry))
                {
                    GetBodyProbeOffset(geometry, 0.40f, 0f, out dx, out dy);
                    pointX = x + dx;
                    pointY = y + dy;
                }
            }

            if (!IsAutoSnapHoverPoint(pointX, pointY))
                return false;

            if (!IsCursorAtPoint(pointX, pointY, CursorNoOpTolerancePxSq)
                && !SafeMouseMove(pointX, pointY, tick))
                return false;

            _postTeleportReacquireMoveTick = tick;
            _lastMouseMoveTick = tick;
            return true;
        }

        private void CompletePostTeleportReacquire(uint acd)
        {
            if (_postTeleportReacquireAcdId == acd)
                ClearPostTeleportReacquire();
        }

        private void ClearPostTeleportReacquire()
        {
            _postTeleportReacquireAcdId = 0;
            _postTeleportReacquireUntilTick = 0;
            _postTeleportReacquireMoveTick = 0;
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

                int pauseTicks = HasActiveVerifiedLeaderHover(tick)
                    ? LockedHoverManualOverridePauseTicks
                    : ManualCursorOverridePauseTicks;
                _manualCursorOverrideUntilTick = Math.Max(_manualCursorOverrideUntilTick, tick + pauseTicks);
                _movementDisengageUntilTick = Math.Max(_movementDisengageUntilTick, tick + MovementDisengageAfterCursorOverrideTicks);
                SuppressCursorRestoreForEngagement();
            }
            catch { }
        }

        private bool HasActiveVerifiedLeaderHover(int tick)
        {
            uint acd = 0;
            if (_verifiedHoverAcdId != 0 && tick <= _verifiedHoverUntilTick)
                acd = _verifiedHoverAcdId;
            else if (_stableLockAcdId != 0 && tick <= _stableLockUntilTick)
                acd = _stableLockAcdId;

            IMonster target = FindAliveMonsterByAcdId(acd);
            return IsAliveTarget(target) && (IsLeader(target) || IsBossLike(target));
        }

        private bool IsManualCursorOverrideActive(int tick)
        {
            return _manualCursorOverrideUntilTick > 0 && tick <= _manualCursorOverrideUntilTick;
        }


        private bool IsOpportunisticHoverPriorityAllowed(IMonster monster, int tick)
        {
            if (!IsTargetEligibleForLock(monster, tick))
                return false;

            if (IsBossLike(monster) || IsLeader(monster))
            {
                RememberRecentLeaderPriority(monster, tick);
                return true;
            }

            if (!IsEliteMinionLike(monster))
                return false;

            return !AnyVisibleLeaderPriorityCandidateExists(tick)
                && !HasRecentLeaderPriority(tick)
                && !HasActiveLeaderPriorityTarget(tick);
        }

        private bool HasActiveLeaderPriorityTarget(int tick)
        {
            return IsLeaderPriorityAcd(_manualJuggerLockAcdId, tick)
                || IsLeaderPriorityAcd(_lockedTargetAcdId, tick)
                || IsLeaderPriorityAcd(_reacquireAcdId, tick)
                || IsLeaderPriorityAcd(_snapPhaseAcdId, tick)
                || IsLeaderPriorityAcd(_forcedSnapAcdId, tick);
        }

        private bool IsLeaderPriorityAcd(uint acd, int tick)
        {
            if (acd == 0)
                return false;

            return IsLeaderPriorityCandidate(FindAliveMonsterByAcdId(acd), tick);
        }

        private void RememberRecentLeaderPriority(IMonster monster, int tick)
        {
            if (tick <= 0 || !IsLeaderPriorityCandidate(monster, tick))
                return;

            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
                return;

            _recentPriorityLeaderAcdId = acd;
            _recentPriorityLeaderUntilTick = Math.Max(
                _recentPriorityLeaderUntilTick,
                tick + RecentLeaderPriorityLatchTicks);
            ClearPendingMinionPulseHover();
        }

        private void ClearPendingMinionPulseHover()
        {
            if (_pendingPulseHoverAcdId == 0)
                return;

            IMonster pending = FindAliveMonsterByAcdId(_pendingPulseHoverAcdId);
            if (IsEliteMinionLike(pending))
                ClearPendingPulseHover();
        }

        private bool HasRecentLeaderPriority(int tick)
        {
            if (_recentPriorityLeaderUntilTick <= 0 || tick > _recentPriorityLeaderUntilTick)
            {
                _recentPriorityLeaderAcdId = 0;
                _recentPriorityLeaderUntilTick = 0;
                return false;
            }

            // Bridge one-frame collection/render gaps so minions cannot replace an active leader.
            return _recentPriorityLeaderAcdId != 0;
        }

        private bool IsLeaderPriorityCandidate(IMonster monster, int tick)
        {
            if (!IsAliveForScan(monster) || !SafeIsOnScreen(monster)
                || !(IsBossLike(monster) || IsLeader(monster))
                || IsIllusionOrClone(monster))
                return false;

            if (IsJuggernautPack(monster)
                && !SameMonster(monster, GetManualJuggerLockTarget()))
                return false;

            return IsWithinPestilenceTargetRange(monster);
        }

        private bool AnyVisibleLeaderPriorityCandidateExists(int tick)
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsLeaderPriorityCandidate(monster, tick))
                        continue;

                    RememberRecentLeaderPriority(monster, tick);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void UpdatePassiveEliteHoverCache(int tick)
        {
            try
            {
                var hovered = Hud.Game.SelectedMonster2;
                bool eliteLikeHover = IsLeader(hovered) || IsEliteMinionLike(hovered) || IsBossLike(hovered);
                if (!IsAliveTarget(hovered) || !eliteLikeHover)
                    return;

                uint acd = hovered.AcdId;
                if (_lastPassiveHoverCaptureTick == tick && _lastPassiveHoverCaptureAcdId == acd)
                    return;
                if (IsPostTeleportSelectionStale(acd, tick))
                    return;
                if (!IsOpportunisticHoverPriorityAllowed(hovered, tick))
                    return;

                if (_manualJuggerLockAcdId != 0 && acd != _manualJuggerLockAcdId)
                    return;
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

                RememberVerifiedHoverPoint(acd, Hud.Window.CursorX, Hud.Window.CursorY, tick);
                LearnHoverProfile(hovered, dx, dy);
                if (_pulseActive && acd != _lastSiphonTargetAcdId)
                    RememberPendingPulseHover(acd, dx, dy, tick);

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
                ClearBadHoverState(acd);
                _lastPassiveHoverCaptureTick = tick;
                _lastPassiveHoverCaptureAcdId = acd;
                LockTarget(hovered, tick);
            }
            catch { }
        }

        private bool IsPlayerRunning()
        {
            try { return Hud.Game.Me != null && Hud.Game.Me.AnimationState == AcdAnimationState.Running; }
            catch { return false; }
        }

        private bool IsMovementDisengageActive()
        {
            int tick = 0;
            try { tick = Hud.Game.CurrentGameTick; } catch { }
            return tick > 0 && tick <= _movementDisengageUntilTick;
        }

        #region Target Helpers

        private bool IsPestilenceReachOrOnScreen(float distance, bool onScreen)
        {
            return distance <= PestiPick() + PestilenceTargetRangeLeewayYards;
        }

        private bool IsWithinPestilenceTargetRange(IMonster monster)
        {
            if (!IsAliveTarget(monster))
                return false;

            if (IsBossLike(monster))
                return true;

            float distance = SafeDistance(monster);
            float range = PestiPick() + PestilenceTargetRangeLeewayYards;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return distance <= range;

            return IncludeTrashTargets && distance <= range + HitboxRangeBonus(monster);
        }

        private float PestiPick()
        {
            return ClampRange(PestilenceRangeYards);
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
                // Unique is a leader safety net; bosses use the separate RG path.
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
            // Apply Juggernaut exclusion only to leaders; inherited pack affixes can mark minions too.
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
            return GetSiphonDebuffTier(monster) > 0;
        }

        private int GetSiphonDebuffTier(IMonster monster)
        {
            try
            {
                if (monster == null || !monster.IsAlive)
                    return 0;

                if (monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_11_Visual_Effect_D, _snoSiphonBlood, 0) == 1)
                    return 2;

                return monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_9_Visual_Effect_D, _snoSiphonBlood, 0) == 1 ? 1 : 0;
            }
            catch { return 0; }
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
            try { return monster == null ? float.MaxValue : (float)monster.CentralXyDistanceToMe; }
            catch { return float.MaxValue; }
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

        private float HitboxRangeBonus(IMonster monster)
        {
            try
            {
                float radius = monster.RadiusBottom;
                if (radius <= 0f)
                    radius = BaselineRadiusBottom;

                return Math.Min((radius / BaselineRadiusBottom) * BaselineHitboxYards, HitboxBonusCap);
            }
            catch { return BaselineHitboxYards; }
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
                if (IsLastAliveBluePackElite(monster))
                    score -= EliteLastBluePackFinishBonus;
                else if (monster != null && monster.Rarity == ActorRarity.Rare)
                {
                    double hp = SafeHitPoints(monster);
                    if (hp >= 0.0d && hp <= 0.35d)
                        score -= EliteLowHpYellowFinishBonus;
                }
            }
            catch { }

            try
            {
                if (PrioritizeDebuffedElites)
                {
                    int siphonTier = GetSiphonDebuffTier(monster);
                    if (siphonTier > 0)
                    {
                        float siphonBonus = EliteDebuffPriorityBonus;
                        // A 600% visual is normal on a single target. It becomes stronger
                        // coordination evidence only when multiple Siphon targets are active.
                        if (siphonTier >= 2 && _siphonAffectedTargetCount >= 2)
                            siphonBonus = EliteMultiTargetMaxSiphonPriorityBonus;
                        score -= siphonBonus;

                        double hp = SafeHitPoints(monster);
                        if (hp >= 0.0d && hp <= 1.0d)
                            score -= (float)((1.0d - hp) * EliteDebuffedFinishPriorityBonusMax);
                    }
                }
            }
            catch { }

            try
            {
                if (GetMonsterAcdId(monster) == _lockedTargetAcdId)
                    score -= LockedEliteStickinessBonus;
            }
            catch { }

            score -= GetEngagementFocusBonus(monster, SafeGameTick());

            if (!onScreen)
                score += 3.0f;

            return score;
        }

        private float GetEliteDistanceScore(float distance)
        {
            if (distance <= PestilenceEdgePenaltyStart)
                return distance * EliteNearDistanceWeight;

            float denom = Math.Max(0.1f, PestiPick() - PestilenceEdgePenaltyStart);
            float t = (distance - PestilenceEdgePenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            return (PestilenceEdgePenaltyStart * EliteNearDistanceWeight)
                + ((distance - PestilenceEdgePenaltyStart) * EliteFarDistanceWeight)
                + (t * PestilenceEdgePenaltyMax);
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

                    if (!IsWithinPestilenceTargetRange(other))
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

                int aliveChampions = 0;
                foreach (var other in Hud.Game.AliveMonsters)
                {
                    if (other == null || !IsAliveForScan(other))
                        continue;

                    if (other.Rarity != ActorRarity.Champion || IsIllusionOrClone(other) || IsInvulnerable(other))
                        continue;

                    if (!SamePack(other, pack))
                        continue;

                    if (!IsWithinPestilenceTargetRange(other))
                        continue;

                    aliveChampions++;
                    if (aliveChampions > 1)
                        return false;
                }

                return aliveChampions == 1;
            }
            catch { return false; }
        }

        private int GetPestilenceSelectionRank(IMonster monster)
        {
            if (!IsAliveTarget(monster) || !IsWithinPestilenceTargetRange(monster))
                return 99;

            if (IsBossLike(monster))
                return RgAutoSnapSiphonAssist ? 0 : 99;

            if (IsLeader(monster))
                return 1;

            if (IsEliteMinionLike(monster))
                return 2;

            return IncludeTrashTargets ? 3 : 99;
        }

        private bool ShouldForceTargetTurnover(IMonster selected, IMonster replacement, int tick)
        {
            if (!IsAliveTarget(replacement) || !IsWithinPestilenceTargetRange(replacement))
                return false;

            uint replacementAcd = GetMonsterAcdId(replacement);
            if (replacementAcd == 0)
                return false;

            if (!IsAliveTarget(selected))
                return true;

            if (IsSameAcd(selected, replacementAcd))
                return false;

            IMonster remotePrimary = GetConfirmedRemoteSiphonPrimaryTarget(tick);
            if (remotePrimary != null && GetMonsterAcdId(remotePrimary) == replacementAcd)
                return true;

            int selectedRank = GetPestilenceSelectionRank(selected);
            int replacementRank = GetPestilenceSelectionRank(replacement);

            // Correct hierarchy violations without forcing same-category churn.
            if (replacementRank < selectedRank)
                return true;

            if (replacementRank > selectedRank)
                return false;

            return false;
        }

        private void PrepareImmediateRetarget(IMonster target, int tick)
        {
            uint acd = GetMonsterAcdId(target);
            if (acd == 0)
                return;

            _forcedSnapAcdId = acd;
            _forcedSnapUntilTick = tick + ForcedRetargetTicks;
            _lastMouseMoveTick = 0;
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

        private float GetTrashCloseBucketScore(float distance)
        {
            if (distance <= TrashPenaltyStart)
                return distance;

            float denom = Math.Max(0.1f, PestiPick() - TrashPenaltyStart);
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
            // Normal trash fallback should mostly mean "closest to me".
            // High/medium progression trash is handled by PickHighValueTrash().
            float score = GetTrashCloseBucketScore(distance);

            float radius = 0f;
            try { radius = monster.RadiusBottom; } catch { }
            if (radius > BaselineRadiusBottom)
                score -= Math.Min(0.35f, (radius - BaselineRadiusBottom) * 0.12f);

            return score;
        }


        #endregion

        #region Aiming

        private bool TryAcquireHoverForAutoSiphon(int tick, out IMonster target)
        {
            target = null;

            if (!AutoSnapEnabled)
                return false;

            if (_lockedTargetAcdId != 0)
            {
                var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsAliveTarget(locked) && IsWithinPestilenceTargetRange(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                {
                    target = locked;
                    TrySnap(locked, tick);
                    return true;
                }
            }

            if (_reacquireAcdId != 0 && tick < _reacquireUntilTick)
            {
                var reacquire = FindAliveMonsterByAcdId(_reacquireAcdId);
                if (IsAliveTarget(reacquire) && IsWithinPestilenceTargetRange(reacquire) && !IsJuggernautPack(reacquire) && !IsInvulnerable(reacquire) && !IsIllusionOrClone(reacquire))
                {
                    target = reacquire;
                    TrySnap(reacquire, tick);
                    return true;
                }
            }

            var preferred = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters);
            if (IsAliveTarget(preferred))
            {
                target = preferred;
                if (IsLeader(preferred) || IsEliteMinionLike(preferred))
                    TrySnap(preferred, tick);
                return true;
            }

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected) && !IsInvulnerable(selected) && !IsIllusionOrClone(selected))
                {
                    if (IsBossLike(selected) || IsLeader(selected) || (!AnyValidLeaderCandidateExists() && (IsEliteMinionLike(selected) || IncludeTrashTargets)))
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
                if (IsAutoSnapHoverPoint(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    return true;
                }
            }

            if (force && _stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                float px = x + _stableLockDx;
                float py = y + _stableLockDy;
                if (IsAutoSnapHoverPoint(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    return true;
                }
            }

            return false;
        }

        private void ClearTrashAcquireAttempt()
        {
            _trashAcquireAcdId = 0;
            _trashAcquireStartTick = 0;
        }

        private bool ShouldSkipFailedTrashTarget(uint acd, int tick, bool selectedSame)
        {
            if (acd == 0)
                return false;

            if (selectedSame)
            {
                ClearTrashAcquireAttempt();
                return false;
            }

            if (_trashAcquireAcdId != acd)
            {
                _trashAcquireAcdId = acd;
                _trashAcquireStartTick = tick;
                return false;
            }

            if (_trashAcquireStartTick <= 0 || tick - _trashAcquireStartTick < TrashAcquireFailTicks)
                return false;

            _skipAcdId = acd;
            _skipUntilTick = tick + TrashAcquireSkipTicks;
            ClearTrashAcquireAttempt();

            if (_snapPhaseAcdId == acd)
            {
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
            }

            if (_forcedSnapAcdId == acd)
            {
                _forcedSnapAcdId = 0;
                _forcedSnapUntilTick = 0;
            }

            _lastMouseMoveTick = 0;
            return true;
        }

        private void TrySnap(IMonster monster, int tick)
        {
            if (!AutoSnapEnabled || monster == null || !monster.IsAlive)
                return;

            if (!IsWithinPestilenceTargetRange(monster))
                return;

            uint acd = GetMonsterAcdId(monster);
            if (acd == 0)
                return;

            TrackCursorRestoreTargetSegment(monster, tick);

            bool forcedSnap = acd == _forcedSnapAcdId && tick < _forcedSnapUntilTick;

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
                _normalScanPauseUntilTick = 0;
                _leaderPackCoreRetryAcdId = 0;
                _leaderPackCoreRetryTick = 0;
                ResetRecentProbePoints();
            }

            bool reacquireQuick = acd == _reacquireAcdId && tick < _reacquireUntilTick;
            bool alternateScan = acd == _alternateScanAcdId && tick < _alternateScanUntilTick;
            bool leaderLike = IsLeader(monster) || IsBossLike(monster);
            bool eliteLike = leaderLike || IsEliteMinionLike(monster);
            if (leaderLike)
                RememberRecentLeaderPriority(monster, tick);

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }
            bool postTeleportSelectionStale = IsPostTeleportSelectionStale(acd, tick);
            bool selectedTargetSame = !postTeleportSelectionStale && IsSameAcd(selected, acd);
            bool selectedSame = eliteLike && selectedTargetSame;
            bool samePackLeaderOcclusion = IsSamePackLeaderOcclusion(monster, selected);
            EvaluatePreviousProbeOutcome(monster, selected, acd, tick);

            if (eliteLike)
            {
                ClearTrashAcquireAttempt();
            }
            else
            {
                if (AnyEliteOrMinionCandidateExists())
                {
                    ClearTrashAcquireAttempt();
                    return;
                }

                if (ShouldSkipFailedTrashTarget(acd, tick, selectedTargetSame))
                    return;

                if (selectedTargetSame)
                {
                    _snapPhase = 0;
                    return;
                }
            }

            int minTicks;
            if (forcedSnap)
                minTicks = 0;
            else if (selectedSame || _stableLockAcdId == acd || _cachedHoverAcdId == acd)
                minTicks = 1;
            else if (AggressiveScanMode)
                minTicks = 1;
            else if (reacquireQuick || alternateScan)
                minTicks = Math.Max(2, NormalScanProbeIntervalTicks - 1);
            else
                minTicks = Math.Max(MinTicksBetweenMouseMoves, NormalScanProbeIntervalTicks);
            if (!forcedSnap && (_pulseActive || tick < _siphonAssistUntilTick) && !selectedSame && _stableLockAcdId != acd && _cachedHoverAcdId != acd)
                minTicks = Math.Max(1, minTicks + 1);

            if (!forcedSnap && _lastMouseMoveTick > 0 && tick - _lastMouseMoveTick < minTicks)
                return;

            float x, y;
            if (!TryGetMonsterScreen(monster, out x, out y))
                return;

            bool cheapFarTarget = SafeDistance(monster) > PestiPick() + 2f && !SafeIsOnScreen(monster);

            if (postTeleportSelectionStale && TryMovePostTeleportReacquire(monster, x, y, acd, tick))
                return;

            if (!selectedSame && TryAcceptAdjacentEligibleHover(monster, selected, tick))
                return;

            if (selectedSame)
            {
                float cursorX = Hud.Window.CursorX;
                float cursorY = Hud.Window.CursorY;
                float currentDx = cursorX - x;
                float currentDy = cursorY - y;
                if (Math.Abs(currentDx) > 180f || Math.Abs(currentDy) > 180f)
                {
                    currentDx = _lastHoverDx;
                    currentDy = _lastHoverDy;
                }

                // SelectedMonster2 confirms the current cursor point; do not move again this collection.
                RegisterHoverSuccess(acd, tick, currentDx, currentDy, _lastSnapAttemptBin);
                RememberVerifiedHoverPoint(acd, cursorX, cursorY, tick);
                CompletePostTeleportReacquire(acd);
                if (_pendingPulseHoverAcdId == acd)
                    ClearPendingPulseHover();
                _snapPhase = 0;
                return;
            }

            bool badRecentHover = IsBadRecentHover(acd, selected, tick);
            int badHoverInvalidateTicks = leaderLike
                ? LeaderBadHoverInvalidateTicks
                : BadHoverInvalidateTicks;
            if (badRecentHover)
            {
                RegisterBadHover(acd, tick);
                if (_badHoverAcdId == acd && _badHoverCount >= badHoverInvalidateTicks)
                    InvalidateHoverPoint(acd, true);
            }
            else
            {
                ClearBadHoverState(acd);
            }

            if (!eliteLike)
            {
                if (SafeMouseMove(x, y, tick))
                {
                    _lastMouseMoveTick = tick;
                    _snapPhase = 0;
                }
                return;
            }

            bool canTrustLastPoint = !badRecentHover || (_badHoverAcdId == acd && _badHoverCount < badHoverInvalidateTicks);
            bool pulseOrAssist = _pulseActive || tick < _siphonAssistUntilTick;

            if (canTrustLastPoint && TryMoveVerifiedHoverPoint(acd, tick))
                return;

            if (canTrustLastPoint && (pulseOrAssist || reacquireQuick) && TryMoveCachedHover(acd, x, y, tick, true))
                return;

            if (canTrustLastPoint && _stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                if (SafeMouseMove(x + _stableLockDx, y + _stableLockDy, tick))
                {
                    _lastMouseMoveTick = tick;
                }
                return;
            }

            bool softWindow = pulseOrAssist || reacquireQuick;
            if (canTrustLastPoint && _softLockAcdId == acd && tick < _softLockUntilTick && softWindow)
            {
                if (SafeMouseMove(x + _softLockDx, y + _softLockDy, tick))
                {
                    _lastMouseMoveTick = tick;
                }
                return;
            }

            // Reuse fresh hover truth before starting a new scan.
            if (canTrustLastPoint && _cachedHoverAcdId == acd && tick < _cachedHoverTryUntilTick
                && TryMoveCachedHover(acd, x, y, tick, false))
                return;

            // Dense packs can temporarily place a same-pack minion over the leader's selectable
            // lower core. Recheck that high-probability point periodically instead of excluding it
            // for the full normal-mode recent-point window.
            if (!AggressiveScanMode && samePackLeaderOcclusion
                && TryMoveLeaderPackCoreRetry(monster, x, y, acd, tick))
                return;

            if (!AggressiveScanMode && tick < _normalScanPauseUntilTick)
            {
                if (leaderLike)
                {
                    if (_lastSnapAttemptTick > 0 && tick - _lastSnapAttemptTick < LeaderNormalReprobeTicks)
                        return;
                    _normalScanPauseUntilTick = 0;
                }
                else if (TryMoveNormalScanRestPoint(monster, x, y, acd, tick))
                {
                    return;
                }
            }

            BuildAdaptiveProbeOrder(monster, x, y, cheapFarTarget, alternateScan, reacquireQuick, tick);

            int maxProbes = _rankedProbeCount;
            if (maxProbes <= 0)
            {
                // Never park a leader on an unverified native anchor. Wait briefly and rebuild geometry.
                if (leaderLike)
                {
                    if (!AggressiveScanMode)
                        _normalScanPauseUntilTick = Math.Max(_normalScanPauseUntilTick, tick + LeaderNormalReprobeTicks);
                    return;
                }

                if (SafeMouseMove(x, y, tick))
                {
                    _lastMouseMoveTick = tick;
                }
                return;
            }

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
            }

            int probesThisTick = maxProbes;
            int start = Math.Max(0, Math.Min(_snapPhase, maxProbes - 1));
            for (int i = 0; i < probesThisTick; i++)
            {
                int idx = (start + i) % maxProbes;
                float dx = _rankedProbeDx[idx];
                float dy = _rankedProbeDy[idx];
                float px = x + dx;
                float py = y + dy;

                if (!IsAutoSnapHoverPoint(px, py))
                    continue;

                if (WasRecentProbePoint(px, py, tick))
                    continue;

                int repeatAge = _lastSnapAttemptTick > 0 ? tick - _lastSnapAttemptTick : int.MaxValue;
                if (_lastSnapAttemptAcd == acd && repeatAge >= 1 && repeatAge <= RecentProbePointAvoidTicks
                    && IsCursorAtPoint(px, py, CursorNoOpTolerancePxSq))
                    continue;

                int bin = _rankedProbeBin[idx];
                int zone = _rankedProbeZone[idx];
                _lastHoverDx = dx;
                _lastHoverDy = dy;
                _lastSnapAttemptAcd = acd;
                _lastSnapAttemptBin = bin;
                _lastSnapAttemptZone = zone;
                _lastSnapAttemptTick = tick;
                RecordProbeZoneAttempt(acd, zone, tick);
                RecordRecentProbePoint(px, py, tick);

                if (SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    _snapPhase = idx + 1;
                    if (_snapPhase >= maxProbes)
                    {
                        _snapPhase = 0;
                        if (!AggressiveScanMode && !alternateScan && !reacquireQuick)
                            _normalScanPauseUntilTick = tick + NormalScanCyclePauseTicks;
                    }
                    return;
                }
            }

            if (AggressiveScanMode || reacquireQuick || forcedSnap)
            {
                _alternateScanAcdId = acd;
                _alternateScanUntilTick = tick + AlternateScanWindowTicks;
            }
            else
            {
                _alternateScanAcdId = 0;
                _alternateScanUntilTick = 0;
                _normalScanPauseUntilTick = tick + NormalScanCyclePauseTicks;
            }
            _snapPhase = 0;

            // A probe miss changes scan order; it does not invalidate the target.
            if (_lockedTargetAcdId == acd && !IsActuallySelectedTarget(acd) && _manualJuggerLockAcdId != acd)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }
        }

        private bool TryMoveNormalScanRestPoint(IMonster monster, float x, float y, uint acd, int tick)
        {
            ProbeGeometry geometry;
            if (!TryBuildProbeGeometry(monster, x, y, out geometry))
                return false;

            float dx, dy;
            GetBodyProbeOffset(geometry, 0.40f, 0f, out dx, out dy);
            float px = x + dx;
            float py = y + dy;
            if (!IsAutoSnapHoverPoint(px, py))
                return false;

            if (IsCursorAtPoint(px, py, CursorNoOpTolerancePxSq))
                return true;

            _lastHoverDx = dx;
            _lastHoverDy = dy;
            _lastSnapAttemptAcd = acd;
            _lastSnapAttemptBin = 1;
            _lastSnapAttemptZone = GetProbeZone(monster, dx, dy);
            _lastSnapAttemptTick = tick;

            if (!SafeMouseMove(px, py, tick))
                return false;

            _lastMouseMoveTick = tick;
            return true;
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

        private bool IsSamePackLeaderOcclusion(IMonster leader, IMonster hovered)
        {
            if (!IsAliveTarget(leader) || !IsLeader(leader)
                || !IsAliveTarget(hovered) || !IsEliteMinionLike(hovered))
                return false;

            object pack = GetPackObject(leader);
            return pack != null && SamePack(hovered, pack);
        }

        private bool TryMoveLeaderPackCoreRetry(IMonster leader, float x, float y, uint acd, int tick)
        {
            if (acd == 0 || !IsLeader(leader))
                return false;

            if (_leaderPackCoreRetryAcdId != acd)
            {
                _leaderPackCoreRetryAcdId = acd;
                _leaderPackCoreRetryTick = 0;
            }

            if (_leaderPackCoreRetryTick > 0
                && tick - _leaderPackCoreRetryTick < LeaderPackCoreRetryTicks)
                return false;

            ProbeGeometry geometry;
            if (!TryBuildProbeGeometry(leader, x, y, out geometry))
                return false;

            float dx, dy;
            GetBodyProbeOffset(geometry, 0.40f, 0f, out dx, out dy);
            float px = x + dx;
            float py = y + dy;
            if (!IsAutoSnapHoverPoint(px, py))
                return false;

            _leaderPackCoreRetryTick = tick;
            _lastHoverDx = dx;
            _lastHoverDy = dy;
            _lastSnapAttemptAcd = acd;
            _lastSnapAttemptBin = BodyProbeBin(0.40f, false);
            _lastSnapAttemptZone = GetProbeZone(leader, dx, dy);
            _lastSnapAttemptTick = tick;
            RecordProbeZoneAttempt(acd, _lastSnapAttemptZone, tick);
            RecordRecentProbePoint(px, py, tick);

            if (!SafeMouseMove(px, py, tick))
                return false;

            _lastMouseMoveTick = tick;
            return true;
        }

        private bool IsBadRecentHover(uint acd, IMonster selected, int tick)
        {
            if (acd == 0)
                return false;

            if (IsSameAcd(selected, acd))
                return false;

            // A leader lock treats trash, minion, or empty hover as a failed probe immediately.
            if (IsWrongLeaderHover(acd, selected))
                return true;

            if (_lastHoverAcdId != acd || tick - _lastHoverTick > HoverTruthRecentTicks)
                return false;

            if (!IsAliveTarget(selected))
                return true;

            // Different hover truth advances the probe sequence for the active leader.
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
            IMonster target = FindAliveMonsterByAcdId(acd);
            int window = IsLeader(target) || IsBossLike(target)
                ? LeaderReacquireWindowTicks
                : ReacquireWindowTicks;
            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + window);
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
            if (_verifiedHoverAcdId == acd)
                ClearVerifiedHoverPoint();
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
            LearnHoverProfile(FindAliveMonsterByAcdId(acd), dx, dy);
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
            if (_leaderPackCoreRetryAcdId == acd)
            {
                _leaderPackCoreRetryAcdId = 0;
                _leaderPackCoreRetryTick = 0;
            }
        }

        private void BuildAdaptiveProbeOrder(IMonster monster, float fx, float fy, bool cheapFarTarget, bool alternateScan, bool reacquireQuick, int tick)
        {
            _rankedProbeCount = 0;

            ProbeGeometry geometry;
            if (!TryBuildProbeGeometry(monster, fx, fy, out geometry))
            {
                AddRankedProbeCandidate(0f, 0f, 0, monster, fx, fy, cheapFarTarget);
                return;
            }

            EnsureAdaptiveModelState(monster);
            uint acd = GetMonsterAcdId(monster);
            bool leaderRecovery = (IsLeader(monster) || IsBossLike(monster))
                && (reacquireQuick || alternateScan
                    || (_lastHoverAcdId == acd && tick - _lastHoverTick <= HoverTruthRecentTicks));

            float learnedDx, learnedDy;
            if (TryGetLearnedHoverOffset(monster, geometry, out learnedDx, out learnedDy))
            {
                AddRankedProbeCandidate(learnedDx, learnedDy, 7, monster, fx, fy, cheapFarTarget);
                if (AggressiveScanMode || leaderRecovery)
                {
                    float microX = Math.Max(4f, geometry.RadiusX * 0.14f);
                    float microY = Math.Max(4f, geometry.RadiusY * 0.20f);
                    AddRankedProbeCandidate(learnedDx - microX, learnedDy, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(learnedDx + microX, learnedDy, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(learnedDx, learnedDy - microY, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(learnedDx, learnedDy + microY, 7, monster, fx, fy, cheapFarTarget);

                    if (leaderRecovery)
                    {
                        AddRankedProbeCandidate(learnedDx - microX, learnedDy - microY, 7, monster, fx, fy, cheapFarTarget);
                        AddRankedProbeCandidate(learnedDx + microX, learnedDy - microY, 7, monster, fx, fy, cheapFarTarget);
                        AddRankedProbeCandidate(learnedDx - microX, learnedDy + microY, 7, monster, fx, fy, cheapFarTarget);
                        AddRankedProbeCandidate(learnedDx + microX, learnedDy + microY, 7, monster, fx, fy, cheapFarTarget);
                    }
                }
            }

            if (_cachedHoverAcdId == acd && _cachedHoverUntilTick > 0)
            {
                AddRankedProbeCandidate(_cachedHoverDx, _cachedHoverDy, 7, monster, fx, fy, cheapFarTarget);
                if (leaderRecovery)
                {
                    float microX = Math.Max(4f, geometry.RadiusX * 0.14f);
                    float microY = Math.Max(4f, geometry.RadiusY * 0.20f);
                    AddRankedProbeCandidate(_cachedHoverDx - microX, _cachedHoverDy, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(_cachedHoverDx + microX, _cachedHoverDy, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(_cachedHoverDx, _cachedHoverDy - microY, 7, monster, fx, fy, cheapFarTarget);
                    AddRankedProbeCandidate(_cachedHoverDx, _cachedHoverDy + microY, 7, monster, fx, fy, cheapFarTarget);
                }
            }

            BuildSolverBlockerProfile(monster, fx, fy);
            float left = _solverOccLeft + _solverBigLeft * 1.05f;
            float right = _solverOccRight + _solverBigRight * 1.05f;
            float cleanSide = left <= right ? -1f : 1f;

            AddBarcodeProbeSweep(geometry, cleanSide, monster, fx, fy, cheapFarTarget);

            if (AggressiveScanMode || alternateScan)
            {
                AddBodyProbeCandidate(geometry, 1.05f, 0f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, -1.10f, 0f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, 0.55f, cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, 0.10f, cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, -0.45f, cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, 0.55f, -cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, 0.10f, -cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
                AddBodyProbeCandidate(geometry, -0.45f, -cleanSide * 1.15f, 6, monster, fx, fy, cheapFarTarget);
            }

            if (AggressiveScanMode || leaderRecovery)
                RankAdaptiveProbeCandidates(acd, tick);
        }

        private void AddBarcodeProbeSweep(ProbeGeometry geometry, float cleanSide, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            float[] levels = { 0.88f, 0.64f, 0.40f, 0.16f, -0.08f, -0.32f, -0.56f, -0.80f };

            // Center column moves smoothly from the projected feet toward the upper model.
            for (int i = 0; i < levels.Length; i++)
                AddBodyProbeCandidate(geometry, levels[i], 0f, BodyProbeBin(levels[i], false), monster, fx, fy, cheapFarTarget);

            // Return down the cleaner side, then rise on the opposite side.
            for (int i = levels.Length - 1; i >= 0; i--)
                AddBodyProbeCandidate(geometry, levels[i], cleanSide * 0.58f, BodyProbeBin(levels[i], true), monster, fx, fy, cheapFarTarget);

            for (int i = 0; i < levels.Length; i++)
                AddBodyProbeCandidate(geometry, levels[i], -cleanSide * 0.58f, BodyProbeBin(levels[i], true), monster, fx, fy, cheapFarTarget);
        }

        private int BodyProbeBin(float bodyRatio, bool side)
        {
            if (side)
                return bodyRatio >= 0f ? 4 : 5;
            if (bodyRatio >= 0.55f) return 0;
            if (bodyRatio >= 0.05f) return 1;
            if (bodyRatio >= -0.45f) return 2;
            return 3;
        }

        private void AddBodyProbeCandidate(ProbeGeometry geometry, float bodyRatio, float sideRatio, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            float dx, dy;
            GetBodyProbeOffset(geometry, bodyRatio, sideRatio, out dx, out dy);
            AddRankedProbeCandidate(dx, dy, bin, monster, fx, fy, cheapFarTarget);
        }

        private void GetBodyProbeOffset(ProbeGeometry geometry, float bodyRatio, float sideRatio, out float dx, out float dy)
        {
            dx = geometry.BodyDx * bodyRatio + geometry.RadiusX * sideRatio;
            dy = geometry.BodyDy * bodyRatio;
        }

        private void AddRankedProbeCandidate(float dx, float dy, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            if (_rankedProbeCount >= AdaptiveCandidateCapacity)
                return;

            if (!IsAutoSnapHoverPoint(fx + dx, fy + dy))
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
            _rankedProbeScore[_rankedProbeCount] = 0f;
            _rankedProbeCount++;
        }


        private void RankAdaptiveProbeCandidates(uint acd, int tick)
        {
            for (int i = 0; i < _rankedProbeCount; i++)
            {
                int bin = _rankedProbeBin[i];
                int zone = _rankedProbeZone[i];
                float score = (_rankedProbeCount - i) * 0.012f;
                score += GetProbeZoneCoverageBonus(acd, zone, tick) * 1.45f;
                score += _adaptiveBinGood[bin] * 0.16f;
                score -= _adaptiveBinBad[bin] * 0.24f;

                if (_lastSnapAttemptAcd == acd && zone == _lastSnapAttemptZone)
                {
                    int age = _lastSnapAttemptTick > 0 ? tick - _lastSnapAttemptTick : int.MaxValue;
                    if (age <= 2) score -= 3.0f;
                    else if (age <= 6) score -= 1.35f;
                }

                _rankedProbeScore[i] = score;
            }

            for (int i = 1; i < _rankedProbeCount; i++)
            {
                int j = i;
                while (j > 0 && _rankedProbeScore[j] > _rankedProbeScore[j - 1])
                {
                    SwapRankedProbeCandidates(j, j - 1);
                    j--;
                }
            }
        }

        private void SwapRankedProbeCandidates(int a, int b)
        {
            float f = _rankedProbeDx[a]; _rankedProbeDx[a] = _rankedProbeDx[b]; _rankedProbeDx[b] = f;
            f = _rankedProbeDy[a]; _rankedProbeDy[a] = _rankedProbeDy[b]; _rankedProbeDy[b] = f;
            f = _rankedProbeScore[a]; _rankedProbeScore[a] = _rankedProbeScore[b]; _rankedProbeScore[b] = f;

            int n = _rankedProbeBin[a]; _rankedProbeBin[a] = _rankedProbeBin[b]; _rankedProbeBin[b] = n;
            n = _rankedProbeZone[a]; _rankedProbeZone[a] = _rankedProbeZone[b]; _rankedProbeZone[b] = n;
        }

        private void EvaluatePreviousProbeOutcome(IMonster requested, IMonster selected, uint acd, int tick)
        {
            if (_lastSnapAttemptTick <= 0 || tick <= _lastSnapAttemptTick
                || _lastEvaluatedProbeTick == _lastSnapAttemptTick)
                return;

            _lastEvaluatedProbeTick = _lastSnapAttemptTick;
            if (_lastSnapAttemptAcd != acd || IsSameAcd(selected, acd))
                return;

            if (IsAdjacentEligibleHover(requested, selected, tick))
                return;

            RecordAdaptiveAttempt(_lastSnapAttemptBin);
        }

        private bool TryAcceptAdjacentEligibleHover(IMonster requested, IMonster hovered, int tick)
        {
            if (!IsAdjacentEligibleHover(requested, hovered, tick))
                return false;

            uint hoveredAcd = GetMonsterAcdId(hovered);
            UpdatePassiveEliteHoverCache(tick);
            return hoveredAcd != 0
                && _lastPassiveHoverCaptureTick == tick
                && _lastPassiveHoverCaptureAcdId == hoveredAcd;
        }

        private bool IsAdjacentEligibleHover(IMonster requested, IMonster hovered, int tick)
        {
            if (requested == null || hovered == null || SameMonster(requested, hovered))
                return false;

            if (!(IsLeader(hovered) || IsEliteMinionLike(hovered) || IsBossLike(hovered)))
                return false;

            if (!IsWithinPestilenceTargetRange(hovered))
                return false;

            if (!IsOpportunisticHoverPriorityAllowed(hovered, tick))
                return false;

            uint hoveredAcd = GetMonsterAcdId(hovered);
            if (hoveredAcd == 0)
                return false;

            if (_manualJuggerLockAcdId != 0 && hoveredAcd != _manualJuggerLockAcdId)
                return false;

            float requestedX, requestedY, hoveredX, hoveredY;
            if (!TryGetMonsterScreen(requested, out requestedX, out requestedY)
                || !TryGetMonsterScreen(hovered, out hoveredX, out hoveredY))
                return false;

            float screenDx = requestedX - hoveredX;
            float screenDy = requestedY - hoveredY;
            if (screenDx * screenDx + screenDy * screenDy > AdjacentEliteHoverMaxScreenPxSq)
                return false;

            return SafeWorldDistance(requested, hovered) <= AdjacentEliteHoverMaxWorldYards;
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
                var sc = monster.ScreenCoordinate;
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
                var sc = monster.FloorCoordinate.ToScreenCoordinate();
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

        private void LearnHoverProfile(IMonster monster, float dx, float dy)
        {
            if (monster == null || Math.Abs(dx) > 260f || Math.Abs(dy) > 260f)
                return;

            uint sno = GetMonsterSno(monster);
            if (sno == 0u)
                return;

            float x, y;
            if (!TryGetMonsterScreen(monster, out x, out y))
                return;

            ProbeGeometry geometry;
            if (!TryBuildProbeGeometry(monster, x, y, out geometry))
                return;

            float bodyRatio, sideRatio;
            if (!TryMeasureBodyProbe(geometry, dx, dy, out bodyRatio, out sideRatio))
                return;

            // Ignore distant overlap samples that do not describe a repeatable body point.
            if (bodyRatio < -1.55f || bodyRatio > 1.65f || Math.Abs(sideRatio) > 2.10f)
                return;

            LearnedHoverProfile profile;
            if (!_learnedHoverProfiles.TryGetValue(sno, out profile))
                profile = new LearnedHoverProfile();

            if (profile.Samples <= 0)
            {
                profile.BodyRatio = bodyRatio;
                profile.SideRatio = sideRatio;
            }
            else
            {
                float bodyDelta = ClampFloat(bodyRatio - profile.BodyRatio, -0.45f, 0.45f);
                float sideDelta = ClampFloat(sideRatio - profile.SideRatio, -0.45f, 0.45f);
                float weight = profile.Samples < 4 ? 0.32f : 0.18f;
                profile.BodyRatio += bodyDelta * weight;
                profile.SideRatio += sideDelta * weight;
            }

            profile.Samples = Math.Min(1000, profile.Samples + 1);
            _learnedHoverProfiles[sno] = profile;

            if (_learnedHoverProfiles.Count > LearnedHoverProfileCapacity)
            {
                uint remove = 0u;
                int fewest = int.MaxValue;
                foreach (var pair in _learnedHoverProfiles)
                {
                    if (pair.Key == sno) continue;
                    if (pair.Value.Samples < fewest)
                    {
                        fewest = pair.Value.Samples;
                        remove = pair.Key;
                    }
                }
                if (remove != 0u)
                    _learnedHoverProfiles.Remove(remove);
            }
        }

        private bool TryGetLearnedHoverOffset(IMonster monster, ProbeGeometry geometry, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;

            uint sno = GetMonsterSno(monster);
            LearnedHoverProfile profile;
            if (sno == 0u || !_learnedHoverProfiles.TryGetValue(sno, out profile) || profile.Samples <= 0)
                return false;

            GetBodyProbeOffset(geometry, profile.BodyRatio, profile.SideRatio, out dx, out dy);
            return Math.Abs(dx) <= 260f && Math.Abs(dy) <= 260f;
        }

        private uint GetMonsterSno(IMonster monster)
        {
            try { return monster != null && monster.SnoMonster != null ? monster.SnoMonster.Sno : 0u; }
            catch { return 0u; }
        }

        private void EnsureAdaptiveModelState(IMonster monster)
        {
            uint sno = GetMonsterSno(monster);
            if (_adaptiveModelSno == sno)
                return;

            _adaptiveModelSno = sno;
            for (int i = 0; i < AdaptiveBinCount; i++)
            {
                _adaptiveBinGood[i] = 0f;
                _adaptiveBinBad[i] = 0f;
            }
        }

        private bool TryBuildProbeGeometry(IMonster monster, float nativeX, float nativeY, out ProbeGeometry geometry)
        {
            geometry = new ProbeGeometry();

            float floorX = nativeX;
            float floorY = nativeY;
            bool haveFloor = false;
            try
            {
                IScreenCoordinate floor = monster != null && monster.FloorCoordinate != null
                    ? monster.FloorCoordinate.ToScreenCoordinate()
                    : null;
                if (floor != null)
                {
                    floorX = floor.X;
                    floorY = floor.Y;
                    haveFloor = true;
                }
            }
            catch { }

            float radiusX = 0f;
            float radiusY = 0f;
            if (haveFloor)
                ProjectGroundRadius(monster, floorX, floorY, out radiusX, out radiusY);

            float bodyDx = floorX - nativeX;
            float bodyDy = floorY - nativeY;
            float bodyLength = (float)Math.Sqrt(bodyDx * bodyDx + bodyDy * bodyDy);
            if (bodyLength < 14f)
            {
                bodyDx = 0f;
                bodyDy = Math.Max(28f, radiusY * 2.4f);
            }

            geometry.BodyDx = bodyDx;
            geometry.BodyDy = bodyDy;
            geometry.RadiusX = Math.Max(8f, radiusX);
            geometry.RadiusY = Math.Max(6f, radiusY);
            return true;
        }

        private void ProjectGroundRadius(IMonster monster, float floorX, float floorY, out float radiusX, out float radiusY)
        {
            radiusX = 0f;
            radiusY = 0f;

            try
            {
                if (monster == null || monster.FloorCoordinate == null)
                    return;

                float radius = monster.RadiusBottom;
                if (radius <= 0f)
                    radius = BaselineRadiusBottom;

                IWorldCoordinate[] points =
                {
                    monster.FloorCoordinate.Offset(radius, 0f, 0f),
                    monster.FloorCoordinate.Offset(-radius, 0f, 0f),
                    monster.FloorCoordinate.Offset(0f, radius, 0f),
                    monster.FloorCoordinate.Offset(0f, -radius, 0f)
                };

                for (int i = 0; i < points.Length; i++)
                {
                    IScreenCoordinate screen = points[i] != null ? points[i].ToScreenCoordinate() : null;
                    if (screen == null)
                        continue;

                    radiusX = Math.Max(radiusX, Math.Abs(screen.X - floorX));
                    radiusY = Math.Max(radiusY, Math.Abs(screen.Y - floorY));
                }
            }
            catch { }

            if (radiusX <= 0f)
                radiusX = Math.Max(8f, GetSafeRadiusBottom(monster) * 8f);
            if (radiusY <= 0f)
                radiusY = Math.Max(6f, GetSafeRadiusBottom(monster) * 4f);
        }

        private float GetSafeRadiusBottom(IMonster monster)
        {
            try { return monster != null && monster.RadiusBottom > 0f ? monster.RadiusBottom : BaselineRadiusBottom; }
            catch { return BaselineRadiusBottom; }
        }

        private bool TryMeasureBodyProbe(ProbeGeometry geometry, float dx, float dy, out float bodyRatio, out float sideRatio)
        {
            bodyRatio = 0f;
            sideRatio = 0f;

            float lengthSq = geometry.BodyDx * geometry.BodyDx + geometry.BodyDy * geometry.BodyDy;
            if (lengthSq < 16f)
                return false;

            bodyRatio = (dx * geometry.BodyDx + dy * geometry.BodyDy) / lengthSq;
            float bodyX = geometry.BodyDx * bodyRatio;
            sideRatio = (dx - bodyX) / Math.Max(8f, geometry.RadiusX);
            return true;
        }

        private void RememberPendingPulseHover(uint acd, float dx, float dy, int tick)
        {
            if (acd == 0)
                return;

            _pendingPulseHoverAcdId = acd;
            _pendingPulseHoverDx = dx;
            _pendingPulseHoverDy = dy;
            _pendingPulseHoverUntilTick = tick + PendingPulseHoverTicks;
        }

        private IMonster GetPendingPulseHoverTarget(int tick)
        {
            if (_pulseActive || _pendingPulseHoverAcdId == 0)
                return null;

            if (tick > _pendingPulseHoverUntilTick)
            {
                ClearPendingPulseHover();
                return null;
            }

            IMonster target = FindAliveMonsterByAcdId(_pendingPulseHoverAcdId);
            if (!IsAliveTarget(target) || !IsWithinPestilenceTargetRange(target)
                || !IsOpportunisticHoverPriorityAllowed(target, tick))
            {
                ClearPendingPulseHover();
                return null;
            }

            _cachedHoverAcdId = _pendingPulseHoverAcdId;
            _cachedHoverDx = _pendingPulseHoverDx;
            _cachedHoverDy = _pendingPulseHoverDy;
            _cachedHoverTryUntilTick = Math.Max(_cachedHoverTryUntilTick, tick + 8);
            return target;
        }

        private void ClearPendingPulseHover()
        {
            _pendingPulseHoverAcdId = 0;
            _pendingPulseHoverDx = 0f;
            _pendingPulseHoverDy = 0f;
            _pendingPulseHoverUntilTick = 0;
        }

        private void RememberVerifiedHoverPoint(uint acd, float screenX, float screenY, int tick)
        {
            if (acd == 0 || !IsAutoSnapHoverPoint(screenX, screenY))
                return;

            _verifiedHoverAcdId = acd;
            _verifiedHoverScreenX = screenX;
            _verifiedHoverScreenY = screenY;
            _verifiedHoverHasTargetAnchor = false;
            _verifiedHoverTargetScreenX = 0f;
            _verifiedHoverTargetScreenY = 0f;

            IMonster target = FindAliveMonsterByAcdId(acd);
            float targetX, targetY;
            if (IsAliveTarget(target) && TryGetMonsterScreen(target, out targetX, out targetY))
            {
                _verifiedHoverTargetScreenX = targetX;
                _verifiedHoverTargetScreenY = targetY;
                _verifiedHoverHasTargetAnchor = true;
            }

            _verifiedHoverUntilTick = tick + VerifiedHoverHoldTicks;
            _verifiedHoverLastTryTick = 0;
            _verifiedHoverRetryCount = 0;
        }

        private bool TryMoveVerifiedHoverPoint(uint acd, int tick)
        {
            if (acd == 0 || _verifiedHoverAcdId != acd)
                return false;

            if (tick > _verifiedHoverUntilTick || _verifiedHoverRetryCount >= VerifiedHoverMaxRetries)
            {
                ClearVerifiedHoverPoint();
                return false;
            }

            IMonster target = FindAliveMonsterByAcdId(acd);
            if (!IsAliveTarget(target) || !IsWithinPestilenceTargetRange(target))
            {
                ClearVerifiedHoverPoint();
                return false;
            }

            if (_verifiedHoverLastTryTick > 0
                && tick - _verifiedHoverLastTryTick < VerifiedHoverRetryIntervalTicks)
                return true;

            float pointX = _verifiedHoverScreenX;
            float pointY = _verifiedHoverScreenY;
            float targetX, targetY;
            if (_verifiedHoverHasTargetAnchor && TryGetMonsterScreen(target, out targetX, out targetY))
            {
                pointX = targetX + (_verifiedHoverScreenX - _verifiedHoverTargetScreenX);
                pointY = targetY + (_verifiedHoverScreenY - _verifiedHoverTargetScreenY);
            }

            if (!IsAutoSnapHoverPoint(pointX, pointY))
            {
                ClearVerifiedHoverPoint();
                return false;
            }

            _verifiedHoverLastTryTick = tick;
            _verifiedHoverRetryCount++;

            if (IsCursorAtPoint(pointX, pointY, CursorNoOpTolerancePxSq))
                return true;

            if (!SafeMouseMove(pointX, pointY, tick))
                return false;

            _lastMouseMoveTick = tick;
            return true;
        }

        private void ClearVerifiedHoverPoint()
        {
            _verifiedHoverAcdId = 0;
            _verifiedHoverScreenX = 0f;
            _verifiedHoverScreenY = 0f;
            _verifiedHoverTargetScreenX = 0f;
            _verifiedHoverTargetScreenY = 0f;
            _verifiedHoverHasTargetAnchor = false;
            _verifiedHoverUntilTick = 0;
            _verifiedHoverLastTryTick = 0;
            _verifiedHoverRetryCount = 0;
        }

        private void ResetRecentProbePoints()
        {
            _recentProbePointNext = 0;
            Array.Clear(_recentProbePointX, 0, _recentProbePointX.Length);
            Array.Clear(_recentProbePointY, 0, _recentProbePointY.Length);
            Array.Clear(_recentProbePointTick, 0, _recentProbePointTick.Length);
        }

        private bool WasRecentProbePoint(float x, float y, int tick)
        {
            int avoidTicks = AggressiveScanMode
                ? RecentProbePointAvoidTicks
                : NormalRecentProbePointAvoidTicks;

            for (int i = 0; i < RecentProbePointCapacity; i++)
            {
                int age = _recentProbePointTick[i] > 0 ? tick - _recentProbePointTick[i] : int.MaxValue;
                if (age < 0 || age > avoidTicks)
                    continue;

                float dx = _recentProbePointX[i] - x;
                float dy = _recentProbePointY[i] - y;
                if (dx * dx + dy * dy <= RecentProbePointAvoidPxSq)
                    return true;
            }

            return false;
        }

        private void RecordRecentProbePoint(float x, float y, int tick)
        {
            int index = _recentProbePointNext++ % RecentProbePointCapacity;
            _recentProbePointX[index] = x;
            _recentProbePointY[index] = y;
            _recentProbePointTick[index] = tick;
        }

        private struct ProbeGeometry
        {
            public float BodyDx;
            public float BodyDy;
            public float RadiusX;
            public float RadiusY;
        }

        private sealed class LearnedHoverProfile
        {
            public float BodyRatio;
            public float SideRatio;
            public int Samples;
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

        private bool IsCursorAtPoint(float x, float y, float toleranceSq)
        {
            try
            {
                float dx = Hud.Window.CursorX - x;
                float dy = Hud.Window.CursorY - y;
                return dx * dx + dy * dy <= toleranceSq;
            }
            catch { return false; }
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

                if (!IsAutoSnapHoverPoint(x, y) || _movementEscapeLatched)
                    return false;

                if (IsManualCursorOverrideActive(tick))
                    return false;

                if (tick <= _movementDisengageUntilTick)
                    return false;

                int targetX = (int)Math.Round((double)x);
                int targetY = (int)Math.Round((double)y);
                if (IsCursorAtPoint(targetX, targetY, CursorNoOpTolerancePxSq))
                    return true;

                BeginCursorOwnershipIfNeeded(tick);
                if (!SetCursorPosClient(targetX, targetY))
                    return false;
                _lastPluginCursorX = targetX;
                _lastPluginCursorY = targetY;
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

            if (_cursorRestoreSuppressedForEngagement)
            {
                _haveSavedCursor = false;
                return;
            }

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

        private void TrackCursorRestoreTargetSegment(IMonster target, int tick)
        {
            if (!RestoreCursorOnReleaseOrMove || target == null)
                return;

            uint acd = GetMonsterAcdId(target);
            if (acd == 0)
                return;

            bool longHoldEligible = IsBossLike(target) || IsLeader(target) || IsEliteMinionLike(target);

            if (_cursorRestoreSegmentTargetAcdId == 0)
            {
                _cursorRestoreSegmentTargetAcdId = acd;
                _cursorRestoreSegmentLongHoldEligible = longHoldEligible;
                return;
            }

            if (_cursorRestoreSegmentTargetAcdId == acd)
            {
                _cursorRestoreSegmentLongHoldEligible = longHoldEligible;
                return;
            }

            // A new autosnap target starts a new independent press segment. Capture
            // the current cursor before moving away from the previous target, so a
            // quick release on the new target returns to the location the player
            // was just aiming at instead of inheriting a long hold from the old one.
            if (_cursorOwned && _cursorWasMovedByPlugin && _haveSavedCursor)
            {
                try
                {
                    float anchorX = Hud.Window.CursorX;
                    float anchorY = Hud.Window.CursorY;
                    if (IsCursorRestorePoint(anchorX, anchorY))
                    {
                        _savedCursorX = anchorX;
                        _savedCursorY = anchorY;
                        _engageStartTick = tick;
                    }
                }
                catch { }
            }

            _cursorRestoreSegmentTargetAcdId = acd;
            _cursorRestoreSegmentLongHoldEligible = longHoldEligible;
        }

        private bool TryRestoreCursorImmediately(int tick)
        {
            if (!RestoreCursorOnReleaseOrMove || !_cursorOwned || !_cursorWasMovedByPlugin || !_haveSavedCursor)
            {
                ReleaseCursorOwnershipWithoutRestore();
                return false;
            }

            int engagedTicks = _engageStartTick > 0
                ? Math.Max(0, tick - _engageStartTick)
                : int.MaxValue;

            // Long-hold parking applies only to elite-like targets. Trash and
            // unclassified anchors always restore because leaving the cursor on
            // them provides no intentional long-lock benefit.
            if (_cursorRestoreSegmentLongHoldEligible
                && engagedTicks > CursorRestoreShortEngageTicks)
            {
                ReleaseCursorOwnershipWithoutRestore();
                return false;
            }

            float targetX = _savedCursorX;
            float targetY = _savedCursorY;
            bool restored = false;

            try
            {
                float dx = targetX - Hud.Window.CursorX;
                float dy = targetY - Hud.Window.CursorY;
                if (dx * dx + dy * dy >= CursorRestoreMinMovePxSq
                    && Hud.Window.IsForeground
                    && IsCursorRestorePoint(targetX, targetY))
                {
                    restored = SetCursorPosClient(
                        (int)Math.Round((double)targetX),
                        (int)Math.Round((double)targetY));
                }
            }
            catch { }

            ReleaseCursorOwnershipWithoutRestore();
            return restored;
        }

        private void RefreshCursorRestoreWindowForFreshEngagement(int tick)
        {
            if (!RestoreCursorOnReleaseOrMove)
                return;

            // Every physical press is independent. This is normally reset by the
            // preceding release; the assignment also protects rapid edge sampling.
            if (_cursorOwned && _cursorWasMovedByPlugin && _haveSavedCursor)
                _engageStartTick = tick;
        }

        private void SuppressCursorRestoreForEngagement()
        {
            _cursorRestoreSuppressedForEngagement = true;
            ReleaseCursorOwnershipWithoutRestore();
        }

        private void ReleaseCursorOwnershipWithoutRestore()
        {
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _engageStartTick = 0;
            _cursorRestoreSegmentTargetAcdId = 0;
            _cursorRestoreSegmentLongHoldEligible = false;
        }

        private bool SetCursorPosClient(int x, int y)
        {
            try
            {
                long screenX = (long)x + Hud.Window.Offset.X;
                long screenY = (long)y + Hud.Window.Offset.Y;
                if (screenX < int.MinValue || screenX > int.MaxValue || screenY < int.MinValue || screenY > int.MaxValue)
                    return false;
                return SetCursorPos((int)screenX, (int)screenY);
            }
            catch { return false; }
        }

        private bool IsInsideGameWindow(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                return x >= 0f && y >= 0f && x < Hud.Window.Size.Width && y < Hud.Window.Size.Height;
            }
            catch { return false; }
        }

        private bool IsAutoSnapHoverPoint(float x, float y)
        {
            // Hover movement is allowed only while Corpse Lance is physically held.
            return IsInsideGameWindow(x, y);
        }

        private bool IsCursorRestorePoint(float x, float y)
        {
            return IsInsideGameWindow(x, y);
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
                if (IsInsideGlobalClickDangerGuard(x, y)) return true;
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

        private bool IsInsideSkillbarClickGuard(float x, float y)
        {
            return IsInsideScaledSiphonRect(
                x, y,
                SkillbarClickGuardLeft,
                SkillbarClickGuardTop,
                SkillbarClickGuardRight,
                SkillbarClickGuardBottom);
        }

        private bool IsInsideGlobalClickDangerGuard(float x, float y)
        {
            try
            {
                int ix = (int)Math.Round((double)x);
                int iy = (int)Math.Round((double)y);

                if (IsInsideSkillbarClickGuard(x, y)) return true;

                if (IsInsideHudMenuDefaultButton(ix, iy)) return true;
                if (IsInsideAutoLootIndicatorButton(ix, iy)) return true;

                if (IsInsideSiphonBottomRightMenuGuard(x, y)) return true;
                if (IsInsideSiphonBottomLeftMenuGuard(x, y)) return true;
                if (IsInsideSiphonTopRightPanelGuard(x, y)) return true;
                if (IsInsideSiphonTopRightIconGuard(x, y)) return true;
                if (IsInsideSiphonSocialFlyoutGuard(x, y)) return true;
                if (IsInsideSiphonParagonPlusGuard(x, y)) return true;

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

        private bool EnsureSiphonPulseAnchor(IMonster preferredTarget, int tick)
        {
            if (!IsMouseSiphonAction())
                return true;

            if (IsManualCursorOverrideActive(tick))
                return IsCursorOnValidSiphonAnchor() && !IsCursorOverClickDangerUi();

            if (!IsCursorOverClickDangerUi() && IsCursorOnValidSiphonAnchor())
                return true;

            IMonster anchor = PickSiphonAnchorTarget(preferredTarget);
            if (IsAliveTarget(anchor) && TryMoveToSafeSiphonPulseTarget(anchor, tick))
                return true;

            return TryMoveToAlternateSafeSiphonPulseTarget(anchor, tick);
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

            float distance = SafeDistance(monster);
            float range = PestiPick() + HitboxRangeBonus(monster);
            if (distance > range)
                return false;

            if (IsBossLike(monster) || IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return allowTrash && IncludeTrashTargets;
        }

        private float GetSiphonAnchorScore(IMonster monster)
        {
            float distance = SafeDistance(monster);
            if (IsBossLike(monster) || IsLeader(monster))
                return GetElitePriorityScore(monster, distance, true) - 5.0f;

            if (IsEliteMinionLike(monster))
                return distance + 1.5f;

            return distance + SiphonAnchorTrashPenalty;
        }

        private IMonster PickSiphonAnchorTarget(IMonster preferredTarget)
        {
            if (IsValidSiphonAnchorTarget(preferredTarget, true))
                return preferredTarget;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsValidSiphonAnchorTarget(selected, true))
                    return selected;
            }
            catch { }

            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsValidSiphonAnchorTarget(monster, true))
                        continue;

                    float score = GetSiphonAnchorScore(monster);

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

        private bool TryMoveToSafeSiphonPulseTarget(int tick)
        {
            return TryMoveToSafeSiphonPulseTarget(PickSiphonAnchorTarget(GetSafeSiphonPulseTarget()), tick);
        }

        private bool TryMoveToSafeSiphonPulseTarget(IMonster target, int tick)
        {
            float px, py;
            if (!TryGetSafeSiphonPulsePoint(target, out px, out py))
                return false;

            TrackCursorRestoreTargetSegment(target, tick);

            if (!SafeMouseMove(px, py, tick))
                return false;

            _lastMouseMoveTick = tick;
            return true;
        }

        private bool TryGetSafeSiphonPulsePoint(IMonster target, out float safeX, out float safeY)
        {
            safeX = 0f;
            safeY = 0f;

            try
            {
                if (!IsValidSiphonAnchorTarget(target, true))
                    return false;

                float x, y;
                if (!TryGetMonsterScreen(target, out x, out y))
                    return false;

                // Probe the confirmed target body first, then progressively higher/side points.
                // This lets a target partly covered by the skillbar remain a Siphon anchor when
                // some of its model is still outside the click-danger region.
                float[] dx = { 0f, -12f, 12f, 0f, 0f, -24f, 24f, -36f, 36f, 0f, -18f, 18f, 0f, -30f, 30f };
                float[] dy = { 0f, -8f, -8f, -18f, 12f, -12f, -12f, -24f, -24f, -36f, -44f, -44f, -60f, -68f, -68f };

                for (int i = 0; i < dx.Length; i++)
                {
                    float px = x + dx[i];
                    float py = y + dy[i];
                    if (!IsAutoSnapHoverPoint(px, py))
                        continue;

                    int ix = (int)Math.Round((double)px);
                    int iy = (int)Math.Round((double)py);
                    if (IsInsideSiphonClickDangerUi(ix, iy))
                        continue;

                    safeX = px;
                    safeY = py;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryMoveToAlternateSafeSiphonPulseTarget(IMonster excludedTarget, int tick)
        {
            IMonster best = null;
            float bestX = 0f;
            float bestY = 0f;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsValidSiphonAnchorTarget(monster, true) || SameMonster(monster, excludedTarget))
                        continue;

                    float px, py;
                    if (!TryGetSafeSiphonPulsePoint(monster, out px, out py))
                        continue;

                    float score = GetSiphonAnchorScore(monster);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                        bestX = px;
                        bestY = py;
                    }
                }
            }
            catch { }

            if (!IsAliveTarget(best) || !SafeMouseMove(bestX, bestY, tick))
                return false;

            _lastMouseMoveTick = tick;
            return true;
        }

        private IMonster GetSafeSiphonPulseTarget()
        {
            IMonster target = null;

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

            // Start mouse Siphon only from a click-safe point; later hover movement cannot create another click.
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
            _trashAcquireAcdId = 0;
            _trashAcquireStartTick = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _normalScanPauseUntilTick = 0;
            _lastMouseMoveTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _savedCursorX = 0f;
            _savedCursorY = 0f;
            _engageStartTick = 0;
            _cursorRestoreSegmentTargetAcdId = 0;
            _cursorRestoreSegmentLongHoldEligible = false;
            _cursorRestoreSuppressedForEngagement = false;
            _lastHoverAcdId = 0;
            _lastHoverTick = 0;
            _lastHoverDx = 0f;
            _lastHoverDy = 0f;
            _rankedProbeCount = 0;
            _lastSnapAttemptAcd = 0;
            _lastSnapAttemptBin = -1;
            _lastSnapAttemptZone = -1;
            _lastSnapAttemptTick = 0;
            _lastEvaluatedProbeTick = 0;
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
            _teleportDetectedTick = 0;
            ClearPostTeleportReacquire();
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
            ClearPendingPulseHover();
            ClearVerifiedHoverPoint();
            _lastPassiveHoverCaptureTick = 0;
            _lastPassiveHoverCaptureAcdId = 0;
            _recentPriorityLeaderAcdId = 0;
            _recentPriorityLeaderUntilTick = 0;
            _recentProbePointNext = 0;
            _adaptiveModelSno = 0;
            for (int i = 0; i < AdaptiveBinCount; i++)
            {
                _adaptiveBinGood[i] = 0f;
                _adaptiveBinBad[i] = 0f;
            }
            _badHoverAcdId = 0;
            _badHoverCount = 0;
            _badHoverLastTick = 0;
            _leaderPackCoreRetryAcdId = 0;
            _leaderPackCoreRetryTick = 0;
            _forcedSnapAcdId = 0;
            _forcedSnapUntilTick = 0;
            _movementDisengageUntilTick = 0;
            _manualCursorOverrideUntilTick = 0;
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
            _movementEscapeLatched = false;
            _openingSiphonPending = false;
            _openingSiphonExpireTick = 0;
            _openingSiphonHandoffPending = false;
            _openingSiphonHandoffUntilTick = 0;
            _openingSiphonStartedWhileMoving = false;
            _lastMovementStopSiphonTick = 0;
            _siphonKeyWasDown = false;
            _lanceWasDown = false;
            _engageRefreshConsumed = false;
            _lastSiphonTargetAcdId = 0;
            _targetSwitchRefreshAcdId = 0;
            ClearLateRefreshIntent();
            ClearFastBuildGuard();
            ClearNoTargetAnchorLimiter(false);
            _lastPulseWasNoTargetAnchor = false;
            _lastOwnedMouseSiphonPulseTick = 0;
            _lastOwnedChatRiskPulseTick = 0;
            _lastOwnedUiCloseTick = 0;
            _lastCorpseScanTick = 0;
            _cachedCorpsesAvailable = false;
            _lateRefreshPulseConsumed = false;
            _siphonAssistUntilTick = 0;
            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _corpseLanceRuneKnown = false;
            _corpseLanceRune = 0;
            _corpseLanceRicochet = false;
            _corpseLanceRuneNameEnglish = null;
            _lanceKey = ActionKey.Unknown;
            _siphonKey = ActionKey.Unknown;
            _deadTargetHandoffUntilTick = 0;
            ClearRemoteSiphonTarget();
            _siphonAffectedTargetCount = 0;

            if (releaseInput)
                ClearEngagementFocus();
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

        private int ClampRange(int value)
        {
            if (value < 15) return 15;
            if (value > 90) return 90;
            return ((value + 2) / 5) * 5;
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
