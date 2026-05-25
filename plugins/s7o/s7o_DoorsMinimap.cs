namespace Turbo.Plugins.s7o
{
    using System.Collections.Generic;
    using Turbo.Plugins.Default;

    public class s7o_DoorsMinimap : BasePlugin, IInGameWorldPainter
    {
        public bool ShowInTown { get; set; }
        public float MapShapeRadius { get; set; }

        public WorldDecoratorCollection DoorDecorators { get; set; }
        public WorldDecoratorCollection BreakableDoorDecorators { get; set; }
        public WorldDecoratorCollection BridgeDecorators { get; set; }

        private readonly HashSet<ActorSnoEnum> BridgeIds = new HashSet<ActorSnoEnum>
        {
            ActorSnoEnum._x1_westm_bridge,
            ActorSnoEnum._a3dun_keep_siegetowerdoor_a,
            ActorSnoEnum._x1_westm_bridge_scoundrel,
            ActorSnoEnum._a3dun_keep_bridge_icy,
        };

        private readonly HashSet<ActorSnoEnum> BreakableDoorIds = new HashSet<ActorSnoEnum>
        {
            ActorSnoEnum._a3dun_keep_door_destructable,
            ActorSnoEnum._p4_ruins_frost_breakable_door,
            ActorSnoEnum._trdun_cath_wooddoor_a_barricaded,
            ActorSnoEnum._a1dun_leor_jail_door_breakable_a,
            ActorSnoEnum._p1_cesspools_door_breakable,
            ActorSnoEnum._cemetary_gate_trout_wilderness_no_lock,
        };

        private readonly HashSet<ActorSnoEnum> DoorBlacklist = new HashSet<ActorSnoEnum>
        {
            ActorSnoEnum._cald_merchant_cart,
            ActorSnoEnum._a2dun_cald_exit_gate,
            ActorSnoEnum._a2dunswr_gates_causeway_gates_non_op,
            ActorSnoEnum._a2dun_cald_belial_acid_attack,
            ActorSnoEnum._a2dun_cald_belial_room_gate_a,
            ActorSnoEnum._trout_cultists_summoning_portal_b,
            ActorSnoEnum._caout_target_dummy,
            ActorSnoEnum._start_location_team_0,
            ActorSnoEnum._a3dun_crater_st_demon_chainpylon_fire_azmodan,
            ActorSnoEnum._a3dun_keep_bridge,
            ActorSnoEnum._a3dun_rmpt_frozendoor_a,
            ActorSnoEnum._catapult_a3dunkeep_warmachines_snow_firing,
            ActorSnoEnum._x1_crusader_trebuchet_pending_tar,
            ActorSnoEnum._event_1000monster_portal,
            ActorSnoEnum._a2dun_zolt_sandbridgebase_bossfight,
            ActorSnoEnum._px_highlands_camp_resurgentcult_portal,
            ActorSnoEnum._x1_bog_catacombsportal_beaconloc,
            ActorSnoEnum._x1_malthael_boss_orb_collapse,
            ActorSnoEnum._caout_oasis_mine_entrance_a,
            ActorSnoEnum._trout_leor_painting,
            ActorSnoEnum._a4dun_sigil_room_platform_a,
            ActorSnoEnum._a3dun_rmpt_catapult_follower_event_gate,
            ActorSnoEnum._a1dun_leor_jail_door_superlocked_a_fake,
            ActorSnoEnum._cos_pet_mimic_01,
            ActorSnoEnum._shoulderpads_norm_base_flippy,
            ActorSnoEnum._x1_abattoir_barricade_solid,
            ActorSnoEnum._x1_fortress_floatrubble_a,
            ActorSnoEnum._a3dun_keep_barrel_snow_no_skirt,
            ActorSnoEnum._x1_fortress_crystal_prison_shield,
            ActorSnoEnum._x1_westm_railing_a_01_piece1,
            ActorSnoEnum._x1_pand_hexmaze_corpse,
            ActorSnoEnum._dh_companion_runec,
            ActorSnoEnum._loottype2_tristramvillager_male_c_corpse_01,
            ActorSnoEnum._uber_bossworld3_st_demon_chainpylon_fire_azmodan,
            ActorSnoEnum._trdun_crypt_skeleton_king_throne_parts,
            ActorSnoEnum._double_crane_a_caout_miningevent_chest_minievent,
            ActorSnoEnum._p6_church_bloodchannel_a,
            ActorSnoEnum._a4dun_sigil_tile_invis_wall,
            ActorSnoEnum._p1_tgoblin_gate,
            ActorSnoEnum._p1_tgoblin_vault_door,
            ActorSnoEnum._x1_urzael_soundspawner,
            ActorSnoEnum._x1_urzael_soundspawner_02,
            ActorSnoEnum._x1_urzael_soundspawner_03,
            ActorSnoEnum._x1_urzael_soundspawner_04,
            ActorSnoEnum._x1_westm_ex,
            ActorSnoEnum._trout_cath_entrance_door,
            ActorSnoEnum._x1_westm_shacklebar,
            ActorSnoEnum._x1_westm_shacklebar_b,
            ActorSnoEnum._x1_westm_stock,
        };

        public s7o_DoorsMinimap()
        {
            Enabled = true;
            ShowInTown = false;
            MapShapeRadius = 6.0f;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            DoorDecorators = CreateMapDoorDecorator(255, 216, 0);        // interactive doors: yellow
            BreakableDoorDecorators = CreateMapDoorDecorator(250, 0, 0); // breakable doors: red
            BridgeDecorators = CreateMapDoorDecorator(0, 195, 255);      // bridges: blue
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (Hud.Game.IsInTown && !ShowInTown)
                return;

            foreach (var actor in Hud.Game.Actors)
            {
                PaintDoorActor(layer, actor);
            }
        }

        private void PaintDoorActor(WorldLayer layer, IActor actor)
        {
            if (actor == null || actor.SnoActor == null || actor.FloorCoordinate == null)
                return;

            var sno = actor.SnoActor.Sno;

            if (DoorBlacklist.Contains(sno))
                return;

            if (actor.IsDisabled || actor.IsOperated)
                return;

            if (BreakableDoorIds.Contains(sno) || actor.GizmoType == GizmoType.BreakableDoor)
            {
                BreakableDoorDecorators.Paint(layer, actor, actor.FloorCoordinate, null);
                return;
            }

            if (actor.GizmoType != GizmoType.Door && actor.GizmoType != GizmoType.Gate)
                return;

            if (!actor.DisplayOnOverlay)
                return;

            if (BridgeIds.Contains(sno))
                BridgeDecorators.Paint(layer, actor, actor.FloorCoordinate, null);
            else
                DoorDecorators.Paint(layer, actor, actor.FloorCoordinate, null);
        }

        private WorldDecoratorCollection CreateMapDoorDecorator(int red, int green, int blue)
        {
            return new WorldDecoratorCollection(
                new MapShapeDecorator(Hud)
                {
                    ShapePainter = new DoorShapePainter(Hud),
                    Radius = MapShapeRadius,
                    Brush = Hud.Render.CreateBrush(230, red, green, blue, 1.25f),
                    ShadowBrush = Hud.Render.CreateBrush(180, 0, 0, 0, 2.25f),
                }
            );
        }
    }
}
