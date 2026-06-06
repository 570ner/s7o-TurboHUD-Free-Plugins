using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // s7o HUD MENU - FREEHUD In-Game Manager  (Phase 2: MAIN | TOGGLES | PLUGINS)
    // Revision: Settings tab removed; TTS TEST removed; persistent plugin toggles; macro favorites; plugins-style scrollbars.
    public class s7o_HUD_MENU : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameWorldPainter, IInGameTopPainter, IMouseClickHandler
    {
        private const string SettingsFileName = "s7o_HUD_MENU.ini";
        private const string LegacySettingsFileName = "s7o_HUD_MENU.settings.txt";
        private const string DebugLogFileName = "s7o_HUD_MENU.debug.log";
        private const int VisiblePluginRefreshMs = 2000;
        private const int StartupPluginRefreshMs = 500;
        private const int StartupPluginRefreshDurationMs = 6000;
        private const int HotkeyDebounceMs = 220;

        // Larger header reserve because the menu has title controls,
        // page tabs, and a No-Click Background toggle above page content.
        private const float HeaderReserveHeight = 132f;

        // Profile/no-click background timing copied from the Auto Gem Upgrader pattern.
        private const int ProfileOpenGraceMs = 500;
        private const int ChatCloseBeforeProfileCloseMs = 75;
        private const int ProfileCloseOverlayHideDelayMs = 140;
        // The Profile X mask visual and click target must use the same rectangle.
        // This padding makes the mask slightly larger than the raw Profile X anchor,
        // while avoiding the old overly large +4f inflated hit test.
        private const float ProfileCloseMaskPaddingPx = 3f;

        private const int SettingsVersion = 11;

        // Default menu-dot placement target:
        // lower-right skill-bar area on 1920x1080, away from chat and other overlays.
        private const float DefaultDotBaseX = 1452f;
        private const float DefaultDotBaseY = 1018f;
        private const float DefaultDotBaseW = 1920f;
        private const float DefaultDotBaseH = 1080f;

        private const int GlobalTtsVolumeMin = 0;
        private const int GlobalTtsVolumeMax = 100;
        private const int GlobalTtsVolumeStep = 1;
        private const float ReservedScrollbarGutterW = 22f;

        // Default menu hotkey. Can be changed from the in-game header button.
        public Key MenuHotkey = Key.F8;

        private enum MenuPageTab
        {
            Main = 0,
            Toggles = 1,
            Plugins = 2,
        }

        private static readonly MenuPageTab[] MenuPageOrder =
        {
            MenuPageTab.Main,
            MenuPageTab.Toggles,
            MenuPageTab.Plugins,
        };

        private enum ManagerTab
        {
            Favorites = 0,
            Combat = 1,
            Rift = 2,
            Items = 3,
            Other = 4,
            All = 5,
        }

        private enum ToggleCategory
        {
            Visual = 0,     // Overlays, world markers, party inspector
            Macros = 1,     // DH Strafe, Archon, Autocast Conditional Rules
            TtsAlerts = 2,  // Pylon Alerts, Banner Sound
        }

        private static readonly ToggleCategory[] ToggleCategoryOrder =
        {
            ToggleCategory.Visual,
            ToggleCategory.Macros,
            ToggleCategory.TtsAlerts,
        };

        private enum HotkeyInputType
        {
            Keyboard = 0,
            MouseLeft = 1,
            MouseRight = 2,
            MouseMiddle = 3,
            MouseX1 = 4,
            MouseX2 = 5,
        }


        private sealed class FavoriteEntry
        {
            public string TypeName;
            public string DisplayName;
        }

        private sealed class PluginRow
        {
            public IPlugin Plugin;
            public string TypeName;
            public string FullName;
            public string DisplayName;
            public ManagerTab Category;
            public bool IsFavorite;
            public bool HiddenFromList;
        }

        private struct ToggleHitRect
        {
            public string Action;
            public RectangleF Rect;

            public ToggleHitRect(string action, RectangleF rect)
            {
                Action = action;
                Rect = rect;
            }
        }

        private sealed class MenuLayout
        {
            public RectangleF Dot;
            public RectangleF Window;
            public RectangleF Title;
            public RectangleF DotButton;
            public RectangleF HotkeyLabel;
            public RectangleF HotkeyButton;
            public RectangleF MoveButton;
            public RectangleF DebugButton;
            public RectangleF HideButton;
            public RectangleF CloseHudButton;

            public RectangleF PageTabBand;
            public RectangleF StatusBar;
            public RectangleF TopControlBar;
            public RectangleF ContentPane;
            public RectangleF NoClickCheck;
            public RectangleF NoClickLabel;
            public RectangleF ProfileCloseMask;

            public RectangleF LeftPane;
            public RectangleF MainPane;
            public RectangleF ListRect;
            public RectangleF ScrollUp;
            public RectangleF ScrollDown;
            public RectangleF ScrollTrack;
            public RectangleF ScrollThumb;
            public RectangleF Footer;

            public readonly Dictionary<MenuPageTab, RectangleF> PageTabRects = new Dictionary<MenuPageTab, RectangleF>();
            public readonly Dictionary<ManagerTab, RectangleF> TabRects = new Dictionary<ManagerTab, RectangleF>();

            // Content-pane scrollbar (used by MAIN and TOGGLES pages)
            public RectangleF ContentScrollUp;
            public RectangleF ContentScrollDown;
            public RectangleF ContentScrollTrack;
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP_SIMPLE = 0x0002;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        private readonly List<FavoriteEntry> _favorites = new List<FavoriteEntry>();
        private readonly List<PluginRow> _plugins = new List<PluginRow>();
        private readonly Dictionary<string, RectangleF> _lastHitRects = new Dictionary<string, RectangleF>(StringComparer.OrdinalIgnoreCase);

        private readonly MenuLayout _layout = new MenuLayout();
        private readonly List<PluginRow> _activeRows = new List<PluginRow>();

        private bool _layoutDirty = true;
        private bool _activeRowsDirty = true;

        private bool _visible = false;
        private bool _showDot = true;
        private bool _editMode = false;
        private bool _noClickBackground = true;

        private bool _draggingWindow = false;
        private bool _draggingDot = false;
        private bool _draggingScrollThumb = false;
        private bool _menuConsumedLeftMouseDown = false;
        private bool _hideButtonMouseDown = false;
        private bool _profileCloseMaskMouseDown = false;
        private bool _profileCloseMaskReleasePending = false;
        private bool _suppressNextProfileCloseEscape = false;
        private int _suppressNextProfileCloseEscapeUntil = int.MinValue;
        private int _lastMenuBoundaryLeftUpTick = int.MinValue;

        private float _dragOffsetX;
        private float _dragOffsetY;
        private float _scrollDragOffsetY;

        private RectangleF _windowRect = new RectangleF(430f, 100f, 980f, 770f);
        private RectangleF _dotRect = new RectangleF(DefaultDotBaseX, DefaultDotBaseY, 60f, 60f);

        private ManagerTab _activeTab = ManagerTab.All;
        private MenuPageTab _activePage = MenuPageTab.Main;
        private ToggleCategory _activeToggleCategory = ToggleCategory.Visual;
        private int _toggleDetailScroll = 0;
        private int _macrosScroll = 0;
        private int _macrosTotalItems = 0;
        private int _macrosVisibleItems = 1;
        private float _macrosTotalH = 0f;
        private bool _draggingMacrosThumb = false;
        private float _macrosVisH = 0f;  // visible height of macros content area
        private float _macrosScrollDragOffsetY = 0f;
        private RectangleF _macrosScrollUpRect = RectangleF.Empty;
        private RectangleF _macrosScrollDownRect = RectangleF.Empty;
        private RectangleF _macrosScrollTrackRect = RectangleF.Empty;
        private RectangleF _macrosScrollThumbRect = RectangleF.Empty;
        private readonly HashSet<string> _macroFavorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visualFavorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Key _zbarbAutoSnapHotkey = Key.Space;
        private bool _zbarbAutoSnapHotkeyCapture = false;
        private static readonly string[] ZBarbAutoSnapPluginTypeNames =
        {
            "s7o_ZB_AutoSnap",
            "s7o_ZBarbAutoSnap",
            "ZBarb_SpearSnap"
        };

        // ── VISUAL overlay state ───────────────────────────────────────────────────────
        private bool _visPlayerCircleEnabled = false;    private bool _visPlayerCircleExpanded = false;
        private float _visPlayerCircleYards = 25f;       private int  _visPlayerCircleColorIdx = 3;
        private int   _visPlayerCircleTone  = 5;         private float _visPlayerCircleThickness = 2f;

        private bool  _visMouseCircleEnabled = false;    private bool  _visMouseCircleExpanded = false;
        private float _visMouseCircleYards = 10f;        private int   _visMouseCircleColorIdx = 3;
        private int   _visMouseCircleTone   = 5;         private float _visMouseCircleThickness = 2f;

        private bool  _visLineToTargetEnabled = false;   private bool  _visLineToTargetExpanded = false;
        private int   _visLineToTargetColorIdx = 3;      private int   _visLineToTargetTone = 5;
        private float _visLineToTargetThickness = 2f;

        private bool  _visTargetReticleEnabled = false;  private bool  _visTargetReticleExpanded = false;
        private int   _visTargetReticleColorIdx = 0;     private int   _visTargetReticleTone = 5;
        private float _visTargetReticleSize = 1.0f;

        private bool  _visReticleOutlineEnabled = false; private bool  _visReticleOutlineExpanded = false;
        private int   _visReticleOutlineColorIdx = 3;    private int   _visReticleOutlineTone = 5;
        private float _visReticleOutlineThickness = 2.0f;

        private bool  _visClickAnimEnabled = true;
        private bool  _visClickAnimExpanded = false;
        private const float VisualClickAnimationDurationMs = 480f;

        // Default for new users: orange, normal tone, size 1.0, line 1.5.
        private int   _visClickAnimColorIdx = 1;       // orange
        private int   _visClickAnimTone = 5;
        private float _visClickAnimSize = 1.0f;
        private float _visClickAnimThickness = 1.5f;

        // VISUAL / Menu Button.  Fresh-install defaults mirror the release INI
        // menu-button style only: lower-right position, large closed/open sizes,
        // red closed tone 3 and green open tone 3.
        private bool  _visMenuButtonExpanded = false;
        private int   _menuButtonClosedColorIdx = 0;
        private int   _menuButtonClosedTone = 3;
        private float _menuButtonClosedSize = 60f;
        private int   _menuButtonOpenColorIdx = 3;
        private int   _menuButtonOpenTone = 3;
        private float _menuButtonOpenSize = 70f;

        private long  _visClickAnimStartTicks = 0L;
        private float _visClickAnimX = 0f;
        private float _visClickAnimY = 0f;

        private bool  _visMinionCirclesEnabled = false;  private bool  _visMinionCirclesExpanded = false;
        private int   _visMinionColorIdx = 6;            private int   _visMinionTone = 5;
        private float _visMinionThickness = 1.8f;

        private bool  _visSiphonEnabled = false;         private bool  _visSiphonExpanded = false;
        private int   _visSiphonColorIdx = 5;            private int   _visSiphonTone = 5;
        private float _visSiphonDotSize = 3.35f;

        private const uint VisualSiphonDebuffSno = 453563u;

        // ── HUD-controlled default monster visual toggles ─────────────────────────────
        // These are runtime controls for existing FreeHUD default plugins.
        // Plagued is intentionally OFF by default because it creates too much clutter.
        private bool _visDangerousAffixVisualsEnabled = true;
        private bool _visDangerousAffixVisualsExpanded = false;
        private bool _affixPlagued = false;
        private bool _affixArcaneSpawn = true;
        private bool _affixMoltenExplosion = true;
        private bool _affixDesecrator = true;
        private bool _affixThunderstorm = true;
        private bool _affixFrozen = true;
        private bool _affixFrozenPulse = true;
        private bool _affixGhomGas = true;
        private bool _explosiveGrotesque = true;
        private bool _affixOrbiter = true;
        private bool _affixWaller = true;
        private bool _affixWormholeMines = true;

        private bool _affixBossFallingRocks = true;
        private bool _affixBossLeapTelegraphs = true;
        private bool _affixBossMeteors = true;
        private bool _affixBossKulleHazards = true;
        private bool _affixBossBlighterHazards = true;
        private bool _affixBossRatKingHazards = true;
        private bool _affixBossAdriaGeysers = true;
        private bool _affixBossButcherFire = true;
        private bool _affixBossRimeHazards = true;
        private bool _affixShockTowerHazards = true;
        private bool _affixBossSandmonsterTurretHazards = true;

        private bool _eliteAffixLabelsEnabled = true;
        private bool _dangerousMonsterLabelsEnabled = true;

        // ── VISUAL / Party Inspector ─────────────────────────────────────────────────
        private bool _visPartyInspectorExpanded = false;
        private bool _partyInspectorHotkeyCapture = false;
        private Key _partyInspectorHotkey = Key.F12;

        // ── HUD-controlled OpenGR map selector ───────────────────────────────────────
        // Default true + empty set means HUD MENU intentionally leaves all normal maps
        // unchecked. Orek's Dream remains hardcoded inside RiftFishing.
        private bool _openGrMapsExpanded = false;
        private bool _riftMapSelectionHasOverride = true;
        private readonly HashSet<uint> _riftEnabledMapIds = new HashSet<uint>();

        // VISUAL scrollbar state (index-based, same pattern as MACROS)
        private int _visualScroll = 0;
        private int _visualVisibleItems = 0;
        private int _visualTotalItems = 0;
        private bool _draggingVisualThumb = false;
        private float _visualScrollDragOffsetY = 0f;
        private RectangleF _visualScrollUpRect = RectangleF.Empty;
        private RectangleF _visualScrollDownRect = RectangleF.Empty;
        private RectangleF _visualScrollTrackRect = RectangleF.Empty;
        private RectangleF _visualScrollThumbRect = RectangleF.Empty;
        private int _lastVisualScrollUpTick   = int.MinValue;
        private int _lastVisualScrollDownTick = int.MinValue;
        private const float VisualListSlotH = 84f; // 76px row + 8px gap

        // FreeHUD default plugin that draws Focus/Restraint/Taeguk near screen center.
        // Disabled by default via the existing plugin-override system, but users can
        // still enable it from the Plugins page if they prefer it.
        private static readonly string[] DefaultDisabledPluginTypeNames =
        {
            "PlayerBottomBuffListPlugin",
        };

        private readonly Dictionary<string, bool> _pluginEnabledOverrides =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _macroToggleFlashTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private s7o_AutoSkill _autoSkillPlugin = null;
        private bool _autoSkillKeybindsExpanded = false;
        private int _autoSkillKeybindCaptureSlot = -1;
        private readonly ActionKey[] _autoSkillBindActions =
        {
            ActionKey.Skill1,
            ActionKey.Skill2,
            ActionKey.Skill3,
            ActionKey.Skill4,
            ActionKey.Heal
        };
        private readonly string[] _autoSkillBindSlotLabels = { "1", "2", "3", "4", "POT" };
          // pixel scroll offset inside MACROS detail pane
        private int _lastMacrosScrollUpTick   = int.MinValue;
        private int _lastMacrosScrollDownTick = int.MinValue;
        private bool _globalTtsEnabled = true;
        private int _globalTtsVolume = 50;
        private bool _ttsBroadcastExpanded = false;
        private int _ttsCustomMessageHotkeyCaptureIndex = -1;
        private int _ttsCustomMessageEditIndex = -1;
        private bool _ttsCustomDefaultsSeeded = false;
        private int _ttsCustomDefaultsSeedVersion = 0;
        private bool _ttsPendingKeyboardAutoScroll = false;

        private const int MaxTtsCustomMessages = 12;
        private const int TtsCustomDefaultsCurrentVersion = 3;
        private const int MaxTtsCustomMessageChars = 64;
        private const char TtsKeyboardFakeSpaceChar = '\u00A0'; // legacy editor-space marker from prior builds
        private const string TtsKeyboardFakeSpaceString = "\u00A0";
        private const string TtsDuplicateLineSalt = "\u00A0";
        private const int TtsScrollStepPx = 42;

        private string _lastTtsBroadcastNormalizedPayload = string.Empty;
        private bool _ttsBroadcastDuplicateSaltOn = false;

        private int _ttsAlertsScrollPx = 0;
        private int _ttsAlertsContentHeightPx = 0;
        private int _ttsAlertsViewportHeightPx = 0;

        private RectangleF _ttsScrollUpRect = RectangleF.Empty;
        private RectangleF _ttsScrollDownRect = RectangleF.Empty;
        private RectangleF _ttsScrollTrackRect = RectangleF.Empty;
        private RectangleF _ttsScrollThumbRect = RectangleF.Empty;
        private bool _draggingTtsScrollThumb = false;
        private float _ttsScrollDragOffsetY = 0f;
        private int _lastTtsScrollUpTick = int.MinValue;
        private int _lastTtsScrollDownTick = int.MinValue;

        private bool _ttsKeyboardCaps = false;
        private bool _ttsKeyboardShiftOnce = false;

        private sealed class TtsCustomMessage
        {
            public string Text = string.Empty;
            public Key Hotkey = Key.Unknown;
        }

        private readonly List<TtsCustomMessage> _ttsCustomMessages = new List<TtsCustomMessage>();
        private readonly List<bool> _ttsCustomHotkeyDownLatch = new List<bool>();
        private readonly Dictionary<string, int> _ttsButtonFlashTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _visualButtonFlashTicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private RectangleF _globalTtsSliderTrack = RectangleF.Empty;
        private RectangleF _globalTtsSliderThumb = RectangleF.Empty;
        private bool _draggingGlobalTtsSlider = false;
        private int _lastTtsVolMinusTick = int.MinValue;
        private int _lastTtsVolPlusTick = int.MinValue;
        private int _loadedSettingsVersion = 0;

        private int _scroll = 0;
        private int _lastRefreshTick = int.MinValue;
        private bool _pluginCacheRefreshRequested = false;
        private int _nextVisiblePluginRefreshTick = int.MinValue;
        private int _startupPluginRefreshUntilTick = int.MinValue;
        private int _nextStartupPluginRefreshTick = int.MinValue;
        private string _status = "READY";
        private bool _resourcesReady = false;
        private bool _capturingHotkey = false;
        private bool _debugLogging = false;
        private int _lastHotkeyTick = int.MinValue;
        private string _debugLogPath = string.Empty;
        private IKeyEvent _menuKeyEvent;

        private const string AutoGemUiOwnerName = "s7o_HUD_MENU";

        // ── HUD Hotkeys panel (MAIN page) ─────────────────────────────────────────────
        private bool _hotkeysExpanded = true;
        private int _lastHotkeyResetTick = int.MinValue;

        // Per-hotkey capture state
        private int _capturingHudHotkeyIdx = -1; // -1 = not capturing

        // Hotkey definitions (from config/hotkeys.xml)
        private static readonly string[] HotkeyIds    = { "exit",  "hide_hud", "reload_pickit", "capture", "capture_with_overlay", "stat_tracker", "debug_overlay", "save_debug_data", "reset_session" };
        private static readonly string[] HotkeyLabels = { "Exit TurboHUD", "Hide HUD", "Reload PickIt", "Screenshot", "Screenshot+Overlay", "Stat Tracker", "Debug Overlay", "Save Debug Data", "Reset Session" };
        private static readonly string[] HotkeyDefMod = { "Ctrl",  "",   "",    "Alt",  "Ctrl+Alt",  "",    "",     "Ctrl+Alt",  "Ctrl+Alt" };
        private static readonly string[] HotkeyDefKey = { "End",   "F4", "F3",  "C",    "C",         "F5",  "F11",  "D",         "R" };

        // Current runtime hotkey values (may differ from defaults after user edits)
        private string[] _hudHotkeyMod;
        private string[] _hudHotkeyKey;

        // ── Content-pane scrollbar flash ticks ────────────────────────────────────────
        private int _lastPluginsScrollUpTick   = int.MinValue;
        private int _lastPluginsScrollDownTick = int.MinValue;

        private bool _autoGemExpanded = true;
        private bool _autoGemSpecificExpanded = false;
        private int _autoGemSpecificScroll = 0;

        private RectangleF _autoGemSpecificScrollTrack = RectangleF.Empty;
        private RectangleF _autoGemSpecificScrollThumb = RectangleF.Empty;
        private bool _draggingAutoGemSpecificScrollThumb = false;
        private float _autoGemSpecificScrollDragOffsetY = 0f;
        private int _autoGemSpecificVisibleRows = 1;
        private int _autoGemSpecificMaxScroll = 0;
        private int _lastAutoGemSpecificScrollUpTick = int.MinValue;
        private int _lastAutoGemSpecificScrollDownTick = int.MinValue;
        private int _lastAutoGemSpecificScrollTrackTick = int.MinValue;

        // ── MAIN / AutoSnap ────────────────────────────────────────────────────────
        private bool _autoSnapExpanded = true;

        private readonly string[] _autoSnapLabels = { "1", "2", "3", "4", "L", "R" };
        private readonly int[] _autoSnapSlots = { 0, 1, 2, 3, 4, 5 };

        // UI order: 0-3 = Skill1-4, 4 = LeftSkill, 5 = RightSkill
        private bool[] _asEnabled = new bool[6];

        private int _autoSnapMode = 0; // 0 = Near Me, 1 = Cursor
        private int _autoSnapMeleeRangeYards = 80;
        private int _autoSnapRangedRangeYards = 25;

        private bool _autoSnapLeftClickForceStandStill = true;
        private bool _autoSnapRestoreCursor = false;
        private bool _autoSnapIgnoreJuggernauts = true;
        private bool _autoSnapIgnoreMinions = false;

        // AutoSnap UI safety master toggle.
        // When true, AutoSnap refuses screen targets inside explicit no-click UI masks.
        public bool AutoSnapAvoidUiRegions = true;

        // Use the user-provided red-mask regions instead of a broad bottom-screen band.
        // This avoids blocking large usable/clickable parts of the lower playfield.
        public bool AutoSnapUseExplicitNoClickMask = true;

        // Legacy broad bottom band is intentionally OFF by default.
        // Enable manually only if the explicit mask is not enough for a user.
        public bool AutoSnapUseLegacyBottomBand = false;

        // AutoSnap safety: Orlash clone/helper actors can expose elite/minion-like metadata.
        // Ignore them as AutoSnap targets so they do not steal targeting from the real boss.
        public bool AutoSnapIgnoreOrlashClones = true;

        // Kept for optional legacy fallback only. Not used unless AutoSnapUseLegacyBottomBand = true.
        public float AutoSnapUnsafeBottomFrac = 0.24f;
        public float AutoSnapUnsafeBottomMinPx = 210f;
        public float AutoSnapUnsafeBottomMaxPx = 300f;

        // Optional padding around the explicit red-mask rectangles.
        // Keep 0 by default because the user hand-marked the intended no-click areas.
        public float AutoSnapNoClickMaskPaddingPx = 0f;

        // AutoSnap safety: avoid clicking directly on player/party portrait face icons.
        // Uses IPlayer.PortraitUiElement instead of guessed raw UI paths.
        // This intentionally does NOT block the whole portrait frame or minimap.
        public bool AutoSnapAvoidPlayerPortraitFaces = true;

        // Keep this 0 by default so the clickable portrait frame around the face remains usable.
        // If testing shows the icon rect is slightly too tight, set to 1-3 px.
        public float AutoSnapPortraitFacePaddingPx = 0f;

        private bool _asHotkeysEnabled = false;
        private bool _asHotkeysOnly = false;
        private bool _autoSnapHotkeysExpanded = false;

        private HotkeyInputType[] _asHotkeyInputTypes =
        {
            HotkeyInputType.Keyboard,
            HotkeyInputType.Keyboard,
            HotkeyInputType.Keyboard,
            HotkeyInputType.Keyboard,
            HotkeyInputType.Keyboard,
            HotkeyInputType.Keyboard
        };

        private Key[] _asHotkeyKeys =
        {
            Key.Space,
            Key.Space,
            Key.Space,
            Key.Space,
            Key.Space,
            Key.Space
        };

        private Keys[] _asHotkeyWinKeys =
        {
            Keys.Space,
            Keys.Space,
            Keys.Space,
            Keys.Space,
            Keys.Space,
            Keys.Space
        };

        private string[] _asHotkeyLabels =
        {
            "[Space]",
            "[Space]",
            "[Space]",
            "[Space]",
            "[Space]",
            "[Space]"
        };

        private bool[] _asHotkeyCaptureActive = new bool[6];

        // Skill cache used by AutoSnap.
        private IPlayerSkill[] _asSkills = new IPlayerSkill[6];
        // AutoSnap transient runtime state.
        private int _asLockedAcdId = 0;
        private int _asLastMoveTick = 0;
        private bool _asLeftStandStillOwned = false;

        private bool _asRestoreTrackActive = false;
        private bool _asRestoreWasMoved = false;
        private int _asRestoreStartTick = 0;
        private float _asRestoreSavedX = 0f;
        private float _asRestoreSavedY = 0f;
        private ActionKey _asRestoreKey = ActionKey.Unknown;

        private bool _asHotkeyRequestPending = false;
        private int _asHotkeyRequestSlot = -1;
        private int _asHotkeyRequestTick = 0;
        private float _asHotkeyRequestCursorX = 0f;
        private float _asHotkeyRequestCursorY = 0f;
        private float _asHotkeySavedX = 0f;
        private float _asHotkeySavedY = 0f;

        private bool _asHotkeyCastPending = false;
        private int _asHotkeyCastTick = 0;
        private ActionKey _asHotkeyCastAction = ActionKey.Unknown;

        private bool _asHotkeyRestorePending = false;
        private int _asHotkeyRestoreTick = 0;

        private bool _asReleaseRestorePending = false;
        private int _asReleaseRestoreSlot = -1;
        private int _asReleaseRestorePressStartTick = 0;
        private float _asReleaseRestoreX = 0f;
        private float _asReleaseRestoreY = 0f;

        private int _asHotkeyActiveSlot = -1;
        private int _asHotkeyPressStartTick = 0;
        private float _asHotkeyAimX = 0f;
        private float _asHotkeyAimY = 0f;

        private bool[] _asHotkeyDownPrev = new bool[6];
        private const int AnySnapHotkeyHoldRepeatStartTicks    = 12;
        private const int AnySnapHotkeyHoldRepeatIntervalTicks = 2;

        private int[] _asHotkeyHoldStartTick =
        {
            -1000000, -1000000, -1000000, -1000000, -1000000, -1000000
        };

        private int[] _asHotkeyLastRepeatTick =
        {
            0, 0, 0, 0, 0, 0
        };

        // About 2 seconds at 60 game ticks/sec.
        // Quick tap restores cursor. Long hold/channel does not.
        private const int AnySnapRestoreCursorMaxHoldTicks = 120;
        private const float AnySnapRestoreMinMovePxSq = 16f;
        private const int AnySnapHotkeyPreCastDelayTicks = 1;

        // Suppress the normal held-action AutoSnap path for a few ticks after
        // this plugin injects an AutoSnap hotkey cast. Without this, synthetic
        // 1/2/3/4/L/R taps can echo through Hud.Input and be mistaken for a
        // physical held skill, which can start an unintended held-action loop.
        private ActionKey _asSuppressHeldActionKey = ActionKey.Unknown;
        private int _asSuppressHeldActionUntilTick = 0;
        private const int AnySnapSyntheticActionEchoSuppressTicks = 6;


        // ── MAIN content scrollbar ───────────────────────────────────────────────
        private int _mainScroll = 0;
        private int _mainTotalItems = 0;
        private int _mainVisibleItems = 1;

        private bool _draggingMainThumb = false;
        private float _mainScrollDragOffsetY = 0f;

        private RectangleF _mainScrollUpRect = RectangleF.Empty;
        private RectangleF _mainScrollDownRect = RectangleF.Empty;
        private RectangleF _mainScrollTrackRect = RectangleF.Empty;
        private RectangleF _mainScrollThumbRect = RectangleF.Empty;

        private int _lastMainScrollUpTick = int.MinValue;
        private int _lastMainScrollDownTick = int.MinValue;

        private readonly Dictionary<string, RectangleF> _mainHitRects =
            new Dictionary<string, RectangleF>(StringComparer.OrdinalIgnoreCase);

        private readonly List<ToggleHitRect> _togglesHitRects =
            new List<ToggleHitRect>();

        private readonly Dictionary<string, RectangleF> _globalHitRects =
            new Dictionary<string, RectangleF>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] AutoGemNames =
        {
            "Bane of the Powerful",
            "Bane of the Stricken",
            "Bane of the Trapped",
            "Boon of the Hoarder",
            "Boyarsky's Chip",
            "Enforcer",
            "Esoteric Alteration",
            "Gem of Ease",
            "Gem of Efficacious Toxin",
            "Gogok of Swiftness",
            "Iceblink",
            "Invigorating Gemstone",
            "Legacy of Dreams",
            "Mirinae, Teardrop of the Starweaver",
            "Molten Wildebeest's Gizzard",
            "Moratorium",
            "Mutilation Guard",
            "Pain Enhancer",
            "Simplicity's Strength",
            "Taeguk",
            "Wreath of Lightning",
            "Whisper of Atonement",
            "Zei's Stone of Vengeance",
        };

        private bool _profileOpenedByHudMenu;
        private bool _profileWasVisibleBeforeMenu;
        private bool _profileBackgroundActiveForMenu;
        private bool _profileOpenRequested;
        private int _profileOpenRequestTick = int.MinValue;
        private bool _profileConfirmedVisibleForMenu;
        private bool _pendingProfileCloseAfterChat;
        private int _pendingProfileCloseAt = int.MinValue;
        private bool _pendingProfileCloseHideMenu = true;
        private bool _pendingOverlayHideAfterProfileClose;
        private bool _pendingOverlayHideShouldHideMenu = true;
        private int _pendingOverlayHideAt = int.MinValue;
        private int _pendingOverlayHideStartedAt = int.MinValue;

        private IUiElement _chatEditLine;
        private IUiElement _profileUiElement;

        private IBrush _bShadow, _bFrame, _bFrameBorder, _bInner, _bTitle, _bPane, _bPaneBorder;
        private IBrush _bRow, _bRowAlt, _bBtnGloss, _bBtnEdge;

        private IBrush _bBtnNormalTop, _bBtnNormalBottom;
        private IBrush _bBtnActiveTop, _bBtnActiveBottom;
        private IBrush _bBtnDangerTop, _bBtnDangerBottom;
        private IBrush _bBtnDangerFlashTop, _bBtnDangerFlashBottom;

        private IBrush _bToggleEdge;
        private IBrush _bToggleOffTop, _bToggleOffBottom;
        private IBrush _bToggleOnTop, _bToggleOnBottom;

        private IBrush _bStarOnTop, _bStarOnBottom;
        private IBrush _bStarOffTop, _bStarOffBottom;

        private IBrush _bScrollTrackSolid, _bScrollThumbGreen, _bScrollThumbGreenActive, _bScrollBorder;
        private IBrush _bGlowOuter;     // outer glow ring on flash
        private IBrush _bGlowInner;     // inner glow ring on flash
        private IBrush _bPaneCover;     // opaque pane color for scroll edge masking

        // Cached VISUAL color-picker brushes: 8 colors x 11 tone levels.
        // This avoids creating swatch/menu-button brushes every frame.
        private readonly IBrush[,] _visualPickerFillBrushes = new IBrush[8, 11];
        private readonly IBrush[,] _visualPickerShadowBrushes = new IBrush[8, 11];
        private IBrush _visualPickerEdgeSelected;
        private IBrush _visualPickerEdgeNormal;
        private readonly Dictionary<string, IBrush> _visualBrushCache =
            new Dictionary<string, IBrush>(StringComparer.Ordinal);

        private IBrush _bDotHalo, _bDotFill, _bDotFillOpen, _bDotShadow, _bDotShadowOpen, _bDotRim, _bDotSpec, _bDotHot, _bEditDash, _bTextDimBg;
        private IBrush _bProfileMask;
        private IBrush _bPreviewBlack;
        private IBrush _bPreviewBorder;
        private IBrush _bPrevGreenDash;
        private IBrush _bPrevRedDash;
        private IBrush _bPrevOrangeDash;
        private IBrush _bPrevBlueDash;
        private IBrush _bPrevPurpleDash;
        private IBrush _bPrevGrey;
        private IBrush _bPrevYellow;
        private IFont _fTitle, _fLabel, _fSection, _fText, _fSmall, _fButton, _fButtonActive;
        private IFont _fSmallShadow, _fButtonShadow;
        private IFont _fButtonLarge, _fButtonLargeActive, _fButtonLargeShadow;
        // Larger outlined row fonts for TOGGLES detail rows only.
        private IFont _fRowTitle, _fRowTitleShadow;
        private IFont _fRowText, _fRowTextShadow;
        private IFont _fStar, _fStarShadow;

        public s7o_HUD_MENU()
        {
            Enabled = true;
            Order = 95000;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            LoadSettings();
            ApplyGlobalTtsSettings();
            ClaimAutoGemUiOwnership();
            InitHudHotkeyState();

            try { _chatEditLine = Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null); }
            catch { _chatEditLine = null; }

            // Runtime UI path left unchanged intentionally. The No-Click backdrop is controlled
            // by Shift+P/Profile, but this FreeHUD/Diablo UI locator must not be renamed
            // without verifying the actual in-game UI path.
            try { _profileUiElement = Hud.Render.RegisterUiElement("Root.NormalLayer.seasonal_check_dialog", null, null); }
            catch { _profileUiElement = null; }

            // Public-test safety: never restore an open overlay/Profile session after HUD reload.
            _visible = false;
            _capturingHotkey = false;
            ClearProfileBackgroundState();

            RelocateLegacyRightCenterDotIfNeeded();

            _menuKeyEvent = Hud.Input.CreateKeyEvent(true, MenuHotkey, false, false, false);
            _debugLogPath = DebugLogPath();
            ClampRectsToScreen();
            MarkRowsDirty();
            RefreshPluginCache(true);
            _pluginCacheRefreshRequested = false;
            _lastRefreshTick = Environment.TickCount;
            _nextVisiblePluginRefreshTick = unchecked(_lastRefreshTick + VisiblePluginRefreshMs);
            _startupPluginRefreshUntilTick = unchecked(_lastRefreshTick + StartupPluginRefreshDurationMs);
            _nextStartupPluginRefreshTick = int.MinValue;
            LogDebug("s7o_HUD_MENU loaded. Hotkey=" + MenuHotkey + ", debug=" + _debugLogging + ".");
        }

        public void AfterCollect()
        {
            int now = Environment.TickCount;

            ProcessPendingProfileClose();
            ProcessPendingOverlayHideAfterProfileClose();
            ProcessPendingProfileMaskReleaseClose();
            SyncProfileAndOverlayPair();

            if (!_pendingProfileCloseAfterChat && !_pendingOverlayHideAfterProfileClose && !_profileCloseMaskReleasePending)
                SyncExternalProfileClosure();

            ClaimAutoGemUiOwnership();

            RefreshAutoSnapSkillCache();

            try
            {
                RunAnyAutoSnapHotkeys();
                RunAnyAutoSnap();
            }
            catch { }

            bool startupPluginRefreshDue =
                _startupPluginRefreshUntilTick != int.MinValue &&
                unchecked(now - _startupPluginRefreshUntilTick) < 0 &&
                (_nextStartupPluginRefreshTick == int.MinValue ||
                 unchecked(now - _nextStartupPluginRefreshTick) >= 0);

            if (_pluginCacheRefreshRequested)
            {
                RefreshPluginCache(true);
                _pluginCacheRefreshRequested = false;
                _lastRefreshTick = now;
                _nextVisiblePluginRefreshTick = unchecked(now + VisiblePluginRefreshMs);
                _nextStartupPluginRefreshTick = unchecked(now + StartupPluginRefreshMs);
            }
            else if (startupPluginRefreshDue)
            {
                RefreshPluginCache(true);
                _lastRefreshTick = now;
                _nextStartupPluginRefreshTick = unchecked(now + StartupPluginRefreshMs);
            }
            else if (_visible &&
                     (_nextVisiblePluginRefreshTick == int.MinValue ||
                      unchecked(now - _nextVisiblePluginRefreshTick) >= 0))
            {
                RefreshPluginCache(false);
                _lastRefreshTick = now;
                _nextVisiblePluginRefreshTick = unchecked(now + VisiblePluginRefreshMs);
            }
            else if (_startupPluginRefreshUntilTick != int.MinValue &&
                     unchecked(now - _startupPluginRefreshUntilTick) >= 0)
            {
                _startupPluginRefreshUntilTick = int.MinValue;
                _nextStartupPluginRefreshTick = int.MinValue;
            }

            RunCustomTtsBroadcastHotkeys();

            bool leftDown = false;
            try { leftDown = Hud.Input.IsKeyDown(Keys.LButton); } catch { }

            if (!leftDown)
            {
                bool wasPositionDrag = _draggingWindow || _draggingDot;
                bool wasAutoGemSpecificDrag = _draggingAutoGemSpecificScrollThumb;
                bool wasGlobalTtsDrag = _draggingGlobalTtsSlider;
                bool wasMacrosDrag = _draggingMacrosThumb;
                bool wasVisualDrag = _draggingVisualThumb;
                bool wasMainDrag = _draggingMainThumb;
                bool wasTtsDrag = _draggingTtsScrollThumb;
                bool wasAnyDrag = wasPositionDrag || _draggingScrollThumb || wasAutoGemSpecificDrag || wasGlobalTtsDrag || wasMacrosDrag || wasVisualDrag || wasMainDrag || wasTtsDrag;

                if (wasAnyDrag)
                {
                    _draggingWindow = false;
                    _draggingDot = false;
                    _draggingMacrosThumb = false;
                    _draggingVisualThumb = false;
                    _draggingMainThumb = false;
                    _draggingTtsScrollThumb = false;
                    _draggingScrollThumb = false;
                    _draggingAutoGemSpecificScrollThumb = false;
                    _draggingGlobalTtsSlider = false;

                    if (wasPositionDrag)
                    {
                        ClampRectsToScreen();
                        MarkLayoutDirty();
                        SaveSettings();
                    }

                    if (wasAutoGemSpecificDrag || wasGlobalTtsDrag)
                    {
                        if (wasGlobalTtsDrag)
                        {
                            ApplyGlobalTtsSettings();
                            RequestPluginCacheRefresh();
                        }
                        SaveSettings();
                    }
                }

                return;
            }

            if (_editMode && _draggingWindow)
            {
                try
                {
                    _windowRect.X = Hud.Window.CursorX - _dragOffsetX;
                    _windowRect.Y = Hud.Window.CursorY - _dragOffsetY;
                    ClampWindowOnly();
                    MarkLayoutDirty();
                }
                catch { }
            }
            else if (_editMode && _draggingDot)
            {
                try
                {
                    float size = _visible ? ViClampF(_menuButtonOpenSize, 18f, 70f) : ViClampF(_menuButtonClosedSize, 18f, 60f);
                    float newLeft = Hud.Window.CursorX - _dragOffsetX;
                    float newTop = Hud.Window.CursorY - _dragOffsetY;
                    float cx = newLeft + size * 0.5f;
                    float cy = newTop + size * 0.5f;

                    _dotRect.X = cx - _dotRect.Width * 0.5f;
                    _dotRect.Y = cy - _dotRect.Height * 0.5f;
                    ClampDotOnly();
                    MarkLayoutDirty();
                }
                catch { }
            }
            else if (_draggingGlobalTtsSlider)
            {
                try
                {
                    if (SetGlobalTtsVolumeFromCursorX(Hud.Window.CursorX))
                    {
                        ApplyGlobalTtsSettings();
                        RequestPluginCacheRefresh();
                        _status = "GLOBAL TTS VOLUME: " + _globalTtsVolume.ToString(CultureInfo.InvariantCulture);
                        MarkLayoutDirty();
                    }
                }
                catch { }
            }
            else if (_draggingAutoGemSpecificScrollThumb)
            {
                try
                {
                    if (_activePage == MenuPageTab.Main && _autoGemSpecificExpanded)
                    {
                        SetAutoGemSpecificScrollFromCursorY(Hud.Window.CursorY);
                    }
                    else
                    {
                        _draggingAutoGemSpecificScrollThumb = false;
                    }
                }
                catch { }
            }
            else if (_draggingScrollThumb)
            {
                try
                {
                    var layout = GetLayout();
                    DragScrollThumbToCursor(layout, Hud.Window.CursorY);
                }
                catch { }
            }
            else if (_draggingMacrosThumb)
            {
                try
                {
                    if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
                        DragMacrosScrollThumbToCursor(Hud.Window.CursorY);
                    else
                        _draggingMacrosThumb = false;
                }
                catch { }
            }
            else if (_draggingMainThumb)
            {
                try
                {
                    if (_activePage == MenuPageTab.Main)
                        DragMainScrollThumbToCursor(Hud.Window.CursorY);
                    else
                        _draggingMainThumb = false;
                }
                catch { }
            }
            else if (_draggingVisualThumb)
            {
                try
                {
                    if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Visual)
                        DragVisualScrollThumbToCursor(Hud.Window.CursorY);
                    else
                        _draggingVisualThumb = false;
                }
                catch { }
            }
            else if (_draggingTtsScrollThumb)
            {
                try
                {
                    if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.TtsAlerts)
                        DragTtsAlertsScrollThumbToCursor(Hud.Window.CursorY);
                    else
                        _draggingTtsScrollThumb = false;
                }
                catch { }
            }
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (keyEvent == null || !keyEvent.IsPressed) return;

            if (IsChatEntryOpen())
                return;

            if (ConsumeProfileCloseEscapeSuppression(keyEvent.Key))
                return;

            if (_ttsCustomMessageHotkeyCaptureIndex >= 0)
            {
                if (IsCaptureCancelKey(keyEvent.Key))
                {
                    _ttsCustomMessageHotkeyCaptureIndex = -1;
                    _status = "TTS HOTKEY CAPTURE CANCELLED";
                    return;
                }

                int idx = _ttsCustomMessageHotkeyCaptureIndex;
                _ttsCustomMessageHotkeyCaptureIndex = -1;

                if (idx >= 0 && idx < _ttsCustomMessages.Count)
                {
                    _ttsCustomMessages[idx].Hotkey = keyEvent.Key;
                    _status = "TTS HOTKEY SET TO " + CaptureKeyLabel(keyEvent.Key);
                    SaveSettings();
                }

                return;
            }

            if (_zbarbAutoSnapHotkeyCapture)
            {
                if (IsCaptureCancelKey(keyEvent.Key))
                {
                    _zbarbAutoSnapHotkeyCapture = false;
                    _status = "Z-BARB AUTOSNAP HOTKEY CAPTURE CANCELLED";
                    return;
                }

                _zbarbAutoSnapHotkey = keyEvent.Key;
                _zbarbAutoSnapHotkeyCapture = false;

                ApplyZBarbAutoSnapHotkeyToPlugin();
                RequestPluginCacheRefresh();

                _status = "Z-BARB AUTOSNAP HOTKEY SET TO " + CaptureKeyLabel(_zbarbAutoSnapHotkey);
                SaveSettings();
                return;
            }

            if (_partyInspectorHotkeyCapture)
            {
                if (IsCaptureCancelKey(keyEvent.Key))
                {
                    _partyInspectorHotkeyCapture = false;
                    _status = "PARTY INSPECTOR HOTKEY CAPTURE CANCELLED";
                    return;
                }

                _partyInspectorHotkey = keyEvent.Key;
                _partyInspectorHotkeyCapture = false;
                ApplyPartyInspectorHotkeyToPlugin();
                RequestPluginCacheRefresh();
                _status = "PARTY INSPECTOR HOTKEY SET TO " + CaptureKeyLabel(_partyInspectorHotkey);
                SaveSettings();
                return;
            }

            if (_autoSkillKeybindCaptureSlot >= 0)
            {
                if (IsCaptureCancelKey(keyEvent.Key))
                {
                    _autoSkillKeybindCaptureSlot = -1;
                    _status = "AUTOSKILL KEYBIND CAPTURE CANCELLED";
                    return;
                }

                int slot = _autoSkillKeybindCaptureSlot;
                _autoSkillKeybindCaptureSlot = -1;
                ApplyAutoSkillKeybindFromCapturedKey(slot, keyEvent.Key);
                return;
            }

            for (int i = 0; i < _asHotkeyCaptureActive.Length; i++)
            {
                if (_asHotkeyCaptureActive[i])
                {
                    if (IsCaptureCancelKey(keyEvent.Key))
                    {
                        ClearAnySnapHotkeyCapture();
                        _status = "AUTOSNAP HOTKEY CAPTURE CANCELLED";
                        return;
                    }

                    SetAnySnapHotkey(i, keyEvent.Key);
                    ClearAnySnapHotkeyCapture();
                    SaveSettings();
                    return;
                }
            }

            // Per-hotkey capture mode (HUD Hotkeys panel)
            if (_capturingHudHotkeyIdx >= 0)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHudHotkeyIdx = -1;
                    _status = "HOTKEY CAPTURE CANCELLED";
                    return;
                }

                string keyStr = KeyToHotkeyString(keyEvent.Key);
                if (keyStr == null) return; // modifier key alone — ignore

                // Detect held modifiers via input state
                string modStr = string.Empty;
                try
                {
                    bool ctrl  = Hud.Input.IsKeyDown(Keys.ControlKey) || Hud.Input.IsKeyDown(Keys.LControlKey) || Hud.Input.IsKeyDown(Keys.RControlKey);
                    bool alt   = Hud.Input.IsKeyDown(Keys.Menu)       || Hud.Input.IsKeyDown(Keys.Alt);
                    bool shift = Hud.Input.IsKeyDown(Keys.ShiftKey)   || Hud.Input.IsKeyDown(Keys.LShiftKey)   || Hud.Input.IsKeyDown(Keys.RShiftKey);

                    if (ctrl && alt) modStr = "ctrl+alt";
                    else if (ctrl)   modStr = "ctrl";
                    else if (alt)    modStr = "alt";
                    else if (shift)  modStr = "shift";
                }
                catch { }

                int idx = _capturingHudHotkeyIdx;
                _capturingHudHotkeyIdx = -1;

                _hudHotkeyMod[idx] = modStr;
                _hudHotkeyKey[idx] = keyStr;

                SaveHotkeyToXml(idx);
                _status = "HOTKEY SET: " + (string.IsNullOrEmpty(modStr) ? keyStr : modStr.ToUpperInvariant() + "+" + keyStr.ToUpperInvariant());
                return;
            }

            if (_capturingHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHotkey = false;
                    _status = "HOTKEY CAPTURE CANCELLED";
                    LogDebug("Hotkey capture cancelled.");
                    SaveSettings();
                    return;
                }

                MenuHotkey = keyEvent.Key;
                _menuKeyEvent = Hud.Input.CreateKeyEvent(true, MenuHotkey, false, false, false);
                _capturingHotkey = false;
                _status = "HOTKEY SET TO " + MenuHotkey;
                SaveSettings();
                LogDebug("Menu hotkey changed to " + MenuHotkey + ".");
                return;
            }

            int anySnapHotkeySlot;
            if (AnySnapHotkeysCanHandle() && AnySnapTryGetHotkeySlotByPressedKey(keyEvent.Key, out anySnapHotkeySlot))
            {
                int tick = Hud.Game != null ? Hud.Game.CurrentGameTick : Environment.TickCount;
                AnySnapHotkeyOnPress(anySnapHotkeySlot, tick);
                return;
            }

            if (_menuKeyEvent == null)
                _menuKeyEvent = Hud.Input.CreateKeyEvent(true, MenuHotkey, false, false, false);

            if (_menuKeyEvent.Matches(keyEvent))
            {
                int now = Environment.TickCount;
                if (_lastHotkeyTick == int.MinValue || unchecked(now - _lastHotkeyTick) >= HotkeyDebounceMs)
                {
                    _lastHotkeyTick = now;
                    if (_visible) CloseMenu(true);
                    else OpenMenu();

                    SaveSettings();
                    LogDebug("Menu toggled by hotkey. Visible=" + _visible + ".");
                }
                return;
            }

            if (!_visible) return;

            if (keyEvent.Key == Key.Escape || keyEvent.Key == Key.Space)
            {
                // Physical Escape/Space is allowed to close the No-Click background directly.
                // Hide this menu immediately and clear our binding state.
                CloseMenu(false);
                ClearProfileBackgroundState();
                SaveSettings();
                return;
            }

            if (keyEvent.Key == Key.Tab)
            {
                StepPage(+1);
                return;
            }

            if (keyEvent.Key == Key.Up)
            {
                if (_activePage == MenuPageTab.Main)
                {
                    ScrollMainBy(-1);
                    return;
                }

                if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
                {
                    ScrollMacrosBy(-1);
                    return;
                }

                ScrollBy(-1, GetLayout());
                return;
            }

            if (keyEvent.Key == Key.Down)
            {
                if (_activePage == MenuPageTab.Main)
                {
                    ScrollMainBy(+1);
                    return;
                }

                if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
                {
                    ScrollMacrosBy(+1);
                    return;
                }

                ScrollBy(+1, GetLayout());
                return;
            }

            if (keyEvent.Key == Key.PageUp)
            {
                if (_activePage == MenuPageTab.Main)
                {
                    ScrollMainBy(-Math.Max(1, _mainVisibleItems - 1));
                    return;
                }

                if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
                {
                    ScrollMacrosBy(-Math.Max(1, _macrosVisibleItems - 1));
                    return;
                }

                var layout = GetLayout();
                ScrollBy(-Math.Max(1, VisibleRowCount(layout) - 1), layout);
                return;
            }

            if (keyEvent.Key == Key.PageDown)
            {
                if (_activePage == MenuPageTab.Main)
                {
                    ScrollMainBy(+Math.Max(1, _mainVisibleItems - 1));
                    return;
                }

                if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
                {
                    ScrollMacrosBy(+Math.Max(1, _macrosVisibleItems - 1));
                    return;
                }

                var layout = GetLayout();
                ScrollBy(+Math.Max(1, VisibleRowCount(layout) - 1), layout);
                return;
            }
        }

        private bool ConsumeLeftMouseDown()
        {
            _menuConsumedLeftMouseDown = true;
            return true;
        }

        private void ReleaseLeftMouseForMenuBoundary()
        {
            try
            {
                int now = Environment.TickCount;

                if (_lastMenuBoundaryLeftUpTick != int.MinValue &&
                    unchecked(now - _lastMenuBoundaryLeftUpTick) >= 0 &&
                    unchecked(now - _lastMenuBoundaryLeftUpTick) < 40)
                {
                    return;
                }

                _lastMenuBoundaryLeftUpTick = now;
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch { }

            _menuConsumedLeftMouseDown = false;
        }

        public bool MouseDown(MouseButtons button)
        {
            if (button != MouseButtons.Left) return false;

            _menuConsumedLeftMouseDown = false;

            float x, y;
            try { x = Hud.Window.CursorX; y = Hud.Window.CursorY; }
            catch { return false; }

            var layout = GetLayout();
            if (_visible && Contains(layout.Window, x, y))
                TryStartVisualClickAnimation(x, y);

            if (_visible && ShouldShowProfileCloseMask() && ContainsProfileCloseMask(layout.ProfileCloseMask, x, y))
            {
                // Bound Profile X-mask. Let Diablo's real Profile X button receive
                // the physical mouse-down. HUD MENU only tracks this so it can hide
                // after the real Profile window actually closes.
                _profileCloseMaskMouseDown = true;
                _profileCloseMaskReleasePending = false;
                _menuConsumedLeftMouseDown = false;
                return false;
            }

            if (_showDot && Contains(layout.Dot, x, y))
            {
                if (_editMode)
                {
                    _draggingDot = true;
                    _dragOffsetX = x - layout.Dot.X;
                    _dragOffsetY = y - layout.Dot.Y;
                }
                else
                {
                    if (_visible) CloseMenu(true);
                    else OpenMenu();

                    SaveSettings();
                }
                return ConsumeLeftMouseDown();
            }

            if (!_visible) return false;
            if (!Contains(layout.Window, x, y)) return false;

            if (_visible && Contains(layout.HideButton, x, y))
            {
                _hideButtonMouseDown = true;
                return ConsumeLeftMouseDown();
            }

            if (_editMode && Contains(layout.Title, x, y)
                && !Contains(layout.DotButton, x, y)
                && !Contains(layout.HotkeyLabel, x, y)
                && !Contains(layout.HotkeyButton, x, y)
                && !Contains(layout.MoveButton, x, y)
                && !Contains(layout.HideButton, x, y)
                && !Contains(layout.CloseHudButton, x, y))
            {
                _draggingWindow = true;
                _dragOffsetX = x - _windowRect.X;
                _dragOffsetY = y - _windowRect.Y;
                return ConsumeLeftMouseDown();
            }

            if (TryBeginScrollInteraction(layout, x, y))
                return ConsumeLeftMouseDown();

            HandleMenuClick(layout, x, y);
            return ConsumeLeftMouseDown();
        }

        public bool MouseUp(MouseButtons button)
        {
            if (button != MouseButtons.Left) return false;

            if (_profileCloseMaskMouseDown)
            {
                bool releaseOnProfileMask = false;

                try
                {
                    float x = Hud.Window.CursorX;
                    float y = Hud.Window.CursorY;
                    var layout = GetLayout();
                    releaseOnProfileMask = _visible && ShouldShowProfileCloseMask() && ContainsProfileCloseMask(layout.ProfileCloseMask, x, y);
                }
                catch { releaseOnProfileMask = false; }

                _profileCloseMaskMouseDown = false;
                _menuConsumedLeftMouseDown = false;

                if (releaseOnProfileMask)
                {
                    // Do not send Shift+P here. The physical mouse-up must pass
                    // through to Diablo's real Profile X button. HUD MENU will
                    // hide only after Profile is actually no longer visible.
                    _profileCloseMaskReleasePending = true;
                    _status = "NO-CLICK BACKGROUND: CLOSING BACKDROP";
                }

                return false;
            }

            if (_hideButtonMouseDown)
            {
                bool releaseOnHide = false;

                try
                {
                    float x = Hud.Window.CursorX;
                    float y = Hud.Window.CursorY;
                    var layout = GetLayout();
                    releaseOnHide = _visible && Contains(layout.HideButton, x, y);
                }
                catch
                {
                    releaseOnHide = false;
                }

                _hideButtonMouseDown = false;
                _menuConsumedLeftMouseDown = false;

                if (releaseOnHide)
                {
                    ReleaseLeftMouseForMenuBoundary();
                    CloseMenu(true);
                    SaveSettings();
                }

                return true;
            }

            bool wasPositionDrag = _draggingWindow || _draggingDot;
            bool wasScrollDrag = _draggingScrollThumb;
            bool wasAutoGemSpecificDrag = _draggingAutoGemSpecificScrollThumb;
            bool wasGlobalTtsDrag = _draggingGlobalTtsSlider;
            bool wasMacrosDrag = _draggingMacrosThumb;
            bool wasVisualDrag = _draggingVisualThumb;
            bool wasMainDrag = _draggingMainThumb;
            bool wasTtsDrag = _draggingTtsScrollThumb;

            if (wasPositionDrag || wasScrollDrag || wasAutoGemSpecificDrag || wasGlobalTtsDrag || wasMacrosDrag || wasVisualDrag || wasMainDrag || wasTtsDrag)
            {
                _draggingWindow = false;
                _draggingDot = false;
                _draggingScrollThumb = false;
                _draggingAutoGemSpecificScrollThumb = false;
                _draggingGlobalTtsSlider = false;
                _draggingMacrosThumb = false;
                _draggingVisualThumb = false;
                _draggingMainThumb = false;
                _draggingTtsScrollThumb = false;
                _menuConsumedLeftMouseDown = false;

                if (wasPositionDrag)
                {
                    ClampRectsToScreen();
                    MarkLayoutDirty();
                    SaveSettings();
                }

                if (wasAutoGemSpecificDrag || wasGlobalTtsDrag || wasMainDrag)
                {
                    if (wasGlobalTtsDrag)
                    {
                        ApplyGlobalTtsSettings();
                        RequestPluginCacheRefresh();
                    }
                    SaveSettings();
                }

                return true;
            }

            bool consumeMouseUp = _menuConsumedLeftMouseDown;
            _menuConsumedLeftMouseDown = false;
            return consumeMouseUp;
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Ground) return;
            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.Me == null) return;
            try { DrawVisualWorldOverlays(); } catch { }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip)
                return;

            EnsureResources();

            try
            {
                DrawVisualTopOverlays();
            }
            catch { }

            var layout = GetLayout();

            if (_showDot)
                DrawDot(layout.Dot);

            if (!_visible)
                return;

            DrawWindow(layout);
            DrawProfileCloseMask(layout);

            // Menu-only click animation. Draw last so it appears above the HUD Manager overlay.
            try
            {
                DrawVisualClickAnimation();
            }
            catch { }
        }

        private void HandleMenuClick(MenuLayout layout, float x, float y)
        {
            if (HandleHeaderClick(layout, x, y))
                return;

            if (HandleNoClickClick(layout, x, y))
                return;

            if (HandlePageTabClick(layout, x, y))
                return;

            if (HandleGlobalTopControlClick(layout, x, y))
                return;

            switch (_activePage)
            {
                case MenuPageTab.Main:
                    HandleMainClick(layout, x, y);
                    break;

                case MenuPageTab.Toggles:
                    HandleTogglesClick(layout, x, y);
                    break;

                case MenuPageTab.Plugins:
                    HandlePluginsClick(layout, x, y);
                    break;

                default:
                    _activePage = MenuPageTab.Main;
                    MarkRowsDirty();
                    SaveSettings();
                    break;
            }
        }

        private bool HandleHeaderClick(MenuLayout layout, float x, float y)
        {
            if (Contains(layout.HotkeyButton, x, y))
            {
                _capturingHotkey = true;
                _status = "PRESS NEW MENU HOTKEY OR ESC";
                SaveSettings();
                LogDebug("Hotkey capture started.");
                return true;
            }

            if (Contains(layout.MoveButton, x, y))
            {
                _editMode = !_editMode;
                _status = _editMode ? "MOVE MODE ON" : "MOVE MODE OFF";
                SaveSettings();
                LogDebug("Move mode changed. EditMode=" + _editMode + ".");
                return true;
            }

            if (Contains(layout.HideButton, x, y))
            {
                // HIDE is committed on MouseUp so it behaves like a real button.
                return true;
            }

            if (Contains(layout.CloseHudButton, x, y))
            {
                _status = "CLOSING TURBOHUD";
                SaveSettings();
                LogDebug("Close HUD requested.");
                TryCloseTurboHud();
                return true;
            }

            return false;
        }

        private bool HandleNoClickClick(MenuLayout layout, float x, float y)
        {
            if (Contains(layout.NoClickCheck, x, y) || Contains(layout.NoClickLabel, x, y))
            {
                ToggleNoClickBackground();
                return true;
            }

            return false;
        }

        private bool HandlePageTabClick(MenuLayout layout, float x, float y)
        {
            foreach (MenuPageTab page in MenuPageOrder)
            {
                RectangleF r;
                if (!layout.PageTabRects.TryGetValue(page, out r))
                    continue;

                if (!Contains(r, x, y))
                    continue;

                _activePage = page;
                if (_activePage == MenuPageTab.Plugins)
                    _activeTab = DefaultPluginsTabForPageEntry();
                _scroll = 0;
                MarkRowsDirty();
                RequestPluginCacheRefresh();
                _status = "PAGE: " + PageLabel(_activePage);
                SaveSettings();
                return true;
            }

            return false;
        }

        private bool HandleGlobalTopControlClick(MenuLayout layout, float x, float y)
        {
            string action = FindGlobalHit(x, y);
            if (string.IsNullOrWhiteSpace(action))
                return false;

            if (action == "global:tts-toggle")
            {
                _globalTtsEnabled = !_globalTtsEnabled;
                ApplyGlobalTtsSettings();
                RequestPluginCacheRefresh();
                _status = _globalTtsEnabled ? "GLOBAL TTS ENABLED" : "GLOBAL TTS DISABLED";
                SaveSettings();
                return true;
            }

            if (action == "global:tts-vol-")
            {
                _globalTtsVolume = ClampGlobalTtsVolume(_globalTtsVolume - GlobalTtsVolumeStep);
                _lastTtsVolMinusTick = Environment.TickCount;
                ApplyGlobalTtsSettings();
                RequestPluginCacheRefresh();
                _status = "GLOBAL TTS VOLUME: " + _globalTtsVolume.ToString(CultureInfo.InvariantCulture);
                SaveSettings();
                return true;
            }

            if (action == "global:tts-vol+")
            {
                _globalTtsVolume = ClampGlobalTtsVolume(_globalTtsVolume + GlobalTtsVolumeStep);
                _lastTtsVolPlusTick = Environment.TickCount;
                ApplyGlobalTtsSettings();
                RequestPluginCacheRefresh();
                _status = "GLOBAL TTS VOLUME: " + _globalTtsVolume.ToString(CultureInfo.InvariantCulture);
                SaveSettings();
                return true;
            }

            if (action == "global:tts-slider" || action == "global:tts-slider-thumb")
            {
                bool changed = SetGlobalTtsVolumeFromCursorX(x);

                // Allow click-and-drag from either the thumb or the track.
                _draggingGlobalTtsSlider = true;

                if (changed)
                {
                    ApplyGlobalTtsSettings();
                    RequestPluginCacheRefresh();
                }

                _status = "GLOBAL TTS VOLUME: " + _globalTtsVolume.ToString(CultureInfo.InvariantCulture);

                if (changed)
                    SaveSettings();

                return true;
            }

            return false;
        }

        private int ClampGlobalTtsVolume(int value)
        {
            if (value < GlobalTtsVolumeMin) return GlobalTtsVolumeMin;
            if (value > GlobalTtsVolumeMax) return GlobalTtsVolumeMax;
            return value;
        }

        private bool SetGlobalTtsVolumeFromCursorX(float cursorX)
        {
            if (_globalTtsSliderTrack.Width <= 0f)
                return false;

            int oldVolume = _globalTtsVolume;

            float t = (cursorX - _globalTtsSliderTrack.Left) / _globalTtsSliderTrack.Width;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            _globalTtsVolume = ClampGlobalTtsVolume((int)Math.Round(t * 100f));

            return _globalTtsVolume != oldVolume;
        }


        private void ApplyGlobalTtsSettings()
        {
            try
            {
                _globalTtsVolume = ClampGlobalTtsVolume(_globalTtsVolume);

                if (Hud != null && Hud.Sound != null)
                {
                    Hud.Sound.IsSpeakEnabled = _globalTtsEnabled;
                    Hud.Sound.VolumeMode = global::Turbo.Plugins.VolumeMode.Constant;
                    Hud.Sound.ConstantVolume = _globalTtsVolume;
                }

                ApplyGlobalTtsSettingsToPlugins();
            }
            catch
            {
                _status = "GLOBAL TTS APPLY FAILED";
            }
        }

        private void ApplyGlobalTtsSettingsToPlugins()
        {
            try
            {
                if (Hud == null || Hud.AllPlugins == null)
                    return;

                foreach (var plugin in Hud.AllPlugins)
                {
                    if (plugin == null || object.ReferenceEquals(plugin, this))
                        continue;

                    try
                    {
                        var method = plugin.GetType().GetMethod(
                            "SetGlobalTtsSettings",
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new Type[] { typeof(bool), typeof(int) },
                            null);

                        if (method != null)
                            method.Invoke(plugin, new object[] { _globalTtsEnabled, _globalTtsVolume });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private IPlugin GetZBarbAutoSnapPlugin()
        {
            return FindPluginByTypeName(ZBarbAutoSnapPluginTypeNames);
        }

        private void ApplyZBarbAutoSnapHotkeyToPlugin()
        {
            try
            {
                IPlugin plugin = GetZBarbAutoSnapPlugin();
                if (plugin == null)
                    return;

                var type = plugin.GetType();

                var method = type.GetMethod(
                    "SetHotkey",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Key) },
                    null);

                if (method != null)
                {
                    method.Invoke(plugin, new object[] { _zbarbAutoSnapHotkey });
                    return;
                }

                var prop = type.GetProperty(
                    "Hotkey",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(Key))
                    prop.SetValue(plugin, _zbarbAutoSnapHotkey, null);
            }
            catch
            {
            }
        }

        private string GetZBarbAutoSnapHotkeyLabel()
        {
            return CaptureKeyLabel(_zbarbAutoSnapHotkey);
        }

        private void InitHudHotkeyState()
        {
            _hudHotkeyMod = (string[])HotkeyDefMod.Clone();
            _hudHotkeyKey = (string[])HotkeyDefKey.Clone();

            // Try to load current values from hotkeys.xml
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "hotkeys.xml");
                if (!File.Exists(path)) return;

                string content = File.ReadAllText(path);
                for (int i = 0; i < HotkeyIds.Length; i++)
                {
                    // Find <action_name modifier="..." key="..." />
                    string tag = "<" + HotkeyIds[i] + " ";
                    int tagIdx = content.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                    if (tagIdx < 0) continue;

                    int end = content.IndexOf("/>", tagIdx);
                    if (end < 0) continue;

                    string elem = content.Substring(tagIdx, end - tagIdx + 2);

                    string mod = ExtractXmlAttr(elem, "modifier");
                    string key = ExtractXmlAttr(elem, "key");

                    if (key != null) _hudHotkeyKey[i] = key;
                    if (mod != null) _hudHotkeyMod[i] = mod;
                }
            }
            catch { }
        }

        private static string ExtractXmlAttr(string elem, string attr)
        {
            // Use (char)34 for literal double-quote to avoid Python string-escape issues
            char q = (char)34;
            string search = attr + "=" + q;
            int idx = elem.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int start = idx + search.Length;
            int end   = elem.IndexOf(q, start);
            if (end < 0) return null;
            return elem.Substring(start, end - start);
        }

        private static string KeyToHotkeyString(Key k)
        {
            // Map SharpDX.DirectInput.Key to TurboHUD hotkeys.xml key names
            string name = k.ToString();
            // Handle letter keys: D0..D9, A..Z
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
                return name.Substring(1); // "D5" → "5"
            // Function keys pass through as-is: F1..F12
            // Letter keys pass through as-is: A..Z
            // Special keys
            switch (k)
            {
                case Key.Return:     return "Return";
                case Key.Escape:     return null; // means cancel
                case Key.Space:      return "Space";
                case Key.Back:       return "BackSpace";
                case Key.Tab:        return "Tab";
                case Key.Home:       return "Home";
                case Key.End:        return "End";
// Key.Prior / Key.Next not present in this SharpDX build — handled by default case
                case Key.Insert:     return "Insert";
                case Key.Delete:     return "Delete";
                case Key.Up:         return "Up";
                case Key.Down:       return "Down";
                case Key.Left:       return "Left";
                case Key.Right:      return "Right";
                case Key.LeftShift:
                case Key.RightShift:
                case Key.LeftControl:
                case Key.RightControl:
                case Key.LeftAlt:
                case Key.RightAlt:
                    return null; // modifiers alone don't count as the key
                default:
                    // For letter/function keys the ToString() is already correct
                    if (name.Length == 1 && char.IsLetter(name[0]))
                        return name.ToUpperInvariant();
                    if (name.StartsWith("F") && name.Length <= 3)
                        return name; // F1..F12
                    return name; // best effort
            }
        }

        private void ClaimAutoGemUiOwnership()
        {
            try
            {
                s7o_AutoGemUpgradeState.ClaimUiOwnership(AutoGemUiOwnerName);
                LogDebug("Auto Gem UI ownership claimed by HUD Menu.");
            }
            catch { }
        }

        private void SaveAutoGemState()
        {
            try
            {
                s7o_AutoGemUpgradeState.AutoGemTPDelayMs =
                    s7o_AutoGemUpgradeState.ClampTPDelayMs(s7o_AutoGemUpgradeState.AutoGemTPDelayMs);

                s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining =
                    s7o_AutoGemUpgradeState.ClampTPAnchorRemaining(s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining);

                if (s7o_AutoGemUpgradeState.AutoGemMode < 0)
                    s7o_AutoGemUpgradeState.AutoGemMode = 0;

                if (s7o_AutoGemUpgradeState.AutoGemMode > 4)
                    s7o_AutoGemUpgradeState.AutoGemMode = 4;

                if (s7o_AutoGemUpgradeState.AutoGemSpecificSubMode < 0)
                    s7o_AutoGemUpgradeState.AutoGemSpecificSubMode = 0;

                if (s7o_AutoGemUpgradeState.AutoGemSpecificSubMode > 1)
                    s7o_AutoGemUpgradeState.AutoGemSpecificSubMode = 1;

                if (string.IsNullOrWhiteSpace(s7o_AutoGemUpgradeState.AutoGemSpecificName))
                    s7o_AutoGemUpgradeState.AutoGemSpecificName = "Bane of the Trapped";

                s7o_AutoGemUpgradeState.RequestSettingsSave();
            }
            catch { }

            SaveSettings();
        }

        private void RegisterMainHit(string action, RectangleF rect)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            _mainHitRects[action] = rect;
        }

        private string FindMainHit(float x, float y)
        {
            // The Auto Gem specific-list thumb overlaps the scroll track,
            // so prioritize it before the generic dictionary scan.
            RectangleF priorityRect;
            if (_mainHitRects.TryGetValue("autogem:specific-scroll-thumb", out priorityRect) &&
                Contains(priorityRect, x, y))
            {
                return "autogem:specific-scroll-thumb";
            }

            foreach (var kv in _mainHitRects)
            {
                if (Contains(kv.Value, x, y))
                    return kv.Key;
            }

            return null;
        }

        private void RegisterToggleHit(string action, RectangleF rect)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            _togglesHitRects.Add(new ToggleHitRect(action, rect));
        }

        private string FindToggleHit(float x, float y)
        {
            // Search from last to first so the most recently drawn / top-most control wins.
            for (int i = _togglesHitRects.Count - 1; i >= 0; i--)
            {
                ToggleHitRect hit = _togglesHitRects[i];

                if (Contains(hit.Rect, x, y))
                    return hit.Action;
            }

            return null;
        }


        private void RegisterGlobalHit(string action, RectangleF rect)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            _globalHitRects[action] = rect;
        }

        private string FindGlobalHit(float x, float y)
        {
            RectangleF thumb;
            if (_globalHitRects.TryGetValue("global:tts-slider-thumb", out thumb) &&
                Contains(thumb, x, y))
            {
                return "global:tts-slider-thumb";
            }

            foreach (var kv in _globalHitRects)
            {
                if (Contains(kv.Value, x, y))
                    return kv.Key;
            }

            return null;
        }


        private bool HandleSharedPluginToggleAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            if (action == "main:plugin:itemsalvage" || action == "toggles:plugin:itemsalvage")
                return TogglePluginByTypeName("ITEM SALVAGE", "s7o_ItemSalvage");

            if (action == "main:plugin:mystic" || action == "toggles:plugin:mystic")
                return TogglePluginByTypeName("MYSTIC ENCHANT", "s7o_MysticEnchantPlugin");

            if (action == "main:plugin:turbocube" || action == "toggles:plugin:turbocube" || action == "main:plugin:kanaicube" || action == "toggles:plugin:kanaicube")
                return TogglePluginByTypeName("KANAI CUBE", "s7o_KanaiCube");

            if (action == "main:plugin:autoskill" || action == "toggles:plugin:autoskill")
                return TogglePluginByTypeName("AUTOSKILL", "s7o_AutoSkill");

            if (action == "main:plugin:dhstrafe" || action == "toggles:plugin:dhstrafe")
                return TogglePluginByTypeName("DH STRAFE", "s7o_DHStrafePrimaryPlugin", "s7o_DHStrafe");

            if (action == "toggles:plugin:zbarbautosnap")
            {
                bool ok = TogglePluginByTypeName("Z-BARB AUTOSNAP", ZBarbAutoSnapPluginTypeNames);
                ApplyZBarbAutoSnapHotkeyToPlugin();
                return ok;
            }

            if (action == "main:plugin:exitarchon" || action == "toggles:plugin:exitarchon")
                return TogglePluginByTypeName("EXIT ARCHON", "s7o_ExitArchon");

            if (action == "main:plugin:pylons" || action == "toggles:plugin:pylons" )
                return TogglePluginByTypeName("PYLON ALERTS", "s7o_PylonAlerts", "PylonAlerts", "PylonAlertsPlugin", "OtherShrinePlugin");

            if (action == "main:plugin:banner" || action == "toggles:plugin:banner" )
                return TogglePluginByTypeName("BANNER SOUND", "s7o_BannerSoundPlugin", "BannerSoundPlugin");

            if (action == "main:plugin:ttsbroadcast" || action == "toggles:plugin:ttsbroadcast")
                return TogglePluginByTypeName("TTS BROADCAST", "s7o_TTS_Broadcast");

            if (action == "main:plugin:party" || action == "toggles:plugin:party")
                return TogglePluginByTypeName("PARTY INSPECTOR", "s7o_PartyInspector");

            if (action == "toggles:plugin:elitebars" || action == "toggles:plugin:elitehp")
                return TogglePluginByTypeName("ELITE HEALTH BARS", "s7o_EliteHealthBars");

            if (action == "toggles:plugin:oculus")
                return TogglePluginByTypeName("OCULUS / TRIUNE", "s7o_Oculus", "s7o_Oculus_Triune", "OculusTriune", "OculusTriunePlugin");

            return false;
        }

        private bool HandleMainNavigationAction(string action)
        {
            if (action == "main:go:toggles")
            {
                _activePage = MenuPageTab.Toggles;
                _scroll = 0;
                MarkRowsDirty();
                RequestPluginCacheRefresh();
                _status = "PAGE: TOGGLES";
                SaveSettings();
                return true;
            }

            if (action == "main:go:plugins")
            {
                _activePage = MenuPageTab.Plugins;
                _activeTab = DefaultPluginsTabForPageEntry();
                _scroll = 0;
                MarkRowsDirty();
                RequestPluginCacheRefresh();
                _status = "PAGE: PLUGINS";
                SaveSettings();
                return true;
            }

            return false;
        }


        private void HandleMainClick(MenuLayout layout, float x, float y)
        {
            string action = FindMainHit(x, y);
            if (string.IsNullOrWhiteSpace(action))
                return;

            if (HandleMainNavigationAction(action))
                return;

            if (HandleSharedPluginToggleAction(action))
                return;

            if (action.StartsWith("autosnap:", StringComparison.OrdinalIgnoreCase))
            {
                HandleAutoSnapMainAction(action);
                return;
            }

            if (action.StartsWith("hotkeys:", StringComparison.OrdinalIgnoreCase))
            {
                HandleHotkeysMainAction(action);
                return;
            }

            HandleAutoGemMainAction(action, x, y);
        }

        private void HandleHotkeysMainAction(string action)
        {
            if (action == "hotkeys:expand")
            {
                _hotkeysExpanded = !_hotkeysExpanded;
                _status = _hotkeysExpanded ? "HOTKEYS PANEL OPEN" : "HOTKEYS PANEL CLOSED";
                SaveSettings();
                return;
            }

            if (action == "hotkeys:reset")
            {
                // Reset runtime values to defaults then write xml
                if (_hudHotkeyMod != null) for (int i = 0; i < _hudHotkeyMod.Length && i < HotkeyDefMod.Length; i++) _hudHotkeyMod[i] = HotkeyDefMod[i];
                if (_hudHotkeyKey != null) for (int i = 0; i < _hudHotkeyKey.Length && i < HotkeyDefKey.Length; i++) _hudHotkeyKey[i] = HotkeyDefKey[i];
                WriteDefaultHotkeysXml();
                return;
            }

            // hotkeys:key:N — start capture for hotkey slot N
            if (action.StartsWith("hotkeys:key:", StringComparison.OrdinalIgnoreCase))
            {
                string raw = action.Substring("hotkeys:key:".Length);
                int idx;
                if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out idx)
                    && idx >= 0 && idx < HotkeyIds.Length)
                {
                    _capturingHudHotkeyIdx = idx;
                    _status = "PRESS KEY FOR: " + HotkeyLabels[idx] + "  (ESC to cancel)";
                }
                return;
            }
        }

        private void HandleAutoSnapMainAction(string action)
        {
            string[] p = action.Split(':');

            if (p.Length < 2)
                return;

            if (action == "autosnap:expand")
            {
                _autoSnapExpanded = !_autoSnapExpanded;
                SaveSettings();
                return;
            }

            if (action == "autosnap:restore")
            {
                _autoSnapRestoreCursor = !_autoSnapRestoreCursor;

                if (!_autoSnapRestoreCursor)
                    AnySnapClearAllRestoreState();

                SaveSettings();
                return;
            }

            if (action == "autosnap:ignore-jug")
            {
                _autoSnapIgnoreJuggernauts = !_autoSnapIgnoreJuggernauts;
                SaveSettings();
                return;
            }

            if (action == "autosnap:ignore-minion")
            {
                _autoSnapIgnoreMinions = !_autoSnapIgnoreMinions;
                SaveSettings();
                return;
            }

            if (action == "autosnap:hotkeys-enabled")
            {
                _asHotkeysEnabled = !_asHotkeysEnabled;
                SaveSettings();
                return;
            }

            if (action == "autosnap:hotkeys-only")
            {
                _asHotkeysOnly = !_asHotkeysOnly;
                SaveSettings();
                return;
            }

            if (action == "autosnap:hotkeys-expand")
            {
                _autoSnapHotkeysExpanded = !_autoSnapHotkeysExpanded;
                SaveSettings();
                return;
            }

            if (action == "autosnap:left-stand")
            {
                _autoSnapLeftClickForceStandStill = !_autoSnapLeftClickForceStandStill;
                SaveSettings();
                return;
            }

            if (p.Length >= 3 && p[1] == "slot")
            {
                int idx;

                if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                    ToggleAnyAutoSnapSlot(idx);

                SaveSettings();
                return;
            }

            if (p.Length >= 3 && p[1] == "hotkey")
            {
                int idx;

                if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                    idx >= 0 && idx < _asHotkeyCaptureActive.Length)
                {
                    ClearAnySnapHotkeyCapture();
                    _asHotkeyCaptureActive[idx] = true;
                    _status = "PRESS AUTOSNAP HOTKEY FOR " + _autoSnapLabels[idx];
                }

                return;
            }

            if (p.Length >= 3 && p[1] == "mode")
            {
                int mode;

                if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
                    _autoSnapMode = Math.Max(0, Math.Min(1, mode));

                SaveSettings();
                return;
            }

            if (p.Length >= 3 && p[1] == "melee")
            {
                int delta = p[2] == "-1" ? -5 : 5;
                _autoSnapMeleeRangeYards = Math.Max(0, Math.Min(80, _autoSnapMeleeRangeYards + delta));
                SaveSettings();
                return;
            }

            if (p.Length >= 3 && p[1] == "ranged")
            {
                int delta = p[2] == "-1" ? -5 : 5;
                _autoSnapRangedRangeYards = Math.Max(0, Math.Min(80, _autoSnapRangedRangeYards + delta));
                SaveSettings();
                return;
            }
        }

        private bool IsCaptureCancelKey(Key key)
        {
            return key == Key.Escape;
        }

        private void HandleAutoGemMainAction(string action, float x, float y)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            try
            {
                if (action == "autogem:toggle")
                {
                    s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled = !s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled;
                    _status = s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled ? "AUTO GEM ENABLED" : "AUTO GEM DISABLED";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:expand")
                {
                    _autoGemExpanded = !_autoGemExpanded;
                    _status = _autoGemExpanded ? "AUTO GEM PANEL EXPANDED" : "AUTO GEM PANEL COLLAPSED";
                    SaveSettings();
                    return;
                }

                if (action.StartsWith("autogem:mode:", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = action.Substring("autogem:mode:".Length);
                    int mode;
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
                    {
                        if (mode < 0) mode = 0;
                        if (mode > 4) mode = 4;

                        s7o_AutoGemUpgradeState.AutoGemMode = mode;

                        if (mode != 4)
                        {
                            _autoGemSpecificExpanded = false;  // always collapse when switching away from SPECIFIC
                        }

                        _status = "AUTO GEM MODE: " + AutoGemModeText(mode);
                        SaveAutoGemState();
                    }
                    return;
                }

                if (action == "autogem:anchor3")
                {
                    s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = 3;
                    _status = "AUTO GEM TP ANCHOR: 3RD GEM";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:anchor4")
                {
                    s7o_AutoGemUpgradeState.AutoGemTPAnchorRemaining = 4;
                    _status = "AUTO GEM TP ANCHOR: 4TH GEM";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:delay-")
                {
                    int step = s7o_AutoGemUpgradeState.AutoGemTPDelayStep;
                    s7o_AutoGemUpgradeState.AutoGemTPDelayMs =
                        Math.Max(
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMin,
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMs - step);

                    _status = "AUTO GEM TP DELAY: " + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture) + "MS";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:delay+")
                {
                    int step = s7o_AutoGemUpgradeState.AutoGemTPDelayStep;
                    s7o_AutoGemUpgradeState.AutoGemTPDelayMs =
                        Math.Min(
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMax,
                            s7o_AutoGemUpgradeState.AutoGemTPDelayMs + step);

                    _status = "AUTO GEM TP DELAY: " + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture) + "MS";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:lag")
                {
                    s7o_AutoGemUpgradeState.AutoGemTPLagBoost = !s7o_AutoGemUpgradeState.AutoGemTPLagBoost;
                    _status = s7o_AutoGemUpgradeState.AutoGemTPLagBoost ? "AUTO GEM LAG BOOST ON" : "AUTO GEM LAG BOOST OFF";
                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:specific-list")
                {
                    // Opening the gem name list also switches to SPECIFIC mode
                    if (!_autoGemSpecificExpanded)
                    {
                        _autoGemSpecificExpanded = true;
                        s7o_AutoGemUpgradeState.AutoGemMode = 4;  // force SPECIFIC mode
                        EnsureAutoGemSpecificScrollIncludesCurrentGem();
                        _status = "AUTO GEM: SPECIFIC MODE";
                        SaveSettings();
                    }
                    else
                    {
                        // Already open — clicking again closes it
                        _autoGemSpecificExpanded = false;
                        _status = "AUTO GEM SPECIFIC LIST CLOSED";
                        SaveSettings();
                    }
                    return;
                }

                if (action == "autogem:specific-list-button")
                {
                    // [+/-] expand button: toggles the list
                    _autoGemSpecificExpanded = !_autoGemSpecificExpanded;
                    _status = _autoGemSpecificExpanded ? "AUTO GEM SPECIFIC LIST OPEN" : "AUTO GEM SPECIFIC LIST CLOSED";
                    if (_autoGemSpecificExpanded) EnsureAutoGemSpecificScrollIncludesCurrentGem();
                    SaveSettings();
                    return;
                }

                if (action == "autogem:specific-submode")
                {
                    s7o_AutoGemUpgradeState.AutoGemSpecificSubMode =
                        s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1 ? 0 : 1;

                    _status = s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1
                        ? "AUTO GEM SPECIFIC: HIGHEST"
                        : "AUTO GEM SPECIFIC: AUTO";

                    SaveAutoGemState();
                    return;
                }

                if (action == "autogem:specific-scroll-up")
                {
                    _autoGemSpecificScroll = ClampAutoGemSpecificScroll(_autoGemSpecificScroll - 1);
                    _lastAutoGemSpecificScrollUpTick = Environment.TickCount;
                    SaveSettings();
                    return;
                }

                if (action == "autogem:specific-scroll-down")
                {
                    _autoGemSpecificScroll = ClampAutoGemSpecificScroll(_autoGemSpecificScroll + 1);
                    _lastAutoGemSpecificScrollDownTick = Environment.TickCount;
                    SaveSettings();
                    return;
                }

                if (action == "autogem:specific-scroll-track")
                {
                    JumpAutoGemSpecificScrollToCursorY(y);
                    _lastAutoGemSpecificScrollTrackTick = Environment.TickCount;
                    SaveSettings();
                    return;
                }

                if (action == "autogem:specific-scroll-thumb")
                {
                    _draggingAutoGemSpecificScrollThumb = true;
                    _autoGemSpecificScrollDragOffsetY = y - _autoGemSpecificScrollThumb.Top;
                    _lastAutoGemSpecificScrollTrackTick = Environment.TickCount;
                    return;
                }

                if (action.StartsWith("autogem:specific:", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = action.Substring("autogem:specific:".Length);
                    int idx;
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                    {
                        if (idx >= 0 && idx < AutoGemNames.Length)
                        {
                            s7o_AutoGemUpgradeState.AutoGemSpecificName = AutoGemNames[idx];
                            s7o_AutoGemUpgradeState.AutoGemMode = 4;
                            _autoGemSpecificExpanded = false;
                            _status = "AUTO GEM SPECIFIC: " + AutoGemNames[idx];
                            SaveAutoGemState();
                        }
                    }
                    return;
                }

            }
            catch
            {
                _status = "AUTO GEM ACTION FAILED";
            }
        }

        private void HandleTogglesClick(MenuLayout layout, float x, float y)
        {
            string action = FindToggleHit(x, y);
            if (string.IsNullOrWhiteSpace(action))
                return;

            if (action.StartsWith("toggles:cat:", StringComparison.OrdinalIgnoreCase))
            {
                string raw = action.Substring("toggles:cat:".Length);
                int iv;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv) &&
                    Enum.IsDefined(typeof(ToggleCategory), iv))
                {
                    _activeToggleCategory = (ToggleCategory)iv;
                    if (_activeToggleCategory != ToggleCategory.Macros)
                        _openGrMapsExpanded = false;
                    _toggleDetailScroll = 0;
                    _macrosScroll = 0;
                    _visualScroll = 0;
                    _ttsAlertsScrollPx = 0;
                    _draggingTtsScrollThumb = false;
                    _autoSkillPlugin = null;
                    _status = "TOGGLES: " + ToggleCategoryLabel(_activeToggleCategory);
                    MarkRowsDirty();
                    RequestPluginCacheRefresh();
                    SaveSettings();
                }
                return;
            }


            if (action.StartsWith("affix:", StringComparison.OrdinalIgnoreCase))
            {
                HandleAffixVisualAction(action);
                return;
            }

            if (action.StartsWith("riftmap:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("riftmaps:", StringComparison.OrdinalIgnoreCase))
            {
                HandleRiftMapAction(action);
                return;
            }

            if (action.StartsWith("visual:", StringComparison.OrdinalIgnoreCase))
            {
                if (IsVisualButtonFlashAction(action))
                    _visualButtonFlashTicks[action] = Environment.TickCount;

                HandleVisualAction(action);
                return;
            }

            if (action.StartsWith("tts:", StringComparison.OrdinalIgnoreCase))
            {
                HandleTtsCustomMessageAction(action);
                return;
            }

            if (action == "autoskill:keybinds:expand")
            {
                _autoSkillKeybindsExpanded = !_autoSkillKeybindsExpanded;
                _autoSkillKeybindCaptureSlot = -1;
                _status = _autoSkillKeybindsExpanded ? "AUTOSKILL KEYBINDS EXPANDED" : "AUTOSKILL KEYBINDS COLLAPSED";
                SaveSettings();
                return;
            }

            if (action.StartsWith("autoskill:keybind:", StringComparison.OrdinalIgnoreCase))
            {
                int slot;
                if (int.TryParse(action.Substring("autoskill:keybind:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                    BeginAutoSkillKeybindCapture(slot);

                return;
            }

            if (action == "macro:hotkey:zbarbautosnap")
            {
                _zbarbAutoSnapHotkeyCapture = true;
                _status = "PRESS Z-BARB AUTOSNAP HOTKEY";
                return;
            }

            if (action.StartsWith("macro:toggle:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = action.Substring("macro:toggle:".Length);
                // Format: "conditional:CODE" or "buff:CODE"
                bool isBuff = rest.StartsWith("buff:", StringComparison.OrdinalIgnoreCase);
                string code = isBuff ? rest.Substring(5) : (rest.StartsWith("conditional:", StringComparison.OrdinalIgnoreCase) ? rest.Substring(12) : rest);
                ToggleMacro(code, isBuff);
                return;
            }

            if (action.StartsWith("macro:star:", StringComparison.OrdinalIgnoreCase))
            {
                string code = action.Substring("macro:star:".Length);
                if (!_macroFavorites.Remove(code))
                    _macroFavorites.Add(code);
                SaveSettings();
                return;
            }

            // Flash for plugin toggles triggered from macros rows
            if (action.StartsWith("toggles:plugin:", StringComparison.OrdinalIgnoreCase))
                _macroToggleFlashTicks[action] = Environment.TickCount;

            HandleSharedPluginToggleAction(action);
        }



                private void HandleAffixVisualAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            _visualButtonFlashTicks[action] = Environment.TickCount;

            switch (action)
            {
                case "affix:plagued":
                    _affixPlagued = !_affixPlagued;
                    break;

                case "affix:arcanespawn":
                    _affixArcaneSpawn = !_affixArcaneSpawn;
                    break;

                case "affix:moltenexplosion":
                    _affixMoltenExplosion = !_affixMoltenExplosion;
                    break;

                case "affix:desecrator":
                    _affixDesecrator = !_affixDesecrator;
                    break;

                case "affix:thunderstorm":
                    _affixThunderstorm = !_affixThunderstorm;
                    break;

                case "affix:frozen":
                    _affixFrozen = !_affixFrozen;
                    break;

                case "affix:frozenpulse":
                    _affixFrozenPulse = !_affixFrozenPulse;
                    break;

                case "affix:ghom":
                    _affixGhomGas = !_affixGhomGas;
                    break;

                case "affix:grotesque":
                    _explosiveGrotesque = !_explosiveGrotesque;
                    break;

                case "affix:orbiter":
                    _affixOrbiter = !_affixOrbiter;
                    break;

                case "affix:waller":
                    _affixWaller = !_affixWaller;
                    break;

                case "affix:wormholemines":
                    _affixWormholeMines = !_affixWormholeMines;
                    break;

                case "affix:bossfallingrocks":
                    _affixBossFallingRocks = !_affixBossFallingRocks;
                    break;

                case "affix:bossleaptelegraphs":
                    _affixBossLeapTelegraphs = !_affixBossLeapTelegraphs;
                    break;

                case "affix:bossmeteors":
                    _affixBossMeteors = !_affixBossMeteors;
                    break;

                case "affix:bosskullehazards":
                    _affixBossKulleHazards = !_affixBossKulleHazards;
                    break;

                case "affix:bossblighterhazards":
                    _affixBossBlighterHazards = !_affixBossBlighterHazards;
                    break;

                case "affix:bossratkinghazards":
                    _affixBossRatKingHazards = !_affixBossRatKingHazards;
                    break;

                case "affix:bossadriageysers":
                    _affixBossAdriaGeysers = !_affixBossAdriaGeysers;
                    break;

                case "affix:bossbutcherfire":
                    _affixBossButcherFire = !_affixBossButcherFire;
                    break;

                case "affix:bossrimehazards":
                    _affixBossRimeHazards = !_affixBossRimeHazards;
                    break;

                case "affix:shocktowerhazards":
                    _affixShockTowerHazards = !_affixShockTowerHazards;
                    break;

                case "affix:bosssandmonsterturret":
                    _affixBossSandmonsterTurretHazards = !_affixBossSandmonsterTurretHazards;
                    break;

                case "affix:labels":
                    _eliteAffixLabelsEnabled = !_eliteAffixLabelsEnabled;
                    break;

                case "affix:dangerlabels":
                    _dangerousMonsterLabelsEnabled = !_dangerousMonsterLabelsEnabled;
                    break;

                default:
                    return;
            }

            ApplyMonsterVisualToggles();
            SaveSettings();
        }



        private void HandleRiftMapAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            if (action == "riftmaps:expand")
            {
                _openGrMapsExpanded = !_openGrMapsExpanded;
                _status = _openGrMapsExpanded ? "OPEN GR MAPS EXPANDED" : "OPEN GR MAPS COLLAPSED";
                return;
            }

            if (action.StartsWith("riftmap:", StringComparison.OrdinalIgnoreCase))
            {
                string key = action.Substring("riftmap:".Length);
                RiftMapGroup group = null;

                for (int i = 0; i < RiftMapGroups.Length; i++)
                {
                    if (RiftMapGroups[i] != null &&
                        string.Equals(RiftMapGroups[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        group = RiftMapGroups[i];
                        break;
                    }
                }

                if (group == null)
                    return;

                bool enabled = !RiftMapGroupEnabled(group);
                SetRiftMapGroupEnabled(group, enabled);
                _status = "OPEN GR: " + group.Title + (enabled ? " selected" : " unselected");
            }
        }

        private void HandleTtsCustomMessageAction(string action)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(action))
                    return;

                if (action == "tts:expand")
                {
                    _ttsBroadcastExpanded = !_ttsBroadcastExpanded;
                    SeedDefaultTtsCustomMessagesIfNeeded();
                    EnsureAtLeastOneTtsCustomMessageRowIfExpanded();
                    if (!_ttsBroadcastExpanded)
                    {
                        _ttsCustomMessageEditIndex = -1;
                        _ttsCustomMessageHotkeyCaptureIndex = -1;
                        _draggingTtsScrollThumb = false;
                    }
                    SaveSettings();
                    return;
                }

                if (action == "tts:add")
                {
                    _ttsButtonFlashTicks["tts:add"] = Environment.TickCount;

                    if (_ttsCustomMessages.Count < MaxTtsCustomMessages)
                    {
                        _ttsCustomMessages.Add(new TtsCustomMessage());
                        SyncTtsCustomRuntimeLists();
                        // ADD only adds a row. It must not open the keyboard.
                        // The keyboard opens only when the user clicks the message text box.
                        _ttsCustomMessageHotkeyCaptureIndex = -1;
                        SaveSettings();
                    }
                    return;
                }

                if (action.StartsWith("tts:delete:", StringComparison.OrdinalIgnoreCase))
                {
                    int idx;
                    if (int.TryParse(action.Substring("tts:delete:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                        idx >= 0 && idx < _ttsCustomMessages.Count)
                    {
                        string deleteAction = "tts:delete:" + idx.ToString(CultureInfo.InvariantCulture);
                        _ttsButtonFlashTicks[deleteAction] = Environment.TickCount;

                        _ttsCustomMessages.RemoveAt(idx);
                        SyncTtsCustomRuntimeLists();
                        if (_ttsCustomMessageEditIndex == idx) _ttsCustomMessageEditIndex = -1;
                        else if (_ttsCustomMessageEditIndex > idx) _ttsCustomMessageEditIndex--;
                        if (_ttsCustomMessageHotkeyCaptureIndex == idx) _ttsCustomMessageHotkeyCaptureIndex = -1;
                        else if (_ttsCustomMessageHotkeyCaptureIndex > idx) _ttsCustomMessageHotkeyCaptureIndex--;
                        EnsureAtLeastOneTtsCustomMessageRowIfExpanded();
                        SyncTtsCustomRuntimeLists();
                        SaveSettings();
                    }
                    return;
                }

                if (action.StartsWith("tts:edit:", StringComparison.OrdinalIgnoreCase))
                {
                    int idx;
                    if (int.TryParse(action.Substring("tts:edit:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                        idx >= 0 && idx < _ttsCustomMessages.Count)
                    {
                        string editAction = "tts:edit:" + idx.ToString(CultureInfo.InvariantCulture);
                        _ttsButtonFlashTicks[editAction] = Environment.TickCount;

                        if (_ttsCustomMessageEditIndex == idx)
                        {
                            // Clicking the active message box again closes the keyboard, like DONE.
                            _ttsCustomMessageEditIndex = -1;
                            _ttsKeyboardShiftOnce = false;
                            _ttsPendingKeyboardAutoScroll = false;
                            SaveSettings();
                            return;
                        }

                        _ttsCustomMessageEditIndex = idx;
                        _ttsCustomMessageHotkeyCaptureIndex = -1;
                        _ttsPendingKeyboardAutoScroll = true;
                        SaveSettings();
                        return;
                    }
                    return;
                }

                if (action.StartsWith("tts:reset:", StringComparison.OrdinalIgnoreCase))
                {
                    int idx;
                    if (int.TryParse(action.Substring("tts:reset:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                        idx >= 0 && idx < _ttsCustomMessages.Count)
                    {
                        string resetAction = "tts:reset:" + idx.ToString(CultureInfo.InvariantCulture);
                        _ttsButtonFlashTicks[resetAction] = Environment.TickCount;
                        _ttsCustomMessages[idx].Hotkey = Key.Unknown;
                        if (_ttsCustomMessageHotkeyCaptureIndex == idx)
                            _ttsCustomMessageHotkeyCaptureIndex = -1;

                        _status = "TTS HOTKEY RESET";
                        SaveSettings();
                    }
                    return;
                }

                if (action.StartsWith("tts:hotkey:", StringComparison.OrdinalIgnoreCase))
                {
                    int idx;
                    if (int.TryParse(action.Substring("tts:hotkey:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                        idx >= 0 && idx < _ttsCustomMessages.Count)
                    {
                        string hotkeyAction = "tts:hotkey:" + idx.ToString(CultureInfo.InvariantCulture);
                        _ttsButtonFlashTicks[hotkeyAction] = Environment.TickCount;
                        _ttsCustomMessageHotkeyCaptureIndex = idx;
                        _ttsCustomMessageEditIndex = -1;
                        _status = "PRESS TTS MESSAGE HOTKEY";
                    }
                    return;
                }

                if (action.StartsWith("tts:key:", StringComparison.OrdinalIgnoreCase))
                {
                    _ttsButtonFlashTicks[action] = Environment.TickCount;
                    string token = action.Substring("tts:key:".Length);
                    HandleTtsKeyboardToken(token);
                    return;
                }
            }
            catch
            {
                _status = "TTS MESSAGE ACTION FAILED";
            }
        }

        private void HandleTtsKeyboardToken(string token)
        {
            if (_ttsCustomMessageEditIndex < 0 || _ttsCustomMessageEditIndex >= _ttsCustomMessages.Count)
                return;

            TtsCustomMessage msg = _ttsCustomMessages[_ttsCustomMessageEditIndex];
            if (msg == null)
                return;

            if (string.Equals(token, "DONE", StringComparison.OrdinalIgnoreCase))
            {
                _ttsCustomMessageEditIndex = -1;
                _ttsKeyboardShiftOnce = false;
                SaveSettings();
                return;
            }

            if (string.Equals(token, "SPACE", StringComparison.OrdinalIgnoreCase))
            {
                AppendTtsKeyboardText(msg, " ");
                if (_ttsKeyboardShiftOnce)
                    _ttsKeyboardShiftOnce = false;

                SaveSettings();
                return;
            }

            if (string.Equals(token, "CAPS", StringComparison.OrdinalIgnoreCase))
            {
                _ttsKeyboardCaps = !_ttsKeyboardCaps;
                return;
            }

            if (string.Equals(token, "SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                _ttsKeyboardShiftOnce = true;
                return;
            }

            if (string.Equals(token, "BKSP", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(msg.Text))
                    msg.Text = msg.Text.Substring(0, msg.Text.Length - 1);
                SaveSettings();
                return;
            }

            if (string.Equals(token, "CLR", StringComparison.OrdinalIgnoreCase))
            {
                msg.Text = string.Empty;
                SaveSettings();
                return;
            }

            bool upper = _ttsKeyboardCaps || _ttsKeyboardShiftOnce;
            string add = TtsKeyboardTokenToText(token, upper);
            if (_ttsKeyboardShiftOnce)
                _ttsKeyboardShiftOnce = false;

            if (string.IsNullOrEmpty(add))
                return;

            AppendTtsKeyboardText(msg, add);
            SaveSettings();
        }

        private void AppendTtsKeyboardText(TtsCustomMessage msg, string add)
        {
            if (msg == null || string.IsNullOrEmpty(add))
                return;

            string current = msg.Text ?? string.Empty;

            if (current.Length >= MaxTtsCustomMessageChars)
                return;

            msg.Text = current + add;

            if (msg.Text.Length > MaxTtsCustomMessageChars)
                msg.Text = msg.Text.Substring(0, MaxTtsCustomMessageChars);
        }

        private void HandlePluginsClick(MenuLayout layout, float x, float y)
        {
            foreach (var kv in layout.TabRects)
            {
                if (Contains(kv.Value, x, y))
                {
                    _activeTab = kv.Key;
                    _scroll = 0;
                    MarkRowsDirty();
                    SaveSettings();
                    return;
                }
            }

            string clickedKey = null;
            foreach (var kv in _lastHitRects)
            {
                if (Contains(kv.Value, x, y))
                {
                    clickedKey = kv.Key;
                    break;
                }
            }

            if (string.IsNullOrEmpty(clickedKey))
                return;

            string[] parts = clickedKey.Split('|');
            if (parts.Length != 2)
                return;

            string action = parts[0];
            string typeName = parts[1];

            var row = _plugins.FirstOrDefault(p =>
                string.Equals(p.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

            if (row == null || row.Plugin == null)
                return;

            if (action == "toggle")
            {
                bool target = !SafePluginEnabled(row.Plugin);
                SetPluginEnabledPersistentWithBoundCompanions(row.Plugin, target);
                _status = (target ? "ENABLED " : "DISABLED ") + row.DisplayName;
                SaveSettings();
                LogDebug("Plugin toggle requested. Plugin=" + row.FullName + ", target=" + target + ".");
                RefreshPluginCache(true);
                ApplyGlobalTtsSettings();
                return;
            }

            if (action == "star")
            {
                ToggleFavorite(row);
                _status = IsFavorite(row.TypeName)
                    ? "FAVORITED " + row.DisplayName
                    : "REMOVED FAVORITE " + row.DisplayName;

                SaveSettings();
                LogDebug("Favorite toggled. Plugin=" + row.FullName + ", favorite=" + IsFavorite(row.TypeName) + ".");
                RefreshPluginCache(true);
            }
        }

        private bool TryBeginScrollInteraction(MenuLayout layout, float x, float y)
        {
            if (layout == null) return false;

            // ── MAIN page: PLUGINS-style content scrollbar ───────────────────────
            if (_activePage == MenuPageTab.Main)
            {
                if (!_mainScrollUpRect.IsEmpty && Contains(_mainScrollUpRect, x, y))
                {
                    _lastMainScrollUpTick = Environment.TickCount;
                    ScrollMainBy(-1);
                    return true;
                }

                if (!_mainScrollDownRect.IsEmpty && Contains(_mainScrollDownRect, x, y))
                {
                    _lastMainScrollDownTick = Environment.TickCount;
                    ScrollMainBy(+1);
                    return true;
                }

                if (!_mainScrollThumbRect.IsEmpty && Contains(_mainScrollThumbRect, x, y))
                {
                    _draggingMainThumb = true;
                    _mainScrollDragOffsetY = y - _mainScrollThumbRect.Top;
                    return true;
                }

                if (!_mainScrollTrackRect.IsEmpty &&
                    _mainTotalItems > _mainVisibleItems &&
                    Contains(_mainScrollTrackRect, x, y))
                {
                    int mainMax = MaxMainScroll();

                    if (mainMax > 0)
                    {
                        RectangleF mainThumb = GetMainScrollThumbRect(_mainScrollTrackRect);
                        float mainTravel = Math.Max(1f, _mainScrollTrackRect.Height - mainThumb.Height);
                        float mainRatio = Clamp01((y - _mainScrollTrackRect.Top - mainThumb.Height * 0.5f) / mainTravel);

                        _mainScroll = (int)Math.Round(mainRatio * mainMax);
                        ClampMainScroll();
                        MarkLayoutDirty();
                    }

                    return true;
                }
            }

            // ── TOGGLES/VISUAL page scrollbar ────────────────────────────────────
            if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Visual)
            {
                if (!_visualScrollUpRect.IsEmpty && Contains(_visualScrollUpRect, x, y))
                {
                    _lastVisualScrollUpTick = Environment.TickCount;
                    ScrollVisualBy(-1);
                    return true;
                }
                if (!_visualScrollDownRect.IsEmpty && Contains(_visualScrollDownRect, x, y))
                {
                    _lastVisualScrollDownTick = Environment.TickCount;
                    ScrollVisualBy(+1);
                    return true;
                }
                if (!_visualScrollThumbRect.IsEmpty && Contains(_visualScrollThumbRect, x, y))
                {
                    _draggingVisualThumb = true;
                    _visualScrollDragOffsetY = y - _visualScrollThumbRect.Top;
                    return true;
                }
                if (!_visualScrollTrackRect.IsEmpty &&
                    _visualTotalItems > _visualVisibleItems &&
                    Contains(_visualScrollTrackRect, x, y))
                {
                    int vMax = MaxVisualScroll();
                    if (vMax > 0)
                    {
                        RectangleF vThumb = GetVisualScrollThumbRect(_visualScrollTrackRect);
                        float travel = Math.Max(1f, _visualScrollTrackRect.Height - vThumb.Height);
                        float ratio  = Clamp01((y - _visualScrollTrackRect.Top - vThumb.Height * 0.5f) / travel);
                        _visualScroll = (int)Math.Round(ratio * vMax);
                        ClampVisualScroll();
                        MarkLayoutDirty();
                    }
                    return true;
                }
                return false;
            }

            // ── TOGGLES/TTS ALERTS page: pixel scrollbar ────────────────────────
            if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.TtsAlerts)
            {
                if (!_ttsScrollUpRect.IsEmpty && Contains(_ttsScrollUpRect, x, y))
                {
                    _lastTtsScrollUpTick = Environment.TickCount;
                    ScrollTtsAlertsBy(-TtsScrollStepPx);
                    return true;
                }

                if (!_ttsScrollDownRect.IsEmpty && Contains(_ttsScrollDownRect, x, y))
                {
                    _lastTtsScrollDownTick = Environment.TickCount;
                    ScrollTtsAlertsBy(+TtsScrollStepPx);
                    return true;
                }

                if (!_ttsScrollThumbRect.IsEmpty && Contains(_ttsScrollThumbRect, x, y))
                {
                    _draggingTtsScrollThumb = true;
                    _ttsScrollDragOffsetY = y - _ttsScrollThumbRect.Top;
                    return true;
                }

                if (!_ttsScrollTrackRect.IsEmpty && MaxTtsAlertsScroll() > 0 && Contains(_ttsScrollTrackRect, x, y))
                {
                    JumpTtsAlertsScrollToCursor(y);
                    return true;
                }

                return false;
            }

            // ── TOGGLES/MACROS page: PLUGINS-style index scrollbar ───────────────
            if (_activePage == MenuPageTab.Toggles && _activeToggleCategory == ToggleCategory.Macros)
            {
                if (!_macrosScrollUpRect.IsEmpty && Contains(_macrosScrollUpRect, x, y))
                {
                    _lastMacrosScrollUpTick = Environment.TickCount;
                    ScrollMacrosBy(-1);
                    return true;
                }

                if (!_macrosScrollDownRect.IsEmpty && Contains(_macrosScrollDownRect, x, y))
                {
                    _lastMacrosScrollDownTick = Environment.TickCount;
                    ScrollMacrosBy(+1);
                    return true;
                }

                if (!_macrosScrollThumbRect.IsEmpty && Contains(_macrosScrollThumbRect, x, y))
                {
                    _draggingMacrosThumb = true;
                    _macrosScrollDragOffsetY = y - _macrosScrollThumbRect.Top;
                    return true;
                }

                if (!_macrosScrollTrackRect.IsEmpty &&
                    _macrosTotalItems > _macrosVisibleItems &&
                    Contains(_macrosScrollTrackRect, x, y))
                {
                    JumpMacrosScrollToCursor(y);
                    return true;
                }

                return false;
            }

            // ── PLUGINS page: list scrollbar ───────────────────────────────────────
            if (_activePage != MenuPageTab.Plugins) return false;

            int max = MaxScroll(layout);

            if (Contains(layout.ScrollUp, x, y))
            {
                _lastPluginsScrollUpTick = Environment.TickCount;
                ScrollBy(-1, layout);
                return true;
            }

            if (Contains(layout.ScrollDown, x, y))
            {
                _lastPluginsScrollDownTick = Environment.TickCount;
                ScrollBy(+1, layout);
                return true;
            }

            if (max <= 0) return false;

            RectangleF thumb = GetScrollThumbRect(layout, GetActiveRowsCached().Count, VisibleRowCount(layout));

            if (!thumb.IsEmpty && Contains(thumb, x, y))
            {
                _draggingScrollThumb = true;
                _scrollDragOffsetY = y - thumb.Top;
                return true;
            }

            if (Contains(layout.ScrollTrack, x, y))
            {
                JumpScrollToCursor(layout, y);
                return true;
            }

            return false;
        }


        private void RefreshPluginCache(bool force)
        {
            try
            {
                var source = Hud.AllPlugins == null
                    ? new List<IPlugin>()
                    : Hud.AllPlugins.Where(p => p != null && !object.ReferenceEquals(p, this)).ToList();

                _plugins.Clear();
                foreach (var plugin in source.OrderBy(p => ClassifyPlugin(p.GetType().Name)).ThenBy(p => p.GetType().Name))
                {
                    var type = plugin.GetType();
                    string typeName = type.Name ?? string.Empty;
                    string fullName = type.FullName ?? typeName;
                    var row = new PluginRow
                    {
                        Plugin = plugin,
                        TypeName = typeName,
                        FullName = fullName,
                        DisplayName = CleanDisplayName(typeName),
                        Category = ClassifyPlugin(typeName + " " + fullName),
                        IsFavorite = IsFavorite(typeName) || IsFavorite(fullName),
                        HiddenFromList = IsHiddenPluginListRow(typeName, fullName),
                    };
                    _plugins.Add(row);
                }

                ReconcileFavoritesWithPlugins();
                ApplyPersistedPluginStates();
                ApplyBoundPluginStatesFromPrimary();
                ApplyGlobalTtsSettings();
                ApplyZBarbAutoSnapHotkeyToPlugin();
                ApplyHudMenuRuntimeControlledSettings(force);
                MarkRowsDirty();
                ClampScroll();
            }
            catch { }
        }

        private ManagerTab DefaultPluginsTabForPageEntry()
        {
            return _favorites.Count > 0 ? ManagerTab.Favorites : ManagerTab.All;
        }

        private void NormalizePluginsTabForEmptyFavorites()
        {
            if (_activeTab == ManagerTab.Favorites && _favorites.Count == 0)
                _activeTab = ManagerTab.All;
        }

        private void ReconcileFavoritesWithPlugins()
        {
            foreach (var fav in _favorites)
            {
                if (fav == null || string.IsNullOrWhiteSpace(fav.TypeName)) continue;
                var row = _plugins.FirstOrDefault(p =>
                    string.Equals(p.TypeName, fav.TypeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FullName, fav.TypeName, StringComparison.OrdinalIgnoreCase));
                if (row != null)
                {
                    row.IsFavorite = true;
                    if (string.IsNullOrWhiteSpace(fav.DisplayName)) fav.DisplayName = row.DisplayName;
                }
            }
        }

        private void MarkLayoutDirty()
        {
            _layoutDirty = true;
        }

        private void MarkRowsDirty()
        {
            _activeRowsDirty = true;
            _layoutDirty = true;
        }

        private void RequestPluginCacheRefresh()
        {
            _pluginCacheRefreshRequested = true;
        }


        private List<PluginRow> GetActiveRowsCached()
        {
            if (!_activeRowsDirty)
                return _activeRows;

            _activeRows.Clear();

            if (_activePage != MenuPageTab.Plugins)
            {
                _activeRowsDirty = false;
                return _activeRows;
            }

            if (_activeTab == ManagerTab.Favorites)
            {
                foreach (var fav in _favorites)
                {
                    if (fav == null) continue;

                    var row = _plugins.FirstOrDefault(p =>
                        string.Equals(p.TypeName, fav.TypeName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.FullName, fav.TypeName, StringComparison.OrdinalIgnoreCase));

                    if (row != null && !row.HiddenFromList && !_activeRows.Any(o =>
                        string.Equals(o.TypeName, row.TypeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _activeRows.Add(row);
                    }
                }

                _activeRowsDirty = false;
                return _activeRows;
            }

            IEnumerable<PluginRow> rows = _plugins.Where(r => r != null && !r.HiddenFromList);

            if (_activeTab != ManagerTab.All)
                rows = rows.Where(r => r.Category == _activeTab);

            foreach (var row in rows.OrderByDescending(r => r.IsFavorite).ThenBy(r => r.DisplayName))
                _activeRows.Add(row);

            _activeRowsDirty = false;
            return _activeRows;
        }


        private void ToggleFavorite(PluginRow row)
        {
            if (row == null) return;
            for (int i = _favorites.Count - 1; i >= 0; i--)
            {
                var fav = _favorites[i];
                if (fav == null) continue;
                if (string.Equals(fav.TypeName, row.TypeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fav.TypeName, row.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    _favorites.RemoveAt(i);
                    MarkRowsDirty();
                    return;
                }
            }
            _favorites.Add(new FavoriteEntry { TypeName = row.TypeName, DisplayName = row.DisplayName });
            MarkRowsDirty();
        }

        private bool IsFavorite(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            return _favorites.Any(f => f != null && string.Equals(f.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
        }

        private static ManagerTab ClassifyPlugin(string raw)
        {
            string s = (raw ?? string.Empty).ToLowerInvariant();
            // Items / crafting
            if (ContainsAny(s, "salvage", "item", "inventory", "kanai", "cube", "loot", "pickit", "stash", "mystic", "enchant")) return ManagerTab.Items;
            // Rift content
            if (ContainsAny(s, "rift", "greatrift", "urshi", "gem", "pylon", "boss", "timer", "progress")) return ManagerTab.Rift;
            // Combat + world overlays (oculus, triune merged here)
            if (ContainsAny(s, "autoskill", "autocast", "skill", "snap", "zbarb", "barb", "monster", "elite", "healthbar", "buff", "cooldown", "sentry", "archon", "strafe", "oculus", "triune")) return ManagerTab.Combat;
            return ManagerTab.Other;
        }

        private static bool ContainsAny(string s, params string[] needles)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var n in needles)
                if (!string.IsNullOrEmpty(n) && s.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private void SeedDefaultDisabledPluginOverrides()
        {
            try
            {
                for (int i = 0; i < DefaultDisabledPluginTypeNames.Length; i++)
                {
                    string typeName = DefaultDisabledPluginTypeNames[i];
                    if (!string.IsNullOrWhiteSpace(typeName) && !_pluginEnabledOverrides.ContainsKey(typeName))
                        _pluginEnabledOverrides[typeName] = false;
                }
            }
            catch
            {
            }
        }

        private IPlugin FindPluginByTypeName(params string[] typeNames)
        {
            if (typeNames == null || typeNames.Length == 0)
                return null;

            try
            {
                foreach (var row in _plugins)
                {
                    if (row == null || row.Plugin == null)
                        continue;

                    string typeName = row.TypeName ?? string.Empty;
                    string fullName = row.FullName ?? string.Empty;
                    string displayName = row.DisplayName ?? string.Empty;

                    for (int i = 0; i < typeNames.Length; i++)
                    {
                        string wanted = typeNames[i] ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(wanted))
                            continue;

                        if (string.Equals(typeName, wanted, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fullName, wanted, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(displayName, wanted, StringComparison.OrdinalIgnoreCase) ||
                            fullName.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase) ||
                            displayName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return row.Plugin;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private string[] GetBoundCompanionPluginTypeNames(IPlugin primary)
        {
            if (primary == null)
                return new string[0];

            string typeName = string.Empty;
            string fullName = string.Empty;

            try
            {
                typeName = primary.GetType().Name ?? string.Empty;
                fullName = primary.GetType().FullName ?? typeName;
            }
            catch
            {
            }

            if (IsPluginNameMatch(typeName, fullName, "s7o_AutoGemUpgradeMenu"))
                return new[] { "s7o_AutoGemUpgradeNavigator" };

            if (IsPluginNameMatch(typeName, fullName, "s7o_DHStrafePrimaryPlugin")
                || IsPluginNameMatch(typeName, fullName, "s7o_DHStrafe"))
            {
                return new[] { "s7o_DHStrafePrimaryCustomizer" };
            }

            if (IsPluginNameMatch(typeName, fullName, "s7o_MysticEnchantPlugin"))
                return new[] { "s7o_MysticEnchantCustomizer" };

            if (IsPluginNameMatch(typeName, fullName, "s7o_KanaiCube"))
            {
                return new[] { "s7o_KanaiCubeCustomizer" };
            }

            return new string[0];
        }

        private IEnumerable<IPlugin> FindPluginsByTypeNames(params string[] typeNames)
        {
            if (typeNames == null || typeNames.Length == 0)
                yield break;

            foreach (var row in _plugins)
            {
                if (row == null || row.Plugin == null)
                    continue;

                string typeName = row.TypeName ?? string.Empty;
                string fullName = row.FullName ?? string.Empty;
                string displayName = row.DisplayName ?? string.Empty;

                for (int i = 0; i < typeNames.Length; i++)
                {
                    string wanted = typeNames[i] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(wanted))
                        continue;

                    if (string.Equals(typeName, wanted, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fullName, wanted, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(displayName, wanted, StringComparison.OrdinalIgnoreCase)
                        || fullName.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase)
                        || displayName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        yield return row.Plugin;
                        break;
                    }
                }
            }
        }

        private bool TogglePluginByTypeName(string statusName, params string[] typeNames)
        {
            IPlugin plugin = FindPluginByTypeName(typeNames);

            if (plugin == null)
            {
                _status = statusName + " NOT INSTALLED";
                return false;
            }

            bool target = !SafePluginEnabled(plugin);
            SetPluginEnabledPersistentWithBoundCompanions(plugin, target);
            SetPluginAliasOverrides(target, typeNames);

            _status = statusName + (target ? " ENABLED" : " DISABLED");

            SaveSettings();
            RefreshPluginCache(true);
            ApplyGlobalTtsSettings();
            return true;
        }

        private void TryPreparePluginForDisable(IPlugin plugin)
        {
            if (plugin == null) return;

            // Banner/Pylon alert plugins own their live alert state and primarily route
            // speech through s7o_TTS_Broadcast, with native Hud.Sound only as fallback.
            // Keep generic disable preparation from invoking alert-specific force-stop
            // hooks so toggling one alert plugin cannot disturb another alert plugin.
            if (IsBannerOrPylonAlertPlugin(plugin))
                return;

            try
            {
                var method = plugin.GetType().GetMethod(
                    "ForceStopForDisable",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                if (method != null)
                    method.Invoke(plugin, null);
            }
            catch { }
        }

        private bool IsBannerOrPylonAlertPlugin(IPlugin plugin)
        {
            if (plugin == null)
                return false;

            string typeName = string.Empty;
            string fullName = string.Empty;

            try
            {
                typeName = plugin.GetType().Name ?? string.Empty;
                fullName = plugin.GetType().FullName ?? typeName;
            }
            catch { }

            return IsPluginNameMatch(typeName, fullName, "s7o_BannerSoundPlugin")
                || IsPluginNameMatch(typeName, fullName, "BannerSoundPlugin")
                || IsPluginNameMatch(typeName, fullName, "s7o_PylonAlerts")
                || IsPluginNameMatch(typeName, fullName, "PylonAlerts")
                || IsPluginNameMatch(typeName, fullName, "PylonAlertsPlugin")
                || IsPluginNameMatch(typeName, fullName, "OtherShrinePlugin");
        }

        private void TryResetBannerOrPylonAfterEnable(IPlugin plugin)
        {
            if (!IsBannerOrPylonAlertPlugin(plugin))
                return;

            try
            {
                var methods = plugin.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

                for (int i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method == null || method.Name != "OnNewArea")
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters == null || parameters.Length != 2 || parameters[0].ParameterType != typeof(bool))
                        continue;

                    method.Invoke(plugin, new object[] { true, null });
                    break;
                }
            }
            catch { }

            try
            {
                var method = plugin.GetType().GetMethod(
                    "SetGlobalTtsSettings",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(bool), typeof(int) },
                    null);

                if (method != null)
                    method.Invoke(plugin, new object[] { _globalTtsEnabled, _globalTtsVolume });
            }
            catch { }
        }

        private void TryTogglePlugin(IPlugin plugin, bool enabled)
        {
            if (plugin == null) return;

            if (!enabled && SafePluginEnabled(plugin))
                TryPreparePluginForDisable(plugin);

            try
            {
                foreach (var m in Hud.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == "TogglePlugin" && m.IsGenericMethodDefinition)
                    {
                        m.MakeGenericMethod(plugin.GetType()).Invoke(Hud, new object[] { enabled });
                        return;
                    }
                }
            }
            catch { }

            try { plugin.Enabled = enabled; } catch { }
        }

        private static string PluginStateKey(IPlugin plugin)
        {
            try { return plugin == null ? string.Empty : plugin.GetType().Name; }
            catch { return string.Empty; }
        }

        private void SetPluginAliasOverrides(bool enabled, params string[] typeNames)
        {
            if (typeNames == null)
                return;

            for (int i = 0; i < typeNames.Length; i++)
            {
                string name = typeNames[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                _pluginEnabledOverrides[name] = enabled;
            }
        }

        private void SetPluginEnabledPersistent(IPlugin plugin, bool enabled)
        {
            if (plugin == null) return;

            string key = PluginStateKey(plugin);
            if (!string.IsNullOrWhiteSpace(key))
                _pluginEnabledOverrides[key] = enabled;

            TryTogglePlugin(plugin, enabled);

            if (enabled)
                TryResetBannerOrPylonAfterEnable(plugin);
        }

        private void SetPluginEnabledPersistentWithBoundCompanions(IPlugin primary, bool enabled)
        {
            if (primary == null)
                return;

            SetPluginEnabledPersistent(primary, enabled);

            string[] companions = GetBoundCompanionPluginTypeNames(primary);

            for (int i = 0; i < companions.Length; i++)
            {
                foreach (IPlugin companion in FindPluginsByTypeNames(companions[i]))
                {
                    if (companion == null || object.ReferenceEquals(companion, primary))
                        continue;

                    SetPluginEnabledPersistent(companion, enabled);
                }
            }
        }

        private void ApplyPersistedPluginStates()
        {
            if (_pluginEnabledOverrides.Count == 0 || _plugins == null || _plugins.Count == 0) return;
            foreach (PluginRow row in _plugins)
            {
                if (row == null || row.Plugin == null || string.IsNullOrWhiteSpace(row.TypeName)) continue;
                bool desired;
                if (!_pluginEnabledOverrides.TryGetValue(row.TypeName, out desired)) continue;
                if (SafePluginEnabled(row.Plugin) != desired)
                    TryTogglePlugin(row.Plugin, desired);
            }
        }


        private void ApplyBoundPluginStatesFromPrimary()
        {
            try
            {
                foreach (var row in _plugins)
                {
                    if (row == null || row.Plugin == null || row.HiddenFromList)
                        continue;

                    string[] companions = GetBoundCompanionPluginTypeNames(row.Plugin);
                    if (companions == null || companions.Length == 0)
                        continue;

                    bool desired = SafePluginEnabled(row.Plugin);

                    for (int i = 0; i < companions.Length; i++)
                    {
                        foreach (IPlugin companion in FindPluginsByTypeNames(companions[i]))
                        {
                            if (companion == null || object.ReferenceEquals(companion, row.Plugin))
                                continue;

                            if (SafePluginEnabled(companion) != desired)
                                SetPluginEnabledPersistent(companion, desired);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool SafePluginEnabled(IPlugin plugin)
        {
            try { return plugin != null && plugin.Enabled; } catch { return false; }
        }


        private static void SetWorldDecoratorCollectionEnabled(WorldDecoratorCollection collection, bool enabled)
        {
            try
            {
                if (collection != null)
                    collection.Enabled = enabled;
            }
            catch { }
        }

        private static readonly MonsterAffix[] ImportantEliteAffixLabelTypes =
        {
            MonsterAffix.Arcane,
            MonsterAffix.Desecrator,
            MonsterAffix.Electrified,
            MonsterAffix.Frozen,
            MonsterAffix.FrozenPulse,
            MonsterAffix.Jailer,
            MonsterAffix.Juggernaut,
            MonsterAffix.Molten,
            MonsterAffix.Mortar,
            MonsterAffix.Orbiter,
            MonsterAffix.Plagued,
            MonsterAffix.Poison,
            MonsterAffix.Reflect,
            MonsterAffix.Thunderstorm,
            MonsterAffix.Waller,
        };

        private static void SetEliteAffixLabelsEnabled(EliteMonsterAffixPlugin plugin, bool enabled)
        {
            try
            {
                if (plugin == null || plugin.AffixDecorators == null)
                    return;

                for (int i = 0; i < ImportantEliteAffixLabelTypes.Length; i++)
                {
                    WorldDecoratorCollection collection;
                    if (plugin.AffixDecorators.TryGetValue(ImportantEliteAffixLabelTypes[i], out collection))
                        SetWorldDecoratorCollectionEnabled(collection, enabled);
                }

                // WeakDecorator is shared by the less dangerous/default-grey affixes.
                SetWorldDecoratorCollectionEnabled(plugin.WeakDecorator, enabled);
            }
            catch { }
        }


        private static void SetPluginBoolProperty(IPlugin plugin, string propertyName, bool value)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                var prop = plugin.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public);

                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(bool))
                    return;

                prop.SetValue(plugin, value, null);
            }
            catch
            {
            }
        }
                private void ApplyMonsterVisualToggles()
        {
            bool master = _visDangerousAffixVisualsEnabled;

            try
            {
                Hud.RunOnPlugin<EliteMonsterSkillPlugin>(plugin =>
                {
                    if (plugin == null)
                        return;

                    // User-exposed controls.
                    SetWorldDecoratorCollectionEnabled(plugin.PlaguedDecorator,         master && _affixPlagued);
                    SetWorldDecoratorCollectionEnabled(plugin.ArcaneSpawnDecorator,     master && _affixArcaneSpawn);
                    SetWorldDecoratorCollectionEnabled(plugin.MoltenExplosionDecorator, master && _affixMoltenExplosion);
                    SetWorldDecoratorCollectionEnabled(plugin.DesecratorDecorator,      master && _affixDesecrator);
                    SetWorldDecoratorCollectionEnabled(plugin.ThunderstormDecorator,    master && _affixThunderstorm);
                    SetWorldDecoratorCollectionEnabled(plugin.FrozenBallDecorator,      master && _affixFrozen);
                    SetWorldDecoratorCollectionEnabled(plugin.FrozenPulseDecorator,     master && _affixFrozenPulse);
                    SetWorldDecoratorCollectionEnabled(plugin.GhomDecorator,            master && _affixGhomGas);

                    // Removed as individual toggles, but still controlled by the master row.
                    SetWorldDecoratorCollectionEnabled(plugin.ArcaneDecorator,          master);
                    SetWorldDecoratorCollectionEnabled(plugin.MoltenDecorator,          master);
                });
            }
            catch
            {
            }

            try
            {
                Hud.RunOnPlugin<ExplosiveMonsterPlugin>(plugin =>
                {
                    if (plugin == null)
                        return;

                    SetWorldDecoratorCollectionEnabled(plugin.GrotesqueDecorator, master && _explosiveGrotesque);

                    // Removed as individual toggle, but still follows the master row.
                    SetWorldDecoratorCollectionEnabled(plugin.FastMummyDecorator, master);
                });
            }
            catch
            {
            }

            try
            {
                Hud.RunOnPlugin<DangerousMonsterPlugin>(plugin =>
                {
                    if (plugin == null)
                        return;

                    SetWorldDecoratorCollectionEnabled(plugin.Decorator, master && _dangerousMonsterLabelsEnabled);
                });
            }
            catch
            {
            }

            try
            {
                Hud.RunOnPlugin<EliteMonsterAffixPlugin>(plugin =>
                {
                    if (plugin == null)
                        return;

                    SetEliteAffixLabelsEnabled(plugin, master && _eliteAffixLabelsEnabled);
                });
            }
            catch
            {
            }

            try
            {
                IPlugin dangerousOverlay = FindPluginByTypeName("s7o_DangerousSkillOverlays");
                if (dangerousOverlay != null)
                {
                    SetPluginBoolProperty(dangerousOverlay, "ShowOrbiterProjectiles", master && _affixOrbiter);
                    SetPluginBoolProperty(dangerousOverlay, "ShowOrbiterFocalPoints", master && _affixOrbiter);
                    SetPluginBoolProperty(dangerousOverlay, "ShowWallerWalls", master && _affixWaller);
                    SetPluginBoolProperty(dangerousOverlay, "ShowWormholeMines", master && _affixWormholeMines);

                    // Poison Enchanted trail/pustule overlays are intentionally not exposed in HUD Menu.
                    SetPluginBoolProperty(dangerousOverlay, "ShowPoisonEnchantedProjectiles", false);

                    SetPluginBoolProperty(dangerousOverlay, "ShowBossFallingRocks", master && _affixBossFallingRocks);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossLeapTelegraphs", master && _affixBossLeapTelegraphs);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossMeteors", master && _affixBossMeteors);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossKulleHazards", master && _affixBossKulleHazards);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossBlighterHazards", master && _affixBossBlighterHazards);

                    // Keep noisy Rat King dome/proxy visuals disabled. The HUD row controls the useful rat-cloud overlay.
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossRatKingHazards", false);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossRatKingDomeHazards", false);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossRatKingRatBallHazards", master && _affixBossRatKingHazards);

                    SetPluginBoolProperty(dangerousOverlay, "ShowBossAdriaGeysers", master && _affixBossAdriaGeysers);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossButcherFire", master && _affixBossButcherFire);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossRimeHazards", master && _affixBossRimeHazards);
                    SetPluginBoolProperty(dangerousOverlay, "ShowShockTowerHazards", master && _affixShockTowerHazards);
                    SetPluginBoolProperty(dangerousOverlay, "ShowBossSandmonsterTurretHazards", master && _affixBossSandmonsterTurretHazards);
                }
            }
            catch
            {
            }
        }



        private IPlugin GetRiftFishingPlugin()
        {
            return FindPluginByTypeName("s7o_RiftFishing");
        }

        private bool InvokeRiftFishingEnableMap(IPlugin plugin, uint id, bool enabled)
        {
            try
            {
                if (plugin == null)
                    return false;

                var method = plugin.GetType().GetMethod(
                    "EnableMap",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(uint), typeof(bool) },
                    null);

                if (method == null)
                    return false;

                method.Invoke(plugin, new object[] { id, enabled });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyRiftFishingMapSettings()
        {
            if (!_riftMapSelectionHasOverride)
                return;

            IPlugin plugin = GetRiftFishingPlugin();
            if (plugin == null)
                return;

            try
            {
                for (int gi = 0; gi < RiftMapGroups.Length; gi++)
                {
                    var group = RiftMapGroups[gi];
                    if (group == null || group.Ids == null)
                        continue;

                    for (int ii = 0; ii < group.Ids.Length; ii++)
                    {
                        uint id = group.Ids[ii];
                        InvokeRiftFishingEnableMap(plugin, id, _riftEnabledMapIds.Contains(id));
                    }
                }
            }
            catch { }
        }

        private bool RiftMapGroupEnabled(RiftMapGroup group)
        {
            if (group == null || group.Ids == null || group.Ids.Length == 0)
                return false;

            for (int i = 0; i < group.Ids.Length; i++)
            {
                if (!_riftEnabledMapIds.Contains(group.Ids[i]))
                    return false;
            }

            return true;
        }

        private void SetRiftMapGroupEnabled(RiftMapGroup group, bool enabled)
        {
            if (group == null || group.Ids == null)
                return;

            _riftMapSelectionHasOverride = true;

            for (int i = 0; i < group.Ids.Length; i++)
            {
                if (enabled)
                    _riftEnabledMapIds.Add(group.Ids[i]);
                else
                    _riftEnabledMapIds.Remove(group.Ids[i]);
            }

            ApplyRiftFishingMapSettings();
            SaveSettings();
        }

        private IPlugin FindPartyInspectorPlugin()
        {
            return FindPluginByTypeName("s7o_PartyInspector");
        }

        private bool IsPartyInspectorEnabled()
        {
            IPlugin plugin = FindPartyInspectorPlugin();
            return plugin != null && SafePluginEnabled(plugin);
        }

        private void ApplyPartyInspectorHotkeyToPlugin()
        {
            try
            {
                IPlugin plugin = FindPartyInspectorPlugin();
                if (plugin == null || Hud == null || Hud.Input == null)
                    return;

                Type type = plugin.GetType();
                IKeyEvent keyEvent = Hud.Input.CreateKeyEvent(true, _partyInspectorHotkey, false, false, false);

                PropertyInfo prop = type.GetProperty(
                    "ToggleKeyEvent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop != null && prop.CanWrite && typeof(IKeyEvent).IsAssignableFrom(prop.PropertyType))
                {
                    prop.SetValue(plugin, keyEvent, null);
                    return;
                }

                FieldInfo field = type.GetField(
                    "ToggleKeyEvent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (field != null && typeof(IKeyEvent).IsAssignableFrom(field.FieldType))
                    field.SetValue(plugin, keyEvent);
            }
            catch
            {
            }
        }

                private void ApplyHudMenuRuntimeControlledSettings(bool force)
        {
            ApplyMonsterVisualToggles();
            ApplyRiftFishingMapSettings();
            ApplyPartyInspectorHotkeyToPlugin();
        }


        private void TryCloseTurboHud()
        {
            try
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(120);
                    try { Application.Exit(); } catch { }
                    try { Environment.Exit(0); } catch { }
                    try { Process.GetCurrentProcess().Kill(); } catch { }
                });
            }
            catch
            {
                try { Process.GetCurrentProcess().Kill(); } catch { }
            }
        }

        private MenuLayout GetLayout()
        {
            if (_layoutDirty)
                return BuildLayout();

            return _layout;
        }

        private RectangleF CurrentMenuButtonRect()
        {
            float closedSize = ViClampF(_menuButtonClosedSize, 18f, 60f);
            float openSize = ViClampF(_menuButtonOpenSize, 18f, 70f);
            float size = _visible ? openSize : closedSize;

            float cx = _dotRect.Left + _dotRect.Width * 0.5f;
            float cy = _dotRect.Top + _dotRect.Height * 0.5f;

            return new RectangleF(cx - size * 0.5f, cy - size * 0.5f, size, size);
        }

        private MenuLayout BuildLayout()
        {
            ClampRectsToScreen();

            var l = _layout;
            l.PageTabRects.Clear();
            l.TabRects.Clear();

            l.Dot = CurrentMenuButtonRect();
            l.Window = _windowRect;

            l.Title = new RectangleF(
                _windowRect.Left + 6f,
                _windowRect.Top + 6f,
                _windowRect.Width - 12f,
                34f);

            l.HideButton = new RectangleF(
                _windowRect.Right - 72f,
                _windowRect.Top + 12f,
                58f,
                20f);

            l.CloseHudButton = new RectangleF(
                l.HideButton.Left - 8f - 106f,
                _windowRect.Top + 12f,
                106f,
                20f);

            l.DebugButton = RectangleF.Empty;

            l.MoveButton = new RectangleF(
                l.CloseHudButton.Left - 8f - 80f,
                _windowRect.Top + 12f,
                80f,
                20f);

            l.DotButton = RectangleF.Empty;

            l.HotkeyButton = new RectangleF(
                l.MoveButton.Left - 8f - 78f,
                _windowRect.Top + 12f,
                78f,
                20f);

            l.HotkeyLabel = new RectangleF(
                l.HotkeyButton.Left - 66f,
                _windowRect.Top + 12f,
                62f,
                20f);

            l.PageTabBand = new RectangleF(
                _windowRect.Left + 18f,
                _windowRect.Top + 50f,
                _windowRect.Width - 36f,
                28f);

            float pageX = l.PageTabBand.Left;
            float pageY = l.PageTabBand.Top;
            float pageW = 108f;
            float pageGap = 8f;

            // Four fixed top-level pages.
            // No-Click Background occupies the far-right side of this same row.
            foreach (MenuPageTab page in MenuPageOrder)
            {
                l.PageTabRects[page] = new RectangleF(pageX, pageY, pageW, 24f);
                pageX += pageW + pageGap;
            }

            float noClickW = 174f;
            float noClickCheckSize = 12f;
            float noClickX = l.PageTabBand.Right - noClickW;
            float noClickY = l.PageTabBand.Top + 6f;

            l.NoClickCheck = new RectangleF(
                noClickX,
                noClickY,
                noClickCheckSize,
                noClickCheckSize);

            l.NoClickLabel = new RectangleF(
                l.NoClickCheck.Right + 6f,
                l.PageTabBand.Top + 3f,
                noClickW - noClickCheckSize - 6f,
                18f);

            float statusY = l.PageTabBand.Bottom + 5f;
            l.StatusBar = new RectangleF(
                _windowRect.Left + 18f,
                statusY,
                _windowRect.Width - 36f,
                24f);

            l.TopControlBar = new RectangleF(
                _windowRect.Left + 18f,
                l.StatusBar.Bottom + 5f,
                _windowRect.Width - 36f,
                30f);

            float paneTop = l.TopControlBar.Bottom + 8f;
            float paneHeight = Math.Max(120f, _windowRect.Bottom - paneTop - 20f);

            l.ContentPane = new RectangleF(
                _windowRect.Left + 14f,
                paneTop,
                _windowRect.Width - 28f,
                paneHeight);

            l.LeftPane = new RectangleF(
                _windowRect.Left + 14f,
                paneTop,
                164f,
                paneHeight);

            l.MainPane = new RectangleF(
                _windowRect.Left + 188f,
                paneTop,
                _windowRect.Width - 202f,
                paneHeight);

            l.ListRect = new RectangleF(
                l.MainPane.Left + 10f,
                l.MainPane.Top + 46f,
                l.MainPane.Width - 20f,
                l.MainPane.Height - 92f);

            l.ScrollUp = new RectangleF(
                l.ListRect.Right - 18f,
                l.ListRect.Top,
                16f,
                18f);

            l.ScrollDown = new RectangleF(
                l.ListRect.Right - 18f,
                l.ListRect.Bottom - 18f,
                16f,
                18f);

            l.ScrollTrack = new RectangleF(
                l.ScrollUp.Left,
                l.ScrollUp.Bottom + 2f,
                l.ScrollUp.Width,
                Math.Max(8f, l.ScrollDown.Top - l.ScrollUp.Bottom - 4f));

            if (_activePage == MenuPageTab.Plugins)
            {
                int total = GetActiveRowsCached().Count;
                int visible = VisibleRowCount(l);
                l.ScrollThumb = GetScrollThumbRect(l, total, visible);
            }
            else
            {
                l.ScrollThumb = RectangleF.Empty;
            }

            if (_activePage == MenuPageTab.Plugins)
            {
                l.Footer = new RectangleF(
                    l.MainPane.Left + 10f,
                    l.MainPane.Bottom - 36f,
                    l.MainPane.Width - 20f,
                    26f);
            }
            else
            {
                l.Footer = new RectangleF(
                    l.ContentPane.Left + 10f,
                    l.ContentPane.Bottom - 36f,
                    l.ContentPane.Width - 20f,
                    26f);
            }

            float y = l.LeftPane.Top + 14f;
            foreach (ManagerTab tab in Enum.GetValues(typeof(ManagerTab)))
            {
                l.TabRects[tab] = new RectangleF(
                    l.LeftPane.Left + 10f,
                    y,
                    l.LeftPane.Width - 20f,
                    30f);

                y += 36f;
            }

            // Content-pane scrollbar (right edge of ContentPane, for MAIN and TOGGLES)
            {
                float csW = 20f;
                float csLeft = l.ContentPane.Right - csW;

                l.ContentScrollUp = new RectangleF(csLeft, l.ContentPane.Top + 4f, csW - 2f, 22f);
                l.ContentScrollDown = new RectangleF(csLeft, l.ContentPane.Bottom - 26f, csW - 2f, 22f);
                l.ContentScrollTrack = new RectangleF(
                    csLeft,
                    l.ContentScrollUp.Bottom + 2f,
                    csW - 2f,
                    Math.Max(8f, l.ContentScrollDown.Top - l.ContentScrollUp.Bottom - 4f));
            }

            try
            {
                float sw = Hud.Window.Size.Width;
                float sh = Hud.Window.Size.Height;

                if (sw > 100f && sh > 100f)
                {
                    l.ProfileCloseMask = new RectangleF(
                        sw * (1567f / 1920f),
                        sh * (100f / 1080f),
                        sw * (23f / 1920f),
                        sh * (23f / 1080f));
                }
                else
                {
                    l.ProfileCloseMask = new RectangleF(1567f, 100f, 23f, 23f);
                }
            }
            catch
            {
                l.ProfileCloseMask = new RectangleF(1567f, 100f, 23f, 23f);
            }

            _layoutDirty = false;
            return l;
        }

        private int VisibleRowCount(MenuLayout layout)
        {
            if (layout == null) return 1;
            return Math.Max(1, (int)Math.Floor((layout.ListRect.Height - 8f) / 34f));
        }

        private void ClampScroll()
        {
            var layout = GetLayout();
            ClampScroll(layout, GetActiveRowsCached().Count);
        }

        private void ClampScroll(MenuLayout layout, int rowCount)
        {
            int max = Math.Max(0, rowCount - VisibleRowCount(layout));
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        private int MaxScroll(MenuLayout layout)
        {
            if (_activePage != MenuPageTab.Plugins) return 0;
            if (layout == null) layout = GetLayout();

            int total = GetActiveRowsCached().Count;
            int visible = VisibleRowCount(layout);

            return Math.Max(0, total - visible);
        }

        private void ScrollBy(int delta, MenuLayout layout)
        {
            if (layout == null) layout = GetLayout();

            int max = MaxScroll(layout);
            int old = _scroll;
            _scroll = Math.Max(0, Math.Min(max, _scroll + delta));
            if (_scroll != old)
                MarkLayoutDirty();
        }

        private RectangleF GetScrollThumbRect(MenuLayout layout, int total, int visible)
        {
            if (layout == null) return RectangleF.Empty;
            if (total <= visible || visible <= 0) return RectangleF.Empty;

            float trackTop = layout.ScrollTrack.Top;
            float trackH = Math.Max(8f, layout.ScrollTrack.Height);

            float thumbH = Math.Max(18f, trackH * (visible / (float)Math.Max(visible, total)));
            float maxScroll = Math.Max(1f, total - visible);

            float thumbY = trackTop + (trackH - thumbH) * (_scroll / maxScroll);

            return new RectangleF(
                layout.ScrollTrack.Left + 2f,
                thumbY,
                Math.Max(4f, layout.ScrollTrack.Width - 4f),
                thumbH);
        }

        private void JumpScrollToCursor(MenuLayout layout, float cursorY)
        {
            if (layout == null) layout = GetLayout();

            int total = GetActiveRowsCached().Count;
            int visible = VisibleRowCount(layout);
            int max = Math.Max(0, total - visible);

            if (max <= 0)
            {
                if (_scroll != 0)
                {
                    _scroll = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetScrollThumbRect(layout, total, visible);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;

            float travel = Math.Max(1f, layout.ScrollTrack.Height - thumbH);
            float targetTop = cursorY - thumbH * 0.5f;
            float ratio = Clamp01((targetTop - layout.ScrollTrack.Top) / travel);

            int old = _scroll;
            _scroll = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_scroll != old)
                MarkLayoutDirty();
        }

        private void DragScrollThumbToCursor(MenuLayout layout, float cursorY)
        {
            if (layout == null) layout = GetLayout();

            int total = GetActiveRowsCached().Count;
            int visible = VisibleRowCount(layout);
            int max = Math.Max(0, total - visible);

            if (max <= 0)
            {
                if (_scroll != 0)
                {
                    _scroll = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetScrollThumbRect(layout, total, visible);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;

            float travel = Math.Max(1f, layout.ScrollTrack.Height - thumbH);
            float targetTop = cursorY - _scrollDragOffsetY;
            float ratio = Clamp01((targetTop - layout.ScrollTrack.Top) / travel);

            int old = _scroll;
            _scroll = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_scroll != old)
                MarkLayoutDirty();
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }


        private int GetTtsAlertsViewportHeightEstimate()
        {
            return Math.Max(1, _ttsAlertsViewportHeightPx);
        }

        private int MaxTtsAlertsScroll()
        {
            int viewport = GetTtsAlertsViewportHeightEstimate();
            return Math.Max(0, _ttsAlertsContentHeightPx - viewport);
        }

        private int MaxTtsAlertsScrollForContentHeight(int contentHeightPx)
        {
            int viewport = GetTtsAlertsViewportHeightEstimate();
            return Math.Max(0, contentHeightPx - viewport);
        }

        private void ClampTtsAlertsScroll()
        {
            int max = MaxTtsAlertsScroll();
            if (_ttsAlertsScrollPx < 0) _ttsAlertsScrollPx = 0;
            if (_ttsAlertsScrollPx > max) _ttsAlertsScrollPx = max;
        }

        private void ClampTtsAlertsScrollForContentHeight(int contentHeightPx)
        {
            int max = MaxTtsAlertsScrollForContentHeight(contentHeightPx);

            if (_ttsAlertsScrollPx < 0)
                _ttsAlertsScrollPx = 0;

            if (_ttsAlertsScrollPx > max)
                _ttsAlertsScrollPx = max;
        }

        private void ScrollTtsAlertsBy(int deltaPx)
        {
            int old = _ttsAlertsScrollPx;
            _ttsAlertsScrollPx += deltaPx;
            ClampTtsAlertsScroll();
            if (_ttsAlertsScrollPx != old)
                MarkLayoutDirty();
        }

        private RectangleF GetTtsAlertsScrollThumbRect(RectangleF track)
        {
            int max = MaxTtsAlertsScroll();
            if (max <= 0 || track.Width <= 0f || track.Height <= 0f)
                return RectangleF.Empty;

            float trackH = Math.Max(8f, track.Height);
            float viewport = Math.Max(1f, GetTtsAlertsViewportHeightEstimate());
            float total = Math.Max(viewport, _ttsAlertsContentHeightPx);
            float thumbH = Math.Max(18f, trackH * (viewport / total));
            float thumbY = track.Top + (trackH - thumbH) * (_ttsAlertsScrollPx / (float)Math.Max(1, max));

            return new RectangleF(track.Left + 2f, thumbY, Math.Max(4f, track.Width - 4f), thumbH);
        }

        private void JumpTtsAlertsScrollToCursor(float cursorY)
        {
            int max = MaxTtsAlertsScroll();
            if (max <= 0)
            {
                if (_ttsAlertsScrollPx != 0)
                {
                    _ttsAlertsScrollPx = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetTtsAlertsScrollThumbRect(_ttsScrollTrackRect);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;
            float travel = Math.Max(1f, _ttsScrollTrackRect.Height - thumbH);
            float ratio = Clamp01((cursorY - _ttsScrollTrackRect.Top - thumbH * 0.5f) / travel);
            int old = _ttsAlertsScrollPx;
            _ttsAlertsScrollPx = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_ttsAlertsScrollPx != old)
                MarkLayoutDirty();
        }

        private void DragTtsAlertsScrollThumbToCursor(float cursorY)
        {
            int max = MaxTtsAlertsScroll();
            if (max <= 0)
            {
                if (_ttsAlertsScrollPx != 0)
                {
                    _ttsAlertsScrollPx = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetTtsAlertsScrollThumbRect(_ttsScrollTrackRect);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;
            float travel = Math.Max(1f, _ttsScrollTrackRect.Height - thumbH);
            float targetTop = cursorY - _ttsScrollDragOffsetY;
            float ratio = Clamp01((targetTop - _ttsScrollTrackRect.Top) / travel);
            int old = _ttsAlertsScrollPx;
            _ttsAlertsScrollPx = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_ttsAlertsScrollPx != old)
                MarkLayoutDirty();
        }

        private bool AutoScrollTtsRectIntoView(RectangleF targetR, RectangleF viewport, int anticipatedContentHeightPx)
        {
            bool changed = false;

            if (targetR.Bottom > viewport.Bottom)
            {
                _ttsAlertsScrollPx += (int)Math.Ceiling(targetR.Bottom - viewport.Bottom + 8f);
                changed = true;
            }

            if (targetR.Top < viewport.Top)
            {
                _ttsAlertsScrollPx -= (int)Math.Ceiling(viewport.Top - targetR.Top + 8f);
                changed = true;
            }

            if (changed)
            {
                ClampTtsAlertsScrollForContentHeight(anticipatedContentHeightPx);
                MarkLayoutDirty();
            }

            return changed;
        }

        private void DrawTtsAlertsScrollbar(RectangleF detail, RectangleF viewport)
        {
            const float sbW = 18f;
            const float btnH = 20f;

            float left = detail.Right - sbW - 2f;
            _ttsScrollUpRect = RectangleF.Empty;
            _ttsScrollDownRect = RectangleF.Empty;
            _ttsScrollTrackRect = RectangleF.Empty;
            _ttsScrollThumbRect = RectangleF.Empty;

            if (_ttsAlertsContentHeightPx <= viewport.Height + 1f)
            {
                _draggingTtsScrollThumb = false;
                _ttsAlertsScrollPx = 0;
                return;
            }

            _ttsScrollUpRect = new RectangleF(left, viewport.Top, sbW - 2f, btnH);
            _ttsScrollDownRect = new RectangleF(left, viewport.Bottom - btnH, sbW - 2f, btnH);
            _ttsScrollTrackRect = new RectangleF(
                left,
                _ttsScrollUpRect.Bottom + 2f,
                sbW - 2f,
                Math.Max(8f, _ttsScrollDownRect.Top - _ttsScrollUpRect.Bottom - 4f));

            _bScrollTrackSolid.DrawRectangle(
                _ttsScrollTrackRect.Left - 2f,
                _ttsScrollUpRect.Top,
                _ttsScrollTrackRect.Width + 4f,
                _ttsScrollDownRect.Bottom - _ttsScrollUpRect.Top);

            DrawFlashButton(_ttsScrollUpRect, "^", _lastTtsScrollUpTick, true);
            DrawFlashButton(_ttsScrollDownRect, "v", _lastTtsScrollDownTick, true);
            DrawSolidScrollbarRect(_ttsScrollTrackRect, _bScrollTrackSolid, _bScrollBorder);

            _ttsScrollThumbRect = GetTtsAlertsScrollThumbRect(_ttsScrollTrackRect);
            if (_ttsScrollThumbRect.IsEmpty)
                return;

            bool active =
                _draggingTtsScrollThumb ||
                IsFlashActive(_lastTtsScrollUpTick) ||
                IsFlashActive(_lastTtsScrollDownTick);

            DrawSolidScrollbarRect(
                _ttsScrollThumbRect,
                active ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);

            if (active)
                DrawGlowRings(_ttsScrollThumbRect);
        }

        private void DrawDottedRect(RectangleF r, IBrush brush)
        {
            if (brush == null) return;

            const float dash = 5f;
            const float gap = 4f;

            for (float x = r.Left; x < r.Right; x += dash + gap)
            {
                brush.DrawLine(x, r.Top, Math.Min(x + dash, r.Right), r.Top);
                brush.DrawLine(x, r.Bottom, Math.Min(x + dash, r.Right), r.Bottom);
            }

            for (float y = r.Top; y < r.Bottom; y += dash + gap)
            {
                brush.DrawLine(r.Left, y, r.Left, Math.Min(y + dash, r.Bottom));
                brush.DrawLine(r.Right, y, r.Right, Math.Min(y + dash, r.Bottom));
            }
        }

        private IBrush GetMenuButtonFillBrush(bool open)
        {
            try
            {
                EnsureVisualPickerBrushes();
                int idx = open ? _menuButtonOpenColorIdx : _menuButtonClosedColorIdx;
                int tone = open ? _menuButtonOpenTone : _menuButtonClosedTone;
                idx = ViClamp(idx, 0, 7);
                tone = ViClamp(tone, 0, 10);

                IBrush brush = _visualPickerFillBrushes[idx, tone];
                if (brush != null)
                    return brush;
            }
            catch { }

            return open ? _bDotFillOpen : _bDotFill;
        }

        private IBrush GetMenuButtonShadowBrush(bool open)
        {
            try
            {
                EnsureVisualPickerBrushes();
                int idx = open ? _menuButtonOpenColorIdx : _menuButtonClosedColorIdx;
                int tone = open ? _menuButtonOpenTone : _menuButtonClosedTone;
                idx = ViClamp(idx, 0, 7);
                tone = ViClamp(tone, 0, 10);

                IBrush brush = _visualPickerShadowBrushes[idx, tone];
                if (brush != null)
                    return brush;
            }
            catch { }

            return open ? _bDotShadowOpen : _bDotShadow;
        }

        private void DrawDot(RectangleF r)
        {
            float size = Math.Max(18f, Math.Min(r.Width, r.Height));

            float cx = r.Left + r.Width * 0.5f;
            float cy = r.Top + r.Height * 0.5f;

            float rx = size * 0.5f;
            float ry = size * 0.5f;

            // No green halo: keep the button compact and use a stronger black rim for contrast.
            _bDotRim.DrawEllipse(cx, cy, rx, ry);
            GetMenuButtonFillBrush(_visible).DrawEllipse(cx, cy, rx * 0.91f, ry * 0.91f);
            GetMenuButtonShadowBrush(_visible).DrawEllipse(cx, cy + ry * 0.28f, rx * 0.72f, ry * 0.52f);
            _bDotSpec.DrawEllipse(cx - rx * 0.24f, cy - ry * 0.24f, rx * 0.54f, ry * 0.37f);
            _bDotHot.DrawEllipse(cx - rx * 0.30f, cy - ry * 0.30f, rx * 0.19f, ry * 0.13f);

            if (_editMode)
                DrawDottedRect(new RectangleF(r.Left - 6f, r.Top - 6f, r.Width + 12f, r.Height + 12f), _bEditDash);
        }

        private void DrawWindow(MenuLayout l)
        {
            _lastHitRects.Clear();

            _bShadow.DrawRectangle(l.Window.Left + 5f, l.Window.Top + 5f, l.Window.Width, l.Window.Height);
            _bFrame.DrawRectangle(l.Window.Left, l.Window.Top, l.Window.Width, l.Window.Height);
            _bFrameBorder.DrawRectangle(l.Window.Left, l.Window.Top, l.Window.Width, l.Window.Height);
            _bInner.DrawRectangle(l.Window.Left + 6f, l.Window.Top + 6f, l.Window.Width - 12f, l.Window.Height - 12f);
            if (_editMode) DrawDottedRect(new RectangleF(l.Window.Left - 4f, l.Window.Top - 4f, l.Window.Width + 8f, l.Window.Height + 8f), _bEditDash);
            _bTitle.DrawRectangle(l.Title.Left, l.Title.Top, l.Title.Width, l.Title.Height);
            _bFrameBorder.DrawRectangle(l.Title.Left, l.Title.Top, l.Title.Width, l.Title.Height);

            _fTitle.DrawText("s7o HUD Manager", l.Window.Left + 18f, l.Window.Top + 14f);

            DrawCenteredText(_fSmall, "HOTKEY =", l.HotkeyLabel);
            DrawGlossButton(l.HotkeyButton, _capturingHotkey ? "PRESS..." : MenuHotkey.ToString(), _capturingHotkey, false, true);
            DrawGlossButton(l.MoveButton, "MOVE", _editMode, false, true);
            DrawGlossButton(l.CloseHudButton, "CLOSE HUD", false, true, true);
            DrawGlossButton(l.HideButton, "HIDE", false, false, true);

            DrawPageTabs(l);
            DrawNoClickToggle(l);
            DrawStatusBar(l);
            DrawTopControlBar(l);
            DrawPageContent(l);

            if (_editMode)
            {
                _fSmall.DrawText("MOVE MODE: drag the title bar or menu dot, then click MOVE again.", l.Window.Left + 20f, l.Window.Bottom - 18f);
            }
        }

        private void DrawStatusBar(MenuLayout l)
        {
            if (l == null) return;

            _bTextDimBg.DrawRectangle(l.StatusBar.Left, l.StatusBar.Top, l.StatusBar.Width, l.StatusBar.Height);
            _bPaneBorder.DrawRectangle(l.StatusBar.Left, l.StatusBar.Top, l.StatusBar.Width, l.StatusBar.Height);

            _fSmall.DrawText(
                "Status: " + Trim(_status, 120),
                l.StatusBar.Left + 8f,
                l.StatusBar.Top + 6f);
        }

        private void DrawTopControlBar(MenuLayout l)
        {
            _globalHitRects.Clear();

            _bTextDimBg.DrawRectangle(l.TopControlBar.Left, l.TopControlBar.Top, l.TopControlBar.Width, l.TopControlBar.Height);
            _bPaneBorder.DrawRectangle(l.TopControlBar.Left, l.TopControlBar.Top, l.TopControlBar.Width, l.TopControlBar.Height);

            float x = l.TopControlBar.Left + 10f;
            float cy = l.TopControlBar.Top + l.TopControlBar.Height * 0.5f;
            float btnH = 22f;
            float btnTop = cy - btnH * 0.5f;

            RectangleF check = new RectangleF(x, cy - 7f, 14f, 14f);
            DrawSquareCheck(check, _globalTtsEnabled);
            RegisterGlobalHit("global:tts-toggle", new RectangleF(x, l.TopControlBar.Top, 130f, l.TopControlBar.Height));

            _fText.DrawText("Global TTS", x + 22f, cy - 8f);

            x += 140f;

            _fText.DrawText("Volume", x, cy - 8f);
            x += 68f;

            RectangleF minus = new RectangleF(x, btnTop, 28f, btnH);
            RectangleF value = new RectangleF(minus.Right + 5f, btnTop, 52f, btnH);
            RectangleF plus  = new RectangleF(value.Right + 5f, btnTop, 28f, btnH);

            DrawFlashButton(minus, "-", _lastTtsVolMinusTick, true);
            DrawGlossButton(value, _globalTtsVolume.ToString(CultureInfo.InvariantCulture), false, false, false);
            DrawFlashButton(plus,  "+", _lastTtsVolPlusTick,  true);

            RegisterGlobalHit("global:tts-vol-", minus);
            RegisterGlobalHit("global:tts-vol+", plus);

            x = plus.Right + 16f;

            float sliderH = 12f;
            _globalTtsSliderTrack = new RectangleF(x, cy - sliderH * 0.5f, 190f, sliderH);
            _globalTtsSliderThumb = GetSimpleSliderThumb(
                _globalTtsSliderTrack,
                _globalTtsVolume,
                GlobalTtsVolumeMin,
                GlobalTtsVolumeMax);

            DrawSolidScrollbarRect(_globalTtsSliderTrack, _bScrollTrackSolid, _bScrollBorder);
            DrawSolidScrollbarRect(
                _globalTtsSliderThumb,
                _draggingGlobalTtsSlider ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);

            RegisterGlobalHit("global:tts-slider", _globalTtsSliderTrack);
            RegisterGlobalHit("global:tts-slider-thumb", _globalTtsSliderThumb);

            float labelLeft = _globalTtsSliderTrack.Right + 12f;
            if (labelLeft < l.TopControlBar.Right - 10f)
                _fSmall.DrawText("TTS speech volume", labelLeft, cy - 7f);
        }

        private RectangleF GetSimpleSliderThumb(RectangleF track, int value, int min, int max)
        {
            if (max <= min) max = min + 1;

            int v = value;
            if (v < min) v = min;
            if (v > max) v = max;

            float t = (v - min) / (float)(max - min);
            float thumbW = 14f;
            float x = track.Left + (track.Width - thumbW) * t;

            return new RectangleF(x, track.Top - 5f, thumbW, track.Height + 10f);
        }

        private void DrawPageContent(MenuLayout l)
        {
            switch (_activePage)
            {
                case MenuPageTab.Main:
                    DrawMainPage(l);
                    break;

                case MenuPageTab.Toggles:
                    DrawTogglesPage(l);
                    break;

                case MenuPageTab.Plugins:
                    DrawPluginsPage(l);
                    break;

                default:
                    _activePage = MenuPageTab.Main;
                    DrawMainPage(l);
                    break;
            }
        }

        private void DrawContentPane(MenuLayout l)
        {
            _bPane.DrawRectangle(l.ContentPane.Left, l.ContentPane.Top, l.ContentPane.Width, l.ContentPane.Height);
            _bPaneBorder.DrawRectangle(l.ContentPane.Left, l.ContentPane.Top, l.ContentPane.Width, l.ContentPane.Height);
        }


        private void DrawMainPage(MenuLayout l)
        {
            _mainHitRects.Clear();

            DrawContentPane(l);

            const float margin = 12f;
            const float sbW = 18f;

            float x = l.ContentPane.Left + margin;
            float w = l.ContentPane.Width - margin * 2f - sbW - 10f;

            float paneTop = l.ContentPane.Top + 8f;
            float paneBot = l.ContentPane.Bottom - 8f;

            float listTop = paneTop + 4f;
            RectangleF listRect = new RectangleF(x, listTop, w, Math.Max(1f, paneBot - listTop));

            List<MainPanelItem> panels = BuildMainPanelItems();

            _mainTotalItems = panels.Count;

            // MAIN panels have variable heights, so visible count must be calculated from real panel heights.
            int provisionalStart = Math.Max(0, Math.Min(_mainScroll, Math.Max(0, panels.Count - 1)));
            _mainVisibleItems = CountVisibleMainPanels(panels, provisionalStart, listRect.Height - 4f);
            ClampMainScroll();

            int correctedStart = Math.Max(0, Math.Min(_mainScroll, Math.Max(0, panels.Count - 1)));
            _mainVisibleItems = CountVisibleMainPanels(panels, correctedStart, listRect.Height - 4f);
            ClampMainScroll();

            DrawPluginsStyleMainScrollbar(l.ContentPane);

            int start = Math.Max(0, Math.Min(_mainScroll, MaxMainScroll()));
            int end = Math.Min(panels.Count, start + _mainVisibleItems);

            float y = listRect.Top + 2f;

            for (int i = start; i < end; i++)
            {
                MainPanelItem item = panels[i];
                float h = GetMainPanelHeight(item.Kind);

                RectangleF r = new RectangleF(listRect.Left, y, listRect.Width, h);

                if (r.Bottom > listRect.Bottom)
                    break;

                DrawMainPanelItem(r, item.Kind);

                y += h + 8f;
            }

            if (_bPaneCover != null)
            {
                float coverRight = _mainScrollUpRect.IsEmpty
                    ? l.ContentPane.Right - 1f
                    : Math.Max(l.ContentPane.Left + 1f, _mainScrollUpRect.Left - 4f);

                float coverW = Math.Max(1f, coverRight - (l.ContentPane.Left + 1f));

                _bPaneCover.DrawRectangle(l.ContentPane.Left + 1f, l.ContentPane.Top + 1f, coverW, 4f);
                _bPaneCover.DrawRectangle(l.ContentPane.Left + 1f, l.ContentPane.Bottom - 5f, coverW, 4f);
            }

            _bPaneBorder.DrawRectangle(l.ContentPane.Left, l.ContentPane.Top, l.ContentPane.Width, l.ContentPane.Height);
        }


        private enum MainPanelKind
        {
            AutoGem = 0,
            AutoSnap = 1,
            Hotkeys = 2
        }

        private struct MainPanelItem
        {
            public MainPanelKind Kind;
        }

        private List<MainPanelItem> BuildMainPanelItems()
        {
            return new List<MainPanelItem>
            {
                new MainPanelItem { Kind = MainPanelKind.AutoGem },
                new MainPanelItem { Kind = MainPanelKind.AutoSnap },
                new MainPanelItem { Kind = MainPanelKind.Hotkeys }
            };
        }

        private float GetMainPanelHeight(MainPanelKind kind)
        {
            if (kind == MainPanelKind.AutoGem)
                return _autoGemExpanded ? (_autoGemSpecificExpanded ? 420f : 212f) : 72f;

            if (kind == MainPanelKind.AutoSnap)
            {
                bool showHotkeyBindRow = _autoSnapHotkeysExpanded;
                return _autoSnapExpanded ? (showHotkeyBindRow ? 236f : 176f) : 72f;
            }


            if (kind == MainPanelKind.Hotkeys)
            {
                // Compact 5-column layout: 9 hotkeys + inline reset cell = 10 cells / 2 rows.
                return _hotkeysExpanded ? 142f : 46f;
            }

            return 72f;
        }

        private void DrawMainPanelItem(RectangleF r, MainPanelKind kind)
        {
            if (kind == MainPanelKind.AutoGem)
                DrawAutoGemMainPanel(r);
            else if (kind == MainPanelKind.AutoSnap)
                DrawAutoSnapPanel(r);
            else if (kind == MainPanelKind.Hotkeys)
                DrawHotkeysPanel(r);
        }

        private int CountVisibleMainPanels(List<MainPanelItem> panels, int start, float viewportHeight)
        {
            if (panels == null || panels.Count <= 0)
                return 1;

            if (start < 0)
                start = 0;

            if (start >= panels.Count)
                start = panels.Count - 1;

            float used = 2f;
            int count = 0;

            for (int i = start; i < panels.Count; i++)
            {
                float h = GetMainPanelHeight(panels[i].Kind);
                float need = h + (count > 0 ? 8f : 0f);

                // Always allow at least one panel, even if the panel itself is taller than the viewport.
                if (count > 0 && used + need > viewportHeight)
                    break;

                used += need;
                count++;

                if (used >= viewportHeight)
                    break;
            }

            return Math.Max(1, count);
        }

        private int MaxMainScroll()
        {
            return Math.Max(0, _mainTotalItems - _mainVisibleItems);
        }

        private void ClampMainScroll()
        {
            int mainLimit = MaxMainScroll();

            if (_mainScroll < 0)
                _mainScroll = 0;

            if (_mainScroll > mainLimit)
                _mainScroll = mainLimit;
        }

        private void ScrollMainBy(int delta)
        {
            int old = _mainScroll;
            _mainScroll += delta;
            ClampMainScroll();
            if (_mainScroll != old)
                MarkLayoutDirty();
        }

        private RectangleF GetMainScrollThumbRect(RectangleF track)
        {
            if (_mainTotalItems <= _mainVisibleItems || _mainVisibleItems <= 0 || track.Height <= 0f)
                return RectangleF.Empty;

            float trackH = Math.Max(8f, track.Height);
            float thumbH = Math.Max(18f, trackH * (_mainVisibleItems / (float)Math.Max(_mainVisibleItems, _mainTotalItems)));
            float maxScroll = Math.Max(1f, _mainTotalItems - _mainVisibleItems);
            float thumbY = track.Top + (trackH - thumbH) * (_mainScroll / maxScroll);

            return new RectangleF(track.Left + 2f, thumbY, Math.Max(4f, track.Width - 4f), thumbH);
        }

        private void DragMainScrollThumbToCursor(float cursorY)
        {
            int mainScrollMax = MaxMainScroll();

            if (mainScrollMax <= 0 || _mainScrollTrackRect.Height <= 0f)
                return;

            RectangleF dragThumb = GetMainScrollThumbRect(_mainScrollTrackRect);
            float dragTravel = Math.Max(1f, _mainScrollTrackRect.Height - dragThumb.Height);
            float targetTop = cursorY - _mainScrollDragOffsetY;
            float dragRatio = Clamp01((targetTop - _mainScrollTrackRect.Top) / dragTravel);

            int old = _mainScroll;
            _mainScroll = Math.Max(0, Math.Min(mainScrollMax, (int)Math.Round(dragRatio * mainScrollMax)));
            if (_mainScroll != old)
                MarkLayoutDirty();
        }

        private void DrawPluginsStyleMainScrollbar(RectangleF detail)
        {
            const float sbW = 18f;
            const float btnH = 20f;

            float left = detail.Right - sbW - 2f;

            _mainScrollUpRect = new RectangleF(left, detail.Top + 2f, sbW - 2f, btnH);
            _mainScrollDownRect = new RectangleF(left, detail.Bottom - btnH - 2f, sbW - 2f, btnH);
            _mainScrollTrackRect = new RectangleF(
                left,
                _mainScrollUpRect.Bottom + 2f,
                sbW - 2f,
                Math.Max(8f, _mainScrollDownRect.Top - _mainScrollUpRect.Bottom - 4f));

            _bScrollTrackSolid.DrawRectangle(
                _mainScrollTrackRect.Left - 2f,
                _mainScrollUpRect.Top,
                _mainScrollTrackRect.Width + 4f,
                _mainScrollDownRect.Bottom - _mainScrollUpRect.Top);

            DrawFlashButton(_mainScrollUpRect, "^", _lastMainScrollUpTick, true);
            DrawFlashButton(_mainScrollDownRect, "v", _lastMainScrollDownTick, true);

            DrawSolidScrollbarRect(_mainScrollTrackRect, _bScrollTrackSolid, _bScrollBorder);

            _mainScrollThumbRect = GetMainScrollThumbRect(_mainScrollTrackRect);

            if (_mainScrollThumbRect.IsEmpty)
                return;

            bool active =
                _draggingMainThumb ||
                IsFlashActive(_lastMainScrollUpTick) ||
                IsFlashActive(_lastMainScrollDownTick);

            DrawSolidScrollbarRect(
                _mainScrollThumbRect,
                active ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);

            if (active)
                DrawGlowRings(_mainScrollThumbRect);
        }

        private void DrawAutoSnapPanel(RectangleF r)
        {
            _bTextDimBg.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bPaneBorder.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float pad = 10f;
            float y = r.Top + 7f;
            float rowH = 26f;

            _fSection.DrawText("AutoSnap", r.Left + pad, y + 4f);
            _fSmall.DrawText("Experimental auto targeting for selected skill slots.", r.Left + 118f, y + 10f);

            RectangleF expand = new RectangleF(r.Right - 34f, y + 4f, 24f, 24f);
            DrawGlossButton(expand, _autoSnapExpanded ? "-" : "+", _autoSnapExpanded, false, true);
            RegisterMainHit("autosnap:expand", expand);

            if (!_autoSnapExpanded)
                return;

            y += 34f;

            RectangleF slotRow = new RectangleF(r.Left + pad, y, r.Width - pad * 2f, rowH);
            DrawMainSubRow(slotRow, 0);
            DrawAutoSnapSlotRow(slotRow);
            y += rowH + 6f;

            RectangleF behaviorRow = new RectangleF(r.Left + pad, y, r.Width - pad * 2f, rowH);
            DrawMainSubRow(behaviorRow, 1);
            DrawAutoSnapBehaviorRow(behaviorRow);
            y += rowH + 6f;

            RectangleF hotkeyToggleRow = new RectangleF(r.Left + pad, y, r.Width - pad * 2f, rowH);
            DrawMainSubRow(hotkeyToggleRow, 2);
            DrawAutoSnapHotkeyToggleRow(hotkeyToggleRow);
            y += rowH + 6f;

            if (_autoSnapHotkeysExpanded)
            {
                RectangleF hotkeyRow = new RectangleF(r.Left + pad, y, r.Width - pad * 2f, 46f);
                DrawMainSubRow(hotkeyRow, 3);
                DrawAutoSnapHotkeyBindRow(hotkeyRow);
                y += 52f;
            }

            RectangleF rangeRow = new RectangleF(r.Left + pad, y, r.Width - pad * 2f, rowH);
            DrawMainSubRow(rangeRow, 4);
            DrawAutoSnapRangeRow(rangeRow);
        }

        private void DrawMainSubRow(RectangleF r, int idx)
        {
            (idx % 2 == 0 ? _bRow : _bRowAlt).DrawRectangle(r.Left, r.Top, r.Width, r.Height);
        }

        private void DrawAutoSnapSlotRow(RectangleF row)
        {
            float x = row.Left + 10f;
            float y = row.Top + 3f;

            for (int i = 0; i < _autoSnapLabels.Length; i++)
            {
                RectangleF btn = new RectangleF(x, y, 34f, 22f);
                bool on = GetAnyAutoSnapSlotEnabled(_autoSnapSlots[i]);

                DrawGlossButton(btn, _autoSnapLabels[i], on, false, true);
                RegisterMainHit("autosnap:slot:" + i.ToString(CultureInfo.InvariantCulture), btn);

                x = btn.Right + 8f;
            }

            RectangleF restore = new RectangleF(x + 8f, y + 5f, 13f, 13f);
            DrawSquareCheck(restore, _autoSnapRestoreCursor);
            RegisterMainHit("autosnap:restore", new RectangleF(restore.Left, row.Top, 140f, row.Height));

            _fText.DrawText("Cursor Restore", restore.Right + 6f, row.Top + 6f);
        }

        private void DrawAutoSnapBehaviorRow(RectangleF row)
        {
            float x = row.Left + 10f;
            float y = row.Top + 6f;

            RectangleF jug = new RectangleF(x, y, 13f, 13f);
            DrawSquareCheck(jug, _autoSnapIgnoreJuggernauts);
            RegisterMainHit("autosnap:ignore-jug", new RectangleF(jug.Left, row.Top, 160f, row.Height));
            _fText.DrawText("Ignore Juggernauts", jug.Right + 6f, row.Top + 6f);

            x += 180f;

            RectangleF min = new RectangleF(x, y, 13f, 13f);
            DrawSquareCheck(min, _autoSnapIgnoreMinions);
            RegisterMainHit("autosnap:ignore-minion", new RectangleF(min.Left, row.Top, 140f, row.Height));
            _fText.DrawText("Ignore Minions", min.Right + 6f, row.Top + 6f);
        }

        private void DrawAutoSnapHotkeyToggleRow(RectangleF row)
        {
            float x = row.Left + 10f;
            float y = row.Top + 6f;

            RectangleF use = new RectangleF(x, y, 13f, 13f);
            DrawSquareCheck(use, _asHotkeysEnabled);
            RegisterMainHit("autosnap:hotkeys-enabled", new RectangleF(use.Left, row.Top, 105f, row.Height));
            _fText.DrawText("Use Hotkeys", use.Right + 6f, row.Top + 6f);

            x += 130f;

            RectangleF only = new RectangleF(x, y, 13f, 13f);
            DrawSquareCheck(only, _asHotkeysOnly);
            RegisterMainHit("autosnap:hotkeys-only", new RectangleF(only.Left, row.Top, 105f, row.Height));
            _fText.DrawText("Hotkey Only", only.Right + 6f, row.Top + 6f);

            RectangleF expand = new RectangleF(row.Right - 26f, row.Top + 3f, 22f, 20f);
            DrawGlossButton(expand, _autoSnapHotkeysExpanded ? "-" : "+", _autoSnapHotkeysExpanded, false, true);
            RegisterMainHit("autosnap:hotkeys-expand", expand);
        }

        private void DrawAutoSnapHotkeyBindRow(RectangleF row)
        {
            const int count = 6;
            float gap = 6f;
            float cellW = (row.Width - gap * (count - 1) - 12f) / count;
            float x = row.Left + 6f;

            for (int i = 0; i < count; i++)
            {
                RectangleF labelR = new RectangleF(x, row.Top + 3f, cellW, 16f);
                RectangleF btnR = new RectangleF(x, row.Top + 21f, cellW, 20f);

                _fSmall.DrawText(_autoSnapLabels[i], labelR.Left + 2f, labelR.Top);
                DrawGlossButton(btnR, _asHotkeyCaptureActive[i] ? "..." : _asHotkeyLabels[i], _asHotkeyCaptureActive[i], false, true);
                RegisterMainHit("autosnap:hotkey:" + i.ToString(CultureInfo.InvariantCulture), btnR);

                x += cellW + gap;
            }
        }

        private void DrawAutoSnapRangeRow(RectangleF row)
        {
            float x = row.Left + 8f;
            float y = row.Top + 3f;
            float h = 22f;

            RectangleF melee = new RectangleF(x, y, 74f, h);
            DrawGlossButton(melee, "NEAR ME", _autoSnapMode == 0, false, true);
            RegisterMainHit("autosnap:mode:0", melee);
            x = melee.Right + 6f;

            DrawMainStepper(ref x, y, 94f, h, _autoSnapMeleeRangeYards.ToString(CultureInfo.InvariantCulture) + "y",
                "autosnap:melee:-1", "autosnap:melee:+1");

            x += 10f;

            RectangleF cursor = new RectangleF(x, y, 74f, h);
            DrawGlossButton(cursor, "CURSOR", _autoSnapMode == 1, false, true);
            RegisterMainHit("autosnap:mode:1", cursor);
            x = cursor.Right + 6f;

            DrawMainStepper(ref x, y, 94f, h, _autoSnapRangedRangeYards.ToString(CultureInfo.InvariantCulture) + "y",
                "autosnap:ranged:-1", "autosnap:ranged:+1");

            x += 12f;

            RectangleF stand = new RectangleF(x, y + 4f, 13f, 13f);
            DrawSquareCheck(stand, _autoSnapLeftClickForceStandStill);
            RegisterMainHit("autosnap:left-stand", new RectangleF(stand.Left, row.Top, row.Right - stand.Left, row.Height));
            _fText.DrawText("Force Stand Still (L only)", stand.Right + 6f, row.Top + 6f);
        }

        private void DrawMainStepper(ref float x, float y, float w, float h, string label, string minusAction, string plusAction)
        {
            RectangleF minus = new RectangleF(x, y, 22f, h);
            RectangleF val = new RectangleF(minus.Right + 3f, y, w - 50f, h);
            RectangleF plus = new RectangleF(val.Right + 3f, y, 22f, h);

            DrawGlossButton(minus, "-", false, false, true);
            DrawGlossButton(val, label, false, false, true);
            DrawGlossButton(plus, "+", false, false, true);

            RegisterMainHit(minusAction, minus);
            RegisterMainHit(plusAction, plus);

            x = plus.Right;
        }

        private void DrawAutoGemMainPanel(RectangleF r)
        {
            _bTextDimBg.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bPaneBorder.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float pad = 10f;
            float headerH = 36f;
            float rowH = 34f;
            float rowGap = 6f;
            float buttonH = 26f;
            float y = r.Top + 8f;

            string modeText = AutoGemModeText(s7o_AutoGemUpgradeState.AutoGemMode);
            string tpTimingLabel =
                AutoGemAnchorText(s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining())
                + "+"
                + s7o_AutoGemUpgradeState.AutoGemTPDelayMs.ToString(CultureInfo.InvariantCulture)
                + "ms"
                + (s7o_AutoGemUpgradeState.AutoGemTPLagBoost ? " LAG" : string.Empty);

            var enableRect = new RectangleF(r.Left + pad, y + (headerH - 16f) * 0.5f, 16f, 16f);
            DrawSquareCheck(enableRect, s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled);
            RegisterMainHit("autogem:toggle", enableRect);

            _fSection.DrawText("Auto Gem Upgrade", r.Left + pad + 24f, y + 5f);

            string status =
                (s7o_AutoGemUpgradeState.AutoGemUpgradeEnabled ? "Enabled" : "Disabled")
                + "   Mode: " + modeText
                + "   TP: " + tpTimingLabel;

            _fLabel.DrawText(Trim(status, 100), r.Left + 210f, y + 7f);

            var expandRect = new RectangleF(r.Right - 36f, y + 4f, 26f, buttonH);
            DrawGlossButton(expandRect, _autoGemExpanded ? "-" : "+", _autoGemExpanded, false, true);
            RegisterMainHit("autogem:expand", expandRect);

            y += headerH;

            if (!_autoGemExpanded)
                return;

            float rowX = r.Left + pad;
            float rowW = r.Width - pad * 2f;

            var modeRow = new RectangleF(rowX, y, rowW, rowH);
            _bRow.DrawRectangle(modeRow.Left, modeRow.Top, modeRow.Width, modeRow.Height);

            float gap = 6f;
            float btnY = modeRow.Top + 3f;
            float btnW = (modeRow.Width - 12f - gap * 4f) / 5f;
            float bx = modeRow.Left + 6f;

            DrawAutoGemModeButton("AUTO", 0, new RectangleF(bx, btnY, btnW, buttonH));
            bx += btnW + gap;
            DrawAutoGemModeButton("FAST", 3, new RectangleF(bx, btnY, btnW, buttonH));
            bx += btnW + gap;
            DrawAutoGemModeButton("HIGH", 2, new RectangleF(bx, btnY, btnW, buttonH));
            bx += btnW + gap;
            DrawAutoGemModeButton("LOW", 1, new RectangleF(bx, btnY, btnW, buttonH));
            bx += btnW + gap;
            DrawAutoGemModeButton("SPECIFIC", 4, new RectangleF(bx, btnY, btnW, buttonH));

            y += rowH + rowGap;

            var anchorRow = new RectangleF(rowX, y, rowW, rowH);
            _bRowAlt.DrawRectangle(anchorRow.Left, anchorRow.Top, anchorRow.Width, anchorRow.Height);
            _fText.DrawText("TP Anchor", anchorRow.Left + 8f, anchorRow.Top + 8f);

            int anchor = s7o_AutoGemUpgradeState.GetConfiguredPortalAnchorRemaining();

            var anchor3 = new RectangleF(anchorRow.Left + 100f, anchorRow.Top + 4f, 80f, buttonH);
            var anchor4 = new RectangleF(anchor3.Right + 6f, anchorRow.Top + 4f, 80f, buttonH);

            DrawGlossButton(anchor3, "3RD", anchor == 3, false, false);
            DrawGlossButton(anchor4, "4TH", anchor == 4, false, false);
            RegisterMainHit("autogem:anchor3", anchor3);
            RegisterMainHit("autogem:anchor4", anchor4);

            _fSmall.DrawText("Timer starts when that upgrade begins", anchor4.Right + 10f, anchorRow.Top + 10f);

            y += rowH + rowGap;

            var delayRow = new RectangleF(rowX, y, rowW, rowH);
            _bRow.DrawRectangle(delayRow.Left, delayRow.Top, delayRow.Width, delayRow.Height);
            _fText.DrawText("TP Delay", delayRow.Left + 8f, delayRow.Top + 8f);

            int delay = s7o_AutoGemUpgradeState.AutoGemTPDelayMs;

            var delayMinus = new RectangleF(delayRow.Left + 100f, delayRow.Top + 4f, 28f, buttonH);
            var delayValue = new RectangleF(delayMinus.Right + 6f, delayRow.Top + 4f, 82f, buttonH);
            var delayPlus  = new RectangleF(delayValue.Right + 6f, delayRow.Top + 4f, 28f, buttonH);
            var lagBtn     = new RectangleF(delayPlus.Right + 6f,  delayRow.Top + 4f, 54f, buttonH);

            DrawGlossButton(delayMinus, "-", false, false, true);
            DrawGlossButton(delayValue, delay.ToString(CultureInfo.InvariantCulture) + "ms", false, false, false);
            DrawGlossButton(delayPlus, "+", false, false, true);
            DrawGlossButton(lagBtn, "LAG", s7o_AutoGemUpgradeState.AutoGemTPLagBoost, false, false);

            RegisterMainHit("autogem:delay-", delayMinus);
            RegisterMainHit("autogem:delay+", delayPlus);
            RegisterMainHit("autogem:lag", lagBtn);

            _fSmall.DrawText("0-1500ms after anchor; default = 3RD + 1000ms", lagBtn.Right + 10f, delayRow.Top + 10f);

            y += rowH + rowGap;

            var specificRow = new RectangleF(rowX, y, rowW, rowH);
            _bRowAlt.DrawRectangle(specificRow.Left, specificRow.Top, specificRow.Width, specificRow.Height);
            _fText.DrawText("Specific Gem", specificRow.Left + 8f, specificRow.Top + 8f);

            var listBtn    = new RectangleF(specificRow.Right - 34f, specificRow.Top + 4f, 26f, buttonH);
            var subModeBtn = new RectangleF(listBtn.Left - 64f, specificRow.Top + 4f, 58f, buttonH);
            bool subHighest = s7o_AutoGemUpgradeState.AutoGemSpecificSubMode == 1;

            var gemValueBtn = new RectangleF(
                specificRow.Left + 100f,
                specificRow.Top + 4f,
                Math.Max(100f, subModeBtn.Left - (specificRow.Left + 100f) - 6f),
                buttonH);

            DrawGlossButton(gemValueBtn, Trim(s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty, 44), false, false, false, true);
            DrawGlossButton(subModeBtn, subHighest ? "HIGH" : "AUTO", true, false, false);
            DrawGlossButton(listBtn, _autoGemSpecificExpanded ? "-" : "+", _autoGemSpecificExpanded, false, true);

            RegisterMainHit("autogem:specific-list", gemValueBtn);
            RegisterMainHit("autogem:specific-submode", subModeBtn);
            RegisterMainHit("autogem:specific-list-button", listBtn);

            y += rowH + rowGap;

            if (_autoGemSpecificExpanded)
            {
                float specificListH = 188f;
                DrawAutoGemSpecificList(new RectangleF(rowX, y, rowW, specificListH));
                y += specificListH + rowGap;
            }

        }

        private void DrawAutoGemModeButton(string label, int mode, RectangleF rect)
        {
            DrawGlossButton(rect, label, s7o_AutoGemUpgradeState.AutoGemMode == mode, false, false);
            RegisterMainHit("autogem:mode:" + mode.ToString(CultureInfo.InvariantCulture), rect);
        }

        private void DrawAutoGemSpecificList(RectangleF r)
        {
            _bTextDimBg.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bPaneBorder.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float itemH = 24f;
            float itemGap = 4f;
            float scrollW = 24f;
            float btnH = 22f;
            float pad = 6f;

            var up = new RectangleF(r.Right - scrollW - 4f, r.Top + 4f, scrollW, btnH);
            var down = new RectangleF(r.Right - scrollW - 4f, r.Bottom - btnH - 4f, scrollW, btnH);

            _autoGemSpecificScrollTrack = new RectangleF(
                up.Left,
                up.Bottom + 4f,
                scrollW,
                Math.Max(20f, down.Top - up.Bottom - 8f));

            _autoGemSpecificVisibleRows = Math.Max(1, (int)((r.Height - 10f) / (itemH + itemGap)));
            _autoGemSpecificMaxScroll = Math.Max(0, AutoGemNames.Length - _autoGemSpecificVisibleRows);
            _autoGemSpecificScroll = ClampAutoGemSpecificScroll(_autoGemSpecificScroll);

            _autoGemSpecificScrollThumb = GetAutoGemSpecificScrollThumb(
                _autoGemSpecificScrollTrack,
                AutoGemNames.Length,
                _autoGemSpecificVisibleRows,
                _autoGemSpecificScroll);

            DrawFlashButton(up,   "^", _lastAutoGemSpecificScrollUpTick,   true);
            DrawFlashButton(down, "v", _lastAutoGemSpecificScrollDownTick, true);

            DrawSolidScrollbarRect(_autoGemSpecificScrollTrack, _bScrollTrackSolid, _bScrollBorder);

            bool thumbActive = _draggingAutoGemSpecificScrollThumb
                || IsFlashActive(_lastAutoGemSpecificScrollTrackTick)
                || IsFlashActive(_lastAutoGemSpecificScrollUpTick)
                || IsFlashActive(_lastAutoGemSpecificScrollDownTick);

            if (!_autoGemSpecificScrollThumb.IsEmpty)
            {
                DrawSolidScrollbarRect(
                    _autoGemSpecificScrollThumb,
                    thumbActive ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                    _bScrollBorder);
            }

            RegisterMainHit("autogem:specific-scroll-up", up);
            RegisterMainHit("autogem:specific-scroll-down", down);
            RegisterMainHit("autogem:specific-scroll-track", _autoGemSpecificScrollTrack);
            if (!_autoGemSpecificScrollThumb.IsEmpty)
                RegisterMainHit("autogem:specific-scroll-thumb", _autoGemSpecificScrollThumb);

            float y = r.Top + 5f;
            for (int i = _autoGemSpecificScroll; i < AutoGemNames.Length && y + itemH <= r.Bottom - 5f; i++)
            {
                var opt = new RectangleF(r.Left + pad, y, r.Width - scrollW - 20f, itemH);
                string name = AutoGemNames[i];

                DrawGlossButton(
                    opt,
                    Trim(name, 58),
                    string.Equals(name, s7o_AutoGemUpgradeState.AutoGemSpecificName, StringComparison.OrdinalIgnoreCase),
                    false,
                    false);

                RegisterMainHit("autogem:specific:" + i.ToString(CultureInfo.InvariantCulture), opt);

                y += itemH + itemGap;
            }
        }

        private bool IsRecentTick(int tick, int durationMs)
        {
            if (tick == int.MinValue) return false;
            int e = unchecked(Environment.TickCount - tick);
            return e >= 0 && e <= durationMs;
        }

        // Green emanation flash — 100ms duration, safe at any frame rate.
        private bool IsFlashActive(int tick)
        {
            if (tick == int.MinValue) return false;
            int e = unchecked(Environment.TickCount - tick);
            return e >= 0 && e < 150;
        }

        private bool IsTtsButtonFlashActive(string action)
        {
            try
            {
                int tick;
                if (string.IsNullOrWhiteSpace(action))
                    return false;

                if (!_ttsButtonFlashTicks.TryGetValue(action, out tick))
                    return false;

                return IsFlashActive(tick);
            }
            catch
            {
                return false;
            }
        }

        private bool IsVisualButtonFlashAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            return
                action.StartsWith("visual:tone:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("visual:yards:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("visual:thick:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("visual:size:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("visual:dot:", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("visual:reset:partyinspector", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVisualButtonFlashActive(string action)
        {
            try
            {
                int tick;
                if (!_visualButtonFlashTicks.TryGetValue(action, out tick))
                    return false;

                return IsFlashActive(tick);
            }
            catch
            {
                return false;
            }
        }

        // Draw green emanation glow rings around a rect.
        private void DrawGlowRings(RectangleF r)
        {
            if (_bGlowOuter != null) _bGlowOuter.DrawRectangle(r.Left - 3f, r.Top - 3f, r.Width + 6f, r.Height + 6f);
            if (_bGlowInner != null) _bGlowInner.DrawRectangle(r.Left - 1f, r.Top - 1f, r.Width + 2f, r.Height + 2f);
        }

        // Draw a button with green emanation flash. Returns whether flashing.
        private bool DrawFlashButton(RectangleF r, string text, int flashTick, bool compact)
        {
            bool flashing = IsFlashActive(flashTick);
            DrawGlossButton(r, text, flashing, false, compact);
            if (flashing) DrawGlowRings(r);
            return flashing;
        }


        private int ClampAutoGemSpecificScroll(int value)
        {
            int max = Math.Max(0, _autoGemSpecificMaxScroll);
            if (value < 0) return 0;
            if (value > max) return max;
            return value;
        }

        private RectangleF GetAutoGemSpecificScrollThumb(RectangleF track, int totalRows, int visibleRows, int scroll)
        {
            if (track.Width <= 0f || track.Height <= 0f)
                return RectangleF.Empty;

            if (totalRows <= visibleRows || visibleRows <= 0)
                return RectangleF.Empty;

            float thumbH = Math.Max(24f, track.Height * (visibleRows / (float)Math.Max(visibleRows, totalRows)));
            float maxScroll = Math.Max(1f, totalRows - visibleRows);
            float thumbY = track.Top + (track.Height - thumbH) * (scroll / maxScroll);

            return new RectangleF(
                track.Left + 2f,
                thumbY,
                Math.Max(4f, track.Width - 4f),
                thumbH);
        }


        private int VisibleMacroItemCount(RectangleF listRect)
        {
            if (listRect.Height <= 0f)
                return 1;

            return Math.Max(1, (int)Math.Floor((listRect.Height - 4f) / MacroListSlotH));
        }

        private int MaxMacroScroll()
        {
            return Math.Max(0, _macrosTotalItems - _macrosVisibleItems);
        }

        private void ClampMacroScroll()
        {
            int max = MaxMacroScroll();

            if (_macrosScroll < 0)
                _macrosScroll = 0;

            if (_macrosScroll > max)
                _macrosScroll = max;
        }

        private void ScrollMacrosBy(int delta)
        {
            int old = _macrosScroll;
            _macrosScroll += delta;
            ClampMacroScroll();
            if (_macrosScroll != old)
                MarkLayoutDirty();
        }

        private RectangleF GetMacrosScrollThumbRect(RectangleF track)
        {
            if (_macrosTotalItems <= _macrosVisibleItems || _macrosVisibleItems <= 0)
                return RectangleF.Empty;

            float trackH = Math.Max(8f, track.Height);
            float thumbH = Math.Max(18f, trackH * (_macrosVisibleItems / (float)Math.Max(_macrosVisibleItems, _macrosTotalItems)));
            float maxScroll = Math.Max(1f, _macrosTotalItems - _macrosVisibleItems);

            float thumbY = track.Top + (trackH - thumbH) * (_macrosScroll / maxScroll);

            return new RectangleF(
                track.Left + 2f,
                thumbY,
                Math.Max(4f, track.Width - 4f),
                thumbH);
        }

        private void JumpMacrosScrollToCursor(float cursorY)
        {
            int max = MaxMacroScroll();

            if (max <= 0)
            {
                if (_macrosScroll != 0)
                {
                    _macrosScroll = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetMacrosScrollThumbRect(_macrosScrollTrackRect);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;

            float travel = Math.Max(1f, _macrosScrollTrackRect.Height - thumbH);
            float targetTop = cursorY - thumbH * 0.5f;
            float ratio = Clamp01((targetTop - _macrosScrollTrackRect.Top) / travel);

            int old = _macrosScroll;
            _macrosScroll = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_macrosScroll != old)
                MarkLayoutDirty();
        }

        private void DragMacrosScrollThumbToCursor(float cursorY)
        {
            int max = MaxMacroScroll();

            if (max <= 0)
            {
                if (_macrosScroll != 0)
                {
                    _macrosScroll = 0;
                    MarkLayoutDirty();
                }
                return;
            }

            RectangleF thumb = GetMacrosScrollThumbRect(_macrosScrollTrackRect);
            float thumbH = thumb.IsEmpty ? 18f : thumb.Height;

            float travel = Math.Max(1f, _macrosScrollTrackRect.Height - thumbH);
            float targetTop = cursorY - _macrosScrollDragOffsetY;
            float ratio = Clamp01((targetTop - _macrosScrollTrackRect.Top) / travel);

            int old = _macrosScroll;
            _macrosScroll = Math.Max(0, Math.Min(max, (int)Math.Round(ratio * max)));
            if (_macrosScroll != old)
                MarkLayoutDirty();
        }


        private s7o_AutoSkill GetAutoSkillPlugin()
        {
            if (_autoSkillPlugin != null) return _autoSkillPlugin;
            _autoSkillPlugin = FindPluginByTypeName("s7o_AutoSkill") as s7o_AutoSkill;
            return _autoSkillPlugin;
        }

        private bool GetMacroEnabled(string code, bool isBuff)
        {
            var ask = GetAutoSkillPlugin();
            if (ask == null) return false;
            try { return isBuff ? ask.GetBuffProfileEnabled(code) : ask.GetConditionalProfileEnabled(code); }
            catch { return false; }
        }

        private void ToggleMacro(string code, bool isBuff)
        {
            var ask = GetAutoSkillPlugin();
            if (ask == null) { _status = "AUTOSKILL PLUGIN NOT FOUND"; return; }
            try
            {
                bool newState = isBuff ? ask.ToggleBuffProfile(code) : ask.ToggleConditionalProfile(code);
                _macroToggleFlashTicks[code] = Environment.TickCount;
                _status = code + ": " + (newState ? "ENABLED" : "DISABLED");
            }
            catch (Exception ex) { _status = "MACRO TOGGLE ERROR: " + ex.Message; }
        }

        private void BeginAutoSkillKeybindCapture(int slot)
        {
            if (slot < 0 || _autoSkillBindActions == null || slot >= _autoSkillBindActions.Length)
                return;

            if (GetAutoSkillPlugin() == null)
            {
                _status = "AUTOSKILL PLUGIN NOT FOUND";
                return;
            }

            ActionKey action = _autoSkillBindActions[slot];
            if (action == ActionKey.LeftSkill || action == ActionKey.RightSkill)
            {
                _status = AutoSkillBindSlotName(slot) + " USES MOUSE CLICK";
                return;
            }

            _autoSkillKeybindCaptureSlot = slot;
            _status = "PRESS AUTOSKILL KEY FOR " + AutoSkillBindSlotName(slot);
        }

        private void ApplyAutoSkillKeybindFromCapturedKey(int slot, Key key)
        {
            if (slot < 0 || _autoSkillBindActions == null || slot >= _autoSkillBindActions.Length)
                return;

            var ask = GetAutoSkillPlugin();
            if (ask == null)
            {
                _status = "AUTOSKILL PLUGIN NOT FOUND";
                return;
            }

            ushort virtualKey;
            if (!TryGetVirtualKeyFromDirectInputKey(key, out virtualKey))
            {
                _status = "UNSUPPORTED AUTOSKILL KEY";
                return;
            }

            ActionKey action = _autoSkillBindActions[slot];
            try
            {
                if (ask.SetCastVirtualKey(action, virtualKey))
                {
                    _status = "AUTOSKILL " + AutoSkillBindSlotName(slot) + " SET TO " + AutoSkillVirtualKeyLabel(virtualKey);
                    SaveSettings();
                    RequestPluginCacheRefresh();
                }
                else
                {
                    _status = "AUTOSKILL KEYBIND NOT SET";
                }
            }
            catch (Exception ex)
            {
                _status = "AUTOSKILL KEYBIND ERROR: " + ex.Message;
            }
        }

        private bool TryGetVirtualKeyFromDirectInputKey(Key key, out ushort virtualKey)
        {
            virtualKey = 0;

            Keys winKey = KeyToWinKeys(key);
            if (winKey == Keys.None)
                return false;

            int value = (int)winKey;
            if (value <= 0 || value > ushort.MaxValue)
                return false;

            virtualKey = (ushort)value;
            return true;
        }

        private string GetAutoSkillKeybindButtonLabel(int slot)
        {
            if (slot < 0 || _autoSkillBindActions == null || slot >= _autoSkillBindActions.Length)
                return "?";

            ActionKey action = _autoSkillBindActions[slot];
            if (action == ActionKey.LeftSkill)
                return "LMB";

            if (action == ActionKey.RightSkill)
                return "RMB";

            var ask = GetAutoSkillPlugin();
            ushort vk = 0;

            try
            {
                if (ask != null)
                    vk = ask.GetCastVirtualKey(action);
            }
            catch { }

            if (vk == 0)
                vk = AutoSkillDefaultVirtualKeyForSlot(slot);

            return AutoSkillVirtualKeyLabel(vk);
        }

        private ushort AutoSkillDefaultVirtualKeyForSlot(int slot)
        {
            switch (slot)
            {
                case 0: return 0x31;
                case 1: return 0x32;
                case 2: return 0x33;
                case 3: return 0x34;
                case 4: return 0x51;
                default: return 0;
            }
        }

        private string AutoSkillBindSlotName(int slot)
        {
            if (_autoSkillBindSlotLabels != null && slot >= 0 && slot < _autoSkillBindSlotLabels.Length)
                return _autoSkillBindSlotLabels[slot];

            return "SLOT " + slot.ToString(CultureInfo.InvariantCulture);
        }

        private string AutoSkillVirtualKeyLabel(ushort virtualKey)
        {
            if (virtualKey == 0)
                return "NONE";

            Keys key = (Keys)virtualKey;

            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);

            if (key >= Keys.A && key <= Keys.Z)
                return key.ToString();

            if (key >= Keys.F1 && key <= Keys.F12)
                return key.ToString();

            switch (key)
            {
                case Keys.NumPad0: return "NUM0";
                case Keys.NumPad1: return "NUM1";
                case Keys.NumPad2: return "NUM2";
                case Keys.NumPad3: return "NUM3";
                case Keys.NumPad4: return "NUM4";
                case Keys.NumPad5: return "NUM5";
                case Keys.NumPad6: return "NUM6";
                case Keys.NumPad7: return "NUM7";
                case Keys.NumPad8: return "NUM8";
                case Keys.NumPad9: return "NUM9";
                case Keys.Multiply: return "NUM*";
                case Keys.Divide: return "NUM/";
                case Keys.Subtract: return "NUM-";
                case Keys.Add: return "NUM+";
                case Keys.Decimal: return "NUM.";
                case Keys.Space: return "SPACE";
                case Keys.Tab: return "TAB";
                case Keys.Return: return "ENTER";
                case Keys.Back: return "BACK";
                case Keys.Insert: return "INS";
                case Keys.Delete: return "DEL";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.Up: return "UP";
                case Keys.Down: return "DOWN";
                case Keys.Left: return "LEFT";
                case Keys.Right: return "RIGHT";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemtilde: return "`";
                case Keys.OemPipe: return "\\";
                case Keys.ShiftKey: return "SHIFT";
                case Keys.ControlKey: return "CTRL";
                case Keys.Menu: return "ALT";
                default: return CompactKeyName(key.ToString()).ToUpperInvariant();
            }
        }

        private void SetAutoGemSpecificScrollFromCursorY(float cursorY)
        {
            if (_autoGemSpecificScrollTrack.Height <= 0f || _autoGemSpecificMaxScroll <= 0)
                return;

            RectangleF track = _autoGemSpecificScrollTrack;
            RectangleF thumb = _autoGemSpecificScrollThumb;

            float thumbH = thumb.IsEmpty ? 24f : thumb.Height;
            float movable = Math.Max(1f, track.Height - thumbH);

            float local = cursorY - track.Top - _autoGemSpecificScrollDragOffsetY;
            float t = local / movable;

            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            int old = _autoGemSpecificScroll;
            _autoGemSpecificScroll = ClampAutoGemSpecificScroll((int)Math.Round(t * _autoGemSpecificMaxScroll));
            if (_autoGemSpecificScroll != old)
                MarkLayoutDirty();
        }

        private void JumpAutoGemSpecificScrollToCursorY(float cursorY)
        {
            if (_autoGemSpecificScrollTrack.Height <= 0f || _autoGemSpecificMaxScroll <= 0)
                return;

            RectangleF track = _autoGemSpecificScrollTrack;
            RectangleF thumb = _autoGemSpecificScrollThumb;

            float thumbH = thumb.IsEmpty ? 24f : thumb.Height;
            float movable = Math.Max(1f, track.Height - thumbH);

            float local = cursorY - track.Top - (thumbH * 0.5f);
            float t = local / movable;

            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            int old = _autoGemSpecificScroll;
            _autoGemSpecificScroll = ClampAutoGemSpecificScroll((int)Math.Round(t * _autoGemSpecificMaxScroll));
            if (_autoGemSpecificScroll != old)
                MarkLayoutDirty();
        }

        private void EnsureAutoGemSpecificScrollIncludesCurrentGem()
        {
            try
            {
                string selected = s7o_AutoGemUpgradeState.AutoGemSpecificName ?? string.Empty;

                int idx = -1;
                for (int i = 0; i < AutoGemNames.Length; i++)
                {
                    if (string.Equals(AutoGemNames[i], selected, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx < 0)
                    return;

                // Approximate visible rows with enlarged list.
                const int visibleRows = 6;

                if (idx < _autoGemSpecificScroll)
                    _autoGemSpecificScroll = idx;
                else if (idx >= _autoGemSpecificScroll + visibleRows)
                    _autoGemSpecificScroll = Math.Max(0, idx - visibleRows + 1);
            }
            catch { }
        }

        private static string AutoGemModeText(int mode)
        {
            switch (mode)
            {
                case 0: return "AUTO";
                case 1: return "LOWEST";
                case 2: return "HIGHEST";
                case 3: return "FAST 150";
                case 4: return "SPECIFIC";
                default: return "AUTO";
            }
        }

        private static string AutoGemAnchorText(int remaining)
        {
            return remaining == 4 ? "4TH" : "3RD";
        }

        private void DrawTogglesPage(MenuLayout l)
        {
            _togglesHitRects.Clear();

            // Match PLUGINS page structure: left nav pane + right detail pane.
            // Do not draw the outer content pane here; it creates an unwanted extra layer.
            RectangleF nav    = l.LeftPane;
            RectangleF detail = l.MainPane;

            _bPane.DrawRectangle(nav.Left, nav.Top, nav.Width, nav.Height);
            _bPaneBorder.DrawRectangle(nav.Left, nav.Top, nav.Width, nav.Height);

            _bPane.DrawRectangle(detail.Left, detail.Top, detail.Width, detail.Height);
            _bPaneBorder.DrawRectangle(detail.Left, detail.Top, detail.Width, detail.Height);

            float y = nav.Top + 14f;
            for (int i = 0; i < ToggleCategoryOrder.Length; i++)
            {
                RectangleF btn = new RectangleF(nav.Left + 10f, y, nav.Width - 20f, 30f);
                DrawTogglesCategoryButton(btn, ToggleCategoryOrder[i]);
                y += 36f;
            }

            DrawToggleCategoryDetail(detail);
        }

        private void DrawTogglesCategoryButton(RectangleF r, ToggleCategory cat)
        {
            bool active = _activeToggleCategory == cat;
            DrawGlossButton(r, ToggleCategoryLabel(cat), active, false, false); // compact=false = same large font as Plugins nav
            RegisterToggleHit("toggles:cat:" + ((int)cat).ToString(CultureInfo.InvariantCulture), r);
        }

        private void DrawToggleCategoryDetail(RectangleF detail)
        {
            switch (_activeToggleCategory)
            {
                case ToggleCategory.Visual:    DrawToggleVisualCategory(detail);    break;
                case ToggleCategory.Macros:    DrawToggleMacrosCategory(detail);    break;
                case ToggleCategory.TtsAlerts: DrawToggleTtsAlertsCategory(detail); break;
                default: DrawToggleVisualCategory(detail); break;
            }
        }


        private void DrawTogglePluginButtonRow(RectangleF r, string title, string description, string actionKey, params string[] typeNames)
        {
            IPlugin plugin = FindPluginByTypeName(typeNames);
            bool installed = plugin != null;
            bool enabled   = installed && SafePluginEnabled(plugin);

            (enabled ? _bRow : _bRowAlt).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            const float stateW = 146f;
            RectangleF stateR = new RectangleF(
                r.Right - stateW - 8f, r.Top + 6f, stateW, Math.Max(34f, r.Height - 12f));

            float textX = r.Left + 12f;
            float textRight = stateR.Left - 12f;
            float textW = Math.Max(40f, textRight - textX);

            DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow,
                Trim(title, ApproxCharsForWidth(textW, 5.8f)), textX, r.Top + 8f);

            if (!string.IsNullOrWhiteSpace(description))
            {
                string[] lines = WrapToggleDescription(description, ApproxCharsForToggleDescription(textW), 2);
                if (lines.Length > 0) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[0], textX, r.Top + 31f);
                if (lines.Length > 1) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[1], textX, r.Top + 49f);
            }

            string state = installed ? (enabled ? "ON" : "OFF") : "NOT INSTALLED";
            DrawGlossButton(stateR, FitButtonLabel(state, stateR.Width), enabled, false, false);

            // Only the right-side button is clickable.
            if (installed && !string.IsNullOrWhiteSpace(actionKey))
                RegisterToggleHit(actionKey, stateR);
        }

        private float DrawToggleDetailHeader(RectangleF detail, string title, string description)
        {
            _fSection.DrawText(title, detail.Left + 14f, detail.Top + 10f);

            if (!string.IsNullOrWhiteSpace(description))
                _fLabel.DrawText(Trim(description, 114), detail.Left + 14f, detail.Top + 34f);

            return detail.Top + 64f;
        }


        // ── VISUAL page index scrollbar helpers (mirrors MACROS pattern) ──────────

        private int VisibleVisualItemCount(RectangleF listRect)
        {
            if (VisualListSlotH <= 0f) return 1;
            return Math.Max(1, (int)Math.Floor((listRect.Height - 4f) / VisualListSlotH));
        }

        private int MaxVisualScroll()
        {
            return Math.Max(0, _visualTotalItems - _visualVisibleItems);
        }

        private void ClampVisualScroll()
        {
            int max = MaxVisualScroll();
            if (_visualScroll < 0) _visualScroll = 0;
            if (_visualScroll > max) _visualScroll = max;
        }

        private void ScrollVisualBy(int delta)
        {
            int old = _visualScroll;
            _visualScroll += delta;
            ClampVisualScroll();
            if (_visualScroll != old)
                MarkLayoutDirty();
        }

        private RectangleF GetVisualScrollThumbRect(RectangleF track)
        {
            int total   = _visualTotalItems;
            int visible = _visualVisibleItems;

            if (total <= 0 || visible >= total || visible <= 0 || track.Height <= 0f)
                return RectangleF.Empty;

            float trackH    = Math.Max(8f, track.Height);
            float thumbH    = Math.Max(18f, trackH * (visible / (float)Math.Max(visible, total)));
            float maxScroll = Math.Max(1f, total - visible);
            float thumbY    = track.Top + (trackH - thumbH) * (_visualScroll / maxScroll);

            return new RectangleF(
                track.Left + 2f,
                thumbY,
                Math.Max(4f, track.Width - 4f),
                thumbH);
        }

        private void DragVisualScrollThumbToCursor(float cursorY)
        {
            int max = MaxVisualScroll();

            if (max <= 0 || _visualScrollTrackRect.Height <= 0f)
                return;

            RectangleF thumb = GetVisualScrollThumbRect(_visualScrollTrackRect);
            float usable = _visualScrollTrackRect.Height - thumb.Height;

            if (usable <= 0f)
                return;

            float local = cursorY - _visualScrollTrackRect.Top - _visualScrollDragOffsetY;
            int old = _visualScroll;
            _visualScroll = (int)Math.Round(Clamp01(local / usable) * max);

            ClampVisualScroll();
            if (_visualScroll != old)
                MarkLayoutDirty();
        }

        private void DrawPluginsStyleVisualScrollbar(RectangleF detail)
        {
            const float sbW  = 18f;
            const float btnH = 20f;

            float left = detail.Right - sbW - 2f;

            _visualScrollUpRect = new RectangleF(
                left, detail.Top + 2f, sbW - 2f, btnH);

            _visualScrollDownRect = new RectangleF(
                left, detail.Bottom - btnH - 2f, sbW - 2f, btnH);

            _visualScrollTrackRect = new RectangleF(
                left,
                _visualScrollUpRect.Bottom + 2f,
                sbW - 2f,
                Math.Max(8f, _visualScrollDownRect.Top - _visualScrollUpRect.Bottom - 4f));

            // Same outer track backing as MACROS
            _bScrollTrackSolid.DrawRectangle(
                _visualScrollTrackRect.Left - 2f,
                _visualScrollUpRect.Top,
                _visualScrollTrackRect.Width + 4f,
                _visualScrollDownRect.Bottom - _visualScrollUpRect.Top);

            DrawFlashButton(_visualScrollUpRect,   "^", _lastVisualScrollUpTick,   true);
            DrawFlashButton(_visualScrollDownRect, "v", _lastVisualScrollDownTick, true);

            // Black-bordered inner track — same as MACROS / PLUGINS
            DrawSolidScrollbarRect(_visualScrollTrackRect, _bScrollTrackSolid, _bScrollBorder);

            _visualScrollThumbRect = GetVisualScrollThumbRect(_visualScrollTrackRect);

            if (_visualScrollThumbRect.IsEmpty)
                return;

            bool active =
                _draggingVisualThumb ||
                IsFlashActive(_lastVisualScrollUpTick) ||
                IsFlashActive(_lastVisualScrollDownTick);

            DrawSolidScrollbarRect(
                _visualScrollThumbRect,
                active ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);

            if (active)
                DrawGlowRings(_visualScrollThumbRect);
        }


        // ════════════════════════════════════════════════════════════════════════
        // VISUAL COLOR PALETTE
        // ════════════════════════════════════════════════════════════════════════

        private static readonly Color[] VisualPicker8 =
        {
            Color.FromArgb(255, 220,  48,  42),
            Color.FromArgb(255, 255, 142,  36),
            Color.FromArgb(255, 255, 214,  42),
            Color.FromArgb(255,  60, 220,  70),
            Color.FromArgb(255,  50, 160, 255),
            Color.FromArgb(255, 150,  85, 255),
            Color.FromArgb(255, 235, 235, 235),
            Color.FromArgb(255,  15,  18,  20)
        };

        private static int ViClamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float ViClampF(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float NextPlayerCircleYards(float current, int direction)
        {
            if (direction > 0)
            {
                if (current < 25f)
                    return Math.Min(25f, current + 1f);

                return current + 5f;
            }

            if (direction < 0)
            {
                if (current <= 25f)
                    return Math.Max(1f, current - 1f);

                float next = current - 5f;
                if (next < 25f)
                    next = 25f;

                return next;
            }

            return current;
        }

        private static byte ViClampByte(int value)
        {
            if (value < 0)   return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private Color GetVisualPickerColorToned(int idx, int tone)
        {
            idx  = ViClamp(idx,  0, VisualPicker8.Length - 1);
            tone = ViClamp(tone, 0, 10);
            Color c = VisualPicker8[idx];
            float factor = 0.45f + tone * 0.11f;
            return Color.FromArgb(255,
                ViClampByte((int)(c.R * factor)),
                ViClampByte((int)(c.G * factor)),
                ViClampByte((int)(c.B * factor)));
        }

        private void EnsureVisualPickerBrushes()
        {
            try
            {
                if (_visualPickerFillBrushes[0, 0] != null && _visualPickerShadowBrushes[0, 0] != null)
                    return;

                for (int colorIdx = 0; colorIdx < 8; colorIdx++)
                {
                    for (int tone = 0; tone <= 10; tone++)
                    {
                        Color col = GetVisualPickerColorToned(colorIdx, tone);
                        Color shadowCol = GetVisualPickerColorToned(colorIdx, Math.Max(0, tone - 4));
                        _visualPickerFillBrushes[colorIdx, tone] = Hud.Render.CreateBrush(245, col.R, col.G, col.B, 0);
                        _visualPickerShadowBrushes[colorIdx, tone] = Hud.Render.CreateBrush(185, shadowCol.R, shadowCol.G, shadowCol.B, 0);
                    }
                }

                _visualPickerEdgeSelected = Hud.Render.CreateBrush(255, 220, 255, 220, 2.2f);
                _visualPickerEdgeNormal   = Hud.Render.CreateBrush(255, 20, 20, 20, 1.0f);
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VISUAL OUTLINED DRAW HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private IBrush GetVisualBrush(int alpha, int r, int g, int b, float width)
        {
            width = ViClampF(width, 0f, 32f);

            string key =
                alpha.ToString(CultureInfo.InvariantCulture) + ":" +
                r.ToString(CultureInfo.InvariantCulture) + ":" +
                g.ToString(CultureInfo.InvariantCulture) + ":" +
                b.ToString(CultureInfo.InvariantCulture) + ":" +
                width.ToString("0.00", CultureInfo.InvariantCulture);

            IBrush brush;
            if (!_visualBrushCache.TryGetValue(key, out brush) || brush == null)
            {
                brush = Hud.Render.CreateBrush(alpha, r, g, b, width);
                _visualBrushCache[key] = brush;
            }

            return brush;
        }

        private void DrawWorldEllipseOutlined(IWorldCoordinate world, float yards, Color color, float thickness, int alpha)
        {
            if (world == null) return;

            yards     = ViClampF(yards,     1f, 120f);
            thickness = ViClampF(thickness, 0.5f, 12f);

            try
            {
                GetVisualBrush(Math.Min(240, alpha), 0, 0, 0, thickness + 2.2f)
                    .DrawWorldEllipse(yards, -1, world);

                GetVisualBrush(alpha, color.R, color.G, color.B, thickness)
                    .DrawWorldEllipse(yards, -1, world);
            }
            catch { }
        }

        private void DrawScreenLineOutlined(float x1, float y1, float x2, float y2, Color color, float thickness, int alpha)
        {
            thickness = ViClampF(thickness, 0.5f, 12f);

            try
            {
                GetVisualBrush(Math.Min(245, alpha), 0, 0, 0, thickness + 2.4f)
                    .DrawLine(x1, y1, x2, y2);

                GetVisualBrush(alpha, color.R, color.G, color.B, thickness)
                    .DrawLine(x1, y1, x2, y2);
            }
            catch { }
        }

        private void DrawScreenEllipseOutlined(float cx, float cy, float rx, float ry, Color color, float thickness, int alpha)
        {
            rx        = Math.Max(1f, rx);
            ry        = Math.Max(1f, ry);
            thickness = ViClampF(thickness, 0.5f, 12f);

            try
            {
                GetVisualBrush(Math.Min(245, alpha), 0, 0, 0, thickness + 2.2f)
                    .DrawEllipse(cx, cy, rx, ry);

                GetVisualBrush(alpha, color.R, color.G, color.B, thickness)
                    .DrawEllipse(cx, cy, rx, ry);
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VISUAL WORLD OVERLAYS (PaintWorld)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawVisualWorldOverlays()
        {
            if (!_visPlayerCircleEnabled &&
                !_visMouseCircleEnabled &&
                !_visMinionCirclesEnabled)
            {
                return;
            }

            if (_visPlayerCircleEnabled && Hud.Game.Me != null && Hud.Game.Me.FloorCoordinate != null)
            {
                DrawWorldEllipseOutlined(
                    Hud.Game.Me.FloorCoordinate,
                    _visPlayerCircleYards,
                    GetVisualPickerColorToned(_visPlayerCircleColorIdx, _visPlayerCircleTone),
                    _visPlayerCircleThickness, 230);
            }

            if (_visMouseCircleEnabled)
            {
                try
                {
                    var sc = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var wc = sc.ToWorldCoordinate();
                    if (wc != null)
                        DrawWorldEllipseOutlined(wc, _visMouseCircleYards,
                            GetVisualPickerColorToned(_visMouseCircleColorIdx, _visMouseCircleTone),
                            _visMouseCircleThickness, 230);
                }
                catch { }
            }

            if (_visMinionCirclesEnabled && Hud.Game.AliveMonsters != null)
            {
                Color mc = GetVisualPickerColorToned(_visMinionColorIdx, _visMinionTone);
                foreach (IMonster m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || !m.IsElite || m.FloorCoordinate == null) continue;
                    if (m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Rare) continue;
                    DrawWorldEllipseOutlined(m.FloorCoordinate, 3.0f, mc, _visMinionThickness, 210);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VISUAL TOP-SCREEN OVERLAYS (PaintTopInGame)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawVisualTopOverlays()
        {
            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            if (!_visLineToTargetEnabled &&
                !_visTargetReticleEnabled &&
                !_visReticleOutlineEnabled &&
                !_visSiphonEnabled)
            {
                return;
            }

            DrawVisualLineToTarget();
            DrawVisualTargetReticle();
            DrawVisualSiphonDebuffRing();
        }

        private IMonster GetSelectedVisualMonster()
        {
            try
            {
                IMonster t = Hud.Game.SelectedMonster2;
                if (t == null || !t.IsAlive || !t.IsOnScreen) return null;
                return t;
            }
            catch { return null; }
        }

        private void DrawVisualLineToTarget()
        {
            if (!_visLineToTargetEnabled || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null) return;
            try
            {
                IMonster target = GetSelectedVisualMonster();
                if (target == null || target.FloorCoordinate == null) return;
                var p = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate();
                var t = target.FloorCoordinate.ToScreenCoordinate();
                DrawScreenLineOutlined(p.X, p.Y - 4f, t.X, t.Y - 4f,
                    GetVisualPickerColorToned(_visLineToTargetColorIdx, _visLineToTargetTone),
                    _visLineToTargetThickness, 230);
            }
            catch { }
        }

        private void DrawVisualTargetReticle()
        {
            if (!_visTargetReticleEnabled && !_visReticleOutlineEnabled)
                return;

            IMonster target = GetSelectedVisualMonster();
            if (target == null || target.FloorCoordinate == null) return;
            try
            {
                var screen = target.FloorCoordinate.ToScreenCoordinate();
                float cx = screen.X;
                float cy = screen.Y - 4f;

                if (_visTargetReticleEnabled)
                {
                    float pulse = (float)Math.Sin(Hud.Game.CurrentGameTick * 0.34f) * 1.2f;
                    float r     = Math.Max(2.5f, 5.0f * _visTargetReticleSize + pulse);
                    Color c     = GetVisualPickerColorToned(_visTargetReticleColorIdx, _visTargetReticleTone);
                    DrawScreenEllipseOutlined(cx, cy, r, r, c, 4.8f * _visTargetReticleSize, 245);
                    DrawScreenEllipseOutlined(cx, cy, r * 0.72f, r * 0.72f, c, 3.4f * _visTargetReticleSize, 235);
                    DrawScreenEllipseOutlined(cx, cy, r * 0.46f, r * 0.46f, c, 2.5f * _visTargetReticleSize, 225);
                }

                if (_visReticleOutlineEnabled)
                {
                    float pulse   = (float)Math.Sin(Hud.Game.CurrentGameTick * 0.34f) * 1.5f;
                    float outlineR = 14f * _visTargetReticleSize + pulse;
                    DrawScreenEllipseOutlined(cx, cy, outlineR, outlineR,
                        GetVisualPickerColorToned(_visReticleOutlineColorIdx, _visReticleOutlineTone),
                        _visReticleOutlineThickness, 230);
                }
            }
            catch { }
        }

        private void DrawVisualSiphonDebuffRing()
        {
            if (!_visSiphonEnabled || Hud.Game.AliveMonsters == null) return;
            try
            {
                int gameTick     = Hud.Game.CurrentGameTick;
                float pulse      = (float)Math.Sin(gameTick * 0.22f) * 0.70f;
                float ringRadius = Math.Max(18f, 50f + pulse * 5.0f);
                float rotation   = gameTick * 0.52f;
                const int dots   = 16;
                Color c          = GetVisualPickerColorToned(_visSiphonColorIdx, _visSiphonTone);

                foreach (IMonster m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || !m.IsElite || m.FloorCoordinate == null) continue;
                    double hasDebuff;
                    try { hasDebuff = m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_9_Visual_Effect_D, VisualSiphonDebuffSno); }
                    catch { continue; }
                    if (hasDebuff != 1.0) continue;

                    var sc = m.FloorCoordinate.ToScreenCoordinate();
                    for (int i = 0; i < dots; i++)
                    {
                        float angle = rotation + (float)(Math.PI * 2.0 * i / dots);
                        DrawScreenEllipseOutlined(
                            sc.X + (float)Math.Cos(angle) * ringRadius,
                            sc.Y + (float)Math.Sin(angle) * ringRadius * 0.74f,
                            _visSiphonDotSize, _visSiphonDotSize, c, 2.0f, 235);
                    }
                }
            }
            catch { }
        }

        private void DrawVisualClickAnimation()
        {
            if (!_visible)
            {
                _visClickAnimStartTicks = 0L;
                return;
            }

            if (!_visClickAnimEnabled)
            {
                _visClickAnimStartTicks = 0L;
                return;
            }

            if (_visClickAnimStartTicks == 0L)
                return;

            if (!Contains(_windowRect, _visClickAnimX, _visClickAnimY))
            {
                _visClickAnimStartTicks = 0L;
                return;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            float elapsedMs = (nowTicks - _visClickAnimStartTicks) / 10000f;

            if (elapsedMs > VisualClickAnimationDurationMs)
            {
                _visClickAnimStartTicks = 0L;
                return;
            }

            float t = ViClampF(elapsedMs / VisualClickAnimationDurationMs, 0f, 1f);

            // Smooth ease-out: responsive at the click point, slower near the edge.
            // Squared fade gives the short burst a less abrupt final frame without
            // adding work or touching input/menu state.
            float inv = 1f - t;
            float easeOut = 1f - (inv * inv * inv);
            float fade = inv * inv;

            int alpha = (int)(fade * 245f);

            if (alpha <= 2)
                return;

            Color c = GetVisualPickerColorToned(_visClickAnimColorIdx, _visClickAnimTone);

            float size = ViClampF(_visClickAnimSize, 0.5f, 4.0f);
            float line = ViClampF(_visClickAnimThickness, 0.5f, 4.0f);

            float cx = _visClickAnimX;
            float cy = _visClickAnimY;

            const int rays = 8;

            float inner = (3.0f + 7.0f * easeOut) * size;
            float outer = (15.0f + 34.0f * easeOut) * size;

            // Size slightly increases visual weight too.
            float lineThickness = Math.Max(0.8f, line * (1.0f + 0.18f * size));

            for (int i = 0; i < rays; i++)
            {
                double ang = i * Math.PI * 2.0 / rays;

                float x1 = cx + (float)Math.Cos(ang) * inner;
                float y1 = cy + (float)Math.Sin(ang) * inner;
                float x2 = cx + (float)Math.Cos(ang) * outer;
                float y2 = cy + (float)Math.Sin(ang) * outer;

                DrawScreenLineOutlined(x1, y1, x2, y2, c, lineThickness, alpha);
            }

            float ringR = (4.0f + 19.0f * easeOut) * size;
            DrawScreenEllipseOutlined(
                cx,
                cy,
                ringR,
                ringR,
                c,
                Math.Max(1.0f, lineThickness * 0.85f),
                alpha);
        }

        private void TryStartVisualClickAnimation(float x, float y)
        {
            if (!_visClickAnimEnabled)
                return;

            if (!_visible)
                return;

            if (!Contains(_windowRect, x, y))
                return;

            // Single-pulse state is intentional: spam-clicking replaces the previous
            // pulse instead of accumulating a list of active animation objects.
            _visClickAnimStartTicks = DateTime.UtcNow.Ticks;
            _visClickAnimX = x;
            _visClickAnimY = y;
        }

        // ════════════════════════════════════════════════════════════════════════
        // VISUAL ACTION HANDLER
        // ════════════════════════════════════════════════════════════════════════

        private void HandleVisualAction(string action)
        {
            string[] p = action.Split(':');
            if (p.Length < 3) return;

            string cmd     = p[1];
            string feature = p[2];

            if (cmd == "star")
            {
                if (!_visualFavorites.Remove(feature)) _visualFavorites.Add(feature);
                SaveSettings(); return;
            }
            if (cmd == "toggle") { ToggleVisualFeature(feature); SaveSettings(); return; }
            if (cmd == "expand") { ToggleVisualExpanded(feature); SaveSettings(); return; }
            if (cmd == "hotkey" && feature == "partyinspector")
            {
                _partyInspectorHotkeyCapture = true;
                _status = "PRESS PARTY INSPECTOR HOTKEY";
                return;
            }
            if (cmd == "reset" && feature == "partyinspector")
            {
                _partyInspectorHotkey = Key.F12;
                _partyInspectorHotkeyCapture = false;
                ApplyPartyInspectorHotkeyToPlugin();
                RequestPluginCacheRefresh();
                _status = "PARTY INSPECTOR HOTKEY RESET TO F12";
                SaveSettings();
                return;
            }

            if (cmd == "color" && p.Length >= 4)
            {
                int idx;
                if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                { SetVisualColor(feature, idx); if (feature.StartsWith("menubutton", StringComparison.OrdinalIgnoreCase)) MarkLayoutDirty(); SaveSettings(); }
                return;
            }
if ((cmd == "tone" || cmd == "yards" || cmd == "thick" || cmd == "size" || cmd == "dot") && p.Length >= 4)
            {
                int delta = p[3] == "-1" ? -1 : 1;
                AdjustVisualValue(cmd, feature, delta);
                SaveSettings();
                return;
            }
        }

        private void ToggleVisualFeature(string feature)
        {
            if (feature == "dangeraffixes")
            {
                _visDangerousAffixVisualsEnabled = !_visDangerousAffixVisualsEnabled;
                _visDangerousAffixVisualsExpanded = _visDangerousAffixVisualsEnabled;
                ApplyMonsterVisualToggles();
                RequestPluginCacheRefresh();
                return;
            }

            if (feature == "partyinspector")
            {
                TogglePluginByTypeName("PARTY INSPECTOR", "s7o_PartyInspector");
                _visPartyInspectorExpanded = IsPartyInspectorEnabled();
                ApplyPartyInspectorHotkeyToPlugin();
                RequestPluginCacheRefresh();
                return;
            }

            if (feature == "menubutton")
            {
                _showDot = !_showDot;
                _visMenuButtonExpanded = _showDot;
                _status = _showDot ? "MENU BUTTON ON" : "MENU BUTTON OFF";
                MarkLayoutDirty();
                return;
            }

            if (feature == "click")
            {
                _visClickAnimEnabled = !_visClickAnimEnabled;
                _visClickAnimExpanded = _visClickAnimEnabled;
                return;
            }

            if (feature == "player")
            {
                _visPlayerCircleEnabled = !_visPlayerCircleEnabled;
                _visPlayerCircleExpanded = _visPlayerCircleEnabled;
                return;
            }

            if (feature == "mouse")
            {
                _visMouseCircleEnabled = !_visMouseCircleEnabled;
                _visMouseCircleExpanded = _visMouseCircleEnabled;
                return;
            }

            if (feature == "line")
            {
                _visLineToTargetEnabled = !_visLineToTargetEnabled;
                _visLineToTargetExpanded = _visLineToTargetEnabled;
                return;
            }

            if (feature == "reticle")
            {
                _visTargetReticleEnabled = !_visTargetReticleEnabled;
                _visTargetReticleExpanded = _visTargetReticleEnabled;
                return;
            }

            if (feature == "outline")
            {
                _visReticleOutlineEnabled = !_visReticleOutlineEnabled;
                _visReticleOutlineExpanded = _visReticleOutlineEnabled;
                return;
            }

            if (feature == "minion")
            {
                _visMinionCirclesEnabled = !_visMinionCirclesEnabled;
                _visMinionCirclesExpanded = _visMinionCirclesEnabled;
                return;
            }

            if (feature == "siphon")
            {
                _visSiphonEnabled = !_visSiphonEnabled;
                _visSiphonExpanded = _visSiphonEnabled;
                return;
            }
        }

        private void ToggleVisualExpanded(string feature)
        {
            if (feature == "dangeraffixes")
            {
                _visDangerousAffixVisualsExpanded = !_visDangerousAffixVisualsExpanded;
                return;
            }

            if (feature == "partyinspector")
            {
                _visPartyInspectorExpanded = !_visPartyInspectorExpanded;
                return;
            }

            if (feature == "menubutton")
            {
                _visMenuButtonExpanded = !_visMenuButtonExpanded;
                return;
            }

            if (feature == "click")
            {
                _visClickAnimExpanded = !_visClickAnimExpanded;
                return;
            }

            if (feature == "player")
            {
                _visPlayerCircleExpanded = !_visPlayerCircleExpanded;
                return;
            }

            if (feature == "mouse")
            {
                _visMouseCircleExpanded = !_visMouseCircleExpanded;
                return;
            }

            if (feature == "line")
            {
                _visLineToTargetExpanded = !_visLineToTargetExpanded;
                return;
            }

            if (feature == "reticle")
            {
                _visTargetReticleExpanded = !_visTargetReticleExpanded;
                return;
            }

            if (feature == "outline")
            {
                _visReticleOutlineExpanded = !_visReticleOutlineExpanded;
                return;
            }

            if (feature == "minion")
            {
                _visMinionCirclesExpanded = !_visMinionCirclesExpanded;
                return;
            }

            if (feature == "siphon")
            {
                _visSiphonExpanded = !_visSiphonExpanded;
                return;
            }
        }

        private void SetVisualColor(string f, int idx)
        {
            idx = ViClamp(idx, 0, 7);
            switch (f)
            {
                case "player":  _visPlayerCircleColorIdx   = idx; break;
                case "mouse":   _visMouseCircleColorIdx    = idx; break;
                case "line":    _visLineToTargetColorIdx   = idx; break;
                case "reticle": _visTargetReticleColorIdx  = idx; break;
                case "outline": _visReticleOutlineColorIdx = idx; break;
                case "click":   _visClickAnimColorIdx      = idx; break;
                case "menubuttonclosed": _menuButtonClosedColorIdx = idx; break;
                case "menubuttonopen":   _menuButtonOpenColorIdx   = idx; break;
                case "minion":  _visMinionColorIdx         = idx; break;
                case "siphon":  _visSiphonColorIdx         = idx; break;
            }
        }

        private void AdjustVisualValue(string cmd, string f, int delta)
        {
            switch (cmd)
            {
                case "tone":
                    switch (f)
                    {
                        case "click":   _visClickAnimTone      = ViClamp(_visClickAnimTone      + delta, 0, 10); break;
                        case "player":  _visPlayerCircleTone   = ViClamp(_visPlayerCircleTone   + delta, 0, 10); break;
                        case "mouse":   _visMouseCircleTone    = ViClamp(_visMouseCircleTone    + delta, 0, 10); break;
                        case "line":    _visLineToTargetTone   = ViClamp(_visLineToTargetTone   + delta, 0, 10); break;
                        case "reticle": _visTargetReticleTone  = ViClamp(_visTargetReticleTone  + delta, 0, 10); break;
                        case "outline": _visReticleOutlineTone = ViClamp(_visReticleOutlineTone + delta, 0, 10); break;
                        case "menubuttonclosed": _menuButtonClosedTone = ViClamp(_menuButtonClosedTone + delta, 0, 10); MarkLayoutDirty(); break;
                        case "menubuttonopen":   _menuButtonOpenTone   = ViClamp(_menuButtonOpenTone   + delta, 0, 10); MarkLayoutDirty(); break;
                        case "minion":  _visMinionTone         = ViClamp(_visMinionTone         + delta, 0, 10); break;
                        case "siphon":  _visSiphonTone         = ViClamp(_visSiphonTone         + delta, 0, 10); break;
                    }
                    break;
                case "yards":
                    if (f == "player") _visPlayerCircleYards = ViClampF(NextPlayerCircleYards(_visPlayerCircleYards, delta), 1f, 100f);
                    else if (f == "mouse") _visMouseCircleYards = ViClampF(_visMouseCircleYards + delta * 2f, 2f, 60f);
                    break;
                case "thick":
                    if (f == "click") _visClickAnimThickness = ViClampF(_visClickAnimThickness + delta * 0.5f, 0.5f, 4.0f);
                    else if (f == "player") _visPlayerCircleThickness = ViClampF(_visPlayerCircleThickness + delta * 0.5f, 0.5f, 8f);
                    else if (f == "mouse") _visMouseCircleThickness = ViClampF(_visMouseCircleThickness + delta * 0.5f, 0.5f, 8f);
                    else if (f == "line") _visLineToTargetThickness = ViClampF(_visLineToTargetThickness + delta * 0.5f, 0.5f, 8f);
                    else if (f == "outline") _visReticleOutlineThickness = ViClampF(_visReticleOutlineThickness + delta * 0.5f, 0.5f, 8f);
                    else if (f == "minion") _visMinionThickness = ViClampF(_visMinionThickness + delta * 0.3f, 0.5f, 6f);
                    break;
                case "size":
                    if (f == "click") _visClickAnimSize = ViClampF(_visClickAnimSize + delta * 0.5f, 0.5f, 4.0f);
                    else if (f == "reticle") _visTargetReticleSize = ViClampF(_visTargetReticleSize + delta * 0.25f, 0.25f, 4f);
                    else if (f == "menubuttonclosed") { _menuButtonClosedSize = ViClampF(_menuButtonClosedSize + delta * 1f, 18f, 60f); _dotRect.Width = _menuButtonClosedSize; _dotRect.Height = _menuButtonClosedSize; MarkLayoutDirty(); }
                    else if (f == "menubuttonopen") { _menuButtonOpenSize = ViClampF(_menuButtonOpenSize + delta * 1f, 18f, 70f); MarkLayoutDirty(); }
                    break;
                case "dot":
                    if (f == "siphon") _visSiphonDotSize = ViClampF(_visSiphonDotSize + delta * 0.25f, 1f, 10f);
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VISUAL DRAW HELPERS (UI rows)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawVisualFeatureRow(RectangleF r, string feature, string title, string description,
            bool enabled, bool expanded, int rowIdx)
        {
            const float starW   = 32f;
            const float stateW  = 120f;
            const float expandW = 30f;

            (rowIdx % 2 == 0 ? _bRow : _bRowAlt).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            RectangleF starR   = new RectangleF(r.Left + 7f, r.Top + (r.Height - 32f) * 0.5f, starW, 32f);
            RectangleF expandR = new RectangleF(r.Right - expandW - 8f, r.Top + 6f, expandW, r.Height - 12f);
            RectangleF stateR  = new RectangleF(expandR.Left - stateW - 6f, r.Top + 6f, stateW, r.Height - 12f);

            float textX    = starR.Right + 12f;
            float textRight = stateR.Left - 12f;
            float textW    = Math.Max(40f, textRight - textX);

            DrawStarButton(starR, _visualFavorites.Contains(feature));
            RegisterToggleHit("visual:star:" + feature, starR);

            DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow,
                Trim(title, ApproxCharsForWidth(textW, 5.8f)), textX, r.Top + 8f);

            if (!string.IsNullOrWhiteSpace(description))
            {
                string[] lines = WrapToggleDescription(description, ApproxCharsForToggleDescription(textW), 2);
                if (lines.Length > 0) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[0], textX, r.Top + 31f);
                if (lines.Length > 1) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[1], textX, r.Top + 49f);
            }

            DrawGlossButton(stateR, enabled ? "ON" : "OFF", enabled, false, false);
            RegisterToggleHit("visual:toggle:" + feature, stateR);

            DrawGlossButton(expandR, expanded ? "-" : "+", expanded, false, true);
            RegisterToggleHit("visual:expand:" + feature, expandR);
        }

        private void DrawVisualOptionsRow(RectangleF r, string feature, int rowIdx,
            int colorIdx, int tone, bool hasTone, bool hasYards, float yards,
            bool hasThick, float thick, bool hasSize, float size, bool hasDot, float dot,
            string tonedLabel, string yardsLabel, string thickLabel)
        {
            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            if (feature == "click")
            {
                float clickMidY = r.Top + r.Height * 0.5f;
                float clickH = 30f;
                float clickX = r.Left + 51f;

                // Slightly narrower color rectangle so the three wider steppers fit cleanly.
                RectangleF clickColorR = new RectangleF(clickX, clickMidY - 10f, 172f, 22f);
                DrawVisualColorPickerInline(clickColorR, "click", _visClickAnimColorIdx, _visClickAnimTone);

                clickX = clickColorR.Right + 14f;

                // Adaptive width keeps buttons readable without overflowing the row.
                float availableForSteps = Math.Max(1f, r.Right - clickX - 8f);
                float clickStepW = Math.Max(122f, Math.Min(150f, (availableForSteps - 20f) / 3f));

                RectangleF toneR = new RectangleF(clickX, clickMidY - clickH * 0.5f, clickStepW, clickH);
                DrawVisualStepperWide(
                    toneR,
                    "Tone",
                    _visClickAnimTone.ToString(CultureInfo.InvariantCulture),
                    "visual:tone:click:-1",
                    "visual:tone:click:+1");

                clickX = toneR.Right + 10f;

                RectangleF sizeR = new RectangleF(clickX, clickMidY - clickH * 0.5f, clickStepW, clickH);
                DrawVisualStepperWide(
                    sizeR,
                    "Size",
                    FormatVisualFloat(_visClickAnimSize),
                    "visual:size:click:-1",
                    "visual:size:click:+1");

                clickX = sizeR.Right + 10f;

                RectangleF lineR = new RectangleF(clickX, clickMidY - clickH * 0.5f, clickStepW, clickH);
                DrawVisualStepperWide(
                    lineR,
                    "Line",
                    FormatVisualFloat(_visClickAnimThickness),
                    "visual:thick:click:-1",
                    "visual:thick:click:+1");

                return;
            }

            float midY  = r.Top + r.Height * 0.5f;
            float stepH = 30f;
            float stepY = midY - stepH * 0.5f;

            // Color swatches inline left
            // Align with feature row text: star inset(7) + starW(32) + text gap(12) = 51
            RectangleF colorR = new RectangleF(r.Left + 51f, midY - 9f, 190f, 20f);
            DrawVisualColorPickerInline(colorR, feature, colorIdx, tone);

            // Steppers flow left-to-right after color picker
            float x = colorR.Right + 14f;
            float stepW = 136f;

            if (hasYards)
            {
                DrawVisualStepperWide(new RectangleF(x, stepY, stepW, stepH),
                    "Yards", FormatVisualFloat(yards),
                    "visual:yards:" + feature + ":-1", "visual:yards:" + feature + ":+1");
                x += stepW + 10f;
            }
            if (hasThick)
            {
                DrawVisualStepperWide(new RectangleF(x, stepY, stepW, stepH),
                    "Line", FormatVisualFloat(thick),
                    "visual:thick:" + feature + ":-1", "visual:thick:" + feature + ":+1");
                x += stepW + 10f;
            }
            if (hasSize)
            {
                DrawVisualStepperWide(new RectangleF(x, stepY, stepW, stepH),
                    "Size", FormatVisualFloat(size),
                    "visual:size:" + feature + ":-1", "visual:size:" + feature + ":+1");
                x += stepW + 10f;
            }
            if (hasDot)
            {
                DrawVisualStepperWide(new RectangleF(x, stepY, stepW, stepH),
                    "Dot", FormatVisualFloat(dot),
                    "visual:dot:" + feature + ":-1", "visual:dot:" + feature + ":+1");
                x += stepW + 10f;
            }
            if (hasTone)
            {
                DrawVisualStepperWide(new RectangleF(x, stepY, stepW, stepH),
                    "Tone", tone.ToString(CultureInfo.InvariantCulture),
                    "visual:tone:" + feature + ":-1", "visual:tone:" + feature + ":+1");
            }
        }


        private void DrawVisualColorPickerInline(RectangleF r, string feature, int colorIdx, int tone)
        {
            const float box = 18f;
            const float gap = 4f;

            colorIdx = ViClamp(colorIdx, 0, 7);
            tone = ViClamp(tone, 0, 10);

            EnsureVisualPickerBrushes();

            float x = r.Left;
            float y = r.Top + (r.Height - box) * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                RectangleF sw = new RectangleF(x, y, box, box);

                IBrush fill = _visualPickerFillBrushes[i, tone];
                if (fill != null)
                    fill.DrawRectangle(sw.Left, sw.Top, sw.Width, sw.Height);

                IBrush edge = (i == colorIdx) ? _visualPickerEdgeSelected : _visualPickerEdgeNormal;
                if (edge != null)
                    edge.DrawRectangle(sw.Left, sw.Top, sw.Width, sw.Height);

                RegisterToggleHit("visual:color:" + feature + ":" + i.ToString(CultureInfo.InvariantCulture), sw);

                x += box + gap;
            }
        }

        private void DrawVisualStepperWide(RectangleF r, string label, string valueText, string minusAction, string plusAction)
        {
            const float btnW = 24f;
            RectangleF minus = new RectangleF(r.Left, r.Top, btnW, r.Height);
            RectangleF plus  = new RectangleF(r.Right - btnW, r.Top, btnW, r.Height);
            RectangleF val   = new RectangleF(minus.Right + 4f, r.Top, Math.Max(30f, r.Width - btnW * 2f - 8f), r.Height);
            DrawGlossButton(minus, "-", IsVisualButtonFlashActive(minusAction), false, true);
            DrawGlossButton(val,   FitButtonLabel(label + " " + valueText, val.Width), false, false, true);
            DrawGlossButton(plus,  "+", IsVisualButtonFlashActive(plusAction), false, true);
            RegisterToggleHit(minusAction, minus);
            RegisterToggleHit(plusAction,  plus);
        }

        private static string FormatVisualFloat(float value)
        {
            if (Math.Abs(value - (float)Math.Round(value)) < 0.001f)
                return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }


                private static bool IsCustomVisualFeature(string feature)
        {
            return feature == "dangeraffixes" || feature == "menubutton" || feature == "partyinspector";
        }


                private int GetCustomVisualExpandedRowCount(string feature)
        {
            if (feature == "dangeraffixes")
                return 25;

            if (feature == "menubutton")
                return 2;

            if (feature == "partyinspector")
                return 1;

            return 0;
        }


        private void DrawPreviewDashCircle(IBrush brush, float cx, float cy, float r, int segments)
        {
            if (brush == null || segments <= 0 || r <= 0)
                return;

            for (int i = 0; i < segments; i += 2)
            {
                double a1 = (Math.PI * 2.0 * i) / segments;
                double a2 = (Math.PI * 2.0 * (i + 1)) / segments;
                float x1 = cx + (float)Math.Cos(a1) * r;
                float y1 = cy + (float)Math.Sin(a1) * r;
                float x2 = cx + (float)Math.Cos(a2) * r;
                float y2 = cy + (float)Math.Sin(a2) * r;
                brush.DrawLine(x1, y1, x2, y2);
            }
        }

        private void DrawPreviewSolidCircle(IBrush brush, float cx, float cy, float r)
        {
            if (brush == null || r <= 0)
                return;

            brush.DrawEllipse(cx, cy, r, r);
        }

        private void DrawPreviewMiniTextBar(RectangleF r, IBrush fill, string text)
        {
            if (fill != null)
                fill.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            if (_bPreviewBorder != null)
                _bPreviewBorder.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            if (!string.IsNullOrEmpty(text))
                DrawCenteredOutlinedTextExact(_fRowText, _fRowTextShadow, text, r);
        }

                private void DrawEffectPreviewBox(RectangleF box, string kind)
        {
            if (_bPreviewBlack != null)
                _bPreviewBlack.DrawRectangle(box.Left, box.Top, box.Width, box.Height);
            if (_bPreviewBorder != null)
                _bPreviewBorder.DrawRectangle(box.Left, box.Top, box.Width, box.Height);

            float cx = box.Left + box.Width * 0.5f;
            float cy = box.Top + box.Height * 0.5f;
            float maxR = Math.Min(box.Width, box.Height) * 0.42f;

            if (kind == "plague")
            {
                DrawPreviewDashCircle(_bPrevGreenDash, cx, cy, maxR * 0.72f, 28);
            }
            else if (kind == "arcanespawn")
            {
                DrawPreviewDashCircle(_bPrevPurpleDash, cx, cy, maxR * 0.42f, 18);
            }
            else if (kind == "moltenexplosion")
            {
                DrawPreviewDashCircle(_bPrevRedDash, cx, cy, maxR * 0.80f, 30);
            }
            else if (kind == "desecrator")
            {
                DrawPreviewDashCircle(_bPrevRedDash, cx, cy, maxR * 0.54f, 24);
            }
            else if (kind == "thunderstorm")
            {
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.86f, 30);

                if (_bPrevBlueDash != null)
                {
                    _bPrevBlueDash.DrawLine(cx - 7f, box.Top + 8f, cx + 4f, cy - 1f);
                    _bPrevBlueDash.DrawLine(cx + 4f, cy - 1f, cx - 3f, cy + 3f);
                    _bPrevBlueDash.DrawLine(cx - 3f, cy + 3f, cx + 8f, box.Bottom - 8f);
                }
            }
            else if (kind == "frozen")
            {
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.84f, 30);
            }
            else if (kind == "frozenpulse")
            {
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.76f, 28);
            }
            else if (kind == "ghom")
            {
                DrawPreviewDashCircle(_bPrevGreenDash, cx, cy, maxR * 0.96f, 34);
            }
            else if (kind == "grotesque")
            {
                DrawPreviewDashCircle(_bPrevRedDash, cx, cy, maxR * 0.96f, 34);
            }
            else if (kind == "orbiter")
            {
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.35f, 18);
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.92f, 34);
            }
            else if (kind == "waller")
            {
                if (_bPrevOrangeDash != null)
                {
                    float rw = maxR * 1.35f;
                    float rh = maxR * 0.42f;
                    _bPrevOrangeDash.DrawRectangle(
                        cx - rw * 0.5f,
                        cy - rh * 0.5f,
                        rw,
                        rh);
                }
            }
            else if (kind == "poisontrails")
            {
                DrawPreviewDashCircle(_bPrevGreenDash, cx, cy, maxR * 0.45f, 20);
                DrawPreviewDashCircle(_bPrevGreenDash, cx + maxR * 0.28f, cy - maxR * 0.18f, maxR * 0.28f, 14);
            }
            else if (kind == "bossfallingrocks")
            {
                DrawPreviewDashCircle(_bPrevOrangeDash, cx, cy, maxR * 0.82f, 30);
            }
            else if (kind == "bossleaptelegraphs")
            {
                DrawPreviewDashCircle(_bPrevRedDash, cx, cy, maxR * 0.90f, 32);
            }
            else if (kind == "bossmeteors")
            {
                DrawPreviewDashCircle(_bPrevOrangeDash, cx, cy, maxR * 0.72f, 28);
                DrawPreviewDashCircle(_bPrevRedDash, cx, cy, maxR * 0.38f, 18);
            }
            else if (kind == "bosskullehazards")
            {
                DrawPreviewDashCircle(_bPrevPurpleDash, cx, cy, maxR * 0.76f, 28);
                DrawPreviewDashCircle(_bPrevBlueDash, cx, cy, maxR * 0.48f, 20);
            }
            else if (kind == "bossblighterhazards")
            {
                DrawPreviewDashCircle(_bPrevGreenDash, cx, cy, maxR * 0.78f, 28);
                DrawPreviewDashCircle(_bPrevGreenDash, cx + maxR * 0.25f, cy - maxR * 0.15f, maxR * 0.34f, 16);
            }
            else if (kind == "bossratkinghazards")
            {
                DrawPreviewDashCircle(_bPrevPurpleDash, cx, cy, maxR * 0.86f, 32);
                DrawPreviewDashCircle(_bPrevOrangeDash, cx - maxR * 0.20f, cy + maxR * 0.10f, maxR * 0.38f, 18);
            }
            else if (kind == "labels")
            {
                DrawPreviewMiniTextBar(new RectangleF(box.Left + 10f, cy - 14f, box.Width - 20f, 13f), _bPrevOrangeDash, "MOLTEN");
                DrawPreviewMiniTextBar(new RectangleF(box.Left + 16f, cy + 4f, box.Width - 32f, 11f), _bPrevGrey, "FAST");
            }
            else if (kind == "dangerlabels")
            {
                DrawPreviewSolidCircle(_bPrevRedDash, box.Left + 18f, cy, 4f);

                RectangleF labelRect = new RectangleF(box.Left + 30f, cy - 7f, box.Width - 42f, 14f);

                if (_bPrevRedDash != null)
                    _bPrevRedDash.DrawRectangle(labelRect.Left, labelRect.Top, labelRect.Width, labelRect.Height);

                DrawCenteredOutlinedTextExact(_fRowText, _fRowTextShadow, "ANARCH", labelRect);
            }
            else
            {
                DrawPreviewDashCircle(_bPrevGrey, cx, cy, maxR * 0.70f, 20);
            }
        }


        private void DrawSingleToggleOptionRow(
            RectangleF r,
            int rowIdx,
            string title,
            string desc,
            bool enabled,
            string action,
            string previewKind)
        {
            if (string.IsNullOrWhiteSpace(title))
                return;

            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            bool showPreview =
                string.Equals(previewKind, "labels", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(previewKind, "dangerlabels", StringComparison.OrdinalIgnoreCase);

            float btnW = 74f;
            RectangleF btn = new RectangleF(r.Right - btnW - 8f, r.Top + 8f, btnW, r.Height - 16f);

            float textX;

            if (showPreview)
            {
                RectangleF preview = new RectangleF(r.Left + 8f, r.Top + 7f, 104f, r.Height - 14f);
                DrawEffectPreviewBox(preview, previewKind);
                textX = preview.Right + 12f;
            }
            else
            {
                textX = r.Left + 14f;
            }

            float textW = Math.Max(30f, btn.Left - textX - 10f);

            DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow, Trim(title, ApproxCharsForWidth(textW, 5.8f)), textX, r.Top + 8f);
            DrawOutlinedTextAt(_fRowText, _fRowTextShadow, Trim(desc, ApproxCharsForWidth(textW, 5.0f)), textX, r.Top + 30f);

            DrawGlossButton(btn, enabled ? "ON" : "OFF", enabled || IsVisualButtonFlashActive(action), false, true);
            RegisterToggleHit(action, btn);
        }

                private string PartyInspectorHotkeyLabel()
        {
            if (_partyInspectorHotkey == Key.Unknown)
                return "None";

            string s = _partyInspectorHotkey.ToString();
            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]))
                return s.Substring(1);

            return CompactKeyName(s);
        }

        private void DrawMenuButtonOptionsRow(RectangleF r, int rowIdx, bool openState)
        {
            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            string label = openState ? "Open" : "Closed";
            string feature = openState ? "menubuttonopen" : "menubuttonclosed";
            int colorIdx = openState ? _menuButtonOpenColorIdx : _menuButtonClosedColorIdx;
            int tone = openState ? _menuButtonOpenTone : _menuButtonClosedTone;
            float size = openState ? _menuButtonOpenSize : _menuButtonClosedSize;

            const float colorW = 172f;
            const float stepW = 128f;
            const float gap = 10f;
            const float stepH = 30f;

            float totalW = colorW + gap + stepW + gap + stepW;
            float x = r.Left + Math.Max(51f, (r.Width - totalW) * 0.5f);
            if (x + totalW > r.Right - 8f)
                x = r.Right - totalW - 8f;

            RectangleF labelR = new RectangleF(x, r.Top + 7f, colorW, 24f);
            RectangleF colorR = new RectangleF(x, r.Top + 39f, colorW, 22f);
            RectangleF toneR = new RectangleF(colorR.Right + gap, r.Top + 34f, stepW, stepH);
            RectangleF sizeR = new RectangleF(toneR.Right + gap, r.Top + 34f, stepW, stepH);

            DrawCenteredOutlinedTextExact(_fRowTitle, _fRowTitleShadow, label, labelR);
            DrawVisualColorPickerInline(colorR, feature, colorIdx, tone);

            DrawVisualStepperWide(
                toneR,
                "Tone",
                tone.ToString(CultureInfo.InvariantCulture),
                "visual:tone:" + feature + ":-1",
                "visual:tone:" + feature + ":+1");

            DrawVisualStepperWide(
                sizeR,
                "Size",
                FormatVisualFloat(size),
                "visual:size:" + feature + ":-1",
                "visual:size:" + feature + ":+1");
        }

        private void DrawPartyInspectorHotkeyRow(RectangleF r, int rowIdx)
        {
            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            const float resetW = 70f;
            const float hotkeyW = 112f;
            const float gap = 8f;

            RectangleF resetR = new RectangleF(r.Right - resetW - 8f, r.Top + 8f, resetW, r.Height - 16f);
            RectangleF hotkeyR = new RectangleF(resetR.Left - gap - hotkeyW, r.Top + 8f, hotkeyW, r.Height - 16f);

            float textX = r.Left + 14f;
            float textW = Math.Max(30f, hotkeyR.Left - textX - 12f);

            DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow, "Hotkey", textX, r.Top + 8f);
            DrawOutlinedTextAt(_fRowText, _fRowTextShadow,
                Trim("Press to change the Party Inspector expanded-panel key.", ApproxCharsForWidth(textW, 5.0f)),
                textX, r.Top + 30f);

            string label = _partyInspectorHotkeyCapture ? "PRESS..." : "Hotkey = " + PartyInspectorHotkeyLabel();
            DrawGlossButton(hotkeyR, label, _partyInspectorHotkeyCapture, false, true);
            RegisterToggleHit("visual:hotkey:partyinspector", hotkeyR);

            bool resetActive = IsVisualButtonFlashActive("visual:reset:partyinspector");
            DrawGlossButton(resetR, "Reset", resetActive, false, true);
            RegisterToggleHit("visual:reset:partyinspector", resetR);
        }

        private void DrawCustomVisualOptionsRow(RectangleF r, string feature, int rowIdx, int part)
        {
            if (feature == "partyinspector")
            {
                DrawPartyInspectorHotkeyRow(r, rowIdx);
                return;
            }

            if (feature == "menubutton")
            {
                DrawMenuButtonOptionsRow(r, rowIdx, part != 0);
                return;
            }

            if (feature == "dangeraffixes")
            {
                switch (part)
                {
                    case 0:
                        DrawSingleToggleOptionRow(r, rowIdx, "Plague Pools", "Green ground pools; off by default for clutter.", _affixPlagued, "affix:plagued", "plague");
                        return;

                    case 1:
                        DrawSingleToggleOptionRow(r, rowIdx, "Arcane Spawn", "Purple warning circle before an arcane beam appears.", _affixArcaneSpawn, "affix:arcanespawn", "arcanespawn");
                        return;

                    case 2:
                        DrawSingleToggleOptionRow(r, rowIdx, "Molten Explosion", "Red warning circle for the molten death explosion.", _affixMoltenExplosion, "affix:moltenexplosion", "moltenexplosion");
                        return;

                    case 3:
                        DrawSingleToggleOptionRow(r, rowIdx, "Desecrator", "Red warning circle for the fire damage-over-time affix on the ground.", _affixDesecrator, "affix:desecrator", "desecrator");
                        return;

                    case 4:
                        DrawSingleToggleOptionRow(r, rowIdx, "Thunderstorm", "Large lightning strike warning circle.", _affixThunderstorm, "affix:thunderstorm", "thunderstorm");
                        return;

                    case 5:
                        DrawSingleToggleOptionRow(r, rowIdx, "Frozen", "Blue warning circle for the Frozen affix explosion.", _affixFrozen, "affix:frozen", "frozen");
                        return;

                    case 6:
                        DrawSingleToggleOptionRow(r, rowIdx, "Frozen Pulse", "Moving blue cold pulse warning circle.", _affixFrozenPulse, "affix:frozenpulse", "frozenpulse");
                        return;

                    case 7:
                        DrawSingleToggleOptionRow(r, rowIdx, "Ghom Gas", "Large green poison cloud warning.", _affixGhomGas, "affix:ghom", "ghom");
                        return;

                    case 8:
                        DrawSingleToggleOptionRow(r, rowIdx, "Grotesque Explosion", "Large red corpse explosion warning.", _explosiveGrotesque, "affix:grotesque", "grotesque");
                        return;

                    case 9:
                        DrawSingleToggleOptionRow(r, rowIdx, "Orbiter", "Warning overlays for the small and big orbs.", _affixOrbiter, "affix:orbiter", "orbiter");
                        return;

                    case 10:
                        DrawSingleToggleOptionRow(r, rowIdx, "Waller", "Orange wall warning outlines.", _affixWaller, "affix:waller", "waller");
                        return;

                    case 11:
                        DrawSingleToggleOptionRow(r, rowIdx, "Wormhole", "Wormhole mine warning overlay.", _affixWormholeMines, "affix:wormholemines", "wormhole");
                        return;

                    case 12:
                        DrawSingleToggleOptionRow(r, rowIdx, "Falling Rocks", "Perendi's falling-rock warning circle.", _affixBossFallingRocks, "affix:bossfallingrocks", "bossfallingrocks");
                        return;

                    case 13:
                        DrawSingleToggleOptionRow(r, rowIdx, "Leap Telegraph", "Bloodmaw leap warning overlay.", _affixBossLeapTelegraphs, "affix:bossleaptelegraphs", "bossleaptelegraphs");
                        return;

                    case 14:
                        DrawSingleToggleOptionRow(r, rowIdx, "Meteors", "Ember meteor warning circle.", _affixBossMeteors, "affix:bossmeteors", "bossmeteors");
                        return;

                    case 15:
                        DrawSingleToggleOptionRow(r, rowIdx, "Zoltun Kulle", "Zoltun Kulle boulder warning.", _affixBossKulleHazards, "affix:bosskullehazards", "bosskullehazards");
                        return;

                    case 16:
                        DrawSingleToggleOptionRow(r, rowIdx, "Blighter", "Blighter's poison warning circle overlays.", _affixBossBlighterHazards, "affix:bossblighterhazards", "bossblighterhazards");
                        return;

                    case 17:
                        DrawSingleToggleOptionRow(r, rowIdx, "Hamelin / Rat King", "Rat cloud warning overlays.", _affixBossRatKingHazards, "affix:bossratkinghazards", "bossratkinghazards");
                        return;

                    case 18:
                        DrawSingleToggleOptionRow(r, rowIdx, "Tethrys Geysers", "Tethrys geyser warning circles.", _affixBossAdriaGeysers, "affix:bossadriageysers", "bossadriageysers");
                        return;

                    case 19:
                        DrawSingleToggleOptionRow(r, rowIdx, "Butcher Fire", "Butcher fire warning circle.", _affixBossButcherFire, "affix:bossbutcherfire", "bossbutcherfire");
                        return;

                    case 20:
                        DrawSingleToggleOptionRow(r, rowIdx, "Rime", "Rime cold warning circle overlays.", _affixBossRimeHazards, "affix:bossrimehazards", "bossrimehazards");
                        return;

                    case 21:
                        DrawSingleToggleOptionRow(r, rowIdx, "Shock Tower", "Shock tower danger-zone overlay.", _affixShockTowerHazards, "affix:shocktowerhazards", "shocktowerhazards");
                        return;

                    case 22:
                        DrawSingleToggleOptionRow(r, rowIdx, "Stonesinger Turret", "Stonesinger turret warning circle.", _affixBossSandmonsterTurretHazards, "affix:bosssandmonsterturret", "bosssandmonsterturret");
                        return;

                    case 23:
                        DrawSingleToggleOptionRow(r, rowIdx, "Elite Affix Labels", "Text labels showing elite affixes above packs.", _eliteAffixLabelsEnabled, "affix:labels", "labels");
                        return;

                    default:
                        DrawSingleToggleOptionRow(r, rowIdx, "Dangerous Monster Labels", "Red labels and dots for dangerous monster types.", _dangerousMonsterLabelsEnabled, "affix:dangerlabels", "dangerlabels");
                        return;
                }
            }
        }



        private void DrawToggleVisualCategory(RectangleF detail)
        {
            const float sbW = 18f;
            float x = detail.Left + 8f;
            float w = detail.Width - sbW - 22f;

            // Fixed header (not scrolled)
            float headerY = detail.Top + 8f;
            _fSection.DrawText("VISUAL", x + 10f, headerY + 4f);
            _fLabel.DrawText("World-space circles, target indicators, and click visuals.", x + 10f, headerY + 30f);

            float listTop = headerY + 58f;
            float clipTop = listTop;
            float clipBot = detail.Bottom - 8f;

            string[] feats =
            {
                "dangeraffixes",
                "menubutton",
                "partyinspector",
                "click",
                "player",
                "mouse",
                "line",
                "reticle",
                "outline",
                "minion",
                "siphon"
            };

            string[] ftitles =
            {
                "Elite/Dangerous Affix Visuals",
                "Menu Button",
                "Party Inspector",
                "Click Animation",
                "Player Circle",
                "Mouse Circle",
                "Line to Target",
                "Target Reticle",
                "Reticle Outline",
                "Minion Circles",
                "Siphon Debuff Circle"
            };

            string[] fdescs =
            {
                "Enable or disable elite affix/danger visual effects.",
                "Show/hide and customize the HUD Menu open/close button.",
                "Expanded party build inspector. Default hotkey: F12.",
                "Short color burst when clicking inside this HUD menu.",
                "Draws a ground circle around your hero. Adjustable color, radius, and thickness.",
                "Draws a world circle at the cursor position. Adjustable color, radius, and thickness.",
                "Draws a colored line from your hero to the selected target.",
                "Animated three-ring reticle on selected monster. Adjustable color and size.",
                "Outer ring drawn around selected monster. Adjustable color and thickness.",
                "Draws ground circles on elite minions.",
                "Rotating dotted circle on elites affected by Siphon Blood."
            };

            bool[] fenabled =
            {
                _visDangerousAffixVisualsEnabled,
                _showDot,
                IsPartyInspectorEnabled(),
                _visClickAnimEnabled,
                _visPlayerCircleEnabled,
                _visMouseCircleEnabled,
                _visLineToTargetEnabled,
                _visTargetReticleEnabled,
                _visReticleOutlineEnabled,
                _visMinionCirclesEnabled,
                _visSiphonEnabled
            };

            bool[] fexpanded =
            {
                _visDangerousAffixVisualsExpanded,
                _visMenuButtonExpanded,
                _visPartyInspectorExpanded,
                _visClickAnimExpanded,
                _visPlayerCircleExpanded,
                _visMouseCircleExpanded,
                _visLineToTargetExpanded,
                _visTargetReticleExpanded,
                _visReticleOutlineExpanded,
                _visMinionCirclesExpanded,
                _visSiphonExpanded
            };

            int[] fcolor =
            {
                0,
                0,
                0,
                _visClickAnimColorIdx,
                _visPlayerCircleColorIdx,
                _visMouseCircleColorIdx,
                _visLineToTargetColorIdx,
                _visTargetReticleColorIdx,
                _visReticleOutlineColorIdx,
                _visMinionColorIdx,
                _visSiphonColorIdx
            };

            int[] ftone =
            {
                0,
                0,
                0,
                _visClickAnimTone,
                _visPlayerCircleTone,
                _visMouseCircleTone,
                _visLineToTargetTone,
                _visTargetReticleTone,
                _visReticleOutlineTone,
                _visMinionTone,
                _visSiphonTone
            };

            // Build ordered item list (favorites first)
            var favIdx  = new List<int>();
            var mainIdx = new List<int>();
            for (int fi = 0; fi < feats.Length; fi++)
                (_visualFavorites.Contains(feats[fi]) ? favIdx : mainIdx).Add(fi);

            var ordered = new List<int>();
            ordered.AddRange(favIdx);
            ordered.AddRange(mainIdx);

            // Count total scrollable items (fav header + feature rows + expanded options rows)
            int totalItems = (favIdx.Count > 0 ? 1 : 0);
            foreach (int fi in ordered)
            {
                totalItems++;
                if (fexpanded[fi])
                    totalItems += IsCustomVisualFeature(feats[fi]) ? GetCustomVisualExpandedRowCount(feats[fi]) : 1;
            }

            _visualTotalItems = totalItems;
            RectangleF listRect = new RectangleF(x, listTop, w, Math.Max(1f, clipBot - listTop));
            _visualVisibleItems = Math.Max(1, (int)Math.Floor((listRect.Height - 4f) / VisualListSlotH));
            ClampVisualScroll();
            DrawPluginsStyleVisualScrollbar(detail);

            int ri = 0;
            int itemIdx = 0;
            float drawY = listTop;

            // Favorites header
            if (favIdx.Count > 0)
            {
                if (itemIdx >= _visualScroll)
                {
                    float ry = drawY;
                    if (ry >= clipTop && ry + 30f <= clipBot)
                    {
                        _bTitle.DrawRectangle(x, ry, w, 30f);
                        _bPaneBorder.DrawRectangle(x, ry, w, 30f);
                        _fSection.DrawText("★  Favorites", x + 12f, ry + 6f);
                    }
                    drawY += 34f;
                }
                itemIdx++;
            }

            foreach (int fi in ordered)
            {
                float rowH = VisualListSlotH;

                if (itemIdx >= _visualScroll)
                {
                    float ry = drawY;
                    if (ry >= clipTop && ry + rowH <= clipBot)
                        DrawVisualFeatureRow(new RectangleF(x, ry, w, rowH - 8f), feats[fi], ftitles[fi], fdescs[fi], fenabled[fi], fexpanded[fi], ri);
                    drawY += rowH;
                }
                ri++;
                itemIdx++;

                if (fexpanded[fi])
                {
                    string feature = feats[fi];
                    int expandedRows = IsCustomVisualFeature(feature) ? GetCustomVisualExpandedRowCount(feature) : 1;

                    for (int part = 0; part < expandedRows; part++)
                    {
                        if (itemIdx >= _visualScroll)
                        {
                            float ry = drawY;
                            if (ry >= clipTop && ry + VisualListSlotH <= clipBot)
                            {
                                if (IsCustomVisualFeature(feature))
                                {
                                    DrawCustomVisualOptionsRow(
                                        new RectangleF(x, ry, w, VisualListSlotH - 8f),
                                        feature,
                                        ri++,
                                        part);
                                }
                                else
                                {
                                    bool hasYards = feature == "player" || feature == "mouse";
                                    bool hasThick = feature == "player" || feature == "mouse" || feature == "line" || feature == "outline" || feature == "minion";
                                    bool hasSize = feature == "reticle";
                                    bool hasDot = feature == "siphon";

                                    float yards =
                                        feature == "player" ? _visPlayerCircleYards :
                                        feature == "mouse" ? _visMouseCircleYards :
                                        0f;

                                    float thick =
                                        feature == "player" ? _visPlayerCircleThickness :
                                        feature == "mouse" ? _visMouseCircleThickness :
                                        feature == "line" ? _visLineToTargetThickness :
                                        feature == "outline" ? _visReticleOutlineThickness :
                                        feature == "minion" ? _visMinionThickness :
                                        0f;

                                    float size = feature == "reticle" ? _visTargetReticleSize : 0f;
                                    float dot = feature == "siphon" ? _visSiphonDotSize : 0f;

                                    DrawVisualOptionsRow(
                                        new RectangleF(x, ry, w, VisualListSlotH - 8f),
                                        feature,
                                        ri++,
                                        fcolor[fi],
                                        ftone[fi],
                                        feature != "click",
                                        hasYards,
                                        yards,
                                        hasThick,
                                        thick,
                                        hasSize,
                                        size,
                                        hasDot,
                                        dot,
                                        "Tone",
                                        "Yards",
                                        "Line");
                                }
                            }
                            drawY += VisualListSlotH;
                        }
                        itemIdx++;
                    }
                }
            }

            // Edge cover + border
            if (_bPaneCover != null)
            {
                _bPaneCover.DrawRectangle(detail.Left + 1f, detail.Top + 1f,    detail.Width - 2f, 2f);
                _bPaneCover.DrawRectangle(detail.Left + 1f, detail.Bottom - 3f, detail.Width - 2f, 3f);
            }
            _bPaneBorder.DrawRectangle(detail.Left, detail.Top, detail.Width, detail.Height);
        }



        // ── Macros detail internal scroll state ──────────────────────────────────
        // Called from DrawToggleMacrosCategory; helper draws one class section.


        // ─────────────────────────────────────────────────────────────────────
        // MACROS category — class-structured conditional profile list with scroll,
        // real AutoSkill toggle state, star favorites, and button-only click zones.
        // ─────────────────────────────────────────────────────────────────────────

        // Entry descriptor used to build the macros list
        private struct MacroEntry
        {
            public string Title;
            public string Description;
            public string Code;
            public bool   IsBuff;        // true = AutoBuffProfile, false = ConditionalCastProfile
            public bool   IsPlugin;      // true = plugin row (DH Strafe, Exit Archon)
            public bool   IsOpenGrMapSelector;
            public bool   IsOpenGrMapChild;
            public bool   IsAutoSkillKeybindSelector;
            public bool   IsAutoSkillKeybindChild;
            public RiftMapGroup RiftGroup;
            public RiftMapGroup RiftGroupRight;
            public string[] PluginTypeNames;
            public string PluginAction;
            public bool HasHotkeyButton;
            public string HotkeyAction;
        }

        private sealed class RiftMapGroup
        {
            public readonly string Key;
            public readonly string Title;
            public readonly uint[] Ids;

            public RiftMapGroup(string key, string title, params uint[] ids)
            {
                Key = key;
                Title = title;
                Ids = ids ?? new uint[0];
            }
        }

        private static readonly RiftMapGroup[] RiftMapGroups =
        {
            // Commonly preferred maps first.
            new RiftMapGroup("festering", "The Festering Woods", 24),
            new RiftMapGroup("fields", "Fields of Misery", 9),
            new RiftMapGroup("battlefield", "Battlefields of Eternity", 30),
            new RiftMapGroup("briarthorn", "Briarthorn Cemetery", 32),
            new RiftMapGroup("moors", "Shrouded Moors", 22),
            new RiftMapGroup("greyhollow", "Greyhollow Island", 19),
            new RiftMapGroup("eternal", "Eternal Woods", 20),
            new RiftMapGroup("firstborn", "Temple of the Firstborn", 21),
            new RiftMapGroup("vault", "Vault of the Assassin", 10),
            new RiftMapGroup("desolate", "Desolate Sands", 2),
            new RiftMapGroup("desert", "Desert", 23),
            new RiftMapGroup("halls", "Halls of Agony", 1),
            new RiftMapGroup("keep", "The Keep Depths", 17),
            new RiftMapGroup("spire", "The Silver Spire", 18),
            new RiftMapGroup("banished", "Realm of the Banished", 31),
            new RiftMapGroup("fortress", "Pandemonium Fortress", 29),
            new RiftMapGroup("corvus", "Ruins of Corvus", 28),
            new RiftMapGroup("plague", "Plague Tunnels", 27),
            new RiftMapGroup("cathedral", "Cathedral", 25),
            new RiftMapGroup("crypt", "Crypt", 26),
            new RiftMapGroup("archives", "Archives of Zoltun Kulle", 11),
            new RiftMapGroup("arreat", "Arreat Crater", 15),
            new RiftMapGroup("icefall", "Icefall Cave", 16),
            new RiftMapGroup("araneae", "Caverns of Araneae", 8),
            new RiftMapGroup("floodedcave", "Flooded Cave", 3),
            new RiftMapGroup("betrayer", "Cave of the Betrayer", 4),
            new RiftMapGroup("tidalcave", "Tidal Cave", 5),
            new RiftMapGroup("cave", "Cave", 6),
            new RiftMapGroup("windingcave", "Winding Cave", 7),
            // Duplicate/same-family variants grouped into one visible row.
            new RiftMapGroup("hellrift", "Hell Rift", 12, 13, 14),
            new RiftMapGroup("westmarch", "Westmarch", 33, 34),
        };

        private enum MacroListItemKind
        {
            Title = 0,
            Section = 1,
            Entry = 2
        }

        private struct MacroListItem
        {
            public MacroListItemKind Kind;
            public string SectionTitle;
            public MacroEntry Entry;
            public bool IsFavoriteDisplay;
            public bool IsCompactOpenGrMapRow;
        }

        private const float MacroListSlotH = 84f;
        private const float OpenGrMapChildSlotH = 22f;
        private const float AutoSkillKeybindChildSlotH = 58f;
        private const float MacroEntryH = 76f;
        private const float MacroHeaderH = 36f;

        private float GetMacroListItemSlotHeight(MacroListItem item)
        {
            if (item.IsCompactOpenGrMapRow)
                return OpenGrMapChildSlotH;

            if (item.Entry.IsAutoSkillKeybindChild)
                return AutoSkillKeybindChildSlotH;

            return MacroListSlotH;
        }


        private static readonly MacroEntry[] MacroFavoritesSentinel = new MacroEntry[0]; // unused; just for clarity

        private void DrawMacrosFixedHeader(RectangleF detail)
        {
            _fSection.DrawText("MACROS", detail.Left + 14f, detail.Top + 10f);
            _fLabel.DrawText("Autocast conditional rules per class.", detail.Left + 14f, detail.Top + 34f);
        }

        private void DrawMacroSectionItem(RectangleF r, string title)
        {
            RectangleF rr = new RectangleF(r.Left, r.Top + 4f, r.Width, MacroHeaderH);

            _bTitle.DrawRectangle(rr.Left, rr.Top, rr.Width, rr.Height);
            _bPaneBorder.DrawRectangle(rr.Left, rr.Top, rr.Width, rr.Height);
            _fSection.DrawText(title, rr.Left + 12f, rr.Top + 7f);
        }

        private void DrawMacroEntryRow(RectangleF slot, MacroEntry entry, int rowIdx, bool isFav)
        {
            if (entry.IsOpenGrMapChild)
            {
                DrawOpenGrMapChildRow(slot, entry, rowIdx);
                return;
            }

            if (entry.IsAutoSkillKeybindChild)
            {
                DrawAutoSkillKeybindChildRow(slot, rowIdx);
                return;
            }

            const float starW  = 32f;
            const float stateW = 138f;
            const float hotkeyW = 86f;
            const float buttonGap = 8f;

            RectangleF rr = new RectangleF(slot.Left, slot.Top + 3f, slot.Width, MacroEntryH);

            bool enabled = false;
            bool installed = true;

            if (entry.IsOpenGrMapSelector)
            {
                IPlugin plugin = GetRiftFishingPlugin();
                installed = plugin != null;
                enabled = _openGrMapsExpanded;
            }
            else if (entry.IsAutoSkillKeybindSelector)
            {
                installed = GetAutoSkillPlugin() != null;
                enabled = _autoSkillKeybindsExpanded;
            }
            else if (entry.IsPlugin)
            {
                IPlugin plugin = FindPluginByTypeName(entry.PluginTypeNames);
                installed = plugin != null;
                enabled = installed && SafePluginEnabled(plugin);
            }
            else
            {
                enabled = GetMacroEnabled(entry.Code, entry.IsBuff);
            }

            (rowIdx % 2 == 0 ? _bRow : _bRowAlt).DrawRectangle(rr.Left, rr.Top, rr.Width, rr.Height);

            RectangleF starR = new RectangleF(rr.Left + 7f, rr.Top + (rr.Height - 32f) * 0.5f, starW, 32f);
            DrawStarButton(starR, isFav);
            RegisterToggleHit("macro:star:" + entry.Code, starR);

            RectangleF stateR = new RectangleF(rr.Right - stateW - 8f, rr.Top + 6f, stateW, rr.Height - 12f);

            RectangleF hotkeyR = RectangleF.Empty;
            if (entry.HasHotkeyButton)
                hotkeyR = new RectangleF(stateR.Left - hotkeyW - buttonGap, rr.Top + 6f, hotkeyW, rr.Height - 12f);

            float textX = starR.Right + 10f;
            float textRight = entry.HasHotkeyButton ? hotkeyR.Left - 10f : stateR.Left - 10f;
            float textW = Math.Max(40f, textRight - textX);

            DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow,
                Trim(entry.Title, ApproxCharsForWidth(textW, 5.8f)), textX, rr.Top + 7f);

            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                string[] descLines = WrapToggleDescription(
                    entry.Description, ApproxCharsForToggleDescription(textW), 2);

                if (descLines.Length > 0)
                    DrawOutlinedTextAt(_fRowText, _fRowTextShadow, descLines[0], textX, rr.Top + 30f);

                if (descLines.Length > 1)
                    DrawOutlinedTextAt(_fRowText, _fRowTextShadow, descLines[1], textX, rr.Top + 47f);
            }

            string stateLabel;
            if (entry.IsOpenGrMapSelector)
                stateLabel = installed ? (_openGrMapsExpanded ? "-" : "+") : "NOT INSTALLED";
            else if (entry.IsAutoSkillKeybindSelector)
                stateLabel = installed ? (_autoSkillKeybindsExpanded ? "-" : "+") : "NOT INSTALLED";
            else if (entry.IsPlugin)
                stateLabel = installed ? (enabled ? "ON" : "OFF") : "NOT INSTALLED";
            else
                stateLabel = enabled ? "ON" : "OFF";

            bool canToggle = entry.IsOpenGrMapSelector ? installed : (entry.IsAutoSkillKeybindSelector ? installed : (entry.IsPlugin ? installed : GetAutoSkillPlugin() != null));

            int flashTk;
            _macroToggleFlashTicks.TryGetValue(entry.Code, out flashTk);
            if (flashTk == 0)
                flashTk = int.MinValue;

            if (entry.HasHotkeyButton)
            {
                string hotkeyLabel = GetZBarbAutoSnapHotkeyLabel();
                DrawGlossButton(hotkeyR, FitButtonLabel(hotkeyLabel, hotkeyR.Width), _zbarbAutoSnapHotkeyCapture, false, false);
                RegisterToggleHit(entry.HotkeyAction, hotkeyR);
            }

            // Persistent enabled-state color.
            DrawMacroStateButton(stateR, stateLabel, enabled);

            // Short glow pulse after clicking.
            if (IsFlashActive(flashTk))
                DrawGlowRings(stateR);

            if (canToggle)
            {
                if (entry.IsOpenGrMapSelector)
                    RegisterToggleHit("riftmaps:expand", stateR);
                else if (entry.IsAutoSkillKeybindSelector)
                    RegisterToggleHit("autoskill:keybinds:expand", stateR);
                else if (entry.IsPlugin)
                    RegisterToggleHit(entry.PluginAction, stateR);
                else
                    RegisterToggleHit("macro:toggle:" + (entry.IsBuff ? "buff:" : "conditional:") + entry.Code, stateR);
            }
        }


        private void DrawMacroStateButton(RectangleF r, string label, bool enabled)
        {
            if (string.Equals(label, "NOT INSTALLED", StringComparison.OrdinalIgnoreCase))
            {
                DrawGlossButton(r, string.Empty, false, false, false);

                float lineH = r.Height * 0.38f;
                float totalH = lineH * 2f;
                float top = r.Top + (r.Height - totalH) * 0.5f;

                RectangleF line1 = new RectangleF(r.Left, top - 1f, r.Width, lineH);
                RectangleF line2 = new RectangleF(r.Left, top + lineH + 1f, r.Width, lineH);

                DrawCenteredOutlinedTextExact(_fRowText, _fRowTextShadow, "NOT", line1);
                DrawCenteredOutlinedTextExact(_fRowText, _fRowTextShadow, "INSTALLED", line2);
                return;
            }

            DrawGlossButton(r, FitButtonLabel(label, r.Width), enabled, false, false);
        }

        private List<MacroListItem> BuildMacroListItems(List<MacroEntry> allEntries, int[] classStarts, string[] classNames)
        {
            var items = new List<MacroListItem>();

            var favEntries = allEntries
                .Where(e => _macroFavorites.Contains(e.Code))
                .ToList();

            bool openGrFavorited = _macroFavorites.Contains("OpenGR_MapSelector");
            bool autoSkillKeybindsFavorited = _macroFavorites.Contains("AutoSkill_Keybindings");

            if (favEntries.Count > 0)
            {
                items.Add(new MacroListItem
                {
                    Kind = MacroListItemKind.Section,
                    SectionTitle = "★  Favorites"
                });

                foreach (MacroEntry entry in favEntries)
                {
                    items.Add(new MacroListItem
                    {
                        Kind = MacroListItemKind.Entry,
                        Entry = entry,
                        IsFavoriteDisplay = true
                    });

                    if (entry.IsOpenGrMapSelector && _openGrMapsExpanded)
                    {
                        for (int mi = 0; mi < RiftMapGroups.Length; mi += 2)
                        {
                            items.Add(new MacroListItem
                            {
                                Kind = MacroListItemKind.Entry,
                                Entry = new MacroEntry
                                {
                                    Title = RiftMapGroups[mi].Title,
                                    Description = string.Empty,
                                    Code = "OpenGR_MapPair_" + RiftMapGroups[mi].Key,
                                    IsOpenGrMapChild = true,
                                    RiftGroup = RiftMapGroups[mi],
                                    RiftGroupRight = (mi + 1 < RiftMapGroups.Length) ? RiftMapGroups[mi + 1] : null
                                },
                                IsFavoriteDisplay = true,
                                IsCompactOpenGrMapRow = true
                            });
                        }
                    }

                    if (entry.IsAutoSkillKeybindSelector && _autoSkillKeybindsExpanded)
                    {
                        items.Add(new MacroListItem
                        {
                            Kind = MacroListItemKind.Entry,
                            Entry = new MacroEntry
                            {
                                Title = "AutoSkill Keybind Buttons",
                                Description = string.Empty,
                                Code = "AutoSkill_Keybindings_Row",
                                IsAutoSkillKeybindChild = true
                            },
                            IsFavoriteDisplay = true
                        });
                    }
                }
            }

            for (int si = 0; si < classNames.Length; si++)
            {
                int start = classStarts[si];
                int end = si + 1 < classStarts.Length ? classStarts[si + 1] : allEntries.Count;

                items.Add(new MacroListItem
                {
                    Kind = MacroListItemKind.Section,
                    SectionTitle = classNames[si]
                });

                for (int ei = start; ei < end; ei++)
                {
                    MacroEntry entry = allEntries[ei];

                    if (_macroFavorites.Contains(entry.Code))
                        continue;

                    if (entry.IsOpenGrMapChild && openGrFavorited)
                        continue;

                    if (entry.IsAutoSkillKeybindChild && autoSkillKeybindsFavorited)
                        continue;

                    items.Add(new MacroListItem
                    {
                        Kind = MacroListItemKind.Entry,
                        Entry = entry,
                        IsFavoriteDisplay = false,
                        IsCompactOpenGrMapRow = entry.IsOpenGrMapChild
                    });
                }
            }

            return items;
        }



                private void DrawOpenGrMapChildRow(RectangleF r, MacroEntry entry, int rowIdx)
        {
            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float gap = 10f;
            float colW = (r.Width - gap) * 0.5f;

            DrawOpenGrMapCell(
                new RectangleF(r.Left, r.Top, colW, r.Height),
                entry.RiftGroup);

            if (entry.RiftGroupRight != null)
            {
                DrawOpenGrMapCell(
                    new RectangleF(r.Left + colW + gap, r.Top, colW, r.Height),
                    entry.RiftGroupRight);
            }
        }

        private void DrawOpenGrMapCell(RectangleF cell, RiftMapGroup group)
        {
            if (group == null)
                return;

            bool enabled = RiftMapGroupEnabled(group);

            float checkSize = 11f;
            RectangleF check = new RectangleF(
                cell.Left + 8f,
                cell.Top + (cell.Height - checkSize) * 0.5f,
                checkSize,
                checkSize);

            DrawSquareCheck(check, enabled);

            float textX = check.Right + 7f;
            float textW = cell.Right - textX - 6f;

            DrawOutlinedTextAt(
                _fRowText,
                _fRowTextShadow,
                Trim(group.Title ?? string.Empty, ApproxCharsForWidth(textW, 5.2f)),
                textX,
                cell.Top + (cell.Height - 12f) * 0.5f);

            RegisterToggleHit(
                "riftmap:" + group.Key,
                new RectangleF(cell.Left, cell.Top, cell.Width, cell.Height));
        }


        private void DrawAutoSkillKeybindChildRow(RectangleF r, int rowIdx)
        {
            (rowIdx % 2 == 0 ? _bRowAlt : _bRow).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float pad = 8f;
            float gap = 6f;
            int count = _autoSkillBindActions == null ? 0 : _autoSkillBindActions.Length;
            if (count <= 0)
                return;

            float cellW = Math.Max(36f, (r.Width - pad * 2f - gap * (count - 1)) / count);
            float x = r.Left + pad;

            for (int i = 0; i < count; i++)
            {
                RectangleF labelR = new RectangleF(x, r.Top + 5f, cellW, 14f);
                RectangleF btnR = new RectangleF(x, r.Top + 24f, cellW, 25f);

                DrawCenteredOutlinedTextExact(_fRowText, _fRowTextShadow, _autoSkillBindSlotLabels[i], labelR);

                bool fixedMouse = _autoSkillBindActions[i] == ActionKey.LeftSkill || _autoSkillBindActions[i] == ActionKey.RightSkill;
                bool capturing = _autoSkillKeybindCaptureSlot == i;
                string label = capturing ? "..." : GetAutoSkillKeybindButtonLabel(i);

                DrawGlossButton(btnR, FitButtonLabel(label, btnR.Width), capturing, false, true);

                if (!fixedMouse)
                    RegisterToggleHit("autoskill:keybind:" + i.ToString(CultureInfo.InvariantCulture), btnR);

                x += cellW + gap;
            }
        }

        private void DrawToggleMacrosCategory(RectangleF detail)
        {
            // ── Build ordered entry list (favorites first) ────────────────────
            // Map: code → entry
            var allEntries = new List<MacroEntry>
            {
                new MacroEntry
                {
                    Title="OpenGR Map Selector",
                    Description="Choose which normal Greater Rift maps stop RiftFishing. Orek's Dream is always selected.",
                    Code="OpenGR_MapSelector",
                    IsOpenGrMapSelector=true
                },
            };

            if (_openGrMapsExpanded)
            {
                for (int i = 0; i < RiftMapGroups.Length; i += 2)
                {
                    allEntries.Add(new MacroEntry
                    {
                        Title = RiftMapGroups[i].Title,
                        Description = string.Empty,
                        Code = "OpenGR_MapPair_" + RiftMapGroups[i].Key,
                        IsOpenGrMapChild = true,
                        RiftGroup = RiftMapGroups[i],
                        RiftGroupRight = (i + 1 < RiftMapGroups.Length) ? RiftMapGroups[i + 1] : null
                    });
                }
            }

            allEntries.Add(new MacroEntry
            {
                Title="AutoSkill Keybindings",
                Description="Set the keyboard cast keys AutoSkill sends for skill slots and potion.",
                Code="AutoSkill_Keybindings",
                IsAutoSkillKeybindSelector=true
            });

            if (_autoSkillKeybindsExpanded)
            {
                allEntries.Add(new MacroEntry
                {
                    Title="AutoSkill Keybind Buttons",
                    Description=string.Empty,
                    Code="AutoSkill_Keybindings_Row",
                    IsAutoSkillKeybindChild=true
                });
            }

            int barbarianStart = allEntries.Count;

            allEntries.AddRange(new []
            {
                // ── Barbarian ──────────────────────────────────────────────────
                new MacroEntry {
                    Title="Z-Barb Autosnap",
                    Description="Experimental spear-pull assist.\nPrefers elites aligned near the cursor.",
                    Code="zbarb_autosnap_plugin",
                    IsPlugin=true,
                    PluginTypeNames=ZBarbAutoSnapPluginTypeNames,
                    PluginAction="toggles:plugin:zbarbautosnap",
                    HasHotkeyButton=true,
                    HotkeyAction="macro:hotkey:zbarbautosnap"
                },
                new MacroEntry { Title="War Cry",              Description="Keeps War Cry armor/resist buff active between casts.",              Code="Barbarian_WarCry_KeepUp",           IsBuff=false },
                new MacroEntry { Title="Battle Rage",          Description="Maintains Battle Rage fury-spending buff for crit and damage.",      Code="Barbarian_BattleRage_KeepUp",       IsBuff=false },
                new MacroEntry { Title="Sprint",               Description="Keeps Sprint active while monsters are nearby.",                     Code="Barbarian_Sprint_KeepUpMoving",     IsBuff=false },
                new MacroEntry { Title="Ignore Pain",          Description="Casts Ignore Pain when health is low or elites are nearby.",         Code="Barbarian_IgnorePain_Defensive",    IsBuff=false },
                new MacroEntry { Title="Wrath of the Berserker",Description="Activates WotB on elite/boss or high monster density.",           Code="Barbarian_WOTB_EliteOrDensity",     IsBuff=false },
                new MacroEntry { Title="Call of the Ancients", Description="Summons Ancients in combat when elites or density are nearby.",       Code="Barbarian_COTA_Combat",             IsBuff=false },
                // ── Crusader ──────────────────────────────────────────────────
                new MacroEntry { Title="Laws of Valor",        Description="Activates Law of Valor near elites or bosses.",                      Code="Crusader_LawsOfValor_Elite",        IsBuff=false },
                new MacroEntry { Title="Laws of Justice",      Description="Activates Law of Justice when health is low or elites are nearby.",  Code="Crusader_LawsOfJustice_Defensive",  IsBuff=false },
                new MacroEntry { Title="Laws of Hope",         Description="Activates Law of Hope when health drops below threshold.",            Code="Crusader_LawsOfHope_Defensive",     IsBuff=false },
                new MacroEntry { Title="Akarat's Champion",    Description="Casts Akarat's Champion on elite/boss or high density.",             Code="Crusader_AkaratsChampion_Combat",   IsBuff=false },
                new MacroEntry { Title="Iron Skin",            Description="Casts Iron Skin when health is low or elites are close.",            Code="Crusader_IronSkin_Defensive",       IsBuff=false },
                new MacroEntry { Title="Provoke",              Description="Casts Provoke when wrath is low and enemies are close.",             Code="Crusader_Provoke_ResourceOrDensity",IsBuff=false },
                new MacroEntry { Title="Bombardment",          Description="Fires Bombardment on elites or dense packs (disabled by default).",  Code="Crusader_Bombardment_EliteOrDensity",IsBuff=false },
                // ── Demon Hunter ──────────────────────────────────────────────
                new MacroEntry { Title="Preparation: Disc/Life",Description="Casts Preparation when discipline is missing or life is low.",       Code="DH_Preparation_DisciplineOrLife",   IsBuff=false },
                new MacroEntry { Title="Preparation: Punishment",Description="Punishment rune — casts when hatred is missing by ~75.",          Code="DH_Preparation_Punishment_Hatred",  IsBuff=false },
                new MacroEntry { Title="Companion: Bat",       Description="Bat Companion — casts when hatred is very low.",                     Code="DH_Companion_Bat_Hatred",           IsBuff=false },
                new MacroEntry { Title="Companion: Elite",     Description="Non-Bat Companion — activates near elites, bosses, or goblins.",     Code="DH_Companion_EliteOrBoss",          IsBuff=false },
                new MacroEntry { Title="Vengeance: Combat",    Description="Maintains Vengeance combat buff with dark heart/seethe support.",    Code="DH_Vengeance_KeepCombatBuff",       IsBuff=false },
                new MacroEntry { Title="Vengeance: Hatred",    Description="Hatred-return rune — casts Vengeance when hatred is below 30.",      Code="DH_Vengeance_HatredBurst",          IsBuff=false },
                new MacroEntry { Title="Shadow Power: Defensive",Description="Casts Shadow Power when missing and danger/health conditions met.", Code="DH_ShadowPower_Defensive",          IsBuff=false },
                new MacroEntry { Title="Shadow Power: Elusive",Description="Best-effort Elusive Ring upkeep using Shadow Power.",               Code="DH_ShadowPower_ElusiveRing",        IsBuff=false },
                new MacroEntry { Title="DH Strafe",            Description="Continuous Strafe primary-skill macro. Requires s7o_DHStrafe plugin.",
                    Code="dh_strafe_plugin", IsPlugin=true,
                    PluginTypeNames=new[]{"s7o_DHStrafePrimaryPlugin","s7o_DHStrafe"},
                    PluginAction="toggles:plugin:dhstrafe" },
                // ── Monk ──────────────────────────────────────────────────────
                new MacroEntry { Title="Epiphany",             Description="Casts Epiphany near elites or high density for spirit regen.",       Code="Monk_Epiphany_Combat",              IsBuff=false },
                new MacroEntry { Title="Sweeping Wind",        Description="Keeps Sweeping Wind active near enemies.",                           Code="Monk_SweepingWind_KeepUp",          IsBuff=false },
                new MacroEntry { Title="Mantra of Conviction", Description="Activates Mantra of Conviction near elites or density.",             Code="Monk_MantraConviction_Elite",       IsBuff=false },
                new MacroEntry { Title="Mantra of Salvation",  Description="Activates Mantra of Salvation when health is low or elites near.",   Code="Monk_MantraSalvation_Defensive",    IsBuff=false },
                new MacroEntry { Title="Mantra of Healing",    Description="Activates Mantra of Healing when health drops below threshold.",     Code="Monk_MantraHealing_Defensive",      IsBuff=false },
                new MacroEntry { Title="Mystic Ally",          Description="Casts Mystic Ally when spirit is low or elite/boss is nearby.",      Code="Monk_MysticAlly_ResourceOrElite",   IsBuff=false },
                new MacroEntry { Title="Serenity",             Description="Panic-button invulnerability when health is low or elites close.",   Code="Monk_Serenity_Defensive",           IsBuff=false },
                // ── Necromancer ───────────────────────────────────────────────
                new MacroEntry { Title="Bone Armor",           Description="Casts Bone Armor near enemies when missing or expiring.",            Code="Necro_BoneArmor_NearEnemies",       IsBuff=false },
                new MacroEntry { Title="Simulacrum",           Description="Casts Simulacrum near elites or dense packs.",                       Code="Necro_Simulacrum_EliteOrDensity",   IsBuff=false },
                new MacroEntry { Title="Land of the Dead",     Description="Casts LotD on elites or dense packs (disabled by default).",         Code="Necro_LandOfTheDead_EliteOrDensity",IsBuff=false },
                new MacroEntry { Title="Command Skeletons",    Description="Issues attack command to skeleton warriors near elite targets.",      Code="Necro_CommandSkeletons_Elite",      IsBuff=false },
                new MacroEntry { Title="Skeletal Mage",        Description="Casts Skeletal Mage when essence is high (disabled by default).",     Code="Necro_SkeletalMage_EssenceHigh",    IsBuff=false },
                // ── Witch Doctor ──────────────────────────────────────────────
                new MacroEntry { Title="Spirit Walk",          Description="Defensive Spirit Walk when health is critical or elites are close.",  Code="WD_SpiritWalk_Defensive",           IsBuff=false },
                new MacroEntry { Title="Soul Harvest",         Description="Casts Soul Harvest near enemies when the buff needs upkeep.",         Code="WD_SoulHarvest_NearEnemies",        IsBuff=false },
                new MacroEntry { Title="Horrify",              Description="Casts Horrify when enemies are close or health is low.",              Code="WD_Horrify_Defensive",              IsBuff=false },
                new MacroEntry { Title="Fetish Army",          Description="Summons Fetish Army near elites or dense packs.",                    Code="WD_FetishArmy_Combat",              IsBuff=false },
                new MacroEntry { Title="Gargantuan",           Description="Summons Gargantuan when missing and enemies are nearby.",             Code="WD_Gargantuan_KeepUp",              IsBuff=false },
                new MacroEntry { Title="Zombie Dogs",          Description="Summons Zombie Dogs when missing and enemies are nearby.",            Code="WD_ZombieDogs_KeepUp",              IsBuff=false },
                new MacroEntry { Title="Big Bad Voodoo",       Description="Casts Big Bad Voodoo on elites or dense packs (off by default).",     Code="WD_BigBadVoodoo_EliteOrDensity",    IsBuff=false },
                // ── Wizard ────────────────────────────────────────────────────
                new MacroEntry { Title="Magic Weapon",         Description="Maintains Magic Weapon elemental damage buff.",                       Code="Wizard_MagicWeapon",   IsBuff=true },
                new MacroEntry { Title="Familiar",             Description="Maintains Familiar projectile attack buff.",                          Code="Wizard_Familiar",      IsBuff=true },
                new MacroEntry { Title="Energy Armor",         Description="Maintains Energy Armor mitigation buff.",                             Code="Wizard_EnergyArmor",   IsBuff=true },
                new MacroEntry { Title="Storm Armor",          Description="Maintains Storm Armor retaliation buff.",                             Code="Wizard_StormArmor",    IsBuff=true },
                new MacroEntry { Title="Ice Armor",            Description="Maintains Ice Armor chilling aura buff.",                             Code="Wizard_IceArmor",      IsBuff=true },
                new MacroEntry { Title="Diamond Skin",         Description="Casts Diamond Skin when health is low or elites are close.",          Code="Wizard_DiamondSkin_Defensive",  IsBuff=false },
                new MacroEntry { Title="Mirror Image",         Description="Deploys Mirror Image when health is low or elites are nearby.",       Code="Wizard_MirrorImage_Defensive",  IsBuff=false },
                new MacroEntry { Title="Frost Nova",           Description="Casts Frost Nova when enemies are within close range.",               Code="Wizard_FrostNova_CloseEnemies", IsBuff=false },
                new MacroEntry { Title="Explosive Blast",      Description="Casts Explosive Blast when enemies are close (off by default).",      Code="Wizard_ExplosiveBlast_CloseEnemies",IsBuff=false },
                new MacroEntry { Title="Archon",               Description="Activates Archon on elites or dense packs (off by default).",         Code="Wizard_Archon_EliteOrDensity",  IsBuff=false },
                new MacroEntry { Title="Exit Archon",          Description="Clicks Archon buff icon to cancel Archon form early. Requires s7o_ExitArchon plugin.",
                    Code="exit_archon_plugin", IsPlugin=true,
                    PluginTypeNames=new[]{"s7o_ExitArchon"},
                    PluginAction="toggles:plugin:exitarchon" },
            });

            // Class header boundaries: entry index where each class starts
            int crusaderStart = barbarianStart + 7;
            int demonHunterStart = crusaderStart + 7;
            int monkStart = demonHunterStart + 9;
            int necromancerStart = monkStart + 7;
            int witchDoctorStart = necromancerStart + 5;
            int wizardStart = witchDoctorStart + 7;

            int[] classStarts = { 0, barbarianStart, crusaderStart, demonHunterStart, monkStart, necromancerStart, witchDoctorStart, wizardStart };
            string[] classNames = { "Automation", "Barbarian", "Crusader", "Demon Hunter", "Monk", "Necromancer", "Witch Doctor", "Wizard" };


            List<MacroListItem> items = BuildMacroListItems(allEntries, classStarts, classNames);

            _macrosTotalItems = items.Count;

            DrawMacrosFixedHeader(detail);

            const float sbW = 18f;
            const float btnH = 20f;

            float listTop = detail.Top + 72f;
            RectangleF listRect = new RectangleF(
                detail.Left + 8f,
                listTop,
                Math.Max(1f, detail.Width - sbW - 22f),
                Math.Max(1f, detail.Bottom - listTop - 8f));

            float sbLeft = detail.Right - sbW - 2f;
            float sbTop = listRect.Top;
            float sbBot = detail.Bottom - 2f;

            _macrosScrollUpRect = new RectangleF(sbLeft, sbTop, sbW - 2f, btnH);
            _macrosScrollDownRect = new RectangleF(sbLeft, sbBot - btnH, sbW - 2f, btnH);
            _macrosScrollTrackRect = new RectangleF(
                sbLeft,
                _macrosScrollUpRect.Bottom + 2f,
                sbW - 2f,
                Math.Max(8f, _macrosScrollDownRect.Top - _macrosScrollUpRect.Bottom - 4f));

            _macrosVisibleItems = VisibleMacroItemCount(listRect);
            ClampMacroScroll();

            int start = Math.Max(0, Math.Min(_macrosScroll, MaxMacroScroll()));

            float y = listRect.Top + 2f;
            int rowIdx = 0;

            for (int i = start; i < items.Count; i++)
            {
                MacroListItem item = items[i];
                float slotH = GetMacroListItemSlotHeight(item);
                RectangleF slot = new RectangleF(listRect.Left, y, listRect.Width, Math.Max(1f, slotH - 4f));

                if (slot.Bottom > listRect.Bottom)
                    break;

                if (item.Kind == MacroListItemKind.Section)
                {
                    DrawMacroSectionItem(slot, item.SectionTitle);
                }
                else
                {
                    DrawMacroEntryRow(slot, item.Entry, rowIdx++, item.IsFavoriteDisplay);
                }

                y += slotH;
            }

            DrawToggleViewportCover(detail, listRect);
            DrawMacrosFixedHeader(detail);
            DrawPluginsStyleMacrosScrollbar();
            _bPaneBorder.DrawRectangle(detail.Left, detail.Top, detail.Width, detail.Height);
        }


        private void DrawToggleViewportCover(RectangleF detail, RectangleF viewport)
        {
            if (_bPaneCover == null)
                return;

            float left = detail.Left + 1f;
            float right = detail.Right - 1f;
            float w = Math.Max(1f, right - left);

            // Cover anything that accidentally draws above the scroll viewport.
            if (viewport.Top > detail.Top + 1f)
            {
                _bPaneCover.DrawRectangle(
                    left,
                    detail.Top + 1f,
                    w,
                    Math.Max(1f, viewport.Top - detail.Top - 1f));
            }

            // Cover anything that accidentally draws below the scroll viewport.
            if (viewport.Bottom < detail.Bottom - 1f)
            {
                _bPaneCover.DrawRectangle(
                    left,
                    viewport.Bottom,
                    w,
                    Math.Max(1f, detail.Bottom - viewport.Bottom - 1f));
            }
        }

        private void DrawTtsAlertsFixedHeader(RectangleF detail)
        {
            _fSection.DrawText("TTS ALERTS", detail.Left + 14f, detail.Top + 10f);
            _fLabel.DrawText(
                Trim("TTS-powered alert plugins. Global TTS and volume are controlled from the top bar.", 114),
                detail.Left + 14f,
                detail.Top + 34f);
        }

        private void DrawToggleTtsAlertsCategory(RectangleF detail)
        {
            DrawTtsAlertsFixedHeader(detail);
            float contentTop = detail.Top + 64f;

            float viewportLeft = detail.Left + 10f;
            float viewportTop = contentTop + 8f;
            float viewportW = Math.Max(1f, detail.Width - 20f - ReservedScrollbarGutterW);
            float viewportH = Math.Max(1f, detail.Bottom - viewportTop - 12f);
            RectangleF viewport = new RectangleF(viewportLeft, viewportTop, viewportW, viewportH);
            _ttsAlertsViewportHeightPx = (int)Math.Ceiling(viewport.Height);

            const float rowH = 76f;
            const float gap = 8f;
            float y = viewport.Top - _ttsAlertsScrollPx;
            float contentBottom = 0f;

            RectangleF pylonR = new RectangleF(viewport.Left, y, viewport.Width, rowH);
            if (FullyInsideViewport(pylonR, viewport))
            {
                DrawTogglePluginButtonRow(
                    pylonR,
                    "Pylon Alerts",
                    "Announces pylon type, activation, and expiration.",
                    "toggles:plugin:pylons",
                    "s7o_PylonAlerts", "PylonAlerts", "PylonAlertsPlugin", "OtherShrinePlugin");
            }

            y += rowH + gap;
            contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);

            RectangleF bannerR = new RectangleF(viewport.Left, y, viewport.Width, rowH);
            if (FullyInsideViewport(bannerR, viewport))
            {
                DrawTogglePluginButtonRow(
                    bannerR,
                    "Banner Sound",
                    "Banner drop sound/TTS announcement plugin.",
                    "toggles:plugin:banner",
                    "s7o_BannerSoundPlugin", "BannerSoundPlugin");
            }

            y += rowH + gap;
            contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);

            DrawTtsBroadcastExpandedRow(
                new RectangleF(viewport.Left, y, viewport.Width, rowH),
                detail,
                viewport,
                ref contentBottom);

            _ttsAlertsViewportHeightPx = (int)Math.Ceiling(viewport.Height);
            _ttsAlertsContentHeightPx = Math.Max(0, (int)Math.Ceiling(contentBottom));
            ClampTtsAlertsScroll();

            DrawToggleViewportCover(detail, viewport);
            DrawTtsAlertsFixedHeader(detail);
            DrawTtsAlertsScrollbar(detail, viewport);
            _bPaneBorder.DrawRectangle(detail.Left, detail.Top, detail.Width, detail.Height);
        }


        private void DrawTtsBroadcastExpandedRow(RectangleF r, RectangleF detail, RectangleF viewport, ref float contentBottom)
        {
            IPlugin plugin = FindPluginByTypeName("s7o_TTS_Broadcast");
            bool installed = plugin != null;
            bool enabled = installed && SafePluginEnabled(plugin);

            if (FullyInsideViewport(r, viewport))
            {
                (enabled ? _bRow : _bRowAlt).DrawRectangle(r.Left, r.Top, r.Width, r.Height);

                const float stateW = 146f;
                const float expandW = 38f;
                const float gap = 8f;

                RectangleF stateR = new RectangleF(r.Right - stateW - 8f, r.Top + 6f, stateW, Math.Max(34f, r.Height - 12f));
                RectangleF expandR = new RectangleF(stateR.Left - expandW - gap, r.Top + 6f, expandW, Math.Max(34f, r.Height - 12f));

                float textX = r.Left + 12f;
                float textRight = expandR.Left - 12f;
                float textW = Math.Max(40f, textRight - textX);

                DrawOutlinedTextAt(_fRowTitle, _fRowTitleShadow,
                    Trim("TTS Broadcast", ApproxCharsForWidth(textW, 5.8f)), textX, r.Top + 8f);

                string[] lines = WrapToggleDescription("Type \".tts message\" in chat, or add hotkey messages below.", ApproxCharsForToggleDescription(textW), 2);
                if (lines.Length > 0) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[0], textX, r.Top + 31f);
                if (lines.Length > 1) DrawOutlinedTextAt(_fRowText, _fRowTextShadow, lines[1], textX, r.Top + 49f);

                DrawGlossButton(expandR, _ttsBroadcastExpanded ? "-" : "+", _ttsBroadcastExpanded, false, true);
                RegisterToggleHit("tts:expand", expandR);

                string state = installed ? (enabled ? "ON" : "OFF") : "NOT INSTALLED";
                DrawGlossButton(stateR, FitButtonLabel(state, stateR.Width), enabled, false, false);
                if (installed)
                    RegisterToggleHit("toggles:plugin:ttsbroadcast", stateR);
            }

            contentBottom = Math.Max(contentBottom, r.Bottom + _ttsAlertsScrollPx - viewport.Top);

            if (!_ttsBroadcastExpanded)
                return;

            SeedDefaultTtsCustomMessagesIfNeeded();
            EnsureAtLeastOneTtsCustomMessageRowIfExpanded();
            TrimTtsCustomMessages();

            float y = r.Bottom + 8f;
            float left = r.Left + 12f;
            float right = r.Right - 8f;
            float rowH = 36f;

            RectangleF addR = new RectangleF(left, y, 72f, 30f);
            if (FullyInsideViewport(addR, viewport))
            {
                DrawGlossButton(addR, "ADD", IsTtsButtonFlashActive("tts:add"), false, true);

                if (_ttsCustomMessages.Count < MaxTtsCustomMessages)
                    RegisterToggleHit("tts:add", addR);
            }

            y += 38f;
            contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);

            for (int i = 0; i < _ttsCustomMessages.Count; i++)
            {
                TtsCustomMessage msg = _ttsCustomMessages[i];
                if (msg == null)
                    continue;

                RectangleF rowR = new RectangleF(left, y, Math.Max(1f, right - left), rowH);

                DrawTtsCustomMessageRowIfVisible(i, rowR, viewport);

                y += rowH + 6f;
                contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);

                if (_ttsCustomMessageEditIndex == i)
                {
                    RectangleF keyboardR = new RectangleF(left, y + 2f, Math.Max(1f, right - left), 148f);
                    RectangleF targetR = RectangleF.Union(rowR, keyboardR);

                    float keyboardContentBottom = keyboardR.Bottom + _ttsAlertsScrollPx - viewport.Top + 10f;
                    int anticipatedContentHeightPx = Math.Max(
                        _ttsAlertsContentHeightPx,
                        (int)Math.Ceiling(Math.Max(contentBottom, keyboardContentBottom)));

                    if (_ttsPendingKeyboardAutoScroll)
                    {
                        _ttsPendingKeyboardAutoScroll = false;

                        if (AutoScrollTtsRectIntoView(targetR, viewport, anticipatedContentHeightPx))
                        {
                            contentBottom = Math.Max(contentBottom, keyboardContentBottom);
                            return;
                        }
                    }

                    if (FullyInsideViewport(keyboardR, viewport))
                        DrawTtsMiniKeyboard(keyboardR, viewport);

                    y = keyboardR.Bottom + 10f;
                    contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);
                }
            }

            contentBottom = Math.Max(contentBottom, y + _ttsAlertsScrollPx - viewport.Top);
        }

        private void DrawTtsCustomMessageRowIfVisible(int i, RectangleF rowR, RectangleF viewport)
        {
            if (!FullyInsideViewport(rowR, viewport))
                return;

            if (i < 0 || i >= _ttsCustomMessages.Count)
                return;

            TtsCustomMessage msg = _ttsCustomMessages[i];
            if (msg == null)
                return;

            RectangleF delR = new RectangleF(rowR.Left, rowR.Top + 3f, 32f, rowR.Height - 6f);

            const float resetW = 54f;
            const float hotW = 76f;
            const float btnGap = 6f;

            RectangleF resetR = new RectangleF(rowR.Right - resetW, rowR.Top + 3f, resetW, rowR.Height - 6f);
            RectangleF hotR = new RectangleF(resetR.Left - hotW - btnGap, rowR.Top + 3f, hotW, rowR.Height - 6f);
            RectangleF editR = new RectangleF(delR.Right + 8f, rowR.Top + 3f, Math.Max(50f, hotR.Left - delR.Right - 16f), rowR.Height - 6f);

            string deleteAction = "tts:delete:" + i.ToString(CultureInfo.InvariantCulture);
            DrawGlossButton(delR, "X", IsTtsButtonFlashActive(deleteAction), true, true);
            RegisterToggleHit(deleteAction, delR);

            bool editing = _ttsCustomMessageEditIndex == i;
            string text = GetTtsMessageEditDisplayText(msg.Text, editing);
            string editAction = "tts:edit:" + i.ToString(CultureInfo.InvariantCulture);

            DrawGlossButton(editR, string.Empty, editing || IsTtsButtonFlashActive(editAction), false, true);
            DrawOutlinedTextAt(
                _fRowText,
                _fRowTextShadow,
                TrimRightPreserveSpaces(text, ApproxCharsForWidth(editR.Width - 12f, 5.8f)),
                editR.Left + 7f,
                editR.Top + 7f);

            RegisterToggleHit(editAction, editR);

            bool capturing = _ttsCustomMessageHotkeyCaptureIndex == i;
            string hk = capturing ? "PRESS" : (msg.Hotkey == Key.Unknown ? "SET" : CaptureKeyLabel(msg.Hotkey));
            string hotkeyAction = "tts:hotkey:" + i.ToString(CultureInfo.InvariantCulture);

            DrawGlossButton(hotR, FitButtonLabel(hk, hotR.Width), capturing || IsTtsButtonFlashActive(hotkeyAction), false, true);
            RegisterToggleHit(hotkeyAction, hotR);

            string resetAction = "tts:reset:" + i.ToString(CultureInfo.InvariantCulture);
            DrawGlossButton(resetR, "RST", IsTtsButtonFlashActive(resetAction), false, true);
            RegisterToggleHit(resetAction, resetR);
        }

        private static string GetTtsMessageEditDisplayText(string text, bool editing)
        {
            string s = text ?? string.Empty;
            s = s.Replace('\u00A0', ' ');

            if (!editing)
                return string.IsNullOrEmpty(s) ? "message text..." : s;

            // Blinking underscore caret. When hidden, keep one blank character so
            // trailing spaces still visibly move the cursor position.
            bool showCaret = ((Environment.TickCount / 500) & 1) == 0;
            return s + (showCaret ? "_" : " ");
        }

        private static string TrimRightPreserveSpaces(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (maxChars <= 0)
                return string.Empty;

            if (text.Length <= maxChars)
                return text;

            if (maxChars <= 1)
                return text.Substring(0, maxChars);

            return text.Substring(0, maxChars - 1) + "…";
        }

        private void DrawTtsMiniKeyboard(RectangleF area, RectangleF viewport)
        {
            if (area.Height <= 16f)
                return;

            string[][] rows = new string[][]
            {
                new string[] { "CAPS", "SHIFT", "Backspace", "Clear", "DONE" },
                new string[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" },
                new string[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" },
                new string[] { "Z", "X", "C", "V", "B", "N", "M" },
                new string[] { "SPACE", ".", ",", "!", "?", "'", "-", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" },
            };

            float y = area.Top;
            float keyH = 25f;
            float gap = 4f;

            for (int r = 0; r < rows.Length; r++)
            {
                string[] row = rows[r];
                float totalUnits = 0f;
                for (int i = 0; i < row.Length; i++)
                    totalUnits += TtsKeyboardKeyUnits(row[i]);

                float keyW = Math.Max(18f, (area.Width - gap * Math.Max(0, row.Length - 1)) / Math.Max(1f, totalUnits));
                float x = area.Left;

                for (int i = 0; i < row.Length; i++)
                {
                    string label = row[i];
                    float w = keyW * TtsKeyboardKeyUnits(label);
                    RectangleF keyR = new RectangleF(x, y, w, keyH);

                    if (FullyInsideViewport(keyR, viewport))
                    {
                        bool active =
                            (string.Equals(label, "CAPS", StringComparison.OrdinalIgnoreCase) && _ttsKeyboardCaps) ||
                            (string.Equals(label, "SHIFT", StringComparison.OrdinalIgnoreCase) && _ttsKeyboardShiftOnce);

                        string keyAction = "tts:key:" + TtsKeyboardToken(label);
                        bool flash = IsTtsButtonFlashActive(keyAction);

                        DrawGlossButton(keyR, FitButtonLabel(label, keyR.Width), active || flash, false, true);
                        RegisterToggleHit(keyAction, keyR);
                    }

                    x += w + gap;
                }

                y += keyH + gap;
                if (y > area.Bottom - keyH)
                    break;
            }
        }

        private static float TtsKeyboardKeyUnits(string label)
        {
            if (string.Equals(label, "CAPS", StringComparison.OrdinalIgnoreCase)) return 1.8f;
            if (string.Equals(label, "SHIFT", StringComparison.OrdinalIgnoreCase)) return 2.0f;
            if (string.Equals(label, "SPACE", StringComparison.OrdinalIgnoreCase)) return 2.6f;
            if (string.Equals(label, "Backspace", StringComparison.OrdinalIgnoreCase)) return 2.7f;
            if (string.Equals(label, "Clear", StringComparison.OrdinalIgnoreCase)) return 1.8f;
            if (string.Equals(label, "BKSP", StringComparison.OrdinalIgnoreCase)) return 2.7f;
            if (string.Equals(label, "DONE", StringComparison.OrdinalIgnoreCase)) return 1.8f;
            if (string.Equals(label, "CLR", StringComparison.OrdinalIgnoreCase)) return 1.8f;
            return 1.0f;
        }

        private static string TtsKeyboardToken(string label)
        {
            if (string.Equals(label, "Backspace", StringComparison.OrdinalIgnoreCase)) return "BKSP";
            if (string.Equals(label, "Clear", StringComparison.OrdinalIgnoreCase)) return "CLR";
            if (label == ".") return "DOT";
            if (label == ",") return "COMMA";
            if (label == "!") return "BANG";
            if (label == "?") return "QUESTION";
            if (label == "'") return "APOS";
            if (label == "-") return "DASH";
            return label;
        }

        private static string TtsKeyboardTokenToText(string token, bool upper)
        {
            if (string.Equals(token, "SPACE", StringComparison.OrdinalIgnoreCase)) return " ";
            if (string.Equals(token, "DOT", StringComparison.OrdinalIgnoreCase)) return ".";
            if (string.Equals(token, "COMMA", StringComparison.OrdinalIgnoreCase)) return ",";
            if (string.Equals(token, "BANG", StringComparison.OrdinalIgnoreCase)) return "!";
            if (string.Equals(token, "QUESTION", StringComparison.OrdinalIgnoreCase)) return "?";
            if (string.Equals(token, "APOS", StringComparison.OrdinalIgnoreCase)) return "'";
            if (string.Equals(token, "DASH", StringComparison.OrdinalIgnoreCase)) return "-";
            if (!string.IsNullOrEmpty(token) && token.Length == 1)
            {
                char c = token[0];
                if (char.IsLetter(c))
                    return upper ? char.ToUpperInvariant(c).ToString() : char.ToLowerInvariant(c).ToString();
                return c.ToString();
            }
            return string.Empty;
        }


        private void DrawPluginsPage(MenuLayout l)
        {
            _bPane.DrawRectangle(l.LeftPane.Left, l.LeftPane.Top, l.LeftPane.Width, l.LeftPane.Height);
            _bPaneBorder.DrawRectangle(l.LeftPane.Left, l.LeftPane.Top, l.LeftPane.Width, l.LeftPane.Height);

            _bPane.DrawRectangle(l.MainPane.Left, l.MainPane.Top, l.MainPane.Width, l.MainPane.Height);
            _bPaneBorder.DrawRectangle(l.MainPane.Left, l.MainPane.Top, l.MainPane.Width, l.MainPane.Height);

            DrawTabs(l);
            DrawPluginList(l);
        }


        private void DrawPageTabs(MenuLayout l)
        {
            foreach (MenuPageTab page in MenuPageOrder)
            {
                RectangleF r;
                if (!l.PageTabRects.TryGetValue(page, out r))
                    continue;

                DrawGlossButton(r, PageLabel(page), _activePage == page, false, false);
            }
        }

        private void DrawNoClickToggle(MenuLayout l)
        {
            DrawSquareCheck(l.NoClickCheck, _noClickBackground);
            _fSmall.DrawText("No-Click Background", l.NoClickLabel.Left, l.NoClickLabel.Top + 4f);
        }

        private void DrawProfileCloseMask(MenuLayout l)
        {
            if (!ShouldShowProfileCloseMask()) return;
            if (_bProfileMask == null) return;

            RectangleF raw = l.ProfileCloseMask;
            RectangleF hit = GetProfileCloseMaskHitRect(raw);

            if (hit.Width <= 0f || hit.Height <= 0f)
                return;

            // Draw exactly the same rectangle used for hit-testing.
            _bProfileMask.DrawRectangle(hit.Left, hit.Top, hit.Width, hit.Height);

            // Keep the X centered on the raw Profile X anchor, not the padded hitbox.
            _fSmall.DrawText(
                "X",
                raw.Left + Math.Max(1f, raw.Width * 0.35f),
                raw.Top + Math.Max(0f, raw.Height * 0.12f));
        }

        private void DrawTabs(MenuLayout l)
        {
            foreach (var kv in l.TabRects)
            {
                ManagerTab tab = kv.Key;
                RectangleF r = kv.Value;
                int count = CountForTab(tab);
                string label = TabLabel(tab) + "  " + count.ToString(CultureInfo.InvariantCulture);
                DrawGlossButton(r, label, _activeTab == tab, false, false);
            }
        }

        private void DrawPluginList(MenuLayout l)
        {
            var rows = GetActiveRowsCached();
            ClampScroll(l, rows.Count);

            string title = TabLabel(_activeTab) + " Plugins";
            _fSection.DrawText(title, l.MainPane.Left + 14f, l.MainPane.Top + 14f);

            _bPaneBorder.DrawRectangle(l.ListRect.Left, l.ListRect.Top, l.ListRect.Width, l.ListRect.Height);
            // Scrollbar gutter background
            _bScrollTrackSolid.DrawRectangle(
                l.ScrollTrack.Left - 2f, l.ScrollUp.Top,
                l.ScrollTrack.Width + 4f, l.ScrollDown.Bottom - l.ScrollUp.Top);
            DrawFlashButton(l.ScrollUp,   "^", _lastPluginsScrollUpTick,   true);
            DrawFlashButton(l.ScrollDown, "v", _lastPluginsScrollDownTick, true);

            if (rows.Count == 0)
            {
                _fText.DrawText("No plugins in this section yet. Add some favorites to populate the list.", l.ListRect.Left + 14f, l.ListRect.Top + 16f);
                return;
            }

            int visible = VisibleRowCount(l);
            int start = Math.Max(0, Math.Min(_scroll, Math.Max(0, rows.Count - visible)));
            int end = Math.Min(rows.Count, start + visible);
            float y = l.ListRect.Top + 6f;

            for (int i = start; i < end; i++)
            {
                PluginRow row = rows[i];
                var rr = new RectangleF(l.ListRect.Left + 6f, y, l.ListRect.Width - 30f, 30f);
                ((i % 2) == 0 ? _bRow : _bRowAlt).DrawRectangle(rr.Left, rr.Top, rr.Width, rr.Height);

                bool enabled = SafePluginEnabled(row.Plugin);
                var star       = new RectangleF(rr.Left + 8f, rr.Top + 6f, 24f, 18f);
                var statusRect = new RectangleF(rr.Right - 72f, rr.Top + 5f, 62f, 20f);
                float categoryW = 90f;
                var catRect  = new RectangleF(statusRect.Left - categoryW - 8f, rr.Top + 6f, categoryW, 20f);
                var nameRect = new RectangleF(star.Right + 10f, rr.Top + 6f,
                    Math.Max(40f, catRect.Left - star.Right - 16f), 20f);

                DrawStarButton(star, row.IsFavorite);
                _fText.DrawText(Trim(row.DisplayName, ApproxCharsForWidth(nameRect.Width, 5.5f)),
                    nameRect.Left, nameRect.Top + 2f);
                _fSmall.DrawText(Trim(TabLabel(row.Category), ApproxCharsForWidth(catRect.Width, 5.0f)),
                    catRect.Left, catRect.Top + 3f);
                DrawGlossButton(statusRect, enabled ? "ON" : "OFF", enabled, false, true);

                // Star = favorite; right button = only plugin toggle
                _lastHitRects["star|"   + row.TypeName] = star;
                _lastHitRects["toggle|" + row.TypeName] = statusRect;

                y += 34f;
            }

            DrawScrollThumb(l, rows.Count, visible);
        }

        private int CountForTab(ManagerTab tab)
        {
            if (tab == ManagerTab.Favorites)
                return CountFavoriteRows();

            if (tab == ManagerTab.All)
                return _plugins.Count(p => p != null && !p.HiddenFromList);

            return _plugins.Count(p => p != null && !p.HiddenFromList && p.Category == tab);
        }

        private int CountFavoriteRows()
        {
            int count = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fav in _favorites)
            {
                if (fav == null || string.IsNullOrWhiteSpace(fav.TypeName)) continue;

                var row = _plugins.FirstOrDefault(p =>
                    string.Equals(p.TypeName, fav.TypeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FullName, fav.TypeName, StringComparison.OrdinalIgnoreCase));

                if (row == null) continue;
                if (row.HiddenFromList) continue;

                string key = !string.IsNullOrWhiteSpace(row.FullName) ? row.FullName : row.TypeName;

                if (seen.Add(key))
                    count++;
            }

            return count;
        }

        private void DrawScrollThumb(MenuLayout l, int total, int visible)
        {
            RectangleF thumb = GetScrollThumbRect(l, total, visible);

            // Track (always drawn even when no thumb, matches AutoGem specific pattern)
            DrawSolidScrollbarRect(l.ScrollTrack, _bScrollTrackSolid, _bScrollBorder);

            if (thumb.IsEmpty) return;

            bool active = _draggingScrollThumb
                || IsFlashActive(_lastPluginsScrollUpTick)
                || IsFlashActive(_lastPluginsScrollDownTick);
            DrawSolidScrollbarRect(thumb,
                active ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);
            if (active) DrawGlowRings(thumb);
        }

        private void DrawSolidScrollbarRect(RectangleF r, IBrush fill, IBrush border)
        {
            if (r.Width <= 0f || r.Height <= 0f) return;

            if (fill != null)
                fill.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            if (border != null)
                border.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
        }

        // Draws the MACROS detail scrollbar using the same visual model as the PLUGINS page.
        private void DrawPluginsStyleMacrosScrollbar()
        {
            _bScrollTrackSolid.DrawRectangle(
                _macrosScrollTrackRect.Left - 2f,
                _macrosScrollUpRect.Top,
                _macrosScrollTrackRect.Width + 4f,
                _macrosScrollDownRect.Bottom - _macrosScrollUpRect.Top);

            DrawFlashButton(_macrosScrollUpRect, "^", _lastMacrosScrollUpTick, true);
            DrawFlashButton(_macrosScrollDownRect, "v", _lastMacrosScrollDownTick, true);

            DrawSolidScrollbarRect(_macrosScrollTrackRect, _bScrollTrackSolid, _bScrollBorder);

            _macrosScrollThumbRect = GetMacrosScrollThumbRect(_macrosScrollTrackRect);

            if (_macrosScrollThumbRect.IsEmpty)
                return;

            bool active =
                _draggingMacrosThumb ||
                IsFlashActive(_lastMacrosScrollUpTick) ||
                IsFlashActive(_lastMacrosScrollDownTick);

            DrawSolidScrollbarRect(
                _macrosScrollThumbRect,
                active ? _bScrollThumbGreenActive : _bScrollThumbGreen,
                _bScrollBorder);

            if (active)
                DrawGlowRings(_macrosScrollThumbRect);
        }


        // Draws the HUD Hotkeys panel (MAIN page) as a 3-column grid.
        // Expanded content is laid out inline — scroll down if it exceeds the page.
        private void DrawHotkeysPanel(RectangleF r)
        {
            _bTextDimBg.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
            _bPaneBorder.DrawRectangle(r.Left, r.Top, r.Width, r.Height);

            float pad   = 10f;
            float headH = 38f;
            float btnH  = 24f;
            float y     = r.Top + 7f;

            // ── Header row ─────────────────────────────────────────────────────
            _fSection.DrawText("HUD Hotkeys", r.Left + pad, y + 4f);
            _fSmall.DrawText("Click a key to rebind  ·  Restart HUD to apply", r.Left + 160f, y + 10f);

            var expandBtn = new RectangleF(r.Right - 34f, y + 4f, 24f, btnH);
            DrawGlossButton(expandBtn, _hotkeysExpanded ? "-" : "+", _hotkeysExpanded, false, true);
            RegisterMainHit("hotkeys:expand", expandBtn);

            if (!_hotkeysExpanded)
                return;

            y += headH;

            // ── Compact 5-column grid ──────────────────────────────────────────
            // 9 hotkeys + inline reset cell = 10 cells total, 2 rows.
            const int cols   = 5;
            float cellGapX   = 8f;
            float cellGapY   = 6f;
            float cellW      = (r.Width - pad * 2f - cellGapX * (cols - 1)) / cols;
            float cellH      = 42f;
            float keyBtnH    = 21f;
            float labelYOff  = keyBtnH + 7f;
            int totalCells   = HotkeyIds.Length + 1;

            for (int cellIndex = 0; cellIndex < totalCells; cellIndex++)
            {
                int col = cellIndex % cols;
                int row = cellIndex / cols;

                float cx = r.Left + pad + col * (cellW + cellGapX);
                float cy = y + row * (cellH + cellGapY);

                var cell = new RectangleF(cx, cy, cellW, cellH);
                (row % 2 == 0 ? _bRow : _bRowAlt).DrawRectangle(cell.Left, cell.Top, cell.Width, cell.Height);

                var keyBtn = new RectangleF(cell.Left + 4f, cell.Top + 4f, Math.Max(1f, cell.Width - 8f), keyBtnH);

                if (cellIndex < HotkeyIds.Length)
                {
                    bool capturing = (_capturingHudHotkeyIdx == cellIndex);

                    string mod = (_hudHotkeyMod != null && cellIndex < _hudHotkeyMod.Length) ? _hudHotkeyMod[cellIndex] : HotkeyDefMod[cellIndex];
                    string key = (_hudHotkeyKey != null && cellIndex < _hudHotkeyKey.Length) ? _hudHotkeyKey[cellIndex] : HotkeyDefKey[cellIndex];
                    string keyDisplay = capturing
                        ? "..."
                        : (string.IsNullOrEmpty(mod) ? key : mod.ToUpperInvariant() + "+" + key.ToUpperInvariant());

                    DrawGlossButton(keyBtn, keyDisplay, capturing, false, true);
                    RegisterMainHit("hotkeys:key:" + cellIndex.ToString(CultureInfo.InvariantCulture), keyBtn);

                    _fSmall.DrawText(Trim(HotkeyLabels[cellIndex], ApproxCharsForWidth(cell.Width - 8f, 5.0f)), cell.Left + 4f, cell.Top + labelYOff);
                }
                else
                {
                    bool resetting = IsRecentTick(_lastHotkeyResetTick, 1500);
                    DrawGlossButton(keyBtn, resetting ? "DONE" : "RESET", resetting, !resetting, false);
                    RegisterMainHit("hotkeys:reset", keyBtn);

                    _fSmall.DrawText("Reset Defaults", cell.Left + 4f, cell.Top + labelYOff);
                }
            }
        }

        // Save one modified hotkey back to config/hotkeys.xml (rewrites full file preserving all values)
        private void SaveHotkeyToXml(int idx)
        {
            try
            {
                char q = (char)34;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=" + q + "1.0" + q + " encoding=" + q + "utf-8" + q + "?>");
                sb.AppendLine("<config>");
                sb.AppendLine("\t<hotkeys>");

                for (int i = 0; i < HotkeyIds.Length; i++)
                {
                    string mod = (_hudHotkeyMod != null && i < _hudHotkeyMod.Length) ? (_hudHotkeyMod[i] ?? string.Empty) : HotkeyDefMod[i];
                    string key = (_hudHotkeyKey != null && i < _hudHotkeyKey.Length) ? (_hudHotkeyKey[i] ?? string.Empty) : HotkeyDefKey[i];
                    sb.AppendLine("\t\t<" + HotkeyIds[i] + " modifier=" + q + mod + q + " key=" + q + key + q + " />");
                }

                sb.AppendLine("\t</hotkeys>");
                sb.AppendLine("</config>");

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "hotkeys.xml");
                string dir  = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);

                LogDebug("Hotkeys.xml updated for " + HotkeyIds[idx] + ": mod=" + _hudHotkeyMod[idx] + " key=" + _hudHotkeyKey[idx]);
            }
            catch (Exception ex)
            {
                _status = "FAILED TO SAVE HOTKEY: " + ex.Message;
            }
        }

        // Write default hotkeys.xml (called on "RESET TO DEFAULTS")
        private void WriteDefaultHotkeysXml()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "hotkeys.xml");
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // Also reset runtime arrays so display updates immediately
                if (_hudHotkeyMod != null) for (int ii = 0; ii < _hudHotkeyMod.Length && ii < HotkeyDefMod.Length; ii++) _hudHotkeyMod[ii] = HotkeyDefMod[ii];
                if (_hudHotkeyKey != null) for (int ii = 0; ii < _hudHotkeyKey.Length && ii < HotkeyDefKey.Length; ii++) _hudHotkeyKey[ii] = HotkeyDefKey[ii];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<config>");
                sb.AppendLine("\t<hotkeys>");
                sb.AppendLine("\t\t<exit modifier=\"ctrl\" key=\"End\" />");
                sb.AppendLine("\t\t<hide_hud modifier=\"\" key=\"F4\" />");
                sb.AppendLine("\t\t<reload_pickit modifier=\"\" key=\"F3\" />");
                sb.AppendLine("\t\t<capture modifier=\"alt\" key=\"C\" />");
                sb.AppendLine("\t\t<capture_with_overlay modifier=\"ctrl+alt\" key=\"C\" />");
                sb.AppendLine("\t\t<stat_tracker modifier=\"\" key=\"F5\" />");
                sb.AppendLine("\t\t<debug_overlay modifier=\"\" key=\"F11\" />");
                sb.AppendLine("\t\t<save_debug_data modifier=\"ctrl+alt\" key=\"D\" />");
                sb.AppendLine("\t\t<reset_session modifier=\"ctrl+alt\" key=\"R\" />");
                sb.AppendLine("\t</hotkeys>");
                sb.AppendLine("</config>");

                File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                _lastHotkeyResetTick = Environment.TickCount;
                _status = "HOTKEYS RESET TO DEFAULTS - RESTART HUD TO APPLY";
                LogDebug("Hotkeys.xml written to defaults at " + path);
            }
            catch (Exception ex)
            {
                _status = "FAILED TO WRITE HOTKEYS: " + ex.Message;
                LogDebug("WriteDefaultHotkeysXml error: " + ex.Message);
            }
        }

        private void DrawTwoToneRect(RectangleF r, IBrush top, IBrush bottom, IBrush edge)
        {
            if (r.Width <= 0f || r.Height <= 0f) return;

            float half = r.Height * 0.5f;

            if (top != null)
                top.DrawRectangle(r.Left, r.Top, r.Width, half);

            if (bottom != null)
                bottom.DrawRectangle(r.Left, r.Top + half, r.Width, r.Height - half);

            if (_bBtnGloss != null && r.Height >= 10f)
                _bBtnGloss.DrawRectangle(r.Left + 1f, r.Top + 1f, Math.Max(1f, r.Width - 2f), Math.Max(1f, half * 0.55f));

            if (edge != null)
                edge.DrawRectangle(r.Left, r.Top, r.Width, r.Height);
        }

        private void DrawSquareCheck(RectangleF r, bool on)
        {
            DrawTwoToneRect(
                r,
                on ? _bToggleOnTop : _bToggleOffTop,
                on ? _bToggleOnBottom : _bToggleOffBottom,
                _bToggleEdge);
        }


        private void DrawStarButton(RectangleF r, bool on)
        {
            DrawTwoToneRect(
                r,
                on ? _bStarOnTop : _bStarOffTop,
                on ? _bStarOnBottom : _bStarOffBottom,
                _bBtnEdge);

            DrawCenteredOutlinedTextExact(_fStar, _fStarShadow, "*", r);
        }

        private void DrawGlossButton(RectangleF r, string text, bool active, bool danger, bool compact, bool forceYellowText = false)
        {
            IBrush top;
            IBrush bottom;

            if (danger && active)
            {
                top = _bBtnDangerFlashTop ?? _bBtnDangerTop;
                bottom = _bBtnDangerFlashBottom ?? _bBtnDangerBottom;
            }
            else if (danger)
            {
                top = _bBtnDangerTop;
                bottom = _bBtnDangerBottom;
            }
            else if (active)
            {
                top = _bBtnActiveTop;
                bottom = _bBtnActiveBottom;
            }
            else
            {
                top = _bBtnNormalTop;
                bottom = _bBtnNormalBottom;
            }

            DrawTwoToneRect(r, top, bottom, _bBtnEdge);

            if (_bBtnEdge != null)
            {
                _bBtnEdge.DrawRectangle(r.Left - 0.5f, r.Top - 0.5f, r.Width + 1f, r.Height + 1f);
                _bBtnEdge.DrawRectangle(r.Left + 1f, r.Top + 1f, Math.Max(1f, r.Width - 2f), Math.Max(1f, r.Height - 2f));
            }

            IFont shadow = compact ? _fButtonShadow : _fButtonLargeShadow;
            bool yellowText = active || forceYellowText;
            IFont normal = compact
                ? (yellowText ? _fButtonActive : _fButton)
                : (yellowText ? _fButtonLargeActive : _fButtonLarge);

            DrawCenteredOutlinedText(normal, shadow, text, r);
        }


        private void DrawOutlinedTextAt(IFont mainFont, IFont outlineFont, string text, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            IFont outline = outlineFont ?? mainFont;
            IFont main    = mainFont    ?? outline;
            if (outline != null)
            {
                outline.DrawText(text, x - 2f, y); outline.DrawText(text, x + 2f, y);
                outline.DrawText(text, x, y - 2f); outline.DrawText(text, x, y + 2f);
                outline.DrawText(text, x - 1f, y - 1f); outline.DrawText(text, x + 1f, y - 1f);
                outline.DrawText(text, x - 1f, y + 1f); outline.DrawText(text, x + 1f, y + 1f);
            }
            if (main != null) main.DrawText(text, x, y);
        }

        private void DrawCenteredTextExact(IFont font, string text, RectangleF r)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            try
            {
                var tl = font.GetTextLayout(text);
                font.DrawText(tl,
                    r.Left + (r.Width  - tl.Metrics.Width)  * 0.5f,
                    r.Top  + (r.Height - tl.Metrics.Height) * 0.5f);
            }
            catch { }
        }

        private void DrawCenteredOutlinedTextExact(IFont mainFont, IFont outlineFont, string text, RectangleF r)
        {
            if (string.IsNullOrEmpty(text)) return;
            IFont outline = outlineFont ?? mainFont;
            IFont main    = mainFont    ?? outline;
            if (outline != null)
            {
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left - 2f, r.Top, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left + 2f, r.Top, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left, r.Top - 2f, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left, r.Top + 2f, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left - 1f, r.Top - 1f, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left + 1f, r.Top - 1f, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left - 1f, r.Top + 1f, r.Width, r.Height));
                DrawCenteredTextExact(outline, text, new RectangleF(r.Left + 1f, r.Top + 1f, r.Width, r.Height));
            }
            if (main != null) DrawCenteredTextExact(main, text, r);
        }

        private void DrawCenteredOutlinedText(IFont mainFont, IFont outlineFont, string text, RectangleF r)
        {
            if (string.IsNullOrEmpty(text))
                return;

            IFont outline = outlineFont ?? mainFont;
            IFont main = mainFont ?? outline;

            // Thick black outline / cartoon-style label border.
            DrawCenteredText(outline, text, new RectangleF(r.Left - 2f, r.Top, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left + 2f, r.Top, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left, r.Top - 2f, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left, r.Top + 2f, r.Width, r.Height));

            DrawCenteredText(outline, text, new RectangleF(r.Left - 1f, r.Top - 1f, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left + 1f, r.Top - 1f, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left - 1f, r.Top + 1f, r.Width, r.Height));
            DrawCenteredText(outline, text, new RectangleF(r.Left + 1f, r.Top + 1f, r.Width, r.Height));

            // Final white/light text.
            DrawCenteredText(main, text, r);
        }

        private void DrawCenteredText(IFont font, string text, RectangleF r)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            try
            {
                var tl = font.GetTextLayout(text);
                font.DrawText(tl, r.Left + (r.Width - tl.Metrics.Width) * 0.5f, r.Top + (r.Height - tl.Metrics.Height) * 0.5f - 1f);
            }
            catch { }
        }

        private static string PageLabel(MenuPageTab page)
        {
            switch (page)
            {
                case MenuPageTab.Main: return "MAIN";
                case MenuPageTab.Toggles: return "TOGGLES";
                case MenuPageTab.Plugins: return "PLUGINS";
                default: return "MAIN";
            }
        }

        private static string ToggleCategoryLabel(ToggleCategory cat)
        {
            switch (cat)
            {
                case ToggleCategory.Visual:    return "VISUAL";
                case ToggleCategory.Macros:    return "MACROS";
                case ToggleCategory.TtsAlerts: return "TTS ALERTS";
                default: return cat.ToString().ToUpperInvariant();
            }
        }

        private void StepPage(int dir)
        {
            int idx = Array.IndexOf(MenuPageOrder, _activePage);
            if (idx < 0) idx = 0;

            idx = (idx + dir + MenuPageOrder.Length) % MenuPageOrder.Length;
            _activePage = MenuPageOrder[idx];

            _scroll = 0;
            _autoGemSpecificExpanded = false;  // collapse on page change
            _macrosScroll = 0;
            MarkRowsDirty();
            RequestPluginCacheRefresh();
            SaveSettings();
        }

        private static MenuPageTab NormalizeMenuPage(MenuPageTab page)
        {
            switch (page)
            {
                case MenuPageTab.Main:
                case MenuPageTab.Toggles:
                case MenuPageTab.Plugins:
                    return page;

                default:
                    return MenuPageTab.Main;
            }
        }

        private static MenuPageTab ParseMenuPageSetting(string value, int loadedSettingsVersion)
        {
            string s = (value ?? string.Empty).Trim();

            if (s.Length == 0)
                return MenuPageTab.Main;

            if (string.Equals(s, "Main", StringComparison.OrdinalIgnoreCase))
                return MenuPageTab.Main;

            if (string.Equals(s, "Toggles", StringComparison.OrdinalIgnoreCase))
                return MenuPageTab.Toggles;

            if (string.Equals(s, "Plugins", StringComparison.OrdinalIgnoreCase))
                return MenuPageTab.Plugins;

            // Settings page was removed - redirect to Main
            if (string.Equals(s, "Settings", StringComparison.OrdinalIgnoreCase))
                return MenuPageTab.Main;

            // Retired old page.
            if (string.Equals(s, "System", StringComparison.OrdinalIgnoreCase))
                return MenuPageTab.Main;

            int iv;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
            {
                // Old versions used: Plugins = 0, System = 1, Settings = 3.
                if (loadedSettingsVersion > 0 && loadedSettingsVersion < 3)
                {
                    switch (iv)
                    {
                        case 0: return MenuPageTab.Plugins;
                        case 1: return MenuPageTab.Main;
                        case 3: return MenuPageTab.Main;  // Settings retired
                        default: return MenuPageTab.Main;
                    }
                }

                switch (iv)
                {
                    case 0: return MenuPageTab.Main;
                    case 1: return MenuPageTab.Toggles;
                    case 2: return MenuPageTab.Plugins;
                    case 3: return MenuPageTab.Main;  // Settings retired
                    default: return MenuPageTab.Main;
                }
            }

            MenuPageTab page;
            if (Enum.TryParse<MenuPageTab>(s, true, out page) &&
                Enum.IsDefined(typeof(MenuPageTab), page))
            {
                return NormalizeMenuPage(page);
            }

            return MenuPageTab.Main;
        }

        private static string TabLabel(ManagerTab tab)
        {
            switch (tab)
            {
                case ManagerTab.Favorites: return "Favorites";
                case ManagerTab.Combat: return "Combat";
                case ManagerTab.Rift: return "Rift";
                case ManagerTab.Items: return "Items";
                case ManagerTab.Other: return "Other";
                case ManagerTab.All: return "All";
                default: return tab.ToString();
            }
        }

        private void OpenMenu()
        {
            if (_visible) return;

            ReleaseLeftMouseForMenuBoundary();

            _visible = true;
            _status = "MENU OPENED";
            RequestPluginCacheRefresh();
            _nextVisiblePluginRefreshTick = int.MinValue;

            if (_noClickBackground)
                RequestProfileBackgroundOpen();
            else
                ClearProfileBackgroundState();

            LogDebug("Menu opened.");
        }

        private void CloseMenu(bool closeProfile)
        {
            if (!_visible && !_capturingHotkey)
            {
                if (closeProfile)
                    RequestProfileBackgroundClose(true);

                return;
            }

            if (closeProfile && IsProfileBoundForMenu())
            {
                LogDebug("Menu close requested; No-Click background bound, delaying menu hide until Shift+P close is issued.");
                _capturingHotkey = false;
                _autoSkillKeybindCaptureSlot = -1;
                _draggingWindow = false;
                _draggingDot = false;
                _draggingScrollThumb = false;
                _draggingAutoGemSpecificScrollThumb = false;
                _draggingGlobalTtsSlider = false;
                RequestProfileBackgroundClose(true);
                return;
            }

            HideMenuStateOnly();
            _status = "MENU CLOSED";

            if (closeProfile)
                ClearProfileBackgroundState();

            LogDebug("Menu closed. closeProfile=" + closeProfile + ".");
        }

        private void HideMenuStateOnly()
        {
            ReleaseLeftMouseForMenuBoundary();

            _visible = false;
            _capturingHotkey = false;
            _autoSkillKeybindCaptureSlot = -1;
            _draggingWindow = false;
            _draggingDot = false;
            _draggingScrollThumb = false;
            _draggingAutoGemSpecificScrollThumb = false;
            _draggingGlobalTtsSlider = false;
            _draggingMacrosThumb = false;
            _draggingVisualThumb = false;
            _draggingMainThumb = false;
            _menuConsumedLeftMouseDown = false;
            _hideButtonMouseDown = false;
            _profileCloseMaskMouseDown = false;
            _profileCloseMaskReleasePending = false;
            _pendingOverlayHideStartedAt = int.MinValue;
            ClearProfileCloseEscapeSuppression();

            ClearAnySnapHotkeyCapture();
        }

        private void ToggleNoClickBackground()
        {
            _noClickBackground = !_noClickBackground;

            if (_visible)
            {
                if (_noClickBackground)
                {
                    ClearProfileBackgroundState();
                    RequestProfileBackgroundOpen();
                }
                else
                {
                    RequestProfileBackgroundClose(false);
                }
            }
            else if (!_noClickBackground)
            {
                ClearProfileBackgroundState();
            }

            _status = _noClickBackground ? "NO-CLICK BACKGROUND ON" : "NO-CLICK BACKGROUND OFF";
            SaveSettings();
            LogDebug("No-Click Background toggled. Enabled=" + _noClickBackground + ".");
        }

        private void RunCustomTtsBroadcastHotkeys()
        {
            try
            {
                SyncTtsCustomRuntimeLists();

                bool blocked = IsTtsCustomHotkeyBlocked();

                for (int i = 0; i < _ttsCustomMessages.Count; i++)
                {
                    TtsCustomMessage msg = _ttsCustomMessages[i];

                    if (msg == null || msg.Hotkey == Key.Unknown)
                    {
                        if (i < _ttsCustomHotkeyDownLatch.Count)
                            _ttsCustomHotkeyDownLatch[i] = false;

                        continue;
                    }

                    bool down = IsTtsCustomHotkeyPhysicallyDown(msg.Hotkey);

                    if (!down)
                    {
                        _ttsCustomHotkeyDownLatch[i] = false;
                        continue;
                    }

                    if (blocked)
                    {
                        // Consume the hold while blocked so it does not fire late
                        // when chat/menu/map closes.
                        _ttsCustomHotkeyDownLatch[i] = true;
                        continue;
                    }

                    if (_ttsCustomHotkeyDownLatch[i])
                        continue;

                    _ttsCustomHotkeyDownLatch[i] = true;

                    if (!string.IsNullOrWhiteSpace(msg.Text))
                    {
                        SendTtsBroadcastChatMessage(msg.Text);
                        _ttsButtonFlashTicks["tts:hotkey-fired:" + i.ToString(CultureInfo.InvariantCulture)] = Environment.TickCount;
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsTtsCustomHotkeyBlocked()
        {
            try
            {
                if (_visible)
                    return true;

                if (IsChatEntryOpen())
                    return true;

                if (IsFreeHudBlockingUiVisible())
                    return true;

                IPlugin plugin = FindPluginByTypeName("s7o_TTS_Broadcast");
                if (plugin == null || !SafePluginEnabled(plugin))
                    return true;

                return false;
            }
            catch
            {
                return true;
            }
        }

        private bool IsTtsCustomHotkeyPhysicallyDown(Key key)
        {
            try
            {
                if (key == Key.Unknown)
                    return false;

                Keys winKey = KeyToWinKeys(key);

                if (winKey != Keys.None)
                    return (GetAsyncKeyState((int)winKey) & 0x8000) != 0;

                // Fallback for keys not mapped in KeyToWinKeys.
                return ZbIsKeyDown(key, winKey);
            }
            catch
            {
                return false;
            }
        }

        private void SendTtsBroadcastChatMessage(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                string clean = SanitizeTtsCustomMessage(text);
                if (string.IsNullOrWhiteSpace(clean))
                    return;

                string payload = BuildTtsBroadcastChatCommand(clean);
                if (string.IsNullOrWhiteSpace(payload))
                    return;

                if (TrySetClipboardTextSta(payload))
                {
                    FastPasteChatLine();
                    _status = "TTS BROADCAST SENT";
                    return;
                }

                // Fallback only if clipboard paste fails.
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(15);
                SendKeys.SendWait(EscapeSendKeysText(payload));
                Thread.Sleep(15);
                SendKeys.SendWait("{ENTER}");

                _status = "TTS BROADCAST SENT";
            }
            catch
            {
                _status = "TTS BROADCAST FAILED";
            }
        }

        private void FastPasteChatLine()
        {
            try
            {
                TapVirtualKey(VK_RETURN, 8);
                Thread.Sleep(18);

                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                Thread.Sleep(4);

                TapVirtualKey(VK_V, 8);

                Thread.Sleep(4);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero);

                Thread.Sleep(18);
                TapVirtualKey(VK_RETURN, 8);
            }
            catch
            {
                try { keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); } catch { }
                try { keybd_event(VK_V, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); } catch { }
                try { keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); } catch { }
            }
        }

        private void TapVirtualKey(byte vk, int holdMs)
        {
            try
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                Thread.Sleep(Math.Max(1, holdMs));
                keybd_event(vk, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero);
            }
            catch
            {
                try { keybd_event(vk, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); } catch { }
            }
        }

        private static bool TrySetClipboardTextSta(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            bool ok = false;

            try
            {
                Thread t = new Thread(delegate()
                {
                    try
                    {
                        Clipboard.SetText(text);
                        ok = true;
                    }
                    catch
                    {
                        ok = false;
                    }
                });

                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
                t.Join(150);
            }
            catch
            {
                ok = false;
            }

            return ok;
        }

        private static string SanitizeTtsCustomMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace(TtsKeyboardFakeSpaceChar, ' ');
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            text = text.Trim();

            if (text.Length > MaxTtsCustomMessageChars)
                text = text.Substring(0, MaxTtsCustomMessageChars);

            return text;
        }

        private static string NormalizeTtsCustomMessageForEditor(string text)
        {
            if (text == null)
                return string.Empty;

            // Convert old fake spaces from prior builds into normal editable spaces.
            text = text.Replace(TtsKeyboardFakeSpaceChar, ' ');

            // Do not allow line breaks inside one macro row.
            text = text.Replace('\r', ' ').Replace('\n', ' ');

            if (text.Length > MaxTtsCustomMessageChars)
                text = text.Substring(0, MaxTtsCustomMessageChars);

            // Important: do NOT Trim(). Leading/trailing/interior spaces are valid while editing.
            return text;
        }

        private string BuildTtsBroadcastChatCommand(string clean)
        {
            if (string.IsNullOrWhiteSpace(clean))
                return string.Empty;

            string normalized = NormalizeTtsDuplicateCompareText(clean);
            bool duplicate = !string.IsNullOrEmpty(normalized)
                && string.Equals(normalized, _lastTtsBroadcastNormalizedPayload, StringComparison.Ordinal);

            string suffix = string.Empty;

            if (duplicate)
            {
                _ttsBroadcastDuplicateSaltOn = !_ttsBroadcastDuplicateSaltOn;
                suffix = _ttsBroadcastDuplicateSaltOn ? TtsDuplicateLineSalt : string.Empty;
            }
            else
            {
                _ttsBroadcastDuplicateSaltOn = false;
            }

            _lastTtsBroadcastNormalizedPayload = normalized;

            return ".tts " + clean + suffix;
        }

        private static string NormalizeTtsDuplicateCompareText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace(TtsKeyboardFakeSpaceChar, ' ')
                .Trim();
        }

        private static string EscapeSendKeysText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder();

            foreach (char c in text)
            {
                switch (c)
                {
                    case '+':
                    case '^':
                    case '%':
                    case '~':
                    case '(':
                    case ')':
                    case '{':
                    case '}':
                    case '[':
                    case ']':
                        sb.Append('{').Append(c).Append('}');
                        break;

                    case '\n':
                    case '\r':
                        sb.Append(' ');
                        break;

                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        private void SeedDefaultTtsCustomMessagesIfNeeded()
        {
            if (_ttsCustomDefaultsSeedVersion >= TtsCustomDefaultsCurrentVersion)
                return;

            string[] defaults =
            {
                "Next floor",
                "Take pylon",
                "Skip"
            };

            // Only seed into an empty list or a list that only contains blank rows.
            bool hasRealMessage = false;
            for (int i = 0; i < _ttsCustomMessages.Count; i++)
            {
                if (_ttsCustomMessages[i] != null && !string.IsNullOrWhiteSpace(_ttsCustomMessages[i].Text))
                {
                    hasRealMessage = true;
                    break;
                }
            }

            if (!hasRealMessage)
            {
                _ttsCustomMessages.Clear();

                for (int i = 0; i < defaults.Length && _ttsCustomMessages.Count < MaxTtsCustomMessages; i++)
                {
                    _ttsCustomMessages.Add(new TtsCustomMessage
                    {
                        Text = defaults[i],
                        Hotkey = Key.Unknown
                    });
                }
            }

            _ttsCustomDefaultsSeeded = true;
            _ttsCustomDefaultsSeedVersion = TtsCustomDefaultsCurrentVersion;
            SyncTtsCustomRuntimeLists();
        }

        private void TrimTtsCustomMessages()
        {
            try
            {
                while (_ttsCustomMessages.Count > MaxTtsCustomMessages)
                    _ttsCustomMessages.RemoveAt(_ttsCustomMessages.Count - 1);

                for (int i = 0; i < _ttsCustomMessages.Count; i++)
                {
                    if (_ttsCustomMessages[i] == null)
                        _ttsCustomMessages[i] = new TtsCustomMessage();

                    _ttsCustomMessages[i].Text = NormalizeTtsCustomMessageForEditor(_ttsCustomMessages[i].Text);
                }

                if (_ttsCustomMessageEditIndex >= _ttsCustomMessages.Count)
                    _ttsCustomMessageEditIndex = -1;

                if (_ttsCustomMessageHotkeyCaptureIndex >= _ttsCustomMessages.Count)
                    _ttsCustomMessageHotkeyCaptureIndex = -1;

                SyncTtsCustomRuntimeLists();
            }
            catch { }
        }

        private void EnsureAtLeastOneTtsCustomMessageRowIfExpanded()
        {
            if (!_ttsBroadcastExpanded)
            {
                SyncTtsCustomRuntimeLists();
                return;
            }

            if (_ttsCustomMessages.Count <= 0)
                _ttsCustomMessages.Add(new TtsCustomMessage());

            SyncTtsCustomRuntimeLists();
        }

        private void SyncTtsCustomRuntimeLists()
        {
            try
            {
                while (_ttsCustomHotkeyDownLatch.Count < _ttsCustomMessages.Count)
                    _ttsCustomHotkeyDownLatch.Add(false);

                while (_ttsCustomHotkeyDownLatch.Count > _ttsCustomMessages.Count)
                    _ttsCustomHotkeyDownLatch.RemoveAt(_ttsCustomHotkeyDownLatch.Count - 1);
            }
            catch
            {
            }
        }

        private static string EncodeSettingText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    return string.Empty;

                return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DecodeSettingText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                    return string.Empty;

                return Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch
            {
                return string.Empty;
            }
        }


        private bool IsChatEntryOpen()
        {
            try { return _chatEditLine != null && _chatEditLine.Visible; }
            catch { return false; }
        }

        private bool IsFreeHudBlockingUiVisible()
        {
            try
            {
                // The HUD Manager overlay itself should block gameplay hotkeys/AutoSnap.
                if (_visible)
                    return true;

                try
                {
                    if (_chatEditLine != null && _chatEditLine.Visible)
                        return true;
                }
                catch { }

                try
                {
                    if (Hud != null && Hud.Render != null)
                    {
                        if (Hud.Render.ActMapUiElement != null && Hud.Render.ActMapUiElement.Visible)
                            return true;

                        if (Hud.Render.WorldMapUiElement != null && Hud.Render.WorldMapUiElement.Visible)
                            return true;
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                // Fail closed. If UI state cannot be checked, avoid firing automation/hotkeys.
                return true;
            }
        }

        private bool IsProfileWindowVisible()
        {
            try { return _profileUiElement != null && _profileUiElement.Visible; }
            catch { return false; }
        }

        private bool IsProfileBoundForMenu()
        {
            return _profileBackgroundActiveForMenu || _profileOpenedByHudMenu || _profileConfirmedVisibleForMenu;
        }

        private bool IsProfileOpenGraceActive()
        {
            try
            {
                if (!_profileOpenRequested || _profileOpenRequestTick == int.MinValue)
                    return false;

                return unchecked(Environment.TickCount - _profileOpenRequestTick) < ProfileOpenGraceMs;
            }
            catch
            {
                return false;
            }
        }

        private void SchedulePendingProfileClose(bool hideMenuAfterClose, int delayMs)
        {
            _pendingProfileCloseAfterChat = true;
            _pendingProfileCloseHideMenu = hideMenuAfterClose;
            _pendingProfileCloseAt = unchecked(Environment.TickCount + delayMs);
        }

        private void ArmProfileCloseEscapeSuppression()
        {
            try
            {
                _suppressNextProfileCloseEscape = true;
                _suppressNextProfileCloseEscapeUntil = unchecked(Environment.TickCount + 750);
            }
            catch { }
        }

        private bool ConsumeProfileCloseEscapeSuppression(Key key)
        {
            try
            {
                if (!_suppressNextProfileCloseEscape)
                    return false;

                if (key != Key.Escape)
                    return false;

                int now = Environment.TickCount;

                if (_suppressNextProfileCloseEscapeUntil != int.MinValue &&
                    unchecked(now - _suppressNextProfileCloseEscapeUntil) > 0)
                {
                    _suppressNextProfileCloseEscape = false;
                    _suppressNextProfileCloseEscapeUntil = int.MinValue;
                    return false;
                }

                _suppressNextProfileCloseEscape = false;
                _suppressNextProfileCloseEscapeUntil = int.MinValue;
                return true;
            }
            catch
            {
                _suppressNextProfileCloseEscape = false;
                _suppressNextProfileCloseEscapeUntil = int.MinValue;
                return false;
            }
        }

        private void ClearProfileCloseEscapeSuppression()
        {
            _suppressNextProfileCloseEscape = false;
            _suppressNextProfileCloseEscapeUntil = int.MinValue;
        }

        private void CloseChatIfNeeded()
        {
            if (!IsChatEntryOpen()) return;

            try
            {
                MenuInput.SendEscape();
                Thread.Sleep(35);
            }
            catch { }
        }

        private void RequestProfileBackgroundOpen()
        {
            if (!_visible || !_noClickBackground) return;

            try
            {
                if (Hud.Window == null || !Hud.Window.IsForeground || Hud.Game == null || !Hud.Game.IsInGame)
                    return;
            }
            catch { return; }

            bool visibleNow = IsProfileWindowVisible();

            if (_profileBackgroundActiveForMenu)
            {
                _profileOpenRequested = true;
                if (_profileOpenRequestTick == int.MinValue)
                    _profileOpenRequestTick = Environment.TickCount;

                if (visibleNow)
                    _profileConfirmedVisibleForMenu = true;

                return;
            }

            _profileWasVisibleBeforeMenu = visibleNow;
            _profileBackgroundActiveForMenu = true;
            _profileOpenRequested = true;
            _profileOpenRequestTick = Environment.TickCount;
            _profileConfirmedVisibleForMenu = visibleNow;

            if (visibleNow)
            {
                _profileOpenedByHudMenu = false;
                return;
            }

            try
            {
                CloseChatIfNeeded();
                _status = "NO-CLICK BACKGROUND: OPENING BACKDROP";
                LogDebug("No-Click background open request: Shift+P sent.");
                MenuInput.SendShiftP();
                _profileOpenRequestTick = Environment.TickCount;
                _profileOpenedByHudMenu = true;
                _profileConfirmedVisibleForMenu = false;
            }
            catch { }
        }

        private void RequestProfileBackgroundClose(bool hideMenuAfterClose)
        {
            bool bound = IsProfileBoundForMenu();

            if (!bound)
            {
                if (hideMenuAfterClose)
                {
                    HideMenuStateOnly();
                    _status = "MENU CLOSED";
                }

                ClearProfileBackgroundState();
                return;
            }

            LogDebug("No-Click background close requested. hideMenuAfterClose=" + hideMenuAfterClose + ".");

            try
            {
                if (Hud.Window == null || !Hud.Window.IsForeground || Hud.Game == null || !Hud.Game.IsInGame)
                {
                    if (hideMenuAfterClose)
                    {
                        HideMenuStateOnly();
                        _status = "MENU CLOSED";
                    }

                    ClearProfileBackgroundState();
                    return;
                }
            }
            catch
            {
                if (hideMenuAfterClose)
                {
                    HideMenuStateOnly();
                    _status = "MENU CLOSED";
                }

                ClearProfileBackgroundState();
                return;
            }

            try
            {
                if (IsChatEntryOpen())
                {
                    LogDebug("Chat detected before No-Click background close; Escape sent and Shift+P deferred.");

                    // Schedule first. SendEscape may generate a key event immediately or shortly
                    // after chat closes; the pending close must already exist before that happens.
                    SchedulePendingProfileClose(hideMenuAfterClose, ChatCloseBeforeProfileCloseMs);
                    ArmProfileCloseEscapeSuppression();

                    MenuInput.SendEscape();
                    return;
                }

                _status = "NO-CLICK BACKGROUND: CLOSING BACKDROP";
                LogDebug("No-Click background close request: Shift+P sent.");
                MenuInput.SendShiftP();

                QueueOverlayHideAfterProfileClose(hideMenuAfterClose);
            }
            catch
            {
                if (hideMenuAfterClose)
                {
                    HideMenuStateOnly();
                    _status = "MENU CLOSED";
                }

                ClearProfileBackgroundState();
            }
        }

        private void QueueOverlayHideAfterProfileClose(bool hideMenuAfterClose)
        {
            int now = Environment.TickCount;

            _pendingOverlayHideAfterProfileClose = true;
            _pendingOverlayHideShouldHideMenu = hideMenuAfterClose;
            _pendingOverlayHideStartedAt = now;
            _pendingOverlayHideAt = unchecked(now + ProfileCloseOverlayHideDelayMs);
        }

        private void ProcessPendingProfileClose()
        {
            if (!_pendingProfileCloseAfterChat) return;

            int now = Environment.TickCount;
            if (unchecked(now - _pendingProfileCloseAt) < 0) return;

            bool hideMenuAfterClose = _pendingProfileCloseHideMenu;

            try
            {
                if (IsChatEntryOpen())
                {
                    // Chat is still open. Do not send Shift+P into chat.
                    _pendingProfileCloseAt = unchecked(now + ChatCloseBeforeProfileCloseMs);
                    return;
                }

                _pendingProfileCloseAfterChat = false;
                _pendingProfileCloseAt = int.MinValue;
                _pendingProfileCloseHideMenu = true;
                ClearProfileCloseEscapeSuppression();

                _status = "NO-CLICK BACKGROUND: CLOSING AFTER CHAT";
                LogDebug("Pending No-Click background close after chat: Shift+P sent.");
                MenuInput.SendShiftP();

                QueueOverlayHideAfterProfileClose(hideMenuAfterClose);
            }
            catch
            {
                _pendingProfileCloseAfterChat = false;
                _pendingProfileCloseAt = int.MinValue;
                _pendingProfileCloseHideMenu = true;
                ClearProfileCloseEscapeSuppression();

                if (hideMenuAfterClose)
                {
                    HideMenuStateOnly();
                    _status = "MENU CLOSED";
                }

                ClearProfileBackgroundState();
                SaveSettings();
            }
        }

        private void ProcessPendingOverlayHideAfterProfileClose()
        {
            if (!_pendingOverlayHideAfterProfileClose) return;

            int now = Environment.TickCount;
            if (unchecked(now - _pendingOverlayHideAt) < 0) return;

            bool hideMenu = _pendingOverlayHideShouldHideMenu;

            bool profileStillVisible = false;
            try { profileStillVisible = IsProfileWindowVisible(); }
            catch { profileStillVisible = false; }

            if (profileStillVisible)
            {
                _pendingOverlayHideAt = unchecked(now + ProfileCloseOverlayHideDelayMs);
                _status = "NO-CLICK BACKGROUND: WAITING FOR PROFILE CLOSE";
                return;
            }

            _pendingOverlayHideAfterProfileClose = false;
            _pendingOverlayHideAt = int.MinValue;
            _pendingOverlayHideStartedAt = int.MinValue;
            _pendingOverlayHideShouldHideMenu = true;

            if (hideMenu)
            {
                HideMenuStateOnly();
                _status = "MENU CLOSED";
            }

            ClearProfileBackgroundState();
            SaveSettings();
        }

        private void ClearProfileBackgroundState()
        {
            _profileOpenedByHudMenu = false;
            _profileWasVisibleBeforeMenu = false;
            _profileBackgroundActiveForMenu = false;
            _profileOpenRequested = false;
            _profileOpenRequestTick = int.MinValue;
            _profileConfirmedVisibleForMenu = false;

            _pendingProfileCloseAfterChat = false;
            _pendingProfileCloseAt = int.MinValue;
            _pendingProfileCloseHideMenu = true;

            _pendingOverlayHideAfterProfileClose = false;
            _pendingOverlayHideShouldHideMenu = true;
            _pendingOverlayHideAt = int.MinValue;
            _pendingOverlayHideStartedAt = int.MinValue;

            _profileCloseMaskMouseDown = false;
            _profileCloseMaskReleasePending = false;
            ClearProfileCloseEscapeSuppression();
        }

        private void ProcessPendingProfileMaskReleaseClose()
        {
            if (!_profileCloseMaskReleasePending)
                return;

            try
            {
                // Wait until Diablo actually closes Profile. Do not force-close it
                // with Shift+P here, because that can toggle/reopen Profile.
                if (IsProfileWindowVisible())
                    return;

                _profileCloseMaskReleasePending = false;
                ClearProfileBackgroundState();
                HideMenuStateOnly();
                _status = "MENU CLOSED";
                SaveSettings();
            }
            catch
            {
                _profileCloseMaskReleasePending = false;
                ClearProfileBackgroundState();
                HideMenuStateOnly();
                _status = "MENU CLOSED";
                SaveSettings();
            }
        }

        private void SyncProfileAndOverlayPair()
        {
            try
            {
                if (!_visible)
                    return;

                bool bound =
                    _profileBackgroundActiveForMenu ||
                    _profileOpenedByHudMenu ||
                    _profileConfirmedVisibleForMenu ||
                    _profileCloseMaskReleasePending ||
                    _pendingOverlayHideAfterProfileClose ||
                    _pendingProfileCloseAfterChat;

                if (!bound)
                    return;

                if (IsProfileWindowVisible())
                {
                    _profileConfirmedVisibleForMenu = true;
                    return;
                }

                // Do not auto-hide just because Profile has not appeared yet.
                // The Profile UI locator can lag during the initial Shift+P open.
                if (_profileOpenRequested && !_profileConfirmedVisibleForMenu &&
                    !_profileCloseMaskReleasePending &&
                    !_pendingOverlayHideAfterProfileClose &&
                    !_pendingProfileCloseAfterChat)
                {
                    return;
                }

                // If Profile was confirmed visible before, or we are actively waiting
                // for a close operation to finish, Profile disappearing means HUD MENU
                // should close too.
                if (_profileConfirmedVisibleForMenu ||
                    _profileCloseMaskReleasePending ||
                    _pendingOverlayHideAfterProfileClose ||
                    _pendingProfileCloseAfterChat)
                {
                    ClearProfileBackgroundState();
                    HideMenuStateOnly();
                    _status = "MENU CLOSED";
                    SaveSettings();
                }
            }
            catch { }
        }

        private void SyncExternalProfileClosure()
        {
            if (!_visible || !_profileBackgroundActiveForMenu || !_profileOpenRequested || _profileUiElement == null)
                return;

            bool visibleNow = IsProfileWindowVisible();

            if (visibleNow)
            {
                _profileConfirmedVisibleForMenu = true;
                return;
            }

            int elapsed = unchecked(Environment.TickCount - _profileOpenRequestTick);
            if (elapsed < ProfileOpenGraceMs) return;

            // Do not hide during Profile open latency. Only treat missing Profile
            // as external closure after it was confirmed visible at least once.
            if (!_profileConfirmedVisibleForMenu) return;

            ClearProfileBackgroundState();
            CloseMenu(false);
            SaveSettings();
        }

        private bool ShouldShowProfileCloseMask()
        {
            return _visible && (_profileBackgroundActiveForMenu || _pendingOverlayHideAfterProfileClose || _pendingProfileCloseAfterChat);
        }

        private static RectangleF GetProfileCloseMaskHitRect(RectangleF r)
        {
            if (r.Width <= 0f || r.Height <= 0f)
                return RectangleF.Empty;

            const float leftTopInsetCorrectionPx = 2f;

            float left = r.Left - ProfileCloseMaskPaddingPx + leftTopInsetCorrectionPx;
            float top = r.Top - ProfileCloseMaskPaddingPx + leftTopInsetCorrectionPx;
            float right = r.Right + ProfileCloseMaskPaddingPx;
            float bottom = r.Bottom + ProfileCloseMaskPaddingPx;

            return new RectangleF(
                left,
                top,
                Math.Max(0f, right - left),
                Math.Max(0f, bottom - top));
        }

        private static bool ContainsProfileCloseMask(RectangleF r, float x, float y)
        {
            RectangleF hit = GetProfileCloseMaskHitRect(r);

            if (hit.Width <= 0f || hit.Height <= 0f)
                return false;

            return Contains(hit, x, y);
        }

        private static bool IsHiddenPluginListRow(string typeName, string fullName)
        {
            string t = typeName ?? string.Empty;
            string f = fullName ?? string.Empty;

            return IsPluginNameMatch(t, f, "s7o_AutoGemUpgradeNavigator")
                || IsPluginNameMatch(t, f, "s7o_DHStrafePrimaryCustomizer")
                || IsPluginNameMatch(t, f, "s7o_MysticEnchantCustomizer")
                || IsPluginNameMatch(t, f, "s7o_KanaiCubeCustomizer");
        }

        private static bool IsPluginNameMatch(string typeName, string fullName, string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted))
                return false;

            string t = typeName ?? string.Empty;
            string f = fullName ?? string.Empty;

            return string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, wanted, StringComparison.OrdinalIgnoreCase)
                || f.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanDisplayName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return string.Empty;

            if (string.Equals(typeName, "s7o_DHStrafePrimaryPlugin", StringComparison.OrdinalIgnoreCase))
                return "DH Strafe";

            if (string.Equals(typeName, "s7o_DHStrafe", StringComparison.OrdinalIgnoreCase))
                return "DH Strafe";

            string s = typeName;
            if (s.StartsWith("s7o_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 6);
            if (s.EndsWith("Menu", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4) + " Menu";
            s = s.Replace('_', ' ');
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(s[i - 1]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string[] WrapToggleDescription(string text, int maxCharsPerLine, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text) || maxLines <= 0)
                return new string[0];

            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
                return WrapTextApprox(text, maxCharsPerLine, maxLines);

            var result = new List<string>();
            string[] rawLines = text.Replace("\r", string.Empty).Split('\n');

            for (int i = 0; i < rawLines.Length && result.Count < maxLines; i++)
            {
                string line = (rawLines[i] ?? string.Empty).Trim();
                if (line.Length <= 0)
                    continue;

                string[] wrapped = WrapTextApprox(line, maxCharsPerLine, maxLines - result.Count);
                for (int j = 0; j < wrapped.Length && result.Count < maxLines; j++)
                    result.Add(wrapped[j]);
            }

            return result.ToArray();
        }

                private static string[] WrapTextApprox(string text, int maxCharsPerLine, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text) || maxLines <= 0)
                return new string[0];

            if (maxCharsPerLine < 8)
                maxCharsPerLine = 8;

            var lines = new List<string>();
            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];

                if (sb.Length == 0)
                {
                    sb.Append(word);
                    continue;
                }

                if (sb.Length + 1 + word.Length <= maxCharsPerLine)
                {
                    sb.Append(' ');
                    sb.Append(word);
                    continue;
                }

                lines.Add(sb.ToString());
                sb.Length = 0;
                sb.Append(word);

                if (lines.Count >= maxLines)
                    break;
            }

            if (lines.Count < maxLines && sb.Length > 0)
                lines.Add(sb.ToString());

            if (lines.Count > maxLines)
                lines.RemoveRange(maxLines, lines.Count - maxLines);

            if (lines.Count == maxLines)
            {
                int consumedWords = 0;
                for (int li = 0; li < lines.Count; li++)
                    consumedWords += lines[li].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

                if (consumedWords < words.Length)
                {
                    string last = lines[lines.Count - 1];

                    if (last.Length > Math.Max(4, maxCharsPerLine - 3))
                        last = last.Substring(0, Math.Max(1, maxCharsPerLine - 3)).TrimEnd();

                    lines[lines.Count - 1] = last + "...";
                }
            }

            return lines.ToArray();
        }

        private const float ToggleDescriptionSafetyGap = 150f;

        private static int ApproxCharsForToggleDescription(float textColumnWidth)
        {
            // Conservative universal wrap rule for bold outlined TOGGLES descriptions.
            float safeWidth = Math.Max(40f, textColumnWidth - ToggleDescriptionSafetyGap);
            // 7.4f is intentionally conservative for bold outlined Tahoma ~9.2pt.
            return ApproxCharsForWidth(safeWidth, 7.4f);
        }

        private static int ApproxCharsForWidth(float width, float avgCharWidth)
        {
            if (avgCharWidth <= 0f)
                avgCharWidth = 5.5f;

            return Math.Max(8, (int)Math.Floor(width / avgCharWidth));
        }

        private string FitButtonLabel(string text, float buttonWidth)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            int maxChars = ApproxCharsForWidth(buttonWidth - 10f, 6.2f);
            if (maxChars < 4) maxChars = 4;
            return Trim(text, maxChars);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (max <= 3 || s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        private static bool Intersects(RectangleF a, RectangleF b)
        {
            return a.Right > b.Left && a.Left < b.Right && a.Bottom > b.Top && a.Top < b.Bottom;
        }

        private static bool FullyInsideViewport(RectangleF r, RectangleF viewport)
        {
            return r.Left >= viewport.Left
                && r.Right <= viewport.Right
                && r.Top >= viewport.Top
                && r.Bottom <= viewport.Bottom;
        }

        private static bool Contains(RectangleF r, float x, float y)
        {
            return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private static bool Contains(RectangleF r, float x, float y, float inflate)
        {
            if (r.Width <= 0f || r.Height <= 0f) return false;

            return x >= r.Left - inflate &&
                   x <= r.Right + inflate &&
                   y >= r.Top - inflate &&
                   y <= r.Bottom + inflate;
        }

        private void EnsureResources()
        {
            if (_resourcesReady) return;
            _resourcesReady = true;

            _bShadow      = Hud.Render.CreateBrush(165, 0, 0, 0, 0);
            _bFrame       = Hud.Render.CreateBrush(230, 36, 42, 46, 0);
            _bFrameBorder = Hud.Render.CreateBrush(255, 48, 180, 60, 1.4f);
            _bInner       = Hud.Render.CreateBrush(215, 42, 48, 54, 0);
            _bTitle       = Hud.Render.CreateBrush(235, 18, 58, 20, 0);
            _bPane        = Hud.Render.CreateBrush(170, 35, 41, 46, 0);
            _bPaneBorder  = Hud.Render.CreateBrush(140, 120, 126, 132, 1.0f);
            _bRow         = Hud.Render.CreateBrush(205, 47, 54, 61, 0);
            _bRowAlt      = Hud.Render.CreateBrush(225, 39, 46, 53, 0);

            _bBtnNormalTop    = Hud.Render.CreateBrush(245, 58, 66, 74, 0);
            _bBtnNormalBottom = Hud.Render.CreateBrush(245, 24, 30, 36, 0);
            _bBtnActiveTop    = Hud.Render.CreateBrush(250, 48, 150, 58, 0);
            _bBtnActiveBottom = Hud.Render.CreateBrush(250, 12, 74, 24, 0);
            _bBtnDangerTop    = Hud.Render.CreateBrush(245, 142, 48, 48, 0);
            _bBtnDangerBottom = Hud.Render.CreateBrush(245, 62, 16, 16, 0);
            _bBtnDangerFlashTop    = Hud.Render.CreateBrush(255, 235, 78, 78, 0);
            _bBtnDangerFlashBottom = Hud.Render.CreateBrush(255, 122, 20, 20, 0);
            _bBtnGloss        = Hud.Render.CreateBrush(20, 255, 255, 255, 0);
            _bBtnEdge         = Hud.Render.CreateBrush(255, 4, 6, 8, 1.35f);

            _bToggleOffTop    = Hud.Render.CreateBrush(245, 96, 102, 110, 0);
            _bToggleOffBottom = Hud.Render.CreateBrush(245, 42, 46, 52, 0);
            _bToggleOnTop     = Hud.Render.CreateBrush(250, 118, 242, 92, 0);
            _bToggleOnBottom  = Hud.Render.CreateBrush(250, 24, 120, 34, 0);
            _bToggleEdge      = Hud.Render.CreateBrush(240, 12, 15, 18, 1.1f);

            _bStarOnTop     = Hud.Render.CreateBrush(245, 250, 210, 72, 0);
            _bStarOnBottom  = Hud.Render.CreateBrush(245, 150, 105, 20, 0);
            _bStarOffTop    = Hud.Render.CreateBrush(245, 92, 98, 106, 0);
            _bStarOffBottom = Hud.Render.CreateBrush(245, 42, 46, 52, 0);

            _bScrollTrackSolid       = Hud.Render.CreateBrush(235, 12, 56, 22, 0);    // dark green track bar
            _bScrollThumbGreen       = Hud.Render.CreateBrush(255, 165, 175, 182, 0); // light grey slider thumb
            _bScrollThumbGreenActive = Hud.Render.CreateBrush(255, 72, 230, 96, 0);   // bright green on drag
            _bScrollBorder           = Hud.Render.CreateBrush(255, 8, 22, 10, 1.4f);  // dark green outline stroke

            _bGlowOuter   = Hud.Render.CreateBrush(55,  80, 255, 110, 3.5f);
            _bGlowInner   = Hud.Render.CreateBrush(110, 100, 255, 140, 2.0f);
            _bPaneCover   = Hud.Render.CreateBrush(255, 35,  41,  46,  0);   // opaque pane bg for scroll edge masking

            _bDotHalo       = Hud.Render.CreateBrush(55, 70, 235, 90, 0);
            _bDotFill       = Hud.Render.CreateBrush(245, 205, 42, 42, 0);
            _bDotFillOpen   = Hud.Render.CreateBrush(245, 88, 230, 84, 0);
            _bDotShadow     = Hud.Render.CreateBrush(185, 95, 14, 14, 0);
            _bDotShadowOpen = Hud.Render.CreateBrush(185, 12, 105, 28, 0);
            _bDotRim        = Hud.Render.CreateBrush(245, 0, 0, 0, 3.6f);
            _bDotSpec       = Hud.Render.CreateBrush(130, 255, 255, 255, 0);
            _bDotHot        = Hud.Render.CreateBrush(210, 255, 255, 255, 0);
            _bEditDash      = Hud.Render.CreateBrush(225, 120, 210, 255, 1.2f);
            _bTextDimBg     = Hud.Render.CreateBrush(120, 24, 30, 35, 0);
            _bProfileMask   = Hud.Render.CreateBrush(210, 255, 70, 70, 1.6f);

            _bPreviewBlack = Hud.Render.CreateBrush(230, 0, 0, 0, 0);
            _bPreviewBorder = Hud.Render.CreateBrush(170, 120, 120, 120, 1.0f);
            _bPrevGreenDash = Hud.Render.CreateBrush(170, 160, 255, 160, 2.0f);
            _bPrevRedDash = Hud.Render.CreateBrush(180, 255, 50, 50, 2.0f);
            _bPrevOrangeDash = Hud.Render.CreateBrush(180, 255, 120, 35, 2.0f);
            _bPrevBlueDash = Hud.Render.CreateBrush(170, 200, 200, 255, 2.0f);
            _bPrevPurpleDash = Hud.Render.CreateBrush(170, 255, 60, 255, 2.0f);
            _bPrevGrey = Hud.Render.CreateBrush(190, 150, 150, 150, 0);
            _bPrevYellow = Hud.Render.CreateBrush(220, 255, 220, 70, 0);

            _fTitle   = Hud.Render.CreateFont("tahoma", 10.5f, 255, 255, 225, 70, true, false, 145, 0, 0, 0, true);
            _fLabel   = Hud.Render.CreateFont("tahoma",  8.2f, 255, 215, 222, 226, false, false, 108, 0, 0, 0, true);
            _fSection = Hud.Render.CreateFont("tahoma",  9.5f, 255, 255, 225, 70, true, false, 125, 0, 0, 0, true);
            _fText    = Hud.Render.CreateFont("tahoma",  8.2f, 255, 215, 222, 226, false, false, 105, 0, 0, 0, true);
            _fSmall   = Hud.Render.CreateFont("tahoma",  7.2f, 255, 255, 255, 255, false, false, 112, 0, 0, 0, true);
            _fButton  = Hud.Render.CreateFont("tahoma",  7.8f, 255, 245, 248, 250, false, false, 122, 0, 0, 0, true);
            _fButtonActive = Hud.Render.CreateFont("tahoma",  7.8f, 255, 255, 225, 70, false, false, 122, 0, 0, 0, true);
            _fButtonLarge = Hud.Render.CreateFont("tahoma", 10.0f, 255, 245, 248, 250, true, false, 140, 0, 0, 0, true);
            _fButtonLargeActive = Hud.Render.CreateFont("tahoma", 10.0f, 255, 255, 225, 70, true, false, 140, 0, 0, 0, true);
            _fSmallShadow  = Hud.Render.CreateFont("tahoma",  7.2f, 245, 0, 0, 0, false, false, 0, 0, 0, 0, false);
            _fButtonShadow = Hud.Render.CreateFont("tahoma",  7.8f, 245, 0, 0, 0, false, false, 0, 0, 0, 0, false);
            _fButtonLargeShadow = Hud.Render.CreateFont("tahoma", 10.0f, 245, 0, 0, 0, true, false, 0, 0, 0, 0, false);

            // Local row fonts for larger TOGGLES rows — not used globally.
            _fRowTitle = Hud.Render.CreateFont("tahoma", 9.8f, 255, 255, 225, 70, true, false, 145, 0, 0, 0, true);
            _fRowText  = Hud.Render.CreateFont("tahoma", 9.2f, 255, 235, 242, 246, true, false, 135, 0, 0, 0, true);

            _fRowTitleShadow = Hud.Render.CreateFont("tahoma", 9.8f, 245, 0, 0, 0, true, false, 0, 0, 0, 0, false);
            _fRowTextShadow  = Hud.Render.CreateFont("tahoma", 9.2f, 245, 0, 0, 0, true, false, 0, 0, 0, 0, false);

            _fStar       = Hud.Render.CreateFont("tahoma", 9.4f, 255, 255, 225, 70, true, false, 140, 0, 0, 0, true);
            _fStarShadow = Hud.Render.CreateFont("tahoma", 9.4f, 245, 0, 0, 0, true, false, 0, 0, 0, 0, false);
            EnsureVisualPickerBrushes();
        }

        // ── AutoSnap runtime helpers ─────────────────────────────────────────────
        private void RefreshAutoSnapSkillCache()
        {
            try
            {
                if (Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                    return;

                for (int i = 0; i < _asSkills.Length; i++)
                    _asSkills[i] = null;

                foreach (var skill in Hud.Game.Me.Powers.SkillSlots)
                {
                    if (skill == null)
                        continue;

                    switch (skill.Key)
                    {
                        case ActionKey.Skill1:
                            _asSkills[0] = skill;
                            break;

                        case ActionKey.Skill2:
                            _asSkills[1] = skill;
                            break;

                        case ActionKey.Skill3:
                            _asSkills[2] = skill;
                            break;

                        case ActionKey.Skill4:
                            _asSkills[3] = skill;
                            break;

                        case ActionKey.LeftSkill:
                            _asSkills[4] = skill;
                            break;

                        case ActionKey.RightSkill:
                            _asSkills[5] = skill;
                            break;
                    }
                }
            }
            catch { }
        }

        private bool GetAnyAutoSnapSlotEnabled(int slot)
        {
            if (slot < 0 || slot >= _asEnabled.Length) return false;
            return _asEnabled[slot];
        }

        private void ToggleAnyAutoSnapSlot(int slot)
        {
            if (slot < 0 || slot >= _asEnabled.Length) return;
            _asEnabled[slot] = !_asEnabled[slot];
        }

        private bool SkipAnyAutoSnap()
        {
            try
            {
                var g = Hud.Game;
                if (g == null || !g.IsInGame || g.IsLoading || g.IsPaused) return true;
                var me = g.Me;
                if (me == null || me.IsDead) return true;
                if (!Hud.Window.IsForeground) return true;
                if (AnySnapHasTypingOrBlockingUi()) return true;
                return false;
            }
            catch { return true; }
        }

        private void AnySnapSetLeftStandStill(bool shouldHold)
        {
            try
            {
                const byte VK_SHIFT = 0x10;
                if (shouldHold)
                {
                    if (_asLeftStandStillOwned) return;
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    _asLeftStandStillOwned = true;
                }
                else
                {
                    if (!_asLeftStandStillOwned) return;
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero);
                    _asLeftStandStillOwned = false;
                }
            }
            catch
            {
                if (!shouldHold) _asLeftStandStillOwned = false;
            }
        }

        private float AnySnapGetLeftStandStillRangeYards()
        {
            try
            {
                bool meleeMode = _autoSnapMode == 0;
                float configured = Math.Max(0f, meleeMode ? _autoSnapMeleeRangeYards : _autoSnapRangedRangeYards);
                return (configured > 0f) ? configured : 15f;
            }
            catch { return 15f; }
        }

        private bool AnySnapShouldHoldLeftStandStill(ActionKey heldKey, IMonster target)
        {
            try
            {
                if (heldKey != ActionKey.LeftSkill) return false;
                if (!_autoSnapLeftClickForceStandStill) return false;
                if (target == null || !target.IsAlive) return false;

                bool meleeMode = _autoSnapMode == 0;
                if (!meleeMode) return true;

                float engage = AnySnapGetLeftStandStillRangeYards();
                float dist = (float)target.CentralXyDistanceToMe;
                return dist <= engage;
            }
            catch { return false; }
        }

        private void ClearAnySnapHotkeyCapture()
        {
            if (_asHotkeyCaptureActive == null) return;
            for (int i = 0; i < _asHotkeyCaptureActive.Length; i++) _asHotkeyCaptureActive[i] = false;
        }

        private void SetAnySnapHotkey(int slot, Key k, string forcedLabel = null)
        {
            SetAnySnapHotkeyInput(slot, HotkeyInputType.Keyboard, k, KeyToWinKeys(k), forcedLabel);
        }

        private void SetAnySnapHotkeyInput(int slot, HotkeyInputType type, Key k, Keys winKey, string forcedLabel = null)
        {
            if (slot < 0 || slot >= _asHotkeyKeys.Length) return;
            _asHotkeyInputTypes[slot] = type;
            _asHotkeyKeys[slot] = (type == HotkeyInputType.Keyboard) ? k : Key.Unknown;
            _asHotkeyWinKeys[slot] = (type == HotkeyInputType.Keyboard) ? winKey : Keys.None;
            _asHotkeyLabels[slot] = string.IsNullOrWhiteSpace(forcedLabel) ? (type == HotkeyInputType.Keyboard ? CaptureKeyLabel(k) : HotkeyInputLabel(type)) : forcedLabel;
        }

        private bool AnySnapHasTypingOrBlockingUi()
        {
            try
            {
                return IsFreeHudBlockingUiVisible();
            }
            catch
            {
                return true;
            }
        }

        private bool AnySnapHotkeysCanHandle()
        {
            try
            {
                var g = Hud.Game;
                if (!_asHotkeysEnabled) return false;
                if (g == null || !g.IsInGame || g.IsLoading || g.IsPaused) return false;
                var me = g.Me;
                if (me == null || me.IsDead) return false;
                if (!Hud.Window.IsForeground) return false;
                if (AnySnapHasTypingOrBlockingUi()) return false;
                return true;
            }
            catch { return false; }
        }

        private bool AnySnapHotkeyOnlyActive()
        {
            return _asHotkeysEnabled && _asHotkeysOnly;
        }

        private bool AnySnapTryGetHotkeySlotByPressedKey(Key key, out int slot)
        {
            slot = -1;
            if (!_asHotkeysEnabled || _asHotkeyKeys == null) return false;
            for (int i = 0; i < _asHotkeyKeys.Length; i++)
            {
                if (!_asEnabled[i]) continue;
                if (key == _asHotkeyKeys[i]) { slot = i; return true; }
                // Windows key fallback for layout-sensitive keys
                try
                {
                    if (_asHotkeyWinKeys != null && i < _asHotkeyWinKeys.Length &&
                        _asHotkeyWinKeys[i] != Keys.None && Hud.Input != null &&
                        Hud.Input.IsKeyDown(_asHotkeyWinKeys[i]))
                    { slot = i; return true; }
                }
                catch { }
            }
            return false;
        }

        private bool AnySnapIsHotkeyDown(int slot)
        {
            try
            {
                if (slot < 0 || slot >= _asHotkeyKeys.Length) return false;
                if (!_asEnabled[slot]) return false;
                return IsAssignedHotkeyDown(_asHotkeyInputTypes[slot], _asHotkeyKeys[slot], _asHotkeyWinKeys[slot]);
            }
            catch { return false; }
        }

        private void AnySnapQueueHotkeyRequest(int slot, int tick, float cursorX, float cursorY)
        {
            if (slot < 0 || slot >= _asSkills.Length)
                return;

            ActionKey action = AnySnapGetActionForSlot(slot);
            if (action == ActionKey.Unknown)
                return;

            _asHotkeyRequestPending = true;
            _asHotkeyRequestSlot = slot;
            _asHotkeyRequestTick = tick;
            _asHotkeyRequestCursorX = cursorX;
            _asHotkeyRequestCursorY = cursorY;

            if (_asHotkeyPressStartTick <= 0)
            {
                _asHotkeyPressStartTick = tick;
                _asHotkeySavedX = cursorX;
                _asHotkeySavedY = cursorY;
            }
        }


        private void AnySnapResetHotkeyHoldState()
        {
            try
            {
                for (int i = 0; i < _asHotkeyHoldStartTick.Length; i++)
                {
                    _asHotkeyHoldStartTick[i] = -1000000;
                    _asHotkeyLastRepeatTick[i] = 0;
                }
                if (_asHotkeyDownPrev != null)
                    for (int i = 0; i < _asHotkeyDownPrev.Length; i++)
                        _asHotkeyDownPrev[i] = false;
            }
            catch { }
        }

        private void AnySnapResetHotkeyHoldState(int slot)
        {
            try
            {
                if (slot >= 0 && slot < _asHotkeyHoldStartTick.Length)  _asHotkeyHoldStartTick[slot]  = -1000000;
                if (slot >= 0 && slot < _asHotkeyLastRepeatTick.Length) _asHotkeyLastRepeatTick[slot] = 0;
                if (_asHotkeyDownPrev != null && slot >= 0 && slot < _asHotkeyDownPrev.Length)
                    _asHotkeyDownPrev[slot] = false;
            }
            catch { }
        }

        private void AnySnapHotkeyResetTransient()
        {
            _asHotkeyRequestPending = false;
            _asHotkeyRequestSlot = -1;
            _asHotkeyRequestTick = 0;

            _asHotkeyCastPending = false;
            _asHotkeyCastTick = 0;
            _asHotkeyCastAction = ActionKey.Unknown;

            _asHotkeyRestorePending = false;
            _asHotkeyRestoreTick = 0;

            _asReleaseRestorePending = false;
            _asReleaseRestoreSlot = -1;
            _asReleaseRestorePressStartTick = 0;
            _asReleaseRestoreX = 0f;
            _asReleaseRestoreY = 0f;

            _asHotkeyActiveSlot = -1;
            _asHotkeyAimX = 0f;
            _asHotkeyAimY = 0f;
        }


        private void AnySnapHotkeyOnPress(int slot, int tick)
        {
            if (slot < 0 || slot >= _asHotkeyHoldStartTick.Length) return;
            if (_asHotkeyHoldStartTick[slot] <= -999000)
                _asHotkeyHoldStartTick[slot] = tick;
            _asHotkeyLastRepeatTick[slot] = tick;

            try
            {
                _asHotkeyPressStartTick = tick;
                _asHotkeySavedX = Hud.Window.CursorX;
                _asHotkeySavedY = Hud.Window.CursorY;
            }
            catch
            {
                _asHotkeyPressStartTick = tick;
                _asHotkeySavedX = 0f;
                _asHotkeySavedY = 0f;
            }

            if (!_asHotkeyRequestPending && !_asHotkeyCastPending)
            {
                try { AnySnapQueueHotkeyRequest(slot, tick, Hud.Window.CursorX, Hud.Window.CursorY); }
                catch { }
            }
        }


        private static ActionKey AnySnapSlotToActionKey(int slot)
        {
            switch (slot)
            {
                case 0: return ActionKey.Skill1;
                case 1: return ActionKey.Skill2;
                case 2: return ActionKey.Skill3;
                case 3: return ActionKey.Skill4;
                case 4: return ActionKey.LeftSkill;
                case 5: return ActionKey.RightSkill;
                default: return ActionKey.Unknown;
            }
        }

        private ActionKey AnySnapGetActionForSlot(int slot)
        {
            if (slot < 0 || slot >= 6)
                return ActionKey.Unknown;

            try
            {
                if (_asSkills != null && slot < _asSkills.Length && _asSkills[slot] != null)
                    return _asSkills[slot].Key;
            }
            catch { }

            return AnySnapSlotToActionKey(slot);
        }

        private void AnySnapHotkeyTapAction(ActionKey key)
        {
            try
            {
                switch (key)
                {
                    case ActionKey.Skill1:
                        AnySnapSuppressHeldActionEcho(key);
                        TapVirtualKey(0x31, 8);
                        return;

                    case ActionKey.Skill2:
                        AnySnapSuppressHeldActionEcho(key);
                        TapVirtualKey(0x32, 8);
                        return;

                    case ActionKey.Skill3:
                        AnySnapSuppressHeldActionEcho(key);
                        TapVirtualKey(0x33, 8);
                        return;

                    case ActionKey.Skill4:
                        AnySnapSuppressHeldActionEcho(key);
                        TapVirtualKey(0x34, 8);
                        return;

                    case ActionKey.LeftSkill:
                        AnySnapSuppressHeldActionEcho(key);
                        try
                        {
                            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                            Thread.Sleep(8);
                        }
                        finally
                        {
                            try { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); } catch { }
                        }
                        return;

                    case ActionKey.RightSkill:
                        AnySnapSuppressHeldActionEcho(key);
                        try
                        {
                            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                            Thread.Sleep(8);
                        }
                        finally
                        {
                            try { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero); } catch { }
                        }
                        return;
                }
            }
            catch { }
        }

        private int AnySnapCurrentTick()
        {
            try { return Hud.Game != null ? Hud.Game.CurrentGameTick : Environment.TickCount; }
            catch { return Environment.TickCount; }
        }

        private void AnySnapSuppressHeldActionEcho(ActionKey key)
        {
            try
            {
                if (key == ActionKey.Unknown)
                    return;

                int tick = AnySnapCurrentTick();
                _asSuppressHeldActionKey = key;
                _asSuppressHeldActionUntilTick = unchecked(tick + AnySnapSyntheticActionEchoSuppressTicks);
            }
            catch { }
        }

        private bool AnySnapIsHeldActionEchoSuppressed(ActionKey key)
        {
            try
            {
                if (key == ActionKey.Unknown || key != _asSuppressHeldActionKey)
                    return false;

                if (_asSuppressHeldActionUntilTick == 0)
                    return false;

                int tick = AnySnapCurrentTick();
                if (unchecked(tick - _asSuppressHeldActionUntilTick) < 0)
                    return true;

                _asSuppressHeldActionKey = ActionKey.Unknown;
                _asSuppressHeldActionUntilTick = 0;
            }
            catch { }

            return false;
        }

        private void AnySnapClearRestoreTracking()
        {
            _asRestoreTrackActive = false;
            _asRestoreWasMoved = false;
            _asRestoreStartTick = 0;
            _asRestoreSavedX = 0f;
            _asRestoreSavedY = 0f;
            _asRestoreKey = ActionKey.Unknown;
        }

        private void AnySnapClearAllRestoreState()
        {
            AnySnapClearRestoreTracking();

            _asHotkeyRestorePending = false;
            _asHotkeyRestoreTick = 0;

            _asReleaseRestorePending = false;
            _asReleaseRestoreSlot = -1;
            _asReleaseRestorePressStartTick = 0;
            _asReleaseRestoreX = 0f;
            _asReleaseRestoreY = 0f;

            _asHotkeyPressStartTick = 0;
            _asHotkeySavedX = 0f;
            _asHotkeySavedY = 0f;
            _asHotkeyAimX = 0f;
            _asHotkeyAimY = 0f;
        }


        private void AnySnapBeginRestoreTracking(ActionKey heldKey, int tick)
        {
            if (!_autoSnapRestoreCursor) { AnySnapClearRestoreTracking(); return; }
            if (_asRestoreTrackActive && _asRestoreKey == heldKey) return;

            _asRestoreTrackActive = true;
            _asRestoreWasMoved = false;
            _asRestoreStartTick = tick;
            _asRestoreKey = heldKey;
            try
            {
                _asRestoreSavedX = Hud.Window.CursorX;
                _asRestoreSavedY = Hud.Window.CursorY;
            }
            catch
            {
                _asRestoreSavedX = 0f;
                _asRestoreSavedY = 0f;
            }
        }

        private void AnySnapOnCursorMovedByPlugin()
        {
            if (_asRestoreTrackActive)
                _asRestoreWasMoved = true;
        }

        private bool AnySnapAnyHotkeyHeld()
        {
            try
            {
                for (int i = 0; i < _asHotkeyKeys.Length; i++)
                    if (AnySnapIsHotkeyDown(i)) return true;
            }
            catch { }
            return false;
        }

        private void AnySnapMoveCursor(float x, float y)
        {
            try
            {
                float w = 1920f;
                float h = 1080f;

                try
                {
                    if (Hud != null && Hud.Window != null)
                    {
                        w = Hud.Window.Size.Width;
                        h = Hud.Window.Size.Height;
                    }
                }
                catch { }

                x = Math.Max(0f, Math.Min(w - 1f, x));
                y = Math.Max(0f, Math.Min(h - 1f, y));

                SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            }
            catch { }
        }

        private void AnySnapRestoreCursor(float x, float y)
        {
            AnySnapMoveCursor(x, y);
        }


        private void AnySnapProcessReleaseRestore(int tick)
        {
            if (!_asReleaseRestorePending)
                return;

            bool stillHeld = false;

            try
            {
                if (_asReleaseRestoreSlot >= 0)
                    stillHeld = AnySnapIsHotkeyDown(_asReleaseRestoreSlot);
                else
                    stillHeld = AnySnapAnyHotkeyHeld();
            }
            catch { }

            int heldTicks = (_asReleaseRestorePressStartTick > 0)
                ? Math.Max(0, tick - _asReleaseRestorePressStartTick)
                : int.MaxValue;

            // Long hold / channeling behavior:
            // After the tolerance window expires, abandon restore and let cursor stick.
            if (stillHeld)
            {
                if (heldTicks > AnySnapRestoreCursorMaxHoldTicks)
                {
                    _asReleaseRestorePending = false;
                    _asReleaseRestoreSlot = -1;
                    _asReleaseRestorePressStartTick = 0;
                }

                return;
            }

            try
            {
                if (_autoSnapRestoreCursor && heldTicks <= AnySnapRestoreCursorMaxHoldTicks)
                {
                    float dx = _asReleaseRestoreX - Hud.Window.CursorX;
                    float dy = _asReleaseRestoreY - Hud.Window.CursorY;

                    if ((dx * dx + dy * dy) >= AnySnapRestoreMinMovePxSq)
                        AnySnapRestoreCursor(_asReleaseRestoreX, _asReleaseRestoreY);
                }
            }
            catch { }

            _asReleaseRestorePending = false;
            _asReleaseRestoreSlot = -1;
            _asReleaseRestorePressStartTick = 0;

            if (!AnySnapAnyHotkeyHeld() &&
                !_asHotkeyRequestPending &&
                !_asHotkeyCastPending &&
                !_asHotkeyRestorePending)
            {
                _asHotkeyPressStartTick = 0;
            }
        }


        private void AnySnapHandleReleaseRestore(int tick)
        {
            try
            {
                if (!_asRestoreTrackActive)
                    return;

                if (AnySnapIsActionHeld(_asRestoreKey))
                    return;

                int heldTicks = (_asRestoreStartTick > 0)
                    ? Math.Max(0, tick - _asRestoreStartTick)
                    : int.MaxValue;

                bool restoreEnabled = _autoSnapRestoreCursor && _asRestoreWasMoved;
                bool shouldRestore = restoreEnabled && heldTicks <= AnySnapRestoreCursorMaxHoldTicks;

                if (shouldRestore)
                {
                    float dx = 0f;
                    float dy = 0f;

                    try
                    {
                        dx = _asRestoreSavedX - Hud.Window.CursorX;
                        dy = _asRestoreSavedY - Hud.Window.CursorY;
                    }
                    catch { }

                    if ((dx * dx + dy * dy) >= AnySnapRestoreMinMovePxSq)
                        AnySnapRestoreCursor(_asRestoreSavedX, _asRestoreSavedY);
                }
            }
            catch { }
            finally
            {
                AnySnapClearRestoreTracking();
            }
        }


        private void RunAnyAutoSnapHotkeys()
        {
            int tick = 0;
            try { tick = Hud.Game != null ? Hud.Game.CurrentGameTick : Environment.TickCount; }
            catch { tick = Environment.TickCount; }

            if (!AnySnapHotkeysCanHandle())
            {
                AnySnapHotkeyResetTransient();
                AnySnapClearAllRestoreState();
                AnySnapResetHotkeyHoldState();
                return;
            }

            if (!_autoSnapRestoreCursor)
                AnySnapClearAllRestoreState();

            for (int i = 0; i < _asHotkeyKeys.Length; i++)
            {
                bool down = AnySnapIsHotkeyDown(i);
                bool wasDown = (_asHotkeyDownPrev != null && i < _asHotkeyDownPrev.Length) ? _asHotkeyDownPrev[i] : false;

                if (down && !wasDown)
                    AnySnapHotkeyOnPress(i, tick);

                if (down && _asHotkeyHoldStartTick[i] > -999000)
                {
                    int heldTicks = Math.Max(0, tick - _asHotkeyHoldStartTick[i]);

                    if (heldTicks >= AnySnapHotkeyHoldRepeatStartTicks &&
                        (tick - _asHotkeyLastRepeatTick[i]) >= AnySnapHotkeyHoldRepeatIntervalTicks &&
                        !_asHotkeyRequestPending &&
                        !_asHotkeyCastPending)
                    {
                        _asHotkeyLastRepeatTick[i] = tick;

                        try
                        {
                            AnySnapQueueHotkeyRequest(i, tick, Hud.Window.CursorX, Hud.Window.CursorY);
                        }
                        catch { }
                    }
                }

                if (!down && wasDown)
                    AnySnapResetHotkeyHoldState(i);

                if (_asHotkeyDownPrev != null && i < _asHotkeyDownPrev.Length)
                    _asHotkeyDownPrev[i] = down;
            }

            if (_asHotkeyRequestPending && !_asHotkeyCastPending)
            {
                int slot = _asHotkeyRequestSlot;
                ActionKey castAction = AnySnapGetActionForSlot(slot);

                if (castAction == ActionKey.Unknown)
                {
                    AnySnapHotkeyResetTransient();
                    return;
                }

                // Default aim is the user's current cursor.
                // This is the critical no-target fix: the hotkey must still cast with no monsters around.
                float aimX = _asHotkeyRequestCursorX;
                float aimY = _asHotkeyRequestCursorY;

                try
                {
                    var target = AnySnapPickTarget(_asHotkeyRequestCursorX, _asHotkeyRequestCursorY);

                    if (target != null && target.IsAlive && target.IsOnScreen)
                    {
                        float sx, sy;

                        if (AnySnapGetScreen(target, out sx, out sy))
                        {
                            aimX = sx;
                            aimY = sy;
                        }
                    }
                }
                catch { }

                if ((castAction == ActionKey.LeftSkill || castAction == ActionKey.RightSkill) &&
                    !AnySnapIsSafeScreenTarget(aimX, aimY))
                {
                    AnySnapHotkeyResetTransient();
                    return;
                }

                _asHotkeyAimX = aimX;
                _asHotkeyAimY = aimY;
                _asHotkeyCastAction = castAction;
                _asHotkeyActiveSlot = slot;

                try
                {
                    float dx = aimX - Hud.Window.CursorX;
                    float dy = aimY - Hud.Window.CursorY;

                    if ((dx * dx + dy * dy) >= 1f)
                        AnySnapMoveCursor(aimX, aimY);
                }
                catch { }

                _asHotkeyCastPending = true;
                _asHotkeyCastTick = tick + Math.Max(0, AnySnapHotkeyPreCastDelayTicks);
                _asHotkeyRequestPending = false;
            }

            if (_asHotkeyCastPending && tick >= _asHotkeyCastTick)
            {
                try { AnySnapHotkeyTapAction(_asHotkeyCastAction); }
                catch { }

                if (_autoSnapRestoreCursor && _asHotkeyPressStartTick > 0)
                {
                    _asReleaseRestorePending = true;
                    _asReleaseRestoreSlot = _asHotkeyActiveSlot;
                    _asReleaseRestorePressStartTick = _asHotkeyPressStartTick;
                    _asReleaseRestoreX = _asHotkeySavedX;
                    _asReleaseRestoreY = _asHotkeySavedY;
                }

                _asHotkeyCastPending = false;
                _asHotkeyCastTick = 0;
                _asHotkeyCastAction = ActionKey.Unknown;
            }

            AnySnapProcessReleaseRestore(tick);

            if (_autoSnapRestoreCursor &&
                !AnySnapAnyHotkeyHeld() &&
                !_asHotkeyRequestPending &&
                !_asHotkeyCastPending &&
                !_asReleaseRestorePending &&
                _asHotkeyPressStartTick > 0)
            {
                _asHotkeyPressStartTick = 0;
            }
        }


        private void RunAnyAutoSnap()
        {
            int tick = Hud.Game != null ? Hud.Game.CurrentGameTick : Environment.TickCount;

            if (!_autoSnapRestoreCursor)
                AnySnapClearRestoreTracking();

            if (SkipAnyAutoSnap())
            {
                AnySnapSetLeftStandStill(false);
                AnySnapHandleReleaseRestore(tick);
                return;
            }

            if (AnySnapHotkeyOnlyActive())
            {
                AnySnapSetLeftStandStill(false);
                AnySnapHandleReleaseRestore(tick);
                return;
            }

            ActionKey heldKey;
            if (!TryGetAnyAutoSnapHeldAction(out heldKey))
            {
                _asLockedAcdId = 0;
                AnySnapSetLeftStandStill(false);
                AnySnapHandleReleaseRestore(tick);
                return;
            }

            AnySnapBeginRestoreTracking(heldKey, tick);

            var target = AnySnapPickTarget(Hud.Window.CursorX, Hud.Window.CursorY);

            if (target == null || !target.IsAlive || !target.IsOnScreen)
            {
                _asLockedAcdId = 0;
                AnySnapSetLeftStandStill(false);
                return;
            }

            AnySnapSetLeftStandStill(AnySnapShouldHoldLeftStandStill(heldKey, target));

            float sx, sy;
            if (!AnySnapGetScreen(target, out sx, out sy))
            {
                _asLockedAcdId = 0;
                AnySnapSetLeftStandStill(false);
                return;
            }

            try
            {
                if (tick > 0 && tick == _asLastMoveTick)
                    return;

                float dx = sx - Hud.Window.CursorX;
                float dy = sy - Hud.Window.CursorY;

                if ((dx * dx + dy * dy) < AnySnapRestoreMinMovePxSq)
                    return;

                AnySnapMoveCursor(sx, sy);
                AnySnapOnCursorMovedByPlugin();

                _asLockedAcdId = AnySnapGetAcdId(target);
                _asLastMoveTick = tick;
            }
            catch
            {
                AnySnapSetLeftStandStill(false);
                AnySnapClearRestoreTracking();
            }
        }


        private bool TryGetAnyAutoSnapHeldAction(out ActionKey key)
        {
            key = ActionKey.Unknown;
            try
            {
                for (int i = 0; i < _asSkills.Length; i++)
                {
                    if (!_asEnabled[i]) continue;
                    var skill = _asSkills[i];
                    if (skill == null) continue;
                    if (AnySnapIsActionHeld(skill.Key))
                    {
                        key = skill.Key;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool AnySnapIsActionHeld(ActionKey key)
        {
            try
            {
                if (AnySnapIsHeldActionEchoSuppressed(key))
                    return false;

                bool b;

                if (ZbTryGetBool(key, "IsPressed", out b) ||
                    ZbTryGetBool(key, "Pressed", out b) ||
                    ZbTryGetBool(key, "IsDown", out b) ||
                    ZbTryGetBool(key, "Down", out b) ||
                    ZbTryGetBool(key, "IsHeld", out b) ||
                    ZbTryGetBool(key, "Held", out b) ||
                    ZbTryGetBool(key, "IsKeyDown", out b))
                    return b;

                Keys k;
                if (!TryMapActionKeyToWindowsKey(key, out k))
                    return false;

                return Hud.Input != null && Hud.Input.IsKeyDown(k);
            }
            catch
            {
                return false;
            }
        }


        private bool TryMapActionKeyToWindowsKey(ActionKey key, out Keys keyOut)
        {
            keyOut = Keys.None;
            switch (key)
            {
                case ActionKey.Skill1: keyOut = Keys.D1; return true;
                case ActionKey.Skill2: keyOut = Keys.D2; return true;
                case ActionKey.Skill3: keyOut = Keys.D3; return true;
                case ActionKey.Skill4: keyOut = Keys.D4; return true;
                case ActionKey.LeftSkill: keyOut = Keys.LButton; return true;
                case ActionKey.RightSkill: keyOut = Keys.RButton; return true;
                default: return false;
            }
        }

        private IMonster AnySnapPickTarget(float anchorCursorX, float anchorCursorY)
        {
            try
            {
                var monsters = Hud.Game != null ? Hud.Game.AliveMonsters : null;
                var me = Hud.Game != null ? Hud.Game.Me : null;

                if (monsters == null || me == null)
                    return null;

                bool meleeMode = _autoSnapMode == 0;
                bool ignoreJuggernauts = _autoSnapIgnoreJuggernauts;
                bool ignoreMinions = _autoSnapIgnoreMinions;
                float range = Math.Max(0f, meleeMode ? _autoSnapMeleeRangeYards : _autoSnapRangedRangeYards);

                IWorldCoordinate anchorWorld = meleeMode
                    ? me.FloorCoordinate
                    : AutoSnapGetWorld(anchorCursorX, anchorCursorY);

                const float scanMax = 90f;

                // Bosses first.
                IMonster bestBoss = null;
                float bestBossScore = float.MaxValue;

                foreach (var bm in monsters)
                {
                    if (bm == null || !bm.IsAlive || bm.Rarity != ActorRarity.Boss) continue;
                    if (!bm.IsOnScreen) continue;
                    if (AnySnapShouldIgnoreMonster(bm)) continue;

                    try
                    {
                        if ((float)bm.CentralXyDistanceToMe > scanMax) continue;
                    }
                    catch { continue; }

                    float bossScore = meleeMode
                        ? AnySnapCloseBucketScore((float)bm.CentralXyDistanceToMe)
                        : AnySnapAnchorDistance(anchorWorld, bm);

                    if (bossScore < bestBossScore)
                    {
                        bestBossScore = bossScore;
                        bestBoss = bm;
                    }
                }

                if (bestBoss != null)
                    return bestBoss;

                var locked = AnySnapFindAliveMonsterByAcdId(monsters, _asLockedAcdId);

                IMonster bestLeader = null;
                IMonster bestMinion = null;
                IMonster bestTrash = null;

                float bestLeaderScore = float.MaxValue;
                float bestMinionScore = float.MaxValue;
                float bestTrashScore = float.MaxValue;
                float bestTrashDensityScore = float.NegativeInfinity;

                foreach (var m in monsters)
                {
                    if (m == null || !m.IsAlive || m.Rarity == ActorRarity.Boss) continue;
                    if (!m.IsOnScreen) continue;
                    if (AnySnapShouldIgnoreMonster(m)) continue;

                    float distMe;
                    try
                    {
                        distMe = (float)m.CentralXyDistanceToMe;
                        if (distMe > scanMax) continue;
                    }
                    catch { continue; }

                    float anchorDist = AnySnapAnchorDistance(anchorWorld, m);
                    if (range > 0f && anchorDist > range) continue;

                    bool isJuggernaut = AnySnapIsJuggernautPack(m);
                    bool isInvulnerable = AnySnapIsInvulnerable(m);

                    if (AnySnapIsLeader(m))
                    {
                        if ((ignoreJuggernauts && isJuggernaut) || AnySnapIsShieldingActive(m) || isInvulnerable)
                            continue;

                        float score = meleeMode ? AnySnapCloseBucketScore(distMe) : anchorDist;

                        if (score < bestLeaderScore)
                        {
                            bestLeaderScore = score;
                            bestLeader = m;
                        }

                        continue;
                    }

                    if (isInvulnerable || (ignoreJuggernauts && isJuggernaut))
                        continue;

                    if (AnySnapIsEliteMinionLike(m))
                    {
                        if (ignoreMinions) continue;

                        float score = meleeMode ? AnySnapCloseBucketScore(distMe) : anchorDist;

                        if (score < bestMinionScore)
                        {
                            bestMinionScore = score;
                            bestMinion = m;
                        }

                        continue;
                    }

                    float trashScore = meleeMode ? AnySnapTrashBucketScore(distMe) : anchorDist;
                    float densityScore = AnySnapGetTrashDensityProgressionScore(monsters, m, scanMax);

                    bool betterTrash =
                        densityScore > bestTrashDensityScore + 0.05f ||
                        (Math.Abs(densityScore - bestTrashDensityScore) <= 0.05f && trashScore < bestTrashScore - 0.05f) ||
                        (Math.Abs(densityScore - bestTrashDensityScore) <= 0.05f &&
                         Math.Abs(trashScore - bestTrashScore) <= 0.05f &&
                         AnySnapGetTrashSelfProgression(m) > AnySnapGetTrashSelfProgression(bestTrash) + 0.01f);

                    if (betterTrash)
                    {
                        bestTrashDensityScore = densityScore;
                        bestTrashScore = trashScore;
                        bestTrash = m;
                    }
                }

                if (AnySnapIsValidLocked(locked, anchorWorld, range, meleeMode, true))
                {
                    if (AnySnapIsLeader(locked) && bestLeader != null)
                        return locked;

                    if (!AnySnapIsLeader(locked) &&
                        AnySnapIsEliteMinionLike(locked) &&
                        !ignoreMinions &&
                        bestLeader == null &&
                        bestMinion != null)
                        return locked;

                    if (!AnySnapIsLeader(locked) &&
                        !AnySnapIsEliteMinionLike(locked) &&
                        bestLeader == null &&
                        (ignoreMinions || bestMinion == null) &&
                        bestTrash != null)
                        return locked;
                }

                if (bestLeader != null) return bestLeader;
                if (!ignoreMinions && bestMinion != null) return bestMinion;
                return bestTrash;
            }
            catch
            {
                return null;
            }
        }


        private bool AnySnapIsValidTarget(IMonster m)
        {
            if (m == null || !m.IsAlive) return false;
            try { if (!m.IsOnScreen) return false; } catch { return false; }
            if (m.FloorCoordinate == null) return false;
            if (AnySnapShouldIgnoreMonster(m) || AnySnapIsInvulnerable(m)) return false;
            return true;
        }

        private bool AnySnapIsValidLocked(IMonster m, IWorldCoordinate anchorWorld, float range, bool meleeMode, bool requireOnScreen)
        {
            if (m == null || !m.IsAlive) return false;
            if (requireOnScreen && !m.IsOnScreen) return false;
            if (AnySnapShouldIgnoreMonster(m) || AnySnapIsInvulnerable(m)) return false;
            if (_autoSnapIgnoreJuggernauts && AnySnapIsJuggernautPack(m)) return false;
            if (_autoSnapIgnoreMinions && AnySnapIsEliteMinionLike(m)) return false;
            if (AnySnapIsLeader(m) && AnySnapIsShieldingActive(m)) return false;
            if (range > 0f && AnySnapAnchorDistance(anchorWorld, m) > range) return false;
            return true;
        }

        private IWorldCoordinate AutoSnapGetWorld(float screenX, float screenY)
        {
            try
            {
                return Hud.Window.CreateScreenCoordinate(screenX, screenY).ToWorldCoordinate();
            }
            catch { return null; }
        }

        private static float AnySnapAnchorDistance(IWorldCoordinate anchorWorld, IMonster m)
        {
            try
            {
                if (anchorWorld != null && m != null && m.FloorCoordinate != null)
                    return (float)m.FloorCoordinate.XYDistanceTo(anchorWorld);
            }
            catch { }
            try { return (float)m.CentralXyDistanceToMe; } catch { return float.MaxValue; }
        }

        private static float AnySnapCloseBucketScore(float distance)
        {
            const float edgeStart = 15.8f;
            const float edgePenaltyMax = 1.15f;
            const float pick = 17.0f;
            if (distance <= edgeStart) return distance;
            float denom = Math.Max(0.1f, pick - edgeStart);
            float t = (distance - edgeStart) / denom;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return distance + (t * edgePenaltyMax);
        }

        private static float AnySnapTrashBucketScore(float distance)
        {
            const float edgeStart = 15.8f;
            const float pick = 17.0f;
            float score = AnySnapCloseBucketScore(distance);
            if (distance <= edgeStart) return score;
            float denom = Math.Max(0.1f, pick - edgeStart);
            float t = (distance - edgeStart) / denom;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return score + (t * 0.45f);
        }


        private static float AnySnapGetTrashSelfProgression(IMonster m)
        {
            if (m == null) return 0f;

            try
            {
                var sno = m.SnoMonster;
                double prog = sno != null ? sno.RiftProgression : 0d;
                return (float)Math.Max(0.10d, prog);
            }
            catch
            {
                return 0.10f;
            }
        }

        private static float AnySnapGetTrashDensityProgressionScore(IEnumerable<IMonster> monsters, IMonster center, float scanMax)
        {
            if (monsters == null || center == null || !center.IsAlive)
                return 0f;

            const float clusterRadius = 12f;
            float score = 0f;

            foreach (var mt in monsters)
            {
                if (mt == null || !mt.IsAlive || mt.Rarity == ActorRarity.Boss) continue;
                if (!mt.IsOnScreen) continue;
                if (AnySnapIsIllusionOrClone(mt) || AnySnapIsOrlashCloneOrBreathMinion(mt) || AnySnapIsInvulnerable(mt)) continue;
                if (AnySnapIsLeader(mt) || AnySnapIsEliteMinionLike(mt)) continue;

                try
                {
                    if ((float)mt.CentralXyDistanceToMe > scanMax) continue;
                    if (mt.FloorCoordinate == null || center.FloorCoordinate == null) continue;
                    if ((float)mt.FloorCoordinate.XYDistanceTo(center.FloorCoordinate) > clusterRadius) continue;
                }
                catch { continue; }

                score += AnySnapGetTrashSelfProgression(mt);
            }

            return score;
        }

        // User-provided red no-click mask reference resolution.
        private const float AutoSnapMaskReferenceWidth = 1920f;
        private const float AutoSnapMaskReferenceHeight = 1080f;

        // Explicit no-click regions from the user's red mask screenshot.
        // Coordinates are 1920x1080 reference pixels and are scaled at runtime.
        // Do not add the minimap here; the user specifically wants minimap targeting allowed.
        private static readonly RectangleF[] AutoSnapNoClickMaskRects1920x1080 = new RectangleF[]
        {
            // Local portrait face fallback. Dynamic player.PortraitUiElement is primary,
            // but this protects the same area if portrait UI data is unavailable.
            new RectangleF(116f, 11f, 76f, 71f),

            // Follower/portrait-side clickable region shown in the user's red mask.
            new RectangleF(34f, 57f, 58f, 61f),

            // Top center clickable HUD strip from the red mask.
            new RectangleF(871f, 2f, 179f, 21f),

            // Top-right clickable HUD strip from the red mask.
            new RectangleF(1644f, 23f, 60f, 26f),

            // Small top-right clickable UI element from the red mask.
            new RectangleF(1816f, 120f, 25f, 15f),

            // Objective/side clickable UI element from the red mask.
            new RectangleF(1863f, 363f, 31f, 29f),

            // Bottom-left menu/chat button region from the red mask.
            new RectangleF(8f, 973f, 85f, 80f),

            // Bottom-center skill/resource/action UI from the red mask.
            // This intentionally does NOT block the entire screen width.
            new RectangleF(315f, 893f, 1289f, 187f),

            // Bottom-right inventory/menu/action UI from the red mask.
            new RectangleF(1754f, 961f, 157f, 83f),
        };

        private bool AnySnapIsInsideExplicitNoClickMask(float x, float y)
        {
            try
            {
                if (!AutoSnapAvoidUiRegions || !AutoSnapUseExplicitNoClickMask)
                    return false;

                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float screenW = Hud.Window.Size.Width;
                float screenH = Hud.Window.Size.Height;

                float sx = screenW / AutoSnapMaskReferenceWidth;
                float sy = screenH / AutoSnapMaskReferenceHeight;

                float pad = AutoSnapNoClickMaskPaddingPx;

                for (int i = 0; i < AutoSnapNoClickMaskRects1920x1080.Length; i++)
                {
                    RectangleF src = AutoSnapNoClickMaskRects1920x1080[i];

                    RectangleF r = new RectangleF(
                        (src.Left * sx) - pad,
                        (src.Top * sy) - pad,
                        (src.Width * sx) + (pad * 2f),
                        (src.Height * sy) + (pad * 2f));

                    if (r.Width <= 0f || r.Height <= 0f)
                        continue;

                    if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                        return true;
                }

                return false;
            }
            catch
            {
                // Fail open so AutoSnap does not break if mask scaling fails.
                return false;
            }
        }

        private bool AnySnapIsInsidePlayerPortraitFace(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Players == null)
                    return false;

                foreach (var player in Hud.Game.Players)
                {
                    try
                    {
                        if (player == null)
                            continue;

                        if (!player.IsInGame)
                            continue;

                        var ui = player.PortraitUiElement;
                        if (ui == null)
                            continue;

                        if (!ui.Visible)
                            continue;

                        RectangleF r = ui.Rectangle;

                        if (r.Width <= 0f || r.Height <= 0f)
                            continue;

                        float pad = AutoSnapPortraitFacePaddingPx;

                        if (pad != 0f)
                        {
                            r = new RectangleF(
                                r.Left - pad,
                                r.Top - pad,
                                r.Width + (pad * 2f),
                                r.Height + (pad * 2f));
                        }

                        if (r.Width <= 0f || r.Height <= 0f)
                            continue;

                        if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                            return true;
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                // Fail open so AutoSnap does not break if portrait UI data is unavailable.
                return false;
            }
        }

        private bool AnySnapIsSafeScreenTarget(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;

                if (x < 0f || y < 0f || x > w || y > h)
                    return false;

                if (!AutoSnapAvoidUiRegions)
                    return true;

                // Player/party portrait face icons only.
                // This does not block the whole portrait frame.
                if (AutoSnapAvoidPlayerPortraitFaces && AnySnapIsInsidePlayerPortraitFace(x, y))
                    return false;

                // User-provided explicit red mask regions.
                // This intentionally does not include the minimap.
                if (AnySnapIsInsideExplicitNoClickMask(x, y))
                    return false;

                // Optional old broad bottom-band fallback.
                // This is OFF by default because it blocks too much usable screen area.
                if (AutoSnapUseLegacyBottomBand)
                {
                    float unsafeBottomPx = h * AutoSnapUnsafeBottomFrac;

                    if (unsafeBottomPx < AutoSnapUnsafeBottomMinPx)
                        unsafeBottomPx = AutoSnapUnsafeBottomMinPx;

                    if (unsafeBottomPx > AutoSnapUnsafeBottomMaxPx)
                        unsafeBottomPx = AutoSnapUnsafeBottomMaxPx;

                    float unsafeTopY = h - unsafeBottomPx;

                    if (y >= unsafeTopY)
                        return false;
                }

                return true;
            }
            catch
            {
                // Fail open on unexpected UI exceptions so AutoSnap does not become unusable.
                return true;
            }
        }

        private bool AnySnapGetScreen(IMonster m, out float x, out float y)
        {
            x = y = 0f;
            try
            {
                var sc = m.FloorCoordinate.ToScreenCoordinate();
                x = sc.X; y = sc.Y;
                return ZbInWindow(x, y) && AnySnapIsSafeScreenTarget(x, y);
            }
            catch { return false; }
        }

        private static bool AnySnapIsLeader(IMonster m)
        {
            return m != null && (m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Boss);
        }

        private static bool AnySnapIsRareMinion(IMonster m)
        {
            if (m == null) return false;
            if (m.Rarity == ActorRarity.RareMinion) return true;
            var s = m.Rarity.ToString();
            return s.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0 && m.Rarity != ActorRarity.Boss;
        }

        private static bool AnySnapIsEliteMinionLike(IMonster m)
        {
            if (m == null || AnySnapIsLeader(m)) return false;
            if (AnySnapIsRareMinion(m)) return true;
            bool b;
            return ZbTryGetBool(m, "IsElite", out b) && b;
        }

        private static bool AnySnapIsJuggernautPack(IMonster m)
        {
            return ZbHasAffixName(m, "Juggernaut");
        }

        private static bool AnySnapIsShieldingActive(IMonster m)
        {
            bool b;
            if (ZbTryGetBool(m, "IsShielding", out b) && b) return true;
            if (ZbTryGetBool(m, "Shielding", out b) && b) return true;
            if (ZbTryGetBool(m, "ShieldingActive", out b) && b) return true;
            if (ZbTryGetBool(m, "HasShielding", out b) && b) return true;
            if (ZbTryGetBool(m, "IsShielded", out b) && b) return true;
            if (ZbTryGetBool(m, "Shielded", out b) && b) return true;
            return false;
        }

        private static bool AnySnapIsInvulnerable(IMonster m)
        {
            bool b;
            if (ZbTryGetBool(m, "Invulnerable", out b) && b) return true;
            if (ZbTryGetBool(m, "IsInvulnerable", out b) && b) return true;
            if (ZbTryGetBool(m, "Untargetable", out b) && b) return true;
            if (ZbTryGetBool(m, "IsUntargetable", out b) && b) return true;
            return false;
        }

        private bool AnySnapShouldIgnoreMonster(IMonster m)
        {
            if (m == null)
                return true;

            if (AnySnapIsIllusionOrClone(m))
                return true;

            if (AutoSnapIgnoreOrlashClones && AnySnapIsOrlashCloneOrBreathMinion(m))
                return true;

            return false;
        }

        private static bool AnySnapIsOrlashCloneOrBreathMinion(IMonster m)
        {
            if (m == null)
                return false;

            // Exact actor SNO checks first. These avoid suppressing the real Orlash boss:
            // ActorSnoEnum._x1_lr_boss_terrordemon_a
            if (AnySnapHasActorSno(m, ActorSnoEnum._x1_lr_boss_terrordemon_a_breathminion))
                return true;

            if (AnySnapHasActorSno(m, ActorSnoEnum._x1_lr_boss_minion_terrordemon_clone_c))
                return true;

            string code = AnySnapGetMonsterCode(m);
            string name = AnySnapGetMonsterName(m);

            bool isOrlashFamily =
                code.IndexOf("X1_LR_Boss_TerrorDemon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("X1_LR_Boss_TerrorDemon", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("OrlashBoss", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("OrlashBoss", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("LR_Boss_TerrorDemon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("LR_Boss_TerrorDemon", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isOrlashFamily)
                return false;

            bool isCloneOrHelper =
                code.IndexOf("BreathMinion", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("BreathMinion", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("Clone", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Clone", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("_Minion_", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_Minion_", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("Boss_Minion", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Boss_Minion", StringComparison.OrdinalIgnoreCase) >= 0;

            return isCloneOrHelper;
        }

        private static bool AnySnapHasActorSno(IMonster m, ActorSnoEnum actorSno)
        {
            if (m == null)
                return false;

            try
            {
                if (m.SnoActor != null && m.SnoActor.Sno == actorSno)
                    return true;
            }
            catch { }

            try
            {
                if (m.SnoMonster != null &&
                    m.SnoMonster.SnoActor != null &&
                    m.SnoMonster.SnoActor.Sno == actorSno)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static string AnySnapGetMonsterCode(IMonster m)
        {
            try
            {
                if (m != null && m.SnoMonster != null && !string.IsNullOrWhiteSpace(m.SnoMonster.Code))
                    return m.SnoMonster.Code;
            }
            catch { }

            try
            {
                if (m != null && m.SnoActor != null && !string.IsNullOrWhiteSpace(m.SnoActor.Code))
                    return m.SnoActor.Code;
            }
            catch { }

            try
            {
                return m != null ? (m.ToString() ?? string.Empty) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string AnySnapGetMonsterName(IMonster m)
        {
            try
            {
                if (m != null && m.SnoMonster != null && !string.IsNullOrWhiteSpace(m.SnoMonster.NameEnglish))
                    return m.SnoMonster.NameEnglish;
            }
            catch { }

            try
            {
                if (m != null && m.SnoActor != null && !string.IsNullOrWhiteSpace(m.SnoActor.NameEnglish))
                    return m.SnoActor.NameEnglish;
            }
            catch { }

            return string.Empty;
        }

        private static bool AnySnapIsIllusionOrClone(IMonster m)
        {
            if (m == null) return false;

            try { if (m.Illusion) return true; } catch { }

            bool b;
            if (ZbTryGetBool(m, "IsIllusion", out b) && b) return true;
            if (ZbTryGetBool(m, "Illusion", out b) && b) return true;
            if (ZbTryGetBool(m, "IsClone", out b) && b) return true;
            if (ZbTryGetBool(m, "Clone", out b) && b) return true;

            if (AnySnapIsOrlashCloneOrBreathMinion(m))
                return true;

            return false;
        }

        private static int AnySnapGetAcdId(IMonster m)
        {
            if (m == null) return 0;
            try
            {
                var pi = m.GetType().GetProperty("AcdId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null)
                {
                    object v = pi.GetValue(m, null);
                    if (v is int) return (int)v;
                    if (v != null) return Convert.ToInt32(v, CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return 0;
        }

        private static IMonster AnySnapFindAliveMonsterByAcdId(IEnumerable<IMonster> monsters, int acdId)
        {
            if (monsters == null || acdId == 0) return null;
            foreach (var m in monsters)
            {
                if (m == null || !m.IsAlive) continue;
                try { if (AnySnapGetAcdId(m) == acdId) return m; } catch { }
            }
            return null;
        }

        private static bool ZbTryGetBool(object obj, string propName, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrEmpty(propName)) return false;
            try
            {
                var t = obj.GetType();
                var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    value = (bool)p.GetValue(obj, null);
                    return true;
                }
                var f = t.GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(bool))
                {
                    value = (bool)f.GetValue(obj);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool ZbHasAffixName(IMonster m, string token)
        {
            if (m == null || string.IsNullOrEmpty(token)) return false;
            try
            {
                var affixProp = m.GetType().GetProperty("AffixSnoList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object affixValue = affixProp == null ? null : affixProp.GetValue(m, null);
                string s = Convert.ToString(affixValue, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s) && s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            try
            {
                string s = m.ToString();
                if (!string.IsNullOrEmpty(s) && s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        private bool ZbInWindow(float x, float y)
        {
            try
            {
                return x >= 0 && y >= 0 && x <= Hud.Window.Size.Width && y <= Hud.Window.Size.Height;
            }
            catch { return false; }
        }


        private bool IsAssignedHotkeyDown(HotkeyInputType type, Key dxKey, Keys winKey)
        {
            if (type == HotkeyInputType.Keyboard)
                return ZbIsKeyDown(dxKey, winKey);

            int vk = HotkeyInputVirtualKey(type);
            if (vk == 0) return false;
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; } catch { return false; }
        }

        private int HotkeyInputVirtualKey(HotkeyInputType type)
        {
            switch (type)
            {
                case HotkeyInputType.MouseLeft: return 0x01;
                case HotkeyInputType.MouseRight: return 0x02;
                case HotkeyInputType.MouseMiddle: return 0x04;
                case HotkeyInputType.MouseX1: return 0x05;
                case HotkeyInputType.MouseX2: return 0x06;
                default: return 0;
            }
        }

        private string HotkeyInputLabel(HotkeyInputType type)
        {
            switch (type)
            {
                case HotkeyInputType.MouseLeft: return "LMB";
                case HotkeyInputType.MouseRight: return "RMB";
                case HotkeyInputType.MouseMiddle: return "MMB";
                case HotkeyInputType.MouseX1: return "Mouse4";
                case HotkeyInputType.MouseX2: return "Mouse5";
                default: return "Space";
            }
        }

        private bool ZbIsKeyDown(Key dxKey, Keys winKey)
        {
            try
            {
                if (dxKey != Key.Unknown && Hud.Input != null)
                {
                    var t = Hud.Input.GetType();
                    var md = t.GetMethod("IsKeyDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Key) }, null);
                    if (md != null && md.ReturnType == typeof(bool)) return (bool)md.Invoke(Hud.Input, new object[] { dxKey });
                }
                if (winKey != Keys.None && Hud.Input != null)
                    return Hud.Input.IsKeyDown(winKey);
            }
            catch { }
            return false;
        }

        private Keys KeyToWinKeys(Key k)
        {
            switch (k)
            {
                case Key.D1: return Keys.D1;
                case Key.D2: return Keys.D2;
                case Key.D3: return Keys.D3;
                case Key.D4: return Keys.D4;
                case Key.D5: return Keys.D5;
                case Key.D6: return Keys.D6;
                case Key.D7: return Keys.D7;
                case Key.D8: return Keys.D8;
                case Key.D9: return Keys.D9;
                case Key.D0: return Keys.D0;
                case Key.NumberPad1: return Keys.NumPad1;
                case Key.NumberPad2: return Keys.NumPad2;
                case Key.NumberPad3: return Keys.NumPad3;
                case Key.NumberPad4: return Keys.NumPad4;
                case Key.NumberPad5: return Keys.NumPad5;
                case Key.NumberPad6: return Keys.NumPad6;
                case Key.NumberPad7: return Keys.NumPad7;
                case Key.NumberPad8: return Keys.NumPad8;
                case Key.NumberPad9: return Keys.NumPad9;
                case Key.NumberPad0: return Keys.NumPad0;
                case Key.NumberPadEnter: return Keys.Return;
                case Key.Space: return Keys.Space;
                case Key.Tab: return Keys.Tab;
                case Key.Return: return Keys.Return;
                case Key.Back: return Keys.Back;
                case Key.Insert: return Keys.Insert;
                case Key.Delete: return Keys.Delete;
                case Key.Home: return Keys.Home;
                case Key.End: return Keys.End;
                case Key.Up: return Keys.Up;
                case Key.Down: return Keys.Down;
                case Key.Left: return Keys.Left;
                case Key.Right: return Keys.Right;
                case Key.Minus: return Keys.OemMinus;
                case Key.Equals: return Keys.Oemplus;
                case Key.LeftBracket: return Keys.OemOpenBrackets;
                case Key.RightBracket: return Keys.OemCloseBrackets;
                case Key.Semicolon: return Keys.OemSemicolon;
                case Key.Apostrophe: return Keys.OemQuotes;
                case Key.Grave: return Keys.Oemtilde;
                case Key.Backslash: return Keys.OemPipe;
                case Key.Comma: return Keys.Oemcomma;
                case Key.Period: return Keys.OemPeriod;
                case Key.Slash: return Keys.OemQuestion;
                case Key.LeftShift:
                case Key.RightShift: return Keys.ShiftKey;
                case Key.LeftControl:
                case Key.RightControl: return Keys.ControlKey;
                case Key.LeftAlt:
                case Key.RightAlt: return Keys.Menu;
                case Key.Multiply: return Keys.Multiply;
                case Key.Divide: return Keys.Divide;
                case Key.Subtract: return Keys.Subtract;
                case Key.Add: return Keys.Add;
                case Key.Decimal: return Keys.Decimal;
                case Key.F1: return Keys.F1;
                case Key.F2: return Keys.F2;
                case Key.F3: return Keys.F3;
                case Key.F4: return Keys.F4;
                case Key.F5: return Keys.F5;
                case Key.F6: return Keys.F6;
                case Key.F7: return Keys.F7;
                case Key.F8: return Keys.F8;
                case Key.F9: return Keys.F9;
                case Key.F10: return Keys.F10;
                case Key.F11: return Keys.F11;
                case Key.F12: return Keys.F12;
                case Key.A: return Keys.A;
                case Key.B: return Keys.B;
                case Key.C: return Keys.C;
                case Key.D: return Keys.D;
                case Key.E: return Keys.E;
                case Key.F: return Keys.F;
                case Key.G: return Keys.G;
                case Key.H: return Keys.H;
                case Key.I: return Keys.I;
                case Key.J: return Keys.J;
                case Key.K: return Keys.K;
                case Key.L: return Keys.L;
                case Key.M: return Keys.M;
                case Key.N: return Keys.N;
                case Key.O: return Keys.O;
                case Key.P: return Keys.P;
                case Key.Q: return Keys.Q;
                case Key.R: return Keys.R;
                case Key.S: return Keys.S;
                case Key.T: return Keys.T;
                case Key.U: return Keys.U;
                case Key.V: return Keys.V;
                case Key.W: return Keys.W;
                case Key.X: return Keys.X;
                case Key.Y: return Keys.Y;
                case Key.Z: return Keys.Z;
                default: return Keys.None;
            }
        }

        private string CaptureKeyLabel(Key k)
        {
            if (k == Key.Unknown) return "[None]";
            if (k == Key.LeftBracket) return "[";
            if (k == Key.RightBracket) return "]";

            string s = k.ToString();

            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]))
                return s.Substring(1);

            s = CompactKeyName(s);

            return "[" + s + "]";
        }

        private static string CompactKeyName(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            if (string.Equals(s, "NumberPadAdd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NumPadAdd", StringComparison.OrdinalIgnoreCase)) return "Num+";
            if (string.Equals(s, "NumberPadSubtract", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NumPadSubtract", StringComparison.OrdinalIgnoreCase)) return "Num-";
            if (string.Equals(s, "NumberPadMultiply", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NumPadMultiply", StringComparison.OrdinalIgnoreCase)) return "Num*";
            if (string.Equals(s, "NumberPadDivide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NumPadDivide", StringComparison.OrdinalIgnoreCase)) return "Num/";
            if (string.Equals(s, "NumberPadDecimal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NumPadDecimal", StringComparison.OrdinalIgnoreCase)) return "Num.";

            if (s.StartsWith("NumberPad", StringComparison.OrdinalIgnoreCase))
                s = "NumPad" + s.Substring("NumberPad".Length);

            s = s.Replace("LeftControl", "LCtrl");
            s = s.Replace("RightControl", "RCtrl");
            s = s.Replace("LeftShift", "LShift");
            s = s.Replace("RightShift", "RShift");
            s = s.Replace("LeftAlt", "LAlt");
            s = s.Replace("RightAlt", "RAlt");
            s = s.Replace("Escape", "Esc");
            s = s.Replace("Return", "Enter");
            s = s.Replace("Capital", "Caps");
            s = s.Replace("PageUp", "PgUp");
            s = s.Replace("PageDown", "PgDn");

            return s;
        }

        private void SaveSettings()
        {
            try
            {
                string path = SettingsPath();
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var lines = new List<string>();
                lines.Add("# s7o HUD Manager settings");
                lines.Add("SETTINGS_VERSION=" + SettingsVersion.ToString(CultureInfo.InvariantCulture));
                lines.Add("VISIBLE=False"); // start closed after HUD restart
                lines.Add("SHOW_DOT=" + _showDot);
                lines.Add("EDIT_MODE=" + _editMode);
                lines.Add("NOCLICK_BACKGROUND=" + _noClickBackground);
                lines.Add("MENU_HOTKEY=" + MenuHotkey);
                lines.Add("DEBUG_LOGGING=" + _debugLogging);
                lines.Add("PAGE=" + _activePage);
                lines.Add("TAB=" + _activeTab);
                lines.Add("WINDOW=" + RectToString(_windowRect));
                lines.Add("DOT=" + RectToString(_dotRect));
                lines.Add("VIS_MENU_BUTTON=" +
                    _showDot.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMenuButtonExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonClosedSize.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonClosedColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonClosedTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonOpenSize.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonOpenColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _menuButtonOpenTone.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOGEM_EXPANDED=" + _autoGemExpanded);
                lines.Add("AUTOGEM_SPEC_EXPANDED=" + _autoGemSpecificExpanded);
                lines.Add("AUTOGEM_SPEC_SCROLL=" + _autoGemSpecificScroll.ToString(CultureInfo.InvariantCulture));
                lines.Add("HOTKEYS_EXPANDED=" + _hotkeysExpanded);
                foreach (var fav in _macroFavorites)
                    lines.Add("MACRO_FAVORITE=" + fav);
                foreach (var fav in _visualFavorites)
                    lines.Add("VISUAL_FAVORITE=" + fav);
                // VISUAL overlay settings
                lines.Add("VIS_CLICK=" +
                    _visClickAnimEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visClickAnimExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visClickAnimSize.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visClickAnimColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visClickAnimTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visClickAnimThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_PLAYER=" +
                    _visPlayerCircleEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visPlayerCircleExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visPlayerCircleYards.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visPlayerCircleColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visPlayerCircleTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visPlayerCircleThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_MOUSE=" +
                    _visMouseCircleEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMouseCircleExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMouseCircleYards.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMouseCircleColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMouseCircleTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMouseCircleThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_LINE=" +
                    _visLineToTargetEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visLineToTargetExpanded.ToString(CultureInfo.InvariantCulture) + "|0|" +
                    _visLineToTargetColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visLineToTargetTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visLineToTargetThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_RETICLE=" +
                    _visTargetReticleEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visTargetReticleExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visTargetReticleSize.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visTargetReticleColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visTargetReticleTone.ToString(CultureInfo.InvariantCulture) + "|0");

                lines.Add("VIS_OUTLINE=" +
                    _visReticleOutlineEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visReticleOutlineExpanded.ToString(CultureInfo.InvariantCulture) + "|0|" +
                    _visReticleOutlineColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visReticleOutlineTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visReticleOutlineThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_MINION=" +
                    _visMinionCirclesEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMinionCirclesExpanded.ToString(CultureInfo.InvariantCulture) + "|0|" +
                    _visMinionColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMinionTone.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visMinionThickness.ToString(CultureInfo.InvariantCulture));

                lines.Add("VIS_SIPHON=" +
                    _visSiphonEnabled.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visSiphonExpanded.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visSiphonDotSize.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visSiphonColorIdx.ToString(CultureInfo.InvariantCulture) + "|" +
                    _visSiphonTone.ToString(CultureInfo.InvariantCulture) + "|0");

                lines.Add("VIS_DANGEROUS_AFFIX_VISUALS=" + _visDangerousAffixVisualsEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("VIS_DANGEROUS_AFFIX_VISUALS_EXPANDED=" + _visDangerousAffixVisualsExpanded.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_PLAGUED=" + _affixPlagued.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_ARCANE_SPAWN=" + _affixArcaneSpawn.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_MOLTEN_EXPLOSION=" + _affixMoltenExplosion.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_DESECRATOR=" + _affixDesecrator.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_THUNDERSTORM=" + _affixThunderstorm.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_FROZEN=" + _affixFrozen.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_FROZEN_PULSE=" + _affixFrozenPulse.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_GHOM=" + _affixGhomGas.ToString(CultureInfo.InvariantCulture));
                lines.Add("EXPLOSIVE_GROTESQUE=" + _explosiveGrotesque.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_ORBITER=" + _affixOrbiter.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_WALLER=" + _affixWaller.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_WORMHOLE_MINES=" + _affixWormholeMines.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_FALLING_ROCKS=" + _affixBossFallingRocks.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_LEAP_TELEGRAPHS=" + _affixBossLeapTelegraphs.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_METEORS=" + _affixBossMeteors.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_KULLE_HAZARDS=" + _affixBossKulleHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_BLIGHTER_HAZARDS=" + _affixBossBlighterHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_RATKING_HAZARDS=" + _affixBossRatKingHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_ADRIA_GEYSERS=" + _affixBossAdriaGeysers.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_BUTCHER_FIRE=" + _affixBossButcherFire.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_RIME_HAZARDS=" + _affixBossRimeHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_SHOCK_TOWER_HAZARDS=" + _affixShockTowerHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("AFFIX_BOSS_SANDMONSTER_TURRET_HAZARDS=" + _affixBossSandmonsterTurretHazards.ToString(CultureInfo.InvariantCulture));
                lines.Add("ELITE_AFFIX_LABELS=" + _eliteAffixLabelsEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("DANGEROUS_MONSTER_LABELS=" + _dangerousMonsterLabelsEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("VIS_PARTY_INSPECTOR_EXPANDED=" + _visPartyInspectorExpanded.ToString(CultureInfo.InvariantCulture));
                lines.Add("PARTY_INSPECTOR_HOTKEY=" + _partyInspectorHotkey.ToString());

                lines.Add("OPEN_GR_MAP_OVERRIDE=" + _riftMapSelectionHasOverride.ToString(CultureInfo.InvariantCulture));
                lines.Add("OPEN_GR_MAP_IDS=" + string.Join("|", _riftEnabledMapIds.OrderBy(v => v).Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray()));


                lines.Add("TOGGLE_CATEGORY=" + _activeToggleCategory);
                lines.Add("TOGGLE_DETAIL_SCROLL=" + _toggleDetailScroll.ToString(CultureInfo.InvariantCulture));
                lines.Add("GLOBAL_TTS_ENABLED=" + _globalTtsEnabled);
                lines.Add("GLOBAL_TTS_VOLUME=" + _globalTtsVolume.ToString(CultureInfo.InvariantCulture));
                lines.Add("ZBARB_AUTOSNAP_HOTKEY=" + _zbarbAutoSnapHotkey);
                TrimTtsCustomMessages();
                lines.Add("TTS_BROADCAST_EXPANDED=" + _ttsBroadcastExpanded);
                lines.Add("TTS_CUSTOM_DEFAULTS_SEEDED=" + _ttsCustomDefaultsSeeded.ToString(CultureInfo.InvariantCulture));
                lines.Add("TTS_CUSTOM_DEFAULTS_SEED_VERSION=" + _ttsCustomDefaultsSeedVersion.ToString(CultureInfo.InvariantCulture));
                lines.Add("TTS_CUSTOM_COUNT=" + _ttsCustomMessages.Count.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _ttsCustomMessages.Count; i++)
                {
                    var msg = _ttsCustomMessages[i];
                    if (msg == null)
                        continue;

                    lines.Add("TTS_CUSTOM_" + i.ToString(CultureInfo.InvariantCulture) + "_HOTKEY=" + msg.Hotkey);
                    lines.Add("TTS_CUSTOM_" + i.ToString(CultureInfo.InvariantCulture) + "_TEXT=" + EncodeSettingText(msg.Text));
                }

                lines.Add("AUTOSKILL_KEYBINDS_EXPANDED=" + _autoSkillKeybindsExpanded.ToString(CultureInfo.InvariantCulture));

                // AutoSnap
                lines.Add("AUTOSNAP_EXPANDED=" + _autoSnapExpanded.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_MODE=" + Math.Max(0, Math.Min(1, _autoSnapMode)).ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_MELEE_RANGE=" + Math.Max(0, Math.Min(80, _autoSnapMeleeRangeYards)).ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_RANGED_RANGE=" + Math.Max(0, Math.Min(80, _autoSnapRangedRangeYards)).ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_LEFT_FORCE_STANDSTILL=" + _autoSnapLeftClickForceStandStill.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_RESTORE_CURSOR=" + _autoSnapRestoreCursor.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_IGNORE_JUGGERNAUTS=" + _autoSnapIgnoreJuggernauts.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_IGNORE_MINIONS=" + _autoSnapIgnoreMinions.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_HOTKEYS_ENABLED=" + _asHotkeysEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_HOTKEYS_ONLY=" + _asHotkeysOnly.ToString(CultureInfo.InvariantCulture));
                lines.Add("AUTOSNAP_HOTKEYS_EXPANDED=" + _autoSnapHotkeysExpanded.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _asEnabled.Length; i++)
                    lines.Add("AUTOSNAP_SLOT_" + i.ToString(CultureInfo.InvariantCulture) + "=" + _asEnabled[i].ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _asHotkeyKeys.Length; i++)
                    lines.Add("AUTOSNAP_HOTKEY_" + i.ToString(CultureInfo.InvariantCulture) + "=" + _asHotkeyKeys[i].ToString());

                foreach (var fav in _favorites)
                {
                    if (fav == null || string.IsNullOrWhiteSpace(fav.TypeName)) continue;
                    lines.Add("FAVORITE=" + fav.TypeName + "|" + (fav.DisplayName ?? string.Empty).Replace("|", "/"));
                }
                foreach (var kv in _pluginEnabledOverrides.OrderBy(k => k.Key))
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        lines.Add("PLUGIN_ENABLED=" + kv.Key + "|" + kv.Value.ToString(CultureInfo.InvariantCulture));
                }

                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        private void LoadSettings()
        {
            _loadedSettingsVersion = 0;
            _favorites.Clear();
            _pluginEnabledOverrides.Clear();
            _ttsCustomMessages.Clear();
            SyncTtsCustomRuntimeLists();
            _ttsCustomMessageHotkeyCaptureIndex = -1;
            _ttsCustomMessageEditIndex = -1;

            string officialPath = SettingsPath();
            string readPath = SelectSettingsReadPath();

            try
            {
                if (!File.Exists(readPath))
                {
                    _activePage = NormalizeMenuPage(_activePage);
                    NormalizePluginsTabForEmptyFavorites();
                    SeedDefaultDisabledPluginOverrides();
                    SeedDefaultTtsCustomMessagesIfNeeded();
                    EnsureAtLeastOneTtsCustomMessageRowIfExpanded();
                    SyncTtsCustomRuntimeLists();
                    RequestPluginCacheRefresh();
                    SaveSettings();
                    return;
                }

                foreach (string raw in File.ReadAllLines(readPath))
                {
                    string line = raw == null ? string.Empty : raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "SETTINGS_VERSION")
                    {
                        int iv;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                            _loadedSettingsVersion = iv;
                    }
                    else if (key == "SHOW_DOT") _showDot = ParseBool(val, _showDot);
                    else if (key == "EDIT_MODE") _editMode = ParseBool(val, _editMode);
                    else if (key == "NOCLICK_BACKGROUND") _noClickBackground = ParseBool(val, _noClickBackground);
                    else if (key == "MENU_HOTKEY")
                    {
                        Key hk;
                        if (Enum.TryParse<Key>(val, true, out hk)) MenuHotkey = hk;
                    }
                    else if (key == "DEBUG_LOGGING") _debugLogging = ParseBool(val, _debugLogging);
                    else if (key == "PAGE")
                    {
                        _activePage = ParseMenuPageSetting(val, _loadedSettingsVersion);
                    }
                    else if (key == "TAB")
                    {
                        ManagerTab tab;
                        if (Enum.TryParse<ManagerTab>(val, true, out tab) &&
                            Enum.IsDefined(typeof(ManagerTab), tab))
                        {
                            _activeTab = tab;
                        }
                    }
                    else if (key == "WINDOW")
                    {
                        _windowRect = ParseRect(val, _windowRect);
                        // Upgrade window height if saved from older/smaller settings
                        if (_windowRect.Height < 750f)
                            _windowRect.Height = 770f;
                    }
                    else if (key == "DOT") _dotRect = ParseRect(val, _dotRect);
                    else if (key == "VIS_MENU_BUTTON") ParseMenuButtonVisualLine(val);
                    else if (key == "AUTOGEM_EXPANDED") _autoGemExpanded = ParseBool(val, _autoGemExpanded);
                    else if (key == "AUTOGEM_SPEC_EXPANDED") _autoGemSpecificExpanded = ParseBool(val, _autoGemSpecificExpanded);
                    else if (key == "AUTOGEM_SPEC_SCROLL")
                    {
                        int iv;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                            _autoGemSpecificScroll = Math.Max(0, iv);
                    }
                    else if (key == "HOTKEYS_EXPANDED") _hotkeysExpanded = ParseBool(val, _hotkeysExpanded);
                    else if (key == "MACRO_FAVORITE"  && !string.IsNullOrWhiteSpace(val)) _macroFavorites.Add(val);
                    else if (key == "VISUAL_FAVORITE" && !string.IsNullOrWhiteSpace(val)) _visualFavorites.Add(val);
                    else if (key == "VIS_CLICK")
                    {
                        ParseVisClickLine(val);
                    }
                    else if (key == "VIS_PLAYER")
                    {
                        ParseVisLine(
                            val,
                            out _visPlayerCircleEnabled,
                            out _visPlayerCircleExpanded,
                            ref _visPlayerCircleYards,
                            ref _visPlayerCircleColorIdx,
                            ref _visPlayerCircleTone,
                            ref _visPlayerCircleThickness);
                    }
                    else if (key == "VIS_MOUSE")
                    {
                        ParseVisLine(
                            val,
                            out _visMouseCircleEnabled,
                            out _visMouseCircleExpanded,
                            ref _visMouseCircleYards,
                            ref _visMouseCircleColorIdx,
                            ref _visMouseCircleTone,
                            ref _visMouseCircleThickness);
                    }
                    else if (key == "VIS_LINE")
                    {
                        float unused = 0f;

                        ParseVisLine(
                            val,
                            out _visLineToTargetEnabled,
                            out _visLineToTargetExpanded,
                            ref unused,
                            ref _visLineToTargetColorIdx,
                            ref _visLineToTargetTone,
                            ref _visLineToTargetThickness);
                    }
                    else if (key == "VIS_RETICLE")
                    {
                        float unused = 0f;

                        ParseVisLine(
                            val,
                            out _visTargetReticleEnabled,
                            out _visTargetReticleExpanded,
                            ref _visTargetReticleSize,
                            ref _visTargetReticleColorIdx,
                            ref _visTargetReticleTone,
                            ref unused);
                    }
                    else if (key == "VIS_OUTLINE")
                    {
                        float unused = 0f;

                        ParseVisLine(
                            val,
                            out _visReticleOutlineEnabled,
                            out _visReticleOutlineExpanded,
                            ref unused,
                            ref _visReticleOutlineColorIdx,
                            ref _visReticleOutlineTone,
                            ref _visReticleOutlineThickness);
                    }
                    else if (key == "VIS_MINION")
                    {
                        float unused = 0f;

                        ParseVisLine(
                            val,
                            out _visMinionCirclesEnabled,
                            out _visMinionCirclesExpanded,
                            ref unused,
                            ref _visMinionColorIdx,
                            ref _visMinionTone,
                            ref _visMinionThickness);
                    }
                    else if (key == "VIS_SIPHON")
                    {
                        float unused = 0f;

                        ParseVisLine(
                            val,
                            out _visSiphonEnabled,
                            out _visSiphonExpanded,
                            ref _visSiphonDotSize,
                            ref _visSiphonColorIdx,
                            ref _visSiphonTone,
                            ref unused);
                    }
                    else if (key == "VIS_DANGEROUS_AFFIX_VISUALS")
                    {
                        _visDangerousAffixVisualsEnabled = ParseBool(val, _visDangerousAffixVisualsEnabled);
                    }
                    else if (key == "VIS_DANGEROUS_AFFIX_VISUALS_EXPANDED")
                    {
                        _visDangerousAffixVisualsExpanded = ParseBool(val, _visDangerousAffixVisualsExpanded);
                    }
                    // Compatibility with abandoned intermediate revisions.
                    else if (key == "VIS_DANGER_ZONES")
                    {
                        _visDangerousAffixVisualsEnabled = ParseBool(val, _visDangerousAffixVisualsEnabled);
                    }
                    else if (key == "VIS_DANGER_ZONES_EXPANDED")
                    {
                        _visDangerousAffixVisualsExpanded = ParseBool(val, _visDangerousAffixVisualsExpanded);
                    }
                    else if (key == "AFFIX_PLAGUED")
                    {
                        _affixPlagued = ParseBool(val, _affixPlagued);
                    }
                    else if (key == "AFFIX_ARCANE_SPAWN")
                    {
                        _affixArcaneSpawn = ParseBool(val, _affixArcaneSpawn);
                    }
                    else if (key == "AFFIX_MOLTEN_EXPLOSION")
                    {
                        _affixMoltenExplosion = ParseBool(val, _affixMoltenExplosion);
                    }
                    else if (key == "AFFIX_DESECRATOR")
                    {
                        _affixDesecrator = ParseBool(val, _affixDesecrator);
                    }
                    else if (key == "AFFIX_THUNDERSTORM")
                    {
                        _affixThunderstorm = ParseBool(val, _affixThunderstorm);
                    }
                    else if (key == "AFFIX_FROZEN")
                    {
                        _affixFrozen = ParseBool(val, _affixFrozen);
                    }
                    else if (key == "AFFIX_FROZEN_BALL")
                    {
                        _affixFrozen = ParseBool(val, _affixFrozen);
                    }
                    else if (key == "AFFIX_FROZEN_PULSE")
                    {
                        _affixFrozenPulse = ParseBool(val, _affixFrozenPulse);
                    }
                    else if (key == "AFFIX_GHOM")
                    {
                        _affixGhomGas = ParseBool(val, _affixGhomGas);
                    }
                    else if (key == "EXPLOSIVE_GROTESQUE")
                    {
                        _explosiveGrotesque = ParseBool(val, _explosiveGrotesque);
                    }
                    else if (key == "AFFIX_ORBITER")
                    {
                        _affixOrbiter = ParseBool(val, _affixOrbiter);
                    }
                    else if (key == "AFFIX_WALLER")
                    {
                        _affixWaller = ParseBool(val, _affixWaller);
                    }
                    else if (key == "AFFIX_WORMHOLE_MINES")
                    {
                        _affixWormholeMines = ParseBool(val, _affixWormholeMines);
                    }
                    else if (key == "AFFIX_BOSS_FALLING_ROCKS")
                    {
                        _affixBossFallingRocks = ParseBool(val, _affixBossFallingRocks);
                    }
                    else if (key == "AFFIX_BOSS_LEAP_TELEGRAPHS")
                    {
                        _affixBossLeapTelegraphs = ParseBool(val, _affixBossLeapTelegraphs);
                    }
                    else if (key == "AFFIX_BOSS_METEORS")
                    {
                        _affixBossMeteors = ParseBool(val, _affixBossMeteors);
                    }
                    else if (key == "AFFIX_BOSS_KULLE_HAZARDS")
                    {
                        _affixBossKulleHazards = ParseBool(val, _affixBossKulleHazards);
                    }
                    else if (key == "AFFIX_BOSS_BLIGHTER_HAZARDS")
                    {
                        _affixBossBlighterHazards = ParseBool(val, _affixBossBlighterHazards);
                    }
                    else if (key == "AFFIX_BOSS_RATKING_HAZARDS")
                    {
                        _affixBossRatKingHazards = ParseBool(val, _affixBossRatKingHazards);
                    }
                    else if (key == "AFFIX_BOSS_ADRIA_GEYSERS")
                    {
                        _affixBossAdriaGeysers = ParseBool(val, _affixBossAdriaGeysers);
                    }
                    else if (key == "AFFIX_BOSS_BUTCHER_FIRE")
                    {
                        _affixBossButcherFire = ParseBool(val, _affixBossButcherFire);
                    }
                    else if (key == "AFFIX_BOSS_RIME_HAZARDS")
                    {
                        _affixBossRimeHazards = ParseBool(val, _affixBossRimeHazards);
                    }
                    else if (key == "AFFIX_SHOCK_TOWER_HAZARDS")
                    {
                        _affixShockTowerHazards = ParseBool(val, _affixShockTowerHazards);
                    }
                    else if (key == "AFFIX_BOSS_SANDMONSTER_TURRET_HAZARDS")
                    {
                        _affixBossSandmonsterTurretHazards = ParseBool(val, _affixBossSandmonsterTurretHazards);
                    }
                    else if (key == "ELITE_AFFIX_LABELS")
                    {
                        _eliteAffixLabelsEnabled = ParseBool(val, _eliteAffixLabelsEnabled);
                    }
                    else if (key == "DANGEROUS_MONSTER_LABELS")
                    {
                        _dangerousMonsterLabelsEnabled = ParseBool(val, _dangerousMonsterLabelsEnabled);
                    }
                    // Compatibility with abandoned intermediate revisions.
                    else if (key == "VIS_ELITE_AFFIX_LABELS")
                    {
                        _eliteAffixLabelsEnabled = ParseBool(val, _eliteAffixLabelsEnabled);
                    }
                    else if (key == "VIS_DANGEROUS_MONSTER_LABELS")
                    {
                        _dangerousMonsterLabelsEnabled = ParseBool(val, _dangerousMonsterLabelsEnabled);
                    }
                    else if (key == "VIS_PARTY_INSPECTOR_EXPANDED")
                    {
                        _visPartyInspectorExpanded = ParseBool(val, _visPartyInspectorExpanded);
                    }
                    else if (key == "PARTY_INSPECTOR_HOTKEY")
                    {
                        Key k;
                        if (Enum.TryParse<Key>(val, true, out k) && k != Key.Unknown)
                            _partyInspectorHotkey = k;
                    }
                    else if (key == "OPEN_GR_MAP_OVERRIDE")
                    {
                        _riftMapSelectionHasOverride = ParseBool(val, _riftMapSelectionHasOverride);
                    }
                    else if (key == "OPEN_GR_MAP_IDS")
                    {
                        ParseOpenGrMapIds(val);
                    }
                    else if (key == "TOGGLE_CATEGORY")
                    {
                        ToggleCategory cat;
                        if (Enum.TryParse<ToggleCategory>(val, true, out cat) &&
                            Enum.IsDefined(typeof(ToggleCategory), cat))
                            _activeToggleCategory = cat;
                        else
                            _activeToggleCategory = ToggleCategory.Visual; // fallback: Experimental/AutoSnap/Alerts → Visual
                    }
                    else if (key == "TOGGLE_DETAIL_SCROLL")
                    {
                        int iv;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                            _toggleDetailScroll = Math.Max(0, iv);
                    }
                    else if (key == "GLOBAL_TTS_ENABLED")
                    {
                        _globalTtsEnabled = ParseBool(val, _globalTtsEnabled);
                    }
                    else if (key == "GLOBAL_TTS_VOLUME")
                    {
                        int iv;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                            _globalTtsVolume = ClampGlobalTtsVolume(iv);
                    }
                    else if (key == "ZBARB_AUTOSNAP_HOTKEY")
                    {
                        Key k;
                        if (Enum.TryParse<Key>(val, true, out k) && k != Key.Unknown)
                            _zbarbAutoSnapHotkey = k;
                    }
                    else if (key == "TTS_BROADCAST_EXPANDED")
                    {
                        _ttsBroadcastExpanded = ParseBool(val, _ttsBroadcastExpanded);
                    }
                    else if (key == "TTS_CUSTOM_DEFAULTS_SEEDED")
                    {
                        _ttsCustomDefaultsSeeded = ParseBool(val, _ttsCustomDefaultsSeeded);
                    }
                    else if (key == "TTS_CUSTOM_DEFAULTS_SEED_VERSION")
                    {
                        int v;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            _ttsCustomDefaultsSeedVersion = Math.Max(0, v);
                    }
                    else if (key == "TTS_CUSTOM_COUNT")
                    {
                        int count;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                        {
                            count = Math.Max(0, Math.Min(MaxTtsCustomMessages, count));
                            while (_ttsCustomMessages.Count < count)
                                _ttsCustomMessages.Add(new TtsCustomMessage());
                            while (_ttsCustomMessages.Count > count)
                                _ttsCustomMessages.RemoveAt(_ttsCustomMessages.Count - 1);
                        }
                    }
                    else if (key.StartsWith("TTS_CUSTOM_", StringComparison.OrdinalIgnoreCase))
                    {
                        TryLoadTtsCustomMessageSetting(key, val);
                    }
                    else if (key == "AUTOSKILL_KEYBINDS_EXPANDED")
                    {
                        _autoSkillKeybindsExpanded = ParseBool(val, _autoSkillKeybindsExpanded);
                    }
                    else if (key == "AUTOSNAP_EXPANDED")
                    {
                        _autoSnapExpanded = ParseBool(val, _autoSnapExpanded);
                    }
                    else if (key == "AUTOSNAP_MODE")
                    {
                        int tmp;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmp))
                            _autoSnapMode = Math.Max(0, Math.Min(1, tmp));
                    }
                    else if (key == "AUTOSNAP_MELEE_RANGE")
                    {
                        int tmp;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmp))
                            _autoSnapMeleeRangeYards = Math.Max(0, Math.Min(80, tmp));
                    }
                    else if (key == "AUTOSNAP_RANGED_RANGE")
                    {
                        int tmp;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmp))
                            _autoSnapRangedRangeYards = Math.Max(0, Math.Min(80, tmp));
                    }
                    else if (key == "AUTOSNAP_LEFT_FORCE_STANDSTILL")
                    {
                        _autoSnapLeftClickForceStandStill = ParseBool(val, _autoSnapLeftClickForceStandStill);
                    }
                    else if (key == "AUTOSNAP_RESTORE_CURSOR")
                    {
                        _autoSnapRestoreCursor = ParseBool(val, _autoSnapRestoreCursor);
                    }
                    else if (key == "AUTOSNAP_IGNORE_JUGGERNAUTS")
                    {
                        _autoSnapIgnoreJuggernauts = ParseBool(val, _autoSnapIgnoreJuggernauts);
                    }
                    else if (key == "AUTOSNAP_IGNORE_MINIONS")
                    {
                        _autoSnapIgnoreMinions = ParseBool(val, _autoSnapIgnoreMinions);
                    }
                    else if (key == "AUTOSNAP_HOTKEYS_ENABLED")
                    {
                        _asHotkeysEnabled = ParseBool(val, _asHotkeysEnabled);
                    }
                    else if (key == "AUTOSNAP_HOTKEYS_ONLY")
                    {
                        _asHotkeysOnly = ParseBool(val, _asHotkeysOnly);
                    }
                    else if (key == "AUTOSNAP_HOTKEYS_EXPANDED")
                    {
                        _autoSnapHotkeysExpanded = ParseBool(val, _autoSnapHotkeysExpanded);
                    }
                    else if (key.StartsWith("AUTOSNAP_SLOT_", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx;
                        if (int.TryParse(key.Substring("AUTOSNAP_SLOT_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                            idx >= 0 && idx < _asEnabled.Length)
                        {
                            _asEnabled[idx] = ParseBool(val, _asEnabled[idx]);
                        }
                    }
                    else if (key.StartsWith("AUTOSNAP_HOTKEY_", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx;
                        if (int.TryParse(key.Substring("AUTOSNAP_HOTKEY_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) &&
                            idx >= 0 && idx < _asHotkeyKeys.Length)
                        {
                            Key k;
                            if (Enum.TryParse<Key>(val, true, out k))
                            {
                                _asHotkeyKeys[idx] = k;
                                _asHotkeyWinKeys[idx] = KeyToWinKeys(k);
                                _asHotkeyLabels[idx] = CaptureKeyLabel(k);
                            }
                        }
                    }
                    else if (key == "PLUGIN_ENABLED")
                    {
                        string[] pe = val.Split(new[]{'|'}, 2);
                        string peName = pe.Length > 0 ? pe[0].Trim() : string.Empty;
                        string peState = pe.Length > 1 ? pe[1].Trim() : string.Empty;
                        if (!string.IsNullOrWhiteSpace(peName))
                            _pluginEnabledOverrides[peName] = ParseBool(peState, true);
                    }
                    else if (key == "FAVORITE")
                    {
                        string[] parts = val.Split(new[] { '|' }, 2);
                        string typeName = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                        string display = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                        if (!string.IsNullOrWhiteSpace(typeName) && !IsFavorite(typeName))
                            _favorites.Add(new FavoriteEntry { TypeName = typeName, DisplayName = display });
                    }
                }

                _activePage = NormalizeMenuPage(_activePage);
                NormalizePluginsTabForEmptyFavorites();
                SeedDefaultDisabledPluginOverrides();
                _globalTtsVolume = ClampGlobalTtsVolume(_globalTtsVolume);
                TrimTtsCustomMessages();
                SeedDefaultTtsCustomMessagesIfNeeded();
                EnsureAtLeastOneTtsCustomMessageRowIfExpanded();
                SyncTtsCustomRuntimeLists();
                RequestPluginCacheRefresh();

                // Clean migration from abandoned v5/v6 OpenGR selector test builds.
                if (_loadedSettingsVersion > 0 && _loadedSettingsVersion < 7)
                {
                    _riftMapSelectionHasOverride = true;
                    _riftEnabledMapIds.Clear();
                }

                try
                {
                    if (!SamePath(readPath, officialPath))
                    {
                        SaveSettings();
                        LogDebug("Settings migrated to " + officialPath + " from " + readPath + ".");
                    }
                }
                catch { }
            }
            catch
            {
                _activePage = NormalizeMenuPage(_activePage);
                NormalizePluginsTabForEmptyFavorites();
                RequestPluginCacheRefresh();
            }
        }

        private void TryLoadTtsCustomMessageSetting(string key, string val)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;

                string rest = key.Substring("TTS_CUSTOM_".Length);
                int us = rest.IndexOf('_');
                if (us <= 0)
                    return;

                int idx;
                if (!int.TryParse(rest.Substring(0, us), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                    return;

                if (idx < 0 || idx >= MaxTtsCustomMessages)
                    return;

                while (_ttsCustomMessages.Count <= idx)
                    _ttsCustomMessages.Add(new TtsCustomMessage());

                string field = rest.Substring(us + 1);

                if (string.Equals(field, "HOTKEY", StringComparison.OrdinalIgnoreCase))
                {
                    Key k;
                    if (Enum.TryParse<Key>(val, true, out k))
                        _ttsCustomMessages[idx].Hotkey = k;
                    return;
                }

                if (string.Equals(field, "TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    _ttsCustomMessages[idx].Text = NormalizeTtsCustomMessageForEditor(DecodeSettingText(val));
                    return;
                }
            }
            catch { }
        }


        private string S7oPluginDirectory()
        {
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "s7o");
            }
            catch
            {
                return ".";
            }
        }

        private string S7oSettingsDirectory()
        {
            try
            {
                string dir = Path.Combine(S7oPluginDirectory(), "settings");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return S7oPluginDirectory();
            }
        }

        private string TurboHudLogsDirectory()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return ".";
            }
        }

        private string SettingsPath()
        {
            try
            {
                return Path.Combine(S7oSettingsDirectory(), SettingsFileName);
            }
            catch
            {
                return SettingsFileName;
            }
        }

        private string LegacySettingsPath()
        {
            try
            {
                return Path.Combine(S7oPluginDirectory(), LegacySettingsFileName);
            }
            catch
            {
                return LegacySettingsFileName;
            }
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                    return false;

                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string SelectSettingsReadPath()
        {
            string official = SettingsPath();

            try
            {
                if (File.Exists(official))
                    return official;
            }
            catch { }

            string legacy = LegacySettingsPath();

            try
            {
                if (File.Exists(legacy))
                    return legacy;
            }
            catch { }

            return official;
        }

        private string DebugLogPath()
        {
            try
            {
                return Path.Combine(TurboHudLogsDirectory(), DebugLogFileName);
            }
            catch
            {
                return DebugLogFileName;
            }
        }

        private void LogDebug(string message)
        {
            if (!_debugLogging)
                return;

            try
            {
                string path = DebugLogPath();
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                string line =
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    " " +
                    (message ?? string.Empty) +
                    Environment.NewLine;

                File.AppendAllText(path, line);
                _debugLogPath = path;
            }
            catch
            {
                // Debug logging must never break HUD rendering or input.
            }
        }

        private void ParseMenuButtonVisualLine(string val)
        {
            try
            {
                string[] parts = (val ?? string.Empty).Split('|');
                if (parts.Length > 0) _showDot = ParseBool(parts[0], _showDot);
                if (parts.Length > 1) _visMenuButtonExpanded = ParseBool(parts[1], _visMenuButtonExpanded);

                float fv;
                int iv;

                if (parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out fv))
                    _menuButtonClosedSize = ViClampF(fv, 18f, 60f);
                if (parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                    _menuButtonClosedColorIdx = ViClamp(iv, 0, 7);
                if (parts.Length > 4 && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                    _menuButtonClosedTone = ViClamp(iv, 0, 10);
                if (parts.Length > 5 && float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out fv))
                    _menuButtonOpenSize = ViClampF(fv, 18f, 70f);
                if (parts.Length > 6 && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                    _menuButtonOpenColorIdx = ViClamp(iv, 0, 7);
                if (parts.Length > 7 && int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                    _menuButtonOpenTone = ViClamp(iv, 0, 10);

                _dotRect.Width = _menuButtonClosedSize;
                _dotRect.Height = _menuButtonClosedSize;
            }
            catch { }
        }

        private void ParseVisClickLine(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return;

            string[] p = val.Split('|');

            if (p.Length >= 1)
                _visClickAnimEnabled = ParseBool(p[0].Trim(), _visClickAnimEnabled);

            if (p.Length >= 2)
                _visClickAnimExpanded = ParseBool(p[1].Trim(), _visClickAnimExpanded);

            if (p.Length >= 3)
            {
                float v;
                if (float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    _visClickAnimSize = ViClampF(v, 0.5f, 4.0f);
            }

            if (p.Length >= 4)
            {
                int v;
                if (int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    _visClickAnimColorIdx = ViClamp(v, 0, 7);
            }

            if (p.Length >= 5)
            {
                int v;
                if (int.TryParse(p[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    _visClickAnimTone = ViClamp(v, 0, 10);
            }

            if (p.Length >= 6)
            {
                float v;
                if (float.TryParse(p[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    _visClickAnimThickness = ViClampF(v, 0.5f, 4.0f);
            }

            // Extra fields from older experimental configs, such as rotate/glow, are intentionally ignored.
        }

        private static void ParseVisLine(
            string val,
            out bool enabled,
            out bool expanded,
            ref float f1,
            ref int ci,
            ref int tone,
            ref float f2)
        {
            enabled = false;
            expanded = false;

            if (string.IsNullOrWhiteSpace(val))
                return;

            string[] p = val.Split('|');

            if (p.Length >= 1)
                enabled = ParseBool(p[0].Trim(), enabled);

            if (p.Length >= 2)
                expanded = ParseBool(p[1].Trim(), expanded);

            if (p.Length >= 3)
            {
                float v;
                if (float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    f1 = v;
            }

            if (p.Length >= 4)
            {
                int v;
                if (int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    ci = ViClamp(v, 0, 7);
            }

            if (p.Length >= 5)
            {
                int v;
                if (int.TryParse(p[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    tone = ViClamp(v, 0, 10);
            }

            if (p.Length >= 6)
            {
                float v;
                if (float.TryParse(p[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    f2 = v;
            }

            // If older INI files contain a 7th manual-expand field, it is intentionally ignored.
        }

        private void ParseOpenGrMapIds(string value)
        {
            _riftEnabledMapIds.Clear();

            string[] parts = (value ?? string.Empty).Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                uint id;
                if (uint.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    _riftEnabledMapIds.Add(id);
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool b;
            if (bool.TryParse(value, out b)) return b;
            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static string RectToString(RectangleF r)
        {
            return r.X.ToString(CultureInfo.InvariantCulture) + "," +
                   r.Y.ToString(CultureInfo.InvariantCulture) + "," +
                   r.Width.ToString(CultureInfo.InvariantCulture) + "," +
                   r.Height.ToString(CultureInfo.InvariantCulture);
        }

        private static RectangleF ParseRect(string value, RectangleF fallback)
        {
            try
            {
                string[] parts = (value ?? string.Empty).Split(',');
                if (parts.Length != 4) return fallback;
                float x, y, w, h;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return fallback;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return fallback;
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) return fallback;
                if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return fallback;
                return new RectangleF(x, y, w, h);
            }
            catch
            {
                return fallback;
            }
        }

        private void ClampRectsToScreen()
        {
            ClampWindowOnly();
            ClampDotOnly();
        }

        private void ClampWindowOnly()
        {
            try
            {
                float sw = Hud != null && Hud.Window != null ? Hud.Window.Size.Width : DefaultDotBaseW;
                float sh = Hud != null && Hud.Window != null ? Hud.Window.Size.Height : DefaultDotBaseH;

                _windowRect.Width = Math.Max(720f, Math.Min(_windowRect.Width, sw - 20f));
                _windowRect.Height = Math.Max(580f, Math.Min(_windowRect.Height, sh - 20f));
                _windowRect.X = Math.Max(4f, Math.Min(_windowRect.X, sw - _windowRect.Width - 4f));
                _windowRect.Y = Math.Max(4f, Math.Min(_windowRect.Y, sh - _windowRect.Height - 4f));
            }
            catch { }
        }

        private void ClampDotOnly()
        {
            try
            {
                float sw = Hud != null && Hud.Window != null ? Hud.Window.Size.Width : DefaultDotBaseW;
                float sh = Hud != null && Hud.Window != null ? Hud.Window.Size.Height : DefaultDotBaseH;

                _dotRect.Width = Math.Max(24f, _dotRect.Width);
                _dotRect.Height = Math.Max(24f, _dotRect.Height);

                if (_dotRect.X < 0f || _dotRect.Y < 0f)
                    _dotRect = DefaultDotRect(sw, sh);

                _dotRect.X = Math.Max(2f, Math.Min(_dotRect.X, sw - _dotRect.Width - 2f));
                _dotRect.Y = Math.Max(2f, Math.Min(_dotRect.Y, sh - _dotRect.Height - 2f));
            }
            catch { }
        }

        private RectangleF DefaultDotRect(float sw, float sh)
        {
            float x = DefaultDotBaseX;
            float y = DefaultDotBaseY;

            if (sw > 100f && sh > 100f)
            {
                x = sw * (DefaultDotBaseX / DefaultDotBaseW);
                y = sh * (DefaultDotBaseY / DefaultDotBaseH);
            }

            return new RectangleF(x, y, _menuButtonClosedSize, _menuButtonClosedSize);
        }

        private void RelocateLegacyRightCenterDotIfNeeded()
        {
            if (_loadedSettingsVersion >= SettingsVersion) return;

            try
            {
                float sw = Hud != null && Hud.Window != null ? Hud.Window.Size.Width : DefaultDotBaseW;
                float sh = Hud != null && Hud.Window != null ? Hud.Window.Size.Height : DefaultDotBaseH;

                bool looksLikePriorRightCenterDefault =
                    _dotRect.X >= sw - 130f &&
                    _dotRect.Y >= sh * 0.35f &&
                    _dotRect.Y <= sh * 0.65f;

                if (looksLikePriorRightCenterDefault)
                {
                    _dotRect = DefaultDotRect(sw, sh);
                    _status = "MENU BUTTON RESET TO DEFAULT POSITION";
                    MarkLayoutDirty();
                }
            }
            catch { }
        }

        private static class MenuInput
        {
            private const ushort VK_SHIFT = 0x10;
            private const ushort VK_P = 0x50;
            private const ushort VK_ESCAPE = 0x1B;

            private const uint INPUT_KEYBOARD = 1;
            private const uint KEYEVENTF_KEYUP = 0x0002;

            [StructLayout(LayoutKind.Sequential)]
            private struct INPUT
            {
                public uint type;
                public InputUnion U;
            }

            [StructLayout(LayoutKind.Explicit)]
            private struct InputUnion
            {
                [FieldOffset(0)]
                public KEYBDINPUT ki;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            public static void SendEscape()
            {
                try
                {
                    SendKeys.SendWait("{ESC}");
                }
                catch
                {
                    SendVirtualKey(VK_ESCAPE);
                }
            }

            public static void SendShiftP()
            {
                try
                {
                    // Same SendKeys pattern used by the working Auto Gem no-click background logic.
                    // +p means Shift+P.
                    SendKeys.SendWait("+p");
                }
                catch
                {
                    // Fallback to the existing raw SendInput method if SendKeys fails.
                    SendKeyCombo(VK_SHIFT, VK_P);
                }
            }

            private static void SendVirtualKey(ushort vk)
            {
                if (vk == 0) return;

                SendKeyboard(vk, false);
                Thread.Sleep(10);
                SendKeyboard(vk, true);
            }

            private static void SendKeyCombo(ushort modifierVk, ushort keyVk)
            {
                if (modifierVk == 0 || keyVk == 0) return;

                SendKeyboard(modifierVk, false);
                Thread.Sleep(10);
                SendVirtualKey(keyVk);
                Thread.Sleep(10);
                SendKeyboard(modifierVk, true);
            }

            private static void SendKeyboard(ushort vk, bool up)
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
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    }
                };

                SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
            }
        }
    }
}
