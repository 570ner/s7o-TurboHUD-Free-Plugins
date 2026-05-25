using System;
using System.Collections.Generic;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // s7o_DangerousSkillOverlays.cs
    //
    // FreeHUD / TurboHUD standalone actor-SNO overlay plugin.
    //
    // Purpose:
    // Adds extra dangerous affix visuals that FreeHUD's default EliteMonsterSkillPlugin
    // does not expose as public toggleable decorator collections.
    //
    // Phase 1:
    //   - Orbiter projectile circle
    //   - Waller wall rectangle
    //   - Wormhole mine circle + inside warning label
    //   - Poison Enchanted / Corpse Bomber projectile circle + timer
    //
    // Important:
    //   - Do not modify FreeHUD Default/Monsters/EliteMonsterSkillPlugin.cs.
    //   - Do not use LightningMOD Avoidance APIs.
    //   - Do not add HUD MENU rows yet.
    //   - HUD MENU can later reflect the public booleans in this class.
    public class s7o_DangerousSkillOverlays : BasePlugin, IInGameWorldPainter
    {
        // ============================================================
        // Public toggles for future HUD MENU integration
        // ============================================================
        public bool ShowOrbiterProjectiles { get; set; }
        public bool ShowOrbiterFocalPoints { get; set; }
        public bool ShowWallerWalls { get; set; }
        public bool ShowWormholeMines { get; set; }
        public bool ShowPoisonEnchantedProjectiles { get; set; }

        // Boss / Rift Guardian hazard overlays.
        // All boss hazard circles must use native actor radius, not guessed hardcoded radii.
        public bool ShowBossFallingRocks { get; set; }
        public bool ShowBossLeapTelegraphs { get; set; }
        public bool ShowBossMeteors { get; set; }
        public bool ShowBossKulleHazards { get; set; }
        public bool ShowBossBlighterHazards { get; set; }
        public bool ShowBossRatKingHazards { get; set; }
        public bool ShowBossRatKingDomeHazards { get; set; }
        public bool ShowBossRatKingRatBallHazards { get; set; }
        public bool ShowBossAdriaGeysers { get; set; }
        public bool ShowBossButcherFire { get; set; }
        public bool ShowBossRimeHazards { get; set; }

        // Non-elite environmental hazards.
        public bool ShowShockTowerHazards { get; set; }

        // Boss-spawned environmental hazards.
        public bool ShowBossSandmonsterTurretHazards { get; set; }

        // Conservative safety guards.
        public bool RequireInGame { get; set; }
        public bool HideInTown { get; set; }
        public bool HideWhilePaused { get; set; }
        public bool HideWhileLoading { get; set; }

        // Configurable label text.
        // FreeHUD does not expose the same LightningMOD language API,
        // so keep this as a plain public string.
        public string InWormholeText { get; set; }

        // Waller dimensions are world yards.
        // These are the LightningMOD defaults.
        public double WallWidth { get; set; }
        public double WallLength { get; set; }

        // Wormhole inside threshold:
        // WormholeRadius + player.RadiusBottom + WormholeInsideTolerance.
        public float WormholeInsideTolerance { get; set; }

        // ============================================================
        // Decorators / brushes
        // ============================================================
        public IBrush WallBrush { get; set; }
        public IBrush OrbiterRotatingBackBrush { get; set; }
        public IBrush OrbiterRotatingBlueBrush { get; set; }

        public int OrbiterRotatingDashCount { get; set; }
        public float OrbiterRotatingDashFill { get; set; }
        public float OrbiterRotationSecondsPerTurn { get; set; }
        public float OrbiterFocalPointGroundSearchRadius { get; set; }
        public bool OrbiterFocalPointClampToGroundPlane { get; set; }
        public float OrbiterFocalPointElevatedZThreshold { get; set; }


        // Some telegraph/proxy actors expose RadiusBottom = 0 even though F11 sees them.
        // Use native radius when present, otherwise this fallback.
        public float BossHazardMinimumNativeRadius { get; set; }
        public float BossLeapTelegraphFallbackRadius { get; set; }

        public IBrush BossLeapTelegraphBackBrush { get; set; }
        public IBrush BossLeapTelegraphFrontBrush { get; set; }
        public int BossLeapPostImpactLingerTicks { get; set; }
        public IBrush BossLeapPostImpactBackBrush { get; set; }
        public IBrush BossLeapPostImpactFrontBrush { get; set; }
        public float BossMeteorFallbackRadius { get; set; }
        public IBrush BossMeteorBackBrush { get; set; }
        public IBrush BossMeteorFrontBrush { get; set; }
        public int BossMeteorRotatingDashCount { get; set; }
        public float BossMeteorRotatingDashFill { get; set; }
        public float BossMeteorRotationSecondsPerTurn { get; set; }

        public float BossButcherFireFallbackRadius { get; set; }
        public float BossFallingRocksFallbackRadius { get; set; }
        public float BossBlighterCreepMobArmRadius { get; set; }
        public int BossBlighterCreepMobArmLingerTicks { get; set; }
        public IBrush BossBlighterCreepMobArmBackBrush { get; set; }
        public IBrush BossBlighterCreepMobArmFrontBrush { get; set; }
        public IBrush BossButcherFireBackBrush { get; set; }
        public IBrush BossButcherFireFrontBrush { get; set; }
        public int BossButcherFireRotatingDashCount { get; set; }
        public float BossButcherFireRotatingDashFill { get; set; }
        public float BossButcherFireRotationSecondsPerTurn { get; set; }
        public float ShockTowerHazardRadius { get; set; }
        public IBrush ShockTowerHazardBackBrush { get; set; }
        public IBrush ShockTowerHazardFrontBrush { get; set; }
        public int ShockTowerHazardRotatingDashCount { get; set; }
        public float ShockTowerHazardRotatingDashFill { get; set; }
        public float ShockTowerHazardRotationSecondsPerTurn { get; set; }
        public float BossSandmonsterTurretRadius { get; set; }
        public IBrush BossSandmonsterTurretBackBrush { get; set; }
        public IBrush BossSandmonsterTurretFrontBrush { get; set; }
        public int BossSandmonsterTurretRotatingDashCount { get; set; }
        public float BossSandmonsterTurretRotatingDashFill { get; set; }
        public float BossSandmonsterTurretRotationSecondsPerTurn { get; set; }
        public float BossAdriaGeyserRadius { get; set; }
        public IBrush BossAdriaGeyserPendingBackBrush { get; set; }
        public IBrush BossAdriaGeyserPendingFrontBrush { get; set; }
        public IBrush BossAdriaGeyserActiveBackBrush { get; set; }
        public IBrush BossAdriaGeyserActiveFrontBrush { get; set; }
        public int BossAdriaGeyserRotatingDashCount { get; set; }
        public float BossAdriaGeyserRotatingDashFill { get; set; }
        public float BossAdriaGeyserRotationSecondsPerTurn { get; set; }

        public WorldDecoratorCollection OrbiterDecorator { get; set; }
        public WorldDecoratorCollection OrbiterFocalPointDecorator { get; set; }
        public WorldDecoratorCollection WormholeDecorator { get; set; }
        public WorldDecoratorCollection InWormholeDecorator { get; set; }
        public WorldDecoratorCollection PoisonEnchantedDecorator { get; set; }

        public WorldDecoratorCollection BossFallingRocksDecorator { get; set; }
        public WorldDecoratorCollection BossKulleTwisterDecorator { get; set; }
        public WorldDecoratorCollection BossKulleBoulderDecorator { get; set; }
        public WorldDecoratorCollection BossKulleSlowTimeDecorator { get; set; }
        public WorldDecoratorCollection BossBlighterPustuleDecorator { get; set; }
        public WorldDecoratorCollection BossBlighterCreepMobArmDecorator { get; set; }
        public WorldDecoratorCollection BossRatKingThunderdomeDecorator { get; set; }
        public WorldDecoratorCollection BossRatKingWaspRainDecorator { get; set; }
        public WorldDecoratorCollection BossRatKingRatVolcanoDecorator { get; set; }
        public float BossRatKingRatBallRadius { get; set; }
        public float BossRatKingRatBallGroundClampZThreshold { get; set; }
        public int BossRatKingRatBallRotatingDashCount { get; set; }
        public float BossRatKingRatBallRotatingDashFill { get; set; }
        public float BossRatKingRatBallRotationSecondsPerTurn { get; set; }
        public IBrush BossRatKingRatBallBaseBackBrush { get; set; }
        public IBrush BossRatKingRatBallBaseFrontBrush { get; set; }
        public IBrush BossRatKingRatBallBackBrush { get; set; }
        public IBrush BossRatKingRatBallFrontBrush { get; set; }
        public WorldDecoratorCollection BossRimeCold10FootDecorator { get; set; }
        public WorldDecoratorCollection BossRimeCold20FootDecorator { get; set; }
        private readonly HashSet<uint> _paintedRatKingRatBallKeys =
            new HashSet<uint>();

        // Reused during PaintWorld to avoid allocating a new actor list every frame.
        private readonly List<IActor> _paintWorldActorBuffer = new List<IActor>(256);

        private sealed class CachedGroundCircle
        {
            public float X;
            public float Y;
            public float Z;
            public float Radius;
            public int LastSeenTick;
        }

        private readonly Dictionary<uint, CachedGroundCircle> _blighterCreepMobArmCache =
            new Dictionary<uint, CachedGroundCircle>();

        private readonly Dictionary<uint, CachedGroundCircle> _bloodmawLeapImpactCache =
            new Dictionary<uint, CachedGroundCircle>();

        // ============================================================
        // Constants
        // ============================================================
        private const float OrbiterProjectileRadius = 3.0f;
        private const float OrbiterFocalPointRadius = 5.0f;
        private const float WormholeRadius = 3.8f;
        private const float PoisonEnchantedRadius = 3.0f;
        private const float PoisonEnchantedCountdownSeconds = 5.0f;
        private const float OrbiterCountdownSeconds = 15.0f;
        private const double DegreesToRadians = Math.PI / 180.0d;
        private const uint RatKingRatBallMonsterSno = 427171u;
        private const uint RatKingRatBallModelSno = 427100u;
        private const uint RatKingRatBallPreburstSno = 427863u;
        private const uint RatKingRatBallGlowSphereSno = 427872u;
        private const uint RatKingRatBallCastModelSno = 427897u;

        // ============================================================
        // Constructor
        // ============================================================
        public s7o_DangerousSkillOverlays()
        {
            Enabled = true;

            // Draw late enough to appear over most default ground markings.
            Order = 20000;

            ShowOrbiterProjectiles = true;
            ShowOrbiterFocalPoints = true;
            ShowWallerWalls = true;
            ShowWormholeMines = true;
            ShowPoisonEnchantedProjectiles = false;

            ShowBossFallingRocks = true;
            ShowBossLeapTelegraphs = true;
            ShowBossMeteors = true;
            ShowBossKulleHazards = true;
            ShowBossBlighterHazards = true;
            ShowBossRatKingHazards = true;
            // Rat King dome actors are noisy and were being mistaken for the rat-ball cloud.
            // Keep the dome sub-toggle off for this rat-ball pass.
            ShowBossRatKingDomeHazards = false;
            ShowBossRatKingRatBallHazards = true;
            ShowBossAdriaGeysers = true;
            ShowBossButcherFire = true;
            ShowBossRimeHazards = true;
            ShowShockTowerHazards = true;
            ShowBossSandmonsterTurretHazards = true;

            OrbiterRotatingDashCount = 28;
            OrbiterRotatingDashFill = 0.42f;
            OrbiterRotationSecondsPerTurn = 8.0f;
            OrbiterFocalPointGroundSearchRadius = 8.0f;
            OrbiterFocalPointClampToGroundPlane = true;
            OrbiterFocalPointElevatedZThreshold = 1.0f;

            // Bloodmaw leap telegraph fallback if RadiusBottom/RadiusScaled is zero.
            // This is intentionally configurable because telegraph actors often have unreliable native radius.
            BossHazardMinimumNativeRadius = 0.75f;
            BossLeapTelegraphFallbackRadius = 15.0f;
            // Bloodmaw's leap impact remains dangerous briefly after the landing marker ends.
            // 90 ticks is 1.5 seconds at Diablo III's normal 60 ticks/sec rate.
            BossLeapPostImpactLingerTicks = 90;
            BossMeteorFallbackRadius = 22.0f;
            BossMeteorRotatingDashCount = 52;
            BossMeteorRotatingDashFill = 0.42f;
            BossMeteorRotationSecondsPerTurn = 10.0f;

            // Butcher / Man Carver fire actor is named 10foot, but in-game visual alignment
            // tested too large at 15.0f; use a tighter 12-yard trial radius.
            BossButcherFireFallbackRadius = 12.0f;
            // F11/probe-confirmed Perendi/Mallet Demon falling rocks are larger than native radius.
            BossFallingRocksFallbackRadius = 20.0f;
            // F11/probe-confirmed Blighter creepMobArm poison pools.
            BossBlighterCreepMobArmRadius = 10.5f;
            // creepMobArm actors despawn slightly before the damage zone visually ends.
            // Keep drawing the last known circle briefly after the actor disappears.
            BossBlighterCreepMobArmLingerTicks = 60;
            BossButcherFireRotatingDashCount = 48;
            BossButcherFireRotatingDashFill = 0.42f;
            BossButcherFireRotationSecondsPerTurn = 9.0f;
            ShockTowerHazardRadius = 20.0f;
            ShockTowerHazardRotatingDashCount = 56;
            ShockTowerHazardRotatingDashFill = 0.42f;
            ShockTowerHazardRotationSecondsPerTurn = 8.0f;
            BossSandmonsterTurretRadius = 8.0f;
            BossSandmonsterTurretRotatingDashCount = 52;
            BossSandmonsterTurretRotatingDashFill = 0.42f;
            BossSandmonsterTurretRotationSecondsPerTurn = 8.0f;
            BossAdriaGeyserRadius = 10.0f;
            BossAdriaGeyserRotatingDashCount = 44;
            BossAdriaGeyserRotatingDashFill = 0.42f;
            BossAdriaGeyserRotationSecondsPerTurn = 8.0f;

            BossRatKingRatBallRadius = 8.0f;
            BossRatKingRatBallGroundClampZThreshold = 1.0f;
            BossRatKingRatBallRotatingDashCount = 36;
            BossRatKingRatBallRotatingDashFill = 0.28f;
            BossRatKingRatBallRotationSecondsPerTurn = 6.0f;

            RequireInGame = true;
            HideInTown = true;
            HideWhilePaused = true;
            HideWhileLoading = true;

            InWormholeText = "In Wormhole";

            WallWidth = 1.5d;
            WallLength = 18.0d;
            WormholeInsideTolerance = 0.0f;
        }

        // ============================================================
        // Load
        // ============================================================
        public override void Load(IController hud)
        {
            base.Load(hud);

            WallBrush = Hud.Render.CreateBrush(255, 170, 80, 40, 5);

            // Solid brushes used for manually segmented rotating Orbiter circles.
            // Do not use DashStyle here; the manual segments are the dashes.
            OrbiterRotatingBackBrush = Hud.Render.CreateBrush(235, 0, 0, 0, 5);
            OrbiterRotatingBlueBrush = Hud.Render.CreateBrush(250, 5, 95, 255, 3);

            BossLeapTelegraphBackBrush = Hud.Render.CreateBrush(
                230, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash);

            BossLeapTelegraphFrontBrush = Hud.Render.CreateBrush(
                245, 255, 45, 45, 3, SharpDX.Direct2D1.DashStyle.Dash);

            BossLeapPostImpactBackBrush = Hud.Render.CreateBrush(
                230, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash);

            BossLeapPostImpactFrontBrush = Hud.Render.CreateBrush(
                245, 255, 135, 20, 3, SharpDX.Direct2D1.DashStyle.Dash);

            BossMeteorBackBrush = Hud.Render.CreateBrush(
                230, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash);

            BossMeteorFrontBrush = Hud.Render.CreateBrush(
                245, 255, 95, 35, 3, SharpDX.Direct2D1.DashStyle.Dash);

            BossButcherFireBackBrush = Hud.Render.CreateBrush(
                230, 0, 0, 0, 5);

            BossButcherFireFrontBrush = Hud.Render.CreateBrush(
                245, 255, 55, 20, 3);

            ShockTowerHazardBackBrush = Hud.Render.CreateBrush(
                245, 0, 0, 0, 5);

            ShockTowerHazardFrontBrush = Hud.Render.CreateBrush(
                245, 255, 255, 255, 3);

            BossSandmonsterTurretBackBrush = Hud.Render.CreateBrush(
                245, 0, 0, 0, 5);

            BossSandmonsterTurretFrontBrush = Hud.Render.CreateBrush(
                245, 255, 35, 35, 3);

            BossAdriaGeyserPendingBackBrush = Hud.Render.CreateBrush(
                245, 0, 0, 0, 5);

            BossAdriaGeyserPendingFrontBrush = Hud.Render.CreateBrush(
                245, 255, 145, 20, 3);

            BossAdriaGeyserActiveBackBrush = Hud.Render.CreateBrush(
                245, 0, 0, 0, 5);

            BossAdriaGeyserActiveFrontBrush = Hud.Render.CreateBrush(
                245, 255, 45, 35, 3);

            WormholeDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = WormholeRadius,
                    Brush = Hud.Render.CreateBrush(
                        200,
                        255,
                        0,
                        255,
                        5,
                        SharpDX.Direct2D1.DashStyle.Dash
                    )
                }
            );

            InWormholeDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = WormholeRadius,
                    Brush = Hud.Render.CreateBrush(
                        200,
                        255,
                        0,
                        255,
                        5,
                        SharpDX.Direct2D1.DashStyle.Dash
                    )
                },
                new GroundLabelDecorator(Hud)
                {
                    BackgroundBrush = Hud.Render.CreateBrush(160, 255, 255, 255, 0),
                    TextFont = Hud.Render.CreateFont(
                        "tahoma",
                        9,
                        255,
                        255,
                        0,
                        255,
                        true,
                        false,
                        false
                    )
                }
            );

            PoisonEnchantedDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = PoisonEnchantedRadius,
                    Brush = Hud.Render.CreateBrush(
                        200,
                        117,
                        217,
                        117,
                        2,
                        SharpDX.Direct2D1.DashStyle.Dash
                    )
                },
                new GroundLabelDecorator(Hud)
                {
                    CountDownFrom = PoisonEnchantedCountdownSeconds,
                    TextFont = Hud.Render.CreateFont(
                        "tahoma",
                        7,
                        255,
                        117,
                        217,
                        117,
                        true,
                        false,
                        255,
                        0,
                        0,
                        0,
                        true
                    )
                },
                new GroundTimerDecorator(Hud)
                {
                    CountDownFrom = PoisonEnchantedCountdownSeconds,
                    BackgroundBrushEmpty = Hud.Render.CreateBrush(128, 0, 0, 0, 0),
                    BackgroundBrushFill = Hud.Render.CreateBrush(160, 117, 217, 117, 0),
                    Radius = 15
                }
            );

            OrbiterDecorator = new WorldDecoratorCollection(
                new GroundLabelDecorator(Hud)
                {
                    CountDownFrom = OrbiterCountdownSeconds,
                    TextFont = Hud.Render.CreateFont(
                        "tahoma",
                        7,
                        255,
                        53,
                        146,
                        255,
                        true,
                        false,
                        255,
                        0,
                        0,
                        0,
                        true
                    )
                },
                new GroundTimerDecorator(Hud)
                {
                    CountDownFrom = OrbiterCountdownSeconds,
                    BackgroundBrushEmpty = Hud.Render.CreateBrush(128, 0, 0, 0, 0),
                    BackgroundBrushFill = Hud.Render.CreateBrush(190, 20, 120, 255, 0),
                    Radius = 15
                }
            );

            // Focal point is drawn manually with the same rotating segmented style as small Orbiters.
            // Prefer the native projectile_focus actor's FloorCoordinate to avoid screen-space drift.
            OrbiterFocalPointDecorator = new WorldDecoratorCollection();

            BossFallingRocksDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = BossFallingRocksFallbackRadius,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = BossFallingRocksFallbackRadius,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 255, 90, 40, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );
            BossKulleTwisterDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 185, 80, 255, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossKulleBoulderDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(240, 255, 95, 35, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossKulleSlowTimeDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(200, 0, 0, 0, 4, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(210, 80, 170, 255, 2, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossBlighterPustuleDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 80, 255, 80, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossBlighterCreepMobArmBackBrush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash);
            BossBlighterCreepMobArmFrontBrush = Hud.Render.CreateBrush(235, 80, 255, 80, 3, SharpDX.Direct2D1.DashStyle.Dash);

            BossBlighterCreepMobArmDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = BossBlighterCreepMobArmRadius,
                    HasShadow = false,
                    Brush = BossBlighterCreepMobArmBackBrush
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = BossBlighterCreepMobArmRadius,
                    HasShadow = false,
                    Brush = BossBlighterCreepMobArmFrontBrush
                }
            );

            BossRatKingThunderdomeDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 255, 80, 255, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossRatKingWaspRainDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 255, 190, 40, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossRatKingRatVolcanoDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = -1,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(235, 255, 70, 70, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            // Rat-ball cloud is a small moving monster body; draw a static fixed-radius ring first
            // so the overlay is visible even if the rotating segmented line blends into the swarm.
            BossRatKingRatBallBaseBackBrush = Hud.Render.CreateBrush(245, 0, 0, 0, 7);
            BossRatKingRatBallBaseFrontBrush = Hud.Render.CreateBrush(245, 0, 255, 80, 4);
            BossRatKingRatBallBackBrush = Hud.Render.CreateBrush(255, 0, 0, 0, 7, SharpDX.Direct2D1.DashStyle.Dot);
            BossRatKingRatBallFrontBrush = Hud.Render.CreateBrush(255, 0, 255, 80, 5, SharpDX.Direct2D1.DashStyle.Dot);

            // Rime cold AOE circles: probe-confirmed actors expose no useful native radius.
            // The 10foot / 20foot suffixes are treated as requested 10 / 20 yard danger radii.
            BossRimeCold10FootDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = 10.0f,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = 10.0f,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(240, 90, 220, 255, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );

            BossRimeCold20FootDecorator = new WorldDecoratorCollection(
                new GroundCircleDecorator(Hud)
                {
                    Radius = 20.0f,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(220, 0, 0, 0, 5, SharpDX.Direct2D1.DashStyle.Dash)
                },
                new GroundCircleDecorator(Hud)
                {
                    Radius = 20.0f,
                    HasShadow = false,
                    Brush = Hud.Render.CreateBrush(245, 35, 170, 255, 3, SharpDX.Direct2D1.DashStyle.Dash)
                }
            );
        }

        // ============================================================
        // PaintWorld
        // ============================================================
        public void PaintWorld(WorldLayer layer)
        {
            _paintWorldActorBuffer.Clear();

            if (layer != WorldLayer.Ground)
                return;

            if (!CanPaint())
                return;

            var actors = Hud.Game.Actors;
            if (actors == null)
                return;

            _paintedRatKingRatBallKeys.Clear();

            // Boss special-attack actors are often shared by trash/elites using the
            // same monster family.  Build a lightweight Rift Guardian context first,
            // then draw boss overlays only while the matching guardian is actually
            // present in the same actor pass.
            var actorList = _paintWorldActorBuffer;
            bool isPerendiRiftGuardianPresent = false;
            bool isBloodmawRiftGuardianPresent = false;
            bool isEmberRiftGuardianPresent = false;
            bool isSandShaperRiftGuardianPresent = false;
            bool isBlighterRiftGuardianPresent = false;
            bool isRatKingRiftGuardianPresent = false;
            bool isAdriaOrTethrysBossPresent = false;
            bool isRimeRiftGuardianPresent = false;
            bool isFireDotBossPresent = false;

            foreach (var actor in actors)
            {
                if (!IsValidActor(actor))
                    continue;

                actorList.Add(actor);

                switch (actor.SnoActor.Sno)
                {
                    case ActorSnoEnum._x1_lr_boss_malletdemon:
                    case ActorSnoEnum._p73_lr_boss_malletdemon:
                    case ActorSnoEnum._p76_lr_boss_malletdemon:
                        isPerendiRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_westmarchbrute:
                    case ActorSnoEnum._p73_lr_boss_westmarchbrute:
                    case ActorSnoEnum._p76_lr_boss_westmarchbrute:
                        isBloodmawRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_morluspellcaster_fire:
                        isEmberRiftGuardianPresent = true;
                        isFireDotBossPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_zoltunkulle:
                    case ActorSnoEnum._x1_lr_boss_sandmonster:
                    case ActorSnoEnum._p73_lr_boss_sandmonster:
                    case ActorSnoEnum._p76_lr_boss_sandmonster:
                        isSandShaperRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_creepmob_a:
                    case ActorSnoEnum._p1_lr_bogblight_a:
                        isBlighterRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_ratking_a:
                        isRatKingRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_adria_boss:
                    case ActorSnoEnum._x1_lr_boss_succubus_a:
                    case ActorSnoEnum._p73_lr_boss_succubus_a:
                    case ActorSnoEnum._p76_lr_boss_succubus_a:
                        isAdriaOrTethrysBossPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_morluspellcaster_ice:
                        isRimeRiftGuardianPresent = true;
                        break;

                    case ActorSnoEnum._x1_lr_boss_butcher:
                    case ActorSnoEnum._p73_lr_boss_butcher:
                    case ActorSnoEnum._p76_lr_boss_butcher:
                    case ActorSnoEnum._x1_lr_boss_demonflyermega:
                        isFireDotBossPresent = true;
                        break;
                }
            }

            foreach (var actor in actorList)
            {
                switch (actor.SnoActor.Sno)
                {
                    case ActorSnoEnum._x1_monsteraffix_orbiter_projectile:
                    case ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile:
                        if (ShowOrbiterProjectiles)
                            PaintOrbiter(layer, actor);
                        break;

                    case ActorSnoEnum._x1_monsteraffix_orbiter_focalpoint:
                    case ActorSnoEnum._x1_monsteraffix_avenger_orbiter_focalpoint:
                        if (ShowOrbiterFocalPoints)
                            PaintOrbiterFocalPoint(layer, actor);
                        break;

                    case ActorSnoEnum._x1_monsteraffix_orbiter_projectile_focus:
                    case ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile_focus:
                    case ActorSnoEnum._x1_monsteraffix_orbiter_glowsphere:
                        // Candidate source actors are logged by the throttled general actor probe.
                        // Do not draw here; the focalpoint actor chooses the best source nearby.
                        break;

                    case ActorSnoEnum._monsteraffix_waller_model:
                        if (ShowWallerWalls)
                            PaintWaller(actor);
                        break;

                    case ActorSnoEnum._x1_monsteraffix_teleportmines:
                        if (ShowWormholeMines)
                            PaintWormhole(layer, actor);
                        break;

                    case ActorSnoEnum._x1_monsteraffix_corpsebomber_projectile:
                        if (ShowPoisonEnchantedProjectiles)
                            PaintPoisonEnchanted(layer, actor);
                        break;

                    case ActorSnoEnum._x1_lr_boss_malletdemon_fallingrocks:
                    case ActorSnoEnum._a2dun_zolt_random_fallingrocks_c:
                        if (ShowBossFallingRocks && (isPerendiRiftGuardianPresent || isSandShaperRiftGuardianPresent))
                            PaintBossHazard(layer, actor, BossFallingRocksDecorator);
                        break;

                    case ActorSnoEnum._x1_westmarchbrute_leap_telegraph:
                    case ActorSnoEnum._x1_westmarchbrute_b_leap_telegraph:
                    case ActorSnoEnum._p2_westmarchbrute_leap_telegraph:
                        if (ShowBossLeapTelegraphs && isBloodmawRiftGuardianPresent)
                            PaintBossLeapTelegraph(actor);
                        break;

                    case ActorSnoEnum._morluspellcaster_meteor_pending:
                    case ActorSnoEnum._morluspellcast_meteor_castsphere:
                    case ActorSnoEnum._morluspellcaster_meteor_model:
                    case ActorSnoEnum._morluspellcaster_meteor_impact:
                        if (ShowBossMeteors && isEmberRiftGuardianPresent)
                            PaintBossMeteor(actor);
                        break;

                    case ActorSnoEnum._zoltunkulle_energytwister:
                        if (ShowBossKulleHazards && isSandShaperRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossKulleTwisterDecorator);
                        break;

                    case ActorSnoEnum._zoltunkulle_fieryboulder_model:
                    case ActorSnoEnum._zoltunkulle_fieryboulder_projectile:
                    case ActorSnoEnum._zoltunkulle_fieryboulder_groundimpact:
                        if (ShowBossKulleHazards && isSandShaperRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossKulleBoulderDecorator);
                        break;

                    case ActorSnoEnum._zoltunkulle_slowtime_bubble:
                    case ActorSnoEnum._zoltunkulle_slowtime_shield_dome:
                        if (ShowBossKulleHazards && isSandShaperRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossKulleSlowTimeDecorator);
                        break;

                    case ActorSnoEnum._creepmobarm:
                        if (ShowBossBlighterHazards && isBlighterRiftGuardianPresent)
                            PaintBossBlighterCreepMobArm(actor);
                        break;

                    case ActorSnoEnum._x1_bogblight_pustulespawn_proxy:
                    case ActorSnoEnum._x1_bogblight_pustule_model:
                    case ActorSnoEnum._x1_bogblight_pustule_model_fade:
                        if (ShowBossBlighterHazards && isBlighterRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossBlighterPustuleDecorator);
                        break;

                    case ActorSnoEnum._p4_ratking_ratballmonster:
                        // The probe confirms this actor is the Rat Swarm, but only draw it
                        // while Hamelin is active so rat-family trash cannot trigger it.
                        if (ShowBossRatKingRatBallHazards && isRatKingRiftGuardianPresent)
                            PaintBossRatKingRatBall(actor);
                        break;

                    case ActorSnoEnum._p4_ratking_thunderdome_proxyactor:
                    case ActorSnoEnum._p4_ratking_thunderdome_ringgeo:
                    case ActorSnoEnum._p4_ratking_thunderdomewall:
                        if (ShowBossRatKingHazards && ShowBossRatKingDomeHazards && isRatKingRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossRatKingThunderdomeDecorator);
                        break;

                    case ActorSnoEnum._p4_ratking_wasprain_impact:
                        if (ShowBossRatKingHazards && isRatKingRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossRatKingWaspRainDecorator);
                        break;

                    case ActorSnoEnum._x1_lr_boss_ratking_ratvolcano_a:
                        if (ShowBossRatKingHazards && isRatKingRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossRatKingRatVolcanoDecorator);
                        break;

                    case ActorSnoEnum._x1_adria_geyser_pending:
                        if (ShowBossAdriaGeysers && isAdriaOrTethrysBossPresent)
                            PaintBossAdriaGeyser(actor, false);
                        break;

                    case ActorSnoEnum._x1_adria_geyser:
                        if (ShowBossAdriaGeysers && isAdriaOrTethrysBossPresent)
                            PaintBossAdriaGeyser(actor, true);
                        break;

                    case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_cold_10foot:
                        if (ShowBossRimeHazards && isRimeRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossRimeCold10FootDecorator);
                        break;

                    case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_cold_20foot:
                        if (ShowBossRimeHazards && isRimeRiftGuardianPresent)
                            PaintBossHazard(layer, actor, BossRimeCold20FootDecorator);
                        break;

                    case ActorSnoEnum._x1_unique_monster_generic_aoe_dot_fire_10foot:
                        if (ShowBossButcherFire && isFireDotBossPresent)
                            PaintBossButcherFire(actor);
                        break;

                    case ActorSnoEnum._x1_pand_ext_ordnance_tower_shock_a:
                        if (ShowShockTowerHazards)
                            PaintShockTowerHazard(actor);
                        break;

                    case ActorSnoEnum._p4_lr_boss_sandmonster_turret:
                        if (ShowBossSandmonsterTurretHazards)
                            PaintBossSandmonsterTurretHazard(actor);
                        break;
                }
            }

            if (ShowBossRatKingRatBallHazards && isRatKingRiftGuardianPresent)
                PaintBossRatKingRatBallMonsters();

            if (ShowBossBlighterHazards && isBlighterRiftGuardianPresent)
                PaintCachedBlighterCreepMobArmCircles();
            else
                _blighterCreepMobArmCache.Clear();

            if (ShowBossLeapTelegraphs && isBloodmawRiftGuardianPresent)
                PaintCachedBloodmawLeapImpactCircles();
            else
                _bloodmawLeapImpactCache.Clear();
        }

        // ============================================================
        // Paint helpers
        // ============================================================
        private void PaintOrbiter(WorldLayer layer, IActor actor)
        {
            if (actor == null || actor.FloorCoordinate == null)
                return;

            PaintRotatingOrbiterCircle(actor.FloorCoordinate, OrbiterProjectileRadius);

            if (OrbiterDecorator != null)
                OrbiterDecorator.Paint(layer, actor, actor.FloorCoordinate, string.Empty);
        }

        private void PaintOrbiterFocalPoint(WorldLayer layer, IActor actor)
        {
            if (actor == null || actor.FloorCoordinate == null)
                return;

            IActor source = FindBestOrbiterFocalGroundSource(actor);
            IActor drawActor = source ?? actor;

            if (drawActor == null || drawActor.FloorCoordinate == null)
                return;

            IWorldCoordinate rawCoord = drawActor.FloorCoordinate;
            IWorldCoordinate drawCoord = CreateOrbiterFocalGroundPlaneCoordinate(drawActor, rawCoord);

            if (drawCoord == null)
                drawCoord = rawCoord;

            PaintRotatingOrbiterCircle(drawCoord, OrbiterFocalPointRadius);

            if (OrbiterFocalPointDecorator != null)
                OrbiterFocalPointDecorator.Paint(layer, actor, drawCoord, string.Empty);
        }

        private IWorldCoordinate CreateOrbiterFocalGroundPlaneCoordinate(IActor actor, IWorldCoordinate rawCoord)
        {
            if (rawCoord == null)
                return null;

            if (!OrbiterFocalPointClampToGroundPlane)
                return rawCoord;

            float groundZ = EstimateOrbiterFocalGroundZ(actor, rawCoord);

            try
            {
                return Hud.Window.CreateWorldCoordinate((float)rawCoord.X, (float)rawCoord.Y, groundZ);
            }
            catch
            {
                return rawCoord;
            }
        }

        private float EstimateOrbiterFocalGroundZ(IActor actor, IWorldCoordinate rawCoord)
        {
            if (rawCoord == null)
                return 0.0f;

            float rawZ = (float)rawCoord.Z;

            try
            {
                if (actor != null)
                {
                    float zDistance = (float)actor.ZDistanceToMeAbsolute;

                    if (zDistance > OrbiterFocalPointElevatedZThreshold)
                    {
                        float corrected = rawZ - zDistance;

                        if (!float.IsNaN(corrected) && !float.IsInfinity(corrected))
                            return corrected;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (Hud.Game != null &&
                    Hud.Game.Me != null &&
                    Hud.Game.Me.FloorCoordinate != null)
                {
                    float meZ = (float)Hud.Game.Me.FloorCoordinate.Z;

                    if (Math.Abs(rawZ - meZ) > OrbiterFocalPointElevatedZThreshold)
                        return meZ;
                }
            }
            catch
            {
            }

            return rawZ;
        }

        private bool IsOrbiterFocalGroundSourceSno(ActorSnoEnum sno)
        {
            return sno == ActorSnoEnum._x1_monsteraffix_orbiter_glowsphere ||
                   sno == ActorSnoEnum._x1_monsteraffix_orbiter_projectile_focus ||
                   sno == ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile_focus;
        }

        private int GetOrbiterFocalGroundSourcePriority(ActorSnoEnum sno)
        {
            if (sno == ActorSnoEnum._x1_monsteraffix_orbiter_glowsphere)
                return 100;

            if (sno == ActorSnoEnum._x1_monsteraffix_orbiter_projectile_focus ||
                sno == ActorSnoEnum._x1_monsteraffix_avenger_orbiter_projectile_focus)
                return 80;

            return 0;
        }

        private IActor FindBestOrbiterFocalGroundSource(IActor focalActor)
        {
            if (focalActor == null || focalActor.FloorCoordinate == null)
                return null;

            IActor best = null;
            int bestPriority = 0;
            float bestDistance = float.MaxValue;

            try
            {
                if (Hud.Game == null || Hud.Game.Actors == null)
                    return null;

                foreach (var a in Hud.Game.Actors)
                {
                    if (a == null || a.SnoActor == null || a.FloorCoordinate == null)
                        continue;

                    ActorSnoEnum sno = a.SnoActor.Sno;

                    if (!IsOrbiterFocalGroundSourceSno(sno))
                        continue;

                    float distance = focalActor.FloorCoordinate.XYDistanceTo(a.FloorCoordinate);

                    if (distance > OrbiterFocalPointGroundSearchRadius)
                        continue;

                    int priority = GetOrbiterFocalGroundSourcePriority(sno);

                    if (priority > bestPriority ||
                        (priority == bestPriority && distance < bestDistance))
                    {
                        best = a;
                        bestPriority = priority;
                        bestDistance = distance;
                    }
                }
            }
            catch
            {
                return best;
            }

            return best;
        }

        private void PaintRotatingOrbiterCircle(IWorldCoordinate center, float radius)
        {
            if (center == null)
                return;

            if (radius <= 0.0f)
                return;

            if (OrbiterRotatingBackBrush == null || OrbiterRotatingBlueBrush == null)
                return;

            int dashCount = Math.Max(8, OrbiterRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, OrbiterRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, OrbiterRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(center, radius, dashCount, dashFill, phase, OrbiterRotatingBackBrush);
            DrawRotatingDashedCircle(center, radius, dashCount, dashFill, phase, OrbiterRotatingBlueBrush);
        }

        private void DrawRotatingDashedCircle(
            IWorldCoordinate center,
            float radius,
            int dashCount,
            float dashFill,
            double phase,
            IBrush brush)
        {
            if (center == null || brush == null)
                return;

            double full = Math.PI * 2.0d;
            double step = full / dashCount;
            double dashAngle = step * dashFill;

            for (int i = 0; i < dashCount; i++)
            {
                double a0 = phase + i * step;
                double a1 = a0 + dashAngle;

                float x0 = radius * (float)Math.Cos(a0);
                float y0 = radius * (float)Math.Sin(a0);
                float x1 = radius * (float)Math.Cos(a1);
                float y1 = radius * (float)Math.Sin(a1);

                var p0 = center.Offset(x0, y0, 0);
                var p1 = center.Offset(x1, y1, 0);

                brush.DrawLineWorld(p0, p1);
            }
        }

        private void PaintPoisonEnchanted(WorldLayer layer, IActor actor)
        {
            if (PoisonEnchantedDecorator == null)
                return;

            PoisonEnchantedDecorator.Paint(layer, actor, actor.FloorCoordinate, string.Empty);
        }

        private void PaintBossHazard(WorldLayer layer, IActor actor, WorldDecoratorCollection decorator)
        {
            if (decorator == null)
                return;
            decorator.Paint(layer, actor, actor.FloorCoordinate, string.Empty);
        }

        private void PaintBossLeapTelegraph(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            float radius = Math.Max(
                BossLeapTelegraphFallbackRadius,
                GetEffectiveBossHazardRadius(actor, BossLeapTelegraphFallbackRadius));
            if (radius <= 0.0f)
                return;

            CacheBloodmawLeapImpact(actor, coord, radius);
            DrawBloodmawLeapTelegraphCircle(coord, radius);
        }

        private void DrawBloodmawLeapTelegraphCircle(IWorldCoordinate coord, float radius)
        {
            if (coord == null || radius <= 0.0f)
                return;

            if (BossLeapTelegraphBackBrush != null)
                BossLeapTelegraphBackBrush.DrawWorldEllipse(radius, -1, coord);

            if (BossLeapTelegraphFrontBrush != null)
                BossLeapTelegraphFrontBrush.DrawWorldEllipse(radius, -1, coord);
        }

        private void DrawBloodmawLeapImpactCircle(IWorldCoordinate coord, float radius)
        {
            if (coord == null || radius <= 0.0f)
                return;

            if (BossLeapPostImpactBackBrush != null)
                BossLeapPostImpactBackBrush.DrawWorldEllipse(radius, -1, coord);

            if (BossLeapPostImpactFrontBrush != null)
                BossLeapPostImpactFrontBrush.DrawWorldEllipse(radius, -1, coord);
        }

        private void CacheBloodmawLeapImpact(IActor actor, IWorldCoordinate coord, float radius)
        {
            if (actor == null || coord == null || Hud == null || Hud.Game == null)
                return;

            uint key = GetActorCacheKey(actor);
            if (key == 0)
                return;

            CachedGroundCircle entry;
            if (!_bloodmawLeapImpactCache.TryGetValue(key, out entry))
            {
                entry = new CachedGroundCircle();
                _bloodmawLeapImpactCache[key] = entry;
            }

            try
            {
                entry.X = (float)coord.X;
                entry.Y = (float)coord.Y;
                entry.Z = (float)coord.Z;
                entry.Radius = radius;
                entry.LastSeenTick = Hud.Game.CurrentGameTick;
            }
            catch
            {
                _bloodmawLeapImpactCache.Remove(key);
            }
        }

        private void PaintCachedBloodmawLeapImpactCircles()
        {
            if (_bloodmawLeapImpactCache.Count == 0 || Hud == null || Hud.Game == null || Hud.Window == null)
                return;

            int tick = Hud.Game.CurrentGameTick;
            int lingerTicks = Math.Max(0, BossLeapPostImpactLingerTicks);
            List<uint> remove = null;

            foreach (var pair in _bloodmawLeapImpactCache)
            {
                var entry = pair.Value;
                if (entry == null)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                int age = tick - entry.LastSeenTick;
                if (age < 0)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                if (age == 0)
                    continue;

                if (age > lingerTicks)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                try
                {
                    IWorldCoordinate coord = Hud.Window.CreateWorldCoordinate(entry.X, entry.Y, entry.Z);
                    float radius = entry.Radius > 0.0f ? entry.Radius : BossLeapTelegraphFallbackRadius;
                    DrawBloodmawLeapImpactCircle(coord, radius);
                }
                catch
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                }
            }

            if (remove == null)
                return;

            foreach (uint key in remove)
                _bloodmawLeapImpactCache.Remove(key);
        }

        private void PaintBossMeteor(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            float radius = Math.Max(
                BossMeteorFallbackRadius,
                GetEffectiveBossHazardRadius(actor, BossMeteorFallbackRadius));
            if (radius <= 0.0f)
                return;

            int dashCount = Math.Max(12, BossMeteorRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, BossMeteorRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, BossMeteorRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossMeteorBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossMeteorFrontBrush);
        }

        private void PaintBossButcherFire(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            // The confirmed Butcher fire actor exposes RadiusBottom/RadiusScaled = 0,
            // so force the visual fallback radius instead of trusting native radius.
            float radius = Math.Max(0.0f, BossButcherFireFallbackRadius);
            if (radius <= 0.0f)
                return;

            int dashCount = Math.Max(12, BossButcherFireRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, BossButcherFireRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, BossButcherFireRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossButcherFireBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossButcherFireFrontBrush);
        }

        private void PaintShockTowerHazard(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            float radius = Math.Max(0.0f, ShockTowerHazardRadius);
            if (radius <= 0.0f)
                return;

            int dashCount = Math.Max(12, ShockTowerHazardRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, ShockTowerHazardRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, ShockTowerHazardRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, ShockTowerHazardBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, ShockTowerHazardFrontBrush);
        }

        private void PaintBossSandmonsterTurretHazard(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            float radius = Math.Max(0.0f, BossSandmonsterTurretRadius);
            if (radius <= 0.0f)
                return;

            int dashCount = Math.Max(12, BossSandmonsterTurretRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, BossSandmonsterTurretRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, BossSandmonsterTurretRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossSandmonsterTurretBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossSandmonsterTurretFrontBrush);
        }

        private void PaintBossAdriaGeyser(IActor actor, bool active)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            float radius = Math.Max(0.0f, BossAdriaGeyserRadius);
            if (radius <= 0.0f)
                return;

            int dashCount = Math.Max(12, BossAdriaGeyserRotatingDashCount);
            float dashFill = Math.Max(0.10f, Math.Min(0.90f, BossAdriaGeyserRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, BossAdriaGeyserRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            IBrush backBrush = active
                ? BossAdriaGeyserActiveBackBrush
                : BossAdriaGeyserPendingBackBrush;

            IBrush frontBrush = active
                ? BossAdriaGeyserActiveFrontBrush
                : BossAdriaGeyserPendingFrontBrush;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, backBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, frontBrush);
        }


        private void PaintBossBlighterCreepMobArm(IActor actor)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);
            if (coord == null)
                return;

            CacheBlighterCreepMobArm(actor, coord);
            DrawBlighterCreepMobArmCircle(coord);
        }

        private void DrawBlighterCreepMobArmCircle(IWorldCoordinate coord)
        {
            if (coord == null)
                return;

            float radius = Math.Max(0.0f, BossBlighterCreepMobArmRadius);
            if (radius <= 0.0f)
                return;

            if (BossBlighterCreepMobArmBackBrush != null)
                BossBlighterCreepMobArmBackBrush.DrawWorldEllipse(radius, -1, coord);

            if (BossBlighterCreepMobArmFrontBrush != null)
                BossBlighterCreepMobArmFrontBrush.DrawWorldEllipse(radius, -1, coord);
        }

        private void CacheBlighterCreepMobArm(IActor actor, IWorldCoordinate coord)
        {
            if (actor == null || coord == null || Hud == null || Hud.Game == null)
                return;

            uint key = GetActorCacheKey(actor);
            if (key == 0)
                return;

            CachedGroundCircle entry;
            if (!_blighterCreepMobArmCache.TryGetValue(key, out entry))
            {
                entry = new CachedGroundCircle();
                _blighterCreepMobArmCache[key] = entry;
            }

            try
            {
                entry.X = (float)coord.X;
                entry.Y = (float)coord.Y;
                entry.Z = (float)coord.Z;
                entry.LastSeenTick = Hud.Game.CurrentGameTick;
            }
            catch
            {
                _blighterCreepMobArmCache.Remove(key);
            }
        }

        private void PaintCachedBlighterCreepMobArmCircles()
        {
            if (_blighterCreepMobArmCache.Count == 0 || Hud == null || Hud.Game == null || Hud.Window == null)
                return;

            int tick = Hud.Game.CurrentGameTick;
            int lingerTicks = Math.Max(0, BossBlighterCreepMobArmLingerTicks);
            List<uint> remove = null;

            foreach (var pair in _blighterCreepMobArmCache)
            {
                var entry = pair.Value;
                if (entry == null)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                int age = tick - entry.LastSeenTick;
                if (age < 0)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                if (age == 0)
                    continue;

                if (age > lingerTicks)
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                    continue;
                }

                try
                {
                    IWorldCoordinate coord = Hud.Window.CreateWorldCoordinate(entry.X, entry.Y, entry.Z);
                    DrawBlighterCreepMobArmCircle(coord);
                }
                catch
                {
                    if (remove == null)
                        remove = new List<uint>();
                    remove.Add(pair.Key);
                }
            }

            if (remove == null)
                return;

            foreach (uint key in remove)
                _blighterCreepMobArmCache.Remove(key);
        }

        private uint GetActorCacheKey(IActor actor)
        {
            if (actor == null)
                return 0;

            try
            {
                if (actor.AnnId != 0)
                    return actor.AnnId;
            }
            catch
            {
            }

            try
            {
                if (actor.AcdId != 0)
                    return actor.AcdId;
            }
            catch
            {
            }

            return 0;
        }

        private void PaintBossRatKingRatBall(IActor actor)
        {
            if (actor == null)
                return;

            PaintBossRatKingRatBallCore(actor);
        }

        private void PaintBossRatKingRatBallMonsters()
        {
            try
            {
                if (Hud.Game == null || Hud.Game.Monsters == null)
                    return;

                foreach (var monster in Hud.Game.Monsters)
                {
                    if (!IsRatKingRatBallMonster(monster))
                        continue;

                    PaintBossRatKingRatBallCore(monster);
                }
            }
            catch
            {
            }
        }

        private bool IsRatKingRatBallMonster(IMonster monster)
        {
            if (monster == null || monster.SnoMonster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;
            }
            catch
            {
            }

            try
            {
                if (monster.SnoMonster.Sno == RatKingRatBallMonsterSno)
                    return true;
            }
            catch
            {
            }

            try
            {
                if (monster.SnoActor != null &&
                    monster.SnoActor.Sno == ActorSnoEnum._p4_ratking_ratballmonster)
                    return true;
            }
            catch
            {
            }

            string text = string.Empty;

            try
            {
                text = ((monster.SnoMonster.Code ?? "") + " " + (monster.SnoMonster.NameEnglish ?? "")).ToLowerInvariant();
            }
            catch
            {
                text = string.Empty;
            }

            return text.Contains("ratking_ratballmonster") ||
                   text.Contains("ratking_ratball_monster") ||
                   text.Contains("ratballmonster") ||
                   text.Contains("ratball_monster");
        }

        private void PaintBossRatKingRatBallCore(IActor actor)
        {
            if (actor == null)
                return;

            uint key = GetActorCacheKey(actor);
            if (key != 0 && !_paintedRatKingRatBallKeys.Add(key))
                return;

            IWorldCoordinate coord = GetRatKingRatBallGroundCoordinate(actor);
            if (coord == null)
                return;
            DrawRatKingRatBallCircle(coord);
        }

        private IWorldCoordinate GetRatKingRatBallGroundCoordinate(IActor actor)
        {
            if (actor == null)
                return null;

            IWorldCoordinate coord = null;

            try
            {
                coord = actor.FloorCoordinate;
            }
            catch
            {
                coord = null;
            }

            if (coord == null)
            {
                try
                {
                    coord = actor.CollisionCoordinate;
                }
                catch
                {
                    coord = null;
                }
            }

            if (coord == null)
                return null;

            float groundZ = coord.Z;

            try
            {
                if (actor.ZDistanceToMeAbsolute > BossRatKingRatBallGroundClampZThreshold)
                {
                    float corrected = coord.Z - (float)actor.ZDistanceToMeAbsolute;

                    if (!float.IsNaN(corrected) && !float.IsInfinity(corrected))
                        groundZ = corrected;
                }
            }
            catch
            {
            }

            try
            {
                if (Hud.Game != null &&
                    Hud.Game.Me != null &&
                    Hud.Game.Me.FloorCoordinate != null &&
                    Math.Abs(groundZ - Hud.Game.Me.FloorCoordinate.Z) > BossRatKingRatBallGroundClampZThreshold)
                {
                    groundZ = Hud.Game.Me.FloorCoordinate.Z;
                }
            }
            catch
            {
            }

            try
            {
                return Hud.Window.CreateWorldCoordinate(coord.X, coord.Y, groundZ);
            }
            catch
            {
                return coord;
            }
        }

        private void DrawRatKingRatBallCircle(IWorldCoordinate coord)
        {
            if (coord == null)
                return;

            float radius = Math.Max(0.1f, BossRatKingRatBallRadius);
            int dashCount = Math.Max(12, BossRatKingRatBallRotatingDashCount);
            float dashFill = Math.Max(0.08f, Math.Min(0.80f, BossRatKingRatBallRotatingDashFill));
            float secondsPerTurn = Math.Max(1.0f, BossRatKingRatBallRotationSecondsPerTurn);

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossRatKingRatBallBaseBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossRatKingRatBallBaseFrontBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossRatKingRatBallBackBrush);
            DrawRotatingDashedCircle(coord, radius, dashCount, dashFill, phase, BossRatKingRatBallFrontBrush);
        }
        private void PaintBossHazardFallbackCircle(
            IActor actor,
            float fallbackRadius,
            IBrush backBrush,
            IBrush frontBrush)
        {
            if (actor == null)
                return;

            IWorldCoordinate coord = GetBossHazardCoordinate(actor);

            if (coord == null)
                return;

            float radius = GetEffectiveBossHazardRadius(actor, fallbackRadius);
            if (radius <= 0.0f)
                return;

            if (backBrush != null)
                backBrush.DrawWorldEllipse(radius, -1, coord);

            if (frontBrush != null)
                frontBrush.DrawWorldEllipse(radius, -1, coord);
        }

        private IWorldCoordinate GetBossHazardCoordinate(IActor actor)
        {
            if (actor == null)
                return null;

            try
            {
                if (actor.FloorCoordinate != null)
                    return actor.FloorCoordinate;
            }
            catch
            {
            }

            try
            {
                return actor.CollisionCoordinate;
            }
            catch
            {
                return null;
            }
        }

        private float GetEffectiveBossHazardRadius(IActor actor, float fallbackRadius)
        {
            if (actor == null)
                return Math.Max(0.0f, fallbackRadius);

            float radiusBottom = 0.0f;
            float radiusScaled = 0.0f;

            try { radiusBottom = actor.RadiusBottom; } catch { }
            try { radiusScaled = actor.RadiusScaled; } catch { }

            float nativeRadius = Math.Max(radiusBottom, radiusScaled);

            if (nativeRadius >= BossHazardMinimumNativeRadius)
                return nativeRadius;

            return Math.Max(0.0f, fallbackRadius);
        }

        private void PaintWormhole(WorldLayer layer, IActor actor)
        {
            if (WormholeDecorator == null || InWormholeDecorator == null)
                return;

            var me = Hud.Game.Me;
            if (me == null || me.FloorCoordinate == null)
            {
                WormholeDecorator.Paint(layer, actor, actor.FloorCoordinate, string.Empty);
                return;
            }

            var insideDistance = WormholeRadius + me.RadiusBottom + WormholeInsideTolerance;
            var distanceToPlayer = me.FloorCoordinate.XYDistanceTo(actor.FloorCoordinate);

            if (distanceToPlayer > insideDistance)
            {
                WormholeDecorator.Paint(layer, actor, actor.FloorCoordinate, string.Empty);
            }
            else
            {
                InWormholeDecorator.Paint(layer, actor, actor.FloorCoordinate, InWormholeText);
            }
        }

        private void PaintWaller(IActor actor)
        {
            if (WallBrush == null)
                return;

            DrawWall(WallBrush, actor.FloorCoordinate, GetWallerRotation(actor));
        }

        // ============================================================
        // Guard helpers
        // ============================================================
        private bool CanPaint()
        {
            if (Hud == null || Hud.Game == null)
                return false;

            if (RequireInGame && !Hud.Game.IsInGame)
                return false;

            if (HideWhileLoading && Hud.Game.IsLoading)
                return false;

            if (HideWhilePaused && Hud.Game.IsPaused)
                return false;

            if (HideInTown && Hud.Game.IsInTown)
                return false;

            if (Hud.Game.Me == null)
                return false;

            return true;
        }

        private static bool IsValidActor(IActor actor)
        {
            if (actor == null)
                return false;

            if (actor.SnoActor == null)
                return false;

            if (actor.FloorCoordinate == null)
                return false;

            return true;
        }

        // ============================================================
        // Waller geometry
        // ============================================================
        private float GetWallerRotation(IActor actor)
        {
            if (actor == null)
                return -45.0f;

            if (actor.FloorCoordinate == null || actor.CollisionCoordinate == null)
                return -45.0f;

            var diffX = (double)(actor.FloorCoordinate.X - actor.CollisionCoordinate.X);
            var diffY = (double)(actor.FloorCoordinate.Y - actor.CollisionCoordinate.Y);

            if (diffX == 0.0d && diffY == 0.0d)
                return -45.0f;

            return (float)(Math.Atan2(diffY, diffX) / DegreesToRadians) - 45.0f;
        }

        private void DrawWall(IBrush brush, IWorldCoordinate worldCoord, float rotation)
        {
            if (brush == null || worldCoord == null)
                return;

            var radius = ((float)Math.Sqrt(WallLength * WallLength + WallWidth * WallWidth)) * 0.5f;
            if (radius <= 0.0f)
                return;

            var wallAngleOffset = Math.Atan2(WallWidth, WallLength);

            var angle1 = rotation * DegreesToRadians + wallAngleOffset;
            var x1 = radius * (float)Math.Cos(angle1);
            var y1 = radius * (float)Math.Sin(angle1);
            var coord1 = worldCoord.Offset(x1, y1, 0);
            var coord3 = worldCoord.Offset(-x1, -y1, 0);

            var angle2 = rotation * DegreesToRadians - wallAngleOffset;
            var x2 = radius * (float)Math.Cos(angle2);
            var y2 = radius * (float)Math.Sin(angle2);
            var coord2 = worldCoord.Offset(x2, y2, 0);
            var coord4 = worldCoord.Offset(-x2, -y2, 0);

            brush.DrawLineWorld(coord1, coord2);
            brush.DrawLineWorld(coord2, coord3);
            brush.DrawLineWorld(coord3, coord4);
            brush.DrawLineWorld(coord4, coord1);
        }
    }
}
