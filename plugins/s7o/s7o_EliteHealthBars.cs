using System;
using System.Collections.Generic;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Caches per-frame elite classification and pack state while preserving
    // low-health Morlu recovery as targetable and normally colored.
    public class s7o_EliteHealthBars : BasePlugin, IBeforeRenderHandler, IInGameWorldPainter, IInGameTopPainter
    {
        private const double MorluRecoveryHealthThreshold = 0.12d;
        // Structural elite/pack classification is intentionally slower than drawing.
        // Monster references remain live and bar anchors/HP still update every render;
        // only membership, pack grouping and availability classification refresh at ~15 Hz.
        private const int ClassificationIntervalTicks = 4;
        private const float BarAnchorJitterThresholdPx = 3.5f;
        private const float BarAnchorJitterFollow = 0.60f;

        private bool _defaultEliteMinimapCirclesAdjusted;
        private int _defaultEliteMinimapCircleAdjustAttempts;
        private IBrush _eliteMinimapCircleOutlineBrush;
        private IBrush _eliteGroundCircleOutlineBrush;
        private IBrush _eliteGroundChampionCircleBrush;
        private IBrush _eliteGroundFinalChampionCircleBrush;
        private IBrush _eliteGroundRareCircleBrush;
        private IBrush _eliteGroundRareMinionCircleBrush;
        private IBrush _eliteGroundJuggernautCircleBrush;
        private IBrush _eliteGroundUniqueCircleBrush;
        private IBrush _eliteGroundGoblinCircleBrush;
        private IBrush _eliteGroundRiftGuardianCircleBrush;
        private IBrush _eliteGroundUnavailableCircleBrush;
        private int _eliteGroundCircleBrushAlpha = -1;
        private float _eliteGroundCircleBrushThickness = -1.0f;
        private IBrush _mapOverlayBlackBrush;
        private IBrush _mapOverlayUnavailableBrush;
        private IBrush _mapOverlayChampionBrush;
        private IBrush _mapOverlayRareBrush;
        private IBrush _mapOverlayMinionBrush;
        private IBrush _mapOverlayJuggernautBrush;
        private IBrush _mapOverlayJuggernautCenterBrush;
        private IBrush _mapOverlayUniqueBrush;

        private WorldDecoratorCollection _mapOverlayUnavailableDecorator;
        private WorldDecoratorCollection _mapOverlayMinionDecorator;
        private WorldDecoratorCollection _mapOverlayJuggernautDecorator;
        private WorldDecoratorCollection _mapOverlayJuggernautCenterDecorator;
        private WorldDecoratorCollection _mapOverlayChampionDecorator;
        private WorldDecoratorCollection _mapOverlayRareDecorator;
        private WorldDecoratorCollection _mapOverlayUniqueDecorator;

        private enum EliteVisualKind
        {
            Rare,
            Champion,
            FinalChampion,
            RareMinion,
            Juggernaut,
            Unique,
            Goblin,
            RiftGuardian,
        }

        private struct EliteRenderState
        {
            public EliteVisualKind Kind;
            public bool IsUnavailable;
            public bool IsJuggernaut;
            public bool IsFinalChampion;
        }

        private struct ChampionFamilyKey : IEquatable<ChampionFamilyKey>
        {
            private readonly byte _kind;
            private readonly uint _sno;
            private readonly string _text;

            private ChampionFamilyKey(byte kind, uint sno, string text)
            {
                _kind = kind;
                _sno = sno;
                _text = text;
            }

            public bool IsEmpty { get { return _kind == 0; } }

            public static ChampionFamilyKey FromActor(uint sno)
            {
                return new ChampionFamilyKey(1, sno, null);
            }

            public static ChampionFamilyKey FromCode(string code)
            {
                return string.IsNullOrEmpty(code) ? default(ChampionFamilyKey) : new ChampionFamilyKey(2, 0, code);
            }

            public static ChampionFamilyKey FromName(string name)
            {
                // The previous string key always produced at least "name:", even when
                // no readable monster name existed, so preserve that grouping behavior.
                return new ChampionFamilyKey(3, 0, name ?? string.Empty);
            }

            public bool Equals(ChampionFamilyKey other)
            {
                return _kind == other._kind
                    && _sno == other._sno
                    && string.Equals(_text, other._text, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ChampionFamilyKey && Equals((ChampionFamilyKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _kind;
                    hash = (hash * 397) ^ (int)_sno;
                    hash = (hash * 397) ^ (_text == null ? 0 : _text.GetHashCode());
                    return hash;
                }
            }
        }

        private readonly Dictionary<ChampionFamilyKey, int> _championFamilyAliveCounts = new Dictionary<ChampionFamilyKey, int>();
        private readonly Dictionary<IMonsterPack, int> _championPackAliveCounts = new Dictionary<IMonsterPack, int>();
        private readonly Dictionary<IMonster, EliteRenderState> _renderStateByMonster = new Dictionary<IMonster, EliteRenderState>();

        // One broad AliveMonsters pass feeds this small candidate list and champion counts.
        // Dense trash never reaches the more expensive elite/special classification pass.
        private readonly List<IMonster> _candidateMonsters = new List<IMonster>(64);
        private readonly List<IMonster> _barMonsters = new List<IMonster>();
        private readonly List<IMonster> _offscreenMonsters = new List<IMonster>();
        private readonly List<IMonster> _topLeftCandidates = new List<IMonster>();
        private readonly List<IMonster> _mapUnavailable = new List<IMonster>();
        private readonly List<IMonster> _mapMinions = new List<IMonster>();
        private readonly List<IMonster> _mapJuggernauts = new List<IMonster>();
        private readonly List<IMonster> _mapChampions = new List<IMonster>();
        private readonly List<IMonster> _mapRares = new List<IMonster>();
        private readonly List<IMonster> _mapUniques = new List<IMonster>();

        private readonly List<ElitePackListEntry> _topLeftPacks = new List<ElitePackListEntry>();
        private readonly Stack<ElitePackListEntry> _topLeftPackPool = new Stack<ElitePackListEntry>();
        private readonly Dictionary<IMonsterPack, ElitePackListEntry> _topLeftChampionPacks = new Dictionary<IMonsterPack, ElitePackListEntry>();
        private readonly Dictionary<ChampionFamilyKey, ElitePackListEntry> _topLeftFallbackChampionGroups = new Dictionary<ChampionFamilyKey, ElitePackListEntry>();

        private readonly string[] _hpPercentText = new string[101];
        private readonly float[] _hpPercentTextWidth = new float[101];
        private readonly float[] _hpPercentTextHeight = new float[101];
        private int _preparedGameTick = int.MinValue;
        private int _nextPrepareGameTick = int.MinValue;

        private struct BarAnchorState
        {
            public float X;
            public float Y;
            public int LastSeenTick;
        }

        private readonly Dictionary<uint, BarAnchorState> _barAnchorState = new Dictionary<uint, BarAnchorState>(32);
        private readonly List<uint> _staleBarAnchorIds = new List<uint>(32);
        private int _lastBarAnchorPruneTick = int.MinValue;

        public bool ShowChampionBars { get; set; }
        public bool ShowRareBars { get; set; }
        public bool ShowRareMinionBars { get; set; }
        public bool ShowUniqueBars { get; set; }
        public bool ShowGoblinBars { get; set; }
        public bool ShowRiftGuardianBars { get; set; }
        public bool ShowKeywardenBars { get; set; }
        public bool ShowBossBars { get; set; }

        public bool HideIllusions { get; set; }
        public bool ShowInvisible { get; set; }
        public bool RequireAttackable { get; set; }
        public bool GreyOutUnavailableElites { get; set; }
        public bool GreyOutBurrowedElites { get; set; }
        public bool GreyOutInvulnerableElites { get; set; }
        public bool GreyOutInvisibleElites { get; set; }
        public bool GreyOutUntargetableElites { get; set; }
        public bool ShrinkDefaultEliteMinimapCircles { get; set; }
        public float EliteMinimapCircleRadius { get; set; }
        public float EliteMinionMinimapCircleRadius { get; set; }
        public bool ShrinkBossAndKeywardenMinimapCircles { get; set; }
        public float BossMinimapCircleRadius { get; set; }
        public float KeywardenMinimapCircleRadius { get; set; }
        public bool AddEliteMinimapCircleOutlines { get; set; }
        public float EliteMinimapCircleOutlineWidth { get; set; }
        public int EliteMinimapCircleOutlineAlpha { get; set; }
        public bool ShowLayeredEliteMinimapOverlay { get; set; }
        public bool SuppressDefaultEliteMinimapCirclesWhenLayeredOverlay { get; set; }
        public float EliteMinimapOverlayRadius { get; set; }
        public float EliteMinionMinimapOverlayRadius { get; set; }
        public float JuggernautMinimapOverlayRadius { get; set; }
        public float JuggernautMinimapCenterDotRadius { get; set; }
        public float EliteMinimapOverlayOutlineWidth { get; set; }
        public int EliteMinimapOverlayOutlineAlpha { get; set; }

        public bool ShowEliteHealthBars { get; set; }
        public float BarWidth { get; set; }
        public float BarHeight { get; set; }
        public bool ShowEliteGroundCircles { get; set; }
        public int EliteGroundCircleAlpha { get; set; }
        public float EliteGroundCircleThickness { get; set; }
        public float BarYOffset { get; set; }
        public float BarBorderPadding { get; set; }
        public bool AnchorBarsToFeet { get; set; }
        public float BarFootYOffset { get; set; }
        public bool UseHitboxBottomAnchor { get; set; }
        public bool UsePreciseAnchorProjection { get; set; }
        public float BarEdgeFollowRangePx { get; set; }
        public float HitboxAnchorRadiusScale { get; set; }
        public float MaxHitboxAnchorRadius { get; set; }
        public bool UseSimpleFeetAnchorForBossesAndKeywardens { get; set; }

        public bool ShowOffscreenEliteLines { get; set; }
        public float OffscreenLineMaxWorldDistance { get; set; }
        public float OffscreenLineLength { get; set; }
        public float OffscreenLineMargin { get; set; }
        public float OffscreenLineStrokeWidth { get; set; }
        public bool OffscreenLinesUseFilteredMonstersOnly { get; set; }
        public bool OffscreenLineStopsAtScreenEdge { get; set; }
        public int MaxOffscreenLines { get; set; }

        public bool ShowHpPercentText { get; set; }
        public bool UseTwoToneBarLighting { get; set; }

        public bool ShowTopLeftEliteList { get; set; }
        public bool ShowTopLeftEliteNames { get; set; }
        public bool ShowRareMinionsInTopLeftList { get; set; }
        public float TopLeftEliteListXFraction { get; set; }
        public float TopLeftEliteListYFraction { get; set; }
        public float TopLeftEliteListWidth { get; set; }
        public float TopLeftEliteListBarHeight { get; set; }
        public float TopLeftEliteListGap { get; set; }
        public float TopLeftEliteListInnerBarGap { get; set; }
        public float TopLeftEliteListLabelGap { get; set; }
        public int TopLeftEliteListMaxRows { get; set; }

        // Some pylon/shrine/event-spawned elites can expose affixes before/without
        // a clean IsElite/Rarity classification. Keep this narrow: it only admits
        // alive, visible, filtered monsters that have a real affix list.
        public bool ShowAffixTaggedEliteBars { get; set; }

        // Final alive blue champion in a champion pack gets a distinct dark purple bar.
        public bool HighlightFinalChampion { get; set; }

        // Juggernauts are dangerous enough that their bars should be easier to spot.
        public bool EmphasizeJuggernautBars { get; set; }
        public float JuggernautBarWidthBonus { get; set; }
        public float JuggernautBarHeightBonus { get; set; }

        public IBrush BorderBrush { get; set; }
        public IBrush BackgroundBrush { get; set; }
        public IBrush ChampionBrush { get; set; }
        public IBrush FinalChampionBrush { get; set; }
        public IBrush RareBrush { get; set; }
        public IBrush RareMinionBrush { get; set; }
        public IBrush JuggernautBrush { get; set; }
        public IBrush UniqueBrush { get; set; }
        public IBrush GoblinBrush { get; set; }
        public IBrush RiftGuardianBrush { get; set; }
        public IBrush UnavailableEliteBrush { get; set; }
        public IBrush UnavailableEliteLightBrush { get; set; }
        public IBrush UnavailableEliteShadowBrush { get; set; }
        public IBrush TopLeftUnavailableEliteBrush { get; set; }
        public IBrush TopLeftUnavailableEliteLightBrush { get; set; }
        public IBrush TopLeftUnavailableEliteShadowBrush { get; set; }
        public IBrush TopLeftListBackgroundBrush { get; set; }
        public IBrush TopLeftListBorderBrush { get; set; }
        public IBrush TopLeftChampionBrush { get; set; }
        public IBrush TopLeftFinalChampionBrush { get; set; }
        public IBrush TopLeftRareBrush { get; set; }
        public IBrush TopLeftRareMinionBrush { get; set; }
        public IBrush TopLeftJuggernautBrush { get; set; }
        public IBrush TopLeftChampionLightBrush { get; set; }
        public IBrush TopLeftFinalChampionLightBrush { get; set; }
        public IBrush TopLeftRareLightBrush { get; set; }
        public IBrush TopLeftRareMinionLightBrush { get; set; }
        public IBrush TopLeftJuggernautLightBrush { get; set; }
        public IBrush TopLeftChampionShadowBrush { get; set; }
        public IBrush TopLeftFinalChampionShadowBrush { get; set; }
        public IBrush TopLeftRareShadowBrush { get; set; }
        public IBrush TopLeftRareMinionShadowBrush { get; set; }
        public IBrush TopLeftJuggernautShadowBrush { get; set; }

        public IBrush ChampionLightBrush { get; set; }
        public IBrush FinalChampionLightBrush { get; set; }
        public IBrush RareLightBrush { get; set; }
        public IBrush RareMinionLightBrush { get; set; }
        public IBrush JuggernautLightBrush { get; set; }
        public IBrush UniqueLightBrush { get; set; }
        public IBrush GoblinLightBrush { get; set; }
        public IBrush RiftGuardianLightBrush { get; set; }

        public IBrush ChampionShadowBrush { get; set; }
        public IBrush FinalChampionShadowBrush { get; set; }
        public IBrush RareShadowBrush { get; set; }
        public IBrush RareMinionShadowBrush { get; set; }
        public IBrush JuggernautShadowBrush { get; set; }
        public IBrush UniqueShadowBrush { get; set; }
        public IBrush GoblinShadowBrush { get; set; }
        public IBrush RiftGuardianShadowBrush { get; set; }

        public IBrush ChampionStrokeBrush { get; set; }
        public IBrush FinalChampionStrokeBrush { get; set; }
        public IBrush RareStrokeBrush { get; set; }
        public IBrush RareMinionStrokeBrush { get; set; }
        public IBrush JuggernautStrokeBrush { get; set; }
        public IBrush UniqueStrokeBrush { get; set; }
        public IBrush GoblinStrokeBrush { get; set; }
        public IBrush RiftGuardianStrokeBrush { get; set; }

        public IFont TextFont { get; set; }

        public s7o_EliteHealthBars()
        {
            Enabled = true;
            Order = 20000;

            ShowChampionBars = true;
            ShowRareBars = true;
            ShowRareMinionBars = false;
            ShowUniqueBars = true;
            ShowGoblinBars = true;
            // Purple bars are enabled for special unique/boss actors as a best-effort visual aid.
            // Some keywarden/boss anchors may not be perfect, but showing the bar is preferred.
            ShowRiftGuardianBars = true;
            ShowKeywardenBars = true;
            ShowBossBars = true;

            HideIllusions = true;
            ShowInvisible = false;
            RequireAttackable = false;
            GreyOutUnavailableElites = true;
            GreyOutBurrowedElites = true;
            GreyOutInvulnerableElites = true;
            GreyOutInvisibleElites = true;
            GreyOutUntargetableElites = true;
            ShrinkDefaultEliteMinimapCircles = true;
            EliteMinimapCircleRadius = 7.0f;
            EliteMinionMinimapCircleRadius = 6.5f;
            ShrinkBossAndKeywardenMinimapCircles = false;
            BossMinimapCircleRadius = 10.0f;
            KeywardenMinimapCircleRadius = 6.0f;
            AddEliteMinimapCircleOutlines = true;
            EliteMinimapCircleOutlineWidth = 2.5f;
            EliteMinimapCircleOutlineAlpha = 240;
            ShowLayeredEliteMinimapOverlay = true;
            SuppressDefaultEliteMinimapCirclesWhenLayeredOverlay = true;
            EliteMinimapOverlayRadius = 7.0f;
            EliteMinionMinimapOverlayRadius = 7.0f;
            JuggernautMinimapOverlayRadius = 7.0f;
            JuggernautMinimapCenterDotRadius = 0.0f;
            EliteMinimapOverlayOutlineWidth = 2.35f;
            EliteMinimapOverlayOutlineAlpha = 205;

            ShowEliteHealthBars = true;
            BarWidth = 270.0f;
            BarHeight = 22.0f;
            ShowEliteGroundCircles = false;
            EliteGroundCircleAlpha = 230;
            EliteGroundCircleThickness = 3.0f;
            // Used only when AnchorBarsToFeet is false.
            BarYOffset = -92.0f;
            BarBorderPadding = 3.0f;
            AnchorBarsToFeet = true;
            BarFootYOffset = 8.0f;
            // Use the bottom edge of the monster's ground hitbox for most monsters.
            // Keywardens and bosses use a simpler floor-center anchor by default because their projected hitbox edges can jitter.
            // Use floor-center anchoring instead of projected hitbox-bottom anchoring.
            // This reduces the large variable gap between elite affix labels and HP bars.
            UseHitboxBottomAnchor = false;
            UsePreciseAnchorProjection = true;
            // Keep a visible edge bar while a large monster model remains in view
            // after its floor anchor has crossed the projected viewport edge.
            BarEdgeFollowRangePx = 300.0f;
            // Kept as a reasonable value in case users re-enable hitbox-bottom anchoring manually.
            HitboxAnchorRadiusScale = 0.35f;
            MaxHitboxAnchorRadius = 50.0f;
            // Keywardens and campaign bosses can jitter with projected hitbox-edge sampling.
            // Use their floor-center anchor as a stable best-effort feet position.
            UseSimpleFeetAnchorForBossesAndKeywardens = true;

            ShowOffscreenEliteLines = true;
            OffscreenLineMaxWorldDistance = 120.0f;
            OffscreenLineLength = 72.0f;
            OffscreenLineMargin = 38.0f;
            OffscreenLineStrokeWidth = 2.5f;
            OffscreenLinesUseFilteredMonstersOnly = true;
            OffscreenLineStopsAtScreenEdge = true;
            MaxOffscreenLines = 6;

            ShowHpPercentText = true;
            // Preserve the original two-tone highlight/shadow treatment. Performance
            // logging showed no meaningful PaintTopInGame reduction with it disabled.
            UseTwoToneBarLighting = true;

            ShowTopLeftEliteList = true;
            ShowTopLeftEliteNames = true;
            ShowRareMinionsInTopLeftList = false;
            // Shifted right so it does not overlap player/party portraits. Tune later if needed.
            TopLeftEliteListXFraction = 0.145f;
            TopLeftEliteListYFraction = 0.018f;
            TopLeftEliteListWidth = 130.0f;
            TopLeftEliteListBarHeight = 14.0f;
            TopLeftEliteListGap = 5.0f;
            TopLeftEliteListInnerBarGap = 2.0f;
            TopLeftEliteListLabelGap = 3.0f;
            TopLeftEliteListMaxRows = 6;

            ShowAffixTaggedEliteBars = false;
            HighlightFinalChampion = true;

            EmphasizeJuggernautBars = true;
            JuggernautBarWidthBonus = 36.0f;
            JuggernautBarHeightBonus = 4.0f;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            // Draw late so this HP overlay sits above most other world/text overlays.
            Order = 999999;

            BorderBrush = Hud.Render.CreateBrush(250, 0, 0, 0, 3.0f);
            BackgroundBrush = Hud.Render.CreateBrush(220, 15, 15, 15, 0);
            TopLeftListBackgroundBrush = Hud.Render.CreateBrush(70, 15, 15, 15, 0);
            TopLeftListBorderBrush = Hud.Render.CreateBrush(190, 0, 0, 0, 2.0f);
            TopLeftChampionBrush = Hud.Render.CreateBrush(155, 70, 140, 255, 0);
            TopLeftFinalChampionBrush = Hud.Render.CreateBrush(160, 88, 35, 135, 0);
            TopLeftRareBrush = Hud.Render.CreateBrush(155, 255, 165, 35, 0);
            TopLeftRareMinionBrush = Hud.Render.CreateBrush(140, 235, 135, 30, 0);
            TopLeftJuggernautBrush = Hud.Render.CreateBrush(170, 235, 10, 10, 0);
            TopLeftChampionLightBrush = Hud.Render.CreateBrush(175, 120, 185, 255, 0);
            TopLeftFinalChampionLightBrush = Hud.Render.CreateBrush(180, 135, 75, 190, 0);
            TopLeftRareLightBrush = Hud.Render.CreateBrush(175, 255, 205, 80, 0);
            TopLeftRareMinionLightBrush = Hud.Render.CreateBrush(155, 250, 165, 60, 0);
            TopLeftJuggernautLightBrush = Hud.Render.CreateBrush(190, 255, 120, 120, 0);
            TopLeftChampionShadowBrush = Hud.Render.CreateBrush(135, 25, 80, 190, 0);
            TopLeftFinalChampionShadowBrush = Hud.Render.CreateBrush(145, 45, 10, 90, 0);
            TopLeftRareShadowBrush = Hud.Render.CreateBrush(130, 195, 90, 15, 0);
            TopLeftRareMinionShadowBrush = Hud.Render.CreateBrush(120, 170, 80, 15, 0);
            TopLeftJuggernautShadowBrush = Hud.Render.CreateBrush(150, 160, 0, 0, 0);

            ChampionBrush = Hud.Render.CreateBrush(240, 70, 140, 255, 0);
            // Final alive blue champion: dark purple, distinct from normal blue champions
            // and visually separated from Rift Guardian / special unique purple.
            FinalChampionBrush = Hud.Render.CreateBrush(245, 88, 35, 135, 0);
            // Orangish-yellow elite bars, intentionally more orange than the old yellow/gold.
            RareBrush = Hud.Render.CreateBrush(240, 255, 165, 35, 0);
            RareMinionBrush = Hud.Render.CreateBrush(220, 235, 135, 30, 0);
            JuggernautBrush = Hud.Render.CreateBrush(255, 235, 10, 10, 0);
            UniqueBrush = Hud.Render.CreateBrush(245, 175, 80, 255, 0);
            GoblinBrush = Hud.Render.CreateBrush(245, 105, 45, 175, 0);
            RiftGuardianBrush = Hud.Render.CreateBrush(250, 190, 70, 255, 0);

            ChampionLightBrush = Hud.Render.CreateBrush(255, 120, 185, 255, 0);
            FinalChampionLightBrush = Hud.Render.CreateBrush(255, 135, 75, 190, 0);
            RareLightBrush = Hud.Render.CreateBrush(255, 255, 205, 80, 0);
            RareMinionLightBrush = Hud.Render.CreateBrush(230, 250, 165, 60, 0);
            JuggernautLightBrush = Hud.Render.CreateBrush(255, 255, 120, 120, 0);
            UniqueLightBrush = Hud.Render.CreateBrush(255, 215, 150, 255, 0);
            GoblinLightBrush = Hud.Render.CreateBrush(255, 145, 85, 215, 0);
            RiftGuardianLightBrush = Hud.Render.CreateBrush(255, 225, 130, 255, 0);

            ChampionShadowBrush = Hud.Render.CreateBrush(220, 25, 80, 190, 0);
            FinalChampionShadowBrush = Hud.Render.CreateBrush(230, 45, 10, 90, 0);
            RareShadowBrush = Hud.Render.CreateBrush(210, 195, 90, 15, 0);
            RareMinionShadowBrush = Hud.Render.CreateBrush(200, 170, 80, 15, 0);
            JuggernautShadowBrush = Hud.Render.CreateBrush(240, 160, 0, 0, 0);
            UniqueShadowBrush = Hud.Render.CreateBrush(225, 120, 25, 190, 0);
            GoblinShadowBrush = Hud.Render.CreateBrush(220, 70, 25, 125, 0);
            RiftGuardianShadowBrush = Hud.Render.CreateBrush(230, 130, 35, 200, 0);

            UnavailableEliteBrush = Hud.Render.CreateBrush(210, 105, 105, 105, 0);
            UnavailableEliteLightBrush = Hud.Render.CreateBrush(220, 165, 165, 165, 0);
            UnavailableEliteShadowBrush = Hud.Render.CreateBrush(190, 55, 55, 55, 0);
            TopLeftUnavailableEliteBrush = Hud.Render.CreateBrush(145, 105, 105, 105, 0);
            TopLeftUnavailableEliteLightBrush = Hud.Render.CreateBrush(160, 165, 165, 165, 0);
            TopLeftUnavailableEliteShadowBrush = Hud.Render.CreateBrush(125, 55, 55, 55, 0);

            ChampionStrokeBrush = Hud.Render.CreateBrush(245, 70, 140, 255, OffscreenLineStrokeWidth);
            FinalChampionStrokeBrush = Hud.Render.CreateBrush(250, 88, 35, 135, OffscreenLineStrokeWidth);
            RareStrokeBrush = Hud.Render.CreateBrush(245, 255, 165, 35, OffscreenLineStrokeWidth);
            RareMinionStrokeBrush = Hud.Render.CreateBrush(220, 235, 135, 30, OffscreenLineStrokeWidth);
            JuggernautStrokeBrush = Hud.Render.CreateBrush(255, 235, 10, 10, Math.Max(OffscreenLineStrokeWidth, 3.25f));
            UniqueStrokeBrush = Hud.Render.CreateBrush(250, 175, 80, 255, OffscreenLineStrokeWidth);
            GoblinStrokeBrush = Hud.Render.CreateBrush(245, 105, 45, 175, OffscreenLineStrokeWidth);
            RiftGuardianStrokeBrush = Hud.Render.CreateBrush(250, 190, 70, 255, OffscreenLineStrokeWidth);

            TextFont = Hud.Render.CreateFont("tahoma", 8.0f, 245, 255, 255, 255, true, false, 230, 0, 0, 0, true);

            for (int i = 0; i < _hpPercentText.Length; i++)
            {
                _hpPercentText[i] = i.ToString() + "%";
                var layout = TextFont.GetTextLayout(_hpPercentText[i]);
                _hpPercentTextWidth[i] = layout.Metrics.Width;
                _hpPercentTextHeight[i] = layout.Metrics.Height;
            }

            _eliteMinimapCircleOutlineBrush = Hud.Render.CreateBrush(
                EliteMinimapCircleOutlineAlpha,
                0, 0, 0,
                EliteMinimapCircleOutlineWidth);

            _mapOverlayBlackBrush = Hud.Render.CreateBrush(EliteMinimapOverlayOutlineAlpha, 0, 0, 0, 0);

            _mapOverlayUnavailableBrush = Hud.Render.CreateBrush(170, 105, 105, 105, 0);
            _mapOverlayChampionBrush = Hud.Render.CreateBrush(230, 64, 128, 255, 0);
            _mapOverlayRareBrush = Hud.Render.CreateBrush(235, 255, 148, 20, 0);
            _mapOverlayMinionBrush = Hud.Render.CreateBrush(210, 192, 92, 20, 0);
            // Juggernauts are lower-priority elite markers: compact red body.
            _mapOverlayJuggernautBrush = Hud.Render.CreateBrush(235, 255, 0, 0, 0);
            _mapOverlayJuggernautCenterBrush = null;
            _mapOverlayUniqueBrush = Hud.Render.CreateBrush(225, 255, 140, 255, 0);

            _mapOverlayUnavailableDecorator = CreateMapOverlayCircleDecorator(_mapOverlayUnavailableBrush, EliteMinimapOverlayRadius);
            _mapOverlayMinionDecorator = CreateMapOverlayCircleDecorator(_mapOverlayMinionBrush, EliteMinionMinimapOverlayRadius);
            _mapOverlayJuggernautDecorator = CreateMapOverlayCircleDecorator(_mapOverlayJuggernautBrush, JuggernautMinimapOverlayRadius);
            _mapOverlayJuggernautCenterDecorator = null;
            _mapOverlayChampionDecorator = CreateMapOverlayCircleDecorator(_mapOverlayChampionBrush, EliteMinimapOverlayRadius);
            _mapOverlayRareDecorator = CreateMapOverlayCircleDecorator(_mapOverlayRareBrush, EliteMinimapOverlayRadius);
            _mapOverlayUniqueDecorator = CreateMapOverlayCircleDecorator(_mapOverlayUniqueBrush, EliteMinimapOverlayRadius);

            AdjustDefaultEliteMinimapCircles();
        }

        private void AdjustDefaultEliteMinimapCircles()
        {
            if (!ShrinkDefaultEliteMinimapCircles)
                return;

            if (_defaultEliteMinimapCirclesAdjusted)
                return;

            if (_defaultEliteMinimapCircleAdjustAttempts > 120)
                return;

            _defaultEliteMinimapCircleAdjustAttempts++;

            float eliteRadius = EliteMinimapCircleRadius;
            if (eliteRadius <= 0.1f)
                eliteRadius = 7.0f;

            float minionRadius = EliteMinionMinimapCircleRadius;
            if (minionRadius <= 0.1f)
                minionRadius = 6.5f;

            float bossRadius = BossMinimapCircleRadius;
            if (bossRadius <= 0.1f)
                bossRadius = 10.0f;

            float keywardenRadius = KeywardenMinimapCircleRadius;
            if (keywardenRadius <= 0.1f)
                keywardenRadius = 6.0f;

            int adjusted = 0;

            try
            {
                Hud.RunOnPlugin<StandardMonsterPlugin>(plugin =>
                {
                    if (plugin == null)
                        return;

                    if (SuppressDefaultEliteMinimapCirclesWhenLayeredOverlay && ShowLayeredEliteMinimapOverlay)
                    {
                        adjusted += SetMapShapeDecoratorsEnabled(plugin.EliteChampionDecorator, false);
                        adjusted += SetMapShapeDecoratorsEnabled(plugin.EliteLeaderDecorator, false);
                        adjusted += SetMapShapeDecoratorsEnabled(plugin.EliteUniqueDecorator, false);
                        adjusted += SetMapShapeDecoratorsEnabled(plugin.EliteMinionDecorator, false);
                    }
                    else
                    {
                        adjusted += ConfigureMapShapeDecorators(plugin.EliteChampionDecorator, eliteRadius, true);
                        adjusted += ConfigureMapShapeDecorators(plugin.EliteLeaderDecorator, eliteRadius, true);
                        adjusted += ConfigureMapShapeDecorators(plugin.EliteUniqueDecorator, eliteRadius, true);
                        adjusted += ConfigureMapShapeDecorators(plugin.EliteMinionDecorator, minionRadius, true);
                    }

                    if (ShrinkBossAndKeywardenMinimapCircles)
                    {
                        adjusted += ConfigureMapShapeDecorators(plugin.BossDecorator, bossRadius, true);
                        adjusted += ConfigureMapShapeDecorators(plugin.KeywardenDecorator, keywardenRadius, true);
                    }
                });

                if (adjusted >= 4)
                    _defaultEliteMinimapCirclesAdjusted = true;
            }
            catch
            {
            }
        }

        private int SetMapShapeDecoratorsEnabled(WorldDecoratorCollection collection, bool enabled)
        {
            if (collection == null)
                return 0;

            int changed = 0;

            try
            {
                foreach (var decorator in collection.GetDecorators<MapShapeDecorator>())
                {
                    if (decorator == null)
                        continue;

                    decorator.Enabled = enabled;
                    changed++;
                }
            }
            catch
            {
            }

            return changed;
        }

        private int ConfigureMapShapeDecorators(WorldDecoratorCollection collection, float targetRadius, bool outline)
        {
            if (collection == null)
                return 0;

            if (targetRadius <= 0.1f)
                return 0;

            int changed = 0;

            try
            {
                foreach (var decorator in collection.GetDecorators<MapShapeDecorator>())
                {
                    if (decorator == null)
                        continue;

                    decorator.Radius = targetRadius;

                    if (outline && AddEliteMinimapCircleOutlines && _eliteMinimapCircleOutlineBrush != null)
                        decorator.ShadowBrush = _eliteMinimapCircleOutlineBrush;

                    changed++;
                }
            }
            catch
            {
            }

            return changed;
        }

        private WorldDecoratorCollection CreateMapOverlayCircleDecorator(IBrush fillBrush, float radius)
        {
            float outlineRadius = radius + EliteMinimapOverlayOutlineWidth;

            return new WorldDecoratorCollection(
                new MapShapeDecorator(Hud)
                {
                    Brush = _mapOverlayBlackBrush,
                    ShapePainter = new CircleShapePainter(Hud),
                    Radius = outlineRadius,
                },
                new MapShapeDecorator(Hud)
                {
                    Brush = fillBrush,
                    ShapePainter = new CircleShapePainter(Hud),
                    Radius = radius,
                }
            );
        }

        private void PaintLayeredEliteMinimapOverlay(WorldLayer layer)
        {
            if (!ShowLayeredEliteMinimapOverlay)
                return;

            if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;

            EnsureFramePrepared();

            // Draw order is behavior-sensitive: lower priority first, higher priority last.
            PaintMapOverlayList(_mapOverlayUnavailableDecorator, layer, _mapUnavailable, false);
            PaintMapOverlayList(_mapOverlayMinionDecorator, layer, _mapMinions, false);
            PaintMapOverlayList(_mapOverlayJuggernautDecorator, layer, _mapJuggernauts, true);
            PaintMapOverlayList(_mapOverlayChampionDecorator, layer, _mapChampions, false);
            PaintMapOverlayList(_mapOverlayRareDecorator, layer, _mapRares, false);
            PaintMapOverlayList(_mapOverlayUniqueDecorator, layer, _mapUniques, false);
        }

        private void PaintMapOverlayList(WorldDecoratorCollection decorator, WorldLayer layer, List<IMonster> monsters, bool juggernaut)
        {
            if (decorator == null || monsters == null)
                return;

            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (juggernaut)
                    PaintJuggernautMapOverlayCircle(layer, monster);
                else
                    PaintMapOverlayCircle(decorator, layer, monster);
            }
        }

        private void PaintMapOverlayCircle(WorldDecoratorCollection decorator, WorldLayer layer, IMonster monster)
        {
            if (decorator == null || monster == null || monster.FloorCoordinate == null)
                return;

            try
            {
                string label = monster.SnoMonster != null ? monster.SnoMonster.NameLocalized : string.Empty;
                decorator.Paint(layer, monster, monster.FloorCoordinate, label);
            }
            catch
            {
            }
        }

        private void PaintJuggernautMapOverlayCircle(WorldLayer layer, IMonster monster)
        {
            if (monster == null || monster.FloorCoordinate == null)
                return;

            PaintMapOverlayCircle(_mapOverlayJuggernautDecorator, layer, monster);
        }

        private bool IsExcludedFromEliteMinimapOverlay(IMonster monster)
        {
            if (monster == null)
                return true;

            if (HideIllusions && monster.Illusion)
                return true;

            if (IsOrlashCloneOrBreathMinion(monster))
                return true;

            if (IsRiftGuardian(monster))
                return true;

            if (IsKeywarden(monster))
                return true;

            if (monster.SnoMonster != null &&
                monster.SnoMonster.Priority == MonsterPriority.boss)
            {
                return true;
            }

            return false;
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (layer == WorldLayer.Map)
            {
                PaintLayeredEliteMinimapOverlay(layer);
                return;
            }

            if (layer != WorldLayer.Ground) return;
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null) return;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading) return;

            if (!_defaultEliteMinimapCirclesAdjusted)
                AdjustDefaultEliteMinimapCircles();

            if (!ShowEliteGroundCircles && !ShowOffscreenEliteLines)
                return;

            EnsureFramePrepared();

            if (ShowEliteGroundCircles)
            {
                EnsureEliteGroundCircleBrushes();
                for (int i = 0; i < _barMonsters.Count; i++)
                    DrawEliteGroundCircle(_barMonsters[i]);
            }

            if (!ShowOffscreenEliteLines)
                return;

            int maxLines = Math.Max(0, MaxOffscreenLines);
            int count = Math.Min(maxLines, _offscreenMonsters.Count);
            IScreenCoordinate playerSc = count > 0 ? GetPlayerFeetScreenCoordinate() : null;
            if (playerSc == null)
                return;

            for (int i = 0; i < count; i++)
                DrawOffscreenLine(_offscreenMonsters[i], playerSc);
        }

        private void EnsureEliteGroundCircleBrushes()
        {
            float thickness = Math.Max(0.5f, EliteGroundCircleThickness);
            int alpha = Math.Max(0, Math.Min(255, EliteGroundCircleAlpha));

            if (_eliteGroundCircleOutlineBrush != null &&
                _eliteGroundChampionCircleBrush != null &&
                _eliteGroundCircleBrushAlpha == alpha &&
                Math.Abs(_eliteGroundCircleBrushThickness - thickness) < 0.001f)
                return;

            _eliteGroundCircleBrushAlpha = alpha;
            _eliteGroundCircleBrushThickness = thickness;

            _eliteGroundCircleOutlineBrush = Hud.Render.CreateBrush(245, 0, 0, 0, thickness + 3.5f);

            // Keep these RGB values identical to the corresponding health-bar brushes.
            _eliteGroundChampionCircleBrush = Hud.Render.CreateBrush(alpha, 70, 140, 255, thickness);
            _eliteGroundFinalChampionCircleBrush = Hud.Render.CreateBrush(alpha, 88, 35, 135, thickness);
            _eliteGroundRareCircleBrush = Hud.Render.CreateBrush(alpha, 255, 165, 35, thickness);
            _eliteGroundRareMinionCircleBrush = Hud.Render.CreateBrush(alpha, 235, 135, 30, thickness);
            _eliteGroundJuggernautCircleBrush = Hud.Render.CreateBrush(alpha, 235, 10, 10, thickness);
            _eliteGroundUniqueCircleBrush = Hud.Render.CreateBrush(alpha, 175, 80, 255, thickness);
            _eliteGroundGoblinCircleBrush = Hud.Render.CreateBrush(alpha, 105, 45, 175, thickness);
            _eliteGroundRiftGuardianCircleBrush = Hud.Render.CreateBrush(alpha, 190, 70, 255, thickness);
            _eliteGroundUnavailableCircleBrush = Hud.Render.CreateBrush(alpha, 105, 105, 105, thickness);
        }

        private IBrush GetEliteGroundCircleBrush(IMonster monster)
        {
            IBrush healthBrush = GetHealthBrush(monster);

            if (ReferenceEquals(healthBrush, UnavailableEliteBrush)) return _eliteGroundUnavailableCircleBrush;
            if (ReferenceEquals(healthBrush, ChampionBrush)) return _eliteGroundChampionCircleBrush;
            if (ReferenceEquals(healthBrush, FinalChampionBrush)) return _eliteGroundFinalChampionCircleBrush;
            if (ReferenceEquals(healthBrush, RareMinionBrush)) return _eliteGroundRareMinionCircleBrush;
            if (ReferenceEquals(healthBrush, JuggernautBrush)) return _eliteGroundJuggernautCircleBrush;
            if (ReferenceEquals(healthBrush, UniqueBrush)) return _eliteGroundUniqueCircleBrush;
            if (ReferenceEquals(healthBrush, GoblinBrush)) return _eliteGroundGoblinCircleBrush;
            if (ReferenceEquals(healthBrush, RiftGuardianBrush)) return _eliteGroundRiftGuardianCircleBrush;

            return _eliteGroundRareCircleBrush;
        }

        private void DrawEliteGroundCircle(IMonster monster)
        {
            if (monster == null || !monster.IsAlive || monster.FloorCoordinate == null ||
                !monster.FloorCoordinate.IsValid)
                return;

            IBrush brush = GetEliteGroundCircleBrush(monster);
            if (brush == null)
                return;

            float radius;
            try { radius = monster.RadiusBottom; }
            catch { return; }

            if (radius <= 0.0f || float.IsNaN(radius) || float.IsInfinity(radius))
                return;

            if (_eliteGroundCircleOutlineBrush != null)
                _eliteGroundCircleOutlineBrush.DrawWorldEllipse(radius, -1, monster.FloorCoordinate);

            brush.DrawWorldEllipse(radius, -1, monster.FloorCoordinate);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip) return;
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null) return;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading) return;
            if (!ShowEliteHealthBars) return;

            EnsureFramePrepared();

            for (int i = 0; i < _barMonsters.Count; i++)
                DrawMonsterBar(_barMonsters[i]);

            DrawTopLeftEliteList();
        }

        public void BeforeRender()
        {
            PrepareFrameData();
        }

        private void EnsureFramePrepared()
        {
            int gameTick;
            try { gameTick = Hud.Game.CurrentGameTick; }
            catch { gameTick = int.MinValue; }

            if (ShouldPrepareFrame(gameTick))
                PrepareFrameData();
        }

        private bool ShouldPrepareFrame(int gameTick)
        {
            if (_preparedGameTick == int.MinValue)
                return true;

            // A new game/session can reset the collected tick counter. Rebuild immediately
            // instead of carrying references from the previous actor snapshot.
            if (gameTick < _preparedGameTick)
                return true;

            return gameTick >= _nextPrepareGameTick;
        }

        private void PrepareFrameData()
        {
            int gameTick;
            try { gameTick = Hud != null && Hud.Game != null ? Hud.Game.CurrentGameTick : int.MinValue; }
            catch { gameTick = int.MinValue; }

            if (!ShouldPrepareFrame(gameTick))
                return;

            bool newSessionTick = _preparedGameTick != int.MinValue && gameTick < _preparedGameTick;
            _preparedGameTick = gameTick;
            _nextPrepareGameTick = gameTick == int.MinValue
                ? int.MinValue
                : unchecked(gameTick + ClassificationIntervalTicks);

            if (newSessionTick)
            {
                _barAnchorState.Clear();
                _lastBarAnchorPruneTick = int.MinValue;
            }
            else
            {
                PruneBarAnchorState(gameTick);
            }

            ClearPreparedFrameData();

            if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;

            if (!ShowLayeredEliteMinimapOverlay
                && !ShowEliteHealthBars
                && !ShowEliteGroundCircles
                && !ShowOffscreenEliteLines)
            {
                return;
            }

            var monsters = Hud.Game.AliveMonsters;
            if (monsters == null)
                return;

            BuildCandidateMonstersAndChampionCounts(monsters);

            bool needMap = ShowLayeredEliteMinimapOverlay;
            bool needBars = ShowEliteHealthBars || ShowEliteGroundCircles;
            bool needOffscreen = ShowOffscreenEliteLines;
            bool needTopLeft = ShowEliteHealthBars && ShowTopLeftEliteList;

            foreach (var monster in _candidateMonsters)
            {
                if (monster == null)
                    continue;

                bool eliteLikeForVisibility = IsEliteLikeForVisibility(monster);
                bool needFilteredEligibility = needBars || (needOffscreen && OffscreenLinesUseFilteredMonstersOnly);
                bool filteredEligible = needFilteredEligibility && IsMonsterBarEligible(monster, eliteLikeForVisibility);
                bool barEligible = needBars && filteredEligible;
                bool projectedVisible = barEligible && IsMonsterProjectedInViewport(monster);
                bool offscreenEligible = false;
                if (needOffscreen && !projectedVisible)
                {
                    offscreenEligible = OffscreenLinesUseFilteredMonstersOnly
                        ? filteredEligible && IsNearbyMonster(monster)
                        : IsMonsterOffscreenLineEligible(monster, eliteLikeForVisibility);

                    if (!barEligible && offscreenEligible)
                        projectedVisible = IsMonsterProjectedInViewport(monster);
                }
                bool showBar = barEligible && projectedVisible;
                bool showOffscreen = offscreenEligible && !projectedVisible;
                bool showTopLeft = needTopLeft && ShouldShowInTopLeftList(monster);
                bool mapEligible = needMap
                    && eliteLikeForVisibility
                    && !IsExcludedFromEliteMinimapOverlay(monster);

                if (!showBar && !showOffscreen && !showTopLeft && !mapEligible)
                    continue;

                EliteRenderState state = BuildRenderState(monster, eliteLikeForVisibility);
                _renderStateByMonster[monster] = state;

                if (showBar)
                    _barMonsters.Add(monster);
                if (showOffscreen)
                    _offscreenMonsters.Add(monster);
                if (showTopLeft)
                    _topLeftCandidates.Add(monster);

                if (!mapEligible)
                    continue;

                if (state.IsUnavailable)
                {
                    _mapUnavailable.Add(monster);
                    continue;
                }

                if (monster.Rarity == ActorRarity.RareMinion)
                    _mapMinions.Add(monster);

                if (state.IsJuggernaut)
                {
                    _mapJuggernauts.Add(monster);
                    continue;
                }

                switch (monster.Rarity)
                {
                    case ActorRarity.Champion:
                        _mapChampions.Add(monster);
                        break;
                    case ActorRarity.Rare:
                        _mapRares.Add(monster);
                        break;
                    case ActorRarity.Unique:
                        _mapUniques.Add(monster);
                        break;
                }
            }

            if (needTopLeft)
                BuildTopLeftElitePacks();
        }

        private void ClearPreparedFrameData()
        {
            _renderStateByMonster.Clear();
            _candidateMonsters.Clear();
            _barMonsters.Clear();
            _offscreenMonsters.Clear();
            _topLeftCandidates.Clear();
            _mapUnavailable.Clear();
            _mapMinions.Clear();
            _mapJuggernauts.Clear();
            _mapChampions.Clear();
            _mapRares.Clear();
            _mapUniques.Clear();
            ResetTopLeftPackBuffers();
        }

        private bool IsEliteLikeForVisibility(IMonster monster)
        {
            if (monster == null) return false;

            return monster.IsElite
                || monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.RareMinion
                || monster.Rarity == ActorRarity.Unique
                || monster.Rarity == ActorRarity.Boss
                || IsRiftGuardian(monster)
                || IsKeywarden(monster)
                || IsBossLikeMonster(monster);
        }

        private bool IsMorluRecoveryState(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                string name = monster.SnoMonster != null ? monster.SnoMonster.NameEnglish : null;
                if (string.IsNullOrEmpty(name) || name.IndexOf("Morlu", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                if (!monster.IsAlive || !monster.IsOnScreen || monster.MaxHealth <= 0
                    || monster.Invulnerable || monster.Untargetable || monster.Hidden || monster.Stealthed)
                {
                    return false;
                }

                double hp = monster.CurHealth / monster.MaxHealth;
                return hp > 0.0d && hp <= MorluRecoveryHealthThreshold
                    && (monster.Invisible || !monster.Attackable);
            }
            catch { return false; }
        }

        private bool IsEliteTemporarilyUnavailable(IMonster monster, bool eliteLikeForVisibility)
        {
            if (!GreyOutUnavailableElites) return false;
            if (monster == null) return false;
            if (!eliteLikeForVisibility) return false;
            if (IsMorluRecoveryState(monster)) return false;

            if (GreyOutBurrowedElites && monster.Burrowed)
                return true;

            if (GreyOutInvulnerableElites && monster.Invulnerable)
                return true;

            if (GreyOutInvisibleElites &&
                (monster.Hidden || monster.Invisible || monster.Stealthed))
            {
                return true;
            }

            if (GreyOutUntargetableElites)
            {
                try
                {
                    if (monster.Untargetable)
                        return true;
                }
                catch
                {
                }

                // Avoid greying only because the monster is offscreen.
                // Attackable can be false for offscreen monsters, so only use this onscreen.
                if (monster.IsOnScreen && !monster.Attackable)
                    return true;
            }

            return false;
        }

        private bool IsMonsterBarEligible(IMonster monster, bool eliteLikeForVisibility)
        {
            if (monster == null) return false;
            if (!monster.IsAlive) return false;
            if (monster.MaxHealth <= 0) return false;

            if (!ShowInvisible && !eliteLikeForVisibility)
            {
                if (monster.Invisible || monster.Hidden || monster.Stealthed) return false;
            }
            if (HideIllusions && monster.Illusion) return false;

            if (RequireAttackable && !eliteLikeForVisibility && !monster.Attackable) return false;

            // Fast path for normal blue/yellow elites. Trust Rarity even if IsElite flickers false while burrowed/changing state.
            if (monster.IsElite
                || monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.RareMinion)
            {
                switch (monster.Rarity)
                {
                    case ActorRarity.Champion:
                        return ShowChampionBars;

                    case ActorRarity.Rare:
                        return ShowRareBars;

                    case ActorRarity.RareMinion:
                        return ShowRareMinionBars;
                }
            }

            // Normal non-elite trash should fail closed before expensive special/proxy checks.
            // Goblins remain an explicit opt-in special case using the cheap MonsterPriority path.
            if (!monster.IsElite
                && monster.Rarity != ActorRarity.Champion
                && monster.Rarity != ActorRarity.Rare
                && monster.Rarity != ActorRarity.RareMinion
                && monster.Rarity != ActorRarity.Unique
                && monster.Rarity != ActorRarity.Boss)
            {
                return ShowGoblinBars && HasMonsterPriority(monster, MonsterPriority.goblin);
            }

            if (IsOrlashCloneOrBreathMinion(monster)) return false;
            if (IsRiftGuardianSpawnedMinion(monster)) return false;
            if (IsKnownFalseUniqueSpecial(monster)) return false;
            if (ShouldSuppressUnstableSpecialMonster(monster)) return false;

            if (ShowRiftGuardianBars && IsRiftGuardian(monster)) return true;
            if (ShowKeywardenBars && IsKeywarden(monster)) return true;
            if (ShowBossBars && IsBossLikeMonster(monster)) return true;
            if (ShowGoblinBars && IsGoblin(monster)) return true;
            if (ShowUniqueBars && IsUniqueMonster(monster)) return true;

            if (!monster.IsElite
                && monster.Rarity != ActorRarity.Champion
                && monster.Rarity != ActorRarity.Rare
                && monster.Rarity != ActorRarity.RareMinion)
            {
                return false;
            }

            switch (monster.Rarity)
            {
                case ActorRarity.Unique:
                    return ShowUniqueBars && IsUniqueMonster(monster);

                case ActorRarity.Boss:
                    return ShowBossBars && IsBossLikeMonster(monster);

                default:
                    return false;
            }
        }

        private bool IsMonsterOffscreenLineEligible(IMonster monster, bool eliteLikeForVisibility)
        {
            if (!ShowOffscreenEliteLines || monster == null || !monster.IsAlive || monster.MaxHealth <= 0)
                return false;

            if (OffscreenLinesUseFilteredMonstersOnly)
                return IsMonsterBarEligible(monster, eliteLikeForVisibility) && IsNearbyMonster(monster);

            if (!ShowInvisible && !eliteLikeForVisibility
                && (monster.Invisible || monster.Hidden || monster.Stealthed))
            {
                return false;
            }

            if (HideIllusions && monster.Illusion)
                return false;

            if (RequireAttackable && !eliteLikeForVisibility && !monster.Attackable)
                return false;

            return IsNearbyMonster(monster);
        }

        private bool HasAnyEliteAffix(IMonster monster)
        {
            if (monster == null || monster.AffixSnoList == null)
                return false;

            try
            {
                foreach (var affix in monster.AffixSnoList)
                {
                    if (affix != null)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsAffixTaggedEliteFallback(IMonster monster)
        {
            if (!ShowAffixTaggedEliteBars)
                return false;

            if (monster == null)
                return false;

            // Avoid turning ordinary noncombat/event objects into elite bars.
            if (monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.RareMinion
                || monster.Rarity == ActorRarity.Unique
                || monster.Rarity == ActorRarity.Boss)
            {
                return false;
            }

            return HasAnyEliteAffix(monster);
        }

        private void BuildCandidateMonstersAndChampionCounts(IEnumerable<IMonster> monsters)
        {
            _candidateMonsters.Clear();
            _championFamilyAliveCounts.Clear();
            _championPackAliveCounts.Clear();

            if (monsters == null)
                return;

            try
            {
                foreach (var monster in monsters)
                {
                    if (monster == null || !monster.IsAlive)
                        continue;

                    if (HighlightFinalChampion
                        && monster.Rarity == ActorRarity.Champion
                        && (!HideIllusions || !monster.Illusion))
                    {
                        ChampionFamilyKey familyKey = GetChampionFamilyKey(monster);
                        if (!familyKey.IsEmpty)
                        {
                            int familyCount;
                            _championFamilyAliveCounts.TryGetValue(familyKey, out familyCount);
                            _championFamilyAliveCounts[familyKey] = familyCount + 1;
                        }

                        var pack = monster.Pack;
                        if (pack != null)
                        {
                            int packCount;
                            _championPackAliveCounts.TryGetValue(pack, out packCount);
                            _championPackAliveCounts[pack] = packCount + 1;
                        }
                    }

                    if (IsPotentialEliteRenderCandidate(monster))
                        _candidateMonsters.Add(monster);
                }
            }
            catch
            {
                _candidateMonsters.Clear();
                _championFamilyAliveCounts.Clear();
                _championPackAliveCounts.Clear();
            }
        }

        private bool IsPotentialEliteRenderCandidate(IMonster monster)
        {
            if (monster == null || !monster.IsAlive)
                return false;

            // Preserve the optional unfiltered-line mode exactly: when enabled, ordinary
            // nearby trash is intentionally eligible for offscreen-line consideration.
            if (ShowOffscreenEliteLines && !OffscreenLinesUseFilteredMonstersOnly)
                return true;

            if (monster.IsElite)
                return true;

            switch (monster.Rarity)
            {
                case ActorRarity.Champion:
                case ActorRarity.Rare:
                case ActorRarity.RareMinion:
                case ActorRarity.Unique:
                case ActorRarity.Boss:
                    return true;
            }

            try
            {
                if (monster.SnoMonster != null)
                {
                    MonsterPriority priority = monster.SnoMonster.Priority;
                    if (priority == MonsterPriority.goblin
                        || priority == MonsterPriority.keywarden
                        || priority == MonsterPriority.boss)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsFinalAliveChampion(IMonster monster)
        {
            if (!HighlightFinalChampion || monster == null || !monster.IsAlive || monster.Rarity != ActorRarity.Champion)
                return false;

            var pack = monster.Pack;
            if (pack == null)
                return false;

            int packAliveChampions;
            if (!_championPackAliveCounts.TryGetValue(pack, out packAliveChampions) || packAliveChampions != 1)
                return false;

            ChampionFamilyKey familyKey = GetChampionFamilyKey(monster);
            if (familyKey.IsEmpty)
                return false;

            int sameFamilyAliveChampions;
            return _championFamilyAliveCounts.TryGetValue(familyKey, out sameFamilyAliveChampions)
                && sameFamilyAliveChampions == 1;
        }

        private bool IsJuggernaut(IMonster monster)
        {
            return HasAffix(monster, MonsterAffix.Juggernaut);
        }

        private EliteRenderState GetRenderState(IMonster monster)
        {
            EliteRenderState state;
            if (monster != null && _renderStateByMonster.TryGetValue(monster, out state))
                return state;

            return BuildRenderState(monster);
        }

        private EliteRenderState BuildRenderState(IMonster monster)
        {
            return BuildRenderState(monster, IsEliteLikeForVisibility(monster));
        }

        private EliteRenderState BuildRenderState(IMonster monster, bool eliteLikeForVisibility)
        {
            var state = new EliteRenderState
            {
                Kind = EliteVisualKind.Rare,
                IsUnavailable = monster != null && IsEliteTemporarilyUnavailable(monster, eliteLikeForVisibility),
                IsJuggernaut = monster != null && IsJuggernaut(monster),
                IsFinalChampion = monster != null && IsFinalAliveChampion(monster),
            };

            if (monster == null)
                return state;

            switch (monster.Rarity)
            {
                case ActorRarity.Champion:
                    state.Kind = state.IsJuggernaut
                        ? EliteVisualKind.Juggernaut
                        : (state.IsFinalChampion ? EliteVisualKind.FinalChampion : EliteVisualKind.Champion);
                    return state;

                case ActorRarity.Rare:
                    state.Kind = state.IsJuggernaut ? EliteVisualKind.Juggernaut : EliteVisualKind.Rare;
                    return state;

                case ActorRarity.RareMinion:
                    state.Kind = state.IsJuggernaut ? EliteVisualKind.Juggernaut : EliteVisualKind.RareMinion;
                    return state;
            }

            if (ShowRiftGuardianBars && IsRiftGuardian(monster))
                state.Kind = EliteVisualKind.RiftGuardian;
            else if (state.IsJuggernaut)
                state.Kind = EliteVisualKind.Juggernaut;
            else if (ShowKeywardenBars && IsKeywarden(monster))
                state.Kind = EliteVisualKind.Unique;
            else if (ShowBossBars && IsBossLikeMonster(monster))
                state.Kind = EliteVisualKind.Unique;
            else if (ShowGoblinBars && IsGoblin(monster))
                state.Kind = EliteVisualKind.Goblin;
            else if (ShowUniqueBars && IsUniqueMonster(monster))
                state.Kind = EliteVisualKind.Unique;
            else if (monster.Rarity == ActorRarity.Unique || monster.Rarity == ActorRarity.Boss)
                state.Kind = EliteVisualKind.Unique;

            return state;
        }

        private IBrush GetHealthBrush(IMonster monster)
        {
            return GetHealthBrush(GetRenderState(monster));
        }

        private IBrush GetHealthBrush(EliteRenderState state)
        {
            if (state.IsUnavailable) return UnavailableEliteBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return ChampionBrush;
                case EliteVisualKind.FinalChampion: return FinalChampionBrush;
                case EliteVisualKind.RareMinion: return RareMinionBrush;
                case EliteVisualKind.Juggernaut: return JuggernautBrush;
                case EliteVisualKind.Unique: return UniqueBrush;
                case EliteVisualKind.Goblin: return GoblinBrush;
                case EliteVisualKind.RiftGuardian: return RiftGuardianBrush;
                default: return RareBrush;
            }
        }

        private IBrush GetHealthLightBrush(EliteRenderState state)
        {
            if (state.IsUnavailable) return UnavailableEliteLightBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return ChampionLightBrush;
                case EliteVisualKind.FinalChampion: return FinalChampionLightBrush;
                case EliteVisualKind.RareMinion: return RareMinionLightBrush;
                case EliteVisualKind.Juggernaut: return JuggernautLightBrush;
                case EliteVisualKind.Unique: return UniqueLightBrush;
                case EliteVisualKind.Goblin: return GoblinLightBrush;
                case EliteVisualKind.RiftGuardian: return RiftGuardianLightBrush;
                default: return RareLightBrush;
            }
        }

        private IBrush GetHealthShadowBrush(EliteRenderState state)
        {
            if (state.IsUnavailable) return UnavailableEliteShadowBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return ChampionShadowBrush;
                case EliteVisualKind.FinalChampion: return FinalChampionShadowBrush;
                case EliteVisualKind.RareMinion: return RareMinionShadowBrush;
                case EliteVisualKind.Juggernaut: return JuggernautShadowBrush;
                case EliteVisualKind.Unique: return UniqueShadowBrush;
                case EliteVisualKind.Goblin: return GoblinShadowBrush;
                case EliteVisualKind.RiftGuardian: return RiftGuardianShadowBrush;
                default: return RareShadowBrush;
            }
        }

        private IBrush GetStrokeBrush(IMonster monster)
        {
            EliteRenderState state = GetRenderState(monster);

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return ChampionStrokeBrush;
                case EliteVisualKind.FinalChampion: return FinalChampionStrokeBrush;
                case EliteVisualKind.RareMinion: return RareMinionStrokeBrush;
                case EliteVisualKind.Juggernaut: return JuggernautStrokeBrush;
                case EliteVisualKind.Unique: return UniqueStrokeBrush;
                case EliteVisualKind.Goblin: return GoblinStrokeBrush;
                case EliteVisualKind.RiftGuardian: return RiftGuardianStrokeBrush;
                default: return RareStrokeBrush;
            }
        }

        private void DrawHealthFill(EliteRenderState state, float fillX, float fillY, float fillW, float fillH, float hp)
        {
            float hpW = fillW * hp;
            if (hpW <= 0.0f)
                return;

            var baseBrush = GetHealthBrush(state);
            if (baseBrush != null)
                baseBrush.DrawRectangleGridFit(fillX, fillY, hpW, fillH);

            if (!UseTwoToneBarLighting || fillH < 6.0f)
                return;

            var lightBrush = GetHealthLightBrush(state);
            if (lightBrush != null)
                lightBrush.DrawRectangleGridFit(fillX, fillY, hpW, fillH * 0.45f);

            var shadowBrush = GetHealthShadowBrush(state);
            if (shadowBrush != null)
                shadowBrush.DrawRectangleGridFit(fillX, fillY + fillH * 0.72f, hpW, fillH * 0.28f);
        }

        private void DrawMonsterBar(IMonster monster)
        {
            if (monster == null || !monster.IsAlive)
                return;

            var sc = AnchorBarsToFeet
                ? GetMonsterFeetScreenCoordinate(monster)
                : monster.ScreenCoordinate;

            if (sc == null) return;

            float anchorX = sc.X;
            float anchorY = sc.Y;
            StabilizeBarAnchor(monster, ref anchorX, ref anchorY);

            float hp = 0.0f;
            if (monster.MaxHealth > 0)
                hp = (float)(monster.CurHealth / monster.MaxHealth);

            if (hp < 0.0f) hp = 0.0f;
            if (hp > 1.0f) hp = 1.0f;

            float w = BarWidth;
            float h = BarHeight;

            EliteRenderState renderState = GetRenderState(monster);
            if (EmphasizeJuggernautBars && renderState.IsJuggernaut)
            {
                w += Math.Max(0.0f, JuggernautBarWidthBonus);
                h += Math.Max(0.0f, JuggernautBarHeightBonus);
            }

            float x = anchorX - w * 0.5f;
            float y = AnchorBarsToFeet
                ? anchorY + BarFootYOffset
                : anchorY + BarYOffset;

            // The monster model can remain visible after its floor anchor leaves the
            // viewport, especially along the top edge at deep zoom. Candidates are
            // bounded by BarEdgeFollowRangePx; keep their bar inside the real window.
            if (Hud != null && Hud.Window != null)
            {
                var size = Hud.Window.Size;
                float inset = 4.0f;
                if (size.Width > w + inset * 2.0f)
                    x = Math.Max(inset, Math.Min(size.Width - w - inset, x));
                if (size.Height > h + inset * 2.0f)
                    y = Math.Max(inset, Math.Min(size.Height - h - inset, y));
            }

            float pad = BarBorderPadding;
            if (pad < 0.0f) pad = 0.0f;
            if (pad * 2.0f >= w) pad = 0.0f;
            if (pad * 2.0f >= h) pad = 0.0f;

            float fillX = x + pad;
            float fillY = y + pad;
            float fillW = w - pad * 2.0f;
            float fillH = h - pad * 2.0f;

            BackgroundBrush.DrawRectangleGridFit(x, y, w, h);
            DrawHealthFill(renderState, fillX, fillY, fillW, fillH, hp);
            BorderBrush.DrawRectangleGridFit(x, y, w, h);

            if (ShowHpPercentText)
                DrawHpText(monster, x, y, w, h);

        }

        private void StabilizeBarAnchor(IMonster monster, ref float x, ref float y)
        {
            if (monster == null || float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                return;

            uint acdId;
            try { acdId = (uint)monster.AcdId; }
            catch { return; }
            if (acdId == 0)
                return;

            int tick;
            try { tick = Hud.Game.CurrentGameTick; }
            catch { tick = int.MinValue; }

            BarAnchorState previous;
            if (_barAnchorState.TryGetValue(acdId, out previous)
                && tick >= previous.LastSeenTick
                && tick - previous.LastSeenTick <= 4)
            {
                float dx = x - previous.X;
                float dy = y - previous.Y;
                if (Math.Abs(dx) <= BarAnchorJitterThresholdPx
                    && Math.Abs(dy) <= BarAnchorJitterThresholdPx)
                {
                    x = previous.X + (dx * BarAnchorJitterFollow);
                    y = previous.Y + (dy * BarAnchorJitterFollow);
                }
            }

            _barAnchorState[acdId] = new BarAnchorState
            {
                X = x,
                Y = y,
                LastSeenTick = tick,
            };
        }

        private void PruneBarAnchorState(int gameTick)
        {
            if (_barAnchorState.Count == 0 || gameTick == int.MinValue)
                return;

            if (_lastBarAnchorPruneTick != int.MinValue
                && gameTick >= _lastBarAnchorPruneTick
                && gameTick - _lastBarAnchorPruneTick < 120)
            {
                return;
            }

            _lastBarAnchorPruneTick = gameTick;
            _staleBarAnchorIds.Clear();
            foreach (var pair in _barAnchorState)
            {
                if (gameTick < pair.Value.LastSeenTick || gameTick - pair.Value.LastSeenTick > 180)
                    _staleBarAnchorIds.Add(pair.Key);
            }

            for (int i = 0; i < _staleBarAnchorIds.Count; i++)
                _barAnchorState.Remove(_staleBarAnchorIds[i]);
        }

        private IScreenCoordinate GetMonsterFeetScreenCoordinate(IMonster monster)
        {
            if (monster == null) return null;

            var baseCoord = GetMonsterAnchorWorldCoordinate(monster);
            var centerSc = GetScreenCoordinateSafe(baseCoord, monster.ScreenCoordinate);
            if (centerSc == null)
                return monster.ScreenCoordinate;

            if (!UseHitboxBottomAnchor)
                return centerSc;

            if (ShouldUseSimpleFeetAnchor(monster))
                return centerSc;

            float radius = monster.RadiusBottom;
            if (radius <= 0.0f)
                radius = monster.RadiusScaled;

            radius *= HitboxAnchorRadiusScale;

            if (MaxHitboxAnchorRadius > 0.0f && radius > MaxHitboxAnchorRadius)
                radius = MaxHitboxAnchorRadius;

            if (radius <= 0.1f || baseCoord == null)
                return centerSc;

            // Resolve the screen-lowest point of the circular ground footprint from the
            // local screen-Y gradient. This is continuous as the camera/monster moves,
            // unlike choosing the winner from eight discrete compass samples, and it
            // cuts the hitbox anchor from nine raw projections to four.
            return GetStableHitboxBottomScreenCoordinate(baseCoord, centerSc, radius);
        }

        private bool ShouldUseSimpleFeetAnchor(IMonster monster)
        {
            if (!UseSimpleFeetAnchorForBossesAndKeywardens || monster == null)
                return false;

            // Normal blue/yellow elites never need special/boss anchor classification.
            if (monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.RareMinion)
            {
                return false;
            }

            // Rift Guardians were reported to work well with the normal hitbox-bottom anchor,
            // so do not simplify their anchor unless they are also detected as a non-RG boss.
            if (IsRiftGuardian(monster))
                return false;

            return IsKeywarden(monster) || IsBossLikeMonster(monster);
        }

        private IWorldCoordinate GetMonsterAnchorWorldCoordinate(IMonster monster)
        {
            if (monster == null)
                return null;

            if (monster.FloorCoordinate != null && monster.FloorCoordinate.IsValid)
                return monster.FloorCoordinate;

            if (monster.CollisionCoordinate != null && monster.CollisionCoordinate.IsValid)
                return monster.CollisionCoordinate;

            return null;
        }

        private bool IsMonsterProjectedInViewport(IMonster monster)
        {
            if (Hud == null || Hud.Window == null)
                return false;

            var screen = GetScreenCoordinateSafe(GetMonsterAnchorWorldCoordinate(monster), null);
            if (screen == null ||
                float.IsNaN(screen.X) || float.IsInfinity(screen.X) ||
                float.IsNaN(screen.Y) || float.IsInfinity(screen.Y))
                return false;

            var size = Hud.Window.Size;
            if (size.Width <= 0 || size.Height <= 0)
                return false;

            float range = BarEdgeFollowRangePx;
            if (float.IsNaN(range) || float.IsInfinity(range))
                range = 0.0f;
            else
                range = Math.Max(0.0f, range);

            // Measure the configured follow range from the projected monster anchor,
            // not from the bar rectangle, so 300 px means exactly 300 px on every edge.
            return screen.X >= -range && screen.X <= size.Width + range &&
                   screen.Y >= -range && screen.Y <= size.Height + range;
        }

        private IScreenCoordinate GetScreenCoordinateSafe(IWorldCoordinate coord, IScreenCoordinate fallback)
        {
            if (coord == null)
                return fallback;

            try
            {
                // Raw projection preserves the true screen position outside the
                // viewport; non-raw FreeHUD projection can pull it back toward the edge.
                return coord.ToScreenCoordinate(true, UsePreciseAnchorProjection);
            }
            catch
            {
                try
                {
                    return coord.ToScreenCoordinate(true, false);
                }
                catch
                {
                    return fallback;
                }
            }
        }

        private IScreenCoordinate GetStableHitboxBottomScreenCoordinate(
            IWorldCoordinate baseCoord,
            IScreenCoordinate centerSc,
            float radius)
        {
            if (baseCoord == null || centerSc == null || radius <= 0.1f)
                return centerSc;

            try
            {
                var xProbe = GetScreenCoordinateSafe(baseCoord.Offset(radius, 0.0f, 0.0f), null);
                var yProbe = GetScreenCoordinateSafe(baseCoord.Offset(0.0f, radius, 0.0f), null);
                if (!IsFiniteScreenCoordinate(xProbe) || !IsFiniteScreenCoordinate(yProbe))
                    return centerSc;

                float gradientX = xProbe.Y - centerSc.Y;
                float gradientY = yProbe.Y - centerSc.Y;
                float gradientLength = (float)Math.Sqrt((gradientX * gradientX) + (gradientY * gradientY));
                if (gradientLength <= 0.0001f || float.IsNaN(gradientLength) || float.IsInfinity(gradientLength))
                    return centerSc;

                float scale = radius / gradientLength;
                var bottomWorld = baseCoord.Offset(gradientX * scale, gradientY * scale, 0.0f);
                var bottomSc = GetScreenCoordinateSafe(bottomWorld, null);
                if (!IsFiniteScreenCoordinate(bottomSc))
                    return centerSc;

                return bottomSc.Y >= centerSc.Y ? bottomSc : centerSc;
            }
            catch
            {
                return centerSc;
            }
        }

        private static bool IsFiniteScreenCoordinate(IScreenCoordinate coordinate)
        {
            return coordinate != null
                && !float.IsNaN(coordinate.X) && !float.IsInfinity(coordinate.X)
                && !float.IsNaN(coordinate.Y) && !float.IsInfinity(coordinate.Y);
        }

        private IScreenCoordinate GetPlayerFeetScreenCoordinate()
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return null;

            try
            {
                if (Hud.Game.Me.FloorCoordinate != null)
                    return GetScreenCoordinateSafe(Hud.Game.Me.FloorCoordinate, Hud.Game.Me.ScreenCoordinate);
            }
            catch
            {
                // Fall through to ScreenCoordinate fallback.
            }

            return Hud.Game.Me.ScreenCoordinate;
        }

        private void DrawOffscreenLine(IMonster monster, IScreenCoordinate playerSc)
        {
            if (monster == null || !monster.IsAlive || playerSc == null) return;

            var brush = GetStrokeBrush(monster);
            if (brush == null) return;

            var monsterSc = GetMonsterFeetScreenCoordinate(monster);
            if (monsterSc == null) return;

            var windowSize = Hud.Window.Size;

            float left = OffscreenLineMargin;
            float top = OffscreenLineMargin;
            float right = windowSize.Width - OffscreenLineMargin;
            float bottom = windowSize.Height - OffscreenLineMargin;

            float startX = playerSc.X;
            float startY = playerSc.Y;

            float dx = monsterSc.X - startX;
            float dy = monsterSc.Y - startY;

            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1.0f) return;

            dx /= len;
            dy /= len;

            float endX;
            float endY;

            if (OffscreenLineStopsAtScreenEdge)
            {
                if (!GetScreenEdgePoint(startX, startY, dx, dy, left, top, right, bottom, out endX, out endY))
                    return;
            }
            else
            {
                float lineLength = Math.Max(1.0f, OffscreenLineLength);
                endX = startX + dx * lineLength;
                endY = startY + dy * lineLength;
            }

            brush.DrawLine(startX, startY, endX, endY);
        }

        private bool GetScreenEdgePoint(
            float cx,
            float cy,
            float dx,
            float dy,
            float left,
            float top,
            float right,
            float bottom,
            out float x,
            out float y)
        {
            x = cx;
            y = cy;

            float bestT = float.MaxValue;

            if (Math.Abs(dx) > 0.0001f)
            {
                float tRight = (right - cx) / dx;
                if (tRight > 0.0f)
                {
                    float yy = cy + dy * tRight;
                    if (yy >= top && yy <= bottom && tRight < bestT)
                    {
                        bestT = tRight;
                        x = right;
                        y = yy;
                    }
                }

                float tLeft = (left - cx) / dx;
                if (tLeft > 0.0f)
                {
                    float yy = cy + dy * tLeft;
                    if (yy >= top && yy <= bottom && tLeft < bestT)
                    {
                        bestT = tLeft;
                        x = left;
                        y = yy;
                    }
                }
            }

            if (Math.Abs(dy) > 0.0001f)
            {
                float tBottom = (bottom - cy) / dy;
                if (tBottom > 0.0f)
                {
                    float xx = cx + dx * tBottom;
                    if (xx >= left && xx <= right && tBottom < bestT)
                    {
                        bestT = tBottom;
                        x = xx;
                        y = bottom;
                    }
                }

                float tTop = (top - cy) / dy;
                if (tTop > 0.0f)
                {
                    float xx = cx + dx * tTop;
                    if (xx >= left && xx <= right && tTop < bestT)
                    {
                        bestT = tTop;
                        x = xx;
                        y = top;
                    }
                }
            }

            return bestT < float.MaxValue;
        }

        private bool IsNearbyMonster(IMonster monster)
        {
            if (monster == null || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            if (OffscreenLineMaxWorldDistance <= 0.0f)
                return true;

            if (monster.FloorCoordinate == null || Hud.Game.Me.FloorCoordinate == null)
                return monster.NormalizedXyDistanceToMe <= OffscreenLineMaxWorldDistance;

            return monster.FloorCoordinate.XYDistanceTo(Hud.Game.Me.FloorCoordinate) <= OffscreenLineMaxWorldDistance;
        }

        private void DrawHpText(IMonster monster, float x, float y, float w, float h)
        {
            if (TextFont == null || monster.MaxHealth <= 0) return;

            int pct = (int)Math.Round((monster.CurHealth / monster.MaxHealth) * 100.0);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;

            string text = _hpPercentText[pct];
            TextFont.DrawText(
                text,
                x + (w - _hpPercentTextWidth[pct]) * 0.5f,
                y + (h - _hpPercentTextHeight[pct]) * 0.5f);
        }



        private class ElitePackListEntry
        {
            public IMonster Representative;
            public string Name;
            public readonly List<IMonster> Monsters = new List<IMonster>();

            public void Reset()
            {
                Representative = null;
                Name = string.Empty;
                Monsters.Clear();
            }
        }

        private ElitePackListEntry RentTopLeftPack(IMonster representative)
        {
            ElitePackListEntry pack = _topLeftPackPool.Count > 0
                ? _topLeftPackPool.Pop()
                : new ElitePackListEntry();

            pack.Representative = representative;
            pack.Name = GetShortMonsterName(representative);
            return pack;
        }

        private void ResetTopLeftPackBuffers()
        {
            for (int i = 0; i < _topLeftPacks.Count; i++)
            {
                var pack = _topLeftPacks[i];
                if (pack == null)
                    continue;

                pack.Reset();
                _topLeftPackPool.Push(pack);
            }

            _topLeftPacks.Clear();
            _topLeftChampionPacks.Clear();
            _topLeftFallbackChampionGroups.Clear();
        }

        private void DrawTopLeftEliteList()
        {
            if (!ShowTopLeftEliteList || _topLeftPacks.Count == 0) return;

            float x = Hud.Window.Size.Width * TopLeftEliteListXFraction;
            float y = Hud.Window.Size.Height * TopLeftEliteListYFraction;
            float w = TopLeftEliteListWidth;
            float barH = TopLeftEliteListBarHeight;
            float rowGap = TopLeftEliteListInnerBarGap;
            float packGap = TopLeftEliteListGap;
            int drawnPacks = 0;

            for (int p = 0; p < _topLeftPacks.Count; p++)
            {
                var pack = _topLeftPacks[p];
                if (pack == null || pack.Monsters.Count == 0)
                    continue;

                if (drawnPacks >= TopLeftEliteListMaxRows)
                    break;

                if (ShowTopLeftEliteNames && TextFont != null && !string.IsNullOrEmpty(pack.Name))
                {
                    var nameLayout = TextFont.GetTextLayout(pack.Name);
                    TextFont.DrawText(nameLayout, x, y);
                    y += nameLayout.Metrics.Height + TopLeftEliteListLabelGap;
                }

                for (int i = 0; i < pack.Monsters.Count; i++)
                {
                    DrawTopLeftSmallEliteBar(pack.Monsters[i], x, y, w, barH);
                    y += barH + rowGap;
                }

                y += packGap;
                drawnPacks++;
            }
        }

        private void BuildTopLeftElitePacks()
        {
            ResetTopLeftPackBuffers();

            if (!ShowTopLeftEliteList)
                return;

            for (int i = 0; i < _topLeftCandidates.Count; i++)
            {
                var monster = _topLeftCandidates[i];
                if (monster == null)
                    continue;

                if (monster.Rarity == ActorRarity.Champion)
                {
                    ElitePackListEntry pack;
                    var monsterPack = monster.Pack;

                    if (monsterPack != null)
                    {
                        if (!_topLeftChampionPacks.TryGetValue(monsterPack, out pack))
                        {
                            pack = RentTopLeftPack(monster);
                            _topLeftChampionPacks[monsterPack] = pack;
                            _topLeftPacks.Add(pack);
                        }
                    }
                    else
                    {
                        ChampionFamilyKey key = GetChampionFamilyKey(monster);
                        if (key.IsEmpty || !_topLeftFallbackChampionGroups.TryGetValue(key, out pack))
                        {
                            pack = RentTopLeftPack(monster);
                            if (!key.IsEmpty)
                                _topLeftFallbackChampionGroups[key] = pack;
                            _topLeftPacks.Add(pack);
                        }
                    }

                    pack.Monsters.Add(monster);
                    continue;
                }

                if (monster.Rarity != ActorRarity.Rare)
                    continue;

                var rarePack = RentTopLeftPack(monster);
                rarePack.Monsters.Add(monster);

                if (ShowRareMinionsInTopLeftList && monster.Pack != null)
                {
                    try
                    {
                        var alive = monster.Pack.MonstersAlive;
                        if (alive != null)
                        {
                            foreach (var minion in alive)
                            {
                                if (minion == null || !minion.IsAlive) continue;
                                if (minion.Rarity != ActorRarity.RareMinion) continue;
                                if (HideIllusions && minion.Illusion) continue;

                                bool eliteLikeForVisibility = IsEliteLikeForVisibility(minion);
                                if (!ShowInvisible && !eliteLikeForVisibility && (minion.Invisible || minion.Hidden || minion.Stealthed)) continue;
                                if (minion.MaxHealth <= 0) continue;

                                rarePack.Monsters.Add(minion);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                _topLeftPacks.Add(rarePack);
            }

            for (int i = 0; i < _topLeftPacks.Count; i++)
                _topLeftPacks[i].Monsters.Sort(CompareMonsterHealthDescending);
        }

        private static int CompareMonsterHealthDescending(IMonster a, IMonster b)
        {
            double ahp = a != null && a.MaxHealth > 0 ? a.CurHealth / a.MaxHealth : 0.0;
            double bhp = b != null && b.MaxHealth > 0 ? b.CurHealth / b.MaxHealth : 0.0;
            return bhp.CompareTo(ahp);
        }

        private bool ShouldShowInTopLeftList(IMonster m)
        {
            if (m == null || !m.IsAlive || m.SnoMonster == null) return false;
            if (m.MaxHealth <= 0) return false;

            // This list can only ever admit blue champions, yellow rares, or their
            // minions. Fail ordinary trash/special actors before the expensive proxy
            // and boss classification checks below.
            if (m.Rarity != ActorRarity.Champion
                && m.Rarity != ActorRarity.Rare
                && m.Rarity != ActorRarity.RareMinion)
            {
                return false;
            }

            if (!ShowInvisible)
            {
                bool listEliteLike =
                    m.Rarity == ActorRarity.Champion ||
                    m.Rarity == ActorRarity.Rare ||
                    m.Rarity == ActorRarity.RareMinion;

                if (!listEliteLike && (m.Invisible || m.Hidden || m.Stealthed)) return false;
            }
            if (HideIllusions && m.Illusion) return false;

            bool attackableListEliteLike =
                m.Rarity == ActorRarity.Champion ||
                m.Rarity == ActorRarity.Rare ||
                m.Rarity == ActorRarity.RareMinion;

            if (RequireAttackable && !attackableListEliteLike && !m.Attackable) return false;

            if (IsOrlashCloneOrBreathMinion(m)) return false;
            if (IsRiftGuardianSpawnedMinion(m)) return false;
            if (IsKnownFalseUniqueSpecial(m)) return false;
            if (ShouldSuppressUnstableSpecialMonster(m)) return false;

            // Top-left list is elite-pack awareness only.
            // Never show RG/boss/keywarden/unique/goblin here.
            if (IsRiftGuardian(m)) return false;
            if (IsBossLikeMonster(m)) return false;
            if (IsKeywarden(m)) return false;
            if (IsUniqueMonster(m)) return false;
            if (IsGoblin(m)) return false;

            if (m.Rarity == ActorRarity.Champion)
                return ShowChampionBars;

            if (m.Rarity == ActorRarity.Rare)
                return ShowRareBars;

            if (m.Rarity == ActorRarity.RareMinion)
                return ShowRareMinionsInTopLeftList && ShowRareMinionBars;

            return false;
        }

        private ChampionFamilyKey GetChampionFamilyKey(IMonster monster)
        {
            if (monster == null)
                return default(ChampionFamilyKey);

            if (monster.SnoActor != null)
                return ChampionFamilyKey.FromActor((uint)monster.SnoActor.Sno);

            if (monster.SnoMonster != null && !string.IsNullOrEmpty(monster.SnoMonster.Code))
                return ChampionFamilyKey.FromCode(monster.SnoMonster.Code);

            return ChampionFamilyKey.FromName(GetShortMonsterName(monster));
        }

        private IBrush GetTopLeftHealthBrush(IMonster monster)
        {
            EliteRenderState state = GetRenderState(monster);
            if (state.IsUnavailable) return TopLeftUnavailableEliteBrush ?? UnavailableEliteBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return TopLeftChampionBrush ?? ChampionBrush;
                case EliteVisualKind.FinalChampion: return TopLeftFinalChampionBrush ?? FinalChampionBrush;
                case EliteVisualKind.RareMinion: return TopLeftRareMinionBrush ?? RareMinionBrush;
                case EliteVisualKind.Juggernaut: return TopLeftJuggernautBrush ?? JuggernautBrush;
                default: return TopLeftRareBrush ?? RareBrush;
            }
        }

        private IBrush GetTopLeftHealthLightBrush(IMonster monster)
        {
            EliteRenderState state = GetRenderState(monster);
            if (state.IsUnavailable) return TopLeftUnavailableEliteLightBrush ?? UnavailableEliteLightBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return TopLeftChampionLightBrush ?? ChampionLightBrush;
                case EliteVisualKind.FinalChampion: return TopLeftFinalChampionLightBrush ?? FinalChampionLightBrush;
                case EliteVisualKind.RareMinion: return TopLeftRareMinionLightBrush ?? RareMinionLightBrush;
                case EliteVisualKind.Juggernaut: return TopLeftJuggernautLightBrush ?? JuggernautLightBrush;
                default: return TopLeftRareLightBrush ?? RareLightBrush;
            }
        }

        private IBrush GetTopLeftHealthShadowBrush(IMonster monster)
        {
            EliteRenderState state = GetRenderState(monster);
            if (state.IsUnavailable) return TopLeftUnavailableEliteShadowBrush ?? UnavailableEliteShadowBrush;

            switch (state.Kind)
            {
                case EliteVisualKind.Champion: return TopLeftChampionShadowBrush ?? ChampionShadowBrush;
                case EliteVisualKind.FinalChampion: return TopLeftFinalChampionShadowBrush ?? FinalChampionShadowBrush;
                case EliteVisualKind.RareMinion: return TopLeftRareMinionShadowBrush ?? RareMinionShadowBrush;
                case EliteVisualKind.Juggernaut: return TopLeftJuggernautShadowBrush ?? JuggernautShadowBrush;
                default: return TopLeftRareShadowBrush ?? RareShadowBrush;
            }
        }

        private void DrawTopLeftHealthFill(IMonster monster, float fillX, float fillY, float fillW, float fillH, float hp)
        {
            float hpW = fillW * hp;
            if (hpW <= 0.0f)
                return;

            var baseBrush = GetTopLeftHealthBrush(monster);
            if (baseBrush != null)
                baseBrush.DrawRectangleGridFit(fillX, fillY, hpW, fillH);

            if (!UseTwoToneBarLighting || fillH < 6.0f)
                return;

            var lightBrush = GetTopLeftHealthLightBrush(monster);
            if (lightBrush != null)
                lightBrush.DrawRectangleGridFit(fillX, fillY, hpW, fillH * 0.45f);

            var shadowBrush = GetTopLeftHealthShadowBrush(monster);
            if (shadowBrush != null)
                shadowBrush.DrawRectangleGridFit(fillX, fillY + fillH * 0.72f, hpW, fillH * 0.28f);
        }

        private void DrawTopLeftSmallEliteBar(IMonster monster, float x, float y, float w, float h)
        {
            if (monster == null || monster.MaxHealth <= 0) return;

            double hp = monster.CurHealth / monster.MaxHealth;
            if (hp < 0.0) hp = 0.0;
            if (hp > 1.0) hp = 1.0;

            var bg = TopLeftListBackgroundBrush ?? BackgroundBrush;
            var border = TopLeftListBorderBrush ?? BorderBrush;
            bg.DrawRectangleGridFit(x, y, w, h);
            border.DrawRectangleGridFit(x - 1.0f, y - 1.0f, w + 2.0f, h + 2.0f);

            if (hp > 0.0)
                DrawTopLeftHealthFill(monster, x, y, w, h, (float)hp);

            if (ShowHpPercentText && TextFont != null && h >= 7.0f)
            {
                int pctValue = (int)Math.Round(hp * 100.0);
                if (pctValue < 0) pctValue = 0;
                if (pctValue > 100) pctValue = 100;
                string pct = _hpPercentText[pctValue];
                float textWidth = _hpPercentTextWidth[pctValue];
                float textHeight = _hpPercentTextHeight[pctValue];

                if (textHeight <= h + 4.0f && textWidth <= w)
                {
                    float tx = x + w * 0.5f - textWidth * 0.5f;
                    float ty = y + h * 0.5f - textHeight * 0.5f;
                    TextFont.DrawText(pct, tx, ty);
                }
            }
        }

        private string GetShortMonsterName(IMonster m)
        {
            if (m == null || m.SnoMonster == null) return "";

            string name = m.SnoMonster.NameLocalized;
            if (string.IsNullOrEmpty(name))
                name = m.SnoMonster.NameEnglish;
            if (string.IsNullOrEmpty(name))
                name = m.SnoMonster.Code;

            if (string.IsNullOrEmpty(name))
                return "";

            const int maxLen = 20;
            if (name.Length > maxLen)
                name = name.Substring(0, maxLen - 1) + "…";

            return name;
        }

        private bool HasMonsterPriority(IMonster monster, MonsterPriority priority)
        {
            try
            {
                return monster != null
                    && monster.SnoMonster != null
                    && monster.SnoMonster.Priority == priority;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGoblin(IMonster monster)
        {
            if (monster == null || monster.SnoMonster == null) return false;

            if (HasMonsterPriority(monster, MonsterPriority.goblin))
                return true;

            // Avoid string scans for ordinary non-elite trash. The priority path above catches
            // normal goblins used by the default FREE monster plugin.
            if (!monster.IsElite
                && monster.Rarity != ActorRarity.Unique
                && monster.Rarity != ActorRarity.Boss)
            {
                return false;
            }

            string code = monster.SnoMonster.Code ?? string.Empty;
            string name = monster.SnoMonster.NameEnglish ?? string.Empty;

            return code.IndexOf("Goblin", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Goblin", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Treasure", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsRiftGuardian(IMonster monster)
        {
            if (monster == null)
                return false;

            // Do not scan strings for regular actors. Rift Guardian bars require a real boss classification.
            if (!IsActualBossMonster(monster))
                return false;

            string code = GetMonsterCode(monster);
            string name = GetMonsterName(monster);

            return code.IndexOf("RiftGuardian", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("rift_guardian", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("lr_boss", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rift Guardian", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsKnownOrlashCloneOrBreathActorSno(ActorSnoEnum sno)
        {
            return sno == ActorSnoEnum._x1_lr_boss_minion_terrordemon_clone_c
                || sno == ActorSnoEnum._x1_lr_boss_terrordemon_a_breathminion;
        }

        private bool HasKnownOrlashCloneOrBreathActorSno(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.SnoActor != null && IsKnownOrlashCloneOrBreathActorSno(monster.SnoActor.Sno))
                    return true;
            }
            catch
            {
            }

            try
            {
                if (monster.SnoMonster != null &&
                    monster.SnoMonster.SnoActor != null &&
                    IsKnownOrlashCloneOrBreathActorSno(monster.SnoMonster.SnoActor.Sno))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsOrlashCloneOrBreathMinion(IMonster monster)
        {
            if (monster == null)
                return false;

            if (HasKnownOrlashCloneOrBreathActorSno(monster))
                return true;

            string code = GetMonsterCode(monster);
            string name = GetMonsterName(monster);

            bool knownOrlashHelperCode =
                code.IndexOf("x1_lr_boss_minion_terrordemon_clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("x1_lr_boss_minion_terrordemon_clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("lr_boss_minion_terrordemon_clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("lr_boss_minion_terrordemon_clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("x1_lr_boss_terrordemon_a_breathminion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("x1_lr_boss_terrordemon_a_breathminion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("lr_boss_terrordemon_a_breathminion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("lr_boss_terrordemon_a_breathminion", StringComparison.OrdinalIgnoreCase) >= 0;

            if (knownOrlashHelperCode)
                return true;

            bool orlashRelated =
                code.IndexOf("Orlash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Orlash", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!orlashRelated)
                return false;

            // Do not suppress the actual Rift Guardian/boss itself here.
            // Exact helper SNOs/codes above are already known non-boss spawned actors.
            if (IsActualBossMonster(monster))
                return false;

            bool cloneOrBreath =
                code.IndexOf("Clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("Breath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Breath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("Lightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Lightning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                code.IndexOf("Illusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Illusion", StringComparison.OrdinalIgnoreCase) >= 0;

            return cloneOrBreath;
        }

        private bool IsRiftGuardianSpawnedMinion(IMonster monster)
        {
            if (monster == null) return false;

            string code = GetMonsterCode(monster);
            string name = GetMonsterName(monster);

            bool riftGuardianRelated = code.IndexOf("lr_boss", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("RiftGuardian", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("rift_guardian", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rift Guardian", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!riftGuardianRelated)
                return false;

            // Any lr_boss/RiftGuardian-related actor that is not the actual boss is treated
            // as a spawned add/helper and must not inherit the purple Rift Guardian bar.
            return !IsActualBossMonster(monster);
        }

        private bool IsKnownFalseUniqueSpecial(IMonster monster)
        {
            if (monster == null) return false;

            string code = GetMonsterCode(monster);
            string name = GetMonsterName(monster);

            if (IsOrlashCloneOrBreathMinion(monster))
                return true;

            if (IsRiftGuardianSpawnedMinion(monster))
                return true;

            if (IsProxyActor(monster))
                return true;

            // Warping Horror / Hulking Phasebeast false-special actors use azmodan bodyguard
            // actor families. Suppress the whole family unless the game reports a real
            // Champion/Rare/RareMinion pack.
            bool isAzmodanBodyguardFamily = IsAzmodanBodyguardWarpingHorror(monster)
                || code.IndexOf("azmodanbodyguard", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("azmodanbodyguard", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("azmodan_bodyguard", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("azmodan_bodyguard", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isWarpingHorrorFamily = isAzmodanBodyguardFamily
                || HasTextPair(code, "Warping", "Horror")
                || HasTextPair(name, "Warping", "Horror")
                || HasTextPair(code, "Hulking", "Phasebeast")
                || HasTextPair(name, "Hulking", "Phasebeast")
                || code.IndexOf("WarpingHorror", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("WarpingHorror", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("Warping_Horror", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Warping_Horror", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("HoodedNightmare", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("HoodedNightmare", StringComparison.OrdinalIgnoreCase) >= 0
                || HasTextPair(code, "Hooded", "Nightmare")
                || HasTextPair(name, "Hooded", "Nightmare");

            if (!isWarpingHorrorFamily)
                return false;

            if (monster.Rarity == ActorRarity.Champion
                || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.RareMinion)
            {
                return false;
            }

            return true;
        }

        private bool IsProxyActor(IMonster monster)
        {
            if (monster == null) return false;

            string code = GetMonsterCode(monster);
            string name = GetMonsterName(monster);

            return code.IndexOf("Generic_Proxy", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Generic_Proxy", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("GenericProxy", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("GenericProxy", StringComparison.OrdinalIgnoreCase) >= 0
                || HasTextPair(code, "Generic", "Proxy")
                || HasTextPair(name, "Generic", "Proxy");
        }

        private bool IsAzmodanBodyguardWarpingHorror(IMonster monster)
        {
            if (monster == null) return false;

            try
            {
                if (monster.SnoActor != null && monster.SnoActor.Sno == ActorSnoEnum._azmodanbodyguard_b)
                    return true;
            }
            catch
            {
                // Fall through to other SNO/code checks.
            }

            try
            {
                if (monster.SnoMonster != null
                    && monster.SnoMonster.SnoActor != null
                    && monster.SnoMonster.SnoActor.Sno == ActorSnoEnum._azmodanbodyguard_b)
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to string checks.
            }

            return false;
        }

        private string GetMonsterCode(IMonster monster)
        {
            if (monster == null) return string.Empty;

            // Important: do not return only the first non-empty code.
            // Some false-unique actors expose a generic SnoMonster.Code while the useful
            // family name is only visible through SnoActor.Code or ActorSnoEnum.ToString().
            string code = string.Empty;

            if (monster.SnoMonster != null)
            {
                if (!string.IsNullOrEmpty(monster.SnoMonster.Code))
                    code += monster.SnoMonster.Code + " ";

                if (monster.SnoMonster.SnoActor != null)
                {
                    if (!string.IsNullOrEmpty(monster.SnoMonster.SnoActor.Code))
                        code += monster.SnoMonster.SnoActor.Code + " ";

                    code += monster.SnoMonster.SnoActor.Sno.ToString() + " ";
                }
            }

            if (monster.SnoActor != null)
            {
                if (!string.IsNullOrEmpty(monster.SnoActor.Code))
                    code += monster.SnoActor.Code + " ";

                code += monster.SnoActor.Sno.ToString() + " ";
            }

            return code;
        }

        private string GetMonsterName(IMonster monster)
        {
            if (monster == null) return string.Empty;

            // Aggregate names from both SNO paths for the same reason as GetMonsterCode().
            string name = string.Empty;

            if (monster.SnoMonster != null)
            {
                if (!string.IsNullOrEmpty(monster.SnoMonster.NameEnglish))
                    name += monster.SnoMonster.NameEnglish + " ";

                if (!string.IsNullOrEmpty(monster.SnoMonster.NameLocalized))
                    name += monster.SnoMonster.NameLocalized + " ";

                if (monster.SnoMonster.SnoActor != null)
                {
                    if (!string.IsNullOrEmpty(monster.SnoMonster.SnoActor.NameEnglish))
                        name += monster.SnoMonster.SnoActor.NameEnglish + " ";

                    if (!string.IsNullOrEmpty(monster.SnoMonster.SnoActor.NameLocalized))
                        name += monster.SnoMonster.SnoActor.NameLocalized + " ";
                }
            }

            if (monster.SnoActor != null)
            {
                if (!string.IsNullOrEmpty(monster.SnoActor.NameEnglish))
                    name += monster.SnoActor.NameEnglish + " ";

                if (!string.IsNullOrEmpty(monster.SnoActor.NameLocalized))
                    name += monster.SnoActor.NameLocalized + " ";
            }

            return name;
        }

        private bool HasTextPair(string text, string first, string second)
        {
            if (string.IsNullOrEmpty(text)) return false;

            return text.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf(second, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ShouldSuppressUnstableSpecialMonster(IMonster monster)
        {
            if (monster == null)
                return false;

            // These categories are no longer suppressed by default.
            // The settings remain available as manual kill switches.
            if (!ShowRiftGuardianBars && IsRiftGuardian(monster))
                return true;

            if (!ShowKeywardenBars && IsKeywarden(monster))
                return true;

            if (!ShowBossBars && IsBossLikeMonster(monster))
                return true;

            return false;
        }

        private bool IsKeywarden(IMonster monster)
        {
            if (monster == null || monster.SnoMonster == null) return false;

            if (monster.SnoMonster.Priority == MonsterPriority.keywarden)
                return true;

            if (!monster.IsElite
                && monster.Rarity != ActorRarity.Unique
                && monster.Rarity != ActorRarity.Boss)
            {
                return false;
            }

            string code = monster.SnoMonster.Code ?? string.Empty;
            string name = monster.SnoMonster.NameEnglish ?? string.Empty;

            return code.IndexOf("Keywarden", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("key_warden", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("KeyWarden", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Keywarden", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Key Warden", StringComparison.OrdinalIgnoreCase) >= 0
                || (code.IndexOf("Infernal", StringComparison.OrdinalIgnoreCase) >= 0
                    && code.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0)
                || (name.IndexOf("Infernal", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsBossLikeMonster(IMonster monster)
        {
            if (monster == null)
                return false;

            // Fail closed before any special/helper checks. This prevents boss-name/proxy text
            // from giving purple bars to spawned regular mobs.
            if (!IsActualBossMonster(monster))
                return false;

            // Keep these separate so their specific toggles and colors still work.
            if (IsRiftGuardian(monster)) return false;
            if (IsKeywarden(monster)) return false;
            if (IsKnownFalseUniqueSpecial(monster)) return false;

            return true;
        }

        private bool IsActualBossMonster(IMonster monster)
        {
            if (monster == null)
                return false;

            if (monster.Rarity == ActorRarity.Boss)
                return true;

            try
            {
                if (monster.SnoMonster != null && monster.SnoMonster.Priority == MonsterPriority.boss)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private bool IsUniqueMonster(IMonster monster)
        {
            if (monster == null) return false;
            if (monster.Rarity != ActorRarity.Unique) return false;
            if (!monster.IsElite) return false;
            if (IsKnownFalseUniqueSpecial(monster)) return false;
            if (IsRiftGuardian(monster)) return false;
            if (IsKeywarden(monster)) return false;
            if (IsBossLikeMonster(monster)) return false;
            return true;
        }

        private bool HasAffix(IMonster monster, MonsterAffix affix)
        {
            if (monster == null || monster.AffixSnoList == null) return false;

            foreach (var snoAffix in monster.AffixSnoList)
            {
                if (snoAffix != null && snoAffix.Affix == affix)
                    return true;
            }

            return false;
        }
    }
}
