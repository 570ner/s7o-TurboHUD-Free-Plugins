using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Saves one Paragon layout per character and native Armory slot, then
    // restores it after that exact preset is equipped. Persisted native
    // fingerprints verify settled profiles without opening Paragon. At startup,
    // the already equipped profile may establish layout trust passively; a passive
    // mismatch never opens Paragon or changes points. Legacy or genuinely different
    // layouts retain the visible apply path and are fingerprinted afterward.
    // Native Armory identity and stable item/socket data remain the final stale-slot
    // and transitional-build barriers.
    public class s7o_Paragon_Builds : BasePlugin,
        IAfterCollectHandler,
        IInGameTopPainter
    {
        private const string SettingsFileName = "s7o_Paragon_Builds.ini";
        private const int SettingsVersion = 15;
        private const int NoTick = int.MinValue;
        private const int ActiveTabAnim = 13;

        private const int CoreTab = 0;
        private const int PrimaryRow = 0;
        private const int VitalityRow = 1;
        private const string OverflowPromptText = "Extra Paragon points go into:";

        // Main user-tunable allocation speed. Zero means one click on every
        // available AfterCollect cycle; the HUD collection cadence remains the
        // effective rate limiter.
        public const int PointClickIntervalMs = 0;

        private const int FastActionMs = 0;
        private const int ModifierLeadMs = 0;
        private const int ModifierTailMs = 0;
        private const int TabSettleMs = 0;
        private const int BurstSettleMs = 32;
        private const int ClickProgressTimeoutMs = 300;
        private const int ResetTimeoutMs = 650;
        private const int OpenParagonTimeoutMs = 1800;
        private const int AcceptSettleMs = 32;
        private const int CloseSettleMs = 0;
        private const int EquipDetectTimeoutMs = 7000;
        private const int ArmoryMinimumWaitMs = 550;
        // A matching saved build can proceed after a short stable sample.
        // A mismatch is held longer because ArmoryBugFix may still be moving
        // socketed gems after the native equipment layout has already settled.
        private const int ProfileMatchStableMs = 400;
        private const int ProfileMismatchStableMs = 900;
        private const int ProfileMismatchGraceMs = 3000;
        private const int BuildStableSamples = 3;
        private const int NativeFingerprintSchemaVersion = 1;
        private const int NativeTaskSampleMs = 100;
        private const int NativeTaskStableSamples = 3;
        private const int NativeTaskStableSpanMs = 750;
        private const int NativeTaskGraceMs = 1200;
        private const int NativeVerifyTimeoutMs = 5000;
        private const int NativeCaptureTimeoutMs = 8000;
        private const int StatusHoldMs = 2200;
        private const int WarningHoldMs = 6500;
        private const float WarningPaneWidthRel = 0.74f;
        private const float WarningPaneCenterYRel = 0.39f;
        private const float WarningOutlineOffset = 2.0f;
        private const float WarningLineGap = 3.0f;
        private const int BuildMetadataRefreshMs = 500;
        private const int NativePageSelectedAnim = 18;
        private const int NativeEquipDisabledAnim = 72;
        private const int NativeEquipPressedAnim = 73;
        private const int NativeEquipHoverAnim = 74;
        private const int NativeEquipEnabledAnim = 75;
        private const int MaxAutomationClicks = 2000;
        private const int MaxNoProgressRetries = 3;
        private const int BurstClickBudgetMargin = 2;

        // Verified from the supplied Armory UI log, relative to the Armory root.
        private const float EquipRelX = 0.4810f;
        private const float EquipRelY = 0.7731f;
        private const float EquipRelW = 0.3745f;
        private const float EquipRelH = 0.0269f;
        private const float ArmoryPadX = 0.008f;
        private const float ArmoryPadY = 0.006f;
        private const float ArmoryHeroToggleCenterXRel = 0.715f;
        private const float ArmoryHeroToggleYRel = 0.166f;
        private const float ArmoryHeroToggleWRel = 0.096f;
        private const float ArmoryHeroToggleHRel = 0.022f;
        private const float ArmoryHeroToggleLabelGap = 2.0f;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const uint AnyAttributeModifier = uint.MaxValue;
        private const uint LegacyAnyAttributeModifier = 0xFFFFFu;

        private static readonly string ParagonBase =
            "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect";
        private static readonly string ArmoryBase =
            "Root.NormalLayer.equipmentManager_mainPage";

        private static readonly string[] TabNames =
        {
            "Core", "Offense", "Defense", "Utility"
        };

        private static readonly double[] NativeRowTolerances =
        {
            0.51d, 0.51d, 0.011d, 0.51d,
            0.00025d, 0.00025d, 0.00025d, 0.00025d,
            0.00025d, 0.00025d, 0.51d, 1.01d,
            0.00025d, 0.00025d, 0.51d, 0.021d
        };

        // These aggregate result attributes can vary with equipment or buffs.
        // Their rows are reconstructed from native category spent totals after
        // the other three rows in the category are verified.
        private static readonly int[] NativeInferredRows = { 4, 9, 15 };

        // The same five direct tab controls are reused on both Armory pages.
        private static readonly int[,] NativeSelectedRowAnim =
        {
            { 116, 92, 62, 86, 113 },
            { 116, 38, 44, 98, 116 }
        };

        public bool AutoApplyOnArmoryEquip { get; set; } = true;
        public bool RestoreCursorAfterAutomation { get; set; } = true;
        public bool ShowOverlay { get; set; } = true;
        public Keys ParagonWindowKey { get; set; } = Keys.P;

        public bool CanConfigureCurrentHeroParagon
        {
            get { return CurrentHeroId != 0u; }
        }

        public bool IsCurrentHeroParagonEnabled
        {
            get
            {
                uint heroId = CurrentHeroId;
                return heroId != 0u && !_disabledHeroIds.Contains(heroId);
            }
        }

        public bool SetCurrentHeroParagonEnabled(bool enabled)
        {
            uint heroId = CurrentHeroId;
            if (heroId == 0u) return false;

            bool changed = enabled
                ? _disabledHeroIds.Remove(heroId)
                : _disabledHeroIds.Add(heroId);
            if (!changed) return true;

            ApplyCurrentHeroParagonEnabledState(enabled);
            SaveSettings();
            return true;
        }

        public bool ToggleCurrentHeroParagonEnabled()
        {
            bool enabled = !IsCurrentHeroParagonEnabled;
            return SetCurrentHeroParagonEnabled(enabled)
                ? enabled
                : IsCurrentHeroParagonEnabled;
        }

        public void ForceStopForDisable()
        {
            StopCurrentHeroParagonWork(
                "plugin disabled");

            // Global re-enable must rediscover the current build and may retry
            // passive trust even when it already ran earlier in this session.
            _passiveNativeVerifyAttemptKey = string.Empty;
            _currentBuildKey = string.Empty;
            _currentStableBuildKey = string.Empty;
            _currentBuildDisplayLabel = "current build";
            _activeArmoryIndex = -1;
            _activeArmoryName = string.Empty;
            _nextBuildMetadataRefreshTick = NoTick;
        }

        private IUiElement _paragonRoot;
        private readonly IUiElement[] _tabs = new IUiElement[4];
        private readonly IUiElement[] _available = new IUiElement[4];
        private readonly IUiElement[] _rows = new IUiElement[4];
        private readonly IUiElement[] _pointsSpent = new IUiElement[4];
        private readonly IUiElement[] _plusButtons = new IUiElement[4];
        private IUiElement _resetButton;
        private IUiElement _acceptButton;
        private IUiElement _closeButton;
        private IUiElement _armoryRoot;
        private IUiElement _armoryPage1;
        private IUiElement _armoryPage2;
        private IUiElement _armoryLoadoutName;
        private IUiElement _armoryEquipButton;
        private readonly IUiElement[] _armoryNativeTabs =
            new IUiElement[5];

        private IFont _smallFont;
        private IFont _instructionFont;
        private IFont _profileSavedFont;
        private IFont _profileMissingFont;
        private IFont _warningFont;
        private IFont _warningOutlineFont;
        private IBrush _saveButtonBackBrush;
        private IBrush _saveButtonHoverBrush;
        private IBrush _saveButtonBorderBrush;
        private IBrush _toggleBackBrush;
        private IBrush _toggleHoverBrush;
        private IBrush _toggleBorderBrush;
        private IBrush _toggleSelectedBackBrush;
        private IBrush _toggleSelectedBorderBrush;
        private IBrush _toggleIndicatorOnBrush;
        private IBrush _toggleIndicatorOffBrush;

        private string _settingsPath;
        private readonly Dictionary<string, ParagonProfile> _profiles =
            new Dictionary<string, ParagonProfile>(StringComparer.Ordinal);
        private readonly HashSet<uint> _disabledHeroIds =
            new HashSet<uint>();

        private readonly int[,] _capturedRows = new int[4, 4];
        private readonly bool[] _capturedTabsSeen = new bool[4];
        private readonly int[] _capturedAvailable = new int[4];
        private readonly int[,] _scanRows = new int[4, 4];
        private readonly bool[] _scanTabsSeen = new bool[4];
        private readonly int[] _scanAvailable = new int[4];
        private int _currentOverflowRow = -1;
        private bool _currentOverflowExplicit;
        private string _currentBuildKey = string.Empty;
        private string _currentStableBuildKey = string.Empty;
        private string _currentBuildDisplayLabel = "current build";
        private int _activeArmoryIndex = -1;
        private string _activeArmoryName = string.Empty;

        private int _nextBuildMetadataRefreshTick = NoTick;
        private System.Drawing.RectangleF _profileSaveButtonRect;

        private bool _profileSaveAfterScan;
        private uint _profileSaveHeroId;
        private string _profileSaveLiveKey = string.Empty;
        private string _profileSaveBaseKey = string.Empty;
        private string _profileSaveStableKey = string.Empty;
        private int _profileSaveArmoryIndex = -1;
        private string _profileSaveLabel = string.Empty;

        private bool _knownCurrentLayoutValid;
        private uint _knownCurrentHeroId;
        private string _knownCurrentLayoutKey = string.Empty;
        private readonly Dictionary<uint, string> _knownLayoutByHero =
            new Dictionary<uint, string>();

        private NativeTaskKind _nativeTaskKind = NativeTaskKind.None;
        private ParagonProfile _nativeTaskProfile;
        private NativeFingerprint _nativeTaskCandidate;
        private string _nativeTaskSignature = string.Empty;
        private int _nativeTaskStartTick = NoTick;
        private int _nativeTaskCandidateFirstTick = NoTick;
        private int _nativeTaskSamples;
        private int _nativeTaskNextTick = NoTick;


        // Prevent repeated passive attempts against the same saved fingerprint
        // during one character session.
        private string _passiveNativeVerifyAttemptKey = string.Empty;

        private bool _leftDownLast;
        private bool _paragonOpenLast;
        private bool _scanRunning;
        private int _scanOriginalTab = -1;
        private bool _scanRestoringCore;
        private int _scanTab;
        private int _scanNextTick = NoTick;
        private int _continuousCaptureTick = NoTick;

        private bool _equipPending;
        private int _equipClickTick = NoTick;
        private string _equipStableKey = string.Empty;
        private string _equipStableBaseKey = string.Empty;
        private string _equipStableStableKey = string.Empty;
        private int _equipStableSinceTick = NoTick;
        private int _equipStableSamples;
        private int _equipRequestedArmoryIndex = -1;
        private string _equipRequestedArmoryName = string.Empty;
        private bool _equipNativeStateObserved;
        private bool _equipNativeAccepted;
        private NativeFingerprint _equipNativeCandidate;
        private string _equipNativeSignature = string.Empty;
        private int _equipNativeCandidateFirstTick = NoTick;
        private int _equipNativeSamples;

        private ApplyState _applyState = ApplyState.Idle;
        private ParagonProfile _applyProfile;
        private int _applyTab;
        private int _applyOrderIndex;
        private readonly int[] _applyRowOrder = new int[4];
        private int _applyCurrentRow = -1;
        private bool _applyCurrentIsOverflow;
        private int _applyNextTick = NoTick;
        private int _applyDeadlineTick = NoTick;
        private int _applyNoProgressRetries;
        private int _applyClickCount;
        private int _applyResetAttempts;
        private bool _applyChanged;
        private ClickModifier _burstModifier = ClickModifier.None;
        private bool _pendingModifierOwned;
        private int _burstPointX;
        private int _burstPointY;
        private int _burstClicksPlanned;
        private int _burstClicksSent;
        private bool _burstOverflow;
        private int _burstBeforeAssigned;
        private int _burstBeforeAvailable;
        private int _burstLastAssigned;
        private int _burstLastAvailable;
        private int _burstLastProgressTick = NoTick;
        private ApplyState _burstReturnState = ApplyState.Idle;
        private bool _applyEscapedArmory;

        private bool _restoreCursorCaptured;
        private int _restoreCursorX;
        private int _restoreCursorY;
        private bool _scanCursorCaptured;
        private int _scanCursorX;
        private int _scanCursorY;

        private string _statusText = string.Empty;
        private int _statusUntilTick = NoTick;
        private string _warningText = string.Empty;
        private int _warningUntilTick = NoTick;
        private uint _sessionHeroId;

        public s7o_Paragon_Builds()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            ResolvePaths();
            LoadSettings();
            RegisterUiElements();
            BuildResources();
        }

        private void ApplyCurrentHeroParagonEnabledState(bool enabled)
        {
            if (!enabled)
            {
                StopCurrentHeroParagonWork(
                    "disabled for current hero");
                return;
            }

            // Paragon may have changed while disabled; rebuild trust passively.
            InvalidateKnownCurrentLayout();
            _passiveNativeVerifyAttemptKey = string.Empty;
            _nextBuildMetadataRefreshTick = Environment.TickCount;
        }

        private void StopCurrentHeroParagonWork(
            string reason)
        {
            // Abort through the existing path in case rows already changed.
            if (_applyState != ApplyState.Idle)
                AbortApply(reason);
            else
                InvalidateKnownCurrentLayout();

            ResetNativeTask();
            ClearPendingEquip();
            ClearProfileSaveRequest();
            _scanRunning = false;
            _scanRestoringCore = false;
            _scanNextTick = NoTick;
            _continuousCaptureTick = NoTick;
            RestoreScanCursor();
            ClearWarning();
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            bool leftDown = IsLeftButtonDown();

            if (!Enabled || !CanObserve())
            {
                _leftDownLast = leftDown;
                if (_applyState != ApplyState.Idle)
                    AbortApply("game state unavailable");
                if (_nativeTaskKind != NativeTaskKind.None)
                    ResetNativeTaskSampling(now);
                return;
            }

            EnsureSessionConfidence(now);
            if (!IsForeground())
            {
                _leftDownLast = leftDown;
                if (_applyState != ApplyState.Idle)
                    AbortApply("foreground lost");
                if (_nativeTaskKind != NativeTaskKind.None)
                    ResetNativeTaskSampling(now);
                return;
            }

            if (leftDown != _leftDownLast)
            {
                if (leftDown)
                {
                    bool heroToggleHandled =
                        TryHandleArmoryHeroToggleClick();

                    if (!heroToggleHandled &&
                        IsCurrentHeroParagonEnabled)
                    {
                        if (_applyState != ApplyState.Idle)
                            AbortApply("user mouse input");
                        else
                            HandlePhysicalMouseDown(now);
                    }
                }
            }
            _leftDownLast = leftDown;

            bool paragonOpen = IsParagonOpen();

            if (!IsCurrentHeroParagonEnabled)
            {
                if (_knownCurrentLayoutValid ||
                    _applyState != ApplyState.Idle ||
                    _nativeTaskKind != NativeTaskKind.None ||
                    _equipPending ||
                    _scanRunning ||
                    _profileSaveAfterScan)
                {
                    ApplyCurrentHeroParagonEnabledState(false);
                }

                // Keep the edge detector synchronized while disabled so
                // re-enabling cannot manufacture an open/close transition.
                _paragonOpenLast = paragonOpen;
                return;
            }

            if (paragonOpen && !_paragonOpenLast)
                OnParagonOpened(now);
            else if (!paragonOpen && _paragonOpenLast)
                OnParagonClosed();
            _paragonOpenLast = paragonOpen;

            if (paragonOpen && _applyState == ApplyState.Idle)
            {
                CaptureActiveTab(now);
                ProcessCaptureScan(now, leftDown);
            }

            ProcessPendingEquip(now);
            ProcessNativeTask(now);
            ProcessApply(now, leftDown);

            string liveKey = GetLiveBuildKey();
            if (!string.IsNullOrEmpty(liveKey) &&
                !string.Equals(_currentBuildKey, liveKey,
                    StringComparison.Ordinal))
            {
                _currentBuildKey = liveKey;
                _currentStableBuildKey = GetStableBuildKey();
                TryBootstrapCurrentArmoryProfile();

                ParagonProfile existing =
                    FindCurrentProfile(liveKey);
                if (existing != null)
                    CopyProfileToCapture(existing);
                else
                    ClearCapturedProfile();

                RefreshCurrentBuildDisplay();
                TryBeginPassiveCurrentProfileVerification(
                    existing,
                    now);

                _nextBuildMetadataRefreshTick = unchecked(
                    now + BuildMetadataRefreshMs);
            }
            else if (!string.IsNullOrEmpty(_currentBuildKey) &&
                (_nextBuildMetadataRefreshTick == NoTick ||
                 unchecked(now -
                    _nextBuildMetadataRefreshTick) >= 0))
            {
                if (_activeArmoryIndex < 0)
                    TryBootstrapCurrentArmoryProfile();

                RefreshCurrentBuildDisplay();

                ParagonProfile existing =
                    FindCurrentProfile(_currentBuildKey);

                TryBeginPassiveCurrentProfileVerification(
                    existing,
                    now);

                _nextBuildMetadataRefreshTick = unchecked(
                    now + BuildMetadataRefreshMs);
            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!Enabled || clipState != ClipState.AfterClip)
                return;

            DrawArmoryHeroToggle();

            if (!IsCurrentHeroParagonEnabled)
                return;

            DrawGlobalWarning();

            if (!ShowOverlay || !IsParagonOpen())
                return;

            _profileSaveButtonRect = GetProfileSaveButtonRect();
            if (_profileSaveButtonRect.Width <= 0)
                return;

            int cursorX = SafeCursorX();
            int cursorY = SafeCursorY();
            bool hover = PointInRect(_profileSaveButtonRect, cursorX, cursorY);
            IBrush back = hover ? _saveButtonHoverBrush : _saveButtonBackBrush;
            if (back != null) back.DrawRectangle(_profileSaveButtonRect);
            if (_saveButtonBorderBrush != null) _saveButtonBorderBrush.DrawRectangle(_profileSaveButtonRect);

            string buildName = string.IsNullOrEmpty(_currentBuildDisplayLabel)
                ? "current build"
                : _currentBuildDisplayLabel;
            string label = string.Equals(buildName, "current build",
                    StringComparison.OrdinalIgnoreCase)
                ? "Save current build profile"
                : "Save to build: \"" + buildName + "\"";

            if (_smallFont != null)
                _smallFont.DrawText(label, _profileSaveButtonRect.Left + 9, _profileSaveButtonRect.Top + 5);

            System.Drawing.RectangleF coreToggle;
            System.Drawing.RectangleF vitalityToggle;
            GetOverflowToggleRects(out coreToggle, out vitalityToggle);
            DrawOverflowPrompt(coreToggle, vitalityToggle);
            int selectedOverflow = GetDisplayedOverflowRow();
            DrawOverflowToggle(coreToggle, "Core stat", selectedOverflow == PrimaryRow,
                PointInRect(coreToggle, cursorX, cursorY));
            DrawOverflowToggle(vitalityToggle, "Vitality", selectedOverflow == VitalityRow,
                PointInRect(vitalityToggle, cursorX, cursorY));

            string status = GetStatus();
            if (!string.IsNullOrEmpty(status))
            {
                if (_instructionFont != null)
                    _instructionFont.DrawText(status, _profileSaveButtonRect.Left, _profileSaveButtonRect.Top - 19);
            }
            else
            {
                bool profileSaved = !string.IsNullOrEmpty(_currentBuildKey) &&
                    FindCurrentProfile(_currentBuildKey) != null;
                IFont stateFont = profileSaved ? _profileSavedFont : _profileMissingFont;
                if (stateFont != null)
                    stateFont.DrawText(profileSaved ? "Profile Saved" : "No Profile Saved",
                        _profileSaveButtonRect.Left, _profileSaveButtonRect.Top - 19);
            }
        }

        private void HandlePhysicalMouseDown(int now)
        {
            int x = SafeCursorX();
            int y = SafeCursorY();

            if (IsParagonOpen())
            {
                if (TrySelectOverflowByToggleClick(x, y))
                    return;

                System.Drawing.RectangleF saveButton = GetProfileSaveButtonRect();
                if (PointInRect(saveButton, x, y))
                {
                    BeginProfileSave(now);
                    return;
                }

                // Any manual Paragon edit makes the persisted current-layout
                // assumption unsafe until the custom Save button captures it.
                for (int row = 0; row < 4; row++)
                {
                    if (PointInRect(SafeRect(_plusButtons[row]), x, y))
                    {
                        CancelNativeCapture();
                        InvalidateKnownCurrentLayout();
                        return;
                    }
                }
                if (PointInRect(SafeRect(_resetButton), x, y) ||
                    PointInRect(SafeRect(_acceptButton), x, y))
                {
                    CancelNativeCapture();
                    InvalidateKnownCurrentLayout();
                }

                return;
            }

            if (!IsArmoryOpen() || !AutoApplyOnArmoryEquip)
                return;

            if (PointInRect(GetArmoryEquipRect(), x, y))
            {
                ResetNativeTask();
                ArmoryEquipClicked(now);
            }
        }

        private void BeginProfileSave(int now)
        {
            if (!IsCurrentHeroParagonEnabled ||
                _applyState != ApplyState.Idle ||
                _scanRunning ||
                !IsParagonOpen())
                return;

            uint heroId = CurrentHeroId;
            string liveKey = GetLiveBuildKey();
            string baseKey = GetLiveArmoryKey();
            string stableKey = GetStableBuildKey();

            if (heroId == 0u ||
                string.IsNullOrEmpty(liveKey) ||
                string.IsNullOrEmpty(baseKey) ||
                string.IsNullOrEmpty(stableKey))
            {
                ShowStatus("Current build is not readable yet.");
                return;
            }

            if (_activeArmoryIndex < 0 ||
                !CurrentBuildMatchesArmoryIndex(
                    _activeArmoryIndex, baseKey))
            {
                ShowStatus("Equip the Armory build once before saving.");
                return;
            }

            if (_equipPending)
            {
                int equipAge = _equipClickTick == NoTick
                    ? 0
                    : unchecked(now - _equipClickTick);
                int stableAge = _equipStableSinceTick == NoTick
                    ? 0
                    : unchecked(now - _equipStableSinceTick);

                if (!string.Equals(liveKey, _equipStableKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(baseKey, _equipStableBaseKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(stableKey, _equipStableStableKey,
                        StringComparison.Ordinal) ||
                    _equipStableSamples < BuildStableSamples ||
                    stableAge < ProfileMatchStableMs ||
                    equipAge < ArmoryMinimumWaitMs)
                {
                    ShowStatus(
                        "Build is still settling. Try Save again shortly.");
                    return;
                }
            }

            IPlayerArmorySet set =
                GetArmorySetByIndex(_activeArmoryIndex);

            _profileSaveHeroId = heroId;
            _profileSaveLiveKey = liveKey;
            _profileSaveBaseKey = baseKey;
            _profileSaveStableKey = stableKey;
            _profileSaveArmoryIndex = _activeArmoryIndex;
            _profileSaveLabel = set != null &&
                !string.IsNullOrEmpty(set.Name)
                    ? set.Name
                    : (_activeArmoryName ?? string.Empty);

            if (string.IsNullOrEmpty(_profileSaveLabel))
                _profileSaveLabel = "Armory build";

            _profileSaveAfterScan = true;
            BeginFastCaptureScan(now);
        }

        private void FinalizeProfileSave(int now)
        {
            if (!_profileSaveAfterScan)
                return;

            _profileSaveAfterScan = false;

            string liveKey = GetLiveBuildKey();
            string baseKey = GetLiveArmoryKey();
            string stableKey = GetStableBuildKey();

            if (CurrentHeroId != _profileSaveHeroId ||
                !string.Equals(liveKey, _profileSaveLiveKey,
                    StringComparison.Ordinal) ||
                !string.Equals(baseKey, _profileSaveBaseKey,
                    StringComparison.Ordinal) ||
                !string.Equals(stableKey, _profileSaveStableKey,
                    StringComparison.Ordinal) ||
                !CurrentBuildMatchesArmoryIndex(
                    _profileSaveArmoryIndex, baseKey))
            {
                ClearProfileSaveRequest();
                ShowStatus("Build changed; profile was not saved.");
                return;
            }

            ParagonProfile profile;
            if (!TryCreateProfileFromCapture(liveKey, out profile))
            {
                ClearProfileSaveRequest();
                ShowStatus("Could not read all four Paragon tabs.");
                return;
            }

            profile.HeroId = _profileSaveHeroId;
            profile.LiveSignature = liveKey;
            profile.BaseKey = baseKey;
            profile.StableBuildKey = stableKey;
            profile.ArmoryIndex = _profileSaveArmoryIndex;
            profile.Label = string.IsNullOrEmpty(_profileSaveLabel)
                ? "Armory build"
                : _profileSaveLabel;
            profile.OverflowRow = NormalizeOverflowRow(
                _currentOverflowRow, profile.Rows);
            profile.OverflowExplicit = _currentOverflowExplicit;
            profile.ProfileId = MakeProfileId(profile);
            profile.UpdatedUtcTicks = DateTime.UtcNow.Ticks;

            if (!ClickUi(_acceptButton, ClickModifier.None))
            {
                ClearProfileSaveRequest();
                ShowStatus(
                    "Accept was not available; profile was not saved.");
                return;
            }

            RemoveSupersededProfiles(profile);
            _profiles[profile.ProfileId] = CloneProfile(profile);
            _currentOverflowRow = profile.OverflowRow;
            _currentStableBuildKey = stableKey;
            SetKnownCurrentLayout(profile);
            SaveSettings();
            ScheduleNativeFingerprintCapture(profile, now);
            ClearWarning();

            ShowStatus("Paragon profile saved: " + profile.Label);
            MoveCursorToCloseButton();
            ClearProfileSaveRequest();
        }

        private void ClearProfileSaveRequest()
        {
            ClearProfileSaveRequest(true);
        }

        private void ClearProfileSaveRequest(bool clearIdentity)
        {
            _profileSaveAfterScan = false;
            if (!clearIdentity)
                return;
            _profileSaveHeroId = 0u;
            _profileSaveLiveKey = string.Empty;
            _profileSaveBaseKey = string.Empty;
            _profileSaveStableKey = string.Empty;
            _profileSaveArmoryIndex = -1;
            _profileSaveLabel = string.Empty;
        }

        private void ArmoryEquipClicked(int now)
        {
            if (!IsCurrentHeroParagonEnabled)
                return;

            int selectedIndex;
            string selectedName;
            if (!TryReadNativeArmorySelection(
                    out selectedIndex,
                    out selectedName))
            {
                ShowWarning(
                    "The selected Armory preset could not be read. " +
                    "Select the preset again, then press Equip.");
                return;
            }

            System.Drawing.RectangleF nativeEquipRect;
            int nativeEquipState;
            bool nativeEquipAvailable =
                TryGetNativeEquipState(
                    out nativeEquipRect,
                    out nativeEquipState);

            // State 72 is Diablo's disabled/current/busy state. A physical
            // click there is not a new Equip request and must not manufacture
            // another settlement or Paragon transaction.
            if (nativeEquipAvailable &&
                nativeEquipState == NativeEquipDisabledAnim)
            {
                return;
            }

            // Ignore a duplicate click on the same pending preset. This also
            // prevents clicks on Diablo's greyed Equip button from restarting
            // a transaction that is still waiting for socket repair.
            if (_equipPending &&
                _equipRequestedArmoryIndex == selectedIndex)
            {
                return;
            }

            if (_applyState != ApplyState.Idle)
                AbortApply("new Armory Equip click");

            string baseKey =
                GetLiveArmoryKey();
            string stableKey =
                GetStableBuildKey();

            // Diablo greys Equip when this exact preset is already active.
            // If the native slot, live Armory layout, and saved stable build
            // agree, treat the click as an apply/no-op request without
            // manufacturing another Armory settlement transaction.
            if (!_equipPending &&
                selectedIndex == _activeArmoryIndex &&
                CurrentBuildMatchesArmoryIndex(
                    selectedIndex,
                    baseKey))
            {
                ParagonProfile current =
                    FindProfileByArmoryIndex(
                        CurrentHeroId,
                        selectedIndex);

                if (current != null &&
                    !string.IsNullOrEmpty(stableKey) &&
                    string.Equals(
                        current.StableBuildKey,
                        stableKey,
                        StringComparison.Ordinal))
                {
                    CopyProfileToCapture(current);
                    ClearWarning();

                    ResolveProfileAfterEquip(current, now);
                    return;
                }
            }

            _equipRequestedArmoryIndex = selectedIndex;
            _equipRequestedArmoryName = selectedName;
            _equipPending = true;
            _equipClickTick = now;
            _equipNativeStateObserved =
                nativeEquipAvailable &&
                IsNativeEquipInteractiveState(
                    nativeEquipState);
            _equipNativeAccepted = false;
            ResetEquipStability();
            ClearWarning();
        }

        private void ProcessPendingEquip(int now)
        {
            if (!_equipPending ||
                _applyState != ApplyState.Idle)
                return;

            int elapsed = unchecked(now - _equipClickTick);
            if (elapsed >= EquipDetectTimeoutMs)
            {
                ClearPendingEquip();
                ShowWarning(
                    "The Armory build did not finish settling. " +
                    "Paragon was left unchanged.");
                return;
            }

            if (elapsed < ArmoryMinimumWaitMs)
                return;

            if (_equipRequestedArmoryIndex < 0)
            {
                ClearPendingEquip();
                return;
            }

            string liveKey = GetLiveBuildKey();
            string baseKey = GetLiveArmoryKey();
            string stableKey = GetStableBuildKey();

            if (string.IsNullOrEmpty(liveKey) ||
                string.IsNullOrEmpty(baseKey) ||
                string.IsNullOrEmpty(stableKey))
            {
                ResetEquipStability();
                return;
            }

            if (!CurrentBuildMatchesArmoryIndex(
                    _equipRequestedArmoryIndex,
                    baseKey))
            {
                // Native Armory loading or a separate repair plugin has not
                // finished replacing the transitional equipment state.
                ResetEquipStability();
                return;
            }

            if (!string.Equals(liveKey, _equipStableKey,
                    StringComparison.Ordinal) ||
                !string.Equals(baseKey, _equipStableBaseKey,
                    StringComparison.Ordinal) ||
                !string.Equals(stableKey, _equipStableStableKey,
                    StringComparison.Ordinal))
            {
                _equipStableKey = liveKey;
                _equipStableBaseKey = baseKey;
                _equipStableStableKey = stableKey;
                _equipStableSinceTick = now;
                _equipStableSamples = 1;

                UpdateEquipNativeStability(
                    FindProfileByArmoryIndex(
                        CurrentHeroId,
                        _equipRequestedArmoryIndex),
                    stableKey,
                    now);
                return;
            }

            _equipStableSamples++;

            int stableAge = _equipStableSinceTick == NoTick
                ? 0
                : unchecked(now - _equipStableSinceTick);

            // The native button becoming disabled proves that Diablo accepted
            // the selected preset. It is not sufficient by itself to prove
            // socket repair is complete, so the stable build/socket checks
            // below remain authoritative. If the native element disappears,
            // preserve the existing compatibility fallback.
            if (_equipNativeStateObserved &&
                !_equipNativeAccepted)
            {
                System.Drawing.RectangleF nativeRect;
                int nativeState;

                if (TryGetNativeEquipState(
                        out nativeRect,
                        out nativeState))
                {
                    if (nativeState ==
                        NativeEquipDisabledAnim)
                    {
                        _equipNativeAccepted = true;

                        // Begin the final stability window after Diablo has
                        // accepted the preset. The grey state is not proof of
                        // completed socket repair, so require a fresh stable
                        // build/socket sample after this transition.
                        ResetEquipStability();
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            ParagonProfile profile =
                FindProfileByArmoryIndex(
                    CurrentHeroId,
                    _equipRequestedArmoryIndex);

            UpdateEquipNativeStability(
                profile,
                stableKey,
                now);

            bool expectedBuildMismatch =
                profile != null &&
                !string.IsNullOrEmpty(profile.StableBuildKey) &&
                !string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal);

            int requiredStableMs =
                expectedBuildMismatch
                    ? ProfileMismatchStableMs
                    : ProfileMatchStableMs;

            // A transient socket state can look like a different build for
            // more than a second while ArmoryBugFix is still resocketing gems.
            // Wait through that handoff. A valid saved build proceeds as soon
            // as its expected stable key appears and remains stable.
            if (expectedBuildMismatch &&
                elapsed < ProfileMismatchGraceMs)
            {
                return;
            }

            if (_equipStableSinceTick == NoTick ||
                _equipStableSamples < BuildStableSamples ||
                stableAge < requiredStableMs ||
                elapsed < ArmoryMinimumWaitMs)
            {
                return;
            }

            NativeFingerprint settledNative =
                GetSettledEquipNativeFingerprint(
                    profile,
                    now);

            int armoryIndex = _equipRequestedArmoryIndex;
            string buildLabel = _equipRequestedArmoryName;
            IPlayerArmorySet selectedSet =
                GetArmorySetByIndex(armoryIndex);

            if (selectedSet != null &&
                !string.IsNullOrEmpty(selectedSet.Name))
            {
                buildLabel = selectedSet.Name;
            }

            ClearPendingEquip();
            SetActiveArmoryBuild(
                armoryIndex,
                buildLabel,
                stableKey);

            if (profile == null)
            {
                profile = TryMigrateLegacyProfile(
                    armoryIndex,
                    buildLabel,
                    liveKey,
                    baseKey,
                    stableKey);
            }

            if (profile == null)
            {
                ClearCapturedProfile();
                ShowWarning(
                    "No Paragon profile is saved for \"" +
                    SafeBuildLabel(buildLabel) +
                    "\". Check the points, then save it once.");
                return;
            }

            string validationMessage;
            if (!ValidateProfileBuild(
                    profile,
                    liveKey,
                    stableKey,
                    out validationMessage))
            {
                ClearCapturedProfile();
                ShowWarning(validationMessage);
                return;
            }

            UpdateProfileRuntimeMetadata(
                profile,
                liveKey,
                baseKey,
                buildLabel);

            CopyProfileToCapture(profile);
            ClearWarning();

            ResolveProfileAfterEquip(
                profile,
                now,
                settledNative);
        }

        private void StartApply(
            ParagonProfile profile,
            int now)
        {
            if (!IsCurrentHeroParagonEnabled)
                return;

            ResetNativeTask();

            if (profile == null ||
                profile.HeroId != CurrentHeroId ||
                !CanAutomate())
                return;

            string stableKey =
                GetStableBuildKey();

            if (string.IsNullOrEmpty(stableKey) ||
                !string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal))
            {
                ShowWarning(
                    "The equipped build changed before Paragon " +
                    "could be applied.");
                return;
            }

            _applyProfile = CloneProfile(profile);
            _currentOverflowRow =
                _applyProfile.OverflowRow;
            _currentOverflowExplicit =
                _applyProfile.OverflowExplicit;
            _applyTab = 0;
            _applyOrderIndex = 0;
            _applyCurrentRow = -1;
            _applyCurrentIsOverflow = false;
            _applyNextTick = now;
            _applyDeadlineTick = NoTick;
            _applyNoProgressRetries = 0;
            _applyClickCount = 0;
            _applyResetAttempts = 0;
            _applyChanged = false;
            _applyEscapedArmory = false;
            _scanRunning = false;
            RestoreScanCursor();
            CaptureCursorForRestore();

            _applyState = IsParagonOpen()
                ? ApplyState.SelectTab
                : ApplyState.OpenParagon;
        }

        private void ProcessApply(int now, bool leftDown)
        {
            if (_applyState == ApplyState.Idle)
                return;

            if (!CanAutomate())
            {
                AbortApply("unsafe game state");
                return;
            }

            if (leftDown)
                return;

            bool ownsModifierState =
                _applyState == ApplyState.BurstModifierLead ||
                _applyState == ApplyState.BurstClick ||
                _applyState == ApplyState.BurstModifierTail;

            if (!ownsModifierState &&
                (IsVirtualKeyDown(VK_CONTROL) || IsVirtualKeyDown(VK_SHIFT)))
                return;

            if (_applyClickCount >= MaxAutomationClicks)
            {
                AbortApply("click limit reached");
                return;
            }

            if (!TickReached(now, _applyNextTick))
                return;

            switch (_applyState)
            {
                case ApplyState.OpenParagon:
                    if (IsParagonOpen())
                    {
                        _applyState = ApplyState.SelectTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }

                    if (IsArmoryOpen() && !_applyEscapedArmory)
                    {
                        PressVirtualKey(Keys.Escape);
                        _applyEscapedArmory = true;
                        _applyNextTick = unchecked(now + 32);
                        return;
                    }

                    PressVirtualKey(ParagonWindowKey);
                    _applyClickCount++;
                    _applyDeadlineTick = unchecked(now + OpenParagonTimeoutMs);
                    _applyState = ApplyState.WaitParagon;
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.WaitParagon:
                    if (IsParagonOpen())
                    {
                        _applyState = ApplyState.SelectTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (TickReached(now, _applyDeadlineTick))
                    {
                        AbortApply("Paragon window did not open");
                        return;
                    }
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.SelectTab:
                    if (!IsParagonOpen())
                    {
                        AbortApply("Paragon window closed");
                        return;
                    }
                    if (_applyTab >= 4)
                    {
                        _applyState = _applyChanged ? ApplyState.Accept : ApplyState.CloseParagon;
                        _applyNextTick = now;
                        return;
                    }
                    if (GetActiveTab() == _applyTab)
                    {
                        _applyState = ApplyState.CheckTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (!ClickUi(_tabs[_applyTab], ClickModifier.None))
                    {
                        AbortApply("could not open " + TabNames[_applyTab]);
                        return;
                    }
                    _applyClickCount++;
                    _applyState = ApplyState.WaitTab;
                    _applyDeadlineTick = unchecked(now + 800);
                    _applyNextTick = unchecked(now + TabSettleMs);
                    return;

                case ApplyState.WaitTab:
                    if (GetActiveTab() == _applyTab && RowsReadable())
                    {
                        _applyState = ApplyState.CheckTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (TickReached(now, _applyDeadlineTick))
                    {
                        AbortApply("tab did not load: " + TabNames[_applyTab]);
                        return;
                    }
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.CheckTab:
                    CaptureActiveTab(now);
                    TabPlan plan = EvaluateCurrentTabPlan(_applyTab, _applyProfile);
                    if (plan == TabPlan.Invalid)
                    {
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (plan == TabPlan.Match)
                    {
                        _applyTab++;
                        _applyState = ApplyState.SelectTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (plan == TabPlan.Reset)
                    {
                        _applyResetAttempts = 0;
                        _applyState = ApplyState.ResetTab;
                        _applyNextTick = now;
                        return;
                    }
                    PrepareRowOrder();
                    _applyState = ApplyState.PrepareRow;
                    _applyNextTick = now;
                    return;

                case ApplyState.ResetTab:
                    if (!ClickUi(_resetButton, ClickModifier.None))
                    {
                        AbortApply("could not reset " + TabNames[_applyTab]);
                        return;
                    }
                    _applyClickCount++;
                    _applyChanged = true;
                    _applyResetAttempts++;
                    _applyState = ApplyState.WaitReset;
                    _applyDeadlineTick = unchecked(now + ResetTimeoutMs);
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.WaitReset:
                    if (GetActiveTab() != _applyTab)
                    {
                        _applyState = ApplyState.SelectTab;
                        _applyNextTick = now;
                        return;
                    }
                    if (RowsAreZero())
                    {
                        CaptureActiveTab(now);
                        PrepareRowOrder();
                        _applyState = ApplyState.PrepareRow;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (TickReached(now, _applyDeadlineTick))
                    {
                        if (_applyResetAttempts < 2)
                        {
                            _applyState = ApplyState.ResetTab;
                            _applyNextTick = now;
                            return;
                        }
                        AbortApply("reset did not register: " + TabNames[_applyTab]);
                        return;
                    }
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.PrepareRow:
                    if (_applyOrderIndex >= 4)
                    {
                        _applyTab++;
                        _applyState = ApplyState.SelectTab;
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }

                    _applyCurrentRow = _applyRowOrder[_applyOrderIndex];
                    _applyCurrentIsOverflow =
                        _applyTab == CoreTab && _applyCurrentRow == _applyProfile.OverflowRow;

                    if (_applyCurrentIsOverflow)
                    {
                        _applyState = ApplyState.SpendOverflow;
                        _applyNextTick = now;
                        return;
                    }

                    RowState fixedRow = GetRowState(_applyCurrentRow);
                    int fixedDesired = _applyProfile.Rows[_applyTab, _applyCurrentRow];
                    if (!fixedRow.Valid)
                    {
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    if (fixedRow.Assigned >= fixedDesired)
                    {
                        _applyOrderIndex++;
                        _applyNextTick = now;
                        return;
                    }

                    int fixedAvailable = ParseAvailable(_applyTab);
                    if (fixedAvailable <= 0)
                    {
                        AbortApply("not enough " + TabNames[_applyTab] + " points");
                        return;
                    }

                    int delta = fixedDesired - fixedRow.Assigned;
                    ClickModifier fixedModifier;
                    int fixedClicks;
                    BuildExactBurstPlan(fixedRow, fixedDesired, delta, out fixedModifier, out fixedClicks);
                    if (!StartPointBurst(now, _applyCurrentRow, fixedModifier, fixedClicks, false, ApplyState.PrepareRow))
                    {
                        AbortApply("could not spend " + TabNames[_applyTab] + " row " + _applyCurrentRow);
                        return;
                    }
                    return;

                case ApplyState.SpendOverflow:
                    int available = ParseAvailable(_applyTab);
                    if (available <= 0)
                    {
                        _applyOrderIndex++;
                        _applyState = ApplyState.PrepareRow;
                        _applyNextTick = now;
                        return;
                    }
                    RowState overflow = GetRowState(_applyCurrentRow);
                    if (!overflow.Valid)
                    {
                        _applyNextTick = unchecked(now + FastActionMs);
                        return;
                    }
                    int overflowBudget = (available + 99) / 100 + BurstClickBudgetMargin;
                    if (!StartPointBurst(now, _applyCurrentRow, ClickModifier.Control,
                        overflowBudget, true, ApplyState.SpendOverflow))
                    {
                        AbortApply("could not spend Core overflow");
                        return;
                    }
                    return;

                case ApplyState.BurstModifierLead:
                    _applyState = ApplyState.BurstClick;
                    _applyNextTick = now;
                    return;

                case ApplyState.BurstClick:
                    RowState burstRow = GetRowState(_applyCurrentRow);
                    int burstAvailable = ParseAvailable(_applyTab);
                    if (!burstRow.Valid || burstAvailable < 0)
                    {
                        FinishPointBurst(now);
                        return;
                    }

                    if (burstRow.Assigned != _burstLastAssigned ||
                        burstAvailable != _burstLastAvailable)
                    {
                        _burstLastAssigned = burstRow.Assigned;
                        _burstLastAvailable = burstAvailable;
                        _burstLastProgressTick = now;
                    }

                    bool burstDone = _burstOverflow
                        ? burstAvailable <= 0
                        : _burstClicksSent >= _burstClicksPlanned;
                    if (burstDone)
                    {
                        FinishPointBurst(now);
                        return;
                    }

                    if (_burstClicksSent > 0 && _burstLastProgressTick != NoTick &&
                        unchecked(now - _burstLastProgressTick) >= ClickProgressTimeoutMs)
                    {
                        FinishPointBurst(now);
                        return;
                    }

                    if (!RawMouseClickAt(_burstPointX, _burstPointY))
                    {
                        AbortApply("point burst click failed");
                        return;
                    }
                    _applyClickCount++;
                    _burstClicksSent++;
                    _applyChanged = true;
                    _applyNextTick = unchecked(now + PointClickIntervalMs);
                    return;

                case ApplyState.BurstModifierTail:
                    ReleasePendingModifier();
                    _applyState = ApplyState.BurstSettle;
                    _applyNextTick = unchecked(now + BurstSettleMs);
                    return;

                case ApplyState.BurstSettle:
                    RowState settledRow = GetRowState(_applyCurrentRow);
                    int settledAvailable = ParseAvailable(_applyTab);
                    bool progressed = settledRow.Valid &&
                        (settledRow.Assigned != _burstBeforeAssigned ||
                         settledAvailable != _burstBeforeAvailable);

                    if (!progressed)
                    {
                        _applyNoProgressRetries++;
                        if (_applyNoProgressRetries > MaxNoProgressRetries)
                        {
                            AbortApply("point burst did not register");
                            return;
                        }
                    }
                    else
                    {
                        _applyNoProgressRetries = 0;
                    }

                    _applyState = _burstReturnState;
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;

                case ApplyState.Accept:
                    if (!ClickUi(_acceptButton, ClickModifier.None))
                    {
                        AbortApply("Accept button was not clickable");
                        return;
                    }
                    _applyClickCount++;
                    _applyState = ApplyState.WaitAccept;
                    _applyNextTick = unchecked(now + AcceptSettleMs);
                    return;

                case ApplyState.WaitAccept:
                    CopyProfileToCapture(_applyProfile);
                    _applyState = ApplyState.CloseParagon;
                    _applyNextTick = now;
                    return;

                case ApplyState.CloseParagon:
                    if (!IsParagonOpen())
                    {
                        CompleteApply();
                        return;
                    }
                    PressVirtualKey(ParagonWindowKey);
                    _applyClickCount++;
                    _applyState = ApplyState.WaitClose;
                    _applyDeadlineTick = unchecked(now + 1000);
                    _applyNextTick = unchecked(now + CloseSettleMs);
                    return;

                case ApplyState.WaitClose:
                    if (!IsParagonOpen() || TickReached(now, _applyDeadlineTick))
                    {
                        CompleteApply();
                        return;
                    }
                    _applyNextTick = unchecked(now + FastActionMs);
                    return;
            }
        }

        private bool StartPointBurst(
            int now,
            int row,
            ClickModifier modifier,
            int clickCount,
            bool overflow,
            ApplyState returnState)
        {
            if (clickCount <= 0)
                return false;

            RowState before = GetRowState(row);
            int available = ParseAvailable(_applyTab);
            System.Drawing.RectangleF rect = SafeRect(_plusButtons[row]);
            if (!before.Valid || available <= 0 || !IsVisible(_plusButtons[row]) ||
                rect.Width <= 0 || rect.Height <= 0)
                return false;

            _burstPointX = (int)Math.Round(rect.Left + rect.Width * 0.5f);
            _burstPointY = (int)Math.Round(rect.Top + rect.Height * 0.5f);
            _burstModifier = modifier;
            _burstClicksPlanned = clickCount;
            _burstClicksSent = 0;
            _burstOverflow = overflow;
            _burstBeforeAssigned = before.Assigned;
            _burstBeforeAvailable = available;
            _burstLastAssigned = before.Assigned;
            _burstLastAvailable = available;
            _burstLastProgressTick = now;
            _burstReturnState = returnState;

            if (modifier == ClickModifier.Control || modifier == ClickModifier.Shift)
            {
                _pendingModifierOwned = true;
                if (modifier == ClickModifier.Control) KeyDown(VK_CONTROL);
                else KeyDown(VK_SHIFT);
                _applyState = ApplyState.BurstModifierLead;
                _applyNextTick = unchecked(now + ModifierLeadMs);
            }
            else
            {
                _applyState = ApplyState.BurstClick;
                _applyNextTick = now;
            }

            return true;
        }

        private void PrepareRowOrder()
        {
            _applyOrderIndex = 0;
            if (_applyTab == CoreTab)
            {
                int overflow = NormalizeOverflowRow(_applyProfile.OverflowRow, _applyProfile.Rows);
                _applyProfile.OverflowRow = overflow;
                int fixedCore = overflow == PrimaryRow ? VitalityRow : PrimaryRow;
                _applyRowOrder[0] = 2;
                _applyRowOrder[1] = 3;
                _applyRowOrder[2] = fixedCore;
                _applyRowOrder[3] = overflow;
            }
            else
            {
                _applyRowOrder[0] = 0;
                _applyRowOrder[1] = 1;
                _applyRowOrder[2] = 2;
                _applyRowOrder[3] = 3;
            }
        }

        private TabPlan EvaluateCurrentTabPlan(int tab, ParagonProfile profile)
        {
            if (GetActiveTab() != tab || !RowsReadable())
                return TabPlan.Invalid;

            int available = ParseAvailable(tab);
            if (available < 0)
                return TabPlan.Invalid;

            if (tab == CoreTab)
            {
                int overflow = NormalizeOverflowRow(profile.OverflowRow, profile.Rows);
                int neededFixedPoints = 0;
                bool fixedExact = true;

                for (int row = 0; row < 4; row++)
                {
                    RowState current = GetRowState(row);
                    if (!current.Valid) return TabPlan.Invalid;
                    if (row == overflow) continue;

                    int desired = profile.Rows[tab, row];
                    if (current.Assigned > desired)
                        return TabPlan.Reset;
                    if (current.Assigned < desired)
                    {
                        fixedExact = false;
                        neededFixedPoints += desired - current.Assigned;
                    }
                }

                if (fixedExact)
                    return available == 0 ? TabPlan.Match : TabPlan.Spend;

                // If the required fixed points are currently trapped in the
                // overflow row, a reset is necessary before redistribution.
                if (available < neededFixedPoints)
                    return TabPlan.Reset;

                return TabPlan.Spend;
            }

            bool exact = true;
            for (int row = 0; row < 4; row++)
            {
                RowState current = GetRowState(row);
                if (!current.Valid) return TabPlan.Invalid;
                int desired = profile.Rows[tab, row];
                if (current.Assigned > desired) return TabPlan.Reset;
                if (current.Assigned != desired) exact = false;
            }
            return exact ? TabPlan.Match : TabPlan.Spend;
        }

        private static void BuildExactBurstPlan(
            RowState row,
            int desired,
            int delta,
            out ClickModifier modifier,
            out int clickCount)
        {
            modifier = ClickModifier.None;
            clickCount = 0;
            if (delta <= 0)
                return;

            if (row.HasCap && row.Cap > 0 && desired >= row.Cap)
            {
                modifier = ClickModifier.Control;
                clickCount = 1;
                return;
            }

            if (delta >= 100)
            {
                modifier = ClickModifier.Control;
                clickCount = delta / 100;
                return;
            }

            if (delta >= 10)
            {
                modifier = ClickModifier.Shift;
                clickCount = delta / 10;
                return;
            }

            clickCount = delta;
        }

        private void FinishPointBurst(int now)
        {
            if (_pendingModifierOwned)
            {
                _applyState = ApplyState.BurstModifierTail;
                _applyNextTick = unchecked(now + ModifierTailMs);
            }
            else
            {
                _applyState = ApplyState.BurstSettle;
                _applyNextTick = unchecked(now + BurstSettleMs);
            }
        }

        private void CompleteApply()
        {
            ParagonProfile completed =
                _applyProfile != null
                    ? CloneProfile(_applyProfile)
                    : null;

            if (completed != null)
            {
                SetKnownCurrentLayout(completed);
                SaveSettings();
            }

            ResetApplyState();
            RestoreCapturedCursor();

            if (completed != null)
                ScheduleNativeFingerprintCapture(
                    completed,
                    Environment.TickCount);
        }

        private void AbortApply(string reason)
        {
            if (_applyState == ApplyState.Idle)
                return;

            // Automation may already have changed one or more Paragon rows.
            // Never allow a later equivalent-layout check to trust a cached
            // pre-apply layout after an interrupted or failed application.
            InvalidateKnownCurrentLayout();

            ResetApplyState();
            RestoreCapturedCursor();
        }

        private void ResetApplyState()
        {
            _applyState = ApplyState.Idle;
            _applyProfile = null;
            _applyTab = 0;
            _applyOrderIndex = 0;
            _applyCurrentRow = -1;
            _applyCurrentIsOverflow = false;
            _applyNextTick = NoTick;
            _applyDeadlineTick = NoTick;
            _applyNoProgressRetries = 0;
            _applyClickCount = 0;
            _applyResetAttempts = 0;
            _applyChanged = false;
            ReleasePendingModifier();
            _burstModifier = ClickModifier.None;
            _burstPointX = 0;
            _burstPointY = 0;
            _burstClicksPlanned = 0;
            _burstClicksSent = 0;
            _burstOverflow = false;
            _burstBeforeAssigned = 0;
            _burstBeforeAvailable = 0;
            _burstLastAssigned = 0;
            _burstLastAvailable = 0;
            _burstLastProgressTick = NoTick;
            _burstReturnState = ApplyState.Idle;
            _applyEscapedArmory = false;
        }

        private void OnParagonOpened(int now)
        {
            if (_applyState != ApplyState.Idle)
                return;

            // Quiet background mode: opening Paragon never drives the tabs.
            // The active tab is captured continuously, and a fast complete scan
            // runs only after the user presses Accept.
            _scanRunning = false;
            _scanOriginalTab = -1;
            _scanRestoringCore = false;
            _scanTab = 0;
            _scanNextTick = NoTick;
            CaptureActiveTab(now);
        }

        private void OnParagonClosed()
        {
            _scanRunning = false;
            _scanRestoringCore = false;
            _scanNextTick = NoTick;
            _profileSaveAfterScan = false;
            RestoreScanCursor();
        }

        private void BeginFastCaptureScan(int now)
        {
            if (_applyState != ApplyState.Idle || !IsParagonOpen())
                return;

            _scanRunning = true;
            _scanOriginalTab = GetActiveTab();
            if (_scanOriginalTab < 0) _scanOriginalTab = 0;
            _scanRestoringCore = false;
            _scanTab = 0;
            _scanNextTick = now;
            for (int tab = 0; tab < 4; tab++)
                _scanTabsSeen[tab] = false;
            CaptureActiveTab(now);
            CaptureScanCursor();
        }

        private void ProcessCaptureScan(int now, bool leftDown)
        {
            if (!_scanRunning || _applyState != ApplyState.Idle || !IsParagonOpen())
                return;

            if (leftDown)
            {
                _scanNextTick = unchecked(now + FastActionMs);
                return;
            }

            if (!TickReached(now, _scanNextTick))
                return;

            while (_scanTab < 4 && _scanTabsSeen[_scanTab])
                _scanTab++;

            if (_scanTab >= 4)
            {
                // A profile Save always finishes on Core. Wait for the tab to
                // become active and readable before committing the profile.
                if (GetActiveTab() != CoreTab)
                {
                    if (ClickUi(_tabs[CoreTab], ClickModifier.None))
                        _scanRestoringCore = true;

                    _scanNextTick = unchecked(now + TabSettleMs);
                    return;
                }

                if (!RowsReadable())
                {
                    _scanNextTick = unchecked(now + FastActionMs);
                    return;
                }

                for (int tab = 0; tab < 4; tab++)
                {
                    for (int row = 0; row < 4; row++)
                        _capturedRows[tab, row] = _scanRows[tab, row];
                    _capturedAvailable[tab] = _scanAvailable[tab];
                    _capturedTabsSeen[tab] = _scanTabsSeen[tab];
                }

                _scanRunning = false;
                _scanRestoringCore = false;
                _scanOriginalTab = -1;
                _scanNextTick = NoTick;

                // The cursor is intentionally moved to Close after saving.
                _scanCursorCaptured = false;

                if (_profileSaveAfterScan)
                    FinalizeProfileSave(now);
                return;
            }

            if (GetActiveTab() != _scanTab)
            {
                if (ClickUi(_tabs[_scanTab], ClickModifier.None))
                {
                    _scanNextTick = unchecked(now + TabSettleMs);
                    return;
                }
                _scanNextTick = unchecked(now + FastActionMs);
                return;
            }

            if (CaptureActiveTab(now))
            {
                _scanTab++;
                _scanNextTick = unchecked(now + FastActionMs);
            }
            else
            {
                _scanNextTick = unchecked(now + FastActionMs);
            }
        }

        private bool CaptureActiveTab(int now)
        {
            if (!IsParagonOpen())
                return false;

            if (_continuousCaptureTick != NoTick && unchecked(now - _continuousCaptureTick) < 10)
                return false;
            _continuousCaptureTick = now;

            int tab = GetActiveTab();
            if (tab < 0 || tab > 3 || !RowsReadable())
                return false;

            int available = ParseAvailable(tab);
            if (available < 0)
                return false;

            int[] values = new int[4];
            for (int row = 0; row < 4; row++)
            {
                RowState state = GetRowState(row);
                if (!state.Valid)
                    return false;
                values[row] = state.Assigned;
            }

            if (_scanRunning)
            {
                for (int row = 0; row < 4; row++) _scanRows[tab, row] = values[row];
                _scanAvailable[tab] = available;
                _scanTabsSeen[tab] = true;
            }
            else
            {
                for (int row = 0; row < 4; row++) _capturedRows[tab, row] = values[row];
                _capturedAvailable[tab] = available;
                _capturedTabsSeen[tab] = true;
            }
            return true;
        }

        private bool TrySelectOverflowByToggleClick(int x, int y)
        {
            if (_applyState != ApplyState.Idle || _scanRunning || !IsParagonOpen())
                return false;

            System.Drawing.RectangleF coreToggle;
            System.Drawing.RectangleF vitalityToggle;
            GetOverflowToggleRects(out coreToggle, out vitalityToggle);

            if (PointInRect(coreToggle, x, y))
            {
                _currentOverflowRow = PrimaryRow;
                _currentOverflowExplicit = true;
                return true;
            }

            if (PointInRect(vitalityToggle, x, y))
            {
                _currentOverflowRow = VitalityRow;
                _currentOverflowExplicit = true;
                return true;
            }

            return false;
        }

        private int GetDisplayedOverflowRow()
        {
            if (_currentOverflowExplicit &&
                (_currentOverflowRow == PrimaryRow || _currentOverflowRow == VitalityRow))
                return _currentOverflowRow;

            int primary = 0;
            int vitality = 0;

            if (_capturedTabsSeen[CoreTab])
            {
                primary = _capturedRows[CoreTab, PrimaryRow];
                vitality = _capturedRows[CoreTab, VitalityRow];
            }
            else
            {
                ParagonProfile profile = FindCurrentProfile(_currentBuildKey);
                if (profile != null && profile.Rows != null)
                {
                    primary = profile.Rows[CoreTab, PrimaryRow];
                    vitality = profile.Rows[CoreTab, VitalityRow];
                }
            }

            return vitality > primary ? VitalityRow : PrimaryRow;
        }

        private bool TryCreateProfileFromCapture(string buildKey, out ParagonProfile profile)
        {
            profile = null;
            for (int tab = 0; tab < 4; tab++)
                if (!_capturedTabsSeen[tab]) return false;

            int[,] rows = new int[4, 4];
            for (int tab = 0; tab < 4; tab++)
                for (int row = 0; row < 4; row++)
                    rows[tab, row] = _capturedRows[tab, row];

            int overflow = NormalizeOverflowRow(_currentOverflowRow, rows);
            profile = new ParagonProfile
            {
                HeroId = CurrentHeroId,
                ProfileId = string.Empty,
                LiveSignature = buildKey,
                BaseKey = string.Empty,
                StableBuildKey = string.Empty,
                ArmoryIndex = -1,
                Label = "Armory build",
                Rows = rows,
                OverflowRow = overflow,
                OverflowExplicit = _currentOverflowExplicit,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            };
            return true;
        }

        private static int NormalizeOverflowRow(int selected, int[,] rows)
        {
            if (selected == PrimaryRow || selected == VitalityRow)
                return selected;

            int primary = rows != null ? rows[CoreTab, PrimaryRow] : 0;
            int vitality = rows != null ? rows[CoreTab, VitalityRow] : 0;

            if (primary > 0 && vitality <= 0) return PrimaryRow;
            if (vitality > 0 && primary <= 0) return VitalityRow;
            if (vitality > primary) return VitalityRow;
            return PrimaryRow;
        }

        private uint CurrentHeroId
        {
            get
            {
                try
                {
                    return Hud.Game.Me != null ? Hud.Game.Me.HeroId : 0u;
                }
                catch { return 0u; }
            }
        }

        private string GetLiveBuildKey()
        {
            return BuildLiveKey(true);
        }

        private string GetLiveArmoryKey()
        {
            return BuildLiveKey(false);
        }

        private string GetStableBuildKey()
        {
            try
            {
                IPlayer me = Hud.Game.Me;
                if (me == null || me.HeroClassDefinition == null)
                    return string.Empty;

                IItem[] equipped = GetEquippedItems();
                StringBuilder sb = new StringBuilder(560);

                sb.Append("class=").Append(
                    me.HeroClassDefinition.Code ??
                    me.HeroClassDefinition.Name ??
                    "?");

                sb.Append(";items=");
                for (int i = 0; i < equipped.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    IItem item = equipped[i];
                    sb.Append(item != null &&
                        item.SnoItem != null
                            ? item.SnoItem.Sno
                            : 0u);
                }

                AppendLivePowerKey(sb, me);

                uint potionPower =
                    me.Powers != null &&
                    me.Powers.HealthPotionSkill != null &&
                    me.Powers.HealthPotionSkill.SnoPower != null
                        ? me.Powers.HealthPotionSkill
                            .SnoPower.Sno
                        : 0u;

                sb.Append(";potionPower=")
                    .Append(potionPower);

                sb.Append(";sockets=");
                for (int i = 0; i < equipped.Length; i++)
                {
                    if (i > 0) sb.Append('|');
                    AppendSocketSignature(
                        sb,
                        equipped[i]);
                }

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private IItem[] GetEquippedItems()
        {
            IItem[] equipped = new IItem[13];

            try
            {
                foreach (IItem item in Hud.Game.Items)
                {
                    if (item == null ||
                        !IsEquipmentSlot(item.Location))
                        continue;

                    int index =
                        (int)item.Location -
                        (int)ItemLocation.Head;

                    if (index >= 0 &&
                        index < equipped.Length)
                    {
                        equipped[index] = item;
                    }
                }
            }
            catch { }

            return equipped;
        }

        private static void AppendLivePowerKey(
            StringBuilder sb,
            IPlayer me)
        {
            sb.Append(";cube=")
                .Append(Sno(me.CubeSnoItem1)).Append(',')
                .Append(Sno(me.CubeSnoItem2)).Append(',')
                .Append(Sno(me.CubeSnoItem3)).Append(',')
                .Append(Sno(me.CubeSnoItem4));

            uint[] powers = new uint[6];
            byte[] runes = new byte[6];
            IPlayerSkill[] slots =
                me.Powers != null
                    ? me.Powers.SkillSlots
                    : null;

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    IPlayerSkill skill = slots[i];
                    if (skill == null)
                        continue;

                    int index =
                        ActionSlotIndex(skill.Key);

                    if (index < 0 || index >= 6)
                        continue;

                    powers[index] =
                        skill.SnoPower != null
                            ? skill.SnoPower.Sno
                            : 0u;

                    runes[index] = skill.Rune;
                }
            }

            sb.Append(";skills=");
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(powers[i])
                    .Append(':')
                    .Append(runes[i]);
            }

            sb.Append(";passives=");
            ISnoPower[] passives =
                me.Powers != null
                    ? me.Powers.PassiveSlots
                    : null;

            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(
                    passives != null &&
                    i < passives.Length &&
                    passives[i] != null
                        ? passives[i].Sno
                        : 0u);
            }
        }

        private string BuildLiveKey(
            bool includeSockets)
        {
            try
            {
                IPlayer me = Hud.Game.Me;
                if (me == null ||
                    me.HeroClassDefinition == null)
                    return string.Empty;

                IItem[] equipped =
                    GetEquippedItems();
                StringBuilder sb =
                    new StringBuilder(520);

                sb.Append("class=").Append(
                    me.HeroClassDefinition.Code ??
                    me.HeroClassDefinition.Name ??
                    "?");

                sb.Append(";items=");
                for (int i = 0;
                    i < equipped.Length;
                    i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(
                        equipped[i] != null
                            ? equipped[i].AnnId
                            : 0u);
                }

                AppendLivePowerKey(sb, me);

                if (includeSockets)
                {
                    uint potionPower =
                        me.Powers != null &&
                        me.Powers
                            .HealthPotionSkill != null &&
                        me.Powers
                            .HealthPotionSkill
                            .SnoPower != null
                            ? me.Powers
                                .HealthPotionSkill
                                .SnoPower.Sno
                            : 0u;

                    sb.Append(";potionPower=")
                        .Append(potionPower);

                    sb.Append(";sockets=");
                    for (int i = 0;
                        i < equipped.Length;
                        i++)
                    {
                        if (i > 0) sb.Append('|');
                        AppendSocketSignature(
                            sb,
                            equipped[i]);
                    }
                }

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendSocketSignature(StringBuilder sb, IItem item)
        {
            if (item == null || item.ItemsInSocket == null || item.ItemsInSocket.Length == 0)
            {
                sb.Append('-');
                return;
            }

            IItem[] sockets = item.ItemsInSocket;
            for (int i = 0; i < sockets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                IItem socket = sockets[i];
                sb.Append(socket != null && socket.SnoItem != null
                    ? socket.SnoItem.Sno
                    : 0u);
            }
        }

        private string GetArmoryBuildKey(IPlayerArmorySet set)
        {
            if (set == null)
                return string.Empty;

            try
            {
                IPlayer player = set.Player ?? Hud.Game.Me;
                StringBuilder sb = new StringBuilder(360);
                string classCode = player != null && player.HeroClassDefinition != null
                    ? (player.HeroClassDefinition.Code ?? player.HeroClassDefinition.Name ?? "?")
                    : "?";
                sb.Append("class=").Append(classCode);
                sb.Append(";items=");

                uint[] items = new uint[13];
                int index = 0;
                if (set.ItemAnnIds != null)
                {
                    foreach (uint annId in set.ItemAnnIds)
                    {
                        if (index >= items.Length) break;
                        items[index++] = annId;
                    }
                }
                AppendUIntArray(sb, items);

                sb.Append(";cube=")
                    .Append(Sno(set.CubeSnoItem1)).Append(',')
                    .Append(Sno(set.CubeSnoItem2)).Append(',')
                    .Append(Sno(set.CubeSnoItem3)).Append(',')
                    .Append(Sno(set.CubeSnoItem4));

                sb.Append(";skills=")
                    .Append(Power(set.LeftSkillSnoPower)).Append(':').Append(set.LeftSkillRune).Append(',')
                    .Append(Power(set.RightSkillSnoPower)).Append(':').Append(set.RightSkillRune).Append(',')
                    .Append(Power(set.Skill1SnoPower)).Append(':').Append(set.Skill1Rune).Append(',')
                    .Append(Power(set.Skill2SnoPower)).Append(':').Append(set.Skill2Rune).Append(',')
                    .Append(Power(set.Skill3SnoPower)).Append(':').Append(set.Skill3Rune).Append(',')
                    .Append(Power(set.Skill4SnoPower)).Append(':').Append(set.Skill4Rune);

                sb.Append(";passives=")
                    .Append(Power(set.PassiveSnoPower1)).Append(',')
                    .Append(Power(set.PassiveSnoPower2)).Append(',')
                    .Append(Power(set.PassiveSnoPower3)).Append(',')
                    .Append(Power(set.PassiveSnoPower4));

                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        private ParagonProfile FindProfileByArmoryIndex(
            uint heroId,
            int armoryIndex)
        {
            if (heroId == 0u ||
                armoryIndex < 0)
                return null;

            ParagonProfile profile;
            string id = MakeIndexedProfileId(
                heroId, armoryIndex);

            if (_profiles.TryGetValue(id, out profile) &&
                profile != null &&
                profile.HeroId == heroId &&
                profile.ArmoryIndex == armoryIndex)
            {
                return profile;
            }

            // Compatibility with settings written before indexed IDs became
            // the primary dictionary key.
            foreach (ParagonProfile candidate in _profiles.Values)
            {
                if (candidate != null &&
                    candidate.HeroId == heroId &&
                    candidate.ArmoryIndex == armoryIndex)
                    return candidate;
            }

            return null;
        }

        private ParagonProfile FindLegacyProfile(
            string liveSignature)
        {
            if (string.IsNullOrEmpty(liveSignature))
                return null;

            ParagonProfile found = null;

            foreach (ParagonProfile profile in _profiles.Values)
            {
                if (profile == null ||
                    profile.HeroId != CurrentHeroId ||
                    profile.ArmoryIndex >= 0 ||
                    !string.Equals(
                        profile.LiveSignature,
                        liveSignature,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                    return null;

                found = profile;
            }

            return found;
        }

        private ParagonProfile FindCurrentProfile(
            string liveSignature)
        {
            if (_activeArmoryIndex >= 0)
            {
                ParagonProfile indexed =
                    FindProfileByArmoryIndex(
                        CurrentHeroId,
                        _activeArmoryIndex);

                if (indexed != null)
                {
                    if (!string.IsNullOrEmpty(
                            indexed.StableBuildKey) &&
                        !string.IsNullOrEmpty(
                            _currentStableBuildKey) &&
                        string.Equals(
                            indexed.StableBuildKey,
                            _currentStableBuildKey,
                            StringComparison.Ordinal))
                    {
                        return indexed;
                    }

                    if (string.IsNullOrEmpty(
                            indexed.StableBuildKey) &&
                        string.Equals(
                            indexed.LiveSignature,
                            liveSignature,
                            StringComparison.Ordinal))
                    {
                        return indexed;
                    }

                    return null;
                }
            }

            int uniqueIndex;
            ParagonProfile stable =
                FindIndexedProfileForCurrentBuild(
                    out uniqueIndex);

            if (stable != null)
                return stable;

            return FindLegacyProfile(
                liveSignature);
        }

        private HashSet<int> FindNativeArmoryMatches(
            string baseKey)
        {
            HashSet<int> matches =
                new HashSet<int>();

            if (string.IsNullOrEmpty(baseKey))
                return matches;

            try
            {
                IPlayerArmorySet[] sets =
                    Hud.Game.Me != null
                        ? Hud.Game.Me.ArmorySets
                        : null;

                if (sets == null)
                    return matches;

                for (int i = 0;
                    i < sets.Length;
                    i++)
                {
                    IPlayerArmorySet set =
                        sets[i];

                    if (set != null &&
                        !string.IsNullOrEmpty(
                            set.Name) &&
                        string.Equals(
                            GetArmoryBuildKey(set),
                            baseKey,
                            StringComparison.Ordinal))
                    {
                        matches.Add(set.Index);
                    }
                }
            }
            catch { }

            return matches;
        }

        private ParagonProfile FindIndexedProfileForCurrentBuild(
            out int uniqueArmoryIndex)
        {
            uniqueArmoryIndex = -1;

            uint heroId = CurrentHeroId;
            string stableKey =
                _currentStableBuildKey;
            string baseKey =
                GetLiveArmoryKey();

            if (heroId == 0u ||
                string.IsNullOrEmpty(stableKey) ||
                string.IsNullOrEmpty(baseKey))
            {
                return null;
            }

            HashSet<int> nativeMatches =
                FindNativeArmoryMatches(baseKey);

            if (nativeMatches.Count == 0)
                return null;

            ParagonProfile representative = null;
            string commonLayout = string.Empty;
            bool equivalentLayouts = true;
            int matchingProfileCount = 0;
            int profiledNativeSlotCount = 0;
            int onlyProfileIndex = -1;

            // Evaluate exactly one indexed profile per matching native slot.
            // This keeps cross-class names irrelevant because HeroId remains
            // part of every lookup, and it lets startup distinguish a unique
            // stable build from its gemmed/unsocketed public-layout siblings.
            foreach (int nativeIndex in nativeMatches)
            {
                ParagonProfile profile =
                    FindProfileByArmoryIndex(
                        heroId,
                        nativeIndex);

                if (profile == null ||
                    string.IsNullOrEmpty(
                        profile.StableBuildKey))
                {
                    continue;
                }

                profiledNativeSlotCount++;

                if (!string.Equals(
                        profile.StableBuildKey,
                        stableKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string layout =
                    BuildParagonLayoutKey(profile);

                if (representative == null)
                {
                    representative = profile;
                    commonLayout = layout;
                    onlyProfileIndex =
                        profile.ArmoryIndex;
                }
                else
                {
                    if (!string.Equals(
                            commonLayout,
                            layout,
                            StringComparison.Ordinal))
                    {
                        equivalentLayouts = false;
                    }

                    if (profile.UpdatedUtcTicks >
                        representative.UpdatedUtcTicks)
                    {
                        representative = profile;
                    }
                }

                matchingProfileCount++;
            }

            // Exact startup slot identity is safe only when the current public
            // Armory layout belongs to one native slot and that slot owns the
            // matching stable profile.
            if (nativeMatches.Count == 1 &&
                matchingProfileCount == 1 &&
                onlyProfileIndex >= 0)
            {
                uniqueArmoryIndex =
                    onlyProfileIndex;
                return representative;
            }

            // When every public-layout sibling has a saved stable profile and
            // only one of those profiles matches the live stable build, the
            // profile itself is safe to recognize at startup. The exact active
            // slot deliberately remains unresolved until native Armory UI data
            // proves it, so Save and grey-button handling cannot target a
            // guessed sibling. This covers common gemmed/unsocketed pairs.
            if (nativeMatches.Count > 1 &&
                profiledNativeSlotCount ==
                    nativeMatches.Count &&
                matchingProfileCount == 1)
            {
                return representative;
            }

            // Completely identical siblings may share a representative saved
            // profile only when every sibling is saved and every saved Paragon
            // layout is equivalent. Exact slot identity remains unresolved.
            if (nativeMatches.Count > 1 &&
                matchingProfileCount ==
                    nativeMatches.Count &&
                equivalentLayouts &&
                !string.IsNullOrEmpty(
                    commonLayout))
            {
                return representative;
            }

            return null;
        }

        private void TryBootstrapCurrentArmoryProfile()
        {
            if (_activeArmoryIndex >= 0 ||
                _equipPending ||
                _applyState != ApplyState.Idle ||
                _nativeTaskKind != NativeTaskKind.None)
            {
                return;
            }

            int uniqueIndex;
            ParagonProfile profile =
                FindIndexedProfileForCurrentBuild(
                    out uniqueIndex);

            if (profile == null ||
                uniqueIndex < 0)
            {
                return;
            }

            IPlayerArmorySet set =
                GetArmorySetByIndex(uniqueIndex);

            SetActiveArmoryBuild(
                uniqueIndex,
                set != null ? set.Name : profile.Label,
                _currentStableBuildKey);
        }

        private static string MakeIndexedProfileId(
            uint heroId,
            int armoryIndex)
        {
            return "hero=" +
                heroId.ToString(
                    CultureInfo.InvariantCulture) +
                ";armory=" +
                armoryIndex.ToString(
                    CultureInfo.InvariantCulture);
        }

        private string MakeProfileId(
            ParagonProfile profile)
        {
            uint heroId =
                profile != null &&
                profile.HeroId != 0u
                    ? profile.HeroId
                    : CurrentHeroId;

            if (profile != null &&
                profile.ArmoryIndex >= 0)
            {
                return MakeIndexedProfileId(
                    heroId,
                    profile.ArmoryIndex);
            }

            return "hero=" +
                heroId.ToString(
                    CultureInfo.InvariantCulture) +
                ";legacy=" +
                (profile != null
                    ? profile.LiveSignature ??
                        string.Empty
                    : string.Empty);
        }

        private void RemoveSupersededProfiles(
            ParagonProfile profile)
        {
            if (profile == null)
                return;

            List<string> remove =
                new List<string>();

            foreach (
                KeyValuePair<string, ParagonProfile> pair
                in _profiles)
            {
                ParagonProfile existing = pair.Value;
                if (existing == null ||
                    existing.HeroId != profile.HeroId)
                    continue;

                bool sameSlot =
                    profile.ArmoryIndex >= 0 &&
                    existing.ArmoryIndex ==
                        profile.ArmoryIndex;

                bool matchingLegacy =
                    existing.ArmoryIndex < 0 &&
                    string.Equals(
                        existing.LiveSignature,
                        profile.LiveSignature,
                        StringComparison.Ordinal);

                if (sameSlot || matchingLegacy)
                    remove.Add(pair.Key);
            }

            for (int i = 0;
                i < remove.Count;
                i++)
            {
                _profiles.Remove(remove[i]);
            }
        }

        private void CopyProfileToCapture(ParagonProfile profile)
        {
            if (profile == null || profile.Rows == null) return;
            for (int tab = 0; tab < 4; tab++)
            {
                for (int row = 0; row < 4; row++)
                    _capturedRows[tab, row] = profile.Rows[tab, row];
                _capturedAvailable[tab] = 0;
                _capturedTabsSeen[tab] = true;
            }
            _currentOverflowRow = profile.OverflowRow;
            _currentOverflowExplicit = profile.OverflowExplicit;
        }
        private void ClearCapturedProfile()
        {
            _currentOverflowRow = -1;
            _currentOverflowExplicit = false;

            for (int tab = 0; tab < 4; tab++)
            {
                _capturedTabsSeen[tab] = false;
                _capturedAvailable[tab] = 0;

                for (int row = 0; row < 4; row++)
                    _capturedRows[tab, row] = 0;
            }
        }

        private void ResetEquipStability()
        {
            _equipStableKey = string.Empty;
            _equipStableBaseKey = string.Empty;
            _equipStableStableKey = string.Empty;
            _equipStableSinceTick = NoTick;
            _equipStableSamples = 0;
            ResetEquipNativeStability();
        }

        private void ResetEquipNativeStability()
        {
            _equipNativeCandidate = null;
            _equipNativeSignature = string.Empty;
            _equipNativeCandidateFirstTick = NoTick;
            _equipNativeSamples = 0;
        }

        private void UpdateEquipNativeStability(
            ParagonProfile profile,
            string stableKey,
            int now)
        {
            if (profile == null ||
                profile.NativeFingerprint == null ||
                !NativeFingerprintHasRequiredSignals(
                    profile,
                    profile.NativeFingerprint,
                    false) ||
                string.IsNullOrEmpty(stableKey) ||
                !string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal) ||
                (_equipNativeStateObserved &&
                 !_equipNativeAccepted))
            {
                ResetEquipNativeStability();
                return;
            }

            NativeFingerprint live =
                ReadNativeFingerprint();

            if (live == null)
            {
                ResetEquipNativeStability();
                return;
            }

            NormalizeNativeFingerprint(
                profile,
                live);

            if (!NativeFingerprintHasRequiredSignals(
                    profile,
                    live,
                    true))
            {
                ResetEquipNativeStability();
                return;
            }

            string signature =
                BuildNativeFingerprintSignature(
                    live,
                    profile);

            if (string.IsNullOrEmpty(signature))
            {
                ResetEquipNativeStability();
                return;
            }

            if (!string.Equals(
                    signature,
                    _equipNativeSignature,
                    StringComparison.Ordinal))
            {
                _equipNativeSignature = signature;
                _equipNativeCandidate = live.Clone();
                _equipNativeCandidateFirstTick = now;
                _equipNativeSamples = 1;
                return;
            }

            _equipNativeCandidate = live.Clone();
            _equipNativeSamples++;
        }

        private NativeFingerprint GetSettledEquipNativeFingerprint(
            ParagonProfile profile,
            int now)
        {
            if (profile == null ||
                _equipNativeCandidate == null ||
                _equipNativeSamples < NativeTaskStableSamples ||
                _equipNativeCandidateFirstTick == NoTick ||
                unchecked(
                    now -
                    _equipNativeCandidateFirstTick) <
                    ProfileMatchStableMs ||
                !NativeFingerprintHasRequiredSignals(
                    profile,
                    _equipNativeCandidate,
                    true))
            {
                return null;
            }

            return _equipNativeCandidate.Clone();
        }

        private void ResolveProfileAfterEquip(
            ParagonProfile profile,
            int now)
        {
            ResolveProfileAfterEquip(
                profile,
                now,
                null);
        }

        private void ResolveProfileAfterEquip(
            ParagonProfile profile,
            int now,
            NativeFingerprint settledNative)
        {
            if (profile == null)
                return;

            if (KnownCurrentLayoutMatches(profile))
            {
                SetKnownCurrentLayout(profile);
                if (profile.NativeFingerprint == null)
                    ScheduleNativeFingerprintCapture(profile, now);
                return;
            }

            // Native rows are sampled while the existing Armory equipment/socket
            // settlement window is already running. A stable match may skip the
            // visible check without adding a second post-Equip delay. Unavailable,
            // unstable, or mismatching data falls through immediately.
            if (settledNative != null &&
                NativeFingerprintMatches(
                    profile,
                    settledNative))
            {
                SetKnownCurrentLayout(profile);
                return;
            }

            StartApply(profile, now);
        }

        private bool BeginNativeVerification(
            ParagonProfile profile,
            int now)
        {
            if (profile == null ||
                profile.NativeFingerprint == null ||
                !NativeFingerprintHasRequiredSignals(
                    profile,
                    profile.NativeFingerprint,
                    false) ||
                profile.HeroId != CurrentHeroId ||
                _applyState != ApplyState.Idle ||
                _scanRunning)
            {
                return false;
            }

            BeginNativeTask(
                NativeTaskKind.Verify,
                profile,
                now);

            return true;
        }

        private string BuildPassiveNativeVerifyAttemptKey(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.NativeFingerprint == null ||
                profile.HeroId == 0u ||
                string.IsNullOrEmpty(
                    profile.StableBuildKey))
            {
                return string.Empty;
            }

            string layoutKey =
                BuildParagonLayoutKey(profile);

            if (string.IsNullOrEmpty(layoutKey))
                return string.Empty;

            string fingerprintKey =
                BuildNativeFingerprintSignature(
                    profile.NativeFingerprint,
                    profile);

            if (string.IsNullOrEmpty(fingerprintKey))
                return string.Empty;

            return profile.HeroId.ToString(
                    CultureInfo.InvariantCulture) +
                "|" +
                (profile.ProfileId ??
                    string.Empty) +
                "|" +
                profile.StableBuildKey +
                "|" +
                layoutKey +
                "|" +
                fingerprintKey;
        }

        private void TryBeginPassiveCurrentProfileVerification(
            ParagonProfile profile,
            int now)
        {
            if (!IsCurrentHeroParagonEnabled ||
                profile == null ||
                profile.HeroId != CurrentHeroId ||
                profile.NativeFingerprint == null ||
                _equipPending ||
                _applyState != ApplyState.Idle ||
                _scanRunning ||
                _nativeTaskKind != NativeTaskKind.None ||
                IsParagonOpen() ||
                IsArmoryOpen() ||
                !CanAutomate())
            {
                return;
            }

            // Trust is already sufficient; no native work is needed.
            if (KnownCurrentLayoutMatches(profile))
                return;

            if (!NativeFingerprintHasRequiredSignals(
                    profile,
                    profile.NativeFingerprint,
                    false))
            {
                return;
            }

            string stableKey =
                GetStableBuildKey();

            if (string.IsNullOrEmpty(stableKey) ||
                !string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            string attemptKey =
                BuildPassiveNativeVerifyAttemptKey(
                    profile);

            if (string.IsNullOrEmpty(attemptKey) ||
                string.Equals(
                    _passiveNativeVerifyAttemptKey,
                    attemptKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (BeginNativeVerification(
                    profile,
                    now))
            {
                // Mark the attempt when it begins, not when it succeeds. A
                // mismatch or timeout must not restart every metadata refresh.
                _passiveNativeVerifyAttemptKey =
                    attemptKey;
            }
        }

        private void ScheduleNativeFingerprintCapture(
            ParagonProfile profile,
            int now)
        {
            if (!IsCurrentHeroParagonEnabled ||
                profile == null ||
                profile.HeroId != CurrentHeroId)
            {
                return;
            }

            BeginNativeTask(
                NativeTaskKind.Capture,
                profile,
                now);
        }

        private void BeginNativeTask(
            NativeTaskKind kind,
            ParagonProfile profile,
            int now)
        {
            ResetNativeTask();
            _nativeTaskKind = kind;
            _nativeTaskProfile = CloneProfile(profile);
            _nativeTaskStartTick = now;
            _nativeTaskNextTick = now;
        }

        private void ProcessNativeTask(int now)
        {
            if (_nativeTaskKind == NativeTaskKind.None ||
                _nativeTaskProfile == null ||
                _applyState != ApplyState.Idle ||
                _scanRunning)
            {
                return;
            }

            if (!CanAutomate())
            {
                ResetNativeTaskSampling(now);
                return;
            }

            if (!NativeTaskProfileStillCurrent(
                    _nativeTaskProfile))
            {
                ResetNativeTask();
                return;
            }

            if (IsParagonOpen())
            {
                if (_nativeTaskKind ==
                    NativeTaskKind.Verify)
                {
                    ResetNativeTask();
                }
                else
                {
                    ResetNativeTaskSampling(now);
                }
                return;
            }

            if (_nativeTaskKind ==
                    NativeTaskKind.Capture &&
                IsArmoryOpen())
            {
                ResetNativeTaskSampling(now);
                return;
            }

            if (!TickReached(now, _nativeTaskNextTick))
                return;

            _nativeTaskNextTick = unchecked(
                now + NativeTaskSampleMs);

            NativeFingerprint live =
                ReadNativeFingerprint();

            if (live != null)
            {
                NormalizeNativeFingerprint(
                    _nativeTaskProfile,
                    live);
            }

            if (live == null)
            {
                HandleNativeTaskTimeout(now);
                return;
            }

            string signature =
                BuildNativeFingerprintSignature(
                    live,
                    _nativeTaskProfile);

            if (!string.Equals(
                    signature,
                    _nativeTaskSignature,
                    StringComparison.Ordinal))
            {
                _nativeTaskSignature = signature;
                _nativeTaskCandidate = live;
                _nativeTaskCandidateFirstTick = now;
                _nativeTaskSamples = 1;
            }
            else
            {
                _nativeTaskSamples++;
            }

            int stableSpan =
                _nativeTaskCandidateFirstTick == NoTick
                    ? 0
                    : unchecked(
                        now -
                        _nativeTaskCandidateFirstTick);

            if (_nativeTaskSamples <
                    NativeTaskStableSamples ||
                stableSpan < NativeTaskStableSpanMs ||
                unchecked(now - _nativeTaskStartTick) <
                    NativeTaskGraceMs)
            {
                HandleNativeTaskTimeout(now);
                return;
            }

            NativeTaskKind task =
                _nativeTaskKind;
            ParagonProfile profile =
                _nativeTaskProfile;
            NativeFingerprint settled =
                _nativeTaskCandidate != null
                    ? _nativeTaskCandidate.Clone()
                    : live.Clone();

            ResetNativeTask();

            if (task == NativeTaskKind.Capture)
            {
                if (NativeFingerprintHasRequiredSignals(
                        profile,
                        settled,
                        true))
                {
                    CommitNativeFingerprint(
                        profile,
                        settled);
                }
                return;
            }

            if (NativeFingerprintMatches(
                    profile,
                    settled))
            {
                SetKnownCurrentLayout(profile);
                return;
            }

            // Native verification is passive and trust-only. A mismatch leaves
            // the plugin idle; a later Armory transaction uses the immediate
            // visible apply path.
        }

        private void HandleNativeTaskTimeout(int now)
        {
            int timeout =
                _nativeTaskKind ==
                    NativeTaskKind.Verify
                    ? NativeVerifyTimeoutMs
                    : NativeCaptureTimeoutMs;

            if (_nativeTaskStartTick == NoTick ||
                unchecked(now - _nativeTaskStartTick) <
                    timeout)
            {
                return;
            }

            ResetNativeTask();
        }

        private void ResetNativeTaskSampling(int now)
        {
            _nativeTaskSignature = string.Empty;
            _nativeTaskCandidate = null;
            _nativeTaskCandidateFirstTick = NoTick;
            _nativeTaskSamples = 0;
            _nativeTaskStartTick = now;
            _nativeTaskNextTick = unchecked(
                now + NativeTaskSampleMs);
        }

        private void CancelNativeCapture()
        {
            if (_nativeTaskKind ==
                NativeTaskKind.Capture)
            {
                ResetNativeTask();
            }
        }

        private void ResetNativeTask()
        {
            _nativeTaskKind = NativeTaskKind.None;
            _nativeTaskProfile = null;
            _nativeTaskCandidate = null;
            _nativeTaskSignature = string.Empty;
            _nativeTaskStartTick = NoTick;
            _nativeTaskCandidateFirstTick = NoTick;
            _nativeTaskSamples = 0;
            _nativeTaskNextTick = NoTick;
        }

        private bool NativeTaskProfileStillCurrent(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.HeroId != CurrentHeroId)
            {
                return false;
            }

            if (_activeArmoryIndex >= 0 &&
                profile.ArmoryIndex >= 0 &&
                _activeArmoryIndex !=
                    profile.ArmoryIndex)
            {
                return false;
            }

            string stableKey =
                GetStableBuildKey();

            return !string.IsNullOrEmpty(stableKey) &&
                string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal);
        }

        private void CommitNativeFingerprint(
            ParagonProfile identity,
            NativeFingerprint fingerprint)
        {
            if (identity == null ||
                fingerprint == null)
            {
                return;
            }

            ParagonProfile stored = null;
            if (!string.IsNullOrEmpty(
                    identity.ProfileId))
            {
                _profiles.TryGetValue(
                    identity.ProfileId,
                    out stored);
            }

            if (stored == null &&
                identity.ArmoryIndex >= 0)
            {
                stored = FindProfileByArmoryIndex(
                    identity.HeroId,
                    identity.ArmoryIndex);
            }

            if (stored == null ||
                !string.Equals(
                    stored.StableBuildKey,
                    identity.StableBuildKey,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    BuildParagonLayoutKey(stored),
                    BuildParagonLayoutKey(identity),
                    StringComparison.Ordinal))
            {
                return;
            }

            stored.NativeFingerprint =
                fingerprint.Clone();
            SaveSettings();
        }

        private bool KnownCurrentLayoutMatches(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.Rows == null ||
                profile.HeroId == 0u)
            {
                return false;
            }

            string knownLayout;
            if (!_knownLayoutByHero.TryGetValue(
                    profile.HeroId,
                    out knownLayout))
            {
                return false;
            }

            string targetKey =
                BuildParagonLayoutKey(profile);

            if (string.IsNullOrEmpty(targetKey) ||
                !string.Equals(
                    knownLayout,
                    targetKey,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (profile.HeroId == CurrentHeroId)
            {
                SetKnownLayoutMirror(
                    profile.HeroId,
                    knownLayout);
            }

            return true;
        }

        private void SetKnownCurrentLayout(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.Rows == null ||
                profile.HeroId == 0u)
            {
                return;
            }

            string layoutKey =
                BuildParagonLayoutKey(profile);

            if (string.IsNullOrEmpty(layoutKey))
            {
                InvalidateKnownCurrentLayout();
                return;
            }

            _knownLayoutByHero[profile.HeroId] =
                layoutKey;

            SetKnownLayoutMirror(
                profile.HeroId,
                layoutKey);
        }

        private void SetKnownLayoutMirror(
            uint heroId,
            string layoutKey)
        {
            _knownCurrentLayoutValid =
                heroId != 0u &&
                !string.IsNullOrEmpty(layoutKey);
            _knownCurrentHeroId =
                _knownCurrentLayoutValid
                    ? heroId
                    : 0u;
            _knownCurrentLayoutKey =
                _knownCurrentLayoutValid
                    ? layoutKey
                    : string.Empty;
        }

        private void RestoreKnownLayoutMirror(
            uint heroId)
        {
            string layoutKey;

            if (heroId != 0u &&
                _knownLayoutByHero.TryGetValue(
                    heroId,
                    out layoutKey))
            {
                SetKnownLayoutMirror(
                    heroId,
                    layoutKey);
                return;
            }

            SetKnownLayoutMirror(
                0u,
                string.Empty);
        }

        private static string BuildParagonLayoutKey(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.Rows == null)
            {
                return string.Empty;
            }

            int overflow =
                NormalizeOverflowRow(
                    profile.OverflowRow,
                    profile.Rows);

            StringBuilder sb =
                new StringBuilder(96);

            sb.Append("overflow=")
                .Append(overflow)
                .Append(";rows=");

            for (int tab = 0; tab < 4; tab++)
            {
                if (tab > 0)
                    sb.Append('|');

                for (int row = 0; row < 4; row++)
                {
                    if (row > 0)
                        sb.Append(',');

                    if (tab == CoreTab &&
                        row == overflow)
                    {
                        sb.Append('*');
                    }
                    else
                    {
                        sb.Append(
                            profile.Rows[tab, row]);
                    }
                }
            }

            return sb.ToString();
        }

        private void InvalidateKnownCurrentLayout()
        {
            uint heroId = CurrentHeroId;

            if (heroId != 0u)
                _knownLayoutByHero.Remove(heroId);

            SetKnownLayoutMirror(
                0u,
                string.Empty);
        }

        private void ClearAllKnownLayouts()
        {
            _knownLayoutByHero.Clear();
            SetKnownLayoutMirror(
                0u,
                string.Empty);
        }

        private static void AppendUIntArray(StringBuilder sb, uint[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i]);
            }
        }

        private static int ActionSlotIndex(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0;
                case ActionKey.RightSkill: return 1;
                case ActionKey.Skill1: return 2;
                case ActionKey.Skill2: return 3;
                case ActionKey.Skill3: return 4;
                case ActionKey.Skill4: return 5;
                default: return -1;
            }
        }

        private static uint Sno(ISnoItem item)
        {
            return item != null ? item.Sno : 0u;
        }

        private static uint Power(ISnoPower power)
        {
            return power != null ? power.Sno : 0u;
        }

        private static bool IsEquipmentSlot(ItemLocation location)
        {
            return location >= ItemLocation.Head && location <= ItemLocation.Neck;
        }

        private bool RowsReadable()
        {
            for (int row = 0; row < 4; row++)
                if (!GetRowState(row).Valid) return false;
            return true;
        }

        private bool RowsAreZero()
        {
            if (!RowsReadable()) return false;
            for (int row = 0; row < 4; row++)
                if (GetRowState(row).Assigned != 0) return false;
            return true;
        }

        private RowState GetRowState(int row)
        {
            RowState result = new RowState();
            string text = PointsSpentText(row);
            if (string.IsNullOrEmpty(text))
                return result;

            int slash = text.IndexOf('/');
            if (slash >= 0)
            {
                int assigned;
                int cap;
                if (TryParseFirstInt(text.Substring(0, slash), out assigned) &&
                    TryParseFirstInt(text.Substring(slash + 1), out cap))
                {
                    result.Valid = true;
                    result.Assigned = assigned;
                    result.Cap = cap;
                    result.HasCap = cap > 0;
                }
                return result;
            }

            int value;
            if (TryParseFirstInt(text, out value))
            {
                result.Valid = true;
                result.Assigned = value;
            }
            return result;
        }

        private string PointsSpentText(int row)
        {
            try
            {
                if (row < 0 || row >= 4 || !IsVisible(_pointsSpent[row]))
                    return string.Empty;
                return CleanText(_pointsSpent[row].ReadText(Encoding.UTF8, true));
            }
            catch { return string.Empty; }
        }

        private int ParseAvailable(int tab)
        {
            try
            {
                if (tab >= 0 && tab < 4 && IsVisible(_available[tab]))
                {
                    int value;
                    string text = CleanText(_available[tab].ReadText(Encoding.UTF8, true));
                    if (TryParseFirstInt(text, out value))
                        return value;
                }
            }
            catch { }

            try
            {
                int[] native = Hud.Game.Me.ParagonPointsAvailable;
                if (native != null && tab >= 0 && tab < native.Length)
                    return native[tab];
            }
            catch { }
            return -1;
        }

        private int GetActiveTab()
        {
            for (int tab = 0; tab < 4; tab++)
                if (IsVisible(_tabs[tab]) && SafeAnim(_tabs[tab]) == ActiveTabAnim)
                    return tab;
            return -1;
        }

        private bool IsParagonOpen()
        {
            return IsVisible(_paragonRoot);
        }

        private bool IsArmoryOpen()
        {
            return IsVisible(_armoryRoot);
        }

        private System.Drawing.RectangleF GetArmoryEquipRect()
        {
            System.Drawing.RectangleF nativeRect;
            int nativeState;

            if (TryGetNativeEquipState(
                    out nativeRect,
                    out nativeState))
            {
                return nativeRect;
            }

            return GetRelativeArmoryRect(
                EquipRelX,
                EquipRelY,
                EquipRelW,
                EquipRelH);
        }

        private bool TryGetNativeEquipState(
            out System.Drawing.RectangleF rectangle,
            out int animState)
        {
            rectangle =
                System.Drawing.RectangleF.Empty;
            animState = -999;

            if (_armoryEquipButton == null)
                return false;

            try
            {
                _armoryEquipButton.Refresh();
                rectangle =
                    _armoryEquipButton.Rectangle;
                animState =
                    _armoryEquipButton.AnimState;

                return _armoryEquipButton.Visible &&
                    rectangle.Width > 0f &&
                    rectangle.Height > 0f;
            }
            catch
            {
                rectangle =
                    System.Drawing.RectangleF.Empty;
                animState = -999;
                return false;
            }
        }

        private static bool IsNativeEquipInteractiveState(
            int animState)
        {
            return animState == NativeEquipPressedAnim ||
                animState == NativeEquipHoverAnim ||
                animState == NativeEquipEnabledAnim;
        }

        private System.Drawing.RectangleF GetRelativeArmoryRect(float x, float y, float w, float h)
        {
            System.Drawing.RectangleF root = SafeRect(_armoryRoot);
            if (root.Width <= 0 || root.Height <= 0)
                return System.Drawing.RectangleF.Empty;
            float px = root.Width * ArmoryPadX;
            float py = root.Height * ArmoryPadY;
            return new System.Drawing.RectangleF(
                root.Left + root.Width * x - px,
                root.Top + root.Height * y - py,
                root.Width * w + px * 2,
                root.Height * h + py * 2);
        }

        private bool ClickUi(IUiElement element, ClickModifier modifier)
        {
            if (!IsParagonOpen() || !IsVisible(element))
                return false;
            System.Drawing.RectangleF rect = SafeRect(element);
            if (rect.Width <= 0 || rect.Height <= 0)
                return false;
            int x = (int)Math.Round(rect.Left + rect.Width * 0.5f);
            int y = (int)Math.Round(rect.Top + rect.Height * 0.5f);
            return ClickAt(x, y, modifier);
        }

        private bool ClickAt(int x, int y, ClickModifier modifier)
        {
            if (!IsParagonOpen() || !PointInRect(SafeRect(_paragonRoot), x, y))
                return false;

            bool ownCtrl = false;
            bool ownShift = false;
            try
            {
                ownCtrl = modifier == ClickModifier.Control && !IsVirtualKeyDown(VK_CONTROL);
                ownShift = modifier == ClickModifier.Shift && !IsVirtualKeyDown(VK_SHIFT);
                if (ownCtrl) KeyDown(VK_CONTROL);
                if (ownShift) KeyDown(VK_SHIFT);

                SetCursorPos(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (ownShift) KeyUp(VK_SHIFT);
                if (ownCtrl) KeyUp(VK_CONTROL);
            }
        }

        private bool RawMouseClickAt(int x, int y)
        {
            if (!IsParagonOpen() || !PointInRect(SafeRect(_paragonRoot), x, y))
                return false;
            try
            {
                SetCursorPos(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            catch { return false; }
        }

        private void ReleasePendingModifier()
        {
            if (!_pendingModifierOwned)
                return;
            if (_burstModifier == ClickModifier.Control)
                KeyUp(VK_CONTROL);
            else if (_burstModifier == ClickModifier.Shift)
                KeyUp(VK_SHIFT);
            _pendingModifierOwned = false;
        }

        private void PressVirtualKey(Keys key)
        {
            byte vk = (byte)((int)key & 0xFF);
            try
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        private static void KeyDown(byte vk)
        {
            try { keybd_event(vk, 0, 0, UIntPtr.Zero); } catch { }
        }

        private static void KeyUp(byte vk)
        {
            try { keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); } catch { }
        }

        private static bool IsVirtualKeyDown(byte vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        private void CaptureScanCursor()
        {
            _scanCursorCaptured = false;
            POINT point;
            if (GetCursorPos(out point))
            {
                _scanCursorX = point.X;
                _scanCursorY = point.Y;
                _scanCursorCaptured = true;
            }
        }

        private void RestoreScanCursor()
        {
            if (!_scanCursorCaptured)
                return;
            _scanCursorCaptured = false;
            try { SetCursorPos(_scanCursorX, _scanCursorY); } catch { }
        }

        private void MoveCursorToCloseButton()
        {
            System.Drawing.RectangleF root = SafeRect(_paragonRoot);
            if (root.Width <= 0 || root.Height <= 0)
                return;

            int x = (int)Math.Round(root.Left + root.Width * 0.6300f);
            int y = (int)Math.Round(root.Top + root.Height * 0.9030f);
            try { SetCursorPos(x, y); } catch { }
        }

        private void CaptureCursorForRestore()
        {
            _restoreCursorCaptured = false;
            if (!RestoreCursorAfterAutomation)
                return;
            POINT point;
            if (GetCursorPos(out point))
            {
                _restoreCursorX = point.X;
                _restoreCursorY = point.Y;
                _restoreCursorCaptured = true;
            }
        }

        private void RestoreCapturedCursor()
        {
            if (!_restoreCursorCaptured || !RestoreCursorAfterAutomation)
                return;
            _restoreCursorCaptured = false;
            try { SetCursorPos(_restoreCursorX, _restoreCursorY); } catch { }
        }

        private bool CanObserve()
        {
            try
            {
                return Hud != null &&
                    Hud.Game != null &&
                    Hud.Game.IsInGame &&
                    !Hud.Game.IsLoading;
            }
            catch
            {
                return false;
            }
        }

        private bool IsForeground()
        {
            try
            {
                return Hud != null &&
                    Hud.Window != null &&
                    Hud.Window.IsForeground;
            }
            catch
            {
                return false;
            }
        }

        private bool CanAutomate()
        {
            try
            {
                return CanObserve() &&
                    IsForeground() &&
                    Hud.Game.Me != null &&
                    Hud.Game.Me.IsInTown &&
                    !Hud.Game.Me.IsDeadSafeCheck;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLeftButtonDown()
        {
            try { return Hud.Input.IsKeyDown(Keys.LButton); }
            catch { return false; }
        }

        private bool IsVisible(IUiElement element)
        {
            if (element == null) return false;
            try
            {
                element.Refresh();
                return element.Visible && element.Rectangle.Width > 0 && element.Rectangle.Height > 0;
            }
            catch { return false; }
        }

        private static void SafeRefresh(
            IUiElement element)
        {
            try
            {
                if (element != null)
                    element.Refresh();
            }
            catch { }
        }

        private static int SafeAnim(IUiElement element)
        {
            try { return element != null ? element.AnimState : -999; }
            catch { return -999; }
        }

        private static System.Drawing.RectangleF SafeRect(IUiElement element)
        {
            try { return element != null ? element.Rectangle : System.Drawing.RectangleF.Empty; }
            catch { return System.Drawing.RectangleF.Empty; }
        }

        private int SafeCursorX()
        {
            try { return Hud.Window.CursorX; }
            catch { return 0; }
        }

        private int SafeCursorY()
        {
            try { return Hud.Window.CursorY; }
            catch { return 0; }
        }

        private void RegisterUiElements()
        {
            _paragonRoot = RegisterUi(
                ParagonBase, null);

            for (int tab = 0; tab < 4; tab++)
            {
                int number = tab + 1;
                string suffix =
                    number.ToString(
                        CultureInfo.InvariantCulture);

                _tabs[tab] = RegisterUi(
                    ParagonBase + ".tab_" + suffix,
                    _paragonRoot);

                _available[tab] = RegisterUi(
                    ParagonBase +
                    ".Points_Available_" + suffix,
                    _paragonRoot);
            }

            for (int row = 0; row < 4; row++)
            {
                string path =
                    ParagonBase +
                    ".Bonuses.bonus" +
                    row.ToString(
                        CultureInfo.InvariantCulture);

                _rows[row] = RegisterUi(
                    path, _paragonRoot);
                _pointsSpent[row] = RegisterUi(
                    path + ".PointsSpent",
                    _paragonRoot);
                _plusButtons[row] = RegisterUi(
                    path + ".IncreaseStat",
                    _paragonRoot);
            }

            _resetButton = RegisterUi(
                ParagonBase +
                ".ResetParagonPointsButton",
                _paragonRoot);

            _acceptButton = RegisterUi(
                ParagonBase +
                ".AcceptParagonPointsButton",
                _paragonRoot);

            _closeButton = RegisterUi(
                ParagonBase + ".CloseButton",
                _paragonRoot);

            _armoryRoot = RegisterUi(
                ArmoryBase, null);

            _armoryPage1 = RegisterUi(
                ArmoryBase +
                ".armory_pages.page_1",
                _armoryRoot);

            _armoryPage2 = RegisterUi(
                ArmoryBase +
                ".armory_pages.page_2",
                _armoryRoot);

            _armoryLoadoutName = RegisterUi(
                ArmoryBase + ".loadout_name",
                _armoryRoot);

            _armoryEquipButton = RegisterUi(
                ArmoryBase + ".overlay.equip",
                _armoryRoot);

            for (int row = 0; row < 5; row++)
            {
                _armoryNativeTabs[row] =
                    RegisterUi(
                        ArmoryBase + ".tab_" +
                        row.ToString(
                            CultureInfo.InvariantCulture),
                        _armoryRoot);
            }
        }

        private IUiElement RegisterUi(string path, IUiElement parent)
        {
            try { return Hud.Render.RegisterUiElement(path, parent, null); }
            catch
            {
                try { return Hud.Render.GetUiElement(path); }
                catch { return null; }
            }
        }

        private System.Drawing.RectangleF GetProfileSaveButtonRect()
        {
            System.Drawing.RectangleF root = SafeRect(_paragonRoot);
            System.Drawing.RectangleF reset = SafeRect(_resetButton);
            if (root.Width <= 0 || root.Height <= 0)
                return System.Drawing.RectangleF.Empty;

            if (reset.Width > 0 && reset.Height > 0)
            {
                float height = Math.Max(24.0f, Math.Min(28.0f, reset.Height - 4.0f));
                float width = Math.Min(245.0f, root.Width * 0.30f);
                float x = reset.Left - 18.0f - width;
                float minimumX = root.Left + 70.0f;
                if (x < minimumX) x = minimumX;
                float y = reset.Top + (reset.Height - height) * 0.5f;
                return new System.Drawing.RectangleF(x, y, width, height);
            }

            return new System.Drawing.RectangleF(
                root.Left + 74.0f,
                root.Bottom - 144.0f,
                Math.Min(245.0f, root.Width * 0.30f),
                27.0f);
        }

        private void GetOverflowToggleRects(
            out System.Drawing.RectangleF coreToggle,
            out System.Drawing.RectangleF vitalityToggle)
        {
            coreToggle = System.Drawing.RectangleF.Empty;
            vitalityToggle = System.Drawing.RectangleF.Empty;

            System.Drawing.RectangleF root = SafeRect(_paragonRoot);
            System.Drawing.RectangleF reset = SafeRect(_resetButton);
            if (root.Width <= 0 || root.Height <= 0 || reset.Width <= 0 || reset.Height <= 0)
                return;

            float height = Math.Max(24.0f, Math.Min(28.0f, reset.Height - 4.0f));
            float x = reset.Right + 24.0f;
            float available = root.Right - x - 74.0f;
            float totalWidth = Math.Min(245.0f, available);
            if (totalWidth < 160.0f)
                return;

            const float gap = 8.0f;
            float width = (totalWidth - gap) * 0.5f;
            float y = reset.Top + (reset.Height - height) * 0.5f;
            coreToggle = new System.Drawing.RectangleF(x, y, width, height);
            vitalityToggle = new System.Drawing.RectangleF(x + width + gap, y, width, height);
        }

        private void DrawOverflowPrompt(
            System.Drawing.RectangleF coreToggle,
            System.Drawing.RectangleF vitalityToggle)
        {
            if (_instructionFont == null ||
                coreToggle.Width <= 0 || coreToggle.Height <= 0 ||
                vitalityToggle.Width <= 0 || vitalityToggle.Height <= 0)
            {
                return;
            }

            float left = Math.Min(coreToggle.Left, vitalityToggle.Left);
            float right = Math.Max(coreToggle.Right, vitalityToggle.Right);
            float top = Math.Min(coreToggle.Top, vitalityToggle.Top);
            float x = left;

            try
            {
                var layout = _instructionFont.GetTextLayout(OverflowPromptText);
                if (layout != null)
                    x = left + Math.Max(0.0f, (right - left - layout.Metrics.Width) * 0.5f);
            }
            catch
            {
                x = left;
            }

            _instructionFont.DrawText(OverflowPromptText, x, top - 19.0f);
        }

        private System.Drawing.RectangleF GetArmoryHeroToggleRect()
        {
            System.Drawing.RectangleF root = SafeRect(_armoryRoot);
            if (root.Width <= 0f || root.Height <= 0f)
                return System.Drawing.RectangleF.Empty;

            float width = Math.Max(
                38.0f,
                Math.Min(
                    44.0f,
                    root.Width * ArmoryHeroToggleWRel));

            float height = Math.Max(
                16.0f,
                Math.Min(
                    18.0f,
                    root.Height * ArmoryHeroToggleHRel));

            float centerX =
                root.Left +
                root.Width * ArmoryHeroToggleCenterXRel;

            return new System.Drawing.RectangleF(
                centerX - width * 0.5f,
                root.Top +
                    root.Height * ArmoryHeroToggleYRel,
                width,
                height);
        }

        private void DrawArmoryHeroToggle()
        {
            if (!IsArmoryOpen() ||
                !CanConfigureCurrentHeroParagon)
            {
                return;
            }

            System.Drawing.RectangleF button =
                GetArmoryHeroToggleRect();

            if (button.Width <= 0f ||
                button.Height <= 0f)
            {
                return;
            }

            bool enabled =
                IsCurrentHeroParagonEnabled;

            bool hover =
                PointInRect(
                    button,
                    SafeCursorX(),
                    SafeCursorY());

            DrawArmoryHeroToggleButton(
                button,
                enabled,
                hover);

            if (_smallFont == null)
                return;

            try
            {
                var layout =
                    _smallFont.GetTextLayout(
                        "Save Paragon");

                if (layout == null)
                    return;

                float x =
                    button.Left +
                    Math.Max(
                        0.0f,
                        (button.Width -
                            layout.Metrics.Width) *
                        0.5f);

                float y =
                    button.Top -
                    ArmoryHeroToggleLabelGap -
                    layout.Metrics.Height;

                _smallFont.DrawText(
                    layout,
                    x,
                    y);
            }
            catch { }
        }

        private void DrawArmoryHeroToggleButton(
            System.Drawing.RectangleF rect,
            bool enabled,
            bool hover)
        {
            float half =
                rect.Height * 0.5f;

            System.Drawing.RectangleF top =
                new System.Drawing.RectangleF(
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    half);

            System.Drawing.RectangleF bottom =
                new System.Drawing.RectangleF(
                    rect.Left,
                    rect.Top + half,
                    rect.Width,
                    rect.Height - half);

            IBrush topBrush =
                enabled
                    ? _toggleIndicatorOnBrush
                    : (hover
                        ? _toggleHoverBrush
                        : _toggleIndicatorOffBrush);

            IBrush bottomBrush =
                enabled
                    ? _toggleSelectedBackBrush
                    : _toggleBackBrush;

            if (bottomBrush != null)
                bottomBrush.DrawRectangle(bottom);

            if (topBrush != null)
                topBrush.DrawRectangle(top);

            if (_toggleBorderBrush != null)
                _toggleBorderBrush.DrawRectangle(rect);

            if (_smallFont == null)
                return;

            string text =
                enabled ? "ON" : "OFF";

            try
            {
                var layout =
                    _smallFont.GetTextLayout(text);

                if (layout == null)
                    return;

                float x =
                    rect.Left +
                    Math.Max(
                        0.0f,
                        (rect.Width -
                            layout.Metrics.Width) *
                        0.5f);

                float y =
                    rect.Top +
                    Math.Max(
                        0.0f,
                        (rect.Height -
                            layout.Metrics.Height) *
                        0.5f);

                _smallFont.DrawText(
                    layout,
                    x,
                    y);
            }
            catch { }
        }

        private bool TryHandleArmoryHeroToggleClick()
        {
            if (!IsArmoryOpen() ||
                !CanConfigureCurrentHeroParagon)
            {
                return false;
            }

            System.Drawing.RectangleF rect =
                GetArmoryHeroToggleRect();

            if (!PointInRect(
                    rect,
                    SafeCursorX(),
                    SafeCursorY()))
            {
                return false;
            }

            ToggleCurrentHeroParagonEnabled();
            return true;
        }

        private void DrawOverflowToggle(
            System.Drawing.RectangleF rect,
            string label,
            bool selected,
            bool hover)
        {
            if (rect.Width <= 0 ||
                rect.Height <= 0)
            {
                return;
            }

            IBrush back = selected
                ? _toggleSelectedBackBrush
                : (hover
                    ? _toggleHoverBrush
                    : _toggleBackBrush);

            IBrush border = selected
                ? _toggleSelectedBorderBrush
                : _toggleBorderBrush;

            if (back != null)
                back.DrawRectangle(rect);

            if (border != null)
                border.DrawRectangle(rect);

            float indicatorHeight =
                Math.Max(
                    8.0f,
                    rect.Height - 12.0f);

            float indicatorWidth =
                Math.Min(
                    34.0f,
                    rect.Width * 0.28f);

            System.Drawing.RectangleF indicator =
                new System.Drawing.RectangleF(
                    rect.Left + 6.0f,
                    rect.Top +
                        (rect.Height -
                            indicatorHeight) *
                        0.5f,
                    indicatorWidth,
                    indicatorHeight);

            IBrush indicatorBrush =
                selected
                    ? _toggleIndicatorOnBrush
                    : _toggleIndicatorOffBrush;

            if (indicatorBrush != null)
                indicatorBrush.DrawRectangle(
                    indicator);

            if (_toggleBorderBrush != null)
                _toggleBorderBrush.DrawRectangle(
                    indicator);

            if (_smallFont != null)
            {
                _smallFont.DrawText(
                    label,
                    indicator.Right + 7.0f,
                    rect.Top + 5.0f);
            }
        }

        private bool TryReadNativeArmorySelection(
            out int armoryIndex,
            out string armoryName)
        {
            armoryIndex = -1;
            armoryName = string.Empty;

            if (!IsArmoryOpen())
                return false;

            SafeRefresh(_armoryPage1);
            SafeRefresh(_armoryPage2);

            int page1 = SafeAnim(
                _armoryPage1);
            int page2 = SafeAnim(
                _armoryPage2);

            int page;
            if (page1 == NativePageSelectedAnim &&
                page2 != NativePageSelectedAnim)
            {
                page = 0;
            }
            else if (
                page2 == NativePageSelectedAnim &&
                page1 != NativePageSelectedAnim)
            {
                page = 1;
            }
            else
            {
                return false;
            }

            string visibleName =
                ReadArmoryLoadoutName();

            // The selected page plus a unique native loadout name is the
            // strongest and most portable signal. It avoids depending on
            // row animation numbers when names are unique.
            int nameIndex =
                FindArmoryIndexByName(
                    page,
                    visibleName);

            if (nameIndex >= 0)
            {
                armoryIndex = nameIndex;
                armoryName = visibleName;
                return true;
            }

            // Duplicate names need the persistent selected-row animation.
            int row = -1;
            for (int i = 0; i < 5; i++)
            {
                SafeRefresh(
                    _armoryNativeTabs[i]);

                if (SafeAnim(
                        _armoryNativeTabs[i]) !=
                    NativeSelectedRowAnim[
                        page, i])
                {
                    continue;
                }

                if (row >= 0)
                    return false;

                row = i;
            }

            if (row < 0)
                return false;

            int rowIndex =
                page * 5 + row;

            IPlayerArmorySet set =
                GetArmorySetByIndex(rowIndex);

            if (set == null ||
                string.IsNullOrEmpty(set.Name))
                return false;

            if (!string.IsNullOrEmpty(
                    visibleName) &&
                !NamesEqual(
                    set.Name,
                    visibleName))
            {
                return false;
            }

            armoryIndex = rowIndex;
            armoryName =
                !string.IsNullOrEmpty(
                    visibleName)
                    ? visibleName
                    : set.Name;
            return true;
        }

        private string ReadArmoryLoadoutName()
        {
            if (_armoryLoadoutName == null)
                return string.Empty;

            try
            {
                _armoryLoadoutName.Refresh();
                if (!_armoryLoadoutName.Visible)
                    return string.Empty;

                return NormalizeUiText(
                    _armoryLoadoutName.ReadText(
                        Encoding.UTF8,
                        true));
            }
            catch
            {
                return string.Empty;
            }
        }

        private int FindArmoryIndexByName(
            int page,
            string name)
        {
            if (page < 0 ||
                page > 1 ||
                string.IsNullOrEmpty(name))
                return -1;

            int match = -1;

            try
            {
                IPlayerArmorySet[] sets =
                    Hud.Game.Me != null
                        ? Hud.Game.Me.ArmorySets
                        : null;

                if (sets == null)
                    return -1;

                for (int i = 0;
                    i < sets.Length;
                    i++)
                {
                    IPlayerArmorySet set =
                        sets[i];

                    if (set == null ||
                        set.Index / 5 != page ||
                        string.IsNullOrEmpty(set.Name) ||
                        !NamesEqual(
                            set.Name,
                            name))
                    {
                        continue;
                    }

                    if (match >= 0)
                        return -1;

                    match = set.Index;
                }
            }
            catch
            {
                return -1;
            }

            return match;
        }

        private static bool NamesEqual(
            string left,
            string right)
        {
            return string.Equals(
                NormalizeUiText(left),
                NormalizeUiText(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUiText(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder result =
                new StringBuilder(value.Length);

            for (int i = 0;
                i < value.Length;
                i++)
            {
                char c = value[i];

                if (c == '\0' ||
                    c == '\r' ||
                    c == '\n' ||
                    c == '\t')
                    continue;

                if (!char.IsControl(c))
                    result.Append(c);
            }

            return result.ToString().Trim();
        }

        private IPlayerArmorySet GetArmorySetByIndex(
            int armoryIndex)
        {
            try
            {
                IPlayerArmorySet[] sets =
                    Hud.Game.Me != null
                        ? Hud.Game.Me.ArmorySets
                        : null;

                if (sets == null)
                    return null;

                for (int i = 0;
                    i < sets.Length;
                    i++)
                {
                    IPlayerArmorySet set =
                        sets[i];

                    if (set != null &&
                        set.Index == armoryIndex)
                        return set;
                }
            }
            catch { }

            return null;
        }

        private bool CurrentBuildMatchesArmoryIndex(
            int armoryIndex,
            string baseKey)
        {
            if (armoryIndex < 0 ||
                string.IsNullOrEmpty(baseKey))
                return false;

            IPlayerArmorySet set =
                GetArmorySetByIndex(
                    armoryIndex);

            return set != null &&
                string.Equals(
                    GetArmoryBuildKey(set),
                    baseKey,
                    StringComparison.Ordinal);
        }

        private void SetActiveArmoryBuild(
            int armoryIndex,
            string armoryName,
            string stableKey)
        {
            _activeArmoryIndex = armoryIndex;
            _activeArmoryName =
                armoryName ?? string.Empty;
            _currentStableBuildKey =
                stableKey ?? string.Empty;
            RefreshCurrentBuildDisplay();
        }

        private void ClearPendingEquip()
        {
            _equipPending = false;
            _equipClickTick = NoTick;
            _equipRequestedArmoryIndex = -1;
            _equipRequestedArmoryName =
                string.Empty;
            _equipNativeStateObserved = false;
            _equipNativeAccepted = false;
            ResetEquipStability();
        }

        private ParagonProfile TryMigrateLegacyProfile(
            int armoryIndex,
            string label,
            string liveKey,
            string baseKey,
            string stableKey)
        {
            ParagonProfile legacy =
                FindLegacyProfile(liveKey);

            if (legacy == null)
                return null;

            string oldId =
                legacy.ProfileId;
            ParagonProfile migrated =
                CloneProfile(legacy);

            migrated.ArmoryIndex =
                armoryIndex;
            migrated.Label =
                string.IsNullOrEmpty(label)
                    ? "Armory build"
                    : label;
            migrated.LiveSignature =
                liveKey;
            migrated.BaseKey = baseKey;
            migrated.StableBuildKey =
                stableKey;
            migrated.ProfileId =
                MakeProfileId(migrated);

            if (!string.IsNullOrEmpty(oldId))
                _profiles.Remove(oldId);

            RemoveSupersededProfiles(migrated);
            _profiles[migrated.ProfileId] =
                migrated;
            SaveSettings();

            return migrated;
        }

        private bool ValidateProfileBuild(
            ParagonProfile profile,
            string liveKey,
            string stableKey,
            out string message)
        {
            message = string.Empty;

            if (profile == null ||
                string.IsNullOrEmpty(stableKey))
            {
                message =
                    "The selected Armory profile could not be validated.";
                return false;
            }

            if (string.IsNullOrEmpty(
                    profile.StableBuildKey))
            {
                if (string.Equals(
                        profile.LiveSignature,
                        liveKey,
                        StringComparison.Ordinal))
                {
                    profile.StableBuildKey =
                        stableKey;
                    profile.UpdatedUtcTicks =
                        DateTime.UtcNow.Ticks;
                    SaveSettings();
                    return true;
                }

                message =
                    "This saved profile predates native slot validation. " +
                    "Open Paragon and save it once for this Armory preset.";
                return false;
            }

            if (!string.Equals(
                    profile.StableBuildKey,
                    stableKey,
                    StringComparison.Ordinal))
            {
                message =
                    "This Armory slot now contains a different build. " +
                    "Paragon was left unchanged; save the slot again " +
                    "only if the new build should replace its profile.";
                return false;
            }

            return true;
        }

        private void UpdateProfileRuntimeMetadata(
            ParagonProfile profile,
            string liveKey,
            string baseKey,
            string label)
        {
            if (profile == null)
                return;

            bool changed = false;

            if (!string.Equals(
                    profile.LiveSignature,
                    liveKey,
                    StringComparison.Ordinal))
            {
                profile.LiveSignature =
                    liveKey;
                changed = true;
            }

            if (!string.Equals(
                    profile.BaseKey,
                    baseKey,
                    StringComparison.Ordinal))
            {
                profile.BaseKey = baseKey;
                changed = true;
            }

            if (!string.IsNullOrEmpty(label) &&
                !string.Equals(
                    profile.Label,
                    label,
                    StringComparison.Ordinal))
            {
                profile.Label = label;
                changed = true;
            }

            if (changed)
                SaveSettings();
        }

        private static string SafeBuildLabel(
            string label)
        {
            return string.IsNullOrEmpty(label)
                ? "Armory build"
                : label.Replace("\"", "'");
        }

        private void RefreshCurrentBuildDisplay()
        {
            _currentBuildDisplayLabel =
                "current build";

            if (_activeArmoryIndex < 0)
            {
                // A stable startup profile hint may identify the semantic build
                // even when several native slots share the same public layout.
                // It is display-only: exact slot actions still require native
                // Armory page/row/name confirmation.
                int ignoredIndex;
                ParagonProfile hint =
                    FindIndexedProfileForCurrentBuild(
                        out ignoredIndex);

                if (hint != null &&
                    !string.IsNullOrEmpty(
                        hint.Label))
                {
                    _currentBuildDisplayLabel =
                        hint.Label;
                }

                return;
            }

            string baseKey =
                GetLiveArmoryKey();

            if (!CurrentBuildMatchesArmoryIndex(
                    _activeArmoryIndex,
                    baseKey))
                return;

            IPlayerArmorySet set =
                GetArmorySetByIndex(
                    _activeArmoryIndex);

            string label =
                set != null &&
                !string.IsNullOrEmpty(set.Name)
                    ? set.Name
                    : _activeArmoryName;

            if (string.IsNullOrEmpty(label))
                return;

            _currentBuildDisplayLabel = label;
        }

        private NativeFingerprint ReadNativeFingerprint()
        {
            IPlayer me = Hud.Game.Me;
            if (me == null || me.Stats == null)
                return null;

            NativeFingerprint fingerprint =
                new NativeFingerprint();
            IAttributeList attributes =
                Hud.Sno.Attributes;

            SetNativeFingerprintValue(
                fingerprint,
                0,
                ReadPrimaryStat(me, attributes));
            SetNativeFingerprintValue(
                fingerprint,
                1,
                me.Stats.Vitality);
            SetNativeFingerprintValue(
                fingerprint,
                2,
                me.Stats.MoveSpeed);
            SetNativeFingerprintValue(
                fingerprint,
                3,
                me.Stats.ResourceMaxPri);

            SetNativeFingerprintValue(
                fingerprint,
                5,
                ReadAnyAttribute(
                    me,
                    attributes.Power_Cooldown_Reduction_Percent_All));
            SetNativeFingerprintValue(
                fingerprint,
                6,
                ReadAnyAttribute(
                    me,
                    attributes.Crit_Percent_Bonus_Capped));
            SetNativeFingerprintValue(
                fingerprint,
                7,
                ReadAnyAttribute(
                    me,
                    attributes.Crit_Damage_Percent));

            SetNativeFingerprintValue(
                fingerprint,
                8,
                ReadAnyAttribute(
                    me,
                    attributes.Hitpoints_Max_Percent_Bonus));
            SetNativeFingerprintValue(
                fingerprint,
                10,
                ReadAnyAttribute(
                    me,
                    attributes.Resistance_All));
            SetNativeFingerprintValue(
                fingerprint,
                11,
                ReadAnyAttribute(
                    me,
                    attributes.Hitpoints_Regen_Per_Second));

            SetNativeFingerprintValue(
                fingerprint,
                12,
                ReadAnyAttribute(
                    me,
                    attributes.Splash_Damage_Effect_Percent));
            SetNativeFingerprintValue(
                fingerprint,
                13,
                ReadAnyAttribute(
                    me,
                    attributes.Resource_Cost_Reduction_Percent_All));
            SetNativeFingerprintValue(
                fingerprint,
                14,
                ReadAnyAttribute(
                    me,
                    attributes.Hitpoints_On_Hit));

            fingerprint.CoreCrossChecks =
                BuildCoreCrossChecks(
                    me,
                    attributes,
                    out fingerprint.MoveSpeedSaturated);
            fingerprint.CategorySpent =
                ReadNativeCategorySpent(me);
            return fingerprint;
        }

        private static void SetNativeFingerprintValue(
            NativeFingerprint fingerprint,
            int row,
            double value)
        {
            fingerprint.Values[row] = value;
            fingerprint.Valid[row] =
                IsFinite(value);
        }

        private double ReadPrimaryStat(
            IPlayer player,
            IAttributeList attributes)
        {
            try
            {
                HeroClass heroClass =
                    player.HeroClassDefinition != null
                        ? player.HeroClassDefinition.HeroClass
                        : HeroClass.None;
                IAttribute attribute = null;

                switch (heroClass)
                {
                    case HeroClass.Barbarian:
                    case HeroClass.Crusader:
                        attribute = attributes.Strength;
                        break;
                    case HeroClass.DemonHunter:
                    case HeroClass.Monk:
                        attribute = attributes.Dexterity;
                        break;
                    case HeroClass.Wizard:
                    case HeroClass.WitchDoctor:
                    case HeroClass.Necromancer:
                        attribute = attributes.Intelligence;
                        break;
                }

                double value =
                    ReadAnyAttribute(
                        player,
                        attribute);
                return IsFinite(value)
                    ? value
                    : player.Stats.MainStat;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static double ReadAnyAttribute(
            IPlayer player,
            IAttribute attribute)
        {
            double value = ReadAttribute(
                player,
                attribute,
                AnyAttributeModifier);
            if (IsFinite(value))
                return value;

            value = ReadAttribute(
                player,
                attribute,
                LegacyAnyAttributeModifier);
            if (IsFinite(value))
                return value;

            return ReadAttribute(
                player,
                attribute,
                0u);
        }

        private static double ReadAttribute(
            IPlayer player,
            IAttribute attribute,
            uint modifier)
        {
            if (player == null ||
                attribute == null)
            {
                return double.NaN;
            }

            try
            {
                return player.GetAttributeValue(
                    attribute,
                    modifier,
                    double.NaN);
            }
            catch
            {
                return double.NaN;
            }
        }

        private string BuildCoreCrossChecks(
            IPlayer player,
            IAttributeList attributes,
            out bool moveSpeedSaturated)
        {
            Dictionary<string, double> values =
                new Dictionary<string, double>(
                    StringComparer.Ordinal);

            AddNativeCrossCheck(
                values,
                "Movement_Scalar_Subtotal",
                ReadAnyAttribute(
                    player,
                    attributes.Movement_Scalar_Subtotal));
            AddNativeCrossCheck(
                values,
                "Movement_Scalar_Uncapped_Bonus",
                ReadAnyAttribute(
                    player,
                    attributes.Movement_Scalar_Uncapped_Bonus));
            AddNativeCrossCheck(
                values,
                "Movement_Bonus_Run_Speed",
                ReadAnyAttribute(
                    player,
                    attributes.Movement_Bonus_Run_Speed));
            AddNativeCrossCheck(
                values,
                "Movement_Scalar_Capped_Total",
                ReadAnyAttribute(
                    player,
                    attributes.Movement_Scalar_Capped_Total));
            AddNativeCrossCheck(
                values,
                "Movement_Scalar_Cap",
                ReadAnyAttribute(
                    player,
                    attributes.Movement_Scalar_Cap));

            double move = player.Stats.MoveSpeed;
            double cap = GetNativeCrossCheck(
                values,
                "Movement_Scalar_Cap");
            double capped = GetNativeCrossCheck(
                values,
                "Movement_Scalar_Capped_Total");

            moveSpeedSaturated =
                move >= 24.999d ||
                (IsFinite(cap) &&
                 IsFinite(capped) &&
                 capped >= cap - 0.0001d);

            List<string> keys =
                new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);

            StringBuilder sb =
                new StringBuilder(384);
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');

                sb.Append(keys[i])
                    .Append('=')
                    .Append(
                        values[keys[i]].ToString(
                            "R",
                            CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static void AddNativeCrossCheck(
            Dictionary<string, double> values,
            string key,
            double value)
        {
            if (IsFinite(value))
                values[key] = value;
        }

        private static double GetNativeCrossCheck(
            Dictionary<string, double> values,
            string key)
        {
            double value;
            return values != null &&
                values.TryGetValue(key, out value)
                    ? value
                    : double.NaN;
        }

        private static string ReadNativeCrossCheck(
            string text,
            string key)
        {
            if (string.IsNullOrEmpty(text) ||
                string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string prefix = key + "=";
            string[] parts = text.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                {
                    return parts[i].Substring(
                        prefix.Length);
                }
            }

            return string.Empty;
        }

        private static bool TryReadFirstNativeCrossCheck(
            string text,
            out double value)
        {
            string[] keys =
            {
                "Movement_Scalar_Uncapped_Bonus",
                "Movement_Bonus_Run_Speed",
                "Movement_Scalar_Subtotal"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                string raw = ReadNativeCrossCheck(
                    text,
                    keys[i]);
                if (double.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return true;
                }
            }

            value = double.NaN;
            return false;
        }

        private int[] ReadNativeCategorySpent(
            IPlayer player)
        {
            try
            {
                int level =
                    (int)player.CurrentLevelParagon;
                int capped = Math.Min(
                    800,
                    Math.Max(0, level));
                int[] available =
                    player.ParagonPointsAvailable;

                if (available == null ||
                    available.Length < 4)
                {
                    return null;
                }

                int[] earned = new int[4];
                earned[0] =
                    (capped + 3) / 4 +
                    Math.Max(0, level - 800);
                earned[1] = (capped + 2) / 4;
                earned[2] = (capped + 1) / 4;
                earned[3] = capped / 4;

                int[] spent = new int[4];
                for (int tab = 0; tab < 4; tab++)
                {
                    spent[tab] = Math.Max(
                        0,
                        earned[tab] -
                        available[tab]);
                }

                return spent;
            }
            catch
            {
                return null;
            }
        }

        private void NormalizeNativeFingerprint(
            ParagonProfile profile,
            NativeFingerprint fingerprint)
        {
            if (profile == null ||
                fingerprint == null ||
                profile.OverflowRow != PrimaryRow ||
                !fingerprint.Valid[0] ||
                !fingerprint.Valid[10])
            {
                return;
            }

            try
            {
                HeroClass heroClass =
                    Hud.Game.Me != null &&
                    Hud.Game.Me.HeroClassDefinition != null
                        ? Hud.Game.Me.HeroClassDefinition.HeroClass
                        : HeroClass.None;

                if (heroClass == HeroClass.Wizard ||
                    heroClass == HeroClass.WitchDoctor ||
                    heroClass == HeroClass.Necromancer)
                {
                    // Intelligence contributes 0.1 All Resistance per point.
                    // Remove that dynamic overflow contribution so routine Core
                    // growth cannot masquerade as a Defense-row change.
                    fingerprint.Values[10] -=
                        fingerprint.Values[0] * 0.1d;
                }
            }
            catch { }
        }

        private string BuildNativeFingerprintSignature(
            NativeFingerprint fingerprint,
            ParagonProfile profile)
        {
            int overflow =
                profile != null &&
                profile.OverflowRow == VitalityRow
                    ? VitalityRow
                    : PrimaryRow;

            StringBuilder sb =
                new StringBuilder(512);

            for (int row = 0; row < 16; row++)
            {
                if (row > 0)
                    sb.Append('|');

                if (row == overflow ||
                    IsNativeInferredRow(row))
                {
                    sb.Append('*');
                    continue;
                }

                if (!fingerprint.Valid[row])
                {
                    sb.Append("NA");
                    continue;
                }

                double step = Math.Max(
                    NativeRowTolerances[row] * 0.25d,
                    0.0000001d);
                double normalized = Math.Round(
                    fingerprint.Values[row] / step) * step;
                sb.Append(
                    normalized.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
            }

            sb.Append("|sat=")
                .Append(
                    fingerprint.MoveSpeedSaturated);

            if (fingerprint.CategorySpent != null &&
                fingerprint.CategorySpent.Length >= 4)
            {
                sb.Append("|spent=")
                    .Append(
                        fingerprint.CategorySpent[1])
                    .Append(',')
                    .Append(
                        fingerprint.CategorySpent[2])
                    .Append(',')
                    .Append(
                        fingerprint.CategorySpent[3]);
            }
            else
            {
                sb.Append("|spent=NA");
            }

            return sb.ToString();
        }

        private bool NativeFingerprintHasRequiredSignals(
            ParagonProfile profile,
            NativeFingerprint fingerprint,
            bool requireAccounting)
        {
            if (profile == null ||
                fingerprint == null)
            {
                return false;
            }

            int overflow =
                profile.OverflowRow == VitalityRow
                    ? VitalityRow
                    : PrimaryRow;

            for (int row = 0; row < 16; row++)
            {
                if (row == overflow ||
                    IsNativeInferredRow(row))
                {
                    continue;
                }

                if (!fingerprint.Valid[row])
                    return false;
            }

            if (fingerprint.MoveSpeedSaturated)
            {
                double move;
                if (!TryReadFirstNativeCrossCheck(
                        fingerprint.CoreCrossChecks,
                        out move))
                {
                    return false;
                }
            }

            return !requireAccounting ||
                (fingerprint.CategorySpent != null &&
                 fingerprint.CategorySpent.Length >= 4);
        }

        private bool NativeFingerprintMatches(
            ParagonProfile profile,
            NativeFingerprint live)
        {
            NativeFingerprint saved =
                profile != null
                    ? profile.NativeFingerprint
                    : null;

            if (profile == null ||
                saved == null ||
                live == null)
            {
                return false;
            }

            int overflow =
                profile.OverflowRow == VitalityRow
                    ? VitalityRow
                    : PrimaryRow;

            for (int row = 0; row < 16; row++)
            {
                if (row == overflow ||
                    IsNativeInferredRow(row))
                {
                    continue;
                }

                if (!saved.Valid[row] ||
                    !live.Valid[row] ||
                    Math.Abs(
                        saved.Values[row] -
                        live.Values[row]) >
                        NativeRowTolerances[row])
                {
                    return false;
                }
            }

            if (saved.MoveSpeedSaturated ||
                live.MoveSpeedSaturated)
            {
                double savedMove;
                double liveMove;
                if (!TryReadFirstNativeCrossCheck(
                        saved.CoreCrossChecks,
                        out savedMove) ||
                    !TryReadFirstNativeCrossCheck(
                        live.CoreCrossChecks,
                        out liveMove) ||
                    Math.Abs(savedMove - liveMove) >
                        0.00025d)
                {
                    return false;
                }
            }

            if (live.CategorySpent == null ||
                live.CategorySpent.Length < 4)
            {
                return false;
            }

            for (int tab = 1; tab < 4; tab++)
            {
                int expected = 0;
                for (int row = 0; row < 4; row++)
                    expected += profile.Rows[tab, row];

                if (live.CategorySpent[tab] != expected)
                    return false;
            }

            return true;
        }

        private static bool IsNativeInferredRow(
            int row)
        {
            return row == NativeInferredRows[0] ||
                row == NativeInferredRows[1] ||
                row == NativeInferredRows[2];
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value);
        }

        private void BuildResources()
        {
            _smallFont = Hud.Render.CreateFont("tahoma", 7.4f, 255, 245, 245, 245, false, false, 255, 0, 0, 0, true);
            _instructionFont = Hud.Render.CreateFont("tahoma", 7.4f, 255, 255, 220, 55, false, false, 255, 0, 0, 0, true);
            _profileSavedFont = Hud.Render.CreateFont("tahoma", 7.4f, 255, 75, 235, 95, false, false, 255, 0, 0, 0, true);
            _profileMissingFont = Hud.Render.CreateFont("tahoma", 7.4f, 255, 245, 75, 65, false, false, 255, 0, 0, 0, true);
            _warningFont = Hud.Render.CreateFont("tahoma", 9.2f, 255, 255, 82, 72, true, false, 255, 0, 0, 0, true);
            _warningOutlineFont = Hud.Render.CreateFont("tahoma", 9.2f, 255, 0, 0, 0, true, false, false);
            _warningFont.WordWrap = true;
            _warningOutlineFont.WordWrap = true;
            _saveButtonBackBrush = Hud.Render.CreateBrush(205, 20, 25, 32, 0);
            _saveButtonHoverBrush = Hud.Render.CreateBrush(225, 35, 48, 62, 0);
            _saveButtonBorderBrush = Hud.Render.CreateBrush(235, 220, 180, 55, 1.4f);
            _toggleBackBrush = Hud.Render.CreateBrush(205, 20, 25, 32, 0);
            _toggleHoverBrush = Hud.Render.CreateBrush(225, 35, 48, 62, 0);
            _toggleBorderBrush = Hud.Render.CreateBrush(235, 185, 145, 45, 1.2f);
            _toggleSelectedBackBrush = Hud.Render.CreateBrush(220, 28, 55, 34, 0);
            _toggleSelectedBorderBrush = Hud.Render.CreateBrush(245, 55, 235, 85, 2.4f);
            _toggleIndicatorOnBrush = Hud.Render.CreateBrush(245, 55, 235, 85, 0);
            _toggleIndicatorOffBrush = Hud.Render.CreateBrush(230, 32, 35, 42, 0);
        }

        private void ResolvePaths()
        {
            try
            {
                string settingsDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "plugins", "s7o", "settings");
                Directory.CreateDirectory(settingsDir);
                _settingsPath = Path.Combine(settingsDir, SettingsFileName);
            }
            catch
            {
                _settingsPath = null;
            }
        }

        private void LoadSettings()
        {
            ClearAllKnownLayouts();
            _disabledHeroIds.Clear();

            if (string.IsNullOrEmpty(_settingsPath) ||
                !File.Exists(_settingsPath))
                return;

            try
            {
                foreach (string raw in
                    File.ReadAllLines(_settingsPath))
                {
                    string line =
                        (raw ?? string.Empty).Trim();

                    if (line.Length == 0 ||
                        line.StartsWith(
                            "#",
                            StringComparison.Ordinal))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key =
                        line.Substring(0, eq).Trim();
                    string value =
                        line.Substring(eq + 1).Trim();

                    if (key.Equals(
                            "ParagonWindowKey",
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        try
                        {
                            ParagonWindowKey =
                                (Keys)Enum.Parse(
                                    typeof(Keys),
                                    value,
                                    true);
                        }
                        catch { }
                    }
                    else if (key.Equals(
                            "HeroDisabled",
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        uint heroId;
                        if (uint.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out heroId) &&
                            heroId != 0u)
                        {
                            _disabledHeroIds.Add(heroId);
                        }
                    }
                    else if (key.Equals(
                            "Profile9",
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        ParagonProfile profile;
                        if (TryParseProfile9(
                                value,
                                out profile))
                        {
                            StoreLoadedProfile(profile);
                        }
                    }
                }
            }
            catch { }
        }

        private void StoreLoadedProfile(
            ParagonProfile profile)
        {
            if (profile == null ||
                profile.HeroId == 0u ||
                string.IsNullOrEmpty(
                    profile.LiveSignature))
                return;

            string key;
            if (profile.ArmoryIndex >= 0)
            {
                key = MakeIndexedProfileId(
                    profile.HeroId,
                    profile.ArmoryIndex);
                profile.ProfileId = key;
            }
            else
            {
                key =
                    !string.IsNullOrEmpty(
                        profile.ProfileId)
                        ? profile.ProfileId
                        : MakeProfileId(profile);
                profile.ProfileId = key;
            }

            ParagonProfile existing;
            if (_profiles.TryGetValue(
                    key,
                    out existing) &&
                existing != null &&
                existing.UpdatedUtcTicks >
                    profile.UpdatedUtcTicks)
            {
                return;
            }

            _profiles[key] = profile;
        }

        private void SaveSettings()
        {
            if (string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                StringBuilder sb =
                    new StringBuilder();

                sb.AppendLine(
                    "# s7o_Paragon_Builds native slot profiles");

                sb.AppendLine(
                    "SettingsVersion=" +
                    SettingsVersion.ToString(
                        CultureInfo.InvariantCulture));

                sb.AppendLine(
                    "ParagonWindowKey=" +
                    ParagonWindowKey.ToString());

                List<uint> disabledHeroIds =
                    new List<uint>(_disabledHeroIds);
                disabledHeroIds.Sort();

                for (int i = 0;
                    i < disabledHeroIds.Count;
                    i++)
                {
                    sb.AppendLine(
                        "HeroDisabled=" +
                        disabledHeroIds[i].ToString(
                            CultureInfo.InvariantCulture));
                }

                foreach (
                    ParagonProfile profile
                    in _profiles.Values)
                {
                    if (profile == null ||
                        profile.HeroId == 0u ||
                        string.IsNullOrEmpty(
                            profile.LiveSignature))
                    {
                        continue;
                    }

                    sb.Append("Profile9=");
                    sb.Append(
                        profile.HeroId.ToString(
                            CultureInfo
                                .InvariantCulture));

                    sb.Append('|').Append(
                        ToBase64(
                            profile.ProfileId ??
                            string.Empty));

                    sb.Append('|').Append(
                        ToBase64(
                            profile.LiveSignature ??
                            string.Empty));

                    sb.Append('|').Append(
                        ToBase64(
                            profile.BaseKey ??
                            string.Empty));

                    sb.Append('|').Append(
                        profile.ArmoryIndex
                            .ToString(
                                CultureInfo
                                    .InvariantCulture));

                    sb.Append('|').Append(
                        profile.OverflowRow
                            .ToString(
                                CultureInfo
                                    .InvariantCulture));

                    sb.Append('|').Append(
                        profile.OverflowExplicit
                            ? "1"
                            : "0");

                    sb.Append('|').Append(
                        RowsText(profile.Rows));

                    sb.Append('|').Append(
                        profile.UpdatedUtcTicks
                            .ToString(
                                CultureInfo
                                    .InvariantCulture));

                    sb.Append('|').Append(
                        ToBase64(
                            profile.Label ??
                            string.Empty));

                    // Optional extensions remain backward-readable by older
                    // Profile9 parsers, which ignore fields after the label.
                    sb.Append('|').Append(
                        ToBase64(
                            profile.StableBuildKey ??
                            string.Empty));

                    AppendNativeFingerprintSettings(
                        sb,
                        profile.NativeFingerprint);

                    sb.AppendLine();
                }

                WriteSettingsAtomically(
                    sb.ToString());
            }
            catch { }
        }

        private void WriteSettingsAtomically(
            string content)
        {
            string temporary =
                _settingsPath + ".tmp";

            try
            {
                File.WriteAllText(
                    temporary,
                    content ?? string.Empty);

                if (File.Exists(_settingsPath))
                {
                    try
                    {
                        File.Replace(
                            temporary,
                            _settingsPath,
                            null);
                        return;
                    }
                    catch
                    {
                        File.Copy(
                            temporary,
                            _settingsPath,
                            true);
                        File.Delete(temporary);
                        return;
                    }
                }

                File.Move(
                    temporary,
                    _settingsPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch { }
            }
        }

        private bool TryParseProfile9(
            string value,
            out ParagonProfile profile)
        {
            profile = null;

            try
            {
                string[] parts =
                    value.Split('|');

                if (parts.Length < 10)
                    return false;

                uint heroId;
                int armoryIndex;
                int overflow;
                int explicitValue;
                int[,] rows;
                long ticks;

                if (!uint.TryParse(
                        parts[0],
                        NumberStyles.Integer,
                        CultureInfo
                            .InvariantCulture,
                        out heroId) ||
                    !int.TryParse(
                        parts[4],
                        NumberStyles.Integer,
                        CultureInfo
                            .InvariantCulture,
                        out armoryIndex) ||
                    !int.TryParse(
                        parts[5],
                        NumberStyles.Integer,
                        CultureInfo
                            .InvariantCulture,
                        out overflow) ||
                    !int.TryParse(
                        parts[6],
                        NumberStyles.Integer,
                        CultureInfo
                            .InvariantCulture,
                        out explicitValue) ||
                    !TryParseRows(
                        parts[7],
                        out rows) ||
                    !long.TryParse(
                        parts[8],
                        NumberStyles.Integer,
                        CultureInfo
                            .InvariantCulture,
                        out ticks))
                {
                    return false;
                }

                profile = new ParagonProfile
                {
                    HeroId = heroId,
                    ProfileId =
                        FromBase64(parts[1]),
                    LiveSignature =
                        FromBase64(parts[2]),
                    BaseKey =
                        FromBase64(parts[3]),
                    ArmoryIndex =
                        armoryIndex,
                    OverflowRow =
                        NormalizeOverflowRow(
                            overflow,
                            rows),
                    OverflowExplicit =
                        explicitValue != 0,
                    Rows = rows,
                    UpdatedUtcTicks =
                        ticks,
                    Label =
                        FromBase64(parts[9]),
                    StableBuildKey =
                        parts.Length >= 11
                            ? FromBase64(
                                parts[10])
                            : string.Empty,
                    NativeFingerprint =
                        ParseNativeFingerprintSettings(
                            parts)
                };

                return profile.HeroId != 0u &&
                    !string.IsNullOrEmpty(
                        profile.LiveSignature);
            }
            catch
            {
                return false;
            }
        }

        private static void AppendNativeFingerprintSettings(
            StringBuilder sb,
            NativeFingerprint fingerprint)
        {
            if (fingerprint == null)
            {
                sb.Append("|0|||");
                sb.Append('|');
                return;
            }

            StringBuilder mask =
                new StringBuilder(16);
            StringBuilder values =
                new StringBuilder(256);

            for (int i = 0; i < 16; i++)
            {
                if (i > 0)
                    values.Append(',');

                mask.Append(
                    fingerprint.Valid[i]
                        ? '1'
                        : '0');

                values.Append(
                    fingerprint.Valid[i]
                        ? fingerprint.Values[i]
                            .ToString(
                                "R",
                                CultureInfo.InvariantCulture)
                        : "NA");
            }

            sb.Append('|').Append(
                NativeFingerprintSchemaVersion);
            sb.Append('|').Append(mask);
            sb.Append('|').Append(values);
            sb.Append('|').Append(
                fingerprint.MoveSpeedSaturated
                    ? "1"
                    : "0");
            sb.Append('|').Append(
                ToBase64(
                    fingerprint.CoreCrossChecks ??
                    string.Empty));
        }

        private static NativeFingerprint
            ParseNativeFingerprintSettings(
                string[] parts)
        {
            if (parts == null ||
                parts.Length < 16)
            {
                return null;
            }

            int schema;
            if (!int.TryParse(
                    parts[11],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out schema) ||
                schema !=
                    NativeFingerprintSchemaVersion ||
                parts[12].Length != 16)
            {
                return null;
            }

            string[] values =
                parts[13].Split(',');
            if (values.Length != 16)
                return null;

            NativeFingerprint fingerprint =
                new NativeFingerprint();

            for (int i = 0; i < 16; i++)
            {
                fingerprint.Valid[i] =
                    parts[12][i] == '1';

                double value = double.NaN;
                if (fingerprint.Valid[i] &&
                    !double.TryParse(
                        values[i],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return null;
                }

                fingerprint.Values[i] = value;
            }

            fingerprint.MoveSpeedSaturated =
                parts[14] == "1";
            fingerprint.CoreCrossChecks =
                FromBase64(parts[15]);
            return fingerprint;
        }

        private static bool TryParseRows(string text, out int[,] rows)
        {
            rows = null;
            string[] points = (text ?? string.Empty).Split(',');
            if (points.Length != 16) return false;
            int[,] parsed = new int[4, 4];
            int index = 0;
            for (int tab = 0; tab < 4; tab++)
            {
                for (int row = 0; row < 4; row++)
                {
                    int point;
                    if (!int.TryParse(points[index++], NumberStyles.Integer, CultureInfo.InvariantCulture, out point))
                        return false;
                    parsed[tab, row] = Math.Max(0, point);
                }
            }
            rows = parsed;
            return true;
        }

        private static string RowsText(int[,] rows)
        {
            if (rows == null) return string.Empty;
            StringBuilder sb = new StringBuilder();
            for (int tab = 0; tab < 4; tab++)
            {
                for (int row = 0; row < 4; row++)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(rows[tab, row].ToString(CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        private static string ToBase64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        private static string FromBase64(string text)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(text ?? string.Empty)); }
            catch { return string.Empty; }
        }

        private static ParagonProfile CloneProfile(
            ParagonProfile source)
        {
            if (source == null)
                return null;

            int[,] rows = new int[4, 4];

            for (int tab = 0; tab < 4; tab++)
            {
                for (int row = 0; row < 4; row++)
                {
                    rows[tab, row] =
                        source.Rows[tab, row];
                }
            }

            return new ParagonProfile
            {
                HeroId = source.HeroId,
                ProfileId = source.ProfileId,
                LiveSignature =
                    source.LiveSignature,
                BaseKey = source.BaseKey,
                StableBuildKey =
                    source.StableBuildKey,
                ArmoryIndex =
                    source.ArmoryIndex,
                Label = source.Label,
                Rows = rows,
                OverflowRow =
                    source.OverflowRow,
                OverflowExplicit =
                    source.OverflowExplicit,
                UpdatedUtcTicks =
                    source.UpdatedUtcTicks,
                NativeFingerprint =
                    source.NativeFingerprint != null
                        ? source.NativeFingerprint.Clone()
                        : null
            };
        }

        private void EnsureSessionConfidence(
            int now)
        {
            uint heroId = CurrentHeroId;

            if (heroId == 0u)
                return;

            if (_sessionHeroId == 0u)
            {
                _sessionHeroId = heroId;
                RestoreKnownLayoutMirror(heroId);
                return;
            }

            if (_sessionHeroId != heroId)
            {
                InvalidateSessionState(
                    now,
                    "hero changed",
                    true);

                _sessionHeroId = heroId;
                RestoreKnownLayoutMirror(heroId);
                return;
            }

            // Foreground and loading gaps do not change the character's
            // accepted Paragon allocation. Pause input while unavailable,
            // but preserve one in-memory layout record per hero.
        }

        private void InvalidateSessionState(
            int now,
            string reason,
            bool clearPendingEquip)
        {
            ResetNativeTask();
            _passiveNativeVerifyAttemptKey =
                string.Empty;

            _activeArmoryIndex = -1;
            _activeArmoryName = string.Empty;
            _currentStableBuildKey =
                string.Empty;
            _currentBuildDisplayLabel =
                "current build";

            if (clearPendingEquip)
                ClearPendingEquip();

            ClearWarning();
        }

        private void ShowWarning(string text)
        {
            _warningText = text ?? string.Empty;
            _warningUntilTick = unchecked(
                Environment.TickCount + WarningHoldMs);
        }

        private void ClearWarning()
        {
            _warningText = string.Empty;
            _warningUntilTick = NoTick;
        }

        private string GetWarning()
        {
            if (!string.IsNullOrEmpty(_warningText) &&
                _warningUntilTick != NoTick &&
                unchecked(Environment.TickCount - _warningUntilTick) < 0)
                return _warningText;
            return string.Empty;
        }

        private void DrawGlobalWarning()
        {
            string warning = GetWarning();
            if (string.IsNullOrEmpty(warning) ||
                _warningFont == null ||
                _warningOutlineFont == null ||
                !IsArmoryOpen())
                return;

            try
            {
                System.Drawing.RectangleF root = SafeRect(_armoryRoot);
                if (root.Width <= 0 || root.Height <= 0)
                    return;

                float maxWidth = Math.Max(260.0f,
                    Math.Min(460.0f, root.Width * WarningPaneWidthRel));
                string[] lines = WrapWarningToTwoLines(warning, maxWidth);
                if (lines == null || lines.Length == 0)
                    return;

                var layouts = new SharpDX.DirectWrite.TextLayout[lines.Length];
                float blockHeight = 0.0f;
                for (int i = 0; i < lines.Length; i++)
                {
                    layouts[i] = _warningFont.GetTextLayout(lines[i]);
                    if (layouts[i] == null)
                        return;
                    blockHeight += layouts[i].Metrics.Height;
                    if (i > 0)
                        blockHeight += WarningLineGap;
                }

                float centerX = root.Left + root.Width * 0.5f;
                float centerY = root.Top + root.Height * WarningPaneCenterYRel;
                float y = centerY - blockHeight * 0.5f;

                for (int i = 0; i < layouts.Length; i++)
                {
                    var layout = layouts[i];
                    float x = centerX - layout.Metrics.Width * 0.5f;
                    DrawOutlinedWarningLine(layout, x, y);
                    y += layout.Metrics.Height + WarningLineGap;
                }
            }
            catch { }
        }

        private string[] WrapWarningToTwoLines(string text, float maxWidth)
        {
            string cleaned = CleanText(text);
            if (string.IsNullOrEmpty(cleaned))
                return new string[0];

            string[] words = cleaned.Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                return new[] { cleaned };

            int bestSplit = 1;
            float bestScore = float.MaxValue;
            for (int split = 1; split < words.Length; split++)
            {
                string first = string.Join(" ", words, 0, split);
                string second = string.Join(" ", words, split,
                    words.Length - split);
                float firstWidth = MeasureWarningText(first);
                float secondWidth = MeasureWarningText(second);
                float overflow = Math.Max(0.0f, firstWidth - maxWidth) +
                    Math.Max(0.0f, secondWidth - maxWidth);
                float balance = Math.Abs(firstWidth - secondWidth) * 0.15f;
                float score = overflow * 20.0f +
                    Math.Max(firstWidth, secondWidth) + balance;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestSplit = split;
                }
            }

            return new[]
            {
                string.Join(" ", words, 0, bestSplit),
                string.Join(" ", words, bestSplit,
                    words.Length - bestSplit)
            };
        }

        private float MeasureWarningText(string text)
        {
            try
            {
                var layout = _warningFont.GetTextLayout(text ?? string.Empty);
                return layout != null ? layout.Metrics.Width : 0.0f;
            }
            catch { return 0.0f; }
        }

        private void DrawOutlinedWarningLine(
            SharpDX.DirectWrite.TextLayout layout,
            float x,
            float y)
        {
            if (layout == null)
                return;

            float o = WarningOutlineOffset;
            _warningOutlineFont.DrawText(layout, x - o, y - o);
            _warningOutlineFont.DrawText(layout, x, y - o);
            _warningOutlineFont.DrawText(layout, x + o, y - o);
            _warningOutlineFont.DrawText(layout, x - o, y);
            _warningOutlineFont.DrawText(layout, x + o, y);
            _warningOutlineFont.DrawText(layout, x - o, y + o);
            _warningOutlineFont.DrawText(layout, x, y + o);
            _warningOutlineFont.DrawText(layout, x + o, y + o);
            _warningFont.DrawText(layout, x, y);
        }

        private void ShowStatus(string text)
        {
            _statusText = text ?? string.Empty;
            _statusUntilTick = unchecked(Environment.TickCount + StatusHoldMs);
        }

        private string GetStatus()
        {
            if (!string.IsNullOrEmpty(_statusText) &&
                _statusUntilTick != NoTick &&
                unchecked(Environment.TickCount - _statusUntilTick) < 0)
                return _statusText;
            return string.Empty;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (value.Contains("  ")) value = value.Replace("  ", " ");
            return value;
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

        private static bool PointInRect(System.Drawing.RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 &&
                x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        private static bool TickReached(int now, int tick)
        {
            return tick == NoTick || unchecked(now - tick) >= 0;
        }

        private enum NativeTaskKind
        {
            None,
            Verify,
            Capture
        }

        private enum ApplyState
        {
            Idle,
            OpenParagon,
            WaitParagon,
            SelectTab,
            WaitTab,
            CheckTab,
            ResetTab,
            WaitReset,
            PrepareRow,
            SpendOverflow,
            BurstModifierLead,
            BurstClick,
            BurstModifierTail,
            BurstSettle,
            Accept,
            WaitAccept,
            CloseParagon,
            WaitClose
        }

        private enum TabPlan
        {
            Invalid,
            Match,
            Spend,
            Reset
        }

        private enum ClickModifier
        {
            None,
            Shift,
            Control
        }

        private sealed class ParagonProfile
        {
            public uint HeroId;
            public string ProfileId;
            public string LiveSignature;
            public string BaseKey;
            public string StableBuildKey;
            public int ArmoryIndex = -1;
            public string Label;
            public int[,] Rows;
            public int OverflowRow;
            public bool OverflowExplicit;
            public long UpdatedUtcTicks;
            public NativeFingerprint NativeFingerprint;
        }

        private sealed class NativeFingerprint
        {
            public readonly double[] Values =
                new double[16];
            public readonly bool[] Valid =
                new bool[16];
            public bool MoveSpeedSaturated;
            public string CoreCrossChecks =
                string.Empty;
            public int[] CategorySpent;

            public NativeFingerprint Clone()
            {
                NativeFingerprint copy =
                    new NativeFingerprint();
                Array.Copy(
                    Values,
                    copy.Values,
                    Values.Length);
                Array.Copy(
                    Valid,
                    copy.Valid,
                    Valid.Length);
                copy.MoveSpeedSaturated =
                    MoveSpeedSaturated;
                copy.CoreCrossChecks =
                    CoreCrossChecks ?? string.Empty;
                copy.CategorySpent =
                    CategorySpent != null
                        ? (int[])CategorySpent.Clone()
                        : null;
                return copy;
            }
        }

        private struct RowState
        {
            public bool Valid;
            public int Assigned;
            public bool HasCap;
            public int Cap;
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
