using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharpDX;
using SharpDX.Direct2D1;
using DXRectangleF = SharpDX.RectangleF;
using DXVector2 = SharpDX.Vector2;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_PartyInspector : BasePlugin, IInGameTopPainter, IInGameWorldPainter, IKeyEventHandler
    {
        public IKeyEvent ToggleKeyEvent { get; set; }

        public bool ShowPanel { get; set; }
        public bool ShowGemUpgradeReminder { get; set; }
        public bool ShowSkills { get; set; }
        public bool ShowCooldowns { get; set; }
        public bool ShowPassives { get; set; }
        public bool ShowCubeItems { get; set; }

        public bool HideReminderWhenPanelOpen { get; set; }

        public float PanelX { get; set; }
        public float PanelY { get; set; }
        public float PanelWidth { get; set; }
        public float RowHeight { get; set; }
        public float SkillIconSize { get; set; }
        public float SkillIconGap { get; set; }

        public float ReminderX { get; set; }
        public float ReminderY { get; set; }

        // Always-on compact portrait skillbars.
        public bool ShowAlwaysOnPartySkillBars { get; set; }
        public bool ShowSelfInAlwaysOnBars { get; set; }
        public bool ShowAlwaysOnCooldowns { get; set; }
        public bool ShowAlwaysOnEquippedLegendaryGems { get; set; }
        public bool ShowGemUpgradesNearPortraits { get; set; }
        public bool ShowNemesisBracersNearPortraits { get; set; }
        public bool ShowArchonStacksNearSkillBars { get; set; }
        public float ArchonStackIndicatorXOffset { get; set; }
        public float ArchonStackIndicatorYOffset { get; set; }
        public float ArchonStackFadeStartSeconds { get; set; }

        // Compact portrait cleanup options. These affect only the small portrait bars.
        public bool ShowCompactSkillKeys { get; set; }
        public bool ShowCompactRuneLetters { get; set; }

        // Portrait-hover stat table replacement/cover layer.
        // Plugin-only workaround for the native TurboHUD portrait hover info: draw our own table
        // when the cursor is over any in-game player portrait.
        public bool ShowPortraitHoverStats { get; set; }
        public bool PortraitHoverStatsCoverNativeInfo { get; set; }
        public bool PortraitHoverStatsShowAllPlayers { get; set; }
        public bool PortraitHoverStatsUseLastKnownStats { get; set; }
        public float PortraitHoverStatsX { get; set; }
        public float PortraitHoverStatsY { get; set; }
        public float PortraitHoverStatsCellHeight { get; set; }
        public float PortraitHoverStatsPaddingX { get; set; }
        public float PortraitHoverStatsCoverPaddingX { get; set; }
        public float PortraitHoverStatsCoverPaddingY { get; set; }
        public float PortraitHoverStatsNameColumnWidth { get; set; }
        public float PortraitHoverStatsStatColumnWidth { get; set; }
        public float PortraitHoverStatsSoloColumnWidth { get; set; }

        // F12 expanded inspector sections.
        public bool ShowExpandedSkills { get; set; }
        public bool ShowExpandedPassives { get; set; }
        public bool ShowExpandedCubeItems { get; set; }
        public bool ShowExpandedGemUpgrades { get; set; }
        public bool ShowExpandedExtraStats { get; set; }

        // Portrait-anchored compact skillbar layout.
        public float PortraitSkillIconScale { get; set; }
        public float PortraitSkillIconGap { get; set; }
        public float PortraitSkillXOffset { get; set; }
        public float PortraitSkillYOffsetScale { get; set; }

        // Fine-tune the compact skillbar Y position in pixels.
        // Negative moves it upward to make room for the expanded row 2 below.
        public float PortraitSkillYOffsetPx { get; set; }

        // Nemesis Bracers portrait marker layout.
        public float PortraitNemesisIconScale { get; set; }
        public float PortraitNemesisIconXOffset { get; set; }
        public float PortraitNemesisIconYOffset { get; set; }

        // Compact legendary gem icon layout (used only when ShowAlwaysOnEquippedLegendaryGems is true).
        public float PortraitGemIconScale { get; set; }
        public float PortraitGemIconGap { get; set; }
        public float PortraitGemYOffsetScale { get; set; }
        public int MaxPortraitLegendaryGemIcons { get; set; }

        // F12 portrait-anchored build extension layout.
        public float ExpandedPortraitDetailIconScale { get; set; }
        public float ExpandedPortraitDetailGap { get; set; }
        public bool ShowExpandedPortraitStatsText { get; set; }
        public bool ShowExpandedLegendaryGems { get; set; }
        public float ExpandedPortraitGemIconScale { get; set; }
        public float ExpandedPortraitGemIconGap { get; set; }
        public float ExpandedPortraitGemYOffset { get; set; }

        // F12 two-row expanded layout.
        // Row 2 starts this many pixels below the bottom of row 1 icons.
        public float ExpandedBuildRowYOffset { get; set; }
        // Inline equipped gem scale relative to the compact skillbar icon size.
        public float ExpandedInlineGemIconScale { get; set; }
        // Gap between the end of the skillbar and the first inline gem, and between gems.
        public float ExpandedInlineGemGap { get; set; }

        // Cube power icon box shape.
        // CubeIconHeightMultiplier controls how many times taller the box is than it is wide.
        // 2.0 means the box is twice as tall as wide — a stave fits cleanly, a ring
        // centers with padding above and below.  Neither item is ever stretched or clipped.
        public float CubeIconHeightMultiplier { get; set; }

        // Urshi gem-upgrade counters.
        // When true, gem-upgrade text near portraits only appears while Urshi is visible on screen.
        // Default false: keep portrait counters visible in town as long as any player still has upgrades left.
        public bool ShowGemUpgradeCountsOnlyWhenUrshiVisible { get; set; }
        // When true, players with 0 upgrades remaining also show their counter during Urshi context.
        public bool ShowZeroGemUpgradeCountsInContext { get; set; }
        // Additional X/Y pixel offsets for the gem-upgrade counter label position.
        public float GemUpgradeCounterXOffset { get; set; }
        public float GemUpgradeCounterYOffset { get; set; }

        public float CompactCooldownFontSize { get; set; }

        private IFont _titleFont;
        private IFont _nameFont;
        private IFont _smallFont;
        private IFont _warningFont;
        private IFont _cooldownFont;
        private IFont _compactCooldownFont;
        private IFont _keyFont;
        private IFont _portraitSmallFont;
        private IFont _statsFont;
        private IFont _selfGemFont;
        private IFont _otherGemFont;

        private IFont _portraitHoverHeaderFont;
        private IFont _portraitHoverValueFont;
        private IFont _archonCurrentStackFont;
        private IFont _archonLingeringStackFont;
        private IFont _archonStackOutlineFont;

        private IBrush _panelBrush;
        private IBrush _rowBrush;
        private IBrush _borderBrush;
        private IBrush _skillBorderBrush;
        private IBrush _cooldownOverlayBrush;
        private IBrush _cooldownArcBrush;
        private IBrush _warningBackBrush;
        private IBrush _cooldownClockBrush;
        private IBrush _gemIconBorderBrush;
        private IBrush _gemAccentBrush;
        private IBrush _portraitGemBackBrush;
        private IBrush _cubeItemBackBrush;
        private IBrush _archonSkillOverlayBrush;
        private IBrush _nemesisIconBorderBrush;

        private IBrush _portraitHoverBackBrush;
        private IBrush _portraitHoverHeaderBrush;
        private IBrush _portraitHoverCellBrush;
        private IBrush _portraitHoverBorderBrush;

        private readonly Dictionary<string, PortraitHoverPlayerStatsSnapshot> _portraitHoverStatsCache =
            new Dictionary<string, PortraitHoverPlayerStatsSnapshot>();

        // FreeHUD can temporarily expose empty remote-player build/stat objects when
        // a party member is in another world instance. These caches preserve the last
        // valid live data we have seen for that account/hero, without inventing data.
        private readonly Dictionary<string, List<LegendaryGemDisplayInfo>> _legendaryGemInfoCache =
            new Dictionary<string, List<LegendaryGemDisplayInfo>>();

        private readonly Dictionary<string, List<ISnoPower>> _passivePowerCache =
            new Dictionary<string, List<ISnoPower>>();

        private readonly Dictionary<string, List<ISnoItem>> _cubeItemCache =
            new Dictionary<string, List<ISnoItem>>();

        public s7o_PartyInspector()
        {
            Enabled = true;

            // Draw late so the plugin-only replacement table has the best chance to cover
            // native portrait hover info without editing default XML or executable resources.
            Order = 999999;

            // F12 expanded panel.
            ShowPanel = false;

            // Always-on features.
            ShowAlwaysOnPartySkillBars = true;
            ShowSelfInAlwaysOnBars = false;
            ShowAlwaysOnCooldowns = true;
            ShowAlwaysOnEquippedLegendaryGems = false;
            ShowGemUpgradesNearPortraits = true;
            ShowNemesisBracersNearPortraits = true;
            ShowArchonStacksNearSkillBars = true;

            // Archon stack readout: anchored below the portrait buff/CoE area instead of
            // to the right of the skillbar, so it is less likely to collide with HP lists.
            ArchonStackIndicatorXOffset = 16.0f;
            ArchonStackIndicatorYOffset = 0.0f;
            ArchonStackFadeStartSeconds = 5.0f;

            ShowCompactSkillKeys = false;
            ShowCompactRuneLetters = false;

            ShowPortraitHoverStats = true;
            PortraitHoverStatsCoverNativeInfo = true;
            // Hovering any portrait should replace the whole native party stat table,
            // not just the single hovered player row.
            PortraitHoverStatsShowAllPlayers = true;
            // FreeHUD can return zeroed stat objects for remote party members in another
            // town/world instance. Keep the last valid snapshot and use it instead of
            // overwriting a row with false 0s.
            PortraitHoverStatsUseLastKnownStats = true;
            // 0 = auto-center horizontally; otherwise absolute screen X.
            PortraitHoverStatsX = 0.0f;
            // Slightly lower than RC4, while the multi-row cover now hides the native table.
            PortraitHoverStatsY = 35.0f;
            // Taller rows plus a larger cover pad hide native overlap more reliably.
            PortraitHoverStatsCellHeight = 26.0f;
            PortraitHoverStatsPaddingX = 5.0f;
            PortraitHoverStatsCoverPaddingX = 4.0f;
            PortraitHoverStatsCoverPaddingY = 10.0f;
            PortraitHoverStatsNameColumnWidth = 112.0f;
            PortraitHoverStatsStatColumnWidth = 62.0f;
            PortraitHoverStatsSoloColumnWidth = 60.0f;

            // Portrait counters are the default reminder style.
            ShowGemUpgradeReminder = false;
            HideReminderWhenPanelOpen = false;

            // Expanded panel sections.
            ShowSkills = true;
            ShowCooldowns = true;
            ShowPassives = true;
            ShowCubeItems = true;

            ShowExpandedSkills = true;
            ShowExpandedPassives = true;
            ShowExpandedCubeItems = true;
            ShowExpandedGemUpgrades = true;
            ShowExpandedExtraStats = false;

            PanelX = 360.0f;
            PanelY = 120.0f;
            PanelWidth = 680.0f;
            RowHeight = 74.0f;
            SkillIconSize = 36.0f;
            SkillIconGap = 4.0f;

            ReminderX = 0.0f; // 0 = auto-center.
            ReminderY = 0.0f; // 0 = auto top-ish.

            // GLQ-style portrait bar defaults.
            PortraitSkillIconScale = 0.38f;
            PortraitSkillIconGap = 1.0f;
            PortraitSkillXOffset = 0.0f;
            PortraitSkillYOffsetScale = 0.095f;

            // Move the compact skillbar slightly upward to make room for the second row.
            PortraitSkillYOffsetPx = -5.0f;

            PortraitNemesisIconScale = 0.62f;
            PortraitNemesisIconXOffset = 0.0f;
            PortraitNemesisIconYOffset = 0.0f;

            // Compact gem icon layout (always-on only — off by default).
            PortraitGemIconScale = 0.58f;
            PortraitGemIconGap = 2.0f;
            PortraitGemYOffsetScale = 0.45f;
            MaxPortraitLegendaryGemIcons = 3;

            // F12 expanded layout (legacy sub-settings, kept for the old below-row method).
            ExpandedPortraitDetailIconScale = 1.05f;
            ExpandedPortraitDetailGap = 3.0f;
            ShowExpandedPortraitStatsText = false;
            ShowExpandedLegendaryGems = true;
            ExpandedPortraitGemIconScale = 1.10f;
            ExpandedPortraitGemIconGap = 3.0f;
            ExpandedPortraitGemYOffset = 3.0f;

            // F12 two-row layout.
            ExpandedBuildRowYOffset = 3.0f;
            ExpandedInlineGemIconScale = 1.00f;
            ExpandedInlineGemGap = 3.0f;

            // Cube icon boxes are rectangular — tall enough to fit a stave without distortion.
            // A ring will center with empty space above/below (clean, not distorted).
            CubeIconHeightMultiplier = 2.0f;

            // Urshi gem-upgrade counters.
            ShowGemUpgradeCountsOnlyWhenUrshiVisible = false;
            ShowZeroGemUpgradeCountsInContext = true;
            GemUpgradeCounterXOffset = 0.0f;
            GemUpgradeCounterYOffset = 0.0f;

            CompactCooldownFontSize = 11.0f;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            ToggleKeyEvent = Hud.Input.CreateKeyEvent(true, Key.F12, false, false, false);

            _titleFont        = Hud.Render.CreateFont("tahoma", 10.5f, 255, 255, 255, 255, true,  false, 180, 0, 0, 0, true);
            _nameFont         = Hud.Render.CreateFont("tahoma",  8.5f, 255, 255, 255, 255, true,  false, 180, 0, 0, 0, true);
            _smallFont        = Hud.Render.CreateFont("tahoma",  7.5f, 230, 220, 220, 220, false, false, 160, 0, 0, 0, true);
            _warningFont      = Hud.Render.CreateFont("tahoma",  8.5f, 255, 255,  90,  90, true,  false, 180, 0, 0, 0, true);
            _cooldownFont     = Hud.Render.CreateFont("tahoma",  9.0f, 255, 255, 255, 255, true,  false, 210, 0, 0, 0, true);
            _keyFont          = Hud.Render.CreateFont("tahoma",  7.0f, 255, 255, 255, 180, true,  false, 160, 0, 0, 0, true);
            _portraitSmallFont= Hud.Render.CreateFont("tahoma",  6.5f, 255, 255, 255, 220, true,  false, 150, 0, 0, 0, true);
            _statsFont        = Hud.Render.CreateFont("tahoma",  7.5f, 240, 190, 230, 255, false, false, 160, 0, 0, 0, true);

            _portraitHoverHeaderFont = Hud.Render.CreateFont("tahoma", 7.0f, 255, 255, 255, 255, true, false, 180, 0, 0, 0, true);
            _portraitHoverValueFont  = Hud.Render.CreateFont("tahoma", 7.5f, 255, 235, 235, 235, true, false, 180, 0, 0, 0, true);

            _compactCooldownFont = Hud.Render.CreateFont(
                "tahoma", CompactCooldownFontSize, 255, 255, 255, 255, true, false, 220, 0, 0, 0, true);

            float archonStackFontSize = CompactCooldownFontSize + 1.5f;
            _archonCurrentStackFont = Hud.Render.CreateFont(
                "tahoma", archonStackFontSize, 255, 190, 80, 255, true, false, 255, 0, 0, 0, true);

            _archonLingeringStackFont = Hud.Render.CreateFont(
                "tahoma", archonStackFontSize, 255, 255, 55, 55, true, false, 255, 0, 0, 0, true);

            _archonStackOutlineFont = Hud.Render.CreateFont(
                "tahoma", archonStackFontSize, 255, 0, 0, 0, true, false, false);

            // Self gem-upgrade counter: bold yellow, slightly larger for visibility.
            _selfGemFont = Hud.Render.CreateFont(
                "tahoma", 8.5f,
                255,  // alpha
                255,  // R
                215,  // G — warm gold-yellow
                 40,  // B
                true, false,
                170,  // shadow alpha
                0, 0, 0,
                true);

            // Other-player gem-upgrade counter: muted grey, readable but secondary.
            _otherGemFont = Hud.Render.CreateFont(
                "tahoma", 7.5f,
                210,  // alpha
                175,  // R
                175,  // G
                175,  // B
                true, false,
                150,  // shadow alpha
                0, 0, 0,
                true);

            _panelBrush          = Hud.Render.CreateBrush(160,   0,   0,   0, 0);
            _rowBrush            = Hud.Render.CreateBrush( 95,  20,  20,  20, 0);
            _borderBrush         = Hud.Render.CreateBrush(210, 140, 140, 140, 1.5f);
            _cooldownOverlayBrush= Hud.Render.CreateBrush(150,   0,   0,   0, 0);
            _cooldownArcBrush    = Hud.Render.CreateBrush(220, 255, 255, 255, 2.0f);
            _warningBackBrush    = Hud.Render.CreateBrush(140,   0,   0,   0, 0);
            _cooldownClockBrush  = Hud.Render.CreateBrush(200,   0,   0,   0, 0);
            _portraitGemBackBrush= Hud.Render.CreateBrush(150,   0,   0,   0, 0);

            // Cube item slot background — flat consistent fill, same alpha for all slots
            // regardless of item type (ring, weapon, etc).  Eliminates the transparency
            // mismatch caused by the inventory background textures varying by item height.
            _cubeItemBackBrush   = Hud.Render.CreateBrush(200,  18,  10,   5, 0);
            _archonSkillOverlayBrush = Hud.Render.CreateBrush(125, 0, 0, 0, 0);
            _nemesisIconBorderBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 1.25f);

            _portraitHoverBackBrush   = Hud.Render.CreateBrush(245, 0, 0, 0, 0);
            _portraitHoverHeaderBrush = Hud.Render.CreateBrush(230, 18, 18, 18, 0);
            _portraitHoverCellBrush   = Hud.Render.CreateBrush(215, 6, 6, 6, 0);
            _portraitHoverBorderBrush = Hud.Render.CreateBrush(235, 145, 145, 145, 1.0f);

            // Thick black outlines — 2.5px gives strong contrast against the colourful icons.
            _skillBorderBrush    = Hud.Render.CreateBrush(255,   0,   0,   0, 2.5f);

            // Gem icon outer border: thick black for contrast.
            _gemIconBorderBrush  = Hud.Render.CreateBrush(255,   0,   0,   0, 2.5f);

            // Gem icon inner accent: thin blue highlight inside the black border.
            _gemAccentBrush      = Hud.Render.CreateBrush(170,  80, 155, 255, 1.0f);
        }

        // -----------------------------------------------------------------------
        // Key events
        // -----------------------------------------------------------------------

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (ToggleKeyEvent == null || keyEvent == null)
                return;

            if (!ToggleKeyEvent.Matches(keyEvent))
                return;

            if (!keyEvent.IsPressed)
                return;

            ShowPanel = !ShowPanel;
        }

        // -----------------------------------------------------------------------
        // Paint entry points
        // -----------------------------------------------------------------------

        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Ground)
                return;

            if (!Enabled || Hud == null || Hud.Game == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;

            bool needsPortraitOverlay =
                ShowAlwaysOnPartySkillBars ||
                ShowGemUpgradesNearPortraits ||
                ShowNemesisBracersNearPortraits ||
                ShowPanel;

            if (needsPortraitOverlay)
                DrawPartyPortraitOverlay();
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip)
                return;

            if (!Enabled || Hud == null || Hud.Game == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;

            // Optional legacy top-center text reminder. Default is false.
            // Portrait Gem Ups counters are the primary reminder style.
            if (ShowGemUpgradeReminder && IsGemUpgradeContextActive())
                DrawGemUpgradeReminder();

            var portraitPlayers = GetPartyPlayers();
            UpdatePortraitHoverStatsCache(portraitPlayers);

            if (ShowPortraitHoverStats)
                DrawPortraitHoverStatsIfNeeded(portraitPlayers);
        }

        // -----------------------------------------------------------------------
        // Gem-upgrade context: Urshi detection
        // -----------------------------------------------------------------------

        private bool IsUrshiVisible()
        {
            try
            {
                if (Hud.Game == null || Hud.Game.Actors == null)
                    return false;

                return Hud.Game.Actors.Any(actor =>
                    actor != null &&
                    actor.SnoActor != null &&
                    actor.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem &&
                    !actor.IsDisabled);
            }
            catch
            {
                return false;
            }
        }

        private bool IsGemUpgradeContextActive()
        {
            if (ShowGemUpgradeCountsOnlyWhenUrshiVisible)
                return IsUrshiVisible();

            try
            {
                foreach (var player in GetPartyPlayers())
                {
                    if (GetGemUpgradesLeft(player) > 0)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        // -----------------------------------------------------------------------
        // Player helpers
        // -----------------------------------------------------------------------

        private List<IPlayer> GetPartyPlayers()
        {
            try
            {
                return Hud.Game.Players
                    .Where(p => p != null && p.IsInGame)
                    .OrderBy(p => p.PortraitIndex)
                    .ToList();
            }
            catch
            {
                return new List<IPlayer>();
            }
        }

        private int GetGemUpgradesLeft(IPlayer player)
        {
            if (player == null)
                return 0;

            try
            {
                int bonus = player.GetAttributeValueAsInt(
                    Hud.Sno.Attributes.Jewel_Upgrades_Bonus,
                    2147483647,
                    0);

                int max = player.GetAttributeValueAsInt(
                    Hud.Sno.Attributes.Jewel_Upgrades_Max,
                    2147483647,
                    0);

                int used = player.GetAttributeValueAsInt(
                    Hud.Sno.Attributes.Jewel_Upgrades_Used,
                    2147483647,
                    0);

                int left = bonus + max - used;
                return left < 0 ? 0 : left;
            }
            catch
            {
                return 0;
            }
        }

        // -----------------------------------------------------------------------
        // Main portrait overlay dispatcher
        // -----------------------------------------------------------------------

        private void DrawPartyPortraitOverlay()
        {
            var players = GetPartyPlayers();
            if (players.Count == 0)
                return;

            // Compute Urshi context once per frame — not once per player.
            bool gemUpgradeContextActive =
                ShowGemUpgradesNearPortraits &&
                IsGemUpgradeContextActive();

            foreach (var player in players)
            {
                if (player == null)
                    continue;

                // Always-on: other players (or self if ShowSelfInAlwaysOnBars).
                bool alwaysOnStrip = ShouldDrawAlwaysOnBuildStrip(player);

                // F12 also draws the compact skillbar for self so row 1 + row 2 work.
                bool drawCompact = alwaysOnStrip || ShowPanel;

                if (drawCompact && ShowAlwaysOnPartySkillBars)
                    DrawCompactPortraitPackage(player);

                // F12 expanded details: ALL players including self.
                if (ShowPanel)
                    DrawExpandedPortraitBuildDetails(player);

                if (ShowNemesisBracersNearPortraits)
                    DrawPortraitNemesisBracersMarker(player);

                // Gem-upgrade counters: independent from F12, gated by Urshi.
                if (gemUpgradeContextActive)
                    DrawCompactPortraitGemUpgradeCount(player);
            }
        }

        private bool ShouldDrawAlwaysOnBuildStrip(IPlayer player)
        {
            if (player == null)
                return false;

            // Other players always get the compact always-on bar.
            if (!player.IsMe)
                return true;

            // Self only gets the always-on bar when explicitly enabled.
            // F12 handles self separately in DrawPartyPortraitOverlay.
            return ShowSelfInAlwaysOnBars;
        }

        // -----------------------------------------------------------------------
        // Compact portrait package (always-on, F12 off)
        // -----------------------------------------------------------------------

        private void DrawCompactPortraitPackage(IPlayer player)
        {
            if (player == null)
                return;

            // Skills only. Equipped legendary gems are F12-only.
            if (ShowAlwaysOnPartySkillBars)
                DrawCompactPortraitSkills(player);
        }

        // -----------------------------------------------------------------------
        // Portrait bar layout
        // -----------------------------------------------------------------------

        private struct PortraitBarLayout
        {
            public float X;
            public float Y;
            public float IconSize;
            public float Gap;

            public float SkillBarEndX
            {
                get { return X + (6.0f * (IconSize + Gap)); }
            }
        }

        private bool TryGetPortraitBarLayout(IPlayer player, out PortraitBarLayout layout)
        {
            layout = new PortraitBarLayout();

            if (player == null || player.PortraitUiElement == null)
                return false;

            var portraitRect = player.PortraitUiElement.Rectangle;
            if (portraitRect.Width <= 0 || portraitRect.Height <= 0)
                return false;

            layout.IconSize = portraitRect.Width * PortraitSkillIconScale;
            layout.Gap      = PortraitSkillIconGap;
            layout.X        = portraitRect.Right + PortraitSkillXOffset;

            // PortraitSkillYOffsetPx shifts the entire strip up/down by a fixed number of pixels.
            layout.Y = portraitRect.Y
                + portraitRect.Height * PortraitSkillYOffsetScale
                + PortraitSkillYOffsetPx;

            return layout.IconSize > 0.0f;
        }

        // -----------------------------------------------------------------------
        // Compact skill icons
        // -----------------------------------------------------------------------

        private void DrawCompactPortraitSkills(IPlayer player)
        {
            if (player == null || player.Powers == null || player.Powers.SkillSlots == null)
                return;

            PortraitBarLayout layout;
            if (!TryGetPortraitBarLayout(player, out layout))
                return;

            float x = layout.X;
            float y = layout.Y;
            float size = layout.IconSize;
            int drawnSkills = 0;

            foreach (var skill in player.Powers.SkillSlots)
            {
                if (skill == null)
                    continue;

                ISnoPower power = GetSkillPower(skill);
                if (power == null)
                    continue;

                var rect = new DXRectangleF(x, y, size, size);
                DrawCompactSkill(skill, rect);
                drawnSkills++;

                if (Hud.Window.CursorInsideRect(rect.X, rect.Y, rect.Width, rect.Height))
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(skill.RuneNameLocalized))
                            Hud.Render.SetHint(skill.RuneNameLocalized);
                        else if (power != null)
                            Hud.Render.SetHint(power.NameLocalized);
                    }
                    catch
                    {
                    }
                }

                x += size + layout.Gap;
            }

            if (drawnSkills > 0)
            {
                double archonSeconds;
                if (TryGetActiveArchonSeconds(player, out archonSeconds))
                    DrawArchonSkillbarOverlay(layout, drawnSkills, archonSeconds);

                if (ShowArchonStacksNearSkillBars)
                    DrawArchonStackIndicator(player, layout, drawnSkills);
            }
        }

        private ISnoPower GetSkillPower(IPlayerSkill skill)
        {
            if (skill == null)
                return null;

            try
            {
                if (skill.SnoPower != null)
                    return skill.SnoPower;
            }
            catch
            {
            }

            try
            {
                return skill.CurrentSnoPower;
            }
            catch
            {
                return null;
            }
        }

        private void DrawCompactSkill(IPlayerSkill skill, DXRectangleF rect)
        {
            if (skill == null)
                return;

            DrawSkillIcon(skill, rect.X, rect.Y, rect.Width);

            if (_skillBorderBrush != null)
                _skillBorderBrush.DrawRectangle(rect.X, rect.Y, rect.Width, rect.Height);

            if (ShowCompactSkillKeys)
                DrawSkillKey(skill, rect.X, rect.Y, rect.Width);

            if (ShowAlwaysOnCooldowns)
                DrawSkillCooldownClock(skill, rect);

            if (ShowCompactRuneLetters)
                DrawSkillRuneHint(skill, rect.X, rect.Y, rect.Width);
        }

        // -----------------------------------------------------------------------
        // Nemesis Bracers portrait marker
        // -----------------------------------------------------------------------

        private bool HasNemesisBracers(IPlayer player)
        {
            if (player == null || player.Powers == null)
                return false;

            try
            {
                if (player.Powers.BuffIsActive(Hud.Sno.SnoPowers.NemesisBracers.Sno))
                    return true;
            }
            catch
            {
            }

            try
            {
                var buff = player.Powers.UsedLegendaryPowers.NemesisBracers;
                if (buff != null && buff.Active)
                    return true;
            }
            catch
            {
            }

            try
            {
                uint nemesisSno = Hud.Sno.SnoItems.Unique_Bracer_106_x1.Sno;
                if ((player.CubeSnoItem1 != null && player.CubeSnoItem1.Sno == nemesisSno) ||
                    (player.CubeSnoItem2 != null && player.CubeSnoItem2.Sno == nemesisSno) ||
                    (player.CubeSnoItem3 != null && player.CubeSnoItem3.Sno == nemesisSno) ||
                    (player.CubeSnoItem4 != null && player.CubeSnoItem4.Sno == nemesisSno))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private void DrawPortraitNemesisBracersMarker(IPlayer player)
        {
            if (player == null || player.PortraitUiElement == null || !HasNemesisBracers(player))
                return;

            var portraitRect = player.PortraitUiElement.Rectangle;
            if (portraitRect.Width <= 0 || portraitRect.Height <= 0)
                return;

            ISnoItem item = null;
            try
            {
                item = Hud.Sno.SnoItems.Unique_Bracer_106_x1;
            }
            catch
            {
                item = null;
            }

            float height = Math.Max(36.0f, portraitRect.Width * PortraitNemesisIconScale);
            float ratio = 0.50f;
            if (item != null && item.ItemWidth > 0 && item.ItemHeight > 0)
                ratio = (float)item.ItemWidth / item.ItemHeight;

            float width = height * ratio;
            float padding = 2.0f;
            float x = portraitRect.X + padding + PortraitNemesisIconXOffset;
            float y = portraitRect.Y + (portraitRect.Height * 0.42f) + PortraitNemesisIconYOffset;

            float minY = portraitRect.Y + padding;
            float maxY = portraitRect.Bottom - height - padding;
            if (maxY > minY)
                y = Math.Min(Math.Max(y, minY), maxY);

            bool drew = false;

            if (item != null)
                drew = DrawSnoItemInventoryIcon(item, x, y, width, height, 0.95f);

            if (!drew && _smallFont != null)
            {
                var textLayout = _smallFont.GetTextLayout("N");
                _smallFont.DrawText(
                    textLayout,
                    x + ((width - textLayout.Metrics.Width) * 0.5f),
                    y + ((height - textLayout.Metrics.Height) * 0.5f));
            }

            if (_nemesisIconBorderBrush != null)
                _nemesisIconBorderBrush.DrawRectangle(x, y, width, height);

            if (Hud.Window.CursorInsideRect(x, y, width, height))
            {
                try
                {
                    Hud.Render.SetHint(item != null ? item.NameLocalized : "Nemesis Bracers");
                }
                catch
                {
                }
            }
        }

        // -----------------------------------------------------------------------
        // Archon compact skillbar state
        // -----------------------------------------------------------------------

        private bool TryGetActiveArchonSeconds(IPlayer player, out double seconds)
        {
            seconds = 0.0d;

            if (player == null || player.Powers == null)
                return false;

            try
            {
                if (!player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Wizard_Archon.Sno, 2))
                    return false;
            }
            catch
            {
                return false;
            }

            try
            {
                var buff = player.Powers.GetBuff(Hud.Sno.SnoPowers.Wizard_Archon.Sno);
                if (buff != null && buff.TimeLeftSeconds != null)
                {
                    if (buff.TimeLeftSeconds.Length > 2 && buff.TimeLeftSeconds[2] > 0.0d)
                    {
                        seconds = buff.TimeLeftSeconds[2];
                        return true;
                    }

                    foreach (double value in buff.TimeLeftSeconds)
                    {
                        if (value > seconds)
                            seconds = value;
                    }
                }
            }
            catch
            {
            }

            return true;
        }

        private void DrawArchonSkillbarOverlay(PortraitBarLayout layout, int drawnSkills, double seconds)
        {
            if (drawnSkills <= 0)
                return;

            float width = (drawnSkills * layout.IconSize) + ((drawnSkills - 1) * layout.Gap);
            if (width <= 0.0f)
                return;

            if (_archonSkillOverlayBrush != null)
                _archonSkillOverlayBrush.DrawRectangle(layout.X, layout.Y, width, layout.IconSize);

            IFont font = _compactCooldownFont ?? _cooldownFont ?? _smallFont;
            if (font == null)
                return;

            string text = seconds > 0.0d
                ? "ARCHON: " + Math.Ceiling(seconds).ToString("F0", CultureInfo.InvariantCulture)
                : "ARCHON";

            var textLayout = font.GetTextLayout(text);
            float x = layout.X + ((width - textLayout.Metrics.Width) * 0.5f);
            float y = layout.Y + ((layout.IconSize - textLayout.Metrics.Height) * 0.5f);

            if (_warningBackBrush != null)
            {
                _warningBackBrush.DrawRectangle(
                    x - 2.0f,
                    y - 1.0f,
                    textLayout.Metrics.Width + 4.0f,
                    textLayout.Metrics.Height + 2.0f);
            }

            font.DrawText(textLayout, x, y);
        }

        private void DrawArchonStackIndicator(IPlayer player, PortraitBarLayout layout, int drawnSkills)
        {
            if (drawnSkills <= 0)
                return;

            List<ArchonStackDisplayInfo> stacks;
            if (!TryGetArchonStackInfo(player, out stacks) || stacks == null || stacks.Count <= 0)
                return;

            float skillWidth = (drawnSkills * layout.IconSize) + ((drawnSkills - 1) * layout.Gap);
            if (skillWidth <= 0.0f)
                return;

            float x = layout.X + ArchonStackIndicatorXOffset;
            float y = layout.Y + (layout.IconSize * 3.0f) + ArchonStackIndicatorYOffset;
            float gap = Math.Max(6.0f, layout.Gap + 6.0f);

            foreach (var stack in stacks)
            {
                if (stack == null || stack.Count <= 0)
                    continue;

                IFont font = stack.IsLingering
                    ? (_archonLingeringStackFont ?? _compactCooldownFont ?? _cooldownFont ?? _smallFont)
                    : (_archonCurrentStackFont ?? _compactCooldownFont ?? _cooldownFont ?? _smallFont);

                if (font == null)
                    continue;

                string text = stack.Count.ToString(CultureInfo.InvariantCulture);
                var textLayout = font.GetTextLayout(text);
                float opacity = GetArchonStackOpacity(stack.TimeLeftSeconds);

                DrawArchonStackText(font, text, textLayout, x, y, opacity);

                x += textLayout.Metrics.Width + gap;
            }
        }

        private void DrawArchonStackText(IFont font, string text, SharpDX.DirectWrite.TextLayout textLayout, float x, float y, float opacity)
        {
            if (font == null || textLayout == null || string.IsNullOrEmpty(text))
                return;

            IFont outlineFont = _archonStackOutlineFont;
            float oldTextOpacity = font.Opacity;
            float oldOutlineOpacity = outlineFont != null ? outlineFont.Opacity : 1.0f;

            try
            {
                if (outlineFont != null)
                {
                    outlineFont.Opacity = opacity;
                    var outlineLayout = outlineFont.GetTextLayout(text);

                    outlineFont.DrawText(outlineLayout, x - 2.0f, y);
                    outlineFont.DrawText(outlineLayout, x + 2.0f, y);
                    outlineFont.DrawText(outlineLayout, x, y - 2.0f);
                    outlineFont.DrawText(outlineLayout, x, y + 2.0f);
                    outlineFont.DrawText(outlineLayout, x - 1.5f, y - 1.5f);
                    outlineFont.DrawText(outlineLayout, x + 1.5f, y - 1.5f);
                    outlineFont.DrawText(outlineLayout, x - 1.5f, y + 1.5f);
                    outlineFont.DrawText(outlineLayout, x + 1.5f, y + 1.5f);
                }

                font.Opacity = opacity;
                font.DrawText(textLayout, x, y);
            }
            finally
            {
                font.Opacity = oldTextOpacity;
                if (outlineFont != null)
                    outlineFont.Opacity = oldOutlineOpacity;
            }
        }

        private bool TryGetArchonStackInfo(IPlayer player, out List<ArchonStackDisplayInfo> stacks)
        {
            stacks = null;

            if (player == null || player.Powers == null)
                return false;

            IBuff buff = null;
            try
            {
                buff = player.Powers.GetBuff(Hud.Sno.SnoPowers.Wizard_Archon.Sno);
            }
            catch
            {
                buff = null;
            }

            if (buff == null || buff.IconCounts == null || buff.IconCounts.Length <= 0)
                return false;

            var result = new List<ArchonStackDisplayInfo>(2);

            // Index 5 is the retained/lingering Archon stack icon observed during double-stack windows.
            // Index 2 is the current active Archon form stack icon.
            AddArchonStackInfo(buff, 5, true, result, true);
            AddArchonStackInfo(buff, 2, false, result, true);

            if (result.Count <= 0)
            {
                for (int i = 0; i < buff.IconCounts.Length && result.Count < 2; i++)
                {
                    if (i == 2 || i == 5)
                        continue;

                    AddArchonStackInfo(buff, i, false, result, false);
                }
            }

            if (result.Count <= 0)
                return false;

            stacks = result;
            return true;
        }

        private void AddArchonStackInfo(IBuff buff, int iconIndex, bool isLingering, List<ArchonStackDisplayInfo> stacks, bool allowSingleStack)
        {
            if (buff == null || buff.IconCounts == null || stacks == null || iconIndex < 0 || iconIndex >= buff.IconCounts.Length)
                return;

            int count = buff.IconCounts[iconIndex];
            if (count <= 0)
                return;

            if (!allowSingleStack && count <= 1)
                return;

            double timeLeft = 0.0d;
            try
            {
                if (buff.TimeLeftSeconds != null && iconIndex < buff.TimeLeftSeconds.Length)
                    timeLeft = buff.TimeLeftSeconds[iconIndex];
            }
            catch
            {
                timeLeft = 0.0d;
            }

            stacks.Add(new ArchonStackDisplayInfo
            {
                Count = count,
                TimeLeftSeconds = timeLeft,
                IsLingering = isLingering
            });
        }

        private float GetArchonStackOpacity(double secondsLeft)
        {
            const double minimumOpacity = 0.50d;

            if (secondsLeft <= 0.0d)
                return (float)minimumOpacity;

            double fadeStart = Math.Max(1.0d, ArchonStackFadeStartSeconds);
            if (secondsLeft >= fadeStart)
                return 1.0f;

            double ratio = Math.Max(0.0d, Math.Min(1.0d, secondsLeft / fadeStart));
            return (float)(minimumOpacity + (ratio * (1.0d - minimumOpacity)));
        }

        private sealed class ArchonStackDisplayInfo
        {
            public int Count;
            public double TimeLeftSeconds;
            public bool IsLingering;
        }

        // -----------------------------------------------------------------------
        // Cooldown clock overlay
        // -----------------------------------------------------------------------

        private void DrawSkillCooldownClock(IPlayerSkill skill, DXRectangleF rect)
        {
            DrawSkillCooldownClock(skill, rect, _compactCooldownFont ?? _cooldownFont);
        }

        private void DrawSkillCooldownClock(IPlayerSkill skill, DXRectangleF rect, IFont cooldownFont)
        {
            if (skill == null)
                return;

            try
            {
                if (!skill.IsOnCooldown)
                    return;

                if (skill.CooldownFinishTick <= Hud.Game.CurrentGameTick)
                    return;

                double total     = (skill.CooldownFinishTick - skill.CooldownStartTick) / 60.0d;
                double remaining = (skill.CooldownFinishTick - Hud.Game.CurrentGameTick) / 60.0d;
                double elapsed   = total - remaining;

                if (total <= 0.0d || remaining <= 0.0d)
                    return;

                if (elapsed < 0.0d)
                    elapsed = 0.0d;

                DrawCooldownRemainingWedge(rect, elapsed, remaining);

                string text = remaining > 1.0d
                    ? remaining.ToString("F0", CultureInfo.InvariantCulture)
                    : remaining.ToString("F1", CultureInfo.InvariantCulture);

                IFont font = cooldownFont ?? _cooldownFont;
                if (font == null)
                    return;

                var textLayout = font.GetTextLayout(text);

                font.DrawText(
                    textLayout,
                    rect.X + (rect.Width - (float)Math.Ceiling(textLayout.Metrics.Width)) / 2.0f,
                    rect.Y + (rect.Height - textLayout.Metrics.Height) / 2.0f);
            }
            catch
            {
            }
        }

        private void DrawCooldownRemainingWedge(DXRectangleF rect, double elapsed, double remaining)
        {
            if (_cooldownClockBrush == null)
                return;

            double total = elapsed + remaining;
            if (total <= 0.0d)
                return;

            double progress = elapsed / total;

            if (progress < 0.0d)
                progress = 0.0d;

            if (progress >= 0.999d)
                return;

            int startAngle = Convert.ToInt32(360.0d * progress);
            int endAngle   = 360;

            _cooldownClockBrush.Opacity = 0.82f;
            DrawClockWedge(rect, startAngle, endAngle, _cooldownClockBrush);
        }

        private void DrawClockWedge(DXRectangleF rect, int startAngle, int endAngle, IBrush brush)
        {
            if (brush == null)
                return;

            if (endAngle <= startAngle)
                return;

            float radius = Math.Min(rect.Width, rect.Height) * 0.56f;
            DXVector2 center = rect.Center;

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(center, FigureBegin.Filled);

                    for (int angle = startAngle; angle <= endAngle; angle++)
                    {
                        float mx = radius * (float)Math.Cos((angle - 90) * Math.PI / 180.0f);
                        float my = radius * (float)Math.Sin((angle - 90) * Math.PI / 180.0f);

                        sink.AddLine(new DXVector2(center.X + mx, center.Y + my));
                    }

                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        // -----------------------------------------------------------------------
        // F12 expanded portrait build details (two-row layout)
        // -----------------------------------------------------------------------
        // Row 1: skillbar (already drawn by compact package) + inline equipped gems.
        // Row 2: passives + cube powers, starting directly below row 1.
        // -----------------------------------------------------------------------

        private void DrawExpandedPortraitBuildDetails(IPlayer player)
        {
            if (player == null)
                return;

            PortraitBarLayout layout;
            if (!TryGetPortraitBarLayout(player, out layout))
                return;

            float row2Size = Math.Max(22.0f, layout.IconSize * ExpandedPortraitDetailIconScale);

            // Row 1: equipped legendary gems extend the skillbar to the right.
            if (ShowExpandedLegendaryGems)
                DrawExpandedPortraitLegendaryGemsInline(player, layout, layout.IconSize);

            // Row 2: passives + cube powers below the skillbar, left-aligned.
            float x = layout.X;
            float y = layout.Y + layout.IconSize + ExpandedBuildRowYOffset;

            if (ShowExpandedPassives && ShowPassives)
                DrawExpandedPortraitPassives(player, ref x, ref y, row2Size, layout);

            if (ShowExpandedCubeItems && ShowCubeItems)
                DrawExpandedPortraitCubeItems(player, ref x, ref y, row2Size, layout);
        }

        // -----------------------------------------------------------------------
        // Row 1 inline equipped gem icons (F12 on, beside the skillbar)
        // -----------------------------------------------------------------------

        private void DrawExpandedPortraitLegendaryGemsInline(
            IPlayer player,
            PortraitBarLayout layout,
            float baseSize)
        {
            if (player == null)
                return;

            float size = Math.Max(20.0f, baseSize * ExpandedInlineGemIconScale);
            float x    = layout.SkillBarEndX + ExpandedInlineGemGap;
            float y    = layout.Y + ((layout.IconSize - size) * 0.5f);

            var gems = GetLegendaryGemDisplayInfos(player)
                .Take(Math.Max(0, MaxPortraitLegendaryGemIcons))
                .ToList();

            foreach (var gem in gems)
            {
                DrawLegendaryGemDisplayIcon(gem, x, y, size);
                x += size + ExpandedInlineGemGap;
            }
        }

        // -----------------------------------------------------------------------
        // Legacy below-row gem drawer — RETAINED but NOT CALLED.
        // DrawExpandedPortraitBuildDetails no longer calls this method.
        // -----------------------------------------------------------------------
        private void DrawExpandedPortraitLegendaryGems(IPlayer player, PortraitBarLayout layout)
        {
            // LEGACY: previously placed gems below the skillbar.
            // Replaced by DrawExpandedPortraitLegendaryGemsInline for the two-row layout.
            // Do not call this method.
        }

        // -----------------------------------------------------------------------
        // Row 2 wrap helpers — wrap at screen edge back to layout.X
        // -----------------------------------------------------------------------

        // Standard overload: square items (passives).  Y advances by size.
        private void WrapExpandedPortraitDetailIfNeeded(
            ref float x,
            ref float y,
            float size,
            PortraitBarLayout layout)
        {
            float rightLimit = Hud.Window.Size.Width - 6.0f;

            if (x + size <= rightLimit)
                return;

            x  = layout.X;
            y += size + ExpandedPortraitDetailGap;
        }

        // Rectangular-item overload: Y advances by itemHeight, not width.
        private void WrapExpandedPortraitDetailIfNeeded(
            ref float x,
            ref float y,
            float itemWidth,
            float itemHeight,
            PortraitBarLayout layout)
        {
            float rightLimit = Hud.Window.Size.Width - 6.0f;

            if (x + itemWidth <= rightLimit)
                return;

            x  = layout.X;
            y += itemHeight + ExpandedPortraitDetailGap;
        }

        // -----------------------------------------------------------------------
        // Passive powers (row 2)
        // -----------------------------------------------------------------------

        private IEnumerable<ISnoPower> GetPlayerPassivePowers(IPlayer player)
        {
            List<ISnoPower> result = new List<ISnoPower>();

            if (player == null)
                return result;

            if (player.Powers != null)
            {
                try
                {
                    if (player.Powers.PassiveSlots != null)
                        result.AddRange(player.Powers.PassiveSlots.Where(p => p != null));
                }
                catch
                {
                }

                if (result.Count == 0)
                {
                    try
                    {
                        if (player.Powers.UsedPassives != null)
                            result.AddRange(player.Powers.UsedPassives.Where(p => p != null));
                    }
                    catch
                    {
                    }
                }
            }

            if (result.Count > 0)
            {
                CachePassivePowers(player, result);
                return result;
            }

            return GetCachedPassivePowers(player);
        }

        private void DrawExpandedPortraitPassives(
            IPlayer player,
            ref float x,
            ref float y,
            float size,
            PortraitBarLayout layout)
        {
            foreach (var passive in GetPlayerPassivePowers(player).Take(4))
            {
                WrapExpandedPortraitDetailIfNeeded(ref x, ref y, size, layout);
                DrawPassive(passive, x, y, size, false);
                x += size + ExpandedPortraitDetailGap;
            }
        }

        // -----------------------------------------------------------------------
        // Cube powers (row 2)
        // -----------------------------------------------------------------------

        private IEnumerable<ISnoItem> GetPlayerCubeItems(IPlayer player)
        {
            List<ISnoItem> result = new List<ISnoItem>();

            if (player == null)
                return result;

            ISnoItem item = null;

            try { item = player.CubeSnoItem1; } catch { item = null; }
            if (item != null) result.Add(item);

            try { item = player.CubeSnoItem2; } catch { item = null; }
            if (item != null) result.Add(item);

            try { item = player.CubeSnoItem3; } catch { item = null; }
            if (item != null) result.Add(item);

            try { item = player.CubeSnoItem4; } catch { item = null; }
            if (item != null) result.Add(item);

            if (result.Count > 0)
            {
                CacheCubeItems(player, result);
                return result;
            }

            return GetCachedCubeItems(player);
        }

        private void DrawExpandedPortraitCubeItems(
            IPlayer player,
            ref float x,
            ref float y,
            float size,
            PortraitBarLayout layout)
        {
            // Cube icon boxes are rectangular: same width as other row-2 icons,
            // but taller so staves, swords, and other portrait-aspect items fit
            // cleanly without distortion.  Rings center naturally with empty space.
            float cubeH = size * CubeIconHeightMultiplier;

            foreach (var item in GetPlayerCubeItems(player).Take(4))
            {
                WrapExpandedPortraitDetailIfNeeded(ref x, ref y, size, cubeH, layout);
                DrawCubeItemIcon(item, x, y, size, cubeH, false);
                x += size + ExpandedPortraitDetailGap;
            }
        }

        // -----------------------------------------------------------------------
        // Legendary gem display info
        // -----------------------------------------------------------------------

        private sealed class LegendaryGemDisplayInfo
        {
            public string   Name;
            public string   ShortName;
            public ISnoItem SnoItem;
            public IBuff    Primary;
            public IBuff    Secondary;
            public IBuff    DisplayBuff;
        }

        private IEnumerable<LegendaryGemDisplayInfo> GetLegendaryGemDisplayInfos(IPlayer player)
        {
            List<LegendaryGemDisplayInfo> result = new List<LegendaryGemDisplayInfo>();

            if (player == null)
                return result;

            if (player.IsMe)
                return GetSelfEquippedLegendaryGemDisplayInfos();

            result = ReadCurrentRemoteLegendaryGemDisplayInfos(player);
            if (result != null && result.Count > 0)
            {
                CacheLegendaryGemDisplayInfos(player, result);
                return result;
            }

            return GetCachedLegendaryGemDisplayInfos(player);
        }

        private List<LegendaryGemDisplayInfo> ReadCurrentRemoteLegendaryGemDisplayInfos(IPlayer player)
        {
            List<LegendaryGemDisplayInfo> result = new List<LegendaryGemDisplayInfo>();

            if (player == null || player.Powers == null || player.Powers.UsedLegendaryGems == null)
                return result;

            var gems = player.Powers.UsedLegendaryGems;

            try
            {
                var items = Hud.Sno.SnoItems;

                AddLegendaryGemDisplay(result, "Bane of the Powerful",                    "BotP", items.Unique_Gem_001_x1, gems.BaneOfThePowerfulPrimary,                gems.BaneOfThePowerfulSecondary);
                AddLegendaryGemDisplay(result, "Bane of the Stricken",                    "Str",  items.Unique_Gem_018_x1, gems.BaneOfTheStrickenPrimary,                gems.BaneOfTheStrickenSecondary);
                AddLegendaryGemDisplay(result, "Bane of the Trapped",                     "BotT", items.Unique_Gem_002_x1, gems.BaneOfTheTrappedPrimary,                 gems.BaneOfTheTrappedSecondary);
                AddLegendaryGemDisplay(result, "Boon of the Hoarder",                     "Boon", items.Unique_Gem_014_x1, gems.BoonOfTheHoarderPrimary,                 gems.BoonOfTheHoarderSecondary);
                AddLegendaryGemDisplay(result, "Boyarsky's Chip",                         "Boy",  items.Unique_Gem_020_x1, gems.BoyarskysChipPrimary,                   gems.BoyarskysChipSecondary);
                AddLegendaryGemDisplay(result, "Enforcer",                                "Enf",  items.Unique_Gem_010_x1, gems.EnforcerPrimary,                        gems.EnforcerSecondary);
                AddLegendaryGemDisplay(result, "Esoteric Alteration",                     "Eso",  items.Unique_Gem_016_x1, gems.EsotericAlterationPrimary,              gems.EsotericAlterationSecondary);
                AddLegendaryGemDisplay(result, "Gem of Ease",                             "Ease", items.Unique_Gem_003_x1, gems.GemOfEasePrimary,                       gems.GemOfEaseSecondary);
                AddLegendaryGemDisplay(result, "Gem of Efficacious Toxin",                "Tox",  items.Unique_Gem_005_x1, gems.GemOfEfficaciousToxinPrimary,           gems.GemOfEfficaciousToxinSecondary);
                AddLegendaryGemDisplay(result, "Gogok of Swiftness",                      "Gog",  items.Unique_Gem_008_x1, gems.GogokOfSwiftnessPrimary,                gems.GogokOfSwiftnessSecondary);
                AddLegendaryGemDisplay(result, "Iceblink",                                "Ice",  items.Unique_Gem_021_x1, gems.IceblinkPrimary,                        gems.IceblinkSecondary);
                AddLegendaryGemDisplay(result, "Invigorating Gemstone",                   "Inv",  items.Unique_Gem_009_x1, gems.InvigoratingGemstonePrimary,            gems.InvigoratingGemstoneSecondary);
                AddLegendaryGemDisplay(result, "Legacy of Dreams",                        "LoD",  items.Unique_Gem_023_x1, gems.LegacyOfDreamsPrimary,                  gems.LegacyOfDreamsSecondary);
                AddLegendaryGemDisplay(result, "Mirinae, Teardrop of the Starweaver",     "Mir",  items.Unique_Gem_007_x1, gems.MirinaeTeardropOfTheStarweaverPrimary,  gems.MirinaeTeardropOfTheStarweaverSecondary);
                AddLegendaryGemDisplay(result, "Molten Wildebeest's Gizzard",             "MWG",  items.Unique_Gem_017_x1, gems.MoltenWildebeestsGizzardPrimary,        gems.MoltenWildebeestsGizzardSecondary);
                AddLegendaryGemDisplay(result, "Moratorium",                              "Mor",  items.Unique_Gem_011_x1, gems.MoratoriumPrimary,                      gems.MoratoriumSecondary);
                AddLegendaryGemDisplay(result, "Mutilation Guard",                        "Mut",  items.Unique_Gem_019_x1, gems.MutilationGuardPrimary,                 gems.MutilationGuardSecondary);
                AddLegendaryGemDisplay(result, "Pain Enhancer",                           "Pain", items.Unique_Gem_006_x1, gems.PainEnhancerPrimary,                    gems.PainEnhancerSecondary);
                AddLegendaryGemDisplay(result, "Red Soul Shard",                          "RSS",  items.Unique_Gem_022_x1, gems.RedSoulShardPrimary,                    gems.RedSoulShardSecondary);
                AddLegendaryGemDisplay(result, "Simplicity's Strength",                   "Simp", items.Unique_Gem_013_x1, gems.SimplicitysStrengthPrimary,             gems.SimplicitysStrengthSecondary);
                AddLegendaryGemDisplay(result, "Taeguk",                                  "Tae",  items.Unique_Gem_015_x1, gems.TaegukPrimary,                          gems.TaegukSecondary);
                AddLegendaryGemDisplay(result, "Whisper of Atonement",                    "WoA",  items.P73_Unique_Gem_25, gems.WhisperOfAtonementPrimary,              gems.WhisperOfAtonementSecondary);
                AddLegendaryGemDisplay(result, "Wreath of Lightning",                     "WoL",  items.Unique_Gem_004_x1, gems.WreathOfLightningPrimary,               gems.WreathOfLightningSecondary);
                AddLegendaryGemDisplay(result, "Zei's Stone of Vengeance",                "Zei",  items.Unique_Gem_012_x1, gems.ZeisStoneOfVengeancePrimary,            gems.ZeisStoneOfVengeanceSecondary);
            }
            catch
            {
            }

            return result;
        }

        private List<LegendaryGemDisplayInfo> GetSelfEquippedLegendaryGemDisplayInfos()
        {
            List<LegendaryGemDisplayInfo> result = new List<LegendaryGemDisplayInfo>();

            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Items == null)
                    return result;

                HashSet<uint> seen = new HashSet<uint>();

                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || !IsLocalPlayerEquippedItemLocation(item.Location) || item.ItemsInSocket == null)
                        continue;

                    foreach (var socketed in item.ItemsInSocket)
                    {
                        if (!IsSocketedLegendaryGem(socketed) || socketed.SnoItem == null)
                            continue;

                        uint sno = 0;
                        try { sno = socketed.SnoItem.Sno; } catch { sno = 0; }

                        if (sno != 0)
                        {
                            if (seen.Contains(sno))
                                continue;

                            seen.Add(sno);
                        }

                        result.Add(new LegendaryGemDisplayInfo()
                        {
                            Name        = GetLocalLegendaryGemDisplayName(socketed),
                            ShortName   = GetLocalLegendaryGemShortName(socketed),
                            SnoItem     = socketed.SnoItem,
                            Primary     = null,
                            Secondary   = null,
                            DisplayBuff = null,
                        });
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private bool IsLocalPlayerEquippedItemLocation(ItemLocation location)
        {
            return
                location == ItemLocation.Head ||
                location == ItemLocation.Torso ||
                location == ItemLocation.RightHand ||
                location == ItemLocation.LeftHand ||
                location == ItemLocation.Hands ||
                location == ItemLocation.Waist ||
                location == ItemLocation.Feet ||
                location == ItemLocation.Shoulders ||
                location == ItemLocation.Legs ||
                location == ItemLocation.Bracers ||
                location == ItemLocation.LeftRing ||
                location == ItemLocation.RightRing ||
                location == ItemLocation.Neck;
        }

        private bool IsSocketedLegendaryGem(IItem item)
        {
            if (item == null || item.SnoItem == null)
                return false;

            try
            {
                return item.Quality == ItemQuality.Legendary && item.JewelRank > -1;
            }
            catch
            {
                return false;
            }
        }

        private string GetLocalLegendaryGemDisplayName(IItem item)
        {
            if (item == null)
                return "Legendary Gem";

            try
            {
                if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameEnglish))
                    return item.SnoItem.NameEnglish;
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(item.FullNameEnglish))
                    return item.FullNameEnglish;
            }
            catch
            {
            }

            try
            {
                if (item.SnoItem != null && !string.IsNullOrEmpty(item.SnoItem.NameLocalized))
                    return item.SnoItem.NameLocalized;
            }
            catch
            {
            }

            return "Legendary Gem";
        }

        private string GetLocalLegendaryGemShortName(IItem item)
        {
            string name = GetLocalLegendaryGemDisplayName(item);

            if (string.IsNullOrEmpty(name)) return "Gem";
            if (name.IndexOf("Powerful", StringComparison.OrdinalIgnoreCase) >= 0) return "BotP";
            if (name.IndexOf("Stricken", StringComparison.OrdinalIgnoreCase) >= 0) return "Str";
            if (name.IndexOf("Trapped", StringComparison.OrdinalIgnoreCase) >= 0) return "BotT";
            if (name.IndexOf("Hoarder", StringComparison.OrdinalIgnoreCase) >= 0) return "Boon";
            if (name.IndexOf("Boyarsky", StringComparison.OrdinalIgnoreCase) >= 0) return "Boy";
            if (name.IndexOf("Enforcer", StringComparison.OrdinalIgnoreCase) >= 0) return "Enf";
            if (name.IndexOf("Esoteric", StringComparison.OrdinalIgnoreCase) >= 0) return "Eso";
            if (name.IndexOf("Ease", StringComparison.OrdinalIgnoreCase) >= 0) return "Ease";
            if (name.IndexOf("Toxin", StringComparison.OrdinalIgnoreCase) >= 0) return "Tox";
            if (name.IndexOf("Gogok", StringComparison.OrdinalIgnoreCase) >= 0) return "Gog";
            if (name.IndexOf("Iceblink", StringComparison.OrdinalIgnoreCase) >= 0) return "Ice";
            if (name.IndexOf("Invigorating", StringComparison.OrdinalIgnoreCase) >= 0) return "Inv";
            if (name.IndexOf("Dreams", StringComparison.OrdinalIgnoreCase) >= 0) return "LoD";
            if (name.IndexOf("Mirinae", StringComparison.OrdinalIgnoreCase) >= 0) return "Mir";
            if (name.IndexOf("Wildebeest", StringComparison.OrdinalIgnoreCase) >= 0) return "MWG";
            if (name.IndexOf("Moratorium", StringComparison.OrdinalIgnoreCase) >= 0) return "Mor";
            if (name.IndexOf("Mutilation", StringComparison.OrdinalIgnoreCase) >= 0) return "Mut";
            if (name.IndexOf("Pain Enhancer", StringComparison.OrdinalIgnoreCase) >= 0) return "Pain";
            if (name.IndexOf("Soul Shard", StringComparison.OrdinalIgnoreCase) >= 0) return "RSS";
            if (name.IndexOf("Simplicity", StringComparison.OrdinalIgnoreCase) >= 0) return "Simp";
            if (name.IndexOf("Taeguk", StringComparison.OrdinalIgnoreCase) >= 0) return "Tae";
            if (name.IndexOf("Atonement", StringComparison.OrdinalIgnoreCase) >= 0) return "WoA";
            if (name.IndexOf("Wreath", StringComparison.OrdinalIgnoreCase) >= 0) return "WoL";
            if (name.IndexOf("Zei", StringComparison.OrdinalIgnoreCase) >= 0) return "Zei";

            return "Gem";
        }

        private void AddLegendaryGemDisplay(
            List<LegendaryGemDisplayInfo> result,
            string name,
            string shortName,
            ISnoItem snoItem,
            IBuff primary,
            IBuff secondary)
        {
            if (result == null)
                return;

            IBuff displayBuff = GetPreferredLegendaryGemBuff(primary, secondary);
            if (displayBuff == null || displayBuff.SnoPower == null)
                return;

            result.Add(new LegendaryGemDisplayInfo()
            {
                Name        = name,
                ShortName   = shortName,
                SnoItem     = snoItem,
                Primary     = primary,
                Secondary   = secondary,
                DisplayBuff = displayBuff,
            });
        }

        private void CacheLegendaryGemDisplayInfos(IPlayer player, List<LegendaryGemDisplayInfo> gems)
        {
            if (player == null || gems == null || gems.Count == 0)
                return;

            var snapshot = CloneLegendaryGemDisplayInfos(gems);
            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
                _legendaryGemInfoCache[key] = CloneLegendaryGemDisplayInfos(snapshot);
        }

        private List<LegendaryGemDisplayInfo> GetCachedLegendaryGemDisplayInfos(IPlayer player)
        {
            List<LegendaryGemDisplayInfo> empty = new List<LegendaryGemDisplayInfo>();

            if (player == null)
                return empty;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
            {
                List<LegendaryGemDisplayInfo> cached;
                if (!string.IsNullOrEmpty(key) && _legendaryGemInfoCache.TryGetValue(key, out cached) && cached != null && cached.Count > 0)
                    return CloneLegendaryGemDisplayInfos(cached);
            }

            return empty;
        }

        private List<LegendaryGemDisplayInfo> CloneLegendaryGemDisplayInfos(IEnumerable<LegendaryGemDisplayInfo> source)
        {
            List<LegendaryGemDisplayInfo> result = new List<LegendaryGemDisplayInfo>();

            if (source == null)
                return result;

            foreach (var gem in source)
            {
                if (gem == null)
                    continue;

                result.Add(new LegendaryGemDisplayInfo
                {
                    Name = gem.Name,
                    ShortName = gem.ShortName,
                    SnoItem = gem.SnoItem,
                    Primary = gem.Primary,
                    Secondary = gem.Secondary,
                    DisplayBuff = gem.DisplayBuff
                });
            }

            return result;
        }

        private void CachePassivePowers(IPlayer player, List<ISnoPower> powers)
        {
            if (player == null || powers == null || powers.Count == 0)
                return;

            List<ISnoPower> snapshot = powers.Where(p => p != null).ToList();
            if (snapshot.Count == 0)
                return;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
                _passivePowerCache[key] = snapshot.ToList();
        }

        private List<ISnoPower> GetCachedPassivePowers(IPlayer player)
        {
            List<ISnoPower> empty = new List<ISnoPower>();

            if (player == null)
                return empty;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
            {
                List<ISnoPower> cached;
                if (!string.IsNullOrEmpty(key) && _passivePowerCache.TryGetValue(key, out cached) && cached != null && cached.Count > 0)
                    return cached.Where(p => p != null).ToList();
            }

            return empty;
        }

        private void CacheCubeItems(IPlayer player, List<ISnoItem> items)
        {
            if (player == null || items == null || items.Count == 0)
                return;

            List<ISnoItem> snapshot = items.Where(i => i != null).ToList();
            if (snapshot.Count == 0)
                return;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
                _cubeItemCache[key] = snapshot.ToList();
        }

        private List<ISnoItem> GetCachedCubeItems(IPlayer player)
        {
            List<ISnoItem> empty = new List<ISnoItem>();

            if (player == null)
                return empty;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
            {
                List<ISnoItem> cached;
                if (!string.IsNullOrEmpty(key) && _cubeItemCache.TryGetValue(key, out cached) && cached != null && cached.Count > 0)
                    return cached.Where(i => i != null).ToList();
            }

            return empty;
        }

        private IBuff GetPreferredLegendaryGemBuff(IBuff primary, IBuff secondary)
        {
            if (LegendaryGemBuffLooksUsable(primary))
                return primary;

            if (LegendaryGemBuffLooksUsable(secondary))
                return secondary;

            return null;
        }

        private bool LegendaryGemBuffLooksUsable(IBuff buff)
        {
            if (buff == null || buff.SnoPower == null)
                return false;

            // For remote party players, UsedLegendaryGems is the best available
            // equipment signal. Passive gems may not expose Active/IconCounts
            // reliably, so accept any buff that has a valid SnoPower.
            return true;
        }

        // -----------------------------------------------------------------------
        // Legacy compact gem stub — intentionally empty, not called
        // -----------------------------------------------------------------------

        private void DrawCompactPortraitLegendaryGems(IPlayer player)
        {
            // LEGACY STUB: equipped legendary gems are now drawn only in F12 mode
            // via DrawExpandedPortraitLegendaryGemsInline.
            // This method is intentionally empty.
        }

        // -----------------------------------------------------------------------
        // Gem icon texture helper
        // -----------------------------------------------------------------------

        private ITexture GetBuffTexture(IBuff buff)
        {
            if (buff == null || buff.SnoPower == null)
                return null;

            try
            {
                if (buff.SnoPower.Icons != null)
                {
                    foreach (var icon in buff.SnoPower.Icons)
                    {
                        if (icon.Exists && icon.TextureId != 0)
                        {
                            var texture = Hud.Texture.GetTexture(icon.TextureId);
                            if (texture != null)
                                return texture;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (buff.SnoPower.NormalIconTextureId != 0)
                    return Hud.Texture.GetTexture(buff.SnoPower.NormalIconTextureId);
            }
            catch
            {
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // Legendary gem icon drawing (F12 inline gems)
        // -----------------------------------------------------------------------

        private void DrawLegendaryGemDisplayIcon(LegendaryGemDisplayInfo gem, float x, float y, float size)
        {
            if (gem == null)
                return;

            // Dark background fill.
            if (_portraitGemBackBrush != null)
                _portraitGemBackBrush.DrawRectangle(x, y, size, size);

            bool drew = false;

            if (gem.SnoItem != null)
                drew = DrawSnoItemIconTexture(gem.SnoItem, x, y, size, size, 0.95f);

            if (!drew && gem.DisplayBuff != null && gem.DisplayBuff.SnoPower != null)
            {
                try
                {
                    var texture = GetBuffTexture(gem.DisplayBuff);
                    if (texture != null)
                    {
                        texture.Draw(x, y, size, size, 0.95f);
                        drew = true;
                    }
                }
                catch
                {
                }
            }

            if (!drew)
            {
                string fallback = !string.IsNullOrEmpty(gem.ShortName) ? gem.ShortName : "Gem";
                IFont font = _smallFont ?? _portraitSmallFont;
                if (font != null)
                {
                    var layout = font.GetTextLayout(fallback);
                    font.DrawText(
                        layout,
                        x + ((size - layout.Metrics.Width)  * 0.5f),
                        y + ((size - layout.Metrics.Height) * 0.5f));
                }
            }

            // Thick black outer border for strong contrast.
            if (_gemIconBorderBrush != null)
                _gemIconBorderBrush.DrawRectangle(x, y, size, size);

            // Thin colored accent drawn just inside the black border.
            if (_gemAccentBrush != null && size > 8.0f)
                _gemAccentBrush.DrawRectangle(x + 1.5f, y + 1.5f, size - 3.0f, size - 3.0f);

            if (Hud.Window.CursorInsideRect(x, y, size, size))
            {
                try
                {
                    Hud.Render.SetHint(gem.Name);
                }
                catch
                {
                }
            }
        }

        // -----------------------------------------------------------------------
        // Gem-upgrade portrait counter (Urshi context)
        // -----------------------------------------------------------------------

        private void DrawCompactPortraitGemUpgradeCount(IPlayer player)
        {
            if (player == null || player.PortraitUiElement == null)
                return;

            int left = GetGemUpgradesLeft(player);
            if (left < 0)
                left = 0;

            // Respect the ShowZeroGemUpgradeCountsInContext toggle.
            if (left == 0 && !ShowZeroGemUpgradeCountsInContext)
                return;

            var portraitRect = player.PortraitUiElement.Rectangle;
            if (portraitRect.Width <= 0 || portraitRect.Height <= 0)
                return;

            // Format: self shows "Gem Ups: 0" (yellow), others show "Gem Ups: +N" (grey).
            string valueText = left > 0
                ? "+" + left.ToString(CultureInfo.InvariantCulture)
                : "0";

            string text = "Gem Ups: " + valueText;

            IFont font = player.IsMe ? _selfGemFont : _otherGemFont;
            if (font == null)
                font = player.IsMe ? _warningFont : _smallFont;

            var layout = font.GetTextLayout(text);

            float x = portraitRect.Right + PortraitSkillXOffset + GemUpgradeCounterXOffset;
            float y = portraitRect.Bottom - layout.Metrics.Height - 2.0f + GemUpgradeCounterYOffset;

            if (_warningBackBrush != null)
            {
                _warningBackBrush.DrawRectangle(
                    x - 2.0f,
                    y - 1.0f,
                    layout.Metrics.Width + 4.0f,
                    layout.Metrics.Height + 2.0f);
            }

            font.DrawText(layout, x, y);
        }

        // -----------------------------------------------------------------------
        // Optional legacy top-center gem upgrade reminder (ShowGemUpgradeReminder)
        // -----------------------------------------------------------------------

        private void DrawGemUpgradeReminder()
        {
            var players = GetPartyPlayers();
            if (players.Count == 0)
                return;

            List<string> lines = new List<string>();

            foreach (var player in players)
            {
                int left = GetGemUpgradesLeft(player);
                if (left <= 0)
                    continue;

                string name = GetPlayerDisplayName(player);
                lines.Add(name + " has " + left.ToString(CultureInfo.InvariantCulture)
                    + " gem upgrade" + (left == 1 ? "" : "s") + " left");
            }

            if (lines.Count == 0)
                return;

            float x = ReminderX > 0.0f ? ReminderX : Hud.Window.Size.Width * 0.5f - 170.0f;
            float y = ReminderY > 0.0f ? ReminderY : Hud.Window.Size.Height * 0.18f;

            float width      = 340.0f;
            float lineHeight = 15.0f;
            float height     = 8.0f + lines.Count * lineHeight;

            if (_warningBackBrush != null)
                _warningBackBrush.DrawRectangle(x - 6.0f, y - 4.0f, width, height);

            if (_borderBrush != null)
                _borderBrush.DrawRectangle(x - 6.0f, y - 4.0f, width, height);

            for (int i = 0; i < lines.Count; i++)
            {
                var lineLayout = _warningFont.GetTextLayout(lines[i]);
                _warningFont.DrawText(lineLayout, x, y + i * lineHeight);
            }
        }

        // -----------------------------------------------------------------------
        // Portrait-hover stat table replacement
        // -----------------------------------------------------------------------

        private class PortraitHoverStatCell
        {
            public string Header;
            public string Value;
            public float Width;
        }

        private class PortraitHoverPlayerStatsSnapshot
        {
            public string Name;
            public string Ehp;
            public string Dps;
            public string Aps;
            public string Chc;
            public string Chd;
            public string Cdr;
            public string Rcr;
            public string Ad;
            public string Solo;
            public bool HasUsableCoreStats;
            public int LastSeenTick;
        }

        private void DrawPortraitHoverStatsIfNeeded(List<IPlayer> players)
        {
            if (!ShowPortraitHoverStats)
                return;

            if (players == null || players.Count == 0)
                return;

            IPlayer hoveredPlayer;
            if (!TryGetHoveredPortraitPlayer(players, out hoveredPlayer))
                return;

            if (PortraitHoverStatsShowAllPlayers)
                DrawPortraitHoverStatsTable(players);
            else
                DrawPortraitHoverStatsTable(new List<IPlayer> { hoveredPlayer });
        }

        private bool TryGetHoveredPortraitPlayer(List<IPlayer> players, out IPlayer hoveredPlayer)
        {
            hoveredPlayer = null;

            try
            {
                if (players == null)
                    return false;

                foreach (var player in players)
                {
                    if (player == null || !player.IsInGame)
                        continue;

                    IUiElement portrait = player.PortraitUiElement;
                    if (portrait == null)
                        continue;

                    try
                    {
                        if (!portrait.Visible && portrait.ReplacementWhenNotVisible != null)
                            portrait = portrait.ReplacementWhenNotVisible;
                    }
                    catch
                    {
                    }

                    if (portrait == null)
                        continue;

                    var rect = portrait.Rectangle;
                    if (rect.Width <= 0.0f || rect.Height <= 0.0f)
                        continue;

                    if (Hud.Window.CursorInsideRect(rect.X, rect.Y, rect.Width, rect.Height))
                    {
                        hoveredPlayer = player;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void DrawPortraitHoverStatsTable(List<IPlayer> players)
        {
            if (players == null || players.Count == 0)
                return;

            var rows = new List<List<PortraitHoverStatCell>>();
            foreach (var player in players)
            {
                if (player == null || !player.IsInGame)
                    continue;

                var row = BuildPortraitHoverStatCells(player);
                if (row != null && row.Count > 0)
                    rows.Add(row);
            }

            if (rows.Count == 0)
                return;

            var headerCells = BuildPortraitHoverHeaderCells();
            if (headerCells == null || headerCells.Count == 0)
                return;

            float rowHeight = Math.Max(14.0f, PortraitHoverStatsCellHeight);
            float tableWidth = 0.0f;
            foreach (var cell in headerCells)
            {
                if (cell != null)
                    tableWidth += Math.Max(20.0f, cell.Width);
            }

            // Header row + one row for every actual in-game party member. This intentionally
            // covers the native FreeHUD all-player hover table when any portrait is hovered.
            float tableHeight = rowHeight * (rows.Count + 1);
            float x = PortraitHoverStatsX;
            float y = PortraitHoverStatsY;

            if (x <= 0.0f)
                x = (Hud.Window.Size.Width - tableWidth) * 0.5f;

            // Keep table inside the game window when users tune the offsets.
            if (x < 0.0f)
                x = 0.0f;
            if (x + tableWidth > Hud.Window.Size.Width)
                x = Math.Max(0.0f, Hud.Window.Size.Width - tableWidth);
            if (y < 0.0f)
                y = 0.0f;

            // Opaque cover rectangle. This is the plugin-only workaround for native portrait info.
            // It now uses the full party-table height, not a single hovered-player row.
            if (PortraitHoverStatsCoverNativeInfo && _portraitHoverBackBrush != null)
            {
                float coverPadX = Math.Max(0.0f, PortraitHoverStatsCoverPaddingX);
                float coverPadY = Math.Max(0.0f, PortraitHoverStatsCoverPaddingY);
                _portraitHoverBackBrush.DrawRectangle(
                    x - coverPadX,
                    y - coverPadY,
                    tableWidth + coverPadX * 2.0f,
                    tableHeight + coverPadY * 2.0f);
            }

            DrawPortraitHoverStatsRow(headerCells, x, y, rowHeight, true);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                DrawPortraitHoverStatsRow(
                    rows[rowIndex],
                    x,
                    y + rowHeight * (rowIndex + 1),
                    rowHeight,
                    false);
            }
        }

        private void DrawPortraitHoverStatsRow(List<PortraitHoverStatCell> cells, float x, float y, float rowHeight, bool header)
        {
            if (cells == null || cells.Count == 0)
                return;

            float cellX = x;
            foreach (var cell in cells)
            {
                if (cell == null)
                    continue;

                float width = Math.Max(20.0f, cell.Width);

                if (header)
                {
                    if (_portraitHoverHeaderBrush != null)
                        _portraitHoverHeaderBrush.DrawRectangle(cellX, y, width, rowHeight);
                }
                else
                {
                    if (_portraitHoverCellBrush != null)
                        _portraitHoverCellBrush.DrawRectangle(cellX, y, width, rowHeight);
                }

                if (_portraitHoverBorderBrush != null)
                    _portraitHoverBorderBrush.DrawRectangle(cellX, y, width, rowHeight);

                DrawPortraitHoverCellText(
                    header ? cell.Header : cell.Value,
                    header ? _portraitHoverHeaderFont : _portraitHoverValueFont,
                    cellX,
                    y,
                    width,
                    rowHeight,
                    true);

                cellX += width;
            }
        }

        private List<PortraitHoverStatCell> BuildPortraitHoverHeaderCells()
        {
            var cells = new List<PortraitHoverStatCell>();

            float nameWidth = Math.Max(60.0f, PortraitHoverStatsNameColumnWidth);
            float statWidth = Math.Max(34.0f, PortraitHoverStatsStatColumnWidth);
            float soloWidth = Math.Max(34.0f, PortraitHoverStatsSoloColumnWidth);

            cells.Add(new PortraitHoverStatCell { Header = "PLAYER", Value = "PLAYER", Width = nameWidth });
            cells.Add(new PortraitHoverStatCell { Header = "EHP", Value = "EHP", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "DPS", Value = "DPS", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "APS", Value = "APS", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CHC", Value = "CHC", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CHD", Value = "CHD", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CDR", Value = "CDR", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "RCR", Value = "RCR", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "AD", Value = "AD", Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "SOLO", Value = "SOLO", Width = soloWidth });

            return cells;
        }

        private List<PortraitHoverStatCell> BuildPortraitHoverStatCells(IPlayer player)
        {
            var snapshot = GetPortraitHoverPlayerStats(player);
            var cells = new List<PortraitHoverStatCell>();

            float nameWidth = Math.Max(60.0f, PortraitHoverStatsNameColumnWidth);
            float statWidth = Math.Max(34.0f, PortraitHoverStatsStatColumnWidth);
            float soloWidth = Math.Max(34.0f, PortraitHoverStatsSoloColumnWidth);

            cells.Add(new PortraitHoverStatCell { Header = "PLAYER", Value = snapshot.Name, Width = nameWidth });
            cells.Add(new PortraitHoverStatCell { Header = "EHP", Value = snapshot.Ehp, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "DPS", Value = snapshot.Dps, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "APS", Value = snapshot.Aps, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CHC", Value = snapshot.Chc, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CHD", Value = snapshot.Chd, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "CDR", Value = snapshot.Cdr, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "RCR", Value = snapshot.Rcr, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "AD", Value = snapshot.Ad, Width = statWidth });
            cells.Add(new PortraitHoverStatCell { Header = "SOLO", Value = snapshot.Solo, Width = soloWidth });

            return cells;
        }

        private void UpdatePortraitHoverStatsCache(List<IPlayer> players)
        {
            if (!PortraitHoverStatsUseLastKnownStats || players == null)
                return;

            try
            {
                foreach (var player in players)
                {
                    if (player == null || !player.IsInGame)
                        continue;

                    var snapshot = ReadCurrentPortraitHoverPlayerStats(player);
                    if (snapshot == null || !snapshot.HasUsableCoreStats)
                        continue;

                    CachePortraitHoverPlayerStats(player, snapshot);
                }
            }
            catch
            {
            }
        }

        private PortraitHoverPlayerStatsSnapshot GetPortraitHoverPlayerStats(IPlayer player)
        {
            var current = ReadCurrentPortraitHoverPlayerStats(player);
            if (current == null)
                return CreateEmptyPortraitHoverPlayerStats(player);

            if (!PortraitHoverStatsUseLastKnownStats || current.HasUsableCoreStats)
                return current;

            try
            {
                PortraitHoverPlayerStatsSnapshot cached;
                if (TryGetCachedPortraitHoverPlayerStats(player, out cached) && cached != null)
                {
                    // Keep live identity/solo data when available, but preserve cached combat
                    // stat columns if FreeHUD exposes false zeroes for a player in another world.
                    var merged = ClonePortraitHoverPlayerStatsSnapshot(cached);
                    merged.Name = !string.IsNullOrEmpty(current.Name) && current.Name != "?" ? current.Name : merged.Name;
                    merged.Solo = !string.IsNullOrEmpty(current.Solo) && current.Solo != "-" ? current.Solo : merged.Solo;
                    return merged;
                }
            }
            catch
            {
            }

            // Do not display misleading live zeroes for remote players whose stat block is
            // currently unavailable. Show blanks until FreeHUD exposes valid data or until
            // the player has a cached snapshot from this session.
            var empty = CreateEmptyPortraitHoverPlayerStats(player);
            empty.Name = !string.IsNullOrEmpty(current.Name) && current.Name != "?" ? current.Name : empty.Name;
            empty.Solo = !string.IsNullOrEmpty(current.Solo) && current.Solo != "-" ? current.Solo : empty.Solo;
            return empty;
        }

        private PortraitHoverPlayerStatsSnapshot ReadCurrentPortraitHoverPlayerStats(IPlayer player)
        {
            var snapshot = CreateEmptyPortraitHoverPlayerStats(player);

            if (player == null)
                return snapshot;

            snapshot.Ehp = GetPlayerEhpText(player);
            snapshot.Dps = GetPlayerDpsText(player);
            snapshot.Aps = GetPlayerApsText(player);
            snapshot.Chc = GetPlayerChcText(player);
            snapshot.Chd = GetPlayerChdText(player);
            snapshot.Cdr = GetPlayerCdrText(player);
            snapshot.Rcr = GetPlayerRcrText(player);
            snapshot.Ad = GetPlayerAreaDamageText(player);
            snapshot.Solo = GetPlayerSoloText(player);
            snapshot.HasUsableCoreStats = HasUsablePortraitHoverCoreStats(player);

            try
            {
                if (Hud != null && Hud.Game != null)
                    snapshot.LastSeenTick = Hud.Game.CurrentGameTick;
            }
            catch
            {
            }

            return snapshot;
        }

        private PortraitHoverPlayerStatsSnapshot CreateEmptyPortraitHoverPlayerStats(IPlayer player)
        {
            return new PortraitHoverPlayerStatsSnapshot
            {
                Name = GetPortraitHoverPlayerName(player),
                Ehp = "-",
                Dps = "-",
                Aps = "-",
                Chc = "-",
                Chd = "-",
                Cdr = "-",
                Rcr = "-",
                Ad = "-",
                Solo = GetPlayerSoloText(player),
                HasUsableCoreStats = false,
                LastSeenTick = 0
            };
        }

        private bool HasUsablePortraitHoverCoreStats(IPlayer player)
        {
            try
            {
                if (player == null || player.Defense == null || player.Offense == null)
                    return false;

                // These three are never legitimately all zero for an in-game level-70 player.
                // When another player is in a different town/world instance, FreeHUD may still
                // expose the IPlayer row but temporarily zero the calculated stat objects.
                return player.Defense.EhpCur > 1.0f &&
                    player.Offense.SheetDps > 1.0f &&
                    player.Offense.AttackSpeed > 0.1f;
            }
            catch
            {
            }

            return false;
        }

        private void CachePortraitHoverPlayerStats(IPlayer player, PortraitHoverPlayerStatsSnapshot snapshot)
        {
            if (player == null || snapshot == null)
                return;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
                _portraitHoverStatsCache[key] = ClonePortraitHoverPlayerStatsSnapshot(snapshot);
        }

        private bool TryGetCachedPortraitHoverPlayerStats(IPlayer player, out PortraitHoverPlayerStatsSnapshot snapshot)
        {
            snapshot = null;

            if (player == null)
                return false;

            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                if (_portraitHoverStatsCache.TryGetValue(key, out snapshot) && snapshot != null)
                    return true;
            }

            return false;
        }

        private PortraitHoverPlayerStatsSnapshot ClonePortraitHoverPlayerStatsSnapshot(PortraitHoverPlayerStatsSnapshot source)
        {
            if (source == null)
                return null;

            return new PortraitHoverPlayerStatsSnapshot
            {
                Name = source.Name,
                Ehp = source.Ehp,
                Dps = source.Dps,
                Aps = source.Aps,
                Chc = source.Chc,
                Chd = source.Chd,
                Cdr = source.Cdr,
                Rcr = source.Rcr,
                Ad = source.Ad,
                Solo = source.Solo,
                HasUsableCoreStats = source.HasUsableCoreStats,
                LastSeenTick = source.LastSeenTick
            };
        }

        private string GetPortraitHoverPlayerCacheKey(IPlayer player)
        {
            foreach (var key in GetPortraitHoverPlayerCacheKeys(player))
                return key;

            return null;
        }

        private IEnumerable<string> GetPortraitHoverPlayerCacheKeys(IPlayer player)
        {
            List<string> keys = new List<string>();

            if (player == null)
                return keys;

            try
            {
                if (player.HeroId != 0)
                    AddPortraitHoverCacheKey(keys, "hero:" + player.HeroId.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait))
                {
                    string raw = player.BattleTagAbovePortrait.Trim();
                    AddPortraitHoverCacheKey(keys, "bt:" + NormalizePortraitHoverCacheText(raw));

                    string account = ExtractPortraitAccountName(raw);
                    if (!string.IsNullOrEmpty(account))
                        AddPortraitHoverCacheKey(keys, "acct:" + NormalizePortraitHoverCacheText(account));
                }
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(player.HeroName))
                    AddPortraitHoverCacheKey(keys, "heroname:" + NormalizePortraitHoverCacheText(player.HeroName));
            }
            catch
            {
            }

            // Last-resort session fallback. This can help while FreeHUD is still
            // hydrating remote profile data, but stronger hero/account keys above win first.
            try
            {
                AddPortraitHoverCacheKey(keys, "idx:" + player.Index.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
            }

            return keys;
        }

        private void AddPortraitHoverCacheKey(List<string> keys, string key)
        {
            if (keys == null || string.IsNullOrEmpty(key))
                return;

            if (!keys.Contains(key))
                keys.Add(key);
        }

        private string NormalizePortraitHoverCacheText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Trim().ToLowerInvariant();
        }

        private string ExtractPortraitAccountName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            string result = name.Trim();

            int clanEnd = result.LastIndexOf('>');
            if (clanEnd >= 0 && clanEnd + 1 < result.Length)
                result = result.Substring(clanEnd + 1).Trim();

            int hashIndex = result.IndexOf('#');
            if (hashIndex > 0)
                result = result.Substring(0, hashIndex).Trim();

            return result;
        }

        private void DrawPortraitHoverCellText(string text, IFont font, float x, float y, float width, float height, bool center)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            try
            {
                var layout = font.GetTextLayout(text);
                float padding = Math.Max(0.0f, PortraitHoverStatsPaddingX);
                float textX = center
                    ? x + (width - layout.Metrics.Width) * 0.5f
                    : x + padding;
                float textY = y + (height - layout.Metrics.Height) * 0.5f;

                if (textX < x + 1.0f)
                    textX = x + 1.0f;

                font.DrawText(layout, textX, textY);
            }
            catch
            {
            }
        }

        private string GetPortraitHoverPlayerName(IPlayer player)
        {
            if (player == null)
                return "?";

            // Use the account/BattleTag name for the hover stats table, not the hero name.
            // FreeHUD exposes this as the portrait BattleTag string, sometimes with clan and
            // #discriminator text. For compact stat rows we show the account-name portion first.
            try
            {
                if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait))
                {
                    string name = ExtractPortraitAccountName(player.BattleTagAbovePortrait);

                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            catch
            {
            }

            // Fallback only. This should be used when FreeHUD has not populated the portrait
            // BattleTag/account string yet.
            try
            {
                if (!string.IsNullOrEmpty(player.HeroName))
                    return player.HeroName;
            }
            catch
            {
            }

            return "?";
        }

        private string GetPlayerEhpText(IPlayer player)
        {
            try
            {
                if (player != null && player.Defense != null)
                    return ValueToString(player.Defense.EhpCur, ValueFormat.ShortNumber);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerDpsText(IPlayer player)
        {
            try
            {
                if (player != null && player.Offense != null)
                {
                    // Match TurboHUD's practical "elite DPS" value, but keep the header as DPS.
                    // This is sheet DPS with the highest elemental bonus and elite damage bonus applied.
                    double sheetDps = player.Offense.SheetDps;
                    double elementalMultiplier = 1.0 + Math.Max(0.0, player.Offense.HighestElementalDamageBonus);
                    double eliteMultiplier = 1.0 + Math.Max(0.0, player.Offense.BonusToElites);

                    return ValueToString(sheetDps * elementalMultiplier * eliteMultiplier, ValueFormat.ShortNumber);
                }
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerApsText(IPlayer player)
        {
            try
            {
                if (player != null && player.Offense != null)
                    return player.Offense.AttackSpeed.ToString("F2", CultureInfo.InvariantCulture) + "/s";
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerChcText(IPlayer player)
        {
            try
            {
                if (player != null && player.Offense != null)
                    return FormatPercentNoScaling(player.Offense.CriticalHitChance, 1);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerChdText(IPlayer player)
        {
            try
            {
                if (player != null && player.Offense != null)
                    return FormatPercentNoScaling(player.Offense.CritDamage, 0);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerCdrText(IPlayer player)
        {
            try
            {
                if (player != null && player.Stats != null)
                    return FormatPercent(player.Stats.CooldownReduction);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerRcrText(IPlayer player)
        {
            try
            {
                if (player != null && player.Stats != null)
                    return FormatPercent(player.Stats.ResourceCostReduction);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerAreaDamageText(IPlayer player)
        {
            try
            {
                if (player != null && player.Offense != null)
                    return FormatPercentNoScaling(player.Offense.AreaDamageBonus, 0);
            }
            catch
            {
            }

            return "-";
        }

        private string GetPlayerSoloText(IPlayer player)
        {
            try
            {
                if (player == null)
                    return "-";

                // Account-wide highest solo clear across any class.
                // Do not use HighestHeroSoloRiftLevel here; that is current-hero/class scoped.
                int solo = player.HighestSoloRiftLevel;

                return solo > 0 ? solo.ToString(CultureInfo.InvariantCulture) + " LV" : "-";
            }
            catch
            {
            }

            return "-";
        }

        private string FormatPercentNoScaling(float value, int decimals)
        {
            string format = decimals <= 0 ? "F0" : "F" + decimals.ToString(CultureInfo.InvariantCulture);
            return value.ToString(format, CultureInfo.InvariantCulture) + "%";
        }

        // -----------------------------------------------------------------------
        // Player display helpers
        // -----------------------------------------------------------------------

        private string GetPlayerDisplayName(IPlayer player)
        {
            if (player == null)
                return "?";

            try
            {
                if (!string.IsNullOrEmpty(player.BattleTagAbovePortrait))
                    return player.BattleTagAbovePortrait;
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(player.HeroName))
                    return player.HeroName;
            }
            catch
            {
            }

            return "?";
        }

        // -----------------------------------------------------------------------
        // F12 expanded legacy panel — exists but is NOT called anywhere.
        // Retained for reference; DrawPartyPortraitOverlay does not invoke this.
        // -----------------------------------------------------------------------

        private void DrawPartyInspectorPanel()
        {
            var players = GetPartyPlayers();

            float headerHeight = 28.0f;
            float height = headerHeight + Math.Max(1, players.Count) * RowHeight + 8.0f;

            if (_panelBrush != null)
                _panelBrush.DrawRectangle(PanelX, PanelY, PanelWidth, height);

            if (_borderBrush != null)
                _borderBrush.DrawRectangle(PanelX, PanelY, PanelWidth, height);

            var titleLayout = _titleFont.GetTextLayout("s7o Party Inspector  (F12 expanded)");
            _titleFont.DrawText(titleLayout, PanelX + 8.0f, PanelY + 6.0f);

            float y = PanelY + headerHeight;

            if (players.Count == 0)
            {
                var empty = _smallFont.GetTextLayout("No party players found.");
                _smallFont.DrawText(empty, PanelX + 8.0f, y + 8.0f);
                return;
            }

            foreach (var player in players)
            {
                DrawPlayerRow(player, PanelX + 8.0f, y, PanelWidth - 16.0f);
                y += RowHeight;
            }
        }

        private void DrawPlayerRow(IPlayer player, float x, float y, float width)
        {
            if (player == null)
                return;

            if (_rowBrush != null)
                _rowBrush.DrawRectangle(x, y, width, RowHeight - 4.0f);

            string name      = GetPlayerDisplayName(player);
            string classText = GetPlayerClassText(player);
            int    gemLeft   = GetGemUpgradesLeft(player);

            var nameLayout = _nameFont.GetTextLayout(name + classText);
            _nameFont.DrawText(nameLayout, x + 5.0f, y + 4.0f);

            if (ShowExpandedGemUpgrades)
            {
                string gemText = gemLeft > 0
                    ? "Gem upgrades left: " + gemLeft.ToString(CultureInfo.InvariantCulture)
                    : "Gem upgrades: 0";

                var gemFont   = gemLeft > 0 ? _warningFont : _smallFont;
                var gemLayout = gemFont.GetTextLayout(gemText);
                gemFont.DrawText(gemLayout, x + 5.0f, y + 22.0f);
            }

            if (ShowExpandedExtraStats)
            {
                string stats = GetExtraStatsText(player);
                if (!string.IsNullOrEmpty(stats))
                {
                    var statsLayout = _statsFont.GetTextLayout(stats);
                    _statsFont.DrawText(statsLayout, x + 5.0f, y + 40.0f);
                }
            }

            float skillX = x + 185.0f;
            float skillY = y + 8.0f;

            if (ShowExpandedSkills && ShowSkills)
                DrawPlayerSkills(player, skillX, skillY);

            float passiveX = skillX + (6.0f * (SkillIconSize + SkillIconGap)) + 10.0f;

            if (ShowExpandedPassives && ShowPassives)
                DrawPlayerPassives(player, passiveX, skillY);

            if (ShowExpandedCubeItems && ShowCubeItems)
                DrawCubeItemsExpanded(player, passiveX, y + 34.0f);
        }

        private string GetPlayerClassText(IPlayer player)
        {
            try
            {
                return " - " + player.HeroClassDefinition.HeroClass.ToString();
            }
            catch
            {
                return "";
            }
        }

        private string GetExtraStatsText(IPlayer player)
        {
            if (player == null)
                return "";

            List<string> parts = new List<string>();

            try
            {
                if (player.Offense != null)
                {
                    float areaDamage = player.Offense.AreaDamageBonus;
                    if (areaDamage > 0.0f)
                        parts.Add("AD " + FormatPercent(areaDamage));
                }
            }
            catch
            {
            }

            try
            {
                if (player.Stats != null)
                {
                    float cdr = player.Stats.CooldownReduction;
                    if (cdr > 0.0f)
                        parts.Add("CDR " + FormatPercent(cdr));

                    float rcr = player.Stats.ResourceCostReduction;
                    if (rcr > 0.0f)
                        parts.Add("RCR " + FormatPercent(rcr));
                }
            }
            catch
            {
            }

            return string.Join("  ", parts.ToArray());
        }

        private string FormatPercent(float value)
        {
            float pct = value;
            if (pct > 0.0f && pct <= 1.5f)
                pct *= 100.0f;

            return pct.ToString("F0", CultureInfo.InvariantCulture) + "%";
        }

        // -----------------------------------------------------------------------
        // Skill drawing (expanded legacy panel — used by DrawPlayerSkills)
        // -----------------------------------------------------------------------

        private void DrawPlayerSkills(IPlayer player, float x, float y)
        {
            if (player == null || player.Powers == null || player.Powers.SkillSlots == null)
                return;

            var skills = player.Powers.SkillSlots
                .Where(s => s != null && GetSkillPower(s) != null)
                .OrderBy(s => (int)s.Key)
                .Take(6)
                .ToList();

            float sx = x;

            foreach (var skill in skills)
            {
                DrawSkill(skill, sx, y, SkillIconSize);
                sx += SkillIconSize + SkillIconGap;
            }
        }

        private void DrawSkill(IPlayerSkill skill, float x, float y, float size)
        {
            if (skill == null || GetSkillPower(skill) == null)
                return;

            DrawSkillIcon(skill, x, y, size);

            if (_skillBorderBrush != null)
                _skillBorderBrush.DrawRectangle(x, y, size, size);

            DrawSkillKey(skill, x, y, size);

            if (ShowCooldowns)
                DrawSkillCooldown(skill, x, y, size);

            DrawSkillRuneHint(skill, x, y, size);
        }

        private void DrawSkillIcon(IPlayerSkill skill, float x, float y, float size)
        {
            try
            {
                ISnoPower power = GetSkillPower(skill);
                if (power != null)
                {
                    var texture = Hud.Texture.GetTexture(power.NormalIconTextureId);
                    if (texture != null)
                    {
                        texture.Draw(x, y, size, size, 1.0f);
                        return;
                    }
                }
            }
            catch
            {
            }

            if (_cooldownOverlayBrush != null)
                _cooldownOverlayBrush.DrawRectangle(x, y, size, size);
        }

        private void DrawSkillKey(IPlayerSkill skill, float x, float y, float size)
        {
            if (_keyFont == null)
                return;

            string text = "";

            try
            {
                text = GetActionKeyText(skill.Key);
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(text))
                return;

            var layout = _keyFont.GetTextLayout(text);
            _keyFont.DrawText(layout, x + 2.0f, y + 1.0f);
        }

        private string GetActionKeyText(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill:  return "L";
                case ActionKey.RightSkill: return "R";
                case ActionKey.Skill1:     return "1";
                case ActionKey.Skill2:     return "2";
                case ActionKey.Skill3:     return "3";
                case ActionKey.Skill4:     return "4";
                case ActionKey.Heal:       return "Q";
                default:                   return key.ToString();
            }
        }

        private void DrawSkillCooldown(IPlayerSkill skill, float x, float y, float size)
        {
            DrawSkillCooldownClock(skill, new DXRectangleF(x, y, size, size), _cooldownFont);
        }

        private void DrawSkillRuneHint(IPlayerSkill skill, float x, float y, float size)
        {
            if (_smallFont == null)
                return;

            string rune = "";

            try
            {
                if (!string.IsNullOrEmpty(skill.RuneNameEnglish))
                    rune = skill.RuneNameEnglish.Substring(0, 1);
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(rune))
                return;

            var layout = _smallFont.GetTextLayout(rune);
            _smallFont.DrawText(
                layout,
                x + size - layout.Metrics.Width  - 2.0f,
                y + size - layout.Metrics.Height - 1.0f);
        }

        // -----------------------------------------------------------------------
        // Passive and cube drawers (expanded legacy panel)
        // -----------------------------------------------------------------------

        private void DrawPlayerPassives(IPlayer player, float x, float y)
        {
            if (player == null || player.Powers == null)
                return;

            List<ISnoPower> passives = new List<ISnoPower>();

            try
            {
                if (player.Powers.PassiveSlots != null)
                    passives.AddRange(player.Powers.PassiveSlots.Where(p => p != null));
            }
            catch
            {
            }

            if (passives.Count == 0)
            {
                try
                {
                    if (player.Powers.UsedPassives != null)
                        passives.AddRange(player.Powers.UsedPassives.Where(p => p != null));
                }
                catch
                {
                }
            }

            if (passives.Count == 0)
                return;

            float passiveSize = Math.Max(16.0f, SkillIconSize * 0.56f);
            float sx = x;

            foreach (var passive in passives.Take(4))
            {
                DrawPassive(passive, sx, y + ((SkillIconSize - passiveSize) * 0.5f), passiveSize, true);
                sx += passiveSize + SkillIconGap;
            }
        }

        private void DrawPassive(ISnoPower passive, float x, float y, float size, bool drawLabel)
        {
            if (passive == null)
                return;

            bool drewTexture = false;

            try
            {
                var texture = Hud.Texture.GetTexture(passive.NormalIconTextureId);
                if (texture != null)
                {
                    texture.Draw(x, y, size, size, 0.9f);
                    drewTexture = true;
                }
            }
            catch
            {
            }

            if (!drewTexture && _cooldownOverlayBrush != null)
                _cooldownOverlayBrush.DrawRectangle(x, y, size, size);

            // Thick black border for contrast.
            if (_skillBorderBrush != null)
                _skillBorderBrush.DrawRectangle(x, y, size, size);

            if (drawLabel && _keyFont != null)
            {
                var layout = _keyFont.GetTextLayout("P");
                _keyFont.DrawText(layout, x + 1.0f, y + 1.0f);
            }

            if (Hud.Window.CursorInsideRect(x, y, size, size))
            {
                try
                {
                    Hud.Render.SetHint(passive.NameLocalized);
                }
                catch
                {
                }
            }
        }

        private void DrawCubeItemsExpanded(IPlayer player, float x, float y)
        {
            if (player == null)
                return;

            List<ISnoItem> cubeItems = new List<ISnoItem>();

            try
            {
                if (player.CubeSnoItem1 != null) cubeItems.Add(player.CubeSnoItem1);
                if (player.CubeSnoItem2 != null) cubeItems.Add(player.CubeSnoItem2);
                if (player.CubeSnoItem3 != null) cubeItems.Add(player.CubeSnoItem3);
                if (player.CubeSnoItem4 != null) cubeItems.Add(player.CubeSnoItem4);
            }
            catch
            {
            }

            if (cubeItems.Count == 0)
                return;

            float w  = Math.Max(18.0f, SkillIconSize * 0.55f);
            float h  = w * CubeIconHeightMultiplier;
            float sx = x;

            foreach (var item in cubeItems.Take(4))
            {
                DrawCubeItemIcon(item, sx, y, w, h, true);
                sx += w + SkillIconGap;
            }
        }

        private void DrawCubeItemIcon(ISnoItem item, float x, float y, float width, float height, bool drawLabel)
        {
            if (item == null)
                return;

            // Flat consistent background — same alpha for every slot regardless of item type.
            // This eliminates the transparency mismatch from the inventory background textures.
            if (_cubeItemBackBrush != null)
                _cubeItemBackBrush.DrawRectangle(x, y, width, height);

            bool drew = DrawSnoItemIconTexture(item, x, y, width, height, 0.95f);

            if (!drew)
            {
                try
                {
                    if (item.LegendaryPower != null && item.LegendaryPower.NormalIconTextureId != 0)
                    {
                        var texture = Hud.Texture.GetTexture(item.LegendaryPower.NormalIconTextureId);
                        if (texture != null)
                        {
                            // Power icons are typically square — use aspect-fit into the tall box.
                            DrawTextureAspectFit(texture, x, y, width, height, 0.9f);
                            drew = true;
                        }
                    }
                }
                catch
                {
                }
            }

            if (!drew && _cooldownOverlayBrush != null)
                _cooldownOverlayBrush.DrawRectangle(x, y, width, height);

            // Thick black border for contrast.
            if (_skillBorderBrush != null)
                _skillBorderBrush.DrawRectangle(x, y, width, height);

            if (drawLabel && _keyFont != null)
            {
                var layout = _keyFont.GetTextLayout("C");
                _keyFont.DrawText(layout, x + 1.0f, y + 1.0f);
            }

            if (Hud.Window.CursorInsideRect(x, y, width, height))
            {
                try
                {
                    string description = item.NameLocalized;

                    if (item.LegendaryPower != null && !string.IsNullOrEmpty(item.LegendaryPower.DescriptionLocalized))
                        description += "\n\n" + item.LegendaryPower.DescriptionLocalized;

                    Hud.Render.SetHint(description);
                }
                catch
                {
                }
            }
        }

        // -----------------------------------------------------------------------
        // Item texture rendering — pure centered aspect-fit
        // -----------------------------------------------------------------------
        // The caller supplies a box of the correct shape for the item type:
        //   - Square boxes  → skill/passive/gem icons  (always roughly square art)
        //   - Tall rect boxes → cube powers            (staves fill it, rings center)
        //
        // All items are drawn centered inside their box with correct aspect ratio.
        // Nothing is ever stretched, cropped, or zoomed past the box boundary.
        // -----------------------------------------------------------------------

        private bool DrawTextureAspectFit(
            ITexture texture,
            float x, float y,
            float width, float height,
            float opacity)
        {
            if (texture == null)
                return false;

            try
            {
                if (texture.Width <= 0.0f || texture.Height <= 0.0f
                    || width <= 0.0f || height <= 0.0f)
                    return false;

                // Scale uniformly so the entire image fits inside the box.
                float scale = Math.Min(width / texture.Width, height / texture.Height);

                float drawWidth  = texture.Width  * scale;
                float drawHeight = texture.Height * scale;

                // Center within the box on both axes.
                float drawX = x + (width  - drawWidth)  * 0.5f;
                float drawY = y + (height - drawHeight) * 0.5f;

                texture.Draw(drawX, drawY, drawWidth, drawHeight, opacity);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool DrawSnoItemInventoryIcon(ISnoItem item, float x, float y, float width, float height, float opacity)
        {
            if (item == null || width <= 0.0f || height <= 0.0f)
                return false;

            try
            {
                var slotTexture = Hud.Texture.InventorySlotTexture;
                if (slotTexture != null)
                    slotTexture.Draw(x, y, width, height, opacity);

                var background = item.SetItemBonusesSno == uint.MaxValue
                    ? (item.ItemHeight == 1 ? Hud.Texture.InventoryLegendaryBackgroundSmall : Hud.Texture.InventoryLegendaryBackgroundLarge)
                    : (item.ItemHeight == 1 ? Hud.Texture.InventorySetBackgroundSmall : Hud.Texture.InventorySetBackgroundLarge);

                if (background != null)
                    background.Draw(x, y, width, height, opacity);

                var itemTexture = Hud.Texture.GetItemTexture(item);
                if (itemTexture != null)
                    itemTexture.Draw(x, y, width, height, opacity);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool DrawSnoItemIconTexture(ISnoItem item, float x, float y, float width, float height, float opacity)
        {
            if (item == null)
                return false;

            // Draw the item art centered and aspect-correct inside the box.
            // Background is the caller's responsibility (see DrawCubeItemIcon).
            try
            {
                var itemTexture = Hud.Texture.GetItemTexture(item);
                if (itemTexture != null)
                    return DrawTextureAspectFit(itemTexture, x, y, width, height, opacity);
            }
            catch
            {
            }

            return false;
        }
    }
}
