using System;
using System.Collections.Generic;
using System.Reflection;
using SharpDX;
using SharpDX.Direct2D1;
using Turbo.Plugins;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Detection approach: fast player-animation scan plus safe native banner fallback.
    //
    // The animation path is instant when FreeHUD exposes remote animation data.
    // The native Hud.Game.Banners path is a reliability fallback for long-range
    // same-world banners, but it stores only lightweight coordinate keys -- never
    // IBanner wrapper references -- to avoid shutdown/area-transition retention risk.
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

    public class s7o_BannerSoundPlugin : BasePlugin, INewAreaHandler, IBeforeRenderHandler, IAfterCollectHandler, IInGameTopPainter
    {
        // -----------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------

        public bool UseTTS { get; set; }

        // Prefer the independent SAPI route exposed by s7o_TTS_Broadcast.
        // Native Hud.Sound is shared/global, so the broadcast service remains preferred.
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

        // Banner alerts are high priority. This bypasses Hud.Sound.LastSpeak spacing
        // so the banner request can be submitted immediately and queued by the TTS path.
        public bool BannerIgnoresHudSoundSpeakGap { get; set; }

        // Native same-world fallback. This is the reliable path when player animation
        // is not exposed at long range. It stores coordinate keys, not IBanner objects.
        public bool UseNativeBannerFallback { get; set; }
        public int NativeBannerFallbackDedupeMs { get; set; }
        public float NativeBannerCoordinateKeyGridYards { get; set; }

        // Animated direction arrow shown when a reliable banner/target coordinate is known.
        public bool ShowBannerDirectionArrow { get; set; }
        public int BannerArrowLifetimeMs { get; set; }
        public int BannerArrowFadeMs { get; set; }
        public int BannerArrowChevronCount { get; set; }
        public int BannerArrowCycleMs { get; set; }
        public float BannerArrowStartDistancePx { get; set; }
        public float BannerArrowChevronSpacingPx { get; set; }
        public float BannerArrowChevronLengthPx { get; set; }
        public float BannerArrowChevronWidthPx { get; set; }
        public float BannerArrowOutlinePx { get; set; }

        // RC4+: single floor-anchored arrow settings. The old chevron settings are
        // kept for config compatibility, but drawing now uses one glossy arrow.
        public float BannerArrowLengthPx { get; set; }
        public float BannerArrowShaftWidthPx { get; set; }
        public float BannerArrowHeadLengthPx { get; set; }
        public float BannerArrowHeadWidthPx { get; set; }
        public float BannerArrowMotionPx { get; set; }
        public float BannerArrowAnchorYOffsetPx { get; set; }

        // RC10: when the banner coordinate is visible on screen, the cue moves from
        // the player-foot anchor to the banner itself and points directly at it.
        public bool BannerArrowMoveToVisibleBanner { get; set; }
        public int BannerArrowFlyToBannerMs { get; set; }
        public float BannerArrowVisibleTargetOffsetPx { get; set; }

        // RC5/RC6: optional label support kept for config compatibility.
        // RC8 disables the label by default and draws one glossy bordered arrow.
        public bool ShowBannerArrowLabel { get; set; }
        public string BannerArrowLabelText { get; set; }
        public float BannerArrowLabelAlongOffsetPx { get; set; }
        public float BannerArrowLabelGapPx { get; set; }
        public float BannerArrowLabelOutlineRadiusPx { get; set; }

        public bool BannerArrowUseLastKnownPlayerCoordinate { get; set; }
        public int BannerLastKnownCoordinateMaxAgeMs { get; set; }

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

        private class PlayerCoordinateSnapshot
        {
            public float X;
            public float Y;
            public float Z;
            public uint WorldId;
            public long TimeMs;
        }

        private class BannerArrowAlert
        {
            public float X;
            public float Y;
            public float Z;
            public long StartMs;
            public long ExpireMs;
            public string Source;
        }

        private readonly Dictionary<int, PlayerCoordinateSnapshot> LastKnownPlayerCoordinates = new Dictionary<int, PlayerCoordinateSnapshot>();
        private readonly HashSet<string> KnownNativeBannerKeys = new HashSet<string>();
        private readonly Dictionary<string, long> AlertedNativeBannerKeysMs = new Dictionary<string, long>();

        private IBrush BannerArrowFillBrush;
        private IBrush BannerArrowOutlineBrush;
        private IBrush BannerArrowLightBrush;
        private IBrush BannerArrowShadowBrush;
        private IFont BannerArrowLabelFont;
        private IFont BannerArrowLabelOutlineFont;

        private BannerArrowAlert _activeBannerArrow;
        private bool   _nativeBannerKeysSeeded = false;
        private long   _lastAnimationAlertMs   = 0;
        private long   _lastNativeAlertMs      = 0;

        private bool   _bannerArrowTargetWasVisible = false;
        private long   _bannerArrowTargetVisibleSinceMs = 0;
        private float  _lastArrowUx = 1.0f;
        private float  _lastArrowUy = 0.0f;

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

            MinGlobalAlertGapMs = 1500;
            PerPlayerFastRearmMs= 6000;
            TtsMinGapMs         = 0;
            BannerIgnoresHudSoundSpeakGap = true;

            UseNativeBannerFallback = true;
            NativeBannerFallbackDedupeMs = 10000;
            NativeBannerCoordinateKeyGridYards = 2.0f;

            ShowBannerDirectionArrow = true;
            BannerArrowLifetimeMs = 10000;
            BannerArrowFadeMs = 700;
            BannerArrowChevronCount = 1;
            BannerArrowCycleMs = 700;
            BannerArrowStartDistancePx = 190.0f;
            BannerArrowChevronSpacingPx = 24.0f;
            BannerArrowChevronLengthPx = 28.0f;
            BannerArrowChevronWidthPx = 20.0f;
            BannerArrowOutlinePx = 6.0f;
            BannerArrowLengthPx = 52.0f;
            BannerArrowShaftWidthPx = 16.0f;
            BannerArrowHeadLengthPx = 21.0f;
            BannerArrowHeadWidthPx = 36.0f;
            BannerArrowMotionPx = 3.0f;
            BannerArrowAnchorYOffsetPx = 0.0f;
            BannerArrowMoveToVisibleBanner = true;
            BannerArrowFlyToBannerMs = 240;
            BannerArrowVisibleTargetOffsetPx = 18.0f;
            ShowBannerArrowLabel = false;
            BannerArrowLabelText = "BANNER";
            BannerArrowLabelAlongOffsetPx = 26.0f;
            BannerArrowLabelGapPx = 2.0f;
            BannerArrowLabelOutlineRadiusPx = 2.0f;
            BannerArrowUseLastKnownPlayerCoordinate = true;
            BannerLastKnownCoordinateMaxAgeMs = 20000;

            // Scan every render frame. Only four players are inspected, so this is cheap
            // and removes the extra 50 ms polling delay from the fast animation path.
            ScanIntervalMs      = 0;

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

            // Draw late so the banner direction cue is not hidden by normal UI/world overlays.
            Order = int.MaxValue;

            BannerArrowFillBrush = Hud.Render.CreateBrush(235, 240, 0, 0, 0);
            BannerArrowOutlineBrush = Hud.Render.CreateBrush(240, 0, 0, 0, 0);
            BannerArrowLightBrush = Hud.Render.CreateBrush(90, 255, 255, 255, 0);
            BannerArrowShadowBrush = Hud.Render.CreateBrush(105, 85, 0, 0, 0);
            BannerArrowLabelFont = Hud.Render.CreateFont("tahoma", 12.0f, 255, 240, 0, 0, true, false, 255, 0, 0, 0, true);
            BannerArrowLabelOutlineFont = Hud.Render.CreateFont("tahoma", 12.0f, 255, 0, 0, 0, true, false, 255, 0, 0, 0, true);

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
                    LastKnownPlayerCoordinates.Clear();
                    KnownNativeBannerKeys.Clear();
                    AlertedNativeBannerKeysMs.Clear();
                    _activeBannerArrow = null;
                    _nativeBannerKeysSeeded = false;
                    _lastAnimationAlertMs = 0;
                    _lastNativeAlertMs = 0;
                    _bannerArrowTargetWasVisible = false;
                    _bannerArrowTargetVisibleSinceMs = 0;
                    _lastArrowUx = 1.0f;
                    _lastArrowUy = 0.0f;

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
            LastKnownPlayerCoordinates.Clear();
            KnownNativeBannerKeys.Clear();
            AlertedNativeBannerKeysMs.Clear();
            _activeBannerArrow = null;
            _nativeBannerKeysSeeded = false;
            _lastAnimationAlertMs = 0;
            _lastNativeAlertMs = 0;
            _bannerArrowTargetWasVisible = false;
            _bannerArrowTargetVisibleSinceMs = 0;
            _lastArrowUx = 1.0f;
            _lastArrowUy = 0.0f;

            _lastGlobalAlertMs = 0;
            _lastScanMs        = 0;
        }

        // -----------------------------------------------------------------------
        // Native same-world banner fallback and top-layer arrow paint
        // -----------------------------------------------------------------------

        public void AfterCollect()
        {
            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            long nowMs = Hud.Game.CurrentRealTimeMilliseconds;

            UpdateAllKnownPlayerCoordinates(nowMs);

            if (UseNativeBannerFallback)
                ScanNativeBanners(nowMs);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip)
                return;

            DrawActiveBannerArrow();
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

                if (!player.IsInGame)
                {
                    ActiveBannerAnimPlayers.Remove(player.Index);
                    continue;
                }

                // Cache coordinates when FreeHUD exposes them, but do not require a
                // valid actor/coordinate before checking animation. Remote banner-drop
                // animation may still be available when the player is far away.
                UpdateKnownPlayerCoordinate(player, nowMs);

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
                        bool hasVisualCoordinate;
                        float visualX;
                        float visualY;
                        float visualZ;
                        hasVisualCoordinate = TryGetBannerVisualCoordinateForPlayer(player, nowMs, out visualX, out visualY, out visualZ);

                        bool alerted = TryAlert(player.Index, nowMs, animName, shouldLog, "animation", hasVisualCoordinate, visualX, visualY, visualZ);

                        // Mark active regardless — prevents per-frame spam during one animation window.
                        ActiveBannerAnimPlayers.Add(player.Index);

                        if (alerted)
                        {
                            LastFastAlertByPlayerMs[player.Index] = nowMs;
                            _lastAnimationAlertMs = nowMs;
                        }
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

        private bool TryAlert(int playerIndex, long nowMs, string animName, bool shouldLog, string source, bool hasCoordinate, float coordinateX, float coordinateY, float coordinateZ)
        {
            // Visual cue should appear as soon as a banner event is detected, even if
            // speech is disabled, queued, or blocked by another sound path.
            ActivateBannerArrowIfPossible(nowMs, source, hasCoordinate, coordinateX, coordinateY, coordinateZ);

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
                    Log("TryAlert SPOKE VIA TTS_BROADCAST \"" + BannerSpeechText + "\" source=" + source + " player=" + playerIndex + " anim=" + animName + " hasCoord=" + hasCoordinate);

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
            if (!BannerIgnoresHudSoundSpeakGap && TtsMinGapMs > 0 && Hud.Sound.LastSpeak != null)
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
                    Log("TryAlert SPOKE VIA Hud.Sound \"" + BannerSpeechText + "\" source=" + source + " player=" + playerIndex + " anim=" + animName + " hasCoord=" + hasCoordinate);

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

        private void UpdateAllKnownPlayerCoordinates(long nowMs)
        {
            try
            {
                var players = Hud.Game.Players;
                if (players == null)
                    return;

                foreach (var player in players)
                {
                    if (player == null || !player.IsInGame)
                        continue;

                    UpdateKnownPlayerCoordinate(player, nowMs);
                }
            }
            catch { }
        }

        private void UpdateKnownPlayerCoordinate(IPlayer player, long nowMs)
        {
            if (player == null)
                return;

            try
            {
                if (!player.CoordinateKnown)
                    return;

                var coordinate = player.FloorCoordinate;
                if (coordinate == null || !coordinate.IsValid)
                    return;

                LastKnownPlayerCoordinates[player.Index] = new PlayerCoordinateSnapshot
                {
                    X = coordinate.X,
                    Y = coordinate.Y,
                    Z = coordinate.Z,
                    WorldId = player.WorldId,
                    TimeMs = nowMs
                };
            }
            catch { }
        }

        private bool TryGetBannerVisualCoordinateForPlayer(IPlayer player, long nowMs, out float x, out float y, out float z)
        {
            x = 0;
            y = 0;
            z = 0;

            if (player == null)
                return false;

            try
            {
                if (player.CoordinateKnown)
                {
                    var coordinate = player.FloorCoordinate;
                    if (coordinate != null && coordinate.IsValid)
                    {
                        x = coordinate.X;
                        y = coordinate.Y;
                        z = coordinate.Z;
                        return true;
                    }
                }
            }
            catch { }

            if (!BannerArrowUseLastKnownPlayerCoordinate)
                return false;

            try
            {
                PlayerCoordinateSnapshot snapshot;
                if (!LastKnownPlayerCoordinates.TryGetValue(player.Index, out snapshot) || snapshot == null)
                    return false;

                if (nowMs - snapshot.TimeMs > BannerLastKnownCoordinateMaxAgeMs)
                    return false;

                x = snapshot.X;
                y = snapshot.Y;
                z = snapshot.Z;
                return true;
            }
            catch { }

            // RC6: If the animation path fires but the remote player has no current
            // coordinate, try to claim a newly visible native banner coordinate immediately.
            // This avoids waiting for the next AfterCollect fallback pass and also makes
            // the arrow appear in more cases when the animation event itself has no position.
            try
            {
                if (TryGetNewNativeBannerCoordinate(out x, out y, out z))
                    return true;
            }
            catch { }

            return false;
        }

        private bool TryGetNewNativeBannerCoordinate(out float x, out float y, out float z)
        {
            x = 0;
            y = 0;
            z = 0;

            try
            {
                if (!UseNativeBannerFallback || Hud == null || Hud.Game == null || Hud.Game.Banners == null)
                    return false;

                foreach (var banner in Hud.Game.Banners)
                {
                    if (banner == null)
                        continue;

                    var coordinate = banner.FloorCoordinate;
                    if (coordinate == null || !coordinate.IsValid)
                        continue;

                    string key = BuildNativeBannerKey(coordinate);
                    if (string.IsNullOrEmpty(key))
                        continue;

                    // Prefer banners that were not part of the seeded/known set.
                    // We do not add the key here; ScanNativeBanners owns that state.
                    // This keeps the normal fallback path intact while giving the
                    // immediate animation path a reliable coordinate when available.
                    if (KnownNativeBannerKeys.Contains(key))
                        continue;

                    x = coordinate.X;
                    y = coordinate.Y;
                    z = coordinate.Z;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void ScanNativeBanners(long nowMs)
        {
            try
            {
                var banners = Hud.Game.Banners;
                if (banners == null)
                {
                    _nativeBannerKeysSeeded = true;
                    return;
                }

                foreach (var banner in banners)
                {
                    if (banner == null)
                        continue;

                    var coordinate = banner.FloorCoordinate;
                    if (coordinate == null || !coordinate.IsValid)
                        continue;

                    string key = BuildNativeBannerKey(coordinate);
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (!_nativeBannerKeysSeeded)
                    {
                        KnownNativeBannerKeys.Add(key);
                        continue;
                    }

                    if (KnownNativeBannerKeys.Contains(key))
                        continue;

                    KnownNativeBannerKeys.Add(key);

                    bool duplicateOfRecentBannerAlert =
                        (_lastAnimationAlertMs > 0 && nowMs - _lastAnimationAlertMs <= NativeBannerFallbackDedupeMs) ||
                        (_lastGlobalAlertMs > 0 && nowMs - _lastGlobalAlertMs <= NativeBannerFallbackDedupeMs);
                    if (duplicateOfRecentBannerAlert)
                    {
                        // The animation path already spoke, or another banner alert was
                        // just submitted. Still refresh the arrow with the native banner
                        // coordinate because it is usually more precise, but do not speak twice.
                        ActivateBannerArrowIfPossible(nowMs, "native-banner-dedupe", true, coordinate.X, coordinate.Y, coordinate.Z);

                        if (DebugLog)
                            Log("Native banner fallback deduped after recent banner alert, arrow coordinate refreshed key=" + key);

                        continue;
                    }

                    long previousAlertMs;
                    if (AlertedNativeBannerKeysMs.TryGetValue(key, out previousAlertMs) && nowMs - previousAlertMs < PerPlayerFastRearmMs)
                        continue;

                    bool alerted = TryAlert(-1, nowMs, "native-banner-fallback", DebugLog, "native-banner-fallback", true, coordinate.X, coordinate.Y, coordinate.Z);
                    if (alerted)
                    {
                        AlertedNativeBannerKeysMs[key] = nowMs;
                        _lastNativeAlertMs = nowMs;
                    }
                }

                _nativeBannerKeysSeeded = true;
                PruneNativeBannerAlertMemory(nowMs);
            }
            catch (Exception ex)
            {
                if (DebugLog)
                    Log("ScanNativeBanners THREW — " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private string BuildNativeBannerKey(IWorldCoordinate coordinate)
        {
            if (coordinate == null)
                return string.Empty;

            float grid = NativeBannerCoordinateKeyGridYards;
            if (grid <= 0.1f)
                grid = 2.0f;

            try
            {
                uint worldId = 0;
                try
                {
                    if (Hud.Game != null && Hud.Game.Me != null)
                        worldId = Hud.Game.Me.WorldId;
                }
                catch { }

                int qx = (int)Math.Round(coordinate.X / grid);
                int qy = (int)Math.Round(coordinate.Y / grid);
                int qz = (int)Math.Round(coordinate.Z / grid);

                return worldId.ToString() + ":" + qx.ToString() + ":" + qy.ToString() + ":" + qz.ToString();
            }
            catch { }

            return string.Empty;
        }

        private void PruneNativeBannerAlertMemory(long nowMs)
        {
            try
            {
                if (AlertedNativeBannerKeysMs.Count <= 32)
                    return;

                var remove = new List<string>();
                foreach (var pair in AlertedNativeBannerKeysMs)
                {
                    if (nowMs - pair.Value > 30000)
                        remove.Add(pair.Key);
                }

                foreach (string key in remove)
                    AlertedNativeBannerKeysMs.Remove(key);
            }
            catch { }
        }

        private void ActivateBannerArrowIfPossible(long nowMs, string source, bool hasCoordinate, float x, float y, float z)
        {
            if (!ShowBannerDirectionArrow || !hasCoordinate)
                return;

            _activeBannerArrow = new BannerArrowAlert
            {
                X = x,
                Y = y,
                Z = z,
                StartMs = nowMs,
                ExpireMs = nowMs + Math.Max(500, BannerArrowLifetimeMs),
                Source = source ?? string.Empty
            };

            // New banner event: start in normal player-tracking mode. If the target
            // is already visible this will transition to the banner anchor on the
            // next paint without delaying the initial cue.
            _bannerArrowTargetWasVisible = false;
            _bannerArrowTargetVisibleSinceMs = 0;
        }

        private void DrawActiveBannerArrow()
        {
            if (!ShowBannerDirectionArrow || _activeBannerArrow == null)
                return;

            if (Hud == null || Hud.Game == null || !Hud.Game.IsInGame || Hud.Game.Me == null)
                return;

            long nowMs = Hud.Game.CurrentRealTimeMilliseconds;
            if (nowMs > _activeBannerArrow.ExpireMs)
            {
                _activeBannerArrow = null;
                _bannerArrowTargetWasVisible = false;
                _bannerArrowTargetVisibleSinceMs = 0;
                return;
            }

            if (BannerArrowFillBrush == null || BannerArrowOutlineBrush == null)
                return;

            try
            {
                var me = Hud.Game.Me.FloorCoordinate;
                if (me == null || !me.IsValid)
                    return;

                float dxWorld = _activeBannerArrow.X - me.X;
                float dyWorld = _activeBannerArrow.Y - me.Y;
                float distanceSq = dxWorld * dxWorld + dyWorld * dyWorld;
                if (distanceSq < 4.0f)
                    return;

                float distance = (float)Math.Sqrt(distanceSq);
                float nxWorld = dxWorld / distance;
                float nyWorld = dyWorld / distance;

                // Always start from the player's foot coordinate so the normal cue
                // tracks the player cleanly. When the banner itself becomes visible,
                // we interpolate the arrow to the banner screen coordinate instead.
                var screenMe = me.ToScreenCoordinate(true, true);
                var screenStep = me.Offset(nxWorld * 10.0f, nyWorld * 10.0f, 0).ToScreenCoordinate(true, true);
                if (screenMe == null || screenStep == null)
                    return;

                float ux = screenStep.X - screenMe.X;
                float uy = screenStep.Y - screenMe.Y;

                bool targetOnScreen = false;
                float targetScreenX = 0.0f;
                float targetScreenY = 0.0f;

                // If the actual banner coordinate is visible, use screen-space player ->
                // banner direction and move the arrow to the banner. This prevents the
                // short-distance wobble that can happen when the cue is still anchored
                // at the player's feet while the target is very close.
                try
                {
                    if (BannerArrowMoveToVisibleBanner)
                    {
                        var targetCoordinate = me.Offset(dxWorld, dyWorld, _activeBannerArrow.Z - me.Z);
                        if (targetCoordinate != null && targetCoordinate.IsValid && targetCoordinate.IsOnScreen(1))
                        {
                            var screenTarget = targetCoordinate.ToScreenCoordinate(true, true);
                            if (screenTarget != null)
                            {
                                float sx = screenTarget.X - screenMe.X;
                                float sy = screenTarget.Y - screenMe.Y;
                                float sl = (float)Math.Sqrt(sx * sx + sy * sy);
                                if (sl >= 0.01f)
                                {
                                    ux = sx;
                                    uy = sy;
                                    targetScreenX = screenTarget.X;
                                    targetScreenY = screenTarget.Y;
                                    targetOnScreen = true;
                                }
                            }
                        }
                    }
                }
                catch { }

                float screenLen = (float)Math.Sqrt(ux * ux + uy * uy);
                if (screenLen < 0.01f)
                {
                    ux = _lastArrowUx;
                    uy = _lastArrowUy;
                    screenLen = (float)Math.Sqrt(ux * ux + uy * uy);
                    if (screenLen < 0.01f)
                        return;
                }

                ux /= screenLen;
                uy /= screenLen;

                _lastArrowUx = ux;
                _lastArrowUy = uy;

                if (targetOnScreen != _bannerArrowTargetWasVisible)
                {
                    _bannerArrowTargetWasVisible = targetOnScreen;
                    _bannerArrowTargetVisibleSinceMs = targetOnScreen ? nowMs : 0;
                }

                float px = -uy;
                float py = ux;

                float alpha = 1.0f;
                long remainingMs = _activeBannerArrow.ExpireMs - nowMs;
                if (BannerArrowFadeMs > 0 && remainingMs < BannerArrowFadeMs)
                    alpha = Math.Max(0.0f, Math.Min(1.0f, remainingMs / (float)BannerArrowFadeMs));

                BannerArrowFillBrush.Opacity = alpha;
                BannerArrowOutlineBrush.Opacity = alpha;
                if (BannerArrowLightBrush != null) BannerArrowLightBrush.Opacity = alpha * 0.45f;
                if (BannerArrowShadowBrush != null) BannerArrowShadowBrush.Opacity = alpha * 0.55f;

                int cycle = BannerArrowCycleMs;
                if (cycle < 150)
                    cycle = 750;

                // Small fore/back motion so the cue feels alive without sliding into the feet.
                float t = ((nowMs - _activeBannerArrow.StartMs) % cycle) / (float)cycle;
                float pulse = (float)Math.Sin(t * Math.PI * 2.0) * Math.Max(0.0f, BannerArrowMotionPx);

                float length = Math.Max(28.0f, BannerArrowLengthPx);
                float shaftWidth = Math.Max(6.0f, BannerArrowShaftWidthPx);
                float headLength = Math.Max(10.0f, Math.Min(length - 8.0f, BannerArrowHeadLengthPx));
                float headWidth = Math.Max(shaftWidth + 12.0f, BannerArrowHeadWidthPx);
                float outline = Math.Max(0.0f, BannerArrowOutlinePx);

                // Normal mode: a player-foot anchored arrow, offset far enough that it
                // does not overlap the model/foot radius circle.
                float playerBaseX = screenMe.X + ux * (BannerArrowStartDistancePx + pulse);
                float playerBaseY = screenMe.Y + BannerArrowAnchorYOffsetPx + uy * (BannerArrowStartDistancePx + pulse);

                float baseX = playerBaseX;
                float baseY = playerBaseY;

                if (targetOnScreen)
                {
                    // Visible-banner mode: put the arrow near the banner and make the
                    // tip point at it. The short interpolation gives the requested
                    // "fly to the banner" feel without a heavy animation system.
                    float targetOffset = Math.Max(0.0f, BannerArrowVisibleTargetOffsetPx);
                    float targetBaseX = targetScreenX - ux * (length + targetOffset - pulse);
                    float targetBaseY = targetScreenY - uy * (length + targetOffset - pulse);

                    float flyT = 1.0f;
                    int flyMs = BannerArrowFlyToBannerMs;
                    if (flyMs > 0 && _bannerArrowTargetVisibleSinceMs > 0)
                    {
                        flyT = Math.Max(0.0f, Math.Min(1.0f, (nowMs - _bannerArrowTargetVisibleSinceMs) / (float)flyMs));
                        // Smoothstep easing: quick start, gentle settle near the banner.
                        flyT = flyT * flyT * (3.0f - 2.0f * flyT);
                    }

                    baseX = playerBaseX + (targetBaseX - playerBaseX) * flyT;
                    baseY = playerBaseY + (targetBaseY - playerBaseY) * flyT;
                }

                // RC10 keeps the RC9 glossy badge-style arrow: thick black outline,
                // red body, darker lower bevel, and a white/pink highlight streak.
                float outlineBaseX = baseX - ux * outline;
                float outlineBaseY = baseY - uy * outline;

                // Soft screen-space drop shadow behind the outline. This is intentionally
                // separate from the black border so the border stays crisp.
                if (BannerArrowShadowBrush != null)
                {
                    BannerArrowShadowBrush.Opacity = alpha * 0.28f;
                    DrawSingleArrowGeometry(outlineBaseX + 4.0f, outlineBaseY + 5.0f, ux, uy, px, py,
                        length + outline * 2.5f,
                        shaftWidth + outline * 2.0f,
                        headLength + outline * 2.2f,
                        headWidth + outline * 2.4f,
                        BannerArrowShadowBrush,
                        0.0f);
                }

                // Full black outer shell. The base moves backward and the tip extends
                // forward, so every edge including the flat tail has a real border.
                DrawSingleArrowGeometry(outlineBaseX, outlineBaseY, ux, uy, px, py,
                    length + outline * 2.5f,
                    shaftWidth + outline * 2.0f,
                    headLength + outline * 2.2f,
                    headWidth + outline * 2.4f,
                    BannerArrowOutlineBrush,
                    0.0f);

                // Main red body.
                DrawSingleArrowGeometry(baseX, baseY, ux, uy, px, py,
                    length,
                    shaftWidth,
                    headLength,
                    headWidth,
                    BannerArrowFillBrush,
                    0.0f);

                // Darker lower bevel inside the red body, similar to the sample image.
                if (BannerArrowShadowBrush != null)
                {
                    BannerArrowShadowBrush.Opacity = alpha * 0.50f;
                    DrawBannerArrowLowerBevelGeometry(baseX, baseY, ux, uy, px, py,
                        length, shaftWidth, headLength, headWidth, BannerArrowShadowBrush);
                }

                // Thin bright highlight across the top of the shaft and the upper head edge.
                if (BannerArrowLightBrush != null)
                {
                    BannerArrowLightBrush.Opacity = alpha * 0.70f;
                    DrawBannerArrowHighlightGeometry(baseX, baseY, ux, uy, px, py,
                        length, shaftWidth, headLength, headWidth, BannerArrowLightBrush);
                }

                DrawBannerArrowLabel(baseX, baseY, ux, uy, alpha);
            }
            catch { }
        }

        private void DrawBannerArrowLabel(float baseX, float baseY, float ux, float uy, float alpha)
        {
            if (!ShowBannerArrowLabel || BannerArrowLabelFont == null || string.IsNullOrEmpty(BannerArrowLabelText))
                return;

            try
            {
                string text = BannerArrowLabelText;
                var layout = BannerArrowLabelFont.GetTextLayout(text);
                if (layout == null)
                    return;

                float width = layout.Metrics.Width;
                float height = layout.Metrics.Height;

                float labelCenterX = baseX + ux * BannerArrowLabelAlongOffsetPx;
                float labelBottomY = baseY - Math.Max(0.0f, BannerArrowLabelGapPx);
                float x = labelCenterX - width * 0.5f;
                float y = labelBottomY - height;

                // Label alpha is fixed because FreeHUD font opacity support varies by build.
                // The arrow geometry still fades; keeping text opaque also improves readability.

                int radius = (int)Math.Round(Math.Max(0.0f, BannerArrowLabelOutlineRadiusPx));
                if (BannerArrowLabelOutlineFont != null && radius > 0)
                {
                    var outlineLayout = BannerArrowLabelOutlineFont.GetTextLayout(text);
                    if (outlineLayout != null)
                    {
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            for (int oy = -radius; oy <= radius; oy++)
                            {
                                if (ox == 0 && oy == 0)
                                    continue;
                                if ((ox * ox) + (oy * oy) > radius * radius + 1)
                                    continue;
                                BannerArrowLabelOutlineFont.DrawText(outlineLayout, x + ox, y + oy);
                            }
                        }
                    }
                }

                BannerArrowLabelFont.DrawText(layout, x, y);
            }
            catch { }
        }

        private void DrawBannerArrowLowerBevelGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float bevel = Math.Max(4.0f, Math.Min(shaftWidth * 0.30f, 10.0f));
            float inset = Math.Max(2.0f, shaftWidth * 0.10f);

            // Local +p side is treated as the lower lit edge. It rotates with the arrow,
            // keeping the bevel contained inside the red face and away from the border.
            Vector2 tailOuter = new Vector2(baseX + px * (halfShaft - inset), baseY + py * (halfShaft - inset));
            Vector2 shaftOuter = new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset), baseY + uy * shaftLength + py * (halfShaft - inset));
            Vector2 headOuter = new Vector2(baseX + ux * shaftLength + px * (halfHead - inset), baseY + uy * shaftLength + py * (halfHead - inset));
            Vector2 nearTip = new Vector2(baseX + ux * (length - Math.Max(8.0f, headLength * 0.18f)) + px * Math.Max(1.5f, bevel * 0.35f), baseY + uy * (length - Math.Max(8.0f, headLength * 0.18f)) + py * Math.Max(1.5f, bevel * 0.35f));
            Vector2 headInner = new Vector2(baseX + ux * shaftLength + px * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel), baseY + uy * shaftLength + py * Math.Max(halfShaft * 0.25f, halfHead - inset - bevel));
            Vector2 shaftInner = new Vector2(baseX + ux * shaftLength + px * (halfShaft - inset - bevel), baseY + uy * shaftLength + py * (halfShaft - inset - bevel));
            Vector2 tailInner = new Vector2(baseX + px * (halfShaft - inset - bevel), baseY + py * (halfShaft - inset - bevel));

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(tailOuter, FigureBegin.Filled);
                    sink.AddLine(shaftOuter);
                    sink.AddLine(headOuter);
                    sink.AddLine(nearTip);
                    sink.AddLine(headInner);
                    sink.AddLine(shaftInner);
                    sink.AddLine(tailInner);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawBannerArrowHighlightGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;
            float yTop = -halfShaft + Math.Max(4.0f, shaftWidth * 0.22f);
            float thickness = Math.Max(2.0f, Math.Min(4.0f, shaftWidth * 0.14f));
            float start = Math.Max(6.0f, shaftWidth * 0.35f);
            float end = Math.Max(start + 6.0f, shaftLength - Math.Max(3.0f, headLength * 0.12f));

            // Top shaft highlight: a slim inset bar with squared ends.
            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    Vector2 a = new Vector2(baseX + ux * start + px * yTop, baseY + uy * start + py * yTop);
                    Vector2 b = new Vector2(baseX + ux * end + px * yTop, baseY + uy * end + py * yTop);
                    Vector2 c = new Vector2(baseX + ux * end + px * (yTop + thickness), baseY + uy * end + py * (yTop + thickness));
                    Vector2 d = new Vector2(baseX + ux * start + px * (yTop + thickness), baseY + uy * start + py * (yTop + thickness));
                    sink.BeginFigure(a, FigureBegin.Filled);
                    sink.AddLine(b);
                    sink.AddLine(c);
                    sink.AddLine(d);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }

            // Head highlight: a diagonal streak following the upper head edge.
            float diagStartX = shaftLength + Math.Max(2.0f, headLength * 0.12f);
            float diagEndX = length - Math.Max(8.0f, headLength * 0.16f);
            float diagStartY = -halfHead + Math.Max(6.0f, headWidth * 0.18f);
            float diagEndY = -Math.Max(2.0f, headWidth * 0.06f);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    Vector2 a = new Vector2(baseX + ux * diagStartX + px * diagStartY, baseY + uy * diagStartX + py * diagStartY);
                    Vector2 b = new Vector2(baseX + ux * diagEndX + px * diagEndY, baseY + uy * diagEndX + py * diagEndY);
                    Vector2 c = new Vector2(baseX + ux * diagEndX + px * (diagEndY + thickness), baseY + uy * diagEndX + py * (diagEndY + thickness));
                    Vector2 d = new Vector2(baseX + ux * diagStartX + px * (diagStartY + thickness), baseY + uy * diagStartX + py * (diagStartY + thickness));
                    sink.BeginFigure(a, FigureBegin.Filled);
                    sink.AddLine(b);
                    sink.AddLine(c);
                    sink.AddLine(d);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawSingleArrowGeometry(float baseX, float baseY, float ux, float uy, float px, float py, float length, float shaftWidth, float headLength, float headWidth, IBrush brush, float sideOffset)
        {
            if (brush == null || length <= 0 || shaftWidth <= 0 || headLength <= 0 || headWidth <= 0)
                return;

            float shaftLength = Math.Max(4.0f, length - headLength);
            float halfShaft = shaftWidth * 0.5f;
            float halfHead = headWidth * 0.5f;

            float ox = px * sideOffset;
            float oy = py * sideOffset;

            Vector2 tailLeft = new Vector2(baseX + ox - px * halfShaft, baseY + oy - py * halfShaft);
            Vector2 shaftLeft = new Vector2(baseX + ox + ux * shaftLength - px * halfShaft, baseY + oy + uy * shaftLength - py * halfShaft);
            Vector2 headLeft = new Vector2(baseX + ox + ux * shaftLength - px * halfHead, baseY + oy + uy * shaftLength - py * halfHead);
            Vector2 tip = new Vector2(baseX + ox + ux * length, baseY + oy + uy * length);
            Vector2 headRight = new Vector2(baseX + ox + ux * shaftLength + px * halfHead, baseY + oy + uy * shaftLength + py * halfHead);
            Vector2 shaftRight = new Vector2(baseX + ox + ux * shaftLength + px * halfShaft, baseY + oy + uy * shaftLength + py * halfShaft);
            Vector2 tailRight = new Vector2(baseX + ox + px * halfShaft, baseY + oy + py * halfShaft);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(tailLeft, FigureBegin.Filled);
                    sink.AddLine(shaftLeft);
                    sink.AddLine(headLeft);
                    sink.AddLine(tip);
                    sink.AddLine(headRight);
                    sink.AddLine(shaftRight);
                    sink.AddLine(tailRight);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
            }
        }

        private void DrawChevronGeometry(float cx, float cy, float ux, float uy, float px, float py, float length, float width, IBrush brush)
        {
            if (brush == null || length <= 0 || width <= 0)
                return;

            float halfLength = length * 0.5f;
            float halfWidth = width * 0.5f;

            // Concave six-point chevron, like a filled "greater than" marker.
            Vector2 tip = new Vector2(cx + ux * halfLength, cy + uy * halfLength);
            Vector2 frontTop = new Vector2(cx + ux * (length * 0.16f) + px * halfWidth, cy + uy * (length * 0.16f) + py * halfWidth);
            Vector2 backTop = new Vector2(cx - ux * halfLength + px * halfWidth, cy - uy * halfLength + py * halfWidth);
            Vector2 notch = new Vector2(cx - ux * (length * 0.12f), cy - uy * (length * 0.12f));
            Vector2 backBottom = new Vector2(cx - ux * halfLength - px * halfWidth, cy - uy * halfLength - py * halfWidth);
            Vector2 frontBottom = new Vector2(cx + ux * (length * 0.16f) - px * halfWidth, cy + uy * (length * 0.16f) - py * halfWidth);

            using (var geometry = Hud.Render.CreateGeometry())
            {
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(tip, FigureBegin.Filled);
                    sink.AddLine(frontTop);
                    sink.AddLine(backTop);
                    sink.AddLine(notch);
                    sink.AddLine(backBottom);
                    sink.AddLine(frontBottom);
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }

                brush.DrawGeometry(geometry);
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

                    // Some versions expose SpeakExternal as void and still speak/queue the
                    // line. Treat a successful void invocation as handled so Hud.Sound
                    // fallback does not also speak and create a duplicate banner alert.
                    if (result == null)
                        return true;

                    if (result is bool)
                        return (bool)result;

                    return true;
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
