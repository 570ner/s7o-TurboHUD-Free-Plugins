using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Optional contextual help. All positions come from the HUD Menu's current controls.
    public class s7o_HoverTips : BasePlugin, IAfterCollectHandler
    {
        public int HoverDelayMilliseconds { get; set; } = 350;
        private s7o_HUD_MENU _menu;
        private IFont _titleFont,_bodyFont;
        private IBrush _background,_border,_shadow;
        private string _identity,_title,_body,_language;
        private int _since,_context,_height,_screenWidth,_fontHeight;
        private float _anchorX,_anchorY,_width,_boxHeight,_lineHeight,_titleHeight;
        private readonly List<string> _titleLines=new List<string>(),_bodyLines=new List<string>();
        public s7o_HoverTips(){Enabled=true;}
        public override void Load(IController hud)
        {
            base.Load(hud);
            _background=Hud.Render.CreateBrush(245,22,29,34,0);
            _border=Hud.Render.CreateBrush(255,72,176,82,1.2f);
            _shadow=Hud.Render.CreateBrush(150,0,0,0,0);
        }
        public void AfterCollect()
        {
            if(_menu==null)Hud.RunOnPlugin<s7o_HUD_MENU>(menu=>{_menu=menu;menu.HoverTipsPainter=PaintTip;});
            if(_menu==null || !_menu.HoverTipsMenuVisible || !_menu.HoverTipsEnabled
                || _menu.HoverTipsBlocked || !Hud.Window.IsForeground)Reset();
        }
        private void Reset(){_identity=null;_since=Environment.TickCount;}
        private sealed class HelpText
        {
            public readonly string Title,Body;
            public HelpText(string title,string body){Title=title;Body=body;}
        }
        private static readonly Dictionary<string,HelpText> Text=new Dictionary<string,HelpText>(StringComparer.OrdinalIgnoreCase)
        {
            { "shared.hover_tips", new HelpText("Hover Tips","Pause the pointer over an option to see a short explanation. Turn this off to hide these popups.") },
            { "shared.main", new HelpText("MAIN","Open gem upgrade settings, general target assistance and HUD hotkeys.") },
            { "shared.toggles", new HelpText("TOGGLES","Choose visual effects, class automation and spoken alerts.") },
            { "shared.plugins", new HelpText("PLUGINS","Browse installed plugins and switch them on or off. Some helpers are also controlled by more specific options under TOGGLES.") },
            { "shared.visual", new HelpText("VISUAL","Choose which markers, warnings and information appear on screen, then expand an option to adjust its appearance.") },
            { "shared.macros", new HelpText("MACROS","Enable the combat and convenience helpers you want to use. Class skill automation requires the matching skill on your bar.") },
            { "shared.tts_alerts", new HelpText("TTS Alerts","Choose spoken notifications and create messages you can play with a hotkey.") },
            { "shared.menu_key", new HelpText("Menu hotkey","Choose the key that opens or closes this menu. Press Escape to cancel assigning a new key.") },
            { "shared.move", new HelpText("Move menu","Enable moving the menu by dragging its title bar or floating button. Finish moving before using normal controls.") },
            { "shared.close", new HelpText("Close HUD","Exit TurboHUD while leaving Diablo running.") },
            { "shared.hide", new HelpText("Hide menu","Hide the menu without closing the game panels behind it.") },
            { "shared.no_click", new HelpText("No-Click Background","Open a game-panel background behind this menu to reduce accidental clicks into the world.") },
            { "shared.tts_enabled", new HelpText("Global TTS","Allow or mute supported spoken alerts and custom messages together.") },
            { "shared.tts_volume", new HelpText("TTS volume","Set the loudness of supported spoken alerts and custom messages.") },
            { "shared.expand", new HelpText("More options","Show or hide additional settings for {0}. Expanding the settings does not turn the feature on or off.") },
            { "shared.favorite", new HelpText("Favorite","Pin {0} in this category's favorites section for quicker access.") },
            { "shared.scroll", new HelpText("Scroll list","Move through the options in this list. This changes which rows you can see, not their settings.") },
            { "shared.unavailable", new HelpText("Unavailable option","{0} must be installed and enabled before these settings can be used.") },
            { "autogem.enable", new HelpText("Auto Gem Upgrade","Use Urshi's available upgrade attempts automatically with the selected mode. Coordinates the return-to-town timing while upgrades continue.") },
            { "autogem.auto", new HelpText("AUTO","Prefer the highest eligible gem at or below the Greater Rift level, keeping at least a 60% chance when possible. If none qualify, use the best available lower chance.") },
            { "autogem.fast", new HelpText("FAST","Use remaining upgrade attempts on the first available eligible gem, even at 0% chance. Keep using it while eligible; useful when your gems are already maxed.") },
            { "autogem.high", new HelpText("HIGH","Focus on the highest-rank eligible gem regardless of its upgrade chance, subject to the automatic rank limits.") },
            { "autogem.low", new HelpText("LOW","Upgrade the lowest-rank eligible gems, reconsidering the next choice as ranks rise. Useful for bringing several gems up together.") },
            { "autogem.specific", new HelpText("SPECIFIC","Upgrade only the chosen gem type. If you own several copies, the AUTO or HIGH setting decides which copy to use.") },
            { "autogem.specific_list", new HelpText("Specific gem","Choose the gem type to upgrade. This restricts selection to that type rather than falling back to unrelated gems.") },
            { "autogem.specific_auto", new HelpText("Specific: AUTO","Among copies of the chosen gem, use the normal AUTO chance rules. A copy with a better chance can be preferred over a higher-rank copy.") },
            { "autogem.specific_high", new HelpText("Specific: HIGH","Among eligible copies of the chosen gem, prefer the highest rank even when its upgrade chance is lower.") },
            { "autogem.anchor_three", new HelpText("3RD TP Anchor","Start the portal delay when an upgrade begins with three attempts remaining. This counts remaining attempts, not the third click of the run.") },
            { "autogem.anchor_four", new HelpText("4TH TP Anchor","Start the portal delay when an upgrade begins with four attempts remaining. If the run starts with fewer attempts, use the available starting count.") },
            { "autogem.delay", new HelpText("TP Delay","Set how long to wait after the chosen anchor upgrade starts before beginning Town Portal. A longer delay leaves more time for upgrades before returning to town.") },
            { "autogem.lag", new HelpText("LAG","Add 400 milliseconds to the portal delay. Use this extra margin if latency makes the normal return timing too tight.") },
            { "autosnap.overview", new HelpText("AutoSnap","Help aim selected skill slots at monsters, prioritizing bosses and elite leaders. This is separate from the Barbarian spear-pull helper.") },
            { "autosnap.slot", new HelpText("Skill slot","Enable target assistance for skill slot {0}. Disabled slots keep their normal aiming behavior.") },
            { "autosnap.restore", new HelpText("Cursor Restore","Return the pointer toward its saved position after target assistance, making it easier to resume your intended movement.") },
            { "autosnap.ignore_jug", new HelpText("Ignore Juggernauts","Exclude Juggernaut packs from this target assistant so other eligible monsters receive attention instead.") },
            { "autosnap.ignore_minions", new HelpText("Ignore Minions","Skip the minions accompanying rare elites when choosing a target.") },
            { "autosnap.hotkeys_enabled", new HelpText("Use Hotkeys","Allow separate target-assist hotkeys for the enabled skill slots.") },
            { "autosnap.hotkeys_only", new HelpText("Hotkey Only","Require the assigned target-assist hotkey instead of activating from an ordinary skill hold. Use Hotkeys must also be enabled.") },
            { "autosnap.hotkey", new HelpText("Slot hotkey","Choose the target-assist key for skill slot {0}. Press Escape to cancel assigning a new key.") },
            { "autosnap.near", new HelpText("NEAR ME","Choose targets around your hero. Use its range setting to limit the ordinary search area; bosses retain special priority.") },
            { "autosnap.cursor", new HelpText("CURSOR","Choose targets around the point you are aiming at. Use its range setting to control how far the aim may shift toward a target.") },
            { "autosnap.near_range", new HelpText("Near-me range","Adjust the normal target search radius around your hero. Larger values allow attention to farther monsters.") },
            { "autosnap.cursor_range", new HelpText("Cursor range","Adjust the normal search radius around your aim point. Smaller values keep target assistance closer to your intended location.") },
            { "autosnap.stand", new HelpText("Force Stand Still","Keep left-click attacks from turning into movement while the assistant is targeting. This setting applies only to the left mouse skill.") },
            { "hotkey.overview", new HelpText("HUD Hotkeys","Change the standard HUD shortcuts. Restart HUD after saving these shortcuts to apply them.") },
            { "hotkey.bind", new HelpText("Change shortcut","Choose a replacement shortcut for {0}. These standard HUD shortcuts take effect after restarting HUD.") },
            { "hotkey.reset", new HelpText("Reset Defaults","Restore the standard HUD shortcuts. Restart HUD to apply the restored bindings.") },
            { "hotkey.exit", new HelpText("Exit TurboHUD","Choose the shortcut that exits HUD while leaving Diablo running.") },
            { "hotkey.hide_hud", new HelpText("Hide HUD","Choose the shortcut that shows or hides HUD overlays.") },
            { "hotkey.reload_pickit", new HelpText("Reload PickIt","Choose the shortcut that reloads your loot highlighting rules after you change them.") },
            { "hotkey.capture", new HelpText("Screenshot","Choose the shortcut for a screenshot without HUD overlays.") },
            { "hotkey.capture_with_overlay", new HelpText("Screenshot + Overlay","Choose the shortcut for a screenshot that includes HUD overlays.") },
            { "hotkey.stat_tracker", new HelpText("Stat Tracker","Choose the shortcut that opens or closes the HUD statistics display.") },
            { "hotkey.debug_overlay", new HelpText("Debug Overlay","Choose the shortcut that shows diagnostic information used when troubleshooting HUD.") },
            { "hotkey.save_debug_data", new HelpText("Save Debug Data","Choose the shortcut that saves information useful for reporting a HUD problem.") },
            { "hotkey.reset_session", new HelpText("Reset Session","Choose the shortcut that resets HUD's current session statistics.") },
            { "macro.sanctified_monk_auto_dash", new HelpText("Auto Dash","Refresh the Raiment and Dash rune buffs during combat. This also enables automatic Dash movement used by target assistance.") },
            { "macro.sanctified_monk_self_dash", new HelpText("Self-Dash","Use Shift to Dash in place for upkeep instead of toward the pointer. Target assistance chooses its own combat destination.") },
            { "macro.sanctified_monk_melee_assist", new HelpText("Melee Target Assist","Hold its hotkey to approach and attack enemies with elite priority. Release it to stop the assistance.") },
            { "macro.sanctified_impale_auto_aim", new HelpText("Auto-aim Bolas","Aim the supporting Bolas shot at a monster, then return the pointer toward your movement position.") },
            { "macro.sanctified_impale_cadence", new HelpText("Combat Cast Interval","Set the gap between manually aimed Impale or Multishot casts in Combat mode. Shorter gaps interrupt Strafe more often; Speed mode is unaffected.") },
            { "macro.zbarb_autosnap_cone", new HelpText("Aim cone","Widen the cone to allow more pull directions, or narrow it to keep pulls closer to where you aim.") },
            { "macro.opengr_mapselector", new HelpText("OpenGR Map Selector","Choose the normal Greater Rift maps that stop RiftFishing so you can play the run. Orek's Dream remains selected.") },
            { "macro.auto_loot_pickup", new HelpText("Auto Loot Pickup","Automatically collect the item types enabled below. You can pause pickup temporarily without changing your filters.") },
            { "macro.inventory_drop_feature", new HelpText("Inventory Item Drop","Enable quick item dropping: Shift+F2 drops all eligible inventory items, while Shift+F3 follows your chosen filters. Review the filters before using it.") },
            { "macro.paragon_builds_plugin", new HelpText("Paragon Builds","Save and restore Paragon layouts and autocast settings for your heroes and Armory loadouts. The Armory control can disable it for an individual hero.") },
            { "macro.zdh_helper_plugin", new HelpText("ZDH Helper","Track important support effects on elites and optionally help apply support skills. Expand to choose each automatic cast and display separately.") },
            { "macro.armory_bug_fix_plugin", new HelpText("Armory Bug Fix","Help complete an Armory build swap when equipment is left unchanged or only partly swapped. Retries a failed Equip action.") },
            { "macro.autoskill_keybindings", new HelpText("AutoSkill Keybindings","Match AutoSkill's cast keys to your game's skill and potion bindings so enabled automation uses the intended skills.") },
            { "macro.zbarb_autosnap_plugin", new HelpText("Z-Barb AutoSnap","Assist Ancient Spear pulls toward useful groups. The aim cone limits how far a pull may deviate from your pointer.") },
            { "macro.barbarian_warcry_keepup", new HelpText("War Cry","Refresh War Cry when its buff is missing or close to expiring.") },
            { "macro.barbarian_battlerage_keepup", new HelpText("Battle Rage","Refresh Battle Rage when its buff is missing or close to expiring.") },
            { "macro.barbarian_sprint_keepupmoving", new HelpText("Sprint","Refresh Sprint while monsters are nearby. This can spend Fury between fights when enemies remain in range.") },
            { "macro.barbarian_ignorepain_defensive", new HelpText("Ignore Pain","Casts Ignore Pain when health is low or elites are nearby.") },
            { "macro.barbarian_wotb_eliteordensity", new HelpText("Wrath of the Berserker","Use Wrath of the Berserker near elites, bosses or large groups of monsters when its buff needs refreshing.") },
            { "macro.barbarian_cota_combat", new HelpText("Call of the Ancients","Summon missing Ancients when enemies are nearby.") },
            { "macro.crusader_lawsofvalor_elite", new HelpText("Laws of Valor","Activates Law of Valor near elites or bosses.") },
            { "macro.crusader_lawsofjustice_defensive", new HelpText("Laws of Justice","Activates Law of Justice when health is low or elites are nearby.") },
            { "macro.crusader_lawsofhope_defensive", new HelpText("Laws of Hope","Activates Law of Hope when health drops below threshold.") },
            { "macro.crusader_akaratschampion_combat", new HelpText("Akarat's Champion","Casts Akarat's Champion on elite/boss or high density.") },
            { "macro.crusader_ironskin_defensive", new HelpText("Iron Skin","Casts Iron Skin when health is low or elites are close.") },
            { "macro.crusader_provoke_resourceordensity", new HelpText("Provoke","Use Provoke when at least 40 Wrath is missing and at least three enemies are within 30 yards.") },
            { "macro.crusader_bombardment_eliteordensity", new HelpText("Bombardment","Fires Bombardment on elites or dense packs (disabled by default).") },
            { "macro.dh_preparation_disciplineorlife", new HelpText("Preparation: Discipline/Life","Use Preparation when at least 30 Discipline is missing, or with Battle Scars below 40% health. Punishment is controlled by the separate Hatred option.") },
            { "macro.dh_preparation_punishment_hatred", new HelpText("Preparation: Punishment","Use the Punishment rune when at least 75 Hatred is missing.") },
            { "macro.dh_companion_bat_hatred", new HelpText("Companion: Bat","Use Bat Companion when Hatred falls below 15% of its maximum.") },
            { "macro.dh_companion_eliteorboss", new HelpText("Companion: Elite","Use a non-Bat Companion's active ability near elites, bosses or goblins.") },
            { "macro.dh_vengeance_keepcombatbuff", new HelpText("Vengeance: Combat","Refresh Vengeance in combat when its buff needs renewing. Depending on the equipped rune, this may wait until health is below 60%.") },
            { "macro.dh_vengeance_hatredburst", new HelpText("Vengeance: Hatred","Use the Hatred-restoring Vengeance rune when Hatred is below 30 and the buff needs refreshing.") },
            { "macro.dh_shadowpower_defensive", new HelpText("Shadow Power: Defensive","Cast missing Shadow Power below 65% health, or below 75% with an elite on screen. With the four-piece Shadow bonus, maintain it whenever missing.") },
            { "macro.dh_shadowpower_elusivering", new HelpText("Shadow Power: Elusive","Use Shadow Power to help refresh Elusive Ring's damage reduction when the build supports it.") },
            { "macro.dh_strafe_plugin", new HelpText("DH Strafe","Maintain Strafe while using the primary skill needed by the build. Use the assigned start/stop and Combat/Speed controls during play.") },
            { "macro.monk_epiphany_combat", new HelpText("Epiphany","Casts Epiphany near elites or high density for spirit regen.") },
            { "macro.monk_sweepingwind_keepup", new HelpText("Sweeping Wind","Keeps Sweeping Wind active near enemies.") },
            { "macro.monk_mantraconviction_elite", new HelpText("Mantra of Conviction","Renew the short active Mantra bonus while you are attacking nearby enemies.") },
            { "macro.monk_mantrasalvation_defensive", new HelpText("Mantra of Salvation","Renew the short active resistance bonus when health is low or elites are nearby.") },
            { "macro.monk_mantrahealing_defensive", new HelpText("Mantra of Healing","Refresh the short shield while attacking or below 75% health. Recast when the shield weakens.") },
            { "macro.monk_mystically_resourceorelite", new HelpText("Mystic Ally","Casts Mystic Ally when spirit is low or elite/boss is nearby.") },
            { "macro.monk_serenity_defensive", new HelpText("Serenity","Use Serenity for protection below 55% health or when an elite or boss is within 30 yards.") },
            { "macro.pestilence_rgk_plugin", new HelpText("Pestilence RGK","Help aim Corpse Lance at priority targets and maintain Siphon Blood's Power Shift stacks. Expand to control targeting and stack upkeep separately.") },
            { "macro.inarius_rgk_plugin", new HelpText("Inarius RGK","Help aim at targets within the Inarius damage range and maintain Siphon Blood's Power Shift stacks. Expand to adjust aiming and stack upkeep.") },
            { "macro.necro_bonearmor_nearenemies", new HelpText("Bone Armor","Casts Bone Armor near enemies when missing or expiring.") },
            { "macro.necro_simulacrum_eliteordensity", new HelpText("Simulacrum","Casts Simulacrum near elites or dense packs.") },
            { "macro.necro_landofthedead_eliteordensity", new HelpText("Land of the Dead","Casts LotD on elites or dense packs (disabled by default).") },
            { "macro.necro_devour_corpseorlotd", new HelpText("Devour","Help maintain Devour's resource and buff benefits, with faster casting during Land of the Dead.") },
            { "macro.necro_commandskeletons_elite", new HelpText("Command Skeletons","Help keep a commanded target for Jesseth's bonus; otherwise prefer suitable elite and Rift Guardian targets.") },
            { "macro.necro_nayrsblackdeath", new HelpText("Nayr's Black Death","After you begin attacking with Siphon Blood, help maintain the poison-skill stacks used by Nayr's Black Death.") },
            { "macro.necro_skeletalmage_essencehigh", new HelpText("Skeletal Mage","Casts Skeletal Mage when essence is high (disabled by default).") },
            { "macro.wd_spiritwalk_defensive", new HelpText("Spirit Walk","Defensive Spirit Walk when health is critical or elites are close.") },
            { "macro.wd_soulharvest_nearenemies", new HelpText("Soul Harvest","Casts Soul Harvest near enemies when the buff needs upkeep.") },
            { "macro.wd_horrify_defensive", new HelpText("Horrify","Casts Horrify when enemies are close or health is low.") },
            { "macro.wd_fetisharmy_combat", new HelpText("Fetish Army","Summons Fetish Army near elites or dense packs.") },
            { "macro.wd_gargantuan_keepup", new HelpText("Gargantuan","Summons Gargantuan when missing and enemies are nearby.") },
            { "macro.wd_zombiedogs_keepup", new HelpText("Zombie Dogs","Summons Zombie Dogs when missing and enemies are nearby.") },
            { "macro.wd_bigbadvoodoo_eliteordensity", new HelpText("Big Bad Voodoo","Casts Big Bad Voodoo on elites or dense packs (off by default).") },
            { "macro.wizard_magicweapon", new HelpText("Magic Weapon","Recast Magic Weapon when its buff needs refreshing. Uses your equipped rune.") },
            { "macro.wizard_familiar", new HelpText("Familiar","Recast Familiar when its buff needs refreshing. Uses your equipped rune.") },
            { "macro.wizard_energyarmor", new HelpText("Energy Armor","Refresh Energy Armor when its buff needs renewing.") },
            { "macro.wizard_stormarmor", new HelpText("Storm Armor","Refresh Storm Armor when its buff needs renewing.") },
            { "macro.wizard_icearmor", new HelpText("Ice Armor","Refresh Ice Armor when its buff needs renewing.") },
            { "macro.wizard_diamondskin_defensive", new HelpText("Diamond Skin","Casts Diamond Skin when health is low or elites are close.") },
            { "macro.wizard_mirrorimage_defensive", new HelpText("Mirror Image","Deploys Mirror Image when health is low or elites are nearby.") },
            { "macro.wizard_frostnova_closeenemies", new HelpText("Frost Nova","Casts Frost Nova when enemies are within close range.") },
            { "macro.wizard_explosiveblast_closeenemies", new HelpText("Explosive Blast","Casts Explosive Blast when enemies are close (off by default).") },
            { "macro.wizard_archon_eliteordensity", new HelpText("Archon","Activates Archon on elites or dense packs (off by default).") },
            { "macro.exit_archon_plugin", new HelpText("Exit Archon","Leave Archon early with its assigned hotkey, rather than waiting for the form to expire.") },
            { "macro.sanctified_impale_plugin", new HelpText("Sanctified Impale / Multishot","Maintain Strafe with the equipped sanctified spender. Combat mode adds aimed casts; Speed mode primes the spender once and avoids repeated manual shots.") },
            { "macro.sanctified_monk_plugin", new HelpText("Sanctified Raiment Monk","Maintain generator and Dash buffs while attacking. Optional target assistance approaches enemies and helps choose combat positions.") },
            { "macro.pestilence_rgk_autosnap", new HelpText("AutoSnap","Aim toward the selected combat target while you hold Corpse Lance.") },
            { "macro.pestilence_rgk_autosiphon", new HelpText("AutoSiphon","Briefly use Siphon Blood while Corpse Lance is held to build or refresh Power Shift.") },
            { "macro.pestilence_rgk_normal_stacks", new HelpText("Normal stacks","Choose the Power Shift stack target used outside a Power pylon buff. Higher targets require more Siphon Blood time.") },
            { "macro.pestilence_rgk_power_stacks", new HelpText("Power stacks","Choose the Power Shift stack target used during a Power pylon buff, separately from normal combat.") },
            { "macro.pestilence_rgk_late_refresh", new HelpText("Late refresh assist","Permit a late Siphon Blood refresh when ordinary AutoSiphon is off, helping prevent the current stack buff from expiring.") },
            { "macro.pestilence_rgk_debuff_priority", new HelpText("Debuffed elite priority","Favor elites affected by the tracked Siphon effect when otherwise comparable targets have the same priority.") },
            { "macro.pestilence_rgk_trash_fallback", new HelpText("Trash fallback","Allow suitable trash targets after elite leaders are gone, rather than stopping target assistance immediately.") },
            { "macro.pestilence_rgk_target_range", new HelpText("Target range","Choose how far away this helper may search for targets. This changes selection distance, not Corpse Lance damage range.") },
            { "macro.pestilence_rgk_cursor_restore", new HelpText("Cursor restore","Return the pointer after brief target-assist actions so you can resume your intended aim or movement.") },
            { "macro.pestilence_rgk_jugger_lock", new HelpText("Juggernaut / Elite cycling","Use the assigned key to lock or cycle elite targets, starting with lower-health targets. The target marker shows the current selection.") },
            { "macro.pestilence_rgk_jugger_hotkey", new HelpText("Elite cycle hotkey","Choose the key for locking or cycling elite targets. Press Escape to cancel changing it.") },
            { "macro.pestilence_rgk_oculus_hotkey", new HelpText("Oculus teleport hotkey","Choose the key for the helper's Oculus or Triune teleport assistance.") },
            { "macro.pestilence_rgk_rg_assist", new HelpText("RG AutoSnap/Siphon","Allow these helpers during Rift Guardian fights. AutoSnap and AutoSiphon still follow their separate switches.") },
            { "macro.pestilence_rgk_target_line", new HelpText("Target line / reticle","Show which monster the helper is currently targeting, making unexpected target changes easier to see.") },
            { "macro.pestilence_rgk_zei_circle", new HelpText("Zei's 50 Yard Circle","Draw a distance reference around your hero for positioning with Zei's Stone of Vengeance. The circle itself adds no damage.") },
            { "macro.pestilence_rgk_zei_style", new HelpText("Zei style","Adjust the reference circle's color, brightness and line thickness.") },
            { "macro.inarius_rgk_autosnap", new HelpText("AutoSnap","Aim toward the selected combat target while you hold Corpse Lance.") },
            { "macro.inarius_rgk_autosiphon", new HelpText("AutoSiphon","Briefly use Siphon Blood while Corpse Lance is held to build or refresh Power Shift.") },
            { "macro.inarius_rgk_normal_stacks", new HelpText("Normal stacks","Choose the Power Shift stack target used outside a Power pylon buff. Higher targets require more Siphon Blood time.") },
            { "macro.inarius_rgk_power_stacks", new HelpText("Power stacks","Choose the Power Shift stack target used during a Power pylon buff, separately from normal combat.") },
            { "macro.inarius_rgk_late_refresh", new HelpText("Late refresh assist","Permit a late Siphon Blood refresh when ordinary AutoSiphon is off, helping prevent the current stack buff from expiring.") },
            { "macro.inarius_rgk_debuff_priority", new HelpText("Debuffed elite priority","Favor elites affected by the tracked Siphon effect when otherwise comparable targets have the same priority.") },
            { "macro.inarius_rgk_aggressive_scan", new HelpText("Aggressive scan","Try alternate aiming points more actively when an elite is difficult to select.") },
            { "macro.inarius_rgk_trash_fallback", new HelpText("Trash fallback","Allow suitable trash targets after elite leaders are gone, rather than stopping target assistance immediately.") },
            { "macro.inarius_rgk_cursor_restore", new HelpText("Cursor restore","Return the pointer after brief target-assist actions so you can resume your intended aim or movement.") },
            { "macro.inarius_rgk_range_indicator", new HelpText("Range indicator","Show the helper's colored range guide so you can judge whether a target is inside Inarius range.") },
            { "macro.inarius_rgk_target_line", new HelpText("Target line / reticle","Show which monster the helper is currently targeting, making unexpected target changes easier to see.") },
            { "macro.inarius_rgk_jugger_lock", new HelpText("Juggernaut / Elite cycling","Use the assigned key to lock or cycle elite targets, starting with lower-health targets. The target marker shows the current selection.") },
            { "macro.inarius_rgk_jugger_hotkey", new HelpText("Elite cycle hotkey","Choose the key for locking or cycling elite targets. Press Escape to cancel changing it.") },
            { "macro.inarius_rgk_rg_assist", new HelpText("RG AutoSnap/Siphon","Allow these helpers during Rift Guardian fights. AutoSnap and AutoSiphon still follow their separate switches.") },
            { "macro.auto_loot_pause", new HelpText("Pause Hotkey","Choose the key that pauses or resumes pickup without changing item filters. The pickup status dot can also pause or resume it.") },
            { "macro.auto_loot_normal_radius", new HelpText("Normal Radius","Choose how far away ordinary idle pickup can consider loot. Normal pickup movement remains limited to five yards.") },
            { "macro.auto_loot_event_radius", new HelpText("Event Chests & Boss Radius","Choose the wider pickup area used for Rift Guardian rewards and supported event chests.") },
            { "macro.auto_loot_urshi", new HelpText("Talk to Urshi","After Greater Rift reward cleanup, approach Urshi and open gem upgrades. Late ordinary items may be left behind to keep the run moving.") },
            { "macro.auto_loot_primals", new HelpText("Primals","Pick up primal ancient legendary/set items.") },
            { "macro.auto_loot_ancients", new HelpText("Ancients","Pick up ancient legendary/set items.") },
            { "macro.auto_loot_legendaries", new HelpText("Legendaries","Pick up normal legendary/set items and legendary potions.") },
            { "macro.auto_loot_gems", new HelpText("Gems","Pick up normal and legendary gems.") },
            { "macro.auto_loot_gifts", new HelpText("Gifts","Pick up Ramaladni's Gifts.") },
            { "macro.auto_loot_screams", new HelpText("Screams","Pick up Petrified Screams.") },
            { "macro.auto_loot_materials", new HelpText("Materials","Pick up crafting materials except Death's Breaths.") },
            { "macro.auto_loot_deaths_breath", new HelpText("Death's Breath","Pick up Death's Breaths separately from materials.") },
            { "macro.auto_loot_trash", new HelpText("Trash Items","Pick up rare, magic, and normal items.") },
            { "macro.inventory_drop_nonbound_legendary", new HelpText("Unbound legendaries","Include tradable legendary and set items in filtered dropping, including Ancient and Primal items. Bound items are excluded by this filter.") },
            { "macro.inventory_drop_trash", new HelpText("Trash","Drop rare, magic, normal, superior, and inferior items.") },
            { "macro.inventory_drop_gifts", new HelpText("Gifts","Drop Ramaladni's Gifts.") },
            { "macro.inventory_drop_screams", new HelpText("Screams","Drop Petrified Screams.") },
            { "macro.inventory_drop_gems", new HelpText("Gems","Include ordinary gems in filtered dropping. Legendary gems are not included by this filter.") },
            { "macro.autoskill_keybindings_row", new HelpText("Skill cast keys","Choose the keyboard keys used for each skill slot and potion. Match the bindings you use in Diablo.") },
            { "macro.zdh_auto_entangle", new HelpText("Auto Entangle","Help apply Entangling Shot's Odyssey damage effect to engaged elites. Higher-priority support casts are handled first.") },
            { "macro.zdh_auto_multishot", new HelpText("Auto Multishot","Refresh Wind Chill and Iceblink on engaged elites when the build can apply those effects.") },
            { "macro.zdh_auto_mfd", new HelpText("Auto Valley of Death","Place Marked for Death's damage area to cover useful elite targets, rather than replacing it for small position changes.") },
            { "macro.zdh_auto_sentry", new HelpText("Auto Guardian Sentry","Place Guardian Turrets around an ongoing fight to cover damage dealers who need protection.") },
            { "macro.zdh_rg_automation", new HelpText("RG Automation","Allow the enabled ZDH support casts during Rift Guardian fights. The individual skill settings still apply.") },
            { "macro.zdh_labels", new HelpText("Elite Labels","Show compact Iceblink, damage-buff and Marked for Death indicators below eligible elite health bars.") },
            { "macro.zdh_uptime", new HelpText("Uptime Stats","Show how consistently the tracked support effects are active while eligible elites are being fought.") },
            { "visual.hudlanguage", new HelpText("HUD Language","Choose the language used by supported HUD text. Restart HUD after selecting it.") },
            { "visual.mapviewer", new HelpText("Map Viewer","Show the floor grid and obstacle outlines. Navigation helpers can still use the map when this display is hidden.") },
            { "visual.mapviewer_debug", new HelpText("Debug Logging","Save map troubleshooting information to a log. Leave this off unless you are investigating a problem.") },
            { "visual.xptracker", new HelpText("XP Tracker","Show experience rates while your inventory is open. Each counter has its own reset button.") },
            { "visual.xptracker_persist", new HelpText("Persist Tracker","Keep the experience counters after restarting HUD instead of beginning a fresh tracking session.") },
            { "visual.playerbuffs", new HelpText("Player Buffs","Place selected buff icons and recovery timers near your hero. Expand to choose categories and adjust their appearance.") },
            { "visual.playerbuffs_shrines", new HelpText("Shrines","Show active shrine effects in the movable shrine group.") },
            { "visual.playerbuffs_pylons", new HelpText("Pylons","Show active pylon effects in the movable power group.") },
            { "visual.playerbuffs_classskills", new HelpText("Class Skills","Show supported active class buffs and cooldowns separately from item effects and pylons.") },
            { "visual.playerbuffs_cheatdeath", new HelpText("Cheat Death Icons","Show recovery timers for class and follower cheat-death effects after they trigger.") },
            { "visual.playerbuffs_legendary", new HelpText("Legendary Powers","Show supported item and legendary-gem effects, including their available buff information.") },
            { "visual.playerbuffs_move", new HelpText("Move Buff Groups","Close the menu, hold the chosen key over a buff group and move the pointer. Release the key to save its position.") },
            { "visual.playerbuffs_reset", new HelpText("Reset Positions","Return all player buff groups to their default positions. Their enabled categories and appearance settings stay selected.") },
            { "visual.playercoe", new HelpText("Player CoE Overlay","Keep selected players' Convention of Elements cycles near your hero so you can follow their damage windows.") },
            { "visual.playercoe_add", new HelpText("Add / Remove Player Overlay","Tap the assigned key over a party portrait or CoE row to add an overlay. Tap that portrait, row or placed overlay again to remove it.") },
            { "visual.playercoe_element", new HelpText("Highest-Damage Element","Hover an element in a placed overlay and press the assigned key to mark that player's important damage element.") },
            { "visual.playercoe_move", new HelpText("Move and Place the Overlay","Hold the placement key while dragging a placed CoE overlay. Release it to keep that position.") },
            { "visual.tipshelper", new HelpText("Visual Helpers","Choose loot alerts, globe highlights and party markers to make important objects easier to see.") },
            { "visual.dangeraffixes", new HelpText("Elite/Dangerous Affix Visuals","Choose which dangerous effects receive visible warnings. These switches control drawings, not automatic dodging.") },
            { "visual.elitehp", new HelpText("Elite Health Bars","Show elite health information and adjust bar dimensions to suit your display.") },
            { "visual.elitecircles", new HelpText("Elite Hitbox Circles","Draw circles around elite bodies to help judge spacing and positioning. Colors follow their elite health bars.") },
            { "visual.simhp", new HelpText("Simulacrum Health Bars","Watch your own or party members' Simulacrum health so you can notice when they need protection or healing.") },
            { "visual.guardiansentry", new HelpText("Guardian Sentry Circle","Mark Guardian Turret protection areas so you can position inside them.") },
            { "visual.valleyofdeath", new HelpText("Valley of Death Circle","Mark the ground area affected by Marked for Death: Valley of Death.") },
            { "visual.menubutton", new HelpText("Menu Button","Choose whether the floating menu button is visible and adjust its appearance when the menu is open or closed.") },
            { "visual.partyinspector", new HelpText("Party Inspector","Inspect party equipment and build information. Its expanded view has a separate configurable hotkey.") },
            { "visual.click", new HelpText("Click Animation","Show a brief visual burst when clicking inside this HUD menu, making menu clicks easier to notice.") },
            { "visual.player", new HelpText("Player Circle","Mark the ground around your hero with a distance circle for positioning.") },
            { "visual.mouse", new HelpText("Mouse Circle","Show a ground-distance reference at the point under your pointer.") },
            { "visual.line", new HelpText("Line to Target","Draw a line from your hero to the selected monster so your current target is easy to follow.") },
            { "visual.reticle", new HelpText("Target Reticle","Mark the selected monster with an animated target indicator.") },
            { "visual.outline", new HelpText("Reticle Outline","Add a clearer outer edge to the selected monster's target marker.") },
            { "visual.minion", new HelpText("Minion Circles","Mark the ground under rare elites' minions so they are easier to distinguish in a pack.") },
            { "visual.siphon", new HelpText("Siphon Debuff Circle","Show tracked Siphon effects and other Necromancers' primary targets, including when you play another class.") },
            { "visual.primal", new HelpText("Primal Alerts","Make Primal drops easier to find with the enabled text, screen-edge arrows and map markers.") },
            { "visual.primal_arrows", new HelpText("Primal arrows","Point toward Primal drops near the edge of the screen.") },
            { "visual.primal_text", new HelpText("Primal text","Show a text alert when a Primal item drops. The text has its own size, color and display duration.") },
            { "visual.ancient", new HelpText("Ancient Alerts","Make Ancient drops easier to find with the enabled text, screen-edge arrows and map markers.") },
            { "visual.ancient_arrows", new HelpText("Ancient arrows","Point toward Ancient drops near the edge of the screen.") },
            { "visual.ancient_text", new HelpText("Ancient text","Show a text alert when an Ancient item drops. The text has its own size, color and display duration.") },
            { "visual.healthglobes", new HelpText("Health Globes","Mark health globes with red dots so they stand out during a fight.") },
            { "visual.progressorbs", new HelpText("Progress Orbs","Mark Greater Rift progress orbs with purple dots so they are easier to collect.") },
            { "visual.bloodispower", new HelpText("Blood is Power Tracker","Show progress toward Blood is Power's cooldown reduction for Land of the Dead and Simulacrum.") },
            { "visual.playermarkers", new HelpText("Player Markers","Mark or unmark a party member by hovering their portrait and pressing the assigned key.") },
            { "visual.player_color", new HelpText("Player marker color","Choose the marker color used for party member {0}.") },
            { "visual.unscathed", new HelpText("Trash HP Bars","Show health bars over undamaged trash monsters, making them easier to see before they are hit.") },
            { "visual.minimapcursor", new HelpText("Minimap Mouse Cursor","Make your pointer easier to find on the minimap, above ordinary monster markers.") },
            { "visual.minimapcursor_town", new HelpText("Show in town","Keep the minimap pointer marker visible in town as well as during combat areas.") },
            { "visual.simhp_own", new HelpText("Own Simulacrums","Show health bars for your own Simulacrums independently of the party setting.") },
            { "visual.simhp_party", new HelpText("Party Simulacrums","Show health bars for other players' Simulacrums independently of your own.") },
            { "warning.plagued", new HelpText("Plague Pools","Highlight Plagued ground pools so their affected area is easier to avoid.") },
            { "warning.arcanespawn", new HelpText("Arcane Spawn","Show where an Arcane beam is about to appear.") },
            { "warning.moltenexplosion", new HelpText("Molten Explosion","Mark the dangerous area around a Molten monster's death explosion.") },
            { "warning.desecrator", new HelpText("Desecrator","Highlight Desecrator ground areas that deal repeated damage while you remain inside.") },
            { "warning.thunderstorm", new HelpText("Thunderstorm","Large lightning strike warning circle.") },
            { "warning.frozen", new HelpText("Frozen","Mark Frozen bombs and their explosion area so you can move away before they burst.") },
            { "warning.frozenpulse", new HelpText("Frozen Pulse","Highlight the moving Frozen Pulse hazard so you can track its position.") },
            { "warning.ghom", new HelpText("Ghom Gas","Large green poison cloud warning.") },
            { "warning.grotesque", new HelpText("Grotesque Explosion","Large red corpse explosion warning.") },
            { "warning.orbiter", new HelpText("Orbiter","Highlight both the central and smaller orbiting lightning hazards.") },
            { "warning.waller", new HelpText("Waller","Orange wall warning outlines.") },
            { "warning.wormholemines", new HelpText("Wormhole","Wormhole mine warning overlay.") },
            { "warning.bossfallingrocks", new HelpText("Falling Rocks","Perendi's falling-rock warning circle.") },
            { "warning.bossleaptelegraphs", new HelpText("Leap Telegraph","Bloodmaw leap warning overlay.") },
            { "warning.bossmeteors", new HelpText("Meteors","Ember meteor warning circle.") },
            { "warning.echoingmeteors", new HelpText("Echoing Nightmare Meteors","Green meteor warning circles in Echoing Nightmare.") },
            { "warning.bosskullehazards", new HelpText("Zoltun Kulle","Zoltun Kulle boulder warning.") },
            { "warning.bossblighterhazards", new HelpText("Blighter","Blighter's poison warning circle overlays.") },
            { "warning.bossratkinghazards", new HelpText("Hamelin / Rat King","Rat cloud warning overlays.") },
            { "warning.bossadriageysers", new HelpText("Tethrys Geysers","Tethrys geyser warning circles.") },
            { "warning.bossbutcherfire", new HelpText("Butcher Fire","Butcher fire warning circle.") },
            { "warning.bossrimehazards", new HelpText("Rime","Rime cold warning circle overlays.") },
            { "warning.shocktowerhazards", new HelpText("Shock Tower","Shock tower danger-zone overlay.") },
            { "warning.bosssandmonsterturret", new HelpText("Stonesinger Turret","Stonesinger turret warning circle.") },
            { "warning.labels", new HelpText("Elite Affix Labels","Show the affixes on elite packs so you can recognize their abilities before engaging.") },
            { "warning.dangerlabels", new HelpText("Dangerous Monster Labels","Add red labels and markers to supported dangerous monster types.") },
            { "adjust.color", new HelpText("Color","Choose the color used for {0}. This changes the display, not the strength of the effect.") },
            { "adjust.tone", new HelpText("Tone","Make the chosen color for {0} lighter or darker without changing its basic color.") },
            { "adjust.size", new HelpText("Size","Make {0} larger or smaller on screen.") },
            { "adjust.width", new HelpText("Width","Adjust how wide {0} appears on screen.") },
            { "adjust.height", new HelpText("Height","Adjust how tall {0} appears on screen.") },
            { "adjust.line", new HelpText("Line thickness","Make the outline or line for {0} easier to see by increasing its thickness.") },
            { "adjust.yards", new HelpText("Yards","Change the radius of the visible {0} distance guide. This does not change a skill's actual range.") },
            { "adjust.dot", new HelpText("Dot size","Adjust the small marker for {0} separately from its surrounding ring.") },
            { "adjust.duration", new HelpText("Display duration","Choose how long the {0} text alert remains visible after a drop.") },
            { "adjust.opacity", new HelpText("Opacity","Make {0} more solid or more see-through. Higher opacity makes it easier to see over the game.") },
            { "adjust.transparency", new HelpText("Transparency","Make {0} more see-through to reveal the game behind it. Higher transparency makes it less prominent.") },
            { "adjust.hotkey", new HelpText("Assign key","Choose the key used for {0}. Press Escape to leave the current assignment unchanged.") },
            { "adjust.preview", new HelpText("Preview","See how {0} will look with the current appearance settings.") },
            { "adjust.reset", new HelpText("Reset","Restore the default settings for {0}.") },
            { "adjust.increase", new HelpText("Increase","Increase {0}. {1}") },
            { "adjust.decrease", new HelpText("Decrease","Decrease {0}. {1}") },
            { "adjust.language", new HelpText("Select language","Use {0} for supported HUD text. Restart HUD after changing this selection.") },
            { "adjust.riftmap", new HelpText("Accept map","Select {0} as a Greater Rift map on which RiftFishing should stop searching. Orek's Dream remains accepted.") },
            { "adjust.skill_key", new HelpText("Skill cast key","Set the key AutoSkill uses for {0}. It should match that action's binding in Diablo.") },
            { "adjust.toggle", new HelpText("Enable option","Allow {0}. {1}") },
            { "tts.pylons", new HelpText("Pylon Alerts","Hear spoken notifications about supported pylons, their activation and their expiration.") },
            { "tts.banner", new HelpText("Banner Sound","Hear the configured announcement when a banner drops, instead of relying only on seeing it.") },
            { "tts.broadcast", new HelpText("TTS Broadcast","Speak text entered with the .tts chat command. You can also save messages and assign keys to play them quickly.") },
            { "tts.add", new HelpText("Add message","Create another saved spoken message, then choose its text and optional hotkey.") },
            { "tts.edit", new HelpText("Edit message","Change what this saved message says using the menu's text editor.") },
            { "tts.delete", new HelpText("Delete message","Remove this saved message and its assigned hotkey.") },
            { "tts.hotkey", new HelpText("Message hotkey","Choose a key that plays this saved message during normal gameplay. It does not type the message into chat.") },
            { "tts.reset", new HelpText("Reset message hotkey","Clear this message's assigned hotkey while keeping its text.") },
            { "tts.key", new HelpText("Type character","Insert {0} into the message being edited.") },
            { "tts.caps", new HelpText("Caps Lock","Switch between upper-case and lower-case letters in this message editor.") },
            { "tts.shift", new HelpText("Shift","Use an upper-case letter for the next character entered with the on-screen keyboard.") },
            { "tts.backspace", new HelpText("Backspace","Delete the character immediately before the message cursor.") },
            { "tts.clear", new HelpText("Clear message","Erase the text of the message currently being edited.") },
            { "tts.done", new HelpText("Done","Finish editing this message and keep its current text.") },
            { "plugin.armorybugfix", new HelpText("Armory Bug Fix","Help complete failed or partial Armory equipment swaps by checking the result and retrying Equip when needed.") },
            { "plugin.autogem", new HelpText("Auto Gem Upgrade","Use Urshi's upgrade attempts with the selected gem mode and coordinate the return-to-town timing.") },
            { "plugin.autoloot", new HelpText("Auto Loot Pickup","Collect the enabled item types automatically, with separate ordinary and reward-pile pickup settings.") },
            { "plugin.autoskill", new HelpText("AutoSkill","Use enabled skills automatically when their combat, resource or buff conditions are met.") },
            { "plugin.banner", new HelpText("Banner Sound","Announce banner drops with the configured sound or spoken message.") },
            { "plugin.buffs", new HelpText("Player Buffs","Keep selected buffs and cheat-death recovery timers near your character in movable groups.") },
            { "plugin.dangerousskills", new HelpText("Dangerous Skill Overlays","Show ground warnings for supported dangerous boss and Rift Guardian attacks.") },
            { "plugin.dhstrafe", new HelpText("DH Strafe","Maintain Strafe while using supporting primary-skill casts and Combat/Speed controls.") },
            { "plugin.doors", new HelpText("Doors Minimap","Mark detected doors on the minimap so their positions are easier to recognize.") },
            { "plugin.elitebars", new HelpText("Elite Health Bars","Show elite health information and indicators for supported elites outside the visible fight area.") },
            { "plugin.exitarchon", new HelpText("Exit Archon","Use a hotkey to leave Archon before its normal duration ends.") },
            { "plugin.genmonk", new HelpText("Raiment Monk","Maintain generator and Dash buffs, with optional target assistance for approaching and fighting enemies.") },
            { "plugin.hudmenu", new HelpText("HUD Menu","Open the shared controls for visuals, automation, spoken alerts and hotkeys.") },
            { "plugin.impale", new HelpText("Sanctified Impale / Multishot","Maintain Strafe with the equipped sanctified spender and optional aimed casts in Combat mode.") },
            { "plugin.inarius", new HelpText("Inarius RGK","Assist close-range Inarius targeting and Siphon Blood's Power Shift upkeep.") },
            { "plugin.inventory", new HelpText("Inventory Helpers","Show extra item information and help sort supported stacks and item groups into storage.") },
            { "plugin.salvage", new HelpText("Item Salvage","Help salvage selected items while applying its item protections and recovery behavior.") },
            { "plugin.kadala", new HelpText("Kadala Helper","Hover an item at Kadala and press F2 to mark it. Press F3 to spend Blood Shards on your saved targets.") },
            { "plugin.cube", new HelpText("Kanai's Cube","Help perform supported cube tasks such as reforging, upgrading rare items and converting materials.") },
            { "plugin.localization", new HelpText("HUD Language","Use the selected language for supported HUD labels and messages.") },
            { "plugin.mapcursor", new HelpText("Minimap Mouse Cursor","Make the pointer easier to see over ordinary minimap markers, with adjustable appearance.") },
            { "plugin.mapviewer", new HelpText("Map Viewer","Display the floor grid and obstacles while providing map assistance to compatible movement helpers.") },
            { "plugin.mystic", new HelpText("Mystic Enchant","Help reroll the chosen item stat at the Mystic using the assigned hotkey.") },
            { "plugin.oculus", new HelpText("Oculus / Triune","Make the supported damage and other buff circles easier to recognize on the ground.") },
            { "plugin.paragonbuilds", new HelpText("Paragon Builds","Save and restore Paragon layouts and autocast settings for each hero and Armory loadout.") },
            { "plugin.paragonhelper", new HelpText("Paragon Helper","Quickly allocate Core Paragon points using the selected Primary Stat or Vitality preference.") },
            { "plugin.party", new HelpText("Party Inspector","Inspect party builds and equipment, with optional Convention of Elements overlays near your hero.") },
            { "plugin.pestilence", new HelpText("Pestilence RGK","Assist Corpse Lance targeting and Power Shift upkeep, with separate aiming and Siphon controls.") },
            { "plugin.pylons", new HelpText("Pylon Alerts","Announce supported pylons and their activation or expiration with spoken alerts.") },
            { "plugin.riftfishing", new HelpText("Rift Fishing","Search for acceptable Greater Rift maps using your map selections and stop or announce when a suitable run is found.") },
            { "plugin.riftinfo", new HelpText("Rift Info","Show Greater Rift timing, progress and fight information to help judge the run.") },
            { "plugin.simulacrum", new HelpText("Simulacrum Health Bars","Watch your own and party Simulacrum health to spot when they need protection or healing.") },
            { "plugin.tipshelper", new HelpText("Visual Helpers","Highlight important loot, globes and party members, with optional Blood is Power progress tracking.") },
            { "plugin.ttsbroadcast", new HelpText("TTS Broadcast","Speak .tts chat messages and saved hotkey messages through the configured voice settings.") },
            { "plugin.ttsconfig", new HelpText("TTS Settings Helper","Apply the shared speech settings used by supported spoken alerts.") },
            { "plugin.zbarb", new HelpText("Z-Barb AutoSnap","Assist Ancient Spear pulls while keeping them within the configured cone around your aim.") },
            { "plugin.zdh", new HelpText("ZDH Helper","Track support effects on elites and optionally assist Entangling Shot, Multishot, Valley of Death and Guardian Sentry.") },
        };
        private static readonly Dictionary<string,string> PluginKeys=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            { "s7o_armorybugfix","armorybugfix" },
            { "s7o_autogemupgradenavigator","autogem" },
            { "s7o_autoloot","autoloot" },
            { "s7o_autoskill","autoskill" },
            { "s7o_bannersoundplugin","banner" },
            { "s7o_buffs","buffs" },
            { "s7o_dangerousskilloverlays","dangerousskills" },
            { "s7o_dhstrafeprimaryplugin","dhstrafe" },
            { "s7o_dhstrafeprimarycustomizer","dhstrafe" },
            { "s7o_doorsminimap","doors" },
            { "s7o_elitehealthbars","elitebars" },
            { "s7o_exitarchon","exitarchon" },
            { "s7o_genmonk","genmonk" },
            { "s7o_hud_menu","hudmenu" },
            { "s7o_impale","impale" },
            { "s7o_inarius_rgk","inarius" },
            { "s7o_inventoryadjustments","inventory" },
            { "s7o_itemsalvage","salvage" },
            { "s7o_kadalahelper","kadala" },
            { "s7o_kanaicubecustomizer","cube" },
            { "mapcursor","mapcursor" },
            { "s7o_mapviewer","mapviewer" },
            { "s7o_mysticenchantplugin","mystic" },
            { "s7o_mysticenchantcustomizer","mystic" },
            { "s7o_oculus_triune","oculus" },
            { "s7o_paragon_builds","paragonbuilds" },
            { "s7o_paragonhelper","paragonhelper" },
            { "s7o_partyinspector","party" },
            { "s7o_pestilence_rgk","pestilence" },
            { "s7o_pylonalerts","pylons" },
            { "s7o_riftfishing","riftfishing" },
            { "s7o_riftinfo","riftinfo" },
            { "s7o_simulacrum_hp_bars","simulacrum" },
            { "s7o_tipshelper","tipshelper" },
            { "s7o_tts_broadcast","ttsbroadcast" },
            { "s7o_ttsconfigplugin","ttsconfig" },
            { "s7o_zb_autosnap","zbarb" },
            { "s7o_zdh_helper","zdh" },
            { "itemsalvage","salvage" },
            { "armorybugfix","armorybugfix" },
            { "paragonbuilds","paragonbuilds" },
            { "mystic","mystic" },
            { "kanaicube","cube" },
            { "turbocube","cube" },
            { "autoskill","autoskill" },
            { "pestilencergk","pestilence" },
            { "inariusrgk","inarius" },
            { "sanctifiedimpale","impale" },
            { "sanctifiedmonk","genmonk" },
            { "dhstrafe","dhstrafe" },
            { "zbarbautosnap","zbarb" },
            { "exitarchon","exitarchon" },
            { "pylons","pylons" },
            { "banner","banner" },
            { "ttsbroadcast","ttsbroadcast" },
            { "party","party" },
            { "elitebars","elitebars" },
            { "elitehp","elitebars" },
            { "oculus","oculus" },
        };
        private static string Token(string text){return Regex.Replace((text??"").ToLowerInvariant(),"[^a-z0-9]+","_").Trim('_');}
        private static string ParentKey(string parent)
        {
            if(string.IsNullOrEmpty(parent))return null;
            if(parent.StartsWith("cat:"))return parent.Substring(4);
            if(parent.StartsWith("macro:"))
            {
                string code=parent.Substring(6);int option;
                if(code.StartsWith("zdh_helper_option_",StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(code.Substring(18),out option) && option>=1 && option<=7)
                    return "macro.zdh_"+new[]{"auto_entangle","auto_multishot","auto_mfd","auto_sentry","labels","uptime","rg_automation"}[option-1];
                return "macro."+Token(code);
            }
            if(parent.StartsWith("plugin:"))
            {if(parent.Substring(7).Equals("s7o_HoverTips",StringComparison.OrdinalIgnoreCase))return "shared.hover_tips";
                string key;return PluginKeys.TryGetValue(parent.Substring(7),out key)?"plugin."+key:null;}
            if(parent.StartsWith("affix:"))return "warning."+parent.Substring(6);
            if(parent.StartsWith("visual:"))
            {
                if(parent=="visual:xppersist:toggle")return "visual.xptracker_persist";
                string id=parent.Substring(7);if(id.StartsWith("tipstoggle:"))id=id.Substring(11);
                if(id.StartsWith("playerbuffs:flag:"))
                {
                    string flag=id.Substring(17).ToLowerInvariant().Replace("show","");
                    return "visual.playerbuffs_"+flag;
                }
                var aliases=new Dictionary<string,string>{
                    {"primalarrows","primal_arrows"},{"primaltext","primal_text"},{"ancientarrows","ancient_arrows"},{"ancienttext","ancient_text"},
                    {"tipsprimaltext","primal_text"},{"tipsancienttext","ancient_text"},{"minimapcursor","minimapcursor"},
                    {"tipshealthdot","healthglobes"},{"tipsprogressdot","progressorbs"},{"tipsplayer","playermarkers"}};
                string replacement;if(aliases.TryGetValue(id,out replacement))id=replacement;
                if(id.StartsWith("tipsplayer"))return "visual.player_color";
                return "visual."+id;
            }
            if(parent.StartsWith("toggles:plugin:"))
            {string key;return PluginKeys.TryGetValue(parent.Substring(15),out key)?"plugin."+key:null;}
            return null;
        }
        private static string Resolve(string action,string parent,string label,out object[] args)
        {
            args=new object[0];if(string.IsNullOrEmpty(action))return null;
            string pk=ParentKey(parent),a=action.ToLowerInvariant();
            if(a.StartsWith("cat:"))return a.Substring(4);
            if(a=="row")
            {
                if(pk=="visual.player_color"){var m=Regex.Match(parent,"[1-4]$");args=new object[]{m.Success?m.Value:""};}
                return pk;
            }
            if(a=="favorite" || a.Contains(":star:")){args=new object[]{label};return "shared.favorite";}
            if(a.StartsWith("macro:toggle:"))return ParentKey("macro:"+action.Substring(action.LastIndexOf(':')+1));
            if(a.StartsWith("global:tts"))return a=="global:tts-toggle"?"shared.tts_enabled":"shared.tts_volume";
            if(a.StartsWith("toggles:cat:"))return a.EndsWith(":0")?"shared.visual":a.EndsWith(":1")?"shared.macros":"shared.tts_alerts";
            if(a.StartsWith("toggles:plugin:"))return ParentKey(a);
            if(a.StartsWith("affix:"))return "warning."+a.Substring(6);
            if(a.StartsWith("autogem:"))
            {
                string id=a.Substring(8);
                if(id.StartsWith("mode:")){int mode;return int.TryParse(id.Substring(5),out mode)?"autogem."+new[]{"auto","low","high","fast","specific"}[Math.Max(0,Math.Min(4,mode))]:null;}
                if(id=="toggle")return "autogem.enable";
                if(id=="anchor3")return "autogem.anchor_three";if(id=="anchor4")return "autogem.anchor_four";
                if(id.StartsWith("delay"))return "autogem.delay";if(id=="lag")return "autogem.lag";
                if(id=="specific-submode")return "autogem.specific";
                if(id.StartsWith("specific-scroll"))return "shared.scroll";
                if(id.StartsWith("specific"))return "autogem.specific_list";
            }
            if(a.StartsWith("autosnap:"))
            {
                string id=a.Substring(9);
                if(id.StartsWith("slot:")){args=new object[]{SlotName(id.Substring(5))};return "autosnap.slot";}
                if(id.StartsWith("hotkey:")){args=new object[]{SlotName(id.Substring(7))};return "autosnap.hotkey";}
                if(id=="mode:0")return "autosnap.near";if(id=="mode:1")return "autosnap.cursor";
                if(id.StartsWith("melee:"))return "autosnap.near_range";if(id.StartsWith("ranged:"))return "autosnap.cursor_range";
                var ids=new Dictionary<string,string>{{"restore","restore"},{"ignore-jug","ignore_jug"},{"ignore-minion","ignore_minions"},
                    {"hotkeys-enabled","hotkeys_enabled"},{"hotkeys-only","hotkeys_only"},{"left-stand","stand"}};
                string idKey;if(ids.TryGetValue(id,out idKey))return "autosnap."+idKey;
            }
            if(a.StartsWith("hotkeys:"))
            {
                if(a=="hotkeys:reset")return "hotkey.reset";
                if(a.StartsWith("hotkeys:key:"))
                {int n;if(int.TryParse(a.Substring(12),out n) && n>=0 && n<9)return "hotkey."+new[]{"exit","hide_hud","reload_pickit","capture","capture_with_overlay","stat_tracker","debug_overlay","save_debug_data","reset_session"}[n];}
            }
            if(a.StartsWith("tts:"))
            {
                string id=a.Substring(4).Split(':')[0];
                if(id=="add" || id=="edit" || id=="delete" || id=="hotkey" || id=="reset")return "tts."+id;
                if(id=="key")
                {
                    string key=a.Substring(8);
                    var special=new Dictionary<string,string>{{"caps","caps"},{"shift","shift"},{"backspace","backspace"},{"bksp","backspace"},{"clear","clear"},{"clr","clear"},{"done","done"}};
                    string mapped;if(special.TryGetValue(key,out mapped))return "tts."+mapped;
                    args=new object[]{key.ToUpperInvariant()};return "tts.key";
                }
            }
            if(a.StartsWith("visual:"))
            {
                if(a.StartsWith("visual:tipstoggle:"))return ParentKey(a);
                if(a.StartsWith("visual:toggle:"))return ParentKey("visual:"+a.Substring(14));
                if(a.StartsWith("visual:mapviewer:"))return a.EndsWith(":debug")?"visual.mapviewer_debug":a.EndsWith(":grid")?"visual.mapviewer":Expand(label,out args);
                if(a=="visual:xppersist:toggle")return "visual.xptracker_persist";
                if(a.StartsWith("visual:simhp:toggle:"))return a.EndsWith(":own")?"visual.simhp_own":"visual.simhp_party";
                if(a.StartsWith("visual:reset:")){args=new object[]{label};return "adjust.reset";}
                if(a.StartsWith("visual:language:")){args=new object[]{label};return "adjust.language";}
                if(a=="visual:playerbuffs:positions")return "visual.playerbuffs_reset";
                if(a=="visual:playerbuffs:hotkey")return "visual.playerbuffs_move";
                if(a.StartsWith("visual:playerbuffs:size:")){args=new object[]{label};return "adjust.size";}
                if(a.StartsWith("visual:playerbuffs:flag:"))return ParentKey(a);
                if(a=="visual:hotkey:playercoe")return "visual.playercoe_add";
                if(a=="visual:hotkey:playercoeelement")return "visual.playercoe_element";
                if(a=="visual:hotkey:tipsplayer")return "visual.playermarkers";
            }
            if(a.StartsWith("sanctifiedimpale:cadence"))return "macro.sanctified_impale_cadence";
            if(a=="sanctifiedimpale:bolasaim")return "macro.sanctified_impale_auto_aim";
            if(a=="sanctifiedmonk:option:hotkey"){args=new object[]{label};return "adjust.hotkey";}
            if(a.StartsWith("sanctifiedmonk:option:"))return a.EndsWith(":1")?"macro.sanctified_monk_auto_dash":a.EndsWith(":2")?"macro.sanctified_monk_self_dash":"macro.sanctified_monk_melee_assist";
            if(a.StartsWith("zbarb:cone"))return "macro.zbarb_autosnap_cone";
            if(a.StartsWith("riftmap:")){args=new object[]{label};return "adjust.riftmap";}
            if(a.StartsWith("autoskill:")){args=new object[]{label};return "adjust.skill_key";}
            if(a.Contains(":color:")){args=new object[]{label};return "adjust.color";}
            string adjustment=a.Contains(":transparency:")?"transparency":a.Contains(":opacity:")?"opacity":a.Contains(":width:")?"width"
                :a.Contains(":height:")?"height":a.Contains(":thick:") || a.Contains(":thickness:")?"line":a.Contains(":tone:")?"tone"
                :a.Contains(":yards:")?"yards":a.Contains(":dot:")?"dot":a.Contains(":seconds:")?"duration":a.Contains(":size:")?"size":null;
            if(adjustment!=null){args=new object[]{label};return "adjust."+adjustment;}
            if(a.Contains("expand"))return Expand(label,out args);
            if(a.Contains(":hotkey")){args=new object[]{label};return "adjust.hotkey";}
            return pk;
        }
        private static string Expand(string label,out object[] args){args=new object[]{label};return "shared.expand";}
        private static string SlotName(string value){int i;return int.TryParse(value,out i) && i>=0 && i<6?new[]{"1","2","3","4","L","R"}[i]:value;}
        private static string Local(string key,string fallback,object[] args)
        {
            string text=s7o_Localization.Get("help."+key,fallback);
            try{return args.Length==0?text:string.Format(CultureInfo.InvariantCulture,text,args);}
            catch{return args.Length==0?fallback:string.Format(CultureInfo.InvariantCulture,fallback,args);}
        }
        private void PaintTip(string action,string parent,string label,RectangleF region)
        {
            if(!Enabled || _menu==null || !_menu.HoverTipsEnabled || _menu.HoverTipsBlocked || !Hud.Window.IsForeground
                || string.IsNullOrEmpty(action)){Reset();return;}
            string identity=action+"|"+parent+"|"+label;int now=Environment.TickCount;
            if(_identity!=identity || _context!=_menu.HoverTipsContext || _language!=s7o_Localization.LanguageCode
                || Math.Abs(Hud.Window.CursorX-_anchorX)>5 || Math.Abs(Hud.Window.CursorY-_anchorY)>5)
            {
                object[] args;string key=Resolve(action,parent,label,out args);HelpText text;
                if(key==null || !Text.TryGetValue(key,out text)){Reset();return;}
                _identity=identity;_context=_menu.HoverTipsContext;_language=s7o_Localization.LanguageCode;_since=now;
                _anchorX=Hud.Window.CursorX;_anchorY=Hud.Window.CursorY;
                _title=Local(key+".title",text.Title,new object[0]);_body=Local(key+".description",text.Body,args);
                _height=0;
            }
            if(unchecked(now-_since)<Math.Max(0,HoverDelayMilliseconds))return;
            int height=Hud.Window.Size.Height,width=Hud.Window.Size.Width;if(height<100 || width<120)return;
            if(_height!=height || _screenWidth!=width)
            {
                _height=height;_screenWidth=width;float scale=Math.Max(.8f,Math.Min(1.4f,height/1080f));
                if(_fontHeight!=height)
                {
                    _fontHeight=height;
                    _titleFont=Hud.Render.CreateFont("tahoma",9.5f*scale,255,255,225,90,true,false,true);
                    _bodyFont=Hud.Render.CreateFont("tahoma",8.7f*scale,255,235,240,243,false,false,true);
                }
                _width=Math.Min(460f*scale,width-32f);
                _titleHeight=_titleFont.GetTextLayout(_title).Metrics.Height+2;
                _lineHeight=_bodyFont.GetTextLayout(_body).Metrics.Height+3;
                Wrap(_title,_titleFont,_width-24,_titleLines);Wrap(_body,_bodyFont,_width-24,_bodyLines);
                _boxHeight=22+_titleLines.Count*_titleHeight+_bodyLines.Count*_lineHeight;
            }
            float x=Hud.Window.CursorX+18,y=Hud.Window.CursorY+22;
            if(x+_width>width-8)x=Hud.Window.CursorX-_width-14;
            if(y+_boxHeight>height-8)y=Hud.Window.CursorY-_boxHeight-14;
            x=Math.Max(8,Math.Min(width-_width-8,x));y=Math.Max(8,Math.Min(height-_boxHeight-8,y));
            _shadow.DrawRectangle(x+3,y+3,_width,_boxHeight);_background.DrawRectangle(x,y,_width,_boxHeight);_border.DrawRectangle(x,y,_width,_boxHeight);
            float ty=y+9;foreach(string line in _titleLines){_titleFont.DrawText(line,x+12,ty);ty+=_titleHeight;}
            ty+=4;foreach(string line in _bodyLines){_bodyFont.DrawText(line,x+12,ty);ty+=_lineHeight;}
        }
        private static void Wrap(string text,IFont font,float width,List<string> lines)
        {
            lines.Clear();string current="";var elements=StringInfo.GetTextElementEnumerator(text??"");
            while(elements.MoveNext())
            {
                string next=elements.GetTextElement();
                if(next=="\n"){lines.Add(current.Trim());current="";continue;}
                if(current.Length>0 && font.GetTextLayout(current+next).Metrics.Width>width)
                {
                    int space=current.LastIndexOf(' ');
                    if(space>current.Length/3){lines.Add(current.Substring(0,space).Trim());current=current.Substring(space+1);}
                    else {lines.Add(current.Trim());current="";}
                }
                current+=next;
            }
            if(current.Length>0)lines.Add(current.Trim());
        }
    }
}
