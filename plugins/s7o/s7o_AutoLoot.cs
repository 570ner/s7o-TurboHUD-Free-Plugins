using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Commits to Urshi as soon as the established primary reward pile is clear.
    public class s7o_AutoLoot : BasePlugin, IAfterCollectHandler, IItemPickedHandler, IItemLocationChangedHandler, INewAreaHandler, IMonsterKilledHandler
    {
        public const int DefaultNormalPickupRangeYards = 10;
        public const int DefaultEventPickupRangeYards = 40;
        public const int MinNormalPickupRangeYards = 3;
        public const int MaxNormalPickupRangeYards = 30;
        public const int MinEventPickupRangeYards = 10;
        public const int MaxEventPickupRangeYards = 120;

        private const int MovingPickupRangeCapYards = 5;
        // GR rewards can remain visible while Zei-range positioning leaves the hero
        // well outside the normal event radius. A direct item click lets Diablo's
        // pathfinder approach the reward; this envelope is post-Rift-only.
        private const int PostRiftApproachRangeYards = 80;
        private const int LootBurstMonsterBlockYards = 45;
        private const int LootBurstThreshold = 8;
        private const int LootBurstLatchMs = 5000;
        // Confirm that the guardian reward set stays empty across delayed native
        // drop frames before returning to ordinary pickup range.
        private const int NephalemRiftRewardEmptyConfirmMs = 250;
        private const int NormalDelayMs = 80;
        private const int CursorRestoreDelayMs = 15;
        private const int CursorRestoreExpireMs = 250;
        // Native pickup/location confirmation arrived within 183 ms in the
        // validation logs. This is only a same-seed fallback watchdog; other
        // candidates remain immediately available to the normal selector.
        private const int PickupAcknowledgeMs = 200;
        private const int HazardHoverMaxCollections = 8;
        private const int CleanupDelayMs = 25;
        private const int CleanupFarMoveDelayMs = 220;
        private const int SpecialCleanupDelayMs = 55;
        private const int SpecialCleanupFarMoveDelayMs = 180;
        private const int StackedLootDelayMs = 22;
        private const int StackedLootSkipMs = 75;
        private const int StackedLootRotationMemoryMs = 650;
        private const float StackedLootScreenRadiusPx = 22f;
        private const float StackedLootWorldRadiusYards = 1.8f;

        // Same proven 1920x1080 no-click regions used by HUD Menu AutoSnap and ZDH.
        // Uniform scaling preserves UI size; left/center/right anchoring preserves placement on ultrawide windows.
        private const float ClickGuardReferenceWidth = 1920f;
        private const float ClickGuardReferenceHeight = 1080f;
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
        private const int MovementSampleMs = 90;
        private const float MovementThresholdYards = 0.22f;
        private const int MaxAttempts = 8;
        private const int StuckRetryCooldownMs = 6000;
        private const int NoSpacePickupRetryCooldownMs = 1800;
        private const int ProtectedChestBlockYards = 45;
        private const int ProtectedChestRiskYards = 16;
        private const int VisionFightBlockYards = 70;
        private const int GoblinPackMinCount = 2;
        private const int GoblinPackBlockYards = 120;
        private const int GoblinPackClearMs = 2000;
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
        // Accidental Urshi clicks can advance through conversation before the gem pane appears.
        // Keep recovery bounded, but leave enough Space attempts to close the full UI chain.
        private const int UrshiSpaceMaxAttempts = 6;
        private const int UrshiProblemItemMisclickLimit = 5;
        private const int UrshiPortalCancelRetryMs = 120;
        private const int UrshiPortalCancelMaxAttempts = 3;
        private const int UrshiPortalArbitrationMs = 180;
        private const int UrshiPortalFollowupMs = 700;
        private const int UrshiProblemItemSuppressMs = 1800;
        private const int UrshiFallbackRetryDelayMs = 70;
        private const int UrshiFallbackWindowMs = 2200;
        private const int UrshiFallbackMaxTries = 8;
        private const int AutoUrshiRewardSettleMs = 4000;
        private const int AutoUrshiLegendaryRewardMinObserved = 10;
        private const int AutoUrshiTalkClickDelayMs = 700;
        private const int AutoUrshiTalkMaxAttempts = 12;
        private const int AutoUrshiTalkRetryCooldownMs = 8000;
        private const int AutoUrshiTalkHoverSettleMs = 70;
        private const int AutoUrshiTalkProbeRetryMs = 20;
        private const int AutoUrshiRecentTalkLootCancelWindowMs = 1800;
        private const int AutoUrshiTalkLootCancelRetryMs = 70;
        private const int AutoUrshiTalkLootCancelMaxAttempts = 3;
        private const float AutoUrshiFarLootRiskYards = 55f;
        private const float AutoUrshiBreadcrumbStepYards = 4f;
        private const float AutoUrshiReturnMinClickYards = 2f;
        private const int AutoUrshiBreadcrumbMax = 64;
        private const float AutoUrshiUnknownApproachMaxYards = 120f;
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
        private readonly Dictionary<int, long> _pickupAcknowledgeUntilMs = new Dictionary<int, long>();
        private readonly Dictionary<int, DropSuppress> _droppedSuppress = new Dictionary<int, DropSuppress>();
        private readonly Dictionary<int, long> _cleanupStuckIgnoreUntilMs = new Dictionary<int, long>();
        private readonly Dictionary<int, long> _stackedLootSkipUntilMs = new Dictionary<int, long>();
        private long _lastStackedLootClickMs;
        private int _lastStackedLootClickX;
        private int _lastStackedLootClickY;
        private readonly Dictionary<int, int> _urshiMisclicksBySeed = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _urshiFallbackTriesBySeed = new Dictionary<int, int>();
        private IUiElement _urshiGemPane;
        private IUiElement _urshiConversationMain;
        private IUiElement _chatEditLine;
        private IUiElement _skillPaneSkillsList;
        private IUiElement _vendorMainPage;
        private IUiElement _shopMainPanel;
        private IUiElement _scriptedSequenceDialog;
        private long _lastClickMs;
        private long _urshiArmedUntilMs;
        private long _nextUrshiRiskClickMs;
        private long _urshiRiskHoverClickAtMs;
        private int _urshiRiskHoverSeed;
        private int _urshiRiskHoverX;
        private int _urshiRiskHoverY;
        private long _nextUrshiSpaceMs;
        private int _lastClickSeed;
        private int _lootProgressSerial;
        private long _lastLootProgressMs;
        private int _lastRetryRefreshSerial;
        private int _lastVisibleEligibleLootCount;
        private int _urshiArmedSeed;
        private int _urshiSpaceAttempts;
        private int _urshiFallbackSeed;
        private long _urshiFallbackUntilMs;
        private int _genericUrshiRecoverySeed;
        private long _genericUrshiRecoveryUntilMs;
        private int _urshiPortalCancelAttempts;
        private int _accidentalUrshiRecoverySeed;
        private long _accidentalUrshiRecoveryUntilMs;
        private long _accidentalUrshiPortalWatchUntilMs;
        private bool _accidentalUrshiRecoveryArmed;
        private bool _autoUrshiUnsafeHandoffRecoveryActive;
        private bool _accidentalUrshiHasClickPoint;
        private int _accidentalUrshiClickX;
        private int _accidentalUrshiClickY;
        private long _nextAutoUrshiTalkMs;
        private long _autoUrshiTalkCooldownUntilMs;
        private long _autoUrshiHoverClickAtMs;
        private int _autoUrshiTalkAttempts;
        private int _autoUrshiHoverX;
        private int _autoUrshiHoverY;
        private bool _autoUrshiTalkDone;
        private bool _autoUrshiHandoffCommitted;
        private bool _autoUrshiGemHandoffActive;
        private bool _autoUrshiHandoffPortalObserved;
        private bool _autoUrshiHandoffTransformObserved;
        private long _autoUrshiRecentTalkOpenedUntilMs;
        private int _autoUrshiTalkLootCancelAttempts;
        private long _nextAutoUrshiTalkLootCancelMs;
        private bool _autoUrshiHasRestorePoint;
        private NativePoint _autoUrshiRestorePoint;
        private long _postRiftCleanupStartedMs;
        private long _autoUrshiRewardGateStartedMs;
        private readonly HashSet<int> _autoUrshiObservedLegendarySeeds = new HashSet<int>();
        private int _autoUrshiObservedLegendaryRewardCount;
        private readonly List<AutoUrshiReturnPoint> _autoUrshiReturnTrail = new List<AutoUrshiReturnPoint>(AutoUrshiBreadcrumbMax);
        private uint _autoUrshiTrailWorldId;
        private int _autoUrshiReturnProbeTick;
        private int _autoUrshiReturnProbeX;
        private int _autoUrshiReturnProbeY;
        private long _autoUrshiReturnProbeMs;
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
        private bool _autoUrshiProbeFallbackPending;
        private long _autoUrshiApproachSampleMs;
        private float _autoUrshiApproachSampleX;
        private float _autoUrshiApproachSampleY;
        private float _autoUrshiApproachBestGoalDistance;
        private bool _autoUrshiApproachHasGoalDistance;
        private bool _cleanupLatched;
        private bool _nephalemRiftRewardPending;
        private bool _nephalemRiftRewardObserved;
        private bool _nephalemRiftRewardLootSeen;
        private long _nephalemRiftRewardEmptySinceMs;
        private long _nephalemRiftRewardLastNewFloorItemMs;
        private readonly HashSet<int> _nephalemRiftRewardSeenFloorSeeds = new HashSet<int>();
        private bool _lastCleanupClickFar;
        private int _wideCleanupCommittedSeed;
        private bool _enabled;
        private bool _paused;
        private bool _talkToUrshiAfterLoot;
        private int _normalPickupRangeYards = DefaultNormalPickupRangeYards;
        private int _eventPickupRangeYards = DefaultEventPickupRangeYards;
        private bool _primals = true, _ancients = true, _legendaries = true, _gems = true, _gifts = true, _screams = true, _trash, _materials = true, _deathsBreath;
        private uint _lastAreaSno;
        private bool _areaIsTown;
        private bool _goblinPackPaused;
        private long _goblinFreeSinceMs;
        private readonly HashSet<uint> _unopenedProtectedRewardChestAnnIds = new HashSet<uint>();
        private long _lootBurstCleanupUntilMs;
        private long _lastMovementSampleMs;
        private float _lastPlayerX;
        private float _lastPlayerY;
        private bool _playerMoving;
        private bool _deathSuspended;
        private bool _townSuspended;
        private bool _pendingCursorRestore;
        private NativePoint _pendingCursorPoint;
        private long _pendingCursorRestoreAtMs;
        private long _pendingCursorRestoreExpireMs;
        private int _hazardHoverSeed;
        private int _hazardHoverProbe;
        private bool _hazardHoverHasRestorePoint;
        private NativePoint _hazardHoverRestorePoint;
        private int _materialHoverSeed;
        private int _materialHoverX;
        private int _materialHoverY;
        private long _materialHoverExpireMs;
        private bool _materialHoverHasRestorePoint;
        private NativePoint _materialHoverRestorePoint;
        private int _materialLiftFallbackSeed;
        private int _materialProbeSeed, _materialProbeIndex, _materialProbeTick;
        private int _materialOverlapSeed;
        private int _materialOverlapChecks;
        private bool _inventoryFullAlertActive;
        private int _inventoryFullAlertUsed;
        private int _inventoryFullAlertTotal;
        private int _inventoryFullAlertWidth;
        private int _inventoryFullAlertHeight;

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
            _skillPaneSkillsList = Hud.Render.RegisterUiElement("Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList", null, null);
            _vendorMainPage = Hud.Render.RegisterUiElement("Root.NormalLayer.vendor_dialog_mainPage", null, null);
            _shopMainPanel = Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.panel", null, null);
            _scriptedSequenceDialog = Hud.Render.RegisterUiElement("Root.TopLayer.scripted_sequence", null, null);
            if (Hud.Game.IsInGame && Hud.Game.Me != null && Hud.Game.Me.SnoArea != null)
            {
                _lastAreaSno = Hud.Game.Me.SnoArea.Sno;
                _areaIsTown = Hud.Game.Me.SnoArea.IsTown;
            }
        }

        public void ConfigureAutoLoot(bool enabled, bool primals, bool ancients, bool legendaries, bool gems, bool gifts, bool screams, bool trash, bool materials, bool deathsBreath)
        {
            ConfigureAutoLoot(enabled, primals, ancients, legendaries, gems, gifts, screams, trash, materials, deathsBreath, false, DefaultNormalPickupRangeYards, DefaultEventPickupRangeYards);
        }

        public void ConfigureAutoLoot(bool enabled, bool primals, bool ancients, bool legendaries, bool gems, bool gifts, bool screams, bool trash, bool materials, bool deathsBreath, bool talkToUrshiAfterLoot)
        {
            ConfigureAutoLoot(enabled, primals, ancients, legendaries, gems, gifts, screams, trash, materials, deathsBreath, talkToUrshiAfterLoot, DefaultNormalPickupRangeYards, DefaultEventPickupRangeYards);
        }

        public void ConfigureAutoLoot(bool enabled, bool primals, bool ancients, bool legendaries, bool gems, bool gifts, bool screams, bool trash, bool materials, bool deathsBreath, bool talkToUrshiAfterLoot, int normalPickupRangeYards, int eventPickupRangeYards)
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
            _normalPickupRangeYards = Clamp(normalPickupRangeYards, MinNormalPickupRangeYards, MaxNormalPickupRangeYards);
            _eventPickupRangeYards = Clamp(eventPickupRangeYards, MinEventPickupRangeYards, MaxEventPickupRangeYards);
            if (!_talkToUrshiAfterLoot)
            {
                _autoUrshiHandoffCommitted = false;
                ResetAutoUrshiRewardBatch();
            }
            if (!_enabled)
            {
                _paused = false;
                ResetRuntimeState();
            }
        }

        public bool IsPaused { get { return _paused; } }
        public int NormalPickupRangeYards { get { return _normalPickupRangeYards; } }
        public int EventPickupRangeYards { get { return _eventPickupRangeYards; } }

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
            _pickupAcknowledgeUntilMs.Clear();
            _stackedLootSkipUntilMs.Clear();
            _lastStackedLootClickMs = 0;
            _lastStackedLootClickX = 0;
            _lastStackedLootClickY = 0;
            if (!keepDroppedSuppress)
                _droppedSuppress.Clear();
            _cleanupStuckIgnoreUntilMs.Clear();
            _lastClickSeed = 0;
            _lootProgressSerial = 0;
            _lastLootProgressMs = 0;
            _lastRetryRefreshSerial = 0;
            _lastVisibleEligibleLootCount = -1;
            _lastCleanupClickFar = false;
            _wideCleanupCommittedSeed = 0;
            _cleanupLatched = false;
            _nephalemRiftRewardPending = false;
            _nephalemRiftRewardObserved = false;
            _nephalemRiftRewardLootSeen = false;
            _nephalemRiftRewardEmptySinceMs = 0;
            _nephalemRiftRewardLastNewFloorItemMs = 0;
            _nephalemRiftRewardSeenFloorSeeds.Clear();
            _goblinPackPaused = false;
            _goblinFreeSinceMs = 0;
            _unopenedProtectedRewardChestAnnIds.Clear();
            _lootBurstCleanupUntilMs = 0;
            _lastMovementSampleMs = 0;
            _playerMoving = false;
            _pendingCursorRestore = false;
            ClearHazardHoverState(false, 0);
            ClearMaterialHoverState(true);
            _materialLiftFallbackSeed = 0;
            _materialProbeSeed = _materialProbeIndex = _materialProbeTick = 0;
            _materialOverlapSeed = 0;
            _materialOverlapChecks = 0;
            _inventoryFullAlertActive = false;
            _inventoryFullAlertUsed = 0;
            _inventoryFullAlertTotal = 0;
            _inventoryFullAlertWidth = 0;
            _inventoryFullAlertHeight = 0;
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
            ClearGenericUrshiRecoveryState();
            ClearAccidentalUrshiRecoveryState();
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiHandoffCommitted = false;
            _autoUrshiGemHandoffActive = false;
            ResetAutoUrshiGemHandoffWatch();
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            _autoUrshiHasRestorePoint = false;
            _autoUrshiRestorePoint = new NativePoint();
            _postRiftCleanupStartedMs = 0;
            ResetAutoUrshiRewardBatch();
            ResetAutoUrshiReturnState();
        }

        public void OnItemPicked(IItem item)
        {
            if (item == null) return;
            if (!IsBloodShard(item))
                MarkLootPickupProgress();
            _attempts.Remove(item.Seed);
            _retryAfterMs.Remove(item.Seed);
            _pickupAcknowledgeUntilMs.Remove(item.Seed);
            if (_lastClickSeed == item.Seed) _lastClickSeed = 0;
            if (_wideCleanupCommittedSeed == item.Seed) _wideCleanupCommittedSeed = 0;
            _droppedSuppress.Remove(item.Seed);
            _stackedLootSkipUntilMs.Remove(item.Seed);
            if (_hazardHoverSeed == item.Seed)
                ClearHazardHoverState(false, 0);
            if (_materialHoverSeed == item.Seed)
                ClearMaterialHoverState(false);
            ClearMaterialFallback(item.Seed);
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
            if (_accidentalUrshiRecoverySeed == item.Seed)
                ClearAccidentalUrshiRecoveryState();
        }

        public void OnItemLocationChanged(IItem item, ItemLocation from, ItemLocation to)
        {
            if (item == null) return;
            if (from == ItemLocation.Floor && to != ItemLocation.Floor)
            {
                if (!IsBloodShard(item))
                    MarkLootPickupProgress();

                if (item.Seed == _genericUrshiRecoverySeed)
                    ClearGenericUrshiRecoveryState();
                if (item.Seed == _accidentalUrshiRecoverySeed)
                    ClearAccidentalUrshiRecoveryState();
            }
            if (to != ItemLocation.Floor)
            {
                _pickupAcknowledgeUntilMs.Remove(item.Seed);
                _cleanupStuckIgnoreUntilMs.Remove(item.Seed);
                if (_wideCleanupCommittedSeed == item.Seed) _wideCleanupCommittedSeed = 0;
                if (_hazardHoverSeed == item.Seed)
                    ClearHazardHoverState(false, 0);
                if (_materialHoverSeed == item.Seed)
                    ClearMaterialHoverState(false);
                ClearMaterialFallback(item.Seed);
            }
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

            _areaIsTown = area != null && area.IsTown;
        }

        public void OnMonsterKilled(IMonster monster)
        {
            if (monster == null || monster.Rarity != ActorRarity.Boss)
                return;

            try
            {
                if (Hud.Game.RiftPercentage < 100.0d)
                    return;

                if (Hud.Game.SpecialArea == SpecialArea.GreaterRift)
                {
                    // Anchor the reward gate to the native guardian-death event. GR
                    // progress can reach 100% several seconds before the boss dies.
                    long now = Math.Max(1L, Hud.Game.CurrentRealTimeMilliseconds);
                    _postRiftCleanupStartedMs = now;
                    _autoUrshiHandoffCommitted = false;
                    ResetAutoUrshiRewardBatch();
                    _autoUrshiRewardGateStartedMs = now;
                    return;
                }

                if (Hud.Game.SpecialArea != SpecialArea.Rift)
                    return;

                _nephalemRiftRewardPending = true;
                _nephalemRiftRewardObserved = false;
                _nephalemRiftRewardLootSeen = false;
                _nephalemRiftRewardEmptySinceMs = 0;
                _nephalemRiftRewardLastNewFloorItemMs = 0;
                _nephalemRiftRewardSeenFloorSeeds.Clear();
            }
            catch { }
        }

        public void AfterCollect()
        {
            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            IPlayer me = Hud.Game.Me;
            if (me == null)
                return;

            if (me.IsDead || me.IsDeadSafeCheck)
            {
                if (!_deathSuspended)
                {
                    ResetRuntimeState(true);
                    _deathSuspended = true;
                }
                return;
            }

            _deathSuspended = false;

            ISnoArea area = me.SnoArea;
            uint areaSno = area != null ? area.Sno : 0;
            if (areaSno != 0 && areaSno != _lastAreaSno)
            {
                _lastAreaSno = areaSno;
                ResetRuntimeState();
                _areaIsTown = area != null && area.IsTown;
            }

            ISnoArea sceneArea = me.Scene != null ? me.Scene.SnoArea : null;
            bool townContext = _areaIsTown || Hud.Game.IsInTown || me.IsInTown ||
                (area != null && area.IsTown) || (sceneArea != null && sceneArea.IsTown);
            if (townContext)
            {
                if (!_townSuspended)
                {
                    ResetRuntimeState(true);
                    _townSuspended = true;
                }
                return;
            }

            _townSuspended = false;
            ProcessPendingCursorRestore(Hud.Game.CurrentRealTimeMilliseconds);

            if (!_enabled || _paused || Hud.Game.IsPaused || !Hud.Window.IsForeground)
            {
                ClearMaterialHoverState(true);
                return;
            }

            long now = Hud.Game.CurrentRealTimeMilliseconds;
            if (Hud.Game.IsLoading)
            {
                ClearMaterialHoverState(true);
                return;
            }

            // Record the guardian-phase route even while stationary combat owns input.
            // This observes travel only; it never clicks before the loot handoff.
            TrackAutoUrshiReturnTrail();

            if (HandlePendingAccidentalUrshiRecovery(now, me))
            {
                ClearMaterialHoverState(true);
                return;
            }

            if (me.Powers == null || me.Powers.CantMove)
            {
                ClearMaterialHoverState(true);
                return;
            }

            if (area != null && sceneArea != null && area.IsTown != sceneArea.IsTown)
            {
                _lootBurstCleanupUntilMs = 0;
                ClearMaterialHoverState(true);
                return;
            }

            if (ShouldPauseForGoblinPack(now))
            {
                _lootBurstCleanupUntilMs = 0;
                ClearMaterialHoverState(true);
                return;
            }

            PurgeDroppedSuppressions(now);
            PurgeStackedLootSkips(now);
            PurgeRetryState(now);
            PurgeCleanupStuckIgnores(now);

            // Drop stale risky-click recovery state before interpreting a user-opened Urshi pane.
            // A successfully picked/vanished item must not make the next manual pane look accidental.
            PurgeResolvedUrshiArmedState(now);

            // Urshi recovery must run before inventory/vendor UI early returns because Urshi opens UI layers.
            if (HandleAutoLootUrshiRecovery(now))
            {
                ClearMaterialHoverState(true);
                return;
            }

            // Recovery above intentionally owns Urshi conversation/gem UI. Outside that
            // lifecycle, never synthesize world clicks while a normal blocking UI is open.
            if (IsBlockingLootUiOpen())
            {
                ClearMaterialHoverState(true);
                return;
            }

            var state = me.AnimationState;
            bool combatAction = state == AcdAnimationState.Attacking || state == AcdAnimationState.Casting || state == AcdAnimationState.Channeling;
            bool playerMoving = UpdatePlayerMovement(now);

            if (state == AcdAnimationState.CastingPortal)
            {
                ClearMaterialHoverState(true);
                return;
            }
            // Transform frames did not produce a single confirmed pickup in telemetry.
            // Resume from the native state transition instead of burning retry attempts.
            if (state == AcdAnimationState.Transform)
            {
                ClearMaterialHoverState(true);
                return;
            }
            // Wide reward cleanup must not override a stationary attack/channel.
            // Movement-compatible channels (for example Strafe/Whirlwind) continue
            // through the existing native movement-state path.
            if (combatAction && !playerMoving)
            {
                ClearMaterialHoverState(true);
                return;
            }

            // Reward timers begin only when user-controlled stationary combat ends,
            // so waiting for a channel does not consume the cleanup window.
            TrackProtectedRewardChestOpen(now);
            TrackNephalemRiftRewardWindow(now);

            IActor protectedChest = GetUnopenedProtectedChest();
            bool protectedChestBlocked = protectedChest != null;
            bool postRiftCleanup = !protectedChestBlocked && IsPostRiftCleanup();
            TrackPostRiftCleanupWindow(postRiftCleanup, now);
            bool lootBurstCleanup = !protectedChestBlocked && IsLootBurstCleanup(now);
            bool nephalemRiftRewardCleanup = !protectedChestBlocked && _nephalemRiftRewardObserved &&
                (lootBurstCleanup || !HasNearbyAttackableMonster(LootBurstMonsterBlockYards));
            bool wideCleanup = postRiftCleanup || lootBurstCleanup || nephalemRiftRewardCleanup;
            if (!wideCleanup)
                _wideCleanupCommittedSeed = 0;
            int normalRange = (state == AcdAnimationState.Running || (combatAction && playerMoving))
                ? Math.Min(MovingPickupRangeCapYards, _normalPickupRangeYards)
                : _normalPickupRangeYards;
            int range = postRiftCleanup || nephalemRiftRewardCleanup
                ? PostRiftLootRangeYards()
                : (lootBurstCleanup ? _eventPickupRangeYards : normalRange);
            int freeSlots = SafeFreeSlots();
            IActor urshi = GetUrshiActor();
            TrackAutoUrshiReturnState(postRiftCleanup, now, urshi);
            BeginAutoUrshiRewardBatch(postRiftCleanup, now, urshi != null);
            var visibleCandidates = Hud.Game.Items
                .Where(i => i != null && i.Location == ItemLocation.Floor && i.IsOnScreen && !IsExcludedPickup(i) && !IsSuppressedDroppedItem(i, now) && !IsCleanupStuckIgnored(i, now) && !IsProtectedChestRisk(i, protectedChest) && i.CentralXyDistanceToMe <= range)
                .Select(i => new LootCandidate(i, WantedPriority(i), IsUrshiRisk(i, urshi)))
                .Where(c => c.Priority >= 0 && IsAutoUrshiHandoffLoot(c.Item))
                .ToList();
            if (nephalemRiftRewardCleanup)
            {
                TrackNephalemRiftRewardFloorActivity(now);
                UpdateNephalemRiftRewardCompletion(visibleCandidates.Count, now);
            }
            UpdateInventoryFullAlert(visibleCandidates, freeSlots);
            var candidates = visibleCandidates
                .Where(c => CanFit(c.Item, freeSlots))
                .ToList();
            if (_hazardHoverSeed != 0 && !candidates.Any(c => c.Item != null && c.Item.Seed == _hazardHoverSeed))
                ClearHazardHoverState(true, now);
            if (_materialHoverSeed != 0 && !candidates.Any(c => c.Item != null && c.Item.Seed == _materialHoverSeed))
                ClearMaterialHoverState(true);
            if (_materialLiftFallbackSeed != 0 && !candidates.Any(c => c.Item != null && c.Item.Seed == _materialLiftFallbackSeed))
                ClearMaterialFallback(_materialLiftFallbackSeed);

            // Once the established gem/legendary pile is clear, commit before
            // ordinary late drops can hold the Urshi handoff open.
            if (postRiftCleanup && TryCommitAutoUrshiHandoff(now) && candidates.Count > 0)
                candidates = candidates.Where(c => IsAutoUrshiHandoffLoot(c.Item)).ToList();

            TrackVisibleEligibleLootProgress(candidates.Count);
            if (postRiftCleanup && candidates.Count > 0)
            {
                _autoUrshiReturning = false;
            }

            if (candidates.Count == 0)
            {
                if (postRiftCleanup)
                {
                    if (_autoUrshiTalkDone || _autoUrshiApproachAborted)
                        return;

                    if (!TryCommitAutoUrshiHandoff(now))
                        return;

                    if ((_autoUrshiActorPathActive || _autoUrshiReturning) &&
                        !UpdateAutoUrshiApproachProgress(now))
                        return;

                    // An actor click hands movement to Diablo's pathfinder. Never replace
                    // that command with a breadcrumb click if on-screen state flickers.
                    if (_autoUrshiActorPathActive)
                    {
                        if (!playerMoving && IsAutoUrshiTalkActorClickable(urshi))
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
                : (lootBurstCleanup || nephalemRiftRewardCleanup
                    ? (_lastCleanupClickFar ? SpecialCleanupFarMoveDelayMs : SpecialCleanupDelayMs)
                    : NormalDelayMs);
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
            var target = _materialHoverSeed != 0
                ? tryCandidates.FirstOrDefault(c => c.Item != null && c.Item.Seed == _materialHoverSeed)
                : null;
            if (target == null)
                target = GetHazardHoverCandidate(tryCandidates);
            if (target == null)
                target = GetCommittedWideCleanupCandidate(tryCandidates, wideCleanup, stackedLoot);
            if (target == null)
                target = SelectBestCandidate(tryCandidates, wideCleanup, now, stackedLoot, farUrshiLootRisk);
            if (target == null && stackedLoot)
                target = SelectBestCandidate(tryCandidates, wideCleanup, now, false, farUrshiLootRisk);

            if (target == null)
            {
                ResetAutoUrshiTalkReadyState();
                return;
            }

            LootCandidate selectedMaterial = GetSelectedStackedMaterialCandidate(target, tryCandidates);
            if (selectedMaterial != null)
            {
                // Keep the confirmed cursor/restore point; only the material identity changes.
                target = selectedMaterial;
                _materialHoverSeed = target.Item.Seed;
            }

            bool targetStacked = stackedLoot && IsStackedWithAnother(target, candidates);
            if (wideCleanup && !targetStacked)
                _wideCleanupCommittedSeed = target.Item.Seed;
            else if (!wideCleanup || _wideCleanupCommittedSeed == target.Item.Seed)
                _wideCleanupCommittedSeed = 0;

            ClickItem(target.Item, target.UrshiRisk && urshi != null, wideCleanup, targetStacked, now);
        }

        private void MarkLootPickupProgress()
        {
            _lootProgressSerial = _lootProgressSerial == int.MaxValue ? 1 : _lootProgressSerial + 1;
            try { _lastLootProgressMs = Hud.Game.CurrentRealTimeMilliseconds; }
            catch { _lastLootProgressMs = 0; }
        }

        private void TrackPostRiftCleanupWindow(bool postRiftCleanup, long now)
        {
            if (postRiftCleanup)
            {
                if (_postRiftCleanupStartedMs == 0)
                {
                    _postRiftCleanupStartedMs = now;
                    _autoUrshiHandoffCommitted = false;
                    ResetAutoUrshiRewardBatch();
                }
                return;
            }

            _postRiftCleanupStartedMs = 0;
            _autoUrshiHandoffCommitted = false;
            ResetAutoUrshiRewardBatch();
            if (Hud.Game.SpecialArea != SpecialArea.GreaterRift || Hud.Game.RiftPercentage < 100.0d)
                ResetAutoUrshiReturnState();
        }

        private void ResetAutoUrshiRewardBatch()
        {
            _autoUrshiRewardGateStartedMs = 0;
            _autoUrshiObservedLegendarySeeds.Clear();
            _autoUrshiObservedLegendaryRewardCount = 0;
        }

        private void BeginAutoUrshiRewardBatch(bool postRiftCleanup, long now, bool urshiAvailable)
        {
            if (!_talkToUrshiAfterLoot || !postRiftCleanup || _autoUrshiHandoffCommitted ||
                (!urshiAvailable && _autoUrshiRewardGateStartedMs == 0))
                return;

            if (_autoUrshiRewardGateStartedMs == 0)
                _autoUrshiRewardGateStartedMs = now;

            ObserveAutoUrshiLegendaryRewards(now);
        }

        private void ObserveAutoUrshiLegendaryRewards(long now)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Items == null)
                    return;

                IActor protectedChest = GetUnopenedProtectedChest();
                int freeSlots = SafeFreeSlots();
                int range = PostRiftLootRangeYards();

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Seed == 0 || item.Location != ItemLocation.Floor ||
                        !item.IsOnScreen || !IsAutoUrshiCountedLegendaryReward(item))
                        continue;
                    if (IsExcludedPickup(item) || IsSuppressedDroppedItem(item, now) ||
                        IsProtectedChestRisk(item, protectedChest) ||
                        item.CentralXyDistanceToMe > range || WantedPriority(item) < 0 ||
                        !CanFit(item, freeSlots))
                        continue;

                    if (_autoUrshiObservedLegendarySeeds.Add(item.Seed))
                        _autoUrshiObservedLegendaryRewardCount = _autoUrshiObservedLegendarySeeds.Count;
                }
            }
            catch { }
        }

        private void ClearGenericUrshiRecoveryState()
        {
            _genericUrshiRecoverySeed = 0;
            _genericUrshiRecoveryUntilMs = 0;
            _urshiPortalCancelAttempts = 0;
        }

        private void ArmGenericUrshiPickupRecovery(IItem item, bool cleanup, long now)
        {
            if (!_talkToUrshiAfterLoot || !cleanup || item == null)
                return;

            ClearAccidentalUrshiRecoveryState();
            _genericUrshiRecoverySeed = item.Seed;
            _genericUrshiRecoveryUntilMs = now + UrshiPanelRecoveryWindowMs;
            _urshiPortalCancelAttempts = 0;
            CacheAccidentalUrshiClickPoint(GetUrshiActor());
        }

        private void ResetAutoUrshiReturnState()
        {
            _autoUrshiReturnTrail.Clear();
            _autoUrshiTrailWorldId = 0;
            _autoUrshiReturnProbeTick = 0;
            _autoUrshiReturnProbeMs = 0;
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
            _autoUrshiProbeFallbackPending = false;
            ResetAutoUrshiApproachSample();
        }

        private void TrackAutoUrshiReturnState(bool postRiftCleanup, long now, IActor urshi)
        {
            if (!_talkToUrshiAfterLoot || !postRiftCleanup)
                return;

            try
            {
                if (urshi != null && urshi.FloorCoordinate != null && urshi.WorldId == Hud.Game.Me.WorldId)
                {
                    _autoUrshiHasLastSeenWorld = true;
                    _autoUrshiLastSeenX = urshi.FloorCoordinate.X;
                    _autoUrshiLastSeenY = urshi.FloorCoordinate.Y;
                    _autoUrshiLastSeenZ = urshi.FloorCoordinate.Z;
                    _autoUrshiLastSeenMs = now;

                    if (IsAutoUrshiTalkActorClickable(urshi))
                    {
                        _autoUrshiReturnProbeTick = 0;
                        _autoUrshiReturnClicks = 0;
                        _nextAutoUrshiReturnMs = 0;
                        if (_autoUrshiReturning)
                        {
                            ResetAutoUrshiApproachSample();
                            ResetAutoUrshiTalkProbesAfterReturn();
                        }
                        _autoUrshiReturning = false;
                    }
                }

            }
            catch { }
        }

        private void TrackAutoUrshiReturnTrail()
        {
            if (!_talkToUrshiAfterLoot || Hud.Game.SpecialArea != SpecialArea.GreaterRift ||
                Hud.Game.RiftPercentage < 100.0d)
                return;

            var me = Hud.Game.Me;
            if (me == null || me.FloorCoordinate == null || !me.FloorCoordinate.IsValid)
                return;
            if (_autoUrshiTrailWorldId != me.WorldId)
            {
                ResetAutoUrshiReturnState();
                _autoUrshiTrailWorldId = me.WorldId;
            }
            if (me.AnimationState == AcdAnimationState.Transform)
            {
                _autoUrshiReturnTrail.Clear(); // A teleport is not a traversed ground segment.
                _autoUrshiReturnProbeTick = 0;
                if (_autoUrshiReturning || _autoUrshiActorPathActive)
                    AbortAutoUrshiApproach(Hud.Game.CurrentRealTimeMilliseconds);
                return;
            }
            if (_autoUrshiReturning || _autoUrshiActorPathActive || _autoUrshiApproachAborted || _autoUrshiTalkDone)
                return;

            var position = me.FloorCoordinate;
            if (_autoUrshiReturnTrail.Count > 0)
            {
                var last = _autoUrshiReturnTrail[_autoUrshiReturnTrail.Count - 1];
                float distance = position.XYDistanceTo(last.X, last.Y);
                if (distance < AutoUrshiBreadcrumbStepYards)
                    return;
                if (distance > 20f)
                    _autoUrshiReturnTrail.Clear(); // Loading/jumps must not join disconnected paths.
            }
            _autoUrshiReturnTrail.Add(new AutoUrshiReturnPoint(position.X, position.Y, position.Z));
            if (_autoUrshiReturnTrail.Count > AutoUrshiBreadcrumbMax)
                _autoUrshiReturnTrail.RemoveAt(0);
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
            // Consume every progress serial, but refresh only a recently collapsed
            // genuine stack near its last click. Unrelated items keep their cooldowns.
            bool recentStackedProgress = _lastLootProgressMs > 0 &&
                _lastStackedLootClickMs > 0 &&
                now >= _lastLootProgressMs &&
                now >= _lastStackedLootClickMs &&
                _lastLootProgressMs >= _lastStackedLootClickMs &&
                now - _lastLootProgressMs <= StackedLootRotationMemoryMs &&
                now - _lastStackedLootClickMs <= StackedLootRotationMemoryMs;
            double refreshRadius = StackedLootScreenRadiusPx * 2.0d;
            double refreshRadiusSquared = refreshRadius * refreshRadius;
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Item == null) continue;
                if (!recentStackedProgress || candidate.Item.ScreenCoordinate == null ||
                    StackedLootScreenDistanceSquared(candidate.Item) > refreshRadiusSquared)
                    continue;

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

            if (_pickupAcknowledgeUntilMs.Count != 0)
            {
                foreach (var pair in _pickupAcknowledgeUntilMs.ToArray())
                {
                    if (now >= pair.Value || !IsVisibleFloorSeed(pair.Key))
                        _pickupAcknowledgeUntilMs.Remove(pair.Key);
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
            long acknowledgeUntil;
            if (_pickupAcknowledgeUntilMs.TryGetValue(item.Seed, out acknowledgeUntil))
            {
                if (now < acknowledgeUntil) return false;
                _pickupAcknowledgeUntilMs.Remove(item.Seed);
            }

            long retryAt;
            _attempts.TryGetValue(item.Seed, out n);
            if (_retryAfterMs.TryGetValue(item.Seed, out retryAt))
            {
                // Exact selection alone is not pickup progress: an automatic skill
                // can leave an item selected while repeatedly canceling its click.
                // A real movement transition still releases the cooldown immediately.
                bool movedToSelectedItem = !riskyUrshi && _playerMoving && IsExactItemSelected(item);
                if (now < retryAt && !movedToSelectedItem) return false;
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
            List<LootCandidate> eligible = candidates
                .Where(c => !respectStackedSkip || !IsStackedLootTemporarilySkipped(c.Item, now))
                .ToList();

            LootCandidate best = eligible
                .OrderBy(c => c.UrshiRisk ? 1 : 0)
                .ThenBy(c => c.Item.Seed == _lastClickSeed ? 1 : 0)
                .ThenByDescending(c => farUrshiLootRisk ? DistanceToLastSeenUrshi(c.Item) : 0f)
                .ThenBy(c => wideCleanup ? 0 : c.Priority)
                .ThenBy(c => c.Item.CentralXyDistanceToMe)
                .ThenBy(c => c.Priority)
                .FirstOrDefault();

            if (best == null || _lastStackedLootClickMs == 0 || now - _lastStackedLootClickMs > StackedLootRotationMemoryMs)
                return best;

            List<LootCandidate> cluster = eligible
                .Where(c => c != null && c.Item != null
                    && c.UrshiRisk == best.UrshiRisk
                    && IsStackedLootPair(c.Item, best.Item))
                .ToList();

            if (cluster.Count < 2)
                return best;

            // A collapsing floor-label stack is faster and more reliable when the next
            // click jumps away from the previous label position instead of chasing the
            // newly adjacent row. This changes only candidate order; Urshi-risk items
            // still use the normal hover/selection validation before any click.
            return cluster
                .OrderBy(c => c.Item.Seed == _lastClickSeed ? 1 : 0)
                .ThenByDescending(c => StackedLootScreenDistanceSquared(c.Item))
                .ThenBy(c => wideCleanup ? 0 : c.Priority)
                .ThenBy(c => c.Item.CentralXyDistanceToMe)
                .ThenBy(c => c.Priority)
                .FirstOrDefault() ?? best;
        }

        private LootCandidate GetCommittedWideCleanupCandidate(List<LootCandidate> candidates, bool wideCleanup, bool stackedLoot)
        {
            if (!wideCleanup || _wideCleanupCommittedSeed == 0)
            {
                _wideCleanupCommittedSeed = 0;
                return null;
            }

            LootCandidate committed = candidates.FirstOrDefault(c =>
                c != null && c.Item != null && c.Item.Seed == _wideCleanupCommittedSeed);

            // Stacked labels keep their proven rotation. A separated reward keeps the
            // current native pathfinder destination until floor state or retry state
            // proves that this seed is resolved or temporarily unavailable.
            if (committed == null || (stackedLoot && IsStackedWithAnother(committed, candidates)))
            {
                _wideCleanupCommittedSeed = 0;
                return null;
            }

            return committed;
        }

        private LootCandidate GetSelectedStackedMaterialCandidate(LootCandidate anchor, List<LootCandidate> candidates)
        {
            if (anchor == null || anchor.Item == null || anchor.UrshiRisk
                || _materialHoverSeed != anchor.Item.Seed || !IsNoSpaceMaterialPickup(anchor.Item)
                || Hud.Game.CurrentGameTick <= _materialProbeTick)
                return null;

            NativePoint cursor;
            if (!GetCursorPos(out cursor) || !IsSafeSyntheticWorldClick(cursor.X, cursor.Y))
                return null;
            double dx = cursor.X - _materialHoverX;
            double dy = cursor.Y - _materialHoverY;
            double radius = Math.Max(3.0d, 4.0d * UiScale());
            if (dx * dx + dy * dy > radius * radius)
                return null;

            // All entries already passed wanted/fit/range/retry guards. Clear a
            // selectable material from the same pile instead of chasing its neighbor.
            foreach (LootCandidate candidate in candidates)
            {
                if (candidate.Item == null || candidate.Item.Seed == anchor.Item.Seed
                    || candidate.UrshiRisk || candidate.Priority > anchor.Priority
                    || !IsNoSpaceMaterialPickup(candidate.Item)
                    || !IsStackedLootPair(anchor.Item, candidate.Item))
                    continue;
                if (IsExactItemSelected(candidate.Item) && IsCursorNearMaterialBase(candidate.Item, cursor))
                    return candidate;
            }
            return null;
        }

        private LootCandidate GetHazardHoverCandidate(List<LootCandidate> candidates)
        {
            if (_hazardHoverSeed == 0 || candidates == null)
                return null;

            LootCandidate pending = candidates.FirstOrDefault(c =>
                c != null && c.Item != null && c.Item.Seed == _hazardHoverSeed);
            if (pending != null)
                return pending;

            ClearHazardHoverState(true, Hud.Game.CurrentRealTimeMilliseconds);
            return null;
        }

        private double StackedLootScreenDistanceSquared(IItem item)
        {
            try
            {
                if (item == null || item.ScreenCoordinate == null) return 0d;
                double x = item.ScreenCoordinate.X + Hud.Window.Offset.X - _lastStackedLootClickX;
                double y = item.ScreenCoordinate.Y + Hud.Window.Offset.Y - _lastStackedLootClickY;
                return x * x + y * y;
            }
            catch { return 0d; }
        }

        private void RememberStackedLootClick(IItem item, long now)
        {
            try
            {
                if (item == null || item.ScreenCoordinate == null || Hud == null || Hud.Window == null) return;
                _lastStackedLootClickX = (int)Math.Round(item.ScreenCoordinate.X + Hud.Window.Offset.X);
                _lastStackedLootClickY = (int)Math.Round(item.ScreenCoordinate.Y + Hud.Window.Offset.Y);
                _lastStackedLootClickMs = now;
            }
            catch { }
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
            if (candidate == null || candidate.Item == null || candidate.Item.ScreenCoordinate == null || candidates == null)
                return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                LootCandidate other = candidates[i];
                if (other == null || other == candidate || other.Item == null || other.Item.Seed == candidate.Item.Seed || other.Item.ScreenCoordinate == null)
                    continue;
                if (other.UrshiRisk != candidate.UrshiRisk)
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

        private void PauseDhStrafeForPickup()
        {
            try
            {
                s7o_DHStrafePrimaryPlugin strafe = Hud.GetPlugin<s7o_DHStrafePrimaryPlugin>();
                if (strafe != null && strafe.Enabled)
                    strafe.PauseForAutoLootPickup();
            }
            catch { }
        }

        private void StopDhStrafeForUrshiHandoff()
        {
            try
            {
                s7o_DHStrafePrimaryPlugin strafe = Hud.GetPlugin<s7o_DHStrafePrimaryPlugin>();
                if (strafe != null)
                    strafe.StopForAutoLootUrshiHandoff();
            }
            catch { }
        }

        private void ClickItem(IItem item, bool riskyUrshi, bool cleanup, bool stackedLoot, long now)
        {
            PauseDhStrafeForPickup();

            NativePoint old = new NativePoint();
            bool restore = !cleanup && GetCursorPos(out old);
            int tries = 0;
            _attempts.TryGetValue(item.Seed, out tries);

            if (riskyUrshi && HandleUrshiRiskLootHoverClick(item, tries, stackedLoot, now))
                return;

            if (IsNoSpaceMaterialPickup(item) &&
                HandleMaterialConfirmedRetry(item, tries, cleanup, stackedLoot, now, old, restore))
                return;

            IActor selectedBeforeMove = GetSelectedActorSafe();
            bool hazardHoverPending = _hazardHoverSeed == item.Seed;
            int pointAttempt = tries + (hazardHoverPending ? _hazardHoverProbe : 0);
            int x, y;
            bool hasClickPoint = TryGetUiSafeItemClickPoint(item, pointAttempt, cleanup, stackedLoot, out x, out y);

            if (!hasClickPoint || !TrySetCursorForWorldClick(x, y))
            {
                if (hazardHoverPending)
                    ClearHazardHoverState(true, now);
                // Rotate away briefly instead of hammering a label that currently overlaps UI.
                _retryAfterMs[item.Seed] = now + Math.Max(75, StackedLootSkipMs);
                _lastClickMs = now;
                return;
            }

            if (hazardHoverPending)
            {
                if (IsHazardousSelectedInteractable(selectedBeforeMove))
                {
                    _hazardHoverProbe++;
                    if (_hazardHoverProbe >= HazardHoverMaxCollections)
                    {
                        _retryAfterMs[item.Seed] = now + StackedLootSkipMs;
                        ClearHazardHoverState(true, now);
                    }
                    return;
                }

                if (!cleanup && _hazardHoverHasRestorePoint)
                {
                    old = _hazardHoverRestorePoint;
                    restore = true;
                }
                ClearHazardHoverState(false, now);
            }
            else if (IsHazardousSelectedInteractable(selectedBeforeMove))
            {
                _hazardHoverSeed = item.Seed;
                _hazardHoverProbe = 1;
                _hazardHoverHasRestorePoint = restore;
                _hazardHoverRestorePoint = old;
                return;
            }

            CommitItemClick(item, tries, cleanup, stackedLoot, now, old, restore);
        }

        private bool HandleMaterialConfirmedRetry(IItem item, int tries, bool cleanup, bool stackedLoot, long now, NativePoint old, bool restore)
        {
            int tick = Hud.Game.CurrentGameTick;
            NativePoint cursor;
            if (_materialHoverSeed == item.Seed)
            {
                bool haveCursor = GetCursorPos(out cursor);
                double ownershipRadius = Math.Max(3.0d, 4.0d * UiScale());
                double dx = haveCursor ? cursor.X - _materialHoverX : double.MaxValue;
                double dy = haveCursor ? cursor.Y - _materialHoverY : double.MaxValue;
                bool cursorOwned = haveCursor && dx * dx + dy * dy <= ownershipRadius * ownershipRadius;

                // Native selection is the click authority. Check it before cursor
                // ownership so harmless projection/movement drift cannot discard a
                // confirmed material target between adjacent collection frames.
                if (haveCursor && IsExactItemSelected(item) && IsCursorNearMaterialBase(item, cursor) &&
                    IsSafeSyntheticWorldClick(cursor.X, cursor.Y))
                {
                    NativePoint restorePoint = _materialHoverRestorePoint;
                    bool restoreAfterClick = _materialHoverHasRestorePoint;
                    ClearMaterialHoverState(false);
                    CommitItemClick(item, tries, cleanup, stackedLoot, now, restorePoint, restoreAfterClick);
                    return true;
                }

                // A material label can share its zero-lift point with another item.
                // Confirm that overlap twice, then use the ordinary proven label lift;
                // the exact selected actor remains mandatory before any click.
                if (cursorOwned && _materialProbeIndex == 0 && tick > _materialProbeTick
                    && _materialLiftFallbackSeed != item.Seed && IsDifferentItemSelected(item))
                {
                    _materialProbeTick = tick;
                    if (_materialOverlapSeed == item.Seed)
                        _materialOverlapChecks++;
                    else
                    {
                        _materialOverlapSeed = item.Seed;
                        _materialOverlapChecks = 1;
                    }

                    if (_materialOverlapChecks >= 2)
                    {
                        _materialLiftFallbackSeed = item.Seed;
                        _materialProbeIndex = 1;
                        _materialProbeTick = tick;
                        _materialHoverExpireMs = now + CursorRestoreExpireMs;
                    }
                }

                // Never fight a manual cursor move or another plugin that has taken control.
                if (!cursorOwned)
                {
                    ClearMaterialHoverState(false);
                    _retryAfterMs[item.Seed] = now + StackedLootSkipMs;
                    _lastClickMs = now;
                    return true;
                }

                // Give native selection fresh ticks to acknowledge this point.
                // Exact selection above still commits immediately; only misses advance.
                if (tick < _materialProbeTick || tick - _materialProbeTick >= 2)
                {
                    _materialProbeIndex = (_materialProbeIndex + 1) % 8;
                    _materialProbeTick = tick;
                }

                if (now >= _materialHoverExpireMs)
                {
                    ClearMaterialHoverState(true);
                    _retryAfterMs[item.Seed] = now + StackedLootSkipMs;
                    _lastClickMs = now;
                    return true;
                }
            }
            else
            {
                ClearMaterialHoverState(true);
                _materialHoverSeed = item.Seed;
                if (_materialProbeSeed != item.Seed)
                {
                    _materialProbeSeed = item.Seed;
                    _materialProbeIndex = _materialLiftFallbackSeed == item.Seed ? 1 : 0;
                }
                _materialProbeTick = tick;
                _materialHoverExpireMs = now + CursorRestoreExpireMs;
                _materialHoverHasRestorePoint = restore;
                _materialHoverRestorePoint = old;
            }

            int x, y;
            GetMaterialHoverPoint(item, _materialProbeIndex, out x, out y);
            if (!IsSafeSyntheticWorldClick(x, y) || !TrySetCursorForWorldClick(x, y))
            {
                ClearMaterialHoverState(true);
                _retryAfterMs[item.Seed] = now + StackedLootSkipMs;
                _lastClickMs = now;
                return true;
            }

            _materialHoverX = x;
            _materialHoverY = y;
            return true;
        }

        private void GetMaterialHoverPoint(IItem item, int probe, out int x, out int y)
        {
            // Native zero lift and the proven ordinary lift precede gentle, existing
            // fallback geometry. These are hover probes, never unconfirmed clicks.
            if (probe < 2)
            {
                GetItemClickBase(item, probe == 0, out x, out y);
                return;
            }
            int phase = probe == 2 ? 2 : probe == 3 ? 1 : probe < 6 ? probe - 1 : probe;
            GetClickPoint(item, phase, true, out x, out y);
            int baseX, baseY;
            GetItemClickBase(item, true, out baseX, out baseY);
            float scale = UiScale();
            x = baseX + (int)Math.Round((x - baseX) * scale);
            y = baseY + (int)Math.Round((y - baseY) * scale);
        }

        private bool IsDifferentItemSelected(IItem item)
        {
            if (item == null) return false;
            try
            {
                IActor selected = GetSelectedActorSafe();
                return selected != null && selected.GizmoType == GizmoType.Item && selected.AnnId != item.AnnId;
            }
            catch { return false; }
        }

        private void ClearMaterialFallback(int seed)
        {
            if (_materialProbeSeed == seed)
                _materialProbeSeed = _materialProbeIndex = _materialProbeTick = 0;
            if (_materialLiftFallbackSeed == seed)
                _materialLiftFallbackSeed = 0;
            if (_materialOverlapSeed == seed)
            {
                _materialOverlapSeed = 0;
                _materialOverlapChecks = 0;
            }
        }

        private void CommitItemClick(IItem item, int tries, bool cleanup, bool stackedLoot, long now, NativePoint old, bool restore)
        {
            _attempts[item.Seed] = tries + 1;
            ClearUrshiArmedRecoveryState(true);
            ArmGenericUrshiPickupRecovery(item, cleanup, now);
            MouseLeftClick();
            if (!stackedLoot)
                _pickupAcknowledgeUntilMs[item.Seed] = now + PickupAcknowledgeMs;
            if (restore) ScheduleCursorRestore(old, now);
            _lastClickSeed = item.Seed;
            if (stackedLoot)
            {
                RememberStackedLootClick(item, now);
                _stackedLootSkipUntilMs[item.Seed] = now + StackedLootSkipMs;
            }
            _lastCleanupClickFar = cleanup && item.CentralXyDistanceToMe > _normalPickupRangeYards;
            _lastClickMs = now;
        }

        private void ClearMaterialHoverState(bool restoreCursor)
        {
            if (restoreCursor && _materialHoverHasRestorePoint)
            {
                NativePoint cursor;
                double ownershipRadius = Math.Max(3.0d, 4.0d * UiScale());
                if (GetCursorPos(out cursor))
                {
                    double dx = cursor.X - _materialHoverX;
                    double dy = cursor.Y - _materialHoverY;
                    if (dx * dx + dy * dy <= ownershipRadius * ownershipRadius)
                        SetCursorPos(_materialHoverRestorePoint.X, _materialHoverRestorePoint.Y);
                }
            }

            _materialHoverSeed = 0;
            _materialHoverX = 0;
            _materialHoverY = 0;
            _materialHoverExpireMs = 0;
            _materialHoverHasRestorePoint = false;
            _materialHoverRestorePoint = new NativePoint();
        }

        private void ClearHazardHoverState(bool restoreCursor, long now)
        {
            if (restoreCursor && _hazardHoverHasRestorePoint)
                ScheduleCursorRestore(_hazardHoverRestorePoint, now);

            _hazardHoverSeed = 0;
            _hazardHoverProbe = 0;
            _hazardHoverHasRestorePoint = false;
            _hazardHoverRestorePoint = new NativePoint();
        }

        private static bool IsHazardousSelectedInteractable(IActor actor)
        {
            if (actor == null)
                return false;

            try
            {
                switch (actor.GizmoType)
                {
                    case GizmoType.Chest:
                    case GizmoType.BreakableChest:
                    case GizmoType.LoreChest:
                    case GizmoType.Portal:
                    case GizmoType.TownPortal:
                    case GizmoType.HearthPortal:
                    case GizmoType.PortalDestination:
                    case GizmoType.PageOfFatePortal:
                    case GizmoType.SecretPortal:
                    case GizmoType.BossPortal:
                    case GizmoType.ReturnPointPortal:
                    case GizmoType.DungeonPortal:
                    case GizmoType.ReturnPortal:
                        return true;
                }
            }
            catch { }

            return false;
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
                int range = _postRiftCleanupStartedMs != 0
                    ? PostRiftLootRangeYards()
                    : _eventPickupRangeYards;

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen) continue;
                    if (IsExcludedPickup(item) || IsSuppressedDroppedItem(item, now) || IsCleanupStuckIgnored(item, now)) continue;
                    if (IsProtectedChestRisk(item, protectedChest) || item.CentralXyDistanceToMe > range) continue;
                    if (WantedPriority(item) < 0 || !CanFit(item, freeSlots) || !IsAutoUrshiHandoffLoot(item)) continue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool HandleUrshiRiskLootHoverClick(IItem item, int tries, bool stackedLoot, long now)
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
                    && IsSafeSyntheticWorldClick(hx, hy)
                    && TrySetCursorForWorldClick(hx, hy))
                {
                    _attempts[item.Seed] = tries + 1;

                    if (fallbackMode)
                        _urshiFallbackTriesBySeed[item.Seed] = fallbackTries + 1;

                    _nextUrshiRiskClickMs = now + UrshiRiskClickDelayMs;
                    ClearAccidentalUrshiRecoveryState();
                    _urshiArmedUntilMs = now + UrshiPanelRecoveryWindowMs;
                    _urshiArmedSeed = item.Seed;
                    _nextUrshiSpaceMs = 0;
                    _urshiSpaceAttempts = 0;
                    _urshiPortalCancelAttempts = 0;
                    CacheAccidentalUrshiClickPoint(urshi);

                    MouseLeftClick();

                    _lastClickSeed = item.Seed;
                    if (stackedLoot)
                    {
                        RememberStackedLootClick(item, now);
                        _stackedLootSkipUntilMs[item.Seed] = now + StackedLootSkipMs;
                    }
                    _lastCleanupClickFar = item.CentralXyDistanceToMe > _normalPickupRangeYards;
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

            if (!TrySetCursorForWorldClick(x, y))
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

        private void GetMaterialClickPoint(IItem item, int attempt, out int x, out int y)
        {
            // Telemetry shows the native zero-lift point becomes the exact selected
            // material actor one or two collection frames after cursor movement.
            // Keep every material attempt focused on that validated point.
            GetItemClickBase(item, true, out x, out y);
        }

        private bool IsCursorNearMaterialBase(IItem item, NativePoint point)
        {
            try
            {
                int baseX, baseY;
                GetItemClickBase(item, true, out baseX, out baseY);
                double dx = point.X - baseX;
                double dy = point.Y - baseY;
                double radius = 36.0d * UiScale();
                return dx * dx + dy * dy <= radius * radius;
            }
            catch { return false; }
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
            if (IsPlan(item) || IsWhisper(item)) return 2;
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
            if (item == null) return false;
            if (IsNoSpacePickup(item)) return true;
            if (item.AccountBound && !item.BoundToMyAccount) return false;
            if (HasMatchingStack(item)) return true;
            if (freeSlots <= 0) return false;
            if (item.SnoItem == null) return freeSlots > 0;

            int width = Math.Max(1, item.SnoItem.ItemWidth);
            int height = Math.Max(1, item.SnoItem.ItemHeight);
            return HasFreeInventoryFootprint(width, height);
        }

        private void UpdateInventoryFullAlert(List<LootCandidate> visibleCandidates, int freeSlots)
        {
            bool capacityBlocked = false;
            int blockedWidth = 1;
            int blockedHeight = 1;
            if (visibleCandidates != null)
            {
                for (int i = 0; i < visibleCandidates.Count; i++)
                {
                    LootCandidate candidate = visibleCandidates[i];
                    IItem item = candidate != null ? candidate.Item : null;
                    if (item == null || IsNoSpacePickup(item) || HasMatchingStack(item))
                        continue;
                    if (item.AccountBound && !item.BoundToMyAccount)
                        continue;
                    if (!CanFit(item, freeSlots))
                    {
                        capacityBlocked = true;
                        if (item.SnoItem != null)
                        {
                            blockedWidth = Math.Max(1, item.SnoItem.ItemWidth);
                            blockedHeight = Math.Max(1, item.SnoItem.ItemHeight);
                        }
                        break;
                    }
                }
            }

            int total = 0;
            try { total = Math.Max(0, Hud.Game.Me.InventorySpaceTotal); }
            catch { }
            int used = Math.Max(0, Math.Min(total, total - Math.Max(0, freeSlots)));

            if (_inventoryFullAlertActive)
            {
                bool capacityRecovered = total != _inventoryFullAlertTotal ||
                    HasFreeInventoryFootprint(Math.Max(1, _inventoryFullAlertWidth), Math.Max(1, _inventoryFullAlertHeight));
                if (!capacityRecovered)
                    return;

                _inventoryFullAlertActive = false;
                _inventoryFullAlertUsed = 0;
                _inventoryFullAlertTotal = 0;
                _inventoryFullAlertWidth = 0;
                _inventoryFullAlertHeight = 0;
            }

            if (!capacityBlocked)
                return;

            _inventoryFullAlertActive = true;
            _inventoryFullAlertUsed = used;
            _inventoryFullAlertTotal = total;
            _inventoryFullAlertWidth = blockedWidth;
            _inventoryFullAlertHeight = blockedHeight;

            try
            {
                s7o_TipsHelper tips = Hud.GetPlugin<s7o_TipsHelper>();
                if (tips != null && tips.Enabled)
                    tips.ShowInventoryFullAlert(used, total);
            }
            catch { }
        }

        private static bool IsPlan(IItem item)
        {
            return item != null && item.SnoActor != null && PlanActors.Contains(item.SnoActor.Sno);
        }

        private bool HasFreeInventoryFootprint(int width, int height)
        {
            try
            {
                IPlayer me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null || Hud.Inventory == null) return false;

                int total = me.InventorySpaceTotal;
                if (total <= 0) return false;

                const int columns = 10;
                int rows = (total + columns - 1) / columns;
                width = Math.Max(1, width);
                height = Math.Max(1, height);
                if (width > columns || height > rows) return false;

                var occupied = new bool[total];
                foreach (IItem inventoryItem in Hud.Inventory.ItemsInInventory)
                {
                    if (inventoryItem == null || inventoryItem.InventoryX < 0 || inventoryItem.InventoryY < 0)
                        continue;

                    int itemWidth = inventoryItem.SnoItem != null ? Math.Max(1, inventoryItem.SnoItem.ItemWidth) : 1;
                    int itemHeight = inventoryItem.SnoItem != null ? Math.Max(1, inventoryItem.SnoItem.ItemHeight) : 1;
                    for (int y = 0; y < itemHeight; y++)
                    {
                        for (int x = 0; x < itemWidth; x++)
                        {
                            int slot = (inventoryItem.InventoryY + y) * columns + inventoryItem.InventoryX + x;
                            if (slot >= 0 && slot < total)
                                occupied[slot] = true;
                        }
                    }
                }

                for (int y = 0; y <= rows - height; y++)
                {
                    for (int x = 0; x <= columns - width; x++)
                    {
                        bool fits = true;
                        for (int iy = 0; iy < height && fits; iy++)
                        {
                            for (int ix = 0; ix < width; ix++)
                            {
                                int slot = (y + iy) * columns + x + ix;
                                if (slot >= total || occupied[slot])
                                {
                                    fits = false;
                                    break;
                                }
                            }
                        }

                        if (fits) return true;
                    }
                }

                return false;
            }
            catch { return false; }
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

        private static bool IsLegendaryGem(IItem item)
        {
            return item != null && item.SnoItem != null &&
                string.Equals(item.SnoItem.MainGroupCode, "gems_unique", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUnownedLegendaryGem(IItem item)
        {
            if (!IsLegendaryGem(item))
                return false;

            uint sno = item.SnoItem.Sno;
            try
            {
                return !Hud.Game.Items.Any(owned =>
                    owned != null && owned.Location != ItemLocation.Floor &&
                    ContainsLegendaryGem(owned, sno));
            }
            catch { return true; }
        }

        private static bool ContainsLegendaryGem(IItem item, uint sno)
        {
            return (IsLegendaryGem(item) && item.SnoItem.Sno == sno) ||
                (item.ItemsInSocket != null && item.ItemsInSocket.Any(g => IsLegendaryGem(g) && g.SnoItem.Sno == sno));
        }

        private static bool IsBloodShard(IItem item)
        {
            return item != null && item.SnoActor != null && item.SnoActor.Sno == ActorSnoEnum._horadricrelic;
        }

        private static bool IsLegendaryLike(IItem item)
        {
            if (item == null) return false;
            if (item.IsLegendary || item.Quality == ItemQuality.Legendary) return true;
            if (item.SetSno != 0 && item.SetSno != uint.MaxValue) return true;
            return item.SnoItem != null && (item.SnoItem.LegendaryPower != null || (item.SnoItem.SetItemBonusesSno != 0 && item.SnoItem.SetItemBonusesSno != uint.MaxValue));
        }

        private static bool IsAutoUrshiPrimaryReward(IItem item)
        {
            if (item == null)
                return false;
            if (IsGem(item))
                return true;
            if (item.SnoItem == null || item.SnoItem.Kind != ItemKind.loot)
                return false;

            ActorSnoEnum actor = item.SnoActor != null ? item.SnoActor.Sno : 0;
            if (item.SnoItem.Sno == PetrifiedScreamSno || actor == ActorSnoEnum._swarmriftkey)
                return false;

            return IsLegendaryLike(item);
        }

        private static bool IsAutoUrshiCountedLegendaryReward(IItem item)
        {
            return item != null && !IsGem(item) && IsAutoUrshiPrimaryReward(item);
        }

        private bool IsProtectedAutoUrshiLoot(IItem item)
        {
            return (item != null && item.AncientRank >= 1 && IsLegendaryLike(item))
                || IsUnownedLegendaryGem(item);
        }

        private bool IsAutoUrshiHandoffLoot(IItem item)
        {
            return !_talkToUrshiAfterLoot
                || !_autoUrshiHandoffCommitted
                || (IsGem(item) && !_autoUrshiTalkDone)
                || IsProtectedAutoUrshiLoot(item);
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
            catch { return 0; }
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

        private bool IsLootBurstCleanup(long now)
        {
            if (HasUnopenedProtectedChestNearby(ProtectedChestBlockYards) || HasActiveVisionFight())
            {
                _lootBurstCleanupUntilMs = 0;
                return false;
            }

            if (HasNearbyAttackableMonster(LootBurstMonsterBlockYards))
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
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen || item.CentralXyDistanceToMe > _eventPickupRangeYards) continue;
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

        private void TrackProtectedRewardChestOpen(long now)
        {
            try
            {
                foreach (var actor in Hud.Game.Actors)
                {
                    if (actor == null || actor.SnoActor == null || actor.AnnId == 0)
                        continue;

                    ActorSnoEnum sno = actor.SnoActor.Sno;
                    if (sno != ActorSnoEnum._p76_chest && sno != ActorSnoEnum._p73_chestreward)
                        continue;

                    if (!actor.IsDisabled && !actor.IsOperated && (actor.IsClickable || actor.DisplayOnOverlay))
                    {
                        _unopenedProtectedRewardChestAnnIds.Add(actor.AnnId);
                        continue;
                    }

                    if ((actor.IsDisabled || actor.IsOperated) && _unopenedProtectedRewardChestAnnIds.Remove(actor.AnnId))
                        _lootBurstCleanupUntilMs = now + LootBurstLatchMs;
                }
            }
            catch { }
        }

        private void TrackNephalemRiftRewardWindow(long now)
        {
            try
            {
                bool nephalemRift = Hud.Game.SpecialArea == SpecialArea.Rift && GetUrshiActor() == null;
                if (!nephalemRift || Hud.Game.RiftPercentage < 100.0d)
                {
                    _nephalemRiftRewardPending = false;
                    _nephalemRiftRewardObserved = false;
                    _nephalemRiftRewardLootSeen = false;
                    _nephalemRiftRewardEmptySinceMs = 0;
                    _nephalemRiftRewardLastNewFloorItemMs = 0;
                    _nephalemRiftRewardSeenFloorSeeds.Clear();
                    return;
                }

                if (!_nephalemRiftRewardPending || _nephalemRiftRewardObserved)
                    return;

                // The native boss-killed event identifies the real guardian transition.
                // If the player is still channeling, this remains pending until the
                // ordinary combat gate allows collection again.
                if (HasNearbyAttackableMonster(LootBurstMonsterBlockYards))
                    return;

                _nephalemRiftRewardPending = false;
                _nephalemRiftRewardObserved = true;
                _nephalemRiftRewardLootSeen = false;
                _nephalemRiftRewardEmptySinceMs = 0;
                _nephalemRiftRewardLastNewFloorItemMs = now;
                _nephalemRiftRewardSeenFloorSeeds.Clear();
                _lootBurstCleanupUntilMs = Math.Max(_lootBurstCleanupUntilMs, now + LootBurstLatchMs);
            }
            catch { }
        }

        private void TrackNephalemRiftRewardFloorActivity(long now)
        {
            try
            {
                int range = PostRiftLootRangeYards();
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen ||
                        item.CentralXyDistanceToMe > range)
                        continue;

                    if (_nephalemRiftRewardSeenFloorSeeds.Add(item.Seed))
                        _nephalemRiftRewardLastNewFloorItemMs = now;
                }
            }
            catch { }
        }

        private void UpdateNephalemRiftRewardCompletion(int visibleEligibleCount, long now)
        {
            if (!_nephalemRiftRewardObserved || Hud.Game.SpecialArea != SpecialArea.Rift)
            {
                _nephalemRiftRewardEmptySinceMs = 0;
                return;
            }

            if (visibleEligibleCount > 0)
            {
                _nephalemRiftRewardLootSeen = true;
                _nephalemRiftRewardEmptySinceMs = 0;
                return;
            }

            if (!_nephalemRiftRewardLootSeen)
            {
                _nephalemRiftRewardEmptySinceMs = 0;
                return;
            }

            // Guardian items can arrive a few native frames after the first pile
            // clears. Require a short continuous empty state; this delays no pickup
            // and still restores ordinary range immediately after the settled batch.
            if (_nephalemRiftRewardEmptySinceMs == 0)
            {
                _nephalemRiftRewardEmptySinceMs = now;
                return;
            }

            if (now - _nephalemRiftRewardEmptySinceMs >= NephalemRiftRewardEmptyConfirmMs &&
                now - _nephalemRiftRewardLastNewFloorItemMs >= NephalemRiftRewardEmptyConfirmMs)
            {
                _lootBurstCleanupUntilMs = 0;
                _nephalemRiftRewardObserved = false;
                _nephalemRiftRewardLootSeen = false;
                _nephalemRiftRewardEmptySinceMs = 0;
                _nephalemRiftRewardLastNewFloorItemMs = 0;
                _nephalemRiftRewardSeenFloorSeeds.Clear();
            }
        }

        private bool IsAutoUrshiTalkActorClickable(IActor urshi)
        {
            try
            {
                if (!_talkToUrshiAfterLoot || (_autoUrshiProbeFallbackPending && (urshi == null || !urshi.IsSelected)) ||
                    urshi == null || !urshi.IsOnScreen || urshi.ScreenCoordinate == null ||
                    Hud == null || Hud.Window == null)
                    return false;

                // Use the actual UI-safe probe envelope, including zoomed-out edge
                // positions, rather than rejecting a whole strip of visible world.
                int x, y;
                return urshi.WorldId == Hud.Game.Me.WorldId &&
                    TryGetAutoUrshiTalkPoint(urshi, _autoUrshiTalkAttempts, out x, out y);
            }
            catch { return false; }
        }

        private bool TryReturnTowardAutoUrshi(long now)
        {
            if (_autoUrshiTalkDone || !_talkToUrshiAfterLoot || !_autoUrshiHasLastSeenWorld ||
                _autoUrshiActorPathActive || _autoUrshiApproachAborted)
                return false;

            var me = Hud.Game.Me;
            if (me == null || me.FloorCoordinate == null ||
                me.FloorCoordinate.XYDistanceTo(_autoUrshiLastSeenX, _autoUrshiLastSeenY) > AutoUrshiUnknownApproachMaxYards)
                return false;

            _autoUrshiReturning = true;

            if (_autoUrshiProbeFallbackPending && _autoUrshiReturnClicks > 0)
                return true;

            if (_autoUrshiReturnClicks >= AutoUrshiReturnMaxClicks)
            {
                AbortAutoUrshiApproach(now);
                return false;
            }

            if (now < _nextAutoUrshiReturnMs)
                return true;

            int x, y;
            if (_autoUrshiReturnProbeTick == 0)
            {
                if (!TryGetAutoUrshiReturnPoint(out x, out y) || !TrySetCursorForWorldClick(x, y))
                {
                    AbortAutoUrshiApproach(now);
                    return false;
                }
                _autoUrshiReturnProbeX = x;
                _autoUrshiReturnProbeY = y;
                _autoUrshiReturnProbeTick = Hud.Game.CurrentGameTick;
                _autoUrshiReturnProbeMs = now;
                return true;
            }
            if (Hud.Game.CurrentGameTick == _autoUrshiReturnProbeTick)
                return true;

            NativePoint cursor;
            x = _autoUrshiReturnProbeX;
            y = _autoUrshiReturnProbeY;
            _autoUrshiReturnProbeTick = 0;
            // Ground is only an approach fallback, never an item/portal/monster click.
            if (now - _autoUrshiReturnProbeMs > CursorRestoreExpireMs || !GetCursorPos(out cursor) ||
                Math.Abs(cursor.X - x) > 4 || Math.Abs(cursor.Y - y) > 4 ||
                GetSelectedActorSafe() != null || Hud.Game.SelectedMonster2 != null ||
                !TrySetCursorForWorldClick(x, y))
            {
                AbortAutoUrshiApproach(now);
                return false;
            }

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
                _autoUrshiProbeFallbackPending = false;
                _autoUrshiActorPathActive = true;
                _autoUrshiReturning = false;
                _autoUrshiReturnProbeTick = 0;
                // Preserve the outbound trail so geometry-blind native-path failures
                // can fall back to the known return route.
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
                {
                    SeedAutoUrshiApproachSample(now, me.FloorCoordinate.X, me.FloorCoordinate.Y);
                }
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

                if (_autoUrshiActorPathActive && _autoUrshiHasLastSeenWorld)
                {
                    float goalDx = x - _autoUrshiLastSeenX;
                    float goalDy = y - _autoUrshiLastSeenY;
                    float goalDistance = (float)Math.Sqrt(goalDx * goalDx + goalDy * goalDy);

                    if (!_autoUrshiApproachHasGoalDistance)
                    {
                        _autoUrshiApproachBestGoalDistance = goalDistance;
                        _autoUrshiApproachHasGoalDistance = true;
                    }
                    else if (goalDistance <= _autoUrshiApproachBestGoalDistance - AutoUrshiApproachProgressYards)
                    {
                        _autoUrshiApproachBestGoalDistance = goalDistance;
                        SeedAutoUrshiApproachSample(now, x, y);
                        return true;
                    }

                    // A valid navmesh route around a wall/ledge can initially move
                    // sideways or slightly away from Urshi. Count real player movement
                    // as progress too; only genuine immobility is a path stall.
                    float movedX = x - _autoUrshiApproachSampleX;
                    float movedY = y - _autoUrshiApproachSampleY;
                    if (movedX * movedX + movedY * movedY >=
                        AutoUrshiApproachProgressYards * AutoUrshiApproachProgressYards)
                    {
                        SeedAutoUrshiApproachSample(now, x, y);
                        return true;
                    }

                    if (now - _autoUrshiApproachSampleMs < AutoUrshiApproachStallMs)
                        return true;

                    // Do not cancel the game's actor path with a blind ground click.
                    // Genuine immobility yields control to the player.
                    AbortAutoUrshiApproach(now);
                    return false;
                }

                float dx = x - _autoUrshiApproachSampleX;
                float dy = y - _autoUrshiApproachSampleY;

                if (dx * dx + dy * dy >=
                    AutoUrshiApproachProgressYards * AutoUrshiApproachProgressYards)
                {
                    if (_autoUrshiProbeFallbackPending)
                    {
                        _autoUrshiProbeFallbackPending = false;
                        _autoUrshiReturning = false;
                        _autoUrshiReturnClicks = 0;
                        _nextAutoUrshiReturnMs = 0;
                        ResetAutoUrshiApproachSample();
                        ResetAutoUrshiTalkProbesAfterReturn();
                        return true;
                    }

                    _autoUrshiReturnClicks = 0;
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

            if (_autoUrshiActorPathActive && _autoUrshiHasLastSeenWorld)
            {
                float dx = x - _autoUrshiLastSeenX;
                float dy = y - _autoUrshiLastSeenY;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (!_autoUrshiApproachHasGoalDistance || distance < _autoUrshiApproachBestGoalDistance)
                    _autoUrshiApproachBestGoalDistance = distance;
                _autoUrshiApproachHasGoalDistance = true;
            }
        }

        private void ResetAutoUrshiApproachSample()
        {
            _autoUrshiApproachSampleMs = 0;
            _autoUrshiApproachSampleX = 0f;
            _autoUrshiApproachSampleY = 0f;
            _autoUrshiApproachBestGoalDistance = 0f;
            _autoUrshiApproachHasGoalDistance = false;
        }

        private void AbortAutoUrshiApproach(long now)
        {
            _autoUrshiApproachAborted = true;
            _autoUrshiReturnProbeTick = 0;
            _autoUrshiActorPathActive = false;
            _autoUrshiReturning = false;
            _autoUrshiProbeFallbackPending = false;
            _autoUrshiReturnTrail.Clear();
            _autoUrshiReturnClicks = 0;
            _nextAutoUrshiReturnMs = 0;
            ResetAutoUrshiApproachSample();
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            ResetAutoUrshiTalkReadyState();
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

                float goalDistance = me.FloorCoordinate.XYDistanceTo(_autoUrshiLastSeenX, _autoUrshiLastSeenY);
                int goalIndex = -1;
                float bestDistance = goalDistance - 0.1f;
                for (int i = 0; i < _autoUrshiReturnTrail.Count; i++)
                {
                    var point = _autoUrshiReturnTrail[i];
                    float dx = point.X - _autoUrshiLastSeenX;
                    float dy = point.Y - _autoUrshiLastSeenY;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        goalIndex = i;
                    }
                }
                if (goalIndex >= 0)
                {
                    while (_autoUrshiReturnTrail.Count > goalIndex)
                    {
                        int index = _autoUrshiReturnTrail.Count - 1;
                        var point = _autoUrshiReturnTrail[index];
                        if (me.FloorCoordinate.XYDistanceTo(point.X, point.Y) >= AutoUrshiReturnMinClickYards)
                        {
                            // Never skip an intermediate waypoint to cut across unseen geometry.
                            return TryProjectAutoUrshiGroundPoint(point.X, point.Y, point.Z, out x, out y);
                        }
                        _autoUrshiReturnTrail.RemoveAt(index);
                    }
                }

                if (goalDistance < AutoUrshiReturnMinClickYards || goalDistance > AutoUrshiUnknownApproachMaxYards)
                    return false;
                // No traversed segment leads closer. Take a short directional step;
                // the existing movement watchdog stops if terrain prevents progress.
                for (float step = Math.Min(18f, goalDistance); step >= AutoUrshiReturnMinClickYards; step -= 6f)
                {
                    float fraction = step / goalDistance;
                    float wx = me.FloorCoordinate.X + (_autoUrshiLastSeenX - me.FloorCoordinate.X) * fraction;
                    float wy = me.FloorCoordinate.Y + (_autoUrshiLastSeenY - me.FloorCoordinate.Y) * fraction;
                    if (TryProjectAutoUrshiGroundPoint(wx, wy, me.FloorCoordinate.Z, out x, out y))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryProjectAutoUrshiGroundPoint(float wx, float wy, float wz, out int x, out int y)
        {
            x = y = 0;
            var world = Hud.Window.CreateWorldCoordinate(wx, wy, wz);
            if (world == null || !world.IsValid) return false;
            var screen = world.ToScreenCoordinate(true, true);
            if (screen == null || float.IsNaN(screen.X) || float.IsNaN(screen.Y) ||
                float.IsInfinity(screen.X) || float.IsInfinity(screen.Y)) return false;
            x = (int)Math.Round(screen.X + Hud.Window.Offset.X);
            y = (int)Math.Round(screen.Y + Hud.Window.Offset.Y);
            return IsSafeSyntheticWorldClick(x, y);
        }

        private void ResetAutoUrshiTalkReadyState()
        {
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            _autoUrshiGemHandoffActive = false;
            ResetAutoUrshiGemHandoffWatch();
            ClearAutoUrshiTalkHover();
        }

        private bool IsAutoUrshiRewardBatchReady(long now)
        {
            if (!_talkToUrshiAfterLoot || _postRiftCleanupStartedMs == 0 ||
                _autoUrshiRewardGateStartedMs == 0)
                return false;

            long gateAge = now - _autoUrshiRewardGateStartedMs;

            if (HasLiveAutoUrshiPrimaryReward())
                return false;

            // The final legendary/set rewards can materialize after the earlier wave.
            // Once ten unique legendary/set rewards have actually been observed,
            // native floor state is enough to hand off immediately. The old settle
            // window remains only as a fallback for unusually small reward batches.
            return _autoUrshiObservedLegendaryRewardCount >= AutoUrshiLegendaryRewardMinObserved ||
                gateAge >= AutoUrshiRewardSettleMs;
        }

        private bool HasLiveAutoUrshiPrimaryReward()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Items == null)
                    return false;

                long now = Hud.Game.CurrentRealTimeMilliseconds;
                IActor protectedChest = GetUnopenedProtectedChest();
                int freeSlots = SafeFreeSlots();
                int range = PostRiftLootRangeYards();

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.Location != ItemLocation.Floor || !item.IsOnScreen)
                        continue;
                    if (!IsAutoUrshiPrimaryReward(item) || IsExcludedPickup(item) ||
                        IsSuppressedDroppedItem(item, now))
                        continue;
                    if (IsProtectedChestRisk(item, protectedChest) ||
                        item.CentralXyDistanceToMe > range ||
                        WantedPriority(item) < 0 || !CanFit(item, freeSlots))
                        continue;

                    if (!IsProtectedAutoUrshiLoot(item))
                    {
                        if (IsCleanupStuckIgnored(item, now))
                            continue;

                        long retryAt;
                        if (_retryAfterMs.TryGetValue(item.Seed, out retryAt) && now < retryAt)
                            continue;
                    }

                    return true;
                }
            }
            catch { }

            return false;
        }

        private int PostRiftLootRangeYards()
        {
            return Math.Max(_eventPickupRangeYards, PostRiftApproachRangeYards);
        }

        private bool TryCommitAutoUrshiHandoff(long now)
        {
            if (_autoUrshiHandoffCommitted)
                return true;

            if (!IsAutoUrshiRewardBatchReady(now))
                return false;

            _autoUrshiHandoffCommitted = true;
            StopDhStrafeForUrshiHandoff();
            return true;
        }

        private void ResetAutoUrshiGemHandoffWatch()
        {
            _autoUrshiHandoffPortalObserved = false;
            _autoUrshiHandoffTransformObserved = false;
        }

        private void ClearAutoUrshiTalkHover()
        {
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
        }

        private void ResetAutoUrshiTalkProbesAfterReturn()
        {
            _autoUrshiTalkAttempts = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _nextAutoUrshiTalkMs = 0;
            ClearAutoUrshiTalkHover();
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
            _autoUrshiReturnProbeTick = 0;
            _autoUrshiActorPathActive = false;
            _autoUrshiReturning = false;
            _autoUrshiProbeFallbackPending = false;
            ResetAutoUrshiApproachSample();
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiGemHandoffActive = false;
            ResetAutoUrshiGemHandoffWatch();
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

            if (!TryCommitAutoUrshiHandoff(now))
            {
                ResetAutoUrshiTalkReadyState();
                return false;
            }

            if (now < _autoUrshiTalkCooldownUntilMs || now < _nextAutoUrshiTalkMs)
                return false;

            if (_autoUrshiHoverClickAtMs != 0)
            {
                if (now < _autoUrshiHoverClickAtMs)
                    return true;

                if (urshi.IsSelected && IsSafeSyntheticWorldClick(_autoUrshiHoverX, _autoUrshiHoverY)
                    && TrySetCursorForWorldClick(_autoUrshiHoverX, _autoUrshiHoverY))
                {
                    ResetAutoUrshiGemHandoffWatch();
                    CacheAccidentalUrshiClickPoint(urshi);
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
                if (TryFallbackAutoUrshiTalkToBreadcrumb(now))
                    return true;

                _autoUrshiTalkCooldownUntilMs = now + AutoUrshiTalkRetryCooldownMs;
                RestoreAutoUrshiTalkCursor(now);
                return false;
            }

            return BeginAutoUrshiTalkHoverProbe(now, urshi);
        }

        private bool TryFallbackAutoUrshiTalkToBreadcrumb(long now)
        {
            if (_autoUrshiActorPathActive || _autoUrshiApproachAborted ||
                _autoUrshiReturnTrail.Count == 0)
                return false;

            ClearAutoUrshiTalkHover();
            _autoUrshiReturning = false;
            _autoUrshiReturnProbeTick = 0;
            _autoUrshiReturnClicks = 0;
            _nextAutoUrshiReturnMs = 0;
            _autoUrshiProbeFallbackPending = true;
            ResetAutoUrshiApproachSample();

            if (!TryReturnTowardAutoUrshi(now))
            {
                _autoUrshiProbeFallbackPending = false;
                return false;
            }

            RestoreAutoUrshiTalkCursor(now);
            return true;
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

            if (!TrySetCursorForWorldClick(x, y))
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
            for (int probe = 0; probe < 12; probe++)
            {
                float ox = 0f;
                float oy = 0f;
                int phase = ((attempt < 0 ? 0 : attempt) + probe) % 12;

                switch (phase)
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

                int candidateX = (int)Math.Round((double)urshi.ScreenCoordinate.X + (double)Hud.Window.Offset.X + (double)(ox * scale));
                int candidateY = (int)Math.Round((double)urshi.ScreenCoordinate.Y + (double)Hud.Window.Offset.Y + (double)(oy * scale));
                if (!IsSafeSyntheticWorldClick(candidateX, candidateY))
                    continue;

                x = candidateX;
                y = candidateY;
                return true;
            }

            return false;
        }

        private bool TryGetUiSafeItemClickPoint(IItem item, int startAttempt, bool cleanup, bool stacked, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (item == null || item.ScreenCoordinate == null) return false;

            bool material = IsNoSpaceMaterialPickup(item);
            int variants = material ? 1 : (stacked ? 12 : 8);
            for (int i = 0; i < variants; i++)
            {
                int phase = Math.Max(0, startAttempt) + i;
                int candidateX, candidateY;
                if (material)
                    GetMaterialClickPoint(item, phase, out candidateX, out candidateY);
                else if (stacked)
                    GetStackedLootClickPoint(item, phase, cleanup, out candidateX, out candidateY);
                else
                    GetClickPoint(item, phase, true, out candidateX, out candidateY);

                if (!IsSafeSyntheticWorldClick(candidateX, candidateY))
                    continue;

                x = candidateX;
                y = candidateY;
                return true;
            }

            if (TryGetFloorClickPoint(item, out x, out y) && IsSafeSyntheticWorldClick(x, y))
                return true;

            x = 0;
            y = 0;
            return false;
        }

        private bool TrySetCursorForWorldClick(int x, int y)
        {
            return IsSafeSyntheticWorldClick(x, y) && SetCursorPos(x, y);
        }

        private bool IsSafeSyntheticWorldClick(int screenX, int screenY)
        {
            try
            {
                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground) return false;
                if (!IsInsideGameWindow(screenX, screenY)) return false;

                float clientX = screenX - Hud.Window.Offset.X;
                float clientY = screenY - Hud.Window.Offset.Y;
                return !IsClickDangerUiClient(clientX, clientY);
            }
            catch { return true; } // UI safety is fail-open; never globally disable AutoLoot on API anomalies.
        }

        private bool IsClickDangerUiClient(float x, float y)
        {
            return IsInsideExplicitClickGuard(x, y) || IsInsidePlayerPortraitFace(x, y);
        }

        private bool IsInsideExplicitClickGuard(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null) return false;
                var size = Hud.Window.Size;
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

        private bool IsBlockingLootUiOpen()
        {
            if (IsChatEntryOpen()) return true;
            if (IsUiVisible(_skillPaneSkillsList)) return true;
            if (IsUiVisible(_vendorMainPage)) return true;
            if (IsUiVisible(_shopMainPanel)) return true;
            if (IsUiVisible(_scriptedSequenceDialog)) return true;

            try
            {
                if (Hud.Render.WorldMapUiElement != null && Hud.Render.WorldMapUiElement.Visible) return true;
                if (Hud.Inventory != null)
                {
                    if (IsUiVisible(Hud.Inventory.InventoryMainUiElement)) return true;
                    if (IsUiVisible(Hud.Inventory.StashMainUiElement)) return true;
                    if (IsUiVisible(Hud.Inventory.FollowerMainUiElement)) return true;
                }
            }
            catch { }

            return false;
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
                IActor urshi = GetUrshiActor();
                bool greaterRift = Hud.Game.SpecialArea == SpecialArea.GreaterRift;
                if (!greaterRift && urshi == null)
                {
                    _cleanupLatched = false;
                    return false;
                }

                if (HasNearbyAttackableMonster(CleanupMonsterBlockYards)) return false;
                if (_cleanupLatched) return true;

                if (Hud.Game.RiftPercentage < 100.0d && urshi == null) return false;

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

        private bool ShouldPauseForGoblinPack(long now)
        {
            int count = 0;
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster == null || !monster.IsAlive || monster.Illusion ||
                        monster.CentralXyDistanceToMe > GoblinPackBlockYards || !IsTreasureGoblin(monster))
                        continue;
                    if (++count >= GoblinPackMinCount) break;
                }
            }
            catch { return _goblinPackPaused; }

            if (!_goblinPackPaused)
            {
                if (count < GoblinPackMinCount) return false;
                _goblinPackPaused = true;
            }

            if (count > 0)
            {
                _goblinFreeSinceMs = 0;
                return true;
            }

            if (_goblinFreeSinceMs == 0)
                _goblinFreeSinceMs = now;
            if (now - _goblinFreeSinceMs < GoblinPackClearMs)
                return true;

            _goblinPackPaused = false;
            _goblinFreeSinceMs = 0;
            return false;
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
            _urshiPortalCancelAttempts = 0;
            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiHoverClickAtMs = 0;
            _autoUrshiHoverX = 0;
            _autoUrshiHoverY = 0;
            _autoUrshiTalkDone = false;
            _autoUrshiGemHandoffActive = false;
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
            return item == null || WantedPriority(item) >= 0;
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

            return HasVisibleEligibleLootBlockingUrshiTalk();
        }

        private bool IsIntentionalAutoUrshiInteraction(long now)
        {
            return _autoUrshiGemHandoffActive || HasRecentIntentionalUrshiTalk(now);
        }

        private void CompleteAutoUrshiGemHandoff()
        {
            ClearAccidentalUrshiRecoveryState();
            CacheAccidentalUrshiClickPoint(GetUrshiActor());
            _autoUrshiActorPathActive = false;
            _autoUrshiReturning = false;
            ResetAutoUrshiApproachSample();
            ClearAutoUrshiTalkHover();

            // The real gem pane now belongs to Auto Gem Upgrade. Drop every AutoLoot
            // recovery/cursor state that could send Space or move the cursor over it.
            _urshiArmedSeed = 0;
            _urshiArmedUntilMs = 0;
            _nextUrshiSpaceMs = 0;
            _urshiSpaceAttempts = 0;
            _urshiFallbackSeed = 0;
            _urshiFallbackUntilMs = 0;
            ClearUrshiRiskLootHover();
            ClearGenericUrshiRecoveryState();

            _nextAutoUrshiTalkMs = 0;
            _autoUrshiTalkCooldownUntilMs = 0;
            _autoUrshiTalkAttempts = 0;
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            _autoUrshiTalkDone = true;
            _autoUrshiHandoffCommitted = true;
            _autoUrshiGemHandoffActive = true;

            _autoUrshiHasRestorePoint = false;
            _autoUrshiRestorePoint = new NativePoint();
            _pendingCursorRestore = false;
            _pendingCursorRestoreAtMs = 0;
            _pendingCursorRestoreExpireMs = 0;
        }

        private bool ShouldRecoverUnsafeAutoUrshiGemHandoff(long now)
        {
            try
            {
                if (!TryCommitAutoUrshiHandoff(now))
                    return true;

                IPlayer me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null)
                    return HasVisibleEligibleLootBlockingUrshiTalk();

                AcdAnimationState state = me.AnimationState;
                if (state == AcdAnimationState.Transform)
                {
                    _autoUrshiHandoffTransformObserved = true;
                    return false;
                }

                if (state == AcdAnimationState.CastingPortal)
                    _autoUrshiHandoffPortalObserved = true;

                if (_autoUrshiHandoffTransformObserved)
                    return false;

                return HasVisibleEligibleLootBlockingUrshiTalk()
                    || (!_autoUrshiHandoffCommitted
                        && (state == AcdAnimationState.Running
                            || (_autoUrshiHandoffPortalObserved && state != AcdAnimationState.CastingPortal)));
            }
            catch
            {
                return false;
            }
        }

        private bool BeginUnsafeAutoUrshiHandoffRecovery(long now)
        {
            bool hadClickPoint = _accidentalUrshiHasClickPoint;
            int clickX = _accidentalUrshiClickX;
            int clickY = _accidentalUrshiClickY;

            ClearAccidentalUrshiRecoveryState();
            _autoUrshiUnsafeHandoffRecoveryActive = true;
            _accidentalUrshiRecoveryUntilMs = now + UrshiPanelRecoveryWindowMs + UrshiPortalFollowupMs;
            _nextUrshiSpaceMs = now;
            _urshiSpaceAttempts = 0;

            if (hadClickPoint)
            {
                _accidentalUrshiHasClickPoint = true;
                _accidentalUrshiClickX = clickX;
                _accidentalUrshiClickY = clickY;
            }
            CacheAccidentalUrshiClickPoint(GetUrshiActor());

            _autoUrshiTalkDone = false;
            _autoUrshiGemHandoffActive = false;
            _autoUrshiRecentTalkOpenedUntilMs = 0;
            _autoUrshiTalkLootCancelAttempts = 0;
            _nextAutoUrshiTalkLootCancelMs = 0;
            ResetAutoUrshiGemHandoffWatch();

            return HandlePendingAccidentalUrshiRecovery(now, Hud.Game.Me);
        }

        private void ClearAccidentalUrshiRecoveryState()
        {
            _accidentalUrshiRecoverySeed = 0;
            _accidentalUrshiRecoveryUntilMs = 0;
            _accidentalUrshiPortalWatchUntilMs = 0;
            _accidentalUrshiRecoveryArmed = false;
            _autoUrshiUnsafeHandoffRecoveryActive = false;
            _accidentalUrshiHasClickPoint = false;
            _accidentalUrshiClickX = 0;
            _accidentalUrshiClickY = 0;
            _urshiPortalCancelAttempts = 0;
        }

        private void CacheAccidentalUrshiClickPoint(IActor urshi)
        {
            int x, y;
            if (urshi == null || !urshi.IsOnScreen || urshi.ScreenCoordinate == null
                || !TryGetAutoUrshiTalkPoint(urshi, 0, out x, out y))
                return;

            _accidentalUrshiClickX = x;
            _accidentalUrshiClickY = y;
            _accidentalUrshiHasClickPoint = true;
        }

        private bool TryGetAccidentalUrshiClickPoint(int attempt, out int x, out int y)
        {
            IActor urshi = GetUrshiActor();
            if (urshi == null)
            {
                IActor selectedActor = GetSelectedActorSafe();
                if (IsUrshiActor(selectedActor))
                    urshi = selectedActor;
            }

            if (urshi != null && urshi.IsOnScreen && urshi.ScreenCoordinate != null
                && TryGetAutoUrshiTalkPoint(urshi, attempt, out x, out y))
            {
                _accidentalUrshiClickX = x;
                _accidentalUrshiClickY = y;
                _accidentalUrshiHasClickPoint = true;
                return true;
            }

            x = _accidentalUrshiClickX;
            y = _accidentalUrshiClickY;
            return _accidentalUrshiHasClickPoint && IsSafeSyntheticWorldClick(x, y);
        }

        private void BeginAccidentalUrshiRecovery(int seed, bool armed, long now)
        {
            if (_accidentalUrshiRecoverySeed == seed)
            {
                CacheAccidentalUrshiClickPoint(GetUrshiActor());
                return;
            }

            bool hasCachedPoint = _accidentalUrshiHasClickPoint;
            int cachedX = _accidentalUrshiClickX;
            int cachedY = _accidentalUrshiClickY;
            ClearAccidentalUrshiRecoveryState();
            _accidentalUrshiRecoverySeed = seed;
            _accidentalUrshiRecoveryUntilMs = now + UrshiPanelRecoveryWindowMs + UrshiPortalFollowupMs;
            _accidentalUrshiRecoveryArmed = armed;
            _nextUrshiSpaceMs = now + UrshiPortalArbitrationMs;
            _urshiSpaceAttempts = 0;
            if (hasCachedPoint)
            {
                _accidentalUrshiHasClickPoint = true;
                _accidentalUrshiClickX = cachedX;
                _accidentalUrshiClickY = cachedY;
            }
            CacheAccidentalUrshiClickPoint(GetUrshiActor());

            if (armed)
            {
                HandleArmedUrshiMisclick(now);
            }
            else
            {
                HandleGenericUrshiPickupMisclick(now);
            }
        }

        private void FinishAccidentalUrshiRecovery(long now)
        {
            int seed = _accidentalUrshiRecoverySeed;
            bool armed = _accidentalUrshiRecoveryArmed;
            ClearAccidentalUrshiRecoveryState();

            if (armed && _urshiArmedSeed == seed)
                ClearUrshiArmedRecoveryState(false);
            if (!armed && _genericUrshiRecoverySeed == seed)
                ClearGenericUrshiRecoveryState();

            MarkUrshiPanelCloseForFastLootResume(now);
        }

        private bool HandlePendingAccidentalUrshiRecovery(long now, IPlayer me)
        {
            if (_accidentalUrshiRecoverySeed == 0 && !_autoUrshiUnsafeHandoffRecoveryActive)
                return false;

            if (now > _accidentalUrshiRecoveryUntilMs)
            {
                FinishAccidentalUrshiRecovery(now);
                return false;
            }

            IItem item = _accidentalUrshiRecoverySeed != 0
                ? FindVisibleFloorItemBySeed(_accidentalUrshiRecoverySeed)
                : null;
            if (item != null && WantedPriority(item) < 0)
            {
                FinishAccidentalUrshiRecovery(now);
                return false;
            }

            if (me != null && me.AnimationState == AcdAnimationState.CastingPortal)
                return HandleAccidentalUrshiPortalCancel(now);

            bool recoveryUiVisible = IsUrshiRecoveryUiVisible();
            if (recoveryUiVisible)
            {
                CacheAccidentalUrshiClickPoint(GetUrshiActor());

                if (now < _nextUrshiSpaceMs || !CanSendUrshiRecoverySpace())
                    return true;

                if (_urshiSpaceAttempts >= UrshiSpaceMaxAttempts)
                    return true;

                SendSpace();
                _urshiSpaceAttempts++;
                _nextUrshiSpaceMs = now + UrshiSpaceRetryMs;
                _accidentalUrshiPortalWatchUntilMs = now + UrshiPortalFollowupMs;
                MarkUrshiPanelCloseForFastLootResume(now);
                return true;
            }

            if (_urshiPortalCancelAttempts > 0)
            {
                FinishAccidentalUrshiRecovery(now);
                return false;
            }

            if (_accidentalUrshiPortalWatchUntilMs != 0
                && now <= _accidentalUrshiPortalWatchUntilMs)
                return true;

            FinishAccidentalUrshiRecovery(now);
            return false;
        }

        private bool HandleAccidentalUrshiPortalCancel(long now)
        {
            IPlayer me = Hud.Game.Me;
            if (me == null || me.AnimationState != AcdAnimationState.CastingPortal)
                return false;

            if (_urshiPortalCancelAttempts >= UrshiPortalCancelMaxAttempts
                || IsChatEntryOpen()
                || IsStashUiOpen())
                return true;

            if (now < _nextUrshiSpaceMs)
                return true;

            int x, y;
            if (!TryGetAccidentalUrshiClickPoint(_urshiPortalCancelAttempts, out x, out y)
                || !TrySetCursorForWorldClick(x, y))
            {
                _nextUrshiSpaceMs = now + UrshiPortalCancelRetryMs;
                return true;
            }

            _pendingCursorRestore = false;
            MouseLeftClick();
            _urshiPortalCancelAttempts++;
            _accidentalUrshiPortalWatchUntilMs = 0;
            _nextUrshiSpaceMs = now + UrshiPortalCancelRetryMs;
            _lastClickMs = now;
            return true;
        }

        private bool HandleAutoLootUrshiRecovery(long now)
        {
            bool gemPaneVisible = IsUrshiGemPaneVisible();
            bool conversationVisible = IsUrshiConversationVisible();
            bool recoveryUiVisible = gemPaneVisible || conversationVisible;
            bool intentionalAutoTalk = IsIntentionalAutoUrshiInteraction(now);

            if (recoveryUiVisible)
            {
                _autoUrshiActorPathActive = false;
                _autoUrshiReturning = false;
                ResetAutoUrshiApproachSample();
                BeginAutoUrshiRewardBatch(_postRiftCleanupStartedMs != 0, now, true);

                // Acknowledge the intentional actor click here, before the normal
                // candidate pipeline can schedule another Urshi click during the
                // conversation-to-gem-pane transition.
                if (intentionalAutoTalk)
                    _autoUrshiTalkDone = true;
            }

            if (!recoveryUiVisible)
            {
                if (_autoUrshiGemHandoffActive)
                {
                    _autoUrshiGemHandoffActive = false;
                    ResetAutoUrshiGemHandoffWatch();
                    ClearAccidentalUrshiRecoveryState();
                    return false;
                }

                // Auto Gem advances the conversation before the real pane appears.
                // Keep AutoLoot inert during that short gap unless it intentionally
                // cancelled the conversation because genuinely new loot appeared.
                if (_autoUrshiTalkDone
                    && _autoUrshiTalkLootCancelAttempts == 0
                    && HasRecentIntentionalUrshiTalk(now))
                {
                    return true;
                }

                if (_urshiArmedSeed != 0 && now > _urshiArmedUntilMs)
                    ClearUrshiArmedRecoveryState(false);

                if (_genericUrshiRecoverySeed != 0 && now > _genericUrshiRecoveryUntilMs)
                    ClearGenericUrshiRecoveryState();

                _urshiPortalCancelAttempts = 0;
                return false;
            }

            if (gemPaneVisible && _autoUrshiRewardGateStartedMs != 0 && !intentionalAutoTalk)
            {
                // A real user-opened pane is authoritative when no eligible loot or known
                // synthetic pickup recovery remains. The fixed auto-handoff settle timer alone
                // must never make AutoLoot close Urshi on the player.
                if (!HasPendingArmedUrshiLoot(now)
                    && !HasPendingGenericUrshiPickupRecovery(now)
                    && !HasVisibleEligibleLootBlockingUrshiTalk())
                {
                    CompleteAutoUrshiGemHandoff();
                    return true;
                }

                if (ShouldRecoverUnsafeAutoUrshiGemHandoff(now))
                    return BeginUnsafeAutoUrshiHandoffRecovery(now);

                CompleteAutoUrshiGemHandoff();
                return true;
            }

            if (intentionalAutoTalk)
            {
                // Keep a clean pane under Auto Gem control. If loot remains, movement
                // resumes, or the portal drops before transform, re-enter the existing
                // guarded Urshi recovery instead of consuming the handoff permanently.
                if (gemPaneVisible)
                {
                    if (ShouldRecoverUnsafeAutoUrshiGemHandoff(now))
                        return BeginUnsafeAutoUrshiHandoffRecovery(now);

                    if (!_autoUrshiGemHandoffActive)
                        CompleteAutoUrshiGemHandoff();
                    return true;
                }

                if (HandleAutoUrshiTalkInterruptedByNewLoot(now))
                    return true;

                return true;
            }

            if (HasPendingArmedUrshiLoot(now))
            {
                BeginAccidentalUrshiRecovery(_urshiArmedSeed, true, now);
                return HandlePendingAccidentalUrshiRecovery(now, Hud.Game.Me);
            }

            if (_urshiArmedSeed != 0
                && (now > _urshiArmedUntilMs || FindVisibleFloorItemBySeed(_urshiArmedSeed) == null))
                ClearUrshiArmedRecoveryState(true);

            if (HasPendingGenericUrshiPickupRecovery(now))
            {
                BeginAccidentalUrshiRecovery(_genericUrshiRecoverySeed, false, now);
                return HandlePendingAccidentalUrshiRecovery(now, Hud.Game.Me);
            }

            ClearGenericUrshiRecoveryState();
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

            if (closes >= UrshiProblemItemMisclickLimit)
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

            if (closes >= UrshiProblemItemMisclickLimit)
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

            for (int probe = 0; probe < 12; probe++)
            {
                int phase = ((attempt < 0 ? 0 : attempt) + probe) % 12;
                int candidateX = baseX;
                int candidateY = baseY;

                switch (phase)
                {
                    case 0: break;
                    case 1: candidateY = baseY - stepSmall; break;
                    case 2: candidateY = baseY + stepSmall; break;
                    case 3: candidateX = baseX - stepSmall; break;
                    case 4: candidateX = baseX + stepSmall; break;
                    case 5: candidateX = baseX - stepMed; candidateY = baseY - stepSmall; break;
                    case 6: candidateX = baseX + stepMed; candidateY = baseY - stepSmall; break;
                    case 7: candidateX = baseX - stepMed; candidateY = baseY + stepSmall; break;
                    case 8: candidateX = baseX + stepMed; candidateY = baseY + stepSmall; break;
                    case 9: candidateY = baseY - stepMed; break;
                    case 10: candidateX = baseX - stepWide; break;
                    default: candidateX = baseX + stepWide; break;
                }

                if (!IsSafeSyntheticWorldClick(candidateX, candidateY))
                    continue;

                x = candidateX;
                y = candidateY;
                return true;
            }

            return false;
        }

        private IActor GetSelectedActorSafe()
        {
            try
            {
                return Hud != null && Hud.Game != null ? Hud.Game.SelectedActor : null;
            }
            catch { return null; }
        }

        private bool IsExactItemSelected(IItem item)
        {
            if (item == null) return false;
            try
            {
                IActor selectedActor = GetSelectedActorSafe();
                if (selectedActor != null)
                    return selectedActor.GizmoType == GizmoType.Item &&
                        selectedActor.AnnId == item.AnnId;

                return item.IsSelected;
            }
            catch { return false; }
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

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
