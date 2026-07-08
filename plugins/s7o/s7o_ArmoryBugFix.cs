namespace Turbo.Plugins.s7o
{
    using System;
    using System.Drawing;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using Turbo.Plugins.Default;

    public class s7o_ArmoryBugFix : BasePlugin, IAfterCollectHandler, IChatLineChangedHandler
    {
        private const int AssistClickIntervalMs = 10;
        private const int AssistBurstDurationMs = 30;
        private const int AssistLockoutMs = 700;
        private const int FirstValidateMs = 350;
        private const int RetryValidateMs = 180;
        private const int UnknownRescanMs = 100;
        private const int MinPrimalizedNotNeededConfirmMs = 550;
        private const int CooldownRetryMs = 1050;
        private const int VerifyWindowMs = 3600;
        private const int MaxVerifyRetries = 4;
        private const int MinTargetSetItemMatches = 4;

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

        private sealed class ArmorySetScan
        {
            public int MatchCount;
            public int SavedItemCount;
            public bool HasPrimalizedItem;
            public bool NeedsRetry;
        }

        private IUiElement _armory;
        private IUiElement _confirmationDialog;
        private IUiElement _confirmationOk;
        private IUiElement _confirmationCancel;

        private bool _leftDownLast;
        private bool _pending;
        private int _clickX;
        private int _clickY;
        private int _nextClickTick = int.MinValue;
        private int _stopClickTick = int.MinValue;
        private int _lastAssistTick = int.MinValue;

        private bool _verifying;
        private int _verifyUntilTick = int.MinValue;
        private int _retryAtTick = int.MinValue;
        private int _verifyRetries;
        private bool _cooldownSeen;
        private int _verifyStartTick = int.MinValue;
        private bool _prePrimalizedSeen;

        public s7o_ArmoryBugFix()
        {
            Enabled = true;
            Order = 30205;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            _armory = RegisterUi("Root.NormalLayer.equipmentManager_mainPage", null);
            _confirmationDialog = RegisterUi("Root.TopLayer.confirmation.subdlg", null);
            _confirmationOk = RegisterUi("Root.TopLayer.confirmation.subdlg.stack.wrap.button_ok", null);
            _confirmationCancel = RegisterUi("Root.TopLayer.confirmation.subdlg.stack.wrap.button_cancel", null);
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

            if (leftDown && !_leftDownLast)
                HandleArmoryClick(now);

            _leftDownLast = leftDown;

            ProcessPending(now);
            ProcessVerify(now);
        }

        public void OnChatLineChanged(string currentLine, string previousLine)
        {
            if (!_verifying || string.IsNullOrEmpty(currentLine))
                return;

            string line = currentLine.ToLowerInvariant();
            if (line.IndexOf("armory", StringComparison.Ordinal) < 0 || line.IndexOf("cooldown", StringComparison.Ordinal) < 0)
                return;

            int now = Environment.TickCount;
            _cooldownSeen = true;
            _retryAtTick = unchecked(now + CooldownRetryMs);
            _verifyUntilTick = MaxTick(_verifyUntilTick, unchecked(now + VerifyWindowMs));
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

            _clickX = Hud.Window.CursorX;
            _clickY = Hud.Window.CursorY;
            _verifyStartTick = now;
            CapturePreClickPrimalizedState();
            _verifying = true;
            _verifyRetries = 0;
            _verifyUntilTick = unchecked(now + VerifyWindowMs);
            _retryAtTick = unchecked(now + FirstValidateMs);
            _cooldownSeen = false;
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
            if (!Inside(hit, _clickX, _clickY) || unchecked(now - _stopClickTick) > 0)
            {
                ClearPending();
                return;
            }

            if (unchecked(now - _nextClickTick) < 0)
                return;

            _lastAssistTick = now;
            ArmoryClick.LeftClickPoint(_clickX, _clickY);
            _nextClickTick = unchecked(now + AssistClickIntervalMs);
        }

        private void ProcessVerify(int now)
        {
            if (!_verifying)
                return;

            if (unchecked(now - _verifyUntilTick) > 0)
            {
                ClearVerify();
                return;
            }

            if (_pending || unchecked(now - _retryAtTick) < 0)
                return;

            if (!CanRun() || IsArmoryConfirmationVisible())
            {
                ClearVerify();
                return;
            }

            SmartDecision decision = GetPrimalizedRetryDecision();
            if (decision == SmartDecision.Unknown)
            {
                _retryAtTick = unchecked(now + UnknownRescanMs);
                return;
            }

            if (decision == SmartDecision.NotNeeded)
            {
                if (_prePrimalizedSeen && unchecked(now - _verifyStartTick) < MinPrimalizedNotNeededConfirmMs)
                {
                    _retryAtTick = unchecked(now + UnknownRescanMs);
                    return;
                }

                ClearVerify();
                return;
            }

            if (_verifyRetries >= MaxVerifyRetries)
            {
                ClearVerify();
                return;
            }

            RectangleF hit = GetEquipHitRect();
            if (!Inside(hit, _clickX, _clickY))
            {
                ClearVerify();
                return;
            }

            if (_lastAssistTick != int.MinValue && _verifyRetries == 0 && unchecked(now - _lastAssistTick) < AssistLockoutMs)
            {
                _retryAtTick = unchecked(now + RetryValidateMs);
                return;
            }

            _verifyRetries++;
            _retryAtTick = unchecked(now + GetNextRetryDelayMs());
            _verifyUntilTick = MaxTick(_verifyUntilTick, unchecked(now + VerifyWindowMs));
            _cooldownSeen = false;
            StartBurst(now);
        }

        private SmartDecision GetPrimalizedRetryDecision()
        {
            try
            {
                var me = Hud.Game.Me;
                var sets = me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return SmartDecision.NotNeeded;

                Dictionary<uint, IItem> itemsByAnnId = GetItemsByAnnId();
                if (itemsByAnnId.Count == 0 || !HasAnyPrimalizedItem(itemsByAnnId))
                    return SmartDecision.NotNeeded;

                Dictionary<uint, ItemLocation> equippedLocations = GetEquippedLocationsByAnnId();
                if (equippedLocations.Count == 0)
                    return SmartDecision.Unknown;

                List<ArmorySetScan> scans = new List<ArmorySetScan>();
                int bestScore = 0;

                for (int i = 0; i < sets.Length; i++)
                {
                    ArmorySetScan scan = ScanArmorySet(sets[i], itemsByAnnId, equippedLocations);
                    if (scan == null || scan.SavedItemCount == 0)
                        continue;

                    scans.Add(scan);
                    if (scan.MatchCount > bestScore)
                        bestScore = scan.MatchCount;
                }

                if (bestScore < MinTargetSetItemMatches)
                    return SmartDecision.Unknown;

                int bestPrimalizedCandidates = 0;
                bool anyNeedsRetry = false;
                bool anyNotNeeded = false;

                for (int i = 0; i < scans.Count; i++)
                {
                    ArmorySetScan scan = scans[i];
                    if (scan.MatchCount != bestScore)
                        continue;

                    if (!scan.HasPrimalizedItem)
                        continue;

                    bestPrimalizedCandidates++;
                    if (scan.NeedsRetry)
                        anyNeedsRetry = true;
                    else
                        anyNotNeeded = true;
                }

                if (bestPrimalizedCandidates == 0)
                    return SmartDecision.NotNeeded;

                if (anyNeedsRetry)
                    return SmartDecision.RetryNeeded;

                if (anyNotNeeded)
                    return SmartDecision.NotNeeded;

                return SmartDecision.Unknown;
            }
            catch { return SmartDecision.Unknown; }
        }

        private ArmorySetScan ScanArmorySet(IPlayerArmorySet set, Dictionary<uint, IItem> itemsByAnnId, Dictionary<uint, ItemLocation> equippedLocations)
        {
            if (set == null || set.ItemAnnIds == null)
                return null;

            ArmorySetScan scan = new ArmorySetScan();
            int itemIndex = 0;

            foreach (uint annId in set.ItemAnnIds)
            {
                ItemLocation targetSlot = ArmorySlotFromIndex(itemIndex);
                itemIndex++;

                if (annId == 0 || !IsEquipmentSlot(targetSlot))
                    continue;

                scan.SavedItemCount++;
                if (equippedLocations.ContainsKey(annId))
                    scan.MatchCount++;

                IItem item;
                if (!itemsByAnnId.TryGetValue(annId, out item) || !IsPrimalized(item))
                    continue;

                scan.HasPrimalizedItem = true;

                ItemLocation currentLocation;
                if (!equippedLocations.TryGetValue(annId, out currentLocation) || currentLocation != targetSlot)
                    scan.NeedsRetry = true;
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

        private bool HasAnyPrimalizedItem(Dictionary<uint, IItem> itemsByAnnId)
        {
            try
            {
                foreach (var pair in itemsByAnnId)
                {
                    if (IsPrimalized(pair.Value))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void CapturePreClickPrimalizedState()
        {
            _prePrimalizedSeen = false;

            try
            {
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.AnnId == 0 || !IsEquipmentSlot(item.Location) || !IsPrimalized(item))
                        continue;

                    _prePrimalizedSeen = true;
                    return;
                }
            }
            catch { }
        }

        private bool IsPrimalized(IItem item)
        {
            if (item == null)
                return false;

            try
            {
                var stats = item.StatList;
                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        if (stat == null || !IsPrimalizedStat(stat))
                            continue;

                        if (stat.IntegerValue.HasValue && stat.IntegerValue.Value != 0)
                            return true;
                        if (Math.Abs(stat.DoubleValue) > 0.0001d)
                            return true;
                    }
                }
            }
            catch { }

            try
            {
                var attr = Hud == null || Hud.Sno == null || Hud.Sno.Attributes == null ? null : Hud.Sno.Attributes.Itemwasprimalized;
                if (attr != null)
                {
                    if (item.GetAttributeValueAsInt(attr, 0u, 0) != 0)
                        return true;
                    if (item.GetAttributeValueAsInt(attr, 1048575u, 0) != 0)
                        return true;
                    if (item.GetAttributeValueAsInt(attr, 2147483647u, 0) != 0)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsPrimalizedStat(IItemStat stat)
        {
            try
            {
                string id = stat.Id ?? string.Empty;
                string code = stat.Attribute == null ? string.Empty : (stat.Attribute.Code ?? string.Empty);
                return id.IndexOf("primalized", StringComparison.OrdinalIgnoreCase) >= 0
                    || code.IndexOf("primalized", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
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

        private int GetNextRetryDelayMs()
        {
            if (_cooldownSeen)
                return CooldownRetryMs;

            return RetryValidateMs;
        }

        private void StartBurst(int now)
        {
            _nextClickTick = now;
            _stopClickTick = unchecked(now + AssistBurstDurationMs);
            _pending = true;
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
            RectangleF r = RectangleF.Empty;
            try { _armory.Refresh(); r = _armory.Rectangle; } catch { }
            if (r.Width <= 0f || r.Height <= 0f)
                return RectangleF.Empty;

            float x = r.Left + r.Width * EquipRelX;
            float y = r.Top + r.Height * EquipRelY;
            float w = r.Width * EquipRelW;
            float h = r.Height * EquipRelH;
            float px = r.Width * EquipPadX;
            float py = r.Height * EquipPadY;
            return new RectangleF(x - px, y - py, w + px * 2f, h + py * 2f);
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
            _pending = false;
            if (!_verifying)
            {
                _clickX = 0;
                _clickY = 0;
            }
            _nextClickTick = int.MinValue;
            _stopClickTick = int.MinValue;
        }

        private void ClearVerify()
        {
            _verifying = false;
            if (!_pending)
            {
                _clickX = 0;
                _clickY = 0;
            }
            _verifyUntilTick = int.MinValue;
            _retryAtTick = int.MinValue;
            _verifyRetries = 0;
            _cooldownSeen = false;
            _verifyStartTick = int.MinValue;
            _prePrimalizedSeen = false;
        }

        private static bool Inside(RectangleF r, int x, int y)
        {
            return r.Width > 0f && r.Height > 0f && x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private static int MaxTick(int a, int b)
        {
            return unchecked(a - b) >= 0 ? a : b;
        }

        private static class ArmoryClick
        {
            private const uint InputMouse = 0;
            private const uint MouseLeftDown = 0x0002;
            private const uint MouseLeftUp = 0x0004;
            private const uint WmLButtonDown = 0x0201;
            private const uint WmLButtonUp = 0x0202;

            [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public MOUSEINPUT Mouse; }
            [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

            [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
            [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);
            [DllImport("user32.dll")] private static extern IntPtr FindWindow(string className, string windowText);
            [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            public static bool LeftClickPoint(int x, int y)
            {
                IntPtr hwnd = FindWindow("D3 Main Window Class", null);
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr point = MakeLParam(x, y);
                    SendMessage(hwnd, WmLButtonDown, (IntPtr)1, point);
                    SendMessage(hwnd, WmLButtonUp, IntPtr.Zero, point);
                    return true;
                }

                if (!SetCursorPos(x, y))
                    return false;

                var input = new INPUT[2];
                input[0].Type = InputMouse;
                input[0].Mouse.Flags = MouseLeftDown;
                input[1].Type = InputMouse;
                input[1].Mouse.Flags = MouseLeftUp;
                return SendInput(2, input, Marshal.SizeOf(typeof(INPUT))) == 2;
            }

            private static IntPtr MakeLParam(int x, int y)
            {
                return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            }
        }
    }
}
