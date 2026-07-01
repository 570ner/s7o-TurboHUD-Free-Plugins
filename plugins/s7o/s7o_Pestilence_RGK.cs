using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // Pestilence RGK helper: Pestilence-specific target assist + Siphon Blood Power Shift stack pulses.
    // HUD MENU owns enable/settings; this plugin intentionally has no visual overlays or old quick menu.
    public class s7o_Pestilence_RGK : BasePlugin, IAfterCollectHandler, INewAreaHandler, IInGameWorldPainter
    {
        #region Settings

        public int NormalStackTarget { get; private set; }
        public int PowerStackTarget { get; private set; }

        public bool AutoSiphonEnabled = true;
        public bool LateRefreshAssist = true;
        public bool PrioritizeDebuffedElites = true;
        public bool AggressiveScanMode = false;
        public bool IncludeTrashTargets = true;
        public bool RestoreCursorOnReleaseOrMove = true;
        public int PestilenceRangeYards = 70;
        public bool JuggerHotkeyEnabled = true;
        public ushort JuggerHotkeyVirtualKey = 0x20; // Space
        public bool RgAutoSnapSiphonAssist = false;

        public ushort ForceStandstillVirtualKey = 0x10; // Shift
        public ushort Skill1VirtualKey = 0x31;          // 1
        public ushort Skill2VirtualKey = 0x32;          // 2
        public ushort Skill3VirtualKey = 0x33;          // 3
        public ushort Skill4VirtualKey = 0x34;          // 4

        private const int BuildPulseMs = 100;
        private const int LotdBuildPulseMs = 70;
        private const int MaintainPulseMs = 400;
        private const int LateRefreshWindowMsDefault = 800;
        private const int BossLateRefreshPulseMs = 800;
        private const int ProactiveRefreshWindowMs = 1200;
        private const int PowerShiftBuffIconIndex = 10;
        private const int FirstEngageSiphonDelayTicks = 2;
        private const int LateRefreshIntentExpireTicks = 30;
        private const int LateRefreshIntentRetryTicks = 4;
        private const int LateRefreshIntentMaxAttempts = 4;
        private const float LateRefreshIntentRefreshGainSeconds = 0.35f;
        private const int PulseDownTicks = 2;
        private const int LateRefreshDownTicks = 4;
        private const int LotdPulseDownTicks = 6;
        private const int BossPulseDownTicks = 6;
        private const int CorpseScanIntervalTicks = 10;
        private const int SiphonAssistPauseTicks = 10;
        private const int CursorRestoreShortEngageTicks = 120; // HUD_MENU AutoSnap restore window (~2s at 60 tps)
        private const float CursorRestoreMinMovePxSq = 16f;
        private const int SkipUnhoverableLeaderTicks = 18;
        private const int SkipUnhoverableTrashTicks = 10;
        private const float SnapRangeYards = 90f;
        private const float CorpseScanRangeYards = 120f;
        private const float JuggerLockCircleYards = 10f;
        private const int JuggerLockDashCount = 28;
        private const float JuggerLockDashFill = 0.46f;
        private const double JuggerLockRotationSeconds = 6.5d;
        private const int NoTargetAnchorPulseMs = 100;
        private const int NoTargetAnchorDownTicks = 2;
        private const int NoTargetAnchorPulsesPerBurst = 3;
        private const int NoTargetAnchorMaxPulses = 6;
        private const int NoTargetAnchorBurstPauseMs = 500;
        private const float PestilenceEdgePenaltyStart = 15.8f;
        private const float PestilenceEdgePenaltyMax = 0.85f;
        private const float TrashPenaltyStart = 15.0f;
        private const float TrashEdgePenaltyMax = 2.35f;
        private const float BaselineRadiusBottom = 3.15f;
        private const float BaselineHitboxYards = 0.9f;
        private const float HitboxBonusCap = 2.0f;
        private const float BaseHeadPx = 65f;
        private const float BaseShoulderPx = 50f;
        private const float BaseChestPx = 35f;
        private const float BaseAbdomenPx = 18f;
        private const float BaseShoulderXPx = 25f;
        private const double HighValueTrashMinRiftProgression = 0.85d;
        private const double MediumValueTrashMinRiftProgression = 0.60d;
        private const double BigTrashMinRiftProgression = HighValueTrashMinRiftProgression; // compatibility for existing occlusion/probe code
        private const float MediumValueTrashPreferRangeYards = 22.0f;

        private const int AdaptiveBinCount = 8;
        private const int AdaptiveCandidateCapacity = 96;
        private const int ProbeZoneCount = 9;
        private const int MinTicksBetweenMouseMoves = 1;
        private const int StableLockTicks = 16;
        private const int SoftLockTicks = 12;
        private const int ReacquireWindowTicks = 16;
        private const int FailedLeaderRetryWindowTicks = 20;
        private const int AlternateScanWindowTicks = 18;
        private const int SkipLeaderWindowTicks = 10;
        private const float TeleportThresholdSq = 100f;
        private const int PostTeleportForceSnapTicks = 15;
        private const uint PowerPylonBuffSno = 262935u; // Generic_PagesBuffDamage

        // Same explicit no-click UI masks used by HUD_MENU AutoSnap.
        // Reference coordinates are 1920x1080 and scale to the current HUD window.
        private const float UiMaskReferenceWidth = 1920f;
        private const float UiMaskReferenceHeight = 1080f;
        private const float UiMaskPaddingPx = 0f;

        private static readonly RectangleF[] NoClickMaskRects1920x1080 = new RectangleF[]
        {
            new RectangleF(116f, 11f, 76f, 71f),
            new RectangleF(34f, 57f, 58f, 61f),
            new RectangleF(871f, 2f, 179f, 21f),
            new RectangleF(1644f, 23f, 60f, 26f),
            new RectangleF(1816f, 120f, 25f, 15f),
            new RectangleF(1863f, 363f, 31f, 29f),
            new RectangleF(8f, 973f, 85f, 80f),
            new RectangleF(315f, 893f, 1289f, 187f),
            new RectangleF(1754f, 961f, 157f, 83f),
        };

        // Ported from the original LightningMod selector. FreeHUD exposes most of these fields natively,
        // but pack/affix state is not always surfaced consistently for minions and shielding packs.
        private static readonly string[] PackPropNames = { "MonsterPack", "Pack", "RiftMonsterPack", "ElitePack", "MonsterPackInfo" };
        private static readonly string[] PackIdPropNames = { "Id", "PackId", "MonsterPackId", "RiftMonsterPackId", "ElitePackId" };
        private static readonly string[] AffixPropNames = { "AffixSnoList", "Affixes", "AffixList" };
        private static readonly Dictionary<Type, PropertyInfo> PackPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> PackIdPropCache = new Dictionary<Type, PropertyInfo>(8);
        private static readonly Dictionary<Type, PropertyInfo> AffixListPropCache = new Dictionary<Type, PropertyInfo>(16);

        #endregion

        #region Runtime State

        private uint _snoCorpseLance;
        private uint _snoSiphonBlood;
        private uint _snoLandOfTheDead;

        private ActionKey _lanceKey = ActionKey.Unknown;
        private ActionKey _siphonKey = ActionKey.Unknown;
        private bool _lanceKeyKnown;
        private bool _siphonKeyKnown;

        private bool _pulseActive;
        private bool _siphonPulseOwned;
        private bool _standstillOwned;
        private bool _pulseWasBuild;
        private int _pulseBuildTarget;
        private int _pulseDownUntilTick;
        private int _nextPulseTick;
        private bool _lateRefreshPulseConsumed;
        private int _siphonAssistUntilTick;
        private int _fastBuildStartTick;
        private int _fastBuildPulseCount;
        private int _fastBuildTarget;

        private uint _lockedTargetAcdId;
        private int _lockedTargetKeepUntilTick;
        private const int LockedTargetKeepTicks = 600; // ~10s at 60 tps; mirrors LM leader lock persistence.
        private uint _returnToRareAcdId;
        private uint _skipAcdId;
        private int _skipUntilTick;
        private uint _snapPhaseAcdId;
        private int _snapPhase;
        private int _lastMouseMoveTick;
        private bool _cursorOwned;
        private bool _cursorWasMovedByPlugin;
        private bool _haveSavedCursor;
        private float _savedCursorX;
        private float _savedCursorY;
        private int _engageStartTick;
        private uint _lastHoverAcdId;
        private int _lastHoverTick;
        private float _lastHoverDx;
        private float _lastHoverDy;

        private readonly float[] _rankedProbeDx = new float[AdaptiveCandidateCapacity];
        private readonly float[] _rankedProbeDy = new float[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeBin = new int[AdaptiveCandidateCapacity];
        private readonly int[] _rankedProbeZone = new int[AdaptiveCandidateCapacity];
        private readonly float[] _rankedProbeScore = new float[AdaptiveCandidateCapacity];
        private readonly float[] _adaptiveBinGood = new float[AdaptiveBinCount];
        private readonly float[] _adaptiveBinBad = new float[AdaptiveBinCount];
        private int _rankedProbeCount;
        private uint _lastSnapAttemptAcd;
        private int _lastSnapAttemptBin = -1;
        private int _lastSnapAttemptZone = -1;
        private int _lastSnapAttemptTick;
        private uint _probeZoneAcdId;
        private readonly int[] _probeZoneLastTryTick = new int[ProbeZoneCount];
        private readonly int[] _probeZoneTryCount = new int[ProbeZoneCount];
        private uint _probeZoneSuccessAcdId;
        private int _lastSuccessProbeZone = -1;
        private int _lastSuccessProbeZoneTick;
        private uint _stableLockAcdId;
        private float _stableLockDx;
        private float _stableLockDy;
        private int _stableLockUntilTick;
        private uint _softLockAcdId;
        private float _softLockDx;
        private float _softLockDy;
        private int _softLockUntilTick;
        private uint _cachedHoverAcdId;
        private float _cachedHoverDx;
        private float _cachedHoverDy;
        private int _cachedHoverUntilTick;
        private int _cachedHoverTryUntilTick;
        private uint _reacquireAcdId;
        private int _reacquireUntilTick;
        private uint _unhoverableAcdId;
        private int _unhoverableUntilTick;
        private uint _lastFailedLeaderAcdId;
        private int _lastFailedLeaderTick;
        private int _lastFailedLeaderCount;
        private uint _alternateScanAcdId;
        private int _alternateScanUntilTick;
        private uint _skipLeaderAcdId;
        private int _skipLeaderUntilTick;

        private uint _solverProfileAcd;
        private float _solverOccLeft, _solverOccRight, _solverOccUp, _solverOccDown;
        private float _solverBigLeft, _solverBigRight, _solverBigUp, _solverBigDown;
        private float _solverBlockerCx, _solverBlockerCy;
        private float _solverBlockerWeightTotal;
        private int _solverBlockerCount;

        private float _lastMeX;
        private float _lastMeY;
        private bool _haveLastMePos;
        private int _teleportDetectedTick;
        private bool _forceMoveWasDown;
        private bool _siphonKeyWasDown;
        private bool _lanceWasDown;
        private bool _engageRefreshConsumed;
        private uint _lastSiphonTargetAcdId;
        private uint _manualJuggerLockAcdId;
        private bool _juggerHotkeyWasDown;
        private IBrush _juggerCircleBrush;
        private IBrush _juggerCircleOutlineBrush;
        private uint _targetSwitchRefreshAcdId;
        private bool _lateRefreshIntentActive;
        private bool _lateRefreshIntentFromEngage;
        private int _lateRefreshIntentReadyTick;
        private int _lateRefreshIntentExpireTick;
        private int _lateRefreshIntentAttempts;
        private int _lateRefreshIntentNextAttemptTick;
        private float _lateRefreshIntentStartTimeLeft;
        private int _noTargetAnchorPulses;
        private int _noTargetAnchorLastTick;
        private bool _lastPulseWasNoTargetAnchor;

        private int _lastCorpseScanTick;
        private bool _cachedCorpsesAvailable;
        private int _lastSeenGameTick;

        private IUiElement _chatEditLine;

        #endregion

        public s7o_Pestilence_RGK()
        {
            Enabled = false;
            Order = 65000;
            NormalStackTarget = 8;
            PowerStackTarget = 6;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            try { _snoCorpseLance = hud.Sno.SnoPowers.Necromancer_CorpseLance.Sno; } catch { _snoCorpseLance = 461650u; }
            try { _snoSiphonBlood = hud.Sno.SnoPowers.Necromancer_SiphonBlood.Sno; } catch { _snoSiphonBlood = 453563u; }
            try { _snoLandOfTheDead = hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno; } catch { _snoLandOfTheDead = 465839u; }

            try { _chatEditLine = Hud.Render.GetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline"); } catch { }
            try
            {
                _juggerCircleOutlineBrush = Hud.Render.CreateBrush(235, 0, 0, 0, 7.0f);
                _juggerCircleBrush = Hud.Render.CreateBrush(245, 255, 132, 0, 3.4f);
            }
            catch { }
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards)
        {
            Configure(normalStacks, powerStacks, autoSiphon, lateRefreshAssist, prioritizeDebuffedElites, aggressiveScanMode, includeTrashTargets, restoreCursorOnReleaseOrMove, pestilenceRangeYards, JuggerHotkeyEnabled, JuggerHotkeyVirtualKey, RgAutoSnapSiphonAssist);
        }

        public void Configure(int normalStacks, int powerStacks, bool autoSiphon, bool lateRefreshAssist, bool prioritizeDebuffedElites, bool aggressiveScanMode, bool includeTrashTargets, bool restoreCursorOnReleaseOrMove, int pestilenceRangeYards, bool juggerHotkeyEnabled, ushort juggerHotkeyVirtualKey, bool rgAutoSnapSiphonAssist)
        {
            NormalStackTarget = ClampStackTarget(normalStacks);
            PowerStackTarget = ClampStackTarget(powerStacks);
            AutoSiphonEnabled = autoSiphon;
            LateRefreshAssist = lateRefreshAssist;
            PrioritizeDebuffedElites = prioritizeDebuffedElites;
            AggressiveScanMode = aggressiveScanMode;
            IncludeTrashTargets = includeTrashTargets;
            RestoreCursorOnReleaseOrMove = restoreCursorOnReleaseOrMove;
            PestilenceRangeYards = ClampRange(pestilenceRangeYards);
            JuggerHotkeyEnabled = juggerHotkeyEnabled;
            JuggerHotkeyVirtualKey = juggerHotkeyVirtualKey;
            RgAutoSnapSiphonAssist = rgAutoSnapSiphonAssist;
            if (!JuggerHotkeyEnabled || JuggerHotkeyVirtualKey == 0)
                ClearManualJuggerLock();
        }

        public void SetStackTargets(int normal, int power)
        {
            Configure(normal, power, AutoSiphonEnabled, LateRefreshAssist, PrioritizeDebuffedElites, AggressiveScanMode, IncludeTrashTargets, RestoreCursorOnReleaseOrMove, PestilenceRangeYards);
        }

        public void ForceStopForDisable()
        {
            ResetRuntime(true);
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            ResetRuntime(true);
            ClearManualJuggerLock();
        }

        public void AfterCollect()
        {
            try
            {
                if (!CanRun())
                {
                    ResetRuntime(true);
                    return;
                }

                int tick = Hud.Game.CurrentGameTick;
                if (DetectStaleSessionReset(tick))
                    return;

                ProcessPendingCursorRestore(tick);
                UpdatePlayerMotionState(tick);
                UpdatePassiveEliteHoverCache(tick);
                ClearDeadTargetState();
                ProcessJuggerHotkey(tick);

                if (!ResolveSkillKeys())
                {
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                    ResetRuntime(true);
                    return;
                }

                TickPulseReleaseIfNeeded(tick);

                bool lanceHeld = IsActionPhysicallyDown(_lanceKey);
                bool forceMoving = IsForceMoving();

                if (!lanceHeld)
                {
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                    ResetRuntime(true);
                    return;
                }

                bool newLanceEngagement = !_lanceWasDown || _forceMoveWasDown;
                _lanceWasDown = true;

                if (forceMoving)
                {
                    StopPulseNow();
                    ReleaseStandstillIfOwned();
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                    _forceMoveWasDown = true;
                    return;
                }
                _forceMoveWasDown = false;

                bool manualSiphonDown = _siphonKeyKnown && _siphonKey != _lanceKey && !_siphonPulseOwned && IsActionPhysicallyDown(_siphonKey);
                if (manualSiphonDown)
                {
                    _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);
                    _siphonKeyWasDown = true;
                }
                else if (_siphonKeyWasDown)
                {
                    _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);
                    _siphonKeyWasDown = false;
                }

                if (TryRunLateRefreshIntent(tick))
                    return;

                bool delayInitialAutosnapForRefresh = ShouldDelayInitialAutosnapForLateRefresh(newLanceEngagement, manualSiphonDown);
                if (delayInitialAutosnapForRefresh)
                {
                    QueueLateRefreshIntent(tick, true);
                    return;
                }

                IMonster target = null;
                IMonster siphonTarget = null;
                IMonster forcedTarget = GetManualJuggerLockTarget();
                if (!IsAliveTarget(forcedTarget) && RgAutoSnapSiphonAssist)
                    forcedTarget = FindRiftGuardianTarget();

                if (IsAliveTarget(forcedTarget))
                {
                    target = forcedTarget;
                    siphonTarget = target;
                    TrySnap(target, tick);
                }
                else if (!BossAlive())
                {
                    target = AcquireTarget(tick);
                    if (IsAliveTarget(target))
                    {
                        siphonTarget = target;
                        TrySnap(target, tick);
                    }
                    else
                    {
                        TryAcquireHoverForAutoSiphon(tick, out siphonTarget);
                    }
                }
                else
                {
                    // Default RG behavior preserves manual aim unless RG AutoSnap/Siphon Assist is enabled.
                    ClearAutosnapLockState();
                    siphonTarget = GetManualSiphonTargetFallback();
                }

                if (!IsAliveTarget(siphonTarget))
                    siphonTarget = GetManualSiphonTargetFallback();

                if (!IsAliveTarget(target) && !IsAliveTarget(siphonTarget) && AnyEliteOrMinionCandidateExists() )
                {
                    QueueCursorRestore(tick);
                    ProcessPendingCursorRestore(tick);
                }

                RunAutoSiphon(tick, siphonTarget, newLanceEngagement);
            }
            catch
            {
                ResetRuntime(true);
            }
        }

        private bool CanRun()
        {
            if (!Enabled || Hud == null || Hud.Game == null || Hud.Window == null)
                return false;

            if (!Hud.Window.IsForeground || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.IsInTown)
                return false;

            if (Hud.Game.MapMode != MapMode.Minimap && Hud.Game.MapMode != MapMode.PermaMap)
                return false;

            if (IsUiVisible(_chatEditLine))
                return false;

            try
            {
                if (Hud.Inventory != null && IsUiVisible(Hud.Inventory.InventoryMainUiElement))
                    return false;
            }
            catch { }

            var me = Hud.Game.Me;
            if (me == null || me.IsDead || me.Powers == null || me.HeroClassDefinition == null)
                return false;

            return me.HeroClassDefinition.HeroClass == HeroClass.Necromancer;
        }

        private bool IsUiVisible(IUiElement element)
        {
            try { return element != null && element.Visible; }
            catch { return false; }
        }


        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Ground || Hud == null || Hud.Game == null || !Hud.Game.IsInGame)
                return;

            IMonster jugg = GetManualJuggerLockTarget();
            if (!IsAliveTarget(jugg) || jugg.FloorCoordinate == null)
                return;

            try
            {
                DrawJuggerLockCircle(jugg.FloorCoordinate);
            }
            catch { }
        }

        private void DrawJuggerLockCircle(IWorldCoordinate center)
        {
            if (center == null)
                return;

            double tick = Hud.Game != null ? Hud.Game.CurrentGameTick : 0.0d;
            double secondsPerTurn = Math.Max(1.0d, JuggerLockRotationSeconds);
            double phase = (tick / (secondsPerTurn * 60.0d)) * Math.PI * 2.0d;

            DrawDashedWorldCircle(center, JuggerLockCircleYards, JuggerLockDashCount, JuggerLockDashFill, phase, _juggerCircleOutlineBrush);
            DrawDashedWorldCircle(center, JuggerLockCircleYards, JuggerLockDashCount, JuggerLockDashFill, phase, _juggerCircleBrush);
        }

        private void DrawDashedWorldCircle(IWorldCoordinate center, float radius, int dashCount, float dashFill, double phase, IBrush brush)
        {
            if (center == null || brush == null || radius <= 0f || dashCount <= 0)
                return;

            double full = Math.PI * 2.0d;
            double step = full / dashCount;
            double dashAngle = step * Math.Max(0.10d, Math.Min(0.90d, dashFill));

            for (int i = 0; i < dashCount; i++)
            {
                double a0 = phase + i * step;
                double a1 = a0 + dashAngle;

                var p0 = center.Offset(radius * (float)Math.Cos(a0), radius * (float)Math.Sin(a0), 0);
                var p1 = center.Offset(radius * (float)Math.Cos(a1), radius * (float)Math.Sin(a1), 0);

                brush.DrawLineWorld(p0, p1);
            }
        }

        private bool DetectStaleSessionReset(int tick)
        {
            if (tick <= 0)
            {
                ResetRuntime(true);
                _lastSeenGameTick = 0;
                return true;
            }

            if (_lastSeenGameTick > 0 && tick + 60 < _lastSeenGameTick)
            {
                ResetRuntime(true);
                _lastSeenGameTick = tick;
                return true;
            }

            _lastSeenGameTick = tick;
            return false;
        }

        #region Skill / AutoSiphon

        private bool ResolveSkillKeys()
        {
            if (_lanceKeyKnown && _siphonKeyKnown)
                return true;

            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _lanceKey = ActionKey.Unknown;
            _siphonKey = ActionKey.Unknown;

            try
            {
                var slots = Hud.Game.Me.Powers.SkillSlots;
                if (slots == null)
                    return false;

                for (int i = 0; i < slots.Length; i++)
                {
                    var skill = slots[i];
                    if (skill == null || skill.SnoPower == null)
                        continue;

                    uint sno = skill.SnoPower.Sno;
                    if (sno == _snoCorpseLance)
                    {
                        _lanceKey = skill.Key;
                        _lanceKeyKnown = true;
                    }
                    else if (sno == _snoSiphonBlood)
                    {
                        _siphonKey = skill.Key;
                        _siphonKeyKnown = true;
                    }
                }
            }
            catch { }

            return _lanceKeyKnown && _siphonKeyKnown && _lanceKey != ActionKey.Unknown && _siphonKey != ActionKey.Unknown;
        }

        private void RunAutoSiphon(int tick, IMonster currentTarget, bool newLanceEngagement)
        {
            TickPulseReleaseIfNeeded(tick);

            if (_pulseActive)
                return;

            bool fullAuto = AutoSiphonEnabled;
            bool refreshOnly = !fullAuto && LateRefreshAssist;

            if (!fullAuto && !refreshOnly)
            {
                StopPulseNow();
                return;
            }

            if (!_siphonKeyKnown)
            {
                StopPulseNow();
                return;
            }

            if (!IsAliveTarget(currentTarget))
            {
                if (TryAnchorSiphonWithoutTarget(tick))
                    return;

                StopPulseNow();
                return;
            }

            ClearNoTargetAnchorLimiter(true);

            if (_siphonKey != _lanceKey && !_siphonPulseOwned && IsActionPhysicallyDown(_siphonKey))
            {
                StopPulseNow();
                return;
            }

            bool lotdActive = IsBuffActive(_snoLandOfTheDead);
            bool powerActive = IsBuffActive(PowerPylonBuffSno);
            bool corpsesAvailable = lotdActive || CorpsesAvailable(tick);

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            ResetLateRefreshSinglePulseGate(havePowerShift, stacks, timeLeft);

            uint currentTargetAcdId = GetMonsterAcdId(currentTarget);
            bool targetChanged = currentTargetAcdId != 0 && currentTargetAcdId != _lastSiphonTargetAcdId;
            if (currentTargetAcdId != 0)
                _lastSiphonTargetAcdId = currentTargetAcdId;

            if (fullAuto && ShouldDoProactiveRefresh(tick, currentTargetAcdId, targetChanged, newLanceEngagement, havePowerShift, stacks, timeLeft, lotdActive))
                return;

            if (BossAlive() && !RgAutoSnapSiphonAssist)
            {
                if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                    return;

                if ((timeLeft * 1000f) <= LateRefreshWindowMsDefault)
                {
                    if (PulseSiphon(tick, MsToTicks(BossLateRefreshPulseMs), BossPulseDownTicks, false))
                        _lateRefreshPulseConsumed = true;
                }
                return;
            }

            if (refreshOnly)
            {
                if (!havePowerShift || stacks <= 0 || timeLeft <= 0f)
                    return;

                if ((timeLeft * 1000f) <= LateRefreshWindowMsDefault)
                {
                    if (PulseSiphon(tick, MsToTicks(BossLateRefreshPulseMs), BossPulseDownTicks, false))
                        _lateRefreshPulseConsumed = true;
                }
                return;
            }

            int activeTarget = ClampStackTarget(powerActive ? PowerStackTarget : NormalStackTarget);
            bool reachedConfiguredTarget = havePowerShift && stacks >= activeTarget;

            // Fast cadence is only for the configured HUD Menu target.
            // If the user wants 10 stacks, they set 10; do not silently escalate lower targets to 10.
            bool needBuild = fullAuto && (!havePowerShift || stacks < activeTarget);
            bool needRefresh = LateRefreshAssist && havePowerShift && stacks > 0 && timeLeft > 0f && corpsesAvailable && (timeLeft * 1000f) <= LateRefreshWindowMsDefault;
            bool needMaintain = fullAuto && havePowerShift && reachedConfiguredTarget;

            if (needBuild && FastBuildFuseTripped(tick, activeTarget, havePowerShift, stacks))
            {
                needBuild = false;
                needMaintain = havePowerShift && stacks > 0;
            }

            int intervalTicks;
            int downTicks = PulseDownTicks;

            if (needBuild)
            {
                intervalTicks = MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs);
                downTicks = lotdActive ? LotdPulseDownTicks : PulseDownTicks;

                if (havePowerShift && stacks >= activeTarget - 1)
                    downTicks = lotdActive ? Math.Max(1, Math.Min(LotdPulseDownTicks, 3)) : 1;
            }
            else if (needRefresh)
            {
                intervalTicks = MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs);
                downTicks = GetLateRefreshDownTicks(lotdActive);
                ClearFastBuildGuard();
            }
            else if (needMaintain)
            {
                intervalTicks = MsToTicks(MaintainPulseMs);
                ClearFastBuildGuard();
            }
            else
            {
                ClearFastBuildGuard();
                return;
            }

            _pulseWasBuild = needBuild;
            _pulseBuildTarget = needBuild ? activeTarget : 0;

            bool pulsed = PulseSiphon(tick, intervalTicks, downTicks, true);
            if (pulsed && needBuild)
                _fastBuildPulseCount++;

            if (pulsed && needRefresh)
                _lateRefreshPulseConsumed = true;
        }


        private bool FastBuildFuseTripped(int tick, int activeTarget, bool havePowerShift, int stacks)
        {
            if (_fastBuildTarget != activeTarget || _fastBuildStartTick <= 0)
            {
                _fastBuildStartTick = tick;
                _fastBuildPulseCount = 0;
                _fastBuildTarget = activeTarget;
                return false;
            }

            if (havePowerShift && stacks >= activeTarget)
                return false;

            return unchecked(tick - _fastBuildStartTick) > MsToTicks(4000) || _fastBuildPulseCount > 45;
        }

        private void ClearFastBuildGuard()
        {
            _fastBuildStartTick = 0;
            _fastBuildPulseCount = 0;
            _fastBuildTarget = 0;
        }

        private bool TryAnchorSiphonWithoutTarget(int tick)
        {
            if (!LateRefreshAssist || !_siphonKeyKnown || _pulseActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);
            if (havePowerShift && stacks > 0 && timeLeft > 0f && (timeLeft * 1000f) > ProactiveRefreshWindowMs)
                return false;

            return PulseNoTargetAnchor(tick);
        }

        private bool PulseNoTargetAnchor(int tick)
        {
            if (NoTargetAnchorExhausted())
                return false;

            int gapTicks = MsToTicks(NoTargetAnchorPulseMs);
            if (_noTargetAnchorPulses > 0 && (_noTargetAnchorPulses % NoTargetAnchorPulsesPerBurst) == 0)
                gapTicks = MsToTicks(NoTargetAnchorBurstPauseMs);

            if (_noTargetAnchorLastTick > 0 && tick - _noTargetAnchorLastTick < gapTicks)
                return false;

            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            if (!PulseSiphon(tick, MsToTicks(NoTargetAnchorPulseMs), NoTargetAnchorDownTicks, false))
                return false;

            _noTargetAnchorPulses++;
            _noTargetAnchorLastTick = tick;
            _lastPulseWasNoTargetAnchor = true;
            return true;
        }

        private bool NoTargetAnchorExhausted()
        {
            return _noTargetAnchorPulses >= NoTargetAnchorMaxPulses;
        }

        private void ClearNoTargetAnchorLimiter(bool clearNoTargetPulseThrottle)
        {
            _noTargetAnchorPulses = 0;
            _noTargetAnchorLastTick = 0;

            if (clearNoTargetPulseThrottle && _lastPulseWasNoTargetAnchor)
            {
                _nextPulseTick = 0;
                _lastPulseWasNoTargetAnchor = false;
            }
        }

        private IMonster GetManualSiphonTargetFallback()
        {
            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected))
                {
                    if (IsBossLike(selected))
                        return selected;

                    if (!IsInvulnerable(selected) && !IsIllusionOrClone(selected))
                    {
                        if (IsLeader(selected) || IsEliteMinionLike(selected))
                            return selected;

                        if (IncludeTrashTargets && SafeDistance(selected) <= PestiPick() + HitboxRangeBonus(selected))
                            return selected;
                    }
                }
            }
            catch { }

            try
            {
                if (_lockedTargetAcdId != 0)
                {
                    var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                    if (IsAliveTarget(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                        return locked;
                }
            }
            catch { }

            return null;
        }

        private bool ShouldDelayInitialAutosnapForLateRefresh(bool newLanceEngagement, bool manualSiphonDown)
        {
            if (!newLanceEngagement || manualSiphonDown || _engageRefreshConsumed || _pulseActive)
                return false;

            if (!AutoSiphonEnabled || !LateRefreshAssist || !_siphonKeyKnown)
                return false;

            int stacks;
            float timeLeft;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f)
                return false;

            return (timeLeft * 1000f) <= ProactiveRefreshWindowMs;
        }

        private void QueueLateRefreshIntent(int tick, bool fromEngage)
        {
            if (_lateRefreshIntentActive)
                return;

            int stacks;
            float timeLeft;
            if (!TryGetPowerShift(out stacks, out timeLeft) || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > ProactiveRefreshWindowMs)
                return;

            _lateRefreshIntentActive = true;
            _lateRefreshIntentFromEngage = fromEngage;
            _lateRefreshIntentReadyTick = tick + (fromEngage ? FirstEngageSiphonDelayTicks : 0);
            _lateRefreshIntentExpireTick = tick + LateRefreshIntentExpireTicks;
            _lateRefreshIntentAttempts = 0;
            _lateRefreshIntentNextAttemptTick = _lateRefreshIntentReadyTick;
            _lateRefreshIntentStartTimeLeft = timeLeft;
        }

        private bool TryRunLateRefreshIntent(int tick)
        {
            if (!_lateRefreshIntentActive)
                return false;

            int stacks;
            float timeLeft;
            bool havePowerShift = TryGetPowerShift(out stacks, out timeLeft);

            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > ProactiveRefreshWindowMs || timeLeft > _lateRefreshIntentStartTimeLeft + LateRefreshIntentRefreshGainSeconds)
            {
                if (_lateRefreshIntentFromEngage)
                    _engageRefreshConsumed = true;

                ClearLateRefreshIntent();
                return false;
            }

            if (!AutoSiphonEnabled || !LateRefreshAssist)
            {
                ClearLateRefreshIntent();
                return false;
            }

            IMonster target = FindLateRefreshIntentTarget(tick);
            bool hasTarget = IsAliveTarget(target);
            bool lotdActive = IsBuffActive(_snoLandOfTheDead);

            if (tick > _lateRefreshIntentExpireTick || (!hasTarget && _lateRefreshIntentAttempts >= LateRefreshIntentMaxAttempts))
            {
                if (_lateRefreshIntentFromEngage)
                    _engageRefreshConsumed = true;

                ClearLateRefreshIntent();
                return false;
            }

            if (tick < _lateRefreshIntentReadyTick || _pulseActive)
                return true;

            if (!hasTarget)
            {
                if (tick < _lateRefreshIntentNextAttemptTick)
                    return true;

                if (NoTargetAnchorExhausted())
                {
                    ClearLateRefreshIntent();
                    return false;
                }

                if (PulseNoTargetAnchor(tick))
                    _lateRefreshIntentNextAttemptTick = tick + Math.Max(LateRefreshIntentRetryTicks, MsToTicks(NoTargetAnchorPulseMs));
                else
                    _lateRefreshIntentNextAttemptTick = tick + LateRefreshIntentRetryTicks;

                return true;
            }

            ClearNoTargetAnchorLimiter(true);
            _lateRefreshIntentNextAttemptTick = 0;
            TrySnap(target, tick);

            _pulseWasBuild = false;
            _pulseBuildTarget = 0;

            int refreshIntervalTicks = _lateRefreshIntentAttempts <= 0
                ? MsToTicks(lotdActive ? LotdBuildPulseMs : BuildPulseMs)
                : MsToTicks(MaintainPulseMs);

            if (PulseSiphon(tick, refreshIntervalTicks, GetLateRefreshDownTicks(lotdActive), true))
            {
                _lateRefreshIntentAttempts++;
                _lateRefreshIntentNextAttemptTick = tick + Math.Max(LateRefreshIntentRetryTicks, refreshIntervalTicks);

                uint acd = GetMonsterAcdId(target);
                if (acd != 0)
                    _lastSiphonTargetAcdId = acd;
            }

            return true;
        }

        private IMonster FindLateRefreshIntentTarget(int tick)
        {
            IMonster target = GetManualJuggerLockTarget();
            if (IsAliveTarget(target)) return target;

            if (RgAutoSnapSiphonAssist)
            {
                target = FindRiftGuardianTarget();
                if (IsAliveTarget(target)) return target;
            }

            target = GetManualSiphonTargetFallback();
            if (IsAliveTarget(target))
                return target;

            if (!BossAlive() || RgAutoSnapSiphonAssist)
            {
                target = AcquireTarget(tick);
                if (IsAliveTarget(target))
                    return target;

                try
                {
                    target = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters);
                    if (IsAliveTarget(target))
                        return target;
                }
                catch { }
            }

            return null;
        }

        private void ClearLateRefreshIntent()
        {
            _lateRefreshIntentActive = false;
            _lateRefreshIntentFromEngage = false;
            _lateRefreshIntentReadyTick = 0;
            _lateRefreshIntentExpireTick = 0;
            _lateRefreshIntentAttempts = 0;
            _lateRefreshIntentNextAttemptTick = 0;
            _lateRefreshIntentStartTimeLeft = 0f;
        }

        private int GetLateRefreshDownTicks(bool lotdActive)
        {
            return lotdActive ? LotdPulseDownTicks : LateRefreshDownTicks;
        }

        private bool TryGetPowerShift(out int stacksOut, out float timeLeftOut)
        {
            stacksOut = 0;
            timeLeftOut = 0f;

            try
            {
                var buff = Hud.Game.Me.Powers.GetBuff(_snoSiphonBlood);
                if (buff == null || !buff.Active)
                    return false;

                var counts = buff.IconCounts;
                var times = buff.TimeLeftSeconds;

                if (counts != null && counts.Length > PowerShiftBuffIconIndex && counts[PowerShiftBuffIconIndex] > 0)
                {
                    stacksOut = counts[PowerShiftBuffIconIndex];
                    if (times != null && times.Length > PowerShiftBuffIconIndex && times[PowerShiftBuffIconIndex] > 0d)
                        timeLeftOut = (float)times[PowerShiftBuffIconIndex];

                    return true;
                }

                if (counts != null)
                {
                    for (int i = 0; i < counts.Length; i++)
                    {
                        if (counts[i] > stacksOut)
                            stacksOut = counts[i];
                    }
                }

                double best = 0d;
                if (times != null)
                {
                    for (int i = 0; i < times.Length; i++)
                    {
                        double t = times[i];
                        if (t > 0d && (best <= 0d || t < best))
                            best = t;
                    }
                }

                timeLeftOut = (float)best;
                return stacksOut > 0;
            }
            catch { return false; }
        }

        private bool ShouldDoProactiveRefresh(int tick, uint currentTargetAcdId, bool targetChanged, bool newLanceEngagement, bool havePowerShift, int stacks, float timeLeft, bool lotdActive)
        {
            if (!LateRefreshAssist || !havePowerShift || stacks <= 0 || timeLeft <= 0f)
                return false;

            if ((timeLeft * 1000f) > ProactiveRefreshWindowMs)
                return false;

            if (newLanceEngagement)
            {
                if (_engageRefreshConsumed)
                    return false;

                QueueLateRefreshIntent(tick, true);
                return _lateRefreshIntentActive;
            }

            if (!_lateRefreshIntentActive && (timeLeft * 1000f) <= LateRefreshWindowMsDefault)
            {
                QueueLateRefreshIntent(tick, false);
                return _lateRefreshIntentActive;
            }

            if (targetChanged && currentTargetAcdId != 0 && _targetSwitchRefreshAcdId != currentTargetAcdId)
            {
                _targetSwitchRefreshAcdId = currentTargetAcdId;
                QueueLateRefreshIntent(tick, false);
                return _lateRefreshIntentActive;
            }

            return false;
        }

        private uint GetMonsterAcdId(IMonster monster)
        {
            try { return monster != null ? monster.AcdId : 0u; }
            catch { return 0u; }
        }

        private bool IsBuffActive(uint sno)
        {
            try { return Hud.Game.Me.Powers.BuffIsActive(sno); }
            catch { return false; }
        }

        private void ResetLateRefreshSinglePulseGate(bool havePowerShift, int stacks, float timeLeft)
        {
            if (!havePowerShift || stacks <= 0 || timeLeft <= 0f || (timeLeft * 1000f) > LateRefreshWindowMsDefault)
                _lateRefreshPulseConsumed = false;
        }

        private bool CorpsesAvailable(int tick)
        {
            if (_lastCorpseScanTick > 0 && tick - _lastCorpseScanTick < CorpseScanIntervalTicks)
                return _cachedCorpsesAvailable;

            _lastCorpseScanTick = tick;
            _cachedCorpsesAvailable = false;

            try
            {
                foreach (var actor in Hud.Game.Actors)
                {
                    if (actor == null || actor.SnoActor == null)
                        continue;

                    if (actor.SnoActor.Sno != ActorSnoEnum._p6_necro_corpse_flesh)
                        continue;

                    if (actor.CentralXyDistanceToMe <= CorpseScanRangeYards && actor.IsOnScreen)
                    {
                        _cachedCorpsesAvailable = true;
                        break;
                    }
                }
            }
            catch { }

            return _cachedCorpsesAvailable;
        }

        #endregion

        #region Targeting

        private IMonster AcquireTarget(int tick)
        {
            IMonster forced = GetManualJuggerLockTarget();
            if (IsAliveTarget(forced))
                return forced;

            if (BossAlive())
            {
                IMonster boss = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget() : null;
                if (IsAliveTarget(boss))
                    return boss;

                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
                _returnToRareAcdId = 0;
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
                return null;
            }

            // PickTarget owns the old LightningMod-style priority buckets.
            // Selected trash is never accepted before the full elite scan.
            IMonster picked = PickTarget(tick);
            if (IsAliveTarget(picked))
            {
                LockTarget(picked, tick);
                return picked;
            }

            if (AnyElitePackCandidateExists())
            {
                // Original LM fallback: when the active lock/hover enters dead space, recover toward
                // the nearest visible elite-like target instead of falling through to trash.
                IMonster preferred = null;
                try { preferred = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters); } catch { }
                if (IsAliveTarget(preferred) && (IsLeader(preferred) || IsEliteMinionLike(preferred)))
                {
                    LockTarget(preferred, tick);
                    return preferred;
                }
            }

            IMonster selected = null;
            try { selected = Hud.Game.SelectedMonster2; } catch { }

            if (IsBossLike(selected))
            {
                LockTarget(selected, tick);
                return selected;
            }

            if (tick >= _lockedTargetKeepUntilTick)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }
            return null;
        }


        private void ProcessJuggerHotkey(int tick)
        {
            if (!JuggerHotkeyEnabled || JuggerHotkeyVirtualKey == 0)
            {
                _juggerHotkeyWasDown = false;
                return;
            }

            bool down = IsKeyPhysicallyDown(JuggerHotkeyVirtualKey);
            if (down && !_juggerHotkeyWasDown)
                ToggleManualJuggerLock();

            _juggerHotkeyWasDown = down;
        }

        private void ToggleManualJuggerLock()
        {
            IMonster current = GetManualJuggerLockTarget();
            if (IsAliveTarget(current))
            {
                ClearManualJuggerLock();
                return;
            }

            IMonster jugg = FindClosestJuggernaut();
            if (!IsAliveTarget(jugg))
            {
                ClearManualJuggerLock();
                return;
            }

            _manualJuggerLockAcdId = GetMonsterAcdId(jugg);
            _lockedTargetAcdId = _manualJuggerLockAcdId;
            _lockedTargetKeepUntilTick = int.MaxValue / 2;
            ClearSoftHoverLocks();
        }

        private void ClearManualJuggerLock()
        {
            if (_lockedTargetAcdId == _manualJuggerLockAcdId)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }
            _manualJuggerLockAcdId = 0;
        }

        private IMonster GetManualJuggerLockTarget()
        {
            if (_manualJuggerLockAcdId == 0)
                return null;

            IMonster monster = FindAliveMonsterByAcdId(_manualJuggerLockAcdId);
            return IsAliveTarget(monster) && IsJuggernautPack(monster) ? monster : null;
        }

        private IMonster FindClosestJuggernaut()
        {
            IMonster best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster) || !IsJuggernautPack(monster) || IsIllusionOrClone(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d < bestDist)
                    {
                        best = monster;
                        bestDist = d;
                    }
                }
            }
            catch { }
            return best;
        }

        private IMonster FindRiftGuardianTarget()
        {
            try { return FindRiftGuardianTarget(Hud.Game.AliveMonsters); }
            catch { return null; }
        }

        private IMonster FindRiftGuardianTarget(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveTarget(monster) || !IsBossLike(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d < bestDist)
                    {
                        best = monster;
                        bestDist = d;
                    }
                }
            }
            catch { }
            return best;
        }

        private void ClearSoftHoverLocks()
        {
            _stableLockAcdId = 0;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
        }

        private void ClearAutosnapLockState()
        {
            _lockedTargetAcdId = 0;
            _lockedTargetKeepUntilTick = 0;
            _returnToRareAcdId = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _stableLockAcdId = 0;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
        }

        private IMonster PickTarget(int tick)
        {
            IEnumerable<IMonster> monsters = null;
            try { monsters = Hud.Game.AliveMonsters; } catch { }
            if (monsters == null)
                return null;

            IMonster forced = GetManualJuggerLockTarget();
            if (IsAliveTarget(forced))
                return forced;

            if (BossAlive())
            {
                IMonster boss = RgAutoSnapSiphonAssist ? FindRiftGuardianTarget(monsters) : null;
                return IsAliveTarget(boss) ? boss : null;
            }

            IMonster shieldingRare = GetShieldingRare(monsters);
            bool peelMode = IsAliveTarget(shieldingRare) && shieldingRare.CentralXyDistanceToMe <= PestiPick() && IsShieldingActive(shieldingRare);
            object peelPack = peelMode ? GetPackObject(shieldingRare) : null;
            if (peelMode)
                _returnToRareAcdId = shieldingRare.AcdId;

            // Original LM rule: a Juggernaut leader inside 10y disables cursor steering so the player
            // can kill it manually. AutoSiphon still runs through GetManualSiphonTargetFallback when the
            // cursor is actually hovering a valid target.

            IMonster returnRare = FindAliveMonsterByAcdId(_returnToRareAcdId);
            if (!peelMode && IsValidEligibleLeader(returnRare, tick) && !IsShieldingActive(returnRare))
                return returnRare;

            IMonster bestLeaderClose = null;
            IMonster bestLeaderAny = null;
            IMonster bestDebuffedLeaderClose = null;
            IMonster bestDebuffedLeaderAny = null;
            IMonster bestPeelMinion = null;
            IMonster bestMinionClose = null;
            IMonster bestTrashClose = null;
            IMonster bestNearestAny = null;
            IMonster bestOnScreenAny = null;

            float bestLeaderCloseScore = float.MaxValue;
            float bestLeaderAnyDist = float.MaxValue;
            float bestDebuffedLeaderCloseScore = float.MaxValue;
            float bestDebuffedLeaderAnyDist = float.MaxValue;
            float bestPeelMinionDist = float.MaxValue;
            float bestMinionCloseDist = float.MaxValue;
            float bestTrashCloseScore = float.MaxValue;
            float bestNearestAnyDist = float.MaxValue;
            float bestOnScreenAnyDist = float.MaxValue;
            bool anyLeader = false;
            bool anyElitePackMember = false;

            IMonster selectedLeader = null;
            try { selectedLeader = Hud.Game.SelectedMonster2; } catch { }
            if (IsValidEligibleLeader(selectedLeader, tick) && SafeDistance(selectedLeader) <= PestiPick())
            {
                float selectedScore = GetCloseBucketScore(SafeDistance(selectedLeader));
                bestLeaderClose = selectedLeader;
                bestLeaderCloseScore = selectedScore;
                bestLeaderAny = selectedLeader;
                bestLeaderAnyDist = SafeDistance(selectedLeader);
                if (PrioritizeDebuffedElites && HasAnySiphonDebuff(selectedLeader))
                {
                    bestDebuffedLeaderClose = selectedLeader;
                    bestDebuffedLeaderCloseScore = selectedScore;
                    bestDebuffedLeaderAny = selectedLeader;
                    bestDebuffedLeaderAnyDist = SafeDistance(selectedLeader);
                }
                anyLeader = true;
            }

            foreach (var monster in monsters)
            {
                if (!IsAliveForScan(monster))
                    continue;

                if (monster.Rarity == ActorRarity.Boss)
                    continue;

                float distance = SafeDistance(monster);
                bool onScreen = SafeIsOnScreen(monster);
                if (!onScreen && distance > SnapRangeYards)
                    continue;

                // Record elite-pack presence before later eligibility filters. This is the critical LM behavior:
                // a temporarily invalid elite/minion must block trash fallback instead of letting trash steal aim.
                if (IsElitePackCandidate(monster))
                    anyElitePackMember = true;

                if (IsSkipped(monster, tick))
                {
                    if (IsLeader(monster) && !IsIllusionOrClone(monster))
                        anyLeader = true;
                    continue;
                }

                // Ported ordering from the original LightningMod selector:
                // record live elite leaders before filtering invulnerable/jugger/shield states.
                // Otherwise a temporarily invalid elite makes anyLeader=false and trash can steal the cursor.
                if (IsLeader(monster))
                {
                    if (IsIllusionOrClone(monster))
                        continue;

                    if (IsJuggernautPack(monster))
                        continue;

                    anyLeader = true;

                    if (!onScreen || IsShieldingActive(monster) || IsInvulnerable(monster))
                        continue;

                    if (distance < bestNearestAnyDist)
                    {
                        bestNearestAnyDist = distance;
                        bestNearestAny = monster;
                    }

                    if (onScreen && distance < bestOnScreenAnyDist)
                    {
                        bestOnScreenAnyDist = distance;
                        bestOnScreenAny = monster;
                    }

                    float closeScore = GetCloseBucketScore(distance);
                    if (distance <= PestiPick())
                    {
                        if (closeScore < bestLeaderCloseScore)
                        {
                            bestLeaderCloseScore = closeScore;
                            bestLeaderClose = monster;
                        }

                        if (PrioritizeDebuffedElites && HasAnySiphonDebuff(monster) && closeScore < bestDebuffedLeaderCloseScore)
                        {
                            bestDebuffedLeaderCloseScore = closeScore;
                            bestDebuffedLeaderClose = monster;
                        }
                    }

                    if (distance < bestLeaderAnyDist)
                    {
                        bestLeaderAnyDist = distance;
                        bestLeaderAny = monster;
                    }

                    if (PrioritizeDebuffedElites && HasAnySiphonDebuff(monster) && distance < bestDebuffedLeaderAnyDist)
                    {
                        bestDebuffedLeaderAnyDist = distance;
                        bestDebuffedLeaderAny = monster;
                    }

                    continue;
                }

                if (IsInvulnerable(monster) || IsIllusionOrClone(monster) || IsJuggernautPack(monster))
                    continue;

                if (onScreen && distance < bestOnScreenAnyDist)
                {
                    bestOnScreenAnyDist = distance;
                    bestOnScreenAny = monster;
                }

                if (IsEliteMinionLike(monster) && distance < bestNearestAnyDist)
                {
                    bestNearestAnyDist = distance;
                    bestNearestAny = monster;
                }

                if (IsEliteMinionLike(monster))
                {
                    if (peelMode && SamePack(monster, peelPack) && distance < bestPeelMinionDist)
                    {
                        bestPeelMinionDist = distance;
                        bestPeelMinion = monster;
                    }

                    if (distance <= PestiPick() && onScreen && distance < bestMinionCloseDist)
                    {
                        bestMinionCloseDist = distance;
                        bestMinionClose = monster;
                    }

                    continue;
                }

                if (IncludeTrashTargets && onScreen)
                {
                    float bonus = HitboxRangeBonus(monster);
                    if (distance <= PestiPick() + bonus)
                    {
                        float score = GetTrashPriorityScore(monster, distance);
                        if (score < bestTrashCloseScore)
                        {
                            bestTrashCloseScore = score;
                            bestTrashClose = monster;
                        }
                    }
                }
            }

            if (peelMode)
            {
                if (bestLeaderClose != null && !SameMonster(bestLeaderClose, shieldingRare))
                    return bestLeaderClose;

                if (bestPeelMinion != null) return bestPeelMinion;
                if (bestMinionClose != null) return bestMinionClose;
                if (bestTrashClose != null) return bestTrashClose;
                return null;
            }

            if (bestLeaderClose != null)
            {
                IMonster preferredClose = ChoosePositionFirstLeader(bestLeaderClose, bestLeaderCloseScore, bestDebuffedLeaderClose, bestDebuffedLeaderCloseScore);
                IMonster locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsValidEligibleLeader(locked, tick) && SafeDistance(locked) <= PestiPick())
                {
                    float lockedScore = GetCloseBucketScore(SafeDistance(locked));
                    if (PrioritizeDebuffedElites && bestDebuffedLeaderClose != null && !HasAnySiphonDebuff(locked) && bestDebuffedLeaderCloseScore <= lockedScore + 2.0f)
                        return bestDebuffedLeaderClose;

                    float preferredScore = preferredClose == bestDebuffedLeaderClose ? bestDebuffedLeaderCloseScore : bestLeaderCloseScore;
                    return lockedScore - preferredScore > 0.75f ? preferredClose : locked;
                }

                return preferredClose;
            }

            if (bestLeaderAny != null)
            {
                IMonster preferredAny = ChoosePositionFirstLeader(bestLeaderAny, bestLeaderAnyDist, bestDebuffedLeaderAny, bestDebuffedLeaderAnyDist);
                IMonster locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsValidEligibleLeader(locked, tick))
                {
                    float lockedDist = SafeDistance(locked);
                    if (PrioritizeDebuffedElites && bestDebuffedLeaderAny != null && !HasAnySiphonDebuff(locked) && bestDebuffedLeaderAnyDist <= lockedDist + 3.0f)
                        return bestDebuffedLeaderAny;

                    float preferredDist = preferredAny == bestDebuffedLeaderAny ? bestDebuffedLeaderAnyDist : bestLeaderAnyDist;
                    return lockedDist - preferredDist > 3f ? preferredAny : locked;
                }

                return preferredAny;
            }

            if (anyLeader)
                return null;

            if (bestMinionClose != null) return bestMinionClose;

            // Strong elite-pack guard: minions/elite-pack members still outrank all trash.
            // This deliberately prevents the FreeHUD port from fighting trash before an elite pack is resolved.
            if (anyElitePackMember)
                return bestNearestAny;

            IMonster shockTower = PickShockTower(monsters);
            if (shockTower != null) return shockTower;

            if (IncludeTrashTargets)
            {
                // Old LM-style policy restored: after elites/minions are gone,
                // kill high-progression trash before filler trash.
                IMonster highTrash = PickHighValueTrash(monsters, false);
                if (highTrash != null)
                    return highTrash;

                IMonster mediumTrash = PickHighValueTrash(monsters, true);
                if (mediumTrash != null)
                    return mediumTrash;

                if (bestTrashClose != null) return bestTrashClose;
                if (bestOnScreenAny != null) return bestOnScreenAny;
            }

            return bestNearestAny;
        }

        private bool IsEligibleSelectedTarget(IMonster monster)
        {
            if (!IsAliveTarget(monster))
                return false;

            if (IsBossLike(monster))
                return true;

            if (IsJuggernautPack(monster))
                return SameMonster(monster, GetManualJuggerLockTarget());

            if (IsShieldingActive(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets && SafeDistance(monster) <= PestiPick() + HitboxRangeBonus(monster);
        }

        private bool IsTargetEligibleForLock(IMonster monster, int tick)
        {
            if (!IsAliveTarget(monster) || IsSkipped(monster, tick))
                return false;

            if (IsBossLike(monster))
                return true;

            if (IsJuggernautPack(monster))
                return SameMonster(monster, GetManualJuggerLockTarget());

            if (IsShieldingActive(monster) || IsInvulnerable(monster) || IsIllusionOrClone(monster))
                return false;

            if (IsLeader(monster) || IsEliteMinionLike(monster))
                return true;

            return IncludeTrashTargets && SafeDistance(monster) <= PestiPick() + HitboxRangeBonus(monster);
        }

        private void LockTarget(IMonster monster, int tick)
        {
            if (monster == null)
                return;

            uint acd = 0;
            try { acd = monster.AcdId; } catch { }

            // Match the old Pestilence behavior: persist leader locks only.
            // Trash fallback may be aimed at, but must never become a sticky lock that competes with elites later.
            if (IsLeader(monster) && (!IsJuggernautPack(monster) || acd == _manualJuggerLockAcdId) && acd != 0)
            {
                _lockedTargetAcdId = acd;
                _lockedTargetKeepUntilTick = Math.Max(_lockedTargetKeepUntilTick, tick + LockedTargetKeepTicks);
            }
            else if (tick >= _lockedTargetKeepUntilTick)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
            }
        }

        private IMonster FindAliveMonsterByAcdId(uint acdId)
        {
            if (acdId == 0)
                return null;

            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster != null && monster.AcdId == acdId && monster.IsAlive)
                        return monster;
                }
            }
            catch { }

            return null;
        }

        private void ClearDeadTargetState()
        {
            if (_lockedTargetAcdId != 0 && FindAliveMonsterByAcdId(_lockedTargetAcdId) == null)
            {
                _lockedTargetAcdId = 0;
                _lockedTargetKeepUntilTick = 0;
            }

            if (_manualJuggerLockAcdId != 0 && GetManualJuggerLockTarget() == null)
                ClearManualJuggerLock();

            if (_returnToRareAcdId != 0 && FindAliveMonsterByAcdId(_returnToRareAcdId) == null)
                _returnToRareAcdId = 0;

            if (_snapPhaseAcdId != 0 && FindAliveMonsterByAcdId(_snapPhaseAcdId) == null)
            {
                _snapPhaseAcdId = 0;
                _snapPhase = 0;
            }

            if (_stableLockAcdId != 0 && FindAliveMonsterByAcdId(_stableLockAcdId) == null)
            {
                _stableLockAcdId = 0;
                _stableLockUntilTick = 0;
            }

            if (_softLockAcdId != 0 && FindAliveMonsterByAcdId(_softLockAcdId) == null)
            {
                _softLockAcdId = 0;
                _softLockUntilTick = 0;
            }

            if (_cachedHoverAcdId != 0 && FindAliveMonsterByAcdId(_cachedHoverAcdId) == null)
            {
                _cachedHoverAcdId = 0;
                _cachedHoverUntilTick = 0;
                _cachedHoverTryUntilTick = 0;
            }

            if (_lastHoverAcdId != 0 && FindAliveMonsterByAcdId(_lastHoverAcdId) == null)
            {
                _lastHoverAcdId = 0;
                _lastHoverTick = 0;
            }

            if (_reacquireAcdId != 0 && FindAliveMonsterByAcdId(_reacquireAcdId) == null)
            {
                _reacquireAcdId = 0;
                _reacquireUntilTick = 0;
            }

            if (_alternateScanAcdId != 0 && FindAliveMonsterByAcdId(_alternateScanAcdId) == null)
            {
                _alternateScanAcdId = 0;
                _alternateScanUntilTick = 0;
            }
        }

        private bool BossAlive()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster != null && monster.IsAlive && IsBossLike(monster))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool AnyEliteOrMinionCandidateExists()
        {
            return AnyElitePackCandidateExists();
        }

        private bool AnyElitePackCandidateExists()
        {
            try
            {
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (!IsAliveForScan(monster))
                        continue;

                    if (monster.Rarity == ActorRarity.Boss || IsIllusionOrClone(monster))
                        continue;

                    if (!IsElitePackCandidate(monster))
                        continue;

                    if (IsLeader(monster) && IsJuggernautPack(monster))
                        continue;

                    if (SafeIsOnScreen(monster) || SafeDistance(monster) <= SnapRangeYards)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private IMonster FindNearestOnScreenPreferred(IEnumerable<IMonster> monsters)
        {
            if (monsters == null) return null;

            IMonster bestLeader = null;
            float bestLeaderDist = float.MaxValue;
            IMonster bestMinion = null;
            float bestMinionDist = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveForScan(monster) || !SafeIsOnScreen(monster)) continue;
                    if (monster.Rarity == ActorRarity.Boss) continue;
                    if (IsInvulnerable(monster) || IsJuggernautPack(monster) || IsIllusionOrClone(monster)) continue;

                    float d = SafeDistance(monster);
                    if (IsLeader(monster))
                    {
                        if (d < bestLeaderDist) { bestLeaderDist = d; bestLeader = monster; }
                    }
                    else if (IsEliteMinionLike(monster))
                    {
                        if (d < bestMinionDist) { bestMinionDist = d; bestMinion = monster; }
                    }
                }
            }
            catch { }

            return bestLeader ?? bestMinion;
        }

        private IMonster GetShieldingRare(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestDist = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveForScan(monster) || !IsLeader(monster) || monster.Rarity != ActorRarity.Rare)
                        continue;

                    if (IsIllusionOrClone(monster) || IsJuggernautPack(monster) || IsInvulnerable(monster) || !IsShieldingActive(monster))
                        continue;

                    float d = SafeDistance(monster);
                    if (d <= PestiPick() && d < bestDist)
                    {
                        bestDist = d;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }

        private double GetMonsterRiftProgression(IMonster monster)
        {
            try { return monster != null && monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0d; }
            catch { return 0d; }
        }

        private double GetBigTrashProgressionScore(float distance, double progression)
        {
            // High-value trash can win over nearby filler, but do not over-chase the edge of the ring.
            if (distance <= TrashPenaltyStart)
                return progression;

            float denom = Math.Max(0.1f, PestiPick() - TrashPenaltyStart);
            float t = (distance - TrashPenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            return progression - (t * 0.12d);
        }

        private IMonster PickHighValueTrash(IEnumerable<IMonster> monsters, bool includeMedium)
        {
            if (monsters == null)
                return null;

            IMonster best = null;
            double bestScore = double.NegativeInfinity;
            float bestRadius = -1f;
            float bestDistanceScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveTarget(monster))
                        continue;

                    if (IsLeader(monster) || IsEliteMinionLike(monster))
                        continue;

                    if (IsInvulnerable(monster) || IsIllusionOrClone(monster) || IsJuggernautPack(monster))
                        continue;

                    if (IsShockTowerThreat(monster))
                        continue;

                    float distance = SafeDistance(monster);
                    float radius = 0f;
                    try { radius = monster.RadiusBottom; } catch { }

                    float bonus = HitboxRangeBonus(monster);
                    if (distance > PestiPick() + bonus)
                        continue;

                    double progression = GetMonsterRiftProgression(monster);

                    bool highValue = progression >= HighValueTrashMinRiftProgression;
                    bool mediumValue = includeMedium && progression >= MediumValueTrashMinRiftProgression && distance <= MediumValueTrashPreferRangeYards;

                    if (!highValue && !mediumValue)
                        continue;

                    double score = GetBigTrashProgressionScore(distance, progression);

                    // Medium-value trash is useful, but it must not beat true 0.85+ big trash.
                    if (!highValue)
                        score -= 0.18d;

                    float distanceScore = GetTrashCloseBucketScore(distance);

                    bool better =
                        score > bestScore + 0.02d
                        || (Math.Abs(score - bestScore) <= 0.02d && radius > bestRadius + 0.20f)
                        || (Math.Abs(score - bestScore) <= 0.02d && Math.Abs(radius - bestRadius) <= 0.20f && distanceScore < bestDistanceScore);

                    if (better)
                    {
                        best = monster;
                        bestScore = score;
                        bestRadius = radius;
                        bestDistanceScore = distanceScore;
                    }
                }
            }
            catch { }

            return best;
        }

        private IMonster PickShockTower(IEnumerable<IMonster> monsters)
        {
            IMonster best = null;
            float bestScore = float.MaxValue;

            try
            {
                foreach (var monster in monsters)
                {
                    if (!IsAliveTarget(monster) || IsLeader(monster) || IsEliteMinionLike(monster) || IsInvulnerable(monster))
                        continue;

                    if (!IsShockTowerThreat(monster))
                        continue;

                    float distance = SafeDistance(monster);
                    if (distance > PestiPick() + HitboxRangeBonus(monster))
                        continue;

                    float score = GetTrashCloseBucketScore(distance) - 4.0f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = monster;
                    }
                }
            }
            catch { }

            return best;
        }


        #endregion

        private void UpdatePlayerMotionState(int tick)
        {
            try
            {
                var me = Hud.Game.Me;
                if (me == null || me.FloorCoordinate == null)
                    return;

                float x = me.FloorCoordinate.X;
                float y = me.FloorCoordinate.Y;

                if (_haveLastMePos)
                {
                    float dx = x - _lastMeX;
                    float dy = y - _lastMeY;
                    if (dx * dx + dy * dy >= TeleportThresholdSq)
                    {
                        _teleportDetectedTick = tick;
                        if (_lockedTargetAcdId != 0)
                        {
                            _reacquireAcdId = _lockedTargetAcdId;
                            _reacquireUntilTick = Math.Max(_reacquireUntilTick, tick + PostTeleportForceSnapTicks);
                        }
                    }
                }

                _lastMeX = x;
                _lastMeY = y;
                _haveLastMePos = true;
            }
            catch { }
        }

        private void UpdatePassiveEliteHoverCache(int tick)
        {
            try
            {
                var hovered = Hud.Game.SelectedMonster2;
                if (!IsAliveTarget(hovered) || (!IsLeader(hovered) && !IsEliteMinionLike(hovered)))
                    return;

                if (IsInvulnerable(hovered) || IsIllusionOrClone(hovered))
                    return;

                uint acd = hovered.AcdId;
                _lastHoverAcdId = acd;
                _lastHoverTick = tick;
                _cachedHoverAcdId = acd;
                _cachedHoverUntilTick = Math.Max(_cachedHoverUntilTick, tick + 120);
                _cachedHoverTryUntilTick = Math.Max(_cachedHoverTryUntilTick, tick + 8);
                _stableLockAcdId = acd;
                _stableLockUntilTick = Math.Max(_stableLockUntilTick, tick + 8);
                _softLockAcdId = acd;
                _softLockDx = 0f;
                _softLockDy = 0f;
                _softLockUntilTick = Math.Max(_softLockUntilTick, tick + 10);
                LockTarget(hovered, tick);
            }
            catch { }
        }

        private bool IsForceMoving()
        {
            try
            {
                // FreeHUD does not expose LightningMod's continuous Move state. This catches the common case
                // where the force-move ActionKey is bound to one of the normal skill/mouse action keys.
                if (IsKeyPhysicallyDown(0x20) && !(JuggerHotkeyEnabled && JuggerHotkeyVirtualKey == 0x20)) return true; // Space is a common force-move bind.
            }
            catch { }
            return false;
        }

        #region Target Helpers

        private float PestiPick()
        {
            return ClampRange(PestilenceRangeYards);
        }

        private bool IsAliveForScan(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;

                if (!monster.Attackable && !monster.IsOnScreen)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private bool IsAliveTarget(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive || !monster.IsOnScreen)
                    return false;

                if (monster.Illusion || monster.Invulnerable || monster.Untargetable || monster.Invisible || monster.Hidden || monster.Stealthed)
                    return false;

                if (!monster.Attackable)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private bool IsValidEligibleLeader(IMonster monster, int tick)
        {
            if (!IsAliveTarget(monster) || IsSkipped(monster, tick))
                return false;

            return IsLeader(monster) && !IsIllusionOrClone(monster) && !IsJuggernautPack(monster) && !IsShieldingActive(monster) && !IsInvulnerable(monster);
        }

        private bool IsLeader(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                // Original LM leader logic was Champion/Rare. Keep Unique as a FreeHUD safety net,
                // but boss encounters are handled separately to preserve manual RG control.
                return monster.Rarity == ActorRarity.Rare || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique;
            }
            catch { return false; }
        }

        private bool IsRareMinion(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.Rarity == ActorRarity.RareMinion)
                    return true;

                string rarity = monster.Rarity.ToString();
                if (rarity.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0 && monster.Rarity != ActorRarity.Boss)
                    return true;

                string name = monster.SnoMonster != null ? monster.SnoMonster.NameEnglish : string.Empty;
                return !string.IsNullOrEmpty(name) && name.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0 && monster.Rarity != ActorRarity.Boss;
            }
            catch { return false; }
        }

        private bool IsEliteMinionLike(IMonster monster)
        {
            if (monster == null || IsLeader(monster))
                return false;

            try { if (IsRareMinion(monster)) return true; } catch { }
            try { if (monster.IsElite) return true; } catch { }

            bool b;
            if (TryGetBool(monster, "IsElite", out b) && b) return true;

            var affixes = GetAffixList(GetPackObject(monster));
            if (affixes == null) return false;

            try
            {
                foreach (var ignored in affixes)
                    return true;
            }
            catch { }

            return false;
        }

        private bool IsElitePackCandidate(IMonster monster)
        {
            if (monster == null) return false;
            try
            {
                if (IsLeader(monster) || IsEliteMinionLike(monster)) return true;
                if (monster.IsElite && monster.Rarity != ActorRarity.Boss) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsElite", out b) && b) return true;
            return false;
        }

        private bool IsBossLike(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (!monster.IsAlive)
                    return false;

                if (monster.Rarity == ActorRarity.Boss)
                    return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsBoss", out b) && b) return true;
            if (TryGetBool(monster, "Boss", out b) && b) return true;
            if (TryGetBool(monster, "IsRiftGuardian", out b) && b) return true;
            if (TryGetBool(monster, "RiftGuardian", out b) && b) return true;

            try
            {
                var snoMonster = monster.SnoMonster;
                if (snoMonster != null)
                {
                    if (TryGetBool(snoMonster, "IsBoss", out b) && b) return true;
                    if (TryGetBool(snoMonster, "Boss", out b) && b) return true;
                    if (TryGetBool(snoMonster, "IsRiftGuardian", out b) && b) return true;
                    if (TryGetBool(snoMonster, "RiftGuardian", out b) && b) return true;
                }
            }
            catch { }

            return false;
        }

        private bool IsInvulnerable(IMonster monster)
        {
            if (monster == null) return true;
            try
            {
                if (!monster.IsAlive) return true;
                if (monster.Invulnerable || monster.Untargetable || monster.Invisible || monster.Hidden || monster.Stealthed) return true;
                if (monster.IsOnScreen && !monster.Attackable) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "Invulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsInvulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsShielded", out b) && b) return true;
            if (TryGetBool(monster, "Shielded", out b) && b) return true;
            if (TryGetBool(monster, "HasShield", out b) && b) return true;
            if (TryGetBool(monster, "Untargetable", out b) && b) return true;
            if (TryGetBool(monster, "IsUntargetable", out b) && b) return true;
            if (TryGetBool(monster, "IsImmune", out b) && b) return true;
            return false;
        }

        private bool IsIllusionOrClone(IMonster monster)
        {
            if (monster == null) return false;

            try { if (monster.Illusion) return true; } catch { }

            bool b;
            if (TryGetBool(monster, "IsIllusion", out b) && b) return true;
            if (TryGetBool(monster, "Illusion", out b) && b) return true;
            if (TryGetBool(monster, "IsClone", out b) && b) return true;
            if (TryGetBool(monster, "Clone", out b) && b) return true;
            return false;
        }

        private bool IsShieldingActive(IMonster monster)
        {
            if (monster == null || !HasAffix(monster, MonsterAffix.Shielding)) return false;

            try
            {
                if (monster.Invulnerable || monster.Untargetable) return true;
                if (monster.IsOnScreen && !monster.Attackable) return true;
            }
            catch { }

            bool b;
            if (TryGetBool(monster, "IsShielding", out b) && b) return true;
            if (TryGetBool(monster, "Shielding", out b) && b) return true;
            if (TryGetBool(monster, "ShieldingActive", out b) && b) return true;
            if (TryGetBool(monster, "HasShielding", out b) && b) return true;
            if (TryGetBool(monster, "IsShielded", out b) && b) return true;
            if (TryGetBool(monster, "Shielded", out b) && b) return true;
            if (TryGetBool(monster, "HasShield", out b) && b) return true;
            if (TryGetBool(monster, "Invulnerable", out b) && b) return true;
            if (TryGetBool(monster, "IsInvulnerable", out b) && b) return true;
            return IsInvulnerable(monster);
        }

        private bool IsJuggernautPack(IMonster monster)
        {
            // Treat Juggernaut as a leader-only autosnap exclusion. FreeHUD pack-affix
            // fallback can make minions look like Juggernauts too; skipping those minions
            // is what made the plugin stand down around Jugger packs.
            return IsLeader(monster) && HasAffix(monster, MonsterAffix.Juggernaut);
        }

        private bool HasAffix(IMonster monster, MonsterAffix affix)
        {
            if (monster == null) return false;

            try
            {
                var direct = monster.AffixSnoList;
                if (direct != null)
                {
                    foreach (var a in direct)
                    {
                        if (a == null) continue;
                        try { if (a.Affix == affix) return true; } catch { }
                        try { if ((int)a.Id == (int)affix) return true; } catch { }
                        try
                        {
                            var name = a.NameEnglish;
                            if (!string.IsNullOrEmpty(name) && string.Equals(name, affix.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                var list = GetAffixList(GetPackObject(monster));
                if (list == null) return false;

                foreach (var a in list)
                {
                    if (a == null) continue;
                    if (a is MonsterAffix && (MonsterAffix)a == affix) return true;
                    if (a is int && (int)a == (int)affix) return true;
                    if (a is uint && (uint)a == (uint)(int)affix) return true;
                    var snoAffix = a as ISnoMonsterAffix;
                    if (snoAffix != null)
                    {
                        try { if (snoAffix.Affix == affix) return true; } catch { }
                        try { if ((int)snoAffix.Id == (int)affix) return true; } catch { }
                    }
                    if (string.Equals(a.ToString(), affix.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            return false;
        }

        private bool HasAnySiphonDebuff(IMonster monster)
        {
            try
            {
                if (monster == null || !monster.IsAlive)
                    return false;

                return monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_9_Visual_Effect_D, _snoSiphonBlood, 0) == 1
                    || monster.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_11_Visual_Effect_D, _snoSiphonBlood, 0) == 1;
            }
            catch { return false; }
        }

        private bool IsShockTowerThreat(IMonster monster)
        {
            if (monster == null)
                return false;

            try
            {
                if (monster.SnoActor != null && monster.SnoActor.Sno == ActorSnoEnum._x1_pand_ext_ordnance_tower_shock_a)
                    return true;
            }
            catch { }

            try
            {
                string name = monster.SnoActor != null ? monster.SnoActor.NameEnglish : string.Empty;
                if (string.IsNullOrEmpty(name) && monster.SnoMonster != null)
                    name = monster.SnoMonster.NameEnglish;

                return !string.IsNullOrEmpty(name)
                    && name.IndexOf("shock", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool SameMonster(IMonster a, IMonster b)
        {
            if (a == null || b == null)
                return false;

            try { return a.AcdId != 0 && a.AcdId == b.AcdId; }
            catch { return object.ReferenceEquals(a, b); }
        }

        private object GetPackObject(IMonster monster)
        {
            if (monster == null) return null;

            try
            {
                var nativePack = monster.Pack;
                if (nativePack != null) return nativePack;
            }
            catch { }

            try
            {
                var mt = monster.GetType();
                PropertyInfo pi;
                if (!PackPropCache.TryGetValue(mt, out pi))
                {
                    pi = null;
                    foreach (var name in PackPropNames)
                    {
                        pi = mt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    PackPropCache[mt] = pi;
                }
                return pi == null ? null : pi.GetValue(monster, null);
            }
            catch { return null; }
        }

        private int GetPackId(object pack)
        {
            if (pack == null) return 0;
            try
            {
                var pt = pack.GetType();
                PropertyInfo pi;
                if (!PackIdPropCache.TryGetValue(pt, out pi))
                {
                    pi = null;
                    foreach (var name in PackIdPropNames)
                    {
                        pi = pt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    PackIdPropCache[pt] = pi;
                }

                var v = pi == null ? null : pi.GetValue(pack, null);
                if (v == null) return 0;
                if (v is int) return (int)v;
                if (v is uint) return unchecked((int)(uint)v);
                if (v is long) return (int)(long)v;
                int parsed;
                return int.TryParse(v.ToString(), out parsed) ? parsed : 0;
            }
            catch { return 0; }
        }

        private bool SamePack(IMonster monster, object pack)
        {
            if (monster == null || pack == null) return false;
            var mp = GetPackObject(monster);
            if (mp == null) return false;
            if (ReferenceEquals(mp, pack)) return true;
            try { if (mp.Equals(pack) || pack.Equals(mp)) return true; } catch { }
            int a = GetPackId(mp);
            int b = GetPackId(pack);
            return a != 0 && a == b;
        }

        private IEnumerable GetAffixList(object pack)
        {
            if (pack == null) return null;

            try
            {
                var nativePack = pack as IMonsterPack;
                if (nativePack != null) return nativePack.AffixSnoList;
            }
            catch { }

            try
            {
                var pt = pack.GetType();
                PropertyInfo pi;
                if (!AffixListPropCache.TryGetValue(pt, out pi))
                {
                    pi = null;
                    foreach (var name in AffixPropNames)
                    {
                        pi = pt.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pi != null) break;
                    }
                    AffixListPropCache[pt] = pi;
                }
                return pi == null ? null : pi.GetValue(pack, null) as IEnumerable;
            }
            catch { return null; }
        }

        private bool TryGetBool(object obj, string name, out bool value)
        {
            value = false;
            if (obj == null || string.IsNullOrEmpty(name)) return false;
            try
            {
                var pi = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi == null || pi.PropertyType != typeof(bool)) return false;
                value = (bool)pi.GetValue(obj, null);
                return true;
            }
            catch { return false; }
        }

        private float SafeDistance(IMonster monster)
        {
            try { return monster == null ? float.MaxValue : (float)monster.CentralXyDistanceToMe; }
            catch { return float.MaxValue; }
        }

        private bool SafeIsOnScreen(IMonster monster)
        {
            try { return monster != null && monster.IsOnScreen; }
            catch { return false; }
        }

        private bool IsSkipped(IMonster monster, int tick)
        {
            try { return monster != null && _skipAcdId != 0 && monster.AcdId == _skipAcdId && tick < _skipUntilTick; }
            catch { return false; }
        }

        private float HitboxRangeBonus(IMonster monster)
        {
            try
            {
                float radius = monster.RadiusBottom;
                if (radius <= 0f)
                    radius = BaselineRadiusBottom;

                return Math.Min((radius / BaselineRadiusBottom) * BaselineHitboxYards, HitboxBonusCap);
            }
            catch { return BaselineHitboxYards; }
        }

        private float GetCloseBucketScore(float distance)
        {
            if (distance <= PestilenceEdgePenaltyStart)
                return distance;

            float denom = Math.Max(0.1f, PestiPick() - PestilenceEdgePenaltyStart);
            float t = (distance - PestilenceEdgePenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return distance + (t * PestilenceEdgePenaltyMax);
        }

        private float GetTrashCloseBucketScore(float distance)
        {
            if (distance <= TrashPenaltyStart)
                return distance;

            float denom = Math.Max(0.1f, PestiPick() - TrashPenaltyStart);
            float t = (distance - TrashPenaltyStart) / denom;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return distance + (t * TrashEdgePenaltyMax);
        }

        private IMonster ChoosePositionFirstLeader(IMonster nearest, float nearestScore, IMonster debuffed, float debuffedScore)
        {
            if (!PrioritizeDebuffedElites || debuffed == null)
                return nearest;

            if (nearest == null || debuffedScore <= nearestScore + 2.0f)
                return debuffed;

            return nearest;
        }

        private float GetTrashPriorityScore(IMonster monster, float distance)
        {
            // Normal trash fallback should mostly mean "closest to me".
            // High/medium progression trash is handled by PickHighValueTrash().
            float score = GetTrashCloseBucketScore(distance);

            float radius = 0f;
            try { radius = monster.RadiusBottom; } catch { }
            if (radius > BaselineRadiusBottom)
                score -= Math.Min(0.35f, (radius - BaselineRadiusBottom) * 0.12f);

            return score;
        }


        #endregion

        #region Aiming

        private void AimAt(IMonster monster, int tick)
        {
            TrySnap(monster, tick);
        }

        private bool TryAcquireHoverForAutoSiphon(int tick, out IMonster target)
        {
            target = null;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                if (IsAliveTarget(selected) && !IsInvulnerable(selected) && !IsIllusionOrClone(selected))
                {
                    if (IsBossLike(selected) || IsLeader(selected) || IsEliteMinionLike(selected) || IncludeTrashTargets)
                    {
                        target = selected;
                        if (!IsJuggernautPack(selected))
                            LockTarget(selected, tick);
                        return true;
                    }
                }
            }
            catch { }

            if (_lockedTargetAcdId != 0)
            {
                var locked = FindAliveMonsterByAcdId(_lockedTargetAcdId);
                if (IsAliveTarget(locked) && !IsJuggernautPack(locked) && !IsInvulnerable(locked) && !IsIllusionOrClone(locked))
                {
                    target = locked;
                    TrySnap(locked, tick);
                    return true;
                }
            }

            if (_reacquireAcdId != 0 && tick < _reacquireUntilTick)
            {
                var reacquire = FindAliveMonsterByAcdId(_reacquireAcdId);
                if (IsAliveTarget(reacquire) && !IsJuggernautPack(reacquire) && !IsInvulnerable(reacquire) && !IsIllusionOrClone(reacquire))
                {
                    target = reacquire;
                    TrySnap(reacquire, tick);
                    return true;
                }
            }

            var preferred = FindNearestOnScreenPreferred(Hud.Game.AliveMonsters);
            if (IsAliveTarget(preferred))
            {
                target = preferred;
                if (IsLeader(preferred) || IsEliteMinionLike(preferred))
                    TrySnap(preferred, tick);
                return true;
            }

            return false;
        }


        private bool IsActuallySelectedTarget(uint acd)
        {
            if (acd == 0)
                return false;

            try
            {
                var selected = Hud.Game.SelectedMonster2;
                return selected != null && selected.IsAlive && selected.AcdId == acd;
            }
            catch { return false; }
        }

        private bool TryMoveCachedHover(uint acd, float x, float y, int tick, bool force)
        {
            if (acd == 0)
                return false;

            if (_cachedHoverAcdId == acd && tick < _cachedHoverUntilTick)
            {
                float px = x + _cachedHoverDx;
                float py = y + _cachedHoverDy;
                if (IsHardSafeScreenTarget(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    return true;
                }
            }

            if (force && _stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                float px = x + _stableLockDx;
                float py = y + _stableLockDy;
                if (IsHardSafeScreenTarget(px, py) && SafeMouseMove(px, py, tick))
                {
                    _lastMouseMoveTick = tick;
                    return true;
                }
            }

            return false;
        }
        private void TrySnap(IMonster monster, int tick)
        {
            if (monster == null || !monster.IsAlive || !monster.IsOnScreen)
                return;

            uint acd = 0;
            try { acd = monster.AcdId; } catch { }
            if (acd == 0)
                return;

            bool reacquireQuick = acd == _reacquireAcdId && tick < _reacquireUntilTick;
            bool alternateScan = acd == _alternateScanAcdId && tick < _alternateScanUntilTick;
            int minTicks = (reacquireQuick || alternateScan || AggressiveScanMode) ? 1 : MinTicksBetweenMouseMoves;
            if (_pulseActive || tick < _siphonAssistUntilTick)
                minTicks = Math.Max(1, minTicks + 1);

            if (_lastMouseMoveTick > 0 && tick - _lastMouseMoveTick < minTicks)
                return;

            float x, y;
            if (!TryGetMonsterScreen(monster, out x, out y))
                return;

            bool eliteLike = IsLeader(monster) || IsEliteMinionLike(monster) || IsBossLike(monster);
            bool cheapFarTarget = SafeDistance(monster) > PestiPick() + 2f;

            if (eliteLike && IsActuallySelectedTarget(acd))
            {
                RegisterHoverSuccess(acd, tick, _lastHoverDx, _lastHoverDy, _lastSnapAttemptBin);
                _snapPhase = 0;
                return;
            }

            if (_unhoverableUntilTick > tick && acd == _unhoverableAcdId && SafeDistance(monster) <= PestiPick() + 3f)
                return;

            if (!eliteLike)
            {
                if (AnyEliteOrMinionCandidateExists())
                    return;

                if (SafeMouseMove(x, y, tick))
                {
                    _lastMouseMoveTick = tick;
                    _snapPhase = 0;
                }
                return;
            }

            bool pulseOrAssist = _pulseActive || tick < _siphonAssistUntilTick;
            if ((pulseOrAssist || reacquireQuick) && TryMoveCachedHover(acd, x, y, tick, true))
                return;

            if (_stableLockAcdId == acd && tick < _stableLockUntilTick)
            {
                if (SafeMouseMove(x + _stableLockDx, y + _stableLockDy, tick))
                    _lastMouseMoveTick = tick;
                return;
            }

            bool softWindow = pulseOrAssist || reacquireQuick;
            if (_softLockAcdId == acd && tick < _softLockUntilTick && softWindow)
            {
                if (SafeMouseMove(x + _softLockDx, y + _softLockDy, tick))
                    _lastMouseMoveTick = tick;
                return;
            }

            BuildAdaptiveProbeOrder(monster, x, y, cheapFarTarget, alternateScan);

            int maxProbes = _rankedProbeCount;
            if (maxProbes <= 0)
            {
                if (SafeMouseMove(x, y, tick))
                    _lastMouseMoveTick = tick;
                return;
            }

            if (_snapPhaseAcdId != acd)
            {
                _snapPhaseAcdId = acd;
                _snapPhase = 0;
            }

            int start = Math.Max(0, Math.Min(_snapPhase, maxProbes - 1));
            float bestScore = float.MinValue;
            float bestDx = 0f, bestDy = 0f;
            int bestBin = -1;
            int bestZone = -1;
            int bestIndex = start;
            bool hadHardSafeProbe = false;

            int probesThisTick = AggressiveScanMode
                ? Math.Min(maxProbes, 30)
                : ((alternateScan || reacquireQuick) ? Math.Min(maxProbes, 22) : Math.Min(maxProbes, 10));

            for (int pass = 0; pass < 2 && bestScore == float.MinValue; pass++)
            {
                bool allowSoftSkillbar = pass == 1;

                for (int i = 0; i < probesThisTick; i++)
                {
                    int idx = (start + i) % maxProbes;
                    float dx = _rankedProbeDx[idx];
                    float dy = _rankedProbeDy[idx];
                    int bin = _rankedProbeBin[idx];
                    int zone = _rankedProbeZone[idx];
                    float px = x + dx;
                    float py = y + dy;

                    if (!IsHardSafeScreenTarget(px, py))
                        continue;

                    hadHardSafeProbe = true;
                    bool softSkillbar = IsInsideSoftSkillbarMask(px, py);
                    if (softSkillbar && !allowSoftSkillbar)
                        continue;

                    float score = _rankedProbeScore[idx];
                    if (softSkillbar)
                        score -= 2.5f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDx = dx;
                        bestDy = dy;
                        bestBin = bin;
                        bestZone = zone;
                        bestIndex = idx;
                    }
                }
            }

            if (bestScore > float.MinValue)
            {
                _lastHoverDx = bestDx;
                _lastHoverDy = bestDy;
                _softLockAcdId = acd;
                _softLockDx = bestDx;
                _softLockDy = bestDy;
                _softLockUntilTick = tick + SoftLockTicks + (pulseOrAssist ? 4 : 0);
                _lastSnapAttemptAcd = acd;
                _lastSnapAttemptBin = bestBin;
                _lastSnapAttemptZone = bestZone;
                _lastSnapAttemptTick = tick;

                if (bestBin >= 0 && bestBin < AdaptiveBinCount)
                    RecordAdaptiveAttempt(bestBin);
                RecordProbeZoneAttempt(acd, bestZone, tick);

                if (SafeMouseMove(x + bestDx, y + bestDy, tick))
                {
                    _lastMouseMoveTick = tick;
                    _snapPhase = bestIndex + 1;
                    if (_snapPhase >= maxProbes)
                        _snapPhase = 0;
                    return;
                }
            }
            else if (hadHardSafeProbe)
            {
                _alternateScanAcdId = acd;
                _alternateScanUntilTick = tick + AlternateScanWindowTicks;
                _snapPhase = 0;
                return;
            }

            if (SafeDistance(monster) <= PestiPick() + 3f)
            {
                if (eliteLike)
                {
                    if (_lastFailedLeaderAcdId == acd && tick - _lastFailedLeaderTick <= FailedLeaderRetryWindowTicks)
                        _lastFailedLeaderCount++;
                    else
                    {
                        _lastFailedLeaderAcdId = acd;
                        _lastFailedLeaderCount = 1;
                    }

                    _lastFailedLeaderTick = tick;
                    if (_lastFailedLeaderCount >= 3)
                    {
                        _skipLeaderAcdId = acd;
                        _skipLeaderUntilTick = tick + SkipLeaderWindowTicks;
                        if (_lockedTargetAcdId == acd) _lockedTargetAcdId = 0;
                    }
                    else
                    {
                        _alternateScanAcdId = acd;
                        _alternateScanUntilTick = tick + AlternateScanWindowTicks;
                    }
                }

                _unhoverableAcdId = acd;
                _unhoverableUntilTick = tick + (eliteLike ? 4 : 16);
            }

            _snapPhase = 0;
        }

        private void RegisterHoverSuccess(uint acd, int tick, float dx, float dy, int bin)
        {
            _lastHoverAcdId = acd;
            _lastHoverTick = tick;
            _cachedHoverAcdId = acd;
            _cachedHoverDx = dx;
            _cachedHoverDy = dy;
            _cachedHoverUntilTick = tick + 300;
            _cachedHoverTryUntilTick = tick + 12;
            _stableLockAcdId = acd;
            _stableLockDx = dx;
            _stableLockDy = dy;
            _stableLockUntilTick = tick + StableLockTicks;
            _softLockAcdId = acd;
            _softLockDx = dx;
            _softLockDy = dy;
            _softLockUntilTick = tick + SoftLockTicks;

            if (bin >= 0 && bin < AdaptiveBinCount)
            {
                _adaptiveBinGood[bin] = Math.Min(20f, _adaptiveBinGood[bin] + 1.0f);
                _adaptiveBinBad[bin] = Math.Max(0f, _adaptiveBinBad[bin] - 0.25f);
            }

            if (_lastSnapAttemptAcd == acd && _lastSnapAttemptZone >= 0)
                RecordProbeZoneSuccess(acd, _lastSnapAttemptZone, tick);

            if (_reacquireAcdId == acd) { _reacquireAcdId = 0; _reacquireUntilTick = 0; }
            if (_alternateScanAcdId == acd) { _alternateScanAcdId = 0; _alternateScanUntilTick = 0; }
            if (_skipLeaderAcdId == acd) { _skipLeaderAcdId = 0; _skipLeaderUntilTick = 0; }
            if (_lastFailedLeaderAcdId == acd) { _lastFailedLeaderAcdId = 0; _lastFailedLeaderCount = 0; }
            if (_unhoverableAcdId == acd) { _unhoverableAcdId = 0; _unhoverableUntilTick = 0; }
        }

        private void BuildAdaptiveProbeOrder(IMonster monster, float fx, float fy, bool cheapFarTarget, bool alternateScan)
        {
            _rankedProbeCount = 0;

            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;
            float scale = Math.Max(0.5f, Math.Min(rb / BaselineRadiusBottom, 3.0f));

            float headHighY = -(BaseHeadPx * scale);
            float headLowY = -(BaseHeadPx * 0.84f * scale);
            float shoulderY = -(BaseShoulderPx * 0.98f * scale);
            float chestTopY = -(BaseChestPx * scale);
            float abdomenY = -(BaseAbdomenPx * scale);
            float torsoMidY = (chestTopY + abdomenY) * 0.5f;
            float hipY = -(Math.Max(6f, BaseAbdomenPx * 0.35f) * scale);
            float kneeY = -(Math.Max(3f, BaseAbdomenPx * 0.20f) * scale);

            float headTriX = Math.Max(8f, BaseShoulderXPx * 0.45f) * scale;
            float shoulderX = BaseShoulderXPx * scale;
            float torsoEdgeX = Math.Max(14f, BaseShoulderXPx * 0.55f) * scale;
            float hipX = Math.Max(10f, BaseShoulderXPx * 0.40f) * scale;
            float footX = Math.Max(8f, BaseShoulderXPx * 0.30f) * scale;

            bool smallTarget = scale <= 0.92f;
            bool wideTarget = scale >= 1.45f;
            if (smallTarget)
            {
                headTriX *= 0.82f; shoulderX *= 0.84f; torsoEdgeX *= 0.80f; hipX *= 0.80f; footX *= 0.82f;
                headHighY *= 0.90f; headLowY *= 0.92f; shoulderY *= 0.94f; chestTopY *= 0.96f; abdomenY *= 0.96f; hipY *= 0.92f; kneeY *= 0.90f;
            }
            else if (wideTarget)
            {
                headTriX *= 1.05f; shoulderX *= 1.10f; torsoEdgeX *= 1.18f; hipX *= 1.12f; footX *= 1.08f;
            }

            BuildSolverBlockerProfile(monster, fx, fy);

            AddRankedProbeCandidate(0f, 0f, 1, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, hipY, 1, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, kneeY, 1, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, abdomenY, 2, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, torsoMidY, 2, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, chestTopY, 3, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, shoulderY, 3, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, headLowY, 4, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(0f, headHighY, 4, monster, fx, fy, cheapFarTarget);

            AddRankedProbeCandidate(-headTriX, headLowY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(headTriX, headLowY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(-shoulderX, shoulderY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(shoulderX, shoulderY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(-torsoEdgeX, torsoMidY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(torsoEdgeX, torsoMidY, 5, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(-hipX, hipY, 6, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(hipX, hipY, 6, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(-footX, kneeY, 6, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(footX, kneeY, 6, monster, fx, fy, cheapFarTarget);

            if (_cachedHoverAcdId != 0 && _cachedHoverAcdId == monster.AcdId && _cachedHoverUntilTick > 0)
                AddMicroProbeCluster(_cachedHoverDx, _cachedHoverDy, 5f * scale, 4f * scale, 7, monster, fx, fy, cheapFarTarget);

            if (_lastFailedLeaderAcdId == monster.AcdId || alternateScan || AggressiveScanMode)
            {
                AddMicroProbeCluster(-torsoEdgeX * 1.15f, torsoMidY, 8f * scale, 8f * scale, 7, monster, fx, fy, cheapFarTarget);
                AddMicroProbeCluster(torsoEdgeX * 1.15f, torsoMidY, 8f * scale, 8f * scale, 7, monster, fx, fy, cheapFarTarget);
                AddMicroProbeCluster(-shoulderX * 1.25f, headLowY, 8f * scale, 8f * scale, 7, monster, fx, fy, cheapFarTarget);
                AddMicroProbeCluster(shoulderX * 1.25f, headLowY, 8f * scale, 8f * scale, 7, monster, fx, fy, cheapFarTarget);
            }

            if (AggressiveScanMode || alternateScan)
            {
                AddRankedProbeCandidate(-shoulderX * 1.35f, shoulderY, 0, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(shoulderX * 1.35f, shoulderY, 1, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-torsoEdgeX * 1.45f, torsoMidY, 2, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(torsoEdgeX * 1.45f, torsoMidY, 3, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-hipX * 1.40f, hipY, 4, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(hipX * 1.40f, hipY, 5, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(-footX * 1.55f, kneeY, 6, monster, fx, fy, cheapFarTarget);
                AddRankedProbeCandidate(footX * 1.55f, kneeY, 7, monster, fx, fy, cheapFarTarget);
            }

            SortRankedProbes();
        }

        private void AddMicroProbeCluster(float dx, float dy, float jitterX, float jitterY, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            AddRankedProbeCandidate(dx, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx - jitterX, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx + jitterX, dy, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx, dy - jitterY, bin, monster, fx, fy, cheapFarTarget);
            AddRankedProbeCandidate(dx, dy + jitterY, bin, monster, fx, fy, cheapFarTarget);
        }
        private void AddRankedProbeCandidate(float dx, float dy, int bin, IMonster monster, float fx, float fy, bool cheapFarTarget)
        {
            if (_rankedProbeCount >= AdaptiveCandidateCapacity)
                return;

            if (!IsHardSafeScreenTarget(fx + dx, fy + dy))
                return;

            int safeBin = Math.Max(0, Math.Min(AdaptiveBinCount - 1, bin));
            int zone = GetProbeZone(monster, dx, dy);
            float score = ScoreAdaptiveProbe(monster, fx, fy, dx, dy, safeBin, cheapFarTarget);

            uint acd = 0;
            int tick = 0;
            try { acd = monster != null ? monster.AcdId : 0u; } catch { }
            try { tick = Hud.Game.CurrentGameTick; } catch { }
            if (acd != 0 && tick > 0)
                score += GetProbeZoneCoverageBonus(acd, zone, tick) * 0.45f;

            _rankedProbeDx[_rankedProbeCount] = dx;
            _rankedProbeDy[_rankedProbeCount] = dy;
            _rankedProbeBin[_rankedProbeCount] = safeBin;
            _rankedProbeZone[_rankedProbeCount] = zone;
            _rankedProbeScore[_rankedProbeCount] = score;
            _rankedProbeCount++;
        }


        private void SortRankedProbes()
        {
            for (int i = 0; i < _rankedProbeCount - 1; i++)
            {
                int best = i;
                float bestScore = _rankedProbeScore[i];
                for (int j = i + 1; j < _rankedProbeCount; j++)
                {
                    if (_rankedProbeScore[j] > bestScore)
                    {
                        best = j;
                        bestScore = _rankedProbeScore[j];
                    }
                }

                if (best == i)
                    continue;

                float s = _rankedProbeScore[i]; _rankedProbeScore[i] = _rankedProbeScore[best]; _rankedProbeScore[best] = s;
                float dx = _rankedProbeDx[i]; _rankedProbeDx[i] = _rankedProbeDx[best]; _rankedProbeDx[best] = dx;
                float dy = _rankedProbeDy[i]; _rankedProbeDy[i] = _rankedProbeDy[best]; _rankedProbeDy[best] = dy;
                int b = _rankedProbeBin[i]; _rankedProbeBin[i] = _rankedProbeBin[best]; _rankedProbeBin[best] = b;
                int z = _rankedProbeZone[i]; _rankedProbeZone[i] = _rankedProbeZone[best]; _rankedProbeZone[best] = z;
            }
        }

        private float ScoreAdaptiveProbe(IMonster target, float fx, float fy, float dx, float dy, int bin, bool cheapFarTarget)
        {
            float px = fx + dx;
            float py = fy + dy;
            float score = 0f;

            try
            {
                float curX = Hud.Window.CursorX;
                float curY = Hud.Window.CursorY;
                float cursorDist = (float)Math.Sqrt((px - curX) * (px - curX) + (py - curY) * (py - curY));
                score -= cursorDist * 0.018f;
            }
            catch { }

            score += _adaptiveBinGood[bin] * 1.25f;
            score -= _adaptiveBinBad[bin] * 0.75f;

            uint acd = 0;
            try { acd = target.AcdId; } catch { }

            if (_cachedHoverAcdId == acd)
            {
                float ddx = dx - _cachedHoverDx;
                float ddy = dy - _cachedHoverDy;
                float cachedDist = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                score += Math.Max(0f, 4.8f - cachedDist * 0.065f);
            }

            float rb = 0f;
            try { rb = (float)target.RadiusBottom; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;
            float scale = Math.Max(0.5f, Math.Min(rb / BaselineRadiusBottom, 3.0f));
            float headLowY = -(BaseHeadPx * 0.84f * scale);
            float chestTopY = -(BaseChestPx * scale);
            float abdomenY = -(BaseAbdomenPx * scale);
            float torsoMidY = (chestTopY + abdomenY) * 0.5f;
            bool feetZone = dy >= -(Math.Max(4f, BaseAbdomenPx * 0.22f) * scale) && dy <= Math.Max(2.5f, 4f * scale);
            bool coreZone = !feetZone && dy > -(BaseShoulderPx * 0.88f * scale);
            bool headZone = dy <= -(BaseShoulderPx * 0.88f * scale);
            bool flankZone = Math.Abs(dx) >= Math.Max(10f, BaseShoulderXPx * 0.52f * scale);
            bool outerZone = Math.Sqrt(dx * dx + dy * dy) >= Math.Max(18f, BaseShoulderXPx * 0.92f * scale);

            if (!cheapFarTarget)
            {
                if (feetZone) score += 2.30f;
                else if (coreZone) score += 1.55f;
                else if (headZone) score += 1.95f;
                if (flankZone) score += 0.45f;
                if (outerZone) score += 0.35f;
            }
            else
            {
                if (feetZone || coreZone) score += 2.05f;
                else if (headZone) score += 0.95f;
                else score -= 1.05f;
            }

            if (_solverProfileAcd == acd)
            {
                float upper = _solverOccUp + _solverBigUp * 0.90f;
                float lower = _solverOccDown + _solverBigDown * 1.15f;
                float left = _solverOccLeft + _solverBigLeft * 1.05f;
                float right = _solverOccRight + _solverBigRight * 1.05f;

                if (lower > upper + 0.50f)
                {
                    if (headZone) score += 1.85f;
                    if (feetZone) score -= 1.15f;
                }
                else if (upper > lower + 0.75f)
                {
                    if (feetZone || coreZone) score += 0.95f;
                    if (headZone) score -= 0.55f;
                }

                if (left > right + 0.55f)
                {
                    if (dx > 4f) score += 1.25f;
                    else if (dx < -4f) score -= 1.10f;
                }
                else if (right > left + 0.55f)
                {
                    if (dx < -4f) score += 1.25f;
                    else if (dx > 4f) score -= 1.10f;
                }

                if (_solverBlockerWeightTotal > 0.01f)
                {
                    float awayX = fx - _solverBlockerCx;
                    float awayY = fy - _solverBlockerCy;
                    float probeLen = (float)Math.Sqrt(dx * dx + dy * dy);
                    float awayLen = (float)Math.Sqrt(awayX * awayX + awayY * awayY);
                    if (probeLen > 0.01f && awayLen > 0.01f)
                        score += ((dx * awayX + dy * awayY) / (probeLen * awayLen)) * 2.75f;
                }
            }

            try
            {
                foreach (var other in Hud.Game.AliveMonsters)
                {
                    if (other == null || !other.IsAlive || !other.IsOnScreen || SameMonster(other, target))
                        continue;

                    float ox, oy;
                    if (!TryGetMonsterScreen(other, out ox, out oy))
                        continue;

                    float odx = px - ox;
                    float ody = py - oy;
                    float dist = (float)Math.Sqrt(odx * odx + ody * ody);
                    float maxDist = IsLargeOccludingTrash(other) ? 76f : 56f;
                    if (dist > maxDist)
                        continue;

                    float proximity = 1.18f - Math.Min(1f, dist / maxDist);
                    score -= GetAdaptiveBlockerWeight(other) * proximity;
                }
            }
            catch { }

            return score;
        }

        private void BuildSolverBlockerProfile(IMonster target, float fx, float fy)
        {
            _solverProfileAcd = 0;
            _solverOccLeft = _solverOccRight = _solverOccUp = _solverOccDown = 0f;
            _solverBigLeft = _solverBigRight = _solverBigUp = _solverBigDown = 0f;
            _solverBlockerCx = _solverBlockerCy = 0f;
            _solverBlockerWeightTotal = 0f;
            _solverBlockerCount = 0;

            if (target == null)
                return;

            uint targetAcd = 0;
            try { targetAcd = target.AcdId; } catch { }
            if (targetAcd == 0)
                return;

            _solverProfileAcd = targetAcd;

            try
            {
                foreach (var other in Hud.Game.AliveMonsters)
                {
                    if (other == null || !other.IsAlive || !other.IsOnScreen || SameMonster(other, target))
                        continue;

                    float ox, oy;
                    if (!TryGetMonsterScreen(other, out ox, out oy))
                        continue;

                    float relx = ox - fx;
                    float rely = oy - fy;
                    float dist = (float)Math.Sqrt(relx * relx + rely * rely);

                    float rb = 0f;
                    try { rb = (float)other.RadiusBottom; } catch { }
                    if (rb <= 0f) rb = BaselineRadiusBottom;
                    float bodyScale = Math.Max(0.60f, Math.Min(2.60f, rb / BaselineRadiusBottom));

                    float range = 34f + (bodyScale * 18f);
                    bool bigTrash = IsLargeOccludingTrash(other);
                    if (bigTrash) range += 18f;
                    if (IsLeader(other) || IsEliteMinionLike(other)) range += 10f;
                    if (dist > range) continue;

                    float proximity = 1.32f - Math.Min(1f, dist / range);
                    if (proximity <= 0f) continue;

                    float occ = proximity * GetAdaptiveBlockerWeight(other);
                    if (relx < 0f) _solverOccLeft += occ; else _solverOccRight += occ;
                    if (rely < 0f) _solverOccUp += occ; else _solverOccDown += occ;

                    if (bigTrash)
                    {
                        float bigOcc = occ * 1.10f;
                        if (relx < 0f) _solverBigLeft += bigOcc; else _solverBigRight += bigOcc;
                        if (rely < 0f) _solverBigUp += bigOcc; else _solverBigDown += bigOcc;
                    }

                    _solverBlockerCx += ox * occ;
                    _solverBlockerCy += oy * occ;
                    _solverBlockerWeightTotal += occ;
                    _solverBlockerCount++;
                }
            }
            catch { }

            if (_solverBlockerWeightTotal > 0.01f)
            {
                _solverBlockerCx /= _solverBlockerWeightTotal;
                _solverBlockerCy /= _solverBlockerWeightTotal;
            }
        }

        private float GetAdaptiveBlockerWeight(IMonster blocker)
        {
            if (blocker == null) return 1f;

            float weight = 1f;
            if (IsLeader(blocker) || IsEliteMinionLike(blocker)) weight += 0.65f;
            if (IsLargeOccludingTrash(blocker)) weight += 0.95f;

            float rb = 0f;
            try { rb = (float)blocker.RadiusBottom; } catch { }
            if (rb > 0f)
            {
                float body = Math.Max(0f, Math.Min(1.25f, (rb / BaselineRadiusBottom) - 0.95f));
                weight += body * 0.55f;
            }

            double prog = 0d;
            try { prog = blocker.SnoMonster != null ? blocker.SnoMonster.RiftProgression : 0d; } catch { }
            if (prog >= 0.80d) weight += 0.18f;

            return weight;
        }

        private bool IsLargeOccludingTrash(IMonster monster)
        {
            if (monster == null || IsLeader(monster) || IsEliteMinionLike(monster))
                return false;

            float rb = 0f;
            try { rb = (float)monster.RadiusBottom; } catch { }
            if (rb >= BaselineRadiusBottom * 1.35f)
                return true;

            double prog = 0d;
            try { prog = monster.SnoMonster != null ? monster.SnoMonster.RiftProgression : 0d; } catch { }
            if (prog >= BigTrashMinRiftProgression)
                return true;

            try
            {
                string name = monster.SnoMonster != null ? monster.SnoMonster.NameEnglish : string.Empty;
                return !string.IsNullOrEmpty(name)
                    && (name.IndexOf("Grotesque", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Mallet", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Executioner", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Punisher", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        private bool TryGetMonsterScreen(IMonster monster, out float x, out float y)
        {
            x = 0f;
            y = 0f;

            try
            {
                var sc = monster.FloorCoordinate.ToScreenCoordinate();
                if (sc != null)
                {
                    x = sc.X;
                    y = sc.Y;
                    return true;
                }
            }
            catch { }

            try
            {
                var sc = monster.ScreenCoordinate;
                if (sc != null)
                {
                    x = sc.X;
                    y = sc.Y;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void RecordAdaptiveAttempt(int bin)
        {
            if (bin < 0 || bin >= AdaptiveBinCount)
                return;

            _adaptiveBinBad[bin] = Math.Min(20f, _adaptiveBinBad[bin] + 0.10f);
        }

        private int GetProbeZone(IMonster monster, float dx, float dy)
        {
            float rb = 0f;
            try { rb = monster != null ? (float)monster.RadiusBottom : 0f; } catch { }
            if (rb <= 0f) rb = BaselineRadiusBottom;
            float scale = Math.Max(0.5f, Math.Min(rb / BaselineRadiusBottom, 3.0f));
            float headLowY = -(BaseHeadPx * 0.84f * scale);
            float chestTopY = -(BaseChestPx * scale);
            float abdomenY = -(BaseAbdomenPx * scale);
            float torsoMidY = (chestTopY + abdomenY) * 0.5f;
            return GetProbeZone(dx, dy, scale, torsoMidY, headLowY);
        }

        private int GetProbeZone(float dx, float dy, float scale, float torsoMidY, float headLowY)
        {
            float centerHalfX = Math.Max(8f, BaseShoulderXPx * 0.42f * scale);
            int col = dx < -centerHalfX ? 0 : (dx > centerHalfX ? 2 : 1);

            float topCut = (headLowY + torsoMidY) * 0.5f;
            float bottomCut = torsoMidY + Math.Max(6f, BaseAbdomenPx * 0.42f * scale);
            int row = dy <= topCut ? 0 : (dy >= bottomCut ? 2 : 1);

            return row * 3 + col;
        }

        private void ClearProbeZoneState()
        {
            _probeZoneAcdId = 0;
            _probeZoneSuccessAcdId = 0;
            _lastSuccessProbeZone = -1;
            _lastSuccessProbeZoneTick = 0;
            for (int i = 0; i < ProbeZoneCount; i++)
            {
                _probeZoneLastTryTick[i] = 0;
                _probeZoneTryCount[i] = 0;
            }
        }

        private void EnsureProbeZoneState(uint acd)
        {
            if (_probeZoneAcdId == acd)
                return;

            _probeZoneAcdId = acd;
            for (int i = 0; i < ProbeZoneCount; i++)
            {
                _probeZoneLastTryTick[i] = 0;
                _probeZoneTryCount[i] = 0;
            }
        }

        private float GetProbeZoneCoverageBonus(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return 0f;

            EnsureProbeZoneState(acd);

            int last = _probeZoneLastTryTick[zone];
            int age = last > 0 ? tick - last : 9999;
            int tries = _probeZoneTryCount[zone];
            float bonus = 0f;

            if (last == 0)
                bonus += 0.95f;
            else if (age >= 10)
                bonus += 0.55f;
            else if (age >= 5)
                bonus += 0.25f;

            bonus -= Math.Min(1.10f, tries * 0.16f);

            if (_probeZoneSuccessAcdId == acd && _lastSuccessProbeZone >= 0)
            {
                if (zone == _lastSuccessProbeZone)
                    bonus += 0.80f;
                else if (ProbeZonesAdjacent(zone, _lastSuccessProbeZone))
                    bonus += 0.32f;
            }

            return bonus;
        }

        private bool ProbeZonesAdjacent(int a, int b)
        {
            if (a < 0 || b < 0) return false;
            int ar = a / 3, ac = a % 3;
            int br = b / 3, bc = b % 3;
            return Math.Abs(ar - br) <= 1 && Math.Abs(ac - bc) <= 1;
        }

        private void RecordProbeZoneAttempt(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return;

            EnsureProbeZoneState(acd);
            _probeZoneLastTryTick[zone] = tick;
            _probeZoneTryCount[zone] = Math.Min(99, _probeZoneTryCount[zone] + 1);
        }

        private void RecordProbeZoneSuccess(uint acd, int zone, int tick)
        {
            if (acd == 0 || zone < 0 || zone >= ProbeZoneCount)
                return;

            _probeZoneSuccessAcdId = acd;
            _lastSuccessProbeZone = zone;
            _lastSuccessProbeZoneTick = tick;
        }

        private void GetProbeOffset(IMonster monster, int phase, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;

            float radius = BaselineRadiusBottom;
            try { radius = monster.RadiusBottom > 0f ? monster.RadiusBottom : BaselineRadiusBottom; } catch { }
            float scale = Math.Max(0.65f, Math.Min(radius / BaselineRadiusBottom, 2.8f));

            switch (phase % 16)
            {
                case 0: dx = 0f; dy = -16f * scale; break;
                case 1: dx = 0f; dy = -32f * scale; break;
                case 2: dx = -13f * scale; dy = -20f * scale; break;
                case 3: dx = 13f * scale; dy = -20f * scale; break;
                case 4: dx = 0f; dy = -48f * scale; break;
                case 5: dx = -20f * scale; dy = -34f * scale; break;
                case 6: dx = 20f * scale; dy = -34f * scale; break;
                case 7: dx = 0f; dy = 0f; break;
                case 8: dx = -28f * scale; dy = -12f * scale; break;
                case 9: dx = 28f * scale; dy = -12f * scale; break;
                case 10: dx = -16f * scale; dy = -52f * scale; break;
                case 11: dx = 16f * scale; dy = -52f * scale; break;
                case 12: dx = 0f; dy = 10f * scale; break;
                case 13: dx = -36f * scale; dy = -28f * scale; break;
                case 14: dx = 36f * scale; dy = -28f * scale; break;
                default: dx = 0f; dy = -64f * scale; break;
            }
        }

        private bool SafeMouseMove(float x, float y, int tick)
        {
            try
            {
                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;

                if (w <= 0f) w = 1920f;
                if (h <= 0f) h = 1080f;

                if (x < 0f) x = 0f;
                if (y < 0f) y = 0f;
                if (x > w - 1f) x = w - 1f;
                if (y > h - 1f) y = h - 1f;

                if (!IsHardSafeScreenTarget(x, y))
                    return false;

                BeginCursorOwnershipIfNeeded(tick);
                SetCursorPos((int)Math.Round((double)x), (int)Math.Round((double)y));
                _cursorWasMovedByPlugin = true;
                return true;
            }
            catch { return false; }
        }

        private void BeginCursorOwnershipIfNeeded(int tick)
        {
            if (_cursorOwned)
                return;

            _cursorOwned = true;
            _cursorWasMovedByPlugin = false;
            _engageStartTick = tick;

            try
            {
                _savedCursorX = Hud.Window.CursorX;
                _savedCursorY = Hud.Window.CursorY;
                _haveSavedCursor = true;
            }
            catch
            {
                _savedCursorX = 0f;
                _savedCursorY = 0f;
                _haveSavedCursor = false;
            }
        }

        private void QueueCursorRestore(int tick)
        {
            if (!RestoreCursorOnReleaseOrMove || !_cursorOwned || !_cursorWasMovedByPlugin)
                return;

            int engagedTicks = _engageStartTick > 0 ? Math.Max(0, tick - _engageStartTick) : int.MaxValue;
            if (engagedTicks > CursorRestoreShortEngageTicks || !_haveSavedCursor)
            {
                ReleaseCursorOwnershipWithoutRestore();
                return;
            }

            float targetX = _savedCursorX;
            float targetY = _savedCursorY;

            try
            {
                float dx = targetX - Hud.Window.CursorX;
                float dy = targetY - Hud.Window.CursorY;
                if ((dx * dx + dy * dy) < CursorRestoreMinMovePxSq)
                {
                    ReleaseCursorOwnershipWithoutRestore();
                    return;
                }
            }
            catch { }

            _pendingRestoreX = targetX;
            _pendingRestoreY = targetY;
            _pendingRestoreTick = tick;
        }

        private void ReleaseCursorOwnershipWithoutRestore()
        {
            _pendingRestoreTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _engageStartTick = 0;
        }

        private float _pendingRestoreX;
        private float _pendingRestoreY;
        private int _pendingRestoreTick;

        private void ProcessPendingCursorRestore(int tick)
        {
            if (_pendingRestoreTick <= 0 || tick < _pendingRestoreTick)
                return;

            try
            {
                if (Hud != null && Hud.Window != null && Hud.Window.IsForeground && IsHardSafeScreenTarget(_pendingRestoreX, _pendingRestoreY))
                    SetCursorPos((int)Math.Round((double)_pendingRestoreX), (int)Math.Round((double)_pendingRestoreY));
            }
            catch { }

            _pendingRestoreTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _engageStartTick = 0;
        }

        private bool IsHardSafeScreenTarget(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;

                if (x < 0f || y < 0f || x > w || y > h)
                    return false;

                if (IsInsidePlayerPortraitFace(x, y))
                    return false;

                if (IsInsideHardExplicitNoClickMask(x, y))
                    return false;

                return true;
            }
            catch { return true; }
        }

        private bool IsPreferredAutoSnapScreenTarget(float x, float y)
        {
            if (!IsHardSafeScreenTarget(x, y))
                return false;

            return !IsInsideSoftSkillbarMask(x, y);
        }

        private bool IsSafeScreenTarget(float x, float y)
        {
            return IsHardSafeScreenTarget(x, y);
        }

        private bool IsLargeLowerSkillbarMaskSource(RectangleF src)
        {
            return src.Top >= 850f
                && src.Left <= 400f
                && src.Width >= 900f
                && src.Height >= 120f;
        }

        private bool IsInsideScaledMask(RectangleF src, float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null || Hud.Window.Size.Width <= 0 || Hud.Window.Size.Height <= 0)
                    return false;

                float sx = Hud.Window.Size.Width / UiMaskReferenceWidth;
                float sy = Hud.Window.Size.Height / UiMaskReferenceHeight;

                RectangleF r = new RectangleF(
                    (src.Left * sx) - UiMaskPaddingPx,
                    (src.Top * sy) - UiMaskPaddingPx,
                    (src.Width * sx) + (UiMaskPaddingPx * 2f),
                    (src.Height * sy) + (UiMaskPaddingPx * 2f));

                return r.Width > 0f
                    && r.Height > 0f
                    && x >= r.Left
                    && x <= r.Right
                    && y >= r.Top
                    && y <= r.Bottom;
            }
            catch { return false; }
        }

        private bool IsInsideHardExplicitNoClickMask(float x, float y)
        {
            try
            {
                for (int i = 0; i < NoClickMaskRects1920x1080.Length; i++)
                {
                    RectangleF src = NoClickMaskRects1920x1080[i];
                    if (IsLargeLowerSkillbarMaskSource(src))
                        continue;

                    if (IsInsideScaledMask(src, x, y))
                        return true;
                }

                return false;
            }
            catch { return false; }
        }

        private bool IsInsideSoftSkillbarMask(float x, float y)
        {
            try
            {
                for (int i = 0; i < NoClickMaskRects1920x1080.Length; i++)
                {
                    RectangleF src = NoClickMaskRects1920x1080[i];
                    if (!IsLargeLowerSkillbarMaskSource(src))
                        continue;

                    if (IsInsideScaledMask(src, x, y))
                        return true;
                }

                return false;
            }
            catch { return false; }
        }

        private bool IsInsideExplicitNoClickMask(float x, float y)
        {
            return IsInsideHardExplicitNoClickMask(x, y) || IsInsideSoftSkillbarMask(x, y);
        }

        private bool IsInsidePlayerPortraitFace(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Players == null)
                    return false;

                foreach (var player in Hud.Game.Players)
                {
                    try
                    {
                        if (player == null || !player.IsInGame)
                            continue;

                        var ui = player.PortraitUiElement;
                        if (ui == null || !ui.Visible)
                            continue;

                        RectangleF r = ui.Rectangle;
                        if (r.Width <= 0f || r.Height <= 0f)
                            continue;

                        if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                            return true;
                    }
                    catch { }
                }

                return false;
            }
            catch { return false; }
        }

        #endregion

        #region Input
        #endregion

        #region Input

        private bool PulseSiphon(int tick, int intervalTicks, int downTicks, bool allowEarlyRelease)
        {
            if (_pulseActive)
                return false;

            if (_nextPulseTick > 0 && tick < _nextPulseTick)
                return false;

            _nextPulseTick = tick + Math.Max(1, intervalTicks);
            _siphonAssistUntilTick = Math.Max(_siphonAssistUntilTick, tick + SiphonAssistPauseTicks);

            try
            {
                BeginStandstillIfNeeded();
                SendActionDown(_siphonKey);

                _siphonPulseOwned = true;
                _pulseActive = true;
                _pulseDownUntilTick = tick + Math.Max(1, downTicks);

                if (!allowEarlyRelease)
                    _pulseWasBuild = false;

                return true;
            }
            catch
            {
                StopPulseNow();
                return false;
            }
        }

        private void TickPulseReleaseIfNeeded(int tick)
        {
            if (!_pulseActive)
                return;

            if (_pulseWasBuild && _pulseBuildTarget > 0)
            {
                int stacks;
                float timeLeft;
                if (TryGetPowerShift(out stacks, out timeLeft) && stacks >= _pulseBuildTarget)
                {
                    ReleasePulseInput();
                    return;
                }
            }

            if (tick >= _pulseDownUntilTick)
                ReleasePulseInput();
        }

        private void ReleasePulseInput()
        {
            try
            {
                if (_siphonPulseOwned && _siphonKeyKnown)
                    SendActionUp(_siphonKey);
            }
            catch { }

            ReleaseStandstillIfOwned();

            _pulseActive = false;
            _siphonPulseOwned = false;
            _pulseDownUntilTick = 0;
            _pulseWasBuild = false;
            _pulseBuildTarget = 0;
        }

        private void StopPulseNow()
        {
            ReleasePulseInput();
            _nextPulseTick = 0;
        }

        private void ResetRuntime(bool releaseInput)
        {
            if (releaseInput)
                StopPulseNow();

            _lockedTargetAcdId = 0;
            _lockedTargetKeepUntilTick = 0;
            _returnToRareAcdId = 0;
            _skipAcdId = 0;
            _skipUntilTick = 0;
            _snapPhaseAcdId = 0;
            _snapPhase = 0;
            _lastMouseMoveTick = 0;
            _cursorOwned = false;
            _cursorWasMovedByPlugin = false;
            _haveSavedCursor = false;
            _savedCursorX = 0f;
            _savedCursorY = 0f;
            _engageStartTick = 0;
            _pendingRestoreTick = 0;
            _pendingRestoreX = 0f;
            _pendingRestoreY = 0f;
            _lastHoverAcdId = 0;
            _lastHoverTick = 0;
            _lastHoverDx = 0f;
            _lastHoverDy = 0f;
            _rankedProbeCount = 0;
            _lastSnapAttemptAcd = 0;
            _lastSnapAttemptBin = -1;
            _lastSnapAttemptZone = -1;
            _lastSnapAttemptTick = 0;
            ClearProbeZoneState();
            _stableLockAcdId = 0;
            _stableLockDx = 0f;
            _stableLockDy = 0f;
            _stableLockUntilTick = 0;
            _softLockAcdId = 0;
            _softLockDx = 0f;
            _softLockDy = 0f;
            _softLockUntilTick = 0;
            _cachedHoverAcdId = 0;
            _cachedHoverDx = 0f;
            _cachedHoverDy = 0f;
            _cachedHoverUntilTick = 0;
            _cachedHoverTryUntilTick = 0;
            _reacquireAcdId = 0;
            _reacquireUntilTick = 0;
            _unhoverableAcdId = 0;
            _unhoverableUntilTick = 0;
            _lastFailedLeaderAcdId = 0;
            _lastFailedLeaderTick = 0;
            _lastFailedLeaderCount = 0;
            _alternateScanAcdId = 0;
            _alternateScanUntilTick = 0;
            _skipLeaderAcdId = 0;
            _skipLeaderUntilTick = 0;
            _solverProfileAcd = 0;
            _solverOccLeft = _solverOccRight = _solverOccUp = _solverOccDown = 0f;
            _solverBigLeft = _solverBigRight = _solverBigUp = _solverBigDown = 0f;
            _solverBlockerCx = _solverBlockerCy = 0f;
            _solverBlockerWeightTotal = 0f;
            _solverBlockerCount = 0;
            _forceMoveWasDown = false;
            _siphonKeyWasDown = false;
            _lanceWasDown = false;
            _engageRefreshConsumed = false;
            _lastSiphonTargetAcdId = 0;
            _targetSwitchRefreshAcdId = 0;
            ClearLateRefreshIntent();
            ClearFastBuildGuard();
            ClearNoTargetAnchorLimiter(false);
            _lastPulseWasNoTargetAnchor = false;
            _lastCorpseScanTick = 0;
            _cachedCorpsesAvailable = false;
            _lateRefreshPulseConsumed = false;
            _siphonAssistUntilTick = 0;
            _lanceKeyKnown = false;
            _siphonKeyKnown = false;
            _lanceKey = ActionKey.Unknown;
            _siphonKey = ActionKey.Unknown;
        }

        private bool IsActionPhysicallyDown(ActionKey key)
        {
            ushort vk = VirtualKeyForAction(key);
            return vk != 0 && IsKeyPhysicallyDown(vk);
        }

        private ushort VirtualKeyForAction(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0x01;
                case ActionKey.RightSkill: return 0x02;
                case ActionKey.Skill1: return Skill1VirtualKey;
                case ActionKey.Skill2: return Skill2VirtualKey;
                case ActionKey.Skill3: return Skill3VirtualKey;
                case ActionKey.Skill4: return Skill4VirtualKey;
                default: return 0;
            }
        }

        private void BeginStandstillIfNeeded()
        {
            if (ForceStandstillVirtualKey == 0)
                return;

            if (IsKeyPhysicallyDown(ForceStandstillVirtualKey))
                return;

            SendKeyDown(ForceStandstillVirtualKey);
            _standstillOwned = true;
        }

        private void ReleaseStandstillIfOwned()
        {
            if (!_standstillOwned)
                return;

            SendKeyUp(ForceStandstillVirtualKey);
            _standstillOwned = false;
        }

        private void SendActionDown(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill:
                    SendMouse(LeftDown);
                    break;
                case ActionKey.RightSkill:
                    SendMouse(RightDown);
                    break;
                case ActionKey.Skill1:
                case ActionKey.Skill2:
                case ActionKey.Skill3:
                case ActionKey.Skill4:
                    SendKeyDown(VirtualKeyForAction(key));
                    break;
            }
        }

        private void SendActionUp(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill:
                    SendMouse(LeftUp);
                    break;
                case ActionKey.RightSkill:
                    SendMouse(RightUp);
                    break;
                case ActionKey.Skill1:
                case ActionKey.Skill2:
                case ActionKey.Skill3:
                case ActionKey.Skill4:
                    SendKeyUp(VirtualKeyForAction(key));
                    break;
            }
        }

        private static void SendKeyDown(ushort virtualKey)
        {
            SendKey(virtualKey, false);
        }

        private static void SendKeyUp(ushort virtualKey)
        {
            SendKey(virtualKey, true);
        }

        private static void SendKey(ushort virtualKey, bool keyUp)
        {
            if (virtualKey == 0)
                return;

            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;
            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        private static void SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;
            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        #endregion

        #region Small Utilities / Win32

        private int MsToTicks(int ms)
        {
            if (ms <= 0)
                return 1;

            return Math.Max(1, (int)Math.Round(ms / 1000.0 * 60.0));
        }

        private int ClampStackTarget(int value)
        {
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }

        private int ClampRange(int value)
        {
            if (value < 15) return 15;
            if (value > 90) return 90;
            return ((value + 2) / 5) * 5;
        }

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint RightDown = 0x0008;
        private const uint RightUp = 0x0010;
        private const uint KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        private static bool IsKeyPhysicallyDown(ushort virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        #endregion
    }
}
