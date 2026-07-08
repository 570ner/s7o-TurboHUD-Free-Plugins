using System;
using System.Collections.Generic;
using System.Drawing;
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

        // Persist only user-adjusted UI preferences: SalvageSpeed, SalvageHotkey, and one DebugLogging toggle.
        // Safety filters, timing arrays, and layout settings remain script-editable defaults.
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

        // Default speed if no saved settings file exists.
        // UI changes are saved to plugins/s7o/settings/s7o_ItemSalvage.ini when PersistUserSettings is true.
        // All speeds use turbo mode. Speed 1 is already very fast; speed 10 is near-burst.
        public int SalvageSpeed = 8;
        public int MaxItemClickRetries = 1;
        public int DebounceMs = 150;

        // After the final salvage click, park the cursor at the player's feet once, then release control.
        public bool ParkCursorAtPlayerFeetAfterFinalSalvage = true;

        // Stable repair/safety additions.
        // Repair is checked once per F3 run before salvage. If no repair is needed, no repair click is sent.
        public bool AutoRepair = true;
        public int RepairTabDelayMs = 100;
        public int RepairClickDelayMs = 100;
        public int SalvageTabDelayMs = 100;

        // Wait only when the plugin actually toggles the salvage/anvil button on.
        // This mirrors LightningMOD's UI-transition settling without slowing successful per-item clicks.
        public int AnvilEnableReadyDelayMs = 100;

        // Failure fallback: do not hammer a greyed/stuck item for the whole run.
        // 2 = initial click + one retry, then that ItemUniqueId is skipped until the user presses F3 again.
        public int MaxTotalTurboClicksPerItem = 2;
        public int TurboRetryBackoffDelayMs = 35;

        // All speeds use turbo mode. Speed 1 is fast; speed 10 is very fast but still paced.
        public bool UseTurboMode = true;
        public bool UseStrictModeForSpeed1 = false;
        public int PacedTurboMinimumSpeed = 2;
        public int TurboMaxRetryPasses = 1;

        // If true, turbo mode will press Enter only when the salvage confirmation OK button is visible.
        // It must never send blind Enter, because blind Enter can open chat.
        public bool TurboImmediateLegendaryEnter = true;

        // Turbo legendary confirmation stability. After a legendary click, wait briefly for
        // the OK dialog before clicking another item; never send blind Enter.
        public int TurboLegendaryConfirmWindowMs = 180;
        public int TurboLegendaryConfirmPollMs = 10;
        public int TurboPostConfirmSettleMs = 20;

        // Confirmation-first retry throttle. Keep this close to TurboLegendaryConfirmPollMs so
        // safety does not turn turbo legendary salvage into strict per-item verification.
        public int ConfirmRetryThrottleMs = 15;

        // Chat safety: confirmation Enter can occasionally be observed again while the OK dialog is
        // fading, so allow only one Enter per visible confirmation before using a click fallback.
        public int ConfirmEnterRepeatDelayMs = 90;
        public int ConfirmClickFallbackDelayMs = 140;
        public bool CloseChatIfOpenedDuringSalvage = true;
        public int ChatCloseSettleMs = 90;

        // If a clicked item is still present, give Diablo III a short stale/pending cooldown
        // before retrying it. This avoids hammering greyed items and preserves cleanup retries.
        public int FailedItemRetryCooldownMs = 200;

        public bool EnableTurboFinalCleanup = true;

        // Run final validation cleanup for all speeds.
        // The main pass remains fast; this only adds a delayed verification pass after the run.
        public int TurboFinalCleanupMinimumSpeed = 1;

        // Allow multiple cleanup passes because speed 10 can leave delayed inventory leftovers.
        public int TurboFinalCleanupMaxPasses = 3;

        // This is the key timing. 80ms can be too short for FREE inventory collection after speed-10 clicks.
        public int TurboFinalCleanupValidationDelayMs = 120;

        // Cleanup remains fast, but slightly more tolerant than the main speed-10 pass.
        public int TurboFinalCleanupClickDelayMs = 25;
        public int TurboFinalCleanupSettleDelayMs = 100;

        // If a cleanup rescan sees zero candidates, verify a few more times before ending.
        // This catches delayed inventory snapshots.
        public int TurboFinalCleanupEmptyValidationRetries = 2;

        // Cleanup retry passes should be more tolerant than the main turbo retry pass.
        public int TurboFinalCleanupRetryPasses = 2;

        // Blacksmith pane detection. This keeps the overlay off Mystic/Jeweler/vendor panes.
        public bool UseBlacksmithTextDetection = true;
        public bool UseSelectedBlacksmithActorFallback = true;
        public bool StickyBlacksmithPaneUntilClosed = true;
        public int BlacksmithContextRefreshMs = 250;

        // Release default: debug logging is off.
        // To troubleshoot, set DebugLogging=true in plugins/s7o/settings/s7o_ItemSalvage.ini.
        // That single INI switch enables/disables all detailed debug flags below.
        // Logs are written to plugins/s7o/s7o_ItemSalvage.debug.log.
        public bool DebugLogging = false;
        public bool DebugTurboTimings = false;
        public bool DebugCandidateReasons = false;
        public bool DebugSocketStats = false;
        public bool DebugBlacksmithContext = false;
        public bool DebugVendorPaneText = false;

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

        public float SpeedControlWidth = 112.0f;
        public float SpeedControlHeight = 20.0f;
        public float SpeedSideButtonWidth = 28.0f;

        public int ButtonFlashMs = 90;
        public int MaxDebugLogBytes = 1024 * 1024;

        // ============================================================
        // Speed tuning:
        // Speed 1 uses strict one-by-one verification.
        // Speeds 2-10 use paced turbo mode.
        // TurboClickDelayBySpeed is the main visible per-item pacing value.
        // TurboSettleDelayBySpeed controls how long the plugin waits after a paced turbo pass before verifying results.
        // ItemTimeoutBySpeed is a failure threshold, not normal pacing; setting it too low can cause false retries.
        // ============================================================

        public int[] StepDelayBySpeed = new int[]
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        public int[] ConfirmPollDelayBySpeed = new int[]
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        public int[] ConfirmWindowBySpeed = new int[]
        {
            0,
            60, // 1
            54, // 2
            48, // 3
            42, // 4
            36, // 5
            30, // 6
            24, // 7
            18, // 8
            12, // 9
            8   // 10
        };

        public int[] ItemTimeoutBySpeed = new int[]
        {
            0,
            200, // 1
            190, // 2
            180, // 3
            170, // 4
            160, // 5
            150, // 6
            140, // 7
            130, // 8
            120, // 9
            110  // 10
        };

        public int[] TurboClickDelayBySpeed = new int[]
        {
            0,
            100, // 1
            90,  // 2
            80,  // 3
            70,  // 4
            60,  // 5
            50,  // 6
            40,  // 7
            30,  // 8
            20,  // 9
            15   // 10
        };

        public int[] TurboClicksPerPassBySpeed = new int[]
        {
            0,
            1, // 1
            1, // 2
            1, // 3
            1, // 4
            1, // 5
            1, // 6
            1, // 7
            1, // 8
            1, // 9
            1  // 10
        };

        public int[] TurboSettleDelayBySpeed = new int[]
        {
            0,
            100, // 1
            90,  // 2
            80,  // 3
            70,  // 4
            60,  // 5
            50,  // 6
            40,  // 7
            30,  // 8
            20,  // 9
            10   // 10
        };

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
        private IBrush _pillOrangeSeparatorBrush;
        private IFont _armoryMarkerFont;
        private IFont _salvageProtectionMarkerFont;

        private RectangleF _speedMinusRect = RectangleF.Empty;
        private RectangleF _speedPlusRect = RectangleF.Empty;
        private RectangleF _speedControlRect = RectangleF.Empty;
        private RectangleF _speedValueRect = RectangleF.Empty;
        private RectangleF _hotkeyButtonRect = RectangleF.Empty;
        private bool _overlayControlsVisible;
        private bool _capturingHotkey;
        private const int NoTick = int.MinValue;
        private int _minusFlashUntilTick = NoTick;
        private int _plusFlashUntilTick = NoTick;

        private State _state;
        private bool _cancelRequested;
        private bool _salvageTabClickSent;
        private bool _repairCheckedThisRun;
        private bool _repairTabClickSent;
        private bool _repairClickedThisRun;
        private long _runRepairCost;
        private bool _anvilEnableClickSent;
        private bool _runSummaryLogged;
        private string _runEndReason;

        private int _lastHotkeyTick;
        private int _nextStepTick;
        private int _itemStartTick;
        private int _confirmStartTick;
        private int _originalCursorX;
        private int _originalCursorY;
        private int _activeRetryCount;
        private int _runCandidateCount;
        private int _runClickedCount;
        private int _runGoneCount;
        private int _runRetryCount;
        private int _runTimeoutSkipCount;
        private int _runResolveSkipCount;
        private int _runAttemptCapSkipCount;

        private readonly List<string> _turboClickedKeys = new List<string>();
        private readonly HashSet<string> _turboGoneKeys = new HashSet<string>();
        private readonly Dictionary<string, int> _turboItemClickAttempts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _turboItemNextRetryTick = new Dictionary<string, int>();
        private readonly HashSet<string> _turboItemKeysSkippedForRun = new HashSet<string>();
        private int _turboRetryPass;
        private bool _turboFinalCleanupActive;
        private int _turboFinalCleanupPass;
        private int _runCleanupCandidateCount;
        private int _turboFinalCleanupEmptyValidationCount;

        private int _lastItemClickTick;
        private int _lastConfirmTick;
        private int _lastGoneTick;
        private int _confirmVisibleSinceTick = NoTick;
        private int _lastConfirmPressTick = NoTick;
        private int _confirmPressAttempts;

        private bool _cachedBlacksmithPaneVisible;
        private bool _stickyBlacksmithPaneVisible;
        private int _nextBlacksmithContextRefreshTick = NoTick;
        private int _lastBlacksmithActorSeenTick;
        private string _lastVendorContextSignature;

        private bool _paintExceptionLogged;
        private bool _geometryDrawFailed;
        private bool _geometryDrawFailureLogged;

        private string _debugLogPath;
        private string _settingsPath;
        private string _legacySettingsPath;

        private readonly Queue<string> _pendingItemKeys = new Queue<string>();
        private string _activeItemKey;
        private bool _parkCursorOnCompletionPending;
        private bool _finalCursorParked;

        private enum State
        {
            Idle,
            Prepare,
            OpenRepairTab,
            RepairIfNeeded,
            OpenSalvageTab,
            EnableAnvil,
            WaitAfterAnvilEnable,
            ClickItem,
            ConfirmIfNeeded,
            WaitForItemGone,
            TurboClickItem,
            TurboAwaitLegendaryConfirm,
            TurboSettle,
            TurboFinalCleanupValidate,
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
            _pillOrangeSeparatorBrush = Hud.Render.CreateBrush(190, 120, 65, 15, 1);

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

            LogDebug("s7o_ItemSalvage loaded.");
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            LogDebug("New area detected. Resetting Item Salvage runtime state.");
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
                    LogDebug("Hotkey capture cancelled.");
                    return;
                }

                SalvageHotkey = keyEvent.Key;
                _salvageKeyEvent = Hud.Input.CreateKeyEvent(true, SalvageHotkey, false, false, false);
                _capturingHotkey = false;
                SaveUserSettings();
                LogDebug("Salvage hotkey changed to " + SalvageHotkey);
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
                LogDebug("Item Salvage cancel requested by hotkey.");
                return;
            }

            if (!IsBlacksmithPaneVisible())
            {
                LogDebug("Salvage hotkey ignored: blacksmith pane is not active.");
                return;
            }

            if (IsChatEntryOpen())
            {
                LogDebug("Salvage hotkey ignored: chat entry is active.");
                return;
            }

            QueueRun();
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            UpdateBlacksmithActorLatch(now);

            if (_state == State.Idle) return;

            if (_cancelRequested)
            {
                CancelRun(!ShouldRestoreCursorOnCancel(), true);
                return;
            }

            if (!IsValidContext())
            {
                _runEndReason = "Item Salvage cancelled: invalid context";
                CancelRun(!ShouldRestoreCursorOnCancel(), true);
                return;
            }

            if (TryCloseChatEntryDuringRun(now, "after collect"))
                return;

            // Confirmation popups are authoritative: do not complete/cancel/park/click another item while OK is visible.
            if (HandleVisibleSalvageConfirmFirst(now, "after collect"))
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
                        _nextStepTick = now + GetStepDelay();
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
                        _nextStepTick = now + GetStepDelay();
                        return;
                    }

                    _state = State.EnableAnvil;
                    _nextStepTick = now + GetStepDelay();
                    return;

                case State.OpenRepairTab:
                    if (IsRepairDialogVisible())
                    {
                        _state = State.RepairIfNeeded;
                        _nextStepTick = now + GetStepDelay();
                        return;
                    }

                    if (!_repairTabClickSent && ClickUi(_repairTab))
                    {
                        _repairTabClickSent = true;
                        LogDebug("Repair tab clicked once.");
                        _nextStepTick = now + Math.Max(0, RepairTabDelayMs);
                        return;
                    }

                    // Do not block salvage forever if the repair tab/page is unavailable.
                    _repairCheckedThisRun = true;
                    LogDebug("AutoRepair skipped: repair tab/page not available.");
                    _state = State.OpenSalvageTab;
                    _salvageTabClickSent = false;
                    _nextStepTick = now + GetStepDelay();
                    return;

                case State.RepairIfNeeded:
                    _repairCheckedThisRun = true;

                    long repairCost;
                    if (TryGetRepairCost(out repairCost))
                    {
                        _runRepairCost = repairCost;

                        if (repairCost > 0 && CanAffordRepair(repairCost))
                        {
                            if (ClickUi(_repairCostButton))
                            {
                                _repairClickedThisRun = true;
                                LogDebug("AutoRepair clicked. cost=" + repairCost + ".");
                                _state = State.OpenSalvageTab;
                                _salvageTabClickSent = false;
                                _nextStepTick = now + Math.Max(0, RepairClickDelayMs);
                                return;
                            }

                            LogDebug("AutoRepair skipped: repair button click failed. cost=" + repairCost + ".");
                        }
                        else
                        {
                            LogDebug("AutoRepair not needed or not affordable. cost=" + repairCost + ".");
                        }
                    }
                    else
                    {
                        LogDebug("AutoRepair skipped: repair cost could not be read.");
                    }

                    _state = State.OpenSalvageTab;
                    _salvageTabClickSent = false;
                    _nextStepTick = now + GetStepDelay();
                    return;

                case State.OpenSalvageTab:
                    if (IsSalvageDialogVisible())
                    {
                        _state = State.Prepare;
                        _nextStepTick = now + GetStepDelay();
                        return;
                    }

                    if (!_salvageTabClickSent && ClickUi(_salvageTab))
                    {
                        _salvageTabClickSent = true;
                        LogDebug("Salvage tab clicked once.");
                        _nextStepTick = now + Math.Max(0, SalvageTabDelayMs);
                        return;
                    }

                    _runEndReason = "Item Salvage cancelled: salvage tab/page not available";
                    CancelRun(true, true);
                    return;

                case State.EnableAnvil:
                    bool anvilClicked;
                    if (!SetAnvil(true, out anvilClicked))
                    {
                        _runEndReason = "Item Salvage cancelled: could not enable anvil";
                        CancelRun(true, true);
                        return;
                    }

                    _anvilEnableClickSent = anvilClicked;
                    _state = State.WaitAfterAnvilEnable;
                    _nextStepTick = now + (anvilClicked ? Math.Max(0, AnvilEnableReadyDelayMs) : GetStepDelay());
                    return;

                case State.WaitAfterAnvilEnable:
                    if (!IsAnvilEnabled(GetVisibleAnvilButton()))
                    {
                        _state = State.EnableAnvil;
                        _nextStepTick = now + 5;
                        return;
                    }

                    _state = IsTurboMode() ? State.TurboClickItem : State.ClickItem;
                    _nextStepTick = now + GetStepDelay();
                    return;

                case State.ClickItem:
                    ProcessStrictClickItem(now);
                    return;

                case State.ConfirmIfNeeded:
                    ProcessStrictConfirmIfNeeded(now);
                    return;

                case State.WaitForItemGone:
                    ProcessStrictWaitForItemGone(now);
                    return;

                case State.TurboClickItem:
                    ProcessTurboClickItem(now);
                    return;

                case State.TurboAwaitLegendaryConfirm:
                    ProcessTurboAwaitLegendaryConfirm(now);
                    return;

                case State.TurboSettle:
                    ProcessTurboSettle(now);
                    return;

                case State.TurboFinalCleanupValidate:
                    ProcessTurboFinalCleanupValidate(now);
                    return;

                case State.Done:
                    LogRunSummary(string.IsNullOrEmpty(_runEndReason) ? "Item Salvage completed" : _runEndReason);
                    CancelRun(false, false);
                    return;
            }
        }

        private void ProcessStrictClickItem(int now)
        {
            _activeItemKey = null;
            _activeRetryCount = 0;

            while (_pendingItemKeys.Count > 0)
            {
                string key = _pendingItemKeys.Dequeue();
                IItem item = ResolveCandidate(key);

                if (item == null)
                {
                    _runResolveSkipCount++;
                    continue;
                }

                if (!TryRegisterItemClickAttempt(key, item))
                    continue;

                _activeItemKey = key;
                if (!ClickInventoryItem(item))
                {
                    _runResolveSkipCount++;
                    _activeItemKey = null;
                    continue;
                }

                if (_pendingItemKeys.Count == 0)
                    MarkFinalItemClickedForCompletionPark();

                _lastItemClickTick = now;
                _runClickedCount++;
                _confirmStartTick = now;
                _itemStartTick = 0;

                LogDebug("Strict click. item=" + SafeItemName(item) + ", speed=" + GetSpeed() + ", tick=" + now + ".");

                _state = State.ConfirmIfNeeded;
                _nextStepTick = now + GetConfirmPollDelay();
                return;
            }

            _runEndReason = "Item Salvage completed";
            CancelRun(false, true);
        }

        private void ProcessStrictConfirmIfNeeded(int now)
        {
            if (string.IsNullOrEmpty(_activeItemKey) || !InventoryContainsKey(_activeItemKey))
            {
                _lastGoneTick = now;
                _runGoneCount++;
                LogDebug("Item gone observed before confirmation. clickToGone=" + (now - _lastItemClickTick) + "ms.");
                _activeItemKey = null;
                _activeRetryCount = 0;
                if (_pendingItemKeys.Count == 0)
                {
                    CompleteRunWithOptionalCursorPark("Item Salvage completed");
                    return;
                }
                _state = State.ClickItem;
                _nextStepTick = now + GetStepDelay();
                return;
            }

            if (TryConfirmSalvageDialogWithEnter(now, "strict confirm"))
            {
                _itemStartTick = now;
                _state = State.WaitForItemGone;
                _nextStepTick = now + GetStepDelay();
                return;
            }

            if ((uint)(now - _confirmStartTick) < (uint)GetConfirmWindow())
            {
                _nextStepTick = now + GetConfirmPollDelay();
                return;
            }

            _itemStartTick = now;
            _state = State.WaitForItemGone;
            _nextStepTick = now + GetStepDelay();
        }

        private void ProcessStrictWaitForItemGone(int now)
        {
            if (string.IsNullOrEmpty(_activeItemKey) || !InventoryContainsKey(_activeItemKey))
            {
                _lastGoneTick = now;
                _runGoneCount++;
                LogDebug("Item gone observed. confirmToGone=" + (now - _lastConfirmTick) + "ms, clickToGone=" + (now - _lastItemClickTick) + "ms.");
                _activeItemKey = null;
                _activeRetryCount = 0;
                if (_pendingItemKeys.Count == 0)
                {
                    CompleteRunWithOptionalCursorPark("Item Salvage completed");
                    return;
                }
                _state = State.ClickItem;
                _nextStepTick = now + GetStepDelay();
                return;
            }

            if ((uint)(now - _itemStartTick) >= (uint)GetItemTimeout())
            {
                IItem retryItem = ResolveCandidate(_activeItemKey);
                if (retryItem != null && _activeRetryCount < Math.Max(0, MaxItemClickRetries))
                {
                    _activeRetryCount++;
                    _runRetryCount++;
                    LogDebug("Timeout waiting for item gone; retry "
                        + _activeRetryCount
                        + "/"
                        + MaxItemClickRetries
                        + ". Speed="
                        + GetSpeed()
                        + ", timeout="
                        + GetItemTimeout()
                        + "ms, item="
                        + SafeItemName(retryItem));

                    if (!TryRegisterItemClickAttempt(_activeItemKey, retryItem))
                    {
                        _activeItemKey = null;
                        _activeRetryCount = 0;
                        _state = State.ClickItem;
                        _nextStepTick = now + GetStepDelay();
                        return;
                    }

                    if (ClickInventoryItem(retryItem))
                    {
                        _lastItemClickTick = now;
                        _runClickedCount++;
                        _confirmStartTick = now;
                        _itemStartTick = 0;
                        _state = State.ConfirmIfNeeded;
                        _nextStepTick = now + GetConfirmPollDelay();
                        return;
                    }
                }

                _runTimeoutSkipCount++;
                LogDebug("Timeout skip. Speed="
                    + GetSpeed()
                    + ", timeout="
                    + GetItemTimeout()
                    + "ms, activeKey="
                    + (_activeItemKey ?? string.Empty));

                _activeItemKey = null;
                _activeRetryCount = 0;
                _state = State.ClickItem;
                _nextStepTick = now + GetStepDelay();
                return;
            }

            _nextStepTick = now + GetStepDelay();
        }

                private bool IsActiveSalvageMousePhase()
        {
            return _state == State.EnableAnvil
                || _state == State.WaitAfterAnvilEnable
                || _state == State.ClickItem
                || _state == State.ConfirmIfNeeded
                || _state == State.WaitForItemGone
                || _state == State.TurboClickItem
                || _state == State.TurboAwaitLegendaryConfirm
                || _state == State.TurboSettle
                || _state == State.TurboFinalCleanupValidate;
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
                if (IsItemKeySkippedForThisRun(key)) continue;

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

            LogDebug("No live salvage candidates remain; completing safely. source=" + source + ".");
            return CompleteRunWithOptionalCursorPark("Item Salvage completed");
        }

        private bool ShouldRestoreCursorOnCancel()
        {
            if (IsActiveSalvageMousePhase() && !HasLiveSalvageCandidates())
                return false;

            return true;
        }

        private bool TryConfirmSalvageDialogWithEnter(int now, string source)
        {
            if (!IsSalvageConfirmVisible()) return false;

            if (TryCloseChatEntryDuringRun(now, "confirm " + source))
                return true;

            int enterRepeatDelayMs = Math.Max(Math.Max(10, ConfirmRetryThrottleMs), Math.Max(10, ConfirmEnterRepeatDelayMs));
            if (!ElapsedAtLeast(now, _lastConfirmPressTick, enterRepeatDelayMs))
                return true;

            if (!TickSet(_confirmVisibleSinceTick))
                _confirmVisibleSinceTick = now;

            _confirmPressAttempts++;
            _lastConfirmPressTick = now;
            _lastConfirmTick = now;

            PressEnter();

            LogDebug("Confirmation accepted with Enter. source="
                + source
                + ", attempt="
                + _confirmPressAttempts
                + ", tick="
                + now
                + (TickSet(_lastItemClickTick) ? ", clickToConfirm=" + unchecked(now - _lastItemClickTick) + "ms" : string.Empty)
                + ".");
            return true;
        }

        private bool IsSalvageConfirmVisible()
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

            PressEscape();
            _nextStepTick = unchecked(now + Math.Max(0, ChatCloseSettleMs));
            LogDebug("Chat entry detected during salvage; Escape sent. source=" + source + ".");
            return true;
        }

        private bool IsConfirmationSensitiveState()
        {
            return _state == State.ConfirmIfNeeded
                || _state == State.WaitForItemGone
                || _state == State.TurboAwaitLegendaryConfirm
                || _state == State.TurboSettle
                || _state == State.TurboFinalCleanupValidate
                || !string.IsNullOrEmpty(_activeItemKey)
                || _turboClickedKeys.Count > 0
                || TickSet(_confirmStartTick)
                || TickSet(_lastItemClickTick);
        }

        private bool HandleVisibleSalvageConfirmFirst(int now, string source)
        {
            if (!IsConfirmationSensitiveState())
                return false;

            if (!IsSalvageConfirmVisible())
            {
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

            bool turboAwait = _state == State.TurboAwaitLegendaryConfirm;

            _confirmPressAttempts++;
            _lastConfirmPressTick = now;
            _lastConfirmTick = now;

            if (_confirmPressAttempts == 1)
            {
                PressEnter();
                LogDebug("Confirmation visible; pressed Enter. source="
                    + source
                    + ", attempt="
                    + _confirmPressAttempts
                    + ".");
            }
            else
            {
                ClickUiDirectNoCompletionCheck(_okButton);
                LogDebug("Confirmation visible; clicked OK fallback. source="
                    + source
                    + ", attempt="
                    + _confirmPressAttempts
                    + ".");
            }

            if (turboAwait)
            {
                _activeItemKey = null;
                ScheduleNextTurboStepAfterItem(
                    now,
                    Math.Max(GetActiveTurboClickDelay(), Math.Max(20, TurboPostConfirmSettleMs)));
                return true;
            }

            _nextStepTick = unchecked(now + (_confirmPressAttempts == 1 ? Math.Max(20, TurboPostConfirmSettleMs) : fallbackDelayMs));
            return true;
        }

        private bool HasUnresolvedSalvageWorkForEarlyCompletion(int now)
        {
            if (HasUnresolvedClickedOrConfirmWork(now)) return true;

            if (_state == State.ConfirmIfNeeded) return true;
            if (_state == State.WaitForItemGone) return true;
            if (_state == State.TurboAwaitLegendaryConfirm) return true;
            if (_state == State.TurboSettle) return true;
            if (_state == State.TurboFinalCleanupValidate) return true;

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
                if (_turboGoneKeys.Contains(key)) continue;
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

            if (IsSalvageConfirmVisible() || HasUnresolvedClickedOrConfirmWork(now, false))
            {
                _nextStepTick = unchecked(now + Math.Max(1, GetActiveTurboSettleDelay()));
                return false;
            }

            _runEndReason = reason;

            if (_parkCursorOnCompletionPending || ParkCursorAtPlayerFeetAfterFinalSalvage)
                ParkCursorAtPlayerFeetAfterFinalSalvageOnce();

            CancelRun(false, true);
            return true;
        }

        private void ProcessTurboClickItem(int now)
        {
            if (TryConfirmSalvageDialogWithEnter(now, "turbo pre-click"))
            {
                _state = State.TurboClickItem;
                _nextStepTick = now + GetActiveTurboClickDelay();
                return;
            }

            var anvilButton = GetVisibleAnvilButton();
            if (!IsAnvilEnabled(anvilButton))
            {
                _state = State.EnableAnvil;
                _nextStepTick = now;
                return;
            }

            int maxClicks = GetTurboClicksPerPass();
            int clickedThisPass = 0;

            while (_pendingItemKeys.Count > 0 && clickedThisPass < maxClicks)
            {
                string key = _pendingItemKeys.Dequeue();
                IItem item = ResolveCandidate(key);

                if (item == null)
                {
                    _runResolveSkipCount++;
                    continue;
                }

                if (!TryRegisterItemClickAttempt(key, item))
                    continue;

                if (!ClickInventoryItem(item))
                {
                    _runResolveSkipCount++;
                    continue;
                }

                if (_pendingItemKeys.Count == 0)
                    MarkFinalItemClickedForCompletionPark();

                _lastItemClickTick = now;
                _runClickedCount++;
                clickedThisPass++;
                _turboClickedKeys.Add(key);

                if (TurboImmediateLegendaryEnter && item.IsLegendary)
                {
                    _activeItemKey = key;
                    _confirmStartTick = now;

                    if (TryConfirmSalvageDialogWithEnter(now, "turbo post-click"))
                    {
                        _activeItemKey = null;
                        ScheduleNextTurboStepAfterItem(now, Math.Max(GetActiveTurboClickDelay(), Math.Max(0, TurboPostConfirmSettleMs)));
                        return;
                    }

                    if (DebugTurboTimings)
                    {
                        LogDebug("Awaiting legendary confirmation. item="
                            + SafeItemName(item)
                            + ", window="
                            + Math.Max(0, TurboLegendaryConfirmWindowMs)
                            + "ms, poll="
                            + Math.Max(1, TurboLegendaryConfirmPollMs)
                            + "ms.");
                    }

                    _state = State.TurboAwaitLegendaryConfirm;
                    _nextStepTick = now + Math.Max(1, TurboLegendaryConfirmPollMs);
                    return;
                }

                if (DebugTurboTimings)
                {
                    LogDebug("Turbo click. item="
                        + SafeItemName(item)
                        + ", speed="
                        + GetSpeed()
                        + ", turboClickDelay="
                        + GetActiveTurboClickDelay()
                        + "ms, clicksThisPass="
                        + clickedThisPass
                        + "/"
                        + maxClicks
                        + ", remainingQueue="
                        + _pendingItemKeys.Count
                        + ".");
                }
            }

            ScheduleNextTurboStepAfterItem(now, GetActiveTurboClickDelay());
        }

        private void ProcessTurboAwaitLegendaryConfirm(int now)
        {
            if (TryConfirmSalvageDialogWithEnter(now, "turbo legendary await"))
            {
                _activeItemKey = null;
                ScheduleNextTurboStepAfterItem(now, Math.Max(GetActiveTurboClickDelay(), Math.Max(0, TurboPostConfirmSettleMs)));
                return;
            }

            if (!string.IsNullOrEmpty(_activeItemKey) && !InventoryContainsKey(_activeItemKey))
            {
                if (!_turboGoneKeys.Contains(_activeItemKey))
                {
                    _turboGoneKeys.Add(_activeItemKey);
                    _runGoneCount++;
                    _lastGoneTick = now;
                }

                LogDebug("Legendary item gone while awaiting confirmation. clickToGone="
                    + unchecked(now - _lastItemClickTick)
                    + "ms.");

                _activeItemKey = null;
                ScheduleNextTurboStepAfterItem(now, Math.Max(GetActiveTurboClickDelay(), Math.Max(0, TurboPostConfirmSettleMs)));
                return;
            }

            if ((uint)(now - _confirmStartTick) < (uint)Math.Max(0, TurboLegendaryConfirmWindowMs))
            {
                _state = State.TurboAwaitLegendaryConfirm;
                _nextStepTick = now + Math.Max(1, TurboLegendaryConfirmPollMs);
                return;
            }

            if (DebugTurboTimings)
            {
                LogDebug("Legendary confirmation did not appear within window. clickToWindow="
                    + unchecked(now - _lastItemClickTick)
                    + "ms, window="
                    + Math.Max(0, TurboLegendaryConfirmWindowMs)
                    + "ms.");
            }

            _activeItemKey = null;
            ScheduleNextTurboStepAfterItem(now, GetActiveTurboClickDelay());
        }

        private void ScheduleNextTurboStepAfterItem(int now, int nextClickDelay)
        {
            if (_pendingItemKeys.Count > 0)
            {
                _state = State.TurboClickItem;
                _nextStepTick = now + Math.Max(0, nextClickDelay);
                return;
            }

            _state = State.TurboSettle;
            _nextStepTick = now + Math.Max(GetActiveTurboSettleDelay(), Math.Max(0, nextClickDelay));
        }

                private void ProcessTurboSettle(int now)
        {
            if (TryConfirmSalvageDialogWithEnter(now, "turbo settle"))
            {
                _state = State.TurboSettle;
                _nextStepTick = now + GetActiveTurboSettleDelay();
                return;
            }

            int goneThisCheck = 0;
            var remaining = new List<string>();

            foreach (string key in _turboClickedKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (_turboGoneKeys.Contains(key)) continue;

                if (!InventoryContainsKey(key))
                {
                    _turboGoneKeys.Add(key);
                    goneThisCheck++;
                }
                else
                {
                    remaining.Add(key);
                }
            }

            _runGoneCount += goneThisCheck;
            if (goneThisCheck > 0)
                _lastGoneTick = now;

            if (DebugTurboTimings)
            {
                LogDebug("Turbo settle. goneThisCheck="
                    + goneThisCheck
                    + ", totalGone="
                    + _runGoneCount
                    + ", remaining="
                    + remaining.Count
                    + ", retryPass="
                    + _turboRetryPass
                    + ", settleDelay="
                    + GetActiveTurboSettleDelay()
                    + "ms.");
            }

            if (remaining.Count > 0 && _turboRetryPass < GetActiveTurboMaxRetryPasses())
            {
                _turboRetryPass++;
                _runRetryCount += remaining.Count;

                foreach (string key in remaining)
                    SetItemRetryCooldown(key, now, "turbo settle remaining");

                _pendingItemKeys.Clear();
                foreach (string key in remaining)
                    _pendingItemKeys.Enqueue(key);

                _turboClickedKeys.Clear();

                int retryDelay = Math.Max(Math.Max(0, TurboRetryBackoffDelayMs), GetMaxRetryCooldownDelay(remaining, now));

                LogDebug("Turbo retry pass queued. remaining="
                    + remaining.Count
                    + ", retryPass="
                    + _turboRetryPass
                    + "/"
                    + GetActiveTurboMaxRetryPasses()
                    + ", retryDelay="
                    + retryDelay
                    + "ms.");

                _state = State.TurboClickItem;
                _nextStepTick = now + retryDelay;
                return;
            }

            if (remaining.Count > 0)
            {
                foreach (string key in remaining)
                    SetItemRetryCooldown(key, now, "turbo retry limit before cleanup");

                LogDebug("Turbo remaining items after retry limit before final cleanup. remaining="
                    + remaining.Count
                    + ".");
            }

            if (ShouldStartTurboFinalCleanup(remaining.Count))
            {
                StartTurboFinalCleanup(now, remaining.Count > 0 ? "remaining after settle" : "final validation");
                return;
            }

            if (remaining.Count > 0)
            {
                _runTimeoutSkipCount += remaining.Count;
                LogDebug("Turbo remaining items after retry/cleanup limit. remaining="
                    + remaining.Count
                    + ". They may be protected, failed clicks, or delayed collection.");
            }

            _turboFinalCleanupActive = false;
            CompleteRunWithOptionalCursorPark("Item Salvage completed");
        }

        private bool ShouldStartTurboFinalCleanup(int remainingCount)
        {
            if (!EnableTurboFinalCleanup) return false;
            if (_turboFinalCleanupPass >= Math.Max(0, TurboFinalCleanupMaxPasses)) return false;

            if (GetSpeed() < Math.Max(1, Math.Min(10, TurboFinalCleanupMinimumSpeed)))
                return false;

            // Only run cleanup when the settle pass still knows items remain.
            // If every clicked item is gone, release control immediately instead of moving the cursor again.
            if (!_turboFinalCleanupActive)
                return remainingCount > 0;

            // After a cleanup pass, only start another cleanup pass if we still know items remain.
            return remainingCount > 0;
        }

        private void StartTurboFinalCleanup(int now, string reason)
        {
            _turboFinalCleanupPass++;
            _turboFinalCleanupActive = true;
            _turboFinalCleanupEmptyValidationCount = 0;

            _pendingItemKeys.Clear();
            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboRetryPass = 0;

            LogDebug("Turbo final cleanup scheduled. pass="
                + _turboFinalCleanupPass
                + "/"
                + TurboFinalCleanupMaxPasses
                + ", reason="
                + reason
                + ", validationDelay="
                + TurboFinalCleanupValidationDelayMs
                + "ms, cleanupClickDelay="
                + TurboFinalCleanupClickDelayMs
                + "ms, cleanupSettle="
                + TurboFinalCleanupSettleDelayMs
                + "ms.");

            _state = State.TurboFinalCleanupValidate;
            _nextStepTick = now + Math.Max(0, TurboFinalCleanupValidationDelayMs);
        }

        private void ProcessTurboFinalCleanupValidate(int now)
        {
            if (TryConfirmSalvageDialogWithEnter(now, "turbo final cleanup validate"))
            {
                _state = State.TurboFinalCleanupValidate;
                _nextStepTick = now + GetActiveTurboSettleDelay();
                return;
            }

            BuildCandidateQueue(true);

            if (_pendingItemKeys.Count == 0)
            {
                if (_turboFinalCleanupEmptyValidationCount < Math.Max(0, TurboFinalCleanupEmptyValidationRetries))
                {
                    _turboFinalCleanupEmptyValidationCount++;

                    LogDebug("Turbo final cleanup empty validation retry "
                        + _turboFinalCleanupEmptyValidationCount
                        + "/"
                        + TurboFinalCleanupEmptyValidationRetries
                        + ". Waiting "
                        + TurboFinalCleanupValidationDelayMs
                        + "ms before rescanning.");

                    _state = State.TurboFinalCleanupValidate;
                    _nextStepTick = now + Math.Max(0, TurboFinalCleanupValidationDelayMs);
                    return;
                }

                LogDebug("Turbo final cleanup found no remaining candidates after validation retries.");
                _turboFinalCleanupActive = false;
                CompleteRunWithOptionalCursorPark("Item Salvage completed");
                return;
            }

            _turboFinalCleanupEmptyValidationCount = 0;

            LogDebug("Turbo final cleanup starting. remainingCandidates="
                + _pendingItemKeys.Count
                + ", cleanupPass="
                + _turboFinalCleanupPass
                + ".");

            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboRetryPass = 0;

            _state = State.TurboClickItem;
            _nextStepTick = now + GetActiveTurboClickDelay();
        }

                private void QueueRun()
        {
            _originalCursorX = Hud.Window.CursorX;
            _originalCursorY = Hud.Window.CursorY;
            _activeItemKey = null;
            _parkCursorOnCompletionPending = false;
            _finalCursorParked = false;
            _pendingItemKeys.Clear();
            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboItemClickAttempts.Clear();
            _turboItemNextRetryTick.Clear();
            _turboItemKeysSkippedForRun.Clear();
            _turboRetryPass = 0;
            _turboFinalCleanupActive = false;
            _turboFinalCleanupPass = 0;
            _runCleanupCandidateCount = 0;
            _turboFinalCleanupEmptyValidationCount = 0;
            _activeRetryCount = 0;
            _runCandidateCount = 0;
            _runClickedCount = 0;
            _runGoneCount = 0;
            _runRetryCount = 0;
            _runTimeoutSkipCount = 0;
            _runResolveSkipCount = 0;
            _runAttemptCapSkipCount = 0;
            _repairCheckedThisRun = false;
            _repairTabClickSent = false;
            _repairClickedThisRun = false;
            _runRepairCost = 0;
            _anvilEnableClickSent = false;
            _confirmStartTick = 0;
            _itemStartTick = 0;
            _lastItemClickTick = 0;
            _lastConfirmTick = 0;
            _lastGoneTick = 0;
            _confirmVisibleSinceTick = NoTick;
            _lastConfirmPressTick = NoTick;
            _confirmPressAttempts = 0;
            _cancelRequested = false;
            _salvageTabClickSent = false;
            _repairTabClickSent = false;
            _runSummaryLogged = false;
            _runEndReason = null;
            _state = State.Prepare;
            _nextStepTick = Environment.TickCount;

            LogDebug("Item Salvage queued. Speed="
                + GetSpeed()
                + ", mode="
                + GetModeName()
                + ", step="
                + GetStepDelay()
                + "ms, confirmPoll="
                + GetConfirmPollDelay()
                + "ms, confirmWindow="
                + GetConfirmWindow()
                + "ms, timeout="
                + GetItemTimeout()
                + "ms, turboClickDelay="
                + GetTurboClickDelay()
                + "ms, turboClicksPerPass="
                + GetTurboClicksPerPass()
                + ", turboSettle="
                + GetTurboSettleDelay()
                + "ms, autoRepair="
                + AutoRepair
                + ", anvilReadyDelay="
                + AnvilEnableReadyDelayMs
                + "ms, maxTotalItemClicks="
                + Math.Max(1, MaxTotalTurboClicksPerItem)
                + ", legendaryConfirmWindow="
                + Math.Max(0, TurboLegendaryConfirmWindowMs)
                + "ms, legendaryConfirmPoll="
                + Math.Max(1, TurboLegendaryConfirmPollMs)
                + "ms, postConfirmSettle="
                + Math.Max(0, TurboPostConfirmSettleMs)
                + "ms, confirmRetryThrottle="
                + Math.Max(10, ConfirmRetryThrottleMs)
                + "ms, failedItemCooldown="
                + Math.Max(0, FailedItemRetryCooldownMs)
                + "ms.");
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
                SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                _finalCursorParked = true;
            }
            catch
            {
            }
        }

                private void CancelRun(bool restoreCursor, bool logSummary)
        {
            if (logSummary)
                LogRunSummary(string.IsNullOrEmpty(_runEndReason) ? "Item Salvage cancelled" : _runEndReason);

            _pendingItemKeys.Clear();
            _turboClickedKeys.Clear();
            _turboGoneKeys.Clear();
            _turboItemClickAttempts.Clear();
            _turboItemNextRetryTick.Clear();
            _turboItemKeysSkippedForRun.Clear();
            _activeItemKey = null;
            _parkCursorOnCompletionPending = false;
            _finalCursorParked = false;
            _turboFinalCleanupActive = false;
            _turboFinalCleanupPass = 0;
            _turboFinalCleanupEmptyValidationCount = 0;
            _activeRetryCount = 0;
            _confirmStartTick = 0;
            _itemStartTick = 0;
            _lastItemClickTick = 0;
            _lastConfirmTick = 0;
            _lastGoneTick = 0;
            _confirmVisibleSinceTick = NoTick;
            _lastConfirmPressTick = NoTick;
            _confirmPressAttempts = 0;
            _cancelRequested = false;
            _salvageTabClickSent = false;
            _repairCheckedThisRun = false;
            _repairTabClickSent = false;
            _repairClickedThisRun = false;
            _runRepairCost = 0;
            _anvilEnableClickSent = false;

            if (restoreCursor)
                SetCursorPos(_originalCursorX, _originalCursorY);

            _state = State.Idle;
            _nextStepTick = 0;
        }

                private void LogRunSummary(string reason)
        {
            if (_runSummaryLogged) return;
            _runSummaryLogged = true;

            LogDebug(reason
                + ". candidates=" + _runCandidateCount
                + ", clicked=" + _runClickedCount
                + ", gone=" + _runGoneCount
                + ", retries=" + _runRetryCount
                + ", timeoutSkips=" + _runTimeoutSkipCount
                + ", resolveSkips=" + _runResolveSkipCount
                + ", attemptCapSkips=" + _runAttemptCapSkipCount
                + ", repairChecked=" + _repairCheckedThisRun
                + ", repairClicked=" + _repairClickedThisRun
                + ", repairCost=" + _runRepairCost
                + ", anvilClicked=" + _anvilEnableClickSent
                + ", cleanupPasses=" + _turboFinalCleanupPass
                + ", cleanupCandidates=" + _runCleanupCandidateCount
                + ", cleanupEmptyValidations=" + _turboFinalCleanupEmptyValidationCount
                + ", speed=" + GetSpeed()
                + ", mode=" + GetModeName()
                + ", step=" + GetStepDelay()
                + "ms, confirmPoll=" + GetConfirmPollDelay()
                + "ms, confirmWindow=" + GetConfirmWindow()
                + "ms, timeout=" + GetItemTimeout()
                + "ms, turboClickDelay=" + GetTurboClickDelay()
                + "ms, turboClicksPerPass=" + GetTurboClicksPerPass()
                + ", turboSettle=" + GetTurboSettleDelay()
                + "ms.");
        }

        private int GetSpeed()
        {
            return Math.Max(1, Math.Min(10, SalvageSpeed));
        }

        private int GetSpeedProfileValue(int[] values, int fallback)
        {
            int speed = GetSpeed();
            if (values == null || values.Length <= speed)
                return Math.Max(0, fallback);
            return Math.Max(0, values[speed]);
        }

        private int GetStepDelay()
        {
            return GetSpeedProfileValue(StepDelayBySpeed, 0);
        }

        private int GetConfirmPollDelay()
        {
            return GetSpeedProfileValue(ConfirmPollDelayBySpeed, 0);
        }

        private int GetConfirmWindow()
        {
            return GetSpeedProfileValue(ConfirmWindowBySpeed, 40);
        }

        private int GetItemTimeout()
        {
            return GetSpeedProfileValue(ItemTimeoutBySpeed, 150);
        }

        private int GetTurboClickDelay()
        {
            return GetSpeedProfileValue(TurboClickDelayBySpeed, 85);
        }

        private int GetTurboClicksPerPass()
        {
            return Math.Max(1, GetSpeedProfileValue(TurboClicksPerPassBySpeed, 1));
        }

        private int GetTurboSettleDelay()
        {
            return GetSpeedProfileValue(TurboSettleDelayBySpeed, 100);
        }

        private int GetActiveTurboClickDelay()
        {
            if (_turboFinalCleanupActive)
                return Math.Max(0, TurboFinalCleanupClickDelayMs);

            return GetTurboClickDelay();
        }

        private int GetActiveTurboSettleDelay()
        {
            if (_turboFinalCleanupActive)
                return Math.Max(0, TurboFinalCleanupSettleDelayMs);

            return GetTurboSettleDelay();
        }

        private int GetActiveTurboMaxRetryPasses()
        {
            if (_turboFinalCleanupActive)
                return Math.Max(0, TurboFinalCleanupRetryPasses);

            return Math.Max(0, TurboMaxRetryPasses);
        }

        private bool IsTurboMode()
        {
            if (!UseTurboMode) return false;

            int speed = GetSpeed();
            return speed >= 1;
        }

        private string GetModeName()
        {
            return IsTurboMode() ? "turbo" : "strict";
        }


        private void InitializePluginPaths()
        {
            var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
            var settingsDir = Path.Combine(pluginDir, "settings");

            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(settingsDir);

            // Preserve existing debug log behavior.
            _debugLogPath = Path.Combine(pluginDir, "s7o_ItemSalvage.debug.log");

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

                    if (EqualsText(key, "SalvageSpeed"))
                    {
                        int speed;
                        if (int.TryParse(value, out speed))
                            SalvageSpeed = Math.Max(1, Math.Min(10, speed));
                    }
                    else if (EqualsText(key, "SalvageHotkey"))
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
                    else if (EqualsText(key, "DebugLogging"))
                    {
                        bool parsed;
                        if (TryParseBoolSetting(value, out parsed))
                            SetAllDebugFlags(parsed);
                    }
                }

                LogDebug("User settings loaded. SalvageSpeed="
                    + GetSpeed()
                    + ", SalvageHotkey="
                    + SalvageHotkey
                    + ".");

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
            catch (Exception ex)
            {
                LogDebug("Failed to load user settings. " + ex);
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
                    + "# This file is written by the plugin when you change the UI speed or hotkey." + Environment.NewLine
                    + "# DebugLogging=true enables all debug logging." + Environment.NewLine
                    + "# DebugLogging=false disables all debug logging." + Environment.NewLine
                    + Environment.NewLine
                    + "SalvageSpeed=" + GetSpeed() + Environment.NewLine
                    + "SalvageHotkey=" + SalvageHotkey + Environment.NewLine
                    + "DebugLogging=" + DebugLogging.ToString().ToLowerInvariant() + Environment.NewLine;

                string dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_settingsPath, content);
                LogDebug("User settings saved. SalvageSpeed=" + GetSpeed() + ", SalvageHotkey=" + SalvageHotkey + ".");
            }
            catch (Exception ex)
            {
                LogDebug("Failed to save user settings. " + ex);
            }
        }

        private static bool TryParseBoolSetting(string value, out bool result)
        {
            result = false;

            if (string.IsNullOrEmpty(value))
                return false;

            value = value.Trim();

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("0", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            return false;
        }

        private void SetAllDebugFlags(bool enabled)
        {
            DebugLogging = enabled;
            DebugTurboTimings = enabled;
            DebugCandidateReasons = enabled;
            DebugSocketStats = enabled;
            DebugBlacksmithContext = enabled;
            DebugVendorPaneText = enabled;
        }

        private void LogDebug(string message)
        {
            if (!DebugLogging) return;

            try
            {
                if (string.IsNullOrEmpty(_debugLogPath))
                {
                    var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
                    Directory.CreateDirectory(pluginDir);
                    _debugLogPath = Path.Combine(pluginDir, "s7o_ItemSalvage.debug.log");
                }

                if (MaxDebugLogBytes > 0 && File.Exists(_debugLogPath))
                {
                    var info = new FileInfo(_debugLogPath);
                    if (info.Length > MaxDebugLogBytes)
                    {
                        File.WriteAllText(
                            _debugLogPath,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            + " Debug log truncated after reaching MaxDebugLogBytes."
                            + Environment.NewLine);
                    }
                }

                File.AppendAllText(
                    _debugLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " "
                    + message
                    + Environment.NewLine);
            }
            catch
            {
                // Debug logging must never break plugin execution.
            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            try
            {
                PaintTopInGameSafe(clipState);
            }
            catch (Exception ex)
            {
                _overlayControlsVisible = false;
                if (!_paintExceptionLogged)
                {
                    _paintExceptionLogged = true;
                    LogDebug("PaintTopInGame exception. Overlay disabled for this frame. " + ex);
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
            {
                DrawHeaderHotkey();
                DrawHeaderSpeedControl();
            }

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

            bool hitMinus = PointInRect(_speedMinusRect, x, y);
            bool hitPlus = PointInRect(_speedPlusRect, x, y);
            bool hitHotkey = PointInRect(_hotkeyButtonRect, x, y);

            if (!hitMinus && !hitPlus && !hitHotkey)
                return false;

            LogDebug("Overlay click. x="
                + x
                + ", y="
                + y
                + ", minus="
                + hitMinus
                + ", plus="
                + hitPlus
                + ", hotkey="
                + hitHotkey);

            if (hitMinus)
            {
                int old = GetSpeed();
                SalvageSpeed = Math.Max(1, old - 1);
                _minusFlashUntilTick = unchecked(now + Math.Max(30, ButtonFlashMs));
                if (GetSpeed() != old)
                    SaveUserSettings();
                LogDebug("Speed minus clicked. old="
                    + old
                    + ", new="
                    + GetSpeed()
                    + ", step="
                    + GetStepDelay()
                    + "ms, confirmPoll="
                    + GetConfirmPollDelay()
                    + "ms, confirmWindow="
                    + GetConfirmWindow()
                    + "ms, timeout="
                    + GetItemTimeout()
                    + "ms, turboClickDelay="
                    + GetTurboClickDelay()
                    + "ms, turboSettle="
                    + GetTurboSettleDelay()
                    + "ms.");
                return true;
            }

            if (hitPlus)
            {
                int old = GetSpeed();
                SalvageSpeed = Math.Min(10, old + 1);
                _plusFlashUntilTick = unchecked(now + Math.Max(30, ButtonFlashMs));
                if (GetSpeed() != old)
                    SaveUserSettings();
                LogDebug("Speed plus clicked. old="
                    + old
                    + ", new="
                    + GetSpeed()
                    + ", step="
                    + GetStepDelay()
                    + "ms, confirmPoll="
                    + GetConfirmPollDelay()
                    + "ms, confirmWindow="
                    + GetConfirmWindow()
                    + "ms, timeout="
                    + GetItemTimeout()
                    + "ms, turboClickDelay="
                    + GetTurboClickDelay()
                    + "ms, turboSettle="
                    + GetTurboSettleDelay()
                    + "ms.");
                return true;
            }

            if (hitHotkey)
            {
                if (_state == State.Idle)
                {
                    _capturingHotkey = true;
                    LogDebug("Hotkey capture started.");
                }
                else
                {
                    LogDebug("Hotkey capture ignored because Item Salvage is running.");
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
            _lastVendorContextSignature = null;

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
                _lastVendorContextSignature = null;
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

            if (DebugVendorPaneText)
                LogBlacksmithContextChange("vendorText=" + combinedText);

            if (IsDefinitelyNonBlacksmithText(combinedText))
            {
                _stickyBlacksmithPaneVisible = false;
                LogBlacksmithContextChange("blacksmith=false, rejected non-blacksmith text, text=" + combinedText);
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

            LogBlacksmithContextChange("blacksmith=false, text=" + combinedText);
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

        private void LogBlacksmithContextChange(string signature)
        {
            if (!DebugBlacksmithContext) return;
            if (string.Equals(signature, _lastVendorContextSignature, StringComparison.Ordinal)) return;
            _lastVendorContextSignature = signature;
            LogDebug("Blacksmith context update. " + signature);
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
            BuildCandidateQueue(false);
        }

        private void BuildCandidateQueue(bool cleanup)
        {
            _pendingItemKeys.Clear();

            var inventoryItems = Hud.Inventory.ItemsInInventory;
            if (inventoryItems == null)
            {
                if (!cleanup)
                    _runCandidateCount = 0;

                return;
            }

            int now = Environment.TickCount;

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
                    int cooldownMs;
                    if (string.IsNullOrEmpty(key))
                    {
                        if (DebugLogging && DebugCandidateReasons)
                            LogDebug("Candidate skipped: reason=empty item key, name=" + SafeItemName(item));
                    }
                    else if (IsItemKeySkippedForThisRun(key))
                    {
                        if (DebugLogging && DebugCandidateReasons)
                            LogDebug("Candidate skipped: reason=attempt cap for this run, name=" + SafeItemName(item));
                    }
                    else if (IsItemRetryCoolingDown(key, now, out cooldownMs))
                    {
                        if (DebugLogging && DebugTurboTimings)
                            LogDebug("Candidate delayed by retry cooldown. cooldownRemaining=" + cooldownMs + "ms, name=" + SafeItemName(item));
                    }
                    else
                    {
                        _pendingItemKeys.Enqueue(key);
                    }
                }
                else if (DebugLogging && DebugCandidateReasons)
                {
                    LogDebug("Candidate skipped: reason="
                        + reason
                        + ", name="
                        + SafeItemName(item)
                        + ", ancientRank="
                        + item.AncientRank
                        + ", caldesann="
                        + item.CaldesannRank
                        + ", enchanted="
                        + item.EnchantedAffixCounter
                        + ", locked="
                        + item.IsInventoryLocked
                        + ", quality="
                        + item.Quality
                        + ", main="
                        + (item.SnoItem == null ? string.Empty : (item.SnoItem.MainGroupCode ?? string.Empty))
                        + ", code="
                        + (item.SnoItem == null ? string.Empty : (item.SnoItem.Code ?? string.Empty))
                        + (DebugSocketStats ? ", " + GetSocketDebugInfo(item) : string.Empty));
                }
            }

            if (cleanup)
            {
                _runCleanupCandidateCount += _pendingItemKeys.Count;
                LogDebug("Turbo final cleanup queue built: " + _pendingItemKeys.Count + " item(s).");
            }
            else
            {
                _runCandidateCount = _pendingItemKeys.Count;
                LogDebug("Candidate queue built: " + _runCandidateCount + " item(s).");
            }
        }

        private bool IsItemKeySkippedForThisRun(string key)
        {
            return !string.IsNullOrEmpty(key) && _turboItemKeysSkippedForRun.Contains(key);
        }

        private int GetActiveMaxTotalTurboClicksPerItem()
        {
            int maxAttempts = Math.Max(1, MaxTotalTurboClicksPerItem);
            if (_turboFinalCleanupActive)
                maxAttempts += Math.Max(0, TurboFinalCleanupRetryPasses);

            return maxAttempts;
        }

        private void SetItemRetryCooldown(string key, int now, string source)
        {
            if (string.IsNullOrEmpty(key)) return;

            int cooldown = Math.Max(0, FailedItemRetryCooldownMs);
            if (cooldown <= 0) return;

            _turboItemNextRetryTick[key] = now + cooldown;

            if (DebugTurboTimings)
            {
                LogDebug("Item retry cooldown set. source="
                    + source
                    + ", cooldown="
                    + cooldown
                    + "ms, key="
                    + key
                    + ".");
            }
        }

        private bool IsItemRetryCoolingDown(string key, int now, out int cooldownRemainingMs)
        {
            cooldownRemainingMs = 0;
            if (string.IsNullOrEmpty(key)) return false;

            int nextTick;
            if (!_turboItemNextRetryTick.TryGetValue(key, out nextTick))
                return false;

            int remaining = unchecked(nextTick - now);
            if (remaining <= 0)
            {
                _turboItemNextRetryTick.Remove(key);
                return false;
            }

            cooldownRemainingMs = remaining;
            return true;
        }

        private int GetMaxRetryCooldownDelay(IEnumerable<string> keys, int now)
        {
            if (keys == null) return 0;

            int maxDelay = 0;
            foreach (string key in keys)
            {
                int cooldownRemainingMs;
                if (IsItemRetryCoolingDown(key, now, out cooldownRemainingMs) && cooldownRemainingMs > maxDelay)
                    maxDelay = cooldownRemainingMs;
            }

            return maxDelay;
        }

        private bool TryRegisterItemClickAttempt(string key, IItem item)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (IsItemKeySkippedForThisRun(key))
                return false;

            int maxAttempts = GetActiveMaxTotalTurboClicksPerItem();

            int current;
            _turboItemClickAttempts.TryGetValue(key, out current);

            if (current >= maxAttempts)
            {
                MarkItemKeySkippedForThisRun(key, current, item);
                return false;
            }

            current++;
            _turboItemClickAttempts[key] = current;

            return true;
        }

        private void MarkItemKeySkippedForThisRun(string key, int attempts, IItem item)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_turboItemKeysSkippedForRun.Contains(key)) return;

            _turboItemKeysSkippedForRun.Add(key);
            _runAttemptCapSkipCount++;

            LogDebug("Item marked skipped for this run after max click attempts. attempts="
                + attempts
                + "/"
                + GetActiveMaxTotalTurboClicksPerItem()
                + ", item="
                + SafeItemName(item)
                + ".");
        }

        private IItem ResolveCandidate(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (IsItemKeySkippedForThisRun(key)) return null;

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

        private static string GetSocketDebugInfo(IItem item)
        {
            if (item == null) return string.Empty;

            int socketedCount = 0;
            if (item.ItemsInSocket != null)
                socketedCount = item.ItemsInSocket.Count(socketedItem => socketedItem != null);

            var result = "socketCount="
                + item.SocketCount
                + ", occupiedSockets="
                + socketedCount
                + ", addedSocketEnhancement="
                + HasAddedSocketEnhancement(item);

            if (item.StatList == null) return result;

            var socketStats = item.StatList
                .Where(s => s != null && s.Attribute != null && !string.IsNullOrEmpty(s.Attribute.Code))
                .Select(s => s.Attribute.Code)
                .Where(code => code.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0
                    || code.IndexOf("AddSockets", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .ToList();

            if (socketStats.Count > 0)
                result += ", socketStats=[" + string.Join("|", socketStats.ToArray()) + "]";

            return result;
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
            _speedMinusRect = RectangleF.Empty;
            _speedPlusRect = RectangleF.Empty;
            _speedControlRect = RectangleF.Empty;
            _speedValueRect = RectangleF.Empty;

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

            float speedX = pane.X + pane.Width - HeaderRightOffset - SpeedControlWidth;
            float speedY = topY + 4.0f;
            float minSpeedX = pane.X + 12.0f;
            float maxSpeedX = pane.X + pane.Width - SpeedControlWidth - 12.0f;
            speedX = Math.Max(minSpeedX, Math.Min(maxSpeedX, speedX));

            float sideWidth = Math.Max(1.0f, SpeedSideButtonWidth);
            float centerWidth = Math.Max(1.0f, SpeedControlWidth - (sideWidth * 2.0f));

            _speedMinusRect = new RectangleF(speedX, speedY, sideWidth, SpeedControlHeight);
            _speedValueRect = new RectangleF(speedX + sideWidth, speedY, centerWidth, SpeedControlHeight);
            _speedPlusRect = new RectangleF(speedX + sideWidth + centerWidth, speedY, sideWidth, SpeedControlHeight);
            _speedControlRect = new RectangleF(speedX, speedY, SpeedControlWidth, SpeedControlHeight);

            return true;
        }

        private void DrawHeaderHotkey()
        {
            if (_yellowFont == null || _vendorPage == null) return;

            var pane = _vendorPage.Rectangle;
            float groupX = pane.X + HeaderLeftOffset;
            float topY = pane.Y + HeaderTopOffset;

            string label = "SALVAGE";
            var layout = _yellowFont.GetTextLayout(label);
            float labelX = groupX + HeaderHotkeyGroupWidth * 0.5f - layout.Metrics.Width * 0.5f;
            _yellowFont.DrawText(layout, labelX, topY);

            DrawPillButton(_hotkeyButtonRect, _capturingHotkey ? "..." : SalvageHotkey.ToString(), _capturingHotkey);
        }

        private void DrawHeaderSpeedControl()
        {
            int now = Environment.TickCount;

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
            DrawCenteredText(_speedValueRect, GetSpeed().ToString());
            DrawCenteredText(_speedPlusRect, "+");
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

        private void DrawSegmentedPillBase(RectangleF rect)
        {
            float radius = rect.Height * 0.5f;
            DrawRoundedRect(rect, radius, _pillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, _pillDarkBrush);

            var highlight = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, inner.Width - 2.0f, inner.Height * 0.42f);
            DrawRoundedRect(highlight, highlight.Height * 0.5f, _pillLightBrush);
        }

        private void DrawPillSegment(RectangleF rect, bool leftRounded, bool rightRounded, bool green)
        {
            if (!green) return;

            var inner = InsetRect(rect, 1.0f);
            float radius = inner.Height * 0.5f;
            DrawRoundedSegment(inner, radius, leftRounded, rightRounded, _pillGreenBrush);

            var highlight = new RectangleF(inner.X + 1.0f, inner.Y + 1.0f, inner.Width - 2.0f, inner.Height * 0.42f);
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
                    LogDebug("Rounded geometry drawing failed. Falling back to rectangles. " + ex);
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
                    LogDebug("Rounded segment drawing failed. Falling back to rectangles. " + ex);
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
            if (button == null) return false;

            if (IsAnvilEnabled(button) == enabled)
                return true;

            if (!ClickUi(button))
                return false;

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

        private bool ClickUi(IUiElement element)
        {
            if (CompleteIfNoLiveSalvageCandidates("pre-ui-click")) return false;
            if (element == null) return false;
            element.Refresh();
            if (!element.Visible) return false;
            return ClickRect(element.Rectangle);
        }

        private bool ClickInventoryItem(IItem item)
        {
            if (CompleteIfNoLiveSalvageCandidates("pre-item-click")) return false;
            if (item == null) return false;
            var rect = Hud.Inventory.GetItemRect(item);
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            return ClickRect(rect);
        }

        private static bool ClickRect(RectangleF rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return false;
            int x = (int)Math.Round(rect.X + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Y + rect.Height * 0.5f);
            if (!SetCursorPos(x, y)) return false;
            SendMouse(LeftDown);
            SendMouse(LeftUp);
            return true;
        }

        private static void PressEnter()
        {
            SendKey(VkEnter, false);
            SendKey(VkEnter, true);
        }

        private static void PressEscape()
        {
            SendKey(VkEscape, false);
            SendKey(VkEscape, true);
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
