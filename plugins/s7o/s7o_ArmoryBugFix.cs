namespace Turbo.Plugins.s7o
{
    using System;
    using System.Drawing;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Forms;
    using Turbo.Plugins.Default;

    public class s7o_ArmoryBugFix : BasePlugin, IAfterCollectHandler, IChatLineChangedHandler
    {
        // Do not inspect the transaction until shortly after physical release.
        private const int FirstValidateAfterReleaseMs = 350;

        // A physical press that never receives a release edge is cancelled.
        private const int PhysicalReleaseTimeoutMs = 1500;

        // The first conditional repair must not occur before this point.
        private const int FirstConditionalRetryMs = 850;

        // Use a natural held click only after validation proves a mismatch.
        private const int AssistHoldMs = 100;

        // A complete single-slot mismatch may be repaired earlier, but only after
        // repeated identical observations.
        private const int EarlySingleSlotRetryMs = 500;
        private const int EarlyMismatchStableMs = 140;
        private const int EarlyMismatchRequiredSamples = 3;

        private const int RetrySettleMs = 650;
        private const int RetryIntervalMs = 1100;
        private const int CooldownRetryMs = 1200;
        private const int SlowRetryIntervalMs = 2500;
        private const int UnknownRescanMs = 100;
        private const int MinFullExactConfirmMs = 650;
        private const int TargetDataUnknownAbortMs = 5000;
        private const int TargetAcquireAbortMs = 5000;
        private const int VerifyWindowMs = 60000;
        private const int FastRetryLimit = 8;
        private const int FullExactConfirmSamples = 3;
        private const int MinTargetSetItemMatches = 8;
        private const int NativePageSelectedAnim = 18;

        private static readonly int[,] NativeSelectedRowAnim =
        {
            { 116, 92, 62, 86, 113 },
            { 116, 38, 44, 98, 116 }
        };

        private const float EquipRelX = 0.481008f;
        private const float EquipRelY = 0.773148f;
        private const float EquipRelW = 0.374512f;
        private const float EquipRelH = 0.026852f;
        private const float EquipPadX = 0.020000f;
        private const float EquipPadY = 0.010000f;

        private enum SmartDecision
        {
            Unknown,
            NotNeeded,
            RetryNeeded
        }

        private enum TargetAcquireState
        {
            NoChange,
            Locked,
            Ambiguous,
            IncompleteSnapshot,
            NoCandidate
        }

        private sealed class ArmorySetScan
        {
            public int SetIndex;
            public int MatchCount;
            public int ExactMatchCount;
            public int SavedSlotCount;
            public int SavedItemCount;
            public int ExactSlotCount;
            public int MissingItemCount;
            public string LayoutFingerprint;

            public bool SnapshotComplete
            {
                get { return MissingItemCount == 0; }
            }

            public bool FullExact
            {
                get
                {
                    return SavedSlotCount > 0
                        && MissingItemCount == 0
                        && ExactSlotCount == SavedSlotCount;
                }
            }
        }

        private IUiElement _armory;
        private IUiElement _equipButton;
        private IUiElement _armoryPage1;
        private IUiElement _armoryPage2;
        private IUiElement _armoryLoadoutName;
        private readonly IUiElement[] _armoryTabs = new IUiElement[5];
        private IUiElement _confirmationDialog;
        private IUiElement _confirmationOk;
        private IUiElement _confirmationCancel;

        private bool _leftDownLast;
        private bool _pending;
        private int _clickX;
        private int _clickY;
        private int _stopClickTick = int.MinValue;
        private int _lastAssistTick = int.MinValue;

        private bool _awaitingPhysicalRelease;
        private int _physicalDownTick = int.MinValue;
        private int _physicalReleaseTick = int.MinValue;

        private bool _verifying;
        private int _verifyUntilTick = int.MinValue;
        private int _retryAtTick = int.MinValue;
        private int _verifyRetries;
        private bool _cooldownSeen;
        private int _verifyStartTick = int.MinValue;
        private int _targetDataUnknownSinceTick = int.MinValue;

        private readonly HashSet<int> _preExactSetIndices = new HashSet<int>();
        private readonly HashSet<int> _targetSetIndices = new HashSet<int>();
        private string _preEquipFingerprint = string.Empty;
        private string _targetLayoutFingerprint = string.Empty;
        private bool _targetLocked;
        private bool _nativeTargetLocked;
        private int _requestedArmoryIndex = -1;
        private int _fullExactConfirmSamples;

        private string _earlyMismatchKey = string.Empty;
        private int _earlyMismatchSinceTick = int.MinValue;
        private int _earlyMismatchSamples;

        public s7o_ArmoryBugFix()
        {
            Enabled = true;
            Order = 30205;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            _armory = RegisterUi("Root.NormalLayer.equipmentManager_mainPage", null);
            _equipButton = RegisterUi(
                "Root.NormalLayer.equipmentManager_mainPage.overlay.equip",
                _armory);
            _armoryPage1 = RegisterUi(
                "Root.NormalLayer.equipmentManager_mainPage.armory_pages.page_1",
                _armory);
            _armoryPage2 = RegisterUi(
                "Root.NormalLayer.equipmentManager_mainPage.armory_pages.page_2",
                _armory);
            _armoryLoadoutName = RegisterUi(
                "Root.NormalLayer.equipmentManager_mainPage.loadout_name",
                _armory);
            for (int row = 0; row < _armoryTabs.Length; row++)
            {
                _armoryTabs[row] = RegisterUi(
                    "Root.NormalLayer.equipmentManager_mainPage.tab_" + row,
                    _armory);
            }
            _confirmationDialog = RegisterUi("Root.TopLayer.confirmation.subdlg", null);
            _confirmationOk = RegisterUi("Root.TopLayer.confirmation.subdlg.stack.wrap.button_ok", null);
            _confirmationCancel = RegisterUi("Root.TopLayer.confirmation.subdlg.stack.wrap.button_cancel", null);
        }

        public void ForceStopForDisable()
        {
            // A manager can disable this plugin before another AfterCollect()
            // arrives. Release any synthetic held click and discard the active
            // transaction while callbacks are still available.
            ClearPending();
            ClearVerify();

            // Synchronize the physical edge detector so re-enabling while the
            // mouse button is held cannot fabricate a new Armory Equip press.
            _leftDownLast = IsLeftButtonDown();
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            bool leftDown = IsLeftButtonDown();

            if (!Enabled)
            {
                _leftDownLast = leftDown;
                ClearPending();
                ClearVerify();
                return;
            }

            if (IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                _leftDownLast = leftDown;
                return;
            }

            bool physicalPressed = leftDown && !_leftDownLast;
            bool physicalReleased = !leftDown && _leftDownLast;

            if (physicalPressed)
                HandleArmoryClick(now);

            if (physicalReleased)
                HandleArmoryRelease(now);

            _leftDownLast = leftDown;

            ProcessPending(now);
            ProcessVerify(now);
        }

        public void OnChatLineChanged(string currentLine, string previousLine)
        {
            if (!Enabled ||
                !_verifying ||
                string.IsNullOrEmpty(currentLine))
            {
                return;
            }

            string line = currentLine.ToLowerInvariant();
            if (line.IndexOf("armory", StringComparison.Ordinal) < 0)
                return;

            int now = Environment.TickCount;
            _verifyUntilTick = MaxTick(_verifyUntilTick, unchecked(now + VerifyWindowMs));

            if (line.IndexOf("cooldown", StringComparison.Ordinal) >= 0)
            {
                _cooldownSeen = true;
                _retryAtTick = MaxTick(_retryAtTick, unchecked(now + CooldownRetryMs));
                return;
            }

            if (line.IndexOf("changed", StringComparison.Ordinal) >= 0)
            {
                // A "changed" line generated by the user's physical click must not
                // postpone initial target acquisition. Only apply the settlement
                // delay after this plugin has issued a synthetic repair.
                if (_pending ||
                    _lastAssistTick != int.MinValue ||
                    _verifyRetries > 0)
                {
                    _retryAtTick = MaxTick(
                        _retryAtTick,
                        unchecked(now + RetrySettleMs));
                }

                return;
            }
        }


        private void HandleArmoryClick(int now)
        {
            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            RectangleF hit = GetEquipHitRect();
            if (Inside(hit, Hud.Window.CursorX, Hud.Window.CursorY))
            {
                TryArm(now);
                return;
            }

            if (_pending || _verifying)
            {
                ClearPending();
                ClearVerify();
            }
        }

        private void TryArm(int now)
        {
            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            RectangleF hit = GetEquipHitRect();
            if (!Inside(hit, Hud.Window.CursorX, Hud.Window.CursorY))
                return;

            // Release any old synthetic hold before reusing shared state.
            ClearPending();
            ClearVerify();

            Point assistPoint = GetEquipClickPoint(hit);
            _clickX = assistPoint.X;
            _clickY = assistPoint.Y;

            _physicalDownTick = now;
            _physicalReleaseTick = int.MinValue;
            _awaitingPhysicalRelease = true;

            int selectedIndex;
            bool nativeSelection = TryReadNativeArmorySelection(
                out selectedIndex);

            CapturePreClickState();

            if (string.IsNullOrEmpty(_preEquipFingerprint))
            {
                ClearVerify();
                return;
            }

            if (nativeSelection)
            {
                _requestedArmoryIndex = selectedIndex;
                TryLockNativeTarget(selectedIndex);
            }

            _verifying = true;

            // Verification timing begins after physical release.
            _verifyStartTick = int.MinValue;
            _verifyUntilTick = int.MinValue;
            _retryAtTick = int.MinValue;

            _verifyRetries = 0;
            _lastAssistTick = int.MinValue;
            _targetDataUnknownSinceTick = int.MinValue;
            _cooldownSeen = false;
        }

        private void HandleArmoryRelease(int now)
        {
            if (!_verifying || !_awaitingPhysicalRelease)
                return;

            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            RectangleF hit = GetEquipHitRect();

            // Releasing outside the Equip region is treated as a cancelled click.
            if (!Inside(hit, Hud.Window.CursorX, Hud.Window.CursorY))
            {
                ClearPending();
                ClearVerify();
                return;
            }

            _awaitingPhysicalRelease = false;
            _physicalReleaseTick = now;

            // Use release time as the transaction timing origin.
            _verifyStartTick = now;
            _verifyUntilTick = unchecked(now + VerifyWindowMs);
            _retryAtTick = unchecked(now + FirstValidateAfterReleaseMs);
        }



        private void ProcessPending(int now)
        {
            if (!_pending)
                return;

            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            RectangleF hit = GetEquipHitRect();
            if (!Inside(hit, _clickX, _clickY))
            {
                ClearPending();
                ClearVerify();
                return;
            }

            if (unchecked(now - _stopClickTick) < 0)
                return;

            ArmoryClick.LeftUpPoint(_clickX, _clickY);

            _pending = false;
            _stopClickTick = int.MinValue;

            // Allow the Armory transaction to settle before validating again.
            _retryAtTick = unchecked(now + RetrySettleMs);
            _verifyUntilTick = MaxTick(
                _verifyUntilTick,
                unchecked(now + VerifyWindowMs));
        }


        private void ResetEarlyMismatch()
        {
            _earlyMismatchKey = string.Empty;
            _earlyMismatchSinceTick = int.MinValue;
            _earlyMismatchSamples = 0;
        }

        private void ObserveEarlyMismatch(
            int now,
            string mismatchKey)
        {
            if (string.IsNullOrEmpty(mismatchKey))
            {
                ResetEarlyMismatch();
                return;
            }

            if (!string.Equals(
                _earlyMismatchKey,
                mismatchKey,
                StringComparison.Ordinal))
            {
                _earlyMismatchKey = mismatchKey;
                _earlyMismatchSinceTick = now;
                _earlyMismatchSamples = 1;
                return;
            }

            _earlyMismatchSamples++;
        }

        private bool IsEarlyMismatchStable(int now)
        {
            return !string.IsNullOrEmpty(_earlyMismatchKey)
                && _earlyMismatchSinceTick != int.MinValue
                && _earlyMismatchSamples >= EarlyMismatchRequiredSamples
                && unchecked(now - _earlyMismatchSinceTick)
                    >= EarlyMismatchStableMs;
        }

        private bool TryGetCompleteSingleSlotMismatchKey(
            out string mismatchKey)
        {
            mismatchKey = string.Empty;

            if (!_targetLocked ||
                _targetSetIndices.Count == 0 ||
                string.IsNullOrEmpty(_targetLayoutFingerprint))
            {
                return false;
            }

            try
            {
                var sets = Hud.Game.Me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return false;

                Dictionary<uint, IItem> itemsByAnnId =
                    GetItemsByAnnId();

                Dictionary<uint, ItemLocation> equippedLocations =
                    GetEquippedLocationsByAnnId();

                Dictionary<ItemLocation, uint> equippedBySlot =
                    GetEquippedAnnIdsBySlot();

                string equippedFingerprint =
                    GetEquippedFingerprint();

                if (itemsByAnnId.Count == 0 ||
                    equippedLocations.Count == 0 ||
                    equippedBySlot.Count == 0 ||
                    string.IsNullOrEmpty(equippedFingerprint))
                {
                    return false;
                }

                bool foundValidTarget = false;

                foreach (int setIndex in _targetSetIndices)
                {
                    if (setIndex < 0 || setIndex >= sets.Length)
                        continue;

                    ArmorySetScan scan = ScanArmorySet(
                        setIndex,
                        sets[setIndex],
                        itemsByAnnId,
                        equippedLocations,
                        equippedBySlot);

                    if (scan == null)
                        return false;

                    if (!string.Equals(
                        scan.LayoutFingerprint,
                        _targetLayoutFingerprint,
                        StringComparison.Ordinal))
                    {
                        return false;
                    }

                    foundValidTarget = true;

                    // Early repair is allowed only for a complete, visible,
                    // exactly-one-slot mismatch.
                    if (scan.MissingItemCount != 0 ||
                        scan.SavedSlotCount <= 0 ||
                        scan.ExactSlotCount != scan.SavedSlotCount - 1)
                    {
                        return false;
                    }
                }

                if (!foundValidTarget)
                    return false;

                mismatchKey =
                    _targetLayoutFingerprint +
                    "|" +
                    equippedFingerprint;

                return true;
            }
            catch
            {
                mismatchKey = string.Empty;
                return false;
            }
        }


        private void ProcessVerify(int now)
        {
            if (!_verifying)
                return;

            if (_awaitingPhysicalRelease)
            {
                if (_physicalDownTick != int.MinValue &&
                    unchecked(now - _physicalDownTick) >=
                        PhysicalReleaseTimeoutMs)
                {
                    ClearPending();
                    ClearVerify();
                }

                return;
            }

            // Do not alter transaction state while a synthetic mouse-down is active.
            if (_pending)
                return;

            if (_physicalReleaseTick == int.MinValue ||
                _verifyStartTick == int.MinValue)
            {
                ClearPending();
                ClearVerify();
                return;
            }

            if (unchecked(now - _verifyUntilTick) > 0)
            {
                if (!_targetLocked)
                {
                    ClearVerify();
                    return;
                }

                // A securely locked target may remain under verification while the
                // loadout is still visibly incorrect.
                _verifyUntilTick = unchecked(now + VerifyWindowMs);
            }

            if (unchecked(now - _retryAtTick) < 0)
                return;

            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            if (_nativeTargetLocked && NativeSelectionChanged())
            {
                ClearPending();
                ClearVerify();
                return;
            }

            TargetAcquireState acquisition = TryAcquireTargetSet();

            if (!_targetLocked)
            {
                _fullExactConfirmSamples = 0;
                _targetDataUnknownSinceTick = int.MinValue;
                ResetEarlyMismatch();

                // NoChange cannot distinguish an already-equipped preset, an
                // ignored physical click, or a delayed HUD update. Stay passive.
                if (acquisition == TargetAcquireState.NoChange)
                {
                    if (unchecked(now - _verifyStartTick) >=
                        TargetAcquireAbortMs)
                    {
                        ClearVerify();
                        return;
                    }

                    _retryAtTick = unchecked(now + UnknownRescanMs);
                    return;
                }

                // Ambiguous, incomplete, and low-confidence states also stay passive.
                if (acquisition == TargetAcquireState.Ambiguous ||
                    acquisition == TargetAcquireState.IncompleteSnapshot ||
                    acquisition == TargetAcquireState.NoCandidate)
                {
                    if (unchecked(now - _verifyStartTick) >=
                        TargetAcquireAbortMs)
                    {
                        ClearVerify();
                        return;
                    }

                    _retryAtTick = unchecked(now + UnknownRescanMs);
                    return;
                }

                _retryAtTick = unchecked(now + UnknownRescanMs);
                return;
            }

            bool targetDataIncomplete;
            SmartDecision decision =
                GetFullLoadoutDecision(out targetDataIncomplete);

            if (decision == SmartDecision.Unknown)
            {
                _fullExactConfirmSamples = 0;
                ResetEarlyMismatch();

                if (targetDataIncomplete)
                {
                    if (_targetDataUnknownSinceTick == int.MinValue)
                    {
                        _targetDataUnknownSinceTick = now;
                    }
                    else if (unchecked(
                        now - _targetDataUnknownSinceTick) >=
                        TargetDataUnknownAbortMs)
                    {
                        // Stop safely. No clicks were issued while the target
                        // snapshot was incomplete.
                        ClearVerify();
                        return;
                    }
                }
                else
                {
                    _targetDataUnknownSinceTick = int.MinValue;
                }

                _retryAtTick = unchecked(now + UnknownRescanMs);
                return;
            }

            _targetDataUnknownSinceTick = int.MinValue;

            if (decision == SmartDecision.NotNeeded)
            {
                ResetEarlyMismatch();

                // Do not finish on a very early transient full-match snapshot.
                if (unchecked(now - _verifyStartTick) < MinFullExactConfirmMs)
                {
                    _retryAtTick = unchecked(now + UnknownRescanMs);
                    return;
                }

                _fullExactConfirmSamples++;

                if (_fullExactConfirmSamples < FullExactConfirmSamples)
                {
                    _retryAtTick = unchecked(now + UnknownRescanMs);
                    return;
                }

                ClearVerify();
                return;
            }

            // From here onward, the locked target is visible and proven incorrect.
            _fullExactConfirmSamples = 0;

            string currentEarlyMismatchKey = string.Empty;

            bool completeSingleSlotMismatch =
                !_cooldownSeen &&
                _verifyRetries == 0 &&
                _lastAssistTick == int.MinValue &&
                TryGetCompleteSingleSlotMismatchKey(
                    out currentEarlyMismatchKey);

            if (completeSingleSlotMismatch)
            {
                ObserveEarlyMismatch(
                    now,
                    currentEarlyMismatchKey);
            }
            else
            {
                ResetEarlyMismatch();
            }

            RectangleF hit = GetEquipHitRect();
            if (!Inside(hit, _clickX, _clickY))
            {
                ClearVerify();
                return;
            }

            int retryInterval = _cooldownSeen
                ? CooldownRetryMs
                : (_verifyRetries >= FastRetryLimit
                    ? SlowRetryIntervalMs
                    : RetryIntervalMs);

            int standardAssistTick;

            if (_lastAssistTick == int.MinValue)
            {
                standardAssistTick =
                    unchecked(
                        _physicalReleaseTick +
                        FirstConditionalRetryMs);
            }
            else
            {
                standardAssistTick =
                    unchecked(
                        _lastAssistTick +
                        retryInterval);
            }

            int earlyAssistTick =
                unchecked(
                    _physicalReleaseTick +
                    EarlySingleSlotRetryMs);

            bool earlyRetryReady =
                _lastAssistTick == int.MinValue &&
                _verifyRetries == 0 &&
                !_cooldownSeen &&
                completeSingleSlotMismatch &&
                IsEarlyMismatchStable(now) &&
                unchecked(now - earlyAssistTick) >= 0;

            bool standardRetryReady =
                unchecked(now - standardAssistTick) >= 0;

            if (!earlyRetryReady && !standardRetryReady)
            {
                int nextCheckTick =
                    unchecked(now + UnknownRescanMs);

                // While a complete one-slot mismatch is being observed, wake at the
                // early deadline rather than sleeping until the 850 ms fallback.
                if (completeSingleSlotMismatch &&
                    _lastAssistTick == int.MinValue &&
                    !_cooldownSeen)
                {
                    nextCheckTick = MinFutureTick(
                        nextCheckTick,
                        earlyAssistTick,
                        now);
                }

                nextCheckTick = MinFutureTick(
                    nextCheckTick,
                    standardAssistTick,
                    now);

                _retryAtTick = nextCheckTick;
                return;
            }

            // Recheck immediately before click-down. The loadout may have
            // finished settling while the retry deadline was pending.
            bool finalDataIncomplete;
            SmartDecision finalDecision =
                GetFullLoadoutDecision(out finalDataIncomplete);

            if (finalDecision != SmartDecision.RetryNeeded)
            {
                ResetEarlyMismatch();
                _retryAtTick = unchecked(now + UnknownRescanMs);
                return;
            }

            if (earlyRetryReady)
            {
                string finalEarlyMismatchKey = string.Empty;

                bool finalSingleSlotMismatch =
                    !_cooldownSeen &&
                    TryGetCompleteSingleSlotMismatchKey(
                        out finalEarlyMismatchKey);

                if (!finalSingleSlotMismatch ||
                    !string.Equals(
                        finalEarlyMismatchKey,
                        _earlyMismatchKey,
                        StringComparison.Ordinal) ||
                    !IsEarlyMismatchStable(now))
                {
                    _retryAtTick =
                        unchecked(now + UnknownRescanMs);
                    return;
                }
            }

            _cooldownSeen = false;

            if (StartHeldClick(now))
            {
                ResetEarlyMismatch();

                _verifyRetries++;
                _retryAtTick =
                    unchecked(now + RetrySettleMs);

                _verifyUntilTick = MaxTick(
                    _verifyUntilTick,
                    unchecked(now + VerifyWindowMs));
            }
            else
            {
                _retryAtTick = unchecked(now + UnknownRescanMs);
            }
        }


        private SmartDecision GetFullLoadoutDecision(
            out bool targetDataIncomplete)
        {
            targetDataIncomplete = false;

            try
            {
                if (!_targetLocked ||
                    _targetSetIndices.Count == 0 ||
                    string.IsNullOrEmpty(_targetLayoutFingerprint))
                {
                    targetDataIncomplete = true;
                    return SmartDecision.Unknown;
                }

                var sets = Hud.Game.Me.ArmorySets;
                if (sets == null || sets.Length == 0)
                {
                    targetDataIncomplete = true;
                    return SmartDecision.Unknown;
                }

                Dictionary<uint, IItem> itemsByAnnId = GetItemsByAnnId();
                Dictionary<uint, ItemLocation> equippedLocations =
                    GetEquippedLocationsByAnnId();
                Dictionary<ItemLocation, uint> equippedBySlot =
                    GetEquippedAnnIdsBySlot();

                if (itemsByAnnId.Count == 0 ||
                    equippedLocations.Count == 0 ||
                    equippedBySlot.Count == 0)
                {
                    targetDataIncomplete = true;
                    return SmartDecision.Unknown;
                }

                bool anyValidTargetCandidate = false;
                bool anyFullExact = false;
                bool anyCompleteVisibleMismatch = false;
                bool anyMissingTargetItems = false;

                foreach (int setIndex in _targetSetIndices)
                {
                    if (setIndex < 0 || setIndex >= sets.Length)
                        continue;

                    ArmorySetScan scan = ScanArmorySet(
                        setIndex,
                        sets[setIndex],
                        itemsByAnnId,
                        equippedLocations,
                        equippedBySlot);

                    if (scan == null)
                        continue;

                    // The saved preset changed or target indices no longer describe
                    // the fingerprint locked for this transaction.
                    if (!string.Equals(
                        scan.LayoutFingerprint,
                        _targetLayoutFingerprint,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    anyValidTargetCandidate = true;

                    if (scan.FullExact)
                    {
                        anyFullExact = true;
                    }
                    else if (scan.MissingItemCount > 0)
                    {
                        anyMissingTargetItems = true;
                    }
                    else
                    {
                        // Every expected target item is visible to the HUD, but at
                        // least one exact saved slot is incorrect.
                        anyCompleteVisibleMismatch = true;
                    }
                }

                // This is the only success path.
                if (anyFullExact)
                    return SmartDecision.NotNeeded;

                // Retry only when the snapshot is complete enough to prove a mismatch.
                if (anyCompleteVisibleMismatch)
                    return SmartDecision.RetryNeeded;

                // Missing target items, missing candidates, changed target layouts,
                // or incomplete snapshots are never success.
                if (anyMissingTargetItems || !anyValidTargetCandidate)
                {
                    targetDataIncomplete = true;
                    return SmartDecision.Unknown;
                }

                targetDataIncomplete = true;
                return SmartDecision.Unknown;
            }
            catch
            {
                targetDataIncomplete = true;
                return SmartDecision.Unknown;
            }
        }

        private bool TryReadNativeArmorySelection(out int armoryIndex)
        {
            armoryIndex = -1;
            if (!IsVisible(_armory))
                return false;

            int page1 = SafeAnim(_armoryPage1);
            int page2 = SafeAnim(_armoryPage2);
            int page = page1 == NativePageSelectedAnim && page2 != NativePageSelectedAnim
                ? 0
                : page2 == NativePageSelectedAnim && page1 != NativePageSelectedAnim
                    ? 1
                    : -1;

            if (page < 0)
                return false;

            int row = -1;
            for (int i = 0; i < _armoryTabs.Length; i++)
            {
                if (SafeAnim(_armoryTabs[i]) != NativeSelectedRowAnim[page, i])
                    continue;
                if (row >= 0)
                    return false;
                row = i;
            }

            string name = ReadUiText(_armoryLoadoutName);
            int nameIndex = FindArmoryIndexByName(page, name);
            int rowIndex = row >= 0 ? page * 5 + row : -1;

            // Native signals may briefly disagree while a page or row animates.
            if (nameIndex >= 0 && rowIndex >= 0 && nameIndex != rowIndex)
                return false;

            armoryIndex = nameIndex >= 0 ? nameIndex : rowIndex;
            if (armoryIndex < 0)
                return false;

            IPlayerArmorySet set = GetArmorySetByIndex(armoryIndex);
            return set != null &&
                !string.IsNullOrEmpty(set.Name) &&
                (string.IsNullOrEmpty(name) || NamesEqual(set.Name, name));
        }

        private int FindArmoryIndexByName(int page, string name)
        {
            if (page < 0 || page > 1 || string.IsNullOrEmpty(name))
                return -1;

            int match = -1;
            try
            {
                IPlayerArmorySet[] sets = Hud.Game.Me != null
                    ? Hud.Game.Me.ArmorySets
                    : null;

                if (sets == null)
                    return -1;

                for (int i = 0; i < sets.Length; i++)
                {
                    IPlayerArmorySet set = sets[i];
                    if (set == null || set.Index / 5 != page ||
                        string.IsNullOrEmpty(set.Name) || !NamesEqual(set.Name, name))
                        continue;

                    if (match >= 0)
                        return -1;
                    match = set.Index;
                }
            }
            catch { return -1; }

            return match;
        }

        private IPlayerArmorySet GetArmorySetByIndex(int armoryIndex)
        {
            try
            {
                IPlayerArmorySet[] sets = Hud.Game.Me != null
                    ? Hud.Game.Me.ArmorySets
                    : null;

                if (sets != null)
                {
                    for (int i = 0; i < sets.Length; i++)
                    {
                        if (sets[i] != null && sets[i].Index == armoryIndex)
                            return sets[i];
                    }
                }
            }
            catch { }

            return null;
        }

        private bool TryLockNativeTarget(int armoryIndex)
        {
            try
            {
                IPlayerArmorySet[] sets = Hud.Game.Me != null
                    ? Hud.Game.Me.ArmorySets
                    : null;

                if (sets == null)
                    return false;

                for (int position = 0; position < sets.Length; position++)
                {
                    IPlayerArmorySet set = sets[position];
                    if (set == null || set.Index != armoryIndex)
                        continue;

                    string fingerprint = GetArmorySetFingerprint(set);
                    if (string.IsNullOrEmpty(fingerprint))
                        return false;

                    _targetSetIndices.Clear();
                    _targetSetIndices.Add(position);
                    _targetLayoutFingerprint = fingerprint;
                    _targetLocked = true;
                    _nativeTargetLocked = true;
                    _fullExactConfirmSamples = 0;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool NativeSelectionChanged()
        {
            int selectedIndex;
            return TryReadNativeArmorySelection(out selectedIndex) &&
                selectedIndex != _requestedArmoryIndex;
        }

        private static bool NamesEqual(string left, string right)
        {
            return string.Equals(
                NormalizeUiText(left),
                NormalizeUiText(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadUiText(IUiElement element)
        {
            try
            {
                if (element == null)
                    return string.Empty;
                element.Refresh();
                return element.Visible
                    ? NormalizeUiText(element.ReadText(Encoding.UTF8, true))
                    : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string NormalizeUiText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsControl(c))
                    result.Append(c);
            }
            return result.ToString().Trim();
        }

        private TargetAcquireState TryAcquireTargetSet()
        {
            if (_targetLocked)
                return TargetAcquireState.Locked;

            try
            {
                string currentFingerprint = GetEquippedFingerprint();
                if (string.IsNullOrEmpty(currentFingerprint) ||
                    string.IsNullOrEmpty(_preEquipFingerprint))
                {
                    return TargetAcquireState.IncompleteSnapshot;
                }

                if (string.Equals(
                    currentFingerprint,
                    _preEquipFingerprint,
                    StringComparison.Ordinal))
                {
                    return TargetAcquireState.NoChange;
                }

                var sets = Hud.Game.Me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return TargetAcquireState.IncompleteSnapshot;

                Dictionary<uint, IItem> itemsByAnnId = GetItemsByAnnId();
                Dictionary<uint, ItemLocation> equippedLocations =
                    GetEquippedLocationsByAnnId();
                Dictionary<ItemLocation, uint> equippedBySlot =
                    GetEquippedAnnIdsBySlot();

                if (itemsByAnnId.Count == 0 ||
                    equippedLocations.Count == 0 ||
                    equippedBySlot.Count == 0)
                {
                    return TargetAcquireState.IncompleteSnapshot;
                }

                List<ArmorySetScan> scans = BuildArmorySetScans(
                    sets,
                    itemsByAnnId,
                    equippedLocations,
                    equippedBySlot);

                int bestExact = -1;

                for (int i = 0; i < scans.Count; i++)
                {
                    ArmorySetScan scan = scans[i];

                    // A preset that was already exact before the physical click
                    // cannot be the newly selected target.
                    if (_preExactSetIndices.Contains(scan.SetIndex))
                        continue;

                    if (scan.ExactMatchCount > bestExact)
                        bestExact = scan.ExactMatchCount;
                }

                if (bestExact < MinTargetSetItemMatches)
                    return TargetAcquireState.NoCandidate;

                string candidateLayout = null;
                bool candidateSnapshotIncomplete = false;
                int candidateCount = 0;

                for (int i = 0; i < scans.Count; i++)
                {
                    ArmorySetScan scan = scans[i];

                    if (_preExactSetIndices.Contains(scan.SetIndex) ||
                        scan.ExactMatchCount != bestExact)
                    {
                        continue;
                    }

                    candidateCount++;

                    if (scan.MissingItemCount > 0)
                        candidateSnapshotIncomplete = true;

                    if (candidateLayout == null)
                    {
                        candidateLayout = scan.LayoutFingerprint;
                    }
                    else if (!string.Equals(
                        candidateLayout,
                        scan.LayoutFingerprint,
                        StringComparison.Ordinal))
                    {
                        // Same score, but genuinely different loadouts.
                        // Do not guess and do not click.
                        return TargetAcquireState.Ambiguous;
                    }
                }

                if (candidateCount == 0 || string.IsNullOrEmpty(candidateLayout))
                    return TargetAcquireState.NoCandidate;

                if (candidateSnapshotIncomplete)
                    return TargetAcquireState.IncompleteSnapshot;

                _targetSetIndices.Clear();

                for (int i = 0; i < scans.Count; i++)
                {
                    ArmorySetScan scan = scans[i];

                    if (_preExactSetIndices.Contains(scan.SetIndex))
                        continue;

                    if (string.Equals(
                        scan.LayoutFingerprint,
                        candidateLayout,
                        StringComparison.Ordinal))
                    {
                        _targetSetIndices.Add(scan.SetIndex);
                    }
                }

                if (_targetSetIndices.Count == 0)
                    return TargetAcquireState.NoCandidate;

                _targetLayoutFingerprint = candidateLayout;
                _targetLocked = true;
                _targetDataUnknownSinceTick = int.MinValue;
                _fullExactConfirmSamples = 0;

                return TargetAcquireState.Locked;
            }
            catch
            {
                return TargetAcquireState.IncompleteSnapshot;
            }
        }

        private List<ArmorySetScan> BuildArmorySetScans(
            IPlayerArmorySet[] sets,
            Dictionary<uint, IItem> itemsByAnnId,
            Dictionary<uint, ItemLocation> equippedLocations,
            Dictionary<ItemLocation, uint> equippedBySlot)
        {
            List<ArmorySetScan> scans = new List<ArmorySetScan>();

            for (int i = 0; i < sets.Length; i++)
            {
                ArmorySetScan scan = ScanArmorySet(
                    i,
                    sets[i],
                    itemsByAnnId,
                    equippedLocations,
                    equippedBySlot);

                if (scan != null && scan.SavedSlotCount > 0)
                    scans.Add(scan);
            }

            return scans;
        }


        private ArmorySetScan ScanArmorySet(
            int setIndex,
            IPlayerArmorySet set,
            Dictionary<uint, IItem> itemsByAnnId,
            Dictionary<uint, ItemLocation> equippedLocations,
            Dictionary<ItemLocation, uint> equippedBySlot)
        {
            if (set == null || set.ItemAnnIds == null)
                return null;

            ArmorySetScan scan = new ArmorySetScan();
            scan.SetIndex = setIndex;
            scan.LayoutFingerprint = GetArmorySetFingerprint(set);

            List<uint> savedAnnIds = new List<uint>(set.ItemAnnIds);
            int slotCount = (int)ItemLocation.Neck - (int)ItemLocation.Head + 1;

            for (int itemIndex = 0; itemIndex < slotCount; itemIndex++)
            {
                ItemLocation targetSlot = ArmorySlotFromIndex(itemIndex);
                if (!IsEquipmentSlot(targetSlot))
                    continue;

                scan.SavedSlotCount++;

                uint savedAnnId = itemIndex < savedAnnIds.Count
                    ? savedAnnIds[itemIndex]
                    : 0u;

                uint actualAnnId;
                bool actualSlotVisible =
                    equippedBySlot.TryGetValue(targetSlot, out actualAnnId);

                // An empty slot is part of the saved Armory layout and must also match.
                if (savedAnnId == 0)
                {
                    if (!actualSlotVisible || actualAnnId == 0)
                        scan.ExactSlotCount++;

                    continue;
                }

                scan.SavedItemCount++;

                ItemLocation currentLocation;
                if (equippedLocations.TryGetValue(savedAnnId, out currentLocation))
                {
                    scan.MatchCount++;

                    if (currentLocation == targetSlot)
                        scan.ExactMatchCount++;
                }

                if (actualSlotVisible && actualAnnId == savedAnnId)
                    scan.ExactSlotCount++;

                IItem savedItem;
                if (!itemsByAnnId.TryGetValue(savedAnnId, out savedItem))
                    scan.MissingItemCount++;
            }

            return scan;
        }


        private Dictionary<uint, IItem> GetItemsByAnnId()
        {
            Dictionary<uint, IItem> items = new Dictionary<uint, IItem>();
            try
            {
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.AnnId == 0)
                        continue;
                    if (!items.ContainsKey(item.AnnId))
                        items.Add(item.AnnId, item);
                }
            }
            catch { }
            return items;
        }

        private Dictionary<uint, ItemLocation> GetEquippedLocationsByAnnId()
        {
            Dictionary<uint, ItemLocation> equipped = new Dictionary<uint, ItemLocation>();
            try
            {
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.AnnId == 0 || !IsEquipmentSlot(item.Location))
                        continue;
                    if (!equipped.ContainsKey(item.AnnId))
                        equipped.Add(item.AnnId, item.Location);
                }
            }
            catch { }
            return equipped;
        }

        private Dictionary<ItemLocation, uint> GetEquippedAnnIdsBySlot()
        {
            Dictionary<ItemLocation, uint> equippedBySlot =
                new Dictionary<ItemLocation, uint>();

            try
            {
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.AnnId == 0 || !IsEquipmentSlot(item.Location))
                        continue;

                    if (!equippedBySlot.ContainsKey(item.Location))
                        equippedBySlot.Add(item.Location, item.AnnId);
                }
            }
            catch
            {
            }

            return equippedBySlot;
        }

        private void CapturePreClickState()
        {
            _preEquipFingerprint = GetEquippedFingerprint();

            _preExactSetIndices.Clear();
            _targetSetIndices.Clear();
            _targetLocked = false;
            _targetLayoutFingerprint = string.Empty;
            _fullExactConfirmSamples = 0;

            try
            {
                Dictionary<uint, IItem> itemsByAnnId = GetItemsByAnnId();
                Dictionary<uint, ItemLocation> equippedLocations = GetEquippedLocationsByAnnId();
                Dictionary<ItemLocation, uint> equippedBySlot = GetEquippedAnnIdsBySlot();

                var sets = Hud.Game.Me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return;

                for (int i = 0; i < sets.Length; i++)
                {
                    ArmorySetScan scan = ScanArmorySet(
                        i,
                        sets[i],
                        itemsByAnnId,
                        equippedLocations,
                        equippedBySlot);

                    if (scan != null && scan.FullExact)
                        _preExactSetIndices.Add(i);
                }
            }
            catch
            {
                // Fail closed. Target acquisition will remain unavailable if this
                // snapshot was incomplete.
            }
        }

        private string GetEquippedFingerprint()
        {
            try
            {
                uint[] slots = new uint[(int)ItemLocation.Neck - (int)ItemLocation.Head + 1];
                int visibleEquippedSlots = 0;

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.AnnId == 0 || !IsEquipmentSlot(item.Location))
                        continue;

                    int index = (int)item.Location - (int)ItemLocation.Head;
                    if (index < 0 || index >= slots.Length)
                        continue;

                    if (slots[index] == 0)
                        visibleEquippedSlots++;

                    slots[index] = item.AnnId;
                }

                return visibleEquippedSlots == 0
                    ? string.Empty
                    : string.Join(",", slots);
            }
            catch
            {
                return string.Empty;
            }
        }


        private string GetArmorySetFingerprint(IPlayerArmorySet set)
        {
            if (set == null || set.ItemAnnIds == null)
                return string.Empty;

            int slotCount = (int)ItemLocation.Neck - (int)ItemLocation.Head + 1;
            uint[] slots = new uint[slotCount];
            List<uint> savedAnnIds = new List<uint>(set.ItemAnnIds);

            for (int i = 0; i < slotCount; i++)
            {
                if (i < savedAnnIds.Count)
                    slots[i] = savedAnnIds[i];
            }

            return string.Join(",", slots);
        }


        private ItemLocation ArmorySlotFromIndex(int index)
        {
            int slot = index + (int)ItemLocation.Head;
            if (slot < (int)ItemLocation.Head || slot > (int)ItemLocation.Neck)
                return ItemLocation.Floor;
            return (ItemLocation)slot;
        }

        private bool IsEquipmentSlot(ItemLocation location)
        {
            return (int)location >= (int)ItemLocation.Head && (int)location <= (int)ItemLocation.Neck;
        }

        private bool StartHeldClick(int now)
        {
            if (_pending ||
                !ArmoryClick.LeftDownPoint(_clickX, _clickY))
            {
                return false;
            }

            _lastAssistTick = now;
            _pending = true;
            _stopClickTick = unchecked(now + AssistHoldMs);

            return true;
        }


        private bool CanRun()
        {
            try
            {
                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
                    return false;
                if (Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null || !Hud.Game.IsInTown)
                    return false;
                return IsVisible(_armory);
            }
            catch { return false; }
        }

        private bool IsArmoryConfirmationVisible()
        {
            try
            {
                if (!IsVisible(_armory))
                    return false;

                return IsVisible(_confirmationOk) || IsVisible(_confirmationCancel) || IsVisible(_confirmationDialog);
            }
            catch { return false; }
        }

        private bool IsLeftButtonDown()
        {
            try { return Hud != null && Hud.Input != null && Hud.Input.IsKeyDown(Keys.LButton); }
            catch { return false; }
        }

        private RectangleF GetEquipHitRect()
        {
            RectangleF native = SafeRect(_equipButton);
            if (native.Width > 0f && native.Height > 0f)
                return Inflate(native, 3.0f, 2.0f);

            RectangleF root = SafeRect(_armory);
            if (root.Width <= 0f || root.Height <= 0f)
                return RectangleF.Empty;

            float x = root.Left + root.Width * EquipRelX;
            float y = root.Top + root.Height * EquipRelY;
            float w = root.Width * EquipRelW;
            float h = root.Height * EquipRelH;
            float px = root.Width * EquipPadX;
            float py = root.Height * EquipPadY;
            return new RectangleF(
                x - px,
                y - py,
                w + px * 2f,
                h + py * 2f);
        }

        private Point GetEquipClickPoint(RectangleF fallback)
        {
            RectangleF native = SafeRect(_equipButton);
            RectangleF rect = native.Width > 0f && native.Height > 0f
                ? native
                : fallback;

            return new Point(
                (int)Math.Round(rect.Left + rect.Width * 0.5f),
                (int)Math.Round(rect.Top + rect.Height * 0.5f));
        }

        private static RectangleF Inflate(
            RectangleF rect,
            float x,
            float y)
        {
            return new RectangleF(
                rect.Left - x,
                rect.Top - y,
                rect.Width + x * 2f,
                rect.Height + y * 2f);
        }

        private static RectangleF SafeRect(IUiElement element)
        {
            try
            {
                if (element == null)
                    return RectangleF.Empty;
                element.Refresh();
                return element.Visible
                    ? element.Rectangle
                    : RectangleF.Empty;
            }
            catch
            {
                return RectangleF.Empty;
            }
        }

        private static int SafeAnim(IUiElement element)
        {
            try
            {
                if (element == null)
                    return -999;
                element.Refresh();
                return element.AnimState;
            }
            catch
            {
                return -999;
            }
        }

        private bool IsVisible(IUiElement element)
        {
            if (element == null)
                return false;
            try { element.Refresh(); return element.Visible; }
            catch { return false; }
        }

        private IUiElement RegisterUi(string path, IUiElement parent)
        {
            try { return Hud.Render.RegisterUiElement(path, parent, null); }
            catch { try { return Hud.Render.GetUiElement(path); } catch { return null; } }
        }

        private void ClearPending()
        {
            if (_pending)
                ArmoryClick.LeftUpPoint(_clickX, _clickY);

            _pending = false;
            _stopClickTick = int.MinValue;

            if (!_verifying)
            {
                _clickX = 0;
                _clickY = 0;
            }
        }


        private void ClearVerify()
        {
            _verifying = false;

            _awaitingPhysicalRelease = false;
            _physicalDownTick = int.MinValue;
            _physicalReleaseTick = int.MinValue;

            if (!_pending)
            {
                _clickX = 0;
                _clickY = 0;
            }

            _verifyUntilTick = int.MinValue;
            _retryAtTick = int.MinValue;
            _verifyStartTick = int.MinValue;

            _verifyRetries = 0;
            _cooldownSeen = false;

            // Transaction-local assisted-click timing must never leak into the next
            // physical Armory click.
            _lastAssistTick = int.MinValue;
            _targetDataUnknownSinceTick = int.MinValue;

            _preEquipFingerprint = string.Empty;
            _preExactSetIndices.Clear();

            _targetSetIndices.Clear();
            _targetLayoutFingerprint = string.Empty;
            _targetLocked = false;
            _nativeTargetLocked = false;
            _requestedArmoryIndex = -1;

            _fullExactConfirmSamples = 0;

            ResetEarlyMismatch();
        }

        private static bool Inside(RectangleF r, int x, int y)
        {
            return r.Width > 0f && r.Height > 0f && x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private static int MinFutureTick(int a, int b, int now)
        {
            int da = unchecked(a - now);
            int db = unchecked(b - now);
            if (da < 0) return b;
            if (db < 0) return a;
            return da <= db ? a : b;
        }

        private static int MaxTick(int a, int b)
        {
            return unchecked(a - b) >= 0 ? a : b;
        }

        private static class ArmoryClick
        {
            private const uint WmLButtonDown = 0x0201;
            private const uint WmLButtonUp = 0x0202;

            [DllImport("user32.dll")]
            private static extern IntPtr FindWindow(string className, string windowText);

            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            public static bool LeftDownPoint(int x, int y)
            {
                IntPtr hwnd = FindWindow("D3 Main Window Class", null);
                if (hwnd == IntPtr.Zero)
                    return false;

                SendMessage(hwnd, WmLButtonDown, (IntPtr)1, MakeLParam(x, y));
                return true;
            }

            public static void LeftUpPoint(int x, int y)
            {
                IntPtr hwnd = FindWindow("D3 Main Window Class", null);
                if (hwnd == IntPtr.Zero)
                    return;

                SendMessage(hwnd, WmLButtonUp, IntPtr.Zero, MakeLParam(x, y));
            }

            private static IntPtr MakeLParam(int x, int y)
            {
                return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            }
        }
    }
}
