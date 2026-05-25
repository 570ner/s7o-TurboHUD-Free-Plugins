using System;
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

        // ValidationRetries = 1 means up to 2 total buff-click attempts.
        public int ValidationRetries = 1;

        // One physical key press should only queue one cancel job.
        public int DebounceMs = 120;

        // Safety timeout for the cancel phase.
        public int CancelTimeoutMs = 750;

        // Ordered input handoff after Archon exits.
        public int RestoreStartDelayMs = 120;
        public int RestoreChordStepMs = 45;
        public int RestoreFinalDownMs = 120;

        // ============================================================

        private const int VkRightButton = 0x02;
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
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
        private int _retriesLeft;
        private int _restoreStep;

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

            _lastHotkeyTick = now;

            // Capture user intent at hotkey time. Do not wait until AfterCollect:
            // RMB may read differently a few milliseconds later after UI/input changes.
            _queuedRmb = IsKeyDown(VkRightButton);
            _queuedStandstill = IsStandstillDown();
            _queuedIntentCaptured = true;
            _requestQueued = true;
        }

        public void AfterCollect()
        {
            if (!IsValidContext())
            {
                ResetRuntimeState();
                return;
            }

            if (_state == State.RestoringInput)
            {
                ContinueInputRestore();
                return;
            }

            bool archonActive = Hud.Game.Me.Powers != null && Hud.Game.Me.Powers.SkillOverrideActive;

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
            _retriesLeft = Math.Max(0, ValidationRetries);
            _cancelClickSent = false;

            _heldRmb = (_queuedIntentCaptured && _queuedRmb) || IsKeyDown(VkRightButton);
            _heldStandstill = (_queuedIntentCaptured && _queuedStandstill) || IsStandstillDown();

            ClickActiveArchonBuff();
        }

        private void ContinueWaitingForArchonExit(bool archonActive)
        {
            bool targetExists = HasActiveArchonBuff();

            if (!archonActive || !targetExists)
            {
                if (_archonExitTick == 0)
                    _archonExitTick = Environment.TickCount;

                if ((uint)(Environment.TickCount - _archonExitTick) >= (uint)Math.Max(0, RestoreStartDelayMs))
                    BeginInputRestore();

                return;
            }

            if (_retriesLeft > 0)
            {
                _retriesLeft--;
                ClickActiveArchonBuff();
                return;
            }

            int timeoutMs = CancelTimeoutMs < 0 ? 0 : CancelTimeoutMs;
            if ((uint)(Environment.TickCount - _cancelStartTick) >= (uint)timeoutMs)
                BeginInputRestore();
        }

        private void BeginInputRestore()
        {
            if (!_cancelClickSent || (!_heldRmb && !_heldStandstill))
            {
                ResetRuntimeState();
                return;
            }

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

        private void ClickActiveArchonBuff()
        {
            var target = FindActiveArchonBuff();
            if (target == null)
                return;

            target.Refresh();
            if (!IsUsableTarget(target))
                return;

            Point cursor;
            if (!GetCursorPos(out cursor))
                return;

            try
            {
                // Release Shift first, then RMB, so the buff-bar click is not blocked.
                // Rebuild the user's Shift+RMB chord only after Archon is gone.
                if (_heldStandstill && ForceStandstillVirtualKey != 0)
                    SendKey(ForceStandstillVirtualKey, true);

                if (_heldRmb)
                    SendMouse(RightUp);

                Point targetPoint = GetUiElementCenterOnScreen(target);

                SetCursorPos(targetPoint.X, targetPoint.Y);
                SendRightClick();

                _cancelClickSent = true;
            }
            finally
            {
                SetCursorPos(cursor.X, cursor.Y);
            }
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
            foreach (var element in Hud.Render.BuffBarUiElements)
            {
                if (!IsUsableTarget(element)) continue;

                var path = element.Path;
                if (string.IsNullOrEmpty(path)) continue;

                if (path.IndexOf(ActiveArchonMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return element;
            }

            return null;
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

        private bool HasActiveArchonBuff()
        {
            return FindActiveArchonBuff() != null;
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
            _retriesLeft = 0;
            _restoreStep = 0;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static void SendRightClick()
        {
            SendMouse(RightDown);
            SendMouse(RightUp);
        }

        private static void SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;

            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        private static void SendKey(byte virtualKey, bool keyUp)
        {
            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;

            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }
    }
}
