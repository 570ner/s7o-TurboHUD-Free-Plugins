using Turbo.Plugins;
using Turbo.Plugins.Default;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;

namespace Turbo.Plugins.s7o
{
    public class s7o_PylonAlerts : BasePlugin, IAfterCollectHandler, INewAreaHandler, IMonsterKilledHandler
    {
        private Dictionary<ShrineType, ShrineData> ShrinesData { get; set; }
        private Dictionary<uint, ShrineType> PylonBuffType { get; set; }

        // marker.Id is STRING
        private Dictionary<string, bool> SeenMarkers { get; set; } = new Dictionary<string, bool>();
        private int MyIndex { get; set; } = -1;

        // State / safety guards.
        // _grStartResetDone prevents the 0% GR-start reset from running every frame.
        private bool _grStartResetDone;

        // Once the Rift Guardian is killed/reward state is reached, pylon alerts are
        // irrelevant for the completed GR. This is latched until the next true GR start
        // so town portals, Urshi, and GR-close cleanup cannot re-announce or lose-spam.
        private bool _suppressPylonTtsForCompletedGreaterRift;

        // Tracks that the current GR reached the guardian phase. This lets the
        // monster-killed event and close fallback distinguish RG completion from
        // ordinary town portals during progression.
        private bool _greaterRiftGuardianPhaseSeen;

        private bool _globalTtsEnabled = true;
        private int _globalTtsVolume = 100;

        public bool NotifyInTown { get; set; }
        public bool TTSViewPylon { get; set; }
        public bool TTSBuffPylon { get; set; }

        public bool UseTTS { get; set; }

        // Prefer the independent SAPI route exposed by s7o_TTS_Broadcast.
        // Native Hud.Sound is shared/global and should only be a fallback.
        public bool PreferTtsBroadcastService { get; set; }
        public bool UseHudSoundFallback { get; set; }

        public int TtsMinGapMs { get; set; }

        private class ShrineData
        {
            public string FoundSpeechText { get; set; }
            public string ActivatedSpeechText { get; set; }
            public string LostSpeechText { get; set; }

            public bool Buff { get; set; }

            public void Reset()
            {
                Buff = false;
            }
        }

        public s7o_PylonAlerts()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            Order = 30001;

            NotifyInTown = true;
            TTSViewPylon = true;
            TTSBuffPylon = true;

            UseTTS = true;
            PreferTtsBroadcastService = true;
            UseHudSoundFallback = true;
            TtsMinGapMs = 0;

            ShrinesData = new Dictionary<ShrineType, ShrineData>();

            ShrinesData[ShrineType.PowerPylon] = new ShrineData
            {
                FoundSpeechText     = "Power Pylon",
                ActivatedSpeechText = "You have Power",
                LostSpeechText      = "Power Lost"
            };
            ShrinesData[ShrineType.ConduitPylon] = new ShrineData
            {
                FoundSpeechText     = "Conduit Pylon",
                ActivatedSpeechText = "You have Conduit",
                LostSpeechText      = "Conduit Lost"
            };
            ShrinesData[ShrineType.ChannelingPylon] = new ShrineData
            {
                FoundSpeechText     = "Channeling Pylon",
                ActivatedSpeechText = "You have Channeling",
                LostSpeechText      = "Channeling Lost"
            };
            ShrinesData[ShrineType.ShieldPylon] = new ShrineData
            {
                FoundSpeechText     = "Shield Pylon",
                ActivatedSpeechText = "You have Shield",
                LostSpeechText      = "Shield Lost"
            };
            ShrinesData[ShrineType.SpeedPylon] = new ShrineData
            {
                FoundSpeechText     = "Speed Pylon",
                ActivatedSpeechText = "You have Speed",
                LostSpeechText      = "Speed Lost"
            };

            PylonBuffType = new Dictionary<uint, ShrineType>
            {
                { 262935, ShrineType.PowerPylon },
                { 403404, ShrineType.ConduitPylon },
                { 266258, ShrineType.ChannelingPylon },
                { 266254, ShrineType.ShieldPylon },
                { 266271, ShrineType.SpeedPylon }
            };
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
            {
                if (newGame)
                {
                    MyIndex = -1;

                    foreach (var data in ShrinesData.Values)
                        data.Reset();

                    SeenMarkers.Clear();
                    _grStartResetDone = false;
                    _suppressPylonTtsForCompletedGreaterRift = false;
                    _greaterRiftGuardianPhaseSeen = false;
                }

                return;
            }

            if (newGame || MyIndex != Hud.Game.Me.Index)
            {
                MyIndex = Hud.Game.Me.Index;

                foreach (var data in ShrinesData.Values)
                    data.Reset();

                SeenMarkers.Clear();
                _grStartResetDone = false;
                _suppressPylonTtsForCompletedGreaterRift = false;
                _greaterRiftGuardianPhaseSeen = false;
            }
        }

        public void OnMonsterKilled(IMonster monster)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return;

                // The kill event is the most reliable moment to latch post-RG
                // silence. Polling quest/actor state can disappear after town
                // portals or GR-close transitions.
                if (!Hud.Game.Me.InGreaterRift && !_greaterRiftGuardianPhaseSeen)
                    return;

                if (IsRiftGuardianMonster(monster))
                    LatchCompletedGreaterRiftSilently();
            }
            catch { }
        }

        public void AfterCollect()
        {
            if (Hud == null || Hud.Game == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused)
                return;

            if (Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                return;

            bool inTown = Hud.Game.IsInTown;
            bool inGreaterRift = Hud.Game.Me.InGreaterRift;

            // We still process existing pylon-buff loss transitions in town.
            // Otherwise a pylon that expires or is stripped while repairing/town-porting
            // stays latched as active and gets announced later as stale state.
            if (!inGreaterRift && !(NotifyInTown && inTown))
            {
                _grStartResetDone = false;
                return;
            }

            // ============================
            // SILENT RESET ON REAL GR START
            // ============================
            // A new GR starts at 0%.  Clear prior-rift buff state once, before
            // normal buff checks, so opening/entering a new GR never announces
            // stale "Lost" messages.  The one-shot guard is essential: resetting
            // every frame at 0% caused "You have X" to loop if a pylon was taken
            // before gaining progress.
            if (inGreaterRift && Hud.Game.RiftPercentage == 0 && !_grStartResetDone)
            {
                SilentClearPylonState();

                _grStartResetDone = true;
                _suppressPylonTtsForCompletedGreaterRift = false;
                _greaterRiftGuardianPhaseSeen = false;
            }

            // Once real progress exists, arm the next 0% phase.  This keeps the
            // reset one-shot during the rare 0%-pylon case, but allows a later
            // newly-opened GR at 0% to clear prior-rift state silently.
            if (inGreaterRift && Hud.Game.RiftPercentage > 0)
                _grStartResetDone = false;

            if (inGreaterRift && IsGreaterRiftGuardianPhase())
                _greaterRiftGuardianPhaseSeen = true;

            // After RG death/reward/close, all pylon TTS is irrelevant for the
            // completed GR. Keep this latched until the next true GR start.
            if (_suppressPylonTtsForCompletedGreaterRift || IsGreaterRiftCompletedOrClosed())
            {
                LatchCompletedGreaterRiftSilently();
                return;
            }

            // ===== PYLON SEEN =====
            var markers = inGreaterRift ? Hud.Game.Markers : null;

            if (markers != null)
            {
                foreach (var marker in markers.Where(m => m.IsPylon && m.SnoActor != null))
                {
                    if (SeenMarkers.ContainsKey(marker.Id))
                        continue;

                    SeenMarkers[marker.Id] = true;

                    if (!TTSViewPylon)
                        continue;

                    ShrineType stype;
                    switch (marker.SnoActor.Sno)
                    {
                        case ActorSnoEnum._x1_lr_shrine_damage:
                            stype = ShrineType.PowerPylon;
                            break;
                        case ActorSnoEnum._x1_lr_shrine_electrified:
                            stype = ShrineType.ConduitPylon;
                            break;
                        case ActorSnoEnum._x1_lr_shrine_infinite_casting:
                            stype = ShrineType.ChannelingPylon;
                            break;
                        case ActorSnoEnum._x1_lr_shrine_invulnerable:
                            stype = ShrineType.ShieldPylon;
                            break;
                        case ActorSnoEnum._x1_lr_shrine_run_speed:
                            stype = ShrineType.SpeedPylon;
                            break;
                        default:
                            continue;
                    }

                    TrySpeak(ShrinesData[stype].FoundSpeechText);
                }
            }

            // ===== PYLON BUFF ON / OFF =====
            foreach (var kv in PylonBuffType)
            {
                uint buffSno = kv.Key;
                ShrineType type = kv.Value;

                var buff = Hud.Game.Me.Powers.GetBuff(buffSno);
                bool active = false;

                if (buff != null)
                {
                    try { active = Hud.Game.Me.Powers.BuffIsActive(buffSno, 0); }
                    catch { active = false; }
                }

                if (active && !ShrinesData[type].Buff)
                {
                    ShrinesData[type].Buff = true;

                    if (TTSBuffPylon)
                        TrySpeak(ShrinesData[type].ActivatedSpeechText);
                }
                else if (!active && ShrinesData[type].Buff)
                {
                    ShrinesData[type].Buff = false;

                    if (TTSBuffPylon)
                        TrySpeak(ShrinesData[type].LostSpeechText);
                }
            }
        }


        private void SilentClearPylonState()
        {
            foreach (var data in ShrinesData.Values)
                data.Buff = false;

            SeenMarkers.Clear();
        }

        private void LatchCompletedGreaterRiftSilently()
        {
            _suppressPylonTtsForCompletedGreaterRift = true;
            SilentClearPylonState();
        }

        private IQuest GetGreaterRiftQuest()
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Quests == null)
                    return null;

                return Hud.Game.Quests.FirstOrDefault(q =>
                    q != null &&
                    q.SnoQuest != null &&
                    q.SnoQuest.Sno == 382695);
            }
            catch
            {
                return null;
            }
        }

        private uint GetGreaterRiftQuestStepId()
        {
            var quest = GetGreaterRiftQuest();
            return quest != null ? quest.QuestStepId : 0;
        }

        private bool IsGreaterRiftGuardianPhase()
        {
            try
            {
                uint step = GetGreaterRiftQuestStepId();

                if (step == 16)
                    return true;

                if (Hud != null && Hud.Game != null && Hud.Game.RiftPercentage >= 100.0d)
                    return true;

                return Hud != null &&
                       Hud.Game != null &&
                       Hud.Game.AliveMonsters != null &&
                       Hud.Game.AliveMonsters.Any(m => IsRiftGuardianMonster(m));
            }
            catch
            {
                return false;
            }
        }

        private bool IsGreaterRiftCompletedOrClosed()
        {
            try
            {
                if (Hud == null || Hud.Game == null)
                    return false;

                if (IsUrshiVisible())
                    return true;

                var quest = GetGreaterRiftQuest();
                if (quest != null)
                {
                    uint step = quest.QuestStepId;

                    if (quest.State == QuestState.completed)
                        return true;

                    // LightningMOD / FreeHUD GR reward-completion steps.
                    if (step == 5 || step == 10 || step == 34 || step == 46)
                        return true;
                }

                if (Hud.Game.Monsters != null &&
                    Hud.Game.Monsters.Any(m => !m.IsAlive && IsRiftGuardianMonster(m)))
                {
                    return true;
                }

                // Last-resort close fallback: if this GR reached guardian phase and
                // the player is now in town with GR progress reset, the rift was
                // closed. Do not announce remaining buff losses during that cleanup.
                if (_greaterRiftGuardianPhaseSeen &&
                    !Hud.Game.Me.InGreaterRift &&
                    Hud.Game.IsInTown &&
                    Hud.Game.RiftPercentage < 1.0d)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRiftGuardianMonster(IMonster monster)
        {
            if (monster == null || monster.SnoMonster == null)
                return false;

            if (monster.Rarity == ActorRarity.Boss)
                return true;

            if (monster.SnoMonster.Priority == MonsterPriority.boss)
                return true;

            return false;
        }

        private bool IsUrshiVisible()
        {
            try
            {
                return Hud != null &&
                       Hud.Game != null &&
                       Hud.Game.Actors != null &&
                       Hud.Game.Actors.Any(actor =>
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


        public void SetGlobalTtsSettings(bool enabled, int volume)
        {
            _globalTtsEnabled = enabled;
            _globalTtsVolume = Math.Max(0, Math.Min(100, volume));

            // Do not call Hud.Sound.StopSpeak() here. Hud.Sound is global/shared;
            // stopping it from Pylon can suppress BannerSound and other native TTS users.
            // s7o_TTS_Broadcast handles its own SAPI stop when Global TTS is disabled.
        }

        public void ForceStopForDisable()
        {
            // Intentionally no Hud.Sound.StopSpeak(). Generic disable must not stop
            // shared/global native speech because BannerSound also depends on it.
        }

        private bool TrySpeak(string text)
        {
            if (!UseTTS || !_globalTtsEnabled)
                return false;

            if (string.IsNullOrEmpty(text))
                return false;

            if (PreferTtsBroadcastService && TrySpeakViaTtsBroadcast(text))
                return true;

            if (!UseHudSoundFallback)
                return false;

            if (Hud == null || Hud.Sound == null)
                return false;

            try
            {
                if (!Hud.Sound.IsSpeakEnabled)
                    return false;

                if (TtsMinGapMs > 0 && Hud.Sound.LastSpeak != null && !Hud.Sound.LastSpeak.TimerTest(TtsMinGapMs))
                    return false;

                Hud.Sound.Speak(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySpeakViaTtsBroadcast(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Hud == null || Hud.AllPlugins == null)
                return false;

            try
            {
                foreach (IPlugin plugin in Hud.AllPlugins)
                {
                    if (plugin == null)
                        continue;

                    try
                    {
                        if (!plugin.Enabled)
                            continue;
                    }
                    catch { }

                    Type type = plugin.GetType();
                    string name = type.Name ?? string.Empty;
                    string fullName = type.FullName ?? name;

                    if (!string.Equals(name, "s7o_TTS_Broadcast", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(fullName, "Turbo.Plugins.s7o.s7o_TTS_Broadcast", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    MethodInfo method = type.GetMethod(
                        "SpeakExternal",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string) },
                        null);

                    if (method == null)
                        return false;

                    object result = method.Invoke(plugin, new object[] { text });
                    return result is bool && (bool)result;
                }
            }
            catch { }

            return false;
        }
    }
}
