using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
public static class s7o_AutoGemUpgradeState
    {
        // Town portal timing is anchored to a specific upgrade phase and then
        // delayed by a literal number of milliseconds. Users choose whether the
        // timer starts after the 3-remaining or 4-remaining upgrade begins, and
        // then tune a real millisecond delay from that anchor. This keeps the
        // control linear and avoids hidden profile switches.
        public const int AutoGemTPDelayMin = 0;
        public const int AutoGemTPDelayMax = 1500;
        public const int AutoGemTPDelayStep = 100;
        public const int AutoGemTPLagBoostMs = 400;
        public const int AutoGemTPAnchorRemainingMin = 3;
        public const int AutoGemTPAnchorRemainingMax = 4;

        public static bool AutoGemUpgradeEnabled = true;
        public static int AutoGemMode = 0;
        public static string AutoGemSpecificName = "Bane of the Trapped";
        // SPECIFIC sub-mode: 0=AUTO (chance-tier logic scoped to gem name), 1=HIGHEST (highest eligible rank first)
        public static int AutoGemSpecificSubMode = 0;
        // Literal delay after the configured anchor upgrade begins.
        public static int AutoGemTPDelayMs = 1000;
        // Which upgrade-remaining phase starts the TP timer: 3 or 4.
        public static int AutoGemTPAnchorRemaining = 3;
        public static bool AutoGemTPLagBoost = false;

        // HUD Menu ownership bridge.
        // When true, the standalone Auto Gem overlay/menu button/hotkey suppresses itself,
        // but the navigator/automation continues to use this same shared state.
        public static bool HudMenuOwnsUi = false;
        public static string HudMenuUiOwner = string.Empty;

        // Optional persistence bridge. The standalone menu owns the Auto Gem settings file,
        // so HUD Menu can request a save after changing shared Auto Gem state.
        public static Action SaveSettingsRequested;

        public static bool IsUiOwnedByHudMenu()
        {
            return HudMenuOwnsUi &&
                string.Equals(HudMenuUiOwner, "s7o_HUD_MENU", StringComparison.OrdinalIgnoreCase);
        }

        public static void ClaimUiOwnership(string owner)
        {
            HudMenuOwnsUi = true;
            HudMenuUiOwner = owner ?? string.Empty;
        }

        public static void ReleaseUiOwnership(string owner)
        {
            if (!string.Equals(HudMenuUiOwner, owner ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return;

            HudMenuOwnsUi = false;
            HudMenuUiOwner = string.Empty;
        }

        public static void RequestSettingsSave()
        {
            try
            {
                Action cb = SaveSettingsRequested;
                if (cb != null) cb();
            }
            catch { }
        }

        public static string AutoGemDebugAction = string.Empty;
        public static string AutoGemDebugDetail = string.Empty;
        public static int AutoGemDebugTargetAbs = -1;
        public static int AutoGemDebugTargetRow = -1;

        // === Debug Logging Configuration ===
        // Debugging is off by default. When enabled via the overlay menu, logs are written
        // to the TurboHUD logs folder. The debug level controls which messages are emitted:
        // 0 = off, 1 = state changes, 2 = verbose.
        // Do not change these defaults unless adding new debug controls.
        public const int DebugLevelOff = 0;
        public const int DebugLevelState = 1;
        public const int DebugLevelVerbose = 2;
        public const string DebugPluginName = "s7o_AutoGem_FREEHUD";
        public const string DebugLogFileName = "s7o_AutoGem_FREEHUD_debug.log";

        // Whether debugging is enabled at all. Controlled by the menu toggle.
        public static bool DebugEnabled = false;
        // Whether file logging is active. This is set internally when debug is enabled.
        public static bool DebugFileEnabled = false;
        // Current debug level. When DebugEnabled is true this is set to DebugLevelState.
        public static int DebugLevel = DebugLevelOff;
        // Resolved log file path. If empty it will be resolved lazily on the first log flush.
        public static string DebugLogPath = string.Empty;
        // Maximum number of log lines to buffer before flushing to disk.
        public static int MaxBufferedLines = 64;
        // Maximum log file size in megabytes. If exceeded the log will be truncated.
        public static int MaxFileSizeMb = 4;

        public static int ClampTPDelayMs(int ms)
        {
            if (ms < AutoGemTPDelayMin) return AutoGemTPDelayMin;
            if (ms > AutoGemTPDelayMax) return AutoGemTPDelayMax;
            return ms;
        }

        public static int ClampTPAnchorRemaining(int remaining)
        {
            if (remaining <= AutoGemTPAnchorRemainingMin) return AutoGemTPAnchorRemainingMin;
            if (remaining >= AutoGemTPAnchorRemainingMax) return AutoGemTPAnchorRemainingMax;
            return remaining;
        }

        public static int GetConfiguredPortalAnchorRemaining()
        {
            return ClampTPAnchorRemaining(AutoGemTPAnchorRemaining);
        }

        public static int GetEffectivePortalAnchorRemaining(int initialAttempts)
        {
            if (initialAttempts == int.MinValue) return GetConfiguredPortalAnchorRemaining();
            return Math.Max(1, Math.Min(GetConfiguredPortalAnchorRemaining(), initialAttempts));
        }

        public static bool IsBelowConfiguredPortalAnchorAtRunStart(int initialAttempts)
        {
            if (initialAttempts == int.MinValue) return false;
            return initialAttempts < GetConfiguredPortalAnchorRemaining();
        }

        public static int GetPortalDelayMsBase()
        {
            return ClampTPDelayMs(AutoGemTPDelayMs);
        }

        public static int GetFullPortalDelayMs()
        {
            int delay = GetPortalDelayMsBase();
            if (AutoGemTPLagBoost) delay += AutoGemTPLagBoostMs;
            return delay;
        }
    }



    internal enum AutoGemOverlayCommand
    {
        None,
        ToggleMenu,
        HideMenu,
        ToggleMove,
        ToggleMenuButtonVisible,
        ToggleNoClickBg,
        ToggleEnabled,
        ToggleSection,
        SetModeAuto,
        SetModeFast150,
        SetModeHighest,
        SetModeLowest,
        SetModeSpecific,
        SetAnchor3,
        SetAnchor4,
        DelayMinus,
        DelayPlus,
        ToggleLag,
        ToggleSpecificSubMode,
        ToggleSpecificDropdown,
        ToggleDebug,
        SelectSpecificGem,
        BeginHotkeyCapture,
        BeginPanelDrag,
        BeginDotDrag,
        BeginSpecificScrollDrag,
        ScrollSpecificUp,
        ScrollSpecificDown,
        ToggleInfoPopup,
        CloseInfoPopup,
        JourneyCloseMask
    }

    // FREE TurboHUD self-contained menu: replaces LightningMod/Razor Movable overlay.
    // REV13 keeps the REV10 visible Journey X-mask, forces the overlay closed
    // on plugin load, and closes a bound Journey background with unconditional
    // Shift+J while delaying overlay/mask teardown until the close toggle is issued.
    public class s7o_AutoGemUpgradeMenu : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameTopPainter, IMouseClickHandler
    {
        private const string SettingsFileName = "s7o_AutoGemUpgrade.ini";
        private const string LegacySettingsFileName = "s7o_AutoGemUpgrade.settings.txt";
        private const int JourneyOpenGraceMs = 500;
        private const int ChatCloseBeforeJourneyCloseMs = 75;
        private const int JourneyCloseOverlayHideDelayMs = 140;

        private readonly AutoGemOverlayModel _model = new AutoGemOverlayModel();
        private readonly AutoGemOverlayRenderer _renderer = new AutoGemOverlayRenderer();
        private readonly AutoGemOverlayController _controller = new AutoGemOverlayController();

        private bool _overlayLeftDownBlocked;
        private bool _overlayMouseCapture;
        private bool _capturingMenuHotkey;
        private Key _menuHotkeyKey = Key.F7;
        private string _menuHotkeyLabel = "F7";

        private bool _journeyOpenedByAutoGemMenu;
        private bool _journeyWasVisibleBeforeMenu;
        private bool _journeyBackgroundActiveForMenu;
        private bool _journeyOpenRequested;
        private int _journeyOpenRequestTick = int.MinValue;
        private bool _journeyConfirmedVisibleForMenu;
        private bool _pendingJourneyCloseAfterChat;
        private int _pendingJourneyCloseAt = int.MinValue;
        private bool _pendingJourneyCloseHideMenu = true;
        private bool _pendingOverlayHideAfterJourneyClose;
        private bool _pendingOverlayHideShouldHideMenu = true;
        private int _pendingOverlayHideAt = int.MinValue;
        private int _lastChatEscapeTick = int.MinValue;
        private bool _initialJourneySyncDone;
        private bool _infoPopupVisible;
        private IUiElement _chatEditLine;
        private IUiElement _journeyUiElement;

        private AutoGemOverlayCommand _pendingCommand = AutoGemOverlayCommand.None;
        private int _pendingGemIndex = -1;
        private float _pendingMouseX;
        private float _pendingMouseY;

        private struct OverlayHitResult
        {
            public bool ShouldBlock;
            public AutoGemOverlayCommand Command;
            public int GemIndex;

            public OverlayHitResult(bool shouldBlock, AutoGemOverlayCommand command, int gemIndex)
            {
                ShouldBlock = shouldBlock;
                Command = command;
                GemIndex = gemIndex;
            }
        }

        public s7o_AutoGemUpgradeMenu() { Enabled = true; }

        public override void Load(IController hud)
        {
            base.Load(hud);
            LoadSettings();
            SetDebug(false);

            // Allow HUD Menu to persist Auto Gem state changes through the existing
            // standalone settings writer without duplicating settings-file logic.
            s7o_AutoGemUpgradeState.SaveSettingsRequested = SaveSettings;

            NormalizeLoadedState();
            // Public-test safety: never restore an open overlay/Journey session on HUD reload.
            _model.Visible = false;
            _model.SpecificExpanded = false;
            _infoPopupVisible = false;
            _capturingMenuHotkey = false;
            ClearJourneyBackgroundState();
            try { _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null); } catch { _chatEditLine = null; }
            try { _journeyUiElement = Hud.Render.RegisterUiElement("Root.NormalLayer.seasonal_check_dialog", null, null); } catch { _journeyUiElement = null; }
            _controller.ResetSnapshot(_model);

            if (StandaloneUiSuppressedByHudMenu())
                SuppressStandaloneUiForHudMenu();
        }

        public void AfterCollect()
        {
            if (StandaloneUiSuppressedByHudMenu())
            {
                SuppressStandaloneUiForHudMenu();
                return;
            }

            if (!_initialJourneySyncDone)
            {
                _initialJourneySyncDone = true;
                if (_model.Visible && _model.NoClickBackground) RequestJourneyBackgroundOpen();
            }
            ProcessPendingJourneyClose();
            ProcessPendingOverlayHideAfterJourneyClose();
            if (!_pendingJourneyCloseAfterChat && !_pendingOverlayHideAfterJourneyClose) SyncExternalJourneyClosure();
            ExecutePendingOverlayCommand();
            bool physicalLeftDown = false;
            try { physicalLeftDown = Hud.Input.IsKeyDown(Keys.LButton); } catch { }
            bool leftDown = physicalLeftDown || _overlayLeftDownBlocked;

            AutoGemOverlayLayout layout = AutoGemOverlayLayout.Build(Hud, _model);
            _controller.UpdateContinuous(Hud, _model, layout, leftDown);
            if (_controller.ConsumeDirty()) SaveSettings();
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (keyEvent == null || !keyEvent.IsPressed) return;

            if (StandaloneUiSuppressedByHudMenu())
            {
                SuppressStandaloneUiForHudMenu();
                return;
            }

            if (_capturingMenuHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingMenuHotkey = false;
                    return;
                }

                _menuHotkeyKey = keyEvent.Key;
                _menuHotkeyLabel = KeyLabel(_menuHotkeyKey);
                _capturingMenuHotkey = false;
                _controller.MarkDirty();
                SaveSettings();
                return;
            }

            if (keyEvent.Key == _menuHotkeyKey)
            {
                if (_model.Visible) CloseMenu(true);
                else OpenMenu();
                SaveSettings();
                return;
            }

            if (_model.Visible && (keyEvent.Key == Key.Escape || keyEvent.Key == Key.Space))
            {
                // Physical Escape/Space is allowed to close Journey directly.
                // Hide our overlay immediately and clear only our binding state; do not
                // synthesize a separate Journey close here.
                CloseMenu(false);
                ClearJourneyBackgroundState();
                SaveSettings();
                return;
            }
        }

        public bool MouseDown(MouseButtons button)
        {
            if (button != MouseButtons.Left) return false;

            if (StandaloneUiSuppressedByHudMenu())
            {
                SuppressStandaloneUiForHudMenu();
                return false;
            }

            AutoGemOverlayLayout layout;
            float x, y;
            try
            {
                layout = AutoGemOverlayLayout.Build(Hud, _model);
                x = Hud.Window.CursorX;
                y = Hud.Window.CursorY;
            }
            catch { return false; }

            OverlayHitResult hit = HitTestOverlay(layout, x, y);
            if (!hit.ShouldBlock && hit.Command == AutoGemOverlayCommand.None) return false;

            if (hit.Command == AutoGemOverlayCommand.JourneyCloseMask)
            {
                // This is a bound close zone over Diablo's Journey X. Hide our overlay
                // immediately, then return false so the same physical click still reaches
                // Diablo and closes Journey.
                LogMenuEvent("Journey X-mask clicked; closing overlay and passing click through");
                CloseMenu(false);
                ClearJourneyBackgroundState();
                SaveSettings();
                return false;
            }

            _pendingCommand = hit.Command;
            _pendingGemIndex = hit.GemIndex;
            _pendingMouseX = x;
            _pendingMouseY = y;
            _overlayMouseCapture = hit.ShouldBlock;
            _overlayLeftDownBlocked = hit.ShouldBlock;
            return hit.ShouldBlock;
        }

        public bool MouseUp(MouseButtons button)
        {
            if (button != MouseButtons.Left) return false;

            if (StandaloneUiSuppressedByHudMenu())
            {
                SuppressStandaloneUiForHudMenu();
                return false;
            }
            if (!_overlayMouseCapture && !IsOverlayClickTarget()) return false;

            _overlayMouseCapture = false;
            _overlayLeftDownBlocked = false;
            return true;
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip) return;

            if (StandaloneUiSuppressedByHudMenu())
            {
                SuppressStandaloneUiForHudMenu();
                return;
            }

            _renderer.EnsureResources(Hud);

            ExecutePendingOverlayCommand();

            bool physicalLeftDown = false;
            try { physicalLeftDown = Hud.Input.IsKeyDown(Keys.LButton); } catch { }
            bool leftDown = physicalLeftDown || _overlayLeftDownBlocked;

            AutoGemOverlayLayout layout = AutoGemOverlayLayout.Build(Hud, _model);
            _controller.UpdateContinuous(Hud, _model, layout, leftDown);
            if (_controller.ConsumeDirty()) SaveSettings();

            layout = AutoGemOverlayLayout.Build(Hud, _model);
            _renderer.Draw(Hud, _model, layout, _capturingMenuHotkey ? "press key..." : _menuHotkeyLabel, _infoPopupVisible, ShouldShowJourneyCloseMask());
        }

        private bool StandaloneUiSuppressedByHudMenu()
        {
            return s7o_AutoGemUpgradeState.IsUiOwnedByHudMenu();
        }

        private void SuppressStandaloneUiForHudMenu()
        {
            if (!_model.Visible &&
                !_infoPopupVisible &&
                !_capturingMenuHotkey &&
                !_overlayMouseCapture &&
                !_overlayLeftDownBlocked &&
                _pendingCommand == AutoGemOverlayCommand.None &&
                !_journeyBackgroundActiveForMenu &&
                !_journeyOpenedByAutoGemMenu &&
                !_pendingJourneyCloseAfterChat &&
                !_pendingOverlayHideAfterJourneyClose)
            {
                return;
            }

            _model.Visible = false;
            _model.SpecificExpanded = false;
            _infoPopupVisible = false;
            _capturingMenuHotkey = false;
            _overlayMouseCapture = false;
            _overlayLeftDownBlocked = false;
            _pendingCommand = AutoGemOverlayCommand.None;
            _pendingGemIndex = -1;
            ClearJourneyBackgroundState();
            _controller.ResetSnapshot(_model);
        }

        private void ExecutePendingOverlayCommand()
        {
            if (_pendingCommand == AutoGemOverlayCommand.None) return;

            AutoGemOverlayCommand command = _pendingCommand;
            int gemIndex = _pendingGemIndex;
            float x = _pendingMouseX;
            float y = _pendingMouseY;
            _pendingCommand = AutoGemOverlayCommand.None;
            _pendingGemIndex = -1;

            if (command == AutoGemOverlayCommand.ToggleMenu)
            {
                if (_model.Visible) CloseMenu(true);
                else OpenMenu();
                return;
            }
            if (command == AutoGemOverlayCommand.HideMenu)
            {
                CloseMenu(true);
                return;
            }
            if (command == AutoGemOverlayCommand.JourneyCloseMask)
            {
                CloseMenu(false);
                ClearJourneyBackgroundState();
                SaveSettings();
                return;
            }
            if (command == AutoGemOverlayCommand.ToggleNoClickBg)
            {
                _model.NoClickBackground = !_model.NoClickBackground;
                if (_model.Visible)
                {
                    if (_model.NoClickBackground) RequestJourneyBackgroundOpen();
                    else RequestJourneyBackgroundClose(false);
                }
                _controller.MarkDirty();
                SaveSettings();
                return;
            }
            if (command == AutoGemOverlayCommand.ToggleInfoPopup)
            {
                _infoPopupVisible = !_infoPopupVisible;
                return;
            }
            if (command == AutoGemOverlayCommand.CloseInfoPopup)
            {
                _infoPopupVisible = false;
                return;
            }

            AutoGemOverlayLayout layout = AutoGemOverlayLayout.Build(Hud, _model);
            _controller.ExecuteCommand(Hud, _model, layout, command, gemIndex, x, y);
            if (command == AutoGemOverlayCommand.BeginHotkeyCapture)
            {
                _capturingMenuHotkey = true;
            }
        }

        private void OpenMenu()
        {
            if (_model.Visible) return;
            _model.Visible = true;
            LogMenuEvent("menu open");
            if (_model.NoClickBackground) RequestJourneyBackgroundOpen();
            else ClearJourneyBackgroundState();
            _controller.MarkDirty();
        }

        private void CloseMenu(bool closeJourney)
        {
            if (!_model.Visible && !_infoPopupVisible && !_capturingMenuHotkey)
            {
                if (closeJourney) RequestJourneyBackgroundClose(true);
                return;
            }

            if (closeJourney && IsJourneyBoundForMenu())
            {
                LogMenuEvent("menu close requested; Journey bound, delaying overlay hide until Shift+J close is issued");
                _model.SpecificExpanded = false;
                _infoPopupVisible = false;
                _capturingMenuHotkey = false;
                RequestJourneyBackgroundClose(true);
                _controller.MarkDirty();
                return;
            }

            HideOverlayStateOnly();
            LogMenuEvent("menu close; closeJourney=" + closeJourney.ToString());
            if (closeJourney) ClearJourneyBackgroundState();
            _controller.MarkDirty();
        }

        private void HideOverlayStateOnly()
        {
            _model.Visible = false;
            _model.SpecificExpanded = false;
            _infoPopupVisible = false;
            _capturingMenuHotkey = false;
        }

        private bool IsChatEntryOpen()
        {
            try { return _chatEditLine != null && _chatEditLine.Visible; }
            catch { return false; }
        }

        private bool IsJourneyWindowVisible()
        {
            try { return _journeyUiElement != null && _journeyUiElement.Visible; }
            catch { return false; }
        }

        private bool IsJourneyBoundForMenu()
        {
            return _journeyBackgroundActiveForMenu || _journeyOpenedByAutoGemMenu || _journeyConfirmedVisibleForMenu;
        }

        private void CloseChatIfNeeded()
        {
            if (!IsChatEntryOpen()) return;
            try
            {
                FreeHudInput.SendEscape();
                _lastChatEscapeTick = Environment.TickCount;
                Thread.Sleep(35);
            }
            catch { }
        }

        private void RequestJourneyBackgroundOpen()
        {
            if (!_model.Visible || !_model.NoClickBackground) return;
            try { if (Hud.Window == null || !Hud.Window.IsForeground || Hud.Game == null || !Hud.Game.IsInGame) return; } catch { return; }

            bool visibleNow = IsJourneyWindowVisible();
            if (_journeyBackgroundActiveForMenu)
            {
                _journeyOpenRequested = true;
                if (_journeyOpenRequestTick == int.MinValue) _journeyOpenRequestTick = Environment.TickCount;
                if (visibleNow) _journeyConfirmedVisibleForMenu = true;
                return;
            }

            _journeyWasVisibleBeforeMenu = visibleNow;
            _journeyBackgroundActiveForMenu = true;
            _journeyOpenRequested = true;
            _journeyOpenRequestTick = Environment.TickCount;
            _journeyConfirmedVisibleForMenu = visibleNow;

            if (visibleNow)
            {
                _journeyOpenedByAutoGemMenu = false;
                return;
            }

            try
            {
                CloseChatIfNeeded();
                LogMenuEvent("Journey open request: Shift+J sent");
                FreeHudInput.SendShiftJ();
                _journeyOpenRequestTick = Environment.TickCount;
                _journeyOpenedByAutoGemMenu = true;
                _journeyConfirmedVisibleForMenu = false;
            }
            catch { }
        }

        private void RequestJourneyBackgroundClose(bool hideMenuAfterClose)
        {
            // Close a Journey background because this overlay believes it is bound,
            // not because the instantaneous UI visibility probe happens to be true.
            bool bound = IsJourneyBoundForMenu();
            if (!bound)
            {
                if (hideMenuAfterClose)
                {
                    HideOverlayStateOnly();
                    _controller.MarkDirty();
                }
                return;
            }

            LogMenuEvent("Journey close requested; bound=true; hideMenuAfterClose=" + hideMenuAfterClose.ToString());

            try
            {
                if (Hud.Window == null || !Hud.Window.IsForeground || Hud.Game == null || !Hud.Game.IsInGame)
                {
                    if (hideMenuAfterClose) HideOverlayStateOnly();
                    ClearJourneyBackgroundState();
                    _controller.MarkDirty();
                    return;
                }
            }
            catch
            {
                if (hideMenuAfterClose) HideOverlayStateOnly();
                ClearJourneyBackgroundState();
                _controller.MarkDirty();
                return;
            }

            try
            {
                if (IsChatEntryOpen())
                {
                    LogMenuEvent("chat detected before Journey close; Escape sent and Shift+J deferred");
                    FreeHudInput.SendEscape();
                    _lastChatEscapeTick = Environment.TickCount;
                    _pendingJourneyCloseAfterChat = true;
                    _pendingJourneyCloseHideMenu = hideMenuAfterClose;
                    _pendingJourneyCloseAt = unchecked(Environment.TickCount + ChatCloseBeforeJourneyCloseMs);
                    return;
                }

                LogMenuEvent("Journey close request: unconditional Shift+J sent");
                FreeHudInput.SendShiftJ();
                QueueOverlayHideAfterJourneyClose(hideMenuAfterClose);
            }
            catch
            {
                if (hideMenuAfterClose) HideOverlayStateOnly();
                ClearJourneyBackgroundState();
                _controller.MarkDirty();
            }
        }

        private void QueueOverlayHideAfterJourneyClose(bool hideMenuAfterClose)
        {
            _pendingOverlayHideAfterJourneyClose = true;
            _pendingOverlayHideShouldHideMenu = hideMenuAfterClose;
            _pendingOverlayHideAt = unchecked(Environment.TickCount + JourneyCloseOverlayHideDelayMs);
            LogMenuEvent("overlay/mask teardown queued after Journey Shift+J; hideMenu=" + hideMenuAfterClose.ToString());
        }

        private void ProcessPendingJourneyClose()
        {
            if (!_pendingJourneyCloseAfterChat) return;
            int now = Environment.TickCount;
            if (unchecked(now - _pendingJourneyCloseAt) < 0) return;

            bool hideMenuAfterClose = _pendingJourneyCloseHideMenu;
            _pendingJourneyCloseAfterChat = false;
            _pendingJourneyCloseAt = int.MinValue;
            _pendingJourneyCloseHideMenu = true;

            try
            {
                LogMenuEvent("pending Journey close after chat: unconditional Shift+J sent");
                FreeHudInput.SendShiftJ();
                QueueOverlayHideAfterJourneyClose(hideMenuAfterClose);
            }
            catch
            {
                if (hideMenuAfterClose) HideOverlayStateOnly();
                ClearJourneyBackgroundState();
                _controller.MarkDirty();
            }
        }

        private void ProcessPendingOverlayHideAfterJourneyClose()
        {
            if (!_pendingOverlayHideAfterJourneyClose) return;
            int now = Environment.TickCount;
            if (unchecked(now - _pendingOverlayHideAt) < 0) return;

            bool hideMenu = _pendingOverlayHideShouldHideMenu;
            _pendingOverlayHideAfterJourneyClose = false;
            _pendingOverlayHideAt = int.MinValue;
            _pendingOverlayHideShouldHideMenu = true;

            if (hideMenu)
            {
                HideOverlayStateOnly();
                LogMenuEvent("overlay hidden after Journey Shift+J close delay");
            }
            else
            {
                LogMenuEvent("Journey mask/binding cleared after Shift+J close delay");
            }

            ClearJourneyBackgroundState();
            _controller.MarkDirty();
            SaveSettings();
        }

        private void ClearJourneyBackgroundState()
        {
            _journeyOpenedByAutoGemMenu = false;
            _journeyWasVisibleBeforeMenu = false;
            _journeyBackgroundActiveForMenu = false;
            _journeyOpenRequested = false;
            _journeyOpenRequestTick = int.MinValue;
            _journeyConfirmedVisibleForMenu = false;
            _pendingJourneyCloseAfterChat = false;
            _pendingJourneyCloseAt = int.MinValue;
            _pendingJourneyCloseHideMenu = true;
            _pendingOverlayHideAfterJourneyClose = false;
            _pendingOverlayHideShouldHideMenu = true;
            _pendingOverlayHideAt = int.MinValue;
        }

        private void SyncExternalJourneyClosure()
        {
            if (!_model.Visible || !_journeyBackgroundActiveForMenu || !_journeyOpenRequested || _journeyUiElement == null) return;

            bool visibleNow = IsJourneyWindowVisible();
            if (visibleNow)
            {
                _journeyConfirmedVisibleForMenu = true;
                return;
            }

            int elapsed = unchecked(Environment.TickCount - _journeyOpenRequestTick);
            if (elapsed < JourneyOpenGraceMs) return;

            // Do not hide the overlay during Journey's open latency. Only treat a missing
            // Journey window as an external close after it was confirmed visible at least once.
            if (!_journeyConfirmedVisibleForMenu) return;

            ClearJourneyBackgroundState();
            CloseMenu(false);
            SaveSettings();
        }

        private bool ShouldShowJourneyCloseMask()
        {
            // Keep the visible close-zone highlight present for the entire bound menu
            // session and during the brief close delay after Shift+J is issued.
            return _model.Visible && (_journeyBackgroundActiveForMenu || _pendingOverlayHideAfterJourneyClose || _pendingJourneyCloseAfterChat);
        }

        private bool IsOverlayClickTarget()
        {
            try
            {
                AutoGemOverlayLayout layout = AutoGemOverlayLayout.Build(Hud, _model);
                float x = Hud.Window.CursorX;
                float y = Hud.Window.CursorY;
                return HitTestOverlay(layout, x, y).ShouldBlock;
            }
            catch { }
            return false;
        }

        private OverlayHitResult HitTestOverlay(AutoGemOverlayLayout layout, float x, float y)
        {
            if (_model.Visible && ShouldShowJourneyCloseMask() && Contains(layout.JourneyCloseMask, x, y, 4f))
                return new OverlayHitResult(false, AutoGemOverlayCommand.JourneyCloseMask, -1);

            // The bottom-right dot is always independent of the panel. In MOVE mode it drags;
            // otherwise it toggles the main panel.
            if (_model.ShowMenuDot && Contains(layout.DotHitRect, x, y, 8f))
                return new OverlayHitResult(true, _model.EditMode ? AutoGemOverlayCommand.BeginDotDrag : AutoGemOverlayCommand.ToggleMenu, -1);

            if (!_model.Visible) return new OverlayHitResult(false, AutoGemOverlayCommand.None, -1);

            if (Contains(layout.InfoButton, x, y, 7f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleInfoPopup, -1);
            if (_infoPopupVisible)
            {
                if (Contains(layout.InfoPopup, x, y, 2f)) return new OverlayHitResult(true, AutoGemOverlayCommand.None, -1);
                return new OverlayHitResult(true, AutoGemOverlayCommand.CloseInfoPopup, -1);
            }

            if (Contains(layout.HotkeyButton, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.BeginHotkeyCapture, -1);
            if (Contains(layout.MenuDotButton, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleMenuButtonVisible, -1);
            if (Contains(layout.EditButton, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleMove, -1);
            if (Contains(layout.HideButton, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.HideMenu, -1);

            if (_model.EditMode && Contains(layout.HeaderDragArea, x, y, 4f)) return new OverlayHitResult(true, AutoGemOverlayCommand.BeginPanelDrag, -1);

            if (Contains(layout.NoClickCheck, x, y, 7f) || Contains(layout.NoClickLabel, x, y, 4f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleNoClickBg, -1);
            if (Contains(layout.EnabledCheck, x, y, 7f) || Contains(layout.EnabledLabel, x, y, 4f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleEnabled, -1);
            if (Contains(layout.SectionToggle, x, y, 6f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleSection, -1);

            if (_model.AutoGemExpanded)
            {
                if (Contains(layout.ModeAuto, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetModeAuto, -1);
                if (Contains(layout.ModeFast150, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetModeFast150, -1);
                if (Contains(layout.ModeHighest, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetModeHighest, -1);
                if (Contains(layout.ModeLowest, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetModeLowest, -1);
                if (Contains(layout.ModeSpecific, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetModeSpecific, -1);
                if (Contains(layout.Anchor3, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetAnchor3, -1);
                if (Contains(layout.Anchor4, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SetAnchor4, -1);
                if (Contains(layout.DelayMinus, x, y, 7f)) return new OverlayHitResult(true, AutoGemOverlayCommand.DelayMinus, -1);
                if (Contains(layout.DelayPlus, x, y, 7f)) return new OverlayHitResult(true, AutoGemOverlayCommand.DelayPlus, -1);
                if (Contains(layout.DelayValue, x, y, 3f)) return new OverlayHitResult(true, AutoGemOverlayCommand.None, -1);
                if (Contains(layout.LagBoost, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleLag, -1);
                if (Contains(layout.SpecificSubMode, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleSpecificSubMode, -1);
                if (Contains(layout.SpecificValue, x, y, 4f) || Contains(layout.SpecificToggle, x, y, 7f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleSpecificDropdown, -1);

                if (_model.SpecificExpanded)
                {
                    if (Contains(layout.ScrollUp, x, y, 8f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ScrollSpecificUp, -1);
                    if (Contains(layout.ScrollDown, x, y, 8f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ScrollSpecificDown, -1);
                    if (Contains(layout.ScrollThumb, x, y, 8f) || Contains(layout.ScrollTrack, x, y, 5f)) return new OverlayHitResult(true, AutoGemOverlayCommand.BeginSpecificScrollDrag, -1);
                    for (int i = 0; i < layout.GemOptions.Count; i++)
                    {
                        GemOptionLayout opt = layout.GemOptions[i];
                        if (Contains(opt.Rect, x, y, 4f)) return new OverlayHitResult(true, AutoGemOverlayCommand.SelectSpecificGem, opt.Index);
                    }
                    if (Contains(layout.SpecificList, x, y, 2f)) return new OverlayHitResult(true, AutoGemOverlayCommand.None, -1);
                }
            }

            if (Contains(layout.DebugCheck, x, y, 7f) || Contains(layout.DebugLabel, x, y, 4f)) return new OverlayHitResult(true, AutoGemOverlayCommand.ToggleDebug, -1);

            if (_model.NoClickBackground && Contains(layout.Panel, x, y, 0f)) return new OverlayHitResult(true, AutoGemOverlayCommand.None, -1);
            return new OverlayHitResult(false, AutoGemOverlayCommand.None, -1);
        }

        private static bool Contains(RectangleF rect, float x, float y, float inflate)
        {
            if (rect.Width <= 0f || rect.Height <= 0f) return false;
            return x >= rect.Left - inflate && x <= rect.Right + inflate && y >= rect.Top - inflate && y <= rect.Bottom + inflate;
        }

        private void NormalizeLoadedState()
        {
            s7o_AutoGemUpgradeState.AutoGemMode = ClampInt(s7o_AutoGemUpgradeState.AutoGemMode, 0, 4);
            s7o_AutoGemUpgradeState.AutoGemSpecificSubMode = ClampInt(s7o_AutoGemUpgradeState.AutoGemSpecificSubMode, 0, 1);
            s7o_AutoGemUpgradeState.AutoGemTPDelayMs = s7o_AutoGemUpgradeState.ClampTPDelayMs(s7o_AutoGemUpgradeState.AutoGemTPDelayMs);
            s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = s7o_AutoGemUpgradeState.ClampTPAnchorRemaining(s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining);
            if (string.IsNullOrWhiteSpace(s7o_AutoGemUpgradeState.AutoGemSpecificName))
                s7o_AutoGemUpgradeState.AutoGemSpecificName = AutoGemOverlayModel.DefaultSpecificGemName;
            _model.SpecificScroll = ClampInt(_model.SpecificScroll, 0, Math.Max(0, AutoGemOverlayModel.AutoGemNames.Length - 1));
            _model.NormalizeRects(Hud);
            _model.Visible = false;
            if (string.IsNullOrWhiteSpace(_menuHotkeyLabel)) _menuHotkeyLabel = KeyLabel(_menuHotkeyKey);
            SetDebug(false);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string KeyLabel(Key key)
        {
            string raw = key.ToString();
            if (string.IsNullOrEmpty(raw)) return "?";
            if (raw.StartsWith("D", StringComparison.Ordinal) && raw.Length == 2 && char.IsDigit(raw[1])) return raw.Substring(1);
            if (raw.StartsWith("NumberPad", StringComparison.Ordinal)) return "NUM" + raw.Substring("NumberPad".Length);
            return raw.ToUpperInvariant();
        }

        private static void SetDebug(bool enabled)
        {
            s7o_AutoGemUpgradeState.DebugEnabled = enabled;
            s7o_AutoGemUpgradeState.DebugFileEnabled = enabled;
            s7o_AutoGemUpgradeState.DebugLevel = enabled
                ? s7o_AutoGemUpgradeState.DebugLevelState
                : s7o_AutoGemUpgradeState.DebugLevelOff;

            if (!enabled)
            {
                s7o_AutoGemUpgradeState.DebugLogPath = string.Empty;
                return;
            }

            s7o_AutoGemUpgradeState.DebugLogPath = FreeHudDebugPath();
        }

        private static string FreeHudDebugPath()
        {
            try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o", s7o_AutoGemUpgradeState.DebugLogFileName); }
            catch { return s7o_AutoGemUpgradeState.DebugLogFileName; }
        }

        private void LogMenuEvent(string message)
        {
            if (!s7o_AutoGemUpgradeState.DebugEnabled || !s7o_AutoGemUpgradeState.DebugFileEnabled) return;
            try
            {
                if (string.IsNullOrWhiteSpace(s7o_AutoGemUpgradeState.DebugLogPath))
                    s7o_AutoGemUpgradeState.DebugLogPath = FreeHudDebugPath();
                string dirName = Path.GetDirectoryName(s7o_AutoGemUpgradeState.DebugLogPath);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName)) Directory.CreateDirectory(dirName);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " menu: " + (message ?? string.Empty);
                File.AppendAllText(s7o_AutoGemUpgradeState.DebugLogPath, line + Environment.NewLine);
            }
            catch { }
        }

        private string SettingsDirectory()
        {
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "s7o", "settings");
            }
            catch
            {
                return "settings";
            }
        }

        private string SettingsPath()
        {
            try
            {
                return Path.Combine(SettingsDirectory(), SettingsFileName);
            }
            catch
            {
                return SettingsFileName;
            }
        }

        private string LegacySettingsPath()
        {
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "s7o", LegacySettingsFileName);
            }
            catch
            {
                return LegacySettingsFileName;
            }
        }

        private string ResolveSettingsLoadPath()
        {
            try
            {
                string current = SettingsPath();
                if (File.Exists(current))
                    return current;

                string legacy = LegacySettingsPath();
                if (File.Exists(legacy))
                    return legacy;

                return current;
            }
            catch
            {
                return SettingsPath();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _model.NormalizeRects(Hud);
                var lines = new List<string>();
                lines.Add("# s7o Auto Gem Upgrade FreeHUD settings");
                // Do not persist open-state for public testing; overlay must start closed after HUD reload.
                lines.Add("MENU_VISIBLE=False");
                lines.Add("VISIBLE=False");
                lines.Add("EDITMODE=" + _model.EditMode);
                lines.Add("SHOWMENUDOT=" + _model.ShowMenuDot);
                lines.Add("AUTOGEM_MENU_X=" + _model.PanelRect.X.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_MENU_Y=" + _model.PanelRect.Y.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_MENU_VISIBLE=False");
                lines.Add("AUTOGEM_MENU_BUTTON_VISIBLE=" + _model.ShowMenuDot);
                lines.Add("AUTOGEM_MOVE_MODE=" + _model.EditMode);
                lines.Add("AUTOGEM_NOCLICK_BACKGROUND=" + _model.NoClickBackground);
                lines.Add("AUTOGEM_BUTTON_X=" + _model.DotRect.X.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_BUTTON_Y=" + _model.DotRect.Y.ToString(CultureInfo.InvariantCulture));
                lines.Add("WINX=" + _model.PanelRect.X.ToString(CultureInfo.InvariantCulture));
                lines.Add("WINY=" + _model.PanelRect.Y.ToString(CultureInfo.InvariantCulture));
                lines.Add("WINW=" + _model.PanelRect.Width.ToString(CultureInfo.InvariantCulture));
                lines.Add("WINH=" + _model.PanelRect.Height.ToString(CultureInfo.InvariantCulture));
                lines.Add("DOTX=" + _model.DotRect.X.ToString(CultureInfo.InvariantCulture));
                lines.Add("DOTY=" + _model.DotRect.Y.ToString(CultureInfo.InvariantCulture));
                lines.Add("MENU_HOTKEY=" + _menuHotkeyKey);
                lines.Add("MENU_HOTKEY_LABEL=" + _menuHotkeyLabel);
                lines.Add("AUTOGEM_ENABLED=" + s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled);
                lines.Add("AUTOGEM_ON=" + s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled);
                lines.Add("AUTOGEM_MODE=" + s7o_AutoGemUpgradeState.AutoGemMode.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_NAME=" + (s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty));
                lines.Add("AUTOGEM_SPEC_SUBMODE=" + s7o_AutoGemUpgradeState.AutoGemSpecificSubMode.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_TP_DELAY_MODE=ANCHOR_DELAY");
                lines.Add("AUTOGEM_TP_DELAY_MS=" + s7o_AutoGemUpgradeState.ClampTPDelayMs(s7o_AutoGemUpgradeState.AutoGemTPDelayMs).ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_TP_ANCHOR=" + s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining().ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_TP_LAG=" + s7o_AutoGemUpgradeState.AutoGemTPLagBoost);
                lines.Add("AUTOGEM_EXPANDED=" + _model.AutoGemExpanded);
                lines.Add("AUTOGEM_SPEC_EXPANDED=" + _model.SpecificExpanded);
                lines.Add("AUTOGEM_SPEC_SCROLL=" + _model.SpecificScroll.ToString(CultureInfo.InvariantCulture));
                // Debug logging is intentionally not persisted ON in public releases.
                // It should always start disabled unless manually enabled for the current session.
                lines.Add("DEBUG_ENABLED=false");
                lines.Add("DEBUG_ON=false");

                string dir = SettingsDirectory();
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllLines(SettingsPath(), lines.ToArray());
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string path = ResolveSettingsLoadPath();
                if (!File.Exists(path)) return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    float fv; int iv; bool bv; Key k;
                    if (key == "MENU_VISIBLE" || key == "VISIBLE") { /* ignored: public-test builds always start closed */ }
                    else if (key == "EDITMODE" && bool.TryParse(val, out bv)) _model.EditMode = bv;
                    else if ((key == "SHOWMENUDOT" || key == "AUTOGEM_MENU_BUTTON_VISIBLE") && bool.TryParse(val, out bv)) _model.ShowMenuDot = bv;
                    else if (key == "AUTOGEM_MENU_VISIBLE") { /* ignored: public-test builds always start closed */ }
                    else if (key == "AUTOGEM_MOVE_MODE" && bool.TryParse(val, out bv)) _model.EditMode = bv;
                    else if (key == "AUTOGEM_NOCLICK_BACKGROUND" && bool.TryParse(val, out bv)) _model.NoClickBackground = bv;
                    else if (key == "AUTOGEM_MENU_X" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.X = fv;
                    else if (key == "AUTOGEM_MENU_Y" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.Y = fv;
                    else if (key == "AUTOGEM_BUTTON_X" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.DotRect.X = fv;
                    else if (key == "AUTOGEM_BUTTON_Y" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.DotRect.Y = fv;
                    else if (key == "WINX" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.X = fv;
                    else if (key == "WINY" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.Y = fv;
                    else if (key == "WINW" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.Width = fv;
                    else if (key == "WINH" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.PanelRect.Height = fv;
                    else if (key == "DOTX" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.DotRect.X = fv;
                    else if (key == "DOTY" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out fv)) _model.DotRect.Y = fv;
                    else if (key == "MENU_HOTKEY" && Enum.TryParse<Key>(val, out k)) { _menuHotkeyKey = k; _menuHotkeyLabel = KeyLabel(k); }
                    else if (key == "MENU_HOTKEY_LABEL") _menuHotkeyLabel = val;
                    else if ((key == "AUTOGEM_ENABLED" || key == "AUTOGEM_ON") && bool.TryParse(val, out bv)) s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled = bv;
                    else if (key == "AUTOGEM_MODE" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) s7o_AutoGemUpgradeState.AutoGemMode = iv;
                    else if (key == "AUTOGEM_NAME") s7o_AutoGemUpgradeState.AutoGemSpecificName = string.IsNullOrWhiteSpace(val) ? AutoGemOverlayModel.DefaultSpecificGemName : val;
                    else if (key == "AUTOGEM_SPEC_SUBMODE" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) s7o_AutoGemUpgradeState.AutoGemSpecificSubMode = iv;
                    else if (key == "AUTOGEM_TP_DELAY_MS" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) s7o_AutoGemUpgradeState.AutoGemTPDelayMs = s7o_AutoGemUpgradeState.ClampTPDelayMs(iv);
                    else if (key == "AUTOGEM_TP_ANCHOR" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = s7o_AutoGemUpgradeState.ClampTPAnchorRemaining(iv);
                    else if (key == "AUTOGEM_TP_LAG" && bool.TryParse(val, out bv)) s7o_AutoGemUpgradeState.AutoGemTPLagBoost = bv;
                    else if (key == "AUTOGEM_EXPANDED" && bool.TryParse(val, out bv)) _model.AutoGemExpanded = bv;
                    else if (key == "AUTOGEM_SPEC_EXPANDED" && bool.TryParse(val, out bv)) _model.SpecificExpanded = bv;
                    else if (key == "AUTOGEM_SPEC_SCROLL" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) _model.SpecificScroll = Math.Max(0, iv);
                    else if (key == "DEBUG_ENABLED" || key == "DEBUG_ON")
                    {
                        // Ignore persisted debug flags in release builds.
                        // Debug should not auto-enable from saved settings.
                        SetDebug(false);
                    }
                }
            }
            catch { }
        }
    }

    internal sealed class AutoGemOverlayModel
    {
        public const string DefaultSpecificGemName = "Bane of the Trapped";
        public bool Visible;
        public bool AutoGemExpanded = true;
        public bool _autoGemSpecificExpanded;
        public int _autoGemSpecificScroll;
        public bool DraggingSpecificScroll;

        public RectangleF PanelRect = new RectangleF(8f, 6f, 690f, 490f);
        public RectangleF DotRect = new RectangleF(-1f, -1f, 18f, 18f);
        public bool EditMode;
        public bool ShowMenuDot = true;
        public bool NoClickBackground = true;
        public int DotColorIdx = 0;
        public int DotOpenColorIdx = 3;
        public int DotSize = 5;
        public int DotOpenSize = 7;
        public bool DraggingPanel;
        public bool DraggingDot;
        public float DragOffsetX;
        public float DragOffsetY;

        public bool SpecificExpanded { get { return _autoGemSpecificExpanded; } set { _autoGemSpecificExpanded = value; } }
        public int SpecificScroll { get { return _autoGemSpecificScroll; } set { _autoGemSpecificScroll = value; } }

        public static readonly string[] _autoGemNames =
        {
            "Bane of the Powerful",
            "Bane of the Stricken",
            "Bane of the Trapped",
            "Boon of the Hoarder",
            "Boyarsky's Chip",
            "Enforcer",
            "Esoteric Alteration",
            "Gem of Ease",
            "Gem of Efficacious Toxin",
            "Gogok of Swiftness",
            "Iceblink",
            "Invigorating Gemstone",
            "Legacy of Dreams",
            "Mirinae, Teardrop of the Starweaver",
            "Molten Wildebeest's Gizzard",
            "Moratorium",
            "Mutilation Guard",
            "Pain Enhancer",
            "Simplicity's Strength",
            "Taeguk",
            "Wreath of Lightning",
            "Whisper of Atonement",
            "Zei's Stone of Vengeance",
        };

        public static string[] AutoGemNames { get { return _autoGemNames; } }

        public void NormalizeRects(IController hud)
        {
            if (PanelRect.Width < 690f) PanelRect.Width = 690f;
            if (PanelRect.Height < 490f) PanelRect.Height = 490f;
            if (DotRect.Width <= 0f) DotRect.Width = 18f;
            if (DotRect.Height <= 0f) DotRect.Height = 18f;
            try
            {
                float sw = hud.Window.Size.Width;
                float sh = hud.Window.Size.Height;
                if (DotRect.X < 0f && sw > 100f) DotRect.X = Math.Max(0f, sw - 34f);
                if (DotRect.Y < 0f && sh > 100f) DotRect.Y = Math.Max(0f, sh - 34f);
                if (sw > 100f)
                {
                    if (PanelRect.Width > sw) PanelRect.Width = sw;
                    if (PanelRect.Left < 0f) PanelRect.X = 0f;
                    if (PanelRect.Right > sw) PanelRect.X = Math.Max(0f, sw - PanelRect.Width);
                    if (DotRect.Left < 0f) DotRect.X = 0f;
                    if (DotRect.Right > sw) DotRect.X = Math.Max(0f, sw - DotRect.Width);
                }
                if (sh > 100f)
                {
                    if (PanelRect.Height > sh) PanelRect.Height = sh;
                    if (PanelRect.Top < 0f) PanelRect.Y = 0f;
                    if (PanelRect.Bottom > sh) PanelRect.Y = Math.Max(0f, sh - PanelRect.Height);
                    if (DotRect.Top < 0f) DotRect.Y = 0f;
                    if (DotRect.Bottom > sh) DotRect.Y = Math.Max(0f, sh - DotRect.Height);
                }
            }
            catch { }
        }
    }

    internal sealed class AutoGemOverlayLayout
    {
        public RectangleF Indicator;
        public RectangleF DotHitRect;
        public RectangleF DotDragFrame;
        public RectangleF Panel;
        public RectangleF TitleBar;
        public RectangleF HeaderDragArea;
        public RectangleF HotkeyButton;
        public RectangleF MenuDotButton;
        public RectangleF EditButton;
        public RectangleF HideButton;
        public RectangleF MainPane;
        public RectangleF InfoButton;
        public RectangleF InfoPopup;
        public RectangleF JourneyCloseMask;
        public RectangleF StatusRow;
        public RectangleF NoClickCheck;
        public RectangleF NoClickLabel;
        public RectangleF EnabledCheck;
        public RectangleF EnabledLabel;
        public RectangleF SectionToggle;
        public RectangleF ModeAuto;
        public RectangleF ModeFast150;
        public RectangleF ModeHighest;
        public RectangleF ModeLowest;
        public RectangleF ModeSpecific;
        public RectangleF Anchor3;
        public RectangleF Anchor4;
        public RectangleF DelayMinus;
        public RectangleF DelayValue;
        public RectangleF DelayPlus;
        public RectangleF LagBoost;
        public RectangleF SpecificValue;
        public RectangleF SpecificSubMode;
        public RectangleF SpecificToggle;
        public RectangleF SpecificList;
        public RectangleF ScrollUp;
        public RectangleF ScrollDown;
        public RectangleF ScrollTrack;
        public RectangleF ScrollThumb;
        public RectangleF DebugCheck;
        public RectangleF DebugLabel;
        public int VisibleGemRows;
        public int MaxGemScroll;
        public readonly List<GemOptionLayout> GemOptions = new List<GemOptionLayout>();

        public static AutoGemOverlayLayout Build(IController hud, AutoGemOverlayModel model)
        {
            model.NormalizeRects(hud);
            var l = new AutoGemOverlayLayout();
            RectangleF panel = model.PanelRect;
            float requiredHeight = 490f + (model.SpecificExpanded ? 40f : 0f);
            if (panel.Height < requiredHeight) panel.Height = requiredHeight;
            try
            {
                float sh = hud.Window.Size.Height;
                if (sh > 100f && panel.Bottom > sh) panel.Y = Math.Max(0f, sh - panel.Height);
            }
            catch { }
            l.Panel = panel;
            l.Indicator = model.DotRect;
            l.DotHitRect = new RectangleF(model.DotRect.Left - 12f, model.DotRect.Top - 12f, model.DotRect.Width + 24f, model.DotRect.Height + 24f);
            l.DotDragFrame = new RectangleF(model.DotRect.Left - 6f, model.DotRect.Top - 6f, model.DotRect.Width + 12f, model.DotRect.Height + 12f);

            l.TitleBar = new RectangleF(panel.Left + 6f, panel.Top + 6f, panel.Width - 12f, 34f);
            l.HideButton = new RectangleF(panel.Right - 68f, panel.Top + 12f, 54f, 20f);
            l.EditButton = new RectangleF(l.HideButton.Left - 4f - 82f, panel.Top + 12f, 82f, 20f);
            l.MenuDotButton = new RectangleF(l.EditButton.Left - 8f - 128f, panel.Top + 12f, 128f, 20f);
            l.HotkeyButton = new RectangleF(l.MenuDotButton.Left - 96f, panel.Top + 13f, 78f, 18f);
            l.HeaderDragArea = new RectangleF(panel.Left + 6f, panel.Top + 6f, Math.Max(1f, l.HotkeyButton.Left - panel.Left - 12f), 34f);

            float margin = 16f;
            float topY = panel.Top + 52f;
            float paneH = panel.Height - 68f;
            l.MainPane = new RectangleF(panel.Left + margin, topY, panel.Width - margin * 2f, paneH);
            l.InfoButton = new RectangleF(l.MainPane.Left + 170.5f, l.MainPane.Top + 6.5f, 15f, 15f);
            l.InfoPopup = new RectangleF(l.MainPane.Left + 12f, l.MainPane.Top + 31f, 430f, 118f);
            try
            {
                float sw = hud.Window.Size.Width;
                float sh = hud.Window.Size.Height;
                if (sw > 100f && sh > 100f)
                    l.JourneyCloseMask = new RectangleF(sw * (1567f / 1920f), sh * (100f / 1080f), sw * (23f / 1920f), sh * (23f / 1080f));
                else
                    l.JourneyCloseMask = new RectangleF(1567f, 100f, 23f, 23f);
            }
            catch { l.JourneyCloseMask = new RectangleF(1567f, 100f, 23f, 23f); }

            float contentX = l.MainPane.Left + 12f;
            float contentW = l.MainPane.Width - 24f;
            float y = l.MainPane.Top + 34f;
            l.StatusRow = new RectangleF(contentX, y, contentW, 18f);
            l.NoClickCheck = new RectangleF(l.StatusRow.Right - 138f, l.StatusRow.Top + 3f, 12f, 12f);
            l.NoClickLabel = new RectangleF(l.NoClickCheck.Right + 6f, l.StatusRow.Top + 1f, 120f, 16f);
            y += 24f;

            RectangleF gemTitleRow = new RectangleF(contentX, y, contentW, 20f);
            l.EnabledCheck = new RectangleF(gemTitleRow.Left + 6f, gemTitleRow.Top + 4f, 12f, 12f);
            l.EnabledLabel = new RectangleF(gemTitleRow.Left + 24f, gemTitleRow.Top + 2f, 155f, 16f);
            l.SectionToggle = new RectangleF(gemTitleRow.Right - 20f, gemTitleRow.Top + 4f, 14f, 12f);
            y += 24f;

            if (model.AutoGemExpanded)
            {
                RectangleF gemModeRow = new RectangleF(contentX, y, contentW, 22f);
                float btnY = gemModeRow.Top + 3f;
                float modeGap = 4f;
                float modeInnerLeft = gemModeRow.Left + 6f;
                float modeBtnW = (gemModeRow.Width - 12f - modeGap * 4f) / 5f;
                l.ModeAuto = new RectangleF(modeInnerLeft, btnY, modeBtnW, 16f);
                l.ModeFast150 = new RectangleF(l.ModeAuto.Right + modeGap, btnY, modeBtnW, 16f);
                l.ModeHighest = new RectangleF(l.ModeFast150.Right + modeGap, btnY, modeBtnW, 16f);
                l.ModeLowest = new RectangleF(l.ModeHighest.Right + modeGap, btnY, modeBtnW, 16f);
                l.ModeSpecific = new RectangleF(l.ModeLowest.Right + modeGap, btnY, modeBtnW, 16f);
                y += 26f;

                RectangleF gemAnchorRow = new RectangleF(contentX, y, contentW, 22f);
                l.Anchor3 = new RectangleF(gemAnchorRow.Left + 86f, gemAnchorRow.Top + 3f, 58f, 16f);
                l.Anchor4 = new RectangleF(l.Anchor3.Right + 4f, gemAnchorRow.Top + 3f, 58f, 16f);
                y += 26f;

                RectangleF gemDelayRow = new RectangleF(contentX, y, contentW, 22f);
                l.DelayMinus = new RectangleF(gemDelayRow.Left + 86f, gemDelayRow.Top + 3f, 18f, 16f);
                l.DelayValue = new RectangleF(l.DelayMinus.Right + 4f, gemDelayRow.Top + 3f, 56f, 16f);
                l.DelayPlus = new RectangleF(l.DelayValue.Right + 4f, gemDelayRow.Top + 3f, 18f, 16f);
                l.LagBoost = new RectangleF(l.DelayPlus.Right + 4f, gemDelayRow.Top + 3f, 36f, 16f);
                y += 26f;

                RectangleF gemSpecificRow = new RectangleF(contentX, y, contentW, 22f);
                l.SpecificToggle = new RectangleF(gemSpecificRow.Right - 26f, gemSpecificRow.Top + 3f, 18f, 16f);
                l.SpecificSubMode = new RectangleF(l.SpecificToggle.Left - 46f, gemSpecificRow.Top + 3f, 40f, 16f);
                l.SpecificValue = new RectangleF(gemSpecificRow.Left + 86f, gemSpecificRow.Top + 3f, Math.Max(68f, l.SpecificSubMode.Left - (gemSpecificRow.Left + 86f) - 4f), 16f);
                y += 26f;

                if (model.SpecificExpanded)
                {
                    l.SpecificList = new RectangleF(contentX, y, contentW, 148f);
                    l.VisibleGemRows = Math.Max(1, (int)((l.SpecificList.Height - 8f) / 18f));
                    l.MaxGemScroll = Math.Max(0, AutoGemOverlayModel.AutoGemNames.Length - l.VisibleGemRows);
                    if (model.SpecificScroll > l.MaxGemScroll) model.SpecificScroll = l.MaxGemScroll;
                    if (model.SpecificScroll < 0) model.SpecificScroll = 0;
                    l.ScrollUp = new RectangleF(l.SpecificList.Right - 12f, l.SpecificList.Top + 2f, 10f, 14f);
                    l.ScrollDown = new RectangleF(l.SpecificList.Right - 12f, l.SpecificList.Bottom - 16f, 10f, 14f);
                    l.ScrollTrack = new RectangleF(l.SpecificList.Right - 12f, l.ScrollUp.Bottom + 2f, 10f, l.ScrollDown.Top - l.ScrollUp.Bottom - 4f);
                    float knobH = Math.Max(18f, l.ScrollTrack.Height * Math.Min(1f, l.VisibleGemRows / (float)Math.Max(1, AutoGemOverlayModel.AutoGemNames.Length)));
                    float knobTravel = Math.Max(1f, l.ScrollTrack.Height - knobH);
                    float knobY = l.ScrollTrack.Top + ((l.MaxGemScroll <= 0) ? 0f : (model.SpecificScroll / (float)l.MaxGemScroll) * knobTravel);
                    l.ScrollThumb = new RectangleF(l.ScrollTrack.Left + 1f, knobY, l.ScrollTrack.Width - 2f, knobH);
                    float gy = l.SpecificList.Top + 4f;
                    for (int gi = model.SpecificScroll; gi < AutoGemOverlayModel.AutoGemNames.Length && gy + 16f <= l.SpecificList.Bottom - 4f; gi++)
                    {
                        l.GemOptions.Add(new GemOptionLayout(gi, new RectangleF(l.SpecificList.Left + 4f, gy, l.SpecificList.Width - 20f, 16f)));
                        gy += 18f;
                    }
                    y += 156f;
                }
            }

            l.DebugCheck = new RectangleF(contentX + 6f, y + 4f, 12f, 12f);
            l.DebugLabel = new RectangleF(contentX + 24f, y + 2f, 145f, 16f);
            y += 24f;

            return l;
        }
    }

    internal sealed class GemOptionLayout
    {
        public readonly int Index;
        public readonly RectangleF Rect;
        public GemOptionLayout(int index, RectangleF rect) { Index = index; Rect = rect; }
    }

    internal sealed class AutoGemOverlayController
    {
        private bool _dirty;
        private string _lastSpecificName = string.Empty;
        private int _lastSpecificScroll;

        public void ResetSnapshot(AutoGemOverlayModel model)
        {
            _lastSpecificName = s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty;
            _lastSpecificScroll = model.SpecificScroll;
            _dirty = false;
        }

        public void MarkDirty() { _dirty = true; }
        public bool ConsumeDirty() { bool value = _dirty; _dirty = false; return value; }

        public bool ExecuteCommand(IController hud, AutoGemOverlayModel model, AutoGemOverlayLayout layout, AutoGemOverlayCommand command, int gemIndex, float mouseX, float mouseY)
        {
            bool changed = false;
            switch (command)
            {
                case AutoGemOverlayCommand.ToggleMenu:
                    model.Visible = !model.Visible;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.HideMenu:
                    model.Visible = false;
                    model.SpecificExpanded = false;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleMove:
                    model.EditMode = !model.EditMode;
                    if (!model.EditMode)
                    {
                        model.DraggingPanel = false;
                        model.DraggingDot = false;
                    }
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleMenuButtonVisible:
                    model.ShowMenuDot = !model.ShowMenuDot;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleNoClickBg:
                    model.NoClickBackground = !model.NoClickBackground;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleEnabled:
                    s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled = !s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleSection:
                    model.AutoGemExpanded = !model.AutoGemExpanded;
                    if (!model.AutoGemExpanded) model.SpecificExpanded = false;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.SetModeAuto:
                    changed = SetMode(model, 0);
                    break;

                case AutoGemOverlayCommand.SetModeFast150:
                    changed = SetMode(model, 3);
                    break;

                case AutoGemOverlayCommand.SetModeHighest:
                    changed = SetMode(model, 2);
                    break;

                case AutoGemOverlayCommand.SetModeLowest:
                    changed = SetMode(model, 1);
                    break;

                case AutoGemOverlayCommand.SetModeSpecific:
                    changed = SetMode(model, 4);
                    break;

                case AutoGemOverlayCommand.SetAnchor3:
                    if (s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining != 3) changed = true;
                    s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = 3;
                    break;

                case AutoGemOverlayCommand.SetAnchor4:
                    if (s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining != 4) changed = true;
                    s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = 4;
                    break;

                case AutoGemOverlayCommand.DelayMinus:
                    {
                        int next = s7o_AutoGemUpgradeState.ClampTPDelayMs(s7o_AutoGemUpgradeState.AutoGemTPDelayMs - s7o_AutoGemUpgradeState.AutoGemTPDelayStep);
                        if (next != s7o_AutoGemUpgradeState.AutoGemTPDelayMs)
                        {
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMs = next;
                            changed = true;
                        }
                        break;
                    }

                case AutoGemOverlayCommand.DelayPlus:
                    {
                        int next = s7o_AutoGemUpgradeState.ClampTPDelayMs(s7o_AutoGemUpgradeState.AutoGemTPDelayMs + s7o_AutoGemUpgradeState.AutoGemTPDelayStep);
                        if (next != s7o_AutoGemUpgradeState.AutoGemTPDelayMs)
                        {
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMs = next;
                            changed = true;
                        }
                        break;
                    }

                case AutoGemOverlayCommand.ToggleLag:
                    s7o_AutoGemUpgradeState.AutoGemTPLagBoost = !s7o_AutoGemUpgradeState.AutoGemTPLagBoost;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleSpecificSubMode:
                    s7o_AutoGemUpgradeState.AutoGemSpecificSubMode = s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1 ? 0 : 1;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleSpecificDropdown:
                    model.SpecificExpanded = !model.SpecificExpanded;
                    changed = true;
                    break;

                case AutoGemOverlayCommand.ToggleDebug:
                    SetDebug(!s7o_AutoGemUpgradeState.DebugEnabled);
                    changed = true;
                    break;

                case AutoGemOverlayCommand.SelectSpecificGem:
                    if (gemIndex >= 0 && gemIndex < AutoGemOverlayModel.AutoGemNames.Length)
                    {
                        s7o_AutoGemUpgradeState.AutoGemSpecificName = AutoGemOverlayModel.AutoGemNames[gemIndex];
                        s7o_AutoGemUpgradeState.AutoGemMode = 4;
                        model.SpecificExpanded = false;
                        changed = true;
                    }
                    break;

                case AutoGemOverlayCommand.ScrollSpecificUp:
                    {
                        int next = Math.Max(0, model.SpecificScroll - 3);
                        if (next != model.SpecificScroll)
                        {
                            model.SpecificScroll = next;
                            changed = true;
                        }
                        break;
                    }

                case AutoGemOverlayCommand.ScrollSpecificDown:
                    {
                        int maxScroll = Math.Max(0, layout.MaxGemScroll);
                        int next = Math.Min(maxScroll, model.SpecificScroll + 3);
                        if (next != model.SpecificScroll)
                        {
                            model.SpecificScroll = next;
                            changed = true;
                        }
                        break;
                    }

                case AutoGemOverlayCommand.BeginSpecificScrollDrag:
                    model.DraggingSpecificScroll = true;
                    if (UpdateSpecificScrollFromCursor(hud, model, layout)) changed = true;
                    break;

                case AutoGemOverlayCommand.BeginDotDrag:
                    if (model.EditMode && model.ShowMenuDot)
                    {
                        model.DraggingDot = true;
                        model.DraggingPanel = false;
                        model.DragOffsetX = mouseX - model.DotRect.Left;
                        model.DragOffsetY = mouseY - model.DotRect.Top;
                    }
                    break;

                case AutoGemOverlayCommand.BeginPanelDrag:
                    if (model.EditMode)
                    {
                        model.DraggingPanel = true;
                        model.DraggingDot = false;
                        model.DragOffsetX = mouseX - model.PanelRect.Left;
                        model.DragOffsetY = mouseY - model.PanelRect.Top;
                    }
                    break;
            }

            string curName = s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty;
            if (!string.Equals(curName, _lastSpecificName, StringComparison.Ordinal) || model.SpecificScroll != _lastSpecificScroll)
            {
                _lastSpecificName = curName;
                _lastSpecificScroll = model.SpecificScroll;
                changed = true;
            }

            if (changed) _dirty = true;
            return changed;
        }

        public void UpdateContinuous(IController hud, AutoGemOverlayModel model, AutoGemOverlayLayout layout, bool leftDown)
        {
            if (HandleDragging(hud, model, layout, leftDown)) _dirty = true;

            if (!leftDown)
            {
                if (model.DraggingSpecificScroll)
                {
                    model.DraggingSpecificScroll = false;
                    _dirty = true;
                }
                return;
            }

            if (model.DraggingSpecificScroll && UpdateSpecificScrollFromCursor(hud, model, layout)) _dirty = true;
        }

        private bool HandleDragging(IController hud, AutoGemOverlayModel model, AutoGemOverlayLayout layout, bool leftDown)
        {
            bool changed = false;
            if (!leftDown)
            {
                if (model.DraggingPanel || model.DraggingDot) changed = true;
                model.DraggingPanel = false;
                model.DraggingDot = false;
                return changed;
            }

            if (!model.EditMode) return false;

            float cx = 0f, cy = 0f;
            try { cx = hud.Window.CursorX; cy = hud.Window.CursorY; } catch { return false; }

            if (model.DraggingDot)
            {
                float nx = cx - model.DragOffsetX;
                float ny = cy - model.DragOffsetY;
                if (Math.Abs(nx - model.DotRect.X) > 0.1f || Math.Abs(ny - model.DotRect.Y) > 0.1f)
                {
                    model.DotRect.X = nx;
                    model.DotRect.Y = ny;
                    model.NormalizeRects(hud);
                }
            }
            if (model.DraggingPanel)
            {
                float nx = cx - model.DragOffsetX;
                float ny = cy - model.DragOffsetY;
                if (Math.Abs(nx - model.PanelRect.X) > 0.1f || Math.Abs(ny - model.PanelRect.Y) > 0.1f)
                {
                    model.PanelRect.X = nx;
                    model.PanelRect.Y = ny;
                    model.NormalizeRects(hud);
                }
            }
            return changed;
        }

        private bool UpdateSpecificScrollFromCursor(IController hud, AutoGemOverlayModel model, AutoGemOverlayLayout layout)
        {
            int maxScroll = Math.Max(0, layout.MaxGemScroll);
            if (maxScroll <= 0) return false;
            float cy;
            try { cy = hud.Window.CursorY; } catch { return false; }
            float pct = (cy - layout.ScrollTrack.Top) / Math.Max(1f, layout.ScrollTrack.Height);
            if (pct < 0f) pct = 0f;
            if (pct > 1f) pct = 1f;
            int next = (int)Math.Round(maxScroll * pct);
            if (next == model.SpecificScroll) return false;
            model.SpecificScroll = next;
            return true;
        }

        private static bool SetMode(AutoGemOverlayModel model, int mode)
        {
            bool changed = s7o_AutoGemUpgradeState.AutoGemMode != mode;
            s7o_AutoGemUpgradeState.AutoGemMode = mode;
            if (mode != 4 && model.SpecificExpanded)
            {
                model.SpecificExpanded = false;
                changed = true;
            }
            return changed;
        }

        private static void SetDebug(bool enabled)
        {
            s7o_AutoGemUpgradeState.DebugEnabled = enabled;
            s7o_AutoGemUpgradeState.DebugFileEnabled = enabled;
            s7o_AutoGemUpgradeState.DebugLevel = enabled
                ? s7o_AutoGemUpgradeState.DebugLevelState
                : s7o_AutoGemUpgradeState.DebugLevelOff;

            if (!enabled)
            {
                s7o_AutoGemUpgradeState.DebugLogPath = string.Empty;
                return;
            }

            try
            {
                s7o_AutoGemUpgradeState.DebugLogPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "plugins",
                    "s7o",
                    s7o_AutoGemUpgradeState.DebugLogFileName);
            }
            catch
            {
                s7o_AutoGemUpgradeState.DebugLogPath = s7o_AutoGemUpgradeState.DebugLogFileName;
            }
        }
    }

    internal sealed class AutoGemOverlayRenderer
    {
        private bool _ready;
        private IBrush _bShadow, _bFrame, _bFrameBorder, _bInner, _bTitle, _bPane, _bPaneBorder;
        private IBrush _bStatus, _bRow, _bRowAlt, _bGemListBg, _bGemListBorder;
        private IBrush _bBtnNormal, _bBtnActive, _bBtnDanger, _bBtnGloss, _bBtnEdge;
        private IBrush _bChkBg, _bChkEdge, _bChkFill, _bChkShadow, _bChkGloss;
        private IBrush _bScrollTrack, _bScrollThumb, _bScrollButton;
        private IBrush _bInfoFill, _bInfoBorder, _bInfoPanelBg, _bInfoPanelEdge, _bJourneyMaskFill, _bJourneyMaskEdge, _bEditDash, _bDotRim, _bDotSpec, _bDotHot;
        private readonly IBrush[] _dotFill = new IBrush[8];
        private readonly IBrush[] _dotShadow = new IBrush[8];
        private IFont _fTitle, _fLabel, _fSection, _fText, _fBtnCompact, _fBtnNormal, _fLogoSilver, _fLogoGreen, _fInfoSub;

        private static readonly Color[] _picker8 = new Color[]
        {
            Color.FromArgb(210, 40, 40),
            Color.FromArgb(220, 130, 30),
            Color.FromArgb(220, 200, 40),
            Color.FromArgb(70, 200, 70),
            Color.FromArgb(50, 110, 225),
            Color.FromArgb(140, 70, 215),
            Color.FromArgb(225, 225, 225),
            Color.FromArgb(18, 18, 18),
        };

        public void EnsureResources(IController hud)
        {
            if (_ready) return;
            _ready = true;
            _bShadow      = hud.Render.CreateBrush(165, 0, 0, 0, 0);
            _bFrame       = hud.Render.CreateBrush(230, 36, 42, 46, 0);
            _bFrameBorder = hud.Render.CreateBrush(255, 48, 180, 60, 1.4f);
            _bInner       = hud.Render.CreateBrush(215, 42, 48, 54, 0);
            _bTitle       = hud.Render.CreateBrush(235, 18, 58, 20, 0);
            _bPane        = hud.Render.CreateBrush(170, 35, 41, 46, 0);
            _bPaneBorder  = hud.Render.CreateBrush(140, 120, 126, 132, 1.0f);
            _bStatus = hud.Render.CreateBrush(215, 54, 61, 68, 0);
            _bRow    = hud.Render.CreateBrush(205, 47, 54, 61, 0);
            _bRowAlt = hud.Render.CreateBrush(225, 39, 46, 53, 0);
            _bGemListBg     = hud.Render.CreateBrush(90, 20, 26, 30, 0);
            _bGemListBorder = hud.Render.CreateBrush(140, 120, 126, 132, 1.0f);
            _bScrollTrack = hud.Render.CreateBrush(100, 30, 38, 44, 0);
            _bScrollThumb = hud.Render.CreateBrush(210, 70, 195, 85, 0);
            _bScrollButton = hud.Render.CreateBrush(235, 60, 66, 72, 0);
            _bChkBg     = hud.Render.CreateBrush(235, 58, 64, 70, 0);
            _bChkEdge   = hud.Render.CreateBrush(220, 28, 32, 36, 1f);
            _bChkFill   = hud.Render.CreateBrush(245, 62, 185, 52, 0);
            _bChkShadow = hud.Render.CreateBrush(130, 22, 88, 18, 0);
            _bChkGloss  = hud.Render.CreateBrush(140, 170, 255, 150, 0);
            _bBtnNormal = hud.Render.CreateBrush(235, 60, 66, 72, 0);
            _bBtnActive = hud.Render.CreateBrush(235, 78, 195, 72, 0);
            _bBtnDanger = hud.Render.CreateBrush(235, 145, 58, 58, 0);
            _bBtnGloss  = hud.Render.CreateBrush(32, 235, 245, 245, 0);
            _bBtnEdge   = hud.Render.CreateBrush(230, 18, 22, 24, 1f);
            _bInfoFill   = hud.Render.CreateBrush(70, 35, 155, 45, 0);
            _bInfoBorder = hud.Render.CreateBrush(230, 50, 200, 60, 1.5f);
            _bInfoPanelBg = hud.Render.CreateBrush(238, 24, 30, 35, 0);
            _bInfoPanelEdge = hud.Render.CreateBrush(235, 58, 200, 72, 1.3f);
            _bJourneyMaskFill = hud.Render.CreateBrush(70, 255, 90, 70, 0);
            _bJourneyMaskEdge = hud.Render.CreateBrush(255, 255, 90, 70, 2.4f);
            _bEditDash   = hud.Render.CreateBrush(220, 140, 220, 120, 1f);
            _bDotRim     = hud.Render.CreateBrush(210, 8, 10, 12, 1.7f);
            _bDotSpec    = hud.Render.CreateBrush(130, 255, 255, 255, 0);
            _bDotHot     = hud.Render.CreateBrush(210, 255, 255, 255, 0);
            for (int i = 0; i < 8; i++)
            {
                Color c = _picker8[i];
                Color d = Darken(c, 0.45f);
                _dotFill[i] = hud.Render.CreateBrush(245, c.R, c.G, c.B, 0);
                _dotShadow[i] = hud.Render.CreateBrush(170, d.R, d.G, d.B, 0);
            }
            _fTitle   = hud.Render.CreateFont("tahoma", 9.2f, 255, 255, 255, 255, true, false, 130, 0, 0, 0, true);
            _fLabel   = hud.Render.CreateFont("tahoma", 7.2f, 255, 255, 255, 255, false, false, 95, 0, 0, 0, true);
            _fSection = hud.Render.CreateFont("tahoma", 8.0f, 255, 255, 255, 255, true, false, 110, 0, 0, 0, true);
            _fText    = hud.Render.CreateFont("tahoma", 6.9f, 255, 220, 225, 228, false, false, 90, 0, 0, 0, true);
            _fBtnCompact = hud.Render.CreateFont("tahoma", 6.0f, 255, 255, 255, 255, false, false, 100, 0, 0, 0, true);
            _fBtnNormal  = hud.Render.CreateFont("tahoma", 6.6f, 255, 255, 255, 255, false, false, 100, 0, 0, 0, true);
            _fLogoSilver = hud.Render.CreateFont("tahoma", 8.0f, 255, 190, 195, 208, true, false, 110, 0, 0, 0, true);
            _fLogoGreen  = hud.Render.CreateFont("tahoma", 10.5f, 255, 50, 230, 80, true, false, 150, 0, 0, 0, true);
            _fInfoSub    = hud.Render.CreateFont("tahoma", 6.5f, 200, 140, 220, 120, false, false, 90, 0, 0, 0, true);
        }

        public void Draw(IController hud, AutoGemOverlayModel model, AutoGemOverlayLayout layout, string hotkeyLabel, bool infoPopupVisible, bool journeyMaskVisible)
        {
            if (model.ShowMenuDot) DrawOpenDot(layout.Indicator, model);
            if (!model.Visible) return;

            // The Journey X mask belongs to the Journey background layer, so draw it
            // before the AutoGem panel and info popup. The panel still wins visually
            // if the user moves it over the mask.
            if (journeyMaskVisible) DrawJourneyCloseMask(layout.JourneyCloseMask);
            DrawPanel(model, layout, hotkeyLabel);
            if (infoPopupVisible) DrawInfoPopup(layout, hotkeyLabel);
        }

        private void DrawPanel(AutoGemOverlayModel model, AutoGemOverlayLayout layout, string hotkeyLabel)
        {
            RectangleF rect = layout.Panel;
            _bShadow.DrawRectangle(rect.Left + 5f, rect.Top + 5f, rect.Width, rect.Height);
            _bFrame.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            _bFrameBorder.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            _bInner.DrawRectangle(rect.Left + 6f, rect.Top + 6f, rect.Width - 12f, rect.Height - 12f);
            _bTitle.DrawRectangle(layout.TitleBar.Left, layout.TitleBar.Top, layout.TitleBar.Width, layout.TitleBar.Height);
            _bFrameBorder.DrawRectangle(layout.TitleBar.Left, layout.TitleBar.Top, layout.TitleBar.Width, layout.TitleBar.Height);

            _fTitle.DrawText("Auto Gem Upgrade", rect.Left + 14f, rect.Top + 14f);
            _fLabel.DrawText("Menu Hotkey =", Math.Max(rect.Left + 188f, layout.HotkeyButton.Left - 92f), rect.Top + 15f);
            DrawGlossButton(layout.HotkeyButton, hotkeyLabel, false, false, true);
            DrawGlossButton(layout.MenuDotButton, "MENU BUTTON", model.ShowMenuDot, false, false);
            DrawGlossButton(layout.EditButton, "MOVE", model.EditMode, false, true);
            DrawGlossButton(layout.HideButton, "HIDE", false, false, true);

            RectangleF mainRect = layout.MainPane;
            _bPane.DrawRectangle(mainRect.Left, mainRect.Top, mainRect.Width, mainRect.Height);
            _bPaneBorder.DrawRectangle(mainRect.Left, mainRect.Top, mainRect.Width, mainRect.Height);
            _fSection.DrawText("Auto Gem Controls", mainRect.Left + 12f, mainRect.Top + 10f);
            DrawInfoCircle(layout.InfoButton);
            DrawStaticLogo(mainRect.Right - 36f, mainRect.Top + 10f);
            if (model.EditMode)
            {
                DrawRect(layout.Panel.Left + 2f, layout.Panel.Top + 2f, layout.Panel.Width - 4f, layout.Panel.Height - 4f, _bEditDash);
                _fInfoSub.DrawText("DRAG TITLE OR MENU BUTTON TO MOVE", rect.Left + 10f, rect.Bottom - 16f);
            }

            float contentX = mainRect.Left + 12f;
            float contentW = mainRect.Width - 24f;
            float y = mainRect.Top + 34f;

            _bStatus.DrawRectangle(layout.StatusRow.Left, layout.StatusRow.Top, layout.StatusRow.Width, layout.StatusRow.Height);
            string modeText = ModeName(s7o_AutoGemUpgradeState.AutoGemMode);
            string tpTimingLabel = AutoGemAnchorText(s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining()) + "+" + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture) + "ms";
            _fText.DrawText(TrimToWidth("Status: " + (s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled ? "Enabled" : "Disabled") + "   Mode: " + modeText + "   TP: " + tpTimingLabel + (s7o_AutoGemUpgradeState.AutoGemTPLagBoost ? "   LAG" : ""), 70), contentX + 6f, y + 3f);
            DrawSquareCheckPassive(layout.NoClickCheck, model.NoClickBackground);
            _fText.DrawText("No-Click Background", layout.NoClickLabel.Left, layout.NoClickLabel.Top + 2f);
            y += 24f;

            RectangleF gemTitleRow = new RectangleF(contentX, y, contentW, 20f);
            _bRowAlt.DrawRectangle(gemTitleRow.Left, gemTitleRow.Top, gemTitleRow.Width, gemTitleRow.Height);
            DrawSquareCheckPassive(layout.EnabledCheck, s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled);
            _fSection.DrawText("Auto Gem Upgrade", gemTitleRow.Left + 24f, gemTitleRow.Top + 2f);
            DrawGlossButton(layout.SectionToggle, model.AutoGemExpanded ? "-" : "+", s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled && model.AutoGemExpanded, false, true);
            y += 24f;

            if (model.AutoGemExpanded)
            {
                RectangleF modeRow = new RectangleF(contentX, y, contentW, 22f);
                _bRow.DrawRectangle(modeRow.Left, modeRow.Top, modeRow.Width, modeRow.Height);
                DrawGlossButton(layout.ModeAuto, "AUTO", s7o_AutoGemUpgradeState.AutoGemMode == 0, false, true);
                DrawGlossButton(layout.ModeFast150, "FAST 150", s7o_AutoGemUpgradeState.AutoGemMode == 3, false, true);
                DrawGlossButton(layout.ModeHighest, "HIGHEST", s7o_AutoGemUpgradeState.AutoGemMode == 2, false, true);
                DrawGlossButton(layout.ModeLowest, "LOWEST", s7o_AutoGemUpgradeState.AutoGemMode == 1, false, true);
                DrawGlossButton(layout.ModeSpecific, "SPECIFIC", s7o_AutoGemUpgradeState.AutoGemMode == 4, false, true);
                y += 26f;

                RectangleF anchorRow = new RectangleF(contentX, y, contentW, 22f);
                _bRowAlt.DrawRectangle(anchorRow.Left, anchorRow.Top, anchorRow.Width, anchorRow.Height);
                _fText.DrawText("TP Anchor", anchorRow.Left + 8f, anchorRow.Top + 4f);
                int tpAnchor = s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining();
                DrawGlossButton(layout.Anchor3, "3RD GEM", tpAnchor == 3, false, true);
                DrawGlossButton(layout.Anchor4, "4TH GEM", tpAnchor == 4, false, true);
                _fText.DrawText("Timer starts when that upgrade begins", layout.Anchor4.Right + 10f, anchorRow.Top + 4f);
                y += 26f;

                RectangleF delayRow = new RectangleF(contentX, y, contentW, 22f);
                _bRow.DrawRectangle(delayRow.Left, delayRow.Top, delayRow.Width, delayRow.Height);
                _fText.DrawText("TP Delay", delayRow.Left + 8f, delayRow.Top + 4f);
                int tpDelay = s7o_AutoGemUpgradeState.AutoGemTPDelayMs;
                DrawGlossButton(layout.DelayMinus, "-", false, false, true);
                DrawGlossButton(layout.DelayValue, tpDelay.ToString(CultureInfo.InvariantCulture) + "ms", false, false, true);
                DrawGlossButton(layout.DelayPlus, "+", false, false, true);
                DrawGlossButton(layout.LagBoost, "LAG", s7o_AutoGemUpgradeState.AutoGemTPLagBoost, false, true);
                _fText.DrawText("0-1500ms after anchor; default = 3RD + 1000ms", layout.LagBoost.Right + 10f, delayRow.Top + 4f);
                y += 26f;

                RectangleF specificRow = new RectangleF(contentX, y, contentW, 22f);
                _bRow.DrawRectangle(specificRow.Left, specificRow.Top, specificRow.Width, specificRow.Height);
                _fText.DrawText("Specific Gem", specificRow.Left + 8f, specificRow.Top + 4f);
                DrawGlossButton(layout.SpecificValue, TrimToWidth(s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty, 28), false, false, true);
                DrawGlossButton(layout.SpecificSubMode, s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1 ? "HIGH" : "AUTO", true, false, true);
                DrawGlossButton(layout.SpecificToggle, model.SpecificExpanded ? "-" : "+", model.SpecificExpanded, false, true);
                y += 26f;

                if (model.SpecificExpanded)
                {
                    _bGemListBg.DrawRectangle(layout.SpecificList.Left, layout.SpecificList.Top, layout.SpecificList.Width, layout.SpecificList.Height);
                    _bGemListBorder.DrawRectangle(layout.SpecificList.Left, layout.SpecificList.Top, layout.SpecificList.Width, layout.SpecificList.Height);
                    for (int i = 0; i < layout.GemOptions.Count; i++)
                    {
                        GemOptionLayout opt = layout.GemOptions[i];
                        string name = AutoGemOverlayModel.AutoGemNames[opt.Index];
                        bool selected = string.Equals(name, s7o_AutoGemUpgradeState.AutoGemSpecificName, StringComparison.OrdinalIgnoreCase);
                        DrawGlossButton(opt.Rect, TrimToWidth(name, 40), selected, false, true);
                    }
                    DrawScrollBar(layout);
                    y = layout.SpecificList.Bottom + 8f;
                }
            }

            RectangleF debugRow = new RectangleF(contentX, y, contentW, 22f);
            _bRow.DrawRectangle(debugRow.Left, debugRow.Top, debugRow.Width, debugRow.Height);
            DrawSquareCheckPassive(layout.DebugCheck, s7o_AutoGemUpgradeState.DebugEnabled);
            _fText.DrawText("Debug Logging", layout.DebugLabel.Left, layout.DebugLabel.Top + 2f);
            y += 24f;


        }

        private void DrawInfoPopup(AutoGemOverlayLayout layout, string hotkeyLabel)
        {
            RectangleF r = layout.InfoPopup;
            _bShadow.DrawRectangle(r.Left + 4f, r.Top + 4f, r.Width, r.Height);
            _bInfoPanelBg.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bInfoPanelEdge.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _fSection.DrawText("Auto Gem Upgrade Info", r.Left + 10f, r.Top + 8f);
            float y = r.Top + 30f;
            _fText.DrawText("Menu hotkey [" + hotkeyLabel + "] opens or closes this menu.", r.Left + 12f, y); y += 18f;
            _fText.DrawText("No-Click Background opens Journey behind this overlay to absorb game clicks.", r.Left + 12f, y); y += 18f;
            _fText.DrawText("MOVE lets you drag the title bar or the small menu button.", r.Left + 12f, y); y += 18f;
            _fText.DrawText("Urshi upgrade behavior is controlled by the Auto Gem Upgrade section.", r.Left + 12f, y); y += 18f;
            _fInfoSub.DrawText("Click the green info icon again, Escape, or outside this popup to close it.", r.Left + 12f, y + 3f);
        }

        private void DrawJourneyCloseMask(RectangleF r)
        {
            if (r.Width <= 1f || r.Height <= 1f) return;

            // Draw a visible square bound-close zone over the Journey X button.
            // This is intentionally obvious: it confirms where the overlay is
            // watching for the user's Journey close click.
            _bJourneyMaskFill.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bJourneyMaskEdge.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float pad = Math.Max(2f, Math.Min(r.Width, r.Height) * 0.18f);
            _bJourneyMaskEdge.DrawRectangle(r.Left + pad, r.Top + pad, Math.Max(1f, r.Width - pad * 2f), Math.Max(1f, r.Height - pad * 2f));
        }

        private void DrawInfoCircle(RectangleF r)
        {
            float cx = r.Left + r.Width * 0.5f;
            float cy = r.Top + r.Height * 0.5f;
            float rad = Math.Min(r.Width, r.Height) * 0.5f;
            _bInfoFill.DrawEllipse(cx, cy, rad, rad);
            _bInfoBorder.DrawEllipse(cx, cy, rad, rad);
            _fSection.DrawText("i", cx - 1.8f, cy - rad + 0.5f);
        }

        private void DrawStaticLogo(float x, float y)
        {
            _fLogoSilver.DrawText("s", x, y);
            _fLogoGreen.DrawText("7", x + 7f, y - 3f);
            _fLogoSilver.DrawText("o", x + 14f, y);
        }

        private void DrawOpenDot(RectangleF rect, AutoGemOverlayModel model)
        {
            int activeSize = model.Visible ? model.DotOpenSize : model.DotSize;
            int activeIdx = model.Visible ? model.DotOpenColorIdx : model.DotColorIdx;
            if (activeIdx < 0) activeIdx = 0;
            if (activeIdx > 7) activeIdx = 7;
            float sz = rect.Width * (activeSize / 5.0f);
            float cx = rect.Left + rect.Width * 0.5f;
            float cy = rect.Top + rect.Height * 0.5f;
            float rx = sz * 0.5f;
            float ry = rx * 0.90f;
            _bDotRim.DrawEllipse(cx, cy, rx, ry);
            _dotFill[activeIdx].DrawEllipse(cx, cy, rx * 0.91f, ry * 0.91f);
            _dotShadow[activeIdx].DrawEllipse(cx, cy + ry * 0.28f, rx * 0.72f, ry * 0.52f);
            _bDotSpec.DrawEllipse(cx - rx * 0.24f, cy - ry * 0.24f, rx * 0.54f, ry * 0.37f);
            _bDotHot.DrawEllipse(cx - rx * 0.30f, cy - ry * 0.30f, rx * 0.19f, ry * 0.13f);
            if (model.EditMode) DrawRect(rect.Left - 6f, rect.Top - 6f, rect.Width + 12f, rect.Height + 12f, _bEditDash);
        }

        private void DrawSquareCheckPassive(RectangleF rect, bool value)
        {
            _bChkBg.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            _bChkEdge.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            if (!value) return;
            float ix = rect.Left + 2f, iy = rect.Top + 2f, iw = rect.Width - 4f, ih = rect.Height - 4f;
            _bChkFill.DrawRectangle(ix, iy, iw, ih);
            _bChkShadow.DrawRectangle(ix, iy + ih * 0.52f, iw, ih * 0.48f);
            _bChkGloss.DrawRectangle(ix, iy, iw, ih * 0.44f);
        }

        private void DrawGlossButton(RectangleF rect, string text, bool active, bool danger, bool compact)
        {
            IBrush body = active ? _bBtnActive : danger ? _bBtnDanger : _bBtnNormal;
            IFont font = compact ? _fBtnCompact : _fBtnNormal;
            body.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            _bBtnGloss.DrawRectangle(rect.Left + 1f, rect.Top + 1f, Math.Max(1f, rect.Width - 2f), rect.Height * 0.45f);
            _bBtnEdge.DrawRectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            if (string.IsNullOrEmpty(text)) return;
            DrawCenteredText(font, text, rect);
        }

        private void DrawCenteredText(IFont font, string text, RectangleF rect)
        {
            try
            {
                var tl = font.GetTextLayout(text);
                font.DrawText(tl, rect.Left + (rect.Width - tl.Metrics.Width) / 2f, rect.Top + (rect.Height - tl.Metrics.Height) / 2f - 1f);
            }
            catch
            {
                float tx = rect.Left + Math.Max(4f, (rect.Width * 0.5f) - (text.Length * 2.4f));
                font.DrawText(text, tx, rect.Top + 1f);
            }
        }

        private void DrawScrollBar(AutoGemOverlayLayout layout)
        {
            if (layout.SpecificList.Width <= 0f) return;
            _bScrollTrack.DrawRectangle(layout.ScrollTrack.Left, layout.ScrollTrack.Top, layout.ScrollTrack.Width, layout.ScrollTrack.Height);
            _bScrollButton.DrawRectangle(layout.ScrollUp.Left, layout.ScrollUp.Top, layout.ScrollUp.Width, layout.ScrollUp.Height);
            _bScrollButton.DrawRectangle(layout.ScrollDown.Left, layout.ScrollDown.Top, layout.ScrollDown.Width, layout.ScrollDown.Height);
            _bBtnEdge.DrawRectangle(layout.ScrollUp.Left, layout.ScrollUp.Top, layout.ScrollUp.Width, layout.ScrollUp.Height);
            _bBtnEdge.DrawRectangle(layout.ScrollDown.Left, layout.ScrollDown.Top, layout.ScrollDown.Width, layout.ScrollDown.Height);
            _fBtnCompact.DrawText("^", layout.ScrollUp.Left + 2f, layout.ScrollUp.Top - 1f);
            _fBtnCompact.DrawText("v", layout.ScrollDown.Left + 2f, layout.ScrollDown.Top - 1f);
            _bScrollThumb.DrawRectangle(layout.ScrollThumb.Left, layout.ScrollThumb.Top, layout.ScrollThumb.Width, layout.ScrollThumb.Height);
            _bBtnEdge.DrawRectangle(layout.ScrollThumb.Left, layout.ScrollThumb.Top, layout.ScrollThumb.Width, layout.ScrollThumb.Height);
        }

        private void DrawRect(float x, float y, float w, float h, IBrush b)
        {
            b.DrawRectangle(x, y, w, 1f); b.DrawRectangle(x, y + h - 1f, w, 1f); b.DrawRectangle(x, y, 1f, h); b.DrawRectangle(x + w - 1f, y, 1f, h);
        }

        private static string ModeName(int mode)
        {
            if (mode == 0) return "AUTO";
            if (mode == 3) return "FAST 150";
            if (mode == 2) return "HIGHEST";
            if (mode == 4) return "SPECIFIC";
            return "LOWEST";
        }

        private static string AutoGemAnchorText(int remaining)
        {
            return remaining == 4 ? "4TH" : "3RD";
        }

        private static string TrimToWidth(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxChars) return text;
            if (maxChars <= 3) return text.Substring(0, Math.Max(0, maxChars));
            return text.Substring(0, maxChars - 3) + "...";
        }

        private static Color Darken(Color c, float factor)
        {
            if (factor < 0f) factor = 0f;
            if (factor > 1f) factor = 1f;
            return Color.FromArgb((int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));
        }
    }

    internal static class FreeHudInput
    {
        public const ushort VirtualKeyForTownPortal = 0x54; // T: FreeHUD uses direct virtual-key input for Town Portal.
        public const ushort VirtualKeyForInteract = 0x00;
        public const ushort VK_SHIFT = 0x10;
        public const ushort VK_J = 0x4A;
        public const ushort VK_ESCAPE = 0x1B;
        public const ushort VK_SPACE = 0x20;
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;

        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static void MouseMove(int x, int y) { SetCursorPos(x, y); }
        public static void MouseDown(MouseButtons button) { if (button == MouseButtons.Left) SendMouse(MOUSEEVENTF_LEFTDOWN, 0); }
        public static void MouseUp(MouseButtons button) { if (button == MouseButtons.Left) SendMouse(MOUSEEVENTF_LEFTUP, 0); }
        public static void ScrollDown(int clicks) { SendMouse(MOUSEEVENTF_WHEEL, unchecked((uint)(-WHEEL_DELTA * Math.Max(1, clicks)))); }
        public static void ScrollUp(int clicks) { SendMouse(MOUSEEVENTF_WHEEL, (uint)(WHEEL_DELTA * Math.Max(1, clicks))); }
        public static void ClickUiElement(MouseButtons button, IUiElement element)
        {
            if (element == null) return;
            var r = element.Rectangle;
            MouseMove((int)Math.Round(r.Left + r.Width * 0.5f), (int)Math.Round(r.Top + r.Height * 0.5f));
            MouseDown(button); Thread.Sleep(10); MouseUp(button);
        }
        public static void SendVirtualKey(ushort vk)
        {
            if (vk == 0) return;
            SendKeyboard(vk, false); Thread.Sleep(10); SendKeyboard(vk, true);
        }
        public static void SendKeyDown(ushort vk)
        {
            if (vk == 0) return;
            SendKeyboard(vk, false);
        }
        public static void SendKeyUp(ushort vk)
        {
            if (vk == 0) return;
            SendKeyboard(vk, true);
        }
        public static void SendKeyCombo(ushort modifierVk, ushort keyVk)
        {
            if (modifierVk == 0 || keyVk == 0) return;
            SendKeyDown(modifierVk);
            Thread.Sleep(10);
            SendVirtualKey(keyVk);
            Thread.Sleep(10);
            SendKeyUp(modifierVk);
        }
        public static void SendEscape() { SendVirtualKey(VK_ESCAPE); }
        public static void SendSpace() { SendVirtualKey(VK_SPACE); }
        public static void SendShiftJ() { SendKeyCombo(VK_SHIFT, VK_J); }
        private static void SendMouse(uint flags, uint mouseData)
        {
            var input = new[] { new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = mouseData, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } } } };
            SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
        }
        private static void SendKeyboard(ushort vk, bool up)
        {
            var input = new[] { new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = up ? KEYEVENTF_KEYUP : 0, time = 0, dwExtraInfo = IntPtr.Zero } } } };
            SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
        }
    }

public class s7o_AutoGemUpgradeNavigator : BasePlugin, IAfterCollectHandler, IInGameTopPainter
    {
        public bool AutoStartEnabled { get; set; } = true;

        public bool PreferHighestNonMaxFirst { get; set; } = false;

        public bool AutoPercentMode { get; set; } = false;

        public string ForcedGemNameExact { get; set; } = string.Empty;

        public bool FastFallbackMode { get; set; } = false;

        // Delay between successive list cell clicks in milliseconds.
        public int CellClickDelayMs { get; set; } = 2;
        // Delay after performing a validation click on a cell before reading the result.
        public int CellValidateDelayMs { get; set; } = 6;
        // Delay between scroll actions.
        public int ScrollClickDelayMs { get; set; } = 5;
        // Delay to allow the list to settle after a scroll before proceeding.
        public int ScrollSettleDelayMs { get; set; } = 10;
        private const int MaxIdentityLossRetries   = 3;  // 3 retries at 60ms = 180ms max identity-loss wait
        private const int IdentityLossRetryWaitMs  = 60; // ACD identity typically resolves within one capture cycle
        // Delay between upgrade button clicks.
        public int UpgradeClickDelayMs { get; set; } = 8;
        // After Auto Gem closes chat, keep the cursor away from the chat area for this long
        // before sending Space or touching the gem pane. Increase if chat fade still blocks clicks.
        public int ChatCloseFadeDelayMs { get; set; } = 500;
        public int PortalAtFourDelayMs { get; set; } = 400;
        public int PortalAfterInitialClickDelayMs { get; set; } = 150;
        private const int DefaultThreePhasePortalReadyTimeoutMs = 900;
        private const int DefaultThreePhasePortalPostStartLeadMs = 250;
        private const int DefaultThreePhasePortalPostStartLeadSafeMs = 1000;
        private const int TargetValidationReclickSettleMs = 8;
                public int UserInterferenceCursorThresholdPx { get; set; } = 18;
        public int UserInterferenceIgnoreAfterPluginInputMs { get; set; } = 80;
        public int UserInterferenceSettleDelayMs { get; set; } = 100;
        public int SoftRestartBackoffMs { get; set; } = 40;
        public int MaxSoftRestartsPerWindow { get; set; } = 4;
        public int SoftRestartWindowMs { get; set; } = 5000;
        public int TargetValidationTimeoutMs { get; set; } = 35;
        public int TargetValidationPollMs { get; set; } = 10;
        public bool IgnoreUpgradeButtonAnimGate { get; set; } = false;

        public int MaxResetScrollClicks { get; set; } = 40;
        public int MaxDownScrollClicks { get; set; } = 80;
        public int MinIdentifiedCellsForNavigation { get; set; } = 1;
        public int MinLiveScanRowsForNavigation { get; set; } = 2;
        public int ScrollRowsPerClick { get; set; } = 1;
        public int MaxProbeNoIdentityRetries { get; set; } = 0;

        public int ScrollHoldMs { get; set; } = 0;

        public int MaxScrollHoldMsPerRow { get; set; } = 700;

        private const int UrshiCols = 5;
        public int ScanRowsPerViewport { get; set; } = 3;
        public int MaxMicroScrollClicksPerRow { get; set; } = 8;
        public float ScrollRowAdvanceFraction { get; set; } = 0.82f;

        public bool DisableResetScrollUpInVerification { get; set; } = true;

        private bool _menuStateApplied;
        private bool _lastMenuEnabled;
        private int _lastMenuMode = -1;
        private string _lastMenuSpecificName = string.Empty;

        public bool FullListVerificationMode { get; set; } = false;
        public bool AutoUpgradeAfterFullListVerification { get; set; } = false;
        public bool RequireIdentifiedCellsForNavigation { get; set; } = false;
        public bool ResetToTopBeforeFullScan { get; set; } = false;
        public bool LogSelectionEvidence { get; set; } = false;

        public int CandidateRowCount { get; set; } = 16;
        public int CandidateColumnCount { get; set; } = 5;
        public int FlatCandidateItemCount { get; set; } = 24;
        public int FlatCandidateRowProbeCount { get; set; } = 3;

        public float GemListLeftRatio { get; set; } = 0.06f;
        public float GemListTopRatio { get; set; } = 0.685f;
        public float GemListRightRatio { get; set; } = 0.89f;
        public float GemListBottomRatio { get; set; } = 0.965f;

        public float MinCellWidthPx { get; set; } = 28f;
        public float MaxCellWidthPx { get; set; } = 82f;
        public float MinCellHeightPx { get; set; } = 28f;
        public float MaxCellHeightPx { get; set; } = 82f;
        public float RowClusterTolerancePx { get; set; } = 6f;

        private static readonly Dictionary<string, int> HardCapByGemName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Boon of the Hoarder", 50 },
            { "Iceblink", 50 },
            { "Legacy of Dreams", 99 },
            { "Esoteric Alteration", 100 },
            { "Mutilation Guard", 100 },
            { "Whisper of Atonement", 150 },
        };

        private static readonly Dictionary<string, int> AutomationStopCapByGemName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Iceblink", 25 },
            { "Whisper of Atonement", 150 },
        };

        private static readonly HashSet<string> Allowed150Fallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Bane of the Trapped",
            "Zei's Stone of Vengeance",
            "Bane of the Stricken",
            "Simplicity's Strength",
            "Taeguk",
            "Bane of the Powerful",
            "Pain Enhancer",
            "Gem of Ease",
            "Moratorium",
            "Wreath of Lightning",
            "Enforcer",
            "Molten Wildebeest's Gizzard",
            "Invigorating Gemstone",
            "Boyarsky's Chip",
            "Mirinae",
            "Mirinae Teardrop of the Starweaver",
            "Mirinae, Teardrop of the Starweaver",
            "Gem of Efficacious Toxin",
        };

        private IUiElement _gemUpgradePane;
        private IUiElement _itemsList;
        private IUiElement _itemsContent;
        private IUiElement _stackPanel;
        private IUiElement _scrollBar;
        private IUiElement _upgradeButton;
        private IUiElement _itemButton;
        private IUiElement _gemStatusText;
        private IUiElement _conversationDialogMain;
        private IUiElement _chatEditLine;
        private int _lastConversationCloseTick = int.MinValue;
        private int _lastGemPaneChatCloseTick = int.MinValue;
        private int _chatCloseFadeWaitUntilTick = int.MinValue;
        private bool _chatCloseFadePendingDialogSpace;
        private int _chatCloseFadePendingAttempts = int.MinValue;
        private const int ConversationCloseThrottleMs = 150;
        private readonly List<CellRef> _candidateCells = new List<CellRef>();

        private IFont _warningFont;

        private AutomationStage _stage = AutomationStage.Idle;
        private string _lastFailureReason = string.Empty;
        private string _paneWarningMessage = string.Empty;
        private int _lastActionTick = int.MinValue;

        private int _lastUpgradeClickTick = int.MinValue;
        private int _portalAnchorClickTick = int.MinValue;
        private int _lastObservedUpgradeAttempts = int.MinValue;
        private int _lastUpgradeProgressTick = int.MinValue;
        private int _lastPortalActionTick = int.MinValue;
        private int _lastRecoveryUpgradeAttempts = int.MinValue;
        private int _portalRequestedTick = int.MinValue;
        private int _runningStartTick = int.MinValue;
        private int _firstUpgradeClickTick = int.MinValue;
        private int _initialUpgradeAttemptsThisRun = int.MinValue;
        private int _noProgressAbortTick = int.MinValue;
        private bool _hasSentInitialUpgradeClick;
        private bool _portalRequestedThisRun;
        private bool _upgradeProgressObservedThisRun;
        private int _savedCursorX;
        private int _savedCursorY;
        private bool _autoRunning;
        private int _targetValidationStartTick = int.MinValue;
        private int _targetValidationAttempts;
        private int _cursorBaselineX = int.MinValue;
        private int _cursorBaselineY = int.MinValue;
        private int _cursorIgnoreUntilTick = int.MinValue;
        private bool _softRestartPending;
        private int _softRestartBlockedUntilTick = int.MinValue;
        private int _userSettleUntilTick = int.MinValue;
        private int _lastUserInterferenceTick = int.MinValue;
        private int _softRestartWindowStartTick = int.MinValue;
        private int _softRestartCountInWindow;

        private GemTarget _target;
        private readonly List<GemOrderEntry> _orderedGems = new List<GemOrderEntry>();

        private ObservedPageSnapshot _currentSnapshot;
        private readonly HashSet<string> _seenPageSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private uint _targetAcd;          // AcdId of target gem — for ACD-based post-click validation
        private bool _navTargetLogged;    // Suppresses repeated row/col log lines within one nav run

        private readonly List<AutoPlanStep> _autoPlan = new List<AutoPlanStep>();
        private string _autoPlanSummary = string.Empty;
        private readonly List<int> _lowestPlanSequence = new List<int>();
        private string _lowestPlanSummary = string.Empty;
        private int _lowestPlanPointer;
        private bool _lowestAwaitingResolution;
        private int _lowestUpgradeClickStartUpgrades = int.MinValue;
        private int _lowestAttemptResolvedTick = int.MinValue;
        private int _lowestRetargetEarliestTick = int.MinValue;
        private uint _lowestValidationAcd;
        private int _lowestValidationPreRank = -1;
        private bool _autoAwaitingResolution;
        private int _autoUpgradeClickStartUpgrades = int.MinValue;
        private int _autoAttemptResolvedTick = int.MinValue;
        private int _autoRetargetEarliestTick = int.MinValue;
        private readonly Dictionary<int, int> _autoConfirmedRankByAbs = new Dictionary<int, int>();
        private int _autoValidationPreRank = -1;
        private bool _persistentAwaitingResolution;
        private int _persistentUpgradeClickStartUpgrades = int.MinValue;
        private int _persistentAttemptResolvedTick = int.MinValue;
        private int _persistentRetargetEarliestTick = int.MinValue;

        private readonly Dictionary<int, Tuple<string, int>> _confirmedSlotMap = new Dictionary<int, Tuple<string, int>>();

        private int _resetScrollClicks;
        private int _downScrollClicks;
        private int _arrowScrollAttempts;  // clicks sent this navigation pass; reset with ResetState
        private int _lastArrowScrollDirection;
        private int _afterScrollWait;
        private int _postScrollRealignAttempts;
        private const int MaxPostScrollRealignAttempts = 3;
        // Post-scroll settle time shared by both realign and general settle passes.
        private const int PostScrollWaitMs = 8;
        private const int PageTrustSettleWaitMs = 35; // wait when IsPageTrustworthyForResolve fails; ACD identity needs longer to stabilize
        private int _lastKnownPhysicalBottomTopRow = -1;
        private int _lastOrderedGemCountSignature = -1;
        private int _lastVirtualGridColumnSignature = -1;
        private int _lastVirtualGridRowSignature = -1;
        private bool _lostLiveIdentityAfterScroll;
        private bool _identityLossCheckPending;
        private bool _scrollCaptureFailed;
        private int _identityLossRetryCount;
        private int _identityLossRetryUntilTick = int.MinValue;
        private bool _lastCaptureHadUsableLiveAcds;
        private VirtualGridModel _virtualGrid;
        private AbsoluteGridModel _absoluteGrid;
        private int _estimatedTopVisibleRow = -1;
        private float _viewportOriginRowFloat = -1f;
        private int _viewportOriginRowInt = -1;
        private int _viewportEpoch;
        private float _lastGoodStackPanelTop = float.NaN;
        private float _lastMeasuredRowPitch = float.NaN;
        private float _lastMeasuredColumnPitch = float.NaN;
        private float _lastMeasuredCellHeight = float.NaN;
        private RectangleF _stableGridAnchorRect = RectangleF.Empty;
        private float _lastStableStackTop = float.NaN;
        private int _noProgressSeekCount = 0;
        private const int MaxNoProgressSeekCount = 5; // 5 capture cycles before declaring a stall; tolerates async wheel-tick settle time
        private const float ScrollMotionThresholdRows = 0.08f;
        private const float LiveAnchorSnapThresholdPx = 10f;
        private bool _runtimeBottomLocked = false;
        private int _runtimeBottomTopRow = -1;
        private int _lastLiveCellCountBeforeScroll = 0;
        private int _postScrollSettlePasses = 0;
        private const int MaxPostScrollSettlePasses = 4;
        private const int CandidateStrideRows = 18;
        private const int CandidateStrideCols = 5;
        private const int ItemStridePerRow = 6;

        private sealed class TrackedLiveCell
        {
            public int AbsoluteIndex;
            public uint AcdId;
            public RectangleF LastRect;
            public float LastStackTop;
            public int LastSeenTick;
            public bool ConfirmedLive;
        }

        private readonly Dictionary<int, TrackedLiveCell> _trackedLiveCells = new Dictionary<int, TrackedLiveCell>();
        private int _trackedLiveTtlMs = 280;
        private int _highestNativeAbsoluteIndexSeen = -1;
        private List<VisibleCell> _lastExtendedNativeCells = new List<VisibleCell>();
        private int _lastExtendedNativeRowCount = 0;
        private int _lastMeasuredVisibleRowCount;
        private int _currentProbeAbsoluteIndex = -1;
        private int _preClickItemButtonAnim = -2;
        private int _targetComfortNudgeAttempts = 0;
        private bool _wheelPostNudgeCorrectionPending = false;
        private int _wheelPostNudgeTargetAbs = -1;

        // Indicates whether an immediate adjacent wheel step has already been performed for the current target.
        private bool _directAdjacentStepDone = false;
        private const int MaxTargetComfortNudgeAttempts = 2;
        private const int LateTpComfortNudgeMaxHoldMs = 12;
        private uint _latchedItemButtonAcd;
        private int _latchedItemButtonAcdTick = int.MinValue;
        private uint _selectedReadyLatchedAcd;
        private string _selectedReadyLatchedName = string.Empty;
        private int _selectedReadyLatchedRank = -1;
        private int _selectedReadyLatchedAbsoluteIndex = -1;
        private int _selectedReadyTick = int.MinValue;
        private int _viewportRecoveryAttempts;
        private int _runningUiLossCount;
        private bool _preserveRunningStateOnReacquire;
        // Rev 5.6.9: set by cap-stop handlers when retargeting off a capped gem.  Tells the
        // validation layer that stale "Upgrade Succeeded" text from the prior capped gem is
        // expected, so it should wait for the item-button anim to change (proof the new gem's
        // selection actually loaded) rather than timing out after 35ms.  Cleared once the new
        // target's selection is confirmed.
        private bool _capRetargetInProgress;
        // Rev 5.6.11: tick at which the capped upgrade attempt resolved (counter dropped).
        // Used to gate the first selection click on the new gem so it doesn't fire during
        // the stale-text/lockout window.  Normal gems settle ~585–600ms after attempt-consumed;
        // first cap-retarget click begins at 420ms; retries continue through the slower lockout window up to 850ms.
        private int _capRetargetResolvedTick = int.MinValue;
        // Rev 5.6.13: start cap-retarget clicks at 420ms (covers fast profiles that settle
        // around ~580ms) and continue up to 850ms (covers slow profiles at ~731ms).
        // Use ~40ms retry spacing (every 4th 10ms poll) to avoid dense input while still
        // landing a click well before the window closes on any tested configuration.
        private const int CapRetargetFirstClickDelayMs = 420;
        // Rev 5.6.12: dedicated sentinel so the 420ms gate only fires on the very first
        // click attempt for a cap-retarget.  _targetValidationAttempts is stale from the
        // previous target cycle so cannot be used as the first-click guard.
        private bool _capRetargetFirstClickPending;
        private bool _scrollAtBottom;
        private readonly HashSet<int> _scannedAbsoluteIndices = new HashSet<int>();
        private bool _bottomNudgeAttempted;
        private bool _usedViewportProbeFallbackThisRun;

        private bool _probeActive;
        private ProbeReason _probeReason;
        private List<VisibleCell> _probeCells = new List<VisibleCell>();
        private ObservedPageSnapshot _probeSnapshot;
        private int _probeIndex;
        private bool _probeWaitingForValidation;
        private VisibleCell _probePendingCell;
        private int _probeActionTick = int.MinValue;
        private int _probeNoIdentityRetryCount;

        private enum AutomationStage
        {
            Idle,
            ResetProbeCurrentPage,
            ResetScrollUp,
            WaitAfterScrollUp,
            SearchProbeCurrentPage,
            SearchScrollDown,
            WaitAfterScrollDown,
            DirectCaptureCurrentPage,
            DirectScrollToTargetViewport,
            SelectObservedTarget,
            ValidateObservedTarget,
            Running,
            VerificationComplete,
            Failed,
        }

        private enum ProbeReason
        {
            None,
            Reset,
            Search,
        }

        private class CellRef
        {
            public string Path;
            public IUiElement Element;
            public string Family;
            public int Major;
            public int Minor;
            public uint CachedLegendaryGemAcdId;       // LegendaryGemAcdId read in GetMappedVisibleCells — used for ACD identity (Priority 0 in TryEnrichCellsFromDirectText) and scroll calibration
        }

        private class VisibleCell
        {
            public CellRef Ref;
            public RectangleF Rect;
            public int RowIndex;
            public int ColumnIndex;
            public string DirectText;
            public string FamilyTag;
            public bool IsProjected;
            public int AbsoluteIndex = -1;
        }

        private class ObservedCell
        {
            public VisibleCell VisibleCell;
            public string SelectedGemName;
            public int SelectedGemRank;
            public string SourceText;
            public bool MatchTarget;
            public bool ItemButtonLoaded;
            public int UpgradeButtonAnimState;
            public int ViewportEpoch;

            public string IdentityKey
            {
                get
                {
                    if (!string.IsNullOrEmpty(SelectedGemName) && SelectedGemRank >= 0)
                        return NormalizeGemLabel(SelectedGemName) + "#" + SelectedGemRank.ToString();
                    var path = (VisibleCell != null && VisibleCell.Ref != null) ? VisibleCell.Ref.Path : string.Empty;
                    return "unknown@" + (!string.IsNullOrEmpty(path) ? GetShortPath(path) : "cell");
                }
            }
        }

        private class ObservedPageSnapshot
        {
            public List<VisibleCell> VisibleCells = new List<VisibleCell>();
            public List<VisibleCell> LiveVisibleCells = new List<VisibleCell>();
            public List<VisibleCell> InferredViewportCells = new List<VisibleCell>();
            public List<ObservedCell> ObservedCells = new List<ObservedCell>();
            public RectangleF PaneRect;
            public RectangleF ListBounds;
            public PointF ScrollUpPoint;
            public PointF ScrollDownPoint;
            public string Signature = string.Empty;
            public int IdentifiedCellCount;
            public ObservedCell TargetCell;
            public ProbeReason Reason;
        }

        private sealed class ViewportCapture
        {
            public bool HasPane;
            public bool HasListBounds;
            public bool HasScrollLane;
            public bool HasLiveCells;

            public RectangleF PaneRect;
            public RectangleF ListBounds;
            public RectangleF ScrollLaneRect;

            public List<VisibleCell> LiveCells = new List<VisibleCell>();
        }

        private sealed class AbsoluteGridSlot
        {
            public int AbsoluteIndex;
            public int AbsoluteRow;
            public int AbsoluteCol;

            public RectangleF PredictedRect;
            public bool IntersectsViewport;
            public bool HasLiveCell;
            public VisibleCell LiveCell;
        }

        private sealed class AbsoluteGridModel
        {
            public int ColumnCount = 5;
            public int TotalSlotCount;
            public int TotalRowCount;

            public int ViewportTopRowInt;
            public float ViewportTopRowFloat;
            public int VisibleRowCount;

            public float RowPitch;
            public float ColumnPitch;
            public float CellWidth;
            public float CellHeight;

            public RectangleF AnchorRect;
            public RectangleF ListBounds;

            public readonly List<AbsoluteGridSlot> Slots = new List<AbsoluteGridSlot>();
        }

        private class VirtualGridModel
        {
            public int ColumnCount = 5;
            public int VisibleRowCount = 3;
            public int LiveScanRowCount = 3;
            public int TotalSlotCount;
            public int TotalRowCount;
            public float CellWidth;
            public float CellHeight;
            public float ColumnPitch;
            public float RowPitch;
            public RectangleF AnchorCellRect;
            public int EstimatedTopVisibleRow = -1;
            public readonly List<VirtualSlot> Slots = new List<VirtualSlot>();
        }

        private class VirtualSlot
        {
            public int AbsoluteIndex;
            public int RowIndex;
            public int ColumnIndex;
            public string GemName;
            public int GemRank;
            public bool IsTarget;
            public bool IsPredictedVisible;
            public RectangleF PredictedRect;
        }

        private class GemOrderEntry
        {
            public IItem Item;
            public int AbsoluteIndex;
            public int HardCap;
            public int EffectiveStopCap;
            public bool BelowEffectiveStopCap;
            public bool CanAttemptAt150Fallback;
        }

        private class GemTarget
        {
            public string Name;
            public int Rank;
            public int AbsoluteIndex;
            public string Reason;
            public GemOrderEntry Source;
        }

        private class AutoPlanStep
        {
            public string Name;
            public int AbsoluteIndex;
            public int Attempts;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            Enabled = true;

            const string root = "Root.NormalLayer.vendor_dialog_mainPage.riftReward_dialog.LayoutRoot.gemUpgradePane";
            _gemUpgradePane = Hud.Render.RegisterUiElement(root, null, null);
            _itemsList = Hud.Render.RegisterUiElement(root + ".items_list", null, null);
            _itemsContent = Hud.Render.RegisterUiElement(root + ".items_list._content", null, null);
            _stackPanel = Hud.Render.RegisterUiElement(root + ".items_list._content._stackpanel", null, null);
            _scrollBar = Hud.Render.RegisterUiElement(root + ".items_list._scrollbar", null, null);
            _upgradeButton = Hud.Render.RegisterUiElement(root + ".upgrade_button", null, null);
            _itemButton = Hud.Render.RegisterUiElement(root + ".item_button", null, null);
            _gemStatusText = Hud.Render.RegisterUiElement(root + ".gemStatusText", null, null);
            _conversationDialogMain = Hud.Render.RegisterUiElement("Root.NormalLayer.conversation_dialog_main", null, null);
            _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);

            _candidateCells.Clear();

            for (int row = 0; row < Math.Max(1, CandidateRowCount); row++)
            {
                for (int col = 0; col < Math.Max(1, CandidateColumnCount); col++)
                    RegisterCandidatePath("row", root + ".items_list._content._stackpanel._tilerow" + row + "._item" + col + ".Item", row, col);
            }

            for (int row = 0; row < Math.Max(1, FlatCandidateRowProbeCount); row++)
            {
                for (int index = 0; index < Math.Max(1, FlatCandidateItemCount); index++)
                    RegisterCandidatePath("flatrow", root + ".items_list._content._stackpanel._tilerow" + row + "._item" + index + ".Item", row, index);
            }

            for (int index = 0; index < Math.Max(1, FlatCandidateItemCount); index++)
                RegisterCandidatePath("stack", root + ".items_list._content._stackpanel._item" + index + ".Item", 0, index);

            RegisterStrideCandidatePaths();

            _warningFont = Hud.Render.CreateFont("tahoma", 8, 255, 255, 70, 70, true, false, 220, 0, 0, 0, true);

        }

        private void RegisterCandidatePath(string family, string path, int major, int minor)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (_candidateCells.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;

            _candidateCells.Add(new CellRef
            {
                Path = path,
                Element = Hud.Render.RegisterUiElement(path, null, null),
                Family = family ?? string.Empty,
                Major = major,
                Minor = minor,
            });
        }

        private void RegisterStrideCandidatePaths()
        {
            const string urshiRoot = "Root.NormalLayer.vendor_dialog_mainPage.riftReward_dialog.LayoutRoot.gemUpgradePane";
            for (int row = 0; row < CandidateStrideRows; row++)
            {
                for (int col = 0; col < CandidateStrideCols; col++)
                {
                    int itemIndex = row * ItemStridePerRow + col;
                    RegisterCandidatePath("row6", urshiRoot + ".items_list._content._stackpanel._tilerow" + row + "._item" + itemIndex + ".Item", row, col);
                }
            }
        }

        private int GetCandidateFamilyPriority(string family)
        {
            if (string.Equals(family, "row6", StringComparison.Ordinal)) return 0;
            if (string.Equals(family, "row", StringComparison.Ordinal)) return 1;
            if (string.Equals(family, "flatrow", StringComparison.Ordinal)) return 2;
            if (string.Equals(family, "stack", StringComparison.Ordinal)) return 3;
            return 9;
        }

        private List<VisibleCell> DeduplicateVisibleCells(List<VisibleCell> cells)
        {
            var best = new Dictionary<string, VisibleCell>(StringComparer.Ordinal);
            foreach (var c in cells)
            {
                if (c == null) continue;
                string key = ((int)c.Rect.Left).ToString(CultureInfo.InvariantCulture) + ":" + ((int)c.Rect.Top).ToString(CultureInfo.InvariantCulture) + ":" + ((int)c.Rect.Width).ToString(CultureInfo.InvariantCulture) + ":" + ((int)c.Rect.Height).ToString(CultureInfo.InvariantCulture);
                VisibleCell existing;
                if (!best.TryGetValue(key, out existing)) { best[key] = c; continue; }
                int a = GetCandidateFamilyPriority(c.Ref != null ? c.Ref.Family : string.Empty);
                int b = GetCandidateFamilyPriority(existing.Ref != null ? existing.Ref.Family : string.Empty);
                if (a < b) best[key] = c;
            }
            return best.Values.OrderBy(v => v.Rect.Top).ThenBy(v => v.Rect.Left).ToList();
        }

        private void SyncMenuState()
        {
            bool enabled = s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled;
            int mode = Math.Max(0, Math.Min(4, s7o_AutoGemUpgradeState.AutoGemMode));
            string specificName = (s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty).Trim();

            bool changed = !_menuStateApplied
                || enabled != _lastMenuEnabled
                || mode != _lastMenuMode
                || !string.Equals(specificName, _lastMenuSpecificName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            AutoStartEnabled = enabled;
            AutoPercentMode = (mode == 0);
            PreferHighestNonMaxFirst = (mode == 2);
            FastFallbackMode = (mode == 3);
            ForcedGemNameExact = (mode == 4) ? specificName : string.Empty;
            // Keep the legacy property synchronized with the literal anchor delay
            // so any existing UI or debug paths that inspect it still see the
            // current effective delay value.
            PortalAtFourDelayMs = s7o_AutoGemUpgradeState.GetFullPortalDelayMs();

            if (changed)
            {
                ResetState();
                Log(() => "menu sync: enabled=" + (enabled ? "1" : "0") + ", mode=" + mode.ToString(CultureInfo.InvariantCulture) + ", specific='" + specificName + "'");
            }

            _menuStateApplied = true;
            _lastMenuEnabled = enabled;
            _lastMenuMode = mode;
            _lastMenuSpecificName = specificName;
        }

        public void AfterCollect()
        {
            // invoke debug instrumentation before normal logic so run metrics can observe pane hide and resets
            try { InstrumentationHook(); } catch { }
            SyncMenuState();

            if (!AutoStartEnabled)
            {
                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            if (!Hud.Game.IsInGame || !Hud.Window.IsForeground)
            {
                _lastConversationCloseTick = int.MinValue;
                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            if (HandleChatCloseFadeWait())
                return;

            if (_gemUpgradePane?.Visible != true)
            {
                if (TryCloseConversationDialogBeforeGemPane())
                    return;

                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            _lastConversationCloseTick = int.MinValue;

            int upgrades = GetUpgradeAttempts();
            if (_tailWaitAfterFinalAttempt)
            {
                ClearSoftRestartWait(true);
                return;
            }

            if (TryCloseChatBeforeGemPaneAutomation(upgrades))
                return;

            if (upgrades <= 0)
            {
                _tailWaitAfterFinalAttempt = true;
                ClearSoftRestartWait(true);
                return;
            }

            if (HandleSoftRestartWait())
                return;

            if (_probeActive)
            {
                AdvancePageProbe();
                return;
            }

            if (_autoRunning || _stage == AutomationStage.Running)
            {
                HandleRunningState(upgrades);
                return;
            }

            // Terminal failure: hold until the pane closes or user changes mode.
            // Without this guard, _target == null below would call AcquireTarget()
            // again every tick, re-entering Fail() and spamming the log until pane-hide.
            if (_stage == AutomationStage.Failed)
                return;

            if (_target == null)
            {
                if (!AcquireTarget())
                    return;
            }


            switch (_stage)
            {
                case AutomationStage.Idle:
                    Log(() => "Target acquired: " + _target.Name + " r" + _target.Rank + " a" + _target.AbsoluteIndex + " (" + _target.Reason + ")" + (_targetAcd != 0 ? " acd=" + _targetAcd : string.Empty));
                    _navTargetLogged = false;
            _autoPlan.Clear();
            _autoPlanSummary = string.Empty;
                    _scannedAbsoluteIndices.Clear();
                    _currentProbeAbsoluteIndex = -1;
                    _bottomNudgeAttempted = false;
            _usedViewportProbeFallbackThisRun = false;
                    if (FullListVerificationMode)
                    {
                        if (ResetToTopBeforeFullScan && !DisableResetScrollUpInVerification)
                        {
                            _stage = AutomationStage.ResetProbeCurrentPage;
                            StartPageProbe(ProbeReason.Reset);
                        }
                        else
                        {
                            SetViewportOriginExact(0, "run-start");
                            _stage = AutomationStage.SearchProbeCurrentPage;
                            StartPageProbe(ProbeReason.Search);
                        }
                    }
                    else
                    {
                        SetViewportOriginExact(0, "top-reset");
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                    }
                    break;

                case AutomationStage.ResetProbeCurrentPage:
                    break;

                case AutomationStage.ResetScrollUp:
                {
                    if (_currentSnapshot == null)
                    {
                        Fail("reset snapshot missing");
                        return;
                    }

                    int requiredResetClicks = GetRequiredTopResetClicks();
                    if (_resetScrollClicks >= requiredResetClicks)
                    {
                        SetViewportOriginExact(0, "top-reset");
                        Log(() => "top reset complete after " + _resetScrollClicks + " scroll-up clicks");
                        _lastActionTick = NowTick();
                        _stage = AutomationStage.WaitAfterScrollUp;
                        return;
                    }

                    if (ElapsedMs(_lastActionTick) < ScrollClickDelayMs)
                        return;

                    _resetScrollClicks++;
                    ClickPoint(_currentSnapshot.ScrollUpPoint, "reset-scroll-up #" + _resetScrollClicks, ScrollHoldMs);
                    _lastActionTick = NowTick();
                    return;
                }

                case AutomationStage.WaitAfterScrollUp:
                    if (ElapsedMs(_lastActionTick) < ScrollSettleDelayMs)
                        return;
                    _stage = AutomationStage.SearchProbeCurrentPage;
                    StartPageProbe(ProbeReason.Search);
                    break;

                case AutomationStage.SearchProbeCurrentPage:
                    break;

                case AutomationStage.SearchScrollDown:
                {
                    if (!FullListVerificationMode)
                    {
                        SoftAbortAndRestart("normal runtime must not re-enter broad probe/search flow");
                        return;
                    }

                    if (ElapsedMs(_lastActionTick) < ScrollClickDelayMs)
                        return;
                    if (_currentSnapshot == null)
                    {
                        Fail("search snapshot missing");
                        return;
                    }
                    if (_downScrollClicks >= MaxDownScrollClicks)
                    {
                        Fail("hit down-scroll limit before locating target");
                        return;
                    }

                    if (_target != null)
                    {
                        int desiredTopRow;
                        int startTopRow;
                        int deltaRows;
                        if (!CanSeekTargetViewport(out desiredTopRow, out startTopRow, out deltaRows))
                        {
                            if (IsTargetViewportTrulyLocked(_target))
                            {
                                StartPageProbe(ProbeReason.Search);
                                return;
                            }

                            if (QueueViewportRecovery("search seek blocked because viewport truth is inconsistent", 35))
                                return;

                            SoftAbortAndRestart("search seek blocked because viewport truth is inconsistent");
                            return;
                        }

                        if (!InvariantAllowsTravel("SearchScrollDown"))
                        {
                            SoftAbortAndRestart("invariant violation: attempted travel while target viewport is already locked");
                            return;
                        }

                        if (!TryScrollToTargetTopRow(desiredTopRow))
                        {
                            Fail("viewport seek did not advance toward desiredTopRow=" + desiredTopRow + " from topRow=" + startTopRow + " (deltaRows=" + deltaRows + ")");
                            return;
                        }

                        _lastActionTick = NowTick();
                        StartPageProbe(ProbeReason.Search);
                        return;
                    }

                    int maxTopScanRow = GetMaxTopScanRow();
                    if (GetAuthoritativeViewportTopRow() >= maxTopScanRow)
                    {
                        Log(() => "bottom of scan range reached");
                        if (FullListVerificationMode && !AutoUpgradeAfterFullListVerification)
                        {
                            _autoRunning = false;
                            _stage = AutomationStage.VerificationComplete;
                            return;
                        }
                    }

                    int startTopRowFallback = GetAuthoritativeViewportTopRow();
                    int remainingRows = Math.Max(0, maxTopScanRow - startTopRowFallback);
                    int targetRows = Math.Max(1, Math.Min(ScanRowsPerViewport, remainingRows));

                    if (!TryDragScrollDownRows(targetRows))
                    {
                        Fail("held scroll-down did not advance requested rows=" + targetRows + " from topRow=" + startTopRowFallback);
                        return;
                    }

                    _lastActionTick = NowTick();
                    StartPageProbe(ProbeReason.Search);
                    return;
                }

                case AutomationStage.WaitAfterScrollDown:
                    break;

                case AutomationStage.DirectCaptureCurrentPage:
                {
                    int captureWaitMs = _afterScrollWait > 0 ? _afterScrollWait : CellClickDelayMs;
                    _afterScrollWait = 0;  // consume
                    if (ElapsedMs(_lastActionTick) < captureWaitMs)
                        return;

                    int topRowBeforeCapture = GetAuthoritativeViewportTopRow();

                    bool geometryFresh = TryCaptureAndRefreshCurrentGeometry();
                    if (geometryFresh)
                    {
                        if (_currentSnapshot != null && _currentSnapshot.VisibleCells != null && _currentSnapshot.VisibleCells.Count >= 3)
                            _viewportRecoveryAttempts = 0;
                    }
                    if (!geometryFresh)
                    {
                        _postScrollRealignAttempts = 0;
                        _postScrollSettlePasses = 0;
                        SoftAbortAndRestart("geometry exists but live slot identity is absent");
                        return;
                    }

                    if (DetectedLiveOvershootAfterScroll())
                    {
                        _postScrollSettlePasses = 0;
                        _lastActionTick = NowTick();
                        _afterScrollWait = PostScrollWaitMs;
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }

                    // Rev 5.6.4: Reverted Finding B (early ACD-direct before realign/settle).
                    // The early-exit bypassed alignment stabilization, which caused downstream
                    // IsPageTrustworthyForResolve failures ("alignment-error=70.2 -> 262 -> 280")
                    // and soft-restart-limit FAILs on retargets where the ACD cache was populated
                    // quickly enough for the early block to fire.  The late ACD-direct block at
                    // line ~2541 still benefits from Finding A's _targetAcd preservation and is
                    // the correct place for the shortcut — it fires AFTER stabilization, when
                    // the page is trustworthy.

                    if (NeedsPostScrollRealignment() && _postScrollRealignAttempts < MaxPostScrollRealignAttempts)
                    {
                        _postScrollRealignAttempts++;
                        _lastActionTick = NowTick();
                        _afterScrollWait = PostScrollWaitMs;
                        Log(() => "post-scroll realign retry #" + _postScrollRealignAttempts);
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }

                    if (ViewportNeedsSettle() && _postScrollSettlePasses < MaxPostScrollSettlePasses)
                    {
                        _postScrollSettlePasses++;
                        _lastActionTick = NowTick();
                        _afterScrollWait = PostScrollWaitMs;
                        Log(() => "post-scroll settle retry #" + _postScrollSettlePasses + " alignErr=" + GetCurrentAlignmentErrorPx().ToString("0.0", CultureInfo.InvariantCulture));
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }

                    _postScrollRealignAttempts = 0;
                    _postScrollSettlePasses = 0;

                    int topRowAfterCapture = GetAuthoritativeViewportTopRow();

                    if (_target == null)
                    {
                        Fail("direct navigation missing target");
                        return;
                    }

                    // ACD-direct shortcut: if the target gem's ACD is live in the current
                    // viewport snapshot, jump straight to SelectObservedTarget without going
                    // through the stall counter at all.  This must run before
                    // RegisterSeekProgressOrStall so that a valid, already-visible target
                    // never increments the stall count or triggers a spurious restart.
                    if (_targetAcd != 0 && _currentSnapshot?.VisibleCells != null)
                    {
                        VisibleCell acdDirectCell = null;
                        foreach (var vc in _currentSnapshot.VisibleCells)
                        {
                            if (vc == null || vc.IsProjected || vc.Ref == null) continue;
                            if (vc.Ref.CachedLegendaryGemAcdId == _targetAcd)
                            {
                                acdDirectCell = vc;
                                break;
                            }
                        }
                        if (acdDirectCell != null)
                        {
                            acdDirectCell.AbsoluteIndex = _target.AbsoluteIndex;
                            _currentSnapshot.TargetCell = new ObservedCell
                            {
                                VisibleCell = acdDirectCell,
                                SelectedGemName = _target.Name,
                                SelectedGemRank = _target.Rank,
                                SourceText = "acd-direct",
                                MatchTarget = true,
                                ItemButtonLoaded = SafeAnimState(_itemButton) != -1,
                                UpgradeButtonAnimState = SafeAnimState(_upgradeButton),
                            ViewportEpoch = _viewportEpoch,
                            };
                            _currentProbeAbsoluteIndex = _target.AbsoluteIndex;
                            _arrowScrollAttempts = 0;
                            _noProgressSeekCount = 0;
                            Log(() => "ACD-direct: target a" + _target.AbsoluteIndex
                                + " '" + _target.Name + "#" + _target.Rank
                                + "' visible at viewport row=" + acdDirectCell.RowIndex
                                + " col=" + acdDirectCell.ColumnIndex
                                + " rect=(" + (int)acdDirectCell.Rect.Left + "," + (int)acdDirectCell.Rect.Top + ")");
                            _lastActionTick = NowTick();
                            _stage = AutomationStage.SelectObservedTarget;
                            return;
                        }
                    }

                    // ACD not visible — register seek progress or stall, then decide next action.
                    RegisterSeekProgressOrStall(topRowBeforeCapture, topRowAfterCapture);

                    if (HitSeekStallLimit())
                    {
                        _noProgressSeekCount = 0;

                        if (IsTargetRowReliablyVisible(_target))
                        {
                            if (TryAssignTargetCellFromCurrentViewport())
                            {
                                _lastActionTick = NowTick();
                                _stage = AutomationStage.SelectObservedTarget;
                                return;
                            }

                            SoftAbortAndRestart("seek stalled while target row was visible but unresolved");
                            return;
                        }

                        SoftAbortAndRestart("seek stalled without meaningful viewport progress");
                        return;
                    }

                    if (GetAuthoritativeViewportTopRow() != topRowBeforeCapture)
                        _arrowScrollAttempts = 0;

                    if (_virtualGrid != null && _virtualGrid.ColumnCount > 0
                        && !_navTargetLogged)
                    {
                        _navTargetLogged = true;
                        int cols = _virtualGrid.ColumnCount;
                        int tRows = _virtualGrid.TotalRowCount;
                        int tRow1 = _target.AbsoluteIndex / cols + 1;
                        int tCol1 = _target.AbsoluteIndex % cols + 1;
                        Log(() => "nav target: gem " + (_target.AbsoluteIndex + 1) + "/" + _orderedGems.Count + " '"
                            + _target.Name + "#" + _target.Rank
                            + "' → row " + tRow1 + "/" + tRows
                            + ", col " + tCol1 + "/" + cols
                            + " (" + cols + "-per-row grid)");
                    }

                    if (_viewportOriginRowInt < 0)
                        SetViewportOriginExact(0, "direct-init");

                    int currentTopRow = GetAuthoritativeViewportTopRow();
                    int currentBottomRow = GetCurrentViewportBottomRow();
                    int targetRow = _virtualGrid != null && _virtualGrid.ColumnCount > 0
                        ? Math.Max(0, _target.AbsoluteIndex / Math.Max(1, _virtualGrid.ColumnCount))
                        : currentTopRow;

                    Log(() => "direct target viewport: rows=" + currentTopRow + "-" + currentBottomRow + ", targetRow=" + targetRow + ", targetAbs=" + _target.AbsoluteIndex + "; attempting direct slot resolution");
                    string trustReason;
                    if (!IsPageTrustworthyForResolve(out trustReason))
                    {
                        Log(() => "resolve-block: " + trustReason
                            + " topRow=" + _viewportOriginRowInt
                            + " liveRows=" + GetLiveVisibleRowCount()
                            + " trackedRows=" + GetTrackedProjectedRowCount()
                            + " authRows=" + GetAuthoritativeViewportVisibleRowCount());
                        _lastActionTick = NowTick();
                        _afterScrollWait = PageTrustSettleWaitMs;
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }
                    if (TryAssignTargetCellFromCurrentViewport())
                    {
                        _lastActionTick = NowTick();
                        _stage = AutomationStage.SelectObservedTarget;
                        return;
                    }

                    bool targetAboveViewport;
                    bool targetBelowViewport;
                    if (IsTargetOutsideCurrentViewport(out targetAboveViewport, out targetBelowViewport))
                    {
                        // Before initiating heavy offscreen travel, attempt an immediate adjacent wheel step if
                        // the target is exactly one row above or below the current viewport.  This avoids the
                        // situation where prearm occurs but the first movement is deferred until the comfort nudge
                        // path.  TryCommitImmediateAdjacentWheelStep() will set a flag so it only fires once per
                        // target.  If it returns true, we have either moved the cursor to arm or sent the wheel
                        // tick.  In either case, stay in this stage so the geometry will be captured again on the
                        // next loop before deciding whether further travel is necessary.
                        if (TryCommitImmediateAdjacentWheelStep())
                        {
                            _stage = AutomationStage.DirectCaptureCurrentPage;
                            return;
                        }

                        Log(() => "direct target not visible on current viewport — scrolling " + (targetBelowViewport ? "down" : "up"));
                        _stage = AutomationStage.DirectScrollToTargetViewport;
                        return;
                    }

                    if (QueueViewportRecovery("could not resolve target on current viewport even though row should be visible", 35))
                        return;

                    SoftAbortAndRestart("could not resolve live target slot on current viewport");
                    return;
                }

                case AutomationStage.DirectScrollToTargetViewport:
                {
                    if (_currentSnapshot == null || _target == null || _virtualGrid == null)
                    {
                        Fail("direct navigation snapshot missing");
                        return;
                    }

                    bool targetAboveViewport;
                    bool targetBelowViewport;
                    if (!IsTargetOutsideCurrentViewport(out targetAboveViewport, out targetBelowViewport))
                    {
                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }

                    if (!InvariantAllowsTravel("DirectScrollToTargetViewport"))
                    {
                        SoftAbortAndRestart("invariant violation: attempted travel while target row is already in the current viewport");
                        return;
                    }

                    int desiredTopRow;
                    int currentTopRow;
                    int deltaRows;
                    if (!CanSeekTargetViewport(out desiredTopRow, out currentTopRow, out deltaRows))
                    {
                        if (IsTargetViewportTrulyLocked(_target))
                        {
                            _stage = AutomationStage.DirectCaptureCurrentPage;
                            return;
                        }

                        if (QueueViewportRecovery("direct seek blocked because viewport truth is inconsistent", 45))
                            return;

                        SoftAbortAndRestart("direct seek blocked because viewport truth is inconsistent");
                        return;
                    }

                    bool moved = TryScrollToTargetTopRow(desiredTopRow);
                    if (!moved)
                    {
                        if (QueueViewportRecovery("scroll produced no confirmed viewport progress", 45))
                            return;

                        SoftAbortAndRestart("scroll produced no confirmed viewport progress");
                        return;
                    }

                    _lastActionTick = NowTick();
                    _afterScrollWait = 0;
                    _stage = AutomationStage.DirectCaptureCurrentPage;
                    return;
                }

                case AutomationStage.SelectObservedTarget:
                    if (_currentSnapshot?.TargetCell == null || _currentSnapshot.TargetCell.VisibleCell == null)
                    {
                        SoftAbortAndRestart("target cell missing after probe");
                        return;
                    }
                    if (IsSelectedTargetReady(_target))
                    {
                        Log(() => "select short-circuit: target already selected and upgrade-ready");
                        StartRunningFromConfirmedTarget();
                        return;
                    }
                    if (!HasLiveViewportTruth())
                    {
                        if (QueueViewportRecovery("selection blocked because live viewport truth is gone", 35))
                            return;

                        SoftAbortAndRestart("selection blocked because live viewport truth is gone");
                        return;
                    }
                    if (_currentSnapshot.TargetCell.ViewportEpoch != _viewportEpoch
                        || _currentSnapshot.TargetCell.VisibleCell.IsProjected
                        || !IsCurrentEpochLiveSlot(_currentSnapshot.TargetCell.VisibleCell))
                    {
                        if (QueueViewportRecovery("selection blocked because target slot is stale for the current viewport epoch", 35))
                            return;

                        SoftAbortAndRestart("selection blocked because target slot is stale for the current viewport epoch");
                        return;
                    }
                    if (ElapsedMs(_lastActionTick) < CellClickDelayMs)
                        return;
                    if (!CanAttemptListCommit(_currentSnapshot.TargetCell.VisibleCell, "select-target"))
                    {
                        if (QueueViewportRecovery("refusing list click because live/current-epoch slot proof is missing", 35))
                            return;

                        SoftAbortAndRestart("refusing list click because live/current-epoch slot proof is missing");
                        return;
                    }
                    if (TryQueueTargetComfortNudge(_currentSnapshot.TargetCell.VisibleCell, "select-target"))
                        return;
                    TryCorrectCursorAfterWheelNudge(_currentSnapshot.TargetCell.VisibleCell, "select-target");

                    // Rev 5.6.11/5.6.14: cap-retarget first-click delay.
                    // The HUD does not expose a "next gem is ready to be selected" signal
                    // for an unselected gem — everything we can see (upgrade button anim,
                    // item button ACD, loaded state) belongs to the currently-selected
                    // (capped) gem.  So we use a bounded first-click delay based on the
                    // observed normal lockout duration: fast profiles settle ~585ms and
                    // slow profiles ~731ms after attempt-consumed, so the first cap-retarget
                    // click fires at 420ms and extended reclicks continue through 850ms
                    // (see capExtendedReclick below).  After the first click lands,
                    // validation uses item-button anim change as the live event signal
                    // (handled by the 1200ms cap-retarget timeout in ValidateObservedTarget).
                    // Scoped to the very first click in a cap-retarget only — reclicks
                    // during validation retries are unaffected.
                    if (_capRetargetInProgress
                        && _capRetargetFirstClickPending
                        && _capRetargetResolvedTick != int.MinValue
                        && ElapsedMs(_capRetargetResolvedTick) < CapRetargetFirstClickDelayMs)
                    {
                        return;
                    }
                    // Sentinel consumed — first click is about to fire.
                    _capRetargetFirstClickPending = false;

                    bool requiresStrictValidation = _currentSnapshot != null && _currentSnapshot.TargetCell != null && !_currentSnapshot.TargetCell.MatchTarget;
                    if (requiresStrictValidation)
                        Log(() => "soft target assignment requires post-click validation before upgrade");
                    _preClickItemButtonAnim = SafeAnimState(_itemButton);
                    int totalGemSlots = Math.Max(_orderedGems != null ? _orderedGems.Count : 0, _target.AbsoluteIndex + 1);
                    Log(() => "gem click: slot " + (_target.AbsoluteIndex + 1) + "/" + totalGemSlots + " a" + _target.AbsoluteIndex + " (" + _target.Name + "#" + _target.Rank + "), preClickAnim=" + _preClickItemButtonAnim);
                    ClickVisibleCell(_currentSnapshot.TargetCell.VisibleCell);
                    _targetValidationStartTick = NowTick();
                    _targetValidationAttempts = 0;
                    _lastActionTick = _targetValidationStartTick;
                    _stage = AutomationStage.ValidateObservedTarget;
                    break;

                case AutomationStage.ValidateObservedTarget:
                {
                    if (ElapsedMs(_lastActionTick) < TargetValidationPollMs)
                        return;

                    _lastActionTick = NowTick();
                    _targetValidationAttempts++;

                    string observedName;
                    int observedRank;
                    string sourceText;
                    bool isMatch = ValidateLoadedSelectionAgainstTarget(_target, out observedName, out observedRank, out sourceText);
                    if (!isMatch && IsSelectedTargetReady(_target))
                    {
                        isMatch = true;
                        if (string.IsNullOrWhiteSpace(sourceText)) sourceText = "ready-short-circuit";
                        if (string.IsNullOrWhiteSpace(observedName) && _target != null) observedName = _target.Name;
                        if (observedRank < 0 && _target != null) observedRank = _target.Rank;
                    }
                    if (LogSelectionEvidence && (_targetValidationAttempts <= 3 || isMatch))
                        Log(() => "validate attempt " + _targetValidationAttempts + ": observed=" + (observedName ?? "<null>") + "#" + observedRank + ", loaded=" + (SafeAnimState(_itemButton) != -1 ? "1" : "0") + ", itemAnim=" + SafeAnimState(_itemButton) + " (was " + _preClickItemButtonAnim + "), evidence='" + ShortEvidence(sourceText) + "'", s7o_AutoGemUpgradeState.DebugLevelState);

                    bool selectionUiLoaded = SafeAnimState(_itemButton) != -1;
                    bool validationSettleElapsed = ElapsedMs(_targetValidationStartTick) >= TargetValidationReclickSettleMs;
                    // Rev 5.6.13: for cap-retarget, extend reclicks at every 4th validation
                    // attempt (~40ms spacing at 10ms poll rate) while within 850ms of the
                    // cap-resolved tick.  Normal (non-cap) paths only reclick at attempts 2
                    // and 4; this extension lets cap-retarget clicks keep firing past the
                    // ~731ms slow-profile lockout to make sure one lands once the stale-text
                    // window closes.  850ms ceiling keeps this scoped; the 1200ms validation
                    // timeout remains as the last-resort fallback.  The previous every-2nd
                    // (20ms) dense spacing was replaced in 5.6.14 to reduce input density.
                    bool capExtendedReclick = _capRetargetInProgress
                        && _targetValidationAttempts > 4
                        && _targetValidationAttempts % 4 == 0
                        && _capRetargetResolvedTick != int.MinValue
                        && ElapsedMs(_capRetargetResolvedTick) <= 850;
                    bool shouldReclick = !isMatch
                        && selectionUiLoaded
                        && validationSettleElapsed
                        && (_targetValidationAttempts == 2 || _targetValidationAttempts == 4 || capExtendedReclick)
                        && _currentSnapshot != null
                        && _currentSnapshot.TargetCell != null
                        && _currentSnapshot.TargetCell.VisibleCell != null;
                    if (shouldReclick)
                    {
                        if (TryQueueTargetComfortNudge(_currentSnapshot.TargetCell.VisibleCell, "validate-retry"))
                            return;
                        TryCorrectCursorAfterWheelNudge(_currentSnapshot.TargetCell.VisibleCell, "validate-retry");
                        _preClickItemButtonAnim = SafeAnimState(_itemButton);
                        int retryTotalSlots = Math.Max(_orderedGems != null ? _orderedGems.Count : 0, _target.AbsoluteIndex + 1);
                        Log(() => "validate retry #" + _targetValidationAttempts + ": reclicking slot " + (_target.AbsoluteIndex + 1) + "/" + retryTotalSlots + " a" + _target.AbsoluteIndex + ", preClickAnim=" + _preClickItemButtonAnim);
                        ClickVisibleCell(_currentSnapshot.TargetCell.VisibleCell);
                        _lastActionTick = NowTick();
                        return;
                    }

                    if (isMatch)
                    {
                        StartRunningFromConfirmedTarget();
                        return;
                    }

                    // Rev 5.6.9: during cap-retarget, the pane carries stale state from the
                    // prior capped success.  The item-button anim state is the authoritative
                    // signal for "new gem's selection has loaded" — when it changes from the
                    // pre-click value, the pane has accepted the click.  For cap-retarget we
                    // wait for that change (up to 1200ms) instead of timing out after 35ms.
                    // Normal (non-cap) validation is untouched.
                    int effectiveTimeout = TargetValidationTimeoutMs;
                    if (_capRetargetInProgress)
                        effectiveTimeout = 1200;

                    if (ElapsedMs(_targetValidationStartTick) >= effectiveTimeout)
                    {
                        SoftAbortAndRestart("target cell did not validate within timeout");
                        return;
                    }
                    return;
                }

                case AutomationStage.VerificationComplete:
                    break;

                case AutomationStage.Failed:
                    break;
            }
        }

        private void TryRequestTimedPortalDuringRun(int upgrades, int now)
        {
            if (_portalRequestedThisRun) return;
            if (Hud.Game?.Me == null) return;
            if (Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal) return;
            if (_portalRequestedTick != int.MinValue && ElapsedMs(_portalRequestedTick) < 1500) return;

            int configuredAnchorRemaining = s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining();
            int effectiveAnchorRemaining = s7o_AutoGemUpgradeState.GetEffectivePortalAnchorRemaining(_initialUpgradeAttemptsThisRun);
            int effectiveDelayMs = s7o_AutoGemUpgradeState.GetFullPortalDelayMs();

            // Cleanup / below-threshold runs should immediately overlap TP again.
            // Example: configured 4R and reopen with 3/2/1 attempts remaining, or
            // configured 3R and reopen with 2/1 remaining. Do not wait for another
            // anchor click or configured delay in these recovery tails.
            if (s7o_AutoGemUpgradeState.IsBelowConfiguredPortalAnchorAtRunStart(_initialUpgradeAttemptsThisRun))
            {
                // Rev 5.6.3 polish: sleepAfter=0 overrides the default 10ms post-key wait.
                // The key has already been sent; the capture thread should not idle after the call.
                FreeHudInput.SendVirtualKey(FreeHudInput.VirtualKeyForTownPortal);
                _lastPortalActionTick = now;
                _portalRequestedTick = now;
                _portalRequestedThisRun = true;
                UpdateSharedDebugState("tp-request-below-threshold", "upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture), int.MinValue, int.MinValue);
                Log(() => "town portal requested: below-threshold immediate, upgrades=" + upgrades
                    + ", configuredAnchor=" + configuredAnchorRemaining.ToString(CultureInfo.InvariantCulture)
                    + ", initialAttempts=" + _initialUpgradeAttemptsThisRun.ToString(CultureInfo.InvariantCulture)
                    + ", effectiveAnchor=" + effectiveAnchorRemaining.ToString(CultureInfo.InvariantCulture)
                    + ", tpDelayMs=0");
                return;
            }

            if (_portalAnchorClickTick == int.MinValue)
                return;

            if (ElapsedMs(_portalAnchorClickTick) < effectiveDelayMs)
                return;

            // Rev 5.6.3 polish: sleepAfter=0 overrides the default 10ms post-key wait.
            // Saves ~10ms against the tight final-upgrade TP margin.
            FreeHudInput.SendVirtualKey(FreeHudInput.VirtualKeyForTownPortal);
            _lastPortalActionTick = now;
            _portalRequestedTick = now;
            _portalRequestedThisRun = true;
            UpdateSharedDebugState("tp-request", "upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture) + " delay=" + effectiveDelayMs.ToString(CultureInfo.InvariantCulture), int.MinValue, int.MinValue);
            Log(() => "town portal requested: upgrades=" + upgrades
                + ", configuredAnchor=" + configuredAnchorRemaining.ToString(CultureInfo.InvariantCulture)
                + ", effectiveAnchor=" + effectiveAnchorRemaining.ToString(CultureInfo.InvariantCulture)
                + ", anchorClickAgeMs=" + (_portalAnchorClickTick == int.MinValue ? -1 : ElapsedMs(_portalAnchorClickTick))
                + ", progressObserved=" + (_upgradeProgressObservedThisRun ? "1" : "0")
                + ", tpDelayMs=" + effectiveDelayMs.ToString(CultureInfo.InvariantCulture)
                + ", userTpDelayMs=" + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture)
                + ", lagBoost=" + (s7o_AutoGemUpgradeState.AutoGemTPLagBoost ? "1" : "0"));
        }

        private void HandleRunningState(int upgrades)
        {
            int now = NowTick();
            int buttonAnim = SafeAnimState(_upgradeButton);
            bool buttonVisible = _upgradeButton != null && _upgradeButton.Visible;
            bool loaded = SafeAnimState(_itemButton) != -1;

            if (_lastObservedUpgradeAttempts == int.MinValue)
            {
                _lastObservedUpgradeAttempts = upgrades;
                _initialUpgradeAttemptsThisRun = upgrades;
                _lastUpgradeProgressTick = now;
                _lastRecoveryUpgradeAttempts = int.MinValue;
                _upgradeProgressObservedThisRun = false;
                _noProgressAbortTick = int.MinValue;
                _portalAnchorClickTick = int.MinValue;
            }
            else if (upgrades != _lastObservedUpgradeAttempts)
            {
                Log(() => "upgrade progress: " + _lastObservedUpgradeAttempts + " -> " + upgrades + ", buttonAnim=" + buttonAnim + ", loaded=" + (loaded ? "1" : "0"), s7o_AutoGemUpgradeState.DebugLevelState);
                if (_initialUpgradeAttemptsThisRun != int.MinValue && upgrades < _initialUpgradeAttemptsThisRun)
                    _upgradeProgressObservedThisRun = true;
                _lastObservedUpgradeAttempts = upgrades;
                _lastUpgradeProgressTick = now;
                _lastRecoveryUpgradeAttempts = int.MinValue;
                _noProgressAbortTick = int.MinValue;
            }

            TryRequestTimedPortalDuringRun(upgrades, now);

            if (AutoPercentMode && upgrades > 0)
            {
                if (_autoAwaitingResolution)
                {
                    bool attemptConsumed = _autoUpgradeClickStartUpgrades != int.MinValue
                        && upgrades < _autoUpgradeClickStartUpgrades;

                    if (!attemptConsumed)
                    {
                        if (_hasSentInitialUpgradeClick
                            && !_upgradeProgressObservedThisRun
                            && _noProgressAbortTick != int.MinValue
                            && ElapsedMs(_noProgressAbortTick) >= 1600)
                        {
                            SoftAbortAndRestart("selected gem did not begin upgrading before timeout");
                            return;
                        }

                        return;
                    }

                    if (_autoAttemptResolvedTick == int.MinValue)
                    {
                        _autoAttemptResolvedTick = now;
                        _autoRetargetEarliestTick = now + 10;
                        TryPrepositionForPlannedTarget(upgrades, "auto");
                        Log(() => "auto resolve: "
                            + _autoUpgradeClickStartUpgrades.ToString(CultureInfo.InvariantCulture)
                            + " -> " + upgrades.ToString(CultureInfo.InvariantCulture)
                            + ", buttonAnim=" + buttonAnim
                            + ", loaded=" + (loaded ? "1" : "0"));

                        // Rev 5.6.9: cap-retarget fires immediately on attempt-consumed, before
                        // any button-state gate.  A capped gem's upgrade button gets stuck at 27
                        // permanently (it has no "ready" state to return to), so any wait on
                        // buttonAnim != 27 will never fire for cap cases.  We decide cap-retarget
                        // from ground-truth signals only: attempt counter dropped + gem at cap.
                        // The retarget's downstream selection validation handles the stale-text
                        // window via item-button anim change detection (see ValidateLoaded... fix).
                        if (TryHandleAutoSuccessNoReadyCapStop(upgrades))
                            return;

                        return;
                    }

                    bool retargetUiUnlocked = buttonAnim != 27;

                    if (now >= _autoRetargetEarliestTick
                        && !retargetUiUnlocked
                        && _autoAttemptResolvedTick != int.MinValue
                        && ElapsedMs(_autoAttemptResolvedTick) >= 1600)
                    {
                        _autoAwaitingResolution = false;
                        _autoUpgradeClickStartUpgrades = int.MinValue;
                        _autoAttemptResolvedTick = int.MinValue;
                        _autoRetargetEarliestTick = int.MinValue;
                        BeginCurrentTargetRecoveryFromRunning("auto retarget ui did not return to ready state after attempt");
                        return;
                    }
                    if (now < _autoRetargetEarliestTick || !retargetUiUnlocked)
                        return;

                    bool succeeded = WasLastUpgradeSuccessful();
                    if (succeeded && _target != null)
                    {
                        int currentAbs = _target.AbsoluteIndex;
                        if (currentAbs >= 0)
                        {
                            int confirmedRank = _autoValidationPreRank >= 0 ? (_autoValidationPreRank + 1) : GetLiveEffectiveRank(_target.Source);
                            int observedRank = GetObservedSelectedRankForCurrentTarget(_target.Source);
                            if (observedRank > confirmedRank)
                                confirmedRank = observedRank;
                            int prior;
                            if (!_autoConfirmedRankByAbs.TryGetValue(currentAbs, out prior) || confirmedRank > prior)
                                _autoConfirmedRankByAbs[currentAbs] = confirmedRank;
                        }
                    }

                    GemTarget plannedTarget;
                    bool havePlannedTarget = TryGetPlannedAutoTarget(upgrades, out plannedTarget, succeeded) && plannedTarget != null;
                    bool sameAsCurrent = havePlannedTarget && _target != null && plannedTarget.AbsoluteIndex == _target.AbsoluteIndex;
                    Log(() => "auto validation: success=" + (succeeded ? "1" : "0")
                        + ", same=" + (sameAsCurrent ? "1" : "0")
                        + ", plan='" + _autoPlanSummary + "'");

                    _autoAwaitingResolution = false;
                    _autoUpgradeClickStartUpgrades = int.MinValue;
                    _autoAttemptResolvedTick = int.MinValue;
                    _autoRetargetEarliestTick = int.MinValue;
                    _autoValidationPreRank = -1;

                    if (havePlannedTarget && !sameAsCurrent)
                    {
                        Log(() => "auto retarget planned: " + (_target != null ? _target.Name : "<none>") + " -> " + plannedTarget.Name + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture) + ", plan='" + _autoPlanSummary + "'");
                        BeginPlannedRetarget(plannedTarget);
                        return;
                    }
                }
            }


            if (IsLowestBalanceMode() && upgrades > 0)
            {
                if (_lowestAwaitingResolution)
                {
                    bool attemptConsumed = _lowestUpgradeClickStartUpgrades != int.MinValue
                        && upgrades < _lowestUpgradeClickStartUpgrades;

                    if (!attemptConsumed)
                    {
                        if (_hasSentInitialUpgradeClick
                            && !_upgradeProgressObservedThisRun
                            && _noProgressAbortTick != int.MinValue
                            && ElapsedMs(_noProgressAbortTick) >= 1600)
                        {
                            SoftAbortAndRestart("selected gem did not begin upgrading before timeout");
                            return;
                        }

                        return;
                    }

                    if (_lowestAttemptResolvedTick == int.MinValue)
                    {
                        _lowestAttemptResolvedTick = now;
                        _lowestRetargetEarliestTick = now + 10;
                        TryPrepositionForPlannedTarget(upgrades, "lowest");
                        Log(() => "lowest resolve: "
                            + _lowestUpgradeClickStartUpgrades.ToString(CultureInfo.InvariantCulture)
                            + " -> " + upgrades.ToString(CultureInfo.InvariantCulture)
                            + ", buttonAnim=" + buttonAnim
                            + ", loaded=" + (loaded ? "1" : "0"));

                        // Rev 5.6.9: see AUTO
                        if (TryHandleLowestSuccessNoReadyCapStop(upgrades))
                            return;

                        return;
                    }

                    bool retargetUiUnlocked = buttonAnim != 27;
                    if (now < _lowestRetargetEarliestTick || !retargetUiUnlocked)
                        return;

                    bool succeeded = WasLastUpgradeSuccessful();
                    if (succeeded)
                        _lowestPlanPointer++;

                    Log(() => "lowest validation: acd=" + _lowestValidationAcd.ToString(CultureInfo.InvariantCulture)
                        + ", rank=" + _lowestValidationPreRank.ToString(CultureInfo.InvariantCulture)
                        + ", success=" + (succeeded ? "1" : "0")
                        + ", ptr=" + _lowestPlanPointer.ToString(CultureInfo.InvariantCulture) + "/" + _lowestPlanSequence.Count.ToString(CultureInfo.InvariantCulture)
                        + ", plan='" + _lowestPlanSummary + "'");

                    _lowestAwaitingResolution = false;
                    _lowestUpgradeClickStartUpgrades = int.MinValue;
                    _lowestAttemptResolvedTick = int.MinValue;
                    _lowestRetargetEarliestTick = int.MinValue;
                    _lowestValidationAcd = 0;
                    _lowestValidationPreRank = -1;

                    GemTarget plannedTarget;
                    if (succeeded && TryGetLowestPlannedTarget(_lowestPlanPointer, out plannedTarget) && plannedTarget != null)
                    {
                        bool sameAsCurrent = _target != null && plannedTarget.AbsoluteIndex == _target.AbsoluteIndex;
                        if (!sameAsCurrent)
                        {
                            Log(() => "lowest retarget: " + (_target != null ? (_target.Name + "#" + _target.Rank + " a" + _target.AbsoluteIndex) : "<none>")
                                + " -> " + plannedTarget.Name + "#" + plannedTarget.Rank + " a" + plannedTarget.AbsoluteIndex
                                + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture)
                                + ", ptr=" + _lowestPlanPointer.ToString(CultureInfo.InvariantCulture) + "/" + _lowestPlanSequence.Count.ToString(CultureInfo.InvariantCulture)
                                + ", plan='" + _lowestPlanSummary + "'");
                            BeginPlannedRetarget(plannedTarget);
                            return;
                        }
                    }
                }
            }

            if (!AutoPercentMode && !IsLowestBalanceMode() && upgrades > 0)
            {
                if (_persistentAwaitingResolution)
                {
                    bool attemptConsumed = _persistentUpgradeClickStartUpgrades != int.MinValue
                        && upgrades < _persistentUpgradeClickStartUpgrades;

                    if (!attemptConsumed)
                    {
                        if (_hasSentInitialUpgradeClick
                            && !_upgradeProgressObservedThisRun
                            && _noProgressAbortTick != int.MinValue
                            && ElapsedMs(_noProgressAbortTick) >= 1600)
                        {
                            SoftAbortAndRestart("selected gem did not begin upgrading before timeout");
                            return;
                        }

                        return;
                    }

                    if (_persistentAttemptResolvedTick == int.MinValue)
                    {
                        _persistentAttemptResolvedTick = now;
                        _persistentRetargetEarliestTick = now + 10;
                        Log(() => "persistent resolve: "
                            + _persistentUpgradeClickStartUpgrades.ToString(CultureInfo.InvariantCulture)
                            + " -> " + upgrades.ToString(CultureInfo.InvariantCulture)
                            + ", buttonAnim=" + buttonAnim
                            + ", loaded=" + (loaded ? "1" : "0"));

                        // Rev 5.6.9: see AUTO
                        if (TryHandlePersistentSuccessNoReadyCapStop(upgrades))
                            return;

                        return;
                    }

                    bool retargetUiUnlocked = buttonAnim != 27;
                    if (now < _persistentRetargetEarliestTick || !retargetUiUnlocked)
                        return;

                    _persistentAwaitingResolution = false;
                    _persistentUpgradeClickStartUpgrades = int.MinValue;
                    _persistentAttemptResolvedTick = int.MinValue;
                    _persistentRetargetEarliestTick = int.MinValue;

                    GemTarget desiredTarget;
                    string modeWarning;
                    string modeFailure;
                    bool haveDesiredTarget = TryChoosePersistentModeTarget(upgrades, out desiredTarget, out modeWarning, out modeFailure, WasLastUpgradeSuccessful());
                    if (!haveDesiredTarget)
                    {
                        HandleNoEligibleTargetStop(modeWarning, modeFailure);
                        return;
                    }

                    bool sameAsCurrent = _target != null && desiredTarget.AbsoluteIndex == _target.AbsoluteIndex;
                    if (!sameAsCurrent)
                    {
                        Log(() => "persistent retarget planned: " + (_target != null ? _target.Name : "<none>") + " -> " + desiredTarget.Name + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture));
                        BeginPlannedRetarget(desiredTarget);
                        return;
                    }
                }
            }

            bool firstClickPending = !_hasSentInitialUpgradeClick;
            bool selectedReady = IsSelectedTargetReady(_target);
            if (selectedReady || (buttonVisible && loaded))
                _runningUiLossCount = 0;
            else if (upgrades > 0)
                _runningUiLossCount++;

            if (_runningUiLossCount >= 2
                && upgrades > 0
                && _target != null
                && _lastUpgradeProgressTick != int.MinValue
                && ElapsedMs(_lastUpgradeProgressTick) >= 250)
            {
                BeginCurrentTargetRecoveryFromRunning("upgrade UI lost for current target; reacquiring target viewport");
                return;
            }

            bool animReady = buttonAnim != 27;
            bool recoveryReady = !firstClickPending
                && _lastUpgradeProgressTick != int.MinValue
                && ElapsedMs(_lastUpgradeProgressTick) >= 75
                && upgrades != _lastRecoveryUpgradeAttempts;
            bool lowestHold = IsLowestBalanceMode() && _lowestAwaitingResolution;
            bool autoHold = AutoPercentMode && _autoAwaitingResolution;
            bool canClickUpgrade = buttonVisible
                && loaded
                && !lowestHold
                && !autoHold
                && ElapsedMs(_lastUpgradeClickTick) >= UpgradeClickDelayMs
                && (firstClickPending || animReady || IgnoreUpgradeButtonAnimGate || recoveryReady);

            if (canClickUpgrade)
            {
                ClickUi(_upgradeButton);
                _lastUpgradeClickTick = now;
                if (!_hasSentInitialUpgradeClick)
                {
                    _hasSentInitialUpgradeClick = true;
                    _firstUpgradeClickTick = now;
                    _noProgressAbortTick = now;
                }
                if (recoveryReady)
                    _lastRecoveryUpgradeAttempts = upgrades;
                int runAnchorRemaining = s7o_AutoGemUpgradeState.GetEffectivePortalAnchorRemaining(_initialUpgradeAttemptsThisRun == int.MinValue ? upgrades : _initialUpgradeAttemptsThisRun);
                if (_portalAnchorClickTick == int.MinValue && upgrades == runAnchorRemaining)
                {
                    _portalAnchorClickTick = now;
                    UpdateSharedDebugState("tp-anchor-start", "upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture) + " delay=" + s7o_AutoGemUpgradeState.GetFullPortalDelayMs().ToString(CultureInfo.InvariantCulture), _target != null ? _target.AbsoluteIndex : int.MinValue, int.MinValue);
                    Log(() => "portal anchor started: upgrades=" + upgrades
                        + ", anchorRemaining=" + runAnchorRemaining.ToString(CultureInfo.InvariantCulture)
                        + ", delayMs=" + s7o_AutoGemUpgradeState.GetFullPortalDelayMs().ToString(CultureInfo.InvariantCulture)
                        + ", userDelayMs=" + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture)
                        + ", lag=" + (s7o_AutoGemUpgradeState.AutoGemTPLagBoost ? "1" : "0"));
                }
                if (IsLowestBalanceMode())
                {
                    _lowestAwaitingResolution = true;
                    _lowestUpgradeClickStartUpgrades = upgrades;
                    _lowestAttemptResolvedTick = int.MinValue;
                    _lowestRetargetEarliestTick = int.MinValue;
                    _lowestValidationAcd = GetTargetSourceAcd(_target);
                    _lowestValidationPreRank = _target != null ? _target.Rank : -1;
                }
                if (AutoPercentMode)
                {
                    _autoAwaitingResolution = true;
                    _autoUpgradeClickStartUpgrades = upgrades;
                    _autoAttemptResolvedTick = int.MinValue;
                    _autoRetargetEarliestTick = int.MinValue;
                    _autoValidationPreRank = _target != null && _target.Source != null ? GetAutoEffectiveRank(_target.Source) : (_target != null ? _target.Rank : -1);
                }
                else if (!IsLowestBalanceMode())
                {
                    _persistentAwaitingResolution = true;
                    _persistentUpgradeClickStartUpgrades = upgrades;
                    _persistentAttemptResolvedTick = int.MinValue;
                    _persistentRetargetEarliestTick = int.MinValue;
                }
                Log(() => "click upgrade_button: upgrades=" + upgrades + ", anim=" + buttonAnim + ", recovery=" + (recoveryReady ? "1" : "0") + ", initial=" + (firstClickPending ? "1" : "0") + ", autoPending=" + (AutoPercentMode ? "1" : "0") + ", lowestPending=" + (IsLowestBalanceMode() ? "1" : "0") + ", persistentPending=" + ((!AutoPercentMode && !IsLowestBalanceMode()) ? "1" : "0"));
            }
            bool initialClickSent = _hasSentInitialUpgradeClick && _firstUpgradeClickTick != int.MinValue;
            if (initialClickSent)
                TryRequestTimedPortalDuringRun(upgrades, now);

            if (initialClickSent && !_upgradeProgressObservedThisRun && _noProgressAbortTick != int.MinValue && ElapsedMs(_noProgressAbortTick) >= 1600)
            {
                SoftAbortAndRestart("selected gem did not begin upgrading before timeout");
                return;
            }
        }


        private bool IsLowestBalanceMode()
        {
            return !AutoPercentMode
                && !FastFallbackMode
                && !PreferHighestNonMaxFirst
                && string.IsNullOrWhiteSpace(ForcedGemNameExact);
        }

        private bool WasLastUpgradeSuccessful()
        {
            string statusText = ReadText(_gemStatusText);
            if (!string.IsNullOrWhiteSpace(statusText)
                && statusText.IndexOf("Upgrade Succeeded", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string paneText = ReadText(_gemUpgradePane);
            return !string.IsNullOrWhiteSpace(paneText)
                && paneText.IndexOf("Upgrade Succeeded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int GetLowestBalanceCurrentEffectiveRank()
        {
            if (_target == null)
                return -1;

            int effectiveRank = _target.Rank;
            string observedSourceText;
            var observed = ReadCurrentSelectionEvidence(out observedSourceText);
            uint stableButtonAcd = GetStableItemButtonAcd();
            uint targetSourceAcd = GetTargetSourceAcd(_target);
            bool currentSelected = (targetSourceAcd != 0 && stableButtonAcd == targetSourceAcd)
                || (!string.IsNullOrWhiteSpace(observed.Item1)
                    && string.Equals(observed.Item1, _target.Name, StringComparison.OrdinalIgnoreCase));

            if (!currentSelected)
                return effectiveRank;

            if (!string.IsNullOrWhiteSpace(observed.Item1)
                && observed.Item2 >= 0
                && string.Equals(observed.Item1, _target.Name, StringComparison.OrdinalIgnoreCase))
            {
                effectiveRank = Math.Max(effectiveRank, observed.Item2);
            }

            if (WasLastUpgradeSuccessful())
                effectiveRank = Math.Max(effectiveRank, _target.Rank + 1);

            return effectiveRank;
        }

        private GemTarget GetLowestBalancePlannedTarget()
        {
            _orderedGems.Clear();
            _orderedGems.AddRange(BuildOrderedGemEntries());

            var candidates = _orderedGems
                .Where(g => g != null && g.Item != null)
                .Select(g => new
                {
                    Source = g,
                    Rank = g.Item.JewelRank,
                    EffectiveStopCap = g.EffectiveStopCap,
                    CanAttemptAt150Fallback = g.CanAttemptAt150Fallback,
                })
                .ToList();

            if (_target != null)
            {
                int effectiveCurrentRank = GetLowestBalanceCurrentEffectiveRank();
                if (effectiveCurrentRank >= 0)
                {
                    var activeCandidate = candidates.FirstOrDefault(c => c != null && c.Source != null && c.Source.AbsoluteIndex == _target.AbsoluteIndex);
                    if (activeCandidate != null && effectiveCurrentRank > activeCandidate.Rank)
                    {
                        candidates[candidates.IndexOf(activeCandidate)] = new
                        {
                            Source = activeCandidate.Source,
                            Rank = effectiveCurrentRank,
                            EffectiveStopCap = activeCandidate.EffectiveStopCap,
                            CanAttemptAt150Fallback = activeCandidate.CanAttemptAt150Fallback,
                        };
                    }
                }
            }

            var belowCap = candidates.Where(c => c.Rank < c.EffectiveStopCap).ToList();
            var chosenCandidate = belowCap.Count > 0
                ? belowCap.OrderBy(c => c.Rank).ThenBy(c => c.Source.AbsoluteIndex).FirstOrDefault()
                : candidates.Where(c => c.CanAttemptAt150Fallback).OrderBy(c => c.Source.AbsoluteIndex).FirstOrDefault();

            if (chosenCandidate == null)
                return null;

            return new GemTarget
            {
                Name = GetGemName(chosenCandidate.Source.Item),
                Rank = chosenCandidate.Rank,
                AbsoluteIndex = chosenCandidate.Source.AbsoluteIndex,
                Reason = chosenCandidate.Rank < chosenCandidate.EffectiveStopCap ? "lowest balance" : "FAST 150 fallback",
                Source = chosenCandidate.Source,
            };
        }


        private void BuildLowestPlan(int attemptsRemaining)
        {
            _lowestPlanSequence.Clear();
            _lowestPlanSummary = string.Empty;
            _lowestPlanPointer = 0;
            _lowestAwaitingResolution = false;
            _lowestUpgradeClickStartUpgrades = int.MinValue;
            _lowestAttemptResolvedTick = int.MinValue;
            _lowestRetargetEarliestTick = int.MinValue;
            _lowestValidationAcd = 0;
            _lowestValidationPreRank = -1;

            if (!IsLowestBalanceMode() || attemptsRemaining <= 0 || _orderedGems == null || _orderedGems.Count == 0)
                return;

            var simRanks = new Dictionary<int, int>();
            foreach (var gem in _orderedGems)
            {
                if (gem == null || gem.Item == null) continue;
                simRanks[gem.AbsoluteIndex] = gem.Item.JewelRank;
            }

            for (int i = 0; i < attemptsRemaining; i++)
            {
                GemOrderEntry bestBelowCap = null;
                GemOrderEntry bestFallback = null;

                foreach (var gem in _orderedGems)
                {
                    if (gem == null || gem.Item == null)
                        continue;

                    int simRank;
                    if (!simRanks.TryGetValue(gem.AbsoluteIndex, out simRank))
                        continue;

                    if (simRank < gem.EffectiveStopCap)
                    {
                        if (bestBelowCap == null
                            || simRank < simRanks[bestBelowCap.AbsoluteIndex]
                            || (simRank == simRanks[bestBelowCap.AbsoluteIndex] && gem.AbsoluteIndex < bestBelowCap.AbsoluteIndex))
                            bestBelowCap = gem;
                    }
                    else if (gem.CanAttemptAt150Fallback)
                    {
                        if (bestFallback == null || gem.AbsoluteIndex < bestFallback.AbsoluteIndex)
                            bestFallback = gem;
                    }
                }

                var chosen = bestBelowCap ?? bestFallback;
                if (chosen == null)
                    break;

                _lowestPlanSequence.Add(chosen.AbsoluteIndex);
                if (bestBelowCap != null)
                    simRanks[chosen.AbsoluteIndex] = simRanks[chosen.AbsoluteIndex] + 1;
            }

            if (_lowestPlanSequence.Count > 0)
            {
                var summaryParts = new List<string>();
                int start = 0;
                while (start < _lowestPlanSequence.Count)
                {
                    int abs = _lowestPlanSequence[start];
                    int count = 1;
                    while (start + count < _lowestPlanSequence.Count && _lowestPlanSequence[start + count] == abs)
                        count++;
                    var gem = _orderedGems.FirstOrDefault(g => g != null && g.AbsoluteIndex == abs);
                    string gemName = gem != null ? GetGemName(gem.Item) : ("a" + abs.ToString(CultureInfo.InvariantCulture));
                    summaryParts.Add(gemName + "x" + count.ToString(CultureInfo.InvariantCulture));
                    start += count;
                }
                _lowestPlanSummary = string.Join(" -> ", summaryParts);
            }
        }

        private bool TryGetLowestPlannedTarget(int planPointer, out GemTarget nextTarget)
        {
            nextTarget = null;
            if (!IsLowestBalanceMode() || _lowestPlanSequence.Count == 0)
                return false;
            if (planPointer < 0 || planPointer >= _lowestPlanSequence.Count)
                return false;

            int absIndex = _lowestPlanSequence[planPointer];
            var chosen = _orderedGems.FirstOrDefault(g => g != null && g.Item != null && g.AbsoluteIndex == absIndex);
            if (chosen == null)
                return false;

            nextTarget = new GemTarget
            {
                Name = GetGemName(chosen.Item),
                Rank = chosen.Item.JewelRank,
                AbsoluteIndex = chosen.AbsoluteIndex,
                Reason = string.IsNullOrWhiteSpace(_lowestPlanSummary) ? "lowest balance" : ("lowest balance: " + _lowestPlanSummary),
                Source = chosen,
            };
            return true;
        }

        private void PreparePlannedRetarget(GemTarget plannedTarget)
        {
            if (plannedTarget == null)
                return;

            _autoAwaitingResolution = false;
            _autoUpgradeClickStartUpgrades = int.MinValue;
            _autoAttemptResolvedTick = int.MinValue;
            _autoRetargetEarliestTick = int.MinValue;
            _autoValidationPreRank = -1;
            _lowestAwaitingResolution = false;
            _lowestUpgradeClickStartUpgrades = int.MinValue;
            _lowestAttemptResolvedTick = int.MinValue;
            _lowestRetargetEarliestTick = int.MinValue;
            _lowestValidationAcd = 0;
            _lowestValidationPreRank = -1;
            _persistentAwaitingResolution = false;
            _persistentUpgradeClickStartUpgrades = int.MinValue;
            _persistentAttemptResolvedTick = int.MinValue;
            _persistentRetargetEarliestTick = int.MinValue;
            _autoRunning = false;
            _target = plannedTarget;
            // Rev 5.6.3 (Finding A): re-seed target ACD from the planned gem's live inventory ACD
            // so the ACD-direct shortcut in DirectCaptureCurrentPage can fire on retargets, not just
            // on the first gem of a run.  Previous behavior (= 0) disabled the shortcut for every
            // retarget, forcing the stall-counter seek path even when the gem was already visible.
            _targetAcd = GetTargetSourceAcd(plannedTarget);
            ClearSelectedReadyLatch();
            ResetTargetRecoveryState();
            _lastUpgradeClickTick = int.MinValue;
            _portalAnchorClickTick = int.MinValue;
            _firstUpgradeClickTick = int.MinValue;
            _hasSentInitialUpgradeClick = false;
            _noProgressAbortTick = int.MinValue;
            _lastRecoveryUpgradeAttempts = int.MinValue;
            _targetComfortNudgeAttempts = 0;
            _wheelPostNudgeCorrectionPending = false;
            _wheelPostNudgeTargetAbs = -1;

            // reset direct adjacent commit flag for the upcoming target
            _directAdjacentStepDone = false;
        }

        private void BeginPlannedRetarget(GemTarget plannedTarget)
        {
            if (plannedTarget == null)
                return;

            PreparePlannedRetarget(plannedTarget);
            _stage = AutomationStage.Idle;
        }


        private void StartRunningFromConfirmedTarget()
        {
            string trustReason2;
            if (!IsPageTrustworthyForResolve(out trustReason2))
            {
                Log(() => "upgrade-block: " + trustReason2);
                SoftAbortAndRestart("refusing upgrade because page truth is not trustworthy");
                return;
            }

            if (_target != null)
                LatchSelectedReady(_target, GetStableItemButtonAcd());

            bool preserveRunningState = _preserveRunningStateOnReacquire;
            _preserveRunningStateOnReacquire = false;
            _capRetargetInProgress = false;  // Rev 5.6.9: clear on successful confirmation
            _capRetargetResolvedTick = int.MinValue;  // Rev 5.6.11: clear click-delay gate
            _capRetargetFirstClickPending = false;  // Rev 5.6.12
            _viewportRecoveryAttempts = 0;
            _runningUiLossCount = 0;

            _autoAwaitingResolution = false;
            _autoUpgradeClickStartUpgrades = int.MinValue;
            _autoAttemptResolvedTick = int.MinValue;
            _autoRetargetEarliestTick = int.MinValue;
            _autoValidationPreRank = -1;
            _persistentAwaitingResolution = false;
            _persistentUpgradeClickStartUpgrades = int.MinValue;
            _persistentAttemptResolvedTick = int.MinValue;
            _persistentRetargetEarliestTick = int.MinValue;
            _targetComfortNudgeAttempts = 0;
            _autoRunning = true;
            _stage = AutomationStage.Running;
            _lastUpgradeClickTick = int.MinValue;
            _portalAnchorClickTick = int.MinValue;

            // Reset immediate adjacent wheel step flag for this confirmed target run.
            _directAdjacentStepDone = false;

            if (preserveRunningState)
            {
                _firstUpgradeClickTick = int.MinValue;
                _hasSentInitialUpgradeClick = false;
                _noProgressAbortTick = int.MinValue;
                _lastRecoveryUpgradeAttempts = int.MinValue;
                Log(() => "Target re-confirmed. Resuming upgrade loop.");
                return;
            }

            _lastObservedUpgradeAttempts = GetUpgradeAttempts();
            _initialUpgradeAttemptsThisRun = _lastObservedUpgradeAttempts;
            _lastUpgradeProgressTick = NowTick();
            _portalAnchorClickTick = int.MinValue;
            bool preservePortalRequest = _portalRequestedThisRun;
            int preservePortalRequestedTick = _portalRequestedTick;
            int preserveLastPortalActionTick = _lastPortalActionTick;
            _lastPortalActionTick = preservePortalRequest ? preserveLastPortalActionTick : int.MinValue;
            _lastRecoveryUpgradeAttempts = int.MinValue;
            _portalRequestedTick = preservePortalRequest ? preservePortalRequestedTick : int.MinValue;
            _runningStartTick = _lastUpgradeProgressTick;
            _firstUpgradeClickTick = int.MinValue;
            _hasSentInitialUpgradeClick = false;
            _portalRequestedThisRun = preservePortalRequest;
            _upgradeProgressObservedThisRun = false;
            _noProgressAbortTick = int.MinValue;
            Log(() => "Target confirmed. Starting upgrade loop.");
        }

        private static int GetUpgradeChanceTier(int greaterRiftLevel, int gemRank)
        {
            if (greaterRiftLevel >= gemRank + 10) return 100;
            if (greaterRiftLevel == gemRank + 9) return 90;
            if (greaterRiftLevel == gemRank + 8) return 80;
            if (greaterRiftLevel == gemRank + 7) return 70;
            if (greaterRiftLevel >= gemRank) return 60;
            if (greaterRiftLevel == gemRank - 1) return 30;
            if (greaterRiftLevel == gemRank - 2) return 15;
            if (greaterRiftLevel == gemRank - 3) return 8;
            if (greaterRiftLevel == gemRank - 4) return 4;
            if (greaterRiftLevel == gemRank - 5) return 2;
            if (greaterRiftLevel == gemRank - 6) return 1;
            return 0;
        }


        private int GetCurrentGreaterRiftLevel()
        {
            try
            {
                if (Hud?.Game?.Me != null && Hud.Game.Me.InGreaterRift)
                    return (int)Hud.Game.Me.InGreaterRiftRank;
            }
            catch { }

            return 0;
        }

        
private GemOrderEntry ChooseAutoPercentGem(List<GemOrderEntry> gems, int greaterRiftLevel, out int chosenChance)
{
    chosenChance = -1;
    if (gems == null || gems.Count == 0) return null;

    var normalEligible = gems
        .Where(g => g != null && g.Item != null && g.BelowEffectiveStopCap)
        .ToList();

    if (normalEligible.Count > 0)
    {
        var atOrBelowRift = normalEligible
            .Where(g => g.Item.JewelRank <= greaterRiftLevel)
            .OrderByDescending(g => g.Item.JewelRank)
            .ThenBy(g => g.AbsoluteIndex)
            .ToList();

        if (atOrBelowRift.Count > 0)
        {
            var chosen = atOrBelowRift.First();
            chosenChance = GetUpgradeChanceTier(greaterRiftLevel, chosen.Item.JewelRank);
            return chosen;
        }

        var aboveRift = normalEligible
            .Select(g => new
            {
                Gem = g,
                Chance = GetUpgradeChanceTier(greaterRiftLevel, g.Item.JewelRank)
            })
            .ToList();

        int[] preferredTiers = { 30, 15, 8, 4, 2, 1, 0 };

        foreach (int tier in preferredTiers)
        {
            var match = aboveRift
                .Where(x => x.Chance == tier)
                .OrderByDescending(x => x.Gem.Item.JewelRank)
                .ThenBy(x => x.Gem.AbsoluteIndex)
                .Select(x => x.Gem)
                .FirstOrDefault();

            if (match != null)
            {
                chosenChance = tier;
                return match;
            }
        }
    }

    var fallback = gems
        .Where(g => g != null && g.Item != null && g.CanAttemptAt150Fallback)
        .OrderBy(g => g.AbsoluteIndex)
        .FirstOrDefault();

    if (fallback != null)
    {
        chosenChance = GetUpgradeChanceTier(greaterRiftLevel, fallback.Item.JewelRank);
        return fallback;
    }

    return null;
}


private GemOrderEntry ChooseSpecificSubModeTarget(List<GemOrderEntry> forcedMatches, bool usePostSuccessAwareRank, out int autoChosenChance, int excludeAbs = -1)
{
    autoChosenChance = -1;
    if (forcedMatches == null || forcedMatches.Count == 0) return null;

    bool subHighest = s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1;

    // When called from a cap-retarget path, excludeAbs is the just-capped gem's AbsoluteIndex.
    // This prevents re-selecting the same gem before HUD has updated its settled rank.
    // HIGHEST already uses IsStrictUpgradeEligible (post-success-aware) so it self-excludes;
    // AUTO uses BelowEffectiveStopCap (settled) which can see a just-capped gem as eligible.
    var candidates = (excludeAbs >= 0)
        ? forcedMatches.Where(g => g != null && g.Item != null && g.AbsoluteIndex != excludeAbs).ToList()
        : forcedMatches;

    if (subHighest)
    {
        // SPECIFIC + HIGHEST: same ordering as global HIGHEST, scoped to the name-filtered pool.
        var eligible = candidates
            .Where(g => g != null && g.Item != null && (usePostSuccessAwareRank ? IsStrictUpgradeEligible(g) : IsStrictUpgradeEligibleSettled(g)))
            .ToList();
        if (eligible.Count > 0)
            return eligible.OrderByDescending(g => GetPlannerEffectiveRank(g, usePostSuccessAwareRank)).ThenBy(g => g.AbsoluteIndex).First();
        // 150-fallback within name filter (excluding capped gem)
        return candidates.Where(g => g != null && g.Item != null && g.CanAttemptAt150Fallback).OrderBy(g => g.AbsoluteIndex).FirstOrDefault();
    }
    else
    {
        // SPECIFIC + AUTO: full AUTO GR-level/chance logic, scoped to the name-filtered pool.
        // Uses the same success-aware candidate model as global AUTO (BuildAutoPlan) so a
        // just-upgraded current gem is evaluated at its effective post-success rank before
        // HUD list text settles.  This prevents the stale-rank bug where a gem that just
        // succeeded 126→127 could win the AUTO tie-break again at its old rank and get
        // clicked a second time at the worse 30% chance tier.  Same chance-tier selection
        // as global AUTO but confined to duplicates of the chosen gem name — no fallback to
        // unrelated gems.  Rev 5.8.2.
        int grl = GetCurrentGreaterRiftLevel();

        var autoCandidates = candidates
            .Where(g => g != null && g.Item != null)
            .Select(g => new AutoPlanCandidate
            {
                Source = g,
                Name = GetGemName(g.Item),
                Rank = GetPlannerEffectiveRank(g, usePostSuccessAwareRank),
                HardCap = g.HardCap,
                EffectiveStopCap = g.EffectiveStopCap,
                CanAttemptAt150Fallback = g.CanAttemptAt150Fallback,
            })
            .ToList();

        return ChooseAutoPercentGemFromCandidates(autoCandidates, grl, out autoChosenChance);
    }
}

        private bool AcquireTarget()
        {
            _paneWarningMessage = string.Empty;
            _orderedGems.Clear();
            _orderedGems.AddRange(BuildOrderedGemEntries());
            if (_orderedGems.Count == 0)
            {
                Fail("no owned legendary gems were found by HUD");
                return false;
            }

            GemOrderEntry forced = null;
            bool specificMode = !string.IsNullOrWhiteSpace(ForcedGemNameExact);
            if (specificMode)
            {
                var forcedMatches = _orderedGems.Where(g => string.Equals(GetGemName(g.Item), ForcedGemNameExact.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (forcedMatches.Count == 0)
                {
                    _paneWarningMessage = "You do not have the selected specific gem.\nChoose a different gem.";
                    Fail("forced gem not found in owned Urshi order: " + ForcedGemNameExact.Trim());
                    return false;
                }

                forced = ChooseSpecificSubModeTarget(forcedMatches, false, out _);
                if (forced == null)
                {
                    string forcedName = ForcedGemNameExact.Trim();
                    bool forcedAtCap = forcedMatches.All(g =>
                        g.Item != null
                        && GetPostSuccessAwareEffectiveRank(g) >= Math.Max(0, g.EffectiveStopCap)
                        && !g.CanAttemptAt150Fallback);

                    string warningMsg;
                    if (string.Equals(forcedName, "Iceblink", StringComparison.OrdinalIgnoreCase)
                        && forcedMatches.All(g => g.Item != null && GetPlannerEffectiveRank(g, false) >= Math.Max(25, g.EffectiveStopCap)))
                    {
                        warningMsg = "Iceblink can't be upgraded past 25 automatically.\nUpgrade it manually.";
                    }
                    else if (forcedAtCap)
                    {
                        warningMsg = forcedName + " is max level.\nUpgrade attempts are not possible with this gem.\nChoose another gem or mode.";
                    }
                    else
                    {
                        // Gem exists but is not eligible under current rules
                        warningMsg = forcedName + " cannot be upgraded under current SPECIFIC mode rules.\n"
                            + "Choose a different gem or mode.";
                    }

                    // Route through HandleNoEligibleTargetStop so a TP-cancel is attempted
                    // if the portal is already active — Fail() alone does not do that.
                    HandleNoEligibleTargetStop(warningMsg,
                        "forced gem exists but is not eligible under current rules: " + forcedName);
                    return false;
                }
            }

            GemOrderEntry chosen = forced;
            int autoChosenChance = -1;

            if (chosen == null)
            {
                if (AutoPercentMode)
                {
                    GemTarget plannedTarget;
                    if (TryGetPlannedAutoTarget(GetUpgradeAttempts(), out plannedTarget) && plannedTarget?.Source != null)
                    {
                        chosen = plannedTarget.Source;
                        autoChosenChance = GetUpgradeChanceTier(GetCurrentGreaterRiftLevel(), chosen.Item.JewelRank);
                    }
                    else
                    {
                        int greaterRiftLevel = GetCurrentGreaterRiftLevel();
                        chosen = ChooseAutoPercentGem(_orderedGems, greaterRiftLevel, out autoChosenChance);
                    }
                }
                else if (FastFallbackMode)
                {
                    chosen = ChooseFirstVisibleBurnGem();
                }
                else if (IsLowestBalanceMode())
                {
                    BuildLowestPlan(GetUpgradeAttempts());
                    GemTarget plannedLowest;
                    if (TryGetLowestPlannedTarget(0, out plannedLowest) && plannedLowest?.Source != null)
                        chosen = plannedLowest.Source;
                }
                else
                {
                    var belowCap = _orderedGems.Where(g => g.BelowEffectiveStopCap).ToList();
                    if (belowCap.Count > 0)
                    {
                        chosen = PreferHighestNonMaxFirst
                            ? belowCap.OrderByDescending(g => g.Item.JewelRank).ThenBy(g => g.AbsoluteIndex).FirstOrDefault()
                            : belowCap.OrderBy(g => g.Item.JewelRank).ThenBy(g => g.AbsoluteIndex).FirstOrDefault();
                    }
                    else
                    {
                        chosen = ChooseFirstVisibleFallbackGem();
                    }
                }
            }

            if (chosen == null)
            {
                HandleNoEligibleTargetStop("No gems can be upgraded under current rules.\nChoose another mode or gem.", "no eligible target gem under current rules");
                return false;
            }

            _target = new GemTarget
            {
                Name = GetGemName(chosen.Item),
                Rank = chosen.Item.JewelRank,
                AbsoluteIndex = chosen.AbsoluteIndex,
                Reason = string.IsNullOrWhiteSpace(ForcedGemNameExact)
                    ? (AutoPercentMode
                        ? ("auto " + Math.Max(0, autoChosenChance).ToString(CultureInfo.InvariantCulture) + "%")
                        : (IsLowestBalanceMode()
                            ? (string.IsNullOrWhiteSpace(_lowestPlanSummary) ? "lowest balance" : ("lowest balance: " + _lowestPlanSummary))
                            : (chosen.BelowEffectiveStopCap
                                ? (PreferHighestNonMaxFirst ? "highest non-max" : "lowest non-max")
                                : (FastFallbackMode ? "FAST 150 fallback" : "FAST 150 fallback"))))
                    : ("forced name override " + (s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1 ? "HIGHEST" : "AUTO")),
                Source = chosen,
            };
            try { _targetAcd = (uint)chosen.Item.AcdId; } catch { _targetAcd = 0; }
            ClearSelectedReadyLatch();
            ResetTargetRecoveryState();

            _stage = AutomationStage.Idle;
            _lastFailureReason = string.Empty;
            _paneWarningMessage = string.Empty;
            _seenPageSignatures.Clear();
            _currentSnapshot = null;
            _resetScrollClicks = 0;
            _downScrollClicks = 0;
            _arrowScrollAttempts = 0;
            _lastArrowScrollDirection = 0;
            _virtualGrid = null;
            _absoluteGrid = null;
            _estimatedTopVisibleRow = -1;
            _viewportOriginRowFloat = -1f;
            _viewportOriginRowInt = -1;
            _viewportEpoch = 0;
            _lastGoodStackPanelTop = float.NaN;
            _lastMeasuredRowPitch = float.NaN;
            _lastMeasuredVisibleRowCount = 0;
            _currentProbeAbsoluteIndex = -1;
            _bottomNudgeAttempted = false;
            _usedViewportProbeFallbackThisRun = false;
            _preClickItemButtonAnim = -2;
            _targetComfortNudgeAttempts = 0;
            _autoRunning = false;
            return true;
        }

private sealed class AutoPlanCandidate
{
    public GemOrderEntry Source;
    public string Name;
    public int Rank;
    public int HardCap;
    public int EffectiveStopCap;
    public bool CanAttemptAt150Fallback;
}

private GemOrderEntry ChooseAutoPercentGemFromCandidates(List<AutoPlanCandidate> gems, int greaterRiftLevel, out int chosenChance)
{
    chosenChance = -1;
    if (gems == null || gems.Count == 0) return null;

    var normalEligible = gems
        .Where(g => g != null && g.Source != null && g.Rank < g.EffectiveStopCap)
        .ToList();

    if (normalEligible.Count > 0)
    {
        var atOrBelowRift = normalEligible
            .Where(g => g.Rank <= greaterRiftLevel)
            .OrderByDescending(g => g.Rank)
            .ThenBy(g => g.Source.AbsoluteIndex)
            .ToList();

        if (atOrBelowRift.Count > 0)
        {
            var chosen = atOrBelowRift.First();
            chosenChance = GetUpgradeChanceTier(greaterRiftLevel, chosen.Rank);
            return chosen.Source;
        }

        var aboveRift = normalEligible
            .Select(g => new
            {
                Gem = g,
                Chance = GetUpgradeChanceTier(greaterRiftLevel, g.Rank)
            })
            .ToList();

        int[] preferredTiers = { 30, 15, 8, 4, 2, 1, 0 };

        foreach (int tier in preferredTiers)
        {
            var match = aboveRift
                .Where(x => x.Chance == tier)
                .OrderByDescending(x => x.Gem.Rank)
                .ThenBy(x => x.Gem.Source.AbsoluteIndex)
                .Select(x => x.Gem.Source)
                .FirstOrDefault();

            if (match != null)
            {
                chosenChance = tier;
                return match;
            }
        }
    }

    var fallback = gems
        .Where(g => g != null && g.Source != null && g.CanAttemptAt150Fallback)
        .OrderBy(g => g.Source.AbsoluteIndex)
        .Select(g => g.Source)
        .FirstOrDefault();

    if (fallback != null)
    {
        chosenChance = GetUpgradeChanceTier(greaterRiftLevel, fallback.Item.JewelRank);
        return fallback;
    }

    return null;
}

private void RebuildAutoPlan(int attemptsRemaining, bool usePostSuccessAwareRank = false)
{
    _autoPlan.Clear();
    _autoPlanSummary = string.Empty;

    if (!AutoPercentMode || attemptsRemaining <= 0)
        return;

    int greaterRiftLevel = GetCurrentGreaterRiftLevel();
    if (greaterRiftLevel <= 0)
        return;

    var candidates = _orderedGems
        .Where(g => g != null && g.Item != null)
        .Select(g => new AutoPlanCandidate
        {
            Source = g,
            Name = GetGemName(g.Item),
            Rank = GetPlannerEffectiveRank(g, usePostSuccessAwareRank),
            HardCap = g.HardCap,
            EffectiveStopCap = g.EffectiveStopCap,
            CanAttemptAt150Fallback = g.CanAttemptAt150Fallback,
        })
        .ToList();

    for (int i = 0; i < attemptsRemaining; i++)
    {
        int chance;
        GemOrderEntry next = ChooseAutoPercentGemFromCandidates(candidates, greaterRiftLevel, out chance);
        if (next == null)
            break;

        var step = _autoPlan.LastOrDefault();
        string nextName = GetGemName(next.Item);
        if (step != null && string.Equals(step.Name, nextName, StringComparison.OrdinalIgnoreCase) && step.AbsoluteIndex == next.AbsoluteIndex)
        {
            step.Attempts++;
        }
        else
        {
            _autoPlan.Add(new AutoPlanStep
            {
                Name = nextName,
                AbsoluteIndex = next.AbsoluteIndex,
                Attempts = 1,
            });
        }

        var candidate = candidates.FirstOrDefault(c => c.Source != null && c.Source.AbsoluteIndex == next.AbsoluteIndex);
        if (candidate == null)
            break;

        if (candidate.Rank < candidate.EffectiveStopCap)
            candidate.Rank++;
    }

    if (_autoPlan.Count > 0)
        _autoPlanSummary = string.Join(" -> ", _autoPlan.Select(s => s.Name + "x" + s.Attempts.ToString(CultureInfo.InvariantCulture)));
}

private bool TryGetPlannedAutoTarget(int attemptsRemaining, out GemTarget nextTarget, bool usePostSuccessAwareRank = false)
{
    nextTarget = null;
    RebuildAutoPlan(attemptsRemaining, usePostSuccessAwareRank);
    if (_autoPlan.Count == 0)
        return false;

    AutoPlanStep first = _autoPlan[0];
    GemOrderEntry chosen = _orderedGems
        .Where(g => g != null && g.Item != null && g.AbsoluteIndex == first.AbsoluteIndex)
        .OrderByDescending(g => g.Item.JewelRank)
        .FirstOrDefault();

    if (chosen == null)
        return false;

    int chosenRank = GetPlannerEffectiveRank(chosen, usePostSuccessAwareRank);

    nextTarget = new GemTarget
    {
        Name = GetGemName(chosen.Item),
        Rank = chosenRank,
        AbsoluteIndex = chosen.AbsoluteIndex,
        Reason = "auto plan" + (string.IsNullOrWhiteSpace(_autoPlanSummary) ? string.Empty : ": " + _autoPlanSummary),
        Source = chosen,
    };
    return true;
}


private int GetObservedSelectedRankForCurrentTarget(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null || _target == null || entry.AbsoluteIndex != _target.AbsoluteIndex)
        return -1;

    string observedSourceText;
    var observed = ReadCurrentSelectionEvidence(out observedSourceText);
    if (!string.IsNullOrWhiteSpace(observed.Item1)
        && observed.Item2 >= 0
        && string.Equals(observed.Item1, _target.Name, StringComparison.OrdinalIgnoreCase))
        return observed.Item2;

    return -1;
}

private int GetLiveEffectiveRank(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return -1;

    int rank = entry.Item.JewelRank;
    int observedRank = GetObservedSelectedRankForCurrentTarget(entry);
    if (observedRank > rank)
        rank = observedRank;

    return rank;
}

private int GetPostSuccessAwareEffectiveRank(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return -1;

    int rank = GetLiveEffectiveRank(entry);
    if (rank < 0)
        rank = entry.Item.JewelRank;

    // After multiple consecutive successes on the same gem, _target.Rank is still the
    // rank at acquisition time, so _target.Rank+1 only reflects ONE success. Use the
    // confirmed-rank dict (populated after every consumed upgrade) for a more accurate
    // post-success rank when the gem has been upgraded more than once this run.
    int confirmed;
    if (entry.AbsoluteIndex >= 0
        && _autoConfirmedRankByAbs.TryGetValue(entry.AbsoluteIndex, out confirmed)
        && confirmed > rank)
    {
        rank = confirmed;
    }

    if (_target != null
        && entry.AbsoluteIndex == _target.AbsoluteIndex
        && WasLastUpgradeSuccessful())
    {
        rank = Math.Max(rank, _target.Rank + 1);
    }

    return rank;
}

private int GetPlannerEffectiveRank(GemOrderEntry entry, bool usePostSuccessAwareRank)
{
    int rank = usePostSuccessAwareRank ? GetPostSuccessAwareEffectiveRank(entry) : GetLiveEffectiveRank(entry);
    if (rank < 0 && entry != null && entry.Item != null)
        rank = entry.Item.JewelRank;
    return rank;
}

private bool IsBurnEligibleSettled(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return false;

    int rank = GetPlannerEffectiveRank(entry, false);
    return rank < entry.EffectiveStopCap || entry.CanAttemptAt150Fallback;
}

private bool IsStrictUpgradeEligibleSettled(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return false;

    int rank = GetPlannerEffectiveRank(entry, false);
    return rank < entry.EffectiveStopCap;
}

private int GetAutoEffectiveRank(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return -1;

    int rank = entry.Item.JewelRank;
    int delta;
    if (_autoConfirmedRankByAbs.TryGetValue(entry.AbsoluteIndex, out delta) && delta > rank)
        rank = delta;

    int observedRank = GetObservedSelectedRankForCurrentTarget(entry);
    if (observedRank > rank)
        rank = observedRank;

    return rank;
}

private uint SafeGemOrderEntryAcd(GemOrderEntry entry)
{
    try { return entry != null && entry.Item != null ? (uint)entry.Item.AcdId : 0u; }
    catch { return 0u; }
}

private GemOrderEntry ChooseFirstVisibleFallbackGem()
{
    var fallbackPool = _orderedGems
        .Where(g => g != null && g.Item != null && g.CanAttemptAt150Fallback)
        .OrderBy(g => g.AbsoluteIndex)
        .ToList();

    if (fallbackPool.Count == 0)
        return null;

    List<VisibleCell> visibleCells = null;
    if (_currentSnapshot != null && _currentSnapshot.VisibleCells != null && _currentSnapshot.VisibleCells.Count > 0)
    {
        visibleCells = _currentSnapshot.VisibleCells;
    }
    else
    {
        ViewportCapture cap;
        if (TryCaptureViewport(out cap) && cap.HasLiveCells)
            visibleCells = cap.LiveCells;
    }

    if (visibleCells != null && visibleCells.Count > 0)
    {
        foreach (var cell in visibleCells
            .Where(c => c != null && !c.IsProjected && c.Ref != null)
            .OrderBy(c => c.RowIndex)
            .ThenBy(c => c.ColumnIndex))
        {
            uint cellAcd = cell.Ref.CachedLegendaryGemAcdId;
            if (cellAcd == 0 || cellAcd == 0xFFFFFFFF)
                continue;

            var match = fallbackPool.FirstOrDefault(g => SafeGemOrderEntryAcd(g) == cellAcd);
            if (match != null)
                return match;
        }
    }

    return fallbackPool.FirstOrDefault();
}

private bool IsBurnEligible(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return false;

    int rank = GetPostSuccessAwareEffectiveRank(entry);
    if (rank < 0)
        rank = entry.Item.JewelRank;

    return rank < entry.EffectiveStopCap || entry.CanAttemptAt150Fallback;
}

private bool IsStrictUpgradeEligible(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return false;

    int rank = GetPostSuccessAwareEffectiveRank(entry);
    if (rank < 0)
        rank = entry.Item.JewelRank;

    return rank < entry.EffectiveStopCap;
}

private GemOrderEntry FindOrderedEntryForTarget(GemTarget target)
{
    if (target == null || _orderedGems == null || _orderedGems.Count == 0)
        return null;

    return _orderedGems.FirstOrDefault(g => g != null && g.Item != null && g.AbsoluteIndex == target.AbsoluteIndex);
}

private bool IsPortalActiveOrRequested()
{
    if (_portalRequestedThisRun)
        return true;
    try { return Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal; }
    catch { return false; }
}

private bool SafeUiVisible(IUiElement element)
{
    try { return element != null && element.Visible; }
    catch { return false; }
}

private void FocusSelectedGemAfterTeleportCancel()
{
    try
    {
        if (_itemButton != null && _itemButton.Visible)
        {
            RectangleF r = _itemButton.Rectangle;
            int x = (int)Math.Round(r.Left + (r.Width * 0.5f));
            int y = (int)Math.Round(r.Top + (r.Height * 0.5f));
            FreeHudInput.MouseMove(x, y);
            Log(() => "urshi-cancel: focused-selected-gem x=" + x.ToString(CultureInfo.InvariantCulture)
                + " y=" + y.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (_gemStatusText != null && _gemStatusText.Visible)
        {
            RectangleF s = _gemStatusText.Rectangle;
            int x = (int)Math.Round(s.Left + 24f);
            int y = (int)Math.Round(s.Bottom + 18f);
            FreeHudInput.MouseMove(x, y);
            Log(() => "urshi-cancel: focused-status-fallback x=" + x.ToString(CultureInfo.InvariantCulture)
                + " y=" + y.ToString(CultureInfo.InvariantCulture));
        }
    }
    catch { }
}


private bool TryCancelTeleportByTalkingToUrshi(string reason)
{
    if (!IsPortalActiveOrRequested())
        return false;

    Log(() => "urshi-cancel: " + (reason ?? "no eligible target"));

    Func<int, bool> tryClickUrshi = attempt =>
    {
        try
        {
            var urshi = Hud.Game != null && Hud.Game.Actors != null
                ? Hud.Game.Actors
                    .Where(x =>
                        x != null &&
                        x.SnoActor != null &&
                        x.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem &&
                        x.IsOnScreen)
                    .OrderBy(x =>
                    {
                        try
                        {
                            float dx = x.ScreenCoordinate.X - (Hud.Window.Size.Width * 0.5f);
                            float dy = x.ScreenCoordinate.Y - (Hud.Window.Size.Height * 0.5f);
                            return (dx * dx) + (dy * dy);
                        }
                        catch { return float.MaxValue; }
                    })
                    .FirstOrDefault()
                : null;

            if (urshi == null)
            {
                Log(() => "urshi-cancel: urshi not found/on-screen attempt=" + attempt.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            int ux = (int)Math.Round(urshi.ScreenCoordinate.X);
            int uy = (int)Math.Round(urshi.ScreenCoordinate.Y);

            if (ux <= 0 || uy <= 0 || ux >= Hud.Window.Size.Width || uy >= Hud.Window.Size.Height)
            {
                Log(() => "urshi-cancel: invalid screen point attempt=" + attempt.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            MarkAutomationInputAction();
            FreeHudInput.MouseMove(ux, uy);
            try { Thread.Sleep(20); } catch { }
            FreeHudInput.MouseDown(MouseButtons.Left);
            try { Thread.Sleep(30); } catch { }
            FreeHudInput.MouseUp(MouseButtons.Left);

            Log(() => "urshi-cancel: click attempt=" + attempt.ToString(CultureInfo.InvariantCulture)
                + " x=" + ux.ToString(CultureInfo.InvariantCulture)
                + " y=" + uy.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            Log(() => "urshi-cancel: click exception attempt=" + attempt.ToString(CultureInfo.InvariantCulture));
            return false;
        }
    };

    if (!tryClickUrshi(1))
        return false;

    for (int wait = 0; wait < 4; wait++)
    {
        try { Thread.Sleep(25); } catch { }
        if (!IsPortalActiveOrRequested())
        {
            FocusSelectedGemAfterTeleportCancel();
            return true;
        }
    }

    try { Thread.Sleep(100); } catch { }

    if (!IsPortalActiveOrRequested())
    {
        FocusSelectedGemAfterTeleportCancel();
        return true;
    }

    Log(() => "urshi-cancel: first click did not cancel teleport, trying once more");
    if (!tryClickUrshi(2))
        return false;

    for (int wait = 0; wait < 8; wait++)
    {
        try { Thread.Sleep(25); } catch { }
        if (!IsPortalActiveOrRequested())
        {
            FocusSelectedGemAfterTeleportCancel();
            return true;
        }
    }

    Log(() => "urshi-cancel: teleport did not cancel after two clicks");
    return false;
}

private void HandleNoEligibleTargetStop(string warningText, string failureReason)
{
    if (!string.IsNullOrWhiteSpace(warningText))
        _paneWarningMessage = warningText;

    string finalReason = string.IsNullOrWhiteSpace(failureReason)
        ? "no eligible target gem under current rules"
        : failureReason;

    bool portalWasActive = IsPortalActiveOrRequested();
    bool canceled = false;
    if (portalWasActive)
        canceled = TryCancelTeleportByTalkingToUrshi(finalReason);

    Log(() => "no-eligible-stop: portalActive=" + (portalWasActive ? "1" : "0") + ", canceled=" + (canceled ? "1" : "0") + ", reason=" + finalReason);
    Fail(finalReason);
}

private bool TryHandlePersistentSuccessNoReadyCapStop(int upgrades)
{
    if (_target == null || WasLastUpgradeSuccessful() == false)
        return false;

    _orderedGems.Clear();
    _orderedGems.AddRange(BuildOrderedGemEntries());
    var currentEntry = FindOrderedEntryForTarget(_target);
    if (currentEntry == null)
        return false;

    bool stillEligible = FastFallbackMode ? IsBurnEligible(currentEntry) : IsStrictUpgradeEligible(currentEntry);
    if (stillEligible)
        return false;

    // Rev 5.6.11: capture resolved tick for cap-retarget first-click delay gate.
    int capResolvedTick = _persistentAttemptResolvedTick;

    _persistentAwaitingResolution = false;
    _persistentUpgradeClickStartUpgrades = int.MinValue;
    _persistentAttemptResolvedTick = int.MinValue;
    _persistentRetargetEarliestTick = int.MinValue;

    GemTarget desiredTarget;
    string modeWarning;
    string modeFailure;
    // Pass the just-capped gem's AbsoluteIndex so ChooseSpecificSubModeTarget
    // excludes it even before HUD settles its rank (SPECIFIC+AUTO only — HIGHEST
    // self-excludes via IsStrictUpgradeEligible which is post-success-aware).
    int justCappedAbs = _target != null ? _target.AbsoluteIndex : -1;
    bool haveDesiredTarget = TryChoosePersistentModeTarget(upgrades, out desiredTarget, out modeWarning, out modeFailure, true, justCappedAbs);
    if (!haveDesiredTarget)
    {
        HandleNoEligibleTargetStop(modeWarning, modeFailure);
        return true;
    }

    bool sameAsCurrent = _target != null && desiredTarget.AbsoluteIndex == _target.AbsoluteIndex;
    if (!sameAsCurrent)
    {
        Log(() => "persistent no-ready cap retarget: " + (_target != null ? _target.Name : "<none>") + " -> " + desiredTarget.Name + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture));
        _capRetargetInProgress = true;
        _capRetargetFirstClickPending = true;
        _capRetargetResolvedTick = capResolvedTick;
        BeginPlannedRetarget(desiredTarget);
        return true;
    }

    return false;
}

private bool TryHandleAutoSuccessNoReadyCapStop(int upgrades)
{
    if (_target == null || WasLastUpgradeSuccessful() == false)
        return false;

    _orderedGems.Clear();
    _orderedGems.AddRange(BuildOrderedGemEntries());
    var currentEntry = FindOrderedEntryForTarget(_target);
    if (currentEntry == null)
        return false;

    int confirmedRank = _autoValidationPreRank >= 0
        ? (_autoValidationPreRank + 1)
        : Math.Max(_target.Rank + 1, GetLiveEffectiveRank(currentEntry));
    int prior;
    if (!_autoConfirmedRankByAbs.TryGetValue(currentEntry.AbsoluteIndex, out prior) || confirmedRank > prior)
        _autoConfirmedRankByAbs[currentEntry.AbsoluteIndex] = confirmedRank;

    bool stillEligible = GetAutoEffectiveRank(currentEntry) < currentEntry.EffectiveStopCap;
    if (stillEligible)
        return false;

    // Rev 5.6.11: capture the resolved tick before clearing it, for the cap-retarget
    // first-click delay gate.
    int capResolvedTick = _autoAttemptResolvedTick;

    _autoAwaitingResolution = false;
    _autoUpgradeClickStartUpgrades = int.MinValue;
    _autoAttemptResolvedTick = int.MinValue;
    _autoRetargetEarliestTick = int.MinValue;
    _autoValidationPreRank = -1;

    // First pass: normal AUTO planner
    GemTarget plannedTarget;
    bool havePlannedTarget = TryGetPlannedAutoTarget(upgrades, out plannedTarget, true) && plannedTarget != null;
    if (havePlannedTarget)
    {
        bool sameAsCurrent = _target != null && plannedTarget.AbsoluteIndex == _target.AbsoluteIndex;
        if (!sameAsCurrent)
        {
            Log(() => "auto no-ready cap retarget: " + (_target != null ? _target.Name : "<none>") + " -> " + plannedTarget.Name + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture) + ", plan='" + _autoPlanSummary + "'");
            _capRetargetInProgress = true;
            _capRetargetFirstClickPending = true;
            _capRetargetResolvedTick = capResolvedTick;
            BeginPlannedRetarget(plannedTarget);
            return true;
        }
    }

    // Second pass: success-aware AUTO fallback.
    // Re-evaluate the pool with post-success-aware ranks and explicitly exclude the
    // just-capped current target so AUTO cannot re-pick it from stale entry data.
    {
        int greaterRiftLevel = GetCurrentGreaterRiftLevel();
        int autoChosenChance;
        int excludeAbs = _target != null ? _target.AbsoluteIndex : -1;
        var fallbackCandidates = _orderedGems
            .Where(g => g != null && g.Item != null && g.AbsoluteIndex != excludeAbs)
            .Select(g => new AutoPlanCandidate
            {
                Source = g,
                Name = GetGemName(g.Item),
                Rank = GetPlannerEffectiveRank(g, true),
                HardCap = g.HardCap,
                EffectiveStopCap = g.EffectiveStopCap,
                CanAttemptAt150Fallback = g.CanAttemptAt150Fallback,
            })
            .ToList();
        GemOrderEntry fallbackEntry = ChooseAutoPercentGemFromCandidates(fallbackCandidates, greaterRiftLevel, out autoChosenChance);
        if (fallbackEntry != null)
        {
            var fallbackTarget = new GemTarget
            {
                Name          = GetGemName(fallbackEntry.Item),
                Rank          = GetPlannerEffectiveRank(fallbackEntry, true),
                AbsoluteIndex = fallbackEntry.AbsoluteIndex,
                Reason        = "auto fallback after cap",
                Source        = fallbackEntry,
            };
            Log(() => "auto no-ready cap fallback retarget: "
                + (_target != null ? _target.Name : "<none>") + " -> " + fallbackTarget.Name
                + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture)
                + ", chance=" + autoChosenChance.ToString(CultureInfo.InvariantCulture) + "%");
            _capRetargetInProgress = true;
            _capRetargetFirstClickPending = true;
            _capRetargetResolvedTick = capResolvedTick;
            BeginPlannedRetarget(fallbackTarget);
            return true;
        }
    }

    HandleNoEligibleTargetStop("No gems can be upgraded under current rules.\nChoose another mode or gem.", "no eligible target gem under current AUTO rules");
    return true;
}

private bool TryHandleLowestSuccessNoReadyCapStop(int upgrades)
{
    if (_target == null || WasLastUpgradeSuccessful() == false)
        return false;

    _orderedGems.Clear();
    _orderedGems.AddRange(BuildOrderedGemEntries());
    var currentEntry = FindOrderedEntryForTarget(_target);
    if (currentEntry == null)
        return false;

    int confirmedRank = _lowestValidationPreRank >= 0
        ? (_lowestValidationPreRank + 1)
        : Math.Max(_target.Rank + 1, GetLiveEffectiveRank(currentEntry));
    bool stillEligible = confirmedRank < currentEntry.EffectiveStopCap;
    if (stillEligible)
        return false;

    // Rev 5.6.11: capture resolved tick for cap-retarget first-click delay gate.
    int capResolvedTick = _lowestAttemptResolvedTick;

    _lowestPlanPointer++;
    _lowestAwaitingResolution = false;
    _lowestUpgradeClickStartUpgrades = int.MinValue;
    _lowestAttemptResolvedTick = int.MinValue;
    _lowestRetargetEarliestTick = int.MinValue;
    _lowestValidationAcd = 0;
    _lowestValidationPreRank = -1;

    GemTarget plannedTarget;
    if (TryGetLowestPlannedTarget(_lowestPlanPointer, out plannedTarget) && plannedTarget != null)
    {
        bool sameAsCurrent = _target != null && plannedTarget.AbsoluteIndex == _target.AbsoluteIndex;
        if (!sameAsCurrent)
        {
            Log(() => "lowest no-ready cap retarget: " + (_target != null ? _target.Name : "<none>") + " -> " + plannedTarget.Name + ", upgrades=" + upgrades.ToString(CultureInfo.InvariantCulture) + ", ptr=" + _lowestPlanPointer.ToString(CultureInfo.InvariantCulture) + "/" + _lowestPlanSequence.Count.ToString(CultureInfo.InvariantCulture));
            _capRetargetInProgress = true;
            _capRetargetFirstClickPending = true;
            _capRetargetResolvedTick = capResolvedTick;
            BeginPlannedRetarget(plannedTarget);
            return true;
        }
    }

    HandleNoEligibleTargetStop("No gems can be upgraded under current rules.\nChoose another mode or gem.", "no eligible target gem under current LOWEST rules");
    return true;
}

private GemOrderEntry ChooseFirstVisibleBurnGem()
{
    var eligiblePool = _orderedGems
        .Where(g => g != null && g.Item != null && IsBurnEligible(g))
        .OrderBy(g => g.AbsoluteIndex)
        .ToList();

    if (eligiblePool.Count == 0)
        return null;

    List<VisibleCell> visibleCells = null;
    if (_currentSnapshot != null && _currentSnapshot.VisibleCells != null && _currentSnapshot.VisibleCells.Count > 0)
    {
        visibleCells = _currentSnapshot.VisibleCells;
    }
    else
    {
        ViewportCapture cap;
        if (TryCaptureViewport(out cap) && cap.HasLiveCells)
            visibleCells = cap.LiveCells;
    }

    if (visibleCells != null && visibleCells.Count > 0)
    {
        foreach (var cell in visibleCells
            .Where(c => c != null && !c.IsProjected && c.Ref != null)
            .OrderBy(c => c.RowIndex)
            .ThenBy(c => c.ColumnIndex))
        {
            uint cellAcd = cell.Ref.CachedLegendaryGemAcdId;
            if (cellAcd == 0 || cellAcd == 0xFFFFFFFF)
                continue;

            var match = eligiblePool.FirstOrDefault(g => SafeGemOrderEntryAcd(g) == cellAcd);
            if (match != null)
                return match;
        }
    }

    return eligiblePool.FirstOrDefault();
}

private bool TryChoosePersistentModeTarget(int upgrades, out GemTarget nextTarget, out string warningText, out string failureReason, bool usePostSuccessAwareRank = false, int excludeSpecificAbs = -1)
{
    nextTarget = null;
    warningText = string.Empty;
    failureReason = string.Empty;

    _orderedGems.Clear();
    _orderedGems.AddRange(BuildOrderedGemEntries());
    if (_orderedGems.Count == 0)
    {
        failureReason = "no owned legendary gems were found by HUD";
        return false;
    }

    bool specificMode = !string.IsNullOrWhiteSpace(ForcedGemNameExact);
    GemOrderEntry chosen = null;

    if (specificMode)
    {
        string forcedName = ForcedGemNameExact.Trim();
        var forcedMatches = _orderedGems
            .Where(g => g != null && g.Item != null && string.Equals(GetGemName(g.Item), forcedName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (forcedMatches.Count == 0)
        {
            warningText = "You do not have the selected specific gem.\nChoose a different gem.";
            failureReason = "forced gem not found in owned Urshi order: " + forcedName;
            return false;
        }

        // excludeSpecificAbs is only set by the cap-retarget path, so SPECIFIC+AUTO
        // can avoid reselecting the just-capped gem before HUD rank text settles.
        chosen = ChooseSpecificSubModeTarget(forcedMatches, usePostSuccessAwareRank, out _, excludeSpecificAbs);
        if (chosen == null)
        {
            bool forcedAtCap = forcedMatches.All(g => g.Item != null && GetPlannerEffectiveRank(g, usePostSuccessAwareRank) >= Math.Max(0, g.EffectiveStopCap) && !g.CanAttemptAt150Fallback);
            if (string.Equals(forcedName, "Iceblink", StringComparison.OrdinalIgnoreCase)
                && forcedMatches.All(g => g.Item != null && GetPlannerEffectiveRank(g, false) >= Math.Max(25, g.EffectiveStopCap)))
            {
                warningText = "Iceblink can't be upgraded past 25 automatically.\nUpgrade it manually.";
            }
            else if (forcedAtCap)
            {
                warningText = forcedName + " is max level.\nUpgrade attempts are not possible with this gem.\nChoose another gem or mode.";
            }
            else
            {
                warningText = forcedName + " cannot be upgraded under current SPECIFIC mode rules.\nChoose a different gem or mode.";
            }

            failureReason = "forced gem exists but is not eligible under current rules: " + forcedName;
            return false;
        }
    }
    else if (FastFallbackMode)
    {
        if (_target != null)
        {
            var currentEntry = _orderedGems.FirstOrDefault(g => g != null && g.AbsoluteIndex == _target.AbsoluteIndex);
            if (usePostSuccessAwareRank ? IsBurnEligible(currentEntry) : IsBurnEligibleSettled(currentEntry))
                chosen = currentEntry;
        }

        if (chosen == null)
            chosen = ChooseFirstVisibleBurnGem();

        if (chosen == null)
        {
            warningText = "No gems can be upgraded under current rules.\nChoose another mode or gem.";
            failureReason = "no FAST 150-eligible gem under current rules";
            return false;
        }
    }
    else
    {
        var belowCap = _orderedGems
            .Where(g => g != null && g.Item != null && GetPlannerEffectiveRank(g, usePostSuccessAwareRank) < g.EffectiveStopCap)
            .ToList();

        if (belowCap.Count > 0)
        {
            chosen = PreferHighestNonMaxFirst
                ? belowCap.OrderByDescending(g => GetPlannerEffectiveRank(g, usePostSuccessAwareRank)).ThenBy(g => g.AbsoluteIndex).FirstOrDefault()
                : belowCap.OrderBy(g => GetPlannerEffectiveRank(g, usePostSuccessAwareRank)).ThenBy(g => g.AbsoluteIndex).FirstOrDefault();
        }
        else
        {
            chosen = ChooseFirstVisibleFallbackGem();
        }
    }

    if (chosen == null)
    {
        failureReason = "no eligible target gem under current rules";
        return false;
    }

    int chosenRank = FastFallbackMode || specificMode || PreferHighestNonMaxFirst ? GetPlannerEffectiveRank(chosen, usePostSuccessAwareRank) : chosen.Item.JewelRank;
    nextTarget = new GemTarget
    {
        Name = GetGemName(chosen.Item),
        Rank = Math.Max(chosen.Item.JewelRank, chosenRank),
        AbsoluteIndex = chosen.AbsoluteIndex,
        Reason = specificMode
            ? ("forced name override " + (s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1 ? "HIGHEST" : "AUTO"))
            : (FastFallbackMode
                ? "FAST 150 fallback"
                : (PreferHighestNonMaxFirst ? "highest non-max" : "lowest non-max")),
        Source = chosen,
    };
    return true;
}

private void ClearSelectedReadyLatch()
{
    _selectedReadyLatchedAcd = 0;
    _selectedReadyLatchedName = string.Empty;
    _selectedReadyLatchedRank = -1;
    _selectedReadyLatchedAbsoluteIndex = -1;
    _selectedReadyTick = int.MinValue;
}

private void ResetTargetRecoveryState()
{
    _viewportRecoveryAttempts = 0;
    _runningUiLossCount = 0;
    _preserveRunningStateOnReacquire = false;
}

private bool QueueViewportRecovery(string reason, int delayMs)
{
    const int maxViewportRecoveryAttempts = 2;
    if (_viewportRecoveryAttempts >= maxViewportRecoveryAttempts)
        return false;

    _viewportRecoveryAttempts++;
    _lastActionTick = NowTick();
    _afterScrollWait = Math.Max(0, delayMs);
    Log(() => "viewport recovery " + _viewportRecoveryAttempts + "/" + maxViewportRecoveryAttempts + ": " + reason);
    _stage = AutomationStage.DirectCaptureCurrentPage;
    return true;
}

private void BeginCurrentTargetRecoveryFromRunning(string reason)
{
    _autoRunning = false;
    _runningUiLossCount = 0;
    _viewportRecoveryAttempts = 0;
    _preserveRunningStateOnReacquire = true;
    _lastUpgradeClickTick = int.MinValue;
    _firstUpgradeClickTick = int.MinValue;
    _hasSentInitialUpgradeClick = false;
    _noProgressAbortTick = int.MinValue;
    _lastRecoveryUpgradeAttempts = int.MinValue;
    _lastActionTick = NowTick();
    _afterScrollWait = 0;
    Log(() => "running recovery: " + reason);
    _stage = AutomationStage.DirectCaptureCurrentPage;
}

private uint GetTargetSourceAcd(GemTarget target)
{
    try { return target != null && target.Source != null && target.Source.Item != null ? (uint)target.Source.Item.AcdId : 0u; }
    catch { return 0u; }
}

private void LatchSelectedReady(GemTarget target, uint stableButtonAcd)
{
    _selectedReadyTick = NowTick();
    _selectedReadyLatchedAcd = stableButtonAcd != 0 && stableButtonAcd != 0xFFFFFFFF ? stableButtonAcd : GetTargetSourceAcd(target);
    _selectedReadyLatchedName = target != null ? (target.Name ?? string.Empty) : string.Empty;
    _selectedReadyLatchedRank = target != null ? target.Rank : -1;
    _selectedReadyLatchedAbsoluteIndex = target != null ? target.AbsoluteIndex : -1;
}

private uint GetStableItemButtonAcd()
{
    uint acd = SafeItemButtonAcd();
    if (acd != 0 && acd != 0xFFFFFFFF)
    {
        _latchedItemButtonAcd = acd;
        _latchedItemButtonAcdTick = NowTick();
        return acd;
    }

    if (_latchedItemButtonAcd != 0 && _latchedItemButtonAcd != 0xFFFFFFFF && ElapsedMs(_latchedItemButtonAcdTick) <= 250)
        return _latchedItemButtonAcd;

    return acd;
}

private bool IsSelectedTargetReady(GemTarget target)
{
    if (target == null)
        return false;

    bool buttonVisible = _upgradeButton != null && _upgradeButton.Visible;
    if (!buttonVisible)
        return false;

    bool loaded = SafeAnimState(_itemButton) != -1;
    uint stableButtonAcd = GetStableItemButtonAcd();

    if (loaded)
    {
        uint targetSourceAcd = GetTargetSourceAcd(target);
        if (targetSourceAcd != 0 && stableButtonAcd == targetSourceAcd)
        {
            LatchSelectedReady(target, stableButtonAcd);
            return true;
        }

        if (_targetAcd != 0 && _targetAcd != 0xFFFFFFFF && stableButtonAcd == _targetAcd)
        {
            LatchSelectedReady(target, stableButtonAcd);
            return true;
        }

        string sourceText;
        var selection = ReadCurrentSelectionEvidence(out sourceText);
        if (!string.IsNullOrWhiteSpace(selection.Item1)
            && string.Equals(selection.Item1, target.Name, StringComparison.OrdinalIgnoreCase)
            && selection.Item2 == target.Rank)
        {
            LatchSelectedReady(target, stableButtonAcd);
            return true;
        }
    }

    bool latchFresh = _selectedReadyTick != int.MinValue && ElapsedMs(_selectedReadyTick) <= 250;
    if (!latchFresh)
        return false;

    bool sameNameRank = !string.IsNullOrWhiteSpace(_selectedReadyLatchedName)
        && string.Equals(_selectedReadyLatchedName, target.Name, StringComparison.OrdinalIgnoreCase)
        && _selectedReadyLatchedRank == target.Rank;
    bool sameAbsoluteIndex = _selectedReadyLatchedAbsoluteIndex >= 0
        && target != null
        && _selectedReadyLatchedAbsoluteIndex == target.AbsoluteIndex;
    bool sameAcd = _selectedReadyLatchedAcd != 0
        && _selectedReadyLatchedAcd != 0xFFFFFFFF
        && ((_targetAcd != 0 && _targetAcd != 0xFFFFFFFF && _selectedReadyLatchedAcd == _targetAcd)
            || _selectedReadyLatchedAcd == GetTargetSourceAcd(target));

    return sameAcd || (sameNameRank && sameAbsoluteIndex);
}

private bool CurrentSelectionMatchesTarget(GemTarget target)
{
    if (target == null)
        return false;

    if (IsSelectedTargetReady(target))
        return true;

    uint stableButtonAcd = GetStableItemButtonAcd();
    uint targetSourceAcd = GetTargetSourceAcd(target);
    if (targetSourceAcd != 0 && stableButtonAcd == targetSourceAcd)
        return true;

    string sourceText;
    var selection = ReadCurrentSelectionEvidence(out sourceText);
    return !string.IsNullOrWhiteSpace(selection.Item1)
        && string.Equals(selection.Item1, target.Name, StringComparison.OrdinalIgnoreCase)
        && selection.Item2 == target.Rank;
}

private List<GemOrderEntry> BuildOrderedGemEntries()

        {
            var ordered = new List<IItem>();

            try
            {
                ordered.AddRange(
                    Hud.Inventory.ItemsInInventory
                        .Where(IsLegendaryGem)
                        .OrderBy(i => i.InventoryY)
                        .ThenBy(i => i.InventoryX));
            }
            catch { }

            try
            {
                AddEquippedJewelryGemIfAny(ordered, ItemLocation.LeftRing);
                AddEquippedJewelryGemIfAny(ordered, ItemLocation.RightRing);
                AddEquippedJewelryGemIfAny(ordered, ItemLocation.Neck);
            }
            catch { }

            try
            {
                ordered.AddRange(
                    Hud.Game.Items
                        .Where(i => IsLegendaryGem(i)
                                    && i.Location == ItemLocation.Stash
                                    && i.InventoryX >= 0
                                    && i.InventoryY >= 0)
                        .OrderBy(i => i.InventoryY)
                        .ThenBy(i => i.InventoryX));
            }
            catch { }

            var result = new List<GemOrderEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in ordered)
            {
                if (item?.SnoItem == null)
                    continue;

                string key = BuildItemKey(item);
                if (!seen.Add(key))
                    continue;

                string name = GetGemName(item);
                int hardCap = GetHardCap(name);
                int stopCap = GetEffectiveStopCap(name, hardCap);
                bool belowStopCap = item.JewelRank < stopCap;
                bool canAttemptAt150 = !belowStopCap && stopCap == 150 && Allowed150Fallback.Contains(name);

                result.Add(new GemOrderEntry
                {
                    Item = item,
                    AbsoluteIndex = result.Count,
                    HardCap = hardCap,
                    EffectiveStopCap = stopCap,
                    BelowEffectiveStopCap = belowStopCap,
                    CanAttemptAt150Fallback = canAttemptAt150,
                });
            }

            if (ShouldLog(s7o_AutoGemUpgradeState.DebugLevelState))
            {
                string signature = string.Join(", ", result.Select(g => "a" + g.AbsoluteIndex + "=" + GetGemName(g.Item) + "#" + g.Item.JewelRank));
                if (!string.Equals(_lastGemOrderLogSignature, signature, StringComparison.Ordinal))
                {
                    _lastGemOrderLogSignature = signature;
                    // Compact summary always; full list only at verbose level
                    AppendDebugLine("gem order: count=" + result.Count
                        + " eligible=" + result.Count(g => IsStrictUpgradeEligible(g) || (FastFallbackMode && IsBurnEligible(g))));
                    if (ShouldLog(s7o_AutoGemUpgradeState.DebugLevelVerbose))
                        AppendDebugLine("gem order (" + result.Count + "): " + signature);
                }
            }
            return result;
        }

        private void AddEquippedJewelryGemIfAny(List<IItem> list, ItemLocation location)
        {
            try
            {
                var equippedItem = Hud.Game.Items.FirstOrDefault(i => i != null && i.Location == location);
                if (equippedItem?.ItemsInSocket != null)
                {
                    var socketedGem = equippedItem.ItemsInSocket.FirstOrDefault(IsLegendaryGem);
                    if (socketedGem != null)
                    {
                        list.Add(socketedGem);
                        return;
                    }
                }

                var directGem = Hud.Game.Items.Where(IsLegendaryGem).FirstOrDefault(i => i.Location == location);
                if (directGem != null)
                    list.Add(directGem);
            }
            catch { }
        }

        private void TryEnrichCellsFromDirectText(List<VisibleCell> cells)
        {
            if (cells == null || cells.Count == 0 || _orderedGems.Count == 0)
                return;

            var acdToEntry = new Dictionary<uint, GemOrderEntry>();
            foreach (var gem in _orderedGems)
            {
                if (gem?.Item == null) continue;
                try
                {
                    uint acd = (uint)gem.Item.AcdId;
                    if (acd != 0 && acd != 0xFFFFFFFF && !acdToEntry.ContainsKey(acd))
                        acdToEntry[acd] = gem;
                }
                catch { }
            }

            var rankToGems = new Dictionary<int, List<GemOrderEntry>>();
            foreach (var gem in _orderedGems)
            {
                if (gem?.Item == null) continue;
                int r = gem.Item.JewelRank;
                List<GemOrderEntry> bucket;
                if (!rankToGems.TryGetValue(r, out bucket))
                {
                    bucket = new List<GemOrderEntry>();
                    rankToGems[r] = bucket;
                }
                bucket.Add(gem);
            }

            var assignedIndices = new HashSet<int>();
            foreach (var c in cells)
            {
                if (c != null && c.AbsoluteIndex >= 0)
                    assignedIndices.Add(c.AbsoluteIndex);
            }

            int enriched = 0;
            foreach (var cell in cells)
            {
                if (cell == null || cell.AbsoluteIndex >= 0) continue;

                uint cellAcd = cell.Ref?.CachedLegendaryGemAcdId ?? 0u;
                bool hasAcd = (cellAcd != 0 && cellAcd != 0xFFFFFFFF);
                bool hasText = !string.IsNullOrWhiteSpace(cell.DirectText);
                if (!hasAcd && !hasText) continue;

                if (hasAcd)
                {
                    GemOrderEntry acdMatch;
                    if (acdToEntry.TryGetValue(cellAcd, out acdMatch))
                    {
                        if (!assignedIndices.Contains(acdMatch.AbsoluteIndex))
                        {
                            cell.AbsoluteIndex = acdMatch.AbsoluteIndex;
                            assignedIndices.Add(acdMatch.AbsoluteIndex);
                            string acdName = GetGemName(acdMatch.Item);
                            int acdRank = acdMatch.Item.JewelRank;
                            if (!_confirmedSlotMap.ContainsKey(acdMatch.AbsoluteIndex))
                            {
                                _confirmedSlotMap[acdMatch.AbsoluteIndex] = Tuple.Create(acdName, acdRank);
                                Log(() => "confirmed slot a" + acdMatch.AbsoluteIndex + " = " + acdName + "#" + acdRank + " (cell-acd=" + cellAcd + ", row=" + cell.RowIndex + " col=" + cell.ColumnIndex + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
                            }
                            else
                            {
                                Log(() => "cell-acd=" + cellAcd + " -> a" + acdMatch.AbsoluteIndex + " = " + acdName + "#" + acdRank + " (row=" + cell.RowIndex + " col=" + cell.ColumnIndex + ", slot already confirmed)", s7o_AutoGemUpgradeState.DebugLevelVerbose);
                            }
                            enriched++;
                            continue;
                        }
                        else
                        {
                            Log(() => "cell-acd=" + cellAcd + " matched a" + acdMatch.AbsoluteIndex + " but index already assigned — fallback to text parse (row=" + cell.RowIndex + " col=" + cell.ColumnIndex + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
                        }
                    }
                    else
                    {
                        Log(() => "cell-acd=" + cellAcd + " not in acdToEntry (orderedGems=" + _orderedGems.Count + ", acdMap=" + acdToEntry.Count + ") — fallback to text parse (row=" + cell.RowIndex + " col=" + cell.ColumnIndex + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
                    }
                }

                if (!hasText) continue;

                var identity = ParseGemIdentityFromText(cell.DirectText);
                if (!string.IsNullOrEmpty(identity.Item1) && identity.Item2 >= 0)
                {
                    var match = _orderedGems.FirstOrDefault(g =>
                        string.Equals(GetGemName(g.Item), identity.Item1, StringComparison.OrdinalIgnoreCase)
                        && g.Item.JewelRank == identity.Item2
                        && !assignedIndices.Contains(g.AbsoluteIndex));
                    if (match != null)
                    {
                        cell.AbsoluteIndex = match.AbsoluteIndex;
                        assignedIndices.Add(match.AbsoluteIndex);
                        if (!_confirmedSlotMap.ContainsKey(match.AbsoluteIndex))
                        {
                            _confirmedSlotMap[match.AbsoluteIndex] = Tuple.Create(identity.Item1, identity.Item2);
                            Log(() => "confirmed slot a" + match.AbsoluteIndex + " = " + identity.Item1 + "#" + identity.Item2 + " (directtext-name, row=" + cell.RowIndex + " col=" + cell.ColumnIndex + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
                        }
                        enriched++;
                        continue;
                    }
                }

                int rank = ExtractGemRank(cell.DirectText);
                if (rank >= 0 && _confirmedSlotMap.Count > 0)
                {
                    int estimatedAbs;
                    if (TryGetPredictedAbsoluteIndex(cell, out estimatedAbs))
                    {
                        Tuple<string, int> confirmed;
                        if (_confirmedSlotMap.TryGetValue(estimatedAbs, out confirmed) && confirmed.Item2 == rank
                            && !assignedIndices.Contains(estimatedAbs))
                        {
                            cell.AbsoluteIndex = estimatedAbs;
                            assignedIndices.Add(estimatedAbs);
                            enriched++;
                            continue;
                        }
                    }
                }

                if (rank < 0) continue;

                List<GemOrderEntry> candidates;
                if (!rankToGems.TryGetValue(rank, out candidates) || candidates.Count == 0) continue;

                var available = candidates.Where(g => !assignedIndices.Contains(g.AbsoluteIndex)).ToList();
                if (available.Count == 0) continue;

                GemOrderEntry chosen;
                if (available.Count == 1)
                {
                    chosen = available[0];
                }
                else
                {
                    int estimatedAbs2;
                    if (!TryGetPredictedAbsoluteIndex(cell, out estimatedAbs2)) continue;

                    GemOrderEntry confirmedCandidate = null;
                    foreach (var c in available)
                    {
                        Tuple<string, int> cEntry;
                        if (_confirmedSlotMap.TryGetValue(c.AbsoluteIndex, out cEntry) && cEntry.Item2 == rank
                            && Math.Abs(c.AbsoluteIndex - estimatedAbs2) <= 1)
                        {
                            confirmedCandidate = c;
                            break;
                        }
                    }
                    chosen = confirmedCandidate ?? available.OrderBy(g => Math.Abs(g.AbsoluteIndex - estimatedAbs2)).First();
                }

                cell.AbsoluteIndex = chosen.AbsoluteIndex;
                assignedIndices.Add(chosen.AbsoluteIndex);
                enriched++;
            }

            if (enriched > 0)
                Log(() => "direct-text enrichment: resolved " + enriched + " slot(s) (confirmedMap=" + _confirmedSlotMap.Count + ", acdMap=" + acdToEntry.Count + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
        }

        private void StartPageProbe(ProbeReason reason)
        {
            if (_gemUpgradePane?.Visible != true)
                return;

            RectangleF paneRect;
            try
            {
                paneRect = _gemUpgradePane.Rectangle;
            }
            catch
            {
                Fail("could not read gem pane rectangle");
                return;
            }

            RectangleF listBounds = GetAuthoritativeGemListBounds(paneRect);
            var visibleCells = GetMappedVisibleCells(listBounds);
            if (visibleCells.Count == 0)
            {
                Fail("no visible Urshi cells were detected inside list bounds");
                return;
            }

            RebuildVirtualGrid(listBounds, visibleCells);

            if (reason == ProbeReason.Search && _target != null)
            {
                TryEnrichCellsFromDirectText(visibleCells);
                VisibleCell targetVC = visibleCells.FirstOrDefault(
                    c => c != null && !c.IsProjected && c.Ref != null && c.AbsoluteIndex == _target.AbsoluteIndex);
                if (targetVC == null && _target.Source?.Item != null)
                {
                    uint tAcd = (uint)_target.Source.Item.AcdId;
                    if (tAcd != 0 && tAcd != 0xFFFFFFFF)
                        targetVC = visibleCells.FirstOrDefault(
                            c => c != null && !c.IsProjected && c.Ref != null && c.Ref.CachedLegendaryGemAcdId == tAcd);
                }
                if (targetVC != null)
                {
                    Log(() => "probe shortcut: target " + _target.Name + "#" + _target.Rank + " visible at a" + targetVC.AbsoluteIndex + ", skipping probe");
                    RefreshSnapshotFromViewportCapture(new ViewportCapture
                    {
                        HasPane = true,
                        HasListBounds = true,
                        HasScrollLane = true,
                        HasLiveCells = visibleCells != null && visibleCells.Count > 0,
                        PaneRect = paneRect,
                        ListBounds = listBounds,
                        ScrollLaneRect = GetAuthoritativeScrollLane(paneRect, listBounds),
                        LiveCells = visibleCells != null ? new List<VisibleCell>(visibleCells.Where(c => c != null && !c.IsProjected)) : new List<VisibleCell>(),
                    });
                    _currentSnapshot.TargetCell = new ObservedCell
                    {
                        VisibleCell = targetVC,
                        SelectedGemName = _target.Name,
                        SelectedGemRank = _target.Rank,
                        SourceText = "acd-shortcut",
                        MatchTarget = true,
                        ItemButtonLoaded = SafeAnimState(_itemButton) != -1,
                        UpgradeButtonAnimState = SafeAnimState(_upgradeButton),
                    ViewportEpoch = _viewportEpoch,
                    };
                    _stage = AutomationStage.SelectObservedTarget;
                    return;
                }
            }

            var probeCells = BuildProbeCellsForCurrentViewport(listBounds, visibleCells);
            var scrollPoints = GetScrollPoints(paneRect, listBounds, visibleCells);
            _probeReason = reason;
            _probeCells = visibleCells != null ? new List<VisibleCell>(visibleCells) : new List<VisibleCell>();
            _probeSnapshot = new ObservedPageSnapshot
            {
                PaneRect = paneRect,
                ListBounds = listBounds,
                ScrollUpPoint = scrollPoints.Item1,
                ScrollDownPoint = scrollPoints.Item2,
                Reason = reason,
                VisibleCells = visibleCells,
                LiveVisibleCells = visibleCells,
                InferredViewportCells = probeCells,
            };
            _probeIndex = 0;
            _probePendingCell = null;
            _probeWaitingForValidation = false;
            _probeActionTick = int.MinValue;
            _probeNoIdentityRetryCount = 0;
            _probeActive = true;

            Log(() => "start probe " + reason.ToString().ToLowerInvariant() + ": visibleCells=" + visibleCells.Count + ", topRow=" + GetAuthoritativeViewportTopRow() + ", paneRect=" + ((int)paneRect.Left) + "," + ((int)paneRect.Top) + "," + ((int)paneRect.Width) + "x" + ((int)paneRect.Height) + ", listBounds=" + ((int)listBounds.Left) + "," + ((int)listBounds.Top) + "," + ((int)listBounds.Width) + "x" + ((int)listBounds.Height) + (_virtualGrid != null ? (", rowPitch=" + ((int)Math.Round(_virtualGrid.RowPitch)) + ", visRows=" + _virtualGrid.VisibleRowCount) : string.Empty));
            LogViewportMetrics("probe-start");
            LogScrollLane("probe-start", paneRect, listBounds);

            if (reason == ProbeReason.Reset)
                _stage = AutomationStage.ResetProbeCurrentPage;
            else if (reason == ProbeReason.Search)
                _stage = AutomationStage.SearchProbeCurrentPage;
        }

        private List<VisibleCell> BuildProbeCellsForCurrentViewport(RectangleF listBounds, List<VisibleCell> liveVisible)
        {
            var result = new List<VisibleCell>();
            if (liveVisible != null)
                result.AddRange(liveVisible.Where(c => c != null));

            if (_absoluteGrid == null || _absoluteGrid.Slots.Count == 0)
                return result;

            var seen = new HashSet<string>(
                result.Where(c => c != null)
                      .Select(c => c.RowIndex.ToString(CultureInfo.InvariantCulture) + ":" + c.ColumnIndex.ToString(CultureInfo.InvariantCulture)),
                StringComparer.Ordinal);

            foreach (var slot in _absoluteGrid.Slots)
            {
                if (!slot.IntersectsViewport)
                    continue;

                int localRow = slot.AbsoluteRow - _absoluteGrid.ViewportTopRowInt;
                if (localRow < 0 || localRow >= Math.Max(1, _absoluteGrid.VisibleRowCount))
                    continue;

                string key = localRow.ToString(CultureInfo.InvariantCulture) + ":" + slot.AbsoluteCol.ToString(CultureInfo.InvariantCulture);
                if (seen.Contains(key))
                    continue;

                RectangleF clipped = RectangleF.Intersect(slot.PredictedRect, listBounds);
                if (clipped.Width < Math.Max(10f, _absoluteGrid.CellWidth * 0.25f) ||
                    clipped.Height < Math.Max(10f, _absoluteGrid.CellHeight * 0.20f))
                    continue;

                result.Add(new VisibleCell
                {
                    Ref = null,
                    Rect = clipped,
                    RowIndex = localRow,
                    ColumnIndex = slot.AbsoluteCol,
                    DirectText = string.Empty,
                    FamilyTag = "proj:" + localRow.ToString(CultureInfo.InvariantCulture) + "." + slot.AbsoluteCol.ToString(CultureInfo.InvariantCulture),
                    IsProjected = true,
                    AbsoluteIndex = slot.AbsoluteIndex,
                });

                seen.Add(key);
            }

            result.Sort(delegate (VisibleCell a, VisibleCell b)
            {
                float dy = Math.Abs(a.Rect.Y - b.Rect.Y);
                if (dy > RowClusterTolerancePx)
                    return a.Rect.Y.CompareTo(b.Rect.Y);
                return a.Rect.X.CompareTo(b.Rect.X);
            });

            return result;
        }

        private RectangleF ChooseStableGridAnchor(RectangleF listBounds, List<VisibleCell> liveCells)
        {
            float fallbackW = _absoluteGrid != null && _absoluteGrid.CellWidth > 1f ? _absoluteGrid.CellWidth : 58f;
            float fallbackH = _absoluteGrid != null && _absoluteGrid.CellHeight > 1f ? _absoluteGrid.CellHeight : 58f;
            RectangleF liveAnchor = RectangleF.Empty;
            if (liveCells != null && liveCells.Count > 0)
            {
                var first = liveCells.OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).First();
                liveAnchor = first.Rect;
            }
            if (_stableGridAnchorRect == RectangleF.Empty)
            {
                if (liveAnchor != RectangleF.Empty)
                {
                    _stableGridAnchorRect = liveAnchor;
                    return _stableGridAnchorRect;
                }
                _stableGridAnchorRect = new RectangleF(listBounds.Left + 8f, listBounds.Top + 8f, fallbackW, fallbackH);
                return _stableGridAnchorRect;
            }
            if (liveAnchor != RectangleF.Empty)
            {
                float dx = liveAnchor.Left - _stableGridAnchorRect.Left;
                float dy = liveAnchor.Top - _stableGridAnchorRect.Top;
                if (Math.Abs(dx) <= LiveAnchorSnapThresholdPx && Math.Abs(dy) <= LiveAnchorSnapThresholdPx)
                {
                    _stableGridAnchorRect = new RectangleF(liveAnchor.Left, liveAnchor.Top, liveAnchor.Width > 1f ? liveAnchor.Width : _stableGridAnchorRect.Width, liveAnchor.Height > 1f ? liveAnchor.Height : _stableGridAnchorRect.Height);
                }
                else
                {
                    _stableGridAnchorRect = new RectangleF(_stableGridAnchorRect.Left + dx * 0.35f, _stableGridAnchorRect.Top + dy * 0.35f, liveAnchor.Width > 1f ? liveAnchor.Width : _stableGridAnchorRect.Width, liveAnchor.Height > 1f ? liveAnchor.Height : _stableGridAnchorRect.Height);
                }
            }
            return _stableGridAnchorRect;
        }

        private void RebuildAbsoluteGrid(RectangleF listBounds, List<VisibleCell> liveCells)
        {
            int totalSlots = Math.Max(_orderedGems != null ? _orderedGems.Count : 0, Math.Max(_target != null ? _target.AbsoluteIndex + 1 : 0, _highestNativeAbsoluteIndexSeen + 1));
            if (totalSlots <= 0)
            {
                _absoluteGrid = null;
                return;
            }

            if (_absoluteGrid == null)
                _absoluteGrid = new AbsoluteGridModel();

            int totalRows = (int)Math.Ceiling(totalSlots / 5.0);
            int visibleRows = Math.Max(1, GetAuthoritativeViewportVisibleRowCount());
            int maxTop = Math.Max(0, totalRows - visibleRows);

            float clampedTopFloat = Math.Max(0f, Math.Min(maxTop, _viewportOriginRowFloat));
            int clampedTopInt = Math.Max(0, Math.Min(maxTop, _viewportOriginRowInt));

            _absoluteGrid.TotalSlotCount = totalSlots;
            _absoluteGrid.TotalRowCount = totalRows;
            _absoluteGrid.VisibleRowCount = visibleRows;
            _absoluteGrid.ViewportTopRowFloat = clampedTopFloat;
            _absoluteGrid.ViewportTopRowInt = clampedTopInt;
            _absoluteGrid.ListBounds = listBounds;

            float rowPitch = _lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : 70f;
            float colPitch = _lastMeasuredColumnPitch > 1f ? _lastMeasuredColumnPitch : 70f;
            float cellW = 58f;
            float cellH = _lastMeasuredCellHeight > 1f ? _lastMeasuredCellHeight : 58f;

            if (liveCells != null && liveCells.Count > 0)
            {
                var first = liveCells.OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).First();
                cellW = first.Rect.Width > 1f ? first.Rect.Width : cellW;
                cellH = first.Rect.Height > 1f ? first.Rect.Height : cellH;
            }

            RectangleF anchor = ChooseStableGridAnchor(listBounds, liveCells);
            if (anchor == RectangleF.Empty)
                anchor = new RectangleF(listBounds.Left + 8f, listBounds.Top + 8f, cellW, cellH);

            _absoluteGrid.AnchorRect = anchor;
            _absoluteGrid.RowPitch = rowPitch;
            _absoluteGrid.ColumnPitch = colPitch;
            _absoluteGrid.CellWidth = cellW;
            _absoluteGrid.CellHeight = cellH;

            _absoluteGrid.Slots.Clear();

            for (int abs = 0; abs < totalSlots; abs++)
            {
                int absRow = abs / 5;
                int absCol = abs % 5;
                float localRowFloat = absRow - _absoluteGrid.ViewportTopRowFloat;

                RectangleF predicted = new RectangleF(
                    anchor.Left + absCol * colPitch,
                    anchor.Top + localRowFloat * rowPitch,
                    cellW,
                    cellH);

                RectangleF ix = RectangleF.Intersect(predicted, listBounds);
                bool intersects = ix.Width > 0f && ix.Height > 0f;

                _absoluteGrid.Slots.Add(new AbsoluteGridSlot
                {
                    AbsoluteIndex = abs,
                    AbsoluteRow = absRow,
                    AbsoluteCol = absCol,
                    PredictedRect = predicted,
                    IntersectsViewport = intersects,
                    HasLiveCell = false,
                    LiveCell = null,
                });
            }

            if (liveCells != null)
            {
                foreach (var live in liveCells)
                {
                    if (live == null)
                        continue;

                    int absRow = _absoluteGrid.ViewportTopRowInt + live.RowIndex;
                    int absIndex = absRow * 5 + live.ColumnIndex;
                    if (absIndex < 0 || absIndex >= _absoluteGrid.Slots.Count)
                        continue;

                    var slot = _absoluteGrid.Slots[absIndex];
                    slot.HasLiveCell = true;
                    slot.LiveCell = live;
                    slot.PredictedRect = live.Rect;
                }
            }
        }

        private void UpdateTrackedLiveCells(List<VisibleCell> liveCells)
        {
            if (liveCells == null || _absoluteGrid == null) return;
            int now = Environment.TickCount;
            float stackTop = GetCurrentStackPanelTop();
            foreach (var live in liveCells)
            {
                if (live == null || live.IsProjected) continue;
                int absIndex = (_viewportOriginRowInt + live.RowIndex) * 5 + live.ColumnIndex;
                if (absIndex < 0) continue;
                TrackedLiveCell t;
                if (!_trackedLiveCells.TryGetValue(absIndex, out t))
                {
                    t = new TrackedLiveCell();
                    _trackedLiveCells[absIndex] = t;
                }
                t.AbsoluteIndex = absIndex;
                t.AcdId = live.Ref != null ? live.Ref.CachedLegendaryGemAcdId : 0;
                t.LastRect = live.Rect;
                t.LastStackTop = stackTop;
                t.LastSeenTick = now;
                t.ConfirmedLive = true;
            }
            foreach (var k in _trackedLiveCells.Where(kv => now - kv.Value.LastSeenTick > _trackedLiveTtlMs).Select(kv => kv.Key).ToList())
                _trackedLiveCells.Remove(k);
        }

        private List<VisibleCell> GetTrackedProjectedCells()
        {
            var result = new List<VisibleCell>();
            if (_absoluteGrid == null || _trackedLiveCells.Count == 0) return result;
            float stackTop = GetCurrentStackPanelTop();
            if (float.IsNaN(stackTop)) return result;
            foreach (var kv in _trackedLiveCells)
            {
                var t = kv.Value;
                if (t == null || t.AbsoluteIndex < 0 || t.AbsoluteIndex >= _absoluteGrid.Slots.Count) continue;
                var slot = _absoluteGrid.Slots[t.AbsoluteIndex];
                RectangleF baseRect = slot.PredictedRect;
                if (baseRect == RectangleF.Empty) continue;
                float dy = !float.IsNaN(t.LastStackTop) ? (stackTop - t.LastStackTop) : 0f;
                RectangleF r = new RectangleF(baseRect.Left, baseRect.Top + dy, baseRect.Width, baseRect.Height);
                RectangleF ix = RectangleF.Intersect(r, _absoluteGrid.ListBounds);
                if (ix.Width <= 0f || ix.Height <= 0f) continue;
                result.Add(new VisibleCell { Ref = null, Rect = ix, RowIndex = slot.AbsoluteRow - _viewportOriginRowInt, ColumnIndex = slot.AbsoluteCol, AbsoluteIndex = slot.AbsoluteIndex, IsProjected = true, DirectText = string.Empty, FamilyTag = "tracked:" + slot.AbsoluteRow.ToString(CultureInfo.InvariantCulture) + "." + slot.AbsoluteCol.ToString(CultureInfo.InvariantCulture) });
            }
            return result;
        }

        private float GetCurrentAlignmentErrorPx()
        {
            if (_absoluteGrid == null || _currentSnapshot == null || _currentSnapshot.LiveVisibleCells == null) return 0f;
            float worst = 0f; int matched = 0;
            foreach (var live in _currentSnapshot.LiveVisibleCells)
            {
                if (live == null || live.IsProjected) continue;
                int absIndex = (_viewportOriginRowInt + live.RowIndex) * 5 + live.ColumnIndex;
                if (absIndex < 0 || absIndex >= _absoluteGrid.Slots.Count) continue;
                var slot = _absoluteGrid.Slots[absIndex];
                worst = Math.Max(worst, Math.Abs(live.Rect.Top - slot.PredictedRect.Top));
                worst = Math.Max(worst, Math.Abs(live.Rect.Left - slot.PredictedRect.Left));
                matched++;
            }
            return matched > 0 ? worst : 0f;
        }

        private bool ViewportNeedsSettle()
        {
            float stackTop = GetCurrentStackPanelTop();
            float motion = (!float.IsNaN(stackTop) && !float.IsNaN(_lastStableStackTop)) ? Math.Abs(stackTop - _lastStableStackTop) : 0f;
            float alignErr = GetCurrentAlignmentErrorPx();
            return motion > 1.2f || alignErr > Math.Max(3f, _lastMeasuredRowPitch * 0.08f);
        }

        private bool DetectedLiveOvershootAfterScroll()
        {
            int nowLive = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null ? _currentSnapshot.LiveVisibleCells.Count : 0;
            bool overshot = (_lastLiveCellCountBeforeScroll >= 15 && nowLive == 0) || (_lastLiveCellCountBeforeScroll >= 20 && nowLive <= 5);
            if (overshot) Log(() => "overshoot-detected: liveCells " + _lastLiveCellCountBeforeScroll + " -> " + nowLive + ", top=" + _viewportOriginRowFloat.ToString("0.00", CultureInfo.InvariantCulture));
            return overshot;
        }

        private int GetTrackedProjectedRowCount()
        {
            var tracked = GetTrackedProjectedCells();
            if (tracked == null || tracked.Count == 0)
                return 0;
            return tracked.Where(c => c != null).Select(c => c.RowIndex).Distinct().Count();
        }

        private RectangleF ExpandVertically(RectangleF rect, float amount)
        {
            return new RectangleF(rect.Left, rect.Top - amount, rect.Width, rect.Height + amount * 2f);
        }

        private List<VisibleCell> GetExtendedNativeCells(RectangleF listBounds)
        {
            float extra = Math.Max(120f, _lastMeasuredRowPitch * 2.5f);
            RectangleF probeBounds = ExpandVertically(listBounds, extra);
            var cells = GetMappedVisibleCells(probeBounds);
            if (cells == null)
                return new List<VisibleCell>();
            return DeduplicateVisibleCells(cells).Where(c => c != null && !c.IsProjected).ToList();
        }

        private void UpdateRow6NativeExtentEvidence(List<VisibleCell> nativeCells)
        {
            _lastExtendedNativeCells = nativeCells != null
                ? nativeCells.Where(c => c != null && c.Ref != null && string.Equals(c.Ref.Family, "row6", StringComparison.Ordinal)).ToList()
                : new List<VisibleCell>();

            _lastExtendedNativeRowCount = 0;
            if (_lastExtendedNativeCells.Count == 0)
                return;

            var seenRows = new HashSet<int>();
            foreach (var cell in _lastExtendedNativeCells)
            {
                int absIndex = cell.Ref.Major * 5 + cell.ColumnIndex;
                if (absIndex > _highestNativeAbsoluteIndexSeen)
                    _highestNativeAbsoluteIndexSeen = absIndex;
                seenRows.Add(cell.Ref.Major);
            }

            _lastExtendedNativeRowCount = seenRows.Count;
        }

        private void UpdateTrackedNativeEvidence(List<VisibleCell> nativeCells, RectangleF listBounds)
        {
            if (nativeCells == null || _absoluteGrid == null)
                return;

            int now = Environment.TickCount;
            float stackTop = GetCurrentStackPanelTop();

            foreach (var cell in nativeCells)
            {
                if (cell == null || cell.Ref == null)
                    continue;
                if (!string.Equals(cell.Ref.Family, "row6", StringComparison.Ordinal))
                    continue;

                int absIndex = cell.Ref.Major * 5 + cell.ColumnIndex;
                if (absIndex < 0 || absIndex >= _absoluteGrid.Slots.Count)
                    continue;

                TrackedLiveCell t;
                if (!_trackedLiveCells.TryGetValue(absIndex, out t))
                {
                    t = new TrackedLiveCell();
                    _trackedLiveCells[absIndex] = t;
                }

                t.AbsoluteIndex = absIndex;
                t.AcdId = cell.Ref.CachedLegendaryGemAcdId;
                t.LastRect = cell.Rect;
                t.LastStackTop = stackTop;
                t.LastSeenTick = now;

                RectangleF ix = RectangleF.Intersect(cell.Rect, listBounds);
                bool insideVisibleList = ix.Width > 0f && ix.Height > 0f;
                if (insideVisibleList)
                    t.ConfirmedLive = true;
            }
        }

        private bool IsPageTrustworthyForResolve(out string reason)
        {
            reason = string.Empty;

            int liveRows = GetLiveVisibleRowCount();
            int authRows = GetAuthoritativeViewportVisibleRowCount();
            int trackedRows = GetTrackedProjectedRowCount();
            int nativeRows = _lastExtendedNativeRowCount;

            if (liveRows <= 0)
            {
                reason = "no-live-rows";
                return false;
            }

            int strongestRows = Math.Max(liveRows, Math.Max(trackedRows, nativeRows));
            if (authRows > strongestRows && liveRows < 3)
            {
                reason = "authRows>rows";
                return false;
            }

            float alignErr = GetCurrentAlignmentErrorPx();
            if (alignErr > Math.Max(4f, _lastMeasuredRowPitch * 0.10f))
            {
                reason = "alignment-error=" + alignErr.ToString("0.0", CultureInfo.InvariantCulture);
                return false;
            }

            return true;
        }

        private void LogTargetRejectReason(string reason)

        {
            Log(() => "target-reject: " + reason
                + " targetAbs=" + (_target != null ? _target.AbsoluteIndex : -1)
                + " topRow=" + _viewportOriginRowInt
                + " liveRows=" + GetLiveVisibleRowCount()
                + " trackedRows=" + GetTrackedProjectedRowCount()
                + " authRows=" + GetAuthoritativeViewportVisibleRowCount()
                + " epoch=" + _viewportEpoch);
        }

        private bool ApplyLiveAlignmentCorrection(RectangleF listBounds, List<VisibleCell> liveCells, string reason)
        {
            if (_absoluteGrid == null || liveCells == null || liveCells.Count < 3) return false;
            if (_absoluteGrid.Slots == null || _absoluteGrid.Slots.Count == 0) return false;
            if (_absoluteGrid.RowPitch <= 1f || _absoluteGrid.ColumnPitch <= 1f) return false;
            var dxs = new List<float>();
            var dys = new List<float>();
            int matched = 0;
            foreach (var live in liveCells)
            {
                if (live == null) continue;
                int absIndex = (_viewportOriginRowInt + live.RowIndex) * 5 + live.ColumnIndex;
                if (absIndex < 0 || absIndex >= _absoluteGrid.Slots.Count) continue;
                var slot = _absoluteGrid.Slots[absIndex];
                RectangleF predicted = slot.PredictedRect;
                if (predicted == RectangleF.Empty) continue;
                dxs.Add(live.Rect.Left - predicted.Left);
                dys.Add(live.Rect.Top - predicted.Top);
                matched++;
            }
            if (matched < 3) return false;
            dxs.Sort(); dys.Sort();
            float dx = dxs[dxs.Count/2], dy = dys[dys.Count/2];
            if (Math.Abs(dx) < 1.5f && Math.Abs(dy) < 1.5f) return false;
            RectangleF anchor = _absoluteGrid.AnchorRect; anchor.X += dx; anchor.Y += dy;
            _absoluteGrid.AnchorRect = anchor; _stableGridAnchorRect = anchor;
            float correctedTopFloat = _viewportOriginRowFloat - (dy / Math.Max(1f, _absoluteGrid.RowPitch));
            SetViewportOriginMeasured(correctedTopFloat, "live-align:" + reason);
            ClampViewportTop();
            int maxTop = GetMaxTopVisibleRow();
            _absoluteGrid.ViewportTopRowFloat = Math.Max(0f, Math.Min(maxTop, _viewportOriginRowFloat));
            _absoluteGrid.ViewportTopRowInt = _viewportOriginRowInt;
            for (int i=0;i<_absoluteGrid.Slots.Count;i++)
            {
                var slot = _absoluteGrid.Slots[i];
                float localRowFloat = slot.AbsoluteRow - _absoluteGrid.ViewportTopRowFloat;
                slot.PredictedRect = new RectangleF(_absoluteGrid.AnchorRect.Left + slot.AbsoluteCol * _absoluteGrid.ColumnPitch, _absoluteGrid.AnchorRect.Top + localRowFloat * _absoluteGrid.RowPitch, _absoluteGrid.CellWidth, _absoluteGrid.CellHeight);
                RectangleF ix = RectangleF.Intersect(slot.PredictedRect, listBounds);
                slot.IntersectsViewport = ix.Width > 0f && ix.Height > 0f;
            }
            Log(() => "live-align " + reason + ": matched=" + matched + ", dx=" + dx.ToString("0.0", CultureInfo.InvariantCulture) + ", dy=" + dy.ToString("0.0", CultureInfo.InvariantCulture) + ", top=" + _viewportOriginRowFloat.ToString("0.00", CultureInfo.InvariantCulture));
            return true;
        }

        private bool NeedsPostScrollRealignment()
        {
            if (_currentSnapshot == null || _currentSnapshot.LiveVisibleCells == null)
                return false;

            var live = _currentSnapshot.LiveVisibleCells;
            if (live.Count < 3 || _absoluteGrid == null)
                return false;

            float worstDy = 0f;
            int checkedCount = 0;

            foreach (var cell in live)
            {
                int absRow = _viewportOriginRowInt + cell.RowIndex;
                int absIndex = absRow * 5 + cell.ColumnIndex;
                if (absIndex < 0 || absIndex >= _absoluteGrid.Slots.Count)
                    continue;

                var slot = _absoluteGrid.Slots[absIndex];
                worstDy = Math.Max(worstDy, Math.Abs(cell.Rect.Top - slot.PredictedRect.Top));
                checkedCount++;
            }

            if (checkedCount < 3)
                return false;

            return worstDy > Math.Max(3f, _absoluteGrid.RowPitch * 0.10f);
        }

        private void AdvancePageProbe()
        {
            if (!_probeActive)
                return;

            if (_probeCells == null || _probeCells.Count == 0)
            {
                _probeActive = false;
                Fail("probe started without visible cells");
                return;
            }

            if (!_probeWaitingForValidation)
            {
                while (_probeIndex < _probeCells.Count)
                {
                    var candidateCell = _probeCells[_probeIndex];
                    int candidateAbs;
                    if (candidateCell == null)
                    {
                        _probeIndex++;
                        continue;
                    }
                    if (FullListVerificationMode && TryGetPredictedAbsoluteIndex(candidateCell, out candidateAbs) && _scannedAbsoluteIndices.Contains(candidateAbs))
                    {
                        _probeIndex++;
                        continue;
                    }
                    break;
                }

                if (_probeIndex >= _probeCells.Count)
                {
                    FinalizePageProbe();
                    return;
                }

                if (ElapsedMs(_probeActionTick) < CellClickDelayMs)
                    return;

                _probePendingCell = _probeCells[_probeIndex];

                ClickVisibleCell(_probePendingCell);
                _probeWaitingForValidation = true;
                _probeActionTick = NowTick();
                return;
            }

            if (ElapsedMs(_probeActionTick) < CellValidateDelayMs)
                return;

            var observed = ObservePendingCell(_probePendingCell);
            bool identified = !string.IsNullOrEmpty(observed.SelectedGemName) && observed.SelectedGemRank >= 0;

            if (!identified && _probePendingCell != null && _probeNoIdentityRetryCount < MaxProbeNoIdentityRetries)
            {
                _probeNoIdentityRetryCount++;
                _probeWaitingForValidation = false;
                _probeActionTick = NowTick();
                int retryAbs;
                string retryAbsText = TryGetPredictedAbsoluteIndex(_probePendingCell, out retryAbs) ? (" a" + retryAbs) : string.Empty;
                Log(() => "probe retry " + _probeNoIdentityRetryCount + retryAbsText + " on " + GetShortPath(_probePendingCell.Ref.Path));
                return;
            }

            _probeSnapshot.ObservedCells.Add(observed);
            if (_currentProbeAbsoluteIndex >= 0)
                _scannedAbsoluteIndices.Add(_currentProbeAbsoluteIndex);
            if (identified)
                _probeSnapshot.IdentifiedCellCount++;
            if (_target != null && observed.MatchTarget && _probeSnapshot.TargetCell == null)
                _probeSnapshot.TargetCell = observed;

            _probeIndex++;
            _probeNoIdentityRetryCount = 0;
            _probeWaitingForValidation = false;
            _probePendingCell = null;
            _probeActionTick = NowTick();

            if (_probeIndex >= _probeCells.Count)
                FinalizePageProbe();
        }

        private ObservedCell ObservePendingCell(VisibleCell cell)
        {
            string sourceText;
            var selection = ReadCurrentSelectionEvidence(out sourceText);
            int absIndex;
            int probeAbs = TryGetPredictedAbsoluteIndex(cell, out absIndex) ? absIndex : -1;
            if (LogSelectionEvidence)
            {
                Log(() => "observe" + (probeAbs >= 0 ? (" a" + probeAbs) : string.Empty)
                    + " => name=" + (selection.Item1 ?? "<null>")
                    + ", rank=" + selection.Item2
                    + ", loaded=" + (SafeAnimState(_itemButton) != -1 ? "1" : "0")
                    + ", text='" + ShortEvidence(sourceText) + "'");
            }

            return new ObservedCell
            {
                VisibleCell = cell,
                SelectedGemName = selection.Item1,
                SelectedGemRank = selection.Item2,
                SourceText = sourceText,
                MatchTarget = selection.Item1 != null && _target != null && IsTargetMatch(selection.Item1, selection.Item2, _target),
                ItemButtonLoaded = SafeAnimState(_itemButton) != -1,
                UpgradeButtonAnimState = SafeAnimState(_upgradeButton),
            ViewportEpoch = _viewportEpoch,
            };
        }

        private void FinalizePageProbe()
        {
            _probeSnapshot.Signature = BuildPageSignature(_probeSnapshot.ObservedCells);
            _currentSnapshot = _probeSnapshot;
            _probeActive = false;

            Log(() => "probe " + _probeReason.ToString().ToLowerInvariant() + ": cells=" + _currentSnapshot.VisibleCells.Count + ", identified=" + _currentSnapshot.IdentifiedCellCount + ", sig=" + ShortSignature(_currentSnapshot.Signature));
            Log(() => "probe families: " + string.Join(", ", _currentSnapshot.VisibleCells.Take(8).Select(c => c.FamilyTag)));

            if (_currentSnapshot.IdentifiedCellCount < MinIdentifiedCellsForNavigation)
            {
                string warn = "probe identified only " + _currentSnapshot.IdentifiedCellCount + " visible gems";
                Log(warn);
                if (RequireIdentifiedCellsForNavigation && !FullListVerificationMode)
                {
                    Fail("probe could not identify enough visible gems for navigation");
                    return;
                }
            }

            switch (_probeReason)
            {
                case ProbeReason.Reset:
                    HandleCompletedResetProbe();
                    break;
                case ProbeReason.Search:
                    HandleCompletedSearchProbe();
                    break;
                default:
                    Fail("unexpected probe reason");
                    break;
            }
        }

        private void HandleCompletedResetProbe()
        {
            _seenPageSignatures.Clear();
            Log(() => "reset probe complete; beginning bounded top reset burst");
            _stage = AutomationStage.ResetScrollUp;
        }

        private bool NeedsBottomNudgeForProjectedFinalRow()
        {
            if (_bottomNudgeAttempted)
                return false;
            if (_currentSnapshot == null || _currentSnapshot.VisibleCells == null || _currentSnapshot.VisibleCells.Count == 0)
                return false;
            if (_virtualGrid == null || _orderedGems == null || _orderedGems.Count == 0)
                return false;
            if (GetAuthoritativeViewportTopRow() < 0)
                return false;
            if (GetAuthoritativeViewportTopRow() < GetMaxTopVisibleRow())
                return false;

            var inferredCells = _currentSnapshot.InferredViewportCells ?? new List<VisibleCell>();
            return inferredCells.Any(c => c != null
                && c.IsProjected
                && c.AbsoluteIndex >= 0
                && c.AbsoluteIndex < _orderedGems.Count);
        }

        private bool TryBottomNudgeScroll()
        {
            if (_currentSnapshot == null)
                return false;

            // REWRITTEN: was ClickPoint(hold=420ms) + Thread.Sleep(150ms) = 570ms blocking.
            // Now uses a wheel tick — zero blocking, async verify on next capture.
            RectangleF listBounds = _currentSnapshot.ListBounds;
            if (listBounds == RectangleF.Empty || listBounds.Width <= 1f || listBounds.Height <= 1f)
                return false;

            float cx = listBounds.Left + listBounds.Width * 0.50f;
            float cy = Math.Max(listBounds.Top + 8f, listBounds.Bottom - 10f);
            PointF hoverPoint = new PointF(cx, cy);

            if (!EnsureCursorReadyForWheelScroll(hoverPoint, "bottom-nudge",
                    _target != null ? _target.AbsoluteIndex : int.MinValue, int.MinValue))
                return true; // cursor moved; wheel fires next tick

            _identityLossCheckPending = true;
            _lastLiveCellCountBeforeScroll = _currentSnapshot.LiveVisibleCells != null
                ? _currentSnapshot.LiveVisibleCells.Count : 0;

            WheelScrollTick(true, "final-bottom-nudge");
            _lastActionTick = NowTick();
            _afterScrollWait = 0;

            Log(() => "final bottom nudge: wheel tick sent (no-sleep), async verify on next capture");

            ViewportCapture cap;
            if (TryCaptureViewport(out cap))
            {
                bool hasLiveCells = RefreshSnapshotFromViewportCapture(cap);
                if (hasLiveCells)
                {
                    Log(() => "final bottom nudge complete: visibleCells=" + cap.LiveCells.Count + ", topRow=" + GetAuthoritativeViewportTopRow());
                    return true;
                }
                Log(() => "final bottom nudge geometry refreshed but there are no live Urshi cells on the page");
            }
            else
            {
                Log(() => "final bottom nudge failed to refresh visible geometry");
            }
            return false;
        }

        private void HandleCompletedSearchProbe()
        {
            int authoritativeTopRow = GetAuthoritativeViewportTopRow();
            int authoritativeVisibleRows = GetAuthoritativeViewportVisibleRowCount();
            Log(() => "search probe complete: topRow=" + authoritativeTopRow + ", visibleRows=" + authoritativeVisibleRows + ", targetAbs=" + (_target != null ? _target.AbsoluteIndex : -1) + ", probeAbs=" + _currentProbeAbsoluteIndex);
            bool targetRowVisible = IsTargetRowReliablyVisible(_target);

            if (!FullListVerificationMode)
            {
                if (_currentSnapshot.TargetCell != null)
                {
                    Log(() => "target found on current observed page via guarded viewport probe");
                    _stage = AutomationStage.SelectObservedTarget;
                    return;
                }

                if (_target != null && _currentSnapshot?.VisibleCells != null)
                {
                    VisibleCell acdTargetCell = _currentSnapshot.VisibleCells.FirstOrDefault(
                        c => c != null && !c.IsProjected && c.Ref != null && c.AbsoluteIndex == _target.AbsoluteIndex);
                    if (acdTargetCell == null && _target.Source?.Item != null)
                    {
                        uint tAcd = (uint)_target.Source.Item.AcdId;
                        if (tAcd != 0 && tAcd != 0xFFFFFFFF)
                            acdTargetCell = _currentSnapshot.VisibleCells.FirstOrDefault(
                                c => c != null && !c.IsProjected && c.Ref != null && c.Ref.CachedLegendaryGemAcdId == tAcd);
                    }
                    if (acdTargetCell != null)
                    {
                        Log(() => "probe acd-recovery: target visible via ACD at a" + acdTargetCell.AbsoluteIndex);
                        _currentSnapshot.TargetCell = new ObservedCell
                        {
                            VisibleCell = acdTargetCell,
                            SelectedGemName = _target.Name,
                            SelectedGemRank = _target.Rank,
                            SourceText = "probe-acd-recovery",
                            MatchTarget = true,
                            ItemButtonLoaded = SafeAnimState(_itemButton) != -1,
                            UpgradeButtonAnimState = SafeAnimState(_upgradeButton),
                        ViewportEpoch = _viewportEpoch,
                        };
                        _stage = AutomationStage.SelectObservedTarget;
                        return;
                    }
                }

                if (targetRowVisible)
                {
                    if (!TryAssignTargetCellFromCurrentViewport())
                    {
                        SoftAbortAndRestart("target row is in-band, but no live slot could be resolved on the current epoch");
                        return;
                    }

                    _stage = AutomationStage.SelectObservedTarget;
                    return;
                }

                int desiredTopRow;
                int currentTopRow;
                int deltaRows;
                if (FullListVerificationMode)
                {
                    if (CanSeekTargetViewport(out desiredTopRow, out currentTopRow, out deltaRows))
                    {
                        if (!InvariantAllowsTravel("SearchScrollDown"))
                        {
                            SoftAbortAndRestart("invariant violation: attempted travel while target viewport is already locked");
                            return;
                        }

                        Log(() => "verification-mode seek desiredTopRow=" + desiredTopRow
                            + " from topRow=" + currentTopRow
                            + " (deltaRows=" + deltaRows + ")");
                        _stage = AutomationStage.SearchScrollDown;
                        return;
                    }

                    Fail("verification probe exhausted desired viewport without live target proof");
                    return;
                }
                else
                {
                    SoftAbortAndRestart("normal runtime must not re-enter broad probe/search flow");
                    return;
                }
            }

            if (FullListVerificationMode && NeedsBottomNudgeForProjectedFinalRow())
            {
                _bottomNudgeAttempted = true;
                Log(() => "bottom-aligned projected row detected; performing one final bottom nudge before completion");
                if (TryBottomNudgeScroll())
                {
                    StartPageProbe(ProbeReason.Search);
                    return;
                }
            }

            if (FullListVerificationMode && _orderedGems != null && _orderedGems.Count > 0 && _scannedAbsoluteIndices.Count >= _orderedGems.Count)
            {
                Log(() => "full Urshi list verification completed by scanned-slot count=" + _scannedAbsoluteIndices.Count
                    + "/" + _orderedGems.Count
                    + (targetRowVisible ? "; target row is in current viewport" : string.Empty));

                if (!AutoUpgradeAfterFullListVerification)
                {
                    _autoRunning = false;
                    _stage = AutomationStage.VerificationComplete;
                    return;
                }

                if (targetRowVisible)
                {
                    _stage = AutomationStage.SelectObservedTarget;
                    return;
                }

                Fail("full scan completed but target row is not in the current viewport");
                return;
            }
        }


        private bool IsUiElementVisible(IUiElement element)
        {
            try { return element != null && element.Visible; }
            catch { return false; }
        }

        private float GetCurrentStackPanelTop()
        {
            try
            {
                if (_stackPanel != null && _stackPanel.Visible)
                    return _stackPanel.Rectangle.Top;
            }
            catch { }

            return GetFirstVisibleCellTop(_currentSnapshot != null ? _currentSnapshot.VisibleCells : null);
        }

        private float GetCurrentMeasuredRowPitch()
        {
            if (_virtualGrid != null && _virtualGrid.RowPitch > 1f)
                return _virtualGrid.RowPitch;
            if (_lastMeasuredRowPitch > 1f)
                return _lastMeasuredRowPitch;
            return float.NaN;
        }

        private int GetAuthoritativeViewportTopRow()
        {
            return _viewportOriginRowInt;
        }
        private void UpdateDebugTopRowMirror()
        {
            _estimatedTopVisibleRow = _viewportOriginRowInt;
        }

        private int CalculateVisibleRowGeometryCap(RectangleF listBounds, float rowPitch, float cellHeight)
        {
            if (listBounds.Height <= 1f)
                return 1;

            float effectivePitch = rowPitch > 1f ? rowPitch : Math.Max(1f, cellHeight);
            int cap = (int)Math.Ceiling(listBounds.Height / Math.Max(1f, effectivePitch));
            return Math.Max(1, Math.Min(4, cap));
        }

        private int GetLiveVisibleRowCount()
        {
            try
            {
                var live = _currentSnapshot != null ? _currentSnapshot.LiveVisibleCells : null;
                if (live != null && live.Count > 0)
                    return Math.Max(1, live.Select(c => c.RowIndex).DefaultIfEmpty(-1).Max() + 1);
            }
            catch { }

            return 0;
        }

        private int GetGeometryCappedVisibleRowCount()
        {
            RectangleF listBounds = RectangleF.Empty;
            float rowPitch = 0f;
            float cellHeight = 0f;
            try { if (_currentSnapshot != null) listBounds = _currentSnapshot.ListBounds; } catch { }
            try
            {
                if (_absoluteGrid != null)
                {
                    if (_absoluteGrid.RowPitch > 1f) rowPitch = _absoluteGrid.RowPitch;
                    if (_absoluteGrid.CellHeight > 1f) cellHeight = _absoluteGrid.CellHeight;
                }
            }
            catch { }
            if (rowPitch <= 1f) rowPitch = _lastMeasuredRowPitch;
            if (cellHeight <= 1f && rowPitch > 1f) cellHeight = rowPitch;
            if (cellHeight <= 1f) cellHeight = 58f;
            int cap = CalculateVisibleRowGeometryCap(listBounds, rowPitch, cellHeight);
            int liveRows = GetLiveVisibleRowCount();
            if (liveRows > 0)
                return Math.Max(1, Math.Min(4, Math.Max(cap, liveRows)));
            int fallbackRows = _lastMeasuredVisibleRowCount > 0 ? _lastMeasuredVisibleRowCount : 1;
            return Math.Max(1, Math.Min(4, Math.Min(cap, fallbackRows)));
        }

        private void LogViewportMetrics(string reason)
        {
            try
            {
                RectangleF lb = _currentSnapshot != null ? _currentSnapshot.ListBounds : RectangleF.Empty;
                float rowPitch = (_virtualGrid != null && _virtualGrid.RowPitch > 1f) ? _virtualGrid.RowPitch : _lastMeasuredRowPitch;
                float cellHeight = (_virtualGrid != null && _virtualGrid.CellHeight > 1f) ? _virtualGrid.CellHeight : rowPitch;
                int liveRows = GetLiveVisibleRowCount();
                int cap = CalculateVisibleRowGeometryCap(lb, rowPitch, cellHeight);
                int authRows = GetAuthoritativeViewportVisibleRowCount();

                Log(() => "viewport-metrics " + reason
                    + ": topRow=" + _viewportOriginRowInt
                    + ", liveRows=" + liveRows
                    + ", authRows=" + authRows
                    + ", cap=" + cap
                    + ", rowPitch=" + rowPitch.ToString("F1", CultureInfo.InvariantCulture)
                    + ", cellH=" + cellHeight.ToString("F1", CultureInfo.InvariantCulture)
                    + ", listH=" + lb.Height.ToString("F1", CultureInfo.InvariantCulture)
                    + ", epoch=" + _viewportEpoch, s7o_AutoGemUpgradeState.DebugLevelVerbose);
            }
            catch { }
        }

        private void LogViewportGridMismatch(ObservedPageSnapshot snap)
        {
            if (snap == null) return;

            int liveRows = GetLiveVisibleRowCount();
            int authRows = GetAuthoritativeViewportVisibleRowCount();
            if (liveRows > 0 && authRows > liveRows)
            {
                Log(() => "grid-mismatch: authRows=" + authRows
                    + " exceeds liveRows=" + liveRows
                    + " at epoch=" + _viewportEpoch
                    + " topRow=" + _viewportOriginRowInt);
            }
        }

        private RectangleF GetAuthoritativeScrollLane(RectangleF paneRect, RectangleF listBounds)
        {
            try
            {
                if (_scrollBar != null && _scrollBar.Visible)
                {
                    RectangleF sb = _scrollBar.Rectangle;
                    if (sb.Width > 4f && sb.Height > 20f)
                        return sb;
                }
            }
            catch { }

            float fallbackLeft = listBounds.Right;
            float fallbackWidth = Math.Max(18f, paneRect.Right - listBounds.Right);
            return new RectangleF(fallbackLeft, listBounds.Top, fallbackWidth, listBounds.Height);
        }

        private void LogScrollLane(string reason, RectangleF paneRect, RectangleF listBounds)
        {
            try
            {
                RectangleF lane = GetAuthoritativeScrollLane(paneRect, listBounds);
                var pts = GetScrollPoints(paneRect, listBounds, _currentSnapshot != null ? _currentSnapshot.LiveVisibleCells : new List<VisibleCell>());
                PointF fastUp = GetFastScrollPoint(paneRect, listBounds, false);
                PointF fastDown = GetFastScrollPoint(paneRect, listBounds, true);

                Log(() => "scroll-lane " + reason
                    + ": lane=(" + (int)lane.Left + "," + (int)lane.Top + "," + (int)lane.Width + "x" + (int)lane.Height + ")"
                    + ", up=(" + (int)pts.Item1.X + "," + (int)pts.Item1.Y + ")"
                    + ", down=(" + (int)pts.Item2.X + "," + (int)pts.Item2.Y + ")"
                    + ", fastUp=(" + (int)fastUp.X + "," + (int)fastUp.Y + ")"
                    + ", fastDown=(" + (int)fastDown.X + "," + (int)fastDown.Y + ")", s7o_AutoGemUpgradeState.DebugLevelVerbose);
            }
            catch { }
        }

        private bool CanSeekTargetViewport(out int desiredTopRow, out int currentTopRow, out int deltaRows)
        {
            desiredTopRow = -1;
            currentTopRow = GetAuthoritativeViewportTopRow();
            deltaRows = 0;

            if (_target == null || _virtualGrid == null || _virtualGrid.ColumnCount <= 0)
                return false;
            if (currentTopRow < 0)
                return false;

            desiredTopRow = GetDesiredTopScanRowForAbsoluteIndex(_target.AbsoluteIndex);
            if (desiredTopRow < 0)
                return false;

            deltaRows = desiredTopRow - currentTopRow;
            return deltaRows != 0;
        }

        private bool IsTargetViewportTrulyLocked(GemTarget target)
        {
            if (target == null)
                return false;

            if (!IsTargetRowReliablyVisible(target))
                return false;

            if (!HasLiveViewportTruth())
                return false;

            int liveRows = GetLiveVisibleRowCount();
            int authRows = GetAuthoritativeViewportVisibleRowCount();
            if (liveRows > 0 && liveRows < authRows)
                return false;

            return true;
        }

        private bool InvariantAllowsTravel(string nextStage)
        {
            if (_target != null && IsTargetViewportTrulyLocked(_target))
            {
                Log(() => "invariant violation: attempted " + nextStage
                    + " while target viewport is already truly locked"
                    + " (topRow=" + GetAuthoritativeViewportTopRow()
                    + ", authRows=" + GetAuthoritativeViewportVisibleRowCount()
                    + ", liveRows=" + GetLiveVisibleRowCount()
                    + ", epoch=" + _viewportEpoch + ")");
                return false;
            }

            return true;
        }

        private int GetAuthoritativeViewportVisibleRowCount()
        {
            return GetGeometryCappedVisibleRowCount();
        }

        private bool HasLiveViewportTruth()
        {
            return _currentSnapshot != null
                && _currentSnapshot.LiveVisibleCells != null
                && _currentSnapshot.LiveVisibleCells.Count > 0;
        }

        private bool IsCurrentEpochLiveSlot(VisibleCell cell)
        {
            if (cell == null || cell.IsProjected || !HasLiveViewportTruth())
                return false;

            return _currentSnapshot.LiveVisibleCells.Any(c => c != null
                && !c.IsProjected
                && c.RowIndex == cell.RowIndex
                && c.ColumnIndex == cell.ColumnIndex
                && ((c.Ref == null || cell.Ref == null || c.Ref.CachedLegendaryGemAcdId == 0u || cell.Ref.CachedLegendaryGemAcdId == 0u)
                    || c.Ref.CachedLegendaryGemAcdId == cell.Ref.CachedLegendaryGemAcdId));
        }

        private bool IsTargetRowInCurrentViewport(GemTarget target)
        {
            if (target == null || _absoluteGrid == null)
                return false;

            int targetRow = Math.Max(0, target.AbsoluteIndex / 5);
            int currentTop = Math.Max(0, _absoluteGrid.ViewportTopRowInt);
            int currentBottomExclusive = currentTop + Math.Max(1, _absoluteGrid.VisibleRowCount);

            return targetRow >= currentTop && targetRow < currentBottomExclusive;
        }

        private bool IsTargetRowReliablyVisible(GemTarget target)
        {
            if (target == null)
                return false;

            if (!IsTargetRowInCurrentViewport(target))
                return false;

            if (!HasLiveViewportTruth())
                return false;

            int liveRows = GetLiveVisibleRowCount();
            if (liveRows < MinLiveScanRowsForNavigation)
            {
                Log(() => "target row band reached, but liveRows=" + liveRows
                    + " is below MinLiveScanRowsForNavigation=" + MinLiveScanRowsForNavigation);
                return false;
            }

            return true;
        }


        private bool CanAttemptListCommit(VisibleCell candidate, string reason)
        {
            if (candidate == null)
            {
                Log(() => "commit-block " + reason + ": candidate=null");
                return false;
            }

            if (candidate.IsProjected)
            {
                Log(() => "commit-block " + reason + ": candidate is projected");
                return false;
            }

            if (!HasLiveViewportTruth())
            {
                Log(() => "commit-block " + reason + ": no live viewport truth");
                return false;
            }

            if (!IsCurrentEpochLiveSlot(candidate))
            {
                Log(() => "commit-block " + reason + ": candidate is not a current-epoch live slot");
                return false;
            }

            if (_target == null || !IsTargetRowReliablyVisible(_target))
            {
                Log(() => "commit-block " + reason + ": target row not in current viewport");
                return false;
            }

            return true;
        }

        private void UpdateViewportMetricsFromSnapshot()
        {
            if (_virtualGrid != null)
            {
                if (_virtualGrid.RowPitch > 1f)
                    _lastMeasuredRowPitch = _virtualGrid.RowPitch;
                if (_virtualGrid.VisibleRowCount > 0)
                    _lastMeasuredVisibleRowCount = _virtualGrid.VisibleRowCount;
            }

            float stackTop = GetCurrentStackPanelTop();
            if (!float.IsNaN(stackTop) && !float.IsInfinity(stackTop))
                _lastGoodStackPanelTop = stackTop;
        }

        private void SetViewportOriginExact(int topRow, string reason)
        {
            int maxTop = GetMaxTopVisibleRow();
            int clampedTop = Math.Max(0, Math.Min(maxTop, topRow));
            float newTopFloat = clampedTop;
            RectangleF anchor = _stableGridAnchorRect;
            bool changed = ShouldAdvanceViewportEpoch(clampedTop, newTopFloat, anchor, _currentSnapshot != null ? _currentSnapshot.LiveVisibleCells : null);
            _viewportOriginRowInt = clampedTop;
            _viewportOriginRowFloat = newTopFloat;
            ClampViewportTop();
            if (changed)
            {
                _viewportEpoch++;
                if (!string.IsNullOrWhiteSpace(reason))
                    Log(() => "viewport-origin " + reason + ": topRow=" + _viewportOriginRowInt + ", epoch=" + _viewportEpoch);
            }
            UpdateDebugTopRowMirror();
            SyncVirtualGridViewport();
        }

        private void SetViewportOriginMeasured(float topRowFloat, string reason)
        {
            int maxTop = GetMaxTopVisibleRow();
            topRowFloat = Math.Max(0f, Math.Min(maxTop, topRowFloat));
            int topRowInt = (int)Math.Floor(topRowFloat + 0.15f);
            RectangleF anchor = _stableGridAnchorRect;
            bool changed = ShouldAdvanceViewportEpoch(topRowInt, topRowFloat, anchor, _currentSnapshot != null ? _currentSnapshot.LiveVisibleCells : null);
            _viewportOriginRowFloat = topRowFloat;
            _viewportOriginRowInt = topRowInt;
            ClampViewportTop();
            if (changed)
            {
                _viewportEpoch++;
                if (!string.IsNullOrWhiteSpace(reason))
                    Log(() => "viewport-origin " + reason + ": topRow≈" + _viewportOriginRowFloat.ToString("0.00", CultureInfo.InvariantCulture) + " (" + _viewportOriginRowInt + "), epoch=" + _viewportEpoch);
            }
            UpdateDebugTopRowMirror();
            SyncVirtualGridViewport();
        }

        private bool ShouldAdvanceViewportEpoch(int newTopInt, float newTopFloat, RectangleF newAnchor, IEnumerable<VisibleCell> liveCells)
        {
            bool topChanged = newTopInt != _viewportOriginRowInt;
            bool floatChanged = Math.Abs(newTopFloat - _viewportOriginRowFloat) > 0.18f;
            bool anchorChanged = _stableGridAnchorRect != RectangleF.Empty && (Math.Abs(newAnchor.Left - _stableGridAnchorRect.Left) > 2.0f || Math.Abs(newAnchor.Top - _stableGridAnchorRect.Top) > 2.0f);
            int liveCount = liveCells != null ? liveCells.Count(c => c != null && !c.IsProjected) : 0;
            int oldLiveCount = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null ? _currentSnapshot.LiveVisibleCells.Count : 0;
            bool liveSetChanged = Math.Abs(liveCount - oldLiveCount) >= 5;
            return topChanged || (floatChanged && anchorChanged) || liveSetChanged;
        }

        private void ClampViewportTop()
        {
            int maxTop = GetMaxTopVisibleRow();
            if (float.IsNaN(_viewportOriginRowFloat) || float.IsInfinity(_viewportOriginRowFloat))
                _viewportOriginRowFloat = 0f;
            _viewportOriginRowFloat = Math.Max(0f, Math.Min(maxTop, _viewportOriginRowFloat));
            _viewportOriginRowInt = Math.Max(0, Math.Min(maxTop, _viewportOriginRowInt));
        }

        private void CalibrateViewportBoundaryAfterNoMovement(bool goDown)
        {
            float stackTop = GetCurrentStackPanelTop();
            float rowPitch = GetCurrentMeasuredRowPitch();
            float listTop = (_currentSnapshot != null) ? _currentSnapshot.ListBounds.Top : float.NaN;

            if (!goDown)
            {
                if (_viewportOriginRowInt <= 0
                    || (!float.IsNaN(stackTop) && !float.IsNaN(listTop)
                        && ((rowPitch > 1f && stackTop >= listTop - rowPitch * 0.35f)
                            || stackTop >= listTop - 8f)))
                {
                    SetViewportOriginExact(0, "top-saturate");
                }
                return;
            }

            int maxTop = GetMaxTopVisibleRow();
            bool lowerBand = !float.IsNaN(stackTop) && !float.IsNaN(listTop)
                && ((rowPitch > 1f && stackTop <= listTop - rowPitch * 4.5f)
                    || stackTop <= listTop - 160f);

            if (_arrowScrollAttempts >= 2 && (lowerBand || GetAuthoritativeViewportTopRow() >= maxTop))
            {
                SetViewportOriginExact(maxTop, "bottom-saturate");
                _scrollAtBottom = true;
                if (_lastKnownPhysicalBottomTopRow < 0)
                    _lastKnownPhysicalBottomTopRow = maxTop;
            }
        }

        private bool UpdateViewportOriginFromStackMotion(string reason)
        {
            float stackTop = GetCurrentStackPanelTop();
            if (float.IsNaN(stackTop) || float.IsInfinity(stackTop))
                return false;

            bool changed = false;

            if (!float.IsNaN(_lastGoodStackPanelTop) && _lastMeasuredRowPitch > 1f)
            {
                float rowsMoved = (_lastGoodStackPanelTop - stackTop) / _lastMeasuredRowPitch;
                if (Math.Abs(rowsMoved) >= ScrollMotionThresholdRows)
                {
                    float nextTop = _viewportOriginRowFloat + rowsMoved;
                    SetViewportOriginMeasured(nextTop, reason + ":stack");
                    changed = true;
                }
            }

            _lastGoodStackPanelTop = stackTop;
            _lastStableStackTop = stackTop;
            return changed;
        }

        private bool RegisterSeekProgressOrStall(int topRowBefore, int topRowAfter)
        {
            float stackTop = GetCurrentStackPanelTop();
            bool topMoved = topRowAfter != topRowBefore;
            bool stackMoved = !float.IsNaN(_lastStableStackTop) && !float.IsNaN(stackTop) && Math.Abs(stackTop - _lastStableStackTop) >= 1.5f;

            if (topMoved || stackMoved)
            {
                _noProgressSeekCount = 0;
                _lastStableStackTop = stackTop;
                return true;
            }

            _noProgressSeekCount++;
            Log(() => "seek stall #" + _noProgressSeekCount
                + ": topBefore=" + topRowBefore
                + ", topAfter=" + topRowAfter
                + ", stackTop=" + (float.IsNaN(stackTop) ? "nan" : stackTop.ToString("0.0", CultureInfo.InvariantCulture)), s7o_AutoGemUpgradeState.DebugLevelState);

            return false;
        }

        private bool HitSeekStallLimit()
        {
            return _noProgressSeekCount >= MaxNoProgressSeekCount;
        }

        private void UpdateRuntimeBottomLock()
        {
            if (_absoluteGrid == null)
                return;

            float stackTop = GetCurrentStackPanelTop();
            if (float.IsNaN(stackTop) || float.IsNaN(_lastStableStackTop))
                return;

            int visibleRows = Math.Max(1, _absoluteGrid.VisibleRowCount);
            int maxTop = Math.Max(0, _absoluteGrid.TotalRowCount - visibleRows);

            bool physicallyStill = Math.Abs(stackTop - _lastStableStackTop) < 1.5f;
            bool atOrNearMaxTop = _viewportOriginRowInt >= Math.Max(0, maxTop - 1);

            if (physicallyStill && atOrNearMaxTop)
            {
                _runtimeBottomLocked = true;
                _runtimeBottomTopRow = Math.Max(_runtimeBottomTopRow, _viewportOriginRowInt);
                SetViewportOriginExact(Math.Min(maxTop, _runtimeBottomTopRow), "bottom-lock");
            }
        }

        private bool TryCaptureAndRefreshCurrentGeometry()
        {
            ViewportCapture cap;
            if (!TryCaptureViewport(out cap))
                return false;

            bool hasLiveCells = RefreshSnapshotFromViewportCapture(cap);
            if (!hasLiveCells)
            {
                _scrollCaptureFailed = true;
                Log(() => "capture: geometry refreshed but there are no live Urshi cells on the page", s7o_AutoGemUpgradeState.DebugLevelState);
                return false;
            }

            if (_viewportOriginRowInt < 0)
                SetViewportOriginExact(0, "init");

            TryCalibrateTopVisibleRowFromAcd(cap.LiveCells);
            UpdateViewportMetricsFromSnapshot();
            SyncVirtualGridViewport();
            return true;
        }

        private void TryCalibrateTopVisibleRowFromAcd(List<VisibleCell> visibleCells)
        {
            if (visibleCells == null || _orderedGems == null || _virtualGrid == null)
                return;

            var acdToAbs = new Dictionary<uint, int>();
            foreach (var gem in _orderedGems)
            {
                if (gem?.Item == null) continue;
                try
                {
                    uint acd = (uint)gem.Item.AcdId;
                    if (acd != 0 && acd != 0xFFFFFFFF && !acdToAbs.ContainsKey(acd))
                        acdToAbs[acd] = gem.AbsoluteIndex;
                }
                catch { }
            }

            if (acdToAbs.Count == 0) return;

            int cols = Math.Max(1, _virtualGrid.ColumnCount);
            int maxTop = GetMaxTopVisibleRow();
            int bestVotes = 0;
            int bestTop = -1;

            var votes = new Dictionary<int, int>();
            foreach (var cell in visibleCells)
            {
                if (cell == null || cell.IsProjected || cell.Ref?.Element == null)
                    continue;
                uint cellAcd = cell.Ref.CachedLegendaryGemAcdId;
                if (cellAcd == 0 || cellAcd == 0xFFFFFFFF)
                    continue;

                int absIdx;
                if (!acdToAbs.TryGetValue(cellAcd, out absIdx))
                    continue;

                int absRow = absIdx / cols;
                int inferredTop = absRow - cell.RowIndex;
                if (inferredTop < 0 || inferredTop > maxTop)
                    continue;

                int count;
                votes[inferredTop] = votes.TryGetValue(inferredTop, out count) ? count + 1 : 1;
            }

            foreach (var kv in votes)
            {
                if (kv.Value > bestVotes)
                {
                    bestVotes = kv.Value;
                    bestTop = kv.Key;
                }
            }

            bool hasUsableLiveAcds = bestVotes > 0;
            if (_identityLossCheckPending)
            {
                if (_lastCaptureHadUsableLiveAcds && !hasUsableLiveAcds)
                {
                    _lostLiveIdentityAfterScroll = true;
                    Log(() => "identity loss: live cell ACDs disappeared after scroll at topRow=" + GetAuthoritativeViewportTopRow()
                        + " — measured transport fallback engaged; permissive ACD-dead confirmation disabled");
                }
                else if (hasUsableLiveAcds)
                {
                    _lostLiveIdentityAfterScroll = false;
                }
                _identityLossCheckPending = false;
            }
            _lastCaptureHadUsableLiveAcds = hasUsableLiveAcds;

            if (bestTop >= 0)
            {
                Log(() => "ACD calibration: topRow " + GetAuthoritativeViewportTopRow() + "→" + bestTop
                    + " (votes=" + bestVotes + "/" + visibleCells.Count(c => !c.IsProjected && c.Ref?.CachedLegendaryGemAcdId != 0 && c.Ref?.CachedLegendaryGemAcdId != 0xFFFFFFFF) + ")");
                SetViewportOriginExact(bestTop, "acd");
                _lostLiveIdentityAfterScroll = false;
            }
            else // bestTop < 0 — no usable ACDs
            {
                if (!_lostLiveIdentityAfterScroll)
                    Log(() => "ACD calibration: no usable cell ACDs (LegendaryGemAcdId not live on this client — using position math)");
            }
        }

        private int GetDesiredTopScanRowForAbsoluteIndex(int absoluteIndex)
        {
            if (_virtualGrid == null || _virtualGrid.ColumnCount <= 0)
                return 0;

            int cols = Math.Max(1, _virtualGrid.ColumnCount);
            int visRows = Math.Max(1, _virtualGrid.VisibleRowCount);
            int maxTop = GetMaxTopVisibleRow();
            int targetRow = Math.Max(0, absoluteIndex / cols);

            int preferredLocalRow = Math.Min(1, Math.Max(0, visRows - 1));
            int desiredTopRow = targetRow - preferredLocalRow;
            return Math.Max(0, Math.Min(maxTop, desiredTopRow));
        }

        private bool TryAssignTargetCellFromCurrentViewport()
        {
            if (_target == null || _currentSnapshot == null || _absoluteGrid == null)
                return false;

            _currentSnapshot.TargetCell = null;

            // 1) direct ACD
            if (_targetAcd != 0 && _currentSnapshot.LiveVisibleCells != null)
            {
                var liveAcd = _currentSnapshot.LiveVisibleCells
                    .FirstOrDefault(c => c != null && c.Ref != null && c.Ref.CachedLegendaryGemAcdId == _targetAcd);

                if (liveAcd != null)
                    return AssignObservedTarget(liveAcd, "acd-direct", true);
            }

            // 2) exact absolute slot -> live slot
            if (_target.AbsoluteIndex >= 0 && _target.AbsoluteIndex < _absoluteGrid.Slots.Count)
            {
                var slot = _absoluteGrid.Slots[_target.AbsoluteIndex];
                if (slot.HasLiveCell && slot.LiveCell != null)
                    return AssignObservedTarget(slot.LiveCell, "abs-live", true);
            }

            // 3) live row/col fallback
            if (_currentSnapshot.LiveVisibleCells != null)
            {
                int absRow = _target.AbsoluteIndex / 5;
                int absCol = _target.AbsoluteIndex % 5;
                int localRow = absRow - _viewportOriginRowInt;

                var liveRowCol = _currentSnapshot.LiveVisibleCells.FirstOrDefault(c =>
                    c != null && !c.IsProjected && c.RowIndex == localRow && c.ColumnIndex == absCol);

                if (liveRowCol != null)
                    return AssignObservedTarget(liveRowCol, "live-rowcol", true);
            }

            // 4) tracked nearest fallback — soft only
            var tracked = GetTrackedProjectedCells();
            if (tracked.Count > 0 && _target.AbsoluteIndex >= 0 && _target.AbsoluteIndex < _absoluteGrid.Slots.Count)
            {
                RectangleF pr = _absoluteGrid.Slots[_target.AbsoluteIndex].PredictedRect;
                if (pr != RectangleF.Empty)
                {
                    PointF pc = new PointF(pr.Left + pr.Width * 0.5f, pr.Top + pr.Height * 0.5f);
                    VisibleCell best = null;
                    float bestDistSq = float.MaxValue;

                    foreach (var c in tracked)
                    {
                        if (c == null) continue;
                        PointF cc = new PointF(c.Rect.Left + c.Rect.Width * 0.5f, c.Rect.Top + c.Rect.Height * 0.5f);
                        float dx = cc.X - pc.X;
                        float dy = cc.Y - pc.Y;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < bestDistSq)
                        {
                            bestDistSq = d2;
                            best = c;
                        }
                    }

                    float tolX = Math.Max(14f, _absoluteGrid.ColumnPitch * 0.35f);
                    float tolY = Math.Max(14f, _absoluteGrid.RowPitch * 0.35f);

                    if (best != null)
                    {
                        PointF bc = new PointF(best.Rect.Left + best.Rect.Width * 0.5f, best.Rect.Top + best.Rect.Height * 0.5f);
                        bool nearEnough = Math.Abs(bc.X - pc.X) <= tolX && Math.Abs(bc.Y - pc.Y) <= tolY;
                        if (nearEnough)
                            return AssignObservedTarget(best, "tracked-nearest", false);
                    }
                }
            }

            LogTargetRejectReason("no-live-slot");
            return false;
        }

        private bool AssignObservedTarget(VisibleCell cell, string source, bool hardMatch)
        {
            if (cell == null)
                return false;
            cell.AbsoluteIndex = _target.AbsoluteIndex;
            _currentSnapshot.TargetCell = new ObservedCell
            {
                VisibleCell = cell,
                SelectedGemName = _target.Name,
                SelectedGemRank = _target.Rank,
                SourceText = source,
                MatchTarget = hardMatch,
                ItemButtonLoaded = SafeAnimState(_itemButton) != -1,
                UpgradeButtonAnimState = SafeAnimState(_upgradeButton),
                ViewportEpoch = _viewportEpoch,
            };
            _currentProbeAbsoluteIndex = _target.AbsoluteIndex;
            return true;
        }

        private int GetCurrentViewportBottomRow()
        {
            int currentTop = GetAuthoritativeViewportTopRow();
            if (currentTop < 0)
                return -1;
            return currentTop + GetCurrentViewportVisibleRowCount() - 1;
        }

        private bool IsTargetOutsideCurrentViewport(out bool targetAbove, out bool targetBelow)
        {
            targetAbove = false;
            targetBelow = false;

            if (_target == null || _virtualGrid == null || _virtualGrid.ColumnCount <= 0)
                return false;

            int currentTop = GetAuthoritativeViewportTopRow();
            int currentBottom = GetCurrentViewportBottomRow();
            int targetRow = Math.Max(0, _target.AbsoluteIndex / Math.Max(1, _virtualGrid.ColumnCount));

            if (currentTop < 0 || currentBottom < 0)
            {
                targetBelow = true;
                return true;
            }

            if (targetRow < currentTop)
            {
                targetAbove = true;
                return true;
            }

            if (targetRow > currentBottom)
            {
                targetBelow = true;
                return true;
            }

            return false;
        }

        private bool DidViewportScrollProgress(int direction, int beforeTopRow, float beforeStackTop, float beforeRowPitch, uint beforeTopAcd, out string detail)
        {
            detail = string.Empty;

            if (_currentSnapshot == null)
            {
                detail = "no-snapshot";
                return false;
            }

            UpdateViewportMetricsFromSnapshot();

            int afterTopRow = GetAuthoritativeViewportTopRow();
            if (_lastCaptureHadUsableLiveAcds)
            {
                if (direction > 0 && afterTopRow > beforeTopRow)
                {
                    SetViewportOriginExact(afterTopRow, "acd-scroll");
                    detail = "acd-row " + beforeTopRow + "→" + afterTopRow;
                    return true;
                }
                if (direction < 0 && afterTopRow < beforeTopRow)
                {
                    SetViewportOriginExact(afterTopRow, "acd-scroll");
                    detail = "acd-row " + beforeTopRow + "→" + afterTopRow;
                    return true;
                }
            }

            float rowPitch = beforeRowPitch > 1f ? beforeRowPitch
                : (_virtualGrid != null && _virtualGrid.RowPitch > 1f ? _virtualGrid.RowPitch : _lastMeasuredRowPitch);
            float afterStackTop = GetCurrentStackPanelTop();
            if (float.IsNaN(beforeStackTop) || float.IsNaN(afterStackTop) || rowPitch <= 1f)
            {
                detail = "missing-stack-or-rowpitch";
                return false;
            }

            float deltaY = beforeStackTop - afterStackTop;
            float rowsMovedFloat = deltaY / rowPitch;
            float thresholdRows = 0.25f;

            if (direction > 0 && rowsMovedFloat < thresholdRows)
            {
                detail = "deltaRows=" + rowsMovedFloat.ToString("0.00", CultureInfo.InvariantCulture);
                return false;
            }
            if (direction < 0 && rowsMovedFloat > -thresholdRows)
            {
                detail = "deltaRows=" + rowsMovedFloat.ToString("0.00", CultureInfo.InvariantCulture);
                return false;
            }

            float baseTopRow = _viewportOriginRowFloat >= 0f ? _viewportOriginRowFloat : beforeTopRow;
            SetViewportOriginMeasured(baseTopRow + rowsMovedFloat, "measured-scroll");
            detail = "deltaY=" + deltaY.ToString("0.0", CultureInfo.InvariantCulture)
                + ", rowPitch=" + rowPitch.ToString("0.0", CultureInfo.InvariantCulture)
                + ", rows=" + rowsMovedFloat.ToString("0.00", CultureInfo.InvariantCulture);
            return true;
        }

        private int GetWheelBurstTicks(int rowsHint)
        {
            rowsHint = Math.Max(1, rowsHint);
            if (rowsHint >= 10) return 6;
            if (rowsHint >= 5) return 4;
            if (rowsHint >= 2) return 2;
            return 1;
        }

        private bool TryViewportGuidedArrowScroll(bool goDown, int rowsHint, string reason)
        {
            // REWRITTEN: was synchronous click-hold + Thread.Sleep verification loop (up to ~1180ms blocking).
            // Now uses WheelScrollTick — zero blocking, completes in microseconds, lets the stage machine
            // re-capture and verify on the very next AfterCollect tick.
            if (_currentSnapshot == null)
                return false;

            int direction = goDown ? 1 : -1;
            if (_lastArrowScrollDirection != direction)
            {
                _arrowScrollAttempts = 0;
                _lastArrowScrollDirection = direction;
            }

            int currentTop = GetAuthoritativeViewportTopRow();
            if (goDown && _scrollAtBottom)
                return false;
            if (!goDown && currentTop <= 0)
                return false;

            // Build a hover point inside the list/scrollbar activation area.
            RectangleF listBounds = _currentSnapshot.ListBounds;
            float cx = listBounds.Left + listBounds.Width * 0.50f;
            float cy = goDown
                ? Math.Max(listBounds.Top + 8f, listBounds.Bottom - 10f)
                : Math.Min(listBounds.Bottom - 8f, listBounds.Top + 10f);
            PointF hoverPoint = new PointF(cx, cy);

            if (float.IsNaN(cx) || float.IsNaN(cy) || cy <= 0f)
                return false;

            // Ensure cursor is inside the scroll-activation area before sending wheel ticks.
            // EnsureCursorReadyForWheelScroll returns false when it had to move the cursor,
            // meaning we should wait one tick. In late-TP mode it returns true immediately
            // so the wheel fires on the same tick (see that function).
            if (!EnsureCursorReadyForWheelScroll(hoverPoint, reason,
                    _target != null ? _target.AbsoluteIndex : int.MinValue, currentTop))
                return true; // cursor moved; wheel fires next tick

            // Send multiple wheel ticks for larger distances, capped to avoid over-scrolling.
            // Each tick moves exactly one row. We fire up to 4 at once; if more rows are
            // needed the stage machine will call us again on the next pass.
            rowsHint = Math.Max(1, rowsHint);
            int ticksToSend = GetWheelBurstTicks(rowsHint);

            Log(() => "viewport-scroll " + reason + ": " + (goDown ? "↓" : "↑")
                + " topRow=" + currentTop
                + " rowsHint=" + rowsHint
                + " ticks=" + ticksToSend
                + " wheel=1 (no-sleep)", s7o_AutoGemUpgradeState.DebugLevelState);

            _identityLossCheckPending = true;
            _lastLiveCellCountBeforeScroll = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null
                ? _currentSnapshot.LiveVisibleCells.Count
                : 0;

            for (int i = 0; i < ticksToSend; i++)
                WheelScrollTick(goDown, i == 0 ? reason : null);

            _arrowScrollAttempts = 0;
            if (goDown)
                _scrollAtBottom = false;

            // Schedule async verification on next tick — no Thread.Sleep needed.
            _afterScrollWait = 0;
            _lastActionTick = NowTick();

            Log(() => "viewport-scroll " + reason + ": wheel ticks sent, async verify on next capture", s7o_AutoGemUpgradeState.DebugLevelState);
            return true;
        }

        private bool TryScrollToTargetTopRow(int desiredTopRow)
        {
            int currentTopRow = GetAuthoritativeViewportTopRow();
            if (currentTopRow < 0)
                return false;

            VisibleCell alreadyVisibleTarget;
            if (TryGetLiveVisibleTargetCell(out alreadyVisibleTarget))
            {
                Log(() => "direct-seek skipped: target already visible at row=" + alreadyVisibleTarget.RowIndex.ToString(CultureInfo.InvariantCulture));
                UpdateSharedDebugState("skip-scroll-visible-target", "row=" + alreadyVisibleTarget.RowIndex.ToString(CultureInfo.InvariantCulture), _target != null ? _target.AbsoluteIndex : int.MinValue, alreadyVisibleTarget.RowIndex);
                return true;
            }

            int deltaRows = desiredTopRow - currentTopRow;
            if (deltaRows == 0)
                return true;

            ViewportCapture cap;
            if (!TryCaptureViewport(out cap))
                return false;

            bool downward = deltaRows > 0;
            int absRows = Math.Abs(deltaRows);

            // REWRITTEN: was click-and-hold on scrollbar track with Thread.Sleep (up to 42ms blocking per call).
            // Now uses wheel ticks exclusively — zero blocking, same-tick delivery.
            RectangleF listBounds = cap.ListBounds;
            float cx = listBounds.Left + listBounds.Width * 0.50f;
            float cy = downward
                ? Math.Max(listBounds.Top + 8f, listBounds.Bottom - 10f)
                : Math.Min(listBounds.Bottom - 8f, listBounds.Top + 10f);
            PointF hoverPoint = new PointF(cx, cy);

            if (!EnsureCursorReadyForWheelScroll(hoverPoint, "direct-seek",
                    _target != null ? _target.AbsoluteIndex : int.MinValue, desiredTopRow))
                return true; // cursor moved; wheel fires next tick

            int ticksToSend = GetWheelBurstTicks(absRows);

            Log(() => "direct-seek desiredTop=" + desiredTopRow
                + " currentTop=" + currentTopRow
                + " deltaRows=" + deltaRows
                + " ticks=" + ticksToSend
                + " wheel=1 (no-sleep)", s7o_AutoGemUpgradeState.DebugLevelState);

            _lastLiveCellCountBeforeScroll = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null
                ? _currentSnapshot.LiveVisibleCells.Count : 0;
            _identityLossCheckPending = true;

            for (int i = 0; i < ticksToSend; i++)
                WheelScrollTick(downward, i == 0 ? "direct-seek" : null);

            _afterScrollWait = 0;
            _lastActionTick = NowTick();
            return true;
        }

        private bool TryDragScrollDownRows(int targetRows, bool scrollToBottom = false)
        {
            if (_currentSnapshot == null || _virtualGrid == null)
                return false;

            int currentTop = GetAuthoritativeViewportTopRow();
            int desiredTop = scrollToBottom
                ? GetMaxTopVisibleRow()
                : Math.Min(GetMaxTopScanRow(), currentTop + Math.Max(1, targetRows));

            bool moved = TryViewportGuidedArrowScroll(true, Math.Max(1, desiredTop - currentTop), scrollToBottom ? "search-bottom" : "search");
            if (!moved)
                return false;

            Log(() => "end viewport-guided search scroll: topRow=" + currentTop + "→" + GetAuthoritativeViewportTopRow()
                + ", desired=" + desiredTop
                + (scrollToBottom ? " [bottom]" : string.Empty));
            return true;
        }

        private void ClickUi(IUiElement element)
        {
            if (element == null)
                return;

            _savedCursorX = Hud.Window.CursorX;
            _savedCursorY = Hud.Window.CursorY;
            FreeHudInput.ClickUiElement(MouseButtons.Left, element);
            if (!ShouldKeepCursorAtAutomationActionPoint())
                FreeHudInput.MouseMove(_savedCursorX, _savedCursorY);
            MarkAutomationInputAction();
            UpdateSharedDebugState("click-ui", element.Path, _target != null ? _target.AbsoluteIndex : int.MinValue);
        }

        private void ClickPoint(PointF point, string reason = null, int holdMs = 0)
        {
            _savedCursorX = Hud.Window.CursorX;
            _savedCursorY = Hud.Window.CursorY;
            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            if (!string.IsNullOrWhiteSpace(reason))
                Log(() => "click " + reason + " @(" + x + "," + y + ") hold=" + holdMs);
            FreeHudInput.MouseMove(x, y);
            FreeHudInput.MouseDown(MouseButtons.Left);
            // All scroll callers now use WheelScrollTick (holdMs=0). The only remaining
            // callers that pass holdMs>0 are the urshi-cancel path (intentional) and
            // reset-scroll-up (ScrollHoldMs=0). Cap to 12ms as a hard safety ceiling
            // so no future holdMs regressions can block the thread meaningfully.
            int clampedHold = Math.Min(holdMs, 12);
            if (clampedHold > 0)
            {
                try { Thread.Sleep(clampedHold); } catch { }
            }
            FreeHudInput.MouseUp(MouseButtons.Left);
            if (!ShouldKeepCursorAtAutomationActionPoint())
                FreeHudInput.MouseMove(_savedCursorX, _savedCursorY);
            MarkAutomationInputAction(clampedHold > 0 ? UserInterferenceIgnoreAfterPluginInputMs + clampedHold : -1);
            UpdateSharedDebugState("click-point", (reason ?? string.Empty) + "|hold=" + holdMs.ToString(CultureInfo.InvariantCulture), _target != null ? _target.AbsoluteIndex : int.MinValue);
        }

        private int GetUpgradeAttempts()
        {
            try
            {
                int attempts =
                    Hud.Game.Me.GetAttributeValueAsInt(Hud.Sno.Attributes.Jewel_Upgrades_Bonus, 2147483647, 0)
                    + Hud.Game.Me.GetAttributeValueAsInt(Hud.Sno.Attributes.Jewel_Upgrades_Max, 2147483647, 0)
                    - Hud.Game.Me.GetAttributeValueAsInt(Hud.Sno.Attributes.Jewel_Upgrades_Used, 2147483647, 0);

                return Math.Max(0, attempts);
            }
            catch
            {
                return 0;
            }
        }

        private int SafeAnimState(IUiElement el)
        {
            try { return el != null ? el.AnimState : -1; }
            catch { return -1; }
        }

        private uint SafeItemButtonAcd()
        {
            try { return _itemButton != null ? (uint)_itemButton.LegendaryGemAcdId : 0u; }
            catch { return 0u; }
        }

        private string ReadText(IUiElement el)
        {
            try
            {
                if (el == null || !el.Visible) return string.Empty;
                return el.ReadText(Encoding.UTF8, true) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private bool IsChatEntryOpen()
        {
            try { return _chatEditLine != null && _chatEditLine.Visible; }
            catch { return false; }
        }

        private bool TryCloseChatBeforeGemPaneAutomation(int remainingAttempts)
        {
            try
            {
                if (remainingAttempts <= 0)
                {
                    _lastGemPaneChatCloseTick = int.MinValue;
                    return false;
                }

                if (!IsChatEntryOpen())
                {
                    _lastGemPaneChatCloseTick = int.MinValue;
                    return false;
                }

                int now = NowTick();
                if (_lastGemPaneChatCloseTick != int.MinValue &&
                    now - _lastGemPaneChatCloseTick < ConversationCloseThrottleMs)
                {
                    return true;
                }

                _lastGemPaneChatCloseTick = now;

                // The gem pane is already open and upgrade attempts remain. Chat can cover
                // the gem list/buttons, so close chat, move away from the chat area, then wait
                // for the chat fade before selection/scroll/click automation resumes.
                StartChatCloseFadeWait(false, remainingAttempts, "gem-pane");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int GetChatCloseFadeDelayMs()
        {
            return Math.Max(0, Math.Min(ChatCloseFadeDelayMs, 3000));
        }

        private void ClearChatCloseFadeWait()
        {
            _chatCloseFadeWaitUntilTick = int.MinValue;
            _chatCloseFadePendingDialogSpace = false;
            _chatCloseFadePendingAttempts = int.MinValue;
        }

        private bool TryHoverUrshiAfterChatClose(string reason)
        {
            try
            {
                var urshi = Hud.Game != null && Hud.Game.Actors != null
                    ? Hud.Game.Actors
                        .Where(x =>
                            x != null &&
                            x.SnoActor != null &&
                            x.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem &&
                            x.IsOnScreen)
                        .OrderBy(x =>
                        {
                            try
                            {
                                float dx = x.ScreenCoordinate.X - (Hud.Window.Size.Width * 0.5f);
                                float dy = x.ScreenCoordinate.Y - (Hud.Window.Size.Height * 0.5f);
                                return (dx * dx) + (dy * dy);
                            }
                            catch { return float.MaxValue; }
                        })
                        .FirstOrDefault()
                    : null;

                if (urshi != null)
                {
                    int ux = (int)Math.Round(urshi.ScreenCoordinate.X);
                    int uy = (int)Math.Round(urshi.ScreenCoordinate.Y);

                    if (ux > 0 && uy > 0 && ux < Hud.Window.Size.Width && uy < Hud.Window.Size.Height)
                    {
                        FreeHudInput.MouseMove(ux, uy);
                        UpdateSharedDebugState("hover-urshi-after-chat-close", reason ?? string.Empty, _target != null ? _target.AbsoluteIndex : int.MinValue);
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                // Fallback: move away from the bottom chat area even if Urshi projection is unavailable.
                int x = (int)Math.Round(Hud.Window.Size.Width * 0.50f);
                int y = (int)Math.Round(Hud.Window.Size.Height * 0.42f);
                if (_gemUpgradePane != null && _gemUpgradePane.Visible)
                {
                    RectangleF r = _gemUpgradePane.Rectangle;
                    x = (int)Math.Round(r.Left + r.Width * 0.50f);
                    y = (int)Math.Round(r.Top + Math.Min(60f, Math.Max(20f, r.Height * 0.10f)));
                }

                if (x > 0 && y > 0 && x < Hud.Window.Size.Width && y < Hud.Window.Size.Height)
                {
                    FreeHudInput.MouseMove(x, y);
                    UpdateSharedDebugState("hover-safe-after-chat-close", reason ?? string.Empty, _target != null ? _target.AbsoluteIndex : int.MinValue);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void StartChatCloseFadeWait(bool pendingDialogSpace, int remainingAttempts, string reason)
        {
            int delay = GetChatCloseFadeDelayMs();
            FreeHudInput.SendEscape();
            TryHoverUrshiAfterChatClose(reason);
            _chatCloseFadeWaitUntilTick = NowTick() + delay;
            _chatCloseFadePendingDialogSpace = pendingDialogSpace;
            _chatCloseFadePendingAttempts = remainingAttempts;
            MarkAutomationInputAction(delay + 120);
            UpdateSharedDebugState(pendingDialogSpace ? "escape-chat-before-urshi-dialog" : "escape-chat-before-gem-pane-automation",
                (reason ?? string.Empty) + "|fade=" + delay.ToString(CultureInfo.InvariantCulture), remainingAttempts);
        }

        private bool HandleChatCloseFadeWait()
        {
            if (_chatCloseFadeWaitUntilTick == int.MinValue)
                return false;

            try
            {
                TryHoverUrshiAfterChatClose("chat-fade-wait");

                int now = NowTick();
                if (unchecked(_chatCloseFadeWaitUntilTick - now) > 0)
                    return true;

                if (IsChatEntryOpen())
                {
                    // The configured delay elapsed but the chat edit line is still visible.
                    // Keep waiting without sending Space into chat.
                    _chatCloseFadeWaitUntilTick = now + 50;
                    UpdateSharedDebugState("wait-chat-fade-still-visible", "fade=" + GetChatCloseFadeDelayMs().ToString(CultureInfo.InvariantCulture), _chatCloseFadePendingAttempts);
                    return true;
                }

                bool sendDialogSpace = _chatCloseFadePendingDialogSpace;
                int pendingAttempts = _chatCloseFadePendingAttempts;
                ClearChatCloseFadeWait();

                if (sendDialogSpace && pendingAttempts > 0 && _conversationDialogMain != null && _conversationDialogMain.Visible)
                {
                    _lastConversationCloseTick = NowTick();
                    FreeHudInput.SendSpace();
                    MarkAutomationInputAction(ConversationCloseThrottleMs + 30);
                    UpdateSharedDebugState("space-conversation-dialog-after-chat-fade", "conversation_dialog_main", pendingAttempts);
                    return true;
                }

                return false;
            }
            catch
            {
                ClearChatCloseFadeWait();
                return false;
            }
        }

        private bool TryCloseConversationDialogBeforeGemPane()
        {
            try
            {
                if (_gemUpgradePane != null && _gemUpgradePane.Visible)
                {
                    _lastConversationCloseTick = int.MinValue;
                    return false;
                }

                if (_conversationDialogMain == null || !_conversationDialogMain.Visible)
                {
                    _lastConversationCloseTick = int.MinValue;
                    return false;
                }

                int now = NowTick();

                if (_lastConversationCloseTick != int.MinValue &&
                    now - _lastConversationCloseTick < ConversationCloseThrottleMs)
                {
                    return true;
                }

                _lastConversationCloseTick = now;

                if (IsChatEntryOpen())
                {
                    int remainingAttempts = GetUpgradeAttempts();
                    if (remainingAttempts > 0)
                    {
                        // Chat input owns Space. When upgrade attempts remain, close chat, move the
                        // cursor away from the chat area, wait for the chat fade, then send Space.
                        StartChatCloseFadeWait(true, remainingAttempts, "conversation_dialog_main");
                    }
                    else
                    {
                        // No gem upgrades remain: this is usually a generic reward/close-rift dialog.
                        // Preserve the player's typed chat message and do not send Space into chat.
                        UpdateSharedDebugState("skip-conversation-chat-open-no-upgrades", "conversation_dialog_main", 0);
                    }

                    return true;
                }

                // Do not use Escape here.
                // Space advances conversation without world-clicking after the dialog closes.
                FreeHudInput.SendSpace();

                MarkAutomationInputAction(ConversationCloseThrottleMs + 30);
                UpdateSharedDebugState("space-conversation-dialog", "conversation_dialog_main", -1);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int NowTick() => Environment.TickCount;
        private static int ElapsedMs(int startTick) => startTick == int.MinValue ? int.MaxValue : unchecked(Environment.TickCount - startTick);

        private static bool IsLegendaryGem(IItem item)
        {
            return item != null && item.IsLegendary && item.SnoItem != null && item.SnoItem.MainGroupCode == "gems_unique";
        }

        private static string GetGemName(IItem item)
        {
            return item?.SnoItem != null ? (item.SnoItem.NameEnglish ?? string.Empty) : string.Empty;
        }

        private static string BuildItemKey(IItem item)
        {
            return GetGemName(item) + "|" + item.JewelRank.ToString() + "|" + item.Location.ToString() + "|" + item.InventoryX.ToString() + "|" + item.InventoryY.ToString();
        }

        private static int GetHardCap(string gemName)
        {
            if (string.IsNullOrWhiteSpace(gemName)) return 150;
            int value;
            return HardCapByGemName.TryGetValue(gemName, out value) ? value : 150;
        }

        private static int GetEffectiveStopCap(string gemName, int hardCap)
        {
            if (string.IsNullOrWhiteSpace(gemName)) return hardCap;
            int stopCap;
            if (AutomationStopCapByGemName.TryGetValue(gemName, out stopCap))
                return Math.Min(hardCap, stopCap);
            return hardCap;
        }

        private static string NormalizeGemLabel(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var chars = s.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray();
                return new string(chars).Trim().ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private static string GetShortPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            int idx = path.LastIndexOf("items_list", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? path.Substring(idx) : path;
        }

        private static string ShortSignature(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return "<empty>";
            if (signature.Length <= 42) return signature;
            return signature.Substring(0, 42) + "...";
        }

        private static string ShortEvidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string compact = text.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim();
            while (compact.Contains("  ")) compact = compact.Replace("  ", " ");
            if (compact.Length <= 96) return compact;
            return compact.Substring(0, 96) + "...";
        }

        private bool HasActiveAutomationContext()
        {
            return _autoRunning
                || _probeActive
                || _target != null
                || (_stage != AutomationStage.Idle && _stage != AutomationStage.VerificationComplete && _stage != AutomationStage.Failed);
        }

        private void ResetCursorWatchToCurrent(int ignoreMs = 0)
        {
            try
            {
                _cursorBaselineX = Hud.Window.CursorX;
                _cursorBaselineY = Hud.Window.CursorY;
            }
            catch
            {
                _cursorBaselineX = int.MinValue;
                _cursorBaselineY = int.MinValue;
            }

            int delay = Math.Max(0, ignoreMs);
            _cursorIgnoreUntilTick = delay > 0 ? NowTick() + delay : int.MinValue;
        }

        private void MarkAutomationInputAction(int ignoreMs = -1)
        {
            int delay = ignoreMs >= 0 ? ignoreMs : UserInterferenceIgnoreAfterPluginInputMs;
            ResetCursorWatchToCurrent(Math.Max(0, delay));
        }

        private void UpdateSharedDebugState(string action, string detail = null, int targetAbs = int.MinValue, int targetRow = int.MinValue)
        {
            try
            {
                s7o_AutoGemUpgradeState.AutoGemDebugAction = action ?? string.Empty;
                s7o_AutoGemUpgradeState.AutoGemDebugDetail = detail ?? string.Empty;
                if (targetAbs != int.MinValue) s7o_AutoGemUpgradeState.AutoGemDebugTargetAbs = targetAbs;
                if (targetRow != int.MinValue) s7o_AutoGemUpgradeState.AutoGemDebugTargetRow = targetRow;
            }
            catch { }
        }

        private bool ShouldKeepCursorAtAutomationActionPoint()
        {
            return _autoRunning || _target != null;
        }

        private bool TryGetLiveVisibleTargetCell(out VisibleCell liveCell)
        {
            liveCell = null;
            try
            {
                if (_target == null || _currentSnapshot == null) return false;
                if (_currentSnapshot.TargetCell != null
                    && _currentSnapshot.TargetCell.VisibleCell != null
                    && _currentSnapshot.TargetCell.MatchTarget
                    && !_currentSnapshot.TargetCell.VisibleCell.IsProjected
                    && IsCurrentEpochLiveSlot(_currentSnapshot.TargetCell.VisibleCell))
                {
                    liveCell = _currentSnapshot.TargetCell.VisibleCell;
                    return true;
                }

                if (_currentSnapshot.LiveVisibleCells == null || _currentSnapshot.LiveVisibleCells.Count == 0)
                    return false;

                foreach (var cell in _currentSnapshot.LiveVisibleCells)
                {
                    int absIndex;
                    if (cell != null
                        && !cell.IsProjected
                        && IsCurrentEpochLiveSlot(cell)
                        && TryGetPredictedAbsoluteIndex(cell, out absIndex)
                        && absIndex == _target.AbsoluteIndex)
                    {
                        liveCell = cell;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool TryHoverUpgradeButton(string modeTag, GemTarget plannedTarget)
        {
            if (!ShouldKeepCursorAtAutomationActionPoint())
                return false;
            if (plannedTarget == null || _upgradeButton == null || !_upgradeButton.Visible)
                return false;

            RectangleF rect;
            try { rect = _upgradeButton.Rectangle; }
            catch { return false; }

            if (rect == RectangleF.Empty || rect.Width <= 1f || rect.Height <= 1f)
                return false;

            PointF p = new PointF(rect.Left + rect.Width * 0.50f, rect.Top + rect.Height * 0.50f);
            MoveCursorToPointNoClick(p, "keep-upgrade-button-" + modeTag);
            Log(() => "keep-upgrade-button: mode=" + modeTag
                + ", abs=" + plannedTarget.AbsoluteIndex.ToString(CultureInfo.InvariantCulture)
                + ", name='" + plannedTarget.Name + "'");
            UpdateSharedDebugState("keep-upgrade-button", modeTag + " same-target", plannedTarget.AbsoluteIndex, int.MinValue);
            return true;
        }

        private void MoveCursorToPointNoClick(PointF point, string reason)
        {
            if (!ShouldKeepCursorAtAutomationActionPoint())
                return;

            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            FreeHudInput.MouseMove(x, y);
            MarkAutomationInputAction();
            if (!string.IsNullOrWhiteSpace(reason))
                Log(() => "hover " + reason + " @(" + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private bool TryFindLiveVisibleCellByAbsIndex(int absoluteIndex, out VisibleCell liveCell)
        {
            liveCell = null;
            try
            {
                if (_currentSnapshot == null || _currentSnapshot.LiveVisibleCells == null || _currentSnapshot.LiveVisibleCells.Count == 0)
                    return false;

                foreach (var cell in _currentSnapshot.LiveVisibleCells)
                {
                    int absIndex;
                    if (cell != null
                        && !cell.IsProjected
                        && IsCurrentEpochLiveSlot(cell)
                        && TryGetPredictedAbsoluteIndex(cell, out absIndex)
                        && absIndex == absoluteIndex)
                    {
                        liveCell = cell;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool TryFindLiveVisibleCellForPlannedTarget(GemTarget plannedTarget, out VisibleCell liveCell)
        {
            liveCell = null;
            try
            {
                if (plannedTarget == null || _currentSnapshot == null || _currentSnapshot.LiveVisibleCells == null || _currentSnapshot.LiveVisibleCells.Count == 0)
                    return false;

                uint plannedAcd = SafeGemOrderEntryAcd(plannedTarget.Source);
                if (plannedAcd != 0 && plannedAcd != 0xFFFFFFFF)
                {
                    liveCell = _currentSnapshot.LiveVisibleCells.FirstOrDefault(c => c != null
                        && !c.IsProjected
                        && IsCurrentEpochLiveSlot(c)
                        && c.Ref != null
                        && c.Ref.CachedLegendaryGemAcdId == plannedAcd);
                    if (liveCell != null)
                        return true;
                }

                return TryFindLiveVisibleCellByAbsIndex(plannedTarget.AbsoluteIndex, out liveCell);
            }
            catch { }
            liveCell = null;
            return false;
        }

        private bool TryGetWheelComfortHoverPoint(VisibleCell cell, out PointF hoverPoint, out bool downward)
        {
            hoverPoint = PointF.Empty;
            downward = false;

            if (cell == null || cell.IsProjected || _currentSnapshot == null)
                return false;
            if (_currentSnapshot.ListBounds == RectangleF.Empty)
                return false;
            if (!IsCurrentEpochLiveSlot(cell))
                return false;

            RectangleF safeVisibleRect;
            PointF safeVisiblePoint;
            if (TryGetSafeVisibleClickRect(cell, out safeVisibleRect, out safeVisiblePoint))
                return false;

            RectangleF comfortBounds = GetTargetComfortBounds(_currentSnapshot.ListBounds);
            float topOverflow;
            float bottomOverflow;
            bool comfortable = IsCellComfortablyInsideViewport(cell, comfortBounds, out topOverflow, out bottomOverflow);

            float rowPitch = _absoluteGrid != null && _absoluteGrid.RowPitch > 1f
                ? _absoluteGrid.RowPitch
                : (_lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : Math.Max(40f, cell.Rect.Height));

            int maxObservedRow = -1;
            try
            {
                if (_currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null && _currentSnapshot.LiveVisibleCells.Count > 0)
                    maxObservedRow = _currentSnapshot.LiveVisibleCells.Max(c => c.RowIndex);
            }
            catch { }

            bool onBottomVisibleRow = maxObservedRow >= 0 && cell.RowIndex >= maxObservedRow;
            bool nearBottomEdge = cell.Rect.Bottom >= (comfortBounds.Bottom - Math.Max(4f, Math.Min(10f, rowPitch * 0.10f)));
            bool forceBottomNudge = onBottomVisibleRow && (bottomOverflow > 0.5f || nearBottomEdge);
            bool edgeRow = forceBottomNudge;
            if (edgeRow)
                comfortable = false;

            if (comfortable)
                return false;

            downward = forceBottomNudge || bottomOverflow > topOverflow;
            float overflow = Math.Max(topOverflow, bottomOverflow);
            // CHANGED: was (edgeRow || overflow < rowPitch*0.22f). Widened to always provide
            // a hover point so the cursor is reliably inside the list before the wheel fires,
            // regardless of how much of the cell is clipped. Matches TryApplyComfortNudge.
            bool useWheel = true;
            if (!useWheel)
                return false;

            float halfW = Math.Max(4f, cell.Rect.Width * 0.50f);
            float halfH = Math.Max(4f, cell.Rect.Height * 0.50f);
            float margin = Math.Max(4f, Math.Min(10f, rowPitch * 0.10f));
            float cx = Math.Max(comfortBounds.Left + halfW, Math.Min(comfortBounds.Right - halfW, cell.Rect.Left + (cell.Rect.Width * 0.50f)));
            float cy = downward
                ? (comfortBounds.Bottom - halfH - margin)
                : (comfortBounds.Top + halfH + margin);

            int currentTop = GetAuthoritativeViewportTopRow();
            if (downward && onBottomVisibleRow && currentTop == 0)
                cy = Math.Max(comfortBounds.Top + halfH + margin, cy - rowPitch);

            hoverPoint = new PointF(cx, cy);
            return true;
        }

        private void WheelScrollTick(bool downward, string reason)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    Log(() => "wheel " + reason + " " + (downward ? "down" : "up") + " tick=1");

                if (downward)
                    FreeHudInput.ScrollDown(1);
                else
                    FreeHudInput.ScrollUp(1);

                MarkAutomationInputAction();
            }
            catch { }
        }

        private RectangleF GetWheelScrollActivationRect()
        {
            RectangleF activation = RectangleF.Empty;
            try
            {
                if (_currentSnapshot != null && _currentSnapshot.ListBounds != RectangleF.Empty)
                    activation = _currentSnapshot.ListBounds;

                if (_scrollBar != null && _scrollBar.Visible)
                {
                    RectangleF scrollRect = _scrollBar.Rectangle;
                    if (scrollRect != RectangleF.Empty && scrollRect.Width > 1f && scrollRect.Height > 1f)
                        activation = activation == RectangleF.Empty ? scrollRect : RectangleF.Union(activation, scrollRect);
                }
            }
            catch { }
            return activation;
        }

        // Rev 5.6.10: a strictly-inset safe rect used for wheel-scroll cursor safety.
        // The raw list bounds are the "visually visible" area, but mouse-wheel events at
        // the extreme edges can leak into the game world (via force-move bind or sibling
        // UI elements), which can pull the character away from Urshi mid-run.  We inset
        // by a conservative margin so wheel-arm and the IsCursorInsideRect check treat
        // the edges as unsafe.  12px chosen because cells are ~58px — 12px is ~20% of a
        // cell, comfortably away from the list border without shrinking the usable area
        // more than necessary.
        private const float WheelSafeInsetPx = 12f;

        private RectangleF GetWheelSafeInsetRect()
        {
            RectangleF raw = GetWheelScrollActivationRect();
            if (raw == RectangleF.Empty || raw.Width <= (WheelSafeInsetPx * 2f + 4f) || raw.Height <= (WheelSafeInsetPx * 2f + 4f))
                return raw; // too small to inset safely — leave as-is
            return new RectangleF(
                raw.Left + WheelSafeInsetPx,
                raw.Top + WheelSafeInsetPx,
                raw.Width - (WheelSafeInsetPx * 2f),
                raw.Height - (WheelSafeInsetPx * 2f));
        }

        private PointF ClampPointToWheelSafeRect(PointF p)
        {
            RectangleF safe = GetWheelSafeInsetRect();
            if (safe == RectangleF.Empty || safe.Width <= 0f || safe.Height <= 0f)
                return p;
            float x = p.X;
            float y = p.Y;
            if (x < safe.Left) x = safe.Left;
            else if (x > safe.Right) x = safe.Right;
            if (y < safe.Top) y = safe.Top;
            else if (y > safe.Bottom) y = safe.Bottom;
            return new PointF(x, y);
        }

        private bool IsCursorInsideRect(RectangleF rect)
        {
            if (rect == RectangleF.Empty || rect.Width <= 1f || rect.Height <= 1f)
                return false;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;
            return x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        private bool EnsureCursorReadyForWheelScroll(PointF hoverPoint, string reason, int targetAbs, int targetRow)
        {
            // Rev 5.6.10: use the safe-inset rect for both the "is cursor safe" check
            // and the final hover position.  Wheel events at the raw list edge can leak
            // into the game world (force-move bind), so we refuse to accept an edge
            // cursor as "ready" and we clamp any hover point to the inset rect before
            // moving the cursor to it.
            RectangleF safeInset = GetWheelSafeInsetRect();

            // Rev 5.7.0: add 1px tolerance to the "already safe" check only.  If
            // ListBounds pixel-jitters by 1-2px between the arm tick and the next
            // capture tick, a cursor placed at exactly safeInset.Bottom (835.0) can
            // land outside the freshly-recomputed safe rect (now 834.5) and trigger
            // another arm instead of firing the wheel.  The move target (ClampPoint…)
            // still uses the exact 12px inset so force-move safety is unchanged.
            RectangleF safeCheck = (safeInset == RectangleF.Empty || safeInset.Width <= 2f || safeInset.Height <= 2f)
                ? safeInset
                : new RectangleF(safeInset.X - 1f, safeInset.Y - 1f, safeInset.Width + 2f, safeInset.Height + 2f);
            if (IsCursorInsideRect(safeCheck))
                return true;

            PointF safePoint = ClampPointToWheelSafeRect(hoverPoint);

            // Move cursor into the safe activation area.
            MoveCursorToPointNoClick(safePoint, "arm-wheel-" + reason);
            UpdateSharedDebugState("wheel-arm", reason, targetAbs, targetRow);
            _lastActionTick = NowTick();

            // Rev 5.6.11: ALWAYS return false on the arm tick.  Previously late-TP mode
            // returned true so the caller could fire the wheel on the same tick as the
            // move, but that caused the wheel event to fire while the cursor may not yet
            // have settled inside the safe rect — triggering the user's force-move bind
            // and walking the character out of Urshi range.  The one-tick cost is far
            // cheaper than a drift-out-of-range failure.
            return false;
        }

        /// <summary>
        /// When a planned target is exactly one row above or below the current viewport, perform a direct wheel tick
        /// immediately after lockout ends. This avoids waiting for comfort-nudge logic to trigger the first movement.
        /// Returns true if an action was taken or the cursor was moved to prepare for scrolling; false otherwise.
        /// </summary>
        private bool TryCommitImmediateAdjacentWheelStep()
        {
            // only perform once per target
            if (_directAdjacentStepDone)
                return false;

            // require current snapshot and target information
            if (_currentSnapshot == null || _virtualGrid == null || _target == null)
                return false;

            if (_virtualGrid.ColumnCount <= 0)
                return false;

            // determine current viewport row range
            int currentTop = GetAuthoritativeViewportTopRow();
            int currentBottom = GetCurrentViewportBottomRow();
            if (currentTop < 0 || currentBottom < 0)
                return false;

            // determine target row
            int targetRow = Math.Max(0, _target.AbsoluteIndex / Math.Max(1, _virtualGrid.ColumnCount));

            // check if target is exactly one row above or below the viewport
            bool downward;
            bool isAdjacent;
            if (targetRow == currentBottom + 1)
            {
                downward = true;
                isAdjacent = true;
            }
            else if (targetRow == currentTop - 1)
            {
                downward = false;
                isAdjacent = true;
            }
            else
            {
                isAdjacent = false;
                downward = false;
            }

            if (!isAdjacent)
                return false;

            // compute a hover point near the scroll activation area similar to TryHoverAdjacentPlannedTargetWheelPrearm
            RectangleF listBounds = _currentSnapshot.ListBounds;
            if (listBounds == RectangleF.Empty || listBounds.Width <= 1f || listBounds.Height <= 1f)
                return false;

            // use measured row pitch or defaults to estimate size and margins
            float rowPitch = _absoluteGrid.RowPitch > 1f
                ? _absoluteGrid.RowPitch
                : (_lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : (_absoluteGrid.CellHeight > 1f ? _absoluteGrid.CellHeight : 58f));
            float cellWidth = _absoluteGrid.CellWidth > 1f ? _absoluteGrid.CellWidth : Math.Min(58f, Math.Max(24f, listBounds.Width * 0.14f));
            float cellHeight = _absoluteGrid.CellHeight > 1f ? _absoluteGrid.CellHeight : Math.Min(58f, Math.Max(24f, rowPitch));
            float halfW = Math.Max(4f, cellWidth * 0.50f);
            float halfH = Math.Max(4f, cellHeight * 0.50f);
            float margin = Math.Max(4f, Math.Min(10f, rowPitch * 0.10f));

            // choose horizontal center at middle of list
            float cx = listBounds.Left + (listBounds.Width * 0.50f);
            // clamp within list bounds
            cx = Math.Max(listBounds.Left + halfW, Math.Min(listBounds.Right - halfW, cx));

            // choose y based on direction
            float cy = downward
                ? (listBounds.Bottom - halfH - margin)
                : (listBounds.Top + halfH + margin);

            PointF hoverPoint = new PointF(cx, cy);

            // ensure cursor is ready for wheel scroll; if cursor needed to move, return true but do not scroll yet
            if (!EnsureCursorReadyForWheelScroll(hoverPoint, "adjacent-direct", _target.AbsoluteIndex, targetRow))
            {
                // cursor has been moved; we will wait for next tick to perform the wheel tick
                return true;
            }

            // send the wheel tick immediately
            WheelScrollTick(downward, "adjacent-direct");
            UpdateSharedDebugState("adjacent-commit-immediate", (downward ? "down" : "up"), _target.AbsoluteIndex, targetRow);

            // mark that we have performed the immediate adjacent commit
            _directAdjacentStepDone = true;
            _lastActionTick = NowTick();
            // no additional wait needed here; allow next capture shortly
            _afterScrollWait = 0;
            return true;
        }

        private void TryCorrectCursorAfterWheelNudge(VisibleCell cell, string reason)
        {
            if (!_wheelPostNudgeCorrectionPending)
                return;

            if (cell == null || _target == null || _wheelPostNudgeTargetAbs != _target.AbsoluteIndex)
            {
                _wheelPostNudgeCorrectionPending = false;
                _wheelPostNudgeTargetAbs = -1;
                return;
            }

            RectangleF safeRect;
            PointF actualPoint;
            if (!TryGetSafeVisibleClickRect(cell, out safeRect, out actualPoint))
            {
                safeRect = cell.Rect;
                float insetX = Math.Min(14f, Math.Max(6f, safeRect.Width * 0.18f));
                float insetY = Math.Min(14f, Math.Max(6f, safeRect.Height * 0.18f));
                safeRect = new RectangleF(
                    safeRect.Left + insetX,
                    safeRect.Top + insetY,
                    Math.Max(1f, safeRect.Width - insetX * 2f),
                    Math.Max(1f, safeRect.Height - insetY * 2f));
                actualPoint = new PointF(safeRect.Left + safeRect.Width * 0.50f, safeRect.Top + safeRect.Height * 0.50f);
            }

            int curX = Hud.Window.CursorX;
            int curY = Hud.Window.CursorY;
            bool insideSafe = curX >= safeRect.Left && curX <= safeRect.Right && curY >= safeRect.Top && curY <= safeRect.Bottom;
            if (!insideSafe)
            {
                MoveCursorToPointNoClick(actualPoint, "wheel-correct-" + reason);
                UpdateSharedDebugState("wheel-correct", reason, _target.AbsoluteIndex, cell.RowIndex);
            }

            _wheelPostNudgeCorrectionPending = false;
            _wheelPostNudgeTargetAbs = -1;
        }

        private bool TryHoverPredictedVisiblePlannedTarget(GemTarget plannedTarget, string modeTag)
        {
            if (plannedTarget == null || _currentSnapshot == null || _absoluteGrid == null)
                return false;

            if (_currentSnapshot.ListBounds == RectangleF.Empty)
                return false;

            if (_absoluteGrid.Slots == null || plannedTarget.AbsoluteIndex < 0 || plannedTarget.AbsoluteIndex >= _absoluteGrid.Slots.Count)
                return false;

            var slot = _absoluteGrid.Slots[plannedTarget.AbsoluteIndex];
            if (slot == null || slot.PredictedRect == RectangleF.Empty)
                return false;

            RectangleF visibleRect = RectangleF.Intersect(slot.PredictedRect, _currentSnapshot.ListBounds);
            if (visibleRect == RectangleF.Empty || visibleRect.Width <= 1f || visibleRect.Height <= 1f)
                return false;

            float fullArea = Math.Max(1f, slot.PredictedRect.Width * slot.PredictedRect.Height);
            float visibleFraction = (visibleRect.Width * visibleRect.Height) / fullArea;
            if (visibleFraction < 0.55f)  // consistent with TryGetSafeVisibleClickRect threshold
                return false;

            float insetX = Math.Min(10f, Math.Max(4f, visibleRect.Width * 0.15f));
            float insetY = Math.Min(10f, Math.Max(4f, visibleRect.Height * 0.15f));

            RectangleF safeRect = new RectangleF(
                visibleRect.Left + insetX,
                visibleRect.Top + insetY,
                Math.Max(1f, visibleRect.Width - insetX * 2f),
                Math.Max(1f, visibleRect.Height - insetY * 2f));

            PointF safePoint = new PointF(
                safeRect.Left + safeRect.Width * 0.50f,
                safeRect.Top + safeRect.Height * 0.50f);

            MoveCursorToPointNoClick(safePoint, "hover-target-" + modeTag + "-predicted");

            int localRow = slot.AbsoluteRow - Math.Max(0, _absoluteGrid.ViewportTopRowInt);
            UpdateSharedDebugState("hover-target", modeTag + " predicted-visible", plannedTarget.AbsoluteIndex, localRow);

            return true;
        }

        private bool TryHoverAdjacentPlannedTargetWheelPrearm(GemTarget plannedTarget, string modeTag)
        {
            if (plannedTarget == null || _currentSnapshot == null || _absoluteGrid == null)
                return false;

            if (_currentSnapshot.ListBounds == RectangleF.Empty)
                return false;

            if (_absoluteGrid.Slots == null || plannedTarget.AbsoluteIndex < 0 || plannedTarget.AbsoluteIndex >= _absoluteGrid.Slots.Count)
                return false;

            var slot = _absoluteGrid.Slots[plannedTarget.AbsoluteIndex];
            if (slot == null)
                return false;

            int currentTop = GetAuthoritativeViewportTopRow();
            int currentBottom = GetCurrentViewportBottomRow();
            if (currentTop < 0 || currentBottom < 0)
                return false;

            bool downward;
            if (slot.AbsoluteRow == currentBottom + 1)
                downward = true;
            else if (slot.AbsoluteRow == currentTop - 1)
                downward = false;
            else
                return false;

            RectangleF listBounds = _currentSnapshot.ListBounds;
            if (listBounds == RectangleF.Empty || listBounds.Width <= 1f || listBounds.Height <= 1f)
                return false;

            float rowPitch = _absoluteGrid.RowPitch > 1f
                ? _absoluteGrid.RowPitch
                : (_lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : (_absoluteGrid.CellHeight > 1f ? _absoluteGrid.CellHeight : 58f));
            float cellWidth = _absoluteGrid.CellWidth > 1f ? _absoluteGrid.CellWidth : Math.Min(58f, Math.Max(24f, listBounds.Width * 0.14f));
            float cellHeight = _absoluteGrid.CellHeight > 1f ? _absoluteGrid.CellHeight : Math.Min(58f, Math.Max(24f, rowPitch));
            float halfW = Math.Max(4f, cellWidth * 0.50f);
            float halfH = Math.Max(4f, cellHeight * 0.50f);
            float margin = Math.Max(4f, Math.Min(10f, rowPitch * 0.10f));

            float cx = slot.PredictedRect != RectangleF.Empty
                ? slot.PredictedRect.Left + (slot.PredictedRect.Width * 0.50f)
                : (listBounds.Left + (listBounds.Width * 0.50f));
            cx = Math.Max(listBounds.Left + halfW, Math.Min(listBounds.Right - halfW, cx));

            float cy = downward
                ? (listBounds.Bottom - halfH - margin)
                : (listBounds.Top + halfH + margin);

            MoveCursorToPointNoClick(new PointF(cx, cy), "hover-wheel-target-" + modeTag);
            UpdateSharedDebugState("hover-wheel-target",
                modeTag + " adjacent-" + (downward ? "down" : "up"),
                plannedTarget.AbsoluteIndex,
                slot.AbsoluteRow - currentTop);

            return true;
        }

        private void TryPrepositionForPlannedTarget(int upgrades, string modeTag)
        {
            if (!ShouldKeepCursorAtAutomationActionPoint())
                return;

            if (_currentSnapshot == null || _currentSnapshot.ListBounds == RectangleF.Empty)
                return;

            GemTarget planned = null;
            try
            {
                bool successAware = WasLastUpgradeSuccessful();
                if (AutoPercentMode)
                {
                    TryGetPlannedAutoTarget(upgrades, out planned, successAware);
                }
                else if (IsLowestBalanceMode())
                {
                    int planPointer = _lowestPlanPointer;
                    if (successAware && _lowestPlanSequence != null && _lowestPlanSequence.Count > 0)
                        planPointer = Math.Min(_lowestPlanSequence.Count - 1, Math.Max(0, _lowestPlanPointer + 1));
                    TryGetLowestPlannedTarget(planPointer, out planned);
                }
                else
                {
                    string ignoredWarning;
                    string ignoredFailure;
                    TryChoosePersistentModeTarget(upgrades, out planned, out ignoredWarning, out ignoredFailure, successAware);
                }
            }
            catch { planned = null; }

            if (planned == null || planned.Source == null)
                return;

            bool sameAsCurrent = _target != null && planned.AbsoluteIndex == _target.AbsoluteIndex;
            if (sameAsCurrent && TryHoverUpgradeButton(modeTag, planned))
                return;

            int abs = planned.AbsoluteIndex;
            VisibleCell visible;
            if (TryFindLiveVisibleCellForPlannedTarget(planned, out visible) && visible != null)
            {
                RectangleF safeVisibleRect;
                PointF safeVisiblePoint;
                if (TryGetSafeVisibleClickRect(visible, out safeVisibleRect, out safeVisiblePoint))
                {
                    MoveCursorToPointNoClick(safeVisiblePoint, "hover-target-" + modeTag);
                    UpdateSharedDebugState("hover-target", modeTag + " safe-visible", abs, visible.RowIndex);
                    return;
                }

                PointF wheelHoverPoint;
                bool wheelDownward;
                if (TryGetWheelComfortHoverPoint(visible, out wheelHoverPoint, out wheelDownward))
                {
                    MoveCursorToPointNoClick(wheelHoverPoint, "hover-wheel-target-" + modeTag);
                    UpdateSharedDebugState("hover-wheel-target", modeTag + " " + (wheelDownward ? "down" : "up"), abs, visible.RowIndex);
                    return;
                }

                float cx = visible.Rect.Left + (visible.Rect.Width * 0.50f);
                float cy = visible.Rect.Top + (visible.Rect.Height * 0.50f);
                MoveCursorToPointNoClick(new PointF(cx, cy), "hover-target-" + modeTag);
                UpdateSharedDebugState("hover-target", modeTag + " cell", abs, visible.RowIndex);
                return;
            }

            if (TryHoverPredictedVisiblePlannedTarget(planned, modeTag))
                return;

            int desiredTop = GetDesiredTopScanRowForAbsoluteIndex(abs);
            int currentTop = GetAuthoritativeViewportTopRow();
            if (currentTop < 0)
                return;

            if (IsTargetRowInCurrentViewport(planned))
            {
                UpdateSharedDebugState("hold-preposition", modeTag + " row-visible-no-live", abs, int.MinValue);
                return;
            }

            if (TryHoverAdjacentPlannedTargetWheelPrearm(planned, modeTag))
                return;

            bool downward = desiredTop > currentTop;
            PointF p = GetFastScrollPoint(_currentSnapshot.PaneRect, _currentSnapshot.ListBounds, downward);
            MoveCursorToPointNoClick(p, "hover-scroll-" + modeTag);
            UpdateSharedDebugState("hover-scroll", modeTag + " scroll", abs, int.MinValue);
        }

        private void ClearSoftRestartWait(bool clearWindow)
        {
            _softRestartPending = false;
            _softRestartBlockedUntilTick = int.MinValue;
            _userSettleUntilTick = int.MinValue;
            _lastUserInterferenceTick = int.MinValue;
            if (clearWindow)
            {
                _softRestartWindowStartTick = int.MinValue;
                _softRestartCountInWindow = 0;
            }
            ResetCursorWatchToCurrent(0);
        }

        private bool HandleSoftRestartWait()
        {
            if (!_softRestartPending)
                return false;

            int now = NowTick();
            int curX = Hud.Window.CursorX;
            int curY = Hud.Window.CursorY;

            if (_cursorBaselineX == int.MinValue || _cursorBaselineY == int.MinValue)
            {
                _cursorBaselineX = curX;
                _cursorBaselineY = curY;
                _userSettleUntilTick = now + Math.Max(120, UserInterferenceSettleDelayMs);
                return true;
            }

            int threshold = Math.Max(4, UserInterferenceCursorThresholdPx);
            if (Math.Abs(curX - _cursorBaselineX) >= threshold || Math.Abs(curY - _cursorBaselineY) >= threshold)
            {
                _cursorBaselineX = curX;
                _cursorBaselineY = curY;
                _userSettleUntilTick = now + Math.Max(120, UserInterferenceSettleDelayMs);
                _lastUserInterferenceTick = now;
                return true;
            }

            if (now < _softRestartBlockedUntilTick || now < _userSettleUntilTick)
                return true;

            _softRestartPending = false;
            _softRestartBlockedUntilTick = int.MinValue;
            _userSettleUntilTick = int.MinValue;
            ResetCursorWatchToCurrent(UserInterferenceIgnoreAfterPluginInputMs);
            Log(() => "soft restart resume: cursor settled, restarting acquisition");
            return false;
        }

        private bool DetectUserInterference()
        {
            if (!HasActiveAutomationContext())
            {
                if (!_softRestartPending)
                    ResetCursorWatchToCurrent(0);
                return false;
            }

            int now = NowTick();
            if (now < _cursorIgnoreUntilTick)
                return false;

            if (_cursorBaselineX == int.MinValue || _cursorBaselineY == int.MinValue)
            {
                ResetCursorWatchToCurrent(0);
                return false;
            }

            int curX = Hud.Window.CursorX;
            int curY = Hud.Window.CursorY;
            int dx = Math.Abs(curX - _cursorBaselineX);
            int dy = Math.Abs(curY - _cursorBaselineY);
            if (dx < Math.Max(4, UserInterferenceCursorThresholdPx) && dy < Math.Max(4, UserInterferenceCursorThresholdPx))
                return false;

            SoftAbortAndRestart("user interference detected dx=" + dx + ", dy=" + dy);
            return true;
        }

        private void SoftAbortAndRestart(string reason)
        {
            int now = NowTick();
            int windowMs = Math.Max(1000, SoftRestartWindowMs);
            if (_softRestartWindowStartTick == int.MinValue || ElapsedMs(_softRestartWindowStartTick) > windowMs)
            {
                _softRestartWindowStartTick = now;
                _softRestartCountInWindow = 0;
            }

            _softRestartCountInWindow++;
            if (_softRestartCountInWindow > Math.Max(1, MaxSoftRestartsPerWindow))
            {
                Fail((reason ?? "recoverable failure") + " (soft-restart limit reached)");
                return;
            }

            int curX = 0;
            int curY = 0;
            try
            {
                curX = Hud.Window.CursorX;
                curY = Hud.Window.CursorY;
            }
            catch { }

            // Save run-level portal baseline before wiping state.
            // A soft restart is a navigation/viewport recovery — it is NOT a new run.
            // ResetState() clears _initialUpgradeAttemptsThisRun and _portalRequestedThisRun,
            // causing TryRequestTimedPortalDuringRun to misclassify the surviving run as a
            // fresh below-threshold reopen and fire a second DoAction(TownPortal) — which
            // closes the pane before the in-flight upgrade result can land.
            int  savedInitialAttempts         = _initialUpgradeAttemptsThisRun;
            int  savedLastObservedAttempts    = _lastObservedUpgradeAttempts;
            bool savedPortalRequestedThisRun  = _portalRequestedThisRun;
            int  savedPortalAnchorClickTick   = _portalAnchorClickTick;
            int  savedPortalRequestedTick     = _portalRequestedTick;
            bool savedHasSentInitialClick     = _hasSentInitialUpgradeClick;
            int  savedFirstUpgradeClickTick   = _firstUpgradeClickTick;
            bool savedUpgradeProgressObserved = _upgradeProgressObservedThisRun;
            int  savedLastUpgradeProgressTick = _lastUpgradeProgressTick;
            int  savedNoProgressAbortTick     = _noProgressAbortTick;

            Log(() => "soft restart: " + (reason ?? "recoverable failure"));
            ResetState();

            // Restore run-level portal fields only when a run was already in progress.
            // Condition: savedInitialAttempts != int.MinValue means HandleRunningState
            // had already recorded the baseline — this is definitely an in-pane restart.
            if (savedInitialAttempts != int.MinValue)
            {
                _initialUpgradeAttemptsThisRun  = savedInitialAttempts;
                _lastObservedUpgradeAttempts    = savedLastObservedAttempts;
                _portalRequestedThisRun         = savedPortalRequestedThisRun;
                _portalAnchorClickTick          = savedPortalAnchorClickTick;
                _portalRequestedTick            = savedPortalRequestedTick;
                _hasSentInitialUpgradeClick     = savedHasSentInitialClick;
                _firstUpgradeClickTick          = savedFirstUpgradeClickTick;
                _upgradeProgressObservedThisRun = savedUpgradeProgressObserved;
                _lastUpgradeProgressTick        = savedLastUpgradeProgressTick;
                _noProgressAbortTick            = savedNoProgressAbortTick;
                Log(() => "soft restart: run-level portal state preserved"
                    + " (initialAttempts=" + savedInitialAttempts
                    + " portalRequested=" + (savedPortalRequestedThisRun ? "1" : "0")
                    + " lastObserved=" + (savedLastObservedAttempts == int.MinValue ? "na" : savedLastObservedAttempts.ToString(CultureInfo.InvariantCulture))
                    + ")");
            }

            _softRestartPending = true;
            _softRestartBlockedUntilTick = now + Math.Max(0, SoftRestartBackoffMs);
            _userSettleUntilTick = now + Math.Max(120, UserInterferenceSettleDelayMs);
            _lastUserInterferenceTick = now;
            _cursorBaselineX = curX;
            _cursorBaselineY = curY;
            _cursorIgnoreUntilTick = now + Math.Max(80, UserInterferenceIgnoreAfterPluginInputMs);
        }

        private void Fail(string reason)
        {
            _stage = AutomationStage.Failed;
            _autoRunning = false;
            _lastFailureReason = reason ?? "unknown failure";
            Log(() => "FAIL: " + _lastFailureReason);
        }

        private void Log(string message)
        {
            // Forward simple log calls to the lazy logger with state-level severity.
            Log(() => message, s7o_AutoGemUpgradeState.DebugLevelState);
        }

        private void ResetState()
        {
            _stage = AutomationStage.Idle;
            _lastFailureReason = string.Empty;
            _lastActionTick = int.MinValue;
            _lastUpgradeClickTick = int.MinValue;
            _portalAnchorClickTick = int.MinValue;
            _lastObservedUpgradeAttempts = int.MinValue;
            _lastUpgradeProgressTick = int.MinValue;
            _lastPortalActionTick = int.MinValue;
            _portalRequestedTick = int.MinValue;
            _lastRecoveryUpgradeAttempts = int.MinValue;
            _runningStartTick = int.MinValue;
            _targetValidationStartTick = int.MinValue;
            _targetValidationAttempts = 0;
            _targetComfortNudgeAttempts = 0;
            _wheelPostNudgeCorrectionPending = false;
            _wheelPostNudgeTargetAbs = -1;
            _firstUpgradeClickTick = int.MinValue;
            _initialUpgradeAttemptsThisRun = int.MinValue;
            _noProgressAbortTick = int.MinValue;
            _hasSentInitialUpgradeClick = false;
            _portalRequestedThisRun = false;
            _upgradeProgressObservedThisRun = false;
            _autoRunning = false;
            _target = null;
            _autoPlan.Clear();
            _autoPlanSummary = string.Empty;
            _autoConfirmedRankByAbs.Clear();
            _autoAwaitingResolution = false;
            _autoUpgradeClickStartUpgrades = int.MinValue;
            _autoAttemptResolvedTick = int.MinValue;
            _autoRetargetEarliestTick = int.MinValue;
            _autoValidationPreRank = -1;
            _persistentAwaitingResolution = false;
            _persistentUpgradeClickStartUpgrades = int.MinValue;
            _persistentAttemptResolvedTick = int.MinValue;
            _persistentRetargetEarliestTick = int.MinValue;
            _lowestPlanSequence.Clear();
            _lowestPlanSummary = string.Empty;
            _lowestPlanPointer = 0;
            _lowestAwaitingResolution = false;
            _lowestUpgradeClickStartUpgrades = int.MinValue;
            _lowestAttemptResolvedTick = int.MinValue;
            _lowestRetargetEarliestTick = int.MinValue;
            _lowestValidationAcd = 0;
            _lowestValidationPreRank = -1;

            _orderedGems.Clear();
            _currentSnapshot = null;
            _seenPageSignatures.Clear();
            _confirmedSlotMap.Clear();
            _resetScrollClicks = 0;
            _downScrollClicks = 0;
            _arrowScrollAttempts = 0;
            _virtualGrid = null;
            _absoluteGrid = null;
            _estimatedTopVisibleRow = -1;
            _viewportOriginRowFloat = -1f;
            _viewportOriginRowInt = -1;
            _viewportEpoch = 0;
            _lastGoodStackPanelTop = float.NaN;
            _lastMeasuredRowPitch = float.NaN;
            _lastMeasuredVisibleRowCount = 0;
            _currentProbeAbsoluteIndex = -1;
            _scannedAbsoluteIndices.Clear();
            _scrollAtBottom = false;
            _afterScrollWait = 0;
            _lastKnownPhysicalBottomTopRow = -1;
            _lastOrderedGemCountSignature = -1;
            _lastVirtualGridColumnSignature = -1;
            _lastVirtualGridRowSignature = -1;
            _lostLiveIdentityAfterScroll = false;
            _identityLossCheckPending = false;
            _scrollCaptureFailed = false;
            _identityLossRetryCount = 0;
            _identityLossRetryUntilTick = int.MinValue;
            _lastCaptureHadUsableLiveAcds = false;
            _targetAcd = 0;
            _latchedItemButtonAcd = 0;
            _latchedItemButtonAcdTick = int.MinValue;
            _selectedReadyLatchedAcd = 0;
            _selectedReadyLatchedName = string.Empty;
            _selectedReadyLatchedRank = -1;
            _selectedReadyLatchedAbsoluteIndex = -1;
            _selectedReadyTick = int.MinValue;
            _viewportRecoveryAttempts = 0;
            _runningUiLossCount = 0;
            _preserveRunningStateOnReacquire = false;
            _capRetargetInProgress = false;  // Rev 5.6.9: clear on reset
            _capRetargetResolvedTick = int.MinValue;  // Rev 5.6.11: clear click-delay gate
            _capRetargetFirstClickPending = false;  // Rev 5.6.12
            _navTargetLogged = false;

            _probeActive = false;
            _probeReason = ProbeReason.None;
            _probeCells.Clear();
            _probeSnapshot = null;
            _probeIndex = 0;
            _probeWaitingForValidation = false;
            _probePendingCell = null;
            _probeActionTick = int.MinValue;
            _probeNoIdentityRetryCount = 0;

            _cursorIgnoreUntilTick = int.MinValue;
            _lastGemPaneChatCloseTick = int.MinValue;
            ClearChatCloseFadeWait();
            _tailWaitAfterFinalAttempt = false;
        }

        // === Instrumentation & Debug Logging ===
        // The following fields and helpers implement a non-intrusive run metrics recorder and
        // buffered logging system. When debug is enabled via the overlay, these metrics are
        // collected in real time without altering the plugin’s automation behaviour. See the
        // revision plan for details.

        // instrumentation state
        private bool _runActive = false;
        private bool _runOutcomeLogged = false;
        private int _runSequence = 0;
        private int _runStartTick = int.MinValue;
        private int _runInitialAttempts = int.MinValue;
        private int _runLastObservedAttempts = int.MinValue;
        private int _runFinalAttemptConsumedTick = int.MinValue;
        private int _runFinalResultTick = int.MinValue;
        private string _runFinalResultText = string.Empty;
        private int _runPaneHiddenTick = int.MinValue;
        private int _runFinalResultToPaneHideMs = int.MinValue;
        private int _runTpEndToPaneHideMs = int.MinValue;
        private int _runPortalRequestedToPaneHideMs = int.MinValue;
        private int _runFinalAttemptToResultMs = int.MinValue;
        private bool _runPass = false;
        private int _runPortalRequestedTick = int.MinValue;
        private int _runPortalCastStartTick = int.MinValue;
        private int _runPortalCastEndTick = int.MinValue;
        private bool _runPortalCastingLastTick = false;
        private bool _runFinalResultLatched = false;
        private bool _tailWaitAfterFinalAttempt = false;
        private bool _runIgnoreStalePaneOpenOutcome = false;
        private int _runFreshAttemptDropCount = 0;
        private int _lastUpgradeResultSeenTick = int.MinValue;
        private string _lastUpgradeResultSeenText = string.Empty;
        // debug session and pane visibility tracking
        private bool _debugSessionStarted = false;
        private bool _paneVisibleLastTick = false;
        private AutomationStage _lastStageLogged = AutomationStage.Idle;
        private string _lastGemOrderLogSignature = string.Empty;
        // debug log buffer
        private readonly List<string> _debugBuffer = new List<string>();

        // Begin a new run if not already tracking one. Captures the starting tick and attempts count.
        private void BeginRunMetricsIfNeeded(int upgrades, int now)
        {
            if (_runActive)
                return;
            _runActive = true;
            _runOutcomeLogged = false;
            _tailWaitAfterFinalAttempt = false;
            _runStartTick = now;
            _runInitialAttempts = upgrades;
            _runLastObservedAttempts = upgrades;
            _runFinalAttemptConsumedTick = int.MinValue;
            _runFinalResultTick = int.MinValue;
            _runFinalResultText = string.Empty;
            _runPaneHiddenTick = int.MinValue;
            _runFinalResultToPaneHideMs = int.MinValue;
            _runTpEndToPaneHideMs = int.MinValue;
            _runPortalRequestedToPaneHideMs = int.MinValue;
            _runFinalAttemptToResultMs = int.MinValue;
            _runPass = false;
            _runPortalRequestedTick = int.MinValue;
            _runPortalCastStartTick = int.MinValue;
            _runPortalCastEndTick = int.MinValue;
            _runPortalCastingLastTick = false;
            _runFinalResultLatched = false;
            _runIgnoreStalePaneOpenOutcome = true;
            _runFreshAttemptDropCount = 0;
            _lastUpgradeResultSeenTick = int.MinValue;
            _lastUpgradeResultSeenText = string.Empty;
            _runSequence++;
            Log(() => "run note: pane-open result text ignored until first fresh attempt transition", s7o_AutoGemUpgradeState.DebugLevelState);
            Log(() => "run start: attempts=" + upgrades.ToString(CultureInfo.InvariantCulture) + " startTick=" + now.ToString(CultureInfo.InvariantCulture), s7o_AutoGemUpgradeState.DebugLevelState);
        }

        // Track portal cast animation transitions. Logs when casting starts and ends.
        private void TrackPortalCastWindow(int now)
        {
            if (!_runActive)
                return;
            bool casting = false;
            try
            {
                casting = Hud.Game?.Me != null && Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal;
            }
            catch { }
            if (casting && !_runPortalCastingLastTick)
            {
                _runPortalCastStartTick = now;
                Log(() => "portal cast start", s7o_AutoGemUpgradeState.DebugLevelState);
            }
            else if (!casting && _runPortalCastingLastTick)
            {
                _runPortalCastEndTick = now;
                Log(() => "portal cast end", s7o_AutoGemUpgradeState.DebugLevelState);
            }
            _runPortalCastingLastTick = casting;
        }

        private bool TryReadUpgradeOutcomeText(out string outcomeText)
        {
            outcomeText = string.Empty;

            string statusText = ReadText(_gemStatusText);
            string paneText = ReadText(_gemUpgradePane);
            string combined = (statusText ?? string.Empty) + "\n" + (paneText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(combined))
                return false;

            if (combined.IndexOf("Upgrade Succeeded", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                outcomeText = "Upgrade Succeeded";
                return true;
            }

            if (combined.IndexOf("Upgrade Failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                outcomeText = "Upgrade Failed";
                return true;
            }

            return false;
        }

        private bool IsFreshOutcomeEligibleForCurrentRun(int upgrades)
        {
            if (!_runIgnoreStalePaneOpenOutcome)
                return true;
            if (_runFreshAttemptDropCount > 0)
                return true;
            if (_tailWaitAfterFinalAttempt && upgrades == 0)
                return true;
            return false;
        }

        private void TrackRunOutcomeWhilePaneVisible(int upgrades, int now)
        {
            if (!_runActive)
                BeginRunMetricsIfNeeded(upgrades, now);

            _runLastObservedAttempts = upgrades;

            string outcomeText;
            if (TryReadUpgradeOutcomeText(out outcomeText))
            {
                if (!IsFreshOutcomeEligibleForCurrentRun(upgrades))
                {
                    TrackPortalCastWindow(now);
                    return;
                }

                if (_lastUpgradeResultSeenTick == int.MinValue || !string.Equals(_lastUpgradeResultSeenText, outcomeText, StringComparison.Ordinal))
                {
                    _lastUpgradeResultSeenTick = now;
                    _lastUpgradeResultSeenText = outcomeText;
                    Log(() => "upgrade result visible: '" + outcomeText + "' at=" + (_lastUpgradeResultSeenTick - _runStartTick).ToString(CultureInfo.InvariantCulture), s7o_AutoGemUpgradeState.DebugLevelState);
                }

                if (upgrades == 0 && !_runFinalResultLatched)
                {
                    _runFinalResultTick = now;
                    _runFinalResultText = outcomeText;
                    _runFinalResultLatched = true;
                    Log(() => "final result latched: '" + outcomeText + "' at=" + (_runFinalResultTick - _runStartTick).ToString(CultureInfo.InvariantCulture), s7o_AutoGemUpgradeState.DebugLevelState);
                }
            }

            TrackPortalCastWindow(now);
        }

        // Track consumption of upgrade attempts by comparing the current and last observed counts.
        private void TrackUpgradeAttemptConsumption(int upgrades, int now)
        {
            if (!_runActive)
                return;
            if (_runLastObservedAttempts == int.MinValue)
            {
                _runLastObservedAttempts = upgrades;
            }
            else if (upgrades < _runLastObservedAttempts)
            {
                int previous = _runLastObservedAttempts;
                _runFreshAttemptDropCount++;
                _runIgnoreStalePaneOpenOutcome = false;
                _runLastObservedAttempts = upgrades;
                Log(() => "upgrade attempt consumed: " + previous.ToString(CultureInfo.InvariantCulture) + " -> " + upgrades.ToString(CultureInfo.InvariantCulture), s7o_AutoGemUpgradeState.DebugLevelState);

                if (upgrades == 0)
                {
                    _runFinalAttemptConsumedTick = now;
                    _tailWaitAfterFinalAttempt = true;
                    Log(() => "final attempt consumed; entering tail-wait monitor", s7o_AutoGemUpgradeState.DebugLevelState);
                }
            }
            else if (upgrades > _runLastObservedAttempts)
            {
                _runLastObservedAttempts = upgrades;
            }
        }

        // Finalize a run, computing timing metrics and writing a summary line. Called once per run.
        private void FinalizeRunOutcome(int now)
        {
            if (!_runActive || _runOutcomeLogged)
                return;
            // ensure pane hidden tick
            if (_runPaneHiddenTick == int.MinValue)
                _runPaneHiddenTick = now;
            // compute metrics
            if (_runFinalResultTick != int.MinValue && _runPaneHiddenTick != int.MinValue)
                _runFinalResultToPaneHideMs = _runPaneHiddenTick - _runFinalResultTick;
            if (_runPortalCastEndTick != int.MinValue && _runPaneHiddenTick != int.MinValue)
                _runTpEndToPaneHideMs = _runPaneHiddenTick - _runPortalCastEndTick;
            if (_runPortalRequestedTick != int.MinValue && _runPaneHiddenTick != int.MinValue)
                _runPortalRequestedToPaneHideMs = _runPaneHiddenTick - _runPortalRequestedTick;
            if (_runFinalAttemptConsumedTick != int.MinValue && _runFinalResultTick != int.MinValue)
                _runFinalAttemptToResultMs = _runFinalResultTick - _runFinalAttemptConsumedTick;
            // determine pass/fail
            _runPass = (_runFinalResultTick != int.MinValue && _runPaneHiddenTick != int.MinValue && _runFinalResultTick < _runPaneHiddenTick);

            string Ms(int v) => (v == int.MinValue ? "na" : v.ToString(CultureInfo.InvariantCulture));
            Log(() =>
            {
                return "RUN " + (_runPass ? "PASS" : "FAIL")
                    + " run#" + _runSequence.ToString(CultureInfo.InvariantCulture)
                    + " initialAttempts=" + _runInitialAttempts.ToString(CultureInfo.InvariantCulture)
                    + " finalObservedAttempts=" + _runLastObservedAttempts.ToString(CultureInfo.InvariantCulture)
                    + " finalAttemptConsumedAt=" + Ms(_runFinalAttemptConsumedTick == int.MinValue ? int.MinValue : (_runFinalAttemptConsumedTick - _runStartTick))
                    + " finalResultAt=" + Ms(_runFinalResultTick == int.MinValue ? int.MinValue : (_runFinalResultTick - _runStartTick))
                    + " finalResult='" + (_runFinalResultText ?? string.Empty) + "'"
                    + " portalRequestedAt=" + Ms(_runPortalRequestedTick == int.MinValue ? int.MinValue : (_runPortalRequestedTick - _runStartTick))
                    + " portalCastStartAt=" + Ms(_runPortalCastStartTick == int.MinValue ? int.MinValue : (_runPortalCastStartTick - _runStartTick))
                    + " portalCastEndAt=" + Ms(_runPortalCastEndTick == int.MinValue ? int.MinValue : (_runPortalCastEndTick - _runStartTick))
                    + " paneHiddenAt=" + Ms(_runPaneHiddenTick == int.MinValue ? int.MinValue : (_runPaneHiddenTick - _runStartTick))
                    + " finalResultToPaneHideMs=" + Ms(_runFinalResultToPaneHideMs)
                    + " tpEndToPaneHideMs=" + Ms(_runTpEndToPaneHideMs)
                    + " portalRequestedToPaneHideMs=" + Ms(_runPortalRequestedToPaneHideMs)
                    + " finalAttemptToResultMs=" + Ms(_runFinalAttemptToResultMs);
            }, s7o_AutoGemUpgradeState.DebugLevelState);
            _runOutcomeLogged = true;
            _runActive = false;
            // flush any remaining buffered lines to disk
            try { FlushDebugLines(); } catch { }
        }

        // Instrumentation hook invoked once per AfterCollect tick. Performs debug state sync,
        // pane open/hide detection, stage transition logging, run metric updates, and finalization.
        private void InstrumentationHook()
        {
            // sync debug state and manage session boundaries
            if (!s7o_AutoGemUpgradeState.DebugEnabled)
            {
                _debugSessionStarted = false;
                _debugBuffer.Clear();
                s7o_AutoGemUpgradeState.DebugFileEnabled = false;
                s7o_AutoGemUpgradeState.DebugLevel = s7o_AutoGemUpgradeState.DebugLevelOff;
                s7o_AutoGemUpgradeState.DebugLogPath = string.Empty;
                return;
            }
            // debug is enabled: ensure file and level are set
            if (!s7o_AutoGemUpgradeState.DebugFileEnabled)
                s7o_AutoGemUpgradeState.DebugFileEnabled = true;
            if (s7o_AutoGemUpgradeState.DebugLevel != s7o_AutoGemUpgradeState.DebugLevelState)
                s7o_AutoGemUpgradeState.DebugLevel = s7o_AutoGemUpgradeState.DebugLevelState;
            if (!_debugSessionStarted)
            {
                try { StartDebugSession(); } catch { }
                _debugSessionStarted = true;
            }

            int now = NowTick();
            bool paneVisible = false;
            try { paneVisible = _gemUpgradePane != null && _gemUpgradePane.Visible; } catch { }

            // pane open/hide detection logs
            if (paneVisible && !_paneVisibleLastTick)
            {
                Log(() => "pane-open", s7o_AutoGemUpgradeState.DebugLevelState);
                _paneVisibleLastTick = true;
            }
            else if (!paneVisible && _paneVisibleLastTick)
            {
                Log(() => "pane-hide", s7o_AutoGemUpgradeState.DebugLevelState);
                _paneVisibleLastTick = false;
            }

            // stage transition logging
            if (_stage != _lastStageLogged)
            {
                Log(() => "stage " + _lastStageLogged.ToString() + " -> " + _stage.ToString(), s7o_AutoGemUpgradeState.DebugLevelState);
                _lastStageLogged = _stage;
            }

            // get current upgrade attempts
            int upgrades = int.MinValue;
            try { upgrades = GetUpgradeAttempts(); } catch { }

            // begin run when pane is visible and there are upgrade attempts
            if (!_runActive && paneVisible && upgrades > 0)
            {
                BeginRunMetricsIfNeeded(upgrades, now);
            }

            // track portal requested tick if set by automation logic
            if (_runActive)
            {
                if (_runPortalRequestedTick == int.MinValue && _portalRequestedTick != int.MinValue)
                {
                    _runPortalRequestedTick = _portalRequestedTick;
                    Log(() => "portal requested", s7o_AutoGemUpgradeState.DebugLevelState);
                }

                if (upgrades != int.MinValue)
                    TrackUpgradeAttemptConsumption(upgrades, now);

                if (paneVisible)
                    TrackRunOutcomeWhilePaneVisible(upgrades, now);

                // finalize only when pane hides; attempts reaching zero enter observer-only tail-wait
                if (!paneVisible)
                {
                    if (_runPaneHiddenTick == int.MinValue)
                        _runPaneHiddenTick = now;
                    FinalizeRunOutcome(now);
                }
            }
        }

        // Determine whether to log a message at the given level. Returns false when
        // debugging is disabled or the message level exceeds the configured level.
        private bool ShouldLog(int level)
        {
            if (!s7o_AutoGemUpgradeState.DebugEnabled)
                return false;
            return level <= s7o_AutoGemUpgradeState.DebugLevel;
        }

        // Append a single line to the in-memory debug buffer. Each message is prefixed with a timestamp.
        private void AppendDebugLine(string message)
        {
            try
            {
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                _debugBuffer.Add(time + " " + (message ?? string.Empty));
                if (_debugBuffer.Count >= s7o_AutoGemUpgradeState.MaxBufferedLines)
                    FlushDebugLines();
            }
            catch { }
        }

        // Flush buffered log lines to disk. Handles directory creation and basic file size limiting.
        private void FlushDebugLines()
        {
            if (!s7o_AutoGemUpgradeState.DebugFileEnabled)
            {
                _debugBuffer.Clear();
                return;
            }
            if (string.IsNullOrWhiteSpace(s7o_AutoGemUpgradeState.DebugLogPath))
            {
                // path is unresolved; attempt to resolve lazily
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var dir = Path.GetDirectoryName(asm);
                    var logsDir = Path.Combine(dir ?? string.Empty, "logs");
                    if (!Directory.Exists(logsDir))
                        Directory.CreateDirectory(logsDir);
                    s7o_AutoGemUpgradeState.DebugLogPath = Path.Combine(logsDir, s7o_AutoGemUpgradeState.DebugLogFileName);
                }
                catch
                {
                    s7o_AutoGemUpgradeState.DebugLogPath = s7o_AutoGemUpgradeState.DebugLogFileName;
                }
            }
            try
            {
                string dirName = Path.GetDirectoryName(s7o_AutoGemUpgradeState.DebugLogPath);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                    Directory.CreateDirectory(dirName);
                if (File.Exists(s7o_AutoGemUpgradeState.DebugLogPath))
                {
                    long length = new FileInfo(s7o_AutoGemUpgradeState.DebugLogPath).Length;
                    long maxBytes = (long)s7o_AutoGemUpgradeState.MaxFileSizeMb * 1024L * 1024L;
                    if (length > maxBytes)
                    {
                        File.Delete(s7o_AutoGemUpgradeState.DebugLogPath);
                    }
                }
                File.AppendAllLines(s7o_AutoGemUpgradeState.DebugLogPath, _debugBuffer);
            }
            catch { }
            _debugBuffer.Clear();
        }

        // Start a new debug session by resolving the log path and emitting a session header.
        private void StartDebugSession()
        {
            // ensure log path resolved
            if (string.IsNullOrWhiteSpace(s7o_AutoGemUpgradeState.DebugLogPath))
            {
                try
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var dir = Path.GetDirectoryName(asm);
                    var logsDir = Path.Combine(dir ?? string.Empty, "logs");
                    if (!Directory.Exists(logsDir))
                        Directory.CreateDirectory(logsDir);
                    s7o_AutoGemUpgradeState.DebugLogPath = Path.Combine(logsDir, s7o_AutoGemUpgradeState.DebugLogFileName);
                }
                catch
                {
                    s7o_AutoGemUpgradeState.DebugLogPath = s7o_AutoGemUpgradeState.DebugLogFileName;
                }
            }
            Log(() =>
            {
                return "=== session start | plugin=" + s7o_AutoGemUpgradeState.DebugPluginName
                    + " | level=" + s7o_AutoGemUpgradeState.DebugLevel.ToString(CultureInfo.InvariantCulture)
                    + " | file=" + (s7o_AutoGemUpgradeState.DebugFileEnabled ? "1" : "0")
                    + " | path='" + s7o_AutoGemUpgradeState.DebugLogPath + "' ===";
            }, s7o_AutoGemUpgradeState.DebugLevelState);
        }

        // Lazy logging overload. Evaluates the message factory only when logging is enabled and level allows.
        private void Log(Func<string> messageFactory, int level = 1)
        {
            if (!ShouldLog(level))
                return;
            string msg;
            try
            {
                msg = messageFactory != null ? messageFactory() : string.Empty;
            }
            catch
            {
                msg = "[log message factory threw]";
            }
            AppendDebugLine(msg);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (_gemUpgradePane?.Visible != true)
                return;

            PaintPaneWarning();
        }

        private void PaintPaneWarning()
        {
            if (string.IsNullOrWhiteSpace(_paneWarningMessage) || _warningFont == null)
                return;

            RectangleF paneRect;
            try
            {
                paneRect = _gemUpgradePane.Rectangle;
            }
            catch
            {
                return;
            }

            float warningLeft = 60f;
            float warningRight = 470f;
            float warningTop = 200f;
            float warningBottom = 470f;
            float maxWidth = Math.Max(110f, warningRight - warningLeft);

            var wrappedLines = WrapPaneWarningLines(_paneWarningMessage, maxWidth);
            if (wrappedLines == null || wrappedLines.Count == 0)
                return;

            float totalHeight = 0f;
            var layouts = new List<SharpDX.DirectWrite.TextLayout>();
            foreach (var line in wrappedLines)
            {
                var layout = _warningFont.GetTextLayout(line);
                layouts.Add(layout);
                totalHeight += layout.Metrics.Height;
            }

            float y = warningTop;
            if (y + totalHeight > warningBottom)
                y = Math.Max(warningTop, warningBottom - totalHeight);

            for (int i = 0; i < layouts.Count; i++)
            {
                var layout = layouts[i];
                _warningFont.DrawText(layout, warningLeft, y);
                y += layout.Metrics.Height * 1.02f;
            }
        }

        private List<string> WrapPaneWarningLines(string text, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || _warningFont == null)
                return lines;

            var paragraphs = text.Replace("\r", string.Empty).Split('\n');
            foreach (var rawParagraph in paragraphs)
            {
                var paragraph = (rawParagraph ?? string.Empty).Trim();
                if (paragraph.Length == 0)
                {
                    if (lines.Count == 0 || lines[lines.Count - 1].Length != 0)
                        lines.Add(string.Empty);
                    continue;
                }

                var words = Regex.Split(paragraph, @"\s+").Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();
                if (words.Length == 0)
                    continue;

                string current = words[0];
                for (int i = 1; i < words.Length; i++)
                {
                    string candidate = current + " " + words[i];
                    var candidateLayout = _warningFont.GetTextLayout(candidate);
                    if (candidateLayout.Metrics.Width <= maxWidth)
                    {
                        current = candidate;
                        continue;
                    }

                    lines.Add(current);
                    current = words[i];

                    while (true)
                    {
                        var currentLayout = _warningFont.GetTextLayout(current);
                        if (currentLayout.Metrics.Width <= maxWidth || current.Length <= 1)
                            break;

                        int split = current.Length - 1;
                        while (split > 1)
                        {
                            var probe = current.Substring(0, split) + "-";
                            if (_warningFont.GetTextLayout(probe).Metrics.Width <= maxWidth)
                                break;
                            split--;
                        }

                        if (split <= 1)
                            break;

                        lines.Add(current.Substring(0, split) + "-");
                        current = current.Substring(split);
                    }
                }

                if (!string.IsNullOrWhiteSpace(current))
                    lines.Add(current);
            }

            return lines;
        }

        private static bool AreSameRect(RectangleF a, RectangleF b)
        {
            return Math.Abs(a.X - b.X) <= 1f && Math.Abs(a.Y - b.Y) <= 1f && Math.Abs(a.Width - b.Width) <= 1f && Math.Abs(a.Height - b.Height) <= 1f;
        }

        private int GetRequiredTopResetClicks()
        {
            int totalRows = _virtualGrid != null && _virtualGrid.TotalRowCount > 0
                ? _virtualGrid.TotalRowCount
                : Math.Max(1, (_orderedGems.Count + 4) / 5);
            return Math.Min(MaxResetScrollClicks, Math.Max(6, totalRows + 2));
        }

        private int GetMaxTopVisibleRow()
        {
            if (_virtualGrid == null)
                return 0;
            return Math.Max(0, _virtualGrid.TotalRowCount - Math.Max(1, _virtualGrid.VisibleRowCount));
        }

        private int GetMaxTopScanRow()
        {
            if (_virtualGrid == null)
                return 0;
            return Math.Max(0, _virtualGrid.TotalRowCount - Math.Max(1, _virtualGrid.LiveScanRowCount));
        }

        private bool TryCaptureViewport(out ViewportCapture cap)
        {
            cap = new ViewportCapture();

            if (_gemUpgradePane?.Visible != true)
                return false;

            try
            {
                cap.PaneRect = _gemUpgradePane.Rectangle;
            }
            catch
            {
                return false;
            }

            cap.HasPane = cap.PaneRect.Width > 10f && cap.PaneRect.Height > 10f;
            if (!cap.HasPane)
                return false;

            cap.ListBounds = GetAuthoritativeGemListBounds(cap.PaneRect);
            cap.HasListBounds = cap.ListBounds.Width > 10f && cap.ListBounds.Height > 10f;
            if (!cap.HasListBounds)
                return false;

            cap.LiveCells = GetMappedVisibleCells(cap.ListBounds)
                .Where(c => c != null && !c.IsProjected)
                .ToList();
            cap.HasLiveCells = cap.LiveCells.Count > 0;

            cap.ScrollLaneRect = GetAuthoritativeScrollLane(cap.PaneRect, cap.ListBounds);
            cap.HasScrollLane = cap.ScrollLaneRect.Width > 4f && cap.ScrollLaneRect.Height > 20f;

            return true;
        }

        private bool RefreshSnapshotFromViewportCapture(ViewportCapture cap)
        {
            if (cap == null || !cap.HasPane || !cap.HasListBounds)
                return false;

            var liveCells = cap.LiveCells != null ? new List<VisibleCell>(cap.LiveCells.Where(c => c != null && !c.IsProjected)) : new List<VisibleCell>();
            UpdateViewportOriginFromStackMotion("refresh");
            RebuildAbsoluteGrid(cap.ListBounds, liveCells);
            bool aligned = ApplyLiveAlignmentCorrection(cap.ListBounds, liveCells, "refresh");
            if (aligned)
                RebuildAbsoluteGrid(cap.ListBounds, liveCells);

            var extendedNativeCells = GetExtendedNativeCells(cap.ListBounds);
            UpdateRow6NativeExtentEvidence(extendedNativeCells);
            UpdateTrackedNativeEvidence(extendedNativeCells, cap.ListBounds);

            UpdateTrackedLiveCells(liveCells);
            var trackedProjected = GetTrackedProjectedCells();
            var inferred = BuildProbeCellsForCurrentViewport(cap.ListBounds, liveCells);
            if (trackedProjected.Count > 0)
            {
                inferred.AddRange(trackedProjected);
                inferred = DeduplicateVisibleCells(inferred);
            }

            var scrollPoints = GetScrollPoints(cap.PaneRect, cap.ListBounds, liveCells);
            _currentSnapshot = new ObservedPageSnapshot
            {
                PaneRect = cap.PaneRect,
                ListBounds = cap.ListBounds,
                ScrollUpPoint = scrollPoints.Item1,
                ScrollDownPoint = scrollPoints.Item2,
                VisibleCells = liveCells,
                LiveVisibleCells = liveCells,
                InferredViewportCells = inferred,
            };
            RebuildVirtualGrid(cap.ListBounds, liveCells);
            TryEnrichCellsFromDirectText(liveCells);
            UpdateViewportMetricsFromSnapshot();
            LogViewportMetrics("refresh");
            LogViewportGridMismatch(_currentSnapshot);
            LogScrollLane("refresh", cap.PaneRect, cap.ListBounds);
            UpdateRuntimeBottomLock();
            return liveCells.Count > 0;
        }

        private static float GetFirstVisibleCellTop(List<VisibleCell> visibleCells)
        {
            if (visibleCells == null || visibleCells.Count == 0)
                return float.NaN;

            try
            {
                return visibleCells.OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).First().Rect.Top;
            }
            catch
            {
                return float.NaN;
            }
        }


private static uint GetTopCellAcd(List<VisibleCell> cells)
        {
            if (cells == null || cells.Count == 0) return 0u;
            try
            {
                var top = cells.Where(c => c != null && !c.IsProjected && c.Ref != null)
                               .OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex)
                               .FirstOrDefault();
                return top?.Ref?.CachedLegendaryGemAcdId ?? 0u;
            }
            catch { return 0u; }
        }

private List<VisibleCell> GetMappedVisibleCells(RectangleF listBounds)
        {
            var result = new List<VisibleCell>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in _candidateCells)
            {
                if (candidate?.Element == null)
                    continue;

                RectangleF rect;
                try
                {
                    if (!candidate.Element.Visible)
                        continue;
                    rect = candidate.Element.Rectangle;
                    try { candidate.CachedLegendaryGemAcdId = (uint)candidate.Element.LegendaryGemAcdId; } catch { candidate.CachedLegendaryGemAcdId = 0; }
                }
                catch
                {
                    continue;
                }

                if (rect.Width < MinCellWidthPx || rect.Width > MaxCellWidthPx || rect.Height < MinCellHeightPx || rect.Height > MaxCellHeightPx)
                    continue;

                float overlapLeft = Math.Max(rect.Left, listBounds.Left);
                float overlapTop = Math.Max(rect.Top, listBounds.Top);
                float overlapRight = Math.Min(rect.Right, listBounds.Right);
                float overlapBottom = Math.Min(rect.Bottom, listBounds.Bottom);
                float overlapW = overlapRight - overlapLeft;
                float overlapH = overlapBottom - overlapTop;
                if (overlapW <= 0f || overlapH <= 0f)
                    continue;

                float minVisibleW = Math.Max(6f, rect.Width * 0.25f);
                float minVisibleH = Math.Max(6f, rect.Height * 0.18f);
                if (overlapW < minVisibleW || overlapH < minVisibleH)
                    continue;

                string rectKey = ((int)Math.Round(rect.X)).ToString() + "," + ((int)Math.Round(rect.Y)).ToString() + "," + ((int)Math.Round(rect.Width)).ToString() + "," + ((int)Math.Round(rect.Height)).ToString();
                if (!seen.Add(rectKey))
                    continue;

                result.Add(new VisibleCell
                {
                    Ref = candidate,
                    Rect = rect,
                    DirectText = ReadText(candidate.Element),
                    FamilyTag = BuildCandidateTag(candidate),
                });
            }

            result.Sort(delegate (VisibleCell a, VisibleCell b)
            {
                float dy = Math.Abs(a.Rect.Y - b.Rect.Y);
                if (dy > RowClusterTolerancePx)
                    return a.Rect.Y.CompareTo(b.Rect.Y);
                return a.Rect.X.CompareTo(b.Rect.X);
            });

            int rowIndex = -1;
            float currentRowY = float.MinValue;
            int column = 0;
            foreach (var cell in result)
            {
                if (rowIndex < 0 || Math.Abs(cell.Rect.Y - currentRowY) > RowClusterTolerancePx)
                {
                    rowIndex++;
                    currentRowY = cell.Rect.Y;
                    column = 0;
                }
                cell.RowIndex = rowIndex;
                cell.ColumnIndex = column;
                column++;
            }

            return result;
        }

private RectangleF GetGemListBounds(RectangleF paneRect)
        {
            float left = paneRect.Left + paneRect.Width * GemListLeftRatio;
            float top = paneRect.Top + paneRect.Height * GemListTopRatio;
            float right = paneRect.Left + paneRect.Width * GemListRightRatio;
            float bottom = paneRect.Top + paneRect.Height * GemListBottomRatio;
            return new RectangleF(left, top, Math.Max(20f, right - left), Math.Max(20f, bottom - top));
        }

private static float EstimateColumnPitch(List<VisibleCell> visibleCells, float fallbackWidth)
        {
            try
            {
                var diffs = new List<float>();
                foreach (var row in visibleCells.GroupBy(c => c.RowIndex))
                {
                    var ordered = row.OrderBy(c => c.ColumnIndex).ToList();
                    for (int i = 1; i < ordered.Count; i++)
                    {
                        float diff = ordered[i].Rect.Left - ordered[i - 1].Rect.Left;
                        if (diff > fallbackWidth * 0.60f)
                            diffs.Add(diff);
                    }
                }
                if (diffs.Count > 0)
                    return diffs.Average();
            }
            catch { }

            return Math.Max(fallbackWidth + 2f, fallbackWidth * 1.04f);
        }

private float EstimateRowPitch(List<VisibleCell> visibleCells, float fallbackHeight)
        {
            try
            {
                var rowTops = visibleCells
                    .GroupBy(c => c.RowIndex)
                    .OrderBy(g => g.Key)
                    .Select(g => g.Min(c => c.Rect.Top))
                    .ToList();

                var diffs = new List<float>();
                for (int i = 1; i < rowTops.Count; i++)
                {
                    float diff = rowTops[i] - rowTops[i - 1];
                    if (diff > fallbackHeight * 0.60f)
                        diffs.Add(diff);
                }
                if (diffs.Count > 0)
                    return diffs.Average();
            }
            catch { }

            return Math.Max(fallbackHeight + 4f, fallbackHeight * 1.08f);
        }

private static int CalculateVisibleWindowRowCount(RectangleF listBounds, List<VisibleCell> visibleCells, float rowPitch)
        {
            int observedRows = Math.Max(1, visibleCells.Select(c => c.RowIndex).DefaultIfEmpty(-1).Max() + 1);
            if (rowPitch <= 1f || visibleCells == null || visibleCells.Count == 0)
                return observedRows;

            float spanFromTop    = Math.Max(1f, listBounds.Bottom - listBounds.Top);
            float spanFromAnchor = Math.Max(1f, listBounds.Bottom - visibleCells.OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).First().Rect.Top);
            float visibleSpan = Math.Max(spanFromTop, spanFromAnchor);
            int projectedRows = (int)Math.Round(visibleSpan / rowPitch);
            return Math.Max(observedRows, Math.Max(1, projectedRows));
        }

private static string BuildCandidateTag(CellRef candidate)
        {
            if (candidate == null) return string.Empty;
            string family = candidate.Family ?? string.Empty;
            if (family.Length > 4) family = family.Substring(0, 4);
            return family + ":" + candidate.Major + "." + candidate.Minor;
        }

private RectangleF GetAuthoritativeGemListBounds(RectangleF paneRect)
        {
            try
            {
                if (_itemsList != null && _itemsList.Visible)
                {
                    var r = _itemsList.Rectangle;
                    if (r.Width > 20f && r.Height > 20f)
                        return r;
                }
            }
            catch { }

            try
            {
                if (_itemsContent != null && _itemsContent.Visible)
                {
                    var r = _itemsContent.Rectangle;
                    if (r.Width > 20f && r.Height > 20f)
                        return r;
                }
            }
            catch { }

            return GetGemListBounds(paneRect);
        }

private Tuple<PointF, PointF> GetScrollPoints(RectangleF paneRect, RectangleF listBounds, List<VisibleCell> visibleCells)
        {
            RectangleF lane = GetAuthoritativeScrollLane(paneRect, listBounds);
            float x = lane.Left + lane.Width * 0.50f;
            float upY = lane.Top + lane.Height * 0.10f;
            float downY = lane.Bottom - lane.Height * 0.05f;
            return Tuple.Create(new PointF(x, upY), new PointF(x, downY));
        }

private PointF GetFastScrollPoint(RectangleF paneRect, RectangleF listBounds, bool downward)
        {
            RectangleF lane = GetAuthoritativeScrollLane(paneRect, listBounds);
            float x = lane.Left + lane.Width * 0.50f;
            float y = downward ? lane.Top + lane.Height * 0.86f : lane.Top + lane.Height * 0.20f;
            return new PointF(x, y);
        }

private PointF GetProportionalScrollPoint(RectangleF paneRect, RectangleF listBounds, int desiredTopRow)
        {
            RectangleF lane = GetAuthoritativeScrollLane(paneRect, listBounds);
            float x = lane.Left + lane.Width * 0.50f;
            int maxTop = Math.Max(0, GetMaxTopVisibleRow());
            float frac = maxTop <= 0 ? 0.50f : (desiredTopRow + 0.50f) / (maxTop + 1.0f);
            frac = Math.Max(0.14f, Math.Min(0.86f, frac));
            return new PointF(x, lane.Top + lane.Height * frac);
        }

private bool ValidateLoadedSelectionAgainstTarget(GemTarget target)
        {
            string observedName;
            int observedRank;
            string sourceText;
            return ValidateLoadedSelectionAgainstTarget(target, out observedName, out observedRank, out sourceText);
        }

private bool ValidateLoadedSelectionAgainstTarget(GemTarget target, out string observedName, out int observedRank, out string sourceText)
        {
            observedName = null;
            observedRank = -1;
            sourceText = string.Empty;

            // Rev 5.6.3 (Finding C): ACD fast-path.
            // When the item button already reports our target's ACD and the button is loaded
            // and not mid-upgrade-animation, the gem identity is proven — no need to run three
            // ReadText(UTF8, removeColors=true) calls per 10ms validation poll.  This matches the
            // same ACD signal the fallback block below uses to confirm; we just short-circuit to it.
            // Safe because: (1) the ACD check alone is the strongest identity signal; (2) gates
            // mirror the minimum subset of the existing fallback (loaded + button not in state 27);
            // (3) on ACD miss or read failure, we fall through unchanged.
            if (target != null && _targetAcd != 0 && _targetAcd != 0xFFFFFFFF && _itemButton != null)
            {
                try
                {
                    int fastItemAnim = SafeAnimState(_itemButton);
                    int fastBtnAnim = SafeAnimState(_upgradeButton);
                    if (fastItemAnim != -1 && fastBtnAnim != 27)
                    {
                        uint fastButtonAcd = (uint)_itemButton.LegendaryGemAcdId;
                        if (fastButtonAcd == _targetAcd)
                        {
                            observedName = target.Name;
                            observedRank = target.Rank;
                            sourceText = "acd-fast";
                            if (target.AbsoluteIndex >= 0)
                                _confirmedSlotMap[target.AbsoluteIndex] = Tuple.Create(target.Name, target.Rank);
                            // Rev 5.6.3 polish: log so debug runs can confirm the fast-path is firing.
                            // State-level so it's on by default at DebugLevelState, off at DebugLevelOff.
                            Log(() => "validate acd-fast: buttonAcd=" + fastButtonAcd + " == target acd=" + _targetAcd + " → confirmed (skipped text reads)", s7o_AutoGemUpgradeState.DebugLevelState);
                            return true;
                        }
                    }
                }
                catch { /* fall through to full validation */ }
            }

            var selection = ReadCurrentSelectionEvidence(out sourceText);
            observedName = selection.Item1;
            observedRank = selection.Item2;
            if (IsTargetMatch(observedName, observedRank, target))
            {
                if (target != null && target.AbsoluteIndex >= 0
                    && !string.IsNullOrEmpty(observedName) && observedRank >= 0)
                {
                    _confirmedSlotMap[target.AbsoluteIndex] = Tuple.Create(observedName, observedRank);
                    // observedName/observedRank are out params — capture locally before lambda
                    string _logName = observedName; int _logRank = observedRank;
                    Log(() => "confirmed slot a" + target.AbsoluteIndex + " = " + _logName + "#" + _logRank);
                }
                return true;
            }

            if (target == null || string.IsNullOrWhiteSpace(sourceText))
                return false;

            bool loaded = SafeAnimState(_itemButton) != -1;
            string normalizedText = NormalizeGemLabel(sourceText);
            bool rawNameMatch = !string.IsNullOrEmpty(normalizedText) && normalizedText.Contains(NormalizeGemLabel(target.Name));
            int rawRank = ExtractGemRank(sourceText);
            if (loaded && rawNameMatch && rawRank == target.Rank)
            {
                observedName = target.Name;
                observedRank = target.Rank;
                if (target.AbsoluteIndex >= 0)
                {
                    _confirmedSlotMap[target.AbsoluteIndex] = Tuple.Create(target.Name, target.Rank);
                    Log(() => "confirmed slot a" + target.AbsoluteIndex + " = " + target.Name + "#" + target.Rank + " (rawmatch)");
                }
                return true;
            }

            int buttonAnim = SafeAnimState(_upgradeButton);
            bool targetSlotVisible = _virtualGrid != null
                && _virtualGrid.Slots.Any(s => s.AbsoluteIndex == target.AbsoluteIndex && s.IsPredictedVisible);
            bool stalePostUpgrade = string.IsNullOrEmpty(observedName)
                && sourceText.IndexOf("Upgrade Succeeded", StringComparison.OrdinalIgnoreCase) >= 0;
            bool itemAnimChanged = _preClickItemButtonAnim != -2
                && SafeAnimState(_itemButton) != _preClickItemButtonAnim;
            if (stalePostUpgrade && !itemAnimChanged && buttonAnim == 27)
            {
                Log(() => "validate fallback blocked: stale text, no item anim change (pre="
                    + _preClickItemButtonAnim + " curr=" + SafeAnimState(_itemButton) + ")");
                return false;
            }
            if (!FullListVerificationMode
                && loaded
                && buttonAnim != 27
                && _currentSnapshot != null
                && _currentSnapshot.TargetCell != null
                && _currentSnapshot.TargetCell.VisibleCell != null
                && _currentSnapshot.TargetCell.ViewportEpoch == _viewportEpoch
                && _currentProbeAbsoluteIndex == target.AbsoluteIndex
                && targetSlotVisible
                && _upgradeButton != null
                && _upgradeButton.Visible)
            {
                if (_targetAcd != 0 && _targetAcd != 0xFFFFFFFF && _itemButton != null)
                {
                    try
                    {
                        uint buttonAcd = (uint)_itemButton.LegendaryGemAcdId;
                        if (buttonAcd == _targetAcd)
                        {
                            Log(() => "validate ACD match: _itemButton.LegendaryGemAcdId=" + buttonAcd + " == target acd=" + _targetAcd + " → confirmed");
                            sourceText = string.IsNullOrWhiteSpace(sourceText) ? "acd-identity" : sourceText + "+acd";
                            if (string.IsNullOrWhiteSpace(observedName)) observedName = "<acd:" + target.Name + ">";
                            if (observedRank < 0) observedRank = target.Rank;
                            return true;
                        }
                        else if (buttonAcd == 0)
                        {
                            bool freshLoad = _preClickItemButtonAnim != -2
                                             && SafeAnimState(_itemButton) != _preClickItemButtonAnim;
                            if (!freshLoad)
                            {
                                Log(() => "validate fallback blocked: ACD dead and no fresh anim change (preAnim="
                                    + _preClickItemButtonAnim + " currAnim=" + SafeAnimState(_itemButton)
                                    + ", targetAcd=" + _targetAcd
                                    + (_lostLiveIdentityAfterScroll ? " [identity-loss]" : "") + ")");
                                return false;
                            }
                            Log(() => "validate blocked: ACD dead" + (_lostLiveIdentityAfterScroll ? " [identity-loss after scroll]" : "")
                                + ", fresh gem load observed but permissive confirmation is disabled (preAnim=" + _preClickItemButtonAnim
                                + "→" + SafeAnimState(_itemButton) + ", targetAcd=" + _targetAcd + ")");
                            return false;
                        }
                        else
                        {
                            Log(() => "validate fallback blocked: ACD mismatch (buttonAcd=" + buttonAcd + " != targetAcd=" + _targetAcd + ") — wrong gem selected");
                            return false;
                        }
                    }
                    catch { }
                }

                Log(() => "validate blocked: projected slot a" + target.AbsoluteIndex + " loaded without hard proof, epoch=" + _viewportEpoch + " — permissive slot-loaded confirmation disabled");
                return false;
            }

            return false;
        }

private Tuple<string, int> ReadCurrentSelectionEvidence(out string sourceText)
        {
            string s1 = ReadText(_gemStatusText);
            var p1 = ParseGemIdentityFromText(s1);
            if (!string.IsNullOrEmpty(p1.Item1))
            {
                sourceText = s1;
                return p1;
            }

            string s2 = ReadText(_itemButton);
            var p2 = ParseGemIdentityFromText(s2);
            if (!string.IsNullOrEmpty(p2.Item1))
            {
                sourceText = s2;
                return p2;
            }

            string s3 = ReadText(_gemUpgradePane);
            var p3 = ParseGemIdentityFromText(s3);
            if (!string.IsNullOrEmpty(p3.Item1))
            {
                sourceText = s3;
                return p3;
            }

            sourceText = s1 + " || " + s2 + " || " + s3;
            return Tuple.Create<string, int>(null, -1);
        }

private Tuple<string, int> ParseGemIdentityFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _orderedGems.Count == 0)
                return Tuple.Create<string, int>(null, -1);

            string normalized = NormalizeGemLabel(text);
            if (string.IsNullOrEmpty(normalized))
                return Tuple.Create<string, int>(null, -1);

            string matchedName = null;
            foreach (var name in _orderedGems.Select(g => GetGemName(g.Item)).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderByDescending(n => n.Length))
            {
                if (normalized.Contains(NormalizeGemLabel(name)))
                {
                    matchedName = name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(matchedName))
                return Tuple.Create<string, int>(null, -1);

            int rank = ExtractGemRank(text);
            return Tuple.Create(matchedName, rank);
        }

private static int ExtractGemRank(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return -1;

            try
            {
                var rankMatch = Regex.Match(text, @"\bRank\s*[:：]?\s*(\d{1,3})\b", RegexOptions.IgnoreCase);
                if (rankMatch.Success)
                {
                    int rankValue;
                    if (int.TryParse(rankMatch.Groups[1].Value, out rankValue))
                        return rankValue;
                }

                var matches = Regex.Matches(text, @"(\d{1,3})");
                foreach (Match match in matches)
                {
                    int end = match.Index + match.Length;
                    bool nextIsPercent = end < text.Length && text[end] == '%';
                    int value;
                    if (!nextIsPercent && int.TryParse(match.Groups[1].Value, out value))
                        return value;
                }
            }
            catch { }

            return -1;
        }

private bool IsTargetMatch(string observedName, int observedRank, GemTarget target)
        {
            return target != null
                && !string.IsNullOrEmpty(observedName)
                && observedRank >= 0
                && string.Equals(observedName, target.Name, StringComparison.OrdinalIgnoreCase)
                && observedRank == target.Rank;
        }

private static string BuildPageSignature(List<ObservedCell> observedCells)
        {
            if (observedCells == null || observedCells.Count == 0)
                return string.Empty;
            return string.Join("|", observedCells.Select(c => c.IdentityKey));
        }

private void RebuildVirtualGrid(RectangleF listBounds, List<VisibleCell> visibleCells)
        {
            if (_orderedGems == null || _orderedGems.Count == 0)
            {
                _virtualGrid = null;
            _absoluteGrid = null;
                return;
            }

            if (visibleCells == null || visibleCells.Count == 0)
            {
                if (_virtualGrid != null)
                {
                    _virtualGrid.TotalSlotCount = _orderedGems.Count;
                    _virtualGrid.TotalRowCount = (_orderedGems.Count + Math.Max(1, _virtualGrid.ColumnCount) - 1) / Math.Max(1, _virtualGrid.ColumnCount);
                    SyncVirtualGridViewport();
                }
                return;
            }

            int observedRows = Math.Max(1, visibleCells.Select(c => c.RowIndex).DefaultIfEmpty(-1).Max() + 1);
            int columnCount = Math.Max(1, visibleCells.GroupBy(c => c.RowIndex).OrderByDescending(g => g.Count()).Select(g => g.Count()).FirstOrDefault());
            if (columnCount < 5 && _orderedGems.Count >= 5)
                columnCount = 5;

            float cellWidth = visibleCells.Average(c => c.Rect.Width);
            float cellHeight = visibleCells.Average(c => c.Rect.Height);
            float columnPitch = EstimateColumnPitch(visibleCells, cellWidth);
            float rowPitch = EstimateRowPitch(visibleCells, cellHeight);
            int visibleRowCount = CalculateVisibleWindowRowCount(listBounds, visibleCells, rowPitch);
            int geometryCap = CalculateVisibleRowGeometryCap(listBounds, rowPitch, cellHeight);
            visibleRowCount = Math.Max(1, Math.Min(Math.Max(1, observedRows), Math.Min(4, Math.Min(visibleRowCount, geometryCap))));

            var anchorCell = visibleCells.OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex).First();

            RectangleF anchorRect = anchorCell.Rect;
            float usedColPitch = columnPitch > 1f
                ? columnPitch
                : (_virtualGrid != null && _virtualGrid.ColumnPitch > 1f ? _virtualGrid.ColumnPitch : cellWidth);
            float usedRowPitch = rowPitch > 1f
                ? rowPitch
                : (_virtualGrid != null && _virtualGrid.RowPitch > 1f ? _virtualGrid.RowPitch : cellHeight);

            int totalRowCount = (_orderedGems.Count + columnCount - 1) / columnCount;
            if (_lastVirtualGridColumnSignature != -1
                && (_lastVirtualGridColumnSignature != columnCount || _lastVirtualGridRowSignature != totalRowCount))
            {
                Log(() => "grid-shape change: cols " + _lastVirtualGridColumnSignature + "→" + columnCount
                    + ", rows " + _lastVirtualGridRowSignature + "→" + totalRowCount
                    + " — invalidating cached bottom calibration");
                _lastKnownPhysicalBottomTopRow = -1;
            _lastOrderedGemCountSignature = -1;
            _lastVirtualGridColumnSignature = -1;
            _lastVirtualGridRowSignature = -1;
                _scrollAtBottom = false;
            }

            var model = new VirtualGridModel
            {
                ColumnCount = columnCount,
                VisibleRowCount = visibleRowCount,
                LiveScanRowCount = observedRows,
                TotalSlotCount = _orderedGems.Count,
                TotalRowCount = totalRowCount,
                CellWidth = cellWidth,
                CellHeight = cellHeight,
                ColumnPitch = usedColPitch,
                RowPitch = usedRowPitch,
                AnchorCellRect = anchorRect,
                EstimatedTopVisibleRow = GetAuthoritativeViewportTopRow(),
            };

            foreach (var gem in _orderedGems)
            {
                int row = gem.AbsoluteIndex / model.ColumnCount;
                int col = gem.AbsoluteIndex % model.ColumnCount;
                model.Slots.Add(new VirtualSlot
                {
                    AbsoluteIndex = gem.AbsoluteIndex,
                    RowIndex = row,
                    ColumnIndex = col,
                    GemName = GetGemName(gem.Item),
                    GemRank = gem.Item != null ? gem.Item.JewelRank : -1,
                    IsTarget = _target != null && gem.AbsoluteIndex == _target.AbsoluteIndex,
                });
            }

            _virtualGrid = model;
            _lastVirtualGridColumnSignature = model.ColumnCount;
            _lastVirtualGridRowSignature = model.TotalRowCount;
            SyncVirtualGridViewport();
        }

private void SyncVirtualGridViewport()
        {
            if (_virtualGrid == null)
                return;

            _virtualGrid.EstimatedTopVisibleRow = GetAuthoritativeViewportTopRow();
            int effectiveVisibleRows = GetAuthoritativeViewportVisibleRowCount() + (_scrollAtBottom ? 1 : 0);
            _virtualGrid.VisibleRowCount = Math.Max(1, GetAuthoritativeViewportVisibleRowCount());
            foreach (var slot in _virtualGrid.Slots)
            {
                slot.IsPredictedVisible = false;
                slot.PredictedRect = RectangleF.Empty;

                if (_virtualGrid.EstimatedTopVisibleRow < 0)
                    continue;

                int localRow = slot.RowIndex - _virtualGrid.EstimatedTopVisibleRow;
                if (localRow < 0 || localRow >= effectiveVisibleRows)
                    continue;

                float x = _virtualGrid.AnchorCellRect.Left + slot.ColumnIndex * _virtualGrid.ColumnPitch;
                float y = _virtualGrid.AnchorCellRect.Top + localRow * _virtualGrid.RowPitch;
                slot.IsPredictedVisible = true;
                slot.PredictedRect = new RectangleF(x, y, _virtualGrid.CellWidth, _virtualGrid.CellHeight);
            }
        }

private bool TryGetPredictedAbsoluteIndex(VisibleCell cell, out int absoluteIndex)
        {
            absoluteIndex = -1;
            if (cell == null)
                return false;

            if (cell.AbsoluteIndex >= 0)
            {
                absoluteIndex = cell.AbsoluteIndex;
                return true;
            }

            if (_virtualGrid == null || _virtualGrid.EstimatedTopVisibleRow < 0)
                return false;

            absoluteIndex = ((_virtualGrid.EstimatedTopVisibleRow + cell.RowIndex) * _virtualGrid.ColumnCount) + cell.ColumnIndex;
            return absoluteIndex >= 0 && absoluteIndex < _virtualGrid.TotalSlotCount;
        }

private RectangleF GetTargetComfortBounds(RectangleF listBounds)
        {
            if (listBounds == RectangleF.Empty)
                return listBounds;

            float rowPitch = _absoluteGrid != null && _absoluteGrid.RowPitch > 1f
                ? _absoluteGrid.RowPitch
                : (_lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : 58f);
            float columnPitch = _absoluteGrid != null && _absoluteGrid.ColumnPitch > 1f
                ? _absoluteGrid.ColumnPitch
                : (_lastMeasuredColumnPitch > 1f ? _lastMeasuredColumnPitch : Math.Max(40f, listBounds.Width / 5f));

            float verticalInset = Math.Max(8f, Math.Min(18f, rowPitch * 0.18f));
            float horizontalInset = Math.Max(4f, Math.Min(10f, columnPitch * 0.10f));

            return new RectangleF(
                listBounds.Left + horizontalInset,
                listBounds.Top + verticalInset,
                Math.Max(1f, listBounds.Width - horizontalInset * 2f),
                Math.Max(1f, listBounds.Height - verticalInset * 2f));
        }

        private bool TryGetSafeVisibleClickRect(VisibleCell cell, out RectangleF safeRect, out PointF safePoint)
        {
            safeRect = RectangleF.Empty;
            safePoint = PointF.Empty;

            if (cell == null || _currentSnapshot == null || _currentSnapshot.ListBounds == RectangleF.Empty)
                return false;

            RectangleF visibleRect = RectangleF.Intersect(cell.Rect, _currentSnapshot.ListBounds);
            if (visibleRect == RectangleF.Empty || visibleRect.Width <= 1f || visibleRect.Height <= 1f || cell.Rect.Width <= 1f || cell.Rect.Height <= 1f)
                return false;

            float visibleFraction = (visibleRect.Width * visibleRect.Height) / Math.Max(1f, cell.Rect.Width * cell.Rect.Height);
            if (visibleFraction < 0.55f)  // 55% exposed = ~32px on a 58px cell; center-click lands at y+16, inside hitbox
                return false;

            float insetX = Math.Min(10f, Math.Max(4f, visibleRect.Width * 0.15f));
            float insetY = Math.Min(10f, Math.Max(4f, visibleRect.Height * 0.15f));
            safeRect = new RectangleF(
                visibleRect.Left + insetX,
                visibleRect.Top + insetY,
                Math.Max(1f, visibleRect.Width - insetX * 2f),
                Math.Max(1f, visibleRect.Height - insetY * 2f));

            safePoint = new PointF(safeRect.Left + safeRect.Width * 0.50f, safeRect.Top + safeRect.Height * 0.50f);
            return safeRect.Width > 1f && safeRect.Height > 1f;
        }

        private bool IsCellComfortablyInsideViewport(VisibleCell cell, RectangleF comfortBounds, out float topOverflow, out float bottomOverflow)
        {
            topOverflow = 0f;
            bottomOverflow = 0f;

            if (cell == null || comfortBounds == RectangleF.Empty)
                return true;

            topOverflow = Math.Max(0f, comfortBounds.Top - cell.Rect.Top);
            bottomOverflow = Math.Max(0f, cell.Rect.Bottom - comfortBounds.Bottom);

            return topOverflow <= 0.5f && bottomOverflow <= 0.5f;
        }

        private bool TryQueueTargetComfortNudge(VisibleCell cell, string reason)
        {
            if (cell == null || cell.IsProjected || _currentSnapshot == null)
                return false;
            if (_currentSnapshot.ListBounds == RectangleF.Empty)
                return false;
            if (!IsCurrentEpochLiveSlot(cell))
                return false;

            RectangleF comfortBounds = GetTargetComfortBounds(_currentSnapshot.ListBounds);
            float topOverflow;
            float bottomOverflow;
            bool comfortable = IsCellComfortablyInsideViewport(cell, comfortBounds, out topOverflow, out bottomOverflow);

            float rowPitch = _absoluteGrid != null && _absoluteGrid.RowPitch > 1f
                ? _absoluteGrid.RowPitch
                : (_lastMeasuredRowPitch > 1f ? _lastMeasuredRowPitch : Math.Max(40f, cell.Rect.Height));

            // Safe-click check runs BEFORE edgeRow override.
            // If 55%+ of the cell is inside the viewport, click directly without nudging.
            // The edgeRow heuristic was added to catch cells that can't safely be clicked,
            // but it fired even when TryGetSafeVisibleClickRect would have succeeded.
            // 55% of a 58px cell = 32px exposed; center-point is at y+16, reliably inside the hitbox.
            RectangleF safeVisibleRect;
            PointF safeVisiblePoint;
            if (!comfortable && TryGetSafeVisibleClickRect(cell, out safeVisibleRect, out safeVisiblePoint))
            {
                _targetComfortNudgeAttempts = 0;
                MoveCursorToPointNoClick(safeVisiblePoint, "skip-wheel-visible-target-" + reason);
                Log(() => "comfort-nudge skipped: visible target is sufficiently exposed, reason=" + reason);
                UpdateSharedDebugState("skip-nudge-visible-target", reason + " safe-visible", _target != null ? _target.AbsoluteIndex : int.MinValue, cell.RowIndex);
                return false;
            }

            int maxObservedRow = -1;
            try
            {
                if (_currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null && _currentSnapshot.LiveVisibleCells.Count > 0)
                    maxObservedRow = _currentSnapshot.LiveVisibleCells.Max(c => c.RowIndex);
            }
            catch { }

            bool onBottomVisibleRow = maxObservedRow >= 0 && cell.RowIndex >= maxObservedRow;
            bool nearBottomEdge = cell.Rect.Bottom >= (comfortBounds.Bottom - Math.Max(4f, Math.Min(10f, rowPitch * 0.10f)));
            bool forceBottomNudge = onBottomVisibleRow && (bottomOverflow > 0.5f || nearBottomEdge);
            bool edgeRow = forceBottomNudge;
            if (edgeRow)
                comfortable = false;

            Log(() => "comfort-check: target a" + (_target != null ? _target.AbsoluteIndex.ToString(CultureInfo.InvariantCulture) : "-1")
                + " topOverflow=" + topOverflow.ToString("0.0", CultureInfo.InvariantCulture)
                + " bottomOverflow=" + bottomOverflow.ToString("0.0", CultureInfo.InvariantCulture)
                + " edgeRow=" + (edgeRow ? "1" : "0")
                + " comfortable=" + (comfortable ? "1" : "0")
                + " reason=" + reason);
            UpdateSharedDebugState("comfort-check",
                "top=" + topOverflow.ToString("0.0", CultureInfo.InvariantCulture)
                + " bottom=" + bottomOverflow.ToString("0.0", CultureInfo.InvariantCulture)
                + " edge=" + (edgeRow ? "1" : "0")
                + " ok=" + (comfortable ? "1" : "0")
                + " reason=" + reason,
                _target != null ? _target.AbsoluteIndex : int.MinValue,
                cell.RowIndex);

            if (comfortable)
            {
                _targetComfortNudgeAttempts = 0;
                Log(() => "comfort-nudge skipped: visible target already comfortably clickable, reason=" + reason);
                UpdateSharedDebugState("skip-nudge-visible-target", reason, _target != null ? _target.AbsoluteIndex : int.MinValue, cell.RowIndex);
                return false;
            }

            if (_targetComfortNudgeAttempts >= MaxTargetComfortNudgeAttempts)
            {
                Log(() => "comfort-nudge: giving up after " + MaxTargetComfortNudgeAttempts.ToString(CultureInfo.InvariantCulture) + " attempts");
                UpdateSharedDebugState("comfort-giveup", reason, _target != null ? _target.AbsoluteIndex : int.MinValue, cell.RowIndex);
                return false;
            }

            bool downward = forceBottomNudge || bottomOverflow > topOverflow;
            float overflow = Math.Max(topOverflow, bottomOverflow);

            // CHANGED: was (edgeRow || overflow < rowPitch*0.22f).
            // 0.22 was too narrow — a 34px clip on a 70px row (49%) fell through to legacy
            // click-hold (Thread.Sleep). Now always prefer wheel; one tick = one row, which
            // is exactly right for any partial-clip scenario. The legacy path is removed.
            bool useWheel = true;
            bool lateTp = _portalRequestedThisRun || (Hud.Game?.Me != null && Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal);

            if (useWheel)
            {
                PointF wheelHoverPoint;
                bool wheelDownward;
                if (TryGetWheelComfortHoverPoint(cell, out wheelHoverPoint, out wheelDownward)
                    && !EnsureCursorReadyForWheelScroll(
                        wheelHoverPoint,
                        "comfort-nudge-" + (wheelDownward ? "down" : "up"),
                        _target != null ? _target.AbsoluteIndex : int.MinValue,
                        cell.RowIndex))
                {
                    return true;
                }

                _targetComfortNudgeAttempts++;
                _lastLiveCellCountBeforeScroll = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null
                    ? _currentSnapshot.LiveVisibleCells.Count
                    : 0;

                Log(() => "comfort-wheel-nudge: " + (downward ? "downward" : "upward")
                    + " tick=1 because " + (downward ? "bottomOverflow=" : "topOverflow=")
                    + overflow.ToString("0.0", CultureInfo.InvariantCulture)
                    + (edgeRow ? " edgeRow=1" : string.Empty)
                    + " reason=" + reason
                    + (lateTp ? " [late-tp]" : string.Empty));
                UpdateSharedDebugState("comfort-wheel-nudge",
                    (downward ? "down" : "up") + " tick=1 overflow=" + overflow.ToString("0.0", CultureInfo.InvariantCulture)
                    + " edge=" + (edgeRow ? "1" : "0")
                    + " reason=" + reason,
                    _target != null ? _target.AbsoluteIndex : int.MinValue,
                    cell.RowIndex);

                WheelScrollTick(downward, "comfort-nudge");
                _wheelPostNudgeCorrectionPending = true;
                _wheelPostNudgeTargetAbs = _target != null ? _target.AbsoluteIndex : int.MinValue;
                _lastActionTick = NowTick();
                _afterScrollWait = 0;
                _stage = AutomationStage.DirectCaptureCurrentPage;
                return true;
            }

            // useWheel is always true; this line is only here to satisfy the compiler.
            return false;
        }

private void ClickVisibleCell(VisibleCell cell)
        {
            if (cell == null)
                return;
            if (!HasLiveViewportTruth())
            {
                Log(() => "click blocked: no live viewport truth on current epoch");
                return;
            }
            if (cell.IsProjected)
            {
                Log(() => "click blocked: projected cell is travel-only and cannot be committed");
                return;
            }
            if (!IsCurrentEpochLiveSlot(cell))
            {
                Log(() => "click blocked: cell is not a verified live slot on the current viewport epoch");
                return;
            }

            int absIndex;
            _currentProbeAbsoluteIndex = TryGetPredictedAbsoluteIndex(cell, out absIndex) ? absIndex : -1;

            string cellTag = "probe-cell" + (_currentProbeAbsoluteIndex >= 0 ? (" a" + _currentProbeAbsoluteIndex) : string.Empty)
                + " " + GetShortPath(cell.Ref != null ? cell.Ref.Path : string.Empty);

            bool forceCoordinate = _lostLiveIdentityAfterScroll;

            if (cell.Ref?.Element != null && !cell.IsProjected && !forceCoordinate)
            {
                Log(() => "click " + cellTag + " via UIElement");
                UpdateSharedDebugState("select-visible", cellTag + "|ui", _target != null ? _target.AbsoluteIndex : int.MinValue, cell.RowIndex);
                ClickUi(cell.Ref.Element);
            }
            else
            {
                if (forceCoordinate)
                    Log(() => "click " + cellTag + " via coordinate (identity-loss: stale UIElement skipped)");

                float x = cell.Rect.Left + cell.Rect.Width * 0.5f;
                bool edgeBiasedClick = cell.IsProjected || forceCoordinate || _lostLiveIdentityAfterScroll;
                float y;
                if (edgeBiasedClick)
                {
                    RectangleF hitBounds = RectangleF.Empty;
                    try
                    {
                        if (_currentSnapshot != null)
                            hitBounds = _currentSnapshot.ListBounds;
                        else if (_gemUpgradePane?.Visible == true)
                            hitBounds = GetAuthoritativeGemListBounds(_gemUpgradePane.Rectangle);
                    }
                    catch { }

                    float retryFrac = 0.20f;
                    if (_stage == AutomationStage.ValidateObservedTarget)
                    {
                        if (_targetValidationAttempts >= 2)
                            retryFrac = 0.14f;
                        else if (_targetValidationAttempts >= 1)
                            retryFrac = 0.28f;
                    }

                    float inset = Math.Min(16f, Math.Max(8f, cell.Rect.Height * retryFrac));
                    float topOverflow = hitBounds == RectangleF.Empty ? 0f : Math.Max(0f, hitBounds.Top - cell.Rect.Top);
                    float bottomOverflow = hitBounds == RectangleF.Empty ? 0f : Math.Max(0f, cell.Rect.Bottom - hitBounds.Bottom);

                    bool preferBottomEdge = topOverflow > bottomOverflow && topOverflow > 1f;
                    bool preferTopEdge = bottomOverflow > topOverflow && bottomOverflow > 1f;

                    if (!preferTopEdge && !preferBottomEdge)
                    {
                        int maxObservedRow = -1;
                        try
                        {
                            if (_currentSnapshot != null && _currentSnapshot.VisibleCells != null && _currentSnapshot.VisibleCells.Count > 0)
                                maxObservedRow = _currentSnapshot.VisibleCells.Max(c => c.RowIndex);
                        }
                        catch { }

                        if (cell.RowIndex <= 0 && GetAuthoritativeViewportTopRow() > 0)
                            preferBottomEdge = true;
                        else if (maxObservedRow >= 0 && cell.RowIndex >= maxObservedRow)
                            preferTopEdge = true;
                    }

                    y = preferBottomEdge
                        ? (cell.Rect.Bottom - inset)
                        : (cell.Rect.Top + inset);
                }
                else
                {
                    y = cell.Rect.Top + cell.Rect.Height * 0.5f;
                }
                UpdateSharedDebugState("select-visible", cellTag + "|coord", _target != null ? _target.AbsoluteIndex : int.MinValue, cell.RowIndex);
                ClickPoint(new PointF(x, y), cellTag, 0);
            }
        }

private int GetCurrentViewportVisibleRowCount()
        {
            return GetAuthoritativeViewportVisibleRowCount();
        }

    }
}
