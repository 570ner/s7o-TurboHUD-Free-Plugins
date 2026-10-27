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

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        private static bool TryMoveClientPoint(IController hud,int x,int y,out Point screen)
        {
            screen=Point.Empty;
            if(hud==null || hud.Window==null || !hud.Window.IsForeground)return false;
            var size=hud.Window.Size;
            if(size.Width<=0 || size.Height<=0 || x<0 || y<0 || x>=size.Width || y>=size.Height)return false;
            IntPtr prior=IntPtr.Zero;
            try
            {
                // Limit DPI awareness to this conversion; never change HUD process settings.
                try { prior=SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch(EntryPointNotFoundException) { }
                var window=GetForegroundWindow(); NativeRect rect;
                if(window!=IntPtr.Zero && GetClientRect(window,out rect))
                {
                    int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
                    if(width<=0 || height<=0)return false;
                    var point=new NativePoint {
                        X=(int)Math.Round((double)x*width/size.Width),
                        Y=(int)Math.Round((double)y*height/size.Height) };
                    if(!ClientToScreen(window,ref point) || window!=GetForegroundWindow()
                        || !hud.Window.IsForeground)return false;
                    screen=new Point(point.X,point.Y); // Negative monitor coordinates are valid.
                    return SetCursorPos(screen.X,screen.Y) && hud.Window.IsForeground;
                }
                // Retain the previous signed-offset conversion if native geometry is unavailable.
                var offset=hud.Window.Offset;
                long sx=(long)offset.X+x,sy=(long)offset.Y+y;
                if(sx<int.MinValue || sx>int.MaxValue || sy<int.MinValue || sy>int.MaxValue)return false;
                screen=new Point((int)sx,(int)sy);return SetCursorPos(screen.X,screen.Y) && hud.Window.IsForeground;
            }
            catch { return false; }
            finally { if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior); }
        }

        [DllImport("user32.dll", EntryPoint="PostMessageW", SetLastError=true)]
        private static extern bool PostUiMessage(IntPtr window,uint message,IntPtr keys,IntPtr point);
        [DllImport("user32.dll", EntryPoint="GetClassNameW", CharSet=CharSet.Unicode)]
        private static extern int MessageWindowClass(IntPtr window,StringBuilder text,int capacity);
        [DllImport("user32.dll", EntryPoint="GetAsyncKeyState")]
        private static extern short MessageMouseState(int key);
        private static IntPtr _messageReleaseWindow, _messageReleasePoint;
        private static int _messageInputFrame, _messageInputTick, _messageManualMask;
        private static int _messageManualReleaseFrame = -1, _messageManualReleaseTick = int.MinValue;
        private static int _messageDownFrame, _messageBalancedFrame = -1, _messageBalancedTick = int.MinValue;
        private static bool _messageMouseKnown;
        public static string MessageInputGate { get; private set; } = "not-observed";
        public static bool MessageReleasePending { get { return _messageReleaseWindow != IntPtr.Zero; } }
        public static bool MessagePairNeedsHandoff
        {
            get { return MessageReleasePending || (_messageBalancedFrame >= 0
                && (_messageBalancedFrame == _messageInputFrame || _messageBalancedTick == _messageInputTick)); }
        }

        public static void BeginMessageInputFrame(IController hud)
        {
            unchecked { _messageInputFrame++; }
            _messageInputTick = hud != null && hud.Game != null ? hud.Game.CurrentGameTick : int.MinValue;
            MessageMouseHeld();
            // Balance only our own deferred DOWN, after manual ownership has ended.
            // This also runs when closing the pane means no further click is requested.
            if (MessageReleasePending && hud != null && hud.Window != null && hud.Window.IsForeground
                && GetForegroundWindow() == _messageReleaseWindow) CompleteMessageRelease();
        }

        public static bool MessageMouseHeld()
        {
            int mask = ((MessageMouseState(0x01) & 0x8000) != 0 ? 1 : 0)
                | ((MessageMouseState(0x02) & 0x8000) != 0 ? 2 : 0)
                | ((MessageMouseState(0x04) & 0x8000) != 0 ? 4 : 0);
            if (!_messageMouseKnown || mask != _messageManualMask)
            {
                int previous = _messageManualMask;
                if (_messageMouseKnown && previous != 0 && mask == 0)
                {
                    _messageManualReleaseFrame = _messageInputFrame;
                    _messageManualReleaseTick = _messageInputTick;
                }
                _messageManualMask = mask;
                _messageMouseKnown = true;
            }
            return mask != 0;
        }

        private static bool MessageInputAvailable()
        {
            bool held = MessageMouseHeld();
            string reason = held ? "manual-held" : _messageInputFrame == 0 ? "not-observed"
                : _messageManualReleaseFrame >= 0 && (_messageManualReleaseFrame == _messageInputFrame
                    || _messageManualReleaseTick == _messageInputTick) ? "manual-release-handoff"
                : !MessageReleasePending && MessagePairNeedsHandoff ? "synthetic-release-handoff" : "ready";
            MessageInputGate = reason;
            return reason == "ready";
        }

        private static bool CompleteMessageRelease()
        {
            if (!MessageInputAvailable()) return false;
            if (_messageReleaseWindow == IntPtr.Zero) return true;
            // The UP matches our original window and point; never release the OS mouse.
            if (!PostUiMessage(_messageReleaseWindow, 0x0202, IntPtr.Zero, _messageReleasePoint)) return false;
            if (_messageDownFrame != _messageInputFrame)
            { _messageBalancedFrame = _messageInputFrame; _messageBalancedTick = _messageInputTick; }
            _messageReleaseWindow = _messageReleasePoint = IntPtr.Zero;
            return true;
        }

        public static bool MessageClickClient(IController hud, int x, int y)
        {
            if (hud == null || hud.Window == null || !hud.Window.IsForeground) return false;
            if (!MessageInputAvailable() || !CompleteMessageRelease()) return false;
            IntPtr window, point;
            if (!TryMessagePoint(hud, x, y, false, out window, out point) || !MessageInputAvailable()) return false;
            if (!PostUiMessage(window, 0x0201, new IntPtr(1), point)) return false;
            _messageReleaseWindow = window; _messageReleasePoint = point;
            _messageDownFrame = _messageInputFrame;
            return CompleteMessageRelease();
        }


        private static IntPtr MessagePackedPoint(int x,int y)
        {
            return new IntPtr(unchecked((int)((uint)(ushort)x|((uint)(ushort)y<<16))));
        }
        private static bool TryMessagePoint(IController hud,int x,int y,bool screen,out IntPtr window,out IntPtr packed)
        {
            window=packed=IntPtr.Zero;
            if(hud==null || hud.Window==null || !hud.Window.IsForeground || MessageMouseHeld())return false;
            var size=hud.Window.Size;
            if(size.Width<=0 || size.Height<=0 || x<0 || y<0 || x>=size.Width || y>=size.Height)return false;
            IntPtr prior=IntPtr.Zero;
            try
            {
                try { prior=SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch(EntryPointNotFoundException) { }
                window=GetForegroundWindow();
                var name=new StringBuilder(64);
                if(window==IntPtr.Zero || MessageWindowClass(window,name,name.Capacity)==0
                    || name.ToString()!="D3 Main Window Class")return false;
                NativeRect rect;
                if(!GetClientRect(window,out rect) || rect.Right<=rect.Left || rect.Bottom<=rect.Top)return false;
                var point=new NativePoint {
                    X=(int)Math.Round((double)x*(rect.Right-rect.Left)/size.Width),
                    Y=(int)Math.Round((double)y*(rect.Bottom-rect.Top)/size.Height) };
                if(screen && !ClientToScreen(window,ref point))return false;
                if(point.X<short.MinValue || point.X>short.MaxValue || point.Y<short.MinValue || point.Y>short.MaxValue
                    || GetForegroundWindow()!=window || !hud.Window.IsForeground || MessageMouseHeld())return false;
                packed=MessagePackedPoint(point.X,point.Y);return true;
            }
            catch { return false; }
            finally { if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior); }
        }



        public static bool MouseMoveClient(IController hud, int x, int y)
        {
            Point screen;
            return TryMoveClientPoint(hud, x, y, out screen);
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
        // Experimental pane-only input; false retains the established physical-mouse path.
        public bool UseWindowMessageInput { get; set; } = true;

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
                public int UserInterferenceCursorThresholdPx { get; set; } = 18;
        public int UserInterferenceIgnoreAfterPluginInputMs { get; set; } = 80;
        public int UserInterferenceSettleDelayMs { get; set; } = 100;
        public int SoftRestartBackoffMs { get; set; } = 40;
        public int MaxSoftRestartsPerWindow { get; set; } = 4;
        public int SoftRestartWindowMs { get; set; } = 5000;
        public int TargetValidationTimeoutMs { get; set; } = 250;
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

        // Cell geometry and row grouping are measured from live FreeHUD rectangles.

        // Canonical identities from the FreeHUD SnoItemList and the v1.5.2 HUD_MENU.
        // Native SNO avoids depending on a particular client language for special gem rules.
        private static readonly Dictionary<uint, string> CanonicalGemNameBySno = new Dictionary<uint, string>
        {
            { 3248511367u, "Bane of the Powerful" },
            { 3249948847u, "Bane of the Stricken" },
            { 3248547304u, "Bane of the Trapped" },
            { 3249805099u, "Boon of the Hoarder" },
            { 3250847272u, "Boyarsky's Chip" },
            { 3249661351u, "Enforcer" },
            { 3249876973u, "Esoteric Alteration" },
            { 3248583241u, "Gem of Ease" },
            { 3248655115u, "Gem of Efficacious Toxin" },
            { 3248762926u, "Gogok of Swiftness" },
            { 3250883209u, "Iceblink" },
            { 3248798863u, "Invigorating Gemstone" },
            { 3250955083u, "Legacy of Dreams" },
            { 3248726989u, "Mirinae, Teardrop of the Starweaver" },
            { 3249912910u, "Molten Wildebeest's Gizzard" },
            { 3249697288u, "Moratorium" },
            { 3249984784u, "Mutilation Guard" },
            { 3248691052u, "Pain Enhancer" },
            { 3249769162u, "Simplicity's Strength" },
            { 3249841036u, "Taeguk" },
            { 3248619178u, "Wreath of Lightning" },
            { 1141915165u, "Whisper of Atonement" },
            { 3249733225u, "Zei's Stone of Vengeance" },
        };

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
        private IUiElement _scrollBar, _scrollThumb, _scrollUp, _scrollDown;
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

        private IFont _warningFont;

        private AutomationStage _stage = AutomationStage.Idle;
        private string _paneWarningMessage = string.Empty;
        private int _lastActionTick = int.MinValue;

        private int _lastUpgradeClickTick = int.MinValue;
        private int _portalAnchorClickTick = int.MinValue;
        private int _lastObservedUpgradeAttempts = int.MinValue;
        private int _lastUpgradeProgressTick = int.MinValue;
        private int _lastPortalActionTick = int.MinValue;
        private int _lastRecoveryUpgradeAttempts = int.MinValue;
        private int _portalRequestedTick = int.MinValue;
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
        private bool _retargetNeedsButtonReady;
        private int _retargetStartedTick = int.MinValue;
        private PendingInputKind _pendingInputKind;
        private ushort _pendingKey;
        private int _pendingInputReleaseTick = int.MinValue;
        private bool _pendingRestoreCursor;
        private int _pendingRestoreCursorX;
        private int _pendingRestoreCursorY;
        private bool _pendingInputReleaseSucceeded = true;
        private int _pendingReleaseRetries;
        private UrshiCancelStage _urshiCancelStage;
        private int _urshiCancelDueTick = int.MinValue;
        private int _urshiCancelChecksRemaining;
        private bool _autoRunning;
        private int _cursorBaselineX = int.MinValue;
        private int _cursorBaselineY = int.MinValue;
        private int _cursorIgnoreUntilTick = int.MinValue;
        private bool _softRestartPending;
        private int _softRestartBlockedUntilTick = int.MinValue;
        private int _userSettleUntilTick = int.MinValue;
        private int _softRestartWindowStartTick = int.MinValue;
        private int _softRestartCountInWindow;

        private GemTarget _target;
        private readonly List<GemOrderEntry> _orderedGems = new List<GemOrderEntry>();
        private uint _targetAcd;          // AcdId of target gem — for ACD-based post-click validation

        private readonly List<AutoPlanStep> _autoPlan = new List<AutoPlanStep>();
        private string _autoPlanSummary = string.Empty;
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
        private uint _latchedItemButtonAcd;
        private int _latchedItemButtonAcdTick = int.MinValue;
        private uint _selectedReadyLatchedAcd;
        private int _selectedReadyTick = int.MinValue;
        private int _runningUiLossCount;
        private bool _preserveRunningStateOnReacquire;

        private bool _probeActive;

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
            _scrollThumb = Hud.Render.RegisterUiElement(root + ".items_list._scrollbar.thumb", null, null);
            _scrollUp = Hud.Render.RegisterUiElement(root + ".items_list._scrollbar.up", null, null);
            _scrollDown = Hud.Render.RegisterUiElement(root + ".items_list._scrollbar.down", null, null);
            _upgradeButton = Hud.Render.RegisterUiElement(root + ".upgrade_button", null, null);
            _itemButton = Hud.Render.RegisterUiElement(root + ".item_button", null, null);
            _gemStatusText = Hud.Render.RegisterUiElement(root + ".gemStatusText", null, null);
            _conversationDialogMain = Hud.Render.RegisterUiElement("Root.NormalLayer.conversation_dialog_main", null, null);
            _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);

            RegisterNativeRows(24);

            _warningFont = Hud.Render.CreateFont("tahoma", 8, 255, 255, 70, 70, true, false, 220, 0, 0, 0, true);
            _inventoryPortalWaitFill=Hud.Render.CreateBrush(62,200,15,15,0);
            _inventoryPortalWaitBorder=Hud.Render.CreateBrush(210,255,65,65,2);

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
            FreeHudInput.BeginMessageInputFrame(Hud);
            // HUD cursor coordinates settle on the following collection after our input.
            // Refresh during the existing ignore window, without extending that window.
            if (NowTick() < _cursorIgnoreUntilTick)
            {
                _cursorBaselineX = Hud.Window.CursorX;
                _cursorBaselineY = Hud.Window.CursorY;
            }
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

            if (WindowPaneInputActive()) ObserveMessageScroll();
            int upgrades = GetUpgradeAttempts();
            // Run-level portal clock survives gem changes. Never inject T during an owned
            // mouse drag or a pending key/mouse pulse; service it at the first free update.
            if (_initialUpgradeAttemptsThisRun != int.MinValue
                && _pendingInputKind == PendingInputKind.None
                && TryRequestTimedPortalDuringRun(NowTick()))
                return;
            // Keep the existing portal clock active when this count read fails.
            if (upgrades < 0) return;
            if (upgrades > 0) _tailWaitAfterFinalAttempt = false;
            if (_tailWaitAfterFinalAttempt)
            {
                ObserveFinalNativeAttempt(upgrades);
                ClearSoftRestartWait(true);
                return;
            }

            if (TryCloseChatBeforeGemPaneAutomation(upgrades))
                return;

            if (upgrades <= 0)
            {
                ObserveFinalNativeAttempt(upgrades);
                _tailWaitAfterFinalAttempt = true;
                ClearSoftRestartWait(true);
                return;
            }

            if (HandleSoftRestartWait())
                return;

            if (_autoRunning || _stage == AutomationStage.Running)
            { HandleRunningState(upgrades); return; }
            if (_stage == AutomationStage.Failed || _stage == AutomationStage.VerificationComplete) return;
            if (_target == null && !AcquireTarget()) return;
            if (FullListVerificationMode && !AutoUpgradeAfterFullListVerification)
            {
                CaptureNativeCells(); _stage=AutomationStage.VerificationComplete; return;
            }
            AdvanceNativeSelection();
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

        private bool PortalDueSoon(int withinMs)
        {
            if (_portalRequestedThisRun || _portalRequestPending || _portalRetryExhaustedThisRun
                || _initialUpgradeAttemptsThisRun == int.MinValue)
                return false;
            if (s7o_AutoGemUpgradeState.IsBelowConfiguredPortalAnchorAtRunStart(_initialUpgradeAttemptsThisRun))
                return true;
            return _portalAnchorClickTick != int.MinValue
                && ElapsedMs(_portalAnchorClickTick) + withinMs
                    >= s7o_AutoGemUpgradeState.GetFullPortalDelayMs();
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
            if (upgrades < 0) return;
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

            if (_nativeAttemptKey != null && upgrades > 0)
            {
                if (WaitForNativeAttempt(upgrades)) return;
                if (buttonAnim==27 && upgrades<_nativeAttemptCount)
                {
                    TryPrepositionForPlannedTarget(upgrades,"overlap");
                    if(TryPrepareConfirmedNativeSelection(upgrades))return;
                }
                if (upgrades < _nativeAttemptCount && !NativeAttemptResolved())
                {
                    // Navigation may overlap an unknown result only when both outcomes
                    // choose the same ACD. No mode can spend the next attempt while unknown.
                    if (buttonAnim != 27 && TryPrepareConfirmedNativeSelection(upgrades)) return;
                    if (_nativeConsumedTick != int.MinValue && buttonAnim == 27
                        && ElapsedMs(_nativeConsumedTick) >= 1600)
                    {
                        BeginCurrentTargetRecoveryFromRunning("upgrade UI did not settle; reacquiring");
                        return;
                    }

                    // Preserve the original consumption bookkeeping during animation;
                    // its 10ms retarget window must expire before readiness, not after it.
                    if (buttonAnim != 27) return;
                }
                if (WasLastUpgradeSuccessful())
                {
                    if (AutoPercentMode && TryHandleAutoSuccessNoReadyCapStop(upgrades)) return;
                    if (IsLowestBalanceMode() && TryHandleLowestSuccessNoReadyCapStop(upgrades)) return;
                    if (!AutoPercentMode && !IsLowestBalanceMode()
                        && TryHandlePersistentSuccessNoReadyCapStop(upgrades)) return;
                }
                if (_nativeConsumedTick != int.MinValue && buttonAnim == 27
                    && ElapsedMs(_nativeConsumedTick) >= 1600)
                {
                    BeginCurrentTargetRecoveryFromRunning("upgrade UI did not settle; reacquiring");
                    return;
                }
            }

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

                        // cap-retarget fires immediately on attempt-consumed, before
                        // any button-state gate.  A capped gem's upgrade button gets stuck at 27
                        // permanently (it has no "ready" state to return to), so any wait on
                        // buttonAnim != 27 will never fire for cap cases.  We decide cap-retarget
                        // from ground-truth signals only: attempt counter dropped + gem at cap.
                        // The retarget's downstream selection validation handles the stale-text
                        // window via item-button anim change detection (see ValidateLoaded... fix).
                        if (TryHandleAutoSuccessNoReadyCapStop(upgrades))
                            return;

                        // AUTO can commit to a different next gem only after the previous
                        // result was confirmed. Selection overlaps button cooldown; upgrading
                        // does not. Unresolved AUTO results keep the original observation gate.
                        if (WasLastUpgradeSuccessful())
                        {
                            int currentAbs = _target != null ? _target.AbsoluteIndex : -1;
                            if (currentAbs >= 0)
                            {
                                int confirmed = Math.Max(_autoValidationPreRank + 1,
                                    GetLiveEffectiveRank(_target.Source));
                                int prior;
                                if (!_autoConfirmedRankByAbs.TryGetValue(currentAbs, out prior) || confirmed > prior)
                                    _autoConfirmedRankByAbs[currentAbs] = confirmed;
                            }
                            GemTarget next;
                            if (TryGetPlannedAutoTarget(upgrades, out next, true) && next != null
                                && !SameGem(next, _target))
                            {
                                BeginPlannedRetarget(next);
                                return;
                            }
                        }
                        TryPrepositionForPlannedTarget(upgrades, "auto");
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
                    bool sameAsCurrent = havePlannedTarget && _target != null && SameGem(plannedTarget, _target);


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

                        // see AUTO
                        if (TryHandleLowestSuccessNoReadyCapStop(upgrades))
                            return;

                        if (TryPrepareConfirmedNativeSelection(upgrades)) return;
                        TryPrepositionForPlannedTarget(upgrades, "lowest");
                        return;
                    }

                    bool retargetUiUnlocked = buttonAnim != 27;
                    if (now < _lowestRetargetEarliestTick || !retargetUiUnlocked)
                        return;

                    bool succeeded = WasLastUpgradeSuccessful();
                    _lowestAwaitingResolution = false;
                    _lowestUpgradeClickStartUpgrades = int.MinValue;
                    _lowestAttemptResolvedTick = int.MinValue;
                    _lowestRetargetEarliestTick = int.MinValue;
                    _lowestValidationAcd = 0;
                    _lowestValidationPreRank = -1;

                    GemTarget plannedTarget;
                    if ((plannedTarget = GetLowestBalancePlannedTarget()) != null)
                    {
                        bool sameAsCurrent = _target != null && SameGem(plannedTarget, _target);
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


                        // see AUTO
                        if (TryHandlePersistentSuccessNoReadyCapStop(upgrades))
                            return;

                        if (WasLastUpgradeSuccessful())
                        {
                            GemTarget next;
                            string warning, failure;
                            if (TryChoosePersistentModeTarget(upgrades, out next, out warning,
                                out failure, true) && next != null && !SameGem(next, _target))
                            {
                                BeginPlannedRetarget(next);
                                return;
                            }
                        }
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

                    bool sameAsCurrent = _target != null && SameGem(desiredTarget, _target);
                    if (!sameAsCurrent)
                    {

                        BeginPlannedRetarget(desiredTarget);
                        return;
                    }
                }
            }

            if (_retargetNeedsButtonReady && buttonAnim == 27
                && _retargetStartedTick != int.MinValue && ElapsedMs(_retargetStartedTick) >= 1600)
            {
                BeginCurrentTargetRecoveryFromRunning("retarget upgrade UI did not unlock; reacquiring");
                _retargetStartedTick = now;
                return;
            }

            bool firstClickPending = !_hasSentInitialUpgradeClick;
            bool selectedReady = IsNativeTargetSelected(_target);
            if (selectedReady)
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
            bool priorAttemptConsumed = _nativeAttemptKey == null
                || upgrades < _nativeAttemptCount;
            bool canClickUpgrade = buttonVisible
                && (_nativeAttemptKey == null || NativeAttemptResolved())
                && priorAttemptConsumed
                && selectedReady
                && loaded
                && !lowestHold
                && !autoHold
                && ElapsedMs(_lastUpgradeClickTick) >= UpgradeClickDelayMs
                // Preserve the original first-click/recovery exceptions. Selected ACD and
                // attempt consumption are checked separately; animation alone cannot stall
                // a capped transition forever.
                && (firstClickPending || animReady || IgnoreUpgradeButtonAnimGate
                    || (recoveryReady && (!_retargetNeedsButtonReady || PreviousNativeGemAtCap())));

            if (canClickUpgrade)
            {
                var nativeGem = _target != null && _target.Source != null && _target.Source.Item != null
                    ? FindNativeGem(BuildItemKey(_target.Source.Item)) : null;
                if (nativeGem == null) return;
                int directPreRank = ReadDirectGemRank(nativeGem.Item);
                int nativePreRank = Math.Max(GetConfirmedLowRank(nativeGem.Item), directPreRank);
                string nativeKey = BuildItemKey(nativeGem.Item);
                int capResultBaseline = nativePreRank == 149 ? ReadCompletedUiResult() : 0;
                if (!ClickUi(_upgradeButton)) return;
                ResetNativeCapResult(capResultBaseline);
                _nativeOutcome = NativeAttemptOutcome.Pending;
                _nativeOutcomePrepared = false;
                _nativeAttemptKey = nativeKey;
                _nativeAttemptRank = nativePreRank;
                _nativeDirectBaseline = directPreRank;
                _nativeAttemptCount = upgrades;
                _nativeConsumedTick = int.MinValue;
                _nativeAttemptSucceeded = false;
                _lastUpgradeClickTick = now;
                _retargetNeedsButtonReady = false;
                _retargetStartedTick = int.MinValue;
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



        // Native identities and ranks drive selection; localized results can confirm a consumed attempt.
        private string _nativeAttemptKey;
        private int _nativeAttemptRank = -1, _nativeAttemptCount = -1;
        private int _nativeDirectBaseline = -1;
        private int _nativeCapResultBaseline, _nativeCapClearSamples;
        private int _nativeCapClearTick = int.MinValue;

        private int ReadDirectGemRank(IItem item)
        {
            if(item==null)return -1;
            try
            {
                int rank=item.GetAttributeValueAsInt(Hud.Sno.Attributes.Jewel_Rank, 2147483647u, -1);
                return rank>=0 && rank<=150 ? rank : -1;
            }
            catch { return -1; }
        }
        private static bool DirectResultReady(int before, int baseline, int current,
            bool selected, bool visible, int animation)
        {
            return before>=0 && before==baseline && current>=before && current<=150
                && selected && visible && animation==24;
        }
        private int _nativeConsumedTick = int.MinValue;
        private bool _nativeAttemptSucceeded;
        private readonly Dictionary<string, int> _confirmedLowRanks = new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool SameGem(GemTarget a, GemTarget b)
        {
            return a != null && b != null && a.Source != null && b.Source != null
                && a.Source.Item != null && b.Source.Item != null
                && BuildItemKey(a.Source.Item) == BuildItemKey(b.Source.Item);
        }

        private GemOrderEntry FindNativeGem(string key)
        {
            return key == null ? null : BuildOrderedGemEntries().FirstOrDefault(g =>
                g != null && g.Item != null && BuildItemKey(g.Item) == key);
        }





        private static string NormalizeCompletedResult(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var normalized = value.Normalize(NormalizationForm.FormKC);
            var result = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
                if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
            return result.ToString();
        }

        private int ReadCompletedUiResult()
        {
            bool readable, visible, blank;
            return ReadCompletedUiResultSnapshot(out readable, out visible, out blank);
        }

        private int ReadCompletedUiResultSnapshot(out bool readable, out bool visible, out bool blank)
        {
            readable = visible = blank = false;
            try
            {
                if(_gemStatusText==null)return 0;
                visible = _gemStatusText.Visible;
                string raw = _gemStatusText.ReadText(Encoding.UTF8,true);
                if (raw == null) return 0;
                string text=NormalizeCompletedResult(raw);
                readable = true; blank = text.Length == 0;
                if (!visible) return 0;
                // Exact completed-result labels; unknown translations retain native fallback.
                string[] success={"Upgrade Succeeded","업그레이드 성공","강화 성공","강화 성공했습니다","升级成功","升級成功","強化成功","Mejora realizada","Mejora completada","Mejora conseguida","Aprimoramento realizado","Aprimoramento bem-sucedido","Amélioration réussie","Aufwertung erfolgreich","Potenziamento riuscito","Улучшение успешно","Улучшение удалось","Ulepszenie udane"};
                string[] failure={"Upgrade Failed","업그레이드 실패","강화 실패","강화 실패했습니다","升级失败","升級失敗","強化失敗","Mejora fallida","Aprimoramento falhou","Falha no aprimoramento","Échec de l’amélioration","Échec de l'amélioration","Aufwertung fehlgeschlagen","Potenziamento fallito","Улучшение не удалось","Ulepszenie nieudane"};
                if(success.Any(x=>string.Equals(text,NormalizeCompletedResult(x),StringComparison.Ordinal)))return 1;
                if(failure.Any(x=>string.Equals(text,NormalizeCompletedResult(x),StringComparison.Ordinal)))return -1;
            }
            catch { }
            return 0;
        }

private void ResetNativeCapResult(int baseline)
        {
            _nativeCapResultBaseline = baseline;
            _nativeCapClearSamples = 0;
            _nativeCapClearTick = int.MinValue;
        }

        private bool ObserveFreshNativeCapResult(int upgrades, int animation, int result,
            bool readable, bool visible, bool blank, bool sameSelected)
        {
            if (_nativeAttemptRank != 149) return false;
            if (!sameSelected)
            {
                ResetNativeCapResult(0);
                return false;
            }
            if (!readable) return false;
            int tick = Hud.Game.CurrentGameTick;
            if (animation == 27 && blank && tick != _nativeCapClearTick)
            {
                _nativeCapClearTick = tick;
                _nativeCapClearSamples = Math.Min(2, _nativeCapClearSamples + 1);
            }
            // An unchanged old success cannot confirm a new attempt. Either the
            // recognized failure changed to success, or the result field cleared
            // on two distinct busy ticks and subsequently displayed a success.
            return upgrades < _nativeAttemptCount && animation == 27 && visible && result > 0
                && (_nativeCapResultBaseline < 0
                    || (_nativeCapClearSamples >= 2 && tick != _nativeCapClearTick));
        }

        private bool WaitForNativeAttempt(int upgrades)
        {
            if (_nativeAttemptKey == null || NativeAttemptResolved()) return false;
            int animation = SafeAnimState(_upgradeButton);
            bool resultReadable, resultVisible, resultBlank;
            int uiResult = ReadCompletedUiResultSnapshot(out resultReadable, out resultVisible, out resultBlank);
            bool sameSelected = _target != null && _target.Source != null && _target.Source.Item != null
                && BuildItemKey(_target.Source.Item) == _nativeAttemptKey && IsNativeTargetSelected(_target);
            bool freshCapResult = ObserveFreshNativeCapResult(upgrades, animation, uiResult,
                resultReadable, resultVisible, resultBlank, sameSelected);
            if (upgrades >= _nativeAttemptCount) return false;
            if (_nativeConsumedTick == int.MinValue)
            {
                _nativeConsumedTick = NowTick();
                _nativeOutcome = NativeAttemptOutcome.Consumed;

            }
            var gem = FindNativeGem(_nativeAttemptKey);
            int directRank = gem == null ? -1 : ReadDirectGemRank(gem.Item);
            int rank = gem == null ? -1 : Math.Max(gem.Item.JewelRank, directRank);

            if (rank > _nativeAttemptRank)
                ResolveNativeAttempt(true, rank);
            else if (freshCapResult && gem != null && gem.EffectiveStopCap == 150)
            {
                ResolveNativeAttempt(true, 150);
            }
            // Completion belongs to the consumed attempt's native ready cycle.
            // Consecutive failures/successes legitimately retain the same result label.
            else if (sameSelected && animation == 24 && SafeUiVisible(_upgradeButton) && uiResult != 0)
                ResolveNativeAttempt(uiResult > 0, uiResult > 0 ? _nativeAttemptRank + 1 : rank);
            else if (DirectResultReady(_nativeDirectBaseline, _nativeAttemptRank,
                directRank, sameSelected || _nativeOutcomePrepared, SafeUiVisible(_upgradeButton), animation))
                ResolveNativeAttempt(directRank > _nativeAttemptRank, directRank);
            if (NativeAttemptResolved()) return false;
            if (gem == null)
            {
                BeginCurrentTargetRecoveryFromRunning("native upgrade item unavailable; reacquiring");
                return true;
            }
            // Uncertain outcomes permit preparation, never an assumed failed upgrade.
            // The shared caller keeps the portal scheduler and native observations running.
            return false;
        }
        private enum NativeAttemptOutcome { None, Pending, Consumed, Succeeded, Failed }
        private NativeAttemptOutcome _nativeOutcome;
        private bool _nativeOutcomePrepared;
        private bool NativeAttemptResolved()
        {
            return _nativeOutcome == NativeAttemptOutcome.Succeeded || _nativeOutcome == NativeAttemptOutcome.Failed;
        }
        private void ResolveNativeAttempt(bool success, int rank)
        {
            _nativeAttemptSucceeded = success;
            _nativeOutcome = success ? NativeAttemptOutcome.Succeeded : NativeAttemptOutcome.Failed;
            if (success)
            {
                int previous;
                rank = Math.Max(rank, _nativeAttemptRank + 1);
                if (!_confirmedLowRanks.TryGetValue(_nativeAttemptKey, out previous) || rank > previous)
                    _confirmedLowRanks[_nativeAttemptKey] = rank;
            }

        }


        private void ObserveFinalNativeAttempt(int upgrades)
        {
            // The final attempt can finish while portal is casting. Never act here;
            // only retain native result evidence until the Urshi pane closes.
            if (_nativeAttemptKey == null || _nativeAttemptCount <= 0
                || upgrades >= _nativeAttemptCount) return;
            if (_nativeConsumedTick == int.MinValue)
            {
                _nativeConsumedTick = NowTick();
            }
            if (_nativeAttemptSucceeded) return;
            var gem = FindNativeGem(_nativeAttemptKey);
            int directRank = gem == null ? -1 : ReadDirectGemRank(gem.Item);
            int resultRank = gem == null ? -1 : Math.Max(gem.Item.JewelRank, directRank);
            if (gem != null && resultRank > _nativeAttemptRank)
            {
                ResolveNativeAttempt(true, resultRank);
            }
        }

        private bool WasLastUpgradeSuccessful()
        {
            return _nativeAttemptSucceeded && _target != null && _target.Source != null
                && _target.Source.Item != null
                && BuildItemKey(_target.Source.Item) == _nativeAttemptKey;
        }


        private int GetConfirmedLowRank(IItem item)
        {
            if(item==null)return -1;
            int rank=item.JewelRank,confirmed;
            string key=BuildItemKey(item);
            if(_confirmedLowRanks.TryGetValue(key,out confirmed))rank=Math.Max(rank,confirmed);
            // Only the read-only preparation planner sees the possible successful rank.
            // Actual gem selection runs with this flag off and uses confirmed results.
            if(_prepositionAssumeSuccess && key==_nativeAttemptKey)
                rank=Math.Max(rank,_nativeAttemptRank+1);
            return rank;
        }

        private GemTarget GetLowestBalancePlannedTarget()
        {
            _orderedGems.Clear();
            _orderedGems.AddRange(BuildOrderedGemEntries());
            var chosen = _orderedGems
                .Where(g => g != null && g.Item != null
                    && GetConfirmedLowRank(g.Item) < g.EffectiveStopCap)
                .OrderBy(g => GetConfirmedLowRank(g.Item))
                .ThenBy(g => g.AbsoluteIndex)
                .FirstOrDefault();
            if (chosen == null)
                chosen = _orderedGems.Where(g => g != null && g.Item != null
                        && g.CanAttemptAt150Fallback)
                    .OrderBy(g => g.AbsoluteIndex).FirstOrDefault();
            if (chosen == null) return null;
            return new GemTarget
            {
                Name = GetGemName(chosen.Item),
                Rank = GetConfirmedLowRank(chosen.Item),
                AbsoluteIndex = chosen.AbsoluteIndex,
                Reason = "lowest balance",
                Source = chosen,
            };
        }

        private void PreparePlannedRetarget(GemTarget plannedTarget)
        {
            ResetNativeNavigation();
            _nativeConfirmedPreparation=false;
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
            // (Finding A): re-seed target ACD from the planned gem's live inventory ACD
            // so the ACD-direct shortcut in DirectCaptureCurrentPage can fire on retargets, not just
            // on the first gem of a run.  Previous behavior (= 0) disabled the shortcut for every
            // retarget, forcing the stall-counter seek path even when the gem was already visible.
            _targetAcd = GetTargetSourceAcd(plannedTarget);
            ClearSelectedReadyLatch();
            ResetTargetRecoveryState();
            // This is still the same Urshi session. Do not re-anchor Town Portal,
            // forget its original attempt count, or make the next click a 'first' click.
            _retargetNeedsButtonReady = _initialUpgradeAttemptsThisRun != int.MinValue;
            _retargetStartedTick = NowTick();
        }

        private void BeginPlannedRetarget(GemTarget plannedTarget,bool confirmedPreparation=false)
        {
            if (plannedTarget == null)
                return;

            PreparePlannedRetarget(plannedTarget);
            _nativeConfirmedPreparation=confirmedPreparation;
            _stage = AutomationStage.Idle;
            // Dispatch the verified next target without an extra collection-frame delay.
            if(_pendingInputKind==PendingInputKind.None)AdvanceNativeSelection();
        }

        private void StartRunningFromConfirmedTarget()
        {
            if (!IsNativeTargetSelected(_target)) return;

            if (_target != null)
                LatchSelectedReady(_target, GetStableItemButtonAcd());

            bool preserveRunningState = _preserveRunningStateOnReacquire;
            _preserveRunningStateOnReacquire = false;
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
            _autoRunning = true;
            _stage = AutomationStage.Running;
            // Already active? This was internal navigation, not a new 4-attempt run.
            if (_initialUpgradeAttemptsThisRun != int.MinValue)
                return;

            if (preserveRunningState)
            {
                _firstUpgradeClickTick = int.MinValue;
                _hasSentInitialUpgradeClick = false;
                _noProgressAbortTick = int.MinValue;
                _lastRecoveryUpgradeAttempts = int.MinValue;

                return;
            }

            int upgrades = GetUpgradeAttempts();
            if (upgrades < 0) return;
            _lastObservedUpgradeAttempts = upgrades;
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
        // unrelated gems.  .
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
            ResetNativeNavigation();
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
                var forcedMatches = _orderedGems.Where(g => MatchesSpecificGem(g.Item, ForcedGemNameExact.Trim())).ToList();
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
                    int upgrades = GetUpgradeAttempts();
                    if (upgrades < 0) return false;
                    GemTarget plannedTarget;
                    if (TryGetPlannedAutoTarget(upgrades, out plannedTarget) && plannedTarget?.Source != null)
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
                    GemTarget plannedLowest = GetLowestBalancePlannedTarget();
                    if (plannedLowest != null && plannedLowest.Source != null)
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
                            ? "lowest balance"
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
            _paneWarningMessage = string.Empty;
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
    if (entry == null || entry.Item == null || (uint)entry.Item.AcdId != SafeItemButtonAcd()) return -1;
    var live = FindNativeGem(BuildItemKey(entry.Item));
    return live == null ? -1 : live.Item.JewelRank;
}


private int GetLiveEffectiveRank(GemOrderEntry entry)
{
    if (entry == null || entry.Item == null)
        return -1;

    int rank = GetConfirmedLowRank(entry.Item);
    int observedRank = GetObservedSelectedRankForCurrentTarget(entry);
    if (observedRank > rank)
        rank = observedRank;

    return rank;
}


private int GetPostSuccessAwareEffectiveRank(GemOrderEntry entry)
{
    return GetLiveEffectiveRank(entry);
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
    return GetLiveEffectiveRank(entry);
}


private uint SafeGemOrderEntryAcd(GemOrderEntry entry)
{
    try { return entry != null && entry.Item != null ? (uint)entry.Item.AcdId : 0u; }
    catch { return 0u; }
}

private GemOrderEntry ChooseFirstVisibleFallbackGem()
{
            var pool=_orderedGems.Where(g=>g!=null && g.Item!=null && g.CanAttemptAt150Fallback).OrderBy(g=>g.AbsoluteIndex).ToList();
            var viewport=NativeViewport();
            foreach(var cell in CaptureNativeCells().Where(c=>NativeVisible(c,viewport)).OrderBy(c=>c.Row).ThenBy(c=>c.Column))
            {
                var match=pool.FirstOrDefault(g=>SafeGemOrderEntryAcd(g)==cell.Acd);
                if(match!=null)return match;
            }
            return pool.FirstOrDefault();
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
    if (target == null || target.Source == null || target.Source.Item == null) return null;
    string key = BuildItemKey(target.Source.Item);
    return _orderedGems.FirstOrDefault(g => g != null && g.Item != null && BuildItemKey(g.Item) == key);
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
    int justCappedAbs = currentEntry.AbsoluteIndex;
    bool haveDesiredTarget = TryChoosePersistentModeTarget(upgrades, out desiredTarget, out modeWarning, out modeFailure, true, justCappedAbs);
    if (!haveDesiredTarget)
    {
        HandleNoEligibleTargetStop(modeWarning, modeFailure);
        return true;
    }

    bool sameAsCurrent = _target != null && SameGem(desiredTarget, _target);
    if (!sameAsCurrent)
    {
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
        bool sameAsCurrent = _target != null && SameGem(plannedTarget, _target);
        if (!sameAsCurrent)
        {
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



    _lowestAwaitingResolution = false;
    _lowestUpgradeClickStartUpgrades = int.MinValue;
    _lowestAttemptResolvedTick = int.MinValue;
    _lowestRetargetEarliestTick = int.MinValue;
    _lowestValidationAcd = 0;
    _lowestValidationPreRank = -1;

    GemTarget plannedTarget;
    if ((plannedTarget = GetLowestBalancePlannedTarget()) != null)
    {
        bool sameAsCurrent = _target != null && SameGem(plannedTarget, _target);
        if (!sameAsCurrent)
        {
            BeginPlannedRetarget(plannedTarget);
            return true;
        }
    }

    HandleNoEligibleTargetStop("No gems can be upgraded under current rules.\nChoose another mode or gem.", "no eligible target gem under current LOWEST rules");
    return true;
}

private GemOrderEntry ChooseFirstVisibleBurnGem()
{
            var pool=_orderedGems.Where(g=>g!=null && g.Item!=null && IsBurnEligible(g)).OrderBy(g=>g.AbsoluteIndex).ToList();
            var viewport=NativeViewport();
            foreach(var cell in CaptureNativeCells().Where(c=>NativeVisible(c,viewport)).OrderBy(c=>c.Row).ThenBy(c=>c.Column))
            {
                var match=pool.FirstOrDefault(g=>SafeGemOrderEntryAcd(g)==cell.Acd);
                if(match!=null)return match;
            }
            return pool.FirstOrDefault();
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
            .Where(g => g != null && g.Item != null && MatchesSpecificGem(g.Item, forcedName))
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
            var currentEntry = FindOrderedEntryForTarget(_target);
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
    _selectedReadyTick = int.MinValue;
}

private void ResetTargetRecoveryState()
{
    _runningUiLossCount = 0;
    _preserveRunningStateOnReacquire = false;
}

private void BeginCurrentTargetRecoveryFromRunning(string reason)
{
            ResetNativeNavigation();
    _autoRunning = false;
    _runningUiLossCount = 0;
    _preserveRunningStateOnReacquire = true;
    if (_initialUpgradeAttemptsThisRun != int.MinValue)
        _retargetNeedsButtonReady = true;
    else
    {
        _lastUpgradeClickTick = int.MinValue;
        _firstUpgradeClickTick = int.MinValue;
        _hasSentInitialUpgradeClick = false;
        _noProgressAbortTick = int.MinValue;
        _lastRecoveryUpgradeAttempts = int.MinValue;
    }
    _lastActionTick = NowTick();

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


        private bool IsNativeTargetSelected(GemTarget target)
{
    if (target == null || _itemButton == null || !_itemButton.Visible
        || SafeAnimState(_itemButton) == -1) return false;
    uint expected = GetTargetSourceAcd(target);
    uint actual = SafeItemButtonAcd();
    // Fresh native identity is mandatory before spending an attempt.
    return expected != 0 && expected != 0xFFFFFFFF && actual == expected;
}

private bool IsSelectedTargetReady(GemTarget target)
{
    return _upgradeButton != null && _upgradeButton.Visible && IsNativeTargetSelected(target);
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

        private bool ClickUi(IUiElement element)
        {
            if (element == null)
                return false;

            if (WindowPaneInputActive() && object.ReferenceEquals(element,_upgradeButton)
                && (_target==null || !IsNativeTargetSelected(_target))) return false;
            RectangleF rectangle;
            try { rectangle = element.Rectangle; }
            catch { return false; }

            int x = (int)Math.Round(rectangle.Left + rectangle.Width * 0.5f);
            int y = (int)Math.Round(rectangle.Top + rectangle.Height * 0.5f);
            return BeginMousePulseAt(x, y, InputPulseMs, ShouldKeepCursorAtAutomationActionPoint());
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
                return -1; // Unavailable is not a consumed final attempt.
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
            SetTownRewardState(TownRewardSpaceState.AwaitingTown, reason);
        }

        private void ResetTownRewardLifecycle(string reason)
        {
            _townRewardSessionStartTick = int.MinValue;
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
            if (item?.SnoItem == null) return string.Empty;
            string canonical;
            if (CanonicalGemNameBySno.TryGetValue(item.SnoItem.Sno, out canonical))
                return canonical;
            return item.SnoItem.NameEnglish ?? string.Empty;
        }

        private static bool MatchesSpecificGem(IItem item, string configuredName)
        {
            if (item?.SnoItem == null || string.IsNullOrWhiteSpace(configuredName)) return false;
            return string.Equals(GetGemName(item), configuredName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.SnoItem.NameLocalized, configuredName, StringComparison.OrdinalIgnoreCase);
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

            // Last resort only; rank changes after a successful upgrade, so it must not enter identity.
            return GetGemName(item) + "|" + item.Location.ToString() + "|"
                + item.InventoryX.ToString() + "|" + item.InventoryY.ToString() + "|" + item.Seed.ToString();
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
            if (_pendingInputKind != PendingInputKind.None || virtualKey == 0 || FreeHudInput.MessagePairNeedsHandoff)
                return false;

            if (!FreeHudInput.KeyDown(virtualKey))
                return false;

            _pendingInputKind = PendingInputKind.Key;
            _pendingReleaseRetries = 0;
            _pendingKey = virtualKey;
            _pendingInputReleaseTick = NowTick() + Math.Max(0, holdMs);
            _pendingInputReleaseSucceeded = true;
            return true;
        }

        private bool BeginMousePulseAt(int x, int y, int holdMs, bool keepCursor)
        {
            if (UseWindowMessageInput)
            {
                if (!WindowPaneInputActive() || _pendingInputKind != PendingInputKind.None || !NativeCellClickInsidePane(x,y)) return false;
                bool sent=FreeHudInput.MessageClickClient(Hud,x,y);

                return sent;
            }

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
            _pendingReleaseRetries = 0;
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
            _pendingReleaseRetries = 0;
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

            if (!released && _pendingReleaseRetries++ < 2)
            {
                // Retry on the next collection. Normal successful releases have no extra delay.
                _pendingInputReleaseTick = NowTick();
                return true;
            }
            if (_pendingInputKind == PendingInputKind.Mouse && _pendingRestoreCursor)
            {
                FreeHudInput.MouseMoveClient(Hud, _pendingRestoreCursorX, _pendingRestoreCursorY);
                ResetCursorWatchToCurrent(UserInterferenceIgnoreAfterPluginInputMs);
            }

            _pendingInputReleaseSucceeded = released;
            if (!released) Fail("native input release failed");
            _pendingReleaseRetries = 0;
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
            _pendingReleaseRetries = 0;
            _pendingKey = 0;
            _pendingInputReleaseTick = int.MinValue;
            _pendingRestoreCursor = false;
        }

        private bool ShouldKeepCursorAtAutomationActionPoint()
        {
            return _autoRunning || _target != null;
        }

        private bool TryHoverUpgradeButton(string modeTag, GemTarget plannedTarget)
        {
            if (UseWindowMessageInput) return false;
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
            if (UseWindowMessageInput) return;
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

        private GemTarget PlanNativeNextTarget(int upgrades)
        {
            GemTarget planned=null;
            if(AutoPercentMode)TryGetPlannedAutoTarget(upgrades,out planned,true);
            else if(IsLowestBalanceMode())planned=GetLowestBalancePlannedTarget();
            else
            {
                string warning,failure;
                TryChoosePersistentModeTarget(upgrades,out planned,out warning,out failure,true);
            }
            return planned;
        }

private bool TryPrepareConfirmedNativeSelection(int upgrades)
        {
            if (_pendingInputKind != PendingInputKind.None) return false;
            GemTarget planned;
            if (NativeAttemptResolved()) planned = PlanNativeNextTarget(upgrades);
            else
            {
                // A live pre-click attribute lets us settle this consumed attempt after
                // selection changes. Without it, keep selection until its result is known.
                if (_nativeDirectBaseline < 0 || _nativeDirectBaseline != _nativeAttemptRank) return false;
                var failed = PlanNativeOutcomeTarget(upgrades, false);
                var succeeded = PlanNativeOutcomeTarget(upgrades, true);
                if (!SameGem(failed, succeeded)) return false;
                planned = failed;
            }
            if (planned == null || SameGem(planned, _target)) return false;
            _nativeOutcomePrepared = !NativeAttemptResolved();

            BeginPlannedRetarget(planned, true);
            return true;
        }
        private GemTarget PlanNativeOutcomeTarget(int upgrades, bool success)
        {
            bool savedPrediction = _prepositionAssumeSuccess;
            var savedPlan = _autoPlan.ToArray();
            var savedOrder = _orderedGems.ToArray();
            string savedSummary = _autoPlanSummary;
            try
            {
                _prepositionAssumeSuccess = success;
                return PlanNativeNextTarget(upgrades);
            }
            finally
            {
                _prepositionAssumeSuccess = savedPrediction;
                _autoPlan.Clear(); _autoPlan.AddRange(savedPlan); _autoPlanSummary = savedSummary;
                _orderedGems.Clear(); _orderedGems.AddRange(savedOrder);
            }
        }

        private bool _nativeConfirmedPreparation,_nativeClickDuringBusy;
        private uint _preparedScrollTarget;
        private int _preparedScrollSent=int.MinValue,_preparedScrollCount,_preparedScrollNoProgress;
        private float _preparedScrollTop;
        private void PrepareNativeScroll(NativeGemCell cell,RectangleF viewport)
        {
            RectangleF stack;
            try { stack=_stackPanel.Rectangle; } catch { return; }
            if(!NativeRectValid(stack))return;
            // Reuse the live wheel engine with isolated speculative state. A failed
            // preparation never aborts the real selection or consumes its retry budget.
            int sent=_nativeWheelSentAt,aim=_nativeWheelAimTick,count=_nativeScrollCount;
            int noProgress=_nativeNoProgress,direction=_nativeSearchDirection;
            float top=_nativeWheelStackTop;
            try
            {
                _nativeWheelSentAt=_preparedScrollSent;_nativeWheelAimTick=_preparedWheelTick;
                _nativeScrollCount=_preparedScrollCount;_nativeNoProgress=_preparedScrollNoProgress;
                _nativeWheelStackTop=_preparedScrollTop;
                TryNativeScrollToCell(cell,viewport,stack,NowTick(),true);
                _preparedScrollSent=_nativeWheelSentAt;_preparedWheelTick=_nativeWheelAimTick;
                _preparedScrollCount=_nativeScrollCount;_preparedScrollNoProgress=_nativeNoProgress;
                _preparedScrollTop=_nativeWheelStackTop;
            }
            finally
            {
                _nativeWheelSentAt=sent;_nativeWheelAimTick=aim;_nativeScrollCount=count;
                _nativeNoProgress=noProgress;_nativeSearchDirection=direction;_nativeWheelStackTop=top;
            }
        }

        private bool _prepositionAssumeSuccess;
        private uint _preparedAcd;
        private RectangleF _preparedRect;
        private Size _preparedWindowSize;
        private Point _preparedWindowOffset;
        private int _preparedSince=int.MinValue,_preparedTick=int.MinValue,_preparedWheelTick=int.MinValue;

        private void TryPrepositionForPlannedTarget(int upgrades, string modeTag)
        {
            if(!ShouldKeepCursorAtAutomationActionPoint() || _pendingInputKind!=PendingInputKind.None
                || _nativeAttemptKey==null || upgrades>=_nativeAttemptCount
                || SafeAnimState(_upgradeButton)!=27)return;
            GemTarget planned=null;
            var savedPlan=_autoPlan.ToArray();
            string savedSummary=_autoPlanSummary;
            try
            {
                // Preparation may predict success, but never selects a gem or upgrades it.
                _prepositionAssumeSuccess=true;
                planned=PlanNativeNextTarget(upgrades);
            }
            finally
            {
                _prepositionAssumeSuccess=false;
                _autoPlan.Clear();_autoPlan.AddRange(savedPlan);_autoPlanSummary=savedSummary;
            }
            if(planned==null || SameGem(planned,_target))return;
            var viewport=NativeViewport();
            var cell=NativeFind(GetTargetSourceAcd(planned),CaptureNativeCells());
            if(cell==null || !NativeRectValid(viewport))return;
            if(_preparedScrollTarget!=cell.Acd || _preparedWindowSize!=Hud.Window.Size
                || _preparedWindowOffset!=Hud.Window.Offset)
            {
                _preparedScrollTarget=cell.Acd;_preparedScrollSent=int.MinValue;
                _preparedScrollCount=_preparedScrollNoProgress=0;
            }
            bool changed=_preparedAcd!=cell.Acd || _preparedWindowSize!=Hud.Window.Size
                || _preparedWindowOffset!=Hud.Window.Offset
                || !NativeRectSame(_preparedRect,cell.Rect);
            if(changed)
            {
                _preparedSince=NowTick();_preparedTick=Hud.Game.CurrentGameTick;
                _preparedWheelTick=int.MinValue;
            }
            _preparedAcd=cell.Acd;_preparedRect=cell.Rect;
            _preparedWindowSize=Hud.Window.Size;_preparedWindowOffset=Hud.Window.Offset;
            var area=NativeComfortable(cell,viewport)?RectangleF.Intersect(cell.Rect,viewport):viewport;
            int x=(int)Math.Round(area.Left+area.Width/2),y=(int)Math.Round(area.Top+area.Height/2);
            if(!UseWindowMessageInput && (Math.Abs(Hud.Window.CursorX-x)>2 || Math.Abs(Hud.Window.CursorY-y)>2))
            {
                if(!FreeHudInput.MouseMoveClient(Hud,x,y))return;
                MarkAutomationInputAction();
            }
            if(!NativeComfortable(cell,viewport) && _preparedWheelTick==int.MinValue)
                _preparedWheelTick=Hud.Game.CurrentGameTick;
            // Scrolling is reversible preparation; it does not select or upgrade a gem.
            if(!NativeComfortable(cell,viewport))PrepareNativeScroll(cell,viewport);
        }
        private static bool NativeRectSame(RectangleF a,RectangleF b)
        {
            return NativeRectValid(a) && NativeRectValid(b)
                && Math.Abs(a.X-b.X)<=0.75f && Math.Abs(a.Y-b.Y)<=0.75f
                && Math.Abs(a.Width-b.Width)<=0.75f && Math.Abs(a.Height-b.Height)<=0.75f;
        }
        private void RestorePreparedNativeTarget(uint expected)
        {
            if(_preparedAcd!=expected || _preparedWindowSize!=Hud.Window.Size
                || _preparedWindowOffset!=Hud.Window.Offset)return;
            _nativePreviousRect=_preparedRect;
            _nativeStableSince=_preparedSince;_nativeStableTick=_preparedTick;
            _nativeWheelAimTick=_preparedWheelTick;
        }

        private void ClearSoftRestartWait(bool clearWindow)
        {
            _softRestartPending = false;
            _softRestartBlockedUntilTick = int.MinValue;
            _userSettleUntilTick = int.MinValue;
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
            if (WindowPaneInputActive()) return false;
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
            int savedUpgradeClickTick = _lastUpgradeClickTick;
            bool savedRetargetNeedsReady = _retargetNeedsButtonReady;
            int savedRetargetStarted = _retargetStartedTick;
            var savedConfirmedLowRanks = new Dictionary<string, int>(_confirmedLowRanks, StringComparer.Ordinal);

            ResetState();
            foreach (var pair in savedConfirmedLowRanks) _confirmedLowRanks[pair.Key] = pair.Value;

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
                _lastUpgradeClickTick           = savedUpgradeClickTick;
                _retargetNeedsButtonReady       = savedRetargetNeedsReady;
                _retargetStartedTick            = savedRetargetStarted;

            }

            _softRestartPending = true;
            _softRestartBlockedUntilTick = now + Math.Max(0, SoftRestartBackoffMs);
            _userSettleUntilTick = now + Math.Max(120, UserInterferenceSettleDelayMs);
            _cursorBaselineX = curX;
            _cursorBaselineY = curY;
            _cursorIgnoreUntilTick = now + Math.Max(80, UserInterferenceIgnoreAfterPluginInputMs);
        }

        private void Fail(string reason)
        {
            _stage = AutomationStage.Failed;
            _autoRunning = false;
        }

        private void ResetState()
        {
            ResetMessageScroll(false);
            _preparedAcd=0;_prepositionAssumeSuccess=false;
            _nativeConfirmedPreparation=false;_preparedScrollTarget=0;
            _preparedScrollSent=int.MinValue;_preparedScrollCount=_preparedScrollNoProgress=0;
            _preparedSince=_preparedTick=_preparedWheelTick=int.MinValue;
            // A confirmed offset survives another Urshi opening in the same window geometry.
            // RefreshNativeCalibrationGeometry invalidates it on client, DPI or viewport changes.
            _nativeCellOffsetX=_nativeConfirmedOffsetX;
            _nativeCalibrationPending=false;
            ResetNativeNavigation();
            _stage = AutomationStage.Idle;
            _lastActionTick = int.MinValue;
            _lastUpgradeClickTick = int.MinValue;
            _portalAnchorClickTick = int.MinValue;
            _lastObservedUpgradeAttempts = int.MinValue;
            _lastUpgradeProgressTick = int.MinValue;
            _lastPortalActionTick = int.MinValue;
            _portalRequestedTick = int.MinValue;
            _lastRecoveryUpgradeAttempts = int.MinValue;
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
            _nativeAttemptKey = null;
            _nativeOutcome = NativeAttemptOutcome.None;
            _nativeOutcomePrepared = false;
            _nativeAttemptSucceeded = false;
            _nativeConsumedTick = int.MinValue;
            ResetNativeCapResult(0);
            _confirmedLowRanks.Clear();
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
            _lowestAwaitingResolution = false;
            _lowestUpgradeClickStartUpgrades = int.MinValue;
            _lowestAttemptResolvedTick = int.MinValue;
            _lowestRetargetEarliestTick = int.MinValue;
            _lowestValidationAcd = 0;
            _lowestValidationPreRank = -1;

            _orderedGems.Clear();
            _targetAcd = 0;
            _latchedItemButtonAcd = 0;
            _latchedItemButtonAcdTick = int.MinValue;
            _selectedReadyLatchedAcd = 0;
            _selectedReadyTick = int.MinValue;
            _runningUiLossCount = 0;
            _preserveRunningStateOnReacquire = false;

            _cursorIgnoreUntilTick = int.MinValue;
            _lastGemPaneChatCloseTick = int.MinValue;
            ClearChatCloseFadeWait();
            _tailWaitAfterFinalAttempt = false;
            _retargetNeedsButtonReady = false;
            _retargetStartedTick = int.MinValue;
        }

        private IBrush _inventoryPortalWaitFill,_inventoryPortalWaitBorder;

private void PaintInventoryPortalWait(ClipState clipState)
        {
            // Visual only: the first portal command can be rejected, so wait for native confirmation.
            if(clipState!=ClipState.AfterClip || !AutoStartEnabled || !Hud.Game.IsInGame
                || Hud.Game.IsInTown || Hud.Game.IsLoading || Hud.Game.IsPaused || !Hud.Window.IsForeground
                || !SafeUiVisible(_gemUpgradePane) || _portalRequestedThisRun || _portalRetryExhaustedThisRun
                || Hud.Game.Me==null || Hud.Game.Me.AnimationState==AcdAnimationState.CastingPortal
                || _stage==AutomationStage.Failed || _stage==AutomationStage.VerificationComplete
                || _inventoryPortalWaitFill==null || _inventoryPortalWaitBorder==null)return;
            try {
                var inventory=Hud.Inventory.InventoryItemsUiElement;
                if(!SafeUiVisible(inventory))return;
                var size=Hud.Window.Size;
                var rect=RectangleF.Intersect(inventory.Rectangle,new RectangleF(0,0,size.Width,size.Height));
                if(!NativeRectValid(rect))return;
                _inventoryPortalWaitFill.DrawRectangle(rect);
                _inventoryPortalWaitBorder.DrawRectangle(rect);
            }catch { /* A transient native UI read cannot interrupt upgrading. */ }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (_gemUpgradePane?.Visible != true)
                return;

            PaintInventoryPortalWait(clipState);
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

        // Native Urshi navigation: identities and rectangles are read together each capture.
        private sealed class NativeGemCell
        {
            public IUiElement Ui; public uint Acd; public RectangleF Rect; public int Row, Column;
        }
        private readonly List<IUiElement> _nativeUiCells = new List<IUiElement>();
        private readonly Dictionary<string, Tuple<int,int>> _nativeUiSlots = new Dictionary<string, Tuple<int,int>>();
        private List<NativeGemCell> _nativeCells = new List<NativeGemCell>();
        [DllImport("user32.dll", EntryPoint="GetAsyncKeyState")]
        private static extern short NativeMouseState(int key);
        private int _nativeWheelSentAt=int.MinValue,_nativeWheelAimTick=int.MinValue;
        private float _nativeWheelStackTop,_nativeCellOffsetX;
        private bool _nativeCalibrationTried, _nativeCalibrationPending, _nativeGeometryKnown;
        // One optional native-cell probe resolves a clipped edge target without guessing its offset.
        private uint _nativeProbeAcd;
        private bool _nativeProbeUsed;
        private const int NativeSelectionPollMs = 32;
        private const int NativeSelectionMaxClicks = 36;
        private const int NativeCapFirstSelectionMs = 420;
        private float _nativeConfirmedOffsetX;
        private Size _nativeCalibrationSize;
        private Point _nativeCalibrationOffset;
        private RectangleF _nativeCalibrationViewport;
        private uint _nativeCalibrationDpi;
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        private uint NativeWindowDpi()
        { try { return GetDpiForWindow(GetForegroundWindow()); } catch { return 0; } }
        private void RollbackNativeCalibration()
        {
            if (!_nativeCalibrationPending) return;
            _nativeCellOffsetX = _nativeConfirmedOffsetX;
            _nativeCalibrationPending = false;
            // Preserve the per-target attempt budget after rollback. ResetNativeNavigation
            // rearms it for a new target; this target retries verified geometry.
            
        }
        private void RefreshNativeCalibrationGeometry()
        {
            var viewport = NativeViewport();
            var size = Hud.Window.Size; var offset = Hud.Window.Offset;
            uint dpi = NativeWindowDpi();
            if (_nativeGeometryKnown && (size != _nativeCalibrationSize || offset != _nativeCalibrationOffset
                || dpi != _nativeCalibrationDpi || viewport != _nativeCalibrationViewport))
            {
                ResetMessageScroll(true);
                _nativeCalibrationPending = false;
                _nativeCellOffsetX = _nativeConfirmedOffsetX = 0;
                ResetNativeNavigation();
                
            }
            _nativeGeometryKnown = true;
            _nativeCalibrationSize = size; _nativeCalibrationOffset = offset;
            _nativeCalibrationViewport = viewport; _nativeCalibrationDpi = dpi;
        }
        private uint _nativeSelectionBeforeClick;

        private bool PreviousNativeGemAtCap()
        {
            var gem=FindNativeGem(_nativeAttemptKey);
            if (gem == null || gem.Item == null) return false;
            int rank = gem.Item.JewelRank, confirmed;
            if (_confirmedLowRanks.TryGetValue(BuildItemKey(gem.Item), out confirmed)) rank = Math.Max(rank, confirmed);
            return rank >= gem.EffectiveStopCap;
        }
        private bool NativeCellClickInsidePane(int x,int y)
        {
            try {
                var pane=_gemUpgradePane.Rectangle;
                return NativeRectValid(pane) && x>=pane.Left && x<pane.Right
                    && y>=pane.Top && y<pane.Bottom;
            } catch { return false; }
        }
        private bool TryCalibrateNativeCell(NativeGemCell expected,List<NativeGemCell> cells)
        {
            if (_nativeCalibrationTried || expected == null) return false;
            uint selected = SafeItemButtonAcd();
            // A capped neighbor can leave the Upgrade button permanently in state 27.
            // Selection ACD, not the Upgrade animation, authorizes a tentative correction.
            if (!NativeValidAcd(selected) || selected == expected.Acd) return false;
            // An unchanged selection cannot establish an offset after a busy click.
            if (_nativeClickDuringBusy && selected == _nativeSelectionBeforeClick) return false;
            var actual=NativeFind(selected,cells);
            if(actual==null || actual.Row!=expected.Row || Math.Abs(actual.Column-expected.Column)!=1
                || Math.Abs(actual.Rect.Width-expected.Rect.Width)>1
                || Math.Abs(actual.Rect.Top-expected.Rect.Top)>1)return false;
            float correction=expected.Rect.Left-actual.Rect.Left;
            float proposed=_nativeCellOffsetX+correction;
            // One adjacent-cell correction per target, validated by the resulting selected ACD.
            // A tentative correction is rolled back unless the intended ACD is selected.
            if(Math.Abs(proposed)>expected.Rect.Width*2)return false;
            int x=(int)Math.Round(expected.Rect.Left+expected.Rect.Width/2+proposed);
            int y=(int)Math.Round(expected.Rect.Top+expected.Rect.Height/2);
            if(!NativeCellClickInsidePane(x,y))return false;
            _nativeCalibrationTried=true;
            _nativeCalibrationPending=true;
            _nativeCellOffsetX=proposed;
            
            return true;
        }

        private bool CanProbeUnchangedNativeCapSelection()
        {
            uint selected = SafeItemButtonAcd();
            // Retry the verified coordinates before probing a possible column offset.
            return _nativeClickDuringBusy && _nativeTotalClicks >= 2 && PreviousNativeGemAtCap()
                && NativeValidAcd(selected) && selected == _nativeSelectionBeforeClick;
        }

        private bool TryProbeNativeEdgeCell(NativeGemCell target, List<NativeGemCell> cells,
            RectangleF viewport, int now)
        {
            if (_nativeProbeUsed || _nativeCalibrationPending || target == null
                || (target.Column != UrshiCols - 1 && !CanProbeUnchangedNativeCapSelection())
                || Math.Abs(_nativeCellOffsetX) > 0.5f)
                return false;
            var probe = cells.FirstOrDefault(c => c.Row == target.Row
                && c.Column == target.Column - 1 && NativeComfortable(c, viewport));
            if (probe == null && CanProbeUnchangedNativeCapSelection())
                probe = cells.FirstOrDefault(c => c.Row == target.Row
                    && c.Column == target.Column + 1 && NativeComfortable(c, viewport));
            if (probe == null || !NativeValidAcd(probe.Acd)) return false;
            int x = (int)Math.Round(probe.Rect.Left + probe.Rect.Width / 2);
            int y = (int)Math.Round(probe.Rect.Top + probe.Rect.Height / 2);
            if (!NativeCellClickInsidePane(x, y) || (NativeMouseState(0x01) & 0x8000) != 0)
                return false;
            _nativeSelectionBeforeClick = SafeItemButtonAcd();
            if (!BeginMousePulseAt(x, y, 0, ShouldKeepCursorAtAutomationActionPoint())) return false;
            _nativeProbeUsed = true;
            _nativeProbeAcd = probe.Acd;
            _nativeClickDuringBusy = SafeAnimState(_upgradeButton) == 27;
            _nativeClickAt = now;
            _nativeClickCount++; _nativeTotalClicks++;
            return true;
        }

        private int _nativeMissingSince=int.MinValue, _nativeTargetMissingSince=int.MinValue;
        private int _nativeTargetMissingTick=int.MinValue;
        private uint _nativeTarget;
        private int _nativeStarted = int.MinValue;
        private int _nativeStableSince = int.MinValue, _nativeStableTick = int.MinValue;
        private int _nativeClickAt = int.MinValue, _nativeClickCount, _nativeScrollCount, _nativeTotalClicks;
        private int _nativeRegisteredRows, _nativeNoProgress, _nativeSearchDirection = -1;
        private RectangleF _nativePreviousRect;
        private Size _nativeWindowSize; private Point _nativeWindowOffset;

        private void RegisterNativeRows(int rows)
        {
            const string path = "Root.NormalLayer.vendor_dialog_mainPage.riftReward_dialog.LayoutRoot.gemUpgradePane.items_list._content._stackpanel";
            rows = Math.Min(256, Math.Max(24, rows));
            for (int row = _nativeRegisteredRows; row < rows; row++)
                for (int col = 0; col < UrshiCols; col++)
                {
                    string p = path + "._tilerow" + row + "._item" + (row * 6 + col) + ".Item";
                    _nativeUiCells.Add(Hud.Render.RegisterUiElement(p, null, null));
                    _nativeUiSlots[p] = Tuple.Create(row, col);
                }
            _nativeRegisteredRows = rows;
        }
        private void ResetNativeNavigation()
        {
            RollbackNativeCalibration();
            _nativeWheelSentAt=_nativeWheelAimTick=int.MinValue;
            _nativeCalibrationTried=false;_nativeClickDuringBusy=false;
            _nativeProbeAcd=0;_nativeProbeUsed=false;
            _nativeSelectionBeforeClick=0;
            _nativeMissingSince=_nativeTargetMissingSince=_nativeTargetMissingTick=int.MinValue;
            _nativeTarget = 0; _nativeStarted = int.MinValue;
            _nativeStableSince = _nativeStableTick = int.MinValue;
            _nativeClickAt = int.MinValue; _nativeClickCount = _nativeScrollCount = _nativeNoProgress = _nativeTotalClicks = 0;
            _nativePreviousRect = RectangleF.Empty;
            _nativeSearchDirection = -1;
            _nativeCells.Clear();
        }
        // One bounded, pane-only scroll transaction for selection and preparation.
        private int _messageScrollSentAt=int.MinValue, _messageScrollSentTick=int.MinValue;
        private int _messageScrollObservedTick=int.MinValue, _messageScrollObservations;
        private int _messageScrollClicks, _messageScrollFailures, _messageScrollDeferredAnim=int.MinValue;
        private int _messageScrollLastAnim=int.MinValue;
        private float _messageScrollTop, _messageScrollPlayerX, _messageScrollPlayerY;
        private float _messageScrollQuantum, _messageScrollWantedThumb;
        private bool _messageScrollDown, _messageScrollInterrupted;
        private uint _messageScrollTarget;
        private bool WindowPaneInputActive()
        { return UseWindowMessageInput && SafeUiVisible(_gemUpgradePane); }
        private void ResetMessageScroll(bool geometryChanged)
        {
            _messageScrollSentAt=_messageScrollSentTick=_messageScrollObservedTick=int.MinValue;
            _messageScrollDeferredAnim=int.MinValue;
            _messageScrollLastAnim=int.MinValue;
            _messageScrollObservations=_messageScrollFailures=_messageScrollClicks=0;
            _messageScrollInterrupted=false;_messageScrollTarget=0;
            if(geometryChanged)_messageScrollQuantum=0;
        }
        private RectangleF MessageControlRect(IUiElement ui)
        {
            try {
                if(ui==null || !ui.Visible || !NativeRectValid(ui.Rectangle))return RectangleF.Empty;
                return _gemUpgradePane.Rectangle.Contains(ui.Rectangle)?ui.Rectangle:RectangleF.Empty;
            } catch { return RectangleF.Empty; }
        }
        private static int NativeScrollBurst(float distance,float quantum)
        {
            if(float.IsNaN(distance)||float.IsInfinity(distance)||quantum<1)return 1;
            return Math.Max(1,Math.Min(24,(int)Math.Ceiling(Math.Max(0,distance)/quantum)));
        }
        private float NativeScrollPitch(NativeGemCell cell)
        {
            float pitch=float.MaxValue;
            if(cell!=null)foreach(var other in _nativeCells) {
                int rows=Math.Abs(other.Row-cell.Row);
                if(rows==0)continue;
                float value=Math.Abs(other.Rect.Top-cell.Rect.Top)/rows;
                if(value>=cell.Rect.Height && value<pitch)pitch=value;
            }
            if(pitch!=float.MaxValue)return pitch;
            return cell!=null?Math.Max(1,cell.Rect.Height):Math.Max(1,NativeViewport().Height/3);
        }
        private bool MessageInventoryPointerActive()
        {
            try {
                var grid=Hud.Inventory.InventoryItemsUiElement;
                return grid!=null && grid.Visible && grid.Rectangle.Contains(Hud.Window.CursorX,Hud.Window.CursorY);
            } catch { return false; }
        }

private bool NativeCapSelectionWindowElapsed()
        {
            // The cap lockout belongs to the consumed attempt, not to its later result.
            // Retarget time remains the fallback when consumption was not observed.
            int start = _nativeConsumedTick != int.MinValue ? _nativeConsumedTick : _retargetStartedTick;
            return start != int.MinValue && ElapsedMs(start) >= NativeCapFirstSelectionMs;
        }

        private bool CanRetryNativeCapScroll(int anim)
        {
            // A capped gem can remain at animation 27 until a different gem is selected.
            // Reuse the existing cap lockout and navigation budget instead of waiting for ready.
            return anim == 27 && _retargetNeedsButtonReady
                && NativeCapSelectionWindowElapsed()
                && NativeAttemptResolved() && _nativeAttemptSucceeded && PreviousNativeGemAtCap();
        }

        private void ObserveMessageScroll()
        {
            // Observe readiness even with no queued scroll, so a later busy
            // phase cannot inherit a rejection from the previous animation cycle.
            if(_messageScrollDeferredAnim!=int.MinValue
                && SafeAnimState(_upgradeButton)!=_messageScrollDeferredAnim)
                _messageScrollDeferredAnim=int.MinValue;
            int tick=Hud.Game.CurrentGameTick;
            if(_messageScrollSentAt==int.MinValue || tick==_messageScrollSentTick)return;
            RectangleF stack;try { stack=_stackPanel.Rectangle; }catch{return;}
            if(!NativeRectValid(stack))return;
            if(_messageScrollObservedTick!=tick){_messageScrollObservedTick=tick;_messageScrollObservations++;}
            float delta=stack.Top-_messageScrollTop;
            bool moved=Math.Abs(delta)>=0.75f;
            bool playerMoved=Math.Abs(Hud.Game.Me.FloorCoordinate.X-_messageScrollPlayerX)
                +Math.Abs(Hud.Game.Me.FloorCoordinate.Y-_messageScrollPlayerY)>2;
            if(!moved && _messageScrollObservations<2)return;
            int anim=SafeAnimState(_upgradeButton);

            if(moved && ((_messageScrollDown&&delta<0)||(!_messageScrollDown&&delta>0))) {
                // A burst may stop when it reaches the thumb; never infer a smaller step from that partial burst.
                if(_messageScrollClicks==1 && Math.Abs(delta)>=_messageScrollQuantum*0.75f)_messageScrollQuantum=Math.Abs(delta);
                _messageScrollFailures=0;_messageScrollDeferredAnim=int.MinValue;
            } else if(playerMoved || FreeHudInput.MessageMouseHeld()) {
                _messageScrollInterrupted=true;_messageScrollFailures=0;
                // Releasing the mouse does not unlock an unresponsive upgrade UI.
                if(anim!=24)_messageScrollDeferredAnim=anim;
                _nativeScrollCount=Math.Max(0,_nativeScrollCount-1);
            } else if(anim!=24) {
                // Ordinary busy probes wait for readiness; confirmed cap retries consume the navigation budget.
                _messageScrollDeferredAnim=anim;
                if(!CanRetryNativeCapScroll(anim))
                    _nativeScrollCount=Math.Max(0,_nativeScrollCount-1);
            } else {
                _messageScrollFailures++;
            }

            _messageScrollSentAt=int.MinValue;
        }

private bool TryMessageScrollToCell(NativeGemCell cell,RectangleF viewport,RectangleF stack,int now,bool preparation)
        {
            viewport=NativeViewportForCell(cell,viewport);
            if(cell!=null && NativeRectValid(viewport)
                && Math.Min(cell.Rect.Right,viewport.Right)-Math.Max(cell.Rect.Left,viewport.Left)<cell.Rect.Width*0.80f)
            {if(!preparation)Fail("native gem horizontally clipped");return true;}
            if(PortalDueSoon(0)||_pendingInputKind!=PendingInputKind.None)return false;
            if(FreeHudInput.MessageMouseHeld())return false;
            ObserveMessageScroll();
            if(_messageScrollSentAt!=int.MinValue)return true;
            uint target=cell==null?0:cell.Acd;
            if(target!=_messageScrollTarget){_messageScrollFailures=0;_messageScrollTarget=target;}
            if(_messageScrollInterrupted) {
                if(Hud.Game.Me.AnimationState==AcdAnimationState.Running)return false;
                _messageScrollInterrupted=false;
            }
            int anim=SafeAnimState(_upgradeButton);
            // Initial pane animation 27 can scroll. During a real outstanding
            // upgrade, avoid speculative track clicks across inventory handling.
            // Selection, result reads, ready upgrade clicks and portal input continue.
            if(anim==27 && _nativeAttemptKey!=null && !NativeAttemptResolved() && MessageInventoryPointerActive())
                return true;
            if(anim!=_messageScrollLastAnim){_messageScrollFailures=0;_messageScrollLastAnim=anim;}
            if(_messageScrollDeferredAnim!=int.MinValue) {
                if(anim==_messageScrollDeferredAnim && !CanRetryNativeCapScroll(anim))return true;
                _messageScrollDeferredAnim=int.MinValue;_messageScrollFailures=0;
            }
            if(_messageScrollFailures>=3)
            {if(!preparation)Fail("native scrollbar made no progress while ready");return true;}
            if(_nativeScrollCount>=Math.Max(4,MaxDownScrollClicks))
            {if(!preparation)Fail("native scroll search limit");return true;}
            var bar=MessageControlRect(_scrollBar);var thumb=MessageControlRect(_scrollThumb);
            var up=MessageControlRect(_scrollUp);var downControl=MessageControlRect(_scrollDown);
            if(!NativeRectValid(bar)||!NativeRectValid(thumb)||thumb.Right<=bar.Left||thumb.Left>=bar.Right)
            {if(!preparation)Fail("native scrollbar geometry unavailable");return true;}
            float top=NativeRectValid(up)&&bar.Contains(up)?up.Bottom:bar.Top;
            float bottom=NativeRectValid(downControl)&&bar.Contains(downControl)?downControl.Top:bar.Bottom;
            float travel=bottom-top-thumb.Height, span=stack.Height-viewport.Height;
            if(travel<=1||span<=1)return true;
            bool down=cell!=null?cell.Rect.Bottom>viewport.Bottom:_nativeSearchDirection>0;
            if(cell==null&&!down&&stack.Top>=viewport.Top-1){_nativeSearchDirection=1;down=true;}
            float distance=cell==null?viewport.Height:down?cell.Rect.Bottom-viewport.Bottom+4:viewport.Top-cell.Rect.Top+4;
            float desired=cell!=null?viewport.Top-stack.Top+cell.Rect.Top+cell.Rect.Height/2
                -(viewport.Top+viewport.Height/2):down?span:0;
            float fraction=Math.Max(0,Math.Min(1,desired/span));
            _messageScrollWantedThumb=top+fraction*travel+thumb.Height/2;
            int x=(int)Math.Round(bar.Left+bar.Width/2),y=(int)Math.Round(_messageScrollWantedThumb);
            // If the desired center is already under the thumb, page on the required side; never drag or use wheel input.
            if(thumb.Contains(x,y))y=(int)Math.Round(down?Math.Min(bottom-1,thumb.Bottom+2):Math.Max(top+1,thumb.Top-2));
            float quantum=_messageScrollQuantum>0?_messageScrollQuantum:NativeScrollPitch(cell);
            _messageScrollQuantum=quantum;
            int requested=NativeScrollBurst(distance,quantum),queued=0;
            long scrollBurstBudget=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<requested;i++) {
                if(i>0 && (System.Diagnostics.Stopwatch.GetTimestamp()-scrollBurstBudget)*1000.0/System.Diagnostics.Stopwatch.Frequency>=5)break;
                if(!FreeHudInput.MessageClickClient(Hud,x,y))break;
                queued++;
            }
            if(queued==0)return false;
            _messageScrollClicks=queued;_messageScrollDown=down;_messageScrollTop=stack.Top;
            _messageScrollPlayerX=Hud.Game.Me.FloorCoordinate.X;_messageScrollPlayerY=Hud.Game.Me.FloorCoordinate.Y;
            _messageScrollSentAt=now;_messageScrollSentTick=Hud.Game.CurrentGameTick;
            _messageScrollObservedTick=int.MinValue;_messageScrollObservations=0;_nativeScrollCount++;

            return true;
        }

        private static bool NativeValidAcd(uint acd) { return acd != 0u && acd != uint.MaxValue; }
        private static bool NativeRectValid(RectangleF r)
        {
            return !float.IsNaN(r.X) && !float.IsNaN(r.Y) && !float.IsInfinity(r.X) && !float.IsInfinity(r.Y)
                && r.Width > 1 && r.Height > 1 && !float.IsInfinity(r.Width) && !float.IsInfinity(r.Height);
        }
        private RectangleF NativeViewport()
        {
            try
            {
                var ui = _itemsContent != null && _itemsContent.Visible ? _itemsContent : _itemsList;
                return ui != null && ui.Visible && NativeRectValid(ui.Rectangle) ? ui.Rectangle : RectangleF.Empty;
            }
            catch { return RectangleF.Empty; }
        }
        private List<NativeGemCell> CaptureNativeCells()
        {
            RegisterNativeRows((_orderedGems.Count + UrshiCols - 1) / UrshiCols + 4);
            var cells = new List<NativeGemCell>();
            foreach (var ui in _nativeUiCells)
            {
                try
                {
                    if (ui == null || !ui.Visible) continue;
                    uint acd = ui.LegendaryGemAcdId; var rect = ui.Rectangle;
                    if (!NativeValidAcd(acd) || !NativeRectValid(rect)) continue;
                    var slot = _nativeUiSlots[ui.Path];
                    cells.Add(new NativeGemCell { Ui=ui, Acd=acd, Rect=rect, Row=slot.Item1, Column=slot.Item2 });
                }
                catch { }
            }
            _nativeCells = cells;
            return cells;
        }
        private RectangleF NativeViewportForCell(NativeGemCell cell, RectangleF viewport)
        {
            if (cell == null || !NativeRectValid(viewport)) return viewport;
            if (RectangleF.Intersect(cell.Rect, viewport).Width >= cell.Rect.Width * 0.80f) return viewport;
            try
            {
                if (_itemsList == null || !_itemsList.Visible || _gemUpgradePane == null || !_gemUpgradePane.Visible) return viewport;
                var parent = RectangleF.Intersect(_itemsList.Rectangle, _gemUpgradePane.Rectangle);
                // Only use a live enclosing parent, never an inferred column or scroll position.
                if (NativeRectValid(parent) && parent.Left <= viewport.Left && parent.Right >= viewport.Right
                    && parent.Top <= viewport.Top && parent.Bottom >= viewport.Bottom
                    && cell.Rect.Left >= parent.Left && cell.Rect.Right <= parent.Right)
                {
                    
                    return RectangleF.FromLTRB(parent.Left, viewport.Top, parent.Right, viewport.Bottom);
                }
            } catch { }
            return viewport;
        }
        private static bool NativeComfortable(NativeGemCell cell,RectangleF viewport)
        {
            if(cell==null || !NativeRectValid(viewport) || !NativeRectValid(cell.Rect))return false;
            var visible=RectangleF.Intersect(cell.Rect,viewport);
            // The clipped center must retain at least three pixels of inset.
            return visible.Height>=Math.Max(6,cell.Rect.Height*0.55f)
                && visible.Width>=Math.Max(6,cell.Rect.Width*0.80f);
        }
        private static bool NativeVisible(NativeGemCell cell, RectangleF viewport)
        {
            if (cell == null || !NativeRectValid(viewport)) return false;
            var overlap = RectangleF.Intersect(cell.Rect, viewport);
            return overlap.Width > 2 && overlap.Height > 2
                && overlap.Width * overlap.Height >= cell.Rect.Width * cell.Rect.Height * 0.55f;
        }
        private NativeGemCell NativeFind(uint acd, List<NativeGemCell> cells)
        {
            var matches = cells.Where(c => c.Acd == acd).ToList();
            // Duplicate identities are ambiguous; never choose an arbitrary rectangle.
            if (matches.Count != 1) return null;
            var cell = matches[0];
            return cells.Any(c => c.Acd != acd && Math.Abs(c.Rect.X-cell.Rect.X)<1
                && Math.Abs(c.Rect.Y-cell.Rect.Y)<1) ? null : cell;
        }
        private void AdvanceNativeSelection()
        {
            if (_target == null) return;
            RefreshNativeCalibrationGeometry();
            int now=NowTick(); uint expected=GetTargetSourceAcd(_target);
            if (!NativeValidAcd(expected)) { Fail("native target identity unavailable"); return; }
            if (_nativeTarget != expected)
            {
                ResetNativeNavigation(); _nativeTarget=expected; _nativeStarted=now;
                _nativeWindowSize=Hud.Window.Size; _nativeWindowOffset=Hud.Window.Offset;
                RestorePreparedNativeTarget(expected);
            }
            if (DetectUserInterference()) return; // The detector already schedules one restart.
            if (IsNativeTargetSelected(_target))
            {
                // A right-edge probe can prove a one-column offset by selecting the
                // intended gem from its left neighbor. Retain that verified geometry.
                if (_nativeProbeAcd != 0)
                {
                    var probeCells = CaptureNativeCells();
                    var probed = NativeFind(_nativeProbeAcd, probeCells);
                    var intended = NativeFind(expected, probeCells);
                    if (probed != null && intended != null && probed.Row == intended.Row
                        && Math.Abs(probed.Column - intended.Column) == 1)
                    {
                        float offset = probed.Rect.Left - intended.Rect.Left;
                        if (Math.Abs(offset) <= intended.Rect.Width * 2)
                            _nativeCellOffsetX = _nativeConfirmedOffsetX = offset;
                    }
                    _nativeProbeAcd = 0;
                }
                if (_nativeCalibrationPending)
                {
                    _nativeConfirmedOffsetX = _nativeCellOffsetX;
                    _nativeCalibrationPending = false;
                    
                }
                StartRunningFromConfirmedTarget();
                if (_autoRunning && _pendingInputKind == PendingInputKind.None)
                    HandleRunningState(GetUpgradeAttempts());
                return;
            }
            if (ElapsedMs(_nativeStarted)>12000) { Fail("native gem navigation timeout"); return; }
            if (_nativeWindowSize != Hud.Window.Size || _nativeWindowOffset != Hud.Window.Offset)
            {
                _nativeCellOffsetX=_nativeConfirmedOffsetX=0;
            _nativeCalibrationPending=false; _nativeGeometryKnown=false;
                ResetNativeNavigation(); return; // Reacquire geometry after moving/resizing the window.
            }
            var viewport=NativeViewport(); var cells=CaptureNativeCells();
            if (!NativeRectValid(viewport) || cells.Count==0)
            {
                _nativeStableSince=int.MinValue;
                if (_nativeMissingSince==int.MinValue)_nativeMissingSince=now;
                if (ElapsedMs(_nativeMissingSince)>1000) Fail("native Urshi cell data unavailable");
                return;
            }
            _nativeMissingSince=int.MinValue;
            var cell=NativeFind(expected,cells);
            viewport=NativeViewportForCell(cell,viewport);
            // A present but ambiguous ACD is not a missing row: scrolling cannot
            // establish which duplicate rectangle is authoritative.
            if(cell==null && cells.Any(c=>c.Acd==expected))return;
            if(cell==null && _nativeTargetMissingSince==int.MinValue)
            { _nativeTargetMissingSince=now;_nativeTargetMissingTick=Hud.Game.CurrentGameTick; }
            if(cell!=null)_nativeTargetMissingSince=_nativeTargetMissingTick=int.MinValue;
            if (_nativeClickAt != int.MinValue)
            {
                // Observe every HUD collection. The physical click is retried no sooner than
                // one short confirmation window; no 120/1600 ms animation sleep is imposed.
                int clickAge=ElapsedMs(_nativeClickAt);
                bool stillBusy=_nativeClickDuringBusy && SafeAnimState(_upgradeButton)==27;
                if (stillBusy && clickAge<NativeSelectionPollMs) return;
                // Keep polling every collection. A changed selection or a ready button
                // is actionable immediately; unchanged busy selection gets a bounded retry.
                uint observed=SafeItemButtonAcd();
                if(stillBusy && NativeValidAcd(observed) && observed==_nativeSelectionBeforeClick
                    && !_nativeCalibrationPending && _nativeProbeAcd==0 && clickAge<96) return;
                var calibrationCell = _nativeProbeAcd != 0
                    ? NativeFind(_nativeProbeAcd, cells) : cell;
                bool probeResult = _nativeProbeAcd != 0;
                _nativeProbeAcd = 0;
                _nativeClickAt = int.MinValue;
                RollbackNativeCalibration();
                if (TryCalibrateNativeCell(calibrationCell, cells)) _nativeClickCount = 0;
                else if (!probeResult && cell != null
                    && (!NativeValidAcd(SafeItemButtonAcd()) || CanProbeUnchangedNativeCapSelection())
                    && TryProbeNativeEdgeCell(cell, cells, viewport, now)) return;
                if (_nativeTotalClicks >= NativeSelectionMaxClicks)
                { Fail("native selection retry budget exhausted; no unverified upgrade was sent"); return; }
                
            }
            if (cell!=null && NativeComfortable(cell,viewport))
            {
                bool stable = NativeRectValid(_nativePreviousRect)
                    && Math.Abs(cell.Rect.X-_nativePreviousRect.X)<=0.75f
                    && Math.Abs(cell.Rect.Y-_nativePreviousRect.Y)<=0.75f
                    && Math.Abs(cell.Rect.Width-_nativePreviousRect.Width)<=0.75f
                    && Math.Abs(cell.Rect.Height-_nativePreviousRect.Height)<=0.75f;
                _nativePreviousRect=cell.Rect;
                if (!stable || _nativeStableSince==int.MinValue) { _nativeStableSince=now; _nativeStableTick=Hud.Game.CurrentGameTick;  return; }
                if (Hud.Game.CurrentGameTick==_nativeStableTick || ElapsedMs(_nativeStableSince)<Math.Max(0,CellClickDelayMs)) return;
                var clipped=RectangleF.Intersect(cell.Rect,viewport);
                int x=(int)Math.Round(clipped.Left+clipped.Width/2), y=(int)Math.Round(clipped.Top+clipped.Height/2);
                // BeginMousePulseAt positions and clicks atomically; no extra aim frame.
                // Navigation may overlap the previous Upgrade animation. Only a genuinely
                // capped previous gem needs its documented initial selection-lockout guard.
                if (_retargetNeedsButtonReady && PreviousNativeGemAtCap()
                    && !NativeCapSelectionWindowElapsed()) return;
                x += (int)Math.Round(_nativeCellOffsetX);
                if (!NativeCellClickInsidePane(x,y)) { Fail("native cell point outside pane"); return; }
                // Validate the same native element again at the final input boundary.
                try
                {
                    if (!cell.Ui.Visible || cell.Ui.LegendaryGemAcdId!=expected
                        || Math.Abs(cell.Ui.Rectangle.X-cell.Rect.X)>0.75f
                        || Math.Abs(cell.Ui.Rectangle.Y-cell.Rect.Y)>0.75f) { _nativeStableSince=int.MinValue; return; }
                } catch { _nativeStableSince=int.MinValue;return; }
                if ((NativeMouseState(0x01) & 0x8000)!=0) return;
                _nativeSelectionBeforeClick=SafeItemButtonAcd();
                if (BeginMousePulseAt(x,y,0,ShouldKeepCursorAtAutomationActionPoint()))
                { _nativeClickDuringBusy=SafeAnimState(_upgradeButton)==27; _nativeClickAt=now; _nativeClickCount++; _nativeTotalClicks++; }
                return;
            }
            _nativeStableSince=int.MinValue;
            if (PortalDueSoon(0)) return;
            if (cell==null && (Hud.Game.CurrentGameTick==_nativeTargetMissingTick
                || ElapsedMs(_nativeTargetMissingSince)<32)) return;
            if (_nativeScrollCount>=Math.Max(4,MaxDownScrollClicks)) { Fail("native scroll search limit"); return; }
            RectangleF stack;
            try { stack=_stackPanel.Rectangle; } catch { return; }
            if (!NativeRectValid(stack)) return;
            TryNativeScrollToCell(cell,viewport,stack,now);
        }
                private bool TryNativeScrollToCell(NativeGemCell cell,RectangleF viewport,RectangleF stack,int now,bool preparation=false)
        {
            if (UseWindowMessageInput) return WindowPaneInputActive()
                && TryMessageScrollToCell(cell,viewport,stack,now,preparation);
            viewport=NativeViewportForCell(cell,viewport);
            if(cell!=null && NativeRectValid(viewport)
                && Math.Min(cell.Rect.Right,viewport.Right)-Math.Max(cell.Rect.Left,viewport.Left)<cell.Rect.Width*0.80f)
            {
                if(!preparation)Fail("native gem horizontally clipped; vertical scrolling cannot resolve it");
                return true;
            }
            if(PortalDueSoon(0) || (NativeMouseState(0x01)&0x8000)!=0)return false;
            if(_nativeWheelSentAt!=int.MinValue)
            {
                if(Math.Abs(stack.Top-_nativeWheelStackTop)<0.75f)
                {
                    if(ElapsedMs(_nativeWheelSentAt)<120)return true;
                    // Recheck scroll position each HUD tick; animation is not a scroll lock.
                    if(++_nativeNoProgress>=3){if(!preparation)Fail("native wheel made no progress");return true;}
                }
                else _nativeNoProgress=0;
                _nativeWheelSentAt=int.MinValue;
            }
            bool down=cell!=null ? cell.Rect.Bottom>viewport.Bottom : _nativeSearchDirection>0;
            if(cell==null && !down && stack.Top>=viewport.Top-1)
            { _nativeSearchDirection=1;down=true; }
            if(_nativeScrollCount>=Math.Max(4,MaxDownScrollClicks))
            {if(!preparation)Fail("native scroll search limit");return true;}
            int x=(int)Math.Round(viewport.Left+viewport.Width/2);
            int y=(int)Math.Round(viewport.Top+viewport.Height/2);
            var wheelArea=viewport;wheelArea.Inflate(-3,-3);
            if(!wheelArea.Contains(Hud.Window.CursorX,Hud.Window.CursorY))
            {
                if(!FreeHudInput.MouseMoveClient(Hud,x,y))return true;
                MarkAutomationInputAction();_nativeWheelAimTick=Hud.Game.CurrentGameTick;return true;
            }
            if(Hud.Game.CurrentGameTick==_nativeWheelAimTick)return true;
            float distance=cell==null ? 0 : down ? cell.Rect.Bottom-viewport.Bottom : viewport.Top-cell.Rect.Top;
            int rows=cell==null ? 1 : Math.Max(1,(int)Math.Ceiling(distance/Math.Max(1,cell.Rect.Height)));
            int ticks=rows>=10?6:rows>=5?4:rows>=2?2:1;
            bool sent=false;
            for(int i=0;i<ticks;i++) sent=(down?FreeHudInput.ScrollDown(1):FreeHudInput.ScrollUp(1)) || sent;
            if(sent)
            {
                _nativeScrollCount++;_nativeWheelSentAt=now;_nativeWheelStackTop=stack.Top;
                _nativeWheelAimTick=int.MinValue;MarkAutomationInputAction();
            }
            return true;
        }
        

    }
}
