using System;
using System.Collections.Generic;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_Oculus_Triune : BasePlugin, IInGameWorldPainter, ICustomizer
    {
        private const uint TriuneWillSno = 483606u;

        // CONFIRMED from debug log — all three circles use SNO 488071 on their proxy actor,
        // differentiated only by buff slot. Player buff icon appears when standing inside.
        //   Purple (damage):         proxy B1N, player icon 2
        //   Blue   (CDR):            proxy B7N, player icon 8
        //   Green  (resource cost):  proxy B6N, player icon 5
        private const uint TriuneProxySno = 488071u;
        private const int TriunePurplePlayerIcon = 2;
        private const int TriuneBluePlayerIcon   = 8;
        private const int TriuneGreenPlayerIcon  = 5;

        // ============================================================
        // USER SETTINGS
        // ============================================================

        public bool DisableDefaultOculusPlugin { get; set; }
        public bool DrawWhenDefaultPluginCannotBeDisabled { get; set; }

        public bool ShowOculusCircle { get; set; }
        public bool ShowTriuneBlueCircle { get; set; }
        public bool ShowTriuneGreenCircle { get; set; }
        public bool ShowTriunePurpleCircle { get; set; }
        public bool ShowInactiveCircles { get; set; }

        public bool AnimateWhenPlayerInside { get; set; }
        public bool ShowActiveLabel { get; set; }
        public bool ShowCountdownText { get; set; }
        public bool ShowCountdownDial { get; set; }
        public float CountdownDialRadius { get; set; }
        public int CountdownDialSegments { get; set; }

        public float OculusRadius { get; set; }
        public float OtherBonusCircleRadius { get; set; }
        public float PlayerInsideTolerance { get; set; }

        public float BaseStrokeWidth { get; set; }
        public float ActiveStrokeWidth { get; set; }
        public float ShadowStrokeWidth { get; set; }

        public int AnimationTickCount { get; set; }
        public float AnimationInnerRadiusScale { get; set; }
        public float AnimationOuterRadiusScale { get; set; }
        public int AnimationCycleMs { get; set; }

        public bool ShowRotatingOctagon { get; set; }
        public int OctagonSides { get; set; }
        public float OctagonRadiusScale { get; set; }
        public int OctagonRotationCycleMs { get; set; }

        public int OculusLifetimeSeconds { get; set; }
        public int OtherCircleLifetimeSeconds { get; set; }

        // Optional mapping/debug aid for Oculus/Triune bonus circle actors.
        // Keep false for normal gameplay.
        public bool DebugLogUnknownBonusProxies { get; set; }

        // Unsafe diagnostic fallback.
        // Keep false in normal gameplay. When true, generic proxies may be classified
        // by the player's current Triune buff, which can create false duplicate circles.
        public bool UseTriunePlayerBuffProxyFallback { get; set; }

        // Raw diagnostic test overlay.
        // White = every valid Generic_Proxy.
        // Yellow = confirmed purple Triune proxy: 488071 + Power_Buff_1_Visual_Effect_None.
        // Cyan/blue = likely blue fallback proxy while the mapped Triune blue player signal is active.
        public bool RawTestMode { get; set; }
        public bool DebugDumpNearbyActors { get; set; }
        public float NearbyDumpRadius { get; set; }

        // ============================================================

        private sealed class BonusCircleProfile
        {
            public string Code;

            // Direct actor SNO detection, used by Triune dome actors.
            public ActorSnoEnum[] ActorSnos;

            // Attribute/power detection, used by Oculus and fallback proxy detection.
            public uint[] PowerSnos;
            public IAttribute[] Attributes;

            public float Radius;
            public int LifetimeSeconds;
            public bool Enabled;
            public IBrush BaseBrush;
            public IBrush ActiveBrush;
            public IBrush ShadowBrush;
            public IBrush TickBrush;
            public IBrush PulseBrush;
            public IBrush DialBrush;   // countdown arc — matches the circle's own color
            public IFont LabelFont;
            public IFont DescriptorFont;  // DMG / CDR / RCR label — colored to match the circle
            public string LabelText;
        }

        private readonly List<BonusCircleProfile> _profiles = new List<BonusCircleProfile>();
        private IFont _countdownFont;
        private IBrush _activeOctagonOutlineBrush;
        private IBrush _activePulseOutlineBrush;
        private IBrush _ringOutlineBrush;
        private IBrush _countdownDialBackBrush;
        private IBrush _countdownDialProgressBrush;
        private IBrush _rawProxyBrush;
        private IBrush _rawPurpleProxyBrush;
        private IBrush _rawBlueProxyBrush;
        private int _lastUnknownProxyDebugTick;
        private int _lastNearbyDumpTick;

        public s7o_Oculus_Triune()
        {
            Enabled = true;

            DisableDefaultOculusPlugin = true;
            DrawWhenDefaultPluginCannotBeDisabled = true;

            ShowOculusCircle = true;
            ShowTriuneBlueCircle = true;
            ShowTriuneGreenCircle = true;
            ShowTriunePurpleCircle = true;
            ShowInactiveCircles = true;

            AnimateWhenPlayerInside = true;
            ShowActiveLabel = false;
            ShowCountdownText = true;
            ShowCountdownDial = true;
            CountdownDialRadius = 15.0f;
            CountdownDialSegments = 32;

            OculusRadius = 10.0f;
            OtherBonusCircleRadius = 10.0f;
            // 2.0 yard edge tolerance so animation triggers at the visible circle edge.
            PlayerInsideTolerance = 2.0f;

            // Preserve the original visual style, but make the black outline read more
            // like a strong shadow and make the colored strokes slightly fuller.
            BaseStrokeWidth   = 5.5f;
            ActiveStrokeWidth = 5.5f;
            ShadowStrokeWidth = 11.0f;

            // Inner pulse speed.
            AnimationTickCount = 16; // retained for compatibility; no longer used by the octagon.
            AnimationInnerRadiusScale = 0.86f;
            AnimationOuterRadiusScale = 1.10f;
            AnimationCycleMs = 1200;

            // Outer active marker: slow rotating octagon instead of radial spinning tick lines.
            ShowRotatingOctagon = true;
            OctagonSides = 8;
            OctagonRadiusScale = 1.08f;
            // Active octagon spin. 1500ms is slower/smoother than the diagnostic 1000ms.
            OctagonRotationCycleMs = 1500;

            OculusLifetimeSeconds = 7;
            OtherCircleLifetimeSeconds = 7;

            // Detection complete. All three circle SNOs and player buff icons confirmed.
            DebugLogUnknownBonusProxies = false;
            UseTriunePlayerBuffProxyFallback = false;
            RawTestMode = false;
            DebugDumpNearbyActors = false;
            NearbyDumpRadius = 20.0f;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            // Draw very late so this plugin visually overrides the default green Oculus circle
            // if the default plugin is still enabled for any reason.
            Order = 999999;

            CreateBrushesAndFonts();
            BuildProfiles();
        }

        public void Customize()
        {
            if (!DisableDefaultOculusPlugin)
                return;

            try
            {
                Hud.TogglePlugin<OculusPlugin>(false);
            }
            catch
            {
                // If this fails, this plugin still draws late and thick enough to visually override it.
            }
        }

        private void CreateBrushesAndFonts()
        {
            _countdownFont  = Hud.Render.CreateFont("tahoma", 13.0f, 255, 255, 255, 255, true, false, 220, 0, 0, 0, true);

            // Outer elements get a thick black outline. The colored strokes are also wider
            // so the outline peeks out cleanly without reducing the visible color width.
            _activeOctagonOutlineBrush = Hud.Render.CreateBrush(220,   0,   0,   0, 10.0f);
            _ringOutlineBrush          = Hud.Render.CreateBrush(190,   0,   0,   0, ShadowStrokeWidth);

            // Inner pulse ring stays borderless — no outline brush.
            _activePulseOutlineBrush = null;

            _countdownDialBackBrush     = Hud.Render.CreateBrush(130,   0,   0,   0, 2.5f);
            _countdownDialProgressBrush = Hud.Render.CreateBrush(240, 255, 255, 255, 2.8f);

            _rawProxyBrush       = Hud.Render.CreateBrush(185, 255, 255, 255, 1.5f);
            _rawPurpleProxyBrush = Hud.Render.CreateBrush(255, 255, 230,  40, 2.5f);
            _rawBlueProxyBrush   = Hud.Render.CreateBrush(255,  80, 210, 255, 2.5f);
        }

        private void BuildProfiles()
        {
            _profiles.Clear();

            // Oculus: uniform gold. One color, alpha-only variation between states.
            // R=255 G=215 B=0 — classic gold, same hue across inactive ring, pulse and octagon.
            AddProfile(
                "Oculus",
                null,
                new uint[] { GetSno(Hud.Sno.SnoPowers.OculusRing, 402461) },
                BuildVisualAttributes(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None),
                OculusRadius,
                OculusLifetimeSeconds,
                ShowOculusCircle,
                "",      // no descriptor for Oculus
                Hud.Render.CreateBrush(220, 255, 215,   0, BaseStrokeWidth),   // inactive ring
                Hud.Render.CreateBrush(255, 255, 215,   0, ActiveStrokeWidth), // active ring (fallback)
                null,                                                            // no shadow
                Hud.Render.CreateBrush(255, 255, 215,   0, 5.25f),              // octagon (thicker)
                Hud.Render.CreateBrush( 90, 255, 215,   0, 3.25f),              // pulse fill
                Hud.Render.CreateFont("tahoma", 9.0f, 255, 255, 215, 0, true, false, 180, 0, 0, 0, true)
                );

            ActorSnoEnum[] triuneBlueActors = new ActorSnoEnum[]
            {
                ActorSnoEnum._p2_itempassive_unique_ring_017_dome_blue
            };

            ActorSnoEnum[] triuneGreenActors = new ActorSnoEnum[]
            {
                // Teal is the green-ish Triune ground ring variant.
                ActorSnoEnum._p2_itempassive_unique_ring_017_dome_teal
            };

            ActorSnoEnum[] triunePurpleActors = new ActorSnoEnum[]
            {
                ActorSnoEnum._p2_itempassive_unique_ring_017_dome_purple,
                // Altar / P75 variant seen in the actor enum.
                ActorSnoEnum._p75_itempassive_unique_ring_017_dome_purple_red
            };

            // Triune Love / Damage: purple.
            // CONFIRMED: Generic_Proxy + 488071 + Power_Buff_1_Visual_Effect_None.
            // Do NOT add TriuneWillSno (483606) here — the player actor itself carries
            // [483606:B0N=1] constantly, which would cause false matches.
            AddProfile(
                "TriunePurple",
                triunePurpleActors,
                new uint[] { 488071u },
                BuildVisualAttributes(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None),
                OtherBonusCircleRadius,
                OtherCircleLifetimeSeconds,
                ShowTriunePurpleCircle,
                "DMG",
                Hud.Render.CreateBrush(220, 185,  60, 255, BaseStrokeWidth),
                Hud.Render.CreateBrush(255, 185,  60, 255, ActiveStrokeWidth),
                null,
                Hud.Render.CreateBrush(255, 185,  60, 255, 5.25f),
                Hud.Render.CreateBrush( 90, 185,  60, 255, 3.25f),
                Hud.Render.CreateFont("tahoma", 9.0f, 255, 185, 60, 255, true, false, 180, 0, 0, 0, true),
                Hud.Render.CreateFont("tahoma", 13.0f, 255, 185, 60, 255, true, false, 255, 0, 0, 0, true)
                );

            // Triune Creation / Cooldown: blue.
            // CONFIRMED: proxy uses [488071:B7N=1], player gets BuffIsActive(488071,8) when inside.
            AddProfile(
                "TriuneBlue",
                triuneBlueActors,
                new uint[] { TriuneProxySno },
                BuildVisualAttributes(Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_None),
                OtherBonusCircleRadius,
                OtherCircleLifetimeSeconds,
                ShowTriuneBlueCircle,
                "CDR",
                Hud.Render.CreateBrush(220,   0, 200, 255, BaseStrokeWidth),
                Hud.Render.CreateBrush(255,   0, 200, 255, ActiveStrokeWidth),
                null,
                Hud.Render.CreateBrush(255,   0, 200, 255, 5.25f),
                Hud.Render.CreateBrush( 90,   0, 200, 255, 3.25f),
                Hud.Render.CreateFont("tahoma", 9.0f, 255, 0, 200, 255, true, false, 180, 0, 0, 0, true),
                Hud.Render.CreateFont("tahoma", 13.0f, 255, 0, 200, 255, true, false, 255, 0, 0, 0, true)
                );

            // Triune Determination / Resource Cost Reduction: green.
            // CONFIRMED: proxy uses [488071:B6N=1], player gets BuffIsActive(488071,5) when inside.
            AddProfile(
                "TriuneGreen",
                triuneGreenActors,
                new uint[] { TriuneProxySno },
                BuildVisualAttributes(Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_None),
                OtherBonusCircleRadius,
                OtherCircleLifetimeSeconds,
                ShowTriuneGreenCircle,
                "RCR",
                Hud.Render.CreateBrush(220,   0, 230,  80, BaseStrokeWidth),
                Hud.Render.CreateBrush(255,   0, 230,  80, ActiveStrokeWidth),
                null,
                Hud.Render.CreateBrush(255,   0, 230,  80, 5.25f),
                Hud.Render.CreateBrush( 90,   0, 230,  80, 3.25f),
                Hud.Render.CreateFont("tahoma", 9.0f, 255, 0, 230, 80, true, false, 180, 0, 0, 0, true),
                Hud.Render.CreateFont("tahoma", 13.0f, 255, 0, 230, 80, true, false, 255, 0, 0, 0, true)
                );
        }

        private void AddProfile(
            string code,
            ActorSnoEnum[] actorSnos,
            uint[] powerSnos,
            IAttribute[] attributes,
            float radius,
            int lifetimeSeconds,
            bool enabled,
            string labelText,
            IBrush baseBrush,
            IBrush activeBrush,
            IBrush shadowBrush,
            IBrush tickBrush,
            IBrush pulseBrush,
            IFont labelFont,
            IFont descriptorFont = null)
        {
            bool hasActorSnos = actorSnos != null && actorSnos.Length > 0;
            bool hasPowerAttributes = powerSnos != null && powerSnos.Length > 0 && attributes != null && attributes.Length > 0;

            if (!hasActorSnos && !hasPowerAttributes)
                return;

            IBrush dialBrush = tickBrush;

            _profiles.Add(new BonusCircleProfile
            {
                Code = code,
                ActorSnos = actorSnos,
                PowerSnos = powerSnos,
                Attributes = attributes,
                Radius = radius,
                LifetimeSeconds = lifetimeSeconds,
                Enabled = enabled,
                LabelText = labelText,
                BaseBrush = baseBrush,
                ActiveBrush = activeBrush,
                ShadowBrush = shadowBrush,
                TickBrush = tickBrush,
                PulseBrush = pulseBrush,
                DialBrush = dialBrush,
                LabelFont = labelFont,
                DescriptorFont = descriptorFont
            });
        }

        private uint GetSno(ISnoPower power, uint fallback)
        {
            try
            {
                if (power != null && power.Sno != 0)
                    return power.Sno;
            }
            catch
            {
            }

            return fallback;
        }

        private uint[] BuildTriunePowerSnos()
        {
            return new uint[]
            {
                // Older/community Triune's Will power.
                GetSno(Hud.Sno.SnoPowers.Generic_CommunityBuffTriunesWill, 483606),

                // Altar potion powers from LightningMOD references.
                // 488004 is the Triune's Will potion effect/unlock path.
                GetSno(Hud.Sno.SnoPowers.Generic_P75ItemPassiveDarkAlchemyMajor001, 488004),

                // Add these as fallbacks because the altar potion implementation also references them.
                GetSno(Hud.Sno.SnoPowers.Generic_P75ItemPassiveDarkAlchemyMajor002, 488036),
                GetSno(Hud.Sno.SnoPowers.Generic_P75ItemPassiveDarkAlchemyMajor003, 488037)
            };
        }

        private IAttribute[] BuildVisualAttributes(params IAttribute[] attrs)
        {
            return attrs;
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Ground)
                return;

            if (!Enabled || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                return;

            if (!Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;

            var actors = Hud.Game.Actors;
            if (actors == null)
                return;

            if (DebugDumpNearbyActors)
                DumpNearbyActors(actors);

            foreach (var actor in actors)
            {
                if (RawTestMode)
                    DrawRawDiagnosticRings(actor);

                if (!IsCandidateBonusCircleActor(actor))
                    continue;

                var profile = GetMatchingProfile(actor);
                if (profile == null)
                {
                    DebugUnknownBonusCircleActor(actor);
                    continue;
                }

                var coord = GetBestCoordinate(actor);
                if (coord == null || !coord.IsValid)
                    continue;

                PaintBonusCircle(actor, profile, coord);
            }
        }

        private bool IsCandidateBonusCircleActor(IActor actor)
        {
            if (actor == null || actor.SnoActor == null)
                return false;

            if (actor.FloorCoordinate == null || !actor.FloorCoordinate.IsValid)
                return false;

            // Direct Triune dome actor path.
            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                if (profile == null || !profile.Enabled)
                    continue;

                if (ActorMatchesProfileSno(actor, profile))
                    return true;
            }

            // Generic proxy path:
            // Only accept exact profile power/attribute matches.
            // Do not accept every Generic_Proxy and recolor it by player buff;
            // that was the source of random duplicate blue/green/purple circles.
            if (actor.SnoActor.Sno == ActorSnoEnum._generic_proxy)
            {
                for (int i = 0; i < _profiles.Count; i++)
                {
                    var profile = _profiles[i];
                    if (profile == null || !profile.Enabled)
                        continue;

                    if (ActorHasProfilePowerAttribute(actor, profile))
                        return true;
                }

                return UseTriunePlayerBuffProxyFallback && IsLikelyTriuneProxy(actor);
            }

            return false;
        }

        

        private BonusCircleProfile GetMatchingProfile(IActor actor)
        {
            if (actor == null)
                return null;

            // Exact profile matches always win.
            // This includes direct Triune dome SNOs and exact Generic_Proxy visual attributes.
            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                if (profile == null || !profile.Enabled)
                    continue;

                if (ActorHasProfile(actor, profile))
                    return profile;
            }

            // Diagnostic fallback only. It must remain false by default.
            if (UseTriunePlayerBuffProxyFallback)
                return GetFallbackTriuneProfile(actor);

            return null;
        }

        

        private bool ActorMatchesProfileSno(IActor actor, BonusCircleProfile profile)
        {
            if (actor == null || actor.SnoActor == null || profile == null || profile.ActorSnos == null)
                return false;

            for (int i = 0; i < profile.ActorSnos.Length; i++)
            {
                if (actor.SnoActor.Sno == profile.ActorSnos[i])
                    return true;
            }

            return false;
        }

        private bool ActorHasProfile(IActor actor, BonusCircleProfile profile)
        {
            if (actor == null || profile == null)
                return false;

            // Direct actor SNO match first.
            // This is the important Triune fix.
            if (ActorMatchesProfileSno(actor, profile))
                return true;

            if (profile.PowerSnos == null || profile.Attributes == null)
                return false;

            for (int p = 0; p < profile.PowerSnos.Length; p++)
            {
                uint powerSno = profile.PowerSnos[p];
                if (powerSno == 0)
                    continue;

                for (int a = 0; a < profile.Attributes.Length; a++)
                {
                    var attr = profile.Attributes[a];
                    if (attr == null)
                        continue;

                    try
                    {
                        if (actor.GetAttributeValueAsInt(attr, powerSno, 0) == 1)
                            return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private bool ActorHasProfilePowerAttribute(IActor actor, BonusCircleProfile profile)
        {
            if (actor == null || profile == null)
                return false;

            if (profile.PowerSnos == null || profile.Attributes == null)
                return false;

            for (int p = 0; p < profile.PowerSnos.Length; p++)
            {
                uint powerSno = profile.PowerSnos[p];
                if (powerSno == 0)
                    continue;

                for (int a = 0; a < profile.Attributes.Length; a++)
                {
                    var attr = profile.Attributes[a];
                    if (attr == null)
                        continue;

                    try
                    {
                        if (actor.GetAttributeValueAsInt(attr, powerSno, 0) == 1)
                            return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private bool IsPlayerBuffActive(uint sno, int iconIndex)
        {
            try
            {
                return Hud.Game.Me != null
                    && Hud.Game.Me.Powers != null
                    && Hud.Game.Me.Powers.BuffIsActive(sno, iconIndex);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTriunePurplePlayerSignalActive()
        {
            // CONFIRMED: BuffIsActive(488071, 2) is true when inside the purple circle.
            return IsPlayerBuffActive(TriuneProxySno, TriunePurplePlayerIcon);
        }

        private bool IsTriuneBluePlayerSignalActive()
        {
            // CONFIRMED: BuffIsActive(488071, 8) is true when inside the blue circle.
            return IsPlayerBuffActive(TriuneProxySno, TriuneBluePlayerIcon);
        }

        private bool IsTriuneGreenPlayerSignalActive()
        {
            // CONFIRMED: BuffIsActive(488071, 5) is true when inside the green circle.
            return IsPlayerBuffActive(TriuneProxySno, TriuneGreenPlayerIcon);
        }

        private bool IsLegacyBlueTriunePlayerSignalActive()
        {
            // Previous working diagnostic clue: blue/white circle while this player-side signal is active.
            return IsPlayerBuffActive(488544u, 5);
        }

        private BonusCircleProfile GetFallbackTriuneProfile(IActor actor)
        {
            if (!IsLikelyTriuneProxy(actor))
                return null;

            // Player-buff fallback using official Triune's Will buff icon indices:
            //   icon 2 = Love      / Damage           = purple
            //   icon 8 = Creation  / Cooldown          = blue
            //   icon 5 = Determination / Resource Cost = green
            //
            // NOTE: When all three circles are active simultaneously, all three
            // flags are true. Green is checked first to prevent the old bug where
            // blue was stealing the green circle position.
            //
            // The 488544:icon5 legacy clue has been REMOVED. It was active during
            // all Triune circle windows and was incorrectly classifying the green
            // proxy as blue.

            bool purple = IsTriunePurplePlayerSignalActive();
            bool blue   = IsTriuneBluePlayerSignalActive();
            bool green  = IsTriuneGreenPlayerSignalActive();

            // When only one type is active, the assignment is unambiguous.
            if (green  && !blue && !purple) return FindProfileByCode("TriuneGreen");
            if (blue   && !green && !purple) return FindProfileByCode("TriuneBlue");
            if (purple && !blue && !green)   return FindProfileByCode("TriunePurple");

            // When multiple are active simultaneously (the common case — all three
            // circles spawn together), we still need to draw something. Green is
            // preferred here to ensure it is never stolen by blue. In practice the
            // exact B6/B7 attribute matching above will have already handled the
            // correctly-attributable proxies before the fallback runs.
            if (green)  return FindProfileByCode("TriuneGreen");
            if (blue)   return FindProfileByCode("TriuneBlue");
            if (purple) return FindProfileByCode("TriunePurple");

            return null;
        }

        private int GetActiveTriunePlayerSignalCount()
        {
            int count = 0;

            if (IsTriunePurplePlayerSignalActive()) count++;
            if (IsTriuneBluePlayerSignalActive()) count++;
            if (IsTriuneGreenPlayerSignalActive()) count++;

            return count;
        }

        private bool IsLikelyTriuneProxy(IActor actor)
        {
            if (actor == null || actor.SnoActor == null)
                return false;

            if (actor.SnoActor.Sno != ActorSnoEnum._generic_proxy)
                return false;

            var coord = GetBestCoordinate(actor);
            if (coord == null || !coord.IsValid)
                return false;

            try
            {
                // Use the same radius as the visual circle so proxies near the
                // outer edge of a circle are still caught. The old 6.0 hardcoded
                // value was a debug diagnostic limit, not a gameplay value.
                return actor.CentralXyDistanceToMe <= OtherBonusCircleRadius + PlayerInsideTolerance;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLikelyBlueTriuneProxy(IActor actor)
        {
            return ShowTriuneBlueCircle
                && IsLikelyTriuneProxy(actor)
                && (IsTriuneBluePlayerSignalActive() || IsLegacyBlueTriunePlayerSignalActive());
        }

        private BonusCircleProfile FindProfileByCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                if (profile != null && profile.Enabled && string.Equals(profile.Code, code, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }

            return null;
        }

        private IWorldCoordinate GetBestCoordinate(IActor actor)
        {
            if (actor == null)
                return null;

            if (actor.FloorCoordinate != null && actor.FloorCoordinate.IsValid)
                return actor.FloorCoordinate;

            if (actor.CollisionCoordinate != null && actor.CollisionCoordinate.IsValid)
                return actor.CollisionCoordinate;

            return null;
        }

        private bool ActorHasPowerBuff1None(IActor actor, uint powerSno)
        {
            if (actor == null || powerSno == 0)
                return false;

            try
            {
                return actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, powerSno, 0) == 1;
            }
            catch
            {
                return false;
            }
        }

        private void DrawRawDiagnosticRings(IActor actor)
        {
            if (actor == null || actor.SnoActor == null)
                return;

            if (actor.SnoActor.Sno != ActorSnoEnum._generic_proxy)
                return;

            var coord = GetBestCoordinate(actor);
            if (coord == null || !coord.IsValid)
                return;

            if (_rawProxyBrush != null)
                _rawProxyBrush.DrawWorldEllipse(OtherBonusCircleRadius, -1, coord);

            if (_rawPurpleProxyBrush != null && ActorHasPowerBuff1None(actor, 488071u))
                _rawPurpleProxyBrush.DrawWorldEllipse(OtherBonusCircleRadius * 1.02f, -1, coord);

            if (_rawBlueProxyBrush != null && IsLikelyBlueTriuneProxy(actor))
                _rawBlueProxyBrush.DrawWorldEllipse(OtherBonusCircleRadius * 0.98f, -1, coord);
        }

        private void PaintBonusCircle(IActor actor, BonusCircleProfile profile, IWorldCoordinate coord)
        {
            if (actor == null || profile == null || coord == null || !coord.IsValid)
                return;

            bool playerInside = IsPlayerInside(profile, coord);
            if (!ShowInactiveCircles && !playerInside)
                return;

            if (playerInside && AnimateWhenPlayerInside)
            {
                // Active state: spinning octagon only.
                if (ShowRotatingOctagon)
                    DrawRotatingWorldOctagonWithOutline(coord, profile);
                else if (profile.ActiveBrush != null)
                    profile.ActiveBrush.DrawWorldEllipse(profile.Radius, -1, coord);
            }
            else
            {
                // Inactive state: black halo drawn first (thicker), colored ring on top.
                if (_ringOutlineBrush != null)
                    _ringOutlineBrush.DrawWorldEllipse(profile.Radius, -1, coord);

                if (profile.BaseBrush != null)
                    profile.BaseBrush.DrawWorldEllipse(profile.Radius, -1, coord);
            }

            // Countdown dial + descriptor label always visible so the player can see
            // time remaining and circle type without needing to stand inside it.
            if (ShowCountdownText)
                DrawCountdownDial(actor, coord, profile);

            if (playerInside && ShowActiveLabel)
                DrawCircleLabel(coord, profile);
        }

        private bool IsPlayerInside(BonusCircleProfile profile, IWorldCoordinate circleCoord)
        {
            try
            {
                if (profile == null || circleCoord == null || !circleCoord.IsValid)
                    return false;

                if (Hud.Game.Me == null || Hud.Game.Me.FloorCoordinate == null || Hud.Game.Me.Powers == null)
                    return false;

                // Distance still gates this specific drawn circle so a global player buff
                // cannot activate the wrong nearby Oculus/Triune profile.
                if (Hud.Game.Me.FloorCoordinate.XYDistanceTo(circleCoord) > profile.Radius + PlayerInsideTolerance)
                    return false;

                if (string.Equals(profile.Code, "Oculus", StringComparison.OrdinalIgnoreCase))
                {
                    uint oculusSno = GetSno(Hud.Sno.SnoPowers.OculusRing, 402461);
                    return Hud.Game.Me.Powers.BuffIsActive(oculusSno);
                }

                if (string.Equals(profile.Code, "TriunePurple", StringComparison.OrdinalIgnoreCase))
                    return IsTriunePurplePlayerSignalActive();

                if (string.Equals(profile.Code, "TriuneBlue", StringComparison.OrdinalIgnoreCase))
                    return IsTriuneBluePlayerSignalActive();

                if (string.Equals(profile.Code, "TriuneGreen", StringComparison.OrdinalIgnoreCase))
                    return IsTriuneGreenPlayerSignalActive();

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void DrawActivePulse(IWorldCoordinate coord, BonusCircleProfile profile)
        {
            if (coord == null || profile == null)
                return;

            int now = Environment.TickCount;
            int cycle = Math.Max(250, AnimationCycleMs);
            float t = (now % cycle) / (float)cycle;
            float pulse = 0.92f + 0.16f * (float)Math.Sin(t * Math.PI * 2.0);
            float radius = profile.Radius * pulse;

            if (_activePulseOutlineBrush != null)
                _activePulseOutlineBrush.DrawWorldEllipse(radius, -1, coord);

            if (profile.PulseBrush != null)
                profile.PulseBrush.DrawWorldEllipse(radius, -1, coord);
        }

        private void DrawRotatingWorldOctagonWithOutline(IWorldCoordinate coord, BonusCircleProfile profile)
        {
            if (coord == null || profile == null)
                return;

            if (_activeOctagonOutlineBrush != null)
                DrawRotatingWorldOctagon(coord, profile, _activeOctagonOutlineBrush);

            if (profile.TickBrush != null)
                DrawRotatingWorldOctagon(coord, profile, profile.TickBrush);
        }

        private void DrawRotatingWorldOctagon(IWorldCoordinate coord, BonusCircleProfile profile, IBrush brush)
        {
            if (coord == null || profile == null || brush == null)
                return;

            int sides = Math.Max(3, OctagonSides);
            int cycle = Math.Max(1000, OctagonRotationCycleMs);

            float phase = ((Environment.TickCount % cycle) / (float)cycle) * (float)Math.PI * 2.0f;
            float radius = profile.Radius * Math.Max(0.1f, OctagonRadiusScale);

            float[] xs = new float[sides];
            float[] ys = new float[sides];
            bool[] valid = new bool[sides];

            for (int i = 0; i < sides; i++)
            {
                float angle = phase + ((float)Math.PI * 2.0f * i / sides);

                try
                {
                    var world = coord.Offset(
                        (float)Math.Cos(angle) * radius,
                        (float)Math.Sin(angle) * radius,
                        0.0f);

                    if (world == null)
                        continue;

                    var screen = world.ToScreenCoordinate(false, true);
                    if (screen == null)
                        continue;

                    xs[i] = screen.X;
                    ys[i] = screen.Y;
                    valid[i] = true;
                }
                catch
                {
                    valid[i] = false;
                }
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;

                if (!valid[i] || !valid[next])
                    continue;

                brush.DrawLine(xs[i], ys[i], xs[next], ys[next]);
            }
        }

        private void DrawCircleLabel(IWorldCoordinate coord, BonusCircleProfile profile)
        {
            if (coord == null || profile == null || profile.LabelFont == null)
                return;

            var sc = coord.ToScreenCoordinate(false, true);
            if (sc == null)
                return;

            string text = profile.LabelText ?? profile.Code;
            var layout = profile.LabelFont.GetTextLayout(text);
            profile.LabelFont.DrawText(
                layout,
                sc.X - layout.Metrics.Width * 0.5f,
                sc.Y - layout.Metrics.Height * 0.5f
                );
        }

        private void DrawCountdownDial(IActor actor, IWorldCoordinate coord, BonusCircleProfile profile)
        {
            if (actor == null || coord == null || profile == null)
                return;

            if (profile.LifetimeSeconds <= 0)
                return;

            double ageSeconds = (Hud.Game.CurrentGameTick - actor.CreatedAtInGameTick) / 60.0d;
            double remaining = profile.LifetimeSeconds - ageSeconds;

            if (remaining <= 0.0d || remaining > profile.LifetimeSeconds + 1.0d)
                return;

            double pct = remaining / profile.LifetimeSeconds;
            if (pct < 0.0d)
                pct = 0.0d;
            if (pct > 1.0d)
                pct = 1.0d;

            var sc = coord.ToScreenCoordinate(false, true);
            if (sc == null)
                return;

            if (ShowCountdownDial)
                DrawCountdownDialSegments(sc.X, sc.Y, CountdownDialRadius, pct, profile);

            if (_countdownFont != null)
            {
                string text = Math.Ceiling(remaining).ToString("0");
                var layout = _countdownFont.GetTextLayout(text);

                _countdownFont.DrawText(
                    layout,
                    sc.X - layout.Metrics.Width * 0.5f,
                    sc.Y - layout.Metrics.Height * 0.5f
                    );
            }

            // Descriptor label (DMG / CDR / RCR) drawn below the countdown, in the circle's own color.
            // Oculus has no DescriptorFont so nothing is drawn for it.
            if (profile.DescriptorFont != null
                && profile.LabelText != null
                && profile.LabelText.Length > 0)
            {
                var descLayout = profile.DescriptorFont.GetTextLayout(profile.LabelText);
                profile.DescriptorFont.DrawText(
                    descLayout,
                    sc.X - descLayout.Metrics.Width  * 0.5f,
                    sc.Y + descLayout.Metrics.Height * 1.4f
                    );
            }
        }

        private void DrawCountdownDialSegments(float cx, float cy, float radius, double pct, BonusCircleProfile profile)
        {
            int segments = Math.Max(8, CountdownDialSegments);

            // Background full small circle. This is a screen-space UI glyph, not a ground-radius circle.
            if (_countdownDialBackBrush != null)
                DrawScreenCircleSegments(cx, cy, radius, segments, 1.0d, _countdownDialBackBrush);

            // Remaining time arc in the circle's own color.
            IBrush progressBrush = (profile != null && profile.DialBrush != null)
                ? profile.DialBrush
                : _countdownDialProgressBrush;

            if (progressBrush != null)
                DrawScreenCircleSegments(cx, cy, radius, segments, pct, progressBrush);
        }

        private void DrawScreenCircleSegments(float cx, float cy, float radius, int segments, double pct, IBrush brush)
        {
            if (brush == null)
                return;

            if (pct <= 0.0d)
                return;

            if (pct > 1.0d)
                pct = 1.0d;

            int drawSegments = (int)Math.Ceiling(segments * pct);
            if (drawSegments <= 0)
                return;

            float start = -(float)Math.PI * 0.5f;
            float step = ((float)Math.PI * 2.0f) / segments;

            for (int i = 0; i < drawSegments; i++)
            {
                float a1 = start + step * i;
                float a2 = start + step * (i + 1);

                float x1 = cx + (float)Math.Cos(a1) * radius;
                float y1 = cy + (float)Math.Sin(a1) * radius;
                float x2 = cx + (float)Math.Cos(a2) * radius;
                float y2 = cy + (float)Math.Sin(a2) * radius;

                brush.DrawLine(x1, y1, x2, y2);
            }
        }

        private void DumpNearbyActors(IEnumerable<IActor> actors)
        {
            if (!DebugDumpNearbyActors || actors == null)
                return;

            int now = Environment.TickCount;
            if ((uint)(now - _lastNearbyDumpTick) < 500)
                return;

            _lastNearbyDumpTick = now;

            DebugPlayerCommunityBuffSignals();

            foreach (var actor in actors)
            {
                if (actor == null || actor.SnoActor == null)
                    continue;

                if (actor.SnoActor.Sno != ActorSnoEnum._generic_proxy)
                    continue;

                double distance;
                try
                {
                    distance = actor.CentralXyDistanceToMe;
                }
                catch
                {
                    continue;
                }

                if (distance > NearbyDumpRadius)
                    continue;

                string msg = "s7o_Oculus NEARBY sno="
                    + actor.SnoActor.Sno
                    + " code="
                    + actor.SnoActor.Code
                    + " name="
                    + actor.SnoActor.NameEnglish
                    + " dist="
                    + distance.ToString("0.0");

                msg += BuildActorTwoSlotDebugScan(actor);

                Hud.Debug(msg);
            }
        }

        private void DebugPlayerCommunityBuffSignals()
        {
            if (!DebugDumpNearbyActors)
                return;

            try
            {
                if (Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                    return;

                uint[] snos = new uint[]
                {
                    488071u, // confirmed purple P75/altar proxy signal — also check other icons
                    488544u, // appeared on player during blue circle session
                    TriuneWillSno,  // 483606
                    483485u,
                    483967u,
                    484633u,
                    484426u,
                    488004u,
                    488036u,
                    488037u,
                    488038u
                };

                string msg = "s7o_Oculus PLAYER_BUFF_SCAN";
                bool any = false;

                for (int i = 0; i < snos.Length; i++)
                {
                    uint sno = snos[i];

                    // Scan icon indices 0-12 to catch any unusual mappings.
                    for (int icon = 0; icon <= 12; icon++)
                    {
                        bool active = false;

                        try
                        {
                            active = Hud.Game.Me.Powers.BuffIsActive(sno, icon);
                        }
                        catch
                        {
                        }

                        if (active)
                        {
                            any = true;
                            msg += " [" + sno + ":B" + icon + "N=1]";
                        }
                    }
                }

                if (!any)
                    msg += " [none]";

                Hud.Debug(msg);
            }
            catch
            {
            }
        }

        private string BuildActorTwoSlotDebugScan(IActor actor)
        {
            if (actor == null)
                return string.Empty;

            uint[] snos = new uint[]
            {
                488071u, // confirmed purple P75/altar proxy signal
                488544u, // previous diagnostic candidate retained for testing
                TriuneWillSno,
                488004u,
                488036u,
                488037u,
                488038u
            };

            string msg = string.Empty;
            bool any = false;

            for (int i = 0; i < snos.Length; i++)
            {
                uint sno = snos[i];
                AppendActorBuffSlotHit(actor, sno, 1, ref msg, ref any);
                AppendActorBuffSlotHit(actor, sno, 2, ref msg, ref any);
                AppendActorBuffSlotHit(actor, sno, 6, ref msg, ref any);
                AppendActorBuffSlotHit(actor, sno, 7, ref msg, ref any);
            }

            return any ? msg : " attrs=none";
        }

        private void AppendActorBuffSlotHit(IActor actor, uint sno, int slot, ref string msg, ref bool any)
        {
            IAttribute attrNone = null;
            IAttribute attrA = null;
            IAttribute attrB = null;
            IAttribute attrC = null;
            IAttribute attrD = null;
            IAttribute attrE = null;

            if (slot == 1)
            {
                attrNone = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None;
                attrA = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_A;
                attrB = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_B;
                attrC = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_C;
                attrD = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_D;
                attrE = Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_E;
            }
            else if (slot == 2)
            {
                attrNone = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_None;
                attrA = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_A;
                attrB = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_B;
                attrC = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_C;
                attrD = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_D;
                attrE = Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_E;
            }
            else if (slot == 6)
            {
                attrNone = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_None;
                attrA = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_A;
                attrB = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_B;
                attrC = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_C;
                attrD = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_D;
                attrE = Hud.Sno.Attributes.Power_Buff_6_Visual_Effect_E;
            }
            else if (slot == 7)
            {
                attrNone = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_None;
                attrA = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_A;
                attrB = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_B;
                attrC = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_C;
                attrD = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_D;
                attrE = Hud.Sno.Attributes.Power_Buff_7_Visual_Effect_E;
            }
            else
            {
                return;
            }

            AppendActorAttrHit(actor, sno, slot, "N", attrNone, ref msg, ref any);
            AppendActorAttrHit(actor, sno, slot, "A", attrA, ref msg, ref any);
            AppendActorAttrHit(actor, sno, slot, "B", attrB, ref msg, ref any);
            AppendActorAttrHit(actor, sno, slot, "C", attrC, ref msg, ref any);
            AppendActorAttrHit(actor, sno, slot, "D", attrD, ref msg, ref any);
            AppendActorAttrHit(actor, sno, slot, "E", attrE, ref msg, ref any);
        }

        private void AppendActorAttrHit(IActor actor, uint sno, int slot, string suffix, IAttribute attr, ref string msg, ref bool any)
        {
            if (actor == null || attr == null || sno == 0)
                return;

            try
            {
                int value = actor.GetAttributeValueAsInt(attr, sno, 0);
                if (value != 0)
                {
                    any = true;
                    msg += " [" + sno + ":B" + slot + suffix + "=" + value + "]";
                }
            }
            catch
            {
            }
        }

        private void DebugUnknownBonusCircleActor(IActor actor)
        {
            if (!DebugLogUnknownBonusProxies)
                return;

            if (actor == null || actor.SnoActor == null)
                return;

            int now = Environment.TickCount;
            if ((uint)(now - _lastUnknownProxyDebugTick) < 3000)
                return;

            _lastUnknownProxyDebugTick = now;

            try
            {
                string msg = "s7o_Oculus unknown candidate actor: sno="
                    + actor.SnoActor.Sno
                    + ", code="
                    + actor.SnoActor.Code
                    + ", name="
                    + actor.SnoActor.NameEnglish;

                if (actor.SnoActor.Sno == ActorSnoEnum._generic_proxy)
                {
                    uint[] powerSnos = BuildTriunePowerSnos();

                    for (int i = 0; i < powerSnos.Length; i++)
                    {
                        uint sno = powerSnos[i];
                        if (sno == 0)
                            continue;

                        int none = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, sno, 0);
                        int a = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_A, sno, 0);
                        int b = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_B, sno, 0);
                        int c = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_C, sno, 0);
                        int d = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_D, sno, 0);
                        int e = actor.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_E, sno, 0);

                        msg += " | powerSno=" + sno
                            + " None=" + none
                            + " A=" + a
                            + " B=" + b
                            + " C=" + c
                            + " D=" + d
                            + " E=" + e;
                    }
                }

                Hud.Debug(msg);
            }
            catch
            {
            }
        }
    }
}
