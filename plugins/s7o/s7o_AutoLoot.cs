using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // REV02 preserves REV01's breadcrumb return, but latches a successful Urshi actor click
    // so later breadcrumb clicks cannot replace Diablo's native path. A bounded coordinate
    // watchdog disengages the automatic approach when an issued command is truly stalled.
    public class s7o_AutoLoot : BasePlugin, IAfterCollectHandler, IItemPickedHandler, IItemLocationChangedHandler, INewAreaHandler
    {
        private const int RunRange = 5;
        private const int IdleRange = 10;
        private const int SpecialCleanupRange = 40;
        private const int LootBurstMonsterBlockYards = 45;
        private const int LootBurstThreshold = 8;
        private const int LootBurstLatchMs = 5000;
        private const int NormalDelayMs = 80;
        private const int CursorRestoreDelayMs = 15;
        private const int CursorRestoreExpireMs = 250;
        private const int CleanupDelayMs = 25;
        private const int CleanupFarMoveDelayMs = 220;
        private const int SpecialCleanupDelayMs = 55;
        private const int SpecialCleanupFarMoveDelayMs = 180;
        private const int StackedLootDelayMs = 22;
        private const int StackedLootSkipMs = 75;
        private const float StackedLootScreenRadiusPx = 22f;
        private const float StackedLootWorldRadiusYards = 1.8f;
        private const int MovementSampleMs = 90;
        private const float MovementThresholdYards = 0.22f;
        private const int MaxAttempts = 8;
        private const int StuckRetryCooldownMs = 6000;
        private const int NoSpacePickupRetryCooldownMs = 1800;
        private const int ProtectedChestBlockYards = 45;
        private const int ProtectedChestRiskYards = 16;
        private const int VisionFightBlockYards = 70;
        private const int UrshiRiskYards = 16;
        private const int CleanupMonsterBlockYards = 45;
        private const int UrshiPanelRecoveryWindowMs = 2200;
        private const int UrshiPanelConfirmDelayMs = 60;
        private const int UrshiRiskClickDelayMs = 70;
        private const int UrshiRiskHoverSettleMs = 28;
        private const int UrshiRiskHoverRetryMs = 22;
        private const int UrshiRiskMaxCycleAttempts = 12;
        private const int UrshiRiskRetryCooldownMs = 450;
        private const int UrshiRiskRotateItemMs = 120;
        private const int UrshiSpaceRetryMs = 70;
        private const int UrshiSpaceMaxAttempts = 3;
        private const int UrshiProblemItemSuppressMs = 1800;
        private const int UrshiFallbackRetryDelayMs = 70;
        private const int UrshiFallbackWindowMs = 2200;
        private const int UrshiFallbackMaxTries = 8;
        private const int AutoUrshiTalkNoLootSettleMs = 220;
        private const int AutoUrshiTalkWaitForLootDropMs = 1800;
        private const int AutoUrshiTalkClickDelayMs = 700;
        private const int AutoUrshiTalkMaxAttempts = 12;
        private const int AutoUrshiTalkRetryCooldownMs = 8000;
        private const int AutoUrshiTalkHoverSettleMs = 70;
        private const int AutoUrshiTalkProbeRetryMs = 20;
        private const int AutoUrshiRecentTalkLootCancelWindowMs = 1800;
        private const int AutoUrshiTalkLootCancelRetryMs = 70;
        private const int AutoUrshiTalkLootCancelMaxAttempts = 3;
        private const float AutoUrshiFarLootRiskYards = 55f;
        private const float AutoUrshiBreadcrumbStepYards = 12f;
        private const float AutoUrshiReturnMinClickYards = 5f;
        private const int AutoUrshiBreadcrumbMax = 8;
        private const int AutoUrshiReturnClickDelayMs = 120;
        private const int AutoUrshiReturnMaxClicks = 10;
        private const int AutoUrshiApproachStallMs = AutoUrshiTalkClickDelayMs * 2;
        private const float AutoUrshiApproachProgressYards = 1.0f;
        private const int DroppedItemIgnoreMs = 20000;
        private const int DroppedItemVisibilityGraceMs = 500;
        private const int CleanupStuckIgnoreMs = 8000;
        private const uint RamaladniGiftSno = 1844495708;
        private const uint PetrifiedScreamSno = 1051857800;
        private const uint WhisperLowSno = 685356142;
        private const uint WhisperHighSno = 1141915165;

        private static readonly ActorSnoEnum[] NoSpaceActors =
        {
            ActorSnoEnum._crafting_assortedparts_05,
            ActorSnoEnum._crafting_magic_05,
            ActorSnoEnum._crafting_rare_05,
            ActorSnoEnum._crafting_legendary_05,
            ActorSnoEnum._crafting_looted_reagent_05,
            ActorSnoEnum._craftingreagent_legendary_set_borns_x1,
            ActorSnoEnum._craftingreagent_legendary_set_cains_x1,
            ActorSnoEnum._craftingreagent_legendary_set_demon_x1,
            ActorSnoEnum._craftingreagent_legendary_set_hallowed_x1,
            ActorSnoEnum._craftingreagent_legendary_set_captaincrimsons_x1,
            ActorSnoEnum._demonorgan_skeletonking_x1,
            ActorSnoEnum._demonorgan_ghom_x1,
            ActorSnoEnum._demonorgan_siegebreaker_x1,
            ActorSnoEnum._demonorgan_diablo_x1,
        };

        private static readonly ActorSnoEnum[] PlanActors =
        {
            ActorSnoEnum._craftingplan_smith_drop,
            ActorSnoEnum._craftingplan_jeweler_drop,
            ActorSnoEnum._craftingplan_smith_drop_soulbound,
            ActorSnoEnum._craftingplan_mystic_transmog_drop,
            ActorSnoEnum._craftingplan_mystic_transmog_drop_bound,
        };

        private readonly Dictionary<int, int> _attempts = new Dictionary<int, int>();
        private readonly Dictionary<int, long> _retryAfterMs = new Dictionary<int, long>();
        private readonly Dictionary<int, DropSuppress> _droppedSuppress = new Dictionary<int, DropSuppress>();
        private readonly Dictionary<int, long> _cleanupStuckIgnoreUntilMs = new Dictionary<int, long>();
        private readonly Dictionary<int, long> _stackedLootSkipUntilMs = new Dictionary<int, long>();
        private readonly Dictionary<int, int> _urshiMisclicksBySeed = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _urshiFallbackTriesBySeed = new Dictionary<int, int>();
        private IUiElement _urshiGemPane;
        private IUiElement _urshiConversationMain;
        private IUiElement _chatEditLine;
        private long _lastClickMs;
        private long _urshiArmedUntilMs;
        private long _nextUrshiRiskClickMs;
        private long _urshiRiskHoverClickAtMs;
        private int _urshiRiskHoverSeed;
        private int _urshiRiskHoverX;
        private int _urshiRiskHoverY;
        private long _nextUrshiSpaceMs;
        private int _lastClickSeed;
        private int _stackedLootClickPhase;
        private int _lootProgressSerial;
        private int _lastRetryRefreshSerial;
        private int _lastCleanupIgnoreSerial;
        private int _lastVisibleEligibleLootCount;
        private int _urshiArmedSeed;
        private int _urshiSpaceAttempts;
        private int _urshiFallbackSeed;
        private long _urshiFallbackUntilMs;
        private int _urshiRecoveryOpenSeed;
        private long _urshiRecoveryOpenSeenMs;
        private bool _urshiRecoveryMisclickRecorded;
        private int _genericUrshiRecoverySeed;
        private long _genericUrshiRecoveryUntilMs;
        private int _genericUrshiRecoveryAttempts;
        private bool _genericUrshiRecoveryMisclickRecorded;
        private long _autoUrshiNoLootSinceMs;
        private long _nextAutoUrshiTalkMs;
        private long _autoUrshiTalkCooldownUntilMs;
        private long _autoUrshiHoverClickAtMs;
        private int _autoUrshiTalkAttempts;
        private int _autoUrshiHoverX;
        private int _autoUrshiHoverY;
        private bool _autoUrshiTalkDone;
        private long _autoUrshiRecentTalkOpenedUntilMs;
        private int _autoUrshiTalkLootCancelAttempts;
        private long _nextAutoUrshiTalkLootCancelMs;
        private bool _autoUrshiHasRestorePoint;
        private NativePoint _autoUrshiRestorePoint;
        private long _postRiftCleanupStartedMs;
        private long _autoUrshiLootDropGateStartedMs;
        private bool _autoUrshiSawEligibleLootThisCleanup;
        private readonly List<AutoUrshiReturnPoint> _autoUrshiReturnTrail = new List<AutoUrshiReturnPoint>(AutoUrshiBreadcrumbMax);
        private bool _autoUrshiHasLastSeenWorld;
        private float _autoUrshiLastSeenX;
        private float _autoUrshiLastSeenY;
        private float _autoUrshiLastSeenZ;
        private long _autoUrshiLastSeenMs;
        private long _nextAutoUrshiReturnMs;
        private int _autoUrshiReturnClicks;
        private bool _autoUrshiReturning;
        private bool _autoUrshiActorPathActive;
        private bool _autoUrshiApproachAborted;
        private long _autoUrshiApproachSampleMs;
        private float _autoUrshiApproachSampleX;
        private float _autoUrshiApproachSampleY;
        private bool _cleanupLatched;
        private bool _lastCleanupClickFar;
        private bool _enabled;
        private bool _paused;
        private bool _talkToUrshiAfterLoot;
        private bool _primals = true, _ancients = true, _legendaries = true, _gems = true, _gifts = true, _screams = true, _trash, _materials = true, _deathsBreath;
        private uint _lastAreaSno;
        private long _lootBurstCleanupUntilMs;
        private long _lastMovementSampleMs;
        private float _lastPlayerX;
        private float _lastPlayerY;
        private bool _playerMoving;
        private bool _pendingCursorRestore;
        private NativePoint _pendingCursorPoint;
        private long _pendingCursorRestoreAtMs;
        private long _pendingCursorRestoreExpireMs;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public s7o_AutoLoot()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            _urshiGemPane = Hud.Render.RegisterUiElement("Root.NormalLayer.vendor_dialog_mainPage.riftReward_dialog.LayoutRoot.gemUpgradePane", null, null);
            _urshiConversationMain = Hud.Render.RegisterUiElement("Root.NormalLayer.conversation_dialog_main", null, null);
            _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);
            if (Hud.Game.IsInGame && Hud.Game.Me != null && Hud.Game.Me.SnoArea != null)
                _lastAreaSno = Hud.Game.Me.SnoArea.Sno;
        }

        public void ConfigureAutoLoot(bool enabled, bool primals, bool ancients, bool legendaries, bool gems, bool gifts, bool screams, bool trash, bool materials, bool deathsBreath)
        {
            ConfigureAutoLoot(enabled, primals, ancients, legendaries, gems, gifts, screams, trash, materials, deathsBreath, false);
        }

        public void ConfigureAutoLoot(bool enabled, bool primals, bool ancients, bool legendaries, bool gems, bool gifts, bool screams, bool trash, bool materials, bool deathsBreath, bool talkToUrshiAfterLoot)
        {
            _enabled = enabled;
            _primals = primals;
            _ancients = ancients;
            _legendaries = legendaries;
            _gems = gems;
            _gifts = gifts;
            _screams = screams;
            _trash = trash;
            _materials = materials;
            _deathsBreath = deathsBreath;
            _talkToUrshiAfterLoot = talkToUrshiAfterLoot;
            if (!_enabled)
            {
                _paused = false;
                ResetRuntimeState();
            }
        }

        public bool IsPaused { get { return _paused; } }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            ResetRuntimeState(true);
        }

        private void ResetRuntimeState(bool keepDroppedSuppress = false)
        {
            _attempts.Clear();
            _retryAfterMs.Clear();
            _stackedLootSkipUntilMs.Clear();
            if (!keepDroppedSuppress)
                _droppedSuppress.Clear();
            _cleanupStuckIgnoreUntilMs.Clear();
            _lastClickSeed = 0;
            _stackedLootClickPhase = 0;
            _lootProgressSerial = 0;
            _lastRetryRefreshSerial = 0;
            _lastCleanupIgnoreSerial = 0;
            _lastVisibleEligibleLootCount = -1;
            _lastCleanupClickFar = false;
            _cleanupLatched = false;
            _lootBurstCleanupUntilMs = 0;
            _lastMovementSampleMs = 0;
            _playerMoving = false;
            _pendingCursorRestore = false;
            _urshiArmedUntilMs = 0;
            _nextUrshiRiskClickMs = 0;
            ClearUrshiRiskLootHover();
            _nextUrshiSpaceMs = 0;
            _urshiArmedSeed = 0;
            _urshiSpaceAttempts = 0;
            _urshiMisclicksBySeed.Clear();
            _urshiFallbackTriesBySeed.Clear();
            _urshiFallbackSeed = 0;
            _urshiFallbackUntilMs = 0;
            _urshiRecoveryOpenSeed = 0;
            _urshiRecoveryOpenSeenMs = 0;
            _urshiRecoveryMisclickRecorded = false;
            ClearGenericUrshiRecoveryState();
            _autoUrshiNoLootSinceMs = 0;
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            _autoUrshiHasRestorePoint = false;
            _autoUrshiRestorePoint = new NativePoint();
            _postRiftCleanupStartedMs = 0;
            _autoUrshiLootDropGateStartedMs = 0;
            _autoUrshiSawEligibleLootThisCleanup = false;
            ResetAutoUrshiReturnState();
        }

        public void OnItemPicked(IItem item)
        {
            if (item == null) return;
            MarkLootPickupProgress();
            _attempts.Remove(item.Seed);
            _retryAfterMs.Remove(item.Seed);
            if (_lastClickSeed == item.Seed) _lastClickSeed = 0;
            _droppedSuppress.Remove(item.Seed);
            _stackedLootSkipUntilMs.Remove(item.Seed);
            _urshiMisclicksBySeed.Remove(item.Seed);
            _urshiFallbackTriesBySeed.Remove(item.Seed);
            if (_urshiFallbackSeed == item.Seed)
            {
                _urshiFallbackSeed = 0;
                _urshiFallbackUntilMs = 0;
            }
            if (_urshiArmedSeed == item.Seed)
                ClearUrshiArmedRecoveryState(true);
            if (_genericUrshiRecoverySeed == item.Seed)
                ClearGenericUrshiRecoveryState();
        }

        public void OnItemLocationChanged(IItem item, ItemLocation from, ItemLocation to)
        {
            if (item == null) return;
            if (from == ItemLocation.Floor && to != ItemLocation.Floor)
            {
                MarkLootPickupProgress();

                if (item.Seed == _genericUrshiRecoverySeed)
                    ClearGenericUrshiRecoveryState();
            }
            if (to != ItemLocation.Floor)
                _cleanupStuckIgnoreUntilMs.Remove(item.Seed);
            if (from == ItemLocation.Inventory && to == ItemLocation.Floor)
            {
                long now = Hud.Game.CurrentRealTimeMilliseconds;
                _droppedSuppress[item.Seed] = new DropSuppress(now + DroppedItemIgnoreMs, now + DroppedItemVisibilityGraceMs);
            }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            uint sno = area != null ? area.Sno : 0;
            if (newGame || sno != _lastAreaSno)
            {
                _lastAreaSno = sno;
                ResetRuntimeState();
            }
        }

        public void AfterCollect()
        {
            if (Hud != null && Hud.Game != null && Hud.Game.IsInGame)
                ProcessPendingCursorRestore(Hud.Game.CurrentRealTimeMilliseconds);

            if (!_enabled || _paused || Hud == null || Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.IsPaused || !Hud.Window.IsForeground)
                return;

            long now = Hud.Game.CurrentRealTimeMilliseconds;
            PurgeDroppedSuppressions(now);
            PurgeStackedLootSkips(now);
            PurgeRetryState(now);
            PurgeCleanupStuckIgnores(now);
            PurgeResolvedUrshiArmedState(now);

            // Urshi recovery must run before inventory/vendor UI early returns because Urshi opens UI layers.
            if (HandleAutoLootUrshiRecovery(now))
                return;

            if (Hud.Render.WorldMapUiElement != null && Hud.Render.WorldMapUiElement.Visible)
                return;
            if (Hud.Inventory != null && Hud.Inventory.InventoryMainUiElement != null && Hud.Inventory.InventoryMainUiElement.Visible)
                return;
            if (Hud.Inventory != null && Hud.Inventory.StashMainUiElement != null && Hud.Inventory.StashMainUiElement.Visible)
                return;
            if (Hud.Game.Me == null || Hud.Game.Me.Powers == null || Hud.Game.Me.Powers.CantMove)
                return;

            bool inTown = Hud.Game.IsInTown;
            IActor protectedChest = GetUnopenedProtectedChest();
            bool protectedChestBlocked = protectedChest != null;
            bool postRiftCleanup = !inTown && !protectedChestBlocked && IsPostRiftCleanup();
            TrackPostRiftCleanupWindow(postRiftCleanup, now);
            bool lootBurstCleanup = !protectedChestBlocked && IsLootBurstCleanup(now, inTown);
            bool wideCleanup = postRiftCleanup || lootBurstCleanup;
            var state = Hud.Game.Me.AnimationState;
            bool combatAction = state == AcdAnimationState.Attacking || state == AcdAnimationState.Casting || state == AcdAnimationState.Channeling;
            bool playerMoving = UpdatePlayerMovement(now);

            if (state == AcdAnimationState.CastingPortal)
                return;
            if (inTown && !lootBurstCleanup)
                return;
            if (!wideCleanup && combatAction && !playerMoving)
                return;

            int range = postRiftCleanup ? int.MaxValue : (lootBurstCleanup ? SpecialCleanupRange : ((state == AcdAnimationState.Running || (combatAction && playerMoving)) ? RunRange : IdleRange));
            int freeSlots = SafeFreeSlots();
            IActor urshi = GetUrshiActor();
            TrackAutoUrshiReturnState(postRiftCleanup, now, urshi);

            var candidates = Hud.Game.Items
                .Where(i => i != null && i.Location == ItemLocation.Floor && i.IsOnScreen && !IsExcludedPickup(i) && !IsSuppressedDroppedItem(i, now) && !IsCleanupStuckIgnored(i, now) && !IsProtectedChestRisk(i, protectedChest) && i.CentralXyDistanceToMe <= range)
                .Select(i => new LootCandidate(i, WantedPriority(i), IsUrshiRisk(i, urshi)))
                .Where(c => c.Priority >= 0 && CanFit(c.Item, freeSlots))
                .ToList();

            TrackVisibleEligibleLootProgress(candidates.Count);
            if (postRiftCleanup && candidates.Count > 0)
            {
                _autoUrshiSawEligibleLootThisCleanup = true;
                _autoUrshiLootDropGateStartedMs = 0;
                _autoUrshiReturning = false;
            }

            if (candidates.Count == 0)
            {
                if (postRiftCleanup)
                {
                    if (_autoUrshiApproachAborted)
                        return;

                    if ((_autoUrshiActorPathActive || _autoUrshiReturning) &&
                        !UpdateAutoUrshiApproachProgress(now))
                        return;

                    // An actor click hands movement to Diablo's pathfinder. Never replace
                    // that command with a breadcrumb click if on-screen state flickers.
                    if (_autoUrshiActorPathActive)
                    {
                        if (IsAutoUrshiTalkActorClickable(urshi))
                            TryTalkToUrshiAfterLoot(now, urshi);
                        return;
                    }

                    if (IsAutoUrshiTalkActorClickable(urshi))
                        TryTalkToUrshiAfterLoot(now, urshi);
                    else
                        TryReturnTowardAutoUrshi(now);
                }
                else
                {
                    ResetAutoUrshiTalkReadyState();
                }

                return;
            }

            AbortAutoUrshiTalkForVisibleLoot(now);

            bool stackedLoot = HasStackedLootCluster(candidates);
            bool noSpacePickupOnScreen = candidates.Any(c => IsNoSpaceMaterialPickup(c.Item));
            int delay = postRiftCleanup
                ? (_lastCleanupClickFar ? CleanupFarMoveDelayMs : CleanupDelayMs)
                : (lootBurstCleanup ? (_lastCleanupClickFar ? SpecialCleanupFarMoveDelayMs : SpecialCleanupDelayMs) : NormalDelayMs);
            if ((stackedLoot || (postRiftCleanup && noSpacePickupOnScreen)) && delay > StackedLootDelayMs)
                delay = StackedLootDelayMs;
            if (now - _lastClickMs < delay)
                return;

            var tryCandidates = candidates.Where(c => CanTry(c.Item, c.UrshiRisk, now)).ToList();
            if (tryCandidates.Count == 0 && RefreshRetryStateAfterLootProgress(candidates, now))
                tryCandidates = candidates.Where(c => CanTry(c.Item, c.UrshiRisk, now)).ToList();

            if (tryCandidates.Count == 0)
            {
                ResetAutoUrshiTalkReadyState();
                return;
            }

            bool farUrshiLootRisk = postRiftCleanup && HasAutoUrshiFarLootRisk(tryCandidates);
            var target = SelectBestCandidate(tryCandidates, wideCleanup, now, stackedLoot, farUrshiLootRisk);
            if (target == null && stackedLoot)
                target = SelectBestCandidate(tryCandidates, wideCleanup, now, false, farUrshiLootRisk);

            if (target == null)
            {
                ResetAutoUrshiTalkReadyState();
                return;
            }

            ClickItem(target.Item, target.UrshiRisk && urshi != null, wideCleanup, stackedLoot && IsStackedWithAnother(target, candidates), now);
        }

        private void MarkLootPickupProgress()
        {
            _lootProgressSerial = _lootProgressSerial == int.MaxValue ? 1 : _lootProgressSerial + 1;
        }

        private void TrackPostRiftCleanupWindow(bool postRiftCleanup, long now)
        {
            if (postRiftCleanup)
            {
                if (_postRiftCleanupStartedMs == 0)
                {
                    _postRiftCleanupStartedMs = now;
                    _autoUrshiSawEligibleLootThisCleanup = false;
                }
                return;
            }

            _postRiftCleanupStartedMs = 0;
            _autoUrshiLootDropGateStartedMs = 0;
            _autoUrshiSawEligibleLootThisCleanup = false;
            ResetAutoUrshiReturnState();
        }

        private void ClearGenericUrshiRecoveryState()
        {
            _genericUrshiRecoverySeed = 0;
            _genericUrshiRecoveryUntilMs = 0;
            _genericUrshiRecoveryAttempts = 0;
            _genericUrshiRecoveryMisclickRecorded = false;
        }

        private void ArmGenericUrshiPickupRecovery(IItem item, bool cleanup, long now)
        {
            if (!_talkToUrshiAfterLoot || !cleanup || item == null)
                return;

            _genericUrshiRecoverySeed = item.Seed;
            _genericUrshiRecoveryUntilMs = now + UrshiPanelRecoveryWindowMs;
            _genericUrshiRecoveryAttempts = 0;
            _genericUrshiRecoveryMisclickRecorded = false;
        }

        private void ResetAutoUrshiReturnState()
        {
            _autoUrshiReturnTrail.Clear();
            _autoUrshiHasLastSeenWorld = false;
            _autoUrshiLastSeenX = 0f;
            _autoUrshiLastSeenY = 0f;
            _autoUrshiLastSeenZ = 0f;
            _autoUrshiLastSeenMs = 0;
            _nextAutoUrshiReturnMs = 0;
            _autoUrshiReturnClicks = 0;
            _autoUrshiReturning = false;
            _autoUrshiActorPathActive = false;
            _autoUrshiApproachAborted = false;
            ResetAutoUrshiApproachSample();
        }

        private void TrackAutoUrshiReturnState(bool postRiftCleanup, long now, IActor urshi)
        {
            if (!_talkToUrshiAfterLoot || !postRiftCleanup)
                return;

            try
            {
                if (urshi != null && urshi.FloorCoordinate != null)
                {
                    _autoUrshiHasLastSeenWorld = true;
                    _autoUrshiLastSeenX = urshi.FloorCoordinate.X;
                    _autoUrshiLastSeenY = urshi.FloorCoordinate.Y;
                    _autoUrshiLastSeenZ = urshi.FloorCoordinate.Z;
                    _autoUrshiLastSeenMs = now;

                    if (urshi.IsOnScreen && urshi.ScreenCoordinate != null)
                    {
                        _autoUrshiReturnClicks = 0;
                        _nextAutoUrshiReturnMs = 0;
                        if (_autoUrshiReturning)
                            ResetAutoUrshiApproachSample();
                        _autoUrshiReturning = false;
                    }
                }

                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (!_autoUrshiHasLastSeenWorld || me == null || me.FloorCoordinate == null)
                    return;

                if (_autoUrshiReturning)
                    return;

                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;
                float z = me.FloorCoordinate.Z;

                if (_autoUrshiReturnTrail.Count > 0)
                {
                    var last = _autoUrshiReturnTrail[_autoUrshiReturnTrail.Count - 1];
                    float dx = x - last.X;
                    float dy = y - last.Y;
                    if ((dx * dx + dy * dy) < AutoUrshiBreadcrumbStepYards * AutoUrshiBreadcrumbStepYards)
                        return;
                }

                _autoUrshiReturnTrail.Add(new AutoUrshiReturnPoint(x, y, z));

                while (_autoUrshiReturnTrail.Count > AutoUrshiBreadcrumbMax)
                    _autoUrshiReturnTrail.RemoveAt(0);
            }
            catch { }
        }

        private bool HasAutoUrshiFarLootRisk(List<LootCandidate> candidates)
        {
            if (!_talkToUrshiAfterLoot || !_autoUrshiHasLastSeenWorld || candidates == null || candidates.Count == 0)
                return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i] != null ? candidates[i].Item : null;
                if (DistanceToLastSeenUrshi(item) >= AutoUrshiFarLootRiskYards)
                    return true;
            }

            return false;
        }

        private float DistanceToLastSeenUrshi(IItem item)
        {
            try
            {
                if (!_autoUrshiHasLastSeenWorld || item == null || item.FloorCoordinate == null)
                    return 0f;

                return item.FloorCoordinate.XYDistanceTo(_autoUrshiLastSeenX, _autoUrshiLastSeenY);
            }
            catch { return 0f; }
        }

        private void TrackVisibleEligibleLootProgress(int count)
        {
            if (_lastVisibleEligibleLootCount >= 0 && count < _lastVisibleEligibleLootCount)
                MarkLootPickupProgress();
            _lastVisibleEligibleLootCount = count;
        }

        private bool RefreshRetryStateAfterLootProgress(List<LootCandidate> candidates, long now)
        {
            if (_lootProgressSerial == 0 || _lastRetryRefreshSerial == _lootProgressSerial || candidates == null || candidates.Count == 0)
                return false;

            bool changed = false;
            bool hasUrshiRisk = false;
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Item == null) continue;
                int seed = candidate.Item.Seed;
                if (_retryAfterMs.Remove(seed)) changed = true;
                if (_attempts.Remove(seed)) changed = true;
                if (_stackedLootSkipUntilMs.Remove(seed)) changed = true;
                if (candidate.UrshiRisk)
                {
                    hasUrshiRisk = true;
                    if (_urshiMisclicksBySeed.Remove(seed)) changed = true;
                    if (_urshiFallbackTriesBySeed.Remove(seed)) changed = true;
                    if (_urshiFallbackSeed == seed)
                    {
                        _urshiFallbackSeed = 0;
                        _urshiFallbackUntilMs = 0;
                        changed = true;
                    }
                }
            }

            if (hasUrshiRisk)
            {
                ClearUrshiRiskLootHover();
                _nextUrshiRiskClickMs = 0;
                changed = true;
            }

            _lastRetryRefreshSerial = _lootProgressSerial;
            return changed;
        }

        private void PurgeDroppedSuppressions(long now)
        {
            if (_droppedSuppress.Count == 0) return;
            foreach (var seed in _droppedSuppress.Keys.ToArray())
            {
                DropSuppress block = _droppedSuppress[seed];
                if (now >= block.Until || (now >= block.VisibleCheckAfter && !IsVisibleFloorSeed(seed)))
                    _droppedSuppress.Remove(seed);
            }
        }

        private void PurgeStackedLootSkips(long now)
        {
            if (_stackedLootSkipUntilMs.Count == 0) return;
            foreach (var pair in _stackedLootSkipUntilMs.ToArray())
            {
                if (now >= pair.Value || !IsVisibleFloorSeed(pair.Key))
                    _stackedLootSkipUntilMs.Remove(pair.Key);
            }
        }

        private void PurgeCleanupStuckIgnores(long now)
        {
            if (_cleanupStuckIgnoreUntilMs.Count == 0) return;
            foreach (var pair in _cleanupStuckIgnoreUntilMs.ToArray())
            {
                if (now >= pair.Value || !IsVisibleFloorSeed(pair.Key))
                    _cleanupStuckIgnoreUntilMs.Remove(pair.Key);
            }
        }

        private void PurgeRetryState(long now)
        {
            if (_attempts.Count != 0)
            {
                foreach (var seed in _attempts.Keys.ToArray())
                    if (!IsVisibleFloorSeed(seed)) _attempts.Remove(seed);
            }

            if (_retryAfterMs.Count != 0)
            {
                foreach (var pair in _retryAfterMs.ToArray())
                {
                    if (now >= pair.Value || !IsVisibleFloorSeed(pair.Key))
                        _retryAfterMs.Remove(pair.Key);
                }
            }

            if (_urshiFallbackSeed != 0 && (now > _urshiFallbackUntilMs || !IsVisibleFloorSeed(_urshiFallbackSeed)))
            {
                _urshiFallbackSeed = 0;
                _urshiFallbackUntilMs = 0;
            }

            if (_urshiFallbackTriesBySeed.Count != 0)
            {
                foreach (var seed in _urshiFallbackTriesBySeed.Keys.ToArray())
                    if (!IsVisibleFloorSeed(seed)) _urshiFallbackTriesBySeed.Remove(seed);
            }

            if (_urshiMisclicksBySeed.Count != 0)
            {
                foreach (var seed in _urshiMisclicksBySeed.Keys.ToArray())
                    if (!IsVisibleFloorSeed(seed)) _urshiMisclicksBySeed.Remove(seed);
            }
        }


        private void PurgeResolvedUrshiArmedState(long now)
        {
            if (_urshiArmedSeed == 0)
                return;

            if (now > _urshiArmedUntilMs || FindVisibleFloorItemBySeed(_urshiArmedSeed) == null)
                ClearUrshiArmedRecoveryState(true);
        }

        private bool IsCleanupStuckIgnored(IItem item, long now)
        {
            long until;
            if (item == null || !_cleanupStuckIgnoreUntilMs.TryGetValue(item.Seed, out until)) return false;
            if (now >= until)
            {
                _cleanupStuckIgnoreUntilMs.Remove(item.Seed);
                return false;
            }
            return true;
        }

        private bool IsSuppressedDroppedItem(IItem item, long now)
        {
            DropSuppress block;
            if (item == null || !_droppedSuppress.TryGetValue(item.Seed, out block)) return false;
            if (now >= block.Until)
            {
                _droppedSuppress.Remove(item.Seed);
                return false;
            }
            return true;
        }

        private bool IsVisibleFloorSeed(int seed)
        {
            return Hud.Game.Items.Any(i => i != null && i.Seed == seed && i.Location == ItemLocation.Floor && i.IsOnScreen);
        }

        private bool CanTry(IItem item, bool riskyUrshi, long now)
        {
            int n;
            long retryAt;
            _attempts.TryGetValue(item.Seed, out n);
            if (_retryAfterMs.TryGetValue(item.Seed, out retryAt))
            {
                if (now < retryAt) return false;
                _retryAfterMs.Remove(item.Seed);
            }

            if (riskyUrshi)
            {
                bool fallbackMode = item.Seed == _urshiFallbackSeed && now <= _urshiFallbackUntilMs;

                if (now < _nextUrshiRiskClickMs) return false;

                if (fallbackMode)
                {
                    int fallbackTries;
                    _urshiFallbackTriesBySeed.TryGetValue(item.Seed, out fallbackTries);

                    if (fallbackTries >= UrshiFallbackMaxTries)
                    {
                        _retryAfterMs[item.Seed] = now + UrshiRiskRetryCooldownMs;
                        _nextUrshiRiskClickMs = now + UrshiRiskRetryCooldownMs;
                        _urshiFallbackSeed = 0;
                        _urshiFallbackUntilMs = 0;
                        return false;
                    }

                    return true;
                }

                if (n >= UrshiRiskMaxCycleAttempts)
                {
                    _attempts[item.Seed] = 0;
                    _retryAfterMs[item.Seed] = now + UrshiRiskRetryCooldownMs;
                    _nextUrshiRiskClickMs = now + UrshiRiskRetryCooldownMs;
                    if (_urshiArmedSeed == item.Seed)
                        ClearUrshiArmedRecoveryState(false);
                    return false;
                }
                return true;
            }

            if (n < MaxAttempts)
                return true;

            _attempts[item.Seed] = 0;
            _retryAfterMs[item.Seed] = now + (IsNoSpaceMaterialPickup(item) ? NoSpacePickupRetryCooldownMs : StuckRetryCooldownMs);
            return false;
        }

        private LootCandidate SelectBestCandidate(List<LootCandidate> candidates, bool wideCleanup, long now, bool respectStackedSkip, bool farUrshiLootRisk)
        {
            return candidates
                .Where(c => !respectStackedSkip || !IsStackedLootTemporarilySkipped(c.Item, now))
                .OrderBy(c => c.UrshiRisk ? 1 : 0)
                .ThenBy(c => c.Item.Seed == _lastClickSeed ? 1 : 0)
                .ThenByDescending(c => farUrshiLootRisk ? DistanceToLastSeenUrshi(c.Item) : 0f)
                .ThenBy(c => wideCleanup ? 0 : c.Priority)
                .ThenBy(c => c.Item.CentralXyDistanceToMe)
                .ThenBy(c => c.Priority)
                .FirstOrDefault();
        }

        private bool IsStackedLootTemporarilySkipped(IItem item, long now)
        {
            long until;
            if (item == null || !_stackedLootSkipUntilMs.TryGetValue(item.Seed, out until))
                return false;
            if (now < until)
                return true;
            _stackedLootSkipUntilMs.Remove(item.Seed);
            return false;
        }

        private bool HasStackedLootCluster(List<LootCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2) return false;
            for (int i = 0; i < candidates.Count; i++)
                if (IsStackedWithAnother(candidates[i], candidates))
                    return true;
            return false;
        }

        private bool IsStackedWithAnother(LootCandidate candidate, List<LootCandidate> candidates)
        {
            if (candidate == null || candidate.UrshiRisk || candidate.Item == null || candidate.Item.ScreenCoordinate == null || candidates == null)
                return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                LootCandidate other = candidates[i];
                if (other == null || other == candidate || other.UrshiRisk || other.Item == null || other.Item.Seed == candidate.Item.Seed || other.Item.ScreenCoordinate == null)
                    continue;
                if (IsStackedLootPair(candidate.Item, other.Item))
                    return true;
            }
            return false;
        }

        private bool IsStackedLootPair(IItem a, IItem b)
        {
            float dx = a.ScreenCoordinate.X - b.ScreenCoordinate.X;
            float dy = a.ScreenCoordinate.Y - b.ScreenCoordinate.Y;
            if (dx * dx + dy * dy <= StackedLootScreenRadiusPx * StackedLootScreenRadiusPx)
                return true;
            return a.FloorCoordinate != null && b.FloorCoordinate != null && a.FloorCoordinate.XYDistanceTo(b.FloorCoordinate) <= StackedLootWorldRadiusYards;
        }

        private void ClickItem(IItem item, bool riskyUrshi, bool cleanup, bool stackedLoot, long now)
        {
            NativePoint old = new NativePoint();
            bool restore = !cleanup && GetCursorPos(out old);
            int tries = 0;
            _attempts.TryGetValue(item.Seed, out tries);

            if (riskyUrshi && HandleUrshiRiskLootHoverClick(item, tries, now))
                return;

            int x, y;
            if ((stackedLoot || IsNoSpaceMaterialPickup(item)) && !riskyUrshi)
                GetStackedLootClickPoint(item, _stackedLootClickPhase++, cleanup, out x, out y);
            else
                GetClickPoint(item, tries, cleanup, out x, out y);

            if (!SetCursorPos(x, y))
            {
                _lastClickMs = now;
                return;
            }

            _attempts[item.Seed] = tries + 1;
            ClearUrshiArmedRecoveryState(true);

            ArmGenericUrshiPickupRecovery(item, cleanup, now);
            MouseLeftClick();
            if (restore) ScheduleCursorRestore(old, now);
            _lastClickSeed = item.Seed;
            if (stackedLoot)
                _stackedLootSkipUntilMs[item.Seed] = now + StackedLootSkipMs;
            _lastCleanupClickFar = cleanup && item.CentralXyDistanceToMe > IdleRange;
            _lastClickMs = now;
        }

        private bool IsAnyEligibleLootSelectedNear(IItem anchor)
        {
            if (anchor == null) return false;
            if (anchor.IsSelected) return true;

            try
            {
                if (anchor.FloorCoordinate == null || Hud == null || Hud.Game == null || Hud.Game.Items == null)
                    return false;

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item == anchor || item.Location != ItemLocation.Floor || !item.IsOnScreen || !item.IsSelected) continue;
                    if (item.FloorCoordinate == null || item.FloorCoordinate.XYDistanceTo(anchor.FloorCoordinate) > StackedLootWorldRadiusYards + 0.8f) continue;
                    if (IsExcludedPickup(item) || WantedPriority(item) < 0) continue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool HasVisibleEligibleLootBlockingUrshiTalk()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Items == null) return false;

                long now = Hud.Game.CurrentRealTimeMilliseconds;
                IActor protectedChest = GetUnopenedProtectedChest();
                int freeSlots = SafeFreeSlots();

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen) continue;
                    if (IsExcludedPickup(item) || IsSuppressedDroppedItem(item, now) || IsCleanupStuckIgnored(item, now)) continue;
                    if (IsProtectedChestRisk(item, protectedChest)) continue;
                    if (WantedPriority(item) < 0 || !CanFit(item, freeSlots)) continue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool HandleUrshiRiskLootHoverClick(IItem item, int tries, long now)
        {
            IActor urshi = GetUrshiActor();
            if (urshi == null)
                return false;

            bool fallbackMode = item.Seed == _urshiFallbackSeed && now <= _urshiFallbackUntilMs;
            int fallbackTries = 0;
            if (fallbackMode)
                _urshiFallbackTriesBySeed.TryGetValue(item.Seed, out fallbackTries);

            int probe = fallbackMode ? fallbackTries : tries;

            if (_urshiRiskHoverSeed == item.Seed && _urshiRiskHoverClickAtMs != 0)
            {
                if (now < _urshiRiskHoverClickAtMs)
                    return true;

                int hx = _urshiRiskHoverX;
                int hy = _urshiRiskHoverY;
                ClearUrshiRiskLootHover();

                bool recoveryUiVisible = IsUrshiRecoveryUiVisible();
                IActor selectedActor = GetSelectedActorSafe();
                bool urshiSelected = IsUrshiSelected(urshi, selectedActor);
                bool lootSelected = IsAnyEligibleLootSelectedNear(item);
                bool itemActorSelected = IsSelectedActorItem(selectedActor);
                bool noActorSelected = selectedActor == null;

                if (!recoveryUiVisible
                    && !urshiSelected
                    && (lootSelected || itemActorSelected || noActorSelected)
                    && IsInsideGameWindow(hx, hy)
                    && SetCursorPos(hx, hy))
                {
                    _attempts[item.Seed] = tries + 1;

                    if (fallbackMode)
                        _urshiFallbackTriesBySeed[item.Seed] = fallbackTries + 1;

                    _nextUrshiRiskClickMs = now + UrshiRiskClickDelayMs;
                    _urshiArmedUntilMs = now + UrshiPanelRecoveryWindowMs;
                    _urshiArmedSeed = item.Seed;
                    _nextUrshiSpaceMs = 0;
                    _urshiSpaceAttempts = 0;

                    MouseLeftClick();

                    _lastClickSeed = item.Seed;
                    _lastCleanupClickFar = item.CentralXyDistanceToMe > IdleRange;
                    _lastClickMs = now;
                    return true;
                }

                _attempts[item.Seed] = tries + 1;

                if (fallbackMode)
                    _urshiFallbackTriesBySeed[item.Seed] = fallbackTries + 1;

                if (recoveryUiVisible)
                {
                    _nextUrshiRiskClickMs = now + UrshiPanelConfirmDelayMs;
                    _lastClickMs = now;
                }
                else
                {
                    _nextUrshiRiskClickMs = now + UrshiRiskHoverRetryMs;

                    if (urshiSelected || (!lootSelected && !itemActorSelected && !noActorSelected))
                    {
                        int failedProbe = fallbackMode ? fallbackTries : tries;
                        if (failedProbe >= 3)
                            _retryAfterMs[item.Seed] = now + UrshiRiskRotateItemMs;
                    }
                }

                return true;
            }

            int x, y;
            if (!TryGetUrshiSafeFallbackClickPoint(item, urshi, probe, out x, out y))
            {
                _retryAfterMs[item.Seed] = now + UrshiRiskRetryCooldownMs;
                _nextUrshiRiskClickMs = now + UrshiRiskRetryCooldownMs;
                return true;
            }

            if (!SetCursorPos(x, y))
                return true;

            _urshiRiskHoverSeed = item.Seed;
            _urshiRiskHoverX = x;
            _urshiRiskHoverY = y;
            _urshiRiskHoverClickAtMs = now + UrshiRiskHoverSettleMs;
            _nextUrshiRiskClickMs = now + UrshiRiskHoverRetryMs;
            return true;
        }

        private void ClearUrshiRiskLootHover()
        {
            _urshiRiskHoverSeed = 0;
            _urshiRiskHoverClickAtMs = 0;
            _urshiRiskHoverX = 0;
            _urshiRiskHoverY = 0;
        }

        private void ScheduleCursorRestore(NativePoint point, long now)
        {
            _pendingCursorPoint = point;
            _pendingCursorRestore = true;
            _pendingCursorRestoreAtMs = now + CursorRestoreDelayMs;
            _pendingCursorRestoreExpireMs = now + CursorRestoreExpireMs;
        }

        private void ProcessPendingCursorRestore(long now)
        {
            if (!_pendingCursorRestore) return;
            if (now < _pendingCursorRestoreAtMs) return;

            _pendingCursorRestore = false;
            if (now > _pendingCursorRestoreExpireMs || Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return;

            SetCursorPos(_pendingCursorPoint.X, _pendingCursorPoint.Y);
        }

        private void GetStackedLootClickPoint(IItem item, int phase, bool cleanup, out int x, out int y)
        {
            int baseX, baseY;
            GetItemClickBase(item, IsNoSpaceMaterialPickup(item), out baseX, out baseY);
            x = baseX;
            y = baseY;

            switch (phase % 12)
            {
                case 1: y = baseY + 18; return;
                case 2: y = baseY - 18; return;
                case 3: y = baseY + 34; return;
                case 4: y = baseY - 34; return;
                case 5: x = baseX - 18; return;
                case 6: x = baseX + 18; return;
                case 7: x = baseX - 18; y = baseY + 18; return;
                case 8: x = baseX + 18; y = baseY + 18; return;
                case 9: x = baseX - 18; y = baseY - 18; return;
                case 10: x = baseX + 18; y = baseY - 18; return;
                case 11:
                    if (cleanup && TryGetFloorClickPoint(item, out x, out y)) return;
                    break;
            }
        }

        private void GetClickPoint(IItem item, int attempt, bool allowAlternate, out int x, out int y)
        {
            int baseX, baseY;
            GetItemClickBase(item, IsNoSpaceMaterialPickup(item), out baseX, out baseY);
            x = baseX;
            y = baseY;
            if (!allowAlternate) return;

            switch (attempt % 8)
            {
                case 1: y = baseY + 10; return;
                case 2: y = baseY - 10; return;
                case 3: x = baseX - 16; return;
                case 4: x = baseX + 16; return;
                case 5:
                    if (TryGetFloorClickPoint(item, out x, out y)) return;
                    break;
                case 6: x = baseX - 24; y = baseY + 10; return;
                case 7: x = baseX + 24; y = baseY + 10; return;
            }
        }

        private void GetItemClickBase(IItem item, bool noSpaceMaterial, out int x, out int y)
        {
            x = (int)Math.Round((double)item.ScreenCoordinate.X + (double)Hud.Window.Offset.X);
            float lift = noSpaceMaterial ? 0f : Hud.Window.Size.Height / 55f;
            y = (int)Math.Round(item.ScreenCoordinate.Y - lift + Hud.Window.Offset.Y);
        }

        private bool TryGetFloorClickPoint(IItem item, out int x, out int y)
        {
            x = 0;
            y = 0;
            try
            {
                if (item == null || item.FloorCoordinate == null) return false;
                var screen = item.FloorCoordinate.ToScreenCoordinate(false, true);
                if (screen == null) return false;
                x = (int)Math.Round(screen.X + Hud.Window.Offset.X);
                y = (int)Math.Round(screen.Y + Hud.Window.Offset.Y);
                return true;
            }
            catch { return false; }
        }

        private int WantedPriority(IItem item)
        {
            if (IsExcludedPickup(item)) return -1;
            ActorSnoEnum actor = item.SnoActor != null ? item.SnoActor.Sno : 0;
            uint itemSno = item.SnoItem != null ? item.SnoItem.Sno : 0;

            if (actor == ActorSnoEnum._horadricrelic) return IsBloodShardCapped() ? -1 : 0;
            if (IsGreaterRiftKey(actor)) return 1;
            if (PlanActors.Contains(actor) || IsWhisper(item)) return 2;
            if (actor == ActorSnoEnum._crafting_looted_reagent_05) return _deathsBreath ? 40 : -1;
            if (_materials && NoSpaceActors.Contains(actor)) return 3;
            if (_gifts && (itemSno == RamaladniGiftSno || actor == ActorSnoEnum._consumable_add_sockets || actor == ActorSnoEnum._consumable_add_sockets_flippy)) return 4;
            if (_screams && (itemSno == PetrifiedScreamSno || actor == ActorSnoEnum._swarmriftkey)) return 4;
            if (_gems && IsGem(item)) return 5;
            if (IsLegendaryLike(item))
            {
                if (item.AncientRank >= 2) return _primals ? 10 : -1;
                if (item.AncientRank == 1) return _ancients ? 11 : -1;
                return _legendaries ? 12 : -1;
            }
            if (_trash && (item.IsRare || item.IsMagic || item.IsNormal)) return 100;
            return -1;
        }

        private bool CanFit(IItem item, int freeSlots)
        {
            if (IsNoSpacePickup(item)) return true;
            if (item.AccountBound && !item.BoundToMyAccount) return false;
            if (freeSlots <= 0) return HasMatchingStack(item);
            if (freeSlots > 1 || item.SnoItem == null) return true;
            string group = item.SnoItem.MainGroupCode ?? string.Empty;
            ItemKind kind = item.SnoItem.Kind;
            return kind == ItemKind.uberstuff || kind == ItemKind.craft || kind == ItemKind.gem || group == "gems_unique" || group == "ring" || group == "amulet" || group == "belt" || group == "consumable";
        }

        private bool HasMatchingStack(IItem item)
        {
            if (item == null || item.SnoActor == null) return false;
            return Hud.Inventory.ItemsInInventory.Any(i => i != null && i.SnoActor != null && i.SnoActor.Sno == item.SnoActor.Sno && IsStackable(i));
        }

        private static bool IsStackable(IItem item)
        {
            if (item == null || item.SnoItem == null) return false;
            if (item.SnoItem.StackSize > 1) return true;
            return item.StatList != null && item.StatList.Any(q => q != null && q.Id == "ItemStackQuantityLo#1048575");
        }

        private bool IsNoSpacePickup(IItem item)
        {
            if (item == null || item.SnoActor == null) return false;
            ActorSnoEnum actor = item.SnoActor.Sno;
            return actor == ActorSnoEnum._horadricrelic || IsGreaterRiftKey(actor) || NoSpaceActors.Contains(actor);
        }

        private bool IsNoSpaceMaterialPickup(IItem item)
        {
            if (item == null || item.SnoActor == null) return false;
            return NoSpaceActors.Contains(item.SnoActor.Sno);
        }

        private static bool IsExcludedPickup(IItem item)
        {
            if (item == null || item.SnoActor == null) return true;
            ActorKind kind = item.SnoActor.Kind;
            return kind == ActorKind.HealthGlobe || kind == ActorKind.PowerGlobe || kind == ActorKind.RiftOrb || kind == ActorKind.Gold;
        }

        private static bool IsGreaterRiftKey(ActorSnoEnum actor)
        {
            int sno = (int)actor;
            return actor == ActorSnoEnum._lootrunkey || actor == ActorSnoEnum._tieredlootrunkey_0 || (sno >= 408130 && sno <= 408230);
        }

        private static bool IsGem(IItem item)
        {
            if (item == null || item.SnoItem == null) return false;
            return item.SnoItem.Kind == ItemKind.gem || string.Equals(item.SnoItem.MainGroupCode, "gems_unique", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegendaryLike(IItem item)
        {
            if (item == null) return false;
            if (item.IsLegendary || item.Quality == ItemQuality.Legendary) return true;
            if (item.SetSno != 0 && item.SetSno != uint.MaxValue) return true;
            return item.SnoItem != null && (item.SnoItem.LegendaryPower != null || (item.SnoItem.SetItemBonusesSno != 0 && item.SnoItem.SetItemBonusesSno != uint.MaxValue));
        }

        private static bool IsWhisper(IItem item)
        {
            if (item == null || item.SnoItem == null) return false;
            uint sno = item.SnoItem.Sno;
            if (sno >= WhisperLowSno && sno <= WhisperHighSno) return true;
            string name = item.SnoItem.NameEnglish ?? item.FullNameEnglish;
            return !string.IsNullOrEmpty(name) && name.IndexOf("Whisper of Atonement", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBloodShardCapped()
        {
            try { return Hud.Game.Me.Materials.BloodShard >= 500 + Hud.Game.Me.HighestSoloRiftLevel * 10; }
            catch { return false; }
        }

        private int SafeFreeSlots()
        {
            try { return Hud.Game.Me.InventorySpaceTotal - Hud.Game.InventorySpaceUsed; }
            catch { return 2; }
        }

        private bool UpdatePlayerMovement(long now)
        {
            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null || me.FloorCoordinate == null) return false;

                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;
                if (_lastMovementSampleMs == 0)
                {
                    _lastPlayerX = x;
                    _lastPlayerY = y;
                    _lastMovementSampleMs = now;
                    _playerMoving = me.AnimationState == AcdAnimationState.Running;
                    return _playerMoving;
                }

                if (now - _lastMovementSampleMs < MovementSampleMs)
                    return _playerMoving || me.AnimationState == AcdAnimationState.Running;

                float dx = x - _lastPlayerX;
                float dy = y - _lastPlayerY;
                _playerMoving = (dx * dx + dy * dy) >= MovementThresholdYards * MovementThresholdYards || me.AnimationState == AcdAnimationState.Running;
                _lastPlayerX = x;
                _lastPlayerY = y;
                _lastMovementSampleMs = now;
                return _playerMoving;
            }
            catch { return false; }
        }

        private bool IsLootBurstCleanup(long now, bool allowTown)
        {
            if (HasUnopenedProtectedChestNearby(ProtectedChestBlockYards) || HasActiveVisionFight())
            {
                _lootBurstCleanupUntilMs = 0;
                return false;
            }

            if (!allowTown && HasNearbyAttackableMonster(LootBurstMonsterBlockYards))
            {
                _lootBurstCleanupUntilMs = 0;
                return false;
            }
            if (_lootBurstCleanupUntilMs > now)
                return true;

            try
            {
                int freeSlots = SafeFreeSlots();
                int count = 0;
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen || item.CentralXyDistanceToMe > SpecialCleanupRange) continue;
                    if (IsExcludedPickup(item) || IsSuppressedDroppedItem(item, now) || WantedPriority(item) < 0 || !CanFit(item, freeSlots)) continue;
                    if (++count >= LootBurstThreshold)
                    {
                        _lootBurstCleanupUntilMs = now + LootBurstLatchMs;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsAutoUrshiTalkActorClickable(IActor urshi)
        {
            try
            {
                return _talkToUrshiAfterLoot
                    && urshi != null
                    && urshi.IsOnScreen
                    && urshi.ScreenCoordinate != null;
            }
            catch { return false; }
        }

        private bool TryReturnTowardAutoUrshi(long now)
        {
            if (!_talkToUrshiAfterLoot || !_autoUrshiHasLastSeenWorld ||
                _autoUrshiActorPathActive || _autoUrshiApproachAborted)
                return false;

            if (_autoUrshiReturnTrail.Count == 0)
                return false;

            _autoUrshiReturning = true;

            if (_autoUrshiReturnClicks >= AutoUrshiReturnMaxClicks)
            {
                AbortAutoUrshiApproach(now);
                return false;
            }

            if (now < _nextAutoUrshiReturnMs)
                return true;

            int x, y;
            if (!TryGetAutoUrshiReturnPoint(out x, out y))
                return false;

            if (!SetCursorPos(x, y))
                return false;

            MouseLeftClick();
            BeginAutoUrshiApproach(now, false);

            _autoUrshiReturnClicks++;
            _nextAutoUrshiReturnMs = now + AutoUrshiReturnClickDelayMs;
            _lastClickMs = now;
            _lastCleanupClickFar = true;
            return true;
        }

        private void BeginAutoUrshiApproach(long now, bool actorPath)
        {
            if (actorPath && !_autoUrshiActorPathActive)
            {
                _autoUrshiActorPathActive = true;
                _autoUrshiReturning = false;
                _autoUrshiReturnTrail.Clear();
                _autoUrshiReturnClicks = 0;
                _nextAutoUrshiReturnMs = 0;
                ResetAutoUrshiApproachSample();
            }

            if (_autoUrshiApproachSampleMs != 0)
                return;

            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me != null && me.FloorCoordinate != null)
                    SeedAutoUrshiApproachSample(now, me.FloorCoordinate.X, me.FloorCoordinate.Y);
            }
            catch { }
        }

        private bool UpdateAutoUrshiApproachProgress(long now)
        {
            if (_autoUrshiApproachSampleMs == 0)
                return true;

            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null || me.FloorCoordinate == null)
                    return true;

                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;
                float dx = x - _autoUrshiApproachSampleX;
                float dy = y - _autoUrshiApproachSampleY;

                if (dx * dx + dy * dy >=
                    AutoUrshiApproachProgressYards * AutoUrshiApproachProgressYards)
                {
                    SeedAutoUrshiApproachSample(now, x, y);
                    return true;
                }

                if (now - _autoUrshiApproachSampleMs < AutoUrshiApproachStallMs)
                    return true;

                AbortAutoUrshiApproach(now);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private void SeedAutoUrshiApproachSample(long now, float x, float y)
        {
            _autoUrshiApproachSampleMs = now;
            _autoUrshiApproachSampleX = x;
            _autoUrshiApproachSampleY = y;
        }

        private void ResetAutoUrshiApproachSample()
        {
            _autoUrshiApproachSampleMs = 0;
            _autoUrshiApproachSampleX = 0f;
            _autoUrshiApproachSampleY = 0f;
        }

        private void AbortAutoUrshiApproach(long now)
        {
            _autoUrshiApproachAborted = true;
            _autoUrshiActorPathActive = false;
            _autoUrshiReturning = false;
            _autoUrshiReturnTrail.Clear();
            _autoUrshiReturnClicks = 0;
            _nextAutoUrshiReturnMs = 0;
            ResetAutoUrshiApproachSample();
            ClearAutoUrshiTalkHover();
            RestoreAutoUrshiTalkCursor(now);
        }

        private bool TryGetAutoUrshiReturnPoint(out int x, out int y)
        {
            x = 0;
            y = 0;

            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null || me.FloorCoordinate == null || Hud.Window == null)
                    return false;

                while (_autoUrshiReturnTrail.Count > 0)
                {
                    int lastIndex = _autoUrshiReturnTrail.Count - 1;
                    var last = _autoUrshiReturnTrail[lastIndex];

                    if (me.FloorCoordinate.XYDistanceTo(last.X, last.Y) >= AutoUrshiReturnMinClickYards)
                        break;

                    _autoUrshiReturnTrail.RemoveAt(lastIndex);
                }

                for (int i = _autoUrshiReturnTrail.Count - 1; i >= 0; i--)
                {
                    var point = _autoUrshiReturnTrail[i];
                    var world = Hud.Window.CreateWorldCoordinate(point.X, point.Y, point.Z);

                    if (world == null || !world.IsOnScreen(0.8d))
                        continue;

                    var screen = world.ToScreenCoordinate(false, true);
                    if (screen == null)
                        continue;

                    x = (int)Math.Round((double)screen.X + (double)Hud.Window.Offset.X);
                    y = (int)Math.Round((double)screen.Y + (double)Hud.Window.Offset.Y);

                    if (IsInsideGameWindow(x, y))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private void ResetAutoUrshiTalkReadyState()
        {
            _autoUrshiNoLootSinceMs = 0;
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            ClearAutoUrshiTalkHover();
        }

        private void ClearAutoUrshiTalkHover()
        {
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
        }

        private void RestoreAutoUrshiTalkCursor(long now)
        {
            if (!_autoUrshiHasRestorePoint) return;
            ScheduleCursorRestore(_autoUrshiRestorePoint, now);
            _autoUrshiHasRestorePoint = false;
            _autoUrshiRestorePoint = new NativePoint();
        }

        private void AbortAutoUrshiTalkForVisibleLoot(long now)
        {
            RestoreAutoUrshiTalkCursor(now);
            _autoUrshiActorPathActive = false;
            _autoUrshiReturning = false;
            ResetAutoUrshiApproachSample();
            _autoUrshiNoLootSinceMs = 0;
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
        }

        private void MarkUrshiPanelCloseForFastLootResume(long now)
        {
            _lastCleanupClickFar = false;

            long resumeMs = now - CleanupDelayMs;
            _lastClickMs = resumeMs > 0 ? resumeMs : 0;
        }


        private bool TryTalkToUrshiAfterLoot(long now, IActor urshi)
        {
            if (_autoUrshiTalkDone || !_talkToUrshiAfterLoot || _autoUrshiApproachAborted ||
                urshi == null || !urshi.IsOnScreen || urshi.ScreenCoordinate == null)
            {
                ResetAutoUrshiTalkReadyState();
                return false;
            }

            if (HasVisibleEligibleLootBlockingUrshiTalk())
            {
                AbortAutoUrshiTalkForVisibleLoot(now);
                return false;
            }

            if (IsUrshiRecoveryUiVisible())
            {
                if (_autoUrshiTalkAttempts > 0)
                    _autoUrshiTalkDone = true;
                RestoreAutoUrshiTalkCursor(now);
                return false;
            }

            if (!_autoUrshiSawEligibleLootThisCleanup)
            {
                if (_autoUrshiLootDropGateStartedMs == 0)
                    _autoUrshiLootDropGateStartedMs = now;
                if (now - _autoUrshiLootDropGateStartedMs < AutoUrshiTalkWaitForLootDropMs)
                {
                    ResetAutoUrshiTalkReadyState();
                    return false;
                }
            }

            if (now < _autoUrshiTalkCooldownUntilMs || now < _nextAutoUrshiTalkMs)
                return false;

            if (_autoUrshiNoLootSinceMs == 0)
                _autoUrshiNoLootSinceMs = now;
            if (AutoUrshiTalkNoLootSettleMs > 0 && now - _autoUrshiNoLootSinceMs < AutoUrshiTalkNoLootSettleMs)
                return false;

            if (_autoUrshiHoverClickAtMs != 0)
            {
                if (now < _autoUrshiHoverClickAtMs)
                    return true;

                if (urshi.IsSelected && IsInsideGameWindow(_autoUrshiHoverX, _autoUrshiHoverY) && SetCursorPos(_autoUrshiHoverX, _autoUrshiHoverY))
                {
                    MouseLeftClick();
                    BeginAutoUrshiApproach(now, true);

                    _autoUrshiRecentTalkOpenedUntilMs = now + AutoUrshiRecentTalkLootCancelWindowMs;
                    _autoUrshiTalkLootCancelAttempts = 0;
                    _nextAutoUrshiTalkLootCancelMs = 0;

                    RestoreAutoUrshiTalkCursor(now);
                    ClearAutoUrshiTalkHover();
                    _nextAutoUrshiTalkMs = now + AutoUrshiTalkClickDelayMs;
                    _lastClickMs = now;
                    return true;
                }

                ClearAutoUrshiTalkHover();
                _nextAutoUrshiTalkMs = now + AutoUrshiTalkProbeRetryMs;
            }

            if (_autoUrshiTalkAttempts >= AutoUrshiTalkMaxAttempts)
            {
                _autoUrshiTalkCooldownUntilMs = now + AutoUrshiTalkRetryCooldownMs;
                RestoreAutoUrshiTalkCursor(now);
                return false;
            }

            return BeginAutoUrshiTalkHoverProbe(now, urshi);
        }

        private bool BeginAutoUrshiTalkHoverProbe(long now, IActor urshi)
        {
            int x, y;
            if (!TryGetAutoUrshiTalkPoint(urshi, _autoUrshiTalkAttempts, out x, out y))
                return false;

            if (!_autoUrshiHasRestorePoint)
            {
                NativePoint old;
                if (GetCursorPos(out old))
                {
                    _autoUrshiRestorePoint = old;
                    _autoUrshiHasRestorePoint = true;
                }
            }

            if (!SetCursorPos(x, y))
                return false;

            _autoUrshiHoverX = x;
            _autoUrshiHoverY = y;
            _autoUrshiHoverClickAtMs = now + AutoUrshiTalkHoverSettleMs;
            _autoUrshiTalkAttempts++;
            return true;
        }

        private bool TryGetAutoUrshiTalkPoint(IActor urshi, int attempt, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (urshi == null || urshi.ScreenCoordinate == null)
                return false;

            float scale = UiScale();
            float ox = 0f;
            float oy = 0f;

            switch (attempt % 12)
            {
                case 0: ox = 0f; oy = 0f; break;
                case 1: ox = 0f; oy = 14f; break;
                case 2: ox = -14f; oy = 8f; break;
                case 3: ox = 14f; oy = 8f; break;
                case 4: ox = 0f; oy = 28f; break;
                case 5: ox = -22f; oy = 18f; break;
                case 6: ox = 22f; oy = 18f; break;
                case 7: ox = -28f; oy = 0f; break;
                case 8: ox = 28f; oy = 0f; break;
                case 9: ox = 0f; oy = -8f; break;
                case 10: ox = -14f; oy = -8f; break;
                default: ox = 14f; oy = -8f; break;
            }

            x = (int)Math.Round((double)urshi.ScreenCoordinate.X + (double)Hud.Window.Offset.X + (double)(ox * scale));
            y = (int)Math.Round((double)urshi.ScreenCoordinate.Y + (double)Hud.Window.Offset.Y + (double)(oy * scale));
            return IsInsideGameWindow(x, y);
        }

        private bool IsInsideGameWindow(int x, int y)
        {
            try
            {
                int left = (int)Math.Round((double)Hud.Window.Offset.X);
                int top = (int)Math.Round((double)Hud.Window.Offset.Y);
                int right = left + (int)Math.Round((double)Hud.Window.Size.Width);
                int bottom = top + (int)Math.Round((double)Hud.Window.Size.Height);
                return x >= left && x <= right && y >= top && y <= bottom;
            }
            catch { return false; }
        }

        private bool IsPostRiftCleanup()
        {
            try
            {
                if (HasNearbyAttackableMonster(CleanupMonsterBlockYards)) return false;
                if (_cleanupLatched) return true;
                if (Hud.Game.SpecialArea != SpecialArea.GreaterRift && Hud.Game.SpecialArea != SpecialArea.Rift) return false;
                if (Hud.Game.RiftPercentage < 100.0d && GetUrshiActor() == null) return false;
                _cleanupLatched = true;
                return true;
            }
            catch { return false; }
        }

        private bool HasNearbyAttackableMonster(int yards)
        {
            try
            {
                return Hud.Game.AliveMonsters.Any(m => m != null && m.IsAlive && m.Attackable && !m.Illusion && m.CentralXyDistanceToMe <= yards);
            }
            catch { return true; }
        }

        private IActor GetUnopenedProtectedChest()
        {
            try
            {
                return Hud.Game.Actors.FirstOrDefault(a => IsUnopenedProtectedChest(a) && a.CentralXyDistanceToMe <= ProtectedChestBlockYards);
            }
            catch { return null; }
        }

        private bool HasUnopenedProtectedChestNearby(int yards)
        {
            try
            {
                return Hud.Game.Actors.Any(a => IsUnopenedProtectedChest(a) && a.CentralXyDistanceToMe <= yards);
            }
            catch { return true; }
        }

        private static bool IsUnopenedProtectedChest(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return false;
            ActorSnoEnum sno = actor.SnoActor.Sno;
            if (sno != ActorSnoEnum._p76_chest && sno != ActorSnoEnum._p73_chestreward) return false;
            if (actor.IsDisabled || actor.IsOperated) return false;
            return actor.IsClickable || actor.DisplayOnOverlay;
        }

        private static bool IsProtectedChestRisk(IItem item, IActor chest)
        {
            try { return item != null && chest != null && item.FloorCoordinate.XYDistanceTo(chest.FloorCoordinate) <= ProtectedChestRiskYards; }
            catch { return true; }
        }

        private bool HasActiveVisionFight()
        {
            if (!IsVisionWorld()) return false;
            try { if (Hud.Game.IsGoblinOnScreen) return true; } catch { }
            try
            {
                return Hud.Game.AliveMonsters.Any(m => m != null && m.IsAlive && !m.Illusion && m.CentralXyDistanceToMe <= VisionFightBlockYards && (m.Attackable || IsTreasureGoblin(m)));
            }
            catch { return true; }
        }

        private bool IsVisionWorld()
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

        private static bool IsTreasureGoblin(IActor actor)
        {
            if (actor == null || actor.SnoActor == null) return false;
            int sno = (int)actor.SnoActor.Sno;
            return (sno >= 5984 && sno <= 5987)
                || sno == 391593 || sno == 408354 || sno == 408655 || sno == 408989 || sno == 413289 || sno == 428663 || sno == 429161 || sno == 450993
                || (sno >= 487312 && sno <= 487318)
                || sno == 488564 || sno == 488932 || (sno >= 488935 && sno <= 488939);
        }

        private bool IsUiVisible(IUiElement element)
        {
            try
            {
                if (element == null) return false;
                element.Refresh();
                return element.Visible;
            }
            catch { return false; }
        }

        private bool IsUrshiGemPaneVisible()
        {
            return IsUiVisible(_urshiGemPane);
        }

        private bool IsUrshiConversationVisible()
        {
            return IsUiVisible(_urshiConversationMain);
        }

        private bool IsUrshiRecoveryUiVisible()
        {
            return IsUrshiGemPaneVisible() || IsUrshiConversationVisible();
        }

        private bool IsChatEntryOpen()
        {
            return IsUiVisible(_chatEditLine);
        }

        private bool IsStashUiOpen()
        {
            try
            {
                return Hud != null
                    && Hud.Inventory != null
                    && Hud.Inventory.StashMainUiElement != null
                    && Hud.Inventory.StashMainUiElement.Visible;
            }
            catch { return false; }
        }

        private bool CanSendUrshiRecoverySpace()
        {
            if (IsChatEntryOpen()) return false;
            if (IsStashUiOpen()) return false;
            return true;
        }


        private IItem FindVisibleFloorItemBySeed(int seed)
        {
            if (seed == 0)
                return null;

            try
            {
                return Hud.Game.Items.FirstOrDefault(i =>
                    i != null
                    && i.Seed == seed
                    && i.Location == ItemLocation.Floor
                    && i.IsOnScreen);
            }
            catch { return null; }
        }

        private void ClearUrshiArmedRecoveryState(bool clearFallback)
        {
            _urshiArmedSeed = 0;
            _urshiArmedUntilMs = 0;
            _nextUrshiSpaceMs = 0;
            _urshiSpaceAttempts = 0;
            ClearUrshiRiskLootHover();
            _urshiRecoveryOpenSeed = 0;
            _urshiRecoveryOpenSeenMs = 0;
            _urshiRecoveryMisclickRecorded = false;
            _autoUrshiNoLootSinceMs = 0;
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiHasRestorePoint = false;
            _autoUrshiRestorePoint = new NativePoint();
            if (clearFallback)
            {
                _urshiFallbackSeed = 0;
                _urshiFallbackUntilMs = 0;
            }
        }

        private bool HasPendingArmedUrshiLoot(long now)
        {
            if (_urshiArmedSeed == 0 || now > _urshiArmedUntilMs)
                return false;

            IItem item = FindVisibleFloorItemBySeed(_urshiArmedSeed);
            if (item == null)
                return false;

            IActor urshi = GetUrshiActor();
            if (urshi == null)
                return false;

            if (!IsUrshiRisk(item, urshi))
                return false;

            if (WantedPriority(item) < 0)
                return false;

            return true;
        }

        private bool HasRecentIntentionalUrshiTalk(long now)
        {
            return _autoUrshiRecentTalkOpenedUntilMs != 0 && now <= _autoUrshiRecentTalkOpenedUntilMs;
        }

        private bool HandleAutoUrshiTalkInterruptedByNewLoot(long now)
        {
            if (!HasRecentIntentionalUrshiTalk(now))
                return false;

            if (!HasVisibleEligibleLootBlockingUrshiTalk())
                return false;

            if (now < _nextAutoUrshiTalkLootCancelMs)
                return true;

            if (!CanSendUrshiRecoverySpace())
                return true;

            if (_autoUrshiTalkLootCancelAttempts >= AutoUrshiTalkLootCancelMaxAttempts)
                return true;

            SendSpace();

            _autoUrshiTalkLootCancelAttempts++;
            _nextAutoUrshiTalkLootCancelMs = now + AutoUrshiTalkLootCancelRetryMs;
            MarkUrshiPanelCloseForFastLootResume(now);

            // This was not a failed pickup. Do not increment Urshi misclick counters.
            // Reset only talk state so visible loot can be picked up after the panel closes.
            _autoUrshiNoLootSinceMs = 0;
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkAttempts = 0;
            ClearAutoUrshiTalkHover();
            _autoUrshiTalkDone = false;

            return true;
        }

        private bool HasPendingGenericUrshiPickupRecovery(long now)
        {
            if (_genericUrshiRecoverySeed == 0 || now > _genericUrshiRecoveryUntilMs)
                return false;

            if (!HasVisibleEligibleLootBlockingUrshiTalk())
                return false;

            IItem item = FindVisibleFloorItemBySeed(_genericUrshiRecoverySeed);
            if (item == null)
                return false;

            if (WantedPriority(item) < 0)
                return false;

            return true;
        }

        private bool ShouldRecoverAutoLootUrshiMisclick(long now)
        {
            return _urshiArmedSeed != 0 && now <= _urshiArmedUntilMs;
        }

        private bool HandleAutoLootUrshiRecovery(long now)
        {
            bool recoveryUiVisible = IsUrshiRecoveryUiVisible();
            if (recoveryUiVisible)
            {
                _autoUrshiActorPathActive = false;
                _autoUrshiReturning = false;
                ResetAutoUrshiApproachSample();
            }

            if (!recoveryUiVisible)
            {
                bool recoveryAttempted = _urshiSpaceAttempts > 0 || _nextUrshiSpaceMs != 0 || _genericUrshiRecoveryAttempts > 0;

                if (recoveryAttempted || (_urshiArmedSeed != 0 && now > _urshiArmedUntilMs))
                    ClearUrshiArmedRecoveryState(false);

                if (_genericUrshiRecoveryAttempts > 0 || (_genericUrshiRecoverySeed != 0 && now > _genericUrshiRecoveryUntilMs))
                    ClearGenericUrshiRecoveryState();

                _urshiRecoveryOpenSeed = 0;
                _urshiRecoveryOpenSeenMs = 0;
                _urshiRecoveryMisclickRecorded = false;
                return false;
            }

            if (HandleAutoUrshiTalkInterruptedByNewLoot(now))
                return true;

            // Urshi UI is visible. Do not click floor loot behind it.
            // Prefer the specific risky-loot recovery path when armed. If a normal
            // AutoLoot pickup click opened Urshi and loot still remains, close it too.
            if (!HasPendingArmedUrshiLoot(now))
            {
                if (_urshiArmedSeed != 0 && (now > _urshiArmedUntilMs || FindVisibleFloorItemBySeed(_urshiArmedSeed) == null))
                    ClearUrshiArmedRecoveryState(true);

                if (HasPendingGenericUrshiPickupRecovery(now))
                    return HandleGenericUrshiPickupRecovery(now);

                ClearGenericUrshiRecoveryState();
                return true;
            }

            if (_urshiRecoveryOpenSeed != _urshiArmedSeed)
            {
                _urshiRecoveryOpenSeed = _urshiArmedSeed;
                _urshiRecoveryOpenSeenMs = now;
                _urshiRecoveryMisclickRecorded = false;
                _nextUrshiSpaceMs = now + UrshiPanelConfirmDelayMs;
                return true;
            }

            if (!HasPendingArmedUrshiLoot(now))
            {
                ClearUrshiArmedRecoveryState(true);
                return true;
            }

            if (now < _nextUrshiSpaceMs)
                return true;

            if (!_urshiRecoveryMisclickRecorded)
            {
                HandleArmedUrshiMisclick(now);
                _urshiRecoveryMisclickRecorded = true;
            }

            if (!CanSendUrshiRecoverySpace())
                return true;

            if (_urshiSpaceAttempts >= UrshiSpaceMaxAttempts)
                return true;

            if (_nextUrshiSpaceMs != 0 && now < _nextUrshiSpaceMs)
                return true;

            SendSpace();

            _urshiSpaceAttempts++;
            _nextUrshiSpaceMs = now + UrshiSpaceRetryMs;
            MarkUrshiPanelCloseForFastLootResume(now);
            return true;
        }

        private bool HandleGenericUrshiPickupRecovery(long now)
        {
            if (now < _nextUrshiSpaceMs)
                return true;

            if (!_genericUrshiRecoveryMisclickRecorded)
            {
                HandleGenericUrshiPickupMisclick(now);
                _genericUrshiRecoveryMisclickRecorded = true;
            }

            if (!CanSendUrshiRecoverySpace())
                return true;

            if (_genericUrshiRecoveryAttempts >= UrshiSpaceMaxAttempts)
                return true;

            SendSpace();

            _genericUrshiRecoveryAttempts++;
            _nextUrshiSpaceMs = now + UrshiSpaceRetryMs;
            MarkUrshiPanelCloseForFastLootResume(now);
            return true;
        }

        private void HandleGenericUrshiPickupMisclick(long now)
        {
            if (_genericUrshiRecoverySeed == 0)
                return;

            int seed = _genericUrshiRecoverySeed;

            int closes;
            _urshiMisclicksBySeed.TryGetValue(seed, out closes);
            closes++;
            _urshiMisclicksBySeed[seed] = closes;

            _attempts[seed] = 0;

            if (_lastClickSeed == seed)
                _lastClickSeed = 0;

            if (closes >= 3)
            {
                _retryAfterMs[seed] = now + UrshiProblemItemSuppressMs;
                _cleanupStuckIgnoreUntilMs[seed] = now + CleanupStuckIgnoreMs;
                _nextUrshiRiskClickMs = now + UrshiFallbackRetryDelayMs;
            }
            else
            {
                _retryAfterMs[seed] = now + UrshiFallbackRetryDelayMs;
                _nextUrshiRiskClickMs = now + UrshiFallbackRetryDelayMs;
            }
        }

        private void HandleArmedUrshiMisclick(long now)
        {
            if (_urshiArmedSeed == 0)
                return;

            int seed = _urshiArmedSeed;
            int closes;
            _urshiMisclicksBySeed.TryGetValue(seed, out closes);
            closes++;
            _urshiMisclicksBySeed[seed] = closes;

            _attempts[seed] = 0;

            if (_lastClickSeed == seed)
                _lastClickSeed = 0;

            if (closes >= 3)
            {
                _retryAfterMs[seed] = now + UrshiProblemItemSuppressMs;
                _cleanupStuckIgnoreUntilMs[seed] = now + CleanupStuckIgnoreMs;
                _nextUrshiRiskClickMs = now + UrshiFallbackRetryDelayMs;
                _urshiFallbackSeed = 0;
                _urshiFallbackUntilMs = 0;
            }
            else
            {
                _urshiFallbackSeed = seed;
                _urshiFallbackUntilMs = now + UrshiFallbackWindowMs;
                _retryAfterMs[seed] = now + UrshiFallbackRetryDelayMs;
                _nextUrshiRiskClickMs = now + UrshiFallbackRetryDelayMs;
            }
        }

        private IActor GetUrshiActor()
        {
            try { return Hud.Game.Actors.FirstOrDefault(a => IsUrshiActor(a)); }
            catch { return null; }
        }

        private static bool IsUrshiActor(IActor actor)
        {
            return actor != null && actor.SnoActor != null && actor.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem;
        }

        private static bool IsUrshiRisk(IItem item, IActor urshi)
        {
            try { return item != null && urshi != null && item.FloorCoordinate.XYDistanceTo(urshi.FloorCoordinate) <= UrshiRiskYards; }
            catch { return false; }
        }

        private float UiScale()
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return 1f;

                float sx = Hud.Window.Size.Width / 1920f;
                float sy = Hud.Window.Size.Height / 1080f;
                float s = Math.Min(sx, sy);

                if (s < 0.65f) return 0.65f;
                if (s > 2.25f) return 2.25f;
                return s;
            }
            catch { return 1f; }
        }

        private bool TryGetUrshiSafeFallbackClickPoint(IItem item, IActor urshi, int attempt, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (item == null || item.ScreenCoordinate == null)
                return false;

            int baseX, baseY;
            GetItemClickBase(item, IsNoSpaceMaterialPickup(item), out baseX, out baseY);

            float scale = UiScale();
            int stepSmall = Math.Max(4, (int)Math.Round(6f * scale));
            int stepMed = Math.Max(8, (int)Math.Round(12f * scale));
            int stepWide = Math.Max(12, (int)Math.Round(18f * scale));

            switch ((attempt < 0 ? 0 : attempt) % 12)
            {
                case 0: x = baseX; y = baseY; break;
                case 1: x = baseX; y = baseY - stepSmall; break;
                case 2: x = baseX; y = baseY + stepSmall; break;
                case 3: x = baseX - stepSmall; y = baseY; break;
                case 4: x = baseX + stepSmall; y = baseY; break;
                case 5: x = baseX - stepMed; y = baseY - stepSmall; break;
                case 6: x = baseX + stepMed; y = baseY - stepSmall; break;
                case 7: x = baseX - stepMed; y = baseY + stepSmall; break;
                case 8: x = baseX + stepMed; y = baseY + stepSmall; break;
                case 9: x = baseX; y = baseY - stepMed; break;
                case 10: x = baseX - stepWide; y = baseY; break;
                default: x = baseX + stepWide; y = baseY; break;
            }

            return IsInsideGameWindow(x, y);
        }

        private IActor GetSelectedActorSafe()
        {
            try
            {
                return Hud != null && Hud.Game != null ? Hud.Game.SelectedActor : null;
            }
            catch { return null; }
        }

        private bool IsUrshiSelected(IActor urshi, IActor selectedActor)
        {
            try
            {
                if (urshi != null && urshi.IsSelected)
                    return true;
            }
            catch { }

            try
            {
                return IsUrshiActor(selectedActor);
            }
            catch { return false; }
        }

        private static bool IsSelectedActorItem(IActor selectedActor)
        {
            try
            {
                return selectedActor != null && selectedActor.GizmoType == GizmoType.Item;
            }
            catch { return false; }
        }


        private static void MouseLeftClick()
        {
            mouse_event(6U, 0, 0, 0U, IntPtr.Zero);
        }


        private static void SendSpace()
        {
            keybd_event(0x20, 0, 0, UIntPtr.Zero);
            keybd_event(0x20, 0, 2, UIntPtr.Zero);
        }

        private struct NativePoint { public int X; public int Y; }
        private struct AutoUrshiReturnPoint
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public AutoUrshiReturnPoint(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
        private struct DropSuppress
        {
            public readonly long Until;
            public readonly long VisibleCheckAfter;
            public DropSuppress(long until, long visibleCheckAfter) { Until = until; VisibleCheckAfter = visibleCheckAfter; }
        }
        private sealed class LootCandidate
        {
            public readonly IItem Item;
            public readonly int Priority;
            public readonly bool UrshiRisk;
            public LootCandidate(IItem item, int priority, bool urshiRisk) { Item = item; Priority = priority; UrshiRisk = urshiRisk; }
        }
    }
}
