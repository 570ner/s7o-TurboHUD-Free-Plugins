// REV30 - production cleanup; retain tab/anvil readiness and monitor-coordinate fixes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
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

namespace Turbo.Plugins.s7o
{

    public class s7o_ItemSalvage : BasePlugin,
        IKeyEventHandler,
        IAfterCollectHandler,
        INewAreaHandler,
        IInGameTopPainter,
        IMouseClickHandler
    {
        // ============================================================
        // USER SETTINGS
        // ============================================================

        // Default hotkey if no saved settings file exists.
        // UI hotkey changes are saved to plugins/s7o/settings/s7o_ItemSalvage.ini when PersistUserSettings is true.
        // To change it in script, edit this line, for example: Key.F4, Key.X, Key.Comma.
        public Key SalvageHotkey = Key.F3;

        // Salvage now has one universal adaptive speed; there is no user speed selector.
        public bool PersistUserSettings = true;

        // Safety toggles.
        public bool UseInventoryLock = true;
        public bool UseAddedSocketProtection = true;
        public bool SalvageNormal = true;
        public bool SalvageMagic = true;
        public bool SalvageRare = true;

        // Legendary/set/ancient/primal gear is eligible by default, but protected user-modified gear
        // is still blocked: occupied socket, enchanted, added socket enhancement, augmented, armory, locked, vendor-bought.
        public bool SalvageLegendary = true;

        // Salvage legendary/special potions by default.
        // Stacked/material/special safety filters still apply before this.
        public bool SalvagePotion = true;
        public bool SalvageWhisperOfAtonementBelow125 = false;

        // One universal adaptive mode. The user no longer chooses among arbitrary speed levels.
        // The bounded pipeline ramps after clean completions and skips overdue items for this run.
        // No network-latency value is used for pacing.
        public int AdaptiveInitialOutstanding = 12;
        public int AdaptiveMinOutstanding = 2;
        public int AdaptiveMaxOutstanding = 24;
        public int AdaptiveMaxClicksPerCollect = 24;
        public int AdaptiveRampAfterCleanGone = 10;
        public int AdaptiveStaleAgeMs = 300;
        public int AdaptiveStaleFreshObservations = 3;
        public int AdaptiveLegendaryWatchdogMs = 1500;
        public int AdaptiveUiTransitionWatchdogMs = 500;
        public int DebounceMs = 150;
        public bool SpeculativeLegendaryEnter = true;
        private const int MaxInputFailuresPerRun = 3;
        private const int MaxConfirmationActions = 4;
        private int _inputFailuresThisRun;
        private int _repairClickTick = NoTick;
        private bool _speculativeEnterPending;
        private int _runStartTick;

        // After the final salvage click, park the cursor at the player's feet once, then release control.
        public bool ParkCursorAtPlayerFeetAfterFinalSalvage = true;

        // Stable repair/safety additions.
        // Repair is checked once per F3 run before salvage. If no repair is needed, no repair click is sent.
        public bool AutoRepair = true;

        // Wait only when the plugin actually toggles the salvage/anvil button on.
        // This mirrors LightningMOD's UI-transition settling without slowing successful per-item clicks.

        // Throttle visible-dialog fallback; speculative Enter is sent once at item click.
        public int ConfirmRetryThrottleMs = 15;

        // Chat safety: confirmation Enter can occasionally be observed again while the OK dialog is
        // fading, so allow only one Enter per visible confirmation before using a click fallback.
        public int ConfirmClickFallbackDelayMs = 140;
        public bool CloseChatIfOpenedDuringSalvage = true;
        public int ChatCloseSettleMs = 90;

        // Blacksmith pane detection. This keeps the overlay off Mystic/Jeweler/vendor panes.
        public bool UseBlacksmithTextDetection = true;
        public bool UseSelectedBlacksmithActorFallback = true;
        public bool StickyBlacksmithPaneUntilClosed = true;
        public int BlacksmithContextRefreshMs = 250;

        // Rounded geometry is preferred, but the overlay can fall back to rectangles if Direct2D geometry fails.
        public bool UseRoundedGeometryButtons = true;

        // Marker settings.
        // Blue armory dots are general inventory/stash guidance.
        // Purple protection dots are salvage-context-only warnings.
        public bool ShowArmoryItemDots = true;
        public bool ShowSalvageProtectionDots = true;

        // Backward-compatible master toggle for all item protection markers.
        public bool ShowProtectedSkipDots = true;

        // Font marker placement. Blue * = Armory item. Purple * = salvage-protected non-armory item.
        public float ItemMarkerTextOffsetX = 3.0f;
        public float ItemMarkerTextOffsetY = 0.0f;
        public float ItemMarkerFontSize = 9.0f;

        public float HeaderTopOffset = 38.0f;
        public float HeaderLeftOffset = 46.0f;
        public float HeaderRightOffset = 84.0f;
        public float HeaderHotkeyGroupWidth = 92.0f;

        public float HotkeyButtonWidth = 42.0f;
        public float HotkeyButtonHeight = 18.0f;

        public int ButtonFlashMs = 90;

        // Universal adaptive scheduling replaces the old 1-10 speed tables.

        private IKeyEvent _salvageKeyEvent;

        private IUiElement _vendorPage;
        private IUiElement _salvageDialog;
        private IUiElement _repairDialog;
        private IUiElement _salvageTab;
        private IUiElement _repairTab;
        private IUiElement _repairCostButton;
        private IUiElement _salvageSelectedButton1;
        private IUiElement _salvageSelectedButton2;
        private IUiElement _okButton;
        private IUiElement _chatEditLine;

        private IFont _yellowFont;
        private IFont _buttonFont;
        private IBrush _pillDarkBrush;
        private IBrush _pillLightBrush;
        private IBrush _pillGreenBrush;
        private IBrush _pillGreenLightBrush;
        private IBrush _pillOrangeBorderBrush;
        private IFont _armoryMarkerFont;
        private IFont _salvageProtectionMarkerFont;

        private RectangleF _hotkeyButtonRect = RectangleF.Empty;
        private bool _overlayControlsVisible;
        private bool _capturingHotkey;
        private const int NoTick = int.MinValue;

        private State _state;
        private bool _cancelRequested;
        private bool _salvageTabClickSent;
        private bool _repairCheckedThisRun;
        private bool _repairTabClickSent;
        private bool _anvilEnableClickSent;
        private string _runEndReason;
        private int _anvilReadyWaitTick = NoTick;

        private int _lastHotkeyTick;
        private int _nextStepTick;
        private int _confirmStartTick;
        private int _originalCursorX;
        private int _originalCursorY;
        private int _runClickedCount;
        private int _runGoneCount;
        private readonly List<string> _turboClickedKeys = new List<string>();
        private readonly HashSet<string> _turboGoneKeys = new HashSet<string>();
        private readonly Dictionary<string, int> _turboItemClickAttempts = new Dictionary<string, int>();

        private int _lastItemClickTick;
        private int _lastConfirmTick;
        private int _confirmVisibleSinceTick = NoTick;
        private int _lastConfirmPressTick = NoTick;
        private int _confirmPressAttempts;
        private bool _awaitingSalvageConfirm;
        private bool _adaptiveLegendaryEnterSent;

        // Universal adaptive transaction ledger. A clicked item remains pending until it
        // disappears; merely surviving an early collection pass is never treated as failure.
        private readonly Dictionary<string, int> _adaptivePendingSinceTick = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _adaptivePendingFreshObservations = new Dictionary<string, int>();
        private readonly HashSet<string> _adaptiveAbandonedKeys = new HashSet<string>();
        private int _adaptiveOutstandingLimit;
        private int _adaptiveCleanGoneStreak;

        // Event-driven UI transition watchdogs. Healthy transitions proceed on the first
        // observed ready state; these ticks are only failure ceilings, not pacing delays.
        private int _repairTabClickTick = NoTick;
        private int _salvageTabClickTick = NoTick;
        private int _anvilEnableClickTick = NoTick;

        private bool _cachedBlacksmithPaneVisible;
        private bool _stickyBlacksmithPaneVisible;
        private int _nextBlacksmithContextRefreshTick = NoTick;
        private int _lastBlacksmithActorSeenTick;

        private bool _paintExceptionLogged;
        private bool _geometryDrawFailed;
        private bool _geometryDrawFailureLogged;

        private string _settingsPath;
        private string _legacySettingsPath;

        private readonly Queue<string> _pendingItemKeys = new Queue<string>();
        private string _activeItemKey;
        private bool _parkCursorOnCompletionPending;
        private bool _finalCursorParked;

        // ============================================================

        // ============================================================

        // ============================================================================

        // Test-only hooks: deleting the diagnostics companion erases calls and argument evaluation.

        private enum State
        {
            Idle,
            Prepare,
            OpenRepairTab,
            RepairIfNeeded,
            AwaitRepair,
            OpenSalvageTab,
            EnableAnvil,
            WaitAfterAnvilEnable,
            AdaptiveClickItem,
            AdaptiveAwaitLegendaryConfirm,
            AdaptiveSettle,
            Done
        }

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint KeyUp = 0x0002;
        private const ushort VkEnter = 0x0D;
        private const ushort VkEscape = 0x1B;
        private const int BlacksmithActorLatchMs = 1500;

        public s7o_ItemSalvage()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            InitializePluginPaths();
            LoadUserSettings();
            if (PersistUserSettings && !string.IsNullOrEmpty(_settingsPath) && !File.Exists(_settingsPath))
                SaveUserSettings();

            _salvageKeyEvent = Hud.Input.CreateKeyEvent(true, SalvageHotkey, false, false, false);

            _vendorPage = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage",
                Hud.Inventory.InventoryMainUiElement,
                null);

            _salvageDialog = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.salvage_dialog",
                _vendorPage,
                null);

            _repairDialog = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.repair_dialog",
                _vendorPage,
                null);

            _salvageTab = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.tab_2",
                _vendorPage,
                null);

            _repairTab = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.tab_3",
                _vendorPage,
                null);

            _repairCostButton = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.repair_dialog.RepairEquipped",
                _vendorPage,
                null);

            _salvageSelectedButton1 = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.salvage_dialog.salvage_all_wrapper.salvage_button",
                _salvageDialog,
                null);

            _salvageSelectedButton2 = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.vendor_dialog_mainPage.salvage_dialog.salvage_button",
                _salvageDialog,
                null);

            _okButton = Hud.Render.RegisterUiElement(
                "Root.TopLayer.confirmation.subdlg.stack.wrap.button_ok",
                _salvageDialog,
                null);

            try
            {
                _chatEditLine = Hud.Render.RegisterUiElement(
                    "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline",
                    null,
                    null);
            }
            catch
            {
                _chatEditLine = null;
            }

            _yellowFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 220, 0, true, false, 255, 0, 0, 0, true);
            _buttonFont = Hud.Render.CreateFont("tahoma", 8.0f, 255, 235, 235, 235, true, false, 255, 0, 0, 0, true);

            _pillDarkBrush = Hud.Render.CreateBrush(255, 48, 48, 48, 0);
            _pillLightBrush = Hud.Render.CreateBrush(80, 125, 125, 125, 0);
            _pillGreenBrush = Hud.Render.CreateBrush(255, 0, 170, 60, 0);
            _pillGreenLightBrush = Hud.Render.CreateBrush(90, 120, 255, 150, 0);
            _pillOrangeBorderBrush = Hud.Render.CreateBrush(225, 105, 55, 10, 0);

            // Font markers behave like compact item labels and draw immediately during inventory paint.
            _armoryMarkerFont = Hud.Render.CreateFont(
                "tahoma",
                ItemMarkerFontSize,
                255,
                40,
                150,
                255,
                true,
                false,
                255,
                0,
                0,
                0,
                true);

            _salvageProtectionMarkerFont = Hud.Render.CreateFont(
                "tahoma",
                ItemMarkerFontSize,
                255,
                190,
                70,
                255,
                true,
                false,
                255,
                0,
                0,
                0,
                true);

        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {

            ResetBlacksmithContextCache();
            CancelRun(false, false);
        }

        public void ForceStopForDisable()
        {
            try
            {
                if (_state == State.Idle)
                {
                    _cancelRequested = false;
                    return;
                }

                if (string.IsNullOrEmpty(_runEndReason))
                    _runEndReason = "Item Salvage disabled";

                CancelRun(true, true);
            }
            catch { }
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed) return;

            if (_capturingHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHotkey = false;

                    return;
                }

                SalvageHotkey = keyEvent.Key;
                _salvageKeyEvent = Hud.Input.CreateKeyEvent(true, SalvageHotkey, false, false, false);
                _capturingHotkey = false;
                SaveUserSettings();

                return;
            }

            if (_salvageKeyEvent == null || !_salvageKeyEvent.Matches(keyEvent)) return;

            int now = Environment.TickCount;
            UpdateBlacksmithActorLatch(now);

            if ((uint)(now - _lastHotkeyTick) < (uint)Math.Max(0, DebounceMs)) return;
            _lastHotkeyTick = now;

            if (_state != State.Idle)
            {
                _cancelRequested = true;
                _runEndReason = "Item Salvage cancelled by hotkey";

                return;
            }

            if (!IsBlacksmithPaneVisible())
            {

                return;
            }

            if (IsChatEntryOpen())
            {

                return;
            }

            QueueRun();

        }

        public void AfterCollect()
        {
            try
            {
                AfterCollectSafe();

            }
            catch (Exception)
            {

                _runEndReason = "Item Salvage cancelled: runtime state reset after UI interruption";

                ResetBlacksmithContextCache();
                CancelRun(false, true);
            }
        }

        private void AfterCollectSafe()
        {
            int now = Environment.TickCount;
            UpdateBlacksmithActorLatch(now);

            if (_state == State.Idle) return;

            if (ElapsedAtLeast(now, _runStartTick, 30000))
            {
                _runEndReason = "Item Salvage stopped: 30-second run watchdog";
                CancelRun(true, true);
                return;
            }

            if (_cancelRequested)
            {
                CancelRun(!ShouldRestoreCursorOnCancel(), true);
                return;
            }

            if (IsGenericConfirmationVisible() && !IsSalvageConfirmVisible())
            {
                _runEndReason = "Item Salvage cancelled: unrelated confirmation dialog interrupted salvage";
                ResetBlacksmithContextCache();
                CancelRun(false, true);
                return;
            }

            if (!IsValidContext())
            {
                _runEndReason = IsGenericConfirmationVisible()
                    ? "Item Salvage cancelled: external dialog interrupted the blacksmith pane"
                    : "Item Salvage cancelled: blacksmith pane closed";
                ResetBlacksmithContextCache();
                CancelRun(false, true);
                return;
            }

            if (TryCloseChatEntryDuringRun(now, "after collect"))
                return;

            // Confirmation popups are authoritative: do not complete/cancel/park/click another item while OK is visible.
            if (HandleVisibleSalvageConfirmFirst(now, "after collect"))
                return;

            // Observe completions before another click; skip overdue items for this run.
            if (Hud.Inventory.ItemsInInventory == null)
            {
                _runEndReason = "Item Salvage stopped: inventory snapshot unavailable";
                CancelRun(true, true);
                return;
            }
            if (ProcessAdaptiveTransactions(now))
                return;

            if (CompleteIfNoLiveSalvageCandidates("after collect"))
                return;

            if (!TickReachedOrUnset(now, _nextStepTick)) return;

            switch (_state)
            {
                case State.Prepare:
                    if (ShouldCheckRepairThisRun())
                    {
                        _state = State.OpenRepairTab;
                        _repairTabClickSent = false;
                        _repairTabClickTick = NoTick;
                        _nextStepTick = now;
                        return;
                    }

                    BuildCandidateQueue();
                    if (_pendingItemKeys.Count == 0)
                    {
                        _runEndReason = "Item Salvage completed: no eligible candidates";
                        CancelRun(false, true);
                        return;
                    }

                    if (!IsSalvageDialogVisible())
                    {
                        _state = State.OpenSalvageTab;
                        _salvageTabClickSent = false;
                        _salvageTabClickTick = NoTick;
                        _nextStepTick = now;
                        return;
                    }

                    _state = State.EnableAnvil;
                    goto case State.EnableAnvil;

                case State.OpenRepairTab:
                    if (IsRepairDialogVisible())
                    {
                        _repairTabClickTick = NoTick;
                        _state = State.RepairIfNeeded;
                        goto case State.RepairIfNeeded;
                    }

                    if (!_repairTabClickSent)
                    {
                        bool repairInputAttempted;
                        if (TryClickStartupTab(_repairTab, out repairInputAttempted))
                        {
                            _repairTabClickSent = true;
                            _repairTabClickTick = now;

                            _nextStepTick = now;
                            return;
                        }

                        if (_state == State.Idle) return;
                        if (repairInputAttempted)
                        {
                            _runEndReason = "Item Salvage stopped: repair tab input failed";
                            CancelRun(true, true);
                            return;
                        }

                        if (!TickSet(_repairTabClickTick))
                        {
                            _repairTabClickTick = now;

                        }
                        if (unchecked(now - _repairTabClickTick) < Math.Max(100, AdaptiveUiTransitionWatchdogMs))
                        {
                            _nextStepTick = now;
                            return;
                        }

                        _repairCheckedThisRun = true;
                        _state = State.OpenSalvageTab;
                        _salvageTabClickSent = false;
                        _salvageTabClickTick = NoTick;
                        _nextStepTick = now;
                        return;
                    }

                    if (TickSet(_repairTabClickTick)
                        && unchecked(now - _repairTabClickTick) < Math.Max(100, AdaptiveUiTransitionWatchdogMs))
                    {
                        _nextStepTick = now;
                        return;
                    }

                    _repairCheckedThisRun = true;

                    _state = State.OpenSalvageTab;
                    _salvageTabClickSent = false;
                    _nextStepTick = now;
                    return;

                case State.RepairIfNeeded:
                    _repairCheckedThisRun = true;

                    long repairCost;
                    if (TryGetRepairCost(out repairCost))
                    {

                        if (repairCost > 0 && CanAffordRepair(repairCost))
                        {
                            if (ClickUi(_repairCostButton))
                            {

                                _repairClickTick = now;
                                _state = State.AwaitRepair;
                                _salvageTabClickSent = false;
                                _salvageTabClickTick = NoTick;
                                _nextStepTick = now;
                                return;
                            }

                        }
                        else
                        {

                        }
                    }
                    else
                    {

                    }

                    _state = State.OpenSalvageTab;
                    _salvageTabClickSent = false;
                    _nextStepTick = now;
                    return;

                case State.AwaitRepair:
                    long remainingRepairCost;
                    bool repairCostReadable = TryGetRepairCost(out remainingRepairCost);

                    bool repaired = repairCostReadable && remainingRepairCost == 0;
                    if (!repaired && !ElapsedAtLeast(now, _repairClickTick, Math.Max(100, AdaptiveUiTransitionWatchdogMs)))
                        return;

                    if (!repaired)
                    {
                        _runEndReason = "Item Salvage stopped: repair completion not verified";
                        CancelRun(true, true);
                        return;
                    }
                    _state = State.OpenSalvageTab;
                    goto case State.OpenSalvageTab;

                case State.OpenSalvageTab:
                    if (IsSalvageDialogVisible())
                    {
                        _salvageTabClickTick = NoTick;
                        _state = State.Prepare;
                        goto case State.Prepare;
                    }

                    if (!_salvageTabClickSent)
                    {
                        bool salvageInputAttempted;
                        if (TryClickStartupTab(_salvageTab, out salvageInputAttempted))
                        {
                            _salvageTabClickSent = true;
                            _salvageTabClickTick = now;

                            _nextStepTick = now;
                            return;
                        }

                        if (_state == State.Idle) return;
                        if (salvageInputAttempted)
                        {
                            _runEndReason = "Item Salvage stopped: salvage tab input failed";
                            CancelRun(true, true);
                            return;
                        }

                        if (!TickSet(_salvageTabClickTick))
                        {
                            _salvageTabClickTick = now;

                        }
                        if (unchecked(now - _salvageTabClickTick) < Math.Max(100, AdaptiveUiTransitionWatchdogMs))
                        {
                            _nextStepTick = now;
                            return;
                        }

                        _runEndReason = "Item Salvage cancelled: salvage tab control unavailable before watchdog";
                        CancelRun(true, true);
                        return;
                    }

                    if (TickSet(_salvageTabClickTick)
                        && unchecked(now - _salvageTabClickTick) < Math.Max(100, AdaptiveUiTransitionWatchdogMs))
                    {
                        _nextStepTick = now;
                        return;
                    }

                    _runEndReason = "Item Salvage cancelled: salvage page did not become ready before watchdog";
                    CancelRun(true, true);
                    return;

                case State.EnableAnvil:
                    var readyAnvil = GetVisibleAnvilButton();
                    if (readyAnvil == null || readyAnvil.Rectangle.Width <= 0 || readyAnvil.Rectangle.Height <= 0)
                    {
                        if (!TickSet(_anvilReadyWaitTick))
                        {
                            _anvilReadyWaitTick = now;

                        }
                        if (!ElapsedAtLeast(now, _anvilReadyWaitTick, Math.Max(100, AdaptiveUiTransitionWatchdogMs)))
                            return;
                        _runEndReason = "Item Salvage stopped: anvil button unavailable before watchdog";
                        CancelRun(true, true);
                        return;
                    }

                    bool anvilClicked;
                    if (!SetAnvil(true, out anvilClicked))
                    {
                        _runEndReason = "Item Salvage cancelled: could not enable anvil";
                        CancelRun(true, true);
                        return;
                    }

                    _anvilEnableClickSent = anvilClicked;
                    if (anvilClicked)
                    {
                        _anvilEnableClickTick = now;

                    }
                    else if (!TickSet(_anvilEnableClickTick))
                    {
                        _anvilEnableClickTick = now;
                    }
                    _state = State.WaitAfterAnvilEnable;
                    _nextStepTick = now;
                    return;

                case State.WaitAfterAnvilEnable:
                    var observedAnvil = GetVisibleAnvilButton();
                    if (!IsAnvilEnabled(observedAnvil))
                    {
                        if (!TickSet(_anvilEnableClickTick)
                            || unchecked(now - _anvilEnableClickTick) < Math.Max(100, AdaptiveUiTransitionWatchdogMs))
                        {
                            if (!_anvilEnableClickSent && observedAnvil != null)
                            {

                                _state = State.EnableAnvil;
                                goto case State.EnableAnvil;
                            }
                            _nextStepTick = now;
                            return;
                        }

                        _runEndReason = "Item Salvage cancelled: anvil did not become ready before watchdog";
                        CancelRun(true, true);
                        return;
                    }

                    _anvilEnableClickTick = NoTick;
                    _state = State.AdaptiveClickItem;
                    goto case State.AdaptiveClickItem;

                case State.AdaptiveClickItem:
                    ProcessAdaptiveClickItem(now);
                    return;

                case State.AdaptiveAwaitLegendaryConfirm:
                    ProcessAdaptiveAwaitLegendaryConfirm(now);
                    return;

                case State.AdaptiveSettle:
                    ProcessAdaptiveSettle(now);
                    return;

                case State.Done:

                    CancelRun(false, false);
                    return;
            }
        }

        private bool IsActiveSalvageMousePhase()
        {
            return _state == State.EnableAnvil
                || _state == State.WaitAfterAnvilEnable
                || _state == State.AdaptiveClickItem
                || _state == State.AdaptiveAwaitLegendaryConfirm
                || _state == State.AdaptiveSettle;
        }

        private bool HasLiveSalvageCandidates()
        {
            var inventoryItems = Hud.Inventory.ItemsInInventory;
            if (inventoryItems == null) return false;

            foreach (var item in inventoryItems)
            {
                if (item == null) continue;
                if (!CanSalvage(item)) continue;

                string key = item.ItemUniqueId;
                if (string.IsNullOrEmpty(key)) continue;

                return true;
            }

            return false;
        }

        private bool CompleteIfNoLiveSalvageCandidates(string source)
        {
            if (!IsActiveSalvageMousePhase())
                return false;

            int now = Environment.TickCount;

            if (HasUnresolvedSalvageWorkForEarlyCompletion(now))
                return false;

            if (HasLiveSalvageCandidates())
                return false;

            return CompleteRunWithOptionalCursorPark("Item Salvage completed");
        }

        private bool ShouldRestoreCursorOnCancel()
        {
            if (IsActiveSalvageMousePhase() && !HasLiveSalvageCandidates())
                return false;

            return true;
        }

        private bool IsSalvageConfirmVisible()
        {
            if (!_awaitingSalvageConfirm)
                return false;

            if (!IsSalvageDialogVisible())
                return false;

            return IsGenericConfirmationVisible();
        }

        private bool IsGenericConfirmationVisible()
        {
            try
            {
                if (_okButton == null) return false;
                _okButton.Refresh();
                return _okButton.Visible;
            }
            catch
            {
                return false;
            }
        }

        private bool IsChatEntryOpen()
        {
            try
            {
                if (_chatEditLine == null) return false;
                _chatEditLine.Refresh();
                return _chatEditLine.Visible;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCloseChatEntryDuringRun(int now, string source)
        {
            if (!CloseChatIfOpenedDuringSalvage || !IsChatEntryOpen())
                return false;

            if (!TickReachedOrUnset(now, _nextStepTick)) return true;
            PressEscape();
            _nextStepTick = unchecked(now + Math.Max(0, ChatCloseSettleMs));

            return true;
        }

        private bool IsConfirmationSensitiveState()
        {
            return _state == State.AdaptiveAwaitLegendaryConfirm
                || _state == State.AdaptiveSettle
                || _awaitingSalvageConfirm
                || !string.IsNullOrEmpty(_activeItemKey)
                || _adaptivePendingSinceTick.Count > 0
                || TickSet(_confirmStartTick)
                || TickSet(_lastItemClickTick);
        }

        private bool HandleVisibleSalvageConfirmFirst(int now, string source)
        {
            if (!IsConfirmationSensitiveState())
                return false;

            if (!IsSalvageConfirmVisible())
            {
                if (!IsGenericConfirmationVisible() && TickSet(_confirmVisibleSinceTick))
                    _awaitingSalvageConfirm = false;

                _confirmVisibleSinceTick = NoTick;
                _lastConfirmPressTick = NoTick;
                _confirmPressAttempts = 0;
                return false;
            }

            if (TryCloseChatEntryDuringRun(now, "visible confirm " + source))
                return true;

            if (!TickSet(_confirmVisibleSinceTick))
                _confirmVisibleSinceTick = now;

            int confirmRetryMs = Math.Max(10, ConfirmRetryThrottleMs);
            int fallbackDelayMs = Math.Max(confirmRetryMs, Math.Max(10, ConfirmClickFallbackDelayMs));
            int nextActionDelayMs = _confirmPressAttempts <= 0 ? confirmRetryMs : fallbackDelayMs;
            if (!ElapsedAtLeast(now, _lastConfirmPressTick, nextActionDelayMs))
                return true;

            bool turboAwait = _state == State.AdaptiveAwaitLegendaryConfirm;

            if (_confirmPressAttempts >= MaxConfirmationActions)
            {
                _runEndReason = "Item Salvage stopped: confirmation did not close after bounded fallback";
                CancelRun(true, true);
                return true;
            }
            _confirmPressAttempts++;
            _lastConfirmPressTick = now;
            _lastConfirmTick = now;

            if (_confirmPressAttempts == 1)
            {

                PressEnter();

            }
            else
            {

                ClickUiDirectNoCompletionCheck(_okButton);

            }

            if (turboAwait)
            {
                _adaptiveLegendaryEnterSent = true;
                _nextStepTick = now;
                return true;
            }

            _nextStepTick = unchecked(now + (_confirmPressAttempts == 1 ? 1 : fallbackDelayMs));
            return true;
        }

        private bool ProcessAdaptiveTransactions(int now)
        {
            foreach (string key in _adaptivePendingSinceTick.Keys.ToList())
            {
                if (!InventoryContainsKey(key)) { MarkAdaptiveGone(key, now); continue; }
                int observations;
                _adaptivePendingFreshObservations.TryGetValue(key, out observations);
                _adaptivePendingFreshObservations[key] = Math.Min(1000, observations + 1);
                if (_state == State.AdaptiveAwaitLegendaryConfirm && key == _activeItemKey) continue;
                if (unchecked(now - _adaptivePendingSinceTick[key]) < Math.Max(100, AdaptiveStaleAgeMs)
                    || observations + 1 < Math.Max(1, AdaptiveStaleFreshObservations)) continue;
                AbandonAdaptiveItemForRun(key);
            }
            return false;
        }

        private void AbandonAdaptiveItemForRun(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _adaptivePendingSinceTick.Remove(key);
            _adaptivePendingFreshObservations.Remove(key);
            _adaptiveAbandonedKeys.Add(key);
        }

        private void MarkAdaptiveGone(string key, int now)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool pending = _adaptivePendingSinceTick.Remove(key);
            _adaptivePendingFreshObservations.Remove(key);
            if (!_turboGoneKeys.Add(key)) return;
            _runGoneCount++;
            if (pending && ++_adaptiveCleanGoneStreak >= Math.Max(1, AdaptiveRampAfterCleanGone)
                && _adaptiveOutstandingLimit < Math.Max(1, AdaptiveMaxOutstanding))
            {
                _adaptiveOutstandingLimit++;
                _adaptiveCleanGoneStreak = 0;
            }
        }

        private IItem ResolveInventoryItemByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var items = Hud.Inventory.ItemsInInventory;
            if (items == null) return null;
            return items.FirstOrDefault(i => i != null && string.Equals(i.ItemUniqueId, key, StringComparison.Ordinal));
        }

        private bool HasUnresolvedSalvageWorkForEarlyCompletion(int now)
        {
            if (HasUnresolvedClickedOrConfirmWork(now)) return true;
            if (_state == State.AdaptiveAwaitLegendaryConfirm) return true;
            if (_state == State.AdaptiveSettle) return true;

            if (_adaptivePendingSinceTick.Count > 0) return true;
            return false;
        }

        private bool HasUnresolvedClickedOrConfirmWork(int now)
        {
            return HasUnresolvedClickedOrConfirmWork(now, true);
        }

        private bool HasUnresolvedClickedOrConfirmWork(int now, bool includeRecentGrace)
        {
            if (IsSalvageConfirmVisible()) return true;
            if (!string.IsNullOrEmpty(_activeItemKey)) return true;
            if (_pendingItemKeys.Count > 0) return true;

            foreach (string key in _turboClickedKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (_turboGoneKeys.Contains(key) || _adaptiveAbandonedKeys.Contains(key)) continue;
                return true;
            }

            if (includeRecentGrace)
            {
                if (IsRecentTick(now, _lastItemClickTick, 300)) return true;
                if (IsRecentTick(now, _lastConfirmTick, 300)) return true;
                if (IsRecentTick(now, _confirmStartTick, 300)) return true;
            }

            return false;
        }

        private bool CompleteRunWithOptionalCursorPark(string reason)
        {
            int now = Environment.TickCount;

            _runEndReason = reason;

            if (_parkCursorOnCompletionPending || ParkCursorAtPlayerFeetAfterFinalSalvage)
                ParkCursorAtPlayerFeetAfterFinalSalvageOnce();

            CancelRun(false, true);
            return true;
        }

        private void ProcessAdaptiveClickItem(int now)
        {

            var anvilButton = GetVisibleAnvilButton();
            if (!IsAnvilEnabled(anvilButton))
            {
                _state = State.EnableAnvil;
                _nextStepTick = now;
                return;
            }

            int maxPerCollect = Math.Max(1, AdaptiveMaxClicksPerCollect);
            int clickedThisCollect = 0;
            int scanBudget = _pendingItemKeys.Count;

            while (_pendingItemKeys.Count > 0 && scanBudget-- > 0
                && clickedThisCollect < maxPerCollect
                && _adaptivePendingSinceTick.Count < _adaptiveOutstandingLimit)
            {
                string key = _pendingItemKeys.Dequeue();
                IItem item = ResolveCandidate(key);

                if (item == null)
                {
                    continue;
                }

                // Never click an already in-flight item. A surviving item is pending, not failed.
                if (_adaptivePendingSinceTick.ContainsKey(key))
                    continue;

                if (!TryRegisterItemClickAttempt(key, item))
                    continue;

                int clickTick = Environment.TickCount;

                if (!ClickInventoryItemTurbo(item))
                {
                    _turboItemClickAttempts.Remove(key);
                    _inputFailuresThisRun++;

                    if (_inputFailuresThisRun >= MaxInputFailuresPerRun)
                    {
                        _runEndReason = "Item Salvage stopped: input failure budget exhausted";
                        CancelRun(true, true);
                    }
                    else if (CanSalvage(item))
                        _pendingItemKeys.Enqueue(key);
                    return;
                }

                if (_pendingItemKeys.Count == 0)
                    MarkFinalItemClickedForCompletionPark();

                _lastItemClickTick = clickTick;
                _runClickedCount++;
                clickedThisCollect++;
                _turboClickedKeys.Add(key);
                _adaptivePendingSinceTick[key] = clickTick;
                _adaptivePendingFreshObservations[key] = 0;

                if (item.IsLegendary)
                {
                    _activeItemKey = key;
                    _awaitingSalvageConfirm = true;
                    _adaptiveLegendaryEnterSent = false;
                    _confirmStartTick = clickTick;
                    _state = State.AdaptiveAwaitLegendaryConfirm;
                    _speculativeEnterPending = SpeculativeLegendaryEnter && !IsChatEntryOpen();
                    if (_speculativeEnterPending)
                    {
                        _adaptiveLegendaryEnterSent = PressEnter();
                        _speculativeEnterPending = _adaptiveLegendaryEnterSent;

                    }
                    _nextStepTick = now;
                    return;
                }
            }

            if (_pendingItemKeys.Count == 0)
            {
                _state = State.AdaptiveSettle;
                _nextStepTick = now;
                return;
            }

            // Pipeline full or per-collect budget exhausted. The next collection boundary is
            // the natural scheduler; no Thread.Sleep, Hud.Wait, or latency-derived delay.
            _state = State.AdaptiveClickItem;
            _nextStepTick = now;
        }

        private void ProcessAdaptiveAwaitLegendaryConfirm(int now)
        {
            if (IsSalvageConfirmVisible())
            {
                HandleVisibleSalvageConfirmFirst(now, "adaptive legendary await");
                _nextStepTick = now;
                return;
            }

            if (!string.IsNullOrEmpty(_activeItemKey) && !InventoryContainsKey(_activeItemKey))
            {
                MarkAdaptiveGone(_activeItemKey, now);
                _activeItemKey = null;
                _awaitingSalvageConfirm = false;
                _adaptiveLegendaryEnterSent = false;
                ResumeAdaptivePipeline(now);
                return;
            }

            // A hidden dialog after speculative Enter does not prove completion.
            // Keep the item pending until disappearance or the bounded confirmation fallback.
            if (_adaptiveLegendaryEnterSent && !IsGenericConfirmationVisible())
            {

                _speculativeEnterPending = false;
                _activeItemKey = null;
                _awaitingSalvageConfirm = false;
                _adaptiveLegendaryEnterSent = false;
                ResumeAdaptivePipeline(now);
                if (_state == State.AdaptiveClickItem) ProcessAdaptiveClickItem(now);
                return;
            }

            if (TickSet(_confirmStartTick)
                && unchecked(now - _confirmStartTick) >= Math.Max(250, AdaptiveLegendaryWatchdogMs))
            {
                string stuckKey = _activeItemKey;
                _activeItemKey = null;
                _awaitingSalvageConfirm = false;
                _adaptiveLegendaryEnterSent = false;

                if (!string.IsNullOrEmpty(stuckKey) && InventoryContainsKey(stuckKey))
                {

                    AbandonAdaptiveItemForRun(stuckKey);
                }

                ResumeAdaptivePipeline(now);
                return;
            }

            _state = State.AdaptiveAwaitLegendaryConfirm;
            _nextStepTick = now;
        }

        private void ResumeAdaptivePipeline(int now)
        {
            _state = _pendingItemKeys.Count > 0 ? State.AdaptiveClickItem : State.AdaptiveSettle;
            _nextStepTick = now;
        }

        private void ProcessAdaptiveSettle(int now)
        {

            if (_adaptivePendingSinceTick.Count > 0)
            {
                _state = State.AdaptiveSettle;
                _nextStepTick = now;
                return;
            }

            if (_pendingItemKeys.Count > 0)
            {
                _state = State.AdaptiveClickItem;
                _nextStepTick = now;
                return;
            }

            CompleteRunWithOptionalCursorPark("Item Salvage completed");
        }

        private void QueueRun()
        {
            _anvilReadyWaitTick = NoTick;
            _runStartTick = Environment.TickCount;

            _inputFailuresThisRun = 0;
            _speculativeEnterPending = false;
            _repairClickTick = NoTick;
            _originalCursorX = Hud.Window.CursorX;
            _originalCursorY = Hud.Window.CursorY;
            _activeItemKey = null;
            _parkCursorOnCompletionPending = false;
            _finalCursorParked = false;
            _pendingItemKeys.Clear();
            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboItemClickAttempts.Clear();
            _runClickedCount = 0;
            _runGoneCount = 0;
            _repairCheckedThisRun = false;
            _repairTabClickSent = false;
            _anvilEnableClickSent = false;
            _confirmStartTick = 0;
            _lastItemClickTick = 0;
            _lastConfirmTick = 0;
            _confirmVisibleSinceTick = NoTick;
            _lastConfirmPressTick = NoTick;
            _confirmPressAttempts = 0;
            _awaitingSalvageConfirm = false;
            _adaptiveLegendaryEnterSent = false;
            _adaptivePendingSinceTick.Clear();
            _adaptivePendingFreshObservations.Clear();
            _adaptiveAbandonedKeys.Clear();
            _adaptiveOutstandingLimit = Math.Max(
                Math.Max(1, AdaptiveMinOutstanding),
                Math.Min(Math.Max(1, AdaptiveMaxOutstanding), Math.Max(1, AdaptiveInitialOutstanding)));
            _adaptiveCleanGoneStreak = 0;

            _repairTabClickTick = NoTick;
            _salvageTabClickTick = NoTick;
            _anvilEnableClickTick = NoTick;
            _cancelRequested = false;
            _salvageTabClickSent = false;
            _repairTabClickSent = false;
            _runEndReason = null;
            _state = State.Prepare;
            _nextStepTick = Environment.TickCount;

        }

        private void MarkFinalItemClickedForCompletionPark()
        {
            _parkCursorOnCompletionPending = true;
        }

        private void ParkCursorAtPlayerFeetAfterFinalSalvageOnce()
        {
            if (_finalCursorParked || !ParkCursorAtPlayerFeetAfterFinalSalvage)
                return;

            try
            {
                var me = Hud.Game.Me;
                if (me == null || me.FloorCoordinate == null)
                    return;

                var screen = me.FloorCoordinate.ToScreenCoordinate();
                MoveCursorClient((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                _finalCursorParked = true;
            }
            catch
            {
            }
        }

        private void CancelRun(bool restoreCursor, bool logSummary)
        {

            _pendingItemKeys.Clear();
            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboItemClickAttempts.Clear();
            _activeItemKey = null;
            _parkCursorOnCompletionPending = false;
            _finalCursorParked = false;
            _confirmStartTick = 0;
            _lastItemClickTick = 0;
            _lastConfirmTick = 0;
            _confirmVisibleSinceTick = NoTick;
            _lastConfirmPressTick = NoTick;
            _confirmPressAttempts = 0;
            _awaitingSalvageConfirm = false;
            _adaptiveLegendaryEnterSent = false;
            _adaptivePendingSinceTick.Clear();
            _adaptivePendingFreshObservations.Clear();
            _adaptiveAbandonedKeys.Clear();

            _repairTabClickTick = NoTick;
            _salvageTabClickTick = NoTick;
            _anvilEnableClickTick = NoTick;
            _cancelRequested = false;
            _salvageTabClickSent = false;
            _repairCheckedThisRun = false;
            _repairTabClickSent = false;
            _anvilEnableClickSent = false;

            if (restoreCursor)
                MoveCursorClient(_originalCursorX, _originalCursorY);

            _state = State.Idle;
            _nextStepTick = 0;
        }

        private void InitializePluginPaths()
        {
            var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
            var settingsDir = Path.Combine(pluginDir, "settings");

            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(settingsDir);

            _settingsPath = Path.Combine(settingsDir, "s7o_ItemSalvage.ini");
            _legacySettingsPath = Path.Combine(pluginDir, "s7o_ItemSalvage.settings.ini");
        }

        private string SelectUserSettingsReadPath()
        {
            try
            {
                if (!string.IsNullOrEmpty(_settingsPath) && File.Exists(_settingsPath))
                    return _settingsPath;
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(_legacySettingsPath) && File.Exists(_legacySettingsPath))
                    return _legacySettingsPath;
            }
            catch { }

            return _settingsPath;
        }

        private void LoadUserSettings()
        {
            if (!PersistUserSettings) return;
            if (string.IsNullOrEmpty(_settingsPath)) return;

            string readPath = SelectUserSettingsReadPath();

            if (string.IsNullOrEmpty(readPath)) return;
            if (!File.Exists(readPath)) return;

            try
            {
                var lines = File.ReadAllLines(readPath);

                foreach (var rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;

                    string line = rawLine.Trim();
                    if (line.StartsWith("#")) continue;

                    int split = line.IndexOf('=');
                    if (split <= 0) continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    if (EqualsText(key, "SalvageHotkey"))
                    {
                        try
                        {
                            SalvageHotkey = (Key)Enum.Parse(typeof(Key), value, true);
                        }
                        catch
                        {
                            // Ignore invalid saved hotkey.
                        }
                    }

                }

                try
                {
                    if (!string.Equals(
                        Path.GetFullPath(readPath).TrimEnd('\\', '/'),
                        Path.GetFullPath(_settingsPath).TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        SaveUserSettings();
                    }
                }
                catch { }
            }
            catch (Exception)
            {

            }
        }

        private void SaveUserSettings()
        {
            if (!PersistUserSettings) return;

            try
            {
                if (string.IsNullOrEmpty(_settingsPath))
                    InitializePluginPaths();

                string content =
                    "# s7o_ItemSalvage user settings" + Environment.NewLine
                    + "# This file is written by the plugin when you change the salvage hotkey." + Environment.NewLine
                    + "# Salvage uses one universal adaptive speed; legacy SalvageSpeed entries are ignored." + Environment.NewLine
                    + Environment.NewLine
                    + "SalvageHotkey=" + SalvageHotkey + Environment.NewLine;

                string dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_settingsPath, content);

            }
            catch (Exception)
            {

            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            try
            {
                PaintTopInGameSafe(clipState);
            }
            catch (Exception)
            {
                _overlayControlsVisible = false;
                if (!_paintExceptionLogged)
                {
                    _paintExceptionLogged = true;

                }
            }
        }

        private void PaintTopInGameSafe(ClipState clipState)
        {
            if (clipState != ClipState.Inventory)
            {
                _overlayControlsVisible = false;
                return;
            }

            bool overlayVisible = UpdateOverlayLayoutRects();
            _overlayControlsVisible = overlayVisible;

            if (overlayVisible)
                DrawHeaderHotkey();

            // Armory dots must show during normal inventory/stash browsing,
            // even when the blacksmith/salvage panel is closed.
            DrawProtectedDots();
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left) return false;
            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground) return false;

            int now = Environment.TickCount;
            UpdateBlacksmithActorLatch(now);

            if (!UpdateOverlayLayoutRects())
            {
                _overlayControlsVisible = false;
                return false;
            }

            _overlayControlsVisible = true;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;

            bool hitHotkey = PointInRect(_hotkeyButtonRect, x, y);

            if (!hitHotkey)
                return false;

            if (hitHotkey)
            {
                if (_state == State.Idle)
                {
                    _capturingHotkey = true;

                }
                else
                {

                }
                return true;
            }

            return false;
        }

        public bool MouseUp(MouseButtons button)
        {
            return false;
        }

        private bool IsValidContext()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Inventory == null || Hud.Window == null) return false;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused) return false;
            if (!Hud.Game.IsInTown) return false;
            if (!Hud.Window.IsForeground) return false;

            if (!IsBaseVendorPaneVisible()) return false;
            if (IsSalvageDialogVisible()) return true;

            if (_state == State.Prepare ||
                _state == State.OpenRepairTab ||
                _state == State.RepairIfNeeded ||
                _state == State.AwaitRepair ||
                _state == State.OpenSalvageTab)
            {
                return IsBlacksmithPaneVisible();
            }

            return false;
        }

        private bool IsSalvageDialogVisible()
        {
            if (_salvageDialog == null) return false;
            _salvageDialog.Refresh();
            return _salvageDialog.Visible;
        }

        private bool IsRepairDialogVisible()
        {
            if (_repairDialog == null) return false;
            _repairDialog.Refresh();
            return _repairDialog.Visible;
        }

        private bool IsOverlayContextVisible()
        {
            return IsBlacksmithPaneVisible();
        }

        private bool IsBaseVendorPaneVisible()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Inventory == null) return false;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused) return false;

            if (Hud.Inventory.InventoryMainUiElement == null) return false;
            Hud.Inventory.InventoryMainUiElement.Refresh();
            if (!Hud.Inventory.InventoryMainUiElement.Visible) return false;

            if (_vendorPage == null) return false;
            _vendorPage.Refresh();
            return _vendorPage.Visible;
        }

        private bool IsVisible(IUiElement element)
        {
            if (element == null) return false;
            element.Refresh();
            return element.Visible;
        }

        private string ReadUiTextSafe(IUiElement element)
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

        private bool ShouldCheckRepairThisRun()
        {
            if (!AutoRepair) return false;
            if (_repairCheckedThisRun) return false;
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null) return false;

            // Matches LightningMOD's practical guard: auto-repair only on level 70 characters.
            if (Hud.Game.Me.CurrentLevelNormal < 70) return false;

            return true;
        }

        private bool TryGetRepairCost(out long cost)
        {
            cost = 0;

            if (_repairCostButton == null)
                return false;

            string text = ReadUiTextSafe(_repairCostButton);
            if (string.IsNullOrEmpty(text))
                return false;

            return TryExtractDigitsAsLong(text, out cost);
        }

        private bool CanAffordRepair(long cost)
        {
            if (cost <= 0) return false;

            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Materials == null)
                    return false;

                return Hud.Game.Me.Materials.Gold >= cost;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractDigitsAsLong(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var digits = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                    digits.Append(c);
            }

            if (digits.Length == 0)
                return false;

            return long.TryParse(digits.ToString(), out value);
        }

        private static bool ContainsText(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return false;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResetBlacksmithContextCache()
        {
            ResetBlacksmithContextCache(true);
        }

        private void ResetBlacksmithContextCache(bool clearActorLatch)
        {
            _cachedBlacksmithPaneVisible = false;
            _stickyBlacksmithPaneVisible = false;
            _nextBlacksmithContextRefreshTick = NoTick;

            if (clearActorLatch)
                _lastBlacksmithActorSeenTick = 0;
        }

        private void UpdateBlacksmithActorLatch(int now)
        {
            if (!UseSelectedBlacksmithActorFallback) return;

            try
            {
                if (Hud == null || Hud.Game == null) return;
                if (Hud.Game.SelectedActor == null || Hud.Game.SelectedActor.SnoActor == null) return;

                if (!IsKnownBlacksmithActor(Hud.Game.SelectedActor.SnoActor.Sno)) return;

                _lastBlacksmithActorSeenTick = now;
                _nextBlacksmithContextRefreshTick = NoTick;
            }
            catch { }
        }

        private bool RecentlySawBlacksmithActor(int now)
        {
            if (_lastBlacksmithActorSeenTick == 0) return false;
            return (uint)(now - _lastBlacksmithActorSeenTick) <= (uint)BlacksmithActorLatchMs;
        }

        private static bool IsKnownBlacksmithActor(ActorSnoEnum sno)
        {
            switch (sno)
            {
                case ActorSnoEnum._pt_blacksmith:
                case ActorSnoEnum._pt_blacksmith_repairshortcut:
                case ActorSnoEnum._pt_blacksmith_forgeweaponshortcut:
                case ActorSnoEnum._pt_blacksmith_forgearmorshortcut:
                case ActorSnoEnum._p76_pt_blacksmith_repairshortcut:
                case ActorSnoEnum._p76_pt_blacksmith_forgeweaponshortcut:
                case ActorSnoEnum._p76_pt_blacksmith_forgearmorshortcut:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsBlacksmithPaneVisible()
        {
            if (!IsBaseVendorPaneVisible())
            {
                ResetBlacksmithContextCache(false);
                return false;
            }

            int now = Environment.TickCount;
            if (TickIsFuture(now, _nextBlacksmithContextRefreshTick))
                return _cachedBlacksmithPaneVisible || _stickyBlacksmithPaneVisible;

            _nextBlacksmithContextRefreshTick = unchecked(now + Math.Max(50, BlacksmithContextRefreshMs));

            bool result = ComputeBlacksmithPaneVisible();
            _cachedBlacksmithPaneVisible = result;
            return result || _stickyBlacksmithPaneVisible;
        }

        private bool ComputeBlacksmithPaneVisible()
        {
            string vendorText = UseBlacksmithTextDetection ? ReadUiTextSafe(_vendorPage) : string.Empty;
            string salvageTabText = UseBlacksmithTextDetection ? ReadUiTextSafe(_salvageTab) : string.Empty;
            string repairTabText = UseBlacksmithTextDetection ? ReadUiTextSafe(_repairTab) : string.Empty;
            string selectedActorText = UseSelectedBlacksmithActorFallback ? GetSelectedActorText() : string.Empty;

            string combinedText = vendorText
                + " "
                + salvageTabText
                + " "
                + repairTabText
                + " "
                + selectedActorText;

            if (IsDefinitelyNonBlacksmithText(combinedText))
            {
                _stickyBlacksmithPaneVisible = false;

                return false;
            }

            if (IsVisible(_salvageDialog) ||
                IsVisible(_repairDialog) ||
                IsVisible(_salvageSelectedButton1) ||
                IsVisible(_salvageSelectedButton2))
            {
                _stickyBlacksmithPaneVisible = true;
                return true;
            }

            if (IsBlacksmithText(combinedText))
            {
                _stickyBlacksmithPaneVisible = true;
                return true;
            }

            if (RecentlySawBlacksmithActor(Environment.TickCount))
            {
                _stickyBlacksmithPaneVisible = true;
                return true;
            }

            if (StickyBlacksmithPaneUntilClosed && _stickyBlacksmithPaneVisible)
                return true;

            return false;
        }

        private bool IsBlacksmithText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            return ContainsText(text, "blacksmith")
                || ContainsText(text, "haedrig")
                || ContainsText(text, "eamon")
                || ContainsText(text, "forge armor")
                || ContainsText(text, "forge weapon")
                || ContainsText(text, "forge weapons")
                || ContainsText(text, "forgearmor")
                || ContainsText(text, "forgeweapon")
                || ContainsText(text, "salvage");
        }

        private bool IsDefinitelyNonBlacksmithText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            return ContainsText(text, "jeweler")
                || ContainsText(text, "covetous shen")
                || ContainsText(text, "combine gems")
                || ContainsText(text, "remove gem")
                || ContainsText(text, "mystic")
                || ContainsText(text, "myriam")
                || ContainsText(text, "transmogrification")
                || ContainsText(text, "transmog")
                || ContainsText(text, "enchant item")
                || ContainsText(text, "dye");
        }

        private string GetSelectedActorText()
        {
            try
            {
                if (Hud == null || Hud.Game == null) return string.Empty;
                if (Hud.Game.SelectedActor == null || Hud.Game.SelectedActor.SnoActor == null) return string.Empty;

                var sno = Hud.Game.SelectedActor.SnoActor;
                return (sno.Code ?? string.Empty)
                    + " "
                    + (sno.NameEnglish ?? string.Empty)
                    + " "
                    + sno.Sno.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

                private void BuildCandidateQueue()
        {
            _pendingItemKeys.Clear();

            var inventoryItems = Hud.Inventory.ItemsInInventory;
            if (inventoryItems == null)
            {
                return;
            }

            var items = inventoryItems
                .Where(i => i != null)
                .OrderBy(i => i.InventoryX)
                .ThenBy(i => i.InventoryY)
                .ToList();

            foreach (var item in items)
            {
                string key = item.ItemUniqueId;
                string reason = GetSalvageBlockReason(item);

                if (reason == null)
                {
                    if (string.IsNullOrEmpty(key))
                    {

                    }
                    else if (_adaptivePendingSinceTick.ContainsKey(key))
                    {

                    }
                    else
                    {
                        int attempts;
                        _turboItemClickAttempts.TryGetValue(key, out attempts);
                        if (attempts <= 0)
                            _pendingItemKeys.Enqueue(key);
                    }
                }

            }

        }

        private bool TryRegisterItemClickAttempt(string key, IItem item)
        {
            if (string.IsNullOrEmpty(key)) return false;

            int attempts;
            _turboItemClickAttempts.TryGetValue(key, out attempts);
            if (attempts > 0)
                return false;

            _turboItemClickAttempts[key] = 1;
            return true;
        }

        private IItem ResolveCandidate(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            var inventoryItems = Hud.Inventory.ItemsInInventory;
            if (inventoryItems == null) return null;

            var item = inventoryItems.FirstOrDefault(i => i != null && string.Equals(i.ItemUniqueId, key, StringComparison.Ordinal));
            if (item == null) return null;
            if (!CanSalvage(item)) return null;
            return item;
        }

        private bool InventoryContainsKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            var inventoryItems = Hud.Inventory.ItemsInInventory;
            if (inventoryItems == null) return false;
            return inventoryItems.Any(i => i != null && string.Equals(i.ItemUniqueId, key, StringComparison.Ordinal));
        }

        private bool CanSalvage(IItem item)
        {
            return GetSalvageBlockReason(item) == null;
        }

        private string GetSalvageBlockReason(IItem item)
        {
            if (item == null) return "null item";
            if (item.SnoItem == null) return "null SnoItem";
            if (item.Location != ItemLocation.Inventory) return "not inventory";
            if (item.SnoItem.Kind != ItemKind.loot && item.SnoItem.Kind != ItemKind.potion) return "not loot/potion";
            if (item.VendorBought) return "vendor bought";
            if (UseInventoryLock && item.IsInventoryLocked) return "inventory locked";
            if (HasOccupiedSocket(item)) return "occupied socket";
            if (item.EnchantedAffixCounter != 0) return "enchanted";
            if (UseAddedSocketProtection && HasAddedSocketEnhancement(item)) return "added socket enhancement";
            if (item.CaldesannRank > 0) return "augmented CaldesannRank=" + item.CaldesannRank;
            if (InArmorySet(item)) return "armory";
            if (item.Quantity > 1) return "stack quantity=" + item.Quantity;

            string main = item.SnoItem.MainGroupCode ?? string.Empty;
            string code = item.SnoItem.Code ?? string.Empty;
            string name = item.SnoItem.NameEnglish ?? string.Empty;

            if (EqualsText(main, "riftkeystone")) return "rift key";
            if (EqualsText(main, "horadriccache")) return "horadric cache";
            if (EqualsText(main, "-")) return "main group -";
            if (EqualsText(main, "pony")) return "pony/special";
            if (EqualsText(main, "plans")) return "plans";
            if (main.IndexOf("cosmetic", StringComparison.OrdinalIgnoreCase) >= 0) return "cosmetic";
            if (EqualsText(name, "Staff of Herding")) return "Staff of Herding";
            if (EqualsText(name, "Hellforge Ember")) return "Hellforge Ember";

            if (item.SnoItem.Sno == 1661412389u) return "starter item";
            if (item.SnoItem.Sno == 1815806856u) return "starter item";
            if (item.SnoItem.Sno == 3382510415u) return "starter item";
            if (item.SnoItem.Sno == 4176712417u) return "starter item";
            if (item.SnoItem.Sno == 3931575626u) return "starter item";
            if (item.SnoItem.Sno == 1236604967u) return "starter item";
            if (item.SnoItem.Sno == 111732407u) return "starter item";
            if (item.SnoItem.Sno == 3659697712u) return "starter item";
            if (item.SnoItem.Sno == 88665049u) return "starter item";

            if (EqualsText(main, "gems_unique"))
            {
                if (IsWhisperOfAtonement(item))
                {
                    if (!SalvageWhisperOfAtonementBelow125) return "WoA disabled";
                    if (item.JewelRank >= 125) return "WoA rank >= 125";
                    return null;
                }

                return "legendary gem";
            }

            if (code.StartsWith("P72_Soulshard", StringComparison.OrdinalIgnoreCase)) return "soul shard";

            if (item.SnoItem.Kind == ItemKind.potion)
            {
                return SalvagePotion ? null : "potion disabled";
            }

            if (item.IsNormal) return SalvageNormal ? null : "normal disabled";
            if (item.IsMagic) return SalvageMagic ? null : "magic disabled";
            if (item.IsRare) return SalvageRare ? null : "rare disabled";

            if (item.IsLegendary)
            {
                if (IsWhisperOfAtonement(item))
                {
                    if (!SalvageWhisperOfAtonementBelow125) return "WoA disabled";
                    if (item.JewelRank >= 125) return "WoA rank >= 125";
                    return null;
                }

                if (code.StartsWith("HealthPotionLegendary", StringComparison.OrdinalIgnoreCase))
                    return SalvagePotion ? null : "potion disabled";

                return SalvageLegendary ? null : "legendary disabled";
            }

            return "unknown quality";
        }

        private static bool HasOccupiedSocket(IItem item)
        {
            if (item == null || item.ItemsInSocket == null) return false;
            return item.ItemsInSocket.Any(socketedItem => socketedItem != null);
        }

        private static bool HasAddedSocketEnhancement(IItem item)
        {
            if (item == null || item.StatList == null) return false;

            foreach (var stat in item.StatList)
            {
                if (stat == null || stat.Attribute == null) continue;
                var code = stat.Attribute.Code ?? string.Empty;

                if (code.IndexOf("ConsumableAddSockets", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (code.IndexOf("AddSocketsType", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (code.IndexOf("AddSockets", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private bool IsInventoryVisibleForMarkers()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Inventory == null)
                return false;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused)
                return false;

            if (Hud.Inventory.InventoryMainUiElement == null)
                return false;

            Hud.Inventory.InventoryMainUiElement.Refresh();
            return Hud.Inventory.InventoryMainUiElement.Visible;
        }

        private bool IsStashVisibleForMarkers()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Inventory == null)
                return false;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused)
                return false;

            if (Hud.Inventory.StashMainUiElement == null)
                return false;

            Hud.Inventory.StashMainUiElement.Refresh();
            return Hud.Inventory.StashMainUiElement.Visible;
        }

        private bool IsSalvagePanelVisibleForProtectionMarkers()
        {
            // Purple protection dots should only appear in the blacksmith/salvage context.
            // Reuse the existing overlay visibility logic instead of showing these during normal browsing.
            return IsOverlayContextVisible();
        }

        private bool ShouldShowArmoryDot(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            if (item.Location != ItemLocation.Inventory && item.Location != ItemLocation.Stash)
                return false;

            if (item.SnoItem.Kind != ItemKind.loot && item.SnoItem.Kind != ItemKind.potion)
                return false;

            return InArmorySet(item);
        }

        private bool ShouldShowSalvageProtectionDot(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            // Purple dots are only for inventory items during salvage context.
            if (item.Location != ItemLocation.Inventory)
                return false;

            if (item.SnoItem.Kind != ItemKind.loot && item.SnoItem.Kind != ItemKind.potion)
                return false;

            // Armory items get the blue dot instead, not purple.
            if (InArmorySet(item))
                return false;

            // Purple means protected from salvage for modification/lock reasons.
            if (HasOccupiedSocket(item)) return true;
            if (item.EnchantedAffixCounter != 0) return true;
            if (UseAddedSocketProtection && HasAddedSocketEnhancement(item)) return true;
            if (item.CaldesannRank > 0) return true;
            if (UseInventoryLock && item.IsInventoryLocked) return true;

            return false;
        }

        private bool UpdateOverlayLayoutRects()
        {
            _hotkeyButtonRect = RectangleF.Empty;

            if (!IsOverlayContextVisible()) return false;
            if (_vendorPage == null) return false;

            _vendorPage.Refresh();
            if (!_vendorPage.Visible) return false;

            var pane = _vendorPage.Rectangle;
            float topY = pane.Y + HeaderTopOffset;

            float hotkeyGroupX = pane.X + HeaderLeftOffset;
            float hotkeyButtonX = hotkeyGroupX + (HeaderHotkeyGroupWidth - HotkeyButtonWidth) * 0.5f;
            float hotkeyButtonY = topY + 18.0f;

            _hotkeyButtonRect = new RectangleF(hotkeyButtonX, hotkeyButtonY, HotkeyButtonWidth, HotkeyButtonHeight);

            return true;
        }

        private void DrawHeaderHotkey()
        {
            if (_yellowFont == null || _vendorPage == null) return;

            var pane = _vendorPage.Rectangle;
            float groupX = pane.X + HeaderLeftOffset;
            float topY = pane.Y + HeaderTopOffset;

            string label = s7o_Localization.Get("overlay.salvage.title", "SALVAGE");
            var layout = _yellowFont.GetTextLayout(label);
            float labelX = groupX + HeaderHotkeyGroupWidth * 0.5f - layout.Metrics.Width * 0.5f;
            _yellowFont.DrawText(layout, labelX, topY);

            DrawPillButton(_hotkeyButtonRect, _capturingHotkey ? "..." : SalvageHotkey.ToString(), _capturingHotkey);
        }

        private void DrawProtectedDots()
        {
            if (!ShowProtectedSkipDots) return;

            if (ShowArmoryItemDots)
            {
                DrawArmoryMarkersFromInventory();
                DrawArmoryMarkersFromStash();
            }

            if (ShowSalvageProtectionDots && IsSalvagePanelVisibleForProtectionMarkers())
            {
                DrawSalvageProtectionMarkersFromInventory();
            }
        }

        private void DrawArmoryMarkersFromInventory()
        {
            if (!IsInventoryVisibleForMarkers()) return;

            var items = Hud.Inventory.ItemsInInventory;
            if (items == null) return;

            foreach (var item in items)
            {
                if (!ShouldShowArmoryDot(item)) continue;

                var rect = Hud.Inventory.GetItemRect(item);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                DrawItemTopLeftMarker(rect, _armoryMarkerFont, "*");
            }
        }

        private void DrawArmoryMarkersFromStash()
        {
            if (!IsStashVisibleForMarkers()) return;

            var items = Hud.Inventory.ItemsInStash;
            if (items == null) return;

            int selectedPage = Hud.Inventory.SelectedStashPageIndex;
            int selectedTab = Hud.Inventory.SelectedStashTabIndex;
            int selectedTabAbs = selectedTab + (selectedPage * Hud.Inventory.MaxStashTabCountPerPage);

            foreach (var item in items)
            {
                if (!ShouldShowArmoryDot(item)) continue;

                int itemTabAbs = item.InventoryY / 10;
                if (itemTabAbs != selectedTabAbs)
                    continue;

                var rect = Hud.Inventory.GetItemRect(item);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                DrawItemTopLeftMarker(rect, _armoryMarkerFont, "*");
            }
        }

        private void DrawSalvageProtectionMarkersFromInventory()
        {
            if (!IsInventoryVisibleForMarkers()) return;

            var items = Hud.Inventory.ItemsInInventory;
            if (items == null) return;

            foreach (var item in items)
            {
                if (!ShouldShowSalvageProtectionDot(item)) continue;

                var rect = Hud.Inventory.GetItemRect(item);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                DrawItemTopLeftMarker(rect, _salvageProtectionMarkerFont, "*");
            }
        }

        private void DrawItemTopLeftMarker(RectangleF rect, IFont font, string marker)
        {
            if (font == null || string.IsNullOrEmpty(marker))
                return;

            var layout = font.GetTextLayout(marker);

            float x = rect.X + ItemMarkerTextOffsetX;
            float y = rect.Y + ItemMarkerTextOffsetY;

            font.DrawText(layout, x, y);
        }

        private void DrawPillButton(RectangleF rect, string text, bool green)
        {
            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, _pillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? _pillGreenBrush : _pillDarkBrush);

            var highlight = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, inner.Width - 2.0f, inner.Height * 0.42f);
            DrawRoundedRect(highlight, highlight.Height * 0.5f, green ? _pillGreenLightBrush : _pillLightBrush);

            DrawCenteredText(rect, text);
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
            catch (Exception)
            {
                _geometryDrawFailed = true;
                if (!_geometryDrawFailureLogged)
                {
                    _geometryDrawFailureLogged = true;

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
                gs.AddLine(new Vector2(cx + radius * (float)Math.Cos(rad), cy + radius * (float)Math.Sin(rad)));
            }
        }

        private void DrawCenteredText(RectangleF rect, string text)
        {
            text = s7o_Localization.DisplayButton(text);
            if (_buttonFont == null) return;

            var layout = _buttonFont.GetTextLayout(text ?? string.Empty);
            float tx = rect.X + rect.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float ty = rect.Y + rect.Height * 0.5f - layout.Metrics.Height * 0.5f;
            _buttonFont.DrawText(layout, tx, ty);
        }

        private static bool TickSet(int tick)
        {
            return tick != 0 && tick != NoTick;
        }

        private static bool TickReachedOrUnset(int now, int tick)
        {
            return !TickSet(tick) || unchecked(now - tick) >= 0;
        }

        private static bool TickIsFuture(int now, int untilTick)
        {
            return TickSet(untilTick) && unchecked(now - untilTick) < 0;
        }

        private static bool ElapsedAtLeast(int now, int sinceTick, int ms)
        {
            if (!TickSet(sinceTick)) return true;
            return unchecked(now - sinceTick) >= Math.Max(0, ms);
        }

        private static bool IsRecentTick(int now, int sinceTick, int ms)
        {
            if (!TickSet(sinceTick)) return false;
            return unchecked(now - sinceTick) < Math.Max(0, ms);
        }

        private static bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && rect.Contains(x, y);
        }

        private static string SafeItemName(IItem item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrEmpty(item.FullNameEnglish)) return item.FullNameEnglish;
            if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameEnglish)) return item.SnoItem.NameEnglish;
            return item.ItemUniqueId ?? string.Empty;
        }

        private bool IsWhisperOfAtonement(IItem item)
        {
            if (item == null || item.SnoItem == null) return false;
            string name = item.SnoItem.NameEnglish ?? string.Empty;
            string fullName = item.FullNameEnglish ?? string.Empty;
            return EqualsText(name, "Whisper of Atonement") || EqualsText(fullName, "Whisper of Atonement");
        }

        private bool InArmorySet(IItem item)
        {
            if (item == null || Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.ArmorySets == null) return false;

            for (int i = 0; i < Hud.Game.Me.ArmorySets.Length; i++)
            {
                var armorySet = Hud.Game.Me.ArmorySets[i];
                if (armorySet != null && armorySet.ContainsItem(item))
                    return true;
            }

            return false;
        }

        private static bool EqualsText(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private IUiElement GetVisibleAnvilButton()
        {
            if (_salvageSelectedButton1 != null)
            {
                _salvageSelectedButton1.Refresh();
                if (_salvageSelectedButton1.Visible) return _salvageSelectedButton1;
            }

            if (_salvageSelectedButton2 != null)
            {
                _salvageSelectedButton2.Refresh();
                if (_salvageSelectedButton2.Visible) return _salvageSelectedButton2;
            }

            return null;
        }

        private bool IsAnvilEnabled(IUiElement button)
        {
            if (button == null) return false;
            button.Refresh();
            return button.Visible && (button.AnimState == 19 || button.AnimState == 20);
        }

        private bool SetAnvil(bool enabled)
        {
            bool clicked;
            return SetAnvil(enabled, out clicked);
        }

        private bool SetAnvil(bool enabled, out bool clicked)
        {
            clicked = false;

            var button = GetVisibleAnvilButton();
            if (button == null)
            { return false; }

            if (IsAnvilEnabled(button) == enabled)
                return true;

            if (!ClickUi(button))
            { return false; }

            clicked = true;
            return true;
        }

        private bool ClickUiDirectNoCompletionCheck(IUiElement element)
        {
            try
            {
                if (element == null) return false;
                element.Refresh();
                if (!element.Visible) return false;
                return ClickRect(element.Rectangle);
            }
            catch
            {
                return false;
            }
        }

        private bool TryClickStartupTab(IUiElement element, out bool inputAttempted)
        {
            inputAttempted = false;
            if (CompleteIfNoLiveSalvageCandidates("pre-tab-click")) return false;
            if (element == null) return false;
            element.Refresh();
            if (!element.Visible) return false;
            var rect = element.Rectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            inputAttempted = true;
            return ClickRect(rect);
        }

        private bool ClickUi(IUiElement element)
        {
            if (CompleteIfNoLiveSalvageCandidates("pre-ui-click")) return false;
            if (element == null) return false;
            element.Refresh();
            if (!element.Visible) return false;
            return ClickRect(element.Rectangle);
        }

        private bool ClickInventoryItemTurbo(IItem item)
        {
            if (item == null) return false;

            var rect = Hud.Inventory.GetItemRect(item);
            if (rect.Width <= 0 || rect.Height <= 0)
                return false;

            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);

            if (!MoveCursorClient(x, y)) return false;

            bool clicked = SendMouseClickPair();

            return clicked;
        }

        private bool ClickRect(RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);
            if (!MoveCursorClient(x, y)) return false;
            bool sent = SendMouseClickPair();

            return sent;
        }

        private bool MoveCursorClient(int x, int y)
        {
            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
            { return false; }
            var size = Hud.Window.Size;
            if (x < 0 || y < 0 || x >= size.Width || y >= size.Height)
            { return false; }

            // Desktop coordinates may be negative on secondary monitors.
            var offset = Hud.Window.Offset;
            long screenX = (long)offset.X + x;
            long screenY = (long)offset.Y + y;
            if (screenX < int.MinValue || screenX > int.MaxValue ||
                screenY < int.MinValue || screenY > int.MaxValue) return false;
            bool moved = SetCursorPos((int)screenX, (int)screenY) && Hud.Window.IsForeground;

            return moved;
        }

        private static bool PressEnter()
        {
            bool down = SendKey(VkEnter, false);
            bool up = SendKey(VkEnter, true);
            return down && up;
        }

        private static void PressEscape()
        {
            SendKey(VkEscape, false);
            SendKey(VkEscape, true);
        }

        private static bool SendMouseClickPair()
        {
            var inputs = new Input[2];
            inputs[0].Type = InputMouse;
            inputs[0].U.Mouse.Flags = LeftDown;
            inputs[1].Type = InputMouse;
            inputs[1].U.Mouse.Flags = LeftUp;
            uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
            if (sent == 1) SendMouse(LeftUp);
            return sent == 2;
        }

        private static bool SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;
            return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
        }

        private static bool SendKey(ushort virtualKey, bool keyUp)
        {
            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;
            return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

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

            [FieldOffset(0)]
            public KeyboardInput Keyboard;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }
    }
}

