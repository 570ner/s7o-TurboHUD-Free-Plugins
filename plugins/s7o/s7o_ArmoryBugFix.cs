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
        private const int FastRetryMs = 120;
        private const int CooldownRetryMs = 1050;
        private const int VerifyWindowMs = 3600;
        private const int MaxVerifyRetries = 6;

        private const float EquipRelX = 0.481008f;
        private const float EquipRelY = 0.773148f;
        private const float EquipRelW = 0.374512f;
        private const float EquipRelH = 0.026852f;
        private const float EquipPadX = 0.020000f;
        private const float EquipPadY = 0.010000f;

        private IUiElement _armory;

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
        private ulong _startSignature;
        private bool _cooldownSeen;

        public s7o_ArmoryBugFix()
        {
            Enabled = true;
            Order = 30205;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            _armory = RegisterUi("Root.NormalLayer.equipmentManager_mainPage", null);
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

            if (leftDown && !_leftDownLast)
                TryArm(now);

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

        private void TryArm(int now)
        {
            if (!CanRun())
            {
                ClearPending();
                return;
            }

            RectangleF hit = GetEquipHitRect();
            if (!Inside(hit, Hud.Window.CursorX, Hud.Window.CursorY))
                return;

            _clickX = Hud.Window.CursorX;
            _clickY = Hud.Window.CursorY;
            _startSignature = GetLoadoutSignature();
            _verifying = true;
            _verifyRetries = 0;
            _verifyUntilTick = unchecked(now + VerifyWindowMs);
            _retryAtTick = unchecked(now + FastRetryMs);
            _cooldownSeen = false;

            if (_lastAssistTick != int.MinValue && unchecked(now - _lastAssistTick) < AssistLockoutMs)
                return;

            StartBurst(now);
        }

        private void ProcessPending(int now)
        {
            if (!_pending)
                return;

            if (!CanRun())
            {
                ClearPending();
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

            ulong currentSignature = GetLoadoutSignature();
            if (currentSignature != _startSignature && LoadoutMatchesAnyArmorySet())
            {
                ClearVerify();
                return;
            }

            if (currentSignature != _startSignature && !HasUsableArmorySetData())
            {
                ClearVerify();
                return;
            }

            if (unchecked(now - _verifyUntilTick) > 0)
            {
                ClearVerify();
                return;
            }

            if (_pending || _verifyRetries >= MaxVerifyRetries || unchecked(now - _retryAtTick) < 0)
                return;

            if (!CanRun())
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

            _verifyRetries++;
            _retryAtTick = unchecked(now + GetNextRetryDelayMs());
            _verifyUntilTick = MaxTick(_verifyUntilTick, unchecked(now + VerifyWindowMs));
            _cooldownSeen = false;
            StartBurst(now);
        }

        private int GetNextRetryDelayMs()
        {
            if (_cooldownSeen)
                return CooldownRetryMs;

            int delay = FastRetryMs + _verifyRetries * 90;
            return Math.Max(FastRetryMs, Math.Min(500, delay));
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

        private ulong GetLoadoutSignature()
        {
            ulong hash = 1469598103934665603UL;
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return hash;

                ulong[] equipped = new ulong[14];
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || (int)item.Location < (int)ItemLocation.Head || (int)item.Location > (int)ItemLocation.Neck)
                        continue;

                    int slot = (int)item.Location;
                    ulong itemHash = 1469598103934665603UL;
                    Mix(ref itemHash, item.AnnId);
                    Mix(ref itemHash, item.SnoItem == null ? 0u : item.SnoItem.Sno);
                    Mix(ref itemHash, (uint)item.InventoryX);
                    Mix(ref itemHash, (uint)item.InventoryY);
                    equipped[slot] = itemHash;
                }

                for (int i = 1; i < equipped.Length; i++)
                {
                    Mix(ref hash, (uint)i);
                    Mix(ref hash, (uint)equipped[i]);
                    Mix(ref hash, (uint)(equipped[i] >> 32));
                }

                var powers = Hud.Game.Me.Powers;
                if (powers != null)
                {
                    var skills = powers.SkillSlots;
                    if (skills != null)
                    {
                        for (int i = 0; i < skills.Length; i++)
                        {
                            var skill = skills[i];
                            Mix(ref hash, (uint)(100 + i));
                            Mix(ref hash, skill == null || skill.SnoPower == null ? 0u : skill.SnoPower.Sno);
                            Mix(ref hash, skill == null ? 0u : skill.Rune);
                        }
                    }

                    var passives = powers.PassiveSlots;
                    if (passives != null)
                    {
                        for (int i = 0; i < passives.Length; i++)
                        {
                            Mix(ref hash, (uint)(200 + i));
                            Mix(ref hash, passives[i] == null ? 0u : passives[i].Sno);
                        }
                    }
                }

                var me = Hud.Game.Me;
                Mix(ref hash, me.CubeSnoItem1 == null ? 0u : me.CubeSnoItem1.Sno);
                Mix(ref hash, me.CubeSnoItem2 == null ? 0u : me.CubeSnoItem2.Sno);
                Mix(ref hash, me.CubeSnoItem3 == null ? 0u : me.CubeSnoItem3.Sno);
                Mix(ref hash, me.CubeSnoItem4 == null ? 0u : me.CubeSnoItem4.Sno);
            }
            catch { }
            return hash;
        }


        private bool HasUsableArmorySetData()
        {
            try
            {
                var sets = Hud.Game.Me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return false;

                for (int i = 0; i < sets.Length; i++)
                {
                    if (sets[i] == null)
                        continue;
                    if (sets[i].ItemAnnIds != null || sets[i].LeftSkillSnoPower != null || sets[i].RightSkillSnoPower != null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private bool LoadoutMatchesAnyArmorySet()
        {
            try
            {
                var me = Hud.Game.Me;
                var sets = me.ArmorySets;
                if (sets == null || sets.Length == 0)
                    return false;

                HashSet<uint> equipped = GetEquippedAnnIds();
                for (int i = 0; i < sets.Length; i++)
                {
                    var set = sets[i];
                    if (set == null)
                        continue;
                    if (ItemsMatch(set, equipped) && SkillsMatch(set) && PassivesMatch(set) && CubeMatch(set))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private HashSet<uint> GetEquippedAnnIds()
        {
            var equipped = new HashSet<uint>();
            try
            {
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || (int)item.Location < (int)ItemLocation.Head || (int)item.Location > (int)ItemLocation.Neck)
                        continue;
                    if (item.AnnId != 0)
                        equipped.Add(item.AnnId);
                }
            }
            catch { }
            return equipped;
        }

        private bool ItemsMatch(IPlayerArmorySet set, HashSet<uint> equipped)
        {
            HashSet<uint> saved = new HashSet<uint>();
            try
            {
                if (set.ItemAnnIds != null)
                {
                    foreach (uint annId in set.ItemAnnIds)
                    {
                        if (annId != 0)
                            saved.Add(annId);
                    }
                }
            }
            catch { return false; }

            if (saved.Count == 0)
                return false;
            if (saved.Count != equipped.Count)
                return false;

            foreach (uint annId in saved)
            {
                if (!equipped.Contains(annId))
                    return false;
            }
            return true;
        }

        private bool SkillsMatch(IPlayerArmorySet set)
        {
            try
            {
                var skills = Hud.Game.Me.Powers == null ? null : Hud.Game.Me.Powers.SkillSlots;
                if (skills == null || skills.Length < 6)
                    return true;

                return SkillMatches(skills[0], set.LeftSkillSnoPower, set.LeftSkillRune)
                    && SkillMatches(skills[1], set.RightSkillSnoPower, set.RightSkillRune)
                    && SkillMatches(skills[2], set.Skill1SnoPower, set.Skill1Rune)
                    && SkillMatches(skills[3], set.Skill2SnoPower, set.Skill2Rune)
                    && SkillMatches(skills[4], set.Skill3SnoPower, set.Skill3Rune)
                    && SkillMatches(skills[5], set.Skill4SnoPower, set.Skill4Rune);
            }
            catch { return false; }
        }

        private bool SkillMatches(IPlayerSkill current, ISnoPower savedPower, byte savedRune)
        {
            uint currentSno = current == null || current.SnoPower == null ? 0u : current.SnoPower.Sno;
            uint savedSno = savedPower == null ? 0u : savedPower.Sno;
            uint currentRune = current == null ? 0u : current.Rune;
            return currentSno == savedSno && currentRune == savedRune;
        }

        private bool PassivesMatch(IPlayerArmorySet set)
        {
            try
            {
                var passives = Hud.Game.Me.Powers == null ? null : Hud.Game.Me.Powers.PassiveSlots;
                if (passives == null)
                    return true;

                var current = new HashSet<uint>();
                for (int i = 0; i < passives.Length; i++)
                {
                    if (passives[i] != null && passives[i].Sno != 0)
                        current.Add(passives[i].Sno);
                }

                var saved = new HashSet<uint>();
                AddPassive(saved, set.PassiveSnoPower1);
                AddPassive(saved, set.PassiveSnoPower2);
                AddPassive(saved, set.PassiveSnoPower3);
                AddPassive(saved, set.PassiveSnoPower4);

                if (current.Count != saved.Count)
                    return false;
                foreach (uint sno in saved)
                {
                    if (!current.Contains(sno))
                        return false;
                }
                return true;
            }
            catch { return false; }
        }

        private void AddPassive(HashSet<uint> passives, ISnoPower power)
        {
            if (power != null && power.Sno != 0)
                passives.Add(power.Sno);
        }

        private bool CubeMatch(IPlayerArmorySet set)
        {
            try
            {
                var me = Hud.Game.Me;
                return Sno(me.CubeSnoItem1) == Sno(set.CubeSnoItem1)
                    && Sno(me.CubeSnoItem2) == Sno(set.CubeSnoItem2)
                    && Sno(me.CubeSnoItem3) == Sno(set.CubeSnoItem3)
                    && Sno(me.CubeSnoItem4) == Sno(set.CubeSnoItem4);
            }
            catch { return false; }
        }

        private uint Sno(ISnoItem item)
        {
            return item == null ? 0u : item.Sno;
        }

        private static void Mix(ref ulong hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
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
            _startSignature = 0;
            _cooldownSeen = false;
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
