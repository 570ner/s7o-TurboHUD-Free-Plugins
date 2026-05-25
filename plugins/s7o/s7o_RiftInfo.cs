using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // s7o_RiftInfo.cs — TurboHUD Free 1.4.3.0
    //
    // Owns rift progression prediction, rift timers, boss timer, and a safe
    // Bane of the Stricken estimator for selected/hovered elite targets.
    //
    // Nearby RP uses native monster body RiftProgression values plus estimated
    // elite purple-orb progression. Stricken is an estimator based on proc edges;
    // it is not a native exact per-monster stack read.

    public class s7o_RiftInfo : BasePlugin, IInGameTopPainter, IAfterCollectHandler
    {
        // ── Display ──────────────────────────────────────────────────────
        public bool ShowNearbyRP { get; set; } = true;
        public bool ShowBossTimer { get; set; } = true;
        public bool ShowElapsedRiftTimer { get; set; } = true;

        // Completed-rift summary should survive town/loading flicker and clear only
        // after Urshi/reward state has disappeared for a short grace period.
        public int CompletedRiftTownClearGraceTicks { get; set; } = 180;

        // Timer row layout.
        // Draw below the GR percent text so Boss/Elapsed cannot overlap the purple 100.0% label.
        public float RiftTimerBelowBarOffsetY { get; set; } = 2.0f;
        public float RiftTimerHorizontalPadding { get; set; } = 4.0f;
        public float RiftTimerMinGap { get; set; } = 12.0f;

        // After the boss dies and the GR progress bar is gone, draw the frozen timer row
        // near the old bar's top instead of below the old bar, reducing the large gap.
        public float RiftSummaryTimerTopOffsetY { get; set; } = 2.0f;

        // Display-only correction for FreeHUD tick/format rounding so elapsed matches the native timer.
        public int RiftElapsedDisplayOffsetTicks { get; set; } = 59;
        public bool ShowStricken { get; set; } = true;
        public bool ShowStrickenOnAnySelectedElite { get; set; } = true;

        // ── Nearby RP ────────────────────────────────────────────────────
        public int NearbyRangeYards { get; set; } = 40;

        // Native body progression is always read from monster.SnoMonster.RiftProgression.
        // Purple orb progression is estimated because the orbs do not exist until death.
        public double ProgressOrbPct { get; set; } = 1.0;

        // Base game values.
        public double BlueChampionBaseOrbCount { get; set; } = 3.0;
        public double YellowRareBaseOrbCount { get; set; } = 4.0;

        // Current altar/reaper behavior.
        // Enabled by default because current seasonal/endgame expectation is extra elite orb.
        public bool AssumeReaperSealExtraOrb { get; set; } = true;
        public double ReaperSealExtraOrbCount { get; set; } = 1.0;

        // false = rare minion body progression goes into TrashRP.
        // true = rare minion body progression goes into EliteRP.
        public bool CountRareMinionBodyAsEliteRP { get; set; } = true;

        // ── Alert behavior ───────────────────────────────────────────────
        public bool ShowSpawnAlert { get; set; } = true;
        public double SpawnAlertPulseSpeed { get; set; } = 10.0;

        // ── Stricken estimator ───────────────────────────────────────────
        // Names are intentionally hidden; the stack counter is drawn below the icon instead.
        public bool ShowStrickenPlayerNames { get; set; } = false;

        // For testing, show the icon even at 0 so we can confirm target/player detection.
        // Later stable release can set this false if desired.
        public bool ShowStrickenZeroStacks { get; set; } = true;

        // For party testing, show remote Stricken players even before their first estimated stack.
        public bool ShowRemoteStrickenZeroStacks { get; set; } = true;

        // Larger icon. Counter is drawn below the icon so it does not cover the gem texture.
        public int StrickenIconSize { get; set; } = 34;

        // Horizontal spacing between player icons.
        public int StrickenIconGap { get; set; } = 7;

        // 18 overlapped the top HP bar in live testing. Keep this clearly beside the bar.
        public int StrickenBarOffsetX { get; set; } = 60;

        // Existing local-only mode can still be enabled, but default now allows party display.
        public bool TrackOnlyLocalPlayerStricken { get; set; } = false;

        // Remote party players cannot be safely gated by the local player's animation.
        // This counts remote Stricken proc rising edges against the current tracked target.
        public bool UseProcEdgeEstimatorForRemotePlayers { get; set; } = true;

        // Prevent remote proc flicker/double-counts.
        public int RemoteStrickenMinAcceptedStackIntervalTicks { get; set; } = 25;

        // Exact AcdId does not work for this setup; FreeHUD reports real damage through AnnId.
        public bool StrictDirectPlayerAcdOnly { get; set; } = false;

        // Allow AnnId only when the local player was recently attacking/casting/channeling.
        public bool AllowAnnIdDamageWhenLocalAttackRecent { get; set; } = true;

        // This remains mandatory. It blocks follower-only idle false counts.
        public bool RequireLocalAttackAnimationForStricken { get; set; } = true;

        // Stricken proc/damage timing can lag well after animation sampling.
        public int StrickenRecentLocalAttackTicks { get; set; } = 45;

        // AnnId attribution must be close to a verified attack/cast/channel animation.
        public int StrickenAnnIdMaxLocalAttackAgeTicks { get; set; } = 45;

        // Conservative accepted-stack cooldown to block flicker/double-counting.
        public int StrickenMinAcceptedStackIntervalTicks { get; set; } = 25;

        // Search enough nearby monsters to catch density around the player/target.
        public int StrickenCandidateRangeYards { get; set; } = 80;

        // Do not use pre-proc damage. It can pair old follower damage with a new proc.
        public int StrickenPreProcDamageTicks { get; set; } = 0;

        // Keep short, but slightly more forgiving than the strict direct-only build.
        public int StrickenPostProcDamageTicks { get; set; } = 10;

        // If two player-sourced monsters are damaged on the same first tick,
        // reject the attribution instead of guessing.
        public bool RejectAmbiguousStrickenFirstHits { get; set; } = true;

        // Selected monster can flicker null briefly while the top HP bar still belongs
        // to the same target. Keep a tiny cache so proc/damage attribution survives it.
        public int StrickenTargetCacheTicks { get; set; } = 15;

        // During Rift Guardian phase, keep the last boss target identity through town/load transitions.
        public int StrickenBossTargetCacheTicks { get; set; } = 7200;

        // Dedicated Stricken debug. Enable manually for diagnostics.
        public bool EnableStrickenDebug { get; set; } = false;
        public int StrickenDebugIntervalTicks { get; set; } = 30;

        // Correct native proc pulse discovered from debug logs.
        // Primary 428348 index 2 toggles; primary index 0 is always active/equipped state.
        public int StrickenProcIconIndex { get; set; } = 2;

        // Diagnostic fallback only. Disabled now that the real proc pulse was identified.
        public bool EnableStrickenDamageOnlyTestFallback { get; set; } = false;

        // About 0.33s at 60 ticks/sec, only used if the diagnostic fallback is manually enabled.
        public int StrickenDamageOnlyFallbackCooldownTicks { get; set; } = 20;

        // ── Debug logging ────────────────────────────────────────────────
        public bool EnableRiftInfoDebug { get; set; } = false;
        public int DebugLogIntervalTicks { get; set; } = 120;

        // ── Fonts / textures ─────────────────────────────────────────────
        public IFont BarPctFont { get; set; }
        public IFont RpFont { get; set; }
        public IFont TimeFont { get; set; }
        public IFont BossFont { get; set; }

        private IFont RpYellowFont;
        private IFont RpRedFont;
        private IFont RpOrangeFont;
        private IFont RpPurpleFont;

        private IFont AlertYellowBrightFont;
        private IFont AlertYellowDimFont;
        private IFont AlertRedBrightFont;
        private IFont AlertRedDimFont;
        private IFont AlertOrangeBrightFont;
        private IFont AlertOrangeDimFont;
        private IFont AlertPurpleBrightFont;
        private IFont AlertPurpleDimFont;

        private IFont StrickenFont;
        private IFont StrickenSmallFont;
        private ITexture StrickenTexture;

        // ── Private state ────────────────────────────────────────────────
        private bool _bossActive = false;
        private int _bossStartTick = 0;
        private int _lastGuardTick = 0;

        private bool _riftSessionActive = false;
        private bool _riftSummaryActive = false;
        private int _riftStartTick = 0;
        private int _riftCompleteTick = 0;

        private bool _bossSeenThisRift = false;
        private int _bossEndTick = 0;

        private RectangleF _lastGreaterRiftBarRect = RectangleF.Empty;
        private RectangleF _summaryGreaterRiftBarRect = RectangleF.Empty;

        private bool _summaryUrshiSeenInTown = false;
        private int _summaryUrshiMissingInTownSinceTick = 0;

        private double _killRate = 0;
        private double _prevPct = 0;
        private int _prevPctTick = 0;

        private int _lastDebugTick = 0;
        private int _lastSeenGameTick = 0;
        private bool _strickenDebugStarted = false;

        private IMonster _lastValidStrickenTarget = null;
        private int _lastValidStrickenTargetKey = 0;
        private int _lastValidStrickenTargetTick = 0;
        private string _lastValidStrickenTargetName = "";

        private int _lastLocalAttackTick = 0;
        private string _lastLocalAttackState = "";
        private string _lastLocalAttackAnimation = "";
        private int _lastLocalAttackRejectDebugTick = 0;

        private const uint StrickenSno = 428348u;
        private const uint StrickenSecondarySno = 428349u;

        private readonly Dictionary<uint, StrickenProcState> _strickenProcByHero =
            new Dictionary<uint, StrickenProcState>();

        private readonly Dictionary<string, int> _strickenStacksByHeroAndTarget =
            new Dictionary<string, int>();


        private readonly Dictionary<int, StrickenGlobalDamageSample> _strickenGlobalDamageSamples =
            new Dictionary<int, StrickenGlobalDamageSample>();

        private class NearbyRpSnapshot
        {
            public double TrashBodyPct;
            public double EliteBodyPct;
            public double EliteOrbPct;

            public int BluePacks;
            public int YellowPacks;
            public int RareMinions;

            public double TrashRP { get { return TrashBodyPct; } }
            public double EliteRP { get { return EliteBodyPct + EliteOrbPct; } }
            public double TotalRP { get { return TrashRP + EliteRP; } }
        }

        private class StrickenProcState
        {
            public bool LastProcActive;
            public int LastProcTick;
            public PendingStrickenProc PendingProc;
            public int LastDamageOnlyFallbackTick;
            public int LastAcceptedStackTick;

            // For remote-party proc-edge estimator.
            public int LastRemoteAcceptedStackTick;
        }

        private class PendingStrickenProc
        {
            public bool Active;
            public int ProcTick;
            public int LockedTargetKey;
            public string LockedTargetName;

            public int LocalAttackTick;
            public bool LocalAttackRecentAtProc;
            public string LocalAttackState;
            public string LocalAttackAnimation;
        }

        private class StrickenGlobalDamageSample
        {
            public int TargetKey;
            public IMonster Monster;
            public string Name;
            public string Code;
            public ActorRarity Rarity;
            public double LastHealth;
            public int LastSeenTick;
            public int LastDamageTick;
            public double LastDamageAmount;
            public uint LastDamageAcd;
            public uint LastAcdAttackedBy;
            public uint LastDamageMainActor;
        }


        public s7o_RiftInfo()
        {
            Enabled = true;
            Order = 29500;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            BarPctFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 200, 255, 200, true, false, 160, 0, 0, 0, true);
            RpFont = Hud.Render.CreateFont("tahoma", 9.0f, 255, 220, 230, 220, true, false, 165, 0, 0, 0, true);
            // Use the same orange theme for both rift timers.
            TimeFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 255, 160, 60, true, false, 160, 0, 0, 0, true);
            BossFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 255, 160, 60, true, false, 160, 0, 0, 0, true);

            RpYellowFont = Hud.Render.CreateFont("tahoma", 9.0f, 255, 255, 220, 50, true, false, 170, 0, 0, 0, true);
            RpRedFont = Hud.Render.CreateFont("tahoma", 9.0f, 255, 255, 70, 50, true, false, 170, 0, 0, 0, true);
            RpOrangeFont = Hud.Render.CreateFont("tahoma", 9.0f, 255, 255, 150, 35, true, false, 170, 0, 0, 0, true);
            RpPurpleFont = Hud.Render.CreateFont("tahoma", 9.0f, 255, 190, 90, 255, true, false, 170, 0, 0, 0, true);

            AlertYellowBrightFont = Hud.Render.CreateFont("tahoma", 8.2f, 255, 255, 230, 40, true, false, 180, 0, 0, 0, true);
            AlertYellowDimFont = Hud.Render.CreateFont("tahoma", 8.2f, 150, 255, 230, 40, true, false, 130, 0, 0, 0, true);

            AlertRedBrightFont = Hud.Render.CreateFont("tahoma", 8.2f, 255, 255, 65, 50, true, false, 180, 0, 0, 0, true);
            AlertRedDimFont = Hud.Render.CreateFont("tahoma", 8.2f, 150, 255, 65, 50, true, false, 130, 0, 0, 0, true);

            AlertOrangeBrightFont = Hud.Render.CreateFont("tahoma", 8.2f, 255, 255, 150, 35, true, false, 180, 0, 0, 0, true);
            AlertOrangeDimFont = Hud.Render.CreateFont("tahoma", 8.2f, 150, 255, 150, 35, true, false, 130, 0, 0, 0, true);
            AlertPurpleBrightFont = Hud.Render.CreateFont("tahoma", 8.2f, 255, 210, 120, 255, true, false, 180, 0, 0, 0, true);
            AlertPurpleDimFont = Hud.Render.CreateFont("tahoma", 8.2f, 150, 210, 120, 255, true, false, 130, 0, 0, 0, true);

            // White counter drawn over the gem icon.
            StrickenFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 255, 255, 255, true, false, 220, 0, 0, 0, true);

            // Small optional player index/name if ever needed; not used by default.
            StrickenSmallFont = Hud.Render.CreateFont("tahoma", 7.0f, 255, 255, 230, 120, true, false, 180, 0, 0, 0, true);

            StrickenTexture = Hud.Texture.GetItemTexture(Hud.Sno.SnoItems.Unique_Gem_018_x1);
        }

        // ── AfterCollect ─────────────────────────────────────────────────
        public void AfterCollect()
        {
            // Preserve completed-rift timer state through transient loading/HUD invalid frames.
            if (Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.Me == null)
            {
                if (_riftSummaryActive || _riftSessionActive)
                    return;

                ResetVolatileState(false);
                return;
            }

            if (EnableStrickenDebug && !_strickenDebugStarted)
            {
                _strickenDebugStarted = true;
                Hud.TextLog.Log(
                    "s7o_RiftInfo_StrickenDebug",
                    "Stricken debug started. Look for this file in TurboHUD/FreeHUD logs. " +
                    "Primary=428348 Secondary=428349 HybridAnnIdLocalAttack=True LocalAttackGate=True");
            }

            int tick = Hud.Game.CurrentGameTick;

            if (_lastSeenGameTick > 0 && tick < _lastSeenGameTick)
            {
                // Do not destroy an active/completed GR timer state during boss-death,
                // town, or loading transitions where FreeHUD tick reporting can briefly roll back.
                if (!_riftSessionActive && !_riftSummaryActive)
                    ResetVolatileState(false);
            }

            _lastSeenGameTick = tick;

            bool inGR = Hud.Game.Me.InGreaterRift;
            bool inNR = !inGR && Hud.Game.RiftPercentage > 0 && !Hud.Game.IsInTown;

            // If a stale completed summary survived into the next GR, clear it immediately
            // so elapsed/boss timers do not block the next run's RP panel.
            if (HasNewGreaterRiftStartedWhileSummaryActive())
            {
                ResetVolatileState(false);
                inGR = Hud.Game.Me.InGreaterRift;
                inNR = !inGR && Hud.Game.RiftPercentage > 0 && !Hud.Game.IsInTown;
            }

            if (inGR)
            {
                EnsureGreaterRiftSession(tick);
                UpdateKillRate(tick);

                if (!_riftSummaryActive && IsGreaterRiftGuardianPhase())
                    StartBossTimerIfNeeded(tick, "guardian-phase");

                // Freeze only from native dead/reward signals.
                // Do not freeze merely because AliveMonsters temporarily lost the boss.
                if (!_riftSummaryActive && _bossStartTick > 0 && IsGreaterRiftGuardianDead())
                {
                    FreezeCompletedRiftTimers(tick, "guardian-dead-native");
                }
                else
                {
                    UpdateBossState(tick);
                }

                // Additional completion signal in case the alive->dead transition is missed.
                if (!_riftSummaryActive && _bossStartTick > 0 && IsUrshiVisible())
                    FreezeCompletedRiftTimers(tick, "urshi-visible-in-gr");

                if (ShowStricken)
                    UpdateStrickenEstimator(tick);

                return;
            }

            // Completion signal outside GR. Do not freeze simply because the player left the GR
            // after boss phase started; that can happen during town portals. Freeze only when
            // Urshi/reward state is visible.
            if (!_riftSummaryActive &&
                _riftSessionActive &&
                _bossStartTick > 0 &&
                (IsUrshiVisible() || IsGreaterRiftGuardianDead()))
            {
                FreezeCompletedRiftTimers(tick, IsUrshiVisible()
                    ? "urshi-visible-outside-gr"
                    : "guardian-dead-outside-gr");
                return;
            }

            if (_riftSummaryActive)
            {
                if (!IsCompletedRiftSummaryStillValid(tick))
                    ResetVolatileState(false);

                return;
            }

            if (inNR)
            {
                UpdateKillRate(tick);
                return;
            }

            // Do not immediately destroy an active GR session during the short transition after
            // boss death. If Urshi appears, the block above freezes the timers.
            if (_riftSessionActive && _bossStartTick > 0)
                return;

            ResetVolatileState(false);
        }

        private int ReadNativeGreaterRiftStartTick()
        {
            try
            {
                if (Hud.Game != null)
                    return Hud.Game.CurrentTimedEventStartTick;
            }
            catch
            {
            }

            return 0;
        }


        private uint GetGreaterRiftQuestStepId()
        {
            try
            {
                if (Hud.Game == null || Hud.Game.Quests == null)
                    return 0;

                var quest = Hud.Game.Quests.FirstOrDefault(q =>
                    q != null &&
                    q.SnoQuest != null &&
                    q.SnoQuest.Sno == 382695);

                return quest != null ? quest.QuestStepId : 0;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsGreaterRiftGuardianPhase()
        {
            uint step = GetGreaterRiftQuestStepId();

            // LightningMOD-style native signal:
            // step 16 means the Greater Rift Guardian phase is active.
            if (step == 16)
                return true;

            // Fallback for HUD states where RiftPercentage still reports completion directly.
            try
            {
                return Hud.Game != null &&
                       Hud.Game.Me != null &&
                       Hud.Game.Me.InGreaterRift &&
                       Hud.Game.RiftPercentage >= 100.0d;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGreaterRiftGuardianDead()
        {
            try
            {
                if (Hud.Game == null)
                    return false;

                if (Hud.Game.Monsters != null &&
                    Hud.Game.Monsters.Any(m => IsRiftGuardianDeadLike(m)))
                {
                    return true;
                }

                uint step = GetGreaterRiftQuestStepId();

                // LightningMOD-style guardian-dead / reward steps.
                return step == 5 || step == 10 || step == 34 || step == 46;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRiftGuardianDeadLike(IMonster m)
        {
            if (m == null || m.IsAlive || m.SnoMonster == null)
                return false;

            if (m.Rarity == ActorRarity.Boss)
                return true;

            if (m.SnoMonster.Priority == MonsterPriority.boss)
                return true;

            return false;
        }

        private bool HasNewGreaterRiftStartedWhileSummaryActive()
        {
            if (!_riftSummaryActive)
                return false;

            try
            {
                if (Hud.Game == null || Hud.Game.Me == null)
                    return false;

                if (!Hud.Game.Me.InGreaterRift)
                    return false;

                // If we are back in a GR below 100%, this is a new run.
                if (Hud.Game.RiftPercentage < 100.0d)
                    return true;

                int nativeStartTick = ReadNativeGreaterRiftStartTick();

                return nativeStartTick > 0 &&
                       _riftStartTick > 0 &&
                       nativeStartTick != _riftStartTick;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGreaterRiftBarVisible()
        {
            try
            {
                var bar = Hud.Render.GreaterRiftBarUiElement;
                return bar != null && bar.Visible;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureGreaterRiftSession(int tick)
        {
            int nativeStartTick = ReadNativeGreaterRiftStartTick();

            if (!_riftSessionActive)
            {
                _riftSessionActive = true;
                _riftSummaryActive = false;

                _riftStartTick = nativeStartTick > 0 ? nativeStartTick : tick;
                _riftCompleteTick = 0;

                _bossSeenThisRift = false;
                _bossActive = false;
                _bossStartTick = 0;
                _bossEndTick = 0;
            }
            else
            {
                // Keep the timer aligned with the native GR start tick when available.
                // This prevents the custom elapsed timer from lagging behind the native timer.
                if (nativeStartTick > 0)
                    _riftStartTick = nativeStartTick;
                else if (_riftStartTick <= 0)
                    _riftStartTick = tick;
            }
        }
        private void FreezeCompletedRiftTimers(int tick, string reason)
        {
            if (!_riftSessionActive)
                return;

            if (_riftSummaryActive)
                return;

            int nativeStartTick = ReadNativeGreaterRiftStartTick();

            if (nativeStartTick > 0)
                _riftStartTick = nativeStartTick;
            else if (_riftStartTick <= 0)
                _riftStartTick = tick;

            _riftCompleteTick = tick;

            // Boss timer must freeze whenever elapsed freezes.
            // If the boss start was known, lock its end tick unconditionally.
            if (_bossStartTick > 0)
                _bossEndTick = tick;

            // Boss fight is over. Stricken stacks should persist through the fight,
            // but not into the completed-rift / next-rift state.
            ClearStrickenStacks("guardian-dead", tick);
            ClearPendingStrickenAttribution();
            _strickenProcByHero.Clear();

            _bossActive = false;
            _riftSummaryActive = true;

            RectangleF snapshot;
            if (TryReadLiveGreaterRiftBarRect(out snapshot))
                _summaryGreaterRiftBarRect = snapshot;
            else if (!_lastGreaterRiftBarRect.IsEmpty)
                _summaryGreaterRiftBarRect = _lastGreaterRiftBarRect;

            _summaryUrshiSeenInTown = false;
            _summaryUrshiMissingInTownSinceTick = 0;

            if (EnableRiftInfoDebug)
            {
                try
                {
                    Hud.TextLog.Log(
                        "s7o_RiftInfo_Debug",
                        "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                        " phase=freeze-completed-rift reason=" + reason +
                        " riftStart=" + _riftStartTick.ToString(CultureInfo.InvariantCulture) +
                        " riftEnd=" + _riftCompleteTick.ToString(CultureInfo.InvariantCulture) +
                        " bossStart=" + _bossStartTick.ToString(CultureInfo.InvariantCulture) +
                        " bossEnd=" + _bossEndTick.ToString(CultureInfo.InvariantCulture)
                    );
                }
                catch
                {
                }
            }
        }

        private bool IsCompletedRiftSummaryStillValid(int tick)
        {
            if (!_riftSummaryActive)
                return false;

            try
            {
                // Keep through transient invalid/loading frames.
                if (Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.Me == null)
                    return true;

                // If a new GR starts, immediately clear stale frozen timers so RP stats
                // and the normal next-run display can resume.
                if (HasNewGreaterRiftStartedWhileSummaryActive())
                    return false;

                // While still inside the completed rift, keep the frozen summary.
                if (Hud.Game.Me.InGreaterRift)
                    return Hud.Game.RiftPercentage >= 100.0d;

                // Outside town can include rift-to-town loading/transition.
                if (!Hud.Game.IsInTown)
                    return true;

                // In town after the rift is closed, RiftPercentage should reset.
                // This is the strongest simple clear signal.
                if (Hud.Game.RiftPercentage < 1.0d)
                    return false;

                // While the completed GR reward/progress bar is still visible, keep timers.
                if (IsGreaterRiftBarVisible())
                {
                    _summaryUrshiMissingInTownSinceTick = 0;
                    return true;
                }

                // If the GR bar disappeared in town, clear after a short grace window.
                // This avoids clearing on one-frame UI flicker.
                if (_summaryUrshiMissingInTownSinceTick <= 0)
                {
                    _summaryUrshiMissingInTownSinceTick = tick;
                    return true;
                }

                return tick - _summaryUrshiMissingInTownSinceTick < CompletedRiftTownClearGraceTicks;
            }
            catch
            {
                return true;
            }
        }

        private bool IsUrshiVisible()
        {
            try
            {
                return Hud.Game != null &&
                       Hud.Game.Actors != null &&
                       Hud.Game.Actors.Any(actor =>
                           actor != null &&
                           actor.SnoActor != null &&
                           actor.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem &&
                           !actor.IsDisabled);
            }
            catch
            {
                return false;
            }
        }

        

        private void ResetVolatileState(bool clearStrickenStacks)
        {
            _riftSessionActive = false;
            _riftSummaryActive = false;
            _riftStartTick = 0;
            _riftCompleteTick = 0;

            _bossSeenThisRift = false;
            _bossEndTick = 0;

            _lastGreaterRiftBarRect = RectangleF.Empty;
            _summaryGreaterRiftBarRect = RectangleF.Empty;
            _summaryUrshiSeenInTown = false;
            _summaryUrshiMissingInTownSinceTick = 0;

            _bossActive = false;
            _bossStartTick = 0;
            _lastGuardTick = 0;
            _killRate = 0;
            _prevPct = 0;
            _prevPctTick = 0;
            _strickenProcByHero.Clear();
            _lastValidStrickenTarget = null;
            _lastValidStrickenTargetKey = 0;
            _lastValidStrickenTargetTick = 0;
            _lastValidStrickenTargetName = "";
            _lastLocalAttackTick = 0;
            _lastLocalAttackState = "";
            _lastLocalAttackAnimation = "";
            ClearPendingStrickenAttribution();

            if (clearStrickenStacks)
                ClearStrickenStacks("explicit-reset", _lastSeenGameTick);
        }

        private void ClearStrickenStacks(string reason, int tick)
        {
            _strickenStacksByHeroAndTarget.Clear();

            if (EnableStrickenDebug)
            {
                try
                {
                    Hud.TextLog.Log(
                        "s7o_RiftInfo_StrickenDebug",
                        "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                        " phase=stricken-stack-clear reason=" + reason
                    );
                }
                catch
                {
                }
            }
        }

        private void ClearPendingStrickenAttribution()
        {
            _strickenGlobalDamageSamples.Clear();
        }

        private void UpdateKillRate(int tick)
        {
            if (_prevPctTick <= 0)
            {
                _prevPctTick = tick;
                _prevPct = Hud.Game.RiftPercentage;
                return;
            }

            if (tick - _prevPctTick <= 90)
                return;

            double cur = Hud.Game.RiftPercentage;
            int dt = tick - _prevPctTick;

            if (dt > 0 && cur > _prevPct)
            {
                double instant = (cur - _prevPct) / dt * 60.0;
                _killRate = _killRate * 0.7 + instant * 0.3;
            }

            _prevPct = cur;
            _prevPctTick = tick;
        }

        private void StartBossTimerIfNeeded(int tick, string reason)
        {
            if (_bossStartTick > 0 || _riftSummaryActive)
                return;

            _bossStartTick = tick;
            _bossEndTick = 0;
            _bossActive = true;

            if (EnableRiftInfoDebug)
            {
                try
                {
                    Hud.TextLog.Log(
                        "s7o_RiftInfo_Debug",
                        "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                        " phase=boss-timer-start reason=" + reason +
                        " step=" + GetGreaterRiftQuestStepId().ToString(CultureInfo.InvariantCulture) +
                        " rp=" + Hud.Game.RiftPercentage.ToString("F2", CultureInfo.InvariantCulture)
                    );
                }
                catch
                {
                }
            }
        }

        private void UpdateBossState(int tick)
        {
            // Before boss phase/timer starts, scan only once per second.
            // After timer starts, scan every collect tick only to mark visibility/alive state.
            // Missing from AliveMonsters is NOT death.
            if (_bossStartTick <= 0 && tick - _lastGuardTick < 60)
                return;

            _lastGuardTick = tick;

            bool alive = false;

            try
            {
                alive = Hud.Game.AliveMonsters != null &&
                        Hud.Game.AliveMonsters.Any(m => IsRiftGuardianLike(m));
            }
            catch
            {
                alive = false;
            }

            if (alive)
            {
                _bossSeenThisRift = true;
                _bossActive = true;

                // Fallback only. Normal start is IsGreaterRiftGuardianPhase().
                StartBossTimerIfNeeded(tick, "guardian-alive");

                return;
            }

            // Important:
            // Do not freeze here. The boss can disappear from AliveMonsters when out of range,
            // off-screen, teleporting, phased, or not currently collected by FreeHUD.
            // Freezing is handled only by IsGreaterRiftGuardianDead().
        }

        // ── PaintTopInGame ───────────────────────────────────────────────
        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip) return;
            if (Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.Me == null) return;

            bool inGR = Hud.Game.Me.InGreaterRift;
            bool inNR = !inGR && Hud.Game.RiftPercentage > 0 && !Hud.Game.IsInTown;
            bool showCompletedGrSummary = _riftSummaryActive && _riftStartTick > 0;

            if (!inGR && !inNR && !showCompletedGrSummary)
                return;

            try
            {
                if (inGR || showCompletedGrSummary)
                {
                    var bar = Hud.Render.GreaterRiftBarUiElement;

                    if (bar != null && bar.Visible && inGR && !_riftSummaryActive)
                    {
                        _lastGreaterRiftBarRect = bar.Rectangle;

                        DrawBarPercent(bar);

                        if (ShowNearbyRP)
                            DrawRpPanel(bar);
                    }

                    RectangleF barRect;
                    if (TryGetGreaterRiftBarRect(out barRect))
                    {
                        if (ShowElapsedRiftTimer || ShowBossTimer)
                            DrawRiftTimerStack(barRect);
                    }

                    if (inGR && ShowStricken)
                        DrawStrickenOnSelectedMonster();
                }
                else
                {
                    var bar = Hud.Render.NephalemRiftBarUiElement;

                    if (bar != null && bar.Visible && ShowNearbyRP)
                        DrawRpPanel(bar);
                }
            }
            catch
            {
            }
        }

        private void DrawBarPercent(IUiElement bar)
        {
            double pct = Hud.Game.RiftPercentage;
            string text = pct.ToString("F1", CultureInfo.InvariantCulture) + "%";
            var layout = BarPctFont.GetTextLayout(text);

            float x = (float)(bar.Rectangle.Left + bar.Rectangle.Width / 100.0 * Math.Min(pct, 100.0))
                      - layout.Metrics.Width * 0.5f;

            float y = bar.Rectangle.Top - bar.Rectangle.Height * 0.7f - layout.Metrics.Height;

            BarPctFont.DrawText(layout, x, y);
        }

        // ── Nearby RP panel ──────────────────────────────────────────────
        private void DrawRpPanel(IUiElement bar)
        {
            if (Hud.Game.RiftPercentage >= 100.0)
                return;

            NearbyRpSnapshot rp = CalculateNearbyRp();
            if (rp == null || rp.TotalRP < 0.001)
                return;

            double remaining = Math.Max(0.0, 100.0 - Hud.Game.RiftPercentage);

            bool eliteSpawns = rp.EliteRP >= remaining;
            bool trashSpawns = rp.TrashRP >= remaining;
            bool totalSpawns = rp.TotalRP >= remaining;

            SpawnAlertKind alert = GetSpawnAlertKind(eliteSpawns, trashSpawns, totalSpawns);

            float x = bar.Rectangle.Left;
            float y = bar.Rectangle.Bottom + bar.Rectangle.Height * 1.5f;
            float lh = 18.0f;

            IFont totalFont = RpFont;
            IFont eliteFont = eliteSpawns ? RpYellowFont : RpFont;
            IFont trashFont = trashSpawns ? RpRedFont : RpFont;

            if (alert == SpawnAlertKind.EitherEliteOrTrash)
                totalFont = RpOrangeFont;
            else if (alert == SpawnAlertKind.EliteAndTrash)
                totalFont = RpPurpleFont;

            DrawRpLine("TotalRP", rp.TotalRP, x, y, totalFont); y += lh;
            DrawRpLine("EliteRP", rp.EliteRP, x, y, eliteFont); y += lh;
            DrawRpLine("TrashRP", rp.TrashRP, x, y, trashFont); y += lh;

            if (ShowSpawnAlert && alert != SpawnAlertKind.None)
                DrawSpawnAlert(GetSpawnAlertText(alert), x, y + 2.0f, alert);

            DebugRpSnapshot(rp, remaining, alert);
        }

        private enum SpawnAlertKind
        {
            None,
            EliteOnly,
            TrashOnly,
            EitherEliteOrTrash,
            EliteAndTrash
        }

        private enum StrickenDamageSourceMatch
        {
            None,
            ExactAcd,
            AnnIdWithRecentLocalAttack
        }

        private SpawnAlertKind GetSpawnAlertKind(bool eliteSpawns, bool trashSpawns, bool totalSpawns)
        {
            if (eliteSpawns && trashSpawns)
                return SpawnAlertKind.EitherEliteOrTrash;

            if (eliteSpawns)
                return SpawnAlertKind.EliteOnly;

            if (trashSpawns)
                return SpawnAlertKind.TrashOnly;

            if (totalSpawns)
                return SpawnAlertKind.EliteAndTrash;

            return SpawnAlertKind.None;
        }

        private string GetSpawnAlertText(SpawnAlertKind kind)
        {
            switch (kind)
            {
                case SpawnAlertKind.EliteOnly:
                    return "KILL ELITE";

                case SpawnAlertKind.TrashOnly:
                    return "KILL TRASH";

                case SpawnAlertKind.EitherEliteOrTrash:
                    return "KILL ELITE OR TRASH";

                case SpawnAlertKind.EliteAndTrash:
                    return "KILL ELITE AND TRASH";

                default:
                    return "";
            }
        }

        private IFont GetRpFont(SpawnAlertKind kind, bool bright)
        {
            switch (kind)
            {
                case SpawnAlertKind.EliteOnly:
                    return RpYellowFont;

                case SpawnAlertKind.TrashOnly:
                    return RpRedFont;

                case SpawnAlertKind.EitherEliteOrTrash:
                    return RpOrangeFont;

                case SpawnAlertKind.EliteAndTrash:
                    return RpPurpleFont;

                default:
                    return RpFont;
            }
        }

        private void DrawSpawnAlert(string text, float x, float y, SpawnAlertKind kind)
        {
            if (string.IsNullOrEmpty(text))
                return;

            bool bright = ((Hud.Game.CurrentGameTick / (int)Math.Max(1.0, SpawnAlertPulseSpeed)) % 2) == 0;

            IFont font;

            if (kind == SpawnAlertKind.EliteOnly)
                font = bright ? AlertYellowBrightFont : AlertYellowDimFont;
            else if (kind == SpawnAlertKind.TrashOnly)
                font = bright ? AlertRedBrightFont : AlertRedDimFont;
            else if (kind == SpawnAlertKind.EitherEliteOrTrash)
                font = bright ? AlertOrangeBrightFont : AlertOrangeDimFont;
            else if (kind == SpawnAlertKind.EliteAndTrash)
                font = bright ? AlertPurpleBrightFont : AlertPurpleDimFont;
            else
                return;

            font.DrawText(text, x, y);
        }

        private NearbyRpSnapshot CalculateNearbyRp()
        {
            double maxProg = Hud.Game.MaxQuestProgress;
            if (maxProg <= 0)
                return null;

            var monsters = Hud.Game.AliveMonsters;
            if (monsters == null)
                return null;

            var rp = new NearbyRpSnapshot();

            var countedBluePacks = new HashSet<string>();
            var countedYellowPacks = new HashSet<string>();

            foreach (var m in monsters)
            {
                if (!ShouldCountMonsterForNearbyRp(m))
                    continue;

                double bodyPct = GetMonsterBodyProgressPct(m, maxProg);
                bool isRareMinion = m.Rarity == ActorRarity.RareMinion;

                if (m.Rarity == ActorRarity.Champion)
                {
                    rp.EliteBodyPct += bodyPct;

                    string key = GetPackKey(m);
                    if (countedBluePacks.Add(key))
                    {
                        rp.BluePacks++;
                        rp.EliteOrbPct += GetBluePackOrbPct();
                    }

                    continue;
                }

                if (m.Rarity == ActorRarity.Rare)
                {
                    rp.EliteBodyPct += bodyPct;

                    string key = GetPackKey(m);
                    if (countedYellowPacks.Add(key))
                    {
                        rp.YellowPacks++;
                        rp.EliteOrbPct += GetYellowPackOrbPct();
                    }

                    continue;
                }

                if (isRareMinion)
                {
                    rp.RareMinions++;

                    if (CountRareMinionBodyAsEliteRP)
                        rp.EliteBodyPct += bodyPct;
                    else
                        rp.TrashBodyPct += bodyPct;

                    continue;
                }

                if (m.IsElite)
                {
                    // Unknown elite-like things: count body only as elite body,
                    // but do not add purple-orb estimate.
                    rp.EliteBodyPct += bodyPct;
                    continue;
                }

                rp.TrashBodyPct += bodyPct;
            }

            return rp;
        }

        private bool ShouldCountMonsterForNearbyRp(IMonster m)
        {
            if (m == null || !m.IsAlive || m.SnoMonster == null)
                return false;

            if (m.NormalizedXyDistanceToMe > NearbyRangeYards)
                return false;

            if (m.SnoMonster.RiftProgression <= 0)
                return false;

            // Do not count guardian/boss/unique/keywarden-style monsters for nearby spawn prediction.
            // The panel is for spawning the guardian, not boss phase.
            if (IsRiftGuardianLike(m))
                return false;

            if (m.SnoMonster.IsUnique &&
                m.Rarity != ActorRarity.Rare &&
                m.Rarity != ActorRarity.Champion &&
                m.Rarity != ActorRarity.RareMinion)
            {
                return false;
            }

            return true;
        }

        private double GetMonsterBodyProgressPct(IMonster m, double maxProg)
        {
            if (m == null || m.SnoMonster == null || maxProg <= 0)
                return 0.0;

            return m.SnoMonster.RiftProgression / maxProg * 100.0;
        }

        private double GetBluePackOrbPct()
        {
            double orbs = BlueChampionBaseOrbCount;

            if (AssumeReaperSealExtraOrb)
                orbs += ReaperSealExtraOrbCount;

            return orbs * ProgressOrbPct;
        }

        private double GetYellowPackOrbPct()
        {
            double orbs = YellowRareBaseOrbCount;

            if (AssumeReaperSealExtraOrb)
                orbs += ReaperSealExtraOrbCount;

            return orbs * ProgressOrbPct;
        }

        private string GetPackKey(IMonster m)
        {
            if (m == null)
                return "null";

            try
            {
                if (m.Pack != null)
                    return "pack:" + m.Pack.GetHashCode().ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
            }

            try
            {
                if (m.FloorCoordinate != null)
                {
                    int gx = (int)(m.FloorCoordinate.X / 80.0f);
                    int gy = (int)(m.FloorCoordinate.Y / 80.0f);

                    return "fallback:" +
                           m.Rarity.ToString() + ":" +
                           m.SnoMonster.Sno.ToString(CultureInfo.InvariantCulture) + ":" +
                           gx.ToString(CultureInfo.InvariantCulture) + ":" +
                           gy.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return "fallback:" +
                   m.Rarity.ToString() + ":" +
                   m.SnoMonster.Sno.ToString(CultureInfo.InvariantCulture) + ":" +
                   m.AcdId.ToString(CultureInfo.InvariantCulture);
        }

        private void DrawRpLine(string label, double pct, float x, float y, IFont font)
        {
            string timeStr = "--";

            if (_killRate > 0.001 && pct > 0)
            {
                double sec = pct / _killRate;
                var ts = TimeSpan.FromSeconds(sec);
                timeStr = string.Format("{0}m {1:D2}s", (int)ts.TotalMinutes, ts.Seconds);
            }

            string text = string.Format(
                CultureInfo.InvariantCulture,
                "{0,-8} {1:F2}% = {2}",
                label,
                pct,
                timeStr
            );

            (font ?? RpFont).DrawText(text, x, y);
        }

        // ── Timers ───────────────────────────────────────────────────────
        private bool TryReadLiveGreaterRiftBarRect(out RectangleF rect)
        {
            rect = RectangleF.Empty;

            try
            {
                var bar = Hud.Render.GreaterRiftBarUiElement;

                if (bar != null)
                {
                    RectangleF r = bar.Rectangle;

                    if (r.Width > 0.0f && r.Height > 0.0f)
                    {
                        rect = r;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetGreaterRiftBarRect(out RectangleF rect)
        {
            rect = RectangleF.Empty;

            if (_riftSummaryActive &&
                !_summaryGreaterRiftBarRect.IsEmpty &&
                _summaryGreaterRiftBarRect.Width > 0.0f &&
                _summaryGreaterRiftBarRect.Height > 0.0f)
            {
                rect = _summaryGreaterRiftBarRect;
                return true;
            }

            RectangleF liveRect;
            if (TryReadLiveGreaterRiftBarRect(out liveRect))
            {
                var bar = Hud.Render.GreaterRiftBarUiElement;

                if (bar != null && bar.Visible && !_riftSummaryActive)
                    _lastGreaterRiftBarRect = liveRect;

                rect = liveRect;
                return true;
            }

            if (!_lastGreaterRiftBarRect.IsEmpty &&
                _lastGreaterRiftBarRect.Width > 0.0f &&
                _lastGreaterRiftBarRect.Height > 0.0f)
            {
                rect = _lastGreaterRiftBarRect;
                return true;
            }

            return false;
        }

        private int GetRiftElapsedTicks()
        {
            int startTick = _riftStartTick;

            // While the rift is still active, prefer the native GR start tick.
            // Once frozen, keep using the stored _riftStartTick.
            if (!_riftSummaryActive)
            {
                int nativeStartTick = ReadNativeGreaterRiftStartTick();
                if (nativeStartTick > 0)
                    startTick = nativeStartTick;
            }

            if (startTick <= 0)
            {
                int nativeStartTick = ReadNativeGreaterRiftStartTick();
                if (nativeStartTick > 0)
                    startTick = nativeStartTick;
            }

            if (startTick <= 0)
                return -1;

            int endTick = _riftCompleteTick > 0
                ? _riftCompleteTick
                : Hud.Game.CurrentGameTick;

            int elapsed = endTick - startTick;

            if (elapsed < 0)
                return 0;

            // Display correction only. Keep compact and configurable.
            elapsed += RiftElapsedDisplayOffsetTicks;

            return elapsed < 0 ? 0 : elapsed;
        }

        private int GetBossElapsedTicks()
        {
            if (_bossStartTick <= 0)
                return -1;

            int endTick = _bossEndTick > 0
                ? _bossEndTick
                : (_riftCompleteTick > 0 ? _riftCompleteTick : Hud.Game.CurrentGameTick);

            int elapsed = endTick - _bossStartTick;
            return elapsed < 0 ? 0 : elapsed;
        }

        private void DrawRiftTimerStack(RectangleF barRect)
        {
            bool drawBoss = ShowBossTimer &&
                            _bossStartTick > 0 &&
                            (_bossActive || _bossEndTick > 0 || _riftSummaryActive || _riftCompleteTick > 0);
            bool drawElapsed = ShowElapsedRiftTimer;

            int elapsedTicks = drawElapsed ? GetRiftElapsedTicks() : -1;
            int bossTicks = drawBoss ? GetBossElapsedTicks() : -1;

            drawElapsed = elapsedTicks >= 0;
            drawBoss = bossTicks >= 0;

            if (!drawBoss && !drawElapsed)
                return;

            SharpDX.DirectWrite.TextLayout bossLayout = null;
            SharpDX.DirectWrite.TextLayout elapsedLayout = null;

            if (drawBoss)
            {
                var ts = TimeSpan.FromSeconds(bossTicks / 60.0);
                var bossText = string.Format("Boss: {0}:{1:D2}", (int)ts.TotalMinutes, ts.Seconds);
                bossLayout = BossFont.GetTextLayout(bossText);
            }

            if (drawElapsed)
            {
                var ts = TimeSpan.FromSeconds(elapsedTicks / 60.0);
                var elapsedText = string.Format("Elapsed: {0}:{1:D2}", (int)ts.TotalMinutes, ts.Seconds);
                elapsedLayout = TimeFont.GetTextLayout(elapsedText);
            }

            // Split layout below the GR progress percentage:
            // Elapsed left, Boss right. Both use orange timer fonts.
            float y = _riftSummaryActive
                ? barRect.Top + RiftSummaryTimerTopOffsetY
                : barRect.Bottom + RiftTimerBelowBarOffsetY;

            if (drawElapsed && elapsedLayout != null && drawBoss && bossLayout != null)
            {
                float elapsedX = barRect.Left + RiftTimerHorizontalPadding;
                float bossX = barRect.Right - bossLayout.Metrics.Width - RiftTimerHorizontalPadding;

                // Fallback for narrow bars or unexpectedly long text:
                // center both labels as a pair while preserving a gap.
                if (elapsedX + elapsedLayout.Metrics.Width + RiftTimerMinGap > bossX)
                {
                    float totalWidth = elapsedLayout.Metrics.Width + RiftTimerMinGap + bossLayout.Metrics.Width;
                    float startX = barRect.Left + (barRect.Width - totalWidth) * 0.5f;

                    if (startX < barRect.Left + RiftTimerHorizontalPadding)
                        startX = barRect.Left + RiftTimerHorizontalPadding;

                    elapsedX = startX;
                    bossX = elapsedX + elapsedLayout.Metrics.Width + RiftTimerMinGap;
                }

                TimeFont.DrawText(elapsedLayout, elapsedX, y);
                BossFont.DrawText(bossLayout, bossX, y);
                return;
            }

            if (drawElapsed && elapsedLayout != null)
            {
                float elapsedX = barRect.Left + RiftTimerHorizontalPadding;
                TimeFont.DrawText(elapsedLayout, elapsedX, y);
                return;
            }

            if (drawBoss && bossLayout != null)
            {
                float bossX = barRect.Right - bossLayout.Metrics.Width - RiftTimerHorizontalPadding;
                BossFont.DrawText(bossLayout, bossX, y);
            }
        }

        private bool IsRiftGuardianLike(IMonster m)
        {
            if (m == null || !m.IsAlive || m.SnoMonster == null)
                return false;

            if (m.Rarity == ActorRarity.Boss)
                return true;

            if (m.SnoMonster.Priority == MonsterPriority.boss)
                return true;

            // Fallback for guardian-like uniques in GR.
            if (Hud.Game.Me != null &&
                Hud.Game.Me.InGreaterRift &&
                Hud.Game.RiftPercentage >= 100.0 &&
                m.SnoMonster.IsUnique &&
                m.SnoMonster.RiftProgression > 0)
            {
                return true;
            }

            return false;
        }

        

        // ── Stricken estimator ───────────────────────────────────────────
        private void UpdateStrickenEstimator(int tick)
        {
            UpdateLocalAttackGate(tick);
            UpdateTrackedStrickenTarget(tick);
            UpdateStrickenGlobalDamageSamples(tick);

            if (TrackOnlyLocalPlayerStricken)
            {
                UpdateStrickenEstimatorForPlayer(Hud.Game.Me, tick);
                return;
            }

            if (Hud.Game.Players == null)
                return;

            foreach (var player in Hud.Game.Players)
                UpdateStrickenEstimatorForPlayer(player, tick);
        }

        private void UpdateStrickenEstimatorForPlayer(IPlayer player, int tick)
        {
            if (player == null || !player.IsInGame)
                return;

            uint heroId = GetPlayerKey(player);
            if (heroId == 0)
                return;

            StrickenProcState state;
            if (!_strickenProcByHero.TryGetValue(heroId, out state))
            {
                state = new StrickenProcState();
                _strickenProcByHero[heroId] = state;
            }

            IMonster trackedTarget = GetTrackedStrickenTarget();

            if (!PlayerHasStricken(player))
            {
                state.LastProcActive = false;
                state.PendingProc = null;
                DebugStrickenPlayerState(player, trackedTarget, tick, "no-stricken");
                return;
            }

            bool procActive = IsStrickenProcActive(player);

            if (!player.IsMe && UseProcEdgeEstimatorForRemotePlayers)
            {
                UpdateRemotePlayerStrickenEstimator(player, heroId, state, trackedTarget, procActive, tick);
                return;
            }

            // Local-player path: keep the validated conservative attribution model unchanged.
            if (procActive && !state.LastProcActive)
            {
                state.LastProcTick = tick;

                bool localAttackRecent = HasRecentLocalAttack(tick);

                if (!localAttackRecent)
                {
                    state.PendingProc = null;
                    DebugStrickenPlayerState(player, trackedTarget, tick, "reject-proc-no-local-attack");
                }
                else
                {
                    state.PendingProc = new PendingStrickenProc
                    {
                        Active = true,
                        ProcTick = tick,
                        LockedTargetKey = _lastValidStrickenTargetKey,
                        LockedTargetName = GetTrackedStrickenTargetName(),
                        LocalAttackTick = _lastLocalAttackTick,
                        LocalAttackRecentAtProc = true,
                        LocalAttackState = _lastLocalAttackState,
                        LocalAttackAnimation = _lastLocalAttackAnimation
                    };

                    DebugStrickenPlayerState(player, trackedTarget, tick, "proc-edge");
                }
            }

            if (!procActive && state.LastProcActive)
                DebugStrickenPlayerState(player, trackedTarget, tick, "proc-reset");

            ResolvePendingStrickenProc(player, heroId, state, tick);

            state.LastProcActive = procActive;

            DebugStrickenPlayerState(player, GetTrackedStrickenTarget(), tick, "tick");
        }

        private void UpdateRemotePlayerStrickenEstimator(
            IPlayer player,
            uint heroId,
            StrickenProcState state,
            IMonster trackedTarget,
            bool procActive,
            int tick)
        {
            if (state == null)
                return;

            if (procActive && !state.LastProcActive)
            {
                state.LastProcTick = tick;

                int targetKey = GetFreshTrackedStrickenTargetKey(trackedTarget, tick);

                if (targetKey != 0)
                {
                    if (state.LastRemoteAcceptedStackTick <= 0 ||
                        tick - state.LastRemoteAcceptedStackTick >= RemoteStrickenMinAcceptedStackIntervalTicks)
                    {
                        IncrementStrickenStack(heroId, targetKey, tick, "remote-proc-edge-estimator");
                        state.LastRemoteAcceptedStackTick = tick;

                        DebugStrickenPlayerState(player, trackedTarget, tick, "remote-proc-edge-accepted");
                    }
                    else
                    {
                        DebugStrickenPlayerState(player, trackedTarget, tick, "remote-proc-too-soon");
                    }
                }
                else
                {
                    DebugStrickenPlayerState(player, trackedTarget, tick, "remote-proc-no-target");
                }
            }

            if (!procActive && state.LastProcActive)
                DebugStrickenPlayerState(player, trackedTarget, tick, "remote-proc-reset");

            state.LastProcActive = procActive;

            DebugStrickenPlayerState(player, trackedTarget, tick, "remote-tick");
        }

        private void UpdateLocalAttackGate(int tick)
        {
            IPlayer me = null;

            try
            {
                me = Hud.Game.Me;
            }
            catch
            {
                me = null;
            }

            if (me == null)
                return;

            AcdAnimationState state;

            try
            {
                state = me.AnimationState;
            }
            catch
            {
                return;
            }

            bool activeAttack =
                state == AcdAnimationState.Attacking ||
                state == AcdAnimationState.Casting ||
                state == AcdAnimationState.Channeling;

            if (!activeAttack)
                return;

            string anim = "";

            try
            {
                anim = me.Animation.ToString();
            }
            catch
            {
                anim = "";
            }

            if (!IsValidStrickenAttackAnimation(state, anim))
            {
                DebugStrickenLocalAttackAnimationRejected(tick, state, anim);
                return;
            }

            _lastLocalAttackTick = tick;
            _lastLocalAttackState = state.ToString();
            _lastLocalAttackAnimation = anim;
        }

        private bool IsBlockedUtilityAnimation(string animation)
        {
            if (string.IsNullOrEmpty(animation))
                return false;

            string a = animation.ToLowerInvariant();

            return
                a.Contains("buff") ||
                a.Contains("teleport") ||
                a.Contains("town") ||
                a.Contains("recall") ||
                a.Contains("portal");
        }

        private bool IsValidStrickenAttackAnimation(AcdAnimationState state, string animation)
        {
            if (string.IsNullOrEmpty(animation))
                return false;

            if (IsBlockedUtilityAnimation(animation))
                return false;

            if (state == AcdAnimationState.Channeling)
                return true;

            string a = animation.ToLowerInvariant();

            return
                a.Contains("attack") ||
                a.Contains("spellcast_aoe") ||
                a.Contains("spellcast_directed") ||
                a.Contains("orb_spellcast");
        }

        private bool HasRecentLocalAttack(int tick)
        {
            if (!RequireLocalAttackAnimationForStricken)
                return true;

            if (_lastLocalAttackTick <= 0)
                return false;

            return tick - _lastLocalAttackTick <= StrickenRecentLocalAttackTicks;
        }

        private string GetLocalAttackDebug(int tick)
        {
            return
                " localAttackTick=" + _lastLocalAttackTick.ToString(CultureInfo.InvariantCulture) +
                " localAttackAge=" + (_lastLocalAttackTick <= 0 ? "none" : (tick - _lastLocalAttackTick).ToString(CultureInfo.InvariantCulture)) +
                " localAttackRecent=" + HasRecentLocalAttack(tick).ToString() +
                " localAttackState=" + _lastLocalAttackState +
                " localAttackAnim=" + _lastLocalAttackAnimation;
        }

        private void DebugStrickenLocalAttackAnimationRejected(int tick, AcdAnimationState state, string animation)
        {
            if (!EnableStrickenDebug)
                return;

            if (tick - _lastLocalAttackRejectDebugTick < Math.Max(1, StrickenDebugIntervalTicks))
                return;

            _lastLocalAttackRejectDebugTick = tick;

            try
            {
                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " phase=local-attack-animation-rejected" +
                    " state=" + state.ToString() +
                    " animation=" + animation +
                    GetLocalAttackDebug(tick);

                Hud.TextLog.Log("s7o_RiftInfo_StrickenDebug", line);
            }
            catch
            {
            }
        }

        private void UpdateStrickenGlobalDamageSamples(int tick)
        {
            if (Hud.Game.AliveMonsters == null)
                return;

            foreach (var m in Hud.Game.AliveMonsters)
            {
                if (!ShouldSampleMonsterForStricken(m))
                    continue;

                int key = GetMonsterTargetKey(m);
                if (key == 0)
                    continue;

                double curHealth;
                try
                {
                    curHealth = m.CurHealth;
                }
                catch
                {
                    continue;
                }

                StrickenGlobalDamageSample sample;
                if (!_strickenGlobalDamageSamples.TryGetValue(key, out sample))
                {
                    sample = new StrickenGlobalDamageSample
                    {
                        TargetKey = key,
                        Monster = m,
                        Name = GetSafeMonsterName(m),
                        Code = GetSafeMonsterCode(m),
                        Rarity = m.Rarity,
                        LastHealth = curHealth,
                        LastSeenTick = tick
                    };

                    _strickenGlobalDamageSamples[key] = sample;
                    continue;
                }

                sample.Monster = m;
                sample.Name = GetSafeMonsterName(m);
                sample.Code = GetSafeMonsterCode(m);
                sample.Rarity = m.Rarity;
                sample.LastSeenTick = tick;

                if (curHealth > 0 && sample.LastHealth > 0 && curHealth < sample.LastHealth)
                {
                    sample.LastDamageTick = tick;
                    sample.LastDamageAmount = sample.LastHealth - curHealth;
                    sample.LastDamageAcd = ReadActorAttributeUInt(m, Hud.Sno.Attributes.Last_Damage_ACD);
                    sample.LastAcdAttackedBy = ReadActorAttributeUInt(m, Hud.Sno.Attributes.Last_ACD_Attacked_By);
                    sample.LastDamageMainActor = ReadActorAttributeUInt(m, Hud.Sno.Attributes.Last_Damage_MainActor);

                    DebugStrickenDamageSample(sample, tick);
                }

                sample.LastHealth = curHealth;
            }
        }

        private bool ShouldSampleMonsterForStricken(IMonster m)
        {
            if (m == null || !m.IsAlive || m.SnoMonster == null)
                return false;

            if (m.CurHealth <= 0)
                return false;

            if (m.Invulnerable || m.Illusion)
                return false;

            if (m.NormalizedXyDistanceToMe > StrickenCandidateRangeYards)
                return false;

            return true;
        }

        private string GetSafeMonsterName(IMonster m)
        {
            try
            {
                if (m != null && m.SnoMonster != null && !string.IsNullOrEmpty(m.SnoMonster.NameLocalized))
                    return m.SnoMonster.NameLocalized;
            }
            catch
            {
            }

            return GetSafeMonsterCode(m);
        }

        private string GetSafeMonsterCode(IMonster m)
        {
            try
            {
                if (m != null && m.SnoMonster != null && !string.IsNullOrEmpty(m.SnoMonster.Code))
                    return m.SnoMonster.Code;
            }
            catch
            {
            }

            return "monster";
        }

        private uint ReadActorAttributeUInt(IActor actor, IAttribute attribute)
        {
            if (actor == null || attribute == null)
                return 0;

            uint[] modifiers = new uint[]
            {
                0,
                0xFFFFF,
                0xFFFFFFFF,
                2147483647
            };

            foreach (uint modifier in modifiers)
            {
                try
                {
                    uint value = actor.GetAttributeValueAsUInt(attribute, modifier, 0);
                    if (value != 0 && value != uint.MaxValue)
                        return value;
                }
                catch
                {
                }
            }

            return 0;
        }

        private StrickenDamageSourceMatch GetStrickenDamageSourceMatch(
            StrickenGlobalDamageSample sample,
            IPlayer player,
            PendingStrickenProc proc,
            int tick)
        {
            if (sample == null || player == null)
                return StrickenDamageSourceMatch.None;

            uint playerAcd = 0;
            uint playerAnn = 0;

            try { playerAcd = player.AcdId; } catch { playerAcd = 0; }
            try { playerAnn = player.AnnId; } catch { playerAnn = 0; }

            bool exactAcdMatch =
                playerAcd != 0 &&
                (sample.LastDamageAcd == playerAcd || sample.LastAcdAttackedBy == playerAcd);

            if (exactAcdMatch)
                return StrickenDamageSourceMatch.ExactAcd;

            if (StrictDirectPlayerAcdOnly)
                return StrickenDamageSourceMatch.None;

            bool annMatch =
                playerAnn != 0 &&
                (sample.LastDamageAcd == playerAnn || sample.LastAcdAttackedBy == playerAnn);

            if (!annMatch)
                return StrickenDamageSourceMatch.None;

            if (!AllowAnnIdDamageWhenLocalAttackRecent)
                return StrickenDamageSourceMatch.None;

            if (proc == null || !proc.LocalAttackRecentAtProc)
                return StrickenDamageSourceMatch.None;

            if (tick - proc.LocalAttackTick > StrickenAnnIdMaxLocalAttackAgeTicks)
                return StrickenDamageSourceMatch.None;

            return StrickenDamageSourceMatch.AnnIdWithRecentLocalAttack;
        }

        private void ResolvePendingStrickenProc(IPlayer player, uint heroId, StrickenProcState state, int tick)
        {
            if (state == null || state.PendingProc == null || !state.PendingProc.Active)
                return;

            PendingStrickenProc proc = state.PendingProc;

            if (RequireLocalAttackAnimationForStricken && !proc.LocalAttackRecentAtProc)
            {
                DebugStrickenAttribution(player, proc, null, tick, "reject-no-local-attack-at-proc", StrickenDamageSourceMatch.None);
                proc.Active = false;
                return;
            }

            int procTick = proc.ProcTick;
            int startTick = procTick; // no pre-window
            int endTick = procTick + Math.Max(1, StrickenPostProcDamageTicks);

            StrickenGlobalDamageSample first = null;
            StrickenDamageSourceMatch firstMatch = StrickenDamageSourceMatch.None;
            int firstTick = int.MaxValue;
            int sameTickAcceptedHitCount = 0;

            foreach (var kv in _strickenGlobalDamageSamples)
            {
                var sample = kv.Value;
                if (sample == null)
                    continue;

                if (sample.LastDamageTick < startTick || sample.LastDamageTick > tick)
                    continue;

                if (sample.LastDamageTick > endTick)
                    continue;

                StrickenDamageSourceMatch match = GetStrickenDamageSourceMatch(sample, player, proc, tick);

                if (match == StrickenDamageSourceMatch.None)
                    continue;

                if (sample.LastDamageTick < firstTick)
                {
                    first = sample;
                    firstMatch = match;
                    firstTick = sample.LastDamageTick;
                    sameTickAcceptedHitCount = 1;
                }
                else if (sample.LastDamageTick == firstTick)
                {
                    sameTickAcceptedHitCount++;
                }
            }

            if (first != null)
            {
                if (RejectAmbiguousStrickenFirstHits && sameTickAcceptedHitCount > 1)
                {
                    DebugStrickenAttribution(player, proc, first, tick, "reject-ambiguous-first-hit", firstMatch);
                    proc.Active = false;
                    return;
                }

                if (state.LastAcceptedStackTick > 0 &&
                    tick - state.LastAcceptedStackTick < StrickenMinAcceptedStackIntervalTicks)
                {
                    DebugStrickenAttribution(player, proc, first, tick, "reject-accepted-stack-too-soon", firstMatch);
                    proc.Active = false;
                    return;
                }

                string reason =
                    firstMatch == StrickenDamageSourceMatch.ExactAcd
                        ? "exact-acd-first-hit"
                        : "annid-local-attack-first-hit";

                IncrementStrickenStack(heroId, first.TargetKey, tick, reason);
                state.LastAcceptedStackTick = tick;

                if (proc.LockedTargetKey != 0 && first.TargetKey != proc.LockedTargetKey)
                    DebugStrickenAttribution(player, proc, first, tick, "diverted-stack-not-tracked-target", firstMatch);
                else
                    DebugStrickenAttribution(player, proc, first, tick, "accepted-" + reason, firstMatch);

                proc.Active = false;
                return;
            }

            if (tick > endTick)
            {
                DebugStrickenAttribution(player, proc, null, tick, "expired-no-accepted-source-hit", StrickenDamageSourceMatch.None);
                proc.Active = false;
            }
        }



        private void IncrementStrickenStack(uint heroId, int targetKey, int tick, string reason)
        {
            if (heroId == 0 || targetKey == 0)
                return;

            string key = GetStrickenStackKey(heroId, targetKey);

            int stacks;
            _strickenStacksByHeroAndTarget.TryGetValue(key, out stacks);
            stacks++;

            _strickenStacksByHeroAndTarget[key] = stacks;

            DebugStrickenStackIncrement(heroId, targetKey, tick, stacks, reason);
        }

        private IMonster GetSelectedStrickenTarget()
        {
            IMonster m = null;

            try
            {
                m = Hud.Game.SelectedMonster2 ?? Hud.Game.SelectedMonster1;
            }
            catch
            {
                m = null;
            }

            if (!IsValidStrickenDisplayTarget(m))
                return null;

            return m;
        }

        private void UpdateTrackedStrickenTarget(int tick)
        {
            IMonster current = GetSelectedStrickenTarget();
            if (current == null)
                return;

            int key = GetMonsterTargetKey(current);
            if (key == 0)
                return;

            _lastValidStrickenTarget = current;
            _lastValidStrickenTargetKey = key;
            _lastValidStrickenTargetTick = tick;
            _lastValidStrickenTargetName = GetSafeMonsterName(current);
        }

        private IMonster GetTrackedStrickenTarget()
        {
            if (_lastValidStrickenTargetKey == 0)
                return null;

            IMonster live = FindAliveMonsterByTargetKey(_lastValidStrickenTargetKey);
            if (live != null)
            {
                _lastValidStrickenTarget = live;
                return live;
            }

            return _lastValidStrickenTarget;
        }

        private int GetFreshTrackedStrickenTargetKey(IMonster trackedTarget, int tick)
        {
            if (trackedTarget != null && IsValidStrickenDisplayTarget(trackedTarget))
            {
                int liveKey = GetMonsterTargetKey(trackedTarget);
                if (liveKey != 0)
                    return liveKey;
            }

            if (_lastValidStrickenTargetKey == 0)
                return 0;

            if (_lastValidStrickenTargetTick <= 0)
                return 0;

            int cacheTicks = (_bossStartTick > 0 && !_riftSummaryActive)
                ? StrickenBossTargetCacheTicks
                : StrickenTargetCacheTicks;

            if (tick - _lastValidStrickenTargetTick > cacheTicks)
                return 0;

            return _lastValidStrickenTargetKey;
        }

        private string GetTrackedStrickenTargetName()
        {
            if (!string.IsNullOrEmpty(_lastValidStrickenTargetName))
                return _lastValidStrickenTargetName;

            return "target";
        }

        private IMonster FindAliveMonsterByTargetKey(int key)
        {
            if (key == 0 || Hud.Game.AliveMonsters == null)
                return null;

            foreach (var m in Hud.Game.AliveMonsters)
            {
                if (m == null || !m.IsAlive)
                    continue;

                if (GetMonsterTargetKey(m) == key)
                    return m;
            }

            return null;
        }


        private bool IsValidStrickenDisplayTarget(IMonster m)
        {
            if (m == null || !m.IsAlive || m.SnoMonster == null)
                return false;

            if (!ShowStrickenOnAnySelectedElite)
                return IsRiftGuardianLike(m);

            if (IsRiftGuardianLike(m))
                return true;

            if (m.Rarity == ActorRarity.Champion)
                return true;

            if (m.Rarity == ActorRarity.Rare)
                return true;

            if (m.Rarity == ActorRarity.Unique)
                return true;

            if (m.SnoMonster.Priority == MonsterPriority.keywarden)
                return true;

            if (m.SnoMonster.Priority == MonsterPriority.boss)
                return true;

            return false;
        }

        private bool PlayerHasStricken(IPlayer player)
        {
            if (player == null || player.Powers == null)
                return false;

            try
            {
                var gem = player.Powers.UsedLegendaryGems.BaneOfTheStrickenPrimary;
                if (gem != null && gem.Active)
                    return true;
            }
            catch
            {
            }

            // Fallback only for gem-equipped/active detection.
            // Do not use this as the proc/counter signal.
            return SafeBuffIsActive(player, StrickenSno, 0);
        }

        private bool IsStrickenProcActive(IPlayer player)
        {
            if (player == null || player.Powers == null)
                return false;

            // Debug log proved primary 428348 index 2 is the toggling proc/cooldown pulse.
            // Primary index 0 is always active and must not be counted as procActive.
            return SafeBuffIsActive(player, StrickenSno, StrickenProcIconIndex);
        }

        private bool SafeBuffIsActive(IPlayer player, uint sno, int iconIndex)
        {
            if (player == null || player.Powers == null)
                return false;

            try
            {
                return player.Powers.BuffIsActive(sno, iconIndex);
            }
            catch
            {
                return false;
            }
        }

        private uint GetPlayerKey(IPlayer player)
        {
            if (player == null)
                return 0;

            if (player.HeroId != 0)
                return player.HeroId;

            return (uint)Math.Max(1, player.Index + 1);
        }

        private int GetMonsterTargetKey(IMonster monster)
        {
            if (monster == null)
                return 0;

            // During Rift Guardian phase, use a stable key so Stricken stacks survive
            // boss unload/reload, town portals, and ACD identity changes.
            if (_bossStartTick > 0 && !_riftSummaryActive && IsRiftGuardianLike(monster))
            {
                try
                {
                    if (monster.SnoMonster != null && monster.SnoMonster.Sno != 0)
                    {
                        int sno = unchecked((int)monster.SnoMonster.Sno);
                        return sno == 0 ? -1 : -Math.Abs(sno);
                    }
                }
                catch
                {
                    return -1;
                }

                return -1;
            }

            return unchecked((int)monster.AcdId);
        }

        private string GetStrickenStackKey(uint heroId, int targetKey)
        {
            return heroId.ToString(CultureInfo.InvariantCulture) + ":" +
                   targetKey.ToString(CultureInfo.InvariantCulture);
        }

        private int GetEstimatedStrickenStacks(IPlayer player, IMonster target)
        {
            if (player == null || target == null)
                return 0;

            uint heroId = GetPlayerKey(player);
            int targetKey = GetMonsterTargetKey(target);

            string key = GetStrickenStackKey(heroId, targetKey);

            int stacks;
            if (_strickenStacksByHeroAndTarget.TryGetValue(key, out stacks))
                return stacks;

            return 0;
        }


        private int GetEstimatedStrickenStacksByKey(IPlayer player, int targetKey)
        {
            if (player == null || targetKey == 0)
                return 0;

            uint heroId = GetPlayerKey(player);
            if (heroId == 0)
                return 0;

            string key = GetStrickenStackKey(heroId, targetKey);

            int stacks;
            _strickenStacksByHeroAndTarget.TryGetValue(key, out stacks);
            return stacks;
        }

        private string GetShortPlayerName(IPlayer player)
        {
            if (player == null)
                return "?";

            string name = null;

            try { name = player.HeroName; } catch { }

            if (string.IsNullOrEmpty(name))
            {
                try { name = player.BattleTagAbovePortrait; } catch { }
            }

            if (string.IsNullOrEmpty(name))
                name = "P" + (player.Index + 1).ToString(CultureInfo.InvariantCulture);

            if (name.Length > 7)
                name = name.Substring(0, 7);

            return name;
        }

        private void DrawStrickenOnSelectedMonster()
        {
            IMonster target = GetSelectedStrickenTarget();
            if (target == null)
                return;

            var uiBar = Hud.Render.MonsterHpBarUiElement;
            if (uiBar == null || !uiBar.Visible)
                return;

            if (Hud.Game.Players == null)
                return;

            float iconSize = StrickenIconSize;
            float startX = uiBar.Rectangle.Right + StrickenBarOffsetX;
            float y = uiBar.Rectangle.Top + (uiBar.Rectangle.Height - iconSize) * 0.5f;

            int col = 0;

            foreach (var player in Hud.Game.Players)
            {
                if (player == null || !player.IsInGame)
                    continue;

                if (TrackOnlyLocalPlayerStricken && !player.IsMe)
                    continue;

                if (!PlayerHasStricken(player))
                    continue;

                int stacks = GetEstimatedStrickenStacks(player, target);
                bool isRemotePlayer = !player.IsMe;

                if (stacks <= 0)
                {
                    if (isRemotePlayer)
                    {
                        if (!ShowRemoteStrickenZeroStacks)
                            continue;
                    }
                    else if (!ShowStrickenZeroStacks)
                    {
                        continue;
                    }
                }

                float x = startX + col * (iconSize + StrickenIconGap);

                if (StrickenTexture != null)
                    StrickenTexture.Draw(x, y, iconSize, iconSize, 1.0f);

                // Draw only the stack count below the icon. Do not draw player names.
                DrawStrickenStackTextBelowIcon(stacks, x, y + iconSize + 1.0f, iconSize);

                col++;
            }
        }

        private void DrawStrickenStackTextBelowIcon(int stacks, float x, float y, float iconSize)
        {
            if (StrickenSmallFont == null)
                return;

            string text = stacks.ToString(CultureInfo.InvariantCulture);
            var layout = StrickenSmallFont.GetTextLayout(text);

            float tx = x + iconSize * 0.5f - layout.Metrics.Width * 0.5f;

            StrickenSmallFont.DrawText(layout, tx, y);
        }

        private void DrawStrickenPlayerName(IPlayer player, float x, float y, float iconSize)
        {
            if (StrickenSmallFont == null || player == null)
                return;

            string text = GetShortPlayerName(player);
            if (string.IsNullOrEmpty(text))
                return;

            var layout = StrickenSmallFont.GetTextLayout(text);
            float tx = x + iconSize * 0.5f - layout.Metrics.Width * 0.5f;

            StrickenSmallFont.DrawText(layout, tx, y);
        }

        // ── Debug ────────────────────────────────────────────────────────
        private void DebugRpSnapshot(NearbyRpSnapshot rp, double remaining, SpawnAlertKind alert)
        {
            if (!EnableRiftInfoDebug)
                return;

            int tick = Hud.Game.CurrentGameTick;

            if (tick - _lastDebugTick < DebugLogIntervalTicks)
                return;

            _lastDebugTick = tick;

            try
            {
                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " pct=" + Hud.Game.RiftPercentage.ToString("F2", CultureInfo.InvariantCulture) +
                    " remaining=" + remaining.ToString("F2", CultureInfo.InvariantCulture) +
                    " total=" + rp.TotalRP.ToString("F2", CultureInfo.InvariantCulture) +
                    " elite=" + rp.EliteRP.ToString("F2", CultureInfo.InvariantCulture) +
                    " eliteBody=" + rp.EliteBodyPct.ToString("F2", CultureInfo.InvariantCulture) +
                    " eliteOrb=" + rp.EliteOrbPct.ToString("F2", CultureInfo.InvariantCulture) +
                    " trash=" + rp.TrashRP.ToString("F2", CultureInfo.InvariantCulture) +
                    " bluePacks=" + rp.BluePacks.ToString(CultureInfo.InvariantCulture) +
                    " yellowPacks=" + rp.YellowPacks.ToString(CultureInfo.InvariantCulture) +
                    " rareMinions=" + rp.RareMinions.ToString(CultureInfo.InvariantCulture) +
                    " alert=" + alert.ToString();

                Hud.TextLog.Log("s7o_RiftInfo_Debug", line);
            }
            catch
            {
            }
        }

        private void DebugStrickenPlayerState(IPlayer player, IMonster target, int tick, string phase)
        {
            if (!EnableStrickenDebug)
                return;

            if (tick % Math.Max(1, StrickenDebugIntervalTicks) != 0 &&
                phase == "tick")
            {
                return;
            }

            try
            {
                IMonster selected = GetSelectedStrickenTarget();
                IMonster tracked = GetTrackedStrickenTarget();

                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " phase=" + phase +
                    " player=" + GetDebugPlayerName(player) +
                    " hasStricken=" + PlayerHasStricken(player).ToString() +
                    " proc=" + IsStrickenProcActive(player).ToString() +
                    " procDebug=" + GetPrimaryStrickenProcDebug(player) +
                    " buff=" + GetStrickenBuffDebug(player) +
                    " selectedTarget=" + GetDebugMonster(selected) +
                    " trackedTarget=" + GetDebugMonster(tracked) +
                    " trackedTargetKey=" + _lastValidStrickenTargetKey.ToString(CultureInfo.InvariantCulture) +
                    " trackedTargetName=" + GetTrackedStrickenTargetName() +
                    " trackedStacks=" + GetEstimatedStrickenStacksByKey(player, _lastValidStrickenTargetKey).ToString(CultureInfo.InvariantCulture) +
                    GetLocalAttackDebug(tick);

                Hud.TextLog.Log("s7o_RiftInfo_StrickenDebug", line);
            }
            catch
            {
            }
        }

        private void DebugStrickenDamageSample(StrickenGlobalDamageSample sample, int tick)
        {
            if (!EnableStrickenDebug || sample == null)
                return;

            try
            {
                uint meAcd = 0;
                uint meAnn = 0;

                try { if (Hud.Game.Me != null) meAcd = Hud.Game.Me.AcdId; } catch { }
                try { if (Hud.Game.Me != null) meAnn = Hud.Game.Me.AnnId; } catch { }

                bool exactAcdMatch =
                    meAcd != 0 &&
                    (sample.LastDamageAcd == meAcd || sample.LastAcdAttackedBy == meAcd);

                bool annMatch =
                    meAnn != 0 &&
                    (sample.LastDamageAcd == meAnn || sample.LastAcdAttackedBy == meAnn);

                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " phase=damage-sample" +
                    " targetAcdId=" + sample.TargetKey.ToString(CultureInfo.InvariantCulture) +
                    " name=" + sample.Name +
                    " code=" + sample.Code +
                    " rarity=" + sample.Rarity.ToString() +
                    " dmg=" + sample.LastDamageAmount.ToString("F0", CultureInfo.InvariantCulture) +
                    " lastDamageAcd=" + sample.LastDamageAcd.ToString(CultureInfo.InvariantCulture) +
                    " lastAcdAttackedBy=" + sample.LastAcdAttackedBy.ToString(CultureInfo.InvariantCulture) +
                    " lastDamageMainActor=" + sample.LastDamageMainActor.ToString(CultureInfo.InvariantCulture) +
                    " meAcd=" + meAcd.ToString(CultureInfo.InvariantCulture) +
                    " meAnn=" + meAnn.ToString(CultureInfo.InvariantCulture) +
                    " exactAcdMatch=" + exactAcdMatch.ToString() +
                    " annMatch=" + annMatch.ToString() +
                    GetLocalAttackDebug(tick);

                Hud.TextLog.Log("s7o_RiftInfo_StrickenDebug", line);
            }
            catch
            {
            }
        }

        private void DebugStrickenAttribution(
            IPlayer player,
            PendingStrickenProc proc,
            StrickenGlobalDamageSample first,
            int tick,
            string result,
            StrickenDamageSourceMatch sourceMatch)
        {
            if (!EnableStrickenDebug)
                return;

            try
            {
                uint meAcd = 0;
                uint meAnn = 0;

                try { if (Hud.Game.Me != null) meAcd = Hud.Game.Me.AcdId; } catch { }
                try { if (Hud.Game.Me != null) meAnn = Hud.Game.Me.AnnId; } catch { }

                string firstText = "null";

                if (first != null)
                {
                    bool exactAcdMatch =
                        meAcd != 0 &&
                        (first.LastDamageAcd == meAcd || first.LastAcdAttackedBy == meAcd);

                    bool annMatch =
                        meAnn != 0 &&
                        (first.LastDamageAcd == meAnn || first.LastAcdAttackedBy == meAnn);

                    firstText =
                        "targetAcdId=" + first.TargetKey.ToString(CultureInfo.InvariantCulture) +
                        " name=" + first.Name +
                        " code=" + first.Code +
                        " dmgTick=" + first.LastDamageTick.ToString(CultureInfo.InvariantCulture) +
                        " lastDamageAcd=" + first.LastDamageAcd.ToString(CultureInfo.InvariantCulture) +
                        " lastAcdAttackedBy=" + first.LastAcdAttackedBy.ToString(CultureInfo.InvariantCulture) +
                        " exactAcdMatch=" + exactAcdMatch.ToString() +
                        " annMatch=" + annMatch.ToString();
                }

                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " phase=stricken-attribution" +
                    " result=" + result +
                    " sourceMatch=" + sourceMatch.ToString() +
                    " procTick=" + (proc == null ? "0" : proc.ProcTick.ToString(CultureInfo.InvariantCulture)) +
                    " lockedTargetKey=" + (proc == null ? "0" : proc.LockedTargetKey.ToString(CultureInfo.InvariantCulture)) +
                    " lockedTargetName=" + (proc == null ? "" : proc.LockedTargetName) +
                    " localAttackTick=" + (proc == null ? "0" : proc.LocalAttackTick.ToString(CultureInfo.InvariantCulture)) +
                    " localAttackRecentAtProc=" + (proc == null ? "False" : proc.LocalAttackRecentAtProc.ToString()) +
                    " localAttackState=" + (proc == null ? "" : proc.LocalAttackState) +
                    " localAttackAnim=" + (proc == null ? "" : proc.LocalAttackAnimation) +
                    GetLocalAttackDebug(tick) +
                    " first=" + firstText;

                Hud.TextLog.Log("s7o_RiftInfo_StrickenDebug", line);
            }
            catch
            {
            }
        }


        private void DebugStrickenStackIncrement(uint heroId, int targetKey, int tick, int stacks, string reason)
        {
            if (!EnableStrickenDebug)
                return;

            try
            {
                string line =
                    "tick=" + tick.ToString(CultureInfo.InvariantCulture) +
                    " phase=stack-increment" +
                    " heroId=" + heroId.ToString(CultureInfo.InvariantCulture) +
                    " targetAcdId=" + targetKey.ToString(CultureInfo.InvariantCulture) +
                    " stacks=" + stacks.ToString(CultureInfo.InvariantCulture) +
                    " reason=" + reason;

                Hud.TextLog.Log("s7o_RiftInfo_StrickenDebug", line);
            }
            catch
            {
            }
        }

        private string GetDebugPlayerName(IPlayer player)
        {
            if (player == null)
                return "null";

            try
            {
                return player.Index.ToString(CultureInfo.InvariantCulture) + ":" + player.HeroName;
            }
            catch
            {
                return "player";
            }
        }

        private string GetDebugMonster(IMonster m)
        {
            if (m == null)
                return "null";

            string name = "";

            try
            {
                if (m.SnoMonster != null)
                    name = m.SnoMonster.NameLocalized;
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    if (m.SnoMonster != null)
                        name = m.SnoMonster.Code;
                }
                catch
                {
                }
            }

            string code = "";

            try
            {
                if (m.SnoMonster != null)
                    code = m.SnoMonster.Code;
            }
            catch
            {
            }

            return
                "acd=" + GetMonsterTargetKey(m).ToString(CultureInfo.InvariantCulture) +
                " name=" + name +
                " code=" + code +
                " rarity=" + m.Rarity.ToString() +
                " hp=" + SafeMonsterHealthText(m);
        }

        private string SafeMonsterHealthText(IMonster m)
        {
            try
            {
                return m.CurHealth.ToString("F0", CultureInfo.InvariantCulture) + "/" +
                       m.MaxHealth.ToString("F0", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "?";
            }
        }

        private string GetPrimaryStrickenProcDebug(IPlayer player)
        {
            if (player == null || player.Powers == null)
                return "no-player";

            try
            {
                var buff = player.Powers.GetBuff(StrickenSno);

                string active0 = SafeBuffIsActive(player, StrickenSno, 0) ? "1" : "0";
                string active2 = SafeBuffIsActive(player, StrickenSno, StrickenProcIconIndex) ? "1" : "0";

                if (buff == null)
                {
                    return "primary428348 active0=" + active0 +
                           " active2=" + active2 +
                           " buff=null";
                }

                string count2 = "?";
                string elapsed2 = "?";
                string left2 = "?";

                try
                {
                    if (buff.IconCounts != null && buff.IconCounts.Length > StrickenProcIconIndex)
                        count2 = buff.IconCounts[StrickenProcIconIndex].ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                }

                try
                {
                    if (buff.TimeElapsedSeconds != null && buff.TimeElapsedSeconds.Length > StrickenProcIconIndex)
                        elapsed2 = buff.TimeElapsedSeconds[StrickenProcIconIndex].ToString("F2", CultureInfo.InvariantCulture);
                }
                catch
                {
                }

                try
                {
                    if (buff.TimeLeftSeconds != null && buff.TimeLeftSeconds.Length > StrickenProcIconIndex)
                        left2 = buff.TimeLeftSeconds[StrickenProcIconIndex].ToString("F2", CultureInfo.InvariantCulture);
                }
                catch
                {
                }

                return "primary428348 active0=" + active0 +
                       " active2=" + active2 +
                       " count2=" + count2 +
                       " elapsed2=" + elapsed2 +
                       " left2=" + left2;
            }
            catch (Exception ex)
            {
                return "primary-debug-error=" + ex.GetType().Name;
            }
        }

        private string GetStrickenBuffDebug(IPlayer player)
        {
            return
                "primary{" + GetSingleStrickenBuffDebug(player, StrickenSno) + "} " +
                "secondary{" + GetSingleStrickenBuffDebug(player, StrickenSecondarySno) + "}";
        }

        private string GetSingleStrickenBuffDebug(IPlayer player, uint sno)
        {
            if (player == null || player.Powers == null)
                return "no-player";

            try
            {
                var buff = player.Powers.GetBuff(sno);
                string activeIdx = GetStrickenIconActiveDebug(player, sno);

                if (buff == null)
                    return "sno=" + sno.ToString(CultureInfo.InvariantCulture) +
                           " buff=null activeIdx=" + activeIdx;

                string counts = ArrayToDebugString(buff.IconCounts);
                string elapsed = ArrayToDebugString(buff.TimeElapsedSeconds);
                string left = ArrayToDebugString(buff.TimeLeftSeconds);

                return
                    "sno=" + sno.ToString(CultureInfo.InvariantCulture) +
                    " buffActive=" + buff.Active.ToString() +
                    " activeIdx=" + activeIdx +
                    " counts=" + counts +
                    " elapsed=" + elapsed +
                    " left=" + left;
            }
            catch (Exception ex)
            {
                return "sno=" + sno.ToString(CultureInfo.InvariantCulture) +
                       " buff-error=" + ex.GetType().Name;
            }
        }

        private string GetStrickenIconActiveDebug(IPlayer player, uint sno)
        {
            if (player == null || player.Powers == null)
                return "";

            string s = "";

            for (int i = 0; i <= 7; i++)
            {
                bool active = SafeBuffIsActive(player, sno, i);
                if (i > 0) s += ",";
                s += i.ToString(CultureInfo.InvariantCulture) + ":" + (active ? "1" : "0");
            }

            return s;
        }

        private string ArrayToDebugString(int[] values)
        {
            if (values == null)
                return "null";

            int len = Math.Min(values.Length, 8);
            string s = "[";

            for (int i = 0; i < len; i++)
            {
                if (i > 0) s += ",";
                s += values[i].ToString(CultureInfo.InvariantCulture);
            }

            return s + "]";
        }

        private string ArrayToDebugString(double[] values)
        {
            if (values == null)
                return "null";

            int len = Math.Min(values.Length, 8);
            string s = "[";

            for (int i = 0; i < len; i++)
            {
                if (i > 0) s += ",";
                s += values[i].ToString("F2", CultureInfo.InvariantCulture);
            }

            return s + "]";
        }
    }
}
