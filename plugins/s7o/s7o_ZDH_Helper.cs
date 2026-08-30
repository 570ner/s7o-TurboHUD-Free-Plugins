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
        // Per-eligible-elite MFD coverage. Juggernauts remain excluded from uptime metrics.
        public static long MarkedForDeathMilliseconds;
        public static long MarkedForDeathEligibleMilliseconds;
        // Historical MFD presence metric retained for tooltip context only: one successful sample
        // means at least one eligible non-Juggernaut elite had MFD. Gameplay never reads this.
        public static long MarkedForDeathPresenceMilliseconds;
        public static long MarkedForDeathPresenceEligibleMilliseconds;

        public static int Percent(long value)
        {
            return EligibleMilliseconds <= 0 ? 0 : (int)Math.Round(value * 100.0 / EligibleMilliseconds);
        }

        public static int MarkedForDeathPercent()
        {
            return MarkedForDeathEligibleMilliseconds <= 0 ? 0 : (int)Math.Round(
                MarkedForDeathMilliseconds * 100.0 / MarkedForDeathEligibleMilliseconds);
        }

        public static int MarkedForDeathPresencePercent()
        {
            return MarkedForDeathPresenceEligibleMilliseconds <= 0 ? 0 : (int)Math.Round(
                MarkedForDeathPresenceMilliseconds * 100.0 / MarkedForDeathPresenceEligibleMilliseconds);
        }

        public static int AveragePercent()
        {
            if (EligibleMilliseconds <= 0 && MarkedForDeathEligibleMilliseconds <= 0) return 0;
            return (int)Math.Round((Percent(IceblinkMilliseconds)
                + Percent(DamageMilliseconds) + MarkedForDeathPercent()) / 3.0);
        }

        // Historical-style average used only as tooltip context. IB/Odyssey are unchanged; MFD
        // uses presence on at least one eligible elite instead of per-elite coverage efficiency.
        public static int UptimeAveragePercent()
        {
            if (EligibleMilliseconds <= 0 && MarkedForDeathPresenceEligibleMilliseconds <= 0) return 0;
            return (int)Math.Round((Percent(IceblinkMilliseconds)
                + Percent(DamageMilliseconds) + MarkedForDeathPresencePercent()) / 3.0);
        }

        public static void ResetUptime()
        {
            EligibleMilliseconds = 0;
            IceblinkMilliseconds = 0;
            DamageMilliseconds = 0;
            MarkedForDeathMilliseconds = 0;
            MarkedForDeathEligibleMilliseconds = 0;
            MarkedForDeathPresenceMilliseconds = 0;
            MarkedForDeathPresenceEligibleMilliseconds = 0;
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
        public int MultishotMaintenanceMs = 2100;
        // When the shared Combat slot is free, allow an efficient cone refresh from about
        // 1.8 seconds instead of spending that slot on a filler Entangle.
        public int EfficientMultishotLeadMs = 300;
        public float EfficientMultishotCoverageRatio = 0.80f;
        public int AttackMultishotMaintenanceMs = 2100;
        // Iceblink lasts about 3 seconds. Start RG maintenance early enough to leave
        // room for the existing 600 ms validation window and one bounded miss retry.
        public int BossMultishotMaintenanceMs = 1500;
        // Odyssey lasts about 2 seconds on the RG. Refresh proactively so the boss never
        // has to become fully missing before the standalone scheduler reacts.
        public int BossEntangleMaintenanceMs = 1500;
        public int MultishotFailedRetryMs = 250;
        // A native Multishot animation with no Iceblink is an effect/LOS miss, not an input failure.
        // Keep that elite live, but briefly rotate to other due elites before retrying the same angle.
        public int MultishotEffectMissRetryBaseMs = 750;
        public int MultishotEffectMissRetryStepMs = 250;
        public int MultishotEffectMissRetryMaxMs = 1250;
        public int MultishotRefreshRetryMs = 500;
        public int IceblinkExpectedDurationMs = 3000;
        public int IceblinkValidationSlackMs = 250;
        public int IceblinkFirstObservedGraceMs = 750;
        public int IceblinkMaxRefreshAttempts = 2;
        public int IceblinkPrimaryPreemptLeadMs = 300;
        // Once a real Iceblink refresh is due, include elites that will become due shortly in
        // the same cone optimization. This does not arm extra directional shots or retain a lease.
        public int MultishotConePlanningHorizonMs = 650;
        public int CombatSupportPrimaryQuietMs = 180;
        public int SpeedSupportPrimaryQuietMs = 120;
        public int BossSupportPrimaryQuietMs = 80;
        public int PrimaryPreemptLeaseMs = 350;
        public float PylonInteractionPauseRange = 15f;
        public int FailedCastRetryMs = 450;
        public int GlobalCastGapMs = 450;
        public int ManualDebuffCastGapMs = 50;
        public int ManualDebuffReleaseMovementMs = 200;
        public int ElectrifiedAlertPopMs = 560;
        public int ElectrifiedAlertHoldMs = 1800;
        public int ElectrifiedAlertFadeMs = 900;
        public int ElectrifiedAlertRearmMs = 650;
        public int ElectrifiedAlertPulsePeriodMs = 720;
        public float ElectrifiedAlertRearmDistance = 70f;
        public float ElectrifiedAlertTextSize = 11.5f;
        public int EntangleAimSettleMs = 16;
        // One bounded preview frame lets Diablo consume the synthetic support aim before
        // skill-down. Readiness waits happen before cursor ownership; if readiness is lost
        // during this preview, restore immediately and retry without keeping the cursor pinned.
        public int SupportCursorPreviewMs = 31;
        // Conservative support-input holds. FREEHUD runs this asynchronously, so these are
        // key-down durations, not sleeps; the actual release occurs on a later collect frame.
        public int EntangleSkillHoldMs = 35;
        public int MultishotSkillHoldMs = 45;
        public int GroundSkillHoldMs = 50;
        public int SentrySkillHoldMs = 50;
        public int MinimumCastLeaseMs = 105;
        public int StrafePauseAckTimeoutMs = 80;
        public int CastPauseHardLimitMs = 200;
        public int MultishotPreInputHardLimitMs = 520;
        public int GroundPreInputHardLimitMs = 380;
        public int SentryPreInputHardLimitMs = 360;
        public int CastPostInputHardLimitMs = 320;
        public int CursorRestoreTolerancePixels = 10;
        // One rescue write is allowed only when the first restore clearly remained near the
        // synthetic aim instead of returning toward the user restore target.
        public int CursorRestoreRescueDistancePixels = 240;
        // Cursor restore preserves trusted physical steering at full magnitude. Synthetic absolute
        // cursor writes are filtered before they can enter UserCursorDelta, then only the actual
        // game-window boundary clamps the reconstructed endpoint.
        public int CursorSyntheticEchoTolerancePixels = 14;
        // Fallback only: if Diablo has not yet exposed animation evidence that the support input
        // was consumed, keep the previous one-frame post-input settle before restoring the cursor.
        public int CursorPostInputSettleMs = 24;
        public int CursorSafetyRecoveryMs = 350;
        private const int MultishotNativeAnimationCorrelationMs = 350;
        private const int EliteSentryUrgentAttemptLimit = 2;
        public int AimCorrectionRetryMs = 16;
        public int MovementModeCastGapMs = 650;
        public int BossStandaloneCastGapMs = 350;
        public int UrgentRetryGapMs = 300;
        public int MovementUrgentRetryGapMs = 450;
        public int BossUrgentRetryGapMs = 200;
        public int UrgentRetryLifetimeMs = 1800;
        public int AttackMovementWindowMs = 1000;
        // Combat-mode idle filler uses a shorter movement slot than ordinary maintenance so
        // movement speed stays predictable without accelerating real debuff/Sentry priorities.
        public int CombatCadenceMovementWindowMs = 800;
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
        public float BossStandaloneRange = 65f;
        public int TravelSampleMs = 250;
        public float TravelEngagedClusterRange = 42f;
        public float MobilityAdvanceDistance = 50f;
        public float MobilityAdvanceResetSpeed = 6f;
        public int MobilityAdvanceSettleMs = 500;
        public int MobilityAdvanceProgressHoldMs = 700;
        // Synchronous checks only confirm Diablo accepted the activation. Gameplay effects
        // (Iceblink, Valley coverage, Sentry placement) are observed asynchronously afterward.
        public int EntangleVerifyMs = 220;
        public int MultishotCommitMs = 200;
        public int MultishotVerifyMs = 600; // asynchronous Iceblink/effect window
        public int MarkedForDeathVerifyMs = 340;
        public int SentryVerifyMs = 260;
        public int MarkedForDeathPrimaryQuietMs = 180;
        public int MultishotPrimaryQuietMs = 420;
        public int SentryPrimaryQuietMs = 350;
        public int MarkedForDeathRecastMs = 2500;
        public int MarkedForDeathUrgentRecastMs = 550;
        // Once an existing Valley already covers elites, require a sustained material gain
        // before moving it again. Initial/no-coverage setup still uses the urgent path above.
        public int MfdEliteGainStableMs = 200;
        public int MfdEliteGainRecastMs = 600;
        // A one-elite gain must persist longer when the current Valley already covers most
        // eligible elites. Multi-elite, boss, focus, and genuinely incomplete fields stay fast.
        public int MfdSingleEliteGainStableMs = 500;
        public int MfdSingleEliteGainRecastMs = 900;
        public float MfdSatisfiedCoverageRatio = 0.66f;
        // Low-priority field centering: protect elites already covered by a live Valley when
        // one is close to the physical edge. This lane uses the normal 2.5s MFD recast gate.
        public float MfdEdgeSafetyMargin = 2.5f;
        public float MfdEdgeMinimumImprovement = 2.0f;
        public int MfdEdgeStableMs = 400;
        public int NewElitePriorityMs = 3000;
        public int MfdSentryBlockedYieldMs = 1200;
        // Never let the first failed hard-MFD attempt open the Guardian burst.
        // After repeated real failures, permit one standalone Sentry turn before MFD retries.
        public int MfdSentryFailureYieldAttempts = 2;
        public int SentryRecastMs = 300;
        public float SentrySevereOverlapDistance = 8f;
        public int SentrySevereOverlapPairThreshold = 3;
        public float SentryRelocationSinkRadius = 5f;
        public int SentryRelocationSinkHoldMs = 8000;
        public int SentryRelocationSinkRepeatThreshold = 2;
        public int SentryRelocationSinkBackoffMs = 4500;
        public float SentryRelocationPlayerMoveClearDistance = 8f;
        public float SentryRelocationAnchorMoveClearDistance = 14f;
        public int InitialSetupBurstGapMs = 100;
        public float SentryStackedDistance = 14f;
        public float SentryProtectedMinSeparation = 16f;
        public float SentryDistinctCoreSeparation = 18f;
        public float SentryMinScreenSeparationPixels = 32f;
        public float SentryVisiblePatternMinScale = 0.75f;
        public float SentryVisiblePatternScaleStep = 0.05f;
        public int SentryFailedRetryMs = 350;
        public int SentryRejectedPositionHoldMs = 1400;
        public float SentryRejectedPositionRadius = 9f;
        public int SentrySetupPrimaryQuietMs = 120;
        public int SentrySetupPreemptLeaseMs = 260;
        public int SentryCoreBurstAbsoluteMaxMs = 1700;
        // Release movement between Sentry children when one continuous burst lease grows unsafe.
        public int SentryContinuousLeaseMaxMs = 800;
        public int SentryCompletionBurstAbsoluteMaxMs = 1100;
        public int SentryBurstAcquireMaxMs = 260;
        public int SentryBurstMovementSettleMaxMs = 300;
        public int SentryCoreBurstMaxAttemptsPerEngagement = 3;
        public int SentryRelevanceDeficitStabilityMs = 180;
        // Once a five-Sentry field has been established, require a sustained relevance loss
        // before replacing it so transient orbiting/elite movement cannot flood the support queue.
        public int SentryFullFieldRelevanceStabilityMs = 1000;
        public int SentryFullFieldRecastMs = 4000;
        public int SentryRollingRefreshLeadMs = 15000;
        public int SentryRollingRefreshEmergencyLeadMs = 9000;
        public int EliteSentryCoverageInitialMs = 300;
        public int EliteSentryCoverageStepMs = 500;
        public int EliteSentryCoverageMaxMs = 2000;
        public int EliteSentryCoverageResetMs = 5000;
        public int EliteSentryCoverageMaxPlacements = 5;
        public int SpeedCombatDwellMs = 1500;
        public int SpeedCombatSampleMs = 100;
        public float SpeedCombatMaxStationaryNetDistance = 5f;
        public float SpeedCombatMaxStationaryPathDistance = 8f;
        public float SpeedCombatMaxStationarySpeed = 6f;
        public float SpeedCombatMinOrbitPathDistance = 8f;
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
        public int SentryDpsEmergencyStableMs = 450;
        public float SentryBonusCircleRadius = 10f;
        // A bonus circle is an area, not a point target. Require the entire circle to fit
        // inside Guardian coverage; the 0.5-yard margin avoids edge-only nominal coverage.
        public float SentryBonusCircleCoverageSafetyMargin = 0.5f;
        public float SentryEliteComfortRadius = 13f;
        public float LocalSentryProtectionHealthPct = 50f;
        public int LocalSentryProtectionStationaryMs = 900;
        public float LocalSentryProtectionMaxSpeed = 3f;
        public int SentryPackSlots = 5;
        public int InitialSentryFieldCount = 3;
        public float SentryFieldRelevanceRadius = 35f;
        public float SentryPatternColumnSpacing = 24f;
        public float SentryPatternMatchRadius = 12f;
        public float SentryMinSeparation = 22f;
        public float MultishotAimDistance = 80f;
        public float MultishotAimMinimumScreenDistance = 110f;
        // LightningMod's Multishot cone geometry uses the Demon Hunter chest plane (~6.2 world Z)
        // as the skill origin. Keep XY targeting on actor floor cores, but project direct rays
        // through that native skill-origin plane so point-blank cursor direction matches the cast.
        public float MultishotAimOriginZOffset = 6.2f;
        public float MultishotCloseRangeDirectAimDistance = 5f;
        public float MultishotCloseRangeAimPastTargetPixels = 48f;
        public float MultishotSafeTopRatio = 0.08f;
        public float MultishotSafeBottomRatio = 0.78f;
        public float MultishotSafeSideRatio = 0.04f;
        public float GroundCastSafeBottomRatio = 0.84f;

        // Click safety uses the same 1920x1080 red-mask regions as HUD Menu AutoSnap,
        // uniformly scaled and edge/center anchored for aspect-safe placement. The minimap is intentionally not blocked.
        private const float ClickGuardReferenceWidth = 1920f;
        private const float ClickGuardReferenceHeight = 1080f;
        private const float ClickGuardRayProbeStepPixels = 24f;
        private static readonly RectangleF[] ClickGuardRects1920x1080 =
        {
            new RectangleF(116f, 11f, 76f, 71f),
            new RectangleF(34f, 57f, 58f, 61f),
            new RectangleF(871f, 2f, 179f, 21f),
            new RectangleF(1644f, 23f, 60f, 26f),
            new RectangleF(1816f, 120f, 25f, 15f),
            new RectangleF(1863f, 363f, 31f, 29f),
            new RectangleF(8f, 973f, 85f, 80f),
            new RectangleF(315f, 893f, 1289f, 187f),
            new RectangleF(1754f, 961f, 157f, 83f),
        };
        // Conservative 40-wide triangle at 80-yard range (~14.04° half-angle).
        public float MultishotRange = 80f;
        public float MultishotConeHalfAngleDegrees = 14.04f;
        // Keep every currently due elite safely inside the conservative cone before optimizing free refresh coverage.
        public float MultishotDueEliteSafeAngleDegrees = 14f;
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
        // Valley of Death is a 15-yard radius. Monster hitbox is added by IsInsideValley().
        public float ValleyRadius = 15f;
        // This bonus only breaks equal-coverage placement decisions; elite count remains primary.
        public float MfdNearPlayerPriorityRange = 30f;
        public float MfdNearPlayerPriorityBonus = 1.5f;
        public float GuardianRadius = 16f;
        // Native Sentry placement is limited to about 60 yards. Keep the exact tested value as
        // the direct-cast authority and combine it with Guardian coverage radius only when
        // rejecting targets that are geometrically impossible to satisfy from any legal cast.
        private const float NativeSentryPlacementMaxRangeYards = 60f;
        public int MfdNativeDropoutGraceMs = 350;
        public int MfdDensityMinimumGain = 3;
        public float MfdDensityMinimumGainRatio = 0.20f;
        // Bounded window in which a real Combat/new-pack opening may run before a preventive
        // at-cap Momentum refresh. An in-flight Primary and any <20-stack recovery stay strict.
        public int CombatOpeningPriorityMaxMs = 2600;
        // Allow newly cast ground effects this long to appear before actor adoption expires.
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
        private static int _manualDebuffMovementUntilTick = int.MinValue;

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

        private void RecordCombatActionCompleted(int now)
        {
            _lastPauseReleasedTick = now;
            s7o_DHStrafePrimaryPlugin.NotifySupportActionCompletedForZdh(now);
        }

        public static bool IsDhStrafePrimarySuppressed(int now)
        {
            return _dhStrafePrimarySuppressUntilTick != int.MinValue
                && unchecked(_dhStrafePrimarySuppressUntilTick - now) > 0;
        }

        public static bool IsManualDebuffMovementRequested(int now)
        {
            return _manualDebuffMovementUntilTick != int.MinValue
                && unchecked(_manualDebuffMovementUntilTick - now) > 0;
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
        private const uint OculusRingSno = 402461;
        private const uint TriuneProxySno = 488071;
        private const uint LegacyBombardiersRucksackSno = 318804;
        private const ActorSnoEnum GuardianSentryActor = ActorSnoEnum._dh_sentry_addsshield;
        private static readonly uint[] IdentityAttributeModifiers = { 0u, 0xFFFFFu, 0xFFFFFFFFu, 2147483647u };

        private sealed class TargetState
        {
            public double Health;
            public int FirstSupportTick = int.MinValue;
            public int LastDamageTick = int.MinValue;
            public int LastSeenTick = int.MinValue;
            public int LastEntangleAttempt = int.MinValue;
            public int LastMultishotAttempt = int.MinValue;
            public int IceblinkMissingSinceTick = int.MinValue;
            public int IceblinkConfirmedTick = int.MinValue;
            public int PendingIceblinkRefreshTick = int.MinValue;
            public int PendingIceblinkAttemptCount;
            public int ConsecutiveMultishotMisses;
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

        public enum CastKind { None, Entangle, Multishot, MarkedForDeath, Sentry }
        public enum CastStage { Idle, Lease, Aim, Hold, PostInputSettle, Restore, RestoreSettle, Verify }
        public enum SentryBurstMode { None, Core, Completion }
        public enum SentryBurstStage { Idle, Acquire, Settle, Ready }

        private sealed class RuntimeState
        {
            public int SentryDesired;
            public bool HighFrequencyMode;
            public float SentryAnchorX;
            public float SentryAnchorY;
            public int SentryPlacementDeficit;
            public int SentryCapacity;
            public int SentryOwned;
            public int SentryRelevant;
            public int SentryLocalCoreRelevant;
            public int SentryDistinctCoreRelevant;
            public int SentryDistinctCoreDeficit;
            public int SentryHardDeficit;
            public int SentryOldestAgeMs = -1;
            public int SentryCharges;
            public bool SentryPlanValid;
            public bool OpeningSentryBurstsClosed;
            public int CoreBurstAttempts;
            public int CoreBurstAttemptLimit;
            public bool SentryRelocationBackoff;
            public int SentryRelocationSinkCount;
            public bool SentryRollingRefreshDue;
            public bool SentryRollingRefreshReady;
            public bool SentryFairnessDemand;
            public int SentryFairnessTurns;
            public int SentryFairnessBudget;
            public bool SentryFairnessDue;
            public bool TrashFightLatched;
            public int TrashFightLatchBodies;
            public bool ProtectedSentryCoverageMissing;
            public bool EliteSentryCoverageMissing;
            public int EliteSentryUncoveredCount;
            public int EliteSentryReadyCount;
            public uint EliteSentryPriorityAcd;
            public int EliteSentryPriorityAgeMs = -1;
            public int EliteSentryPriorityDelayMs = -1;
            public int EliteSentryPriorityAttempts;
            public bool EliteSentryUrgent;
            public bool PlayerSentryProtectionMissing;
            public bool BonusCircleSentryCoverageMissing;
            public bool UrgentBonusCircleSentryCoverageMissing;
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
            public int InputDownTick;
            public int AimSettleMs;
            public int HoldMs;
            public int MinimumLeaseMs;
            public int VerifyMs;
            public int SavedCursorX;
            public int SavedCursorY;
            public int CursorReferenceX;
            public int CursorReferenceY;
            public bool CursorReferenceValid;
            public int UserCursorDeltaX;
            public int UserCursorDeltaY;
            public bool CursorSyntheticWritePending;
            public int CursorSyntheticFromX;
            public int CursorSyntheticFromY;
            public int CursorSyntheticTargetX;
            public int CursorSyntheticTargetY;
            public int CursorSyntheticEchoRejectCount;
            public int AimX;
            public int AimY;
            public bool StandstillHeld;
            public bool ActionHeld;
            public bool CursorOwned;
            public int RestoreX;
            public int RestoreY;
            public bool RestoreWriteSent;
            public bool RestoreRescueAttempted;
            public bool BaselineTargetFlag;
            public bool SawCastAnimation;
            public bool SawNativeMultishotAnimation;
            public bool SawNativeMfdAnimation;
            public AnimSnoEnum PreInputAnimationSno;
            public bool PreInputAnimationSnoValid;
            public bool TrashInitialMultishot;
            public bool InputSent;
            public bool CancellationPending;
            public string CancellationReason;
            public bool RequiresStrafePause;
            public int BaselineCharges;
            public int BaselineOwnedSentries;
            public int BaselineImportantApplied;
            public uint BaselineMfdActorAcd;
            public int BaselineMfdActorCreatedTick;
            public int BaselineMfdGameTick;
            public readonly HashSet<uint> BaselineActorAcds = new HashSet<uint>();
            public readonly HashSet<uint> VerifyTargetAcds = new HashSet<uint>();
            public readonly HashSet<uint> VerifyImportantAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotEligibleAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotDueAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotPlanningAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotCoveredEliteAcds = new HashSet<uint>();
            public readonly HashSet<uint> MultishotBaselineActiveAcds = new HashSet<uint>();
            public readonly HashSet<uint> SentryCoverageAcds = new HashSet<uint>();
            public float ExpectedWorldX;
            public float ExpectedWorldY;
            public float ExpectedWorldZ;
            public float MultishotDirectionX;
            public float MultishotDirectionY;
            public bool HasMultishotDirection;
            public bool MultishotDirectCore;
            public int MultishotMinimumBodyCoverage;
            public int LastAppliedCount;
            public int SentrySlot;
            public bool SentryRelocated;
            public float SentryRequiredMatchRadius;
            public float SentryActualWorldX;
            public float SentryActualWorldY;
            public bool SentryBurstChild;
            public bool ManualDebuff;
            public bool UseCurrentCursorAim;
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
        private int _sentryRelevanceDeficitSinceTick = int.MinValue;
        private bool _openingSentryBurstsClosedForEngagement;
        private bool _sentryPlacedThisEngagement;
        private int _sentryFairnessMultishotTurns;
        private bool _sentryFairnessDemandActive;
        private bool _sentryFullFieldHold;
        private bool _initialMfdSetupSatisfiedForEngagement;
        private bool _openingMultishotAttemptedForEngagement;
        private int _engagementStartedTick = int.MinValue;
        private bool _wasHighFrequencyMode;
        private int _combatModeEnteredTick = int.MinValue;
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
        private bool _interactionPauseActive;
        private int _lastSampleTick = int.MinValue;
        private bool _hasTrackedUptimeHero;
        private uint _trackedUptimeHeroId;
        private int _lastCastFinishedTick = int.MinValue;
        private int _lastManualDebuffCastFinishedTick = int.MinValue;
        private CastKind _lastSupportKind = CastKind.None;
        private bool _supportPrimaryGateBlocked;
        private int _lastEntangleMaintenanceTick = int.MinValue;
        private int _lastMultishotMaintenanceTick = int.MinValue;
        private int _lastMfdCastTick = int.MinValue;
        private int _lastUnverifiedMfdTick = int.MinValue;
        private int _mfdUnavailableSinceTick = int.MinValue;
        private int _hardMfdFailureStreak;
        private bool _mfdRetryDebt;
        private uint _mfdRetryDebtBaselineActorAcd;
        private int _mfdRetryDebtBaselineActorCreatedTick;
        private int _lastSentryCastTick = int.MinValue;
        private int _lastObservedSentryCharges = -1;
        private int _lastSentryChargeIncreaseTick = int.MinValue;
        private int _sentryRetryTick = int.MinValue;
        private int _sentryRetryDelayMs;
        private string _sentryRetryReason = string.Empty;
        private float _sentryRelocationSinkX;
        private float _sentryRelocationSinkY;
        private int _sentryRelocationSinkTick = int.MinValue;
        private int _sentryRelocationSinkCount;
        private int _sentryRelocationBackoffTick = int.MinValue;
        private float _sentryRelocationOriginX;
        private float _sentryRelocationOriginY;
        private float _sentryRelocationAnchorX;
        private float _sentryRelocationAnchorY;
        private bool _sentryRelocationContextValid;
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
        private string _mfdEdgeImprovementSignature = string.Empty;
        private int _mfdEdgeImprovementTick = int.MinValue;
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
        private bool _lastRestoreConfirmed;
        private int _cursorSafetyBlockedTick = int.MinValue;
        private int _cursorSafetyRestoreX;
        private int _cursorSafetyRestoreY;
        private float _localTravelX;
        private float _localTravelY;
        private int _localTravelSampleTick = int.MinValue;
        private float _localTravelSpeed;
        private int _electrifiedAbsentSinceTick = int.MinValue;
        private int _electrifiedAlertTick = int.MinValue;
        private bool _electrifiedPresenceActive;
        private string _electrifiedAlertText = string.Empty;
        private float _electrifiedLastSeenX;
        private float _electrifiedLastSeenY;
        private bool _electrifiedLastSeenValid;
        private readonly HashSet<uint> _electrifiedEncounterAcds = new HashSet<uint>();
        private bool _wasManualDebuffHold;
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
        private IFont _purpleFont;
        private IFont[] _electrifiedPopFonts;
        private IFont[] _electrifiedOutlinePopFonts;
        private IFont[] _electrifiedFadeFonts;
        private IFont[] _electrifiedOutlineFadeFonts;
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
            _purpleFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 205, 95, 255, true, false, 235, 0, 0, 0, true);
            _electrifiedPopFonts = CreateAlertPopFonts(70, 170, 255, ElectrifiedAlertTextSize);
            _electrifiedOutlinePopFonts = CreateAlertPopFonts(0, 0, 0, ElectrifiedAlertTextSize);
            _electrifiedFadeFonts = CreateAlertFadeFonts(70, 170, 255, ElectrifiedAlertTextSize);
            _electrifiedOutlineFadeFonts = CreateAlertFadeFonts(0, 0, 0, ElectrifiedAlertTextSize);
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
            _manualDebuffMovementUntilTick = int.MinValue;
            _targets.Clear();
            _playerPositions.Clear();
            ResetOwnedGroundEffectState("new area");
            _lastSampleTick = int.MinValue;
            _lastCastFinishedTick = int.MinValue;
            _lastManualDebuffCastFinishedTick = int.MinValue;
            _lastSupportKind = CastKind.None;
            _supportPrimaryGateBlocked = false;
            _lastEntangleMaintenanceTick = int.MinValue;
            _lastMultishotMaintenanceTick = int.MinValue;
            _lastMfdCastTick = int.MinValue;
            _lastUnverifiedMfdTick = int.MinValue;
            _mfdUnavailableSinceTick = int.MinValue;
            _hardMfdFailureStreak = 0;
            ClearMfdRetryDebt();
            _lastSentryCastTick = int.MinValue;
            _lastObservedSentryCharges = -1;
            _lastSentryChargeIncreaseTick = int.MinValue;
            ResetSentryBurstEngagement();
            ClearSentryRetry();
            ClearRejectedSentryPositions();
            _lastPauseReleasedTick = int.MinValue;
            ClearMfdImprovementCandidate();
            ClearMfdEdgeImprovementCandidate();
            _packCandidateTick = int.MinValue;
            _packCandidateValid = false;
            ClearTrashFightLatch("new area");
            _cursorSafetyBlocked = false;
            _cursorSafetyBlockedTick = int.MinValue;
            _cursorSafetyRestoreX = 0;
            _cursorSafetyRestoreY = 0;
            _localTravelSampleTick = int.MinValue;
            _localTravelSpeed = 0;
            if (newGame) ResetElectrifiedAlert();
            _wasManualDebuffHold = false;
            _channelingPylonActive = false;
            _speedPylonActive = false;
            _interactionPauseActive = false;
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
            _wasDead = Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.IsDead;
            if (newGame)
            {
                _hasTrackedUptimeHero = false;
                _trackedUptimeHeroId = 0;
                _lastRestoreConfirmed = false;
                s7o_ZDH_HelperMetrics.ResetUptime();
            }
        }

        private void ResetOwnedGroundEffectState(string reason)
        {
            _wasHighFrequencyMode = false;
            _combatModeEnteredTick = int.MinValue;
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
            _lastUnverifiedMfdTick = int.MinValue;
            _mfdUnavailableSinceTick = int.MinValue;
            _hardMfdFailureStreak = 0;
            ClearMfdRetryDebt();
            _lastSentryCastTick = int.MinValue;
            _lastObservedSentryCharges = -1;
            _lastSentryChargeIncreaseTick = int.MinValue;
            ResetSentryBurstEngagement();
            ResetSpeedCombatIntent(reason);
            ClearMfdImprovementCandidate();
            ClearMfdEdgeImprovementCandidate();
            ClearSentryRetry();
            ClearSentryRelocationState();
            ClearRejectedSentryPositions();
            foreach (TargetState state in _targets.Values)
            {
                state.SentryUncoveredSinceTick = int.MinValue;
                state.SentryCoveredSinceTick = int.MinValue;
                state.SentryCoverageLastActiveTick = int.MinValue;
                state.SentryCoverageAttempts = 0;
            }
            ClearTrashFightLatch(reason);
            ClearPendingMultishotValidations();
            _urgentRetryKind = CastKind.None;
            _urgentRetryTick = int.MinValue;

        }

        public void AfterCollect()
        {
            s7o_ZDH_HelperState.EnsureLoaded();
            int now = Environment.TickCount;
            _runtime.ProtectedSentryCoverageMissing = false;
            _runtime.EliteSentryCoverageMissing = false;
            _runtime.EliteSentryUncoveredCount = 0;
            _runtime.EliteSentryReadyCount = 0;
            _runtime.EliteSentryPriorityAcd = 0;
            _runtime.EliteSentryPriorityAgeMs = -1;
            _runtime.EliteSentryPriorityDelayMs = -1;
            _runtime.EliteSentryPriorityAttempts = 0;
            _runtime.EliteSentryUrgent = false;
            _runtime.PlayerSentryProtectionMissing = false;
            _runtime.BonusCircleSentryCoverageMissing = false;
            _runtime.UrgentBonusCircleSentryCoverageMissing = false;
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
                _interactionPauseActive = false;
                _wasHighFrequencyMode = false;
                _combatModeEnteredTick = int.MinValue;
                _lastUnverifiedMfdTick = int.MinValue;
                _mfdUnavailableSinceTick = int.MinValue;
                ClearTrashFightLatch("context");
                PublishTrashFightLatch(now, false, 0);
                ResetSpeedCombatIntent("context");
                ClearPendingMultishotValidations();
                ForceAbortSentryBurst("context", now);
                CancelCast("context");
                ReleaseBossEntangleStandstill();
                _wasManualDebuffHold = false;
                return;
            }

            bool ghosted = IsLocalGhosted();
            if (dead || ghosted)
            {
                _interactionPauseActive = false;
                _wasHighFrequencyMode = false;
                _combatModeEnteredTick = int.MinValue;
                _lastUnverifiedMfdTick = int.MinValue;
                _mfdUnavailableSinceTick = int.MinValue;
                _lastSampleTick = now;
                ClearTrashFightLatch(dead ? "dead" : "ghosted");
                PublishTrashFightLatch(now, false, 0);
                ResetSpeedCombatIntent(dead ? "dead" : "ghosted");
                ClearPendingMultishotValidations();
                ForceAbortSentryBurst(dead ? "dead" : "ghosted", now);
                CancelCast(dead ? "dead" : "ghosted");
                ReleaseBossEntangleStandstill();
                ReleaseDhStrafePause();
                ReleaseDhStrafePrimarySuppression();
                _wasManualDebuffHold = false;
                return;
            }

            UpdateBossEncounterState(now);
            bool strafeMacroRunning = s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh;
            bool highFrequencyMode = strafeMacroRunning && s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh;
            if (highFrequencyMode && !_wasHighFrequencyMode)
            {
                _combatModeEnteredTick = now;
                // Explicit F2 Combat entry starts a fresh opening decision from live effects.
                // Clear only scheduler debt that can be stale across Speed -> Combat; do not
                // disturb owned actors, target history, or the established field itself.
                _openingMultishotAttemptedForEngagement = false;
                _initialMfdSetupSatisfiedForEngagement = false;
                ClearPendingMultishotValidations();
                _urgentRetryKind = CastKind.None;
                _urgentRetryTick = int.MinValue;
                _lastUnverifiedMfdTick = int.MinValue;
                _mfdUnavailableSinceTick = int.MinValue;
                _hardMfdFailureStreak = 0;
                ClearMfdRetryDebt();
                ClearMfdImprovementCandidate();
            }
            else if (!highFrequencyMode)
                _combatModeEnteredTick = int.MinValue;
            _wasHighFrequencyMode = highFrequencyMode;
            ZdhLoadout local = BuildLoadout(Hud.Game.Me);
            UpdatePylonState();
            UpdateSentryChargeTelemetry(local == null || local.Sentry == null ? -1 : local.Sentry.Charges, now);
            if (local != null && local.Player != null)
                UpdateLocalTravelState(local.Player, now);
            UpdateElectrifiedAlert(local, now);

            // DHStrafe owns the portal approach/arrival distinction. Refresh it here because
            // Helper collects first, then consume the same state so both plugins agree this frame.
            // Pylons remain an immediate local interaction authority.
            s7o_DHStrafePrimaryPlugin.RefreshPortalInteractionForZdh(now);

            if (s7o_DHStrafePrimaryPlugin.IsPortalEscapeActiveForZdh)
            {
                _interactionPauseActive = false;
                ForceAbortSentryBurst("portal escape", now);
                CancelCast("portal escape");
                ReleaseBossEntangleStandstill();
                ReleaseDhStrafePause();
                ReleaseDhStrafePrimarySuppression();
                _manualDebuffMovementUntilTick = int.MinValue;
                _wasManualDebuffHold = false;
                return;
            }

            _interactionPauseActive = IsInteractionPauseNearby();
            if (_interactionPauseActive)
            {
                ForceAbortSentryBurst("interaction", now);
                CancelCast("interaction");
                ReleaseBossEntangleStandstill();
                ReleaseDhStrafePause();
                ReleaseDhStrafePrimarySuppression();
                _manualDebuffMovementUntilTick = int.MinValue;
                _wasManualDebuffHold = false;
                return;
            }

            bool manualDebuffHold = highFrequencyMode
                && s7o_DHStrafePrimaryPlugin.IsManualDebuffHoldActiveForZdh;
            if (_wasManualDebuffHold && !manualDebuffHold)
                HandleManualDebuffRelease(now);
            else if (manualDebuffHold)
                _manualDebuffMovementUntilTick = int.MinValue;
            _wasManualDebuffHold = manualDebuffHold;

            IMonster bossSpawnAnchor = FindBossSpawnAnchor(local == null ? null : local.Player);
            bool bossPreSpawn = CanUseBossPreSpawn(local, bossSpawnAnchor, now);
            bool bossStandalone = !strafeMacroRunning && CanUseBossStandalone(local, now);
            bool speedMode = strafeMacroRunning && !highFrequencyMode;
            bool sentryBurstContextActive = strafeMacroRunning || bossStandalone || bossPreSpawn;
            _bossStandaloneActive = bossStandalone;
            UpdateBossEntangleStandstill(local, bossStandalone);
            _runtime.HighFrequencyMode = highFrequencyMode;

            if (!strafeMacroRunning && !bossStandalone && !bossPreSpawn)
            {
                ForceAbortSentryBurst("automation inactive", now);
                _lastUnverifiedMfdTick = int.MinValue;
                _mfdUnavailableSinceTick = int.MinValue;
                ClearCursorSafetyBlock();
                ClearTrashFightLatch("automation inactive");
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

            if (!s7o_ZDH_HelperState.Enabled)
            {
                _wasHighFrequencyMode = false;
                _combatModeEnteredTick = int.MinValue;
            }

            // AdvanceCast above is allowed to finish/release any transaction already in flight.
            // Starting new support requires an active Strafe macro, except for the intentional
            // standalone RG paths. CTRL stationary support still qualifies because it pauses
            // Strafe without turning the DHStrafe macro itself off.
            if (!s7o_ZDH_HelperState.Enabled
                || (!strafeMacroRunning && !bossStandalone && !bossPreSpawn)
                || _cursorSafetyBlocked
                || _cast.Stage != CastStage.Idle
                || !AutomationContextValid())
                return;

            // CTRL release is the user's explicit movement pulse. Once any already-committed
            // support tail is finished, leave a short clean Strafe window before normal support
            // or Momentum maintenance can claim input again. Re-pressing CTRL cancels this grace.
            if (!manualDebuffHold && IsManualDebuffMovementRequested(now))
            {
                if (_sentryBurst.Mode != SentryBurstMode.None)
                    EndSentryBurst("manual movement", now);
                else
                {
                    ReleaseDhStrafePause();
                    ReleaseDhStrafePrimarySuppression();
                }
                return;
            }

            // A primary input already in flight is always atomic.
            if (s7o_DHStrafePrimaryPlugin.IsPrimaryTransactionPendingForZdh)
            {
                if (_sentryBurst.Mode != SentryBurstMode.None)
                    EndSentryBurst("momentum primary due", now);
                else
                {
                    ReleaseDhStrafePause();
                    ReleaseDhStrafePrimarySuppression();
                }
                return;
            }

            int momentumTarget = Math.Max(1, s7o_DHStrafePrimaryPlugin.MomentumTargetStacksForZdh);
            int momentumStacks = s7o_DHStrafePrimaryPlugin.MomentumStacksForZdh;
            bool combatMomentumLaneReserved = !manualDebuffHold && highFrequencyMode && !bossPreSpawn
                && s7o_DHStrafePrimaryPlugin.IsCombatMomentumLaneReservedForZdh;
            bool combatMomentumRecoveryPriority = combatMomentumLaneReserved
                && momentumStacks < momentumTarget;
            bool combatMomentumRefreshReserved = combatMomentumLaneReserved
                && momentumStacks >= momentumTarget;
            bool combatMomentumRecoveryInputDue = combatMomentumRecoveryPriority
                && s7o_DHStrafePrimaryPlugin.IsCombatPrimaryMaintenanceDueForZdh;
            bool combatMomentumRefreshInputDue = combatMomentumRefreshReserved
                && s7o_DHStrafePrimaryPlugin.IsCombatMomentumRefreshInputDueForZdh;
            bool speedMomentumBuildPriority = speedMode && !bossPreSpawn
                && s7o_DHStrafePrimaryPlugin.IsSpeedMomentumBuildActiveForZdh;
            // Speed recovery stays authoritative. Combat recovery is decided later, after the
            // explicit Multishot -> MFD -> three-Sentry opening has been planned from live state.
            if (speedMomentumBuildPriority)
            {
                if (_sentryBurst.Mode != SentryBurstMode.None)
                    EndSentryBurst("momentum primary due", now);
                else
                {
                    ReleaseDhStrafePause();
                    ReleaseDhStrafePrimarySuppression();
                }
                return;
            }

            // Explicit Combat mode is authoritative immediately. Waiting for Diablo's delayed
            // InCombat flag here made the helper orbit visible elites without opening support.
            if (local == null || local.Player == null
                || (!local.Player.InCombat && !highFrequencyMode && !bossStandalone && !bossPreSpawn))
            {
                if (local != null && local.Player != null && !local.Player.InCombat
                    && !highFrequencyMode && !bossPreSpawn)
                {
                    ForceAbortSentryBurst("combat ended", now);
                    ResetSentryBurstEngagement();
                    _lastUnverifiedMfdTick = int.MinValue;
                    _mfdUnavailableSinceTick = int.MinValue;
                    ClearTrashFightLatch("combat ended");
                    ClearPendingMultishotValidations();
                    ResetSpeedCombatIntent("combat ended");
                }
                PublishTrashFightLatch(now, false, 0);
                return;
            }

            UpdatePartyFocus(now);
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
                if (manualDebuffHold && local.Odyssey && local.Entangle != null)
                    TryStartManualDebuffEntangle(local, null, now);
                return;
            }

            float clusterDistance = ClusterDistance(local.Player, cluster);

            // Speed pass-through is now governed by the explicit 1.5s local-fight dwell below.
            // Do not discard a passive trash cluster here: the dwell detector needs to observe it
            // while travel continues, and it will refuse support until movement actually settles.

            List<IMonster> activePrimaryElites = GetActivePrimaryElites(local.Player, now);
            List<IMonster> groundSupportPrimaryElites = GetActiveGroundSupportPrimaryElites(local.Player, now);
            List<IMonster> activeMfdOnlyTargets = GetActiveGroundSupportMfdOnlyTargets(local.Player, now);
            List<IMonster> groundSupportElites = MergeMonsters(groundSupportPrimaryElites, activeMfdOnlyTargets);
            UpdateSentryRelocationContext(local.Player, cluster);

            // Speed observes passive trash without casting. Only after the 1.5s local-fight
            // dwell succeeds does IsCombatIntentTrash() become true and activate the normal
            // trash debuff/Sentry hierarchy. Combat mode is explicit immediately.
            bool speedCombatEvidence = speedMode && local.Player.InCombat
                && (groundSupportPrimaryElites.Count > 0 || activeMfdOnlyTargets.Count > 0
                    || IsPassiveTrashCandidate(cluster));
            bool speedLocalCombat = UpdateSpeedCombatIntent(local.Player, cluster, now, speedMode,
                speedCombatEvidence, clusterDistance);
            bool speedSentryActive = speedLocalCombat && _speedCombatLeavingTick == int.MinValue;
            bool speedSentryPassThrough = speedMode && !speedSentryActive;
            bool sentryBurstStartAllowed = highFrequencyMode || speedSentryActive || bossStandalone || bossPreSpawn;

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
            int missingIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsDebuffBody(m) && !HasIceblink(m)
                    && !HasPendingMultishotValidation(m.AcdId, now)) : 0;
            int freshMissingIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsDebuffBody(m) && !HasIceblink(m)
                    && !HasPendingMultishotValidation(m.AcdId, now)
                    && HasUnattemptedCurrentIceblinkLoss(m, now)) : 0;
            int actionableIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsIceblinkActionable(m, now)) : 0;
            int preemptIceblinkElites = s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                ? activePrimaryElites.Count(m => IsIceblinkPrimaryPreemptDue(m, now)) : 0;
            bool materialMfdUpgradeReady = false;
            bool majorMfdUpgradeReady = false;
            bool missingMfdCoverage = s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && HasMissingPrimaryMfdCoverage(local.Player, now, out materialMfdUpgradeReady,
                    out majorMfdUpgradeReady);
            if (_mfdRetryDebt && HasMfdRetryDebtRecoveryEvidence()) ClearMfdRetryDebt();
            bool bossEntangleDue = bossStandalone && s7o_ZDH_HelperState.AutoEntangle
                && local.Odyssey && local.Entangle != null
                && IsBossEntangleRefreshDue(cluster, now);
            if (speedSentryPassThrough && !bossPreSpawn)
            {
                ForceAbortSentryBurst("speed pass-through", now);
                ResetSentryBurstEngagement();
                ReleaseDhStrafePrimarySuppression();
                return;
            }
            bool sentryEngagementActive = bossPreSpawn || bossStandalone
                || groundSupportPrimaryElites.Count > 0
                || activeMfdOnlyTargets.Count > 0 || trashFightActive;
            UpdateSentryBurstEngagement(cluster, sentryEngagementActive, now);

            List<IActor> allOwnedSentries = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetOwnedSentries() : new List<IActor>();
            if (s7o_ZDH_HelperState.AutoSentry && local.Guardian)
                UpdateEliteSentryCoverageStates(groundSupportElites, allOwnedSentries, now);

            int sentryCapacity = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetDesiredSentryCount(local) : 0;
            int sentryEffectiveOwned = 0;
            int sentryDistinctRelevant = 0;
            bool sentryPlanValid = false;
            int sentryPlacementDeficit = s7o_ZDH_HelperState.AutoSentry && local.Guardian
                ? GetSentryPlacementDeficit(local, cluster, now, allOwnedSentries,
                    out sentryEffectiveOwned, out sentryDistinctRelevant, out sentryPlanValid) : 0;

            // Physical population, functional local coverage, and ideal spread are separate.
            // Geometry may describe fewer slots transiently; it must never redefine capacity.
            int sentryPlacementTarget = Math.Min(sentryCapacity, _runtime.SentryDesired);
            int sentryOwnedCount = Math.Min(sentryCapacity, allOwnedSentries.Count);
            int currentFightRelevantSentries = sentryPlanValid
                ? Math.Min(sentryPlacementTarget, sentryEffectiveOwned) : 0;
            int sentryCoreTarget = Math.Min(Math.Max(1, InitialSentryFieldCount), sentryCapacity);
            int sentryLocalCoreRelevant = sentryPlanValid
                ? Math.Min(sentryCoreTarget, currentFightRelevantSentries) : 0;
            int sentryLocalCoreDeficit = sentryPlanValid
                ? Math.Max(0, sentryCoreTarget - sentryLocalCoreRelevant) : 0;
            int sentryDistinctCoreRelevant = sentryPlanValid
                ? Math.Min(sentryCoreTarget, sentryDistinctRelevant) : 0;
            int sentryDistinctCoreDeficit = sentryPlanValid
                ? Math.Max(0, sentryCoreTarget - sentryDistinctCoreRelevant) : 0;
            int sentryHardDeficit = Math.Max(0, sentryCapacity - sentryOwnedCount);
            bool sentryPopulationFull = sentryCapacity > 0 && sentryHardDeficit == 0;
            bool sentryHardRefillPending = sentryEngagementActive && sentryHardDeficit > 0;

            int maxCoreBurstAttempts = Math.Max(1, SentryCoreBurstMaxAttemptsPerEngagement);
            bool openingCoreAttemptBudgetExhausted = _coreBurstAttemptsThisEngagement >= maxCoreBurstAttempts;
            bool localOpeningCoreEstablished = sentryPlanValid && sentryCoreTarget > 0
                && sentryLocalCoreRelevant >= sentryCoreTarget;
            bool carriedFieldFullyRelevant = sentryPlanValid && sentryPlacementTarget >= sentryCapacity
                && currentFightRelevantSentries >= sentryCapacity;
            if (!_openingSentryBurstsClosedForEngagement
                && sentryEngagementActive && sentryPopulationFull
                && ((localOpeningCoreEstablished && (_sentryPlacedThisEngagement || carriedFieldFullyRelevant))
                    || openingCoreAttemptBudgetExhausted))
            {
                _openingSentryBurstsClosedForEngagement = true;
            }

            _sentryFullFieldHold = _openingSentryBurstsClosedForEngagement
                && sentryPopulationFull && sentryPlacementDeficit <= 0;

            bool sentryCorePending = !_openingSentryBurstsClosedForEngagement
                && sentryEngagementActive && sentryPlanValid && sentryCoreTarget > 0
                && sentryLocalCoreDeficit > 0 && sentryPlacementDeficit > 0
                && !_coreBurstAttemptedForEngagement
                && !openingCoreAttemptBudgetExhausted
                && CoreBurstRetryReady(now);

            bool mfdOnlyCorePriority = activeMfdOnlyTargets.Count > 0 && sentryCorePending;
            bool effectiveTrashIceblinkQueueDue = trashIceblinkQueueDue && !mfdOnlyCorePriority;
            bool currentFightSentryFillPending = sentryEngagementActive && sentryCapacity > 0
                && (sentryHardRefillPending
                    || (sentryPlacementDeficit > 0 && !_sentryFullFieldHold));

            bool sentryRelevanceOnlyDeficit = sentryEngagementActive && sentryPopulationFull
                && sentryPlanValid && currentFightRelevantSentries < sentryPlacementTarget;
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
            int sentryRelevanceStabilityMs = _openingSentryBurstsClosedForEngagement
                ? Math.Max(SentryRelevanceDeficitStabilityMs, SentryFullFieldRelevanceStabilityMs)
                : SentryRelevanceDeficitStabilityMs;

            bool freshEliteSentryNeed = HasFreshReadyEliteSentryNeed(groundSupportElites, now);
            bool protectedSentryCoverageMissing = sentryPlacementDeficit > 0
                && _runtime.ProtectedSentryCoverageMissing;
            bool playerSentryProtectionMissing = sentryPlacementDeficit > 0
                && _runtime.PlayerSentryProtectionMissing;
            bool bossProtectedSentryNeed = protectedSentryCoverageMissing
                && cluster.Elites.Any(m => m != null && m.Rarity == ActorRarity.Boss);
            bool sentryRelevanceDeficitStable = sentryHardRefillPending || sentryCorePending
                || !sentryRelevanceOnlyDeficit
                || freshEliteSentryNeed
                || _runtime.EliteSentryCoverageMissing
                || bossProtectedSentryNeed
                || _runtime.UrgentBonusCircleSentryCoverageMissing
                || sentryRelevanceDeficitAgeMs >= Math.Max(0, sentryRelevanceStabilityMs);
            bool postFullFieldSentryReady = sentryHardRefillPending
                || !_openingSentryBurstsClosedForEngagement
                || freshEliteSentryNeed
                || _runtime.EliteSentryCoverageMissing
                || bossProtectedSentryNeed
                || playerSentryProtectionMissing
                || _runtime.BonusCircleSentryCoverageMissing
                || _lastSentryCastTick == int.MinValue
                || Elapsed(_lastSentryCastTick, now) >= Math.Max(0, SentryFullFieldRecastMs);

            int oldestSentryAgeMs = GetOldestOwnedSentryAgeMs(allOwnedSentries);
            int sentryLifetimeMs = GetExpectedSentryLifetimeMs(local);
            int rollingEarlyAgeMs = Math.Max(0, sentryLifetimeMs - Math.Max(0, SentryRollingRefreshLeadMs));
            int rollingEmergencyAgeMs = Math.Max(rollingEarlyAgeMs,
                sentryLifetimeMs - Math.Max(0, SentryRollingRefreshEmergencyLeadMs));
            int sentryCharges = local.Sentry == null ? 0 : Math.Max(0, local.Sentry.Charges);
            bool rollingFieldHealthy = _openingSentryBurstsClosedForEngagement
                && sentryEngagementActive && sentryPopulationFull && sentryPlanValid
                && sentryLocalCoreRelevant >= sentryCoreTarget;
            bool sentryRollingRefreshPending = rollingFieldHealthy && oldestSentryAgeMs >= 0
                && (oldestSentryAgeMs >= rollingEmergencyAgeMs
                    || (oldestSentryAgeMs >= rollingEarlyAgeMs && sentryCharges >= 2));
            bool sentryRollingRefreshReady = sentryRollingRefreshPending
                && SentryAvailable(local.Sentry)
                && (oldestSentryAgeMs >= rollingEmergencyAgeMs ? sentryCharges >= 1 : sentryCharges >= 2)
                && (_lastSentryCastTick == int.MinValue
                    || Elapsed(_lastSentryCastTick, now) >= Math.Max(SentryRecastMs, SentryFullFieldRecastMs));

            bool populationFillContextReady = sentryPlanValid || bossPreSpawn || bossStandalone
                || cluster.TrashLatched || cluster.Stable;
            bool sentryPlanReady = sentryCapacity > 0
                && (sentryPlanValid || (sentryHardRefillPending && populationFillContextReady));
            bool sentrySetupDemand = sentryHardRefillPending
                || sentryPlacementDeficit > 0 || sentryRollingRefreshPending;
            bool sentrySetupActive = sentryPlanReady && sentrySetupDemand
                && (!speedMode || speedLocalCombat)
                && sentryEngagementActive && sentryRelevanceDeficitStable
                && (!_sentryFullFieldHold || protectedSentryCoverageMissing || sentryRollingRefreshPending)
                && SentryAvailable(local.Sentry);

            if (!sentrySetupDemand) ClearSentryRetry();
            bool sentryRetryPending = IsSentryRetryPending();
            int sentryRetryAgeMs = sentryRetryPending ? Elapsed(_sentryRetryTick, now) : 0;
            bool sentryRetryReady = !sentryRetryPending
                || sentryRetryAgeMs >= Math.Max(0, _sentryRetryDelayMs);

            _runtime.SentryPlacementDeficit = sentryPlacementDeficit;
            _runtime.SentryCapacity = sentryCapacity;
            _runtime.SentryOwned = sentryOwnedCount;
            _runtime.SentryRelevant = currentFightRelevantSentries;
            _runtime.SentryLocalCoreRelevant = sentryLocalCoreRelevant;
            _runtime.SentryDistinctCoreRelevant = sentryDistinctCoreRelevant;
            _runtime.SentryDistinctCoreDeficit = sentryDistinctCoreDeficit;
            _runtime.SentryHardDeficit = sentryHardDeficit;
            _runtime.SentryOldestAgeMs = oldestSentryAgeMs;
            _runtime.SentryCharges = sentryCharges;
            _runtime.SentryPlanValid = sentryPlanValid;
            _runtime.OpeningSentryBurstsClosed = _openingSentryBurstsClosedForEngagement;
            _runtime.CoreBurstAttempts = _coreBurstAttemptsThisEngagement;
            _runtime.CoreBurstAttemptLimit = maxCoreBurstAttempts;
            _runtime.SentryRelocationBackoff = !SentryRelocationBackoffReady(now);
            _runtime.SentryRelocationSinkCount = _sentryRelocationSinkCount;
            _runtime.SentryRollingRefreshDue = sentryRollingRefreshPending;
            _runtime.SentryRollingRefreshReady = sentryRollingRefreshReady;

            bool blockedMfdYieldReady = sentryCorePending
                && _mfdUnavailableSinceTick != int.MinValue
                && Elapsed(_mfdUnavailableSinceTick, now) >= Math.Max(500, MfdSentryBlockedYieldMs);
            bool trashMfdCoverageMissing = s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && trashFightActive && groundSupportPrimaryElites.Count == 0 && activeMfdOnlyTargets.Count == 0
                && !HasCurrentTrashMfdCoverage(cluster, now);
            bool bossPreSpawnMfdReady = !bossPreSpawn
                || !s7o_ZDH_HelperState.AutoMarkedForDeath || !local.Valley
                || HasAuthoritativeValleyAtPoint(cluster.CenterX, cluster.CenterY, now);
            bool hardIceblinkWork = missingIceblinkElites > 0 && actionableIceblinkElites > 0;
            bool sentryBaseReady = (currentFightSentryFillPending || sentryRollingRefreshPending)
                && (!speedMode || speedLocalCombat)
                && sentryRelevanceDeficitStable
                && postFullFieldSentryReady
                && trashInitialMultishotReady
                && sentryRetryReady
                && sentryEngagementActive
                && s7o_ZDH_HelperState.AutoSentry
                && local.Guardian
                && local.Sentry != null
                && SentryAvailable(local.Sentry);

            bool mfdSetupRequired = sentryEngagementActive
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && local.MarkedForDeath != null
                && (groundSupportPrimaryElites.Count > 0 || activeMfdOnlyTargets.Count > 0 || trashFightActive);
            bool currentOpeningMfdCoverageReady = !mfdSetupRequired
                || HasCurrentInitialMfdSetupCoverage(cluster, trashFightActive, now);
            // Opening completion is historical state; current coverage is authoritative live state.
            // Keep the historical latch so a transient geometry change cannot tear down an active
            // three-Sentry core, but never let it hide an expired or materially under-covered Valley.
            if (currentOpeningMfdCoverageReady)
                _initialMfdSetupSatisfiedForEngagement = true;
            bool openingMfdCoverageReady = !mfdSetupRequired
                || _initialMfdSetupSatisfiedForEngagement;
            bool openingMfdMissing = mfdSetupRequired && !openingMfdCoverageReady;
            // missingMfdCoverage is recomputed from current actors/monster flags and the current
            // best placement every frame. Keep stable better-placement work on its existing
            // material-upgrade lane; this recovery lane is for expired/materially deficient live
            // coverage and cannot be suppressed by the historical opening-complete latch.
            bool liveMfdRecoveryRequired = mfdSetupRequired && missingMfdCoverage
                && !materialMfdUpgradeReady;
            // One bounded opening pipeline: one Multishot input -> real MFD effect -> core Sentries.
            // F2/new-pack entry intentionally sends one Multishot even if an incidental Strafe proc
            // has already applied Iceblink; normal maintenance resumes after that single input.
            int engagementPriorityAgeMs = _engagementStartedTick == int.MinValue
                ? int.MaxValue : Elapsed(_engagementStartedTick, now);
            int combatModePriorityAgeMs = _combatModeEnteredTick == int.MinValue
                ? int.MaxValue : Elapsed(_combatModeEnteredTick, now);
            int openingPriorityAgeMs = Math.Min(engagementPriorityAgeMs, combatModePriorityAgeMs);
            bool openingMultishotDeadlineExpired = highFrequencyMode
                && openingPriorityAgeMs > Math.Max(500, CombatOpeningPriorityMaxMs);
            if (openingMultishotDeadlineExpired && !_openingMultishotAttemptedForEngagement)
            {
                _openingMultishotAttemptedForEngagement = true;
            }
            bool openingMultishotRequired = highFrequencyMode
                && activePrimaryElites.Any(IsDebuffBody)
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && local.Multishot != null && !openingMultishotDeadlineExpired;
            bool openingMultishotReadyForCore = !openingMultishotRequired
                || _openingMultishotAttemptedForEngagement;
            bool openingCoreSetupPriority = sentryCorePending && sentryBaseReady
                && openingMultishotReadyForCore && openingMfdCoverageReady
                && currentOpeningMfdCoverageReady;
            // Historical opening completion must never suppress live MFD recovery. Complete loss
            // or materially insufficient current coverage re-enters the urgent MFD lane immediately;
            // stable better-placement work remains handled by materialMfdUpgradeReady below.
            bool urgentMfdBeforeSentry = openingMfdMissing || liveMfdRecoveryRequired;
            bool strictMfdSentryGate = urgentMfdBeforeSentry;
            bool mfdReadyForSentryFill = bossPreSpawn
                ? bossPreSpawnMfdReady
                : !strictMfdSentryGate;
            if (!strictMfdSentryGate)
            {
                _hardMfdFailureStreak = 0;
                _mfdUnavailableSinceTick = int.MinValue;
            }
            bool sentryFillReady = sentryBaseReady && mfdReadyForSentryFill;
            // Explicit Multishot sweep queues are disabled. The cone planner may still include
            // multiple due/near-due elites in one shot, but uncovered directions return to the
            // normal scheduler so Strafe/Primary receives a movement opportunity between casts.
            bool repeatedMfdFailureYield = _hardMfdFailureStreak >= Math.Max(2, MfdSentryFailureYieldAttempts)
                && _lastUnverifiedMfdTick != int.MinValue
                && _lastSupportKind == CastKind.MarkedForDeath
                && Elapsed(_lastUnverifiedMfdTick, now)
                    <= Math.Max(750, MarkedForDeathUrgentRecastMs + 250);
            bool unavailableMfdYield = blockedMfdYieldReady && _lastSupportKind != CastKind.Sentry;
            bool mfdRetryYieldToSentry = sentryBaseReady
                && strictMfdSentryGate
                && (repeatedMfdFailureYield || unavailableMfdYield);
            bool combatTrashSetupChain = (highFrequencyMode || speedLocalCombat)
                && trashFightActive && trashInitialMultishotReady
                && currentFightSentryFillPending && trashMfdCoverageMissing;
            bool hasMultishotFillTargets = sentryFillReady
                && HasMultishotFillTargets(local, cluster, now);
            int fillInterleaveMultishotAge = _lastMultishotMaintenanceTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastMultishotMaintenanceTick, now);
            // Sentry placement must never manufacture a Multishot requirement. Interleave only
            // when the normal efficient ~1.8s refresh window is genuinely open.
            bool fillInterleaveMultishotTurn = sentryFillReady
                && _lastSupportKind == CastKind.Sentry
                && hasMultishotFillTargets
                && fillInterleaveMultishotAge >= Math.Max(0,
                    iceblinkRefreshAgeMs - Math.Max(0, EfficientMultishotLeadMs));
            bool eliteCoveragePending = sentryEngagementActive && sentryPlanValid
                && _runtime.EliteSentryCoverageMissing;
            bool urgentEliteCoveragePending = eliteCoveragePending
                && HasUrgentReadyEliteSentryNeed(groundSupportElites, now);
            bool urgentBonusCoveragePending = sentryEngagementActive && sentryPlanValid
                && _runtime.UrgentBonusCircleSentryCoverageMissing;
            bool eligibleBonusCoveragePending = sentryEngagementActive && sentryPlanValid
                && _runtime.BonusCircleSentryCoverageMissing;
            bool sentryFairnessDemand = sentryHardRefillPending
                || eliteCoveragePending
                || urgentBonusCoveragePending || eligibleBonusCoveragePending
                || sentryRollingRefreshPending;
            if (!sentryFairnessDemand) _sentryFairnessMultishotTurns = 0;
            _sentryFairnessDemandActive = sentryFairnessDemand;

            int sentryFairnessBudget = (urgentBonusCoveragePending || urgentEliteCoveragePending) ? 1
                : sentryFairnessDemand ? 2 : 0;
            bool sentryFairnessDue = sentryFairnessBudget > 0
                && _sentryFairnessMultishotTurns >= sentryFairnessBudget;

            bool hardSentryRecoveryReady = sentryHardRefillPending
                && populationFillContextReady
                && sentryEngagementActive && (!speedMode || speedLocalCombat)
                && sentryRetryReady
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian
                && local.Sentry != null && SentryAvailable(local.Sentry);
            bool bonusSentryFairnessReady = (urgentBonusCoveragePending || eligibleBonusCoveragePending)
                && sentryEngagementActive && (!speedMode || speedLocalCombat)
                && sentryRelevanceDeficitStable && postFullFieldSentryReady && sentryRetryReady
                && local.Sentry != null && SentryAvailable(local.Sentry);
            bool rollingSentryFairnessReady = sentryRollingRefreshReady && sentryRetryReady;
            bool protectedSentryWorkReady = protectedSentryCoverageMissing
                && !speedSentryPassThrough
                && sentryRelevanceDeficitStable && postFullFieldSentryReady && sentryRetryReady
                && SentryAvailable(local.Sentry);
            bool eliteSentryFairnessReady = eliteCoveragePending
                && protectedSentryWorkReady && SentryRelocationBackoffReady(now);
            bool sentryFairnessReady = hardSentryRecoveryReady || eliteSentryFairnessReady
                || bonusSentryFairnessReady || rollingSentryFairnessReady;

            _runtime.SentryFairnessDemand = sentryFairnessDemand;
            _runtime.SentryFairnessTurns = _sentryFairnessMultishotTurns;
            _runtime.SentryFairnessBudget = sentryFairnessBudget;
            _runtime.SentryFairnessDue = sentryFairnessDue;

            // Fresh Iceblink keeps first claim. During an unresolved Sentry contention episode,
            // repeated Multishot turns are bounded so Waller/density misses cannot monopolize support.
            bool yieldIceblinkRetryToSentry = sentryFairnessDue && sentryFairnessReady
                && missingIceblinkElites > 0 && !bossEntangleDue;
            bool yieldProactiveIceblinkToSentry = sentrySetupActive && sentryRetryReady
                && missingIceblinkElites == 0 && actionableIceblinkElites > 0
                && (sentryFairnessDue || _lastSupportKind == CastKind.Multishot);
            bool iceblinkAllowsSentryFill = missingIceblinkElites == 0 || yieldIceblinkRetryToSentry;
            int sentryRefreshLeadMs = Math.Max(0, IceblinkPrimaryPreemptLeadMs);
            bool eliteSentryRefreshPreempt = sentryFillReady && preemptIceblinkElites > 0;
            bool trashSentryRefreshPreempt = trashFightActive && sentryFillReady
                && _trashInitialMultishotDone
                && trashMultishotMaintenanceAge >= Math.Max(0, iceblinkRefreshAgeMs - sentryRefreshLeadMs);
            bool sentryIceblinkPreempt = (eliteSentryRefreshPreempt || trashSentryRefreshPreempt)
                && !yieldProactiveIceblinkToSentry;
            bool fillSentryTurn = sentryFillReady
                && !fillInterleaveMultishotTurn
                && !hardIceblinkWork
                && iceblinkAllowsSentryFill
                && !effectiveTrashIceblinkQueueDue
                && !bossEntangleDue
                && !sentryIceblinkPreempt;
            bool sentryTimingWorkActive = sentryFillReady || mfdRetryYieldToSentry || protectedSentryWorkReady;
            bool initialDebuffBurst = openingMfdMissing
                && _openingMultishotAttemptedForEngagement;
            bool openingMultishotPending = openingMultishotRequired
                && !_openingMultishotAttemptedForEngagement;
            bool openingMfdPending = sentryCorePending && openingMultishotReadyForCore
                && (openingMfdMissing || liveMfdRecoveryRequired);
            bool openingSentryPending = openingCoreSetupPriority;
            bool activeOpeningCoreBurst = _sentryBurst.Mode == SentryBurstMode.Core;
            bool openingPipelinePending = openingMultishotPending
                || openingMfdPending || openingSentryPending || activeOpeningCoreBurst;
            bool combatOpeningPriorityActive = highFrequencyMode
                && openingPipelinePending
                && (activeOpeningCoreBurst
                    || openingPriorityAgeMs <= Math.Max(500, CombatOpeningPriorityMaxMs));
            bool combatMomentumRecoveryDeferredForOpening = combatMomentumRecoveryPriority
                && combatOpeningPriorityActive;
            bool combatMomentumRefreshDeferredForOpening = combatMomentumRefreshReserved
                && combatOpeningPriorityActive;
            // A reserved Momentum lane must not starve a genuinely missing support debuff across
            // its own bounded retry cooldown. Primary still owns every open input window; while
            // that window is closed, permit only one urgent MFD/Iceblink transaction before the
            // scheduler yields back to Momentum. Below-cap recovery therefore remains authoritative
            // whenever its next input can actually be sent.
            bool urgentDebuffRecoveryWork = liveMfdRecoveryRequired
                || (missingIceblinkElites > 0 && actionableIceblinkElites > 0);
            bool combatMomentumRetryGapUrgentDebuff = !combatOpeningPriorityActive
                && urgentDebuffRecoveryWork
                && ((combatMomentumRecoveryPriority && !combatMomentumRecoveryInputDue)
                    || (combatMomentumRefreshReserved && !combatMomentumRefreshInputDue));

            // Combat Momentum is level-triggered, but one bounded opening or one urgent-debuff
            // transaction inside a closed retry window may defer it. Never use that retry gap for
            // Sentry/maintenance work; a later guard returns directly to Primary.
            if ((combatMomentumRecoveryPriority || combatMomentumRefreshReserved)
                && !combatOpeningPriorityActive && !combatMomentumRetryGapUrgentDebuff)
            {
                if (_sentryBurst.Mode != SentryBurstMode.None)
                    EndSentryBurst("momentum primary due", now);
                else
                {
                    ReleaseDhStrafePause();
                    ReleaseDhStrafePrimarySuppression();
                }
                return;
            }
            bool sentryBurstHardMfdReady = bossPreSpawn
                ? bossPreSpawnMfdReady
                : !strictMfdSentryGate;
            bool sentryBurstMfdStartReady = mfdReadyForSentryFill;
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
                (!eliteSentryRefreshPreempt || yieldProactiveIceblinkToSentry
                    || sentryBurstEliteIceblinkRemainingMs >= sentryBurstIceblinkChildBudgetMs)
                && (!trashSentryRefreshPreempt || yieldProactiveIceblinkToSentry
                    || sentryBurstTrashIceblinkRemainingMs >= sentryBurstIceblinkChildBudgetMs);
            int sentryBurstStartRunwayBudgetMs = Math.Max(sentryBurstIceblinkChildBudgetMs,
                Math.Max(0, SentryBurstAcquireMaxMs)
                + Math.Max(0, SentryBurstMovementSettleMaxMs)
                + sentryBurstIceblinkChildBudgetMs);
            bool sentryBurstStartIceblinkRunwayReady = openingCoreSetupPriority
                || sentryBurstIceblinkRemainingMs == int.MaxValue
                || sentryBurstIceblinkRemainingMs >= sentryBurstStartRunwayBudgetMs;

            bool sentryBurstContinuationDebuffsClear = activeOpeningCoreBurst
                || openingCoreSetupPriority
                || (!materialMfdUpgradeReady
                    && sentryBurstHardMfdReady
                    && trashInitialMultishotReady
                    && !hardIceblinkWork
                    && !bossEntangleDue
                    && sentryBurstSoftIceblinkAllowed);
            bool sentryBurstStartDebuffsClear = openingCoreSetupPriority
                || (!materialMfdUpgradeReady
                    && sentryBurstHardMfdReady
                    && trashInitialMultishotReady
                    && !hardIceblinkWork
                    && !effectiveTrashIceblinkQueueDue
                    && !bossEntangleDue
                    && !sentryIceblinkPreempt
                    && sentryBurstMfdStartReady
                    && sentryBurstStartIceblinkRunwayReady
                    && !urgentMfdBeforeSentry);

            if (bossPreSpawn && !bossPreSpawnMfdReady)
            {
                if (TryStartBossPreSpawnMarkedForDeath(local, bossSpawnAnchor, now))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
                return;
            }

            // With Strafe intentionally off at the RG, a missing Iceblink is authoritative.
            // The boss is already a settled support target and Multishot itself is a standstill
            // transaction, so neither player movement nor Sentry maintenance should be a
            // prerequisite. End only an idle burst shell; never cancel an active child input.
            bool bossStandaloneIceblinkPriority = bossStandalone
                && missingIceblinkElites > 0
                && s7o_ZDH_HelperState.AutoMultishot
                && local.Iceblink && local.WindChill && local.Multishot != null;
            if (bossStandaloneIceblinkPriority)
            {
                if (_sentryBurst.Mode != SentryBurstMode.None)
                    EndSentryBurst("boss iceblink priority", now);

                // The RG keeps the same bounded Multishot/Sentry fairness contract as elite combat.
                // A newly missing Iceblink still gets the first shot; repeated retries cannot hold a
                // physically/core-deficient Guardian field below capacity indefinitely.
                bool bossYieldToSentry = sentryFairnessDue && sentryFairnessReady
                    && !combatMomentumRetryGapUrgentDebuff;
                if (bossYieldToSentry
                    && TryStartFairnessSentry(local, cluster, now,
                        sentryHardRefillPending, eliteCoveragePending,
                        urgentBonusCoveragePending, eligibleBonusCoveragePending,
                        sentryRollingRefreshPending))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

                int bossIceblinkGap = Math.Max(100, BossUrgentRetryGapMs);
                if (_lastCastFinishedTick == int.MinValue
                    || Elapsed(_lastCastFinishedTick, now) >= bossIceblinkGap)
                {
                    if (TryStartMultishot(local, cluster, now, true))
                        return;
                    if (_supportPrimaryGateBlocked) return;
                }
            }

            // Strict first child: one preemptive Multishot before MFD/core. Suppress Primary for
            // the scheduler frame, but leave Strafe running until the cast itself acquires its
            // normal short transaction. A failed readiness check therefore cannot freeze movement.
            if (openingMultishotPending)
            {
                SuppressDhStrafePrimary(150);
                if (_sentryBurst.Mode != SentryBurstMode.None)
                {
                    EndSentryBurst("combat opening debuff", now);
                    if (_sentryBurst.Mode != SentryBurstMode.None) return;
                }
                if (_lastCastFinishedTick != int.MinValue
                    && Elapsed(_lastCastFinishedTick, now) < Math.Max(100, InitialSetupBurstGapMs))
                {
                    if (manualDebuffHold) TryStartManualDebuffEntangle(local, cluster, now);
                    return;
                }
                if (TryStartMultishot(local, cluster, now, false,
                    false, false, false, true))
                    return;
                return;
            }

            // Never tear down an already-running initial core because live MFD changed after
            // the handoff. Finish the atomic core, then the independent recovery lane gets MFD.
            bool openingDebuffPending = openingMfdPending && !activeOpeningCoreBurst;
            if (_sentryBurst.Mode != SentryBurstMode.None
                && combatOpeningPriorityActive && openingDebuffPending)
                EndSentryBurst("combat opening debuff", now);

            if (_sentryBurst.Mode != SentryBurstMode.None)
            {
                AdvanceSentryBurst(local, cluster, now, sentryLocalCoreRelevant, sentryOwnedCount,
                    sentryCapacity, sentryLocalCoreDeficit, sentryHardDeficit, sentryRetryReady,
                    sentryBurstContinuationDebuffsClear, _channelingPylonActive);
                return;
            }

            // Initial MFD setup and MFD field quality are separate concerns. Once a real Valley
            // exists, a stable placement that covers additional elites remains actionable instead
            // of being suppressed merely because opening setup is already satisfied. A newly lost
            // Iceblink still gets first recovery attempt, but a major stable Valley upgrade may then
            // preempt repeated Iceblink retries so completion/+2/focus gains cannot starve indefinitely.
            bool materialMfdUpgradeTurn = materialMfdUpgradeReady
                && !openingCoreSetupPriority
                && !activeOpeningCoreBurst
                && (!hardIceblinkWork || (majorMfdUpgradeReady && freshMissingIceblinkElites == 0));
            if (materialMfdUpgradeTurn
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && local.MarkedForDeath != null)
            {
                int materialMfdCastGap = bossStandalone ? BossUrgentRetryGapMs
                    : highFrequencyMode ? UrgentRetryGapMs : MovementUrgentRetryGapMs;
                if (_lastCastFinishedTick != int.MinValue
                    && Elapsed(_lastCastFinishedTick, now) < Math.Max(100, materialMfdCastGap))
                {
                    if (manualDebuffHold) TryStartManualDebuffEntangle(local, cluster, now);
                    return;
                }
                if (TryStartMarkedForDeath(local, cluster, now, true, true)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            if (!_openingSentryBurstsClosedForEngagement
                && TryBeginCoreSentryBurst(local, cluster, now,
                    sentryBurstStartAllowed && sentryCorePending && postFullFieldSentryReady,
                    sentryEngagementActive, sentryRetryReady, sentryBurstStartDebuffsClear,
                    sentryRelevanceDeficitStable,
                    sentryLocalCoreRelevant, sentryCoreTarget, sentryCapacity, _channelingPylonActive))
                return;

            if (!_openingSentryBurstsClosedForEngagement && sentryHardDeficit > 0
                && TryBeginCompletionSentryBurst(local, cluster, now,
                    sentryBurstStartAllowed && postFullFieldSentryReady,
                    sentryEngagementActive, sentryRetryReady, sentryBurstStartDebuffsClear,
                    sentryRelevanceDeficitStable,
                    sentryOwnedCount, sentryLocalCoreRelevant, sentryCoreTarget, sentryCapacity,
                    sentryHardDeficit, _channelingPylonActive))
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

            bool urgentRetryActive = _urgentRetryKind != CastKind.None
                && Elapsed(_urgentRetryTick, now) <= Math.Max(500, UrgentRetryLifetimeMs);
            if ((_urgentRetryKind == CastKind.Multishot && actionableIceblinkElites == 0)
                || !urgentRetryActive)
            {
                _urgentRetryKind = CastKind.None;
                _urgentRetryTick = int.MinValue;
                urgentRetryActive = false;
            }

            bool combatCadenceEntangleAvailable = highFrequencyMode && !manualDebuffHold
                && s7o_ZDH_HelperState.AutoEntangle && local.Odyssey
                && local.Entangle != null && SkillReady(local.Entangle);
            int movementWindow = manualDebuffHold ? 0
                : bossStandalone ? BossMovementWindowMs
                    : highFrequencyMode ? AttackMovementWindowMs : MovementModeMovementWindowMs;
            if (!manualDebuffHold && (combatOpeningPriorityActive || initialDebuffBurst || sentryFillReady || mfdRetryYieldToSentry
                || combatTrashSetupChain))
            {
                movementWindow = 0;
            }
            else if (!manualDebuffHold && missingIceblinkElites > 0)
            {
                movementWindow = highFrequencyMode ? 0
                    : bossStandalone ? BossIceblinkMovementWindowMs : MovementIceblinkMovementWindowMs;
            }
            else if (!manualDebuffHold && missingMfdCoverage)
            {
                // A genuinely missing/insufficient live Valley in Combat is debuff recovery, not
                // ordinary placement optimization. Do not add the normal 600 ms movement window
                // after another support cast before restoring it. Stable better-position upgrades
                // continue to use their existing hysteresis and movement timing.
                movementWindow = highFrequencyMode && liveMfdRecoveryRequired ? 0
                    : bossStandalone ? BossMfdMovementWindowMs
                        : highFrequencyMode ? AttackMfdMovementWindowMs : MovementMfdMovementWindowMs;
            }
            else if (!manualDebuffHold && bossEntangleDue)
            {
                movementWindow = BossMovementWindowMs;
            }
            else if (!manualDebuffHold && (actionableIceblinkElites > 0 || trashIceblinkQueueDue || sentryIceblinkPreempt))
            {
                movementWindow = highFrequencyMode && actionableIceblinkElites > 0 ? 0
                    : bossStandalone ? BossIceblinkMovementWindowMs
                        : highFrequencyMode ? AttackIceblinkMovementWindowMs : MovementIceblinkMovementWindowMs;
            }
            else if (!manualDebuffHold && _runtime.UrgentBonusCircleSentryCoverageMissing)
            {
                movementWindow = 0;
            }
            else if (!manualDebuffHold && sentryTimingWorkActive && protectedSentryCoverageMissing)
            {
                movementWindow = bossStandalone ? BossMovementWindowMs
                    : highFrequencyMode ? AttackSentryMovementWindowMs : MovementSentryMovementWindowMs;
            }

            if (combatCadenceEntangleAvailable && movementWindow > 0)
                movementWindow = Math.Min(movementWindow, Math.Max(250, CombatCadenceMovementWindowMs));

            // In Combat, a direct primary pulse and a completed Helper transaction consume the
            // same movement slot. Urgent branches above may still shorten the window explicitly.
            int movementElapsed = highFrequencyMode
                ? s7o_DHStrafePrimaryPlugin.CombatActionQuietAgeForZdh(now)
                : _lastPauseReleasedTick == int.MinValue
                    ? int.MaxValue : Elapsed(_lastPauseReleasedTick, now);
            int movementRemaining = movementElapsed == int.MaxValue ? 0 : Math.Max(0, movementWindow - movementElapsed);
            if (movementRemaining > 0) return;

            int normalCastGap = bossStandalone ? BossStandaloneCastGapMs
                : highFrequencyMode ? GlobalCastGapMs : MovementModeCastGapMs;
            int castGap = combatOpeningPriorityActive || initialDebuffBurst || sentryFillReady || mfdRetryYieldToSentry
                    || combatTrashSetupChain
                ? Math.Max(100, InitialSetupBurstGapMs)
                : missingIceblinkElites > 0 || urgentMfdBeforeSentry || materialMfdUpgradeReady
                    || bossEntangleDue || actionableIceblinkElites > 0
                    || trashIceblinkQueueDue || sentryIceblinkPreempt
                    ? bossStandalone ? BossUrgentRetryGapMs
                        : highFrequencyMode ? UrgentRetryGapMs : MovementUrgentRetryGapMs
                    : sentryTimingWorkActive
                        ? SentryRecastMs : normalCastGap;
            if (_lastCastFinishedTick != int.MinValue && Elapsed(_lastCastFinishedTick, now) < castGap)
            {
                if (manualDebuffHold) TryStartManualDebuffEntangle(local, cluster, now);
                return;
            }

            if (trashFightActive && !_trashInitialMultishotDone && trashIceblinkDue > 0
                && !(sentryFairnessDue && sentryFairnessReady)
                && s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill
                && local.Multishot != null && TryStartMultishot(local, cluster, now, false, true))
            {
                _cast.TrashInitialMultishot = true;
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            // One canonical MFD->Sentry fairness handoff. After repeated real MFD effect
            // failures (or a sustained unavailable gate), allow exactly one Sentry turn.
            // The Sentry changes _lastSupportKind, so the next scheduler pass returns to
            // authoritative MFD recovery instead of repeatedly yielding the queue.
            if (mfdRetryYieldToSentry)
            {
                if (TryStartSentryDuringMfdRetry(local, cluster, now, true)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            if (trashFightActive && trashInitialMultishotReady
                && currentFightSentryFillPending && trashMfdCoverageMissing
                && s7o_ZDH_HelperState.AutoSentry && local.Guardian && local.Sentry != null)
            {
                if (s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                    && local.MarkedForDeath != null
                    && TryStartMarkedForDeathFair(local, cluster, now, true, false))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
            }

            // A DPS must be able to stand on the elite under Guardian coverage. After the
            // existing per-elite validation delay, the first two actionable re-cover attempts
            // receive one narrow post-opening handoff so repeated MFD/Multishot traffic cannot
            // leave an elite uncovered for seconds. Fresh Iceblink and Momentum retry ownership
            // retain their existing first claim.
            bool urgentEliteProtectionTurn = _openingSentryBurstsClosedForEngagement
                && urgentEliteCoveragePending && eliteSentryFairnessReady
                && freshMissingIceblinkElites == 0
                && !combatMomentumRetryGapUrgentDebuff;
            if (urgentEliteProtectionTurn)
            {
                if (TryStartSentry(local, cluster, now, true, false)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            // An MFD input with no observed effect creates durable retry debt. One bounded
            // Sentry fairness turn may occur after repeated failures, but Valley recovery is
            // otherwise authoritative until real coverage exists.
            if (_mfdRetryDebt && (missingMfdCoverage || trashMfdCoverageMissing)
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && local.MarkedForDeath != null)
            {
                if (TryStartMarkedForDeathFair(local, cluster, now, true, false)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            // Fresh Iceblink loss keeps first claim because one cone shot is normally the fastest
            // recovery. If this same loss has already received a Multishot attempt, do not let a
            // blocked/missed cone keep an entirely missing Valley behind repeated Iceblink retries.
            bool mfdRecoveryBeforeRepeatedIceblink = liveMfdRecoveryRequired
                && missingIceblinkElites > 0 && freshMissingIceblinkElites == 0;
            if (mfdRecoveryBeforeRepeatedIceblink
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                && local.MarkedForDeath != null)
            {
                if (TryStartMarkedForDeathFair(local, cluster, now, true, false)) return;
                if (_supportPrimaryGateBlocked) return;
            }

            if (missingIceblinkElites > 0)
            {
                if (initialDebuffBurst)
                {
                    if (s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley && local.MarkedForDeath != null
                        && TryStartMarkedForDeathFair(local, cluster, now, true, false))
                    {
                        return;
                    }
                    if (_supportPrimaryGateBlocked) return;

                }

                if (yieldIceblinkRetryToSentry && !combatMomentumRetryGapUrgentDebuff
                    && TryStartFairnessSentry(local, cluster, now,
                        sentryHardRefillPending, eliteCoveragePending,
                        urgentBonusCoveragePending, eligibleBonusCoveragePending,
                        sentryRollingRefreshPending))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

                if (s7o_ZDH_HelperState.AutoMultishot && local.Iceblink && local.WindChill && local.Multishot != null
                    && TryStartMultishot(local, cluster, now, true))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
                if (sentryEngagementActive && s7o_ZDH_HelperState.AutoMultishot
                    && local.Iceblink && local.WindChill && local.Multishot != null
                    && !_openingMultishotAttemptedForEngagement)
                    return;

                if (urgentMfdBeforeSentry
                    && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley
                    && local.MarkedForDeath != null && TryStartMarkedForDeathFair(local, cluster, now, true, false))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;

            }

            if (urgentMfdBeforeSentry
                && s7o_ZDH_HelperState.AutoMarkedForDeath && local.Valley && local.MarkedForDeath != null
                && TryStartMarkedForDeathFair(local, cluster, now, true, false))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            // A closed Momentum retry window may host urgent debuff recovery only. If neither
            // missing-debuff branch could start, do not spend the gap on Sentry or routine upkeep;
            // the next open Primary input window remains authoritative.
            if (combatMomentumRetryGapUrgentDebuff)
            {
                return;
            }

            bool prioritySentryRepair = sentryFairnessDue && sentryFairnessReady;
            if (prioritySentryRepair
                && TryStartFairnessSentry(local, cluster, now,
                    sentryHardRefillPending, eliteCoveragePending,
                    urgentBonusCoveragePending, eligibleBonusCoveragePending,
                    sentryRollingRefreshPending))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

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
                if (TryStartSentry(local, cluster, now, false,
                    countSetup: !_openingSentryBurstsClosedForEngagement,
                    forcePopulationFill: sentryHardRefillPending))
                {
                    return;
                }
                if (_supportPrimaryGateBlocked) return;
            }

            // While an at-cap refresh is deferred, do not let unrelated maintenance consume the
            // exception. If no opening action could start this pass, hand the queue back to Primary.
            if (combatMomentumRecoveryDeferredForOpening || combatMomentumRefreshDeferredForOpening)
            {
                SuppressDhStrafePrimary(150);
                return;
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
                && TryStartMultishot(local, cluster, now, false, trashFightActive,
                    sentryIceblinkPreempt))
            {
                return;
            }
            if (_supportPrimaryGateBlocked) return;

            bool sentryPlannerTried = false;
            if ((bossStandalone || sentryPopulationFull) && protectedSentryWorkReady
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
                && TryStartMarkedForDeathFair(local, cluster, now, false, false))
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

            if (manualDebuffHold && local.Odyssey && local.Entangle != null
                && TryStartManualDebuffEntangle(local, cluster, now))
                return;

            if (!bossStandalone && s7o_ZDH_HelperState.AutoEntangle && local.Odyssey && local.Entangle != null
                && TryStartEntangle(local, cluster, now, true))
            {
                return;
            }

            // Stable Combat fallback: use the user's live cursor rather than autosnapping. This
            // fills otherwise-empty movement slots with a short Entangle pulse, giving predictable
            // stop/go steering while every real Multishot/MFD/Sentry/Momentum priority above wins.
            if (combatCadenceEntangleAvailable && TryStartCombatCadenceEntangle(local, now))
                return;

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
                    if (!IsDisplayEligible(monster, zdh, now)) continue;
                    DrawDebuffTokens(monster, HasIceblink(monster), HasEntangle(monster), monster.MarkedForDeath, IsElectrified(monster));
                }
            }

            DrawElectrifiedAlert(now);

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
            if (wasBlocked)
            {
                // Cursor-safety recovery retains Helper's local movement delay, but it did not
                // consume a combat action and therefore must not reset the shared filler clock.
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
                    _lastRestoreConfirmed = true;
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
            ClearMfdRetryDebt();
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

            // The native spawn actor is already an authoritative ground anchor. Waiting for
            // the zDH's previous movement sample wastes most of the boss emergence animation;
            // the support transaction itself pauses/settles input before placing MFD/Sentries.
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
            _cast.ExpectedWorldZ = placement.WorldZ;

            _cast.BaselineImportantApplied = 0;
            _cast.BaselineMfdActorAcd = _lastValleyActorAcd;
            _cast.BaselineMfdActorCreatedTick = _lastValleyActorCreatedTick;
            _cast.BaselineMfdGameTick = Hud.Game.CurrentGameTick;
            ClearMfdImprovementCandidate();
            ClearMfdEdgeImprovementCandidate();
            return true;
        }

        private bool CanUseBossStandalone(ZdhLoadout local, int now)
        {
            if (local == null || local.Player == null || local.Player.FloorCoordinate == null) return false;

            // Turning F3 off near the RG must not disable support while the last movement
            // sample still reflects Strafe. Cast transactions already enforce their own
            // movement/input safety, so boss eligibility is based on the live boss itself.
            return FindStandaloneBoss(local.Player) != null;
        }

        private IMonster FindStandaloneBoss(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return null;
            return Hud.Game.AliveMonsters.Where(m => m != null && m.Rarity == ActorRarity.Boss
                    && IsAutomationBody(m) && !m.Invulnerable && m.Attackable && m.IsOnScreen
                    && Distance(player, m) <= BossStandaloneRange)
                .OrderBy(m => Distance(player, m))
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
            IMonster boss = cluster.Elites.FirstOrDefault(m =>
                m != null && m.Rarity == ActorRarity.Boss && IsDebuffBody(m));
            bool bossRefreshDue = boss != null && IsBossEntangleRefreshDue(cluster, now);
            List<IMonster> missingElites = cluster.Elites
                .Where(m => IsDebuffBody(m) && !HasEntangle(m)).ToList();
            if (cluster.SustainedSpecialFocus && cluster.FocusTarget != null && IsDebuffBody(cluster.FocusTarget)
                && !HasEntangle(cluster.FocusTarget) && !missingElites.Any(m => SameMonster(m, cluster.FocusTarget)))
                missingElites.Add(cluster.FocusTarget);

            // Outside RG standalone, Entangle remains recovery-only. At the RG, proactively
            // refresh the boss on the bounded maintenance cadence so the ~2s Odyssey effect
            // does not have to drop before the scheduler can react.
            if (!urgentOnly || (missingElites.Count == 0 && !bossRefreshDue)) return false;

            IMonster target = bossRefreshDue
                ? boss
                : missingElites.OrderByDescending(m => EntangleTargetScore(m, cluster.Bodies))
                    .ThenBy(m => Distance(local.Player, m))
                    .ThenBy(m => m.AcdId).FirstOrDefault();
            if (target == null || !IsDebuffBody(target)) return false;
            TargetState state = GetTargetState(target, now);
            if (Elapsed(state.LastEntangleAttempt, now) < FailedCastRetryMs) return false;
            IScreenCoordinate aim = CreateSafeDirectionalAim(local.Player, target.ScreenCoordinate);
            if (aim == null || !StartCast(CastKind.Entangle, local.Entangle, target.AcdId, aim, now,
                "Entangle Elite")) return false;

            state.LastEntangleAttempt = now;
            _lastEntangleMaintenanceTick = now;
            return true;
        }

        private bool IsBossEntangleRefreshDue(CombatCluster cluster, int now)
        {
            if (!_bossStandaloneActive || cluster == null) return false;
            IMonster boss = cluster.Elites.FirstOrDefault(m =>
                m != null && m.Rarity == ActorRarity.Boss && IsDebuffBody(m));
            if (boss == null) return false;
            if (!HasEntangle(boss)) return true;
            return _lastEntangleMaintenanceTick == int.MinValue
                || Elapsed(_lastEntangleMaintenanceTick, now) >= Math.Max(500, BossEntangleMaintenanceMs);
        }

        private void HandleManualDebuffRelease(int now)
        {
            _manualDebuffMovementUntilTick = unchecked(now
                + Math.Max(0, ManualDebuffReleaseMovementMs));

            if (_cast.Stage == CastStage.Idle) return;

            // Releasing CTRL is the explicit movement handoff. Abort any cast that has not
            // committed its skill input yet, and always abort the short manual Entangle filler.
            // A committed support cast keeps only its existing bounded release/restore tail.
            if (!_cast.InputSent || _cast.ManualDebuff)
                CancelCast("manual hold released");
        }

        private bool TryStartManualDebuffEntangle(ZdhLoadout local, CombatCluster cluster, int now)
        {
            if (local == null || local.Player == null || local.Entangle == null || !SkillReady(local.Entangle))
                return false;
            if (_lastManualDebuffCastFinishedTick != int.MinValue
                && Elapsed(_lastManualDebuffCastFinishedTick, now) < Math.Max(25, ManualDebuffCastGapMs))
                return false;

            List<IMonster> visible = Hud.Game.AliveMonsters
                .Where(m => m != null && m.IsOnScreen && IsDebuffBody(m))
                .ToList();
            List<IMonster> elites = visible.Where(IsStatusTarget).ToList();
            List<IMonster> eligible = elites.Count > 0 ? elites : visible;

            if (eligible.Count > 0)
            {
                List<IMonster> missing = eligible.Where(m => !HasEntangle(m)).ToList();
                IMonster target = (missing.Count > 0 ? missing : eligible)
                    .OrderByDescending(m => missing.Count > 0 && m.Rarity == ActorRarity.Boss)
                    .ThenBy(m => GetTargetState(m, now).LastEntangleAttempt)
                    .ThenByDescending(m => EntangleTargetScore(m, visible))
                    .ThenBy(m => Distance(local.Player, m))
                    .ThenBy(m => m.AcdId).FirstOrDefault();
                if (target == null) return false;

                TargetState state = GetTargetState(target, now);
                IScreenCoordinate aim = CreateSafeDirectionalAim(local.Player, target.ScreenCoordinate);
                if (aim == null || !StartCast(CastKind.Entangle, local.Entangle, target.AcdId, aim, now,
                    "Entangle CTRL", manualDebuff: true)) return false;

                state.LastEntangleAttempt = now;
                _lastEntangleMaintenanceTick = now;
                return true;
            }

            IScreenCoordinate cursorAim = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
            if (cursorAim == null || !StartCast(CastKind.Entangle, local.Entangle, 0, cursorAim, now,
                "Entangle CTRL Aim", manualDebuff: true, useCurrentCursorAim: true)) return false;

            _lastEntangleMaintenanceTick = now;
            return true;
        }

        private bool TryStartCombatCadenceEntangle(ZdhLoadout local, int now)
        {
            if (local == null || local.Entangle == null || !SkillReady(local.Entangle)
                || Hud == null || Hud.Window == null) return false;

            IScreenCoordinate cursorAim = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
            return cursorAim != null && PointInsideCastArea(cursorAim.X, cursorAim.Y)
                && StartCast(CastKind.Entangle, local.Entangle, 0, cursorAim, now,
                    "Entangle Cadence", useCurrentCursorAim: true);
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

        private bool IsIceblinkActionable(IMonster monster, int now)
        {
            if (monster == null || HasPendingMultishotValidation(monster.AcdId, now)) return false;
            if (!IsIceblinkDue(monster, now)) return false;
            if (HasPendingIceblinkValidation(monster, now)) return false;
            TargetState state = GetTargetState(monster, now);
            int retryMs = HasIceblink(monster)
                ? MultishotRefreshRetryMs
                : GetMultishotMissingRetryMs(state);
            return Elapsed(state.LastMultishotAttempt, now) >= Math.Max(100, retryMs);
        }

        private int GetMultishotMissingRetryMs(TargetState state)
        {
            if (state == null || state.ConsecutiveMultishotMisses <= 0)
                return Math.Max(100, MultishotFailedRetryMs);

            int misses = Math.Max(1, state.ConsecutiveMultishotMisses);
            int retry = Math.Max(MultishotFailedRetryMs, MultishotEffectMissRetryBaseMs)
                + Math.Max(0, misses - 1) * Math.Max(0, MultishotEffectMissRetryStepMs);
            return Math.Min(Math.Max(100, MultishotEffectMissRetryMaxMs), retry);
        }

        private bool HasUnattemptedCurrentIceblinkLoss(IMonster monster, int now)
        {
            if (monster == null || HasIceblink(monster)) return false;
            TargetState state = GetTargetState(monster, now);
            if (state.IceblinkMissingSinceTick == int.MinValue)
                state.IceblinkMissingSinceTick = now;
            return state.LastMultishotAttempt == int.MinValue
                || unchecked(state.IceblinkMissingSinceTick - state.LastMultishotAttempt) > 0;
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

        private bool IsIceblinkConePlanningDue(IMonster monster, int now)
        {
            if (monster == null || !IsDebuffBody(monster)
                || HasPendingMultishotValidation(monster.AcdId, now)
                || HasPendingIceblinkValidation(monster, now)) return false;
            if (!HasIceblink(monster)) return IsIceblinkActionable(monster, now);
            TargetState state = GetTargetState(monster, now);
            if (state.IceblinkConfirmedTick == int.MinValue) return true;
            int planningAge = Math.Max(0,
                GetIceblinkRefreshAgeMs() - Math.Max(0, MultishotConePlanningHorizonMs));
            return Elapsed(state.IceblinkConfirmedTick, now) >= planningAge;
        }

        private bool HasMultishotFillTargets(ZdhLoadout local, CombatCluster cluster, int now)
        {
            if (!s7o_ZDH_HelperState.AutoMultishot || local == null || local.Multishot == null
                || !local.Iceblink || !local.WindChill || cluster == null) return false;
            return cluster.Bodies.Any(monster => monster != null && IsDebuffBody(monster)
                && !monster.Invulnerable && monster.Attackable && monster.IsOnScreen);
        }

        private IMonster FindIceblinkRecoveryFocus(IEnumerable<IMonster> elites,
            HashSet<uint> plannedDueAcds, int now)
        {
            if (elites == null || plannedDueAcds == null || plannedDueAcds.Count == 0) return null;

            var actionable = elites.Where(elite => elite != null
                    && plannedDueAcds.Contains(elite.AcdId)
                    && IsIceblinkActionable(elite, now))
                .ToList();
            if (actionable.Count == 0 || !actionable.Any(elite =>
                    GetTargetState(elite, now).ConsecutiveMultishotMisses > 0))
                return null;

            // After a confirmed-fired shot misses an elite, prefer another due elite with fewer
            // misses before retrying the same obstructed angle. If it is the only target left,
            // its bounded retry timer makes it eligible again automatically.
            return actionable
                .OrderBy(elite => GetTargetState(elite, now).ConsecutiveMultishotMisses)
                .ThenByDescending(elite =>
                {
                    TargetState state = GetTargetState(elite, now);
                    return state.IceblinkMissingSinceTick == int.MinValue
                        ? 0 : Elapsed(state.IceblinkMissingSinceTick, now);
                })
                .ThenByDescending(elite => TargetPriority(elite, true))
                .ThenBy(elite => elite.AcdId)
                .FirstOrDefault();
        }

        private bool TryStartMultishot(ZdhLoadout local, CombatCluster cluster, int now,
            bool urgentOnly, bool trashDensityTimer = false, bool allowEarlyMaintenance = false,
            bool sentryFillInterleave = false, bool forceOpening = false)
        {
            if (cluster == null || cluster.Bodies.Count == 0 || !SkillReady(local.Multishot)) return false;

            bool openingSweep = forceOpening && _runtime.HighFrequencyMode
                && _wasSentryEngagementActive && !_openingMultishotAttemptedForEngagement;
            List<IMonster> primaryElites = MergeMonsters(cluster.Elites.Where(IsDebuffBody), GetActivePrimaryElites(local.Player, now));
            List<IMonster> eligible = MergeMonsters(cluster.Bodies.Where(IsDebuffBody), primaryElites.Where(IsDebuffBody));
            if (eligible.Count == 0) return false;

            bool densityTimer = trashDensityTimer && primaryElites.Count == 0;
            bool combatIntentTrash = primaryElites.Count == 0 && IsCombatIntentTrash(cluster);
            List<IMonster> missingPrimary = primaryElites.Where(m => !HasIceblink(m)).ToList();
            IEnumerable<IMonster> dueCandidates = openingSweep
                ? (primaryElites.Count > 0 ? primaryElites : eligible)
                : densityTimer ? eligible
                : urgentOnly && missingPrimary.Count > 0
                    ? missingPrimary
                    : primaryElites.Count > 0
                        ? primaryElites.Where(m => IsIceblinkDue(m, now))
                        : eligible.Where(m => IsIceblinkDue(m, now));
            var dueAcds = new HashSet<uint>(dueCandidates.Select(m => m.AcdId));

            List<IMonster> dueImportant = primaryElites.Where(m => dueAcds.Contains(m.AcdId)).ToList();
            bool urgent = !densityTimer && dueImportant.Count > 0;
            if (urgentOnly && !urgent && !openingSweep) return false;
            if (!urgentOnly && urgent && !sentryFillInterleave && !openingSweep) return false;

            int maintenanceMs = GetIceblinkRefreshAgeMs();
            int maintenanceAge = _lastMultishotMaintenanceTick == int.MinValue
                ? int.MaxValue : Elapsed(_lastMultishotMaintenanceTick, now);
            int maintenanceThreshold = allowEarlyMaintenance
                ? Math.Max(0, maintenanceMs - Math.Max(0, IceblinkPrimaryPreemptLeadMs))
                : maintenanceMs;
            bool maintenance = openingSweep || sentryFillInterleave || (densityTimer
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
            if (urgent && !openingSweep
                && !primaryElites.Any(m => actionableDueAcds.Contains(m.AcdId))) return false;

            HashSet<uint> plannedDueAcds = actionableDueAcds.Count > 0 ? actionableDueAcds : dueAcds;
            var burstPlanningAcds = new HashSet<uint>(plannedDueAcds);
            if (!densityTimer && plannedDueAcds.Count > 0)
                foreach (IMonster elite in primaryElites.Where(m => IsIceblinkConePlanningDue(m, now)))
                    burstPlanningAcds.Add(elite.AcdId);

            IMonster recoveryFocus = densityTimer ? null
                : FindIceblinkRecoveryFocus(primaryElites, plannedDueAcds, now);
            uint recoveryFocusAcd = recoveryFocus == null ? 0u : recoveryFocus.AcdId;
            bool singleImportantDue = !densityTimer && actionableDueAcds.Count == 1
                && primaryElites.Any(m => m != null
                    && actionableDueAcds.Contains(m.AcdId) && IsImportantDebuffTarget(m));
            // At point-blank range, aim directly through a due elite's world-space core.
            // Never let a nearby elite that is already covered steal the ray from the elites
            // that actually made Multishot due; otherwise the scheduler can retry forever in
            // the wrong direction while the missing targets remain unchanged.
            IMonster closeRangeDirectTarget = densityTimer || recoveryFocus != null ? null : primaryElites
                .Where(m => m != null && plannedDueAcds.Contains(m.AcdId)
                    && IsCloseRangeMultishotDirectTarget(local.Player, m))
                .OrderByDescending(m => TargetPriority(m, true))
                .ThenBy(m => Distance(local.Player, m))
                .ThenBy(m => m.AcdId)
                .FirstOrDefault();
            MultishotPlan plan = closeRangeDirectTarget == null ? null
                : BuildDirectMultishotFallbackPlan(local.Player, closeRangeDirectTarget, plannedDueAcds);
            bool closeRangeDirect = plan != null && plan.Primary != null && plan.Aim != null;
            if (!closeRangeDirect)
                plan = BuildMultishotPlan(local.Player, eligible, plannedDueAcds,
                    burstPlanningAcds, now, recoveryFocusAcd);
            bool urgentDirectFallback = false;
            if ((plan == null || plan.Primary == null || plan.Aim == null)
                && urgent && actionableDueAcds.Count > 0)
            {
                IMonster directTarget = primaryElites
                    .Where(m => m != null && actionableDueAcds.Contains(m.AcdId))
                    .OrderByDescending(m => TargetPriority(m, true))
                    .FirstOrDefault();
                if (directTarget != null)
                {
                    // Keep the optimized planner first. If it cannot produce any legal shot while
                    // Iceblink is actively missing, retry the same validated geometry centered on one
                    // actionable elite so planning failure can never turn into several seconds idle.
                    plan = BuildMultishotPlan(local.Player, eligible, plannedDueAcds,
                        burstPlanningAcds, now, directTarget.AcdId);
                    if (plan == null || plan.Primary == null || plan.Aim == null)
                        plan = BuildDirectMultishotFallbackPlan(local.Player, directTarget, plannedDueAcds);
                    urgentDirectFallback = plan != null && plan.Primary != null && plan.Aim != null;
                }
            }
            if (plan == null || plan.Primary == null || plan.Aim == null) return false;
            int minimumSettledCoverage = 0;
            int minimumTrashCoverage = openingSweep ? 1
                : combatIntentTrash ? 1 : Math.Min(3, TrashClusterMinBodies);
            if (primaryElites.Count == 0 && dueAcds.Count > 0
                && plan.CoveredMissingAcds.Count < minimumTrashCoverage) return false;
            if (primaryElites.Count == 0 && dueAcds.Count > 0)
                minimumSettledCoverage = minimumTrashCoverage;
            bool efficientCast = efficientWindow && !maintenance;
            if (efficientCast && !closeRangeDirect)
            {
                int coverageFloor = primaryElites.Count > 0 ? 1 : 3;
                int requiredCoverage = Math.Max(coverageFloor, (int)Math.Ceiling(
                    eligible.Count * Math.Max(0.50f, Math.Min(1.0f, EfficientMultishotCoverageRatio))));
                if (plan.CoveredBodyCount < requiredCoverage) return false;
                minimumSettledCoverage = Math.Max(minimumSettledCoverage, requiredCoverage);
            }

            if (!densityTimer && !maintenance && !efficientCast && !closeRangeDirect
                && !IsIceblinkActionable(plan.Primary, now)) return false;
            if (!EnsureSupportPrimaryReady(CastKind.Multishot, false, now)) return false;
            if (!StartCast(CastKind.Multishot, local.Multishot, plan.Primary.AcdId, plan.Aim, now,
                openingSweep ? "Multishot Combat Opening"
                    : closeRangeDirect ? "Multishot Close Core"
                    : recoveryFocus != null ? "Multishot Recovery Core"
                    : singleImportantDue ? "Multishot Single Core"
                    : urgentDirectFallback ? "Multishot Urgent Direct"
                    : sentryFillInterleave ? "Multishot Sentry Interleave"
                    : plan.CoveredEliteCount > 0 ? "Multishot Elite Cone" : "Multishot Density",
                float.NaN, float.NaN, plan.CoveredMissingAcds)) return false;
            _cast.MultishotDirectionX = plan.DirectionX;
            _cast.MultishotDirectionY = plan.DirectionY;
            _cast.HasMultishotDirection = true;
            // Any single-target or confirmed-miss recovery is resolved from the settled frame.
            // The five-yard close guard remains useful for the initial point-blank case, but it
            // must not be the only path allowed to discard a pre-pause direction.
            _cast.MultishotDirectCore = closeRangeDirect || recoveryFocus != null
                || singleImportantDue || urgentDirectFallback;
            _cast.MultishotMinimumBodyCoverage = minimumSettledCoverage;

            foreach (IMonster target in eligible)
                if (target != null && target.AcdId != 0)
                    _cast.MultishotEligibleAcds.Add(target.AcdId);
            foreach (uint acd in plannedDueAcds)
                if (acd != 0) _cast.MultishotDueAcds.Add(acd);
            foreach (uint acd in burstPlanningAcds)
                if (acd != 0) _cast.MultishotPlanningAcds.Add(acd);

            foreach (uint acd in plan.CoveredEliteAcds) _cast.VerifyImportantAcds.Add(acd);
            foreach (uint acd in plan.CoveredPrimaryEliteAcds)
            {
                _cast.MultishotCoveredEliteAcds.Add(acd);
                IMonster coveredElite = FindMonster(acd);
                if (coveredElite != null && HasIceblink(coveredElite))
                    _cast.MultishotBaselineActiveAcds.Add(acd);
            }
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
                UntilTick = unchecked(inputTick + Math.Max(1, MultishotVerifyMs)),
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
                _pendingMultishots.Add(pending);
        }

        private void UpdatePendingMultishotValidations(int now)
        {
            ObserveNativeMultishotAnimation(now);
            if (_pendingMultishots.Count == 0)
            {
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
                    _pendingMultishots.Remove(pending);
                    continue;
                }

                if (unresolved.Count == 0)
                {
                    foreach (uint acd in liveMissing)
                    {
                        IMonster appliedElite = FindMonster(acd);
                        if (appliedElite != null)
                            GetTargetState(appliedElite, now).ConsecutiveMultishotMisses = 0;
                    }
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

                foreach (uint acd in liveMissing.Except(unresolved))
                {
                    IMonster appliedElite = FindMonster(acd);
                    if (appliedElite != null)
                        GetTargetState(appliedElite, now).ConsecutiveMultishotMisses = 0;
                }
                if (pending.AnimationSeen || applied > 0)
                {
                    foreach (uint acd in unresolved)
                    {
                        if (!pending.ImportantAcds.Contains(acd)) continue;
                        IMonster missedElite = FindMonster(acd);
                        if (missedElite == null) continue;
                        TargetState missedState = GetTargetState(missedElite, now);
                        missedState.ConsecutiveMultishotMisses = Math.Min(8,
                            missedState.ConsecutiveMultishotMisses + 1);
                    }
                }

                if (pending.AnimationSeen || applied > 0) CommitPendingIceblinkRefresh(pending, now);
                string result = applied > 0 ? "partial"
                    : pending.AnimationSeen ? "fired-no-debuff" : "input-unconfirmed";
                string source = pending.AnimationSeen
                    ? (applied > 0 ? "native animation / partial debuff" : "native animation / no debuff")
                    : (applied > 0 ? "partial debuff" : "no native animation / no debuff");
                _pendingMultishots.Remove(pending);
                if (pending.TrashInitial && applied == 0) _trashInitialMultishotDone = false;
                if (unresolved.Any(acd => pending.ImportantAcds.Contains(acd)))
                {
                    _urgentRetryKind = CastKind.Multishot;
                    _urgentRetryTick = now;
                }
            }
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
            _lastMultishotMaintenanceTick = now;
            if (pending.TrashInitial) _trashInitialMultishotDone = true;
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

        private static bool IsNativeMfdAnimation(AnimSnoEnum animation)
        {
            return animation == AnimSnoEnum._demonhunter_female_cast_markedfordeath
                || animation == AnimSnoEnum._demonhunter_male_cast_markedfordeath;
        }

        private void CommitPendingIceblinkRefresh(PendingMultishotValidation pending, int now)
        {
            _lastMultishotMaintenanceTick = now;
            if (pending != null && pending.TrashInitial) _trashInitialMultishotDone = true;
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

        private void ClearPendingMultishotValidations()
        {
            _pendingMultishots.Clear();
            _lastObservedPlayerAnimationValid = false;
        }

        private void CompleteMultishotDispatch(int now, bool activationAccepted)
        {
            // Hand movement back before this short activation observer, but only arm effect
            // validation after Diablo accepts the Multishot. This prevents failed inputs from spawning
            // overlapping retry validators and flooding the support scheduler.
            if (activationAccepted) QueuePendingMultishotValidation(now);
            if (activationAccepted)
            {
                _lastMultishotMaintenanceTick = now;
                if (_cast.TrashInitialMultishot) _trashInitialMultishotDone = true;
                if (_urgentRetryKind == CastKind.Multishot)
                {
                    _urgentRetryKind = CastKind.None;
                    _urgentRetryTick = int.MinValue;
                }
            }
            else
            {
                if (_cast.TrashInitialMultishot) _trashInitialMultishotDone = false;
                _urgentRetryKind = CastKind.Multishot;
                _urgentRetryTick = now;
            }
            if (_sentryFairnessDemandActive)
                _sentryFairnessMultishotTurns = Math.Min(8, _sentryFairnessMultishotTurns + 1);
            _lastSupportKind = CastKind.Multishot;
            _lastCastFinishedTick = now;
            ResetCast();
            CompleteBossEntangleStandstillRelease();
        }

        private void MarkOpeningInputAttempted(CastKind kind)
        {
            if (!_wasSentryEngagementActive || kind != CastKind.Multishot) return;
            _openingMultishotAttemptedForEngagement = true;
        }

        private void ArmMfdRetryDebt(int now)
        {
            _mfdRetryDebt = true;
            _mfdRetryDebtBaselineActorAcd = _cast.BaselineMfdActorAcd;
            _mfdRetryDebtBaselineActorCreatedTick = _cast.BaselineMfdActorCreatedTick;
        }

        private void ClearMfdRetryDebt()
        {
            _mfdRetryDebt = false;
            _mfdRetryDebtBaselineActorAcd = 0;
            _mfdRetryDebtBaselineActorCreatedTick = 0;
        }

        private bool HasMfdRetryDebtRecoveryEvidence()
        {
            if (!_mfdRetryDebt) return false;
            return IsGenerationNewer(_lastValleyActorCreatedTick, _lastValleyActorAcd,
                _mfdRetryDebtBaselineActorCreatedTick, _mfdRetryDebtBaselineActorAcd)
                && _lastValleyActorSeenTick != int.MinValue;
        }

        private bool TryStartMarkedForDeathFair(ZdhLoadout local, CombatCluster cluster, int now,
            bool urgentOnly, bool eliteGainOnly = false)
        {
            bool started = TryStartMarkedForDeath(local, cluster, now, urgentOnly, eliteGainOnly);
            if (started)
            {
                _mfdUnavailableSinceTick = int.MinValue;
            }
            else if (urgentOnly && !_supportPrimaryGateBlocked
                && _mfdUnavailableSinceTick == int.MinValue)
            {
                _mfdUnavailableSinceTick = now;
            }
            return started;
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
            List<IMonster> planningTargets = hasPrimaryElite ? primaryElites : allTargets;
            if (!hasPrimaryElite && mfdOnlyTargets.Count == 0
                && !cluster.Stable && !cluster.TrashLatched && !cluster.SustainedSpecialFocus) return false;

            Placement trashSnapshot = !hasPrimaryElite && mfdOnlyTargets.Count == 0
                ? CreateTrashSnapshotPlacement(cluster, planningTargets, now) : null;
            IMonster uncoveredBoss = hasPrimaryElite
                ? primaryElites.FirstOrDefault(m => m != null && m.Rarity == ActorRarity.Boss
                    && m.Attackable && !m.Invulnerable && !m.MarkedForDeath)
                : null;
            Placement best = uncoveredBoss != null
                ? (CreateScoredPlacement(uncoveredBoss.FloorCoordinate.X, uncoveredBoss.FloorCoordinate.Y,
                    uncoveredBoss.FloorCoordinate.Z, planningTargets, now) ?? FindBestPlacement(planningTargets, now, true))
                : !hasPrimaryElite && mfdOnlyTargets.Count > 0
                    ? FindBestJuggernautAnchoredPlacement(mfdOnlyTargets, allTargets, now)
                    : trashSnapshot ?? FindBestPlacement(planningTargets, now, true);
            if (best == null) return false;
            Placement current = CurrentValleyPlacement(planningTargets, now);
            int currentBodies = current == null ? 0 : current.CoveredBodies;
            double currentScore = current == null ? 0 : current.Score;
            int densityGain = best.CoveredBodies - currentBodies;

            var currentSupportAcds = GetEffectiveMfdEliteCoverage(current, primaryElites);
            bool edgeReposition = false;
            bool urgent;
            if (hasPrimaryElite)
            {
                bool bossUnmarked = uncoveredBoss != null;
                bool noEliteCovered = currentSupportAcds.Count == 0;
                bool newEliteGain = HasNewEliteMfdCoverageGain(currentSupportAcds, best, primaryElites, now);
                bool materialGain = HasMaterialMfdCoverageGain(currentSupportAcds, best);
                bool priorityGain = HasPriorityMfdCoverageGain(currentSupportAcds, best,
                    primaryElites, now);
                bool currentCoverageSatisfied = IsMfdCoverageSatisfied(
                    currentSupportAcds.Count, primaryElites.Count);
                int eliteGainCount = Math.Max(0, best.CoveredElites - currentSupportAcds.Count);
                bool completesEliteCoverage = best.CoveredElites >= primaryElites.Count
                    && currentSupportAcds.Count < primaryElites.Count;
                bool immediateNewEliteGain = newEliteGain && materialGain && !currentCoverageSatisfied;
                bool eliteGain = materialGain || priorityGain;
                bool marginalSingleEliteGain = currentCoverageSatisfied && eliteGainCount <= 1
                    && !completesEliteCoverage;
                int gainStableMs = marginalSingleEliteGain
                    ? Math.Max(MfdEliteGainStableMs, MfdSingleEliteGainStableMs)
                    : MfdEliteGainStableMs;
                bool gainStable = bossUnmarked || noEliteCovered || immediateNewEliteGain
                    || (eliteGain && IsMfdImprovementStable(best, currentSupportAcds, now, gainStableMs));
                if (!eliteGain && !bossUnmarked && !noEliteCovered)
                    ClearMfdImprovementCandidate();
                urgent = bossUnmarked || noEliteCovered || (eliteGain && gainStable);

                if (eliteGainOnly && !(eliteGain && gainStable)) return false;
                if (urgentOnly && !urgent) return false;

                if (!urgentOnly)
                {
                    // Routine reposition is deliberately weaker than every missing-debuff lane.
                    // If a coverage/boss/focus improvement exists, let the normal urgent path own it.
                    if (eliteGain || bossUnmarked || noEliteCovered)
                    {
                        ClearMfdEdgeImprovementCandidate();
                        return false;
                    }

                    edgeReposition = IsMfdEdgeRepositionStable(
                        current, best, currentSupportAcds, primaryElites, now);
                    if (!edgeReposition) return false;
                    if (Elapsed(_lastMfdCastTick, now) < Math.Max(0, MarkedForDeathRecastMs))
                        return false;
                }

                if (urgent)
                {
                    int recast = noEliteCovered || bossUnmarked || immediateNewEliteGain
                        ? MarkedForDeathUrgentRecastMs
                        : marginalSingleEliteGain
                            ? Math.Max(MfdEliteGainRecastMs, MfdSingleEliteGainRecastMs)
                            : MfdEliteGainRecastMs;
                    if (Elapsed(_lastMfdCastTick, now) < recast) return false;
                }
            }
            else
            {
                ClearMfdImprovementCandidate();
                ClearMfdEdgeImprovementCandidate();
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
            IMonster primary = !hasPrimaryElite && mfdOnlyTargets.Count > 0
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
            bool bossPlanned = primaryElites.Any(m => m != null && m.Rarity == ActorRarity.Boss)
                && best.CoveredBosses > 0;
            string mfdLabel = !hasPrimaryElite && mfdOnlyTargets.Count > 0 ? "MFD Juggernaut"
                : uncoveredBoss != null ? "MFD Boss"
                : bossPlanned ? "MFD Boss Priority"
                : edgeReposition ? "MFD Elite Reposition"
                : hasPrimaryElite ? "MFD Elite Density" : "MFD Trash Density";
            if (!StartCast(CastKind.MarkedForDeath, local.MarkedForDeath, primary.AcdId, best.Screen, now,
                mfdLabel, best.WorldX, best.WorldY, verifyTargets)) return false;
            _cast.ExpectedWorldZ = best.WorldZ;

            foreach (uint acd in best.CoveredEliteAcds) _cast.VerifyImportantAcds.Add(acd);
            _cast.BaselineImportantApplied = _cast.VerifyImportantAcds.Count(acd =>
            {
                IMonster planned = FindMonster(acd);
                return planned != null && planned.MarkedForDeath;
            });
            _cast.BaselineMfdActorAcd = _lastValleyActorAcd;
            _cast.BaselineMfdActorCreatedTick = _lastValleyActorCreatedTick;
            _cast.BaselineMfdGameTick = Hud.Game.CurrentGameTick;
            ClearMfdImprovementCandidate();
            ClearMfdEdgeImprovementCandidate();
            return true;
        }

        private bool IsMfdImprovementStable(Placement best, HashSet<uint> currentEliteAcds,
            int now, int requiredStableMs)
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
            return stableMs >= Math.Max(0, requiredStableMs);
        }

        private void ClearMfdImprovementCandidate()
        {
            _mfdImprovementSignature = string.Empty;
            _mfdImprovementTick = int.MinValue;
        }

        private bool IsMfdEdgeRepositionStable(Placement current, Placement best,
            HashSet<uint> currentEliteAcds, IEnumerable<IMonster> primaryElites, int now)
        {
            if (current == null || best == null || currentEliteAcds == null
                || currentEliteAcds.Count == 0 || primaryElites == null
                || float.IsNaN(current.WorldX) || float.IsNaN(current.WorldY)
                || float.IsNaN(best.WorldX) || float.IsNaN(best.WorldY))
            {
                ClearMfdEdgeImprovementCandidate();
                return false;
            }

            // A centering move may never drop an elite that the current live Valley covers.
            if (currentEliteAcds.Any(acd => !best.CoveredEliteAcds.Contains(acd)))
            {
                ClearMfdEdgeImprovementCandidate();
                return false;
            }

            double currentMargin = MfdPhysicalCoverageMargin(current, primaryElites, currentEliteAcds);
            double bestMargin = MfdPhysicalCoverageMargin(best, primaryElites, currentEliteAcds);
            if (currentMargin == double.MinValue || bestMargin == double.MinValue
                || currentMargin > Math.Max(0f, MfdEdgeSafetyMargin)
                || bestMargin < currentMargin + Math.Max(0f, MfdEdgeMinimumImprovement))
            {
                ClearMfdEdgeImprovementCandidate();
                return false;
            }

            string signature = string.Join(",", currentEliteAcds
                .OrderBy(x => x)
                .Select(x => x.ToString(CultureInfo.InvariantCulture))
                .ToArray());
            if (!string.Equals(signature, _mfdEdgeImprovementSignature, StringComparison.Ordinal))
            {
                _mfdEdgeImprovementSignature = signature;
                _mfdEdgeImprovementTick = now;
                return false;
            }

            if (_mfdEdgeImprovementTick == int.MinValue)
            {
                _mfdEdgeImprovementTick = now;
                return false;
            }

            return Elapsed(_mfdEdgeImprovementTick, now) >= Math.Max(0, MfdEdgeStableMs);
        }

        private double MfdPhysicalCoverageMargin(Placement placement,
            IEnumerable<IMonster> targets, HashSet<uint> requiredAcds)
        {
            if (placement == null || targets == null || requiredAcds == null
                || requiredAcds.Count == 0 || float.IsNaN(placement.WorldX)
                || float.IsNaN(placement.WorldY)) return double.MinValue;

            double minimum = double.MaxValue;
            bool found = false;
            foreach (IMonster target in targets)
            {
                if (target == null || target.FloorCoordinate == null
                    || !requiredAcds.Contains(target.AcdId)) continue;

                double margin = ValleyRadius + GetMonsterRadiusBottom(target)
                    - target.FloorCoordinate.XYDistanceTo(placement.WorldX, placement.WorldY);
                minimum = Math.Min(minimum, margin);
                found = true;
            }
            return found ? minimum : double.MinValue;
        }

        private void ClearMfdEdgeImprovementCandidate()
        {
            _mfdEdgeImprovementSignature = string.Empty;
            _mfdEdgeImprovementTick = int.MinValue;
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

        private bool HasMissingPrimaryMfdCoverage(IPlayer zdh, int now, out bool materialUpgradeReady,
            out bool majorUpgradeReady)
        {
            materialUpgradeReady = false;
            majorUpgradeReady = false;
            List<IMonster> targets = GetActiveMfdSupportTargets(zdh, now);
            if (targets.Count == 0)
            {
                ClearMfdImprovementCandidate();
                return false;
            }

            List<IMonster> primaryElites = targets.Where(IsGroundSupportPrimaryElite).ToList();
            List<IMonster> planningTargets = primaryElites.Count > 0 ? primaryElites : targets;
            if (primaryElites.Any(m => m != null && m.Rarity == ActorRarity.Boss
                && m.Attackable && !m.Invulnerable && !m.MarkedForDeath))
            {
                ClearMfdImprovementCandidate();
                return true;
            }

            Placement current = CurrentValleyPlacement(planningTargets, now);
            List<IMonster> mfdOnlyTargets = planningTargets.Where(IsGroundSupportMfdOnlyTarget).ToList();
            Placement best = primaryElites.Count == 0 && mfdOnlyTargets.Count > 0
                ? FindBestJuggernautAnchoredPlacement(mfdOnlyTargets, planningTargets, now)
                : FindBestPlacement(planningTargets, now, true);
            var currentAcds = GetEffectiveMfdEliteCoverage(current, primaryElites);
            bool newEliteGain = HasNewEliteMfdCoverageGain(currentAcds, best, primaryElites, now);
            bool materialGain = HasMaterialMfdCoverageGain(currentAcds, best);
            bool priorityGain = HasPriorityMfdCoverageGain(currentAcds, best,
                primaryElites, now);
            bool currentCoverageSatisfied = IsMfdCoverageSatisfied(
                currentAcds.Count, primaryElites.Count);
            int eliteGainCount = best == null ? 0
                : Math.Max(0, best.CoveredElites - currentAcds.Count);
            bool completesEliteCoverage = best != null
                && best.CoveredElites >= primaryElites.Count
                && currentAcds.Count < primaryElites.Count;
            bool immediateNewEliteGain = newEliteGain && materialGain && !currentCoverageSatisfied;
            bool actionableGain = materialGain || priorityGain;
            if (currentAcds.Count > 0 && actionableGain)
            {
                bool marginalSingleEliteGain = currentCoverageSatisfied && eliteGainCount <= 1
                    && !completesEliteCoverage;
                int gainStableMs = marginalSingleEliteGain
                    ? Math.Max(MfdEliteGainStableMs, MfdSingleEliteGainStableMs)
                    : MfdEliteGainStableMs;
                bool gainStable = immediateNewEliteGain
                    || IsMfdImprovementStable(best, currentAcds, now, gainStableMs);
                int recast = immediateNewEliteGain
                    ? MarkedForDeathUrgentRecastMs
                    : marginalSingleEliteGain
                        ? Math.Max(MfdEliteGainRecastMs, MfdSingleEliteGainRecastMs)
                        : MfdEliteGainRecastMs;
                materialUpgradeReady = gainStable && Elapsed(_lastMfdCastTick, now) >= recast;
                majorUpgradeReady = materialUpgradeReady
                    && (eliteGainCount >= 2 || completesEliteCoverage || priorityGain);
            }
            else
            {
                ClearMfdImprovementCandidate();
            }
            // When the current field already covers most elites, a marginal one-elite move is
            // not treated as missing coverage until it has passed both stability and recast gates.
            bool hardMissing = currentAcds.Count == 0
                || (actionableGain && (!currentCoverageSatisfied || materialUpgradeReady));
            return hardMissing;
        }

        private static HashSet<uint> GetEffectiveMfdEliteCoverage(Placement current,
            IEnumerable<IMonster> primaryElites)
        {
            var covered = new HashSet<uint>(current == null
                ? Enumerable.Empty<uint>() : current.CoveredEliteAcds);

            // When the native Valley actor is available, its physical 15-yard field is the
            // placement authority. Monster.MarkedForDeath can linger briefly after an elite
            // reaches the edge and must not hide a real geometry loss. Only use native monster
            // flags as the existing fallback when the Valley actor itself is unavailable.
            bool hasPhysicalValley = current != null
                && !float.IsNaN(current.WorldX) && !float.IsNaN(current.WorldY);
            if (hasPhysicalValley || primaryElites == null) return covered;

            foreach (IMonster elite in primaryElites)
                if (elite != null && elite.MarkedForDeath)
                    covered.Add(elite.AcdId);
            return covered;
        }

        private static bool HasMaterialMfdCoverageGain(HashSet<uint> currentAcds, Placement best)
        {
            if (best == null) return false;
            int currentCount = currentAcds == null ? 0 : currentAcds.Count;
            return best.CoveredElites > currentCount;
        }

        private bool HasPriorityMfdCoverageGain(HashSet<uint> currentAcds, Placement best,
            IEnumerable<IMonster> primaryElites, int now)
        {
            if (best == null || primaryElites == null) return false;
            return primaryElites.Any(elite => elite != null
                && (currentAcds == null || !currentAcds.Contains(elite.AcdId))
                && best.CoveredEliteAcds.Contains(elite.AcdId)
                && (elite.Rarity == ActorRarity.Boss || IsCurrentPartyFocus(elite, now)));
        }

        private bool IsMfdCoverageSatisfied(int coveredElites, int eligibleElites)
        {
            if (coveredElites <= 0 || eligibleElites <= 0) return false;
            float ratio = Math.Max(0.50f, Math.Min(1.0f, MfdSatisfiedCoverageRatio));
            int required = Math.Max(1, (int)Math.Ceiling(eligibleElites * ratio));
            return coveredElites >= required && eligibleElites - coveredElites <= 1;
        }

        private bool HasNewEliteMfdCoverageGain(HashSet<uint> currentAcds, Placement best,
            IEnumerable<IMonster> primaryElites, int now)
        {
            if (best == null || primaryElites == null) return false;
            int currentCount = currentAcds == null ? 0 : currentAcds.Count;
            if (best.CoveredElites < currentCount) return false;
            foreach (IMonster elite in primaryElites)
            {
                if (elite == null || (currentAcds != null && currentAcds.Contains(elite.AcdId))
                    || !best.CoveredEliteAcds.Contains(elite.AcdId)) continue;
                if (IsFreshSupportElite(elite, now)) return true;
            }
            return false;
        }

        private bool IsFreshSupportElite(IMonster elite, int now)
        {
            if (elite == null) return false;
            TargetState state = GetTargetState(elite, now);
            return state.FirstSupportTick != int.MinValue
                && Elapsed(state.FirstSupportTick, now) <= Math.Max(0, NewElitePriorityMs);
        }

        private bool HasFreshReadyEliteSentryNeed(IEnumerable<IMonster> elites, int now)
        {
            return (elites ?? Enumerable.Empty<IMonster>()).Any(elite => elite != null
                && IsFreshSupportElite(elite, now) && EliteSentryCoverageReady(elite, now));
        }

        private bool HasUrgentReadyEliteSentryNeed(IEnumerable<IMonster> elites, int now)
        {
            foreach (IMonster elite in elites ?? Enumerable.Empty<IMonster>())
            {
                if (elite == null || !EliteSentryCoverageReady(elite, now)) continue;
                TargetState state;
                if (!_targets.TryGetValue(elite.AcdId, out state)) continue;
                if (state.SentryCoverageAttempts < EliteSentryUrgentAttemptLimit) return true;
            }
            return false;
        }

        private bool HasCurrentInitialMfdSetupCoverage(CombatCluster cluster,
            bool trashFightActive, int now)
        {
            if (cluster == null) return false;
            List<IMonster> clusterTargets = cluster.Bodies
                .Where(m => m != null && m.Rarity != ActorRarity.RareMinion)
                .ToList();
            List<IMonster> primaryElites = clusterTargets.Where(IsGroundSupportPrimaryElite).ToList();
            List<IMonster> planningTargets = primaryElites.Count > 0
                ? primaryElites : clusterTargets.Where(IsGroundSupportMfdOnlyTarget).ToList();

            if (planningTargets.Count > 0)
            {
                Placement current = CurrentValleyPlacement(planningTargets, now);
                HashSet<uint> covered = GetEffectiveMfdEliteCoverage(current, planningTargets);
                bool priorityTargetMissing = planningTargets.Any(m => m != null
                    && (m.Rarity == ActorRarity.Boss || IsCurrentPartyFocus(m, now))
                    && !covered.Contains(m.AcdId));
                if (!priorityTargetMissing
                    && IsMfdCoverageSatisfied(covered.Count, planningTargets.Count))
                    return true;
            }

            if (trashFightActive && clusterTargets.Any(m => m.MarkedForDeath)) return true;
            return trashFightActive && HasCurrentTrashMfdCoverage(cluster, now);
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

        private bool SentryRelocationBackoffReady(int now)
        {
            return _sentryRelocationBackoffTick == int.MinValue
                || Elapsed(_sentryRelocationBackoffTick, now) >= Math.Max(0, SentryRelocationSinkBackoffMs);
        }

        private bool IsTerrainSentryRetry()
        {
            return !string.IsNullOrEmpty(_sentryRetryReason)
                && (_sentryRetryReason.IndexOf("relocation sink", StringComparison.OrdinalIgnoreCase) >= 0
                    || _sentryRetryReason.IndexOf("local Sentry overlap", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ClearSentryRelocationState()
        {
            _sentryRelocationSinkX = 0;
            _sentryRelocationSinkY = 0;
            _sentryRelocationSinkTick = int.MinValue;
            _sentryRelocationSinkCount = 0;
            _sentryRelocationBackoffTick = int.MinValue;
            _sentryRelocationOriginX = 0;
            _sentryRelocationOriginY = 0;
            _sentryRelocationAnchorX = 0;
            _sentryRelocationAnchorY = 0;
            _sentryRelocationContextValid = false;
            if (IsTerrainSentryRetry()) ClearSentryRetry();
        }

        private void UpdateSentryRelocationContext(IPlayer player, CombatCluster cluster)
        {
            if (_sentryRelocationBackoffTick == int.MinValue || !_sentryRelocationContextValid) return;
            bool playerMoved = player != null && player.FloorCoordinate != null
                && Distance2D(_sentryRelocationOriginX, _sentryRelocationOriginY,
                    player.FloorCoordinate.X, player.FloorCoordinate.Y)
                    >= Math.Max(2f, SentryRelocationPlayerMoveClearDistance);
            bool anchorMoved = cluster != null
                && Distance2D(_sentryRelocationAnchorX, _sentryRelocationAnchorY,
                    cluster.CenterX, cluster.CenterY)
                    >= Math.Max(4f, SentryRelocationAnchorMoveClearDistance);
            if (playerMoved || anchorMoved) ClearSentryRelocationState();
        }

        private bool RecordSentryRelocation(int now)
        {
            if (float.IsNaN(_cast.SentryActualWorldX) || float.IsNaN(_cast.SentryActualWorldY)) return false;

            bool sameSink = _sentryRelocationSinkTick != int.MinValue
                && Elapsed(_sentryRelocationSinkTick, now) <= Math.Max(500, SentryRelocationSinkHoldMs)
                && Distance2D(_sentryRelocationSinkX, _sentryRelocationSinkY,
                    _cast.SentryActualWorldX, _cast.SentryActualWorldY) <= Math.Max(1f, SentryRelocationSinkRadius);
            if (sameSink)
            {
                _sentryRelocationSinkCount++;
            }
            else
            {
                _sentryRelocationSinkX = _cast.SentryActualWorldX;
                _sentryRelocationSinkY = _cast.SentryActualWorldY;
                _sentryRelocationSinkCount = 1;
            }
            _sentryRelocationSinkTick = now;

            IPlayer local = Hud == null || Hud.Game == null ? null : Hud.Game.Me;
            bool localStack = false;
            if (local != null && local.FloorCoordinate != null
                && DistanceToPoint(local, _cast.SentryActualWorldX, _cast.SentryActualWorldY) <= GuardianRadius)
            {
                List<IActor> nearby = GetOnScreenOwnedSentries()
                    .Where(actor => actor != null && actor.FloorCoordinate != null
                        && actor.FloorCoordinate.XYDistanceTo(_cast.SentryActualWorldX, _cast.SentryActualWorldY)
                            <= Math.Max(1f, SentrySevereOverlapDistance))
                    .ToList();
                localStack = nearby.Count >= 2;
            }

            bool repeatedSink = _sentryRelocationSinkCount >= Math.Max(2, SentryRelocationSinkRepeatThreshold);
            if (!localStack && !repeatedSink) return false;

            _sentryRelocationBackoffTick = now;
            if (local != null && local.FloorCoordinate != null)
            {
                _sentryRelocationOriginX = local.FloorCoordinate.X;
                _sentryRelocationOriginY = local.FloorCoordinate.Y;
                _sentryRelocationAnchorX = _runtime.SentryAnchorX;
                _sentryRelocationAnchorY = _runtime.SentryAnchorY;
                _sentryRelocationContextValid = true;
            }
            int delayMs = Math.Max(800, SentryRelocationSinkBackoffMs);
            SetSentryRetry(now, delayMs, localStack ? "local Sentry overlap backoff" : "relocation sink backoff");
            return true;
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
            return cluster;
        }

        private int GetSentryPlacementDeficit(ZdhLoadout local, CombatCluster cluster, int now,
            List<IActor> sentries, out int effectiveOwned, out int distinctRelevant, out bool planValid)
        {
            effectiveOwned = 0;
            distinctRelevant = 0;
            planValid = false;
            _runtime.ProtectedSentryCoverageMissing = false;
            _runtime.EliteSentryCoverageMissing = false;
            _runtime.PlayerSentryProtectionMissing = false;
            _runtime.BonusCircleSentryCoverageMissing = false;
            _runtime.UrgentBonusCircleSentryCoverageMissing = false;
            _runtime.SentryDesired = 0;
            if (local == null || cluster == null || !local.Guardian || local.Sentry == null) return 0;

            List<Placement> desired = BuildDesiredSentryPlacements(local, cluster, now, false);
            if (desired.Count == 0) return 0;

            planValid = true;
            sentries = sentries ?? new List<IActor>();
            int targetCount = Math.Min(GetDesiredSentryCount(local), desired.Count);
            effectiveOwned = Math.Min(targetCount, CountRelevantSentries(desired, sentries));
            distinctRelevant = Math.Min(targetCount, CountDistinctRelevantSentries(desired, sentries));

            bool playerProtectionMissing = desired
                .Where(IsPlayerProtectionPlacement)
                .Any(placement => !IsSentryNear(sentries, placement.WorldX, placement.WorldY, GuardianRadius));
            float bonusCircleCoverageRadius = BonusCircleFullCoverageCenterRadius();
            bool bonusCircleCoverageMissing = desired
                .Where(IsBonusCircleSentryPlacement)
                .Any(placement => !IsSentryNear(sentries, placement.WorldX, placement.WorldY, bonusCircleCoverageRadius));
            bool urgentBonusCircleCoverageMissing = desired
                .Where(IsUrgentBonusCircleSentryPlacement)
                .Any(placement => !IsSentryNear(sentries, placement.WorldX, placement.WorldY, bonusCircleCoverageRadius));

            float eliteComfort = Math.Min(GuardianRadius, SentryEliteComfortRadius);
            bool eliteCoverageMissing = desired
                .Where(IsEliteSentryCoveragePlacement)
                .Any(placement => placement.CoveredEliteAcds.Any(acd =>
                    {
                        IMonster elite = FindMonster(acd);
                        return elite != null && EliteSentryCoverageReady(elite, now);
                    })
                    && !IsSentryNear(sentries, placement.WorldX, placement.WorldY, eliteComfort));

            _runtime.EliteSentryCoverageMissing = eliteCoverageMissing;
            _runtime.PlayerSentryProtectionMissing = playerProtectionMissing;
            _runtime.BonusCircleSentryCoverageMissing = bonusCircleCoverageMissing;
            _runtime.UrgentBonusCircleSentryCoverageMissing = urgentBonusCircleCoverageMissing;
            _runtime.ProtectedSentryCoverageMissing = playerProtectionMissing || bonusCircleCoverageMissing || eliteCoverageMissing;
            _runtime.SentryDesired = targetCount;

            int countDeficit = Math.Max(0, targetCount - effectiveOwned);
            if (countDeficit > 0) return countDeficit;

            // Spread/stacking is diagnostic and replacement quality, not an independent reason
            // to cast. Concrete player/bonus/elite coverage remains functional placement demand.
            return playerProtectionMissing || bonusCircleCoverageMissing || eliteCoverageMissing ? 1 : 0;
        }

        private bool TryStartSentry(ZdhLoadout local, CombatCluster cluster, int now, bool emergencyOnly,
            bool countSetup = false, bool sentryBurstChild = false, bool bypassBurstRecast = false,
            bool forcePopulationFill = false, bool rollingRefresh = false)
        {
            if (!SentryRelocationBackoffReady(now)) return false;
            if (local == null || cluster == null || local.Sentry == null || !local.Guardian
                || !SentryAvailable(local.Sentry))
                return false;

            int desiredCount = GetDesiredSentryCount(local);
            _runtime.SentryDesired = 0;
            if (countSetup && _sentryFullFieldHold) return false;

            int recastMs = countSetup ? Math.Max(100, InitialSetupBurstGapMs) : SentryRecastMs;
            if (!bypassBurstRecast && Elapsed(_lastSentryCastTick, now) < recastMs) return false;

            List<IActor> allSentries = GetOwnedSentries();
            List<Placement> desired = BuildDesiredSentryPlacements(local, cluster, now, emergencyOnly);
            int targetCount = Math.Min(desiredCount, desired.Count);
            int effectiveOwned = targetCount > 0 ? CountRelevantSentries(desired, allSentries) : 0;

            Placement missing = null;
            if (rollingRefresh)
            {
                missing = FindRollingSentryRefreshPlacement(
                    cluster, desired, allSentries, desiredCount, now);
            }
            else if (targetCount > 0)
            {
                bool eliteTriangleCore = desired.Count >= 3
                    && desired.Take(3).All(x => x != null
                        && string.Equals(x.Label, "Sentry Field Elite Coverage Triangle", StringComparison.Ordinal));
                missing = countSetup && effectiveOwned == 0 && !eliteTriangleCore
                    ? CreateRecentMfdSentryAnchor(cluster, allSentries, now) : null;
                if (missing != null && IsRejectedSentryPlacement(missing, now))
                    missing = null;
                if (missing == null)
                    missing = FindMissingDesiredSentryPlacement(
                        desired, allSentries, targetCount, emergencyOnly, now);
            }

            if (missing == null && forcePopulationFill && allSentries.Count < desiredCount)
            {
                missing = FindSafePopulationFillPlacement(
                    cluster, desired, allSentries, Math.Min(desiredCount, allSentries.Count + 1), now);
            }

            _runtime.SentryDesired = targetCount;
            _runtime.SentryPlacementDeficit = Math.Max(0, targetCount - effectiveOwned);
            if (missing == null) return false;

            string sentryLabel = string.IsNullOrEmpty(missing.Label) ? "Sentry Field" : missing.Label;
            if (countSetup) sentryLabel = "Sentry Count Setup";
            else if (rollingRefresh) sentryLabel = "Sentry Rolling Refresh";
            else if (forcePopulationFill && allSentries.Count < desiredCount)
                sentryLabel = "Sentry Population Fill";

            if (!sentryBurstChild && !EnsureSupportPrimaryReady(CastKind.Sentry, countSetup, now))
                return false;
            if (!StartCast(CastKind.Sentry, local.Sentry, missing.TargetAcd, missing.Screen, now,
                sentryLabel, missing.WorldX, missing.WorldY, null, sentryBurstChild))
                return false;

            _cast.ExpectedWorldZ = missing.WorldZ;
            _cast.SentryRequiredMatchRadius = Math.Max(1f, DesiredSentryMatchRadius(missing));
            foreach (uint acd in missing.CoveredEliteAcds)
                if (acd != 0) _cast.SentryCoverageAcds.Add(acd);
            _cast.SentrySlot = missing.SentrySlot;
            return true;
        }

        private bool TryStartFairnessSentry(ZdhLoadout local, CombatCluster cluster, int now,
            bool hardPopulation, bool eliteCoverage, bool urgentBonus,
            bool eligibleBonus, bool rollingRefresh)
        {
            if (hardPopulation && TryStartSentry(
                local, cluster, now, false, forcePopulationFill: true))
                return true;
            if ((eliteCoverage || urgentBonus || eligibleBonus)
                && TryStartSentry(local, cluster, now, true))
                return true;
            return rollingRefresh && TryStartSentry(
                local, cluster, now, false, rollingRefresh: true);
        }

        private bool TryStartSentryDuringMfdRetry(ZdhLoadout local, CombatCluster cluster, int now, bool allowed)
        {
            if (!allowed || local == null || local.Sentry == null || !local.Guardian) return false;
            if (!TryStartSentry(local, cluster, now, false,
                countSetup: !_openingSentryBurstsClosedForEngagement)) return false;
            return true;
        }

        private bool EnsureSupportPrimaryReady(CastKind kind, bool sentrySetup, int now)
        {
            if (kind == CastKind.Entangle || !s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh) return true;

            bool firstMfdInput = kind == CastKind.MarkedForDeath
                && !_initialMfdSetupSatisfiedForEngagement
                && _lastMfdCastTick == int.MinValue && _lastUnverifiedMfdTick == int.MinValue;
            bool openingSupport = _wasSentryEngagementActive && !sentrySetup
                && ((kind == CastKind.Multishot && !_openingMultishotAttemptedForEngagement)
                    || firstMfdInput);
            int requiredMs = openingSupport ? 0
                : sentrySetup ? SentrySetupPrimaryQuietMs
                : _bossStandaloneActive ? BossSupportPrimaryQuietMs
                : s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                    ? CombatSupportPrimaryQuietMs : SpeedSupportPrimaryQuietMs;
            int quietAgeMs = s7o_DHStrafePrimaryPlugin.PrimaryQuietAgeForZdh(now);
            int leaseMs = sentrySetup ? SentrySetupPreemptLeaseMs : PrimaryPreemptLeaseMs;
            SuppressDhStrafePrimary(Math.Max(leaseMs, requiredMs + 80));

            if (quietAgeMs == int.MaxValue || quietAgeMs >= Math.Max(0, requiredMs)) return true;

            _supportPrimaryGateBlocked = true;
            return false;
        }

        private void ResetEngagementSupportState()
        {
            _openingMultishotAttemptedForEngagement = false;
            _engagementStartedTick = int.MinValue;
            _lastSupportKind = CastKind.None;
            _lastMfdCastTick = int.MinValue;
            _lastUnverifiedMfdTick = int.MinValue;
            _mfdUnavailableSinceTick = int.MinValue;
            _hardMfdFailureStreak = 0;
            ClearMfdRetryDebt();
            ClearMfdImprovementCandidate();
            _urgentRetryKind = CastKind.None;
            _urgentRetryTick = int.MinValue;
        }

        private void ResetSentryBurstEngagement()
        {
            _runtime.SentryDesired = 0;
            _runtime.SentryAnchorX = 0;
            _runtime.SentryAnchorY = 0;
            _runtime.SentryPlacementDeficit = 0;
            _runtime.SentryCapacity = 0;
            _runtime.SentryOwned = 0;
            _runtime.SentryRelevant = 0;
            _runtime.SentryLocalCoreRelevant = 0;
            _runtime.SentryDistinctCoreRelevant = 0;
            _runtime.SentryDistinctCoreDeficit = 0;
            _runtime.SentryHardDeficit = 0;
            _runtime.SentryOldestAgeMs = -1;
            _runtime.SentryCharges = 0;
            _runtime.SentryPlanValid = false;
            _runtime.OpeningSentryBurstsClosed = false;
            _runtime.CoreBurstAttempts = 0;
            _runtime.CoreBurstAttemptLimit = Math.Max(1, SentryCoreBurstMaxAttemptsPerEngagement);
            _runtime.SentryRelocationBackoff = false;
            _runtime.SentryRelocationSinkCount = 0;
            _runtime.SentryRollingRefreshDue = false;
            _runtime.SentryRollingRefreshReady = false;
            _runtime.SentryFairnessDemand = false;
            _runtime.SentryFairnessTurns = 0;
            _runtime.SentryFairnessBudget = 0;
            _runtime.SentryFairnessDue = false;
            _runtime.ProtectedSentryCoverageMissing = false;
            _runtime.EliteSentryCoverageMissing = false;
            _runtime.EliteSentryUncoveredCount = 0;
            _runtime.EliteSentryReadyCount = 0;
            _runtime.EliteSentryPriorityAcd = 0;
            _runtime.EliteSentryPriorityAgeMs = -1;
            _runtime.EliteSentryPriorityDelayMs = -1;
            _runtime.EliteSentryPriorityAttempts = 0;
            _runtime.EliteSentryUrgent = false;
            _runtime.PlayerSentryProtectionMissing = false;
            _runtime.BonusCircleSentryCoverageMissing = false;
            _runtime.UrgentBonusCircleSentryCoverageMissing = false;
            _coreBurstAttemptedForEngagement = false;
            _coreBurstAttemptsThisEngagement = 0;
            _coreBurstRetryAfterTick = int.MinValue;
            _sentryRelevanceDeficitSinceTick = int.MinValue;
            _openingSentryBurstsClosedForEngagement = false;
            _sentryPlacedThisEngagement = false;
            _sentryFairnessMultishotTurns = 0;
            _sentryFairnessDemandActive = false;
            _sentryFullFieldHold = false;
            _initialMfdSetupSatisfiedForEngagement = false;
            ResetEngagementSupportState();
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
            ClearSentryRetry();
            ClearSentryRelocationState();
        }

        private void UpdateSentryBurstEngagement(CombatCluster cluster, bool sentryEngagementActive, int now)
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
                _sentryRelevanceDeficitSinceTick = int.MinValue;
                _openingSentryBurstsClosedForEngagement = false;
                _sentryPlacedThisEngagement = false;
                _sentryFairnessMultishotTurns = 0;
                _sentryFairnessDemandActive = false;
                _sentryFullFieldHold = false;
                _initialMfdSetupSatisfiedForEngagement = false;
                ResetEngagementSupportState();
                _engagementStartedTick = now;
                ClearSentryRetry();
                ClearSentryRelocationState();
                _coreBurstAnchorValid = true;
                _coreBurstAnchorX = cluster.CenterX;
                _coreBurstAnchorY = cluster.CenterY;

                _lastCompletionBurstAttemptCharges = -1;
                _lastCompletionBurstAttemptRelevant = -1;
                _lastCompletionBurstAttemptAnchorValid = false;
                _lastCompletionBurstAttemptTick = int.MinValue;
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

            bool voluntaryPreempt = string.Equals(
                    reason, "debuff preempt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "momentum primary due", StringComparison.OrdinalIgnoreCase);
            _coreBurstAttemptedForEngagement = false;
            if (voluntaryPreempt)
            {
                // A safe scheduler handoff is not a failed core attempt. Preserve any Sentries
                // already placed, but do not consume the bounded failure-attempt budget.
                if (_coreBurstAttemptsThisEngagement > 0)
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

            SuppressDhStrafePrimary(Math.Max(SentrySetupPreemptLeaseMs, SentryBurstAcquireMaxMs + 120));
            RequestDhStrafePause(Math.Max(120, maxMs + 120));
            return true;
        }

        private bool IsSentryBurstPrimaryQuietReady(int now)
        {
            int quietAge = s7o_DHStrafePrimaryPlugin.PrimaryQuietAgeForZdh(now);
            return quietAge == int.MaxValue
                || quietAge >= Math.Max(0, SentrySetupPrimaryQuietMs);
        }

        private bool TryBeginCoreSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            bool burstAutomationActive, bool sentryEngagementActive,
            bool sentryRetryReady, bool debuffsClear, bool fieldDeficitStable,
            int currentCoreRelevant, int coreTarget, int targetCount, bool channelingPylonActive)
        {
            if (_openingSentryBurstsClosedForEngagement
                || !IsSentryBurstPrimaryQuietReady(now)
                || _coreBurstAttemptedForEngagement
                || _coreBurstAttemptsThisEngagement >= Math.Max(1, SentryCoreBurstMaxAttemptsPerEngagement)
                || !CoreBurstRetryReady(now)
                || !SentryBurstFirstChildGapReady(now)
                || !burstAutomationActive
                || !sentryEngagementActive || !sentryRetryReady || !debuffsClear
                || !fieldDeficitStable
                || !s7o_ZDH_HelperState.AutoSentry || local == null || !local.Guardian
                || local.Sentry == null || !SentryAvailable(local.Sentry))
                return false;

            int deficit = Math.Max(0, coreTarget - currentCoreRelevant);
            int charges = Math.Max(0, local.Sentry.Charges);
            int planned = channelingPylonActive ? deficit : Math.Min(deficit, charges);
            if (planned < 1) return false;
            if (!BeginSentryBurst(SentryBurstMode.Core, local, cluster, now,
                planned, currentCoreRelevant, targetCount)) return false;
            return true;
        }

        private bool TryBeginCompletionSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            bool burstAutomationActive, bool sentryEngagementActive,
            bool sentryRetryReady, bool debuffsClear, bool fieldDeficitStable,
            int currentOwned, int currentCoreRelevant, int coreTarget, int targetCount, int hardDeficit,
            bool channelingPylonActive)
        {
            bool coreReady = currentCoreRelevant >= coreTarget;
            if (_openingSentryBurstsClosedForEngagement
                || !IsSentryBurstPrimaryQuietReady(now)
                || !SentryBurstFirstChildGapReady(now)
                || !burstAutomationActive || !sentryEngagementActive || !sentryRetryReady
                || !debuffsClear || !fieldDeficitStable || !coreReady
                || !s7o_ZDH_HelperState.AutoSentry || local == null || !local.Guardian
                || local.Sentry == null || !SentryAvailable(local.Sentry))
                return false;

            int deficit = Math.Max(0, hardDeficit);
            int charges = Math.Max(0, local.Sentry.Charges);
            int planned = channelingPylonActive
                ? Math.Min(2, deficit) : Math.Min(2, Math.Min(deficit, charges));
            if (planned <= 0 || !IsNewCompletionBurstOpportunity(cluster, currentOwned, charges, now))
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
                planned, currentOwned, targetCount)) return false;
            return true;
        }

        private void AdvanceSentryBurst(ZdhLoadout local, CombatCluster cluster, int now,
            int currentCoreRelevant, int currentOwned, int targetCount, int coreDeficit, int hardDeficit,
            bool sentryRetryReady, bool debuffsClear, bool channelingPylonActive)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            int charges = local == null || local.Sentry == null ? 0 : local.Sentry.Charges;
            int currentBurstRelevant = _sentryBurst.Mode == SentryBurstMode.Core
                ? currentCoreRelevant : currentOwned;
            _sentryBurst.CurrentRelevant = currentBurstRelevant;

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

            // The initial three-Sentry core may exceed the ordinary continuous-lease split below.
            // Momentum ownership is handled before planning reaches this method, at an idle child
            // boundary, so no partial child input can be interrupted here.
            bool initialThreeSentryCore = _sentryBurst.Mode == SentryBurstMode.Core
                && _sentryBurst.PlannedSentries > 0 && _sentryBurst.PlannedSentries <= 3;

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

                // The initial three-Sentry core pauses the macro but does not force Shift.
                // Natural Strafe release is enough to settle the actor, avoids a long standstill
                // ownership state, and the existing settle/watchdog path aborts cleanly if movement
                // does not stop. Other maintenance/replacement bursts keep the conservative Shift.
                if (!initialThreeSentryCore
                    && ForceStandstillVirtualKey != 0
                    && !ZdhInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
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
                return;
            }

            if (_sentryBurst.Stage == SentryBurstStage.Settle)
            {
                if (Hud.Game.Me.AnimationState != AcdAnimationState.Running)
                {
                    _sentryBurst.Stage = SentryBurstStage.Ready;
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

            // The initial three-Sentry Guardian core is deliberately allowed to finish
            // under one lease. The 800 ms movement split applies to larger/replacement
            // bursts and completion work only.
            int continuousLeaseMs = Elapsed(_sentryBurst.StartedTick, now);
            if (!initialThreeSentryCore
                && _sentryBurst.VerifiedSentries > 0
                && continuousLeaseMs >= Math.Max(500, SentryContinuousLeaseMaxMs))
            {
                EndSentryBurst("movement safety split", now);
                return;
            }

            int deficit = _sentryBurst.Mode == SentryBurstMode.Core
                ? Math.Max(0, coreDeficit) : Math.Max(0, hardDeficit);
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
            if (!TryStartSentry(local, cluster, now, false, true, true, bypassRecast,
                forcePopulationFill: _sentryBurst.Mode == SentryBurstMode.Completion))
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
            if (!SentryRelocationBackoffReady(now))
            {
                EndSentryBurst("relocation backoff", now);
                return;
            }
            if (_sentryBurst.Mode == SentryBurstMode.Core)
            {
                // A verified child is progress within this burst, not a new engagement.
                // Preserve the bounded Core-attempt budget until the engagement actually resets.
                _coreBurstRetryAfterTick = int.MinValue;
            }
            _sentryBurst.Stage = SentryBurstStage.Ready;
            _sentryBurst.ChildJustFinished = true;
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
            RecordCoreBurstEnd(reason, now);
            ReleaseActionInput();
            if (_cast.Stage != CastStage.Idle && _cast.SentryBurstChild && _cast.CursorOwned)
                RestoreCursorImmediately();
            ReleaseSentryBurstStandstill();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            bool momentumHandoff = string.Equals(
                reason, "momentum primary due", StringComparison.OrdinalIgnoreCase);
            if (_sentryBurst.VerifiedSentries > 0 && !momentumHandoff)
            {
                int quietMs = GetPostCastPrimaryQuietMs(CastKind.Sentry);
                if (quietMs > 0) SuppressDhStrafePrimary(quietMs);
            }
            RecordCombatActionCompleted(now);
            _lastCastFinishedTick = now;
            ResetSentryBurstState();
        }

        private void ForceAbortSentryBurst(string reason, int now)
        {
            if (_sentryBurst.Mode == SentryBurstMode.None) return;
            RecordCoreBurstEnd(reason, now);
            ReleaseActionInput();
            if (_cast.Stage != CastStage.Idle && _cast.SentryBurstChild && _cast.CursorOwned)
                RestoreCursorImmediately();
            ResetCast();
            ReleaseSentryBurstStandstill();
            ReleaseDhStrafePause();
            ReleaseDhStrafePrimarySuppression();
            RecordCombatActionCompleted(now);
            _lastCastFinishedTick = now;
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
            bool sentryBurstChild = false, bool manualDebuff = false, bool useCurrentCursorAim = false)
        {
            if (_cast.Stage != CastStage.Idle || skill == null || aim == null || !PointInsideCastArea(aim.X, aim.Y)) return false;
            if (ActionIsDown(skill.Key)) return false;

            ResetCast();
            _cast.Kind = kind;
            _cast.Stage = CastStage.Lease;
            _cast.SentryBurstChild = sentryBurstChild;
            _cast.ManualDebuff = manualDebuff;
            _cast.UseCurrentCursorAim = useCurrentCursorAim;
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
            _cast.BaselineCharges = kind == CastKind.Sentry ? skill.Charges : -1;
            _cast.BaselineOwnedSentries = kind == CastKind.Sentry ? GetOnScreenOwnedSentries().Count : 0;
            foreach (uint acd in GetRelevantActorIds(kind)) _cast.BaselineActorAcds.Add(acd);
            if (verifyTargetAcds != null)
                foreach (uint acd in verifyTargetAcds)
                    if (acd != 0) _cast.VerifyTargetAcds.Add(acd);
            _cast.ExpectedWorldX = expectedWorldX;
            _cast.ExpectedWorldY = expectedWorldY;
            _cast.ExpectedWorldZ = float.NaN;
            _cast.MultishotDirectionX = 0;
            _cast.MultishotDirectionY = 0;
            _cast.HasMultishotDirection = false;
            _cast.MultishotDirectCore = false;
            _cast.MultishotMinimumBodyCoverage = 0;
            _cast.SentryActualWorldX = float.NaN;
            _cast.SentryActualWorldY = float.NaN;
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
                if (!SetCastAimCursor(_cast.AimX, _cast.AimY))
                {
                    ResetCast();
                    return false;
                }
                _cast.CursorOwned = true;
                _cast.Stage = CastStage.Aim;
                _cast.DueTick = unchecked(now + _cast.AimSettleMs);
                RequestDhStrafePause(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                return true;
            }

            if (_cast.RequiresStrafePause) RequestDhStrafePause(GetPreInputHardLimitMs(kind) + 80);
            return true;
        }

        private bool RestoreCursorPreviewForRetry(int now, bool readinessRestart)
        {
            bool restored = RestoreCursorImmediately();
            if (!restored)
            {
                FinalizeCancelledCast("preview restore failed", now, false);
                return false;
            }

            _cast.CursorOwned = false;
            _cast.CursorReferenceValid = false;
            _cast.UserCursorDeltaX = 0;
            _cast.UserCursorDeltaY = 0;
            _cast.Stage = CastStage.Lease;
            _cast.DueTick = unchecked(now + Math.Max(1, AimCorrectionRetryMs));
            if (_cast.RequiresStrafePause)
                RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
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
            if (_cast.Stage != CastStage.Verify
                && _cast.Stage != CastStage.PostInputSettle
                && _cast.Stage != CastStage.Restore
                && _cast.Stage != CastStage.RestoreSettle
                && !_cast.CancellationPending)
            {
                int hardLimit = _cast.InputSent ? CastPostInputHardLimitMs : GetPreInputHardLimitMs(_cast.Kind);
                int hardLimitAge = _cast.InputSent && _cast.InputDownTick != int.MinValue
                    ? Elapsed(_cast.InputDownTick, now)
                    : Elapsed(_cast.StartedTick, now);
                bool finalPreInputOpportunity = !_cast.InputSent
                    && CanAdvanceExpiredPreInputFrame(animation, now);
                if (hardLimitAge > hardLimit && !finalPreInputOpportunity)
                {
                    bool settleTimeout = !_cast.InputSent
                        && RequiresMovementSettleBeforeInput() && !MovementSettledForCast(animation);
                    CancelCast(_cast.InputSent ? "post-input hard limit"
                        : settleTimeout ? "movement settle timeout" : "pause hard limit");
                    return;
                }
            }
            if (_cast.InputSent && _cast.Stage != CastStage.Lease)
            {
                AnimSnoEnum animationSno = Hud.Game.Me.Animation;
                bool animationChanged = !_cast.PreInputAnimationSnoValid
                    || animationSno != _cast.PreInputAnimationSno;

                // Generic Attacking/Casting is valid commit evidence only while Helper still
                // owns the cast transaction. After handback, resumed Strafe can itself enter
                // Attacking and must never retroactively validate MFD/Entangle. Multishot's
                // skill-specific native animation remains safe to observe during Verify.
                if (_cast.Stage != CastStage.Verify && animationChanged
                    && (animation == AcdAnimationState.Casting || animation == AcdAnimationState.Attacking))
                    _cast.SawCastAnimation = true;
                if (_cast.Kind == CastKind.Multishot && IsNativeMultishotAnimation(animationSno))
                    _cast.SawNativeMultishotAnimation = true;
                if (_cast.Kind == CastKind.MarkedForDeath && IsNativeMfdAnimation(animationSno))
                    _cast.SawNativeMfdAnimation = true;
            }

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
                    if (ForceStandstillVirtualKey != 0 && !ZdhInput.IsVirtualKeyDown(ForceStandstillVirtualKey))
                    {
                        _cast.StandstillHeld = ZdhInput.KeyDown(ForceStandstillVirtualKey);
                        if (!_cast.StandstillHeld) { CancelCast("standstill failed"); return; }
                    }
                }

                AcdAnimationState leaseAnimation = Hud.Game.Me.AnimationState;
                if (RequiresMovementSettleBeforeInput() && !MovementSettledForCast(leaseAnimation))
                {
                    if (_cast.RequiresStrafePause)
                        RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                    return;
                }
                if (!_cast.UseCurrentCursorAim)
                {
                    if (!RefreshCastAimFromCurrentView()) { CancelCast("aim refresh failed"); return; }
                    _cast.SavedCursorX = Hud.Window.CursorX;
                    _cast.SavedCursorY = Hud.Window.CursorY;
                    InitializeCursorIntent();
                    if (!SetCastAimCursor(_cast.AimX, _cast.AimY)) { CancelCast("aim failed"); return; }
                    _cast.CursorOwned = true;
                }
                else
                {
                    _cast.SavedCursorX = Hud.Window.CursorX;
                    _cast.SavedCursorY = Hud.Window.CursorY;
                    _cast.CursorOwned = false;
                }
                _cast.Stage = CastStage.Aim;
                _cast.DueTick = unchecked(now + _cast.AimSettleMs);
                if (_cast.RequiresStrafePause) RequestDhStrafePause(GetPreInputHardLimitMs(_cast.Kind) + 80);
                return;
            }

            if (_cast.Stage == CastStage.Aim)
            {
                if (!Reached(now, _cast.DueTick)) return;
                CaptureUserCursorIntent();

                // The preview cursor is allowed to exist for one bounded frame only. If the
                // readiness condition is lost during that frame, hand the user's cursor
                // back immediately and return to the pre-input wait. Never keep synthetic aim
                // pinned while waiting for another usable movement frame.
                AcdAnimationState preInputAnimation = Hud.Game.Me.AnimationState;
                if (RequiresMovementSettleBeforeInput() && !MovementSettledForCast(preInputAnimation))
                {
                    RestoreCursorPreviewForRetry(now, true);
                    return;
                }

                // Keep the exact aim that was previewed for this frame. Reprojecting to a new
                // point here would recreate a move+skill-without-preview failure mode.
                // Reassert the exact previewed absolute aim + skill-down as one ordered
                // SendInput batch. No other injected input can be interleaved between the
                // previewed aim and the support-skill press.
                if (_cast.Skill == null || !SkillReady(_cast.Skill)) { CancelCast("skill unavailable"); return; }
                if (ActionIsDown(_cast.Skill.Key)) { CancelCast("player skill input"); return; }
                _cast.PreInputAnimationSno = Hud.Game.Me.Animation;
                _cast.PreInputAnimationSnoValid = true;
                _cast.ActionHeld = _cast.UseCurrentCursorAim
                    ? ActionDownAtSafeCurrentCursor(_cast.Skill.Key)
                    : SetCastCursorAndActionDown(_cast.AimX, _cast.AimY, _cast.Skill.Key);
                if (!_cast.ActionHeld)
                {
                    if (Elapsed(_cast.StartedTick, now) + Math.Max(1, AimCorrectionRetryMs)
                        < GetPreInputHardLimitMs(_cast.Kind))
                    {
                        RestoreCursorPreviewForRetry(now, false);
                        return;
                    }
                    CancelCast("aim/input batch failed");
                    return;
                }
                _cast.InputSent = true;
                _cast.InputDownTick = now;
                if (_cast.Kind == CastKind.Multishot) MarkMultishotAttemptTargets(now);
                _cast.Stage = CastStage.Hold;
                _cast.DueTick = unchecked(now + _cast.HoldMs);
                return;
            }

            if (_cast.Stage == CastStage.Hold)
            {
                if (!Reached(now, _cast.DueTick)) return;
                ReleaseActionInput();
                MarkOpeningInputAttempted(_cast.Kind);
                RememberGroundCastInput(now);
                BeginPostInputCursorSettle(now);
                return;
            }

            if (_cast.Stage == CastStage.PostInputSettle)
            {
                CaptureUserCursorIntent();
                if (CanHandBackCursorAfterAcceptedInput())
                {
                    BeginCursorRestore(now);
                    return;
                }
                if (!Reached(now, _cast.DueTick))
                {
                    if (_cast.RequiresStrafePause)
                        RequestDhStrafePause(Math.Max(80, unchecked(_cast.DueTick - now) + 120));
                    return;
                }
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
                BeginVerificationAfterRestore(now);
                return;
            }

            if (_cast.Stage == CastStage.Verify)
            {
                if (CastActivationAccepted())
                {
                    if (_cast.Kind == CastKind.Multishot) CompleteMultishotDispatch(now, true);
                    else FinishCast("verified", now);
                    return;
                }
                if (Reached(now, _cast.VerifyUntilTick))
                {
                    if (_cast.Kind == CastKind.Multishot) CompleteMultishotDispatch(now, false);
                    else FinishCast("unverified", now);
                }
            }
        }

        private bool CastActivationAccepted()
        {
            IMonster target = FindMonster(_cast.TargetAcd);
            _cast.LastAppliedCount = 0;

            if (_cast.Kind == CastKind.Entangle)
            {
                bool debuffApplied = !_cast.BaselineTargetFlag && target != null && HasEntangle(target);
                if (debuffApplied || _cast.SawCastAnimation)
                {
                    _cast.LastAppliedCount = 1;
                    return true;
                }
                return false;
            }

            if (_cast.Kind == CastKind.Multishot)
            {
                bool debuffApplied = _cast.VerifyTargetAcds.Any(acd =>
                {
                    IMonster planned = FindMonster(acd);
                    return planned != null && HasIceblink(planned)
                        && !_cast.MultishotBaselineActiveAcds.Contains(acd);
                });
                if (_cast.SawNativeMultishotAnimation || debuffApplied)
                {
                    return true;
                }
                return false;
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
                _cast.LastAppliedCount = _cast.VerifyImportantAcds.Count > 0
                    ? importantApplied : markedAppliedCount;

                if (HasNewMfdActorGeneration())
                {
                    return true;
                }
                if (_cast.LastAppliedCount > _cast.BaselineImportantApplied)
                {
                    return true;
                }
                if (_cast.SawNativeMfdAnimation)
                return false;
            }

            if (_cast.Kind == CastKind.Sentry)
            {
                int currentCharges = _cast.Skill == null ? -1 : _cast.Skill.Charges;
                int currentOwned = GetOnScreenOwnedSentries().Count;
                IActor spawned = FindNewNativeOwnedSentryActor();

                // Prefer actor evidence whenever available. The native landing position is
                // required for corridor/wall relocation backoff and is more informative than
                // a charge drop by itself.
                if (spawned != null)
                {
                    if (spawned.FloorCoordinate != null
                        && !float.IsNaN(_cast.ExpectedWorldX) && !float.IsNaN(_cast.ExpectedWorldY))
                    {
                        float spawnError = Distance2D(spawned.FloorCoordinate.X, spawned.FloorCoordinate.Y,
                            _cast.ExpectedWorldX, _cast.ExpectedWorldY);
                        float relocationTolerance = _cast.SentryRequiredMatchRadius > 0f
                            ? _cast.SentryRequiredMatchRadius
                            : Math.Max(6f, SentryRejectedPositionRadius);
                        _cast.SentryRelocated = spawnError > relocationTolerance;
                        _cast.SentryActualWorldX = spawned.FloorCoordinate.X;
                        _cast.SentryActualWorldY = spawned.FloorCoordinate.Y;
                    }
                    _cast.LastAppliedCount = 1;
                    return true;
                }

                if (_cast.BaselineCharges >= 0 && currentCharges >= 0 && currentCharges < _cast.BaselineCharges)
                {
                    _cast.LastAppliedCount = 1;
                    return true;
                }
                if (currentOwned > _cast.BaselineOwnedSentries)
                {
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
            bool manualDebuffCast = _cast.ManualDebuff;
            CastKind finishedKind = _cast.Kind;
            ReleaseActionInput();
            if (!sentryBurstChild)
            {
                ReleaseStandstillInput();
                ReleaseDhStrafePause();
            }
            if (finishedKind == CastKind.MarkedForDeath)
            {
                bool mfdAccepted = string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase);
                if (mfdAccepted)
                {
                    _lastMfdCastTick = now;
                    _lastUnverifiedMfdTick = int.MinValue;
                    _hardMfdFailureStreak = 0;
                    ClearMfdRetryDebt();
                }
                else if (_cast.InputSent)
                {
                    _lastUnverifiedMfdTick = now;
                    _hardMfdFailureStreak = Math.Min(99, _hardMfdFailureStreak + 1);
                    ArmMfdRetryDebt(now);
                }
                _mfdUnavailableSinceTick = int.MinValue;
            }

            if (finishedKind == CastKind.Sentry)
            {
                bool sentryAccepted = string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase);
                bool actorObserved = !float.IsNaN(_cast.SentryActualWorldX)
                    && !float.IsNaN(_cast.SentryActualWorldY);
                if (sentryAccepted)
                {
                    _lastSentryCastTick = now;
                    _sentryPlacedThisEngagement = _wasSentryEngagementActive;
                    _sentryFairnessMultishotTurns = 0;
                    if (_cast.SentryCoverageAcds.Count > 0)
                        RecordEliteSentryCoverageAttempt(_cast.SentryCoverageAcds, now);
                    ClearSentryRetry();

                    // Placement/relocation is effect state, not activation state. Always
                    // inspect the observed native landing so repeated wall/corridor sinks and
                    // local stacking can trip the existing relocation backoff.
                    if (actorObserved)
                    {
                        if (_cast.SentryRelocated)
                            MarkRejectedSentryPosition(now, "native relocated");

                        bool constrainedPlacement = RecordSentryRelocation(now);
                        if (!constrainedPlacement && !_cast.SentryRelocated)
                        {
                            ClearRejectedSentryPositionNear(_cast.ExpectedWorldX, _cast.ExpectedWorldY);
                            ClearSentryRelocationState();
                        }
                    }
                }
                else if (_cast.InputSent)
                {
                    if (_cast.SentryCoverageAcds.Count > 0)
                        RecordEliteSentryCoverageAttempt(_cast.SentryCoverageAcds, now);

                    // A no-effect ground cast may be input rejection or invalid terrain. Reject
                    // this exact point briefly so the next attempt uses the existing occupied-
                    // ground fallback instead of repeating blind triangle geometry.
                    MarkRejectedSentryPosition(now, "activation unconfirmed");
                    SetSentryRetry(now, SentryFailedRetryMs, "activation unconfirmed");
                }
                PublishRejectedSentryPositions(now);
            }

            if (finishedKind == CastKind.Multishot
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

            if (manualDebuffCast)
                _lastManualDebuffCastFinishedTick = now;
            else
                _lastSupportKind = finishedKind;
            if (sentryBurstChild)
            {
                bool verified = string.Equals(result, "verified", StringComparison.OrdinalIgnoreCase);
                ResetCast();
                OnSentryBurstChildFinished(verified, now);
                CompleteBossEntangleStandstillRelease();
                return;
            }
            if (!manualDebuffCast)
                _lastCastFinishedTick = now;
            ResetCast();
            CompleteBossEntangleStandstillRelease();
        }

        private static bool IsLifecycleCancellation(string reason)
        {
            return string.Equals(reason, "context", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "new area", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "ghosted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "strafe off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "boss dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "manual hold released", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "interaction", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "portal escape", StringComparison.OrdinalIgnoreCase);
        }

        private void CancelCast(string reason)
        {
            if (_cast.Stage == CastStage.Idle) return;
            int now = Environment.TickCount;

            ReleaseActionInput();

            // Death, area/context loss, and macro shutdown are lifecycle exits: restore once
            // if possible and release immediately. Ordinary cast/retry failures keep ownership
            // through the same Restore -> RestoreSettle contract as successful casts.
            bool deferredRestore = !IsLifecycleCancellation(reason)
                && !_cast.CancellationPending
                && _cast.CursorOwned
                && Hud != null && Hud.Window != null && Hud.Window.IsForeground;
            if (deferredRestore)
            {
                _cast.CancellationPending = true;
                _cast.CancellationReason = reason ?? string.Empty;
                if (_cast.InputSent) BeginPostInputCursorSettle(now);
                else BeginCursorRestore(now);
                return;
            }

            bool restored = RestoreCursorImmediately();
            FinalizeCancelledCast(reason, now, restored);
        }

        private void FinalizeCancelledCast(string reason, int now, bool restored)
        {
            bool sentryBurstChild = _cast.SentryBurstChild
                && _sentryBurst.Mode != SentryBurstMode.None;
            bool manualDebuffCast = _cast.ManualDebuff;
            CastKind cancelledKind = _cast.Kind;
            bool inputSent = _cast.InputSent;

            ReleaseActionInput();
            if (!sentryBurstChild) ReleaseStandstillInput();
            if (restored && !sentryBurstChild)
            {
                if (!manualDebuffCast)
                    _lastPauseReleasedTick = now;
                if (inputSent && !manualDebuffCast)
                    s7o_DHStrafePrimaryPlugin.NotifySupportActionCompletedForZdh(now);
                int primaryQuietMs = inputSent && !manualDebuffCast && _cast.RequiresStrafePause
                    ? GetPostCastPrimaryQuietMs(cancelledKind) : 0;
                if (primaryQuietMs > 0) SuppressDhStrafePrimary(primaryQuietMs);
                ReleaseDhStrafePause();
            }
            else if (!restored && !sentryBurstChild)
                RequestDhStrafePause(Math.Max(120, CursorSafetyRecoveryMs + 120));

            if (!IsLifecycleCancellation(reason)
                && (cancelledKind == CastKind.Multishot || cancelledKind == CastKind.MarkedForDeath))
            {
                if (cancelledKind == CastKind.Multishot)
                {
                    _urgentRetryKind = CastKind.Multishot;
                    _urgentRetryTick = now;
                }
                if (cancelledKind == CastKind.MarkedForDeath && inputSent)
                {
                    _lastUnverifiedMfdTick = now;
                    _hardMfdFailureStreak = Math.Min(99, _hardMfdFailureStreak + 1);
                    ArmMfdRetryDebt(now);
                }
            }
            else if (cancelledKind == CastKind.Sentry
                && !string.Equals(reason, "context", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "new area", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "strafe off", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "interaction", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reason, "portal escape", StringComparison.OrdinalIgnoreCase))
            {
                if (inputSent && _cast.SentryCoverageAcds.Count > 0)
                    RecordEliteSentryCoverageAttempt(_cast.SentryCoverageAcds, now);
                if (inputSent)
                    MarkRejectedSentryPosition(now, reason);
                SetSentryRetry(now, SentryFailedRetryMs, reason);
            }

            if (manualDebuffCast)
                _lastManualDebuffCastFinishedTick = now;
            else
                _lastSupportKind = cancelledKind;
            if (sentryBurstChild)
            {
                ResetCast();
                EndSentryBurst("child cancelled: " + reason, now);
                CompleteBossEntangleStandstillRelease();
                return;
            }

            if (!manualDebuffCast)
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
            _cast.StartedTick = int.MinValue;
            _cast.DueTick = int.MinValue;
            _cast.VerifyUntilTick = int.MinValue;
            _cast.SavedCursorX = 0;
            _cast.SavedCursorY = 0;
            _cast.AimX = 0;
            _cast.AimY = 0;
            _cast.ActionHeld = false;
            _cast.StandstillHeld = false;
            _cast.CursorOwned = false;
            _cast.CursorReferenceX = 0;
            _cast.CursorReferenceY = 0;
            _cast.CursorReferenceValid = false;
            _cast.UserCursorDeltaX = 0;
            _cast.UserCursorDeltaY = 0;
            _cast.CursorSyntheticWritePending = false;
            _cast.CursorSyntheticFromX = 0;
            _cast.CursorSyntheticFromY = 0;
            _cast.CursorSyntheticTargetX = 0;
            _cast.CursorSyntheticTargetY = 0;
            _cast.CursorSyntheticEchoRejectCount = 0;
            _cast.RestoreX = 0;
            _cast.RestoreY = 0;
            _cast.RestoreWriteSent = false;
            _cast.RestoreRescueAttempted = false;
            _cast.BaselineTargetFlag = false;
            _cast.SawCastAnimation = false;
            _cast.SawNativeMultishotAnimation = false;
            _cast.SawNativeMfdAnimation = false;
            _cast.PreInputAnimationSno = default(AnimSnoEnum);
            _cast.PreInputAnimationSnoValid = false;
            _cast.TrashInitialMultishot = false;
            _cast.InputSent = false;
            _cast.CancellationPending = false;
            _cast.CancellationReason = string.Empty;
            _cast.RequiresStrafePause = false;
            _cast.BaselineCharges = -1;
            _cast.BaselineOwnedSentries = 0;
            _cast.BaselineImportantApplied = 0;
            _cast.BaselineMfdActorAcd = 0;
            _cast.BaselineMfdActorCreatedTick = 0;
            _cast.BaselineMfdGameTick = 0;
            _cast.BaselineActorAcds.Clear();
            _cast.VerifyTargetAcds.Clear();
            _cast.VerifyImportantAcds.Clear();
            _cast.MultishotEligibleAcds.Clear();
            _cast.MultishotDueAcds.Clear();
            _cast.MultishotPlanningAcds.Clear();
            _cast.MultishotCoveredEliteAcds.Clear();
            _cast.MultishotBaselineActiveAcds.Clear();
            _cast.SentryCoverageAcds.Clear();
            _cast.ExpectedWorldX = float.NaN;
            _cast.ExpectedWorldY = float.NaN;
            _cast.ExpectedWorldZ = float.NaN;
            _cast.MultishotDirectionX = 0;
            _cast.MultishotDirectionY = 0;
            _cast.HasMultishotDirection = false;
            _cast.MultishotDirectCore = false;
            _cast.MultishotMinimumBodyCoverage = 0;
            _cast.PauseAckTick = int.MinValue;
            _cast.InputDownTick = int.MinValue;
            _cast.AimSettleMs = 0;
            _cast.HoldMs = 0;
            _cast.MinimumLeaseMs = 0;
            _cast.VerifyMs = 0;
            _cast.LastAppliedCount = 0;
            _cast.SentrySlot = 0;
            _cast.SentryRelocated = false;
            _cast.SentryRequiredMatchRadius = 0f;
            _cast.SentryActualWorldX = float.NaN;
            _cast.SentryActualWorldY = float.NaN;
            _cast.SentryBurstChild = false;
            _cast.ManualDebuff = false;
            _cast.UseCurrentCursorAim = false;
        }

        private void ReleaseActionInput()
        {
            if (_cast.ActionHeld && _cast.Skill != null)
            {
                ActionUp(_cast.Skill.Key);
            }
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
                        FirstSupportTick = monster.IsOnScreen && Hud.Game.Me != null
                            && Distance(Hud.Game.Me, monster) <= Math.Max(10f, Math.Min(AutomationRange, EliteEncounterRange))
                            ? now : int.MinValue,
                        LastSeenTick = now,
                        IceblinkActive = iceblink,
                        IceblinkMissingSinceTick = iceblink ? int.MinValue : now,
                        IceblinkConfirmedTick = iceblink ? unchecked(now - firstObservedAge) : int.MinValue,
                    };
                    _targets[monster.AcdId] = state;
                }
                else if (iceblink && !state.IceblinkActive)
                {
                    state.IceblinkMissingSinceTick = int.MinValue;
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
                    if (state.IceblinkMissingSinceTick == int.MinValue)
                        state.IceblinkMissingSinceTick = now;
                    state.PendingIceblinkRefreshTick = int.MinValue;
                    state.PendingIceblinkAttemptCount = 0;
                }
                if (state.FirstSupportTick == int.MinValue && monster.IsOnScreen && Hud.Game.Me != null
                    && Distance(Hud.Game.Me, monster) <= Math.Max(10f, Math.Min(AutomationRange, EliteEncounterRange)))
                    state.FirstSupportTick = now;
                if (iceblink)
                {
                    state.IceblinkMissingSinceTick = int.MinValue;
                    state.ConsecutiveMultishotMisses = 0;
                }

                double damageThreshold = Math.Max(1.0, monster.MaxHealth * 0.000001);
                if (monster.CurHealth + damageThreshold < state.Health)
                {
                    state.LastDamageTick = now;
                    state.Health = monster.CurHealth;
                }
                else if (monster.CurHealth > state.Health)
                {
                    state.Health = monster.CurHealth;
                }
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

            ZdhLoadout zdh = GetTrackedUptimeZdh();
            if (zdh == null || zdh.Player == null || !zdh.Player.InCombat || zdh.Player.FloorCoordinate == null) return;

            List<IMonster> eligible = Hud.Game.AliveMonsters.Where(monster =>
                    IsStatusTarget(monster) && !IsJuggernaut(monster) && !monster.Invulnerable && monster.Attackable
                    && Distance(zdh.Player, monster) <= ZdhParticipationRange
                    && IsUptimeEligible(monster, zdh, now))
                .ToList();

            if (eligible.Count > 0)
            {
                s7o_ZDH_HelperMetrics.MarkedForDeathPresenceEligibleMilliseconds += sampleMs;
                if (eligible.Any(monster => monster.MarkedForDeath))
                    s7o_ZDH_HelperMetrics.MarkedForDeathPresenceMilliseconds += sampleMs;
            }

            foreach (IMonster monster in eligible)
            {
                s7o_ZDH_HelperMetrics.EligibleMilliseconds += sampleMs;
                if (HasIceblink(monster)) s7o_ZDH_HelperMetrics.IceblinkMilliseconds += sampleMs;
                if (HasEntangle(monster)) s7o_ZDH_HelperMetrics.DamageMilliseconds += sampleMs;

                // Measure Valley efficiency per eligible elite instead of treating one marked
                // elite as full success for the whole sample. Juggernauts remain excluded by
                // the existing eligibility filter above and therefore do not penalize uptime.
                s7o_ZDH_HelperMetrics.MarkedForDeathEligibleMilliseconds += sampleMs;
                if (monster.MarkedForDeath)
                    s7o_ZDH_HelperMetrics.MarkedForDeathMilliseconds += sampleMs;
            }
        }

        private ZdhLoadout GetTrackedUptimeZdh()
        {
            IEnumerable<IPlayer> players = Hud.Game.Players ?? Enumerable.Empty<IPlayer>();
            if (_hasTrackedUptimeHero)
            {
                IPlayer tracked = players.FirstOrDefault(p => p != null && p.HeroId == _trackedUptimeHeroId);
                if (tracked == null)
                {
                    _hasTrackedUptimeHero = false;
                    _trackedUptimeHeroId = 0;
                    s7o_ZDH_HelperMetrics.ResetUptime();
                }
                else
                {
                    ZdhLoadout current = BuildLoadout(tracked);
                    return current != null && current.QualifiesForDisplay ? current : null;
                }
            }

            ZdhLoadout next = GetPartyZdhLoadouts().FirstOrDefault(x => x.QualifiesForDisplay);
            if (next == null || next.Player == null) return null;
            _trackedUptimeHeroId = next.Player.HeroId;
            _hasTrackedUptimeHero = true;
            s7o_ZDH_HelperMetrics.ResetUptime();
            return next;
        }

        private bool IsDisplayEligible(IMonster monster, ZdhLoadout zdh, int now)
        {
            if (monster == null || zdh == null || zdh.Player == null) return false;
            if (SamePlayer(zdh.Player, Hud.Game.Me))
            {
                return !IsLocalGhosted() && zdh.Player.InCombat && monster.IsOnScreen
                    && Distance(zdh.Player, monster) <= Math.Max(10f, ZdhParticipationRange);
            }
            return WasRecentlyDamaged(GetTargetState(monster, now), now, PrimaryEliteMaintenanceMs);
        }

        private bool IsUptimeEligible(IMonster monster, ZdhLoadout zdh, int now)
        {
            if (monster == null || zdh == null || zdh.Player == null) return false;

            // Local zDH tracking/display must not depend on scheduler mode, Speed-combat
            // classification, or monster health-delta telemetry. If the local zDH is in
            // combat and a valid elite is visibly within participation range, it is part
            // of the current support window. Remote zDH observation still requires
            // recent damage so passing party members do not inflate their uptime.
            if (SamePlayer(zdh.Player, Hud.Game.Me))
            {
                bool macroRunning = s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh;
                bool combatMode = macroRunning && s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh;
                bool supportWindow = !macroRunning || combatMode || _speedCombatEngaged;
                return supportWindow && zdh.Player.InCombat
                    && monster.IsOnScreen
                    && Distance(zdh.Player, monster) <= Math.Max(10f, ZdhParticipationRange);
            }

            return WasRecentlyDamaged(GetTargetState(monster, now), now, PrimaryEliteMaintenanceMs);
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
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsGroundSupportPrimaryElite(m) && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((_bossStandaloneActive && m.Rarity == ActorRarity.Boss)
                        || IsImmediateGroundSupportEncounter(m, zdh)
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
                        || (focus != null && SameMonster(focus, m))))
                .ToList();
        }

        private List<IMonster> GetActiveGroundSupportMfdOnlyTargets(IPlayer zdh, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null) return new List<IMonster>();
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsGroundSupportMfdOnlyTarget(m) && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((IsImmediateGroundSupportEncounter(m, zdh)
                            && (s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh
                                || _speedCombatEngaged || _bossStandaloneActive))
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
                        || (focus != null && SameMonster(focus, m))))
                .ToList();
        }

        private List<IMonster> GetActivePrimaryElites(IPlayer zdh, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null) return new List<IMonster>();
            IMonster focus = GetPartyFocusMonster(now);
            return Hud.Game.AliveMonsters.Where(m => IsStatusTarget(m) && !IsJuggernaut(m)
                    && !m.Invulnerable && m.Attackable && m.IsOnScreen
                    && Distance(zdh, m) <= AutomationRange
                    && ((_bossStandaloneActive && m.Rarity == ActorRarity.Boss)
                        || IsImmediatePrimaryEliteEncounter(m, zdh)
                        || WasRecentlyDamaged(GetTargetState(m, now), now, PrimaryEliteMaintenanceMs)
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
                    || (focus != null && SameMonster(focus, m)))
                .ToList();

            // Combat mode itself is explicit fight intent; Speed mode needs a passive local
            // cluster so its 1.5s dwell detector can decide whether the player actually stopped.
            // Never let this fallback outrank a legitimate normal elite encounter.
            if (anchors.Count == 0 && !valid.Any(IsGroundSupportPrimaryElite))
            {
                IMonster trashAnchor = FindPassiveTrashAnchor(valid, zdh);
                if (trashAnchor != null) anchors.Add(trashAnchor);
            }
            if (anchors.Count == 0) return new List<IMonster>();

            return valid.Where(m => anchors.Any(a => SameMonster(a, m)
                    || a.FloorCoordinate.XYDistanceTo(m.FloorCoordinate) <= CombatBodyNearAnchorRadius + GetMonsterRadiusBottom(m)))
                .ToList();
        }

        private IMonster FindPassiveTrashAnchor(IEnumerable<IMonster> valid, IPlayer zdh)
        {
            if (valid == null || zdh == null) return null;
            return valid.Where(m => m != null && IsDebuffBody(m)
                    && !IsGroundSupportElite(m) && m.IsOnScreen)
                .OrderBy(m => Distance(zdh, m))
                .FirstOrDefault();
        }

        private bool IsPassiveTrashCandidate(CombatCluster cluster)
        {
            return cluster != null
                && cluster.Elites.Count == 0
                && cluster.MfdOnlyTargets.Count == 0
                && cluster.Bodies.Any(monster => monster != null && IsDebuffBody(monster)
                    && !IsGroundSupportElite(monster) && monster.IsOnScreen);
        }

        private bool IsCombatIntentTrash(CombatCluster cluster)
        {
            return IsPassiveTrashCandidate(cluster)
                && (s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh || _speedCombatEngaged);
        }

        private CombatCluster BuildBestCombatCluster(List<IMonster> bodies, int now)
        {
            if (bodies == null || bodies.Count == 0) return null;
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
                bool anchorFocused = focus != null && SameMonster(focus, anchor);
                bool anchorPassiveTrash = IsDebuffBody(anchor) && !IsGroundSupportElite(anchor)
                    && !bodies.Any(IsGroundSupportPrimaryElite);
                if (!anchorEngaged && !anchorEncountered && !anchorGroundSupport
                    && !anchorFocused && !anchorPassiveTrash) continue;

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
                    || (focus != null && SameMonster(focus, m)));
                bool engagedElite = cluster.PriorityEliteCount > 0;
                bool densityFight = cluster.Bodies.Count >= TrashClusterMinBodies
                    && cluster.RecentDamageCount >= TrashClusterMinDamagedBodies;
                bool passiveTrashCandidate = IsPassiveTrashCandidate(cluster);
                bool combatIntentTrash = IsCombatIntentTrash(cluster);
                bool mfdOnlyFight = cluster.MfdOnlyTargets.Count > 0;
                if (!engagedElite && !densityFight && !passiveTrashCandidate
                    && !combatIntentTrash && !mfdOnlyFight) continue;

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
                state = new TargetState
                {
                    Health = monster.CurHealth,
                    FirstSupportTick = monster.IsOnScreen && Hud.Game.Me != null
                        && Distance(Hud.Game.Me, monster) <= Math.Max(10f, Math.Min(AutomationRange, EliteEncounterRange))
                        ? now : int.MinValue,
                    LastSeenTick = now
                };
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

        private void DrawDebuffTokens(IMonster monster, bool ib, bool dmg, bool mfd, bool electrified)
        {
            IScreenCoordinate sc = null;
            try { sc = monster.FloorCoordinate == null ? monster.ScreenCoordinate : monster.FloorCoordinate.ToScreenCoordinate(false, true); }
            catch { sc = monster.ScreenCoordinate; }
            if (sc == null) return;

            int labelCount = electrified ? 4 : 3;
            string[] texts = electrified ? new[] { "⚡", "IB", "DMG", "MFD" } : new[] { "IB", "DMG", "MFD" };
            bool[] states = electrified ? new[] { true, ib, dmg, mfd } : new[] { ib, dmg, mfd };
            float[] widths = new float[labelCount];
            float total = 0;
            for (int i = 0; i < labelCount; i++)
            {
                IFont font = electrified && i == 0 ? _purpleFont : states[i] ? _greenFont : _redFont;
                widths[i] = font.GetTextLayout(texts[i]).Metrics.Width;
                total += widths[i];
            }
            total += (labelCount - 1) * 6f;
            float x = sc.X - total * 0.5f;
            float y = sc.Y + 33f;
            for (int i = 0; i < labelCount; i++)
            {
                IFont font = electrified && i == 0 ? _purpleFont : states[i] ? _greenFont : _redFont;
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
            int uptimeAverage = s7o_ZDH_HelperMetrics.UptimeAveragePercent();

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
            string[] valueTexts =
            {
                iceblink.ToString(CultureInfo.InvariantCulture) + "%",
                odyssey.ToString(CultureInfo.InvariantCulture) + "%",
                mark.ToString(CultureInfo.InvariantCulture) + "%",
                average.ToString(CultureInfo.InvariantCulture) + "% ("
                    + uptimeAverage.ToString(CultureInfo.InvariantCulture) + "%)",
            };
            float[] labelWidths = labels.Select(label => _tooltipLabelFont.GetTextLayout(label).Metrics.Width).ToArray();
            float valueGap = 3f;
            float lineHeight = _tooltipLabelFont.GetTextLayout("Ag").Metrics.Height + 1f;
            float widestRow = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                IFont valueFont = TooltipUptimeFont(values[i]);
                widestRow = Math.Max(widestRow, labelWidths[i] + valueGap
                    + valueFont.GetTextLayout(valueTexts[i]).Metrics.Width);
            }

            float x = r.Right + 6f;
            if (x + widestRow > Hud.Window.Size.Width - 8f)
                x = Math.Max(8f, r.Left - widestRow - 6f);
            float y = r.Top;

            for (int i = 0; i < labels.Length; i++)
            {
                _tooltipLabelFont.DrawText(labels[i], x, y);
                TooltipUptimeFont(values[i]).DrawText(valueTexts[i],
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
            public int CoveredPlanningEliteCount;
            public int RequiredApplied;
            public bool PrimaryMustApply;
            public float DirectionX;
            public float DirectionY;
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

        private Placement FindBestPlacement(List<IMonster> targets, int now, bool preferBoss = false)
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
                // MFD planning can request strict boss-first ordering. Sentry field anchoring uses
                // the default density-first ordering, so this does not broaden boss bias elsewhere.
                .OrderByDescending(x => preferBoss && bossPresent ? x.CoveredBosses : 0)
                .ThenByDescending(x => x.CoveredElites)
                .ThenByDescending(x => !preferBoss && bossPresent ? x.CoveredBosses : 0)
                .ThenByDescending(x => x.CoversFocus)
                .ThenByDescending(MfdPlacementPriorityScore)
                .ThenByDescending(x => MfdCoverageMargin(x, targets))
                .ThenByDescending(x => x.CoveredBodies)
                .FirstOrDefault();
        }

        private double MfdCoverageMargin(Placement placement, IEnumerable<IMonster> targets)
        {
            if (placement == null || targets == null) return double.MinValue;
            double minimum = double.MaxValue;
            bool found = false;
            foreach (IMonster target in targets)
            {
                if (target == null || target.FloorCoordinate == null
                    || !IsGroundSupportPrimaryElite(target)
                    || !placement.CoveredEliteAcds.Contains(target.AcdId))
                    continue;
                double margin = ValleyRadius
                    - target.FloorCoordinate.XYDistanceTo(placement.WorldX, placement.WorldY);
                minimum = Math.Min(minimum, margin);
                found = true;
            }
            return found ? minimum : 0.0;
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
                .ThenByDescending(MfdPlacementPriorityScore)
                .ThenByDescending(x => x.CoveredBodies)
                .FirstOrDefault();
        }

        private double MfdPlacementPriorityScore(Placement placement)
        {
            if (placement == null) return double.MinValue;
            double score = placement.Score;
            IPlayer local = Hud == null || Hud.Game == null ? null : Hud.Game.Me;
            if (local == null || local.FloorCoordinate == null
                || float.IsNaN(placement.WorldX) || float.IsNaN(placement.WorldY))
                return score;

            double distance = DistanceToPoint(local, placement.WorldX, placement.WorldY);
            float range = Math.Max(10f, MfdNearPlayerPriorityRange);
            double falloffRange = range * 1.5;
            if (distance < falloffRange)
                score += Math.Max(0f, MfdNearPlayerPriorityBonus)
                    * (1.0 - distance / falloffRange);
            return score;
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

        private Placement CreateScoredPlacement(float x, float y, float z,
            IEnumerable<IMonster> targets, int now)
        {
            Placement placement = CreatePlacement(x, y, z);
            if (placement != null) ScorePlacement(placement, targets, now);
            return placement;
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
                    // Actor enumeration can briefly outlive/under-report the proxy. Native
                    // monster MFD state is still authoritative coverage evidence, so do not
                    // recast an otherwise-working field merely because the proxy disappeared.
                    var marked = (targets ?? new List<IMonster>())
                        .Where(m => m != null && m.MarkedForDeath).ToList();
                    if (marked.Count == 0) return null;
                    var stateOnly = new Placement
                    {
                        WorldX = float.NaN, WorldY = float.NaN, WorldZ = float.NaN
                    };
                    foreach (IMonster target in marked)
                    {
                        stateOnly.Score += MfdTargetWeight(target, now);
                        stateOnly.CoveredBodies++;
                        if (!IsGroundSupportElite(target)) continue;
                        stateOnly.CoveredElites++;
                        stateOnly.CoveredEliteAcds.Add(target.AcdId);
                        if (target.Rarity == ActorRarity.Boss) stateOnly.CoveredBosses++;
                    }
                    return stateOnly;
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

        private int GetExpectedSentryLifetimeMs(ZdhLoadout local)
        {
            return local != null && local.CustomEngineering ? 60000 : 30000;
        }

        private int GetOldestOwnedSentryAgeMs(List<IActor> sentries)
        {
            if (Hud == null || Hud.Game == null || sentries == null || sentries.Count == 0)
                return -1;

            IActor oldest = sentries.Where(a => a != null)
                .OrderBy(a => a.CreatedAtInGameTick)
                .FirstOrDefault();
            if (oldest == null) return -1;

            int ageTicks = unchecked(Hud.Game.CurrentGameTick - oldest.CreatedAtInGameTick);
            if (ageTicks < 0) return -1;
            return (int)Math.Round(ageTicks * 1000.0 / 60.0);
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
                List<Placement> emergency = BuildBonusCircleSentryPlacements(local.Player);
                emergency.AddRange(BuildDpsProtectionPlacements(
                    local.Player, fieldCluster, emergency, now, false));
                emergency.AddRange(BuildEliteSentryCoveragePlacements(
                    local, cluster, now, Math.Min(
                        Math.Max(0, EliteSentryCoverageMaxPlacements), emergencyDesiredCount)));
                return emergency.OrderByDescending(x => x.Priority).Take(emergencyDesiredCount).ToList();
            }
            bool primaryEliteField = fieldCluster != null && fieldCluster.Elites.Any(IsGroundSupportPrimaryElite);
            if (!primaryEliteField && !cluster.TrashLatched
                && (!cluster.Stable || Elapsed(_packCandidateTick, now) < SentryPackStableMs)) return result;

            int desiredCount = GetDesiredSentryCount(local);

            // Desired-field order is deliberate. Pre-spawn/multi-elite play keeps the
            // coverage planner + triangle core. Once Strafe is off on an attackable RG,
            // the actual focused boss owns exactly one direct Guardian placement; remaining
            // slots protect DPS/local positions and create useful spread around the fight.
            List<Placement> field;
            Placement bossFocusPlacement = BuildStandaloneBossSentryPlacement(cluster);
            if (bossFocusPlacement != null)
            {
                field = new List<Placement> { bossFocusPlacement };
            }
            else
            {
                field = BuildEliteSentryCoveragePlacements(
                    local, cluster, now, desiredCount, false);
                TryUseEliteCoverageTriangleCore(local, fieldCluster, field, desiredCount, now);
            }

            List<Placement> bonusCircles = BuildBonusCircleSentryPlacements(local.Player);
            MergeBonusCircleSentryProtection(field, bonusCircles, desiredCount);

            List<Placement> dps = BuildDpsProtectionPlacements(local.Player, fieldCluster, field, now, false);
            MergePlayerSentryProtection(field, dps, desiredCount);

            foreach (Placement spread in BuildSentryPattern(fieldCluster, desiredCount))
            {
                if (field.Count >= desiredCount) break;
                if (spread == null || field.Any(x => x != null
                    && Distance2D(x.WorldX, x.WorldY, spread.WorldX, spread.WorldY)
                        <= SentryPatternMatchRadius))
                    continue;
                field.Add(spread);
            }

            return field.Take(desiredCount).ToList();
        }

        private List<Placement> BuildBonusCircleSentryPlacements(IPlayer zdh)
        {
            var oculus = new List<Placement>();
            var damage = new List<Placement>();
            var cooldown = new List<Placement>();
            if (Hud == null || Hud.Game == null || zdh == null || Hud.Game.Actors == null) return oculus;

            List<IPlayer> dps = GetDpsPlayers(zdh, true)
                .Concat(GetSentryDpsPlayers(zdh).Where(player => player != null && !player.IsOnScreen))
                .GroupBy(player => player.AcdId)
                .Select(group => group.First())
                .ToList();
            List<IActor> sentries = GetOwnedSentries();
            float bonusCircleCoverageRadius = BonusCircleFullCoverageCenterRadius();

            foreach (IActor actor in Hud.Game.Actors)
            {
                if (!IsPotentialBonusCircleActor(actor)) continue;

                IWorldCoordinate coord = actor.FloorCoordinate != null && actor.FloorCoordinate.IsValid
                    ? actor.FloorCoordinate
                    : actor.CollisionCoordinate != null && actor.CollisionCoordinate.IsValid
                        ? actor.CollisionCoordinate : null;
                if (coord == null) continue;

                double circleDistance = DistanceToPoint(zdh, coord.X, coord.Y);
                bool alreadyFullyCovered = IsSentryNear(
                    sentries, coord.X, coord.Y, bonusCircleCoverageRadius);

                // A legal Sentry cannot get close enough to fully contain a farther circle.
                // Apply this regardless of visibility so a visible but impossible target cannot
                // keep requesting native casts that clamp short and remain permanently incomplete.
                if (!alreadyFullyCovered
                    && circleDistance > NativeSentryPlacementMaxRangeYards + bonusCircleCoverageRadius)
                    continue;

                // Newly admitted off-screen circles remain deliberately stricter: their requested
                // center itself must be inside native Sentry cast range.
                if (!actor.IsOnScreen && !alreadyFullyCovered
                    && circleDistance > NativeSentryPlacementMaxRangeYards)
                    continue;

                // Only relevant generic proxies reach visual-effect classification. This keeps
                // off-screen actor scanning bounded before the more expensive projection check.
                string kind = null;
                if (IsOculusCircleActor(actor)) kind = "Oculus";
                else if (IsDamageTriuneCircleActor(actor)) kind = "Triune Damage";
                else if (IsCooldownTriuneCircleActor(actor)) kind = "Triune CDR";
                if (kind == null) continue;

                if (!actor.IsOnScreen && !alreadyFullyCovered
                    && !IsProjectableEdgeSentryPoint(zdh, coord))
                    continue;

                Placement placement = CreatePlacement(coord.X, coord.Y, coord.Z);
                if (placement == null) continue;

                IPlayer occupant = dps.Where(player => player != null && player.FloorCoordinate != null
                        && DistanceToPoint(player, coord.X, coord.Y) <= Math.Max(4f, SentryBonusCircleRadius))
                    .OrderByDescending(player => PlayerDpsScore(player)).FirstOrDefault();
                bool occupiedByDps = occupant != null;
                placement.TargetAcd = occupiedByDps ? occupant.AcdId : actor.AcdId;
                placement.Priority = kind == "Oculus"
                    ? (occupiedByDps ? 330 : 245)
                    : kind == "Triune Damage" ? (occupiedByDps ? 315 : 220)
                    : (occupiedByDps ? 300 : 195);
                placement.Label = "Sentry Bonus Circle " + kind + (occupiedByDps ? " Urgent" : string.Empty);

                if (kind == "Oculus") oculus.Add(placement);
                else if (kind == "Triune Damage") damage.Add(placement);
                else cooldown.Add(placement);
            }

            // Keep already-covered higher-tier circles in the desired field so maintenance does
            // not immediately move their Sentry away. The next tier is admitted only after every
            // higher-tier circle is already covered by a live Sentry.
            bool uncoveredOculus = oculus.Any(x => !IsSentryNear(sentries, x.WorldX, x.WorldY, bonusCircleCoverageRadius));
            bool uncoveredDamage = damage.Any(x => !IsSentryNear(sentries, x.WorldX, x.WorldY, bonusCircleCoverageRadius));

            var chosen = new List<Placement>();
            chosen.AddRange(oculus);
            if (!uncoveredOculus)
            {
                chosen.AddRange(damage);
                if (!uncoveredDamage) chosen.AddRange(cooldown);
            }

            return chosen.OrderByDescending(x => x.Priority)
                .ThenBy(x => DistanceToPoint(zdh, x.WorldX, x.WorldY)).ToList();
        }

        private void MergeBonusCircleSentryProtection(List<Placement> field,
            IEnumerable<Placement> protections, int desiredCount)
        {
            if (field == null || protections == null || desiredCount <= 0) return;
            int maximumBonusSlots = Math.Max(0, desiredCount);
            int bonusSlots = field.Count(IsBonusCircleSentryPlacement);

            foreach (Placement protection in protections.Where(x => x != null)
                .OrderByDescending(x => x.Priority))
            {
                if (bonusSlots >= maximumBonusSlots) break;

                Placement overlapping = field.Where(x => x != null
                        && Distance2D(x.WorldX, x.WorldY, protection.WorldX, protection.WorldY)
                            <= SentryPatternMatchRadius)
                    .OrderBy(x => Distance2D(x.WorldX, x.WorldY, protection.WorldX, protection.WorldY))
                    .FirstOrDefault();
                if (overlapping != null)
                {
                    // Use the actual circle coordinate rather than merely relabeling a nearby
                    // elite/player placement. That keeps coverage accounting honest and ensures
                    // the Sentry is visibly centered on the bonus circle as intended.
                    bool wasBonus = IsBonusCircleSentryPlacement(overlapping);
                    int index = field.IndexOf(overlapping);
                    if (index >= 0) field[index] = protection;
                    if (!wasBonus) bonusSlots++;
                    continue;
                }

                if (field.Count >= desiredCount)
                {
                    Placement replacement = field.Where(x => x != null
                            && !IsBonusCircleSentryPlacement(x)
                            && !IsPlayerProtectionPlacement(x))
                        .OrderBy(x => x.Priority).FirstOrDefault();
                    if (replacement == null) continue;
                    field.Remove(replacement);
                }

                field.Add(protection);
                bonusSlots++;
            }
        }

        private bool IsPotentialBonusCircleActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return false;
            ActorSnoEnum sno = actor.SnoActor.Sno;
            return sno == ActorSnoEnum._generic_proxy
                || sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_purple
                || sno == ActorSnoEnum._p75_itempassive_unique_ring_017_dome_purple_red
                || sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_blue;
        }

        private bool IsOculusCircleActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null
                || actor.SnoActor.Sno != ActorSnoEnum._generic_proxy) return false;
            return ActorHasVisualEffect(actor, Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None,
                Hud.Sno.SnoPowers.OculusRing == null ? OculusRingSno : Hud.Sno.SnoPowers.OculusRing.Sno);
        }

        private bool IsDamageTriuneCircleActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return false;
            ActorSnoEnum sno = actor.SnoActor.Sno;
            if (sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_purple
                || sno == ActorSnoEnum._p75_itempassive_unique_ring_017_dome_purple_red)
                return true;
            return sno == ActorSnoEnum._generic_proxy
                && ActorHasVisualEffect(actor, Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, TriuneProxySno);
        }

        private bool IsCooldownTriuneCircleActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return false;
            ActorSnoEnum sno = actor.SnoActor.Sno;
            if (sno == ActorSnoEnum._p2_itempassive_unique_ring_017_dome_blue)
                return true;
            return sno == ActorSnoEnum._generic_proxy
                && ActorHasVisualEffect(actor, Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_None, TriuneProxySno);
        }

        private bool ActorHasVisualEffect(IActor actor, IAttribute attribute, uint powerSno)
        {
            if (actor == null || attribute == null || powerSno == 0) return false;
            try { return actor.GetAttributeValueAsInt(attribute, powerSno, 0) == 1; }
            catch { return false; }
        }

        private void MergePlayerSentryProtection(List<Placement> field,
            IEnumerable<Placement> protections, int desiredCount)
        {
            if (field == null || protections == null || desiredCount <= 0) return;
            int maximumProtectionSlots = Math.Min(2, Math.Max(1, desiredCount - 1));
            int protectionSlots = field.Count(IsPlayerProtectionPlacement);

            foreach (Placement protection in protections.Where(x => x != null)
                .OrderByDescending(x => x.Priority))
            {
                if (protectionSlots >= maximumProtectionSlots) break;
                if (field.Any(x => x != null
                    && Distance2D(x.WorldX, x.WorldY, protection.WorldX, protection.WorldY)
                        <= SentryPatternMatchRadius))
                    continue;

                if (field.Count >= desiredCount)
                {
                    Placement replacement = field.Where(x => x != null
                            && !IsPlayerProtectionPlacement(x)
                            && !IsBonusCircleSentryPlacement(x))
                        .OrderBy(x => x.Priority).FirstOrDefault();

                    // If every slot is already reserved for bonus circles, a genuinely higher
                    // priority far/low-health DPS protection may replace the lowest unoccupied
                    // circle. Occupied circles remain urgent and are never displaced here.
                    if (replacement == null)
                        replacement = field.Where(x => IsBonusCircleSentryPlacement(x)
                                && !IsUrgentBonusCircleSentryPlacement(x))
                            .OrderBy(x => x.Priority).FirstOrDefault();

                    if (replacement == null || protection.Priority <= replacement.Priority + 0.5)
                        continue;
                    field.Remove(replacement);
                }

                field.Add(protection);
                protectionSlots++;
            }
        }

        private CombatCluster BuildSentryFieldCluster(ZdhLoadout local, CombatCluster source, int now)
        {
            if (local == null || local.Player == null || source == null) return source;

            if (_bossStandaloneActive && source.FocusTarget != null)
            {
                IMonster boss = source.FocusTarget;
                if (boss.Rarity == ActorRarity.Boss && boss.IsAlive && boss.Attackable
                    && !boss.Invulnerable && boss.IsOnScreen && boss.FloorCoordinate != null)
                {
                    var bossField = new CombatCluster
                    {
                        CenterX = boss.FloorCoordinate.X,
                        CenterY = boss.FloorCoordinate.Y,
                        CenterZ = boss.FloorCoordinate.Z,
                        Stable = true,
                        FocusTarget = boss,
                        SustainedSpecialFocus = true,
                        PriorityEliteCount = 1,
                        RecentDamageCount = IsEngaged(GetTargetState(boss, now), now) ? 1 : 0,
                        Score = source.Score,
                        AxisX = source.AxisX,
                        AxisY = source.AxisY,
                    };
                    bossField.Bodies.Add(boss);
                    bossField.Elites.Add(boss);
                    _runtime.SentryAnchorX = bossField.CenterX;
                    _runtime.SentryAnchorY = bossField.CenterY;
                    return bossField;
                }
            }

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

        private Placement BuildStandaloneBossSentryPlacement(CombatCluster cluster)
        {
            if (!_bossStandaloneActive || cluster == null || cluster.FocusTarget == null)
                return null;

            IMonster boss = cluster.FocusTarget;
            if (boss.Rarity != ActorRarity.Boss || !boss.IsAlive || !boss.Attackable
                || boss.Invulnerable || !boss.IsOnScreen || boss.FloorCoordinate == null)
                return null;

            Placement placement = CreatePlacement(
                boss.FloorCoordinate.X, boss.FloorCoordinate.Y, boss.FloorCoordinate.Z);
            if (placement == null) return null;

            placement.TargetAcd = boss.AcdId;
            placement.Priority = 220;
            placement.Label = "Sentry Boss Coverage";
            placement.CoveredEliteAcds.Add(boss.AcdId);
            return placement;
        }

        private void TryUseEliteCoverageTriangleCore(ZdhLoadout local, CombatCluster fieldCluster,
            List<Placement> field, int desiredCount, int now)
        {
            if (local == null || local.Player == null || fieldCluster == null
                || field == null || field.Count == 0 || desiredCount < 3) return;

            List<IMonster> elites = MergeMonsters(
                    GetActiveGroundSupportPrimaryElites(local.Player, now),
                    GetActiveGroundSupportMfdOnlyTargets(local.Player, now))
                .Where(m => m != null && m.FloorCoordinate != null
                    && DistanceToPoint(m, _runtime.SentryAnchorX, _runtime.SentryAnchorY)
                        <= SentryFieldRelevanceRadius)
                .GroupBy(m => m.AcdId).Select(g => g.First()).ToList();
            if (elites.Count == 0) return;

            // The triangle is opening/pre-spawn or ordinary multi-elite coverage only.
            // In standalone RG mode the focused boss gets one direct Guardian placement,
            // regardless of boss-rarity adds reported around it.
            if (_bossStandaloneActive)
                return;

            var coveredByCurrent = new HashSet<uint>(field
                .Where(x => x != null)
                .SelectMany(x => x.CoveredEliteAcds));
            if (elites.Any(elite => !coveredByCurrent.Contains(elite.AcdId))) return;

            List<Placement> triangle = BuildSentryPattern(fieldCluster, 3)
                .Where(x => x != null && x.SentrySlot >= 1 && x.SentrySlot <= 3)
                .OrderBy(x => x.SentrySlot).ToList();
            if (triangle.Count != 3) return;

            float coverageRadius = Math.Max(6f, GuardianRadius - 1f);
            if (elites.Any(elite => !triangle.Any(placement =>
                    DistanceToPoint(elite, placement.WorldX, placement.WorldY) <= coverageRadius)))
                return;

            double priority = field.Max(x => x == null ? 0 : x.Priority);
            uint targetAcd = field.Where(x => x != null && x.TargetAcd != 0)
                .OrderByDescending(x => x.Priority).Select(x => x.TargetAcd).FirstOrDefault();
            foreach (Placement placement in triangle)
            {
                placement.Priority = Math.Max(placement.Priority, priority - (placement.SentrySlot - 1) * 0.01);
                placement.TargetAcd = targetAcd;
                placement.Label = "Sentry Field Elite Coverage Triangle";
                placement.CoveredEliteAcds.Clear();
                foreach (IMonster elite in elites)
                    if (DistanceToPoint(elite, placement.WorldX, placement.WorldY) <= GuardianRadius)
                        placement.CoveredEliteAcds.Add(elite.AcdId);
            }

            field.Clear();
            field.AddRange(triangle);
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
            Placement centerProbe = null;
            for (float shift = 0f; shift <= GuardianRadius && centerProbe == null; shift += 4f)
            {
                centerX = cluster.CenterX + forwardX * shift;
                centerY = cluster.CenterY + forwardY * shift;
                centerProbe = CreatePlacement(centerX, centerY, cluster.CenterZ);
            }
            for (float radius = 4f; radius <= GuardianRadius && centerProbe == null; radius += 4f)
            {
                for (int angle = 0; angle < 360 && centerProbe == null; angle += 30)
                {
                    double radians = angle * Math.PI / 180.0;
                    centerX = cluster.CenterX + (float)Math.Cos(radians) * radius;
                    centerY = cluster.CenterY + (float)Math.Sin(radians) * radius;
                    centerProbe = CreatePlacement(centerX, centerY, cluster.CenterZ);
                }
            }
            if (centerProbe == null) return result;

            float sideX = -forwardY;
            float sideY = forwardX;
            float spacing = Math.Max(SentryMinSeparation + 1f,
                Math.Min(28f, Math.Max(24f, SentryPatternColumnSpacing)));
            float half = spacing * 0.5f;
            float triangleRadius = spacing / (float)Math.Sqrt(3.0);
            float rear = -triangleRadius * 0.5f;

            // Core slots form an equilateral triangle centered on the combat anchor.
            // With the default 24 yd side and 16 yd Guardian radius, the center
            // remains inside all three bubbles while each Sentry stays well separated.
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                triangleRadius, 0f, 145, "Sentry Field Triangle", 1, false, string.Empty);
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                rear, -half, 140, "Sentry Field Triangle", 2, false, string.Empty);
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                rear, half, 139, "Sentry Field Triangle", 3, false, string.Empty);

            // Complete the five-Sentry field on the same triangular lattice.
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                triangleRadius, -spacing, 122, "Sentry Field Extension", 4, false, string.Empty);
            TryAddSentryPlacement(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY,
                triangleRadius, spacing, 121, "Sentry Field Extension", 5, false, string.Empty);

            FillVisibleSentryFallbacks(result, centerX, centerY, cluster.CenterZ,
                forwardX, forwardY, sideX, sideY, count, spacing);

            return result.OrderByDescending(x => x.Priority).Take(count).ToList();
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
            bool bestTrackedUrgent = false;
            int bestAge = 0;
            int bestDelay = 0;
            int bestAttempts = 0;

            foreach (IMonster elite in active)
            {
                TargetState state = GetTargetState(elite, now);
                state.SentryCoverageLastActiveTick = now;
                bool covered = IsSentryNear(sentries,
                    elite.FloorCoordinate.X, elite.FloorCoordinate.Y,
                    Math.Min(GuardianRadius, SentryEliteComfortRadius));
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
                bool isUrgent = isReady
                    && state.SentryCoverageAttempts < EliteSentryUrgentAttemptLimit;
                if (isReady) ready++;

                if (bestTracked == null
                    || (isReady && !bestTrackedReady)
                    || (isReady == bestTrackedReady && isUrgent && !bestTrackedUrgent)
                    || (isReady == bestTrackedReady && isUrgent == bestTrackedUrgent
                        && MfdTargetWeight(elite, now) > MfdTargetWeight(bestTracked, now)))
                {
                    bestTracked = elite;
                    bestTrackedReady = isReady;
                    bestTrackedUrgent = isUrgent;
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

            _runtime.EliteSentryUncoveredCount = uncovered;
            _runtime.EliteSentryReadyCount = ready;
            _runtime.EliteSentryPriorityAcd = bestTracked == null ? 0 : bestTracked.AcdId;
            _runtime.EliteSentryPriorityAgeMs = bestTracked == null ? -1 : bestAge;
            _runtime.EliteSentryPriorityDelayMs = bestTracked == null ? -1 : bestDelay;
            _runtime.EliteSentryPriorityAttempts = bestTracked == null ? 0 : bestAttempts;
            _runtime.EliteSentryUrgent = bestTracked != null && bestTrackedUrgent;
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
            ZdhLoadout local, CombatCluster cluster, int now, int maxCount, bool requireReady = true)
        {
            var result = new List<Placement>();
            if (local == null || local.Player == null || cluster == null || maxCount <= 0) return result;

            List<IMonster> elites = MergeMonsters(
                    GetActiveGroundSupportPrimaryElites(local.Player, now),
                    GetActiveGroundSupportMfdOnlyTargets(local.Player, now))
                .Where(m => m != null && m.FloorCoordinate != null
                    && (!requireReady || EliteSentryCoverageReady(m, now))
                    && DistanceToPoint(m, _runtime.SentryAnchorX, _runtime.SentryAnchorY)
                        <= SentryFieldRelevanceRadius)
                .OrderByDescending(m => MfdTargetWeight(m, now))
                .ToList();
            if (elites.Count == 0) return result;

            float comfort = Math.Min(GuardianRadius, SentryEliteComfortRadius);
            var candidates = new List<Placement>();
            Action<float, float, float> addCandidate = (x, y, z) =>
            {
                if (candidates.Any(candidate => candidate != null && Distance2D(candidate.WorldX, candidate.WorldY, x, y) < 1f)) return;
                Placement placement = CreatePlacement(x, y, z);
                if (placement != null) candidates.Add(placement);
            };

            foreach (IMonster elite in elites)
                addCandidate(elite.FloorCoordinate.X, elite.FloorCoordinate.Y, elite.FloorCoordinate.Z);

            for (int i = 0; i < elites.Count; i++)
            {
                for (int j = i + 1; j < elites.Count; j++)
                {
                    IMonster a = elites[i];
                    IMonster b = elites[j];
                    if (Distance2D(a.FloorCoordinate.X, a.FloorCoordinate.Y,
                            b.FloorCoordinate.X, b.FloorCoordinate.Y) > comfort * 2f)
                        continue;
                    addCandidate((a.FloorCoordinate.X + b.FloorCoordinate.X) * 0.5f,
                        (a.FloorCoordinate.Y + b.FloorCoordinate.Y) * 0.5f,
                        (a.FloorCoordinate.Z + b.FloorCoordinate.Z) * 0.5f);
                }
            }

            if (elites.Count >= 2)
            {
                addCandidate(elites.Average(m => m.FloorCoordinate.X),
                    elites.Average(m => m.FloorCoordinate.Y),
                    elites.Average(m => m.FloorCoordinate.Z));
            }

            var uncovered = new HashSet<uint>(elites.Select(m => m.AcdId));
            while (result.Count < maxCount && uncovered.Count > 0)
            {
                Placement best = null;
                List<IMonster> bestCovered = null;
                double bestScore = double.MinValue;
                foreach (Placement candidate in candidates)
                {
                    if (candidate == null) continue;
                    List<IMonster> covered = elites.Where(m => uncovered.Contains(m.AcdId)
                        && DistanceToPoint(m, candidate.WorldX, candidate.WorldY) <= comfort).ToList();
                    if (covered.Count == 0) continue;

                    double priority = covered.Sum(m => MfdTargetWeight(m, now));
                    double separation = result.Count == 0 ? SentryMinSeparation
                        : result.Min(p => Distance2D(p.WorldX, p.WorldY, candidate.WorldX, candidate.WorldY));
                    double score = covered.Count * 100000.0 + priority * 100.0
                        + Math.Min(SentryMinSeparation, separation);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                    bestCovered = covered;
                }

                if (best == null || bestCovered == null || bestCovered.Count == 0) break;
                IMonster primary = bestCovered.OrderByDescending(m => MfdTargetWeight(m, now)).First();
                best.TargetAcd = primary.AcdId;
                best.Priority = bestCovered.Any(m => m.Rarity == ActorRarity.Boss || IsCurrentPartyFocus(m, now))
                    ? 170 : 150;
                best.Label = "Sentry Field Elite Coverage";
                best.CoveredEliteAcds.Clear();
                foreach (IMonster elite in bestCovered)
                {
                    best.CoveredEliteAcds.Add(elite.AcdId);
                    uncovered.Remove(elite.AcdId);
                }
                result.Add(best);
                candidates.Remove(best);
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
            if (!lowHealthOnly && ShouldProtectLocalZdh(zdh, field, now))
            {
                Placement local = CreatePlacement(zdh.FloorCoordinate.X, zdh.FloorCoordinate.Y, zdh.FloorCoordinate.Z);
                if (local != null)
                {
                    local.TargetAcd = zdh.AcdId;
                    local.Priority = 265;
                    local.Label = "Sentry ZDH Emergency";
                    result.Add(local);
                }
            }

            bool bossField = cluster != null
                && cluster.Elites.Any(m => m != null && m.Rarity == ActorRarity.Boss);
            List<IPlayer> dps = GetSentryDpsPlayers(zdh)
                .OrderByDescending(player => IsLowHealth(player))
                .ThenByDescending(player => cluster == null ? 0
                    : DistanceToPoint(player, cluster.CenterX, cluster.CenterY))
                .Take(2).ToList();
            var needed = new List<IPlayer>();

            foreach (IPlayer player in dps)
            {
                bool low = IsLowHealth(player);
                double fieldDistance = cluster == null ? 0
                    : DistanceToPoint(player, cluster.CenterX, cluster.CenterY);
                bool farFromField = fieldDistance > SentryDpsPackRange;
                IActor covering = FindCoveringOwnedSentry(player);
                bool edgeProjectable = !player.IsOnScreen
                    && IsProjectableEdgeSentryPoint(zdh, player.FloorCoordinate);
                bool spatiallyEligible = player.IsOnScreen || covering != null || edgeProjectable;
                bool eligible = spatiallyEligible && (bossField || farFromField || player.InCombat);
                int stableMs = (bossField || farFromField)
                    ? Math.Min(SentryDpsStableMs, SentryDpsEmergencyStableMs)
                    : SentryDpsStableMs;
                bool stable = eligible && IsPlayerPositionStable(player, now, stableMs);
                bool emergencyStable = eligible
                    && IsPlayerPositionStable(player, now, SentryDpsEmergencyStableMs);
                if (low ? !emergencyStable : (lowHealthOnly || !stable)) continue;
                if (CoveredByPlacements(field, player)) continue;
                if (covering != null)
                {
                    if (!lowHealthOnly && (low ? emergencyStable : stable))
                    {
                        Placement retained = CreatePlacement(covering.FloorCoordinate.X, covering.FloorCoordinate.Y, covering.FloorCoordinate.Z);
                        if (retained != null)
                        {
                            retained.TargetAcd = player.AcdId;
                            retained.Priority = low ? 285 : farFromField ? 260 : 116;
                            retained.Label = low ? "Sentry DPS Emergency Retain"
                                : farFromField ? "Sentry DPS Far Retain" : "Sentry DPS Retain";
                            result.Add(retained);
                        }
                    }
                    continue;
                }

                needed.Add(player);
            }

            if (needed.Count >= 2 && PlayerDistance(needed[0], needed[1]) <= GuardianRadius * 2f - 2f)
            {
                float x = (needed[0].FloorCoordinate.X + needed[1].FloorCoordinate.X) * 0.5f;
                float y = (needed[0].FloorCoordinate.Y + needed[1].FloorCoordinate.Y) * 0.5f;
                float z = (needed[0].FloorCoordinate.Z + needed[1].FloorCoordinate.Z) * 0.5f;
                bool edgePair = (needed[0] != null && !needed[0].IsOnScreen)
                    || (needed[1] != null && !needed[1].IsOnScreen);
                Placement pair = edgePair
                    && DistanceToPoint(zdh, x, y) > NativeSentryPlacementMaxRangeYards
                    ? null : CreatePlacement(x, y, z);
                if (pair != null)
                {
                    pair.TargetAcd = needed[0].AcdId;
                    bool farPair = cluster != null && (
                        DistanceToPoint(needed[0], cluster.CenterX, cluster.CenterY) > SentryDpsPackRange
                        || DistanceToPoint(needed[1], cluster.CenterX, cluster.CenterY) > SentryDpsPackRange);
                    pair.Priority = IsLowHealth(needed[0]) || IsLowHealth(needed[1]) ? 290
                        : farPair ? 265 : bossField ? 162 : 118;
                    pair.Label = farPair ? "Sentry DPS Far Pair" : "Sentry DPS Pair";
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
                bool farFromField = cluster != null
                    && DistanceToPoint(player, cluster.CenterX, cluster.CenterY) > SentryDpsPackRange;
                placement.Priority = low ? 280 : farFromField ? 260 : bossField ? 160 : 115;
                placement.Label = low ? "Sentry DPS Emergency"
                    : farFromField ? "Sentry DPS Far" : "Sentry DPS";
                result.Add(placement);
            }
            return result;
        }

        private bool ShouldProtectLocalZdh(IPlayer zdh, List<Placement> field, int now)
        {
            if (zdh == null || zdh.FloorCoordinate == null
                || !s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh
                || (!zdh.InCombat && !s7o_DHStrafePrimaryPlugin.IsHighFrequencyModeForZdh))
                return false;
            float health = PlayerHealthPct(zdh);
            if (health <= 0f || health > LocalSentryProtectionHealthPct
                || _localTravelSpeed > LocalSentryProtectionMaxSpeed)
                return false;
            PlayerPositionState state;
            if (!_playerPositions.TryGetValue(zdh.AcdId, out state)
                || state.StableSinceTick == int.MinValue
                || Elapsed(state.StableSinceTick, now) < Math.Max(0, LocalSentryProtectionStationaryMs))
                return false;
            return !CoveredByPlacements(field, zdh) && FindCoveringOwnedSentry(zdh) == null;
        }

        private IActor FindCoveringOwnedSentry(IPlayer player)
        {
            if (player == null || player.FloorCoordinate == null) return null;
            return GetOwnedSentries()
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
                foreach (Placement placement in missing)
                {
                    if (IsRejectedSentryPlacement(placement, now))
                    {
                        Placement occupied = CreateOccupiedSentryFillFallback(
                            placement, sentries, now);
                        if (occupied != null) return occupied;

                        Placement fallback = CreateRejectedSentryFallback(placement, desired, sentries, now);
                        if (fallback != null) return fallback;
                        continue;
                    }

                    float nearest = NearestSentryDistance(sentries, placement.WorldX, placement.WorldY);
                    if (nearest < RequiredSentrySeparation(placement)) continue;

                    float nearestScreen = NearestSentryScreenDistance(sentries, placement);
                    if (RequiresSentryScreenSeparation(placement)
                        && nearestScreen < SentryScreenSeparationThreshold())
                    {
                        continue;
                    }

                    return placement;
                }
                Placement occupiedFallback = CreateOccupiedSentryFillFallback(
                    missing.Where(placement => placement != null)
                        .OrderByDescending(placement => placement.Priority).FirstOrDefault(),
                    sentries, now);
                if (occupiedFallback != null) return occupiedFallback;

                Placement openFallback = CreateOpenSentryFillFallback(missing, desired, sentries, now);
                if (openFallback != null) return openFallback;

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
            List<Placement> survivorMissing = GetUnmatchedDesiredSentryPlacements(desired, survivors);
            if (currentStackedPairs >= Math.Max(1, SentrySevereOverlapPairThreshold))
            {
                Placement occupiedStackRepair = CreateOccupiedSentryFillFallback(
                    survivorMissing.Where(placement => placement != null)
                        .OrderByDescending(placement => placement.Priority).FirstOrDefault(),
                    survivors, now);
                if (occupiedStackRepair != null) return occupiedStackRepair;
            }

            foreach (Placement placement in survivorMissing)
            {
                if (IsRejectedSentryPlacement(placement, now))
                {
                    Placement occupied = CreateOccupiedSentryFillFallback(
                        placement, survivors, now);
                    if (occupied != null) return occupied;

                    Placement fallback = CreateRejectedSentryFallback(placement, desired, survivors, now);
                    if (fallback != null) return fallback;
                    continue;
                }

                float nearest = NearestSentryDistance(survivors, placement.WorldX, placement.WorldY);
                if (nearest < RequiredSentrySeparation(placement))
                {
                    continue;
                }

                float nearestScreen = NearestSentryScreenDistance(survivors, placement);
                if (RequiresSentryScreenSeparation(placement)
                    && nearestScreen < SentryScreenSeparationThreshold())
                {
                    continue;
                }

                double futureScore = ScoreDesiredSentryMatches(desired, survivors) + placement.Priority;
                bool emergency = emergencyOnly || (!string.IsNullOrEmpty(placement.Label)
                    && placement.Label.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0);
                bool protectedPlacement = IsProtectedSentryPlacement(placement);
                bool stackCorrection = currentStackedPairs >= Math.Max(1, SentrySevereOverlapPairThreshold);
                if (emergency || protectedPlacement || stackCorrection
                    || futureScore > currentScore + 0.5)
                    return placement;
            }

            if (replaceWasIrrelevant
                || currentStackedPairs >= Math.Max(1, SentrySevereOverlapPairThreshold))
            {
                List<Placement> remaining = GetUnmatchedDesiredSentryPlacements(desired, survivors);
                Placement occupiedReplacement = CreateOccupiedSentryFillFallback(
                    remaining.Where(placement => placement != null)
                        .OrderByDescending(placement => placement.Priority).FirstOrDefault(),
                    survivors, now);
                if (occupiedReplacement != null) return occupiedReplacement;

                Placement replacementFallback = CreateOpenSentryFillFallback(
                    remaining, desired, survivors, now);
                if (replacementFallback != null) return replacementFallback;
            }

            return null;
        }

        private Placement FindSafePopulationFillPlacement(CombatCluster cluster,
            List<Placement> desired, List<IActor> sentries, int slot, int now)
        {
            if (cluster == null) return null;
            if ((desired == null || desired.Count == 0)
                && !cluster.Stable && !cluster.TrashLatched && !_bossStandaloneActive)
                return null;

            var searchDesired = desired == null
                ? new List<Placement>()
                : desired.Where(x => x != null).ToList();

            Placement center = searchDesired.FirstOrDefault(x => x.SentrySlot == 1);
            if (center == null)
            {
                center = CreatePlacement(cluster.CenterX, cluster.CenterY, cluster.CenterZ);
                if (center == null) return null;
                center.Label = "Sentry Field Center";
                center.SentrySlot = 1;
                center.Priority = 120;
                searchDesired.Insert(0, center);
            }

            Placement template = CreatePlacement(center.WorldX, center.WorldY, center.WorldZ);
            if (template == null) return null;
            template.Label = "Sentry Field Extension";
            template.SentrySlot = Math.Max(1, slot);
            template.Priority = 110;

            return CreateOpenSentryFillFallback(
                new List<Placement> { template }, searchDesired, sentries ?? new List<IActor>(), now);
        }

        private Placement FindRollingSentryRefreshPlacement(CombatCluster cluster,
            List<Placement> desired, List<IActor> sentries, int capacity, int now)
        {
            if (cluster == null || sentries == null || sentries.Count == 0 || capacity <= 0)
                return null;

            IActor oldest = sentries.Where(a => a != null)
                .OrderBy(a => a.CreatedAtInGameTick)
                .FirstOrDefault();
            if (oldest == null) return null;

            List<IActor> survivors = sentries
                .Where(a => a != null && a.AcdId != oldest.AcdId)
                .ToList();
            int geometryTarget = Math.Min(capacity, desired == null ? 0 : desired.Count);
            Placement replacement = geometryTarget > 0
                ? FindMissingDesiredSentryPlacement(desired, survivors, geometryTarget, false, now)
                : null;
            if (replacement == null)
            {
                replacement = FindSafePopulationFillPlacement(
                    cluster, desired, survivors, Math.Min(capacity, survivors.Count + 1), now);
            }
            return replacement;
        }

        private Placement CreateOccupiedSentryFillFallback(Placement template,
            List<IActor> sentries, int now)
        {
            if (template == null || Hud == null || Hud.Game == null) return null;
            // A monster/player fallback can protect a point target, but it cannot stand in for
            // a bonus circle unless it is explicitly validated inside that circle's containment
            // zone. Rejected bonus cores use the bounded geometric fallback below instead.
            if (IsBonusCircleSentryPlacement(template)) return null;

            sentries = sentries ?? new List<IActor>();
            IPlayer local = Hud.Game.Me;
            if (local == null || local.FloorCoordinate == null) return null;

            var candidates = new List<Placement>();
            float fieldRadius = Math.Max(10f, SentryFieldRelevanceRadius - 1f);

            Action<Placement, double> consider = (placementCandidate, actorPriority) =>
            {
                if (placementCandidate == null || IsRejectedSentryPlacement(placementCandidate, now)) return;
                if (Distance2D(placementCandidate.WorldX, placementCandidate.WorldY,
                        _runtime.SentryAnchorX, _runtime.SentryAnchorY) > fieldRadius)
                    return;

                float nearest = NearestSentryDistance(sentries, placementCandidate.WorldX, placementCandidate.WorldY);
                if (nearest < RequiredSentrySeparation(template)) return;
                if (RequiresSentryScreenSeparation(template)
                    && NearestSentryScreenDistance(sentries, placementCandidate) < SentryScreenSeparationThreshold())
                    return;

                double anchorDistance = Distance2D(placementCandidate.WorldX, placementCandidate.WorldY,
                    _runtime.SentryAnchorX, _runtime.SentryAnchorY);
                placementCandidate.Priority = actorPriority
                    + Math.Min(40f, nearest) * 8.0
                    - anchorDistance;
                candidates.Add(placementCandidate);
            };

            IEnumerable<IMonster> monsters = Hud.Game.AliveMonsters ?? Enumerable.Empty<IMonster>();
            List<IMonster> occupancyAnchors = monsters
                .Where(m => m != null && m.FloorCoordinate != null && m.IsOnScreen
                    && m.Attackable && !m.Invulnerable && Distance(local, m) <= AutomationRange
                    && Distance2D(m.FloorCoordinate.X, m.FloorCoordinate.Y,
                        _runtime.SentryAnchorX, _runtime.SentryAnchorY) <= fieldRadius)
                .OrderByDescending(m => IsGroundSupportPrimaryElite(m) ? 4
                    : IsGroundSupportMfdOnlyTarget(m) ? 3
                    : m.IsElite ? 2 : IsHighValueTrash(m) ? 1 : 0)
                .ThenByDescending(m => NearestSentryDistance(
                    sentries, m.FloorCoordinate.X, m.FloorCoordinate.Y))
                .Take(24)
                .ToList();
            foreach (IMonster monster in occupancyAnchors)
            {
                Placement monsterPlacement = CreatePlacement(
                    monster.FloorCoordinate.X, monster.FloorCoordinate.Y, monster.FloorCoordinate.Z);
                if (monsterPlacement == null) continue;

                monsterPlacement.TargetAcd = monster.AcdId;
                double monsterPriority = IsGroundSupportPrimaryElite(monster) ? 1000
                    : IsGroundSupportMfdOnlyTarget(monster) ? 700
                    : monster.IsElite ? 550
                    : IsHighValueTrash(monster) ? 350 : 220;
                consider(monsterPlacement, monsterPriority);
            }

            bool localProtectionTemplate = !string.IsNullOrEmpty(template.Label)
                && template.Label.StartsWith("Sentry ZDH", StringComparison.Ordinal);
            IEnumerable<IPlayer> players = Hud.Game.Players ?? Enumerable.Empty<IPlayer>();
            foreach (IPlayer player in players)
            {
                if (player == null || player.IsDead || player.FloorCoordinate == null) continue;
                if (player.AcdId == local.AcdId
                    && (!localProtectionTemplate || FindCoveringOwnedSentry(local) != null))
                    continue;
                Placement playerPlacement = CreatePlacement(
                    player.FloorCoordinate.X, player.FloorCoordinate.Y, player.FloorCoordinate.Z);
                if (playerPlacement == null) continue;

                playerPlacement.TargetAcd = player.AcdId;
                consider(playerPlacement, player.AcdId == local.AcdId ? 420 : 650);
            }

            Placement best = candidates.OrderByDescending(candidate => candidate.Priority).FirstOrDefault();
            if (best == null) return null;

            best.Priority = template.Priority - 0.05;
            best.Label = "Sentry Occupied Fallback";
            best.SentrySlot = template.SentrySlot;
            best.SentryFallback = true;
            best.SentryFallbackReason = "occupied actor ground";
            if (template.CoveredEliteAcds != null)
                foreach (uint acd in template.CoveredEliteAcds)
                    best.CoveredEliteAcds.Add(acd);
            return best;
        }

        private Placement CreateOpenSentryFillFallback(List<Placement> missing,
            List<Placement> desired, List<IActor> sentries, int now)
        {
            if (missing == null || missing.Count == 0 || desired == null || desired.Count == 0)
                return null;

            Placement center = desired.FirstOrDefault(x => x != null && x.SentrySlot == 1)
                ?? desired.FirstOrDefault(IsProtectedSentryPlacement)
                ?? desired.FirstOrDefault();
            Placement template = missing.Where(x => x != null && !IsBonusCircleSentryPlacement(x))
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
            bool bonusCirclePlacement = IsBonusCircleSentryPlacement(rejected);
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
                if (bonusCirclePlacement)
                {
                    // Exact core is preferred. If terrain rejects it, remain strictly inside
                    // the full-containment zone instead of falling back to partial coverage.
                    radius = BonusCircleFullCoverageCenterRadius();
                }
                else
                {
                    float maximumCoverageRadius = Math.Max(6f, GuardianRadius - 1f);
                    radius = Math.Min(maximumCoverageRadius,
                        Math.Max(11f, SentryRejectedPositionRadius + 2f));
                }
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
            float[] scales = bonusCirclePlacement
                ? new[] { 0.5f, 0.75f, 1f }
                : protectedPlacement
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
                    if (bonusCirclePlacement && Distance2D(candidate.WorldX, candidate.WorldY,
                            rejected.WorldX, rejected.WorldY) > BonusCircleFullCoverageCenterRadius() + 0.01f)
                        continue;

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
            IActor valley = FindAuthoritativeValleyActor();
            if (cluster == null || valley == null || valley.FloorCoordinate == null
                || _lastValleyActorSeenTick == int.MinValue
                || Elapsed(_lastValleyActorSeenTick, now) > Math.Max(0, MfdNativeDropoutGraceMs)
                || Distance2D(valley.FloorCoordinate.X, valley.FloorCoordinate.Y,
                    cluster.CenterX, cluster.CenterY) > CombatBodyNearAnchorRadius)
                return null;
            if (NearestSentryDistance(sentries, valley.FloorCoordinate.X, valley.FloorCoordinate.Y)
                < SentryMinSeparation) return null;

            Placement placement = CreatePlacement(valley.FloorCoordinate.X, valley.FloorCoordinate.Y,
                valley.FloorCoordinate.Z);
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
                || IsEliteSentryCoveragePlacement(placement)
                || IsPlayerProtectionPlacement(placement)
                || IsBonusCircleSentryPlacement(placement);
        }

        private bool IsEliteSentryCoveragePlacement(Placement placement)
        {
            return placement != null && !string.IsNullOrEmpty(placement.Label)
                && (placement.Label.StartsWith("Sentry Field Elite Coverage", StringComparison.Ordinal)
                    || string.Equals(placement.Label, "Sentry Boss Coverage", StringComparison.Ordinal));
        }

        private bool IsBonusCircleSentryPlacement(Placement placement)
        {
            return placement != null && !string.IsNullOrEmpty(placement.Label)
                && placement.Label.StartsWith("Sentry Bonus Circle", StringComparison.Ordinal);
        }

        private bool IsUrgentBonusCircleSentryPlacement(Placement placement)
        {
            return IsBonusCircleSentryPlacement(placement)
                && placement.Label.EndsWith(" Urgent", StringComparison.Ordinal);
        }

        private float BonusCircleFullCoverageCenterRadius()
        {
            return Math.Max(0.5f, GuardianRadius - Math.Max(0f, SentryBonusCircleRadius)
                - Math.Max(0f, SentryBonusCircleCoverageSafetyMargin));
        }

        private float DesiredSentryMatchRadius(Placement placement)
        {
            return IsBonusCircleSentryPlacement(placement)
                ? BonusCircleFullCoverageCenterRadius()
                : SentryPatternMatchRadius;
        }

        private bool IsPlayerProtectionPlacement(Placement placement)
        {
            return placement != null && !string.IsNullOrEmpty(placement.Label)
                && (placement.Label.StartsWith("Sentry DPS", StringComparison.Ordinal)
                    || placement.Label.StartsWith("Sentry ZDH", StringComparison.Ordinal));
        }

        private float RequiredSentrySeparation(Placement placement)
        {
            // A partially covering Sentry must not block a core placement that is needed to
            // fully contain an Oculus/Triune circle. If it were already this close, the bonus
            // placement would have matched and would not be missing.
            if (IsBonusCircleSentryPlacement(placement))
                return BonusCircleFullCoverageCenterRadius();
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
            float limit = Math.Max(2f, Math.Min(SentryStackedDistance, SentrySevereOverlapDistance));
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
                float matchRadius = DesiredSentryMatchRadius(placement);
                IActor match = available
                    .Where(actor => DistanceToPoint(actor, placement.WorldX, placement.WorldY) <= matchRadius)
                    .OrderBy(actor => DistanceToPoint(actor, placement.WorldX, placement.WorldY))
                    .FirstOrDefault();
                if (match == null) missing.Add(placement);
                else available.Remove(match);
            }
            return missing;
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

        private void UpdateElectrifiedAlert(ZdhLoadout local, int now)
        {
            bool eligible = s7o_ZDH_HelperState.Enabled && local != null && local.QualifiesForDisplay
                && local.Player != null && local.Player.HasValidActor && !local.Player.IsDead;
            if (!eligible) return;

            var electrified = Hud.Game.AliveMonsters
                .Where(IsElectrifiedAlertActor)
                .ToList();
            var onScreen = electrified.Where(monster => monster.IsOnScreen).ToList();

            if (onScreen.Count > 0)
            {
                float x = 0f;
                float y = 0f;
                int count = 0;
                foreach (IMonster monster in onScreen)
                {
                    _electrifiedEncounterAcds.Add(monster.AcdId);
                    if (monster.FloorCoordinate == null) continue;
                    x += monster.FloorCoordinate.X;
                    y += monster.FloorCoordinate.Y;
                    count++;
                }
                if (count > 0)
                {
                    _electrifiedLastSeenX = x / count;
                    _electrifiedLastSeenY = y / count;
                    _electrifiedLastSeenValid = true;
                }

                if (!_electrifiedPresenceActive)
                {
                    _electrifiedPresenceActive = true;
                    _electrifiedAlertTick = now;
                    _electrifiedAlertText = GetElectrifiedLocalizedName(onScreen[0]);
                }
                _electrifiedAbsentSinceTick = int.MinValue;
                return;
            }

            if (!_electrifiedPresenceActive) return;

            // IsOnScreen is not a reliable departure signal for burrowing/phase-shifting elites:
            // FreeHUD can report the same nearby pack off-screen while it is underground. Rearm
            // only after the player actually leaves the encounter area, then sees Electrified again.
            bool playerMovedAway = _electrifiedLastSeenValid && local.Player.FloorCoordinate != null
                && Distance2D(local.Player.FloorCoordinate.X, local.Player.FloorCoordinate.Y,
                    _electrifiedLastSeenX, _electrifiedLastSeenY)
                    >= Math.Max(30f, ElectrifiedAlertRearmDistance);

            if (!playerMovedAway)
            {
                _electrifiedAbsentSinceTick = int.MinValue;
                return;
            }

            if (_electrifiedAbsentSinceTick == int.MinValue) _electrifiedAbsentSinceTick = now;
            if (Elapsed(_electrifiedAbsentSinceTick, now) >= Math.Max(0, ElectrifiedAlertRearmMs))
                ResetElectrifiedAlert();
        }

        private bool IsElectrifiedAlertActor(IMonster monster)
        {
            if (monster == null || !monster.IsAlive || monster.FloorCoordinate == null
                || monster.Illusion || IsJuggernaut(monster))
                return false;
            if (monster.Rarity != ActorRarity.Champion && monster.Rarity != ActorRarity.Rare
                && monster.Rarity != ActorRarity.Unique && monster.Rarity != ActorRarity.Boss)
                return false;
            return IsElectrified(monster);
        }

        private void ResetElectrifiedAlert()
        {
            _electrifiedAbsentSinceTick = int.MinValue;
            _electrifiedAlertTick = int.MinValue;
            _electrifiedPresenceActive = false;
            _electrifiedAlertText = string.Empty;
            _electrifiedLastSeenX = 0f;
            _electrifiedLastSeenY = 0f;
            _electrifiedLastSeenValid = false;
            _electrifiedEncounterAcds.Clear();
        }

        private string GetElectrifiedLocalizedName(IMonster monster)
        {
            try
            {
                var affix = monster == null || monster.AffixSnoList == null ? null
                    : monster.AffixSnoList.FirstOrDefault(a => a != null && a.Affix == MonsterAffix.Electrified);
                return affix == null ? string.Empty : (affix.NameLocalized ?? string.Empty);
            }
            catch { return string.Empty; }
        }

        private IFont[] CreateAlertPopFonts(int r, int g, int b, float size)
        {
            const int count = 21;
            var fonts = new IFont[count];
            float min = Math.Max(6.0f, size * 0.92f);
            float max = Math.Max(min, size * 1.12f);
            for (int i = 0; i < count; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", min + ((max - min) * i / (count - 1)),
                    255, r, g, b, true, false, false);
            return fonts;
        }

        private IFont[] CreateAlertFadeFonts(int r, int g, int b, float size)
        {
            var fonts = new IFont[11];
            float fontSize = Math.Max(6.0f, size);
            for (int i = 0; i < fonts.Length; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", fontSize,
                    (int)Math.Round(255.0d * i / 10.0d), r, g, b, true, false, false);
            return fonts;
        }

        private void DrawElectrifiedAlert(int now)
        {
            if (_electrifiedAlertTick == int.MinValue || string.IsNullOrEmpty(_electrifiedAlertText)
                || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null) return;

            int elapsed = Elapsed(_electrifiedAlertTick, now);
            int popMs = Math.Max(0, ElectrifiedAlertPopMs);
            int holdMs = Math.Max(0, ElectrifiedAlertHoldMs);
            int fadeMs = Math.Max(0, ElectrifiedAlertFadeMs);
            if (elapsed > holdMs + fadeMs) return;

            IFont font;
            IFont outline;
            if (elapsed <= holdMs && _electrifiedPopFonts != null && _electrifiedPopFonts.Length > 0)
            {
                int index = GetAlertPulseIndex(elapsed, ElectrifiedAlertPulsePeriodMs,
                    _electrifiedPopFonts.Length);
                font = _electrifiedPopFonts[index];
                outline = _electrifiedOutlinePopFonts == null ? null : _electrifiedOutlinePopFonts[index];
            }
            else
            {
                if (fadeMs <= 0) return;
                int fadeElapsed = elapsed - holdMs;
                if (fadeElapsed >= fadeMs) return;
                int bucket = Math.Max(1, Math.Min(10,
                    (int)Math.Ceiling((1.0d - ((double)fadeElapsed / fadeMs)) * 10.0d)));
                font = _electrifiedFadeFonts == null ? null : _electrifiedFadeFonts[bucket];
                outline = _electrifiedOutlineFadeFonts == null ? null : _electrifiedOutlineFadeFonts[bucket];
            }
            if (font == null) return;

            IScreenCoordinate sc = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
            if (sc == null) return;
            string text = _electrifiedAlertText;
            float travel = popMs <= 0 ? 1.0f : SmoothAlert(Math.Min(1.0f, (float)elapsed / popMs));
            float y = sc.Y + (-115f + ((-205f + 115f) * travel));
            float x = sc.X - font.GetTextLayout(text).Metrics.Width * 0.5f;
            DrawAlertOutlinedText(text, x, y, font, outline);
        }

        private static int GetAlertPulseIndex(int elapsed, int periodMs, int count)
        {
            if (count <= 1 || periodMs <= 0) return count / 2;
            float phase = (elapsed % periodMs) / (float)periodMs;
            float wave = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
            return Math.Max(0, Math.Min(count - 1,
                (int)Math.Round(SmoothAlert(wave) * (count - 1))));
        }

        private static float SmoothAlert(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3.0f - 2.0f * t);
        }

        private static void DrawAlertOutlinedText(string text, float x, float y, IFont font, IFont outline)
        {
            if (outline != null)
            {
                const int radius = 4;
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                        if ((dx != 0 || dy != 0) && dx * dx + dy * dy <= radius * radius + 2)
                            outline.DrawText(text, x + dx, y + dy, false);
            }
            font.DrawText(text, x, y, false);
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

        private bool IsPlayerPositionStable(IPlayer player, int now, int requiredMs)
        {
            PlayerPositionState state;
            return player != null && _playerPositions.TryGetValue(player.AcdId, out state)
                && state.StableSinceTick != int.MinValue
                && Elapsed(state.StableSinceTick, now) >= Math.Max(0, requiredMs);
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

        private bool IsProjectableEdgeSentryPoint(IPlayer zdh, IWorldCoordinate point)
        {
            if (zdh == null || zdh.FloorCoordinate == null || point == null) return false;
            if (DistanceToPoint(zdh, point.X, point.Y) > NativeSentryPlacementMaxRangeYards)
                return false;
            return CreatePlacement(point.X, point.Y, point.Z) != null;
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

        private IEnumerable<IPlayer> GetSentryDpsPlayers(IPlayer zdh)
        {
            List<IActor> ownedSentries = GetOwnedSentries();
            List<IPlayer> candidates = Hud.Game.Players.Where(p => p != null
                    && p.HasValidActor && !p.IsDead && !SamePlayer(p, zdh)
                    && p.CoordinateKnown && p.FloorCoordinate != null
                    && (p.IsOnScreen
                        || IsSentryNear(ownedSentries, p.FloorCoordinate.X,
                            p.FloorCoordinate.Y, GuardianRadius)
                        || IsProjectableEdgeSentryPoint(zdh, p.FloorCoordinate)))
                .ToList();

            double maxDps = candidates.Count == 0 ? 0 : candidates.Max(p => PlayerDpsScore(p));
            return candidates.Where(p => !IsLikelySupport(p, maxDps))
                .OrderByDescending(p => PlayerRoleScore(p, maxDps));
        }

        private IEnumerable<IPlayer> GetDpsPlayers(IPlayer zdh, bool anyOnScreenRange = false)
        {
            List<IPlayer> candidates = Hud.Game.Players.Where(p => p != null && p.HasValidActor && !p.IsDead
                    && !SamePlayer(p, zdh) && p.CoordinateKnown && p.IsOnScreen
                    && (anyOnScreenRange || PlayerDistance(zdh, p) <= AutomationRange))
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
            IActor newestValley = null;

            foreach (IActor actor in actors)
            {
                if (actor == null || actor.SnoActor == null || actor.FloorCoordinate == null) continue;
                bool isValley = IsValleyActor(actor);
                bool isSentry = IsGuardianSentryBody(actor);
                if (!isValley && !isSentry) continue;
                alive.Add(actor.AcdId);

                if (isValley)
                {
                    if (!IsMfdActorOwnedCandidate(actor, now)
                        || IsGenerationOlder(actor.CreatedAtInGameTick, actor.AcdId,
                            _lastValleyActorCreatedTick, _lastValleyActorAcd))
                    {
                        continue;
                    }

                    _ownedActorAcds.Add(actor.AcdId);
                    if (newestValley == null || IsGenerationNewer(actor.CreatedAtInGameTick, actor.AcdId,
                        newestValley.CreatedAtInGameTick, newestValley.AcdId))
                        newestValley = actor;
                    continue;
                }

                // Native summoner identity is authoritative when FreeHUD exposes it.
                // Never let proximity/timing fallback adopt another player's Guardian Sentry.
                if (HasKnownForeignSummoner(actor))
                {
                    _ownedActorAcds.Remove(actor.AcdId);
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
                && (_recentGroundKind != CastKind.Sentry || !HasKnownForeignSummoner(actor))
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
            if (!IsValleyActor(actor) || actor.FloorCoordinate == null
                || _recentGroundKind != CastKind.MarkedForDeath
                || _recentGroundTick == int.MinValue)
                return false;

            int age = Elapsed(_recentGroundTick, now);
            if (age > GroundActorAdoptionMs) return false;

            // Diablo can relocate Valley away from an obstructed cursor point. During the
            // immediate native-spawn window, use a wider ownership/adoption radius only;
            // actual Valley coverage still uses the configured 15-yard gameplay radius.
            float adoptionRadius = age <= Math.Max(200, MfdNativeDropoutGraceMs)
                ? Math.Max(30f, ValleyRadius * 3f)
                : ValleyRadius;
            return actor.FloorCoordinate.XYDistanceTo(_recentGroundX, _recentGroundY) <= adoptionRadius;
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

        private bool HasNewMfdActorGeneration()
        {
            return IsGenerationNewer(_lastValleyActorCreatedTick, _lastValleyActorAcd,
                    _cast.BaselineMfdActorCreatedTick, _cast.BaselineMfdActorAcd)
                && _lastValleyActorCreatedTick >= _cast.BaselineMfdGameTick
                && _lastValleyActorSeenTick != int.MinValue
                && Elapsed(_lastValleyActorSeenTick, Environment.TickCount)
                    <= Math.Max(0, MfdNativeDropoutGraceMs);
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

        private bool HasKnownForeignSummoner(IActor actor)
        {
            IPlayer me = Hud == null || Hud.Game == null ? null : Hud.Game.Me;
            return actor != null && me != null && me.SummonerId != 0
                && actor.SummonerAcdDynamicId != 0
                && actor.SummonerAcdDynamicId != me.SummonerId;
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
                && !HasKnownForeignSummoner(actor)
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

        private bool IsElectrified(IMonster monster)
        {
            try { return monster != null && monster.AffixSnoList != null
                && monster.AffixSnoList.Any(a => a != null && a.Affix == MonsterAffix.Electrified); }
            catch { return false; }
        }

        private bool IsLocalGhosted()
        {
            try
            {
                return Hud != null && Hud.Game != null && Hud.Game.Me != null
                    && Hud.Game.Me.Powers != null
                    && Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno);
            }
            catch { return false; }
        }

        private bool ContextAvailable()
        {
            return Hud != null && Hud.Game != null && Hud.Window != null && Hud.Game.IsInGame && !Hud.Game.IsLoading && !Hud.Game.IsPaused && Hud.Game.Me != null;
        }

        private bool SentryBurstAutomationContextValid()
        {
            return ContextAvailable() && s7o_ZDH_HelperState.Enabled && !Hud.Game.IsInTown
                && !Hud.Game.Me.IsDead && !IsLocalGhosted() && Hud.Window.IsForeground
                && !ZdhInput.IsVirtualKeyDown(0x5B) && !ZdhInput.IsVirtualKeyDown(0x5C)
                && PointInsideWindow(Hud.Window.CursorX, Hud.Window.CursorY)
                && !InventoryOpen() && !UiVisible(_chatEditLine) && !UiVisible(Hud.Render.WorldMapUiElement)
                && !_interactionPauseActive
                && Hud.Game.Me.AnimationState != AcdAnimationState.CastingPortal;
        }

        private bool AutomationContextValid()
        {
            return ContextAvailable() && s7o_ZDH_HelperState.Enabled && !Hud.Game.IsInTown
                && !Hud.Game.Me.IsDead && !IsLocalGhosted() && Hud.Window.IsForeground
                && !ZdhInput.IsVirtualKeyDown(0x5B) && !ZdhInput.IsVirtualKeyDown(0x5C)
                && PointInsideWindow(Hud.Window.CursorX, Hud.Window.CursorY)
                && !InventoryOpen() && !UiVisible(_chatEditLine) && !UiVisible(Hud.Render.WorldMapUiElement)
                && !_interactionPauseActive
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

        private bool IsInteractionPauseNearby()
        {
            return IsUnoperatedPylonNearby(PylonInteractionPauseRange)
                || s7o_DHStrafePrimaryPlugin.IsPortalInteractionPauseActiveForZdh;
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
        }

        private bool PlayerBuffActive(uint sno)
        {
            try { return Hud.Game.Me != null && Hud.Game.Me.Powers != null && Hud.Game.Me.Powers.BuffIsActive(sno); }
            catch { return false; }
        }

        private bool SentryAvailable(IPlayerSkill skill)
        {
            return skill != null && skill.Key != ActionKey.Unknown
                && (skill.Charges > 0 || (_channelingPylonActive && !skill.IsOnCooldown))
                && HasRequiredSkillResource(skill);
        }

        private bool SkillReady(IPlayerSkill skill)
        {
            return skill != null && skill.Key != ActionKey.Unknown
                && (skill.Charges > 0 || !skill.IsOnCooldown)
                && HasRequiredSkillResource(skill);
        }

        private bool HasRequiredSkillResource(IPlayerSkill skill)
        {
            string resourceType;
            float available;
            float required;
            if (!TryGetSkillResourceSnapshot(skill, out resourceType, out available, out required)) return true;
            return required <= 0.01f || available + 0.01f >= required;
        }

        private bool TryGetSkillResourceSnapshot(IPlayerSkill skill, out string resourceType,
            out float available, out float required)
        {
            resourceType = string.Empty;
            available = 0;
            required = 0;
            if (skill == null || skill.Player == null || skill.Player.Stats == null) return false;

            uint sno = SkillSno(skill);
            if (sno != MarkedForDeathSno && sno != MultishotSno && sno != SentrySno) return false;
            try { required = Math.Max(0f, skill.GetResourceRequirement()); }
            catch { required = Math.Max(0f, skill.ResourceCost); }

            if (sno == MarkedForDeathSno)
            {
                resourceType = "secondary";
                available = skill.Player.Stats.ResourceCurSec;
            }
            else
            {
                resourceType = "primary";
                available = skill.Player.Stats.ResourceCurPri;
            }
            return true;
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

        private bool IsCloseRangeMultishotDirectTarget(IPlayer player, IMonster target)
        {
            return player != null && target != null && player.FloorCoordinate != null
                && target.FloorCoordinate != null && IsImportantDebuffTarget(target)
                && Distance(player, target) <= Math.Max(1f, MultishotCloseRangeDirectAimDistance);
        }

        private MultishotPlan BuildDirectMultishotFallbackPlan(IPlayer player, IMonster target,
            HashSet<uint> dueAcds)
        {
            if (player == null || target == null || player.FloorCoordinate == null
                || target.FloorCoordinate == null || target.ScreenCoordinate == null) return null;

            float directionX = target.FloorCoordinate.X - player.FloorCoordinate.X;
            float directionY = target.FloorCoordinate.Y - player.FloorCoordinate.Y;
            if (!NormalizeDirection(ref directionX, ref directionY)) return null;
            if (!IsInsideMultishotCone(player, target, directionX, directionY)) return null;

            IScreenCoordinate aim = CreateMultishotDirectionalAim(player, directionX, directionY)
                ?? CreateSafeDirectionalAim(player, target.ScreenCoordinate);
            if (aim == null || !PointInsideCastArea(aim.X, aim.Y)) return null;

            var plan = new MultishotPlan
            {
                Primary = target,
                Aim = aim,
                Score = MultishotTargetWeight(target, true),
                CoveredBodyCount = 1,
                CoveredEliteCount = IsStatusTarget(target) ? 1 : 0,
                CoveredPlanningEliteCount = IsImportantDebuffTarget(target) ? 1 : 0,
                RequiredApplied = 1,
                PrimaryMustApply = !HasIceblink(target) && IsImportantDebuffTarget(target),
                DirectionX = directionX,
                DirectionY = directionY,
                MaxDueEliteAngleDegrees = 0,
                AverageDueEliteAngleDegrees = 0,
            };
            if (dueAcds != null && dueAcds.Contains(target.AcdId))
                plan.CoveredMissingAcds.Add(target.AcdId);
            if (IsImportantDebuffTarget(target))
            {
                plan.CoveredEliteAcds.Add(target.AcdId);
                if (dueAcds != null && dueAcds.Contains(target.AcdId))
                    plan.CoveredMissingEliteAcds.Add(target.AcdId);
            }
            if (IsStatusTarget(target))
                plan.CoveredPrimaryEliteAcds.Add(target.AcdId);
            return plan;
        }

        private MultishotPlan BuildMultishotPlan(IPlayer player, List<IMonster> targets,
            HashSet<uint> dueAcds, HashSet<uint> planningAcds, int now, uint recoveryFocusAcd = 0)
        {
            if (player == null || player.FloorCoordinate == null || targets == null || targets.Count == 0)
                return null;

            dueAcds = dueAcds ?? new HashSet<uint>();
            planningAcds = planningAcds ?? dueAcds;
            bool priorityMode = dueAcds.Count > 0;
            targets = targets.OrderByDescending(m => MultishotTargetWeight(m, dueAcds.Contains(m.AcdId)))
                .Take(32).ToList();
            var directions = new List<DirectionCandidate>();
            IMonster recoveryFocus = recoveryFocusAcd == 0 ? null
                : targets.FirstOrDefault(m => m != null && m.AcdId == recoveryFocusAcd);
            if (recoveryFocus != null && recoveryFocus.FloorCoordinate != null)
            {
                // A cone prediction already failed for this elite. Center the next recovery shot
                // directly on it instead of repeating the same optimistic shared-cone assumption.
                AddDirectionCandidate(directions,
                    recoveryFocus.FloorCoordinate.X - player.FloorCoordinate.X,
                    recoveryFocus.FloorCoordinate.Y - player.FloorCoordinate.Y);
            }
            else
            {
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
            }

            MultishotPlan best = null;
            foreach (DirectionCandidate direction in directions)
            {
                var covered = new List<IMonster>();
                double score = 0;
                foreach (IMonster target in targets)
                {
                    if (!IsInsideMultishotCone(player, target, direction.X, direction.Y)) continue;
                    // Monster-radius allowance is useful for trash density, but an important
                    // debuff target is only considered covered when its live core is inside the
                    // conservative cone. Do not plan Iceblink around a marginal hitbox graze.
                    if (IsImportantDebuffTarget(target)
                        && MultishotAngleDegrees(player, target, direction.X, direction.Y)
                            > GetMultishotImportantCoreAngleDegrees()) continue;
                    covered.Add(target);
                    score += MultishotTargetWeight(target, dueAcds.Contains(target.AcdId));
                }
                if (covered.Count == 0) continue;

                List<IMonster> coveredDue = covered.Where(m => dueAcds.Contains(m.AcdId)).ToList();
                if (priorityMode && coveredDue.Count == 0) continue;
                List<IMonster> coveredDueImportant = coveredDue.Where(IsImportantDebuffTarget).ToList();
                List<IMonster> coveredPlanningImportant = covered
                    .Where(m => planningAcds.Contains(m.AcdId) && IsImportantDebuffTarget(m)).ToList();
                List<IMonster> coveredPrimaryElites = covered.Where(IsStatusTarget).ToList();

                double maxDueEliteAngle = 0;
                double averageDueEliteAngle = 0;
                if (recoveryFocus != null)
                {
                    if (!covered.Any(m => m.AcdId == recoveryFocus.AcdId)) continue;
                    maxDueEliteAngle = MultishotAngleDegrees(player, recoveryFocus, direction.X, direction.Y);
                    averageDueEliteAngle = maxDueEliteAngle;
                }
                else if (coveredDueImportant.Count > 0)
                {
                    List<double> dueAngles = coveredDueImportant
                        .Select(m => MultishotAngleDegrees(player, m, direction.X, direction.Y))
                        .ToList();
                    maxDueEliteAngle = dueAngles.Max();
                    averageDueEliteAngle = dueAngles.Average();
                    if (maxDueEliteAngle > GetMultishotImportantCoreAngleDegrees()) continue;
                }

                IMonster primary = recoveryFocus
                    ?? coveredDueImportant.OrderByDescending(m => TargetPriority(m, true)).FirstOrDefault()
                    ?? coveredDue.OrderByDescending(m => MultishotTargetWeight(m, true)).FirstOrDefault()
                    ?? covered.OrderByDescending(m => MultishotTargetWeight(m, false)).FirstOrDefault();
                if (primary == null) continue;

                IScreenCoordinate aim = CreateMultishotDirectionalAim(player, direction.X, direction.Y);
                if (aim == null) continue;

                var plan = new MultishotPlan
                {
                    Primary = primary,
                    Aim = aim,
                    Score = score,
                    CoveredBodyCount = covered.Count,
                    CoveredEliteCount = coveredPrimaryElites.Count,
                    CoveredPlanningEliteCount = coveredPlanningImportant.Count,
                    PrimaryMustApply = priorityMode && !HasIceblink(primary) && IsImportantDebuffTarget(primary),
                    DirectionX = direction.X,
                    DirectionY = direction.Y,
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

                bool sameDueCoverage = best != null
                    && plan.CoveredMissingEliteAcds.Count == best.CoveredMissingEliteAcds.Count;
                bool samePlanningCoverage = sameDueCoverage
                    && plan.CoveredPlanningEliteCount == best.CoveredPlanningEliteCount;
                bool sameEliteCoverage = samePlanningCoverage
                    && plan.CoveredEliteCount == best.CoveredEliteCount;
                bool sameMaxDueAngle = sameEliteCoverage
                    && Math.Abs(plan.MaxDueEliteAngleDegrees - best.MaxDueEliteAngleDegrees) <= 0.1;

                bool better = best == null
                    || plan.CoveredMissingEliteAcds.Count > best.CoveredMissingEliteAcds.Count
                    || (sameDueCoverage && plan.CoveredPlanningEliteCount > best.CoveredPlanningEliteCount)
                    || (samePlanningCoverage && plan.CoveredEliteCount > best.CoveredEliteCount)
                    || (sameEliteCoverage
                        && plan.MaxDueEliteAngleDegrees < best.MaxDueEliteAngleDegrees - 0.1)
                    || (sameMaxDueAngle
                        && plan.AverageDueEliteAngleDegrees < best.AverageDueEliteAngleDegrees - 0.1)
                    || (sameMaxDueAngle
                        && Math.Abs(plan.AverageDueEliteAngleDegrees - best.AverageDueEliteAngleDegrees) <= 0.1
                        && plan.Score > best.Score);
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

        private double GetMultishotImportantCoreAngleDegrees()
        {
            return Math.Max(1f, Math.Min(MultishotConeHalfAngleDegrees,
                MultishotDueEliteSafeAngleDegrees));
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

        private IScreenCoordinate CreateMultishotDirectionalAim(IPlayer player, float directionX, float directionY)
        {
            return CreateMultishotDirectionalAim(player, directionX, directionY, 0f);
        }

        private IScreenCoordinate CreateMultishotDirectionalAim(IPlayer player, float directionX,
            float directionY, float deterministicMinimumScreenDistance)
        {
            if (player == null || player.FloorCoordinate == null || player.ScreenCoordinate == null) return null;
            float worldX = player.FloorCoordinate.X + directionX * MultishotAimDistance;
            float worldY = player.FloorCoordinate.Y + directionY * MultishotAimDistance;
            IScreenCoordinate projected = Hud.Window.WorldToScreenCoordinate(worldX, worldY, player.FloorCoordinate.Z, false, true);
            return CreateMultishotScreenRayAim(player.ScreenCoordinate, projected,
                deterministicMinimumScreenDistance);
        }

        private IScreenCoordinate CreateMultishotCoreAim(IPlayer player, IMonster target)
        {
            if (player == null || target == null || player.FloorCoordinate == null
                || target.FloorCoordinate == null) return null;

            // FloorCoordinate defines the actor's ground/core XY and RadiusBottom its footprint.
            // Multishot itself originates above the floor, so project the same XY ray through the
            // Demon Hunter skill-origin plane instead of deriving screen direction from the feet.
            float directionX = target.FloorCoordinate.X - player.FloorCoordinate.X;
            float directionY = target.FloorCoordinate.Y - player.FloorCoordinate.Y;
            if (!NormalizeDirection(ref directionX, ref directionY)) return null;

            float rayZ = player.FloorCoordinate.Z + Math.Max(0f, MultishotAimOriginZOffset);
            IScreenCoordinate origin = Hud.Window.WorldToScreenCoordinate(
                player.FloorCoordinate.X, player.FloorCoordinate.Y, rayZ, false, true);
            IScreenCoordinate targetOnRay = Hud.Window.WorldToScreenCoordinate(
                target.FloorCoordinate.X, target.FloorCoordinate.Y, rayZ, false, true);
            if (origin == null || targetOnRay == null) return null;

            float dx = targetOnRay.X - origin.X;
            float dy = targetOnRay.Y - origin.Y;
            float targetDistance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (targetDistance < 1f) return null;

            return CreateMultishotScreenRayAim(origin, targetOnRay,
                targetDistance + Math.Max(0f, MultishotCloseRangeAimPastTargetPixels));
        }

        private IScreenCoordinate CreateMultishotScreenRayAim(IScreenCoordinate origin,
            IScreenCoordinate rayPoint, float deterministicMinimumScreenDistance)
        {
            if (origin == null || rayPoint == null) return null;

            float dx = rayPoint.X - origin.X;
            float dy = rayPoint.Y - origin.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1f) return null;
            dx /= length;
            dy /= length;

            Size size = Hud.Window.Size;
            float left = Math.Max(24f, size.Width * MultishotSafeSideRatio);
            float right = Math.Min(size.Width - 24f, size.Width * (1f - MultishotSafeSideRatio));
            float top = Math.Max(24f, size.Height * MultishotSafeTopRatio);
            float bottom = Math.Min(size.Height - 140f, size.Height * MultishotSafeBottomRatio);
            float maximumDistance = float.MaxValue;
            if (dx > 0) maximumDistance = Math.Min(maximumDistance, (right - origin.X) / dx);
            else if (dx < 0) maximumDistance = Math.Min(maximumDistance, (left - origin.X) / dx);
            if (dy > 0) maximumDistance = Math.Min(maximumDistance, (bottom - origin.Y) / dy);
            else if (dy < 0) maximumDistance = Math.Min(maximumDistance, (top - origin.Y) / dy);

            float minimumDistance = Math.Max(90f, MultishotAimMinimumScreenDistance);
            if (maximumDistance < minimumDistance) return null;

            float distance;
            if (deterministicMinimumScreenDistance > 0f)
            {
                // Settled support rays use a deterministic point beyond their tracked cores.
                // This removes current-cursor proximity from combat geometry.
                distance = Math.Min(maximumDistance,
                    Math.Max(minimumDistance, deterministicMinimumScreenDistance));
            }
            else
            {
                // The initial plan is only a lease preview. It is rebuilt deterministically
                // from live targets after movement settles, before any input is sent.
                float cursorProjection = (Hud.Window.CursorX - origin.X) * dx
                    + (Hud.Window.CursorY - origin.Y) * dy;
                distance = Math.Max(minimumDistance, Math.Min(maximumDistance, cursorProjection));
            }
            return CreateUiSafeRayAim(origin, dx, dy, distance, minimumDistance, maximumDistance);
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
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1f) return SafeFallbackAim(fallback);
            dx /= length;
            dy /= length;

            float maximumDistance = float.MaxValue;
            if (dx > 0) maximumDistance = Math.Min(maximumDistance, (right - origin.X) / dx);
            else if (dx < 0) maximumDistance = Math.Min(maximumDistance, (left - origin.X) / dx);
            if (dy > 0) maximumDistance = Math.Min(maximumDistance, (bottom - origin.Y) / dy);
            else if (dy < 0) maximumDistance = Math.Min(maximumDistance, (top - origin.Y) / dy);
            if (maximumDistance < minimumDistance) return SafeFallbackAim(fallback);

            float preferredDistance = Math.Min(length, maximumDistance);
            IScreenCoordinate safe = CreateUiSafeRayAim(origin, dx, dy, preferredDistance, minimumDistance, maximumDistance);
            return safe ?? SafeFallbackAim(fallback);
        }

        private IScreenCoordinate CreateUiSafeRayAim(IScreenCoordinate origin, float dx, float dy,
            float preferredDistance, float minimumDistance, float maximumDistance)
        {
            if (origin == null || maximumDistance < minimumDistance) return null;
            float preferred = Math.Max(minimumDistance, Math.Min(maximumDistance, preferredDistance));
            float step = Math.Max(8f, ClickGuardRayProbeStepPixels);

            for (float distance = preferred; distance >= minimumDistance; distance -= step)
            {
                float x = origin.X + dx * distance;
                float y = origin.Y + dy * distance;
                if (PointInsideCastArea(x, y))
                    return Hud.Window.CreateScreenCoordinate(x, y);
            }

            for (float distance = preferred + step; distance <= maximumDistance; distance += step)
            {
                float x = origin.X + dx * distance;
                float y = origin.Y + dy * distance;
                if (PointInsideCastArea(x, y))
                    return Hud.Window.CreateScreenCoordinate(x, y);
            }

            return null;
        }

        private IScreenCoordinate SafeFallbackAim(IScreenCoordinate fallback)
        {
            if (fallback == null) return null;
            Size size = Hud.Window.Size;
            float left = Math.Max(32f, size.Width * MultishotSafeSideRatio);
            float right = Math.Min(size.Width - 32f, size.Width * (1f - MultishotSafeSideRatio));
            float top = Math.Max(36f, size.Height * MultishotSafeTopRatio);
            float bottom = Math.Min(size.Height - 180f, size.Height * MultishotSafeBottomRatio);
            float x = Math.Max(left, Math.Min(right, fallback.X));
            float y = Math.Max(top, Math.Min(bottom, fallback.Y));
            return PointInsideCastArea(x, y) ? Hud.Window.CreateScreenCoordinate(x, y) : null;
        }

        private int GetAimSettleMs(CastKind kind)
        {
            return kind == CastKind.Entangle
                ? EntangleAimSettleMs
                : SupportCursorPreviewMs;
        }

        private int GetSkillHoldMs(CastKind kind)
        {
            return kind == CastKind.Multishot ? MultishotSkillHoldMs
                : kind == CastKind.Sentry ? SentrySkillHoldMs
                : kind == CastKind.MarkedForDeath ? GroundSkillHoldMs : EntangleSkillHoldMs;
        }

        private int GetVerifyMs(CastKind kind)
        {
            // Short activation-commit observers only serialize the support scheduler; movement
            // has already been returned before verification starts. Iceblink effect validation
            // still runs asynchronously after Multishot acceptance.
            return kind == CastKind.Entangle ? EntangleVerifyMs
                : kind == CastKind.Multishot ? MultishotCommitMs
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
            return _cast.Kind == CastKind.Multishot
                || _cast.Kind == CastKind.MarkedForDeath
                || (_cast.Kind == CastKind.Sentry && !_cast.SentryBurstChild);
        }

        private bool MovementSettledForCast(AcdAnimationState animation)
        {
            if (_cast.Kind == CastKind.Multishot)
            {
                // With Strafe off at the RG, a held manual Entangle keeps the actor in
                // Attacking. Multishot is allowed to interrupt that Primary animation just
                // during boss support; user-held mouse/Shift state is not
                // released or taken over by Helper.
                if (_bossStandaloneActive && !_cast.RequiresStrafePause
                    && animation == AcdAnimationState.Attacking)
                    return true;

                return animation != AcdAnimationState.Running
                    && animation != AcdAnimationState.Attacking;
            }

            return animation != AcdAnimationState.Running;
        }

        private bool CanAdvanceExpiredPreInputFrame(AcdAnimationState animation, int now)
        {
            if (_cast.InputSent) return false;

            if (_cast.Stage == CastStage.Lease)
                return !RequiresMovementSettleBeforeInput() || MovementSettledForCast(animation);

            if (_cast.Stage != CastStage.Aim) return false;

            // If movement became usable on the watchdog boundary, preserve the already-scheduled
            // aim-settle frame and its immediate dispatch opportunity instead of cancelling first.
            if (!Reached(now, _cast.DueTick)) return true;
            return !RequiresMovementSettleBeforeInput() || MovementSettledForCast(animation);
        }

        private void RefreshMfdExpectedWorldFromLiveTargets(int now)
        {
            if (_cast.Kind != CastKind.MarkedForDeath || _cast.VerifyImportantAcds.Count == 0) return;

            List<IMonster> liveTargets = _cast.VerifyImportantAcds
                .Select(FindMonster)
                // MFD is a ground field. Preserve temporarily burrowed, invulnerable, or
                // untargetable elites that the ground-support plan intentionally covered;
                // filtering them through direct-debuff eligibility collapsed multi-elite aim onto
                // the one currently attackable monster during the movement-settle handoff.
                .Where(m => m != null && IsGroundSupportElite(m) && m.IsOnScreen
                    && m.FloorCoordinate != null)
                .ToList();
            if (liveTargets.Count == 0) return;

            IMonster unmarkedBoss = liveTargets.FirstOrDefault(m =>
                m.Rarity == ActorRarity.Boss && m.Attackable && !m.Invulnerable && !m.MarkedForDeath);
            Placement fresh = unmarkedBoss != null
                ? CreateScoredPlacement(unmarkedBoss.FloorCoordinate.X, unmarkedBoss.FloorCoordinate.Y,
                    unmarkedBoss.FloorCoordinate.Z, liveTargets, now)
                : FindBestPlacement(liveTargets, now, true);
            if (fresh == null) return;

            _cast.ExpectedWorldX = fresh.WorldX;
            _cast.ExpectedWorldY = fresh.WorldY;
            _cast.ExpectedWorldZ = fresh.WorldZ;
        }

        private bool RefreshCastAimFromCurrentView()
        {
            IScreenCoordinate aim = null;
            if (_cast.Kind == CastKind.Multishot && _cast.HasMultishotDirection)
            {
                IPlayer player = Hud.Game.Me;
                var dueAcds = new HashSet<uint>(_cast.MultishotDueAcds);
                bool directCore;
                MultishotPlan settledPlan = BuildSettledMultishotPlan(player, dueAcds,
                    Environment.TickCount, out directCore);
                if (settledPlan == null || settledPlan.Primary == null) return false;

                if (directCore)
                {
                    // Direct recovery is deliberately conservative: verify only the target whose
                    // live core defines the settled ray. Any incidental cone coverage is a bonus.
                    aim = CreateMultishotCoreAim(player, settledPlan.Primary);
                }

                if (!directCore)
                {
                    float deterministicDistance = GetSettledMultishotAimDistance(player,
                        settledPlan);
                    aim = CreateMultishotDirectionalAim(player, settledPlan.DirectionX,
                        settledPlan.DirectionY, deterministicDistance);
                    if (aim == null)
                    {
                        // A safe-screen edge can invalidate a shared ray. Fall back to one fresh
                        // core instead of reusing the pre-pause preview or firing ambiguously.
                        settledPlan = BuildDirectMultishotFallbackPlan(player,
                            settledPlan.Primary, dueAcds);
                        if (settledPlan == null) return false;
                        directCore = true;
                        aim = CreateMultishotCoreAim(player, settledPlan.Primary);
                    }
                }

                if (aim == null) return false;
                ApplySettledMultishotPlan(settledPlan, directCore);
            }
            else if ((_cast.Kind == CastKind.MarkedForDeath || _cast.Kind == CastKind.Sentry)
                && !float.IsNaN(_cast.ExpectedWorldX) && !float.IsNaN(_cast.ExpectedWorldY))
            {
                if (_cast.Kind == CastKind.MarkedForDeath)
                    RefreshMfdExpectedWorldFromLiveTargets(Environment.TickCount);
                float z = !float.IsNaN(_cast.ExpectedWorldZ) ? _cast.ExpectedWorldZ
                    : Hud.Game.Me != null && Hud.Game.Me.FloorCoordinate != null
                        ? Hud.Game.Me.FloorCoordinate.Z : 0f;
                aim = Hud.Window.WorldToScreenCoordinate(
                    _cast.ExpectedWorldX, _cast.ExpectedWorldY, z, false, true);
            }
            else
            {
                return true;
            }

            if (aim == null || !PointInsideCastArea(aim.X, aim.Y)) return false;
            _cast.AimX = (int)Math.Round(aim.X);
            _cast.AimY = (int)Math.Round(aim.Y);
            return true;
        }

        private MultishotPlan BuildSettledMultishotPlan(IPlayer player,
            HashSet<uint> dueAcds, int now, out bool directCore)
        {
            directCore = _cast.MultishotDirectCore;
            if (player == null || player.FloorCoordinate == null) return null;
            dueAcds = dueAcds ?? new HashSet<uint>();

            var trackedAcds = new HashSet<uint>(_cast.MultishotEligibleAcds);
            trackedAcds.UnionWith(_cast.VerifyTargetAcds);
            trackedAcds.UnionWith(_cast.MultishotCoveredEliteAcds);
            if (_cast.TargetAcd != 0) trackedAcds.Add(_cast.TargetAcd);

            List<IMonster> liveTargets = trackedAcds.Select(FindMonster)
                .Where(m => m != null && m.FloorCoordinate != null
                    && m.IsOnScreen && IsDebuffBody(m))
                .Distinct().ToList();
            if (liveTargets.Count == 0) return null;

            bool hadDueTargets = dueAcds.Count > 0;
            dueAcds.IntersectWith(liveTargets.Select(m => m.AcdId));
            if (hadDueTargets && dueAcds.Count == 0) return null;

            if (_cast.MultishotDirectCore)
            {
                IMonster target = liveTargets.FirstOrDefault(m => m.AcdId == _cast.TargetAcd);
                if (target == null) return null;
                return BuildDirectMultishotFallbackPlan(player, target, dueAcds);
            }

            var planningAcds = new HashSet<uint>(_cast.MultishotPlanningAcds);
            planningAcds.UnionWith(_cast.VerifyImportantAcds);
            planningAcds.UnionWith(_cast.MultishotCoveredEliteAcds);
            MultishotPlan plan = BuildMultishotPlan(player, liveTargets, dueAcds,
                planningAcds, now);
            if (plan != null
                && plan.CoveredBodyCount >= _cast.MultishotMinimumBodyCoverage) return plan;
            if (_cast.MultishotMinimumBodyCoverage > 1) return null;

            IMonster fallback = liveTargets.Where(m => dueAcds.Contains(m.AcdId))
                    .OrderByDescending(m => TargetPriority(m, true)).FirstOrDefault()
                ?? liveTargets.FirstOrDefault(m => m.AcdId == _cast.TargetAcd)
                ?? liveTargets.Where(IsImportantDebuffTarget)
                    .OrderByDescending(m => TargetPriority(m, true)).FirstOrDefault()
                ?? liveTargets[0];
            directCore = true;
            return BuildDirectMultishotFallbackPlan(player, fallback, dueAcds);
        }

        private float GetSettledMultishotAimDistance(IPlayer player, MultishotPlan plan)
        {
            float minimumDistance = Math.Max(90f, MultishotAimMinimumScreenDistance);
            if (player == null || player.ScreenCoordinate == null || plan == null)
                return minimumDistance;

            foreach (uint acd in plan.CoveredMissingAcds
                .Concat(plan.CoveredPrimaryEliteAcds).Distinct())
            {
                IMonster target = FindMonster(acd);
                if (target == null || target.FloorCoordinate == null) continue;
                IScreenCoordinate core = Hud.Window.WorldToScreenCoordinate(
                    target.FloorCoordinate.X, target.FloorCoordinate.Y,
                    target.FloorCoordinate.Z, false, true);
                if (core == null) continue;
                float dx = core.X - player.ScreenCoordinate.X;
                float dy = core.Y - player.ScreenCoordinate.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                minimumDistance = Math.Max(minimumDistance,
                    distance + Math.Max(0f, MultishotCloseRangeAimPastTargetPixels));
            }
            return minimumDistance;
        }

        private void ApplySettledMultishotPlan(MultishotPlan plan, bool directCore)
        {
            _cast.TargetAcd = plan.Primary.AcdId;
            _cast.MultishotDirectionX = plan.DirectionX;
            _cast.MultishotDirectionY = plan.DirectionY;
            _cast.MultishotDirectCore = directCore;

            _cast.VerifyTargetAcds.Clear();
            foreach (uint acd in plan.CoveredMissingAcds)
                _cast.VerifyTargetAcds.Add(acd);

            _cast.VerifyImportantAcds.Clear();
            foreach (uint acd in plan.CoveredEliteAcds)
                _cast.VerifyImportantAcds.Add(acd);

            _cast.MultishotCoveredEliteAcds.Clear();
            _cast.MultishotBaselineActiveAcds.Clear();
            foreach (uint acd in plan.CoveredPrimaryEliteAcds)
            {
                _cast.MultishotCoveredEliteAcds.Add(acd);
                IMonster elite = FindMonster(acd);
                if (elite != null && HasIceblink(elite))
                    _cast.MultishotBaselineActiveAcds.Add(acd);
            }
        }

        private void MarkMultishotAttemptTargets(int now)
        {
            var attemptedAcds = new HashSet<uint>(_cast.VerifyTargetAcds);
            attemptedAcds.UnionWith(_cast.MultishotCoveredEliteAcds);
            if (_cast.TargetAcd != 0) attemptedAcds.Add(_cast.TargetAcd);
            foreach (uint acd in attemptedAcds)
            {
                IMonster attempted = FindMonster(acd);
                if (attempted != null)
                    GetTargetState(attempted, now).LastMultishotAttempt = now;
            }
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

        private bool SetCastCursorAndActionDown(int x, int y, ActionKey key)
        {
            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground || !PointInsideCastArea(x, y))
                return false;

            int screenX;
            int screenY;
            if (!TryClientToScreen(x, y, out screenX, out screenY))
                return false;

            int beforeX = _cast.CursorReferenceValid ? _cast.CursorReferenceX : x;
            int beforeY = _cast.CursorReferenceValid ? _cast.CursorReferenceY : y;
            int cursorX;
            int cursorY;
            if (TryGetCursorClient(out cursorX, out cursorY))
            {
                beforeX = cursorX;
                beforeY = cursorY;
            }

            bool sent;
            if (key == ActionKey.LeftSkill)
                sent = ZdhInput.MoveCursorAbsoluteAndMouseDown(screenX, screenY, true);
            else if (key == ActionKey.RightSkill)
                sent = ZdhInput.MoveCursorAbsoluteAndMouseDown(screenX, screenY, false);
            else if (key == ActionKey.Skill1)
                sent = ZdhInput.MoveCursorAbsoluteAndKeyDown(screenX, screenY, Skill1VirtualKey);
            else if (key == ActionKey.Skill2)
                sent = ZdhInput.MoveCursorAbsoluteAndKeyDown(screenX, screenY, Skill2VirtualKey);
            else if (key == ActionKey.Skill3)
                sent = ZdhInput.MoveCursorAbsoluteAndKeyDown(screenX, screenY, Skill3VirtualKey);
            else if (key == ActionKey.Skill4)
                sent = ZdhInput.MoveCursorAbsoluteAndKeyDown(screenX, screenY, Skill4VirtualKey);
            else
                return false;

            if (sent)
                ArmSyntheticCursorWrite(beforeX, beforeY, x, y);
            return sent;
        }

        private bool ActionDownAtSafeCurrentCursor(ActionKey key)
        {
            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground
                || !PointInsideCastArea(Hud.Window.CursorX, Hud.Window.CursorY))
                return false;
            return ActionDown(key);
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
            x = 0;
            y = 0;
            if (Hud == null || Hud.Window == null) return false;
            x = Hud.Window.CursorX;
            y = Hud.Window.CursorY;
            return true;
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


        private void BeginPostInputCursorSettle(int now)
        {
            CaptureUserCursorIntent();
            if (CanHandBackCursorAfterAcceptedInput())
            {
                BeginCursorRestore(now);
                return;
            }

            int settleMs = Math.Max(0, CursorPostInputSettleMs);
            if (settleMs <= 0)
            {
                BeginCursorRestore(now);
                return;
            }

            _cast.Stage = CastStage.PostInputSettle;
            _cast.DueTick = unchecked(now + settleMs);
            if (_cast.RequiresStrafePause)
                RequestDhStrafePause(settleMs + 160);
        }

        private bool CanHandBackCursorAfterAcceptedInput()
        {
            if (!_cast.CursorOwned || !_cast.InputSent)
                return false;

            if (_cast.Kind == CastKind.Multishot && _cast.SawNativeMultishotAnimation)
                return true;
            if (_cast.Kind == CastKind.MarkedForDeath && _cast.SawNativeMfdAnimation)
                return true;

            return _cast.SawCastAnimation;
        }

        private void BeginCursorRestore(int now)
        {
            CaptureUserCursorIntent();
            PrepareIntentRestoreTarget();
            _cast.RestoreWriteSent = false;
            _cast.RestoreRescueAttempted = false;
            _cast.Stage = CastStage.Restore;
            _cast.DueTick = int.MinValue;
            // The Restore stage itself is the one following collection-frame observation.
            // Do not hold movement waiting for an exact native cursor acknowledgement.
            RequestDhStrafePause(160);
            _lastRestoreConfirmed = false;

            if (!_cast.CursorOwned)
            {
                CompleteCursorRestore(now, true);
                return;
            }

            _cast.RestoreWriteSent = SetCastCursor(_cast.RestoreX, _cast.RestoreY);
            if (!_cast.RestoreWriteSent)
            {
                SetCursorSafetyBlock(now, _cast.RestoreX, _cast.RestoreY);
                CompleteCursorRestore(now, false);
            }
        }

        private void AdvanceCursorRestore(int now)
        {
            if (!_cast.CursorOwned)
            {
                CompleteCursorRestore(now, true);
                return;
            }

            bool nativeAck = _cast.RestoreWriteSent
                && IsCursorNear(_cast.RestoreX, _cast.RestoreY, CursorRestoreTolerancePixels);
            if (nativeAck)
            {
                CompleteCursorRestore(now, true);
                return;
            }

            // Normally a non-ack can simply be real user movement. Retry only the pathological
            // case where the first restore write leaves the native cursor near the synthetic aim.
            int cursorX;
            int cursorY;
            bool haveCursor = TryGetCursorClient(out cursorX, out cursorY);
            int restoreDistance = haveCursor
                ? CursorDistance(cursorX, cursorY, _cast.RestoreX, _cast.RestoreY) : 0;
            int aimDistance = haveCursor
                ? CursorDistance(cursorX, cursorY, _cast.AimX, _cast.AimY) : int.MaxValue;
            bool syntheticAimResidue = !_cast.RestoreRescueAttempted
                && _cast.RestoreWriteSent
                && haveCursor
                && restoreDistance >= Math.Max(80, CursorRestoreRescueDistancePixels)
                && (long)aimDistance * 2L + 40L < restoreDistance;

            if (syntheticAimResidue)
            {
                _cast.RestoreRescueAttempted = true;
                bool rescueSent = SetCursorClient(_cast.RestoreX, _cast.RestoreY);
                if (!rescueSent)
                {
                    SetCursorSafetyBlock(now, _cast.RestoreX, _cast.RestoreY);
                    CompleteCursorRestore(now, false);
                    return;
                }

                RequestDhStrafePause(160);
                return;
            }

            CompleteCursorRestore(now, false);
        }

        private static int CursorDistance(int x1, int y1, int x2, int y2)
        {
            long dx = x1 - x2;
            long dy = y1 - y2;
            return (int)Math.Min(int.MaxValue, Math.Round(Math.Sqrt(dx * dx + dy * dy)));
        }

        private void CompleteCursorRestore(int now, bool nativeConfirmed)
        {
            _lastRestoreConfirmed = nativeConfirmed;
            _cast.CursorOwned = false;
            if (!nativeConfirmed && !_cast.RestoreWriteSent)
            {
                BeginVerificationAfterRestore(now);
                return;
            }

            // The Restore stage already provided one full post-write observation frame.
            // Hold longer only when the cast's existing minimum lease has not yet elapsed.
            int minimumUntil = unchecked(_cast.StartedTick + Math.Max(1, _cast.MinimumLeaseMs));
            if (unchecked(minimumUntil - now) > 0)
            {
                _cast.Stage = CastStage.RestoreSettle;
                _cast.DueTick = minimumUntil;
                RequestDhStrafePause(Math.Max(80, unchecked(minimumUntil - now) + 80));
                return;
            }

            BeginVerificationAfterRestore(now);
        }

        private void BeginVerificationAfterRestore(int now)
        {
            if ((_lastRestoreConfirmed || _cast.RestoreWriteSent)
                && _cursorSafetyBlocked)
                ClearCursorSafetyBlock();

            if (_cast.CancellationPending)
            {
                string reason = _cast.CancellationReason;
                FinalizeCancelledCast(reason, now, _lastRestoreConfirmed
                    || _cast.RestoreWriteSent);
                return;
            }

            if (_cast.SentryBurstChild && _sentryBurst.Mode != SentryBurstMode.None)
            {
                RequestDhStrafePause(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                SuppressDhStrafePrimary(Math.Max(80, RemainingSentryBurstMs(now) + 80));
                _cast.Stage = CastStage.Verify;
                _cast.VerifyUntilTick = _cast.InputDownTick == int.MinValue
                    ? unchecked(now + Math.Max(1, _cast.VerifyMs))
                    : unchecked(_cast.InputDownTick + Math.Max(1, _cast.VerifyMs));
                return;
            }

            if (!_cast.ManualDebuff)
                RecordCombatActionCompleted(now);
            int verifyUntilTick = _cast.InputDownTick == int.MinValue
                ? unchecked(now + Math.Max(1, _cast.VerifyMs))
                : unchecked(_cast.InputDownTick + Math.Max(1, _cast.VerifyMs));
            int primaryQuietMs = !_cast.ManualDebuff && _cast.RequiresStrafePause
                ? GetPostCastPrimaryQuietMs(_cast.Kind) : 0;
            if (_cast.Kind == CastKind.MarkedForDeath && _cast.InputSent)
            {
                // Keep only Primary suppressed until Valley verification finishes. Strafe/movement
                // is already handed back; this closes the race where a Momentum Primary could
                // begin before the short native MFD actor/effect observation window completed.
                int verifyRemainingMs = Math.Max(0, unchecked(verifyUntilTick - now));
                primaryQuietMs = Math.Max(primaryQuietMs, verifyRemainingMs + 20);
            }
            if (primaryQuietMs > 0) SuppressDhStrafePrimary(primaryQuietMs);
            ReleaseStandstillInput();

            ReleaseDhStrafePause();
            // Movement is handed back before activation observation. MFD alone keeps Primary
            // suppressed through its short verify window; gameplay-effect validation otherwise
            // remains asynchronous and independent from movement.
            _cast.Stage = CastStage.Verify;
            _cast.VerifyUntilTick = verifyUntilTick;
        }

        private bool RestoreCursorImmediately()
        {
            if (!_cast.CursorOwned || Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return true;

            CaptureUserCursorIntent();
            PrepareIntentRestoreTarget();
            bool sent = SetCastCursor(_cast.RestoreX, _cast.RestoreY);
            _cast.RestoreWriteSent = sent;
            _lastRestoreConfirmed = sent;
            if (!sent)
                SetCursorSafetyBlock(Environment.TickCount, _cast.RestoreX, _cast.RestoreY);
            return sent;
        }

        private void InitializeCursorIntent()
        {
            _cast.CursorReferenceX = _cast.SavedCursorX;
            _cast.CursorReferenceY = _cast.SavedCursorY;
            _cast.CursorReferenceValid = true;
            _cast.UserCursorDeltaX = 0;
            _cast.UserCursorDeltaY = 0;
            _cast.CursorSyntheticWritePending = false;
            _cast.CursorSyntheticFromX = _cast.SavedCursorX;
            _cast.CursorSyntheticFromY = _cast.SavedCursorY;
            _cast.CursorSyntheticTargetX = _cast.SavedCursorX;
            _cast.CursorSyntheticTargetY = _cast.SavedCursorY;
            _cast.CursorSyntheticEchoRejectCount = 0;
        }

        private bool SetCastAimCursor(int x, int y)
        {
            int beforeX = _cast.CursorReferenceValid ? _cast.CursorReferenceX : x;
            int beforeY = _cast.CursorReferenceValid ? _cast.CursorReferenceY : y;
            int cursorX;
            int cursorY;
            if (TryGetCursorClient(out cursorX, out cursorY))
            {
                beforeX = cursorX;
                beforeY = cursorY;
            }

            bool sent = SetCursorClient(x, y);
            if (sent)
                ArmSyntheticCursorWrite(beforeX, beforeY, x, y);
            return sent;
        }

        private bool SetCastCursor(int x, int y)
        {
            bool sent = SetCursorClient(x, y);
            if (sent)
            {
                _cast.CursorReferenceX = x;
                _cast.CursorReferenceY = y;
                _cast.CursorReferenceValid = true;
                _cast.CursorSyntheticWritePending = false;
            }
            return sent;
        }

        private void ArmSyntheticCursorWrite(int fromX, int fromY, int targetX, int targetY)
        {
            _cast.CursorSyntheticWritePending = true;
            _cast.CursorSyntheticFromX = fromX;
            _cast.CursorSyntheticFromY = fromY;
            _cast.CursorSyntheticTargetX = targetX;
            _cast.CursorSyntheticTargetY = targetY;
            _cast.CursorReferenceX = targetX;
            _cast.CursorReferenceY = targetY;
            _cast.CursorReferenceValid = true;
        }

        private void AddTrustedCursorDelta(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            long totalX = (long)_cast.UserCursorDeltaX + dx;
            long totalY = (long)_cast.UserCursorDeltaY + dy;
            _cast.UserCursorDeltaX = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, totalX));
            _cast.UserCursorDeltaY = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, totalY));
        }

        private void CaptureUserCursorIntent()
        {
            if (!_cast.CursorOwned || !_cast.CursorReferenceValid) return;
            int cursorX;
            int cursorY;
            if (!TryGetCursorClient(out cursorX, out cursorY)) return;

            if (_cast.CursorSyntheticWritePending)
            {
                int tolerance = Math.Max(4, CursorSyntheticEchoTolerancePixels);
                int targetDistance = CursorDistance(cursorX, cursorY,
                    _cast.CursorSyntheticTargetX, _cast.CursorSyntheticTargetY);
                int fromDistance = CursorDistance(cursorX, cursorY,
                    _cast.CursorSyntheticFromX, _cast.CursorSyntheticFromY);

                if (targetDistance <= tolerance)
                {
                    // The absolute synthetic warp is now observable. Establish a clean physical
                    // reference at that point; the warp itself is never user steering.
                    _cast.CursorSyntheticWritePending = false;
                    _cast.CursorReferenceX = cursorX;
                    _cast.CursorReferenceY = cursorY;
                    return;
                }

                if (fromDistance <= tolerance)
                {
                    // FREEHUD/Windows can expose the pre-warp cursor for one collection frame.
                    // Reject that echo and wait for either the synthetic target or real movement.
                    _cast.CursorSyntheticEchoRejectCount++;
                    return;
                }

                // If the observed point is materially closer to the synthetic target, the warp
                // landed and the remaining displacement is genuine user steering after it.
                if (targetDistance + tolerance < fromDistance)
                {
                    AddTrustedCursorDelta(
                        cursorX - _cast.CursorSyntheticTargetX,
                        cursorY - _cast.CursorSyntheticTargetY);
                    _cast.CursorSyntheticWritePending = false;
                    _cast.CursorReferenceX = cursorX;
                    _cast.CursorReferenceY = cursorY;
                    return;
                }

                // Otherwise the user moved while the pre-warp cursor was still being observed.
                // Preserve that physical movement, but keep waiting so the later synthetic warp
                // cannot be mistaken for another user gesture.
                AddTrustedCursorDelta(
                    cursorX - _cast.CursorSyntheticFromX,
                    cursorY - _cast.CursorSyntheticFromY);
                _cast.CursorSyntheticFromX = cursorX;
                _cast.CursorSyntheticFromY = cursorY;
                return;
            }

            int dx = cursorX - _cast.CursorReferenceX;
            int dy = cursorY - _cast.CursorReferenceY;
            AddTrustedCursorDelta(dx, dy);
            _cast.CursorReferenceX = cursorX;
            _cast.CursorReferenceY = cursorY;
        }

        private void PrepareIntentRestoreTarget()
        {
            Size size = Hud.Window.Size;
            long targetX = (long)_cast.SavedCursorX + _cast.UserCursorDeltaX;
            long targetY = (long)_cast.SavedCursorY + _cast.UserCursorDeltaY;

            // Trusted physical steering is restored at full magnitude. Only the actual window
            // boundary clamps the endpoint; there is no fixed-radius or ratio-based damping.
            int maximumX = Math.Max(0, size.Width - 1);
            int maximumY = Math.Max(0, size.Height - 1);
            _cast.RestoreX = (int)Math.Max(0, Math.Min(maximumX, targetX));
            _cast.RestoreY = (int)Math.Max(0, Math.Min(maximumY, targetY));
        }

        private bool SetCursorClient(int x, int y)
        {
            if (!PointInsideWindow(x, y) || !Hud.Window.IsForeground) return false;
            int screenX;
            int screenY;
            return TryClientToScreen(x, y, out screenX, out screenY)
                && ZdhInput.MoveCursorAbsolute(screenX, screenY);
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

        private bool IsClickDangerUi(float x, float y)
        {
            // Deliberately mirrors the proven HUD Menu AutoSnap safety model.
            // Do not cache broad/root IUiElement rectangles here: some FreeHUD roots
            // can expose layout-sized rectangles that are not actual click targets.
            return IsInsideExplicitClickGuard(x, y) || IsInsidePlayerPortraitFace(x, y);
        }

        private bool IsInsideExplicitClickGuard(float x, float y)
        {
            try
            {
                Size size = Hud.Window.Size;
                if (size.Width <= 0 || size.Height <= 0) return false;
                for (int i = 0; i < ClickGuardRects1920x1080.Length; i++)
                {
                    RectangleF r = ScaleClickGuardRect(ClickGuardRects1920x1080[i], size);
                    if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                        return true;
                }
            }
            catch { return false; }
            return false;
        }

        private static RectangleF ScaleClickGuardRect(RectangleF source, Size size)
        {
            float scale = Math.Min(size.Width / ClickGuardReferenceWidth, size.Height / ClickGuardReferenceHeight);
            if (scale <= 0f) return RectangleF.Empty;

            float extraX = size.Width - ClickGuardReferenceWidth * scale;
            float extraY = size.Height - ClickGuardReferenceHeight * scale;
            float centerX = source.Left + source.Width * 0.5f;
            float centerY = source.Top + source.Height * 0.5f;
            float offsetX = centerX < ClickGuardReferenceWidth / 3f ? 0f
                : centerX > ClickGuardReferenceWidth * 2f / 3f ? extraX : extraX * 0.5f;
            float offsetY = centerY < ClickGuardReferenceHeight / 3f ? 0f
                : centerY > ClickGuardReferenceHeight * 2f / 3f ? extraY : extraY * 0.5f;

            return new RectangleF(source.Left * scale + offsetX, source.Top * scale + offsetY,
                source.Width * scale, source.Height * scale);
        }

        private bool IsInsidePlayerPortraitFace(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Players == null) return false;
                foreach (IPlayer player in Hud.Game.Players)
                {
                    try
                    {
                        if (player == null || !player.IsInGame || player.PortraitUiElement == null
                            || !player.PortraitUiElement.Visible) continue;
                        RectangleF rect = player.PortraitUiElement.Rectangle;
                        if (rect.Width <= 0f || rect.Height <= 0f) continue;
                        if (x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom)
                            return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        private bool PointInsideCastArea(float x, float y)
        {
            if (Hud == null || Hud.Window == null) return false;
            Size size = Hud.Window.Size;
            float bottom = Math.Min(size.Height - 140f, size.Height * GroundCastSafeBottomRatio);
            return x >= 24f && y >= 24f && x < size.Width - 24f && y < bottom
                && !IsClickDangerUi(x, y);
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
            private const uint MouseMove = 0x0001;
            private const uint MouseVirtualDesk = 0x4000;
            private const uint MouseAbsolute = 0x8000;
            private const uint KeyUpFlag = 0x0002;
            private const int SmXVirtualScreen = 76;
            private const int SmYVirtualScreen = 77;
            private const int SmCxVirtualScreen = 78;
            private const int SmCyVirtualScreen = 79;

            [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public UNION Data; }
            [StructLayout(LayoutKind.Explicit)] private struct UNION { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
            [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public IntPtr Extra; }
            [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }
            [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
            [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
            [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);

            public static bool MoveCursorAbsolute(int screenX, int screenY)
            {
                INPUT move;
                if (!TryBuildAbsoluteMove(screenX, screenY, out move)) return false;
                return SendInput(1, new[] { move }, Marshal.SizeOf(typeof(INPUT))) == 1;
            }
            public static bool MoveCursorAbsoluteAndKeyDown(int screenX, int screenY, ushort vk)
            {
                if (vk == 0) return false;
                INPUT move;
                if (!TryBuildAbsoluteMove(screenX, screenY, out move)) return false;
                var input = new[]
                {
                    move,
                    new INPUT
                    {
                        Type = InputKeyboard,
                        Data = new UNION { Keyboard = new KEYBDINPUT { Vk = vk } }
                    }
                };
                return SendInput(2, input, Marshal.SizeOf(typeof(INPUT))) == 2;
            }

            public static bool MoveCursorAbsoluteAndMouseDown(int screenX, int screenY, bool leftButton)
            {
                INPUT move;
                if (!TryBuildAbsoluteMove(screenX, screenY, out move)) return false;
                var input = new[]
                {
                    move,
                    new INPUT
                    {
                        Type = InputMouse,
                        Data = new UNION
                        {
                            Mouse = new MOUSEINPUT { Flags = leftButton ? MouseLeftDown : MouseRightDown }
                        }
                    }
                };
                return SendInput(2, input, Marshal.SizeOf(typeof(INPUT))) == 2;
            }

            private static bool TryBuildAbsoluteMove(int screenX, int screenY, out INPUT input)
            {
                input = new INPUT();
                int left = GetSystemMetrics(SmXVirtualScreen);
                int top = GetSystemMetrics(SmYVirtualScreen);
                int width = GetSystemMetrics(SmCxVirtualScreen);
                int height = GetSystemMetrics(SmCyVirtualScreen);
                if (width <= 1 || height <= 1) return false;

                double nx = (screenX - left) * 65535.0 / (width - 1);
                double ny = (screenY - top) * 65535.0 / (height - 1);
                int absoluteX = (int)Math.Round(Math.Max(0.0, Math.Min(65535.0, nx)));
                int absoluteY = (int)Math.Round(Math.Max(0.0, Math.Min(65535.0, ny)));
                input = new INPUT
                {
                    Type = InputMouse,
                    Data = new UNION
                    {
                        Mouse = new MOUSEINPUT
                        {
                            X = absoluteX,
                            Y = absoluteY,
                            Flags = MouseMove | MouseAbsolute | MouseVirtualDesk
                        }
                    }
                };
                return true;
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
