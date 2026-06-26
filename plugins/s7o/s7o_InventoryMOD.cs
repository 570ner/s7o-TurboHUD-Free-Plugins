namespace Turbo.Plugins.s7o
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Forms;
    using SharpDX.Direct2D1;
    using SharpDX.DirectInput;
    using Vector2 = SharpDX.Vector2;
    using Turbo.Plugins.Default;

    public class s7o_InventoryAdjustments : BasePlugin,
        IInGameTopPainter, IKeyEventHandler, IMouseClickHandler, IAfterCollectHandler
    {

        public int ExistingMaterialRowCount { get; set; } = 3;

        public bool ShowStoragePanel { get; set; } = true;
        public Key StoreItemsHotkey { get; set; } = Key.F3;
        public bool EnableItemDropHotkeys { get; set; } = true;
        public Key DropAllHotkey { get; set; } = Key.F2;
        public Key DropFilteredHotkey { get; set; } = Key.F3;
        public bool DropNonAccountBoundLegendaries { get; set; } = false;
        public bool DropTrashItems { get; set; } = true;
        public bool DropGifts { get; set; } = true;
        public bool DropScreams { get; set; } = false;
        public bool DropGems { get; set; } = false;

        public bool StoreStackables { get; set; } = true;
        public bool StorePrimals { get; set; } = true;
        public bool StoreAncients { get; set; } = false;

        public bool SellGifts { get; set; } = true;
        public bool SellScreams { get; set; } = true;
        public bool SellGems { get; set; } = true;
        public bool DropUnidentified { get; set; } = true;

        public float MerchantDropXRatio { get; set; } = 0.50f;
        public float MerchantDropYRatio { get; set; } = 0.56f;
        public float DropAllXRatio { get; set; } = 0.50f;
        public float DropAllYRatio { get; set; } = 0.56f;

        public int PreferredItemTab { get; set; } = 0;

        public int StoreItemsUiSettleDelayMs { get; set; } = 160;
        public int StoreItemsTabSwitchDelayMs { get; set; } = 100;
        public int StoreItemsClickDelayMs { get; set; } = 30;

        public ushort InventoryVirtualKey { get; set; } = 0x49; // I

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

        public float PanelLeftOffset { get; set; } = 0.0f;
        public float PanelTopOffset { get; set; } = 26.0f;
        public float PanelRightClearance { get; set; } = 40.0f;
        public float PanelWidth { get; set; } = 382.0f;
        public float PanelHeight { get; set; } = 86.0f;
        public float HotkeyButtonWidth { get; set; } = 44.0f;
        public float SmallButtonHeight { get; set; } = 20.0f;
        public float TabControlWidth { get; set; } = 134.0f;

        private const int MaxD3StashTabs = 13;
        private const int StashColumns = 7;
        private const int StashRowsPerTab = 10;
        private const uint PetrifiedScreamSno = 1051857800;
        private const uint RamaladniGiftSno = 1844495708;
        private const ushort VkEscape = 0x1B;
        private const int StorageSettingsVersion = 8;
        private const int SpecialTooltipProbeInitialDelayMs = 30;
        private const int SpecialTooltipProbePollMs = 30;
        private const int SpecialTooltipProbeTimeoutMs = 300;
        private const int TooltipCostPathCount = 13;
        private const int DropAllClickDelayMs = 45;

        private ISnoItem[] _infernalRow;
        private ITexture _rowBackground;
        private IKeyEvent _storeKeyEvent;
        private IKeyEvent _dropAllKeyEvent;
        private IKeyEvent _dropFilteredKeyEvent;
        private bool _capturingHotkey;
        private IUiElement _chatEditLine;
        private IUiElement _shopMainPage;
        private IUiElement _shopPanel;
        private IUiElement _shopGoldText;
        private IUiElement[] _tooltipCostElements;

        private RectangleF _hotkeyButtonRect;
        private RectangleF _stackablesRect;
        private RectangleF _primalsRect;
        private RectangleF _ancientsRect;
        private RectangleF _sellGiftsRect;
        private RectangleF _sellScreamsRect;
        private RectangleF _sellGemsRect;
        private RectangleF _dropUnidentifiedRect;
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
        private readonly List<MerchantCandidate> _merchantQueue = new List<MerchantCandidate>();
        private readonly List<DropCandidate> _dropAllQueue = new List<DropCandidate>();
        private readonly Dictionary<string, SpecialTooltipDecision> _specialCostDecisionCache = new Dictionary<string, SpecialTooltipDecision>();
        private RunMode _runMode = RunMode.None;
        private MerchantStage _merchantStage = MerchantStage.Idle;
        private int _currentMerchantQueueIndex;
        private int _soldCount;
        private int _droppedCount;
        private int _dropAllCount;
        private int _currentDropAllQueueIndex;
        private DropAllStage _dropAllStage = DropAllStage.Idle;
        private bool _dropAllRunUsesFilters;
        private bool _geometryDrawFailed;

        private string _settingsPath;

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
            // Shift+F2 drops all; Shift+F3 drops only the configured filter categories. Both use the local FreeHUD-safe drag helper.
            _dropAllKeyEvent = Hud.Input.CreateKeyEvent(true, DropAllHotkey, false, false, true);
            _dropFilteredKeyEvent = Hud.Input.CreateKeyEvent(true, DropFilteredHotkey, false, false, true);
            _chatEditLine = RegisterUi("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null);
            _shopMainPage = RegisterUi("Root.NormalLayer.shop_dialog_mainPage", null);
            _shopPanel = RegisterUi("Root.NormalLayer.shop_dialog_mainPage.panel", _shopMainPage);
            _shopGoldText = RegisterUi("Root.NormalLayer.shop_dialog_mainPage.gold_text", _shopMainPage);
            _tooltipCostElements = new IUiElement[TooltipCostPathCount];
            for (int i = 0; i < _tooltipCostElements.Length; i++)
                _tooltipCostElements[i] = RegisterUi("Root.TopLayer.item " + i.ToString(CultureInfo.InvariantCulture) + ".stack.frame_cost.cost", null);
        }

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

            bool dropAllPressed = EnableItemDropHotkeys && _dropAllKeyEvent != null && _dropAllKeyEvent.Matches(keyEvent);
            bool dropFilteredPressed = EnableItemDropHotkeys && _dropFilteredKeyEvent != null && _dropFilteredKeyEvent.Matches(keyEvent);
            if (dropAllPressed || dropFilteredPressed)
            {
                if (_running)
                {
                    StopRun("cancelled");
                    return;
                }

                if (!IsInventoryVisible())
                {
                    ShowStatus("open inventory first", 1200);
                    return;
                }

                if (IsStashVisible() || IsMerchantShopVisible())
                {
                    ShowStatus("close stash/shop first", 1200);
                    return;
                }

                BeginDropAllRun(dropFilteredPressed);
                return;
            }

            if (_storeKeyEvent == null || !_storeKeyEvent.Matches(keyEvent))
                return;

            bool stashVisible = IsStashVisible();
            bool merchantVisible = IsMerchantShopVisible();

            if (!stashVisible && !merchantVisible)
                return;

            if (_running)
            {
                StopRun("cancelled");
                return;
            }

            if (stashVisible) BeginStoreRun();
            else BeginMerchantRun();
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left)
                return false;

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return false;

            bool stashVisible = IsStashVisible();
            bool merchantVisible = IsMerchantShopVisible();

            if (!ShowStoragePanel || (!stashVisible && !merchantVisible))
                return false;

            if (stashVisible)
            {
                if (!UpdateStoragePanelLayout())
                    return false;
            }
            else
            {
                if (!UpdateMerchantPanelLayout())
                    return false;
            }

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
                    || PointInRect(_sellGiftsRect, x, y) || PointInRect(_sellScreamsRect, x, y) || PointInRect(_sellGemsRect, x, y) || PointInRect(_dropUnidentifiedRect, x, y)
                    || PointInRect(_tabMinusRect, x, y) || PointInRect(_tabPlusRect, x, y) || PointInRect(_tabValueRect, x, y))
                {
                    ShowStatus("running", 900);
                    return true;
                }

                return false;
            }

            if (merchantVisible)
            {
                if (PointInRect(_sellGiftsRect, x, y))
                {
                    SellGifts = !SellGifts;
                    SaveSettings();
                    return true;
                }

                if (PointInRect(_sellScreamsRect, x, y))
                {
                    SellScreams = !SellScreams;
                    SaveSettings();
                    return true;
                }

                if (PointInRect(_sellGemsRect, x, y))
                {
                    SellGems = !SellGems;
                    SaveSettings();
                    return true;
                }

                if (PointInRect(_dropUnidentifiedRect, x, y))
                {
                    DropUnidentified = !DropUnidentified;
                    SaveSettings();
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

        public void AfterCollect()
        {
            if (!Enabled)
                return;

            if (_capturingHotkey && ((!IsStashVisible() && !IsMerchantShopVisible()) || Hud == null || Hud.Window == null || !Hud.Window.IsForeground))
                _capturingHotkey = false;

            if (!_running)
                return;

            int now = Environment.TickCount;
            if (_runMode == RunMode.Merchant) AdvanceMerchantRun(now);
            else if (_runMode == RunMode.DropAll) AdvanceDropAllRun(now);
            else AdvanceStoreRun(now);
        }

        private void BeginStoreRun()
        {
            if (!StoreStackables && !StorePrimals && !StoreAncients)
            {
                ShowStatus("nothing selected", 1200);
                return;
            }

            _queue.Clear();
            _specialCostDecisionCache.Clear();
            _currentQueueIndex = 0;
            _storedCount = 0;
            _skippedCount = 0;
            _running = true;
            _runMode = RunMode.Store;
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

                        if (_queue[_currentQueueIndex].RequiresTooltipProbe && !_queue[_currentQueueIndex].TooltipResolved)
                        {
                            _stage = StoreStage.ProbeTooltip;
                            continue;
                        }

                        if (EnsureTargetStashTab(_queue[_currentQueueIndex].TargetTabAbs, now))
                        {
                            _stage = StoreStage.ClickItem;
                            continue;
                        }
                        return;

                    case StoreStage.ProbeTooltip:
                        CompleteStoreTooltipProbe(now);
                        continue;

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
            _runMode = RunMode.None;
            _stage = StoreStage.Idle;
            _merchantStage = MerchantStage.Idle;
            _dropAllStage = DropAllStage.Idle;
            _nextActionTick = 0;
            _currentQueueIndex = 0;
            _currentMerchantQueueIndex = 0;
            _currentDropAllQueueIndex = 0;
            _queue.Clear();
            _merchantQueue.Clear();
            _dropAllQueue.Clear();
            _dropAllRunUsesFilters = false;
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

        public void ConfigureItemDropFeature(bool enabled, bool nonAccountBoundLegendaries, bool trash, bool gifts, bool screams, bool gems)
        {
            EnableItemDropHotkeys = enabled;
            DropNonAccountBoundLegendaries = nonAccountBoundLegendaries;
            DropTrashItems = trash;
            DropGifts = gifts;
            DropScreams = screams;
            DropGems = gems;
            ClampSettings();
        }

        public void ConfigureDropAllFeature(bool enabled, bool dropAll, bool nonAccountBoundLegendaries, bool trash, bool gifts, bool screams, bool gems)
        {
            ConfigureItemDropFeature(enabled, nonAccountBoundLegendaries, trash, gifts, screams, gems);
        }

        private void BeginDropAllRun(bool filteredOnly)
        {
            _dropAllQueue.Clear();
            _currentDropAllQueueIndex = 0;
            _dropAllCount = 0;
            _skippedCount = 0;
            _dropAllRunUsesFilters = filteredOnly;
            _running = true;
            _runMode = RunMode.DropAll;
            _dropAllStage = DropAllStage.BuildQueue;
            _nextActionTick = Environment.TickCount;
            ShowStatus(filteredOnly ? "dropping filtered items..." : "dropping inventory...", 1200);
        }

        private void AdvanceDropAllRun(int now)
        {
            for (int step = 0; step < MaxStoreStateStepsPerCollect; step++)
            {
                if (!TickReached(now, _nextActionTick))
                    return;

                if (!IsInventoryVisible())
                {
                    StopRun("inventory closed");
                    return;
                }

                if (IsStashVisible() || IsMerchantShopVisible())
                {
                    StopRun("blocked by stash/shop");
                    return;
                }

                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                    return;

                if (IsChatEntryOpen())
                {
                    s7o_InventoryModInput.TapKey(VkEscape);
                    Delay(now, StoreItemsUiSettleDelayMs);
                    return;
                }

                switch (_dropAllStage)
                {
                    case DropAllStage.BuildQueue:
                        BuildDropAllQueue();
                        if (_dropAllQueue.Count <= 0)
                        {
                            StopRun("no droppable items");
                            return;
                        }
                        _dropAllStage = DropAllStage.DropItem;
                        continue;

                    case DropAllStage.DropItem:
                        if (_currentDropAllQueueIndex >= _dropAllQueue.Count)
                        {
                            _dropAllStage = DropAllStage.Done;
                            continue;
                        }
                        DropCurrentInventoryItem(now);
                        continue;

                    case DropAllStage.WaitAfterDrop:
                        _currentDropAllQueueIndex++;
                        _dropAllStage = DropAllStage.DropItem;
                        continue;

                    case DropAllStage.Done:
                        StopRun(_dropAllCount > 0 ? ("dropped " + _dropAllCount.ToString(CultureInfo.InvariantCulture) + " item(s)") : "done");
                        return;
                }
            }
        }

        private void BuildDropAllQueue()
        {
            _dropAllQueue.Clear();
            _skippedCount = 0;

            var inv = SafeInventoryItems();
            if (inv.Count <= 0)
                return;

            foreach (var item in inv.Where(IsDropAllCandidate).OrderBy(i => i.InventoryY).ThenBy(i => i.InventoryX))
            {
                _dropAllQueue.Add(new DropCandidate
                {
                    ItemKey = item.ItemUniqueId ?? string.Empty,
                    Sno = item.SnoItem != null ? item.SnoItem.Sno : 0,
                    InventoryX = item.InventoryX,
                    InventoryY = item.InventoryY,
                    Seed = item.Seed
                });
            }
        }

        private bool IsDropAllCandidate(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;
            if (item.Location != ItemLocation.Inventory || item.IsInventoryLocked)
                return false;
            if (item.SocketedInto != null)
                return false;
            if (item.SnoItem.ItemWidth <= 0 || item.SnoItem.ItemHeight <= 0)
                return false;
            return IsDropAllCategoryEnabled(item);
        }

        private bool IsDropAllCategoryEnabled(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            if (!_dropAllRunUsesFilters)
                return true;

            uint sno = item.SnoItem.Sno;
            if (DropGifts && sno == RamaladniGiftSno)
                return true;
            if (DropScreams && sno == PetrifiedScreamSno)
                return true;
            if (DropGems && IsNormalGemItem(item))
                return true;
            if (DropTrashItems && IsDropTrashItem(item))
                return true;
            if (DropNonAccountBoundLegendaries && IsUnboundLegendaryDrop(item))
                return true;

            return false;
        }

        private static bool IsDropTrashItem(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;
            if (IsSpecialStackableItem(item) || IsNormalGemItem(item))
                return false;
            if (item.SnoItem.Kind != ItemKind.loot)
                return false;
            return item.IsNormal || item.IsMagic || item.IsRare;
        }

        private static bool IsUnboundLegendaryDrop(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;
            if (IsSpecialStackableItem(item) || IsNormalGemItem(item))
                return false;
            if (item.SnoItem.Kind != ItemKind.loot)
                return false;
            if (item.AccountBound || item.BoundToMyAccount)
                return false;

            return item.IsLegendary || item.Quality == ItemQuality.Legendary;
        }

        private void DropCurrentInventoryItem(int now)
        {
            if (_currentDropAllQueueIndex < 0 || _currentDropAllQueueIndex >= _dropAllQueue.Count)
            {
                _dropAllStage = DropAllStage.Done;
                return;
            }

            var item = FindInventoryItemByDropCandidate(_dropAllQueue[_currentDropAllQueueIndex]);
            if (item == null)
            {
                _skippedCount++;
                _dropAllStage = DropAllStage.WaitAfterDrop;
                Delay(now, 0);
                return;
            }

            RectangleF rect;
            try { rect = Hud.Inventory.GetItemRect(item); }
            catch { rect = RectangleF.Empty; }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                _skippedCount++;
                _dropAllStage = DropAllStage.WaitAfterDrop;
                Delay(now, 0);
                return;
            }

            int x, y;
            GetDropAllPoint(out x, out y);
            if (s7o_InventoryModInput.DragRectToPoint(rect, x, y))
            {
                _dropAllCount++;
                if ((_dropAllCount % 5) == 0)
                    ShowStatus("dropped " + _dropAllCount.ToString(CultureInfo.InvariantCulture), 900);
            }
            else
            {
                _skippedCount++;
            }

            _dropAllStage = DropAllStage.WaitAfterDrop;
            Delay(now, DropAllClickDelayMs);
        }

        private IItem FindInventoryItemByDropCandidate(DropCandidate candidate)
        {
            foreach (var item in SafeInventoryItems())
            {
                if (!IsDropAllCandidate(item) || item.SnoItem == null)
                    continue;
                if (!string.IsNullOrEmpty(candidate.ItemKey)
                    && string.Equals(item.ItemUniqueId, candidate.ItemKey, StringComparison.Ordinal))
                    return item;
                if (candidate.Sno != 0 && item.SnoItem.Sno == candidate.Sno
                    && SameInventorySlot(item, candidate.InventoryX, candidate.InventoryY)
                    && SameSeedWhenKnown(item, candidate.Seed))
                    return item;
            }
            return null;
        }

        private void GetDropAllPoint(out int x, out int y)
        {
            float w = 0.0f;
            float h = 0.0f;
            try
            {
                w = Hud != null && Hud.Window != null ? Hud.Window.Size.Width : 0.0f;
                h = Hud != null && Hud.Window != null ? Hud.Window.Size.Height : 0.0f;
            }
            catch { }

            if (w <= 0.0f || h <= 0.0f)
            {
                w = 1280.0f;
                h = 720.0f;
            }

            x = (int)Math.Round(w * DropAllXRatio);
            y = (int)Math.Round(h * DropAllYRatio);
        }

        private void BeginMerchantRun()
        {
            if (!SellGifts && !SellScreams && !SellGems)
            {
                ShowStatus("nothing selected", 1200);
                return;
            }

            _merchantQueue.Clear();
            _specialCostDecisionCache.Clear();
            _currentMerchantQueueIndex = 0;
            _soldCount = 0;
            _droppedCount = 0;
            _skippedCount = 0;
            _running = true;
            _runMode = RunMode.Merchant;
            _merchantStage = MerchantStage.PrepareUi;
            _nextActionTick = Environment.TickCount;
            ShowStatus("selling/dropping...", 1200);
        }

        private void AdvanceMerchantRun(int now)
        {
            for (int step = 0; step < MaxStoreStateStepsPerCollect; step++)
            {
                if (!TickReached(now, _nextActionTick))
                    return;

                if (!IsMerchantShopVisible())
                {
                    StopRun("merchant closed");
                    return;
                }

                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                    return;

                if (_merchantStage != MerchantStage.Done)
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

                switch (_merchantStage)
                {
                    case MerchantStage.PrepareUi:
                        _merchantStage = MerchantStage.BuildQueue;
                        continue;

                    case MerchantStage.BuildQueue:
                        BuildMerchantQueue();
                        if (_merchantQueue.Count <= 0)
                        {
                            StopRun(_skippedCount > 0 ? "nothing eligible" : "no items");
                            return;
                        }
                        _merchantStage = MerchantStage.ClickItem;
                        continue;

                    case MerchantStage.ClickItem:
                        if (_currentMerchantQueueIndex >= _merchantQueue.Count)
                        {
                            _merchantStage = MerchantStage.Done;
                            continue;
                        }

                        ClickCurrentMerchantCandidate(now);
                        continue;

                    case MerchantStage.ProbeTooltip:
                        CompleteMerchantTooltipProbe(now);
                        continue;

                    case MerchantStage.WaitAfterClick:
                        _currentMerchantQueueIndex++;
                        _merchantStage = MerchantStage.ClickItem;
                        continue;

                    case MerchantStage.Done:
                        string result = "done";
                        if (_soldCount > 0 || _droppedCount > 0)
                            result = "sold " + _soldCount.ToString(CultureInfo.InvariantCulture) + ", dropped " + _droppedCount.ToString(CultureInfo.InvariantCulture);
                        StopRun(result);
                        return;
                }
            }
        }

        private void BuildMerchantQueue()
        {
            _merchantQueue.Clear();
            _skippedCount = 0;

            var inv = SafeInventoryItems();
            if (inv.Count <= 0)
                return;

            var sorted = inv.OrderBy(i => i.InventoryY).ThenBy(i => i.InventoryX).ToList();
            var plan = BuildStashPlan();
            plan.AddTrustedInventorySpecialSeeds(sorted);

            var sellQueue = new List<MerchantCandidate>();
            var dropQueue = new List<MerchantCandidate>();
            var probeQueue = new List<MerchantCandidate>();

            foreach (var item in sorted)
            {
                MerchantKind kind;
                bool drop;

                if (TryClassifyMerchantCandidate(item, plan, out kind, out drop))
                {
                    var candidate = CreateMerchantCandidate(item, kind, drop);
                    if (drop) dropQueue.Add(candidate);
                    else sellQueue.Add(candidate);
                    continue;
                }

                if (TryCreateMerchantTooltipProbe(item, out kind))
                {
                    var probe = CreateMerchantCandidate(item, kind, false);
                    probe.RequiresTooltipProbe = true;
                    probeQueue.Add(probe);
                    continue;
                }

                if (IsSpecialStackableItem(item)) _skippedCount++;
            }

            _merchantQueue.AddRange(sellQueue);
            _merchantQueue.AddRange(dropQueue);
            _merchantQueue.AddRange(probeQueue);

        }

        private MerchantCandidate CreateMerchantCandidate(IItem item, MerchantKind kind, bool drop)
        {
            return new MerchantCandidate
            {
                ItemKey = item.ItemUniqueId ?? string.Empty,
                Sno = item.SnoItem != null ? item.SnoItem.Sno : 0,
                InventoryX = item.InventoryX,
                InventoryY = item.InventoryY,
                Seed = item.Seed,
                SpecialStackable = IsSpecialStackableItem(item),
                Kind = kind,
                Drop = drop
            };
        }

        private bool TryClassifyMerchantCandidate(IItem item, StashPlan plan, out MerchantKind kind, out bool drop)
        {
            kind = MerchantKind.None;
            drop = false;
            if (item == null || item.SnoItem == null) return false;
            if (item.Location != ItemLocation.Inventory || item.IsInventoryLocked) return false;

            uint sno = item.SnoItem.Sno;
            if (sno == RamaladniGiftSno || sno == PetrifiedScreamSno)
            {
                if (sno == RamaladniGiftSno)
                {
                    if (!SellGifts) return false;
                    kind = MerchantKind.Gift;
                }
                else
                {
                    if (!SellScreams) return false;
                    kind = MerchantKind.Scream;
                }

                if (IsKnownSpecialStackSeed(item, plan))
                    return true;

                SpecialTooltipDecision cached;
                if (TryGetCachedSpecialTooltipDecision(item, out cached))
                {
                    if (cached == SpecialTooltipDecision.Identified)
                        return true;

                    if (cached == SpecialTooltipDecision.Unidentified && DropUnidentified)
                    {
                        drop = true;
                        return true;
                    }
                }

                return false;
            }

            if (SellGems && IsNormalGemItem(item))
            {
                kind = MerchantKind.Gem;
                return true;
            }

            return false;
        }

        private bool TryCreateMerchantTooltipProbe(IItem item, out MerchantKind kind)
        {
            kind = MerchantKind.None;
            if (!IsSpecialStackableItem(item)) return false;
            if (item.Location != ItemLocation.Inventory || item.IsInventoryLocked) return false;

            SpecialTooltipDecision cached;
            if (TryGetCachedSpecialTooltipDecision(item, out cached))
                return false;

            uint sno = item.SnoItem.Sno;
            if (sno == RamaladniGiftSno)
            {
                if (!SellGifts) return false;
                kind = MerchantKind.Gift;
                return true;
            }

            if (sno == PetrifiedScreamSno)
            {
                if (!SellScreams) return false;
                kind = MerchantKind.Scream;
                return true;
            }

            return false;
        }

        private bool TryGetCachedSpecialTooltipDecision(IItem item, out SpecialTooltipDecision decision)
        {
            decision = SpecialTooltipDecision.Unknown;
            string key = GetSpecialTooltipCacheKey(item);
            return !string.IsNullOrEmpty(key)
                && _specialCostDecisionCache.TryGetValue(key, out decision)
                && decision != SpecialTooltipDecision.Unknown;
        }

        private void CacheSpecialTooltipDecision(IItem item, SpecialTooltipDecision decision)
        {
            if (decision == SpecialTooltipDecision.Unknown) return;
            string key = GetSpecialTooltipCacheKey(item);
            if (!string.IsNullOrEmpty(key)) _specialCostDecisionCache[key] = decision;
        }

        private static string GetSpecialTooltipCacheKey(IItem item)
        {
            if (!IsSpecialStackableItem(item) || item.Seed == 0) return string.Empty;
            return item.SnoItem.Sno.ToString(CultureInfo.InvariantCulture) + ":" + item.Seed.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsSpecialStackableSno(uint sno)
        {
            return sno == RamaladniGiftSno || sno == PetrifiedScreamSno;
        }

        private static bool IsSpecialStackableItem(IItem item)
        {
            return item != null && item.SnoItem != null && IsSpecialStackableSno(item.SnoItem.Sno);
        }

        private static bool IsKnownSpecialStackSource(IItem item)
        {
            if (!IsSpecialStackableItem(item)) return false;
            if (item.Location != ItemLocation.Stash) return false;
            if (item.IsInventoryLocked) return false;
            if (item.Seed == 0) return false;
            if (item.Quantity <= 0) return false;
            if (!item.AccountBound || !item.BoundToMyAccount) return false;
            return true;
        }

        private static bool IsKnownSpecialStackSeed(IItem item, StashPlan plan)
        {
            if (!IsSpecialStackableItem(item) || plan == null) return false;
            if (item.Location != ItemLocation.Inventory) return false;
            if (item.IsInventoryLocked) return false;
            if (item.Seed == 0) return false;
            if (!item.AccountBound || !item.BoundToMyAccount) return false;
            return plan.HasKnownSpecialSeed(item.SnoItem.Sno, item.Seed);
        }

        private static bool HasSpecialStackableStorageBasics(IItem item)
        {
            if (!IsSpecialStackableItem(item)) return false;
            if (item.Location != ItemLocation.Inventory || item.IsInventoryLocked) return false;
            if (item.Seed == 0) return false;
            return item.AccountBound && item.BoundToMyAccount;
        }

        private static bool IsSpecialStackableStorageSafe(IItem item, StashPlan plan)
        {
            return HasSpecialStackableStorageBasics(item)
                && plan != null
                && plan.HasKnownSpecialSeed(item.SnoItem.Sno, item.Seed);
        }

        private static bool SameInventorySlot(IItem item, int x, int y)
        {
            return item != null && item.InventoryX == x && item.InventoryY == y;
        }

        private static bool SameSeedWhenKnown(IItem item, int seed)
        {
            return item != null && (seed == 0 || item.Seed == seed);
        }

        private static bool ShouldContinueSpecialTooltipProbe(int now, int startedTick)
        {
            return startedTick != 0 && (int)(now - startedTick) < SpecialTooltipProbeTimeoutMs;
        }

        private SpecialTooltipDecision ReadSpecialTooltipDecision(IItem item)
        {
            if (!HoveredItemMatchesTarget(item))
                return SpecialTooltipDecision.Unknown;

            var costTexts = new List<string>();
            if (_tooltipCostElements != null)
            {
                foreach (var element in _tooltipCostElements)
                {
                    string text = ReadUiText(element);
                    if (!string.IsNullOrWhiteSpace(text))
                        costTexts.Add(text.Trim());
                }
            }

            var distinct = costTexts.Distinct().ToList();
            if (distinct.Count != 1)
                return SpecialTooltipDecision.Unknown;

            string cost = distinct[0];
            if (ContainsText(cost, "???"))
                return SpecialTooltipDecision.Unidentified;

            if (ContainsText(cost, "Sell Value") || ContainsText(cost, "icon:gold") || HasDigit(cost))
                return SpecialTooltipDecision.Identified;

            return SpecialTooltipDecision.Unknown;
        }

        private bool HoveredItemMatchesTarget(IItem target)
        {
            if (!IsSpecialStackableItem(target) || Hud == null || Hud.Inventory == null)
                return false;

            IItem hovered;
            try { hovered = Hud.Inventory.HoveredItem; }
            catch { hovered = null; }

            if (!IsSpecialStackableItem(hovered))
                return false;

            if (target.AcdId != 0 && hovered.AcdId == target.AcdId)
                return true;

            return hovered.SnoItem.Sno == target.SnoItem.Sno
                && hovered.InventoryX == target.InventoryX
                && hovered.InventoryY == target.InventoryY
                && (target.Seed == 0 || hovered.Seed == target.Seed);
        }

        private static bool ContainsText(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasDigit(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
                if (char.IsDigit(text[i])) return true;
            return false;
        }

        private void ClickCurrentMerchantCandidate(int now)
        {
            if (_currentMerchantQueueIndex < 0 || _currentMerchantQueueIndex >= _merchantQueue.Count)
            {
                _merchantStage = MerchantStage.Done;
                return;
            }

            var candidate = _merchantQueue[_currentMerchantQueueIndex];
            var plan = BuildStashPlan();
            plan.AddTrustedInventorySpecialSeeds(SafeInventoryItems());

            var item = FindInventoryItemByMerchantCandidate(candidate, plan);
            if (item == null)
            {
                _skippedCount++;
                _merchantStage = MerchantStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            RectangleF rect;
            try { rect = Hud.Inventory.GetItemRect(item); }
            catch { rect = RectangleF.Empty; }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                _skippedCount++;
                _merchantStage = MerchantStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            if (candidate.RequiresTooltipProbe && !candidate.TooltipResolved)
            {
                SpecialTooltipDecision cached;
                if (TryGetCachedSpecialTooltipDecision(item, out cached))
                {
                    ApplyMerchantProbeDecision(candidate, cached);
                    _merchantStage = candidate.SkipAfterProbe ? MerchantStage.WaitAfterClick : MerchantStage.ClickItem;
                    Delay(now, 0);
                    return;
                }

                if (!s7o_InventoryModInput.MoveToRect(rect))
                {
                    _skippedCount++;
                    _merchantStage = MerchantStage.WaitAfterClick;
                    Delay(now, 0);
                    return;
                }

                candidate.ProbeStartedTick = now;
                _merchantStage = MerchantStage.ProbeTooltip;
                Delay(now, SpecialTooltipProbeInitialDelayMs);
                return;
            }

            if (candidate.SkipAfterProbe)
            {
                _skippedCount++;
                _merchantStage = MerchantStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            MerchantKind liveKind;
            bool liveDrop;
            if (candidate.TooltipResolved)
            {
                liveKind = candidate.Kind;
                liveDrop = candidate.Drop;
            }
            else if (!TryClassifyMerchantCandidate(item, plan, out liveKind, out liveDrop) || liveKind != candidate.Kind || liveDrop != candidate.Drop)
            {
                _skippedCount++;
                _merchantStage = MerchantStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            bool ok;
            if (liveDrop)
            {
                int dropX, dropY;
                GetMerchantDropPoint(out dropX, out dropY);
                ok = s7o_InventoryModInput.DragRectToPoint(rect, dropX, dropY);
                if (ok) _droppedCount++;
            }
            else
            {
                ok = s7o_InventoryModInput.RightClickRect(rect);
                if (ok) _soldCount++;
            }

            if (!ok) _skippedCount++;

            _merchantStage = MerchantStage.WaitAfterClick;
            Delay(now, StoreItemsClickDelayMs);
            if (_soldCount > 0 || _droppedCount > 0)
                ShowStatus("sold " + _soldCount.ToString(CultureInfo.InvariantCulture) + ", dropped " + _droppedCount.ToString(CultureInfo.InvariantCulture), 900);
        }

        private IItem FindInventoryItemByMerchantCandidate(MerchantCandidate candidate, StashPlan plan)
        {
            var items = SafeInventoryItems();
            foreach (var item in items)
            {
                if (item == null || item.SnoItem == null)
                    continue;

                MerchantKind kind;
                bool drop;

                if (candidate.SpecialStackable)
                {
                    if (item.SnoItem.Sno != candidate.Sno) continue;
                    if (!SameInventorySlot(item, candidate.InventoryX, candidate.InventoryY)) continue;
                    if (!SameSeedWhenKnown(item, candidate.Seed)) continue;

                    if (candidate.RequiresTooltipProbe || candidate.TooltipResolved)
                        return item;

                    if (!TryClassifyMerchantCandidate(item, plan, out kind, out drop)) continue;
                    if (kind == candidate.Kind && drop == candidate.Drop)
                        return item;

                    continue;
                }

                if (!string.IsNullOrEmpty(candidate.ItemKey)
                    && string.Equals(item.ItemUniqueId, candidate.ItemKey, StringComparison.Ordinal))
                    return item;

                if (candidate.Sno != 0 && item.SnoItem.Sno == candidate.Sno
                    && TryClassifyMerchantCandidate(item, plan, out kind, out drop)
                    && kind == candidate.Kind && drop == candidate.Drop)
                    return item;
            }

            return null;
        }

        private void CompleteMerchantTooltipProbe(int now)
        {
            if (_currentMerchantQueueIndex < 0 || _currentMerchantQueueIndex >= _merchantQueue.Count)
            {
                _merchantStage = MerchantStage.Done;
                return;
            }

            var candidate = _merchantQueue[_currentMerchantQueueIndex];
            var item = FindInventoryItemByMerchantCandidate(candidate, BuildStashPlan());
            if (item == null)
            {
                _skippedCount++;
                _merchantStage = MerchantStage.WaitAfterClick;
                Delay(now, 0);
                return;
            }

            var decision = ReadSpecialTooltipDecision(item);
            if (decision == SpecialTooltipDecision.Unknown && ShouldContinueSpecialTooltipProbe(now, candidate.ProbeStartedTick))
            {
                Delay(now, SpecialTooltipProbePollMs);
                return;
            }

            CacheSpecialTooltipDecision(item, decision);
            ApplyMerchantProbeDecision(candidate, decision);
            if (candidate.SkipAfterProbe) _skippedCount++;

            _merchantStage = candidate.SkipAfterProbe ? MerchantStage.WaitAfterClick : MerchantStage.ClickItem;
            Delay(now, 0);
        }

        private void ApplyMerchantProbeDecision(MerchantCandidate candidate, SpecialTooltipDecision decision)
        {
            candidate.RequiresTooltipProbe = false;
            candidate.TooltipResolved = decision != SpecialTooltipDecision.Unknown;

            if (decision == SpecialTooltipDecision.Identified)
            {
                candidate.Drop = false;
                candidate.SkipAfterProbe = false;
                return;
            }

            if (decision == SpecialTooltipDecision.Unidentified && DropUnidentified)
            {
                candidate.Drop = true;
                candidate.SkipAfterProbe = false;
                return;
            }

            candidate.SkipAfterProbe = true;
        }

        private void GetMerchantDropPoint(out int x, out int y)
        {
            float w = 0.0f;
            float h = 0.0f;
            try
            {
                w = Hud != null && Hud.Window != null ? Hud.Window.Size.Width : 0.0f;
                h = Hud != null && Hud.Window != null ? Hud.Window.Size.Height : 0.0f;
            }
            catch { }

            if (w <= 0.0f || h <= 0.0f)
            {
                var inv = Hud != null && Hud.Inventory != null ? Hud.Inventory.InventoryMainUiElement : null;
                var r = inv != null ? inv.Rectangle : RectangleF.Empty;
                w = r.Width > 0 ? r.Left : 1280.0f;
                h = r.Height > 0 ? r.Bottom : 720.0f;
            }

            x = (int)Math.Round(w * MerchantDropXRatio);
            y = (int)Math.Round(h * MerchantDropYRatio);
        }

        private void BuildStoreQueue()
        {
            _queue.Clear();
            _skippedCount = 0;

            var inv = SafeInventoryItems();
            if (inv.Count <= 0)
                return;

            var sorted = inv.OrderBy(i => i.InventoryY).ThenBy(i => i.InventoryX).ToList();
            var plan = BuildStashPlan();
            plan.AddTrustedInventorySpecialSeeds(sorted);
            int preferredStartTabAbs = PreferredItemTab <= 0 ? 0 : PreferredItemTab - 1;
            var probeQueue = new List<StoreCandidate>();

            if (StoreStackables)
            {
                foreach (var item in sorted)
                {
                    if (IsSpecialStackableItem(item))
                    {
                        if (!HasSpecialStackableStorageBasics(item))
                        {
                            _skippedCount++;
                            continue;
                        }

                        SpecialTooltipDecision cached;
                        if (IsSpecialStackableStorageSafe(item, plan)
                            || (TryGetCachedSpecialTooltipDecision(item, out cached) && cached == SpecialTooltipDecision.Identified))
                        {
                            int specialTargetTabAbs = ResolveSpecialStackableTargetTab(item, plan, true);
                            TryQueueCandidate(item, StoreKind.Stackable, specialTargetTabAbs);
                            continue;
                        }

                        if (TryGetCachedSpecialTooltipDecision(item, out cached) && cached == SpecialTooltipDecision.Unidentified)
                        {
                            _skippedCount++;
                            continue;
                        }

                        probeQueue.Add(CreateStoreCandidate(item, StoreKind.Stackable, -1, true));
                        continue;
                    }

                    if (!IsStoreStackable(item, plan))
                        continue;

                    int targetTabAbs = ResolveStackableTargetTab(item, plan);
                    TryQueueCandidate(item, StoreKind.Stackable, targetTabAbs);
                }
            }

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

            _queue.AddRange(probeQueue);

        }

        private void TryQueueCandidate(IItem item, StoreKind kind, int targetTabAbs)
        {
            if (targetTabAbs < 0)
            {
                _skippedCount++;
                return;
            }

            _queue.Add(CreateStoreCandidate(item, kind, targetTabAbs, false));
        }

        private StoreCandidate CreateStoreCandidate(IItem item, StoreKind kind, int targetTabAbs, bool requiresTooltipProbe)
        {
            return new StoreCandidate
            {
                ItemKey = item.ItemUniqueId ?? string.Empty,
                Sno = item.SnoItem != null ? item.SnoItem.Sno : 0,
                InventoryX = item.InventoryX,
                InventoryY = item.InventoryY,
                Seed = item.Seed,
                SpecialStackable = IsSpecialStackableItem(item),
                TargetTabAbs = targetTabAbs,
                Kind = kind,
                RequiresTooltipProbe = requiresTooltipProbe
            };
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
            return IsStoreStackable(item, null);
        }

        private bool IsStoreStackable(IItem item, StashPlan plan)
        {
            if (item == null || item.SnoItem == null)
                return false;

            uint sno = item.SnoItem.Sno;

            if (IsSpecialStackableSno(sno))
                return IsSpecialStackableStorageSafe(item, plan);

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

                if (IsSpecialStackableSno(sno) && stashItem.Seed != item.Seed)
                    continue;

                int tab = GetAbsoluteTab(stashItem);
                if (tab >= 0 && tab < plan.MaxTabs)
                    return tab;
            }

            return -1;
        }

        private int ResolveStackableTargetTab(IItem item, StashPlan plan)
        {
            if (item == null || item.SnoItem == null || plan == null)
                return -1;

            if (IsSpecialStackableSno(item.SnoItem.Sno))
                return ResolveSpecialStackableTargetTab(item, plan, false);

            int exactStackTab = FindMatchingStackTab(item, plan);
            if (exactStackTab >= 0)
                return exactStackTab;

            int relatedStackableTab = FindAndReserveRelatedStackableTab(item, plan);
            if (relatedStackableTab >= 0)
                return relatedStackableTab;

            return plan.FindAndReserveFreeTab(item, 0, true);
        }

        private int ResolveSpecialStackableTargetTab(IItem item, StashPlan plan, bool tooltipConfirmedIdentified)
        {
            if (plan == null) return -1;
            if (tooltipConfirmedIdentified)
            {
                if (!HasSpecialStackableStorageBasics(item)) return -1;
            }
            else if (!IsSpecialStackableStorageSafe(item, plan))
                return -1;

            int firstMatchingTab = -1;
            var checkedTabs = new List<int>();
            int maxStack = Math.Max(1, item.SnoItem.StackSize);

            List<IItem> matches;
            if (plan.StacksBySno.TryGetValue(item.SnoItem.Sno, out matches) && matches != null)
            {
                foreach (var stashItem in matches)
                {
                    if (stashItem == null || stashItem.SnoItem == null || stashItem.Seed != item.Seed)
                        continue;

                    int tab = GetAbsoluteTab(stashItem);
                    if (tab < 0 || tab >= plan.MaxTabs)
                        continue;

                    if (firstMatchingTab < 0)
                        firstMatchingTab = tab;

                    if (stashItem.Quantity > 0 && stashItem.Quantity < maxStack)
                        return tab;

                    if (!checkedTabs.Contains(tab))
                        checkedTabs.Add(tab);
                }
            }

            foreach (int tab in checkedTabs)
                if (plan.TryReserveInTab(item, tab))
                    return tab;

            int relatedStackableTab = FindAndReserveRelatedStackableTab(item, plan);
            if (relatedStackableTab >= 0)
                return relatedStackableTab;

            int startTab = firstMatchingTab >= 0 ? firstMatchingTab : 0;
            return plan.FindAndReserveFreeTab(item, startTab, true);
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

            if (IsSpecialStackableSno(item.SnoItem.Sno))
                return IsKnownSpecialStackSource(item);

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

            if (PreferredItemTab > 0)
                return plan.FindAndReserveFreeTab(item, 0, true);

            return -1;
        }

        private void CompleteStoreTooltipProbe(int now)
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

            if (candidate.ProbeStartedTick == 0)
            {
                RectangleF rect;
                try { rect = Hud.Inventory.GetItemRect(item); }
                catch { rect = RectangleF.Empty; }

                if (rect.Width <= 0 || rect.Height <= 0 || !s7o_InventoryModInput.MoveToRect(rect))
                {
                    _skippedCount++;
                    _stage = StoreStage.WaitAfterClick;
                    Delay(now, 0);
                    return;
                }

                candidate.ProbeStartedTick = now;
                Delay(now, SpecialTooltipProbeInitialDelayMs);
                return;
            }

            var decision = ReadSpecialTooltipDecision(item);
            if (decision == SpecialTooltipDecision.Unknown && ShouldContinueSpecialTooltipProbe(now, candidate.ProbeStartedTick))
            {
                Delay(now, SpecialTooltipProbePollMs);
                return;
            }

            CacheSpecialTooltipDecision(item, decision);

            if (decision == SpecialTooltipDecision.Identified)
            {
                var plan = BuildStashPlan();
                plan.AddTrustedInventorySpecialSeeds(SafeInventoryItems());
                int targetTabAbs = ResolveSpecialStackableTargetTab(item, plan, true);
                if (targetTabAbs >= 0)
                {
                    candidate.TargetTabAbs = targetTabAbs;
                    candidate.RequiresTooltipProbe = false;
                    candidate.TooltipResolved = true;
                    _stage = StoreStage.SelectTab;
                    Delay(now, 0);
                    return;
                }
            }

            _skippedCount++;
            _stage = StoreStage.WaitAfterClick;
            Delay(now, 0);
        }

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

            if (candidate.SpecialStackable && (item.Seed != candidate.Seed || item.SnoItem == null || item.SnoItem.Sno != candidate.Sno))
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

                if (candidate.SpecialStackable)
                {
                    if (item.SnoItem.Sno != candidate.Sno) continue;
                    if (!SameInventorySlot(item, candidate.InventoryX, candidate.InventoryY)) continue;
                    if (!SameSeedWhenKnown(item, candidate.Seed)) continue;
                    if (candidate.Kind == StoreKind.Stackable)
                        return item;

                    continue;
                }

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

        public void PaintTopInGame(ClipState clipState)
        {
            if (Hud.Game == null || Hud.Game.Me == null) return;

            if (clipState == ClipState.Inventory)
            {
                DrawMaterialRow();

                if (ShowStoragePanel && IsStashVisible())
                    DrawStoragePanel();

                return;
            }

            if (clipState == ClipState.AfterClip && ShowStoragePanel && IsMerchantShopVisible())
                DrawMerchantPanel();
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

        private void DrawMerchantPanel()
        {
            if (!UpdateMerchantPanelLayout())
                return;

            var panel = GetMerchantPanelRect();
            if (PanelBackBrush != null) PanelBackBrush.DrawRectangle(panel);
            if (PanelBorderBrush != null) PanelBorderBrush.DrawRectangle(panel);

            DrawText(YellowFont, "Press", _headerPressX, _headerPressY);
            DrawPillButton(_hotkeyButtonRect, _capturingHotkey ? "..." : StoreItemsHotkey.ToString(), _capturingHotkey);
            DrawText(YellowFont, "to sell/drop", _headerTailX, _headerPressY);

            DrawToggleButton(_sellGiftsRect, "Gifts", SellGifts);
            DrawToggleButton(_sellScreamsRect, "Screams", SellScreams);
            DrawToggleButton(_sellGemsRect, "Gems", SellGems);
            DrawToggleButton(_dropUnidentifiedRect, "Drop unidentified", DropUnidentified);

            string statusText = null;
            if (!string.IsNullOrEmpty(_lastStatus) && !TickReached(Environment.TickCount, _statusUntilTick))
                statusText = _lastStatus;
            else if (_running && _runMode == RunMode.Merchant)
                statusText = "selling/dropping...";

            if (!string.IsNullOrEmpty(statusText))
                DrawCenteredText(SmallFont, new RectangleF(panel.X + 12.0f, _statusY, panel.Width - 24.0f, SmallButtonHeight), statusText);
        }

        private bool UpdateMerchantPanelLayout()
        {
            _hotkeyButtonRect = RectangleF.Empty;
            _stackablesRect = RectangleF.Empty;
            _primalsRect = RectangleF.Empty;
            _ancientsRect = RectangleF.Empty;
            _sellGiftsRect = RectangleF.Empty;
            _sellScreamsRect = RectangleF.Empty;
            _sellGemsRect = RectangleF.Empty;
            _dropUnidentifiedRect = RectangleF.Empty;
            _tabMinusRect = RectangleF.Empty;
            _tabValueRect = RectangleF.Empty;
            _tabPlusRect = RectangleF.Empty;
            _tabControlRect = RectangleF.Empty;
            _headerPressX = _headerPressY = _headerTailX = 0.0f;
            _tabLabelX = _tabLabelY = _statusY = 0.0f;

            if (!IsMerchantShopVisible())
                return false;

            var panel = GetMerchantPanelRect();
            if (panel.Width <= 0 || panel.Height <= 0)
                return false;

            float centerX = panel.X + panel.Width * 0.5f;

            _headerPressY = panel.Y + 5.0f;
            const float headerGap = 9.0f;
            string hotkeyText = _capturingHotkey ? "..." : StoreItemsHotkey.ToString();
            float pressW = MeasureTextWidth(YellowFont, "Press");
            float tailW = MeasureTextWidth(YellowFont, "to sell/drop");
            float hotkeyW = Math.Max(HotkeyButtonWidth, MeasureTextWidth(ButtonFont, hotkeyText) + 18.0f);
            float headerTotalW = pressW + headerGap + hotkeyW + headerGap + tailW;
            _headerPressX = centerX - headerTotalW * 0.5f;
            _hotkeyButtonRect = new RectangleF(_headerPressX + pressW + headerGap, _headerPressY - 2.0f, hotkeyW, SmallButtonHeight);
            _headerTailX = _hotkeyButtonRect.Right + headerGap;

            float toggleY = panel.Y + 30.0f;
            float gap = 18.0f;
            float giftW = GetToggleWidth("Gifts");
            float screamW = GetToggleWidth("Screams");
            float gemW = GetToggleWidth("Gems");
            float dropW = GetToggleWidth("Drop unidentified");
            float totalW = giftW + gap + screamW + gap + gemW + gap + dropW;
            float x = centerX - totalW * 0.5f;
            _sellGiftsRect = new RectangleF(x, toggleY, giftW, SmallButtonHeight);
            _sellScreamsRect = new RectangleF(_sellGiftsRect.Right + gap, toggleY, screamW, SmallButtonHeight);
            _sellGemsRect = new RectangleF(_sellScreamsRect.Right + gap, toggleY, gemW, SmallButtonHeight);
            _dropUnidentifiedRect = new RectangleF(_sellGemsRect.Right + gap, toggleY, dropW, SmallButtonHeight);

            _statusY = panel.Y + 55.0f;
            return true;
        }

        private RectangleF GetMerchantPanelRect()
        {
            RectangleF r;
            if (!TryGetShopRect(out r))
                return RectangleF.Empty;

            float w = Math.Min(Math.Max(PanelWidth, 420.0f), Math.Max(330.0f, r.Width - Math.Max(0.0f, PanelRightClearance)));
            float x = r.Left + (r.Width - w) * 0.5f;
            float maxRight = r.Right - Math.Max(24.0f, PanelRightClearance);
            if (x + w > maxRight) x = maxRight - w;
            if (x < r.Left) x = r.Left;

            float y = r.Top + PanelTopOffset;
            float h = Math.Max(72.0f, PanelHeight - 14.0f);
            return new RectangleF(x, y, w, h);
        }

        private bool TryGetShopRect(out RectangleF rect)
        {
            rect = RectangleF.Empty;

            if (IsVisible(_shopPanel))
            {
                try
                {
                    rect = _shopPanel.Rectangle;
                    if (rect.Width > 0 && rect.Height > 0)
                        return true;
                }
                catch { }
            }

            if (IsVisible(_shopMainPage))
            {
                try
                {
                    rect = _shopMainPage.Rectangle;
                    if (rect.Width > 0 && rect.Height > 0)
                        return true;
                }
                catch { }
            }

            return false;
        }

        private bool UpdateStoragePanelLayout()
        {
            _hotkeyButtonRect = RectangleF.Empty;
            _stackablesRect = RectangleF.Empty;
            _primalsRect = RectangleF.Empty;
            _ancientsRect = RectangleF.Empty;
            _sellGiftsRect = RectangleF.Empty;
            _sellScreamsRect = RectangleF.Empty;
            _sellGemsRect = RectangleF.Empty;
            _dropUnidentifiedRect = RectangleF.Empty;
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

        private bool IsMerchantShopVisible()
        {
            if (!IsVisible(_shopPanel) && !IsVisible(_shopMainPage))
                return false;

            string text = ReadUiText(_shopGoldText);
            if (text.EndsWith("{icon:x1_shard}", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private string ReadUiText(IUiElement element)
        {
            try
            {
                if (!IsVisible(element)) return string.Empty;
                return element.ReadText(System.Text.Encoding.UTF8, true) ?? string.Empty;
            }
            catch { return string.Empty; }
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

        private void ApplySettingsMigration()
        {
            if (_settingsVersion >= StorageSettingsVersion)
                return;

            if (StoreItemsTabSwitchDelayMs == 140) StoreItemsTabSwitchDelayMs = 100;
            if (StoreItemsClickDelayMs == 90) StoreItemsClickDelayMs = 30;
            if (_settingsVersion < 6) SellGems = true;
            if (_settingsVersion < 8)
            {
                DropNonAccountBoundLegendaries = false;
                DropTrashItems = true;
                DropGifts = true;
                DropScreams = false;
                DropGems = false;
            }
            _settingsVersion = StorageSettingsVersion;
        }

        private void ClampSettings()
        {
            PreferredItemTab = Math.Max(0, Math.Min(MaxD3StashTabs, PreferredItemTab));
            StoreItemsUiSettleDelayMs = Math.Max(0, Math.Min(3000, StoreItemsUiSettleDelayMs));
            StoreItemsTabSwitchDelayMs = Math.Max(0, Math.Min(3000, StoreItemsTabSwitchDelayMs));
            StoreItemsClickDelayMs = Math.Max(30, Math.Min(3000, StoreItemsClickDelayMs));
            MerchantDropXRatio = Math.Max(0.15f, Math.Min(0.85f, MerchantDropXRatio));
            MerchantDropYRatio = Math.Max(0.20f, Math.Min(0.85f, MerchantDropYRatio));
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
                    else if (key.Equals("EnableItemDropHotkeys", StringComparison.OrdinalIgnoreCase)) EnableItemDropHotkeys = ParseBool(value, EnableItemDropHotkeys);
                    else if (key.Equals("EnableDropAllHotkey", StringComparison.OrdinalIgnoreCase)) EnableItemDropHotkeys = ParseBool(value, EnableItemDropHotkeys);
                    else if (key.Equals("DropFilteredHotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        try { DropFilteredHotkey = (Key)Enum.Parse(typeof(Key), value, true); } catch { }
                    }
                    else if (key.Equals("DropNonAccountBoundLegendaries", StringComparison.OrdinalIgnoreCase)) DropNonAccountBoundLegendaries = ParseBool(value, DropNonAccountBoundLegendaries);
                    else if (key.Equals("DropTrashItems", StringComparison.OrdinalIgnoreCase)) DropTrashItems = ParseBool(value, DropTrashItems);
                    else if (key.Equals("DropGifts", StringComparison.OrdinalIgnoreCase)) DropGifts = ParseBool(value, DropGifts);
                    else if (key.Equals("DropScreams", StringComparison.OrdinalIgnoreCase)) DropScreams = ParseBool(value, DropScreams);
                    else if (key.Equals("DropGems", StringComparison.OrdinalIgnoreCase)) DropGems = ParseBool(value, DropGems);
                    else if (key.Equals("StoreStackables", StringComparison.OrdinalIgnoreCase)) StoreStackables = ParseBool(value, StoreStackables);
                    else if (key.Equals("StorePrimals", StringComparison.OrdinalIgnoreCase)) StorePrimals = ParseBool(value, StorePrimals);
                    else if (key.Equals("StoreAncients", StringComparison.OrdinalIgnoreCase)) StoreAncients = ParseBool(value, StoreAncients);
                    else if (key.Equals("SellGifts", StringComparison.OrdinalIgnoreCase)) SellGifts = ParseBool(value, SellGifts);
                    else if (key.Equals("SellScreams", StringComparison.OrdinalIgnoreCase)) SellScreams = ParseBool(value, SellScreams);
                    else if (key.Equals("SellGems", StringComparison.OrdinalIgnoreCase)) SellGems = ParseBool(value, SellGems);
                    else if (key.Equals("DropUnidentified", StringComparison.OrdinalIgnoreCase)) DropUnidentified = ParseBool(value, DropUnidentified);
                    else if (key.Equals("MerchantDropXRatio", StringComparison.OrdinalIgnoreCase)) MerchantDropXRatio = ParseFloat(value, MerchantDropXRatio);
                    else if (key.Equals("MerchantDropYRatio", StringComparison.OrdinalIgnoreCase)) MerchantDropYRatio = ParseFloat(value, MerchantDropYRatio);
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
                    "EnableItemDropHotkeys=" + EnableItemDropHotkeys + Environment.NewLine +
                    "DropFilteredHotkey=" + DropFilteredHotkey + Environment.NewLine +
                    "DropNonAccountBoundLegendaries=" + DropNonAccountBoundLegendaries + Environment.NewLine +
                    "DropTrashItems=" + DropTrashItems + Environment.NewLine +
                    "DropGifts=" + DropGifts + Environment.NewLine +
                    "DropScreams=" + DropScreams + Environment.NewLine +
                    "DropGems=" + DropGems + Environment.NewLine +
                    "StoreStackables=" + StoreStackables + Environment.NewLine +
                    "StorePrimals=" + StorePrimals + Environment.NewLine +
                    "StoreAncients=" + StoreAncients + Environment.NewLine +
                    "SellGifts=" + SellGifts + Environment.NewLine +
                    "SellScreams=" + SellScreams + Environment.NewLine +
                    "SellGems=" + SellGems + Environment.NewLine +
                    "DropUnidentified=" + DropUnidentified + Environment.NewLine +
                    "MerchantDropXRatio=" + MerchantDropXRatio.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "MerchantDropYRatio=" + MerchantDropYRatio.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
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

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool EqualsText(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

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

        private enum RunMode
        {
            None,
            Store,
            Merchant,
            DropAll
        }

        private enum DropAllStage
        {
            Idle,
            BuildQueue,
            DropItem,
            WaitAfterDrop,
            Done
        }

        private enum StoreStage
        {
            Idle,
            PrepareUi,
            BuildQueue,
            SelectTab,
            ProbeTooltip,
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

        private enum MerchantStage
        {
            Idle,
            PrepareUi,
            BuildQueue,
            ClickItem,
            ProbeTooltip,
            WaitAfterClick,
            Done
        }

        private enum MerchantKind
        {
            None,
            Gift,
            Scream,
            Gem
        }

        private sealed class DropCandidate
        {
            public string ItemKey;
            public uint Sno;
            public int InventoryX;
            public int InventoryY;
            public int Seed;
        }

        private sealed class StoreCandidate
        {
            public string ItemKey;
            public uint Sno;
            public int InventoryX;
            public int InventoryY;
            public int Seed;
            public bool SpecialStackable;
            public int TargetTabAbs;
            public StoreKind Kind;
            public bool RequiresTooltipProbe;
            public bool TooltipResolved;
            public int ProbeStartedTick;
        }

        private sealed class MerchantCandidate
        {
            public string ItemKey;
            public uint Sno;
            public int InventoryX;
            public int InventoryY;
            public int Seed;
            public bool SpecialStackable;
            public MerchantKind Kind;
            public bool Drop;
            public bool RequiresTooltipProbe;
            public bool TooltipResolved;
            public bool SkipAfterProbe;
            public int ProbeStartedTick;
        }

        private enum SpecialTooltipDecision
        {
            Unknown,
            Identified,
            Unidentified
        }

        private sealed class StashPlan
        {
            public readonly int MaxTabs;
            public readonly Dictionary<uint, List<IItem>> StacksBySno = new Dictionary<uint, List<IItem>>();
            public readonly Dictionary<uint, List<int>> KnownSpecialSeedsBySno = new Dictionary<uint, List<int>>();
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

                if (IsKnownSpecialStackSource(item))
                    AddKnownSpecialSeed(item.SnoItem.Sno, item.Seed);

                int tab = GetAbsoluteTab(item);
                if (tab >= 0 && tab < MaxTabs)
                {
                    AddUniqueTab(StackableTabs, tab);
                    if (IsNormalGemItem(item))
                        AddUniqueTab(NormalGemTabs, tab);
                }
            }

            public void AddTrustedInventorySpecialSeeds(IEnumerable<IItem> items)
            {
                if (items == null) return;

                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var item in items)
                {
                    if (!IsSpecialStackableItem(item)) continue;
                    if (item.Location != ItemLocation.Inventory) continue;
                    if (item.IsInventoryLocked) continue;
                    if (item.Seed == 0) continue;
                    if (!item.AccountBound || !item.BoundToMyAccount) continue;

                    string key = item.SnoItem.Sno.ToString(CultureInfo.InvariantCulture) + ":" + item.Seed.ToString(CultureInfo.InvariantCulture);
                    int amount = (int)Math.Max(1L, item.Quantity);
                    int current;
                    counts.TryGetValue(key, out current);
                    counts[key] = current + amount;
                }

                foreach (var pair in counts)
                {
                    if (pair.Value < 2) continue;
                    int colon = pair.Key.IndexOf(':');
                    if (colon <= 0) continue;

                    uint sno;
                    int seed;
                    if (!uint.TryParse(pair.Key.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out sno)) continue;
                    if (!int.TryParse(pair.Key.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) continue;
                    AddKnownSpecialSeed(sno, seed);
                }
            }

            private void AddKnownSpecialSeed(uint sno, int seed)
            {
                if (sno == 0 || seed == 0) return;

                List<int> seeds;
                if (!KnownSpecialSeedsBySno.TryGetValue(sno, out seeds))
                {
                    seeds = new List<int>();
                    KnownSpecialSeedsBySno[sno] = seeds;
                }

                if (!seeds.Contains(seed))
                    seeds.Add(seed);
            }

            public bool HasKnownSpecialSeed(uint sno, int seed)
            {
                if (sno == 0 || seed == 0) return false;

                List<int> seeds;
                return KnownSpecialSeedsBySno.TryGetValue(sno, out seeds)
                    && seeds != null
                    && seeds.Contains(seed);
            }

            public bool HasAnyKnownSpecialSeed(uint sno)
            {
                List<int> seeds;
                return KnownSpecialSeedsBySno.TryGetValue(sno, out seeds)
                    && seeds != null
                    && seeds.Count > 0;
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
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string className, string windowText);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WmMouseMove = 0x0200;
        private const uint WmLButtonDown = 0x0201;
        private const uint WmLButtonUp = 0x0202;
        private const uint WmRButtonDown = 0x0204;
        private const uint WmRButtonUp = 0x0205;

        public static bool LeftClickRect(RectangleF rect)
        {
            return ClickRect(rect, MouseLeftDown, MouseLeftUp);
        }

        public static bool MoveToRect(RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);
            return SetCursorPos(x, y);
        }

        public static bool RightClickRect(RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);

            var hwnd = FindWindow("D3 Main Window Class", null);
            if (hwnd != IntPtr.Zero)
            {
                var param = MakeLParam(x, y);
                SendMessage(hwnd, WmRButtonDown, (IntPtr)2, param);
                SendMessage(hwnd, WmRButtonUp, IntPtr.Zero, param);
                return true;
            }

            return ClickRect(rect, MouseRightDown, MouseRightUp);
        }

        public static bool DragRectToPoint(RectangleF rect, int targetX, int targetY)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);

            var hwnd = FindWindow("D3 Main Window Class", null);
            if (hwnd != IntPtr.Zero)
            {
                var itemParam = MakeLParam(x, y);
                var dropParam = MakeLParam(targetX, targetY);
                SendMessage(hwnd, WmLButtonDown, (IntPtr)1, itemParam);
                SendMessage(hwnd, WmMouseMove, (IntPtr)1, dropParam);
                SendMessage(hwnd, WmLButtonUp, IntPtr.Zero, dropParam);
                return true;
            }

            if (!SetCursorPos(x, y)) return false;

            var down = new INPUT[1];
            down[0].Type = InputMouse;
            down[0].U.Mouse.Flags = MouseLeftDown;

            var up = new INPUT[1];
            up[0].Type = InputMouse;
            up[0].U.Mouse.Flags = MouseLeftUp;

            if (SendInput(1, down, Marshal.SizeOf(typeof(INPUT))) != 1) return false;
            if (!SetCursorPos(targetX, targetY)) return false;
            return SendInput(1, up, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
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
