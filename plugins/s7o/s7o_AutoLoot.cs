using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Compact FREEHUD auto-loot picker. Pickup cadence and inventory-space checks are adapted from EzGo's PickItemPlugin.
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
        private const int MovementSampleMs = 90;
        private const float MovementThresholdYards = 0.22f;
        private const int MaxAttempts = 8;
        private const int StuckRetryCooldownMs = 6000;
        private const int ProtectedChestBlockYards = 45;
        private const int ProtectedChestRiskYards = 16;
        private const int VisionFightBlockYards = 70;
        private const int UrshiRiskYards = 16;
        private const int CleanupMonsterBlockYards = 45;
        private const int UrshiPanelRecoveryWindowMs = 2200;
        private const int UrshiPanelConfirmDelayMs = 120;
        private const int UrshiRiskClickDelayMs = 300;
        private const int UrshiRiskMaxCycleAttempts = 4;
        private const int UrshiRiskRetryCooldownMs = 20000;
        private const int UrshiSpaceRetryMs = 250;
        private const int UrshiSpaceMaxAttempts = 2;
        private const int UrshiFallbackRetryDelayMs = 350;
        private const int UrshiFallbackWindowMs = 3500;
        private const int UrshiFallbackMaxTries = 1;
        private const int UrshiScreenAvoidPx = 44;
        private const float UrshiLabelYOffsetPx1080 = 32f;
        private const float UrshiLabelHalfWidthMinPx1080 = 48f;
        private const float UrshiLabelHalfWidthMaxPx1080 = 180f;
        private const float UrshiLabelHalfWidthBasePx1080 = 14f;
        private const float UrshiLabelHalfWidthPerCharPx1080 = 4.6f;
        private const int UrshiFallbackWindowMarginPx = 6;
        private const int DroppedItemIgnoreMs = 20000;
        private const int DroppedItemVisibilityGraceMs = 500;
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
        private readonly Dictionary<int, int> _urshiMisclicksBySeed = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _urshiFallbackTriesBySeed = new Dictionary<int, int>();
        private IUiElement _urshiGemPane;
        private IUiElement _urshiConversationMain;
        private IUiElement _chatEditLine;
        private long _lastClickMs;
        private long _urshiArmedUntilMs;
        private long _nextUrshiRiskClickMs;
        private long _nextUrshiSpaceMs;
        private int _lastClickSeed;
        private int _urshiArmedSeed;
        private int _urshiSpaceAttempts;
        private int _urshiFallbackSeed;
        private long _urshiFallbackUntilMs;
        private int _urshiRecoveryOpenSeed;
        private long _urshiRecoveryOpenSeenMs;
        private bool _urshiRecoveryMisclickRecorded;
        private bool _cleanupLatched;
        private bool _lastCleanupClickFar;
        private bool _enabled;
        private bool _paused;
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
            if (!keepDroppedSuppress)
                _droppedSuppress.Clear();
            _lastClickSeed = 0;
            _lastCleanupClickFar = false;
            _cleanupLatched = false;
            _lootBurstCleanupUntilMs = 0;
            _lastMovementSampleMs = 0;
            _playerMoving = false;
            _pendingCursorRestore = false;
            _urshiArmedUntilMs = 0;
            _nextUrshiRiskClickMs = 0;
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
        }

        public void OnItemPicked(IItem item)
        {
            if (item == null) return;
            _attempts.Remove(item.Seed);
            _retryAfterMs.Remove(item.Seed);
            if (_lastClickSeed == item.Seed) _lastClickSeed = 0;
            _droppedSuppress.Remove(item.Seed);
            _urshiMisclicksBySeed.Remove(item.Seed);
            _urshiFallbackTriesBySeed.Remove(item.Seed);
            if (_urshiFallbackSeed == item.Seed)
            {
                _urshiFallbackSeed = 0;
                _urshiFallbackUntilMs = 0;
            }
            if (_urshiArmedSeed == item.Seed)
                ClearUrshiArmedRecoveryState(true);
        }

        public void OnItemLocationChanged(IItem item, ItemLocation from, ItemLocation to)
        {
            if (item == null) return;
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
            PurgeRetryState(now);
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

            int delay = postRiftCleanup
                ? (_lastCleanupClickFar ? CleanupFarMoveDelayMs : CleanupDelayMs)
                : (lootBurstCleanup ? (_lastCleanupClickFar ? SpecialCleanupFarMoveDelayMs : SpecialCleanupDelayMs) : NormalDelayMs);
            if (now - _lastClickMs < delay)
                return;

            int range = postRiftCleanup ? int.MaxValue : (lootBurstCleanup ? SpecialCleanupRange : ((state == AcdAnimationState.Running || (combatAction && playerMoving)) ? RunRange : IdleRange));
            int freeSlots = SafeFreeSlots();
            IActor urshi = GetUrshiActor();

            var target = Hud.Game.Items
                .Where(i => i != null && i.Location == ItemLocation.Floor && i.IsOnScreen && !IsExcludedPickup(i) && !IsSuppressedDroppedItem(i, now) && !IsProtectedChestRisk(i, protectedChest) && i.CentralXyDistanceToMe <= range)
                .Select(i => new LootCandidate(i, WantedPriority(i), IsUrshiRisk(i, urshi)))
                .Where(c => c.Priority >= 0 && CanFit(c.Item, freeSlots) && CanTry(c.Item, c.UrshiRisk, now))
                .OrderBy(c => c.UrshiRisk ? 1 : 0)
                .ThenBy(c => c.Item.Seed == _lastClickSeed ? 1 : 0)
                .ThenBy(c => wideCleanup ? 0 : c.Priority)
                .ThenBy(c => c.Item.CentralXyDistanceToMe)
                .ThenBy(c => c.Priority)
                .FirstOrDefault();

            if (target == null)
                return;

            ClickItem(target.Item, target.UrshiRisk && urshi != null, wideCleanup, now);
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
            _retryAfterMs[item.Seed] = now + StuckRetryCooldownMs;
            return false;
        }

        private void ClickItem(IItem item, bool riskyUrshi, bool cleanup, long now)
        {
            NativePoint old = new NativePoint();
            bool restore = !cleanup && GetCursorPos(out old);
            int tries = 0;
            _attempts.TryGetValue(item.Seed, out tries);

            int x, y;
            bool fallbackMode = riskyUrshi && item.Seed == _urshiFallbackSeed && now <= _urshiFallbackUntilMs;

            if (fallbackMode)
            {
                int fallbackTries;
                _urshiFallbackTriesBySeed.TryGetValue(item.Seed, out fallbackTries);

                IActor urshi = GetUrshiActor();
                if (!TryGetUrshiSafeFallbackClickPoint(item, urshi, fallbackTries, out x, out y))
                {
                    _retryAfterMs[item.Seed] = now + UrshiRiskRetryCooldownMs;
                    _nextUrshiRiskClickMs = now + UrshiRiskRetryCooldownMs;
                    _urshiFallbackSeed = 0;
                    _urshiFallbackUntilMs = 0;
                    _lastClickMs = now;
                    return;
                }

                _urshiFallbackTriesBySeed[item.Seed] = fallbackTries + 1;
            }
            else
            {
                GetClickPoint(item, tries, cleanup || riskyUrshi, out x, out y);
                if (riskyUrshi)
                    OffsetAwayFromUrshi(ref x, ref y, tries);
            }

            if (!SetCursorPos(x, y))
            {
                _lastClickMs = now;
                return;
            }

            _attempts[item.Seed] = tries + 1;
            if (riskyUrshi)
            {
                _nextUrshiRiskClickMs = now + UrshiRiskClickDelayMs;
                _urshiArmedUntilMs = now + UrshiPanelRecoveryWindowMs;
                _urshiArmedSeed = item.Seed;
                _nextUrshiSpaceMs = 0;
                _urshiSpaceAttempts = 0;
            }
            else
            {
                ClearUrshiArmedRecoveryState(true);
            }

            MouseLeftClick();
            if (restore) ScheduleCursorRestore(old, now);
            _lastClickSeed = item.Seed;
            _lastCleanupClickFar = cleanup && item.CentralXyDistanceToMe > IdleRange;
            _lastClickMs = now;
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

        private void GetClickPoint(IItem item, int attempt, bool allowAlternate, out int x, out int y)
        {
            int baseX = (int)Math.Round((double)item.ScreenCoordinate.X + (double)Hud.Window.Offset.X);
            int baseY = (int)Math.Round(item.ScreenCoordinate.Y - Hud.Window.Size.Height / 55f + Hud.Window.Offset.Y);
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
            _urshiRecoveryOpenSeed = 0;
            _urshiRecoveryOpenSeenMs = 0;
            _urshiRecoveryMisclickRecorded = false;

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

        private bool ShouldRecoverAutoLootUrshiMisclick(long now)
        {
            return _urshiArmedSeed != 0 && now <= _urshiArmedUntilMs;
        }

        private bool HandleAutoLootUrshiRecovery(long now)
        {
            if (!IsUrshiRecoveryUiVisible())
            {
                bool recoveryAttempted = _urshiSpaceAttempts > 0 || _nextUrshiSpaceMs != 0;

                if (recoveryAttempted || (_urshiArmedSeed != 0 && now > _urshiArmedUntilMs))
                    ClearUrshiArmedRecoveryState(false);

                _urshiRecoveryOpenSeed = 0;
                _urshiRecoveryOpenSeenMs = 0;
                _urshiRecoveryMisclickRecorded = false;
                return false;
            }

            // Urshi UI is visible. Do not click floor loot behind it. If this is not a
            // proven AutoLoot misclick on pending risky loot, leave the panel alone.
            if (!HasPendingArmedUrshiLoot(now))
            {
                if (_urshiArmedSeed != 0 && (now > _urshiArmedUntilMs || FindVisibleFloorItemBySeed(_urshiArmedSeed) == null))
                    ClearUrshiArmedRecoveryState(true);

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
            _lastClickMs = now;
            return true;
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

            if (closes >= 2)
            {
                _retryAfterMs[seed] = now + UrshiRiskRetryCooldownMs;
                _nextUrshiRiskClickMs = now + UrshiRiskRetryCooldownMs;
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

        private string GetItemLabelText(IItem item)
        {
            try
            {
                if (item == null)
                    return string.Empty;

                if (!string.IsNullOrEmpty(item.FullNameEnglish))
                    return item.FullNameEnglish;

                if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameEnglish))
                    return item.SnoItem.NameEnglish;
            }
            catch { }

            return string.Empty;
        }

        private float EstimateItemLabelHalfWidthPx(IItem item)
        {
            float scale = UiScale();
            string name = GetItemLabelText(item);
            int len = !string.IsNullOrEmpty(name) ? name.Length : 14;

            float half = UrshiLabelHalfWidthBasePx1080 + len * UrshiLabelHalfWidthPerCharPx1080;
            if (half < UrshiLabelHalfWidthMinPx1080)
                half = UrshiLabelHalfWidthMinPx1080;
            if (half > UrshiLabelHalfWidthMaxPx1080)
                half = UrshiLabelHalfWidthMaxPx1080;

            return half * scale;
        }

        private bool IsUsableScreenPoint(int x, int y)
        {
            try
            {
                if (Hud == null || Hud.Window == null)
                    return false;

                int left = (int)Math.Round((double)Hud.Window.Offset.X) + UrshiFallbackWindowMarginPx;
                int top = (int)Math.Round((double)Hud.Window.Offset.Y) + UrshiFallbackWindowMarginPx;
                int right = left + (int)Math.Round((double)Hud.Window.Size.Width) - UrshiFallbackWindowMarginPx * 2;
                int bottom = top + (int)Math.Round((double)Hud.Window.Size.Height) - UrshiFallbackWindowMarginPx * 2;

                return x >= left && x <= right && y >= top && y <= bottom;
            }
            catch { return false; }
        }

        private bool TryGetUrshiSafeFallbackClickPoint(IItem item, IActor urshi, int attempt, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (item == null || urshi == null || item.ScreenCoordinate == null || urshi.ScreenCoordinate == null)
                return false;

            float scale = UiScale();

            // Use the floor-label/icon screen proxy, but aim higher into the visible item text label band.
            int labelCenterX = (int)Math.Round((double)item.ScreenCoordinate.X + (double)Hud.Window.Offset.X);
            int labelCenterY = (int)Math.Round((double)item.ScreenCoordinate.Y - (double)(UrshiLabelYOffsetPx1080 * scale) + (double)Hud.Window.Offset.Y);

            float halfWidth = EstimateItemLabelHalfWidthPx(item);
            float ux = urshi.ScreenCoordinate.X + Hud.Window.Offset.X;
            float uy = urshi.ScreenCoordinate.Y + Hud.Window.Offset.Y;
            int awaySign = labelCenterX >= ux ? 1 : -1;

            float[] xOffsets =
            {
                awaySign * halfWidth * 0.55f,
                awaySign * halfWidth * 0.35f,
                0f,
                -awaySign * halfWidth * 0.25f
            };

            float[] yOffsets =
            {
                0f,
                -4f * scale,
                5f * scale
            };

            float bestScore = float.MinValue;
            int bestX = 0;
            int bestY = 0;
            bool found = false;

            for (int yi = 0; yi < yOffsets.Length; yi++)
            {
                for (int xi = 0; xi < xOffsets.Length; xi++)
                {
                    int cx = (int)Math.Round((double)labelCenterX + (double)xOffsets[xi]);
                    int cy = (int)Math.Round((double)labelCenterY + (double)yOffsets[yi]);

                    if (!IsUsableScreenPoint(cx, cy))
                        continue;

                    float dx = cx - ux;
                    float dy = cy - uy;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < UrshiScreenAvoidPx * UrshiScreenAvoidPx)
                        continue;

                    // Prefer farther from Urshi, while favoring earlier label-band candidates.
                    float score = distSq - yi * 300f - xi * 40f;
                    if (!found || score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestX = cx;
                        bestY = cy;
                    }
                }
            }

            if (!found)
                return false;

            x = bestX;
            y = bestY;
            return true;
        }

        private void OffsetAwayFromUrshi(ref int x, ref int y, int attempt)
        {
            var urshi = GetUrshiActor();
            if (urshi == null || urshi.ScreenCoordinate == null) return;
            float dx = x - (urshi.ScreenCoordinate.X + Hud.Window.Offset.X);
            float dy = y - (urshi.ScreenCoordinate.Y + Hud.Window.Offset.Y);
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f) { dx = 1f; dy = 0f; len = 1f; }

            int step = attempt % 4;
            float away = step < 2 ? 18f : 28f;
            float side = step == 1 ? 18f : (step == 3 ? -18f : 0f);
            x += (int)(dx / len * away + -dy / len * side);
            y += (int)(dy / len * (away * 0.55f) + dx / len * side - (step == 2 ? 6f : 0f));
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
