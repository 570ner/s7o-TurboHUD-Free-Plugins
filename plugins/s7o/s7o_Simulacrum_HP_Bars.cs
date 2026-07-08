using System;
using System.Collections.Generic;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Lightweight standalone Simulacrum HP bars for TurboHUD Free / FreeHUD and LightningMOD.
    // Install as: plugins/s7o/s7o_Simulacrum_HP_Bars.cs
    //
    // Quick customization notes:
    // - ShowOwnSimulacrums / ShowOtherSimulacrums: set false to hide your Sims or party Sims.
    // - WidthScale / HeightScale: 1.0 = default bar size. Example: 0.8 = smaller width, 1.5 = taller bar.
    // - OwnTone / OtherTone: 0 is darker, 5 is normal, 10 is brighter. Defaults make your Sims brighter than others.
    // - Alpha: 255 is solid, lower values are more transparent. Example: 180 is softer.
    // - The HP color automatically ramps green -> yellow -> red as health drops.
    public class s7o_Simulacrum_HP_Bars : BasePlugin, IInGameTopPainter
    {
        public bool ShowOwnSimulacrums { get; set; } = true;
        public bool ShowOtherSimulacrums { get; set; } = true;

        public float OwnWidthScale { get; set; } = 0.80f;
        public float OwnHeightScale { get; set; } = 1.50f;
        public float OtherWidthScale { get; set; } = 0.80f;
        public float OtherHeightScale { get; set; } = 1.50f;

        public int OwnTone { get; set; } = 5;
        public int OtherTone { get; set; } = 0;
        public int OwnAlpha { get; set; } = 245;
        public int OtherAlpha { get; set; } = 205;

        public float BaseWidth { get; set; } = 135.0f;
        public float BaseHeight { get; set; } = 10.0f;
        public float BarYOffset { get; set; } = 15.0f;
        public float OutlineSize { get; set; } = 2.0f;
        public bool UseTwoToneLighting { get; set; } = true;

        private readonly HashSet<ActorSnoEnum> _simulacrumSnos = new HashSet<ActorSnoEnum>
        {
            ActorSnoEnum._p6_necro_simulacrum_male,
            ActorSnoEnum._p6_necro_simulacrum_female,
            ActorSnoEnum._p6_necro_simulacrum_norune,
            ActorSnoEnum._p6_necro_simulacrum_a,
            ActorSnoEnum._p6_necro_simulacrum_a_set,
        };

        private readonly Dictionary<uint, float> _maxHitpointsByActor = new Dictionary<uint, float>();
        private readonly Dictionary<uint, int> _lastSeenTickByActor = new Dictionary<uint, int>();
        private readonly Dictionary<int, IBrush> _brushCache = new Dictionary<int, IBrush>();

        private IBrush _ownOutlineBrush;
        private IBrush _ownBackgroundBrush;
        private IBrush _otherOutlineBrush;
        private IBrush _otherBackgroundBrush;
        private int _lastPruneTick;

        public s7o_Simulacrum_HP_Bars()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            Order = 30205;

            _ownOutlineBrush = Hud.Render.CreateBrush(ClampAlpha(OwnAlpha), 0, 0, 0, 0);
            _ownBackgroundBrush = Hud.Render.CreateBrush(ClampAlpha((int)(OwnAlpha * 0.72f)), 0, 0, 0, 0);
            _otherOutlineBrush = Hud.Render.CreateBrush(ClampAlpha(OtherAlpha), 0, 0, 0, 0);
            _otherBackgroundBrush = Hud.Render.CreateBrush(ClampAlpha((int)(OtherAlpha * 0.72f)), 0, 0, 0, 0);
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null)
                return;

            int tick = Hud.Game.CurrentGameTick;
            if (tick - _lastPruneTick > 600)
                PruneStaleActors(tick);

            foreach (var actor in Hud.Game.Actors)
            {
                if (!IsValidSimulacrum(actor))
                    continue;

                bool mine = actor.SummonerAcdDynamicId == Hud.Game.Me.SummonerId;
                if (mine)
                {
                    if (!ShowOwnSimulacrums) continue;
                }
                else
                {
                    if (!ShowOtherSimulacrums) continue;
                }

                float hpRatio;
                if (!TryGetHealthRatio(actor, tick, out hpRatio))
                    continue;

                DrawSimulacrumBar(actor, hpRatio, mine);
            }
        }

        private bool IsValidSimulacrum(IActor actor)
        {
            if (actor == null || actor.SnoActor == null)
                return false;

            if (!_simulacrumSnos.Contains(actor.SnoActor.Sno))
                return false;

            if (!actor.IsOnScreen || actor.IsDisabled || actor.Hitpoints <= 0.0f)
                return false;

            return true;
        }

        private bool TryGetHealthRatio(IActor actor, int tick, out float ratio)
        {
            ratio = 1.0f;

            float current = actor.Hitpoints;
            if (current <= 0.0f)
            {
                ratio = 0.0f;
                return false;
            }

            uint key = actor.AcdId != 0 ? actor.AcdId : actor.AnnId;
            if (key == 0)
                return false;

            float max;
            if (!_maxHitpointsByActor.TryGetValue(key, out max) || current > max)
            {
                max = current;
                _maxHitpointsByActor[key] = max;
            }

            _lastSeenTickByActor[key] = tick;

            if (max <= 0.0f)
                return false;

            ratio = current / max;
            if (ratio < 0.0f) ratio = 0.0f;
            if (ratio > 1.0f) ratio = 1.0f;
            return true;
        }

        private void DrawSimulacrumBar(IActor actor, float hpRatio, bool mine)
        {
            var sc = actor.FloorCoordinate != null ? actor.FloorCoordinate.ToScreenCoordinate() : actor.ScreenCoordinate;
            if (sc == null)
                return;

            float w = BaseWidth * ClampScale(mine ? OwnWidthScale : OtherWidthScale, 0.25f, 3.0f);
            float h = BaseHeight * ClampScale(mine ? OwnHeightScale : OtherHeightScale, 0.25f, 3.0f);
            float outline = OutlineSize < 0.0f ? 0.0f : OutlineSize;
            if (outline * 2.0f >= w || outline * 2.0f >= h)
                outline = 1.0f;

            float x = sc.X - w * 0.5f;
            float y = sc.Y + BarYOffset;
            float fillX = x + outline;
            float fillY = y + outline;
            float fillW = w - outline * 2.0f;
            float fillH = h - outline * 2.0f;
            if (fillW <= 0.0f || fillH <= 0.0f)
                return;

            IBrush outlineBrush = mine ? _ownOutlineBrush : _otherOutlineBrush;
            IBrush backgroundBrush = mine ? _ownBackgroundBrush : _otherBackgroundBrush;
            if (outlineBrush != null)
                outlineBrush.DrawRectangleGridFit(x, y, w, h);
            if (backgroundBrush != null)
                backgroundBrush.DrawRectangleGridFit(fillX, fillY, fillW, fillH);

            float hpW = fillW * hpRatio;
            if (hpW <= 0.0f)
                return;

            int tone = mine ? OwnTone : OtherTone;
            int alpha = mine ? OwnAlpha : OtherAlpha;
            int r, g, b;
            GetHealthRampColor(hpRatio, out r, out g, out b);
            ApplyTone(ref r, ref g, ref b, tone);

            GetFillBrush(alpha, r, g, b).DrawRectangleGridFit(fillX, fillY, hpW, fillH);

            if (!UseTwoToneLighting || fillH < 5.0f)
                return;

            int lr = r, lg = g, lb = b;
            Lighten(ref lr, ref lg, ref lb, 0.28f);
            GetFillBrush(alpha, lr, lg, lb).DrawRectangleGridFit(fillX, fillY, hpW, fillH * 0.45f);

            int sr = r, sg = g, sb = b;
            Darken(ref sr, ref sg, ref sb, 0.35f);
            GetFillBrush(alpha, sr, sg, sb).DrawRectangleGridFit(fillX, fillY + fillH * 0.72f, hpW, fillH * 0.28f);
        }

        private void GetHealthRampColor(float hp, out int r, out int g, out int b)
        {
            if (hp >= 0.50f)
            {
                float t = (1.0f - hp) / 0.50f;
                LerpColor(55, 235, 65, 245, 220, 35, t, out r, out g, out b);
                return;
            }

            if (hp >= 0.30f)
            {
                float t = (0.50f - hp) / 0.20f;
                LerpColor(245, 220, 35, 235, 45, 45, t, out r, out g, out b);
                return;
            }

            r = 235;
            g = 45;
            b = 45;
        }

        private void LerpColor(int ar, int ag, int ab, int br, int bg, int bb, float t, out int r, out int g, out int b)
        {
            if (t < 0.0f) t = 0.0f;
            if (t > 1.0f) t = 1.0f;
            r = ClampColor((int)(ar + (br - ar) * t));
            g = ClampColor((int)(ag + (bg - ag) * t));
            b = ClampColor((int)(ab + (bb - ab) * t));
        }

        private void ApplyTone(ref int r, ref int g, ref int b, int tone)
        {
            if (tone < 0) tone = 0;
            if (tone > 10) tone = 10;

            if (tone < 5)
            {
                float factor = 0.55f + tone * 0.09f;
                r = ClampColor((int)(r * factor));
                g = ClampColor((int)(g * factor));
                b = ClampColor((int)(b * factor));
                return;
            }

            if (tone > 5)
            {
                float t = (tone - 5) / 5.0f * 0.35f;
                Lighten(ref r, ref g, ref b, t);
            }
        }

        private void Lighten(ref int r, ref int g, ref int b, float t)
        {
            r = ClampColor((int)(r + (255 - r) * t));
            g = ClampColor((int)(g + (255 - g) * t));
            b = ClampColor((int)(b + (255 - b) * t));
        }

        private void Darken(ref int r, ref int g, ref int b, float t)
        {
            r = ClampColor((int)(r * (1.0f - t)));
            g = ClampColor((int)(g * (1.0f - t)));
            b = ClampColor((int)(b * (1.0f - t)));
        }

        private IBrush GetFillBrush(int alpha, int r, int g, int b)
        {
            alpha = ClampAlpha(alpha);
            r = ClampColor(r);
            g = ClampColor(g);
            b = ClampColor(b);

            int key = (alpha << 24) | (r << 16) | (g << 8) | b;
            IBrush brush;
            if (!_brushCache.TryGetValue(key, out brush))
            {
                brush = Hud.Render.CreateBrush(alpha, r, g, b, 0);
                _brushCache[key] = brush;
            }
            return brush;
        }

        private void PruneStaleActors(int tick)
        {
            _lastPruneTick = tick;
            var remove = new List<uint>();
            foreach (var pair in _lastSeenTickByActor)
            {
                if (tick - pair.Value > 3600)
                    remove.Add(pair.Key);
            }

            foreach (uint key in remove)
            {
                _lastSeenTickByActor.Remove(key);
                _maxHitpointsByActor.Remove(key);
            }
        }

        private int ClampAlpha(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private int ClampColor(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private float ClampScale(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
