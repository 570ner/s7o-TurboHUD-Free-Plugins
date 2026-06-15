using System;
using System.Text;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Cancels only the active Wizard Archon form buff exposed as "Wizard_Archon:2".
    // Lingering Swami/old-stack icons such as "Wizard_Archon:5" are intentionally ignored.
    public class s7o_ExitArchon : BasePlugin, IKeyEventHandler, IAfterCollectHandler, INewAreaHandler
    {
        // ============================================================
        // USER SETTINGS
        // ============================================================

        // Default cancel hotkey.
        // Examples: Key.Space, Key.X, Key.F1, Key.Comma.
        public Key CancelHotkey = Key.Space;

        // Diablo III's default Force Standstill key is Shift.
        // This is a Windows virtual-key code. Common values: Shift=0x10, Ctrl=0x11, Alt=0x12.
        public byte ForceStandstillVirtualKey = 0x10;

        // ValidationRetries = 7 means up to 8 total cancel attempts.
        public int ValidationRetries = 7;

        // Minimum delay between cancel attempts. This avoids spamming the same UI target
        // every collect tick while still retrying quickly enough for boss/RG pressure.
        public int RetryDelayMs = 90;

        // Primary target remains the active Archon buff icon. If that UI element is
        // not available, use FreeHUD's native CurrentSkills + skillbar UI as fallback.
        public bool UseArchonCancelSkillbarFallback = true;

        // Extra safety fallback: if the strict Wizard_Archon:2 buff icon is not visible
        // and the native skillbar target is unavailable, try any visible Wizard_Archon
        // buff-bar element before timing out.
        public bool UseLooseArchonBuffFallback = true;

        // The real local Archon cancel buff icon is normally the larger 50x50 icon
        // in the player's bottom buff bar. FreeHUD can also expose smaller Archon
        // UI elements from other overlays/party contexts with the same path marker;
        // those do not cancel local Archon. Prefer the larger/lower candidate when
        // more than one Wizard_Archon:2 UI element is visible.
        public bool PreferLargeLowerArchonBuffIcon = true;
        public float LocalArchonBuffMinIconSize = 40.0f;

        // Diagnostic logging is disabled by default for stable release.
        // Enable only when collecting a targeted troubleshooting log.
        public bool EnableDebugLogging = false;
        public string DebugLogFileName = "s7o_ExitArchon_DEBUG.log";
        public int DebugMinIntervalMs = 60;
        public int DebugSkillSnapshotCooldownMs = 350;

        // One physical key press should only queue one cancel job.
        public int DebounceMs = 120;

        // After a cancel click has been sent, ignore new hotkey repeats briefly.
        // This prevents stale SkillOverrideActive/loose buff fallback from touching
        // lingering Archon stack icons after the active form was already cancelled.
        public int PostCancelHotkeyLockoutMs = 700;

        // Safety timeout for the cancel phase.
        public int CancelTimeoutMs = 1600;

        // Ordered input handoff after Archon exits.
        public int RestoreStartDelayMs = 120;
        public int RestoreChordStepMs = 45;
        public int RestoreFinalDownMs = 120;

        // ============================================================

        private const int VkRightButton = 0x02;
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint RightDown = 0x0008;
        private const uint RightUp = 0x0010;
        private const uint KeyUp = 0x0002;
        private const string ActiveArchonMarker = "Wizard_Archon:2:";
        private const string ChatEditLinePath = "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline";

        private State _state;
        private bool _requestQueued;
        private bool _queuedRmb;
        private bool _queuedStandstill;
        private bool _queuedIntentCaptured;
        private bool _heldRmb;
        private bool _heldStandstill;
        private bool _cancelClickSent;

        private int _lastHotkeyTick;
        private int _cancelStartTick;
        private int _archonExitTick;
        private int _nextRestoreTick;
        private int _nextCancelAttemptTick;
        private int _lastCancelClickTick;
        private int _cancelAttemptsMade;
        private int _restoreStep;
        private int _lastDebugTick;
        private int _lastSkillSnapshotTick;
        private int _debugSequence;

        private IUiElement _chatEditLine;

        private enum State
        {
            Idle,
            WaitingForArchonExit,
            RestoringInput
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
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
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

        public s7o_ExitArchon()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            try { _chatEditLine = Hud.Render.RegisterUiElement(ChatEditLinePath, null, null); }
            catch { _chatEditLine = null; }

            ResetRuntimeState();
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ResetRuntimeState();
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!Enabled || keyEvent == null || !keyEvent.IsPressed) return;
            if (keyEvent.Key != CancelHotkey) return;

            // Space is also normal chat input. If the chat entry is open,
            // do not treat the key press as an Archon cancel request.
            if (IsChatEntryOpen()) return;

            int debounceMs = DebounceMs < 0 ? 0 : DebounceMs;
            int now = Environment.TickCount;

            if ((uint)(now - _lastHotkeyTick) < (uint)debounceMs)
                return;

            int postCancelLockoutMs = PostCancelHotkeyLockoutMs < 0 ? 0 : PostCancelHotkeyLockoutMs;
            if (_lastCancelClickTick != 0 && (uint)(now - _lastCancelClickTick) < (uint)postCancelLockoutMs)
                return;

            _lastHotkeyTick = now;

            // Capture user intent at hotkey time. Do not wait until AfterCollect:
            // RMB may read differently a few milliseconds later after UI/input changes.
            _queuedRmb = IsKeyDown(VkRightButton);
            _queuedStandstill = IsStandstillDown();
            _queuedIntentCaptured = true;
            _requestQueued = true;

            LogDebug("hotkey queued rmb=" + _queuedRmb + " standstill=" + _queuedStandstill + " chat=" + IsChatEntryOpen() + " state=" + _state + " " + GetArchonStateDebug(), true);
        }

        public void AfterCollect()
        {
            if (!IsValidContext())
            {
                if (_state != State.Idle || _requestQueued)
                    LogDebug("reset invalid-context state=" + _state + " request=" + _requestQueued, true);
                ResetRuntimeState();
                return;
            }

            if (_state == State.RestoringInput)
            {
                ContinueInputRestore();
                return;
            }

            bool archonActive = IsArchonActive();

            if (_state == State.WaitingForArchonExit)
            {
                ContinueWaitingForArchonExit(archonActive);
                return;
            }

            if (!_requestQueued)
                return;

            _requestQueued = false;

            if (!archonActive)
                return;

            BeginCancel();
        }

        private bool IsValidContext()
        {
            if (!Enabled || Hud == null || Hud.Game == null) return false;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.Me == null) return false;

            var me = Hud.Game.Me;
            return me.HeroClassDefinition != null && me.HeroClassDefinition.HeroClass == HeroClass.Wizard;
        }

        private void BeginCancel()
        {
            _state = State.WaitingForArchonExit;
            _cancelStartTick = Environment.TickCount;
            _archonExitTick = 0;
            _nextCancelAttemptTick = 0;
            _cancelAttemptsMade = 0;
            _cancelClickSent = false;

            _heldRmb = (_queuedIntentCaptured && _queuedRmb) || IsKeyDown(VkRightButton);
            _heldStandstill = (_queuedIntentCaptured && _queuedStandstill) || IsStandstillDown();

            LogDebug("begin-cancel heldRmb=" + _heldRmb + " heldStandstill=" + _heldStandstill + " maxAttempts=" + GetMaxCancelAttempts() + " timeoutMs=" + Math.Max(0, CancelTimeoutMs) + " " + GetArchonStateDebug(), true);
            LogSkillSnapshot("begin-cancel");

            TryClickCancelTarget();
        }

        private void ContinueWaitingForArchonExit(bool archonActive)
        {
            int now = Environment.TickCount;

            if (!archonActive)
            {
                if (_archonExitTick == 0)
                {
                    _archonExitTick = now;
                    LogDebug("archon-inactive detected attempts=" + _cancelAttemptsMade + " clickSent=" + _cancelClickSent + " elapsedMs=" + (now - _cancelStartTick), true);
                }

                if ((uint)(now - _archonExitTick) >= (uint)Math.Max(0, RestoreStartDelayMs))
                    BeginInputRestore();

                return;
            }

            // Do not treat a missing buff-bar target as success. On boss/RG pulls the
            // buff UI can be hidden/stale for a frame even while Archon is still active.
            // Keep retrying with the skillbar fallback until Archon actually drops or the
            // timeout expires.
            if (_cancelAttemptsMade < GetMaxCancelAttempts() && (int)(now - _nextCancelAttemptTick) >= 0)
            {
                TryClickCancelTarget();
                return;
            }

            int timeoutMs = CancelTimeoutMs < 0 ? 0 : CancelTimeoutMs;
            if ((uint)(now - _cancelStartTick) >= (uint)timeoutMs)
            {
                LogDebug("cancel-timeout attempts=" + _cancelAttemptsMade + " clickSent=" + _cancelClickSent + " " + GetArchonStateDebug(), true);
                LogSkillSnapshot("cancel-timeout");
                BeginInputRestore();
            }
        }

        private void BeginInputRestore()
        {
            if (!_cancelClickSent || (!_heldRmb && !_heldStandstill))
            {
                LogDebug("restore-skip clickSent=" + _cancelClickSent + " heldRmb=" + _heldRmb + " heldStandstill=" + _heldStandstill, true);
                ResetRuntimeState();
                return;
            }

            LogDebug("restore-begin heldRmb=" + _heldRmb + " heldStandstill=" + _heldStandstill, true);
            _state = State.RestoringInput;
            _restoreStep = 0;
            _nextRestoreTick = Environment.TickCount;

            ContinueInputRestore();
        }

        private void ContinueInputRestore()
        {
            int now = Environment.TickCount;
            if ((int)(now - _nextRestoreTick) < 0)
                return;

            int stepMs = Math.Max(1, RestoreChordStepMs);
            int finalMs = Math.Max(1, RestoreFinalDownMs);

            switch (_restoreStep)
            {
                case 0:
                    if (_heldStandstill && ForceStandstillVirtualKey != 0)
                        SendKey(ForceStandstillVirtualKey, true);

                    if (_heldRmb)
                        SendMouse(RightUp);

                    _restoreStep = 1;
                    _nextRestoreTick = now + stepMs;
                    return;

                case 1:
                    if (_heldStandstill && ForceStandstillVirtualKey != 0)
                        SendKey(ForceStandstillVirtualKey, false);

                    _restoreStep = 2;
                    _nextRestoreTick = now + stepMs;
                    return;

                case 2:
                    if (_heldRmb)
                    {
                        Point current;
                        if (GetCursorPos(out current))
                            SetCursorPos(current.X, current.Y);

                        SendMouse(RightDown);
                    }

                    _restoreStep = 3;
                    _nextRestoreTick = now + finalMs;
                    return;

                default:
                    if (_heldRmb)
                        SendMouse(RightDown);

                    ResetRuntimeState();
                    return;
            }
        }

        private void TryClickCancelTarget()
        {
            int now = Environment.TickCount;
            _nextCancelAttemptTick = now + Math.Max(1, RetryDelayMs);

            IUiElement target;
            bool useLeftClick;
            string targetKind;
            if (!TryFindCancelTarget(out target, out useLeftClick, out targetKind))
            {
                LogDebug("attempt-no-target attempt=" + (_cancelAttemptsMade + 1) + " " + GetArchonStateDebug(), true);
                LogSkillSnapshot("attempt-no-target");
                return;
            }

            try { target.Refresh(); }
            catch { }

            if (!IsUsableTarget(target))
            {
                LogDebug("attempt-target-not-usable kind=" + targetKind + " path=" + SafePath(target), true);
                return;
            }

            Point cursor;
            if (!GetCursorPos(out cursor))
            {
                LogDebug("attempt-no-cursor kind=" + targetKind, true);
                return;
            }

            try
            {
                // Release Shift first, then RMB, so the UI click is not blocked.
                // Rebuild the user's Shift+RMB chord only after Archon is gone.
                uint standstillUpSent = 0;
                uint rmbUpSent = 0;
                uint clickSent = 0;

                if (_heldStandstill && ForceStandstillVirtualKey != 0)
                    standstillUpSent = SendKey(ForceStandstillVirtualKey, true);

                if (_heldRmb)
                    rmbUpSent = SendMouse(RightUp);

                Point targetPoint = GetUiElementCenterOnScreen(target);

                SetCursorPos(targetPoint.X, targetPoint.Y);

                if (useLeftClick)
                    clickSent = SendLeftClick();
                else
                    clickSent = SendRightClick();

                _cancelClickSent = true;
                _lastCancelClickTick = now;
                _cancelAttemptsMade++;

                var rect = target.Rectangle;
                LogDebug("attempt-click kind=" + targetKind
                    + " attempt=" + _cancelAttemptsMade + "/" + GetMaxCancelAttempts()
                    + " left=" + useLeftClick
                    + " cursor=" + cursor.X + "," + cursor.Y
                    + " target=" + targetPoint.X + "," + targetPoint.Y
                    + " rect=" + RectToString(rect)
                    + " sendKeyUp=" + standstillUpSent
                    + " sendRmbUp=" + rmbUpSent
                    + " sendClick=" + clickSent
                    + " path=" + SafePath(target), true);
            }
            catch (Exception ex)
            {
                LogDebug("attempt-exception " + ex.GetType().Name + ": " + ex.Message, true);
            }
            finally
            {
                SetCursorPos(cursor.X, cursor.Y);
            }
        }

        private bool TryFindCancelTarget(out IUiElement target, out bool useLeftClick, out string targetKind)
        {
            target = FindActiveArchonBuff();
            useLeftClick = false;
            targetKind = "strict-buff";
            if (target != null)
                return true;

            if (UseArchonCancelSkillbarFallback)
            {
                target = FindArchonCancelSkillbarButton();
                useLeftClick = true;
                targetKind = "skillbar-cancel";
                if (target != null)
                    return true;
            }

            if (UseLooseArchonBuffFallback && !_cancelClickSent)
            {
                target = FindLooseArchonBuff();
                useLeftClick = false;
                targetKind = "loose-buff";
                if (target != null)
                    return true;
            }

            target = null;
            useLeftClick = false;
            targetKind = "none";
            return false;
        }

        private IUiElement FindArchonCancelSkillbarButton()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                    return null;

                var cancelPower = Hud.Sno.SnoPowers.Wizard_ArchonCancel;
                if (cancelPower == null)
                    return null;

                uint cancelSno = cancelPower.Sno;

                IUiElement ui;

                ui = FindArchonCancelSkillbarButtonFromSkills(Hud.Game.Me.Powers.CurrentSkills, cancelSno);
                if (ui != null)
                    return ui;

                // Some FreeHUD builds can expose override skills inconsistently for a frame.
                // UsedSkills and UsedWizardPowers are native, cheap fallbacks and only return
                // a UI target if it is visible/usable.
                ui = FindArchonCancelSkillbarButtonFromSkills(Hud.Game.Me.Powers.UsedSkills, cancelSno);
                if (ui != null)
                    return ui;

                if (Hud.Game.Me.Powers.UsedWizardPowers != null)
                {
                    var archonCancel = Hud.Game.Me.Powers.UsedWizardPowers.ArchonCancel;
                    if (IsArchonCancelSkill(archonCancel, cancelSno))
                    {
                        ui = GetSkillUiIfUsable(archonCancel);
                        if (ui != null)
                            return ui;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private IUiElement FindArchonCancelSkillbarButtonFromSkills(System.Collections.Generic.IEnumerable<IPlayerSkill> skills, uint cancelSno)
        {
            if (skills == null)
                return null;

            foreach (var skill in skills)
            {
                if (!IsArchonCancelSkill(skill, cancelSno))
                    continue;

                var ui = GetSkillUiIfUsable(skill);
                if (ui != null)
                    return ui;
            }

            return null;
        }

        private IUiElement GetSkillUiIfUsable(IPlayerSkill skill)
        {
            if (skill == null)
                return null;

            try
            {
                var ui = Hud.Render.GetPlayerSkillUiElement(skill.Key);
                if (ui == null)
                    return null;

                ui.Refresh();
                return IsUsableTarget(ui) ? ui : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsArchonCancelSkill(IPlayerSkill skill, uint cancelSno)
        {
            if (skill == null)
                return false;

            var current = skill.CurrentSnoPower;
            if (current != null && current.Sno == cancelSno)
                return true;

            var overridePower = skill.OverrideSnoPower;
            if (overridePower != null && overridePower.Sno == cancelSno)
                return true;

            var basePower = skill.SnoPower;
            return basePower != null && basePower.Sno == cancelSno;
        }

        private Point GetUiElementCenterOnScreen(IUiElement element)
        {
            var rect = element.Rectangle;

            var point = new Point
            {
                X = (int)Math.Round(rect.X + rect.Width * 0.5f),
                Y = (int)Math.Round(rect.Y + rect.Height * 0.5f)
            };

            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero && ClientToScreen(hwnd, ref point))
                    return point;
            }
            catch
            {
            }

            try
            {
                if (Hud != null && Hud.Window != null)
                {
                    var offset = Hud.Window.Offset;
                    point.X += offset.X;
                    point.Y += offset.Y;
                }
            }
            catch
            {
            }

            return point;
        }

        private IUiElement FindActiveArchonBuff()
        {
            return FindBestArchonBuffCandidate(ActiveArchonMarker, "strict-buff-candidates");
        }

        private IUiElement FindLooseArchonBuff()
        {
            return FindBestArchonBuffCandidate("Wizard_Archon:", "loose-buff-candidates");
        }

        private IUiElement FindBestArchonBuffCandidate(string marker, string debugLabel)
        {
            IUiElement best = null;
            double bestScore = double.MinValue;
            int matches = 0;
            var debug = EnableDebugLogging ? new StringBuilder() : null;

            foreach (var element in Hud.Render.BuffBarUiElements)
            {
                if (!IsUsableTarget(element))
                    continue;

                var path = element.Path;
                if (string.IsNullOrEmpty(path))
                    continue;

                if (path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                matches++;
                var rect = element.Rectangle;
                double score = GetArchonBuffCandidateScore(rect);

                if (debug != null)
                {
                    if (debug.Length > 0)
                        debug.Append(" | ");
                    debug.Append(RectToString(rect))
                        .Append(" score=").Append(score.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(" path=").Append(SafePath(element));
                }

                if (best == null || score > bestScore)
                {
                    best = element;
                    bestScore = score;
                }
            }

            if (matches > 1 && debug != null)
                LogDebug(debugLabel + " matches=" + matches + " selected=" + (best == null ? "null" : RectToString(best.Rectangle)) + " :: " + debug.ToString(), true);

            return best;
        }

        private double GetArchonBuffCandidateScore(System.Drawing.RectangleF rect)
        {
            double area = Math.Max(0.0d, rect.Width) * Math.Max(0.0d, rect.Height);
            double score = area;

            if (PreferLargeLowerArchonBuffIcon)
            {
                float minSize = Math.Max(1.0f, LocalArchonBuffMinIconSize);
                bool likelyLocalBuffBarIcon = rect.Width >= minSize && rect.Height >= minSize;

                if (likelyLocalBuffBarIcon)
                    score += 1000000.0d;

                // When duplicate Wizard_Archon:2 elements exist, the local cancel icon
                // is the lower player buff-bar icon, not the small portrait/overlay icon.
                score += Math.Max(0.0d, rect.Bottom) * 1000.0d;
            }

            return score;
        }

        private static bool IsUsableTarget(IUiElement element)
        {
            if (element == null || !element.Visible)
                return false;

            var rect = element.Rectangle;
            if (float.IsNaN(rect.X) || float.IsNaN(rect.Y) || float.IsNaN(rect.Width) || float.IsNaN(rect.Height))
                return false;

            return rect.Width > 1.0f && rect.Height > 1.0f;
        }

        private void LogDebug(string message, bool force = false)
        {
            if (!EnableDebugLogging)
                return;

            int now = Environment.TickCount;
            int minMs = Math.Max(0, DebugMinIntervalMs);
            if (!force && minMs > 0 && (uint)(now - _lastDebugTick) < (uint)minMs)
                return;

            _lastDebugTick = now;

            try
            {
                if (Hud != null && Hud.TextLog != null)
                    Hud.TextLog.Log(DebugLogFileName, "#" + (++_debugSequence) + " tick=" + now + " " + message, true, true);
            }
            catch
            {
            }
        }

        private void LogSkillSnapshot(string phase)
        {
            if (!EnableDebugLogging)
                return;

            int now = Environment.TickCount;
            int cooldownMs = Math.Max(0, DebugSkillSnapshotCooldownMs);
            if (cooldownMs > 0 && (uint)(now - _lastSkillSnapshotTick) < (uint)cooldownMs)
                return;

            _lastSkillSnapshotTick = now;

            try
            {
                var sb = new StringBuilder();
                sb.Append("snapshot phase=").Append(phase).Append(' ').Append(GetArchonStateDebug());
                AppendSkillListDebug(sb, " current", Hud.Game.Me.Powers.CurrentSkills);
                AppendSkillListDebug(sb, " used", Hud.Game.Me.Powers.UsedSkills);
                AppendBuffUiDebug(sb);
                LogDebug(sb.ToString(), true);
            }
            catch (Exception ex)
            {
                LogDebug("snapshot-exception " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }

        private void AppendSkillListDebug(StringBuilder sb, string label, System.Collections.Generic.IEnumerable<IPlayerSkill> skills)
        {
            sb.Append(label).Append("Skills=");
            if (skills == null)
            {
                sb.Append("null");
                return;
            }

            int count = 0;
            foreach (var skill in skills)
            {
                if (skill == null)
                    continue;

                if (count++ > 0)
                    sb.Append(" | ");

                sb.Append(skill.Key)
                    .Append(" base=").Append(PowerDebug(skill.SnoPower))
                    .Append(" override=").Append(PowerDebug(skill.OverrideSnoPower))
                    .Append(" current=").Append(PowerDebug(skill.CurrentSnoPower));
            }

            if (count == 0)
                sb.Append("empty");
        }

        private void AppendBuffUiDebug(StringBuilder sb)
        {
            sb.Append(" buffUi=");
            int count = 0;
            foreach (var element in Hud.Render.BuffBarUiElements)
            {
                if (element == null)
                    continue;

                var path = element.Path;
                if (string.IsNullOrEmpty(path) || path.IndexOf("Archon", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (count++ > 0)
                    sb.Append(" | ");

                try { element.Refresh(); } catch { }
                sb.Append(SafePath(element)).Append(" vis=").Append(element.Visible).Append(" rect=").Append(RectToString(element.Rectangle));
            }

            if (count == 0)
                sb.Append("none");
        }

        private string GetArchonStateDebug()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                    return "archonState unavailable";

                var powers = Hud.Game.Me.Powers;
                uint archonSno = 0;
                try
                {
                    var archonPower = Hud.Sno.SnoPowers.Wizard_Archon;
                    if (archonPower != null)
                        archonSno = archonPower.Sno;
                }
                catch
                {
                }

                var sb = new StringBuilder();
                sb.Append("override=").Append(powers.SkillOverrideActive);
                if (archonSno != 0)
                {
                    sb.Append(" archonSno=").Append(archonSno).Append(" buffs=");
                    for (int i = 0; i <= 8; i++)
                    {
                        if (i > 0)
                            sb.Append(',');
                        bool active = false;
                        try { active = powers.BuffIsActive(archonSno, i); } catch { }
                        sb.Append(i).Append(':').Append(active ? '1' : '0');
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return "archonState exception";
            }
        }

        private static string PowerDebug(ISnoPower power)
        {
            if (power == null)
                return "null";

            string code = power.Code;
            if (string.IsNullOrEmpty(code))
                code = power.NameEnglish;
            if (string.IsNullOrEmpty(code))
                code = "?";

            return power.Sno + ":" + code;
        }

        private static string RectToString(System.Drawing.RectangleF rect)
        {
            return ((int)Math.Round(rect.X)) + "," + ((int)Math.Round(rect.Y)) + "," + ((int)Math.Round(rect.Width)) + "x" + ((int)Math.Round(rect.Height));
        }

        private static string SafePath(IUiElement element)
        {
            try
            {
                if (element == null || string.IsNullOrEmpty(element.Path))
                    return "null";
                return element.Path;
            }
            catch
            {
                return "path-exception";
            }
        }

        private bool IsArchonActive()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                    return false;

                var powers = Hud.Game.Me.Powers;

                var archonPower = Hud.Sno.SnoPowers.Wizard_Archon;
                if (archonPower != null && powers.BuffIsActive(archonPower.Sno, 2))
                    return true;

                return powers.SkillOverrideActive;
            }
            catch
            {
                return false;
            }
        }

        private int GetMaxCancelAttempts()
        {
            return Math.Max(1, Math.Max(0, ValidationRetries) + 1);
        }

        private bool IsStandstillDown()
        {
            return ForceStandstillVirtualKey != 0 && IsKeyDown(ForceStandstillVirtualKey);
        }

        private bool IsChatEntryOpen()
        {
            try
            {
                if (_chatEditLine == null)
                    return false;

                _chatEditLine.Refresh();
                return _chatEditLine.Visible;
            }
            catch
            {
                return false;
            }
        }

        private void ResetRuntimeState()
        {
            _state = State.Idle;
            _requestQueued = false;
            _queuedRmb = false;
            _queuedStandstill = false;
            _queuedIntentCaptured = false;
            _heldRmb = false;
            _heldStandstill = false;
            _cancelClickSent = false;
            _lastHotkeyTick = 0;
            _cancelStartTick = 0;
            _archonExitTick = 0;
            _nextRestoreTick = 0;
            _nextCancelAttemptTick = 0;
            _cancelAttemptsMade = 0;
            _restoreStep = 0;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static uint SendLeftClick()
        {
            return SendMouse(LeftDown) + SendMouse(LeftUp);
        }

        private static uint SendRightClick()
        {
            return SendMouse(RightDown) + SendMouse(RightUp);
        }

        private static uint SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;

            return SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        private static uint SendKey(byte virtualKey, bool keyUp)
        {
            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;

            return SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }
    }
}
