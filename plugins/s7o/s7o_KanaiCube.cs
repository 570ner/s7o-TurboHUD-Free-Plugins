namespace Turbo.Plugins.s7o
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;
    using SharpDX.DirectInput;
    using SharpDX.Direct2D1;
    using Vector2 = SharpDX.Vector2;
    using Turbo.Plugins.Default;
    using System.Runtime.InteropServices;

    public abstract class s7o_KanaiInventoryManagementPlugin : BasePlugin
    {
        public bool TurnedOn { get; set; }
        public IKeyEvent ToggleKeyEvent { get; set; }
        public bool Running { get; protected set; }
        public IFont HeaderFont { get; set; }
        public IFont InfoFont { get; set; }
        public IBrush ItemHighlighBrush { get; set; }
        public bool InventoryLockForUpgradeToAncient { get; set; }
        public s7o_KanaiInventoryManagementPlugin() { Enabled = true; TurnedOn = false; Running = false; }
        public override void Load(IController hud)
        {
            base.Load(hud);
            ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, Key.F3, false, false, false);
            HeaderFont = Hud.Render.CreateFont("tahoma", 12, 255, 200, 200, 100, true, false, 255, 0, 0, 0, true);
            InfoFont = Hud.Render.CreateFont("tahoma", 7, 255, 200, 200, 0, true, false, 255, 0, 0, 0, true);
            ItemHighlighBrush = Hud.Render.CreateBrush(255, 200, 200, 100, -1.6f);
        }
    }

    public abstract class s7o_KanaiCubePluginBase : s7o_KanaiInventoryManagementPlugin
    {
        protected IUiElement vendorPage, transmuteDialog, pageNumber, transmuteButton, reciepeButton, fillButton, nextButton, prevButton, item1, item2;
        protected int[] PageIndexes { get; private set; }
        public s7o_KanaiCubePluginBase(params int[] pageIndexes) { PageIndexes = pageIndexes; }
        public override void Load(IController hud)
        {
            base.Load(hud);
            vendorPage = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage", Hud.Inventory.InventoryMainUiElement);
            transmuteDialog = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage.transmute_dialog", vendorPage);
            pageNumber = RegisterOrGetUiElement("Root.NormalLayer.Kanais_Recipes_main.LayoutRoot.PageControls.page_number", transmuteDialog);
            transmuteButton = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage.transmute_dialog.LayoutRoot.transmute_button", transmuteDialog);
            reciepeButton = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage.transmute_dialog.LayoutRoot.recipe_button", transmuteDialog);
            fillButton = RegisterOrGetUiElement("Root.NormalLayer.Kanais_Recipes_main.LayoutRoot.button_fill_currencies", transmuteDialog);
            nextButton = RegisterOrGetUiElement("Root.NormalLayer.Kanais_Recipes_main.LayoutRoot.PageControls.page_next", transmuteDialog);
            prevButton = RegisterOrGetUiElement("Root.NormalLayer.Kanais_Recipes_main.LayoutRoot.PageControls.page_previous", transmuteDialog);
            item1 = RegisterOrGetUiElement("Root.TopLayer.item 1.stack", transmuteDialog);
            item2 = RegisterOrGetUiElement("Root.TopLayer.item 2.stack", transmuteDialog);
        }
        protected IUiElement RegisterOrGetUiElement(string path, IUiElement parent)
        {
            if (string.IsNullOrEmpty(path) || Hud == null || Hud.Render == null) return null;
            try { return Hud.Render.RegisterUiElement(path, parent, null); }
            catch { try { return Hud.Render.GetUiElement(path); } catch { return null; } }
        }
        protected bool IsUiVisible(IUiElement element)
        {
            if (element == null) return false;
            try { element.Refresh(); return element.Visible; } catch { return false; }
        }
        protected string ReadUiTextSafe(IUiElement element)
        {
            if (element == null) return string.Empty;
            try { element.Refresh(); if (!element.Visible) return string.Empty; return element.ReadText(Encoding.UTF8, true) ?? string.Empty; }
            catch { return string.Empty; }
        }
        protected int GetPageNum()
        {
            if (!IsUiVisible(pageNumber)) return -1;
            var pageText = ReadUiTextSafe(pageNumber); if (string.IsNullOrEmpty(pageText)) return -1;
            var pageleft = Between(pageText, null, "，");
            if (string.IsNullOrEmpty(pageleft)) pageleft = Between(pageText, null, ",");
            if (string.IsNullOrEmpty(pageleft)) pageleft = Between(pageText, null, "/");
            if (string.IsNullOrEmpty(pageleft)) pageleft = Between(pageText, null, "из");
            if (string.IsNullOrEmpty(pageleft)) return -1;
            if (pageleft.Contains("10")) return 10; if (pageleft.Contains("1")) return 1; if (pageleft.Contains("2")) return 2; if (pageleft.Contains("3")) return 3; if (pageleft.Contains("4")) return 4; if (pageleft.Contains("5")) return 5; if (pageleft.Contains("6")) return 6; if (pageleft.Contains("7")) return 7; if (pageleft.Contains("8")) return 8; if (pageleft.Contains("9")) return 9;
            return -1;
        }
        protected static string Between(string str, string strLeft, string strRight)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            if (strLeft != null) { int indexLeft = str.IndexOf(strLeft); if (indexLeft < 0) return string.Empty; indexLeft += strLeft.Length; if (strRight != null) { int indexRight = str.IndexOf(strRight, indexLeft); if (indexRight < 0) return string.Empty; return str.Substring(indexLeft, indexRight - indexLeft); } return str.Substring(indexLeft); }
            if (strRight == null) return string.Empty; int indexRightOnly = str.IndexOf(strRight); if (indexRightOnly <= 0) return string.Empty; return str.Substring(0, indexRightOnly);
        }
        protected bool ValidateTransmuteTurnedOn(bool useInventoryLockArea)
        {
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.Me == null || !Hud.Window.IsForeground || !IsUiVisible(transmuteDialog) || !IsUiVisible(pageNumber) || (useInventoryLockArea && (Hud.Inventory.InventoryLockArea.Width <= 0 || Hud.Inventory.InventoryLockArea.Height <= 0))) TurnedOn = false;
            return TurnedOn;
        }
    }

    internal static class s7o_KanaiCubeInput
    {
        private const uint InputMouse = 0, LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
        public static bool TryGetCursorPos(out int x, out int y) { POINT p; if (GetCursorPos(out p)) { x = p.X; y = p.Y; return true; } x = 0; y = 0; return false; }
        public static bool MouseMoveScreen(int x, int y) { return SetCursorPos(x, y); }
        public static bool MouseMoveClient(int x, int y, int offsetX, int offsetY)
        {
            long screenX = (long)x + offsetX;
            long screenY = (long)y + offsetY;
            if (screenX < int.MinValue || screenX > int.MaxValue || screenY < int.MinValue || screenY > int.MaxValue)
                return false;
            return SetCursorPos((int)screenX, (int)screenY);
        }
        public static bool MouseDown(MouseButtons button) { if (button == MouseButtons.Left) return SendMouse(LeftDown); if (button == MouseButtons.Right) return SendMouse(RightDown); return false; }
        public static bool MouseUp(MouseButtons button) { if (button == MouseButtons.Left) return SendMouse(LeftUp); if (button == MouseButtons.Right) return SendMouse(RightUp); return false; }
        public static bool ClickAt(MouseButtons button, float x, float y, int offsetX, int offsetY) { if (!MouseMoveClient((int)Math.Round(x), (int)Math.Round(y), offsetX, offsetY)) return false; if (!MouseDown(button)) return false; return MouseUp(button); }
        public static bool ClickRect(RectangleF rect, MouseButtons button, int offsetX, int offsetY) { if (rect.Width <= 0 || rect.Height <= 0) return false; return ClickAt(button, rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f, offsetX, offsetY); }
        private static bool SendMouse(uint flags) { var input = new Input[1]; input[0].Type = InputMouse; input[0].U.Mouse.Flags = flags; input[0].U.Mouse.Dx = 0; input[0].U.Mouse.Dy = 0; input[0].U.Mouse.MouseData = 0; input[0].U.Mouse.Time = 0; input[0].U.Mouse.ExtraInfo = IntPtr.Zero; return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1; }
    }

    /// Kanai Cube native instance-level eligibility correction.
    /// Load this source as the only file defining s7o_KanaiCube.
    ///
    /// Architecture (do not regress):
    ///   AfterCollect Running guard MUST precede page validation. The intentional
    ///   page-N→N+1 bounce re-enters AfterCollect with a wrong page; validating
    ///   first was flipping TurnedOn=false prematurely.
    ///   Once page-right is clicked, page repair runs even if TurnedOn was cleared.
    ///   _p is frozen at run start so mid-run UI speed changes have no effect.
    ///   Skip recovery: after the snapshot queue finishes, a live cleanup pass
    ///   re-queries inventory and processes any items the queue pass missed.
    ///   Page 3 confirms target insertion from the occupied Cube slot, fills
    ///   immediately, then waits only for the native Transmute-ready state (54).
    ///   After one verified-ready click it overlaps acceptance/settlement with
    ///   page repair and verifies live material/target evidence on return.
    ///   Automation failures never become recipe-ineligible marks.
    public class s7o_KanaiCube : s7o_KanaiCubePluginBase,
        IKeyEventHandler, IInGameTopPainter, IAfterCollectHandler,
        IMouseClickHandler, INewAreaHandler
    {
        public string UpgradeRareEligibilityRevisionId
        {
            get
            {
                return "REV11-POST-FILL-READY-FAST-REPAIR-MULTI-MONITOR";
            }
        }

        private const int NoTick = int.MinValue;

        // ── Speed 1 base — intentionally slow/LightningMod-style normal speed ─
        public int MaxPageNavigationClicks    { get; set; } = 12;
        public int ToggleDebounceMs           { get; set; } = 750;

        // These Speed 1 values come from the former non-turbo/safe profile.
        // Speed 10 still uses the existing fastest profile floors below.
        public int BaseGlobalSleepMs              { get; set; } = 60;
        public int BasePostTransmuteExtraMs       { get; set; } = 125;
        public int BasePageArrowReadyTimeoutMs    { get; set; } = 220;
        public int BasePageRightConfirmTimeoutMs  { get; set; } = 360; // fixed — not lerped
        public int BasePageRightToLeftMinWaitMs   { get; set; } = 120;
        public int BasePageReturnConfirmTimeoutMs { get; set; } = 650; // fixed — not lerped
        public int BasePageReturnRetryWaitMs      { get; set; } = 90;
        public int BasePageReturnMaxRetries       { get; set; } = 4;
        public int BasePageArrowMouseDownMs       { get; set; } = 35;
        public int BasePageArrowPostClickMs       { get; set; } = 20;
        public int BasePageOpenClickWaitMs        { get; set; } = 100;

        // ── Speed Floors — Speed 10 (raise a floor if that timing fails) ────
        // Speed 10 keeps the previously-tested fastest timing floors.
        public int MinGlobalSleepMs           { get; set; } = 15; // log showed 29% skip rate at 5ms — right-click needs ~15ms before fill registers
        public int MinPostTransmuteExtraMs    { get; set; } = 8;
        public int MinPageArrowReadyTimeoutMs { get; set; } = 105; // observed slot-clear up to 88ms; 105 gives safe margin
        public int MinPageRightConfirmTimeoutMs  { get; set; } = 220;
        public int MinPageRightToLeftMs       { get; set; } = 0;
        public int MinPageReturnConfirmTimeoutMs { get; set; } = 330;
        public int MinPageReturnRetryWaitMs   { get; set; } = 5;
        public int MinPageArrowMouseDownMs    { get; set; } = 0;
        public int MinPageArrowPostClickMs    { get; set; } = 0;
        public int MinPageOpenClickWaitMs     { get; set; } = 5;

        // ── Kanai Cube UI ─────────────────────────────────────────────────────
        public int   SpeedLevel               { get; set; } = 10;     // 1–10
        public bool  ShowSpeedControl          { get; set; } = true;

        // ── Kanai Cube fixed header UI ───────────────────────────────────────────────
        // Default is F3, but this is saved independently from ItemSalvage.
        // New file path: plugins\s7o\settings\s7o_KanaiCube.ini. Old s7o_TurboCube settings are migrated.
        public Key KanaiCubeHotkey = Key.F3;

        public bool PersistUserSettings = true;
        public bool UseRoundedGeometryButtons = true;

        // Fixed pane-relative layout. These values target the screenshot layout:
        // left: KANAI CUBE + hotkey; right: speed control.
        public float HeaderLeftOffset = 82.0f;
        public float HeaderTopOffset = 38.0f;
        public float HeaderRightOffset = 74.0f;

        // Lower title/status text starts near the left margin of the cube pane.
        // This is intentionally separate from HeaderLeftOffset, which controls only
        // the top-left KANAI CUBE / hotkey group.
        public float OverlayTextLeftOffset = 24.0f;

        public float HeaderHotkeyGroupWidth = 92.0f;
        public float HotkeyButtonWidth = 42.0f;
        public float HotkeyButtonHeight = 18.0f;

        public float SpeedControlWidth = 112.0f;
        public float SpeedControlHeight = 20.0f;
        public float SpeedSideButtonWidth = 28.0f;

        // Header/status offset below the controls.
        public float OverlayTextTopGap = 44.0f;

        public int ButtonFlashMs = 90;

        // ── Behavior ─────────────────────────────────────────────────────────
        public int  MaxTransmutesPerRun          { get; set; } = 250;
        public bool UseRightClickInsert          { get; set; } = true;
        public bool UsePageFlipReset             { get; set; } = true;
        public bool DoubleClickFillButton        { get; set; } = false;
        public bool ShowOverlay                  { get; set; } = true;
        public bool UseSnapshotQueueForFastPages { get; set; } = true;
        public bool EnableReforgePage2           { get; set; } = true;
        public bool RestoreCursorAfterRun        { get; set; } = false;
        public int  Mode                         { get; set; } = 0;   // 0=stop at ancient/primal, 1=primal only

        private const int MaxUpgradeAttemptsPerItemPerRun = 2;
        private const int MaxInsertAttemptsPerTarget = 2;

        private const int InsertConfirmTimeoutMs = 400;
        private const int InsertRetryDelayMs = 45;

        private const int UpgradeFailureConfirmSamples = 3;
        private const int UpgradeRetryDelayMs = 35;

        // Page 3 timing endpoints. Speed 10 uses the validated gated fast path;
        // lower speed levels interpolate linearly toward the conservative Speed 1
        // values so the control remains predictable and natural.
        public int BasePage3InsertConfirmPollMs { get; set; } = 30;
        public int MinPage3InsertConfirmPollMs { get; set; } = 0;
        public int BasePage3PostInsertSettleMs { get; set; } = 60;
        public int MinPage3PostInsertSettleMs { get; set; } = 0;
        public int BasePage3PostFillSettleMs { get; set; } = 90;
        public int MinPage3PostFillSettleMs { get; set; } = 0;
        public int BasePage3TransmuteToRepairMs { get; set; } = 120;
        public int MinPage3TransmuteToRepairMs { get; set; } = 0;
        public int BasePage3FailureConfirmMs { get; set; } = 240;
        public int MinPage3FailureConfirmMs { get; set; } = 120;
        public int BasePage3FailureSampleMs { get; set; } = 70;
        public int MinPage3FailureSampleMs { get; set; } = 30;
        public int BasePage3SuccessSettleMs { get; set; } = 20;
        public int MinPage3SuccessSettleMs { get; set; } = 0;

        private const int Page3TransmutePollMs = 15;
        private const int Page3TransmuteConfirmTimeoutMs = 900;
        private const int Page3TransmuteRetryDelayMs = 35;
        private const int MaxPage3TransmuteClicksPerCycle = 2;
        private const int Page3TransmuteReadyAnimState = 54;
        private const int Page3TransmuteAcceptedAnimState = 51;
        private const int Page3NativeGatePollMs = 15;
        private const int Page3NativeGateMinTimeoutMs = 250;
        private const int Page3NativeGateMaxTimeoutMs = 900;

        // ── Diagnostics ───────────────────────────────────────────────────────
        public bool   DebugLogEnabled             { get; set; } = false;
        public bool   VerboseDebugLogging         { get; set; } = false; // gates per-click detail logs
        public bool   DebugLogManualClicks        { get; set; } = false;
        public bool   DebugLogUiRectsOnPageClicks { get; set; } = false;
        public bool   ShowCycleTimingOverlay      { get; set; } = false;
        public string DebugLogFileName            { get; set; } = "KanaiCubeDebug.log";

        // ── Private — Active timing snapshot (frozen per run) ────────────────
        private struct TimingProfile
        {
            public string Label;
            public int Sleep, PostTrans, ArrowReady, RightConfirm, RightToLeft;
            public int ReturnConfirm, RetryWait, MaxRetries, ArrowDown, PostClick, OpenWait;
            public int P3InsertPoll, P3InsertToFill, P3FillToTransmute;
            public int P3TransmuteToRepair, P3FailureConfirm, P3FailureSample, P3SuccessSettle;
            public override string ToString() =>
                Label + "{sleep=" + Sleep + ",postTrans=" + PostTrans
                + ",arrowReady=" + ArrowReady + ",rightToLeft=" + RightToLeft
                + ",retryWait=" + RetryWait + ",arrowDown=" + ArrowDown
                + ",postClick=" + PostClick
                + ",p3=" + P3InsertPoll + "/" + P3InsertToFill + "/"
                + P3FillToTransmute + "/" + P3TransmuteToRepair + "}";
        }

        private class Slot { public RectangleF Rect; public int X, Y; public ItemQuality Quality; public string Uid; }

        private TimingProfile _p;
        private int  _runId, _cycleId;
        private bool _sessionLogged;
        private string _settingsPath;
        private string _legacySettingsPath;
        private string _oldSettingsPath;
        private string _oldTurboSettingsPath;
        private string _oldTurboPluginSettingsPath;
        private string _oldTurboRootSettingsPath;
        private RectangleF _lastProcessedRect;
        private readonly List<string> _startAncientIds = new List<string>();
        private List<RectangleF> _lastHighlightRects = new List<RectangleF>();
        private int _toggleTick;

        private RectangleF _hotkeyButtonRect = RectangleF.Empty;
        private RectangleF _speedMinusRect = RectangleF.Empty;
        private RectangleF _speedPlusRect = RectangleF.Empty;
        private RectangleF _speedValueRect = RectangleF.Empty;
        private RectangleF _speedControlRect = RectangleF.Empty;

        private IFont _yellowFont;
        private IFont _buttonFont;

        private IBrush _pillDarkBrush;
        private IBrush _pillLightBrush;
        private IBrush _pillGreenBrush;
        private IBrush _pillGreenLightBrush;
        private IBrush _pillOrangeBorderBrush;
        private IBrush _pillOrangeSeparatorBrush;

        private IBrush _ineligibleOutlineBrush;
        private IBrush _ineligibleBodyBrush;
        private IBrush _ineligibleHighlightBrush;

        private bool _capturingHotkey;
        private bool _overlayControlsVisible;
        private bool _geometryDrawFailed;
        private bool _geometryDrawFailureLogged;

        private int _minusFlashUntilTick = NoTick;
        private int _plusFlashUntilTick = NoTick;

        private string _header, _info, _noItem, _running, _lockMissing;
        private enum CubeStage { Idle, EnsurePage, AcquireTarget, InsertTarget, ConfirmInsert, Fill1, Fill2, WaitPage3TransmuteReady, Transmute, ConfirmPage3Transmute, FlipWaitReadyNext, FlipClickNext, FlipWaitNextConfirm, FlipRightToLeftDelay, FlipWaitReadyPrev, FlipClickPrev, FlipWaitPrevConfirm, OpenPageRecovery, PostCycleEvaluate, Finish, PageArrowDown, PageArrowUp }

        private enum UpgradeRareEligibility
        {
            Eligible,
            IneligibleLevel,
            IneligibleType,
            UnknownLevel
        }

        private sealed class CubeTarget { public RectangleF Rect; public string Uid; public string Key; public IItem Item; }
        private CubeStage _stage = CubeStage.Idle, _afterArrowStage = CubeStage.Idle;
        private int _nextActionTick, _deadlineTick, _afterArrowDelayMs, _runPage, _doneThisRun, _snapshotIndex, _snapshotLimit, _openPageClicks, _flipPrevAttempts, _savedCursorX, _savedCursorY, _cachedOverlayPage = -1, _cachedCandidateCount;
        private int _nextOverlayCacheRefreshTick = NoTick;
        private bool _repairingPageAfterNext;
        private IUiElement _pendingArrowButton;
        private string _pendingArrowLabel, _cachedStatusText = string.Empty;
        private int _cycleStartTick, _lastCycleElapsedMs;
        private CubeTarget _target;
        private List<Slot> _snapshotQueue;
        private System.Collections.Generic.HashSet<string> _skippedThisRun;
        private Dictionary<string, int> _upgradeFailuresThisRun;
        private HashSet<string> _eligibilityLoggedThisRun;

        private int _insertAttemptsForTarget;

        private int _page3TransmuteClicks;
        private int _page3TransmuteClickTick = NoTick;
        private bool _page3SawOccupiedSlot;
        private bool _page3SawNotReadySinceInsert;
        private bool _page3ReadyEdgeSeen;
        private bool _page3TransmuteAccepted;
        private int _page3AnimBeforeInsert = int.MinValue;
        private bool _page3PreflightChecked;
        private CubeStage _pageRepairResumeStage = CubeStage.PostCycleEvaluate;
        private long _page3BeforeDeathsBreath;
        private long _page3BeforeReusableParts;
        private long _page3BeforeArcaneDust;
        private long _page3BeforeVeiledCrystal;

        private string _postFailureKey = string.Empty;
        private int _postFailureSinceTick = NoTick;
        private int _postFailureSamples;

        // ── Constructor ───────────────────────────────────────────────────────
        public s7o_KanaiCube() : base(2, 3, 7, 8, 9) { Enabled = true; }

        // =========================================================
        // Load
        // =========================================================

        public override void Load(IController hud)
        {
            base.Load(hud);
            if (ToggleKeyEvent == null || IsReservedFallbackHotkey(ToggleKeyEvent)) ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, Key.F3, false, false, false);

            _yellowFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 220, 0, true, false, 255, 0, 0, 0, true);
            _buttonFont = Hud.Render.CreateFont("tahoma", 8.0f, 255, 235, 235, 235, true, false, 255, 0, 0, 0, true);

            _pillDarkBrush = Hud.Render.CreateBrush(255, 48, 48, 48, 0);
            _pillLightBrush = Hud.Render.CreateBrush(80, 125, 125, 125, 0);
            _pillGreenBrush = Hud.Render.CreateBrush(255, 0, 170, 60, 0);
            _pillGreenLightBrush = Hud.Render.CreateBrush(90, 120, 255, 150, 0);
            _pillOrangeBorderBrush = Hud.Render.CreateBrush(225, 105, 55, 10, 0);
            _pillOrangeSeparatorBrush = Hud.Render.CreateBrush(190, 120, 65, 15, 1);

            _ineligibleOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 6.0f);
            _ineligibleBodyBrush = Hud.Render.CreateBrush(245, 165, 24, 24, 3.6f);
            _ineligibleHighlightBrush = Hud.Render.CreateBrush(225, 255, 125, 100, 1.25f);

            try
            {
                var pluginDir = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
                var settingsDir = System.IO.Path.Combine(pluginDir, "settings");

                System.IO.Directory.CreateDirectory(pluginDir);
                System.IO.Directory.CreateDirectory(settingsDir);

                _settingsPath = System.IO.Path.Combine(settingsDir, "s7o_KanaiCube.ini");
                _legacySettingsPath = System.IO.Path.Combine(pluginDir, "s7o_KanaiCube.settings.ini");
                _oldSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "s7o_KanaiCube.settings.txt");
                _oldTurboSettingsPath = System.IO.Path.Combine(settingsDir, "s7o_TurboCube.ini");
                _oldTurboPluginSettingsPath = System.IO.Path.Combine(pluginDir, "s7o_TurboCube.settings.ini");
                _oldTurboRootSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "s7o_Turbo.Kanai.Cube.settings.txt");
            }
            catch
            {
                _settingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings", "s7o_KanaiCube.ini");
                _legacySettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "s7o_KanaiCube.settings.ini");
                _oldSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "s7o_KanaiCube.settings.txt");
                _oldTurboSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "settings", "s7o_TurboCube.ini");
                _oldTurboPluginSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", "s7o_TurboCube.settings.ini");
                _oldTurboRootSettingsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "s7o_Turbo.Kanai.Cube.settings.txt");
            }
            LoadSettings();
            _p = CurrentProfile();
            ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, KanaiCubeHotkey, false, false, false);

            Log("LOAD hotkey=" + KanaiCubeHotkey
                + " verbose=" + VerboseDebugLogging
                + " speed=" + SpeedLevel
                + " settings=" + _settingsPath
                + " profile=" + _p);
        }

        // =========================================================
        // Key Handler
        // =========================================================

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed)
                return;

            if (_capturingHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHotkey = false;
                    Log("Hotkey capture cancelled.");
                    return;
                }

                KanaiCubeHotkey = keyEvent.Key;
                ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, KanaiCubeHotkey, false, false, false);
                _capturingHotkey = false;
                SaveSettings();

                Log("Kanai Cube hotkey changed to " + KanaiCubeHotkey);
                return;
            }

            if (ToggleKeyEvent == null || !ToggleKeyEvent.Matches(keyEvent))
                return;

            Log("KEY " + (ToggleKeyEvent?.ToString() ?? "?")
                + " Running=" + Running
                + " TurnedOn=" + TurnedOn
                + " page=" + PageNum());

            if (Running)
            {
                TurnedOn = false;
                ReleaseMouseButtons();
                Log("KEY stop requested");
                return;
            }

            if (!Debounce(ref _toggleTick, ToggleDebounceMs))
                return;

            var page = PageNumDirect();

            if (!TurnedOn && !SupportedPage(page))
            {
                Log("KEY ignored: unsupported page=" + page);
                return;
            }

            TurnedOn = !TurnedOn;
            Log("KEY TurnedOn=" + TurnedOn + " page=" + page);

            if (TurnedOn && page == 2)
                SnapshotAncients();

            if (!TurnedOn)
                _startAncientIds.Clear();
        }

        // =========================================================
        // Painter
        // =========================================================

        public void PaintTopInGame(
            ClipState clipState)
        {
            if (clipState != ClipState.Inventory)
                return;

            if (!Hud.Game.IsInGame ||
                vendorPage == null ||
                transmuteDialog == null)
            {
                return;
            }

            if (!IsVisible(transmuteDialog))
                return;

            int visiblePage = PageNumDirect();

            // The eligibility mark is independent of the optional header/status
            // overlay and should appear as soon as page 3 becomes readable.
            if (visiblePage == 3)
                DrawUpgradeIneligibleMarks();

            if (!ShowOverlay)
                return;

            if (!UpdateOverlayLayoutRects())
            {
                _overlayControlsVisible = false;
                return;
            }

            _overlayControlsVisible = true;

            RectangleF pane = vendorPage.Rectangle;

            if (pane.Width <= 0 ||
                pane.Height <= 0)
            {
                return;
            }

            DrawHeaderHotkey();
            DrawHeaderSpeedControl();

            int page = visiblePage;

            bool supported =
                pageNumber != null &&
                IsVisible(pageNumber) &&
                SupportedPage(page);

            float x0 = pane.X + OverlayTextLeftOffset;
            float y = pane.Y + HeaderTopOffset + OverlayTextTopGap;

            if (supported)
            {
                BuildOverlayText(page);
                var h = HeaderFont.GetTextLayout(_header);
                HeaderFont.DrawText(h, x0, y);
                y += h.Metrics.Height * 1.25f;
            }

            if (!supported)
                return;

            if (page == 2 &&
                InventoryLockForUpgradeToAncient &&
                Hud.Inventory.InventoryLockArea.Width <= 0)
            {
                InfoFont.DrawText(
                    InfoFont.GetTextLayout(_lockMissing),
                    x0,
                    y);

                return;
            }

            foreach (var rect in _lastHighlightRects)
                ItemHighlighBrush.DrawRectangle(rect);

            var txt = string.IsNullOrEmpty(
                _cachedStatusText)
                    ? (Running ? _running : _info)
                    : _cachedStatusText;

            InfoFont.DrawText(
                InfoFont.GetTextLayout(txt),
                x0,
                y);
        }

        private void DrawUpgradeIneligibleMarks()
        {
            if (_ineligibleOutlineBrush == null ||
                _ineligibleBodyBrush == null ||
                _ineligibleHighlightBrush == null)
            {
                return;
            }

            foreach (var item in
                Hud.Inventory.ItemsInInventory)
            {
                if (!ShouldDrawUpgradeIneligibleX(item))
                    continue;

                RectangleF rect =
                    Hud.Inventory.GetItemRect(item);

                if (rect.Width <= 0 ||
                    rect.Height <= 0)
                {
                    continue;
                }

                DrawUpgradeIneligibleX(rect);
            }
        }

        private void DrawUpgradeIneligibleX(
            RectangleF rect)
        {
            float size =
                Math.Min(rect.Width, rect.Height);

            // Shorten the X so it does not cover the whole item.
            float inset = size * 0.24f;

            float left = rect.Left + inset;
            float right = rect.Right - inset;
            float top = rect.Top + inset;
            float bottom = rect.Bottom - inset;

            // Thick black contrast outline.
            DrawXLayer(
                _ineligibleOutlineBrush,
                left,
                top,
                right,
                bottom,
                0.0f,
                0.0f);

            // Main deep-red body.
            DrawXLayer(
                _ineligibleBodyBrush,
                left,
                top,
                right,
                bottom,
                0.0f,
                0.0f);

            // Thin upper-left lighting line.
            DrawXLayer(
                _ineligibleHighlightBrush,
                left,
                top,
                right,
                bottom,
                -0.7f,
                -0.7f);
        }

        private void DrawXLayer(
            IBrush brush,
            float left,
            float top,
            float right,
            float bottom,
            float offsetX,
            float offsetY)
        {
            if (brush == null)
                return;

            brush.DrawLine(
                left + offsetX,
                top + offsetY,
                right + offsetX,
                bottom + offsetY);

            brush.DrawLine(
                right + offsetX,
                top + offsetY,
                left + offsetX,
                bottom + offsetY);
        }

        // =========================================================
        // AfterCollect
        // =========================================================

        public void AfterCollect()
        {
            int now = Environment.TickCount;

            if (_capturingHotkey && (transmuteDialog == null || !IsVisible(transmuteDialog) || !Hud.Window.IsForeground))
                _capturingHotkey = false;

            if (Running)
            {
                AdvanceRun(now);
                return;
            }

            // When the cube UI is not visible, avoid page parsing and inventory/candidate scans.
            if (transmuteDialog == null || pageNumber == null || !IsVisible(transmuteDialog) || !IsVisible(pageNumber))
            {
                if (TurnedOn)
                    TurnedOn = false;

                _overlayControlsVisible = false;
                return;
            }

            var page = PageNumDirect();
            RefreshOverlayCache(page, now);

            if (!ValidateTurnedOn(page))
                return;

            BeginRun(page, now);
            AdvanceRun(now);
        }

        // =========================================================
        // FREEHUD State Machine
        // =========================================================

        private static bool TickReached(int now, int tick)
        {
            return tick == 0 || tick == NoTick || unchecked(now - tick) >= 0;
        }

        private static bool TickIsFuture(int now, int untilTick)
        {
            return untilTick != 0 && untilTick != NoTick && unchecked(now - untilTick) < 0;
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

        private bool SlotsClear()
        {
            return !IsVisible(item1) && !IsVisible(item2);
        }

        private bool PageArrowReadyOrTimedOut(int now)
        {
            EnsureDeadline(now, _p.ArrowReady);
            return SlotsClear() || DeadlineExpired(now);
        }

        private void BeginRun(int page, int now)
        {
            _p = CurrentProfile();
            Running = true;
            _runId++;
            _cycleId = 0;

            _runPage = page;
            _doneThisRun = 0;
            _snapshotIndex = 0;
            _snapshotLimit = 0;
            _openPageClicks = 0;
            _flipPrevAttempts = 0;
            _page3PreflightChecked = false;
            _pageRepairResumeStage = CubeStage.PostCycleEvaluate;

            _target = null;
            _snapshotQueue = null;
            _skippedThisRun = new System.Collections.Generic.HashSet<string>();
            _upgradeFailuresThisRun = new Dictionary<string, int>();
            _eligibilityLoggedThisRun = new HashSet<string>();

            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();
            ResetPostFailureConfirmation();

            _pendingArrowButton = null;
            _pendingArrowLabel = null;
            _afterArrowStage = CubeStage.Idle;
            _afterArrowDelayMs = 0;

            _repairingPageAfterNext = false;
            ClearDeadline();

            if (!s7o_KanaiCubeInput.TryGetCursorPos(out _savedCursorX, out _savedCursorY))
            {
                long fallbackX = (long)Hud.Window.CursorX + Hud.Window.Offset.X;
                long fallbackY = (long)Hud.Window.CursorY + Hud.Window.Offset.Y;
                _savedCursorX = fallbackX >= int.MinValue && fallbackX <= int.MaxValue ? (int)fallbackX : 0;
                _savedCursorY = fallbackY >= int.MinValue && fallbackY <= int.MaxValue ? (int)fallbackY : 0;
            }

            if (page == 2 && _startAncientIds.Count == 0)
                SnapshotAncients();

            if (UseSnapshotQueueForFastPages && page != 2)
            {
                _snapshotQueue = BuildQueue(page);
                _snapshotLimit = _snapshotQueue.Count;

                var matMax = MaxByMaterials(page);
                if (matMax >= 0 && matMax < _snapshotLimit)
                    _snapshotLimit = matMax;

                if (MaxTransmutesPerRun > 0 && MaxTransmutesPerRun < _snapshotLimit)
                    _snapshotLimit = MaxTransmutesPerRun;

                Log("QUEUE page=" + page
                    + " planned=" + _snapshotQueue.Count
                    + " limit=" + _snapshotLimit);
            }

            _stage = CubeStage.EnsurePage;
            Delay(now, 0);

            Log("RUN #" + _runId + " start page=" + page + " profile=" + _p);
        }

        private void EndRun(string reason)
        {
            ReleaseMouseButtons();
            RestoreCursor(_savedCursorX, _savedCursorY);

            _startAncientIds.Clear();
            _lastProcessedRect = RectangleF.Empty;

            _target = null;
            _snapshotQueue = null;
            _skippedThisRun = null;
            _upgradeFailuresThisRun = null;
            _eligibilityLoggedThisRun = null;
            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();
            ResetPostFailureConfirmation();

            _pendingArrowButton = null;
            _pendingArrowLabel = null;
            _afterArrowStage = CubeStage.Idle;

            _repairingPageAfterNext = false;
            _page3PreflightChecked = false;
            _pageRepairResumeStage = CubeStage.PostCycleEvaluate;

            ClearDeadline();

            TurnedOn = false;
            Running = false;
            _stage = CubeStage.Idle;

            Log("RUN #" + _runId + " done: " + reason);
        }

        private void ResetRuntimeStateForInterruptedRun(bool releaseOwnedMouseButtons)
        {
            if (releaseOwnedMouseButtons)
                ReleaseMouseButtons();

            TurnedOn = false;
            Running = false;
            _stage = CubeStage.Idle;

            _target = null;
            _snapshotQueue = null;
            _skippedThisRun = null;
            _upgradeFailuresThisRun = null;
            _eligibilityLoggedThisRun = null;
            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();
            ResetPostFailureConfirmation();

            _pendingArrowButton = null;
            _pendingArrowLabel = null;
            _afterArrowStage = CubeStage.Idle;

            _repairingPageAfterNext = false;
            _page3PreflightChecked = false;
            _pageRepairResumeStage = CubeStage.PostCycleEvaluate;
            _lastProcessedRect = RectangleF.Empty;

            _startAncientIds.Clear();

            ClearDeadline();
        }

        private bool ShouldReleaseMouseButtonsForInterruptedRun()
        {
            try
            {
                if (Running)
                    return true;

                if (_stage != CubeStage.Idle)
                    return true;

                if (_afterArrowStage != CubeStage.Idle)
                    return true;

                if (_pendingArrowButton != null)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ResetRuntimeStateForInterruptedRun(ShouldReleaseMouseButtonsForInterruptedRun());
        }

        public void ForceStopForDisable()
        {
            try
            {
                if (Running)
                {
                    EndRun("disabled");
                    return;
                }

                ResetRuntimeStateForInterruptedRun(ShouldReleaseMouseButtonsForInterruptedRun());
            }
            catch { }
        }

        private bool IsValidCubeAutomationContext(out string reason)
        {
            reason = null;

            if (!Enabled) { reason = "plugin disabled"; return false; }
            if (Hud == null || Hud.Game == null || Hud.Window == null || Hud.Inventory == null) { reason = "hud unavailable"; return false; }
            if (!Hud.Game.IsInGame) { reason = "not in game"; return false; }
            if (Hud.Game.IsLoading) { reason = "loading"; return false; }
            if (Hud.Game.IsPaused) { reason = "paused"; return false; }
            if (Hud.Game.Me == null) { reason = "player unavailable"; return false; }
            if (!Hud.Window.IsForeground) { reason = "window not foreground"; return false; }

            if (transmuteDialog == null || !IsVisible(transmuteDialog))
            {
                reason = "cube dialog hidden";
                return false;
            }

            if (pageNumber == null || !IsVisible(pageNumber))
            {
                reason = "page number hidden";
                return false;
            }

            return true;
        }

        private void AdvanceRun(int now)
        {
            if (!TickReached(now, _nextActionTick))
                return;

            if (!TurnedOn && !_repairingPageAfterNext)
            {
                EndRun("stopped");
                return;
            }

            string reason;
            if (!IsValidCubeAutomationContext(out reason))
            {
                EndRun(reason);
                return;
            }

            RefreshOverlayCache(_runPage, now);

            switch (_stage)
            {
                case CubeStage.Idle:
                    EndRun("idle");
                    return;

                case CubeStage.EnsurePage:
                    AdvanceEnsurePage(now);
                    return;

                case CubeStage.AcquireTarget:
                    AdvanceAcquireTarget(now);
                    return;

                case CubeStage.InsertTarget:
                    AdvanceInsertTarget(now);
                    return;

                case CubeStage.ConfirmInsert:
                    AdvanceConfirmInsert(now);
                    return;

                case CubeStage.Fill1:
                    if (!ClickUi(fillButton, "FILL"))
                    {
                        Delay(now, 30);
                        return;
                    }

                    if (_runPage == 3)
                    {
                        ClearDeadline();
                        _stage = CubeStage.WaitPage3TransmuteReady;
                        Delay(now, _p.P3FillToTransmute);
                    }
                    else if (DoubleClickFillButton)
                    {
                        _stage = CubeStage.Fill2;
                        Delay(now, 10);
                    }
                    else
                    {
                        _stage = CubeStage.Transmute;
                        Delay(now, _p.Sleep);
                    }
                    return;

                case CubeStage.Fill2:
                    if (!ClickUi(fillButton, "FILL2"))
                    {
                        Delay(now, 30);
                        return;
                    }

                    _stage = CubeStage.Transmute;
                    Delay(now, _p.Sleep);
                    return;

                case CubeStage.WaitPage3TransmuteReady:
                    AdvanceWaitPage3TransmuteReady(now);
                    return;

                case CubeStage.Transmute:
                    if (_runPage == 3)
                    {
                        if (ReadUiAnimState(transmuteButton) !=
                            Page3TransmuteReadyAnimState)
                        {
                            ClearDeadline();
                            _stage = CubeStage.WaitPage3TransmuteReady;
                            Delay(now, 0);
                            return;
                        }

                        SnapshotPage3Materials();
                    }

                    if (!ClickUi(transmuteButton, "TRANSMUTE"))
                    {
                        Delay(now, 30);
                        return;
                    }

                    ClearDeadline();

                    if (_runPage == 3)
                    {
                        _page3TransmuteClicks++;
                        _page3TransmuteClickTick = now;
                        _page3TransmuteAccepted = false;

                        // The button was verified ready immediately before the
                        // click. Do not block on 54→51 here: the normal page-arrow
                        // gate already waits for slot clear (or its bounded timeout),
                        // while the return pass verifies materials/target removal.
                        if (UsePageFlipReset)
                        {
                            _stage = CubeStage.FlipWaitReadyNext;
                            Delay(now, 0);
                        }
                        else
                        {
                            _stage = CubeStage.ConfirmPage3Transmute;
                            Delay(now, 0);
                        }
                    }
                    else if (UsePageFlipReset)
                    {
                        _stage = CubeStage.FlipWaitReadyNext;
                        Delay(now, _p.Sleep + _p.PostTrans);
                    }
                    else
                    {
                        _stage = CubeStage.OpenPageRecovery;
                        Delay(now, _p.Sleep + _p.PostTrans);
                    }
                    return;

                case CubeStage.ConfirmPage3Transmute:
                    AdvanceConfirmPage3Transmute(now);
                    return;

                case CubeStage.FlipWaitReadyNext:
                    if (PageArrowReadyOrTimedOut(now))
                    {
                        ClearDeadline();
                        _stage = CubeStage.FlipClickNext;
                        Delay(now, 0);
                    }
                    return;

                case CubeStage.FlipClickNext:
                    _repairingPageAfterNext = true;
                    ClearDeadline();
                    StartPageArrowClick(nextButton, "NEXT", CubeStage.FlipWaitNextConfirm, _p.PostClick);
                    Delay(now, 0);
                    return;

                case CubeStage.FlipWaitNextConfirm:
                {
                    EnsureDeadline(now, _p.RightConfirm);

                    var p = ReadPage();
                    if (p > 0 && p != _runPage)
                    {
                        ClearDeadline();
                        _stage = CubeStage.FlipRightToLeftDelay;
                        Delay(now, _p.RightToLeft);
                        return;
                    }

                    if (DeadlineExpired(now))
                    {
                        ClearDeadline();
                        Log("FLIP NEXT not confirmed; using OpenPage recovery");
                        _stage = CubeStage.OpenPageRecovery;
                        _openPageClicks = 0;
                        Delay(now, 0);
                    }
                    return;
                }

                case CubeStage.FlipRightToLeftDelay:
                    _flipPrevAttempts = 0;
                    ClearDeadline();
                    _stage = CubeStage.FlipWaitReadyPrev;
                    Delay(now, 0);
                    return;

                case CubeStage.FlipWaitReadyPrev:
                    if (PageArrowReadyOrTimedOut(now))
                    {
                        ClearDeadline();
                        _stage = CubeStage.FlipClickPrev;
                        Delay(now, 0);
                    }
                    return;

                case CubeStage.FlipClickPrev:
                    if (_flipPrevAttempts >= _p.MaxRetries)
                    {
                        ClearDeadline();
                        Log("FLIP retries exhausted; using OpenPage recovery");
                        _stage = CubeStage.OpenPageRecovery;
                        _openPageClicks = 0;
                        Delay(now, 0);
                        return;
                    }

                    _flipPrevAttempts++;
                    ClearDeadline();
                    StartPageArrowClick(prevButton, "PREV#" + _flipPrevAttempts, CubeStage.FlipWaitPrevConfirm, _p.PostClick);
                    Delay(now, 0);
                    return;

                case CubeStage.FlipWaitPrevConfirm:
                {
                    EnsureDeadline(now, _p.ReturnConfirm);

                    var p = ReadPage();
                    if (p == _runPage)
                    {
                        ClearDeadline();
                        _repairingPageAfterNext = false;

                        if (!TurnedOn)
                        {
                            EndRun("stopped after page repair");
                            return;
                        }

                        ResumeAfterPageRepair(now);
                        return;
                    }

                    if (DeadlineExpired(now))
                    {
                        ClearDeadline();

                        var vis = ReadPage();
                        if (vis > 0 && vis < _runPage)
                        {
                            Log("FLIP overshot; using OpenPage recovery");
                            _stage = CubeStage.OpenPageRecovery;
                            _openPageClicks = 0;
                            Delay(now, 0);
                            return;
                        }

                        _stage = CubeStage.FlipWaitReadyPrev;
                        Delay(now, _p.RetryWait);
                    }
                    return;
                }

                case CubeStage.OpenPageRecovery:
                    AdvanceOpenPageRecovery(now);
                    return;

                case CubeStage.PostCycleEvaluate:
                    AdvancePostCycleEvaluate(now);
                    return;

                case CubeStage.PageArrowDown:
                    AdvancePageArrowDown(now);
                    return;

                case CubeStage.PageArrowUp:
                    AdvancePageArrowUp(now);
                    return;

                case CubeStage.Finish:
                    EndRun("finished");
                    return;
            }
        }

        private void AdvanceEnsurePage(int now)
        {
            if (!IsVisible(pageNumber))
            {
                Delay(now, _p.OpenWait);
                return;
            }

            var cur = PageNumDirect();

            if (cur == _runPage)
            {
                ClearDeadline();
                _openPageClicks = 0;
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            if (_openPageClicks >= MaxPageNavigationClicks)
            {
                EndRun("cannot open page " + _runPage);
                return;
            }

            if (cur <= 0)
            {
                Delay(now, _p.OpenWait);
                return;
            }

            if (!PageArrowReadyOrTimedOut(now))
                return;

            ClearDeadline();
            _openPageClicks++;

            var forward = cur < _runPage;
            StartPageArrowClick(forward ? nextButton : prevButton, forward ? "NEXT" : "PREV", CubeStage.EnsurePage, _p.OpenWait);
            Delay(now, 0);
        }

        private void AdvanceOpenPageRecovery(int now)
        {
            _repairingPageAfterNext = true;

            if (!IsVisible(pageNumber))
            {
                Delay(now, _p.OpenWait);
                return;
            }

            var cur = PageNumDirect();

            if (cur == _runPage)
            {
                ClearDeadline();
                _repairingPageAfterNext = false;

                if (!TurnedOn)
                {
                    EndRun("stopped after page recovery");
                    return;
                }

                _openPageClicks = 0;
                ResumeAfterPageRepair(now);
                return;
            }

            if (_openPageClicks >= MaxPageNavigationClicks)
            {
                EndRun("page recovery failed");
                return;
            }

            if (cur <= 0)
            {
                Delay(now, _p.OpenWait);
                return;
            }

            if (!PageArrowReadyOrTimedOut(now))
                return;

            ClearDeadline();
            _openPageClicks++;

            var forward = cur < _runPage;
            StartPageArrowClick(forward ? nextButton : prevButton, forward ? "NEXT" : "PREV", CubeStage.OpenPageRecovery, _p.OpenWait);
            Delay(now, 0);
        }

        private void StartPageArrowClick(IUiElement button, string label, CubeStage afterArrowStage, int afterArrowDelayMs)
        {
            _pendingArrowButton = button;
            _pendingArrowLabel = label;
            _afterArrowStage = afterArrowStage;
            _afterArrowDelayMs = Math.Max(0, afterArrowDelayMs);
            _stage = CubeStage.PageArrowDown;
        }

        private void AdvancePageArrowDown(int now)
        {
            if (_pendingArrowButton == null)
            {
                _stage = CubeStage.OpenPageRecovery;
                Delay(now, 0);
                return;
            }

            ReleaseMouseButtons();

            var r = _pendingArrowButton.Rectangle;
            if (r.Width <= 0 || r.Height <= 0)
            {
                _stage = CubeStage.OpenPageRecovery;
                Delay(now, 0);
                return;
            }

            var x = (int)Math.Round(r.Left + r.Width * 0.5f);
            var y = (int)Math.Round(r.Top + r.Height * 0.5f);

            if (!s7o_KanaiCubeInput.MouseMoveClient(x, y, Hud.Window.Offset.X, Hud.Window.Offset.Y) || !s7o_KanaiCubeInput.MouseDown(MouseButtons.Left))
            {
                ReleaseMouseButtons();
                Delay(now, 30);
                return;
            }

            if (_p.ArrowDown <= 0)
            {
                s7o_KanaiCubeInput.MouseUp(MouseButtons.Left);
                LogV("PAGECLK " + _pendingArrowLabel);

                _pendingArrowButton = null;
                _pendingArrowLabel = null;

                var next = _afterArrowStage;
                var delay = _afterArrowDelayMs;

                _afterArrowStage = CubeStage.Idle;
                _afterArrowDelayMs = 0;
                _stage = next;
                Delay(now, delay);
                return;
            }

            Delay(now, _p.ArrowDown);
            _stage = CubeStage.PageArrowUp;
        }

        private void AdvancePageArrowUp(int now)
        {
            s7o_KanaiCubeInput.MouseUp(MouseButtons.Left);

            LogV("PAGECLK " + _pendingArrowLabel);

            _pendingArrowButton = null;
            _pendingArrowLabel = null;

            var next = _afterArrowStage;
            var delay = _afterArrowDelayMs;

            _afterArrowStage = CubeStage.Idle;
            _afterArrowDelayMs = 0;

            _stage = next;
            Delay(now, delay);
        }

        private void AdvanceAcquireTarget(int now)
        {

            if (!ValidateTurnedOn(_runPage))
            {
                EndRun("validation failed");
                return;
            }

            var cur = PageNumDirect();
            if (cur > 0 && cur != _runPage)
            {
                _stage = CubeStage.EnsurePage;
                Delay(now, 0);
                return;
            }

            if (_runPage == 3 && !_page3PreflightChecked)
            {
                _page3PreflightChecked = true;

                if (UsePageFlipReset &&
                    (!SlotsClear() ||
                     ReadUiAnimState(transmuteButton) == Page3TransmuteReadyAnimState))
                {
                    BeginPage3PageRepair(
                        CubeStage.AcquireTarget,
                        now,
                        "preflight-stale-cube-state");
                    return;
                }
            }

            if (MaxTransmutesPerRun > 0 && _doneThisRun >= MaxTransmutesPerRun)
            {
                EndRun("max transmutes reached");
                return;
            }

            if (!HasMaterials(_runPage))
            {
                EndRun("materials exhausted");
                return;
            }

            _target = null;

            if (_snapshotQueue != null)
            {
                while (_snapshotIndex < _snapshotLimit)
                {
                    Slot slot = _snapshotQueue[_snapshotIndex++];
                    string key = StableItemKey(slot.Uid);

                    if (_skippedThisRun != null &&
                        _skippedThisRun.Contains(key))
                    {
                        continue;
                    }

                    IItem liveItem =
                        FindInventoryItemByStableKey(key);

                    if (liveItem == null)
                        continue;

                    if (_runPage == 3 &&
                        !IsUpgradeRareEligible(liveItem))
                    {
                        // Static preflight rejection. Do not click and do not
                        // add this item to the runtime retry mechanism.
                        LogUpgradeEligibilityOnce(
                            liveItem,
                            key);

                        continue;
                    }

                    RectangleF liveRect =
                        Hud.Inventory.GetItemRect(liveItem);

                    if (liveRect.Width <= 0 ||
                        liveRect.Height <= 0)
                    {
                        continue;
                    }

                    _target = new CubeTarget
                    {
                        Rect = liveRect,
                        Uid = liveItem.ItemUniqueId,
                        Key = key,
                        Item = liveItem
                    };

                    break;
                }

                if (_target == null)
                {
                    var remaining = Candidates(_runPage);

                    if (_skippedThisRun != null)
                        remaining.RemoveAll(x => _skippedThisRun.Contains(StableItemKey(x.ItemUniqueId)));

                    if (remaining.Count <= 0)
                    {
                        EndRun("snapshot queue complete");
                        return;
                    }

                    _snapshotQueue = null;
                }
            }

            if (_target == null)
            {
                var items = Candidates(_runPage);

                if (_skippedThisRun != null)
                    items.RemoveAll(x => _skippedThisRun.Contains(StableItemKey(x.ItemUniqueId)));

                if (items.Count <= 0)
                {
                    EndRun("no candidates");
                    return;
                }

                SortItems(_runPage, items);

                var item = items[0];
                var rect = Hud.Inventory.GetItemRect(item);

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    Delay(now, 30);
                    return;
                }

                _target = new CubeTarget
                {
                    Item = item,
                    Rect = rect,
                    Uid = item.ItemUniqueId,
                    Key = StableItemKey(item.ItemUniqueId)
                };
            }

            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();
            ResetPostFailureConfirmation();
            _pageRepairResumeStage = CubeStage.PostCycleEvaluate;

            _stage = CubeStage.InsertTarget;
            Delay(now, 0);
        }

        private void AdvanceInsertTarget(int now)
        {
            if (_target == null || string.IsNullOrEmpty(_target.Key))
            {
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            if (_runPage == 3)
            {
                IItem liveItem =
                    FindInventoryItemByStableKey(
                        _target.Key);

                int detectedLevel;
                string levelSource;

                UpgradeRareEligibility eligibility =
                    GetUpgradeRareEligibility(
                        liveItem,
                        out detectedLevel,
                        out levelSource);

                if (eligibility !=
                    UpgradeRareEligibility.Eligible)
                {
                    LogUpgradeEligibilityOnce(
                        liveItem,
                        _target.Key);

                    Log(
                        "UPGRADE preflight skip key=" +
                        _target.Key +
                        " result=" +
                        eligibility +
                        " level=" +
                        detectedLevel +
                        " source=" +
                        levelSource);

                    // Static ineligibility receives zero insertion attempts.
                    // Unknown data also fails closed rather than being clicked.
                    _target = null;
                    _insertAttemptsForTarget = 0;
                    ResetPage3TransactionState();
                    ResetPostFailureConfirmation();

                    _stage = CubeStage.AcquireTarget;
                    Delay(now, 0);
                    return;
                }

                RectangleF liveRect =
                    Hud.Inventory.GetItemRect(liveItem);

                if (liveRect.Width <= 0 ||
                    liveRect.Height <= 0)
                {
                    Delay(now, 30);
                    return;
                }

                _target.Item = liveItem;
                _target.Rect = liveRect;
                _target.Uid = liveItem.ItemUniqueId;
            }

            if (!StableKeyExistsInInventory(_target.Key))
            {
                _target = null;
                ResetPage3TransactionState();
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            RectangleF rect = _target.Rect;

            if (_target.Item != null)
            {
                var freshRect = Hud.Inventory.GetItemRect(_target.Item);
                if (freshRect.Width > 0 && freshRect.Height > 0)
                    rect = freshRect;
            }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                Delay(now, 30);
                return;
            }

            if (_runPage == 3)
            {
                _page3AnimBeforeInsert = ReadUiAnimState(transmuteButton);
                _page3SawNotReadySinceInsert =
                    _page3AnimBeforeInsert != Page3TransmuteReadyAnimState;
                _page3ReadyEdgeSeen = false;
                _page3TransmuteAccepted = false;
            }

            _cycleId++;
            _cycleStartTick = now;

            if (!ClickRect(MouseButtons.Right, rect, "item"))
            {
                Delay(now, 30);
                return;
            }

            _lastProcessedRect = rect;

            if (_runPage != 3)
            {
                _stage = CubeStage.Fill1;
                Delay(now, _p.Sleep);
                return;
            }

            _insertAttemptsForTarget++;
            ClearDeadline();

            _stage = CubeStage.ConfirmInsert;
            Delay(now, _p.P3InsertPoll);
        }

        private void AdvanceConfirmInsert(int now)
        {
            if (_target == null ||
                string.IsNullOrEmpty(_target.Key))
            {
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            int anim = ReadUiAnimState(transmuteButton);

            if (anim != Page3TransmuteReadyAnimState)
                _page3SawNotReadySinceInsert = true;

            if (_page3SawNotReadySinceInsert &&
                anim == Page3TransmuteReadyAnimState)
            {
                _page3ReadyEdgeSeen = true;
            }

            if (!SlotsClear())
            {
                _page3SawOccupiedSlot = true;
                ClearDeadline();

                // Slot occupancy is the native insertion arbiter. Do not wait
                // for 51→54 before Fill: the supplied runtime log proved that
                // some valid insertions remain at 51 until Fill is clicked.
                _stage = CubeStage.Fill1;
                Delay(now, _p.P3InsertToFill);
                return;
            }

            if (_page3SawOccupiedSlot)
            {
                BeginPage3PageRepair(
                    CubeStage.PostCycleEvaluate,
                    now,
                    "cube-slots-cleared-before-ready");
                return;
            }

            EnsureDeadline(now, InsertConfirmTimeoutMs);

            if (!DeadlineExpired(now))
            {
                Delay(now, 30);
                return;
            }

            ClearDeadline();

            if (_insertAttemptsForTarget <
                    MaxInsertAttemptsPerTarget &&
                RefreshUpgradeTarget(_target.Key))
            {
                Log(
                    "UPGRADE insert not confirmed; retrying key=" +
                    _target.Key +
                    " attempt=" +
                    (_insertAttemptsForTarget + 1));

                _stage = CubeStage.InsertTarget;
                Delay(now, InsertRetryDelayMs);
                return;
            }

            string failedKey = _target.Key;

            if (_skippedThisRun != null)
                _skippedThisRun.Add(failedKey);

            Log(
                "UPGRADE insert failed; skipping for this run key=" +
                failedKey);

            _target = null;
            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();

            _stage = CubeStage.AcquireTarget;
            Delay(now, 0);
        }

        private void ResetPage3TransactionState()
        {
            _page3TransmuteClicks = 0;
            _page3TransmuteClickTick = NoTick;
            _page3SawOccupiedSlot = false;
            _page3SawNotReadySinceInsert = false;
            _page3ReadyEdgeSeen = false;
            _page3TransmuteAccepted = false;
            _page3AnimBeforeInsert = int.MinValue;
            _page3BeforeDeathsBreath = -1;
            _page3BeforeReusableParts = -1;
            _page3BeforeArcaneDust = -1;
            _page3BeforeVeiledCrystal = -1;
        }

        private int ReadUiAnimState(IUiElement element)
        {
            if (element == null)
                return int.MinValue;

            try
            {
                element.Refresh();
                return element.Visible
                    ? element.AnimState
                    : int.MinValue;
            }
            catch
            {
                return int.MinValue;
            }
        }

        private int Page3NativeGateTimeoutMs()
        {
            double latency = 0.0d;

            try
            {
                latency = Math.Max(
                    Hud.Game.CurrentLatency,
                    Hud.Game.AverageLatency);
            }
            catch
            {
            }

            if (double.IsNaN(latency) ||
                double.IsInfinity(latency) ||
                latency < 0.0d)
            {
                latency = 0.0d;
            }

            int timeout =
                (int)Math.Ceiling(latency * 2.0d + 120.0d);

            return Math.Max(
                Page3NativeGateMinTimeoutMs,
                Math.Min(Page3NativeGateMaxTimeoutMs, timeout));
        }

        private void BeginPage3PageRepair(
            CubeStage resumeStage,
            int now,
            string reason)
        {
            Log(
                "UPGRADE native arbiter reset key=" +
                (_target == null ? "?" : _target.Key) +
                " reason=" + reason +
                " animBeforeInsert=" + _page3AnimBeforeInsert +
                " animNow=" + ReadUiAnimState(transmuteButton));

            ClearDeadline();

            if (UsePageFlipReset)
            {
                _pageRepairResumeStage = resumeStage;
                _stage = CubeStage.FlipWaitReadyNext;
                Delay(now, 0);
                return;
            }

            string failedKey =
                _target == null ? string.Empty : _target.Key;

            if (!string.IsNullOrEmpty(failedKey) &&
                _skippedThisRun != null)
            {
                _skippedThisRun.Add(failedKey);
            }

            _target = null;
            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();
            ResetPostFailureConfirmation();
            _stage = CubeStage.AcquireTarget;
            Delay(now, 0);
        }

        private void ResumeAfterPageRepair(int now)
        {
            CubeStage resume = _pageRepairResumeStage;
            _pageRepairResumeStage = CubeStage.PostCycleEvaluate;

            if (resume == CubeStage.AcquireTarget)
            {
                _target = null;
                _insertAttemptsForTarget = 0;
                ResetPage3TransactionState();
                ResetPostFailureConfirmation();
            }

            _stage = resume;
            Delay(now, 0);
        }

        private void AdvanceWaitPage3TransmuteReady(int now)
        {
            if (_target == null ||
                string.IsNullOrEmpty(_target.Key))
            {
                ClearDeadline();
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            if (SlotsClear())
            {
                BeginPage3PageRepair(
                    CubeStage.PostCycleEvaluate,
                    now,
                    "cube-slots-cleared-before-transmute");
                return;
            }

            int anim = ReadUiAnimState(transmuteButton);

            if (anim != Page3TransmuteReadyAnimState)
                _page3SawNotReadySinceInsert = true;

            if (_page3SawNotReadySinceInsert &&
                anim == Page3TransmuteReadyAnimState)
            {
                _page3ReadyEdgeSeen = true;
            }

            if (anim == Page3TransmuteReadyAnimState)
            {
                ClearDeadline();
                _stage = CubeStage.Transmute;
                Delay(now, 0);
                return;
            }

            EnsureDeadline(now, Page3NativeGateTimeoutMs());

            if (!DeadlineExpired(now))
            {
                Delay(now, Page3NativeGatePollMs);
                return;
            }

            BeginPage3PageRepair(
                CubeStage.PostCycleEvaluate,
                now,
                "transmute-ready-lost-after-fill");
        }

        private void SnapshotPage3Materials()
        {
            if (Hud.Game.Me == null)
                return;

            _page3BeforeDeathsBreath =
                Hud.Game.Me.Materials.DeathsBreath;
            _page3BeforeReusableParts =
                Hud.Game.Me.Materials.ReusableParts;
            _page3BeforeArcaneDust =
                Hud.Game.Me.Materials.ArcaneDust;
            _page3BeforeVeiledCrystal =
                Hud.Game.Me.Materials.VeiledCrystal;
        }

        private bool Page3MaterialsWereSpent()
        {
            if (Hud.Game.Me == null ||
                _page3BeforeDeathsBreath < 0 ||
                _page3BeforeReusableParts < 0 ||
                _page3BeforeArcaneDust < 0 ||
                _page3BeforeVeiledCrystal < 0)
            {
                return false;
            }

            return
                Hud.Game.Me.Materials.DeathsBreath <=
                    _page3BeforeDeathsBreath - 25 &&
                Hud.Game.Me.Materials.ReusableParts <=
                    _page3BeforeReusableParts - 50 &&
                Hud.Game.Me.Materials.ArcaneDust <=
                    _page3BeforeArcaneDust - 50 &&
                Hud.Game.Me.Materials.VeiledCrystal <=
                    _page3BeforeVeiledCrystal - 50;
        }

        private bool Page3TransactionSucceeded(
            int now,
            out string evidence)
        {
            evidence = string.Empty;

            if (Page3MaterialsWereSpent())
            {
                evidence = "materials-spent";
                return true;
            }

            if (_page3SawOccupiedSlot &&
                SlotsClear() &&
                _target != null &&
                !CandidateKeyStillPresent(
                    3,
                    _target.Key) &&
                _page3TransmuteClickTick != NoTick &&
                unchecked(
                    now - _page3TransmuteClickTick) >= 90)
            {
                evidence = "slots-clear+rare-gone";
                return true;
            }

            return false;
        }

        private void AdvanceConfirmPage3Transmute(
            int now)
        {
            if (_target == null ||
                string.IsNullOrEmpty(_target.Key))
            {
                ClearDeadline();
                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            string evidence;

            if (Page3TransactionSucceeded(
                now,
                out evidence))
            {
                Log(
                    "UPGRADE transmute confirmed key=" +
                    _target.Key +
                    " evidence=" +
                    evidence +
                    " clicks=" +
                    _page3TransmuteClicks);

                ClearDeadline();

                if (UsePageFlipReset)
                {
                    _stage = CubeStage.FlipWaitReadyNext;
                    Delay(now, _p.P3SuccessSettle);
                }
                else
                {
                    _stage = CubeStage.OpenPageRecovery;
                    Delay(now, _p.P3SuccessSettle);
                }

                return;
            }

            if (!_page3TransmuteAccepted)
            {
                int anim = ReadUiAnimState(transmuteButton);

                if (anim == Page3TransmuteAcceptedAnimState)
                {
                    _page3TransmuteAccepted = true;
                    ClearDeadline();
                    LogV(
                        "UPGRADE transmute accepted key=" +
                        _target.Key +
                        " click=" +
                        _page3TransmuteClicks);
                }
                else
                {
                    EnsureDeadline(now, Page3NativeGateTimeoutMs());

                    if (!DeadlineExpired(now))
                    {
                        Delay(now, Page3NativeGatePollMs);
                        return;
                    }

                    ClearDeadline();

                    if (_page3TransmuteClicks <
                            MaxPage3TransmuteClicksPerCycle &&
                        anim == Page3TransmuteReadyAnimState &&
                        !SlotsClear())
                    {
                        Log(
                            "UPGRADE transmute click not accepted; retrying key=" +
                            _target.Key +
                            " click=" +
                            (_page3TransmuteClicks + 1));

                        _stage = CubeStage.Transmute;
                        Delay(now, Page3TransmuteRetryDelayMs);
                        return;
                    }

                    BeginPage3PageRepair(
                        CubeStage.PostCycleEvaluate,
                        now,
                        "transmute-accept-timeout");
                    return;
                }
            }

            // Fast path: only after native ready (54) changed to accepted (51),
            // overlap transaction settlement with page repair and verify later.
            if (UsePageFlipReset &&
                _page3TransmuteClickTick != NoTick &&
                unchecked(
                    now - _page3TransmuteClickTick) >=
                    _p.P3TransmuteToRepair)
            {
                ClearDeadline();
                _stage = CubeStage.FlipWaitReadyNext;
                Delay(now, 0);
                return;
            }

            EnsureDeadline(
                now,
                Page3TransmuteConfirmTimeoutMs);

            if (!DeadlineExpired(now))
            {
                Delay(now, Page3TransmutePollMs);
                return;
            }

            ClearDeadline();

            if (_page3TransmuteClicks <
                    MaxPage3TransmuteClicksPerCycle &&
                !SlotsClear())
            {
                Log(
                    "UPGRADE transmute not confirmed; retrying Fill/Transmute key=" +
                    _target.Key +
                    " click=" +
                    (_page3TransmuteClicks + 1));

                _stage = CubeStage.Fill1;
                Delay(now, Page3TransmuteRetryDelayMs);
                return;
            }

            Log(
                "UPGRADE transmute not confirmed; repairing page before final evaluation key=" +
                _target.Key +
                " clicks=" +
                _page3TransmuteClicks +
                " slotsClear=" +
                SlotsClear());

            if (UsePageFlipReset)
            {
                _stage = CubeStage.FlipWaitReadyNext;
                Delay(now, 0);
            }
            else
            {
                _stage = CubeStage.OpenPageRecovery;
                Delay(now, 0);
            }
        }

        private void ResetPostFailureConfirmation()
        {
            _postFailureKey = string.Empty;
            _postFailureSinceTick = NoTick;
            _postFailureSamples = 0;
        }

        private bool ConfirmUpgradeFailureSample(
            int now,
            string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                ResetPostFailureConfirmation();
                return false;
            }

            if (!string.Equals(
                _postFailureKey,
                key,
                StringComparison.Ordinal))
            {
                _postFailureKey = key;
                _postFailureSinceTick = now;
                _postFailureSamples = 1;
                return false;
            }

            _postFailureSamples++;

            return _postFailureSamples >=
                    UpgradeFailureConfirmSamples &&
                _postFailureSinceTick != NoTick &&
                unchecked(
                    now - _postFailureSinceTick) >=
                    _p.P3FailureConfirm;
        }

        private void AdvancePostCycleEvaluate(int now)
        {
            string page3Evidence = string.Empty;
            bool page3Succeeded =
                _target != null &&
                _runPage == 3 &&
                Page3TransactionSucceeded(
                    now,
                    out page3Evidence);

            bool page3TargetStillEligible =
                _target != null &&
                _runPage == 3 &&
                CandidateKeyStillPresent(
                    3,
                    _target.Key);

            if (page3TargetStillEligible &&
                !page3Succeeded)
            {
                if (!ConfirmUpgradeFailureSample(
                    now,
                    _target.Key))
                {
                    Delay(
                        now,
                        _p.P3FailureSample);

                    return;
                }

                string failedKey = _target.Key;

                int failures = 0;

                if (_upgradeFailuresThisRun != null)
                    _upgradeFailuresThisRun.TryGetValue(
                        failedKey,
                        out failures);

                failures++;
                if (_upgradeFailuresThisRun != null)
                    _upgradeFailuresThisRun[failedKey] =
                        failures;

                Log(
                    "CYCLE #" +
                    _cycleId +
                    " upgrade attempt failed; key=" +
                    failedKey +
                    " failures=" +
                    failures);

                ResetPostFailureConfirmation();

                if (failures <
                        MaxUpgradeAttemptsPerItemPerRun &&
                    RefreshUpgradeTarget(failedKey))
                {
                    // Retry the same statically eligible item once. This is a
                    // transaction retry, not an eligibility reclassification.
                    _insertAttemptsForTarget = 0;
                    ResetPage3TransactionState();

                    _stage = CubeStage.InsertTarget;
                    Delay(now, UpgradeRetryDelayMs);
                    return;
                }

                if (_skippedThisRun != null)
                    _skippedThisRun.Add(failedKey);

                Log(
                    "UPGRADE skipped for current run after " +
                    failures +
                    " failed attempts; key=" +
                    failedKey);

                // Never add an X here. The item remains recipe-eligible even if
                // the automation failed to process it.
                _target = null;
                _insertAttemptsForTarget = 0;
                ResetPage3TransactionState();

                _stage = CubeStage.AcquireTarget;
                Delay(now, 0);
                return;
            }

            ResetPostFailureConfirmation();

            _doneThisRun++;

            if (_runPage == 2 &&
                _target != null)
            {
                _startAncientIds.Remove(
                    _target.Uid);
            }

            if (_runPage == 3 &&
                _target != null &&
                _upgradeFailuresThisRun != null)
            {
                _upgradeFailuresThisRun.Remove(
                    _target.Key);
            }

            if (_cycleId == 1 ||
                _cycleId % 5 == 0)
            {
                Log(
                    "CYCLE #" +
                    _cycleId +
                    " ok");
            }

            if (_cycleStartTick != 0)
            {
                _lastCycleElapsedMs =
                    unchecked(
                        now - _cycleStartTick);

                if (DebugLogEnabled &&
                    (_cycleId == 1 ||
                     _cycleId % 10 == 0))
                {
                    Log(
                        "CYCLE #" +
                        _cycleId +
                        " elapsed=" +
                        _lastCycleElapsedMs +
                        "ms profile=" +
                        _p);
                }
            }

            _target = null;
            _insertAttemptsForTarget = 0;
            ResetPage3TransactionState();

            _stage = CubeStage.AcquireTarget;
            Delay(now, 0);
        }

        private IItem FindInventoryItemByStableKey(
            string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            foreach (var item in
                Hud.Inventory.ItemsInInventory)
            {
                if (item == null)
                    continue;

                if (StableItemKey(item.ItemUniqueId) == key)
                    return item;
            }

            return null;
        }

        private bool RefreshUpgradeTarget(
            string key)
        {
            IItem item =
                FindInventoryItemByStableKey(key);

            if (!IsUpgradeRareEligible(item))
                return false;

            RectangleF rect =
                Hud.Inventory.GetItemRect(item);

            if (rect.Width <= 0 ||
                rect.Height <= 0)
            {
                return false;
            }

            _target = new CubeTarget
            {
                Item = item,
                Rect = rect,
                Uid = item.ItemUniqueId,
                Key = key
            };

            return true;
        }

        private void LogUpgradeEligibilityOnce(
            IItem item,
            string key)
        {
            if (!DebugLogEnabled ||
                item == null ||
                item.SnoItem == null ||
                string.IsNullOrEmpty(key) ||
                _eligibilityLoggedThisRun == null ||
                !_eligibilityLoggedThisRun.Add(key))
            {
                return;
            }

            int level;
            string source;

            UpgradeRareEligibility result =
                GetUpgradeRareEligibility(
                    item,
                    out level,
                    out source);

            Log(
                "UPGRADE eligibility key=" +
                key +
                " name=" +
                (item.FullNameEnglish ?? "?") +
                " result=" +
                result +
                " detectedLevel=" +
                level +
                " source=" +
                source +
                " snoLevel=" +
                item.SnoItem.Level +
                " requiredLevel=" +
                item.SnoItem.RequiredLevel);
        }

        private bool StableKeyExistsInInventory(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return Hud.Inventory.ItemsInInventory.Any(x =>
                x != null && StableItemKey(x.ItemUniqueId) == key);
        }

        // ── Debounce ──────────────────────────────────────────────────────────
        private bool Debounce(ref int tick, int minMs)
        {
            int now = Environment.TickCount;

            if (tick != 0 && (uint)(now - tick) < (uint)Math.Max(0, minMs))
                return false;

            tick = now;
            return true;
        }

        // =========================================================
        // Click Handlers
        // =========================================================

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left)
                return false;

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                return false;

            if (!UpdateOverlayLayoutRects())
            {
                _overlayControlsVisible = false;
                return false;
            }

            _overlayControlsVisible = true;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;
            int now = Environment.TickCount;

            bool hitMinus = PointInRect(_speedMinusRect, x, y);
            bool hitPlus = PointInRect(_speedPlusRect, x, y);
            bool hitHotkey = PointInRect(_hotkeyButtonRect, x, y);

            if (!hitMinus && !hitPlus && !hitHotkey)
                return false;

            if (hitMinus)
            {
                if (Running)
                {
                    Log("SPEED minus ignored; running");
                    return true;
                }

                int old = Clamp(SpeedLevel);
                SpeedLevel = Clamp(old - 1);
                _minusFlashUntilTick = unchecked(now + Math.Max(30, ButtonFlashMs));

                if (SpeedLevel != old)
                    SaveSettings();

                Log("SPEED " + old + " -> " + SpeedLevel + " profile=" + CurrentProfile());
                return true;
            }

            if (hitPlus)
            {
                if (Running)
                {
                    Log("SPEED plus ignored; running");
                    return true;
                }

                int old = Clamp(SpeedLevel);
                SpeedLevel = Clamp(old + 1);
                _plusFlashUntilTick = unchecked(now + Math.Max(30, ButtonFlashMs));

                if (SpeedLevel != old)
                    SaveSettings();

                Log("SPEED " + old + " -> " + SpeedLevel + " profile=" + CurrentProfile());
                return true;
            }

            if (hitHotkey)
            {
                if (Running)
                {
                    Log("Hotkey capture ignored; running");
                    return true;
                }

                _capturingHotkey = true;
                Log("Hotkey capture started.");
                return true;
            }

            return false;
        }

        public bool MouseUp(MouseButtons button)
        {
            return false;
        }






        // =========================================================
        // UI Drawing — pill-style controls
        // =========================================================





        // =========================================================
        // UI Drawing — fixed ItemSalvage-style header controls
        // =========================================================

        private bool UpdateOverlayLayoutRects()
        {
            _hotkeyButtonRect = RectangleF.Empty;
            _speedMinusRect = RectangleF.Empty;
            _speedPlusRect = RectangleF.Empty;
            _speedValueRect = RectangleF.Empty;
            _speedControlRect = RectangleF.Empty;

            if (!ShowOverlay || vendorPage == null || transmuteDialog == null)
                return false;

            if (!IsVisible(transmuteDialog))
                return false;

            var pane = vendorPage.Rectangle;
            if (pane.Width <= 0 || pane.Height <= 0)
                return false;

            float topY = pane.Y + HeaderTopOffset;

            // Left group: KANAI CUBE over F-key.
            float hotkeyGroupX = pane.X + HeaderLeftOffset;
            float hotkeyButtonX = hotkeyGroupX + (HeaderHotkeyGroupWidth - HotkeyButtonWidth) * 0.5f;
            float hotkeyButtonY = topY + 18.0f;

            _hotkeyButtonRect = new RectangleF(
                hotkeyButtonX,
                hotkeyButtonY,
                HotkeyButtonWidth,
                HotkeyButtonHeight);

            // Right group: segmented speed control.
            float groupWidth = SpeedControlWidth;
            float rightX = pane.X + pane.Width - HeaderRightOffset - groupWidth;
            float minRightX = pane.X + 12.0f;
            float maxRightX = pane.X + pane.Width - groupWidth - 12.0f;
            rightX = Math.Max(minRightX, Math.Min(maxRightX, rightX));

            float speedX = rightX;
            float speedY = topY + 18.0f;

            float sideWidth = Math.Max(1.0f, SpeedSideButtonWidth);
            float centerWidth = Math.Max(1.0f, SpeedControlWidth - sideWidth * 2.0f);

            _speedMinusRect = new RectangleF(speedX, speedY, sideWidth, SpeedControlHeight);
            _speedValueRect = new RectangleF(speedX + sideWidth, speedY, centerWidth, SpeedControlHeight);
            _speedPlusRect = new RectangleF(speedX + sideWidth + centerWidth, speedY, sideWidth, SpeedControlHeight);
            _speedControlRect = new RectangleF(speedX, speedY, SpeedControlWidth, SpeedControlHeight);

            return true;
        }

        private void DrawHeaderHotkey()
        {
            if (_yellowFont == null || vendorPage == null)
                return;

            var pane = vendorPage.Rectangle;
            float groupX = pane.X + HeaderLeftOffset;
            float topY = pane.Y + HeaderTopOffset;

            string label = "KANAI CUBE";
            var layout = _yellowFont.GetTextLayout(label);
            float labelX = groupX + HeaderHotkeyGroupWidth * 0.5f - layout.Metrics.Width * 0.5f;

            _yellowFont.DrawText(layout, labelX, topY);
            DrawPillButton(_hotkeyButtonRect, _capturingHotkey ? "..." : KanaiCubeHotkey.ToString(), _capturingHotkey);
        }


        private void DrawHeaderSpeedControl()
        {
            if (!ShowSpeedControl)
                return;

            int now = Environment.TickCount;

            DrawSpeedLabel();

            DrawSegmentedPillBase(_speedControlRect);

            if (TickIsFuture(now, _minusFlashUntilTick))
                DrawPillSegment(_speedMinusRect, true, false, true);

            if (TickIsFuture(now, _plusFlashUntilTick))
                DrawPillSegment(_speedPlusRect, false, true, true);

            if (_pillOrangeSeparatorBrush != null)
            {
                float y1 = _speedControlRect.Y + 3.0f;
                float y2 = _speedControlRect.Y + _speedControlRect.Height - 3.0f;
                float div1 = _speedMinusRect.Right;
                float div2 = _speedValueRect.Right;

                _pillOrangeSeparatorBrush.DrawLine(div1, y1, div1, y2);
                _pillOrangeSeparatorBrush.DrawLine(div2, y1, div2, y2);
            }

            DrawCenteredText(_speedMinusRect, "-");
            DrawCenteredText(_speedValueRect, Clamp(SpeedLevel).ToString());
            DrawCenteredText(_speedPlusRect, "+");
        }

        private void DrawSpeedLabel()
        {
            if (_yellowFont == null || _speedControlRect.Width <= 0 || _speedControlRect.Height <= 0)
                return;

            var layout = _yellowFont.GetTextLayout("SPEED");
            float x = _speedControlRect.X + _speedControlRect.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float y = _speedControlRect.Y - SpeedControlHeight + 2.0f;
            _yellowFont.DrawText(layout, x, y);
        }

        private void DrawPillButton(RectangleF rect, string text, bool green)
        {
            float radius = rect.Height * 0.5f;

            DrawRoundedRect(rect, radius, _pillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? _pillGreenBrush : _pillDarkBrush);

            var highlight = new RectangleF(
                inner.X + 1.0f,
                inner.Y + 1.0f,
                Math.Max(0.0f, inner.Width - 2.0f),
                inner.Height * 0.42f);

            DrawRoundedRect(highlight, highlight.Height * 0.5f, green ? _pillGreenLightBrush : _pillLightBrush);

            DrawCenteredText(rect, text);
        }

        private void DrawSegmentedPillBase(RectangleF rect)
        {
            float radius = rect.Height * 0.5f;

            DrawRoundedRect(rect, radius, _pillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, _pillDarkBrush);

            var highlight = new RectangleF(
                inner.X + 1.0f,
                inner.Y + 1.0f,
                Math.Max(0.0f, inner.Width - 2.0f),
                inner.Height * 0.42f);

            DrawRoundedRect(highlight, highlight.Height * 0.5f, _pillLightBrush);
        }

        private void DrawPillSegment(RectangleF rect, bool leftRounded, bool rightRounded, bool green)
        {
            if (!green)
                return;

            var inner = InsetRect(rect, 1.0f);
            float radius = inner.Height * 0.5f;

            DrawRoundedSegment(inner, radius, leftRounded, rightRounded, _pillGreenBrush);

            var highlight = new RectangleF(
                inner.X + 1.0f,
                inner.Y + 1.0f,
                Math.Max(0.0f, inner.Width - 2.0f),
                inner.Height * 0.42f);

            DrawRoundedSegment(highlight, highlight.Height * 0.5f, leftRounded, rightRounded, _pillGreenLightBrush);
        }

        private static RectangleF InsetRect(RectangleF rect, float amount)
        {
            return new RectangleF(
                rect.X + amount,
                rect.Y + amount,
                Math.Max(0.0f, rect.Width - amount * 2.0f),
                Math.Max(0.0f, rect.Height - amount * 2.0f));
        }

        private void DrawRoundedRect(RectangleF rect, float radius, IBrush brush)
        {
            if (brush == null) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            if (!UseRoundedGeometryButtons || _geometryDrawFailed)
            {
                brush.DrawRectangle(rect);
                return;
            }

            try
            {
                radius = Math.Max(0.0f, Math.Min(radius, Math.Min(rect.Width, rect.Height) * 0.5f));

                using (var pg = Hud.Render.CreateGeometry())
                {
                    using (var gs = pg.Open())
                    {
                        BeginRoundedRectFigure(gs, rect, radius, true, true, true, true);
                        gs.Close();
                    }

                    brush.DrawGeometry(pg);
                }
            }
            catch (Exception ex)
            {
                _geometryDrawFailed = true;

                if (!_geometryDrawFailureLogged)
                {
                    _geometryDrawFailureLogged = true;
                    Log("Rounded geometry drawing failed. Falling back to rectangles. " + ex);
                }

                brush.DrawRectangle(rect);
            }
        }

        private void DrawRoundedSegment(RectangleF rect, float radius, bool roundLeft, bool roundRight, IBrush brush)
        {
            if (brush == null) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            if (!UseRoundedGeometryButtons || _geometryDrawFailed)
            {
                brush.DrawRectangle(rect);
                return;
            }

            try
            {
                radius = Math.Max(0.0f, Math.Min(radius, Math.Min(rect.Width, rect.Height) * 0.5f));

                using (var pg = Hud.Render.CreateGeometry())
                {
                    using (var gs = pg.Open())
                    {
                        BeginRoundedRectFigure(gs, rect, radius, roundLeft, roundRight, roundRight, roundLeft);
                        gs.Close();
                    }

                    brush.DrawGeometry(pg);
                }
            }
            catch (Exception ex)
            {
                _geometryDrawFailed = true;

                if (!_geometryDrawFailureLogged)
                {
                    _geometryDrawFailureLogged = true;
                    Log("Rounded segment drawing failed. Falling back to rectangles. " + ex);
                }

                brush.DrawRectangle(rect);
            }
        }

        private static void BeginRoundedRectFigure(
            GeometrySink gs,
            RectangleF rect,
            float radius,
            bool roundTopLeft,
            bool roundTopRight,
            bool roundBottomRight,
            bool roundBottomLeft)
        {
            const int steps = 5;

            float left = rect.Left;
            float top = rect.Top;
            float right = rect.Right;
            float bottom = rect.Bottom;

            gs.BeginFigure(new Vector2(left + (roundTopLeft ? radius : 0.0f), top), FigureBegin.Filled);
            gs.AddLine(new Vector2(right - (roundTopRight ? radius : 0.0f), top));

            if (roundTopRight) AddArcPoints(gs, right - radius, top + radius, radius, -90.0f, 0.0f, steps);
            else gs.AddLine(new Vector2(right, top));

            gs.AddLine(new Vector2(right, bottom - (roundBottomRight ? radius : 0.0f)));

            if (roundBottomRight) AddArcPoints(gs, right - radius, bottom - radius, radius, 0.0f, 90.0f, steps);
            else gs.AddLine(new Vector2(right, bottom));

            gs.AddLine(new Vector2(left + (roundBottomLeft ? radius : 0.0f), bottom));

            if (roundBottomLeft) AddArcPoints(gs, left + radius, bottom - radius, radius, 90.0f, 180.0f, steps);
            else gs.AddLine(new Vector2(left, bottom));

            gs.AddLine(new Vector2(left, top + (roundTopLeft ? radius : 0.0f)));

            if (roundTopLeft) AddArcPoints(gs, left + radius, top + radius, radius, 180.0f, 270.0f, steps);
            else gs.AddLine(new Vector2(left, top));

            gs.EndFigure(FigureEnd.Closed);
        }

        private static void AddArcPoints(GeometrySink gs, float cx, float cy, float radius, float startDeg, float endDeg, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float deg = startDeg + (endDeg - startDeg) * t;
                float rad = deg * (float)Math.PI / 180.0f;

                gs.AddLine(new Vector2(
                    cx + radius * (float)Math.Cos(rad),
                    cy + radius * (float)Math.Sin(rad)));
            }
        }

        private void DrawCenteredText(RectangleF rect, string text)
        {
            if (_buttonFont == null)
                return;

            var layout = _buttonFont.GetTextLayout(text ?? string.Empty);

            float tx = rect.X + rect.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float ty = rect.Y + rect.Height * 0.5f - layout.Metrics.Height * 0.5f;

            _buttonFont.DrawText(layout, tx, ty);
        }

        private static bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && rect.Contains(x, y);
        }

        // =========================================================
        // Timing Profile — lerp from LightningMod-style Speed 1 to fastest Speed 10
        //
        // Timings interpolate linearly from Speed 1 to Speed 10:
        //   timing       | spd1 | spd10
        //   Sleep        |  60  |  15
        //   PostTrans    | 125  |   8
        //   ArrowReady   | 220  | 105
        //   RightConfirm | 360  | 220
        //   RightToLeft  | 120  |   0
        //   ReturnConf   | 650  | 330
        //   RetryWait    |  90  |   5
        //   ArrowDown    |  35  |   0
        //   PostClick    |  20  |   0
        //   Page 3 action floors at Speed 10 are zero; the native slot-clear
        //   gate and 105 ms ArrowReady timeout remain the transaction barrier.
        //   Page 3 failure confirmation scales from 240 ms to 120 ms.
        // =========================================================

        private TimingProfile CurrentProfile() =>
            SpeedProfile(Clamp(SpeedLevel));

        private TimingProfile SpeedProfile(int level)
        {
            var t = (level - 1) / 9.0; // 0.0 at Speed 1, 1.0 at Speed 10
            return new TimingProfile
            {
                Label         = "SPEED " + level + "/10",
                Sleep         = Lerp(BaseGlobalSleepMs,           MinGlobalSleepMs,           t),
                PostTrans     = Lerp(BasePostTransmuteExtraMs,     MinPostTransmuteExtraMs,    t),
                ArrowReady    = Lerp(BasePageArrowReadyTimeoutMs,  MinPageArrowReadyTimeoutMs, t),
                RightConfirm  = Lerp(BasePageRightConfirmTimeoutMs, MinPageRightConfirmTimeoutMs, t),
                RightToLeft   = Lerp(BasePageRightToLeftMinWaitMs, MinPageRightToLeftMs,       t),
                ReturnConfirm = Lerp(BasePageReturnConfirmTimeoutMs, MinPageReturnConfirmTimeoutMs, t),
                RetryWait     = Lerp(BasePageReturnRetryWaitMs,    MinPageReturnRetryWaitMs,   t),
                MaxRetries    = BasePageReturnMaxRetries,
                ArrowDown     = Lerp(BasePageArrowMouseDownMs,     MinPageArrowMouseDownMs,    t),
                PostClick     = Lerp(BasePageArrowPostClickMs,     MinPageArrowPostClickMs,    t),
                OpenWait      = Lerp(BasePageOpenClickWaitMs,      MinPageOpenClickWaitMs,     t),
                P3InsertPoll = Lerp(BasePage3InsertConfirmPollMs, MinPage3InsertConfirmPollMs, t),
                P3InsertToFill = Lerp(BasePage3PostInsertSettleMs, MinPage3PostInsertSettleMs, t),
                P3FillToTransmute = Lerp(BasePage3PostFillSettleMs, MinPage3PostFillSettleMs, t),
                P3TransmuteToRepair = Lerp(BasePage3TransmuteToRepairMs, MinPage3TransmuteToRepairMs, t),
                P3FailureConfirm = Lerp(BasePage3FailureConfirmMs, MinPage3FailureConfirmMs, t),
                P3FailureSample = Lerp(BasePage3FailureSampleMs, MinPage3FailureSampleMs, t),
                P3SuccessSettle = Lerp(BasePage3SuccessSettleMs, MinPage3SuccessSettleMs, t),
            };
        }

        private static int Lerp(int from, int to, double t) =>
            Math.Max(0, (int)Math.Round(from + (to - from) * t));

        // =========================================================
        // Guards
        // =========================================================

        private bool ValidateTurnedOn(int page)
        {
            if (!SupportedPage(page)) { TurnedOn = false; return false; }
            return ValidateTransmuteTurnedOn(page == 2 && InventoryLockForUpgradeToAncient);
        }

        // =========================================================
        // Item Queries
        // =========================================================

        private List<Slot> BuildQueue(int page)
        {
            var items = Candidates(page); SortItems(page, items);
            var q = new List<Slot>();
            foreach (var item in items)
            {
                var r = Hud.Inventory.GetItemRect(item);
                if (r.Width > 0) q.Add(new Slot { Rect = r, X = item.InventoryX, Y = item.InventoryY, Quality = item.Quality, Uid = item.ItemUniqueId });
            }
            return q;
        }

        private List<IItem> Candidates(int page)
        {
            if (page == 2)
                return ReforgeItems();

            if (page == 3)
                return RareItems();

            if (page >= 7 && page <= 9)
                return ConvertItems(Qualities(page));

            return new List<IItem>();
        }

        private bool CandidateKeyStillPresent(int page, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var items = Candidates(page);
            return items.Any(x => StableItemKey(x.ItemUniqueId) == key);
        }

        private List<IItem> ReforgeItems()
        {
            var r = new List<IItem>();
            foreach (var item in Hud.Inventory.ItemsInInventory)
            {
                if (item.SnoItem.Kind != ItemKind.loot || !IsReforgable(item)) continue;
                var ok = Running
                    ? item.AncientRank >= 0 && item.AncientRank <= (Mode == 0 ? 0 : 1)
                    : item.AncientRank >= 0;
                if (ok || _startAncientIds.Contains(item.ItemUniqueId)) r.Add(item);
            }
            return r;
        }

        private bool IsReforgable(IItem item) =>
            item.IsLegendary && !item.Unidentified && item.Quantity <= 1
            && (!InventoryLockForUpgradeToAncient || !item.IsInventoryLocked)
            && item.SnoItem.MainGroupCode != "gems_unique"
            && item.SnoItem.MainGroupCode != "riftkeystone"
            && item.SnoItem.MainGroupCode != "horadriccache"
            && item.SnoItem.MainGroupCode != "-"
            && item.SnoItem.MainGroupCode != "pony"
            && !item.SnoItem.Code.StartsWith("P71_Ethereal")
            && !item.SnoItem.Code.StartsWith("P72_Soulshard")
            && item.SnoItem.NameEnglish != "Hellforge Ember";

        private const uint UpgradeRareRequirementModifier =
            57u;

        private bool TryReadUpgradeRareRequirementStat(
            IItem item,
            out int level)
        {
            level = -1;

            if (item == null ||
                item.StatList == null)
            {
                return false;
            }

            try
            {
                foreach (IItemStat stat in
                    item.StatList)
                {
                    if (stat == null ||
                        stat.Attribute == null ||
                        stat.Modifier !=
                            UpgradeRareRequirementModifier ||
                        !string.Equals(
                            stat.Attribute.Code,
                            "Requirement",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    double raw =
                        stat.DoubleValue;

                    if (double.IsNaN(raw) ||
                        double.IsInfinity(raw))
                    {
                        continue;
                    }

                    int candidate =
                        (int)Math.Round(
                            raw,
                            MidpointRounding
                                .AwayFromZero);

                    if (candidate >= 1 &&
                        candidate <= 100 &&
                        Math.Abs(
                            raw -
                            candidate) <= 0.001d)
                    {
                        level = candidate;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private int ReadUpgradeRareRequirementAttribute(
            IItem item)
        {
            if (item == null)
                return -1;

            try
            {
                int value =
                    item.GetAttributeValueAsInt(
                        Hud.Sno.Attributes
                            .Requirement,
                        UpgradeRareRequirementModifier,
                        -1);

                if (value >= 1 &&
                    value <= 100)
                {
                    return value;
                }
            }
            catch
            {
            }

            return -1;
        }

        private int GetUpgradeRareItemLevel(
            IItem item,
            out string source)
        {
            source = "none";

            if (item == null ||
                item.SnoItem == null)
            {
                return -1;
            }

            int instanceLevel;

            if (TryReadUpgradeRareRequirementStat(
                    item,
                    out instanceLevel))
            {
                source =
                    "StatList.Requirement#57";

                return instanceLevel;
            }

            instanceLevel =
                ReadUpgradeRareRequirementAttribute(
                    item);

            if (instanceLevel > 0)
            {
                source =
                    "Attribute.Requirement#57";

                return instanceLevel;
            }

            // LightningMOD's template test is retained only as a definite
            // lower-tier rejection. A template level of 65 can represent
            // either a generated level-70 item or a genuinely lower-level
            // item, so it cannot positively classify the live instance.
            if (item.SnoItem.Level > 0 &&
                item.SnoItem.Level < 65)
            {
                source =
                    "SnoItem.LevelBelow65";

                return item.SnoItem.Level;
            }

            // Do not substitute Item_Quality_Level, RequiredLevel, or the
            // 65+ template convention. None of them distinguishes the two
            // same-SNO items proven by the diagnostic log.
            source =
                "InstanceRequirementUnavailable";

            return -1;
        }

        private UpgradeRareEligibility GetUpgradeRareEligibility(
            IItem item,
            out int detectedLevel,
            out string levelSource)
        {
            detectedLevel = -1;
            levelSource = "none";

            if (item == null ||
                item.SnoItem == null ||
                item.SnoItem.Kind !=
                    ItemKind.loot ||
                !item.IsRare ||
                item.Quantity > 1)
            {
                return UpgradeRareEligibility
                    .IneligibleType;
            }

            string group =
                item.SnoItem.MainGroupCode ??
                string.Empty;

            if (group == "gems_unique" ||
                group == "riftkeystone" ||
                group == "horadriccache" ||
                group == "-" ||
                group == "pony")
            {
                return UpgradeRareEligibility
                    .IneligibleType;
            }

            detectedLevel =
                GetUpgradeRareItemLevel(
                    item,
                    out levelSource);

            if (detectedLevel < 0)
            {
                // Unknown instance metadata is neither clicked nor painted as
                // definitively ineligible. The next collection can classify it
                // once the live Requirement stat becomes available.
                return UpgradeRareEligibility
                    .UnknownLevel;
            }

            if (detectedLevel < 70)
            {
                return UpgradeRareEligibility
                    .IneligibleLevel;
            }

            return UpgradeRareEligibility
                .Eligible;
        }

        private bool IsUpgradeRareEligible(
            IItem item)
        {
            int level;
            string source;

            return GetUpgradeRareEligibility(
                item,
                out level,
                out source) ==
                UpgradeRareEligibility.Eligible;
        }

        private bool ShouldDrawUpgradeIneligibleX(
            IItem item)
        {
            if (item == null ||
                item.SnoItem == null ||
                item.SnoItem.Kind !=
                    ItemKind.loot ||
                !item.IsRare ||
                item.Quantity > 1)
            {
                return false;
            }

            int level;
            string source;

            UpgradeRareEligibility result =
                GetUpgradeRareEligibility(
                    item,
                    out level,
                    out source);

            return result ==
                    UpgradeRareEligibility
                        .IneligibleLevel ||
                result ==
                    UpgradeRareEligibility
                        .IneligibleType;
        }

        private List<IItem> RareItems()
        {
            var result = new List<IItem>();

            foreach (var item in
                Hud.Inventory.ItemsInInventory)
            {
                if (IsUpgradeRareEligible(item))
                    result.Add(item);
            }

            return result;
        }

        private List<IItem> ConvertItems(List<ItemQuality> qualities)
        {
            var r = new List<IItem>();
            foreach (var item in Hud.Inventory.ItemsInInventory)
                if (item.SnoItem.Kind == ItemKind.loot && item.Quantity <= 1
                    && item.SnoItem.MainGroupCode != "gems_unique"
                    && item.SnoItem.MainGroupCode != "riftkeystone"
                    && item.SnoItem.MainGroupCode != "horadriccache"
                    && qualities.Contains(item.Quality))
                    r.Add(item);
            return r;
        }

        private static List<ItemQuality> Qualities(int page)
        {
            var q = new List<ItemQuality>();
            if (page == 7) { q.Add(ItemQuality.Magic4); q.Add(ItemQuality.Magic5); q.Add(ItemQuality.Magic6); q.Add(ItemQuality.Rare4); q.Add(ItemQuality.Rare5); q.Add(ItemQuality.Rare6); }
            if (page == 8) { q.Add(ItemQuality.Inferior); q.Add(ItemQuality.Normal); q.Add(ItemQuality.Superior); q.Add(ItemQuality.Rare4); q.Add(ItemQuality.Rare5); q.Add(ItemQuality.Rare6); }
            if (page == 9) { q.Add(ItemQuality.Inferior); q.Add(ItemQuality.Normal); q.Add(ItemQuality.Superior); q.Add(ItemQuality.Magic4); q.Add(ItemQuality.Magic5); q.Add(ItemQuality.Magic6); }
            return q;
        }

        private bool HasMaterials(int page)
        {
            if (page == 2) return Hud.Game.Me.Materials.ForgottenSoul >= 50
                && Hud.Game.Me.Materials.KhanduranRune >= 5 && Hud.Game.Me.Materials.CaldeumNightShade >= 5
                && Hud.Game.Me.Materials.ArreatWarTapestry >= 5 && Hud.Game.Me.Materials.CorruptedAngelFlesh >= 5
                && Hud.Game.Me.Materials.WestmarchHolyWater >= 5;
            if (page == 3) return Hud.Game.Me.Materials.DeathsBreath >= 25
                && Hud.Game.Me.Materials.ReusableParts >= 50 && Hud.Game.Me.Materials.ArcaneDust >= 50
                && Hud.Game.Me.Materials.VeiledCrystal >= 50;
            if (page >= 7 && page <= 9) return Hud.Game.Me.Materials.DeathsBreath >= 1 && MatAmt(page) >= 100;
            return false;
        }

        private int MaxByMaterials(int page)
        {
            if (page == 3) return Min4(Div(Hud.Game.Me.Materials.DeathsBreath, 25), Div(Hud.Game.Me.Materials.ReusableParts, 50), Div(Hud.Game.Me.Materials.ArcaneDust, 50), Div(Hud.Game.Me.Materials.VeiledCrystal, 50));
            if (page >= 7 && page <= 9) { var db = Div(Hud.Game.Me.Materials.DeathsBreath, 1); var m = Div(MatAmt(page), 100); return db < m ? db : m; }
            return -1;
        }

        private long MatAmt(int p) { if (p == 7) return Hud.Game.Me.Materials.ReusableParts; if (p == 8) return Hud.Game.Me.Materials.ArcaneDust; if (p == 9) return Hud.Game.Me.Materials.VeiledCrystal; return 0; }

        private void SortItems(int page, List<IItem> items)
        {
            // Material conversion is order-insensitive, so use the normal visual
            // inventory sweep: left-to-right across each row, then top-to-bottom.
            if (page >= 7 && page <= 9)
                items.Sort(InvRowMajor);
            else
                items.Sort(InvOrd);
        }

        private static int InvOrd(IItem a, IItem b)
        {
            int x = a.InventoryX.CompareTo(b.InventoryX);
            return x != 0 ? x : a.InventoryY.CompareTo(b.InventoryY);
        }

        private static int InvRowMajor(IItem a, IItem b)
        {
            int y = a.InventoryY.CompareTo(b.InventoryY);
            return y != 0 ? y : a.InventoryX.CompareTo(b.InventoryX);
        }

        private void SnapshotAncients()
        {
            _startAncientIds.Clear();
            foreach (var item in Hud.Inventory.ItemsInInventory)
                if (item.SnoItem.Kind == ItemKind.loot && IsReforgable(item)
                    && item.AncientRank > 0 && !_startAncientIds.Contains(item.ItemUniqueId))
                    _startAncientIds.Add(item.ItemUniqueId);
        }

        // =========================================================
        // Click Helpers
        // =========================================================
        private void ReleaseMouseButtons() { try { s7o_KanaiCubeInput.MouseUp(MouseButtons.Left); } catch { } try { s7o_KanaiCubeInput.MouseUp(MouseButtons.Right); } catch { } }
        private bool ClickUi(IUiElement e, string lbl)
        {
            if (e == null)
            {
                LogV("CLICKUI " + lbl + " null");
                return false;
            }

            if (!IsVisible(e))
            {
                LogV("CLICKUI " + lbl + " hidden");
                return false;
            }

            var r = e.Rectangle;
            if (r.Width <= 0 || r.Height <= 0)
            {
                LogV("CLICKUI " + lbl + " invalid " + Rf(r));
                return false;
            }

            LogV("CLICKUI " + lbl + " rect=" + Rf(r));

            ReleaseMouseButtons();
            return s7o_KanaiCubeInput.ClickRect(r, MouseButtons.Left, Hud.Window.Offset.X, Hud.Window.Offset.Y);
        }
        private bool ClickRect(MouseButtons btn, RectangleF rect, string lbl) { if (rect.Width <= 0 || rect.Height <= 0) { LogV("CLICKRECT " + lbl + " invalid " + Rf(rect)); return false; } LogV("CLICKRECT " + lbl + " " + Rf(rect)); ReleaseMouseButtons(); return s7o_KanaiCubeInput.ClickRect(rect, btn, Hud.Window.Offset.X, Hud.Window.Offset.Y); }
        private void InsertByDrag(IItem item) { if (item == null) return; var r = Hud.Inventory.GetItemRect(item); ClickRect(MouseButtons.Right, r, "item-rightclick-fallback"); }

        // =========================================================
        // Settings Persistence — SpeedLevel and KanaiCubeHotkey survive HUD restarts.
        // New FREEHUD path: plugins\s7o\settings\s7o_KanaiCube.ini.
        // Old s7o_TurboCube paths are migrated once when found.
        // This is intentionally independent from ItemSalvage settings. File errors silently fall back to defaults.
        // =========================================================

        private void LoadSettings()
        {
            if (!PersistUserSettings)
                return;

            try
            {
                string path = null;
                bool migratedFromOld = false;

                if (!string.IsNullOrEmpty(_settingsPath) && System.IO.File.Exists(_settingsPath))
                {
                    path = _settingsPath;
                }
                else if (!string.IsNullOrEmpty(_legacySettingsPath) && System.IO.File.Exists(_legacySettingsPath))
                {
                    path = _legacySettingsPath;
                    migratedFromOld = true;
                }
                else if (!string.IsNullOrEmpty(_oldSettingsPath) && System.IO.File.Exists(_oldSettingsPath))
                {
                    path = _oldSettingsPath;
                    migratedFromOld = true;
                }
                else if (!string.IsNullOrEmpty(_oldTurboSettingsPath) && System.IO.File.Exists(_oldTurboSettingsPath))
                {
                    path = _oldTurboSettingsPath;
                    migratedFromOld = true;
                }
                else if (!string.IsNullOrEmpty(_oldTurboPluginSettingsPath) && System.IO.File.Exists(_oldTurboPluginSettingsPath))
                {
                    path = _oldTurboPluginSettingsPath;
                    migratedFromOld = true;
                }
                else if (!string.IsNullOrEmpty(_oldTurboRootSettingsPath) && System.IO.File.Exists(_oldTurboRootSettingsPath))
                {
                    path = _oldTurboRootSettingsPath;
                    migratedFromOld = true;
                }

                if (string.IsNullOrEmpty(path))
                    return;

                foreach (var rawLine in System.IO.File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    var line = rawLine.Trim();
                    if (line.StartsWith("#"))
                        continue;

                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    if (string.Equals(key, "SpeedLevel", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "TurboSpeedLevel", StringComparison.OrdinalIgnoreCase))
                    {
                        int n;
                        if (int.TryParse(value, out n))
                            SpeedLevel = Clamp(n);
                    }
                    else if (string.Equals(key, "KanaiCubeHotkey", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "TurboCubeHotkey", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "Hotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            KanaiCubeHotkey = (Key)Enum.Parse(typeof(Key), value, true);
                        }
                        catch
                        {
                            // Ignore invalid saved hotkey.
                        }
                    }
                }

                if (migratedFromOld)
                    SaveSettings();
            }
            catch
            {
                // Settings persistence must never break the plugin.
            }
        }
        private void SaveSettings()
        {
            if (!PersistUserSettings)
                return;

            try
            {
                if (string.IsNullOrEmpty(_settingsPath))
                    return;

                var dir = System.IO.Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);

                string content =
                    "# s7o_KanaiCube user settings" + Environment.NewLine
                    + "# This file is intentionally separate from s7o_ItemSalvage settings." + Environment.NewLine
                    + Environment.NewLine
                    + "SpeedLevel=" + Clamp(SpeedLevel) + Environment.NewLine
                    + "KanaiCubeHotkey=" + KanaiCubeHotkey + Environment.NewLine;

                System.IO.File.WriteAllText(_settingsPath, content);
            }
            catch
            {
                // Settings persistence must never break the plugin.
            }
        }

        // =========================================================
        // Cursor Restore — hover top-right inside last item icon so the tooltip
        // appears for the just-processed item, but the cursor stays clear of the
        // bottom-right A/P (Ancient/Primal) corner marker.
        // Falls back to the user's original cursor if nothing was processed.
        // =========================================================

        private void RestoreCursor(int origX, int origY)
        {
            if (_lastProcessedRect.Width > 0 && _lastProcessedRect.Height > 0) { var x = (int)(_lastProcessedRect.X + _lastProcessedRect.Width * 0.78f); var y = (int)(_lastProcessedRect.Y + _lastProcessedRect.Height * 0.22f); s7o_KanaiCubeInput.MouseMoveClient(x, y, Hud.Window.Offset.X, Hud.Window.Offset.Y); }
            else if (RestoreCursorAfterRun) s7o_KanaiCubeInput.MouseMoveScreen(origX, origY);
        }
        private void RefreshOverlayCache(int page, int now)
        {
            if (!Hud.Game.IsInGame || vendorPage == null || transmuteDialog == null || !IsVisible(transmuteDialog))
                return;

            if (!SupportedPage(page))
                return;

            BuildOverlayText(page);

            // Running needs responsive candidate/status updates; idle overlay does not.
            var refreshMs = Running ? 200 : 350;

            if (_cachedOverlayPage == page && TickIsFuture(now, _nextOverlayCacheRefreshTick))
                return;

            _nextOverlayCacheRefreshTick = unchecked(now + refreshMs);
            _cachedOverlayPage = page;

            var items = Candidates(page);
            _cachedCandidateCount = items.Count;

            var rects = new List<RectangleF>();

            foreach (var item in items)
            {
                var r = Hud.Inventory.GetItemRect(item);
                if (r.Width > 0 && r.Height > 0)
                    rects.Add(r);
            }

            if (rects.Count > 0 || !Running)
                _lastHighlightRects = rects;

            _cachedStatusText = Running
                ? _running + (ShowCycleTimingOverlay && _lastCycleElapsedMs > 0 ? "\r\nlast cycle: " + _lastCycleElapsedMs + "ms" : "")
                : items.Count > 0
                    ? _info
                    : _noItem;
        }

        // =========================================================
        // Logging
        // =========================================================

        private void Log(string msg)
        {
            if (!DebugLogEnabled) return;
            try
            {
                if (!_sessionLogged)
                {
                    _sessionLogged = true;
                    Hud.TextLog.Log(DebugLogFileName, "=== Session " + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") + " ===");
                }
                Hud.TextLog.Log(DebugLogFileName, msg);
            }
            catch { } // logging must never break automation
        }

        private void LogV(string msg) { if (VerboseDebugLogging) Log(msg); }

        // =========================================================
        // Helpers
        // =========================================================


        private static bool IsReservedFallbackHotkey(IKeyEvent key)
        {
            if (key == null) return false;
            try
            {
                var s = key.ToString();
                if (string.IsNullOrEmpty(s)) return false;
                var u = s.ToUpperInvariant();
                return u.Contains("F12") || u.Contains("F8");
            }
            catch { return false; }
        }

        private void BuildOverlayText(int page)
        {
            var k = KanaiCubeHotkey.ToString();

            _lockMissing = "inventory lock missing — set it in Macros";

            if (page == 2)
            {
                _header = Mode == 0 ? "【Reforge to Ancient/Primal】" : "【Reforge to Primal】";
                _info = "press " + k + " to start\r\nclick hotkey button to change key; +/- adjusts speed";
                _noItem = "no legendary items";
                _running = "reforging...\r\npress " + k + " to stop";
            }
            else if (page == 3)
            {
                _header = "【Upgrade Rare】";
                _info = "press " + k + " to start\r\nclick hotkey button to change key; +/- adjusts speed";
                _noItem = "no rare items";
                _running = "upgrading...\r\npress " + k + " to stop";
            }
            else
            {
                _header = "【Convert Materials】";
                _info = "press " + k + " to start\r\nclick hotkey button to change key; +/- adjusts speed";
                _noItem = "no items to convert";
                _running = "converting...\r\npress " + k + " to stop";
            }
        }

        private bool SupportedPage(int p) => PageIndexes.Contains(p) && (p != 2 || EnableReforgePage2);
        private int  Clamp(int v)          => v < 1 ? 1 : v > 10 ? 10 : v;

        // TurboHUD's ItemUniqueId for inventory items can encode slot position in the prefix:
        //   "Inventory{position_prefix}-{stable_game_actor_id}"
        // The suffix after the last '-' is the stable item/actor key that survives inventory
        // moves and supports run-local retry tracking without storing inventory coordinates.
        private static string StableItemKey(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return uid ?? "";
            var dash = uid.LastIndexOf('-');
            return dash >= 0 ? uid.Substring(dash + 1) : uid;
        }
        private bool Hit(RectangleF r, int x, int y) => r.Width > 0 && x >= r.X && x <= r.X + r.Width && y >= r.Y && y <= r.Y + r.Height;
        private bool IsVisible(IUiElement e) { if (e == null) return false; try { e.Refresh(); return e.Visible; } catch { return false; } }
        private bool Vis(IUiElement e) { return IsVisible(e); }
        private int ReadPage() { return pageNumber == null || !IsVisible(pageNumber) ? -1 : PageNumDirect(); }
        private int PageNumDirect() { try { return GetPageNum(); } catch { return -1; } }
        private int PageNum() { try { return pageNumber == null || !IsVisible(pageNumber) ? -1 : GetPageNum(); } catch { return -1; } }
        private string Cursor()            => "(" + Hud.Window.CursorX + "," + Hud.Window.CursorY + ")";
        private string Rf(RectangleF r)    => "[" + (int)r.X + "," + (int)r.Y + " " + (int)r.Width + "x" + (int)r.Height + "]";
        private static int Div(long v, int d) { if (d <= 0 || v <= 0) return 0; var r = v / d; return r > int.MaxValue ? int.MaxValue : (int)r; }
        private static int Min4(int a, int b, int c, int dd) { var m = a < b ? a : b; if (c < m) m = c; if (dd < m) m = dd; return m; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Customizer — configures the plugin before Load(). Edit values here.
    // ═══════════════════════════════════════════════════════════════════════
    public class s7o_KanaiCubeCustomizer : BasePlugin, ICustomizer
    {
        public override void Load(IController hud) { base.Load(hud); Enabled = true; }
        public void Customize()
        {
            var p = Hud.GetPlugin<s7o_KanaiCube>(); if (p == null) return;
            // Do not assign ToggleKeyEvent here.
            // KanaiCubeHotkey is loaded from plugins\s7o\settings\s7o_KanaiCube.ini in Load(),
            // then ToggleKeyEvent is created from that saved/default key.
            // This keeps Kanai Cube independent from ItemSalvage even if both default to F3.
            p.MaxPageNavigationClicks = 12; p.ToggleDebounceMs = 750;
            p.BaseGlobalSleepMs = 60; p.BasePostTransmuteExtraMs = 125; p.BasePageArrowReadyTimeoutMs = 220; p.BasePageRightConfirmTimeoutMs = 360; p.BasePageRightToLeftMinWaitMs = 120; p.BasePageReturnConfirmTimeoutMs = 650; p.BasePageReturnRetryWaitMs = 90; p.BasePageReturnMaxRetries = 4; p.BasePageArrowMouseDownMs = 35; p.BasePageArrowPostClickMs = 20; p.BasePageOpenClickWaitMs = 100;
            p.MinGlobalSleepMs = 15; p.MinPostTransmuteExtraMs = 8; p.MinPageArrowReadyTimeoutMs = 105; p.MinPageRightConfirmTimeoutMs = 220; p.MinPageRightToLeftMs = 0; p.MinPageReturnConfirmTimeoutMs = 330; p.MinPageReturnRetryWaitMs = 5; p.MinPageArrowMouseDownMs = 0; p.MinPageArrowPostClickMs = 0; p.MinPageOpenClickWaitMs = 5;
            p.ShowSpeedControl = true;
            p.MaxTransmutesPerRun = 250; p.UseRightClickInsert = true; p.UsePageFlipReset = true; p.DoubleClickFillButton = false; p.UseSnapshotQueueForFastPages = true; p.EnableReforgePage2 = true; p.Mode = 0; p.RestoreCursorAfterRun = true;
            p.DebugLogEnabled = false; p.VerboseDebugLogging = false; p.DebugLogManualClicks = false; p.DebugLogUiRectsOnPageClicks = false; p.ShowCycleTimingOverlay = false; p.DebugLogFileName = "KanaiCubeDebug.log";
        }
        private static bool IsReservedFallbackHotkey(IKeyEvent key) { if (key == null) return false; try { var s = key.ToString(); if (string.IsNullOrEmpty(s)) return false; var u = s.ToUpperInvariant(); return u.Contains("F12") || u.Contains("F8"); } catch { return false; } }
        private static IKeyEvent NonReservedFallbackHotkey(IKeyEvent key) { return IsReservedFallbackHotkey(key) ? null : key; }
    }
}
