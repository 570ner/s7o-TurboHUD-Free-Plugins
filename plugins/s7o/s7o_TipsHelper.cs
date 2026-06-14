using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_TipsHelper : BasePlugin, IInGameWorldPainter, IInGameTopPainter, IAfterCollectHandler, INewAreaHandler, ITransparentCollection, IKeyEventHandler
    {
        private const int AncientRank = 1;
        private const int PrimalRank = 2;
        private const int PlayerMarkerSlots = 4;

        public bool VisualHelpersEnabled { get; set; } = true;

        public bool ShowHealthGlobeDots { get; set; } = true;
        public bool ShowRiftProgressOrbDots { get; set; } = true;
        public bool ShowAncientItems { get; set; } = true;
        public bool ShowPrimalItems { get; set; } = true;
        public bool ShowItemGroundCircles { get; set; } = true;
        public bool ShowItemMinimapMarkers { get; set; } = true;
        public bool ShowItemScreenEdgeArrows { get; set; } = true;
        public bool ShowItemAlertText { get; set; } = true;
        public bool ShowItemAlertTextAbovePlayer { get; set; } = true;
        public bool ShowItemAlertTextNearMinimap { get; set; } = true;
        public bool ShowItemAlertDirectionArrow { get; set; } = true;
        public bool ShowAncientItemArrows { get; set; } = true;
        public bool ShowPrimalItemArrows { get; set; } = true;
        public bool ShowAncientItemAlertText { get; set; } = true;
        public bool ShowPrimalItemAlertText { get; set; } = true;
        public bool ShowPlayerMarkers { get; set; } = true;
        public bool ShowPlayerGroundCircles { get; set; } = true;
        public bool ShowPlayerMinimapDots { get; set; } = true;
        public bool ShowPlayerPortraitMarker { get; set; } = true;

        public int HealthGlobeColorR { get; set; } = 255;
        public int HealthGlobeColorG { get; set; } = 25;
        public int HealthGlobeColorB { get; set; } = 25;
        public int RiftOrbColorR { get; set; } = 185;
        public int RiftOrbColorG { get; set; } = 70;
        public int RiftOrbColorB { get; set; } = 255;
        public int AncientColorR { get; set; } = 185;
        public int AncientColorG { get; set; } = 70;
        public int AncientColorB { get; set; } = 255;
        public int PrimalColorR { get; set; } = 255;
        public int PrimalColorG { get; set; } = 35;
        public int PrimalColorB { get; set; } = 35;
        public int AncientTextColorR { get; set; } = 185;
        public int AncientTextColorG { get; set; } = 70;
        public int AncientTextColorB { get; set; } = 255;
        public int PrimalTextColorR { get; set; } = 255;
        public int PrimalTextColorG { get; set; } = 35;
        public int PrimalTextColorB { get; set; } = 35;

        public int Player1ColorR { get; set; } = 185;
        public int Player1ColorG { get; set; } = 70;
        public int Player1ColorB { get; set; } = 255;
        public int Player2ColorR { get; set; } = 255;
        public int Player2ColorG { get; set; } = 35;
        public int Player2ColorB { get; set; } = 35;
        public int Player3ColorR { get; set; } = 35;
        public int Player3ColorG { get; set; } = 225;
        public int Player3ColorB { get; set; } = 85;
        public int Player4ColorR { get; set; } = 70;
        public int Player4ColorG { get; set; } = 150;
        public int Player4ColorB { get; set; } = 255;

        public float HealthGlobeDotRadius { get; set; } = 0.80f;
        public float RiftOrbDotRadius { get; set; } = 1.05f;
        public float AncientGroundEllipseRadiusX { get; set; } = 34.0f;
        public float AncientGroundEllipseRadiusY { get; set; } = 12.0f;
        public float PrimalGroundEllipseRadiusX { get; set; } = 36.0f;
        public float PrimalGroundEllipseRadiusY { get; set; } = 12.5f;
        public int ItemGroundCirclePulseMs { get; set; } = 620;
        public float ItemGroundCirclePulseScale { get; set; } = 0.16f;
        public int ItemGroundCircleAlpha { get; set; } = 255;
        public int ItemGroundCircleOutlineAlpha { get; set; } = 245;
        public float ItemGroundCircleStrokeWidth { get; set; } = 6.5f;
        public float ItemGroundCircleOutlineWidth { get; set; } = 9.5f;
        public float AncientMinimapTriangleRadius { get; set; } = 13.0f;
        public float PrimalMinimapTriangleRadius { get; set; } = 15.0f;
        public bool ClampItemMinimapMarkers { get; set; } = true;
        public int ItemMarkerMaxAgeMs { get; set; } = 900000;
        public float ItemPickupCleanupDistance { get; set; } = 20.0f;
        public int ItemTownMissingCleanupDelayMs { get; set; } = 350;
        public float ItemOffscreenRestoreMatchDistance { get; set; } = 6.0f;

        public float ItemScreenEdgeArrowRadius { get; set; } = 13.0f;
        public float ItemScreenEdgeArrowMargin { get; set; } = 34.0f;
        public float ItemScreenEdgeInteriorMargin { get; set; } = 44.0f;
        public float ItemScreenEdgeArrowBottomUiReservedFrac { get; set; } = 0.18f;
        public float ItemScreenEdgeArrowBottomUiReservedPixels { get; set; } = 0.0f;

        public int ItemAlertTextHoldMs { get; set; } = 5000;
        public int ItemAlertTextFadeMs { get; set; } = 1500;
        public int ItemAlertTextMaxLines { get; set; } = 4;
        public int ItemAlertTextMaxCharacters { get; set; } = 52;
        public float ItemAlertTextBaseSize { get; set; } = 9.0f;
        public float ItemAlertTextPeakSize { get; set; } = 18.0f;
        public int AncientAlertTextHoldMs { get; set; } = 5000;
        public int PrimalAlertTextHoldMs { get; set; } = 5000;
        public float AncientAlertTextSize { get; set; } = 9.0f;
        public float PrimalAlertTextSize { get; set; } = 9.0f;
        // Minimap toast text stays compact; HUD Menu text-size controls only affect above-player toast text.
        public float ItemAlertMinimapTextSize { get; set; } = 9.0f;
        public int ItemAlertTextPopMs { get; set; } = 560;
        public int ItemAlertTextOutlinePixels { get; set; } = 4;
        public float ItemAlertTextLineHeight { get; set; } = 20.0f;
        public float ItemAlertPlayerXOffset { get; set; } = 0.0f;
        public float ItemAlertPlayerStartYOffset { get; set; } = -115.0f;
        public float ItemAlertPlayerSettledYOffset { get; set; } = -205.0f;
        public float ItemAlertMinimapXOffset { get; set; } = 0.0f;
        public float ItemAlertMinimapStartYFrac { get; set; } = 0.50f;
        public float ItemAlertMinimapSettledYFrac { get; set; } = 0.10f;
        public float ItemAlertMinimapSettledYOffset { get; set; } = 6.0f;
        public float ItemAlertArrowLength { get; set; } = 27.0f;
        public float ItemAlertArrowHeadSize { get; set; } = 10.5f;
        public float ItemAlertArrowYOffset { get; set; } = 30.0f;
        public float ItemAlertArrowPulsePixels { get; set; } = 4.0f;
        public int ItemAlertArrowPulseMs { get; set; } = 420;
        public int ItemAlertArrowAlpha { get; set; } = 255;
        public int ItemAlertArrowHighlightAlpha { get; set; } = 135;
        public int ItemAlertArrowHighlightBoost { get; set; } = 70;

        public bool EnablePlayerMarkerHotkey { get; set; } = true;
        public Key PlayerMarkerHotkeyKey { get; set; } = Key.F7;
        public IKeyEvent PlayerMarkerHotkey { get; private set; }
        public float PlayerCircleScreenRadiusX { get; set; } = 30.0f;
        public float PlayerCircleScreenRadiusY { get; set; } = 8.5f;
        public float PlayerCircleScreenYOffset { get; set; } = 17.0f;
        public int PlayerCircleFillAlpha { get; set; } = 86;
        public int PlayerCircleOutlineAlpha { get; set; } = 225;
        public int PlayerCircleBlackOutlineAlpha { get; set; } = 235;
        public float PlayerCircleOutlineWidth { get; set; } = 2.0f;
        public float PlayerCircleBlackOutlineWidth { get; set; } = 4.0f;
        public float PlayerMinimapDotRadius { get; set; } = 7.0f;
        public bool ClampPlayerMinimapDots { get; set; } = true;
        public float PlayerPortraitDotRadius { get; set; } = 4.5f;
        public float PlayerPortraitOffsetX { get; set; } = 7.0f;
        public float PlayerPortraitOffsetY { get; set; } = 28.0f;
        public float PlayerPortraitArrowLength { get; set; } = 11.5f;
        public float PlayerPortraitArrowHeadSize { get; set; } = 5.2f;

        public IBrush HealthGlobeBrush { get; private set; }
        public IBrush RiftOrbBrush { get; private set; }
        public IBrush AncientRingBrush { get; private set; }
        public IBrush AncientRingOutlineBrush { get; private set; }
        public IBrush PrimalRingBrush { get; private set; }
        public IBrush PrimalRingOutlineBrush { get; private set; }
        public IBrush AncientMapBrush { get; private set; }
        public IBrush AncientMapOutlineBrush { get; private set; }
        public IBrush PrimalMapBrush { get; private set; }
        public IBrush PrimalMapOutlineBrush { get; private set; }
        public IBrush AncientScreenArrowBrush { get; private set; }
        public IBrush AncientScreenArrowOutlineBrush { get; private set; }
        public IBrush PrimalScreenArrowBrush { get; private set; }
        public IBrush PrimalScreenArrowOutlineBrush { get; private set; }
        public IBrush AncientAlertArrowBrush { get; private set; }
        public IBrush AncientAlertArrowOutlineBrush { get; private set; }
        public IBrush AncientAlertArrowHighlightBrush { get; private set; }
        public IBrush PrimalAlertArrowBrush { get; private set; }
        public IBrush PrimalAlertArrowOutlineBrush { get; private set; }
        public IBrush PrimalAlertArrowHighlightBrush { get; private set; }
        public IFont[] AncientFadeFonts { get; private set; }
        public IFont[] PrimalFadeFonts { get; private set; }
        public IFont[] AncientOutlineFadeFonts { get; private set; }
        public IFont[] PrimalOutlineFadeFonts { get; private set; }
        public IFont[] AncientPopFonts { get; private set; }
        public IFont[] PrimalPopFonts { get; private set; }
        public IFont[] AncientOutlinePopFonts { get; private set; }
        public IFont[] PrimalOutlinePopFonts { get; private set; }
        public IFont[] AncientMinimapFadeFonts { get; private set; }
        public IFont[] PrimalMinimapFadeFonts { get; private set; }
        public IFont[] AncientMinimapOutlineFadeFonts { get; private set; }
        public IFont[] PrimalMinimapOutlineFadeFonts { get; private set; }
        public IFont[] AncientMinimapPopFonts { get; private set; }
        public IFont[] PrimalMinimapPopFonts { get; private set; }
        public IFont[] AncientMinimapOutlinePopFonts { get; private set; }
        public IFont[] PrimalMinimapOutlinePopFonts { get; private set; }
        public IBrush PlayerCircleBlackBrush { get; private set; }
        public IBrush PlayerDotOutlineBrush { get; private set; }
        public IBrush[] PlayerFillBrushes { get; private set; }
        public IBrush[] PlayerOutlineBrushes { get; private set; }
        public IBrush[] PlayerDotBrushes { get; private set; }

        private readonly Dictionary<string, ItemMarker> _items = new Dictionary<string, ItemMarker>();
        private readonly List<PlayerMark> _players = new List<PlayerMark>();
        private long _alertSequence;
        private long _latestAbovePlayerSequence;
        private string _resourceSignature;
        private RotatingTriangleShapePainter _trianglePainter;
        private StandardPingRadiusTransformator _mapPulse;

        private static readonly LabelRule[] ExactLabels =
        {
            R("Ring", "ring"), R("Amulet", "amulet"), R("Mighty Belt", "beltbarbarian"), R("Voodoo Mask", "voodoomask"),
            R("Spirit Stone", "spiritstone", "spiritstonemonk"), R("Wizard Hat", "wizardhat"), R("Cloak", "cloak"),
            R("Crusader Shield", "crusadershield"), R("Phylactery", "necromanceroffhand"), R("Belt", "belt", "genericbelt"),
            R("Chest", "genericchestarmor", "chestarmor"), R("Helm", "generichelm", "helm"), R("Shoulders", "shoulders"),
            R("Gloves", "gloves"), R("Bracers", "bracers"), R("Boots", "boots"), R("Pants", "legs"),
            R("Shield", "shield"), R("Quiver", "quiver"), R("Mojo", "mojo"), R("Source", "orb"),
            R("Mighty Weapon", "mightyweapon1h"), R("2H Mighty Weapon", "mightyweapon2h"), R("Flail", "flail", "flail1h"),
            R("2H Flail", "flail2h"), R("Scythe", "scythe", "scythe1h"), R("2H Scythe", "scythe2h"),
            R("Ceremonial Knife", "ceremonialdagger"), R("Fist Weapon", "fistweapon", "battlecestus", "greatertalons", "wristsword"),
            R("Daibo", "combatstaff"), R("Hand Crossbow", "handxbow", "repeatingcrossbow"), R("Crossbow", "crossbow", "ballista"),
            R("Bow", "bow", "hydrabow"), R("2H Axe", "axe2h"), R("Axe", "axe", "flyingaxe"), R("2H Mace", "mace2h"),
            R("Mace", "mace"), R("2H Sword", "sword2h", "colossusblade"), R("Sword", "sword", "championsword"),
            R("Dagger", "dagger", "boneknife", "legendspike"), R("Spear", "spear", "hyperionspear"), R("Polearm", "polearm"),
            R("Staff", "staff", "archonstaff"), R("Wand", "wand", "gravewand", "swirlingcrystal")
        };

        private static readonly LabelRule[] SearchLabels =
        {
            R("Mighty Belt", "mightybelt", "barbbelt", "beltbarbarian", "belt_barbarian"), R("Crusader Shield", "crusadershield", "crusader shield"),
            R("Phylactery", "necromanceroffhand", "necromancer offhand", "phylactery"), R("Voodoo Mask", "voodoomask", "voodoo mask"),
            R("Spirit Stone", "spiritstone", "spirit stone"), R("Wizard Hat", "wizardhat", "wizard hat"), R("Cloak", "cloak"),
            R("Quiver", "quiver"), R("Mojo", "mojo"), R("Source", "source", "wizardorb", "wizard orb", "orb"), R("Shield", "shield"),
            R("2H Mighty Weapon", "mightyweapon2h", "mightyweapon_2h", "2h mighty", "twohandedmighty", "two-handed mighty"),
            R("Mighty Weapon", "mightyweapon1h", "mightyweapon_1h", "mightyweapon", "mighty weapon"),
            R("2H Flail", "flail2h", "flail_2h", "2h flail", "twohandedflail", "two-handed flail"), R("Flail", "flail1h", "flail_1h", "flail"),
            R("2H Scythe", "scythe2h", "scythe_2h", "2h scythe", "twohandedscythe", "two-handed scythe"), R("Scythe", "scythe1h", "scythe_1h", "scythe"),
            R("Ceremonial Knife", "ceremonialdagger", "ceremonial dagger", "ceremonialknife", "ceremonial knife"), R("Fist Weapon", "fistweapon", "fist weapon"),
            R("Daibo", "combatstaff", "daibo"), R("Hand Crossbow", "handxbow", "handcrossbow", "hand crossbow"), R("Crossbow", "crossbow", "xbow"), R("Bow", "bow"),
            R("2H Axe", "axe2h", "axe_2h", "2h axe", "twohandedaxe", "two-handed axe"), R("2H Mace", "mace2h", "mace_2h", "2h mace", "twohandedmace", "two-handed mace"),
            R("2H Sword", "sword2h", "sword_2h", "2h sword", "twohandedsword", "two-handed sword"), R("Polearm", "polearm"), R("Staff", "staff"),
            R("Wand", "wand"), R("Dagger", "dagger"), R("Spear", "spear"), R("Axe", "axe1h", "axe_1h", "onehandedaxe", "one-handed axe", "axe"),
            R("Mace", "mace1h", "mace_1h", "onehandedmace", "one-handed mace", "mace"), R("Sword", "sword1h", "sword_1h", "onehandedsword", "one-handed sword", "sword"),
            R("Helm", "helm", "helmet"), R("Chest", "chestarmor", "chest armor", "chest"), R("Shoulders", "shoulder"), R("Gloves", "glove", "hands"),
            R("Bracers", "bracer"), R("Belt", "belt", "waist"), R("Pants", "pants", "legs"), R("Boots", "boots", "feet"), R("Ring", "ring"), R("Amulet", "amulet", "neck")
        };

        public s7o_TipsHelper()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            SetPlayerMarkerHotkey(PlayerMarkerHotkeyKey);
            RebuildResources(true);
            ClearRuntime();
        }

        public void SetPlayerMarkerHotkey(Key key)
        {
            if (key == Key.Unknown)
                return;

            PlayerMarkerHotkeyKey = key;
            try
            {
                PlayerMarkerHotkey = Hud != null && Hud.Input != null
                    ? Hud.Input.CreateKeyEvent(true, PlayerMarkerHotkeyKey, false, false, false)
                    : null;
            }
            catch
            {
                PlayerMarkerHotkey = null;
            }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ClearItems();
            if (newGame)
                _players.Clear();
        }

        public void AfterCollect()
        {
            if (!IsGameReady())
            {
                ClearRuntime();
                return;
            }

            if (!VisualHelpersEnabled)
            {
                ClearItems();
                return;
            }

            RebuildResources(false);
            UpdateItemMarkers();
            PurgePlayerMarks();
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (!VisualHelpersEnabled || !EnablePlayerMarkerHotkey || keyEvent == null || !keyEvent.IsPressed || !IsGameReady())
                return;

            bool hotkeyMatches = PlayerMarkerHotkey != null
                ? PlayerMarkerHotkey.Matches(keyEvent)
                : keyEvent.Key == PlayerMarkerHotkeyKey;

            if (!hotkeyMatches)
                return;

            IPlayer player;
            if (TryGetHoveredPortraitPlayer(out player))
                TogglePlayerMark(player);
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (!IsGameReady() || !VisualHelpersEnabled)
                return;

            RebuildResources(false);

            if (layer == WorldLayer.Ground)
            {
                PaintPickupDots();
                PaintItemGroundCircles();
                PaintPlayerGroundCircles();
            }
            else if (layer == WorldLayer.Map)
            {
                PaintItemMinimapMarkers();
                PaintPlayerMinimapDots();
            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (!IsGameReady() || !VisualHelpersEnabled || Hud.Render.UiHidden || clipState != ClipState.BeforeClip || IsFullMapOpen())
                return;

            RebuildResources(false);
            PaintItemScreenEdgeArrows();
            PaintItemAlerts();
            PaintPlayerPortraitMarkers();
        }

        public IEnumerable<ITransparent> GetTransparents()
        {
            foreach (var brush in Brushes(HealthGlobeBrush, RiftOrbBrush, AncientRingBrush, AncientRingOutlineBrush, PrimalRingBrush, PrimalRingOutlineBrush,
                AncientMapBrush, AncientMapOutlineBrush, PrimalMapBrush, PrimalMapOutlineBrush, AncientScreenArrowBrush, AncientScreenArrowOutlineBrush,
                PrimalScreenArrowBrush, PrimalScreenArrowOutlineBrush, AncientAlertArrowBrush, AncientAlertArrowOutlineBrush, AncientAlertArrowHighlightBrush,
                PrimalAlertArrowBrush, PrimalAlertArrowOutlineBrush, PrimalAlertArrowHighlightBrush, PlayerCircleBlackBrush, PlayerDotOutlineBrush))
                yield return brush;

            foreach (var brush in Brushes(PlayerFillBrushes)) yield return brush;
            foreach (var brush in Brushes(PlayerOutlineBrushes)) yield return brush;
            foreach (var brush in Brushes(PlayerDotBrushes)) yield return brush;
            foreach (var font in Fonts(AncientFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientPopFonts)) yield return font;
            foreach (var font in Fonts(PrimalPopFonts)) yield return font;
            foreach (var font in Fonts(AncientOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(PrimalOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapOutlineFadeFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapPopFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapPopFonts)) yield return font;
            foreach (var font in Fonts(AncientMinimapOutlinePopFonts)) yield return font;
            foreach (var font in Fonts(PrimalMinimapOutlinePopFonts)) yield return font;
        }

        private void RebuildResources(bool force)
        {
            var sig = BuildResourceSignature();
            if (!force && sig == _resourceSignature)
                return;

            _resourceSignature = sig;
            HealthGlobeBrush = Hud.Render.CreateBrush(235, C(HealthGlobeColorR), C(HealthGlobeColorG), C(HealthGlobeColorB), 0);
            RiftOrbBrush = Hud.Render.CreateBrush(235, C(RiftOrbColorR), C(RiftOrbColorG), C(RiftOrbColorB), 0);
            AncientRingBrush = Hud.Render.CreateBrush(C(ItemGroundCircleAlpha), C(AncientColorR), C(AncientColorG), C(AncientColorB), ItemGroundCircleStrokeWidth);
            AncientRingOutlineBrush = Hud.Render.CreateBrush(C(ItemGroundCircleOutlineAlpha), 0, 0, 0, ItemGroundCircleOutlineWidth);
            PrimalRingBrush = Hud.Render.CreateBrush(C(ItemGroundCircleAlpha), C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), ItemGroundCircleStrokeWidth);
            PrimalRingOutlineBrush = Hud.Render.CreateBrush(C(ItemGroundCircleOutlineAlpha), 0, 0, 0, ItemGroundCircleOutlineWidth);
            AncientMapBrush = Hud.Render.CreateBrush(255, C(AncientColorR), C(AncientColorG), C(AncientColorB), 4);
            AncientMapOutlineBrush = Hud.Render.CreateBrush(180, 0, 0, 0, 1);
            PrimalMapBrush = Hud.Render.CreateBrush(255, C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 4);
            PrimalMapOutlineBrush = Hud.Render.CreateBrush(180, 0, 0, 0, 1);
            AncientScreenArrowBrush = Hud.Render.CreateBrush(255, C(AncientColorR), C(AncientColorG), C(AncientColorB), 4);
            AncientScreenArrowOutlineBrush = Hud.Render.CreateBrush(210, 0, 0, 0, 6.5f);
            PrimalScreenArrowBrush = Hud.Render.CreateBrush(255, C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 4);
            PrimalScreenArrowOutlineBrush = Hud.Render.CreateBrush(210, 0, 0, 0, 6.5f);
            AncientAlertArrowBrush = Hud.Render.CreateBrush(C(ItemAlertArrowAlpha), C(AncientColorR), C(AncientColorG), C(AncientColorB), 0);
            AncientAlertArrowOutlineBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 0);
            AncientAlertArrowHighlightBrush = Hud.Render.CreateBrush(C(ItemAlertArrowHighlightAlpha), C(AncientColorR + ItemAlertArrowHighlightBoost), C(AncientColorG + ItemAlertArrowHighlightBoost), C(AncientColorB + ItemAlertArrowHighlightBoost), 0);
            PrimalAlertArrowBrush = Hud.Render.CreateBrush(C(ItemAlertArrowAlpha), C(PrimalColorR), C(PrimalColorG), C(PrimalColorB), 0);
            PrimalAlertArrowOutlineBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 0);
            PrimalAlertArrowHighlightBrush = Hud.Render.CreateBrush(C(ItemAlertArrowHighlightAlpha), C(PrimalColorR + ItemAlertArrowHighlightBoost), C(PrimalColorG + ItemAlertArrowHighlightBoost), C(PrimalColorB + ItemAlertArrowHighlightBoost), 0);
            AncientFadeFonts = CreateFadeFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), AncientAlertTextSize);
            PrimalFadeFonts = CreateFadeFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), PrimalAlertTextSize);
            AncientOutlineFadeFonts = CreateFadeFonts(0, 0, 0, AncientAlertTextSize);
            PrimalOutlineFadeFonts = CreateFadeFonts(0, 0, 0, PrimalAlertTextSize);
            AncientPopFonts = CreatePopFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), AncientAlertTextSize);
            PrimalPopFonts = CreatePopFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), PrimalAlertTextSize);
            AncientOutlinePopFonts = CreatePopFonts(0, 0, 0, AncientAlertTextSize);
            PrimalOutlinePopFonts = CreatePopFonts(0, 0, 0, PrimalAlertTextSize);
            AncientMinimapFadeFonts = CreateFadeFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), ItemAlertMinimapTextSize);
            PrimalMinimapFadeFonts = CreateFadeFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), ItemAlertMinimapTextSize);
            AncientMinimapOutlineFadeFonts = CreateFadeFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PrimalMinimapOutlineFadeFonts = CreateFadeFonts(0, 0, 0, ItemAlertMinimapTextSize);
            AncientMinimapPopFonts = CreatePopFonts(C(AncientTextColorR), C(AncientTextColorG), C(AncientTextColorB), ItemAlertMinimapTextSize);
            PrimalMinimapPopFonts = CreatePopFonts(C(PrimalTextColorR), C(PrimalTextColorG), C(PrimalTextColorB), ItemAlertMinimapTextSize);
            AncientMinimapOutlinePopFonts = CreatePopFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PrimalMinimapOutlinePopFonts = CreatePopFonts(0, 0, 0, ItemAlertMinimapTextSize);
            PlayerCircleBlackBrush = Hud.Render.CreateBrush(C(PlayerCircleBlackOutlineAlpha), 0, 0, 0, PlayerCircleBlackOutlineWidth);
            PlayerDotOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 0);
            PlayerFillBrushes = CreatePlayerBrushes(PlayerCircleFillAlpha, 0);
            PlayerOutlineBrushes = CreatePlayerBrushes(PlayerCircleOutlineAlpha, PlayerCircleOutlineWidth);
            PlayerDotBrushes = CreatePlayerBrushes(255, 0);
            _trianglePainter = new RotatingTriangleShapePainter(Hud);
            _mapPulse = new StandardPingRadiusTransformator(Hud, 333);
        }

        private string BuildResourceSignature()
        {
            return string.Join("|", new[]
            {
                HealthGlobeColorR, HealthGlobeColorG, HealthGlobeColorB, RiftOrbColorR, RiftOrbColorG, RiftOrbColorB,
                AncientColorR, AncientColorG, AncientColorB, PrimalColorR, PrimalColorG, PrimalColorB,
                AncientTextColorR, AncientTextColorG, AncientTextColorB, PrimalTextColorR, PrimalTextColorG, PrimalTextColorB,
                Player1ColorR, Player1ColorG, Player1ColorB, Player2ColorR, Player2ColorG, Player2ColorB,
                Player3ColorR, Player3ColorG, Player3ColorB, Player4ColorR, Player4ColorG, Player4ColorB,
                PlayerCircleFillAlpha, PlayerCircleOutlineAlpha, PlayerCircleBlackOutlineAlpha, C((int)(PlayerCircleOutlineWidth * 10)), C((int)(PlayerCircleBlackOutlineWidth * 10)),
                ItemGroundCircleAlpha, ItemGroundCircleOutlineAlpha, C((int)(ItemGroundCircleStrokeWidth * 10)), C((int)(ItemGroundCircleOutlineWidth * 10)),
                ItemAlertArrowAlpha, ItemAlertArrowHighlightAlpha, ItemAlertArrowHighlightBoost,
                C((int)(AncientAlertTextSize * 10)), C((int)(PrimalAlertTextSize * 10)), C((int)(ItemAlertMinimapTextSize * 10)),
                AncientAlertTextHoldMs, PrimalAlertTextHoldMs
            }.Select(x => x.ToString()).ToArray());
        }

        private IBrush[] CreatePlayerBrushes(int alpha, float strokeWidth)
        {
            return new[]
            {
                Hud.Render.CreateBrush(C(alpha), C(Player1ColorR), C(Player1ColorG), C(Player1ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player2ColorR), C(Player2ColorG), C(Player2ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player3ColorR), C(Player3ColorG), C(Player3ColorB), strokeWidth),
                Hud.Render.CreateBrush(C(alpha), C(Player4ColorR), C(Player4ColorG), C(Player4ColorB), strokeWidth)
            };
        }

        private IFont[] CreateFadeFonts(int r, int g, int b, float size)
        {
            var fonts = new IFont[11];
            var fontSize = Math.Max(6.0f, size);
            for (var i = 0; i < fonts.Length; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", fontSize, (int)Math.Round(255.0d * i / 10.0d), r, g, b, true, false, false);
            return fonts;
        }

        private IFont[] CreatePopFonts(int r, int g, int b, float size)
        {
            var count = 31;
            var fonts = new IFont[count];
            var min = Math.Max(6.0f, size);
            var max = Math.Max(min, size * 2.0f);
            for (var i = 0; i < count; i++)
                fonts[i] = Hud.Render.CreateFont("tahoma", min + ((max - min) * i / (count - 1)), 255, r, g, b, true, false, false);
            return fonts;
        }

        private void ClearRuntime()
        {
            ClearItems();
            _players.Clear();
        }

        private void ClearItems()
        {
            _items.Clear();
            _alertSequence = 0;
            _latestAbovePlayerSequence = 0;
        }

        private bool IsGameReady()
        {
            return Hud != null && Hud.Game != null && Hud.Game.IsInGame && !Hud.Game.IsLoading && Hud.Game.Me != null;
        }

        private bool IsFullMapOpen()
        {
            return Hud.Game.MapMode == MapMode.WaypointMap || Hud.Game.MapMode == MapMode.ActMap || Hud.Game.MapMode == MapMode.Map;
        }

        private void PaintPickupDots()
        {
            if (ShowHealthGlobeDots && HealthGlobeBrush != null)
                foreach (var actor in Hud.Game.Actors.Where(IsHealthGlobe))
                    HealthGlobeBrush.DrawWorldEllipse(HealthGlobeDotRadius, -1, actor.FloorCoordinate);

            if (ShowRiftProgressOrbDots && RiftOrbBrush != null)
                foreach (var actor in Hud.Game.Actors.Where(IsRiftOrb))
                    RiftOrbBrush.DrawWorldEllipse(RiftOrbDotRadius, -1, actor.FloorCoordinate);
        }

        private void PaintItemGroundCircles()
        {
            if (!ShowItemGroundCircles)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank))
                    continue;

                if (marker.Rank == PrimalRank)
                    DrawPulsingGroundEllipse(marker, PrimalGroundEllipseRadiusX, PrimalGroundEllipseRadiusY, PrimalRingBrush, PrimalRingOutlineBrush);
                else
                    DrawPulsingGroundEllipse(marker, AncientGroundEllipseRadiusX, AncientGroundEllipseRadiusY, AncientRingBrush, AncientRingOutlineBrush);
            }
        }

        private void DrawPulsingGroundEllipse(ItemMarker marker, float radiusX, float radiusY, IBrush brush, IBrush outline)
        {
            if (marker == null || brush == null)
                return;

            var sc = Hud.Window.WorldToScreenCoordinate(marker.X, marker.Y, marker.Z, true, true);
            if (!IsValid(sc))
                return;

            var pulse = 1.0f + ((float)Math.Sin((Hud.Game.CurrentRealTimeMilliseconds % Math.Max(1, ItemGroundCirclePulseMs)) * Math.PI * 2.0d / Math.Max(1, ItemGroundCirclePulseMs)) * ItemGroundCirclePulseScale);
            var rx = Math.Max(2.0f, radiusX * pulse);
            var ry = Math.Max(2.0f, radiusY * pulse);
            if (outline != null)
                outline.DrawEllipse(sc.X, sc.Y, rx, ry);
            brush.DrawEllipse(sc.X, sc.Y, rx, ry);
        }

        private void PaintItemMinimapMarkers()
        {
            if (!ShowItemMinimapMarkers || _trianglePainter == null)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank))
                    continue;

                var radius = ((marker.Rank == PrimalRank) ? PrimalMinimapTriangleRadius : AncientMinimapTriangleRadius) * Hud.Render.MinimapScale;
                if (_mapPulse != null)
                    radius = _mapPulse.TransformRadius(radius);

                float x;
                float y;
                Hud.Render.GetMinimapCoordinates(marker.X, marker.Y, out x, out y);
                if (ClampItemMinimapMarkers)
                    ClampToMinimap(ref x, ref y, radius + 2.0f);

                _trianglePainter.Paint(x, y, radius, marker.Rank == PrimalRank ? PrimalMapBrush : AncientMapBrush, marker.Rank == PrimalRank ? PrimalMapOutlineBrush : AncientMapOutlineBrush);
            }
        }

        private void PaintItemScreenEdgeArrows()
        {
            if (!ShowItemScreenEdgeArrows)
                return;

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank) || !IsRankArrowEnabled(marker.Rank))
                    continue;

                float x;
                float y;
                float angle;
                if (TryGetScreenEdgeArrow(marker, out x, out y, out angle))
                    DrawOutlineTriangle(x, y, ItemScreenEdgeArrowRadius, angle, marker.Rank == PrimalRank ? PrimalScreenArrowBrush : AncientScreenArrowBrush, marker.Rank == PrimalRank ? PrimalScreenArrowOutlineBrush : AncientScreenArrowOutlineBrush);
            }
        }

        private bool TryGetScreenEdgeArrow(ItemMarker marker, out float arrowX, out float arrowY, out float angle)
        {
            arrowX = 0;
            arrowY = 0;
            angle = 0;

            var size = Hud.Window.Size;
            var width = (float)size.Width;
            var height = (float)size.Height;
            if (width <= 100 || height <= 100)
                return false;

            var sc = Hud.Window.WorldToScreenCoordinate(marker.X, marker.Y, marker.Z, true, true);
            if (!IsValid(sc))
                return false;

            var interior = ItemScreenEdgeInteriorMargin;
            var margin = Math.Max(ItemScreenEdgeArrowMargin, ItemScreenEdgeArrowRadius + 4.0f);
            var bottomLimit = GetBottomArrowLimit(height, margin);
            if (sc.X >= interior && sc.X <= width - interior && sc.Y >= interior && sc.Y <= bottomLimit - interior)
                return false;

            var cx = width * 0.5f;
            var cy = height * 0.5f;
            var dx = sc.X - cx;
            var dy = sc.Y - cy;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
                return false;

            var scale = float.MaxValue;
            if (dx > 0) scale = Math.Min(scale, (width - margin - cx) / dx);
            else if (dx < 0) scale = Math.Min(scale, (margin - cx) / dx);
            if (dy > 0) scale = Math.Min(scale, (bottomLimit - cy) / dy);
            else if (dy < 0) scale = Math.Min(scale, (margin - cy) / dy);
            if (scale == float.MaxValue || scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
                return false;

            arrowX = Clamp(cx + (dx * scale), margin, width - margin);
            arrowY = Clamp(cy + (dy * scale), margin, bottomLimit);
            angle = (float)Math.Atan2(dy, dx);
            return true;
        }

        private float GetBottomArrowLimit(float height, float margin)
        {
            var reserved = Math.Max(Math.Max(0.0f, ItemScreenEdgeArrowBottomUiReservedPixels), height * Clamp(ItemScreenEdgeArrowBottomUiReservedFrac, 0.0f, 0.45f));
            return Clamp(height - reserved - Math.Max(0.0f, ItemScreenEdgeArrowRadius * 0.5f), margin, height - margin);
        }

        private void PaintItemAlerts()
        {
            if ((!ShowItemAlertText && !ShowItemAlertDirectionArrow) || ItemAlertTextMaxLines <= 0)
                return;

            var alerts = GetActiveAlerts();
            if (alerts.Count == 0)
                return;

            if (ShowItemAlertTextAbovePlayer && alerts[0].Sequence >= _latestAbovePlayerSequence)
            {
                _latestAbovePlayerSequence = alerts[0].Sequence;
                DrawAbovePlayerAlert(alerts[0]);
            }

            if (ShowItemAlertText && ShowItemAlertTextNearMinimap)
                DrawMinimapAlerts(alerts.Where(a => IsRankTextEnabled(a.Marker.Rank)).ToList());
        }

        private List<ItemAlert> GetActiveAlerts()
        {
            var now = Hud.Game.CurrentRealTimeMilliseconds;
            var result = new List<ItemAlert>();

            foreach (var marker in _items.Values)
            {
                if (!IsRankEnabled(marker.Rank) || !IsRankNotificationEnabled(marker.Rank))
                    continue;

                var elapsed = now - marker.FirstSeenMs;
                var alpha = GetAlertAlphaBucket(marker.Rank, elapsed);
                if (alpha <= 0)
                    continue;

                result.Add(new ItemAlert
                {
                    Marker = marker,
                    Text = BuildAlertText(marker.Name, marker.Label),
                    ElapsedMs = elapsed,
                    Sequence = marker.Sequence,
                    AlphaBucket = alpha,
                    Font = GetAlertFont(marker.Rank, elapsed, alpha, false, false),
                    OutlineFont = GetAlertFont(marker.Rank, elapsed, alpha, true, false),
                    ArrowBrush = marker.Rank == PrimalRank ? PrimalAlertArrowBrush : AncientAlertArrowBrush,
                    ArrowOutlineBrush = marker.Rank == PrimalRank ? PrimalAlertArrowOutlineBrush : AncientAlertArrowOutlineBrush,
                    ArrowHighlightBrush = marker.Rank == PrimalRank ? PrimalAlertArrowHighlightBrush : AncientAlertArrowHighlightBrush
                });
            }

            return result.OrderByDescending(a => a.Sequence).ThenByDescending(a => a.Marker.FirstSeenMs).Take(ItemAlertTextMaxLines).ToList();
        }

        private IFont GetAlertFont(int rank, long elapsed, int alphaBucket, bool outline, bool minimap)
        {
            if (elapsed >= 0 && elapsed <= ItemAlertTextPopMs)
            {
                var pop = outline
                    ? (minimap
                        ? (rank == PrimalRank ? PrimalMinimapOutlinePopFonts : AncientMinimapOutlinePopFonts)
                        : (rank == PrimalRank ? PrimalOutlinePopFonts : AncientOutlinePopFonts))
                    : (minimap
                        ? (rank == PrimalRank ? PrimalMinimapPopFonts : AncientMinimapPopFonts)
                        : (rank == PrimalRank ? PrimalPopFonts : AncientPopFonts));
                if (pop != null && pop.Length > 0)
                    return pop[GetPopFontIndex(elapsed, pop.Length)];
            }

            var fade = outline
                ? (minimap
                    ? (rank == PrimalRank ? PrimalMinimapOutlineFadeFonts : AncientMinimapOutlineFadeFonts)
                    : (rank == PrimalRank ? PrimalOutlineFadeFonts : AncientOutlineFadeFonts))
                : (minimap
                    ? (rank == PrimalRank ? PrimalMinimapFadeFonts : AncientMinimapFadeFonts)
                    : (rank == PrimalRank ? PrimalFadeFonts : AncientFadeFonts));
            if (fade == null || alphaBucket < 0 || alphaBucket >= fade.Length)
                return null;
            return fade[alphaBucket];
        }

        private void DrawAbovePlayerAlert(ItemAlert alert)
        {
            if (alert == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return;

            var sc = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
            if (!IsValid(sc))
                return;

            var y = sc.Y + Lerp(ItemAlertPlayerStartYOffset, ItemAlertPlayerSettledYOffset, GetTravelProgress(alert.ElapsedMs));
            DrawAlertLine(alert, sc.X + ItemAlertPlayerXOffset, y, true, true);
        }

        private void DrawMinimapAlerts(List<ItemAlert> alerts)
        {
            if (alerts == null || alerts.Count == 0)
                return;

            if (Hud.Render.MinimapUiElement == null || !Hud.Render.MinimapUiElement.Visible)
                return;

            var rect = Hud.Render.MinimapUiElement.Rectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var startY = rect.Y + (rect.Height * ItemAlertMinimapStartYFrac);
            var settledY = rect.Y + Math.Max(ItemAlertMinimapSettledYOffset, rect.Height * Clamp(ItemAlertMinimapSettledYFrac, 0.0f, 1.0f));
            var y = Lerp(startY, settledY, GetTravelProgress(alerts[0].ElapsedMs));
            var x = rect.X + (rect.Width * 0.5f) + ItemAlertMinimapXOffset;

            foreach (var alert in alerts)
            {
                y += DrawAlertLine(alert, x, y, true, false, true);
            }
        }

        private float DrawAlertLine(ItemAlert alert, float centerX, float y, bool centered, bool withDirectionArrow, bool minimapText = false)
        {
            if (alert == null)
                return ItemAlertTextLineHeight;

            var font = minimapText ? GetAlertFont(alert.Marker.Rank, alert.ElapsedMs, alert.AlphaBucket, false, true) : alert.Font;
            var outlineFont = minimapText ? GetAlertFont(alert.Marker.Rank, alert.ElapsedMs, alert.AlphaBucket, true, true) : alert.OutlineFont;
            var canDrawText = ShowItemAlertText && IsRankTextEnabled(alert.Marker.Rank) && font != null && !string.IsNullOrEmpty(alert.Text);
            float textCenterX = centerX;
            float textHeight = Math.Max(12.0f, ItemAlertTextLineHeight);

            if (canDrawText)
            {
                var layout = font.GetTextLayout(alert.Text);
                var x = centered ? centerX - (layout.Metrics.Width * 0.5f) : centerX;
                textCenterX = x + (layout.Metrics.Width * 0.5f);
                textHeight = Math.Max(textHeight, layout.Metrics.Height);
                DrawOutlinedText(alert.Text, x, y, font, outlineFont);
            }

            var lineHeight = Math.Max(ItemAlertTextLineHeight, textHeight);
            if (withDirectionArrow && IsRankArrowEnabled(alert.Marker.Rank))
            {
                DrawAlertDirectionArrow(alert, textCenterX, y + textHeight + ItemAlertArrowYOffset);
                lineHeight += ItemAlertArrowYOffset + Math.Max(12.0f, ItemAlertArrowHeadSize * 2.0f);
            }

            return lineHeight;
        }

        private void DrawAlertDirectionArrow(ItemAlert alert, float x, float y)
        {
            if (!ShowItemAlertDirectionArrow || alert == null || alert.ArrowBrush == null || !IsRankArrowEnabled(alert.Marker.Rank))
                return;

            var sc = Hud.Window.WorldToScreenCoordinate(alert.Marker.X, alert.Marker.Y, alert.Marker.Z, true, true);
            if (!IsValid(sc))
                return;

            var angle = (float)Math.Atan2(sc.Y - y, sc.X - x);
            var pulse = Math.Abs(GetPulse(ItemAlertArrowPulseMs, ItemAlertArrowPulsePixels));
            DrawFilledSplitTailArrow(x + ((float)Math.Cos(angle) * pulse), y + ((float)Math.Sin(angle) * pulse), angle, ItemAlertArrowLength + pulse, ItemAlertArrowHeadSize, alert.ArrowBrush, alert.ArrowOutlineBrush, alert.ArrowHighlightBrush);
        }

        private void DrawOutlinedText(string text, float x, float y, IFont font, IFont outline)
        {
            var radius = Math.Max(1, Math.Min(6, ItemAlertTextOutlinePixels));
            if (outline != null)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        if ((dx != 0 || dy != 0) && (dx * dx) + (dy * dy) <= (radius * radius) + 2)
                            outline.DrawText(text, x + dx, y + dy, false);
                    }
                }
            }
            font.DrawText(text, x, y, false);
        }

        private void UpdateItemMarkers()
        {
            RemoveDisabledRankMarkers();

            var now = Hud.Game.CurrentRealTimeMilliseconds;
            var live = new HashSet<string>();

            foreach (var item in Hud.Game.Items.Where(IsWatchedItem))
            {
                var key = GetDropKey(item);
                if (string.IsNullOrEmpty(key))
                    continue;

                live.Add(key);
                var liveKey = GetLiveKey(item);
                var coord = item.FloorCoordinate;
                ItemMarker marker;

                if (!_items.TryGetValue(key, out marker))
                {
                    marker = FindReusableMarker(item, key) ?? NewMarker(now);
                }
                else if (ShouldRearmLiveMarker(marker, item, liveKey, now))
                {
                    RearmMarker(marker, now);
                }

                marker.Key = key;
                marker.LiveKey = liveKey;
                marker.Rank = item.AncientRank;
                marker.Sno = item.SnoItem.Sno;
                marker.X = coord.X;
                marker.Y = coord.Y;
                marker.Z = coord.Z;
                marker.Name = GetItemName(item);
                marker.Label = GetItemLabel(item);
                marker.LastSeenMs = now;
                marker.MissingSinceMs = 0;
                marker.MissingNearPlayer = false;
                _items[key] = marker;
            }

            PurgeMissingItems(live, now);
        }

        private ItemMarker NewMarker(long now)
        {
            return new ItemMarker { FirstSeenMs = now, Sequence = ++_alertSequence };
        }

        private void RearmMarker(ItemMarker marker, long now)
        {
            marker.FirstSeenMs = now;
            marker.Sequence = ++_alertSequence;
            marker.MissingSinceMs = 0;
            marker.MissingNearPlayer = false;
        }

        private bool ShouldRearmLiveMarker(ItemMarker marker, IItem item, string liveKey, long now)
        {
            if (marker == null || item == null)
                return false;

            if (marker.MissingSinceMs > 0)
                return marker.MissingNearPlayer;

            if (string.IsNullOrEmpty(liveKey) || string.Equals(marker.LiveKey, liveKey, StringComparison.Ordinal))
                return false;

            return now - marker.LastSeenMs <= 1000 && IsMarkerNearPlayer(marker, ItemPickupCleanupDistance);
        }

        private ItemMarker FindReusableMarker(IItem item, string incomingKey)
        {
            var coord = item.FloorCoordinate;
            var maxDist = Math.Max(0.5f, ItemOffscreenRestoreMatchDistance);
            var maxDistSq = maxDist * maxDist;
            var bestDistSq = float.MaxValue;
            string bestKey = null;

            foreach (var pair in _items)
            {
                var marker = pair.Value;
                if (marker == null || marker.MissingNearPlayer || marker.Rank != item.AncientRank || marker.Sno != item.SnoItem.Sno)
                    continue;

                if (string.Equals(pair.Key, incomingKey, StringComparison.Ordinal))
                {
                    bestKey = pair.Key;
                    break;
                }

                var dx = marker.X - coord.X;
                var dy = marker.Y - coord.Y;
                var distSq = (dx * dx) + (dy * dy);
                if (distSq <= maxDistSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestKey = pair.Key;
                }
            }

            if (string.IsNullOrEmpty(bestKey))
                return null;

            var reused = _items[bestKey];
            _items.Remove(bestKey);
            return reused;
        }


        private void RemoveDisabledRankMarkers()
        {
            if (_items.Count == 0)
                return;

            var remove = new List<string>();
            foreach (var pair in _items)
                if (!IsRankEnabled(pair.Value.Rank))
                    remove.Add(pair.Key);
            foreach (var key in remove)
                _items.Remove(key);
        }

        private void PurgeMissingItems(HashSet<string> live, long now)
        {
            if (_items.Count == 0)
                return;

            var remove = new List<string>();
            foreach (var pair in _items)
            {
                var marker = pair.Value;
                if (marker == null)
                {
                    remove.Add(pair.Key);
                    continue;
                }

                if (live.Contains(pair.Key))
                    continue;

                if (marker.MissingSinceMs <= 0)
                {
                    marker.MissingSinceMs = now;
                    marker.MissingNearPlayer = IsMarkerNearPlayer(marker, ItemPickupCleanupDistance);
                }

                if (ShouldRemoveMissingMarker(marker, now))
                    remove.Add(pair.Key);
            }

            foreach (var key in remove)
                _items.Remove(key);
        }

        private bool ShouldRemoveMissingMarker(ItemMarker marker, long now)
        {
            if (marker == null)
                return true;

            if (marker.MissingNearPlayer)
                return true;

            if (Hud.Game.IsInTown && ItemTownMissingCleanupDelayMs >= 0 && marker.MissingSinceMs > 0 && now - marker.MissingSinceMs >= ItemTownMissingCleanupDelayMs)
                return true;

            return ItemMarkerMaxAgeMs > 0 && now - marker.LastSeenMs > ItemMarkerMaxAgeMs;
        }

        private bool IsMarkerNearPlayer(ItemMarker marker, float distance)
        {
            if (marker == null || Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null || distance <= 0)
                return false;

            var me = Hud.Game.Me.FloorCoordinate;
            var dx = me.X - marker.X;
            var dy = me.Y - marker.Y;
            return (dx * dx) + (dy * dy) <= distance * distance;
        }

        private bool IsRankEnabled(int rank)
        {
            return (rank == AncientRank && ShowAncientItems) || (rank == PrimalRank && ShowPrimalItems);
        }

        private bool IsRankTextEnabled(int rank)
        {
            return rank == PrimalRank ? ShowPrimalItemAlertText : ShowAncientItemAlertText;
        }

        private bool IsRankArrowEnabled(int rank)
        {
            return rank == PrimalRank ? ShowPrimalItemArrows : ShowAncientItemArrows;
        }

        private bool IsRankNotificationEnabled(int rank)
        {
            return (ShowItemAlertText && IsRankTextEnabled(rank)) || (ShowItemAlertDirectionArrow && IsRankArrowEnabled(rank));
        }

        private int GetRankAlertHoldMs(int rank)
        {
            return Math.Max(0, rank == PrimalRank ? PrimalAlertTextHoldMs : AncientAlertTextHoldMs);
        }

        private void PaintPlayerGroundCircles()
        {
            if (!ShowPlayerMarkers || !ShowPlayerGroundCircles || _players.Count == 0 || PlayerFillBrushes == null || PlayerOutlineBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || player.FloorCoordinate == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                var sc = player.FloorCoordinate.ToScreenCoordinate(true, true);
                if (!IsValid(sc))
                    continue;

                var x = sc.X;
                var y = sc.Y + PlayerCircleScreenYOffset;
                var rx = Math.Max(3.0f, PlayerCircleScreenRadiusX);
                var ry = Math.Max(2.0f, PlayerCircleScreenRadiusY);
                if (PlayerCircleBlackBrush != null) PlayerCircleBlackBrush.DrawEllipse(x, y, rx, ry);
                PlayerFillBrushes[mark.Slot].DrawEllipse(x, y, rx, ry);
                PlayerOutlineBrushes[mark.Slot].DrawEllipse(x, y, rx, ry);
            }
        }

        private void PaintPlayerMinimapDots()
        {
            if (!ShowPlayerMarkers || !ShowPlayerMinimapDots || _players.Count == 0 || PlayerDotBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || player.FloorCoordinate == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                float x;
                float y;
                Hud.Render.GetMinimapCoordinates(player.FloorCoordinate.X, player.FloorCoordinate.Y, out x, out y);
                var radius = Math.Max(2.0f, PlayerMinimapDotRadius * Hud.Render.MinimapScale);
                if (ClampPlayerMinimapDots)
                    ClampToMinimap(ref x, ref y, radius + 2.0f);
                DrawDot(x, y, radius, PlayerDotBrushes[mark.Slot]);
            }
        }

        private void PaintPlayerPortraitMarkers()
        {
            if (!ShowPlayerMarkers || !ShowPlayerPortraitMarker || _players.Count == 0 || PlayerDotBrushes == null)
                return;

            foreach (var mark in _players)
            {
                var player = FindPlayer(mark.Key);
                if (player == null || mark.Slot < 0 || mark.Slot >= PlayerMarkerSlots)
                    continue;

                var portrait = GetVisiblePortrait(player);
                if (portrait == null)
                    continue;

                var rect = portrait.Rectangle;
                if (rect.Width <= 1 || rect.Height <= 1)
                    continue;

                var radius = Math.Max(2.5f, PlayerPortraitDotRadius);
                var x = rect.X + PlayerPortraitOffsetX + radius;
                var y = rect.Y + PlayerPortraitOffsetY + radius;
                float angle;
                bool onScreen;
                if (TryGetPlayerDirection(player, out angle, out onScreen) && !onScreen)
                    DrawFilledSplitTailArrow(x, y, angle, PlayerPortraitArrowLength, PlayerPortraitArrowHeadSize, PlayerDotBrushes[mark.Slot], PlayerDotOutlineBrush, null);
                else
                    DrawDot(x, y, radius, PlayerDotBrushes[mark.Slot]);
            }
        }

        private void DrawDot(float x, float y, float radius, IBrush brush)
        {
            if (PlayerDotOutlineBrush != null)
                PlayerDotOutlineBrush.DrawEllipse(x, y, radius + 2.0f, radius + 2.0f);
            if (brush != null)
                brush.DrawEllipse(x, y, radius, radius);
        }

        private bool TryGetPlayerDirection(IPlayer player, out float angle, out bool onScreen)
        {
            angle = 0;
            onScreen = false;
            if (player == null || player.FloorCoordinate == null)
                return false;

            var target = player.FloorCoordinate.ToScreenCoordinate(true, true);
            if (IsValid(target))
            {
                onScreen = IsPointInsideWindow(target.X, target.Y, ItemScreenEdgeInteriorMargin);
                var origin = Hud.Game.Me != null && Hud.Game.Me.FloorCoordinate != null ? Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true) : null;
                var ox = IsValid(origin) ? origin.X : Hud.Window.Size.Width * 0.5f;
                var oy = IsValid(origin) ? origin.Y : Hud.Window.Size.Height * 0.5f;
                angle = (float)Math.Atan2(target.Y - oy, target.X - ox);
                return true;
            }

            if (Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null)
                return false;

            float tx;
            float ty;
            float mx;
            float my;
            Hud.Render.GetMinimapCoordinates(player.FloorCoordinate.X, player.FloorCoordinate.Y, out tx, out ty);
            Hud.Render.GetMinimapCoordinates(Hud.Game.Me.FloorCoordinate.X, Hud.Game.Me.FloorCoordinate.Y, out mx, out my);
            angle = (float)Math.Atan2(ty - my, tx - mx);
            return Math.Abs(tx - mx) > 0.001f || Math.Abs(ty - my) > 0.001f;
        }

        private bool TryGetHoveredPortraitPlayer(out IPlayer hovered)
        {
            hovered = null;
            try
            {
                foreach (var player in Hud.Game.Players.Where(p => p != null && p.IsInGame))
                {
                    var portrait = GetVisiblePortrait(player);
                    if (portrait == null)
                        continue;
                    var r = portrait.Rectangle;
                    if (r.Width > 0 && r.Height > 0 && Hud.Window.CursorInsideRect(r.X, r.Y, r.Width, r.Height))
                    {
                        hovered = player;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private IUiElement GetVisiblePortrait(IPlayer player)
        {
            if (player == null)
                return null;
            var portrait = player.PortraitUiElement;
            try
            {
                if (portrait != null && !portrait.Visible && portrait.ReplacementWhenNotVisible != null)
                    portrait = portrait.ReplacementWhenNotVisible;
            }
            catch { }
            return portrait != null && portrait.Visible ? portrait : null;
        }

        private void TogglePlayerMark(IPlayer player)
        {
            var key = GetPlayerKey(player);
            if (string.IsNullOrEmpty(key))
                return;

            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].Key == key)
                {
                    _players.RemoveAt(i);
                    return;
                }
            }

            var slot = FirstFreePlayerSlot();
            if (slot >= 0)
                _players.Add(new PlayerMark { Key = key, Slot = slot });
        }

        private int FirstFreePlayerSlot()
        {
            for (var slot = 0; slot < PlayerMarkerSlots; slot++)
            {
                var used = false;
                foreach (var mark in _players)
                    if (mark.Slot == slot) { used = true; break; }
                if (!used)
                    return slot;
            }
            return -1;
        }

        private void PurgePlayerMarks()
        {
            for (var i = _players.Count - 1; i >= 0; i--)
                if (FindPlayer(_players[i].Key) == null)
                    _players.RemoveAt(i);
        }

        private IPlayer FindPlayer(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            foreach (var player in Hud.Game.Players)
                if (player != null && player.IsInGame && GetPlayerKey(player) == key)
                    return player;
            return null;
        }

        private string GetPlayerKey(IPlayer player)
        {
            if (player == null)
                return null;
            if (player.HeroId != 0) return "hero:" + player.HeroId;
            if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait)) return "tag:" + player.BattleTagAbovePortrait;
            if (!string.IsNullOrEmpty(player.HeroName)) return "name:" + player.HeroName + ":" + player.Index;
            return "index:" + player.Index;
        }

        private bool IsWatchedItem(IItem item)
        {
            return item != null && item.Location == ItemLocation.Floor && item.FloorCoordinate != null && item.SnoItem != null && item.IsLegendary && item.SnoItem.Kind != ItemKind.craft && IsRankEnabled(item.AncientRank);
        }

        private bool IsHealthGlobe(IActor actor)
        {
            return actor != null && !actor.IsDisabled && actor.SnoActor != null && actor.SnoActor.Kind == ActorKind.HealthGlobe && actor.FloorCoordinate != null;
        }

        private bool IsRiftOrb(IActor actor)
        {
            return actor != null && !actor.IsDisabled && actor.SnoActor != null && actor.SnoActor.Kind == ActorKind.RiftOrb && actor.FloorCoordinate != null;
        }

        private string GetDropKey(IItem item)
        {
            var c = item.FloorCoordinate;
            return "drop:" + item.SnoItem.Sno + ":" + item.AncientRank + ":" + (int)Math.Round(c.X * 10.0f) + ":" + (int)Math.Round(c.Y * 10.0f);
        }

        private string GetLiveKey(IItem item)
        {
            if (!string.IsNullOrEmpty(item.ItemUniqueId)) return "uid:" + item.ItemUniqueId;
            if (item.AnnId != 0) return "ann:" + item.AnnId;
            if (item.AcdId != 0) return "acd:" + item.AcdId;
            return "tick:" + item.CreatedAtInGameTick;
        }

        private string GetItemName(IItem item)
        {
            if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameLocalized)) return item.SnoItem.NameLocalized;
            if (!string.IsNullOrEmpty(item.FullNameLocalized)) return item.FullNameLocalized;
            if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameEnglish)) return item.SnoItem.NameEnglish;
            if (!string.IsNullOrEmpty(item.FullNameEnglish)) return item.FullNameEnglish;
            return "item";
        }

        private string BuildAlertText(string name, string label)
        {
            var suffix = string.IsNullOrEmpty(label) ? string.Empty : " (" + label + ")";
            var text = (string.IsNullOrEmpty(name) ? "item" : name) + suffix;
            if (ItemAlertTextMaxCharacters <= 0 || text.Length <= ItemAlertTextMaxCharacters)
                return text;
            if (ItemAlertTextMaxCharacters <= 3)
                return text.Substring(0, ItemAlertTextMaxCharacters);
            return text.Substring(0, ItemAlertTextMaxCharacters - 3) + "...";
        }

        private string GetItemLabel(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return null;

            var label = LabelFromSnoType(item.SnoItem.SnoItemType);
            if (!string.IsNullOrEmpty(label))
                return label;

            label = LabelFromSearchText(BuildItemSearchText(item.SnoItem));
            if (!string.IsNullOrEmpty(label))
                return label;

            label = LabelFromLocation(item.SnoItem.UsedLocation1);
            return string.IsNullOrEmpty(label) ? LabelFromLocation(item.SnoItem.UsedLocation2) : label;
        }

        private string LabelFromSnoType(ISnoItemType type)
        {
            var guard = 0;
            while (type != null && guard++ < 8)
            {
                var label = ExactLabel(type.Code);
                if (!string.IsNullOrEmpty(label)) return label;
                label = ExactLabel(type.NameEnglish);
                if (!string.IsNullOrEmpty(label)) return label;
                type = type.ParentSnoType;
            }
            return null;
        }

        private string ExactLabel(string value)
        {
            var key = Normalize(value);
            if (string.IsNullOrEmpty(key))
                return null;
            foreach (var rule in ExactLabels)
                if (rule.MatchesExact(key))
                    return rule.Label;
            return null;
        }

        private string LabelFromSearchText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            foreach (var rule in SearchLabels)
                if (rule.Contains(text))
                    return rule.Label;
            return null;
        }

        private string BuildItemSearchText(ISnoItem sno)
        {
            var parts = new List<string>();
            Add(parts, sno.Code);
            Add(parts, sno.MainGroupCode);
            if (sno.GroupCodes != null)
                foreach (var code in sno.GroupCodes) Add(parts, code);

            var type = sno.SnoItemType;
            var guard = 0;
            while (type != null && guard++ < 8)
            {
                Add(parts, type.Code);
                Add(parts, type.NameEnglish);
                type = type.ParentSnoType;
            }
            return string.Join("|", parts.ToArray()).ToLowerInvariant();
        }

        private void Add(List<string> parts, string value)
        {
            if (!string.IsNullOrEmpty(value))
                parts.Add(value);
        }

        private string LabelFromLocation(ItemLocation location)
        {
            switch (location)
            {
                case ItemLocation.Head: return "Helm";
                case ItemLocation.Torso: return "Chest";
                case ItemLocation.Hands: return "Gloves";
                case ItemLocation.Waist: return "Belt";
                case ItemLocation.Feet: return "Boots";
                case ItemLocation.Shoulders: return "Shoulders";
                case ItemLocation.Legs: return "Pants";
                case ItemLocation.Bracers: return "Bracers";
                case ItemLocation.LeftRing:
                case ItemLocation.RightRing: return "Ring";
                case ItemLocation.Neck: return "Amulet";
                case ItemLocation.PetSpecial: return "Follower Token";
                case ItemLocation.PetNeck: return "Follower Amulet";
                case ItemLocation.PetRightRing:
                case ItemLocation.PetLeftRing: return "Follower Ring";
                default: return null;
            }
        }

        private void DrawOutlineTriangle(float x, float y, float radius, float angle, IBrush brush, IBrush outline)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tipX = x + cos * radius;
            var tipY = y + sin * radius;
            var baseX = x - cos * radius * 0.72f;
            var baseY = y - sin * radius * 0.72f;
            var half = radius * 0.72f;
            var leftX = baseX + px * half;
            var leftY = baseY + py * half;
            var rightX = baseX - px * half;
            var rightY = baseY - py * half;
            if (outline != null) DrawTriangleLines(outline, tipX, tipY, leftX, leftY, rightX, rightY);
            if (brush != null) DrawTriangleLines(brush, tipX, tipY, leftX, leftY, rightX, rightY);
        }

        private void DrawTriangleLines(IBrush brush, float tipX, float tipY, float leftX, float leftY, float rightX, float rightY)
        {
            brush.DrawLine(tipX, tipY, leftX, leftY);
            brush.DrawLine(leftX, leftY, rightX, rightY);
            brush.DrawLine(rightX, rightY, tipX, tipY);
        }

        private void DrawFilledSplitTailArrow(float x, float y, float angle, float length, float halfWidth, IBrush brush, IBrush outline, IBrush highlight)
        {
            if (brush == null)
                return;
            if (outline != null)
                FillSplitTailArrow(x, y, angle, length + 6.5f, halfWidth + 3.8f, outline);
            FillSplitTailArrow(x, y, angle, length, halfWidth, brush);
            if (highlight != null)
                FillArrowHighlight(x, y, angle, length, halfWidth, highlight);
        }

        private void FillSplitTailArrow(float x, float y, float angle, float length, float halfWidth, IBrush brush)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tip = new Vector2(x + cos * length * 0.62f, y + sin * length * 0.62f);
            var back1 = new Vector2(x - cos * length * 0.50f + px * halfWidth, y - sin * length * 0.50f + py * halfWidth);
            var notch = new Vector2(x - cos * length * 0.22f, y - sin * length * 0.22f);
            var back2 = new Vector2(x - cos * length * 0.50f - px * halfWidth, y - sin * length * 0.50f - py * halfWidth);
            FillPolygon(brush, tip, back1, notch, back2);
        }

        private void FillArrowHighlight(float x, float y, float angle, float length, float halfWidth, IBrush brush)
        {
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var px = -sin;
            var py = cos;
            var tip = new Vector2(x + cos * length * 0.34f, y + sin * length * 0.34f);
            var center = new Vector2(x - cos * length * 0.03f, y - sin * length * 0.03f);
            FillPolygon(brush, tip, new Vector2(center.X + px * halfWidth * 0.42f, center.Y + py * halfWidth * 0.42f), new Vector2(center.X - px * halfWidth * 0.12f, center.Y - py * halfWidth * 0.12f));
        }

        private void FillPolygon(IBrush brush, params Vector2[] points)
        {
            if (brush == null || points == null || points.Length < 3)
                return;
            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(points[0], FigureBegin.Filled);
                    for (var i = 1; i < points.Length; i++)
                        sink.AddLine(points[i]);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                brush.DrawGeometry(geometry);
            }
        }

        private float GetPulse(int ms, float amplitude)
        {
            if (ms <= 0 || Math.Abs(amplitude) < 0.001f)
                return 0.0f;
            return (float)Math.Sin((Hud.Game.CurrentRealTimeMilliseconds % ms) * Math.PI * 2.0d / ms) * amplitude;
        }

        private int GetAlertAlphaBucket(int rank, long elapsed)
        {
            var holdMs = GetRankAlertHoldMs(rank);
            if (elapsed <= holdMs)
                return 10;
            if (ItemAlertTextFadeMs <= 0)
                return 0;
            var fadeElapsed = elapsed - holdMs;
            if (fadeElapsed >= ItemAlertTextFadeMs)
                return 0;
            return Math.Max(1, Math.Min(10, (int)Math.Ceiling((1.0d - ((double)fadeElapsed / ItemAlertTextFadeMs)) * 10.0d)));
        }

        private int GetPopFontIndex(long elapsed, int count)
        {
            if (count <= 1 || ItemAlertTextPopMs <= 0)
                return 0;
            var t = Clamp((float)elapsed / ItemAlertTextPopMs, 0.0f, 1.0f);
            var u = t < 0.5f ? Smooth(t / 0.5f) : 1.0f - Smooth((t - 0.5f) / 0.5f);
            return Math.Max(0, Math.Min(count - 1, (int)Math.Round(u * (count - 1))));
        }

        private float GetTravelProgress(long elapsed)
        {
            if (ItemAlertTextPopMs <= 0 || elapsed >= ItemAlertTextPopMs)
                return 1.0f;
            return Smooth(Clamp((float)elapsed / ItemAlertTextPopMs, 0.0f, 1.0f));
        }

        private float Smooth(float t)
        {
            t = Clamp(t, 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        private float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private void ClampToMinimap(ref float x, ref float y, float margin)
        {
            if (Hud.Render.MinimapUiElement == null || !Hud.Render.MinimapUiElement.Visible)
                return;
            var r = Hud.Render.MinimapUiElement.Rectangle;
            x = Clamp(x, r.X + margin, r.X + r.Width - margin);
            y = Clamp(y, r.Y + margin, r.Y + r.Height - margin);
        }

        private bool IsPointInsideWindow(float x, float y, float margin)
        {
            var size = Hud.Window.Size;
            return x >= margin && x <= size.Width - margin && y >= margin && y <= size.Height - margin;
        }

        private bool IsValid(IScreenCoordinate p)
        {
            return p != null && !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y);
        }

        private float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private int C(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static LabelRule R(string label, params string[] keys)
        {
            return new LabelRule(label, keys);
        }

        private IEnumerable<IBrush> Brushes(params IBrush[] brushes)
        {
            if (brushes == null) yield break;
            foreach (var brush in brushes) if (brush != null) yield return brush;
        }


        private IEnumerable<IFont> Fonts(IFont[] fonts)
        {
            if (fonts == null) yield break;
            foreach (var font in fonts) if (font != null) yield return font;
        }

        private class LabelRule
        {
            public readonly string Label;
            private readonly string[] _keys;

            public LabelRule(string label, string[] keys)
            {
                Label = label;
                _keys = keys ?? new string[0];
                for (var i = 0; i < _keys.Length; i++)
                    _keys[i] = _keys[i].Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            }

            public bool MatchesExact(string key)
            {
                foreach (var k in _keys)
                    if (k == key) return true;
                return false;
            }

            public bool Contains(string text)
            {
                var compact = text.Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
                foreach (var k in _keys)
                    if (compact.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }
        }

        private class ItemMarker
        {
            public string Key;
            public string LiveKey;
            public int Rank;
            public uint Sno;
            public float X;
            public float Y;
            public float Z;
            public long FirstSeenMs;
            public long LastSeenMs;
            public long MissingSinceMs;
            public bool MissingNearPlayer;
            public long Sequence;
            public string Name;
            public string Label;
        }

        private class ItemAlert
        {
            public ItemMarker Marker;
            public string Text;
            public long Sequence;
            public long ElapsedMs;
            public int AlphaBucket;
            public IFont Font;
            public IFont OutlineFont;
            public IBrush ArrowBrush;
            public IBrush ArrowOutlineBrush;
            public IBrush ArrowHighlightBrush;
        }

        private class PlayerMark
        {
            public string Key;
            public int Slot;
        }
    }
}
