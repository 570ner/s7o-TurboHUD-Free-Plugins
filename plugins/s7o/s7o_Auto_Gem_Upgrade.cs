using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public static class s7o_AutoGemUpgradeState
    {
        private const string SettingsFileName = "s7o_AutoGemUpgrade.ini";
        private const string LegacySettingsFileName = "s7o_AutoGemUpgrade.settings.txt";

        public const int AutoGemTPDelayMin = 0;
        public const int AutoGemTPDelayMax = 1500;
        public const int AutoGemTPDelayStep = 100;
        public const int AutoGemTPLagBoostMs = 400;
        public const int AutoGemTPAnchorRemainingMin = 3;
        public const int AutoGemTPAnchorRemainingMax = 4;

        public static bool AutoGemUpgradeEnabled = true;
        public static int AutoGemMode = 0;
        public static string AutoGemSpecificName = "Bane of the Trapped";
        public static int AutoGemSpecificSubMode = 0;
        public static int AutoGemTPDelayMs = 1000;
        public static int AutoGemTPAnchorRemaining = 3;
        public static bool AutoGemTPLagBoost = false;

        public static bool HudMenuOwnsUi = false;
        public static string HudMenuUiOwner = string.Empty;
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
            try { SaveSettingsRequested?.Invoke(); } catch { }
        }

        public static void LoadSettings()
        {
            SaveSettingsRequested = SaveSettings;

            try
            {
                string path = ResolveSettingsLoadPath();
                if (!File.Exists(path))
                    return;

                foreach (string raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
                        continue;

                    int separator = raw.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    string key = raw.Substring(0, separator).Trim();
                    string value = raw.Substring(separator + 1).Trim();
                    int intValue;
                    bool boolValue;

                    if ((key == "AUTOGEM_ENABLED" || key == "AUTOGEM_ON") && bool.TryParse(value, out boolValue))
                        AutoGemUpgradeEnabled = boolValue;
                    else if (key == "AUTOGEM_MODE" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        AutoGemMode = intValue;
                    else if (key == "AUTOGEM_NAME")
                        AutoGemSpecificName = value;
                    else if (key == "AUTOGEM_SPEC_SUBMODE" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        AutoGemSpecificSubMode = intValue;
                    else if (key == "AUTOGEM_TP_DELAY_MS" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        AutoGemTPDelayMs = intValue;
                    else if (key == "AUTOGEM_TP_ANCHOR" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        AutoGemTPAnchorRemaining = intValue;
                    else if (key == "AUTOGEM_TP_LAG" && bool.TryParse(value, out boolValue))
                        AutoGemTPLagBoost = boolValue;
                }
            }
            catch { }

            NormalizeSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                NormalizeSettings();
                string directory = SettingsDirectory();
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(SettingsPath(), new[]
                {
                    "# s7o Auto Gem Upgrade FreeHUD settings",
                    "AUTOGEM_ENABLED=" + AutoGemUpgradeEnabled,
                    "AUTOGEM_ON=" + AutoGemUpgradeEnabled,
                    "AUTOGEM_MODE=" + AutoGemMode.ToString(CultureInfo.InvariantCulture),
                    "AUTOGEM_NAME=" + AutoGemSpecificName,
                    "AUTOGEM_SPEC_SUBMODE=" + AutoGemSpecificSubMode.ToString(CultureInfo.InvariantCulture),
                    "AUTOGEM_TP_DELAY_MODE=ANCHOR_DELAY",
                    "AUTOGEM_TP_DELAY_MS=" + AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture),
                    "AUTOGEM_TP_ANCHOR=" + AutoGemTPAnchorRemaining.ToString(CultureInfo.InvariantCulture),
                    "AUTOGEM_TP_LAG=" + AutoGemTPLagBoost,
                });
            }
            catch { }
        }

        private static void NormalizeSettings()
        {
            AutoGemMode = Math.Max(0, Math.Min(4, AutoGemMode));
            AutoGemSpecificSubMode = Math.Max(0, Math.Min(1, AutoGemSpecificSubMode));
            AutoGemTPDelayMs = ClampTPDelayMs(AutoGemTPDelayMs);
            AutoGemTPAnchorRemaining = ClampTPAnchorRemaining(AutoGemTPAnchorRemaining);
            if (string.IsNullOrWhiteSpace(AutoGemSpecificName))
                AutoGemSpecificName = "Bane of the Trapped";
        }

        private static string SettingsDirectory()
        {
            try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "s7o", "settings"); }
            catch { return "settings"; }
        }

        private static string SettingsPath()
        {
            try { return Path.Combine(SettingsDirectory(), SettingsFileName); }
            catch { return SettingsFileName; }
        }

        private static string LegacySettingsPath()
        {
            try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "s7o", LegacySettingsFileName); }
            catch { return LegacySettingsFileName; }
        }

        private static string ResolveSettingsLoadPath()
        {
            string current = SettingsPath();
            try
            {
                if (File.Exists(current)) return current;
                string legacy = LegacySettingsPath();
                return File.Exists(legacy) ? legacy : current;
            }
            catch { return current; }
        }

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

    internal static class FreeHudInput
    {
        public const ushort VirtualKeyForTownPortal = 0x54; // T: FreeHUD uses direct virtual-key input for Town Portal.
        public const ushort VK_ESCAPE = 0x1B;
        public const ushort VK_SPACE = 0x20;
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
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

        public static bool MouseMoveClient(IController hud, int x, int y)
        {
            try
            {
                if (hud == null || hud.Window == null || !hud.Window.IsForeground)
                    return false;

                Size size = hud.Window.Size;
                if (x < 0 || y < 0 || x >= size.Width || y >= size.Height)
                    return false;

                Point offset = hud.Window.Offset;
                long screenX = (long)offset.X + x;
                long screenY = (long)offset.Y + y;
                if (screenX < int.MinValue || screenX > int.MaxValue ||
                    screenY < int.MinValue || screenY > int.MaxValue)
                    return false;

                return SetCursorPos((int)screenX, (int)screenY) && hud.Window.IsForeground;
            }
            catch
            {
                return false;
            }
        }

        public static bool MouseDown(MouseButtons button)
        {
            return button == MouseButtons.Left && SendMouse(MOUSEEVENTF_LEFTDOWN, 0);
        }

        public static bool MouseUp(MouseButtons button)
        {
            return button == MouseButtons.Left && SendMouse(MOUSEEVENTF_LEFTUP, 0);
        }

        public static bool ScrollDown(int clicks)
        {
            return SendMouse(MOUSEEVENTF_WHEEL, unchecked((uint)(-WHEEL_DELTA * Math.Max(1, clicks))));
        }

        public static bool ScrollUp(int clicks)
        {
            return SendMouse(MOUSEEVENTF_WHEEL, (uint)(WHEEL_DELTA * Math.Max(1, clicks)));
        }

        public static bool KeyDown(ushort vk) { return vk != 0 && SendKeyboard(vk, false); }
        public static bool KeyUp(ushort vk) { return vk != 0 && SendKeyboard(vk, true); }

        private static bool SendMouse(uint flags, uint mouseData)
        {
            var input = new[]
            {
                new INPUT
                {
                    type = INPUT_MOUSE,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = mouseData,
                            dwFlags = flags,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero,
                        }
                    }
                }
            };
            return SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        private static bool SendKeyboard(ushort vk, bool up)
        {
            var input = new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = vk,
                            wScan = 0,
                            dwFlags = up ? KEYEVENTF_KEYUP : 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero,
                        }
                    }
                }
            };
            return SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }

public class s7o_AutoGemUpgradeNavigator : BasePlugin, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler
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
        // Preserve the proven first T timing. Only a dropped first request enters retry mode,
        // after a short grace window so AutoLoot can finish its existing Urshi safety arbitration.
        public int PortalRetryInitialDelayMs { get; set; } = 220;
        public int PortalRetryIntervalMs { get; set; } = 30;
        public int PortalMaxAttempts { get; set; } = 4;
        public int PortalConfirmationGraceMs { get; set; } = 220;
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
        private TownRewardSpaceState _townRewardSpaceState = TownRewardSpaceState.Idle;
        private int _townRewardSessionId;
        private int _townRewardSessionStartTick = int.MinValue;
        private int _townRewardSpaceCount;
        private int _chatCloseFadeWaitUntilTick = int.MinValue;
        private bool _chatCloseFadePendingDialogSpace;
        private int _chatCloseFadePendingAttempts = int.MinValue;
        private const int ConversationCloseThrottleMs = 150;
        private const int TownRewardSessionTimeoutMs = 30 * 60 * 1000;
        private const int InputPulseMs = 10;
        private const int PortalKeyPulseMs = 40;
        private const int PortalRetryKeyPulseMs = 20;
        private const int UrshiMoveDelayMs = 20;
        private const int UrshiMouseHoldMs = 30;
        private const int UrshiPortalPollMs = 25;
        private const int UrshiFirstPortalPollCount = 4;
        private const int UrshiSecondPortalPollCount = 8;
        private const int UrshiExtraWaitMs = 100;
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
        private bool _portalRequestPending;
        private bool _portalRetryExhaustedThisRun;
        private int _portalRequestAttempts;
        private bool _upgradeProgressObservedThisRun;
        private bool _tailWaitAfterFinalAttempt;
        private PendingInputKind _pendingInputKind;
        private ushort _pendingKey;
        private int _pendingInputReleaseTick = int.MinValue;
        private bool _pendingRestoreCursor;
        private int _pendingRestoreCursorX;
        private int _pendingRestoreCursorY;
        private bool _pendingInputReleaseSucceeded = true;
        private UrshiCancelStage _urshiCancelStage;
        private int _urshiCancelDueTick = int.MinValue;
        private int _urshiCancelChecksRemaining;
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

        private enum TownRewardSpaceState
        {
            Idle,
            AwaitingTown,
            AwaitingRiftClose,
            RewardSpaceSent,
        }

        private enum PendingInputKind
        {
            None,
            Mouse,
            Key,
        }

        private enum UrshiCancelStage
        {
            Idle,
            FirstMoveDelay,
            FirstMouseHold,
            FirstPortalCheck,
            FirstExtraWait,
            SecondMoveDelay,
            SecondMouseHold,
            SecondPortalCheck,
        }

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
            s7o_AutoGemUpgradeState.LoadSettings();

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

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            if (!newGame)
                return;

            CancelPendingInput();
            CancelUrshiCancel();
            _lastConversationCloseTick = int.MinValue;
            ClearChatCloseFadeWait();
            ResetTownRewardLifecycle("new-game");
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
            // so existing UI consumers still see the current effective value.
            PortalAtFourDelayMs = s7o_AutoGemUpgradeState.GetFullPortalDelayMs();

            if (changed)
            {
                ResetState();

            }

            _menuStateApplied = true;
            _lastMenuEnabled = enabled;
            _lastMenuMode = mode;
            _lastMenuSpecificName = specificName;
        }

        public void AfterCollect()
        {
            if (AdvancePendingInput())
                return;

            if (AdvanceUrshiCancel())
                return;

            SyncMenuState();

            if (!AutoStartEnabled)
            {
                ResetTownRewardLifecycle("disabled");
                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            if (!Hud.Game.IsInGame)
            {
                _lastConversationCloseTick = int.MinValue;
                // Preserve a pending Urshi reward session across transient loading frames.
                // OnNewArea(newGame) remains the authoritative reset for a real game change.
                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            if (!Hud.Window.IsForeground || Hud.Game.IsLoading || Hud.Game.IsPaused)
            {
                _lastConversationCloseTick = int.MinValue;
                ResetState();
                ClearSoftRestartWait(true);
                return;
            }

            bool gemPaneVisible = SafeUiVisible(_gemUpgradePane);
            UpdateTownRewardLifecycle(gemPaneVisible);

            if (Hud.Game.IsInTown)
            {
                TryCloseTownRewardDialogOnce();
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

                        _stage = AutomationStage.DirectCaptureCurrentPage;
                        return;
                    }

                    if (ViewportNeedsSettle() && _postScrollSettlePasses < MaxPostScrollSettlePasses)
                    {
                        _postScrollSettlePasses++;
                        _lastActionTick = NowTick();
                        _afterScrollWait = PostScrollWaitMs;

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

                    if (_viewportOriginRowInt < 0)
                        SetViewportOriginExact(0, "direct-init");

                    int currentTopRow = GetAuthoritativeViewportTopRow();
                    int currentBottomRow = GetCurrentViewportBottomRow();
                    int targetRow = _virtualGrid != null && _virtualGrid.ColumnCount > 0
                        ? Math.Max(0, _target.AbsoluteIndex / Math.Max(1, _virtualGrid.ColumnCount))
                        : currentTopRow;


                    string trustReason;
                    if (!IsPageTrustworthyForResolve(out trustReason))
                    {

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

                    _preClickItemButtonAnim = SafeAnimState(_itemButton);
                    int totalGemSlots = Math.Max(_orderedGems != null ? _orderedGems.Count : 0, _target.AbsoluteIndex + 1);

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

        private bool TryRequestTimedPortalDuringRun(int now)
        {
            if (Hud.Game?.Me == null) return false;

            bool castingPortal = Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal;
            if (_portalRequestPending && castingPortal)
            {
                // Native confirmation ends the retry burst permanently for this upgrade run.
                // If the player cancels the cast afterward, Auto Gem does not start it again.
                _portalRequestPending = false;
                _portalRequestAttempts = 0;
                _portalRequestedThisRun = true;
                return false;
            }

            if (_portalRequestedThisRun || _portalRetryExhaustedThisRun) return false;

            if (_portalRequestPending)
            {
                if (_pendingInputKind != PendingInputKind.None) return false;

                // Do not turn a transient/unsafe Urshi pane into an immediate retry storm.
                // AutoLoot already owns the pane-vs-loot arbitration; the original first T is
                // unchanged, and only a genuinely unconfirmed request waits before retrying.
                int initialRetryDelayMs = Math.Max(0, PortalRetryInitialDelayMs);
                if (_portalRequestAttempts <= 1 && _portalRequestedTick != int.MinValue
                    && ElapsedMs(_portalRequestedTick) < initialRetryDelayMs)
                    return false;

                int retryMs = Math.Max(10, PortalRetryIntervalMs);
                if (_lastPortalActionTick != int.MinValue && ElapsedMs(_lastPortalActionTick) < retryMs)
                    return false;

                int maxAttempts = Math.Max(1, PortalMaxAttempts);
                if (_portalRequestAttempts >= maxAttempts)
                {
                    // Give the last pulse time to surface as CastingPortal, then stop. This is
                    // deliberately bounded so a blocked or intentionally cancelled portal can
                    // never become a persistent T-spam loop.
                    if (_lastPortalActionTick != int.MinValue
                        && ElapsedMs(_lastPortalActionTick) < Math.Max(retryMs, PortalConfirmationGraceMs))
                        return false;

                    _portalRequestPending = false;
                    _portalRequestAttempts = 0;
                    _portalRetryExhaustedThisRun = true;
                    return false;
                }

                return BeginPortalRequestAttempt(now, true);
            }

            // Preserve the old behavior for a portal cast started outside this request path.
            if (castingPortal) return false;
            if (_pendingInputKind != PendingInputKind.None) return false;

            int effectiveDelayMs = s7o_AutoGemUpgradeState.GetFullPortalDelayMs();

            // Cleanup / below-threshold runs immediately overlap TP again.
            if (s7o_AutoGemUpgradeState.IsBelowConfiguredPortalAnchorAtRunStart(_initialUpgradeAttemptsThisRun))
                return BeginPortalRequestAttempt(now, false);

            if (_portalAnchorClickTick == int.MinValue)
                return false;

            if (ElapsedMs(_portalAnchorClickTick) < effectiveDelayMs)
                return false;

            return BeginPortalRequestAttempt(now, false);
        }

        private bool BeginPortalRequestAttempt(int now, bool retry)
        {
            int pulseMs = retry ? PortalRetryKeyPulseMs : PortalKeyPulseMs;
            if (!BeginKeyPulse(FreeHudInput.VirtualKeyForTownPortal, pulseMs))
                return false;

            _lastPortalActionTick = now;
            if (!_portalRequestPending)
                _portalRequestedTick = now;
            _portalRequestPending = true;
            _portalRequestAttempts++;
            return true;
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

                if (_initialUpgradeAttemptsThisRun != int.MinValue && upgrades < _initialUpgradeAttemptsThisRun)
                    _upgradeProgressObservedThisRun = true;
                _lastObservedUpgradeAttempts = upgrades;
                _lastUpgradeProgressTick = now;
                _lastRecoveryUpgradeAttempts = int.MinValue;
                _noProgressAbortTick = int.MinValue;
            }

            if (TryRequestTimedPortalDuringRun(now))
                return;

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


                    _autoAwaitingResolution = false;
                    _autoUpgradeClickStartUpgrades = int.MinValue;
                    _autoAttemptResolvedTick = int.MinValue;
                    _autoRetargetEarliestTick = int.MinValue;
                    _autoValidationPreRank = -1;

                    if (havePlannedTarget && !sameAsCurrent)
                    {

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

            }
            bool initialClickSent = _hasSentInitialUpgradeClick && _firstUpgradeClickTick != int.MinValue;
            if (initialClickSent)
            {
                if (TryRequestTimedPortalDuringRun(now))
                    return;
            }

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

                return;
            }

            _lastObservedUpgradeAttempts = GetUpgradeAttempts();
            _initialUpgradeAttemptsThisRun = _lastObservedUpgradeAttempts;
            _lastUpgradeProgressTick = NowTick();
            _portalAnchorClickTick = int.MinValue;
            bool preservePortalState = _portalRequestedThisRun || _portalRequestPending || _portalRetryExhaustedThisRun;
            bool preservePortalConfirmed = _portalRequestedThisRun;
            bool preservePortalPending = _portalRequestPending;
            bool preservePortalExhausted = _portalRetryExhaustedThisRun;
            int preservePortalAttempts = _portalRequestAttempts;
            int preservePortalRequestedTick = _portalRequestedTick;
            int preserveLastPortalActionTick = _lastPortalActionTick;
            _lastPortalActionTick = preservePortalState ? preserveLastPortalActionTick : int.MinValue;
            _lastRecoveryUpgradeAttempts = int.MinValue;
            _portalRequestedTick = preservePortalState ? preservePortalRequestedTick : int.MinValue;
            _runningStartTick = _lastUpgradeProgressTick;
            _firstUpgradeClickTick = int.MinValue;
            _hasSentInitialUpgradeClick = false;
            _portalRequestedThisRun = preservePortalConfirmed;
            _portalRequestPending = preservePortalPending;
            _portalRetryExhaustedThisRun = preservePortalExhausted;
            _portalRequestAttempts = preservePortalAttempts;
            _upgradeProgressObservedThisRun = false;
            _noProgressAbortTick = int.MinValue;

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
    if (_portalRequestedThisRun || _portalRequestPending)
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
            if (FreeHudInput.MouseMoveClient(Hud, x, y))
            {

                return;
            }
        }

        if (_gemStatusText != null && _gemStatusText.Visible)
        {
            RectangleF s = _gemStatusText.Rectangle;
            int x = (int)Math.Round(s.Left + 24f);
            int y = (int)Math.Round(s.Bottom + 18f);
            FreeHudInput.MouseMoveClient(Hud, x, y);
        }
    }
    catch { }
}


private bool TryMoveCursorToUrshi()
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
            return false;

        int ux = (int)Math.Round(urshi.ScreenCoordinate.X);
        int uy = (int)Math.Round(urshi.ScreenCoordinate.Y);
        if (ux <= 0 || uy <= 0 || ux >= Hud.Window.Size.Width || uy >= Hud.Window.Size.Height)
            return false;

        return FreeHudInput.MouseMoveClient(Hud, ux, uy);
    }
    catch
    {
        return false;
    }
}

private bool TryStartTeleportCancelByTalkingToUrshi()
{
    if (_urshiCancelStage != UrshiCancelStage.Idle ||
        _pendingInputKind != PendingInputKind.None ||
        !IsPortalActiveOrRequested() ||
        !TryMoveCursorToUrshi())
    {
        return false;
    }

    MarkAutomationInputAction();
    _urshiCancelStage = UrshiCancelStage.FirstMoveDelay;
    _urshiCancelDueTick = NowTick() + UrshiMoveDelayMs;
    _urshiCancelChecksRemaining = 0;
    return true;
}

private void CancelUrshiCancel()
{
    _urshiCancelStage = UrshiCancelStage.Idle;
    _urshiCancelDueTick = int.MinValue;
    _urshiCancelChecksRemaining = 0;
}

private bool CompleteUrshiCancel()
{
    FocusSelectedGemAfterTeleportCancel();
    CancelUrshiCancel();
    return true;
}

private bool AdvanceUrshiCancel()
{
    if (_urshiCancelStage == UrshiCancelStage.Idle)
        return false;

    if (!Hud.Game.IsInGame || !Hud.Window.IsForeground || Hud.Game.IsLoading || Hud.Game.IsPaused)
    {
        CancelUrshiCancel();
        return false;
    }

    if (_pendingInputKind != PendingInputKind.None)
        return true;

    int now = NowTick();
    if (unchecked(_urshiCancelDueTick - now) > 0)
        return true;

    switch (_urshiCancelStage)
    {
        case UrshiCancelStage.FirstMoveDelay:
            if (!BeginMousePulseAtCurrentCursor(UrshiMouseHoldMs))
            {
                CancelUrshiCancel();
                return false;
            }
            _urshiCancelStage = UrshiCancelStage.FirstMouseHold;
            return true;

        case UrshiCancelStage.FirstMouseHold:
            if (!_pendingInputReleaseSucceeded)
            {
                CancelUrshiCancel();
                return false;
            }
            _urshiCancelChecksRemaining = UrshiFirstPortalPollCount;
            _urshiCancelStage = UrshiCancelStage.FirstPortalCheck;
            _urshiCancelDueTick = now + UrshiPortalPollMs;
            return true;

        case UrshiCancelStage.FirstPortalCheck:
            if (!IsPortalActiveOrRequested())
                return CompleteUrshiCancel();
            _urshiCancelChecksRemaining--;
            if (_urshiCancelChecksRemaining > 0)
            {
                _urshiCancelDueTick = now + UrshiPortalPollMs;
                return true;
            }
            _urshiCancelStage = UrshiCancelStage.FirstExtraWait;
            _urshiCancelDueTick = now + UrshiExtraWaitMs;
            return true;

        case UrshiCancelStage.FirstExtraWait:
            if (!IsPortalActiveOrRequested())
                return CompleteUrshiCancel();
            if (!TryMoveCursorToUrshi())
            {
                CancelUrshiCancel();
                return false;
            }
            MarkAutomationInputAction();
            _urshiCancelStage = UrshiCancelStage.SecondMoveDelay;
            _urshiCancelDueTick = now + UrshiMoveDelayMs;
            return true;

        case UrshiCancelStage.SecondMoveDelay:
            if (!BeginMousePulseAtCurrentCursor(UrshiMouseHoldMs))
            {
                CancelUrshiCancel();
                return false;
            }
            _urshiCancelStage = UrshiCancelStage.SecondMouseHold;
            return true;

        case UrshiCancelStage.SecondMouseHold:
            if (!_pendingInputReleaseSucceeded)
            {
                CancelUrshiCancel();
                return false;
            }
            _urshiCancelChecksRemaining = UrshiSecondPortalPollCount;
            _urshiCancelStage = UrshiCancelStage.SecondPortalCheck;
            _urshiCancelDueTick = now + UrshiPortalPollMs;
            return true;

        case UrshiCancelStage.SecondPortalCheck:
            if (!IsPortalActiveOrRequested())
                return CompleteUrshiCancel();
            _urshiCancelChecksRemaining--;
            if (_urshiCancelChecksRemaining > 0)
            {
                _urshiCancelDueTick = now + UrshiPortalPollMs;
                return true;
            }
            CancelUrshiCancel();
            return true;

        default:
            CancelUrshiCancel();
            return false;
    }
}

private void HandleNoEligibleTargetStop(string warningText, string failureReason)
{
    if (!string.IsNullOrWhiteSpace(warningText))
        _paneWarningMessage = warningText;

    string finalReason = string.IsNullOrWhiteSpace(failureReason)
        ? "no eligible target gem under current rules"
        : failureReason;

    if (IsPortalActiveOrRequested())
        TryStartTeleportCancelByTalkingToUrshi();


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
                AddAdditionalSocketedLegendaryGems(ordered);
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

        private void AddAdditionalSocketedLegendaryGems(List<IItem> list)
        {
            try
            {
                foreach (var gem in Hud.Game.Items.Where(i => IsLegendaryGem(i) && i.Location == ItemLocation.InSocket))
                    list.Add(gem);
            }
            catch { }

            try
            {
                foreach (var parent in Hud.Game.Items.Where(i => i != null))
                {
                    var socketed = parent.ItemsInSocket;
                    if (socketed == null)
                        continue;

                    foreach (var gem in socketed.Where(IsLegendaryGem))
                        list.Add(gem);
                }
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
                                _confirmedSlotMap[acdMatch.AbsoluteIndex] = Tuple.Create(acdName, acdRank);
                            continue;
                        }
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
                            _confirmedSlotMap[match.AbsoluteIndex] = Tuple.Create(identity.Item1, identity.Item2);
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
            }
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
                VisibleCell targetVC = FindLiveTargetCell(visibleCells);
                if (targetVC != null)
                {

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
            if (authRows > strongestRows && liveRows < 3 && !IsCompleteOwnedGemListVisible())
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
            return new ObservedCell
            {
                VisibleCell = cell,
                SelectedGemName = selection.Item1,
                SelectedGemRank = selection.Item2,
                SourceText = sourceText,
                MatchTarget = HasKnownTargetAcd()
                    ? IsLiveTargetIdentityCell(cell)
                    : selection.Item1 != null && _target != null && IsTargetMatch(selection.Item1, selection.Item2, _target),
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




            if (_currentSnapshot.IdentifiedCellCount < MinIdentifiedCellsForNavigation)
            {
                string warn = "probe identified only " + _currentSnapshot.IdentifiedCellCount + " visible gems";

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

            // The old 570 ms blocking drag path was replaced with staged wheel input.
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



            ViewportCapture cap;
            if (TryCaptureViewport(out cap))
            {
                bool hasLiveCells = RefreshSnapshotFromViewportCapture(cap);
                if (hasLiveCells)
                    return true;
            }
            return false;
        }

        private void HandleCompletedSearchProbe()
        {
            int authoritativeTopRow = GetAuthoritativeViewportTopRow();
            int authoritativeVisibleRows = GetAuthoritativeViewportVisibleRowCount();

            bool targetRowVisible = IsTargetRowReliablyVisible(_target);

            if (!FullListVerificationMode)
            {
                if (_currentSnapshot.TargetCell != null)
                {

                    _stage = AutomationStage.SelectObservedTarget;
                    return;
                }

                if (_target != null && _currentSnapshot?.VisibleCells != null)
                {
                    VisibleCell acdTargetCell = FindLiveTargetCell(_currentSnapshot.VisibleCells);
                    if (acdTargetCell != null)
                    {

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

                if (TryBottomNudgeScroll())
                {
                    StartPageProbe(ProbeReason.Search);
                    return;
                }
            }

            if (FullListVerificationMode && _orderedGems != null && _orderedGems.Count > 0 && _scannedAbsoluteIndices.Count >= _orderedGems.Count)
            {


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

            int liveDirection;
            if (TryGetTargetDirectionFromLiveMappedRange(_target, out liveDirection)
                && liveDirection != 0
                && (deltaRows == 0 || Math.Sign(deltaRows) != liveDirection))
            {
                desiredTopRow = currentTopRow + liveDirection;
                deltaRows = liveDirection;
            }

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

        private bool IsCompleteOwnedGemListVisible()
        {
            return _orderedGems != null
                && _orderedGems.Count > 0
                && GetAuthoritativeViewportTopRow() == 0
                && HasLiveViewportTruth()
                && _currentSnapshot.LiveVisibleCells.Count(c => c != null && !c.IsProjected) >= _orderedGems.Count;
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

            int liveDirection;
            if (TryGetTargetDirectionFromLiveMappedRange(target, out liveDirection))
                return liveDirection == 0;

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
            if (liveRows < MinLiveScanRowsForNavigation && !IsCompleteOwnedGemListVisible())
            {

                return false;
            }

            return true;
        }


        private bool CanAttemptListCommit(VisibleCell candidate, string reason)
        {
            if (candidate == null)
            {

                return false;
            }

            if (candidate.IsProjected)
            {

                return false;
            }

            if (!HasLiveViewportTruth())
            {

                return false;
            }

            if (!IsCurrentEpochLiveSlot(candidate))
            {

                return false;
            }

            if (_target == null || !IsTargetRowReliablyVisible(_target))
            {

                return false;
            }

            if (HasKnownTargetAcd() && !IsLiveTargetIdentityCell(candidate))
            {
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
            }
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
            }
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
                SetViewportOriginExact(bestTop, "acd");
                _lostLiveIdentityAfterScroll = false;
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

            // Native ACD identity is authoritative whenever it is available.
            if (HasKnownTargetAcd())
            {
                VisibleCell liveAcd = FindLiveTargetCell(_currentSnapshot.LiveVisibleCells);

                if (liveAcd != null)
                    return AssignObservedTarget(liveAcd, "acd-direct", true);

                return false;
            }

            // Legacy fallback for the unlikely case where the target has no usable ACD.
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


            return false;
        }

        private bool HasKnownTargetAcd()
        {
            return _targetAcd != 0 && _targetAcd != 0xFFFFFFFF;
        }

        private bool TryGetTargetDirectionFromLiveMappedRange(GemTarget target, out int direction)
        {
            direction = 0;
            if (target == null || _currentSnapshot == null || _currentSnapshot.LiveVisibleCells == null
                || _orderedGems == null || _orderedGems.Count == 0)
                return false;

            if (FindLiveTargetCell(_currentSnapshot.LiveVisibleCells) != null)
                return true;

            int firstAbsoluteIndex = int.MaxValue;
            int lastAbsoluteIndex = int.MinValue;
            int mappedCount = 0;

            foreach (var cell in _currentSnapshot.LiveVisibleCells)
            {
                if (cell == null || cell.IsProjected || cell.Ref == null)
                    continue;

                uint acd = cell.Ref.CachedLegendaryGemAcdId;
                if (acd == 0 || acd == 0xFFFFFFFF)
                    continue;

                GemOrderEntry match = null;
                for (int i = 0; i < _orderedGems.Count; i++)
                {
                    GemOrderEntry candidate = _orderedGems[i];
                    if (SafeGemOrderEntryAcd(candidate) == acd)
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match == null)
                    continue;

                firstAbsoluteIndex = Math.Min(firstAbsoluteIndex, match.AbsoluteIndex);
                lastAbsoluteIndex = Math.Max(lastAbsoluteIndex, match.AbsoluteIndex);
                mappedCount++;
            }

            if (mappedCount < 2)
                return false;

            if (target.AbsoluteIndex < firstAbsoluteIndex)
                direction = -1;
            else if (target.AbsoluteIndex > lastAbsoluteIndex)
                direction = 1;

            return true;
        }

        private bool IsLiveTargetIdentityCell(VisibleCell cell)
        {
            return cell != null
                && !cell.IsProjected
                && cell.Ref != null
                && cell.Ref.CachedLegendaryGemAcdId == _targetAcd;
        }

        private VisibleCell FindLiveTargetCell(IEnumerable<VisibleCell> cells)
        {
            if (_target == null || cells == null)
                return null;

            if (HasKnownTargetAcd())
                return cells.FirstOrDefault(IsLiveTargetIdentityCell);

            return cells.FirstOrDefault(c => c != null
                && !c.IsProjected
                && c.Ref != null
                && c.AbsoluteIndex == _target.AbsoluteIndex);
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

            int liveDirection;
            if (TryGetTargetDirectionFromLiveMappedRange(_target, out liveDirection))
            {
                targetAbove = liveDirection < 0;
                targetBelow = liveDirection > 0;
                return liveDirection != 0;
            }

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
            // The old synchronous click-hold verification loop was replaced with staged polling.
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



            _identityLossCheckPending = true;
            _lastLiveCellCountBeforeScroll = _currentSnapshot != null && _currentSnapshot.LiveVisibleCells != null
                ? _currentSnapshot.LiveVisibleCells.Count
                : 0;

            for (int i = 0; i < ticksToSend; i++)
                WheelScrollTick(goDown, i == 0 ? reason : null);

            _arrowScrollAttempts = 0;
            if (goDown)
                _scrollAtBottom = false;

            // Schedule verification on the next collection tick.
            _afterScrollWait = 0;
            _lastActionTick = NowTick();


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

            // The old blocking scrollbar-track click was replaced with wheel input.
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


            return true;
        }

        private void ClickUi(IUiElement element)
        {
            if (element == null)
                return;

            RectangleF rectangle;
            try { rectangle = element.Rectangle; }
            catch { return; }

            int x = (int)Math.Round(rectangle.Left + rectangle.Width * 0.5f);
            int y = (int)Math.Round(rectangle.Top + rectangle.Height * 0.5f);
            BeginMousePulseAt(x, y, InputPulseMs, ShouldKeepCursorAtAutomationActionPoint());
        }

        private void ClickPoint(PointF point, string reason = null, int holdMs = 0)
        {
            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            // Reset-scroll clicks normally use a zero-length pulse. Preserve the existing
            // 12 ms safety ceiling for any configured nonzero hold without blocking capture.
            int clampedHold = Math.Min(holdMs, 12);
            BeginMousePulseAt(x, y, clampedHold, ShouldKeepCursorAtAutomationActionPoint());
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

        private bool IsStashUiOpen()
        {
            try
            {
                var stash = Hud != null && Hud.Inventory != null ? Hud.Inventory.StashMainUiElement : null;
                return stash != null && stash.Visible;
            }
            catch { return false; }
        }

        private bool CanSendConversationDialogSpace()
        {
            // Repeated inside-rift dialog advancement must never type into chat or
            // leak into storage. The separate town reward one-shot intentionally
            // ignores vendor/storage panels because it can fire only once per session.
            if (IsChatEntryOpen()) return false;
            if (IsStashUiOpen()) return false;
            return true;
        }

        private void UpdateTownRewardLifecycle(bool gemPaneVisible)
        {
            int now = NowTick();

            if ((_townRewardSpaceState == TownRewardSpaceState.AwaitingTown ||
                 _townRewardSpaceState == TownRewardSpaceState.AwaitingRiftClose) &&
                _townRewardSessionStartTick != int.MinValue &&
                unchecked(now - _townRewardSessionStartTick) > TownRewardSessionTimeoutMs)
            {
                ResetTownRewardLifecycle("session-timeout");
            }

            if (!Hud.Game.IsInTown && !gemPaneVisible && IsNewGreaterRiftInProgress())
            {
                if (_townRewardSpaceState != TownRewardSpaceState.Idle)
                    ResetTownRewardLifecycle("new-greater-rift");
                return;
            }

            // A real non-town Urshi gem-pane session is the only event that arms
            // the later town one-shot. Quest/attribute signals can linger and must not
            // arm a stale town Space against an unrelated conversation.
            if (!Hud.Game.IsInTown && gemPaneVisible)
                ArmTownRewardLifecycle("urshi-gem-pane-seen");

            if (Hud.Game.IsInTown && _townRewardSpaceState == TownRewardSpaceState.AwaitingTown)
                SetTownRewardState(TownRewardSpaceState.AwaitingRiftClose, "entered-town");
        }

        private void ArmTownRewardLifecycle(string reason)
        {
            if (_townRewardSpaceState == TownRewardSpaceState.AwaitingTown ||
                _townRewardSpaceState == TownRewardSpaceState.AwaitingRiftClose)
            {
                return;
            }

            _townRewardSessionId++;
            _townRewardSessionStartTick = NowTick();
            _townRewardSpaceCount = 0;
            SetTownRewardState(TownRewardSpaceState.AwaitingTown, reason);
        }

        private void ResetTownRewardLifecycle(string reason)
        {
            _townRewardSessionStartTick = int.MinValue;
            _townRewardSpaceCount = 0;
            SetTownRewardState(TownRewardSpaceState.Idle, reason);
        }

        private void SetTownRewardState(TownRewardSpaceState state, string reason)
        {
            _townRewardSpaceState = state;
        }

        private bool IsNewGreaterRiftInProgress()
        {
            try
            {
                bool inGreaterRift = Hud.Game.Me != null &&
                    (Hud.Game.Me.InGreaterRift || Hud.Game.SpecialArea == SpecialArea.GreaterRift);
                if (!inGreaterRift || IsGreaterRiftRewardQuestStep())
                    return false;

                return Hud.Game.RiftPercentage >= 0.0d && Hud.Game.RiftPercentage < 100.0d;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGreaterRiftRewardQuestStep()
        {
            uint step = GetGreaterRiftQuestStepId(382695);
            if (step == 5 || step == 10 || step == 34 || step == 46)
                return true;

            step = GetGreaterRiftQuestStepId(337492);
            return step == 5 || step == 10 || step == 34 || step == 46;
        }

        private uint GetGreaterRiftQuestStepId(uint questSno)
        {
            try
            {
                if (Hud.Game.Quests == null)
                    return 0;

                IQuest quest = Hud.Game.Quests.FirstOrDefault(q =>
                    q != null && q.SnoQuest != null && q.SnoQuest.Sno == questSno);
                return quest != null ? quest.QuestStepId : 0;
            }
            catch
            {
                return 0;
            }
        }

        private bool TryCloseTownRewardDialogOnce()
        {
            if (_townRewardSpaceState != TownRewardSpaceState.AwaitingRiftClose || !Hud.Game.IsInTown)
                return false;

            try
            {
                if (_conversationDialogMain == null || !_conversationDialogMain.Visible)
                    return false;

                // Chat owns Space. Defer without consuming the one-shot; the next town
                // tick after chat closes can still dismiss the same reward dialog.
                if (IsChatEntryOpen())
                {

                    return true;
                }

                // Consume the one allowed town Space before input. This guarantees that
                // a forge, merchant, salvage, stash, or other panel can never receive a
                // second Space from this reward session, even if SendInput is ambiguous.
                _townRewardSpaceCount = 1;
                SetTownRewardState(TownRewardSpaceState.RewardSpaceSent, "town-rift-close-dialog");
                BeginKeyPulse(FreeHudInput.VK_SPACE);
                MarkAutomationInputAction(ConversationCloseThrottleMs + 30 + InputPulseMs);

                return true;
            }
            catch
            {
                return false;
            }
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
                        if (FreeHudInput.MouseMoveClient(Hud, ux, uy))
                        {

                            return true;
                        }
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
                    if (FreeHudInput.MouseMoveClient(Hud, x, y))
                    {

                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void StartChatCloseFadeWait(bool pendingDialogSpace, int remainingAttempts, string reason)
        {
            int delay = GetChatCloseFadeDelayMs();
            BeginKeyPulse(FreeHudInput.VK_ESCAPE);
            TryHoverUrshiAfterChatClose(reason);
            _chatCloseFadeWaitUntilTick = NowTick() + delay + InputPulseMs;
            _chatCloseFadePendingDialogSpace = pendingDialogSpace;
            _chatCloseFadePendingAttempts = remainingAttempts;
            MarkAutomationInputAction(delay + 120 + InputPulseMs);

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

                    return true;
                }

                bool sendDialogSpace = _chatCloseFadePendingDialogSpace;
                int pendingAttempts = _chatCloseFadePendingAttempts;
                ClearChatCloseFadeWait();

                if (sendDialogSpace && pendingAttempts > 0 && _conversationDialogMain != null && _conversationDialogMain.Visible)
                {
                    if (!CanSendConversationDialogSpace())
                    {

                        return true;
                    }

                    _lastConversationCloseTick = NowTick();
                    BeginKeyPulse(FreeHudInput.VK_SPACE);
                    MarkAutomationInputAction(ConversationCloseThrottleMs + 30 + InputPulseMs);

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

                // Only Urshi's completed-rift reward conversation is eligible here.
                // Guardian-spawn announcements also use conversation_dialog_main but
                // have no jewel upgrade attempts and must never receive synthetic Space.
                int remainingAttempts = GetUpgradeAttempts();
                if (remainingAttempts <= 0)
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
                    // Chat input owns Space. Close chat, move away from the chat area,
                    // wait for its fade, then resume the same verified Urshi dialog path.
                    StartChatCloseFadeWait(true, remainingAttempts, "conversation_dialog_main");
                    return true;
                }

                if (!CanSendConversationDialogSpace())
                {

                    return true;
                }

                // Do not use Escape here.
                // Space advances conversation without world-clicking after the dialog closes.
                BeginKeyPulse(FreeHudInput.VK_SPACE);

                MarkAutomationInputAction(ConversationCloseThrottleMs + 30 + InputPulseMs);


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
            try
            {
                uint acd = item != null ? (uint)item.AcdId : 0u;
                if (acd != 0u && acd != 0xFFFFFFFFu)
                    return "acd|" + acd.ToString();
            }
            catch { }

            try
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.ItemUniqueId))
                    return "uid|" + item.ItemUniqueId;
            }
            catch { }

            return GetGemName(item) + "|" + item.JewelRank.ToString() + "|" + item.Location.ToString() + "|" + item.InventoryX.ToString() + "|" + item.InventoryY.ToString() + "|" + item.Seed.ToString();
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

        private bool BeginKeyPulse(ushort virtualKey, int holdMs = InputPulseMs)
        {
            if (_pendingInputKind != PendingInputKind.None || virtualKey == 0)
                return false;

            if (!FreeHudInput.KeyDown(virtualKey))
                return false;

            _pendingInputKind = PendingInputKind.Key;
            _pendingKey = virtualKey;
            _pendingInputReleaseTick = NowTick() + Math.Max(0, holdMs);
            _pendingInputReleaseSucceeded = true;
            return true;
        }

        private bool BeginMousePulseAt(int x, int y, int holdMs, bool keepCursor)
        {
            if (_pendingInputKind != PendingInputKind.None)
                return false;

            int oldX;
            int oldY;
            try
            {
                oldX = Hud.Window.CursorX;
                oldY = Hud.Window.CursorY;
            }
            catch
            {
                oldX = x;
                oldY = y;
            }

            if (!FreeHudInput.MouseMoveClient(Hud, x, y))
                return false;

            if (!FreeHudInput.MouseDown(MouseButtons.Left))
            {
                if (!keepCursor)
                    FreeHudInput.MouseMoveClient(Hud, oldX, oldY);
                return false;
            }

            int clampedHold = Math.Max(0, holdMs);
            if (clampedHold == 0)
            {
                bool released = FreeHudInput.MouseUp(MouseButtons.Left);
                if (!keepCursor)
                    FreeHudInput.MouseMoveClient(Hud, oldX, oldY);
                MarkAutomationInputAction();
                return released;
            }

            _pendingInputKind = PendingInputKind.Mouse;
            _pendingInputReleaseTick = NowTick() + clampedHold;
            _pendingRestoreCursor = !keepCursor;
            _pendingRestoreCursorX = oldX;
            _pendingRestoreCursorY = oldY;
            _pendingInputReleaseSucceeded = true;
            MarkAutomationInputAction(UserInterferenceIgnoreAfterPluginInputMs + clampedHold);
            return true;
        }

        private bool BeginMousePulseAtCurrentCursor(int holdMs)
        {
            if (_pendingInputKind != PendingInputKind.None)
                return false;

            if (!FreeHudInput.MouseDown(MouseButtons.Left))
                return false;

            _pendingInputKind = PendingInputKind.Mouse;
            _pendingInputReleaseTick = NowTick() + Math.Max(0, holdMs);
            _pendingRestoreCursor = false;
            _pendingInputReleaseSucceeded = true;
            return true;
        }

        private bool AdvancePendingInput()
        {
            if (_pendingInputKind == PendingInputKind.None)
                return false;

            if (unchecked(_pendingInputReleaseTick - NowTick()) > 0)
                return true;

            bool released = _pendingInputKind == PendingInputKind.Key
                ? FreeHudInput.KeyUp(_pendingKey)
                : FreeHudInput.MouseUp(MouseButtons.Left);

            if (_pendingInputKind == PendingInputKind.Mouse && _pendingRestoreCursor)
            {
                FreeHudInput.MouseMoveClient(Hud, _pendingRestoreCursorX, _pendingRestoreCursorY);
                ResetCursorWatchToCurrent(UserInterferenceIgnoreAfterPluginInputMs);
            }

            _pendingInputReleaseSucceeded = released;
            _pendingInputKind = PendingInputKind.None;
            _pendingKey = 0;
            _pendingInputReleaseTick = int.MinValue;
            _pendingRestoreCursor = false;
            return false;
        }

        private void CancelPendingInput()
        {
            if (_pendingInputKind == PendingInputKind.Key)
                _pendingInputReleaseSucceeded = FreeHudInput.KeyUp(_pendingKey);
            else if (_pendingInputKind == PendingInputKind.Mouse)
                _pendingInputReleaseSucceeded = FreeHudInput.MouseUp(MouseButtons.Left);

            if (_pendingInputKind == PendingInputKind.Mouse && _pendingRestoreCursor)
            {
                FreeHudInput.MouseMoveClient(Hud, _pendingRestoreCursorX, _pendingRestoreCursorY);
                ResetCursorWatchToCurrent(UserInterferenceIgnoreAfterPluginInputMs);
            }

            _pendingInputKind = PendingInputKind.None;
            _pendingKey = 0;
            _pendingInputReleaseTick = int.MinValue;
            _pendingRestoreCursor = false;
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
                    && IsCurrentEpochLiveSlot(_currentSnapshot.TargetCell.VisibleCell)
                    && (!HasKnownTargetAcd() || IsLiveTargetIdentityCell(_currentSnapshot.TargetCell.VisibleCell)))
                {
                    liveCell = _currentSnapshot.TargetCell.VisibleCell;
                    return true;
                }

                if (_currentSnapshot.LiveVisibleCells == null || _currentSnapshot.LiveVisibleCells.Count == 0)
                    return false;

                if (HasKnownTargetAcd())
                {
                    liveCell = FindLiveTargetCell(_currentSnapshot.LiveVisibleCells);
                    return liveCell != null && IsCurrentEpochLiveSlot(liveCell);
                }

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


            return true;
        }

        private void MoveCursorToPointNoClick(PointF point, string reason)
        {
            if (!ShouldKeepCursorAtAutomationActionPoint())
                return;

            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            if (!FreeHudInput.MouseMoveClient(Hud, x, y))
            {

                return;
            }

            MarkAutomationInputAction();
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

                    return false;
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

                    return;
                }

                PointF wheelHoverPoint;
                bool wheelDownward;
                if (TryGetWheelComfortHoverPoint(visible, out wheelHoverPoint, out wheelDownward))
                {
                    MoveCursorToPointNoClick(wheelHoverPoint, "hover-wheel-target-" + modeTag);

                    return;
                }

                float cx = visible.Rect.Left + (visible.Rect.Width * 0.50f);
                float cy = visible.Rect.Top + (visible.Rect.Height * 0.50f);
                MoveCursorToPointNoClick(new PointF(cx, cy), "hover-target-" + modeTag);

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

                return;
            }

            if (TryHoverAdjacentPlannedTargetWheelPrearm(planned, modeTag))
                return;

            bool downward = desiredTop > currentTop;
            PointF p = GetFastScrollPoint(_currentSnapshot.PaneRect, _currentSnapshot.ListBounds, downward);
            MoveCursorToPointNoClick(p, "hover-scroll-" + modeTag);

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
            // ResetState() clears run-level portal state. Preserve it here so an internal
            // navigation restart cannot lose or duplicate an in-flight Town Portal request.
            int  savedInitialAttempts         = _initialUpgradeAttemptsThisRun;
            int  savedLastObservedAttempts    = _lastObservedUpgradeAttempts;
            bool savedPortalRequestedThisRun  = _portalRequestedThisRun;
            bool savedPortalRequestPending    = _portalRequestPending;
            bool savedPortalRetryExhausted    = _portalRetryExhaustedThisRun;
            int  savedPortalRequestAttempts   = _portalRequestAttempts;
            int  savedPortalAnchorClickTick   = _portalAnchorClickTick;
            int  savedPortalRequestedTick     = _portalRequestedTick;
            int  savedLastPortalActionTick    = _lastPortalActionTick;
            bool savedHasSentInitialClick     = _hasSentInitialUpgradeClick;
            int  savedFirstUpgradeClickTick   = _firstUpgradeClickTick;
            bool savedUpgradeProgressObserved = _upgradeProgressObservedThisRun;
            int  savedLastUpgradeProgressTick = _lastUpgradeProgressTick;
            int  savedNoProgressAbortTick     = _noProgressAbortTick;


            ResetState();

            // Restore run-level portal fields only when a run was already in progress.
            // Condition: savedInitialAttempts != int.MinValue means HandleRunningState
            // had already recorded the baseline — this is definitely an in-pane restart.
            if (savedInitialAttempts != int.MinValue)
            {
                _initialUpgradeAttemptsThisRun  = savedInitialAttempts;
                _lastObservedUpgradeAttempts    = savedLastObservedAttempts;
                _portalRequestedThisRun         = savedPortalRequestedThisRun;
                _portalRequestPending           = savedPortalRequestPending;
                _portalRetryExhaustedThisRun    = savedPortalRetryExhausted;
                _portalRequestAttempts          = savedPortalRequestAttempts;
                _portalAnchorClickTick          = savedPortalAnchorClickTick;
                _portalRequestedTick            = savedPortalRequestedTick;
                _lastPortalActionTick           = savedLastPortalActionTick;
                _hasSentInitialUpgradeClick     = savedHasSentInitialClick;
                _firstUpgradeClickTick          = savedFirstUpgradeClickTick;
                _upgradeProgressObservedThisRun = savedUpgradeProgressObserved;
                _lastUpgradeProgressTick        = savedLastUpgradeProgressTick;
                _noProgressAbortTick            = savedNoProgressAbortTick;

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
            _portalRequestPending = false;
            _portalRetryExhaustedThisRun = false;
            _portalRequestAttempts = 0;
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
            _viewportOriginRowFloat = -1f;
            _viewportOriginRowInt = -1;
            _viewportEpoch = 0;
            _lastGoodStackPanelTop = float.NaN;
            _lastMeasuredRowPitch = float.NaN;
            _lastMeasuredColumnPitch = float.NaN;
            _lastMeasuredCellHeight = float.NaN;
            _stableGridAnchorRect = RectangleF.Empty;
            _lastStableStackTop = float.NaN;
            _noProgressSeekCount = 0;
            _runtimeBottomLocked = false;
            _runtimeBottomTopRow = -1;
            _lastLiveCellCountBeforeScroll = 0;
            _postScrollRealignAttempts = 0;
            _postScrollSettlePasses = 0;
            _trackedLiveCells.Clear();
            _highestNativeAbsoluteIndexSeen = -1;
            _lastExtendedNativeCells.Clear();
            _lastExtendedNativeRowCount = 0;
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
            _probeActive = false;
            _probeReason = ProbeReason.None;
            _probeCells.Clear();
            _probeSnapshot = null;
            _probeIndex = 0;
            _probeWaitingForValidation = false;
            _probePendingCell = null;
            _probeActionTick = int.MinValue;
            _probeNoIdentityRetryCount = 0;
            _directAdjacentStepDone = false;

            _cursorIgnoreUntilTick = int.MinValue;
            _lastGemPaneChatCloseTick = int.MinValue;
            ClearChatCloseFadeWait();
            _tailWaitAfterFinalAttempt = false;
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

            float horizontalMargin = Math.Max(12f, paneRect.Width * 0.06f);
            float warningLeft = paneRect.Left + horizontalMargin;
            float warningRight = paneRect.Right - horizontalMargin;
            float warningTop = paneRect.Top + Math.Max(70f, paneRect.Height * 0.24f);
            float warningBottom = paneRect.Bottom - Math.Max(16f, paneRect.Height * 0.08f);
            float maxWidth = Math.Max(110f, warningRight - warningLeft);

            var wrappedLines = WrapPaneWarningLines(LocalizeMultilineDisplayText(_paneWarningMessage), maxWidth);
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

        private static string LocalizeMultilineDisplayText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = s7o_Localization.Display(lines[i]);

            return string.Join("\n", lines);
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

                                return false;
                            }

                            return false;
                        }
                        else
                        {

                            return false;
                        }
                    }
                    catch { }
                }


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




            if (comfortable)
            {
                _targetComfortNudgeAttempts = 0;


                return false;
            }

            if (_targetComfortNudgeAttempts >= MaxTargetComfortNudgeAttempts)
            {


                return false;
            }

            bool downward = forceBottomNudge || bottomOverflow > topOverflow;
            float overflow = Math.Max(topOverflow, bottomOverflow);

            // CHANGED: was (edgeRow || overflow < rowPitch*0.22f).
            // 0.22 was too narrow — a 34px clip on a 70px row (49%) fell through to legacy
            // blocking click-hold. Always prefer wheel; one tick = one row, which
            // is exactly right for any partial-clip scenario. The legacy path is removed.
            bool useWheel = true;
            bool lateTp = _portalRequestedThisRun || _portalRequestPending
                || (Hud.Game?.Me != null && Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal);

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

                return;
            }
            if (cell.IsProjected)
            {

                return;
            }
            if (!IsCurrentEpochLiveSlot(cell))
            {

                return;
            }

            int absIndex;
            _currentProbeAbsoluteIndex = TryGetPredictedAbsoluteIndex(cell, out absIndex) ? absIndex : -1;

            string cellTag = "probe-cell" + (_currentProbeAbsoluteIndex >= 0 ? (" a" + _currentProbeAbsoluteIndex) : string.Empty)
                + " " + GetShortPath(cell.Ref != null ? cell.Ref.Path : string.Empty);

            bool forceCoordinate = _lostLiveIdentityAfterScroll && !HasKnownTargetAcd();

            if (cell.Ref?.Element != null && !cell.IsProjected && !forceCoordinate)
            {


                ClickUi(cell.Ref.Element);
            }
            else
            {
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

                ClickPoint(new PointF(x, y), cellTag, 0);
            }
        }

private int GetCurrentViewportVisibleRowCount()
        {
            return GetAuthoritativeViewportVisibleRowCount();
        }

    }
}
