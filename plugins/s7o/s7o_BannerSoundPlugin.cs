using System;
using System.Collections.Generic;
using System.Reflection;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Detection approach: animation-only, no Hud.Game.Banners, no IBanner references.
    //
    // Why no IBanner fallback:
    //   Holding HashSet<IBanner> across frames keeps managed references alive during
    //   shutdown/area transitions.  When TurboHUD unloads, the GC finalizer walks
    //   those references while the game-object graph is already being torn down,
    //   causing the freeze observed on close.
    //
    // TTS close-lag note:
    //   The first call to Hud.Sound.Speak() lazy-initializes the .NET
    //   SpeechSynthesizer, which creates a COM STA background thread.  On process
    //   exit, .NET blocks waiting for that thread to join — this is the lag.
    //   It is a TurboHUD Free framework issue (the synthesizer is not Disposed on
    //   shutdown) and cannot be fixed from a plugin.  The lag is harmless.
    //
    // Missing-class coverage (Monk male, Crusader male, etc.):
    //   The enum list covers every confirmed variant.  The string fallback
    //   (contains "banner" AND "drop") catches any enum not in the list,
    //   including male Monk, male Crusader, and future class additions.
    //   Both paths are distinguished internally so DebugLog = true will show which triggered.

    public class s7o_BannerSoundPlugin : BasePlugin, INewAreaHandler, IBeforeRenderHandler
    {
        // -----------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------

        public bool UseTTS { get; set; }

        // Prefer the independent SAPI route exposed by s7o_TTS_Broadcast.
        // Native Hud.Sound is shared/global and can be disturbed by StopSpeak().
        public bool PreferTtsBroadcastService { get; set; }
        public bool UseHudSoundFallback { get; set; }

        public string BannerSpeechText { get; set; }

        // Minimum milliseconds between any two global alerts.
        public int MinGlobalAlertGapMs { get; set; }

        // Minimum milliseconds before the same player can trigger again.
        public int PerPlayerFastRearmMs { get; set; }

        // Minimum milliseconds between consecutive Speak() calls.
        // 0 = no extra gap beyond the global alert gate.
        public int TtsMinGapMs { get; set; }

        // How often BeforeRender polls player animations (milliseconds).
        // Lower = faster response; 80–120 ms is safe and responsive.
        public int ScanIntervalMs { get; set; }

        // When true, the local player is included in the scan.
        // Useful for solo testing — set false in production.
        public bool IncludeSelfForDebug { get; set; }

        // When true, writes diagnostic lines to plugins.txt via Hud.Debug().
        public bool DebugLog { get; set; }

        // When true, only logs the local player (reduces log noise).
        public bool DebugLogOnlySelf { get; set; }

        // -----------------------------------------------------------------------
        // Internal state
        // -----------------------------------------------------------------------

        // Tracks which players are currently in a banner-drop animation window.
        // Rising-edge only: alert fires once per window, not every frame.
        private readonly HashSet<int> ActiveBannerAnimPlayers = new HashSet<int>();

        // Per-player rearm timestamps.
        private readonly Dictionary<int, long> LastFastAlertByPlayerMs = new Dictionary<int, long>();

        // Known banner-drop animation enums (fast O(1) HashSet lookup).
        private readonly HashSet<AnimSnoEnum> KnownBannerDropAnims = new HashSet<AnimSnoEnum>();

        private int    _myIndex              = -1;
        private long   _lastGlobalAlertMs    =  0;
        private long   _lastScanMs           =  0;

        private bool   _globalTtsEnabled     = true;
        private int    _globalTtsVolume      = 100;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        public s7o_BannerSoundPlugin()
        {
            Enabled = true;

            UseTTS              = true;
            PreferTtsBroadcastService = true;
            UseHudSoundFallback = true;
            BannerSpeechText    = "Move to banner";

            MinGlobalAlertGapMs = 250;
            PerPlayerFastRearmMs= 1500;
            TtsMinGapMs         = 0;
            ScanIntervalMs      = 100;

            // Include self so you hear the alert when you drop your own banner.
            // Set false if you only want alerts for other players.
            IncludeSelfForDebug = true;
            DebugLog            = false;
            DebugLogOnlySelf    = false;

            LoadKnownBannerDropAnims();
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            // No sound files, no preloading. Global TTS is pushed by s7o_HUD_MENU.
            // Speech prefers s7o_TTS_Broadcast independent SAPI service, with Hud.Sound fallback only.
        }

        public void SetGlobalTtsSettings(bool enabled, int volume)
        {
            _globalTtsEnabled = enabled;
            _globalTtsVolume = Math.Max(0, Math.Min(100, volume));
        }

        // -----------------------------------------------------------------------
        // Area reset
        // -----------------------------------------------------------------------

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null)
            {
                if (newGame)
                {
                    _myIndex = -1;
                    ActiveBannerAnimPlayers.Clear();
                    LastFastAlertByPlayerMs.Clear();

                    _lastGlobalAlertMs = 0;
                    _lastScanMs        = 0;
                }

                return;
            }

            if (!newGame && _myIndex == Hud.Game.Me.Index)
                return;

            _myIndex = Hud.Game.Me.Index;

            ActiveBannerAnimPlayers.Clear();
            LastFastAlertByPlayerMs.Clear();

            _lastGlobalAlertMs = 0;
            _lastScanMs        = 0;
        }

        // -----------------------------------------------------------------------
        // Animation scan (throttled)
        // -----------------------------------------------------------------------

        public void BeforeRender()
        {
            if (!Hud.Game.IsInGame)
                return;

            long nowMs = Hud.Game.CurrentRealTimeMilliseconds;

            if (nowMs - _lastScanMs < ScanIntervalMs)
                return;

            _lastScanMs = nowMs;
            ScanPlayers(nowMs);
        }

        // -----------------------------------------------------------------------
        // Player animation scan
        // -----------------------------------------------------------------------

        private void ScanPlayers(long nowMs)
        {
            var players = Hud.Game.Players;
            if (players == null)
                return;

            foreach (var player in players)
            {
                if (player == null)
                    continue;

                if (!player.IsInGame || !player.HasValidActor || !player.CoordinateKnown)
                {
                    ActiveBannerAnimPlayers.Remove(player.Index);
                    continue;
                }

                // Self: only include during debug testing.
                if (player.IsMe && !IncludeSelfForDebug)
                {
                    ActiveBannerAnimPlayers.Remove(player.Index);
                    continue;
                }

                bool shouldLog = DebugLog && (!DebugLogOnlySelf || player.IsMe);

                // Read animation name once, safely.
                string animName = "";
                try { animName = player.Animation.ToString(); }
                catch { animName = ""; }

                // Detect via enum first, then string fallback.
                bool detectedByEnum   = false;
                bool detectedByString = false;
                DetectBannerDrop(player, animName, out detectedByEnum, out detectedByString);
                bool isBannerDrop = detectedByEnum || detectedByString;

                // Candidate log: fires when the animation name even mentions banner or drop.
                if (shouldLog)
                {
                    bool looksLikeCandidate =
                        animName.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        animName.IndexOf("drop",   StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksLikeCandidate)
                    {
                        bool alreadyActive = ActiveBannerAnimPlayers.Contains(player.Index);
                        bool cooldownOk    = CanAlertForPlayer(player.Index, nowMs);

                        Log("CANDIDATE"
                            + " player=" + player.Index
                            + " self="   + player.IsMe
                            + " anim="   + animName
                            + " byEnum=" + detectedByEnum
                            + " byStr="  + detectedByString
                            + " active=" + alreadyActive
                            + " cdOk="   + cooldownOk
                            + " speak="  + SafeIsSpeakEnabled());
                    }
                }

                if (isBannerDrop)
                {
                    bool alreadyActive = ActiveBannerAnimPlayers.Contains(player.Index);
                    bool cooldownOk    = CanAlertForPlayer(player.Index, nowMs);

                    if (!alreadyActive && cooldownOk)
                    {
                        // Attempt alert BEFORE marking active.
                        // This way a blocked attempt is still logged and can be retried
                        // on the next rising edge (after animation ends and restarts).
                        bool alerted = TryAlert(player.Index, nowMs, animName, shouldLog);

                        // Mark active regardless — prevents per-frame spam during one animation window.
                        ActiveBannerAnimPlayers.Add(player.Index);

                        if (alerted)
                            LastFastAlertByPlayerMs[player.Index] = nowMs;
                    }
                    else if (!alreadyActive)
                    {
                        // Cooldown blocked — still mark active so we don't spam the block log.
                        if (shouldLog)
                            Log("BLOCKED per-player cooldown, marking active — player=" + player.Index);

                        ActiveBannerAnimPlayers.Add(player.Index);
                    }
                }
                else
                {
                    // Animation ended — clear so the next drop produces a new rising edge.
                    ActiveBannerAnimPlayers.Remove(player.Index);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Detection
        // -----------------------------------------------------------------------

        private void DetectBannerDrop(
            IPlayer player,
            string animName,
            out bool byEnum,
            out bool byString)
        {
            byEnum   = false;
            byString = false;

            // Fast enum path.
            try { byEnum = KnownBannerDropAnims.Contains(player.Animation); }
            catch { byEnum = false; }

            if (byEnum)
                return;

            // String fallback — catches any variant not in the enum list
            // (confirmed to catch _wizard_male_1hs_orb_banner_drop and others).
            if (string.IsNullOrEmpty(animName))
                return;

            string lower = animName.ToLowerInvariant();
            byString = lower.Contains("banner") && lower.Contains("drop");
        }

        private bool CanAlertForPlayer(int playerIndex, long nowMs)
        {
            long lastMs;
            if (LastFastAlertByPlayerMs.TryGetValue(playerIndex, out lastMs))
                return nowMs - lastMs >= PerPlayerFastRearmMs;

            return true;
        }

        // -----------------------------------------------------------------------
        // Alert
        // -----------------------------------------------------------------------

        private bool TryAlert(int playerIndex, long nowMs, string animName, bool shouldLog)
        {
            // Global cooldown.
            if (_lastGlobalAlertMs > 0 && nowMs - _lastGlobalAlertMs < MinGlobalAlertGapMs)
            {
                if (shouldLog) Log("TryAlert BLOCKED global cooldown — player=" + playerIndex);
                return false;
            }

            if (!UseTTS)
            {
                if (shouldLog) Log("TryAlert BLOCKED UseTTS=false");
                return false;
            }

            if (!_globalTtsEnabled)
            {
                if (shouldLog) Log("TryAlert BLOCKED GlobalTTS=false");
                return false;
            }

            if (PreferTtsBroadcastService && TrySpeakViaTtsBroadcast(BannerSpeechText))
            {
                _lastGlobalAlertMs = nowMs;

                if (shouldLog)
                    Log("TryAlert SPOKE VIA TTS_BROADCAST \"" + BannerSpeechText + "\" for player=" + playerIndex + " anim=" + animName);

                return true;
            }

            if (!UseHudSoundFallback)
            {
                if (shouldLog) Log("TryAlert BLOCKED no TTS_Broadcast service and Hud.Sound fallback disabled");
                return false;
            }

            if (Hud.Sound == null)
            {
                if (shouldLog) Log("TryAlert BLOCKED Hud.Sound is null");
                return false;
            }

            if (!SafeIsSpeakEnabled())
            {
                if (shouldLog) Log("TryAlert BLOCKED IsSpeakEnabled=false");
                return false;
            }

            // Optional gap between consecutive Speak() calls.
            if (TtsMinGapMs > 0 && Hud.Sound.LastSpeak != null)
            {
                try
                {
                    if (!Hud.Sound.LastSpeak.TimerTest(TtsMinGapMs))
                    {
                        if (shouldLog) Log("TryAlert BLOCKED TtsMinGapMs not met");
                        return false;
                    }
                }
                catch { }
            }

            try
            {
                Hud.Sound.Speak(BannerSpeechText);
                _lastGlobalAlertMs = nowMs;

                if (shouldLog)
                    Log("TryAlert SPOKE VIA Hud.Sound \"" + BannerSpeechText + "\" for player=" + playerIndex + " anim=" + animName);

                return true;
            }
            catch (Exception ex)
            {
                if (shouldLog)
                    Log("TryAlert Hud.Sound.Speak() THREW — " + ex.GetType().Name + ": " + ex.Message);

                return false;
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

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

        private bool SafeIsSpeakEnabled()
        {
            try { return Hud.Sound.IsSpeakEnabled; }
            catch { return false; }
        }

        private void Log(string message)
        {
            try { Hud.Debug("s7o_BannerAnimDebug: " + message); }
            catch { }
        }

        // -----------------------------------------------------------------------
        // Known banner-drop animation enums
        // -----------------------------------------------------------------------
        // The string fallback covers any enum not listed here, so missing a gender
        // variant only costs the enum fast-path — detection still works.

        private void LoadKnownBannerDropAnims()
        {
            // Barbarian
            KnownBannerDropAnims.Add(AnimSnoEnum._barbarian_female_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._barbarian_male_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._barbarian_female_1ht_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._barbarian_male_1ht_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._barbarian_male_2ht_banner_drop);

            // Demon Hunter
            KnownBannerDropAnims.Add(AnimSnoEnum._demonhunter_female_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._demonhunter_male_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._demonhunter_female_dw_xbow_banner_drop);

            // Wizard
            KnownBannerDropAnims.Add(AnimSnoEnum._wizard_male_hth_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._wizard_female_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._wizard_male_1hs_orb_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._wizard_female_1hs_orb_banner_drop);

            // Witch Doctor
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_female_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_male_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_male_1ht_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_female_1ht_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_male_2ht_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._witchdoctor_female_2ht_banner_drop);

            // Monk (female confirmed; male caught by string fallback if enum differs)
            KnownBannerDropAnims.Add(AnimSnoEnum._monk_female_banner_drop);

            // Crusader (female confirmed; male caught by string fallback if enum differs)
            KnownBannerDropAnims.Add(AnimSnoEnum._x1_crusader_female_banner_drop);

            // Necromancer
            KnownBannerDropAnims.Add(AnimSnoEnum._p6_necro_male_hth_cast_banner_drop);
            KnownBannerDropAnims.Add(AnimSnoEnum._p6_necro_female_hth_cast_banner_drop);
        }
    }
}
