using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public static class s7o_ZDH_HelperState
    {
        private const string FileName = "s7o_ZDH_Helper.ini";
        private static bool _loaded;

        public static bool Enabled = true;
        public static bool ShowEliteDebuffs = true;
        public static bool TrackUptime = true;
        public static bool RestoreCursor = true;
        public static bool AutoEntangle = false;
        public static bool AutoMultishot = false;
        public static bool AutoMarkedForDeath = false;
        public static bool AutoSentry = false;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    int split = raw.IndexOf('=');
                    if (split <= 0) continue;
                    string key = raw.Substring(0, split).Trim();
                    string value = raw.Substring(split + 1).Trim();
                    bool parsed;
                    if (!bool.TryParse(value, out parsed)) continue;
                    if (key == "ENABLED") Enabled = parsed;
                    else if (key == "SHOW_LABELS") ShowEliteDebuffs = parsed;
                    else if (key == "TRACK_UPTIME") TrackUptime = parsed;
                    else if (key == "RESTORE_CURSOR") RestoreCursor = true;
                    else if (key == "AUTO_ENTANGLE") AutoEntangle = parsed;
                    else if (key == "AUTO_MULTISHOT") AutoMultishot = parsed;
                    else if (key == "AUTO_MFD") AutoMarkedForDeath = parsed;
                    else if (key == "AUTO_SENTRY") AutoSentry = parsed;
                }
            }
            catch { }
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                string path = SettingsPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(path, new[]
                {
                    "# s7o ZDH Helper settings",
                    "ENABLED=" + Enabled,
                    "SHOW_LABELS=" + ShowEliteDebuffs,
                    "TRACK_UPTIME=" + TrackUptime,
                    "RESTORE_CURSOR=True",
                    "AUTO_ENTANGLE=" + AutoEntangle,
                    "AUTO_MULTISHOT=" + AutoMultishot,
                    "AUTO_MFD=" + AutoMarkedForDeath,
                    "AUTO_SENTRY=" + AutoSentry,
                });
            }
            catch { }
        }

        private static string SettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings", FileName);
        }
    }

    public static class s7o_ZDH_HelperMetrics
    {
        public static long EligibleMilliseconds;
        public static long IceblinkMilliseconds;
        public static long DamageMilliseconds;
        public static long MarkedForDeathMilliseconds;
        public static long MarkedForDeathEligibleMilliseconds;
        public static string LastAction = string.Empty;
        public static string LastResult = string.Empty;
        public static uint LastTargetAcd;
        public static bool LastRestoreConfirmed;
        public static int LastRestoreGuardMaxDrift;
        public static bool CursorSafetyBlocked;
        public static int LastLeaseDurationMs;
        public static int LastMfdOnlyTargets;
        public static string LastVerificationSource = string.Empty;
        public static int LastPauseAckMs;
        public static int LastInputDownMs;
        public static int LastInputUpMs;
        public static int LastEffectMs;
        public static int LastTimingSequence;
        public static float LastLocalTravelSpeed;
        public static float LastClusterDistance;
        public static string LastSchedulerMode = string.Empty;
        public static float LastAdvanceDistance;
        public static bool LastAdvanceSuppressed;
        public static bool LastBossStandalone;
        public static int LastMissingIceblinkElites;
        public static int LastActionableIceblinkElites;
        public static bool LastSentryRetryPending;
        public static int LastSentryRetryAgeMs;
        public static int LastSentryRetryDelayMs;
        public static string LastSentryRetryReason = string.Empty;
        public static int LastSentryOnScreenOwned;
        public static int LastSentryTotalOwned;
        public static bool LastTrashIceblinkQueueDue;
        public static int LastSentryOffScreenOwned;
        public static int LastSentryCurrentFightRelevant;
        public static int LastSentryOnScreenIrrelevant;
        public static int LastSentryCurrentFightTarget;
        public static bool LastMfdReadyForSentryFill;
        public static string LastPreInputAnimation = string.Empty;
        public static bool LastSentryBurstActive;
        public static string LastSentryBurstMode = string.Empty;
        public static string LastSentryBurstStage = string.Empty;
        public static int LastSentryBurstPlanned;
        public static int LastSentryBurstVerified;
        public static string LastSentryBurstEndReason = string.Empty;
        public static int LastSentryBurstWatchdogCount;
        public static int LastSentryBurstChildSequence;
        public static bool LastSentryBurstMovementSettled;
        public static int LastSentryBurstDurationMs;
        public static bool LastSentryRelevanceOnlyDeficit;
        public static int LastSentryRelevanceDeficitAgeMs;
        public static bool LastSentryRelevanceDeficitStable;
        public static bool LastSentryFullFieldHold;
        public static bool LastSpeedCombatCandidate;
        public static bool LastSpeedCombatEngaged;
        public static int LastSpeedCombatAgeMs;
        public static float LastSpeedCombatPathDistance;
        public static float LastSpeedCombatNetDistance;
        public static float LastSpeedCombatStraightness;
        public static int LastSpeedCombatExitAgeMs;
        public static string LastSpeedCombatReason = string.Empty;
        public static bool LastSentryBurstHardMfdReady;
        public static int LastSentryBurstIceblinkRemainingMs;
        public static int LastEliteSentryUncovered;
        public static int LastEliteSentryReady;
        public static uint LastEliteSentryTargetAcd;
        public static int LastEliteSentryAgeMs;
        public static int LastEliteSentryDelayMs;
        public static int LastEliteSentryAttempts;
        public static bool LastChannelingPylonActive;
        public static bool LastSpeedPylonActive;
        public static int LastSentryCharges;
        public static bool LastSentryOnCooldown;
        public static int LastGroundSupportElites;
        public static int LastGroundSupportInvulnerable;
        public static int LastGroundSupportBurrowed;
        public static string LastCompletionOpportunityReason = string.Empty;
        public static bool LastMultishotSweepActive;
        public static int LastMultishotSweepRemaining;
        public static int LastMultishotSweepUncoveredElites;
        public static bool LastMfdOnlyCorePriority;
        public static bool LastCombatTrashSetupChain;
        public static bool LastMfdRetryYieldToSentry;
        public static int LastCursorIntentSequence;
        public static int LastCursorIntentRawDeltaX;
        public static int LastCursorIntentRawDeltaY;
        public static int LastCursorIntentDeltaX;
        public static int LastCursorIntentDeltaY;
        public static bool LastCursorIntentClamped;
        public static int LastCursorIntentSamples;
        public static int LastCursorIntentHeroShiftX;
        public static int LastCursorIntentHeroShiftY;
        public static int LastCursorIntentRestoreX;
        public static int LastCursorIntentRestoreY;
        public static int LastMultishotPendingCount;
        public static int LastMultishotAsyncSequence;
        public static uint LastMultishotAsyncTargetAcd;
        public static int LastMultishotAsyncLatencyMs;
        public static string LastMultishotAsyncResult = string.Empty;
        public static string LastMultishotAsyncSource = string.Empty;

        public static int Percent(long value)
        {
            return EligibleMilliseconds <= 0 ? 0 : (int)Math.Round(value * 100.0 / EligibleMilliseconds);
        }

        public static int MarkedForDeathPercent()
        {
            return MarkedForDeathEligibleMilliseconds <= 0 ? 0 : (int)Math.Round(
                MarkedForDeathMilliseconds * 100.0 / MarkedForDeathEligibleMilliseconds);
        }

        public static int AveragePercent()
        {
            if (EligibleMilliseconds <= 0 && MarkedForDeathEligibleMilliseconds <= 0) return 0;
            return (int)Math.Round((Percent(IceblinkMilliseconds)
                + Percent(DamageMilliseconds) + MarkedForDeathPercent()) / 3.0);
        }

        public static void Reset()
        {
            EligibleMilliseconds = 0;
            IceblinkMilliseconds = 0;
            DamageMilliseconds = 0;
            MarkedForDeathMilliseconds = 0;
            MarkedForDeathEligibleMilliseconds = 0;
            LastAction = string.Empty;
            LastResult = string.Empty;
            LastTargetAcd = 0;
            LastRestoreConfirmed = false;
            LastRestoreGuardMaxDrift = 0;
            CursorSafetyBlocked = false;
            LastLeaseDurationMs = 0;
            LastMfdOnlyTargets = 0;
            LastVerificationSource = string.Empty;
            LastPauseAckMs = -1;
            LastInputDownMs = -1;
            LastInputUpMs = -1;
            LastEffectMs = -1;
            LastTimingSequence = 0;
            LastLocalTravelSpeed = 0;
            LastClusterDistance = 0;
            LastSchedulerMode = string.Empty;
            LastAdvanceDistance = 0;
            LastAdvanceSuppressed = false;
            LastBossStandalone = false;
            LastMissingIceblinkElites = 0;
            LastActionableIceblinkElites = 0;
            LastSentryRetryPending = false;
            LastSentryRetryAgeMs = 0;
            LastSentryRetryDelayMs = 0;
            LastSentryRetryReason = string.Empty;
            LastSentryOnScreenOwned = 0;
            LastSentryTotalOwned = 0;
            LastTrashIceblinkQueueDue = false;
            LastSentryOffScreenOwned = 0;
            LastSentryCurrentFightRelevant = 0;
            LastSentryOnScreenIrrelevant = 0;
            LastSentryCurrentFightTarget = 0;
            LastMfdReadyForSentryFill = false;
            LastPreInputAnimation = string.Empty;
            LastSentryBurstActive = false;
            LastSentryBurstMode = string.Empty;
            LastSentryBurstStage = string.Empty;
            LastSentryBurstPlanned = 0;
            LastSentryBurstVerified = 0;
            LastSentryBurstEndReason = string.Empty;
            LastSentryBurstWatchdogCount = 0;
            LastSentryBurstChildSequence = 0;
            LastSentryBurstMovementSettled = false;
            LastSentryBurstDurationMs = 0;
            LastSentryRelevanceOnlyDeficit = false;
            LastSentryRelevanceDeficitAgeMs = 0;
            LastSentryRelevanceDeficitStable = true;
            LastSentryFullFieldHold = false;
            LastSpeedCombatCandidate = false;
            LastSpeedCombatEngaged = false;
            LastSpeedCombatAgeMs = 0;
            LastSpeedCombatPathDistance = 0;
            LastSpeedCombatNetDistance = 0;
            LastSpeedCombatStraightness = 1;
            LastSpeedCombatExitAgeMs = 0;
            LastSpeedCombatReason = string.Empty;
            LastSentryBurstHardMfdReady = false;
            LastSentryBurstIceblinkRemainingMs = -1;
            LastEliteSentryUncovered = 0;
            LastEliteSentryReady = 0;
            LastEliteSentryTargetAcd = 0;
            LastEliteSentryAgeMs = 0;
            LastEliteSentryDelayMs = 0;
            LastEliteSentryAttempts = 0;
            LastChannelingPylonActive = false;
            LastSpeedPylonActive = false;
            LastSentryCharges = -1;
            LastSentryOnCooldown = false;
            LastGroundSupportElites = 0;
            LastGroundSupportInvulnerable = 0;
            LastGroundSupportBurrowed = 0;
            LastCompletionOpportunityReason = string.Empty;
            LastMultishotSweepActive = false;
            LastMultishotSweepRemaining = 0;
            LastMultishotSweepUncoveredElites = 0;
            LastMfdOnlyCorePriority = false;
            LastCombatTrashSetupChain = false;
            LastMfdRetryYieldToSentry = false;
            LastCursorIntentSequence = 0;
            LastCursorIntentRawDeltaX = 0;
            LastCursorIntentRawDeltaY = 0;
            LastCursorIntentDeltaX = 0;
            LastCursorIntentDeltaY = 0;
            LastCursorIntentClamped = false;
            LastCursorIntentSamples = 0;
            LastCursorIntentHeroShiftX = 0;
            LastCursorIntentHeroShiftY = 0;
            LastCursorIntentRestoreX = 0;
            LastCursorIntentRestoreY = 0;
            LastMultishotPendingCount = 0;
            LastMultishotAsyncSequence = 0;
            LastMultishotAsyncTargetAcd = 0;
            LastMultishotAsyncLatencyMs = -1;
            LastMultishotAsyncResult = string.Empty;
            LastMultishotAsyncSource = string.Empty;
        }
    }

    public sealed class s7o_ZDH_Helper : BasePlugin, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler
    {
        public float VisualRange = 120f;
        public float AutomationRange = 75f;
        public float EliteEncounterRange = 65f;
        public float ZdhParticipationRange = 80f;
        public int RecentDamageWindowMs = 1800;
        public int PrimaryEliteMaintenanceMs = 4000;
        public int SampleIntervalMs = 100;
        public int BossEntangleMaintenanceMs = 900;
        public int MultishotMaintenanceMs = 2100;
        public int EfficientMultishotLeadMs = 0;
        public float EfficientMultishotCoverageRatio = 0.80f;
        public int AttackMultishotMaintenanceMs = 2100;
        public int BossMultishotMaintenanceMs = 2100;
        public int MultishotFailedRetryMs = 250;
        public int MultishotRefreshRetryMs = 500;
        public int IceblinkExpectedDurationMs = 3000;
        public int IceblinkValidationSlackMs = 250;
        public int IceblinkFirstObservedGraceMs = 750;
        public int IceblinkMaxRefreshAttempts = 2;
        public int IceblinkPrimaryPreemptLeadMs = 100;
        public int CombatSupportPrimaryQuietMs = 180;
        public int SpeedSupportPrimaryQuietMs = 120;
        public int BossSupportPrimaryQuietMs = 80;
        public int PrimaryPreemptLeaseMs = 350;
        public float PylonInteractionPauseRange = 15f;
        public int FailedCastRetryMs = 450;
        public int GlobalCastGapMs = 450;
        public int EntangleAimSettleMs = 16;
        public int MultishotAimSettleMs = 36;
        public int CombatMultishotAimSettleMs = 36;
        public int CombatGroundAimSettleMs = 22;
        public int GroundAimSettleMs = 22;
        public int SentryAimSettleMs = 34;
        public int EntangleSkillHoldMs = 28;
        public int MultishotSkillHoldMs = 40;
        public int MultishotRunningSkillHoldMs = 60;
        public int MultishotMovementSettleGraceMs = 140;
        public int GroundSkillHoldMs = 34;
        public int SentrySkillHoldMs = 40;
        public int MinimumCastLeaseMs = 105;
        public int StrafePauseAckTimeoutMs = 80;
        public int CastPauseHardLimitMs = 200;
        public int MultishotPreInputHardLimitMs = 260;
        public int GroundPreInputHardLimitMs = 240;
        public int SentryPreInputHardLimitMs = 260;
        public int CastPostInputHardLimitMs = 320;
        public int CursorRestoreTolerancePixels = 10;
        public int CursorIntentMaxRestorePixels = 96;
        public int CursorRestoreRetryMs = 8;
        public int CursorRestoreTimeoutMs = 90;
        public int CursorRestoreSettleMs = 16;
        public int CursorSafetyRecoveryMs = 350;
        private const int CursorRestoreGuardDriftPixels = 160;
        private const int CursorRestoreGuardAimTolerancePixels = 120;
        private const int CursorRestoreGuardEdgeMarginPixels = 24;
        private const int CursorRestoreGuardMaxCorrections = 2;
        private const int MultishotNativeAnimationCorrelationMs = 350;
        public int AimCorrectionRetryMs = 16;
        public int AimCorrectionLimit = 2;
        public int SupportAimCorrectionLimit = 1;
        public float AimDisplacementTolerancePixels = 40f;
        public float SupportAimDisplacementTolerancePixels = 55f;
        public int MovementModeCastGapMs = 650;
        public int BossStandaloneCastGapMs = 350;
        public int UrgentRetryGapMs = 300;
        public int MovementUrgentRetryGapMs = 450;
        public int BossUrgentRetryGapMs = 200;
        public int UrgentRetryLifetimeMs = 1800;
        public int AttackMovementWindowMs = 800;
        public int MovementModeMovementWindowMs = 1000;
        public int BossMovementWindowMs = 500;
        public int AttackIceblinkMovementWindowMs = 500;
        public int MovementIceblinkMovementWindowMs = 750;
        public int BossIceblinkMovementWindowMs = 350;
        public int AttackMfdMovementWindowMs = 600;
        public int MovementMfdMovementWindowMs = 800;
        public int BossMfdMovementWindowMs = 400;
        public int AttackSentryMovementWindowMs = 650;
        public int MovementSentryMovementWindowMs = 850;
        public int BossStandaloneStableMs = 400;
        public float BossStandaloneMaxSpeed = 4f;
        public float BossStandaloneRange = 65f;
        public int TravelSampleMs = 250;
        public int TravelHoldMs = 450;
        public float TravelSpeedThreshold = 14f;
        public float TravelEngagedClusterRange = 42f;
        public float MobilityAdvanceDistance = 50f;
        public float MobilityAdvanceResetSpeed = 6f;
        public int MobilityAdvanceSettleMs = 500;
        public int MobilityAdvanceProgressHoldMs = 700;
        public int EntangleVerifyMs = 240;
        public int MultishotVerifyMs = 700;
        public int MarkedForDeathVerifyMs = 500;
        public int SentryVerifyMs = 350;
        public int MarkedForDeathPrimaryQuietMs = 180;
        public int MultishotPrimaryQuietMs = 420;
        public int SentryPrimaryQuietMs = 350;
        public int MarkedForDeathRecastMs = 2500;
        public int MarkedForDeathUrgentRecastMs = 550;
        public int MfdEliteGainStableMs = 200;
        public int MfdEliteGainRecastMs = 600;
        public int SentryRecastMs = 300;
        public int InitialSetupBurstGapMs = 180;
        public float SentryStackedDistance = 14f;
        public float SentryProtectedMinSeparation = 16f;
        public float SentryDistinctCoreSeparation = 18f;
        public float SentryMinScreenSeparationPixels = 32f;
        public float SentryVisiblePatternMinScale = 0.75f;
        public float SentryVisiblePatternScaleStep = 0.05f;
        public int SentryFailedRetryMs = 350;
        public int SentryUserOverrideRetryMs = 650;
        public int SentryRejectedPositionHoldMs = 1400;
        public float SentryRejectedPositionRadius = 9f;
        public int SentrySetupPrimaryQuietMs = 120;
        public int SentrySetupPreemptLeaseMs = 260;
        public int SentryCoreBurstAbsoluteMaxMs = 1700;
        public int SentryCompletionBurstAbsoluteMaxMs = 1100;
        public int SentryBurstAcquireMaxMs = 260;
        public int SentryBurstMovementSettleMaxMs = 300;
        public float SentryBurstMinorAimDisplacementPixels = 80f;
        public int SentryCoreBurstMaxAttemptsPerEngagement = 3;
        public int SentryCoreLossRearmMs = 120;
        public int SentryRelevanceDeficitStabilityMs = 180;
        public int EliteSentryCoverageInitialMs = 300;
        public int EliteSentryCoverageStepMs = 500;
        public int EliteSentryCoverageMaxMs = 2000;
        public int EliteSentryCoverageResetMs = 5000;
        public int EliteSentryCoverageMaxPlacements = 2;
        public int SpeedCombatDwellMs = 750;
        public int SpeedCombatSampleMs = 100;
        public float SpeedCombatMaxStationaryNetDistance = 3f;
        public float SpeedCombatMaxStationaryPathDistance = 4f;
        public float SpeedCombatMaxStationarySpeed = 6f;
        public float SpeedCombatMinOrbitPathDistance = 12f;
        public float SpeedCombatMaxStraightness = 0.90f;
        public float SpeedCombatAnchorResetDistance = 30f;
        public float SpeedCombatLeavingDistance = 8f;
        public float SpeedCombatEngagedRange = 30f;
        public int SpeedCombatDisengageMs = 350;
        public int SentryCoreBurstTailMinRemainingMs = 320;
        public int SentryCoreBurstMaxTailSentries = 1;
        public int SentryCompletionCoalesceMs = 250;
        public int SentryPackStableMs = 300;
        public int SentryDpsStableMs = 900;
        public int SentryPackSlots = 5;
        public int InitialSentryFieldCount = 3;
        public float SentryFieldRelevanceRadius = 35f;
        public float SentryPatternColumnSpacing = 24f;
        public float SentryPatternMatchRadius = 12f;
        public float SentryMinSeparation = 22f;
        public float MultishotAimDistance = 65f;
        public float MultishotSafeTopRatio = 0.08f;
        public float MultishotSafeBottomRatio = 0.78f;
        public float MultishotSafeSideRatio = 0.04f;
        public float GroundCastSafeBottomRatio = 0.84f;
        public float MultishotRange = 78f;
        public float MultishotConeHalfAngleDegrees = 45f;
        public int TrashClusterMinBodies = 6;
        public int TrashClusterMinDamagedBodies = 2;
        public int TrashFightLatchMs = 1800;
        public int TrashFightLatchMinBodies = 3;
        public int TrashFightLatchMinDamagedBodies = 1;
        public float TrashFightLatchRadius = 36f;
        public int CombatClusterStableMs = 350;
        public int PartyFocusConfirmSamples = 2;
        public int PartyFocusLingerMs = 700;
        public int PartyFocusSpecialTargetMs = 1300;
        public float CombatClusterRadius = 22f;
        public float CombatBodyNearAnchorRadius = 30f;
        public float HighValueTrashMinRiftProgression = 0.70f;
        public float SentryLowHealthPct = 65f;
        public float SentryDpsPackRange = 55f;
        public float ValleyRadius = 15f;
        public float GuardianRadius = 16f;
        public int MfdNativeDropoutGraceMs = 350;
        public int MfdDensityMinimumGain = 3;
        public float MfdDensityMinimumGainRatio = 0.20f;
        public int GroundActorAdoptionMs = 1400;
        public ushort Skill1VirtualKey = 0x31;
        public ushort Skill2VirtualKey = 0x32;
        public ushort Skill3VirtualKey = 0x33;
        public ushort Skill4VirtualKey = 0x34;
        public ushort ForceStandstillVirtualKey = 0x10;

        private const string LeaseOwner = "s7o_ZDH_Helper";
        private static int _dhStrafePauseUntilTick = int.MinValue;
        private static int _dhStrafePausedTick = int.MinValue;
        private static int _dhStrafePrimarySuppressUntilTick = int.MinValue;

        public static bool IsDhStrafePauseRequested(int now)
        {
            return _dhStrafePauseUntilTick != int.MinValue
                && unchecked(_dhStrafePauseUntilTick - now) > 0;
        }

        public static string DhStrafePauseOwner
        {
            get { return IsDhStrafePauseRequested(Environment.TickCount) ? LeaseOwner : string.Empty; }
        }

        public static void ConfirmDhStrafePaused(int now)
        {
            if (IsDhStrafePauseRequested(now))
                _dhStrafePausedTick = now;
        }

        private static bool DhStrafePauseAcknowledgedSince(int startedTick)
        {
            return _dhStrafePausedTick != int.MinValue
                && unchecked(_dhStrafePausedTick - startedTick) >= 0;
        }

        private static void RequestDhStrafePause(int durationMs)
        {
            int now = Environment.TickCount;
            int until = unchecked(now + Math.Max(40, Math.Min(1500, durationMs)));
            if (!IsDhStrafePauseRequested(now))
                _dhStrafePausedTick = int.MinValue;
            if (!IsDhStrafePauseRequested(now) || unchecked(until - _dhStrafePauseUntilTick) > 0)
                _dhStrafePauseUntilTick = until;
        }

        private static void ReleaseDhStrafePause()
        {
            _dhStrafePauseUntilTick = int.MinValue;
            _dhStrafePausedTick = int.MinValue;
        }

        public static bool IsDhStrafePrimarySuppressed(int now)
        {
            return _dhStrafePrimarySuppressUntilTick != int.MinValue
                && unchecked(_dhStrafePrimarySuppressUntilTick - now) > 0;
        }

        public static int DhStrafePrimarySuppressionRemainingMs(int now)
        {
            return IsDhStrafePrimarySuppressed(now)
                ? Math.Max(0, unchecked(_dhStrafePrimarySuppressUntilTick - now))
                : 0;
        }

        private static void SuppressDhStrafePrimary(int durationMs)
        {
            if (durationMs <= 0) return;
            int now = Environment.TickCount;
            int until = unchecked(now + Math.Max(20, Math.Min(1000, durationMs)));
            if (!IsDhStrafePrimarySuppressed(now)
                || unchecked(until - _dhStrafePrimarySuppressUntilTick) > 0)
                _dhStrafePrimarySuppressUntilTick = until;
        }

        private static void ReleaseDhStrafePrimarySuppression()
        {
            _dhStrafePrimarySuppressUntilTick = int.MinValue;
        }

        private const uint EntanglingShotSno = 361936;
        private const uint MultishotSno = 77649;
        private const uint MarkedForDeathSno = 130738;
        private const uint SentrySno = 129217;
        private const uint IceblinkSno = 428354;
        private const uint LegacyBombardiersRucksackSno = 318804;
        private const int MultishotSweepMaxShots = 3;
        private const ActorSnoEnum GuardianSentryActor = ActorSnoEnum._dh_sentry_addsshield;
        private static readonly uint[] IdentityAttributeModifiers = { 0u, 0xFFFFFu, 0xFFFFFFFFu, 2147483647u };


        private sealed class TargetState
        {
            public double Health;
            public int LastDamageTick = int.MinValue;
            public int LastSeenTick = int.MinValue;
            public int LastEntangleAttempt = int.MinValue;
            public int LastMultishotAttempt = int.MinValue;
            public int IceblinkConfirmedTick = int.MinValue;
            public int PendingIceblinkRefreshTick = int.MinValue;
            public int PendingIceblinkAttemptCount;
            public bool IceblinkActive;
            public int SentryUncoveredSinceTick = int.MinValue;
            public int SentryCoveredSinceTick = int.MinValue;
            public int SentryCoverageLastActiveTick = int.MinValue;
            public int SentryCoverageAttempts;
        }

        private sealed class PlayerPositionState
        {
            public float X;
            public float Y;
            public int StableSinceTick = int.MinValue;
            public int LastSeenTick = int.MinValue;
        }

        private sealed class CombatCluster
        {
            public readonly List<IMonster> Bodies = new List<IMonster>();
            public readonly List<IMonster> Elites = new List<IMonster>();
            public readonly List<IMonster> MfdOnlyTargets = new List<IMonster>();
            public float CenterX;
            public float CenterY;
            public float CenterZ;
            public float AxisX = 1f;
            public float AxisY;
            public float MajorExtent;
            public float MinorExtent;
            public int RecentDamageCount;
            public double Score;
            public bool Stable;
            public bool TrashLatched;
            public int PriorityEliteCount;
            public IMonster FocusTarget;
            public bool SustainedSpecialFocus;
        }

        private sealed class ZdhLoadout
        {
            public IPlayer Player;
            public IPlayerSkill Entangle;
            public IPlayerSkill Multishot;
            public IPlayerSkill MarkedForDeath;
            public IPlayerSkill Sentry;
            public bool Odyssey;
            public bool Iceblink;
            public bool WindChill;
            public bool Valley;
            public bool Guardian;
            public bool CustomEngineering;
            public bool BombardiersRucksack;

            public bool QualifiesForDisplay
            {
                get { return Player != null && Odyssey && Iceblink && Entangle != null && Multishot != null && WindChill && MarkedForDeath != null && Valley; }
            }
        }

        private enum CastKind { None, Entangle, Multishot, MarkedForDeath, Sentry }
        private enum CastStage { Idle, Lease, Aim, Hold, Restore, RestoreSettle, Verify }
        private enum SentryBurstMode { None, Core, Completion }
        private enum SentryBurstStage { Idle, Acquire, Settle, Ready }

        private sealed class RuntimeState
        {
            public int SentryDesired;
            public bool HighFrequencyMode;
            public bool MfdCoverageSetChanged;
            public float SentryAnchorX;
            public float SentryAnchorY;
            public int SentryPlacementDeficit;
            public bool TrashFightLatched;
            public int TrashFightLatchBodies;
            public bool ProtectedSentryCoverageMissing;
        }

        private sealed class PendingCast
        {
            public CastKind Kind;
            public CastStage Stage;
            public IPlayerSkill Skill;
            public uint TargetAcd;
            public int StartedTick;
            public int DueTick;
            public int VerifyUntilTick;
            public int PauseAckTick;
            public int AimReadyTick;
            public int InputDownTick;
            public int InputUpTick;
            public int RestoreTick;
            public int AimSettleMs;
            public int HoldMs;
            public int MinimumLeaseMs;
            public int VerifyMs;
            public int SavedCursorX;
            public int SavedCursorY;
            public int SavedHeroScreenX;
            public int SavedHeroScreenY;
            public bool SavedHeroScreenValid;
            public int CursorReferenceX;
            public int CursorReferenceY;
            public bool CursorReferenceValid;
            public int UserCursorDeltaX;
            public int UserCursorDeltaY;
            public int UserCursorDeltaSamples;
            public int AimX;
            public int AimY;
            public bool StandstillHeld;
            public bool ActionHeld;
            public bool CursorOwned;
            public int RestoreX;
            public int RestoreY;
            public int RestoreDeadlineTick;
            public int RestoreAttempts;
            public int RestoreGuardCorrections;
            public bool BaselineTargetFlag;
            public bool SawCastAnimation;
            public bool SawNativeMultishotAnimation;
            public AcdAnimationState PauseAckAnimation;
            public AcdAnimationState PreInputAnimation;
            public bool TrashInitialMultishot;
            public bool InputSent;
            public bool RequiresStrafePause;
            public bool BossStandalone;
            public int AimCorrections;
            public float MaxAimDrift;
            public int BaselineCharges;
            public int BaselineOwnedSentries;
            public int VerifyRequiredCount;
            public bool VerifyPrimaryRequired;
            public int BaselineImportantApplied;
            public int RequiredImportantApplied;
            public uint BaselineMfdActorAcd;
            public int BaselineMfdActorCreatedTick;
            public int BaselineMfdGameTick;
            public readonly HashSet<uint> BaselineActorAcds = new HashSet<uint>();
            public readonly HashSet<uint> VerifyTargetAcds = new HashSet<uint>();
            public readonly HashSet<uint> VerifyImportantAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotCoveredEliteAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotBaselineActiveAcds = new HashSet<uint>();
            public readonly HashSet<uint> SentryCoverageAcds = new HashSet<uint>();
            public float ExpectedWorldX;
            public float ExpectedWorldY;
            public string Label;
            public int LastAppliedCount;
            public bool EfficientMultishot;
            public bool SentryMfdAnchor;
            public int SentrySlot;
            public bool SentryFallback;
            public float SentryCastDistance;
            public string SentryFallbackReason;
            public bool SentryRelocated;
            public bool SentryBurstChild;
        }

        private sealed class PendingMultishotValidation
        {
            public int InputTick;
            public int UntilTick;
            public uint TargetAcd;
            public bool TrashInitial;
            public bool AnimationSeen;
            public int AnimationTick = int.MinValue;
            public readonly HashSet<uint> PendingAcds = new HashSet<uint>();
            public readonly HashSet<uint> BaselineActiveAcds = new HashSet<uint>();
            public readonly HashSet<uint> ImportantAcds = new HashSet<uint>();
        }

        private sealed class SentryBurstState
        {
            public SentryBurstMode Mode;
            public SentryBurstStage Stage;
            public int StartedTick = int.MinValue;
            public int AcquireDeadlineTick = int.MinValue;
            public int AbsoluteDeadlineTick = int.MinValue;
            public int SettleDeadlineTick = int.MinValue;
            public bool StandstillOwned;
            public int PlannedSentries;
            public int VerifiedSentries;
            public int TailSentries;
            public int StartRelevant;
            public int CurrentRelevant;
            public int StartCharges;
            public int TargetCount;
            public float AnchorX;
            public float AnchorY;
            public bool ChildJustFinished;
            public string EndReason = string.Empty;
        }

        private sealed class RejectedSentryPosition
        {
            public float X;
            public float Y;
            public int Tick;
            public int Slot;
            public string Reason;
        }

        private readonly Dictionary<uint, TargetState> _targets = new Dictionary<uint, TargetState>();
        private readonly Dictionary<uint, PlayerPositionState> _playerPositions = new Dictionary<uint, PlayerPositionState>();
        private readonly HashSet<uint> _ownedActorAcds = new HashSet<uint>();
        private readonly RuntimeState _runtime = new RuntimeState();
        private readonly PendingCast _cast = new PendingCast();
        private readonly List<PendingMultishotValidation> _pendingMultishots = new List<PendingMultishotValidation>();
        private AnimSnoEnum _lastObservedPlayerAnimation;
        private bool _lastObservedPlayerAnimationValid;
        private readonly SentryBurstState _sentryBurst = new SentryBurstState();
        private bool _coreBurstAttemptedForEngagement;
        private int _coreBurstAttemptsThisEngagement;
        private int _coreBurstRetryAfterTick = int.MinValue;
        private int _coreBurstBelowTargetSinceTick = int.MinValue;
        private int _sentryRelevanceDeficitSinceTick = int.MinValue;
        private bool _fullSentryFieldEstablishedForEngagement;
        private bool _sentryFullFieldHold;
        private bool _coreBurstAnchorValid;
        private float _coreBurstAnchorX;
        private float _coreBurstAnchorY;
        private bool _wasSentryEngagementActive;
        private int _lastCompletionBurstAttemptCharges = -1;
        private int _lastCompletionBurstAttemptRelevant = -1;
        private float _lastCompletionBurstAttemptAnchorX;
        private float _lastCompletionBurstAttemptAnchorY;
        private bool _lastCompletionBurstAttemptAnchorValid;
        private int _lastCompletionBurstAttemptTick = int.MinValue;
        private bool _channelingPylonActive;
        private bool _speedPylonActive;
        private int _multishotSweepRemaining;
        private int _multishotSweepUntilTick = int.MinValue;
        private int _lastSampleTick = int.MinValue;
        private int _lastCastFinishedTick = int.MinValue;
        private CastKind _lastSupportKind = CastKind.None;
        private bool _supportPrimaryGateBlocked;
        private int _lastEntangleMaintenanceTick = int.MinValue;
        private int _lastMultishotMaintenanceTick = int.MinValue;
        private int _lastMfdCastTick = int.MinValue;
        private int _lastMfdSetupHandoffTick = int.MinValue;
        private int _lastUnverifiedMfdTick = int.MinValue;
        private int _lastSentryCastTick = int.MinValue;
        private int _lastObservedSentryCharges = -1;
        private int _lastSentryChargeIncreaseTick = int.MinValue;
        private int _sentryRetryTick = int.MinValue;
        private int _sentryRetryDelayMs;
        private string _sentryRetryReason = string.Empty;
        private readonly List<RejectedSentryPosition> _rejectedSentryPositions = new List<RejectedSentryPosition>();
        private float _lastValleyX;
        private float _lastValleyY;
        private int _lastValleyTick = int.MinValue;
        private uint _lastValleyActorAcd;
        private int _lastValleyActorCreatedTick;
        private int _lastValleyActorSeenTick = int.MinValue;
        private int _mfdActorEpochGameTick;
        private bool _wasDead;
        private bool _bossEntangleStandstillOwned;
        private bool _bossEntangleStandstillReleasePending;
        private int _lastPauseReleasedTick = int.MinValue;
        private string _mfdImprovementSignature = string.Empty;
        private int _mfdImprovementTick = int.MinValue;
        private float _packCandidateX;
        private float _packCandidateY;
        private float _packCandidateZ;
        private int _packCandidateTick = int.MinValue;
        private bool _packCandidateValid;
        private int _trashFightLatchUntilTick = int.MinValue;
        private float _trashFightLatchX;
        private float _trashFightLatchY;
        private float _trashFightLatchZ;
        private float _trashFightLatchAxisX = 1f;
        private float _trashFightLatchAxisY;
        private float _trashFightLatchMajorExtent;
        private float _trashFightLatchMinorExtent;
        private int _trashFightLatchConfirmedBodies;
        private int _trashFightLatchConfirmedDamaged;
        private string _trashFightLatchState = string.Empty;
        private bool _trashInitialMultishotDone;
        private bool _cursorSafetyBlocked;
        private int _cursorSafetyBlockedTick = int.MinValue;
        private int _cursorSafetyRestoreX;
        private int _cursorSafetyRestoreY;
        private float _localTravelX;
        private float _localTravelY;
        private int _localTravelSampleTick = int.MinValue;
        private int _localTravelUntilTick = int.MinValue;
        private float _localTravelSpeed;
        private float _advanceAnchorX;
        private float _advanceAnchorY;
        private float _advanceDistance;
        private float _previousAdvanceDistance;
        private int _advanceSettledTick = int.MinValue;
        private int _advanceProgressTick = int.MinValue;
        private bool _advanceSuppressed;
        private bool _speedCombatCandidateValid;
        private int _speedCombatCandidateTick = int.MinValue;
        private int _speedCombatSampleTick = int.MinValue;
        private float _speedCombatStartX;
        private float _speedCombatStartY;
        private float _speedCombatLastX;
        private float _speedCombatLastY;
        private float _speedCombatPathDistance;
        private float _speedCombatAnchorX;
        private float _speedCombatAnchorY;
        private float _speedCombatMinClusterDistance;
        private bool _speedCombatEngaged;
        private int _speedCombatLeavingTick = int.MinValue;
        private bool _bossStandaloneActive;
        private uint _trackedBossAcd;
        private int _bossMissingTick = int.MinValue;
        private CastKind _recentGroundKind = CastKind.None;
        private float _recentGroundX;
        private float _recentGroundY;
        private int _recentGroundTick = int.MinValue;
        private CastKind _urgentRetryKind = CastKind.None;
        private int _urgentRetryTick = int.MinValue;
        private uint _partyFocusCandidateAcd;
        private int _partyFocusCandidateSamples;
        private uint _partyFocusAcd;
        private int _partyFocusSinceTick = int.MinValue;
        private int _partyFocusUntilTick = int.MinValue;
        private IFont _greenFont;
        private IFont _yellowFont;
        private IFont _orangeFont;
        private IFont _redFont;
        private IFont _tooltipLabelFont;
        private IFont _tooltipGreenFont;
        private IFont _tooltipYellowFont;
        private IFont _tooltipOrangeFont;
        private IFont _tooltipRedFont;
        private IUiElement _chatEditLine;

        public s7o_ZDH_Helper()
        {
            Enabled = true;
            Order = 20500;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            s7o_ZDH_HelperState.EnsureLoaded();
            _greenFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 90, 255, 115, true, false, 235, 0, 0, 0, true);
            _yellowFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 225, 70, true, false, 235, 0, 0, 0, true);
            _orangeFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 145, 45, true, false, 235, 0, 0, 0, true);
            _redFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 70, 70, true, false, 235, 0, 0, 0, true);
            _tooltipLabelFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 235, 235, 235, true, false, 235, 0, 0, 0, true);
            _tooltipGreenFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 90, 255, 115, true, false, 235, 0, 0, 0, true);
            _tooltipYellowFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 255, 225, 70, true, false, 235, 0, 0, 0, true);
            _tooltipOrangeFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 255, 145, 45, true, false, 235, 0, 0, 0, true);
            _tooltipRedFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 255, 70, 70, true, false, 235, 0, 0, 0, true);
            _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);
            _wasDead = Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.IsDead;
            _mfdActorEpochGameTick = Hud.Game == null ? 0 : Hud.Game.CurrentGameTick;
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ForceAbortSentryBurst("new area", Environment.TickCount);
            CancelCast("new area");
            ReleaseBossEntangleStandstill();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            _targets.Clear();
            _playerPositions.Clear();
            ResetOwnedGroundEffectState("new area");
            _lastSampleTick = int.MinValue;
            _lastCastFinishedTick = int.MinValue;
            _lastSupportKind = CastKind.None;
            _supportPrimaryGateBlocked = false;
            _lastEntangleMaintenanceTick = int.MinValue;
            _lastMultishotMaintenanceTick = int.MinValue;
            _lastMfdCastTick = int.MinValue;
            _lastMfdSetupHandoffTick = int.MinValue;
            _lastUnverifiedMfdTick = int.MinValue;
            _lastSentryCastTick = int.MinValue;
            _lastObservedSentryCharges = -1;
            _lastSentryChargeIncreaseTick = int.MinValue;
            ResetSentryBurstEngagement();
            ClearSentryRetry();
            ClearRejectedSentryPositions();
            _lastPauseReleasedTick = int.MinValue;
            ClearMfdImprovementCandidate();
            _packCandidateTick = int.MinValue;
            _packCandidateValid = false;
            ClearTrashFightLatch("new area");
            _cursorSafetyBlocked = false;
            _cursorSafetyBlockedTick = int.MinValue;
            _cursorSafetyRestoreX = 0;
            _cursorSafetyRestoreY = 0;
            _localTravelSampleTick = int.MinValue;
            _localTravelUntilTick = int.MinValue;
            _localTravelSpeed = 0;
            _channelingPylonActive = false;
            _speedPylonActive = false;
            _advanceAnchorX = 0;
            _advanceAnchorY = 0;
            _advanceDistance = 0;
            _previousAdvanceDistance = 0;
            _advanceSettledTick = int.MinValue;
            _advanceProgressTick = int.MinValue;
            _advanceSuppressed = false;
            ResetSpeedCombatIntent("new area");
            _bossStandaloneActive = false;
            _trackedBossAcd = 0;
            _bossMissingTick = int.MinValue;
            _urgentRetryKind = CastKind.None;
            _urgentRetryTick = int.MinValue;
            ClearPartyFocus();
            s7o_ZDH_HelperMetrics.CursorSafetyBlocked = false;
            s7o_ZDH_HelperMetrics.LastLocalTravelSpeed = 0;
            _wasDead = Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.IsDead;
            if (newGame) s7o_ZDH_HelperMetrics.Reset();
        }

        private void ResetOwnedGroundEffectState(string reason)
        {
            _ownedActorAcds.Clear();
            _lastValleyX = 0;
            _lastValleyY = 0;
            _lastValleyTick = int.MinValue;
            _lastValleyActorAcd = 0;
            _lastValleyActorCreatedTick = 0;
            _lastValleyActorSeenTick = int.MinValue;
            _mfdActorEpochGameTick = Hud.Game == null ? 0 : Hud.Game.CurrentGameTick;
            _recentGroundKind = CastKind.None;
            _recentGroundX = 0;
            _recentGroundY = 0;
            _recentGroundTick = int.MinValue;
            _lastMfdCastTick = int.MinValue;
            _lastMfdSetupHandoffTick = int.MinValue;
            _lastUnverifiedMfdTick = int.MinValue;
            _lastSentryCastTick = int.MinValue;
            _lastObservedSentryCharges = -1;
            _lastSentryChargeIncreaseTick = int.MinValue;
            ResetSentryBurstEngagement();
            ResetSpeedCombatIntent(reason);
            ClearMfdImprovementCandidate();
            ClearSentryRetry();
            ClearRejectedSentryPositions();
            foreach (TargetState state in _targets.Values)
            {
                state.SentryUncoveredSinceTick = int.MinValue;
                state.SentryCoveredSinceTick = int.MinValue;
                state.SentryCoverageLastActiveTick = int.MinValue;
                state.SentryCoverageAttempts = 0;
            }
            ResetEliteSentryCoverageMetrics();
            ClearTrashFightLatch(reason);
            ClearMultishotSweep();
            ClearPendingMultishotValidations();
            _urgentRetryKind = CastKind.None;
            _urgentRetryTick = int.MinValue;

        }

        public void AfterCollect()
        {
            s7o_ZDH_HelperState.EnsureLoaded();
            int now = Environment.TickCount;
            s7o_ZDH_HelperMetrics.LastTrashIceblinkQueueDue = false;
            s7o_ZDH_HelperMetrics.LastMfdOnlyCorePriority = false;
            s7o_ZDH_HelperMetrics.LastCombatTrashSetupChain = false;
            s7o_ZDH_HelperMetrics.LastMfdRetryYieldToSentry = false;
            _runtime.ProtectedSentryCoverageMissing = false;
            s7o_ZDH_HelperMetrics.LastMfdReadyForSentryFill = false;
            s7o_ZDH_HelperMetrics.LastSentryBurstHardMfdReady = false;
            s7o_ZDH_HelperMetrics.LastSentryBurstIceblinkRemainingMs = -1;
            PublishRejectedSentryPositions(now);
            PublishTrashFightLatch(now, _runtime.TrashFightLatched,
                _runtime.TrashFightLatchBodies);
            _supportPrimaryGateBlocked = false;
            bool dead = Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.IsDead;
            if (dead != _wasDead)
            {
                _wasDead = dead;
                if (dead) ForceAbortSentryBurst("death", now);
                ResetOwnedGroundEffectState(dead ? "death" : "revive");
            }
            if (!ContextAvailable())
            {
                _lastUnverifiedMfdTick = int.MinValue;
                ClearTrashFightLatch("context");
                PublishTrashFightLatch(now, false, 0);
                ResetSpeedCombatIntent("context");
                ClearPendingMultishotValidations();
                ForceAbortSentryBurst("context", now);
                CancelCast("context");
                ReleaseBossEntangleStandstill();
                return;
            }

            UpdateBossEncounterState(now);
            bool strafeMacroRunning = s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh;
            bool highFrequencyMode = strafeMacroRunning && s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh;
            ZdhLoadout local = BuildLoadout(Hud.Game.Me);
            UpdatePylonState();
            UpdateSentryChargeTelemetry(local == null || local.Sentry == null ? -1 : local.Sentry.Charges, now);
            s7o_ZDH_HelperMetrics.LastSentryCharges = local == null || local.Sentry == null ? -1 : local.Sentry.Charges;
            s7o_ZDH_HelperMetrics.LastSentryOnCooldown = local != null && local.Sentry != null && local.Sentry.IsOnCooldown;
            if (local != null && local.Player != null)
                UpdateLocalTravelState(local.Player, now);

            IMonster bossSpawnAnchor = FindBossSpawnAnchor(local == null ? null : local.Player);
            bool bossPreSpawn = CanUseBossPreSpawn(local, bossSpawnAnchor, now);
            bool bossStandalone = !strafeMacroRunning && CanUseBossStandalone(local, now);
            bool speedMode = strafeMacroRunning && !highFrequencyMode;
            bool sentryBurstContextActive = strafeMacroRunning || bossStandalone || bossPreSpawn;
            _bossStandaloneActive = bossStandalone;
            UpdateBossEntangleStandstill(local, bossStandalone);
            s7o_ZDH_HelperMetrics.LastBossStandalone = bossStandalone;
            _runtime.HighFrequencyMode = highFrequencyMode;
            s7o_ZDH_HelperMetrics.LastSchedulerMode = bossPreSpawn ? "boss-pre"
                : bossStandalone ? "boss" : highFrequencyMode ? "combat" : "speed";
            s7o_ZDH_HelperMetrics.LastLocalTravelSpeed = _localTravelSpeed;
            s7o_ZDH_HelperMetrics.LastAdvanceDistance = _advanceDistance;
            s7o_ZDH_HelperMetrics.LastAdvanceSuppressed = _advanceSuppressed;

            if (!strafeMacroRunning && !bossStandalone && !bossPreSpawn)
            {
                ForceAbortSentryBurst("automation inactive", now);
                _lastUnverifiedMfdTick = int.MinValue;
                ClearCursorSafetyBlock();
                ClearTrashFightLatch("automation inactive");
                ClearMultishotSweep();
                ClearPendingMultishotValidations();
                PublishTrashFightLatch(now, false, 0);
                ResetSpeedCombatIntent("automation inactive");
                ReleaseDhStrafePause();
                ReleaseDhStrafePrimarySuppression();
            }
            else
                TryRecoverCursorSafety(now);

            UpdatePlayerPositionStates(now);
            UpdateOwnedActors(now);
            if (_sentryBurst.Mode != SentryBurstMode.None
                && (!s7o_ZDH_HelperState.Enabled || !sentryBurstContextActive
                    || !SentryBurstAutomationContextValid()))
                ForceAbortSentryBurst("context reset", now);
            EnforceSentryBurstWatchdog(now);
            AdvanceCast(now);
            UpdateTargetStates(now);
            UpdatePendingMultishotValidations(now);
            if (s7o_ZDH_HelperState.TrackUptime) SampleUptime(now);

            if (!s7o_ZDH_HelperState.Enabled
                || (!strafeMacroRunning && !bossStandalone && !bossPreSpawn)
                || _cursorSafetyBlocked
                || _cast.Stage != CastStage.Idle
                || !AutomationContextValid())
                return;

            if (local == null || local.Player == null || (!local.Player.InCombat && !bossStandalone && !bossPreSpawn))
            {
                if (local != null && local.Player != null && !local.Player.InCombat && !bossPreSpawn)
                {
                    ForceAbortSentryBurst("combat ended", now);
                    ResetSentryBurstEngagement();
                    _lastUnverifiedMfdTick = int.MinValue;
                    ClearTrashFightLatch("combat ended");
                    ClearMultishotSweep();
                    ClearPendingMultishotValidations();
                    ResetSpeedCombatIntent("combat ended");
                }
                PublishTrashFightLatch(now, false, 0);
                return;
            }

            UpdatePartyFocus(now);
            s7o_ZDH_HelperMetrics.LastMfdOnlyTargets = 0;
            List<IMonster> bodies = bossPreSpawn
                ? new List<IMonster> { bossSpawnAnchor }
                : bossStandalone
                    ? GetBossCombatBodies(local.Player, now)
                    : GetActiveCombatBodies(local.Player, now, AutomationRange);
            CombatCluster cluster = bossPreSpawn
                ? BuildBossPreSpawnCluster(bossSpawnAnchor)
                : bossStandalone
                    ? BuildBossCombatCluster(bodies, now)
                    : BuildBestCombatCluster(bodies, now);
            if (!bossStandalone && !bossPreSpawn && (cluster == null || cluster.Bodies.Count == 0))
                cluster = BuildLatchedTrashCluster(bodies, now);
            if (cluster == null || cluster.Bodies.Count == 0)
            {
                ForceAbortSentryBurst("cluster lost", now);
                ResetSentryBurstEngagement();
                ResetSpeedCombatIntent("cluster lost");
                if (IsTrashFightLatchActive(now)) ClearTrashFightLatch("cluster lost");
                ClearPendingMultishotValidations();
                PublishTrashFightLatch(now, false, 0);
                return;
            }

            float clusterDistance = ClusterDistance(local.Player, cluster);
            bool eliteTravelOverride = !bossStandalone && !bossPreSpawn && HasEngagedPrimaryEliteHere(cluster, clusterDistance);
            bool travelSuppressed = !eliteTravelOverride && !bossStandalone && !bossPreSpawn && !highFrequencyMode
                && ShouldSuppressForTravel(cluster, clusterDistance, now);
            s7o_ZDH_HelperMetrics.LastClusterDistance = clusterDistance;
            s7o_ZDH_HelperMetrics.LastAdvanceSuppressed = _advanceSuppressed;
            if (travelSuppressed)
            {
                ForceAbortSentryBurst("travel", now);
                ResetSentryBurstEngagement();
                ResetSpeedCombatIntent("travel");
                ClearTrashFightLatch("travel");
                ClearMultishotSweep();
                ClearPendingMultishotValidations();
                PublishTrashFightLatch(now, false, 0);
                return;
            }

            List<IMonster> activePrimaryElites = GetActivePrimaryElites(local.Player, now);
            List<IMonster> groundSupportPrimaryElites = GetActiveGroundSupportPrimaryElites(local.Player, now);
            List<IMonster> activeMfdOnlyTargets = GetActiveGroundSupportMfdOnlyTargets(local.Player, now);
            List<IMonster> groundSupportElites = MergeMonsters(groundSupportPrimaryElites, activeMfdOnlyTargets);
            s7o_ZDH_HelperMetrics.LastGroundSupportElites = groundSupportElites.Count;
            s7o_ZDH_HelperMetrics.LastGroundSupportInvulnerable = groundSupportElites.Count(m => m.Invulnerable);
            s7o_ZDH_HelperMetrics.LastGroundSupportBurrowed = groundSupportElites.Count(m => m.Burrowed);
            bool combatIntentTrashFight = groundSupportPrimaryElites.Count == 0
                && activeMfdOnlyTargets.Count == 0
                && IsCombatIntentTrash(cluster);
            bool densityTrashFight = groundSupportPrimaryElites.Count == 0
                && cluster.Elites.Count == 0 && cluster.Stable
                && cluster.Bodies.Count >= TrashClusterMinBodies
                && cluster.RecentDamageCount >= TrashClusterMinDamagedBodies;
            bool mfdOnlyTrashFight = groundSupportPrimaryElites.Count == 0
                && cluster.Elites.Count == 0 && cluster.MfdOnlyTargets.Count > 0
                && cluster.RecentDamageCount > 0;
            bool freshTrashFight = combatIntentTrashFight || densityTrashFight || mfdOnlyTrashFight;
            if (groundSupportPrimaryElites.Count > 0 || cluster.Elites.Count > 0)
                ClearTrashFightLatch("elite engagement");
            else if (freshTrashFight)
                ArmTrashFightLatch(cluster, now);

            bool trashFightLatched = !freshTrashFight && IsTrashFightLatchActive(now)
                && CanRetainTrashFight(cluster);
            if (!freshTrashFight && IsTrashFightLatchActive(now) && !trashFightLatched)
                ClearTrashFightLatch("cluster lost");

            bool trashFightActive = freshTrashFight || trashFightLatched;
            cluster.TrashLatched = trashFightLatched || combatIntentTrashFight;
            if (trashFightLatched) _trashFightLatchState = "retained";
            PublishTrashFightLatch(now, trashFightLatched || combatIntentTrashFight,
                trashFightActive ? cluster.Bodies.Count : 0);
            int immediateEliteEncounters = activePrimaryElites.Count(m => IsImmediatePrimaryEliteEncounter(m, local.Player));
            int iceblinkRefreshAgeMs = GetIceblinkRefreshAgeMs();
            int trashMultishotMaintenanceAge = _lastMultishotMaintenanceTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastMultishotMaintenanceTick, now);
            int trashDebuffBodies = cluster.Bodies.Count(IsDebuffBody);
            bool trashMultishotMaintenanceDue = trashFightActive
                && trashMultishotMaintenanceAge >= iceblinkRefreshAgeMs;
            bool trashIceblinkQueueDue = trashFightActive && trashDebuffBodies > 0
                && (!_trashInitialMultishotDone || trashMultishotMaintenanceDue);
            int trashIceblinkDue = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && trashIceblinkQueueDue ? trashDebuffBodies : 0;
            bool trashInitialMultishotRequired = trashFightActive
                && groundSupportPrimaryElites.Count == 0 && activeMfdOnlyTargets.Count == 0
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && local.Multishot != null && trashDebuffBodies > 0;
            bool trashInitialMultishotReady = !trashInitialMultishotRequired || _trashInitialMultishotDone;
            s7o_ZDH_HelperMetrics.LastTrashIceblinkQueueDue = trashIceblinkQueueDue;
            int missingIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsDebuffBody(m) && !HasIceblink(m)
                    && !HasPendingMultishotValidation(m.AcdId, now)) : 0;
            int expiringIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsDebuffBody(m) && HasIceblink(m)
                    && IsIceblinkDue(m, now) && !HasPendingMultishotValidation(m.AcdId, now)) : 0;
            int dueIceblinkElites = missingIceblinkElites + expiringIceblinkElites;
            int pendingIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => HasPendingIceblinkValidation(m, now)
                    || HasPendingMultishotValidation(m.AcdId, now)) : 0;
            int actionableIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsIceblinkActionable(m, now)) : 0;
            int preemptIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsIceblinkPrimaryPreemptDue(m, now)) : 0;
            bool multishotSweepActive = _multishotSweepRemaining > 0
                && _multishotSweepUntilTick != int.MinValue
                && !Reached(now, _multishotSweepUntilTick)
                && actionableIceblinkElites > 0;
            if (!multishotSweepActive && (_multishotSweepRemaining > 0 || _multishotSweepUntilTick != int.MinValue))
                ClearMultishotSweep();
            s7o_ZDH_HelperMetrics.LastMultishotSweepActive = multishotSweepActive;
            s7o_ZDH_HelperMetrics.LastMultishotSweepRemaining = multishotSweepActive ? _multishotSweepRemaining : 0;
            s7o_ZDH_HelperMetrics.LastMultishotSweepUncoveredElites = multishotSweepActive ? actionableIceblinkElites : 0;
            bool missingMfdCoverage = s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && HasMissingPrimaryMfdCoverage(local.Player, now);
            bool noMfdCoverage = missingMfdCoverage && HasNoPrimaryMfdCoverage(local.Player, now);
            bool bossEntangleMissing = bossStandalone && s7o_ZDH_HelperState.AutoEntangle
                && local.Odyssey && local.Entangle != null
                && cluster.Elites.Any(m => IsDebuffBody(m) && !HasEntangle(m));
            bool speedCombatFocus = cluster.FocusTarget != null
                && (cluster.SustainedSpecialFocus || IsCurrentPartyFocus(cluster.FocusTarget, now));
            bool speedGroundSupportRetention = _speedCombatEngaged && groundSupportElites.Count > 0;
            bool speedCombatEvidence = speedMode
                && (cluster.RecentDamageCount > 0 || speedCombatFocus || speedGroundSupportRetention)
                && (groundSupportPrimaryElites.Count > 0 || activeMfdOnlyTargets.Count > 0 || trashFightActive);
            bool speedLocalCombat = UpdateSpeedCombatIntent(local.Player, cluster, now, speedMode,
                speedCombatEvidence, clusterDistance);
            bool speedSentryActive = speedLocalCombat && _speedCombatLeavingTick == int.MinValue;
            bool speedSentryPassThrough = speedMode && !speedSentryActive;
            bool sentryBurstStartAllowed = highFrequencyMode || speedSentryActive || bossStandalone || bossPreSpawn;
            if (speedLocalCombat) s7o_ZDH_HelperMetrics.LastSchedulerMode = "speed-combat";
            List<IActor> allOwnedSentries = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetOwnedSentries() : new List<IActor>();
            List<IActor> ownedSentries = allOwnedSentries.Where(a => a != null && a.IsOnScreen).ToList();
            if (s7o_ZDH_HelperState.AutoSentry && local.Guardian)
                UpdateEliteSentryCoverageStates(groundSupportElites, allOwnedSentries, now);
            else
                ResetEliteSentryCoverageMetrics();
            s7o_ZDH_HelperMetrics.LastSentryTotalOwned = allOwnedSentries.Count;
            s7o_ZDH_HelperMetrics.LastSentryOnScreenOwned = ownedSentries.Count;
            int sentryCapacity = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetDesiredSentryCount(local) : 0;
            int sentryEffectiveOwned = 0;
            int sentryPlacementDeficit = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetSentryPlacementDeficit(local, cluster, now, allOwnedSentries, out sentryEffectiveOwned) : 0;
            int sentryTargetCount = Math.Min(sentryCapacity, _runtime.SentryDesired);
            int currentFightRelevantSentries = Math.Min(sentryTargetCount, sentryEffectiveOwned);
            int sentryCountDeficit = Math.Max(0, sentryTargetCount - currentFightRelevantSentries);
            int sentryCoreTarget = Math.Min(Math.Max(1, InitialSentryFieldCount), sentryTargetCount);
            int sentryCoreDeficit = Math.Max(0, sentryCoreTarget
                - Math.Min(sentryCoreTarget, currentFightRelevantSentries));
            bool sentryPlanReady = sentryTargetCount > 0;
            bool sentryEngagementActive = bossPreSpawn || bossStandalone
                || groundSupportPrimaryElites.Count > 0
                || activeMfdOnlyTargets.Count > 0 || trashFightActive;
            UpdateSentryBurstEngagement(cluster, sentryEngagementActive,
                currentFightRelevantSentries, sentryCoreTarget, now);
            if (sentryEngagementActive && sentryTargetCount > 0
                && currentFightRelevantSentries >= sentryTargetCount)
                _fullSentryFieldEstablishedForEngagement = true;
            _sentryFullFieldHold = _fullSentryFieldEstablishedForEngagement
                && sentryTargetCount > 0
                && allOwnedSentries.Count >= sentryTargetCount
                && currentFightRelevantSentries >= sentryCoreTarget;
            s7o_ZDH_HelperMetrics.LastSentryFullFieldHold = _sentryFullFieldHold;

            bool sentryCorePending = sentryEngagementActive && sentryCoreTarget > 0 && sentryCoreDeficit > 0;
            bool mfdOnlyCorePriority = activeMfdOnlyTargets.Count > 0 && sentryCorePending;
            bool effectiveTrashIceblinkQueueDue = trashIceblinkQueueDue && !mfdOnlyCorePriority;
            s7o_ZDH_HelperMetrics.LastMfdOnlyCorePriority = mfdOnlyCorePriority;
            bool currentFightSentryFillPending = sentryEngagementActive && sentryTargetCount > 0
                && currentFightRelevantSentries < sentryTargetCount
                && !_sentryFullFieldHold;
            bool currentFightSentryFillComplete = sentryEngagementActive && sentryTargetCount > 0
                && currentFightRelevantSentries >= sentryTargetCount;

            bool sentryRelevanceOnlyDeficit = sentryEngagementActive
                && sentryTargetCount > 0
                && allOwnedSentries.Count >= sentryTargetCount
                && currentFightRelevantSentries < sentryTargetCount;
            if (sentryRelevanceOnlyDeficit)
            {
                if (_sentryRelevanceDeficitSinceTick == int.MinValue)
                    _sentryRelevanceDeficitSinceTick = now;
            }
            else
            {
                _sentryRelevanceDeficitSinceTick = int.MinValue;
            }
            int sentryRelevanceDeficitAgeMs = _sentryRelevanceDeficitSinceTick == int.MinValue
                ? 0 : Elapsed(_sentryRelevanceDeficitSinceTick, now);
            bool sentryRelevanceDeficitStable = !sentryRelevanceOnlyDeficit
                || sentryRelevanceDeficitAgeMs >= Math.Max(0, SentryRelevanceDeficitStabilityMs);

            bool protectedSentryCoverageMissing = sentryPlacementDeficit > 0
                && _runtime.ProtectedSentryCoverageMissing;
            bool sentrySetupActive = sentryPlanReady && sentryPlacementDeficit > 0
                && sentryEngagementActive && sentryRelevanceDeficitStable
                && (!_sentryFullFieldHold || protectedSentryCoverageMissing)
                && SentryAvailable(local.Sentry);
            if (sentryPlacementDeficit <= 0) ClearSentryRetry();
            bool sentryRetryPending = sentrySetupActive && IsSentryRetryPending();
            int sentryRetryAgeMs = sentryRetryPending ? Elapsed(_sentryRetryTick, now) : 0;
            bool sentryRetryReady = !sentryRetryPending || sentryRetryAgeMs >= Math.Max(0, _sentryRetryDelayMs);
            s7o_ZDH_HelperMetrics.LastMfdOnlyTargets = activeMfdOnlyTargets.Count;
            s7o_ZDH_HelperMetrics.LastMissingIceblinkElites = missingIceblinkElites;
            s7o_ZDH_HelperMetrics.LastActionableIceblinkElites = actionableIceblinkElites;
            int backupIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsIceblinkBackupActionable(m, now)) : 0;
            _runtime.SentryPlacementDeficit = sentryPlacementDeficit;
            s7o_ZDH_HelperMetrics.LastSentryRetryPending = sentryRetryPending;
            s7o_ZDH_HelperMetrics.LastSentryRetryAgeMs = sentryRetryAgeMs;
            s7o_ZDH_HelperMetrics.LastSentryRetryDelayMs = sentryRetryPending ? _sentryRetryDelayMs : 0;
            s7o_ZDH_HelperMetrics.LastSentryRetryReason = sentryRetryPending ? _sentryRetryReason : string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryOffScreenOwned = Math.Max(0, allOwnedSentries.Count - ownedSentries.Count);
            s7o_ZDH_HelperMetrics.LastSentryCurrentFightRelevant = currentFightRelevantSentries;
            s7o_ZDH_HelperMetrics.LastSentryOnScreenIrrelevant = Math.Max(0, ownedSentries.Count - currentFightRelevantSentries);
            s7o_ZDH_HelperMetrics.LastSentryRelevanceOnlyDeficit = sentryRelevanceOnlyDeficit;
            s7o_ZDH_HelperMetrics.LastSentryRelevanceDeficitAgeMs = sentryRelevanceDeficitAgeMs;
            s7o_ZDH_HelperMetrics.LastSentryRelevanceDeficitStable = sentryRelevanceDeficitStable;
            s7o_ZDH_HelperMetrics.LastSentryCurrentFightTarget = sentryTargetCount;

            int mfdSetupHandoffAgeMs = _lastMfdSetupHandoffTick == int.MinValue
                ? -1 : Elapsed(_lastMfdSetupHandoffTick, now);
            bool recentVerifiedMfdSetupHandoff = sentryCorePending
                && mfdSetupHandoffAgeMs >= 0
                && mfdSetupHandoffAgeMs < Math.Max(250, MarkedForDeathUrgentRecastMs);
            bool urgentMfdBeforeSentry = missingMfdCoverage && !recentVerifiedMfdSetupHandoff;
            bool multishotSweepReady = multishotSweepActive
                && !missingMfdCoverage
                && (!speedMode || speedLocalCombat);
            bool trashMfdCoverageMissing = s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && trashFightActive && groundSupportPrimaryElites.Count == 0 && activeMfdOnlyTargets.Count == 0
                && !HasCurrentTrashMfdCoverage(cluster, now);
            bool bossPreSpawnMfdReady = !bossPreSpawn
                || !s7o_ZDH_HelperState.AutoMarkedForDeath || !local.Valley
                || HasAuthoritativeValleyAtPoint(cluster.CenterX, cluster.CenterY, now);
            bool mfdReadyForSentryFill = bossPreSpawn
                ? bossPreSpawnMfdReady
                : !s7o_ZDH_HelperState.AutoMarkedForDeath
                    || !local.Valley || (!missingMfdCoverage && !trashMfdCoverageMissing);
            bool hardIceblinkWork = missingIceblinkElites > 0 && actionableIceblinkElites > 0;
            bool sentryBaseReady = currentFightSentryFillPending
                && !speedMode
                && sentryRelevanceDeficitStable
                && trashInitialMultishotReady
                && sentryRetryReady
                && sentryEngagementActive
                && s7o_ZDH_HelperState.AutoSentry
                && local.Guardian
                && local.Sentry != null
                && SentryAvailable(local.Sentry);
            bool sentryFillReady = sentryBaseReady && mfdReadyForSentryFill;
            bool mfdRetryYieldToSentry = sentryBaseReady
                && !mfdReadyForSentryFill
                && _lastUnverifiedMfdTick != int.MinValue
                && _lastSupportKind == CastKind.MarkedForDeath
                && Elapsed(_lastUnverifiedMfdTick, now)
                    <= Math.Max(750, MarkedForDeathUrgentRecastMs + 250);
            bool combatTrashSetupChain = highFrequencyMode
                && trashFightActive && trashInitialMultishotReady
                && currentFightSentryFillPending && trashMfdCoverageMissing;
            s7o_ZDH_HelperMetrics.LastCombatTrashSetupChain = combatTrashSetupChain;
            s7o_ZDH_HelperMetrics.LastMfdRetryYieldToSentry = mfdRetryYieldToSentry;
            bool hasMultishotFillTargets = sentryFillReady
                && HasMultishotFillTargets(local, cluster, now);
            bool fillInterleaveMultishotTurn = sentryFillReady
                && _lastSupportKind == CastKind.Sentry
                && hasMultishotFillTargets;
            bool yieldIceblinkRetryToSentry = sentryFillReady
                && missingIceblinkElites > 0
                && actionableIceblinkElites == 0
                && _lastSupportKind == CastKind.Multishot
                && !effectiveTrashIceblinkQueueDue
                && !bossEntangleMissing;
            bool iceblinkAllowsSentryFill = missingIceblinkElites == 0 || yieldIceblinkRetryToSentry;
            int sentryRefreshLeadMs = Math.Max(0, IceblinkPrimaryPreemptLeadMs);
            bool eliteSentryRefreshPreempt = sentryFillReady && preemptIceblinkElites > 0;
            bool trashSentryRefreshPreempt = trashFightActive && sentryFillReady
                && _trashInitialMultishotDone
                && trashMultishotMaintenanceAge >= Math.Max(0, iceblinkRefreshAgeMs - sentryRefreshLeadMs);
            bool sentryIceblinkPreempt = eliteSentryRefreshPreempt || trashSentryRefreshPreempt;
            bool fillSentryTurn = sentryFillReady
                && !fillInterleaveMultishotTurn
                && !hardIceblinkWork
                && iceblinkAllowsSentryFill
                && !effectiveTrashIceblinkQueueDue
                && !bossEntangleMissing
                && !sentryIceblinkPreempt;
            bool protectedSentryWorkReady = protectedSentryCoverageMissing
                && !speedSentryPassThrough
                && sentryRelevanceDeficitStable && sentryRetryReady
                && SentryAvailable(local.Sentry);
            bool sentryTimingWorkActive = sentryFillReady || mfdRetryYieldToSentry || protectedSentryWorkReady;
            bool initialDebuffBurst = urgentMfdBeforeSentry
                && _lastSupportKind == CastKind.Multishot
                && actionableIceblinkElites == 0;
            s7o_ZDH_HelperMetrics.LastMfdReadyForSentryFill = mfdReadyForSentryFill;

            bool sentryBurstHardMfdReady = bossPreSpawn
                ? bossPreSpawnMfdReady
                : !s7o_ZDH_HelperState.AutoMarkedForDeath
                    || !local.Valley || (!noMfdCoverage && !trashMfdCoverageMissing);
            bool sentryBurstMfdHandoffReady = !bossPreSpawn
                && recentVerifiedMfdSetupHandoff
                && sentryBurstHardMfdReady
                && activeMfdOnlyTargets.Count == 0
                && !trashMfdCoverageMissing;
            bool sentryBurstMfdStartReady = mfdReadyForSentryFill || sentryBurstMfdHandoffReady;
            int sentryBurstIceblinkChildBudgetMs = Math.Max(450, SentryVerifyMs + 120);
            int sentryBurstEliteIceblinkRemainingMs = GetMinimumIceblinkRemainingMs(activePrimaryElites, now);
            int sentryBurstTrashIceblinkRemainingMs = !trashFightActive || !_trashInitialMultishotDone
                || _lastMultishotMaintenanceTick == int.MinValue
                    ? int.MaxValue
                    : Math.Max(0, IceblinkExpectedDurationMs - trashMultishotMaintenanceAge);
            int sentryBurstIceblinkRemainingMs = Math.Min(
                sentryBurstEliteIceblinkRemainingMs,
                sentryBurstTrashIceblinkRemainingMs);
            bool sentryBurstSoftIceblinkAllowed =
                (!eliteSentryRefreshPreempt
                    || sentryBurstEliteIceblinkRemainingMs >= sentryBurstIceblinkChildBudgetMs)
                && (!trashSentryRefreshPreempt
                    || sentryBurstTrashIceblinkRemainingMs >= sentryBurstIceblinkChildBudgetMs);

            bool sentryBurstContinuationDebuffsClear = sentryBurstHardMfdReady
                && trashInitialMultishotReady
                && !hardIceblinkWork
                && !bossEntangleMissing
                && sentryBurstSoftIceblinkAllowed;
            bool sentryBurstStartDebuffsClear = sentryBurstHardMfdReady
                && trashInitialMultishotReady
                && !hardIceblinkWork
                && !effectiveTrashIceblinkQueueDue
                && !bossEntangleMissing
                && !sentryIceblinkPreempt
                && sentryBurstMfdStartReady
                && !urgentMfdBeforeSentry;

            s7o_ZDH_HelperMetrics.LastSentryBurstHardMfdReady = sentryBurstHardMfdReady;
            s7o_ZDH_HelperMetrics.LastSentryBurstIceblinkRemainingMs =
                sentryBurstIceblinkRemainingMs == int.MaxValue ? -1 : sentryBurstIceblinkRemainingMs;

            if (bossPreSpawn && !bossPreSpawnMfdReady)
            {
                if (TryStartBossPreSpawnMarkedForDeath(local, bossSpawnAnchor, now))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
                return;
            }

            if (_sentryBurst.Mode != SentryBurstMode.None)
            {
                AdvanceSentryBurst(local, cluster, now, currentFightRelevantSentries,
                    sentryTargetCount, sentryCoreTarget, sentryRetryReady,
                    sentryBurstContinuationDebuffsClear, _channelingPylonActive);
                return;
            }

            if (TryBeginCoreSentryBurst(local, cluster, now, sentryBurstStartAllowed,
                sentryEngagementActive, sentryRetryReady, sentryBurstStartDebuffsClear,
                sentryRelevanceDeficitStable,
                currentFightRelevantSentries, sentryCoreTarget, sentryTargetCount, _channelingPylonActive))
                return;

            if (!_sentryFullFieldHold
                && TryBeginCompletionSentryBurst(local, cluster, now, sentryBurstStartAllowed,
                    sentryEngagementActive, sentryRetryReady, sentryBurstStartDebuffsClear,
                    sentryRelevanceDeficitStable,
                    currentFightRelevantSentries, sentryCoreTarget, sentryTargetCount, _channelingPylonActive))
                return;

            if (bossPreSpawn && currentFightSentryFillPending && sentryRetryReady
                && sentryRelevanceDeficitStable
                && sentryBurstStartDebuffsClear && s7o_ZDH_HelperState.AutoSentry
                && local.Guardian && SentryAvailable(local.Sentry))
            {
                if (TryStartSentry(local, cluster, now, false, true))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
            }
            if (bossPreSpawn) return;

            string primaryPreemptKind = mfdRetryYieldToSentry ? string.Empty
                : missingIceblinkElites > 0 ? "Iceblink"
                : urgentMfdBeforeSentry ? "MFD"
                : preemptIceblinkElites > 0 ? "Iceblink" : string.Empty;
            int primaryQuietRequiredMs = string.IsNullOrEmpty(primaryPreemptKind) ? 0
                : bossStandalone ? BossSupportPrimaryQuietMs
                : highFrequencyMode ? CombatSupportPrimaryQuietMs : SpeedSupportPrimaryQuietMs;
            int primaryQuietAgeMs = s7o_DHStrafePrimaryPlugin.PrimaryQuietAgeForZdh(now);
            if (!string.IsNullOrEmpty(primaryPreemptKind) && strafeMacroRunning)
                SuppressDhStrafePrimary(PrimaryPreemptLeaseMs);

            bool urgentRetryActive = _urgentRetryKind != CastKind.None
                && Elapsed(_urgentRetryTick, now) <= Math.Max(500, UrgentRetryLifetimeMs);
            if ((_urgentRetryKind == CastKind.Multishot && actionableIceblinkElites == 0)
                || (_urgentRetryKind == CastKind.MarkedForDeath && !missingMfdCoverage)
                || !urgentRetryActive)
            {
                _urgentRetryKind = CastKind.None;
                _urgentRetryTick = int.MinValue;
                urgentRetryActive = false;
            }

            int movementWindow = bossStandalone ? BossMovementWindowMs
                : highFrequencyMode ? AttackMovementWindowMs : MovementModeMovementWindowMs;
            if (initialDebuffBurst || sentryFillReady || mfdRetryYieldToSentry
                || multishotSweepReady || combatTrashSetupChain)
            {
                movementWindow = 0;
            }
            else if (missingIceblinkElites > 0)
            {
                movementWindow = bossStandalone ? BossIceblinkMovementWindowMs
                    : highFrequencyMode ? AttackIceblinkMovementWindowMs : MovementIceblinkMovementWindowMs;
            }
            else if (missingMfdCoverage)
            {
                movementWindow = bossStandalone ? BossMfdMovementWindowMs
                    : highFrequencyMode ? AttackMfdMovementWindowMs : MovementMfdMovementWindowMs;
            }
            else if (bossEntangleMissing)
            {
                movementWindow = BossMovementWindowMs;
            }
            else if (actionableIceblinkElites > 0 || trashIceblinkQueueDue || sentryIceblinkPreempt)
            {
                movementWindow = bossStandalone ? BossIceblinkMovementWindowMs
                    : highFrequencyMode ? AttackIceblinkMovementWindowMs : MovementIceblinkMovementWindowMs;
            }
            else if (sentryTimingWorkActive && protectedSentryCoverageMissing)
            {
                movementWindow = bossStandalone ? BossMovementWindowMs
                    : highFrequencyMode ? AttackSentryMovementWindowMs : MovementSentryMovementWindowMs;
            }

            int movementElapsed = _lastPauseReleasedTick == int.MinValue ? int.MaxValue : Elapsed(_lastPauseReleasedTick, now);
            int movementRemaining = movementElapsed == int.MaxValue ? 0 : Math.Max(0, movementWindow - movementElapsed);
            if (movementRemaining > 0) return;

            if (!string.IsNullOrEmpty(primaryPreemptKind)
                && primaryQuietAgeMs != int.MaxValue
                && primaryQuietAgeMs < Math.Max(0, primaryQuietRequiredMs))
            {
                return;
            }

            int normalCastGap = bossStandalone ? BossStandaloneCastGapMs
                : highFrequencyMode ? GlobalCastGapMs : MovementModeCastGapMs;
            int castGap = initialDebuffBurst || sentryFillReady || mfdRetryYieldToSentry
                    || multishotSweepReady || combatTrashSetupChain
                ? Math.Max(100, InitialSetupBurstGapMs)
                : missingIceblinkElites > 0 || urgentMfdBeforeSentry || bossEntangleMissing
                    || actionableIceblinkElites > 0 || trashIceblinkQueueDue || sentryIceblinkPreempt
                    ? bossStandalone ? BossUrgentRetryGapMs
                        : highFrequencyMode ? UrgentRetryGapMs : MovementUrgentRetryGapMs
                    : sentryTimingWorkActive
                        ? SentryRecastMs : normalCastGap;
            if (_lastCastFinishedTick != int.MinValue && Elapsed(_lastCastFinishedTick, now) < castGap) return;


            if (trashFightActive && !_trashInitialMultishotDone && trashIceblinkDue > 0
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && local.Multishot != null && TryStartMultishot(local, cluster, now, false, true))
            {
                _cast.TrashInitialMultishot = true;
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if (trashFightActive && trashInitialMultishotReady
                && currentFightSentryFillPending && trashMfdCoverageMissing
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null)
            {
                if (s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                    && local.MarkedForDeath != null
                    && TryStartMarkedForDeath(local, cluster, now, true, false))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
                if (TryStartSentryDuringMfdRetry(local, cluster, now, mfdRetryYieldToSentry)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            if (missingIceblinkElites > 0)
            {
                if (initialDebuffBurst)
                {
                    if (s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley && local.MarkedForDeath != null
                        && TryStartMarkedForDeath(local, cluster, now, true, false))
                    {
                        return;
                    }
                    if (_supportPrimaryGateBlocked) return;

                }

                if (s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill && local.Multishot != null
                    && TryStartMultishot(local, cluster, now, true))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

                if (TryStartSentryDuringMfdRetry(local, cluster, now, mfdRetryYieldToSentry)) return;
                if (_supportPrimaryGateBlocked) return;
                if (urgentMfdBeforeSentry
                    && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                    && local.MarkedForDeath != null && TryStartMarkedForDeath(local, cluster, now, true, false))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

                if (yieldIceblinkRetryToSentry
                    && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null
                    && TryStartSentry(local, cluster, now, false, true))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

                return;
            }

            if (TryStartSentryDuringMfdRetry(local, cluster, now, mfdRetryYieldToSentry)) return;
            if (_supportPrimaryGateBlocked) return;
            if (urgentMfdBeforeSentry
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley && local.MarkedForDeath != null
                && TryStartMarkedForDeath(local, cluster, now, true, false))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if (urgentMfdBeforeSentry)
            {
                return;
            }

            if (fillInterleaveMultishotTurn
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && local.Multishot != null
                && TryStartMultishot(local, cluster, now, false, false, false, true))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if (fillSentryTurn
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null)
            {
                if (TryStartSentry(local, cluster, now, false, true))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
            }

            if (bossStandalone && s7o_ZDH_HelperState.AutoEntangle && local.Odyssey && local.Entangle != null
                && TryStartEntangle(local, cluster, now, true))
            {
                return;
            }

            if (actionableIceblinkElites > 0
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill && local.Multishot != null
                && TryStartMultishot(local, cluster, now, true))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if ((!trashFightActive || trashIceblinkQueueDue || trashSentryRefreshPreempt)
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill && local.Multishot != null
                && TryStartMultishot(local, cluster, now, false, trashFightActive, sentryIceblinkPreempt))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            bool sentryPlannerTried = false;
            if ((bossStandalone || currentFightSentryFillComplete) && protectedSentryWorkReady
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null)
            {
                sentryPlannerTried = true;
                if (TryStartSentry(local, cluster, now, true, false))
                {
                    return;
                }
                else if (_supportPrimaryGateBlocked)
                {
                    return;
                }
            }

            if (bossStandalone && s7o_ZDH_HelperState.AutoEntangle && local.Odyssey && local.Entangle != null
                && TryStartEntangle(local, cluster, now, false))
            {
                return;
            }

            if (bossStandalone && !sentryPlannerTried && sentrySetupActive && sentryRetryReady
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null)
            {
                if (TryStartSentry(local, cluster, now, false, false))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked)
                {
                    return;
                }
            }

            if ((!currentFightSentryFillPending || bossStandalone)
                && (!trashFightActive || trashInitialMultishotReady)
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley && local.MarkedForDeath != null
                && TryStartMarkedForDeath(local, cluster, now, false, false))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if (bossStandalone
                && sentrySetupActive
                && sentryRetryReady
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null
                && TryStartSentry(local, cluster, now, true))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            if (!bossStandalone && s7o_ZDH_HelperState.AutoEntangle && local.Odyssey && local.Entangle != null
                && TryStartEntangle(local, cluster, now, true))
            {
                return;
            }

        }


        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip || !s7o_ZDH_HelperState.Enabled || !ContextAvailable()) return;
            int now = Environment.TickCount;
            List<ZdhLoadout> zdhPlayers = GetPartyZdhLoadouts();
            ZdhLoadout zdh = zdhPlayers.FirstOrDefault(x => x.QualifiesForDisplay);
            if (zdh != null && zdh.Player.InCombat && s7o_ZDH_HelperState.ShowEliteDebuffs)
            {
                foreach (IMonster monster in Hud.Game.AliveMonsters)
                {
                    if (!IsStatusTarget(monster) || IsJuggernaut(monster) || monster.Invulnerable || !monster.Attackable) continue;
                    if (!monster.IsOnScreen || Distance(zdh.Player, monster) > Math.Min(VisualRange, ZdhParticipationRange)) continue;
                    if (!IsEngaged(GetTargetState(monster, now), now)) continue;
                    DrawDebuffTokens(monster, HasIceblink(monster), HasEntangle(monster), monster.MarkedForDeath);
                }
            }

            foreach (ZdhLoadout loadout in zdhPlayers)
                DrawPortraitHint(loadout);
        }

        private void ClearCursorSafetyBlock()
        {
            bool wasBlocked = _cursorSafetyBlocked;
            _cursorSafetyBlocked = false;
            _cursorSafetyBlockedTick = int.MinValue;
            _cursorSafetyRestoreX = 0;
            _cursorSafetyRestoreY = 0;
            s7o_ZDH_HelperMetrics.CursorSafetyBlocked = false;
            if (wasBlocked)
            {
                _lastPauseReleasedTick = Environment.TickCount;
                ReleaseDhStrafePause();
            }
        }

        private void SetCursorSafetyBlock(int now, int restoreX, int restoreY)
        {
            _cursorSafetyBlocked = true;
            _cursorSafetyBlockedTick = now;
            _cursorSafetyRestoreX = restoreX;
            _cursorSafetyRestoreY = restoreY;
            s7o_ZDH_HelperMetrics.CursorSafetyBlocked = true;
            RequestDhStrafePause(Math.Max(120, CursorSafetyRecoveryMs + 120));
        }

        private void TryRecoverCursorSafety(int now)
        {
            if (!_cursorSafetyBlocked) return;
            int blockedMs = Elapsed(_cursorSafetyBlockedTick, now);
            if (_cast.Stage != CastStage.Idle) return;

            bool pointValid = PointInsideWindow(_cursorSafetyRestoreX, _cursorSafetyRestoreY);
            if (Hud.Window.IsForeground && pointValid)
            {
                bool restored = SetCursorClient(_cursorSafetyRestoreX, _cursorSafetyRestoreY)
                    && IsCursorNear(_cursorSafetyRestoreX, _cursorSafetyRestoreY, CursorRestoreTolerancePixels);
                if (restored)
                {
                    s7o_ZDH_HelperMetrics.LastRestoreConfirmed = true;
                    ClearCursorSafetyBlock();
                    return;
                }
            }

            if (blockedMs < Math.Max(100, CursorSafetyRecoveryMs))
            {
                RequestDhStrafePause(100);
                return;
            }

            ClearCursorSafetyBlock();
        }

        private void UpdateLocalTravelState(IPlayer player, int now)
        {
            if (player == null || player.FloorCoordinate == null) return;
            float x = player.FloorCoordinate.X;
            float y = player.FloorCoordinate.Y;
            if (_localTravelSampleTick == int.MinValue)
            {
                _localTravelX = x;
                _localTravelY = y;
                _advanceAnchorX = x;
                _advanceAnchorY = y;
                _localTravelSampleTick = now;
                _advanceSettledTick = now;
                _advanceProgressTick = now;
                _localTravelSpeed = 0;
                _advanceDistance = 0;
                _previousAdvanceDistance = 0;
                return;
            }

            int elapsed = Elapsed(_localTravelSampleTick, now);
            if (elapsed < Math.Max(100, TravelSampleMs)) return;

            float dx = x - _localTravelX;
            float dy = y - _localTravelY;
            float sampleDistance = (float)Math.Sqrt(dx * dx + dy * dy);
            _localTravelSpeed = sampleDistance * 1000f / Math.Max(1, elapsed);
            _localTravelX = x;
            _localTravelY = y;
            _localTravelSampleTick = now;

            float anchorDx = x - _advanceAnchorX;
            float anchorDy = y - _advanceAnchorY;
            _advanceDistance = (float)Math.Sqrt(anchorDx * anchorDx + anchorDy * anchorDy);
            bool movingOutward = _advanceDistance > _previousAdvanceDistance + 0.75f;
            if (movingOutward) _advanceProgressTick = now;
            _previousAdvanceDistance = _advanceDistance;

            if (_advanceSuppressed && _advanceProgressTick != int.MinValue
                && Elapsed(_advanceProgressTick, now) >= MobilityAdvanceProgressHoldMs)
            {
                _advanceAnchorX = x;
                _advanceAnchorY = y;
                _advanceDistance = 0;
                _previousAdvanceDistance = 0;
                _advanceProgressTick = now;
                _advanceSuppressed = false;
            }

            if (_localTravelSpeed <= MobilityAdvanceResetSpeed)
            {
                if (_advanceSettledTick == int.MinValue) _advanceSettledTick = now;
                if (Elapsed(_advanceSettledTick, now) >= MobilityAdvanceSettleMs)
                {
                    _advanceAnchorX = x;
                    _advanceAnchorY = y;
                    _advanceDistance = 0;
                    _previousAdvanceDistance = 0;
                    _advanceProgressTick = now;
                    _advanceSuppressed = false;
                }
            }
            else
            {
                _advanceSettledTick = int.MinValue;
                if (_advanceDistance >= MobilityAdvanceDistance && movingOutward)
                    _advanceSuppressed = true;
            }

            if (_localTravelSpeed >= TravelSpeedThreshold)
                _localTravelUntilTick = unchecked(now + Math.Max(100, TravelHoldMs));

            s7o_ZDH_HelperMetrics.LastLocalTravelSpeed = _localTravelSpeed;
            s7o_ZDH_HelperMetrics.LastAdvanceDistance = _advanceDistance;
            s7o_ZDH_HelperMetrics.LastAdvanceSuppressed = _advanceSuppressed;
        }

        private bool UpdateSpeedCombatIntent(IPlayer player, CombatCluster cluster, int now,
            bool speedMode, bool combatEvidence, float clusterDistance)
        {
            if (!speedMode || !combatEvidence || player == null || player.FloorCoordinate == null
                || cluster == null || clusterDistance > TravelEngagedClusterRange)
            {
                ResetSpeedCombatIntent(!speedMode ? "inactive" : !combatEvidence ? "no combat evidence" : "cluster out of range");
                return false;
            }

            float x = player.FloorCoordinate.X;
            float y = player.FloorCoordinate.Y;
            bool newCandidate = !_speedCombatCandidateValid
                || Distance2D(_speedCombatAnchorX, _speedCombatAnchorY, cluster.CenterX, cluster.CenterY)
                    > SpeedCombatAnchorResetDistance;
            if (newCandidate)
            {
                _speedCombatCandidateValid = true;
                _speedCombatCandidateTick = now;
                _speedCombatSampleTick = now;
                _speedCombatStartX = _speedCombatLastX = x;
                _speedCombatStartY = _speedCombatLastY = y;
                _speedCombatPathDistance = 0;
                _speedCombatAnchorX = cluster.CenterX;
                _speedCombatAnchorY = cluster.CenterY;
                _speedCombatMinClusterDistance = clusterDistance;
                _speedCombatEngaged = false;
                _speedCombatLeavingTick = int.MinValue;
            }

            _speedCombatMinClusterDistance = Math.Min(_speedCombatMinClusterDistance, clusterDistance);
            if (Elapsed(_speedCombatSampleTick, now) >= Math.Max(50, SpeedCombatSampleMs))
            {
                _speedCombatPathDistance += Distance2D(_speedCombatLastX, _speedCombatLastY, x, y);
                _speedCombatLastX = x;
                _speedCombatLastY = y;
                _speedCombatSampleTick = now;
            }

            int age = Elapsed(_speedCombatCandidateTick, now);
            float netDistance = Distance2D(_speedCombatStartX, _speedCombatStartY, x, y);
            float straightness = _speedCombatPathDistance <= 0.5f
                ? 1f : Math.Min(1f, netDistance / _speedCombatPathDistance);
            bool stationary = netDistance <= SpeedCombatMaxStationaryNetDistance
                && _speedCombatPathDistance <= SpeedCombatMaxStationaryPathDistance
                && _localTravelSpeed <= SpeedCombatMaxStationarySpeed;
            bool orbiting = _speedCombatPathDistance >= SpeedCombatMinOrbitPathDistance
                && straightness <= SpeedCombatMaxStraightness;
            bool initialLeaving = clusterDistance
                > _speedCombatMinClusterDistance + Math.Max(0f, SpeedCombatLeavingDistance);

            if (!_speedCombatEngaged)
            {
                if (age >= Math.Max(0, SpeedCombatDwellMs)
                    && cluster.Stable && !initialLeaving && (stationary || orbiting))
                {
                    _speedCombatEngaged = true;
                    _speedCombatLeavingTick = int.MinValue;
                }
            }
            else
            {
                float engagedLeavingDistance = Math.Min(TravelEngagedClusterRange,
                    Math.Max(Math.Max(0f, SpeedCombatEngagedRange),
                        _speedCombatMinClusterDistance + Math.Max(0f, SpeedCombatLeavingDistance)));
                bool leaving = clusterDistance > engagedLeavingDistance;
                if (leaving)
                {
                    if (_speedCombatLeavingTick == int.MinValue)
                        _speedCombatLeavingTick = now;
                    if (Elapsed(_speedCombatLeavingTick, now) >= Math.Max(0, SpeedCombatDisengageMs))
                    {
                        ResetSpeedCombatIntent("leaving");
                        return false;
                    }
                }
                else
                {
                    _speedCombatLeavingTick = int.MinValue;
                }
            }

            int exitAge = _speedCombatLeavingTick == int.MinValue
                ? 0 : Elapsed(_speedCombatLeavingTick, now);
            s7o_ZDH_HelperMetrics.LastSpeedCombatCandidate = true;
            s7o_ZDH_HelperMetrics.LastSpeedCombatEngaged = _speedCombatEngaged;
            s7o_ZDH_HelperMetrics.LastSpeedCombatAgeMs = age;
            s7o_ZDH_HelperMetrics.LastSpeedCombatPathDistance = _speedCombatPathDistance;
            s7o_ZDH_HelperMetrics.LastSpeedCombatNetDistance = netDistance;
            s7o_ZDH_HelperMetrics.LastSpeedCombatStraightness = straightness;
            s7o_ZDH_HelperMetrics.LastSpeedCombatExitAgeMs = exitAge;
            s7o_ZDH_HelperMetrics.LastSpeedCombatReason = _speedCombatEngaged
                ? exitAge > 0 ? "leaving hold" : stationary ? "stationary" : orbiting ? "orbiting" : "engaged"
                : initialLeaving ? "leaving"
                : age < SpeedCombatDwellMs ? "dwell"
                : !cluster.Stable ? "cluster settling" : "travel pattern";
            return _speedCombatEngaged;
        }

        private void ResetSpeedCombatIntent(string reason)
        {
            _speedCombatCandidateValid = false;
            _speedCombatCandidateTick = int.MinValue;
            _speedCombatSampleTick = int.MinValue;
            _speedCombatPathDistance = 0;
            _speedCombatStartX = _speedCombatStartY = 0;
            _speedCombatLastX = _speedCombatLastY = 0;
            _speedCombatAnchorX = _speedCombatAnchorY = 0;
            _speedCombatMinClusterDistance = 0;
            _speedCombatEngaged = false;
            _speedCombatLeavingTick = int.MinValue;
            s7o_ZDH_HelperMetrics.LastSpeedCombatCandidate = false;
            s7o_ZDH_HelperMetrics.LastSpeedCombatEngaged = false;
            s7o_ZDH_HelperMetrics.LastSpeedCombatAgeMs = 0;
            s7o_ZDH_HelperMetrics.LastSpeedCombatPathDistance = 0;
            s7o_ZDH_HelperMetrics.LastSpeedCombatNetDistance = 0;
            s7o_ZDH_HelperMetrics.LastSpeedCombatStraightness = 1;
            s7o_ZDH_HelperMetrics.LastSpeedCombatExitAgeMs = 0;
            s7o_ZDH_HelperMetrics.LastSpeedCombatReason = reason ?? string.Empty;
        }

        private bool HasEngagedPrimaryEliteHere(CombatCluster cluster, float clusterDistance)
        {
            return cluster != null && cluster.PriorityEliteCount > 0
                && clusterDistance <= Math.Max(TravelEngagedClusterRange, EliteEncounterRange);
        }

        private bool ShouldSuppressForTravel(CombatCluster cluster, float clusterDistance, int now)
        {
            if (_advanceSuppressed) return true;
            if (_localTravelUntilTick == int.MinValue || Reached(now, _localTravelUntilTick)) return false;
            bool engagedHere = cluster != null && cluster.Stable
                && cluster.RecentDamageCount >= TrashClusterMinDamagedBodies
                && clusterDistance <= TravelEngagedClusterRange
                && _advanceDistance < MobilityAdvanceDistance;
            bool focusedHere = cluster != null && cluster.FocusTarget != null
                && (cluster.SustainedSpecialFocus || IsCurrentPartyFocus(cluster.FocusTarget, now))
                && _advanceDistance < MobilityAdvanceDistance;
            return !engagedHere && !focusedHere;
        }

        private void UpdateBossEncounterState(int now)
        {
            IMonster boss = Hud.Game.AliveMonsters.FirstOrDefault(m => m != null
                && m.Rarity == ActorRarity.Boss && m.IsAlive);
            if (boss != null)
            {
                _trackedBossAcd = boss.AcdId;
                _bossMissingTick = int.MinValue;
                return;
            }

            if (_trackedBossAcd == 0) return;
            if (_bossMissingTick == int.MinValue)
            {
                _bossMissingTick = now;
                return;
            }
            if (Elapsed(_bossMissingTick, now) < 125) return;

            _trackedBossAcd = 0;
            _bossMissingTick = int.MinValue;
            DisengageAfterBossDeath(now);
        }

        private void DisengageAfterBossDeath(int now)
        {
            ForceAbortSentryBurst("boss dead", now);
            if (_cast.Stage != CastStage.Idle) CancelCast("boss dead");
            ReleaseBossEntangleStandstill();
            ClearCursorSafetyBlock();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            ClearSentryRetry();
            _urgentRetryKind = CastKind.None;
            _urgentRetryTick = int.MinValue;
            _lastPauseReleasedTick = int.MinValue;
            _bossStandaloneActive = false;
            _supportPrimaryGateBlocked = false;
            _targets.Clear();
            _packCandidateTick = int.MinValue;
            _packCandidateValid = false;
            ClearPartyFocus();
        }

        private IMonster FindBossSpawnAnchor(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return null;
            return (Hud.Game.AliveMonsters ?? Enumerable.Empty<IMonster>())
                .Where(m => m != null && m.Rarity == ActorRarity.Boss && m.IsAlive
                    && m.FloorCoordinate != null
                    && Distance(player, m) <= AutomationRange)
                .OrderBy(m => Distance(player, m))
                .FirstOrDefault();
        }

        private bool CanUseBossPreSpawn(ZdhLoadout local, IMonster boss, int now)
        {
            if (local == null || local.Player == null || local.Player.FloorCoordinate == null
                || boss == null || boss.FloorCoordinate == null)
                return false;

            if (boss.Attackable && !boss.Invulnerable) return false;
            if (_localTravelSpeed > BossStandaloneMaxSpeed || _advanceSettledTick == int.MinValue
                || Elapsed(_advanceSettledTick, now) < BossStandaloneStableMs)
                return false;

            return CreatePlacement(boss.FloorCoordinate.X, boss.FloorCoordinate.Y,
                boss.FloorCoordinate.Z) != null;
        }

        private CombatCluster BuildBossPreSpawnCluster(IMonster boss)
        {
            if (boss == null || boss.FloorCoordinate == null) return null;
            var cluster = new CombatCluster
            {
                CenterX = boss.FloorCoordinate.X,
                CenterY = boss.FloorCoordinate.Y,
                CenterZ = boss.FloorCoordinate.Z,
                Stable = true,
                FocusTarget = boss,
                SustainedSpecialFocus = true,
                PriorityEliteCount = 1,
                RecentDamageCount = 0,
                Score = 1000000,
                AxisX = 1f,
                AxisY = 0f,
            };
            cluster.Bodies.Add(boss);
            cluster.Elites.Add(boss);
            return cluster;
        }

        private bool HasAuthoritativeValleyAtPoint(float x, float y, int now)
        {
            IActor actor = FindAuthoritativeValleyActor();
            if (actor != null && actor.FloorCoordinate != null)
                return actor.FloorCoordinate.XYDistanceTo(x, y) <= ValleyRadius;

            int dropoutMs = _lastValleyActorSeenTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastValleyActorSeenTick, now);
            return _lastValleyActorAcd != 0
                && dropoutMs <= Math.Max(0, MfdNativeDropoutGraceMs)
                && Distance2D(_lastValleyX, _lastValleyY, x, y) <= ValleyRadius;
        }

        private bool TryStartBossPreSpawnMarkedForDeath(
            ZdhLoadout local, IMonster boss, int now)
        {
            if (local == null || boss == null || boss.FloorCoordinate == null
                || local.MarkedForDeath == null || !local.Valley
                || !s7o_ZDH_HelperState.AutoMarkedForDeath
                || !SkillReady(local.MarkedForDeath))
                return false;

            float x = boss.FloorCoordinate.X;
            float y = boss.FloorCoordinate.Y;
            float z = boss.FloorCoordinate.Z;
            if (HasAuthoritativeValleyAtPoint(x, y, now)) return false;
            if (Elapsed(_lastMfdCastTick, now) < Math.Max(100, MarkedForDeathUrgentRecastMs))
                return false;

            Placement placement = CreatePlacement(x, y, z);
            if (placement == null) return false;
            if (!EnsureSupportPrimaryReady(CastKind.MarkedForDeath, false, now)) return false;

            if (!StartCast(CastKind.MarkedForDeath, local.MarkedForDeath, boss.AcdId,
                placement.Screen, now, "MFD Boss Spawn", x, y, null))
                return false;

            _cast.VerifyRequiredCount = 1;
            _cast.BaselineImportantApplied = 0;
            _cast.RequiredImportantApplied = 1;
            _cast.BaselineMfdActorAcd = _lastValleyActorAcd;
            _cast.BaselineMfdActorCreatedTick = _lastValleyActorCreatedTick;
            _cast.BaselineMfdGameTick = Hud.Game.CurrentGameTick;
            _lastMfdCastTick = now;
            ClearMfdImprovementCandidate();
            return true;
        }

        private bool CanUseBossStandalone(ZdhLoadout local, int now)
        {
            if (local == null || local.Player == null || local.Player.FloorCoordinate == null) return false;
            if (_localTravelSpeed > BossStandaloneMaxSpeed || _advanceSettledTick == int.MinValue
                || Elapsed(_advanceSettledTick, now) < BossStandaloneStableMs) return false;
            return FindStandaloneBoss(local.Player) != null;
        }

        private IMonster FindStandaloneBoss(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return null;
            IMonster selected = Hud.Game.SelectedMonster2;
            return Hud.Game.AliveMonsters.Where(m => m != null && m.Rarity == ActorRarity.Boss
                    && IsAutomationBody(m) && !m.Invulnerable && m.Attackable && m.IsOnScreen
                    && Distance(player, m) <= BossStandaloneRange)
                .OrderByDescending(m => selected != null && SameMonster(selected, m))
                .ThenBy(m => Distance(player, m))
                .FirstOrDefault();
        }

        private List<IMonster> GetBossCombatBodies(IPlayer player, int now)
        {
            IMonster boss = FindStandaloneBoss(player);
            if (boss == null) return new List<IMonster>();
            return Hud.Game.AliveMonsters.Where(m => IsAutomationBody(m)
                    && m.Rarity != ActorRarity.RareMinion
                    && !m.Invulnerable && m.Attackable && m.IsOnScreen && m.FloorCoordinate != null
                    && Distance(player, m) <= AutomationRange
                    && boss.FloorCoordinate.XYDistanceTo(m.FloorCoordinate) <= CombatBodyNearAnchorRadius + GetMonsterRadiusBottom(m))
                .ToList();
        }

        private CombatCluster BuildBossCombatCluster(List<IMonster> bodies, int now)
        {
            if (bodies == null || bodies.Count == 0) return null;
            IMonster boss = bodies.Where(m => m.Rarity == ActorRarity.Boss)
                .OrderBy(m => Distance(Hud.Game.Me, m)).FirstOrDefault();
            if (boss == null) return null;

            var cluster = new CombatCluster { FocusTarget = boss };
            foreach (IMonster body in bodies)
            {
                cluster.Bodies.Add(body);
                if (IsGroundSupportMfdOnlyTarget(body)) cluster.MfdOnlyTargets.Add(body);
                else if (IsGroundSupportPrimaryElite(body)) cluster.Elites.Add(body);
                if (IsEngaged(GetTargetState(body, now), now)) cluster.RecentDamageCount++;
                cluster.Score += CombatBodyWeight(body, now);
            }
            FinalizeCombatCluster(cluster, now);
            cluster.Stable = true;
            return cluster;
        }

        private float ClusterDistance(IPlayer player, CombatCluster cluster)
        {
            if (player == null || player.FloorCoordinate == null || cluster == null) return float.MaxValue;
            float dx = player.FloorCoordinate.X - cluster.CenterX;
            float dy = player.FloorCoordinate.Y - cluster.CenterY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private bool TryStartEntangle(ZdhLoadout local, CombatCluster cluster, int now, bool urgentOnly)
        {
            if (cluster == null || cluster.Bodies.Count == 0 || !SkillReady(local.Entangle)) return false;
            bool bossCluster = cluster.Elites.Any(m => m != null && m.Rarity == ActorRarity.Boss);
            List<IMonster> missingElites = cluster.Elites
                .Where(m => IsDebuffBody(m) && !HasEntangle(m)).ToList();
            if (cluster.SustainedSpecialFocus && cluster.FocusTarget != null && IsDebuffBody(cluster.FocusTarget)
                && !HasEntangle(cluster.FocusTarget) && !missingElites.Any(m => SameMonster(m, cluster.FocusTarget)))
                missingElites.Add(cluster.FocusTarget);
            bool urgent = missingElites.Count > 0;
            if (!bossCluster && !urgent) return false;
            if (urgentOnly && !urgent) return false;
            if (!urgentOnly && urgent) return false;

            int entangleAge = _lastEntangleMaintenanceTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastEntangleMaintenanceTick, now);
            bool packMaintenanceDue = bossCluster && entangleAge >= Math.Max(250, BossEntangleMaintenanceMs);
            if (!urgent && !packMaintenanceDue) return false;

            IMonster target = bossCluster
                ? FindBestEntangleTarget(cluster, urgent, now)
                : missingElites.OrderByDescending(m => EntangleTargetScore(m, cluster.Bodies))
                    .ThenBy(m => ScreenDistanceToCursor(m)).FirstOrDefault();
            if (target == null || !IsDebuffBody(target)) return false;
            TargetState state = GetTargetState(target, now);
            if (urgent && Elapsed(state.LastEntangleAttempt, now) < FailedCastRetryMs) return false;
            IScreenCoordinate aim = CreateSafeDirectionalAim(local.Player, target.ScreenCoordinate);
            if (aim == null || !StartCast(CastKind.Entangle, local.Entangle, target.AcdId, aim, now,
                urgent ? "Entangle Elite" : "Entangle Density")) return false;

            state.LastEntangleAttempt = now;
            _lastEntangleMaintenanceTick = now;
            return true;
        }

        private int GetIceblinkRefreshAgeMs()
        {
            return _bossStandaloneActive ? BossMultishotMaintenanceMs
                : _runtime.HighFrequencyMode ? AttackMultishotMaintenanceMs : MultishotMaintenanceMs;
        }

        private int GetMinimumIceblinkRemainingMs(IEnumerable<IMonster> monsters, int now)
        {
            if (!s7o_ZDH_HelperState.AutoMultishot || monsters == null)
                return int.MaxValue;

            int minimum = int.MaxValue;
            bool found = false;
            foreach (IMonster monster in monsters)
            {
                if (monster == null || !IsDebuffBody(monster)
                    || monster.Invulnerable || !monster.Attackable || !monster.IsOnScreen)
                    continue;

                found = true;
                if (!HasIceblink(monster)) return 0;

                TargetState state = GetTargetState(monster, now);
                if (state.IceblinkConfirmedTick == int.MinValue) return 0;

                int remaining = Math.Max(0,
                    IceblinkExpectedDurationMs - Elapsed(state.IceblinkConfirmedTick, now));
                if (remaining < minimum) minimum = remaining;
            }

            return found ? minimum : int.MaxValue;
        }

        private bool IsIceblinkDue(IMonster monster, int now)
        {
            if (monster == null || !HasIceblink(monster)) return true;
            TargetState state = GetTargetState(monster, now);
            return state.IceblinkConfirmedTick == int.MinValue
                || Elapsed(state.IceblinkConfirmedTick, now) >= GetIceblinkRefreshAgeMs();
        }

        private bool HasPendingIceblinkValidation(IMonster monster, int now)
        {
            if (monster == null || !HasIceblink(monster)) return false;
            TargetState state = GetTargetState(monster, now);
            return state.PendingIceblinkRefreshTick != int.MinValue
                && state.IceblinkConfirmedTick != int.MinValue
                && Elapsed(state.IceblinkConfirmedTick, now)
                    < Math.Max(1000, IceblinkExpectedDurationMs + IceblinkValidationSlackMs);
        }

        private bool IsIceblinkBackupActionable(IMonster monster, int now)
        {
            return false;
        }

        private bool IsIceblinkActionable(IMonster monster, int now)
        {
            if (monster == null || HasPendingMultishotValidation(monster.AcdId, now)) return false;
            if (!IsIceblinkDue(monster, now)) return false;
            if (HasPendingIceblinkValidation(monster, now)) return IsIceblinkBackupActionable(monster, now);
            TargetState state = GetTargetState(monster, now);
            int retryMs = HasIceblink(monster) ? MultishotRefreshRetryMs : MultishotFailedRetryMs;
            return Elapsed(state.LastMultishotAttempt, now) >= Math.Max(100, retryMs);
        }

        private bool IsIceblinkPrimaryPreemptDue(IMonster monster, int now)
        {
            if (monster == null || !IsDebuffBody(monster)
                || HasPendingMultishotValidation(monster.AcdId, now)) return false;
            if (!HasIceblink(monster)) return true;
            if (HasPendingIceblinkValidation(monster, now)) return false;
            TargetState state = GetTargetState(monster, now);
            if (state.IceblinkConfirmedTick == int.MinValue) return true;
            int preemptAge = Math.Max(0, GetIceblinkRefreshAgeMs() - Math.Max(0, IceblinkPrimaryPreemptLeadMs));
            return Elapsed(state.IceblinkConfirmedTick, now) >= preemptAge;
        }

        private bool HasMultishotFillTargets(ZdhLoadout local, CombatCluster cluster, int now)
        {
            if (!s7o_ZDH_HelperState.AutoMultishot || local == null || local.Multishot == null
                || !local.Iceblink || !local.WindChill || cluster == null) return false;
            return cluster.Bodies.Any(monster => monster != null && IsDebuffBody(monster)
                && !monster.Invulnerable && monster.Attackable && monster.IsOnScreen);
        }

        private bool TryStartMultishot(ZdhLoadout local, CombatCluster cluster, int now,
            bool urgentOnly, bool trashDensityTimer = false, bool allowEarlyMaintenance = false,
            bool sentryFillInterleave = false)
        {
            if (cluster == null || cluster.Bodies.Count == 0 || !SkillReady(local.Multishot)) return false;

            List<IMonster> primaryElites = MergeMonsters(cluster.Elites.Where(IsDebuffBody), GetActivePrimaryElites(local.Player, now));
            List<IMonster> eligible = MergeMonsters(cluster.Bodies.Where(IsDebuffBody), primaryElites.Where(IsDebuffBody));
            if (eligible.Count == 0) return false;

            bool densityTimer = trashDensityTimer && primaryElites.Count == 0;
            bool combatIntentTrash = primaryElites.Count == 0 && IsCombatIntentTrash(cluster);
            List<IMonster> missingPrimary = primaryElites.Where(m => !HasIceblink(m)).ToList();
            IEnumerable<IMonster> dueCandidates = densityTimer
                ? eligible
                : urgentOnly && missingPrimary.Count > 0
                    ? missingPrimary
                    : primaryElites.Count > 0
                        ? primaryElites.Where(m => IsIceblinkDue(m, now))
                        : eligible.Where(m => IsIceblinkDue(m, now));
            var dueAcds = new HashSet<uint>(dueCandidates.Select(m => m.AcdId));

            List<IMonster> dueImportant = primaryElites.Where(m => dueAcds.Contains(m.AcdId)).ToList();
            bool urgent = !densityTimer && dueImportant.Count > 0;
            if (urgentOnly && !urgent) return false;
            if (!urgentOnly && urgent && !sentryFillInterleave) return false;

            int maintenanceMs = GetIceblinkRefreshAgeMs();
            int maintenanceAge = _lastMultishotMaintenanceTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastMultishotMaintenanceTick, now);
            int maintenanceThreshold = allowEarlyMaintenance
                ? Math.Max(0, maintenanceMs - Math.Max(0, IceblinkPrimaryPreemptLeadMs))
                : maintenanceMs;
            bool maintenance = sentryFillInterleave || (densityTimer
                ? (!_trashInitialMultishotDone || maintenanceAge >= maintenanceThreshold)
                : dueAcds.Count == 0 && maintenanceAge >= maintenanceThreshold);
            bool efficientWindow = !densityTimer && !urgent && dueAcds.Count == 0 && cluster.Stable
                && maintenanceAge >= Math.Max(0, maintenanceMs - Math.Max(0, EfficientMultishotLeadMs));
            if (!urgent && !maintenance && !efficientWindow) return false;
            if (!urgent && !densityTimer && !cluster.Stable) return false;
            if (densityTimer && !cluster.Stable && !cluster.TrashLatched) return false;

            var actionableDueAcds = densityTimer
                ? dueAcds
                : new HashSet<uint>(dueAcds.Where(acd =>
                {
                    IMonster target = FindMonster(acd);
                    return target != null && IsIceblinkActionable(target, now);
                }));
            if (urgent && !primaryElites.Any(m => actionableDueAcds.Contains(m.AcdId))) return false;

            bool continuingSweep = _multishotSweepRemaining > 0
                && _multishotSweepUntilTick != int.MinValue
                && !Reached(now, _multishotSweepUntilTick);
            MultishotPlan plan = BuildMultishotPlan(local.Player, eligible,
                actionableDueAcds.Count > 0 ? actionableDueAcds : dueAcds, now);
            if (plan == null || plan.Primary == null || plan.Aim == null) return false;
            HashSet<uint> plannedDueAcds = actionableDueAcds.Count > 0 ? actionableDueAcds : dueAcds;
            int uncoveredDueImportant = primaryElites.Count(m => plannedDueAcds.Contains(m.AcdId)
                && !plan.CoveredMissingEliteAcds.Contains(m.AcdId));
            int minimumTrashCoverage = combatIntentTrash ? 1 : Math.Min(3, TrashClusterMinBodies);
            if (primaryElites.Count == 0 && dueAcds.Count > 0
                && plan.CoveredMissingAcds.Count < minimumTrashCoverage) return false;
            bool efficientCast = efficientWindow && !maintenance;
            if (efficientCast)
            {
                int requiredCoverage = Math.Max(3, (int)Math.Ceiling(
                    eligible.Count * Math.Max(0.50f, Math.Min(1.0f, EfficientMultishotCoverageRatio))));
                if (plan.CoveredBodyCount < requiredCoverage) return false;
            }

            TargetState state = GetTargetState(plan.Primary, now);
            if (!densityTimer && !maintenance && !efficientCast && !IsIceblinkActionable(plan.Primary, now)) return false;
            if (!EnsureSupportPrimaryReady(CastKind.Multishot, false, now)) return false;
            if (!StartCast(CastKind.Multishot, local.Multishot, plan.Primary.AcdId, plan.Aim, now,
                continuingSweep ? "Multishot Elite Sweep"
                    : sentryFillInterleave ? "Multishot Sentry Interleave"
                    : plan.CoveredEliteCount > 0 ? "Multishot Elite Cone" : "Multishot Density",
                float.NaN, float.NaN, plan.CoveredMissingAcds)) return false;
            _cast.EfficientMultishot = efficientCast;
            if (continuingSweep)
            {
                _multishotSweepRemaining = Math.Max(0, _multishotSweepRemaining - 1);
                if (_multishotSweepRemaining == 0) _multishotSweepUntilTick = int.MinValue;
            }
            else if (urgent && !sentryFillInterleave && uncoveredDueImportant > 0)
            {
                _multishotSweepRemaining = Math.Max(0, MultishotSweepMaxShots - 1);
                _multishotSweepUntilTick = unchecked(now + Math.Max(900, Math.Min(1800, UrgentRetryLifetimeMs)));
            }

            _cast.VerifyRequiredCount = plan.RequiredApplied;
            _cast.VerifyPrimaryRequired = plan.PrimaryMustApply;
            foreach (uint acd in plan.CoveredEliteAcds) _cast.VerifyImportantAcds.Add(acd);
            foreach (uint acd in plan.CoveredPrimaryEliteAcds)
            {
                _cast.MultishotCoveredEliteAcds.Add(acd);
                IMonster coveredElite = FindMonster(acd);
                if (coveredElite != null && HasIceblink(coveredElite))
                    _cast.MultishotBaselineActiveAcds.Add(acd);
            }
            foreach (uint acd in plan.CoveredMissingAcds.Concat(plan.CoveredPrimaryEliteAcds).Distinct())
            {
                IMonster attempted = FindMonster(acd);
                if (attempted != null) GetTargetState(attempted, now).LastMultishotAttempt = now;
            }
            state.LastMultishotAttempt = now;
            _lastMultishotMaintenanceTick = now;
            return true;
        }

        private bool HasPendingMultishotValidation(uint acd, int now)
        {
            if (acd == 0) return false;
            return _pendingMultishots.Any(p => p != null && !Reached(now, p.UntilTick)
                && p.PendingAcds.Contains(acd));
        }

        private void QueuePendingMultishotValidation(int now)
        {
            int inputTick = _cast.InputDownTick == int.MinValue ? now : _cast.InputDownTick;
            var pending = new PendingMultishotValidation
            {
                InputTick = inputTick,
                UntilTick = unchecked(inputTick + Math.Max(1, _cast.VerifyMs)),
                TargetAcd = _cast.TargetAcd,
                TrashInitial = _cast.TrashInitialMultishot,
            };

            foreach (uint acd in _cast.VerifyTargetAcds) pending.PendingAcds.Add(acd);
            foreach (uint acd in _cast.MultishotCoveredEliteAcds) pending.PendingAcds.Add(acd);
            foreach (uint acd in _cast.MultishotBaselineActiveAcds) pending.BaselineActiveAcds.Add(acd);
            foreach (uint acd in _cast.VerifyImportantAcds) pending.ImportantAcds.Add(acd);
            if (pending.PendingAcds.Count == 0 && pending.TargetAcd != 0)
                pending.PendingAcds.Add(pending.TargetAcd);

            if (_cast.SawNativeMultishotAnimation)
            {
                pending.AnimationSeen = true;
                pending.AnimationTick = now;
                if (Hud != null && Hud.Game != null && Hud.Game.Me != null)
                {
                    _lastObservedPlayerAnimation = Hud.Game.Me.Animation;
                    _lastObservedPlayerAnimationValid = true;
                }
            }

            _pendingMultishots.RemoveAll(p => p != null
                && p.PendingAcds.Overlaps(pending.PendingAcds));
            if (pending.PendingAcds.Count > 0)
            {
                _pendingMultishots.Add(pending);
                if (pending.AnimationSeen)
                    PublishMultishotAsyncResult(pending.TargetAcd, "accepted", "native multishot animation",
                        Elapsed(pending.InputTick, pending.AnimationTick));
            }
            else
            {
                PublishMultishotAsyncResult(pending.TargetAcd, "dispatched", "no validation target", -1);
            }
            s7o_ZDH_HelperMetrics.LastMultishotPendingCount = _pendingMultishots.Count;
        }

        private void UpdatePendingMultishotValidations(int now)
        {
            ObserveNativeMultishotAnimation(now);
            if (_pendingMultishots.Count == 0)
            {
                s7o_ZDH_HelperMetrics.LastMultishotPendingCount = 0;
                return;
            }

            foreach (PendingMultishotValidation pending in _pendingMultishots.ToList())
            {
                List<uint> missingAtDispatch = pending.PendingAcds
                    .Where(acd => !pending.BaselineActiveAcds.Contains(acd)).ToList();
                List<uint> liveMissing = missingAtDispatch.Where(acd =>
                {
                    IMonster monster = FindMonster(acd);
                    return monster != null && IsDebuffBody(monster);
                }).ToList();
                int applied = liveMissing.Count(acd =>
                {
                    IMonster monster = FindMonster(acd);
                    return monster != null && HasIceblink(monster);
                });
                List<uint> unresolved = liveMissing.Where(acd =>
                {
                    IMonster monster = FindMonster(acd);
                    return monster != null && !HasIceblink(monster);
                }).ToList();

                if (missingAtDispatch.Count == 0)
                {
                    if (pending.AnimationSeen)
                    {
                        CommitPendingIceblinkRefresh(pending, now);
                        _pendingMultishots.Remove(pending);
                        if (_urgentRetryKind == CastKind.Multishot)
                        {
                            _urgentRetryKind = CastKind.None;
                            _urgentRetryTick = int.MinValue;
                        }
                        continue;
                    }

                    if (!Reached(now, pending.UntilTick)) continue;
                    PublishMultishotAsyncResult(pending.TargetAcd, "unverified", "no native animation", -1);
                    _pendingMultishots.Remove(pending);
                    if (pending.TrashInitial) _trashInitialMultishotDone = false;
                    if (pending.ImportantAcds.Count > 0)
                    {
                        _urgentRetryKind = CastKind.Multishot;
                        _urgentRetryTick = now;
                    }
                    continue;
                }

                if (liveMissing.Count == 0)
                {
                    PublishMultishotAsyncResult(pending.TargetAcd, "target gone", "no actionable target", -1);
                    _pendingMultishots.Remove(pending);
                    continue;
                }

                if (unresolved.Count == 0)
                {
                    CommitPendingIceblinkRefresh(pending, now);
                    int effectMs = Elapsed(pending.InputTick, now);
                    PublishMultishotAsyncResult(pending.TargetAcd, "verified", "debuff", effectMs);
                    _pendingMultishots.Remove(pending);
                    if (_urgentRetryKind == CastKind.Multishot)
                    {
                        _urgentRetryKind = CastKind.None;
                        _urgentRetryTick = int.MinValue;
                    }
                    continue;
                }

                if (!Reached(now, pending.UntilTick)) continue;

                if (pending.AnimationSeen || applied > 0) CommitPendingIceblinkRefresh(pending, now);
                string result = applied > 0 ? "partial" : "unverified";
                string source = pending.AnimationSeen
                    ? (applied > 0 ? "native animation / partial debuff" : "native animation / no debuff")
                    : (applied > 0 ? "partial debuff" : "no debuff");
                PublishMultishotAsyncResult(pending.TargetAcd, result, source,
                    applied > 0 ? Elapsed(pending.InputTick, now) : -1);
                _pendingMultishots.Remove(pending);
                if (pending.TrashInitial && applied == 0) _trashInitialMultishotDone = false;
                if (unresolved.Any(acd => pending.ImportantAcds.Contains(acd)))
                {
                    _urgentRetryKind = CastKind.Multishot;
                    _urgentRetryTick = now;
                }
            }
            s7o_ZDH_HelperMetrics.LastMultishotPendingCount = _pendingMultishots.Count;
        }

        private void ObserveNativeMultishotAnimation(int now)
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null) return;
            AnimSnoEnum animation = Hud.Game.Me.Animation;
            bool multishot = IsNativeMultishotAnimation(animation);
            bool newAnimation = multishot && (!_lastObservedPlayerAnimationValid
                || !IsNativeMultishotAnimation(_lastObservedPlayerAnimation)
                || _lastObservedPlayerAnimation != animation);
            _lastObservedPlayerAnimation = animation;
            _lastObservedPlayerAnimationValid = true;

            bool activeMultishot = _cast.Stage != CastStage.Idle && _cast.Kind == CastKind.Multishot;
            if (activeMultishot || !newAnimation || _pendingMultishots.Count == 0) return;

            PendingMultishotValidation pending = _pendingMultishots
                .Where(p => p != null && !p.AnimationSeen && !Reached(now, p.UntilTick)
                    && Elapsed(p.InputTick, now) <= MultishotNativeAnimationCorrelationMs)
                .OrderByDescending(p => p.InputTick).FirstOrDefault();
            if (pending == null) return;
            pending.AnimationSeen = true;
            pending.AnimationTick = now;
            PublishMultishotAsyncResult(pending.TargetAcd, "accepted", "native multishot animation",
                Elapsed(pending.InputTick, now));
        }

        private static bool IsNativeMultishotAnimation(AnimSnoEnum animation)
        {
            return animation == AnimSnoEnum._demonhunter_female_dw_xbow_multishot_01
                || animation == AnimSnoEnum._demonhunter_female_bow_multishot_01
                || animation == AnimSnoEnum._demonhunter_male_xbow_multishot
                || animation == AnimSnoEnum._demonhunter_male_dw_xbow_multishot
                || animation == AnimSnoEnum._demonhunter_male_1hxbow_multishot
                || animation == AnimSnoEnum._demonhunter_female_xbow_multishot_01
                || animation == AnimSnoEnum._demonhunter_male_bow_multishot
                || animation == AnimSnoEnum._demonhunter_female_1hxbow_multishot_01;
        }

        private void CommitPendingIceblinkRefresh(PendingMultishotValidation pending, int now)
        {
            foreach (uint acd in pending.BaselineActiveAcds)
            {
                IMonster elite = FindMonster(acd);
                if (elite == null || !HasIceblink(elite)) continue;
                TargetState state = GetTargetState(elite, now);
                if (state.PendingIceblinkRefreshTick == int.MinValue)
                {
                    state.PendingIceblinkRefreshTick = pending.InputTick;
                    state.PendingIceblinkAttemptCount = 1;
                }
                else
                {
                    state.PendingIceblinkAttemptCount = Math.Min(
                        Math.Max(1, IceblinkMaxRefreshAttempts), state.PendingIceblinkAttemptCount + 1);
                }
            }
        }

        private void PublishMultishotAsyncResult(uint targetAcd, string result, string source, int effectMs)
        {
            s7o_ZDH_HelperMetrics.LastMultishotAsyncTargetAcd = targetAcd;
            s7o_ZDH_HelperMetrics.LastMultishotAsyncResult = result ?? string.Empty;
            s7o_ZDH_HelperMetrics.LastMultishotAsyncSource = source ?? string.Empty;
            s7o_ZDH_HelperMetrics.LastMultishotAsyncLatencyMs = effectMs;
            s7o_ZDH_HelperMetrics.LastMultishotAsyncSequence++;
        }

        private void ClearPendingMultishotValidations()
        {
            _pendingMultishots.Clear();
            _lastObservedPlayerAnimationValid = false;
            s7o_ZDH_HelperMetrics.LastMultishotPendingCount = 0;
        }

        private void CompleteMultishotDispatch(int now)
        {
            QueuePendingMultishotValidation(now);
            if (_cast.TrashInitialMultishot) _trashInitialMultishotDone = true;
            s7o_ZDH_HelperMetrics.LastVerificationSource = "async released";
            s7o_ZDH_HelperMetrics.LastResult = "dispatched";
            s7o_ZDH_HelperMetrics.LastTimingSequence++;
            _lastSupportKind = CastKind.Multishot;
            _lastCastFinishedTick = now;
            ResetCast();
            CompleteBossEntangleStandstillRelease();
        }

        private bool TryStartMarkedForDeath(ZdhLoadout local, CombatCluster cluster, int now, bool urgentOnly, bool eliteGainOnly = false)
        {
            if (cluster == null || cluster.Bodies.Count == 0 || !SkillReady(local.MarkedForDeath)) return false;

            List<IMonster> allTargets = MergeMonsters(
                cluster.Bodies.Where(m => m != null && m.Rarity != ActorRarity.RareMinion),
                GetActiveMfdSupportTargets(local.Player, now));
            List<IMonster> primaryElites = allTargets.Where(IsGroundSupportPrimaryElite).ToList();
            List<IMonster> mfdOnlyTargets = allTargets.Where(IsGroundSupportMfdOnlyTarget).ToList();
            bool hasPrimaryElite = primaryElites.Count > 0;
            List<IMonster> planningTargets = hasPrimaryElite
                ? MergeMonsters(primaryElites, mfdOnlyTargets) : allTargets;
            s7o_ZDH_HelperMetrics.LastMfdOnlyTargets = mfdOnlyTargets.Count;
            if (!hasPrimaryElite && mfdOnlyTargets.Count == 0
                && !cluster.Stable && !cluster.TrashLatched && !cluster.SustainedSpecialFocus) return false;

            Placement trashSnapshot = !hasPrimaryElite && mfdOnlyTargets.Count == 0
                ? CreateTrashSnapshotPlacement(cluster, planningTargets, now) : null;
            Placement best = mfdOnlyTargets.Count > 0
                ? FindBestJuggernautAnchoredPlacement(mfdOnlyTargets, allTargets, now)
                : trashSnapshot ?? FindBestPlacement(planningTargets, now);
            if (best == null) return false;
            Placement current = CurrentValleyPlacement(planningTargets, now);
            int currentBodies = current == null ? 0 : current.CoveredBodies;
            int currentElites = current == null ? 0 : current.CoveredElites;
            double currentScore = current == null ? 0 : current.Score;
            int densityGain = best.CoveredBodies - currentBodies;

            var currentSupportAcds = new HashSet<uint>(current == null
                ? Enumerable.Empty<uint>() : current.CoveredEliteAcds);
            _runtime.MfdCoverageSetChanged = best.CoveredEliteAcds
                .Any(acd => !currentSupportAcds.Contains(acd));

            bool urgent;
            if (hasPrimaryElite)
            {
                bool noEliteCovered = current == null || currentElites == 0;
                bool eliteGain = _runtime.MfdCoverageSetChanged;
                bool gainStable = noEliteCovered || IsMfdImprovementStable(best, currentSupportAcds, now);
                urgent = noEliteCovered || (eliteGain && gainStable);


                if (eliteGainOnly && !(eliteGain && gainStable)) return false;
                if (urgentOnly && !urgent) return false;
                if (!urgentOnly && urgent) return false;

                if (!urgent)
                {
                    if (!eliteGain) ClearMfdImprovementCandidate();
                    return false;
                }

                int recast = noEliteCovered ? MarkedForDeathUrgentRecastMs : MfdEliteGainRecastMs;
                if (Elapsed(_lastMfdCastTick, now) < recast)
                {
                    return false;
                }
            }
            else
            {
                ClearMfdImprovementCandidate();
                int minimumDensityGain = Math.Max(MfdDensityMinimumGain,
                    (int)Math.Ceiling(Math.Max(1, currentBodies) * MfdDensityMinimumGainRatio));

                var mfdOnlyAcds = new HashSet<uint>(mfdOnlyTargets.Select(m => m.AcdId));
                int currentMfdOnly = current == null ? 0
                    : current.CoveredEliteAcds.Count(acd => mfdOnlyAcds.Contains(acd));
                int bestMfdOnly = best.CoveredEliteAcds.Count(acd => mfdOnlyAcds.Contains(acd));
                bool mfdOnlyGain = bestMfdOnly > currentMfdOnly;
                bool combatIntentTrash = mfdOnlyTargets.Count == 0 && IsCombatIntentTrash(cluster);
                bool stableTrashField = cluster.Stable || cluster.TrashLatched;
                bool snapshotDensity = trashSnapshot != null
                    && _trashFightLatchConfirmedBodies >= TrashClusterMinBodies
                    && _trashFightLatchConfirmedDamaged >= TrashClusterMinDamagedBodies;
                bool initialDensity = current == null
                    && stableTrashField
                    && ((best.CoveredBodies >= TrashClusterMinBodies
                            && cluster.RecentDamageCount >= TrashClusterMinDamagedBodies)
                        || snapshotDensity || bestMfdOnly > 0);
                bool initialCombatIntent = current == null
                    && combatIntentTrash && best != null && best.CoveredBodies > 0;
                bool combatIntentCoverageMissing = combatIntentTrash
                    && best != null && best.CoveredBodies > 0
                    && (current == null || current.CoveredBodies == 0);
                bool meaningfulDensity = current != null
                    && stableTrashField
                    && densityGain >= minimumDensityGain
                    && best.Score > currentScore * 1.12 + 1.0;
                urgent = initialDensity || initialCombatIntent || combatIntentCoverageMissing
                    || meaningfulDensity || mfdOnlyGain;


                if (eliteGainOnly) return false;
                if (urgentOnly && !urgent) return false;
                if (!urgentOnly && urgent) return false;
                int recast = urgent ? MarkedForDeathUrgentRecastMs : MarkedForDeathRecastMs;
                if (Elapsed(_lastMfdCastTick, now) < recast) return false;
                if (current != null && !mfdOnlyGain && !meaningfulDensity
                    && !combatIntentCoverageMissing)
                {
                    return false;
                }
                if (!urgent && current != null && best.Score <= currentScore * 1.18 + 1.0) return false;
            }

            var coveredEliteAcds = new HashSet<uint>(best.CoveredEliteAcds);
            IMonster primary = mfdOnlyTargets.Count > 0
                ? mfdOnlyTargets.FirstOrDefault(m => m != null && m.AcdId == best.TargetAcd)
                : null;
            if (primary == null)
            {
                primary = planningTargets.Where(m => IsGroundSupportElite(m) && coveredEliteAcds.Contains(m.AcdId))
                    .OrderByDescending(m => TargetPriority(m, true)).FirstOrDefault()
                    ?? planningTargets.Where(m => m != null && m.Rarity != ActorRarity.RareMinion
                        && IsInsideValley(m, best.WorldX, best.WorldY))
                        .OrderByDescending(m => MfdTargetWeight(m, now)).FirstOrDefault()
                    ?? (trashSnapshot != null
                        ? planningTargets.Where(m => m != null && m.FloorCoordinate != null)
                            .OrderBy(m => m.FloorCoordinate.XYDistanceTo(best.WorldX, best.WorldY))
                            .FirstOrDefault()
                        : null);
            }
            if (primary == null) return false;
            if (!EnsureSupportPrimaryReady(CastKind.MarkedForDeath, false, now)) return false;

            List<uint> verifyTargets = planningTargets.Where(m => m != null && !m.MarkedForDeath
                    && IsInsideValley(m, best.WorldX, best.WorldY))
                .Select(m => m.AcdId).ToList();
            string mfdLabel = mfdOnlyTargets.Count > 0 ? "MFD Juggernaut"
                : hasPrimaryElite ? "MFD Elite Density" : "MFD Trash Density";
            if (!StartCast(CastKind.MarkedForDeath, local.MarkedForDeath, primary.AcdId, best.Screen, now,
                mfdLabel, best.WorldX, best.WorldY, verifyTargets)) return false;

            foreach (uint acd in best.CoveredEliteAcds) _cast.VerifyImportantAcds.Add(acd);
            _cast.VerifyRequiredCount = best.CoveredElites > 0 ? best.CoveredElites : 1;
            _cast.BaselineImportantApplied = _cast.VerifyImportantAcds.Count(acd =>
            {
                IMonster planned = FindMonster(acd);
                return planned != null && planned.MarkedForDeath;
            });
            _cast.RequiredImportantApplied = _cast.VerifyImportantAcds.Count > 0
                ? Math.Min(_cast.VerifyImportantAcds.Count, Math.Max(1, currentElites + 1))
                : 1;
            _cast.BaselineMfdActorAcd = _lastValleyActorAcd;
            _cast.BaselineMfdActorCreatedTick = _lastValleyActorCreatedTick;
            _cast.BaselineMfdGameTick = Hud.Game.CurrentGameTick;
            _lastMfdCastTick = now;
            ClearMfdImprovementCandidate();
            return true;
        }

        private bool IsMfdImprovementStable(Placement best, HashSet<uint> currentEliteAcds, int now)
        {
            if (best == null || !best.CoveredEliteAcds.Any(acd => currentEliteAcds == null || !currentEliteAcds.Contains(acd)))
            {
                ClearMfdImprovementCandidate();
                return false;
            }

            string signature = string.Join(",", best.CoveredEliteAcds
                .OrderBy(x => x)
                .Select(x => x.ToString(CultureInfo.InvariantCulture))
                .ToArray());
            bool same = string.Equals(signature, _mfdImprovementSignature, StringComparison.Ordinal);
            if (!same)
            {
                _mfdImprovementSignature = signature;
                _mfdImprovementTick = now;
                return false;
            }

            int stableMs = Elapsed(_mfdImprovementTick, now);
            return stableMs >= Math.Max(0, MfdEliteGainStableMs);
        }

        private void ClearMultishotSweep()
        {
            _multishotSweepRemaining = 0;
            _multishotSweepUntilTick = int.MinValue;
            s7o_ZDH_HelperMetrics.LastMultishotSweepActive = false;
            s7o_ZDH_HelperMetrics.LastMultishotSweepRemaining = 0;
            s7o_ZDH_HelperMetrics.LastMultishotSweepUncoveredElites = 0;
        }

        private void ClearMfdImprovementCandidate()
        {
            _mfdImprovementSignature = string.Empty;
            _mfdImprovementTick = int.MinValue;
        }

        private Placement CreateTrashSnapshotPlacement(CombatCluster cluster,
            List<IMonster> planningTargets, int now)
        {
            if (cluster == null || planningTargets == null || planningTargets.Count == 0
                || !cluster.TrashLatched
                || !IsTrashFightLatchActive(now)
                || _trashFightLatchConfirmedBodies <= 0)
                return null;

            Placement placement = CreatePlacement(_trashFightLatchX, _trashFightLatchY, _trashFightLatchZ);
            if (placement == null) return null;
            ScorePlacement(placement, planningTargets, now);
            return placement;
        }

        private bool HasMissingPrimaryMfdCoverage(IPlayer zdh, int now)
        {
            List<IMonster> targets = GetActiveMfdSupportTargets(zdh, now);
            if (targets.Count == 0)
            {
                _runtime.MfdCoverageSetChanged = false;
                return false;
            }

            Placement current = CurrentValleyPlacement(targets, now);
            List<IMonster> mfdOnlyTargets = targets.Where(IsGroundSupportMfdOnlyTarget).ToList();
            Placement best = mfdOnlyTargets.Count > 0
                ? FindBestJuggernautAnchoredPlacement(mfdOnlyTargets, targets, now)
                : FindBestPlacement(targets, now);
            var currentAcds = new HashSet<uint>(current == null
                ? Enumerable.Empty<uint>() : current.CoveredEliteAcds);
            bool setChanged = best != null
                && best.CoveredEliteAcds.Any(acd => !currentAcds.Contains(acd));
            _runtime.MfdCoverageSetChanged = setChanged;
            return currentAcds.Count == 0 || setChanged;
        }

        private bool HasNoPrimaryMfdCoverage(IPlayer zdh, int now)
        {
            List<IMonster> targets = GetActiveMfdSupportTargets(zdh, now);
            if (targets.Count == 0) return false;
            Placement current = CurrentValleyPlacement(targets, now);
            return current == null || current.CoveredElites == 0;
        }

        private bool HasCurrentTrashMfdCoverage(CombatCluster cluster, int now)
        {
            if (cluster == null || cluster.Bodies.Count == 0) return false;
            List<IMonster> targets = cluster.Bodies
                .Where(m => m != null && m.Rarity != ActorRarity.RareMinion
                    && m.FloorCoordinate != null && m.Attackable && !m.Invulnerable)
                .ToList();
            if (targets.Count == 0) return false;
            Placement current = CurrentValleyPlacement(targets, now);
            return current != null && current.CoveredBodies > 0;
        }

        private void UpdateSentryChargeTelemetry(int currentCharges, int now)
        {
            if (currentCharges < 0)
            {
                _lastObservedSentryCharges = -1;
                return;
            }
            if (_lastObservedSentryCharges >= 0 && currentCharges > _lastObservedSentryCharges)
                _lastSentryChargeIncreaseTick = now;
            _lastObservedSentryCharges = currentCharges;
        }

        private bool IsSentryRetryPending()
        {
            return _sentryRetryTick != int.MinValue;
        }

        private void SetSentryRetry(int now, int delayMs, string reason)
        {
            _sentryRetryTick = now;
            _sentryRetryDelayMs = Math.Max(100, delayMs);
            _sentryRetryReason = reason ?? string.Empty;
        }

        private void ClearSentryRetry()
        {
            _sentryRetryTick = int.MinValue;
            _sentryRetryDelayMs = 0;
            _sentryRetryReason = string.Empty;
        }


        private void PruneRejectedSentryPositions(int now)
        {
            int holdMs = Math.Max(250, SentryRejectedPositionHoldMs);
            _rejectedSentryPositions.RemoveAll(x => x == null || Elapsed(x.Tick, now) > holdMs);
            while (_rejectedSentryPositions.Count > 5)
                _rejectedSentryPositions.RemoveAt(0);
        }

        private void MarkRejectedSentryPosition(int now, string reason)
        {
            if (float.IsNaN(_cast.ExpectedWorldX) || float.IsNaN(_cast.ExpectedWorldY)) return;
            PruneRejectedSentryPositions(now);
            float radius = Math.Max(3f, SentryRejectedPositionRadius);
            _rejectedSentryPositions.RemoveAll(x => x != null
                && Distance2D(x.X, x.Y, _cast.ExpectedWorldX, _cast.ExpectedWorldY) <= radius);
            _rejectedSentryPositions.Add(new RejectedSentryPosition
            {
                X = _cast.ExpectedWorldX,
                Y = _cast.ExpectedWorldY,
                Tick = now,
                Slot = _cast.SentrySlot,
                Reason = reason ?? string.Empty,
            });
            PruneRejectedSentryPositions(now);
        }

        private void ClearRejectedSentryPositionNear(float x, float y)
        {
            float radius = Math.Max(3f, SentryRejectedPositionRadius);
            _rejectedSentryPositions.RemoveAll(p => p != null
                && Distance2D(p.X, p.Y, x, y) <= radius);
        }

        private void ClearRejectedSentryPositions()
        {
            _rejectedSentryPositions.Clear();
        }

        private bool IsRejectedSentryPlacement(Placement placement, int now)
        {
            if (placement == null) return false;
            PruneRejectedSentryPositions(now);
            float radius = Math.Max(3f, SentryRejectedPositionRadius);
            return _rejectedSentryPositions.Any(x => x != null
                && Distance2D(x.X, x.Y, placement.WorldX, placement.WorldY) <= radius);
        }

        private RejectedSentryPosition GetRejectedSentryPositionNear(Placement placement, int now)
        {
            if (placement == null) return null;
            PruneRejectedSentryPositions(now);
            float radius = Math.Max(3f, SentryRejectedPositionRadius);
            return _rejectedSentryPositions.Where(x => x != null
                    && Distance2D(x.X, x.Y, placement.WorldX, placement.WorldY) <= radius)
                .OrderByDescending(x => x.Tick).FirstOrDefault();
        }

        private void PublishRejectedSentryPositions(int now)
        {
            PruneRejectedSentryPositions(now);
            RejectedSentryPosition latest = _rejectedSentryPositions
                .OrderByDescending(x => x.Tick).FirstOrDefault();
        }

        private bool IsTrashFightLatchActive(int now)
        {
            return _trashFightLatchUntilTick != int.MinValue
                && unchecked(_trashFightLatchUntilTick - now) > 0;
        }

        private void ArmTrashFightLatch(CombatCluster cluster, int now)
        {
            if (cluster == null || cluster.Bodies.Count == 0) return;
            bool newEngagement = !IsTrashFightLatchActive(now)
                || Distance2D(cluster.CenterX, cluster.CenterY, _trashFightLatchX, _trashFightLatchY)
                    > Math.Max(10f, TrashFightLatchRadius);
            if (newEngagement) _trashInitialMultishotDone = false;
            _trashFightLatchUntilTick = unchecked(now + Math.Max(500, Math.Min(3000, TrashFightLatchMs)));
            _trashFightLatchX = cluster.CenterX;
            _trashFightLatchY = cluster.CenterY;
            _trashFightLatchZ = cluster.CenterZ;
            _trashFightLatchAxisX = cluster.AxisX;
            _trashFightLatchAxisY = cluster.AxisY;
            if (!NormalizeDirection(ref _trashFightLatchAxisX, ref _trashFightLatchAxisY))
            {
                _trashFightLatchAxisX = 1f;
                _trashFightLatchAxisY = 0f;
            }
            _trashFightLatchMajorExtent = cluster.MajorExtent;
            _trashFightLatchMinorExtent = cluster.MinorExtent;
            _trashFightLatchConfirmedBodies = Math.Max(_trashFightLatchConfirmedBodies, cluster.Bodies.Count);
            _trashFightLatchConfirmedDamaged = Math.Max(_trashFightLatchConfirmedDamaged, cluster.RecentDamageCount);
            if (newEngagement)
            {
                _trashFightLatchConfirmedBodies = cluster.Bodies.Count;
                _trashFightLatchConfirmedDamaged = cluster.RecentDamageCount;
            }
            _trashFightLatchState = "confirmed";
        }

        private void ClearTrashFightLatch(string reason)
        {
            _trashFightLatchUntilTick = int.MinValue;
            _trashFightLatchX = 0;
            _trashFightLatchY = 0;
            _trashFightLatchZ = 0;
            _trashFightLatchAxisX = 1f;
            _trashFightLatchAxisY = 0f;
            _trashFightLatchMajorExtent = 0;
            _trashFightLatchMinorExtent = 0;
            _trashFightLatchConfirmedBodies = 0;
            _trashFightLatchConfirmedDamaged = 0;
            _trashFightLatchState = reason ?? string.Empty;
            _trashInitialMultishotDone = false;
            s7o_ZDH_HelperMetrics.LastTrashIceblinkQueueDue = false;
        }

        private void PublishTrashFightLatch(int now, bool retained, int bodies)
        {
            bool active = IsTrashFightLatchActive(now);
            if (!active && _trashFightLatchUntilTick != int.MinValue)
                ClearTrashFightLatch("expired");
            _runtime.TrashFightLatched = retained && active;
            _runtime.TrashFightLatchBodies = active ? Math.Max(0, bodies) : 0;
        }

        private bool CanRetainTrashFight(CombatCluster cluster)
        {
            if (cluster == null || cluster.Elites.Count > 0) return false;
            bool mfdOnlyFight = cluster.MfdOnlyTargets.Count > 0;
            int minimumBodies = mfdOnlyFight ? 1 : Math.Max(1, TrashFightLatchMinBodies);
            int minimumDamaged = mfdOnlyFight ? 1 : Math.Max(0, TrashFightLatchMinDamagedBodies);
            if (cluster.Bodies.Count < minimumBodies || cluster.RecentDamageCount < minimumDamaged) return false;
            float radius = Math.Max(10f, TrashFightLatchRadius);
            return Distance2D(cluster.CenterX, cluster.CenterY, _trashFightLatchX, _trashFightLatchY) <= radius;
        }

        private CombatCluster BuildLatchedTrashCluster(List<IMonster> bodies, int now)
        {
            if (!IsTrashFightLatchActive(now)) return null;
            if (bodies == null || bodies.Count == 0) return null;

            float radius = Math.Max(10f, TrashFightLatchRadius);
            var retained = bodies.Where(m => m != null
                    && (!IsGroundSupportPrimaryElite(m) || IsGroundSupportMfdOnlyTarget(m))
                    && m.FloorCoordinate != null
                    && Distance2D(m.FloorCoordinate.X, m.FloorCoordinate.Y,
                        _trashFightLatchX, _trashFightLatchY) <= radius + GetMonsterRadiusBottom(m))
                .ToList();
            if (retained.Count == 0) return null;

            var cluster = new CombatCluster { TrashLatched = true };
            foreach (IMonster body in retained)
            {
                cluster.Bodies.Add(body);
                if (IsGroundSupportMfdOnlyTarget(body)) cluster.MfdOnlyTargets.Add(body);
                if (IsEngaged(GetTargetState(body, now), now)) cluster.RecentDamageCount++;
                cluster.Score += CombatBodyWeight(body, now);
            }

            bool mfdOnlyFight = cluster.MfdOnlyTargets.Count > 0;
            int minimumBodies = mfdOnlyFight ? 1 : Math.Max(1, TrashFightLatchMinBodies);
            int minimumDamaged = mfdOnlyFight ? 1 : Math.Max(0, TrashFightLatchMinDamagedBodies);
            if (cluster.Bodies.Count < minimumBodies || cluster.RecentDamageCount < minimumDamaged) return null;

            FinalizeCombatCluster(cluster, now);
            cluster.CenterX = _trashFightLatchX;
            cluster.CenterY = _trashFightLatchY;
            cluster.CenterZ = _trashFightLatchZ;
            cluster.AxisX = _trashFightLatchAxisX;
            cluster.AxisY = _trashFightLatchAxisY;
            cluster.MajorExtent = _trashFightLatchMajorExtent;
            cluster.MinorExtent = _trashFightLatchMinorExtent;
            cluster.Stable = true;
            cluster.TrashLatched = true;
            s7o_ZDH_HelperMetrics.LastMfdOnlyTargets = cluster.MfdOnlyTargets.Count;
            return cluster;
        }

        private int GetSentryPlacementDeficit(ZdhLoadout local, CombatCluster cluster, int now,
            List<IActor> sentries, out int effectiveOwned)
        {
            effectiveOwned = 0;
            _runtime.ProtectedSentryCoverageMissing = false;
            _runtime.SentryDesired = 0;
            if (local == null || cluster == null || !local.Guardian || local.Sentry == null) return 0;
            List<Placement> desired = BuildDesiredSentryPlacements(local, cluster, now, false);
            if (desired.Count == 0) return 0;
            sentries = sentries ?? new List<IActor>();
            int targetCount = Math.Min(GetDesiredSentryCount(local), desired.Count);
            int coreDesired = Math.Min(Math.Max(1, InitialSentryFieldCount), targetCount);
            List<Placement> unmatched = GetUnmatchedDesiredSentryPlacements(desired, sentries);
            int matched = Math.Max(0, targetCount - unmatched.Count);
            int coreMatched = CountDesiredSentryMatches(desired.Take(coreDesired).ToList(), sentries);
            effectiveOwned = Math.Min(targetCount, CountRelevantSentries(desired, sentries));
            int distinctOwned = Math.Min(targetCount, CountDistinctRelevantSentries(desired, sentries));
            int stackedPairs = CountSeverelyStackedSentries(desired, sentries);
            bool protectedCoverageMissing = desired
                .Where(placement => placement != null && placement.Label != null
                    && placement.Label.StartsWith("Sentry DPS", StringComparison.Ordinal))
                .Any(placement => !IsSentryNear(sentries, placement.WorldX, placement.WorldY, GuardianRadius));
            int eliteCoverageSlots = Math.Min(
                Math.Max(0, EliteSentryCoverageMaxPlacements),
                Math.Max(0, targetCount - coreDesired));
            bool eliteCoverageMissing = BuildEliteSentryCoveragePlacements(
                    local, cluster, now, eliteCoverageSlots)
                .Any(placement => !IsSentryNear(sentries,
                    placement.WorldX, placement.WorldY, GuardianRadius));
            _runtime.ProtectedSentryCoverageMissing = protectedCoverageMissing || eliteCoverageMissing;
            _runtime.SentryDesired = targetCount;
            int countDeficit = Math.Max(0, targetCount - effectiveOwned);
            int distinctCoreDeficit = Math.Max(0, coreDesired - distinctOwned);
            return countDeficit > 0 ? countDeficit
                : distinctCoreDeficit > 0 ? distinctCoreDeficit
                : protectedCoverageMissing || eliteCoverageMissing || stackedPairs > 0 ? 1 : 0;
        }

        private bool TryStartSentry(ZdhLoadout local, CombatCluster cluster, int now, bool emergencyOnly,
            bool countSetup = false, bool sentryBurstChild = false, bool bypassBurstRecast = false)
        {
            int desiredCount = local == null ? 0 : GetDesiredSentryCount(local);
            _runtime.SentryDesired = 0;
            if (countSetup && _sentryFullFieldHold) return false;
            int recastMs = countSetup
                ? Math.Max(100, InitialSetupBurstGapMs) : SentryRecastMs;
            if (cluster == null || !SentryAvailable(local.Sentry)) return false;
            if (!bypassBurstRecast && Elapsed(_lastSentryCastTick, now) < recastMs) return false;

            List<IActor> allSentries = GetOwnedSentries();
            List<IActor> sentries = allSentries.Where(a => a != null && a.IsOnScreen).ToList();
            s7o_ZDH_HelperMetrics.LastSentryTotalOwned = allSentries.Count;
            s7o_ZDH_HelperMetrics.LastSentryOnScreenOwned = sentries.Count;
            List<Placement> desired = BuildDesiredSentryPlacements(local, cluster, now, emergencyOnly);
            if (desired.Count == 0) return false;

            int targetCount = Math.Min(desiredCount, desired.Count);
            int effectiveOwned = CountRelevantSentries(desired, allSentries);
            Placement missing = countSetup && effectiveOwned == 0
                ? CreateRecentMfdSentryAnchor(cluster, allSentries, now) : null;
            if (missing != null && IsRejectedSentryPlacement(missing, now))
                missing = null;
            bool mfdAnchor = missing != null;
            if (missing == null)
                missing = FindMissingDesiredSentryPlacement(desired, allSentries, targetCount, emergencyOnly, now);
            _runtime.SentryDesired = targetCount;
            int coreDesired = Math.Min(Math.Max(1, InitialSentryFieldCount), targetCount);
            _runtime.SentryPlacementDeficit = Math.Max(0, targetCount - effectiveOwned);
            if (missing == null) return false;

            string sentryLabel = string.IsNullOrEmpty(missing.Label) ? "Sentry Field" : missing.Label;
            if (countSetup) sentryLabel = "Sentry Count Setup";
            float sentryCastDistance = local.Player == null || local.Player.FloorCoordinate == null
                ? 0 : local.Player.FloorCoordinate.XYDistanceTo(missing.WorldX, missing.WorldY);
            if (!sentryBurstChild && !EnsureSupportPrimaryReady(CastKind.Sentry, countSetup, now)) return false;
            if (!StartCast(CastKind.Sentry, local.Sentry, missing.TargetAcd, missing.Screen, now,
                sentryLabel,
                missing.WorldX, missing.WorldY, null, sentryBurstChild)) return false;
            _cast.SentryMfdAnchor = mfdAnchor;
            foreach (uint acd in missing.CoveredEliteAcds)
                if (acd != 0) _cast.SentryCoverageAcds.Add(acd);
            _cast.SentrySlot = missing.SentrySlot;
            _cast.SentryFallback = missing.SentryFallback;
            _cast.SentryCastDistance = sentryCastDistance;
            _cast.SentryFallbackReason = missing.SentryFallbackReason ?? string.Empty;
            _lastSentryCastTick = now;
            return true;
        }

        private bool TryStartSentryDuringMfdRetry(ZdhLoadout local, CombatCluster cluster, int now, bool allowed)
        {
            if (!allowed || local == null || local.Sentry == null || !local.Guardian) return false;
            if (!TryStartSentry(local, cluster, now, false, true)) return false;
            return true;
        }

        private bool EnsureSupportPrimaryReady(CastKind kind, bool sentrySetup, int now)
        {
            if (kind == CastKind.Entangle || !s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh) return true;

            int requiredMs = sentrySetup ? SentrySetupPrimaryQuietMs
                : _bossStandaloneActive ? BossSupportPrimaryQuietMs
                : s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                    ? CombatSupportPrimaryQuietMs : SpeedSupportPrimaryQuietMs;
            int quietAgeMs = s7o_DHStrafePrimaryPlugin.PrimaryQuietAgeForZdh(now);
            int leaseMs = sentrySetup ? SentrySetupPreemptLeaseMs : PrimaryPreemptLeaseMs;
            SuppressDhStrafePrimary(Math.Max(leaseMs, requiredMs + 80));

            string readyKind = kind == CastKind.Multishot ? "Iceblink"
                : kind == CastKind.MarkedForDeath ? "MFD"
                : sentrySetup ? "Sentry setup" : "Sentry";

            if (quietAgeMs == int.MaxValue || quietAgeMs >= Math.Max(0, requiredMs)) return true;

            _supportPrimaryGateBlocked = true;
            return false;
        }

        private void ResetSentryBurstEngagement()
        {
            _coreBurstAttemptedForEngagement = false;
            _coreBurstAttemptsThisEngagement = 0;
            _coreBurstRetryAfterTick = int.MinValue;
            _coreBurstBelowTargetSinceTick = int.MinValue;
            _sentryRelevanceDeficitSinceTick = int.MinValue;
            _fullSentryFieldEstablishedForEngagement = false;
            _sentryFullFieldHold = false;
            _coreBurstAnchorValid = false;
            _coreBurstAnchorX = 0;
            _coreBurstAnchorY = 0;
            _wasSentryEngagementActive = false;
            _lastCompletionBurstAttemptCharges = -1;
            _lastCompletionBurstAttemptRelevant = -1;
            _lastCompletionBurstAttemptAnchorX = 0;
            _lastCompletionBurstAttemptAnchorY = 0;
            _lastCompletionBurstAttemptAnchorValid = false;
            _lastCompletionBurstAttemptTick = int.MinValue;
        }

        private void UpdateSentryBurstEngagement(CombatCluster cluster, bool sentryEngagementActive,
            int currentRelevant, int coreTarget, int now)
        {
            if (!sentryEngagementActive || cluster == null)
            {
                ResetSentryBurstEngagement();
                return;
            }

            bool newEngagement = !_wasSentryEngagementActive
                || !_coreBurstAnchorValid
                || Distance2D(_coreBurstAnchorX, _coreBurstAnchorY,
                    cluster.CenterX, cluster.CenterY) > SentryFieldRelevanceRadius;
            if (newEngagement)
            {
                _coreBurstAttemptedForEngagement = false;
                _coreBurstAttemptsThisEngagement = 0;
                _coreBurstRetryAfterTick = int.MinValue;
                _coreBurstBelowTargetSinceTick = int.MinValue;
                _fullSentryFieldEstablishedForEngagement = false;
                _sentryFullFieldHold = false;
                _coreBurstAnchorValid = true;
                _coreBurstAnchorX = cluster.CenterX;
                _coreBurstAnchorY = cluster.CenterY;

                _lastCompletionBurstAttemptCharges = -1;
                _lastCompletionBurstAttemptRelevant = -1;
                _lastCompletionBurstAttemptAnchorValid = false;
            }

            bool coreBelowTarget = coreTarget > 0 && currentRelevant < coreTarget;
            if (coreBelowTarget)
            {
                if (_coreBurstBelowTargetSinceTick == int.MinValue)
                    _coreBurstBelowTargetSinceTick = now;

                int lossAge = Elapsed(_coreBurstBelowTargetSinceTick, now);
                if (_coreBurstAttemptedForEngagement
                    && _sentryBurst.Mode == SentryBurstMode.None
                    && lossAge >= Math.Max(0, SentryCoreLossRearmMs))
                {
                    _coreBurstAttemptedForEngagement = false;
                    _coreBurstAttemptsThisEngagement = 0;
                    _coreBurstRetryAfterTick = int.MinValue;
                    _coreBurstAnchorValid = true;
                    _coreBurstAnchorX = cluster.CenterX;
                    _coreBurstAnchorY = cluster.CenterY;

                    _lastCompletionBurstAttemptCharges = -1;
                    _lastCompletionBurstAttemptRelevant = -1;
                    _lastCompletionBurstAttemptAnchorValid = false;

                    _coreBurstBelowTargetSinceTick = int.MinValue;
                }
            }
            else
            {
                _coreBurstBelowTargetSinceTick = int.MinValue;
            }

            _wasSentryEngagementActive = true;
        }

        private bool SentryBurstFirstChildGapReady(int now)
        {
            int gapMs = Math.Max(100, InitialSetupBurstGapMs);
            if (_lastCastFinishedTick != int.MinValue
                && Elapsed(_lastCastFinishedTick, now) < gapMs)
                return false;
            if (_lastSentryCastTick != int.MinValue
                && Elapsed(_lastSentryCastTick, now) < gapMs)
                return false;
            return true;
        }

        private bool CoreBurstRetryReady(int now)
        {
            return _coreBurstRetryAfterTick == int.MinValue
                || Reached(now, _coreBurstRetryAfterTick);
        }

        private void RecordCoreBurstEnd(string reason, int now)
        {
            if (_sentryBurst.Mode != SentryBurstMode.Core) return;

            int coreTarget = Math.Min(
                Math.Max(1, InitialSentryFieldCount),
                Math.Max(1, _sentryBurst.TargetCount));
            bool nominallyCompleted = string.Equals(reason, "planned complete", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "field satisfied", StringComparison.OrdinalIgnoreCase);
            bool coreSatisfied = _sentryBurst.CurrentRelevant >= coreTarget;

            if (nominallyCompleted && coreSatisfied)
            {
                _coreBurstAttemptedForEngagement = true;
                _coreBurstRetryAfterTick = int.MinValue;
                return;
            }

            bool debuffPreempt = string.Equals(
                reason, "debuff preempt", StringComparison.OrdinalIgnoreCase);
            _coreBurstAttemptedForEngagement = false;
            if (debuffPreempt)
            {
                if (_sentryBurst.VerifiedSentries == 0 && _coreBurstAttemptsThisEngagement > 0)
                    _coreBurstAttemptsThisEngagement--;
                _coreBurstRetryAfterTick = int.MinValue;
                return;
            }

            if (_coreBurstAttemptsThisEngagement < Math.Max(1, SentryCoreBurstMaxAttemptsPerEngagement))
                _coreBurstRetryAfterTick = unchecked(now + Math.Max(100, SentryFailedRetryMs));
            else
                _coreBurstRetryAfterTick = int.MinValue;
        }

        private bool IsNewCompletionBurstOpportunity(CombatCluster cluster, int relevant, int charges, int now)
        {
            if (cluster == null) return false;
            bool anchorChanged = !_lastCompletionBurstAttemptAnchorValid
                || Distance2D(_lastCompletionBurstAttemptAnchorX, _lastCompletionBurstAttemptAnchorY,
                    cluster.CenterX, cluster.CenterY) > SentryFieldRelevanceRadius;
            bool chargesChanged = charges != _lastCompletionBurstAttemptCharges;
            bool relevantChanged = relevant != _lastCompletionBurstAttemptRelevant;
            bool retryDue = _lastCompletionBurstAttemptTick != int.MinValue
                && Elapsed(_lastCompletionBurstAttemptTick, now) >= Math.Max(SentryRecastMs, SentryFailedRetryMs);
            s7o_ZDH_HelperMetrics.LastCompletionOpportunityReason = anchorChanged ? "anchor"
                : relevantChanged ? "relevant"
                : chargesChanged ? "charges"
                : retryDue ? "retry" : "held";
            return anchorChanged || relevantChanged || chargesChanged || retryDue;
        }

        private int RemainingSentryBurstMs(int now)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None
                || _sentryBurst.AbsoluteDeadlineTick == int.MinValue) return 0;
            return Math.Max(0, unchecked(_sentryBurst.AbsoluteDeadlineTick - now));
        }

        private bool SentryBurstDeadlineExpired(int now)
        {
            return _sentryBurst.Mode != SentryBurstMode.None
                && _sentryBurst.AbsoluteDeadlineTick != int.MinValue
                && Reached(now, _sentryBurst.AbsoluteDeadlineTick);
        }

        private void PublishSentryBurstState(int now, int currentRelevant = -1, int currentCharges = -1)
        {
            bool active = _sentryBurst.Mode != SentryBurstMode.None;
            s7o_ZDH_HelperMetrics.LastSentryBurstActive = active;
            s7o_ZDH_HelperMetrics.LastSentryBurstMode = active ? _sentryBurst.Mode.ToString().ToLowerInvariant() : string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryBurstStage = active ? _sentryBurst.Stage.ToString().ToLowerInvariant() : string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryBurstPlanned = active ? _sentryBurst.PlannedSentries : 0;
            s7o_ZDH_HelperMetrics.LastSentryBurstVerified = active ? _sentryBurst.VerifiedSentries : 0;
        }

        private bool BeginSentryBurst(SentryBurstMode mode, ZdhLoadout local, CombatCluster cluster,
            int now, int plannedSentries, int currentRelevant, int targetCount)
        {
            if (_sentryBurst.Mode != SentryBurstMode.None || _cast.Stage != CastStage.Idle
                || local == null || local.Sentry == null || cluster == null
                || plannedSentries < 1)
                return false;

            int maxMs = mode == SentryBurstMode.Core
                ? SentryCoreBurstAbsoluteMaxMs : SentryCompletionBurstAbsoluteMaxMs;
            _sentryBurst.Mode = mode;
            _sentryBurst.Stage = SentryBurstStage.Acquire;
            _sentryBurst.StartedTick = now;
            _sentryBurst.AcquireDeadlineTick = unchecked(now + Math.Max(80, SentryBurstAcquireMaxMs));
            _sentryBurst.AbsoluteDeadlineTick = unchecked(now + Math.Max(250, maxMs));
            _sentryBurst.SettleDeadlineTick = int.MinValue;
            _sentryBurst.StandstillOwned = false;
            _sentryBurst.PlannedSentries = plannedSentries;
            _sentryBurst.VerifiedSentries = 0;
            _sentryBurst.TailSentries = 0;
            _sentryBurst.StartRelevant = currentRelevant;
            _sentryBurst.CurrentRelevant = currentRelevant;
            _sentryBurst.StartCharges = local.Sentry.Charges;
            _sentryBurst.TargetCount = targetCount;
            _sentryBurst.AnchorX = cluster.CenterX;
            _sentryBurst.AnchorY = cluster.CenterY;
            _sentryBurst.ChildJustFinished = false;
            _sentryBurst.EndReason = string.Empty;

            if (mode == SentryBurstMode.Core)
            {
                _coreBurstAttemptsThisEngagement++;
                _coreBurstRetryAfterTick = int.MinValue;
            }
            else
            {
                _lastCompletionBurstAttemptCharges = local.Sentry.Charges;
                _lastCompletionBurstAttemptRelevant = currentRelevant;
                _lastCompletionBurstAttemptAnchorX = cluster.CenterX;
                _lastCompletionBurstAttemptAnchorY = cluster.CenterY;
                _lastCompletionBurstAttemptAnchorValid = true;
                _lastCompletionBurstAttemptTick = now;
            }

            s7o_ZDH_HelperMetrics.LastSentryBurstEndReason = string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryBurstMovementSettled = false;
            s7o_ZDH_HelperMetrics.LastSentryBurstDurationMs = 0;
            s7o_ZDH_HelperMetrics.LastSentryBurstChildSequence = 0;
            PublishSentryBurstState(now, currentRelevant, local.Sentry.Charges);

            SuppressDhStrafePrimary(Math.Max(SentrySetupPreemptLeaseMs, SentryBurstAcquireMaxMs + 120));
            RequestDhStrafePause(Math.Max(120, maxMs + 120));
            return true;
        }

        private bool TryBeginCoreSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            bool burstAutomationActive, bool sentryEngagementActive,
            bool sentryRetryReady, bool debuffsClear, bool fieldDeficitStable,
            int currentRelevant, int coreTarget, int targetCount, bool channelingPylonActive)
        {
            if (_coreBurstAttemptedForEngagement
                || _coreBurstAttemptsThisEngagement >= Math.Max(1, SentryCoreBurstMaxAttemptsPerEngagement)
                || !CoreBurstRetryReady(now)
                || !SentryBurstFirstChildGapReady(now)
                || !burstAutomationActive
                || !sentryEngagementActive || !sentryRetryReady || !debuffsClear
                || !fieldDeficitStable
                || !s7o_ZDH_HelperState.AutoSentry || local == null || !local.Guardian
                || local.Sentry == null || !SentryAvailable(local.Sentry))
                return false;

            int deficit = Math.Max(0, (channelingPylonActive ? targetCount : coreTarget) - currentRelevant);
            int charges = Math.Max(0, local.Sentry.Charges);
            int planned = channelingPylonActive ? deficit : Math.Min(deficit, charges);
            if (planned < 1) return false;
            if (!BeginSentryBurst(SentryBurstMode.Core, local, cluster, now,
                planned, currentRelevant, targetCount)) return false;
            return true;
        }

        private bool TryBeginCompletionSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            bool burstAutomationActive, bool sentryEngagementActive,
            bool sentryRetryReady, bool debuffsClear, bool fieldDeficitStable,
            int currentRelevant, int coreTarget, int targetCount, bool channelingPylonActive)
        {

            if (!SentryBurstFirstChildGapReady(now)
                || !burstAutomationActive || !sentryEngagementActive || !sentryRetryReady
                || !debuffsClear || !fieldDeficitStable
                || !s7o_ZDH_HelperState.AutoSentry || local == null || !local.Guardian
                || local.Sentry == null || !SentryAvailable(local.Sentry) || currentRelevant < coreTarget)
                return false;

            int deficit = Math.Max(0, targetCount - currentRelevant);
            int charges = Math.Max(0, local.Sentry.Charges);
            int planned = channelingPylonActive
                ? Math.Min(2, deficit) : Math.Min(2, Math.Min(deficit, charges));
            if (planned <= 0 || !IsNewCompletionBurstOpportunity(cluster, currentRelevant, charges, now))
                return false;

            if (!channelingPylonActive && planned == 1 && deficit > 1)
            {
                if (_lastSentryChargeIncreaseTick == int.MinValue)
                    return false;

                int chargeAge = Elapsed(_lastSentryChargeIncreaseTick, now);
                int coalesceMs = Math.Max(0, SentryCompletionCoalesceMs);
                if (chargeAge < coalesceMs)
                {
                    return false;
                }
            }

            if (!BeginSentryBurst(SentryBurstMode.Completion, local, cluster, now,
                planned, currentRelevant, targetCount)) return false;
            return true;
        }

        private void AdvanceSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            int relevant, int targetCount, int coreTarget, bool sentryRetryReady, bool debuffsClear,
            bool channelingPylonActive)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            int charges = local == null || local.Sentry == null ? 0 : local.Sentry.Charges;
            _sentryBurst.CurrentRelevant = relevant;
            PublishSentryBurstState(now, relevant, charges);

            if (SentryBurstDeadlineExpired(now))
            {
                ForceAbortSentryBurst("absolute watchdog", now);
                return;
            }
            if (cluster == null)
            {
                EndSentryBurst("cluster lost", now);
                return;
            }

            _sentryBurst.AnchorX = cluster.CenterX;
            _sentryBurst.AnchorY = cluster.CenterY;

            int remaining = RemainingSentryBurstMs(now);
            RequestDhStrafePause(Math.Max(80, remaining + 80));
            SuppressDhStrafePrimary(Math.Max(80, remaining + 80));

            if (_sentryBurst.Stage == SentryBurstStage.Acquire)
            {
                if (Reached(now, _sentryBurst.AcquireDeadlineTick))
                {
                    EndSentryBurst("acquire timeout", now);
                    return;
                }

                bool pauseReady = !s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh
                    || DhStrafePauseAcknowledgedSince(_sentryBurst.StartedTick);
                int quietAge = s7o_DHStrafePrimaryPlugin.PrimaryQuietAgeForZdh(now);
                bool primaryReady = quietAge == int.MaxValue
                    || quietAge >= Math.Max(0, SentrySetupPrimaryQuietMs);
                if (!pauseReady || !primaryReady) return;

                if (ForceStandstillVirtualKey != 0 && !ZdhInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                {
                    if (!ZdhInput.KeyDown(ForceStandstillVirtualKey))
                    {
                        EndSentryBurst("standstill failed", now);
                        return;
                    }
                    _sentryBurst.StandstillOwned = true;
                }

                _sentryBurst.Stage = SentryBurstStage.Settle;
                _sentryBurst.SettleDeadlineTick = unchecked(now + Math.Max(1, SentryBurstMovementSettleMaxMs));
                PublishSentryBurstState(now, relevant, charges);
                return;
            }

            if (_sentryBurst.Stage == SentryBurstStage.Settle)
            {
                if (Hud.Game.Me.AnimationState != AcdAnimationState.Running)
                {
                    _sentryBurst.Stage = SentryBurstStage.Ready;
                    s7o_ZDH_HelperMetrics.LastSentryBurstMovementSettled = true;
                    PublishSentryBurstState(now, relevant, charges);
                    return;
                }
                if (Reached(now, _sentryBurst.SettleDeadlineTick))
                {
                    EndSentryBurst("movement did not settle", now);
                    return;
                }
                return;
            }

            if (_sentryBurst.Stage != SentryBurstStage.Ready || _cast.Stage != CastStage.Idle) return;
            if (_sentryBurst.ChildJustFinished)
            {
                _sentryBurst.ChildJustFinished = false;
                return;
            }
            int deficit = Math.Max(0, targetCount - relevant);
            if (deficit <= 0)
            {
                EndSentryBurst("field satisfied", now);
                return;
            }

            if (!debuffsClear)
            {
                EndSentryBurst("debuff preempt", now);
                return;
            }
            if (!sentryRetryReady)
            {
                EndSentryBurst("Sentry retry active", now);
                return;
            }

            if (_sentryBurst.VerifiedSentries >= _sentryBurst.PlannedSentries)
            {
                int sentryChildDeadlineBudgetMs = Math.Max(450, SentryVerifyMs + 120);
                bool canUseCoreTail = _sentryBurst.Mode == SentryBurstMode.Core
                    && !channelingPylonActive
                    && _sentryBurst.TailSentries < Math.Max(0, SentryCoreBurstMaxTailSentries)
                    && charges > 0
                    && RemainingSentryBurstMs(now) >= Math.Max(
                        Math.Max(250, SentryCoreBurstTailMinRemainingMs),
                        sentryChildDeadlineBudgetMs);

                if (canUseCoreTail)
                {
                    _sentryBurst.PlannedSentries++;
                    _sentryBurst.TailSentries++;
                    PublishSentryBurstState(now, relevant, charges);
                }
                else
                {
                    EndSentryBurst("planned complete", now);
                    return;
                }
            }

            if (local == null || local.Sentry == null || !local.Guardian || !SentryAvailable(local.Sentry))
            {
                EndSentryBurst("no charges", now);
                return;
            }

            if (Hud.Game.Me.AnimationState == AcdAnimationState.Running) return;

            int sentryChildStartBudgetMs = Math.Max(450, SentryVerifyMs + 120);
            if (RemainingSentryBurstMs(now) < sentryChildStartBudgetMs)
            {
                EndSentryBurst("insufficient child budget", now);
                return;
            }

            bool bypassRecast = _sentryBurst.VerifiedSentries > 0;
            if (!TryStartSentry(local, cluster, now, false, true, true, bypassRecast))
            {
                EndSentryBurst("no Sentry start", now);
                return;
            }
        }

        private void OnSentryBurstChildFinished(bool verified, int now)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            if (!verified)
            {
                EndSentryBurst("Sentry unverified", now);
                return;
            }

            _sentryBurst.VerifiedSentries++;
            if (_sentryBurst.Mode == SentryBurstMode.Core)
            {
                _coreBurstAttemptsThisEngagement = 0;
                _coreBurstRetryAfterTick = int.MinValue;
            }
            _sentryBurst.Stage = SentryBurstStage.Ready;
            _sentryBurst.ChildJustFinished = true;
            PublishSentryBurstState(now);
        }

        private void ReleaseSentryBurstStandstill()
        {
            if (_sentryBurst.StandstillOwned && ForceStandstillVirtualKey != 0)
                ZdhInput.KeyUp(ForceStandstillVirtualKey);
            _sentryBurst.StandstillOwned = false;
        }

        private void EndSentryBurst(string reason, int now)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            if (_cast.Stage != CastStage.Idle && _cast.SentryBurstChild)
            {
                ForceAbortSentryBurst(reason, now);
                return;
            }
            int duration = Elapsed(_sentryBurst.StartedTick, now);
            RecordCoreBurstEnd(reason, now);
            ReleaseActionInput();
            if (_cast.Stage != CastStage.Idle && _cast.SentryBurstChild && _cast.CursorOwned)
                RestoreCursorImmediately();
            ReleaseSentryBurstStandstill();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            if (_sentryBurst.VerifiedSentries > 0)
            {
                int quietMs = GetPostCastPrimaryQuietMs(CastKind.Sentry);
                if (quietMs > 0) SuppressDhStrafePrimary(quietMs);
            }
            _lastPauseReleasedTick = now;
            _lastCastFinishedTick = now;
            s7o_ZDH_HelperMetrics.LastSentryBurstActive = false;
            s7o_ZDH_HelperMetrics.LastSentryBurstPlanned = _sentryBurst.PlannedSentries;
            s7o_ZDH_HelperMetrics.LastSentryBurstVerified = _sentryBurst.VerifiedSentries;
            s7o_ZDH_HelperMetrics.LastSentryBurstEndReason = reason ?? string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryBurstDurationMs = duration;
            ResetSentryBurstState();
        }

        private void ForceAbortSentryBurst(string reason, int now)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            if (string.Equals(reason, "absolute watchdog", StringComparison.OrdinalIgnoreCase))
                s7o_ZDH_HelperMetrics.LastSentryBurstWatchdogCount++;
            int duration = Elapsed(_sentryBurst.StartedTick, now);
            RecordCoreBurstEnd(reason, now);
            ReleaseActionInput();
            if (_cast.Stage != CastStage.Idle && _cast.SentryBurstChild && _cast.CursorOwned)
                RestoreCursorImmediately();
            ResetCast();
            ReleaseSentryBurstStandstill();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            _lastPauseReleasedTick = now;
            _lastCastFinishedTick = now;
            s7o_ZDH_HelperMetrics.LastSentryBurstActive = false;
            s7o_ZDH_HelperMetrics.LastSentryBurstPlanned = _sentryBurst.PlannedSentries;
            s7o_ZDH_HelperMetrics.LastSentryBurstVerified = _sentryBurst.VerifiedSentries;
            s7o_ZDH_HelperMetrics.LastSentryBurstEndReason = reason ?? string.Empty;
            s7o_ZDH_HelperMetrics.LastSentryBurstDurationMs = duration;
            ResetSentryBurstState();
        }

        private void EnforceSentryBurstWatchdog(int now)
        {
            if (SentryBurstDeadlineExpired(now))
                ForceAbortSentryBurst("absolute watchdog", now);
        }

        private void ResetSentryBurstState()
        {
            _sentryBurst.Mode = SentryBurstMode.None;
            _sentryBurst.Stage = SentryBurstStage.Idle;
            _sentryBurst.StartedTick = int.MinValue;
            _sentryBurst.AcquireDeadlineTick = int.MinValue;
            _sentryBurst.AbsoluteDeadlineTick = int.MinValue;
            _sentryBurst.SettleDeadlineTick = int.MinValue;
            _sentryBurst.StandstillOwned = false;
            _sentryBurst.PlannedSentries = 0;
            _sentryBurst.VerifiedSentries = 0;
            _sentryBurst.TailSentries = 0;
            _sentryBurst.StartRelevant = 0;
            _sentryBurst.CurrentRelevant = 0;
            _sentryBurst.StartCharges = 0;
            _sentryBurst.TargetCount = 0;
            _sentryBurst.AnchorX = 0;
            _sentryBurst.AnchorY = 0;
            _sentryBurst.ChildJustFinished = false;
            _sentryBurst.EndReason = string.Empty;
        }

        private bool StartCast(CastKind kind, IPlayerSkill skill, uint targetAcd, IScreenCoordinate aim, int now, string label,
            float expectedWorldX = float.NaN, float expectedWorldY = float.NaN, IEnumerable<uint> verifyTargetAcds = null,
            bool sentryBurstChild = false)
        {
            if (_cast.Stage != CastStage.Idle || skill == null || aim == null || !PointInsideCastArea(aim.X, aim.Y)) return false;
            if (ActionIsDown(skill.Key)) return false;

            ResetCast();
            _cast.Kind = kind;
            _cast.Stage = CastStage.Lease;
            _cast.SentryBurstChild = sentryBurstChild;
            _cast.Skill = skill;
            _cast.TargetAcd = targetAcd;
            _cast.StartedTick = now;
            _cast.DueTick = unchecked(now + StrafePauseAckTimeoutMs);
            _cast.SavedCursorX = Hud.Window.CursorX;
            _cast.SavedCursorY = Hud.Window.CursorY;
            _cast.AimX = (int)Math.Round(aim.X);
            _cast.AimY = (int)Math.Round(aim.Y);
            _cast.AimSettleMs = GetAimSettleMs(kind);
            _cast.HoldMs = GetSkillHoldMs(kind);
            _cast.MinimumLeaseMs = MinimumCastLeaseMs;
            _cast.VerifyMs = GetVerifyMs(kind);
            _cast.BaselineTargetFlag = GetTargetFlag(kind, targetAcd);
            _cast.RequiresStrafePause = s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh;
            _cast.BossStandalone = !_cast.RequiresStrafePause && _bossStandaloneActive;
            _cast.BaselineCharges = kind == CastKind.Sentry ? skill.Charges : -1;
            _cast.BaselineOwnedSentries = kind == CastKind.Sentry ? GetOnScreenOwnedSentries().Count : 0;
            foreach (uint acd in GetRelevantActorIds(kind)) _cast.BaselineActorAcds.Add(acd);
            if (verifyTargetAcds != null)
                foreach (uint acd in verifyTargetAcds)
                    if (acd != 0) _cast.VerifyTargetAcds.Add(acd);
            _cast.ExpectedWorldX = expectedWorldX;
            _cast.ExpectedWorldY = expectedWorldY;
            _cast.Label = label;

            s7o_ZDH_HelperMetrics.LastAction = label;
            s7o_ZDH_HelperMetrics.LastResult = "started";
            s7o_ZDH_HelperMetrics.LastTargetAcd = targetAcd;
            s7o_ZDH_HelperMetrics.LastPauseAckMs = -1;
            s7o_ZDH_HelperMetrics.LastPreInputAnimation = string.Empty;
            s7o_ZDH_HelperMetrics.LastInputDownMs = -1;
            s7o_ZDH_HelperMetrics.LastInputUpMs = -1;
            s7o_ZDH_HelperMetrics.LastEffectMs = -1;
            s7o_ZDH_HelperMetrics.LastVerificationSource = "pending";
            s7o_ZDH_HelperMetrics.LastBossStandalone = _cast.BossStandalone;
            if (sentryBurstChild)
            {
                if (kind != CastKind.Sentry || _sentryBurst.Mode == SentryBurstMode.None
                    || _sentryBurst.Stage != SentryBurstStage.Ready)
                {
                    ResetCast();
                    return false;
                }

                _cast.SavedCursorX = Hud.Window.CursorX;
                _cast.SavedCursorY = Hud.Window.CursorY;
                InitializeCursorIntent();
                _cast.StandstillHeld = false;
                if (!SetCastCursor(_cast.AimX, _cast.AimY))
                {
                    ResetCast();
                    return false;
                }
                _cast.CursorOwned = true;
                _cast.Stage = CastStage.Aim;
                _cast.DueTick = unchecked(now + _cast.AimSettleMs);
                s7o_ZDH_HelperMetrics.LastSentryBurstChildSequence++;
                RequestDhStrafePause(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                return true;
            }

            if (_cast.RequiresStrafePause) RequestDhStrafePause(GetPreInputHardLimitMs(kind) + 80);
            return true;
        }

        private void AdvanceCast(int now)
        {
            if (_cast.Stage == CastStage.Idle) return;
            if (_cast.Stage != CastStage.Verify)
            {
                if (_cast.RequiresStrafePause && !s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh) { CancelCast("strafe off"); return; }
                bool castContextValid = _cast.SentryBurstChild
                    ? SentryBurstAutomationContextValid()
                    : AutomationContextValid();
                if (!castContextValid) { CancelCast("context"); return; }
            }
            else if (!ContextAvailable())
            {
                FinishCast("unverified", now);
                return;
            }
            AcdAnimationState animation = Hud.Game.Me.AnimationState;
            if (_cast.Stage != CastStage.Verify)
            {
                int hardLimit = _cast.InputSent ? CastPostInputHardLimitMs : GetPreInputHardLimitMs(_cast.Kind);
                int hardLimitAge = _cast.InputSent && _cast.InputDownTick != int.MinValue
                    ? Elapsed(_cast.InputDownTick, now)
                    : Elapsed(_cast.StartedTick, now);
                if (hardLimitAge > hardLimit)
                {
                    bool settleTimeout = !_cast.InputSent && _cast.Stage == CastStage.Aim
                        && RequiresMovementSettleBeforeInput() && animation == AcdAnimationState.Running;
                    CancelCast(_cast.InputSent ? "post-input hard limit"
                        : settleTimeout ? "movement settle timeout" : "pause hard limit");
                    return;
                }
            }
            if (_cast.InputSent && _cast.Stage != CastStage.Lease
                && (animation == AcdAnimationState.Casting || animation == AcdAnimationState.Attacking))
                _cast.SawCastAnimation = true;
            if (_cast.InputSent && _cast.Kind == CastKind.Multishot
                && IsNativeMultishotAnimation(Hud.Game.Me.Animation))
                _cast.SawNativeMultishotAnimation = true;

            if (_cast.Stage == CastStage.Lease)
            {
                if (_cast.RequiresStrafePause && !DhStrafePauseAcknowledgedSince(_cast.StartedTick))
                {
                    if (!Reached(now, _cast.DueTick)) return;
                    CancelCast("strafe pause timeout");
                    return;
                }

                if (_cast.PauseAckTick == int.MinValue)
                {
                    _cast.PauseAckTick = now;
                    _cast.PauseAckAnimation = Hud.Game.Me.AnimationState;
                    s7o_ZDH_HelperMetrics.LastPauseAckMs = Elapsed(_cast.StartedTick, now);

                    if (ForceStandstillVirtualKey != 0 && !ZdhInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                    {
                        _cast.StandstillHeld = ZdhInput.KeyDown(ForceStandstillVirtualKey);
                        if (!_cast.StandstillHeld) { CancelCast("standstill failed"); return; }
                    }
                }

                if (ShouldWaitForMultishotMovementSettle(now, Hud.Game.Me.AnimationState))
                {
                    if (_cast.RequiresStrafePause)
                        RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                    return;
                }

                _cast.SavedCursorX = Hud.Window.CursorX;
                _cast.SavedCursorY = Hud.Window.CursorY;
                InitializeCursorIntent();
                if (!SetCastCursor(_cast.AimX, _cast.AimY)) { CancelCast("aim failed"); return; }
                _cast.CursorOwned = true;
                _cast.Stage = CastStage.Aim;
                _cast.DueTick = unchecked(now + _cast.AimSettleMs);
                if (_cast.RequiresStrafePause) RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                return;
            }

            if (_cast.Stage == CastStage.Aim)
            {
                if (!Reached(now, _cast.DueTick)) return;
                CaptureUserCursorIntent();
                float aimDrift = CursorDistanceFrom(_cast.AimX, _cast.AimY);
                _cast.MaxAimDrift = Math.Max(_cast.MaxAimDrift, aimDrift);
                float displacementTolerance = GetAimDisplacementTolerance(_cast.Kind);
                if (aimDrift > displacementTolerance)
                {
                    int correctionLimit = Math.Max(0, GetAimCorrectionLimit(_cast.Kind));
                    if (_cast.SentryBurstChild && _cast.Kind == CastKind.Sentry
                        && aimDrift <= Math.Max(displacementTolerance, SentryBurstMinorAimDisplacementPixels))
                        correctionLimit = Math.Max(correctionLimit, 2);

                    if (_cast.AimCorrections < correctionLimit
                        && Elapsed(_cast.StartedTick, now) + AimCorrectionRetryMs < GetPreInputHardLimitMs(_cast.Kind)
                        && SetCastCursor(_cast.AimX, _cast.AimY))
                    {
                        _cast.AimCorrections++;
                        _cast.DueTick = unchecked(now + Math.Max(1, AimCorrectionRetryMs));
                        return;
                    }
                    CancelCast("aim displaced");
                    return;
                }
                if (!SetCastCursor(_cast.AimX, _cast.AimY)
                    || !IsCursorNear(_cast.AimX, _cast.AimY, 14f))
                {
                    CancelCast("aim reassert failed");
                    return;
                }
                AcdAnimationState preInputAnimation = Hud.Game.Me.AnimationState;
                if (RequiresMovementSettleBeforeInput() && preInputAnimation == AcdAnimationState.Running)
                {
                    if (_cast.RequiresStrafePause)
                        RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                    return;
                }
                if (ShouldWaitForMultishotMovementSettle(now, preInputAnimation))
                {
                    RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                    return;
                }
                _cast.AimReadyTick = now;
                if (_cast.Skill == null || !SkillReady(_cast.Skill)) { CancelCast("skill unavailable"); return; }
                if (ActionIsDown(_cast.Skill.Key)) { CancelCast("player skill input"); return; }
                _cast.PreInputAnimation = preInputAnimation;
                if (_cast.Kind == CastKind.Multishot && preInputAnimation == AcdAnimationState.Running)
                    _cast.HoldMs = Math.Max(_cast.HoldMs, MultishotRunningSkillHoldMs);
                s7o_ZDH_HelperMetrics.LastPreInputAnimation = _cast.PreInputAnimation.ToString();
                _cast.ActionHeld = ActionDown(_cast.Skill.Key);
                if (!_cast.ActionHeld) { CancelCast("input failed"); return; }
                _cast.InputSent = true;
                _cast.InputDownTick = now;
                s7o_ZDH_HelperMetrics.LastInputDownMs = Elapsed(_cast.StartedTick, now);
                _cast.Stage = CastStage.Hold;
                _cast.DueTick = unchecked(now + _cast.HoldMs);
                return;
            }

            if (_cast.Stage == CastStage.Hold)
            {
                if (!Reached(now, _cast.DueTick)) return;
                ReleaseActionInput();
                _cast.InputUpTick = now;
                s7o_ZDH_HelperMetrics.LastInputUpMs = Elapsed(_cast.StartedTick, now);
                RememberGroundCastInput(now);
                BeginCursorRestore(now);
                return;
            }

            if (_cast.Stage == CastStage.Restore)
            {
                AdvanceCursorRestore(now);
                return;
            }

            if (_cast.Stage == CastStage.RestoreSettle)
            {
                if (!Reached(now, _cast.DueTick)) return;
                if (GuardCursorRestoreSettle(now)) return;
                BeginVerificationAfterRestore(now);
                return;
            }

            if (_cast.Stage == CastStage.Verify)
            {
                if (CastVerified())
                {
                    s7o_ZDH_HelperMetrics.LastEffectMs = _cast.InputDownTick == int.MinValue ? -1 : Elapsed(_cast.InputDownTick, now);
                    FinishCast("verified", now);
                    return;
                }
                if (Reached(now, _cast.VerifyUntilTick)) FinishCast("unverified", now);
            }
        }

        private bool CastVerified()
        {
            IMonster target = FindMonster(_cast.TargetAcd);
            _cast.LastAppliedCount = 0;
            if (_cast.Kind == CastKind.Entangle)
            {
                bool entangleApplied = !_cast.BaselineTargetFlag && target != null && HasEntangle(target);
                bool entangleAnimation = _cast.BaselineTargetFlag && _cast.SawCastAnimation;
                bool entangleVerified = entangleApplied || entangleAnimation;
                if (entangleVerified) s7o_ZDH_HelperMetrics.LastVerificationSource = entangleApplied ? "debuff" : "animation";
                _cast.LastAppliedCount = entangleVerified ? 1 : 0;
                return entangleVerified;
            }

            if (_cast.Kind == CastKind.MarkedForDeath)
            {
                int importantApplied = _cast.VerifyImportantAcds.Count(acd =>
                {
                    IMonster planned = FindMonster(acd);
                    return planned != null && planned.MarkedForDeath;
                });
                int markedAppliedCount = _cast.VerifyTargetAcds.Count(acd =>
                {
                    IMonster planned = FindMonster(acd);
                    return planned != null && planned.MarkedForDeath;
                });
                _cast.LastAppliedCount = _cast.VerifyImportantAcds.Count > 0 ? importantApplied : markedAppliedCount;

                bool nativeConfirmed = HasNewMfdActorNearExpected();
                if (nativeConfirmed)
                {
                    s7o_ZDH_HelperMetrics.LastVerificationSource = "new native ground actor";
                    return true;
                }

                s7o_ZDH_HelperMetrics.LastVerificationSource = _cast.LastAppliedCount > _cast.BaselineImportantApplied
                    ? "coverage changed / awaiting native actor"
                    : "awaiting native actor";
                return false;
            }

            if (_cast.Kind == CastKind.Sentry)
            {
                int currentCharges = _cast.Skill == null ? -1 : _cast.Skill.Charges;
                int currentOwned = GetOnScreenOwnedSentries().Count;

                IActor spawned = FindNewNativeOwnedSentryActor();
                if (spawned != null && spawned.FloorCoordinate != null
                    && !float.IsNaN(_cast.ExpectedWorldX) && !float.IsNaN(_cast.ExpectedWorldY))
                {
                    float spawnError = Distance2D(spawned.FloorCoordinate.X, spawned.FloorCoordinate.Y,
                        _cast.ExpectedWorldX, _cast.ExpectedWorldY);
                    float relocationTolerance = Math.Max(6f, SentryRejectedPositionRadius);
                    _cast.SentryRelocated = spawnError > relocationTolerance;
                    s7o_ZDH_HelperMetrics.LastVerificationSource = _cast.SentryRelocated
                        ? "ground actor relocated" : "ground actor";
                    _cast.LastAppliedCount = 1;
                    return true;
                }

                if (_cast.BaselineCharges >= 0 && currentCharges >= 0 && currentCharges < _cast.BaselineCharges)
                {
                    s7o_ZDH_HelperMetrics.LastVerificationSource = "charge consumed";
                    _cast.LastAppliedCount = 1;
                    return true;
                }
                if (currentOwned > _cast.BaselineOwnedSentries)
                {
                    s7o_ZDH_HelperMetrics.LastVerificationSource = "owned count";
                    _cast.LastAppliedCount = 1;
                    return true;
                }
                if (HasNewRelevantActorNearExpected(_cast.Kind))
                {
                    s7o_ZDH_HelperMetrics.LastVerificationSource = "ground actor";
                    _cast.LastAppliedCount = 1;
                    return true;
                }

                IPlayer player = FindPlayer(_cast.TargetAcd);
                if (!_cast.BaselineTargetFlag && player != null && CoveredByOwnedSentry(player))
                {
                    s7o_ZDH_HelperMetrics.LastVerificationSource = "player coverage";
                    _cast.LastAppliedCount = 1;
                    return true;
                }
                return false;
            }
            return false;
        }

        private void FinishCast(string result, int now)
        {
            bool sentryBurstChild = _cast.SentryBurstChild
                && _sentryBurst.Mode != SentryBurstMode.None;
            CastKind finishedKind = _cast.Kind;
            ReleaseActionInput();
            if (!sentryBurstChild)
            {
                ReleaseStandstillInput();
                ReleaseDhStrafePause();
            }
            if (s7o_ZDH_HelperMetrics.LastVerificationSource == "pending")
                s7o_ZDH_HelperMetrics.LastVerificationSource = result == "verified" ? "state" : "none";
            s7o_ZDH_HelperMetrics.LastResult = result;
            s7o_ZDH_HelperMetrics.LastTimingSequence++;
            if (finishedKind == CastKind.MarkedForDeath)
            {
                if (_cast.InputSent
                    && string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase))
                    _lastMfdSetupHandoffTick = now;
                else if (_cast.InputSent)
                    _lastMfdSetupHandoffTick = int.MinValue;

                if (_cast.InputSent
                    && string.Equals(result, "unverified", StringComparison.OrdinalIgnoreCase))
                    _lastUnverifiedMfdTick = now;
                else
                    _lastUnverifiedMfdTick = int.MinValue;
            }

            if (finishedKind == CastKind.Sentry)
            {
                if (_cast.InputSent && _cast.SentryCoverageAcds.Count > 0)
                    RecordEliteSentryCoverageAttempt(_cast.SentryCoverageAcds, now);
                if (string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase))
                {
                    ClearSentryRetry();
                    if (_cast.SentryRelocated)
                        MarkRejectedSentryPosition(now, "native relocated");
                    else
                        ClearRejectedSentryPositionNear(_cast.ExpectedWorldX, _cast.ExpectedWorldY);
                }
                else if (_cast.InputSent)
                {
                    MarkRejectedSentryPosition(now, "native unverified");
                    SetSentryRetry(now, SentryFailedRetryMs, "native unverified / replan");
                }
                PublishRejectedSentryPositions(now);
            }

            if (finishedKind == CastKind.MarkedForDeath
                && !string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase))
            {
                _urgentRetryKind = finishedKind;
                _urgentRetryTick = now;
            }
            else if (result == "verified" && _urgentRetryKind == finishedKind)
            {
                _urgentRetryKind = CastKind.None;
                _urgentRetryTick = int.MinValue;
            }

            _lastSupportKind = finishedKind;
            if (sentryBurstChild)
            {
                bool verified = string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase);
                ResetCast();
                OnSentryBurstChildFinished(verified, now);
                CompleteBossEntangleStandstillRelease();
                return;
            }
            _lastCastFinishedTick = now;
            ResetCast();
            CompleteBossEntangleStandstillRelease();
        }

        private void CancelCast(string reason)
        {
            if (_cast.Stage == CastStage.Idle) return;
            int now = Environment.TickCount;
            bool sentryBurstChild = _cast.SentryBurstChild
                && _sentryBurst.Mode != SentryBurstMode.None;
            CastKind cancelledKind = _cast.Kind;
            ReleaseActionInput();
            bool restored = RestoreCursorImmediately();
            if (!sentryBurstChild) ReleaseStandstillInput();
            if (restored && !sentryBurstChild)
            {
                _lastPauseReleasedTick = now;
                int primaryQuietMs = _cast.InputSent && _cast.RequiresStrafePause
                    ? GetPostCastPrimaryQuietMs(_cast.Kind) : 0;
                if (primaryQuietMs > 0) SuppressDhStrafePrimary(primaryQuietMs);
                ReleaseDhStrafePause();
            }
            else if (!restored && !sentryBurstChild)
                RequestDhStrafePause(Math.Max(120, CursorSafetyRecoveryMs + 120));
            if (s7o_ZDH_HelperMetrics.LastVerificationSource == "pending")
                s7o_ZDH_HelperMetrics.LastVerificationSource = "none";
            s7o_ZDH_HelperMetrics.LastResult = restored
                ? "cancelled: " + reason
                : "cancelled: " + reason + " / cursor restore failed";
            s7o_ZDH_HelperMetrics.LastLeaseDurationMs = Elapsed(_cast.StartedTick, now);
            s7o_ZDH_HelperMetrics.LastTimingSequence++;
            if (cancelledKind == CastKind.Multishot || cancelledKind == CastKind.MarkedForDeath)
            {
                _urgentRetryKind = cancelledKind;
                _urgentRetryTick = now;
            }
            else if (cancelledKind == CastKind.Sentry
                && !string.Equals(reason, "context", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "new area", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "strafe off", StringComparison.OrdinalIgnoreCase))
            {
                if (_cast.InputSent && _cast.SentryCoverageAcds.Count > 0)
                    RecordEliteSentryCoverageAttempt(_cast.SentryCoverageAcds, now);
                bool userOverride = string.Equals(reason, "aim displaced", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(reason, "aim reassert failed", StringComparison.OrdinalIgnoreCase);
                if (_cast.InputSent && !userOverride)
                    MarkRejectedSentryPosition(now, reason);
                SetSentryRetry(now, userOverride ? SentryUserOverrideRetryMs : SentryFailedRetryMs,
                    userOverride ? "user steering" : reason);
            }
            _lastSupportKind = cancelledKind;
            if (sentryBurstChild)
            {
                ResetCast();
                EndSentryBurst("child cancelled: " + reason, now);
                CompleteBossEntangleStandstillRelease();
                return;
            }
            _lastCastFinishedTick = now;
            ResetCast();
            CompleteBossEntangleStandstillRelease();
        }

        private void ResetCast()
        {
            _cast.Kind = CastKind.None;
            _cast.Stage = CastStage.Idle;
            _cast.Skill = null;
            _cast.TargetAcd = 0;
            _cast.ActionHeld = false;
            _cast.StandstillHeld = false;
            _cast.CursorOwned = false;
            _cast.SavedHeroScreenX = 0;
            _cast.SavedHeroScreenY = 0;
            _cast.SavedHeroScreenValid = false;
            _cast.CursorReferenceX = 0;
            _cast.CursorReferenceY = 0;
            _cast.CursorReferenceValid = false;
            _cast.UserCursorDeltaX = 0;
            _cast.UserCursorDeltaY = 0;
            _cast.UserCursorDeltaSamples = 0;
            _cast.RestoreX = 0;
            _cast.RestoreY = 0;
            _cast.RestoreDeadlineTick = int.MinValue;
            _cast.RestoreAttempts = 0;
            _cast.RestoreGuardCorrections = 0;
            _cast.BaselineTargetFlag = false;
            _cast.SawCastAnimation = false;
            _cast.SawNativeMultishotAnimation = false;
            _cast.PauseAckAnimation = default(AcdAnimationState);
            _cast.PreInputAnimation = default(AcdAnimationState);
            _cast.TrashInitialMultishot = false;
            _cast.InputSent = false;
            _cast.RequiresStrafePause = false;
            _cast.BossStandalone = false;
            _cast.AimCorrections = 0;
            _cast.MaxAimDrift = 0;
            _cast.BaselineCharges = -1;
            _cast.BaselineOwnedSentries = 0;
            _cast.VerifyRequiredCount = 0;
            _cast.VerifyPrimaryRequired = false;
            _cast.BaselineImportantApplied = 0;
            _cast.RequiredImportantApplied = 0;
            _cast.BaselineMfdActorAcd = 0;
            _cast.BaselineMfdActorCreatedTick = 0;
            _cast.BaselineMfdGameTick = 0;
            _cast.BaselineActorAcds.Clear();
            _cast.VerifyTargetAcds.Clear();
            _cast.VerifyImportantAcds.Clear();
            _cast.MultishotCoveredEliteAcds.Clear();
            _cast.MultishotBaselineActiveAcds.Clear();
            _cast.SentryCoverageAcds.Clear();
            _cast.ExpectedWorldX = float.NaN;
            _cast.ExpectedWorldY = float.NaN;
            _cast.PauseAckTick = int.MinValue;
            _cast.AimReadyTick = int.MinValue;
            _cast.InputDownTick = int.MinValue;
            _cast.InputUpTick = int.MinValue;
            _cast.RestoreTick = int.MinValue;
            _cast.AimSettleMs = 0;
            _cast.HoldMs = 0;
            _cast.MinimumLeaseMs = 0;
            _cast.VerifyMs = 0;
            _cast.LastAppliedCount = 0;
            _cast.EfficientMultishot = false;
            _cast.SentryMfdAnchor = false;
            _cast.SentrySlot = 0;
            _cast.SentryFallback = false;
            _cast.SentryCastDistance = 0;
            _cast.SentryFallbackReason = string.Empty;
            _cast.SentryRelocated = false;
            _cast.SentryBurstChild = false;
            _cast.Label = string.Empty;
        }

        private void ReleaseActionInput()
        {
            if (_cast.ActionHeld && _cast.Skill != null) ActionUp(_cast.Skill.Key);
            _cast.ActionHeld = false;
        }

        private void ReleaseStandstillInput()
        {
            if (_cast.StandstillHeld && ForceStandstillVirtualKey != 0) ZdhInput.KeyUp(ForceStandstillVirtualKey);
            _cast.StandstillHeld = false;
        }

        private void UpdateTargetStates(int now)
        {
            var seen = new HashSet<uint>();
            foreach (IMonster monster in Hud.Game.AliveMonsters)
            {
                if (!IsAutomationBody(monster)) continue;
                seen.Add(monster.AcdId);
                bool iceblink = HasIceblink(monster);
                TargetState state;
                if (!_targets.TryGetValue(monster.AcdId, out state))
                {
                    int firstObservedAge = Math.Max(0, GetIceblinkRefreshAgeMs() - Math.Max(100, IceblinkFirstObservedGraceMs));
                    state = new TargetState
                    {
                        Health = monster.CurHealth,
                        IceblinkActive = iceblink,
                        IceblinkConfirmedTick = iceblink ? unchecked(now - firstObservedAge) : int.MinValue,
                    };
                    _targets[monster.AcdId] = state;
                }
                else if (iceblink && !state.IceblinkActive)
                {
                    state.IceblinkConfirmedTick = now;
                    state.PendingIceblinkRefreshTick = int.MinValue;
                    state.PendingIceblinkAttemptCount = 0;
                }
                else if (iceblink && state.IceblinkActive
                    && state.IceblinkConfirmedTick != int.MinValue
                    && state.PendingIceblinkRefreshTick != int.MinValue
                    && Elapsed(state.IceblinkConfirmedTick, now)
                        >= Math.Max(1000, IceblinkExpectedDurationMs + IceblinkValidationSlackMs))
                {
                    state.IceblinkConfirmedTick = state.PendingIceblinkRefreshTick;
                    state.PendingIceblinkRefreshTick = int.MinValue;
                    state.PendingIceblinkAttemptCount = 0;
                }
                else if (!iceblink)
                {
                    state.PendingIceblinkRefreshTick = int.MinValue;
                    state.PendingIceblinkAttemptCount = 0;
                }

                if (monster.CurHealth + Math.Max(1.0, monster.MaxHealth * 0.000001) < state.Health)
                    state.LastDamageTick = now;
                state.Health = monster.CurHealth;
                state.LastSeenTick = now;
                state.IceblinkActive = iceblink;
            }
            foreach (uint key in _targets.Where(kv => !seen.Contains(kv.Key) && Elapsed(kv.Value.LastSeenTick, now) > 5000).Select(kv => kv.Key).ToList())
                _targets.Remove(key);

        }

        private void SampleUptime(int now)
        {
            if (_lastSampleTick == int.MinValue)
            {
                _lastSampleTick = now;
                return;
            }

            int elapsed = Elapsed(_lastSampleTick, now);
            if (elapsed < Math.Max(1, SampleIntervalMs)) return;
            int sampleMs = Math.Min(500, elapsed);
            _lastSampleTick = now;

            ZdhLoadout zdh = GetPartyZdhLoadouts().FirstOrDefault(x => x.QualifiesForDisplay);
            if (zdh == null || zdh.Player == null || !zdh.Player.InCombat || zdh.Player.FloorCoordinate == null) return;

            List<IMonster> eligible = Hud.Game.AliveMonsters.Where(monster =>
                    IsStatusTarget(monster) && !IsJuggernaut(monster) && !monster.Invulnerable && monster.Attackable
                    && Distance(zdh.Player, monster) <= ZdhParticipationRange
                    && IsEngaged(GetTargetState(monster, now), now))
                .ToList();

            foreach (IMonster monster in eligible)
            {
                s7o_ZDH_HelperMetrics.EligibleMilliseconds += sampleMs;
                if (HasIceblink(monster)) s7o_ZDH_HelperMetrics.IceblinkMilliseconds += sampleMs;
                if (HasEntangle(monster)) s7o_ZDH_HelperMetrics.DamageMilliseconds += sampleMs;
            }

            if (eligible.Count > 0)
            {
                s7o_ZDH_HelperMetrics.MarkedForDeathEligibleMilliseconds += sampleMs;
                if (eligible.Any(monster => monster.MarkedForDeath))
                    s7o_ZDH_HelperMetrics.MarkedForDeathMilliseconds += sampleMs;
            }
        }

        private bool IsImmediatePrimaryEliteEncounter(IMonster monster, IPlayer zdh)
        {
            if (monster == null || zdh == null || zdh.FloorCoordinate == null) return false;
            return IsStatusTarget(monster) && !IsJuggernaut(monster)
                && !monster.Invulnerable && monster.Attackable && monster.IsOnScreen
                && monster.FloorCoordinate != null
                && Distance(zdh, monster) <= Math.Max(10f, Math.Min(AutomationRange, EliteEncounterRange));
        }

        private bool IsImmediateGroundSupportEncounter(IMonster monster, IPlayer zdh)
        {
            if (monster == null || zdh == null || zdh.FloorCoordinate == null
                || !IsGroundSupportElite(monster) || !monster.IsOnScreen) return false;
            bool specialState = monster.Invulnerable || monster.Burrowed || monster.Untargetable;
            if (specialState && !s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                && !_speedCombatEngaged && !_bossStandaloneActive) return false;
            if (!monster.Attackable && !specialState) return false;
            return Distance(zdh, monster) <= Math.Max(10f, Math.Min(AutomationRange, EliteEncounterRange));
        }

        private List<IMonster> GetActiveGroundSupportPrimaryElites(IPlayer zdh, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null) return new List<IMonster>();
            IMonster selected = Hud.Game.SelectedMonster2;
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsGroundSupportPrimaryElite(m) && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((_bossStandaloneActive && m.Rarity == ActorRarity.Boss)
                        || IsImmediateGroundSupportEncounter(m, zdh)
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
                        || (selected != null && SameMonster(selected, m))
                        || (focus != null && SameMonster(focus, m))))
                .ToList();
        }

        private List<IMonster> GetActiveGroundSupportMfdOnlyTargets(IPlayer zdh, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null) return new List<IMonster>();
            IMonster selected = Hud.Game.SelectedMonster2;
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsGroundSupportMfdOnlyTarget(m) && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((IsImmediateGroundSupportEncounter(m, zdh)
                            && (s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                                || _speedCombatEngaged || _bossStandaloneActive))
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
                        || (selected != null && SameMonster(selected, m))
                        || (focus != null && SameMonster(focus, m))))
                .ToList();
        }

        private List<IMonster> GetActivePrimaryElites(IPlayer zdh, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null) return new List<IMonster>();
            IMonster selected = Hud.Game.SelectedMonster2;
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsStatusTarget(m) && !IsJuggernaut(m)
                    && !m.Invulnerable && m.Attackable && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((_bossStandaloneActive && m.Rarity == ActorRarity.Boss)
                        || IsImmediatePrimaryEliteEncounter(m, zdh)
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
                        || (selected != null && SameMonster(selected, m))
                        || (focus != null && SameMonster(focus, m))))
                .ToList();
        }

        private List<IMonster> GetActiveMfdSupportTargets(IPlayer zdh, int now)
        {
            return MergeMonsters(GetActiveGroundSupportPrimaryElites(zdh, now),
                GetActiveGroundSupportMfdOnlyTargets(zdh, now));
        }

        private static List<IMonster> MergeMonsters(IEnumerable<IMonster> first, IEnumerable<IMonster> second)
        {
            var result = new List<IMonster>();
            var seen = new HashSet<uint>();
            foreach (IMonster monster in (first ?? Enumerable.Empty<IMonster>()).Concat(second ?? Enumerable.Empty<IMonster>()))
                if (monster != null && seen.Add(monster.AcdId)) result.Add(monster);
            return result;
        }

        private List<IMonster> GetActiveCombatBodies(IPlayer zdh, int now, float range)
        {
            IMonster selected = Hud.Game.SelectedMonster2;
            IMonster focus = GetPartyFocusMonster(now);
            var groundSupportAcds = new HashSet<uint>(GetActiveMfdSupportTargets(zdh, now).Select(m => m.AcdId));
            List<IMonster> valid = Hud.Game.AliveMonsters.Where(m => m != null && m.IsAlive
                    && m.FloorCoordinate != null && m.Rarity != ActorRarity.RareMinion
                    && !m.Illusion && !m.Hidden && !m.Stealthed && !m.Invisible && m.IsOnScreen
                    && Distance(zdh, m) <= range
                    && (groundSupportAcds.Contains(m.AcdId)
                        || (IsAutomationBody(m) && !IsJuggernaut(m) && !m.Invulnerable && m.Attackable)))
                .ToList();
            if (valid.Count == 0) return valid;

            List<IMonster> anchors = valid.Where(m =>
                    groundSupportAcds.Contains(m.AcdId)
                    || (IsEngaged(GetTargetState(m, now), now) && !IsJuggernaut(m))
                    || IsImmediatePrimaryEliteEncounter(m, zdh)
                    || (selected != null && SameMonster(selected, m) && !IsJuggernaut(m))
                    || (focus != null && SameMonster(focus, m)))
                .ToList();
            if (anchors.Count == 0) return new List<IMonster>();

            return valid.Where(m => anchors.Any(a => SameMonster(a, m)
                    || a.FloorCoordinate.XYDistanceTo(m.FloorCoordinate) <= CombatBodyNearAnchorRadius + GetMonsterRadiusBottom(m)))
                .ToList();
        }

        private bool IsCombatIntentTrash(CombatCluster cluster)
        {
            return cluster != null
                && s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                && cluster.Elites.Count == 0
                && cluster.MfdOnlyTargets.Count == 0
                && cluster.RecentDamageCount > 0
                && cluster.Bodies.Any(monster => monster != null && IsDebuffBody(monster)
                    && monster.Attackable && !monster.Invulnerable && monster.IsOnScreen);
        }

        private CombatCluster BuildBestCombatCluster(List<IMonster> bodies, int now)
        {
            if (bodies == null || bodies.Count == 0) return null;
            IMonster selected = Hud.Game.SelectedMonster2;
            IMonster focus = GetPartyFocusMonster(now);
            bool sustainedSpecialFocus = focus != null && IsJuggernaut(focus) && IsPartyFocusSustained(now);
            var groundSupportAcds = new HashSet<uint>(GetActiveMfdSupportTargets(Hud.Game.Me, now).Select(m => m.AcdId));
            CombatCluster best = null;

            IPlayer localPlayer = Hud.Game.Me;
            foreach (IMonster anchor in bodies)
            {
                bool anchorEngaged = IsEngaged(GetTargetState(anchor, now), now) && !IsJuggernaut(anchor);
                bool anchorEncountered = IsImmediatePrimaryEliteEncounter(anchor, localPlayer);
                bool anchorGroundSupport = groundSupportAcds.Contains(anchor.AcdId);
                bool anchorSelected = selected != null && SameMonster(selected, anchor) && !IsJuggernaut(anchor);
                bool anchorFocused = focus != null && SameMonster(focus, anchor);
                if (!anchorEngaged && !anchorEncountered && !anchorGroundSupport && !anchorSelected && !anchorFocused) continue;

                var cluster = new CombatCluster
                {
                    FocusTarget = focus,
                    SustainedSpecialFocus = sustainedSpecialFocus,
                };
                foreach (IMonster body in bodies)
                {
                    if (anchor.FloorCoordinate.XYDistanceTo(body.FloorCoordinate) > CombatClusterRadius + GetMonsterRadiusBottom(body)) continue;
                    cluster.Bodies.Add(body);
                    if (IsGroundSupportMfdOnlyTarget(body)) cluster.MfdOnlyTargets.Add(body);
                    else if (IsGroundSupportPrimaryElite(body)) cluster.Elites.Add(body);
                    if (IsEngaged(GetTargetState(body, now), now)) cluster.RecentDamageCount++;
                    cluster.Score += CombatBodyWeight(body, now);
                    if (focus != null && SameMonster(focus, body)) cluster.Score += 24.0;
                }

                cluster.PriorityEliteCount = cluster.Elites.Count(m => groundSupportAcds.Contains(m.AcdId)
                    || IsEngaged(GetTargetState(m, now), now)
                    || IsImmediatePrimaryEliteEncounter(m, localPlayer)
                    || (selected != null && SameMonster(selected, m))
                    || (focus != null && SameMonster(focus, m)));
                bool engagedElite = cluster.PriorityEliteCount > 0;
                bool densityFight = cluster.Bodies.Count >= TrashClusterMinBodies
                    && cluster.RecentDamageCount >= TrashClusterMinDamagedBodies;
                bool combatIntentTrash = IsCombatIntentTrash(cluster);
                bool mfdOnlyFight = cluster.MfdOnlyTargets.Count > 0;
                if (!engagedElite && !densityFight && !combatIntentTrash && !mfdOnlyFight) continue;

                cluster.Score += cluster.Bodies.Count * 0.5;
                bool better = best == null
                    || cluster.PriorityEliteCount > best.PriorityEliteCount
                    || (cluster.PriorityEliteCount == best.PriorityEliteCount && cluster.Elites.Count > best.Elites.Count)
                    || (cluster.PriorityEliteCount == best.PriorityEliteCount && cluster.Elites.Count == best.Elites.Count
                        && cluster.MfdOnlyTargets.Count > best.MfdOnlyTargets.Count)
                    || (cluster.PriorityEliteCount == best.PriorityEliteCount && cluster.Elites.Count == best.Elites.Count
                        && cluster.MfdOnlyTargets.Count == best.MfdOnlyTargets.Count && cluster.Score > best.Score);
                if (better) best = cluster;
            }

            if (best == null || best.Bodies.Count == 0) return null;
            FinalizeCombatCluster(best, now);
            s7o_ZDH_HelperMetrics.LastMfdOnlyTargets = best.MfdOnlyTargets.Count;
            return best;
        }

        private void FinalizeCombatCluster(CombatCluster cluster, int now)
        {
            double total = 0;
            double x = 0;
            double y = 0;
            double z = 0;
            foreach (IMonster body in cluster.Bodies)
            {
                double weight = Math.Max(1.0, MfdTargetWeight(body, now));
                total += weight;
                x += body.FloorCoordinate.X * weight;
                y += body.FloorCoordinate.Y * weight;
                z += body.FloorCoordinate.Z * weight;
            }
            if (total <= 0) return;

            float cx = (float)(x / total);
            float cy = (float)(y / total);
            float cz = (float)(z / total);
            if (!_packCandidateValid || Distance2D(_packCandidateX, _packCandidateY, cx, cy) > 5f)
            {
                _packCandidateX = cx;
                _packCandidateY = cy;
                _packCandidateZ = cz;
                _packCandidateTick = now;
                _packCandidateValid = true;
            }
            else
            {
                _packCandidateX = _packCandidateX * 0.72f + cx * 0.28f;
                _packCandidateY = _packCandidateY * 0.72f + cy * 0.28f;
                _packCandidateZ = _packCandidateZ * 0.72f + cz * 0.28f;
            }

            cluster.CenterX = _packCandidateX;
            cluster.CenterY = _packCandidateY;
            cluster.CenterZ = _packCandidateZ;
            cluster.Stable = Elapsed(_packCandidateTick, now) >= CombatClusterStableMs;

            float bestDistance2 = 0;
            for (int i = 0; i < cluster.Bodies.Count; i++)
                for (int j = i + 1; j < cluster.Bodies.Count; j++)
                {
                    float dx = cluster.Bodies[j].FloorCoordinate.X - cluster.Bodies[i].FloorCoordinate.X;
                    float dy = cluster.Bodies[j].FloorCoordinate.Y - cluster.Bodies[i].FloorCoordinate.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= bestDistance2) continue;
                    bestDistance2 = d2;
                    cluster.AxisX = dx;
                    cluster.AxisY = dy;
                }
            if (!NormalizeDirection(ref cluster.AxisX, ref cluster.AxisY))
            {
                cluster.AxisX = 1f;
                cluster.AxisY = 0f;
            }

            float px = -cluster.AxisY;
            float py = cluster.AxisX;
            foreach (IMonster body in cluster.Bodies)
            {
                float rx = body.FloorCoordinate.X - cluster.CenterX;
                float ry = body.FloorCoordinate.Y - cluster.CenterY;
                cluster.MajorExtent = Math.Max(cluster.MajorExtent, Math.Abs(rx * cluster.AxisX + ry * cluster.AxisY));
                cluster.MinorExtent = Math.Max(cluster.MinorExtent, Math.Abs(rx * px + ry * py));
            }
        }

        private TargetState GetTargetState(IMonster monster, int now)
        {
            TargetState state;
            if (!_targets.TryGetValue(monster.AcdId, out state))
            {
                state = new TargetState { Health = monster.CurHealth, LastSeenTick = now };
                _targets[monster.AcdId] = state;
            }
            return state;
        }

        private bool IsEngaged(TargetState state, int now)
        {
            return WasRecentlyDamaged(state, now, RecentDamageWindowMs);
        }

        private bool WasRecentlyDamaged(TargetState state, int now, int windowMs)
        {
            return state != null && state.LastDamageTick != int.MinValue
                && Elapsed(state.LastDamageTick, now) <= Math.Max(0, windowMs);
        }

        private double TargetPriority(IMonster monster, bool missing)
        {
            double score = missing ? 600 : 0;
            if (SameMonster(Hud.Game.SelectedMonster2, monster)) score += 1000;
            if (IsCurrentPartyFocus(monster, Environment.TickCount)) score += 900;
            if (monster.Rarity == ActorRarity.Boss) score += 800;
            else if (monster.Rarity == ActorRarity.Rare || monster.Rarity == ActorRarity.Unique) score += 350;
            else if (monster.Rarity == ActorRarity.Champion) score += 300;
            if (monster.MaxHealth > 0) score += (1.0 - monster.CurHealth / monster.MaxHealth) * 120.0;
            score -= monster.NormalizedXyDistanceToMe * 0.4;
            return score;
        }

        private List<ZdhLoadout> GetPartyZdhLoadouts()
        {
            var result = new List<ZdhLoadout>();
            foreach (IPlayer player in Hud.Game.Players)
            {
                ZdhLoadout loadout = BuildLoadout(player);
                if (loadout != null && loadout.Player != null) result.Add(loadout);
            }
            return result.OrderByDescending(x => x.QualifiesForDisplay).ThenBy(x => x.Player.PortraitIndex).ToList();
        }

        private ZdhLoadout BuildLoadout(IPlayer player)
        {
            if (player == null || !player.HasValidActor || player.IsDead || player.HeroClassDefinition == null || player.HeroClassDefinition.HeroClass != HeroClass.DemonHunter || player.Powers == null)
                return null;
            var l = new ZdhLoadout { Player = player };
            try
            {
                l.Entangle = player.Powers.UsedSkills.FirstOrDefault(s => SkillSno(s) == EntanglingShotSno);
                l.Multishot = player.Powers.UsedSkills.FirstOrDefault(s => SkillSno(s) == MultishotSno);
                l.MarkedForDeath = player.Powers.UsedSkills.FirstOrDefault(s => SkillSno(s) == MarkedForDeathSno);
                l.Sentry = player.Powers.UsedSkills.FirstOrDefault(s => SkillSno(s) == SentrySno);
                l.Odyssey = BuffActive(player.Powers.UsedLegendaryPowers.OdysseysEnd);
                l.Iceblink = BuffActive(player.Powers.UsedLegendaryGems.IceblinkPrimary) || BuffActive(player.Powers.UsedLegendaryGems.IceblinkSecondary);
                l.WindChill = RuneContains(l.Multishot, "Wind Chill");
                l.Valley = l.MarkedForDeath != null && (l.MarkedForDeath.Rune == 2 || RuneContains(l.MarkedForDeath, "Valley of Death"));
                l.Guardian = l.Sentry != null && (l.Sentry.Rune == 4 || RuneContains(l.Sentry, "Guardian Turret"));
                l.CustomEngineering = player.Powers.UsedPassives.Any(x => x != null && x.Sno == 208610);
                l.BombardiersRucksack = BuffActive(player.Powers.UsedLegendaryPowers.BombardiersRucksack)
                    || player.Powers.BuffIsActive(LegacyBombardiersRucksackSno, 0);
            }
            catch { }
            return l;
        }

        private void DrawDebuffTokens(IMonster monster, bool ib, bool dmg, bool mfd)
        {
            IScreenCoordinate sc = null;
            try { sc = monster.FloorCoordinate == null ? monster.ScreenCoordinate : monster.FloorCoordinate.ToScreenCoordinate(false, true); }
            catch { sc = monster.ScreenCoordinate; }
            if (sc == null) return;
            string[] texts = { "IB", "DMG", "MFD" };
            bool[] states = { ib, dmg, mfd };
            float[] widths = new float[3];
            float total = 0;
            for (int i = 0; i < 3; i++)
            {
                IFont font = states[i] ? _greenFont : _redFont;
                widths[i] = font.GetTextLayout(texts[i]).Metrics.Width;
                total += widths[i];
            }
            total += 12f;
            float x = sc.X - total * 0.5f;
            float y = sc.Y + 33f;
            for (int i = 0; i < 3; i++)
            {
                IFont font = states[i] ? _greenFont : _redFont;
                font.DrawText(texts[i], x, y);
                x += widths[i] + 6f;
            }
        }

        private void DrawPortraitHint(ZdhLoadout loadout)
        {
            if (loadout == null || loadout.Player == null || loadout.Player.PortraitUiElement == null
                || !loadout.QualifiesForDisplay || _tooltipLabelFont == null) return;

            RectangleF r;
            try { r = loadout.Player.PortraitUiElement.Rectangle; }
            catch { return; }
            if (r.Width <= 0 || r.Height <= 0) return;

            int iceblink = s7o_ZDH_HelperMetrics.Percent(s7o_ZDH_HelperMetrics.IceblinkMilliseconds);
            int odyssey = s7o_ZDH_HelperMetrics.Percent(s7o_ZDH_HelperMetrics.DamageMilliseconds);
            int mark = s7o_ZDH_HelperMetrics.MarkedForDeathPercent();
            int average = s7o_ZDH_HelperMetrics.AveragePercent();

            DrawPortraitAverage(r, average);

            float cursorX = Hud.Window.CursorX;
            float cursorY = Hud.Window.CursorY;
            if (cursorX < r.Left || cursorX > r.Right || cursorY < r.Top || cursorY > r.Bottom) return;

            string[] labels =
            {
                T("zdh.tooltip.iceblink", "Iceblink") + ":",
                T("zdh.tooltip.odyssey", "Odyssey") + ":",
                T("zdh.tooltip.mark", "Mark") + ":",
                T("zdh.tooltip.average", "Average Uptime") + ":",
            };
            int[] values = { iceblink, odyssey, mark, average };
            float[] labelWidths = labels.Select(label => _tooltipLabelFont.GetTextLayout(label).Metrics.Width).ToArray();
            float valueGap = 3f;
            float lineHeight = _tooltipLabelFont.GetTextLayout("Ag").Metrics.Height + 1f;
            float widestRow = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                IFont valueFont = TooltipUptimeFont(values[i]);
                widestRow = Math.Max(widestRow, labelWidths[i] + valueGap
                    + valueFont.GetTextLayout(values[i].ToString(CultureInfo.InvariantCulture) + "%").Metrics.Width);
            }

            float x = r.Right + 6f;
            if (x + widestRow > Hud.Window.Size.Width - 8f)
                x = Math.Max(8f, r.Left - widestRow - 6f);
            float y = r.Top;

            for (int i = 0; i < labels.Length; i++)
            {
                _tooltipLabelFont.DrawText(labels[i], x, y);
                TooltipUptimeFont(values[i]).DrawText(values[i].ToString(CultureInfo.InvariantCulture) + "%",
                    x + labelWidths[i] + valueGap, y);
                y += lineHeight;
            }
        }

        private void DrawPortraitAverage(RectangleF portrait, int average)
        {
            IFont font = UptimeFont(average);
            string text = average.ToString(CultureInfo.InvariantCulture) + "%";
            var layout = font.GetTextLayout(text);
            float iconHeight = Math.Max(36f, portrait.Width * 0.62f);
            float iconWidth = iconHeight * 0.50f;
            float iconX = portrait.X + 2f;
            float iconY = portrait.Y + portrait.Height * 0.42f;
            float minIconY = portrait.Y + 2f;
            float maxIconY = portrait.Bottom - iconHeight - 2f;
            if (maxIconY > minIconY)
                iconY = Math.Min(Math.Max(iconY, minIconY), maxIconY);
            float x = iconX + (iconWidth - layout.Metrics.Width) * 0.5f;
            float y = Math.Max(portrait.Top, iconY - layout.Metrics.Height - 1f);
            font.DrawText(layout, x, y);
        }

        private IFont UptimeFont(int percent)
        {
            return percent >= 80 ? _greenFont
                : percent >= 60 ? _yellowFont
                : percent >= 50 ? _orangeFont
                : _redFont;
        }

        private IFont TooltipUptimeFont(int percent)
        {
            return percent >= 80 ? _tooltipGreenFont
                : percent >= 60 ? _tooltipYellowFont
                : percent >= 50 ? _tooltipOrangeFont
                : _tooltipRedFont;
        }

        private sealed class Placement
        {
            public float WorldX, WorldY, WorldZ;
            public IScreenCoordinate Screen;
            public double Score;
            public double Priority;
            public uint TargetAcd;
            public string Label;
            public float SentryScale = 1f;
            public int SentrySlot;
            public bool SentryFallback;
            public string SentryFallbackReason;
            public int CoveredBodies;
            public int CoveredElites;
            public int CoveredBosses;
            public bool CoversFocus;
            public readonly List<uint> CoveredEliteAcds = new List<uint>();
        }

        private sealed class MultishotPlan
        {
            public IMonster Primary;
            public IScreenCoordinate Aim;
            public double Score;
            public int CoveredBodyCount;
            public int CoveredEliteCount;
            public int RequiredApplied;
            public bool PrimaryMustApply;
            public double MaxDueEliteAngleDegrees;
            public double AverageDueEliteAngleDegrees;
            public readonly List<uint> CoveredMissingAcds = new List<uint>();
            public readonly List<uint> CoveredEliteAcds = new List<uint>();
            public readonly List<uint> CoveredMissingEliteAcds = new List<uint>();
            public readonly List<uint> CoveredPrimaryEliteAcds = new List<uint>();
        }

        private sealed class DirectionCandidate
        {
            public float X;
            public float Y;
        }

        private IMonster FindBestEntangleTarget(CombatCluster cluster, bool missingElitePriority, int now)
        {
            if (cluster == null) return null;
            List<IMonster> candidates = missingElitePriority
                ? cluster.Bodies.Where(m => IsDebuffBody(m) && !HasEntangle(m) && IsImportantDebuffTarget(m)).ToList()
                : cluster.Bodies.Where(m => IsDebuffBody(m) && !HasEntangle(m)).ToList();
            if (candidates.Count == 0) candidates = cluster.Bodies.Where(IsDebuffBody).ToList();

            return candidates.OrderByDescending(m => EntangleTargetScore(m, cluster.Bodies))
                .ThenBy(m => ScreenDistanceToCursor(m))
                .FirstOrDefault();
        }

        private double EntangleTargetScore(IMonster target, List<IMonster> bodies)
        {
            if (target == null || target.FloorCoordinate == null) return double.MinValue;
            double score = IsStatusTarget(target) ? TargetPriority(target, !HasEntangle(target)) : CombatBodyWeight(target, Environment.TickCount);
            foreach (IMonster body in bodies)
            {
                if (body == null || body.FloorCoordinate == null || HasEntangle(body)) continue;
                if (body.FloorCoordinate.XYDistanceTo(target.FloorCoordinate) > 13f + GetMonsterRadiusBottom(body)) continue;
                score += IsStatusTarget(body) ? 45.0 : 8.0 + GetRiftProgression(body) * 8.0;
            }
            return score;
        }

        private double ScreenDistanceToCursor(IMonster monster)
        {
            try
            {
                if (monster == null || monster.ScreenCoordinate == null) return double.MaxValue;
                double dx = monster.ScreenCoordinate.X - Hud.Window.CursorX;
                double dy = monster.ScreenCoordinate.Y - Hud.Window.CursorY;
                return Math.Sqrt(dx * dx + dy * dy);
            }
            catch { return double.MaxValue; }
        }

        private Placement FindBestPlacement(List<IMonster> targets, int now)
        {
            if (targets == null || targets.Count == 0) return null;
            List<IMonster> ranked = targets.OrderByDescending(m => MfdTargetWeight(m, now)).Take(28).ToList();
            var candidates = new List<Placement>();

            double total = 0;
            double cx = 0;
            double cy = 0;
            double cz = 0;
            foreach (IMonster target in ranked)
            {
                double weight = Math.Max(1.0, MfdTargetWeight(target, now));
                total += weight;
                cx += target.FloorCoordinate.X * weight;
                cy += target.FloorCoordinate.Y * weight;
                cz += target.FloorCoordinate.Z * weight;
                AddPlacement(candidates, target.FloorCoordinate.X, target.FloorCoordinate.Y, target.FloorCoordinate.Z);
            }
            if (total > 0) AddPlacement(candidates, (float)(cx / total), (float)(cy / total), (float)(cz / total));

            for (int i = 0; i < ranked.Count; i++)
                for (int j = i + 1; j < ranked.Count; j++)
                {
                    float distance = ranked[i].FloorCoordinate.XYDistanceTo(ranked[j].FloorCoordinate);
                    if (distance > ValleyRadius * 2f + GetMonsterRadiusBottom(ranked[i]) + GetMonsterRadiusBottom(ranked[j])) continue;
                    AddPlacement(candidates,
                        (ranked[i].FloorCoordinate.X + ranked[j].FloorCoordinate.X) * 0.5f,
                        (ranked[i].FloorCoordinate.Y + ranked[j].FloorCoordinate.Y) * 0.5f,
                        (ranked[i].FloorCoordinate.Z + ranked[j].FloorCoordinate.Z) * 0.5f);
                    AddValleyIntersectionCandidates(candidates, ranked[i], ranked[j]);
                }

            foreach (Placement placement in candidates)
                ScorePlacement(placement, targets, now);

            bool bossPresent = targets.Any(m => m != null && m.Rarity == ActorRarity.Boss);
            return candidates
                .OrderByDescending(x => bossPresent ? x.CoveredBosses : 0)
                .ThenByDescending(x => x.CoveredElites)
                .ThenByDescending(x => x.CoversFocus)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.CoveredBodies)
                .FirstOrDefault();
        }

        private Placement FindBestJuggernautAnchoredPlacement(
            List<IMonster> juggernauts, List<IMonster> scoringTargets, int now)
        {
            if (juggernauts == null || juggernauts.Count == 0) return null;
            var candidates = new List<Placement>();
            foreach (IMonster jug in juggernauts)
            {
                if (jug == null || jug.FloorCoordinate == null || !jug.IsOnScreen
                    || !IsGroundSupportMfdOnlyTarget(jug)) continue;
                Placement placement = CreatePlacement(jug.FloorCoordinate.X, jug.FloorCoordinate.Y, jug.FloorCoordinate.Z);
                if (placement == null) continue;
                placement.TargetAcd = jug.AcdId;
                ScorePlacement(placement, scoringTargets, now);
                candidates.Add(placement);
            }
            return candidates
                .OrderByDescending(x => x.CoveredElites)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.CoveredBodies)
                .FirstOrDefault();
        }

        private void AddValleyIntersectionCandidates(List<Placement> candidates, IMonster first, IMonster second)
        {
            if (first == null || second == null || first.FloorCoordinate == null || second.FloorCoordinate == null) return;
            float x1 = first.FloorCoordinate.X;
            float y1 = first.FloorCoordinate.Y;
            float x2 = second.FloorCoordinate.X;
            float y2 = second.FloorCoordinate.Y;
            float dx = x2 - x1;
            float dy = y2 - y1;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance < 0.01f) return;

            float r1 = ValleyRadius + GetMonsterRadiusBottom(first);
            float r2 = ValleyRadius + GetMonsterRadiusBottom(second);
            if (distance > r1 + r2 || distance < Math.Abs(r1 - r2)) return;

            float a = (r1 * r1 - r2 * r2 + distance * distance) / (2f * distance);
            float h2 = r1 * r1 - a * a;
            if (h2 < -0.01f) return;
            float h = (float)Math.Sqrt(Math.Max(0, h2));
            float ux = dx / distance;
            float uy = dy / distance;
            float baseX = x1 + a * ux;
            float baseY = y1 + a * uy;
            float offsetX = -uy * h;
            float offsetY = ux * h;
            float z = (first.FloorCoordinate.Z + second.FloorCoordinate.Z) * 0.5f;
            AddPlacement(candidates, baseX + offsetX, baseY + offsetY, z);
            AddPlacement(candidates, baseX - offsetX, baseY - offsetY, z);
        }

        private void AddPlacement(List<Placement> list, float x, float y, float z)
        {
            IScreenCoordinate screen = Hud.Window.WorldToScreenCoordinate(x, y, z, false, true);
            if (screen != null && PointInsideCastArea(screen.X, screen.Y))
                list.Add(new Placement { WorldX = x, WorldY = y, WorldZ = z, Screen = screen });
        }

        private Placement CurrentValleyPlacement(List<IMonster> targets, int now)
        {
            IActor actor = FindAuthoritativeValleyActor();
            float x;
            float y;
            float z;

            if (actor != null)
            {
                _lastValleyX = actor.FloorCoordinate.X;
                _lastValleyY = actor.FloorCoordinate.Y;
                _lastValleyActorSeenTick = now;
                x = _lastValleyX;
                y = _lastValleyY;
                z = actor.FloorCoordinate.Z;
            }
            else
            {
                int dropoutMs = _lastValleyActorSeenTick == int.MinValue
                    ? int.MaxValue : Elapsed(_lastValleyActorSeenTick, now);
                if (_lastValleyActorAcd == 0 || dropoutMs > Math.Max(0, MfdNativeDropoutGraceMs))
                {
                    return null;
                }

                x = _lastValleyX;
                y = _lastValleyY;
                z = Hud.Game.Me.FloorCoordinate.Z;
            }

            var placement = new Placement { WorldX = x, WorldY = y, WorldZ = z };
            ScorePlacement(placement, targets, now);
            return placement;
        }

        private void ScorePlacement(Placement placement, IEnumerable<IMonster> targets, int now)
        {
            if (placement == null) return;
            placement.Score = 0;
            placement.CoveredBodies = 0;
            placement.CoveredElites = 0;
            placement.CoveredBosses = 0;
            placement.CoversFocus = false;
            placement.CoveredEliteAcds.Clear();
            foreach (IMonster target in targets)
            {
                if (target == null || !IsInsideValley(target, placement.WorldX, placement.WorldY)) continue;
                placement.Score += MfdTargetWeight(target, now);
                placement.CoveredBodies++;
                if (IsGroundSupportElite(target))
                {
                    placement.CoveredElites++;
                    placement.CoveredEliteAcds.Add(target.AcdId);
                    if (target.Rarity == ActorRarity.Boss) placement.CoveredBosses++;
                }
                if (IsCurrentPartyFocus(target, now)) placement.CoversFocus = true;
            }
        }

        private bool IsInsideValley(IMonster target, float x, float y)
        {
            return target != null && target.FloorCoordinate != null
                && target.FloorCoordinate.XYDistanceTo(x, y) <= ValleyRadius + GetMonsterRadiusBottom(target);
        }

        private double MfdTargetWeight(IMonster monster, int now)
        {
            if (monster == null) return 0;
            double score;
            if (IsCurrentPartyFocus(monster, now)) score = 20.0;
            else if (SameMonster(Hud.Game.SelectedMonster2, monster)) score = 12.0;
            else if (monster.Rarity == ActorRarity.Boss) score = 10.0;
            else if (monster.Rarity == ActorRarity.Rare || monster.Rarity == ActorRarity.Unique) score = 7.0;
            else if (monster.Rarity == ActorRarity.Champion) score = 6.0;
            else if (monster.Rarity == ActorRarity.RareMinion) score = 1.0;
            else score = IsHighValueTrash(monster) ? 2.5 : 1.0;

            score += Math.Min(2.0, GetRiftProgression(monster) * 2.5);
            if (IsEngaged(GetTargetState(monster, now), now)) score *= 1.25;
            return score;
        }

        private double CombatBodyWeight(IMonster monster, int now)
        {
            if (monster == null) return 0;
            double score = MfdTargetWeight(monster, now);
            if (!IsGroundSupportElite(monster)) score += GetRiftProgression(monster) * 4.0;
            return score;
        }

        private bool IsHighValueTrash(IMonster monster)
        {
            return monster != null && !monster.IsElite && GetRiftProgression(monster) >= HighValueTrashMinRiftProgression;
        }

        private float GetRiftProgression(IMonster monster)
        {
            try { return monster == null || monster.SnoMonster == null ? 0f : Math.Max(0f, monster.SnoMonster.RiftProgression); }
            catch { return 0f; }
        }

        private float GetMonsterRadiusBottom(IMonster monster)
        {
            try { return monster == null ? 0f : Math.Max(0f, monster.RadiusBottom); }
            catch { return 0f; }
        }

        private List<IActor> GetOwnedSentries()
        {
            IEnumerable<IActor> actors = Hud.Game.Actors ?? Enumerable.Empty<IActor>();
            return actors.Where(IsOwnedGuardianSentry)
                .OrderBy(a => a.CreatedAtInGameTick)
                .ToList();
        }

        private List<IActor> GetOnScreenOwnedSentries()
        {
            return GetOwnedSentries().Where(a => a != null && a.IsOnScreen).ToList();
        }

        private List<Placement> BuildDesiredSentryPlacements(ZdhLoadout local, CombatCluster cluster, int now, bool emergencyOnly)
        {
            var result = new List<Placement>();
            _runtime.SentryAnchorX = cluster == null ? 0 : cluster.CenterX;
            _runtime.SentryAnchorY = cluster == null ? 0 : cluster.CenterY;
            if (local == null || local.Player == null || cluster == null) return result;

            CombatCluster fieldCluster = BuildSentryFieldCluster(local, cluster, now);
            if (emergencyOnly)
            {
                int emergencyDesiredCount = GetDesiredSentryCount(local);
                List<Placement> emergency = BuildDpsProtectionPlacements(
                    local.Player, fieldCluster, new List<Placement>(), now, false);
                emergency.AddRange(BuildEliteSentryCoveragePlacements(
                    local, cluster, now, Math.Min(
                        Math.Max(0, EliteSentryCoverageMaxPlacements),
                        Math.Max(0, emergencyDesiredCount - Math.Min(InitialSentryFieldCount, emergencyDesiredCount)))));
                return emergency.OrderByDescending(x => x.Priority).Take(emergencyDesiredCount).ToList();
            }
            bool primaryEliteField = fieldCluster != null && fieldCluster.Elites.Any(IsGroundSupportPrimaryElite);
            if (!primaryEliteField && !cluster.TrashLatched
                && (!cluster.Stable || Elapsed(_packCandidateTick, now) < SentryPackStableMs)) return result;

            int desiredCount = GetDesiredSentryCount(local);

            List<Placement> field = BuildSentryPattern(fieldCluster, desiredCount);
            List<Placement> dps = BuildDpsProtectionPlacements(local.Player, fieldCluster, field, now, false);
            foreach (Placement protection in dps.OrderByDescending(x => x.Priority))
            {
                if (field.Count >= desiredCount)
                {
                    Placement replace = field.Where(x => x != null && x.Label != null && x.Label.StartsWith("Sentry Field", StringComparison.Ordinal))
                        .OrderBy(x => x.Priority).FirstOrDefault()
                        ?? field.OrderBy(x => x.Priority).FirstOrDefault();
                    if (replace != null) field.Remove(replace);
                }
                field.Add(protection);
            }
            return field.OrderByDescending(x => x.Priority).Take(desiredCount).ToList();
        }

        private CombatCluster BuildSentryFieldCluster(ZdhLoadout local, CombatCluster source, int now)
        {
            if (local == null || local.Player == null || source == null) return source;

            List<IMonster> primaryElites = MergeMonsters(source.Elites.Where(IsGroundSupportPrimaryElite),
                    GetActiveGroundSupportPrimaryElites(local.Player, now))
                .Where(m => m != null && IsGroundSupportPrimaryElite(m)
                    && m.IsOnScreen && m.FloorCoordinate != null
                    && Distance(local.Player, m) <= AutomationRange)
                .ToList();
            if (primaryElites.Count == 0) return source;

            Placement anchor = FindBestPlacement(primaryElites, now);
            if (anchor == null) return source;

            var coveredAcds = new HashSet<uint>(anchor.CoveredEliteAcds);
            List<IMonster> covered = primaryElites.Where(m => coveredAcds.Contains(m.AcdId)).ToList();
            if (covered.Count == 0) covered = primaryElites;

            var field = new CombatCluster
            {
                CenterX = anchor.WorldX,
                CenterY = anchor.WorldY,
                CenterZ = anchor.WorldZ,
                Stable = source.Stable,
                FocusTarget = source.FocusTarget,
                SustainedSpecialFocus = source.SustainedSpecialFocus,
                PriorityEliteCount = covered.Count,
                RecentDamageCount = covered.Count(m => IsEngaged(GetTargetState(m, now), now)),
                Score = source.Score,
                AxisX = source.AxisX,
                AxisY = source.AxisY,
            };
            field.Bodies.AddRange(covered);
            field.Elites.AddRange(covered);

            float farthest2 = 0;
            for (int i = 0; i < covered.Count; i++)
                for (int j = i + 1; j < covered.Count; j++)
                {
                    float dx = covered[j].FloorCoordinate.X - covered[i].FloorCoordinate.X;
                    float dy = covered[j].FloorCoordinate.Y - covered[i].FloorCoordinate.Y;
                    float distance2 = dx * dx + dy * dy;
                    if (distance2 <= farthest2) continue;
                    farthest2 = distance2;
                    field.AxisX = dx;
                    field.AxisY = dy;
                }
            if (!NormalizeDirection(ref field.AxisX, ref field.AxisY))
            {
                field.AxisX = source.AxisX;
                field.AxisY = source.AxisY;
                if (!NormalizeDirection(ref field.AxisX, ref field.AxisY))
                {
                    field.AxisX = 1f;
                    field.AxisY = 0f;
                }
            }

            float perpendicularX = -field.AxisY;
            float perpendicularY = field.AxisX;
            foreach (IMonster elite in covered)
            {
                float relativeX = elite.FloorCoordinate.X - field.CenterX;
                float relativeY = elite.FloorCoordinate.Y - field.CenterY;
                field.MajorExtent = Math.Max(field.MajorExtent,
                    Math.Abs(relativeX * field.AxisX + relativeY * field.AxisY));
                field.MinorExtent = Math.Max(field.MinorExtent,
                    Math.Abs(relativeX * perpendicularX + relativeY * perpendicularY));
            }

            _runtime.SentryAnchorX = field.CenterX;
            _runtime.SentryAnchorY = field.CenterY;
            return field;
        }

        private int GetDesiredSentryCount(ZdhLoadout local)
        {
            int count = 2;
            if (local != null && local.CustomEngineering) count++;
            if (local != null && local.BombardiersRucksack) count += 2;
            return Math.Max(1, Math.Min(Math.Min(5, SentryPackSlots), count));
        }

        private List<Placement> BuildSentryPattern(CombatCluster cluster, int count)
        {
            var result = new List<Placement>();
            if (cluster == null || count <= 0) return result;

            float forwardX = 0f;
            float forwardY = 0f;
            IPlayer player = Hud.Game.Me;
            if (player != null && player.FloorCoordinate != null)
            {
                forwardX = player.FloorCoordinate.X - cluster.CenterX;
                forwardY = player.FloorCoordinate.Y - cluster.CenterY;
            }
            if (!NormalizeDirection(ref forwardX, ref forwardY))
            {
                forwardX = -cluster.AxisX;
                forwardY = -cluster.AxisY;
                if (!NormalizeDirection(ref forwardX, ref forwardY))
                {
                    forwardX = 1f;
                    forwardY = 0f;
                }
            }

            float centerX = cluster.CenterX;
            float centerY = cluster.CenterY;
            Placement center = null;
            for (float shift = 0f; shift <= GuardianRadius && center == null; shift += 4f)
            {
                centerX = cluster.CenterX + forwardX * shift;
                centerY = cluster.CenterY + forwardY * shift;
                center = CreatePlacement(centerX, centerY, cluster.CenterZ);
            }
            for (float radius = 4f; radius <= GuardianRadius && center == null; radius += 4f)
            {
                for (int angle = 0; angle < 360 && center == null; angle += 30)
                {
                    double radians = angle * Math.PI / 180.0;
                    centerX = cluster.CenterX + (float)Math.Cos(radians) * radius;
                    centerY = cluster.CenterY + (float)Math.Sin(radians) * radius;
                    center = CreatePlacement(centerX, centerY, cluster.CenterZ);
                }
            }
            if (center == null) return result;

            center.Priority = 145;
            center.Label = "Sentry Field Center";
            center.SentrySlot = 1;
            result.Add(center);
            if (count == 1) return result;

            float sideX = -forwardY;
            float sideY = forwardX;
            float spacing = Math.Max(SentryMinSeparation + 1f,
                Math.Min(28f, Math.Max(24f, SentryPatternColumnSpacing)));
            float half = spacing * 0.5f;
            float row = (float)Math.Sqrt(Math.Max(1f, spacing * spacing - half * half));

            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                0f, -spacing, 140, "Sentry Field Core", 2, false, string.Empty);
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                0f, spacing, 139, "Sentry Field Core", 3, false, string.Empty);
            AddSentryExtension(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                -row, -half, 122, 4);
            AddSentryExtension(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                -row, half, 121, 5);

            FillVisibleSentryFallbacks(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY, count, spacing);

            return result.OrderByDescending(x => x.Priority).Take(count).ToList();
        }

        private void AddSentryExtension(List<Placement> result, float cx, float cy, float cz,
            float axisX, float axisY, float px, float py,
            float along, float across, double priority, int slot)
        {
            if (TryAddSentryPlacement(result, cx, cy, cz, axisX, axisY, px, py,
                along, across, priority, "Sentry Field Extension",
                slot, false, string.Empty)) return;
            TryAddSentryPlacement(result, cx, cy, cz, axisX, axisY, px, py,
                -along, across, priority, "Sentry Field Extension",
                slot, true, "mirrored visible extension");
        }

        private void FillVisibleSentryFallbacks(List<Placement> result,
            float cx, float cy, float cz, float axisX, float axisY,
            float px, float py, int count, float spacing)
        {
            if (result == null || result.Count >= count) return;

            float gap = Math.Max(SentryDistinctCoreSeparation, SentryMinSeparation);
            float[] inwardAlong =
            {
                gap, gap * 2f, gap, gap, gap * 2f, gap * 2f,
                0f, 0f, 0f, 0f, gap * 3f,
            };
            float[] inwardAcross =
            {
                0f, 0f, gap, -gap, gap, -gap,
                gap, -gap, gap * 2f, -gap * 2f, 0f,
            };

            for (int i = 0; i < inwardAlong.Length && result.Count < count; i++)
            {
                int slot = NextMissingSentrySlot(result, count);
                if (slot <= 0) return;
                TryAddSentryPlacement(result, cx, cy, cz, axisX, axisY, px, py,
                    inwardAlong[i], inwardAcross[i], 112 - slot,
                    "Sentry Field Fallback", slot, true, "inward visible fallback");
            }

            float[] radii = { gap, spacing, 27f, 30f, 36f, gap * 2f };
            foreach (float radius in radii)
            {
                for (int angle = 0; angle < 360 && result.Count < count; angle += 30)
                {
                    int slot = NextMissingSentrySlot(result, count);
                    if (slot <= 0) return;
                    double radians = angle * Math.PI / 180.0;
                    float along = (float)Math.Cos(radians) * radius;
                    float across = (float)Math.Sin(radians) * radius;
                    TryAddSentryPlacement(result, cx, cy, cz, axisX, axisY, px, py,
                        along, across, 112 - slot, "Sentry Field Fallback",
                        slot, true, "radial visible fallback");
                }
            }
        }

        private int NextMissingSentrySlot(List<Placement> result, int count)
        {
            return Enumerable.Range(1, Math.Max(0, count))
                .FirstOrDefault(index => result == null
                    || !result.Any(x => x != null && x.SentrySlot == index));
        }

        private bool TryAddSentryPlacement(List<Placement> result, float cx, float cy, float cz,
            float axisX, float axisY, float px, float py, float along, float across,
            double priority, string label, int slot, bool fallback,
            string fallbackReason, float maximumScale = 1f)
        {
            float minimumScale = Math.Max(0.75f, Math.Min(1f, SentryVisiblePatternMinScale));
            float scaleStep = Math.Max(0.05f, Math.Min(0.25f, SentryVisiblePatternScaleStep));
            for (float scale = Math.Max(minimumScale, Math.Min(1f, maximumScale));
                scale + 0.001f >= minimumScale; scale -= scaleStep)
            {
                Placement placement = CreatePlacement(
                    cx + (axisX * along + px * across) * scale,
                    cy + (axisY * along + py * across) * scale,
                    cz);
                if (placement == null) continue;

                float desiredGap = Math.Max(SentryDistinctCoreSeparation, SentryMinSeparation * scale);
                if (result.Any(x => x != null
                    && Distance2D(x.WorldX, x.WorldY, placement.WorldX, placement.WorldY) < desiredGap))
                    continue;

                placement.Priority = priority;
                placement.Label = label;
                placement.SentryScale = scale;
                placement.SentrySlot = slot;
                placement.SentryFallback = fallback;
                placement.SentryFallbackReason = fallbackReason ?? string.Empty;
                result.Add(placement);
                return true;
            }
            return false;
        }

        private void UpdateEliteSentryCoverageStates(
            IEnumerable<IMonster> elites, List<IActor> sentries, int now)
        {
            List<IMonster> active = (elites ?? Enumerable.Empty<IMonster>())
                .Where(m => m != null && IsGroundSupportElite(m)
                    && m.IsOnScreen && m.FloorCoordinate != null)
                .GroupBy(m => m.AcdId).Select(g => g.First()).ToList();

            int uncovered = 0;
            int ready = 0;
            IMonster bestTracked = null;
            bool bestTrackedReady = false;
            int bestAge = 0;
            int bestDelay = 0;
            int bestAttempts = 0;

            foreach (IMonster elite in active)
            {
                TargetState state = GetTargetState(elite, now);
                state.SentryCoverageLastActiveTick = now;
                bool covered = IsSentryNear(sentries,
                    elite.FloorCoordinate.X, elite.FloorCoordinate.Y, GuardianRadius);
                if (covered)
                {
                    state.SentryUncoveredSinceTick = int.MinValue;
                    if (state.SentryCoveredSinceTick == int.MinValue)
                        state.SentryCoveredSinceTick = now;
                    if (state.SentryCoverageAttempts > 0
                        && Elapsed(state.SentryCoveredSinceTick, now)
                            >= Math.Max(0, EliteSentryCoverageResetMs))
                    {
                        state.SentryCoverageAttempts = 0;
                    }
                    continue;
                }

                uncovered++;
                state.SentryCoveredSinceTick = int.MinValue;
                if (state.SentryUncoveredSinceTick == int.MinValue)
                    state.SentryUncoveredSinceTick = now;

                int age = Elapsed(state.SentryUncoveredSinceTick, now);
                int delay = EliteSentryCoverageDelayMs(state.SentryCoverageAttempts);
                bool isReady = age >= delay;
                if (isReady) ready++;

                if (bestTracked == null
                    || (isReady && !bestTrackedReady)
                    || (isReady == bestTrackedReady
                        && MfdTargetWeight(elite, now) > MfdTargetWeight(bestTracked, now)))
                {
                    bestTracked = elite;
                    bestTrackedReady = isReady;
                    bestAge = age;
                    bestDelay = delay;
                    bestAttempts = state.SentryCoverageAttempts;
                }
            }

            foreach (TargetState state in _targets.Values)
            {
                if (state.SentryCoverageLastActiveTick == int.MinValue
                    || Elapsed(state.SentryCoverageLastActiveTick, now)
                        < Math.Max(0, EliteSentryCoverageResetMs))
                    continue;
                state.SentryUncoveredSinceTick = int.MinValue;
                state.SentryCoveredSinceTick = int.MinValue;
                state.SentryCoverageLastActiveTick = int.MinValue;
                state.SentryCoverageAttempts = 0;
            }

            s7o_ZDH_HelperMetrics.LastEliteSentryUncovered = uncovered;
            s7o_ZDH_HelperMetrics.LastEliteSentryReady = ready;
            s7o_ZDH_HelperMetrics.LastEliteSentryTargetAcd = bestTracked == null ? 0 : bestTracked.AcdId;
            s7o_ZDH_HelperMetrics.LastEliteSentryAgeMs = bestTracked == null ? 0 : bestAge;
            s7o_ZDH_HelperMetrics.LastEliteSentryDelayMs = bestTracked == null ? 0 : bestDelay;
            s7o_ZDH_HelperMetrics.LastEliteSentryAttempts = bestTracked == null ? 0 : bestAttempts;
        }

        private void ResetEliteSentryCoverageMetrics()
        {
            s7o_ZDH_HelperMetrics.LastEliteSentryUncovered = 0;
            s7o_ZDH_HelperMetrics.LastEliteSentryReady = 0;
            s7o_ZDH_HelperMetrics.LastEliteSentryTargetAcd = 0;
            s7o_ZDH_HelperMetrics.LastEliteSentryAgeMs = 0;
            s7o_ZDH_HelperMetrics.LastEliteSentryDelayMs = 0;
            s7o_ZDH_HelperMetrics.LastEliteSentryAttempts = 0;
        }

        private int EliteSentryCoverageDelayMs(int attempts)
        {
            long delay = Math.Max(0, EliteSentryCoverageInitialMs)
                + (long)Math.Max(0, attempts) * Math.Max(0, EliteSentryCoverageStepMs);
            return (int)Math.Min(Math.Max(0, EliteSentryCoverageMaxMs), delay);
        }

        private bool EliteSentryCoverageReady(IMonster elite, int now)
        {
            if (elite == null) return false;
            TargetState state;
            if (!_targets.TryGetValue(elite.AcdId, out state)
                || state.SentryUncoveredSinceTick == int.MinValue)
                return false;
            return Elapsed(state.SentryUncoveredSinceTick, now)
                >= EliteSentryCoverageDelayMs(state.SentryCoverageAttempts);
        }

        private List<Placement> BuildEliteSentryCoveragePlacements(
            ZdhLoadout local, CombatCluster cluster, int now, int maxCount)
        {
            var result = new List<Placement>();
            if (local == null || local.Player == null || cluster == null || maxCount <= 0) return result;

            List<IMonster> candidates = MergeMonsters(
                    GetActiveGroundSupportPrimaryElites(local.Player, now),
                    GetActiveGroundSupportMfdOnlyTargets(local.Player, now))
                .Where(m => EliteSentryCoverageReady(m, now)
                    && DistanceToPoint(m, _runtime.SentryAnchorX, _runtime.SentryAnchorY)
                        <= SentryFieldRelevanceRadius)
                .OrderByDescending(m => MfdTargetWeight(m, now))
                .ToList();

            foreach (IMonster elite in candidates)
            {
                Placement shared = result.FirstOrDefault(x => x != null
                    && DistanceToPoint(elite, x.WorldX, x.WorldY) <= GuardianRadius);
                if (shared != null)
                {
                    shared.CoveredEliteAcds.Add(elite.AcdId);
                    continue;
                }
                if (result.Count >= maxCount) break;

                Placement placement = CreatePlacement(
                    elite.FloorCoordinate.X, elite.FloorCoordinate.Y, elite.FloorCoordinate.Z);
                if (placement == null) continue;
                placement.TargetAcd = elite.AcdId;
                placement.Priority = IsCurrentPartyFocus(elite, now)
                    || elite.Rarity == ActorRarity.Boss ? 130 : 110;
                placement.Label = "Sentry Field Elite Coverage";
                placement.CoveredEliteAcds.Add(elite.AcdId);
                result.Add(placement);
            }

            return result;
        }

        private void RecordEliteSentryCoverageAttempt(IEnumerable<uint> acds, int now)
        {
            foreach (uint acd in (acds ?? Enumerable.Empty<uint>()).Distinct())
            {
                TargetState state;
                if (!_targets.TryGetValue(acd, out state)) continue;
                state.SentryCoverageAttempts = Math.Min(4, state.SentryCoverageAttempts + 1);
                state.SentryUncoveredSinceTick = now;
                state.SentryCoveredSinceTick = int.MinValue;
            }
        }

        private List<Placement> BuildDpsProtectionPlacements(IPlayer zdh, CombatCluster cluster, List<Placement> field, int now, bool lowHealthOnly)
        {
            var result = new List<Placement>();
            List<IPlayer> dps = GetDpsPlayers(zdh).Take(2).ToList();
            var needed = new List<IPlayer>();

            foreach (IPlayer player in dps)
            {
                bool low = IsLowHealth(player);
                bool stable = player.InCombat && IsPlayerPositionStable(player, now)
                    && DistanceToPoint(player, cluster.CenterX, cluster.CenterY) <= SentryDpsPackRange;
                if (!low && (lowHealthOnly || !stable)) continue;
                if (CoveredByPlacements(field, player)) continue;

                IActor covering = FindCoveringOwnedSentry(player);
                if (covering != null)
                {
                    if (!lowHealthOnly && stable)
                    {
                        Placement retained = CreatePlacement(covering.FloorCoordinate.X, covering.FloorCoordinate.Y, covering.FloorCoordinate.Z);
                        if (retained != null)
                        {
                            retained.TargetAcd = player.AcdId;
                            retained.Priority = low ? 255 : 116;
                            retained.Label = low ? "Sentry DPS Emergency Retain" : "Sentry DPS Retain";
                            result.Add(retained);
                        }
                    }
                    continue;
                }

                needed.Add(player);
            }

            bool bossField = cluster != null && cluster.Elites.Any(m => m != null && m.Rarity == ActorRarity.Boss);
            if (needed.Count >= 2 && PlayerDistance(needed[0], needed[1]) <= GuardianRadius * 2f - 2f)
            {
                float x = (needed[0].FloorCoordinate.X + needed[1].FloorCoordinate.X) * 0.5f;
                float y = (needed[0].FloorCoordinate.Y + needed[1].FloorCoordinate.Y) * 0.5f;
                float z = (needed[0].FloorCoordinate.Z + needed[1].FloorCoordinate.Z) * 0.5f;
                Placement pair = CreatePlacement(x, y, z);
                if (pair != null)
                {
                    pair.TargetAcd = needed[0].AcdId;
                    pair.Priority = IsLowHealth(needed[0]) || IsLowHealth(needed[1]) ? 260
                        : bossField ? 144 : 118;
                    pair.Label = "Sentry DPS Pair";
                    result.Add(pair);
                    return result;
                }
            }

            foreach (IPlayer player in needed.Take(Math.Max(0, 2 - result.Count)))
            {
                Placement placement = CreatePlacement(player.FloorCoordinate.X, player.FloorCoordinate.Y, player.FloorCoordinate.Z);
                if (placement == null) continue;
                bool low = IsLowHealth(player);
                placement.TargetAcd = player.AcdId;
                placement.Priority = low ? 250 : bossField ? 143 : 115;
                placement.Label = low ? "Sentry DPS Emergency" : "Sentry DPS";
                result.Add(placement);
            }
            return result;
        }

        private IActor FindCoveringOwnedSentry(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return null;
            return GetOnScreenOwnedSentries()
                .Where(a => a != null && a.FloorCoordinate != null
                    && a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate) <= GuardianRadius)
                .OrderBy(a => a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate))
                .FirstOrDefault();
        }

        private bool CoveredByPlacements(IEnumerable<Placement> placements, IPlayer player)
        {
            return player != null && player.FloorCoordinate != null && placements != null
                && placements.Any(x => x != null && DistanceToPoint(player, x.WorldX, x.WorldY) <= GuardianRadius);
        }

        private Placement FindMissingDesiredSentryPlacement(List<Placement> desired, List<IActor> sentries,
            int desiredCount, bool emergencyOnly, int now)
        {
            if (desired == null || desired.Count == 0 || desiredCount <= 0)
            {
                return null;
            }

            sentries = sentries ?? new List<IActor>();
            int currentStackedPairs = CountSeverelyStackedSentries(desired, sentries);
            List<Placement> missing = GetUnmatchedDesiredSentryPlacements(desired, sentries);
            if (missing.Count == 0 && currentStackedPairs == 0)
            {
                return null;
            }

            if (sentries.Count < desiredCount)
            {
                float nearestBlocked = float.MaxValue;
                float nearestScreenBlocked = float.MaxValue;
                bool worldOpen = false;
                foreach (Placement placement in missing)
                {
                    if (IsRejectedSentryPlacement(placement, now))
                    {
                        Placement fallback = CreateRejectedSentryFallback(placement, desired, sentries, now);
                        if (fallback != null)
                        {
                            return fallback;
                        }
                        continue;
                    }

                    float nearest = NearestSentryDistance(sentries, placement.WorldX, placement.WorldY);
                    if (nearest < nearestBlocked) nearestBlocked = nearest;
                    if (nearest < RequiredSentrySeparation(placement)) continue;

                    worldOpen = true;
                    float nearestScreen = NearestSentryScreenDistance(sentries, placement);
                    if (RequiresSentryScreenSeparation(placement)
                        && nearestScreen < SentryScreenSeparationThreshold())
                    {
                        if (nearestScreen < nearestScreenBlocked) nearestScreenBlocked = nearestScreen;
                        continue;
                    }

                    return placement;
                }
                Placement openFallback = CreateOpenSentryFillFallback(missing, desired, sentries, now);
                if (openFallback != null)
                {
                    return openFallback;
                }

                return null;
            }

            float anchorX = _runtime.SentryAnchorX;
            float anchorY = _runtime.SentryAnchorY;
            Func<IActor, bool> individuallyRelevant = actor =>
                actor != null && actor.FloorCoordinate != null
                && (DistanceToPoint(actor, anchorX, anchorY) <= SentryFieldRelevanceRadius
                    || desired.Any(placement => placement != null
                        && DistanceToPoint(actor, placement.WorldX, placement.WorldY)
                            <= SentryPatternMatchRadius + 6f));

            IActor replace = sentries
                .OrderBy(actor => individuallyRelevant(actor) ? 1 : 0)
                .ThenBy(actor => actor.CreatedAtInGameTick)
                .FirstOrDefault();
            if (replace == null)
            {
                return null;
            }

            bool replaceWasIrrelevant = !individuallyRelevant(replace);
            List<IActor> survivors = sentries.Where(a => a.AcdId != replace.AcdId).ToList();
            double currentScore = ScoreDesiredSentryMatches(desired, sentries);
            float nearestBlockedReplacement = float.MaxValue;
            float nearestScreenBlockedReplacement = float.MaxValue;
            bool hadWorldOpenCandidate = false;
            bool hadScreenOpenCandidate = false;
            foreach (Placement placement in GetUnmatchedDesiredSentryPlacements(desired, survivors))
            {
                if (IsRejectedSentryPlacement(placement, now))
                {
                    Placement fallback = CreateRejectedSentryFallback(placement, desired, survivors, now);
                    if (fallback != null)
                    {
                        return fallback;
                    }
                    continue;
                }

                float nearest = NearestSentryDistance(survivors, placement.WorldX, placement.WorldY);
                if (nearest < RequiredSentrySeparation(placement))
                {
                    if (nearest < nearestBlockedReplacement) nearestBlockedReplacement = nearest;
                    continue;
                }

                hadWorldOpenCandidate = true;
                float nearestScreen = NearestSentryScreenDistance(survivors, placement);
                if (RequiresSentryScreenSeparation(placement)
                    && nearestScreen < SentryScreenSeparationThreshold())
                {
                    if (nearestScreen < nearestScreenBlockedReplacement)
                        nearestScreenBlockedReplacement = nearestScreen;
                    continue;
                }

                hadScreenOpenCandidate = true;
                double futureScore = ScoreDesiredSentryMatches(desired, survivors) + placement.Priority;
                bool emergency = emergencyOnly || (!string.IsNullOrEmpty(placement.Label)
                    && placement.Label.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0);
                bool protectedPlacement = IsProtectedSentryPlacement(placement);
                bool stackCorrection = currentStackedPairs > 0;
                if (emergency || protectedPlacement || stackCorrection || futureScore > currentScore + 0.5)
                {
                    return placement;
                }
            }

            if (replaceWasIrrelevant)
            {
                List<Placement> remaining = GetUnmatchedDesiredSentryPlacements(desired, survivors);
                Placement replacementFallback = CreateOpenSentryFillFallback(
                    remaining, desired, survivors, now);
                if (replacementFallback != null)
                {
                    return replacementFallback;
                }
            }

            return null;
        }

        private Placement CreateOpenSentryFillFallback(List<Placement> missing,
            List<Placement> desired, List<IActor> sentries, int now)
        {
            if (missing == null || missing.Count == 0 || desired == null || desired.Count == 0)
                return null;

            Placement center = desired.FirstOrDefault(x => x != null && x.SentrySlot == 1)
                ?? desired.FirstOrDefault(IsProtectedSentryPlacement)
                ?? desired.FirstOrDefault();
            Placement template = missing.Where(x => x != null)
                .OrderByDescending(x => x.Priority).FirstOrDefault();
            if (center == null || template == null) return null;

            float minimumRadius = Math.Max(SentryMinSeparation, SentryDistinctCoreSeparation);
            float maximumRadius = Math.Max(minimumRadius, SentryFieldRelevanceRadius - 1f);
            float[] radii =
            {
                minimumRadius,
                Math.Min(maximumRadius, minimumRadius + 4f),
                Math.Min(maximumRadius, minimumRadius + 8f),
                maximumRadius,
            };

            foreach (float radius in radii.Distinct())
            {
                for (int angle = 0; angle < 360; angle += 15)
                {
                    double radians = angle * Math.PI / 180.0;
                    Placement candidate = CreatePlacement(
                        center.WorldX + (float)Math.Cos(radians) * radius,
                        center.WorldY + (float)Math.Sin(radians) * radius,
                        center.WorldZ);
                    if (candidate == null || IsRejectedSentryPlacement(candidate, now)) continue;

                    if (Distance2D(candidate.WorldX, candidate.WorldY,
                        _runtime.SentryAnchorX,
                        _runtime.SentryAnchorY) > SentryFieldRelevanceRadius - 1f)
                        continue;

                    candidate.SentryScale = template.SentryScale;
                    candidate.Priority = template.Priority - 0.2;
                    candidate.Label = "Sentry Field Open Fallback";
                    candidate.SentrySlot = template.SentrySlot;
                    candidate.SentryFallback = true;
                    candidate.SentryFallbackReason = "nominal slots blocked";

                    float nearest = NearestSentryDistance(sentries, candidate.WorldX, candidate.WorldY);
                    if (nearest < RequiredSentrySeparation(candidate)) continue;
                    float nearestScreen = NearestSentryScreenDistance(sentries, candidate);
                    if (RequiresSentryScreenSeparation(candidate)
                        && nearestScreen < SentryScreenSeparationThreshold()) continue;

                    return candidate;
                }
            }
            return null;
        }

        private Placement CreateRejectedSentryFallback(Placement rejected,
            List<Placement> desired, List<IActor> sentries, int now)
        {
            if (rejected == null || desired == null || desired.Count == 0) return null;
            bool protectedPlacement = IsProtectedSentryPlacement(rejected);
            float baseX;
            float baseY;
            float vx;
            float vy;
            float radius;

            if (protectedPlacement)
            {
                baseX = rejected.WorldX;
                baseY = rejected.WorldY;
                IPlayer player = Hud.Game.Me;
                vx = player == null || player.FloorCoordinate == null
                    ? 1f : rejected.WorldX - player.FloorCoordinate.X;
                vy = player == null || player.FloorCoordinate == null
                    ? 0f : rejected.WorldY - player.FloorCoordinate.Y;
                if (!NormalizeDirection(ref vx, ref vy))
                {
                    vx = 1f;
                    vy = 0f;
                }
                float maximumCoverageRadius = Math.Max(6f, GuardianRadius - 1f);
                radius = Math.Min(maximumCoverageRadius,
                    Math.Max(11f, SentryRejectedPositionRadius + 2f));
            }
            else
            {
                Placement center = desired.FirstOrDefault(x => x != null && x.SentrySlot == 1)
                    ?? desired.FirstOrDefault(IsProtectedSentryPlacement)
                    ?? desired.FirstOrDefault();
                if (center == null) return null;
                baseX = center.WorldX;
                baseY = center.WorldY;
                vx = rejected.WorldX - center.WorldX;
                vy = rejected.WorldY - center.WorldY;
                radius = (float)Math.Sqrt(vx * vx + vy * vy);
                if (radius < SentryDistinctCoreSeparation)
                {
                    vx = 1f;
                    vy = 0f;
                    radius = Math.Max(SentryDistinctCoreSeparation, SentryMinSeparation);
                }
                else
                {
                    vx /= radius;
                    vy /= radius;
                }
            }

            RejectedSentryPosition rejection = GetRejectedSentryPositionNear(rejected, now);
            bool terrainRelocated = rejection != null
                && !string.IsNullOrEmpty(rejection.Reason)
                && rejection.Reason.IndexOf("relocated", StringComparison.OrdinalIgnoreCase) >= 0;
            float[] rotations = terrainRelocated
                ? new[] { 90f, -90f, 60f, -60f, 30f, -30f, 120f, -120f }
                : protectedPlacement
                    ? new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f }
                    : new[] { 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 180f };
            float[] scales = protectedPlacement
                ? new[] { 1f, 0.8f, 0.6f }
                : new[] { 1f, 0.9f, 0.8f };

            foreach (float scale in scales)
            {
                foreach (float rotation in rotations)
                {
                    double radians = rotation * Math.PI / 180.0;
                    float rx = vx * (float)Math.Cos(radians) - vy * (float)Math.Sin(radians);
                    float ry = vx * (float)Math.Sin(radians) + vy * (float)Math.Cos(radians);
                    Placement candidate = CreatePlacement(
                        baseX + rx * radius * scale,
                        baseY + ry * radius * scale,
                        rejected.WorldZ);
                    if (candidate == null || IsRejectedSentryPlacement(candidate, now)) continue;

                    candidate.SentryScale = protectedPlacement
                        ? 1f : Math.Max(0.75f, rejected.SentryScale * scale);
                    candidate.Priority = rejected.Priority - 0.1;
                    candidate.TargetAcd = rejected.TargetAcd;
                    if (protectedPlacement)
                        foreach (uint acd in rejected.CoveredEliteAcds)
                            candidate.CoveredEliteAcds.Add(acd);
                    candidate.Label = protectedPlacement ? rejected.Label : "Sentry Field Fallback";
                    candidate.SentrySlot = rejected.SentrySlot;
                    candidate.SentryFallback = true;
                    candidate.SentryFallbackReason = protectedPlacement
                        ? "protected ground fallback" : "rejected ground fallback";

                    float nearest = NearestSentryDistance(sentries, candidate.WorldX, candidate.WorldY);
                    if (nearest < RequiredSentrySeparation(candidate)) continue;
                    float nearestScreen = NearestSentryScreenDistance(sentries, candidate);
                    if (RequiresSentryScreenSeparation(candidate)
                        && nearestScreen < SentryScreenSeparationThreshold()) continue;
                    return candidate;
                }
            }
            return null;
        }

        private Placement CreateRecentMfdSentryAnchor(CombatCluster cluster, List<IActor> sentries, int now)
        {
            if (cluster == null || _recentGroundKind != CastKind.MarkedForDeath
                || _recentGroundTick == int.MinValue
                || Elapsed(_recentGroundTick, now) > Math.Max(1000,
                    GroundActorAdoptionMs + MarkedForDeathUrgentRecastMs)
                || float.IsNaN(_recentGroundX) || float.IsNaN(_recentGroundY)
                || Distance2D(_recentGroundX, _recentGroundY, cluster.CenterX, cluster.CenterY)
                    > CombatBodyNearAnchorRadius)
                return null;
            if (NearestSentryDistance(sentries, _recentGroundX, _recentGroundY) < SentryMinSeparation)
                return null;

            Placement placement = CreatePlacement(_recentGroundX, _recentGroundY, cluster.CenterZ);
            if (placement == null) return null;
            placement.Priority = 150;
            placement.Label = "Sentry MFD Anchor";
            placement.SentrySlot = 1;
            return placement;
        }

        private bool IsProtectedSentryPlacement(Placement placement)
        {
            if (placement == null || string.IsNullOrEmpty(placement.Label)) return false;
            return string.Equals(placement.Label, "Sentry Field Center", StringComparison.Ordinal)
                || string.Equals(placement.Label, "Sentry MFD Anchor", StringComparison.Ordinal)
                || placement.Label.StartsWith("Sentry Field Elite Coverage", StringComparison.Ordinal)
                || placement.Label.StartsWith("Sentry DPS", StringComparison.Ordinal);
        }

        private float RequiredSentrySeparation(Placement placement)
        {
            if (IsProtectedSentryPlacement(placement))
                return Math.Max(SentryStackedDistance,
                    Math.Min(SentryMinSeparation, SentryProtectedMinSeparation));
            float scale = placement == null ? 1f : Math.Max(0.75f, Math.Min(1f, placement.SentryScale));
            return Math.Max(SentryDistinctCoreSeparation, SentryMinSeparation * scale);
        }

        private bool RequiresSentryScreenSeparation(Placement placement)
        {
            return placement != null && !IsProtectedSentryPlacement(placement);
        }

        private float SentryScreenSeparationThreshold()
        {
            float scale = Hud == null || Hud.Window == null || Hud.Window.Size.Height <= 0
                ? 1f : Hud.Window.Size.Height / 1080f;
            return Math.Max(20f, SentryMinScreenSeparationPixels * scale);
        }

        private float NearestSentryScreenDistance(IEnumerable<IActor> sentries, Placement placement)
        {
            if (placement == null || placement.Screen == null || sentries == null) return float.MaxValue;
            float nearest = float.MaxValue;
            foreach (IActor sentry in sentries)
            {
                if (sentry == null || sentry.FloorCoordinate == null) continue;
                IScreenCoordinate screen;
                try
                {
                    screen = Hud.Window.WorldToScreenCoordinate(
                        sentry.FloorCoordinate.X, sentry.FloorCoordinate.Y, sentry.FloorCoordinate.Z, false, true);
                }
                catch { continue; }
                if (screen == null) continue;
                float dx = screen.X - placement.Screen.X;
                float dy = screen.Y - placement.Screen.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance < nearest) nearest = distance;
            }
            return nearest;
        }

        private int CountDistinctRelevantSentries(List<Placement> desired, List<IActor> sentries)
        {
            if (desired == null || desired.Count == 0 || sentries == null || sentries.Count == 0) return 0;
            float anchorX = _runtime.SentryAnchorX;
            float anchorY = _runtime.SentryAnchorY;
            List<IActor> relevant = sentries.Where(sentry => sentry != null && sentry.FloorCoordinate != null
                    && (DistanceToPoint(sentry, anchorX, anchorY) <= SentryFieldRelevanceRadius
                        || desired.Any(placement => placement != null
                            && DistanceToPoint(sentry, placement.WorldX, placement.WorldY)
                                <= SentryPatternMatchRadius + 6f)))
                .OrderBy(sentry => DistanceToPoint(sentry, anchorX, anchorY))
                .ThenBy(sentry => sentry.CreatedAtInGameTick)
                .ToList();

            float minimumGap = Math.Max(SentryStackedDistance,
                Math.Min(SentryMinSeparation, SentryDistinctCoreSeparation));
            var distinct = new List<IActor>();
            foreach (IActor sentry in relevant)
            {
                if (distinct.All(existing => existing.FloorCoordinate.XYDistanceTo(sentry.FloorCoordinate) >= minimumGap))
                    distinct.Add(sentry);
            }

            bool anchorCovered = distinct.Any(sentry => DistanceToPoint(sentry, anchorX, anchorY) <= GuardianRadius);
            return distinct.Count > 0 && !anchorCovered ? distinct.Count - 1 : distinct.Count;
        }

        private int CountSeverelyStackedSentries(List<Placement> desired, List<IActor> sentries)
        {
            if (desired == null || desired.Count == 0 || sentries == null || sentries.Count < 2) return 0;
            float anchorX = _runtime.SentryAnchorX;
            float anchorY = _runtime.SentryAnchorY;
            List<IActor> relevant = sentries.Where(actor => actor != null && actor.FloorCoordinate != null
                    && (DistanceToPoint(actor, anchorX, anchorY) <= SentryFieldRelevanceRadius
                        || desired.Any(placement => placement != null
                            && DistanceToPoint(actor, placement.WorldX, placement.WorldY)
                                <= SentryPatternMatchRadius + 6f)))
                .ToList();
            int pairs = 0;
            float limit = Math.Max(2f, Math.Min(SentryMinSeparation, SentryStackedDistance));
            for (int i = 0; i < relevant.Count; i++)
                for (int j = i + 1; j < relevant.Count; j++)
                    if (relevant[i].FloorCoordinate.XYDistanceTo(relevant[j].FloorCoordinate) < limit)
                        pairs++;
            return pairs;
        }

        private float NearestSentryDistance(List<IActor> sentries, float x, float y)
        {
            if (sentries == null || sentries.Count == 0) return float.MaxValue;
            double nearest = sentries.Where(a => a != null && a.FloorCoordinate != null)
                .Select(a => DistanceToPoint(a, x, y)).DefaultIfEmpty(double.MaxValue).Min();
            return nearest == double.MaxValue ? float.MaxValue : (float)nearest;
        }

        private List<Placement> GetUnmatchedDesiredSentryPlacements(List<Placement> desired, List<IActor> sentries)
        {
            var missing = new List<Placement>();
            var available = new List<IActor>((sentries ?? new List<IActor>())
                .Where(actor => actor != null && actor.FloorCoordinate != null));
            foreach (Placement placement in (desired ?? new List<Placement>())
                .Where(x => x != null).OrderByDescending(x => x.Priority))
            {
                IActor match = available
                    .Where(actor => DistanceToPoint(actor, placement.WorldX, placement.WorldY) <= SentryPatternMatchRadius)
                    .OrderBy(actor => DistanceToPoint(actor, placement.WorldX, placement.WorldY))
                    .FirstOrDefault();
                if (match == null) missing.Add(placement);
                else available.Remove(match);
            }
            return missing;
        }

        private int CountDesiredSentryMatches(List<Placement> desired, List<IActor> sentries)
        {
            return Math.Max(0, (desired == null ? 0 : desired.Count)
                - GetUnmatchedDesiredSentryPlacements(desired, sentries).Count);
        }

        private int CountRelevantSentries(List<Placement> desired, List<IActor> sentries)
        {
            if (desired == null || desired.Count == 0 || sentries == null) return 0;
            float anchorX = _runtime.SentryAnchorX;
            float anchorY = _runtime.SentryAnchorY;
            int count = 0;
            bool anchorCovered = false;
            foreach (IActor sentry in sentries)
            {
                if (sentry == null || sentry.FloorCoordinate == null) continue;
                double anchorDistance = DistanceToPoint(sentry, anchorX, anchorY);
                bool nearAnchor = anchorDistance <= SentryFieldRelevanceRadius;
                bool nearPlacement = desired.Any(placement => placement != null
                    && DistanceToPoint(sentry, placement.WorldX, placement.WorldY) <= SentryPatternMatchRadius + 6f);
                if (nearAnchor || nearPlacement) count++;
                if (anchorDistance <= GuardianRadius) anchorCovered = true;
            }
            return count > 0 && !anchorCovered ? count - 1 : count;
        }

        private double ScoreDesiredSentryMatches(List<Placement> desired, List<IActor> sentries)
        {
            if (desired == null || desired.Count == 0) return 0;
            var unmatched = new HashSet<Placement>(GetUnmatchedDesiredSentryPlacements(desired, sentries));
            return desired.Where(placement => !unmatched.Contains(placement)).Sum(placement => placement.Priority);
        }

        private void UpdatePlayerPositionStates(int now)
        {
            var seen = new HashSet<uint>();
            foreach (IPlayer player in Hud.Game.Players)
            {
                if (player == null || !player.HasValidActor || player.FloorCoordinate == null) continue;
                seen.Add(player.AcdId);
                PlayerPositionState state;
                if (!_playerPositions.TryGetValue(player.AcdId, out state))
                {
                    state = new PlayerPositionState
                    {
                        X = player.FloorCoordinate.X,
                        Y = player.FloorCoordinate.Y,
                        StableSinceTick = now,
                        LastSeenTick = now,
                    };
                    _playerPositions[player.AcdId] = state;
                    continue;
                }
                if (Distance2D(state.X, state.Y, player.FloorCoordinate.X, player.FloorCoordinate.Y) > 2.5f)
                    state.StableSinceTick = now;
                state.X = player.FloorCoordinate.X;
                state.Y = player.FloorCoordinate.Y;
                state.LastSeenTick = now;
            }
            foreach (uint key in _playerPositions.Where(kv => !seen.Contains(kv.Key) && Elapsed(kv.Value.LastSeenTick, now) > 5000).Select(kv => kv.Key).ToList())
                _playerPositions.Remove(key);
        }

        private bool IsPlayerPositionStable(IPlayer player, int now)
        {
            PlayerPositionState state;
            return player != null && _playerPositions.TryGetValue(player.AcdId, out state)
                && state.StableSinceTick != int.MinValue && Elapsed(state.StableSinceTick, now) >= SentryDpsStableMs;
        }

        private float PlayerHealthPct(IPlayer player)
        {
            try { return player == null || player.Defense == null ? 0f : player.Defense.HealthPct; }
            catch { return 0f; }
        }

        private bool IsLowHealth(IPlayer player)
        {
            float health = PlayerHealthPct(player);
            return health > 0f && health <= SentryLowHealthPct;
        }

        private bool IsSentryNear(IEnumerable<IActor> sentries, float x, float y, float radius)
        {
            return sentries != null && sentries.Any(a => a != null && a.FloorCoordinate != null && a.FloorCoordinate.XYDistanceTo(x, y) <= radius);
        }

        private Placement CreatePlacement(float x, float y, float z)
        {
            IScreenCoordinate screen = Hud.Window.WorldToScreenCoordinate(x, y, z, false, true);
            return screen != null && PointInsideCastArea(screen.X, screen.Y)
                ? new Placement { WorldX = x, WorldY = y, WorldZ = z, Screen = screen }
                : null;
        }

        private double DistanceToPoint(IPlayer player, float x, float y)
        {
            try { return player.FloorCoordinate.XYDistanceTo(x, y); }
            catch { return double.MaxValue; }
        }

        private double DistanceToPoint(IActor actor, float x, float y)
        {
            try { return actor.FloorCoordinate.XYDistanceTo(x, y); }
            catch { return double.MaxValue; }
        }

        private static float Distance2D(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void UpdatePartyFocus(int now)
        {
            uint bestAcd = 0;
            int bestVotes = 0;
            var votes = new Dictionary<uint, int>();
            int dpsRank = 0;
            foreach (IPlayer player in GetDpsPlayers(Hud.Game.Me).Take(2))
            {
                if (player == null || !player.InCombat) { dpsRank++; continue; }
                uint identity = ReadIdentityAttribute(player, Hud.Sno.Attributes.Last_ACD_Attacked);
                IMonster target = FindMonsterByIdentity(identity);
                if (target != null && IsAutomationBody(target))
                {
                    int count;
                    votes.TryGetValue(target.AcdId, out count);
                    votes[target.AcdId] = count + (dpsRank == 0 ? 2 : 1);
                }
                dpsRank++;
            }
            foreach (KeyValuePair<uint, int> vote in votes)
            {
                IMonster target = FindMonster(vote.Key);
                int weighted = vote.Value * 10 + (IsStatusTarget(target) ? 4 : 0);
                if (weighted <= bestVotes) continue;
                bestVotes = weighted;
                bestAcd = vote.Key;
            }

            if (bestAcd != 0)
            {
                if (_partyFocusCandidateAcd == bestAcd) _partyFocusCandidateSamples = Math.Min(99, _partyFocusCandidateSamples + 1);
                else { _partyFocusCandidateAcd = bestAcd; _partyFocusCandidateSamples = 1; }
                if (_partyFocusCandidateSamples >= PartyFocusConfirmSamples)
                {
                    if (_partyFocusAcd != bestAcd) _partyFocusSinceTick = now;
                    _partyFocusAcd = bestAcd;
                    _partyFocusUntilTick = unchecked(now + PartyFocusLingerMs);
                }
            }
            else
            {
                _partyFocusCandidateAcd = 0;
                _partyFocusCandidateSamples = 0;
            }

            if (_partyFocusAcd != 0 && (Reached(now, _partyFocusUntilTick) || FindMonster(_partyFocusAcd) == null))
                ClearPartyFocus();
        }

        private uint ReadIdentityAttribute(IActor actor, IAttribute attribute)
        {
            if (actor == null || attribute == null) return 0;
            foreach (uint modifier in IdentityAttributeModifiers)
            {
                try
                {
                    uint value = actor.GetAttributeValueAsUInt(attribute, modifier, 0u);
                    if (value != 0u && value != uint.MaxValue) return value;
                }
                catch { }
            }
            return 0;
        }

        private IMonster FindMonsterByIdentity(uint identity)
        {
            if (identity == 0 || Hud.Game.AliveMonsters == null) return null;
            return Hud.Game.AliveMonsters.FirstOrDefault(m => m != null && (m.AcdId == identity || m.AnnId == identity));
        }

        private IMonster GetPartyFocusMonster(int now)
        {
            return _partyFocusAcd != 0 && !Reached(now, _partyFocusUntilTick) ? FindMonster(_partyFocusAcd) : null;
        }

        private bool IsCurrentPartyFocus(IMonster monster, int now)
        {
            return monster != null && _partyFocusAcd != 0 && !Reached(now, _partyFocusUntilTick)
                && monster.AcdId == _partyFocusAcd;
        }

        private bool IsPartyFocusSustained(int now)
        {
            return _partyFocusAcd != 0 && _partyFocusSinceTick != int.MinValue
                && Elapsed(_partyFocusSinceTick, now) >= PartyFocusSpecialTargetMs;
        }

        private void ClearPartyFocus()
        {
            _partyFocusCandidateAcd = 0;
            _partyFocusCandidateSamples = 0;
            _partyFocusAcd = 0;
            _partyFocusSinceTick = int.MinValue;
            _partyFocusUntilTick = int.MinValue;
        }

        private static bool SameMonster(IMonster a, IMonster b)
        {
            return a != null && b != null
                && ((a.AcdId != 0 && a.AcdId == b.AcdId) || (a.AnnId != 0 && a.AnnId == b.AnnId));
        }

        private static bool SamePlayer(IPlayer a, IPlayer b)
        {
            return a != null && b != null
                && ((a.AcdId != 0 && a.AcdId == b.AcdId) || (a.AnnId != 0 && a.AnnId == b.AnnId));
        }

        private IEnumerable<IPlayer> GetDpsPlayers(IPlayer zdh)
        {
            List<IPlayer> candidates = Hud.Game.Players.Where(p => p != null && p.HasValidActor && !p.IsDead
                    && !SamePlayer(p, zdh) && p.CoordinateKnown && p.IsOnScreen
                    && PlayerDistance(zdh, p) <= AutomationRange)
                .ToList();
            double maxDps = candidates.Count == 0 ? 0 : candidates.Max(p => PlayerDpsScore(p));
            return candidates.Where(p => !IsLikelySupport(p, maxDps))
                .OrderByDescending(p => PlayerRoleScore(p, maxDps));
        }

        private bool IsLikelySupport(IPlayer player, double partyMaxDps)
        {
            try
            {
                if (player == null || player.HeroClassDefinition == null || player.Powers == null) return false;
                List<uint> skills = player.Powers.UsedSkills.Where(x => x != null && x.SnoPower != null).Select(x => x.SnoPower.Sno).ToList();
                if (player.HeroClassDefinition.HeroClass == HeroClass.DemonHunter
                    && skills.Contains(EntanglingShotSno) && BuffActive(player.Powers.UsedLegendaryPowers.OdysseysEnd))
                    return true;
                if (player.HeroClassDefinition.HeroClass == HeroClass.Barbarian
                    && skills.Contains(377453) && (skills.Contains(79528) || skills.Contains(79446)))
                    return true;

                int supportGems = CountSupportGems(player);
                int damageGems = CountDamageGems(player);
                double dps = PlayerDpsScore(player);
                if (supportGems >= 2 && damageGems == 0) return true;
                if (supportGems >= 2 && damageGems <= 1 && partyMaxDps > 0 && dps <= partyMaxDps * 0.20) return true;
            }
            catch { }
            return false;
        }

        private double PlayerRoleScore(IPlayer player, double partyMaxDps)
        {
            double normalizedDps = partyMaxDps <= 0 ? 0 : Math.Min(1.0, PlayerDpsScore(player) / partyMaxDps);
            return CountDamageGems(player) * 1000.0 + normalizedDps * 500.0 - CountSupportGems(player) * 35.0;
        }

        private int CountSupportGems(IPlayer player)
        {
            try
            {
                ILegendaryGemInfo gems = player == null || player.Powers == null ? null : player.Powers.UsedLegendaryGems;
                if (gems == null) return 0;
                int count = 0;
                if (AnyGem(gems.GemOfEfficaciousToxinPrimary, gems.GemOfEfficaciousToxinSecondary)) count++;
                if (AnyGem(gems.IceblinkPrimary, gems.IceblinkSecondary)) count++;
                if (AnyGem(gems.WreathOfLightningPrimary, gems.WreathOfLightningSecondary)) count++;
                if (AnyGem(gems.GogokOfSwiftnessPrimary, gems.GogokOfSwiftnessSecondary)) count++;
                if (AnyGem(gems.EsotericAlterationPrimary, gems.EsotericAlterationSecondary)) count++;
                if (AnyGem(gems.MutilationGuardPrimary, gems.MutilationGuardSecondary)) count++;
                return count;
            }
            catch { return 0; }
        }

        private int CountDamageGems(IPlayer player)
        {
            try
            {
                ILegendaryGemInfo gems = player == null || player.Powers == null ? null : player.Powers.UsedLegendaryGems;
                if (gems == null) return 0;
                int count = 0;
                if (AnyGem(gems.BaneOfThePowerfulPrimary, gems.BaneOfThePowerfulSecondary)) count++;
                if (AnyGem(gems.BaneOfTheStrickenPrimary, gems.BaneOfTheStrickenSecondary)) count++;
                if (AnyGem(gems.BaneOfTheTrappedPrimary, gems.BaneOfTheTrappedSecondary)) count++;
                if (AnyGem(gems.EnforcerPrimary, gems.EnforcerSecondary)) count++;
                if (AnyGem(gems.LegacyOfDreamsPrimary, gems.LegacyOfDreamsSecondary)) count++;
                if (AnyGem(gems.PainEnhancerPrimary, gems.PainEnhancerSecondary)) count++;
                if (AnyGem(gems.SimplicitysStrengthPrimary, gems.SimplicitysStrengthSecondary)) count++;
                if (AnyGem(gems.TaegukPrimary, gems.TaegukSecondary)) count++;
                if (AnyGem(gems.ZeisStoneOfVengeancePrimary, gems.ZeisStoneOfVengeanceSecondary)) count++;
                return count;
            }
            catch { return 0; }
        }

        private static bool AnyGem(IBuff primary, IBuff secondary)
        {
            return BuffActive(primary) || BuffActive(secondary);
        }

        private double PlayerDpsScore(IPlayer player)
        {
            try
            {
                if (player == null) return 0;
                double score = 0;
                if (player.Damage != null)
                    score = Math.Max(player.Damage.CurrentDps, Math.Max(player.Damage.RunDps, player.Damage.MaximumDps));
                if (score <= 0 && player.Offense != null) score = player.Offense.SheetDps;
                return score;
            }
            catch { return 0; }
        }

        private void UpdateOwnedActors(int now)
        {
            IEnumerable<IActor> actors = Hud.Game.Actors ?? Enumerable.Empty<IActor>();
            var alive = new HashSet<uint>();
            var rawValleys = new List<IActor>();
            var ignoredValleys = new List<IActor>();
            IActor newestValley = null;
            int guardianNativeOwned = 0;
            int guardianAdoptedOwned = 0;

            foreach (IActor actor in actors)
            {
                if (actor == null || actor.SnoActor == null || actor.FloorCoordinate == null) continue;
                bool isValley = IsValleyActor(actor);
                bool isSentry = IsGuardianSentryBody(actor);
                if (!isValley && !isSentry) continue;
                alive.Add(actor.AcdId);

                if (isValley)
                {
                    rawValleys.Add(actor);
                    if (!IsMfdActorOwnedCandidate(actor, now)
                        || IsGenerationOlder(actor.CreatedAtInGameTick, actor.AcdId,
                            _lastValleyActorCreatedTick, _lastValleyActorAcd))
                    {
                        ignoredValleys.Add(actor);
                        continue;
                    }

                    _ownedActorAcds.Add(actor.AcdId);
                    if (newestValley == null || IsGenerationNewer(actor.CreatedAtInGameTick, actor.AcdId,
                        newestValley.CreatedAtInGameTick, newestValley.AcdId))
                        newestValley = actor;
                    continue;
                }

                bool nativeOwned = IsNativeOwnedGuardianSentry(actor);
                bool owned = nativeOwned;
                if (!owned && _cast.Stage != CastStage.Idle && _cast.Kind == CastKind.Sentry
                    && !float.IsNaN(_cast.ExpectedWorldX) && Elapsed(_cast.StartedTick, now) <= GroundActorAdoptionMs
                    && actor.FloorCoordinate.XYDistanceTo(_cast.ExpectedWorldX, _cast.ExpectedWorldY) <= 10f)
                    owned = true;
                if (!owned && RecentGroundActorMatches(actor, now)) owned = true;
                if (owned)
                {
                    _ownedActorAcds.Add(actor.AcdId);
                    if (nativeOwned) guardianNativeOwned++;
                    else guardianAdoptedOwned++;
                }
            }

            foreach (uint acd in _ownedActorAcds.Where(x => !alive.Contains(x)).ToList())
                _ownedActorAcds.Remove(acd);

            if (newestValley != null)
            {
                bool newGeneration = IsGenerationNewer(newestValley.CreatedAtInGameTick, newestValley.AcdId,
                    _lastValleyActorCreatedTick, _lastValleyActorAcd);
                _lastValleyActorAcd = newestValley.AcdId;
                _lastValleyActorCreatedTick = newestValley.CreatedAtInGameTick;
                _lastValleyActorSeenTick = now;
                _lastValleyX = newestValley.FloorCoordinate.X;
                _lastValleyY = newestValley.FloorCoordinate.Y;
                if (newGeneration || _lastValleyTick == int.MinValue)
                {
                    bool recentCast = _recentGroundKind == CastKind.MarkedForDeath
                        && _recentGroundTick != int.MinValue
                        && Elapsed(_recentGroundTick, now) <= GroundActorAdoptionMs
                        && newestValley.FloorCoordinate.XYDistanceTo(_recentGroundX, _recentGroundY) <= ValleyRadius;
                    _lastValleyTick = recentCast ? _recentGroundTick : now;
                }
            }

            IActor authoritative = FindAuthoritativeValleyActor();
            int dropoutMs = _lastValleyActorSeenTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastValleyActorSeenTick, now);
        }

        private void RememberGroundCastInput(int now)
        {
            if ((_cast.Kind != CastKind.MarkedForDeath && _cast.Kind != CastKind.Sentry)
                || float.IsNaN(_cast.ExpectedWorldX) || float.IsNaN(_cast.ExpectedWorldY)) return;
            _recentGroundKind = _cast.Kind;
            _recentGroundX = _cast.ExpectedWorldX;
            _recentGroundY = _cast.ExpectedWorldY;
            _recentGroundTick = now;
        }

        private bool RecentGroundActorMatches(IActor actor, int now)
        {
            return actor != null && actor.FloorCoordinate != null && _recentGroundKind != CastKind.None
                && _recentGroundTick != int.MinValue && Elapsed(_recentGroundTick, now) <= GroundActorAdoptionMs
                && ActorMatchesGroundKind(actor, _recentGroundKind)
                && actor.FloorCoordinate.XYDistanceTo(_recentGroundX, _recentGroundY) <= 10f;
        }

        private bool ActorMatchesGroundKind(IActor actor, CastKind kind)
        {
            if (actor == null || actor.SnoActor == null) return false;
            return kind == CastKind.MarkedForDeath
                ? IsValleyActor(actor)
                : kind == CastKind.Sentry && IsGuardianSentryBody(actor);
        }

        private bool RecentValleyActorMatches(IActor actor, int now)
        {
            return IsValleyActor(actor) && actor.FloorCoordinate != null
                && _recentGroundKind == CastKind.MarkedForDeath
                && _recentGroundTick != int.MinValue
                && Elapsed(_recentGroundTick, now) <= GroundActorAdoptionMs
                && actor.FloorCoordinate.XYDistanceTo(_recentGroundX, _recentGroundY) <= ValleyRadius;
        }

        private bool IsMfdActorOwnedCandidate(IActor actor, int now)
        {
            if (!IsValleyActor(actor) || actor.FloorCoordinate == null
                || actor.CreatedAtInGameTick < _mfdActorEpochGameTick) return false;
            if (_ownedActorAcds.Contains(actor.AcdId)) return true;
            if (actor.AcdId == _lastValleyActorAcd
                && actor.CreatedAtInGameTick == _lastValleyActorCreatedTick) return true;
            if (!RecentValleyActorMatches(actor, now)) return false;
            return _cast.Stage == CastStage.Idle || _cast.Kind != CastKind.MarkedForDeath
                || actor.CreatedAtInGameTick >= _cast.BaselineMfdGameTick;
        }

        private IActor FindAuthoritativeValleyActor()
        {
            if (_lastValleyActorAcd == 0) return null;
            return (Hud.Game.Actors ?? Enumerable.Empty<IActor>()).FirstOrDefault(a => a != null
                && a.SnoActor != null && a.FloorCoordinate != null
                && IsValleyActor(a)
                && a.AcdId == _lastValleyActorAcd
                && a.CreatedAtInGameTick == _lastValleyActorCreatedTick
                && a.CreatedAtInGameTick >= _mfdActorEpochGameTick);
        }

        private bool HasNewMfdActorNearExpected()
        {
            if (float.IsNaN(_cast.ExpectedWorldX)
                || !IsGenerationNewer(_lastValleyActorCreatedTick, _lastValleyActorAcd,
                    _cast.BaselineMfdActorCreatedTick, _cast.BaselineMfdActorAcd)
                || _lastValleyActorCreatedTick < _cast.BaselineMfdGameTick
                || _lastValleyActorSeenTick == int.MinValue
                || Elapsed(_lastValleyActorSeenTick, Environment.TickCount) > Math.Max(0, MfdNativeDropoutGraceMs))
                return false;
            return Distance2D(_lastValleyX, _lastValleyY, _cast.ExpectedWorldX, _cast.ExpectedWorldY) <= ValleyRadius;
        }

        private static bool IsGenerationNewer(int createdTick, uint acd, int baselineCreatedTick, uint baselineAcd)
        {
            return createdTick > baselineCreatedTick
                || (createdTick == baselineCreatedTick && acd > baselineAcd);
        }

        private static bool IsGenerationOlder(int createdTick, uint acd, int baselineCreatedTick, uint baselineAcd)
        {
            return baselineAcd != 0 && (createdTick < baselineCreatedTick
                || (createdTick == baselineCreatedTick && acd < baselineAcd));
        }

        private bool CoveredByOwnedSentry(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return false;
            return GetOnScreenOwnedSentries().Any(a => a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate) <= GuardianRadius);
        }

        private bool IsValleyActor(IActor actor)
        {
            return actor != null && actor.SnoActor != null
                && actor.SnoActor.Sno == ActorSnoEnum._dh_markedfordeath_proxyactor;
        }

        private bool IsGuardianSentryBody(IActor actor)
        {
            return actor != null && actor.SnoActor != null
                && actor.SnoActor.Sno == GuardianSentryActor
                && actor.FloorCoordinate != null;
        }

        private bool IsNativeOwnedGuardianSentry(IActor actor)
        {
            IPlayer me = Hud.Game.Me;
            return IsGuardianSentryBody(actor) && me != null && me.SummonerId != 0
                && actor.SummonerAcdDynamicId == me.SummonerId;
        }

        private bool IsOwnedGuardianSentry(IActor actor)
        {
            return IsGuardianSentryBody(actor)
                && (IsNativeOwnedGuardianSentry(actor) || _ownedActorAcds.Contains(actor.AcdId));
        }

        private IActor FindNewNativeOwnedSentryActor()
        {
            IEnumerable<IActor> actors = Hud.Game.Actors ?? Enumerable.Empty<IActor>();
            return actors.Where(IsNativeOwnedGuardianSentry)
                .Where(actor => !_cast.BaselineActorAcds.Contains(actor.AcdId))
                .OrderByDescending(actor => actor.CreatedAtInGameTick)
                .FirstOrDefault();
        }

        private IEnumerable<uint> GetRelevantActorIds(CastKind kind)
        {
            IEnumerable<IActor> actors = Hud.Game.Actors ?? Enumerable.Empty<IActor>();
            if (kind == CastKind.MarkedForDeath)
                return actors.Where(a => a != null && a.SnoActor != null && a.FloorCoordinate != null
                    && IsValleyActor(a)
                    && a.AcdId == _lastValleyActorAcd
                    && a.CreatedAtInGameTick == _lastValleyActorCreatedTick)
                    .Select(a => a.AcdId).ToList();
            if (kind == CastKind.Sentry)
                return actors.Where(IsOwnedGuardianSentry).Select(a => a.AcdId).ToList();
            return Enumerable.Empty<uint>();
        }

        private bool HasNewRelevantActorNearExpected(CastKind kind)
        {
            if (kind == CastKind.MarkedForDeath) return HasNewMfdActorNearExpected();
            if (float.IsNaN(_cast.ExpectedWorldX)) return false;
            IEnumerable<IActor> actors = Hud.Game.Actors ?? Enumerable.Empty<IActor>();
            return actors.Any(a => IsOwnedGuardianSentry(a)
                && !_cast.BaselineActorAcds.Contains(a.AcdId)
                && a.FloorCoordinate.XYDistanceTo(_cast.ExpectedWorldX, _cast.ExpectedWorldY) <= 10f);
        }

        private bool GetTargetFlag(CastKind kind, uint targetAcd)
        {
            IMonster monster = FindMonster(targetAcd);
            if (kind == CastKind.Entangle) return monster != null && HasEntangle(monster);
            if (kind == CastKind.Multishot) return monster != null && HasIceblink(monster);
            if (kind == CastKind.MarkedForDeath) return monster != null && monster.MarkedForDeath;
            if (kind == CastKind.Sentry)
            {
                IPlayer player = FindPlayer(targetAcd);
                return player != null && CoveredByOwnedSentry(player);
            }
            return false;
        }

        private IMonster FindMonster(uint acd)
        {
            return acd == 0 ? null : Hud.Game.AliveMonsters.FirstOrDefault(m => m != null && m.AcdId == acd);
        }

        private IPlayer FindPlayer(uint acd)
        {
            return acd == 0 ? null : Hud.Game.Players.FirstOrDefault(p => p != null && p.AcdId == acd);
        }

        private bool HasEntangle(IMonster monster)
        {
            return monster != null && monster.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_B, EntanglingShotSno, 0) == 1;
        }

        private bool HasIceblink(IMonster monster)
        {
            return monster != null && monster.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, IceblinkSno, 0) == 1;
        }

        private bool IsAutomationBody(IMonster monster)
        {
            return monster != null && monster.IsAlive && monster.FloorCoordinate != null
                && !monster.Illusion && !monster.Hidden && !monster.Stealthed && !monster.Invisible && !monster.Untargetable;
        }

        private bool IsGroundSupportElite(IMonster monster)
        {
            if (monster == null || !monster.IsAlive || monster.FloorCoordinate == null
                || monster.Illusion || monster.Hidden || monster.Stealthed || monster.Invisible) return false;
            if (monster.Untargetable && !monster.Burrowed && !monster.Invulnerable) return false;
            return monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.Unique || monster.Rarity == ActorRarity.Boss;
        }

        private bool IsGroundSupportPrimaryElite(IMonster monster)
        {
            return IsGroundSupportElite(monster) && !IsJuggernaut(monster);
        }

        private bool IsGroundSupportMfdOnlyTarget(IMonster monster)
        {
            return IsGroundSupportElite(monster) && IsJuggernaut(monster);
        }

        private bool IsStatusTarget(IMonster monster)
        {
            if (monster == null || !monster.IsAlive || monster.FloorCoordinate == null || monster.Illusion || monster.Hidden || monster.Stealthed || monster.Invisible || monster.Untargetable) return false;
            return monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.Unique || monster.Rarity == ActorRarity.Boss;
        }

        private bool IsDebuffBody(IMonster monster)
        {
            return IsAutomationBody(monster) && monster.Rarity != ActorRarity.RareMinion
                && !IsJuggernaut(monster) && !monster.Invulnerable && monster.Attackable;
        }

        private bool IsImportantDebuffTarget(IMonster monster)
        {
            return IsStatusTarget(monster);
        }

        private bool IsJuggernaut(IMonster monster)
        {
            try { return monster.AffixSnoList != null && monster.AffixSnoList.Any(a => a != null && a.Affix == MonsterAffix.Juggernaut); }
            catch { return false; }
        }

        private bool ContextAvailable()
        {
            return Hud != null && Hud.Game != null && Hud.Window != null && Hud.Game.IsInGame && !Hud.Game.IsLoading && !Hud.Game.IsPaused && Hud.Game.Me != null;
        }

        private bool SentryBurstAutomationContextValid()
        {
            return ContextAvailable() && s7o_ZDH_HelperState.Enabled && !Hud.Game.IsInTown && !Hud.Game.Me.IsDead
                && Hud.Window.IsForeground
                && !ZdhInput.IsVirtualKeyDown(0x5B) && !ZdhInput.IsVirtualKeyDown(0x5C)
                && PointInsideWindow(Hud.Window.CursorX, Hud.Window.CursorY)
                && !InventoryOpen() && !UiVisible(_chatEditLine) && !UiVisible(Hud.Render.WorldMapUiElement)
                && !IsUnoperatedPylonNearby(PylonInteractionPauseRange)
                && Hud.Game.Me.AnimationState != AcdAnimationState.CastingPortal;
        }

        private bool AutomationContextValid()
        {
            return ContextAvailable() && s7o_ZDH_HelperState.Enabled && !Hud.Game.IsInTown && !Hud.Game.Me.IsDead
                && Hud.Window.IsForeground
                && !ZdhInput.IsVirtualKeyDown(0x5B) && !ZdhInput.IsVirtualKeyDown(0x5C)
                && PointInsideWindow(Hud.Window.CursorX, Hud.Window.CursorY)
                && !InventoryOpen() && !UiVisible(_chatEditLine) && !UiVisible(Hud.Render.WorldMapUiElement)
                && !IsUnoperatedPylonNearby(PylonInteractionPauseRange)
                && Hud.Game.Me.AnimationState != AcdAnimationState.CastingPortal && Hud.Game.Me.AnimationState != AcdAnimationState.Transform;
        }

        private bool IsUnoperatedPylonNearby(float range)
        {
            try
            {
                if (!ContextAvailable() || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Shrines == null) return false;
                float limit = Math.Max(0, range);
                return Hud.Game.Shrines.Any(shrine => shrine != null && shrine.IsPylon
                    && !shrine.IsDisabled && !shrine.IsOperated && shrine.FloorCoordinate != null
                    && Hud.Game.Me.FloorCoordinate.XYDistanceTo(shrine.FloorCoordinate) <= limit);
            }
            catch { return false; }
        }

        private static bool UiVisible(IUiElement element)
        {
            try { return element != null && element.Visible; }
            catch { return true; }
        }

        private bool InventoryOpen()
        {
            try { return Hud.Inventory != null && Hud.Inventory.InventoryMainUiElement != null && Hud.Inventory.InventoryMainUiElement.Visible; }
            catch { return true; }
        }

        private void UpdatePylonState()
        {
            _channelingPylonActive = PlayerBuffActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno);
            _speedPylonActive = PlayerBuffActive(Hud.Sno.SnoPowers.Generic_PagesBuffRunSpeed.Sno);
            s7o_ZDH_HelperMetrics.LastChannelingPylonActive = _channelingPylonActive;
            s7o_ZDH_HelperMetrics.LastSpeedPylonActive = _speedPylonActive;
        }

        private bool PlayerBuffActive(uint sno)
        {
            try { return Hud.Game.Me != null && Hud.Game.Me.Powers != null && Hud.Game.Me.Powers.BuffIsActive(sno); }
            catch { return false; }
        }

        private bool SentryAvailable(IPlayerSkill skill)
        {
            return skill != null && skill.Key != ActionKey.Unknown
                && (skill.Charges > 0 || (_channelingPylonActive && !skill.IsOnCooldown));
        }

        private bool SkillReady(IPlayerSkill skill)
        {
            return skill != null && skill.Key != ActionKey.Unknown && (skill.Charges > 0 || !skill.IsOnCooldown);
        }

        private static uint SkillSno(IPlayerSkill skill)
        {
            try { return skill == null || skill.SnoPower == null ? 0u : skill.SnoPower.Sno; }
            catch { return 0u; }
        }

        private static bool BuffActive(IBuff buff)
        {
            try { return buff != null && buff.Active; }
            catch { return false; }
        }

        private static bool RuneContains(IPlayerSkill skill, string text)
        {
            return skill != null && !string.IsNullOrEmpty(skill.RuneNameEnglish) && skill.RuneNameEnglish.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private double Distance(IPlayer player, IMonster monster)
        {
            try { return player.FloorCoordinate.XYDistanceTo(monster.FloorCoordinate); }
            catch { return double.MaxValue; }
        }

        private double PlayerDistance(IPlayer a, IPlayer b)
        {
            try { return a.FloorCoordinate.XYDistanceTo(b.FloorCoordinate); }
            catch { return double.MaxValue; }
        }

        private MultishotPlan BuildMultishotPlan(IPlayer player, List<IMonster> targets, HashSet<uint> dueAcds, int now)
        {
            if (player == null || player.FloorCoordinate == null || targets == null || targets.Count == 0)
                return null;

            dueAcds = dueAcds ?? new HashSet<uint>();
            bool priorityMode = dueAcds.Count > 0;
            targets = targets.OrderByDescending(m => MultishotTargetWeight(m, dueAcds.Contains(m.AcdId)))
                .Take(32).ToList();
            var directions = new List<DirectionCandidate>();
            foreach (IMonster target in targets)
                AddDirectionCandidate(directions,
                    target.FloorCoordinate.X - player.FloorCoordinate.X,
                    target.FloorCoordinate.Y - player.FloorCoordinate.Y);

            for (int i = 0; i < targets.Count; i++)
                for (int j = i + 1; j < targets.Count; j++)
                {
                    float ax = targets[i].FloorCoordinate.X - player.FloorCoordinate.X;
                    float ay = targets[i].FloorCoordinate.Y - player.FloorCoordinate.Y;
                    float bx = targets[j].FloorCoordinate.X - player.FloorCoordinate.X;
                    float by = targets[j].FloorCoordinate.Y - player.FloorCoordinate.Y;
                    NormalizeDirection(ref ax, ref ay);
                    NormalizeDirection(ref bx, ref by);
                    AddDirectionCandidate(directions, ax + bx, ay + by);
                }

            MultishotPlan best = null;
            foreach (DirectionCandidate direction in directions)
            {
                var covered = new List<IMonster>();
                double score = 0;
                foreach (IMonster target in targets)
                {
                    if (!IsInsideMultishotCone(player, target, direction.X, direction.Y)) continue;
                    covered.Add(target);
                    score += MultishotTargetWeight(target, dueAcds.Contains(target.AcdId));
                }
                if (covered.Count == 0) continue;

                List<IMonster> coveredDue = covered.Where(m => dueAcds.Contains(m.AcdId)).ToList();
                if (priorityMode && coveredDue.Count == 0) continue;
                List<IMonster> coveredDueImportant = coveredDue.Where(IsImportantDebuffTarget).ToList();
                List<IMonster> coveredPrimaryElites = covered.Where(IsStatusTarget).ToList();

                double maxDueEliteAngle = 0;
                double averageDueEliteAngle = 0;
                if (coveredDueImportant.Count > 0)
                {
                    List<double> dueAngles = coveredDueImportant
                        .Select(m => MultishotAngleDegrees(player, m, direction.X, direction.Y))
                        .ToList();
                    maxDueEliteAngle = dueAngles.Max();
                    averageDueEliteAngle = dueAngles.Average();
                }

                IMonster primary = coveredDueImportant
                    .OrderByDescending(m => TargetPriority(m, true)).FirstOrDefault()
                    ?? coveredDue.OrderByDescending(m => MultishotTargetWeight(m, true)).FirstOrDefault()
                    ?? covered.OrderByDescending(m => MultishotTargetWeight(m, false)).FirstOrDefault();
                if (primary == null) continue;

                IScreenCoordinate aim = CreateExtendedDirectionalAim(player, direction.X, direction.Y, primary.ScreenCoordinate);
                if (aim == null) continue;

                var plan = new MultishotPlan
                {
                    Primary = primary,
                    Aim = aim,
                    Score = score,
                    CoveredBodyCount = covered.Count,
                    CoveredEliteCount = coveredPrimaryElites.Count,
                    PrimaryMustApply = priorityMode && !HasIceblink(primary) && IsImportantDebuffTarget(primary),
                    MaxDueEliteAngleDegrees = maxDueEliteAngle,
                    AverageDueEliteAngleDegrees = averageDueEliteAngle,
                };
                foreach (IMonster target in coveredDue) plan.CoveredMissingAcds.Add(target.AcdId);
                foreach (IMonster target in coveredDueImportant)
                {
                    plan.CoveredEliteAcds.Add(target.AcdId);
                    plan.CoveredMissingEliteAcds.Add(target.AcdId);
                }
                foreach (IMonster target in coveredPrimaryElites)
                    plan.CoveredPrimaryEliteAcds.Add(target.AcdId);
                plan.RequiredApplied = plan.CoveredMissingAcds.Count == 0 ? 0
                    : Math.Max(1, Math.Min(5, (int)Math.Ceiling(plan.CoveredMissingAcds.Count * 0.45)));

                bool better = best == null
                    || plan.CoveredMissingEliteAcds.Count > best.CoveredMissingEliteAcds.Count
                    || (plan.CoveredMissingEliteAcds.Count == best.CoveredMissingEliteAcds.Count
                        && plan.MaxDueEliteAngleDegrees < best.MaxDueEliteAngleDegrees - 0.1)
                    || (plan.CoveredMissingEliteAcds.Count == best.CoveredMissingEliteAcds.Count
                        && Math.Abs(plan.MaxDueEliteAngleDegrees - best.MaxDueEliteAngleDegrees) <= 0.1
                        && plan.AverageDueEliteAngleDegrees < best.AverageDueEliteAngleDegrees - 0.1)
                    || (plan.CoveredMissingEliteAcds.Count == best.CoveredMissingEliteAcds.Count
                        && Math.Abs(plan.MaxDueEliteAngleDegrees - best.MaxDueEliteAngleDegrees) <= 0.1
                        && Math.Abs(plan.AverageDueEliteAngleDegrees - best.AverageDueEliteAngleDegrees) <= 0.1
                        && plan.CoveredEliteCount > best.CoveredEliteCount)
                    || (plan.CoveredMissingEliteAcds.Count == best.CoveredMissingEliteAcds.Count
                        && Math.Abs(plan.MaxDueEliteAngleDegrees - best.MaxDueEliteAngleDegrees) <= 0.1
                        && Math.Abs(plan.AverageDueEliteAngleDegrees - best.AverageDueEliteAngleDegrees) <= 0.1
                        && plan.CoveredEliteCount == best.CoveredEliteCount && plan.Score > best.Score);
                if (better) best = plan;
            }
            return best;
        }

        private double MultishotAngleDegrees(IPlayer player, IMonster target, float directionX, float directionY)
        {
            if (player == null || target == null || player.FloorCoordinate == null || target.FloorCoordinate == null)
                return 180.0;
            float tx = target.FloorCoordinate.X - player.FloorCoordinate.X;
            float ty = target.FloorCoordinate.Y - player.FloorCoordinate.Y;
            if (!NormalizeDirection(ref tx, ref ty)) return 180.0;
            double dot = Math.Max(-1.0, Math.Min(1.0, tx * directionX + ty * directionY));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private bool IsInsideMultishotCone(IPlayer player, IMonster target, float directionX, float directionY)
        {
            if (player == null || target == null || player.FloorCoordinate == null || target.FloorCoordinate == null) return false;
            float tx = target.FloorCoordinate.X - player.FloorCoordinate.X;
            float ty = target.FloorCoordinate.Y - player.FloorCoordinate.Y;
            float distance = (float)Math.Sqrt(tx * tx + ty * ty);
            if (distance < 0.01f || distance > MultishotRange + GetMonsterRadiusBottom(target)) return false;
            tx /= distance;
            ty /= distance;
            double angularAllowance = Math.Atan2(GetMonsterRadiusBottom(target), Math.Max(1f, distance)) * 180.0 / Math.PI;
            double minimumDot = Math.Cos((MultishotConeHalfAngleDegrees + angularAllowance) * Math.PI / 180.0);
            return tx * directionX + ty * directionY >= minimumDot;
        }

        private double MultishotTargetWeight(IMonster target, bool missing)
        {
            if (target == null) return 0;
            double score;
            if (IsCurrentPartyFocus(target, Environment.TickCount)) score = 1350;
            else if (SameMonster(Hud.Game.SelectedMonster2, target)) score = 1200;
            else if (target.Rarity == ActorRarity.Boss) score = 1000;
            else if (target.Rarity == ActorRarity.Rare || target.Rarity == ActorRarity.Unique) score = 520;
            else if (target.Rarity == ActorRarity.Champion) score = 460;
            else if (target.Rarity == ActorRarity.RareMinion) score = 20;
            else score = IsHighValueTrash(target) ? 65 : 18;
            score += GetRiftProgression(target) * 90.0;
            if (missing) score += IsStatusTarget(target) ? 700 : 55;
            else score *= 0.15;
            return score;
        }

        private void AddDirectionCandidate(List<DirectionCandidate> directions, float x, float y)
        {
            if (!NormalizeDirection(ref x, ref y)) return;
            if (directions.Any(d => Math.Abs(d.X - x) < 0.01f && Math.Abs(d.Y - y) < 0.01f)) return;
            directions.Add(new DirectionCandidate { X = x, Y = y });
        }

        private static bool NormalizeDirection(ref float x, ref float y)
        {
            float length = (float)Math.Sqrt(x * x + y * y);
            if (length < 0.001f) return false;
            x /= length;
            y /= length;
            return true;
        }

        private IScreenCoordinate CreateExtendedDirectionalAim(IPlayer player, float directionX, float directionY, IScreenCoordinate fallback)
        {
            if (player == null || player.FloorCoordinate == null) return SafeFallbackAim(fallback);
            IScreenCoordinate origin = player.ScreenCoordinate;
            float worldX = player.FloorCoordinate.X + directionX * MultishotAimDistance;
            float worldY = player.FloorCoordinate.Y + directionY * MultishotAimDistance;
            IScreenCoordinate projected = Hud.Window.WorldToScreenCoordinate(worldX, worldY, player.FloorCoordinate.Z, false, true);
            if (origin == null || projected == null) return SafeFallbackAim(fallback);

            return ClipDirectionalAim(origin, projected, fallback, 110f,
                MultishotSafeSideRatio, MultishotSafeTopRatio, MultishotSafeBottomRatio);
        }

        private IScreenCoordinate CreateSafeDirectionalAim(IPlayer player, IScreenCoordinate target)
        {
            if (player == null || player.ScreenCoordinate == null || target == null) return null;
            return ClipDirectionalAim(player.ScreenCoordinate, target, target, 70f, 0.02f, 0.04f, GroundCastSafeBottomRatio);
        }

        private IScreenCoordinate ClipDirectionalAim(IScreenCoordinate origin, IScreenCoordinate target,
            IScreenCoordinate fallback, float minimumDistance, float sideRatio, float topRatio, float bottomRatio)
        {
            if (origin == null || target == null) return SafeFallbackAim(fallback);
            Size size = Hud.Window.Size;
            float left = Math.Max(24f, size.Width * sideRatio);
            float right = Math.Min(size.Width - 24f, size.Width * (1f - sideRatio));
            float top = Math.Max(24f, size.Height * topRatio);
            float bottom = Math.Min(size.Height - 140f, size.Height * bottomRatio);
            float dx = target.X - origin.X;
            float dy = target.Y - origin.Y;
            if (Math.Abs(dx) < 1f && Math.Abs(dy) < 1f) return SafeFallbackAim(fallback);

            float t = 1f;
            if (dx > 0) t = Math.Min(t, (right - origin.X) / dx);
            else if (dx < 0) t = Math.Min(t, (left - origin.X) / dx);
            if (dy > 0) t = Math.Min(t, (bottom - origin.Y) / dy);
            else if (dy < 0) t = Math.Min(t, (top - origin.Y) / dy);
            t = Math.Max(0.12f, Math.Min(1f, t));
            float x = Math.Max(left, Math.Min(right, origin.X + dx * t));
            float y = Math.Max(top, Math.Min(bottom, origin.Y + dy * t));
            double distance = Math.Sqrt((x - origin.X) * (x - origin.X) + (y - origin.Y) * (y - origin.Y));
            if (distance < minimumDistance) return SafeFallbackAim(fallback);
            return Hud.Window.CreateScreenCoordinate(x, y);
        }

        private IScreenCoordinate SafeFallbackAim(IScreenCoordinate fallback)
        {
            if (fallback == null) return null;
            Size size = Hud.Window.Size;
            float left = Math.Max(32f, size.Width * MultishotSafeSideRatio);
            float right = Math.Min(size.Width - 32f, size.Width * (1f - MultishotSafeSideRatio));
            float top = Math.Max(36f, size.Height * MultishotSafeTopRatio);
            float bottom = Math.Min(size.Height - 180f, size.Height * MultishotSafeBottomRatio);
            return Hud.Window.CreateScreenCoordinate(Math.Max(left, Math.Min(right, fallback.X)), Math.Max(top, Math.Min(bottom, fallback.Y)));
        }

        private int GetAimSettleMs(CastKind kind)
        {
            bool combatMode = _runtime.HighFrequencyMode;
            return kind == CastKind.Multishot
                ? combatMode ? CombatMultishotAimSettleMs : MultishotAimSettleMs
                : kind == CastKind.Sentry ? SentryAimSettleMs
                : kind == CastKind.MarkedForDeath
                    ? combatMode ? CombatGroundAimSettleMs : GroundAimSettleMs
                    : EntangleAimSettleMs;
        }

        private int GetSkillHoldMs(CastKind kind)
        {
            return kind == CastKind.Multishot ? MultishotSkillHoldMs
                : kind == CastKind.Sentry ? SentrySkillHoldMs
                : kind == CastKind.MarkedForDeath ? GroundSkillHoldMs : EntangleSkillHoldMs;
        }

        private int GetVerifyMs(CastKind kind)
        {
            return kind == CastKind.Entangle ? EntangleVerifyMs
                : kind == CastKind.Multishot ? MultishotVerifyMs
                : kind == CastKind.MarkedForDeath ? MarkedForDeathVerifyMs
                : SentryVerifyMs;
        }

        private int GetPostCastPrimaryQuietMs(CastKind kind)
        {
            return kind == CastKind.Multishot ? MultishotPrimaryQuietMs
                : kind == CastKind.Sentry ? SentryPrimaryQuietMs
                : kind == CastKind.MarkedForDeath ? MarkedForDeathPrimaryQuietMs
                : 0;
        }

        private int GetPreInputHardLimitMs(CastKind kind)
        {
            return kind == CastKind.Multishot ? Math.Max(CastPauseHardLimitMs, MultishotPreInputHardLimitMs)
                : kind == CastKind.Sentry ? Math.Max(CastPauseHardLimitMs, SentryPreInputHardLimitMs)
                : kind == CastKind.MarkedForDeath ? Math.Max(CastPauseHardLimitMs, GroundPreInputHardLimitMs)
                : CastPauseHardLimitMs;
        }

        private bool RequiresMovementSettleBeforeInput()
        {
            return _cast.Kind == CastKind.MarkedForDeath
                || (_cast.Kind == CastKind.Sentry && !_cast.SentryBurstChild);
        }

        private bool ShouldWaitForMultishotMovementSettle(int now, AcdAnimationState animation)
        {
            return _cast.Kind == CastKind.Multishot
                && _cast.RequiresStrafePause
                && animation == AcdAnimationState.Running
                && _cast.PauseAckTick != int.MinValue
                && Elapsed(_cast.PauseAckTick, now) < Math.Max(0, MultishotMovementSettleGraceMs)
                && Elapsed(_cast.StartedTick, now) + Math.Max(1, MultishotRunningSkillHoldMs)
                    < GetPreInputHardLimitMs(CastKind.Multishot);
        }

        private int GetAimCorrectionLimit(CastKind kind)
        {
            return kind == CastKind.Multishot || kind == CastKind.MarkedForDeath || kind == CastKind.Sentry
                ? SupportAimCorrectionLimit : AimCorrectionLimit;
        }

        private float GetAimDisplacementTolerance(CastKind kind)
        {
            return kind == CastKind.Multishot || kind == CastKind.MarkedForDeath || kind == CastKind.Sentry
                ? SupportAimDisplacementTolerancePixels : AimDisplacementTolerancePixels;
        }

        private void UpdateBossEntangleStandstill(ZdhLoadout local, bool bossStandalone)
        {
            bool inputDown = bossStandalone && s7o_ZDH_HelperState.Enabled
                && s7o_ZDH_HelperState.AutoEntangle && local != null && local.Entangle != null
                && ActionIsDown(local.Entangle.Key);

            if (inputDown)
            {
                _bossEntangleStandstillReleasePending = false;
                if (!_bossEntangleStandstillOwned && ForceStandstillVirtualKey != 0
                    && !ZdhInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                    _bossEntangleStandstillOwned = ZdhInput.KeyDown(ForceStandstillVirtualKey);
            }
            else if (_bossEntangleStandstillOwned)
            {
                if (_cast.Stage == CastStage.Idle) ReleaseBossEntangleStandstill();
                else _bossEntangleStandstillReleasePending = true;
            }

        }

        private void ReleaseBossEntangleStandstill()
        {
            if (_bossEntangleStandstillOwned && ForceStandstillVirtualKey != 0)
                ZdhInput.KeyUp(ForceStandstillVirtualKey);
            _bossEntangleStandstillOwned = false;
            _bossEntangleStandstillReleasePending = false;
        }

        private void CompleteBossEntangleStandstillRelease()
        {
            if (_bossEntangleStandstillReleasePending && _cast.Stage == CastStage.Idle)
                ReleaseBossEntangleStandstill();
        }

        private bool ActionIsDown(ActionKey key)
        {
            if (key == ActionKey.LeftSkill) return ZdhInput.IsVirtualKeyDown(0x01);
            if (key == ActionKey.RightSkill) return ZdhInput.IsVirtualKeyDown(0x02);
            if (key == ActionKey.Skill1) return ZdhInput.IsVirtualKeyDown(Skill1VirtualKey);
            if (key == ActionKey.Skill2) return ZdhInput.IsVirtualKeyDown(Skill2VirtualKey);
            if (key == ActionKey.Skill3) return ZdhInput.IsVirtualKeyDown(Skill3VirtualKey);
            if (key == ActionKey.Skill4) return ZdhInput.IsVirtualKeyDown(Skill4VirtualKey);
            return false;
        }

        private bool ActionDown(ActionKey key)
        {
            if (key == ActionKey.LeftSkill) return ZdhInput.MouseDownLeft();
            if (key == ActionKey.RightSkill) return ZdhInput.MouseDownRight();
            if (key == ActionKey.Skill1) return ZdhInput.KeyDown(Skill1VirtualKey);
            if (key == ActionKey.Skill2) return ZdhInput.KeyDown(Skill2VirtualKey);
            if (key == ActionKey.Skill3) return ZdhInput.KeyDown(Skill3VirtualKey);
            if (key == ActionKey.Skill4) return ZdhInput.KeyDown(Skill4VirtualKey);
            return false;
        }

        private bool ActionUp(ActionKey key)
        {
            if (key == ActionKey.LeftSkill) return ZdhInput.MouseUpLeft();
            if (key == ActionKey.RightSkill) return ZdhInput.MouseUpRight();
            if (key == ActionKey.Skill1) return ZdhInput.KeyUp(Skill1VirtualKey);
            if (key == ActionKey.Skill2) return ZdhInput.KeyUp(Skill2VirtualKey);
            if (key == ActionKey.Skill3) return ZdhInput.KeyUp(Skill3VirtualKey);
            if (key == ActionKey.Skill4) return ZdhInput.KeyUp(Skill4VirtualKey);
            return false;
        }

        private bool TryGetCursorClient(out int x, out int y)
        {
            x = Hud.Window.CursorX;
            y = Hud.Window.CursorY;

            int screenX;
            int screenY;
            if (!ZdhInput.TryGetCursor(out screenX, out screenY)) return false;
            return TryScreenToClient(screenX, screenY, out x, out y);
        }

        private bool IsCursorNear(float x, float y, float tolerance)
        {
            int cursorX;
            int cursorY;
            TryGetCursorClient(out cursorX, out cursorY);
            float dx = cursorX - x;
            float dy = cursorY - y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        private float CursorDistanceFrom(float x, float y)
        {
            int cursorX;
            int cursorY;
            TryGetCursorClient(out cursorX, out cursorY);
            float dx = cursorX - x;
            float dy = cursorY - y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void BeginCursorRestore(int now)
        {
            CaptureUserCursorIntent();
            PrepareIntentRestoreTarget();
            _cast.RestoreDeadlineTick = unchecked(now + Math.Max(30, CursorRestoreTimeoutMs));
            _cast.RestoreAttempts = 0;
            _cast.Stage = CastStage.Restore;
            _cast.DueTick = now;
            RequestDhStrafePause(CursorRestoreTimeoutMs + CursorRestoreSettleMs + 120);
            s7o_ZDH_HelperMetrics.LastRestoreConfirmed = false;
            s7o_ZDH_HelperMetrics.LastRestoreGuardMaxDrift = 0;
            _cast.RestoreGuardCorrections = 0;

            if (!_cast.CursorOwned)
            {
                CompleteCursorRestore(now, true);
                return;
            }

            _cast.RestoreAttempts = 1;
            if (SetCastCursor(_cast.RestoreX, _cast.RestoreY)
                && IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels))
                CompleteCursorRestore(now, true);
        }

        private void AdvanceCursorRestore(int now)
        {
            if (!_cast.CursorOwned)
            {
                CompleteCursorRestore(now, true);
                return;
            }

            CaptureUserCursorIntent();
            PrepareIntentRestoreTarget();
            if (IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels))
            {
                CompleteCursorRestore(now, true);
                return;
            }

            if (!Reached(now, _cast.DueTick)) return;

            _cast.RestoreAttempts++;
            if (SetCastCursor(_cast.RestoreX, _cast.RestoreY)
                && IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels))
            {
                CompleteCursorRestore(now, true);
                return;
            }

            _cast.DueTick = unchecked(now + Math.Max(1, CursorRestoreRetryMs));
            RequestDhStrafePause(CursorRestoreRetryMs + 80);

            if (Reached(now, _cast.RestoreDeadlineTick))
            {
                bool confirmed = IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels);
                if (!confirmed)
                    SetCursorSafetyBlock(now, _cast.RestoreX, _cast.RestoreY);
                CompleteCursorRestore(now, confirmed);
            }
        }


        private bool GuardCursorRestoreSettle(int now)
        {
            int cursorX;
            int cursorY;
            TryGetCursorClient(out cursorX, out cursorY);

            int restoreDrift = CursorDistance(cursorX, cursorY, _cast.RestoreX, _cast.RestoreY);
            int aimDistance = CursorDistance(cursorX, cursorY, _cast.AimX, _cast.AimY);
            s7o_ZDH_HelperMetrics.LastRestoreGuardMaxDrift =
                Math.Max(s7o_ZDH_HelperMetrics.LastRestoreGuardMaxDrift, restoreDrift);

            bool unexpectedEdge = IsCursorNearWindowEdge(cursorX, cursorY)
                && !IsCursorNearWindowEdge(_cast.RestoreX, _cast.RestoreY);
            bool staleAimReturn = restoreDrift >= CursorRestoreGuardDriftPixels
                && aimDistance <= CursorRestoreGuardAimTolerancePixels
                && aimDistance + 80 < restoreDrift;
            if (!unexpectedEdge && !staleAimReturn) return false;


            if (_cast.RestoreGuardCorrections >= CursorRestoreGuardMaxCorrections)
            {
                bool clamped = SetCursorClient(_cast.RestoreX, _cast.RestoreY);
                if (!clamped)
                {
                    s7o_ZDH_HelperMetrics.LastRestoreConfirmed = false;
                    SetCursorSafetyBlock(now, _cast.RestoreX, _cast.RestoreY);
                }
                return false;
            }

            _cast.RestoreGuardCorrections++;
            if (!SetCursorClient(_cast.RestoreX, _cast.RestoreY))
            {
                s7o_ZDH_HelperMetrics.LastRestoreConfirmed = false;
                SetCursorSafetyBlock(now, _cast.RestoreX, _cast.RestoreY);
                return false;
            }

            _cast.DueTick = unchecked(now + Math.Max(1, CursorRestoreSettleMs));
            RequestDhStrafePause(Math.Max(80, CursorRestoreSettleMs + 80));
            return true;
        }

        private bool IsCursorNearWindowEdge(float x, float y)
        {
            Size size = Hud.Window.Size;
            int margin = CursorRestoreGuardEdgeMarginPixels;
            return x < margin || y < margin
                || x >= size.Width - margin || y >= size.Height - margin;
        }

        private static int CursorDistance(int x1, int y1, int x2, int y2)
        {
            long dx = x1 - x2;
            long dy = y1 - y2;
            return (int)Math.Min(int.MaxValue, Math.Round(Math.Sqrt(dx * dx + dy * dy)));
        }

        private void CompleteCursorRestore(int now, bool confirmed)
        {
            int cursorX;
            int cursorY;
            TryGetCursorClient(out cursorX, out cursorY);
            int dx = cursorX - _cast.RestoreX;
            int dy = cursorY - _cast.RestoreY;
            s7o_ZDH_HelperMetrics.LastRestoreConfirmed = confirmed;
            PublishCursorIntent();
            _cast.CursorOwned = false;
            _cast.RestoreTick = now;

            if (!confirmed)
            {
                BeginVerificationAfterRestore(now);
                return;
            }

            int settleUntil = unchecked(now + Math.Max(1, CursorRestoreSettleMs));
            int minimumUntil = unchecked(_cast.StartedTick + Math.Max(1, _cast.MinimumLeaseMs));
            _cast.Stage = CastStage.RestoreSettle;
            _cast.DueTick = unchecked(minimumUntil - settleUntil) > 0 ? minimumUntil : settleUntil;
            RequestDhStrafePause(Math.Max(80, unchecked(_cast.DueTick - now) + 80));
        }

        private void BeginVerificationAfterRestore(int now)
        {
            if (!s7o_ZDH_HelperMetrics.LastRestoreConfirmed
                && PointInsideWindow(_cast.RestoreX, _cast.RestoreY))
            {
                bool restored = SetCursorClient(_cast.RestoreX, _cast.RestoreY)
                    && IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels);
                if (!restored)
                {
                    _cast.CursorOwned = true;
                    _cast.RestoreDeadlineTick = unchecked(now + 40);
                    _cast.RestoreAttempts = 0;
                    _cast.Stage = CastStage.Restore;
                    _cast.DueTick = now;
                    RequestDhStrafePause(80);
                    return;
                }
                s7o_ZDH_HelperMetrics.LastRestoreConfirmed = true;
                if (_cursorSafetyBlocked)
                {
                    ClearCursorSafetyBlock();
                }
            }

            s7o_ZDH_HelperMetrics.LastLeaseDurationMs = Elapsed(_cast.StartedTick, now);
            if (_cast.SentryBurstChild && _sentryBurst.Mode != SentryBurstMode.None)
            {
                RequestDhStrafePause(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                SuppressDhStrafePrimary(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                _cast.Stage = CastStage.Verify;
                _cast.VerifyUntilTick = unchecked(now + Math.Max(1, _cast.VerifyMs));
                return;
            }

            _lastPauseReleasedTick = now;
            int primaryQuietMs = _cast.RequiresStrafePause ? GetPostCastPrimaryQuietMs(_cast.Kind) : 0;
            if (primaryQuietMs > 0) SuppressDhStrafePrimary(primaryQuietMs);
            ReleaseStandstillInput();
            ReleaseDhStrafePause();
            if (_cast.Kind == CastKind.Multishot && _cast.InputSent)
            {
                CompleteMultishotDispatch(now);
                return;
            }
            _cast.Stage = CastStage.Verify;
            _cast.VerifyUntilTick = unchecked(now + Math.Max(1, _cast.VerifyMs));
        }

        private bool RestoreCursorImmediately()
        {
            if (!_cast.CursorOwned || Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return true;

            CaptureUserCursorIntent();
            PrepareIntentRestoreTarget();
            bool sent = SetCastCursor(_cast.RestoreX, _cast.RestoreY);
            bool confirmed = sent && IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels);
            s7o_ZDH_HelperMetrics.LastRestoreConfirmed = confirmed;
            PublishCursorIntent();
            if (!confirmed)
                SetCursorSafetyBlock(Environment.TickCount, _cast.RestoreX, _cast.RestoreY);
            return confirmed;
        }

        private void InitializeCursorIntent()
        {
            _cast.CursorReferenceX = _cast.SavedCursorX;
            _cast.CursorReferenceY = _cast.SavedCursorY;
            _cast.CursorReferenceValid = true;
            _cast.UserCursorDeltaX = 0;
            _cast.UserCursorDeltaY = 0;
            _cast.UserCursorDeltaSamples = 0;
            int heroX;
            int heroY;
            _cast.SavedHeroScreenValid = TryGetHeroScreen(out heroX, out heroY);
            _cast.SavedHeroScreenX = heroX;
            _cast.SavedHeroScreenY = heroY;
        }

        private bool SetCastCursor(int x, int y)
        {
            bool sent = SetCursorClient(x, y);
            if (sent)
            {
                _cast.CursorReferenceX = x;
                _cast.CursorReferenceY = y;
                _cast.CursorReferenceValid = true;
            }
            return sent;
        }

        private void CaptureUserCursorIntent()
        {
            if (!_cast.CursorOwned || !_cast.CursorReferenceValid) return;
            int cursorX;
            int cursorY;
            if (!TryGetCursorClient(out cursorX, out cursorY)) return;
            int dx = cursorX - _cast.CursorReferenceX;
            int dy = cursorY - _cast.CursorReferenceY;
            if (dx != 0 || dy != 0)
            {
                long totalX = (long)_cast.UserCursorDeltaX + dx;
                long totalY = (long)_cast.UserCursorDeltaY + dy;
                _cast.UserCursorDeltaX = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, totalX));
                _cast.UserCursorDeltaY = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, totalY));
                _cast.UserCursorDeltaSamples++;
            }
            _cast.CursorReferenceX = cursorX;
            _cast.CursorReferenceY = cursorY;
        }

        private void PrepareIntentRestoreTarget()
        {
            Size size = Hud.Window.Size;
            double dx = _cast.UserCursorDeltaX;
            double dy = _cast.UserCursorDeltaY;
            double magnitude = Math.Sqrt(dx * dx + dy * dy);
            double maximumIntent = Math.Max(0, CursorIntentMaxRestorePixels);
            if (maximumIntent > 0 && magnitude > maximumIntent)
            {
                double scale = maximumIntent / magnitude;
                dx *= scale;
                dy *= scale;
            }

            long targetX = (long)_cast.SavedCursorX + (long)Math.Round(dx);
            long targetY = (long)_cast.SavedCursorY + (long)Math.Round(dy);
            int maximumX = Math.Max(0, size.Width - 1);
            int maximumY = Math.Max(0, size.Height - 1);
            _cast.RestoreX = (int)Math.Max(0, Math.Min(maximumX, targetX));
            _cast.RestoreY = (int)Math.Max(0, Math.Min(maximumY, targetY));
        }

        private bool TryGetHeroScreen(out int x, out int y)
        {
            x = 0;
            y = 0;
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null
                || Hud.Game.Me.ScreenCoordinate == null) return false;
            x = (int)Math.Round(Hud.Game.Me.ScreenCoordinate.X);
            y = (int)Math.Round(Hud.Game.Me.ScreenCoordinate.Y);
            return true;
        }

        private void PublishCursorIntent()
        {
            int heroX;
            int heroY;
            bool heroValid = TryGetHeroScreen(out heroX, out heroY);
            int appliedX = _cast.RestoreX - _cast.SavedCursorX;
            int appliedY = _cast.RestoreY - _cast.SavedCursorY;
            s7o_ZDH_HelperMetrics.LastCursorIntentRawDeltaX = _cast.UserCursorDeltaX;
            s7o_ZDH_HelperMetrics.LastCursorIntentRawDeltaY = _cast.UserCursorDeltaY;
            s7o_ZDH_HelperMetrics.LastCursorIntentDeltaX = appliedX;
            s7o_ZDH_HelperMetrics.LastCursorIntentDeltaY = appliedY;
            s7o_ZDH_HelperMetrics.LastCursorIntentClamped =
                appliedX != _cast.UserCursorDeltaX || appliedY != _cast.UserCursorDeltaY;
            s7o_ZDH_HelperMetrics.LastCursorIntentSamples = _cast.UserCursorDeltaSamples;
            s7o_ZDH_HelperMetrics.LastCursorIntentHeroShiftX = heroValid && _cast.SavedHeroScreenValid
                ? heroX - _cast.SavedHeroScreenX : 0;
            s7o_ZDH_HelperMetrics.LastCursorIntentHeroShiftY = heroValid && _cast.SavedHeroScreenValid
                ? heroY - _cast.SavedHeroScreenY : 0;
            s7o_ZDH_HelperMetrics.LastCursorIntentRestoreX = _cast.RestoreX;
            s7o_ZDH_HelperMetrics.LastCursorIntentRestoreY = _cast.RestoreY;
            if (_cast.UserCursorDeltaSamples > 0)
                s7o_ZDH_HelperMetrics.LastCursorIntentSequence++;
        }

        private bool SetCursorClient(int x, int y)
        {
            if (!PointInsideWindow(x, y) || !Hud.Window.IsForeground) return false;
            int screenX;
            int screenY;
            return TryClientToScreen(x, y, out screenX, out screenY)
                && ZdhInput.SetCursor(screenX, screenY);
        }

        private bool TryClientToScreen(int x, int y, out int screenX, out int screenY)
        {
            screenX = 0;
            screenY = 0;
            Point offset = Hud.Window.Offset;
            long sx = (long)x + offset.X;
            long sy = (long)y + offset.Y;
            if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
            screenX = (int)sx;
            screenY = (int)sy;
            return true;
        }

        private bool TryScreenToClient(int screenX, int screenY, out int x, out int y)
        {
            x = 0;
            y = 0;
            Point offset = Hud.Window.Offset;
            long cx = (long)screenX - offset.X;
            long cy = (long)screenY - offset.Y;
            if (cx < int.MinValue || cx > int.MaxValue || cy < int.MinValue || cy > int.MaxValue) return false;
            x = (int)cx;
            y = (int)cy;
            return true;
        }

        private bool PointInsideCastArea(float x, float y)
        {
            Size size = Hud.Window.Size;
            float bottom = Math.Min(size.Height - 140f, size.Height * GroundCastSafeBottomRatio);
            return x >= 24f && y >= 24f && x < size.Width - 24f && y < bottom;
        }

        private bool PointInsideWindow(float x, float y)
        {
            Size size = Hud.Window.Size;
            return x >= 0 && y >= 0 && x < size.Width && y < size.Height;
        }

        private static int Elapsed(int then, int now)
        {
            return then == int.MinValue ? int.MaxValue : unchecked(now - then);
        }

        private static bool Reached(int now, int due)
        {
            return due == int.MinValue || unchecked(now - due) >= 0;
        }

        private static string T(string key, string fallback)
        {
            return s7o_Localization.Get(key, fallback);
        }

        private static class ZdhInput
        {
            private const uint InputMouse = 0;
            private const uint InputKeyboard = 1;
            private const uint MouseLeftDown = 0x0002;
            private const uint MouseLeftUp = 0x0004;
            private const uint MouseRightDown = 0x0008;
            private const uint MouseRightUp = 0x0010;
            private const uint KeyUpFlag = 0x0002;

            [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public UNION Data; }
            [StructLayout(LayoutKind.Explicit)] private struct UNION { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
            [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public IntPtr Extra; }
            [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }
            [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
            [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
            [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
            [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
            [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);

            public static bool SetCursor(int x, int y) { return SetCursorPos(x, y); }
            public static bool TryGetCursor(out int x, out int y)
            {
                POINT point;
                bool ok = GetCursorPos(out point);
                x = ok ? point.X : 0;
                y = ok ? point.Y : 0;
                return ok;
            }
            public static bool IsVirtualKeyDown(ushort vk) { return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0; }
            public static bool MouseDownLeft() { return Mouse(MouseLeftDown); }
            public static bool MouseUpLeft() { return Mouse(MouseLeftUp); }
            public static bool MouseDownRight() { return Mouse(MouseRightDown); }
            public static bool MouseUpRight() { return Mouse(MouseRightUp); }
            public static bool KeyDown(ushort vk) { return Keyboard(vk, false); }
            public static bool KeyUp(ushort vk) { return Keyboard(vk, true); }

            private static bool Mouse(uint flags)
            {
                var input = new[] { new INPUT { Type = InputMouse, Data = new UNION { Mouse = new MOUSEINPUT { Flags = flags } } } };
                return SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1;
            }

            private static bool Keyboard(ushort vk, bool up)
            {
                var input = new[] { new INPUT { Type = InputKeyboard, Data = new UNION { Keyboard = new KEYBDINPUT { Vk = vk, Flags = up ? KeyUpFlag : 0 } } } };
                return vk != 0 && SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1;
            }
        }
    }
}
