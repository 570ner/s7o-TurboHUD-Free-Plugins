namespace Turbo.Plugins.s7o
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using SharpDX.Direct2D1;
    using SharpDX.DirectInput;
    using Vector2 = SharpDX.Vector2;
    using Turbo.Plugins.Default;

    // ============================================================
    // s7o_InventoryMOD.cs  —  TurboHUD Free 1.4.3.0
    //
    // Lightweight storage management and inventory utility plugin.
    //
    // Current features:
    //   • Adds an extra material row to the inventory panel.
    //   • Adds a stash-open helper panel with a configurable hotkey,
    //     item-type toggles, and preferred stash-tab selection.
    //   • Stores enabled item groups in predictable passes:
    //       stackables first, then primal/ancient equipment.
    //   • Stackables include gems, Petrified Screams, and account-bound
    //     Ramaladni's Gifts.
    //   • Preferred item tab 0 = Auto. Manual tabs are capped to the
    //     detected stash tab count when possible.
    // ============================================================

    public class s7o_InventoryAdjustments : BasePlugin,
        IInGameTopPainter, IKeyEventHandler, IMouseClickHandler, IAfterCollectHandler
    {
        // ── Material row settings ──────────────────────────────────────────

        public int ExistingMaterialRowCount { get; set; } = 3;

        // ── Storage helper settings ────────────────────────────────────────

        public bool ShowStoragePanel { get; set; } = true;
        public Key StoreItemsHotkey { get; set; } = Key.F3;

        public bool StoreStackables { get; set; } = true;
        public bool StorePrimals { get; set; } = true;
        public bool StoreAncients { get; set; } = false;

        // 0 = Auto. 1-13 = manual starting tab, capped at runtime to
        // detected unlocked tabs when FreeHUD exposes enough UI/data.
        public int PreferredItemTab { get; set; } = 0;

        public int StoreItemsUiSettleDelayMs { get; set; } = 160;
        public int StoreItemsTabSwitchDelayMs { get; set; } = 100;
        public int StoreItemsClickDelayMs { get; set; } = 15;

        // Default D3 inventory key. Exposed so users who rebound inventory can edit it.
        public ushort InventoryVirtualKey { get; set; } = 0x49; // I

        // ── Fonts / brushes ────────────────────────────────────────────────

        public IFont CountFont { get; set; }
        public IFont YellowFont { get; set; }
        public IFont SmallFont { get; set; }
        public IFont ButtonFont { get; set; }
        public IFont ToggleFont { get; set; }
        public IBrush PanelBackBrush { get; set; }
        public IBrush PanelBorderBrush { get; set; }
        public IBrush PillDarkBrush { get; set; }
        public IBrush PillLightBrush { get; set; }
        public IBrush PillGreenBrush { get; set; }
        public IBrush PillGreenLightBrush { get; set; }
        public IBrush PillOrangeBorderBrush { get; set; }
        public IBrush PillOrangeSeparatorBrush { get; set; }
        public IBrush ToggleOnBrush { get; set; }
        public IBrush ToggleOnTopBrush { get; set; }
        public IBrush ToggleOnBorderBrush { get; set; }
        public IBrush ToggleOffBrush { get; set; }
        public IBrush ToggleOffBorderBrush { get; set; }

        // ── Layout settings ────────────────────────────────────────────────

        public float PanelLeftOffset { get; set; } = 0.0f;
        public float PanelTopOffset { get; set; } = 26.0f;
        public float PanelRightClearance { get; set; } = 40.0f;
        public float PanelWidth { get; set; } = 382.0f;
        public float PanelHeight { get; set; } = 86.0f;
        public float HotkeyButtonWidth { get; set; } = 44.0f;
        public float SmallButtonHeight { get; set; } = 20.0f;
        public float TabControlWidth { get; set; } = 134.0f;

        // ── Constants ──────────────────────────────────────────────────────

        private const int MaxD3StashTabs = 13;
        private const int StashColumns = 7;
        private const int StashRowsPerTab = 10;
        private const uint PetrifiedScreamSno = 1051857800;
        private const uint RamaladniGiftSno = 1844495708;
        private const ushort VkEscape = 0x1B;
        private const int StorageSettingsVersion = 3;

        // ── Private fields ────────────────────────────────────────────────

        private ISnoItem[] _infernalRow;
        private ITexture _rowBackground;
        private IKeyEvent _storeKeyEvent;
        private bool _capturingHotkey;
        private IUiElement _chatEditLine;

        private RectangleF _hotkeyButtonRect;
        private RectangleF _stackablesRect;
        private RectangleF _primalsRect;
        private RectangleF _ancientsRect;
        private RectangleF _tabMinusRect;
        private RectangleF _tabValueRect;
        private RectangleF _tabPlusRect;
        private RectangleF _tabControlRect;
        private float _headerPressX;
        private float _headerPressY;
        private float _headerTailX;
        private float _tabLabelX;
        private float _tabLabelY;
        private float _statusY;

        private bool _running;
        private StoreStage _stage = StoreStage.Idle;
        private int _nextActionTick;
        private int _currentQueueIndex;
        private const int MaxStoreStateStepsPerCollect = 256;
        private int _lastTabActionTick;
        private int _storedCount;
        private int _skippedCount;
        private string _lastStatus = string.Empty;
        private int _statusUntilTick;
        private int _settingsVersion;
        private readonly List<StoreCandidate> _queue = new List<StoreCandidate>();
        private bool _geometryDrawFailed;

        private string _settingsPath;

        // ── Init ──────────────────────────────────────────────────────────

        public s7o_InventoryAdjustments()
        {
            Enabled = true;
            Order = 30200;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings", "s7o_InventoryMOD.ini");
            LoadSettings();
            ApplySettingsMigration();
            ClampSettings();

            CountFont = Hud.Render.CreateFont("tahoma", 8f, 255, 255, 255, 255, false, false, false);
            YellowFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 255, 220, 64, true, false, 255, 0, 0, 0, true);
            SmallFont = Hud.Render.CreateFont("tahoma", 7.6f, 255, 235, 235, 235, false, false, 255, 0, 0, 0, true);
            ButtonFont = Hud.Render.CreateFont("tahoma", 7.6f, 255, 255, 255, 255, true, false, 255, 0, 0, 0, true);
            ToggleFont = Hud.Render.CreateFont("tahoma", 7.8f, 255, 245, 245, 245, false, false, 255, 0, 0, 0, true);

            PanelBackBrush = Hud.Render.CreateBrush(190, 0, 0, 0, 0);
            PanelBorderBrush = Hud.Render.CreateBrush(210, 180, 120, 30, 1.2f);
            PillDarkBrush = Hud.Render.CreateBrush(230, 18, 18, 18, 0);
            PillLightBrush = Hud.Render.CreateBrush(80, 255, 255, 255, 0);
            PillGreenBrush = Hud.Render.CreateBrush(220, 24, 105, 38, 0);
            PillGreenLightBrush = Hud.Render.CreateBrush(75, 150, 255, 150, 0);
            PillOrangeBorderBrush = Hud.Render.CreateBrush(230, 245, 150, 30, 1.2f);
            PillOrangeSeparatorBrush = Hud.Render.CreateBrush(220, 245, 150, 30, 1.0f);
            ToggleOnBrush = Hud.Render.CreateBrush(245, 0, 190, 35, 0);
            ToggleOnTopBrush = Hud.Render.CreateBrush(150, 175, 255, 90, 0);
            ToggleOnBorderBrush = Hud.Render.CreateBrush(255, 150, 255, 80, 1.25f);
            ToggleOffBrush = Hud.Render.CreateBrush(220, 14, 14, 14, 0);
            ToggleOffBorderBrush = Hud.Render.CreateBrush(220, 220, 145, 25, 1.0f);

            _infernalRow = new ISnoItem[]
            {
                SafeGetItem(3336787100), // Heart of Fright
                SafeGetItem(2029265596), // Vial of Putridness
                SafeGetItem(2670343450), // Idol of Terror
                SafeGetItem(1102953247), // Leoric's Regret
                SafeGetItem(198281388),  // Primordial Ashes
            };

            try { _rowBackground = Hud.Texture.GetTexture("inventory_materials"); }
            catch { _rowBackground = null; }

            _storeKeyEvent = Hud.Input.CreateKeyEvent(true, StoreItemsHotkey, false, false, false);
            _chatEditLine = RegisterUi("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null);
        }

        // ── Input handlers ─────────────────────────────────────────────────

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed)
                return;

            if (_capturingHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHotkey = false;
                    ShowStatus("hotkey unchanged", 900);
                    return;
                }

                StoreItemsHotkey = keyEvent.Key;
                _storeKeyEvent = Hud.Input.CreateKeyEvent(true, StoreItemsHotkey, false, false, false);
                _capturingHotkey = false;
                SaveSettings();
                ShowStatus("hotkey: " + StoreItemsHotkey, 1200);
                return;
            }

            if (_storeKeyEvent == null || !_storeKeyEvent.Matches(keyEvent))
                return;

            // If stash is closed, do nothing and show nothing.
            if (!IsStashVisible())
                return;

            if (_running)
            {
                StopRun("cancelled");
                return;
            }

            BeginStoreRun();
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left)
                return false;

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return false;

            if (!ShowStoragePanel || !IsStashVisible())
                return false;

            if (!UpdateStoragePanelLayout())
                return false;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;

            if (PointInRect(_hotkeyButtonRect, x, y))
            {
                if (_running)
                {
                    ShowStatus("running", 900);
                    return true;
                }

                _capturingHotkey = true;
                ShowStatus("press a key", 1500);
                return true;
            }

            if (_running)
            {
                if (PointInRect(_stackablesRect, x, y) || PointInRect(_primalsRect, x, y) || PointInRect(_ancientsRect, x, y)
                    || PointInRect(_tabMinusRect, x, y) || PointInRect(_tabPlusRect, x, y) || PointInRect(_tabValueRect, x, y))
                {
                    ShowStatus("running", 900);
                    return true;
                }

                return false;
            }

            if (PointInRect(_stackablesRect, x, y))
            {
                StoreStackables = !StoreStackables;
                SaveSettings();
                return true;
            }

            if (PointInRect(_primalsRect, x, y))
            {
                StorePrimals = !StorePrimals;
                SaveSettings();
                return true;
            }

            if (PointInRect(_ancientsRect, x, y))
            {
                StoreAncients = !StoreAncients;
                SaveSettings();
                return true;
            }

            if (PointInRect(_tabMinusRect, x, y))
            {
                PreferredItemTab = Math.Max(0, PreferredItemTab - 1);
                SaveSettings();
                return true;
            }

            if (PointInRect(_tabPlusRect, x, y))
            {
                int max = GetPreferredTabUiMax();
                PreferredItemTab = Math.Min(max, PreferredItemTab + 1);
                SaveSettings();
                return true;
            }

            return false;
        }

        public bool MouseUp(MouseButtons button)
        {
            return false;
        }

        // ── Runtime state machine ──────────────────────────────────────────

        public void AfterCollect()
        {
            if (!Enabled)
                return;

            if (_capturingHotkey && (!IsStashVisible() || Hud == null || Hud.Window == null || !Hud.Window.IsForeground))
                _capturingHotkey = false;

            if (!_running)
                return;

            AdvanceStoreRun(Environment.TickCount);
        }

        private void BeginStoreRun()
        {
            if (!StoreStackables && !StorePrimals && !StoreAncients)
            {
                ShowStatus("nothing selected", 1200);
                return;
            }

            _queue.Clear();
            _currentQueueIndex = 0;
            _storedCount = 0;
            _skippedCount = 0;
            _running = true;
            _stage = StoreStage.PrepareUi;
            _nextActionTick = Environment.TickCount;
            ShowStatus("storing items...", 1200);
        }

        private void AdvanceStoreRun(int now)
        {
            for (int step = 0; step < MaxStoreStateStepsPerCollect; step++)
            {
                if (!TickReached(now, _nextActionTick))
                    return;

                if (!IsStashVisible())
                {
                    StopRun("stash closed");
                    return;
                }

                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                    return;

                if (_stage != StoreStage.Done)
                {
                    if (IsChatEntryOpen())
                    {
                        s7o_InventoryModInput.TapKey(VkEscape);
                        Delay(now, StoreItemsUiSettleDelayMs);
                        return;
                    }

                    if (!IsInventoryVisible())
                    {
                        s7o_InventoryModInput.TapKey(InventoryVirtualKey);
                        Delay(now, StoreItemsUiSettleDelayMs);
                        return;
                    }
                }

                switch (_stage)
                {
                    case StoreStage.PrepareUi:
                        _stage = StoreStage.BuildQueue;
                        continue;

                    case StoreStage.BuildQueue:
                        BuildStoreQueue();
                        if (_queue.Count <= 0)
                        {
                            StopRun(_skippedCount > 0 ? "nothing to store" : "no items");
                            return;
                        }
                        _stage = StoreStage.SelectTab;
                        continue;

                    case StoreStage.SelectTab:
                        if (_currentQueueIndex >= _queue.Count)
                        {
                            _stage = StoreStage.Done;
                            continue;
                        }

                        if (EnsureTargetStashTab(_queue[_currentQueueIndex].TargetTabAbs, now))
                        {
                            _stage = StoreStage.ClickItem;
                            continue;
                        }
                        return;

                    case StoreStage.ClickItem:
                        if (_currentQueueIndex >= _queue.Count)
                        {
                            _stage = StoreStage.Done;
                            continue;
                        }

                        ClickCurrentCandidate(now);
                        continue;

                    case StoreStage.WaitAfterClick:
                        _currentQueueIndex++;
                        _stage = StoreStage.SelectTab;
                        continue;

                    case StoreStage.Done:
                        StopRun(_storedCount > 0 ? ("stored " + _storedCount.ToString(CultureInfo.InvariantCulture) + " item(s)") : "done");
                        return;
                }
            }
        }

        private void StopRun(string status)
        {
            _running = false;
            _stage = StoreStage.Idle;
            _nextActionTick = 0;
            _currentQueueIndex = 0;
            _queue.Clear();
            ShowStatus(status, 1800);
        }

        private void Delay(int now, int ms)
        {
            _nextActionTick = now + Math.Max(0, ms);
        }

        private static bool TickReached(int now, int tick)
        {
            return (int)(now - tick) >= 0;
        }

        // ── Candidate building ─────────────────────────────────────────────

        private void BuildStoreQueue()
        {
            _queue.Clear();
            _skippedCount = 0;

            var inv = SafeInventoryItems();
            if (inv.Count <= 0)
                return;

            var sorted = inv.OrderBy(i => i.InventoryY).ThenBy(i => i.InventoryX).ToList();
            var plan = BuildStashPlan();
            int preferredStartTabAbs = PreferredItemTab <= 0 ? 0 : PreferredItemTab - 1;

            // Pass 1: stackables stay together and are stored before equipment.
            // This avoids alternating gems/materials with ancient/primal items
            // just because of current inventory slot order.
            if (StoreStackables)
            {
                foreach (var item in sorted)
                {
                    if (!IsStoreStackable(item))
                        continue;

                    int targetTabAbs = ResolveStackableTargetTab(item, plan);
                    TryQueueCandidate(item, StoreKind.Stackable, targetTabAbs);
                }
            }

            // Pass 2: primal/ancient equipment can share the same placement pass
            // because both use the preferred item tab / Auto free-slot path.
            if (StorePrimals || StoreAncients)
            {
                foreach (var item in sorted)
                {
                    StoreKind kind;
                    if (!TryClassifyEquipmentCandidate(item, out kind))
                        continue;

                    int targetTabAbs = FindAndReserveEquipmentPlacementTab(plan, item, preferredStartTabAbs);
                    TryQueueCandidate(item, kind, targetTabAbs);
                }
            }
        }

        private void TryQueueCandidate(IItem item, StoreKind kind, int targetTabAbs)
        {
            if (targetTabAbs < 0)
            {
                _skippedCount++;
                return;
            }

            _queue.Add(new StoreCandidate
            {
                ItemKey = item.ItemUniqueId ?? string.Empty,
                Sno = item.SnoItem != null ? item.SnoItem.Sno : 0,
                TargetTabAbs = targetTabAbs,
                Kind = kind
            });
        }

        private bool TryClassifyEquipmentCandidate(IItem item, out StoreKind kind)
        {
            kind = StoreKind.None;
            if (item == null || item.SnoItem == null) return false;
            if (item.Location != ItemLocation.Inventory) return false;
            if (item.IsInventoryLocked) return false;

            if (item.SnoItem.Kind == ItemKind.loot && item.IsLegendary)
            {
                if (StorePrimals && item.AncientRank == 2)
                {
                    kind = StoreKind.Primal;
                    return true;
                }

                if (StoreAncients && item.AncientRank == 1)
                {
                    kind = StoreKind.Ancient;
                    return true;
                }
            }

            return false;
        }

        private bool TryClassifyCandidate(IItem item, out StoreKind kind)
        {
            kind = StoreKind.None;
            if (item == null || item.SnoItem == null) return false;
            if (item.Location != ItemLocation.Inventory) return false;
            if (item.IsInventoryLocked) return false;

            if (StoreStackables && IsStoreStackable(item))
            {
                kind = StoreKind.Stackable;
                return true;
            }

            return TryClassifyEquipmentCandidate(item, out kind);
        }

        private bool IsStoreStackable(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            uint sno = item.SnoItem.Sno;

            if (sno == PetrifiedScreamSno)
                return true;

            if (sno == RamaladniGiftSno)
                return !item.Unidentified && item.BoundToMyAccount;

            return IsNormalGemItem(item);
        }

        private static bool IsNormalGemItem(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            if (item.SnoItem.Kind != ItemKind.gem)
                return false;

            string main = item.SnoItem.MainGroupCode ?? string.Empty;
            return !EqualsText(main, "gems_unique");
        }

        private int FindMatchingStackTab(IItem item, StashPlan plan)
        {
            if (item == null || item.SnoItem == null || plan == null)
                return -1;

            uint sno = item.SnoItem.Sno;
            List<IItem> matches;
            if (!plan.StacksBySno.TryGetValue(sno, out matches) || matches == null || matches.Count <= 0)
                return -1;

            foreach (var stashItem in matches)
            {
                if (stashItem == null || stashItem.SnoItem == null)
                    continue;

                if (sno == RamaladniGiftSno && !stashItem.BoundToMyAccount)
                    continue;

                int tab = GetAbsoluteTab(stashItem);
                if (tab >= 0 && tab < plan.MaxTabs)
                    return tab;
            }

            return -1;
        }

        private int ResolveStackableTargetTab(IItem item, StashPlan plan)
        {
            if (item == null || plan == null)
                return -1;

            // Exact stack always wins, regardless of the preferred item tab.
            int exactStackTab = FindMatchingStackTab(item, plan);
            if (exactStackTab >= 0)
                return exactStackTab;

            // New gem/material stacks should stay with the existing stackable area when possible.
            int relatedStackableTab = FindAndReserveRelatedStackableTab(item, plan);
            if (relatedStackableTab >= 0)
                return relatedStackableTab;

            // Stackables do not use PreferredItemTab unless the matching stack is
            // already there. If no related stackable tab has room, use Auto.
            return plan.FindAndReserveFreeTab(item, 0, true);
        }

        private int FindAndReserveRelatedStackableTab(IItem item, StashPlan plan)
        {
            if (item == null || plan == null)
                return -1;

            var tabs = IsNormalGemItem(item) ? plan.NormalGemTabs : plan.StackableTabs;
            if (tabs == null || tabs.Count <= 0)
                return -1;

            foreach (int tab in tabs.OrderBy(t => t))
            {
                if (plan.TryReserveInTab(item, tab))
                    return tab;
            }

            return -1;
        }

        private StashPlan BuildStashPlan()
        {
            int maxTabs = GetAutomationMaxTabs();
            var plan = new StashPlan(maxTabs);

            var stashItems = SafeStashItems();
            foreach (var item in stashItems)
            {
                if (item == null || item.SnoItem == null)
                    continue;

                int tab = GetAbsoluteTab(item);
                if (tab < 0 || tab >= maxTabs)
                    continue;

                plan.MarkOccupied(item, tab);

                if (IsStashStackableIndexItem(item))
                    plan.AddStack(item);
            }

            return plan;
        }

        private static bool IsStashStackableIndexItem(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            if (item.SnoItem.Sno == PetrifiedScreamSno || item.SnoItem.Sno == RamaladniGiftSno)
                return true;

            return IsNormalGemItem(item);
        }

        private int FindAndReserveEquipmentPlacementTab(StashPlan plan, IItem item, int preferredStartTabAbs)
        {
            if (plan == null || item == null)
                return -1;

            preferredStartTabAbs = Math.Max(0, Math.Min(plan.MaxTabs - 1, preferredStartTabAbs));

            int tab = plan.FindAndReserveFreeTab(item, preferredStartTabAbs, false);
            if (tab >= 0)
                return tab;

            // Manual preferred tab is only a starting preference for equipment.
            // If it is full, fall back to Auto placement so the run can still finish.
            if (PreferredItemTab > 0)
                return plan.FindAndReserveFreeTab(item, 0, true);

            return -1;
        }

        // ── Click/deposit execution ────────────────────────────────────────

        private void ClickCurrentCandidate(int now)
        {
            if (_currentQueueIndex < 0 || _currentQueueIndex >= _queue.Count)
            {
                _stage = StoreStage.Done;
                return;
            }

            var candidate = _queue[_currentQueueIndex];
            var item = FindInventoryItemByCandidate(candidate);
            if (item == null)
            {
                _skippedCount++;
                _stage = StoreStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            RectangleF rect;
            try { rect = Hud.Inventory.GetItemRect(item); }
            catch { rect = RectangleF.Empty; }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                _skippedCount++;
                _stage = StoreStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            if (s7o_InventoryModInput.RightClickRect(rect))
            {
                _storedCount++;
                _stage = StoreStage.WaitAfterClick;
                Delay(now, StoreItemsClickDelayMs);
                ShowStatus("stored " + _storedCount.ToString(CultureInfo.InvariantCulture), 900);
            }
            else
            {
                _skippedCount++;
                _stage = StoreStage.WaitAfterClick;
                Delay(now, StoreItemsClickDelayMs);
            }
        }

        private IItem FindInventoryItemByCandidate(StoreCandidate candidate)
        {
            var items = SafeInventoryItems();
            foreach (var item in items)
            {
                if (item == null || item.SnoItem == null)
                    continue;

                if (!string.IsNullOrEmpty(candidate.ItemKey)
                    && string.Equals(item.ItemUniqueId, candidate.ItemKey, StringComparison.Ordinal))
                    return item;

                if (candidate.Sno != 0 && item.SnoItem.Sno == candidate.Sno && TryClassMatches(candidate.Kind, item))
                    return item;
            }

            return null;
        }

        private bool TryClassMatches(StoreKind kind, IItem item)
        {
            StoreKind current;
            return TryClassifyCandidate(item, out current) && current == kind;
        }

        private bool EnsureTargetStashTab(int targetTabAbs, int now)
        {
            targetTabAbs = Math.Max(0, Math.Min(MaxD3StashTabs - 1, targetTabAbs));

            int perPage = GetTabsPerPage();
            int wantedPage = targetTabAbs / perPage;
            int wantedTab = targetTabAbs % perPage;

            int selectedPage = SafeSelectedStashPageIndex();
            int selectedTab = SafeSelectedStashTabIndex();
            int selectedAbs = selectedPage * perPage + selectedTab;

            if (selectedAbs == targetTabAbs)
                return true;

            if ((uint)(now - _lastTabActionTick) < (uint)Math.Max(25, StoreItemsTabSwitchDelayMs / 2))
                return false;

            if (selectedPage != wantedPage)
            {
                var pageElement = GetStashPageElement(wantedPage);
                if (!ClickElement(pageElement))
                {
                    SkipCurrentCandidate(now);
                    return false;
                }

                _lastTabActionTick = now;
                Delay(now, StoreItemsTabSwitchDelayMs);
                return false;
            }

            if (selectedTab != wantedTab)
            {
                var tabElement = GetStashTabElement(wantedTab);
                if (!ClickElement(tabElement))
                {
                    SkipCurrentCandidate(now);
                    return false;
                }

                _lastTabActionTick = now;
                Delay(now, StoreItemsTabSwitchDelayMs);
                return false;
            }

            return false;
        }

        private void SkipCurrentCandidate(int now)
        {
            _skippedCount++;
            _currentQueueIndex++;
            _stage = StoreStage.SelectTab;
            Delay(now, 0);
        }

        private bool ClickElement(IUiElement element)
        {
            if (!IsVisible(element))
                return false;

            RectangleF rect;
            try { rect = element.Rectangle; }
            catch { return false; }

            if (rect.Width <= 0 || rect.Height <= 0)
                return false;

            return s7o_InventoryModInput.LeftClickRect(rect);
        }

        // ── IInGameTopPainter ─────────────────────────────────────────────

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.Inventory) return;
            if (Hud.Game == null || Hud.Game.Me == null) return;

            DrawMaterialRow();

            if (ShowStoragePanel && IsStashVisible())
                DrawStoragePanel();
        }

        private void DrawMaterialRow()
        {
            try
            {
                if (Hud.Game.Me.CurrentLevelNormalCap != 70) return;
            }
            catch { }

            var inv = Hud.Inventory.InventoryMainUiElement;
            if (inv == null || !inv.Visible) return;

            try
            {
                float rowH = _rowBackground != null
                    ? _rowBackground.Height * inv.Rectangle.Width / _rowBackground.Width
                    : inv.Rectangle.Height * 0.043f;

                float y = inv.Rectangle.Top + (inv.Rectangle.Height * 0.825f)
                        + ExistingMaterialRowCount * rowH;

                DrawItemRow(_infernalRow, inv.Rectangle.Left, y, inv.Rectangle.Width, rowH);
            }
            catch { }
        }

        private void DrawStoragePanel()
        {
            if (!UpdateStoragePanelLayout())
                return;

            var panel = GetStoragePanelRect();
            if (PanelBackBrush != null) PanelBackBrush.DrawRectangle(panel);
            if (PanelBorderBrush != null) PanelBorderBrush.DrawRectangle(panel);

            DrawText(YellowFont, "Press", _headerPressX, _headerPressY);
            DrawPillButton(_hotkeyButtonRect, _capturingHotkey ? "..." : StoreItemsHotkey.ToString(), _capturingHotkey);
            DrawText(YellowFont, "to store items", _headerTailX, _headerPressY);

            DrawToggleButton(_stackablesRect, "Stackables", StoreStackables);
            DrawToggleButton(_primalsRect, "Primals", StorePrimals);
            DrawToggleButton(_ancientsRect, "Ancients", StoreAncients);

            DrawText(SmallFont, "Preferred item tab:", _tabLabelX, _tabLabelY);
            DrawSegmentedTabControl();

            string statusText = null;
            if (!string.IsNullOrEmpty(_lastStatus) && !TickReached(Environment.TickCount, _statusUntilTick))
                statusText = _lastStatus;
            else if (_running)
                statusText = "storing items...";

            if (!string.IsNullOrEmpty(statusText))
                DrawCenteredText(SmallFont, new RectangleF(panel.X + 12.0f, _statusY, panel.Width - 24.0f, SmallButtonHeight), statusText);
        }

        private bool UpdateStoragePanelLayout()
        {
            _hotkeyButtonRect = RectangleF.Empty;
            _stackablesRect = RectangleF.Empty;
            _primalsRect = RectangleF.Empty;
            _ancientsRect = RectangleF.Empty;
            _tabMinusRect = RectangleF.Empty;
            _tabValueRect = RectangleF.Empty;
            _tabPlusRect = RectangleF.Empty;
            _tabControlRect = RectangleF.Empty;
            _headerPressX = _headerPressY = _headerTailX = 0.0f;
            _tabLabelX = _tabLabelY = _statusY = 0.0f;

            if (!IsStashVisible())
                return false;

            int maxTab = GetPreferredTabUiMax();
            if (PreferredItemTab > maxTab)
                PreferredItemTab = maxTab;

            var panel = GetStoragePanelRect();
            if (panel.Width <= 0 || panel.Height <= 0)
                return false;

            float centerX = panel.X + panel.Width * 0.5f;

            // Header row: measured and centered so the F3 pill cannot overlap text.
            _headerPressY = panel.Y + 5.0f;
            const float headerGap = 9.0f;
            string hotkeyText = _capturingHotkey ? "..." : StoreItemsHotkey.ToString();
            float pressW = MeasureTextWidth(YellowFont, "Press");
            float tailW = MeasureTextWidth(YellowFont, "to store items");
            float hotkeyW = Math.Max(HotkeyButtonWidth, MeasureTextWidth(ButtonFont, hotkeyText) + 18.0f);
            float headerTotalW = pressW + headerGap + hotkeyW + headerGap + tailW;
            _headerPressX = centerX - headerTotalW * 0.5f;
            _hotkeyButtonRect = new RectangleF(_headerPressX + pressW + headerGap, _headerPressY - 2.0f, hotkeyW, SmallButtonHeight);
            _headerTailX = _hotkeyButtonRect.Right + headerGap;

            // Toggle row: tiny checkbox + readable label, centered as one group.
            float toggleY = panel.Y + 28.0f;
            float toggleGap = 34.0f;
            float stackW = GetToggleWidth("Stackables");
            float primalW = GetToggleWidth("Primals");
            float ancientW = GetToggleWidth("Ancients");
            float toggleTotalW = stackW + toggleGap + primalW + toggleGap + ancientW;
            float toggleX = centerX - toggleTotalW * 0.5f;
            _stackablesRect = new RectangleF(toggleX, toggleY, stackW, SmallButtonHeight);
            _primalsRect = new RectangleF(_stackablesRect.Right + toggleGap, toggleY, primalW, SmallButtonHeight);
            _ancientsRect = new RectangleF(_primalsRect.Right + toggleGap, toggleY, ancientW, SmallButtonHeight);

            // Preferred tab row: centered label + segmented pill.
            float tabY = panel.Y + 52.0f;
            float tabLabelW = MeasureTextWidth(SmallFont, "Preferred item tab:");
            float tabGap = 11.0f;
            float tabTotalW = tabLabelW + tabGap + TabControlWidth;
            _tabLabelX = centerX - tabTotalW * 0.5f;
            _tabLabelY = tabY + 2.0f;
            float tabX = _tabLabelX + tabLabelW + tabGap;
            float side = 28.0f;
            _tabControlRect = new RectangleF(tabX, tabY, TabControlWidth, SmallButtonHeight);
            _tabMinusRect = new RectangleF(tabX, tabY, side, SmallButtonHeight);
            _tabValueRect = new RectangleF(tabX + side, tabY, Math.Max(1.0f, TabControlWidth - side * 2.0f), SmallButtonHeight);
            _tabPlusRect = new RectangleF(_tabValueRect.Right, tabY, side, SmallButtonHeight);

            _statusY = panel.Y + 69.0f;
            return true;
        }

        private RectangleF GetStoragePanelRect()
        {
            var stash = Hud.Inventory.StashMainUiElement;
            var r = stash != null ? stash.Rectangle : RectangleF.Empty;
            if (r.Width <= 0 || r.Height <= 0)
                return RectangleF.Empty;

            // Center on the native stash panel itself, then clamp only enough to keep
            // the close button clear. Centering inside a reduced/right-cleared area
            // shifts the panel left, which makes it look visually off-center.
            float left = r.Left + Math.Max(0.0f, PanelLeftOffset);
            float w = Math.Min(PanelWidth, Math.Max(300.0f, r.Width - Math.Max(0.0f, PanelLeftOffset) - Math.Max(0.0f, PanelRightClearance)));
            float x = r.Left + (r.Width - w) * 0.5f + Math.Max(0.0f, PanelLeftOffset) * 0.5f;
            float maxRight = r.Right - Math.Max(24.0f, PanelRightClearance);
            if (x + w > maxRight)
                x = maxRight - w;
            if (x < left)
                x = left;

            float y = r.Top + PanelTopOffset;
            float h = Math.Max(82.0f, PanelHeight);
            return new RectangleF(x, y, w, h);
        }

        private void DrawToggleButton(RectangleF rect, string label, bool on)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float box = 12.0f;
            float boxY = rect.Y + (rect.Height - box) * 0.5f;
            var boxRect = new RectangleF(rect.X, boxY, box, box);

            DrawRoundedRect(boxRect, 2.0f, on ? ToggleOnBrush : ToggleOffBrush, on ? ToggleOnBorderBrush : ToggleOffBorderBrush);
            if (on && ToggleOnTopBrush != null)
            {
                var hi = new RectangleF(boxRect.X + 2.0f, boxRect.Y + 2.0f, Math.Max(0.0f, boxRect.Width - 4.0f), boxRect.Height * 0.38f);
                DrawRoundedRect(hi, 1.5f, ToggleOnTopBrush, null);
            }

            DrawText(ToggleFont ?? SmallFont, label, boxRect.Right + 7.0f, rect.Y + 1.0f);
        }

        private float GetToggleWidth(string label)
        {
            return 12.0f + 7.0f + MeasureTextWidth(ToggleFont ?? SmallFont, label);
        }

        private void DrawSegmentedTabControl()
        {
            DrawSegmentedPillBase(_tabControlRect);

            if (PillOrangeSeparatorBrush != null && _tabControlRect.Width > 0 && _tabControlRect.Height > 0)
            {
                float y1 = _tabControlRect.Y + 3.0f;
                float y2 = _tabControlRect.Y + _tabControlRect.Height - 3.0f;
                PillOrangeSeparatorBrush.DrawLine(_tabMinusRect.Right, y1, _tabMinusRect.Right, y2);
                PillOrangeSeparatorBrush.DrawLine(_tabValueRect.Right, y1, _tabValueRect.Right, y2);
            }

            DrawCenteredText(_tabMinusRect, "-");
            DrawCenteredText(_tabValueRect, PreferredItemTab <= 0 ? "Auto" : PreferredItemTab.ToString(CultureInfo.InvariantCulture));
            DrawCenteredText(_tabPlusRect, "+");
        }

        private void DrawPillButton(RectangleF rect, string text, bool green)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, null, PillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? PillGreenBrush : PillDarkBrush, null);

            var highlight = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, Math.Max(0.0f, inner.Width - 2.0f), inner.Height * 0.42f);
            DrawRoundedRect(highlight, highlight.Height * 0.5f, green ? PillGreenLightBrush : PillLightBrush, null);

            DrawCenteredText(rect, text);
        }

        private void DrawSegmentedPillBase(RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, null, PillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, PillDarkBrush, null);

            var highlight = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, Math.Max(0.0f, inner.Width - 2.0f), inner.Height * 0.42f);
            DrawRoundedRect(highlight, highlight.Height * 0.5f, PillLightBrush, null);
        }

        private void DrawCenteredText(RectangleF rect, string text)
        {
            DrawCenteredText(ButtonFont, rect, text);
        }

        private void DrawCenteredText(IFont font, RectangleF rect, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            var layout = font.GetTextLayout(text);
            float x = rect.X + rect.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float y = rect.Y + rect.Height * 0.5f - layout.Metrics.Height * 0.5f - 0.5f;
            font.DrawText(layout, x, y);
        }

        private void DrawRoundedRect(RectangleF rect, float radius, IBrush fill, IBrush border)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            if (_geometryDrawFailed)
            {
                if (fill != null) fill.DrawRectangle(rect);
                if (border != null) border.DrawRectangle(rect);
                return;
            }

            try
            {
                if (fill != null)
                {
                    using (var pg = Hud.Render.CreateGeometry())
                    {
                        using (var gs = pg.Open())
                        {
                            BeginRoundedRectFigure(gs, rect, radius, true, true, true, true);
                            gs.Close();
                        }
                        fill.DrawGeometry(pg);
                    }
                }

                if (border != null)
                {
                    using (var pg = Hud.Render.CreateGeometry())
                    {
                        using (var gs = pg.Open())
                        {
                            BeginRoundedRectFigure(gs, rect, radius, true, true, true, true);
                            gs.Close();
                        }
                        border.DrawGeometry(pg);
                    }
                }
            }
            catch
            {
                _geometryDrawFailed = true;
                if (fill != null) fill.DrawRectangle(rect);
                if (border != null) border.DrawRectangle(rect);
            }
        }

        private void BeginRoundedRectFigure(GeometrySink gs, RectangleF r, float radius, bool topLeft, bool topRight, bool bottomRight, bool bottomLeft)
        {
            radius = Math.Max(0.0f, Math.Min(radius, Math.Min(r.Width, r.Height) * 0.5f));

            float left = r.Left;
            float top = r.Top;
            float right = r.Right;
            float bottom = r.Bottom;

            gs.BeginFigure(new Vector2(left + (topLeft ? radius : 0.0f), top), FigureBegin.Filled);

            gs.AddLine(new Vector2(right - (topRight ? radius : 0.0f), top));
            if (topRight) AddArcPoints(gs, right - radius, top + radius, radius, -90.0f, 0.0f);
            else gs.AddLine(new Vector2(right, top));

            gs.AddLine(new Vector2(right, bottom - (bottomRight ? radius : 0.0f)));
            if (bottomRight) AddArcPoints(gs, right - radius, bottom - radius, radius, 0.0f, 90.0f);
            else gs.AddLine(new Vector2(right, bottom));

            gs.AddLine(new Vector2(left + (bottomLeft ? radius : 0.0f), bottom));
            if (bottomLeft) AddArcPoints(gs, left + radius, bottom - radius, radius, 90.0f, 180.0f);
            else gs.AddLine(new Vector2(left, bottom));

            gs.AddLine(new Vector2(left, top + (topLeft ? radius : 0.0f)));
            if (topLeft) AddArcPoints(gs, left + radius, top + radius, radius, 180.0f, 270.0f);
            else gs.AddLine(new Vector2(left, top));

            gs.EndFigure(FigureEnd.Closed);
        }

        private void AddArcPoints(GeometrySink gs, float cx, float cy, float radius, float startDegrees, float endDegrees)
        {
            const int steps = 6;
            float span = endDegrees - startDegrees;
            for (int i = 1; i <= steps; i++)
            {
                float angle = startDegrees + span * i / steps;
                float rad = angle * (float)Math.PI / 180.0f;
                gs.AddLine(new Vector2(cx + radius * (float)Math.Cos(rad), cy + radius * (float)Math.Sin(rad)));
            }
        }

        private float MeasureTextWidth(IFont font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0.0f;

            try { return font.GetTextLayout(text).Metrics.Width; }
            catch { return Math.Max(0, text.Length) * 6.0f; }
        }

        private void DrawText(IFont font, string text, float x, float y)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return;

            font.DrawText(font.GetTextLayout(text), x, y);
        }

        private static RectangleF InsetRect(RectangleF rect, float amount)
        {
            return new RectangleF(rect.X + amount, rect.Y + amount, Math.Max(0.0f, rect.Width - amount * 2.0f), Math.Max(0.0f, rect.Height - amount * 2.0f));
        }

        // ── Material row drawing ──────────────────────────────────────────

        private void DrawItemRow(ISnoItem[] items, float rowLeft, float rowTop, float rowWidth, float rowHeight)
        {
            if (_rowBackground != null)
                _rowBackground.Draw(rowLeft - 1f, rowTop, rowWidth + 2f, rowHeight);

            float slotW = rowWidth / (items.Length + 1f);
            float iconH = rowHeight * 0.85f;
            float iconY = rowTop + (rowHeight - iconH) * 0.5f;

            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null) continue;

                long count = GetCount(item);
                var tex = Hud.Texture.GetItemTexture(item);
                float x = rowLeft + slotW * 0.5f + i * slotW;

                if (tex != null)
                    tex.Draw(x + slotW - iconH, iconY, iconH, iconH, 1f);

                string countStr = FormatCount(count);
                var layout = CountFont.GetTextLayout(countStr);
                float tx = x + slotW - iconH * 1.2f - layout.Metrics.Width;
                float ty = iconY + (iconH - layout.Metrics.Height) * 0.5f;
                CountFont.DrawText(layout, tx, ty);

                if (Hud.Window.CursorInsideRect(tx, iconY, iconH * 1.2f + layout.Metrics.Width, iconH))
                {
                    try { Hud.Render.SetHint(item.NameLocalized); } catch { }
                }
            }
        }

        // ── UI/state helpers ──────────────────────────────────────────────

        private bool IsStashVisible()
        {
            return IsVisible(Hud != null && Hud.Inventory != null ? Hud.Inventory.StashMainUiElement : null);
        }

        private bool IsInventoryVisible()
        {
            return IsVisible(Hud != null && Hud.Inventory != null ? Hud.Inventory.InventoryMainUiElement : null);
        }

        private bool IsChatEntryOpen()
        {
            return IsVisible(_chatEditLine);
        }

        private bool IsVisible(IUiElement element)
        {
            if (element == null) return false;
            try { element.Refresh(); return element.Visible; }
            catch { return false; }
        }

        private IUiElement RegisterUi(string path, IUiElement parent)
        {
            try { return Hud.Render.RegisterUiElement(path, parent, null); }
            catch { try { return Hud.Render.GetUiElement(path); } catch { return null; } }
        }

        private void ShowStatus(string text, int ms)
        {
            _lastStatus = text ?? string.Empty;
            _statusUntilTick = Environment.TickCount + Math.Max(0, ms);
        }

        private static bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        // ── Stash helpers ─────────────────────────────────────────────────

        private int GetAutomationMaxTabs()
        {
            int max = Math.Max(1, GetDetectedTabCount());
            return Math.Max(1, Math.Min(MaxD3StashTabs, max));
        }

        private int GetPreferredTabUiMax()
        {
            return Math.Max(1, Math.Min(MaxD3StashTabs, GetDetectedTabCount()));
        }

        private int GetDetectedTabCount()
        {
            int max = 1;

            try
            {
                int perPage = GetTabsPerPage();
                int selectedAbs = SafeSelectedStashPageIndex() * perPage + SafeSelectedStashTabIndex() + 1;
                if (selectedAbs > max) max = selectedAbs;

                var items = Hud.Inventory.ItemsInStash;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (item == null) continue;
                        int tab = GetAbsoluteTab(item) + 1;
                        if (tab > max) max = tab;
                    }
                }

                // Do not raise the detected cap from stash tab UI placeholders.
                // Some locked/missing tab buttons can still report as visible, which
                // allowed selecting tab 10+ for accounts with fewer unlocked tabs.
                // Item positions plus the currently selected tab are the safer cap.
            }
            catch { }

            return Math.Max(1, Math.Min(MaxD3StashTabs, max));
        }

        private IUiElement GetStashPageElement(int pageIndex)
        {
            try { return Hud.Inventory.GetStashPageUiElement(pageIndex); }
            catch { return null; }
        }

        private IUiElement GetStashTabElement(int tabIndex)
        {
            try { return Hud.Inventory.GetStashTabUiElement(tabIndex); }
            catch { return null; }
        }

        private int SafeSelectedStashPageIndex()
        {
            try { return Math.Max(0, Hud.Inventory.SelectedStashPageIndex); }
            catch { return 0; }
        }

        private int SafeSelectedStashTabIndex()
        {
            try { return Math.Max(0, Hud.Inventory.SelectedStashTabIndex); }
            catch { return 0; }
        }

        private int GetTabsPerPage()
        {
            try { return Math.Max(1, Hud.Inventory.MaxStashTabCountPerPage); }
            catch { return 5; }
        }

        private static int GetAbsoluteTab(IItem item)
        {
            if (item == null) return -1;
            return item.InventoryY >= 0 ? item.InventoryY / StashRowsPerTab : -1;
        }

        private List<IItem> SafeInventoryItems()
        {
            try
            {
                var items = Hud.Inventory.ItemsInInventory;
                return items == null ? new List<IItem>() : items.Where(i => i != null).ToList();
            }
            catch { return new List<IItem>(); }
        }

        private List<IItem> SafeStashItems()
        {
            try
            {
                var items = Hud.Inventory.ItemsInStash;
                return items == null ? new List<IItem>() : items.Where(i => i != null).ToList();
            }
            catch { return new List<IItem>(); }
        }

        // ── Settings ──────────────────────────────────────────────────────

        private void ApplySettingsMigration()
        {
            if (_settingsVersion >= StorageSettingsVersion)
                return;

            // First storage-manager test build used slower defaults. If the
            // user has not manually tuned them, migrate to the faster profile.
            if (StoreItemsTabSwitchDelayMs == 140) StoreItemsTabSwitchDelayMs = 100;
            if (StoreItemsClickDelayMs == 90) StoreItemsClickDelayMs = 30;
            _settingsVersion = StorageSettingsVersion;
        }

        private void ClampSettings()
        {
            PreferredItemTab = Math.Max(0, Math.Min(MaxD3StashTabs, PreferredItemTab));
            StoreItemsUiSettleDelayMs = Math.Max(0, Math.Min(3000, StoreItemsUiSettleDelayMs));
            StoreItemsTabSwitchDelayMs = Math.Max(0, Math.Min(3000, StoreItemsTabSwitchDelayMs));
            StoreItemsClickDelayMs = Math.Max(0, Math.Min(3000, StoreItemsClickDelayMs));
        }

        private void LoadSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
                    return;

                foreach (var rawLine in File.ReadAllLines(_settingsPath))
                {
                    var line = (rawLine ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key.Equals("SettingsVersion", StringComparison.OrdinalIgnoreCase)) _settingsVersion = ParseInt(value, _settingsVersion);
                    else if (key.Equals("StoreItemsHotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        try { StoreItemsHotkey = (Key)Enum.Parse(typeof(Key), value, true); } catch { }
                    }
                    else if (key.Equals("StoreStackables", StringComparison.OrdinalIgnoreCase)) StoreStackables = ParseBool(value, StoreStackables);
                    else if (key.Equals("StorePrimals", StringComparison.OrdinalIgnoreCase)) StorePrimals = ParseBool(value, StorePrimals);
                    else if (key.Equals("StoreAncients", StringComparison.OrdinalIgnoreCase)) StoreAncients = ParseBool(value, StoreAncients);
                    else if (key.Equals("PreferredItemTab", StringComparison.OrdinalIgnoreCase)) PreferredItemTab = ParseInt(value, PreferredItemTab);
                    else if (key.Equals("StoreItemsUiSettleDelayMs", StringComparison.OrdinalIgnoreCase)) StoreItemsUiSettleDelayMs = ParseInt(value, StoreItemsUiSettleDelayMs);
                    else if (key.Equals("StoreItemsTabSwitchDelayMs", StringComparison.OrdinalIgnoreCase)) StoreItemsTabSwitchDelayMs = ParseInt(value, StoreItemsTabSwitchDelayMs);
                    else if (key.Equals("StoreItemsClickDelayMs", StringComparison.OrdinalIgnoreCase)) StoreItemsClickDelayMs = ParseInt(value, StoreItemsClickDelayMs);
                    else if (key.Equals("InventoryVirtualKey", StringComparison.OrdinalIgnoreCase)) InventoryVirtualKey = (ushort)Math.Max(0, Math.Min(255, ParseInt(value, InventoryVirtualKey)));
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(_settingsPath)) return;
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(_settingsPath,
                    "SettingsVersion=" + StorageSettingsVersion + Environment.NewLine +
                    "StoreItemsHotkey=" + StoreItemsHotkey + Environment.NewLine +
                    "StoreStackables=" + StoreStackables + Environment.NewLine +
                    "StorePrimals=" + StorePrimals + Environment.NewLine +
                    "StoreAncients=" + StoreAncients + Environment.NewLine +
                    "PreferredItemTab=" + PreferredItemTab + Environment.NewLine +
                    "StoreItemsUiSettleDelayMs=" + StoreItemsUiSettleDelayMs + Environment.NewLine +
                    "StoreItemsTabSwitchDelayMs=" + StoreItemsTabSwitchDelayMs + Environment.NewLine +
                    "StoreItemsClickDelayMs=" + StoreItemsClickDelayMs + Environment.NewLine +
                    "InventoryVirtualKey=" + InventoryVirtualKey + Environment.NewLine);
            }
            catch { }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool EqualsText(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        // ── Material helpers ──────────────────────────────────────────────

        private long GetCount(ISnoItem item)
        {
            if (item == null) return 0;
            try
            {
                switch (item.Sno)
                {
                    case 3336787100: return Hud.Game.Me.Materials.HeartOfFright;
                    case 2029265596: return Hud.Game.Me.Materials.VialOfPutridness;
                    case 2670343450: return Hud.Game.Me.Materials.IdolOfTerror;
                    case 1102953247: return Hud.Game.Me.Materials.LeoricsRegret;
                    case 198281388: return Hud.Game.Me.Materials.PrimordialAshes;
                    default: return 0;
                }
            }
            catch { return 0; }
        }

        private static string FormatCount(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        private ISnoItem SafeGetItem(uint sno)
        {
            try { return Hud.Inventory.GetSnoItem(sno); }
            catch { return null; }
        }

        // ── Internal data types ───────────────────────────────────────────

        private enum StoreStage
        {
            Idle,
            PrepareUi,
            BuildQueue,
            SelectTab,
            ClickItem,
            WaitAfterClick,
            Done
        }

        private enum StoreKind
        {
            None,
            Stackable,
            Primal,
            Ancient
        }

        private sealed class StoreCandidate
        {
            public string ItemKey;
            public uint Sno;
            public int TargetTabAbs;
            public StoreKind Kind;
        }

        private sealed class StashPlan
        {
            public readonly int MaxTabs;
            public readonly Dictionary<uint, List<IItem>> StacksBySno = new Dictionary<uint, List<IItem>>();
            public readonly List<int> StackableTabs = new List<int>();
            public readonly List<int> NormalGemTabs = new List<int>();
            private readonly bool[,,] _occupied;

            public StashPlan(int maxTabs)
            {
                MaxTabs = Math.Max(1, Math.Min(MaxD3StashTabs, maxTabs));
                _occupied = new bool[MaxTabs, StashColumns, StashRowsPerTab];
            }

            public void AddStack(IItem item)
            {
                if (item == null || item.SnoItem == null) return;

                List<IItem> list;
                if (!StacksBySno.TryGetValue(item.SnoItem.Sno, out list))
                {
                    list = new List<IItem>();
                    StacksBySno[item.SnoItem.Sno] = list;
                }

                list.Add(item);

                int tab = GetAbsoluteTab(item);
                if (tab >= 0 && tab < MaxTabs)
                {
                    AddUniqueTab(StackableTabs, tab);
                    if (IsNormalGemItem(item))
                        AddUniqueTab(NormalGemTabs, tab);
                }
            }

            private static void AddUniqueTab(List<int> tabs, int tab)
            {
                if (tabs == null || tab < 0) return;
                if (!tabs.Contains(tab)) tabs.Add(tab);
            }

            public void MarkOccupied(IItem item, int tab)
            {
                if (item == null || item.SnoItem == null) return;
                if (tab < 0 || tab >= MaxTabs) return;

                int x = Math.Max(0, item.InventoryX);
                int y = item.InventoryY % StashRowsPerTab;
                if (y < 0) y += StashRowsPerTab;

                int width = Math.Max(1, Math.Min(StashColumns, item.SnoItem.ItemWidth));
                int height = Math.Max(1, Math.Min(StashRowsPerTab, item.SnoItem.ItemHeight));

                ReserveCells(tab, x, y, width, height);
            }

            public bool TryReserveInTab(IItem item, int tab)
            {
                if (item == null || item.SnoItem == null) return false;
                if (tab < 0 || tab >= MaxTabs) return false;

                int width = Math.Max(1, Math.Min(StashColumns, item.SnoItem.ItemWidth));
                int height = Math.Max(1, Math.Min(StashRowsPerTab, item.SnoItem.ItemHeight));
                return TryReserveFirstFit(tab, width, height);
            }

            public int FindAndReserveFreeTab(IItem item, int startTab, bool wrapToAuto)
            {
                if (item == null || item.SnoItem == null) return -1;

                int width = Math.Max(1, Math.Min(StashColumns, item.SnoItem.ItemWidth));
                int height = Math.Max(1, Math.Min(StashRowsPerTab, item.SnoItem.ItemHeight));

                startTab = Math.Max(0, Math.Min(MaxTabs - 1, startTab));

                for (int tab = startTab; tab < MaxTabs; tab++)
                {
                    if (TryReserveFirstFit(tab, width, height))
                        return tab;
                }

                if (wrapToAuto && startTab > 0)
                {
                    for (int tab = 0; tab < startTab; tab++)
                    {
                        if (TryReserveFirstFit(tab, width, height))
                            return tab;
                    }
                }

                return -1;
            }

            private bool TryReserveFirstFit(int tab, int width, int height)
            {
                for (int y = 0; y <= StashRowsPerTab - height; y++)
                {
                    for (int x = 0; x <= StashColumns - width; x++)
                    {
                        if (!CanFit(tab, x, y, width, height))
                            continue;

                        ReserveCells(tab, x, y, width, height);
                        return true;
                    }
                }

                return false;
            }

            private bool CanFit(int tab, int x, int y, int width, int height)
            {
                if (tab < 0 || tab >= MaxTabs) return false;
                if (x < 0 || y < 0 || x + width > StashColumns || y + height > StashRowsPerTab) return false;

                for (int yy = y; yy < y + height; yy++)
                    for (int xx = x; xx < x + width; xx++)
                        if (_occupied[tab, xx, yy])
                            return false;

                return true;
            }

            private void ReserveCells(int tab, int x, int y, int width, int height)
            {
                if (tab < 0 || tab >= MaxTabs) return;

                int maxX = Math.Min(StashColumns, x + Math.Max(1, width));
                int maxY = Math.Min(StashRowsPerTab, y + Math.Max(1, height));

                for (int yy = Math.Max(0, y); yy < maxY; yy++)
                    for (int xx = Math.Max(0, x); xx < maxX; xx++)
                        _occupied[tab, xx, yy] = true;
            }
        }
    }

    internal static class s7o_InventoryModInput
    {
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseRightDown = 0x0008;
        private const uint MouseRightUp = 0x0010;
        private const uint KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        public static bool LeftClickRect(RectangleF rect)
        {
            return ClickRect(rect, MouseLeftDown, MouseLeftUp);
        }

        public static bool RightClickRect(RectangleF rect)
        {
            return ClickRect(rect, MouseRightDown, MouseRightUp);
        }

        public static bool TapKey(ushort virtualKey)
        {
            if (virtualKey == 0) return false;

            var input = new INPUT[2];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.ScanCode = 0;
            input[0].U.Keyboard.Flags = 0;
            input[0].U.Keyboard.Time = 0;
            input[0].U.Keyboard.ExtraInfo = IntPtr.Zero;

            input[1].Type = InputKeyboard;
            input[1].U.Keyboard.VirtualKey = virtualKey;
            input[1].U.Keyboard.ScanCode = 0;
            input[1].U.Keyboard.Flags = KeyUp;
            input[1].U.Keyboard.Time = 0;
            input[1].U.Keyboard.ExtraInfo = IntPtr.Zero;

            return SendInput(2, input, Marshal.SizeOf(typeof(INPUT))) == 2;
        }

        private static bool ClickRect(RectangleF rect, uint downFlag, uint upFlag)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);
            if (!SetCursorPos(x, y)) return false;

            var input = new INPUT[2];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = downFlag;
            input[1].Type = InputMouse;
            input[1].U.Mouse.Flags = upFlag;

            return SendInput(2, input, Marshal.SizeOf(typeof(INPUT))) == 2;
        }
    }
}
