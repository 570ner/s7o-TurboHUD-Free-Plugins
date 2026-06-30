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
    public class s7o_ParagonHelper : BasePlugin, IAfterCollectHandler, IInGameTopPainter, IKeyEventHandler, IMouseClickHandler
    {
        private const string SettingsFileName = "s7o_ParagonHelper.ini";
        private const int SettingsVersion = 10;
        private const int ActiveTabAnim = 13;
        private const int CoreCategory = 0;
        private const int PrimaryStatRow = 0;
        private const int VitalityRow = 1;
        private const int StatusHoldMs = 1800;
        private const int TabSettleMs = 75;
        private const int RowSettleMs = 25;
        private const int SpendDelayMs = 25;
        private const int AcceptDelayMs = 35;
        private const int CloseDelayMs = 35;
        private const int SpendClickLimit = 500;
        private const int MaxSavedProfiles = 16;
        private const int NoTick = int.MinValue;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;

        public bool ShowOverlay { get; set; } = true;
        public bool PersistUserSettings { get; set; } = true;
        public bool AutoAcceptAfterSpending { get; set; } = true;
        public bool RestoreCursorAfterAction { get; set; } = true;
        public Key MarkHotkey { get; set; } = Key.F2;
        public Key SpendHotkey { get; set; } = Key.F3;

        private static readonly string BasePath = "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect";
        private static readonly string[] CoreRowNames = { "Primary Stat", "Vitality", "Movement Speed", "Max Resource" };

        private static readonly System.Drawing.RectangleF[] NativeRowRects =
        {
            new System.Drawing.RectangleF(608.0f, 290.0f, 759.0f, 90.0f),
            new System.Drawing.RectangleF(608.0f, 382.0f, 759.0f, 90.0f),
            new System.Drawing.RectangleF(608.0f, 475.0f, 759.0f, 90.0f),
            new System.Drawing.RectangleF(608.0f, 568.0f, 759.0f, 90.0f)
        };

        private static readonly System.Drawing.RectangleF[] NativeArrowRects =
        {
            new System.Drawing.RectangleF(1304.0f, 306.0f, 44.0f, 44.0f),
            new System.Drawing.RectangleF(1304.0f, 399.0f, 44.0f, 44.0f),
            new System.Drawing.RectangleF(1304.0f, 492.0f, 44.0f, 44.0f),
            new System.Drawing.RectangleF(1304.0f, 584.0f, 44.0f, 44.0f)
        };

        private static readonly System.Drawing.RectangleF NativeCloseRect = new System.Drawing.RectangleF(970.0f, 786.0f, 230.0f, 48.0f);

        private IUiElement _pointSelect;
        private IUiElement _coreTab;
        private IUiElement _coreAvailable;
        private readonly IUiElement[] _rows = new IUiElement[4];
        private readonly IUiElement[] _pointsSpent = new IUiElement[4];
        private readonly IUiElement[] _plusButtons = new IUiElement[4];
        private IUiElement _acceptButton;
        private IUiElement _closeButton;
        private IKeyEvent _markKeyEvent;
        private IKeyEvent _spendKeyEvent;
        private string _settingsPath;
        private int _selectedCoreRow = -1;
        private int _legacySelectedCoreRow = -1;
        private string _activeProfileKey;
        private string _activeProfileLabel;
        private readonly Dictionary<string, ProfileSelection> _profileSelections = new Dictionary<string, ProfileSelection>(StringComparer.OrdinalIgnoreCase);
        private string _lastStatus;
        private int _statusUntilTick = NoTick;
        private IBrush _panelBackBrush;
        private IBrush _panelBorderBrush;
        private IBrush _hoverBrush;
        private IBrush _pillBorderBrush;
        private IBrush _pillDarkBrush;
        private IBrush _pillLightBrush;
        private IBrush _pillGreenBrush;
        private IBrush _pillGreenLightBrush;
        private IBrush _selectionArrowBrush;
        private IBrush _selectionArrowOutlineBrush;
        private IBrush _selectionArrowLightBrush;
        private IBrush _selectionArrowShadowBrush;
        private IFont _font;
        private IFont _smallFont;
        private IFont _yellowFont;
        private bool _geometryDrawFailed;
        private bool _captureMarkHotkey;
        private bool _captureSpendHotkey;
        private System.Drawing.RectangleF _panelRect = System.Drawing.RectangleF.Empty;
        private System.Drawing.RectangleF _markHotkeyRect = System.Drawing.RectangleF.Empty;
        private System.Drawing.RectangleF _spendHotkeyRect = System.Drawing.RectangleF.Empty;
        private SpendState _state = SpendState.Idle;
        private int _nextTick;
        private int _clicks;
        private bool _changed;
        private bool _restoreCursorCaptured;
        private int _restoreCursorX;
        private int _restoreCursorY;

        public s7o_ParagonHelper()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            ResolveSettingsPath();
            LoadSettings();
            RegisterHotkeys();
            RegisterUiElements();
            BuildResources();
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed)
                return;

            if (_captureMarkHotkey || _captureSpendHotkey)
            {
                CaptureHotkey(keyEvent.Key);
                return;
            }

            if (Matches(_markKeyEvent, keyEvent, MarkHotkey))
            {
                ToggleCoreSelection();
                return;
            }

            if (Matches(_spendKeyEvent, keyEvent, SpendHotkey))
            {
                if (_state != SpendState.Idle)
                {
                    Stop("Core spend cancelled");
                    return;
                }

                StartCoreSpend();
            }
        }

        public void AfterCollect()
        {
            if (!Enabled)
                return;

            if (!IsParagonOpen())
            {
                _captureMarkHotkey = false;
                _captureSpendHotkey = false;
                if (_state != SpendState.Idle)
                    Stop("Paragon menu closed");
                return;
            }

            if (_state != SpendState.Idle)
                ProcessCoreSpend();
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!Enabled || !ShowOverlay || clipState != ClipState.AfterClip || !IsParagonOpen())
                return;

            DrawHoveredCoreRow();
            DrawSelectedArrow();
            DrawPanel();
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left || !ShowOverlay || !IsParagonOpen())
                return false;

            UpdatePanelLayout();
            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;

            if (PointInRect(_markHotkeyRect, x, y))
            {
                if (_state == SpendState.Idle)
                {
                    _captureMarkHotkey = true;
                    _captureSpendHotkey = false;
                    ShowStatus("press F2 replacement", StatusHoldMs);
                }
                return true;
            }

            if (PointInRect(_spendHotkeyRect, x, y))
            {
                if (_state == SpendState.Idle)
                {
                    _captureSpendHotkey = true;
                    _captureMarkHotkey = false;
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

        private void ToggleCoreSelection()
        {
            if (!IsParagonOpen())
                return;

            EnsureActiveProfile();

            if (!IsCoreTabActive())
            {
                ShowStatus("open Core tab, hover Primary Stat or Vitality", StatusHoldMs);
                return;
            }

            int row = GetHoveredRow();
            if (row != PrimaryStatRow && row != VitalityRow)
            {
                ShowStatus("Core-only: mark Primary Stat or Vitality", StatusHoldMs);
                return;
            }

            if (_selectedCoreRow == row)
            {
                _selectedCoreRow = -1;
                ShowStatus("unmarked " + CoreRowNames[row], StatusHoldMs);
            }
            else
            {
                _selectedCoreRow = row;
                ShowStatus("marked " + CoreRowNames[row], StatusHoldMs);
            }

            UpdateActiveProfileSelection();
            SaveSettings();
        }

        private void StartCoreSpend()
        {
            if (!IsParagonOpen())
                return;

            EnsureActiveProfile();
            CaptureCursorForRestore();
            _state = IsCoreTabActive() ? SpendState.WaitRows : SpendState.OpenCoreTab;
            _nextTick = Environment.TickCount;
            _clicks = 0;
            _changed = false;

            if (_selectedCoreRow == PrimaryStatRow || _selectedCoreRow == VitalityRow)
                ShowStatus("checking Core / " + CoreRowNames[_selectedCoreRow] + "...", StatusHoldMs);
            else
                ShowStatus("checking Core points...", StatusHoldMs);
        }

        private void ProcessCoreSpend()
        {
            if (!IsParagonOpen())
            {
                Stop("Paragon menu closed");
                return;
            }

            int now = Environment.TickCount;
            if (!TickReachedOrUnset(now, _nextTick))
                return;

            if (_clicks >= SpendClickLimit)
            {
                Stop("Core spend stopped: click limit");
                return;
            }

            if (_state == SpendState.OpenCoreTab)
            {
                if (IsCoreTabActive())
                {
                    _state = SpendState.WaitRows;
                    _nextTick = now + RowSettleMs;
                    return;
                }

                if (ClickUi(_coreTab, false))
                {
                    _clicks++;
                    _state = SpendState.WaitRows;
                    _nextTick = now + TabSettleMs;
                }
                else
                {
                    Stop("could not open Core tab");
                }
                return;
            }

            if (_state == SpendState.WaitRows)
            {
                if (!IsCoreTabActive())
                {
                    _state = SpendState.OpenCoreTab;
                    _nextTick = now;
                    return;
                }

                if (!RowsLoaded())
                {
                    _nextTick = now + RowSettleMs;
                    return;
                }

                _state = SpendState.SpendCore;
                _nextTick = now;
                return;
            }

            if (_state == SpendState.SpendCore)
            {
                int available = ParseCoreAvailable();
                if (available <= 0)
                {
                    _state = _changed ? SpendState.Accept : SpendState.CloseNoSpend;
                    _nextTick = now + (_changed ? AcceptDelayMs : CloseDelayMs);
                    return;
                }

                if (_selectedCoreRow != PrimaryStatRow && _selectedCoreRow != VitalityRow)
                {
                    _state = SpendState.CloseNoSpend;
                    _nextTick = now + CloseDelayMs;
                    ShowStatus("no Core selection; closing", StatusHoldMs);
                    return;
                }

                RowState row = GetRowState(_selectedCoreRow);
                if (!row.Valid)
                {
                    _nextTick = now + RowSettleMs;
                    return;
                }

                if (row.IsCapped || !IsVisible(_plusButtons[_selectedCoreRow]))
                {
                    _state = SpendState.CloseNoSpend;
                    _nextTick = now + CloseDelayMs;
                    ShowStatus("selected Core row is not spendable; closing", StatusHoldMs);
                    return;
                }

                if (ClickUi(_plusButtons[_selectedCoreRow], true))
                {
                    _clicks++;
                    _changed = true;
                    _nextTick = now + SpendDelayMs;
                    ShowStatus("spent Core into " + CoreRowNames[_selectedCoreRow], StatusHoldMs);
                }
                else
                {
                    Stop("could not click Core plus button");
                }
                return;
            }

            if (_state == SpendState.CloseNoSpend)
            {
                if (ClickClose())
                {
                    _clicks++;
                    Stop("no Core points; closed");
                }
                else
                {
                    Stop("no Core points; close manually");
                }
                return;
            }

            if (_state == SpendState.Accept)
            {
                if (!_changed)
                {
                    Stop("no Core points to spend");
                    return;
                }

                if (!AutoAcceptAfterSpending)
                {
                    Stop("spent Core; accept manually");
                    return;
                }

                if (ClickUi(_acceptButton, false))
                {
                    _clicks++;
                    Stop("spent Core and accepted");
                    return;
                }

                Stop("spent Core; Accept button not clickable");
            }
        }

        private void Stop(string status)
        {
            _state = SpendState.Idle;
            _changed = false;
            _captureMarkHotkey = false;
            _captureSpendHotkey = false;
            ReleaseCtrl();
            RestoreCapturedCursor();
            ShowStatus(status, StatusHoldMs);
        }


        private void CaptureCursorForRestore()
        {
            _restoreCursorCaptured = false;
            if (!RestoreCursorAfterAction)
                return;

            try
            {
                POINT point;
                if (GetCursorPos(out point))
                {
                    _restoreCursorX = point.X;
                    _restoreCursorY = point.Y;
                    _restoreCursorCaptured = true;
                    return;
                }
            }
            catch { }

            try
            {
                _restoreCursorX = Hud.Window.CursorX;
                _restoreCursorY = Hud.Window.CursorY;
                _restoreCursorCaptured = true;
            }
            catch { _restoreCursorCaptured = false; }
        }

        private void RestoreCapturedCursor()
        {
            if (!_restoreCursorCaptured || !RestoreCursorAfterAction)
                return;

            _restoreCursorCaptured = false;
            try { SetCursorPos(_restoreCursorX, _restoreCursorY); } catch { }
        }

        private bool IsParagonOpen()
        {
            try
            {
                return Hud != null && Hud.Game != null && Hud.Game.IsInGame && !Hud.Game.IsLoading
                    && Hud.Window != null && Hud.Window.IsForeground
                    && _pointSelect != null && _pointSelect.Visible
                    && _pointSelect.Rectangle.Width > 0 && _pointSelect.Rectangle.Height > 0;
            }
            catch { return false; }
        }

        private bool IsCoreTabActive()
        {
            return IsVisible(_coreTab) && SafeAnim(_coreTab) == ActiveTabAnim;
        }

        private int GetHoveredRow()
        {
            try
            {
                int x = Hud.Window.CursorX;
                int y = Hud.Window.CursorY;
                for (int row = 0; row < 4; row++)
                {
                    var r = GetRowRect(row);
                    if (PointInRect(r, x, y))
                        return row;
                }
            }
            catch { }
            return -1;
        }

        private System.Drawing.RectangleF GetRowRect(int row)
        {
            if (row >= 0 && row < _rows.Length && IsVisible(_rows[row]))
                return _rows[row].Rectangle;
            return row >= 0 && row < NativeRowRects.Length ? ScaleRect(NativeRowRects[row]) : System.Drawing.RectangleF.Empty;
        }

        private System.Drawing.RectangleF GetArrowRect(int row)
        {
            return row >= 0 && row < NativeArrowRects.Length ? ScaleRect(NativeArrowRects[row]) : System.Drawing.RectangleF.Empty;
        }

        private System.Drawing.RectangleF ScaleRect(System.Drawing.RectangleF native)
        {
            float sx = 1.0f, sy = 1.0f;
            try
            {
                if (Hud.Window != null && Hud.Window.Size.Width > 0 && Hud.Window.Size.Height > 0)
                {
                    sx = Hud.Window.Size.Width / 1920.0f;
                    sy = Hud.Window.Size.Height / 1080.0f;
                }
            }
            catch { }
            return new System.Drawing.RectangleF(native.X * sx, native.Y * sy, native.Width * sx, native.Height * sy);
        }

        private bool RowsLoaded()
        {
            return !string.IsNullOrEmpty(PointsSpentText(PrimaryStatRow)) || !string.IsNullOrEmpty(PointsSpentText(VitalityRow));
        }

        private int ParseCoreAvailable()
        {
            try
            {
                if (!IsVisible(_coreAvailable))
                    return 0;
                int value;
                return TryParseFirstInt(Clean(_coreAvailable.ReadText(Encoding.UTF8, true)), out value) ? value : 0;
            }
            catch { return 0; }
        }

        private RowState GetRowState(int row)
        {
            RowState state = new RowState();
            string text = PointsSpentText(row);
            if (string.IsNullOrEmpty(text))
                return state;

            int slash = text.IndexOf('/');
            if (slash >= 0)
            {
                int assigned, cap;
                if (TryParseFirstInt(text.Substring(0, slash), out assigned) && TryParseFirstInt(text.Substring(slash + 1), out cap))
                {
                    state.Valid = true;
                    state.Assigned = assigned;
                    state.Cap = cap;
                    state.HasCap = true;
                    state.IsCapped = cap > 0 && assigned >= cap;
                }
                return state;
            }

            int value;
            if (TryParseFirstInt(text, out value))
            {
                state.Valid = true;
                state.Assigned = value;
            }
            return state;
        }

        private string PointsSpentText(int row)
        {
            try
            {
                if (row < 0 || row >= _pointsSpent.Length || !IsVisible(_pointsSpent[row]))
                    return string.Empty;
                return Clean(_pointsSpent[row].ReadText(Encoding.UTF8, true));
            }
            catch { return string.Empty; }
        }

        private bool ClickUi(IUiElement element, bool ctrl)
        {
            if (!IsParagonOpen() || !IsVisible(element))
                return false;

            try
            {
                var r = element.Rectangle;
                int x = (int)Math.Round(r.X + r.Width * 0.5f);
                int y = (int)Math.Round(r.Y + r.Height * 0.5f);
                return ClickAt(x, y, ctrl);
            }
            catch { return false; }
        }

        private bool ClickClose()
        {
            if (ClickUi(_closeButton, false))
                return true;

            return ClickRect(ScaleRect(NativeCloseRect), false);
        }

        private bool ClickRect(System.Drawing.RectangleF r, bool ctrl)
        {
            if (!IsParagonOpen() || r.Width <= 0 || r.Height <= 0)
                return false;

            int x = (int)Math.Round(r.X + r.Width * 0.5f);
            int y = (int)Math.Round(r.Y + r.Height * 0.5f);
            return ClickAt(x, y, ctrl);
        }

        private bool ClickAt(int x, int y, bool ctrl)
        {
            if (!IsSafeParagonClick(x, y))
                return false;

            try
            {
                if (ctrl) PressCtrl();
                SetCursorPos(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            catch { return false; }
            finally
            {
                if (ctrl) ReleaseCtrl();
            }
        }

        private bool IsSafeParagonClick(int x, int y)
        {
            try
            {
                if (_pointSelect == null)
                    return false;
                var r = _pointSelect.Rectangle;
                return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
            }
            catch { return false; }
        }

        private bool IsVisible(IUiElement element)
        {
            try { return element != null && element.Visible && element.Rectangle.Width > 0 && element.Rectangle.Height > 0; }
            catch { return false; }
        }

        private int SafeAnim(IUiElement element)
        {
            try { return element != null ? element.AnimState : -999; }
            catch { return -999; }
        }

        private void DrawPanel()
        {
            if (!UpdatePanelLayout())
                return;

            if (_panelBackBrush != null) _panelBackBrush.DrawRectangle(_panelRect);

            float y = _panelRect.Y + 7.0f;
            float gap = 56.0f;
            float contentW = MeasurePromptItemWidth(_captureMarkHotkey ? "..." : MarkHotkey.ToString(), "on Core stat/Vitality")
                + gap
                + MeasurePromptItemWidth(_captureSpendHotkey ? "..." : SpendHotkey.ToString(), "to spend/close");
            float x = _panelRect.X + (_panelRect.Width - contentW) * 0.5f;

            DrawPromptItem(x, y, _captureMarkHotkey ? "..." : MarkHotkey.ToString(), _captureMarkHotkey, "on Core stat/Vitality", out _markHotkeyRect);
            x = _markHotkeyRect.Right + 10.0f + MeasureTextWidth(_yellowFont, "on Core stat/Vitality") + gap;
            DrawPromptItem(x, y, _captureSpendHotkey ? "..." : SpendHotkey.ToString(), _captureSpendHotkey || _state != SpendState.Idle, "to spend/close", out _spendHotkeyRect);

            string status = GetStatusText();
            if (!string.IsNullOrEmpty(status) && _smallFont != null)
                _smallFont.DrawText(status, _panelRect.Right + 10.0f, _panelRect.Y + 10.0f);
        }

        private bool UpdatePanelLayout()
        {
            if (!IsParagonOpen())
                return false;

            try
            {
                var anchor = _pointSelect.Rectangle;
                if (anchor.Width <= 0 || anchor.Height <= 0)
                    return false;

                float gap = 56.0f;
                float contentW = MeasurePromptItemWidth(_captureMarkHotkey ? "..." : MarkHotkey.ToString(), "on Core stat/Vitality")
                    + gap
                    + MeasurePromptItemWidth(_captureSpendHotkey ? "..." : SpendHotkey.ToString(), "to spend/close");
                float w = Math.Min(anchor.Width - 80.0f, Math.Max(620.0f, contentW + 52.0f));
                float h = 32.0f;
                float x = anchor.X + (anchor.Width - w) * 0.5f;
                float y = anchor.Y + anchor.Height * 0.715f;

                _panelRect = new System.Drawing.RectangleF(x, y, w, h);

                float itemY = _panelRect.Y + 7.0f;
                float itemX = _panelRect.X + (_panelRect.Width - contentW) * 0.5f;
                _markHotkeyRect = LayoutPromptHotkeyRect(itemX, itemY, _captureMarkHotkey ? "..." : MarkHotkey.ToString());
                itemX = _markHotkeyRect.Right + 10.0f + MeasureTextWidth(_yellowFont, "on Core stat/Vitality") + gap;
                _spendHotkeyRect = LayoutPromptHotkeyRect(itemX, itemY, _captureSpendHotkey ? "..." : SpendHotkey.ToString());
                return true;
            }
            catch
            {
                _panelRect = System.Drawing.RectangleF.Empty;
                _markHotkeyRect = System.Drawing.RectangleF.Empty;
                _spendHotkeyRect = System.Drawing.RectangleF.Empty;
                return false;
            }
        }

        private float MeasurePromptItemWidth(string hotkeyText, string tailText)
        {
            return Math.Max(42.0f, MeasureTextWidth(_font, hotkeyText) + 16.0f) + 10.0f + MeasureTextWidth(_yellowFont, tailText);
        }

        private void DrawPromptItem(float x, float y, string hotkeyText, bool active, string tailText, out System.Drawing.RectangleF hotkeyRect)
        {
            hotkeyRect = LayoutPromptHotkeyRect(x, y, hotkeyText);
            DrawPillButton(hotkeyRect, hotkeyText, active);
            DrawText(_yellowFont, tailText, hotkeyRect.Right + 10.0f, y);
        }

        private System.Drawing.RectangleF LayoutPromptHotkeyRect(float x, float y, string hotkeyText)
        {
            float keyW = Math.Max(42.0f, MeasureTextWidth(_font, hotkeyText) + 16.0f);
            return new System.Drawing.RectangleF(x, y - 2.0f, keyW, 20.0f);
        }

        private void DrawPillButton(System.Drawing.RectangleF rect, string text, bool green)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, null, _pillBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? _pillGreenBrush : _pillDarkBrush, null);

            var hi = new System.Drawing.RectangleF(inner.X + 1.0f, inner.Y + 1.0f, Math.Max(0.0f, inner.Width - 2.0f), inner.Height * 0.42f);
            DrawRoundedRect(hi, hi.Height * 0.5f, green ? _pillGreenLightBrush : _pillLightBrush, null);

            DrawCenteredText(_font, rect, text);
        }

        private void DrawText(IFont font, string text, float x, float y)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            font.DrawText(text, x, y);
        }

        private void DrawCenteredText(IFont font, System.Drawing.RectangleF rect, string text)
        {
            if (font == null || string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0) return;
            try
            {
                var layout = font.GetTextLayout(text);
                font.DrawText(layout, rect.X + (rect.Width - layout.Metrics.Width) * 0.5f, rect.Y + (rect.Height - layout.Metrics.Height) * 0.5f);
            }
            catch { font.DrawText(text, rect.X + 4.0f, rect.Y + 4.0f); }
        }

        private void DrawRoundedRect(System.Drawing.RectangleF rect, float radius, IBrush fill, IBrush border)
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

        private static void BeginRoundedRectFigure(GeometrySink gs, System.Drawing.RectangleF r, float radius)
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

        private static System.Drawing.RectangleF InsetRect(System.Drawing.RectangleF rect, float amount)
        {
            return new System.Drawing.RectangleF(rect.X + amount, rect.Y + amount, Math.Max(0.0f, rect.Width - amount * 2.0f), Math.Max(0.0f, rect.Height - amount * 2.0f));
        }

        private float MeasureTextWidth(IFont font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
                return 0.0f;

            try { return font.GetTextLayout(text).Metrics.Width; }
            catch { return Math.Max(0, text.Length) * 6.0f; }
        }

        private void DrawSelectedArrow()
        {
            EnsureActiveProfile();
            if (!IsCoreTabActive() || _selectedCoreRow < 0)
                return;

            var row = GetRowRect(_selectedCoreRow);
            if (row.Width <= 0 || row.Height <= 0)
                return;

            float ui = Math.Max(0.75f, Math.Min(1.45f, row.Height / 90.0f));
            const float markerScale = 0.75f;
            float length = 58.0f * ui * markerScale;
            float shaftWidth = 16.0f * ui * markerScale;
            float headLength = 23.0f * ui * markerScale;
            float headWidth = 38.0f * ui * markerScale;
            float outline = 5.0f * ui * markerScale;
            float baseX = row.Right - 8.0f * ui;
            float baseY = row.Top + row.Height * 0.50f;

            DrawSelectionArrow(baseX, baseY, -1.0f, 0.0f, 0.0f, -1.0f, length, shaftWidth, headLength, headWidth, outline);
        }

        private void DrawSelectionArrow(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, float outline)
        {
            if (_selectionArrowBrush == null || _selectionArrowOutlineBrush == null)
                return;

            try
            {
                if (_selectionArrowShadowBrush != null)
                    DrawSingleArrowGeometry(baseX + 3.0f, baseY + 4.0f, ux, uy, px, py, length + outline * 2.3f, shaftWidth + outline * 1.8f, headLength + outline * 2.0f, headWidth + outline * 2.1f, _selectionArrowShadowBrush);

                DrawSingleArrowGeometry(baseX + outline, baseY, ux, uy, px, py, length + outline * 2.2f, shaftWidth + outline * 1.8f, headLength + outline * 2.0f, headWidth + outline * 2.1f, _selectionArrowOutlineBrush);
                DrawSingleArrowGeometry(baseX, baseY, ux, uy, px, py, length, shaftWidth, headLength, headWidth, _selectionArrowBrush);

                if (_selectionArrowShadowBrush != null)
                    DrawArrowLowerBevel(baseX, baseY, ux, uy, px, py, length, shaftWidth, headLength, headWidth, _selectionArrowShadowBrush);
                if (_selectionArrowLightBrush != null)
                    DrawArrowHighlight(baseX, baseY, ux, uy, px, py, length, shaftWidth, headLength, headWidth, _selectionArrowLightBrush);
            }
            catch { }
        }

        private void DrawSingleArrowGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(baseX - px * halfShaft, baseY - py * halfShaft), FigureBegin.Filled);
                    sink.AddLine(new Vector2(baseX + ux * shaftLength - px * halfShaft, baseY + uy * shaftLength - py * halfShaft));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength - px * halfHead, baseY + uy * shaftLength - py * halfHead));
                    sink.AddLine(new Vector2(baseX + ux * length, baseY + uy * length));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * halfHead, baseY + uy * shaftLength + py * halfHead));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * halfShaft, baseY + uy * shaftLength + py * halfShaft));
                    sink.AddLine(new Vector2(baseX + px * halfShaft, baseY + py * halfShaft));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawArrowLowerBevel(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float bevel = Math.Max(4.0f, Math.Min(shaftWidth * 0.30f, 10.0f));
            float inset = Math.Max(2.0f, shaftWidth * 0.10f);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(baseX + px * (halfShaft - inset), baseY + py * (halfShaft - inset)), FigureBegin.Filled);
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset), baseY + uy * shaftLength + py * (halfShaft - inset)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfHead - inset), baseY + uy * shaftLength + py * (halfHead - inset)));
                    sink.AddLine(new Vector2(baseX + ux * (length - Math.Max(8.0f, headLength * 0.18f)) + px * Math.Max(1.5f, bevel * 0.35f), baseY + uy * (length - Math.Max(8.0f, headLength * 0.18f)) + py * Math.Max(1.5f, bevel * 0.35f)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel), baseY + uy * shaftLength + py * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel)));
                    sink.AddLine(new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset - bevel), baseY + uy * shaftLength + py * (halfShaft - inset - bevel)));
                    sink.AddLine(new Vector2(baseX + px * (halfShaft - inset - bevel), baseY + py * (halfShaft - inset - bevel)));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawArrowHighlight(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float yTop = -halfShaft + Math.Max(4.0f, shaftWidth * 0.22f);
            float thickness = Math.Max(2.0f, Math.Min(4.0f, shaftWidth * 0.14f));
            float start = Math.Max(6.0f, shaftWidth * 0.35f);
            float end = Math.Max(start + 6.0f, shaftLength - Math.Max(3.0f, headLength * 0.12f));

            FillArrowQuad(baseX, baseY, ux, uy, px, py, start, yTop, end, yTop, end, yTop + thickness, start, yTop + thickness, brush);

            float diagStartX = shaftLength + Math.Max(2.0f, headLength * 0.12f);
            float diagEndX = length - Math.Max(8.0f, headLength * 0.16f);
            float diagStartY = -halfHead + Math.Max(6.0f, headWidth * 0.18f);
            float diagEndY = -Math.Max(2.0f, headWidth * 0.06f);
            FillArrowQuad(baseX, baseY, ux, uy, px, py, diagStartX, diagStartY, diagEndX, diagEndY, diagEndX, diagEndY + thickness, diagStartX, diagStartY + thickness, brush);
        }

        private void FillArrowQuad(float baseX, float baseY, float ux, float uy, float px, float py, float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, IBrush brush)
        {
            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(baseX + ux * ax + px * ay, baseY + uy * ax + py * ay), FigureBegin.Filled);
                    sink.AddLine(new Vector2(baseX + ux * bx + px * by, baseY + uy * bx + py * by));
                    sink.AddLine(new Vector2(baseX + ux * cx + px * cy, baseY + uy * cx + py * cy));
                    sink.AddLine(new Vector2(baseX + ux * dx + px * dy, baseY + uy * dx + py * dy));
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawHoveredCoreRow()
        {
            if (!IsCoreTabActive())
                return;

            int row = GetHoveredRow();
            if ((row == PrimaryStatRow || row == VitalityRow) && _hoverBrush != null)
            {
                var r = GetRowRect(row);
                if (r.Width > 0 && r.Height > 0)
                    _hoverBrush.DrawRectangle(r);
            }
        }

        private void RegisterHotkeys()
        {
            try { _markKeyEvent = Hud.Input.CreateKeyEvent(true, MarkHotkey, false, false, false); } catch { _markKeyEvent = null; }
            try { _spendKeyEvent = Hud.Input.CreateKeyEvent(true, SpendHotkey, false, false, false); } catch { _spendKeyEvent = null; }
        }

        private void CaptureHotkey(Key key)
        {
            if (key == Key.Unknown)
                return;

            if (!IsParagonOpen())
            {
                _captureMarkHotkey = false;
                _captureSpendHotkey = false;
                return;
            }

            if (_captureMarkHotkey)
            {
                MarkHotkey = key;
                ShowStatus("mark hotkey: " + key.ToString(), StatusHoldMs);
            }
            else if (_captureSpendHotkey)
            {
                SpendHotkey = key;
                ShowStatus("spend hotkey: " + key.ToString(), StatusHoldMs);
            }

            _captureMarkHotkey = false;
            _captureSpendHotkey = false;
            RegisterHotkeys();
            SaveSettings();
        }

        private static bool Matches(IKeyEvent registered, IKeyEvent actual, Key fallback)
        {
            if (registered != null && registered.Matches(actual)) return true;
            return actual != null && actual.Key == fallback;
        }

        private void RegisterUiElements()
        {
            _pointSelect = Hud.Render.RegisterUiElement(BasePath, null, null);
            _coreTab = Hud.Render.RegisterUiElement(BasePath + ".tab_1", _pointSelect, null);
            _coreAvailable = Hud.Render.RegisterUiElement(BasePath + ".Points_Available_1", _pointSelect, null);
            _acceptButton = Hud.Render.RegisterUiElement(BasePath + ".AcceptParagonPointsButton", _pointSelect, null);
            _closeButton = Hud.Render.RegisterUiElement(BasePath + ".CloseButton", _pointSelect, null);

            for (int row = 0; row < 4; row++)
            {
                string rowPath = BasePath + ".Bonuses.bonus" + row.ToString(CultureInfo.InvariantCulture);
                _rows[row] = Hud.Render.RegisterUiElement(rowPath, _pointSelect, null);
                _pointsSpent[row] = Hud.Render.RegisterUiElement(rowPath + ".PointsSpent", _pointSelect, null);
                _plusButtons[row] = Hud.Render.RegisterUiElement(rowPath + ".IncreaseStat", _pointSelect, null);
            }
        }

        private void BuildResources()
        {
            _panelBackBrush = Hud.Render.CreateBrush(175, 0, 0, 0, 0);
            _panelBorderBrush = Hud.Render.CreateBrush(210, 180, 120, 30, 1.2f);
            _hoverBrush = Hud.Render.CreateBrush(120, 255, 210, 0, 1.5f);
            _pillBorderBrush = Hud.Render.CreateBrush(230, 245, 150, 30, 1.2f);
            _pillDarkBrush = Hud.Render.CreateBrush(230, 18, 18, 18, 0);
            _pillLightBrush = Hud.Render.CreateBrush(80, 255, 255, 255, 0);
            _pillGreenBrush = Hud.Render.CreateBrush(220, 24, 105, 38, 0);
            _pillGreenLightBrush = Hud.Render.CreateBrush(75, 150, 255, 150, 0);
            _selectionArrowBrush = Hud.Render.CreateBrush(250, 80, 255, 110, 0);
            _selectionArrowOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 0);
            _selectionArrowLightBrush = Hud.Render.CreateBrush(95, 240, 255, 240, 0);
            _selectionArrowShadowBrush = Hud.Render.CreateBrush(115, 0, 0, 0, 0);
            _font = Hud.Render.CreateFont("tahoma", 7.6f, 255, 255, 255, 255, true, false, 255, 0, 0, 0, true);
            _smallFont = Hud.Render.CreateFont("tahoma", 7.6f, 255, 235, 235, 235, false, false, 255, 0, 0, 0, true);
            _yellowFont = Hud.Render.CreateFont("tahoma", 8.8f, 255, 255, 220, 64, true, false, 255, 0, 0, 0, true);
        }

        private void ResolveSettingsPath()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings");
                Directory.CreateDirectory(dir);
                _settingsPath = Path.Combine(dir, SettingsFileName);
            }
            catch { _settingsPath = null; }
        }

        private void EnsureActiveProfile()
        {
            string key = GetProfileKey();
            if (string.IsNullOrEmpty(key))
                key = "fallback:unknown";

            if (string.Equals(_activeProfileKey, key, StringComparison.OrdinalIgnoreCase))
                return;

            _activeProfileKey = key;
            _activeProfileLabel = GetProfileLabel();
            ProfileSelection profile;
            if (_profileSelections.TryGetValue(key, out profile))
            {
                _selectedCoreRow = IsSupportedCoreRow(profile.Row) ? profile.Row : -1;
                profile.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
                profile.Label = _activeProfileLabel;
                _profileSelections[key] = profile;
            }
            else
            {
                _selectedCoreRow = IsSupportedCoreRow(_legacySelectedCoreRow) ? _legacySelectedCoreRow : -1;
                UpdateActiveProfileSelection();
            }
        }

        private void UpdateActiveProfileSelection()
        {
            if (string.IsNullOrEmpty(_activeProfileKey))
                return;

            _profileSelections[_activeProfileKey] = new ProfileSelection
            {
                Row = IsSupportedCoreRow(_selectedCoreRow) ? _selectedCoreRow : -1,
                LastSeenUtcTicks = DateTime.UtcNow.Ticks,
                Label = string.IsNullOrEmpty(_activeProfileLabel) ? GetProfileLabel() : _activeProfileLabel
            };

            PruneSavedProfiles();
        }

        private void PruneSavedProfiles()
        {
            if (_profileSelections.Count <= MaxSavedProfiles)
                return;

            while (_profileSelections.Count > MaxSavedProfiles)
            {
                string oldestKey = null;
                long oldestTicks = long.MaxValue;
                foreach (var pair in _profileSelections)
                {
                    if (pair.Value.LastSeenUtcTicks < oldestTicks)
                    {
                        oldestTicks = pair.Value.LastSeenUtcTicks;
                        oldestKey = pair.Key;
                    }
                }

                if (string.IsNullOrEmpty(oldestKey))
                    break;

                _profileSelections.Remove(oldestKey);
            }
        }

        private string GetProfileKey()
        {
            try
            {
                var me = Hud.Game.Me;
                if (me != null)
                {
                    if (me.HeroId != 0)
                        return "hero:" + me.HeroId.ToString(CultureInfo.InvariantCulture);

                    string classCode = me.HeroClassDefinition != null ? (me.HeroClassDefinition.Code ?? me.HeroClassDefinition.Name ?? "class") : "class";
                    string name = me.HeroName ?? "hero";
                    return "fallback:" + CleanProfilePart(classCode) + ":" + CleanProfilePart(name) + ":" + (me.HeroIsHardcore ? "hc" : "sc") + ":" + (me.HeroIsMale ? "m" : "f");
                }
            }
            catch { }

            return "fallback:unknown";
        }

        private string GetProfileLabel()
        {
            try
            {
                var me = Hud.Game.Me;
                if (me != null)
                {
                    string classCode = me.HeroClassDefinition != null ? (me.HeroClassDefinition.Code ?? me.HeroClassDefinition.Name ?? "Hero") : "Hero";
                    string name = string.IsNullOrEmpty(me.HeroName) ? "unnamed" : me.HeroName;
                    string id = me.HeroId != 0 ? " #" + me.HeroId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                    return classCode + " " + name + id;
                }
            }
            catch { }
            return "unknown hero";
        }

        private static bool IsSupportedCoreRow(int row)
        {
            return row == PrimaryStatRow || row == VitalityRow;
        }

        private static string CleanProfilePart(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unknown";
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
            }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        private static string CleanProfileValue(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private bool TryParseProfileLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split('|');
            if (parts.Length < 2)
                return false;

            string key = parts[0].Trim();
            int row;
            if (string.IsNullOrEmpty(key) || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out row))
                return false;

            long ticks = DateTime.UtcNow.Ticks;
            if (parts.Length >= 3)
                long.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks);

            string label = parts.Length >= 4 ? CleanProfileValue(parts[3]) : string.Empty;
            _profileSelections[key] = new ProfileSelection
            {
                Row = IsSupportedCoreRow(row) ? row : -1,
                LastSeenUtcTicks = ticks > 0 ? ticks : DateTime.UtcNow.Ticks,
                Label = label
            };
            return true;
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
                    if (key.Equals("MarkHotkey", StringComparison.OrdinalIgnoreCase)) MarkHotkey = ParseKey(val, MarkHotkey);
                    else if (key.Equals("SpendHotkey", StringComparison.OrdinalIgnoreCase)) SpendHotkey = ParseKey(val, SpendHotkey);
                    else if (key.Equals("RestoreCursorAfterAction", StringComparison.OrdinalIgnoreCase)) RestoreCursorAfterAction = ParseBool(val, RestoreCursorAfterAction);
                    else if (key.Equals("SelectedCoreRow", StringComparison.OrdinalIgnoreCase)) ParseSelectedCoreRow(val);
                    else if (key.Equals("SelectedRows", StringComparison.OrdinalIgnoreCase)) ParseLegacySelectedRows(val);
                    else if (key.Equals("Profile", StringComparison.OrdinalIgnoreCase)) TryParseProfileLine(val);
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
                EnsureActiveProfile();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# s7o_ParagonHelper Core-only settings");
                sb.AppendLine("SettingsVersion=" + SettingsVersion.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("MarkHotkey=" + MarkHotkey.ToString());
                sb.AppendLine("SpendHotkey=" + SpendHotkey.ToString());
                sb.AppendLine("RestoreCursorAfterAction=" + RestoreCursorAfterAction.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("SelectedCoreRow=" + _selectedCoreRow.ToString(CultureInfo.InvariantCulture));
                foreach (var pair in _profileSelections)
                {
                    sb.Append("Profile=");
                    sb.Append(pair.Key);
                    sb.Append('|');
                    sb.Append(pair.Value.Row.ToString(CultureInfo.InvariantCulture));
                    sb.Append('|');
                    sb.Append(pair.Value.LastSeenUtcTicks.ToString(CultureInfo.InvariantCulture));
                    sb.Append('|');
                    sb.AppendLine(CleanProfileValue(pair.Value.Label));
                }
                File.WriteAllText(_settingsPath, sb.ToString());
            }
            catch { }
        }

        private void ParseSelectedCoreRow(string value)
        {
            int row;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out row) && IsSupportedCoreRow(row))
            {
                _legacySelectedCoreRow = row;
                _selectedCoreRow = row;
            }
        }

        private void ParseLegacySelectedRows(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || _selectedCoreRow >= 0)
                return;

            foreach (string part in value.Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2 || kv[0].Trim() != "0") continue;
                ParseSelectedCoreRow(kv[1].Trim());
                return;
            }
        }

        private static Key ParseKey(string value, Key fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return (Key)Enum.Parse(typeof(Key), value, true); }
            catch { return fallback; }
        }


        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            bool parsed;
            return bool.TryParse(value.Trim(), out parsed) ? parsed : fallback;
        }

        private void ShowStatus(string text, int ms)
        {
            _lastStatus = text;
            _statusUntilTick = unchecked(Environment.TickCount + Math.Max(0, ms));
        }

        private string GetStatusText()
        {
            if (!string.IsNullOrEmpty(_lastStatus) && TickIsFuture(Environment.TickCount, _statusUntilTick))
                return _lastStatus;
            return null;
        }

        private static bool TickReachedOrUnset(int now, int tick)
        {
            return tick == 0 || tick == NoTick || unchecked(now - tick) >= 0;
        }

        private static bool TickIsFuture(int now, int untilTick)
        {
            return untilTick != 0 && untilTick != NoTick && unchecked(now - untilTick) < 0;
        }

        private static bool PointInRect(System.Drawing.RectangleF r, int x, int y)
        {
            return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private static bool TryParseFirstInt(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return false;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return int.TryParse(text.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text;
        }

        private static void PressCtrl()
        {
            try { keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); } catch { }
        }

        private static void ReleaseCtrl()
        {
            try { keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); } catch { }
        }

        private enum SpendState
        {
            Idle,
            OpenCoreTab,
            WaitRows,
            SpendCore,
            CloseNoSpend,
            Accept
        }

        private struct ProfileSelection
        {
            public int Row;
            public long LastSeenUtcTicks;
            public string Label;
        }

        private struct RowState
        {
            public bool Valid;
            public int Assigned;
            public int Cap;
            public bool HasCap;
            public bool IsCapped;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
