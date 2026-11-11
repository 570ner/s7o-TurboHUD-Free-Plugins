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
            { "visual.minimap", new HelpText("Minimap","Toggle a movable expanded map with its hotkey. The default is the physical key left of 1. While expanded, arrow keys move it and + or - changes its zoom. The chosen color marks unexplored terrain; light outlines mark explored terrain and purple seams mark missing geometry. Entrance and exit hints appear when identified by available map data. Expand this row to change its key, color and opacity. Settings save automatically.") },
            { "visual.minimap_hotkey", new HelpText("Map hotkey","Click the key button, then press a replacement key. Escape cancels. The default physical key left of 1 may be labelled |, ~ or a backtick, depending on the keyboard.") },
            { "visual.minimap_color", new HelpText("Unexplored color","Changes unexplored outlines and fill on both maps. The final swatch restores the original cyan. Explored outlines and purple seams keep their separate colors.") },
            { "visual.minimap_opacity", new HelpText("Centered map opacity","Adjusts the whole expanded map layer, including its markers, in 5% steps. Lower values are more transparent.") },
            { "plugin.minimap", new HelpText("Minimap","Toggle a movable expanded map with its hotkey. The default is the physical key left of 1. While expanded, arrow keys move it and + or - changes its zoom. The chosen color marks unexplored terrain; light outlines mark explored terrain and purple seams mark missing geometry. Entrance and exit hints appear when identified by available map data. Expand this row to change its key, color and opacity. Settings save automatically.") },
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
            {"s7o_Minimap","minimap"},
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
                if(a.StartsWith("visual:minimap:hotkey"))return "visual.minimap_hotkey";
                if(a.StartsWith("visual:minimap:color:"))return "visual.minimap_color";
                if(a.StartsWith("visual:minimap:opacity:"))return "visual.minimap_opacity";
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
        private s7o_AutoSkill _macroOwner;
        private readonly Dictionary<string,s7o_AutoSkill.MacroDescriptor> _macroHelp=new Dictionary<string,s7o_AutoSkill.MacroDescriptor>(StringComparer.OrdinalIgnoreCase);

        // Display metadata only. Casting predicates remain in AutoSkill.
        private static readonly Dictionary<string,string> MacroDescriptions = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            { "Skill_DrinkHealthPotionArchonMeteorBuild", "For the {p:Wizard_Archon} Meteor build: drink your equipped potion outside {p:Wizard_Archon}, during the 2 seconds before the Arcane rotation. Requires {p:TheSwami}, {p:NilfursBoast}, {p:FazulasImprobableChain} and an unlocked Altar potion power. Intended for the Armor potion toughness buff; the macro does not check which potion you equipped. The normal potion macro can still respond to low health." },
            { "Skill_DrinkHealthPotion", "Drink at 40% {res:Life} or less in softcore, or 70% in Hardcore. Nearby Frozen danger raises the threshold to 80%. Unlocked Altar potion powers add casts near elites or a boss within 25 yards: the damage circle uses the configured elemental window, while damage reduction and shrine effects have their own rules. The {p:Wizard_Archon} potion macro takes priority for its build, except when this health rule is activation conditioned. Requires a ready potion." },
            { "Skill_BarbarianAncientSpear", "A build-specific {res:Fury} spender: requires Raekor 6-piece, {p:SkularsSalvation} and {p:ArreatsLaw}. Use the skill when {res:Fury} is near full and the weapon-throw animation is detected. This is not a general support pull macro. ZB AutoSnap keeps ownership of {p:Barbarian_AncientSpear} while that controller is enabled." },
            { "Barbarian_BattleRage_KeepUp", "Maintain {p:Barbarian_BattleRage}, refreshing with roughly 10–15 seconds remaining. Requires enough {res:Fury} for the resource-reduced 20-{res:Fury} base cost, plus 10 {res:Fury} left in reserve. No elite, density or low-health activation condition is required." },
            { "Barbarian_COTA_Combat", "With Immortal King 2-piece, summon the Ancients when their persistent summons are missing. Otherwise use near an on-screen elite, keywarden or boss, with cooldown-reset gear or {res:Life} at 50% or less. Refresh an expiring active buff at roughly 0.3–0.5 seconds. This is not an unconditional activate on every cooldown." },
            { "Barbarian_IgnorePain_Defensive", "The {r:Mob Rule} support rule checks party danger within 48 yards, player proximity and missing protection, including damage dealers during a Greater Rift. Its health thresholds depend on potion availability. Other runes use personal danger at 40% {res:Life} with a ready potion or 80% on cooldown; cooldown gear can also enable upkeep near enemies. {r:Bravado} has its own near-expiry refresh rule. {p:Generic_PagesBuffInvulnerable} suppresses some personal upkeep rules." },
            { "Skill_BarbarianLeap", "For Might of the Earth 4-piece without {p:LutSocks}: refresh {p:Barbarian_Leap} protection with roughly 0.8–1 second remaining when enemies are within 60 yards. Aim at your own position. Suppressed during {p:Generic_PagesBuffInvulnerable}; this is buff upkeep rather than automatic travel or enemy pursuit." },
            { "Skill_BarbarianOverPower", "{r:Momentum}: activate at 30% {res:Fury} or less with at least 5 enemies within 9 yards. {r:Killing Spree} and {r:Crushing Advance}: maintain their active buffs. The other runes activate when an enemy is within 9 yards. Ordinary cooldown checks still apply." },
            { "Skill_BarbarianRend", "Apply {p:Barbarian_Rend} near an elite or boss, or a sufficiently dense pack. Search range is 18 yards with {r:Ravage}, otherwise 12 yards. The normal density activation condition is 5 enemies, with an additional attack-phase density check. Requires the resource-reduced 20-{res:Fury} base cost plus 20 {res:Fury} in reserve. This does not validate damage or repeatedly refresh every individual enemy debuff." },
            { "Skill_BarbarianSeismicSlam", "For Immortal King 6-piece plus Raekor 4-piece. Spend {res:Fury} above 30 to refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from spending resource. Also spend when {p:Barbarian_WrathOfTheBerserker} is on cooldown and {res:Fury} is almost full. Other builds do not have a general {p:Barbarian_SeismicSlam} autocast rule." },
            { "Barbarian_Sprint_KeepUpMoving", "Normal runes: maintain {p:Barbarian_Sprint} while running, refreshing at roughly 0.1–0.3 seconds; require the reduced 20-{res:Fury} base cost plus 40 {res:Fury} in reserve. {r:Forced March}: also checks nearby party positioning, avoids the boss within 50 yards and can refresh party movement speed. Its party refresh needs a larger 70-{res:Fury} reserve. It is not an unconditional stationary buff." },
            { "Skill_BarbarianThreateningShout", "Default range: 25 yards. Normal runes activate near an elite or when at least 10 enemies are nearby; the active cooldown-reset buff can reduce that count. {r:Grim Harvest} additionally considers a nearby party member and the densest nearby pack. {p:BladeOfTheTribes} adds casts during the supported {p:Barbarian_Leap} or {p:Barbarian_SeismicSlam} animations. The toggle does not mean activate continuously without enemies." },
            { "Barbarian_WarCry_KeepUp", "Maintain {p:Barbarian_WarCry} when missing or within roughly 0.5–1 second of expiry. Extra rules support {p:ChilaniksChain} movement speed, nearby party buff upkeep, cooldown-reset gear and Frenzy builds. {r:Hardened Wrath} checks party protection separately. {p:BladeOfTheTribes} can activation condition {p:Barbarian_WarCry} during supported {p:Barbarian_Leap} or {p:Barbarian_SeismicSlam} animations. Party-distance and resource conditions can defer a movement-speed refresh." },
            { "Barbarian_WOTB_EliteOrDensity", "Refresh an expiring buff at roughly 0.3–0.5 seconds when the build can sustain it: Immortal King, high cooldown reduction or cooldown-reset gear. Otherwise use elite, elemental-window and low-health conditions instead of using the skill everywhere. Can also activate shortly before {p:Generic_PagesBuffInfiniteCasting} ends. Some Raekor/Frenzy builds have an extra elemental timing rule." },
            { "Crusader_AkaratsChampion_Combat", "Needs an enemy within 100 yards. General rules consider cooldown reduction, Akkhan/cooldown gear, {res:Wrath} below 20, elites within 40 yards and {r:Prophet} danger. Valor builds using {p:Crusader_HeavensFury} or {p:Crusader_FistOfTheHeavens} have separate elemental timing rules. {p:Crusader_AkaratsChampion} is not simply activate on every cooldown in every build. {p:Crusader_SteedCharge} can defer the activation." },
            { "Skill_CrusaderBlessedShield", "Refresh the existing {p:Crusader_BlessedShield} stacking buff near expiry, at roughly 1–1.2 seconds, once the detected count reaches 100. Requires a target within 50 yards and aims at that target. This is a narrow stack-maintenance rule, not a complete {p:Crusader_BlessedShield} combat controller." },
            { "Crusader_Bombardment_EliteOrDensity", "The thorns rule uses {r:Barrels of Spikes} near enemies within 30 yards, timed to Physical {p:ConventionOfElements}. Akkhan and Invoker builds have additional set-specific rules; the Akkhan combination also coordinates {p:TheMortalDrama}, {p:Crusader_IronSkin} and {r:Crusader_AkaratsChampion:Rally}. Defers during {p:Crusader_SteedCharge}. Equipping {p:Crusader_Bombardment} alone does not enable every rule." },
            { "Skill_CrusaderCondemn", "Use the skill when enemies are within the calculated close-range area, allowing for your movement speed. {p:FrydehrsWrath} extends the {r:Vacuum} check. Requires the skill cost; the {p:Crusader_Phalanx} support combination with {p:UnrelentingPhalanx} keeps an additional 40 {res:Wrath}. Defers during {p:Crusader_SteedCharge}. This rule does not automatically move you into a pack." },
            { "Skill_CrusaderFallingSword", "Requires {p:FaithfulMemory} and an enemy within 14 yards of the cursor. Use the skill when its stacking buff is missing or has about 2–2.3 seconds remaining. Defers during {p:Crusader_SteedCharge}. The landing location follows the cursor, so keep it over the intended pack." },
            { "Skill_CrusaderFistoftheHeavens", "Valor builds refresh the defensive stack buff when below 50 stacks or 0.5 seconds remaining; with {p:Crusader_HeavensFury} equipped, also maintain the 3-stack damage buff. Another supported set rule refreshes the ring damage buff from spending resource. Requires an enemy within 20 yards of the cursor and enough {res:Wrath}. Defers during {p:Crusader_SteedCharge}." },
            { "Crusader_IronSkin_Defensive", "Normal runes respond to danger near enemies, at 60% {res:Life} with a ready potion or 80% on cooldown; cooldown gear can enable regular upkeep. {r:Reflective Skin} has thorns-specific rules for {p:TheMortalDrama}, Physical {p:ConventionOfElements}, Akkhan/{r:Crusader_AkaratsChampion:Rally} and Invoker. Defers during {p:Crusader_SteedCharge}. The thorns timing rule is not a universal low-health shield." },
            { "Skill_CrusaderJudgment", "Without Akkhan 2-piece, look for an elite or goblin without the {p:Crusader_Judgment} debuff within 20 yards of the cursor. With Akkhan 2-piece, the combat rule can use other eligible enemies there while you attack. Defers during {p:Crusader_SteedCharge}. Place the cursor over the group you intend to control." },
            { "Skill_CrusaderJustice", "Refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from a resource-generating attack when it has about 1.5 seconds remaining. Requires an enemy in the forward attack sector: normally 30 yards, or 70 with {r:Crack}. Defers during {p:Crusader_SteedCharge}. This is ring-buff upkeep rather than continuous generator automation." },
            { "Crusader_LawsOfHope_Defensive", "Use for immediate detected danger, low personal {res:Life} or a threatened party member within 48 yards. Personal thresholds are 70% {res:Life} with a ready potion, 80% on cooldown; party checks use their own potion and movement state. With an enemy within 100 yards, sufficient cooldown reduction or reset gear can maintain the active law near expiry. {p:SquirtsNecklace} adds a shielding rule. Does not simply spam every rune constantly." },
            { "Crusader_LawsOfJustice_Defensive", "Use for detected danger, personal {res:Life} at 70% with a ready potion or 80% on cooldown, or a threatened party member within 48 yards. Additional upkeep near enemies within 100 yards requires enough cooldown reduction or cooldown-reset gear, refreshing at roughly 0.1–0.5 seconds. The active law and its passive aura are separate effects." },
            { "Crusader_LawsOfValor_Elite", "General upkeep needs cooldown reduction or reset gear and refreshes the active law at roughly 0.2–0.5 seconds. {r:Frozen in Terror} has a separate nearby-control rule. Valor 6-piece with {p:Crusader_HeavensFury}/{r:Crusader_HeavensFury:Fires of Heaven}, {p:FateOfTheFell} and {p:ConventionOfElements} uses build-specific Holy timing. Defers during {p:Crusader_SteedCharge}. It is not a universal elite-only activate." },
            { "Skill_CrusaderPhalanx", "For Akkhan 2-piece: activate while attacking, outside {p:Crusader_SteedCharge}. Requires the ordinary skill/resource checks. This is the set-support rule, not a universal summon-count controller for every {p:Crusader_Phalanx} build." },
            { "Crusader_Provoke_ResourceOrDensity", "Needs an enemy within 100 yards. Can activate for high cooldown reduction, danger at 70% {res:Life} with a ready potion or 80% on cooldown, or enough missing {res:Wrath} for 30 plus 5 per nearby enemy within 10 yards. The Invoker/{p:Crusader_Bombardment} combination coordinates {r:Hit Me} with Reflective Skin. Defers during {p:Crusader_SteedCharge}." },
            { "Skill_CrusaderShieldGlare", "Look for an eligible unblinded elite, boss or goblin within 30 yards, excluding Juggernauts and rare minions. Normally requires the target in your facing sector; {p:TheFinalWitness} removes that facing requirement. With {p:ConventionOfElements}, wait for the configured damage window. This is targeted crowd-control upkeep, not a blind activate without a suitable enemy." },
            { "Skill_CrusaderSteedCharge", "Do not recast while already mounted. {r:Draw and Quarter} is disabled by the default policy. {r:Spiked Barding} supports held Force Move, with an exception for the Darklight/{p:Crusader_FistOfTheHeavens} setup. Norvald builds refresh their damage buff near eligible combat targets. The Darklight setup has separate short remount timing. Other runes do not imply continuous travel automation." },
            { "Skill_DemonHunterChakramShurikenCloud", "Only {r:Shuriken Cloud}. Maintain the persistent cloud, refreshing with roughly 20–50 seconds remaining. Requires the resource-reduced 10-{res:Hatred} base cost plus 70 {res:Hatred} in reserve. Other {p:DemonHunter_Chakram} runes have no autocast rule here." },
            { "Skill_DemonHunterBolas", "Refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from a resource-generating attack when an enemy is in the attack sector and the buff is about to expire. With {p:WrapsOfClarity}, also refresh its defensive buff when an enemy is within 200 yards; {p:Generic_PagesBuffInvulnerable} suppresses that rule. DHStrafe keeps ownership of generators while its support controller is running. This is periodic buff upkeep, not a full targeting controller. The ring-buff sector reaches 15 yards, or 30 with {r:Freezing Strike}." },
            { "Skill_DemonHunterEntanglingShot", "Refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from a resource-generating attack when an enemy is in the attack sector and the buff is about to expire. With {p:WrapsOfClarity}, also refresh its defensive buff when an enemy is within 200 yards; {p:Generic_PagesBuffInvulnerable} suppresses that rule. DHStrafe keeps ownership of generators while its support controller is running. This is periodic buff upkeep, not a full targeting controller. The ring-buff sector reaches 15 yards." },
            { "Skill_DemonHunterHungeringArrow", "Refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from a resource-generating attack when an enemy is in the attack sector and the buff is about to expire. With {p:WrapsOfClarity}, also refresh its defensive buff when an enemy is within 200 yards; {p:Generic_PagesBuffInvulnerable} suppresses that rule. DHStrafe keeps ownership of generators while its support controller is running. This is periodic buff upkeep, not a full targeting controller. The ring-buff sector reaches 40 yards." },
            { "Skill_DemonHunterCaltrops", "Natalya 2-piece with {p:DemonHunter_SpikeTrap}: refresh the trap-related buff during the supported {p:DemonHunter_SpikeTrap} animation or near an elite within 12 yards. Also has a combat rule during the {p:DemonHunter_Impale} animation with an enemy within 60 yards. Defers while {p:DemonHunter_Vault} is held. This is build-specific support, not a general ground-trap rotation." },
            { "DH_Companion_Bat_Hatred", "Only {r:Bat Companion}: activate to restore {res:Hatred} when it falls below 15. Very high cooldown reduction or {p:ObsidianRingOfTheZodiac} can activate it earlier through the shared Companion rule. Other Companion runes belong to the separate non-Bat toggle. This threshold is a flat {res:Hatred} value, not a percentage." },
            { "DH_Companion_EliteOrBoss", "All Companion runes except {r:Bat Companion}. Wolf/Marauder rules consider elite proximity and your or nearby damage dealers’ elemental windows. Spider, Boar and the unrunned skill use nearby elites; Ferret has a low-{res:Life}/health-globe rule. Very high cooldown reduction or {p:ObsidianRingOfTheZodiac} can also activation condition activation. Uses the equipped rune rather than using the skill every companion type separately." },
            { "Skill_DemonHunterEvasiveFire", "Only {r:Focus} or {r:Hardened}. Maintain {p:Generic_ItemPassiveUniqueRing735x1} and {p:WrapsOfClarity}, or generate resource below 20% {res:Hatred}; Unhallowed Essence 2-piece also checks {res:Discipline} below 33%. {p:Generic_PagesBuffInfiniteCasting} suppresses the resource rules; {p:Generic_PagesBuffInvulnerable} suppresses bracer upkeep. With Natalya 2-piece and Spike Trap using {r:DemonHunter_SpikeTrap:Custom Trigger}, activate after a full batch of your traps is placed. Trap-trigger attempts are at least 500 ms apart. DHStrafe retains control while running." },
            { "Skill_DemonHunterFanofKnives", "{r:Bladed Armor}: refresh the armor buff with about 0.1–0.2 seconds remaining when an enemy is within 40 yards, or cooldown reduction exceeds 40%. {r:Knives Expert}: with Natalya 6-piece, activate after a full batch of your traps is placed, provided Spike Trap does not use {r:DemonHunter_SpikeTrap:Custom Trigger}. This branch requires enough {res:Hatred} and at least 500 ms between attempts. Other runes have no general autocast rule here." },
            { "Skill_DemonHunterImpale", "For {p:ChainOfShadows}: refresh its buff near expiry when {p:DemonHunter_Vault} is equipped and {res:Discipline} is below 90%. Requires that you are not already in the attacking animation. This supports the movement-cost buff; it is not the {p:DemonHunter_Impale} combat controller and does not replace that plugin’s targeting." },
            { "Skill_DemonHunterMarkedforDeath", "{r:Valley of Death}: look for an eligible unmarked elite, boss, goblin or keywarden within 10 yards of the cursor. Other runes use the selected eligible unmarked target. Requires enough {res:Discipline}. A target already carrying the detected mark does not activation condition another activate." },
            { "Skill_DemonHunterMultishot", "Only {r:Wind Chill}, with {p:IceblinkSecondary}. Use the skill toward the forward attack area when it contains at least 10 enemies without the detected cold debuff, or a qualifying unchilled elite, boss, goblin or keywarden. Excludes Juggernauts and invulnerable/invisible targets. Requires enough {res:Hatred}. This is debuff application, not a general {p:DemonHunter_Multishot} damage rotation." },
            { "DH_Preparation_DisciplineOrLife", "For all runes except {r:Punishment}. Use the skill when at least 30 {res:Discipline} is missing. {r:Battle Scars} also casts at 40% {res:Life} or less for healing, even before that {res:Discipline} deficit. Use the separate {res:Hatred} toggle for Punishment; these two entries intentionally have different resource rules." },
            { "DH_Preparation_Punishment_Hatred", "Only {r:Punishment}. Use the skill when at least 75 {res:Hatred} is missing so its restoration is not mostly wasted. This is a flat deficit from maximum {res:Hatred}, not a low-resource percentage or a {res:Discipline} activation condition." },
            { "Skill_DemonHunterSentry", "Requires a usable {p:DemonHunter_Sentry} charge. {r:Polar Station} with {p:BaneOfTheTrappedPrimary} can place a sentry near an elite that lacks the detected control effect. Otherwise use an elite within 60 yards while your nearby sentry count is below the build cap: 2 normally, 3 with {p:DemonHunter_Passive_CustomEngineering}, plus 2 with {p:BombardiersRucksack}. Placement follows the cursor." },
            { "DH_ShadowPower_Defensive", "Maintain the permanent effect with Shadow 4-piece. Otherwise activate for severe personal danger, low {res:Life}, nearby elites or specific cooldown/resource builds. Unhallowed Essence/Gears of Dreadlands with {p:DemonHunter_Preparation} have movement and enemy-proximity rules; some Natalya {p:DemonHunter_SpikeTrap} setups also have a combat rule. Elusive Ring upkeep is controlled by its separate toggle. Does not unconditionally spam {p:DemonHunter_ShadowPower}." },
            { "DH_ShadowPower_ElusiveRing", "Requires active {p:ElusiveRing}, an enemy within 40 yards and the ring buff missing or within 0.1 seconds of expiry. Then apply the shared {p:DemonHunter_ShadowPower} conditions and {res:Discipline} cost. This toggle enables the ring-maintenance rule separately from the general defensive {p:DemonHunter_ShadowPower} rule." },
            { "Skill_DemonHunterSmokeScreen", "Skip while {p:DemonHunter_SmokeScreen} is already active. Respond to danger at 60% {res:Life} with a ready potion or 80% on cooldown near enemies; also refresh {p:ElusiveRing} near expiry with enemies within 80 yards. {r:Vanishing Powder}, {r:Displacement} and {r:Special Recipe} have additional cooldown-reduction, {p:DemonHunter_Preparation} or movement rules. Natalya {p:DemonHunter_SpikeTrap} has a separate combat rule. Requires enough {res:Discipline}." },
            { "DH_Vengeance_KeepCombatBuff", "Needs an enemy within 100 yards. Low-{res:Life} runes, very high cooldown reduction, {p:Dawn}, cooldown-reset gear and elemental timing decide when to refresh. {r:Seethe} also has a low-{res:Hatred} rule. Typical refresh checks are near buff expiry, with stricter gear/elite conditions in some builds. This is not a promise of unconditional {p:DemonHunter_Vengeance} uptime with insufficient cooldown reduction." },
            { "DH_Vengeance_HatredBurst", "Only {r:Seethe}, and only when {res:Hatred} is below 30. The shared {p:DemonHunter_Vengeance} policy must also pass, including an enemy within 100 yards and its cooldown/buff conditions. A separate Keep Combat Buff toggle handles broader upkeep. This entry uses flat {res:Hatred}, not 30% of maximum." },
            { "Skill_MonkBlindingFlash", "Use the skill while fighting, not while running or standing still without attacking. Use against an elite within 20 yards or at least 5 enemies there, but that rule skips a nearby boss. {p:TheLawsOfSeph} adds a resource activation condition below 20% {res:Spirit}. Patterns of Justice 6-piece with {p:ConventionOfElements} adds elemental timing. Other rules can still apply when the elite/density rule is skipped." },
            { "Skill_MonkBreathOfHeaven", "Heal at 70% {res:Life} or less, or for a threatened party member within 12 yards. {r:Infused with Light} can activate without waiting for low {res:Spirit}. {r:Blazing Wrath} refreshes its damage buff while fighting an elite or boss within 40 yards. {r:Zephyr} has a high-cooldown-reduction movement-buff rule. These are rune-specific activation conditions, not one shared low-resource rule." },
            { "Skill_MonkCripplingWave", "For Raiment 6-piece plus Shenlong 2-piece. During the {p:Monk_WayOfTheHundredFists} attack animation, insert {p:Monk_CripplingWave} when its {p:Monk_Passive_CombinationStrike} contribution has less than 1 second remaining. GenMonk keeps ownership of generators when enabled for the current build. This is combo-buff maintenance, not a full attack rotation by itself." },
            { "Skill_MonkCycloneStrike", "Requires enough {res:Spirit} and nearby enemies. Maintain {p:LefebvresSoliloquy} protection near expiry; {r:Soothing Breeze} also has a fighting rule. {p:BindingsOfTheLesserGods} casts when suitable enemies lack its detected debuff, normally within 24 yards or 34 with {r:Implosion}, while retaining 100 {res:Spirit}. Supported {p:FlyingDragon}/Wave of Light setups and a season-specific bell rule add separate rules. This is not unconditional pull spam." },
            { "Skill_MonkDashingStrike", "For {r:Blinding Speed} or {r:Radiance}, refresh the short buff near expiry when a boss is within 20 yards and the cursor is close enough; aim at your position. Raiment 6-piece plus Shenlong 2-piece adds a combat-buff refresh during generator attacks. GenMonk keeps ownership when enabled. This contains no experimental {p:FlyingDragon} dash-spam burst." },
            { "Skill_MonkDeadlyReach", "For Raiment 6-piece plus Shenlong 2-piece, insert this generator during the supported {p:Monk_WayOfTheHundredFists}/{p:Monk_CripplingWave} animations. Refresh its {p:Monk_Passive_CombinationStrike} contribution below 1 second; {r:Foresight} also refreshes its damage buff. GenMonk keeps ownership when enabled. No automatic travel or independent pack selection is provided." },
            { "Monk_Epiphany_Combat", "Maintain {p:Monk_Epiphany} when its buff is missing or has roughly 0.5–1 second remaining, with an enemy within 100 yards. Does not activate while standing still without attacking; moving or holding an attack can qualify. No scripted low-{res:Spirit} activation condition is required. Normal cooldown checks still apply." },
            { "Skill_MonkFistsOfThunder", "For Raiment 6-piece plus Shenlong 2-piece, insert {p:Monk_FistsOfThunder} during the supported {p:Monk_WayOfTheHundredFists}/{p:Monk_CripplingWave} animations when its {p:Monk_Passive_CombinationStrike} contribution has less than 1 second remaining. GenMonk keeps ownership when enabled. This maintains the combo bonus rather than choosing targets for you." },
            { "Skill_MonkInnerSanctuary", "Use the skill while fighting, not while running or standing still without attacking, with an elite/boss within 50 yards or at least 5 enemies there. Damage builds detected through {p:BaneOfTheTrappedPrimary} wait until the existing Sanctuary buff is absent. Support builds may recast when ready. This does not check that every party member stands inside the area." },
            { "Monk_MantraConviction_Elite", "Needs an enemy within 100 yards and enough {res:Spirit} for the reduced 50-{res:Spirit} base cost plus 50 {res:Spirit} in reserve. Outside a state of standing still without attacking, activate when {p:Monk_Epiphany} is active or an elite, goblin, keywarden or boss is detected. Do not duplicate the detected active Mantra effect. This maintains the active damage effect rather than merely the passive aura." },
            { "Monk_MantraSalvation_Defensive", "Maintain the 3-second active protection while moving, attacking or standing still. Check yourself and loaded, living party members within 60 horizontal yards in the same world; renew missing protection or about 0.3 seconds before expiry. Requests are spaced at least 2.7 seconds apart rather than spammed. No extra {res:Spirit} reserve. Stop in town and after a rift guardian is dead. The passive aura alone does not satisfy this rule." },
            { "Monk_MysticAlly_ResourceOrElite", "{r:Air Ally}: activate when more than 200 {res:Spirit} is missing. Inna 6-piece {r:Water Ally} also uses this test. {r:Enduring Ally}: activate below 30% {res:Life}. Inna 6-piece {r:Water Ally}/{r:Earth Ally} needs an elite or at least 2 enemies within 20 yards and enough detected allies. {r:Fire Ally} also checks {p:BindingsOfTheLesserGods} and Fire elemental timing when applicable. The damage rules do not activate while running or standing still without attacking. Ally counts depend on Inna, {p:TheCrudestBoots} and {p:Generic_PagesBuffInfiniteCasting}." },
            { "Monk_Serenity_Defensive", "Do not recast while {p:Monk_Serenity} is active. Near enemies within 60 yards, respond at 60% {res:Life} with a ready potion or 80% on cooldown. {r:Tranquility} also checks threatened party members within 45 yards and high-cooldown-reduction support uptime. {p:SquirtsNecklace} and cooldown-reset gear add personal upkeep rules, with {p:Generic_PagesBuffInvulnerable} and {p:ConventionOfElements} exclusions. This is more than a single low-health threshold." },
            { "Monk_SweepingWind_KeepUp", "Maintain {p:Monk_SweepingWind}, refreshing with roughly 0.3–1 second remaining. Requires the resource-reduced 75-{res:Spirit} base cost plus 15 {res:Spirit} in reserve. This is persistent-buff upkeep; it does not require an elite or automatically move into range to build stacks." },
            { "Skill_MonkWayOfTheHundredFists", "For Raiment 6-piece plus Shenlong 2-piece, weave this generator from the {p:Monk_CripplingWave} animation. Refresh its {p:Monk_Passive_CombinationStrike} contribution below 1 second; {r:Blazing Fists} also checks its 3 stacks and {r:Assimilation} its damage-buff duration. GenMonk keeps ownership when enabled. This is the imported combo helper, not a replacement for GenMonk’s combat controller." },
            { "Monk_MantraHealing_Defensive", "Maintain the active shield while moving or standing still, refreshing it when missing, damaged or close to expiry. Repeatedly activate while attacking or channeling, and during {p:Generic_PagesBuffInfiniteCasting}, to help protect {p:SquirtsNecklace}. No extra {res:Spirit} reserve during combat, movement or the pylon; idle upkeep keeps the configured reserve, normally 35% of maximum {res:Spirit} plus the skill cost. Stop in town and after a rift guardian is dead. GenMonk and AutoSkill coordinate ownership." },
            { "Skill_NecArmyoftheDead", "Skipped with {p:FuneraryPick}. Use the skill near a qualifying elite/goblin by the cursor, a sufficiently valuable dense pack in the elemental window, or supported Grim Scythe/{p:Necromancer_SiphonBlood} attack animations. Rathma 2-piece also considers an incomplete {p:Necromancer_Revive} army near a pack. Cursor ranges vary by rule and rune. This is a conditional damage rule, not an activation whenever the cooldown ends." },
            { "Skill_NecBloodRush", "A narrow boss helper: requires {p:Necromancer_BoneSpear}, {p:Necromancer_Devour} with {r:Necromancer_Devour:Voracious}, a boss within 50 yards and the cursor’s world position within 50 yards. Refresh the detected {p:Necromancer_Devour} buff below 1 second by aiming {p:Necromancer_BloodRush} at your own position. This is not an automatic travel or emergency-escape controller." },
            { "Necro_BoneArmor_NearEnemies", "Use the skill near enemies within 30 yards to build or refresh {p:Necromancer_BoneArmor} stacks; {p:WisdomOfKalan} changes the stack target and required nearby count. The normal rule excludes {r:Limited Immunity}; {r:Thy Flesh Sustained} is suppressed below 50% {res:Life}. {r:Dislocation} with {p:KrysbinsSentence} and {p:ConventionOfElements} has separate timing rules. Inarius, mage gear and high cooldown reduction add build-specific rules." },
            { "Necro_CommandSkeletons_Elite", "Not for {r:Kill Command}. Requires enough {res:Essence} and an eligible selected target. Command a boss when the command buff is missing; Jesseth 2-piece and {p:BloodsongMail} have additional upkeep rules. {p:ObsidianRingOfTheZodiac} can support repeats when the cost is low enough, and {p:Necromancer_ArmyOfTheDead} cooldown can affect target selection. This does not continuously retarget every visible monster." },
            { "Skill_NecDecrepify", "Curse an eligible elite, boss, goblin or keywarden without {p:Necromancer_Decrepify} within 19 yards of the cursor, accounting for its hitbox. Exclude Juggernauts except with {r:Dizzying Curse}. Requires enough {res:Essence}. The macro checks the detected curse debuff; it does not blindly recast on every enemy in view." },
            { "Necro_Devour_CorpseOrLotD", "Not for {r:Devouring Aura}. Needs combat context or an enemy within 60 yards. During {p:Necromancer_LandOfTheDead}, activate without requiring a visible corpse; otherwise require a corpse within 60 yards and {res:Essence} below maximum. {r:Cannibalize} adds health-based corpse use. Pestilence 4-piece also consumes corpses to maintain its defensive stacks or refresh them below 2 seconds. Other skill owners keep their input priority." },
            { "Skill_NecFrailty", "Not for {r:Aura of Frailty}. Curse a qualifying elite, boss, goblin or keywarden within 18 yards of the cursor when below 15% {res:Life}. {r:Early Grave} raises that threshold to 18% but requires your {res:Life} at least 30%. Requires enough {res:Essence}. This is an execution-focused activate rather than continuous curse maintenance at full enemy health." },
            { "Necro_LandOfTheDead_EliteOrDensity", "Can activate shortly before {p:Generic_PagesBuffInfiniteCasting} ends. Without {p:HauntedVisions}, the general timing rule casts during the first second of {p:Necromancer_Simulacrum}. Pestilence 6-piece with {p:Necromancer_Passive_BloodIsPower} and cooldown-reset gear refreshes near expiry: at 60% cooldown reduction or less it requires an elite/boss within 60 yards; above that, any enemy there can qualify. {p:MesserschmidtsReaver} adds an enemy-within-100-yards rule." },
            { "Skill_NecLeech", "Requires an unlocked Altar potion power, enough {res:Essence} and an eligible elite, boss, goblin or keywarden without {p:Necromancer_Leech} within 19 yards of the cursor. Intended for {r:Blood Flask} potion support, but the current imported rune check does not exclude other runes. This description reflects that limitation; the toggle does not directly drink the potion." },
            { "Necro_NayrsBlackDeath", "One shared toggle maintains poison-skill contributions to {p:NayrsBlackDeath}. Supports the configured poison runes of {p:Necromancer_BoneArmor}, {p:Necromancer_BoneSpear}, {p:Necromancer_BoneSpikes}, {p:Necromancer_CommandSkeletons}, {p:Necromancer_DeathNova}, {p:Necromancer_Revive}, {p:Necromancer_SiphonBlood} and {p:Necromancer_SkeletalMage}. Each rule checks its own contribution, resource/corpse requirements and elemental timing, typically refreshing near expiry. Force Move can suppress these casts. Only equipped supported poison variants participate; this is not a general rotation for those eight skills." },
            { "Skill_NecRevive", "For Rathma 2-piece. Raise a corpse within 20 yards of the cursor while your detected {p:Necromancer_Revive} count is below 10. Requires enough {res:Essence} and an eligible corpse. This is army upkeep, not a damage-targeting controller or an instruction to recast at the cap." },
            { "Necro_Simulacrum_EliteOrDensity", "With {p:HauntedVisions}, restore missing Simulacrums, accounting for Blood and Bone or the Masquerade 2-piece pair. Otherwise coordinate with a recent {p:Necromancer_LandOfTheDead} activate or its cooldown and nearby enemies within 50 yards. With the Trag’Oul 4-piece health buff present, wait until its detected count reaches 300. This does not blindly recast existing permanent copies." },
            { "Skill_NecSiphonBlood", "Require a selected target, no held Force Move and no running animation. Maintain the detected {p:Necromancer_SiphonBlood} stacking effect until it has 10 stacks and sufficient duration. Intended for {r:Power Shift}, but the current imported rune check does not exclude other runes. Pestilence RGK keeps ownership when Auto Siphon is enabled. This is not independent boss selection." },
            { "Necro_SkeletalMage_EssenceHigh", "{r:Skeleton Archer} has a separate boss-maintenance rule without {p:RazethsVolition}. {r:Singularity} or the Razeth combination uses enemies within 55 yards, existing mage count and high {res:Essence}, with different checks during {p:Necromancer_LandOfTheDead}, {p:Necromancer_Simulacrum} or rapid recovery. Keep enough {res:Essence} for the reduced base cost. Near-expiry buffs, {p:AquilaCuirass} and priority targets can alter the decision. It does not simply spend all {res:Essence} on every cooldown." },
            { "WD_BigBadVoodoo_EliteOrDensity", "With {p:StarmetalKukri}, require more than 3 fetishes and an elite within 40 yards. Mundunugu with {p:WitchDoctor_Passive_GraveInjustice} refreshes the active buff near expiry. High cooldown reduction plus that passive can also activate near an elite within 20 yards if no owned Voodoo area is within 30 yards. This is gear-dependent uptime, not unconditional ground placement." },
            { "Skill_WitchDoctorCorpseSpiders", "Use the skill when your own detected {p:WitchDoctor_CorpseSpider} summon is missing. Existing owned spiders suppress another activate. This is summon-presence upkeep rather than a continuous damage rotation or an enemy-density selector." },
            { "WD_FetishArmy_Combat", "Restore the detected army when its count is below the rune’s target: 8 for {r:Legion of Daggers}, 7 for {r:Tiki Torchers}/{r:Head Hunters}, otherwise 5. The default rule counts matching detected army actors, which can include other players’ summons. Support-role rules also consider movement and rift entry. This is not a precise per-player lifetime timer." },
            { "WD_Gargantuan_KeepUp", "Summon when no matching {p:WitchDoctor_Gargantuan} actor is detected, or activate near an elite/boss within 12 yards of the cursor. The presence check uses detected {p:WitchDoctor_Gargantuan} actors rather than a strict owned-only count. This means another player’s summon can affect upkeep; it is not a guaranteed personal pet-count controller." },
            { "Skill_WitchDoctorHex", "For Arachyr 4-piece. Refresh the set-related {p:WitchDoctor_Hex} effect at roughly 0.5–1 second remaining, except for the detected Stricken role. Other builds do not have a general {p:WitchDoctor_Hex}-on-cooldown rule here." },
            { "WD_Horrify_Defensive", "Use the skill for an elite or a dense nearby pack: normally 5 enemies, or 1 with {p:RechelsRingOfLarceny}. Range is normally 18 yards, or 24 with {r:Face of Death}. {p:ObsidianRingOfTheZodiac} adds a nearby-enemy rule. {r:Frightening Aspect} separately refreshes its armor buff near expiry with an enemy within 50 yards. This is not limited to low {res:Life}." },
            { "Skill_WitchDoctorLocustSwarm", "For {p:RingOfEmptiness} without {p:Wormwood}. Find an eligible elite within 20 yards that lacks the detected {p:WitchDoctor_LocustSwarm} debuff and aim at it. Requires enough {res:Mana}. Existing debuffs suppress redundant casts; this is not a general trash-spreading controller." },
            { "Skill_WitchDoctorMassConfusion", "Use the skill near an elite within 20 yards or when at least 5 enemies are within 20 yards. Ordinary cooldown and safety checks apply. This rule does not require low {res:Life} or check every party member before using the control skill." },
            { "Skill_WitchDoctorPiranhas", "Use near an elite within 12 yards of the cursor, or while attacking with at least 3 enemies in that cursor area. Requires enough {res:Mana}. This placement follows your cursor; the macro does not automatically move the pack or your character." },
            { "Skill_WitchDoctorSacrifice", "Only {r:Provoke the Pack}. Outside the running animation, activate near an elite within 12 yards of the cursor while the detected damage-buff count is below 5. Other runes have no general Sacrifice automation here." },
            { "Skill_WitchDoctorSoulHarvestJadeHarvester", "For Jade Harvester 6-piece. Harvest enemies within 18 yards that carry the required {p:WitchDoctor_Haunt}/{p:WitchDoctor_LocustSwarm} effects, accounting for {p:RingOfEmptiness}. Also build missing {p:WitchDoctor_SoulHarvest} stacks toward 5, or 10 with {p:SacredHarvester}, and refresh near expiry. This damage-focused rule is separate from the normal defensive {p:WitchDoctor_SoulHarvest} toggle." },
            { "WD_SoulHarvest_NearEnemies", "Within 18 yards, activate when at least 3 enemies are present, an elite is nearby, or the nearby count is enough to preserve/improve the current stacks. The comparison uses at least 2 enemies when there are fewer current stacks. This is defensive stack upkeep; Jade damage harvesting has a separate toggle." },
            { "Skill_WitchDoctorSpiritBarragePhantasm", "{r:Phantasm}: keep up to 3 owned phantasms within 60 yards, unless Force Move is held. {r:Manitou}: refresh its buff near expiry, roughly 0.3–1 second. Both rules require the resource-reduced 100-{res:Mana} base amount plus 200 {res:Mana} in reserve. Other {p:WitchDoctor_SpiritBarrage} runes have no automatic upkeep here." },
            { "WD_SpiritWalk_Defensive", "Mundunugu 4-piece refreshes {p:WitchDoctor_SpiritWalk} protection with roughly 0.5–1 second remaining. {r:Severance} with {p:ShukranisTriumph} has a separate near-expiry rule. This is build-specific buff upkeep, not an unconditional low-health escape or pathfinding routine." },
            { "Skill_WitchDoctorWallOfDeath", "Helltooth 6-piece: maintain the set buff near expiry with an enemy within 200 yards. {r:Communing with Spirits} also checks eligible elites within 19 yards of the cursor that lack the detected debuff, excluding Juggernauts. Requires enough {res:Mana}. The two rules serve different purposes: set upkeep and targeted control." },
            { "WD_ZombieDogs_KeepUp", "Summon when your owned Zombie Dog count is below the configured target, normally 3. With {p:TheTallMansFinger}, the target becomes 1 large dog. Existing owned dogs suppress redundant summons. This is summon-count upkeep, not repeated using the skill simply because enemies are nearby." },
            { "Skill_WizardArcaneOrb", "Only {r:Arcane Orbit}. Recast when the detected orbiting-orb buff is absent. Other {p:Wizard_ArcaneOrb} runes have no general damage-spam rule here. {p:Triumvirate} stacks are handled by separate generator toggles." },
            { "Skill_WizardArchonArcaneBlastCold", "Use the matching {p:Wizard_Archon} form’s close-range blast when an enemy is within 18 yards and the transformed skill is ready. This checks the active {p:Wizard_Archon} skill, not your normal action-bar spell. It does not move you into range or activate {p:Wizard_Archon} itself." },
            { "Skill_WizardArchonArcaneBlastFire", "Use the matching {p:Wizard_Archon} form’s close-range blast when an enemy is within 18 yards and the transformed skill is ready. This checks the active {p:Wizard_Archon} skill, not your normal action-bar spell. It does not move you into range or activate {p:Wizard_Archon} itself." },
            { "Skill_WizardArchonArcaneBlastLightning", "Use the matching {p:Wizard_Archon} form’s close-range blast when an enemy is within 18 yards and the transformed skill is ready. This checks the active {p:Wizard_Archon} skill, not your normal action-bar spell. It does not move you into range or activate {p:Wizard_Archon} itself." },
            { "Skill_WizardArchonArcaneBlast", "Use the matching {p:Wizard_Archon} form’s close-range blast when an enemy is within 18 yards and the transformed skill is ready. This checks the active {p:Wizard_Archon} skill, not your normal action-bar spell. It does not move you into range or activate {p:Wizard_Archon} itself." },
            { "Wizard_Archon_EliteOrDensity", "Requires an enemy within 100 yards and the detected {p:Wizard_Archon} stack state used by this build. The rule compares nearby cursor density with {p:Wizard_BlackHole} stacks and can request {p:Wizard_BlackHole} before entering {p:Wizard_Archon}. This is a stack-aware {p:Wizard_Archon} helper rather than a simple elite-only or cooldown-only activation condition; missing required build state can defer transformation." },
            { "Skill_WizardArchonSlowTime", "During {p:Wizard_Archon}, activate the transformed Slow Time effect when its detected buff is missing. Normal cooldown and input scheduling apply. This does not manage the ordinary Slow Time spell or enter {p:Wizard_Archon} for you." },
            { "Skill_WizardBlackHole", "With {p:ConventionOfElements}, use the configured elemental lead time and require a nearby elite plus a worthwhile cursor-area stack opportunity. {p:StoneOfJordan} and Firebird/Deathwish combinations alter that rule. Without that timing rule, {r:Spellsteal} and {r:Absolute Zero} use elite proximity and stack/expiry checks. Place the cursor over the intended group; this is not random area placement." },
            { "Skill_WizardBlizzard", "Requires {p:WinterFlurry} and a Hydra near the cursor. Use the skill where suitable nearby enemies are not already covered by the detected {p:Wizard_Blizzard} effect. {r:Apocalypse} uses larger search areas than the other runes. Requires enough {res:ArcanePower}. This supports Hydra damage rather than maintaining {p:Wizard_Blizzard} everywhere on screen." },
            { "Wizard_DiamondSkin_Defensive", "High cooldown reduction with {r:Enduring Skin} or cooldown-reset gear can enable frequent casts; {p:Ingeom} also supports near-expiry upkeep. {r:Prism} and {r:Enduring Skin} have attack/channel conditions, while {r:Sleek Shell} supports running. Some {p:Wizard_Teleport} states are excluded unless {p:AetherWalker} is active. This is rune/build-dependent protection, not one universal low-{res:Life} activation condition." },
            { "Skill_WizardDisintegrate", "For Firebird 6-piece. Build or maintain the configured Combustion state and, with {p:TaegukPrimary}, refresh the channeling buff near expiry in relevant combat/pylon contexts. Do not interfere with held Force Move, running or an already held {p:Wizard_Disintegrate} key. Stop the Greater Rift rule after the guardian is dead. This is short upkeep, not a permanent channel controller." },
            { "Skill_WizardElectrocute", "Refresh the {p:Generic_ItemPassiveUniqueRing735x1} damage bonus from a resource-generating attack when an enemy is within 10 yards of the cursor. The {p:Wizard_Archon} Meteor combination with {p:Wizard_Passive_ArcaneDynamo}, {p:TheSwami}, {p:NilfursBoast} and {p:FazulasImprobableChain} also builds Dynamo stacks outside {p:Wizard_Archon} before its elemental window. Do not activate while running or holding Force Move. {p:Triumvirate} has its own separate generator toggle." },
            { "Wizard_EnergyArmor", "Maintain the long-duration buff, refreshing with roughly 55–65 seconds remaining. If {p:Wizard_Archon} is equipped, refresh earlier when less than 120 seconds remain so the buff can cover transformation. Requires enough {res:ArcanePower} for the activation. This is duration-based upkeep, not a health or enemy-density activation condition." },
            { "Wizard_IceArmor", "Maintain the long-duration buff, refreshing with roughly 55–65 seconds remaining. If {p:Wizard_Archon} is equipped, refresh earlier when less than 120 seconds remain so the buff can cover transformation. Requires enough {res:ArcanePower} for the activation. This is duration-based upkeep, not a health or enemy-density activation condition." },
            { "Wizard_MagicWeapon", "Maintain the long-duration buff, refreshing with roughly 55–65 seconds remaining. If {p:Wizard_Archon} is equipped, refresh earlier when less than 120 seconds remain so the buff can cover transformation. Requires enough {res:ArcanePower} for the activation. This is duration-based upkeep, not a health or enemy-density activation condition." },
            { "Wizard_ExplosiveBlast_CloseEnemies", "Use near close-range enemies with {p:WandOfWoh}, sufficiently high {res:ArcanePower}, or supported Firebird/Deathwish setups. {p:OrbOfInfiniteDepth} maintains 4 stacks and refreshes around 1.7–2 seconds, with an {p:Wizard_Passive_ArcaneDynamo} exclusion. The {p:Wizard_Archon} Meteor combination has a separate outside-{p:Wizard_Archon} elemental rule. Radius depends on the rune and movement. This does not replace a full damage rotation." },
            { "Wizard_Familiar", "Maintain {p:Wizard_Familiar} when missing or within roughly 1–2 seconds of expiry. If {p:Wizard_Archon} is equipped, refresh below 120 seconds instead. A newly gained {p:Generic_PagesBuffRunSpeed} can activation condition a recast during its first 0.5 seconds. Requires enough {res:ArcanePower}. This is buff upkeep, not enemy targeting." },
            { "Wizard_FrostNova_CloseEnemies", "Requires nearby enemies within 16 yards: normally 1, or 5 with {r:Deep Freeze}. During a group guardian fight, non-Frozen-Mist rules are suppressed when another valid player is present. {r:Bone Chill} needs a nearby elite; other control rules consider elemental timing or {p:ObsidianRingOfTheZodiac}. Zodiac repeats are spaced rather than sent every frame. This is not a universal freeze on cooldown." },
            { "Skill_WizardHydra", "For Typhon 4-piece with {p:SerpentsSparker}, excluding {r:Mammoth Hydra}. Needs an enemy within 100 yards. Restore missing heads or a low detected head count; otherwise place near the cursor only when fewer than 2 Hydras cover that area and {p:Wizard_Passive_ArcaneDynamo} is ready if equipped. This is build-specific summon upkeep rather than unrestricted relocation." },
            { "Wizard_MirrorImage_Defensive", "For Firebird 6-piece while the detected {p:Wizard_MirrorImage} buff is absent. Use the skill near an elite within 15 yards, or a dense pack of at least 10 enemies there. {p:ConventionOfElements} adjusts the timing window for these rules. Despite the menu category, this is a set/damage timing rule rather than a simple emergency low-{res:Life} button." },
            { "Wizard_StormArmor", "Maintain {p:Wizard_StormArmor} when missing or within roughly 1–2 seconds of expiry; refresh below 120 seconds if {p:Wizard_Archon} is equipped. A newly gained {p:Generic_PagesBuffRunSpeed} can activation condition an early recast. Requires the resource-reduced 25-Arcane-Power base cost plus 50 {res:ArcanePower} in reserve. This is duration-based upkeep, not a low-health response." },
            { "Skill_WizardTriumvirate_Electrocute", "Requires {p:Triumvirate}. Use this equipped generator when the detected {p:Triumvirate} count is below 3 stacks or its buff has less than 1.5 seconds remaining. The toggle maintains the {p:Wizard_ArcaneOrb} damage stacks; it does not activate {p:Wizard_ArcaneOrb} or replace a full generator combat controller." },
            { "Skill_WizardTriumvirate_MagicMissile", "Requires {p:Triumvirate}. Use this equipped generator when the detected {p:Triumvirate} count is below 3 stacks or its buff has less than 1.5 seconds remaining. The toggle maintains the {p:Wizard_ArcaneOrb} damage stacks; it does not activate {p:Wizard_ArcaneOrb} or replace a full generator combat controller." },
            { "Skill_WizardTriumvirate_ShockPulse", "Requires {p:Triumvirate}. Use this equipped generator when the detected {p:Triumvirate} count is below 3 stacks or its buff has less than 1.5 seconds remaining. The toggle maintains the {p:Wizard_ArcaneOrb} damage stacks; it does not activate {p:Wizard_ArcaneOrb} or replace a full generator combat controller." },
            { "Skill_WizardTriumvirate_SpectralBlade", "Requires {p:Triumvirate}. Use this equipped generator when the detected {p:Triumvirate} count is below 3 stacks or its buff has less than 1.5 seconds remaining. The toggle maintains the {p:Wizard_ArcaneOrb} damage stacks; it does not activate {p:Wizard_ArcaneOrb} or replace a full generator combat controller." },
            { "Skill_WizardWaveofForce", "For the {p:Wizard_Archon} Meteor combination with {p:TheSwami}, {p:FazulasImprobableChain}, {p:TheGrandVizier} and {p:ObsidianRingOfTheZodiac}. Outside {p:Wizard_Archon}, activate near an enemy within 20 yards while {p:Wizard_Archon} has at least 3 seconds of cooldown left and {p:Wizard_Passive_ArcaneDynamo} is below 5 stacks. This is a specific build rule, not automatic {p:Wizard_WaveOfForce} for every Wizard." },
        };
        private static readonly Regex MacroNameToken = new Regex(@"\{(p|r|res):([^}]+)\}",RegexOptions.Compiled);
        private ISnoPower MacroPower(string name)
        {
            switch(name)
            {
                case "AetherWalker": return Hud.Sno.SnoPowers.AetherWalker;
                case "AquilaCuirass": return Hud.Sno.SnoPowers.AquilaCuirass;
                case "ArreatsLaw": return Hud.Sno.SnoPowers.ArreatsLaw;
                case "BaneOfTheTrappedPrimary": return Hud.Sno.SnoPowers.BaneOfTheTrappedPrimary;
                case "Barbarian_AncientSpear": return Hud.Sno.SnoPowers.Barbarian_AncientSpear;
                case "Barbarian_BattleRage": return Hud.Sno.SnoPowers.Barbarian_BattleRage;
                case "Barbarian_Leap": return Hud.Sno.SnoPowers.Barbarian_Leap;
                case "Barbarian_Rend": return Hud.Sno.SnoPowers.Barbarian_Rend;
                case "Barbarian_SeismicSlam": return Hud.Sno.SnoPowers.Barbarian_SeismicSlam;
                case "Barbarian_Sprint": return Hud.Sno.SnoPowers.Barbarian_Sprint;
                case "Barbarian_WarCry": return Hud.Sno.SnoPowers.Barbarian_WarCry;
                case "Barbarian_WrathOfTheBerserker": return Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker;
                case "BindingsOfTheLesserGods": return Hud.Sno.SnoPowers.BindingsOfTheLesserGods;
                case "BladeOfTheTribes": return Hud.Sno.SnoPowers.BladeOfTheTribes;
                case "BloodsongMail": return Hud.Sno.SnoPowers.BloodsongMail;
                case "BombardiersRucksack": return Hud.Sno.SnoPowers.BombardiersRucksack;
                case "ChainOfShadows": return Hud.Sno.SnoPowers.ChainOfShadows;
                case "ChilaniksChain": return Hud.Sno.SnoPowers.ChilaniksChain;
                case "ConventionOfElements": return Hud.Sno.SnoPowers.ConventionOfElements;
                case "Crusader_AkaratsChampion": return Hud.Sno.SnoPowers.Crusader_AkaratsChampion;
                case "Crusader_BlessedShield": return Hud.Sno.SnoPowers.Crusader_BlessedShield;
                case "Crusader_Bombardment": return Hud.Sno.SnoPowers.Crusader_Bombardment;
                case "Crusader_FistOfTheHeavens": return Hud.Sno.SnoPowers.Crusader_FistOfTheHeavens;
                case "Crusader_HeavensFury": return Hud.Sno.SnoPowers.Crusader_HeavensFury;
                case "Crusader_IronSkin": return Hud.Sno.SnoPowers.Crusader_IronSkin;
                case "Crusader_Judgment": return Hud.Sno.SnoPowers.Crusader_Judgment;
                case "Crusader_Phalanx": return Hud.Sno.SnoPowers.Crusader_Phalanx;
                case "Crusader_SteedCharge": return Hud.Sno.SnoPowers.Crusader_SteedCharge;
                case "Dawn": return Hud.Sno.SnoPowers.Dawn;
                case "DemonHunter_Chakram": return Hud.Sno.SnoPowers.DemonHunter_Chakram;
                case "DemonHunter_Impale": return Hud.Sno.SnoPowers.DemonHunter_Impale;
                case "DemonHunter_Multishot": return Hud.Sno.SnoPowers.DemonHunter_Multishot;
                case "DemonHunter_Passive_CustomEngineering": return Hud.Sno.SnoPowers.DemonHunter_Passive_CustomEngineering;
                case "DemonHunter_Preparation": return Hud.Sno.SnoPowers.DemonHunter_Preparation;
                case "DemonHunter_RainOfVengeance": return Hud.Sno.SnoPowers.DemonHunter_RainOfVengeance;
                case "DemonHunter_Sentry": return Hud.Sno.SnoPowers.DemonHunter_Sentry;
                case "DemonHunter_ShadowPower": return Hud.Sno.SnoPowers.DemonHunter_ShadowPower;
                case "DemonHunter_SmokeScreen": return Hud.Sno.SnoPowers.DemonHunter_SmokeScreen;
                case "DemonHunter_SpikeTrap": return Hud.Sno.SnoPowers.DemonHunter_SpikeTrap;
                case "DemonHunter_Vault": return Hud.Sno.SnoPowers.DemonHunter_Vault;
                case "DemonHunter_Vengeance": return Hud.Sno.SnoPowers.DemonHunter_Vengeance;
                case "ElusiveRing": return Hud.Sno.SnoPowers.ElusiveRing;
                case "FaithfulMemory": return Hud.Sno.SnoPowers.FaithfulMemory;
                case "FateOfTheFell": return Hud.Sno.SnoPowers.FateOfTheFell;
                case "FazulasImprobableChain": return Hud.Sno.SnoPowers.FazulasImprobableChain;
                case "FlyingDragon": return Hud.Sno.SnoPowers.FlyingDragon;
                case "FrydehrsWrath": return Hud.Sno.SnoPowers.FrydehrsWrath;
                case "FuneraryPick": return Hud.Sno.SnoPowers.FuneraryPick;
                case "Generic_ItemPassiveUniqueRing735x1": return Hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1;
                case "Generic_PagesBuffInfiniteCasting": return Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting;
                case "Generic_PagesBuffInvulnerable": return Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable;
                case "Generic_PagesBuffRunSpeed": return Hud.Sno.SnoPowers.Generic_PagesBuffRunSpeed;
                case "HauntedVisions": return Hud.Sno.SnoPowers.HauntedVisions;
                case "IceblinkSecondary": return Hud.Sno.SnoPowers.IceblinkSecondary;
                case "Ingeom": return Hud.Sno.SnoPowers.Ingeom;
                case "KrysbinsSentence": return Hud.Sno.SnoPowers.KrysbinsSentence;
                case "LefebvresSoliloquy": return Hud.Sno.SnoPowers.LefebvresSoliloquy;
                case "LutSocks": return Hud.Sno.SnoPowers.LutSocks;
                case "MesserschmidtsReaver": return Hud.Sno.SnoPowers.MesserschmidtsReaver;
                case "Monk_CripplingWave": return Hud.Sno.SnoPowers.Monk_CripplingWave;
                case "Monk_Epiphany": return Hud.Sno.SnoPowers.Monk_Epiphany;
                case "Monk_FistsOfThunder": return Hud.Sno.SnoPowers.Monk_FistsOfThunder;
                case "Monk_Passive_CombinationStrike": return Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike;
                case "Monk_Serenity": return Hud.Sno.SnoPowers.Monk_Serenity;
                case "Monk_SweepingWind": return Hud.Sno.SnoPowers.Monk_SweepingWind;
                case "Monk_WayOfTheHundredFists": return Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists;
                case "NayrsBlackDeath": return Hud.Sno.SnoPowers.NayrsBlackDeath;
                case "Necromancer_ArmyOfTheDead": return Hud.Sno.SnoPowers.Necromancer_ArmyOfTheDead;
                case "Necromancer_BloodRush": return Hud.Sno.SnoPowers.Necromancer_BloodRush;
                case "Necromancer_BoneArmor": return Hud.Sno.SnoPowers.Necromancer_BoneArmor;
                case "Necromancer_BoneSpear": return Hud.Sno.SnoPowers.Necromancer_BoneSpear;
                case "Necromancer_BoneSpikes": return Hud.Sno.SnoPowers.Necromancer_BoneSpikes;
                case "Necromancer_CommandSkeletons": return Hud.Sno.SnoPowers.Necromancer_CommandSkeletons;
                case "Necromancer_DeathNova": return Hud.Sno.SnoPowers.Necromancer_DeathNova;
                case "Necromancer_Decrepify": return Hud.Sno.SnoPowers.Necromancer_Decrepify;
                case "Necromancer_Devour": return Hud.Sno.SnoPowers.Necromancer_Devour;
                case "Necromancer_LandOfTheDead": return Hud.Sno.SnoPowers.Necromancer_LandOfTheDead;
                case "Necromancer_Leech": return Hud.Sno.SnoPowers.Necromancer_Leech;
                case "Necromancer_Passive_BloodIsPower": return Hud.Sno.SnoPowers.Necromancer_Passive_BloodIsPower;
                case "Necromancer_Revive": return Hud.Sno.SnoPowers.Necromancer_Revive;
                case "Necromancer_Simulacrum": return Hud.Sno.SnoPowers.Necromancer_Simulacrum;
                case "Necromancer_SiphonBlood": return Hud.Sno.SnoPowers.Necromancer_SiphonBlood;
                case "Necromancer_SkeletalMage": return Hud.Sno.SnoPowers.Necromancer_SkeletalMage;
                case "NilfursBoast": return Hud.Sno.SnoPowers.NilfursBoast;
                case "ObsidianRingOfTheZodiac": return Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac;
                case "OrbOfInfiniteDepth": return Hud.Sno.SnoPowers.OrbOfInfiniteDepth;
                case "RazethsVolition": return Hud.Sno.SnoPowers.RazethsVolition;
                case "RechelsRingOfLarceny": return Hud.Sno.SnoPowers.RechelsRingOfLarceny;
                case "RingOfEmptiness": return Hud.Sno.SnoPowers.RingOfEmptiness;
                case "SacredHarvester": return Hud.Sno.SnoPowers.SacredHarvester;
                case "SerpentsSparker": return Hud.Sno.SnoPowers.SerpentsSparker;
                case "ShukranisTriumph": return Hud.Sno.SnoPowers.ShukranisTriumph;
                case "SkularsSalvation": return Hud.Sno.SnoPowers.SkularsSalvation;
                case "SquirtsNecklace": return Hud.Sno.SnoPowers.SquirtsNecklace;
                case "StarmetalKukri": return Hud.Sno.SnoPowers.StarmetalKukri;
                case "StoneOfJordan": return Hud.Sno.SnoPowers.StoneOfJordan;
                case "TaegukPrimary": return Hud.Sno.SnoPowers.TaegukPrimary;
                case "TheCrudestBoots": return Hud.Sno.SnoPowers.TheCrudestBoots;
                case "TheFinalWitness": return Hud.Sno.SnoPowers.TheFinalWitness;
                case "TheGrandVizier": return Hud.Sno.SnoPowers.TheGrandVizier;
                case "TheLawsOfSeph": return Hud.Sno.SnoPowers.TheLawsOfSeph;
                case "TheMortalDrama": return Hud.Sno.SnoPowers.TheMortalDrama;
                case "TheSwami": return Hud.Sno.SnoPowers.TheSwami;
                case "TheTallMansFinger": return Hud.Sno.SnoPowers.TheTallMansFinger;
                case "Triumvirate": return Hud.Sno.SnoPowers.Triumvirate;
                case "UnrelentingPhalanx": return Hud.Sno.SnoPowers.UnrelentingPhalanx;
                case "WandOfWoh": return Hud.Sno.SnoPowers.WandOfWoh;
                case "WinterFlurry": return Hud.Sno.SnoPowers.WinterFlurry;
                case "WisdomOfKalan": return Hud.Sno.SnoPowers.WisdomOfKalan;
                case "WitchDoctor_CorpseSpider": return Hud.Sno.SnoPowers.WitchDoctor_CorpseSpider;
                case "WitchDoctor_Gargantuan": return Hud.Sno.SnoPowers.WitchDoctor_Gargantuan;
                case "WitchDoctor_Haunt": return Hud.Sno.SnoPowers.WitchDoctor_Haunt;
                case "WitchDoctor_Hex": return Hud.Sno.SnoPowers.WitchDoctor_Hex;
                case "WitchDoctor_LocustSwarm": return Hud.Sno.SnoPowers.WitchDoctor_LocustSwarm;
                case "WitchDoctor_Passive_GraveInjustice": return Hud.Sno.SnoPowers.WitchDoctor_Passive_GraveInjustice;
                case "WitchDoctor_SoulHarvest": return Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest;
                case "WitchDoctor_SpiritBarrage": return Hud.Sno.SnoPowers.WitchDoctor_SpiritBarrage;
                case "WitchDoctor_SpiritWalk": return Hud.Sno.SnoPowers.WitchDoctor_SpiritWalk;
                case "Wizard_ArcaneOrb": return Hud.Sno.SnoPowers.Wizard_ArcaneOrb;
                case "Wizard_Archon": return Hud.Sno.SnoPowers.Wizard_Archon;
                case "Wizard_BlackHole": return Hud.Sno.SnoPowers.Wizard_BlackHole;
                case "Wizard_Blizzard": return Hud.Sno.SnoPowers.Wizard_Blizzard;
                case "Wizard_Disintegrate": return Hud.Sno.SnoPowers.Wizard_Disintegrate;
                case "Wizard_Familiar": return Hud.Sno.SnoPowers.Wizard_Familiar;
                case "Wizard_MirrorImage": return Hud.Sno.SnoPowers.Wizard_MirrorImage;
                case "Wizard_Passive_ArcaneDynamo": return Hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo;
                case "Wizard_StormArmor": return Hud.Sno.SnoPowers.Wizard_StormArmor;
                case "Wizard_Teleport": return Hud.Sno.SnoPowers.Wizard_Teleport;
                case "Wizard_WaveOfForce": return Hud.Sno.SnoPowers.Wizard_WaveOfForce;
                case "Wormwood": return Hud.Sno.SnoPowers.Wormwood;
                case "WrapsOfClarity": return Hud.Sno.SnoPowers.WrapsOfClarity;
                default: return null;
            }
        }
        private static string MacroPowerFallback(string name)
        {
            switch(name)
            {
                case "AetherWalker": return "Aether Walker";
                case "AquilaCuirass": return "Aquila Cuirass";
                case "ArreatsLaw": return "Arreats Law";
                case "BaneOfTheTrappedPrimary": return "Bane Of The Trapped Primary";
                case "Barbarian_AncientSpear": return "Ancient Spear";
                case "Barbarian_BattleRage": return "Battle Rage";
                case "Barbarian_Leap": return "Leap";
                case "Barbarian_Rend": return "Rend";
                case "Barbarian_SeismicSlam": return "Seismic Slam";
                case "Barbarian_Sprint": return "Sprint";
                case "Barbarian_WarCry": return "War Cry";
                case "Barbarian_WrathOfTheBerserker": return "Wrath of the Berserker";
                case "BindingsOfTheLesserGods": return "Bindings Of The Lesser Gods";
                case "BladeOfTheTribes": return "Blade Of The Tribes";
                case "BloodsongMail": return "Bloodsong Mail";
                case "BombardiersRucksack": return "Bombardiers Rucksack";
                case "ChainOfShadows": return "Chain Of Shadows";
                case "ChilaniksChain": return "Chilaniks Chain";
                case "ConventionOfElements": return "Convention Of Elements";
                case "Crusader_AkaratsChampion": return "Akarat’s Champion";
                case "Crusader_BlessedShield": return "Blessed Shield";
                case "Crusader_Bombardment": return "Bombardment";
                case "Crusader_FistOfTheHeavens": return "Fist of the Heavens";
                case "Crusader_HeavensFury": return "Heaven’s Fury";
                case "Crusader_IronSkin": return "Iron Skin";
                case "Crusader_Judgment": return "Judgment";
                case "Crusader_Phalanx": return "Phalanx";
                case "Crusader_SteedCharge": return "Steed Charge";
                case "Dawn": return "Dawn";
                case "DemonHunter_Chakram": return "Chakram";
                case "DemonHunter_Impale": return "Impale";
                case "DemonHunter_Multishot": return "Multishot";
                case "DemonHunter_Passive_CustomEngineering": return "Demon Hunter_ Passive_ Custom Engineering";
                case "DemonHunter_Preparation": return "Preparation";
                case "DemonHunter_RainOfVengeance": return "Rain of Vengeance";
                case "DemonHunter_Sentry": return "Sentry";
                case "DemonHunter_ShadowPower": return "Shadow Power";
                case "DemonHunter_SmokeScreen": return "Smoke Screen";
                case "DemonHunter_SpikeTrap": return "Spike Trap";
                case "DemonHunter_Vault": return "Vault";
                case "DemonHunter_Vengeance": return "Vengeance";
                case "ElusiveRing": return "Elusive Ring";
                case "FaithfulMemory": return "Faithful Memory";
                case "FateOfTheFell": return "Fate Of The Fell";
                case "FazulasImprobableChain": return "Fazulas Improbable Chain";
                case "FlyingDragon": return "Flying Dragon";
                case "FrydehrsWrath": return "Frydehrs Wrath";
                case "FuneraryPick": return "Funerary Pick";
                case "Generic_ItemPassiveUniqueRing735x1": return "Focus/Restraint";
                case "Generic_PagesBuffInfiniteCasting": return "Channeling Pylon";
                case "Generic_PagesBuffInvulnerable": return "Shield Pylon";
                case "Generic_PagesBuffRunSpeed": return "Speed Pylon";
                case "HauntedVisions": return "Haunted Visions";
                case "IceblinkSecondary": return "Iceblink Secondary";
                case "Ingeom": return "Ingeom";
                case "KrysbinsSentence": return "Krysbins Sentence";
                case "LefebvresSoliloquy": return "Lefebvres Soliloquy";
                case "LutSocks": return "Lut Socks";
                case "MesserschmidtsReaver": return "Messerschmidts Reaver";
                case "Monk_CripplingWave": return "Crippling Wave";
                case "Monk_Epiphany": return "Epiphany";
                case "Monk_FistsOfThunder": return "Fists of Thunder";
                case "Monk_Passive_CombinationStrike": return "Combination Strike";
                case "Monk_Serenity": return "Serenity";
                case "Monk_SweepingWind": return "Sweeping Wind";
                case "Monk_WayOfTheHundredFists": return "Way of the Hundred Fists";
                case "NayrsBlackDeath": return "Nayrs Black Death";
                case "Necromancer_ArmyOfTheDead": return "Army of the Dead";
                case "Necromancer_BloodRush": return "Blood Rush";
                case "Necromancer_BoneArmor": return "Bone Armor";
                case "Necromancer_BoneSpear": return "Bone Spear";
                case "Necromancer_BoneSpikes": return "Bone Spikes";
                case "Necromancer_CommandSkeletons": return "Command Skeletons";
                case "Necromancer_DeathNova": return "Death Nova";
                case "Necromancer_Decrepify": return "Decrepify";
                case "Necromancer_Devour": return "Devour";
                case "Necromancer_LandOfTheDead": return "Land of the Dead";
                case "Necromancer_Leech": return "Leech";
                case "Necromancer_Passive_BloodIsPower": return "Necromancer_ Passive_ Blood Is Power";
                case "Necromancer_Revive": return "Revive";
                case "Necromancer_Simulacrum": return "Simulacrum";
                case "Necromancer_SiphonBlood": return "Siphon Blood";
                case "Necromancer_SkeletalMage": return "Skeletal Mage";
                case "NilfursBoast": return "Nilfurs Boast";
                case "ObsidianRingOfTheZodiac": return "Obsidian Ring Of The Zodiac";
                case "OrbOfInfiniteDepth": return "Orb Of Infinite Depth";
                case "RazethsVolition": return "Razeths Volition";
                case "RechelsRingOfLarceny": return "Rechels Ring Of Larceny";
                case "RingOfEmptiness": return "Ring Of Emptiness";
                case "SacredHarvester": return "Sacred Harvester";
                case "SerpentsSparker": return "Serpents Sparker";
                case "ShukranisTriumph": return "Shukranis Triumph";
                case "SkularsSalvation": return "Skulars Salvation";
                case "SquirtsNecklace": return "Squirt's Necklace";
                case "StarmetalKukri": return "Starmetal Kukri";
                case "StoneOfJordan": return "Stone Of Jordan";
                case "TaegukPrimary": return "Taeguk Primary";
                case "TheCrudestBoots": return "The Crudest Boots";
                case "TheFinalWitness": return "The Final Witness";
                case "TheGrandVizier": return "The Grand Vizier";
                case "TheLawsOfSeph": return "The Laws Of Seph";
                case "TheMortalDrama": return "The Mortal Drama";
                case "TheSwami": return "The Swami";
                case "TheTallMansFinger": return "The Tall Mans Finger";
                case "Triumvirate": return "Triumvirate";
                case "UnrelentingPhalanx": return "Unrelenting Phalanx";
                case "WandOfWoh": return "Wand Of Woh";
                case "WinterFlurry": return "Winter Flurry";
                case "WisdomOfKalan": return "Wisdom Of Kalan";
                case "WitchDoctor_CorpseSpider": return "Corpse Spiders";
                case "WitchDoctor_Gargantuan": return "Gargantuan";
                case "WitchDoctor_Haunt": return "Haunt";
                case "WitchDoctor_Hex": return "Hex";
                case "WitchDoctor_LocustSwarm": return "Locust Swarm";
                case "WitchDoctor_Passive_GraveInjustice": return "Witch Doctor_ Passive_ Grave Injustice";
                case "WitchDoctor_SoulHarvest": return "Soul Harvest";
                case "WitchDoctor_SpiritBarrage": return "Spirit Barrage";
                case "WitchDoctor_SpiritWalk": return "Spirit Walk";
                case "Wizard_ArcaneOrb": return "Arcane Orb";
                case "Wizard_Archon": return "Archon";
                case "Wizard_BlackHole": return "Black Hole";
                case "Wizard_Blizzard": return "Blizzard";
                case "Wizard_Disintegrate": return "Disintegrate";
                case "Wizard_Familiar": return "Familiar";
                case "Wizard_MirrorImage": return "Mirror Image";
                case "Wizard_Passive_ArcaneDynamo": return "Arcane Dynamo";
                case "Wizard_StormArmor": return "Storm Armor";
                case "Wizard_Teleport": return "Teleport";
                case "Wizard_WaveOfForce": return "Wave of Force";
                case "Wormwood": return "Wormwood";
                case "WrapsOfClarity": return "Wraps Of Clarity";
                default: return name;
            }
        }
        private string ExpandMacroText(string text,s7o_AutoSkill.MacroDescriptor entry)
        {
            return MacroNameToken.Replace(text,match=>
            {
                string kind=match.Groups[1].Value,value=match.Groups[2].Value;
                if(kind=="res")
                    return s7o_Localization.Get("macro.help.resource."+value,value=="ArcanePower"?"Arcane Power":value);
                if(kind=="p")
                {
                    var power=MacroPower(value);
                    if(power!=null)
                    {
                        if(!string.IsNullOrWhiteSpace(power.NameLocalized))return power.NameLocalized;
                        if(!string.IsNullOrWhiteSpace(power.NameEnglish))return power.NameEnglish;
                    }
                    return MacroPowerFallback(value);
                }
                int separator=value.IndexOf(':');
                var runePower=separator<0?Hud.Sno.GetSnoPower(entry.PowerSno):MacroPower(value.Substring(0,separator));
                string english=separator<0?value:value.Substring(separator+1);
                if(runePower!=null&&runePower.RuneNamesEnglish!=null&&runePower.RuneNamesLocalized!=null)
                    for(int i=0;i<runePower.RuneNamesEnglish.Length&&i<runePower.RuneNamesLocalized.Length;i++)
                        if(string.Equals(runePower.RuneNamesEnglish[i],english,StringComparison.OrdinalIgnoreCase)
                            &&!string.IsNullOrWhiteSpace(runePower.RuneNamesLocalized[i]))return runePower.RuneNamesLocalized[i];
                return english;
            });
        }

        private bool TryGetMacroHelp(string action,string parent,out HelpText help)
        {
            help=null;string code=null;
            if(string.Equals(action,"row",StringComparison.OrdinalIgnoreCase)&&parent!=null&&parent.StartsWith("macro:",StringComparison.OrdinalIgnoreCase))code=parent.Substring(6);
            else if(action!=null&&action.StartsWith("macro:toggle:",StringComparison.OrdinalIgnoreCase))code=action.Substring(action.LastIndexOf(':')+1);
            if(string.IsNullOrEmpty(code))return false;
            var owner=Hud.GetPlugin<s7o_AutoSkill>();if(owner==null)return false;
            if(!ReferenceEquals(owner,_macroOwner)||_macroHelp.Count==0)
            {
                _macroOwner=owner;_macroHelp.Clear();
                foreach(var item in owner.GetMacroCatalog())if(item!=null&&!string.IsNullOrEmpty(item.Code))_macroHelp[item.Code]=item;
            }
            s7o_AutoSkill.MacroDescriptor entry;if(!_macroHelp.TryGetValue(code,out entry))return false;
            string title=entry.Title;
            if(entry.Code=="Skill_DrinkHealthPotion")title=s7o_Localization.Get("macro.title.potion",title);
            else if(entry.Code=="Skill_DrinkHealthPotionArchonMeteorBuild")title=s7o_Localization.Get("macro.title.archon_potion",title);
            else if(entry.PowerSno!=0)
            {
                var power=Hud.Sno.GetSnoPower(entry.PowerSno);
                if(power!=null&&!string.IsNullOrWhiteSpace(power.NameLocalized))
                {
                    title=power.NameLocalized;
                    if(!string.IsNullOrEmpty(entry.TitleSuffix))title+=": "+s7o_Localization.Get("macro.suffix."+entry.TitleSuffix,entry.TitleSuffix);
                }
            }
            string body;
            if(MacroDescriptions.TryGetValue(code,out body))
            {
                body=ExpandMacroText(s7o_Localization.Get("macro.help."+code,body),entry);
                body+="\n\n"+s7o_Localization.Get("macro.help.common","Requires the macro enabled and the relevant skill equipped and ready. Normal UI, cooldown and input-ownership safeguards still apply. Numbers describe the current default rules.");
            }
            else body=string.IsNullOrEmpty(entry.DescriptionKey)?entry.Description:s7o_Localization.Get(entry.DescriptionKey,entry.Description);
            help=new HelpText(title,body);return true;
        }

        private void PaintTip(string action,string parent,string label,RectangleF region)
        {
            if(!Enabled || _menu==null || !_menu.HoverTipsEnabled || _menu.HoverTipsBlocked || !Hud.Window.IsForeground
                || string.IsNullOrEmpty(action)){Reset();return;}
            string identity=action+"|"+parent+"|"+label;int now=Environment.TickCount;
            if(_identity!=identity || _context!=_menu.HoverTipsContext || _language!=s7o_Localization.LanguageCode
                || Math.Abs(Hud.Window.CursorX-_anchorX)>5 || Math.Abs(Hud.Window.CursorY-_anchorY)>5)
            {
                HelpText text;
                if(TryGetMacroHelp(action,parent,out text)){_title=text.Title;_body=text.Body;}
                else
                {
                    object[] args;string key=Resolve(action,parent,label,out args);
                    if(key==null||!Text.TryGetValue(key,out text)){Reset();return;}
                    _title=Local(key+".title",text.Title,new object[0]);_body=Local(key+".description",text.Body,args);
                }
                _identity=identity;_context=_menu.HoverTipsContext;_language=s7o_Localization.LanguageCode;_since=now;
                _anchorX=Hud.Window.CursorX;_anchorY=Hud.Window.CursorY;
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
                _titleHeight=_titleFont.GetTextLayout("Ag").Metrics.Height+2;
                _lineHeight=_bodyFont.GetTextLayout("Ag").Metrics.Height+3;
                Wrap(_title,_titleFont,_width-24,_titleLines);Wrap(_body,_bodyFont,_width-24,_bodyLines);
                _boxHeight=22+_titleLines.Count*_titleHeight+_bodyLines.Count*_lineHeight;
                // Widen unusually long translations before positioning; never crop their text.
                while(_boxHeight>height-16&&_width<width-32)
                {
                    _width=Math.Min(width-32,_width+80*scale);
                    Wrap(_title,_titleFont,_width-24,_titleLines);Wrap(_body,_bodyFont,_width-24,_bodyLines);
                    _boxHeight=22+_titleLines.Count*_titleHeight+_bodyLines.Count*_lineHeight;
                }
            }
            float x=Hud.Window.CursorX+18,y=Hud.Window.CursorY+22;
            if(x+_width>width-8)x=Hud.Window.CursorX-_width-14;
            if(y+_boxHeight>height-8)y=Hud.Window.CursorY-_boxHeight-14;
            x=Math.Max(8,Math.Min(width-_width-8,x));y=Math.Max(8,Math.Min(height-_boxHeight-8,y));
            _shadow.DrawRectangle(x+3,y+3,_width,_boxHeight);_background.DrawRectangle(x,y,_width,_boxHeight);_border.DrawRectangle(x,y,_width,_boxHeight);
            float ty=y+9;foreach(string line in _titleLines){_titleFont.DrawText(line,x+12,ty);ty+=_titleHeight;}
            ty+=4;foreach(string line in _bodyLines){_bodyFont.DrawText(line,x+12,ty);ty+=_lineHeight;}
        }
        private static float TextWidth(IFont font,string text)
        {
            // Intermediate wrapping candidates must not fill the engine's layout cache.
            using(var layout=font.GetTextLayoutManualDispose(text))return layout.Metrics.Width;
        }
        private static void Wrap(string text,IFont font,float width,List<string> lines)
        {
            lines.Clear();width=Math.Max(1,width);
            foreach(string paragraph in (text??"").Replace("\r","").Split('\n'))
            {
                string current="";
                foreach(string word in paragraph.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate=current.Length==0?word:current+" "+word;
                    if(current.Length>0&&TextWidth(font,candidate)>width){lines.Add(current);current="";}
                    if(current.Length>0){current+=" "+word;continue;}
                    string remaining=word;
                    // CJK and long unspaced names: split only at complete text elements.
                    while(remaining.Length>0&&TextWidth(font,remaining)>width)
                    {
                        int[] elements=StringInfo.ParseCombiningCharacters(remaining);
                        int low=1,high=elements.Length;
                        while(low<high)
                        {
                            int middle=(low+high+1)/2;
                            int length=middle==elements.Length?remaining.Length:elements[middle];
                            if(TextWidth(font,remaining.Substring(0,length))<=width)low=middle;else high=middle-1;
                        }
                        int take=low==elements.Length?remaining.Length:elements[low];
                        lines.Add(remaining.Substring(0,take));remaining=remaining.Substring(take);
                    }
                    current=remaining;
                }
                if(current.Length>0)lines.Add(current);
                else if(paragraph.Length==0)lines.Add("");
            }
        }
    }
}
