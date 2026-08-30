using System;
using System.Collections.Generic;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Lightweight Simulacrum health bars and movable party list for FreeHUD and LightningMOD.
    // Simulacrum alert/list concept credited to RNN's SimulacrumsAlertIcon community plugin.
    // Actor-following bars use every live Sim projected into the current viewport. The fixed list can
    // apply a separate range/completeness gate so it never reports a partial two-Sim set as complete.
    public class s7o_Simulacrum_HP_Bars : BasePlugin, IInGameTopPainter, IKeyEventHandler
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
        public float ActorBarEdgeFollowRangePx { get; set; } = 300.0f;

        public bool ShowScreenList { get; set; } = true;
        public float ScreenListWidth { get; set; } = 130.0f;
        public float ScreenListBarHeight { get; set; } = 14.0f;
        public Key ScreenListDragHotkey { get; set; } = Key.F2;
        public Key ScreenListToggleHotkey { get; set; } = Key.F3;
        // Starts beside the default s7o Elite HP list at 1920x1080.
        public float ScreenListXFraction { get; set; } = 0.22f;
        public float ScreenListYFraction { get; set; } = 0.018f;
        public float ScreenListLabelGap { get; set; } = 3.0f;
        public float ScreenListInnerBarGap { get; set; } = 2.0f;
        public float ScreenListGroupGap { get; set; } = 5.0f;
        public int ScreenListMaxRows { get; set; } = 8;
        public bool ShowScreenListEntityCount { get; set; } = true;
        // Fixed list only. Actor-following bars remain unrestricted by distance.
        public bool LimitOtherScreenListByRange { get; set; } = true;
        public float OtherScreenListRange { get; set; } = 70.0f;
        public bool HideIncompleteOtherScreenListGroups { get; set; } = true;
        public bool ShowSurvivorAfterConfirmedDeath { get; set; } = true;

        private const uint AnyAttributeModifier = 0xFFFFFu;

        private sealed class ScreenListEntry
        {
            public IActor Actor;
            public uint ActorId;
            public uint OwnerId;
            public string OwnerName;
            public float HealthRatio;
            public double Distance;
            public bool Mine;
        }

        private sealed class ScreenListGroup
        {
            public uint OwnerId;
            public string OwnerName;
            public bool Mine;
            public readonly List<ScreenListEntry> Entries = new List<ScreenListEntry>();
        }

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
        private readonly HashSet<uint> _ownersWithConfirmedSimDeath = new HashSet<uint>();

        private IBrush _ownOutlineBrush;
        private IBrush _ownBackgroundBrush;
        private IBrush _otherOutlineBrush;
        private IBrush _otherBackgroundBrush;
        private IFont _screenListTextFont;
        private bool _screenListDragging;
        private bool _screenListBoundsValid;
        private float _screenListDragOffsetX;
        private float _screenListDragOffsetY;
        private float _screenListBoundsX;
        private float _screenListBoundsY;
        private float _screenListBoundsWidth;
        private float _screenListBoundsHeight;
        private int _lastPruneTick;
        private int _lastGameTick = -1;

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
            _screenListTextFont = Hud.Render.CreateFont("tahoma", 8.0f, 245, 255, 255, 255, true, false, 220, 0, 0, 0, true);
        }

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            if (keyEvent == null)
                return;

            if (keyEvent.Key == ScreenListDragHotkey)
            {
                if (keyEvent.IsPressed)
                    BeginScreenListDrag();
                else
                    _screenListDragging = false;
                return;
            }

            if (keyEvent.Key == ScreenListToggleHotkey &&
                keyEvent.IsPressed &&
                (IsCursorOverOwnPortrait() || IsCursorOverScreenList()))
            {
                ToggleScreenList();
            }
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null)
                return;

            int tick = Hud.Game.CurrentGameTick;
            if (_lastGameTick >= 0 && tick < _lastGameTick)
                ResetRuntimeState(tick);
            _lastGameTick = tick;

            if (tick - _lastPruneTick > 600)
                PruneStaleActors(tick);

            var liveEntries = new List<ScreenListEntry>();
            foreach (var actor in Hud.Game.Actors)
            {
                if (!IsSimulacrumSno(actor))
                    continue;

                ObserveConfirmedSimulacrumDeath(actor);
                if (!IsSimulacrumActor(actor))
                    continue;

                bool mine = actor.SummonerAcdDynamicId == Hud.Game.Me.SummonerId;
                if ((mine && !ShowOwnSimulacrums) || (!mine && !ShowOtherSimulacrums))
                    continue;

                float hpRatio;
                if (!TryGetHealthRatio(actor, tick, out hpRatio) || hpRatio <= 0.0f)
                    continue;

                liveEntries.Add(new ScreenListEntry
                {
                    Actor = actor,
                    ActorId = GetActorKey(actor),
                    OwnerId = actor.SummonerAcdDynamicId,
                    OwnerName = GetSimulacrumOwnerName(actor),
                    HealthRatio = hpRatio,
                    Distance = actor.NormalizedXyDistanceToMe,
                    Mine = mine,
                });
            }

            RefreshConfirmedDeathOwners(liveEntries);

            // Actor-following bars reflect every currently collected live Sim.
            // Screen-list completeness/range rules must never suppress a visible actor bar.
            foreach (ScreenListEntry entry in liveEntries)
            {
                if (entry.Actor != null)
                    DrawSimulacrumBar(entry.Actor, entry.HealthRatio, entry.Mine);
            }

            if (ShowScreenList)
                DrawScreenList(FilterIncompleteOtherScreenListGroups(liveEntries));
            else
            {
                _screenListDragging = false;
                _screenListBoundsValid = false;
            }
        }

        private bool IsSimulacrumSno(IActor actor)
        {
            return actor != null && actor.SnoActor != null && _simulacrumSnos.Contains(actor.SnoActor.Sno);
        }

        private bool IsSimulacrumActor(IActor actor)
        {
            return IsSimulacrumSno(actor) && !actor.IsDisabled;
        }

        private void ObserveConfirmedSimulacrumDeath(IActor actor)
        {
            if (actor == null || actor.SummonerAcdDynamicId == 0 ||
                actor.SummonerAcdDynamicId == Hud.Game.Me.SummonerId)
            {
                return;
            }

            float ratio;
            if (TryGetNativeHealthRatio(actor, out ratio) && ratio <= 0.0f)
                _ownersWithConfirmedSimDeath.Add(actor.SummonerAcdDynamicId);
        }

        private void RefreshConfirmedDeathOwners(List<ScreenListEntry> entries)
        {
            if (_ownersWithConfirmedSimDeath.Count == 0)
                return;

            var remove = new List<uint>();
            foreach (uint ownerId in _ownersWithConfirmedSimDeath)
            {
                int liveCount = 0;
                foreach (ScreenListEntry entry in entries)
                {
                    if (!entry.Mine && entry.OwnerId == ownerId)
                        liveCount++;
                }

                // No live Sim means the old cast ended. Two live Sims means the set is complete again.
                if (liveCount == 0 || liveCount >= 2)
                    remove.Add(ownerId);
            }

            foreach (uint ownerId in remove)
                _ownersWithConfirmedSimDeath.Remove(ownerId);
        }

        private List<ScreenListEntry> FilterIncompleteOtherScreenListGroups(List<ScreenListEntry> entries)
        {
            var result = new List<ScreenListEntry>(entries.Count);
            var handledOwners = new List<ScreenListEntry>();

            foreach (ScreenListEntry entry in entries)
            {
                if (entry.Mine)
                {
                    result.Add(entry);
                    continue;
                }

                bool handled = false;
                foreach (ScreenListEntry owner in handledOwners)
                {
                    if (IsSameScreenListOwner(owner, entry))
                    {
                        handled = true;
                        break;
                    }
                }
                if (handled)
                    continue;

                handledOwners.Add(entry);
                int count = 0;
                bool inRange = true;
                foreach (ScreenListEntry candidate in entries)
                {
                    if (!IsSameScreenListOwner(entry, candidate))
                        continue;

                    count++;
                    if (LimitOtherScreenListByRange && !IsValidOtherScreenListDistance(candidate.Distance))
                        inRange = false;
                }

                int expectedCount = GetExpectedOtherSimulacrumCount(entry);
                bool confirmedSurvivor = ShowSurvivorAfterConfirmedDeath &&
                    expectedCount == 2 &&
                    count == 1 &&
                    entry.OwnerId != 0 &&
                    _ownersWithConfirmedSimDeath.Contains(entry.OwnerId);

                if (!inRange || (HideIncompleteOtherScreenListGroups && count < expectedCount && !confirmedSurvivor))
                    continue;

                foreach (ScreenListEntry candidate in entries)
                {
                    if (IsSameScreenListOwner(entry, candidate))
                        result.Add(candidate);
                }
            }

            return result;
        }

        private bool IsValidOtherScreenListDistance(double distance)
        {
            return !double.IsNaN(distance) &&
                !double.IsInfinity(distance) &&
                distance <= Math.Max(0.0f, OtherScreenListRange);
        }

        private int GetExpectedOtherSimulacrumCount(ScreenListEntry entry)
        {
            if (entry.Actor != null && entry.Actor.SnoActor != null)
            {
                ActorSnoEnum sno = entry.Actor.SnoActor.Sno;
                if (sno == ActorSnoEnum._p6_necro_simulacrum_a ||
                    sno == ActorSnoEnum._p6_necro_simulacrum_a_set)
                {
                    return 2;
                }
            }

            IPlayer owner = GetPlayerBySummonerId(entry.OwnerId);
            if (owner == null || owner.Powers == null)
                return 1;

            try
            {
                IPlayerSkill skill = GetSimulacrumSkill(owner);
                bool bloodAndBone = skill != null &&
                    (string.Equals(skill.RuneNameEnglish, "Blood and Bone", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrWhiteSpace(skill.RuneNameEnglish) && skill.Rune == 3));
                return bloodAndBone || owner.Powers.BuffIsActive(484301) ? 2 : 1;
            }
            catch
            {
                return 1;
            }
        }

        private IPlayerSkill GetSimulacrumSkill(IPlayer player)
        {
            if (player == null || player.Powers == null)
                return null;

            try
            {
                if (player.Powers.UsedNecromancerPowers != null &&
                    player.Powers.UsedNecromancerPowers.Simulacrum != null)
                {
                    return player.Powers.UsedNecromancerPowers.Simulacrum;
                }

                return player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Necromancer_Simulacrum);
            }
            catch
            {
                return null;
            }
        }

        private IPlayer GetPlayerBySummonerId(uint summonerId)
        {
            if (summonerId == 0 || Hud.Game.Players == null)
                return null;

            foreach (IPlayer player in Hud.Game.Players)
            {
                if (player != null && player.SummonerId == summonerId)
                    return player;
            }

            return null;
        }

        private bool IsSameScreenListOwner(ScreenListEntry a, ScreenListEntry b)
        {
            if (a == null || b == null || a.Mine != b.Mine)
                return false;

            if (a.OwnerId != 0 || b.OwnerId != 0)
                return a.OwnerId == b.OwnerId;

            return string.Equals(a.OwnerName, b.OwnerName, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetHealthRatio(IActor actor, int tick, out float ratio)
        {
            ratio = 1.0f;

            uint key = GetActorKey(actor);
            if (key == 0)
                return false;

            _lastSeenTickByActor[key] = tick;

            if (TryGetNativeHealthRatio(actor, out ratio))
                return true;

            float current = actor.Hitpoints;
            if (current <= 0.0f)
            {
                ratio = 0.0f;
                return false;
            }

            float max;
            if (!_maxHitpointsByActor.TryGetValue(key, out max) || current > max)
            {
                max = current;
                _maxHitpointsByActor[key] = max;
            }

            if (max <= 0.0f)
                return false;

            ratio = ClampRatio(current / max);
            return true;
        }

        private uint GetActorKey(IActor actor)
        {
            if (actor == null)
                return 0;

            return actor.AcdId != 0 ? actor.AcdId : actor.AnnId;
        }

        private bool TryGetNativeHealthRatio(IActor actor, out float ratio)
        {
            ratio = 1.0f;

            double current = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Cur, AnyAttributeModifier);
            double max = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Max_Total, AnyAttributeModifier);
            if (TryMakeRatio(current, max, out ratio))
                return true;

            current = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Cur, 0);
            max = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Max_Total, 0);
            if (TryMakeRatio(current, max, out ratio))
                return true;

            current = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Cur, AnyAttributeModifier);
            max = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Max, AnyAttributeModifier);
            if (TryMakeRatio(current, max, out ratio))
                return true;

            current = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Cur, 0);
            max = ReadAttribute(actor, Hud.Sno.Attributes.Hitpoints_Max, 0);
            return TryMakeRatio(current, max, out ratio);
        }

        private double ReadAttribute(IActor actor, IAttribute attribute, uint modifier)
        {
            try
            {
                if (actor == null || attribute == null)
                    return double.NaN;

                return actor.GetAttributeValue(attribute, modifier, double.NaN);
            }
            catch
            {
                return double.NaN;
            }
        }

        private bool TryMakeRatio(double current, double max, out float ratio)
        {
            ratio = 1.0f;

            if (double.IsNaN(current) || double.IsNaN(max) || double.IsInfinity(current) || double.IsInfinity(max))
                return false;

            if (current < 0.0 || max <= 0.0)
                return false;

            ratio = ClampRatio((float)(current / max));
            return true;
        }

        private float ClampRatio(float ratio)
        {
            if (ratio < 0.0f) return 0.0f;
            if (ratio > 1.0f) return 1.0f;
            return ratio;
        }

        private void DrawSimulacrumBar(IActor actor, float hpRatio, bool mine)
        {
            IScreenCoordinate sc;
            try
            {
                if (actor == null || actor.FloorCoordinate == null || !actor.FloorCoordinate.IsValid)
                    return;

                sc = actor.FloorCoordinate.ToScreenCoordinate(true, true);
            }
            catch
            {
                return;
            }

            if (sc == null)
                return;

            float w = BaseWidth * ClampScale(mine ? OwnWidthScale : OtherWidthScale, 0.25f, 3.0f);
            float h = BaseHeight * ClampScale(mine ? OwnHeightScale : OtherHeightScale, 0.25f, 3.0f);
            float x = sc.X - w * 0.5f;
            float y = sc.Y + BarYOffset;

            if (Hud == null || Hud.Window == null ||
                float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(y) || float.IsInfinity(y))
                return;

            var size = Hud.Window.Size;
            float range = ActorBarEdgeFollowRangePx;
            if (float.IsNaN(range) || float.IsInfinity(range))
                range = 0.0f;
            else
                range = Math.Max(0.0f, range);

            if (size.Width <= 0 || size.Height <= 0 ||
                sc.X < -range || sc.X > size.Width + range ||
                sc.Y < -range || sc.Y > size.Height + range)
                return;

            const float inset = 4.0f;
            if (size.Width > w + inset * 2.0f)
                x = Math.Max(inset, Math.Min(size.Width - w - inset, x));
            if (size.Height > h + inset * 2.0f)
                y = Math.Max(inset, Math.Min(size.Height - h - inset, y));

            DrawHealthBar(x, y, w, h, hpRatio, mine);
        }

        private void DrawHealthBar(float x, float y, float w, float h, float hpRatio, bool mine)
        {
            float outline = OutlineSize < 0.0f ? 0.0f : OutlineSize;
            if (outline * 2.0f >= w || outline * 2.0f >= h)
                outline = 1.0f;

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

        private void DrawScreenList(List<ScreenListEntry> entries)
        {
            if (entries == null || entries.Count == 0 || Hud.Window == null)
            {
                _screenListDragging = false;
                _screenListBoundsValid = false;
                return;
            }

            entries.Sort(delegate(ScreenListEntry a, ScreenListEntry b)
            {
                int mine = b.Mine.CompareTo(a.Mine);
                if (mine != 0) return mine;

                int owner = string.Compare(a.OwnerName, b.OwnerName, StringComparison.OrdinalIgnoreCase);
                if (owner != 0) return owner;

                int ownerId = a.OwnerId.CompareTo(b.OwnerId);
                if (ownerId != 0) return ownerId;

                return a.ActorId.CompareTo(b.ActorId);
            });

            UpdateScreenListDrag();

            float windowW = Hud.Window.Size.Width;
            float windowH = Hud.Window.Size.Height;
            float w = ClampScale(ScreenListWidth, 60.0f, Math.Max(60.0f, windowW));
            float barH = ClampScale(ScreenListBarHeight, 5.0f, 60.0f);
            float x = ClampRatio(ScreenListXFraction) * windowW;
            float y = ClampRatio(ScreenListYFraction) * windowH;
            float labelGap = Math.Max(0.0f, ScreenListLabelGap);
            float innerBarGap = Math.Max(0.0f, ScreenListInnerBarGap);
            float groupGap = Math.Max(0.0f, ScreenListGroupGap);
            int maxRows = Math.Max(1, Math.Min(16, ScreenListMaxRows));
            List<ScreenListGroup> groups = BuildScreenListGroups(entries, maxRows);
            if (groups.Count == 0)
            {
                _screenListDragging = false;
                _screenListBoundsValid = false;
                return;
            }

            if (x + w > windowW)
                x = Math.Max(0.0f, windowW - w);

            float labelHeight = 0.0f;
            if (_screenListTextFont != null)
                labelHeight = _screenListTextFont.GetTextLayout("SIM").Metrics.Height + labelGap;

            int barCount = 0;
            int innerGapCount = 0;
            foreach (ScreenListGroup group in groups)
            {
                barCount += group.Entries.Count;
                innerGapCount += Math.Max(0, group.Entries.Count - 1);
            }

            float estimatedHeight =
                groups.Count * labelHeight +
                barCount * barH +
                innerGapCount * innerBarGap +
                Math.Max(0, groups.Count - 1) * groupGap;
            if (y + estimatedHeight > windowH)
                y = Math.Max(0.0f, windowH - estimatedHeight);

            float startY = y;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                ScreenListGroup group = groups[groupIndex];
                string label = TruncateLabel(group.OwnerName, 20);

                if (ShowScreenListEntityCount)
                {
                    string suffix = " (" + group.Entries.Count + ")";
                    label = TruncateLabel(group.OwnerName, Math.Max(1, 20 - suffix.Length)) + suffix;
                }

                if (_screenListTextFont != null && !string.IsNullOrEmpty(label))
                {
                    var layout = _screenListTextFont.GetTextLayout(label);
                    _screenListTextFont.DrawText(layout, x, y);
                    y += layout.Metrics.Height + labelGap;
                }

                for (int entryIndex = 0; entryIndex < group.Entries.Count; entryIndex++)
                {
                    ScreenListEntry entry = group.Entries[entryIndex];
                    DrawHealthBar(x, y, w, barH, entry.HealthRatio, entry.Mine);
                    y += barH;
                    if (entryIndex + 1 < group.Entries.Count)
                        y += innerBarGap;
                }

                if (groupIndex + 1 < groups.Count)
                    y += groupGap;
            }

            _screenListBoundsX = x;
            _screenListBoundsY = startY;
            _screenListBoundsWidth = w;
            _screenListBoundsHeight = Math.Max(barH, y - startY);
            _screenListBoundsValid = true;

            ScreenListXFraction = windowW > 0.0f ? x / windowW : ScreenListXFraction;
            ScreenListYFraction = windowH > 0.0f ? startY / windowH : ScreenListYFraction;
        }

        private List<ScreenListGroup> BuildScreenListGroups(List<ScreenListEntry> entries, int maxRows)
        {
            var groups = new List<ScreenListGroup>();
            int rows = 0;

            foreach (ScreenListEntry entry in entries)
            {
                if (rows >= maxRows)
                    break;

                ScreenListGroup group =
                    groups.Count > 0 && IsSameScreenListOwner(groups[groups.Count - 1], entry)
                        ? groups[groups.Count - 1]
                        : null;

                if (group == null)
                {
                    group = new ScreenListGroup
                    {
                        OwnerId = entry.OwnerId,
                        OwnerName = entry.OwnerName,
                        Mine = entry.Mine,
                    };
                    groups.Add(group);
                }

                group.Entries.Add(entry);
                rows++;
            }

            return groups;
        }

        private bool IsSameScreenListOwner(ScreenListGroup group, ScreenListEntry entry)
        {
            if (group == null || entry == null || group.Mine != entry.Mine)
                return false;

            if (group.OwnerId != 0 || entry.OwnerId != 0)
                return group.OwnerId == entry.OwnerId;

            return string.Equals(group.OwnerName, entry.OwnerName, StringComparison.OrdinalIgnoreCase);
        }

        private string GetSimulacrumOwnerName(IActor actor)
        {
            if (actor == null || Hud.Game == null)
                return "SIM";

            uint ownerId = actor.SummonerAcdDynamicId;
            var players = Hud.Game.Players;
            if (players == null)
                return "SIM";

            foreach (IPlayer player in players)
            {
                if (player == null || player.SummonerId != ownerId)
                    continue;

                string name = player.IsMe ? Hud.MyBattleTag : player.BattleTagAbovePortrait;
                if (string.IsNullOrWhiteSpace(name))
                    name = player.BattleTagAbovePortrait;

                name = ExtractAccountName(name);

                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();

                if (!string.IsNullOrWhiteSpace(player.HeroName))
                    return player.HeroName.Trim();
            }

            return "SIM";
        }

        private string ExtractAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string result = name.Trim();

            int clanEnd = result.LastIndexOf('>');
            if (clanEnd >= 0 && clanEnd + 1 < result.Length)
                result = result.Substring(clanEnd + 1).Trim();

            int hashIndex = result.IndexOf('#');
            if (hashIndex > 0)
                result = result.Substring(0, hashIndex).Trim();

            return result;
        }

        private string TruncateLabel(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "SIM";

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, Math.Max(1, maxLength - 1)) + "…";
        }

        private void BeginScreenListDrag()
        {
            if (!ShowScreenList || !_screenListBoundsValid || Hud == null || Hud.Window == null)
                return;

            if (!Hud.Window.CursorInsideRect(
                _screenListBoundsX,
                _screenListBoundsY,
                _screenListBoundsWidth,
                _screenListBoundsHeight))
            {
                return;
            }

            _screenListDragging = true;
            _screenListDragOffsetX = Hud.Window.CursorX - _screenListBoundsX;
            _screenListDragOffsetY = Hud.Window.CursorY - _screenListBoundsY;
        }

        private void UpdateScreenListDrag()
        {
            if (!_screenListDragging || Hud == null || Hud.Window == null)
                return;

            float windowW = Hud.Window.Size.Width;
            float windowH = Hud.Window.Size.Height;
            float w = ClampScale(ScreenListWidth, 60.0f, Math.Max(60.0f, windowW));
            float h = Math.Max(ScreenListBarHeight, _screenListBoundsHeight);
            float x = Hud.Window.CursorX - _screenListDragOffsetX;
            float y = Hud.Window.CursorY - _screenListDragOffsetY;

            x = Math.Max(0.0f, Math.Min(Math.Max(0.0f, windowW - w), x));
            y = Math.Max(0.0f, Math.Min(Math.Max(0.0f, windowH - h), y));

            if (windowW > 0.0f)
                ScreenListXFraction = x / windowW;
            if (windowH > 0.0f)
                ScreenListYFraction = y / windowH;
        }

        private bool IsCursorOverOwnPortrait()
        {
            if (Hud == null || Hud.Window == null || Hud.Game == null || Hud.Game.Me == null)
                return false;

            IUiElement portrait = Hud.Game.Me.PortraitUiElement;
            if (portrait == null)
                return false;

            if (!portrait.Visible && portrait.ReplacementWhenNotVisible != null)
                portrait = portrait.ReplacementWhenNotVisible;

            var rect = portrait.Rectangle;
            return rect.Width > 0.0f &&
                rect.Height > 0.0f &&
                Hud.Window.CursorInsideRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private bool IsCursorOverScreenList()
        {
            return ShowScreenList &&
                _screenListBoundsValid &&
                Hud != null &&
                Hud.Window != null &&
                Hud.Window.CursorInsideRect(
                    _screenListBoundsX,
                    _screenListBoundsY,
                    _screenListBoundsWidth,
                    _screenListBoundsHeight);
        }

        private void ToggleScreenList()
        {
            ShowScreenList = !ShowScreenList;
            if (!ShowScreenList)
            {
                _screenListDragging = false;
                _screenListBoundsValid = false;
            }
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

        private void ResetRuntimeState(int tick)
        {
            _maxHitpointsByActor.Clear();
            _lastSeenTickByActor.Clear();
            _ownersWithConfirmedSimDeath.Clear();
            _screenListDragging = false;
            _screenListBoundsValid = false;
            _lastPruneTick = tick;
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
