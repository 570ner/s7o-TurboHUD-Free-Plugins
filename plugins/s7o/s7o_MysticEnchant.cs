namespace Turbo.Plugins.s7o
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Windows.Forms;
    using SharpDX.Direct2D1;
    using SharpDX.DirectInput;
    using Vector2 = SharpDX.Vector2;
    using Turbo.Plugins.Default;

    internal static class s7o_MysticEnchantInput
    {
        private const uint InputMouse = 0;
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint RightDown = 0x0008;
        private const uint RightUp = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

        public static bool TryGetCursorPos(out int x, out int y)
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                x = p.X;
                y = p.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        public static bool MouseMove(int x, int y)
        {
            return SetCursorPos(x, y);
        }

        public static bool MouseDown(MouseButtons button)
        {
            if (button == MouseButtons.Left) return SendMouse(LeftDown);
            if (button == MouseButtons.Right) return SendMouse(RightDown);
            return false;
        }

        public static bool MouseUp(MouseButtons button)
        {
            if (button == MouseButtons.Left) return SendMouse(LeftUp);
            if (button == MouseButtons.Right) return SendMouse(RightUp);
            return false;
        }

        public static bool ClickRect(RectangleF rect, MouseButtons button)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return false;

            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);

            if (!MouseMove(x, y))
                return false;

            if (!MouseDown(button))
                return false;

            return MouseUp(button);
        }

        private static bool SendMouse(uint flags)
        {
            var input = new Input[1];

            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;
            input[0].U.Mouse.Dx = 0;
            input[0].U.Mouse.Dy = 0;
            input[0].U.Mouse.MouseData = 0;
            input[0].U.Mouse.Time = 0;
            input[0].U.Mouse.ExtraInfo = IntPtr.Zero;

            return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
        }
    }

    public class s7o_MysticEnchantPlugin : BasePlugin,
        IKeyEventHandler,
        IAfterCollectHandler,
        IInGameTopPainter,
        IMouseClickHandler,
        INewAreaHandler
    {
        // ── Mystic Enchant Automation ──────────────────────────────────────────────

        // Default hotkey. This is independent from TurboCube and ItemSalvage.
        public Key MysticEnchantHotkey = Key.F3;

        // Change this if you want more or fewer automatic Mystic attempts.
        // 100 is the default safety limit.
        // Set to 0 to keep rerolling until the target stat reaches max,
        // the Mystic UI closes, materials run out, or you manually stop it.
        public int MaxEnchantAttempts = 100;

        // Optional material/gold reserves.
        // First pass primarily uses the same low-risk gold guard as the original.
        // Material helpers are isolated below for later expansion/refinement.
        public int ReservedArcaneDust = 0;
        public int ReservedVeiledCrystal = 0;
        public int ReservedForgottenSoul = 0;
        public int ReservedDeathsBreath = 0;
        public int ReservedGem = 0;
        public long ReservedGold = 0;

        // Material parsing can vary by UI state/localization.
        // Keep disabled by default; the Mystic button itself prevents impossible rolls.
        public bool EnforceMaterialReserveChecks = false;

        // Gold reserve check only applies if ReservedGold > 0.
        public bool EnforceGoldReserveCheck = true;

        // Mystic timing is controlled by the editable timing values below.
        // No in-game Turbo/speed UI is shown for Mystic.
        public bool ShowMysticSpeedControls = false;

        // Debug.
        public bool DebugLogEnabled = false;
        public bool VerboseDebugLogging = false;
        public string DebugLogFileName = "MysticEnchantDebug.log";

        // Settings persistence.
        public bool PersistUserSettings = true;

        // UI style/layout. Match final TurboCube / ItemSalvage style.
        public bool UseRoundedGeometryButtons = true;

        public float HeaderLeftOffset = 82.0f;
        public float HeaderTopOffset = 38.0f;
        public float HeaderRightOffset = 74.0f;

        // Right-side attempts display. The label is plain text; the pill below is - / value / +.
        public float AttemptToggleWidth = 112.0f;
        public float AttemptToggleHeight = 18.0f;
        public float AttemptToggleRightOffset = 74.0f;
        public float AttemptToggleTopOffset = 38.0f;

        // Used if the settings file has an invalid finite value.
        public int DefaultFiniteAttemptLimit = 100;

        public float OverlayTextLeftOffset = 24.0f;
        public float OverlayTextTopGap = 44.0f;

        public float HeaderHotkeyGroupWidth = 92.0f;
        public float HotkeyButtonWidth = 42.0f;
        public float HotkeyButtonHeight = 18.0f;

        public int ButtonFlashMs = 90;
        public bool ShowCycleTimingOverlay = false;

        // Hovered ? affix visual selector. Overlay-only; does not affect automation.
        public bool ShowHoverAffixDiamond = true;
        public float HoverAffixDiamondSizePx = 11.0f;
        public float HoverAffixDiamondLeftOffsetPx = -4.0f;
        public float HoverAffixDiamondTopOffsetPx = 6.0f;

        // ── Mystic timing ──────────────────────────────────────────────────────────
        // Edit these values if Mystic misses clicks or feels too slow.
        // Lower is faster, but too low can cause missed UI state changes.
        public int ClickDelayMs = 10;
        public int PollDelayMs = 10;
        public int AfterRollDelayMs = 35;
        public int AfterSelectDelayMs = 35;
        public int StateTimeoutMs = 900;

        // After clicking Replace Property, the Mystic UI shows an animation/lockout.
        // We do not use a fixed 2-second wait; instead we wait until choices are visible,
        // the button reads Select Property, and the generated row texts are stable briefly.
        public int ChoiceStableMs = 150;

        // Safety timeout for the long Mystic roll animation.
        // Increase this if your UI takes longer than expected.
        public int RollAnimationTimeoutMs = 3000;

        // Minimum retry spacing if a generated choice click fails or does not select.
        public int ChoiceClickRetryMs = 120;

        private IKeyEvent ToggleKeyEvent;

        private IUiElement _vendorPage;
        private IUiElement _enchantDialog;
        private IUiElement _enchantButton;
        private IUiElement _affixListDialog;
        private IUiElement _requiredGemIcon;

        private readonly IUiElement[] _itemAffixText = new IUiElement[10];
        private readonly IUiElement[] _itemAffixSelected = new IUiElement[10];
        private readonly IUiElement[] _validAffixText = new IUiElement[32];
        private readonly IUiElement[] _requiredMaterialText = new IUiElement[5];

        private IFont _headerFont;
        private IFont _buttonFont;
        private IFont _infoFont;
        private IFont _tinyFont;

        private IBrush _buttonFillBrush;
        private IBrush _buttonFillActiveBrush;
        private IBrush _buttonFillWarningBrush;
        private IBrush _buttonFillDisabledBrush;
        private IBrush _buttonBorderBrush;
        private IBrush _buttonHighlightBrush;
        private IBrush _buttonGreenHighlightBrush;
        private IBrush _buttonSeparatorBrush;
        private IBrush _panelBrush;
        private IBrush _hoverDiamondFillBrush;
        private IBrush _hoverDiamondBorderBrush;

        private bool _geometryDrawFailed;
        private bool _geometryDrawFailureLogged;

        private RectangleF _hotkeyButtonRect = RectangleF.Empty;
        private RectangleF _attemptToggleRect = RectangleF.Empty;
        private RectangleF _attemptMinusRect = RectangleF.Empty;
        private RectangleF _attemptValueRect = RectangleF.Empty;
        private RectangleF _attemptPlusRect = RectangleF.Empty;
        private int _attemptMinusFlashUntilTick;
        private int _attemptValueFlashUntilTick;
        private int _attemptPlusFlashUntilTick;
        private int _lastFiniteMaxEnchantAttempts = 100;

        private enum MysticStage
        {
            Idle,
            ValidateReady,
            ClickGenerate,
            WaitGeneratedChoices,
            SelectBestChoice,
            WaitOptionSelected,
            ClickSelectProperty,
            WaitCommitted,
            Finish
        }

        private enum ButtonState
        {
            Unknown,
            PlaceItem,
            ReplaceProperty,
            SelectProperty
        }

        private sealed class AffixValue
        {
            public string Raw;
            public string Key;
            public double Value;
            public bool HasValue;
        }

        private sealed class RollChoice
        {
            public int Index;
            public IUiElement Element;
            public string RawText;
            public string Key;
            public double Value;
            public bool HasValue;
            public bool MatchesTarget;
            public bool IsMax;
        }

        private sealed class MysticTimingProfile
        {
            public int ClickDelay;
            public int PollDelay;
            public int AfterRoll;
            public int AfterSelect;
            public int StateTimeout;

            public override string ToString()
            {
                return "Click=" + ClickDelay
                    + " Poll=" + PollDelay
                    + " AfterRoll=" + AfterRoll
                    + " AfterSelect=" + AfterSelect
                    + " Timeout=" + StateTimeout;
            }
        }

        private MysticStage _stage = MysticStage.Idle;
        private bool _running;
        private bool _capturingHotkey;

        private int _nextActionTick;
        private int _deadlineTick;
        private int _runId;
        private int _attemptCount;
        private int _cycleStartTick;
        private int _lastCycleElapsedMs;
        private int _hotkeyFlashUntilTick;

        private string _lastChoicesSignature = string.Empty;
        private int _choicesStableSinceTick;
        private int _lastChoiceClickTick;
        private int _generatedReadyTimeouts;

        private AffixValue _target;
        private RollChoice _selectedChoice;
        private bool _perfectPendingStop;

        private string _lastStatus = "ready";
        private string _lastStopReason = string.Empty;
        private string _cachedHoverAffixText = string.Empty;
        private RectangleF _cachedHoverAffixRect = RectangleF.Empty;
        private int _nextPreviewRefreshTick;
        private int _lastPreviewCursorX = int.MinValue;
        private int _lastPreviewCursorY = int.MinValue;

        private string _settingsPath;
        private string _legacySettingsPath;

        private static readonly string[] _placeItemPhrases =
        {
            "Place an Item to Replace a Property",
            "放置一项物品来替换一项属性",
            "放置物品以替換其屬性",
            "Выберите предмет",
            "속성을 교체할 아이템을 올려놓으십시오",
            "Zum Austausch Gegenstand platzieren",
            "Placez un objet",
            "Colloca un oggetto per incantarlo",
            "Colocar objeto",
            "Coloca un objeto",
            "Umieść przedmiot",
            "Insira um Item"
        };

        private static readonly string[] _replacePropertyPhrases =
        {
            "Replace Property",
            "替换属性",
            "替換屬性",
            "Изменить",
            "속성 교체",
            "Eigenschaft austauschen",
            "Remplacer la propriété",
            "Sostituisci proprietà",
            "Reemplazar propiedad",
            "Zmień właściwość",
            "Substituir Propriedade"
        };

        private static readonly string[] _selectPropertyPhrases =
        {
            "Select Property",
            "选择属性",
            "選擇屬性",
            "Выбрать свойство",
            "속성 선택",
            "Eigenschaft auswählen",
            "Choisir une propriété",
            "Seleziona proprietà",
            "Seleccionar propiedad",
            "Wybierz właściwość",
            "Selecionar Propriedade"
        };

        public s7o_MysticEnchantPlugin()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            RegisterUi();
            CreatePaintResources();
            PrepareSettingsPath();
            LoadSettings();

            ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, MysticEnchantHotkey, false, false, false);
            _lastFiniteMaxEnchantAttempts = MaxEnchantAttempts > 0
                ? MaxEnchantAttempts
                : Math.Max(1, DefaultFiniteAttemptLimit);

            DebugLog("LOAD hotkey=" + MysticEnchantHotkey + " maxAttempts=" + MaxEnchantAttempts);
        }

        private void RegisterUi()
        {
            _vendorPage = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage", null);
            _enchantDialog = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog", _vendorPage);
            _enchantButton = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog.LayoutRoot.enchant_button", _enchantDialog);
            _affixListDialog = RegisterOrGetUiElement("Root.NormalLayer.validItemAffixes_dialog", null);

            for (int i = 0; i < _itemAffixText.Length; i++)
            {
                _itemAffixText[i] = RegisterOrGetUiElement(
                    "Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog.LayoutRoot.item_affix_list._content._stackpanel._item" + i + ".list_item_window.text",
                    _enchantDialog);

                _itemAffixSelected[i] = RegisterOrGetUiElement(
                    "Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog.LayoutRoot.item_affix_list._content._stackpanel._item" + i + ".list_item_window.selected_background",
                    _enchantDialog);
            }

            for (int i = 0; i < _validAffixText.Length; i++)
            {
                _validAffixText[i] = RegisterOrGetUiElement(
                    "Root.NormalLayer.validItemAffixes_dialog.affixes._content._stackpanel._item" + i + ".stack.text",
                    _affixListDialog);
            }

            for (int i = 0; i < _requiredMaterialText.Length; i++)
            {
                _requiredMaterialText[i] = RegisterOrGetUiElement(
                    "Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog.LayoutRoot.required_materials.ingredient_" + i + ".text",
                    _enchantDialog);
            }

            _requiredGemIcon = RegisterOrGetUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.enchant_dialog.LayoutRoot.required_materials.ingredient_4.icon",
                _enchantDialog);
        }

        private void CreatePaintResources()
        {
            _headerFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 230, 210, 135, true, false, 255, 0, 0, 0, true);
            _buttonFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 235, 235, 225, true, false, 255, 0, 0, 0, true);
            _infoFont = Hud.Render.CreateFont("tahoma", 7.5f, 255, 230, 220, 180, false, false, 255, 0, 0, 0, true);
            _tinyFont = Hud.Render.CreateFont("tahoma", 6.5f, 230, 210, 205, 180, false, false, 255, 0, 0, 0, true);

            _buttonFillBrush = Hud.Render.CreateBrush(255, 48, 48, 48, 0);
            _buttonFillActiveBrush = Hud.Render.CreateBrush(255, 0, 170, 60, 0);
            _buttonFillWarningBrush = Hud.Render.CreateBrush(255, 88, 43, 28, 0);
            _buttonFillDisabledBrush = Hud.Render.CreateBrush(220, 42, 42, 42, 0);
            _buttonBorderBrush = Hud.Render.CreateBrush(225, 105, 55, 10, 1.2f);
            _buttonHighlightBrush = Hud.Render.CreateBrush(80, 125, 125, 125, 0);
            _buttonGreenHighlightBrush = Hud.Render.CreateBrush(90, 120, 255, 150, 0);
            _buttonSeparatorBrush = Hud.Render.CreateBrush(190, 120, 65, 15, 1.0f);
            _panelBrush = Hud.Render.CreateBrush(170, 10, 10, 10, 0);
            _hoverDiamondFillBrush = Hud.Render.CreateBrush(255, 220, 25, 25, 2.0f);
            _hoverDiamondBorderBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 4.0f);
        }

        private IUiElement RegisterOrGetUiElement(string path, IUiElement parent)
        {
            if (string.IsNullOrEmpty(path) || Hud == null || Hud.Render == null)
                return null;

            try { return Hud.Render.RegisterUiElement(path, parent, null); }
            catch
            {
                try { return Hud.Render.GetUiElement(path); }
                catch { return null; }
            }
        }

        private bool IsVisible(IUiElement element)
        {
            if (element == null) return false;

            try
            {
                element.Refresh();
                return element.Visible;
            }
            catch
            {
                return false;
            }
        }

        private string ReadTextSafe(IUiElement element)
        {
            if (element == null) return string.Empty;

            try
            {
                element.Refresh();
                if (!element.Visible) return string.Empty;
                return element.ReadText(Encoding.UTF8, true) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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
                    _lastStatus = "hotkey capture cancelled";
                    return;
                }

                MysticEnchantHotkey = keyEvent.Key;
                ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, MysticEnchantHotkey, false, false, false);
                _capturingHotkey = false;
                SaveSettings();

                _lastStatus = "hotkey changed to " + MysticEnchantHotkey;
                DebugLog("HOTKEY changed=" + MysticEnchantHotkey);
                return;
            }

            if (ToggleKeyEvent == null || !ToggleKeyEvent.Matches(keyEvent))
                return;

            ToggleAutomation();
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left)
                return false;

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return false;

            if (!UpdateOverlayLayoutRects())
                return false;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;
            int now = Environment.TickCount;

            bool hitAttemptMinus = PointInRect(_attemptMinusRect, x, y);
            bool hitAttemptValue = PointInRect(_attemptValueRect, x, y);
            bool hitAttemptPlus = PointInRect(_attemptPlusRect, x, y);

            if (hitAttemptMinus || hitAttemptValue || hitAttemptPlus)
            {
                if (hitAttemptMinus)
                    _attemptMinusFlashUntilTick = now + Math.Max(30, ButtonFlashMs);
                else if (hitAttemptValue)
                    _attemptValueFlashUntilTick = now + Math.Max(30, ButtonFlashMs);
                else
                    _attemptPlusFlashUntilTick = now + Math.Max(30, ButtonFlashMs);

                if (_running)
                {
                    _lastStatus = "attempt control ignored while running";
                    return true;
                }

                StepAttemptLimit(hitAttemptMinus ? -1 : 1);
                return true;
            }

            if (PointInRect(_hotkeyButtonRect, x, y))
            {
                if (_running)
                {
                    _hotkeyFlashUntilTick = now + ButtonFlashMs;
                    ToggleAutomation();
                }
                else
                {
                    _capturingHotkey = true;
                    _lastStatus = "press a key, or Escape to cancel";
                    _hotkeyFlashUntilTick = now + ButtonFlashMs;
                }

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
            int now = Environment.TickCount;

            if (_capturingHotkey && (_enchantDialog == null || !IsVisible(_enchantDialog) || Hud == null || Hud.Window == null || !Hud.Window.IsForeground))
                _capturingHotkey = false;

            if (!_running)
            {
                RefreshHoverPreviewIfNeeded(now);
                return;
            }

            AdvanceRun(now);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!Enabled)
                return;

            if (clipState != ClipState.Inventory)
                return;

            if (_enchantDialog == null || !IsVisible(_enchantDialog))
                return;

            if (!UpdateOverlayLayoutRects())
                return;

            DrawHeaderHotkey();
            DrawAttemptsToggle();
            DrawHoverAffixDiamond();
            DrawStatusText();
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            StopRun("new area");
        }

        public void ForceStopForDisable()
        {
            try { StopRun("disabled"); }
            catch { }
        }

        private void StepAttemptLimit(int direction)
        {
            if (direction > 0)
            {
                // Plus/value advances finite values by 10 and stops at unlimited.
                if (MaxEnchantAttempts == 0)
                {
                    _lastStatus = "attempt limit: unlimited";
                    SaveSettings();
                    return;
                }

                if (MaxEnchantAttempts >= 100)
                    MaxEnchantAttempts = 0;
                else
                    MaxEnchantAttempts = Math.Min(100, Math.Max(10, ((MaxEnchantAttempts / 10) + 1) * 10));
            }
            else
            {
                // Minus is the only way out of unlimited, and it returns to 100.
                if (MaxEnchantAttempts == 0)
                    MaxEnchantAttempts = 100;
                else if (MaxEnchantAttempts <= 10)
                    MaxEnchantAttempts = 10;
                else
                    MaxEnchantAttempts = Math.Max(10, ((MaxEnchantAttempts - 1) / 10) * 10);
            }

            if (MaxEnchantAttempts > 0)
                _lastFiniteMaxEnchantAttempts = MaxEnchantAttempts;

            _lastStatus = MaxEnchantAttempts == 0
                ? "attempt limit: unlimited"
                : "attempt limit: " + MaxEnchantAttempts.ToString(CultureInfo.InvariantCulture);

            SaveSettings();
        }

        private void ToggleAutomation()
        {
            int now = Environment.TickCount;

            if (_running)
            {
                StopRun("manual stop");
                return;
            }

            string reason;
            if (!IsValidMysticContext(out reason))
            {
                _lastStatus = reason;
                return;
            }

            AffixValue target;
            if (!TryCaptureTargetAffixUnderCursor(out target))
            {
                _lastStatus = "hover a valid ? affix, then press " + MysticEnchantHotkey;
                return;
            }

            if (string.IsNullOrEmpty(target.Key))
            {
                _lastStatus = "could not parse target affix";
                return;
            }

            _target = target;
            BeginRun(now);
        }

        private void BeginRun(int now)
        {
            _running = true;
            _runId++;
            _attemptCount = 0;
            _selectedChoice = null;
            _perfectPendingStop = false;
            _lastStopReason = string.Empty;
            _lastStatus = "running target: " + (_target == null ? "?" : _target.Raw);

            _cycleStartTick = 0;
            _lastCycleElapsedMs = 0;
            _lastChoicesSignature = string.Empty;
            _choicesStableSinceTick = 0;
            _lastChoiceClickTick = 0;
            _generatedReadyTimeouts = 0;

            _stage = MysticStage.ValidateReady;
            ClearDeadline();
            Delay(now, 0);

            DebugLog("START run=" + _runId + " target=" + (_target == null ? "?" : _target.Raw) + " key=" + (_target == null ? "?" : _target.Key) + " profile=" + CurrentProfile());
        }

        private void StopRun(string reason)
        {
            bool hadActiveRun = _running || _stage != MysticStage.Idle;

            if (hadActiveRun)
                ReleaseMouseButtons();

            if (hadActiveRun)
                DebugLog("STOP reason=" + (reason ?? string.Empty) + " attempts=" + _attemptCount + " stage=" + _stage);

            _running = false;
            _stage = MysticStage.Idle;
            ClearDeadline();

            _lastStopReason = reason ?? string.Empty;
            _lastStatus = string.IsNullOrEmpty(_lastStopReason) ? "stopped" : "stopped: " + _lastStopReason;
        }

        private void AdvanceRun(int now)
        {
            if (!TickReached(now, _nextActionTick))
                return;

            string reason;
            if (!IsValidMysticContext(out reason))
            {
                StopRun(reason);
                return;
            }

            var p = CurrentProfile();

            switch (_stage)
            {
                case MysticStage.ValidateReady:
                {
                    var state = GetEnchantButtonState();

                    if (state == ButtonState.PlaceItem)
                    {
                        StopRun("place an item first");
                        return;
                    }

                    if (!HasAnySelectedAffix() && state == ButtonState.ReplaceProperty)
                    {
                        StopRun("select the item stat first");
                        return;
                    }

                    _stage = MysticStage.ClickGenerate;
                    Delay(now, p.PollDelay);
                    return;
                }

                case MysticStage.ClickGenerate:
                {
                    if (MaxEnchantAttempts > 0 && _attemptCount >= MaxEnchantAttempts)
                    {
                        StopRun("max attempts reached");
                        return;
                    }

                    if (!CanAffordNextRoll())
                    {
                        StopRun("gold/material limit");
                        return;
                    }

                    var state = GetEnchantButtonState();
                    if (state != ButtonState.ReplaceProperty)
                    {
                        Delay(now, p.PollDelay);
                        return;
                    }

                    if (CurrentCommittedAffixIsMax())
                    {
                        StopRun("target max reached");
                        return;
                    }

                    if (!ClickUi(_enchantButton, "Replace Property"))
                    {
                        Delay(now, 30);
                        return;
                    }

                    _attemptCount++;
                    _cycleStartTick = now;
                    _perfectPendingStop = false;

                    _lastChoicesSignature = string.Empty;
                    _choicesStableSinceTick = 0;
                    _lastChoiceClickTick = 0;
                    _generatedReadyTimeouts = 0;

                    _stage = MysticStage.WaitGeneratedChoices;
                    ClearDeadline();

                    // Use only a short initial delay. The real wait happens by detecting readiness.
                    Delay(now, p.AfterRoll);
                    DebugLog("CLICK generate attempt=" + _attemptCount);
                    return;
                }

                case MysticStage.WaitGeneratedChoices:
                {
                    EnsureDeadline(now, Math.Max(p.StateTimeout, RollAnimationTimeoutMs));

                    var choices = ReadVisibleRollChoices();

                    if (ChoicesAreStableAndSelectable(now, choices))
                    {
                        ClearDeadline();
                        _stage = MysticStage.SelectBestChoice;
                        Delay(now, 0);
                        return;
                    }

                    if (DeadlineExpired(now))
                    {
                        ClearDeadline();

                        // If the UI is selectable but text stability never latched, allow the fallback.
                        if (choices.Count >= 3 && GetEnchantButtonState() == ButtonState.SelectProperty)
                        {
                            _stage = MysticStage.SelectBestChoice;
                            Delay(now, 0);
                            return;
                        }

                        _generatedReadyTimeouts++;

                        if (_generatedReadyTimeouts <= 2)
                        {
                            DebugLog("generated choices not ready; extending wait #" + _generatedReadyTimeouts);
                            Delay(now, p.PollDelay);
                            return;
                        }

                        StopRun("generated choices not ready");
                        return;
                    }

                    Delay(now, p.PollDelay);
                    return;
                }

                case MysticStage.SelectBestChoice:
                {
                    var choices = ReadVisibleRollChoices();

                    if (!ChoicesAreStableAndSelectable(now, choices))
                    {
                        _stage = MysticStage.WaitGeneratedChoices;
                        Delay(now, p.PollDelay);
                        return;
                    }

                    var best = ChooseBestRollChoice(choices);
                    if (best == null || best.Element == null)
                    {
                        StopRun("no selectable choice");
                        return;
                    }

                    _selectedChoice = best;
                    _perfectPendingStop = best.IsMax;

                    if (_lastChoiceClickTick != 0 && (int)(now - _lastChoiceClickTick) < ChoiceClickRetryMs)
                    {
                        Delay(now, p.PollDelay);
                        return;
                    }

                    if (!ClickChoice(best))
                    {
                        _lastChoiceClickTick = now;
                        Delay(now, ChoiceClickRetryMs);
                        return;
                    }

                    _lastChoiceClickTick = now;

                    _stage = MysticStage.WaitOptionSelected;
                    ClearDeadline();
                    Delay(now, p.ClickDelay);
                    DebugLog("CHOICE selected index=" + best.Index + " value=" + best.Value.ToString(CultureInfo.InvariantCulture) + " target=" + (_target == null ? "?" : _target.Value.ToString(CultureInfo.InvariantCulture)) + " max=" + best.IsMax + " raw=" + best.RawText);
                    return;
                }

                case MysticStage.WaitOptionSelected:
                {
                    EnsureDeadline(now, p.StateTimeout);

                    bool selected = HasSelectedGeneratedChoice();
                    bool buttonReady = GetEnchantButtonState() == ButtonState.SelectProperty;

                    if (selected && buttonReady)
                    {
                        ClearDeadline();
                        _stage = MysticStage.ClickSelectProperty;
                        Delay(now, 0);
                        return;
                    }

                    // If the click did not register, retry selecting the same choice.
                    if (_selectedChoice != null
                        && _lastChoiceClickTick != 0
                        && (int)(now - _lastChoiceClickTick) >= ChoiceClickRetryMs
                        && buttonReady)
                    {
                        if (ClickChoice(_selectedChoice))
                        {
                            _lastChoiceClickTick = now;
                            Delay(now, p.PollDelay);
                            return;
                        }

                        _lastChoiceClickTick = now;
                        Delay(now, ChoiceClickRetryMs);
                        return;
                    }

                    if (DeadlineExpired(now))
                    {
                        ClearDeadline();
                        StopRun("choice selection not confirmed");
                        return;
                    }

                    Delay(now, p.PollDelay);
                    return;
                }

                case MysticStage.ClickSelectProperty:
                {
                    if (!HasSelectedGeneratedChoice())
                    {
                        _stage = MysticStage.WaitOptionSelected;
                        Delay(now, p.PollDelay);
                        return;
                    }

                    if (GetEnchantButtonState() != ButtonState.SelectProperty)
                    {
                        Delay(now, p.PollDelay);
                        return;
                    }

                    if (!ClickUi(_enchantButton, "Select Property"))
                    {
                        Delay(now, 30);
                        return;
                    }

                    _stage = MysticStage.WaitCommitted;
                    ClearDeadline();
                    Delay(now, p.AfterSelect);
                    DebugLog("ACCEPT selected index=" + (_selectedChoice == null ? -1 : _selectedChoice.Index));
                    return;
                }

                case MysticStage.WaitCommitted:
                {
                    EnsureDeadline(now, p.StateTimeout);

                    if (GetEnchantButtonState() == ButtonState.ReplaceProperty)
                    {
                        ClearDeadline();

                        if (_cycleStartTick != 0)
                            _lastCycleElapsedMs = unchecked(now - _cycleStartTick);

                        if (_perfectPendingStop)
                        {
                            StopRun("target max reached");
                            return;
                        }

                        _stage = MysticStage.ClickGenerate;
                        Delay(now, p.PollDelay);
                        return;
                    }

                    if (DeadlineExpired(now))
                    {
                        ClearDeadline();
                        _stage = MysticStage.ClickGenerate;
                        Delay(now, p.PollDelay);
                    }

                    return;
                }

                case MysticStage.Finish:
                    StopRun("finished");
                    return;
            }
        }

        private bool IsValidMysticContext(out string reason)
        {
            reason = null;

            if (!Enabled) { reason = "plugin disabled"; return false; }
            if (Hud == null || Hud.Game == null || Hud.Window == null || Hud.Inventory == null) { reason = "hud unavailable"; return false; }
            if (!Hud.Game.IsInGame) { reason = "not in game"; return false; }
            if (Hud.Game.IsLoading) { reason = "loading"; return false; }
            if (Hud.Game.IsPaused) { reason = "paused"; return false; }
            if (Hud.Game.Me == null) { reason = "player unavailable"; return false; }
            if (!Hud.Window.IsForeground) { reason = "window not foreground"; return false; }

            if (_vendorPage == null || !IsVisible(_vendorPage)) { reason = "vendor hidden"; return false; }
            if (_enchantDialog == null || !IsVisible(_enchantDialog)) { reason = "mystic hidden"; return false; }
            if (_enchantButton == null || !IsVisible(_enchantButton)) { reason = "enchant button hidden"; return false; }

            return true;
        }

        private ButtonState GetEnchantButtonState()
        {
            var str = ReadTextSafe(_enchantButton);
            if (string.IsNullOrEmpty(str))
                return ButtonState.Unknown;

            if (ContainsAny(str, _placeItemPhrases)) return ButtonState.PlaceItem;
            if (ContainsAny(str, _replacePropertyPhrases)) return ButtonState.ReplaceProperty;
            if (ContainsAny(str, _selectPropertyPhrases)) return ButtonState.SelectProperty;

            return ButtonState.Unknown;
        }

        private static bool ContainsAny(string text, string[] phrases)
        {
            if (string.IsNullOrEmpty(text) || phrases == null) return false;

            foreach (var p in phrases)
            {
                if (!string.IsNullOrEmpty(p) && text.Contains(p))
                    return true;
            }

            return false;
        }

        private void RefreshHoverPreviewIfNeeded(int now)
        {
            if (!IsHoverPreviewContextVisible())
            {
                if (!string.IsNullOrEmpty(_cachedHoverAffixText) || _cachedHoverAffixRect.Width > 0 || _cachedHoverAffixRect.Height > 0)
                {
                    _cachedHoverAffixText = string.Empty;
                    _cachedHoverAffixRect = RectangleF.Empty;
                }

                _lastPreviewCursorX = int.MinValue;
                _lastPreviewCursorY = int.MinValue;
                _nextPreviewRefreshTick = 0;
                return;
            }

            int cursorX = Hud.Window.CursorX;
            int cursorY = Hud.Window.CursorY;
            bool cursorMoved = cursorX != _lastPreviewCursorX || cursorY != _lastPreviewCursorY;

            if (!cursorMoved && !TickReached(now, _nextPreviewRefreshTick))
                return;

            _lastPreviewCursorX = cursorX;
            _lastPreviewCursorY = cursorY;
            _nextPreviewRefreshTick = now + 250;

            RefreshHoverPreview();
        }

        private bool IsHoverPreviewContextVisible()
        {
            if (!ShowHoverAffixDiamond || Hud == null || Hud.Window == null)
                return false;

            if (_enchantDialog == null || _affixListDialog == null)
                return false;

            return IsVisible(_enchantDialog) && IsVisible(_affixListDialog);
        }

        private void RefreshHoverPreview()
        {
            AffixValue target;
            RectangleF rowRect;

            if (TryCaptureTargetAffixUnderCursor(out target, out rowRect))
            {
                _cachedHoverAffixText = target.Raw;
                _cachedHoverAffixRect = rowRect;
            }
            else
            {
                _cachedHoverAffixText = string.Empty;
                _cachedHoverAffixRect = RectangleF.Empty;
            }
        }

        private bool TryCaptureTargetAffixUnderCursor(out AffixValue target)
        {
            RectangleF rowRect;
            return TryCaptureTargetAffixUnderCursor(out target, out rowRect);
        }

        private bool TryCaptureTargetAffixUnderCursor(out AffixValue target, out RectangleF rowRect)
        {
            target = null;
            rowRect = RectangleF.Empty;

            if (!IsVisible(_affixListDialog))
                return false;

            int cx = Hud.Window.CursorX;
            int cy = Hud.Window.CursorY;

            foreach (var ui in _validAffixText)
            {
                if (!IsVisible(ui))
                    continue;

                var r = ui.Rectangle;
                if (r.Width <= 0 || r.Height <= 0)
                    continue;

                if (!r.Contains(cx, cy))
                    continue;

                var raw = CleanUiAffixText(ReadTextSafe(ui));
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                target = ParseAffixText(raw);
                if (target == null || string.IsNullOrEmpty(target.Key))
                    return false;

                rowRect = r;
                return true;
            }

            return false;
        }

        private AffixValue ParseAffixText(string raw)
        {
            raw = CleanUiAffixText(raw);

            bool has;
            double value = ParseAffixValue(raw, out has);

            return new AffixValue
            {
                Raw = raw,
                Key = NormalizeAffixKey(raw),
                Value = value,
                HasValue = has
            };
        }

        private string CleanUiAffixText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var s = raw;
            s = Regex.Replace(s, @"\{icon.*?\}", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\{.*?\}", "");
            return s.Trim();
        }

        private string NormalizeAffixKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var s = CleanUiAffixText(raw);
            s = NormalizeElementalDamage(s);

            s = Regex.Replace(s, @"\[.*?\]", "");
            s = Regex.Replace(s, @"\(.*?\)", "");
            s = Regex.Replace(s, @"[0-9\.,\-−–—~％%]", "");
            s = Regex.Replace(s, @"\s+", " ");

            return s.Trim();
        }

        private string NormalizeElementalDamage(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace("点闪电伤害", "点伤害");
            s = s.Replace("点火焰伤害", "点伤害");
            s = s.Replace("点冰霜伤害", "点伤害");
            s = s.Replace("点毒性伤害", "点伤害");
            s = s.Replace("点奥术伤害", "点伤害");
            s = s.Replace("点神圣伤害", "点伤害");

            s = s.Replace("點電擊傷害", "點傷害");
            s = s.Replace("點火焰傷害", "點傷害");
            s = s.Replace("點冰寒傷害", "點傷害");
            s = s.Replace("點毒素傷害", "點傷害");
            s = s.Replace("點秘法傷害", "點傷害");
            s = s.Replace("點神聖傷害", "點傷害");

            s = s.Replace("Lightning Damage", "Damage");
            s = s.Replace("Fire Damage", "Damage");
            s = s.Replace("Cold Damage", "Damage");
            s = s.Replace("Poison Damage", "Damage");
            s = s.Replace("Arcane Damage", "Damage");
            s = s.Replace("Holy Damage", "Damage");

            s = s.Replace("ед. урона от молнии", "ед. урона");
            s = s.Replace("ед. урона от огня", "ед. урона");
            s = s.Replace("ед. урона от холода", "ед. урона");
            s = s.Replace("ед. урона от яда", "ед. урона");
            s = s.Replace("ед. урона от тайной магии", "ед. урона");
            s = s.Replace("ед. урона от сил Света", "ед. урона");

            s = s.Replace("번개 무기 공격력", "무기 공격력");
            s = s.Replace("화염 무기 공격력", "무기 공격력");
            s = s.Replace("냉기 무기 공격력", "무기 공격력");
            s = s.Replace("독 무기 공격력", "무기 공격력");
            s = s.Replace("비전 무기 공격력", "무기 공격력");
            s = s.Replace("신성 무기 공격력", "무기 공격력");

            s = s.Replace("Blitzschaden", "Schaden");
            s = s.Replace("Feuerschaden", "Schaden");
            s = s.Replace("Kälteschaden", "Schaden");
            s = s.Replace("Giftschaden", "Schaden");
            s = s.Replace("Arkanschaden", "Schaden");
            s = s.Replace("Heiligschaden", "Schaden");

            s = s.Replace("points de dégâts de foudre", "points de dégâts");
            s = s.Replace("points de dégâts de feu", "points de dégâts");
            s = s.Replace("points de dégâts de froid", "points de dégâts");
            s = s.Replace("points de dégâts de poison", "points de dégâts");
            s = s.Replace("points de dégâts arcaniques", "points de dégâts");
            s = s.Replace("points de dégâts sacrés", "points de dégâts");

            s = s.Replace("danni da fulmine", "danni");
            s = s.Replace("danni da fuoco", "danni");
            s = s.Replace("danni da freddo", "danni");
            s = s.Replace("danni da veleno", "danni");
            s = s.Replace("danni arcani", "danni");
            s = s.Replace("danni sacri", "danni");

            s = s.Replace("p. de daño de rayos", "p.de daño");
            s = s.Replace("p. de daño de fuego", "p.de daño");
            s = s.Replace("p. de daño de frío", "p.de daño");
            s = s.Replace("p. de daño de veneno", "p.de daño");
            s = s.Replace("p. de daño arcano", "p.de daño");
            s = s.Replace("p. de daño sagrado", "p.de daño");

            s = s.Replace("de daño de Rayo", "de daño");
            s = s.Replace("de daño de Fuego", "de daño");
            s = s.Replace("de daño de Frío", "de daño");
            s = s.Replace("de daño de Veneno", "de daño");
            s = s.Replace("de daño Arcano", "de daño");
            s = s.Replace("de daño Sacro", "de daño");

            s = s.Replace("obrażeń od błyskawic", "obrażeń");
            s = s.Replace("obrażeń od ognia", "obrażeń");
            s = s.Replace("obrażeń od zimna", "obrażeń");
            s = s.Replace("obrażeń od trucizny", "obrażeń");
            s = s.Replace("obrażeń od mocy tajemnej", "obrażeń");
            s = s.Replace("obrażeń od mocy świętej", "obrażeń");

            s = s.Replace("de dano Elétrico", "de dano");
            s = s.Replace("de dano Ígneo", "de dano");
            s = s.Replace("de dano Gélido", "de dano");
            s = s.Replace("de dano Venenoso", "de dano");
            s = s.Replace("de dano Arcano", "de dano");
            s = s.Replace("de dano Sagrado", "de dano");

            return s;
        }

        private double ParseAffixValue(string raw, out bool hasValue)
        {
            hasValue = false;

            if (string.IsNullOrEmpty(raw))
                return 0;

            // Weapon damage affixes can contain two bracketed ranges:
            // +[low - high]-[low - high] Damage
            // The original Lightning plugin effectively uses the last bracket,
            // which represents the upper damage range. Preserve that behavior.
            var bracketMatches = Regex.Matches(raw, @"\[(?<value>[^\[\]]+)\]");

            for (int i = bracketMatches.Count - 1; i >= 0; i--)
            {
                var candidate = TakeHighRangePart(bracketMatches[i].Groups["value"].Value);

                double v;
                if (TryParseLooseDouble(candidate, out v))
                {
                    hasValue = true;
                    return v;
                }
            }

            // Fallback: collect numeric/range text from the raw affix and take the high end.
            var match = Regex.Match(raw, @"[-+]?\d[\d\s,\.\u00A0]*(?:\s*[-~–—]\s*[-+]?\d[\d\s,\.\u00A0]*)?");
            if (match.Success)
            {
                var candidate = TakeHighRangePart(match.Value);

                double v;
                if (TryParseLooseDouble(candidate, out v))
                {
                    hasValue = true;
                    return v;
                }
            }

            return 0;
        }

        private static string TakeHighRangePart(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var s = value.Trim();

            // Normalize common range separators.
            s = s.Replace("–", "-");
            s = s.Replace("—", "-");

            if (s.Contains("~"))
            {
                var split = s.Split('~');
                return split[split.Length - 1].Trim();
            }

            // Split on separator dashes, not leading negative signs.
            var m = Regex.Match(s, @"(.+?)\s*-\s*(.+)");
            if (m.Success)
                return m.Groups[2].Value.Trim();

            return s;
        }

        private static bool TryParseLooseDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            var s = text.Replace("\u00A0", "").Replace(" ", "").Trim();
            s = Regex.Replace(s, @"[^0-9\.,]", "");
            if (string.IsNullOrEmpty(s))
                return false;

            int commaCount = s.Count(c => c == ',');
            int dotCount = s.Count(c => c == '.');

            if (commaCount > 0 && dotCount > 0)
            {
                if (s.LastIndexOf(',') > s.LastIndexOf('.'))
                {
                    s = s.Replace(".", "");
                    s = s.Replace(',', '.');
                }
                else
                {
                    s = s.Replace(",", "");
                }
            }
            else if (commaCount > 0)
            {
                int last = s.LastIndexOf(',');
                int after = s.Length - last - 1;
                if (commaCount > 1 || after == 3)
                    s = s.Replace(",", "");
                else
                    s = s.Replace(',', '.');
            }
            else if (dotCount > 0)
            {
                int last = s.LastIndexOf('.');
                int after = s.Length - last - 1;
                if (dotCount > 1 || (after == 3 && s.Length > 4))
                    s = s.Replace(".", "");
            }

            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private List<RollChoice> ReadVisibleRollChoices()
        {
            var list = new List<RollChoice>();

            for (int i = 0; i < _itemAffixText.Length; i++)
            {
                var ui = _itemAffixText[i];
                if (!IsVisible(ui))
                    continue;

                var raw = CleanUiAffixText(ReadTextSafe(ui));
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var parsed = ParseAffixText(raw);
                if (parsed == null)
                    continue;

                bool matches = _target != null
                    && !string.IsNullOrEmpty(parsed.Key)
                    && string.Equals(parsed.Key, _target.Key, StringComparison.OrdinalIgnoreCase);

                list.Add(new RollChoice
                {
                    Index = i,
                    Element = ui,
                    RawText = raw,
                    Key = parsed.Key,
                    Value = parsed.Value,
                    HasValue = parsed.HasValue,
                    MatchesTarget = matches,
                    IsMax = matches && IsTargetMax(parsed)
                });
            }

            return list;
        }

        private bool IsTargetMax(AffixValue value)
        {
            if (value == null || _target == null)
                return false;

            if (!string.Equals(value.Key, _target.Key, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!_target.HasValue || !value.HasValue)
                return string.Equals(value.Raw, _target.Raw, StringComparison.OrdinalIgnoreCase);

            return value.Value + 0.0001 >= _target.Value;
        }

        private RollChoice ChooseBestRollChoice(List<RollChoice> choices)
        {
            if (choices == null || choices.Count == 0)
                return null;

            // Original/no-change row is usually row 0.
            var current = choices.FirstOrDefault(x => x.Index == 0) ?? choices[0];

            var matching = choices
                .Where(x => x.MatchesTarget)
                .OrderByDescending(x => x.HasValue ? x.Value : double.MinValue)
                .FirstOrDefault();

            if (matching == null)
                return current;

            // If current already has target stat, avoid downgrades and equal non-max rolls.
            if (current.MatchesTarget && current.HasValue && matching.HasValue)
            {
                if (matching.IsMax)
                    return matching;

                if (matching.Value <= current.Value)
                    return current;
            }

            return matching;
        }

        private bool HasAnySelectedAffix()
        {
            foreach (var ui in _itemAffixSelected)
            {
                if (IsVisible(ui))
                    return true;
            }

            return false;
        }

        private bool HasSelectedGeneratedChoice()
        {
            if (_selectedChoice == null)
                return false;

            int index = _selectedChoice.Index;
            if (index < 0 || index >= _itemAffixSelected.Length)
                return false;

            if (!IsVisible(_itemAffixSelected[index]))
                return false;

            // Make sure generated choices are still visible, so a stale original
            // selected-stat highlight does not count as generated-choice selection.
            var choices = ReadVisibleRollChoices();
            if (choices == null || choices.Count < 3)
                return false;

            return choices.Any(x => x.Index == index);
        }

        private bool HasEnoughGeneratedChoices()
        {
            return ReadVisibleRollChoices().Count >= 3;
        }

        private string BuildChoicesSignature(List<RollChoice> choices)
        {
            if (choices == null || choices.Count == 0)
                return string.Empty;

            return string.Join("|", choices
                .OrderBy(x => x.Index)
                .Select(x => x.Index + ":" + (x.RawText ?? string.Empty)));
        }

        private bool ChoicesAreStableAndSelectable(int now, List<RollChoice> choices)
        {
            if (choices == null || choices.Count < 3)
                return false;

            // The button should read Select Property once generated roll choices are in the selection phase.
            if (GetEnchantButtonState() != ButtonState.SelectProperty)
                return false;

            var signature = BuildChoicesSignature(choices);
            if (string.IsNullOrEmpty(signature))
                return false;

            if (!string.Equals(signature, _lastChoicesSignature, StringComparison.Ordinal))
            {
                _lastChoicesSignature = signature;
                _choicesStableSinceTick = now;
                return false;
            }

            return _choicesStableSinceTick != 0 && (int)(now - _choicesStableSinceTick) >= ChoiceStableMs;
        }

        private bool CurrentCommittedAffixIsMax()
        {
            if (_target == null)
                return false;

            var choices = ReadVisibleRollChoices();

            foreach (var c in choices)
            {
                if (!c.MatchesTarget)
                    continue;

                if (c.IsMax)
                    return true;
            }

            return false;
        }

        private bool CanAffordNextRoll()
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Materials == null)
                return true;

            if (EnforceMaterialReserveChecks && !CanAffordRequiredMaterials())
                return false;

            if (!EnforceGoldReserveCheck || ReservedGold <= 0)
                return true;

            long cost = ReadEnchantGoldCost();
            if (cost <= 0)
                return true;

            return Hud.Game.Me.Materials.Gold - cost > ReservedGold;
        }

        private bool CanAffordRequiredMaterials()
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Materials == null)
                return true;

            var mats = Hud.Game.Me.Materials;

            int arcaneDustCost = ReadMaterialUnitRequirement(_requiredMaterialText[0]);
            if (arcaneDustCost > 0 && mats.ArcaneDust - ReservedArcaneDust < arcaneDustCost)
                return false;

            int veiledCrystalCost = ReadMaterialUnitRequirement(_requiredMaterialText[1]);
            if (veiledCrystalCost > 0 && mats.VeiledCrystal - ReservedVeiledCrystal < veiledCrystalCost)
                return false;

            int forgottenSoulCost = ReadMaterialUnitRequirement(_requiredMaterialText[2]);
            if (forgottenSoulCost > 0 && mats.ForgottenSoul - ReservedForgottenSoul < forgottenSoulCost)
                return false;

            int deathsBreathCost = ReadMaterialUnitRequirement(_requiredMaterialText[3]);
            if (deathsBreathCost > 0 && mats.DeathsBreath - ReservedDeathsBreath < deathsBreathCost)
                return false;

            int gemCost = ReadMaterialUnitRequirement(_requiredMaterialText[4]);
            if (gemCost > 0 && GetAvailableGemCountForRequiredGem() - ReservedGem < gemCost)
                return false;

            return true;
        }

        private int ReadMaterialUnitRequirement(IUiElement textElement)
        {
            var text = ReadTextSafe(textElement);
            if (string.IsNullOrEmpty(text))
                return -1;

            int slash = text.IndexOf('/');
            if (slash < 0 || slash >= text.Length - 1)
                return -1;

            var unit = Regex.Replace(text.Substring(slash + 1), @"[^0-9]", "");
            int value;
            return int.TryParse(unit, out value) ? value : -1;
        }

        private long GetAvailableGemCountForRequiredGem()
        {
            if (!IsVisible(_requiredGemIcon))
                return long.MaxValue;

            int gemid = _requiredGemIcon.AnimState;
            if (gemid == 53) return CountGems(1019190640); // ruby
            if (gemid == 15) return CountGems(3446938397); // amethyst
            if (gemid == 91) return CountGems(3256663690); // diamond
            if (gemid == 34) return CountGems(2838965544); // emerald
            if (gemid == 72) return CountGems(4267641564); // topaz

            return long.MaxValue;
        }

        private long CountGems(uint snoItem)
        {
            long count = 0;

            try
            {
                if (Hud.Inventory.ItemsInStash != null)
                {
                    foreach (var item in Hud.Inventory.ItemsInStash)
                    {
                        if (item != null && item.SnoItem != null && item.SnoItem.Sno == snoItem)
                            count += item.Quantity;
                    }
                }

                if (Hud.Inventory.ItemsInInventory != null)
                {
                    foreach (var item in Hud.Inventory.ItemsInInventory)
                    {
                        if (item != null && item.SnoItem != null && item.SnoItem.Sno == snoItem)
                            count += item.Quantity;
                    }
                }
            }
            catch { }

            return count;
        }

        private long ReadEnchantGoldCost()
        {
            var text = ReadTextSafe(_enchantButton);
            if (string.IsNullOrEmpty(text))
                return 0;

            var gold = Between(text, ":", "{");
            if (string.IsNullOrEmpty(gold))
                gold = Between(text, "：", "{");

            gold = Regex.Replace(gold ?? string.Empty, @"[^\d]", "");

            long value;
            return long.TryParse(gold, out value) ? value : 0;
        }

        private static string Between(string text, string left, string right)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int start = 0;

            if (!string.IsNullOrEmpty(left))
            {
                start = text.IndexOf(left, StringComparison.Ordinal);
                if (start < 0) return string.Empty;
                start += left.Length;
            }

            if (string.IsNullOrEmpty(right))
                return text.Substring(start);

            int end = text.IndexOf(right, start, StringComparison.Ordinal);
            if (end < 0) return string.Empty;

            return text.Substring(start, end - start);
        }

        private bool ClickUi(IUiElement element, string label)
        {
            if (element == null)
                return false;

            if (!IsVisible(element))
                return false;

            var r = element.Rectangle;
            if (r.Width <= 0 || r.Height <= 0)
                return false;

            ReleaseMouseButtons();
            bool ok = s7o_MysticEnchantInput.ClickRect(r, MouseButtons.Left);
            if (VerboseDebugLogging)
                DebugLog("CLICK " + label + " ok=" + ok);

            return ok;
        }

        private bool ClickChoice(RollChoice choice)
        {
            if (choice == null || choice.Element == null)
                return false;

            if (!IsVisible(choice.Element))
                return false;

            var r = choice.Element.Rectangle;
            if (r.Width <= 0 || r.Height <= 0)
                return false;

            // Text rects can be narrow. Expand to a safer row-like click area.
            var clickRect = new RectangleF(
                r.X - 8.0f,
                r.Y - 4.0f,
                Math.Max(r.Width + 16.0f, 220.0f),
                r.Height + 8.0f);

            ReleaseMouseButtons();
            bool ok = s7o_MysticEnchantInput.ClickRect(clickRect, MouseButtons.Left);

            if (VerboseDebugLogging)
                DebugLog("CLICK choice index=" + choice.Index + " ok=" + ok + " rect=" + clickRect);

            return ok;
        }

        private void ReleaseMouseButtons()
        {
            try { s7o_MysticEnchantInput.MouseUp(MouseButtons.Left); } catch { }
            try { s7o_MysticEnchantInput.MouseUp(MouseButtons.Right); } catch { }
        }

        private MysticTimingProfile CurrentProfile()
        {
            return new MysticTimingProfile
            {
                ClickDelay = ClickDelayMs,
                PollDelay = PollDelayMs,
                AfterRoll = AfterRollDelayMs,
                AfterSelect = AfterSelectDelayMs,
                StateTimeout = StateTimeoutMs
            };
        }

        private static bool TickReached(int now, int tick)
        {
            return (int)(now - tick) >= 0;
        }

        private void Delay(int now, int ms)
        {
            _nextActionTick = now + Math.Max(0, ms);
        }

        private void EnsureDeadline(int now, int timeoutMs)
        {
            if (_deadlineTick == 0)
                _deadlineTick = now + Math.Max(0, timeoutMs);
        }

        private bool DeadlineExpired(int now)
        {
            return _deadlineTick != 0 && TickReached(now, _deadlineTick);
        }

        private void ClearDeadline()
        {
            _deadlineTick = 0;
        }

        private bool UpdateOverlayLayoutRects()
        {
            if (_vendorPage == null || !IsVisible(_vendorPage) || _enchantDialog == null || !IsVisible(_enchantDialog))
                return false;

            var vr = _vendorPage.Rectangle;
            if (vr.Width <= 0 || vr.Height <= 0)
                return false;

            _hotkeyButtonRect = RectangleF.Empty;
            _attemptToggleRect = RectangleF.Empty;
            _attemptMinusRect = RectangleF.Empty;
            _attemptValueRect = RectangleF.Empty;
            _attemptPlusRect = RectangleF.Empty;

            // Left group: MYSTIC over F-key.
            float groupX = vr.X + HeaderLeftOffset;
            float topY = vr.Y + HeaderTopOffset;

            float hotkeyButtonX = groupX + (HeaderHotkeyGroupWidth - HotkeyButtonWidth) * 0.5f;
            float hotkeyButtonY = topY + 18.0f;

            _hotkeyButtonRect = new RectangleF(
                hotkeyButtonX,
                hotkeyButtonY,
                HotkeyButtonWidth,
                HotkeyButtonHeight);

            // Right group: plain Attempts label over a compact - / value / + pill.
            float attemptX = vr.X + vr.Width - AttemptToggleRightOffset - AttemptToggleWidth;
            float attemptY = vr.Y + AttemptToggleTopOffset + 18.0f;

            _attemptToggleRect = new RectangleF(
                attemptX,
                attemptY,
                AttemptToggleWidth,
                AttemptToggleHeight);

            float sideWidth = 28.0f;
            _attemptMinusRect = new RectangleF(attemptX, attemptY, sideWidth, AttemptToggleHeight);
            _attemptPlusRect = new RectangleF(attemptX + AttemptToggleWidth - sideWidth, attemptY, sideWidth, AttemptToggleHeight);
            _attemptValueRect = new RectangleF(
                attemptX + sideWidth,
                attemptY,
                Math.Max(0.0f, AttemptToggleWidth - (sideWidth * 2.0f)),
                AttemptToggleHeight);

            return true;
        }

        

        private void DrawHeaderHotkey()
        {
            if (_headerFont == null || _vendorPage == null)
                return;

            var vr = _vendorPage.Rectangle;

            float groupX = vr.X + HeaderLeftOffset;
            float topY = vr.Y + HeaderTopOffset;

            string label = "MYSTIC";
            var layout = _headerFont.GetTextLayout(label);

            float labelX = groupX + HeaderHotkeyGroupWidth * 0.5f - layout.Metrics.Width * 0.5f;
            _headerFont.DrawText(layout, labelX, topY);

            string hotkeyText = _capturingHotkey ? "..." : MysticEnchantHotkey.ToString();
            bool flash = !TickReached(Environment.TickCount, _hotkeyFlashUntilTick);

            DrawPillButton(_hotkeyButtonRect, hotkeyText, _running || _capturingHotkey || flash, true, flash);
        }

        private void DrawAttemptsToggle()
        {
            if (_buttonFont == null || _headerFont == null || _vendorPage == null)
                return;

            var vr = _vendorPage.Rectangle;
            float attemptX = vr.X + vr.Width - AttemptToggleRightOffset - AttemptToggleWidth;
            float labelY = vr.Y + AttemptToggleTopOffset;

            string label = "Attempts";
            var labelLayout = _headerFont.GetTextLayout(label);
            float labelX = attemptX + AttemptToggleWidth * 0.5f - labelLayout.Metrics.Width * 0.5f;
            _headerFont.DrawText(labelLayout, labelX, labelY);

            string valueText = MaxEnchantAttempts == 0
                ? "\u221E"
                : MaxEnchantAttempts.ToString(CultureInfo.InvariantCulture);

            int now = Environment.TickCount;

            DrawSegmentedAttemptPillBase(_attemptToggleRect);

            if (!TickReached(now, _attemptMinusFlashUntilTick))
                DrawAttemptPillSegment(_attemptMinusRect, true, false);

            if (!TickReached(now, _attemptValueFlashUntilTick))
                DrawAttemptPillSegment(_attemptValueRect, false, false);

            if (!TickReached(now, _attemptPlusFlashUntilTick))
                DrawAttemptPillSegment(_attemptPlusRect, false, true);

            DrawAttemptSeparators();
            DrawCenteredText(_attemptMinusRect, "-", _buttonFont);
            DrawCenteredText(_attemptValueRect, valueText, _buttonFont);
            DrawCenteredText(_attemptPlusRect, "+", _buttonFont);
        }

        

        private void DrawSegmentedAttemptPillBase(RectangleF rect)
        {
            DrawRoundedRect(rect, rect.Height * 0.5f, _buttonFillBrush, _buttonBorderBrush);

            var highlight = new RectangleF(
                rect.X + 2.0f,
                rect.Y + 2.0f,
                Math.Max(0.0f, rect.Width - 4.0f),
                Math.Max(0.0f, rect.Height * 0.42f));

            DrawRoundedRect(highlight, highlight.Height * 0.5f, _buttonHighlightBrush, null);
        }

        private void DrawAttemptPillSegment(RectangleF rect, bool leftRounded, bool rightRounded)
        {
            var inner = new RectangleF(
                rect.X + 1.0f,
                rect.Y + 1.0f,
                Math.Max(0.0f, rect.Width - 2.0f),
                Math.Max(0.0f, rect.Height - 2.0f));

            DrawRoundedSegment(inner, inner.Height * 0.5f, leftRounded, rightRounded, _buttonFillActiveBrush);

            var highlight = new RectangleF(
                inner.X + 1.0f,
                inner.Y + 1.0f,
                Math.Max(0.0f, inner.Width - 2.0f),
                Math.Max(0.0f, inner.Height * 0.42f));

            DrawRoundedSegment(highlight, highlight.Height * 0.5f, leftRounded, rightRounded, _buttonGreenHighlightBrush);
        }

        private void DrawAttemptSeparators()
        {
            if (_buttonSeparatorBrush == null)
                return;

            float y1 = _attemptToggleRect.Y + 3.0f;
            float y2 = _attemptToggleRect.Y + _attemptToggleRect.Height - 3.0f;
            float div1 = _attemptMinusRect.Right;
            float div2 = _attemptValueRect.Right;

            _buttonSeparatorBrush.DrawLine(div1, y1, div1, y2);
            _buttonSeparatorBrush.DrawLine(div2, y1, div2, y2);
        }

        private void DrawHoverAffixDiamond()
        {
            if (!ShowHoverAffixDiamond || _running || string.IsNullOrEmpty(_cachedHoverAffixText))
                return;

            if (_cachedHoverAffixRect.Width <= 0 || _cachedHoverAffixRect.Height <= 0)
                return;

            if (_affixListDialog == null || !IsVisible(_affixListDialog))
                return;

            float size = Math.Max(6.0f, HoverAffixDiamondSizePx);

            // Static indicator aligned to the existing grey diamond beside the first text line.
            float centerX = _cachedHoverAffixRect.X - HoverAffixDiamondLeftOffsetPx;
            float centerY = _cachedHoverAffixRect.Y + HoverAffixDiamondTopOffsetPx;

            DrawStaticDiamond(centerX, centerY, size);
        }


        private void DrawStatusText()
        {
            var vr = _vendorPage.Rectangle;
            float x = vr.X + OverlayTextLeftOffset;
            float y = vr.Y + HeaderTopOffset + OverlayTextTopGap;

            var lines = new List<string>();

            if (_running && _target != null)
                lines.Add("Target: " + WrapSingleLine(_target.Raw, 58));
            else if (!string.IsNullOrEmpty(_cachedHoverAffixText))
                lines.Add("Hover: " + WrapSingleLine(_cachedHoverAffixText, 58));
            else
                lines.Add("Hover desired ? affix, then press " + MysticEnchantHotkey + ".");

            lines.Add("Status: " + _lastStatus);

            if (ShowCycleTimingOverlay)
                lines.Add("Last cycle: " + _lastCycleElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms | " + CurrentProfile());

            var text = string.Join("\r\n", lines.ToArray());
            var panel = new RectangleF(x - 8.0f, y - 6.0f, 420.0f, 28.0f + (lines.Count * 11.0f));
            DrawRoundedRect(panel, 7.0f, _panelBrush, null);
            _infoFont.DrawText(text, x, y, false);
        }

        private static string WrapSingleLine(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;

            return text.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private void DrawPillButton(RectangleF rect, string text, bool active, bool enabled, bool flash)
        {
            IBrush fill = !enabled ? _buttonFillDisabledBrush : active ? _buttonFillActiveBrush : _buttonFillBrush;
            if (flash)
                fill = _buttonFillWarningBrush;

            DrawRoundedRect(rect, rect.Height * 0.45f, fill, _buttonBorderBrush);
            DrawCenteredText(rect, text, _buttonFont);
        }

        private void DrawStaticDiamond(float centerX, float centerY, float size)
        {
            if (size <= 0)
                return;

            float half = size * 0.5f;
            float topX = centerX;
            float topY = centerY - half;
            float rightX = centerX + half;
            float rightY = centerY;
            float bottomX = centerX;
            float bottomY = centerY + half;
            float leftX = centerX - half;
            float leftY = centerY;

            // Draw with cached line brushes only: no per-frame geometry allocation.
            DrawDiamondLines(_hoverDiamondBorderBrush, topX, topY, rightX, rightY, bottomX, bottomY, leftX, leftY);
            DrawDiamondLines(_hoverDiamondFillBrush, topX, topY, rightX, rightY, bottomX, bottomY, leftX, leftY);
        }

        private void DrawDiamondLines(IBrush brush, float topX, float topY, float rightX, float rightY, float bottomX, float bottomY, float leftX, float leftY)
        {
            if (brush == null)
                return;

            brush.DrawLine(topX, topY, rightX, rightY);
            brush.DrawLine(rightX, rightY, bottomX, bottomY);
            brush.DrawLine(bottomX, bottomY, leftX, leftY);
            brush.DrawLine(leftX, leftY, topX, topY);
        }



        private void DrawRoundedRect(RectangleF rect, float radius, IBrush fill, IBrush border)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            if (!UseRoundedGeometryButtons || _geometryDrawFailed)
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
            catch (Exception ex)
            {
                _geometryDrawFailed = true;

                if (!_geometryDrawFailureLogged)
                {
                    _geometryDrawFailureLogged = true;
                    DebugLog("Rounded geometry drawing failed. Falling back to rectangles. " + ex);
                }

                if (fill != null) fill.DrawRectangle(rect);
                if (border != null) border.DrawRectangle(rect);
            }
        }

        private void DrawRoundedSegment(RectangleF rect, float radius, IBrush fill)
        {
            DrawRoundedRect(rect, radius, fill, null);
        }

        private void DrawRoundedSegment(RectangleF rect, float radius, bool roundLeft, bool roundRight, IBrush fill)
        {
            if (fill == null || rect.Width <= 0 || rect.Height <= 0)
                return;

            if (!UseRoundedGeometryButtons || _geometryDrawFailed)
            {
                fill.DrawRectangle(rect);
                return;
            }

            try
            {
                using (var pg = Hud.Render.CreateGeometry())
                {
                    using (var gs = pg.Open())
                    {
                        BeginRoundedRectFigure(gs, rect, radius, roundLeft, roundRight, roundRight, roundLeft);
                        gs.Close();
                    }

                    fill.DrawGeometry(pg);
                }
            }
            catch (Exception ex)
            {
                _geometryDrawFailed = true;

                if (!_geometryDrawFailureLogged)
                {
                    _geometryDrawFailureLogged = true;
                    DebugLog("Rounded segment drawing failed. Falling back to rectangles. " + ex);
                }

                fill.DrawRectangle(rect);
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

        private void DrawCenteredText(RectangleF rect, string text, IFont font)
        {
            if (font == null || rect.Width <= 0 || rect.Height <= 0)
                return;

            var layout = font.GetTextLayout(text ?? string.Empty);
            float x = rect.X + (rect.Width - layout.Metrics.Width) * 0.5f;
            float y = rect.Y + (rect.Height - layout.Metrics.Height) * 0.5f - 0.5f;
            font.DrawText(layout, x, y);
        }

        private static bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && rect.Contains(x, y);
        }

        private void PrepareSettingsPath()
        {
            try
            {
                var pluginDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
                var settingsDir = System.IO.Path.Combine(pluginDir, "settings");

                System.IO.Directory.CreateDirectory(pluginDir);
                System.IO.Directory.CreateDirectory(settingsDir);

                _settingsPath = System.IO.Path.Combine(settingsDir, "s7o_MysticEnchant.ini");
                _legacySettingsPath = System.IO.Path.Combine(pluginDir, "s7o_MysticEnchant.settings.ini");
            }
            catch
            {
                _settingsPath = null;
                _legacySettingsPath = null;
            }
        }


        private string SelectSettingsReadPath()
        {
            try
            {
                if (!string.IsNullOrEmpty(_settingsPath) && System.IO.File.Exists(_settingsPath))
                    return _settingsPath;
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(_legacySettingsPath) && System.IO.File.Exists(_legacySettingsPath))
                    return _legacySettingsPath;
            }
            catch { }

            return _settingsPath;
        }

        private void LoadSettings()
        {
            if (!PersistUserSettings || string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                string readPath = SelectSettingsReadPath();

                if (string.IsNullOrEmpty(readPath) || !System.IO.File.Exists(readPath))
                    return;

                foreach (var raw in System.IO.File.ReadAllLines(readPath))
                {
                    var line = (raw ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    int idx = line.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();

                    if (key.Equals("MysticEnchantHotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        try { MysticEnchantHotkey = (Key)Enum.Parse(typeof(Key), value, true); } catch { }
                    }
                    else if (key.Equals("MaxEnchantAttempts", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(value, out parsed)) MaxEnchantAttempts = Math.Max(0, parsed);
                    }
                    else if (key.Equals("EnforceMaterialReserveChecks", StringComparison.OrdinalIgnoreCase))
                    {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) EnforceMaterialReserveChecks = parsed;
                    }
                    else if (key.Equals("ReservedGold", StringComparison.OrdinalIgnoreCase))
                    {
                        long parsed;
                        if (long.TryParse(value, out parsed)) ReservedGold = Math.Max(0, parsed);
                    }
                }

                try
                {
                    if (!string.Equals(
                        System.IO.Path.GetFullPath(readPath).TrimEnd('\\', '/'),
                        System.IO.Path.GetFullPath(_settingsPath).TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        SaveSettings();
                    }
                }
                catch { }
            }
            catch { }
        }

        private void SaveSettings()
        {
            if (!PersistUserSettings || string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                var lines = new List<string>();
                lines.Add("MysticEnchantHotkey=" + MysticEnchantHotkey);
                lines.Add("MaxEnchantAttempts=" + Math.Max(0, MaxEnchantAttempts));
                lines.Add("EnforceMaterialReserveChecks=" + EnforceMaterialReserveChecks);
                lines.Add("ReservedGold=" + Math.Max(0, ReservedGold));
                var dir = System.IO.Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllLines(_settingsPath, lines.ToArray());
            }
            catch { }
        }

        private void DebugLog(string message)
        {
            if (!DebugLogEnabled) return;

            try
            {
                Hud.TextLog.Log(DebugLogFileName, "[s7o_MysticEnchant] " + message, true, true);
            }
            catch { }
        }
    }

    public class s7o_MysticEnchantCustomizer : BasePlugin, ICustomizer
    {
        public override void Load(IController hud)
        {
            base.Load(hud);
            Enabled = true;
        }

        public void Customize()
        {
            var p = Hud.GetPlugin<s7o_MysticEnchantPlugin>();
            if (p == null) return;

            p.Enabled = true;

            // Do not assign ToggleKeyEvent here.
            // Do not overwrite MysticEnchantHotkey.
            // Do not force MaxEnchantAttempts here.
            // Defaults come from public fields and/or plugins/s7o/settings/s7o_MysticEnchant.ini.

            p.DebugLogEnabled = false;
            p.VerboseDebugLogging = false;
            p.ShowCycleTimingOverlay = false;
        }
    }
}
