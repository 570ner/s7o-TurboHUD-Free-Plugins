using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_KadalaHelper : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameTopPainter, IMouseClickHandler, INewAreaHandler
    {
        private const string SettingsFileName = "s7o_KadalaHelper.ini";
        private const int SettingsVersion = 1;
        private const int NoTick = int.MinValue;
        private const int MinActionDelayMs = 30;
        private const int TabSwitchDelayMs = 60;
        private const int StatusHoldMs = 1400;
        private const int ClickBurstPerAction = 1; // keep one buy per selected item visit so rotation stays precise

        public bool ShowPanel { get; set; } = true;
        public bool PersistUserSettings { get; set; } = true;
        public Key SelectHotkey { get; set; } = Key.F2;
        public Key BuyHotkey { get; set; } = Key.F3;

        private IKeyEvent _selectKeyEvent;
        private IKeyEvent _buyKeyEvent;
        private IUiElement _shopMainPage;
        private IUiElement _shopPanel;
        private IUiElement _shopGoldText;
        private IUiElement[] _itemRegions;
        private IUiElement[] _tabs;

        private readonly List<KadalaItem> _items = new List<KadalaItem>();
        private readonly HashSet<int> _selected = new HashSet<int>();
        private readonly List<int> _runItems = new List<int>();

        private string _settingsPath;
        private bool _running;
        private bool _captureSelectHotkey;
        private bool _captureBuyHotkey;
        private int _singleHoverItemId;
        private int _rotationIndex;
        private int _nextActionTick;
        private int _lastClickedItemId;
        private int _boughtCount;
        private string _lastStatus;
        private int _statusUntilTick = NoTick;

        private RectangleF _panelRect = RectangleF.Empty;
        private RectangleF _selectHotkeyRect = RectangleF.Empty;
        private RectangleF _buyHotkeyRect = RectangleF.Empty;
        private RectangleF _statusRect = RectangleF.Empty;

        private IBrush _panelBackBrush;
        private IBrush _panelBorderBrush;
        private IBrush _pillBorderBrush;
        private IBrush _pillDarkBrush;
        private IBrush _pillLightBrush;
        private IBrush _pillGreenBrush;
        private IBrush _pillGreenLightBrush;
        private IBrush _toggleOffBrush;
        private IBrush _checkFillBrush;
        private IBrush _checkGlossBrush;
        private IBrush _checkBorderBrush;
        private IBrush _checkShadowBrush;
        private IBrush _checkStrokeBrush;
        private IBrush _checkStrokeShadowBrush;
        private IFont _font;
        private IFont _smallFont;
        private IFont _yellowFont;
        private IFont _greenFont;
        private IFont _checkFont;
        private bool _geometryDrawFailed;

        public s7o_KadalaHelper()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            BuildItems();
            ResolveSettingsPath();
            LoadSettings();
            RegisterHotkeys();
            RegisterUiElements();
            BuildResources();
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            StopRun(null);
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed)
                return;

            if (_captureSelectHotkey || _captureBuyHotkey)
            {
                CaptureHotkey(keyEvent.Key);
                return;
            }

            if (Matches(_selectKeyEvent, keyEvent, SelectHotkey))
            {
                ToggleHoveredSelection();
                return;
            }

            if (Matches(_buyKeyEvent, keyEvent, BuyHotkey))
            {
                ToggleBuyRun();
                return;
            }
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left || !ShowPanel || !IsKadalaShopOpen())
                return false;

            UpdatePanelLayout();
            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;

            if (PointInRect(_selectHotkeyRect, x, y))
            {
                if (!_running)
                {
                    _captureSelectHotkey = true;
                    _captureBuyHotkey = false;
                    ShowStatus("press F2 replacement", StatusHoldMs);
                }
                return true;
            }

            if (PointInRect(_buyHotkeyRect, x, y))
            {
                if (!_running)
                {
                    _captureBuyHotkey = true;
                    _captureSelectHotkey = false;
                    ShowStatus("press F3 replacement", StatusHoldMs);
                }
                else ShowStatus("running", StatusHoldMs);
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

            if (!IsKadalaShopOpen())
            {
                if (_running) StopRun("Kadala closed");
                _captureSelectHotkey = false;
                _captureBuyHotkey = false;
                return;
            }

            if (_running)
                AdvanceBuyRun(Environment.TickCount);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!Enabled || !ShowPanel || clipState != ClipState.AfterClip || !IsKadalaShopOpen())
                return;

            UpdatePanelLayout();
            DrawSelectionChecks();
            DrawPanel();
        }

        private void ToggleHoveredSelection()
        {
            if (!IsKadalaShopOpen())
                return;

            int id = GetHoveredVisibleItemId();
            if (id <= 0)
            {
                ShowStatus("hover a Kadala item", StatusHoldMs);
                return;
            }

            if (!_selected.Remove(id))
                _selected.Add(id);

            SaveSettings();
            ShowStatus((_selected.Contains(id) ? "selected " : "removed ") + GetItem(id).Name, StatusHoldMs);
        }

        private void ToggleBuyRun()
        {
            if (!IsKadalaShopOpen())
                return;

            if (_running)
            {
                StopRun("stopped");
                return;
            }

            _runItems.Clear();
            _singleHoverItemId = 0;

            if (_selected.Count > 0)
                foreach (var item in _items)
                    if (_selected.Contains(item.Id)) _runItems.Add(item.Id);

            if (_runItems.Count == 0)
            {
                int hovered = GetHoveredVisibleItemId();
                if (hovered <= 0)
                {
                    ShowStatus("select or hover item", StatusHoldMs);
                    return;
                }

                _singleHoverItemId = hovered;
                _runItems.Add(hovered);
            }

            _rotationIndex = 0;
            _lastClickedItemId = 0;
            _boughtCount = 0;
            _running = true;
            _nextActionTick = Environment.TickCount;
            ShowStatus("buying...", StatusHoldMs);
        }

        private void AdvanceBuyRun(int now)
        {
            if (!TickReachedOrUnset(now, _nextActionTick))
                return;

            if (!IsKadalaShopOpen() || Hud.Window == null || !Hud.Window.IsForeground)
            {
                StopRun("stopped");
                return;
            }

            if (Hud.Game == null || Hud.Game.Me == null || _runItems.Count == 0)
            {
                StopRun("not ready");
                return;
            }

            int id = FindNextBuyableItemId();
            if (id <= 0)
            {
                StopRun(_boughtCount > 0 ? ("bought " + _boughtCount.ToString(CultureInfo.InvariantCulture)) : "no shards/space");
                return;
            }

            KadalaItem item = GetItem(id);
            if (item == null)
            {
                StopRun("item missing");
                return;
            }

            if (!IsTabActive(item.TabIndex))
            {
                if (ClickTab(item.TabIndex))
                {
                    _lastClickedItemId = 0;
                    _nextActionTick = now + TabSwitchDelayMs;
                    return;
                }

                StopRun("tab unavailable");
                return;
            }

            IUiElement region = GetRegion(item.RegionIndex);
            if (region == null || !region.Visible)
            {
                StopRun("item unavailable");
                return;
            }

            int clickCount = GetClickBurstCount(item);
            if (clickCount <= 0)
            {
                StopRun(_boughtCount > 0 ? ("bought " + _boughtCount.ToString(CultureInfo.InvariantCulture)) : "no shards/space");
                return;
            }

            int sent = 0;
            for (int i = 0; i < clickCount; i++)
            {
                if (!s7o_KadalaInput.RightClickRect(region.Rectangle))
                    break;
                sent++;
            }

            if (sent > 0)
            {
                _boughtCount += sent;
                _lastClickedItemId = id;
                _rotationIndex = NextRotationIndex(id);
                _nextActionTick = now + MinActionDelayMs;
                ShowStatus("buying " + item.Name + "...", 650);
            }
            else StopRun("click failed");
        }

        private int FindNextBuyableItemId()
        {
            if (_runItems.Count <= 0)
                return 0;

            for (int step = 0; step < _runItems.Count; step++)
            {
                int idx = (_rotationIndex + step) % _runItems.Count;
                int id = _runItems[idx];
                KadalaItem item = GetItem(id);
                if (CanBuy(item))
                {
                    _rotationIndex = idx;
                    return id;
                }
            }

            return 0;
        }

        private bool CanBuy(KadalaItem item)
        {
            if (item == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            try
            {
                if ((int)Hud.Game.Me.Materials.BloodShard < item.ShardCost)
                    return false;

                int free = Hud.Game.Me.InventorySpaceTotal - Hud.Game.InventorySpaceUsed;
                return free >= item.InventorySlots;
            }
            catch { return false; }
        }

        private int GetClickBurstCount(KadalaItem item)
        {
            if (item == null || Hud.Game == null || Hud.Game.Me == null)
                return 0;

            try
            {
                int shardBuys = (int)Hud.Game.Me.Materials.BloodShard / Math.Max(1, item.ShardCost);
                int free = Hud.Game.Me.InventorySpaceTotal - Hud.Game.InventorySpaceUsed;
                int spaceBuys = free / Math.Max(1, item.InventorySlots);
                return Math.Max(0, Math.Min(ClickBurstPerAction, Math.Min(shardBuys, spaceBuys)));
            }
            catch { return 0; }
        }

        private int NextRotationIndex(int clickedId)
        {
            if (_runItems.Count <= 0)
                return 0;

            int idx = _runItems.IndexOf(clickedId);
            if (idx < 0)
                return 0;

            return (idx + 1) % _runItems.Count;
        }

        private void StopRun(string status)
        {
            _running = false;
            _nextActionTick = 0;
            _rotationIndex = 0;
            _lastClickedItemId = 0;
            _runItems.Clear();
            _singleHoverItemId = 0;
            if (!string.IsNullOrEmpty(status)) ShowStatus(status, StatusHoldMs);
        }

        private void DrawSelectionChecks()
        {
            foreach (var item in _items)
            {
                if (!_selected.Contains(item.Id))
                    continue;

                if (!IsTabActive(item.TabIndex))
                    continue;

                IUiElement region = GetRegion(item.RegionIndex);
                if (region == null || !region.Visible)
                    continue;

                DrawCheck(ToDxRect(region.Rectangle), item.Id == _lastClickedItemId && _running);
            }
        }

        private void DrawCheck(RectangleF region, bool activePulse)
        {
            if (region.Width <= 0 || region.Height <= 0)
                return;

            // Item regions include the icon on the far left. Anchor the marker under the
            // item-name text, not over the icon, and draw the tick with lines so it centers
            // consistently across fonts/resolutions.
            float size = Math.Max(21.0f, Math.Min(24.0f, Math.Min(region.Width, region.Height) * 0.285f));
            float x = region.Left + Math.Max(38.0f, region.Width * 0.238f);
            float y = region.Top + Math.Max(24.0f, region.Height * 0.305f);

            if (x + size > region.Right - 6.0f)
                x = region.Right - size - 6.0f;
            if (y + size > region.Bottom - 8.0f)
                y = region.Bottom - size - 8.0f;

            var rect = new RectangleF(x, y, size, size);

            if (_checkShadowBrush != null)
                DrawRoundedRect(new RectangleF(rect.X + 1.6f, rect.Y + 1.6f, rect.Width, rect.Height), 4.0f, _checkShadowBrush, null);

            DrawRoundedRect(rect, 4.0f, _checkFillBrush, _checkBorderBrush);

            if (_checkGlossBrush != null)
                _checkGlossBrush.DrawRectangle(rect.X + 2.0f, rect.Y + 2.0f, Math.Max(1.0f, rect.Width - 4.0f), rect.Height * 0.42f);

            float ax = rect.Left + rect.Width * 0.22f;
            float ay = rect.Top + rect.Height * 0.56f;
            float bx = rect.Left + rect.Width * 0.42f;
            float by = rect.Top + rect.Height * 0.74f;
            float cx = rect.Left + rect.Width * 0.80f;
            float cy = rect.Top + rect.Height * 0.30f;

            if (_checkStrokeShadowBrush != null)
            {
                _checkStrokeShadowBrush.DrawLine(ax + 1.0f, ay + 1.0f, bx + 1.0f, by + 1.0f);
                _checkStrokeShadowBrush.DrawLine(bx + 1.0f, by + 1.0f, cx + 1.0f, cy + 1.0f);
            }

            if (_checkStrokeBrush != null)
            {
                _checkStrokeBrush.DrawLine(ax, ay, bx, by);
                _checkStrokeBrush.DrawLine(bx, by, cx, cy);
            }
        }

        private void DrawPanel()
        {
            if (_panelRect.Width <= 0 || _panelRect.Height <= 0)
                return;

            if (_panelBackBrush != null) _panelBackBrush.DrawRectangle(_panelRect);
            if (_panelBorderBrush != null) _panelBorderBrush.DrawRectangle(_panelRect);

            float pad = 10.0f;
            float gap = 18.0f;
            float groupW = (_panelRect.Width - pad * 2.0f - gap) * 0.5f;
            float groupY = _panelRect.Y + 10.0f;
            var markGroup = new RectangleF(_panelRect.X + pad, groupY, groupW, 22.0f);
            var spendGroup = new RectangleF(markGroup.Right + gap, groupY, groupW, 22.0f);

            DrawHeaderGroup(markGroup, _captureSelectHotkey ? "..." : SelectHotkey.ToString(), _captureSelectHotkey, "to mark item(s)", out _selectHotkeyRect);
            DrawHeaderGroup(spendGroup, _captureBuyHotkey ? "..." : BuyHotkey.ToString(), _captureBuyHotkey || _running, _running ? "to stop" : "to spend shards", out _buyHotkeyRect);

            var statusRect = new RectangleF(_panelRect.X + 12.0f, _panelRect.Y + 32.0f, _panelRect.Width - 24.0f, 18.0f);
            var selectedRect = new RectangleF(_panelRect.X + 12.0f, _panelRect.Y + 54.0f, _panelRect.Width - 24.0f, Math.Max(22.0f, _panelRect.Bottom - (_panelRect.Y + 54.0f) - 8.0f));

            string status = GetStatusText();
            if (!string.IsNullOrEmpty(status))
                DrawCenteredText(_smallFont, statusRect, status);

            DrawSelectedText(selectedRect, GetSelectedText());
        }

        private void DrawHeaderGroup(RectangleF group, string hotkeyText, bool active, string tailText, out RectangleF hotkeyRect)
        {
            hotkeyRect = LayoutHeaderHotkeyRect(group, hotkeyText, tailText);
            const float headerGap = 10.0f;
            float y = group.Y;

            DrawPillButton(hotkeyRect, hotkeyText, active);
            DrawText(_yellowFont, tailText, hotkeyRect.Right + headerGap, y);
        }

        private RectangleF LayoutHeaderHotkeyRect(RectangleF group, string hotkeyText, string tailText)
        {
            const float headerGap = 10.0f;
            float tailW = MeasureTextWidth(_yellowFont, tailText);
            float keyW = Math.Max(42.0f, MeasureTextWidth(_font, hotkeyText) + 16.0f);
            float totalW = keyW + headerGap + tailW;
            float x = group.X + (group.Width - totalW) * 0.5f;
            return new RectangleF(x, group.Y - 2.0f, keyW, 20.0f);
        }

        private string GetSelectedText()
        {
            if (_selected.Count <= 0)
                return "Saved: none - hover item + " + SelectHotkey.ToString();

            var names = new List<string>();
            foreach (var item in _items)
                if (_selected.Contains(item.Id)) names.Add(item.Name);

            return "Saved: " + string.Join(", ", names.ToArray());
        }

        private string GetStatusText()
        {
            if (!string.IsNullOrEmpty(_lastStatus) && TickIsFuture(Environment.TickCount, _statusUntilTick))
                return _lastStatus;
            if (_running)
                return "buying...";
            return null;
        }

        private bool UpdatePanelLayout()
        {
            RectangleF anchor = GetShopAnchorRect();
            if (anchor.Width <= 0 || anchor.Height <= 0)
                return false;

            float w = Math.Min(450.0f, Math.Max(330.0f, anchor.Width - 14.0f));
            float h = 104.0f;
            float x = anchor.Left + (anchor.Width - w) * 0.5f;
            float y = anchor.Top + 8.0f;

            _panelRect = new RectangleF(x, y, w, h);

            float pad = 10.0f;
            float gap = 18.0f;
            float groupW = (_panelRect.Width - pad * 2.0f - gap) * 0.5f;
            float groupY = _panelRect.Y + 10.0f;
            var markGroup = new RectangleF(_panelRect.X + pad, groupY, groupW, 22.0f);
            var spendGroup = new RectangleF(markGroup.Right + gap, groupY, groupW, 22.0f);
            _selectHotkeyRect = LayoutHeaderHotkeyRect(markGroup, _captureSelectHotkey ? "..." : SelectHotkey.ToString(), "to mark item(s)");
            _buyHotkeyRect = LayoutHeaderHotkeyRect(spendGroup, _captureBuyHotkey ? "..." : BuyHotkey.ToString(), _running ? "to stop" : "to spend shards");
            _statusRect = new RectangleF(x + 12.0f, y + 32.0f, w - 24.0f, 18.0f);
            return true;
        }

        private RectangleF GetShopAnchorRect()
        {
            try
            {
                if (_shopPanel != null && _shopPanel.Visible)
                {
                    var r = ToDxRect(_shopPanel.Rectangle);
                    if (r.Width > 0 && r.Height > 0)
                        return r;
                }
            }
            catch { }

            try
            {
                if (_shopMainPage != null && _shopMainPage.Visible)
                {
                    var r = ToDxRect(_shopMainPage.Rectangle);
                    if (r.Width > 0 && r.Height > 0)
                        return r;
                }
            }
            catch { }

            return RectangleF.Empty;
        }

        private bool IsKadalaShopOpen()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null || !Hud.Game.IsInGame || !Hud.Game.IsInTown || Hud.Game.IsLoading)
                    return false;
                if (Hud.Window == null || !Hud.Window.IsForeground)
                    return false;
                if (Hud.Inventory == null || Hud.Inventory.InventoryMainUiElement == null || !Hud.Inventory.InventoryMainUiElement.Visible)
                    return false;
                if (_shopMainPage == null || !_shopMainPage.Visible || _shopGoldText == null || !_shopGoldText.Visible)
                    return false;

                string text = _shopGoldText.ReadText(Encoding.UTF8, true);
                return !string.IsNullOrEmpty(text) && text.EndsWith("{icon:x1_shard}", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private int GetHoveredVisibleItemId()
        {
            try
            {
                int x = Hud.Window.CursorX;
                int y = Hud.Window.CursorY;

                foreach (var item in _items)
                {
                    if (!IsTabActive(item.TabIndex))
                        continue;

                    IUiElement region = GetRegion(item.RegionIndex);
                    if (region == null || !region.Visible)
                        continue;

                    if (PointInRect(ToDxRect(region.Rectangle), x, y))
                        return item.Id;
                }
            }
            catch { }

            return 0;
        }

        private bool IsTabActive(int tabIndex)
        {
            IUiElement tab = GetTab(tabIndex);
            if (tab == null || !tab.Visible)
                return false;

            KadalaItem first = null;
            foreach (var item in _items)
                if (item.TabIndex == tabIndex) { first = item; break; }

            if (first == null)
                return false;

            return tab.AnimState == first.ActiveAnimState;
        }

        private bool ClickTab(int tabIndex)
        {
            IUiElement tab = GetTab(tabIndex);
            return tab != null && tab.Visible && s7o_KadalaInput.LeftClickRect(tab.Rectangle);
        }

        private IUiElement GetRegion(int regionIndex)
        {
            return _itemRegions != null && regionIndex >= 0 && regionIndex < _itemRegions.Length ? _itemRegions[regionIndex] : null;
        }

        private IUiElement GetTab(int tabIndex)
        {
            return _tabs != null && tabIndex >= 0 && tabIndex < _tabs.Length ? _tabs[tabIndex] : null;
        }

        private KadalaItem GetItem(int id)
        {
            foreach (var item in _items)
                if (item.Id == id) return item;
            return null;
        }

        private void CaptureHotkey(Key key)
        {
            if (key == Key.Unknown)
                return;

            if (_captureSelectHotkey)
            {
                SelectHotkey = key;
                ShowStatus("select hotkey: " + key.ToString(), StatusHoldMs);
            }
            else if (_captureBuyHotkey)
            {
                BuyHotkey = key;
                ShowStatus("buy hotkey: " + key.ToString(), StatusHoldMs);
            }

            _captureSelectHotkey = false;
            _captureBuyHotkey = false;
            RegisterHotkeys();
            SaveSettings();
        }

        private void RegisterHotkeys()
        {
            try { _selectKeyEvent = Hud.Input.CreateKeyEvent(true, SelectHotkey, false, false, false); } catch { _selectKeyEvent = null; }
            try { _buyKeyEvent = Hud.Input.CreateKeyEvent(true, BuyHotkey, false, false, false); } catch { _buyKeyEvent = null; }
        }

        private static bool Matches(IKeyEvent registered, IKeyEvent actual, Key fallback)
        {
            if (registered != null && registered.Matches(actual))
                return true;
            return actual != null && actual.Key == fallback;
        }

        private void RegisterUiElements()
        {
            _shopMainPage = Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage", null, null);
            _shopPanel = Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.panel", _shopMainPage, null);
            _shopGoldText = Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.gold_text", _shopMainPage, null);

            _itemRegions = new[]
            {
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 0 0", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 1 0", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 0 1", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 1 1", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 0 2", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 1 2", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 0 3", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 1 3", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.shop_item_region.item 0 4", _shopMainPage, null),
            };

            _tabs = new[]
            {
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.tab_0", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.tab_1", _shopMainPage, null),
                Hud.Render.RegisterUiElement("Root.NormalLayer.shop_dialog_mainPage.tab_2", _shopMainPage, null),
            };
        }

        private void BuildItems()
        {
            _items.Clear();
            Add(1, "1H Wep", 0, 0, 49, 75, 2);
            Add(2, "2H Wep", 1, 0, 49, 75, 2);
            Add(3, "Quiver", 2, 0, 49, 25, 2);
            Add(4, "Orb", 3, 0, 49, 25, 2);
            Add(5, "Mojo", 4, 0, 49, 25, 2);
            Add(6, "Phylactery", 5, 0, 49, 25, 2);
            Add(7, "Helm", 0, 1, 40, 25, 2);
            Add(8, "Gloves", 1, 1, 40, 25, 2);
            Add(9, "Boots", 2, 1, 40, 25, 2);
            Add(10, "Chest", 3, 1, 40, 25, 2);
            Add(11, "Belt", 4, 1, 40, 25, 1);
            Add(12, "Shoulders", 5, 1, 40, 25, 2);
            Add(13, "Pants", 6, 1, 40, 25, 2);
            Add(14, "Bracers", 7, 1, 40, 25, 2);
            Add(15, "Shield", 8, 1, 40, 25, 2);
            Add(16, "Ring", 0, 2, 46, 50, 1);
            Add(17, "Amulet", 1, 2, 46, 100, 1);
        }

        private void Add(int id, string name, int region, int tab, int activeAnim, int cost, int slots)
        {
            _items.Add(new KadalaItem { Id = id, Name = name, RegionIndex = region, TabIndex = tab, ActiveAnimState = activeAnim, ShardCost = cost, InventorySlots = slots });
        }

        private void BuildResources()
        {
            _panelBackBrush = Hud.Render.CreateBrush(190, 0, 0, 0, 0);
            _panelBorderBrush = Hud.Render.CreateBrush(210, 180, 120, 30, 1.2f);
            _pillBorderBrush = Hud.Render.CreateBrush(230, 245, 150, 30, 1.2f);
            _pillDarkBrush = Hud.Render.CreateBrush(230, 18, 18, 18, 0);
            _pillLightBrush = Hud.Render.CreateBrush(80, 255, 255, 255, 0);
            _pillGreenBrush = Hud.Render.CreateBrush(220, 24, 105, 38, 0);
            _pillGreenLightBrush = Hud.Render.CreateBrush(75, 150, 255, 150, 0);
            _toggleOffBrush = Hud.Render.CreateBrush(220, 14, 14, 14, 0);
            _checkFillBrush = Hud.Render.CreateBrush(230, 18, 155, 52, 0);
            _checkGlossBrush = Hud.Render.CreateBrush(95, 120, 255, 130, 0);
            _checkBorderBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 2.0f);
            _checkShadowBrush = Hud.Render.CreateBrush(135, 0, 0, 0, 0);
            _checkStrokeBrush = Hud.Render.CreateBrush(255, 218, 255, 205, 2.4f);
            _checkStrokeShadowBrush = Hud.Render.CreateBrush(190, 0, 0, 0, 3.6f);
            _font = Hud.Render.CreateFont("tahoma", 7.6f, 255, 255, 255, 255, true, false, 255, 0, 0, 0, true);
            _smallFont = Hud.Render.CreateFont("tahoma", 7.6f, 255, 235, 235, 235, false, false, 255, 0, 0, 0, true);
            _yellowFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 255, 220, 64, true, false, 255, 0, 0, 0, true);
            _greenFont = Hud.Render.CreateFont("tahoma", 10.0f, 255, 210, 255, 210, true, false, 180, 0, 0, 0, true);
            _checkFont = Hud.Render.CreateFont("tahoma", 14.0f, 255, 210, 255, 210, true, false, 180, 0, 0, 0, true);
        }

        private void ResolveSettingsPath()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings");
                Directory.CreateDirectory(dir);
                _settingsPath = Path.Combine(dir, SettingsFileName);
            }
            catch
            {
                _settingsPath = null;
            }
        }

        private void LoadSettings()
        {
            if (!PersistUserSettings || string.IsNullOrEmpty(_settingsPath) || !File.Exists(_settingsPath))
                return;

            try
            {
                foreach (string raw in File.ReadAllLines(_settingsPath))
                {
                    string line = (raw ?? string.Empty).Trim();
                    if (line.Length <= 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("SelectHotkey", StringComparison.OrdinalIgnoreCase)) SelectHotkey = ParseKey(val, SelectHotkey);
                    else if (key.Equals("BuyHotkey", StringComparison.OrdinalIgnoreCase)) BuyHotkey = ParseKey(val, BuyHotkey);
                    else if (key.Equals("Selected", StringComparison.OrdinalIgnoreCase)) ParseSelected(val);
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            if (!PersistUserSettings || string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                string dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(_settingsPath,
                    "# s7o_KadalaHelper user settings" + Environment.NewLine +
                    "SettingsVersion=" + SettingsVersion.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "SelectHotkey=" + SelectHotkey.ToString() + Environment.NewLine +
                    "BuyHotkey=" + BuyHotkey.ToString() + Environment.NewLine +
                    "Selected=" + BuildSelectedString() + Environment.NewLine);
            }
            catch { }
        }

        private string BuildSelectedString()
        {
            var ids = new List<string>();
            foreach (var item in _items)
                if (_selected.Contains(item.Id)) ids.Add(item.Id.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", ids.ToArray());
        }

        private void ParseSelected(string value)
        {
            _selected.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (string part in value.Split(','))
            {
                int id;
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && GetItem(id) != null)
                    _selected.Add(id);
            }
        }

        private static Key ParseKey(string value, Key fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            try { return (Key)Enum.Parse(typeof(Key), value, true); }
            catch { return fallback; }
        }

        private void ShowStatus(string text, int ms)
        {
            _lastStatus = text;
            _statusUntilTick = unchecked(Environment.TickCount + Math.Max(0, ms));
        }

        private static bool TickReachedOrUnset(int now, int tick)
        {
            return tick == 0 || tick == NoTick || unchecked(now - tick) >= 0;
        }

        private static bool TickIsFuture(int now, int untilTick)
        {
            return untilTick != 0 && untilTick != NoTick && unchecked(now - untilTick) < 0;
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
                            BeginRoundedRectFigure(gs, rect, radius);
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
                            BeginRoundedRectFigure(gs, rect, radius);
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

        private static void BeginRoundedRectFigure(GeometrySink gs, RectangleF r, float radius)
        {
            radius = Math.Max(0.0f, Math.Min(radius, Math.Min(r.Width, r.Height) * 0.5f));

            float left = r.Left;
            float top = r.Top;
            float right = r.Right;
            float bottom = r.Bottom;

            gs.BeginFigure(new Vector2(left + radius, top), FigureBegin.Filled);
            gs.AddLine(new Vector2(right - radius, top));
            AddArcPoints(gs, right - radius, top + radius, radius, -90.0f, 0.0f);
            gs.AddLine(new Vector2(right, bottom - radius));
            AddArcPoints(gs, right - radius, bottom - radius, radius, 0.0f, 90.0f);
            gs.AddLine(new Vector2(left + radius, bottom));
            AddArcPoints(gs, left + radius, bottom - radius, radius, 90.0f, 180.0f);
            gs.AddLine(new Vector2(left, top + radius));
            AddArcPoints(gs, left + radius, top + radius, radius, 180.0f, 270.0f);
            gs.EndFigure(FigureEnd.Closed);
        }

        private static void AddArcPoints(GeometrySink gs, float cx, float cy, float radius, float startDegrees, float endDegrees)
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

        private static RectangleF InsetRect(RectangleF rect, float amount)
        {
            return new RectangleF(rect.X + amount, rect.Y + amount, Math.Max(0.0f, rect.Width - amount * 2.0f), Math.Max(0.0f, rect.Height - amount * 2.0f));
        }

        private static RectangleF ToDxRect(System.Drawing.RectangleF rect)
        {
            return new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        private float MeasureTextWidth(IFont font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0.0f;

            try { return font.GetTextLayout(text).Metrics.Width; }
            catch { return Math.Max(0, text.Length) * 6.0f; }
        }

        private void DrawSelectedText(RectangleF rect, string text)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0)
                return;

            if (MeasureTextWidth(_smallFont, text) <= rect.Width)
            {
                DrawCenteredText(_smallFont, rect, text);
                return;
            }

            DrawWrappedCenteredText(_smallFont, rect, text, 2);
        }

        private int DrawWrappedCenteredText(IFont font, RectangleF rect, string text, int maxLines)
        {
            if (font == null || string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0 || maxLines <= 0)
                return 0;

            var lines = WrapText(font, text, rect.Width, maxLines);
            if (lines.Count <= 0)
                return 0;

            float lineH = Math.Min(13.0f, rect.Height / Math.Max(1, lines.Count));
            float totalH = lineH * lines.Count;
            float y = rect.Y + Math.Max(0.0f, (rect.Height - totalH) * 0.5f);

            foreach (string line in lines)
            {
                DrawCenteredText(font, new RectangleF(rect.X, y, rect.Width, lineH), line);
                y += lineH;
            }

            return lines.Count;
        }

        private List<string> WrapText(IFont font, string text, float maxWidth, int maxLines)
        {
            var lines = new List<string>();
            if (font == null || string.IsNullOrEmpty(text) || maxWidth <= 0 || maxLines <= 0)
                return lines;

            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;
            bool truncated = false;

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                string candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (MeasureTextWidth(font, candidate) <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;

                if (lines.Count >= maxLines)
                {
                    truncated = true;
                    break;
                }
            }

            if (!truncated && lines.Count < maxLines && !string.IsNullOrEmpty(current))
                lines.Add(current);
            else if (!truncated && lines.Count >= maxLines && !string.IsNullOrEmpty(current))
                truncated = true;

            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                truncated = true;
            }

            if (truncated && lines.Count > 0)
            {
                string last = lines[lines.Count - 1];
                while (last.Length > 3 && MeasureTextWidth(font, last + "...") > maxWidth)
                    last = last.Substring(0, last.Length - 1).TrimEnd();
                lines[lines.Count - 1] = last + "...";
            }

            return lines;
        }

        private void DrawPillButton(RectangleF rect, string text, bool green)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, null, _pillBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? _pillGreenBrush : _pillDarkBrush, null);

            var hi = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, Math.Max(0.0f, inner.Width - 2.0f), inner.Height * 0.42f);
            DrawRoundedRect(hi, hi.Height * 0.5f, green ? _pillGreenLightBrush : _pillLightBrush, null);

            DrawCenteredText(_font, rect, text);
        }

        private void DrawText(IFont font, string text, float x, float y)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            font.DrawText(text, x, y);
        }

        private void DrawCenteredText(IFont font, RectangleF rect, string text)
        {
            if (font == null || string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;
            var layout = font.GetTextLayout(text);
            font.DrawText(layout, rect.X + (rect.Width - layout.Metrics.Width) * 0.5f, rect.Y + (rect.Height - layout.Metrics.Height) * 0.5f);
        }

        private sealed class KadalaItem
        {
            public int Id;
            public string Name;
            public int RegionIndex;
            public int TabIndex;
            public int ActiveAnimState;
            public int ShardCost;
            public int InventorySlots;
        }
    }

    internal static class s7o_KadalaInput
    {
        private const uint InputMouse = 0;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseRightDown = 0x0008;
        private const uint MouseRightUp = 0x0010;

        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        public static bool LeftClickRect(RectangleF rect)
        {
            return ClickRect(rect.X, rect.Y, rect.Width, rect.Height, MouseLeftDown, MouseLeftUp);
        }

        public static bool LeftClickRect(System.Drawing.RectangleF rect)
        {
            return ClickRect(rect.X, rect.Y, rect.Width, rect.Height, MouseLeftDown, MouseLeftUp);
        }

        public static bool RightClickRect(RectangleF rect)
        {
            return ClickRect(rect.X, rect.Y, rect.Width, rect.Height, MouseRightDown, MouseRightUp);
        }

        public static bool RightClickRect(System.Drawing.RectangleF rect)
        {
            return ClickRect(rect.X, rect.Y, rect.Width, rect.Height, MouseRightDown, MouseRightUp);
        }

        private static bool ClickRect(float rectX, float rectY, float rectWidth, float rectHeight, uint downFlag, uint upFlag)
        {
            if (rectWidth <= 0 || rectHeight <= 0) return false;
            int x = (int)Math.Round(rectX + rectWidth * 0.5f);
            int y = (int)Math.Round(rectY + rectHeight * 0.5f);
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
