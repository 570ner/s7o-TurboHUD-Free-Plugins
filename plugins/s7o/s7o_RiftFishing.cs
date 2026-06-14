using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpDX.DirectInput;
using SharpDX.Direct2D1;
using Vector2 = SharpDX.Vector2;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // ============================================================
    // s7o_RiftFishing.cs
    // TurboHUD Free / FreeHUD compatible Greater Rift fishing plugin.
    // ============================================================

    public class s7o_RiftFishing : BasePlugin, IInGameTopPainter, IKeyEventHandler,
                                   IAfterCollectHandler, INewAreaHandler, IMouseClickHandler
    {
        // ── User settings ─────────────────────────────────────────────────

        public Key FishKey { get; set; } = Key.F3;
        public IKeyEvent ToggleKeyEvent { get; private set; }

        public bool AnnounceMapName { get; set; } = true;
        public bool AllowPartyMode { get; set; } = false;
        public bool ShowPartyModeBlockedMessage { get; set; } = false;
        public bool PauseOnGoodMap { get; set; } = true;

        public int PortalClickRetryMs { get; set; } = 80;
        public int OrekClickRetryMs { get; set; } = 200;
        public int ObeliskClickRetryMs { get; set; } = 200;
        public int CloseButtonRetryMs { get; set; } = 150;
        public int CloseRiftAfterButtonGoneDelayMs { get; set; } = 1200;
        public int StepDelayMs { get; set; } = 80;
        public int PostEnterMapReadDelayMs { get; set; } = 0;
        public int MapReadMaxWaitMs { get; set; } = 50;
        // Good-map TTS now intentionally plays before ESC, matching LightningMOD's safer order.
        // If AnnounceMapName is false, GoodMapPauseDelayMs remains the direct delay before ESC.
        public int GoodMapPauseDelayMs { get; set; } = 500;
        public int GoodMapTtsDelayBeforePauseMs { get; set; } = 300;
        public int GoodMapPauseDelayAfterTtsMs { get; set; } = 300;

        // Obsolete compatibility setting: TTS is no longer delayed after pause in the TTS-before-ESC flow.
        public int GoodMapTtsDelayAfterPauseMs { get; set; } = 500;
        // Obsolete compatibility setting: timeout-only pause fallback is intentionally disabled.
        // Pause is confirmed only by Hud.Game.IsPaused or a visible game-menu UI element.
        public int GoodMapTtsFallbackAfterEscapeMs { get; set; } = 1200;
        public int GoodMapEscapeRetryMs { get; set; } = 275;
        public int GoodMapEscapeMaxAttempts { get; set; } = 5;
        public int GlobalInputMinIntervalMs { get; set; } = 25;
        public float PortalClickMaxDistance { get; set; } = 180.0f;
        // Fortress return portals project slightly above the screen on Pandemonium Fortress.
        // Keep this fortress-only native path enabled, but leave debug overlays/logging off for release.
        public bool EnableFortressFileDebug { get; set; } = false;
        public bool ShowFortressPortalDebug { get; set; } = false;
        public int FortressFileDebugIntervalMs { get; set; } = 250;
        public bool EnableFortressNativeReturn { get; set; } = true;
        // Temple of the Firstborn can have the same tall-portal projection problem as Fortress.
        // Keep this scene-scoped native path first; the strict normal click path remains untouched for other maps.
        public bool EnableFirstbornTempleNativeReturn { get; set; } = true;
        public int FortressNativeRetryMs { get; set; } = 100;
        public int FortressNativeHoverConfirmMs { get; set; } = 70;
        public int FortressNativeCandidateLogLimit { get; set; } = 12;
        public float OrekInteractDistance { get; set; } = 18.0f;
        public float ObeliskInteractDistance { get; set; } = 18.0f;

        private const int SNO_OREK = 363744;           // X1_LR_Nephalem / Orek
        private const int SNO_OBELISK = 364715;        // x1_OpenWorld_LootRunObelisk_B
        private const int SNO_TOWN_GR_PORTAL = 396751; // X1_OpenWorld_Tiered_Rifts_Portal

        private static readonly HashSet<int> RiftExitPortalActorSnos = new HashSet<int>
        {
            // Generic orange portals
            175482, // g_Portal_Rectangle_Orange
            175501, // g_Portal_Circle_Orange
            175999, // g_Portal_Arch_Orange
            176001, // g_Portal_ArchTall_Orange
            176005, // g_Portal_RectangleTall_Orange
            176008, // g_Portal_Square_Orange
            176038, // g_Portal_Oval_Orange
            176536, // g_Portal_Ladder_Orange
            178293, // g_Portal_Rectangle_Orange_IconDoor
            178304, // g_Portal_ArchTall_Orange_IconDoor
            185067, // g_Portal_Circle_Orange_Bright
            185156, // g_portal_Ladder_Short_Orange
            188743, // g_Portal_Rectangle_Orange_Bright
            190005, // g_Portal_Square_Orange_IconDoor
            204187, // g_Portal_Square_Orange_Bright
            204189, // g_Portal_Square_Orange_SuperBright
            204202, // g_Portal_ArchTall_Orange_IconDoor_Bright
            204747, // g_Portal_ArchTall_Orange_LargeRadius
            206234, // g_portal_Ladder_Short_Orange_Bright
            338949, // g_Portal_RectangleTall_Orange_IconDoor
            359447, // g_portal_Ladder_Very_Short_Orange_VeryBright
            359453, // g_portal_Ladder_Very_Short_Orange_VeryBright_BogPeople
            365394, // g_portal_Ladder_VeryShort_Orange

            // Generic blue portals seen as GR return exits on some maps
            175467, // g_Portal_Rectangle_Blue
            176000, // g_Portal_Arch_Blue
            176002, // g_Portal_ArchTall_Blue
            176003, // g_Portal_Circle_Blue
            176004, // g_Portal_RectangleTall_Blue
            176007, // g_Portal_Square_Blue
            176039, // g_Portal_Oval_Blue
            176537, // g_Portal_Ladder_Blue
            182738, // g_Portal_Oval_Blue_Saturated
            185364, // g_portal_Ladder_Short_Blue
            204183, // g_portal_Ladder_Short_Blue_largeRadius
            221031, // g_Portal_Ladder_Tall_Blue
            229013, // g_Portal_Ladder_OffCenter_Blue
            241674, // g_Portal_Ladder_Blue_OffCenter
            329025, // g_Portal_Rectangle_Blue_Westmarch
            338951, // g_Portal_Rectangle_Blue_IconDoor
            341572, // g_portal_Ladder_VeryShort_Blue
            365112, // g_Portal_ArchTall_Blue_IconBlue
            374925, // g_Portal_ArchTall_Blue_WestMChurch
            396534  // g_Portal_Ladder_Blue_OffCenter_fortress3
        };

        // The tall fortress arch often projects too high. Test downward offsets first, then a small upward check.
        private static readonly int[] FortressNativeYOffsetPixels = new int[] { 0, 24, 48, 72, 96, 128, 160, 192, -24, -48 };

        public IFont HeaderFont { get; private set; }
        public IFont InfoFont { get; private set; }
        public IFont ButtonFont { get; private set; }

        public Dictionary<uint, FishMap> MapList { get; private set; }

        // ── UI elements ───────────────────────────────────────────────────

        private IUiElement uiGRMainPage;
        private IUiElement uiGreaterRiftButton;
        private IUiElement uiAcceptButton;
        private IUiElement uiJoinPartyDialog;
        private IUiElement uiJoinPartyAcceptButton;
        private IUiElement uiCloseRiftButton;
        private IUiElement uiGameMenu;
        private IUiElement uiResumeGame;
        private IUiElement uiMapName;
        private IUiElement uiActorTag;
        private IUiElement[] uiActorTags;

        // ── Draw resources ────────────────────────────────────────────────

        private IBrush _buttonFill;
        private IBrush _buttonFillActive;
        private IBrush _buttonBorder;
        private IBrush _buttonBorderActive;
        private IBrush _hintBackground;

        public bool UseRoundedGeometryButtons = true;
        private bool _geometryDrawFailed;
        private bool _geometryDrawFailureLogged;
        private IBrush _pillDarkBrush;
        private IBrush _pillLightBrush;
        private IBrush _pillGreenBrush;
        private IBrush _pillGreenLightBrush;
        private IBrush _pillOrangeBorderBrush;
        // Status spinner fireball layers: outer halo, mid flame, inner core. Paint-only decoration.
        private IBrush _fireballHaloBrush;
        private IBrush _fireballFlameBrush;
        private IBrush _fireballCoreBrush;
        // Cartoon black outlines. Strokes are positioned to expand OUTWARD only — they never reduce the colored interior.
        private IBrush _fireballOutlineBrush;
        private IBrush _fireballCometOutlineBrush;
        // Pre-built alpha steps for the fading trail. Created once in Load(); zero per-frame allocations.
        private const int FireballTrailAlphaSteps = 12;
        private IBrush[] _fireballTrailBrushes;

        // ── State ─────────────────────────────────────────────────────────

        private enum FishState
        {
            Idle,
            Armed,
            OpenDialog,
            SelectGreater,
            AcceptGreater,
            WaitForRift,
            CheckMap,
            GoodMap,
            BadMapReturnToTown,
            BadMapFindOrek,
            BadMapCloseRift,
            BadMapFindObelisk
        }

        private FishState _state = FishState.Idle;
        private bool _capturingHotkey;
        private RectangleF _hotkeyButtonRect = RectangleF.Empty;
        private bool _blockedMouseDown;
        private bool _enteredRiftThisCycle;
        private string _status = string.Empty;
        private string _lastFoundMapName = string.Empty;
        private bool _goodMapAnnounced;
        private bool _goodMapEscapeSent;
        private bool _goodMapTtsSent;
        private bool _goodMapPauseConfirmed;
        private int _goodMapEscapeAttempts;
        private bool _badMapNeedsClose;
        private bool _closeRiftClickedThisCycle;

        private IWatch _stepWatch;
        private IWatch _stateWatch;
        private IWatch _postEnterWatch;
        private IWatch _closeClickWatch;
        private IWatch _closeConfirmWatch;
        private IWatch _actorClickWatch;
        private IWatch _portalClickWatch;
        private string _fortressDebugLine = string.Empty;
        private int _fortressNativeProbeAttempts;
        private IWatch _fortressNativeRetryWatch;
        private IWatch _fortressNativeHoverWatch;
        private bool _fortressNativeProbeActive;
        private int _fortressNativeProbeX;
        private int _fortressNativeProbeY;
        private int _fortressNativeProbeRawX;
        private int _fortressNativeProbeRawY;
        private string _fortressNativeProbeLabel = string.Empty;
        private string _fortressNativeCandidateSummary = string.Empty;
        private IWatch _fortressFileDebugWatch;
        private IWatch _goodMapWatch;
        private IWatch _goodMapAfterEscapeWatch;
        private IWatch _globalInputWatch;
        public int ActorHoverBeforeClickMs { get; set; } = 45;
        private IWatch _actorHoverWatch;
        private uint _hoverActorAcdId;
        private string _hoverActorPurpose;

        public bool Running { get; private set; }

        // ── Orek's Dream localized names, matching LightningMOD behavior ──

        private readonly HashSet<string> OrekRiftNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "欧雷克的梦境",
            "歐瑞克之夢",
            "Orek's Dream",
            "Sueño de Orek",
            "오레크의 꿈",
            "Oreks Traum",
            "Rêve d’Orek",
            "Rêve d'Orek",
            "Сон Орека",
            "Sen Oreka",
            "Sogno di Orek",
            "Sonho de Orek"
        };

        public s7o_RiftFishing()
        {
            Enabled = true;
            Order = 29600;
            MapList = new Dictionary<uint, FishMap>();
            BuildMaps();
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, FishKey, false, false, false);

            HeaderFont = Hud.Render.CreateFont("tahoma", 10.0f, 255, 230, 210, 80, true, false, 220, 0, 0, 0, true);
            InfoFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 230, 80, true, false, 220, 0, 0, 0, true);
            ButtonFont = Hud.Render.CreateFont("tahoma", 8.5f, 255, 255, 255, 255, true, false, 180, 0, 0, 0, true);

            _buttonFill = Hud.Render.CreateBrush(230, 28, 28, 28, 0);
            _buttonFillActive = Hud.Render.CreateBrush(240, 55, 42, 18, 0);
            _buttonBorder = Hud.Render.CreateBrush(255, 230, 135, 35, 1.5f);
            _buttonBorderActive = Hud.Render.CreateBrush(255, 255, 185, 65, 2.0f);
            _hintBackground = Hud.Render.CreateBrush(170, 0, 0, 0, 0);

            _pillDarkBrush = Hud.Render.CreateBrush(255, 48, 48, 48, 0);
            _pillLightBrush = Hud.Render.CreateBrush(80, 125, 125, 125, 0);
            _pillGreenBrush = Hud.Render.CreateBrush(255, 0, 170, 60, 0);
            _pillGreenLightBrush = Hud.Render.CreateBrush(90, 120, 255, 150, 0);
            _pillOrangeBorderBrush = Hud.Render.CreateBrush(225, 105, 55, 10, 0);

            // Center status spinner fireballs: paint-only decoration, no automation behavior.
            // Filled brushes (strokeWidth = 0). Layered halo/flame/core gives a real fireball glow
            // against the dark game background. No per-frame allocations beyond brush calls.
            _fireballHaloBrush  = Hud.Render.CreateBrush(140, 255,  60,  10, 0);
            _fireballFlameBrush = Hud.Render.CreateBrush(230, 255, 150,  35, 0);
            _fireballCoreBrush  = Hud.Render.CreateBrush(255, 255, 245, 195, 0);

            // Thick cartoon outline around fireball heads. Drawn with inner edge at the halo's
            // outer edge — expands outward only, never reduces the colored interior.
            _fireballOutlineBrush       = Hud.Render.CreateBrush(255, 0, 0, 0, 2.0f);
            // Thinner outline for the bright "comet body" particles right behind each head.
            _fireballCometOutlineBrush  = Hud.Render.CreateBrush(255, 0, 0, 0, 1.0f);

            // Trail alpha gradient. Pre-built once: index 0 is brightest (just behind the fireball),
            // last index is faintest (tail end). Warm orange flame color.
            _fireballTrailBrushes = new IBrush[FireballTrailAlphaSteps];
            for (int i = 0; i < FireballTrailAlphaSteps; i++)
            {
                // Quadratic falloff: trail starts strong, fades softer near the tail end.
                float t = (float)i / FireballTrailAlphaSteps;
                byte a = (byte)Math.Max(0, 175 * (1.0f - t) * (1.0f - t));
                _fireballTrailBrushes[i] = Hud.Render.CreateBrush(a, 255, 140, 30, 0);
            }

            uiGRMainPage = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.rift_dialog_mainPage", null, null);

            uiGreaterRiftButton = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.rift_dialog_mainPage.LayoutRoot.RiftRadioButtons.GreaterRiftButton", null, null);

            uiAcceptButton = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.rift_dialog_mainPage.LayoutRoot.accept_Button", null, null);

            uiJoinPartyDialog = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.rift_join_party_main.LayoutRoot.Background", null, null);

            uiJoinPartyAcceptButton = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.rift_join_party_main.LayoutRoot.Background.buttons.accept", null, null);

            uiCloseRiftButton = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.interact_dialog_mainPage.interact_dialog_Background.stack.interact_button_2", null, null);

            uiGameMenu = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd.title", null, null);

            uiResumeGame = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd.button_resumeGame", null, null);

            uiMapName = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.minimap_dialog_backgroundScreen.minimap_dialog_pve.area_name", null, null);

            uiActorTag = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.game_dialog_main.RActorTag0", null, null);

            uiActorTags = new IUiElement[10];

            for (int i = 0; i < uiActorTags.Length; i++)
            {
                uiActorTags[i] = Hud.Render.RegisterUiElement(
                    "Root.NormalLayer.game_dialog_main.RActorTag" + i.ToString(),
                    null,
                    null);
            }

            _stepWatch = Hud.Time.CreateWatch();
            _stateWatch = Hud.Time.CreateWatch();
            _postEnterWatch = Hud.Time.CreateWatch();
            _closeClickWatch = Hud.Time.CreateWatch();
            _closeConfirmWatch = Hud.Time.CreateWatch();
            _actorClickWatch = Hud.Time.CreateWatch();
            _portalClickWatch = Hud.Time.CreateWatch();
            _fortressNativeRetryWatch = Hud.Time.CreateWatch();
            _fortressNativeHoverWatch = Hud.Time.CreateWatch();
            _fortressFileDebugWatch = Hud.Time.CreateWatch();
            _goodMapWatch = Hud.Time.CreateWatch();
            _goodMapAfterEscapeWatch = Hud.Time.CreateWatch();
            _globalInputWatch = Hud.Time.CreateWatch();
            _actorHoverWatch = Hud.Time.CreateWatch();
        }

        // ── Public map API for later HUD MENU / MACROS integration ─────────

        public void EnableMap(uint id, bool enabled = true)
        {
            FishMap map;
            if (MapList.TryGetValue(id, out map))
                map.Enabled = enabled;
        }

        public void DisableAllMaps()
        {
            foreach (var map in MapList.Values)
                map.Enabled = false;
        }

        public void SetEnabledMaps(params uint[] ids)
        {
            DisableAllMaps();

            if (ids == null) return;

            foreach (var id in ids)
                EnableMap(id, true);
        }

        // Backward-compatible alias for old test customizers.
        public void AddMapsById(params uint[] ids)
        {
            SetEnabledMaps(ids);
        }

        // ── Key / mouse handling ──────────────────────────────────────────

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (keyEvent == null || !keyEvent.IsPressed)
                return;

            if (_capturingHotkey)
            {
                if (keyEvent.Key == Key.Escape)
                {
                    _capturingHotkey = false;
                    return;
                }

                FishKey = keyEvent.Key;
                ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, FishKey, false, false, false);
                _capturingHotkey = false;
                return;
            }

            if (ToggleKeyEvent != null && ToggleKeyEvent.Matches(keyEvent))
                ToggleRunning();
        }

        public bool MouseDown(MouseButtons button)
        {
            _blockedMouseDown = false;

            if (button != MouseButtons.Left)
                return false;

            if (!_hotkeyButtonRect.IsEmpty && IsMouseInside(_hotkeyButtonRect))
            {
                _blockedMouseDown = true;
                return true;
            }

            return false;
        }

        public bool MouseUp(MouseButtons button)
        {
            if (button != MouseButtons.Left)
                return false;

            if (_blockedMouseDown)
            {
                _blockedMouseDown = false;

                if (!_hotkeyButtonRect.IsEmpty && IsMouseInside(_hotkeyButtonRect))
                {
                    if (Running)
                    {
                        Running = false;
                        StopTimersAndFlags();
                        _state = FishState.Idle;
                    }

                    _capturingHotkey = true;
                    _status = "Press new OpenGR hotkey...";

                    return true;
                }
            }

            return false;
        }

        private void ToggleRunning()
        {
            if (Running)
            {
                Running = false;
                StopTimersAndFlags();
                _status = string.Empty;
                return;
            }

            if (!IsGreaterRiftDialogOpen() && !IsJoinPartyDialogOpen())
            {
                // Required behavior: F3 in town without the GR panel does nothing.
                _status = string.Empty;
                return;
            }

            if (IsPartyRunBlocked())
            {
                StopWithStatus(GetPartyModeBlockedStatus());
                return;
            }

            Running = true;
            _capturingHotkey = false;
            _enteredRiftThisCycle = false;
            _lastFoundMapName = string.Empty;
            SetState(FishState.Armed, "OpenGR started");
        }

        // ── New area ──────────────────────────────────────────────────────

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            if (newGame)
            {
                Running = false;
                StopTimersAndFlags();
                _status = string.Empty;
            }
        }

        // ── Automation state machine ──────────────────────────────────────

        public void AfterCollect()
        {
            if (!Running)
                return;

            try
            {
                if (Hud.Game == null || Hud.Game.Me == null)
                    return;

                if (!Hud.Game.IsInGame || Hud.Game.IsLoading || !Hud.Window.IsForeground)
                    return;

                if (IsPartyRunBlocked())
                {
                    StopWithStatus(GetPartyModeBlockedStatus());
                    return;
                }

                if (Running && IsInTown() && _badMapNeedsClose &&
                    _state != FishState.BadMapFindOrek &&
                    _state != FishState.BadMapCloseRift)
                {
                    SetState(FishState.BadMapFindOrek, "Closing bad GR");
                }

                switch (_state)
                {
                    case FishState.Armed:
                        RunArmedState();
                        break;
                    case FishState.OpenDialog:
                        RunOpenDialogState();
                        break;
                    case FishState.SelectGreater:
                        RunSelectGreaterState();
                        break;
                    case FishState.AcceptGreater:
                        RunAcceptGreaterState();
                        break;
                    case FishState.WaitForRift:
                        RunWaitForRiftState();
                        break;
                    case FishState.CheckMap:
                        RunCheckMapState();
                        break;
                    case FishState.GoodMap:
                        RunGoodMapState();
                        break;
                    case FishState.BadMapReturnToTown:
                        RunBadMapReturnToTownState();
                        break;
                    case FishState.BadMapFindOrek:
                        RunBadMapFindOrekState();
                        break;
                    case FishState.BadMapCloseRift:
                        RunBadMapCloseRiftState();
                        break;
                    case FishState.BadMapFindObelisk:
                        RunBadMapFindObeliskState();
                        break;
                    default:
                        SetState(FishState.Idle, string.Empty);
                        Running = false;
                        break;
                }
            }
            catch
            {
            }
        }

        private void RunArmedState()
        {
            if (IsJoinPartyDialogOpen())
            {
                if (uiJoinPartyAcceptButton != null && uiJoinPartyAcceptButton.Visible && UiClickReady())
                {
                    if (!TryLeftClickUi(uiJoinPartyAcceptButton)) return;
                    MarkStep();
                    SetState(FishState.WaitForRift, "Accepted GR invite");
                }
                return;
            }

            if (IsGreaterRiftDialogOpen() && IsInTown())
            {
                SetState(FishState.SelectGreater, "Selecting Greater Rift");
                return;
            }

            StopWithStatus(string.Empty);
        }

        private void RunOpenDialogState()
        {
            if (_badMapNeedsClose || _closeRiftClickedThisCycle)
            {
                SetState(FishState.BadMapFindOrek, "Closing bad GR");
                return;
            }

            if (IsGreaterRiftDialogOpen())
            {
                SetState(FishState.SelectGreater, "Selecting Greater Rift");
                return;
            }

            if (_stateWatch != null && _stateWatch.IsRunning && _stateWatch.ElapsedMilliseconds > 3000)
                SetState(FishState.BadMapFindObelisk, "Opening obelisk");
        }

        private void RunSelectGreaterState()
        {
            if (_badMapNeedsClose || _closeRiftClickedThisCycle)
            {
                SetState(FishState.BadMapFindOrek, "Closing bad GR");
                return;
            }

            if (!HasKeyForNextRift())
            {
                StopWithStatus("No GR keys — OpenGR stopped");
                return;
            }

            if (!IsGreaterRiftDialogOpen())
            {
                SetState(FishState.OpenDialog, "Waiting for rift dialog");
                return;
            }

            if (uiGreaterRiftButton != null &&
                uiGreaterRiftButton.Visible &&
                uiGreaterRiftButton.AnimState == 3)
            {
                if (UiClickReady())
                {
                    if (!TryLeftClickUi(uiGreaterRiftButton)) return;
                    MarkStep();
                    SetState(FishState.AcceptGreater, "Accepting Greater Rift");
                }
                return;
            }

            if (uiGreaterRiftButton != null &&
                uiGreaterRiftButton.Visible &&
                uiGreaterRiftButton.AnimState == 5)
            {
                SetState(FishState.AcceptGreater, "Accepting Greater Rift");
                return;
            }

            _status = "Waiting for Greater Rift button";
        }

        private void RunAcceptGreaterState()
        {
            if (_badMapNeedsClose || _closeRiftClickedThisCycle)
            {
                SetState(FishState.BadMapFindOrek, "Closing bad GR");
                return;
            }

            if (!HasKeyForNextRift())
            {
                StopWithStatus("No GR keys — OpenGR stopped");
                return;
            }

            if (uiGreaterRiftButton != null && uiGreaterRiftButton.AnimState == 3)
            {
                SetState(FishState.SelectGreater, "Selecting Greater Rift");
                return;
            }

            if (uiGreaterRiftButton != null && uiGreaterRiftButton.AnimState == 5 && uiAcceptButton != null && uiAcceptButton.Visible)
            {
                if (UiClickReady())
                {
                    if (!TryLeftClickUi(uiAcceptButton)) return;
                    _enteredRiftThisCycle = false;
                    if (_postEnterWatch != null) _postEnterWatch.Stop();
                    MarkStep();
                    SetState(FishState.WaitForRift, "Entering Greater Rift");
                }
                return;
            }

            if (_stateWatch != null && _stateWatch.IsRunning && _stateWatch.ElapsedMilliseconds > 2500)
                SetState(FishState.SelectGreater, "Selecting Greater Rift");
        }

        private void RunWaitForRiftState()
        {
            if (IsInGreaterRift() && !IsInTown())
            {
                _enteredRiftThisCycle = true;

                if (_postEnterWatch != null)
                    _postEnterWatch.Stop();

                SetState(FishState.CheckMap, "Checking map");
                RunCheckMapState();
                return;
            }

            if (IsJoinPartyDialogOpen() && uiJoinPartyAcceptButton != null && uiJoinPartyAcceptButton.Visible)
            {
                if (UiClickReady())
                {
                    if (!TryLeftClickUi(uiJoinPartyAcceptButton)) return;
                    MarkStep();
                }
                return;
            }

            if (_stateWatch != null &&
                _stateWatch.IsRunning &&
                _stateWatch.ElapsedMilliseconds > 6000)
            {
                if (IsGreaterRiftDialogOpen())
                    SetState(FishState.AcceptGreater, "Retrying GR accept");
                else
                    SetState(FishState.SelectGreater, "Retrying GR open");
                return;
            }
        }

        private void RunCheckMapState()
        {
            string scene = GetCurrentSceneCode();
            string orekName;

            if (IsOrekDream(out orekName))
            {
                _lastFoundMapName = string.IsNullOrEmpty(orekName) ? "Orek's Dream" : orekName;
                SetState(FishState.GoodMap, "Good map found: " + _lastFoundMapName);
                return;
            }

            if (string.IsNullOrEmpty(scene))
            {
                if (_stateWatch != null &&
                    _stateWatch.IsRunning &&
                    _stateWatch.ElapsedMilliseconds < MapReadMaxWaitMs)
                {
                    _status = "Reading map";
                    return;
                }

                BeginBadMapReset();
                return;
            }

            FishMap match = MapList.Values.FirstOrDefault(m => m.Enabled && m.Match(scene));
            if (match != null)
            {
                _lastFoundMapName = match.Name;
                SetState(FishState.GoodMap, "Good map found: " + _lastFoundMapName);
                return;
            }

            BeginBadMapReset();
        }

        private void RunGoodMapState()
        {
            if (!_goodMapAnnounced)
            {
                _lastFoundMapName = string.IsNullOrEmpty(_lastFoundMapName) ? "Good map" : _lastFoundMapName;

                _goodMapAnnounced = true;
                _goodMapEscapeSent = false;
                _goodMapTtsSent = false;
                _goodMapPauseConfirmed = false;
                _goodMapEscapeAttempts = 0;

                if (_goodMapWatch != null)
                    _goodMapWatch.Restart();

                if (_goodMapAfterEscapeWatch != null)
                    _goodMapAfterEscapeWatch.Stop();

                _status = "Good map found: " + _lastFoundMapName;
                return;
            }

            // LightningMOD speaks the good-map name before opening the pause menu.
            // Keep the same order here, but with a small configurable safety delay before TTS
            // and a separate delay between TTS and ESC.
            if (AnnounceMapName && !_goodMapTtsSent)
            {
                int ttsDelayMs = Math.Max(0, GoodMapTtsDelayBeforePauseMs);

                if (_goodMapWatch != null &&
                    _goodMapWatch.IsRunning &&
                    _goodMapWatch.ElapsedMilliseconds < ttsDelayMs)
                {
                    _status = "Announcing good map";
                    return;
                }

                if (Hud.Game.IsLoading)
                {
                    _status = "Waiting to announce good map";
                    return;
                }

                SafeSpeakGoodMapName();

                if (_goodMapAfterEscapeWatch != null)
                    _goodMapAfterEscapeWatch.Restart();

                _status = "Good map found: " + _lastFoundMapName;
                return;
            }

            if (PauseOnGoodMap && !_goodMapPauseConfirmed)
            {
                if (IsGoodMapPauseConfirmed())
                {
                    ConfirmGoodMapPause();
                    return;
                }

                int maxAttempts = Math.Max(1, GoodMapEscapeMaxAttempts);
                if (_goodMapEscapeAttempts >= maxAttempts)
                {
                    StopWithStatus("Good map pause failed: press ESC — " + _lastFoundMapName);
                    return;
                }

                bool firstAttempt = !_goodMapEscapeSent;
                bool waitAfterTts = firstAttempt && AnnounceMapName && _goodMapTtsSent;
                int waitMs = firstAttempt
                    ? Math.Max(0, waitAfterTts ? GoodMapPauseDelayAfterTtsMs : GoodMapPauseDelayMs)
                    : Math.Max(50, GoodMapEscapeRetryMs);

                IWatch waitWatch = firstAttempt
                    ? (waitAfterTts ? _goodMapAfterEscapeWatch : _goodMapWatch)
                    : _goodMapAfterEscapeWatch;

                if (waitWatch != null &&
                    waitWatch.IsRunning &&
                    waitWatch.ElapsedMilliseconds < waitMs)
                {
                    _status = firstAttempt ? "Pausing on good map" : "Retrying ESC pause";
                    return;
                }

                if (Hud.Game.IsLoading || !Hud.Window.IsForeground)
                {
                    _status = "Waiting to pause good map";
                    return;
                }

                if (!GlobalInputReady())
                {
                    _status = "Waiting for input gate";
                    return;
                }

                bool escapeSent = false;

                try
                {
                    if (!IsGoodMapPauseConfirmed())
                    {
                        escapeSent = FreeHudInput.PressEscape();
                    }
                }
                catch
                {
                    escapeSent = false;
                }

                _goodMapEscapeSent = true;
                _goodMapEscapeAttempts++;

                if (escapeSent)
                    MarkGlobalInput();

                if (_goodMapAfterEscapeWatch != null)
                    _goodMapAfterEscapeWatch.Restart();

                _status = escapeSent
                    ? "ESC pause attempt " + _goodMapEscapeAttempts.ToString() + "/" + maxAttempts.ToString()
                    : "ESC send failed " + _goodMapEscapeAttempts.ToString() + "/" + maxAttempts.ToString();

                return;
            }

            Running = false;
            StopTimersAndFlags();
            _status = string.Empty;
            _state = FishState.Idle;
        }

        private bool IsGoodMapPauseConfirmed()
        {
            try
            {
                if (Hud != null && Hud.Game != null && Hud.Game.IsPaused)
                    return true;
            }
            catch
            {
            }

            try
            {
                if (uiGameMenu != null)
                {
                    uiGameMenu.Refresh();
                    if (uiGameMenu.Visible)
                        return true;
                }

                if (uiResumeGame != null)
                {
                    uiResumeGame.Refresh();
                    if (uiResumeGame.Visible)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void ConfirmGoodMapPause()
        {
            _goodMapPauseConfirmed = true;

            if (_goodMapAfterEscapeWatch != null)
                _goodMapAfterEscapeWatch.Restart();

            _status = "Good map paused";
        }

        private void SafeSpeakGoodMapName()
        {
            if (!AnnounceMapName)
                return;

            if (_goodMapTtsSent)
                return;

            string name = string.IsNullOrEmpty(_lastFoundMapName) ? "Good map" : _lastFoundMapName;

            try
            {
                if (Hud.Sound != null && Hud.Sound.IsSpeakEnabled)
                    Hud.Sound.Speak(name);
            }
            catch
            {
            }

            _goodMapTtsSent = true;
        }
        private void BeginBadMapReset()
        {
            _badMapNeedsClose = true;
            _closeRiftClickedThisCycle = false;

            if (_closeConfirmWatch != null)
                _closeConfirmWatch.Stop();

            _fortressDebugLine = string.Empty;
            ResetFortressNativeProbeState();

            if (_fortressNativeRetryWatch != null)
                _fortressNativeRetryWatch.Stop();

            if (_fortressNativeHoverWatch != null)
                _fortressNativeHoverWatch.Stop();

            if (_fortressFileDebugWatch != null)
                _fortressFileDebugWatch.Restart();

            LogFortressDebug("BeginBadMapReset", true);

            SetState(FishState.BadMapReturnToTown, "Bad map — returning to town");
        }
        private void ResetReturnPortalRuntimeState()
        {
            if (_fortressNativeRetryWatch != null) _fortressNativeRetryWatch.Stop();
            if (_fortressNativeHoverWatch != null) _fortressNativeHoverWatch.Stop();
            if (_fortressFileDebugWatch != null) _fortressFileDebugWatch.Stop();
            _fortressDebugLine = string.Empty;
            ResetFortressNativeProbeState();
        }

        private void RunBadMapReturnToTownState()
        {
            if (!_badMapNeedsClose)
            {
                SetState(FishState.BadMapFindObelisk, "Finding obelisk");
                return;
            }

            if (IsTownContext())
            {
                ResetReturnPortalRuntimeState();
                SetState(FishState.BadMapFindOrek, "Closing bad GR");
                return;
            }

            if (IsCurrentScopedNativeReturnScene())
                LogFortressDebug("RunBadMapReturnToTownState " + GetScopedNativeReturnLabel());

            if (PortalClickReady() && ClickRiftExitPortal())
            {
                if (_portalClickWatch != null)
                    _portalClickWatch.Restart();

                MarkStep();
                _status = "Returning to town";
                return;
            }

            if (string.IsNullOrEmpty(_status) ||
                (!_status.StartsWith("Clicking portal:", StringComparison.OrdinalIgnoreCase) &&
                 !_status.StartsWith("Fortress", StringComparison.OrdinalIgnoreCase)))
            {
                _status = "Waiting for rift exit portal";
            }
        }

        private void RunBadMapFindOrekState()
        {
            if (!IsTownContext())
            {
                SetState(FishState.BadMapReturnToTown, "Returning to town");
                return;
            }

            if (!_badMapNeedsClose)
            {
                SetState(FishState.BadMapFindObelisk, "Finding obelisk");
                return;
            }

            if (uiCloseRiftButton != null && uiCloseRiftButton.Visible)
            {
                SetState(FishState.BadMapCloseRift, "Closing Greater Rift");
                return;
            }

            var orek = FindOrekActor();
            if (orek == null)
            {
                _status = "Waiting for Orek";
                return;
            }

            if (!orek.IsOnScreen)
            {
                _status = "Waiting for Orek onscreen";
                return;
            }

            if (OrekClickReady())
            {
                if (TryClickNativeActor(orek, "Orek"))
                {
                    if (_actorClickWatch != null)
                        _actorClickWatch.Restart();

                    MarkStep();
                    _status = "Clicked Orek";
                    return;
                }
            }

            _status = "Hovering Orek";
        }

        private void RunBadMapCloseRiftState()
        {
            if (!IsTownContext())
            {
                SetState(FishState.BadMapReturnToTown, "Returning to town");
                return;
            }

            if (!_badMapNeedsClose)
            {
                SetState(FishState.BadMapFindObelisk, "Finding obelisk");
                return;
            }

            bool closeButtonVisible = false;
            try
            {
                closeButtonVisible = uiCloseRiftButton != null && uiCloseRiftButton.Visible;
            }
            catch
            {
                closeButtonVisible = false;
            }

            if (closeButtonVisible)
            {
                if (_closeConfirmWatch != null)
                    _closeConfirmWatch.Stop();

                if (_closeClickWatch == null ||
                    !_closeClickWatch.IsRunning ||
                    _closeClickWatch.ElapsedMilliseconds >= CloseButtonRetryMs)
                {
                    if (UiClickReady())
                    {
                        if (!TryLeftClickUi(uiCloseRiftButton)) return;
                        _closeRiftClickedThisCycle = true;

                        if (_closeClickWatch != null)
                            _closeClickWatch.Restart();

                        MarkStep();
                        _status = "Clicked Close Rift";
                        return;
                    }
                }

                _status = "Waiting to retry Close Rift";
                return;
            }

            if (!_closeRiftClickedThisCycle)
            {
                SetState(FishState.BadMapFindOrek, "Talking to Orek");
                return;
            }

            if (_closeConfirmWatch != null && !_closeConfirmWatch.IsRunning)
                _closeConfirmWatch.Restart();

            if (_closeConfirmWatch == null ||
                _closeConfirmWatch.ElapsedMilliseconds >= CloseRiftAfterButtonGoneDelayMs)
            {
                _badMapNeedsClose = false;
                _closeRiftClickedThisCycle = false;
                SetState(FishState.BadMapFindObelisk, "Finding obelisk");
                return;
            }

            _status = "Waiting for rift close animation";
        }

        private void RunBadMapFindObeliskState()
        {
            if (!IsTownContext())
            {
                SetState(FishState.BadMapReturnToTown, "Returning to town");
                return;
            }

            if (_badMapNeedsClose || _closeRiftClickedThisCycle)
            {
                SetState(FishState.BadMapFindOrek, "Closing bad GR");
                return;
            }

            if (!HasKeyForNextRift())
            {
                StopWithStatus("No GR keys — OpenGR stopped");
                return;
            }

            if (IsGreaterRiftDialogOpen())
            {
                SetState(FishState.SelectGreater, "Selecting Greater Rift");
                return;
            }

            var obelisk = FindObeliskActor();
            if (obelisk == null)
            {
                _status = "Waiting for obelisk";
                return;
            }

            if (!obelisk.IsOnScreen)
            {
                _status = "Waiting for obelisk onscreen";
                return;
            }

            if (ObeliskClickReady())
            {
                if (TryClickNativeActor(obelisk, "Obelisk"))
                {
                    if (_actorClickWatch != null)
                        _actorClickWatch.Restart();

                    MarkStep();
                    _status = "Clicked obelisk";
                    return;
                }
            }

            _status = "Hovering obelisk";
        }

        private bool UiClickReady()
        {
            return _stepWatch == null ||
                   !_stepWatch.IsRunning ||
                   _stepWatch.ElapsedMilliseconds >= StepDelayMs;
        }

        private bool PortalClickReady()
        {
            return _portalClickWatch == null ||
                   !_portalClickWatch.IsRunning ||
                   _portalClickWatch.ElapsedMilliseconds >= PortalClickRetryMs;
        }

        private bool OrekClickReady()
        {
            return _actorClickWatch == null ||
                   !_actorClickWatch.IsRunning ||
                   _actorClickWatch.ElapsedMilliseconds >= OrekClickRetryMs;
        }

        private bool ObeliskClickReady()
        {
            return _actorClickWatch == null ||
                   !_actorClickWatch.IsRunning ||
                   _actorClickWatch.ElapsedMilliseconds >= ObeliskClickRetryMs;
        }

        private bool GlobalInputReady()
        {
            return _globalInputWatch == null ||
                   !_globalInputWatch.IsRunning ||
                   _globalInputWatch.ElapsedMilliseconds >= GlobalInputMinIntervalMs;
        }

        private void MarkGlobalInput()
        {
            if (_globalInputWatch != null)
                _globalInputWatch.Restart();
        }

        private bool TryLeftClickUi(IUiElement element)
        {
            if (!GlobalInputReady())
                return false;

            bool clicked = FreeHudInput.LeftClickUi(element);
            if (clicked)
                MarkGlobalInput();

            return clicked;
        }

        private void StopWithStatus(string status)
        {
            Running = false;
            StopTimersAndFlags();
            _status = status ?? string.Empty;
            _state = FishState.Idle;
        }

        private void SetState(FishState state, string status)
        {
            if (_state != state)
            {
                _state = state;
                if (_stateWatch != null) _stateWatch.Restart();
                if (state != FishState.BadMapCloseRift && _closeConfirmWatch != null)
                    _closeConfirmWatch.Stop();
            }

            _status = status ?? string.Empty;
        }
        private void StopTimersAndFlags()
        {
            _enteredRiftThisCycle = false;
            _goodMapAnnounced = false;
            _goodMapEscapeSent = false;
            _goodMapTtsSent = false;
            _goodMapPauseConfirmed = false;
            _goodMapEscapeAttempts = 0;
            _badMapNeedsClose = false;
            _closeRiftClickedThisCycle = false;
            if (_stepWatch != null) _stepWatch.Stop();
            if (_stateWatch != null) _stateWatch.Stop();
            if (_postEnterWatch != null) _postEnterWatch.Stop();
            if (_closeClickWatch != null) _closeClickWatch.Stop();
            if (_closeConfirmWatch != null) _closeConfirmWatch.Stop();
            if (_actorClickWatch != null) _actorClickWatch.Stop();
            if (_portalClickWatch != null) _portalClickWatch.Stop();
            if (_fortressNativeRetryWatch != null) _fortressNativeRetryWatch.Stop();
            if (_fortressNativeHoverWatch != null) _fortressNativeHoverWatch.Stop();
            if (_fortressFileDebugWatch != null) _fortressFileDebugWatch.Stop();
            _fortressDebugLine = string.Empty;
            ResetFortressNativeProbeState();
            if (_goodMapWatch != null) _goodMapWatch.Stop();
            if (_goodMapAfterEscapeWatch != null) _goodMapAfterEscapeWatch.Stop();
            if (_globalInputWatch != null) _globalInputWatch.Stop();
            if (_actorHoverWatch != null) _actorHoverWatch.Stop();
            _hoverActorAcdId = 0;
            _hoverActorPurpose = null;
        }

        private bool StepReady(int ms)
        {
            return _stepWatch == null || !_stepWatch.IsRunning || _stepWatch.ElapsedMilliseconds >= ms;
        }

        private void MarkStep()
        {
            if (_stepWatch != null) _stepWatch.Restart();
        }

        // ── Painting ──────────────────────────────────────────────────────

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip)
                return;

            try
            {
                DrawDialogHint();
                DrawCenterStatus();
                DrawFortressPortalDebug();
            }
            catch
            {
            }
        }

        private void DrawDialogHint()
        {
            _hotkeyButtonRect = RectangleF.Empty;

            if (uiGRMainPage == null || !uiGRMainPage.Visible || InfoFont == null || ButtonFont == null)
                return;

            var r = uiGRMainPage.Rectangle;

            float x = r.X + r.Width * 0.10f;
            float y = r.Y + r.Height * 0.065f;

            string prefix = "Select GR level, press ";
            string suffix = " to start OpenGR";
            string hotkeyText = _capturingHotkey ? "Press key..." : FishKey.ToString();

            var prefixLayout = InfoFont.GetTextLayout(prefix);
            var keyLayout = ButtonFont.GetTextLayout(hotkeyText);
            var suffixLayout = InfoFont.GetTextLayout(suffix);

            float buttonX = x + prefixLayout.Metrics.Width + 4;
            float buttonY = y - 2;
            float buttonW = Math.Max(42.0f, keyLayout.Metrics.Width + 14.0f);
            float buttonH = prefixLayout.Metrics.Height + 6;

            _hotkeyButtonRect = new RectangleF(buttonX, buttonY, buttonW, buttonH);

            float line1W = prefixLayout.Metrics.Width + 4 + buttonW + 5 + suffixLayout.Metrics.Width;
            float bgW = line1W + 12.0f;
            float bgH = prefixLayout.Metrics.Height + 12.0f;

            if (_hintBackground != null)
                _hintBackground.DrawRectangle(x - 6.0f, y - 5.0f, bgW, bgH);

            InfoFont.DrawText(prefixLayout, x, y);
            DrawHotkeyButton(_hotkeyButtonRect, hotkeyText);
            InfoFont.DrawText(suffixLayout, buttonX + buttonW + 5, y);
        }

        private void DrawHotkeyButton(RectangleF rect, string text)
        {
            DrawPillButton(rect, text, _capturingHotkey);
        }

        private void DrawPillButton(RectangleF rect, string text, bool green)
        {
            float radius = rect.Height * 0.5f;

            DrawRoundedRect(rect, radius, _pillOrangeBorderBrush);

            var inner = InsetRect(rect, 1.0f);
            DrawRoundedRect(inner, inner.Height * 0.5f, green ? _pillGreenBrush : _pillDarkBrush);

            var highlight = new RectangleF(
                inner.X + 1.0f,
                inner.Y + 1.0f,
                Math.Max(0.0f, inner.Width - 2.0f),
                inner.Height * 0.42f);

            DrawRoundedRect(highlight, highlight.Height * 0.5f, green ? _pillGreenLightBrush : _pillLightBrush);

            DrawCenteredButtonText(rect, text);
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
            catch
            {
                _geometryDrawFailed = true;
                _geometryDrawFailureLogged = true;
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

                gs.AddLine(new Vector2(
                    cx + radius * (float)Math.Cos(rad),
                    cy + radius * (float)Math.Sin(rad)));
            }
        }

        private void DrawCenteredButtonText(RectangleF rect, string text)
        {
            if (ButtonFont == null) return;

            var layout = ButtonFont.GetTextLayout(text ?? string.Empty);
            float tx = rect.X + rect.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float ty = rect.Y + rect.Height * 0.5f - layout.Metrics.Height * 0.5f;
            ButtonFont.DrawText(layout, tx, ty);
        }

        private void DrawCenterStatus()
        {
            if (InfoFont == null)
                return;

            if (!Running && string.IsNullOrEmpty(_status))
                return;

            // Keep the text width stable while running; only the side fireballs animate.
            string display = Running ? "Auto searching GR" : _status;

            var layout = InfoFont.GetTextLayout(display);
            float x = Hud.Window.Size.Width * 0.5f - layout.Metrics.Width * 0.5f;
            float y = Hud.Window.Size.Height * 0.5f - layout.Metrics.Height * 0.5f;
            InfoFont.DrawText(layout, x, y);

            if (Running)
            {
                // Two fireballs chasing each other around an orbit centered on the text.
                // No I/O. No per-frame allocations. Single Environment.TickCount read drives the angle.
                float centerX = x + layout.Metrics.Width * 0.5f;
                float centerY = y + layout.Metrics.Height * 0.5f;

                // Elliptical orbit that comfortably clears the text bounding box.
                float orbitRX = layout.Metrics.Width  * 0.55f + 18.0f;
                float orbitRY = Math.Max(layout.Metrics.Height * 1.4f + 8.0f, 30.0f);

                // ~1.4 s per full rotation — clearly visible motion, easy on the eyes.
                float angle = ((Environment.TickCount & int.MaxValue) % 1400) * 360.0f / 1400.0f;

                // 180° apart so they always chase each other around the orbit.
                DrawOrbitFireball(centerX, centerY, orbitRX, orbitRY, angle);
                DrawOrbitFireball(centerX, centerY, orbitRX, orbitRY, angle + 180.0f);
            }
        }

        private void DrawOrbitFireball(float ocx, float ocy, float rx, float ry, float angleDegrees)
        {
            if (_fireballHaloBrush == null || _fireballFlameBrush == null || _fireballCoreBrush == null)
                return;

            // Fading trail behind the fireball, sampled along the orbit ellipse.
            // Spans 180° backwards; combined with the second fireball (180° offset),
            // the two trails together outline the entire elliptical orbit as a fading ring.
            // Drawn FIRST so the fireball head renders on top.
            if (_fireballTrailBrushes != null)
            {
                const int trailSamples = 32;
                const float trailSpanDeg = 180.0f;
                const float trailStepDeg = trailSpanDeg / trailSamples;
                // The brightest particles right behind the head become a short outlined "comet body".
                const int cometBodySamples = 3;

                for (int i = 1; i <= trailSamples; i++)
                {
                    float trailDeg = angleDegrees - i * trailStepDeg;
                    double ta = trailDeg * Math.PI / 180.0;

                    float tx = ocx + (float)Math.Cos(ta) * rx;
                    float ty = ocy + (float)Math.Sin(ta) * ry;

                    int brushIdx = (i - 1) * FireballTrailAlphaSteps / trailSamples;
                    if (brushIdx >= FireballTrailAlphaSteps) brushIdx = FireballTrailAlphaSteps - 1;

                    // Sharper taper than before for a more comet-like body.
                    float sizeT = 1.0f - 0.65f * ((float)i / trailSamples);
                    float r = 3.5f * sizeT;

                    _fireballTrailBrushes[brushIdx].DrawEllipse(tx, ty, r, r);

                    // Cartoon outline on the comet-body particles only (closest to the head).
                    // Stroke path radius = r + 0.5 with width 1.0 → stroke covers r → r + 1.0.
                    // Inner edge of stroke aligns exactly with the colored particle's outer edge,
                    // so the outline expands OUTWARD only and never reduces the colored interior.
                    if (i <= cometBodySamples && _fireballCometOutlineBrush != null)
                        _fireballCometOutlineBrush.DrawEllipse(tx, ty, r + 0.5f, r + 0.5f);
                }
            }

            double a = angleDegrees * Math.PI / 180.0;
            float fx = ocx + (float)Math.Cos(a) * rx;
            float fy = ocy + (float)Math.Sin(a) * ry;

            // Layered glow: dim outer halo, bright orange flame, yellow-white inner core.
            _fireballHaloBrush.DrawEllipse(fx, fy, 7.5f, 7.5f);

            // Thick cartoon outline around the head. Stroke path radius 8.5 with width 2.0 →
            // stroke covers 7.5 → 9.5. Inner edge sits exactly at the halo's outer edge, so the
            // outline ONLY expands outward — the halo, flame, and core interior is untouched.
            if (_fireballOutlineBrush != null)
                _fireballOutlineBrush.DrawEllipse(fx, fy, 8.5f, 8.5f);

            _fireballFlameBrush.DrawEllipse(fx, fy, 4.5f, 4.5f);
            _fireballCoreBrush.DrawEllipse(fx, fy, 2.2f, 2.2f);
        }

        // ── Detection helpers ─────────────────────────────────────────────

        private bool IsGreaterRiftDialogOpen()
        {
            try { return uiGRMainPage != null && uiGRMainPage.Visible; }
            catch { return false; }
        }

        private bool IsJoinPartyDialogOpen()
        {
            try { return uiJoinPartyDialog != null && uiJoinPartyDialog.Visible; }
            catch { return false; }
        }

        private bool IsPartyRunBlocked()
        {
            if (AllowPartyMode)
                return false;

            try { return Hud.Game != null && Hud.Game.Players != null && Hud.Game.Players.Count() > 1; }
            catch { return false; }
        }

        private string GetPartyModeBlockedStatus()
        {
            return ShowPartyModeBlockedMessage ? "OpenGR stopped: party mode disabled" : string.Empty;
        }

        private bool HasKeyForNextRift()
        {
            try { return Hud.Game.Me != null && Hud.Game.Me.Materials.GreaterRiftKeystone > 0; }
            catch { return true; }
        }
        private bool IsInTown()
        {
            try { return Hud.Game != null && Hud.Game.IsInTown; }
            catch { return false; }
        }

        private bool IsTownContext()
        {
            if (IsInTown())
                return true;

            string scene = GetCurrentSceneCode();
            return IsKnownTownScene(scene);
        }

        private bool IsKnownTownScene(string scene)
        {
            if (string.IsNullOrEmpty(scene))
                return false;

            return scene.StartsWith("px_trout_tristram", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsInGreaterRift()
        {
            try
            {
                return Hud.Game != null &&
                       (Hud.Game.SpecialArea == SpecialArea.GreaterRift ||
                        (Hud.Game.Me != null && Hud.Game.Me.InGreaterRift));
            }
            catch
            {
                return false;
            }
        }

        private string GetCurrentSceneCode()
        {
            try { return Hud.Game.Me.Scene?.SnoScene?.Code ?? string.Empty; }
            catch { return string.Empty; }
        }

        private bool IsOrekDream(out string localizedName)
        {
            localizedName = string.Empty;

            try
            {
                if (uiMapName == null || !uiMapName.Visible)
                    return false;

                string name = uiMapName.ReadText(Encoding.UTF8, true);
                if (string.IsNullOrEmpty(name))
                    return false;

                localizedName = name;

                foreach (var orekName in OrekRiftNames)
                {
                    if (!string.IsNullOrEmpty(orekName) &&
                        name.StartsWith(orekName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                localizedName = string.Empty;
                return false;
            }
        }

        private int GetActorSno(IActor actor)
        {
            try
            {
                return actor != null && actor.SnoActor != null
                    ? (int)actor.SnoActor.Sno
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetActorCode(IActor actor)
        {
            try
            {
                return actor != null && actor.SnoActor != null && actor.SnoActor.Code != null
                    ? actor.SnoActor.Code
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool ActorCodeContains(IActor actor, string text)
        {
            if (actor == null || string.IsNullOrEmpty(text)) return false;

            string code = GetActorCode(actor);
            return !string.IsNullOrEmpty(code) &&
                   code.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsLikelyRiftExitPortalActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null)
                return false;

            int sno = GetActorSno(actor);
            string code = GetActorCode(actor);

            if (RiftExitPortalActorSnos.Contains(sno))
                return true;

            if (string.IsNullOrEmpty(code))
                return false;

            bool isGenericPortal =
                code.IndexOf("g_Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("g_portal", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isGenericPortal)
                return false;

            bool isKnownExitColor =
                code.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("Orange", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isKnownExitColor)
                return false;

            if (code.IndexOf("TownPortal", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }

        private bool IsActorScreenClickable(IActor actor)
        {
            if (actor == null)
                return false;

            try
            {
                var sc = actor.ScreenCoordinate;
                if (sc == null)
                    return false;

                const float margin = 80.0f;

                return sc.X >= -margin &&
                       sc.Y >= -margin &&
                       sc.X <= Hud.Window.Size.Width + margin &&
                       sc.Y <= Hud.Window.Size.Height + margin;
            }
            catch
            {
                return false;
            }
        }

        private bool IsBadGreaterRiftStillOpen()
        {
            try
            {
                if (Hud.Game.Actors.Any(a => a != null &&
                                             a.SnoActor != null &&
                                             (GetActorSno(a) == SNO_TOWN_GR_PORTAL ||
                                              a.SnoActor.Sno == ActorSnoEnum._x1_openworld_tiered_rifts_portal ||
                                              ActorCodeContains(a, "Tiered_Rifts_Portal"))))
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                // In town, the open GR portal is a portal whose target is not town.
                if (IsInTown() &&
                    Hud.Game.Portals.Any(p => p != null &&
                                              p.TargetArea != null &&
                                              !p.TargetArea.IsTown &&
                                              p.CentralXyDistanceToMe < 80))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private IActor FindOrekActor()
        {
            try
            {
                return Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .FirstOrDefault(a =>
                        GetActorSno(a) == SNO_OREK ||
                        a.SnoActor.Sno == ActorSnoEnum._x1_lr_nephalem ||
                        ActorCodeContains(a, "X1_LR_Nephalem"));
            }
            catch
            {
                return null;
            }
        }

        private IActor FindObeliskActor()
        {
            try
            {
                return Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .FirstOrDefault(a =>
                        GetActorSno(a) == SNO_OBELISK ||
                        a.SnoActor.Sno == ActorSnoEnum._x1_openworld_lootrunobelisk_b ||
                        ActorCodeContains(a, "OpenWorld_LootRunObelisk"));
            }
            catch
            {
                return null;
            }
        }

        private bool TryClickNativeActor(IActor actor, string purpose)
        {
            if (actor == null)
                return false;

            try
            {
                if (!actor.IsOnScreen)
                    return false;

                if (actor.IsDisabled)
                    return false;

                var sc = actor.ScreenCoordinate;
                if (sc == null)
                    return false;

                int x = (int)Math.Round(sc.X);
                int y = (int)Math.Round(sc.Y);

                if (x < 0 || y < 0 || x >= Hud.Window.Size.Width || y >= Hud.Window.Size.Height)
                    return false;

                bool sameActor =
                    _hoverActorAcdId == actor.AcdId &&
                    string.Equals(_hoverActorPurpose, purpose, StringComparison.Ordinal);

                if (!sameActor)
                {
                    if (!GlobalInputReady())
                        return false;

                    _hoverActorAcdId = actor.AcdId;
                    _hoverActorPurpose = purpose;

                    if (_actorHoverWatch != null)
                        _actorHoverWatch.Restart();

                    // Move once, then wait briefly before clicking.
                    if (FreeHudInput.MoveMouse(x, y))
                        MarkGlobalInput();

                    return false;
                }

                // Do not move the mouse every frame. The actor hover watch handles the delay.
                bool hovered = false;
                try
                {
                    hovered = actor.IsSelected;
                }
                catch
                {
                    hovered = false;
                }

                if (!hovered &&
                    _actorHoverWatch != null &&
                    _actorHoverWatch.IsRunning &&
                    _actorHoverWatch.ElapsedMilliseconds < ActorHoverBeforeClickMs)
                {
                    return false;
                }

                if (!GlobalInputReady())
                    return false;

                bool clicked = FreeHudInput.LeftClickCurrent();
                if (clicked)
                {
                    MarkGlobalInput();

                    if (_actorHoverWatch != null)
                        _actorHoverWatch.Stop();

                    _hoverActorAcdId = 0;
                    _hoverActorPurpose = null;
                }

                return clicked;
            }
            catch
            {
                return false;
            }
        }

        private bool TryClickRiftExitPortalActor(IActor actor)
        {
            if (actor == null)
                return false;

            try
            {
                if (!IsLikelyRiftExitPortalActor(actor))
                    return false;

                var sc = actor.ScreenCoordinate;
                if (sc == null)
                    return false;

                int x = (int)Math.Round(sc.X);
                int y = (int)Math.Round(sc.Y);

                // No offscreen clamping. Only click if the visible actor coordinate is inside the game window.
                if (x < 0 || y < 0 || x >= Hud.Window.Size.Width || y >= Hud.Window.Size.Height)
                    return false;

                if (!GlobalInputReady())
                    return false;

                bool clicked = FreeHudInput.LeftClick(x, y);
                if (clicked)
                    MarkGlobalInput();

                return clicked;
            }
            catch
            {
                return false;
            }
        }

        private bool IsCurrentFortressScene()
        {
            string scene = GetCurrentSceneCode();

            return !string.IsNullOrEmpty(scene) &&
                   scene.StartsWith("x1_fortress", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrentFirstbornTempleScene()
        {
            string scene = GetCurrentSceneCode();
            if (HasFirstbornTempleToken(scene))
                return true;

            string area = GetCurrentAreaCode();
            return HasFirstbornTempleToken(area);
        }

        private bool HasFirstbornTempleToken(string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;

            return code.IndexOf("p6_church", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   code.IndexOf("A2_p6_Church", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetCurrentAreaCode()
        {
            try { return Hud.Game.Me?.SnoArea?.Code ?? string.Empty; }
            catch { return string.Empty; }
        }

        private bool IsCurrentScopedNativeReturnScene()
        {
            return IsCurrentFortressScene() || IsCurrentFirstbornTempleScene();
        }

        private bool IsScopedNativeReturnEnabled()
        {
            if (IsCurrentFortressScene())
                return EnableFortressNativeReturn;

            if (IsCurrentFirstbornTempleScene())
                return EnableFirstbornTempleNativeReturn;

            return false;
        }

        private string GetScopedNativeReturnLabel()
        {
            return IsCurrentFirstbornTempleScene()
                ? "Temple native"
                : "Fortress native";
        }

        private bool IsInsideWindow(int x, int y)
        {
            return x >= 0 &&
                   y >= 0 &&
                   x < Hud.Window.Size.Width &&
                   y < Hud.Window.Size.Height;
        }

        private string GetFortressSceneDebugText()
        {
            try
            {
                var scene = Hud.Game.Me != null ? Hud.Game.Me.Scene : null;
                if (scene == null)
                    return "scene=null";

                string code = scene.SnoScene != null ? scene.SnoScene.Code : string.Empty;

                return string.Format(
                    "scene={0} sceneId={1} nav={2} posId={3} pos=({4:0},{5:0}) size=({6:0},{7:0}) z={8:0}",
                    code,
                    scene.SceneId,
                    scene.NavMeshId,
                    scene.CalculatedPosId,
                    scene.PosX,
                    scene.PosY,
                    scene.W,
                    scene.H,
                    scene.Z);
            }
            catch
            {
                return "scene debug failed";
            }
        }

        private IActor FindFortressBluePortalActor()
        {
            if (!IsCurrentScopedNativeReturnScene())
                return null;

            try
            {
                return Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .Where(a =>
                    {
                        string code = GetActorCode(a);
                        if (string.IsNullOrEmpty(code))
                            return false;

                        return
                            (code.IndexOf("g_Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             code.IndexOf("g_portal", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            code.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            code.IndexOf("TownPortal", StringComparison.OrdinalIgnoreCase) < 0;
                    })
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void UpdateFortressPortalDebug(string phase, int clickX = -1, int clickY = -1)
        {
            if (!ShowFortressPortalDebug)
                return;

            try
            {
                // Keep the optional on-screen diagnostic Temple-only so it cannot be confused with Fortress debug output.
                if (!IsCurrentFirstbornTempleScene())
                {
                    _fortressDebugLine = string.Empty;
                    return;
                }

                var portal = FindFortressBluePortalActor();

                string portalText = "portal=null";
                if (portal != null)
                {
                    string code = GetActorCode(portal);
                    int sno = GetActorSno(portal);

                    string scText = "sc=null";
                    try
                    {
                        var sc = portal.ScreenCoordinate;
                        if (sc != null)
                            scText = string.Format("sc=({0:0},{1:0})", sc.X, sc.Y);
                    }
                    catch
                    {
                        scText = "sc=err";
                    }

                    portalText = string.Format("portal={0} sno={1} {2}", code, sno, scText);
                }

                string meText = "me=null";
                try
                {
                    if (Hud.Game.Me != null && Hud.Game.Me.ScreenCoordinate != null)
                    {
                        var ms = Hud.Game.Me.ScreenCoordinate;
                        meText = string.Format("me=({0:0},{1:0})", ms.X, ms.Y);
                    }
                }
                catch
                {
                    meText = "me=err";
                }

                string clickText = clickX >= 0 && clickY >= 0
                    ? string.Format("click=({0},{1})", clickX, clickY)
                    : "click=none";

                _fortressDebugLine =
                    phase + "\r\n" +
                    GetFortressSceneDebugText() + "\r\n" +
                    portalText + " " + meText + " " + clickText +
                    " nativeAttempts=" + _fortressNativeProbeAttempts.ToString() +
                    " nativeActive=" + _fortressNativeProbeActive.ToString() +
                    " nativeLabel=" + _fortressNativeProbeLabel +
                    " nativeRaw=(" + _fortressNativeProbeRawX.ToString() + "," + _fortressNativeProbeRawY.ToString() + ")\r\n" +
                    _fortressNativeCandidateSummary;
            }
            catch
            {
                _fortressDebugLine = "fortress debug failed";
            }
        }

        private void DrawFortressPortalDebug()
        {
            if (!ShowFortressPortalDebug)
                return;

            // Optional yellow portal debug is intentionally Temple-only.
            if (!IsCurrentFirstbornTempleScene())
            {
                _fortressDebugLine = string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(_fortressDebugLine) || InfoFont == null)
                return;

            try
            {
                var layout = InfoFont.GetTextLayout(_fortressDebugLine);
                float x = Hud.Window.Size.Width * 0.36f;
                float y = Hud.Window.Size.Height * 0.12f;
                InfoFont.DrawText(layout, x, y);
            }
            catch
            {
            }
        }
        private string GetVisibleActorTagsDebug()
        {
            try
            {
                var parts = new List<string>();

                if (uiActorTag != null && uiActorTag.Visible)
                    parts.Add("uiActorTag0 visible rect=" + RectToText(uiActorTag.Rectangle));

                if (uiActorTags != null)
                {
                    for (int i = 0; i < uiActorTags.Length; i++)
                    {
                        var tag = uiActorTags[i];
                        if (tag == null || !tag.Visible)
                            continue;

                        string txt = string.Empty;
                        try { txt = tag.ReadText(Encoding.UTF8, true); }
                        catch { txt = string.Empty; }

                        parts.Add("RActorTag" + i.ToString() + " visible rect=" + RectToText(tag.Rectangle) + " text=" + txt);
                    }
                }

                return parts.Count == 0 ? "actorTags=none" : string.Join(" | ", parts.ToArray());
            }
            catch
            {
                return "actorTags=err";
            }
        }

        private string RectToText(System.Drawing.RectangleF r)
        {
            return string.Format("({0:0},{1:0},{2:0},{3:0})", r.X, r.Y, r.Width, r.Height);
        }

        private bool IsSelectedPortalActor()
        {
            try
            {
                var actor = Hud.Game.SelectedActor;
                if (actor == null || actor.SnoActor == null)
                    return false;

                if (IsLikelyRiftExitPortalActor(actor))
                    return true;

                try
                {
                    if (actor.GizmoType == GizmoType.Portal)
                        return true;
                }
                catch
                {
                }

                try
                {
                    if (actor.SnoActor.Kind == ActorKind.Portal)
                        return true;
                }
                catch
                {
                }
            }
            catch
            {
            }

            return false;
        }
        private class FortressNativeCandidate
        {
            public string Label;
            public int X;
            public int Y;
            public int RawX;
            public int RawY;
            public IWorldCoordinate WorldCoordinate;
            public double Distance;
            public int Order;
        }

        private void ResetFortressNativeProbeState()
        {
            _fortressNativeProbeActive = false;
            _fortressNativeProbeAttempts = 0;
            _fortressNativeProbeX = 0;
            _fortressNativeProbeY = 0;
            _fortressNativeProbeRawX = 0;
            _fortressNativeProbeRawY = 0;
            _fortressNativeProbeLabel = string.Empty;
            _fortressNativeCandidateSummary = string.Empty;
        }

        private bool IsFortressNativePortalHoverConfirmed()
        {
            // Fortress native return uses strict selected-actor confirmation before clicking.
            return IsSelectedPortalActor();
        }

        private int ClampScreenX(int x)
        {
            int width = Hud.Window.Size.Width;
            if (width <= 8)
                return x;

            if (x < 3) return 3;
            if (x > width - 4) return width - 4;
            return x;
        }

        private int ClampScreenY(int y)
        {
            int height = Hud.Window.Size.Height;
            if (height <= 8)
                return y;

            if (y < 3) return 3;
            if (y > height - 4) return height - 4;
            return y;
        }

        private bool TryClampScreenPoint(float rawX, float rawY, out int x, out int y)
        {
            x = 0;
            y = 0;

            try
            {
                if (Hud.Window == null || Hud.Window.Size.Width <= 8 || Hud.Window.Size.Height <= 8)
                    return false;

                if (float.IsNaN(rawX) || float.IsNaN(rawY) || float.IsInfinity(rawX) || float.IsInfinity(rawY))
                    return false;

                int rx = (int)Math.Round(rawX);
                int ry = (int)Math.Round(rawY);

                x = ClampScreenX(rx);
                y = ClampScreenY(ry);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AddFortressNativeScreenCandidates(
            List<FortressNativeCandidate> candidates,
            HashSet<string> usedPoints,
            string label,
            IScreenCoordinate sc,
            IWorldCoordinate worldCoordinate,
            double distance)
        {
            if (candidates == null || usedPoints == null || sc == null)
                return;

            try
            {
                int rawX = (int)Math.Round(sc.X);
                int rawY = (int)Math.Round(sc.Y);

                foreach (int offsetY in FortressNativeYOffsetPixels)
                {
                    int shiftedRawY = rawY + offsetY;
                    int x;
                    int y;

                    if (!TryClampScreenPoint(rawX, shiftedRawY, out x, out y))
                        continue;

                    string key = x.ToString() + ":" + y.ToString();
                    if (usedPoints.Contains(key))
                        continue;

                    usedPoints.Add(key);

                    string offsetText = offsetY == 0
                        ? string.Empty
                        : (offsetY > 0 ? " y+" : " y") + offsetY.ToString();

                    candidates.Add(new FortressNativeCandidate
                    {
                        Label = label + offsetText,
                        X = x,
                        Y = y,
                        RawX = rawX,
                        RawY = shiftedRawY,
                        WorldCoordinate = worldCoordinate,
                        Distance = distance,
                        Order = candidates.Count
                    });
                }
            }
            catch
            {
            }
        }

        private void AddFortressNativeWorldCandidates(
            List<FortressNativeCandidate> candidates,
            HashSet<string> usedPoints,
            string label,
            IWorldCoordinate worldCoordinate,
            double distance)
        {
            if (worldCoordinate == null || !worldCoordinate.IsValid)
                return;

            try
            {
                var sc = worldCoordinate.ToScreenCoordinate(false, true);
                AddFortressNativeScreenCandidates(candidates, usedPoints, label, sc, worldCoordinate, distance);
            }
            catch
            {
            }
        }

        private bool IsFortressPortalMarkerCandidate(IMarker marker)
        {
            if (marker == null || marker.SnoActor == null)
                return false;

            try
            {
                int sno = (int)marker.SnoActor.Sno;
                if (RiftExitPortalActorSnos.Contains(sno))
                    return true;

                string code = marker.SnoActor.Code;
                if (string.IsNullOrEmpty(code))
                    return false;

                bool isPortal = code.IndexOf("Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                code.IndexOf("g_Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                code.IndexOf("g_portal", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isPortal)
                    return false;

                if (code.IndexOf("TownPortal", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                return code.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       code.IndexOf("Orange", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetMarkerCode(IMarker marker)
        {
            try
            {
                return marker != null && marker.SnoActor != null && marker.SnoActor.Code != null
                    ? marker.SnoActor.Code
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private int GetMarkerSno(IMarker marker)
        {
            try
            {
                return marker != null && marker.SnoActor != null
                    ? (int)marker.SnoActor.Sno
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private double DistanceFromMe(IWorldCoordinate coordinate)
        {
            try
            {
                if (Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null || coordinate == null || !coordinate.IsValid)
                    return 99999.0d;

                return Hud.Game.Me.FloorCoordinate.XYDistanceTo(coordinate);
            }
            catch
            {
                return 99999.0d;
            }
        }

        private List<FortressNativeCandidate> BuildFortressNativeCandidates()
        {
            var candidates = new List<FortressNativeCandidate>();
            var usedPoints = new HashSet<string>();

            try
            {
                foreach (var portal in Hud.Game.Portals
                    .Where(p => p != null && p.TargetArea != null && p.TargetArea.IsTown)
                    .Where(p => p.CentralXyDistanceToMe < PortalClickMaxDistance)
                    .OrderBy(p => p.CentralXyDistanceToMe)
                    .Take(4))
                {
                    string code = GetActorCode(portal);
                    if (string.IsNullOrEmpty(code)) code = "portal";
                    string prefix = "portal:" + code;
                    double dist = portal.CentralXyDistanceToMe;

                    AddFortressNativeScreenCandidates(candidates, usedPoints, prefix + ":screen", portal.ScreenCoordinate, portal.FloorCoordinate, dist);
                    AddFortressNativeWorldCandidates(candidates, usedPoints, prefix + ":collision", portal.CollisionCoordinate, dist);
                    AddFortressNativeWorldCandidates(candidates, usedPoints, prefix + ":floor", portal.FloorCoordinate, dist);
                }
            }
            catch
            {
            }

            try
            {
                foreach (var actor in Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null && IsLikelyRiftExitPortalActor(a))
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .Take(8))
                {
                    string code = GetActorCode(actor);
                    if (string.IsNullOrEmpty(code)) code = "actor";
                    string prefix = "actor:" + code;
                    double dist = actor.NormalizedXyDistanceToMe;

                    AddFortressNativeScreenCandidates(candidates, usedPoints, prefix + ":screen", actor.ScreenCoordinate, actor.FloorCoordinate, dist);
                    AddFortressNativeWorldCandidates(candidates, usedPoints, prefix + ":collision", actor.CollisionCoordinate, dist);
                    AddFortressNativeWorldCandidates(candidates, usedPoints, prefix + ":floor", actor.FloorCoordinate, dist);
                }
            }
            catch
            {
            }

            try
            {
                foreach (var marker in Hud.Game.Markers
                    .Where(m => m != null && IsFortressPortalMarkerCandidate(m))
                    .Take(8))
                {
                    string code = GetMarkerCode(marker);
                    if (string.IsNullOrEmpty(code)) code = "marker";
                    string prefix = "marker:" + code;
                    double dist = DistanceFromMe(marker.FloorCoordinate);

                    AddFortressNativeWorldCandidates(candidates, usedPoints, prefix + ":floor", marker.FloorCoordinate, dist);
                }
            }
            catch
            {
            }

            candidates = candidates
                .OrderBy(c => c.Distance)
                .ThenBy(c => c.Order)
                .ToList();

            _fortressNativeCandidateSummary = GetFortressNativeCandidateSummary(candidates);
            return candidates;
        }

        private string GetFortressNativeCandidateSummary(List<FortressNativeCandidate> candidates)
        {
            try
            {
                if (candidates == null || candidates.Count == 0)
                    return "nativeCandidates=none";

                int limit = Math.Max(1, FortressNativeCandidateLogLimit);
                var parts = new List<string>();

                for (int i = 0; i < candidates.Count && i < limit; i++)
                {
                    var c = candidates[i];
                    parts.Add(i.ToString() + ":" + c.Label +
                              " raw=(" + c.RawX.ToString() + "," + c.RawY.ToString() + ")" +
                              " click=(" + c.X.ToString() + "," + c.Y.ToString() + ")" +
                              " dist=" + c.Distance.ToString("0.0"));
                }

                if (candidates.Count > limit)
                    parts.Add("... +" + (candidates.Count - limit).ToString() + " more");

                return "nativeCandidates=" + string.Join(" | ", parts.ToArray());
            }
            catch
            {
                return "nativeCandidates=err";
            }
        }

        private bool TryFortressNativeReturn()
        {
            if (!IsCurrentScopedNativeReturnScene() || IsTownContext())
                return false;

            if (!IsScopedNativeReturnEnabled())
                return false;

            string nativeLabel = GetScopedNativeReturnLabel();

            try
            {
                if (_fortressNativeProbeActive)
                {
                    if (_fortressNativeHoverWatch != null &&
                        _fortressNativeHoverWatch.IsRunning &&
                        _fortressNativeHoverWatch.ElapsedMilliseconds < FortressNativeHoverConfirmMs)
                    {
                        _status = nativeLabel + " hover: " + _fortressNativeProbeLabel;
                        UpdateFortressPortalDebug(_status, _fortressNativeProbeX, _fortressNativeProbeY);
                        return false;
                    }

                    if (IsFortressNativePortalHoverConfirmed())
                    {
                        if (!GlobalInputReady())
                            return false;

                        bool clicked = FreeHudInput.LeftClickCurrent();
                        if (clicked)
                        {
                            MarkGlobalInput();
                            _status = nativeLabel + " confirmed: " + _fortressNativeProbeLabel;
                            UpdateFortressPortalDebug(_status, _fortressNativeProbeX, _fortressNativeProbeY);
                            LogFortressDebug(_status, true);
                            ResetFortressNativeProbeState();
                            return true;
                        }

                        return false;
                    }

                    _fortressNativeProbeAttempts++;
                    _status = nativeLabel + " miss: " + _fortressNativeProbeLabel;
                    UpdateFortressPortalDebug(_status, _fortressNativeProbeX, _fortressNativeProbeY);
                    LogFortressDebug(_status, true);

                    _fortressNativeProbeActive = false;
                    _fortressNativeProbeLabel = string.Empty;

                    if (_fortressNativeHoverWatch != null)
                        _fortressNativeHoverWatch.Stop();

                    if (_fortressNativeRetryWatch != null)
                        _fortressNativeRetryWatch.Restart();

                    return false;
                }

                if (_fortressNativeRetryWatch != null &&
                    _fortressNativeRetryWatch.IsRunning &&
                    _fortressNativeRetryWatch.ElapsedMilliseconds < FortressNativeRetryMs)
                {
                    return false;
                }

                var candidates = BuildFortressNativeCandidates();
                if (candidates == null || candidates.Count == 0)
                {
                    _fortressNativeProbeAttempts++;
                    _status = nativeLabel + ": no candidates";
                    UpdateFortressPortalDebug(_status);
                    LogFortressDebug(_status, true);

                    if (_fortressNativeRetryWatch != null)
                        _fortressNativeRetryWatch.Restart();

                    return false;
                }

                int index = _fortressNativeProbeAttempts % candidates.Count;
                var candidate = candidates[index];

                if (candidate == null)
                    return false;

                if (!GlobalInputReady())
                    return false;

                bool moved = FreeHudInput.MoveMouse(candidate.X, candidate.Y);
                if (moved)
                {
                    MarkGlobalInput();

                    _fortressNativeProbeActive = true;
                    _fortressNativeProbeX = candidate.X;
                    _fortressNativeProbeY = candidate.Y;
                    _fortressNativeProbeRawX = candidate.RawX;
                    _fortressNativeProbeRawY = candidate.RawY;
                    _fortressNativeProbeLabel = candidate.Label;

                    if (_fortressNativeHoverWatch != null)
                        _fortressNativeHoverWatch.Restart();

                    _status = nativeLabel + " hover: " + candidate.Label;
                    UpdateFortressPortalDebug(_status, candidate.X, candidate.Y);
                    LogFortressDebug(_status, true);
                }
            }
            catch
            {
                _status = nativeLabel + " exception";
                _fortressNativeProbeActive = false;
                UpdateFortressPortalDebug(_status);
                LogFortressDebug(_status, true);
            }

            return false;
        }
        private bool ClickRiftExitPortal()
        {
            if (IsInTown())
                return false;

            if (IsCurrentScopedNativeReturnScene() && IsScopedNativeReturnEnabled())
            {
                string nativeLabel = GetScopedNativeReturnLabel();
                LogFortressDebug("ClickRiftExitPortal " + nativeLabel);

                if (TryFortressNativeReturn())
                    return true;

                UpdateFortressPortalDebug(nativeLabel + " pending");
                return false;
            }

            try
            {
                var townPortal = Hud.Game.Portals
                    .Where(p => p != null &&
                                p.IsOnScreen &&
                                p.TargetArea != null &&
                                p.TargetArea.IsTown &&
                                p.CentralXyDistanceToMe < PortalClickMaxDistance)
                    .OrderBy(p => p.CentralXyDistanceToMe)
                    .FirstOrDefault();

                if (townPortal != null)
                {
                    var sc = townPortal.ScreenCoordinate;
                    if (sc != null)
                    {
                        int x = (int)Math.Round(sc.X);
                        int y = (int)Math.Round(sc.Y);

                        if (x >= 0 && y >= 0 && x < Hud.Window.Size.Width && y < Hud.Window.Size.Height)
                        {
                            if (!GlobalInputReady())
                                return false;

                            bool clicked = FreeHudInput.LeftClick(x, y);
                            if (clicked)
                                MarkGlobalInput();

                            return clicked;
                        }

                        if (IsCurrentFortressScene())
                        {
                            // x1_fortress portal projection is known to be invalid/offscreen.
                            // Fall through to actor checks and then the deterministic fortress portal fix.
                            _status = "Fortress portal offscreen: " + GetActorCode(townPortal);
                        }
                        else
                        {
                            // Preserve old behavior for every non-fortress map.
                            return false;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (IsInTown())
                    return false;

                var portalActor = Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .Where(a => IsLikelyRiftExitPortalActor(a))
                    .Where(a =>
                    {
                        try
                        {
                            var sc = a.ScreenCoordinate;
                            return sc != null &&
                                   sc.X >= 0 &&
                                   sc.Y >= 0 &&
                                   sc.X < Hud.Window.Size.Width &&
                                   sc.Y < Hud.Window.Size.Height;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .FirstOrDefault();

                if (portalActor != null)
                {
                    _status = "Clicking portal: " + GetActorCode(portalActor);

                    if (TryClickRiftExitPortalActor(portalActor))
                        return true;
                }
                else
                {
                    _status = "Waiting for rift exit portal";
                }
            }
            catch
            {
            }

            try
            {
                var loosePortalActor = Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .Where(a =>
                    {
                        string code = GetActorCode(a);
                        if (string.IsNullOrEmpty(code))
                            return false;

                        return
                            (code.IndexOf("g_Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             code.IndexOf("g_portal", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            (code.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             code.IndexOf("Orange", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            code.IndexOf("TownPortal", StringComparison.OrdinalIgnoreCase) < 0;
                    })
                    .Where(a =>
                    {
                        try
                        {
                            var sc = a.ScreenCoordinate;
                            return sc != null &&
                                   sc.X >= 0 &&
                                   sc.Y >= 0 &&
                                   sc.X < Hud.Window.Size.Width &&
                                   sc.Y < Hud.Window.Size.Height;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .FirstOrDefault();

                if (loosePortalActor != null)
                {
                    _status = "Clicking portal: " + GetActorCode(loosePortalActor);

                    if (TryClickRiftExitPortalActor(loosePortalActor))
                        return true;
                }
                else
                {
                    _status = "Waiting for rift exit portal";
                }
            }
            catch
            {
            }

            return false;
        }
        private void LogFortressDebug(string phase, bool force = false)
        {
            if (!EnableFortressFileDebug)
                return;

            try
            {
                if (!force)
                {
                    if (_fortressFileDebugWatch != null &&
                        _fortressFileDebugWatch.IsRunning &&
                        _fortressFileDebugWatch.ElapsedMilliseconds < FortressFileDebugIntervalMs)
                    {
                        return;
                    }
                }

                if (_fortressFileDebugWatch != null)
                    _fortressFileDebugWatch.Restart();

                string line =
                    "phase=" + phase + "\r\n" +
                    "state=" + _state.ToString() +
                    " status=" + _status +
                    " inTown=" + IsInTown().ToString() +
                    " townContext=" + IsTownContext().ToString() +
                    " inGR=" + IsInGreaterRift().ToString() +
                    " specialArea=" + SafeSpecialAreaText() + "\r\n" +
                    "sceneCode=" + GetCurrentSceneCode() + "\r\n" +
                    GetFortressSceneDebugText() + "\r\n" +
                    "nativeAttempts=" + _fortressNativeProbeAttempts.ToString() +
                    " nativeActive=" + _fortressNativeProbeActive.ToString() +
                    " nativeLabel=" + _fortressNativeProbeLabel +
                    " nativeRaw=(" + _fortressNativeProbeRawX.ToString() + "," + _fortressNativeProbeRawY.ToString() + ")\r\n" +
                    "cursor=(" + Hud.Window.CursorX.ToString() + "," + Hud.Window.CursorY.ToString() + ")\r\n" +
                    "selected=" + GetSelectedActorDebug() + "\r\n" +
                    GetVisibleActorTagsDebug() + "\r\n" +
                    GetPortalsDebug() + "\r\n" +
                    GetPortalActorsDebug() + "\r\n" +
                    GetMarkersDebug() + "\r\n" +
                    "----";

                Hud.TextLog.Log("s7o_RiftFishing_FortressDebug", line, true, true);
            }
            catch
            {
            }
        }

        private string SafeSpecialAreaText()
        {
            try { return Hud.Game.SpecialArea.ToString(); }
            catch { return "err"; }
        }

        private string GetSelectedActorDebug()
        {
            try
            {
                var a = Hud.Game.SelectedActor;
                if (a == null)
                    return "selected=null";

                return string.Format(
                    "selected code={0} sno={1} kind={2} gizmo={3} dist={4:0.0} sc={5} fc={6}",
                    GetActorCode(a),
                    GetActorSno(a),
                    a.SnoActor != null ? a.SnoActor.Kind.ToString() : "null",
                    SafeGizmoTypeText(a),
                    a.NormalizedXyDistanceToMe,
                    ScreenCoordToText(a),
                    FloorCoordToText(a));
            }
            catch
            {
                return "selected=err";
            }
        }

        private string SafeGizmoTypeText(IActor a)
        {
            try { return a.GizmoType.ToString(); }
            catch { return "err"; }
        }

        private string GetPortalsDebug()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("Hud.Game.Portals:");

                foreach (var p in Hud.Game.Portals
                    .Where(p => p != null)
                    .OrderBy(p => p.CentralXyDistanceToMe)
                    .Take(12))
                {
                    string target = "target=null";
                    try
                    {
                        target = p.TargetArea != null
                            ? "targetTown=" + p.TargetArea.IsTown.ToString()
                            : "target=null";
                    }
                    catch
                    {
                        target = "target=err";
                    }

                    lines.Add(string.Format(
                        " portal code={0} sno={1} {2} dist={3:0.0} onScreen={4} sc={5} fc={6}",
                        GetActorCode(p),
                        GetActorSno(p),
                        target,
                        p.CentralXyDistanceToMe,
                        p.IsOnScreen,
                        ScreenCoordToText(p),
                        FloorCoordToText(p)));
                }

                return string.Join("\r\n", lines.ToArray());
            }
            catch
            {
                return "Hud.Game.Portals=err";
            }
        }

        private string GetPortalActorsDebug()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("Portal actors:");

                foreach (var a in Hud.Game.Actors
                    .Where(a => a != null && a.SnoActor != null)
                    .Where(a =>
                    {
                        string code = GetActorCode(a);
                        if (string.IsNullOrEmpty(code))
                            return false;

                        return code.IndexOf("Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               code.IndexOf("g_Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               code.IndexOf("g_portal", StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                    .OrderBy(a => a.NormalizedXyDistanceToMe)
                    .Take(20))
                {
                    lines.Add(string.Format(
                        " actor code={0} sno={1} kind={2} gizmo={3} dist={4:0.0} onScreen={5} sc={6} fc={7} likely={8}",
                        GetActorCode(a),
                        GetActorSno(a),
                        a.SnoActor.Kind,
                        SafeGizmoTypeText(a),
                        a.NormalizedXyDistanceToMe,
                        SafeActorOnScreen(a),
                        ScreenCoordToText(a),
                        FloorCoordToText(a),
                        IsLikelyRiftExitPortalActor(a)));
                }

                return string.Join("\r\n", lines.ToArray());
            }
            catch
            {
                return "Portal actors=err";
            }
        }

        private string GetMarkersDebug()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("Markers:");

                foreach (var m in Hud.Game.Markers
                    .Where(m => m != null)
                    .Take(20))
                {
                    lines.Add(string.Format(
                        " marker type={0} name={1} sc={2} fc={3}",
                        m.GetType().Name,
                        SafeObjectText(m),
                        MarkerScreenText(m),
                        MarkerFloorText(m)));
                }

                return string.Join("\r\n", lines.ToArray());
            }
            catch
            {
                return "Markers=err";
            }
        }

        private string ScreenCoordToText(IActor a)
        {
            try
            {
                var sc = a.ScreenCoordinate;
                if (sc == null)
                    return "sc=null";

                return string.Format("({0:0},{1:0})", sc.X, sc.Y);
            }
            catch
            {
                return "sc=err";
            }
        }

        private string FloorCoordToText(IActor a)
        {
            try
            {
                var fc = a.FloorCoordinate;
                if (fc == null || !fc.IsValid)
                    return "fc=null";

                return string.Format("({0:0.0},{1:0.0},{2:0.0})", fc.X, fc.Y, fc.Z);
            }
            catch
            {
                return "fc=err";
            }
        }

        private string SafeActorOnScreen(IActor a)
        {
            try { return a.IsOnScreen.ToString(); }
            catch { return "err"; }
        }

        private string SafeObjectText(object o)
        {
            try { return o == null ? "null" : o.ToString(); }
            catch { return "err"; }
        }

        private string MarkerScreenText(IMarker m)
        {
            try
            {
                var fc = m.FloorCoordinate;
                if (fc == null || !fc.IsValid)
                    return "sc=null";

                var sc = fc.ToScreenCoordinate(false, true);
                if (sc == null)
                    return "sc=null";

                return string.Format("({0:0},{1:0})", sc.X, sc.Y);
            }
            catch
            {
                return "sc=err";
            }
        }

        private string MarkerFloorText(IMarker m)
        {
            try
            {
                var fc = m.FloorCoordinate;
                if (fc == null || !fc.IsValid)
                    return "fc=null";

                return string.Format("({0:0.0},{1:0.0},{2:0.0})", fc.X, fc.Y, fc.Z);
            }
            catch
            {
                return "fc=err";
            }
        }

        private string GetEnabledMapText()
        {
            var names = MapList.Values
                .Where(m => m.Enabled)
                .Select(m => m.Name)
                .ToList();

            names.Add("Orek's Dream");

            return string.Join(", ", names.ToArray());
        }

        private bool IsMouseInside(RectangleF rect)
        {
            int x, y;
            if (!TryGetCursorPos(out x, out y))
                return false;

            return x >= rect.Left && x <= rect.Right &&
                   y >= rect.Top && y <= rect.Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        private static bool TryGetCursorPos(out int x, out int y)
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                x = p.X;
                y = p.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        // ── Map list ──────────────────────────────────────────────────────

        private void BuildMaps()
        {
            AddMap(1,  "Halls of Agony",           "a1dun_leor");
            AddMap(2,  "Desolate Sands",           "caout_boneyard_");
            AddMap(3,  "Flooded Cave",             "a2dun_cave_flooded");
            AddMap(4,  "Cave of the Betrayer",     "a2dun_cave");
            AddMap(5,  "Tidal Cave",               "px_cave_a");
            AddMap(6,  "Cave",                     "trdun_cave");
            AddMap(7,  "Winding Cave",             "x1_bogcave");
            AddMap(8,  "Caverns of Araneae",       "a2dun_spider");
            AddMap(9,  "Fields of Misery",         "px_tristramfields_");
            AddMap(10, "Vault of the Assassin",    "a2dun_zolt_random");
            AddMap(11, "Archives of Zultun Kulle", "a2dun_zolt");
            AddMap(12, "Hell Rift",                "a4dun_hellportal");
            AddMap(13, "Hell Rift",                "a3dun_crater_e_dead_end");
            AddMap(14, "Hell Rift",                "a3dun_crater_s_dead_end");
            AddMap(15, "Arreat Crater",            "a3dun_crater");
            AddMap(16, "Icefall Cave",             "a3dun_icecaves");
            AddMap(17, "The Keep Depths",          "a3dun_keep");
            AddMap(18, "The Silver Spire",         "a4dun_spire_corrupt");
            AddMap(19, "Greyhollow Island",        "p4_forest_coast_border");
            AddMap(20, "Eternal Woods",            "p4_forest_snow_border");
            AddMap(21, "Temple of the Firstborn",  "p6_church");
            AddMap(22, "Shrouded Moors",           "p6_moor");
            AddMap(23, "Desert",                   "px_desert_120_border");
            AddMap(24, "The Festering Woods",      "px_festeringwoods");
            AddMap(25, "Cathedral",                "trdun_cath");
            AddMap(26, "Crypt",                    "trdun_crypt");
            AddMap(27, "Plague Tunnels",           "x1_abattoir");
            AddMap(28, "Ruins of Corvus",          "x1_catacombs");
            AddMap(29, "Pandemonium Fortress",     "x1_fortress");
            AddMap(30, "Battlefields of Eternity", "x1_pand_ext_120_edge");
            AddMap(31, "Realm of the Banished",    "x1_pand_hexmaze");
            AddMap(32, "Briarthorn Cemetery",      "x1_westm_graveyard_");
            AddMap(33, "Westmarch Heights",        "x1_westm", "fire");
            AddMap(34, "Westmarch Commons",        "x1_westm");
        }

        private void AddMap(uint id, string name, string prefix, string suffix = "", bool enabled = false)
        {
            MapList[id] = new FishMap(id, name, prefix, suffix, enabled);
        }

        public class FishMap
        {
            public uint Id { get; private set; }
            public string Name { get; private set; }
            public string ScenePrefix { get; private set; }
            public string SceneSuffix { get; private set; }
            public bool Enabled { get; set; }

            public FishMap(uint id, string name, string prefix, string suffix = "", bool enabled = false)
            {
                Id = id;
                Name = name;
                ScenePrefix = prefix;
                SceneSuffix = suffix;
                Enabled = enabled;
            }

            public bool Match(string scene)
            {
                if (string.IsNullOrEmpty(scene)) return false;
                if (string.IsNullOrEmpty(ScenePrefix)) return false;
                if (!scene.StartsWith(ScenePrefix, StringComparison.OrdinalIgnoreCase)) return false;
                return string.IsNullOrEmpty(SceneSuffix) ||
                       scene.EndsWith(SceneSuffix, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ── FreeHUD-safe input wrapper ─────────────────────────────────────

        private static class FreeHudInput
        {
            private const uint INPUT_MOUSE = 0;
            private const uint INPUT_KEYBOARD = 1;

            private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            private const uint MOUSEEVENTF_LEFTUP = 0x0004;
            private const uint MOUSEEVENTF_MOVE = 0x0001;
            private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
            private const uint KEYEVENTF_KEYUP = 0x0002;

            private const int SM_CXSCREEN = 0;
            private const int SM_CYSCREEN = 1;

            private const ushort VK_ESCAPE = 0x1B;

            [StructLayout(LayoutKind.Sequential)]
            private struct INPUT
            {
                public uint type;
                public INPUTUNION U;
            }

            [StructLayout(LayoutKind.Explicit)]
            private struct INPUTUNION
            {
                [FieldOffset(0)] public MOUSEINPUT mi;
                [FieldOffset(0)] public KEYBDINPUT ki;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
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

            [DllImport("user32.dll")]
            private static extern int GetSystemMetrics(int nIndex);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            public static bool MoveMouse(int x, int y)
            {
                int sw = GetSystemMetrics(SM_CXSCREEN);
                int sh = GetSystemMetrics(SM_CYSCREEN);

                if (sw <= 0 || sh <= 0)
                    return false;

                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x > sw - 1) x = sw - 1;
                if (y > sh - 1) y = sh - 1;

                int ax = (int)Math.Round(x * 65535.0 / Math.Max(1, sw - 1));
                int ay = (int)Math.Round(y * 65535.0 / Math.Max(1, sh - 1));

                var input = new INPUT[1];
                input[0].type = INPUT_MOUSE;
                input[0].U.mi.dx = ax;
                input[0].U.mi.dy = ay;
                input[0].U.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

                return SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) == (uint)input.Length;
            }

            public static bool LeftClickCurrent()
            {
                var input = new INPUT[2];
                input[0].type = INPUT_MOUSE;
                input[0].U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
                input[1].type = INPUT_MOUSE;
                input[1].U.mi.dwFlags = MOUSEEVENTF_LEFTUP;

                return SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) == (uint)input.Length;
            }

            public static bool LeftClick(int x, int y)
            {
                if (!MoveMouse(x, y))
                    return false;

                return LeftClickCurrent();
            }

            public static bool LeftClickUi(IUiElement element)
            {
                if (element == null) return false;

                var r = element.Rectangle;
                int x = (int)Math.Round(r.Left + r.Width * 0.5f);
                int y = (int)Math.Round(r.Top + r.Height * 0.5f);

                return LeftClick(x, y);
            }

            public static bool PressEscape()
            {
                return SendKeyPress(VK_ESCAPE);
            }

            private static bool SendKeyPress(ushort vk)
            {
                var input = new INPUT[2];
                input[0].type = INPUT_KEYBOARD;
                input[0].U.ki.wVk = vk;
                input[0].U.ki.dwFlags = 0;
                input[1].type = INPUT_KEYBOARD;
                input[1].U.ki.wVk = vk;
                input[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
                return SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) == (uint)input.Length;
            }
        }
    }
}
