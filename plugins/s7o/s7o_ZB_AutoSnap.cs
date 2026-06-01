using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    /*
        ZB AutoSnap (clean)
        - Keeps the v1 one-shot cast feel: one SPACE edge -> one snap -> one spear
        - Local elite leaders first, then forward in-lane elite leaders, then high-value trash and density trash; elite minions are treated as trash bodies only
        - Uses native validity filters: illusion, knockback immunity, elite affixes, doors / obstacles / elite walls
        - No cursor restore, hold-repeat, tracked-elite pre-refine, or continuous-input arbitration
    */
    public class s7o_ZB_AutoSnap : BasePlugin, IKeyEventHandler, IAfterCollectHandler, IInGameWorldPainter, INewAreaHandler
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const byte VK_SHIFT = 0x10;
        private const uint KEYEVENTF_KEYUP_SIMPLE = 0x0002;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        // =========================
        // USER SETTINGS
        // =========================

        public float MinPlayerDistanceYards { get; set; } = 0f;
        public float MaxPlayerDistanceYards { get; set; } = 0f;

        public float CursorWorldRadiusYards { get; set; } = 22f;
        public float CursorWorldRadiusFallbackYards { get; set; } = 45f;
        public float CursorWorldConeDegrees { get; set; } = 95f;

        public float ForwardEliteScanMaxYards { get; set; } = 125f;
        public float ForwardEliteCommittedMaxYards { get; set; } = 140f;
        public float PreferredVisibleEliteMaxYards { get; set; } = 85f;
        public float ForwardEliteConeDegrees { get; set; } = 50f;

        public float HitboxRadiusPx { get; set; } = 65f;
        public int MaxCandidates { get; set; } = 10;
        public float TrashClusterRadiusYards { get; set; } = 12f;

        // Trash-only pulls stay conservative, but a strong straight-line density lane is often safer
        // than snapping sideways to a circular clump. This only runs after elite leader targets fail.
        public bool EnableLinearTrashDensityPull { get; set; } = true;
        public float LinearTrashLaneHalfWidthYards { get; set; } = 6.25f;
        public float LinearTrashMaxYards { get; set; } = 90f;
        public float LinearTrashMaxAngleDegrees { get; set; } = 28f;
        public int LinearTrashMinBodies { get; set; } = 4;
        public int LinearTrashPreferWithinBodies { get; set; } = 2;

        // Actual Ancient Spear input hold time.
        // 45ms is long enough to register one press, but far below the range where a second skill use should occur.
        public int SpearInputHoldMs { get; set; } = 45;

        // Optional tiny shift pre-hold for left/right skill fallback.
        public int ShiftPreHoldMs { get; set; } = 4;

        // Deprecated: elite minions are intentionally not priority targets; they are treated like trash density bodies.
        public bool IncludeEliteMinions { get; set; } = false;
        public bool IgnoreJuggernaut { get; set; } = true;
        public bool IgnoreShieldingWhenActive { get; set; } = true;
        public bool IgnoreIllusions { get; set; } = true;
        public bool IgnoreKnownUnpullables { get; set; } = true;
        public bool IgnoreKnockbackImmune { get; set; } = true;
        public bool AllowAutoShiftFallback { get; set; } = true;

        public Key Hotkey { get; set; } = Key.Space;

        public void SetHotkey(Key key)
        {
            if (key == Key.Unknown)
                return;

            Hotkey = key;
            ResetTransientState(false);
        }

        public Key GetHotkey()
        {
            return Hotkey;
        }

        public string GetHotkeyLabel()
        {
            return FormatHotkeyLabel(Hotkey);
        }

        public void ForceStopForDisable()
        {
            ResetTransientState(false);
        }

        private static string FormatHotkeyLabel(Key key)
        {
            if (key == Key.Unknown) return "[None]";
            if (key == Key.LeftBracket) return "[";
            if (key == Key.RightBracket) return "]";
            string s = key.ToString();
            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s.Substring(1);
            return "[" + s + "]";
        }

        public bool PreferHighValueTrash { get; set; } = true;
        public float HighValueTrashMinRiftProgression { get; set; } = 0.80f;
        public bool PreferNamedBigTrash { get; set; } = true;

        public float StraightPathPenaltyScale { get; set; } = 0.90f;
        public float NearbyAlternativeRadiusPx { get; set; } = 110f;
        public float MaxSnapAngleDegrees { get; set; } = 30f;
        public bool UseGeometrySafety { get; set; } = true;

        // Elite course correction is a small aim assist for near-miss spear casts.
        // It only corrects within the player's current spear direction, so it should not snap sideways.
        public bool EnableEliteCourseCorrection { get; set; } = true;
        public float EliteCourseCorrectScreenRadiusPx { get; set; } = 260f;
        public float EliteCourseCorrectWorldRadiusYards { get; set; } = 28f;
        public float EliteCourseCorrectLaneHalfWidthYards { get; set; } = 18f;
        public float EliteCourseCorrectMaxAngleDegrees { get; set; } = 58f;
        public int EliteCourseCorrectCacheMaxTicks { get; set; } = 1;
        public float EliteCourseCorrectCacheCursorMaxPx { get; set; } = 24f;

        // Visible elites close to the cursor are treated as committed targets before normal ray scoring.
        // This is the main autosnap guarantee: obvious elite near-misses should not fly past the elite.
        public bool EnableCommittedVisibleEliteSnap { get; set; } = true;
        public float CommittedEliteScreenRadiusPx { get; set; } = 210f;
        public float CommittedEliteFarScreenRadiusPx { get; set; } = 360f;
        public float CommittedEliteFarStartYards { get; set; } = 30f;
        public float CommittedEliteWorldRadiusYards { get; set; } = 30f;
        public float CommittedEliteLaneYards { get; set; } = 24f;
        public float CommittedEliteSoftSafetyGrace { get; set; } = 12f;

        // Lean blocker awareness. These are hard evidence sources only; no large flow/heatmap system.
        public bool IncludePylonsAsPullBlockers { get; set; } = true;
        public bool IncludeWallerAsPullBlockers { get; set; } = true;
        public bool IncludeProjectileBlockingActors { get; set; } = true;
        public bool EnableSpearImpactMemory { get; set; } = true;
        public int SpearImpactProbeWindowMs { get; set; } = 850;
        public int SpearImpactMemoryMs { get; set; } = 7500;
        public float SpearImpactUnsafeRadiusYards { get; set; } = 5.0f;
        public float SpearImpactMinShortfallYards { get; set; } = 8.0f;
        public float SpearImpactUnsafePenaltyWeight { get; set; } = 20f;

        // If there is no meaningful multi-elite line-up, aim closer to the elite core to avoid edge misses.
        public bool EnableSingleEliteCoreBias { get; set; } = true;
        public float SingleEliteCoreBiasSafetyGrace { get; set; } = 4.0f;

        // Small delay after moving the cursor before tapping spear. This gives the game one brief moment
        // to sample the corrected cursor position before the skill key is pressed.
        public int SpearPreCastSettleMs { get; set; } = 12;

        public bool DrawCastMarker { get; set; } = true;
        public int MarkerDurationTicks { get; set; } = 60;
        public float MarkerCircleRadiusYards { get; set; } = 22f;

        // =========================
        // INTERNAL CONSTANTS
        // =========================

        private const float SimpleLaneHalfWidthYards = 5.25f;
        private const float SimpleClampAimDistanceYards = 48f;
        private const float SimpleMaxClampAimDistanceYards = 72f;
        private const float SimpleDirectAimDistanceYards = 32f;
        private const float SimpleDirectAimScreenMarginPx = 96f;
        private const float SimpleManualCommitCursorYards = 7f;
        private const float SimpleFarIntentStartYards = 24f;
        private const float SimpleNearPlayerPenaltyYards = 18f;
        private const float SimpleNearPlayerPenalty = 280f;
        private const float SimpleLateralWeight = 13.0f;
        private const float SimpleCursorDistanceWeight = 0.85f;
        private const float SimpleFarPreferenceWeight = 0.60f;
        private const float SimpleSafetyPenaltyWeight = 2.90f;
        private const float SimpleExtraEliteLineBonus = 28f;
        private const float SimpleLineCountLaneWidthYards = 5.75f;
        private const float SimplePathRejectThreshold = 18f;
        private const float SimpleFarEliteBandYards = 18f;

        private const float ObstaclePenaltyWeight = 10f;
        private const float EliteWallPenaltyWeight = 16f;
        private const float BlockerSafetyMarginYards = 0.90f;

        // =========================
        // INTERNAL STATE
        // =========================

        private IController Hud;
        private IUiElement _chatEdit;

        private bool _hotkeyDownLatched;
        private bool _castingNow;

        private bool _castPending;
        private int _castTick;
        private float _aimX;
        private float _aimY;
        private IWorldCoordinate _pendingMarkerWorld;
        private uint _pendingMarkerTargetAcdId;

        private ActionKey _spearKey = ActionKey.Unknown;
        private int _lastObservedGameTick = -1;

        private IWorldCoordinate _markerWorld;
        private uint _markerTargetAcdId;
        private int _markerStartTick;
        private int _markerUntilTick;
        private bool _markerFailed;

        private bool _courseCorrectCacheValid;
        private int _courseCorrectCacheTick;
        private float _courseCorrectCacheCursorX;
        private float _courseCorrectCacheCursorY;
        private float _courseCorrectCacheAimX;
        private float _courseCorrectCacheAimY;
        private IWorldCoordinate _courseCorrectCacheMarkerWorld;
        private uint _courseCorrectCacheTargetAcdId;

        private SpearImpactProbe _spearImpactProbe;
        private readonly List<UnsafeImpactSample> _unsafeImpactSamples = new List<UnsafeImpactSample>(12);

        private struct SpearImpactProbe
        {
            public bool Active;
            public int CastTick;
            public int ExpireTick;
            public float FromX;
            public float FromY;
            public float ToX;
            public float ToY;
            public float DirX;
            public float DirY;
            public float Distance;
            public uint TargetAcdId;
        }

        private struct UnsafeImpactSample
        {
            public float X;
            public float Y;
            public int ExpireTick;
        }

        private struct EliteCourseCandidate
        {
            public IMonster Monster;
            public float Score;
            public float ScreenX;
            public float ScreenY;
            public IWorldCoordinate MarkerWorld;
            public uint TargetAcdId;
        }

        private sealed class BlockerCircle
        {
            public float X;
            public float Y;
            public float Radius;
        }

        private GroundCircleDecorator _circleGreen;
        private GroundCircleDecorator _dotGreen;
        private GroundCircleDecorator _circleRed;
        private GroundCircleDecorator _dotRed;

        private enum LocalAimResult
        {
            None,
            Chosen,
            ForwardPreferred,
        }

        public s7o_ZB_AutoSnap()
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            Hud = hud;

            _chatEdit = Hud.Render.GetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline");
            ResolveSpearKey();

            _circleGreen = new GroundCircleDecorator(Hud)
            {
                Enabled = true,
                Radius = MarkerCircleRadiusYards,
                Brush = Hud.Render.CreateBrush(180, 0, 255, 0, 2.5f),
                HasShadow = true,
            };
            _dotGreen = new GroundCircleDecorator(Hud)
            {
                Enabled = true,
                Radius = 1.5f,
                Brush = Hud.Render.CreateBrush(230, 0, 255, 0, 4.0f),
                HasShadow = false,
            };

            _circleRed = new GroundCircleDecorator(Hud)
            {
                Enabled = true,
                Radius = MarkerCircleRadiusYards,
                Brush = Hud.Render.CreateBrush(200, 255, 0, 0, 2.5f),
                HasShadow = true,
            };
            _dotRed = new GroundCircleDecorator(Hud)
            {
                Enabled = true,
                Radius = 1.5f,
                Brush = Hud.Render.CreateBrush(240, 255, 0, 0, 4.0f),
                HasShadow = false,
            };
        }

        // =========================
        // PIPELINE
        // =========================

        public void OnKeyEvent(IKeyEvent keyEvent)
        {
            // Intentionally no-op.
            // Casting is handled by the native physical hotkey edge detector in AfterCollect().
            // Do not start casts here, or key-repeat / duplicate input paths can double-cast.
        }


        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ResetTransientState(true);
        }

        public void AfterCollect()
        {
            if (!Enabled || Hud == null) return;

            int tick = 0;
            try
            {
                tick = Hud.Game.CurrentGameTick;

                if (_lastObservedGameTick >= 0 && tick < _lastObservedGameTick)
                    ResetTransientState(false);
                _lastObservedGameTick = tick;

                bool hotkeyDown = IsHotkeyPhysicallyDownNative();

                if (!CanRun())
                {
                    // If the key is held while the plugin cannot run, consume that hold.
                    // It must not fire later when CanRun() becomes true.
                    _hotkeyDownLatched = hotkeyDown;
                    ClearPendingCastOnly();
                    ClearEliteCourseCorrectionCache();
                    return;
                }

                UpdateEliteCourseCorrectionCache(tick);
                UpdateSpearImpactProbe(tick);
                PruneUnsafeImpactSamples(tick);

                if (!hotkeyDown)
                {
                    // Physical release rearms immediately.
                    _hotkeyDownLatched = false;
                }
                else if (!_hotkeyDownLatched)
                {
                    // One physical down-edge = one spear.
                    _hotkeyDownLatched = true;
                    StartOneCast(tick);
                }
            }
            catch
            {
                HardCleanup();
            }
        }


        private void StartOneCast(int tick)
        {
            if (_castingNow)
                return;

            _castingNow = true;

            try
            {
                ResolveSpearKey();

                if (_spearKey == ActionKey.Unknown)
                    return;

                var me = Hud.Game.Me;
                if (me == null)
                    return;

                float aimX;
                float aimY;
                IWorldCoordinate markerWorld;
                bool failed;

                float cursorX = Hud.Window.CursorX;
                float cursorY = Hud.Window.CursorY;

                ComputeAimNearCursor(
                    me.FloorCoordinate,
                    cursorX,
                    cursorY,
                    out aimX,
                    out aimY,
                    out markerWorld,
                    out failed);

                uint courseCorrectTargetAcdId = 0;
                TryApplyEliteCourseCorrection(
                    me.FloorCoordinate,
                    cursorX,
                    cursorY,
                    tick,
                    ref aimX,
                    ref aimY,
                    ref markerWorld,
                    ref failed,
                    out courseCorrectTargetAcdId);

                IWorldCoordinate finalMarkerWorld = markerWorld ?? GetScreenWorld(aimX, aimY) ?? me.FloorCoordinate;
                uint finalMarkerTargetAcdId = courseCorrectTargetAcdId != 0
                    ? courseCorrectTargetAcdId
                    : FindMarkerTargetAcdId(finalMarkerWorld, aimX, aimY, failed);

                _markerFailed = failed;

                SafeMouseMove(aimX, aimY);

                int settleMs = ClampInt(SpearPreCastSettleMs, 0, 35);
                if (settleMs > 0)
                    Thread.Sleep(settleMs);

                BeginSpearImpactProbe(tick, me.FloorCoordinate, finalMarkerWorld, finalMarkerTargetAcdId);

                TapSpear();

                if (DrawCastMarker)
                {
                    _markerWorld = finalMarkerWorld;
                    _markerTargetAcdId = finalMarkerTargetAcdId;
                    _markerStartTick = tick;
                    _markerUntilTick = tick + MarkerDurationTicks;
                }
            }
            catch
            {
            }
            finally
            {
                _castingNow = false;
            }
        }


        // =========================
        // AIM PIPELINE
        // =========================

        private void ComputeAimNearCursor(IWorldCoordinate mePos, float cursorX, float cursorY, out float aimX, out float aimY, out IWorldCoordinate markerWorld, out bool failed)
        {
            aimX = cursorX;
            aimY = cursorY;
            failed = false;

            var cursorWorld = GetScreenWorld(cursorX, cursorY);
            markerWorld = cursorWorld ?? mePos;
            if (cursorWorld == null) return;

            float vx = cursorWorld.X - mePos.X;
            float vy = cursorWorld.Y - mePos.Y;
            float vlen = (float)Math.Sqrt(vx * vx + vy * vy);

            bool useCone = (CursorWorldConeDegrees < 179.5f) && (vlen > 0.1f);
            if (vlen > 0.1f)
            {
                vx /= vlen;
                vy /= vlen;
            }

            float cosTh = (float)Math.Cos(CursorWorldConeDegrees * Math.PI / 180.0);

            LocalAimResult result;

            result = TryLocalAim(mePos, cursorWorld, useCone, vx, vy, cosTh, CursorWorldRadiusYards, cursorX, cursorY, true, out aimX, out aimY, out markerWorld, out failed);
            if (result == LocalAimResult.Chosen) return;
            if (result == LocalAimResult.ForwardPreferred && TrySimpleForwardEliteAim(mePos, cursorWorld, useCone, vx, vy, cursorX, cursorY, out aimX, out aimY, out markerWorld))
                return;

            result = TryLocalAim(mePos, cursorWorld, useCone, vx, vy, cosTh, CursorWorldRadiusYards, cursorX, cursorY, false, out aimX, out aimY, out markerWorld, out failed);
            if (result == LocalAimResult.Chosen) return;

            result = TryLocalAim(mePos, cursorWorld, useCone, vx, vy, cosTh, CursorWorldRadiusFallbackYards, cursorX, cursorY, true, out aimX, out aimY, out markerWorld, out failed);
            if (result == LocalAimResult.Chosen) return;
            if (result == LocalAimResult.ForwardPreferred && TrySimpleForwardEliteAim(mePos, cursorWorld, useCone, vx, vy, cursorX, cursorY, out aimX, out aimY, out markerWorld))
                return;

            result = TryLocalAim(mePos, cursorWorld, useCone, vx, vy, cosTh, CursorWorldRadiusFallbackYards, cursorX, cursorY, false, out aimX, out aimY, out markerWorld, out failed);
            if (result == LocalAimResult.Chosen) return;

            TrySimpleForwardEliteAim(mePos, cursorWorld, useCone, vx, vy, cursorX, cursorY, out aimX, out aimY, out markerWorld);
        }

        private LocalAimResult TryLocalAim(
            IWorldCoordinate mePos,
            IWorldCoordinate cursorWorld,
            bool useCone,
            float vx,
            float vy,
            float cosTh,
            float radius,
            float baseCursorX,
            float baseCursorY,
            bool deferLowerTiersToForward,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld,
            out bool failed)
        {
            aimX = baseCursorX;
            aimY = baseCursorY;
            markerWorld = cursorWorld;
            failed = false;

            var leaders = new List<IMonster>(16);
            var highTrash = new List<IMonster>(24);
            var trash = new List<IMonster>(48);

            IMonster nearestEliteAny = null;
            float nearestEliteDist = float.MaxValue;
            bool nearestEliteHardInvalid = false;

            foreach (var m in Hud.Game.AliveMonsters)
            {
                if (m == null || !m.IsAlive || !m.IsOnScreen) continue;
                if (!IsWithinWindow(m.ScreenCoordinate?.X ?? -999f, m.ScreenCoordinate?.Y ?? -999f)) continue;

                float distToMe = m.FloorCoordinate.XYDistanceTo(mePos);
                if (MinPlayerDistanceYards > 0 && distToMe < MinPlayerDistanceYards) continue;
                if (MaxPlayerDistanceYards > 0 && distToMe > MaxPlayerDistanceYards) continue;

                float dCursor = m.FloorCoordinate.XYDistanceTo(cursorWorld);
                if (dCursor > radius) continue;

                if (useCone)
                {
                    float tx = m.FloorCoordinate.X - mePos.X;
                    float ty = m.FloorCoordinate.Y - mePos.Y;
                    float tlen = (float)Math.Sqrt(tx * tx + ty * ty);
                    if (tlen > 0.1f)
                    {
                        tx /= tlen;
                        ty /= tlen;
                        float dot = tx * vx + ty * vy;
                        if (dot < cosTh) continue;
                    }
                }

                if (m.IsElite && !IsEliteMinion(m))
                {
                    if (dCursor < nearestEliteDist)
                    {
                        nearestEliteDist = dCursor;
                        nearestEliteAny = m;
                        nearestEliteHardInvalid = IsEliteHardInvalidForPull(m);
                    }

                    if (IsEliteBlocked(m) || !IsEliteAllowed(m))
                        continue;

                    if (IsLeaderElite(m))
                        leaders.Add(m);
                }
                else
                {
                    if (!IsPullableTrashAllowed(m))
                        continue;

                    if (IsHighValueTrash(m))
                        highTrash.Add(m);
                    else
                        trash.Add(m);
                }
            }

            if (nearestEliteAny != null && nearestEliteHardInvalid)
                failed = true;

            leaders = leaders.OrderBy(e => e.FloorCoordinate.XYDistanceTo(cursorWorld)).Take(MaxCandidates).ToList();
            highTrash = highTrash.OrderByDescending(GetHighValueTrashScore).ThenBy(t => t.FloorCoordinate.XYDistanceTo(cursorWorld)).Take(MaxCandidates).ToList();
            trash = trash.OrderByDescending(GetTrashDensityScore).ThenBy(t => t.FloorCoordinate.XYDistanceTo(cursorWorld)).Take(MaxCandidates).ToList();

            uint primaryLeaderId;

            if (leaders.Count > 0)
            {
                if (ComputeLocalOverlapAimPoint(leaders, highTrash, trash, true, false, mePos, baseCursorX, baseCursorY,
                    out aimX, out aimY, out primaryLeaderId))
                {
                    if (primaryLeaderId != 0)
                    {
                        for (int i = 0; i < leaders.Count; i++)
                            if (leaders[i].AcdId == primaryLeaderId)
                            {
                                markerWorld = leaders[i].FloorCoordinate;
                                return LocalAimResult.Chosen;
                            }
                    }

                    markerWorld = leaders[0].FloorCoordinate;
                    return LocalAimResult.Chosen;
                }
            }

            if (deferLowerTiersToForward && HasAnyForwardEliteCandidate(mePos, useCone, vx, vy))
                return LocalAimResult.ForwardPreferred;

            if (EnableLinearTrashDensityPull && (highTrash.Count > 0 || trash.Count > 0))
            {
                if (TryLinearTrashDensityAim(mePos, cursorWorld, useCone, vx, vy, cosTh, highTrash, trash, baseCursorX, baseCursorY,
                    out aimX, out aimY, out markerWorld))
                {
                    return LocalAimResult.Chosen;
                }
            }

            if (highTrash.Count > 0)
            {
                if (ComputeLocalOverlapAimPoint(leaders, highTrash, trash, false, true, mePos, baseCursorX, baseCursorY,
                    out aimX, out aimY, out primaryLeaderId))
                {
                    markerWorld = highTrash.OrderByDescending(GetHighValueTrashScore).ThenBy(t => t.FloorCoordinate.XYDistanceTo(cursorWorld)).First().FloorCoordinate;
                    return LocalAimResult.Chosen;
                }
            }

            if (trash.Count > 0)
            {
                if (ComputeLocalOverlapAimPoint(leaders, highTrash, trash, false, false, mePos, baseCursorX, baseCursorY,
                    out aimX, out aimY, out primaryLeaderId))
                {
                    markerWorld = trash.OrderByDescending(GetTrashDensityScore).ThenBy(t => t.FloorCoordinate.XYDistanceTo(cursorWorld)).First().FloorCoordinate;
                    return LocalAimResult.Chosen;
                }
            }

            if (nearestEliteAny != null && nearestEliteHardInvalid)
            {
                bool hasNearbyAlternative = HasNearbyValidEliteAlternative(nearestEliteAny, leaders, NearbyAlternativeRadiusPx);
                if (!hasNearbyAlternative)
                {
                    aimX = baseCursorX;
                    aimY = baseCursorY;
                    markerWorld = nearestEliteAny.FloorCoordinate;
                    failed = true;
                    return LocalAimResult.Chosen;
                }
            }

            return LocalAimResult.None;
        }

        // =========================
        // ELITE COURSE CORRECTION
        // =========================

        private void UpdateEliteCourseCorrectionCache(int tick)
        {
            try
            {
                if (!EnableEliteCourseCorrection || Hud?.Game?.Me == null)
                {
                    ClearEliteCourseCorrectionCache();
                    return;
                }

                float cursorX = Hud.Window.CursorX;
                float cursorY = Hud.Window.CursorY;
                float aimX;
                float aimY;
                IWorldCoordinate markerWorld;
                uint targetAcdId;

                if (TryComputeEliteCourseCorrection(Hud.Game.Me.FloorCoordinate, cursorX, cursorY, out aimX, out aimY, out markerWorld, out targetAcdId))
                {
                    _courseCorrectCacheValid = true;
                    _courseCorrectCacheTick = tick;
                    _courseCorrectCacheCursorX = cursorX;
                    _courseCorrectCacheCursorY = cursorY;
                    _courseCorrectCacheAimX = aimX;
                    _courseCorrectCacheAimY = aimY;
                    _courseCorrectCacheMarkerWorld = markerWorld;
                    _courseCorrectCacheTargetAcdId = targetAcdId;
                }
                else
                {
                    ClearEliteCourseCorrectionCache();
                }
            }
            catch
            {
                ClearEliteCourseCorrectionCache();
            }
        }

        private void ClearEliteCourseCorrectionCache()
        {
            _courseCorrectCacheValid = false;
            _courseCorrectCacheTick = 0;
            _courseCorrectCacheCursorX = 0f;
            _courseCorrectCacheCursorY = 0f;
            _courseCorrectCacheAimX = 0f;
            _courseCorrectCacheAimY = 0f;
            _courseCorrectCacheMarkerWorld = null;
            _courseCorrectCacheTargetAcdId = 0;
        }

        private bool TryApplyEliteCourseCorrection(
            IWorldCoordinate mePos,
            float cursorX,
            float cursorY,
            int tick,
            ref float aimX,
            ref float aimY,
            ref IWorldCoordinate markerWorld,
            ref bool failed,
            out uint targetAcdId)
        {
            targetAcdId = 0;

            try
            {
                if (!EnableEliteCourseCorrection)
                    return false;

                float cachedAimX;
                float cachedAimY;
                IWorldCoordinate cachedMarkerWorld;
                uint cachedTargetAcdId;
                if (TryGetCachedEliteCourseCorrection(tick, cursorX, cursorY, out cachedAimX, out cachedAimY, out cachedMarkerWorld, out cachedTargetAcdId))
                {
                    aimX = cachedAimX;
                    aimY = cachedAimY;
                    markerWorld = cachedMarkerWorld ?? markerWorld;
                    failed = false;
                    targetAcdId = cachedTargetAcdId;
                    return true;
                }

                if (TryComputeEliteCourseCorrection(mePos, cursorX, cursorY, out cachedAimX, out cachedAimY, out cachedMarkerWorld, out cachedTargetAcdId))
                {
                    aimX = cachedAimX;
                    aimY = cachedAimY;
                    markerWorld = cachedMarkerWorld ?? markerWorld;
                    failed = false;
                    targetAcdId = cachedTargetAcdId;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryGetCachedEliteCourseCorrection(
            int tick,
            float cursorX,
            float cursorY,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld,
            out uint targetAcdId)
        {
            aimX = cursorX;
            aimY = cursorY;
            markerWorld = null;
            targetAcdId = 0;

            try
            {
                if (!_courseCorrectCacheValid || _courseCorrectCacheTargetAcdId == 0)
                    return false;

                int maxTicks = ClampInt(EliteCourseCorrectCacheMaxTicks, 0, 10);
                if (maxTicks <= 0 || tick - _courseCorrectCacheTick > maxTicks)
                    return false;

                float maxCursor = EliteCourseCorrectCacheCursorMaxPx;
                if (maxCursor < 0f) maxCursor = 0f;
                if (maxCursor > 80f) maxCursor = 80f;

                if (Dist(cursorX, cursorY, _courseCorrectCacheCursorX, _courseCorrectCacheCursorY) > maxCursor)
                    return false;

                aimX = _courseCorrectCacheAimX;
                aimY = _courseCorrectCacheAimY;
                markerWorld = _courseCorrectCacheMarkerWorld;
                targetAcdId = _courseCorrectCacheTargetAcdId;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryComputeEliteCourseCorrection(
            IWorldCoordinate mePos,
            float cursorX,
            float cursorY,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld,
            out uint targetAcdId)
        {
            aimX = cursorX;
            aimY = cursorY;
            markerWorld = null;
            targetAcdId = 0;

            try
            {
                if (!EnableEliteCourseCorrection || mePos == null || Hud?.Game?.AliveMonsters == null)
                    return false;

                var cursorWorld = GetScreenWorld(cursorX, cursorY);
                if (cursorWorld == null)
                    return false;

                float vx = cursorWorld.X - mePos.X;
                float vy = cursorWorld.Y - mePos.Y;
                float vlen = (float)Math.Sqrt(vx * vx + vy * vy);
                if (vlen <= 0.1f)
                    return false;
                vx /= vlen;
                vy /= vlen;

                float normalReach = ForwardEliteScanMaxYards;
                if (MaxPlayerDistanceYards > 0)
                    normalReach = Math.Min(normalReach, MaxPlayerDistanceYards);
                if (normalReach <= 0f)
                    normalReach = 90f;

                float committedReach = Math.Max(normalReach, ForwardEliteCommittedMaxYards);
                if (MaxPlayerDistanceYards > 0)
                    committedReach = Math.Min(committedReach, MaxPlayerDistanceYards);

                float maxAngle = EliteCourseCorrectMaxAngleDegrees;
                if (maxAngle < 5f) maxAngle = 5f;
                if (maxAngle > 75f) maxAngle = 75f;
                float angleCos = (float)Math.Cos(maxAngle * Math.PI / 180.0);

                float screenRadius = EliteCourseCorrectScreenRadiusPx;
                if (screenRadius < 0f) screenRadius = 0f;
                if (screenRadius > 420f) screenRadius = 420f;

                float worldRadius = EliteCourseCorrectWorldRadiusYards;
                if (worldRadius < 0f) worldRadius = 0f;
                if (worldRadius > 55f) worldRadius = 55f;

                float laneWidth = EliteCourseCorrectLaneHalfWidthYards;
                if (laneWidth < 0f) laneWidth = 0f;
                if (laneWidth > 32f) laneWidth = 32f;

                var validElitesForLineScore = BuildEliteListForCourseCorrection();
                var validLeaderElitesForLineScore = BuildEliteLeaderListForCourseCorrection();

                EliteCourseCandidate bestCommitted = new EliteCourseCandidate { Score = float.MaxValue };
                EliteCourseCandidate bestReliable = new EliteCourseCandidate { Score = float.MaxValue };
                EliteCourseCandidate bestQuestionable = new EliteCourseCandidate { Score = float.MaxValue };

                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || !m.IsElite || m.FloorCoordinate == null) continue;
                    if (IsEliteBlocked(m) || !IsEliteAllowed(m)) continue;

                    float relX = m.FloorCoordinate.X - mePos.X;
                    float relY = m.FloorCoordinate.Y - mePos.Y;
                    float distToMe = (float)Math.Sqrt(relX * relX + relY * relY);
                    if (distToMe <= 0.1f) continue;
                    if (MinPlayerDistanceYards > 0 && distToMe < MinPlayerDistanceYards) continue;

                    float sx;
                    float sy;
                    if (!TryGetScreen(m, out sx, out sy)) continue;
                    float screenDistToCursor = Dist(sx, sy, cursorX, cursorY);
                    bool visibleInWindow = IsWithinWindow(sx, sy);
                    float committedScreenRadius = distToMe >= CommittedEliteFarStartYards
                        ? CommittedEliteFarScreenRadiusPx
                        : CommittedEliteScreenRadiusPx;
                    if (committedScreenRadius < 60f) committedScreenRadius = 60f;
                    if (committedScreenRadius > 460f) committedScreenRadius = 460f;

                    bool screenCommitted = EnableCommittedVisibleEliteSnap && screenDistToCursor <= committedScreenRadius;
                    if (distToMe > normalReach && (!screenCommitted || distToMe > committedReach)) continue;

                    float proj = relX * vx + relY * vy;
                    if (proj <= 0.1f) continue;

                    float dot = proj / Math.Max(0.001f, distToMe);
                    bool normalAngleOk = dot >= angleCos;
                    bool generallyForward = dot >= 0.18f;
                    if (!normalAngleOk && (!screenCommitted || !generallyForward)) continue;

                    float lateral = Math.Abs(relX * vy - relY * vx);
                    float worldDistToCursor = m.FloorCoordinate.XYDistanceTo(cursorWorld);

                    bool screenNear = screenRadius > 0f && screenDistToCursor <= screenRadius && (visibleInWindow || screenCommitted);
                    bool worldNear = worldRadius > 0f && worldDistToCursor <= worldRadius;
                    bool laneNear = laneWidth > 0f && lateral <= laneWidth;
                    bool committed = screenCommitted || (worldDistToCursor <= CommittedEliteWorldRadiusYards && lateral <= CommittedEliteLaneYards && generallyForward);

                    if (!committed && !screenNear && !worldNear && !laneNear)
                        continue;

                    float safetyPenalty = Math.Max(0f, ComputeLineSafetyPenalty(mePos, m.FloorCoordinate));
                    float hardPenalty = Math.Max(0f, ComputeHardBlockerPenalty(mePos, m.FloorCoordinate));
                    if (hardPenalty >= SimplePathRejectThreshold)
                        continue;
                    if (!committed && safetyPenalty >= SimplePathRejectThreshold)
                        continue;
                    if (committed && safetyPenalty >= SimplePathRejectThreshold + CommittedEliteSoftSafetyGrace)
                        continue;

                    int lineHits = CountSimpleLineHits(IsLeaderElite(m) ? validLeaderElitesForLineScore : validElitesForLineScore, mePos, m, SimpleLineCountLaneWidthYards);
                    bool reliableVisible = visibleInWindow && distToMe <= PreferredVisibleEliteMaxYards;
                    bool questionable = !reliableVisible && (distToMe > PreferredVisibleEliteMaxYards || !visibleInWindow);

                    float score = (lateral * 8.0f)
                                + (worldDistToCursor * 2.4f)
                                + (screenDistToCursor * 0.018f)
                                + (safetyPenalty * 4.0f)
                                + (hardPenalty * 8.0f)
                                - (lineHits * 42.0f);

                    if (IsLeaderElite(m)) score -= 65f;
                    else score -= 24f;
                    if (committed) score -= 260f;
                    if (screenNear) score -= 45f;
                    if (worldNear) score -= 28f;
                    if (laneNear) score -= 18f;
                    if (reliableVisible) score -= 55f;
                    if (questionable && !committed) score += 145f;
                    if (distToMe < SimpleNearPlayerPenaltyYards && !committed)
                        score += 180f + ((SimpleNearPlayerPenaltyYards - distToMe) * 12f);

                    EliteCourseCandidate candidate = new EliteCourseCandidate
                    {
                        Monster = m,
                        Score = score,
                        ScreenX = sx,
                        ScreenY = sy,
                        MarkerWorld = m.FloorCoordinate,
                        TargetAcdId = (uint)m.AcdId,
                    };

                    if (committed)
                    {
                        if (candidate.Score < bestCommitted.Score)
                            bestCommitted = candidate;
                    }
                    else if (reliableVisible)
                    {
                        if (candidate.Score < bestReliable.Score)
                            bestReliable = candidate;
                    }
                    else
                    {
                        if (candidate.Score < bestQuestionable.Score)
                            bestQuestionable = candidate;
                    }
                }

                EliteCourseCandidate best = bestCommitted.Monster != null
                    ? bestCommitted
                    : (bestReliable.Monster != null ? bestReliable : bestQuestionable);

                if (best.Monster == null)
                    return false;

                aimX = best.ScreenX;
                aimY = best.ScreenY;
                markerWorld = best.MarkerWorld;
                targetAcdId = best.TargetAcdId;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<IMonster> BuildEliteLeaderListForCourseCorrection()
        {
            var list = new List<IMonster>(16);
            try
            {
                if (Hud?.Game?.AliveMonsters == null) return list;
                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || !m.IsElite || m.FloorCoordinate == null) continue;
                    if (!IsLeaderElite(m)) continue;
                    if (IsEliteBlocked(m) || !IsEliteAllowed(m)) continue;
                    list.Add(m);
                }
            }
            catch { }
            return list;
        }

        private List<IMonster> BuildEliteListForCourseCorrection()
        {
            var list = new List<IMonster>(24);
            try
            {
                if (Hud?.Game?.AliveMonsters == null) return list;
                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || !m.IsElite || m.FloorCoordinate == null) continue;
                    if (!IsEliteBlocked(m) && IsEliteAllowed(m))
                        list.Add(m);
                }
            }
            catch { }
            return list;
        }

        // =========================
        // FORWARD ELITE ASSIST
        // =========================

        private bool HasAnyForwardEliteCandidate(IWorldCoordinate mePos, bool useCone, float vx, float vy)
        {
            if (Hud?.Game?.AliveMonsters == null) return false;

            float maxReach = ForwardEliteScanMaxYards;
            if (MaxPlayerDistanceYards > 0)
                maxReach = Math.Min(maxReach, MaxPlayerDistanceYards);

            float coneCos = (float)Math.Cos(ForwardEliteConeDegrees * Math.PI / 180.0);

            foreach (var m in Hud.Game.AliveMonsters)
            {
                if (m == null || !m.IsAlive || !m.IsElite) continue;
                if (!IsLeaderElite(m)) continue;
                if (IsEliteBlocked(m) || !IsEliteAllowed(m)) continue;

                float distToMe = m.FloorCoordinate.XYDistanceTo(mePos);
                if (MinPlayerDistanceYards > 0 && distToMe < MinPlayerDistanceYards) continue;
                if (maxReach > 0 && distToMe > maxReach) continue;

                float relX = m.FloorCoordinate.X - mePos.X;
                float relY = m.FloorCoordinate.Y - mePos.Y;
                float proj = relX * vx + relY * vy;
                if (proj <= 0.1f) continue;

                float lateral = Math.Abs(relX * vy - relY * vx);
                if (lateral > SimpleLaneHalfWidthYards) continue;

                if (useCone)
                {
                    float dot = proj / Math.Max(0.001f, distToMe);
                    if (dot < coneCos) continue;
                }

                return true;
            }

            return false;
        }

        private bool TrySimpleForwardEliteAim(
            IWorldCoordinate mePos,
            IWorldCoordinate cursorWorld,
            bool useCone,
            float vx,
            float vy,
            float cursorX,
            float cursorY,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld)
        {
            aimX = cursorX;
            aimY = cursorY;
            markerWorld = cursorWorld;

            if (Hud?.Game?.AliveMonsters == null) return false;

            float intendedDist = cursorWorld.XYDistanceTo(mePos);
            float maxReach = ForwardEliteScanMaxYards;
            if (MaxPlayerDistanceYards > 0)
                maxReach = Math.Min(maxReach, MaxPlayerDistanceYards);

            float forwardConeCos = (float)Math.Cos(ForwardEliteConeDegrees * Math.PI / 180.0);

            var leaders = new List<IMonster>(16);

            foreach (var m in Hud.Game.AliveMonsters)
            {
                if (m == null || !m.IsAlive || !m.IsElite) continue;
                if (!IsLeaderElite(m)) continue;
                if (IsEliteBlocked(m) || !IsEliteAllowed(m)) continue;

                float distToMe = m.FloorCoordinate.XYDistanceTo(mePos);
                if (MinPlayerDistanceYards > 0 && distToMe < MinPlayerDistanceYards) continue;
                if (maxReach > 0 && distToMe > maxReach) continue;

                float relX = m.FloorCoordinate.X - mePos.X;
                float relY = m.FloorCoordinate.Y - mePos.Y;
                float proj = relX * vx + relY * vy;
                if (proj <= 0.1f) continue;

                float lateral = Math.Abs(relX * vy - relY * vx);
                if (lateral > SimpleLaneHalfWidthYards) continue;

                if (useCone)
                {
                    float dot = proj / Math.Max(0.001f, distToMe);
                    if (dot < forwardConeCos) continue;
                }

                leaders.Add(m);
            }

            IMonster chosen;
            if (TrySimplePickForwardElite(leaders, mePos, cursorWorld, vx, vy, intendedDist, out chosen))
            {
                return TrySimpleBuildLaneAimPoint(mePos, cursorWorld, cursorX, cursorY, chosen, out aimX, out aimY, out markerWorld);
            }

            return false;
        }

        private bool TrySimplePickForwardElite(
            List<IMonster> candidates,
            IWorldCoordinate mePos,
            IWorldCoordinate cursorWorld,
            float vx,
            float vy,
            float intendedDist,
            out IMonster chosen)
        {
            chosen = null;
            if (candidates == null || candidates.Count == 0) return false;

            bool preferFar = intendedDist >= SimpleFarIntentStartYards;

            var manualPool = new List<IMonster>(candidates.Count);
            foreach (var m in candidates)
            {
                float cursorDist = m.FloorCoordinate.XYDistanceTo(cursorWorld);
                if (cursorDist <= SimpleManualCommitCursorYards)
                    manualPool.Add(m);
            }

            var pool = manualPool.Count > 0 ? manualPool : candidates;

            if (manualPool.Count == 0 && preferFar && pool.Count > 1)
            {
                float farthestProj = float.MinValue;
                foreach (var m in pool)
                {
                    float relX = m.FloorCoordinate.X - mePos.X;
                    float relY = m.FloorCoordinate.Y - mePos.Y;
                    float proj = relX * vx + relY * vy;
                    if (proj > farthestProj) farthestProj = proj;
                }

                float minProj = Math.Max(SimpleFarIntentStartYards, farthestProj - SimpleFarEliteBandYards);
                var farPool = new List<IMonster>(pool.Count);
                foreach (var m in pool)
                {
                    float relX = m.FloorCoordinate.X - mePos.X;
                    float relY = m.FloorCoordinate.Y - mePos.Y;
                    float proj = relX * vx + relY * vy;
                    if (proj + 0.01f >= minProj)
                        farPool.Add(m);
                }

                if (farPool.Count > 0)
                    pool = farPool;
            }

            float bestCleanScore = float.MaxValue;
            int bestCleanLineHits = -1;
            float bestCleanProj = float.MinValue;
            IMonster bestClean = null;

            float bestAnyScore = float.MaxValue;
            int bestAnyLineHits = -1;
            float bestAnyProj = float.MinValue;
            IMonster bestAny = null;

            foreach (var m in pool)
            {
                float relX = m.FloorCoordinate.X - mePos.X;
                float relY = m.FloorCoordinate.Y - mePos.Y;
                float playerDist = (float)Math.Sqrt(relX * relX + relY * relY);
                float proj = relX * vx + relY * vy;
                float lateral = Math.Abs(relX * vy - relY * vx);
                float cursorDist = m.FloorCoordinate.XYDistanceTo(cursorWorld);

                int lineHits = CountSimpleLineHits(candidates, mePos, m, SimpleLineCountLaneWidthYards);
                float safetyPenalty = Math.Max(0f, ComputeLineSafetyPenalty(mePos, m.FloorCoordinate));
                bool severeBlock = safetyPenalty >= SimplePathRejectThreshold;

                float score = (lateral * SimpleLateralWeight)
                            + (cursorDist * SimpleCursorDistanceWeight)
                            + (safetyPenalty * SimpleSafetyPenaltyWeight);

                if (manualPool.Count == 0 && preferFar)
                {
                    score -= Math.Min(proj, ForwardEliteScanMaxYards) * (SimpleFarPreferenceWeight * 2.0f);
                    if (proj + 4f < intendedDist)
                        score += (intendedDist - proj) * 0.85f;
                }
                else
                {
                    score += Math.Abs(playerDist - intendedDist) * 0.18f;
                }

                if (playerDist < SimpleNearPlayerPenaltyYards)
                    score += SimpleNearPlayerPenalty + ((SimpleNearPlayerPenaltyYards - playerDist) * 18f);

                score -= Math.Max(0, lineHits - 1) * SimpleExtraEliteLineBonus;

                bool betterAny =
                    (lineHits > bestAnyLineHits) ||
                    (lineHits == bestAnyLineHits && proj > bestAnyProj + 0.75f) ||
                    (lineHits == bestAnyLineHits && Math.Abs(proj - bestAnyProj) <= 0.75f && score < bestAnyScore);

                if (betterAny)
                {
                    bestAnyLineHits = lineHits;
                    bestAnyProj = proj;
                    bestAnyScore = score;
                    bestAny = m;
                }

                if (severeBlock)
                    continue;

                bool betterClean =
                    (lineHits > bestCleanLineHits) ||
                    (lineHits == bestCleanLineHits && proj > bestCleanProj + 0.75f) ||
                    (lineHits == bestCleanLineHits && Math.Abs(proj - bestCleanProj) <= 0.75f && score < bestCleanScore);

                if (betterClean)
                {
                    bestCleanLineHits = lineHits;
                    bestCleanProj = proj;
                    bestCleanScore = score;
                    bestClean = m;
                }
            }

            chosen = bestClean ?? bestAny;
            return chosen != null;
        }

        private int CountSimpleLineHits(List<IMonster> candidates, IWorldCoordinate mePos, IMonster anchor, float laneWidthYards)
        {
            if (candidates == null || candidates.Count == 0 || mePos == null || anchor == null) return 0;

            float ax = anchor.FloorCoordinate.X - mePos.X;
            float ay = anchor.FloorCoordinate.Y - mePos.Y;
            float alen = (float)Math.Sqrt(ax * ax + ay * ay);
            if (alen <= 0.001f) return 0;

            float nx = ax / alen;
            float ny = ay / alen;
            int count = 0;

            foreach (var m in candidates)
            {
                if (m == null || !m.IsAlive) continue;

                float rx = m.FloorCoordinate.X - mePos.X;
                float ry = m.FloorCoordinate.Y - mePos.Y;
                float proj = rx * nx + ry * ny;
                if (proj < 1.5f || proj > alen + 4f) continue;

                float lateral = Math.Abs(rx * ny - ry * nx);
                if (lateral <= laneWidthYards)
                    count++;
            }

            return count;
        }

        private bool TrySimpleBuildLaneAimPoint(
            IWorldCoordinate mePos,
            IWorldCoordinate cursorWorld,
            float baseCursorX,
            float baseCursorY,
            IMonster target,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld)
        {
            aimX = baseCursorX;
            aimY = baseCursorY;
            markerWorld = target?.FloorCoordinate ?? mePos;
            if (target == null) return false;

            float tx;
            float ty;
            if (!TryGetScreen(target, out tx, out ty))
                return false;

            var meScreen = mePos.ToScreenCoordinate();

            float relX = target.FloorCoordinate.X - mePos.X;
            float relY = target.FloorCoordinate.Y - mePos.Y;
            float playerDist = (float)Math.Sqrt(relX * relX + relY * relY);

            float manualDx = cursorWorld.X - mePos.X;
            float manualDy = cursorWorld.Y - mePos.Y;
            float manualDist = (float)Math.Sqrt(manualDx * manualDx + manualDy * manualDy);
            float nx;
            float ny;

            if (manualDist > 0.001f)
            {
                nx = manualDx / manualDist;
                ny = manualDy / manualDist;
            }
            else if (playerDist > 0.001f)
            {
                nx = relX / playerDist;
                ny = relY / playerDist;
            }
            else
            {
                nx = 1f;
                ny = 0f;
            }

            float proj = relX * nx + relY * ny;
            float lateral = Math.Abs(relX * ny - relY * nx);
            float directPenalty = Math.Max(0f, ComputeLineSafetyPenalty(mePos, target.FloorCoordinate));

            bool directCenterOk =
                directPenalty < (SimplePathRejectThreshold * 0.90f) &&
                ((playerDist <= SimpleDirectAimDistanceYards) ||
                 (lateral <= 2.75f &&
                  target.IsOnScreen &&
                  IsWithinWindow(tx, ty) &&
                  playerDist <= 52f &&
                  IsComfortableScreenAim(tx, ty, SimpleDirectAimScreenMarginPx)));

            if (directCenterOk)
            {
                aimX = tx;
                aimY = ty;
                return true;
            }

            float laneDist = proj > 0.001f ? proj : playerDist;
            if (laneDist < SimpleClampAimDistanceYards) laneDist = SimpleClampAimDistanceYards;
            if (laneDist > SimpleMaxClampAimDistanceYards) laneDist = SimpleMaxClampAimDistanceYards;

            float screenRatio = laneDist / Math.Max(manualDist, 0.001f);
            if (screenRatio > 1.20f) screenRatio = 1.20f;
            if (screenRatio < 0.35f) screenRatio = 0.35f;

            aimX = meScreen.X + ((baseCursorX - meScreen.X) * screenRatio);
            aimY = meScreen.Y + ((baseCursorY - meScreen.Y) * screenRatio);

            float snapBlend = playerDist >= 42f ? 0.20f : 0.32f;
            if (directPenalty >= SimplePathRejectThreshold)
                snapBlend *= 0.40f;

            aimX = (aimX * (1f - snapBlend)) + (tx * snapBlend);
            aimY = (aimY * (1f - snapBlend)) + (ty * snapBlend);

            if (Hud?.Window?.Size != null)
            {
                float minX = 8f;
                float minY = 8f;
                float maxX = Hud.Window.Size.Width - 8f;
                float maxY = Hud.Window.Size.Height - 8f;
                if (aimX < minX) aimX = minX;
                if (aimY < minY) aimY = minY;
                if (aimX > maxX) aimX = maxX;
                if (aimY > maxY) aimY = maxY;
            }

            return true;
        }

        private bool IsComfortableScreenAim(float x, float y, float margin)
        {
            if (Hud?.Window?.Size == null) return true;
            return x >= margin && y >= margin && x <= Hud.Window.Size.Width - margin && y <= Hud.Window.Size.Height - margin;
        }

        // =========================
        // LOCAL OVERLAP SOLVER
        // =========================

        private bool ComputeLocalOverlapAimPoint(
            List<IMonster> leaders,
            List<IMonster> highTrash,
            List<IMonster> trash,
            bool requireLeaderHit,
            bool requireHighTrashHit,
            IWorldCoordinate mePos,
            float baseCursorX,
            float baseCursorY,
            out float aimX,
            out float aimY,
            out uint primaryLeaderId)
        {
            aimX = baseCursorX;
            aimY = baseCursorY;
            primaryLeaderId = 0;

            var leaderCenters = BuildScreenCenters(leaders);
            var highTrashCenters = BuildScreenCenters(highTrash);
            var trashCenters = BuildScreenCenters(trash);

            if (leaderCenters.Count == 0 && highTrashCenters.Count == 0 && trashCenters.Count == 0)
                return false;

            float curX = baseCursorX;
            float curY = baseCursorY;
            var manualWorld = GetScreenWorld(baseCursorX, baseCursorY);

            var pts = BuildLocalCandidatePoints(leaderCenters, highTrashCenters, trashCenters, curX, curY);

            int bestLeaderHits = -1;
            int bestHighTrashHits = -1;
            int bestTrashHits = -1;
            float bestSafetyPenalty = float.MaxValue;
            float bestCursorDelta = float.MaxValue;
            float bestStraightPenalty = float.MaxValue;
            float bestX = curX;
            float bestY = curY;
            uint bestLeader = 0;

            foreach (var p in pts)
            {
                int leaderHitCount;
                int highTrashHitCount;
                int trashHitCount;
                uint hitLeaderId;

                CountHitsAtPoint(p.x, p.y, leaderCenters, highTrashCenters, trashCenters,
                    out leaderHitCount, out highTrashHitCount, out trashHitCount, out hitLeaderId);

                if (requireLeaderHit && leaderHitCount <= 0) continue;
                if (!requireLeaderHit && requireHighTrashHit && highTrashHitCount <= 0) continue;
                if (!requireLeaderHit && !requireHighTrashHit && leaderHitCount == 0 && highTrashHitCount == 0 && trashHitCount == 0) continue;

                var aimWorld = GetScreenWorld(p.x, p.y);
                if (!IsWithinMaxSnapAngle(mePos, manualWorld, aimWorld, MaxSnapAngleDegrees))
                    continue;

                float safetyPenalty = Math.Max(0f, ComputeLineSafetyPenalty(mePos, aimWorld));
                bool severe = safetyPenalty >= SimplePathRejectThreshold;

                float cursorDelta = Dist(p.x, p.y, curX, curY);
                float straightPenalty = ComputeStraightPathPenalty(mePos, curX, curY, p.x, p.y);

                bool better =
                    (leaderHitCount > bestLeaderHits) ||
                    (leaderHitCount == bestLeaderHits && !severe && bestSafetyPenalty >= SimplePathRejectThreshold) ||
                    (leaderHitCount == bestLeaderHits && safetyPenalty < bestSafetyPenalty - 0.01f) ||
                    (leaderHitCount == bestLeaderHits && Math.Abs(safetyPenalty - bestSafetyPenalty) <= 0.01f && highTrashHitCount > bestHighTrashHits) ||
                    (leaderHitCount == bestLeaderHits && highTrashHitCount == bestHighTrashHits && trashHitCount > bestTrashHits) ||
                    (leaderHitCount == bestLeaderHits && highTrashHitCount == bestHighTrashHits && trashHitCount == bestTrashHits && Math.Abs(safetyPenalty - bestSafetyPenalty) <= 0.01f && straightPenalty < bestStraightPenalty - 0.01f) ||
                    (leaderHitCount == bestLeaderHits && highTrashHitCount == bestHighTrashHits && trashHitCount == bestTrashHits && Math.Abs(safetyPenalty - bestSafetyPenalty) <= 0.01f && Math.Abs(straightPenalty - bestStraightPenalty) <= 0.01f && cursorDelta < bestCursorDelta);

                if (better)
                {
                    bestLeaderHits = leaderHitCount;
                    bestHighTrashHits = highTrashHitCount;
                    bestTrashHits = trashHitCount;
                    bestSafetyPenalty = safetyPenalty;
                    bestCursorDelta = cursorDelta;
                    bestStraightPenalty = straightPenalty;
                    bestX = p.x;
                    bestY = p.y;
                    bestLeader = hitLeaderId;
                }
            }

            if (bestLeaderHits < 0)
                return false;

            if (bestLeader != 0 && bestLeaderHits <= 1)
            {
                for (int i = 0; i < leaderCenters.Count; i++)
                {
                    if (leaderCenters[i].m.AcdId == bestLeader)
                    {
                        aimX = leaderCenters[i].x;
                        aimY = leaderCenters[i].y;
                        primaryLeaderId = bestLeader;
                        return true;
                    }
                }
            }

            aimX = bestX;
            aimY = bestY;
            primaryLeaderId = bestLeader;
            return true;
        }

        private List<(IMonster m, float x, float y)> BuildScreenCenters(List<IMonster> monsters)
        {
            var result = new List<(IMonster m, float x, float y)>(monsters.Count);
            for (int i = 0; i < monsters.Count; i++)
            {
                float x;
                float y;
                if (TryGetScreen(monsters[i], out x, out y))
                    result.Add((monsters[i], x, y));
            }
            return result;
        }

        private bool TryLinearTrashDensityAim(
            IWorldCoordinate mePos,
            IWorldCoordinate cursorWorld,
            bool useCone,
            float vx,
            float vy,
            float cosTh,
            List<IMonster> localHighTrash,
            List<IMonster> localTrash,
            float curX,
            float curY,
            out float aimX,
            out float aimY,
            out IWorldCoordinate markerWorld)
        {
            aimX = curX;
            aimY = curY;
            markerWorld = cursorWorld;

            try
            {
                if (mePos == null || cursorWorld == null || Hud?.Game?.AliveMonsters == null)
                    return false;

                float maxReach = LinearTrashMaxYards;
                if (maxReach <= 0f) maxReach = ForwardEliteScanMaxYards;
                if (MaxPlayerDistanceYards > 0) maxReach = Math.Min(maxReach, MaxPlayerDistanceYards);

                float laneWidth = LinearTrashLaneHalfWidthYards;
                if (laneWidth < 2.5f) laneWidth = 2.5f;
                if (laneWidth > 12f) laneWidth = 12f;

                float maxAngle = LinearTrashMaxAngleDegrees;
                if (maxAngle < 8f) maxAngle = 8f;
                if (maxAngle > MaxSnapAngleDegrees) maxAngle = MaxSnapAngleDegrees;

                int bestCircularBodies = EstimateBestCircularTrashBodies(localHighTrash, localTrash);
                int minBodies = LinearTrashMinBodies < 2 ? 2 : LinearTrashMinBodies;
                int preferWithin = LinearTrashPreferWithinBodies < 0 ? 0 : LinearTrashPreferWithinBodies;

                IMonster best = null;
                int bestBodies = 0;
                float bestProgression = 0f;
                float bestScore = float.MinValue;
                float bestSafety = float.MaxValue;
                float bestProj = 0f;

                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (!IsPullableTrashAllowed(m)) continue;
                    if (m.FloorCoordinate == null) continue;

                    float relX = m.FloorCoordinate.X - mePos.X;
                    float relY = m.FloorCoordinate.Y - mePos.Y;
                    float distToMe = (float)Math.Sqrt(relX * relX + relY * relY);
                    if (distToMe <= 0.1f) continue;
                    if (MinPlayerDistanceYards > 0 && distToMe < MinPlayerDistanceYards) continue;
                    if (maxReach > 0f && distToMe > maxReach) continue;

                    float proj = relX * vx + relY * vy;
                    if (proj <= 2.0f) continue;

                    if (useCone)
                    {
                        float dot = proj / Math.Max(0.001f, distToMe);
                        if (dot < cosTh) continue;
                    }

                    float lateral = Math.Abs(relX * vy - relY * vx);
                    if (lateral > laneWidth * 1.85f) continue;

                    if (!IsWithinMaxSnapAngle(mePos, cursorWorld, m.FloorCoordinate, maxAngle))
                        continue;

                    int bodies;
                    float progression;
                    CountLinearTrashDensity(mePos, m.FloorCoordinate, laneWidth, out bodies, out progression);
                    if (bodies < minBodies) continue;

                    // If a circular clump is much larger, let the existing circular solver handle it.
                    // If the linear lane is close in body count, prefer the straight safer spear line.
                    if (bestCircularBodies > 0 && bodies + preferWithin < bestCircularBodies)
                        continue;

                    float safety = Math.Max(0f, ComputeLineSafetyPenalty(mePos, m.FloorCoordinate));
                    if (safety >= SimplePathRejectThreshold)
                        continue;

                    float cursorWorldDist = m.FloorCoordinate.XYDistanceTo(cursorWorld);
                    float score = (bodies * 100.0f)
                                  + (progression * 18.0f)
                                  + (proj * 0.75f)
                                  - (lateral * 7.0f)
                                  - (cursorWorldDist * 1.15f)
                                  - (safety * 18.0f);

                    bool better =
                        (score > bestScore + 0.01f) ||
                        (Math.Abs(score - bestScore) <= 0.01f && bodies > bestBodies) ||
                        (Math.Abs(score - bestScore) <= 0.01f && bodies == bestBodies && safety < bestSafety - 0.01f) ||
                        (Math.Abs(score - bestScore) <= 0.01f && bodies == bestBodies && Math.Abs(safety - bestSafety) <= 0.01f && proj > bestProj);

                    if (better)
                    {
                        best = m;
                        bestBodies = bodies;
                        bestProgression = progression;
                        bestScore = score;
                        bestSafety = safety;
                        bestProj = proj;
                    }
                }

                if (best == null)
                    return false;

                float sx;
                float sy;
                if (!TryGetScreen(best, out sx, out sy))
                    return false;

                aimX = sx;
                aimY = sy;
                markerWorld = best.FloorCoordinate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int EstimateBestCircularTrashBodies(List<IMonster> localHighTrash, List<IMonster> localTrash)
        {
            int best = 0;
            try
            {
                if (localHighTrash != null)
                    foreach (var m in localHighTrash)
                        if (m != null) best = Math.Max(best, GetTrashDensityScore(m));

                if (localTrash != null)
                    foreach (var m in localTrash)
                        if (m != null) best = Math.Max(best, GetTrashDensityScore(m));
            }
            catch { }
            return best;
        }

        private void CountLinearTrashDensity(IWorldCoordinate mePos, IWorldCoordinate anchor, float laneWidth, out int bodies, out float progression)
        {
            bodies = 0;
            progression = 0f;

            try
            {
                if (mePos == null || anchor == null || Hud?.Game?.AliveMonsters == null)
                    return;

                float ax = anchor.X - mePos.X;
                float ay = anchor.Y - mePos.Y;
                float alen = (float)Math.Sqrt(ax * ax + ay * ay);
                if (alen <= 0.001f) return;

                float nx = ax / alen;
                float ny = ay / alen;

                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (!IsPullableTrashAllowed(m)) continue;
                    if (m.FloorCoordinate == null) continue;

                    float rx = m.FloorCoordinate.X - mePos.X;
                    float ry = m.FloorCoordinate.Y - mePos.Y;
                    float proj = rx * nx + ry * ny;
                    if (proj < 1.5f || proj > alen + 4f) continue;

                    float lateral = Math.Abs(rx * ny - ry * nx);
                    if (lateral > laneWidth) continue;

                    bodies++;

                    try
                    {
                        var sm = m.SnoMonster;
                        if (sm != null && sm.RiftProgression > 0f)
                            progression += sm.RiftProgression;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private List<(float x, float y)> BuildLocalCandidatePoints(
            List<(IMonster m, float x, float y)> leaders,
            List<(IMonster m, float x, float y)> highTrash,
            List<(IMonster m, float x, float y)> trash,
            float curX,
            float curY)
        {
            var pts = new List<(float x, float y)>(80) { (curX, curY) };

            foreach (var c in leaders) pts.Add((c.x, c.y));
            foreach (var c in highTrash) if (pts.Count < 44) pts.Add((c.x, c.y));
            foreach (var c in trash) if (pts.Count < 60) pts.Add((c.x, c.y));

            float sx = 0f;
            float sy = 0f;
            int n = 0;

            foreach (var c in leaders) { sx += c.x; sy += c.y; n++; }
            foreach (var c in highTrash) { sx += c.x; sy += c.y; n++; }
            foreach (var c in trash) { sx += c.x; sy += c.y; n++; }

            if (n > 0)
                pts.Add((sx / n, sy / n));

            for (int i = 0; i < leaders.Count; i++)
                for (int j = i + 1; j < leaders.Count; j++)
                    pts.Add(((leaders[i].x + leaders[j].x) * 0.5f, (leaders[i].y + leaders[j].y) * 0.5f));

            return pts;
        }

        private void CountHitsAtPoint(
            float x,
            float y,
            List<(IMonster m, float x, float y)> leaders,
            List<(IMonster m, float x, float y)> highTrash,
            List<(IMonster m, float x, float y)> trash,
            out int leaderHitCount,
            out int highTrashHitCount,
            out int trashHitCount,
            out uint leaderId)
        {
            leaderHitCount = 0;
            highTrashHitCount = 0;
            trashHitCount = 0;
            leaderId = 0;

            float bestLeaderDist = float.MaxValue;

            foreach (var c in leaders)
            {
                float d = Dist(x, y, c.x, c.y);
                if (d <= HitboxRadiusPx)
                {
                    leaderHitCount++;
                    if (d < bestLeaderDist)
                    {
                        bestLeaderDist = d;
                        leaderId = c.m.AcdId;
                    }
                }
            }

            foreach (var c in highTrash)
                if (Dist(x, y, c.x, c.y) <= HitboxRadiusPx) highTrashHitCount++;

            foreach (var c in trash)
                if (Dist(x, y, c.x, c.y) <= HitboxRadiusPx) trashHitCount++;
        }

        private float ComputeStraightPathPenalty(IWorldCoordinate mePos, float curX, float curY, float aimX, float aimY)
        {
            try
            {
                if (mePos == null) return 0f;
                var meScreen = mePos.ToScreenCoordinate();

                float axisX = curX - meScreen.X;
                float axisY = curY - meScreen.Y;
                float axisLen = (float)Math.Sqrt(axisX * axisX + axisY * axisY);
                if (axisLen <= 0.001f) return 0f;

                axisX /= axisLen;
                axisY /= axisLen;

                float offX = aimX - meScreen.X;
                float offY = aimY - meScreen.Y;
                float proj = offX * axisX + offY * axisY;
                float rejX = offX - proj * axisX;
                float rejY = offY - proj * axisY;
                float lateralOffset = (float)Math.Sqrt(rejX * rejX + rejY * rejY);
                return lateralOffset * StraightPathPenaltyScale;
            }
            catch
            {
                return 0f;
            }
        }

        // =========================
        // VALIDITY + PRIORITY
        // =========================

        private static bool IsLeaderElite(IMonster m)
        {
            if (m == null) return false;
            return m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Rare;
        }

        private static bool IsEliteMinion(IMonster m)
        {
            if (m == null) return false;
            return m.Rarity == ActorRarity.RareMinion;
        }

        private bool IsEliteBlocked(IMonster m)
        {
            if (m == null) return false;
            if (IgnoreJuggernaut && HasAffix(m, MonsterAffix.Juggernaut)) return true;
            if (IgnoreShieldingWhenActive && HasAffix(m, MonsterAffix.Shielding) && IsShieldingActive(m)) return true;
            return false;
        }

        private bool IsEliteHardInvalidForPull(IMonster m)
        {
            if (m == null || !m.IsElite) return true;
            if (IsEliteBlocked(m)) return true;
            return !IsEliteAllowed(m);
        }

        private bool IsEliteAllowed(IMonster m)
        {
            if (m == null) return false;
            if (!IsLeaderElite(m)) return false;
            if (!m.IsAlive || m.Hidden || m.Invisible || m.Stealthed || m.Invulnerable || m.Untargetable) return false;
            if (IgnoreIllusions && m.Illusion) return false;
            if (IgnoreKnockbackImmune && IsNativeKnockbackImmune(m)) return false;
            if (IgnoreKnownUnpullables && IsKnownUnpullableFamily(m)) return false;
            return true;
        }

        private bool IsPullableTrashAllowed(IMonster m)
        {
            if (m == null || !m.IsAlive) return false;
            if (m.IsElite && !IsEliteMinion(m)) return false;
            if (m.Hidden || m.Invisible || m.Stealthed || m.Invulnerable || m.Untargetable) return false;
            if (IgnoreIllusions && m.Illusion) return false;
            if (IgnoreKnockbackImmune && IsNativeKnockbackImmune(m)) return false;
            if (IgnoreKnownUnpullables && IsKnownUnpullableFamily(m)) return false;
            return true;
        }

        private bool IsHighValueTrash(IMonster m)
        {
            if (!PreferHighValueTrash || m == null || m.IsElite) return false;

            try
            {
                var sm = m.SnoMonster;
                if (sm == null) return false;

                if (sm.RiftProgression >= HighValueTrashMinRiftProgression)
                    return true;

                if (!PreferNamedBigTrash) return false;

                var name = sm.NameEnglish ?? sm.Code ?? sm.ToString();
                return ContainsAny(name,
                    "Unburied",
                    "Grotesque",
                    "Oppressor",
                    "Anarch",
                    "Savage Beast",
                    "Bloated",
                    "Exorcist",
                    "Bogan",
                    "Spider");
            }
            catch
            {
                return false;
            }
        }

        private float GetHighValueTrashScore(IMonster m)
        {
            if (m == null) return 0f;

            float score = 0f;

            try
            {
                score += GetNearbyTrashRiftProgressionScore(m) * 10f;
            }
            catch { }

            try
            {
                var sm = m.SnoMonster;
                if (sm != null && sm.RiftProgression > 0f)
                    score += sm.RiftProgression * 100f;
            }
            catch { }

            if (PreferNamedBigTrash)
            {
                try
                {
                    var sm = m.SnoMonster;
                    var name = sm?.NameEnglish ?? sm?.Code ?? sm?.ToString();
                    if (!string.IsNullOrEmpty(name) && ContainsAny(name,
                        "Unburied",
                        "Grotesque",
                        "Oppressor",
                        "Anarch",
                        "Savage Beast",
                        "Bloated",
                        "Exorcist",
                        "Bogan",
                        "Spider"))
                    {
                        score += 25f;
                    }
                }
                catch { }
            }

            score += GetTrashDensityScore(m) * 2f;
            return score;
        }

        private float GetNearbyTrashRiftProgressionScore(IMonster center)
        {
            if (center == null || center.FloorCoordinate == null)
                return 0f;

            float score = 0f;

            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.AliveMonsters == null)
                    return 0f;

                foreach (var x in Hud.Game.AliveMonsters)
                {
                    if (!IsPullableTrashAllowed(x))
                        continue;

                    if (x.FloorCoordinate == null)
                        continue;

                    if (x.FloorCoordinate.XYDistanceTo(center.FloorCoordinate) > TrashClusterRadiusYards)
                        continue;

                    try
                    {
                        var sm = x.SnoMonster;
                        if (sm != null && sm.RiftProgression > 0f)
                            score += sm.RiftProgression;
                    }
                    catch { }
                }
            }
            catch { }

            return score;
        }

        private int GetTrashDensityScore(IMonster m)
        {
            if (m == null || m.FloorCoordinate == null)
                return 0;

            int density = 0;

            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.AliveMonsters == null)
                    return 0;

                foreach (var x in Hud.Game.AliveMonsters)
                {
                    if (!IsPullableTrashAllowed(x))
                        continue;

                    if (x.FloorCoordinate == null)
                        continue;

                    if (x.FloorCoordinate.XYDistanceTo(m.FloorCoordinate) <= TrashClusterRadiusYards)
                        density++;
                }
            }
            catch { }

            return density;
        }

        private bool HasNearbyValidEliteAlternative(IMonster nearestElite, List<IMonster> leaders, float maxScreenDistancePx)
        {
            if (nearestElite == null || maxScreenDistancePx <= 0f) return false;

            float ex;
            float ey;
            if (!TryGetScreen(nearestElite, out ex, out ey)) return false;

            foreach (var m in leaders)
            {
                if (m == null || ReferenceEquals(m, nearestElite)) continue;
                float sx;
                float sy;
                if (!TryGetScreen(m, out sx, out sy)) continue;
                if (Dist(ex, ey, sx, sy) <= maxScreenDistancePx) return true;
            }

            return false;
        }

        // =========================
        // GEOMETRY TIEBREAKS
        // =========================

        private bool IsWithinMaxSnapAngle(IWorldCoordinate mePos, IWorldCoordinate cursorWorld, IWorldCoordinate aimWorld, float maxAngleDegrees)
        {
            try
            {
                if (mePos == null || cursorWorld == null || aimWorld == null) return true;

                float manualX = cursorWorld.X - mePos.X;
                float manualY = cursorWorld.Y - mePos.Y;
                float aimX = aimWorld.X - mePos.X;
                float aimY = aimWorld.Y - mePos.Y;

                float manualLen = (float)Math.Sqrt(manualX * manualX + manualY * manualY);
                float aimLen = (float)Math.Sqrt(aimX * aimX + aimY * aimY);
                if (manualLen <= 0.001f || aimLen <= 0.001f) return true;

                float dot = (manualX / manualLen) * (aimX / aimLen) + (manualY / manualLen) * (aimY / aimLen);
                if (dot > 1f) dot = 1f;
                if (dot < -1f) dot = -1f;
                return dot >= (float)Math.Cos(maxAngleDegrees * Math.PI / 180.0);
            }
            catch
            {
                return true;
            }
        }

        private float ComputeLineSafetyPenalty(IWorldCoordinate fromWorld, IWorldCoordinate toWorld)
        {
            if (!UseGeometrySafety || fromWorld == null || toWorld == null || Hud?.Game == null) return 0f;

            float penalty = 0f;
            penalty += AccumulateBlockerPenalty(SafeGetGameEnumerable("Obstacles"), fromWorld, toWorld, ObstaclePenaltyWeight);
            penalty += AccumulateBlockerPenalty(SafeGetSupplementalBlockerActors(), fromWorld, toWorld, ObstaclePenaltyWeight * 0.85f);
            penalty += AccumulateBlockerPenalty(SafeGetEliteWallEffects(), fromWorld, toWorld, EliteWallPenaltyWeight);
            penalty += AccumulateBlockerPenalty(SafeGetWallerBlockers(), fromWorld, toWorld, EliteWallPenaltyWeight * 1.20f);
            penalty += AccumulateBlockerPenalty(SafeGetPylonBlockers(), fromWorld, toWorld, EliteWallPenaltyWeight * 0.95f);
            penalty += AccumulateBlockerPenalty(SafeGetProjectileBlockingActors(), fromWorld, toWorld, ObstaclePenaltyWeight * 1.10f);
            penalty += AccumulateUnsafeImpactPenalty(fromWorld, toWorld, SpearImpactUnsafePenaltyWeight);

            float openingBonus = ComputeDoorCenterLaneBonus(fromWorld, toWorld) + ComputeCorridorCenterLaneBonus(fromWorld, toWorld);
            penalty -= Math.Min(5.5f, openingBonus);

            return penalty < 0f ? 0f : penalty;
        }

        private float ComputeHardBlockerPenalty(IWorldCoordinate fromWorld, IWorldCoordinate toWorld)
        {
            if (!UseGeometrySafety || fromWorld == null || toWorld == null || Hud?.Game == null) return 0f;

            float penalty = 0f;
            penalty += AccumulateBlockerPenalty(SafeGetPylonBlockers(), fromWorld, toWorld, EliteWallPenaltyWeight * 1.15f);
            penalty += AccumulateBlockerPenalty(SafeGetWallerBlockers(), fromWorld, toWorld, EliteWallPenaltyWeight * 1.35f);
            penalty += AccumulateBlockerPenalty(SafeGetProjectileBlockingActors(), fromWorld, toWorld, ObstaclePenaltyWeight * 1.25f);
            penalty += AccumulateUnsafeImpactPenalty(fromWorld, toWorld, SpearImpactUnsafePenaltyWeight * 1.15f);
            return penalty < 0f ? 0f : penalty;
        }

        private float AccumulateBlockerPenalty(IEnumerable collection, IWorldCoordinate fromWorld, IWorldCoordinate toWorld, float weight)
        {
            if (collection == null || fromWorld == null || toWorld == null) return 0f;

            float penalty = 0f;
            float ax = fromWorld.X;
            float ay = fromWorld.Y;
            float bx = toWorld.X;
            float by = toWorld.Y;
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 <= 0.001f) return 0f;

            foreach (var o in collection)
            {
                if (o == null) continue;

                float ox;
                float oy;
                float radius;
                if (!TryGetBlockerWorldCircle(o, out ox, out oy, out radius)) continue;

                float tproj = ((ox - ax) * dx + (oy - ay) * dy) / len2;
                if (tproj < 0f) tproj = 0f;
                if (tproj > 1f) tproj = 1f;

                float px = ax + tproj * dx;
                float py = ay + tproj * dy;
                float ddx = ox - px;
                float ddy = oy - py;
                float dist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);

                float safe = radius + BlockerSafetyMarginYards;
                if (dist >= safe) continue;

                float overlap = safe - dist;
                float alongFactor = 0.75f + (0.65f * tproj);
                float hardFactor = 1.0f;
                if (dist < Math.Max(0.60f, radius * 0.70f)) hardFactor = 2.20f;
                else if (dist < Math.Max(1.00f, radius * 0.95f)) hardFactor = 1.35f;

                penalty += weight * overlap * alongFactor * hardFactor;
                if (dist < Math.Max(0.35f, radius * 0.45f))
                    penalty += weight * 4.0f;
            }

            return penalty;
        }

        private float ComputeDoorCenterLaneBonus(IWorldCoordinate fromWorld, IWorldCoordinate toWorld)
        {
            if (Hud?.Game?.Doors == null || fromWorld == null || toWorld == null) return 0f;

            float bonus = 0f;
            float ax = fromWorld.X;
            float ay = fromWorld.Y;
            float bx = toWorld.X;
            float by = toWorld.Y;
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 <= 0.001f) return 0f;

            foreach (var door in Hud.Game.Doors)
            {
                if (door == null) continue;

                float ox;
                float oy;
                float radius;
                if (!TryGetBlockerWorldCircle(door, out ox, out oy, out radius)) continue;

                float tproj = ((ox - ax) * dx + (oy - ay) * dy) / len2;
                if (tproj < 0.08f || tproj > 0.95f) continue;

                float px = ax + tproj * dx;
                float py = ay + tproj * dy;
                float ddx = ox - px;
                float ddy = oy - py;
                float dist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                float lane = Math.Max(2.4f, radius + 1.8f);
                if (dist <= lane)
                    bonus += (lane - dist) * 1.15f;
            }

            return Math.Min(3.0f, bonus);
        }

        private float ComputeCorridorCenterLaneBonus(IWorldCoordinate fromWorld, IWorldCoordinate toWorld)
        {
            if (Hud?.Game == null || fromWorld == null || toWorld == null) return 0f;

            float ax = fromWorld.X;
            float ay = fromWorld.Y;
            float bx = toWorld.X;
            float by = toWorld.Y;
            float dx = bx - ax;
            float dy = by - ay;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0.001f) return 0f;

            float nx = dx / len;
            float ny = dy / len;
            float lx = -ny;
            float ly = nx;
            float[] samples = { 0.25f, 0.50f, 0.75f };

            float total = 0f;
            foreach (var tt in samples)
            {
                float sx = ax + dx * tt;
                float sy = ay + dy * tt;
                float nearestLeft = float.MaxValue;
                float nearestRight = float.MaxValue;

                AccumulateCorridorSides(SafeGetGameEnumerable("Obstacles"), sx, sy, nx, ny, lx, ly, ref nearestLeft, ref nearestRight);
                AccumulateCorridorSides(SafeGetEliteWallEffects(), sx, sy, nx, ny, lx, ly, ref nearestLeft, ref nearestRight);

                if (nearestLeft < float.MaxValue && nearestRight < float.MaxValue)
                {
                    float width = nearestLeft + nearestRight;
                    if (width <= 12.0f)
                    {
                        float centered = Math.Max(0f, 2.5f - Math.Abs(nearestLeft - nearestRight));
                        float narrow = Math.Max(0f, 10.0f - width);
                        total += centered * 0.55f + narrow * 0.10f;
                    }
                }
            }

            return Math.Min(3.0f, total);
        }

        private void AccumulateCorridorSides(IEnumerable collection, float sx, float sy, float nx, float ny, float lx, float ly, ref float nearestLeft, ref float nearestRight)
        {
            if (collection == null) return;

            foreach (var o in collection)
            {
                if (o == null) continue;

                float ox;
                float oy;
                float radius;
                if (!TryGetBlockerWorldCircle(o, out ox, out oy, out radius)) continue;

                float rx = ox - sx;
                float ry = oy - sy;
                float along = rx * nx + ry * ny;
                if (Math.Abs(along) > 3.5f) continue;

                float lateral = rx * lx + ry * ly;
                float edgeDist = Math.Abs(lateral) - radius;
                if (edgeDist < 0f) edgeDist = 0f;
                if (edgeDist > 6.0f) continue;

                if (lateral < 0f)
                {
                    if (edgeDist < nearestLeft) nearestLeft = edgeDist;
                }
                else
                {
                    if (edgeDist < nearestRight) nearestRight = edgeDist;
                }
            }
        }

        private IEnumerable SafeGetGameEnumerable(string propertyName)
        {
            try
            {
                if (Hud == null || Hud.Game == null || string.IsNullOrWhiteSpace(propertyName))
                    return null;

                var prop = Hud.Game.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop == null)
                    return null;

                return prop.GetValue(Hud.Game, null) as IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private IEnumerable SafeGetSupplementalBlockerActors()
        {
            var list = new List<BlockerCircle>(32);
            try
            {
                if (Hud?.Game?.Actors == null) return list;
                foreach (var a in Hud.Game.Actors)
                {
                    if (a == null || a.FloorCoordinate == null) continue;
                    if (a.CentralXyDistanceToMe > 145.0) continue;

                    var g = a.GizmoType;
                    if (g != GizmoType.BreakableDoor &&
                        g != GizmoType.Gate &&
                        g != GizmoType.DestroyableObject &&
                        g != GizmoType.ReformingDestroyableObject)
                        continue;

                    if (a.IsDisabled || a.IsOperated) continue;
                    list.Add(MakeBlockerCircle(a, Math.Max(1.4f, a.RadiusScaled * 1.15f)));
                }
            }
            catch { }
            return list;
        }

        private IEnumerable SafeGetPylonBlockers()
        {
            var list = new List<BlockerCircle>(8);
            if (!IncludePylonsAsPullBlockers) return list;
            try
            {
                if (Hud?.Game?.Shrines == null) return list;
                foreach (var shrine in Hud.Game.Shrines)
                {
                    if (shrine == null || !shrine.IsPylon || shrine.FloorCoordinate == null) continue;
                    // Pylons block pulls, but we often stand on/near them, so keep the radius restrained.
                    list.Add(new BlockerCircle { X = shrine.FloorCoordinate.X, Y = shrine.FloorCoordinate.Y, Radius = 2.35f });
                }
            }
            catch { }
            return list;
        }

        private IEnumerable SafeGetWallerBlockers()
        {
            var list = new List<BlockerCircle>(24);
            if (!IncludeWallerAsPullBlockers) return list;
            try
            {
                if (Hud?.Game?.Actors == null) return list;
                foreach (var a in Hud.Game.Actors)
                {
                    if (a == null || a.FloorCoordinate == null || a.SnoActor == null) continue;
                    if (a.CentralXyDistanceToMe > 145.0) continue;

                    uint sno = (uint)a.SnoActor.Sno;
                    bool waller = sno == 226296u || sno == 226808u || sno == 445916u;
                    if (!waller)
                    {
                        string code = a.SnoActor.Code ?? a.SnoActor.NameEnglish ?? a.SnoActor.NameLocalized;
                        waller = !string.IsNullOrEmpty(code) && code.ToLowerInvariant().Contains("waller");
                    }
                    if (!waller) continue;

                    var cc = a.CollisionCoordinate ?? a.FloorCoordinate;
                    if (cc == null) continue;
                    float radius = Math.Max(2.0f, a.RadiusScaled * 1.35f);
                    if (sno == 226808u || sno == 226296u) radius = Math.Max(radius, 3.25f);
                    list.Add(new BlockerCircle { X = cc.X, Y = cc.Y, Radius = radius });
                }
            }
            catch { }
            return list;
        }

        private IEnumerable SafeGetProjectileBlockingActors()
        {
            var list = new List<BlockerCircle>(32);
            if (!IncludeProjectileBlockingActors) return list;
            try
            {
                if (Hud?.Game?.Actors == null || Hud?.Sno?.Attributes?.Blocks_Projectiles == null) return list;
                foreach (var a in Hud.Game.Actors)
                {
                    if (a == null || a.FloorCoordinate == null) continue;
                    if (a.CentralXyDistanceToMe > 145.0) continue;
                    int blocks = a.GetAttributeValueAsInt(Hud.Sno.Attributes.Blocks_Projectiles, 0, 0);
                    if (blocks <= 0) continue;
                    if (a.IsDisabled || a.IsOperated) continue;
                    list.Add(MakeBlockerCircle(a, Math.Max(1.6f, a.RadiusScaled * 1.20f)));
                }
            }
            catch { }
            return list;
        }

        private BlockerCircle MakeBlockerCircle(IActor actor, float radius)
        {
            var cc = actor.CollisionCoordinate ?? actor.FloorCoordinate;
            return new BlockerCircle
            {
                X = cc.X,
                Y = cc.Y,
                Radius = Math.Max(0.9f, radius),
            };
        }

        private IEnumerable SafeGetEliteWallEffects()
        {
            try
            {
                if (Hud == null || Hud.Game == null)
                    return null;

                var actorQueryProp = Hud.Game.GetType().GetProperty(
                    "ActorQuery",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                object actorQuery = actorQueryProp != null
                    ? actorQueryProp.GetValue(Hud.Game, null)
                    : null;

                if (actorQuery == null)
                    return null;

                var method = actorQuery.GetType().GetMethod(
                    "GetSkillEffectActors",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(SkillEffectType) },
                    null);

                if (method == null)
                    return null;

                return method.Invoke(actorQuery, new object[] { SkillEffectType.elite_wall }) as IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetBlockerWorldCircle(object o, out float ox, out float oy, out float radius)
        {
            ox = 0f;
            oy = 0f;
            radius = 1.5f;
            if (o == null) return false;

            try
            {
                var circle = o as BlockerCircle;
                if (circle != null)
                {
                    ox = circle.X;
                    oy = circle.Y;
                    radius = Math.Max(radius, circle.Radius);
                    return true;
                }

                var actor = o as IActor;
                if (actor != null)
                {
                    var cc = actor.CollisionCoordinate ?? actor.FloorCoordinate;
                    if (cc == null) return false;
                    ox = cc.X;
                    oy = cc.Y;
                    radius = Math.Max(radius, actor.RadiusScaled * 1.20f);
                    return true;
                }

                var t = o.GetType();
                object ccObj = t.GetProperty("CollisionCoordinate")?.GetValue(o, null) ?? t.GetProperty("FloorCoordinate")?.GetValue(o, null);
                if (ccObj == null) return false;

                ox = Convert.ToSingle(ccObj.GetType().GetProperty("X")?.GetValue(ccObj, null));
                oy = Convert.ToSingle(ccObj.GetType().GetProperty("Y")?.GetValue(ccObj, null));

                var rp = t.GetProperty("RadiusScaled");
                if (rp != null)
                    radius = Math.Max(radius, Convert.ToSingle(rp.GetValue(o, null)) * 1.20f);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =========================
        // MINIMAL SPEAR IMPACT MEMORY
        // =========================

        private void BeginSpearImpactProbe(int tick, IWorldCoordinate fromWorld, IWorldCoordinate targetWorld, uint targetAcdId)
        {
            try
            {
                _spearImpactProbe.Active = false;
                if (!EnableSpearImpactMemory || fromWorld == null || targetWorld == null) return;

                float dx = targetWorld.X - fromWorld.X;
                float dy = targetWorld.Y - fromWorld.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist < 18f) return;

                int windowTicks = MsToTicks(SpearImpactProbeWindowMs);
                _spearImpactProbe = new SpearImpactProbe
                {
                    Active = true,
                    CastTick = tick,
                    ExpireTick = tick + windowTicks,
                    FromX = fromWorld.X,
                    FromY = fromWorld.Y,
                    ToX = targetWorld.X,
                    ToY = targetWorld.Y,
                    DirX = dx / dist,
                    DirY = dy / dist,
                    Distance = dist,
                    TargetAcdId = targetAcdId,
                };
            }
            catch
            {
                _spearImpactProbe.Active = false;
            }
        }

        private void UpdateSpearImpactProbe(int tick)
        {
            try
            {
                if (!EnableSpearImpactMemory || !_spearImpactProbe.Active) return;
                if (tick > _spearImpactProbe.ExpireTick)
                {
                    _spearImpactProbe.Active = false;
                    return;
                }

                if (Hud?.Game?.Actors == null) return;

                foreach (var actor in Hud.Game.Actors)
                {
                    if (actor == null || actor.FloorCoordinate == null || actor.SnoActor == null) continue;
                    if (actor.CreatedAtInGameTick < _spearImpactProbe.CastTick - 2) continue;
                    if (!IsStrongAncientSpearImpactActor(actor)) continue;

                    float ix = actor.FloorCoordinate.X;
                    float iy = actor.FloorCoordinate.Y;
                    float rx = ix - _spearImpactProbe.FromX;
                    float ry = iy - _spearImpactProbe.FromY;
                    float proj = rx * _spearImpactProbe.DirX + ry * _spearImpactProbe.DirY;
                    if (proj < 6f) continue;
                    if (proj > _spearImpactProbe.Distance - SpearImpactMinShortfallYards) continue;

                    float lateral = Math.Abs(rx * _spearImpactProbe.DirY - ry * _spearImpactProbe.DirX);
                    if (lateral > 7.5f) continue;

                    if (HasPullableMonsterNear(ix, iy, SpearImpactUnsafeRadiusYards + 2.0f))
                        continue;

                    AddUnsafeImpactSample(ix, iy, tick);
                    _spearImpactProbe.Active = false;
                    return;
                }
            }
            catch
            {
                _spearImpactProbe.Active = false;
            }
        }

        private bool IsStrongAncientSpearImpactActor(IActor actor)
        {
            try
            {
                uint sno = (uint)actor.SnoActor.Sno;
                // Confirmed wall/collision visual from F11 debug: x1_Barbarian_AncientSpear_End_Explode = 365342.
                return sno == 365342u || sno == 365789u || sno == 365534u;
            }
            catch
            {
                return false;
            }
        }

        private bool HasPullableMonsterNear(float x, float y, float yards)
        {
            try
            {
                if (Hud?.Game?.AliveMonsters == null) return false;
                float r2 = yards * yards;
                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || m.FloorCoordinate == null) continue;
                    if (m.IsElite ? !IsEliteAllowed(m) : !IsPullableTrashAllowed(m)) continue;
                    float dx = m.FloorCoordinate.X - x;
                    float dy = m.FloorCoordinate.Y - y;
                    if ((dx * dx + dy * dy) <= r2) return true;
                }
            }
            catch { }
            return false;
        }

        private void AddUnsafeImpactSample(float x, float y, int tick)
        {
            try
            {
                int expire = tick + MsToTicks(SpearImpactMemoryMs);
                for (int i = _unsafeImpactSamples.Count - 1; i >= 0; --i)
                {
                    var s = _unsafeImpactSamples[i];
                    float dx = s.X - x;
                    float dy = s.Y - y;
                    if ((dx * dx + dy * dy) <= 9.0f)
                    {
                        _unsafeImpactSamples[i] = new UnsafeImpactSample { X = x, Y = y, ExpireTick = expire };
                        return;
                    }
                }

                if (_unsafeImpactSamples.Count >= 12)
                    _unsafeImpactSamples.RemoveAt(0);

                _unsafeImpactSamples.Add(new UnsafeImpactSample { X = x, Y = y, ExpireTick = expire });
            }
            catch { }
        }

        private void PruneUnsafeImpactSamples(int tick)
        {
            try
            {
                for (int i = _unsafeImpactSamples.Count - 1; i >= 0; --i)
                {
                    if (_unsafeImpactSamples[i].ExpireTick < tick)
                        _unsafeImpactSamples.RemoveAt(i);
                }
            }
            catch { }
        }

        private float AccumulateUnsafeImpactPenalty(IWorldCoordinate fromWorld, IWorldCoordinate toWorld, float weight)
        {
            if (!EnableSpearImpactMemory || fromWorld == null || toWorld == null || _unsafeImpactSamples.Count == 0) return 0f;

            float ax = fromWorld.X;
            float ay = fromWorld.Y;
            float bx = toWorld.X;
            float by = toWorld.Y;
            float dx = bx - ax;
            float dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 <= 0.001f) return 0f;

            float penalty = 0f;
            float safe = Math.Max(2.0f, SpearImpactUnsafeRadiusYards);
            foreach (var sample in _unsafeImpactSamples)
            {
                float tproj = ((sample.X - ax) * dx + (sample.Y - ay) * dy) / len2;
                if (tproj < 0.03f || tproj > 0.96f) continue;

                float px = ax + tproj * dx;
                float py = ay + tproj * dy;
                float ddx = sample.X - px;
                float ddy = sample.Y - py;
                float dist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dist >= safe) continue;

                penalty += weight * (safe - dist) * (0.85f + tproj * 0.45f);
            }
            return penalty;
        }

        private int MsToTicks(int ms)
        {
            if (ms <= 0) return 1;
            int ticks = (int)Math.Ceiling(ms / 16.0);
            return ClampInt(ticks, 1, 600);
        }

        // =========================
        // INPUT + MARKERS
        // =========================

        private uint FindMarkerTargetAcdId(IWorldCoordinate markerWorld, float aimX, float aimY, bool failed)
        {
            try
            {
                if (Hud?.Game?.AliveMonsters == null) return 0;

                IMonster best = null;
                float bestScore = float.MaxValue;
                float worldSlack = Math.Max(MarkerCircleRadiusYards, CursorWorldRadiusYards) + 8f;

                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || m.FloorCoordinate == null) continue;

                    bool candidate = failed
                        ? m.IsElite
                        : (m.IsElite ? IsEliteAllowed(m) : IsPullableTrashAllowed(m));
                    if (!candidate) continue;

                    float sx;
                    float sy;
                    if (!TryGetScreen(m, out sx, out sy)) continue;

                    float screenDist = Dist(sx, sy, aimX, aimY);
                    float worldDist = markerWorld != null ? m.FloorCoordinate.XYDistanceTo(markerWorld) : float.MaxValue;

                    if (markerWorld != null && worldDist > worldSlack && screenDist > HitboxRadiusPx * 1.5f)
                        continue;

                    float score = (worldDist * 20f) + screenDist;
                    if (m.IsElite && IsLeaderElite(m)) score -= 5f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = m;
                    }
                }

                return best != null ? (uint)best.AcdId : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void TryUpdateTrackedMarkerWorld(uint acdId)
        {
            try
            {
                if (acdId == 0 || Hud?.Game?.AliveMonsters == null) return;

                foreach (var m in Hud.Game.AliveMonsters)
                {
                    if (m == null || !m.IsAlive || m.FloorCoordinate == null) continue;
                    if ((uint)m.AcdId != acdId) continue;
                    _markerWorld = m.FloorCoordinate;
                    return;
                }
            }
            catch { }
        }

        private void TapSpear()
        {
            TapAction(_spearKey);
        }

        private void TapAction(ActionKey key)
        {
            try
            {
                switch (key)
                {
                    case ActionKey.Skill1:
                        TapKeyboardKey(Keys.D1);
                        return;

                    case ActionKey.Skill2:
                        TapKeyboardKey(Keys.D2);
                        return;

                    case ActionKey.Skill3:
                        TapKeyboardKey(Keys.D3);
                        return;

                    case ActionKey.Skill4:
                        TapKeyboardKey(Keys.D4);
                        return;

                    case ActionKey.LeftSkill:
                        TapMouse(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, AllowAutoShiftFallback);
                        return;

                    case ActionKey.RightSkill:
                        TapMouse(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, AllowAutoShiftFallback);
                        return;
                }
            }
            catch
            {
            }
        }

        private void TapKeyboardKey(Keys key)
        {
            int holdMs = ClampInt(SpearInputHoldMs, 20, 90);
            byte vk = (byte)key;

            try
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                Thread.Sleep(holdMs);
                keybd_event(vk, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero);
            }
            catch
            {
                try { keybd_event(vk, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); }
                catch { }
            }
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void TapMouse(uint downFlag, uint upFlag, bool holdShift)
        {
            bool shiftOwned = false;

            try
            {
                if (holdShift)
                {
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    shiftOwned = true;
                    Thread.Sleep(ClampInt(ShiftPreHoldMs, 0, 25));
                }

                mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(ClampInt(SpearInputHoldMs, 20, 90));
                mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
            }
            catch
            {
            }
            finally
            {
                if (shiftOwned)
                {
                    try { keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP_SIMPLE, UIntPtr.Zero); }
                    catch { }
                }
            }
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (!Enabled || Hud == null) return;
            if (!DrawCastMarker) return;
            if (layer != WorldLayer.Ground) return;
            if (!Hud.Game.IsInGame) return;

            int tick = Hud.Game.CurrentGameTick;
            if (_markerTargetAcdId != 0)
                TryUpdateTrackedMarkerWorld(_markerTargetAcdId);

            if (_markerWorld == null || tick > _markerUntilTick) return;

            float radius = MarkerCircleRadiusYards > 0 ? MarkerCircleRadiusYards : CursorWorldRadiusYards;
            _circleGreen.Radius = radius;
            _circleRed.Radius = radius;

            float phase = (tick - _markerStartTick) / 12f;
            float pulse = (float)(0.5 + 0.5 * Math.Sin(phase * 2.0 * Math.PI));
            float dotRadius = 0.9f + pulse * 1.9f;
            _dotGreen.Radius = dotRadius;
            _dotRed.Radius = dotRadius;

            if (_markerFailed)
            {
                _circleRed.Paint(null, _markerWorld, null);
                _dotRed.Paint(null, _markerWorld, null);
            }
            else
            {
                _circleGreen.Paint(null, _markerWorld, null);
                _dotGreen.Paint(null, _markerWorld, null);
            }
        }

        // =========================
        // RUNTIME HELPERS
        // =========================

        private void ResolveSpearKey()
        {
            _spearKey = ActionKey.Unknown;

            try
            {
                var skills = Hud?.Game?.Me?.Powers?.CurrentSkills;
                if (skills == null)
                    return;

                var spearSno = Hud.Sno.SnoPowers.Barbarian_AncientSpear.Sno;

                foreach (var s in skills)
                {
                    if (s?.SnoPower != null && s.SnoPower.Sno == spearSno)
                    {
                        _spearKey = s.Key;
                        return;
                    }
                }
            }
            catch
            {
                _spearKey = ActionKey.Unknown;
            }
        }

        private bool CanRun()
        {
            if (!Hud.Window.IsForeground) return false;
            if (!Hud.Game.IsInGame || Hud.Game.IsLoading) return false;
            if (Hud.Game.Me == null || Hud.Game.Me.IsDead) return false;
            if (_chatEdit != null && _chatEdit.Visible) return false;
            if (Hud.Inventory != null && Hud.Inventory.InventoryMainUiElement != null && Hud.Inventory.InventoryMainUiElement.Visible) return false;
            return true;
        }

        private void HardCleanup()
        {
            ResetTransientState(false);
        }

        private void ClearPendingCastOnly()
        {
            _castPending = false;
            _castTick = 0;
            _aimX = 0f;
            _aimY = 0f;
            _pendingMarkerWorld = null;
            _pendingMarkerTargetAcdId = 0;
        }

        private void ResetTransientState(bool resetHotkeyState)
        {
            if (resetHotkeyState)
                _hotkeyDownLatched = false;

            _lastObservedGameTick = -1;
            _castingNow = false;

            ClearPendingCastOnly();

            _markerWorld = null;
            _markerTargetAcdId = 0;
            _markerStartTick = 0;
            _markerUntilTick = 0;
            _markerFailed = false;
            ClearEliteCourseCorrectionCache();
            _spearImpactProbe.Active = false;
            if (resetHotkeyState)
                _unsafeImpactSamples.Clear();

            if (resetHotkeyState)
                _spearKey = ActionKey.Unknown;
        }


        private IWorldCoordinate GetScreenWorld(float x, float y)
        {
            try { return Hud.Window.CreateScreenCoordinate(x, y).ToWorldCoordinate(); }
            catch { return null; }
        }

        private void SafeMouseMove(float x, float y)
        {
            float w = 1920f;
            float h = 1080f;
            try { w = Hud.Window.Size.Width; h = Hud.Window.Size.Height; } catch { }
            if (w <= 0) w = 1920f;
            if (h <= 0) h = 1080f;

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x > w - 1) x = w - 1;
            if (y > h - 1) y = h - 1;

            try { SetCursorPos((int)Math.Round(x), (int)Math.Round(y)); }
            catch { }
        }

        private bool IsWithinWindow(float x, float y)
        {
            try
            {
                var w = Hud.Window.Size.Width;
                var h = Hud.Window.Size.Height;
                return x >= 0 && y >= 0 && x < w && y < h;
            }
            catch
            {
                return true;
            }
        }

        private bool TryGetScreen(IMonster m, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            try
            {
                var sc = m.ScreenCoordinate;
                if (sc != null)
                {
                    x = sc.X;
                    y = sc.Y;
                    return true;
                }

                var tsc = m.FloorCoordinate.ToScreenCoordinate();
                x = tsc.X;
                y = tsc.Y;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float Dist(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static Keys KeyToWinKeys(Key k)
        {
            if (string.Equals(k.ToString(), "Space", StringComparison.Ordinal))
                return Keys.Space;

            switch (k)
            {
                case Key.D1: return Keys.D1;
                case Key.D2: return Keys.D2;
                case Key.D3: return Keys.D3;
                case Key.D4: return Keys.D4;
                case Key.D5: return Keys.D5;
                case Key.D6: return Keys.D6;
                case Key.D7: return Keys.D7;
                case Key.D8: return Keys.D8;
                case Key.D9: return Keys.D9;
                case Key.D0: return Keys.D0;
                case Key.LeftBracket: return Keys.OemOpenBrackets;
                case Key.RightBracket: return Keys.OemCloseBrackets;
                case Key.F1: return Keys.F1;
                case Key.F2: return Keys.F2;
                case Key.F3: return Keys.F3;
                case Key.F4: return Keys.F4;
                case Key.F5: return Keys.F5;
                case Key.F6: return Keys.F6;
                case Key.F7: return Keys.F7;
                case Key.F8: return Keys.F8;
                case Key.F9: return Keys.F9;
                case Key.F10: return Keys.F10;
                case Key.F11: return Keys.F11;
                case Key.F12: return Keys.F12;
                case Key.A: return Keys.A;
                case Key.B: return Keys.B;
                case Key.C: return Keys.C;
                case Key.D: return Keys.D;
                case Key.E: return Keys.E;
                case Key.F: return Keys.F;
                case Key.G: return Keys.G;
                case Key.H: return Keys.H;
                case Key.I: return Keys.I;
                case Key.J: return Keys.J;
                case Key.K: return Keys.K;
                case Key.L: return Keys.L;
                case Key.M: return Keys.M;
                case Key.N: return Keys.N;
                case Key.O: return Keys.O;
                case Key.P: return Keys.P;
                case Key.Q: return Keys.Q;
                case Key.R: return Keys.R;
                case Key.S: return Keys.S;
                case Key.T: return Keys.T;
                case Key.U: return Keys.U;
                case Key.V: return Keys.V;
                case Key.W: return Keys.W;
                case Key.X: return Keys.X;
                case Key.Y: return Keys.Y;
                case Key.Z: return Keys.Z;
                default: return Keys.None;
            }
        }

        private bool IsHotkeyPhysicallyDownNative()
        {
            try
            {
                Keys k = KeyToWinKeys(Hotkey);
                if (k == Keys.None)
                    return false;

                return (GetAsyncKeyState((int)k) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }


        private bool IsKeyPhysicallyDown(Key dxKey, Keys winKey)
        {
            try
            {
                var input = Hud.Input;
                if (input == null) return false;

                var t = input.GetType();

                var miDx = t.GetMethod("IsKeyDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Key) }, null);
                if (miDx != null && miDx.ReturnType == typeof(bool))
                    return (bool)miDx.Invoke(input, new object[] { dxKey });

                var miWin = t.GetMethod("IsKeyDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Keys) }, null);
                if (miWin != null && miWin.ReturnType == typeof(bool))
                    return (bool)miWin.Invoke(input, new object[] { winKey });
            }
            catch { }
            return false;
        }

        private static bool TryInvokeVoidAction(object obj, string methodName, ActionKey key)
        {
            if (obj == null || methodName == null) return false;

            try
            {
                var t = obj.GetType();

                var mi = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(ActionKey) }, null);
                if (mi != null && mi.ReturnType == typeof(void))
                {
                    mi.Invoke(obj, new object[] { key });
                    return true;
                }

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(m.Name, methodName, StringComparison.Ordinal) || m.ReturnType != typeof(void)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;

                    object arg = key;
                    var p0 = ps[0].ParameterType;

                    if (!p0.IsAssignableFrom(typeof(ActionKey)))
                    {
                        if (p0.IsEnum)
                        {
                            try { arg = Enum.Parse(p0, key.ToString(), true); }
                            catch { continue; }
                        }
                        else if (p0 == typeof(int)) arg = Convert.ToInt32(key);
                        else if (p0 == typeof(object)) arg = key;
                        else continue;
                    }

                    m.Invoke(obj, new object[] { arg });
                    return true;
                }
            }
            catch { }

            return false;
        }

        // =========================
        // NATIVE ATTRIBUTE HELPERS
        // =========================

        private bool IsNativeKnockbackImmune(IMonster m)
        {
            if (m == null || Hud?.Sno?.Attributes == null) return false;

            try
            {
                var immune = m.GetAttributeValueAsInt(Hud.Sno.Attributes.Immune_To_Knockback, 0, 0);
                if (immune > 0) return true;
            }
            catch { }

            try
            {
                var weight = m.GetAttributeValue(Hud.Sno.Attributes.Knockback_Weight, 0, -1.0f);
                if (weight >= 0.0 && weight <= 0.001) return true;
            }
            catch { }

            return false;
        }

        private static bool HasAffix(IMonster m, MonsterAffix affix)
        {
            try
            {
                var list = m.AffixSnoList;
                if (list == null) return false;
                foreach (var a in list)
                {
                    if (a != null && a.Affix == affix)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsShieldingActive(IMonster m)
        {
            if (m == null) return false;

            if (TryGetBool(m, "IsShielding", out var b) && b) return true;
            if (TryGetBool(m, "Shielding", out b) && b) return true;
            if (TryGetBool(m, "ShieldingActive", out b) && b) return true;
            if (TryGetBool(m, "HasShielding", out b) && b) return true;
            if (TryGetBool(m, "Invulnerable", out b) && b) return true;
            if (TryGetBool(m, "IsInvulnerable", out b) && b) return true;
            if (TryGetBool(m, "IsShielded", out b) && b) return true;
            if (TryGetBool(m, "Shielded", out b) && b) return true;

            return false;
        }

        private static bool IsKnownUnpullableFamily(IMonster m)
        {
            var sm = m?.SnoMonster;
            if (sm == null) return false;

            var code = sm.Code ?? string.Empty;
            var english = sm.NameEnglish ?? string.Empty;
            var localized = sm.NameLocalized ?? string.Empty;

            return ContainsAny(code,
                    "armaddon", "armadon", "gorgor", "gogor", "mallet", "malletlord",
                    "hellhide", "tremor", "hellhidetremor")
                || ContainsAny(english,
                    "Armaddon", "Armadon", "Gorgor", "Gogor", "Mallet", "Mallet Lord",
                    "Hellhide", "Tremor", "Hellhide Tremor")
                || ContainsAny(localized,
                    "Armaddon", "Armadon", "Gorgor", "Gogor", "Mallet", "Mallet Lord",
                    "Hellhide", "Tremor", "Hellhide Tremor");
        }

        private static bool ContainsAny(string s, params string[] tokens)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var t in tokens)
                if (s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool TryGetBool(object obj, string propName, out bool value)
        {
            value = false;
            if (obj == null) return false;
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.PropertyType == typeof(bool))
                {
                    value = (bool)pi.GetValue(obj, null);
                    return true;
                }

                var fi = obj.GetType().GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null && fi.FieldType == typeof(bool))
                {
                    value = (bool)fi.GetValue(obj);
                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
