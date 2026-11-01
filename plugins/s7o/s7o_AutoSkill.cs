using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpDX.DirectInput;
using SharpDX.Direct2D1;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_AutoSkill : BasePlugin, IAfterCollectHandler, IInGameTopPainter, IMouseClickHandler, INewAreaHandler, IMonsterKilledHandler, IS7oAutoLootInputHandoff, IS7oAutoLootPickupGuard
    {
        private const uint JessethArmsDamageBuffSno = 476047u;
        private const uint NayrsBlackDeathBuffSno = 476587u;
        private const uint SiphonBloodPowerSno = 453563u;
        private const uint CrusaderSteedChargePowerSno = 243853u;
        private const uint NecromancerDevourPowerSno = 460757u;
        private const uint NecromancerSimulacrumPowerSno = 465350u;
        private const uint HauntedVisionsPowerSno = 484309u;
        private const uint ShadowsMantleSetSno = 916931u;
        private const uint MasqueradeOfTheBurningCarnival2PBuffSno = 484301u;
        private const uint NecromancerLandOfTheDeadPowerSno = 465839u;
        private const int HealSlotIndex = 6;
        private const uint MonkHealingShieldBuffSno = 373154u;
        private const int MonkHealingShieldIcon = 1;
        private const int MonkHealingRetryGraceMs = 150;
        private const int MonkHealingDamageRecastMs = 450;
        private const double MonkHealingRefreshSeconds = 0.40;
        private const ushort RiftEntryDialogVirtualKey = 0x20; // Space
        private const int RiftEntryDialogStableMs = 30;
        private const int RiftEntryDialogWindowMs = 12000;
        private const string CommandSkeletonsProfileCode = "Necro_CommandSkeletons_Elite";
        private const string DevourProfileCode = "Necro_Devour_CorpseOrLotD";
        private const string NayrsBlackDeathProfileCode = "Necro_NayrsBlackDeath";

        #region Settings

        public bool EnableManualSlotAutoCast = true;
        public bool EnableAutomaticBuffUpkeep = true;
        public bool EnableConditionalAutoCastProfiles = true;
        public bool PauseWhileWindowsKeyHeld = true;

        public int ConditionalProfileRecheckMs = 25;
        public int ConditionalProfileDelayMinMs = 100;
        public int ConditionalProfileDelayMaxMs = 150;
        public int NayrsSiphonCombatWindowMs = 800;

        public bool EnableHoverToggle = true;
        public bool EnableClickToggle = false;
        public int HoverToggleMs = 300;

        public bool UseForceStandstillForCasts = true;
        public bool AllowBuffUpkeepInTown = true;
        public bool BlockLeftSkillOnSelectedClickableActor = true;
        public bool CommandSkeletonsMoveCursorToTarget = false;
        public ushort ForceStandstillVirtualKey = 0x10; // Shift

        public ushort Skill1VirtualKey = 0x31; // 1
        public ushort Skill2VirtualKey = 0x32; // 2
        public ushort Skill3VirtualKey = 0x33; // 3
        public ushort Skill4VirtualKey = 0x34; // 4
        public ushort HealVirtualKey = 0x51;   // Q

        public int GlobalCastDelayMs = 50;
        public int ManualSlotDelayMinMs = 100;
        public int ManualSlotDelayMaxMs = 100;
        public int BuffCastDelayMinMs = 150;
        public int BuffCastDelayMaxMs = 250;
        public int BuffRecheckMs = 50;
        public int SteedChargeRenewalLeadMs = 200;

        public bool EnableFastMissingBuffRetry = true;
        public int MissingBuffRetryMs = 100;
        public int EntryBuffBurstWindowMs = 1200;
        public int EntryBuffBurstGapMs = 20;
        public int EntryBuffBurstGlobalDelayMs = 20;

        public int BuffRefreshJitterChangeMs = 5000;
        public int ToggleFlashMs = 150;
        public int SkillCacheRefreshMs = 250;

        public float ToggleDotMinSize = 12.0f;
        public float ToggleDotMaxSize = 18.0f;
        public float ToggleDotScale = 0.24f;

        public bool PersistUserSettings = true;
        public bool DebugLogging = false;
        // This diagnostic must log even when the saved production setting is false.
        private readonly bool _forceDiagnosticLogging = false;
        private bool DebugLogActive { get { return _forceDiagnosticLogging || DebugLogging; } }
        private int _lastDiagnosticHeldMask = -1;
        private int _nextDiagnosticStateTick;
        private int _diagnosticResponseBurstUntilTick;
        private const int AutoCastPulseDurationMs = 50;
        private readonly S7oAutoSkillCastPulse _autoCastPulse = new S7oAutoSkillCastPulse();
        private ActionKey _autoCastPulseAction = ActionKey.Unknown;
        public int MaxDebugLogBytes = 4 * 1024 * 1024;

        #endregion

        #region Runtime State

        private readonly bool[] _enabledSlots = new bool[7];
        private readonly RectangleF[] _slotToggleRects = new RectangleF[7];
        private readonly int[] _slotFlashUntilTick = new int[7];

        private readonly Dictionary<uint, int> _lastCastTickByPower = new Dictionary<uint, int>();
        private readonly Dictionary<string, RandomNode> _randomNodes = new Dictionary<string, RandomNode>();
        private readonly Dictionary<string, int> _nextSkipLogTickByKey = new Dictionary<string, int>();
        private int _lastCommandSkeletonsRequestTick;
        private bool _commandSkeletonsBloodsongLotdCast;
        private bool _commandSkeletonsHasAimTarget;
        private int _commandSkeletonsAimX;
        private int _commandSkeletonsAimY;
        private bool _commandSkeletonsCursorRestoreKnown;
        private int _commandSkeletonsRestoreX;
        private int _commandSkeletonsRestoreY;

        private readonly List<SkillSlotState> _skillSlots = new List<SkillSlotState>();
        private readonly List<AutoBuffProfile> _buffProfiles = new List<AutoBuffProfile>();
        private readonly HashSet<uint> _buffProfileSnos = new HashSet<uint>();
        private readonly List<ConditionalCastProfile> _conditionalProfiles = new List<ConditionalCastProfile>();
        private readonly HashSet<uint> _conditionalProfileSnos = new HashSet<uint>();
        private readonly Dictionary<string, int> _lastConditionalProfileCastTickByCode = new Dictionary<string, int>();
        private readonly HashSet<uint> _channelingPowerSnos = new HashSet<uint>();
        private int _lastNayrsSiphonBloodCueTick;
        private float _lastMonkHealingShieldAmount = -1f;
        private int _lastMonkHealingShieldSampleTick;

        private int _nextSkillCacheRefreshTick;
        private int _nextBuffCheckTick;
        private int _nextConditionalProfileCheckTick;
        private int _lastGlobalCastTick;
        private int _entryBuffBurstStartTick;
        private bool _entryBuffBurstPending;
        private int _lastActMapVisibleTick;
        private int _lastWorldMapVisibleTick;

        private int _areaEnterTick;
        private bool _currentAreaIsRift;
        private int _riftEntryDialogVisibleSinceTick;
        private bool _riftEntryDialogSpaceSent;
        private SteedRenewalStage _steedRenewalStage;

        private int _hoverSlotIndex = -1;
        private int _hoverStartTick;
        private int _hoverCompletedSlot = -1;

        private string _settingsPath;
        private string _legacySettingsPath;
        private string _debugLogPath;

        private Random _random;

        private IBrush _dotEnabledBrush;
        private IBrush _dotDisabledBrush;
        private IBrush _dotHoverBrush;
        private IBrush _dotFlashBrush;
        private IBrush _dotEnabledHighlightBrush;
        private IBrush _dotDisabledHighlightBrush;
        private IBrush _dotHoverHighlightBrush;
        private IBrush _dotShadowBrush;
        private IBrush _dotBorderBrush;

        private IUiElement _chatEditLine;
        private IUiElement _skillPaneSkillsList;
        private IUiElement _vendorMainPage;
        private IUiElement _shopMainPanel;
        private IUiElement _conversationDialog;
        private IUiElement _scriptedSequenceDialog;

        #endregion

        #region Data Models

        private sealed class SkillSlotState
        {
            public int SlotIndex;
            public ActionKey ActionKey;
            public IPlayerSkill Skill;
            public uint Sno;
            public string Code;
            public RectangleF UiRect;
            public bool IsValid;
            public bool IsManualEnabled;
        }

        private sealed class AutoBuffProfile
        {
            public uint Sno;
            public string Code;
            public int IconIndex = -1;
            public int RefreshMinMs;
            public int RefreshMaxMs;
            public int CastDelayMinMs;
            public int CastDelayMaxMs;
            public int ReservePrimaryResource;
            public float BasePrimaryResourceRequirement;
            public bool Enabled = true;
        }

        private sealed class ConditionalCastProfile
        {
            public uint Sno;
            public string Code;
            public string DisplayName;
            public string ClassName;
            public string GroupName;
            public string Description;

            public bool Enabled = true;
            public bool DefaultEnabled = true;

            public bool AllowInTown = false;
            public bool UseStandstill = true;
            public bool AllowChannelingSkill = false;

            public int CastDelayMinMs;
            public int CastDelayMaxMs;
            public int RecheckMs;

            public int ReservePrimaryResource;
            public int ReserveSecondaryResource;
            public float BasePrimaryResourceRequirement;
            public float BaseSecondaryResourceRequirement;

            public Func<ConditionalCastContext, bool> ShouldCast;
        }

        private sealed class ConditionalCastContext
        {
            public IController Hud;
            public IPlayer Me;
            public SkillSlotState Slot;
            public IPlayerSkill Skill;
            public ConditionalCastProfile Profile;
            public int Now;
        }

        private sealed class RandomNode
        {
            public int LastChangedTick;
            public int Value;
        }

        private enum SteedRenewalStage
        {
            Idle,
            WaitingForDismount,
            WaitingForRemount
        }

        #endregion

        #region Load / Reset

        public s7o_AutoSkill()
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            _random = new Random(Environment.TickCount);

            InitializePluginPaths();
            CreateRenderResources();
            RegisterUiElements();
            BuildBuffProfiles();
            BuildConditionalCastProfiles();
            BuildImportedPolicies();
            LoadUserSettings();
            BuildChannelingSkillSet();
            RefreshSkillCache(true);
            _currentAreaIsRift = IsRiftArea(Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.SnoArea : null);

            if (PersistUserSettings && !File.Exists(_settingsPath))
                SaveUserSettings();

            LogDebug("DIAGNOSTIC_START version=REV09 forcedLogging="+_forceDiagnosticLogging
                +", savedDebugLogging="+DebugLogging+", output="+_debugLogPath
                +", heldMaskIncludesSynthetic=True, castMessagesMean=INPUT_REQUESTS-not-confirmed-game-casts");
            LogDebug("s7o_AutoSkill loaded. BuffProfiles="
                + _buffProfiles.Count.ToString(CultureInfo.InvariantCulture)
                + ", ConditionalProfiles="
                + _conditionalProfiles.Count.ToString(CultureInfo.InvariantCulture));
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            // OnNewArea supplies the authoritative new SNO area immediately, while
            // Hud.Game.SpecialArea can still describe the previous area for a short time.
            _currentAreaIsRift = IsRiftArea(area);
            StopAutoCastPulse();
            StopImportedChannel();
            ResetImportedPolicies();
            _importedLootPauseTick=int.MinValue; // A pickup grace period cannot cross areas or games.
            _importedUrshiQuiet=false;
            S7oHealingMantraPolicy.OnNewArea(newGame);

            _lastGlobalCastTick = 0;
            _lastCastTickByPower.Clear();
            _nextSkipLogTickByKey.Clear();
            _nextSkillCacheRefreshTick = 0;
            _nextBuffCheckTick = 0;
            _nextConditionalProfileCheckTick = 0;
            _lastConditionalProfileCastTickByCode.Clear();
            _lastNayrsSiphonBloodCueTick = 0;
            _lastMonkHealingShieldAmount = -1f;
            _lastMonkHealingShieldSampleTick = 0;
            _entryBuffBurstPending = true;
            _entryBuffBurstStartTick = 0;
            _lastActMapVisibleTick = 0;
            _lastWorldMapVisibleTick = 0;
            _steedRenewalStage = SteedRenewalStage.Idle;
            _lastCommandSkeletonsRequestTick = 0;
            _commandSkeletonsBloodsongLotdCast = false;


            _areaEnterTick = Environment.TickCount;
            _riftEntryDialogVisibleSinceTick = 0;
            _riftEntryDialogSpaceSent = false;
            ResetHoverToggle();
            RefreshSkillCache(true);
            LogDebug("New area reset.");
        }

        private void CreateRenderResources()
        {
            _dotEnabledBrush = Hud.Render.CreateBrush(245, 20, 185, 70, 0, DashStyle.Solid);
            _dotDisabledBrush = Hud.Render.CreateBrush(245, 190, 25, 25, 0, DashStyle.Solid);
            _dotHoverBrush = Hud.Render.CreateBrush(245, 255, 185, 35, 0, DashStyle.Solid);
            _dotFlashBrush = Hud.Render.CreateBrush(255, 255, 230, 80, 0, DashStyle.Solid);

            _dotEnabledHighlightBrush = Hud.Render.CreateBrush(170, 150, 255, 180, 0, DashStyle.Solid);
            _dotDisabledHighlightBrush = Hud.Render.CreateBrush(170, 255, 145, 145, 0, DashStyle.Solid);
            _dotHoverHighlightBrush = Hud.Render.CreateBrush(170, 255, 245, 150, 0, DashStyle.Solid);

            _dotShadowBrush = Hud.Render.CreateBrush(160, 0, 0, 0, 0, DashStyle.Solid);
            _dotBorderBrush = Hud.Render.CreateBrush(230, 0, 0, 0, 1, DashStyle.Solid);
        }

        private void RegisterUiElements()
        {
            _chatEditLine = RegisterOrGetUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline");
            _skillPaneSkillsList = RegisterOrGetUiElement("Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList");
            _vendorMainPage = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage");
            _shopMainPanel = RegisterOrGetUiElement("Root.NormalLayer.shop_dialog_mainPage.panel");
            _conversationDialog = RegisterOrGetUiElement("Root.NormalLayer.conversation_dialog_main");
            _scriptedSequenceDialog = RegisterOrGetUiElement("Root.TopLayer.scripted_sequence");
        }

        #endregion

        #region AfterCollect Main Loop

        public void AfterCollect()
        {
            // Shared recovery also runs when either companion plugin is disabled.
            if (Hud != null && Hud.Window != null && Hud.Window.IsForeground)
                s7o_InputReleaseArbiter.RetryPending(Environment.TickCount);

            int now = Environment.TickCount;
            ServiceAutoCastPulse(now);
            if (_autoCastPulse.Active)
            {
                ObserveDiagnosticCastState(now);
                return;
            }
            if (PauseWhileWindowsKeyHeld && IsWindowsKeyDown())
            {
                StopImportedChannel();
                ResetHoverToggle();
                return;
            }

            UpdateRecentMapTicks(now);
            S7oMacroQueries.Begin(this, Hud, now);
            RefreshImportedOverride();
            UpdateImportedChannel(now);
            RefreshSkillCacheIfNeeded(now);
            ObserveDiagnosticCastState(now);
            UpdateNayrsSiphonBloodCue(now);
            UpdateHoverToggle(now);

            if (HandleBlockingRiftDialog(now))
                return;

            string standardBlockedReason;
            bool standardContextValid = IsValidAutomationContext(false, out standardBlockedReason);

            if (EnableManualSlotAutoCast
                && _enabledSlots[HealSlotIndex]
                && standardContextValid
                && TryRunHealSlotAutoCast(now))
            {
                return;
            }

            if (EnableAutomaticBuffUpkeep && IsTickReached(now, _nextBuffCheckTick))
            {
                string buffBlockedReason = standardBlockedReason;
                bool buffContextValid = standardContextValid;

                if (AllowBuffUpkeepInTown && Hud != null && Hud.Game != null && Hud.Game.IsInTown)
                    buffContextValid = IsValidAutomationContext(true, out buffBlockedReason);

                if (buffContextValid)
                {
                    StartEntryBuffBurstIfNeeded(now);
                    _nextBuffCheckTick = now + GetBuffCheckInterval(now);

                    if (TryRunAutomaticBuffUpkeep(now))
                        return;
                }
                else
                {
                    LogContextBlockedThrottled("buff: " + buffBlockedReason, now);
                }
            }

            if (EnableConditionalAutoCastProfiles && IsTickReached(now, _nextConditionalProfileCheckTick))
            {
                if (standardContextValid)
                {
                    _nextConditionalProfileCheckTick = now + Math.Max(25, ConditionalProfileRecheckMs);

                    if (TryRunConditionalAutoCastProfiles(now))
                        return;
                }
                else if (StringEquals(standardBlockedReason, "player busy")
                    && IsOwnBuffActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno)
                    && !IsHardBusyOrTeleportMovementForLotdDevour())
                {
                    _nextConditionalProfileCheckTick = now + Math.Max(25, ConditionalProfileRecheckMs);

                    if (TryRunConditionalAutoCastProfiles(now, true))
                        return;
                }
                else
                {
                    LogContextBlockedThrottled("conditional: " + standardBlockedReason, now);
                }
            }

            if (!standardContextValid)
            {
                LogContextBlockedThrottled("manual: " + standardBlockedReason, now);
                return;
            }

            if (EnableManualSlotAutoCast)
                TryRunManualSlotAutoCast(now);
        }

        private void UpdateHoverToggle(int now)
        {
            if (!EnableHoverToggle)
            {
                ResetHoverToggle();
                return;
            }

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground || IsSkillPaneVisible())
            {
                ResetHoverToggle();
                return;
            }

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;
            int hoveredSlot = -1;

            for (int i = 0; i < _slotToggleRects.Length; i++)
            {
                if (PointInRect(_slotToggleRects[i], x, y))
                {
                    hoveredSlot = i;
                    break;
                }
            }

            if (hoveredSlot < 0)
            {
                ResetHoverToggle();
                return;
            }

            if (_hoverSlotIndex != hoveredSlot)
            {
                _hoverSlotIndex = hoveredSlot;
                _hoverStartTick = now;
                _hoverCompletedSlot = -1;
                return;
            }

            if (_hoverCompletedSlot == hoveredSlot)
                return;

            if ((uint)(now - _hoverStartTick) >= (uint)Math.Max(250, HoverToggleMs))
            {
                ToggleSlotAutomation(hoveredSlot, "hover");
                _hoverCompletedSlot = hoveredSlot;
            }
        }

        private void ResetHoverToggle()
        {
            _hoverSlotIndex = -1;
            _hoverStartTick = 0;
            _hoverCompletedSlot = -1;
        }

        #endregion

        #region Context Guard

        private bool IsValidAutomationContext(bool allowTown, out string reason)
        {
            reason = null;

            if (!Enabled) { reason = "plugin disabled"; return false; }
            if (_importedUrshiQuiet) { reason="Urshi handoff";return false; }
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null) { reason = "hud/game/me null"; return false; }
            if (!Hud.Game.IsInGame) { reason = "not in game"; return false; }
            if (Hud.Game.IsLoading) { reason = "loading"; return false; }
            if (Hud.Game.IsPaused) { reason = "paused"; return false; }
            if (Hud.Game.IsInTown && !allowTown) { reason = "in town"; return false; }
            if (Hud.Window == null || !Hud.Window.IsForeground) { reason = "window not foreground"; return false; }
            if (PauseWhileWindowsKeyHeld && IsWindowsKeyDown()) { reason = "windows key held"; return false; }
            if (Hud.Render == null || Hud.Render.MinimapUiElement == null || !IsUiVisible(Hud.Render.MinimapUiElement)) { reason = "minimap hidden"; return false; }

            var loot=Hud.GetPlugin<s7o_AutoLoot>();
            if((loot!=null&&loot.Enabled&&loot.HasPendingPickupInput)||(_importedLootPauseTick!=int.MinValue&&unchecked((uint)(Hud.Game.CurrentGameTick-_importedLootPauseTick))<=2u))
            { reason="loot handoff";return false; }
            var me = Hud.Game.Me;
            if (me.IsDead || me.IsDeadSafeCheck || me.Defense == null || me.Powers == null || me.Stats == null)
            {
                reason = "dead or missing player data";
                return false;
            }

            if (WasMapRecentlyVisible())
            {
                reason = "map recently visible";
                return false;
            }

            // Keep AutoSkill quiet for the completed-GR Urshi reward phase. The gem
            // pane already blocks casts while visible, but it can close before the
            // town-portal animation is observable. Without this handoff guard an
            // enabled skill can fire in that gap and cancel the portal request.
            if (IsUrshiRewardInteractionActive())
            {
                reason = "urshi reward active";
                return false;
            }

            if (IsBlockingUiOpen())
            {
                reason = "blocking ui open";
                return false;
            }

            if (IsPlayerBusy())
            {
                reason = "player busy";
                return false;
            }

            return true;
        }

        private void UpdateRecentMapTicks(int now)
        {
            try
            {
                if (Hud == null || Hud.Game == null)
                    return;

                var mapMode = Hud.Game.MapMode;

                if (mapMode == MapMode.ActMap)
                    _lastActMapVisibleTick = now;

                if (mapMode == MapMode.Map || mapMode == MapMode.PermaMap || mapMode == MapMode.WaypointMap)
                    _lastWorldMapVisibleTick = now;
            }
            catch
            {
            }
        }

        private bool WasMapRecentlyVisible()
        {
            int now = Environment.TickCount;
            return (_lastActMapVisibleTick != 0 && (uint)(now - _lastActMapVisibleTick) < 500u)
                || (_lastWorldMapVisibleTick != 0 && (uint)(now - _lastWorldMapVisibleTick) < 500u);
        }

        private bool IsBlockingUiOpen()
        {
            if (IsSkillPaneVisible()) return true;
            if (IsUiVisible(_conversationDialog)) return true;
            if (IsUiVisible(_scriptedSequenceDialog)) return true;
            if (IsUiVisible(_chatEditLine)) return true;
            if (IsUiVisible(_vendorMainPage)) return true;
            if (IsUiVisible(_shopMainPanel)) return true;

            try
            {
                if (Hud.Inventory != null)
                {
                    if (IsUiVisible(Hud.Inventory.InventoryMainUiElement)) return true;
                    if (IsUiVisible(Hud.Inventory.StashMainUiElement)) return true;
                    if (IsUiVisible(Hud.Inventory.FollowerMainUiElement)) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool HandleBlockingRiftDialog(int now)
        {
            bool conversationVisible = IsUiVisible(_conversationDialog);
            bool scriptedVisible = IsUiVisible(_scriptedSequenceDialog);

            if (!conversationVisible && !scriptedVisible)
            {
                _riftEntryDialogVisibleSinceTick = 0;
                return false;
            }

            // AutoSkill must remain paused while either layer is visible. The normal
            // Rift-entry dialog is a one-shot Space acknowledgement; never use Escape
            // and never repeat Space for the same area/dialog episode.
            if (!_riftEntryDialogSpaceSent &&
                Hud != null && Hud.Game != null && Hud.Window != null &&
                Hud.Window.IsForeground && !Hud.Game.IsLoading && !Hud.Game.IsPaused && !Hud.Game.IsInTown &&
                (_currentAreaIsRift || Hud.Game.SpecialArea == SpecialArea.Rift || Hud.Game.SpecialArea == SpecialArea.GreaterRift) &&
                Hud.Game.RiftPercentage < 100.0d &&
                !IsUiVisible(_chatEditLine) &&
                _areaEnterTick != 0 && unchecked(now - _areaEnterTick) <= RiftEntryDialogWindowMs)
            {
                if (_riftEntryDialogVisibleSinceTick == 0)
                    _riftEntryDialogVisibleSinceTick = now;

                if (unchecked(now - _riftEntryDialogVisibleSinceTick) >= RiftEntryDialogStableMs)
                {
                    _riftEntryDialogSpaceSent = true;
                    PressKey(RiftEntryDialogVirtualKey);
                    LogDebug("Acknowledged Rift entry dialog with Space.");
                }
            }

            return true;
        }

        private static bool IsRiftArea(ISnoArea area)
        {
            try
            {
                // Generated Nephalem/Greater Rift floors use X1_LR_Level_* area codes.
                // Follow HostSnoArea for child/sub-areas hosted by the generated floor.
                ISnoArea current = area;
                for (int depth = 0; current != null && depth < 3; depth++)
                {
                    string code = current.Code;
                    if (!string.IsNullOrEmpty(code)
                        && code.StartsWith("X1_LR_Level_", StringComparison.OrdinalIgnoreCase))
                        return true;

                    current = current.HostSnoArea;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsUrshiRewardInteractionActive()
        {
            if (Hud == null ||
                Hud.Game == null ||
                Hud.Game.SpecialArea != SpecialArea.GreaterRift ||
                Hud.Game.RiftPercentage < 100.0d)
            {
                return false;
            }

            try
            {
                return Hud.Game.Actors.Any(actor =>
                    actor != null &&
                    actor.SnoActor != null &&
                    actor.SnoActor.Sno == ActorSnoEnum._p1_lr_tieredrift_nephalem);
            }
            catch
            {
                return false;
            }
        }

        private bool IsPlayerBusy()
        {
            var me = Hud.Game.Me;
            var powers = me.Powers;

            if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyAllWithCast.Sno)) return true;
            if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCast.Sno)) return true;
            if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCastLegendary.Sno)) return true;
            if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_AxeOperateGizmo.Sno)) return true;
            if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno)) return true;

            if (me.AnimationState == AcdAnimationState.Transform) return true;
            if (me.AnimationState == AcdAnimationState.CastingPortal) return true;
            if (me.AnimationState == AcdAnimationState.Dead) return true;

            return false;
        }

        private bool IsHardBusyOrTeleportMovementForLotdDevour()
        {
            try
            {
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (me == null)
                    return true;

                var powers = me.Powers;
                if (powers == null)
                    return true;

                if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyAllWithCast.Sno)) return true;
                if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCast.Sno)) return true;
                if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_IdentifyWithCastLegendary.Sno)) return true;
                if (powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_AxeOperateGizmo.Sno)) return true;

                if (me.AnimationState == AcdAnimationState.CastingPortal) return true;
                if (me.AnimationState == AcdAnimationState.Dead) return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        private bool IsSkillPaneVisible()
        {
            return IsUiVisible(_skillPaneSkillsList);
        }

        private bool IsUiVisible(IUiElement ui)
        {
            if (ui == null)
                return false;

            try
            {
                ui.Refresh();
                return ui.Visible;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Skill Cache / Resolution

        private void RefreshSkillCacheIfNeeded(int now)
        {
            if (!IsTickReached(now, _nextSkillCacheRefreshTick))
                return;

            _nextSkillCacheRefreshTick = now + Math.Max(50, SkillCacheRefreshMs);
            RefreshSkillCache(false);
        }

        private void RefreshSkillCache(bool force)
        {
            _skillSlots.Clear();
            ClearSlotRects();

            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null)
                return;

            var powers = Hud.Game.Me.Powers;
            var slots = powers.SkillOverrideActive ? powers.CurrentSkills.ToArray() : powers.SkillSlots;

            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var skill = slots[i];
                    if (skill == null || skill.SnoPower == null)
                        continue;

                    int slotIndex = SlotIndexFromActionKey(skill.Key);
                    if (slotIndex < 0 || slotIndex > 5)
                        slotIndex = i >= 0 && i <= 5 ? i : -1;

                    if (slotIndex < 0 || slotIndex > 5)
                        continue;

                    AddOrUpdateSkillSlot(slotIndex, skill.Key, skill);
                }
            }

            try
            {
                var potion = powers.HealthPotionSkill;
                if (potion != null && potion.SnoPower != null)
                    AddOrUpdateSkillSlot(HealSlotIndex, ActionKey.Heal, potion);
            }
            catch
            {
            }
        }

        private void AddOrUpdateSkillSlot(int slotIndex, ActionKey actionKey, IPlayerSkill skill)
        {
            if (slotIndex < 0 || slotIndex >= _enabledSlots.Length)
                return;

            var ui = SafeGetPlayerSkillUiElement(actionKey);
            var uiRect = ui != null ? ui.Rectangle : RectangleF.Empty;
            var toggleRect = BuildToggleRect(uiRect);

            _slotToggleRects[slotIndex] = toggleRect;

            var state = new SkillSlotState
            {
                SlotIndex = slotIndex,
                ActionKey = actionKey,
                Skill = skill,
                Sno = skill != null && skill.SnoPower != null ? skill.SnoPower.Sno : 0,
                Code = skill != null && skill.SnoPower != null ? skill.SnoPower.Code : null,
                UiRect = uiRect,
                IsValid = skill != null && skill.SnoPower != null && uiRect.Width > 0 && uiRect.Height > 0,
                IsManualEnabled = _enabledSlots[slotIndex]
            };

            _skillSlots.RemoveAll(s => s.SlotIndex == slotIndex);
            _skillSlots.Add(state);
        }

        private IPlayerSkill ResolveSkillForSlot(int slotIndex)
        {
            RefreshSkillCacheIfNeeded(Environment.TickCount);

            var state = FindSlotState(slotIndex);
            if (state == null || !state.IsValid || state.Skill == null)
                return null;

            return state.Skill;
        }

        private SkillSlotState FindSlotState(int slotIndex)
        {
            for (int i = 0; i < _skillSlots.Count; i++)
            {
                if (_skillSlots[i] != null && _skillSlots[i].SlotIndex == slotIndex)
                    return _skillSlots[i];
            }

            return null;
        }

        private bool IsSlotAutomationEnabled(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < _enabledSlots.Length)
                return _enabledSlots[slotIndex];

            return false;
        }

        private SkillSlotState FindEquippedSkillBySno(uint sno)
        {
            for (int i = 0; i < _skillSlots.Count; i++)
            {
                var slot = _skillSlots[i];
                if (slot == null || !slot.IsValid || slot.Skill == null || slot.Skill.SnoPower == null)
                    continue;

                if (slot.SlotIndex == HealSlotIndex && sno != Hud.Sno.SnoPowers.Generic_DrinkHealthPotion.Sno)
                    continue;

                if (slot.Skill.SnoPower.Sno == sno)
                    return slot;
            }

            return null;
        }

        private int SlotIndexFromActionKey(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0;
                case ActionKey.RightSkill: return 1;
                case ActionKey.Skill1: return 2;
                case ActionKey.Skill2: return 3;
                case ActionKey.Skill3: return 4;
                case ActionKey.Skill4: return 5;
                case ActionKey.Heal: return HealSlotIndex;
                default: return -1;
            }
        }

        private ActionKey ActionKeyFromSlotIndex(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return ActionKey.LeftSkill;
                case 1: return ActionKey.RightSkill;
                case 2: return ActionKey.Skill1;
                case 3: return ActionKey.Skill2;
                case 4: return ActionKey.Skill3;
                case 5: return ActionKey.Skill4;
                case HealSlotIndex: return ActionKey.Heal;
                default: return ActionKey.Unknown;
            }
        }

        private IUiElement SafeGetPlayerSkillUiElement(ActionKey actionKey)
        {
            try
            {
                return Hud.Render.GetPlayerSkillUiElement(actionKey);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Manual Slot Auto Cast

        private bool TryRunHealSlotAutoCast(int now)
        {
            if (ImportedPotionEnabled()) return false;
            var slot = FindSlotState(HealSlotIndex);
            if (slot == null || !slot.IsValid || slot.Skill == null)
                return false;

            var skill = ResolveSkillForSlot(HealSlotIndex);
            if (skill == null || skill.SnoPower == null)
                return false;

            return TryManualCastSlot(HealSlotIndex, slot, skill, now);
        }

        private bool TryRunManualSlotAutoCast(int now)
        {
            bool deferredSteedCharge = false;

            for (int slotIndex = 0; slotIndex < _enabledSlots.Length; slotIndex++)
            {
                if (slotIndex == HealSlotIndex)
                    continue;

                if (!_enabledSlots[slotIndex])
                    continue;

                var slot = FindSlotState(slotIndex);
                if (slot == null || !slot.IsValid || slot.Skill == null)
                    continue;

                var skill = ResolveSkillForSlot(slotIndex);
                if (skill == null || skill.SnoPower == null)
                    continue;

                if (IsCrusaderSteedChargeSkill(skill))
                {
                    deferredSteedCharge = true;
                    continue;
                }

                if (TryManualCastSlot(slotIndex, slot, skill, now))
                    return true;
            }

            if (!deferredSteedCharge)
            {
                _steedRenewalStage = SteedRenewalStage.Idle;
                return false;
            }

            for (int slotIndex = 0; slotIndex < _enabledSlots.Length; slotIndex++)
            {
                if (slotIndex == HealSlotIndex)
                    continue;

                if (!_enabledSlots[slotIndex])
                    continue;

                var slot = FindSlotState(slotIndex);
                if (slot == null || !slot.IsValid || slot.Skill == null)
                    continue;

                var skill = ResolveSkillForSlot(slotIndex);
                if (skill == null || skill.SnoPower == null || !IsCrusaderSteedChargeSkill(skill))
                    continue;

                if (TryRunSteedChargeSlot(slotIndex, slot, skill, now))
                    return true;
            }

            return false;
        }

        private bool TryRunSteedChargeSlot(int slotIndex, SkillSlotState slot, IPlayerSkill skill, int now)
        {
            bool active;
            double remaining;
            if (!TryGetBuffRemainingSeconds(skill, -1, out active, out remaining))
            {
                _steedRenewalStage = SteedRenewalStage.Idle;
                return TryManualCastSlot(slotIndex, slot, skill, now);
            }

            if (_steedRenewalStage == SteedRenewalStage.WaitingForDismount)
            {
                if (active)
                    return false;

                _steedRenewalStage = SteedRenewalStage.WaitingForRemount;
            }

            if (_steedRenewalStage == SteedRenewalStage.WaitingForRemount)
            {
                if (active)
                {
                    _steedRenewalStage = SteedRenewalStage.Idle;
                    LogDebug("Steed Charge renewal confirmed.");
                    return false;
                }

                return TryManualCastSlot(slotIndex, slot, skill, now);
            }

            if (!active)
            {
                if (!TryManualCastSlot(slotIndex, slot, skill, now))
                    return false;

                _steedRenewalStage = SteedRenewalStage.WaitingForRemount;
                return true;
            }

            if (!IsFiniteBuffTime(remaining) ||
                remaining > Math.Max(0, SteedChargeRenewalLeadMs) / 1000.0d)
            {
                return false;
            }

            if (!IsActionCursorSafe(slot.ActionKey) ||
                (uint)(now - _lastGlobalCastTick) < (uint)Math.Max(0, GlobalCastDelayMs))
            {
                return false;
            }

            if (!DoAction(slot.ActionKey, UseForceStandstillForCasts))
                return false;

            MarkSkillCast(skill, now, slot.ActionKey);
            _steedRenewalStage = SteedRenewalStage.WaitingForDismount;
            LogDebug("Steed Charge renewal dismount requested, remaining="
                + remaining.ToString("0.000", CultureInfo.InvariantCulture));
            return true;
        }

        private bool TryManualCastSlot(int slotIndex, SkillSlotState slot, IPlayerSkill skill, int now)
        {
            int delay = GetRandomizedDelay("manual_" + skill.SnoPower.Sno.ToString(CultureInfo.InvariantCulture), ManualSlotDelayMinMs, ManualSlotDelayMaxMs, 1000);

            string reason;
            if (!CanCastSkill(skill, slot.ActionKey, now, delay, out reason))
            {
                LogDebugCastSkipThrottled("manual", skill, reason, now);
                return false;
            }

            if (DoAction(slot.ActionKey, UseForceStandstillForCasts))
            {
                MarkSkillCast(skill, now, slot.ActionKey);
                LogDebug("Manual auto-cast: slot=" + slotIndex.ToString(CultureInfo.InvariantCulture) + ", skill=" + SafeSkillCode(skill));
                return true;
            }

            return false;
        }

        #endregion

        #region Automatic Buff Upkeep

        private void BuildBuffProfiles()
        {
            _buffProfiles.Clear();
            _buffProfileSnos.Clear();

            AddBuffProfile(GetSno(Hud.Sno.SnoPowers.Wizard_MagicWeapon, 76108), "Wizard_MagicWeapon", 55000, 65000, BuffCastDelayMinMs, BuffCastDelayMaxMs, 0, 0);
            AddBuffProfile(GetSno(Hud.Sno.SnoPowers.Wizard_EnergyArmor, 86991), "Wizard_EnergyArmor", 55000, 65000, BuffCastDelayMinMs, BuffCastDelayMaxMs, 0, 0);
            AddBuffProfile(GetSno(Hud.Sno.SnoPowers.Wizard_IceArmor, 73223), "Wizard_IceArmor", 55000, 65000, BuffCastDelayMinMs, BuffCastDelayMaxMs, 0, 0);
            AddBuffProfile(GetSno(Hud.Sno.SnoPowers.Wizard_StormArmor, 74499), "Wizard_StormArmor", 1000, 2000, BuffCastDelayMinMs, BuffCastDelayMaxMs, 50, 25);
            AddBuffProfile(GetSno(Hud.Sno.SnoPowers.Wizard_Familiar, 99120), "Wizard_Familiar", 1000, 2000, BuffCastDelayMinMs, BuffCastDelayMaxMs, 0, 0);
        }

        private void AddBuffProfile(uint sno, string code, int refreshMinMs, int refreshMaxMs, int castDelayMinMs, int castDelayMaxMs, int reservePrimaryResource, float basePrimaryResourceRequirement)
        {
            if (sno == 0)
                return;

            var profile = new AutoBuffProfile
            {
                Sno = sno,
                Code = code,
                IconIndex = -1,
                RefreshMinMs = refreshMinMs,
                RefreshMaxMs = refreshMaxMs,
                CastDelayMinMs = castDelayMinMs,
                CastDelayMaxMs = castDelayMaxMs,
                ReservePrimaryResource = reservePrimaryResource,
                BasePrimaryResourceRequirement = basePrimaryResourceRequirement,
                Enabled = true
            };

            _buffProfiles.Add(profile);
            _buffProfileSnos.Add(sno);
        }

        private bool TryRunAutomaticBuffUpkeep(int now)
        {
            bool burstActive = IsEntryBuffBurstActive(now);

            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var profile = _buffProfiles[i];
                if (profile == null || !profile.Enabled)
                    continue;

                var slot = FindEquippedSkillBySno(profile.Sno);
                if (slot == null || slot.Skill == null)
                    continue;

                bool missing;
                double remaining;
                if (!IsBuffMissingOrExpiring(slot.Skill, profile, now, out missing, out remaining))
                {
                    if(IsDiagnosticFocusSkill(slot.Skill))
                        LogDebugCastSkipThrottled("policy",slot.Skill,profile.Code+": buff-not-due-or-policy-no-cast",now);
                    continue;
                }

                int delay = GetBuffCastDelay(profile, missing);
                int globalDelay = burstActive && missing ? Math.Max(0, EntryBuffBurstGlobalDelayMs) : GlobalCastDelayMs;

                string reason;
                if (!CanCastSkill(slot.Skill, slot.ActionKey, now, delay, out reason, globalDelay))
                {
                    LogDebugCastSkipThrottled("buff", slot.Skill, reason, now);
                    continue;
                }

                if (!HasEnoughPrimaryResource(slot.Skill, profile.ReservePrimaryResource, profile.BasePrimaryResourceRequirement))
                {
                    LogDebugCastSkipThrottled("buff", slot.Skill, "reserved resource", now);
                    continue;
                }

                if (DoAction(slot.ActionKey, UseForceStandstillForCasts))
                {
                    MarkSkillCast(slot.Skill, now, slot.ActionKey);
                    ImportedCastAccepted(slot.Skill, profile.Code);
                    _nextBuffCheckTick = now + GetBuffCheckIntervalAfterCast(now, missing);
                    LogDebug("Buff upkeep cast: " + profile.Code
                        + ", missing=" + missing.ToString(CultureInfo.InvariantCulture)
                        + ", burst=" + burstActive.ToString(CultureInfo.InvariantCulture)
                        + ", remaining=" + remaining.ToString("0.0", CultureInfo.InvariantCulture));
                    return true;
                }
                LogDebugCastSkipThrottled("buff",slot.Skill,profile.Code+": input-not-sent",now);
            }

            return false;
        }

        private bool IsBuffMissingOrExpiring(IPlayerSkill skill, AutoBuffProfile profile, int now)
        {
            bool missing;
            double remaining;
            return IsBuffMissingOrExpiring(skill, profile, now, out missing, out remaining);
        }

        private bool IsBuffMissingOrExpiring(IPlayerSkill skill, AutoBuffProfile profile, int now, out bool missing, out double remaining)
        {
            if (profile != null && _importedBuffBindings.ContainsKey(profile.Sno))
                return ImportedBuffDue(skill,profile,now,out missing,out remaining);
            missing = true;
            remaining = 0;

            bool active;
            if (!TryGetBuffRemainingSeconds(skill, profile.IconIndex, out active, out remaining))
                return true;

            if (!active)
                return true;

            missing = false;

            if (double.IsPositiveInfinity(remaining) || remaining == double.MaxValue)
                return false;

            int thresholdMs = GetRandomizedDelay("buff_refresh_" + profile.Sno.ToString(CultureInfo.InvariantCulture), profile.RefreshMinMs, profile.RefreshMaxMs, Math.Max(profile.RefreshMaxMs, BuffRefreshJitterChangeMs));
            return remaining <= thresholdMs / 1000.0d;
        }

        private int GetBuffCastDelay(AutoBuffProfile profile, bool missing)
        {
            if (EnableFastMissingBuffRetry && missing)
                return Math.Max(0, MissingBuffRetryMs);

            return GetRandomizedDelay("buff_cast_" + profile.Sno.ToString(CultureInfo.InvariantCulture), profile.CastDelayMinMs, profile.CastDelayMaxMs, 1000);
        }

        private int GetBuffCheckInterval(int now)
        {
            if (IsEntryBuffBurstActive(now))
                return Math.Max(1, EntryBuffBurstGapMs);

            return Math.Max(25, BuffRecheckMs);
        }

        private int GetBuffCheckIntervalAfterCast(int now, bool missing)
        {
            if (missing && IsEntryBuffBurstActive(now))
                return Math.Max(1, EntryBuffBurstGapMs);

            if (EnableFastMissingBuffRetry && missing)
                return Math.Max(50, Math.Min(BuffRecheckMs, MissingBuffRetryMs));

            return GetBuffCheckInterval(now);
        }

        private void StartEntryBuffBurstIfNeeded(int now)
        {
            if (!EnableFastMissingBuffRetry || !_entryBuffBurstPending)
                return;

            _entryBuffBurstPending = false;
            _entryBuffBurstStartTick = now;
            LogDebug("Entry buff burst started.");
        }

        private bool IsEntryBuffBurstActive(int now)
        {
            return EnableFastMissingBuffRetry
                && _entryBuffBurstStartTick != 0
                && (uint)(now - _entryBuffBurstStartTick) < (uint)Math.Max(0, EntryBuffBurstWindowMs);
        }

        private double GetRemainingBuffSeconds(IPlayerSkill skill, int iconIndex)
        {
            bool active;
            double remaining;
            if (!TryGetBuffRemainingSeconds(skill, iconIndex, out active, out remaining))
                return 0;

            return active ? remaining : 0;
        }

        private bool TryGetBuffRemainingSeconds(IPlayerSkill skill, int iconIndex, out bool active, out double remaining)
        {
            active = false;
            remaining = 0;

            if (skill == null || skill.SnoPower == null || skill.Player == null || skill.Player.Powers == null)
                return false;

            var buff = skill.Player.Powers.GetBuff(skill.SnoPower.Sno);
            if (buff == null || !buff.Active)
                return true;

            active = true;

            if (buff.TimeLeftSeconds == null || buff.TimeLeftSeconds.Length == 0)
            {
                remaining = double.MaxValue;
                return true;
            }

            if (iconIndex >= 0 && iconIndex < buff.TimeLeftSeconds.Length)
            {
                remaining = S7oMacroData.Seconds((buff)?.TimeLeftSeconds, iconIndex);
                return true;
            }

            double max = 0;
            for (int i = 0; i < buff.TimeLeftSeconds.Length; i++)
            {
                if (S7oMacroData.Seconds((buff)?.TimeLeftSeconds, i) > max)
                    max = S7oMacroData.Seconds((buff)?.TimeLeftSeconds, i);
            }

            remaining = max;
            return true;
        }


        private AutoBuffProfile FindBuffProfile(uint sno)
        {
            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var profile = _buffProfiles[i];
                if (profile != null && profile.Sno == sno)
                    return profile;
            }

            return null;
        }

        #endregion

        #region Conditional Auto Cast Profiles

        private void BuildConditionalCastProfiles()
        {
            _conditionalProfiles.Clear();
            _conditionalProfileSnos.Clear();

            BuildDemonHunterConditionalProfiles();
            BuildBarbarianConditionalProfiles();
            BuildCrusaderConditionalProfiles();
            BuildMonkConditionalProfiles();
            BuildNecromancerConditionalProfiles();
            BuildWitchDoctorConditionalProfiles();
            BuildWizardConditionalProfiles();
        }

        private bool TryRunConditionalAutoCastProfiles(int now, bool lotdDevourOnly = false)
        {
            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var profile = _conditionalProfiles[i];
                if (profile == null || !profile.Enabled || profile.Sno == 0)
                    continue;

                if (lotdDevourOnly && !StringEquals(profile.Code, DevourProfileCode))
                    continue;

                var slot = FindEquippedSkillBySno(profile.Sno);
                if (slot == null || slot.Skill == null || slot.Skill.SnoPower == null)
                    continue;

                if (profile.RecheckMs > 0)
                {
                    int lastProfileCast;
                    if (_lastConditionalProfileCastTickByCode.TryGetValue(profile.Code, out lastProfileCast))
                    {
                        if ((uint)(now - lastProfileCast) < (uint)Math.Max(0, profile.RecheckMs))
                            continue;
                    }
                }

                var ctx = new ConditionalCastContext
                {
                    Hud = Hud,
                    Me = Hud.Game.Me,
                    Slot = slot,
                    Skill = slot.Skill,
                    Profile = profile,
                    Now = now
                };

                S7oMacroQueries.ClearRequests();
                bool shouldCast = false;

                try
                {
                    shouldCast = profile.ShouldCast != null && profile.ShouldCast(ctx);
                }
                catch (Exception ex)
                {
                    LogDebug("Conditional profile failed: " + profile.Code + ", error=" + ex.Message);
                    continue;
                }

                if (!shouldCast)
                {
                    if(IsDiagnosticFocusSkill(slot.Skill))
                        LogDebugCastSkipThrottled("policy",slot.Skill,profile.Code+": policy-no-cast",now);
                    S7oMacroQueries.ClearRequests();continue;
                }

                int delay = GetRandomizedDelay(
                    "conditional_cast_" + profile.Code,
                    profile.CastDelayMinMs > 0 ? profile.CastDelayMinMs : ConditionalProfileDelayMinMs,
                    profile.CastDelayMaxMs > 0 ? profile.CastDelayMaxMs : ConditionalProfileDelayMaxMs,
                    1000);

                bool commandSkeletonsCursorMoved = false;
                try
                {
                    if (IsCommandSkeletonsProfile(profile) && CommandSkeletonsMoveCursorToTarget)
                        commandSkeletonsCursorMoved = TryMoveCursorToCommandSkeletonsTarget();

                    string reason;
                    if (!CanCastSkill(slot.Skill, slot.ActionKey, now, delay, out reason, -1, profile.AllowChannelingSkill))
                    {
                        LogDebugCastSkipThrottled("conditional", slot.Skill, profile.Code + ": " + reason, now);
                        continue;
                    }

                    if (!HasEnoughPrimaryResource(slot.Skill, profile.ReservePrimaryResource, profile.BasePrimaryResourceRequirement))
                    {
                        LogDebugCastSkipThrottled("conditional", slot.Skill, profile.Code + ": reserved primary resource", now);
                        continue;
                    }

                    if (!HasEnoughSecondaryResource(profile.ReserveSecondaryResource, profile.BaseSecondaryResourceRequirement))
                    {
                        LogDebugCastSkipThrottled("conditional", slot.Skill, profile.Code + ": reserved secondary resource", now);
                        continue;
                    }

                    if (!ApplyImportedRequests(slot.Skill))
                    {
                        LogDebugCastSkipThrottled("conditional",slot.Skill,profile.Code+": input-owner-or-prerequisite-or-aim",now);
                        continue;
                    }
                    if (ExecuteImportedAction(slot.ActionKey, profile.UseStandstill && UseForceStandstillForCasts))
                    {
                        MarkSkillCast(slot.Skill, now, slot.ActionKey);
                        _lastConditionalProfileCastTickByCode[profile.Code] = now;
                        ImportedCastAccepted(slot.Skill, profile.Code);
                        if (IsCommandSkeletonsProfile(profile))
                            _lastCommandSkeletonsRequestTick = now;

                        LogDebug("Conditional profile input sent: " + profile.Code + ", skill=" + SafeSkillCode(slot.Skill));
                        return true;
                    }
                    LogDebugCastSkipThrottled("conditional",slot.Skill,profile.Code+": input-not-sent",now);
                }
                finally
                {
                    if (commandSkeletonsCursorMoved)
                        RestoreCommandSkeletonsCursor();
                    ClearCommandSkeletonsAimTarget();
                    RestoreImportedAim();
                }
            }

            return false;
        }

        private void AddConditionalProfile(
            ISnoPower snoPower,
            uint fallbackSno,
            string code,
            string displayName,
            string className,
            string groupName,
            string description,
            bool defaultEnabled,
            Func<ConditionalCastContext, bool> shouldCast,
            int castDelayMinMs = 250,
            int castDelayMaxMs = 600,
            int recheckMs = 50,
            int reservePrimaryResource = 0,
            int reserveSecondaryResource = 0,
            float basePrimaryRequirement = 0,
            float baseSecondaryRequirement = 0,
            bool useStandstill = true,
            bool allowInTown = false,
            bool allowChannelingSkill = false)
        {
            uint sno = GetSno(snoPower, fallbackSno);
            if (sno == 0 || string.IsNullOrEmpty(code))
                return;

            var profile = new ConditionalCastProfile
            {
                Sno = sno,
                Code = code,
                DisplayName = displayName,
                ClassName = className,
                GroupName = groupName,
                Description = description,
                Enabled = defaultEnabled,
                DefaultEnabled = defaultEnabled,
                ShouldCast = shouldCast,

                CastDelayMinMs = castDelayMinMs,
                CastDelayMaxMs = castDelayMaxMs,
                RecheckMs = recheckMs,

                ReservePrimaryResource = reservePrimaryResource,
                ReserveSecondaryResource = reserveSecondaryResource,
                BasePrimaryResourceRequirement = basePrimaryRequirement,
                BaseSecondaryResourceRequirement = baseSecondaryRequirement,

                UseStandstill = useStandstill,
                AllowInTown = allowInTown,
                AllowChannelingSkill = allowChannelingSkill
            };

            _conditionalProfiles.Add(profile);
            _conditionalProfileSnos.Add(sno);
        }

        // ── Public API for s7o_HUD_MENU integration ───────────────────────────────

        /// <summary>Get whether a conditional cast profile is enabled by code.</summary>
        public bool GetConditionalProfileEnabled(string code)
        {
            bool found = false;
            bool anyEnabled = false;

            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var p = _conditionalProfiles[i];
                if (p == null || !StringEquals(p.Code, code))
                    continue;

                found = true;
                if (p.Enabled)
                    anyEnabled = true;
            }

            return found && anyEnabled;
        }

        /// <summary>Toggle conditional cast profile(s) on/off by code. Returns new state, or false if not found.</summary>
        public bool ToggleConditionalProfile(string code)
        {
            bool found = false;
            bool newState = true;

            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var p = _conditionalProfiles[i];
                if (p == null || !StringEquals(p.Code, code))
                    continue;

                if (!found)
                {
                    newState = !p.Enabled;
                    found = true;
                }

                p.Enabled = newState;
            }

            if (found && PersistUserSettings)
                try { SaveUserSettings(); } catch { }

            return found && newState;
        }

        /// <summary>Set conditional cast profile(s) enabled state by code.</summary>
        public void SetConditionalProfileEnabled(string code, bool enabled)
        {
            bool found = false;

            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var p = _conditionalProfiles[i];
                if (p == null || !StringEquals(p.Code, code))
                    continue;

                p.Enabled = enabled;
                found = true;
            }

            if (found && PersistUserSettings)
                try { SaveUserSettings(); } catch { }
        }

        /// <summary>Get whether a buff upkeep profile is enabled by code.</summary>
        public bool GetBuffProfileEnabled(string code)
        {
            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var p = _buffProfiles[i];
                if (p != null && StringEquals(p.Code, code))
                    return p.Enabled;
            }
            return false;
        }

        /// <summary>Toggle a buff upkeep profile on/off by code. Returns new state, or false if not found.</summary>
        public bool ToggleBuffProfile(string code)
        {
            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var p = _buffProfiles[i];
                if (p != null && StringEquals(p.Code, code))
                {
                    p.Enabled = !p.Enabled;
                    if (PersistUserSettings) try { SaveUserSettings(); } catch { }
                    return p.Enabled;
                }
            }
            return false;
        }

        /// <summary>Set a buff upkeep profile enabled state by code.</summary>
        public void SetBuffProfileEnabled(string code, bool enabled)
        {
            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var p = _buffProfiles[i];
                if (p != null && StringEquals(p.Code, code))
                {
                    p.Enabled = enabled;
                    if (PersistUserSettings) try { SaveUserSettings(); } catch { }
                    return;
                }
            }
        }

        // Bits 0-6: left, right, skills 1-4, potion.
        /// <summary>Returns the seven manual autocast slot states as a compact bit mask.</summary>
        public int GetManualSlotAutoCastMask()
        {
            int mask = 0;
            for (int i = 0; i < _enabledSlots.Length; i++)
            {
                if (_enabledSlots[i])
                    mask |= 1 << i;
            }
            return mask;
        }

        /// <summary>Applies all seven manual autocast slot states and persists once.</summary>
        public void SetManualSlotAutoCastMask(int mask)
        {
            int validMask = mask & ((1 << _enabledSlots.Length) - 1);
            bool changed = false;

            for (int i = 0; i < _enabledSlots.Length; i++)
            {
                bool enabled = (validMask & (1 << i)) != 0;
                if (_enabledSlots[i] != enabled)
                {
                    _enabledSlots[i] = enabled;
                    changed = true;
                }

                var slot = FindSlotState(i);
                if (slot != null)
                    slot.IsManualEnabled = enabled;
            }

            if (changed && PersistUserSettings)
            {
                try { SaveUserSettings(); }
                catch { }
            }
        }

        // ── End public API ──────────────────────────────────────────────────────

        private ConditionalCastProfile FindConditionalProfile(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var profile = _conditionalProfiles[i];
                if (profile != null && StringEquals(profile.Code, code))
                    return profile;
            }

            return null;
        }

        private void BuildDemonHunterConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Preparation, 129212, "DH_Preparation_DisciplineOrLife", "Preparation: Discipline/Life", "Demon Hunter", "Resource", "Casts Preparation when discipline is missing, or Battle Scars rune is equipped and life is low. Punishment rune is handled by the Hatred profile.", true,
                ctx =>
                {
                    if (ctx.Skill.Rune == 0) return false;
                    if (ctx.Skill.Rune == 3 && HealthPctBelow(40)) return true;
                    return SecondaryResourceMissingAtLeast(30);
                }, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Preparation, 129212, "DH_Preparation_Punishment_Hatred", "Preparation: Punishment Hatred", "Demon Hunter", "Resource", "Casts Punishment rune when hatred is missing by about 75.", true, ctx => ctx.Skill.Rune == 0 && PrimaryResourceMissingAtLeast(75), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Companion, 365311, "DH_Companion_Bat_Hatred", "Companion: Bat Hatred", "Demon Hunter", "Resource", "Casts Bat Companion when hatred is very low.", true, ctx => ctx.Skill.Rune == 3 && PrimaryResourcePctBelow(15), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Companion, 365311, "DH_Companion_EliteOrBoss", "Companion: Elite/Boss", "Demon Hunter", "Combat", "Casts non-Bat Companion active when an elite, boss, or goblin is nearby.", true, ctx => ctx.Skill.Rune != 3 && IsEliteOrBossNearby(40, false), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Vengeance, 302846, "DH_Vengeance_KeepCombatBuff", "Vengeance: Combat Buff", "Demon Hunter", "Buff", "Casts Vengeance in combat when missing or almost expired. Also supports Dark Heart/Seethe style resource/danger use.", true,
                ctx =>
                {
                    if (!IsEliteOrBossNearby(100, true) && CountAliveMonstersWithin(100) < 1) return false;
                    if ((ctx.Skill.Rune == 1 || ctx.Skill.Rune == 3) && !HealthPctBelow(60)) return false;
                    return SkillBuffRemainingBelow(ctx.Skill, 50, 100);
                }, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Vengeance, 302846, "DH_Vengeance_HatredBurst", "Vengeance: Hatred Burst", "Demon Hunter", "Resource", "For hatred-return rune, casts Vengeance when hatred is below 30.", true, ctx => ctx.Skill.Rune == 4 && PrimaryResourceBelow(30) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_ShadowPower, 130830, "DH_ShadowPower_Defensive", "Shadow Power: Defensive", "Demon Hunter", "Defense", "Casts Shadow Power when missing and health/danger conditions are met.", true,
                ctx =>
                {
                    if (!SkillBuffMissing(ctx.Skill)) return false;
                    if (HasEffectiveSetBonus(ShadowsMantleSetSno, 4)) return true;
                    if (HealthPctBelow(65)) return true;
                    if (Hud.Game.IsEliteOnScreen && HealthPctBelow(75)) return true;
                    return false;
                }, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_ShadowPower, 130830, "DH_ShadowPower_ElusiveRing", "Shadow Power: Elusive Ring", "Demon Hunter", "Defense", "Keeps Elusive Ring active with Shadow Power.", true,
                ShouldCastShadowPowerForElusiveRing, 500, 750, 100);
        }

        private void BuildBarbarianConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_BattleRage, 79076, "Barbarian_BattleRage_KeepUp", "Battle Rage: Keep Up", "Barbarian", "Buff", "Keeps Battle Rage active.", true, ctx => SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_WarCry, 375483, "Barbarian_WarCry_KeepUp", "War Cry: Keep Up", "Barbarian", "Buff", "Keeps War Cry active.", true, ctx => SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_Sprint, 78551, "Barbarian_Sprint_KeepUpMoving", "Sprint: Keep Up", "Barbarian", "Movement", "Keeps Sprint up while monsters are nearby.", false, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffRemainingBelow(ctx.Skill, 500, 1000), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker, 79607, "Barbarian_WOTB_EliteOrDensity", "Wrath of the Berserker: Elite/Density", "Barbarian", "Cooldown", "Casts WOTB when elite/boss nearby or high monster density.", true, ctx => (IsEliteOrBossNearby(80, false) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_CallOfTheAncients, 80049, "Barbarian_COTA_Combat", "Call of the Ancients: Combat", "Barbarian", "Pet", "Casts Call of the Ancients in combat when missing.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(80) >= 1) && SkillBuffMissing(ctx.Skill), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_IgnorePain, 79528, "Barbarian_IgnorePain_Defensive", "Ignore Pain: Defensive", "Barbarian", "Defense", "Casts Ignore Pain when health is low or elites are nearby.", true, ctx => (HealthPctBelow(65) || IsEliteOrBossNearby(50, true)) && SkillBuffRemainingBelow(ctx.Skill, 100, 250), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private void BuildCrusaderConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_AkaratsChampion, 269032, "Crusader_AkaratsChampion_Combat", "Akarat's Champion: Combat", "Crusader", "Cooldown", "Casts Akarat's Champion when elites/bosses or density are nearby.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfValor, 342281, "Crusader_LawsOfValor_Elite", "Laws of Valor: Elite", "Crusader", "Law", "Activates Law of Valor when elites or bosses are nearby.", true, ctx => IsEliteOrBossNearby(60, true), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfJustice, 342280, "Crusader_LawsOfJustice_Defensive", "Laws of Justice: Defensive", "Crusader", "Law", "Activates Law of Justice when health is low or elites are nearby.", true, ctx => HealthPctBelow(70) || IsEliteOrBossNearby(50, true), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfHope, 342279, "Crusader_LawsOfHope_Defensive", "Laws of Hope: Defensive", "Crusader", "Law", "Activates Law of Hope when health is low.", true, ctx => HealthPctBelow(70), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_IronSkin, 291804, "Crusader_IronSkin_Defensive", "Iron Skin: Defensive", "Crusader", "Defense", "Casts Iron Skin when health is low or elites are nearby.", true, ctx => (HealthPctBelow(70) || IsEliteOrBossNearby(45, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_Provoke, 290545, "Crusader_Provoke_ResourceOrDensity", "Provoke: Resource/Density", "Crusader", "Resource", "Casts Provoke when wrath is missing and enemies are nearby.", true, ctx => PrimaryResourceMissingAtLeast(40) && CountAliveMonstersWithin(30) >= 3, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_Bombardment, 284876, "Crusader_Bombardment_EliteOrDensity", "Bombardment: Elite/Density", "Crusader", "Cooldown", "Casts Bombardment on elite/boss or dense packs.", false, ctx => IsEliteOrBossNearby(60, true) || CountAliveMonstersWithin(50) >= 10, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private void BuildMonkConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_Epiphany, 312307, "Monk_Epiphany_Combat", "Epiphany: Combat", "Monk", "Cooldown", "Casts Epiphany when elite/boss or density is nearby.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_SweepingWind, 96090, "Monk_SweepingWind_KeepUp", "Sweeping Wind: Keep Up", "Monk", "Buff", "Keeps Sweeping Wind active near enemies.", true, ctx => CountAliveMonstersWithin(60) > 0 && SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfConviction, 375088, "Monk_MantraConviction_Elite", "Mantra of Conviction: Elite", "Monk", "Mantra", "Refreshes the 3-second Conviction active only while attacking nearby enemies.", true, ctx => IsMonkAttackingEnemies() && ShouldRefreshMonkMantra(ctx), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfSalvation, 375049, "Monk_MantraSalvation_Defensive", "Mantra of Salvation: Defensive", "Monk", "Mantra", "Refreshes the 3-second Salvation active near danger without repeating casts.", true, ctx => (HealthPctBelow(70) || IsEliteOrBossNearby(45, true)) && ShouldRefreshMonkMantra(ctx), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfHealing, 373143, "Monk_MantraHealing_Defensive", "Mantra of Healing: Defensive", "Monk", "Mantra", "Keeps the shield active; repeats in combat and during Channeling Pylon.", true, ctx => ShouldCastHealingMantra(ctx), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MysticAlly, 362102, "Monk_MysticAlly_ResourceOrElite", "Mystic Ally: Resource/Elite", "Monk", "Resource", "Casts Mystic Ally when spirit is low or elite/boss is nearby.", true, ctx => PrimaryResourcePctBelow(35) || IsEliteOrBossNearby(50, true), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_Serenity, 96215, "Monk_Serenity_Defensive", "Serenity: Defensive", "Monk", "Defense", "Casts Serenity when health is low or elites are close.", true, ctx => (HealthPctBelow(55) || IsEliteOrBossNearby(30, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private bool IsMonkAttackingEnemies()
        {
            try
            {
                if (CountAliveMonstersWithin(35) == 0) return false;
                var state = Hud.Game.Me.AnimationState;
                return state == AcdAnimationState.Attacking || state == AcdAnimationState.Channeling;
            }
            catch { return false; }
        }

        private bool ShouldRefreshMonkMantra(ConditionalCastContext ctx)
        {
            int lastCast;
            if (ctx == null || ctx.Profile == null
                || !_lastConditionalProfileCastTickByCode.TryGetValue(ctx.Profile.Code, out lastCast))
                return true;
            return (uint)(ctx.Now - lastCast) >= 2700u;
        }

        internal bool ShouldMaintainSalvation(IPlayerSkill skill,int now)
        {
            if(skill==null||!S7oHealingMantraPolicy.Allowed(Hud))return false;
            int last;
            // Renew a three-second active effect; never turn this into combat spam.
            if(_lastCastTickByPower.TryGetValue(skill.SnoPower.Sno,out last)&&unchecked(now-last)<2700)return false;
            const uint buffSno=375050u;const int activeIcon=1;
            var me=Hud.Game.Me;var buff=me.Powers.GetBuff(buffSno);
            double left=buff!=null?S7oMacroData.Seconds(buff.TimeLeftSeconds,activeIcon):double.NaN;
            bool selfDue=!me.Powers.BuffIsActive(buffSno,activeIcon)||double.IsNaN(left)||left<=0.3;
            if(selfDue)return true;
            // Only loaded, living allies in the same world and aura vicinity qualify.
            foreach(var ally in Hud.Game.Players)
            {
                if(ally==null||ally.IsMe||!ally.IsInGame||ally.IsDead||!ally.HasValidActor||ally.Powers==null
                    ||ally.WorldId!=me.WorldId||ally.FloorCoordinate==null||me.FloorCoordinate==null
                    ||ally.FloorCoordinate.XYDistanceTo(me.FloorCoordinate)>60)continue;
                var other=ally.Powers.GetBuff(buffSno);
                double otherLeft=other!=null?S7oMacroData.Seconds(other.TimeLeftSeconds,activeIcon):double.NaN;
                if(!ally.Powers.BuffIsActive(buffSno,activeIcon)||double.IsNaN(otherLeft)||otherLeft<=0.3)return true;
            }
            return false;
        }

        private bool ShouldCastHealingMantra(ConditionalCastContext ctx)
        {
            if(ctx==null||ctx.Skill==null||!S7oHealingMantraPolicy.Allowed(Hud))return false;
            var me=Hud.Game.Me;var powers=me.Powers;
            bool pylon=powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno);
            bool combat=me.AnimationState==AcdAnimationState.Attacking||me.AnimationState==AcdAnimationState.Channeling;
            if(S7oHealingMantraPolicy.UseIdleSpiritReserve(me.AnimationState,pylon))
            {
                double cost=ctx.Skill.GetResourceRequirement();
                if(double.IsNaN(cost)||double.IsInfinity(cost)||cost<0)return false;
                double reserve=me.Stats.ResourceMaxPri*Math.Max(0,Math.Min(0.9,HealingMantraSpiritReserve));
                if(me.Stats.ResourceCurPri<cost+reserve)return false;
            }
            float shield=me.Defense.CurShield;
            bool weakened=_lastMonkHealingShieldAmount>0&&unchecked(ctx.Now-_lastMonkHealingShieldSampleTick)<=500
                &&shield<_lastMonkHealingShieldAmount-Math.Max(1,_lastMonkHealingShieldAmount*0.005f);
            _lastMonkHealingShieldAmount=shield;_lastMonkHealingShieldSampleTick=ctx.Now;
            var buff=powers.GetBuff(MonkHealingShieldBuffSno);
            bool active=powers.BuffIsActive(MonkHealingShieldBuffSno,MonkHealingShieldIcon)&&shield>0;
            double left=buff!=null&&buff.TimeLeftSeconds!=null&&buff.TimeLeftSeconds.Length>MonkHealingShieldIcon
                ?S7oMacroData.Seconds((buff)?.TimeLeftSeconds, MonkHealingShieldIcon):-1;
            int last,since=_lastConditionalProfileCastTickByCode.TryGetValue(ctx.Profile.Code,out last)?unchecked(ctx.Now-last):int.MaxValue;
            return S7oHealingMantraPolicy.Due(combat,pylon,active,weakened,left,since,HealingMantraCombatRefreshMs);
        }



        private void BuildNecromancerConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_BoneArmor, 466857, "Necro_BoneArmor_NearEnemies", "Bone Armor: Near Enemies", "Necromancer", "Buff", "Casts Bone Armor near enemies when missing/expiring.", true, ctx => CountAliveMonstersWithin(25) >= 1 && SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_Simulacrum, NecromancerSimulacrumPowerSno, "Necro_Simulacrum_EliteOrDensity", "Simulacrum: Haunted Visions Missing", "Necromancer", "Cooldown", "Casts Simulacrum only with Haunted Visions when one or both Simulacrums are missing.", true, ShouldCastSimulacrum, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead, NecromancerLandOfTheDeadPowerSno, "Necro_LandOfTheDead_EliteOrDensity", "Land of the Dead: Elite/Density", "Necromancer", "Cooldown", "Casts Land of the Dead on elites or dense packs.", false, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 12, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_Devour, NecromancerDevourPowerSno, DevourProfileCode, "Devour: Corpses/LotD", "Necromancer", "Resource", "Maintains Devour buffs/resources; fast-casts while Land of the Dead is active.", true, ShouldCastDevour, 100, 150, 25, 0, 0, 0, 0, false);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons, 453801, "Necro_CommandSkeletons_Elite", "Command Skeletons: Active Target", "Necromancer", "Targeting", "Maintains Command Skeletons/Jesseth on your selected target without moving the cursor.", true, ShouldCastCommandSkeletons, 350, 700, 250);
            AddNayrsBlackDeathProfiles();
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 462089, "Necro_SkeletalMage_EssenceHigh", "Skeletal Mage: Essence High", "Necromancer", "Resource", "Casts Skeletal Mage when essence is high.", false, ctx => !PrimaryResourcePctBelow(80), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private void BuildWitchDoctorConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_FetishArmy, 72785, "WD_FetishArmy_Combat", "Fetish Army: Combat", "Witch Doctor", "Pet", "Casts Fetish Army near elites or dense packs.", true, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_Gargantuan, 30624, "WD_Gargantuan_KeepUp", "Gargantuan: Keep Up", "Witch Doctor", "Pet", "Casts Gargantuan when missing near enemies.", true, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffMissing(ctx.Skill), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SummonZombieDog, 102573, "WD_ZombieDogs_KeepUp", "Zombie Dogs: Keep Up", "Witch Doctor", "Pet", "Summons Zombie Dogs when missing near enemies.", true, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffMissing(ctx.Skill), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest, 67616, "WD_SoulHarvest_NearEnemies", "Soul Harvest: Near Enemies", "Witch Doctor", "Buff", "Casts Soul Harvest when enemies are close and the buff needs upkeep.", true, ctx => CountAliveMonstersWithin(18) >= 1 && SkillBuffRemainingBelow(ctx.Skill, 500, 1500), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_Horrify, 67668, "WD_Horrify_Defensive", "Horrify: Defensive", "Witch Doctor", "Defense", "Casts Horrify when enemies are close or health is low.", true, ctx => HealthPctBelow(70) || CountAliveMonstersWithin(24) >= 1, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SpiritWalk, 106237, "WD_SpiritWalk_Defensive", "Spirit Walk: Defensive", "Witch Doctor", "Defense", "Casts Spirit Walk defensively. Movement use stays manual.", true, ctx => HealthPctBelow(55) || IsEliteOrBossNearby(30, true), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_BigBadVoodoo, 117402, "WD_BigBadVoodoo_EliteOrDensity", "Big Bad Voodoo: Elite/Density", "Witch Doctor", "Cooldown", "Casts Big Bad Voodoo on elites or dense packs.", false, ctx => IsEliteOrBossNearby(60, true) || CountAliveMonstersWithin(50) >= 10, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private void BuildWizardConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_DiamondSkin, 75599, "Wizard_DiamondSkin_Defensive", "Diamond Skin: Defensive", "Wizard", "Defense", "Casts Diamond Skin when health is low or elites are close.", true, ctx => (HealthPctBelow(70) || IsEliteOrBossNearby(35, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_Archon, 134872, "Wizard_Archon_EliteOrDensity", "Archon: Elite/Density", "Wizard", "Cooldown", "Casts Archon near elites or dense packs.", false, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 10, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_MirrorImage, 98027, "Wizard_MirrorImage_Defensive", "Mirror Image: Defensive", "Wizard", "Defense", "Casts Mirror Image when health is low or elites are nearby.", true, ctx => HealthPctBelow(65) || IsEliteOrBossNearby(40, true), ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_FrostNova, 30718, "Wizard_FrostNova_CloseEnemies", "Frost Nova: Close Enemies", "Wizard", "Defense", "Casts Frost Nova when enemies are close.", true, ctx => CountAliveMonstersWithin(18) >= 1, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_ExplosiveBlast, 87525, "Wizard_ExplosiveBlast_CloseEnemies", "Explosive Blast: Close Enemies", "Wizard", "Offense", "Casts Explosive Blast when enemies are close.", false, ctx => CountAliveMonstersWithin(15) >= 1, ConditionalProfileDelayMinMs, ConditionalProfileDelayMaxMs, ConditionalProfileRecheckMs);
        }

        private bool IsOwnBuffActive(uint sno)
        {
            try { return Hud != null && Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.Powers != null && Hud.Game.Me.Powers.BuffIsActive(sno); }
            catch { return false; }
        }

        private bool IsOwnBuffActive(uint sno, int iconIndex)
        {
            try { return Hud != null && Hud.Game != null && Hud.Game.Me != null && Hud.Game.Me.Powers != null && Hud.Game.Me.Powers.BuffIsActive(sno, iconIndex); }
            catch { return false; }
        }

        private double GetOwnBuffRemainingSeconds(uint sno, int iconIndex = -1)
        {
            try
            {
                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                if (powers == null) return 0;

                var buff = powers.GetBuff(sno);
                if (buff == null || !buff.Active || buff.TimeLeftSeconds == null || buff.TimeLeftSeconds.Length == 0) return 0;

                if (iconIndex >= 0 && iconIndex < buff.TimeLeftSeconds.Length) return S7oMacroData.Seconds((buff)?.TimeLeftSeconds, iconIndex);

                double max = 0;
                for (int i = 0; i < buff.TimeLeftSeconds.Length; i++)
                    if (S7oMacroData.Seconds((buff)?.TimeLeftSeconds, i) > max) max = S7oMacroData.Seconds((buff)?.TimeLeftSeconds, i);
                return max;
            }
            catch { return 0; }
        }

        private bool HasEnoughSecondaryResource(int spareResource, float baseRequirement)
        {
            try
            {
                var stats = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Stats : null;
                if (stats == null) return false;
                float requirement = Math.Max(0, baseRequirement) + Math.Max(0, spareResource);
                if (requirement <= 0) return true;
                return stats.ResourceCurSec >= requirement;
            }
            catch { return true; }
        }

        private bool PrimaryResourceMissingAtLeast(float amount)
        {
            try { var stats = Hud.Game.Me.Stats; return stats.ResourceCurPri <= stats.ResourceMaxPri - amount; }
            catch { return false; }
        }

        private bool SecondaryResourceMissingAtLeast(float amount)
        {
            try { var stats = Hud.Game.Me.Stats; return stats.ResourceCurSec <= stats.ResourceMaxSec - amount; }
            catch { return false; }
        }

        private bool PrimaryResourceBelow(float amount)
        {
            try { return Hud.Game.Me.Stats.ResourceCurPri < amount; }
            catch { return false; }
        }

        private bool SecondaryResourceBelow(float amount)
        {
            try { return Hud.Game.Me.Stats.ResourceCurSec < amount; }
            catch { return false; }
        }

        private bool PrimaryResourcePctBelow(float pct)
        {
            try { return Hud.Game.Me.Stats.ResourcePctPri < pct; }
            catch { return false; }
        }

        private bool SecondaryResourcePctBelow(float pct)
        {
            try { return Hud.Game.Me.Stats.ResourcePctSec < pct; }
            catch { return false; }
        }

        private int CountAliveMonstersWithin(double range)
        {
            try
            {
                var monsters = Hud != null && Hud.Game != null ? Hud.Game.AliveMonsters : null;
                if (monsters == null) return 0;

                int count = 0;
                foreach (var monster in monsters)
                {
                    if (!IsValidMonsterForCondition(monster)) continue;
                    if (monster.NormalizedXyDistanceToMe <= range) count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private bool IsEliteOrBossNearby(double range, bool includeMinions = false)
        {
            try
            {
                var monsters = Hud != null && Hud.Game != null ? Hud.Game.AliveMonsters : null;
                if (monsters == null) return false;

                foreach (var monster in monsters)
                {
                    if (!IsValidMonsterForCondition(monster)) continue;
                    if (monster.NormalizedXyDistanceToMe > range) continue;
                    if (monster.Rarity == ActorRarity.Boss) return true;
                    if (monster.IsElite)
                    {
                        if (includeMinions) return true;
                        if (monster.Rarity != ActorRarity.RareMinion) return true;
                    }
                    if (IsGoblinMonster(monster)) return true;
                }
            }
            catch { }
            return false;
        }

        private bool IsValidMonsterForCondition(IMonster monster)
        {
            if (monster == null) return false;
            if (!monster.IsAlive) return false;
            if (monster.Hidden || monster.Invisible || monster.Stealthed) return false;
            if (monster.Illusion) return false;
            if (monster.Untargetable) return false;
            return true;
        }

        private bool IsGoblinMonster(IMonster monster)
        {
            try
            {
                if (monster == null || monster.SnoMonster == null) return false;
                string code = monster.SnoMonster.Code ?? string.Empty;
                string name = monster.SnoMonster.NameEnglish ?? string.Empty;
                return code.IndexOf("Goblin", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Goblin", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Treasure", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool ShouldCastSimulacrum(ConditionalCastContext ctx)
        {
            try
            {
                if (ctx == null || ctx.Skill == null || ctx.Skill.SnoPower == null)
                    return false;

                if (!IsHauntedVisionsActive())
                    return false;

                int desiredCount = UsesTwoSimulacrums(ctx.Skill) ? 2 : 1;
                int activeCount = CountOwnSimulacrumActors();

                if (activeCount < desiredCount)
                    return true;

                return !IsOwnBuffActive(ctx.Skill.SnoPower.Sno) && !IsOwnBuffActive(NecromancerSimulacrumPowerSno);
            }
            catch { return false; }
        }

        private bool IsHauntedVisionsActive()
        {
            try
            {
                if (IsOwnBuffActive(HauntedVisionsPowerSno))
                    return true;

                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                var used = powers != null ? powers.UsedLegendaryPowers : null;
                return used != null && used.HauntedVisions != null && used.HauntedVisions.Active;
            }
            catch { return false; }
        }

        private bool UsesTwoSimulacrums(IPlayerSkill skill)
        {
            try
            {
                return (skill != null && skill.Rune == 3)
                    || IsOwnBuffActive(MasqueradeOfTheBurningCarnival2PBuffSno);
            }
            catch { return false; }
        }

        private int CountOwnSimulacrumActors()
        {
            try
            {
                var actors = Hud != null && Hud.Game != null ? Hud.Game.Actors : null;
                var me = Hud != null && Hud.Game != null ? Hud.Game.Me : null;
                if (actors == null || me == null)
                    return 0;

                uint ownerId = me.SummonerId;
                int count = 0;

                foreach (var actor in actors)
                {
                    if (actor == null || actor.SnoActor == null)
                        continue;

                    if (!IsSimulacrumActorSno(actor.SnoActor.Sno))
                        continue;

                    if (ownerId != 0 && actor.SummonerAcdDynamicId != ownerId)
                        continue;

                    if (actor.IsDisabled || actor.Hitpoints <= 0f)
                        continue;

                    count++;
                }

                return count;
            }
            catch { return 0; }
        }

        private static bool IsSimulacrumActorSno(ActorSnoEnum sno)
        {
            return sno == ActorSnoEnum._p6_necro_simulacrum_male
                || sno == ActorSnoEnum._p6_necro_simulacrum_female
                || sno == ActorSnoEnum._p6_necro_simulacrum_norune
                || sno == ActorSnoEnum._p6_necro_simulacrum_a
                || sno == ActorSnoEnum._p6_necro_simulacrum_a_set;
        }

        private bool HealthPctBelow(float pct)
        {
            try { return Hud.Game.Me.Defense.HealthPct < pct; }
            catch { return false; }
        }

        private bool HasSetPieces(uint setSno, int count)
        {
            try { return Hud.Game.Me.GetSetItemCount(setSno) >= count; }
            catch { return false; }
        }

        private bool HasEffectiveSetBonus(uint setSno, int requiredPieces)
        {
            try
            {
                if (Hud == null || Hud.Game == null || Hud.Game.Me == null) return false;

                int equippedPieces = Hud.Game.Me.GetSetItemCount(setSno);
                if (equippedPieces >= requiredPieces) return true;
                if (equippedPieces != requiredPieces - 1) return false;

                return HasRingOfRoyalGrandeur();
            }
            catch { return false; }
        }

        private bool HasRingOfRoyalGrandeur()
        {
            try
            {
                var me = Hud.Game.Me;
                if (me == null || Hud.Sno == null || Hud.Sno.SnoItems == null) return false;

                var legacy = Hud.Sno.SnoItems.Unique_Ring_107_x1;
                var current = Hud.Sno.SnoItems.P3_Unique_Ring_107;

                if ((legacy != null && me.IsCubed(legacy)) ||
                    (current != null && me.IsCubed(current)))
                    return true;

                if (Hud.Game.Items == null) return false;
                foreach (var item in Hud.Game.Items)
                {
                    if (item == null || item.SnoItem == null) continue;
                    if (item.Location != ItemLocation.LeftRing && item.Location != ItemLocation.RightRing) continue;

                    uint sno = item.SnoItem.Sno;
                    if ((legacy != null && sno == legacy.Sno) ||
                        (current != null && sno == current.Sno))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private bool HasUsedSkill(ISnoPower power)
        {
            try
            {
                if (power == null || Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null) return false;
                return Hud.Game.Me.Powers.GetUsedSkill(power) != null;
            }
            catch { return false; }
        }

        private bool SkillBuffMissing(IPlayerSkill skill)
        {
            try { return skill == null || skill.SnoPower == null || !IsOwnBuffActive(skill.SnoPower.Sno); }
            catch { return true; }
        }

        private bool ShouldCastShadowPowerForElusiveRing(ConditionalCastContext ctx)
        {
            try
            {
                if (ctx == null || ctx.Skill == null || ctx.Skill.Player == null || ctx.Skill.Player.Powers == null)
                    return false;

                if (!IsEliteOrBossNearby(40, true) && CountAliveMonstersWithin(40) < 1)
                    return false;

                var powers = ctx.Skill.Player.Powers;
                var equipped = powers.UsedLegendaryPowers == null ? null : powers.UsedLegendaryPowers.ElusiveRing;
                if (equipped == null || !equipped.Active)
                    return false;

                var buff = powers.GetBuff(Hud.Sno.SnoPowers.ElusiveRing.Sno);
                if (buff == null || !buff.Active)
                    return true;

                var timers = buff.TimeLeftSeconds;
                if (timers == null || timers.Length <= 1 || timers[1] <= 0)
                    return false;

                int thresholdMs = GetRandomizedDelay(
                    "conditional_shadow_power_elusive_refresh", 250, 500, BuffRefreshJitterChangeMs);
                return timers[1] <= thresholdMs / 1000.0d;
            }
            catch { return false; }
        }

        private bool SkillBuffRemainingBelow(IPlayerSkill skill, int minMs, int maxMs, int iconIndex = -1)
        {
            if (skill == null || skill.SnoPower == null) return true;
            double remaining = GetOwnBuffRemainingSeconds(skill.SnoPower.Sno, iconIndex);
            if (remaining <= 0) return true;
            int thresholdMs = GetRandomizedDelay("conditional_buff_refresh_" + skill.SnoPower.Sno.ToString(CultureInfo.InvariantCulture), minMs, maxMs, Math.Max(maxMs, BuffRefreshJitterChangeMs));
            return remaining <= thresholdMs / 1000.0d;
        }

        private void AddNayrsBlackDeathProfiles()
        {
            const string title = "Nayr's Black Death: Keep Stacks";
            const string description = "Maintains Nayr poison stacks after you start attacking with Siphon Blood.";

            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_BoneArmor, 466857, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_BoneSpear, 451490, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_BoneSpikes, 462147, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons, 453801, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_DeathNova, 462243, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 462089, NayrsBlackDeathProfileCode, title, "Necromancer", "Buff", description, false, ShouldCastNayrsBlackDeath, 100, 150, 25);
        }

        private bool ShouldCastNayrsBlackDeath(ConditionalCastContext ctx)
        {
            try
            {
                if (ctx == null || ctx.Skill == null || ctx.Skill.SnoPower == null)
                    return false;

                if (!IsNayrsBlackDeathEquippedOrCubed())
                    return false;

                if (!IsNayrsPoisonSkillRune(ctx.Skill))
                    return false;

                if (!IsNayrsSiphonCombatWindowActive(ctx.Now))
                    return false;

                if (!IsNayrsCombatContext(ctx.Skill))
                    return false;

                return GetNayrsRemainingSeconds(ctx.Skill) < 0.5d;
            }
            catch { return false; }
        }

        private void UpdateNayrsSiphonBloodCue(int now)
        {
            try
            {
                if (!GetConditionalProfileEnabled(NayrsBlackDeathProfileCode))
                    return;

                var slot = FindEquippedSkillBySno(GetSno(Hud.Sno.SnoPowers.Necromancer_SiphonBlood, SiphonBloodPowerSno));
                if (slot == null || slot.Skill == null)
                    return;

                if (IsSiphonBloodActive(slot.Skill) || IsSiphonBloodActionHeldForCombat(slot.ActionKey))
                    _lastNayrsSiphonBloodCueTick = now;
            }
            catch { }
        }

        private bool IsNayrsSiphonCombatWindowActive(int now)
        {
            int window = Math.Max(250, NayrsSiphonCombatWindowMs);
            return _lastNayrsSiphonBloodCueTick != 0 && (uint)(now - _lastNayrsSiphonBloodCueTick) <= (uint)window;
        }

        private bool IsSiphonBloodActive(IPlayerSkill skill)
        {
            try
            {
                if (skill != null && skill.BuffIsActive)
                    return true;

                return IsOwnBuffActive(SiphonBloodPowerSno);
            }
            catch { return false; }
        }

        private bool IsSiphonBloodActionHeldForCombat(ActionKey actionKey)
        {
            switch (actionKey)
            {
                case ActionKey.RightSkill:
                    return IsKeyPhysicallyDown(0x02);

                case ActionKey.Skill1:
                    return IsKeyPhysicallyDown(Skill1VirtualKey);

                case ActionKey.Skill2:
                    return IsKeyPhysicallyDown(Skill2VirtualKey);

                case ActionKey.Skill3:
                    return IsKeyPhysicallyDown(Skill3VirtualKey);

                case ActionKey.Skill4:
                    return IsKeyPhysicallyDown(Skill4VirtualKey);

                case ActionKey.LeftSkill:
                    return ForceStandstillVirtualKey != 0
                        && IsKeyPhysicallyDown(0x01)
                        && IsKeyPhysicallyDown(ForceStandstillVirtualKey);

                default:
                    return false;
            }
        }

        private bool IsNayrsBlackDeathEquippedOrCubed()
        {
            try
            {
                if (IsOwnBuffActive(NayrsBlackDeathBuffSno))
                    return true;

                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                var used = powers != null ? powers.UsedLegendaryPowers : null;
                return used != null && used.NayrsBlackDeath != null && used.NayrsBlackDeath.Active;
            }
            catch { return false; }
        }

        private bool IsNayrsPoisonSkillRune(IPlayerSkill skill)
        {
            if (skill == null || skill.SnoPower == null)
                return false;

            uint sno = skill.SnoPower.Sno;
            int rune = skill.Rune;

            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_BoneArmor, 466857)) return rune == 2;
            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_BoneSpear, 451490)) return rune == 2;
            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_BoneSpikes, 462147)) return rune == 2;
            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons, 453801)) return rune == 3;
            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_DeathNova, 462243)) return rune == 3 || rune == 4;
            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 462089)) return rune == 3;

            return false;
        }

        private bool IsNayrsCombatContext(IPlayerSkill skill)
        {
            if (skill == null || skill.SnoPower == null)
                return false;

            uint sno = skill.SnoPower.Sno;

            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_BoneArmor, 466857))
                return CountAliveMonstersWithin(25) >= 1;

            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_DeathNova, 462243))
                return CountAliveMonstersWithin(25) >= 1;

            if (sno == GetSno(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons, 453801))
                return GetSelectedCommandSkeletonsTarget() != null;

            return CountAliveMonstersWithin(100) >= 1;
        }

        private double GetNayrsRemainingSeconds(IPlayerSkill skill)
        {
            try
            {
                if (skill == null)
                    return 0;

                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                if (powers == null)
                    return 0;

                var buff = powers.GetBuff(NayrsBlackDeathBuffSno);
                if (buff == null || !buff.Active || buff.TimeLeftSeconds == null || buff.TimeLeftSeconds.Length == 0)
                    return 0;

                int iconIndex = skill.Key.GetHashCode() + 1;
                if (iconIndex >= 0 && iconIndex < buff.TimeLeftSeconds.Length)
                    return S7oMacroData.Seconds((buff)?.TimeLeftSeconds, iconIndex);

                return 0;
            }
            catch { return 0; }
        }

        private bool ShouldCastDevour(ConditionalCastContext ctx)
        {
            try
            {
                if (ctx == null || ctx.Skill == null || Hud == null || Hud.Game == null || Hud.Game.Me == null)
                    return false;

                // LightningMod skips Devouring Aura; it is an aura-style rune and should not be key-spammed.
                if (ctx.Skill.Rune == 3)
                    return false;

                bool landOfTheDeadActive = IsOwnBuffActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno);
                if (landOfTheDeadActive)
                    return true;

                int nearbyMonsters = CountAliveMonstersWithin(60);
                bool combatContext = nearbyMonsters > 0 || Hud.Game.SpecialArea == SpecialArea.PvP;
                if (!combatContext)
                    return false;

                int corpseCount = CountNecromancerCorpsesWithin(60d);
                if (corpseCount <= 0)
                    return false;

                // Cannibalize rune: LM casts for recovery when health is low, or scales the health threshold by corpse count.
                if (ctx.Skill.Rune == 0)
                {
                    float health = CurrentHealthPct();
                    if (health < 70f || health < 95f - (corpseCount * 3f))
                        return true;
                }

                if (EssencePctBelow(100f))
                    return true;

                // Trag'Oul 4-piece preservation from LM: keep the generic set buff stacked/refreshed.
                if (HasSetPieces(740282u, 4))
                {
                    uint tragOulBuff = Hud.Sno.SnoPowers.Generic_P6ItemPassiveUniqueRing011.Sno;
                    if (GetOwnBuffMaxIconCount(tragOulBuff) < 25 || GetOwnBuffRemainingSeconds(tragOulBuff, 1) < 2d)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private int CountNecromancerCorpsesWithin(double range)
        {
            try
            {
                var actors = Hud != null && Hud.Game != null ? Hud.Game.Actors : null;
                if (actors == null) return 0;

                int count = 0;
                foreach (var actor in actors)
                {
                    if (actor == null || actor.SnoActor == null || actor.SnoActor.Sno != ActorSnoEnum._p6_necro_corpse_flesh)
                        continue;
                    if (actor.CentralXyDistanceToMe <= range)
                        count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private float CurrentHealthPct()
        {
            try { return Hud.Game.Me.Defense.HealthPct; }
            catch { return 0f; }
        }

        private bool EssencePctBelow(float pct)
        {
            try { return Hud.Game.Me.Stats.ResourcePctEssence < pct; }
            catch { return false; }
        }

        private int GetOwnBuffMaxIconCount(uint sno)
        {
            try
            {
                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                if (powers == null) return 0;

                var buff = powers.GetBuff(sno);
                if (buff == null || !buff.Active || buff.IconCounts == null || buff.IconCounts.Length == 0)
                    return 0;

                int max = 0;
                for (int i = 0; i < buff.IconCounts.Length; i++)
                    if (S7oMacroData.Count((buff)?.IconCounts, i) > max) max = S7oMacroData.Count((buff)?.IconCounts, i);
                return max;
            }
            catch { return 0; }
        }

        private bool ShouldCastCommandSkeletons(ConditionalCastContext ctx)
        {
            ClearCommandSkeletonsAimTarget();

            try
            {
                if (ctx == null || ctx.Skill == null || ctx.Skill.SnoPower == null)
                    return false;

                // LightningMod explicitly skips poison skeletons.
                if (ctx.Skill.Rune == 3)
                    return false;

                var selected = GetSelectedCommandSkeletonsTarget();

                // LM rule 1: selected boss gets a command if Command Skeletons is not already assigned.
                if (selected != null && IsBossLike(selected) && !IsOwnBuffActive(ctx.Skill.SnoPower.Sno))
                {
                    if (SetCommandSkeletonsAimTarget(selected) && CommandSkeletonsThrottleReady(350))
                        return true;
                }

                // LM Bloodsong Mail rule: one Command Skeletons cast during Land of the Dead when Bloodsong is active.
                // FreeHUD safety: do not autosnap/move the cursor; only use the player's selected/hovered target.
                if (IsBloodsongMailActive())
                {
                    if (IsOwnBuffActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno))
                    {
                        if (!_commandSkeletonsBloodsongLotdCast && selected != null)
                        {
                            if (SetCommandSkeletonsAimTarget(selected) && CommandSkeletonsThrottleReady(350))
                            {
                                _commandSkeletonsBloodsongLotdCast = true;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        _commandSkeletonsBloodsongLotdCast = false;
                    }
                }
                else
                {
                    _commandSkeletonsBloodsongLotdCast = false;
                }

                // LM rule 2: once Command Skeletons is assigned/highlighted, do not keep spending essence.
                if (IsOwnBuffActive(ctx.Skill.SnoPower.Sno))
                    return false;

                // LM rule 2: Jesseth Arms active + any monster = command it.  This is what keeps the
                // Jesseth damage bonus alive on trash; do not require an elite here.
                if (IsOwnBuffActive(JessethArmsDamageBuffSno))
                {
                    if (selected != null && SetCommandSkeletonsAimTarget(selected) && CommandSkeletonsThrottleReady(350))
                        return true;
                }

                // LM rule 3: Zodiac + low-cost Command Skeletons can keep commanding the player's selected/hovered monster.
                if (GetSkillResourceRequirementSafe(ctx.Skill) <= 35f && IsOwnBuffActive(402459u))
                {
                    if (selected != null && SetCommandSkeletonsAimTarget(selected) && CommandSkeletonsThrottleReady(350))
                        return true;
                }

                // LM rule 4: slower elite/boss fallback, selected target only. Do not autosnap to nearby targets.
                if (selected != null && IsEliteOrBossTarget(selected) && SetCommandSkeletonsAimTarget(selected) && CommandSkeletonsThrottleReady(2000))
                    return true;

                return false;
            }
            catch { return false; }
        }

        private float GetSkillResourceRequirementSafe(IPlayerSkill skill)
        {
            try { return skill != null ? skill.GetResourceRequirement() : 0f; }
            catch { return 0f; }
        }

        private bool CommandSkeletonsThrottleReady(int throttleMs)
        {
            int tick = Environment.TickCount;
            int delay = Math.Max(0, throttleMs);
            if (_lastCommandSkeletonsRequestTick != 0 && (uint)(tick - _lastCommandSkeletonsRequestTick) < (uint)delay)
                return false;

            _lastCommandSkeletonsRequestTick = tick;
            return true;
        }

        private bool IsBloodsongMailActive()
        {
            try
            {
                var powers = Hud != null && Hud.Game != null && Hud.Game.Me != null ? Hud.Game.Me.Powers : null;
                var used = powers != null ? powers.UsedLegendaryPowers : null;
                return used != null && used.BloodsongMail != null && used.BloodsongMail.Active;
            }
            catch { return false; }
        }

        private bool IsEliteOrBossTarget(IMonster monster)
        {
            try
            {
                if (!IsValidCommandSkeletonsTarget(monster))
                    return false;

                return IsBossLike(monster)
                    || monster.Rarity == ActorRarity.Champion
                    || monster.Rarity == ActorRarity.Rare
                    || monster.IsElite;
            }
            catch { return false; }
        }

        private bool IsBossLike(IMonster monster)
        {
            try
            {
                if (monster == null)
                    return false;

                if (monster.Rarity == ActorRarity.Boss)
                    return true;

                var sno = monster.SnoMonster;
                return sno != null && sno.Priority == MonsterPriority.boss;
            }
            catch { return false; }
        }

        private bool IsCommandSkeletonsProfile(ConditionalCastProfile profile)
        {
            return profile != null && string.Equals(profile.Code, CommandSkeletonsProfileCode, StringComparison.OrdinalIgnoreCase);
        }

        private void ClearCommandSkeletonsAimTarget()
        {
            _commandSkeletonsHasAimTarget = false;
            _commandSkeletonsAimX = 0;
            _commandSkeletonsAimY = 0;
            _commandSkeletonsCursorRestoreKnown = false;
        }

        private bool SetCommandSkeletonsAimTarget(IMonster monster)
        {
            try
            {
                if (!IsValidCommandSkeletonsTarget(monster) || !monster.IsOnScreen)
                    return false;

                var screen = monster.ScreenCoordinate;
                if (screen == null && monster.FloorCoordinate != null)
                    screen = monster.FloorCoordinate.ToScreenCoordinate();
                if (screen == null)
                    return false;

                float x = screen.X;
                float y = screen.Y;

                try
                {
                    if (monster.FloorCoordinate != null)
                    {
                        var floor = monster.FloorCoordinate.ToScreenCoordinate();
                        if (floor != null && Math.Abs(y - floor.Y) < 4f)
                        {
                            float radius = monster.RadiusBottom > 0f ? monster.RadiusBottom : 3.15f;
                            float scale = Math.Max(0.65f, Math.Min(radius / 3.15f, 2.8f));
                            y -= 22f * scale;
                        }
                    }
                }
                catch { }

                if (!IsCommandSkeletonsAimSafe(x, y))
                    return false;

                _commandSkeletonsAimX = (int)Math.Round(x);
                _commandSkeletonsAimY = (int)Math.Round(y);
                _commandSkeletonsHasAimTarget = true;
                return true;
            }
            catch { return false; }
        }

        private bool TryMoveCursorToCommandSkeletonsTarget()
        {
            try
            {
                if (!CommandSkeletonsMoveCursorToTarget || !_commandSkeletonsHasAimTarget)
                    return false;

                if (!IsCommandSkeletonsAimSafe(_commandSkeletonsAimX, _commandSkeletonsAimY))
                    return false;

                CursorPoint point;
                if (!GetCursorPos(out point))
                    return false;

                _commandSkeletonsRestoreX = point.X;
                _commandSkeletonsRestoreY = point.Y;
                _commandSkeletonsCursorRestoreKnown = true;

                return MoveCommandSkeletonsCursorToClientPoint(_commandSkeletonsAimX, _commandSkeletonsAimY);
            }
            catch { return false; }
        }

        private bool MoveCommandSkeletonsCursorToClientPoint(int x, int y)
        {
            try
            {
                if (Hud == null || Hud.Window == null)
                    return false;

                long screenX = (long)x + Hud.Window.Offset.X;
                long screenY = (long)y + Hud.Window.Offset.Y;
                if (screenX < int.MinValue || screenX > int.MaxValue ||
                    screenY < int.MinValue || screenY > int.MaxValue)
                    return false;

                return SetCursorPos((int)screenX, (int)screenY);
            }
            catch { return false; }
        }

        private void RestoreCommandSkeletonsCursor()
        {
            try
            {
                if (_commandSkeletonsCursorRestoreKnown)
                    SetCursorPos(_commandSkeletonsRestoreX, _commandSkeletonsRestoreY);
            }
            catch { }
            finally
            {
                _commandSkeletonsCursorRestoreKnown = false;
            }
        }

        private bool IsCommandSkeletonsAimSafe(float x, float y)
        {
            try
            {
                if (Hud == null || Hud.Window == null)
                    return false;

                float w = Hud.Window.Size.Width;
                float h = Hud.Window.Size.Height;
                if (w <= 0f || h <= 0f)
                    return false;

                if (x < 2f || y < 2f || x > w - 2f || y > h - 2f)
                    return false;

                try
                {
                    if (!Hud.Window.GroundRectangle.Contains((int)Math.Round(x), (int)Math.Round(y)))
                        return false;
                }
                catch { }

                if (y > h * 0.88f)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private IMonster GetSelectedCommandSkeletonsTarget()
        {
            try
            {
                var m = Hud.Game.SelectedMonster2;
                return IsValidCommandSkeletonsTarget(m) ? m : null;
            }
            catch { return null; }
        }

        private bool IsValidCommandSkeletonsTarget(IMonster monster)
        {
            try
            {
                return monster != null
                    && monster.IsAlive
                    && !monster.Illusion
                    && !monster.Hidden
                    && !monster.Invisible
                    && !monster.Stealthed
                    && !monster.Untargetable
                    && monster.Attackable;
            }
            catch { return false; }
        }

        #endregion

        #region Cast Rules

        private bool CanCastSkill(IPlayerSkill skill, ActionKey actionKey, int now, int minDelayMs, out string reason, int globalDelayOverrideMs = -1, bool allowChannelingSkill = false)
        {
            reason = null;

            if (skill == null || skill.SnoPower == null)
            {
                reason = "skill null";
                return false;
            }

            if (_autoCastPulse.Active)
            {
                reason = "input pulse in flight";
                return false;
            }

            if (skill.IsOnCooldown)
            {
                reason = "cooldown";
                return false;
            }

            bool isPotionCast = actionKey == ActionKey.Heal;

            if (!isPotionCast && !HasEnoughPrimaryResource(skill, 0, 0))
            {
                reason = "resource";
                return false;
            }

            if (actionKey != ActionKey.Heal &&
                IsCrusaderSteedChargeActive() &&
                !IsAutomatedSteedChargeNearExpiry())
            {
                reason = "steed charge active";
                return false;
            }

            if (!IsActionCursorSafe(actionKey))
            {
                reason = "cursor unsafe";
                return false;
            }

            uint sno = skill.SnoPower.Sno;
            int last;
            if (_lastCastTickByPower.TryGetValue(sno, out last))
            {
                if ((uint)(now - last) < (uint)Math.Max(0, minDelayMs))
                {
                    reason = "skill delay";
                    return false;
                }
            }

            int globalDelayMs = globalDelayOverrideMs >= 0 ? globalDelayOverrideMs : GlobalCastDelayMs;
            if (!isPotionCast && (uint)(now - _lastGlobalCastTick) < (uint)Math.Max(0, globalDelayMs))
            {
                reason = "global delay";
                return false;
            }

            if (!allowChannelingSkill && !IsChannelSafeInstantCast(skill, actionKey) && IsChannelingSkill(skill))
            {
                reason = "channeling unsupported";
                return false;
            }

            return true;
        }

        private bool IsCrusaderSteedChargeSkill(IPlayerSkill skill)
        {
            try
            {
                return IsLocalCrusader()
                    && skill != null
                    && skill.SnoPower != null
                    && skill.SnoPower.Sno == GetSno(Hud.Sno.SnoPowers.Crusader_SteedCharge, CrusaderSteedChargePowerSno);
            }
            catch
            {
                return false;
            }
        }

        private bool IsCrusaderSteedChargeActive()
        {
            try
            {
                return IsLocalCrusader()
                    && Hud != null
                    && Hud.Game != null
                    && Hud.Game.Me != null
                    && Hud.Game.Me.Powers != null
                    && Hud.Game.Me.Powers.BuffIsActive(GetSno(Hud.Sno.SnoPowers.Crusader_SteedCharge, CrusaderSteedChargePowerSno));
            }
            catch
            {
                return false;
            }
        }

        private bool IsAutomatedSteedChargeNearExpiry()
        {
            try
            {
                if (!IsLocalCrusader())
                    return false;

                uint sno = GetSno(Hud.Sno.SnoPowers.Crusader_SteedCharge, CrusaderSteedChargePowerSno);
                var slot = FindEquippedSkillBySno(sno);
                if (slot == null ||
                    slot.Skill == null ||
                    slot.SlotIndex < 0 ||
                    slot.SlotIndex >= _enabledSlots.Length ||
                    !_enabledSlots[slot.SlotIndex])
                {
                    return false;
                }

                bool active;
                double remaining;
                return TryGetBuffRemainingSeconds(slot.Skill, -1, out active, out remaining)
                    && active
                    && IsFiniteBuffTime(remaining)
                    && remaining <= Math.Max(0, SteedChargeRenewalLeadMs) / 1000.0d;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFiniteBuffTime(double seconds)
        {
            return !double.IsNaN(seconds)
                && !double.IsInfinity(seconds)
                && seconds != double.MaxValue;
        }

        private bool IsLocalCrusader()
        {
            try
            {
                return Hud != null
                    && Hud.Game != null
                    && Hud.Game.Me != null
                    && Hud.Game.Me.HeroClassDefinition != null
                    && Hud.Game.Me.HeroClassDefinition.HeroClass == HeroClass.Crusader;
            }
            catch
            {
                return false;
            }
        }

        private bool IsUnreservedHealingMantra(IPlayerSkill skill)
        {
            if(skill==null||skill.SnoPower==null||Hud==null||Hud.Game==null||Hud.Game.Me==null)return false;
            if(skill.SnoPower.Sno!=Hud.Sno.SnoPowers.Monk_MantraOfHealing.Sno)return false;
            var me=Hud.Game.Me;
            bool pylon=me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno);
            return !S7oHealingMantraPolicy.UseIdleSpiritReserve(me.AnimationState,pylon);
        }

        private bool HasEnoughPrimaryResource(IPlayerSkill skill, int spareResource, float baseRequirement)
        {
            try
            {
                if (skill == null || skill.Player == null || skill.Player.Stats == null)
                    return false;

                // Active Healing Mantra requests are never suppressed by a Spirit quota.
                // This also covers the outer conditional/manual-slot cast checks.
                if(IsUnreservedHealingMantra(skill))return true;

                float requirement = baseRequirement > 0 ? skill.GetResourceRequirement(baseRequirement) : skill.GetResourceRequirement();
                requirement += spareResource;

                if (requirement <= 0)
                    return true;

                return skill.Player.Stats.ResourceCurPri >= requirement;
            }
            catch
            {
                return true;
            }
        }

        private bool IsActionCursorSafe(ActionKey actionKey)
        {
            if (actionKey == ActionKey.Heal)
                return true;

            // Do not let an automatic cast cancel an item, chest, portal, shrine,
            // or other world interaction already selected under the cursor.
            if (IsSelectedClickableWorldObject() &&
                (actionKey != ActionKey.LeftSkill || BlockLeftSkillOnSelectedClickableActor))
                return false;

            if (actionKey == ActionKey.LeftSkill)
            {
                if (!IsCursorInsideGroundRect())
                    return false;

                return true;
            }

            if (actionKey == ActionKey.RightSkill)
                return IsCursorInsideGroundRect();

            return IsCursorInsideGameWindow();
        }

        private bool IsSelectedClickableWorldObject()
        {
            try
            {
                if (Hud == null || Hud.Game == null)
                    return false;

                var actor = Hud.Game.SelectedActor;
                if (actor == null)
                    return false;

                if (Hud.Game.SelectedMonster1 != null || Hud.Game.SelectedMonster2 != null)
                    return false;

                if (actor.IsClickable || actor.DisplayOnOverlay)
                    return true;

                if (actor.GizmoType != GizmoType.Invalid)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsCursorInsideGroundRect()
        {
            try
            {
                return Hud.Window != null && Hud.Window.GroundRectangle.Contains(Hud.Window.CursorX, Hud.Window.CursorY);
            }
            catch
            {
                return false;
            }
        }

        private bool IsCursorInsideGameWindow()
        {
            try
            {
                if (Hud.Window == null)
                    return false;

                int x = Hud.Window.CursorX;
                int y = Hud.Window.CursorY;
                return x >= 0 && x < Hud.Window.Size.Width &&
                    y >= 0 && y < Hud.Window.Size.Height;
            }
            catch
            {
                return false;
            }
        }

        private bool IsChannelSafeInstantCast(IPlayerSkill skill, ActionKey actionKey)
        {
            if (actionKey == ActionKey.Heal)
                return true;

            try
            {
                if (skill == null || skill.SnoPower == null)
                    return false;

                uint sno = skill.SnoPower.Sno;
                return sno == GetSno(Hud.Sno.SnoPowers.Necromancer_Devour, NecromancerDevourPowerSno)
                    || sno == GetSno(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead, NecromancerLandOfTheDeadPowerSno);
            }
            catch
            {
                return false;
            }
        }

        private bool IsChannelingSkill(IPlayerSkill skill)
        {
            if (skill == null)
                return false;

            try
            {
                if (skill.SnoPower != null && _channelingPowerSnos.Contains(skill.SnoPower.Sno))
                    return true;

                if (skill.CurrentSnoPower != null && _channelingPowerSnos.Contains(skill.CurrentSnoPower.Sno))
                    return true;

                string name = skill.SnoPower != null ? skill.SnoPower.NameEnglish : null;
                if (string.IsNullOrEmpty(name))
                    return false;

                return name == "Whirlwind"
                    || name == "Strafe"
                    || name == "Firebats"
                    || name == "Ray of Frost"
                    || name == "Arcane Torrent"
                    || name == "Disintegrate"
                    || name == "Tempest Rush"
                    || name == "Siphon Blood";
            }
            catch
            {
                return false;
            }
        }

        private void BuildChannelingSkillSet()
        {
            _channelingPowerSnos.Clear();

            AddChannelingSno(Hud.Sno.SnoPowers.Barbarian_Whirlwind);
            AddChannelingSno(Hud.Sno.SnoPowers.DemonHunter_Strafe);
            AddChannelingSno(Hud.Sno.SnoPowers.WitchDoctor_Firebats);
            AddChannelingSno(Hud.Sno.SnoPowers.Wizard_RayOfFrost);
            AddChannelingSno(Hud.Sno.SnoPowers.Wizard_ArcaneTorrent);
            AddChannelingSno(Hud.Sno.SnoPowers.Wizard_Disintegrate);
            AddChannelingSno(Hud.Sno.SnoPowers.Monk_TempestRush);
            AddChannelingSno(Hud.Sno.SnoPowers.Necromancer_SiphonBlood);
        }

        private void AddChannelingSno(ISnoPower power)
        {
            if (power != null && power.Sno != 0)
                _channelingPowerSnos.Add(power.Sno);
        }

        private void MarkSkillCast(IPlayerSkill skill, int now, ActionKey actionKey)
        {
            if (skill != null && skill.SnoPower != null)
                _lastCastTickByPower[skill.SnoPower.Sno] = now;

            if (actionKey != ActionKey.Heal)
                _lastGlobalCastTick = now;
            if (DebugLogActive && IsDiagnosticFocusSkill(skill))
            {
                _diagnosticResponseBurstUntilTick = unchecked(now + 750);
                _nextDiagnosticStateTick = now;
            }
        }

        public ushort GetCastVirtualKey(ActionKey actionKey)
        {
            switch (actionKey)
            {
                case ActionKey.Skill1: return Skill1VirtualKey;
                case ActionKey.Skill2: return Skill2VirtualKey;
                case ActionKey.Skill3: return Skill3VirtualKey;
                case ActionKey.Skill4: return Skill4VirtualKey;
                case ActionKey.Heal: return HealVirtualKey;
                default: return 0;
            }
        }

        public bool SetCastVirtualKey(ActionKey actionKey, ushort virtualKey)
        {
            if (virtualKey == 0)
                return false;

            switch (actionKey)
            {
                case ActionKey.Skill1:
                    Skill1VirtualKey = virtualKey;
                    break;

                case ActionKey.Skill2:
                    Skill2VirtualKey = virtualKey;
                    break;

                case ActionKey.Skill3:
                    Skill3VirtualKey = virtualKey;
                    break;

                case ActionKey.Skill4:
                    Skill4VirtualKey = virtualKey;
                    break;

                case ActionKey.Heal:
                    HealVirtualKey = virtualKey;
                    break;

                default:
                    return false;
            }

            SaveUserSettings();
            return true;
        }

        #endregion

        #region Input Layer

        private bool StartAutoCastPulse(ActionKey actionKey, bool standStill, int now)
        {
            ushort key = GetCastVirtualKey(actionKey);
            bool left = actionKey == ActionKey.LeftSkill, right = actionKey == ActionKey.RightSkill;
            if (!left && !right && key == 0) return false;
            int code = left ? 0x10001 : right ? 0x10002 : key;
            uint downFlags = left ? LeftDown : RightDown;
            uint upFlags = left ? LeftUp : right ? RightUp : 0;
            ushort modifier = standStill && IsSkillActionKey(actionKey) && ForceStandstillVirtualKey != 0
                && ForceStandstillVirtualKey != key && !IsKeyPhysicallyDown(ForceStandstillVirtualKey)
                ? ForceStandstillVirtualKey : (ushort)0;
            _autoCastPulseAction = actionKey;
            bool started = false;
            try
            {
                started = _autoCastPulse.TryStart(now, AutoCastPulseDurationMs,
                    () => left || right ? SendMouse(downFlags) : SendKeyDown(key),
                    () => ReleaseAutoCastPulseInput(code, key, upFlags),
                    modifier == 0 ? (Func<bool>)null : () => SendKeyDown(modifier),
                    modifier == 0 ? (Func<bool>)null : () => ReleaseAutoCastPulseInput(modifier, modifier, 0));
                if (started) LogDebug("INPUT_PULSE_START action="+actionKey+", holdBudgetMs="+AutoCastPulseDurationMs
                    +", modifierOwned="+(modifier!=0)+", aimMoved=False, evidence=input-request-not-game-cast");
                return started;
            }
            finally
            {
                if (!started && !_autoCastPulse.Active) _autoCastPulseAction = ActionKey.Unknown;
            }
        }

        private bool ReleaseAutoCastPulseInput(int code, ushort key, uint mouseUp)
        {
            // A deferred UP keeps its exact original code and foreground check in the shared arbiter.
            return s7o_InputReleaseArbiter.Up("AutoSkill", code, () =>
            {
                if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground) return false;
                var input = new Input[1];
                if (code == 0x10001 || code == 0x10002)
                { input[0].Type=InputMouse; input[0].U.Mouse.Flags=mouseUp; }
                else
                { input[0].Type=InputKeyboard; input[0].U.Keyboard.VirtualKey=key; input[0].U.Keyboard.Flags=KeyUp; }
                return SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
            });
        }

        private void ServiceAutoCastPulse(int now)
        {
            if (!_autoCastPulse.Active) return;
            bool cancel = !Enabled || Hud == null || Hud.Game == null || !Hud.Game.IsInGame
                || Hud.Game.IsLoading || Hud.Game.IsPaused || Hud.Game.Me == null || Hud.Game.Me.IsDead
                || Hud.Window == null || !Hud.Window.IsForeground;
            if (!cancel && PauseWhileWindowsKeyHeld) cancel = IsWindowsKeyDown();
            if (!cancel) cancel = IsBlockingUiOpen();
            bool wasActive = _autoCastPulse.Active;
            _autoCastPulse.Update(now, cancel);
            if (wasActive && !_autoCastPulse.Active)
            {
                LogDebug("INPUT_PULSE_END action="+_autoCastPulseAction+", heldMs="+unchecked(now-_autoCastPulse.Started)
                    +", canceled="+cancel);
                _autoCastPulseAction = ActionKey.Unknown;
            }
        }

        private void StopAutoCastPulse()
        {
            if (!_autoCastPulse.Active) return;
            _autoCastPulse.Cancel();
            if (!_autoCastPulse.Active)
            {
                LogDebug("INPUT_PULSE_CANCEL action="+_autoCastPulseAction);
                _autoCastPulseAction = ActionKey.Unknown;
            }
        }

        private bool DoAction(ActionKey actionKey, bool standStill)
        {
            // Do not send UP for a key/button that is already held by the user or another owner.
            if (_autoCastPulse.Active || MacroActionHeld(actionKey)) return false;
            // Explicit cursor handoffs retain their existing synchronous lifetime.
            if (!S7oMacroQueries.HasAim && !_importedRestore && !_commandSkeletonsCursorRestoreKnown)
                return StartAutoCastPulse(actionKey, standStill, Environment.TickCount);
            // LightningMOD used DoActionAutoShift() for normal autocasts.  The FreeHUD SendInput
            // replacement must honor that same standstill request for all skill slots, not only LMB,
            // otherwise cursor-targeted keyboard skills can make the character path toward the cursor.
            bool shouldStandStill = standStill && IsSkillActionKey(actionKey) && ForceStandstillVirtualKey != 0;

            // If the user is already physically holding the standstill key (e.g. Shift), do NOT inject
            // a synthetic key-down/key-up pair around our cast.  Sending a synthetic KeyUp would stomp
            // the OS-level "key is held" state even though the user's finger never moved, which is
            // exactly what caused Black Hole to stop working after EB autocasted while Shift was held.
            bool userAlreadyHolding = shouldStandStill && IsKeyPhysicallyDown(ForceStandstillVirtualKey);

            try
            {
                if (shouldStandStill && !userAlreadyHolding
                    && !SendKeyDown(ForceStandstillVirtualKey)) return false;

                bool result = false;

                switch (actionKey)
                {
                    case ActionKey.LeftSkill:
                        result = SendMouse(LeftDown) && SendMouse(LeftUp);
                        break;

                    case ActionKey.RightSkill:
                        result = SendMouse(RightDown) && SendMouse(RightUp);
                        break;

                    case ActionKey.Skill1:
                        result = PressKey(Skill1VirtualKey);
                        break;

                    case ActionKey.Skill2:
                        result = PressKey(Skill2VirtualKey);
                        break;

                    case ActionKey.Skill3:
                        result = PressKey(Skill3VirtualKey);
                        break;

                    case ActionKey.Skill4:
                        result = PressKey(Skill4VirtualKey);
                        break;

                    case ActionKey.Heal:
                        result = PressKey(HealVirtualKey);
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                LogDebug("DoAction failed: " + ex.Message);
                return false;
            }
            finally
            {
                // Only release the standstill key if we were the ones who pressed it.
                // If the user was already holding it when we entered, leave the state alone.
                if (shouldStandStill && !userAlreadyHolding)
                    SendKeyUp(ForceStandstillVirtualKey);
            }
        }

        private static bool IsSkillActionKey(ActionKey actionKey)
        {
            switch (actionKey)
            {
                case ActionKey.LeftSkill:
                case ActionKey.RightSkill:
                case ActionKey.Skill1:
                case ActionKey.Skill2:
                case ActionKey.Skill3:
                case ActionKey.Skill4:
                    return true;

                default:
                    return false;
            }
        }

        private static bool PressKey(ushort virtualKey)
        {
            if (virtualKey == 0) return false;
            return SendKeyDown(virtualKey) && SendKeyUp(virtualKey);
        }

        private static bool SendKeyDown(ushort virtualKey)
        {
            return SendKey(virtualKey, false);
        }

        private static bool SendKeyUp(ushort virtualKey)
        {
            return SendKey(virtualKey, true);
        }

        private static bool SendKey(ushort virtualKey, bool keyUp)
        {
            if (virtualKey == 0) return false;

            var input = new Input[1];
            input[0].Type = InputKeyboard;
            input[0].U.Keyboard.VirtualKey = virtualKey;
            input[0].U.Keyboard.Flags = keyUp ? KeyUp : 0;

            Func<bool> send = () => SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
            return keyUp ? s7o_InputReleaseArbiter.Up("AutoSkill", virtualKey, send)
                : s7o_InputReleaseArbiter.Down("AutoSkill", virtualKey, send);
        }

        private static bool SendMouse(uint flags)
        {
            var input = new Input[1];
            input[0].Type = InputMouse;
            input[0].U.Mouse.Flags = flags;

            int code = (flags == LeftDown || flags == LeftUp) ? 0x10001 : 0x10002;
            Func<bool> send = () => SendInput(1, input, Marshal.SizeOf(typeof(Input))) == 1;
            return (flags == LeftUp || flags == RightUp)
                ? s7o_InputReleaseArbiter.Up("AutoSkill", code, send)
                : s7o_InputReleaseArbiter.Down("AutoSkill", code, send);
        }

        #endregion

        #region Class macro policies
        public ushort MacroForceMoveVirtualKey = 0;
        public int HealingMantraCombatRefreshMs = 100;
        public double HealingMantraSpiritReserve = 0.35;
        private readonly List<ImportedBinding> _importedBindings = new List<ImportedBinding>();
        private readonly Dictionary<uint,IWatch> _importedSkillWatches = new Dictionary<uint,IWatch>();
        private readonly Dictionary<uint,ImportedBinding> _importedBuffBindings = new Dictionary<uint,ImportedBinding>();
        private readonly List<MacroDescriptor> _macroCatalog = new List<MacroDescriptor>();
        private IWatch _importedSimpleWatch,_importedBuffWatch;
        private ImportedBinding _importedEvaluating;
        private bool _importedRestore;
        private int _importedRestoreX,_importedRestoreY;
        private readonly S7oTestContext _importedContext = new S7oTestContext();
        private ImportedBinding _importedChannel;
        private ActionKey _importedChannelKey=ActionKey.Unknown;
        private ushort _importedChannelStandstill;
        private int _importedLootPauseTick=int.MinValue;
        private bool _importedUrshiQuiet;
        private bool _importedOverrideActive;
        public sealed class MacroDescriptor
        {
            public string Code,Title,Description,ClassName,DescriptionKey,TitleSuffix;
            public uint PowerSno;
            public bool IsBuff;
        }
        private sealed class ImportedBinding
        {
            public string Code,Title,Description,ClassName,DescriptionKey,TitleSuffix;
            public ISnoPower Power;
            public S7oImportedPolicy Policy;
            public bool Loaded;
        }
        public IEnumerable<MacroDescriptor> GetMacroCatalog(){return _macroCatalog;}
        public void OnMonsterKilled(IMonster monster){S7oHealingMantraPolicy.NoteKill(Hud,monster);}
        internal int MacroDelay(string key,int min,int max,int after)
        {return GetRandomizedDelay("macro_"+key,min,max,Math.Max(25,after));}
        internal ushort MacroVirtualKey(ActionKey key)
        {return key==ActionKey.Move?MacroForceMoveVirtualKey:GetCastVirtualKey(key);}
        internal bool MacroActionHeld(ActionKey key)
        {
            if(key==ActionKey.LeftSkill)return IsKeyPhysicallyDown(0x01);
            if(key==ActionKey.RightSkill)return IsKeyPhysicallyDown(0x02);
            ushort vk=MacroVirtualKey(key);return vk!=0&&IsKeyPhysicallyDown(vk);
        }
        private void RegisterImported(S7oImportedPolicy policy,ISnoPower power,string code,string title,string className,string description,string descriptionKey,string suffix)
        {
            policy.Owner=this;policy.Hud=Hud;policy.AssignedSnoPower=power;
            var binding=new ImportedBinding{Code=code,Title=title,ClassName=className,Description=description,DescriptionKey=descriptionKey,TitleSuffix=suffix,Power=power,Policy=policy};
            _importedBindings.Add(binding);
            var buff=_buffProfiles.FirstOrDefault(x=>x.Code==code&&x.Sno==power.Sno);
            if(buff!=null){_importedBuffBindings[power.Sno]=binding;return;}
            var existing=_conditionalProfiles.FirstOrDefault(x=>x.Code==code&&x.Sno==power.Sno);
            if(existing!=null)
            {existing.Description=description;existing.CastDelayMinMs=existing.CastDelayMaxMs=25;existing.RecheckMs=25;
             existing.ReservePrimaryResource=existing.ReserveSecondaryResource=0;existing.BasePrimaryResourceRequirement=existing.BaseSecondaryResourceRequirement=0;
             existing.AllowChannelingSkill=policy.SkillType==S7oCastType.RangedChannelingSkill;existing.ShouldCast=ctx=>EvaluateImported(binding,ctx.Skill,ctx.Now);return;}
            AddConditionalProfile(power,power.Sno,code,title,className,"Skill",description,false,
                ctx=>EvaluateImported(binding,ctx.Skill,ctx.Now),25,25,25,0,0,0,0,true,false,policy.SkillType==S7oCastType.RangedChannelingSkill);
        }
        private void BuildImportedPolicies()
        {
            _importedBindings.Clear();_importedBuffBindings.Clear();
            _importedSimpleWatch=Hud.Time.CreateWatch();_importedBuffWatch=Hud.Time.CreateWatch();
            RegisterImported(new S7oPolicy_DrinkHealthPotionArchonMeteorBuild(),Hud.Sno.SnoPowers.Generic_DrinkHealthPotion,"Skill_DrinkHealthPotionArchonMeteorBuild","Potion: Archon Meteor","AllClasses","Equip an Armor potion for toughness outside Archon; casts before Arcane CoE.","macro.policy.archon_potion","");
            RegisterImported(new S7oPolicy_DrinkHealthPotion(),Hud.Sno.SnoPowers.Generic_DrinkHealthPotion,"Skill_DrinkHealthPotion","Potion: Health and Altar","AllClasses","Use the potion for recovery and enabled Altar effects.","macro.policy.potion","");
            RegisterImported(new S7oPolicy_BarbarianAncientSpear(),Hud.Sno.SnoPowers.Barbarian_AncientSpear,"Skill_BarbarianAncientSpear","Ancient Spear","Barbarian","Use Boulder Toss near full Fury with the required set and items.","macro.policy.density","");
            RegisterImported(new S7oPolicy_BarbarianBattleRage(),Hud.Sno.SnoPowers.Barbarian_BattleRage,"Barbarian_BattleRage_KeepUp","Battle Rage","Barbarian","Refresh the buff before it expires.","macro.policy.upkeep","");
            RegisterImported(new S7oPolicy_BarbarianCallOfTheAncients(),Hud.Sno.SnoPowers.Barbarian_CallOfTheAncients,"Barbarian_COTA_Combat","Call Of The Ancients","Barbarian","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_BarbarianIgnorePain(),Hud.Sno.SnoPowers.Barbarian_IgnorePain,"Barbarian_IgnorePain_Defensive","Ignore Pain","Barbarian","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_BarbarianLeap(),Hud.Sno.SnoPowers.Barbarian_Leap,"Skill_BarbarianLeap","Leap","Barbarian","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_BarbarianOverPower(),Hud.Sno.SnoPowers.Barbarian_Overpower,"Skill_BarbarianOverPower","Overpower","Barbarian","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_BarbarianRend(),Hud.Sno.SnoPowers.Barbarian_Rend,"Skill_BarbarianRend","Rend","Barbarian","Refresh Rend when manual application is useful.","macro.policy.rend","");
            RegisterImported(new S7oPolicy_BarbarianSeismicSlam(),Hud.Sno.SnoPowers.Barbarian_SeismicSlam,"Skill_BarbarianSeismicSlam","Seismic Slam","Barbarian","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_BarbarianSprint(),Hud.Sno.SnoPowers.Barbarian_Sprint,"Barbarian_Sprint_KeepUpMoving","Sprint","Barbarian","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_BarbarianThreateningShout(),Hud.Sno.SnoPowers.Barbarian_ThreateningShout,"Skill_BarbarianThreateningShout","Threatening Shout","Barbarian","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_BarbarianWarCry(),Hud.Sno.SnoPowers.Barbarian_WarCry,"Barbarian_WarCry_KeepUp","War Cry","Barbarian","Refresh the buff before it expires.","macro.policy.upkeep","");
            RegisterImported(new S7oPolicy_BarbarianWrathOfTheBerserker(),Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker,"Barbarian_WOTB_EliteOrDensity","Wrath Of The Berserker","Barbarian","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_CrusaderAkaratsChampion(),Hud.Sno.SnoPowers.Crusader_AkaratsChampion,"Crusader_AkaratsChampion_Combat","Akarats Champion","Crusader","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_CrusaderBlessedShield(),Hud.Sno.SnoPowers.Crusader_BlessedShield,"Skill_CrusaderBlessedShield","Blessed Shield","Crusader","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_CrusaderBombardment(),Hud.Sno.SnoPowers.Crusader_Bombardment,"Crusader_Bombardment_EliteOrDensity","Bombardment","Crusader","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_CrusaderCondemn(),Hud.Sno.SnoPowers.Crusader_Condemn,"Skill_CrusaderCondemn","Condemn","Crusader","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_CrusaderFallingSword(),Hud.Sno.SnoPowers.Crusader_FallingSword,"Skill_CrusaderFallingSword","Falling Sword","Crusader","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_CrusaderFistoftheHeavens(),Hud.Sno.SnoPowers.Crusader_FistOfTheHeavens,"Skill_CrusaderFistoftheHeavens","Fist Of The Heavens","Crusader","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_CrusaderIronSkin(),Hud.Sno.SnoPowers.Crusader_IronSkin,"Crusader_IronSkin_Defensive","Iron Skin","Crusader","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_CrusaderJudgment(),Hud.Sno.SnoPowers.Crusader_Judgment,"Skill_CrusaderJudgment","Judgment","Crusader","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_CrusaderJustice(),Hud.Sno.SnoPowers.Crusader_Justice,"Skill_CrusaderJustice","Justice","Crusader","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_CrusaderLawsOfHope(),Hud.Sno.SnoPowers.Crusader_LawsOfHope,"Crusader_LawsOfHope_Defensive","Laws Of Hope","Crusader","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_CrusaderLawsOfJustice(),Hud.Sno.SnoPowers.Crusader_LawsOfJustice,"Crusader_LawsOfJustice_Defensive","Laws Of Justice","Crusader","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_CrusaderLawsOfValor(),Hud.Sno.SnoPowers.Crusader_LawsOfValor,"Crusader_LawsOfValor_Elite","Laws Of Valor","Crusader","Maintain the active Law using rune and build checks.","macro.policy.law","");
            RegisterImported(new S7oPolicy_CrusaderPhalanx(),Hud.Sno.SnoPowers.Crusader_Phalanx,"Skill_CrusaderPhalanx","Phalanx","Crusader","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_CrusaderProvoke(),Hud.Sno.SnoPowers.Crusader_Provoke,"Crusader_Provoke_ResourceOrDensity","Provoke","Crusader","Recover resource or health with the equipped rune.","macro.policy.resource","");
            RegisterImported(new S7oPolicy_CrusaderShieldGlare(),Hud.Sno.SnoPowers.Crusader_ShieldGlare,"Skill_CrusaderShieldGlare","Shield Glare","Crusader","Use crowd control or its buff with the equipped rune.","macro.policy.control_buff","");
            RegisterImported(new S7oPolicy_CrusaderSteedCharge(),Hud.Sno.SnoPowers.Crusader_SteedCharge,"Skill_CrusaderSteedCharge","Steed Charge","Crusader","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_DemonHunterChakramShurikenCloud(),Hud.Sno.SnoPowers.DemonHunter_Chakram,"Skill_DemonHunterChakramShurikenCloud","Chakram","DemonHunter","Maintain the Shuriken Cloud effect.","macro.policy.shuriken","");
            RegisterImported(new S7oPolicy_DemonHunterBolas(),Hud.Sno.SnoPowers.DemonHunter_Bolas,"Skill_DemonHunterBolas","Bolas","DemonHunter","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_DemonHunterCaltrops(),Hud.Sno.SnoPowers.DemonHunter_Caltrops,"Skill_DemonHunterCaltrops","Caltrops","DemonHunter","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_DemonHunterCompanion(),Hud.Sno.SnoPowers.DemonHunter_Companion,"DH_Companion_Bat_Hatred","Companion: Resource","DemonHunter","Use the companion active effect with rune and combat checks.","macro.policy.companion","resource");
            RegisterImported(new S7oPolicy_DemonHunterEntanglingShot(),Hud.Sno.SnoPowers.DemonHunter_EntanglingShot,"Skill_DemonHunterEntanglingShot","Entangling Shot","DemonHunter","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_DemonHunterEvasiveFire(),Hud.Sno.SnoPowers.DemonHunter_EvasiveFire,"Skill_DemonHunterEvasiveFire","Evasive Fire","DemonHunter","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_DemonHunterFanofKnives(),Hud.Sno.SnoPowers.DemonHunter_FanOfKnives,"Skill_DemonHunterFanofKnives","Fan Of Knives","DemonHunter","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_DemonHunterHungeringArrow(),Hud.Sno.SnoPowers.DemonHunter_HungeringArrow,"Skill_DemonHunterHungeringArrow","Hungering Arrow","DemonHunter","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_DemonHunterImpale(),Hud.Sno.SnoPowers.DemonHunter_Impale,"Skill_DemonHunterImpale","Impale","DemonHunter","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_DemonHunterMarkedforDeath(),Hud.Sno.SnoPowers.DemonHunter_MarkedForDeath,"Skill_DemonHunterMarkedforDeath","Marked For Death","DemonHunter","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_DemonHunterMultishot(),Hud.Sno.SnoPowers.DemonHunter_Multishot,"Skill_DemonHunterMultishot","Multishot","DemonHunter","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_DemonHunterPreparation(),Hud.Sno.SnoPowers.DemonHunter_Preparation,"DH_Preparation_DisciplineOrLife","Preparation: Recovery","DemonHunter","Recover resource or health with the equipped rune.","macro.policy.resource","recovery");
            RegisterImported(new S7oPolicy_DemonHunterPreparationPunishment(),Hud.Sno.SnoPowers.DemonHunter_Preparation,"DH_Preparation_Punishment_Hatred","Preparation: Hatred","DemonHunter","Recover resource or health with the equipped rune.","macro.policy.resource","hatred");
            RegisterImported(new S7oPolicy_DemonHunterSentry(),Hud.Sno.SnoPowers.DemonHunter_Sentry,"Skill_DemonHunterSentry","Sentry","DemonHunter","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_DemonHunterShadowPower(),Hud.Sno.SnoPowers.DemonHunter_ShadowPower,"DH_ShadowPower_Defensive","Shadow Power","DemonHunter","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_DemonHunterSmokeScreen(),Hud.Sno.SnoPowers.DemonHunter_SmokeScreen,"Skill_DemonHunterSmokeScreen","Smoke Screen","DemonHunter","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_DemonHunterVengeance(),Hud.Sno.SnoPowers.DemonHunter_Vengeance,"DH_Vengeance_KeepCombatBuff","Vengeance","DemonHunter","Refresh the buff before it expires.","macro.policy.upkeep","");
            RegisterImported(new S7oPolicy_MonkBlindingFlash(),Hud.Sno.SnoPowers.Monk_BlindingFlash,"Skill_MonkBlindingFlash","Blinding Flash","Monk","Use crowd control or its buff with the equipped rune.","macro.policy.control_buff","");
            RegisterImported(new S7oPolicy_MonkBreathOfHeaven(),Hud.Sno.SnoPowers.Monk_BreathOfHeaven,"Skill_MonkBreathOfHeaven","Breath Of Heaven","Monk","Use healing or buff effects with the equipped rune.","macro.policy.heal_buff","");
            RegisterImported(new S7oPolicy_MonkCripplingWave(),Hud.Sno.SnoPowers.Monk_CripplingWave,"Skill_MonkCripplingWave","Crippling Wave","Monk","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_MonkCycloneStrike(),Hud.Sno.SnoPowers.Monk_CycloneStrike,"Skill_MonkCycloneStrike","Cyclone Strike","Monk","Pull nearby enemies using the equipped build rules.","macro.policy.pull","");
            RegisterImported(new S7oPolicy_MonkDashingStrike(),Hud.Sno.SnoPowers.Monk_DashingStrike,"Skill_MonkDashingStrike","Dashing Strike","Monk","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_MonkDeadlyReach(),Hud.Sno.SnoPowers.Monk_DeadlyReach,"Skill_MonkDeadlyReach","Deadly Reach","Monk","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_MonkEpiphany(),Hud.Sno.SnoPowers.Monk_Epiphany,"Monk_Epiphany_Combat","Epiphany","Monk","Refresh the buff before it expires.","macro.policy.upkeep","");
            RegisterImported(new S7oPolicy_MonkFistsOfThunder(),Hud.Sno.SnoPowers.Monk_FistsOfThunder,"Skill_MonkFistsOfThunder","Fists Of Thunder","Monk","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_MonkInnerSanctuary(),Hud.Sno.SnoPowers.Monk_InnerSanctuary,"Skill_MonkInnerSanctuary","Inner Sanctuary","Monk","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_MonkMantraOfConviction(),Hud.Sno.SnoPowers.Monk_MantraOfConviction,"Monk_MantraConviction_Elite","Mantra Of Conviction","Monk","Activate near elites or while Epiphany is active.","macro.policy.conviction","");
            RegisterImported(new S7oPolicy_MonkMantraOfSalvation(),Hud.Sno.SnoPowers.Monk_MantraOfSalvation,"Monk_MantraSalvation_Defensive","Mantra Of Salvation","Monk","Maintain the active bonus for yourself and nearby allies while idle, moving or attacking; renew duration without combat spam.","macro.policy.salvation","");
            RegisterImported(new S7oPolicy_MonkMysticAlly(),Hud.Sno.SnoPowers.Monk_MysticAlly,"Monk_MysticAlly_ResourceOrElite","Mystic Ally","Monk","Use the active ally effect with rune and pet-count checks.","macro.policy.ally","");
            RegisterImported(new S7oPolicy_MonkSerenity(),Hud.Sno.SnoPowers.Monk_Serenity,"Monk_Serenity_Defensive","Serenity","Monk","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_MonkSweepingWind(),Hud.Sno.SnoPowers.Monk_SweepingWind,"Monk_SweepingWind_KeepUp","Sweeping Wind","Monk","Maintain Sweeping Wind while conserving Spirit.","macro.policy.wind","");
            RegisterImported(new S7oPolicy_MonkWayOfTheHundredFists(),Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists,"Skill_MonkWayOfTheHundredFists","Way Of The Hundred Fists","Monk","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_NecArmyoftheDead(),Hud.Sno.SnoPowers.Necromancer_ArmyOfTheDead,"Skill_NecArmyoftheDead","Army Of The Dead","Necromancer","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_NecBloodRush(),Hud.Sno.SnoPowers.Necromancer_BloodRush,"Skill_NecBloodRush","Blood Rush","Necromancer","Use movement when the equipped build conditions match.","macro.policy.movement","");
            RegisterImported(new S7oPolicy_NecBoneArmor(),Hud.Sno.SnoPowers.Necromancer_BoneArmor,"Necro_BoneArmor_NearEnemies","Bone Armor","Necromancer","Use nearby enemies to refresh defensive stacks.","macro.policy.stacks_defense","");
            RegisterImported(new S7oPolicy_NecCommandSkeletons(),Hud.Sno.SnoPowers.Necromancer_CommandSkeletons,"Necro_CommandSkeletons_Elite","Command Skeletons","Necromancer","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_NecDecrepify(),Hud.Sno.SnoPowers.Necromancer_Decrepify,"Skill_NecDecrepify","Decrepify","Necromancer","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_NecDevour(),Hud.Sno.SnoPowers.Necromancer_Devour,"Necro_Devour_CorpseOrLotD","Devour","Necromancer","Consume corpses for recovery and buff upkeep.","macro.policy.corpses","");
            RegisterImported(new S7oPolicy_NecFrailty(),Hud.Sno.SnoPowers.Necromancer_Frailty,"Skill_NecFrailty","Frailty","Necromancer","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_NecLandOfTheDead(),Hud.Sno.SnoPowers.Necromancer_LandOfTheDead,"Necro_LandOfTheDead_EliteOrDensity","Land Of The Dead","Necromancer","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_NecLeech(),Hud.Sno.SnoPowers.Necromancer_Leech,"Skill_NecLeech","Leech","Necromancer","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_BoneArmor(),Hud.Sno.SnoPowers.Necromancer_BoneArmor,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_BoneSpear(),Hud.Sno.SnoPowers.Necromancer_BoneSpear,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_BoneSpikes(),Hud.Sno.SnoPowers.Necromancer_BoneSpikes,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_CommandSkeletons(),Hud.Sno.SnoPowers.Necromancer_CommandSkeletons,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_DeathNova(),Hud.Sno.SnoPowers.Necromancer_DeathNova,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_Revive(),Hud.Sno.SnoPowers.Necromancer_Revive,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_SiphonBlood(),Hud.Sno.SnoPowers.Necromancer_SiphonBlood,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecNayrsBlackDeath_SkeletalMage(),Hud.Sno.SnoPowers.Necromancer_SkeletalMage,"Necro_NayrsBlackDeath","Nayr's Black Death","Necromancer","Refresh poison skills to maintain Nayr's stacks.","macro.policy.poison","");
            RegisterImported(new S7oPolicy_NecRevive(),Hud.Sno.SnoPowers.Necromancer_Revive,"Skill_NecRevive","Revive","Necromancer","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_NecSimulacrum(),Hud.Sno.SnoPowers.Necromancer_Simulacrum,"Necro_Simulacrum_EliteOrDensity","Simulacrum","Necromancer","Restore missing Simulacrums; time temporary summons with combat and cooldown checks.","macro.policy.simulacrum","");
            RegisterImported(new S7oPolicy_NecSiphonBlood(),Hud.Sno.SnoPowers.Necromancer_SiphonBlood,"Skill_NecSiphonBlood","Siphon Blood","Necromancer","Channel on the selected enemy to refresh the damage buff.","macro.policy.channel","");
            RegisterImported(new S7oPolicy_NecSkeletalMage(),Hud.Sno.SnoPowers.Necromancer_SkeletalMage,"Necro_SkeletalMage_EssenceHigh","Skeletal Mage","Necromancer","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_WitchDoctorBigBadVoodoo(),Hud.Sno.SnoPowers.WitchDoctor_BigBadVoodoo,"WD_BigBadVoodoo_EliteOrDensity","Big Bad Voodoo","WitchDoctor","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_WitchDoctorCorpseSpiders(),Hud.Sno.SnoPowers.WitchDoctor_CorpseSpider,"Skill_WitchDoctorCorpseSpiders","Corpse Spider","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorFetishArmy(),Hud.Sno.SnoPowers.WitchDoctor_FetishArmy,"WD_FetishArmy_Combat","Fetish Army","WitchDoctor","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_WitchDoctorGargantuan(),Hud.Sno.SnoPowers.WitchDoctor_Gargantuan,"WD_Gargantuan_KeepUp","Gargantuan","WitchDoctor","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_WitchDoctorHex(),Hud.Sno.SnoPowers.WitchDoctor_Hex,"Skill_WitchDoctorHex","Hex","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorHorrify(),Hud.Sno.SnoPowers.WitchDoctor_Horrify,"WD_Horrify_Defensive","Horrify","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorLocustSwarm(),Hud.Sno.SnoPowers.WitchDoctor_LocustSwarm,"Skill_WitchDoctorLocustSwarm","Locust Swarm","WitchDoctor","Maintain the debuff on eligible enemies.","macro.policy.debuff","");
            RegisterImported(new S7oPolicy_WitchDoctorMassConfusion(),Hud.Sno.SnoPowers.WitchDoctor_MassConfusion,"Skill_WitchDoctorMassConfusion","Mass Confusion","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorPiranhas(),Hud.Sno.SnoPowers.WitchDoctor_Piranhas,"Skill_WitchDoctorPiranhas","Piranhas","WitchDoctor","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WitchDoctorSacrifice(),Hud.Sno.SnoPowers.WitchDoctor_Sacrifice,"Skill_WitchDoctorSacrifice","Sacrifice","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorSoulHarvestJadeHarvester(),Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest,"Skill_WitchDoctorSoulHarvestJadeHarvester","Soul Harvest: Jade: Damage","WitchDoctor","Consume damage-over-time effects with Soul Harvest.","macro.policy.jade","jade");
            RegisterImported(new S7oPolicy_WitchDoctorSoulHarvest(),Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest,"WD_SoulHarvest_NearEnemies","Soul Harvest","WitchDoctor","Use nearby enemies to refresh defensive stacks.","macro.policy.stacks_defense","");
            RegisterImported(new S7oPolicy_WitchDoctorSpiritBarragePhantasm(),Hud.Sno.SnoPowers.WitchDoctor_SpiritBarrage,"Skill_WitchDoctorSpiritBarragePhantasm","Spirit Barrage","WitchDoctor","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WitchDoctorSpiritWalk(),Hud.Sno.SnoPowers.WitchDoctor_SpiritWalk,"WD_SpiritWalk_Defensive","Spirit Walk","WitchDoctor","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WitchDoctorWallOfDeath(),Hud.Sno.SnoPowers.WitchDoctor_WallOfDeath,"Skill_WitchDoctorWallOfDeath","Wall Of Death","WitchDoctor","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WitchDoctorZombieDogs(),Hud.Sno.SnoPowers.WitchDoctor_SummonZombieDog,"WD_ZombieDogs_KeepUp","Summon Zombie Dog","WitchDoctor","Summon or refresh pets according to the skill's build and combat rules.","macro.policy.pets","");
            RegisterImported(new S7oPolicy_WizardArcaneOrb(),Hud.Sno.SnoPowers.Wizard_ArcaneOrb,"Skill_WizardArcaneOrb","Arcane Orb","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WizardArchonArcaneBlastCold(),Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastCold,"Skill_WizardArchonArcaneBlastCold","Archon Arcane Blast Cold: Cold","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","cold");
            RegisterImported(new S7oPolicy_WizardArchonArcaneBlastFire(),Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastFire,"Skill_WizardArchonArcaneBlastFire","Archon Arcane Blast Fire: Fire","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","fire");
            RegisterImported(new S7oPolicy_WizardArchonArcaneBlastLightning(),Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastLightning,"Skill_WizardArchonArcaneBlastLightning","Archon Arcane Blast Lightning: Lightning","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","lightning");
            RegisterImported(new S7oPolicy_WizardArchonArcaneBlast(),Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlast,"Skill_WizardArchonArcaneBlast","Archon Arcane Blast","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WizardArchon(),Hud.Sno.SnoPowers.Wizard_Archon,"Wizard_Archon_EliteOrDensity","Archon","Wizard","Enter Archon after the required damage stacks are ready.","macro.policy.archon","");
            RegisterImported(new S7oPolicy_WizardArchonSlowTime(),Hud.Sno.SnoPowers.Wizard_ArchonSlowTime,"Skill_WizardArchonSlowTime","Archon Slow Time","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WizardBlackHole(),Hud.Sno.SnoPowers.Wizard_BlackHole,"Skill_WizardBlackHole","Black Hole","Wizard","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WizardBlizzard(),Hud.Sno.SnoPowers.Wizard_Blizzard,"Skill_WizardBlizzard","Blizzard","Wizard","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WizardDiamondSkin(),Hud.Sno.SnoPowers.Wizard_DiamondSkin,"Wizard_DiamondSkin_Defensive","Diamond Skin","Wizard","Use defensive effects according to the skill's rune and combat rules.","macro.policy.defense","");
            RegisterImported(new S7oPolicy_WizardDisintegrate(),Hud.Sno.SnoPowers.Wizard_Disintegrate,"Skill_WizardDisintegrate","Disintegrate","Wizard","Maintain damage stacks for the equipped set or channeling buffs.","macro.policy.set_stacks","");
            RegisterImported(new S7oPolicy_WizardElectrocute(),Hud.Sno.SnoPowers.Wizard_Electrocute,"Skill_WizardElectrocute","Electrocute","Wizard","Maintain generator buffs while fighting.","macro.policy.generator","");
            RegisterImported(new S7oPolicy_WizardEnergyArmor(),Hud.Sno.SnoPowers.Wizard_EnergyArmor,"Wizard_EnergyArmor","Energy Armor","Wizard","Refresh before Archon and when the buff is expiring.","macro.policy.armor","");
            RegisterImported(new S7oPolicy_WizardExplosiveBlast(),Hud.Sno.SnoPowers.Wizard_ExplosiveBlast,"Wizard_ExplosiveBlast_CloseEnemies","Explosive Blast","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WizardFamiliar(),Hud.Sno.SnoPowers.Wizard_Familiar,"Wizard_Familiar","Familiar","Wizard","Refresh before Archon and when the buff is expiring.","macro.policy.armor","");
            RegisterImported(new S7oPolicy_WizardFrostNova(),Hud.Sno.SnoPowers.Wizard_FrostNova,"Wizard_FrostNova_CloseEnemies","Frost Nova","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            RegisterImported(new S7oPolicy_WizardHydra(),Hud.Sno.SnoPowers.Wizard_Hydra,"Skill_WizardHydra","Hydra","Wizard","Place or refresh ground effects according to the equipped build.","macro.policy.ground","");
            RegisterImported(new S7oPolicy_WizardIceArmor(),Hud.Sno.SnoPowers.Wizard_IceArmor,"Wizard_IceArmor","Ice Armor","Wizard","Refresh before Archon and when the buff is expiring.","macro.policy.armor","");
            RegisterImported(new S7oPolicy_WizardMagicWeapon(),Hud.Sno.SnoPowers.Wizard_MagicWeapon,"Wizard_MagicWeapon","Magic Weapon","Wizard","Refresh before Archon and when the buff is expiring.","macro.policy.armor","");
            RegisterImported(new S7oPolicy_WizardMirrorImage(),Hud.Sno.SnoPowers.Wizard_MirrorImage,"Wizard_MirrorImage_Defensive","Mirror Image","Wizard","Time damage with the equipped build and element cycle.","macro.policy.burst","");
            RegisterImported(new S7oPolicy_WizardStormArmor(),Hud.Sno.SnoPowers.Wizard_StormArmor,"Wizard_StormArmor","Storm Armor","Wizard","Refresh before Archon and when the buff is expiring.","macro.policy.armor","");
            RegisterImported(new S7oPolicy_WizardTriumvirate_Electrocute(),Hud.Sno.SnoPowers.Wizard_Electrocute,"Skill_WizardTriumvirate_Electrocute","Electrocute: Orb stacks","Wizard","Maintain generator buffs while fighting.","macro.policy.generator","orb");
            RegisterImported(new S7oPolicy_WizardTriumvirate_MagicMissile(),Hud.Sno.SnoPowers.Wizard_MagicMissile,"Skill_WizardTriumvirate_MagicMissile","Magic Missile: Orb stacks","Wizard","Maintain generator buffs while fighting.","macro.policy.generator","orb");
            RegisterImported(new S7oPolicy_WizardTriumvirate_ShockPulse(),Hud.Sno.SnoPowers.Wizard_ShockPulse,"Skill_WizardTriumvirate_ShockPulse","Shock Pulse: Orb stacks","Wizard","Maintain generator buffs while fighting.","macro.policy.generator","orb");
            RegisterImported(new S7oPolicy_WizardTriumvirate_SpectralBlade(),Hud.Sno.SnoPowers.Wizard_SpectralBlade,"Skill_WizardTriumvirate_SpectralBlade","Spectral Blade: Orb stacks","Wizard","Maintain generator buffs while fighting.","macro.policy.generator","orb");
            RegisterImported(new S7oPolicy_WizardWaveofForce(),Hud.Sno.SnoPowers.Wizard_WaveOfForce,"Skill_WizardWaveofForce","Wave Of Force","Wizard","Use against nearby enemies when its build conditions match.","macro.policy.combat","");
            var aliases=_conditionalProfiles.Where(p=>p.Code!="Monk_MantraHealing_Defensive"
                &&!_importedBindings.Any(b=>b.Code==p.Code&&b.Power.Sno==p.Sno)).ToArray();
            foreach(var alias in aliases)
            {
                var primary=_importedBindings.FirstOrDefault(b=>b.Power.Sno==alias.Sno&&b.Code!=NayrsBlackDeathProfileCode);
                if(primary==null)continue;
                string key=alias.Code=="DH_ShadowPower_ElusiveRing"?"macro.policy.ring":alias.Code=="DH_Companion_EliteOrBoss"?"macro.policy.companion":"macro.policy.resource";
                RegisterImported((S7oImportedPolicy)Activator.CreateInstance(primary.Policy.GetType()),primary.Power,
                    alias.Code,alias.DisplayName,alias.ClassName,alias.Description,key,alias.Code=="DH_ShadowPower_ElusiveRing"?"ring":"resource");
            }
            BuildMacroCatalog();
            var mantra=_conditionalProfiles.FirstOrDefault(p=>p.Code=="Monk_MantraHealing_Defensive");
            if(mantra!=null){mantra.CastDelayMinMs=mantra.CastDelayMaxMs=100;mantra.RecheckMs=25;}
            var potions=_conditionalProfiles.Where(p=>p.Sno==Hud.Sno.SnoPowers.Generic_DrinkHealthPotion.Sno).ToList();
            foreach(var p in potions)_conditionalProfiles.Remove(p);
            _conditionalProfiles.InsertRange(0,potions.OrderByDescending(p=>p.Code.IndexOf("Archon",StringComparison.Ordinal)>=0));
        }
        private void BuildMacroCatalog()
        {
            _macroCatalog.Clear();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var binding in _importedBindings)
            {
                if(!seen.Add(binding.Code))continue;
                bool isBuff=_buffProfiles.Any(x=>x.Code==binding.Code);
                string title=binding.Code==NayrsBlackDeathProfileCode?"Nayr's Black Death":binding.Title;
                _macroCatalog.Add(new MacroDescriptor{Code=binding.Code,Title=title,Description=binding.Description,DescriptionKey=binding.DescriptionKey,TitleSuffix=binding.TitleSuffix,PowerSno=binding.Code==NayrsBlackDeathProfileCode?Hud.Sno.SnoPowers.NayrsBlackDeath.Sno:binding.Power.Sno,ClassName=binding.ClassName,IsBuff=isBuff});
            }
            foreach(var p in _conditionalProfiles)
                if(seen.Add(p.Code))
                {
                    string descriptionKey=p.Code=="Monk_MantraHealing_Defensive"?"macro.policy.mantra":p.Code=="DH_ShadowPower_ElusiveRing"?"macro.policy.ring":"macro.policy.resource";
                    string suffix=p.Code=="DH_ShadowPower_ElusiveRing"?"ring":p.Code=="DH_Companion_EliteOrBoss"?"":"resource";
                    if(p.Code=="Monk_MantraHealing_Defensive")suffix="";
                    _macroCatalog.Add(new MacroDescriptor{Code=p.Code,Title=p.DisplayName,Description=p.Description,DescriptionKey=descriptionKey,TitleSuffix=suffix,PowerSno=p.Sno,ClassName=p.ClassName});
                }
        }
        private bool EvaluateImported(ImportedBinding binding,IPlayerSkill skill,int now)
        {
            _importedEvaluating=null;
            if(binding==null||skill==null||skill.SnoPower==null)return false;
            if(binding.Code=="DH_Companion_Bat_Hatred"&&skill.Rune!=3)return false;
            if(binding.Code=="DH_Companion_EliteOrBoss"&&skill.Rune==3)return false;
            if(binding.Code=="DH_Vengeance_HatredBurst"&&(skill.Rune!=4||!PrimaryResourceBelow(30)))return false;
            if(binding.Code=="DH_ShadowPower_ElusiveRing")
            {
                var ring=Hud.Game.Me.Powers.UsedLegendaryPowers.ElusiveRing;
                if(ring==null||!ring.Active||S7oMacroQueries.CountNear(Hud,Hud.Game.Me,40)==0)return false;
                var buff=Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.ElusiveRing.Sno);
                if(buff!=null&&buff.Active&&S7oMacroData.Seconds(buff.TimeLeftSeconds,1)>0.1)return false;
            }
            var shadow=binding.Policy as S7oPolicy_DemonHunterShadowPower;
            if(shadow!=null)shadow.ElusiveRingEnabled=GetConditionalProfileEnabled("DH_ShadowPower_ElusiveRing");
            if(binding.Policy is S7oPolicy_DrinkHealthPotion && SpecializedPotionOwnsWindow())
            {
                var potion=(S7oPolicy_DrinkHealthPotion)binding.Policy;
                double threshold=Hud.Game.Me.HeroIsHardcore?potion.HC:potion.SDT;
                if(potion.InDangerDrink&&S7oMacroQueries.FrozenNear(Hud,Hud.Game.Me))threshold=potion.InDanger;
                if(Hud.Game.Me.Defense.HealthPct>threshold)return false;
            }
            if(ImportedOwnedByCompanion(binding,skill))return false;
            if(!binding.Loaded)
            {binding.Policy.Load(Hud);binding.Loaded=true;}
            IWatch skillWatch;
            if(!_importedSkillWatches.TryGetValue(skill.SnoPower.Sno,out skillWatch))
            {_importedSkillWatches[skill.SnoPower.Sno]=skillWatch=Hud.Time.CreateWatch();}
            var ctx=_importedContext;ctx.Hud=Hud;ctx.Skill=skill;
            ctx.LastSimpleCasted=_importedSimpleWatch;ctx.LastBuffCasted=_importedBuffWatch;ctx.LastSkillCasted=skillWatch;
            var state=Hud.Game.Me.AnimationState;
            ctx.Phase=state==AcdAnimationState.Running?S7oCastPhase.Move:
                state==AcdAnimationState.Attacking||state==AcdAnimationState.Channeling?S7oCastPhase.Attack:S7oCastPhase.AutoCast;
            if(!binding.Policy.SupportedPhases.Contains(ctx.Phase))
            {
                if(ctx.Phase==S7oCastPhase.Attack&&binding.Policy.SupportedPhases.Contains(S7oCastPhase.PreAttack))ctx.Phase=S7oCastPhase.PreAttack;
                else if(binding.Policy.SupportedPhases.Contains(S7oCastPhase.AutoCast))ctx.Phase=S7oCastPhase.AutoCast;
                else if(binding.Policy.SupportedPhases.Contains(S7oCastPhase.Collect))ctx.Phase=S7oCastPhase.Collect;
                else return false;
            }
            if(binding.Policy.Evaluate(ctx)){_importedEvaluating=binding;return true;}
            return false;
        }
        private bool ImportedPotionEnabled()
        {return EnableConditionalAutoCastProfiles&&_conditionalProfiles.Any(p=>p.Enabled&&p.Sno==Hud.Sno.SnoPowers.Generic_DrinkHealthPotion.Sno);}
        private bool SpecializedPotionOwnsWindow()
        {
            return GetConditionalProfileEnabled("Skill_DrinkHealthPotionArchonMeteorBuild")
                &&Hud.Game.Me.HeroClassDefinition.HeroClass==HeroClass.Wizard
                &&Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheSwami.Sno)
                &&Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.NilfursBoast.Sno)
                &&Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FazulasImprobableChain.Sno);
        }
        private bool ImportedOwnedByCompanion(ImportedBinding binding,IPlayerSkill skill)
        {
            uint sno=skill.SnoPower.Sno;
            if(sno==Hud.Sno.SnoPowers.Necromancer_SiphonBlood.Sno)
            {var pesti=Hud.GetPlugin<s7o_Pestilence_RGK>();if(pesti!=null&&pesti.Enabled&&pesti.AutoSiphonEnabled)return true;}
            // Seasonal controllers keep their existing skill/input ownership.
            var monk=Hud.GetPlugin<s7o_GenMonk>();
            if(monk!=null&&monk.Enabled&&Hud.Game.Me.HeroClassDefinition.HeroClass==HeroClass.Monk
                &&(!monk.RequireRaimentSet||Hud.Game.Me.GetSetItemCount(755275u)>=6))
                if(sno==Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno||sno==Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno
                    ||sno==Hud.Sno.SnoPowers.Monk_FistsOfThunder.Sno||sno==Hud.Sno.SnoPowers.Monk_CripplingWave.Sno
                    ||sno==Hud.Sno.SnoPowers.Monk_DashingStrike.Sno)return true;
            if(s7o_DHStrafePrimaryPlugin.IsMacroRunningForZdh)
                if(sno==Hud.Sno.SnoPowers.DemonHunter_HungeringArrow.Sno||sno==Hud.Sno.SnoPowers.DemonHunter_EntanglingShot.Sno
                    ||sno==Hud.Sno.SnoPowers.DemonHunter_Bolas.Sno||sno==Hud.Sno.SnoPowers.DemonHunter_EvasiveFire.Sno)return true;
            // Impale/Multishot's active input is protected by the shared arbiter.
            var barb=Hud.GetPlugin<s7o_ZB_AutoSnap>();
            if(barb!=null&&barb.Enabled&&sno==Hud.Sno.SnoPowers.Barbarian_AncientSpear.Sno)return true;
            return false;
        }
        private void ImportedCastAccepted(IPlayerSkill skill,string code)
        {
            if(skill==null||skill.SnoPower==null)return;
            IWatch watch;if(_importedSkillWatches.TryGetValue(skill.SnoPower.Sno,out watch))watch.Restart();
            if(_importedEvaluating!=null&&_importedEvaluating.Policy.SkillType==S7oCastType.BuffSkill)_importedBuffWatch.Restart();
            else _importedSimpleWatch.Restart();
        }
        private bool ApplyImportedRequests(IPlayerSkill skill)
        {
            if(_importedEvaluating!=null&&(_importedEvaluating.Policy.SkillType!=S7oCastType.BuffSkill
                ||S7oMacroQueries.HasAim)&&s7o_InputReleaseArbiter.HasOtherOwner("AutoSkill"))return false;
            if(S7oMacroQueries.Prerequisite!=ActionKey.Unknown)
            {
                var slot=_skillSlots.FirstOrDefault(candidate=>candidate.ActionKey==S7oMacroQueries.Prerequisite);
                if(slot!=null&&slot.Skill!=null)
                {
                    int now=Environment.TickCount,last;
                    if(!_lastCastTickByPower.TryGetValue(slot.Skill.SnoPower.Sno,out last)||unchecked(now-last)>=250)
                    {
                        string reason;
                        if(CanCastSkill(slot.Skill,slot.ActionKey,now,100,out reason)&&DoAction(slot.ActionKey,true))
                            MarkSkillCast(slot.Skill,now,slot.ActionKey);
                        return false;
                    }
                }
            }
            if(!S7oMacroQueries.HasAim)return true;
            if(s7o_InputReleaseArbiter.HasOtherOwner("AutoSkill"))return false;
            float x=S7oMacroQueries.AimX,y=S7oMacroQueries.AimY;
            if(!IsCommandSkeletonsAimSafe(x,y))return false;
            CursorPoint cursor;if(!GetCursorPos(out cursor))return false;
            _importedRestoreX=cursor.X;_importedRestoreY=cursor.Y;
            _importedRestore=MoveCommandSkeletonsCursorToClientPoint((int)Math.Round(x),(int)Math.Round(y));
            return _importedRestore;
        }
        private void RestoreImportedAim()
        {
            if(_importedRestore){SetCursorPos(_importedRestoreX,_importedRestoreY);_importedRestore=false;}
            S7oMacroQueries.ClearRequests();
        }
        public bool CanYieldForAutoLootPickup { get { return !_autoCastPulse.Active; } }
        public void PauseForAutoLootPickup()
        {StopAutoCastPulse();StopImportedChannel();_importedLootPauseTick=Hud!=null&&Hud.Game!=null?Hud.Game.CurrentGameTick:0;}
        public void StopForAutoLootUrshiHandoff(){StopMacroInputs();_importedUrshiQuiet=true;}
        public void StopMacroInputs(){StopAutoCastPulse();StopImportedChannel();RestoreImportedAim();}
        private void StopImportedChannel()
        {
            ActionKey key=_importedChannelKey;
            _importedChannel=null;_importedChannelKey=ActionKey.Unknown;
            if(key==ActionKey.LeftSkill)SendMouse(LeftUp);
            else if(key==ActionKey.RightSkill)SendMouse(RightUp);
            else if(key!=ActionKey.Unknown)SendKeyUp(GetCastVirtualKey(key));
            ushort shift=_importedChannelStandstill;_importedChannelStandstill=0;
            if(shift!=0)SendKeyUp(shift);
        }
        private void UpdateImportedChannel(int now)
        {
            if(_importedChannel==null)return;
            string reason;
            if(!EnableConditionalAutoCastProfiles||!GetConditionalProfileEnabled(_importedChannel.Code)
                ||!IsValidAutomationContext(false,out reason)) {StopImportedChannel();return;}
            var slot=FindEquippedSkillBySno(_importedChannel.Power.Sno);
            try {if(slot==null||slot.ActionKey!=_importedChannelKey||!EvaluateImported(_importedChannel,slot.Skill,now))StopImportedChannel();}
            catch {StopImportedChannel();}
            S7oMacroQueries.ClearRequests();
        }
        private void RefreshImportedOverride()
        {
            bool active=Hud!=null&&Hud.Game!=null&&Hud.Game.Me!=null&&Hud.Game.Me.Powers!=null&&Hud.Game.Me.Powers.SkillOverrideActive;
            if(active!=_importedOverrideActive)
            {_importedOverrideActive=active;RefreshSkillCache(true);_nextSkillCacheRefreshTick=0;}
        }
        private void ResetImportedPolicies()
        {foreach(var b in _importedBindings){b.Loaded=false;b.Policy.CastRules.Clear();}_importedSkillWatches.Clear();}
        private bool ExecuteImportedAction(ActionKey key,bool standstill)
        {
            if(_importedEvaluating==null||_importedEvaluating.Policy.SkillType!=S7oCastType.RangedChannelingSkill)
                return DoAction(key,standstill);
            if(_importedChannel!=null&&_importedChannelKey==key)return false;
            if(MacroActionHeld(key)||s7o_InputReleaseArbiter.HasOtherOwner("AutoSkill"))return false;
            bool shift=standstill&&ForceStandstillVirtualKey!=0&&!IsKeyPhysicallyDown(ForceStandstillVirtualKey);
            try
            {
                if(shift&&!SendKeyDown(ForceStandstillVirtualKey))return false;
                bool ok=key==ActionKey.LeftSkill?SendMouse(LeftDown):key==ActionKey.RightSkill?SendMouse(RightDown):SendKeyDown(GetCastVirtualKey(key));
                if(ok){_importedChannel=_importedEvaluating;_importedChannelKey=key;if(shift)_importedChannelStandstill=ForceStandstillVirtualKey;}return ok;
            }
            finally {if(shift&&_importedChannelStandstill==0)SendKeyUp(ForceStandstillVirtualKey);}
        }
        private bool ImportedBuffDue(IPlayerSkill skill,AutoBuffProfile profile,int now,out bool missing,out double remaining)
        {
            missing=!skill.BuffIsActive;remaining=skill.RemainingBuffTime();
            ImportedBinding binding;
            return _importedBuffBindings.TryGetValue(profile.Sno,out binding)&&EvaluateImported(binding,skill,now);
        }
        #endregion

        #region Overlay UI

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip)
                return;

            if (!Enabled || Hud == null || Hud.Window == null || IsSkillPaneVisible())
                return;

            DrawSlotToggleMarkers();
        }

        private void DrawSlotToggleMarkers()
        {
            int now = Environment.TickCount;

            for (int i = 0; i < _slotToggleRects.Length; i++)
            {
                var rect = _slotToggleRects[i];
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                bool enabled = IsSlotAutomationEnabled(i);
                bool flash = IsTickInFuture(_slotFlashUntilTick[i], now);
                bool hoverPending = _hoverSlotIndex == i && _hoverCompletedSlot != i;

                IBrush brush = enabled ? _dotEnabledBrush : _dotDisabledBrush;
                IBrush highlight = enabled ? _dotEnabledHighlightBrush : _dotDisabledHighlightBrush;

                if (hoverPending)
                {
                    brush = _dotHoverBrush;
                    highlight = _dotHoverHighlightBrush;
                }

                if (flash)
                {
                    brush = _dotFlashBrush;
                    highlight = _dotHoverHighlightBrush;
                }

                DrawToggleDot(rect, brush, highlight);
            }
        }

        private void DrawToggleDot(RectangleF rect, IBrush baseBrush, IBrush highlightBrush)
        {
            if (baseBrush == null)
                return;

            float radius = Math.Min(rect.Width, rect.Height) * 0.5f;
            float cx = rect.X + rect.Width * 0.5f;
            float cy = rect.Y + rect.Height * 0.5f;

            if (_dotShadowBrush != null)
                _dotShadowBrush.DrawEllipse(cx + 1.0f, cy + 1.0f, radius, radius);

            baseBrush.DrawEllipse(cx, cy, radius, radius);

            if (highlightBrush != null)
            {
                float hr = radius * 0.42f;
                highlightBrush.DrawEllipse(cx - radius * 0.28f, cy - radius * 0.28f, hr, hr);
            }

            if (_dotBorderBrush != null)
                _dotBorderBrush.DrawEllipse(cx, cy, radius, radius);
        }

        private RectangleF BuildToggleRect(RectangleF uiRect)
        {
            if (uiRect.Width <= 0 || uiRect.Height <= 0)
                return RectangleF.Empty;

            float size = Math.Max(ToggleDotMinSize, Math.Min(ToggleDotMaxSize, uiRect.Width * ToggleDotScale));

            return new RectangleF(
                uiRect.Right - size - 2.0f,
                uiRect.Top + 2.0f,
                size,
                size);
        }

        private void ClearSlotRects()
        {
            for (int i = 0; i < _slotToggleRects.Length; i++)
                _slotToggleRects[i] = RectangleF.Empty;
        }

        #endregion

        #region Mouse Handling

        private void ToggleSlotAutomation(int slotIndex, string source)
        {
            if (slotIndex < 0 || slotIndex >= _enabledSlots.Length)
                return;

            _enabledSlots[slotIndex] = !_enabledSlots[slotIndex];
            _slotFlashUntilTick[slotIndex] = Environment.TickCount + Math.Max(0, ToggleFlashMs);

            var slot = FindSlotState(slotIndex);
            if (slot != null)
                slot.IsManualEnabled = _enabledSlots[slotIndex];

            SaveUserSettings();

            LogDebug("Manual slot toggle: slot="
                + slotIndex.ToString(CultureInfo.InvariantCulture)
                + ", enabled="
                + _enabledSlots[slotIndex].ToString(CultureInfo.InvariantCulture)
                + ", source="
                + source);
        }

        public bool MouseDown(MouseButtons button)
        {
            if (!Enabled || button != MouseButtons.Left)
                return false;

            if (!EnableClickToggle)
                return false;

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground || IsSkillPaneVisible())
                return false;

            int x = Hud.Window.CursorX;
            int y = Hud.Window.CursorY;

            for (int i = 0; i < _slotToggleRects.Length; i++)
            {
                if (!PointInRect(_slotToggleRects[i], x, y))
                    continue;

                ToggleSlotAutomation(i, "click");
                _hoverSlotIndex = i;
                _hoverStartTick = Environment.TickCount;
                _hoverCompletedSlot = i;
                return true;
            }

            return false;
        }

        public bool MouseUp(MouseButtons button)
        {
            return false;
        }

        #endregion

        #region Persistence

        private void InitializePluginPaths()
        {
            string relativePluginDir = Path.Combine("plugins", "s7o");
            string relativeSettingsDir = Path.Combine("plugins", "s7o", "settings");

            string settingsFile = "s7o_AutoSkill.ini";
            string legacySettingsFile = "s7o_AutoSkill.settings.ini";
            string debugFile = "s7o_AutoSkill_REV09_PRODUCTION_"
                +DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".txt";

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string pluginDir = Path.Combine(baseDir, relativePluginDir);
                    string settingsDir = Path.Combine(baseDir, relativeSettingsDir);

                    Directory.CreateDirectory(pluginDir);
                    Directory.CreateDirectory(settingsDir);

                    _settingsPath = Path.Combine(settingsDir, settingsFile);
                    _legacySettingsPath = Path.Combine(pluginDir, legacySettingsFile);

                    string logDir=Path.Combine(baseDir,"logs");
                    Directory.CreateDirectory(logDir);
                    _debugLogPath = Path.Combine(logDir, debugFile);
                    return;
                }
            }
            catch
            {
            }

            _settingsPath = Path.Combine(relativeSettingsDir, settingsFile);
            _legacySettingsPath = Path.Combine(relativePluginDir, legacySettingsFile);

            Directory.CreateDirectory("logs");
            _debugLogPath = Path.Combine("logs", debugFile);

            try
            {
                Directory.CreateDirectory(relativeSettingsDir);
            }
            catch
            {
            }
        }


        private string SelectUserSettingsReadPath()
        {
            try
            {
                if (!string.IsNullOrEmpty(_settingsPath) && File.Exists(_settingsPath))
                    return _settingsPath;
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(_legacySettingsPath) && File.Exists(_legacySettingsPath))
                    return _legacySettingsPath;
            }
            catch { }

            return _settingsPath;
        }

        private void LoadUserSettings()
        {
            if (string.IsNullOrEmpty(_settingsPath))
                return;

            string readPath = SelectUserSettingsReadPath();

            if (string.IsNullOrEmpty(readPath) || !File.Exists(readPath))
                return;

            try
            {
                var lines = File.ReadAllLines(readPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    line = line.Trim();
                    if (line.StartsWith("#") || line.StartsWith(";"))
                        continue;

                    int sep = line.IndexOf('=');
                    if (sep <= 0)
                        continue;

                    string key = line.Substring(0, sep).Trim();
                    string value = line.Substring(sep + 1).Trim();

                    TryApplySetting(key, value);
                }

                LogDebug("Loaded settings from " + readPath);

                try
                {
                    if (!string.Equals(
                        Path.GetFullPath(readPath).TrimEnd('\\', '/'),
                        Path.GetFullPath(_settingsPath).TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        SaveUserSettings();
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                LogDebug("Load settings failed: " + ex.Message);
            }
        }

        private void SaveUserSettings()
        {
            if (!PersistUserSettings || string.IsNullOrEmpty(_settingsPath))
                return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# s7o_AutoSkill settings");
                sb.AppendLine("EnableManualSlotAutoCast=" + EnableManualSlotAutoCast.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("EnableAutomaticBuffUpkeep=" + EnableAutomaticBuffUpkeep.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("EnableConditionalAutoCastProfiles=" + EnableConditionalAutoCastProfiles.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("EnableHoverToggle=" + EnableHoverToggle.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("AllowBuffUpkeepInTown=" + AllowBuffUpkeepInTown.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("BlockLeftSkillOnSelectedClickableActor=" + BlockLeftSkillOnSelectedClickableActor.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("CommandSkeletonsMoveCursorToTarget=" + CommandSkeletonsMoveCursorToTarget.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("DebugLogging=" + DebugLogging.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Skill1VirtualKey=" + Skill1VirtualKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Skill2VirtualKey=" + Skill2VirtualKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Skill3VirtualKey=" + Skill3VirtualKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Skill4VirtualKey=" + Skill4VirtualKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("HealVirtualKey=" + HealVirtualKey.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _enabledSlots.Length; i++)
                    sb.AppendLine("Slot" + i.ToString(CultureInfo.InvariantCulture) + "=" + _enabledSlots[i].ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _buffProfiles.Count; i++)
                {
                    var profile = _buffProfiles[i];
                    if (profile == null || string.IsNullOrEmpty(profile.Code))
                        continue;

                    sb.AppendLine("BuffProfile_" + profile.Code + "=" + profile.Enabled.ToString(CultureInfo.InvariantCulture));
                }

                var savedConditionalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _conditionalProfiles.Count; i++)
                {
                    var profile = _conditionalProfiles[i];
                    if (profile == null || string.IsNullOrEmpty(profile.Code) || savedConditionalCodes.Contains(profile.Code))
                        continue;

                    savedConditionalCodes.Add(profile.Code);
                    sb.AppendLine("ConditionalProfile_" + profile.Code + "=" + profile.Enabled.ToString(CultureInfo.InvariantCulture));
                }

                string dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_settingsPath, sb.ToString());
            }
            catch (Exception ex)
            {
                LogDebug("Save settings failed: " + ex.Message);
            }
        }

        private void TryApplySetting(string key, string value)
        {
            if (TryApplyVirtualKeySetting(key, value))
                return;

            TryApplyBoolSetting(key, value);
        }

        private bool TryApplyVirtualKeySetting(string key, string value)
        {
            ushort parsed;
            if (!TryParseVirtualKey(value, out parsed))
                return false;

            if (StringEquals(key, "Skill1VirtualKey"))
            {
                Skill1VirtualKey = parsed;
                return true;
            }

            if (StringEquals(key, "Skill2VirtualKey"))
            {
                Skill2VirtualKey = parsed;
                return true;
            }

            if (StringEquals(key, "Skill3VirtualKey"))
            {
                Skill3VirtualKey = parsed;
                return true;
            }

            if (StringEquals(key, "Skill4VirtualKey"))
            {
                Skill4VirtualKey = parsed;
                return true;
            }

            if (StringEquals(key, "HealVirtualKey"))
            {
                HealVirtualKey = parsed;
                return true;
            }

            return false;
        }

        private bool TryParseVirtualKey(string value, out ushort result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            int parsed;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                    return false;
            }
            else
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    return false;
            }

            if (parsed < 0 || parsed > ushort.MaxValue)
                return false;

            result = (ushort)parsed;
            return true;
        }

        private void TryApplyBoolSetting(string key, string value)
        {
            bool parsed;
            if (!TryParseBool(value, out parsed))
                return;

            if (StringEquals(key, "EnableManualSlotAutoCast"))
            {
                EnableManualSlotAutoCast = parsed;
                return;
            }

            if (StringEquals(key, "EnableAutomaticBuffUpkeep"))
            {
                EnableAutomaticBuffUpkeep = parsed;
                return;
            }

            if (StringEquals(key, "EnableConditionalAutoCastProfiles"))
            {
                EnableConditionalAutoCastProfiles = parsed;
                return;
            }

            if (StringEquals(key, "EnableHoverToggle"))
            {
                EnableHoverToggle = parsed;
                return;
            }


            if (StringEquals(key, "AllowBuffUpkeepInTown"))
            {
                AllowBuffUpkeepInTown = parsed;
                return;
            }

            if (StringEquals(key, "BlockLeftSkillOnSelectedClickableActor"))
            {
                BlockLeftSkillOnSelectedClickableActor = parsed;
                return;
            }

            if (StringEquals(key, "CommandSkeletonsMoveCursorToTarget"))
            {
                CommandSkeletonsMoveCursorToTarget = parsed;
                return;
            }

            if (StringEquals(key, "DebugLogging"))
            {
                DebugLogging = parsed;
                return;
            }

            if (key.StartsWith("BuffProfile_", StringComparison.OrdinalIgnoreCase))
            {
                string code = key.Substring("BuffProfile_".Length);

                for (int i = 0; i < _buffProfiles.Count; i++)
                {
                    var profile = _buffProfiles[i];
                    if (profile != null && StringEquals(profile.Code, code))
                    {
                        profile.Enabled = parsed;
                        return;
                    }
                }
            }

            if (key.StartsWith("ConditionalProfile_", StringComparison.OrdinalIgnoreCase))
            {
                string code = key.Substring("ConditionalProfile_".Length);
                bool found = false;

                for (int i = 0; i < _conditionalProfiles.Count; i++)
                {
                    var profile = _conditionalProfiles[i];
                    if (profile == null || !StringEquals(profile.Code, code))
                        continue;

                    profile.Enabled = parsed;
                    found = true;
                }

                if (found)
                    return;
            }

            if (key.StartsWith("Slot", StringComparison.OrdinalIgnoreCase))
            {
                int slot;
                if (int.TryParse(key.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                {
                    if (slot >= 0 && slot < _enabledSlots.Length)
                        _enabledSlots[slot] = parsed;
                }
            }
        }

        private bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            return bool.TryParse(value, out result);
        }

        #endregion

        #region Logging

        private bool IsDiagnosticFocusSkill(IPlayerSkill skill)
        {
            return skill!=null&&skill.SnoPower!=null&&Hud!=null&&Hud.Sno!=null
                &&(skill.SnoPower.Sno==Hud.Sno.SnoPowers.Monk_MysticAlly.Sno
                    ||skill.SnoPower.Sno==Hud.Sno.SnoPowers.Monk_Epiphany.Sno);
        }

        private void ObserveDiagnosticCastState(int now)
        {
            if(!DebugLogActive||Hud==null||Hud.Game==null||!Hud.Game.IsInGame||Hud.Game.Me==null)return;
            try
            {
                int held=0;
                foreach(var slot in _skillSlots)
                    if(slot!=null&&slot.SlotIndex>=0&&slot.SlotIndex<7&&MacroActionHeld(slot.ActionKey))held|=1<<slot.SlotIndex;
                bool heldChanged=held!=_lastDiagnosticHeldMask;
                if(!heldChanged&&!IsTickReached(now,_nextDiagnosticStateTick))return;
                bool responseBurst=_diagnosticResponseBurstUntilTick!=0&&!IsTickReached(now,_diagnosticResponseBurstUntilTick);
                _lastDiagnosticHeldMask=held;_nextDiagnosticStateTick=unchecked(now+(responseBurst?50:1000));
                var me=Hud.Game.Me;
                var selected=Hud.Game.SelectedActor;
                LogDebug("CAST_STATE heldMask="+held+", heldChanged="+heldChanged
                    +", pulseActive="+_autoCastPulse.Active+", pulseAction="+_autoCastPulseAction
                    +", animation="+me.Animation+", animationSno="+(int)me.Animation+", state="+me.AnimationState
                    +", spirit="+(me.Stats==null?"unknown":me.Stats.ResourceCurPri.ToString("0.0",CultureInfo.InvariantCulture))
                    +", maxResource="+(me.Stats==null?"unknown":me.Stats.ResourceMaxPri.ToString("0.0",CultureInfo.InvariantCulture))
                    +", otherOwner="+s7o_InputReleaseArbiter.HasOtherOwner("AutoSkill")
                    +", pendingRelease="+s7o_InputReleaseArbiter.HasPendingRelease
                    +", selectedSno="+(selected==null||selected.SnoActor==null?0u:(uint)selected.SnoActor.Sno)
                    +", selectedClickable="+(selected!=null&&selected.IsClickable));
                foreach(var slot in _skillSlots)
                {
                    if(slot==null||slot.Skill==null||slot.Skill.SnoPower==null)continue;
                    var skill=slot.Skill;
                    if(!IsDiagnosticFocusSkill(skill))continue;
                    LogDebug("SKILL_STATE slot="+slot.SlotIndex+", key="+slot.ActionKey+", skill="+SafeSkillCode(skill)
                        +", sno="+skill.SnoPower.Sno+", rune="+skill.Rune+", cooldown="+skill.IsOnCooldown
                        +", cooldownFinishTick="+skill.CooldownFinishTick+", buff="+skill.BuffIsActive
                        +", buffRemaining="+skill.RemainingBuffTime().ToString("0.000",CultureInfo.InvariantCulture)
                        +", resourceRequirement="+skill.GetResourceRequirement().ToString("0.0",CultureInfo.InvariantCulture)
                        +", targetKeyHeld="+MacroActionHeld(slot.ActionKey));
                }
            }
            catch(Exception ex){LogDebug("CAST_STATE_ERROR type="+ex.GetType().Name);}
        }

        private void LogDebug(string message)
        {
            if (!DebugLogActive)
                return;

            try
            {
                if (string.IsNullOrEmpty(_debugLogPath))
                    return;

                RotateDebugLogIfNeeded();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    +" tick="+(Hud!=null&&Hud.Game!=null?Hud.Game.CurrentGameTick:-1)+" | "+message+Environment.NewLine;
                File.AppendAllText(_debugLogPath, line);
            }
            catch
            {
            }
        }

        private void RotateDebugLogIfNeeded()
        {
            try
            {
                int maxBytes = Math.Max(64 * 1024, MaxDebugLogBytes);
                if (File.Exists(_debugLogPath) && new FileInfo(_debugLogPath).Length > maxBytes)
                    File.WriteAllText(_debugLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | log truncated" + Environment.NewLine);
            }
            catch
            {
            }
        }

        private void LogContextBlockedThrottled(string reason, int now)
        {
            if (!DebugLogActive)
                return;

            if (string.IsNullOrEmpty(reason))
                reason = "unknown";

            // Each category/reason has its own throttle; buff/conditional/manual must not reset each other.
            string key = "context:" + reason;
            int next;
            if (_nextSkipLogTickByKey.TryGetValue(key, out next) && !IsTickReached(now, next)) return;
            _nextSkipLogTickByKey[key] = unchecked(now + 3000);
            LogDebug("Context blocked: " + reason);
        }

        private void LogDebugCastSkipThrottled(string category, IPlayerSkill skill, string reason, int now)
        {
            if (!DebugLogActive)
                return;

            if (string.IsNullOrEmpty(reason))
                reason = "unknown";

            string key = category + ":" + SafeSkillCode(skill) + ":" + reason;
            int next;
            if (_nextSkipLogTickByKey.TryGetValue(key, out next) && !IsTickReached(now, next))
                return;

            _nextSkipLogTickByKey[key] = now + 2000;
            LogDebug("Cast skipped: category=" + category + ", skill=" + SafeSkillCode(skill) + ", reason=" + reason);
        }

        #endregion

        #region Utility

        private int GetRandomizedDelay(string key, int min, int max, int changeAfterMs)
        {
            min = Math.Max(0, min);
            max = Math.Max(min, max);
            changeAfterMs = Math.Max(1, changeAfterMs);

            int now = Environment.TickCount;

            RandomNode node;
            if (!_randomNodes.TryGetValue(key, out node))
            {
                node = new RandomNode();
                _randomNodes[key] = node;
            }

            if (node.Value <= 0 || (uint)(now - node.LastChangedTick) >= (uint)changeAfterMs)
            {
                node.Value = min == max ? min : _random.Next(min, max + 1);
                node.LastChangedTick = now;
            }

            return node.Value;
        }

        private IUiElement RegisterOrGetUiElement(string path)
        {
            if (string.IsNullOrEmpty(path) || Hud == null || Hud.Render == null)
                return null;

            try
            {
                return Hud.Render.RegisterUiElement(path, null, null);
            }
            catch
            {
                try
                {
                    return Hud.Render.GetUiElement(path);
                }
                catch
                {
                    return null;
                }
            }
        }

        private uint GetSno(ISnoPower power, uint fallback)
        {
            return power != null && power.Sno != 0 ? power.Sno : fallback;
        }

        private bool PointInRect(RectangleF rect, int x, int y)
        {
            return rect.Width > 0 && rect.Height > 0 && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        private bool IsTickReached(int now, int targetTick)
        {
            return targetTick == 0 || targetTick == int.MinValue || unchecked(now - targetTick) >= 0;
        }

        private bool IsTickInFuture(int targetTick, int now)
        {
            return targetTick != 0 && targetTick != int.MinValue && unchecked(now - targetTick) < 0;
        }

        private bool StringEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private string SafeSkillCode(IPlayerSkill skill)
        {
            try
            {
                if (skill == null || skill.SnoPower == null)
                    return "null";

                if (!string.IsNullOrEmpty(skill.SnoPower.Code))
                    return skill.SnoPower.Code;

                return skill.SnoPower.Sno.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "unknown";
            }
        }

        #endregion

        #region Win32

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
            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public KeyboardInput Keyboard;
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

        // Returns true when the key is physically held down at the hardware level,
        // regardless of any synthetic key events the plugin may have injected.
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        private static bool IsKeyPhysicallyDown(ushort virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        private static bool IsWindowsKeyDown()
        {
            return IsKeyPhysicallyDown(0x5B) || IsKeyPhysicallyDown(0x5C);
        }

        #endregion
    }

    // Shared synthetic-input safety for s7o automation plugins. No polling hooks or logging.
    // Ownership survives a caller resetting its own cast/macro state after a failed UP.
    // Keep this utility independent of the AutoSkill plugin instance so other plugins can reuse it.
    internal static class s7o_InputReleaseArbiter
    {
        private sealed class Held
        {
            public string Owner;
            public Func<bool> Retry;
        }
        private static readonly Dictionary<int, Held> HeldInputs = new Dictionary<int, Held>();
        private static int _lastRetryTick = int.MinValue;
        public static bool HasPendingRelease
        {
            get { foreach (var entry in HeldInputs.Values) if (entry.Retry != null) return true; return false; }
        }
        public static bool HasOtherOwner(string owner)
        { foreach(var held in HeldInputs.Values) if(held.Owner!=owner)return true;return false; }
        public static bool Down(string owner, int code, Func<bool> send)
        {
            if (HasPendingRelease) return false;
            Held held;
            if (HeldInputs.TryGetValue(code, out held) && held.Owner != owner) return false;
            if (!send()) return false;
            if (held == null) HeldInputs[code] = new Held { Owner = owner };
            return true;
        }
        public static bool Up(string owner, int code, Func<bool> send)
        {
            Held held;
            // A rejected DOWN must never be followed by an UP of another producer's input.
            if (!HeldInputs.TryGetValue(code, out held)) return true;
            if (held.Owner != owner) return false;
            held.Retry = send;
            if (!send()) return false;
            HeldInputs.Remove(code);
            return true;
        }
        public static void RetryPending(int now)
        {
            if (!HasPendingRelease || (_lastRetryTick != int.MinValue && unchecked(now - _lastRetryTick) < 50)) return;
            _lastRetryTick = now;
            var codes = new List<int>(HeldInputs.Keys);
            foreach (int code in codes)
            {
                Held held = HeldInputs[code];
                if (held.Retry != null && held.Retry()) HeldInputs.Remove(code);
            }
        }
    }

}

namespace Turbo.Plugins.s7o
{
    internal enum S7oTestResult { Continue, NoCast, Cast }
    internal enum S7oCombatRole { Default, Support }
    internal enum S7oCastType { SimpleSkill, BuffSkill, RangedChannelingSkill }
    internal enum S7oCastPhase { AutoCast, Collect, Move, PreAttack, Attack, AttackIdle, UseTpStart, UseWpStart }
    internal sealed class S7oTestContext
    {
        public IController Hud;
        public IPlayerSkill Skill;
        public S7oCombatRole Role;
        public S7oCastPhase Phase;
        public IWatch LastSimpleCasted, LastBuffCasted, LastSkillCasted;
    }
    internal abstract class S7oImportedPolicy
    {
        internal s7o_AutoSkill Owner;
        internal IController Hud;
        public bool Enabled;
        public ISnoPower AssignedSnoPower;
        public int? Rune;
        public S7oCastType SkillType;
        public S7oCombatRole CombatRole;
        public readonly List<S7oAbstractTest> CastRules = new List<S7oAbstractTest>();
        public readonly HashSet<S7oCastPhase> SupportedPhases;
        protected S7oImportedPolicy(S7oCastType type, params S7oCastPhase[] phases)
        { SkillType=type;SupportedPhases=new HashSet<S7oCastPhase>(phases); }
        public virtual void Load(IController hud) { Hud=hud; }
        public S7oAbstractTest CreateCastRule()
        {
            var node=new S7oCustomTrueTest { TestFunc=ctx=>true };
            node.ThenContinueElseNoCast(); CastRules.Add(node); return node;
        }
        internal bool Evaluate(S7oTestContext ctx)
        {
            if(Rune.HasValue && ctx.Skill.Rune!=Rune.Value)return false;
            ctx.Role=CombatRole;
            foreach(var rule in CastRules)if(rule.Evaluate(ctx)==S7oTestResult.Cast)return true;
            return false;
        }
        public static int ChangeRnd(IController hud,string key,int min,int max,int changeAfter)
        { return S7oMacroQueries.Owner.MacroDelay(key,min,max,changeAfter); }
    }
    internal static class S7oMacroQueries
    {
        internal static s7o_AutoSkill Owner;
        private static IController _hud;
        private static int _tick=int.MinValue;
        private static uint _world;
        private static readonly List<IMonster> _monsters=new List<IMonster>(512);
        private static readonly List<IActor> _actors=new List<IActor>(1024);
        private static bool _haveMonsters,_haveActors;
        internal static bool HasAim;
        internal static float AimX,AimY;
        internal static ActionKey Prerequisite=ActionKey.Unknown;
        public static void Begin(s7o_AutoSkill owner,IController hud,int now)
        {
            Owner=owner;
            if(hud==null||hud.Game==null){ClearRequests();return;}
            int tick=hud.Game.CurrentGameTick;
            uint world=hud.Game.Me==null?0u:hud.Game.Me.WorldId;
            if(!ReferenceEquals(hud,_hud)||tick!=_tick||world!=_world)
            { _hud=hud;_tick=tick;_world=world;_haveMonsters=_haveActors=false;_monsters.Clear();_actors.Clear(); }
            ClearRequests();
        }
        public static void ClearRequests(){HasAim=false;Prerequisite=ActionKey.Unknown;}
        public static IEnumerable<IMonster> Monsters(IController hud)
        {
            if(!_haveMonsters)
            {
                int n=0;
                foreach(var m in hud.Game.AliveMonsters)
                {if(++n>2048)break;if(m!=null&&m.IsAlive&&m.WorldId==_world&&m.FloorCoordinate!=null)_monsters.Add(m);}
                _haveMonsters=true;
            }
            return _monsters;
        }
        public static IEnumerable<IActor> Actors(IController hud)
        {
            if(!_haveActors)
            {int n=0;foreach(var a in hud.Game.Actors){if(++n>8192)break;if(a!=null&&a.WorldId==_world)_actors.Add(a);}_haveActors=true;}
            return _actors;
        }
        public static int CountNear(IController hud,IPlayer player,double range)
        {
            if(player==null||player.FloorCoordinate==null)return 0;
            int count=0;foreach(var m in Monsters(hud))
                if(m.WorldId==player.WorldId&&m.FloorCoordinate.XYDistanceTo(player.FloorCoordinate)-Math.Max(0,m.RadiusBottom)<=range)count++;
            return count;
        }
        private static IMonster Nearest(IController hud,Func<IMonster,bool> predicate)
        {IMonster best=null;double distance=double.MaxValue;foreach(var m in Monsters(hud))if(predicate(m)&&m.NormalizedXyDistanceToMe<distance){best=m;distance=m.NormalizedXyDistanceToMe;}return best;}
        public static IMonster NearestBoss(IController hud){return Nearest(hud,m=>m.Rarity==ActorRarity.Boss);}
        public static IMonster NearestEliteButNoMinion(IController hud){return Nearest(hud,m=>m.Rarity==ActorRarity.Champion||m.Rarity==ActorRarity.Rare);}
        public static IMonster NearestGoblin(IController hud){return Nearest(hud,m=>m.SnoMonster!=null&&m.SnoMonster.Priority==MonsterPriority.goblin);}
        public static IMonster NearestKeywarden(IController hud){return Nearest(hud,m=>m.SnoMonster!=null&&m.SnoMonster.Priority==MonsterPriority.keywarden);}
        public static bool EliteOrBossNear(IController hud,double range,bool includeGoblin=true)
        {foreach(var m in Monsters(hud))if(m.NormalizedXyDistanceToMe<=range&&(m.IsElite||m.Rarity==ActorRarity.Boss||(includeGoblin&&m.SnoMonster!=null&&m.SnoMonster.Priority>=MonsterPriority.keywarden)))return true;return false;}
        public static IActor NearestHealthGlobe(IController hud)
        {
            IActor best=null;double gap=double.MaxValue;
            foreach(var a in Actors(hud))if(a.SnoActor!=null&&a.SnoActor.Kind==ActorKind.HealthGlobe&&a.NormalizedXyDistanceToMe<gap)
            {best=a;gap=a.NormalizedXyDistanceToMe;}return best;
        }
        public static bool IsHotKeySet(IController hud,ActionKey key){return Owner.MacroVirtualKey(key)!=0;}
        public static bool IsActionHeld(IController hud,ActionKey key){return Owner.MacroActionHeld(key);}
        public static void RequestAim(IController hud,float x,float y){HasAim=true;AimX=x;AimY=y;}
        public static void RequestPrerequisite(IController hud,ActionKey key){Prerequisite=key;}
        public static int DangerCount(IController hud,IPlayer player)
        {
            if(player==null||player.FloorCoordinate==null)return 0;
            int count=0;foreach(var a in Actors(hud))
            {
                if(a.SnoActor==null||a.IsDisabled||a.FloorCoordinate==null||a.WorldId!=player.WorldId)continue;
                double radius=0;var sno=a.SnoActor.Sno;
                if(sno==ActorSnoEnum._monsteraffix_frozen_iceclusters)radius=15.8;
                else if(sno==ActorSnoEnum._monsteraffix_molten_deathstart_proxy)radius=13;
                else if(sno==ActorSnoEnum._monsteraffix_desecrator_damage_aoe)radius=8;
                else if(sno==ActorSnoEnum._monsteraffix_plagued_endcloud)radius=12;
                else if(sno==ActorSnoEnum._gluttony_gascloud_proxy)radius=20;
                else if(sno==ActorSnoEnum._x1_monsteraffix_frozenpulse_monster)radius=14;
                if(radius>0&&Math.Abs(a.FloorCoordinate.Z-player.FloorCoordinate.Z)<=8&&a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate)<=radius)count++;
            }
            return count;
        }
        public static bool InstantDanger(IController hud,IPlayer player)
        {
            if(player==null||player.FloorCoordinate==null)return false;
            foreach(var a in Actors(hud))
            {
                if(a.SnoActor==null||a.IsDisabled||a.FloorCoordinate==null||a.WorldId!=player.WorldId)continue;
                bool frozen=a.SnoActor.Sno==ActorSnoEnum._monsteraffix_frozen_iceclusters;
                bool molten=a.SnoActor.Sno==ActorSnoEnum._monsteraffix_molten_deathstart_proxy;
                if(!frozen&&!molten)continue;
                int age=hud.Game.CurrentGameTick-a.CreatedAtInGameTick;
                if(age>=120&&age<=195&&Math.Abs(a.FloorCoordinate.Z-player.FloorCoordinate.Z)<=8
                    &&a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate)<=(frozen?15.8:13))return true;
            }
            return false;
        }
        public static bool FrozenNear(IController hud,IPlayer player)
        {
            if(player==null||player.FloorCoordinate==null)return false;
            foreach(var a in Actors(hud))if(a.SnoActor!=null&&a.SnoActor.Sno==ActorSnoEnum._monsteraffix_frozen_iceclusters
                &&!a.IsDisabled&&a.WorldId==player.WorldId&&a.FloorCoordinate!=null
                &&a.FloorCoordinate.XYDistanceTo(player.FloorCoordinate)<=15.8)return true;
            return false;
        }
    }
    internal static class S7oMacroData
    {
        public static int GetMonsterDensity(this IMonster monster,double range)
        {if(monster==null||monster.FloorCoordinate==null)return 0;int n=0;foreach(var m in S7oMacroQueries.Monsters(S7oMacroQueries.Owner.Hud))if(m.WorldId==monster.WorldId&&m.FloorCoordinate.XYDistanceTo(monster.FloorCoordinate)-m.RadiusBottom<=range)n++;return n;}
        public static double Seconds(double[] a,int i){return a!=null&&i>=0&&i<a.Length?a[i]:double.NaN;}
        public static double Elapsed(double[] a,int i){return a!=null&&i>=0&&i<a.Length?a[i]:double.NaN;}
        public static int Count(int[] a,int i){return a!=null&&i>=0&&i<a.Length?a[i]:0;}
        public static double? NullableSeconds(IBuff b,int i){return b==null?(double?)null:Seconds(b.TimeLeftSeconds,i);}
        public static double? NullableElapsed(IBuff b,int i){return b==null?(double?)null:Elapsed(b.TimeElapsedSeconds,i);}
        public static int? NullableCount(IBuff b,int i){return b==null?(int?)null:Count(b.IconCounts,i);}
        public static double RemainingBuffTime(this IPlayerSkill s,int icon=-1)
        {
            var b=s==null?null:s.Buff;if(b==null||!b.Active)return 0;
            if(icon>=0)return Seconds(b.TimeLeftSeconds,icon);
            if(b.TimeLeftSeconds==null||b.TimeLeftSeconds.Length==0)return double.PositiveInfinity;
            double max=0;foreach(double v in b.TimeLeftSeconds)if(v>max)max=v;return max;
        }
    }
    public static class S7oHealingMantraPolicy
    {
        private static IController _hud;
        private static uint _hero,_boss;
        private static int _tick=int.MinValue,_riftStart;
        private static bool _dead,_guardianPhase;
        public static void OnNewArea(bool newGame){if(newGame){_dead=_guardianPhase=false;_boss=0;_tick=int.MinValue;_riftStart=0;}}
        public static void NoteKill(IController hud,IMonster monster)
        {
            if(hud==null||hud.Game==null||monster==null)return;
            var g=hud.Game;
            if(g.SpecialArea!=SpecialArea.Rift&&g.SpecialArea!=SpecialArea.GreaterRift)return;
            if(monster.Rarity!=ActorRarity.Boss)return;
            Allowed(hud);
            if(_guardianPhase&&_boss!=0&&monster.AcdId==_boss)_dead=true;
        }
        public static bool Allowed(IController hud)
        {
            if(hud==null||hud.Game==null||hud.Game.Me==null)return false;
            var g=hud.Game;var me=g.Me;
            if(!g.IsInGame||g.IsLoading||g.IsPaused||g.IsInTown||me.IsDead)return false;
            uint hero=me.HeroId;
            if(!ReferenceEquals(_hud,hud)||hero!=_hero){_hud=hud;_hero=hero;OnNewArea(true);}
            bool rift=g.SpecialArea==SpecialArea.Rift||g.SpecialArea==SpecialArea.GreaterRift;
            if(!rift)return true;
            if(g.CurrentGameTick==_tick)return !_dead;
            _tick=g.CurrentGameTick;
            if(g.RiftPercentage<99.9&&_guardianPhase){_dead=_guardianPhase=false;_boss=0;}
            if(g.SpecialArea==SpecialArea.GreaterRift&&g.CurrentTimedEventStartTick!=_riftStart)
            {_riftStart=g.CurrentTimedEventStartTick;_dead=_guardianPhase=false;_boss=0;}
            if(g.RiftPercentage>=100)_guardianPhase=true;
            int n=0;
            if(_guardianPhase)foreach(var m in g.Monsters)
            {
                if(++n>2048)break;
                if(m==null||m.WorldId!=me.WorldId||m.Rarity!=ActorRarity.Boss)continue;
                if(_guardianPhase&&m.IsAlive&&_boss==0)_boss=m.AcdId;
                if(_boss!=0&&m.AcdId==_boss&&!m.IsAlive)_dead=true;
            }
            if(g.SpecialArea==SpecialArea.GreaterRift&&_guardianPhase&&g.Quests!=null)
                foreach(var q in g.Quests)if(q!=null&&q.SnoQuest!=null&&q.SnoQuest.Sno==382695)
                    if(q.QuestStepId==5||q.QuestStepId==10||q.QuestStepId==34||q.QuestStepId==46)_dead=true;
            return !_dead;
        }
        public static bool UseIdleSpiritReserve(AcdAnimationState state,bool channelPylon)
        {
            return !channelPylon&&state!=AcdAnimationState.Attacking
                &&state!=AcdAnimationState.Channeling&&state!=AcdAnimationState.Running;
        }
        public static bool Due(bool combat,bool channelPylon,bool active,bool weakened,double left,int since,int cadence)
        {
            if(since<Math.Max(100,cadence))return false;
            if(channelPylon||combat)return true;
            return !active||weakened||(left>0&&left<=0.3)||(left<=0&&since>=2700);
        }
    }


    internal sealed class S7oAttackingTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Me.AnimationState == AcdAnimationState.Attacking ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oAttackingTestFluent
    {
        public static S7oAttackingTest IfAttacking(this S7oAbstractTest parent)
        {
            var test = new S7oAttackingTest();

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oBossNearbyTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> RangeFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var range = RangeFunc(context);
            return S7oMacroQueries.NearestBoss(context.Hud) != null && S7oMacroQueries.NearestBoss(context.Hud).NormalizedXyDistanceToMe <= range ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oBossNearbyTestFluent
    {
        public static S7oBossNearbyTest IfBossIsNearby(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc)
        {
            var test = new S7oBossNearbyTest()
            {
                RangeFunc = rangeFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oBuffIsAboutToExpireTest : S7oAbstractTest
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int IconIndex { get; set; }
        public bool ExcludeZero { get; set; }
        internal override S7oTestResult Test(S7oTestContext context)
        {
            var limit = S7oImportedPolicy.ChangeRnd(context.Hud, "BuffIsAboutToExpire_" + context.Skill.SnoPower.Code, Min, Max, Max);
            if (limit < 0)
                limit = 0;
            var remaining = IconIndex == -1 ? context.Skill.RemainingBuffTime() : context.Skill.RemainingBuffTime(IconIndex);
            return remaining <= (limit / 1000.0f) && ((ExcludeZero && remaining > 0) || !ExcludeZero) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oBuffIsAboutToExpireTestFluent
    {
        public static S7oBuffIsAboutToExpireTest IfBuffIsAboutToExpire(this S7oAbstractTest parent, int min, int max, int iconIndex = -1, bool excludeZero = false)
        {
            var test = new S7oBuffIsAboutToExpireTest()
            {
                Min = min,
                Max = max,
                IconIndex = iconIndex,
                ExcludeZero = excludeZero,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oCanCastBuffTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            var limit = S7oImportedPolicy.ChangeRnd(context.Hud, "CanCastBuff", 400, 800, 300);
            if (limit < 0)
                limit = 0;
            return context.LastBuffCasted.TimerTest(limit) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oCanCastBuffTestFluent
    {
        public static S7oCanCastBuffTest IfCanCastBuff(this S7oAbstractTest parent)
        {
            var test = new S7oCanCastBuffTest();

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oCanCastSimpleTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            var limit = S7oImportedPolicy.ChangeRnd(context.Hud, "CanCastSimple", 150, 250, 250);
            if (limit < 0)
                limit = 0;
            return context.LastSimpleCasted.TimerTest(limit) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oCanCastSimpleTestFluent
    {
        public static S7oCanCastSimpleTest IfCanCastSimple(this S7oAbstractTest parent)
        {
            var test = new S7oCanCastSimpleTest();

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oCanCastSkillTest : S7oAbstractTest
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int ChangeAfter { get; set; }
        public Func<S7oTestContext, double> Multiplier { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var multiplier = Multiplier != null ? Multiplier(context) : 1.0d;
            var min = Convert.ToInt32(Min * multiplier);
            var max = Convert.ToInt32(Max * multiplier);
            var changeAfter = Convert.ToInt32(ChangeAfter * multiplier);

            var limit = min != max ? S7oImportedPolicy.ChangeRnd(context.Hud, "SkillCastDelayTest_" + context.Skill.SnoPower.Code, min, max, changeAfter) : min;
            if (limit < 0)
                limit = 0;
            return context.LastSkillCasted.TimerTest(limit) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oCanCastSkillTestFluent
    {
        public static S7oCanCastSkillTest IfCanCastSkill(this S7oAbstractTest parent, int min, int max, int changeAfter, Func<S7oTestContext, double> multiplier = null)
        {
            var test = new S7oCanCastSkillTest()
            {
                Min = min,
                Max = max,
                ChangeAfter = changeAfter,
                Multiplier = multiplier,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oCastingIdentifyTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.Generic_IdentifyAllWithCast.Sno)
                || context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.Generic_IdentifyWithCast.Sno)
                || context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.Generic_IdentifyWithCastLegendary.Sno)
                ? ResultOnSuccess
                : ResultOnFail;
        }
    }

    internal static class S7oCastingIdentifyTestFluent
    {
        public static S7oCastingIdentifyTest IfCastingIdentify(this S7oAbstractTest parent)
        {
            var test = new S7oCastingIdentifyTest();

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oCastingPortalTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Me.AnimationState == AcdAnimationState.CastingPortal ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oCastingTownPortalTestFluent
    {
        public static S7oCastingPortalTest IfCastingPortal(this S7oAbstractTest parent)
        {
            var test = new S7oCastingPortalTest();

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oChannelingSkillTest : S7oAbstractTest
    {
        public List<ISnoPower> Skills { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            foreach (var otherPower in Skills)
            {
                var otherSkill = context.Skill.Player.Powers.GetUsedSkill(otherPower);
                if (otherSkill == null) continue;

                if (S7oMacroQueries.IsActionHeld(context.Hud, otherSkill.Key))
                {
                    return ResultOnSuccess;
                }
            }

            return ResultOnFail;
        }
    }

    internal static class S7oChannelingSkillTestFluent
    {
        public static S7oChannelingSkillTest IfChannelingSkillTest(this S7oAbstractTest parent, List<ISnoPower> skills)
        {
            var test = new S7oChannelingSkillTest()
            {
                Skills = skills,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oCustomFalseTest : S7oAbstractTest
    {
        public Func<S7oTestContext, bool> TestFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (TestFunc == null) return S7oTestResult.Continue;

            var result = TestFunc.Invoke(context);
            return result ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oCustomFalseTestFluent
    {
        public static S7oCustomFalseTest IfFalse(this S7oAbstractTest parent, Func<S7oTestContext, bool> testFunc)
        {
            var test = new S7oCustomFalseTest()
            {
                TestFunc = testFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oCustomTrueTest : S7oAbstractTest
    {
        public Func<S7oTestContext, bool> TestFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (TestFunc == null) return S7oTestResult.Continue;

            var result = TestFunc.Invoke(context);
            return result ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oCustomTrueTestFluent
    {
        public static S7oCustomTrueTest IfTrue(this S7oAbstractTest parent, Func<S7oTestContext, bool> testFunc)
        {
            var test = new S7oCustomTrueTest()
            {
                TestFunc = testFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oEliteIsNearbyTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> RangeFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var range = RangeFunc(context);
            return (S7oMacroQueries.NearestEliteButNoMinion(context.Hud) != null && S7oMacroQueries.NearestEliteButNoMinion(context.Hud).NormalizedXyDistanceToMe <= range)
                || (S7oMacroQueries.NearestGoblin(context.Hud) != null && S7oMacroQueries.NearestGoblin(context.Hud).NormalizedXyDistanceToMe <= range)
                ? ResultOnSuccess
                : ResultOnFail;
        }
    }

    internal static class S7oEliteIsNearbyTestFluent
    {
        public static S7oEliteIsNearbyTest IfEliteIsNearby(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc)
        {
            var test = new S7oEliteIsNearbyTest()
            {
                RangeFunc = rangeFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }




    internal sealed class S7oEliteOrBossNearbyCursorTest : S7oAbstractTest
    {
        public bool IncludeMinion { get; set; }
        public Func<S7oTestContext, int> RangeFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (RangeFunc == null) return S7oTestResult.Continue;

            var range = RangeFunc(context);
            IWorldCoordinate cursor = context.Hud.Window.CreateScreenCoordinate(context.Hud.Window.CursorX, context.Hud.Window.CursorY).ToWorldCoordinate();
            bool Result = S7oMacroQueries.Monsters(context.Hud).Any(m =>((m.IsElite && (IncludeMinion ? true : m.Rarity != ActorRarity.RareMinion)) || m.SnoMonster.Priority == MonsterPriority.goblin) && (m.FloorCoordinate.XYZDistanceTo(cursor) - m.RadiusBottom) <= range && !m.Invulnerable && !m.Invisible && !m.Illusion);
            return Result ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oIsEliteOrBossNearbyCursorTestFluent
    {
        public static S7oEliteOrBossNearbyCursorTest IfEliteOrBossNearbyCursor(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc, bool includeMinion = false)
        {
            var test = new S7oEliteOrBossNearbyCursorTest()
            {
                RangeFunc = rangeFunc,
                IncludeMinion = includeMinion,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oEliteOrBossNearbyTest : S7oAbstractTest
    {
		public bool IncludeMinion { get; set; }
        public Func<S7oTestContext, int> RangeFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var range = RangeFunc(context);
            return S7oMacroQueries.EliteOrBossNear(context.Hud, range, IncludeMinion)
                || (S7oMacroQueries.NearestGoblin(context.Hud) != null && S7oMacroQueries.NearestGoblin(context.Hud).NormalizedXyDistanceToMe <= range)
                ? ResultOnSuccess
                : ResultOnFail;
        }
    }

    internal static class S7oIsEliteOrBossNearbyTestFluent
    {
        public static S7oEliteOrBossNearbyTest IfEliteOrBossIsNearby(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc, bool includeMinion = false)
        {
            var test = new S7oEliteOrBossNearbyTest()
            {
                RangeFunc = rangeFunc,
                IncludeMinion = includeMinion,
            };

            parent.NextTest = test;
            return test;
        }
    }





    internal sealed class S7oEnoughMonstersInSectorTest : S7oAbstractTest
    {
        public Func<S7oTestContext, float> SkillWidthFunc { get; set; }
        public Func<S7oTestContext, float> SkillRangeFunc { get; set; }
        public Func<S7oTestContext, float> ZoffsetFunc { get; set; }
        
        public Func<S7oTestContext, int> MonsterCountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (SkillWidthFunc == null || SkillRangeFunc == null || MonsterCountFunc == null) return S7oTestResult.Continue;

            var width = SkillWidthFunc(context);
            var range = SkillRangeFunc(context);
            var cursorzoffset = ZoffsetFunc(context);
            var limit = MonsterCountFunc(context);
            var density = context.Hud.Game.Monsters.Count(m => m.IsAlive && !m.Invulnerable && !m.Invisible && S7oMacroMechanics.isMobInSkillRange(context.Hud, m, width, range, cursorzoffset));
            return density >= limit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oIsEnoughMonstersInSectorTestFluent
    {
        public static S7oEnoughMonstersInSectorTest IfEnoughMonstersInSector(this S7oAbstractTest parent, Func<S7oTestContext, float> skillWithFunc, Func<S7oTestContext, float> skillRangeFunc, Func<S7oTestContext, float> ZoffsetFunc, Func<S7oTestContext, int> monsterCountFunc)
        {
            var test = new S7oEnoughMonstersInSectorTest()
            {
                SkillWidthFunc = skillWithFunc,
                SkillRangeFunc = skillRangeFunc,
                MonsterCountFunc = monsterCountFunc,
                ZoffsetFunc = ZoffsetFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }




    internal sealed class S7oEnoughMonstersNearbyCursorTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> RangeFunc { get; set; }
        public Func<S7oTestContext, int> MonsterCountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (RangeFunc == null || MonsterCountFunc == null) return S7oTestResult.Continue;

            var range = RangeFunc(context);
            var limit = MonsterCountFunc(context);
            IWorldCoordinate cursor = context.Hud.Window.CreateScreenCoordinate(context.Hud.Window.CursorX, context.Hud.Window.CursorY).ToWorldCoordinate();
            var density = context.Hud.Game.Monsters.Count(m => (m.FloorCoordinate.XYZDistanceTo(cursor) - m.RadiusBottom) <= range && m.IsAlive && !m.Invulnerable && !m.Invisible);
            return density >= limit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oIsEnoughMonstersNearbyCursorTestFluent
    {
        public static S7oEnoughMonstersNearbyCursorTest IfEnoughMonstersNearbyCursor(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc, Func<S7oTestContext, int> monsterCountFunc)
        {
            var test = new S7oEnoughMonstersNearbyCursorTest()
            {
                RangeFunc = rangeFunc,
                MonsterCountFunc = monsterCountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oEnoughMonstersNearbyTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> RangeFunc { get; set; }
        public Func<S7oTestContext, int> MonsterCountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (RangeFunc == null || MonsterCountFunc == null) return S7oTestResult.Continue;

            var range = RangeFunc(context);
            var limit = MonsterCountFunc(context);

            var density = S7oMacroQueries.CountNear(context.Hud, context.Skill.Player, range);
            return density >= limit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oIsEnoughMonstersNearbyTestFluent
    {
        public static S7oEnoughMonstersNearbyTest IfEnoughMonstersNearby(this S7oAbstractTest parent, Func<S7oTestContext, int> rangeFunc, Func<S7oTestContext, int> monsterCountFunc)
        {
            var test = new S7oEnoughMonstersNearbyTest()
            {
                RangeFunc = rangeFunc,
                MonsterCountFunc = monsterCountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oForceCastTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return S7oTestResult.Cast;
        }
    }

    internal static class S7oForceCastTestFluent
    {
        public static S7oForceCastTest ForceCast(this S7oAbstractTest parent)
        {
            var test = new S7oForceCastTest();

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oHealthBelowPercentageTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> PercentageFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var limit = PercentageFunc(context);
            return context.Skill.Player.Defense.HealthPct < limit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oHealthBelowPercentageTestFluent
    {
        public static S7oHealthBelowPercentageTest IfHealthPercentageIsBelow(this S7oAbstractTest parent, Func<S7oTestContext, int> percentageFunc)
        {
            var test = new S7oHealthBelowPercentageTest()
            {
                PercentageFunc = percentageFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oHealthWarningTest : S7oAbstractTest
    {
        public int LimitWhenPotionAvailable { get; set; }
        public int LimitWhenPotionOnCoolDown { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (!context.Skill.Player.Powers.HealthPotionSkill.IsOnCooldown && context.Skill.Player.Defense.HealthPct <= LimitWhenPotionAvailable) return ResultOnSuccess;
            if (context.Skill.Player.Powers.HealthPotionSkill.IsOnCooldown && context.Skill.Player.Defense.HealthPct <= LimitWhenPotionOnCoolDown) return ResultOnSuccess;

            return ResultOnFail;
        }
    }

    internal static class S7oHealthWarningTestFluent
    {
        public static S7oHealthWarningTest IfHealthWarning(this S7oAbstractTest parent, int limitWhenPotionAvailable, int limitWhenPotionOnCooldown)
        {
            var test = new S7oHealthWarningTest()
            {
                LimitWhenPotionAvailable = limitWhenPotionAvailable,
                LimitWhenPotionOnCoolDown = limitWhenPotionOnCooldown,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oIdleTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Me.AnimationState == AcdAnimationState.Idle ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oIdleTestFluent
    {
        public static S7oIdleTest IfIdle(this S7oAbstractTest parent)
        {
            var test = new S7oIdleTest();

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oInTownTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.IsInTown ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oInTownTestFluent
    {
        public static S7oInTownTest IfInTown(this S7oAbstractTest parent)
        {
            var test = new S7oInTownTest();

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oLevelingNearbyPartyMemberTest : S7oAbstractTest
    {
        public int Range { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.Player.CurrentLevelNormal == context.Skill.Player.CurrentLevelNormalCap
                && context.Hud.Game.Players.Any(player =>
                !player.IsMe
                && player.AcdId != 0
                && !player.Powers.BuffIsActive(224639)
                && !player.IsDead
                && player.Defense.HealthPct >= 1
                && player.NormalizedXyDistanceToMe <= Range
                && player.CurrentLevelNormal < player.CurrentLevelNormalCap)
                ? ResultOnSuccess
                : ResultOnFail;
        }
    }

    internal static class S7oLevelingNearbyPartyMemberTestFluent
    {
        public static S7oLevelingNearbyPartyMemberTest IfLevelingNearbyPartyMember(this S7oAbstractTest parent, int range)
        {
            var test = new S7oLevelingNearbyPartyMemberTest()
            {
                Range = range,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oNearbyPartyMemberIsInDangerTest : S7oAbstractTest
    {
        public int Range { get; set; }
        public int LimitWhenPotionAvailable { get; set; }
        public int LimitWhenPotionOnCoolDown { get; set; }
        public int LimitWhenCantMove { get; set; }
        public bool TriggerOnInstantDeath { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Players.Any(player =>
                !player.IsMe
                && player.HasValidActor
                && !player.IsDead
                && player.Defense.HealthPct >= 1
                && player.NormalizedXyDistanceToMe <= Range
                    && ((TriggerOnInstantDeath && S7oMacroQueries.InstantDanger(context.Hud, player))
                     || (!player.Powers.HealthPotionSkill.IsOnCooldown && player.Defense.HealthPct < LimitWhenPotionAvailable)
                     || (player.Powers.HealthPotionSkill.IsOnCooldown && player.Defense.HealthPct < LimitWhenPotionOnCoolDown)
                     || (player.Powers.CantMove && player.Defense.HealthPct < LimitWhenCantMove)))
                ? ResultOnSuccess
                : ResultOnFail;
        }
    }

    internal static class S7oPartyMemberIsInDangerTestFluent
    {
        public static S7oNearbyPartyMemberIsInDangerTest IfNearbyPartyMemberIsInDanger(this S7oAbstractTest parent, int range, int limitWhenPotionAvailable, int limitWhenPotionOnCooldown, int limitWhenCantMove, bool triggerOnInstantDeath)
        {
            var test = new S7oNearbyPartyMemberIsInDangerTest()
            {
                Range = range,
                LimitWhenPotionAvailable = limitWhenPotionAvailable,
                LimitWhenPotionOnCoolDown = limitWhenPotionOnCooldown,
                LimitWhenCantMove = limitWhenCantMove,
                TriggerOnInstantDeath = triggerOnInstantDeath,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oOnCooldownTest : S7oAbstractTest
    {
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.IsOnCooldown ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oOnCooldownTestFluent
    {
        public static S7oOnCooldownTest IfOnCooldown(this S7oAbstractTest parent)
        {
            var test = new S7oOnCooldownTest();

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oPrimaryResourceAmountIsAboveTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> AmountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var amount = AmountFunc(context);
            return context.Skill.Player.Stats.ResourceCurPri >= amount ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oPrimaryResourceAmountIsAboveTestFluent
    {
        public static S7oPrimaryResourceAmountIsAboveTest IfPrimaryResourceAmountIsAbove(this S7oAbstractTest parent, Func<S7oTestContext, int> amountFunc)
        {
            var test = new S7oPrimaryResourceAmountIsAboveTest()
            {
                AmountFunc = amountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oPrimaryResourceAmountIsBelowTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> AmountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var amount = AmountFunc(context);
            return context.Skill.Player.Stats.ResourceCurPri >= amount ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oPrimaryResourceAmountIsBelowTestFluent
    {
        public static S7oPrimaryResourceAmountIsBelowTest IfPrimaryResourceAmountIsBelow(this S7oAbstractTest parent, Func<S7oTestContext, int> amountFunc)
        {
            var test = new S7oPrimaryResourceAmountIsBelowTest()
            {
                AmountFunc = amountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oPrimaryResourcePercentageIsAboveTest : S7oAbstractTest
    {
        public int PercentageLimit { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.Player.Stats.ResourcePctPri >= PercentageLimit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oPrimaryResourcePercentageIsAboveTestFluent
    {
        public static S7oPrimaryResourcePercentageIsAboveTest IfPrimaryResourcePercentageIsAbove(this S7oAbstractTest parent, int percentageLimit)
        {
            var test = new S7oPrimaryResourcePercentageIsAboveTest()
            {
                PercentageLimit = percentageLimit,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oPrimaryResourcePercentageIsBelowTest : S7oAbstractTest
    {
        public int PercentageLimit { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.Player.Stats.ResourcePctPri >= PercentageLimit ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oPrimaryResourcePercentageIsBelowTestFluent
    {
        public static S7oPrimaryResourcePercentageIsBelowTest IfPrimaryResourcePercentageIsBelow(this S7oAbstractTest parent, int percentageLimit)
        {
            var test = new S7oPrimaryResourcePercentageIsBelowTest()
            {
                PercentageLimit = percentageLimit,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oPrimaryResourceRequirementTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> SpareResourceFunc { get; set; }
        public Func<S7oTestContext, int> BaseResourceCalculatorFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (BaseResourceCalculatorFunc == null)
                return S7oTestResult.Continue;
            var resourceRequirement = BaseResourceCalculatorFunc(context) == 0 ? context.Skill.GetResourceRequirement() : context.Skill.GetResourceRequirement(BaseResourceCalculatorFunc(context));
            resourceRequirement += SpareResourceFunc(context);
            return context.Skill.Player.Stats.ResourceCurPri < resourceRequirement ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oPrimaryResourceRequirementTestFluent
    {
        public static S7oPrimaryResourceRequirementTest IfPrimaryResourceIsEnough(this S7oAbstractTest parent, int spareResource, Func<S7oTestContext, int> baseResourceCalculatorFunc)
        {
            var test = new S7oPrimaryResourceRequirementTest()
            {
                SpareResourceFunc = ctx => spareResource,
                BaseResourceCalculatorFunc = baseResourceCalculatorFunc,
            };

            parent.NextTest = test;
            return test;
        }

        public static S7oPrimaryResourceRequirementTest IfPrimaryResourceIsEnough(this S7oAbstractTest parent, Func<S7oTestContext, int> spareResourceFunc, Func<S7oTestContext, int> baseResourceCalculatorFunc)
        {
            var test = new S7oPrimaryResourceRequirementTest()
            {
                SpareResourceFunc = spareResourceFunc,
                BaseResourceCalculatorFunc = baseResourceCalculatorFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oRunningTest : S7oAbstractTest
    {
        public bool Skill { get; set; }
        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Hud.Game.Me.AnimationState == AcdAnimationState.Running && Skill || (context.Hud.Game.Me.AnimationState == AcdAnimationState.Running && !Skill && 
                !context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.Monk_TempestRush.Sno, 0) &&
                !context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.DemonHunter_Strafe.Sno, 0) &&
                !context.Hud.Game.Me.Powers.BuffIsActive(context.Hud.Sno.SnoPowers.Barbarian_Whirlwind.Sno, 0))
                ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oRunningTestFluent
    {
        public static S7oRunningTest IfRunning(this S7oAbstractTest parent, bool skill = false)
        {
            var test = new S7oRunningTest()
            {
                Skill = skill,
            };
            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oSecondaryResourceAmountIsAboveTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> AmountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var amount = AmountFunc(context);
            return context.Skill.Player.Stats.ResourceCurSec >= amount ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oSecondaryResourceAmountIsAboveTestFluent
    {
        public static S7oSecondaryResourceAmountIsAboveTest IfSecondaryResourceAmountIsAbove(this S7oAbstractTest parent, Func<S7oTestContext, int> amountFunc)
        {
            var test = new S7oSecondaryResourceAmountIsAboveTest()
            {
                AmountFunc = amountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oSecondaryResourceAmountIsBelowTest : S7oAbstractTest
    {
        public Func<S7oTestContext, int> AmountFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var amount = AmountFunc(context);
            return context.Skill.Player.Stats.ResourceCurSec >= amount ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oSecondaryResourceAmountIsBelowTestFluent
    {
        public static S7oSecondaryResourceAmountIsBelowTest IfSecondaryResourceAmountIsBelow(this S7oAbstractTest parent, Func<S7oTestContext, int> amountFunc)
        {
            var test = new S7oSecondaryResourceAmountIsBelowTest()
            {
                AmountFunc = amountFunc,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSecondaryResourcePercentageIsAboveTest : S7oAbstractTest
    {
        public int PercentageLimit { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.Player.Stats.ResourcePctSec >= PercentageLimit ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oSecondaryResourcePercentageIsAboveTestFluent
    {
        public static S7oSecondaryResourcePercentageIsAboveTest IfSecondaryResourcePercentageIsAbove(this S7oAbstractTest parent, int percentageLimit)
        {
            var test = new S7oSecondaryResourcePercentageIsAboveTest()
            {
                PercentageLimit = percentageLimit
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSecondaryResourcePercentageIsBelowTest : S7oAbstractTest
    {
        public int PercentageLimit { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return context.Skill.Player.Stats.ResourcePctSec >= PercentageLimit ? ResultOnFail : ResultOnSuccess;
        }
    }

    internal static class S7oSecondaryResourcePercentageIsBelowTestFluent
    {
        public static S7oSecondaryResourcePercentageIsBelowTest IfSecondaryResourcePercentageIsBelow(this S7oAbstractTest parent, int percentageLimit)
        {
            var test = new S7oSecondaryResourcePercentageIsBelowTest()
            {
                PercentageLimit = percentageLimit
            };

            parent.NextTest = test;
            return test;
        }
    }



    internal sealed class S7oSecondaryResourceRequirementTest : S7oAbstractTest
    {
        public int SpareResource { get; set; }
        public Func<S7oTestContext, int> BaseResourceCalculatorFunc { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (BaseResourceCalculatorFunc == null) return S7oTestResult.Continue;
            var resourceRequirement = this.BaseResourceCalculatorFunc(context) == 0 ? context.Skill.GetResourceRequirement() : context.Skill.GetResourceRequirement(this.BaseResourceCalculatorFunc(context));
            resourceRequirement += this.SpareResource;
            if (context.Skill.Player.Stats.ResourceCurSec < resourceRequirement)
            {
                return ResultOnFail;
            }
            else return ResultOnSuccess;
        }
    }

    internal static class S7oSecondaryResourceRequirementTestFluent
    {
        public static S7oSecondaryResourceRequirementTest IfSecondaryResourceIsEnough(this S7oAbstractTest parent, int spareResource, Func<S7oTestContext, int> baseResourceCalculatorDelegate)
        {
            var test = new S7oSecondaryResourceRequirementTest()
            {
                SpareResource = spareResource,
                BaseResourceCalculatorFunc = baseResourceCalculatorDelegate,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSpecificBuffIsAboutToExpireOnPartyTest : S7oAbstractTest
    {
        public ISnoPower SnoPower { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public int IconIndex { get; set; }
        public HeroClass HeroClass { get; set; }
        public double Range { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (context.Hud.Game.NumberOfPlayersInGame <= 1)
                return ResultOnFail;

            var limit = S7oImportedPolicy.ChangeRnd(context.Hud, "BuffIsAboutToExpire_" + context.Skill.SnoPower.Code, Min, Max, Max);
            if (limit < 0)
                limit = 0;

            var otherPlayersAround = context.Hud.Game.Players.Where(
                x => !x.IsMe
                && !x.IsDead 
                && !x.Powers.BuffIsActive(224639)
                && x.HasValidActor
                && (HeroClass == Turbo.Plugins.HeroClass.None || x.HeroClassDefinition.HeroClass == HeroClass)
                && x.CentralXyDistanceToMe <= Range);

            foreach (var player in otherPlayersAround)
            {
                var buff = player.Powers.GetBuff(SnoPower.Sno);
                if (buff?.Active == true)
                {
                    var remaining = S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex);
                    if (remaining <= (limit / 1000.0f))
                    {
                        return ResultOnSuccess;
                    }
                }
                else
                {
                    return ResultOnSuccess;
                }
            }

            return ResultOnFail;
        }
    }

    internal static class S7oSpecificBuffIsAboutToExpireOnPartyTestFluent
    {
        public static S7oSpecificBuffIsAboutToExpireOnPartyTest IfSpecificBuffIsAboutToExpireOnParty(this S7oAbstractTest parent, ISnoPower snoPower, int iconIndex, int min, int max, HeroClass heroClass, double range)
        {
            var test = new S7oSpecificBuffIsAboutToExpireOnPartyTest()
            {
                SnoPower = snoPower,
                Min = min,
                Max = max,
                IconIndex = iconIndex,
                HeroClass = heroClass,
                Range = range,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSpecificBuffIsAboutToExpireTest : S7oAbstractTest
    {
        public ISnoPower SnoPower { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public int IconIndex { get; set; }
        public bool ExcludeZero { get; set; }
        public int MaxStacks { get; set; }
        internal override S7oTestResult Test(S7oTestContext context)
        {
            var limit = S7oImportedPolicy.ChangeRnd(context.Hud, "BuffIsAboutToExpire_" + context.Skill.SnoPower.Code, Min, Max, Max);
            if (limit < 0)
                limit = 0;

            var buff = context.Skill.Player.Powers.GetBuff(SnoPower.Sno);
            var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
            var stack = buff?.Active == true ? S7oMacroData.Count((buff)?.IconCounts, IconIndex) : 0;
            return (remaining <= (limit / 1000.0f) && ((ExcludeZero && remaining > 0) || !ExcludeZero)) || (stack < MaxStacks && !ExcludeZero) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oSpecificBuffIsAboutToExpireTestFluent
    {
        public static S7oSpecificBuffIsAboutToExpireTest IfSpecificBuffIsAboutToExpire(this S7oAbstractTest parent, ISnoPower snoPower, int iconIndex, int min, int max, bool excludeZero = false, int maxStacks = 1)
        {
            var test = new S7oSpecificBuffIsAboutToExpireTest()
            {
                SnoPower = snoPower,
                Min = min,
                Max = max,
                IconIndex = iconIndex,
                ExcludeZero = excludeZero,
                MaxStacks = maxStacks,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSpecificBuffIsActiveOnPartyTest : S7oAbstractTest
    {
        public ISnoPower SnoPower { get; set; }
        public int IconIndex { get; set; }
        public HeroClass HeroClass { get; set; }
        public double Range { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            if (context.Hud.Game.NumberOfPlayersInGame <= 1)
                return ResultOnFail;
            var otherPlayersAround = context.Hud.Game.Players.Where(
                x => !x.IsMe
                && x.HasValidActor
                && (HeroClass == Turbo.Plugins.HeroClass.None || x.HeroClassDefinition.HeroClass == HeroClass)
                && x.CentralXyDistanceToMe <= Range);

            foreach (var player in otherPlayersAround)
            {
                if (player.Powers.BuffIsActive(SnoPower.Sno, IconIndex))
                {
                    return ResultOnSuccess;
                }
                else
                {
                    return ResultOnFail;
                }
            }

            return ResultOnFail;
        }
    }

    internal static class S7oSpecificBuffIsActiveOnPartyTestFluent
    {
        public static S7oSpecificBuffIsActiveOnPartyTest IfSpecificBuffIsActiveOnParty(this S7oAbstractTest parent, ISnoPower snoPower, int iconIndex, HeroClass heroClass, double range)
        {
            var test = new S7oSpecificBuffIsActiveOnPartyTest()
            {
                SnoPower = snoPower,
                IconIndex = iconIndex,
                HeroClass = heroClass,
                Range = range,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSpecificBuffIsActiveTest : S7oAbstractTest
    {
        public ISnoPower SnoPower { get; set; }
        public int IconIndex { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            return (IconIndex == -1 ? context.Skill.Player.Powers.BuffIsActive(SnoPower.Sno) : context.Skill.Player.Powers.BuffIsActive(SnoPower.Sno, IconIndex)) ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oSpecificBuffIsActiveTestFluent
    {
        public static S7oSpecificBuffIsActiveTest IfSpecificBuffIsActive(this S7oAbstractTest parent, ISnoPower snoPower, int iconIndex = -1)
        {
            var test = new S7oSpecificBuffIsActiveTest()
            {
                SnoPower = snoPower,
                IconIndex = iconIndex,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal sealed class S7oSpecificSkillOnCooldownTest : S7oAbstractTest
    {
        public ISnoPower SnoPower { get; set; }

        internal override S7oTestResult Test(S7oTestContext context)
        {
            var usedSkill = context.Skill.Player.Powers.GetUsedSkill(SnoPower);
            return usedSkill?.IsOnCooldown == true ? ResultOnSuccess : ResultOnFail;
        }
    }

    internal static class S7oSpecificSkillOnCooldownTestFluent
    {
        public static S7oSpecificSkillOnCooldownTest IfSpecificSkillOnCooldown(this S7oAbstractTest parent, ISnoPower snoPower)
        {
            var test = new S7oSpecificSkillOnCooldownTest()
            {
                SnoPower = snoPower,
            };

            parent.NextTest = test;
            return test;
        }
    }


    internal abstract class S7oAbstractTest
    {
        public S7oCombatRole? CombatRoleFilter { get; set; }
        public int? RuneFilter { get; set; }

        public S7oAbstractTest NextTest { get; set; }

        internal abstract S7oTestResult Test(S7oTestContext context);

        public S7oTestResult ResultOnSuccess { get; private set; }
        public S7oTestResult ResultOnFail { get; private set; }

        public S7oTestResult LastEvaluationResult { get; private set; }

        public S7oAbstractTest ThenContinueElseNoCast()
        {
            ResultOnSuccess = S7oTestResult.Continue;
            ResultOnFail = S7oTestResult.NoCast;
            return this;
        }

        public S7oAbstractTest ThenContinueElseCast()
        {
            ResultOnSuccess = S7oTestResult.Continue;
            ResultOnFail = S7oTestResult.Cast;
            return this;
        }

        public S7oAbstractTest ThenCastElseContinue()
        {
            ResultOnSuccess = S7oTestResult.Cast;
            ResultOnFail = S7oTestResult.Continue;
            return this;
        }

        public S7oAbstractTest ThenCastElseNoCast()
        {
            ResultOnSuccess = S7oTestResult.Cast;
            ResultOnFail = S7oTestResult.NoCast;
            return this;
        }

        public S7oAbstractTest ThenNoCastElseCast()
        {
            ResultOnSuccess = S7oTestResult.NoCast;
            ResultOnFail = S7oTestResult.Cast;
            return this;
        }

        public S7oAbstractTest ThenNoCastElseContinue()
        {
            ResultOnSuccess = S7oTestResult.NoCast;
            ResultOnFail = S7oTestResult.Continue;
            return this;
        }

        public S7oAbstractTest ForSpecificCombatRole(S7oCombatRole role)
        {
            CombatRoleFilter = role;
            return this;
        }

        public S7oAbstractTest ForSpecificRune(int rune)
        {
            RuneFilter = rune;
            return this;
        }

        public S7oTestResult Evaluate(S7oTestContext context)
        {
            LastEvaluationResult = S7oTestResult.Continue;

            var skip = false;
            if (CombatRoleFilter != null && CombatRoleFilter.Value != context.Role) skip = true;
            if (RuneFilter != null && RuneFilter.Value != context.Skill.Rune) skip = true;

            if (!skip)
            {
                LastEvaluationResult = Test(context);
            }

            if (LastEvaluationResult == S7oTestResult.Continue && NextTest != null)
            {
                return NextTest.Evaluate(context);
            }

            return LastEvaluationResult;
        }
    }




    internal sealed class S7oPolicy_DrinkHealthPotionArchonMeteorBuild : S7oImportedPolicy
    {
        public S7oPolicy_DrinkHealthPotionArchonMeteorBuild()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Generic_DrinkHealthPotion;


            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(IsPotionOnCoolDown).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Powers.BuffIsActive(488004) ||Hud.Game.Me.Powers.BuffIsActive(488036) || Hud.Game.Me.Powers.BuffIsActive(488037)).ThenContinueElseNoCast()
                .IfTrue(ctx => isArchonMeteorBuild()).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_Archon, 2).ThenNoCastElseContinue()
                .IfTrue(ctx => S7oMacroMechanics.IsElementReadySoon(ctx.Hud, 2, ctx.Skill.Player, 1)).ThenCastElseContinue()
                ;
        }
        private bool IsPotionOnCoolDown(S7oTestContext ctx)
        {
            bool IsOnCooldown;
            double Cooldown;
            Cooldown = (Hud.Game.Me.Powers.HealthPotionSkill.CooldownFinishTick - Hud.Game.CurrentGameTick) / 60d;
            IsOnCooldown = Cooldown <= 30 && Cooldown >= 0 ? true : false;
            return IsOnCooldown;
        }
        private bool isArchonMeteorBuild()
        {
            return Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheSwami.Sno) && 
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.NilfursBoast.Sno) &&
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FazulasImprobableChain.Sno)
                ;
        }
    }




    internal sealed class S7oPolicy_DrinkHealthPotion : S7oImportedPolicy
    {
        public int SDT { get; set; }
        public int HC { get; set; }
        public int InDanger { get; set; }
        public double AltarPotionTriunesWill_COE { get; set; }
        public bool SDTDrink { get; set; }
        public bool HCDrink { get; set; }
        public bool InDangerDrink { get; set; }
        public bool AltarPotionTriunesWill { get; set; }
        public bool AltarPotionTriunesWill_OnlyCOE { get; set; }
        public bool AltarPotionReduceDamage { get; set; }
        public bool AltarPotionShrineBuff { get; set; }
        public S7oPolicy_DrinkHealthPotion()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect)
        {
            Enabled = false;
            SDTDrink = true;
            SDT = 40;
            HCDrink = true;
            HC = 70;
            InDangerDrink = true;
            InDanger = 80;
            AltarPotionTriunesWill = true;
            AltarPotionTriunesWill_OnlyCOE = true;
            AltarPotionTriunesWill_COE = 1d;
            AltarPotionReduceDamage = true;
            AltarPotionShrineBuff = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Generic_DrinkHealthPotion;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                
                .IfTrue(IsPotionOnCoolDown).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                
                .IfHealthPercentageIsBelow(ctx =>
                {
                    var limit = 0;

                    if (ctx.Skill.Player.HeroIsHardcore)
                    {
                        if(HCDrink)
                        {
                            limit = HC;
                        }
                    }
                    else
                    {
                        if(SDTDrink)
                        {
                            limit = SDT;
                        }
                    }
                    if (InDangerDrink && S7oMacroQueries.FrozenNear(ctx.Hud, ctx.Skill.Player))
                    {
                        limit = InDanger;
                    }
                    return limit;
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(IsPotionOnCoolDown).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var COE = false;
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    COE = AltarPotionTriunesWill_OnlyCOE == true ? S7oMacroMechanics.IsElementReady(ctx.Hud, AltarPotionTriunesWill_COE, ctx.Skill.Player, CoeIndex) : true;

                    return AltarPotionTriunesWill && Hud.Game.Me.Powers.BuffIsActive(488004) && COE;
                    }).ThenContinueElseNoCast()
                
                .IfEliteOrBossIsNearby(ctx => 25).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(IsPotionOnCoolDown).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => AltarPotionReduceDamage && Hud.Game.Me.Powers.BuffIsActive(488036)).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 25).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(IsPotionOnCoolDown).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => AltarPotionShrineBuff && Hud.Game.Me.Powers.BuffIsActive(488037)).ThenContinueElseNoCast()
                
                
                .IfEliteOrBossIsNearby(ctx => 25).ThenCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_ShrineDesecratedBlessed, 0, 120, 120).ThenCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_ShrineDesecratedEnlightened, 0, 120, 120).ThenCastElseContinue()
                
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_ShrineDesecratedFrenzied, 0, 120, 120).ThenCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_ShrineDesecratedHoarder, 0, 120, 120).ThenCastElseContinue()
                
                ;
        }
        private bool IsPotionOnCoolDown(S7oTestContext ctx)
        {
            bool IsOnCooldown;
            double Cooldown;
            Cooldown = (Hud.Game.Me.Powers.HealthPotionSkill.CooldownFinishTick - Hud.Game.CurrentGameTick) / 60d;
            IsOnCooldown = Cooldown <= 30 && Cooldown >= 0 ? true : false;
            return IsOnCooldown;
        }
    }


    internal sealed class S7oPolicy_BarbarianAncientSpear : S7oImportedPolicy
    {
        private float fury = 1;
        public S7oPolicy_BarbarianAncientSpear()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_AncientSpear;
            Rune = 1;
            CreateCastRule()
                .IfTrue(ctx => {
                    if (ctx.Skill.Player.IsInTown)
                    {
                        fury = ctx.Skill.Player.Stats.ResourceMaxFury + 200;
                    }
                    return true;
                }).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (fury == 1 && ctx.Skill.Player.Stats.ResourceCurFury < 30)
                    {
                        fury = ctx.Skill.Player.Stats.ResourceMaxFury + 200;
                    }
                    var set = Hud.Game.Me.GetSetItemCount(749637) >= 6;
                    return set &&
                    ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.SkularsSalvation.Sno) &&
                    (ctx.Skill.Player.Stats.ResourceCurFury / (fury == 1 ? ctx.Skill.Player.Stats.ResourceMaxFury : fury)>= 0.98) && 
                    ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.ArreatsLaw.Sno) && 
                    (Hud.Game.Me.Animation.ToString().Contains("throw"));
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianBattleRage : S7oImportedPolicy
    {
        public S7oPolicy_BarbarianBattleRage()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_BattleRage;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(10, ctx => 20).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(10000, 15000).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_BarbarianCallOfTheAncients : S7oImportedPolicy
	{
        public S7oPolicy_BarbarianCallOfTheAncients()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_CallOfTheAncients;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool IKset = Hud.Game.Me.GetSetItemCount(671068) >= 2;
                    return IKset && ctx.Skill.BuffIsActive;
                    }
                ).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    bool IKset = Hud.Game.Me.GetSetItemCount(671068) >= 2;
                    return (IKset) ||
                        (Hud.Game.IsEliteOnScreen) ||
                        Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno) 
                        ||
                        (Hud.Game.MaxPriorityOnScreen >= MonsterPriority.keywarden) ||
                        (S7oMacroQueries.NearestBoss(Hud) != null) ||
                        (ctx.Skill.Player.Defense.HealthPct <= 50);
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(300, 500).ThenCastElseContinue()
                ;
        }
    }



    internal sealed class S7oPolicy_BarbarianIgnorePain : S7oImportedPolicy
	{
        private IPlayer DPS = null;
        private IPlayer shawang = null;
        public S7oPolicy_BarbarianIgnorePain()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_IgnorePain;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(30, 60).ThenCastElseContinue()
                .IfNearbyPartyMemberIsInDanger(48, 30, 60, 40, true).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var players = ctx.Hud.Game.Players.Where(p => !p.IsDead && p.SnoArea.Sno == ctx.Hud.Game.Me.SnoArea.Sno && p.CentralXyDistanceToMe <= 200);
                    return ctx.Skill.Rune == 2 && players.All(p => p.CentralXyDistanceToMe <= 50 && !p.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno));
                }
                ).ThenCastElseContinue()
                .IfSpecificBuffIsActiveOnParty(Hud.Sno.SnoPowers.StoneGauntlets, 2, Turbo.Plugins.HeroClass.None, 50).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.InGreaterRiftRank == 0) return false;
                    var players = ctx.Hud.Game.Players.Where(p => !p.IsMe && !p.IsDead && !p.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno) &&
                    p.SnoArea.Sno == ctx.Hud.Game.Me.SnoArea.Sno && p.CoordinateKnown && 
                    p.Powers.UsedLegendaryGems.EsotericAlterationPrimary?.Active == false && 
                    p.Powers.UsedLegendaryGems.GemOfEfficaciousToxinPrimary?.Active == false && 
                    p.Powers.UsedLegendaryPowers.OculusRing?.Active == false);
                    if (players == null) return false;
                    shawang = players.FirstOrDefault(p => p.Powers.UsedLegendaryGems.BaneOfTheStrickenPrimary?.Active == true);
                    if (shawang == null)
                    {
                        shawang = players.OrderByDescending(p => p.Offense.SheetDps).FirstOrDefault();
                    }
                    DPS = players.Where(p => p.Powers.UsedLegendaryGems.BaneOfTheStrickenPrimary?.Active == false).OrderByDescending(p => p.Offense.SheetDps).FirstOrDefault();
                    if (shawang == null)
                    {
                        DPS = players.OrderByDescending(p => p.Offense.SheetDps).FirstOrDefault();
                    }
                    bool cast = false;
                    if (Hud.Game.RiftPercentage < 100)
                    {
                        if(DPS != null && DPS.CoordinateKnown && DPS.CentralXyDistanceToMe < 50)
                        {
                            cast = true;
                        }
                    }else
                    {
                        if(shawang != null && shawang.CoordinateKnown && shawang.CentralXyDistanceToMe < 50)
                        {
                            cast = true;
                        }
                    }
                        return ctx.Skill.Rune == 2 && cast == true;
                }
                ).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Ingeom.Sno) && ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.PrideOfCassius.Sno) && (ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno))).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpireOnParty(Hud.Sno.SnoPowers.Barbarian_IgnorePain, 0, 8000, 8000, Turbo.Plugins.HeroClass.None, 50).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune != 2).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(40, 80).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune != 3 && ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable.Sno, 0)).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.PrideOfCassius.Sno)
                ).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100,500).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianLeap : S7oImportedPolicy
    {
        private IScreenCoordinate PlayerScreenCoordinate = null;
        private IWorldCoordinate LastWorldCoordinate = null;
        private bool moveback = false;
        public S7oPolicy_BarbarianLeap()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_Leap;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastWorldCoordinate.ToScreenCoordinate().X, LastWorldCoordinate.ToScreenCoordinate().Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(612542) >= 4 && !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.LutSocks.Sno) && !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable.Sno)).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 60,ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Barbarian_Leap, 2, 800, 1000).ThenContinueElseNoCast()
                .IfCanCastSkill(800, 800, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    PlayerScreenCoordinate = ctx.Hud.Game.Me.FloorCoordinate.ToScreenCoordinate();
                    LastWorldCoordinate = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    S7oMacroQueries.RequestAim(ctx.Hud, PlayerScreenCoordinate.X, PlayerScreenCoordinate.Y);
                    moveback = true;
                    return true;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianOverPower : S7oImportedPolicy
    {
        public S7oPolicy_BarbarianOverPower()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_Overpower;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 250)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx=>
                {
                    return
                    ctx.Skill.Rune == 3 &&
                    ctx.Skill.Player.Stats.ResourcePctFury <= 30 &&
                    S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 9) >= 5;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return
                    (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4) &&
                    !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Barbarian_Overpower.Sno);
                }).ThenCastElseContinue()
                
                .IfTrue(ctx =>
                {
                    return (ctx.Skill.Rune == 1 || ctx.Skill.Rune == 2) && (S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 9) >= 1);
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianRend : S7oImportedPolicy
	{
        public S7oPolicy_BarbarianRend()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_Rend;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfCanCastSkill(3300, 4400, 500).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(20, ctx => 20)
                .IfEliteOrBossIsNearby(ctx => ctx.Skill.Rune == 0 ? 18 : 12).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => ctx.Skill.Rune == 0 ? 18 : 12, ctx =>
                {
                    if (ctx.Phase == S7oCastPhase.PreAttack)
                    {
                        if (ctx.Skill.Player.Stats.ResourcePctFury >= 60) return 4;
                        if (ctx.Skill.Player.Stats.ResourcePctFury >= 70) return 3;
                        if (ctx.Skill.Player.Stats.ResourcePctFury >= 80) return 2;
                        if (ctx.Skill.Player.Stats.ResourcePctFury >= 90) return 1;
                    }
                    return 5;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_BarbarianSeismicSlam : S7oImportedPolicy
    {
        public S7oPolicy_BarbarianSeismicSlam()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_SeismicSlam;
            Rune = 1;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => 30).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var set = Hud.Game.Me.GetSetItemCount(671068) == 6 && Hud.Game.Me.GetSetItemCount(749637) == 4;
                    return (set);
                }).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1.Sno, 0) && !ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1.Sno, 2)).ThenCastElseContinue()
                .IfSpecificSkillOnCooldown(Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker).ThenContinueElseNoCast()
                .IfPrimaryResourcePercentageIsAbove(98).ThenCastElseContinue()

                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianSprint : S7oImportedPolicy
    {
        public S7oPolicy_BarbarianSprint()
            : base(S7oCastType.BuffSkill,  S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_Sprint;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune != 3).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfCanCastSkill(300, 400, 500).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(40, ctx => 20).ThenContinueElseNoCast()
                .IfRunning(true).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100,300).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfCanCastSkill(300, 400, 500).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(40, ctx => 0).ThenContinueElseNoCast()
                .IfBossIsNearby(ctx => 50).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var players = ctx.Hud.Game.Players.Where(p => !p.IsDead && p.SnoArea.Sno == ctx.Hud.Game.Me.SnoArea.Sno && p.CentralXyDistanceToMe <= 200);
                    return players.All(p => p.CentralXyDistanceToMe <= 50 && !p.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno));
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(500, 1000).ThenCastElseContinue()
                .IfPrimaryResourceIsEnough(70, ctx => 0).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpireOnParty(Hud.Sno.SnoPowers.Barbarian_Sprint, 0, 2000, 2000, Turbo.Plugins.HeroClass.None, 50).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianThreateningShout : S7oImportedPolicy
	{
        public int ActivationRange { get; set; }
        public int DensityLimit { get; set; }

        public S7oPolicy_BarbarianThreateningShout()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
            ActivationRange = 25;
            DensityLimit = 10;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_ThreateningShout;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenNoCastElseContinue()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => ActivationRange).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => ActivationRange, ctx => ctx.Skill.Player.Powers.BuffIsActive(402458, 1) ? 1 : DensityLimit).ThenCastElseContinue();

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfBossIsNearby(ctx => ActivationRange).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    int playerConut = ctx.Hud.Game.Players.Count();
                    bool playIsNearby = playerConut == 1 ? true : ctx.Hud.Game.Players.Where(p => p.HasValidActor && p.NormalizedXyDistanceToMe < ActivationRange && !p.IsMe).Count() >= (playerConut - (playerConut > 2 ? 2 : 1));
                    var monsterWithHighestMonsterDensity = S7oMacroQueries.Monsters(Hud).Where(x => x.IsOnScreen).OrderByDescending(x => x.GetMonsterDensity(ActivationRange)).FirstOrDefault();
                    return (playIsNearby && monsterWithHighestMonsterDensity != null && monsterWithHighestMonsterDensity.NormalizedXyDistanceToMe <= ActivationRange && (monsterWithHighestMonsterDensity.GetMonsterDensity(25) + 1 >= DensityLimit || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 25)));
                }).ThenCastElseContinue();

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.BladeOfTheTribes).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var Animation = Hud.Game.Me.Animation;
                    return
                    Animation == AnimSnoEnum._barbarian_female_dw_leap_attack_contactend ||
                    Animation == AnimSnoEnum._barbarian_male_dw_eq_contactend ||
                    Animation == AnimSnoEnum._barbarian_male_1hs_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_1ht_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_2hs_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_2ht_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_dw_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_stf_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_female_1hs_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_1ht_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_2hs_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_2ht_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_dw_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_stf_seismic_slam
                    ;
                }
                ).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianWarCry : S7oImportedPolicy
	{
        public int CastBelowFuryPercentage { get; set; }

        public S7oPolicy_BarbarianWarCry()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack, S7oCastPhase.Attack)
        {
            Enabled = false;
            CastBelowFuryPercentage = 50;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_WarCry;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(500, 1000).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ChilaniksChain).ThenContinueElseNoCast()
                .IfPrimaryResourcePercentageIsBelow(CastBelowFuryPercentage).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.GetSetItemCount(786990) < 6 && ctx.Hud.Game.Players.All(p => !p.IsDead && p.HasValidActor && p.CentralXyDistanceToMe <= 100 && !p.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_ActorGhostedBuff.Sno)) && S7oMacroQueries.NearestBoss(ctx.Hud) == null;
                }
                ).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Ingeom.Sno) && (ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno))).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpireOnParty(Hud.Sno.SnoPowers.ChilaniksChain, 1, 8000, 8000, Turbo.Plugins.HeroClass.None, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(786990) >= 6).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ChilaniksChain).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.ChilaniksChain, 1, 300, 500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpireOnParty(Hud.Sno.SnoPowers.Barbarian_WarCry, 1, 200, 500, Turbo.Plugins.HeroClass.None, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.BladeOfTheTribes).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var Animation = Hud.Game.Me.Animation;
                    return 
                    Animation == AnimSnoEnum._barbarian_female_dw_leap_attack_contactend ||
                    Animation == AnimSnoEnum._barbarian_male_dw_eq_contactend ||
                    Animation == AnimSnoEnum._barbarian_male_1hs_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_1ht_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_2hs_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_2ht_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_dw_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_male_stf_seismic_slam_long ||
                    Animation == AnimSnoEnum._barbarian_female_1hs_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_1ht_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_2hs_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_2ht_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_dw_seismic_slam ||
                    Animation == AnimSnoEnum._barbarian_female_stf_seismic_slam
                    ;
                }
                ).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_BarbarianWrathOfTheBerserker : S7oImportedPolicy
    {
        public S7oPolicy_BarbarianWrathOfTheBerserker()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting, 0, 500, 2000, true).ThenCastElseContinue()
                .IfBuffIsAboutToExpire(300, 500).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    bool isCOE = ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno);
                    double HighestElementLeft = S7oMacroMechanics.GetHighestElementLeftSecond(hud, ctx.Skill.Player, CoeIndex);
                    var IKset = Hud.Game.Me.GetSetItemCount(671068) >= 4;
                    return (IKset) || (Hud.Game.Me.Powers.UsedPassives.Any(p => p.Sno == Hud.Sno.SnoPowers.Barbarian_Passive_BoonOfBulKathos.Sno) && ctx.Skill.Player.Stats.CooldownReduction >= 0.75) 
                    || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) 
                    
                    || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno) 
                    ||(isCOE ? ((HighestElementLeft <= 16 && HighestElementLeft >= 15) || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Ingeom.Sno)) && S7oMacroQueries.EliteOrBossNear(Hud, 40, false) : S7oMacroQueries.EliteOrBossNear(Hud, 40, false)) 
                    || (ctx.Skill.Player.Defense.HealthPct <= 30)
                        ;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    bool isCOE = ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno);
                    bool NinetySet = Hud.Game.Me.GetSetItemCount(397674) >= 6;
                    bool RaekorSet = Hud.Game.Me.GetSetItemCount(749637) >= 6;
                    return (NinetySet || RaekorSet) && isCOE && S7oMacroQueries.EliteOrBossNear(Hud, 40, false) && S7oMacroMechanics.IsElementReady(ctx.Hud, 0.1, ctx.Skill.Player, CoeIndex) 
                        ;
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_CrusaderAkaratsChampion : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderAkaratsChampion()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_AkaratsChampion;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => !isSanGuang()).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting, 0, 500, 2000, true).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var buff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Crusader_AkaratsChampion.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, 1) : 0.0d;
                    return remaining <= 1d || ctx.Skill.Rune == 2;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var set = Hud.Game.Me.GetSetItemCount(580748);
                    return !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno) && (ctx.Skill.Player.Stats.CooldownReduction >= 0.75 || (ctx.Skill.Player.Stats.CooldownReduction >= 0.5 && (set >= 4 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459) || ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.AkaratsAwakening.Sno))));
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var Crusader_SteedCharge = ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_SteedCharge);
                    bool isNoFatal = (!ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.Crusader_Passive_Indestructible.Sno, 0) || ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.Crusader_Passive_Indestructible.Sno, 1)) &&
                    ctx.Skill.Rune == 3 &&
                    (ctx.Skill.Player.HeroIsHardcore || (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0) || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 20, false) || S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player) || ctx.Skill.Player.Powers.CantMove || (Hud.Game.Me.Defense.HealthPct < (Hud.Game.Me.Powers.HealthPotionSkill.IsOnCooldown ? 60 : 30))) &&
                    (!ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno) || (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno) && cando()))
                    ;
                    
                    return isNoFatal ? true : !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno);
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var isDLegacyOfDreams = ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.LegacyOfDreamsPrimary.Sno);
                    var isAkkhansLeniency = ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.AkkhansLeniency.Sno);
                    var isJekangbord = ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Jekangbord.Sno);
                    return isDLegacyOfDreams && isAkkhansLeniency && isJekangbord && S7oMacroMechanics.IsElementReady(ctx.Hud, 0.1, ctx.Skill.Player, 3);
                }).ThenCastElseContinue()
                .IfPrimaryResourcePercentageIsBelow(20).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 40).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => isSanGuang()).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno, 4) && (!ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno) || (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno) && cando()));
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 200).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => isPaoMaTianQuan()).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return S7oMacroMechanics.IsElementReady(ctx.Hud, 0.1, ctx.Skill.Player, 5);
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(4000, 4500).ThenCastElseContinue()
                ;
        }
        private bool cando()
        {
            var Crusader_SteedCharge = Hud.Game.Me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_SteedCharge);
            return ((!S7oMacroQueries.IsHotKeySet(Hud, ActionKey.Move) || (S7oMacroQueries.IsHotKeySet(Hud, ActionKey.Move) && !S7oMacroQueries.IsActionHeld(Hud, ActionKey.Move))) && (Crusader_SteedCharge != null && !S7oMacroQueries.IsActionHeld(Hud, Crusader_SteedCharge.Key)));
        }

        private bool isPaoMaTianQuan()
        {
            bool isAegisofValor = Hud.Game.Me.GetSetItemCount(192736) >= 6;
            bool isNorvaldsFervor = Hud.Game.Me.Powers.BuffIsActive(447290);
            bool isFistoftheHeavens = S7oMacroMechanics.FindThisSkill(Hud,Hud.Game.Me, Hud.Sno.SnoPowers.Crusader_FistOfTheHeavens.Sno);
            return isAegisofValor && isNorvaldsFervor && isFistoftheHeavens;
        }
        private bool isSanGuang()
        {
            bool isAegisofValor = Hud.Game.Me.GetSetItemCount(192736) >= 6;
            bool isFateoftheFell = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FateOfTheFell.Sno, 0);
            bool isConventionOfElements = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements.Sno, 0);
            return isAegisofValor && isFateoftheFell && isConventionOfElements;
        }
    }


    internal sealed class S7oPolicy_CrusaderBlessedShield : S7oImportedPolicy
	{
        private IScreenCoordinate LastScreenCoordinate = null;
        private bool moveback = false;
        public S7oPolicy_CrusaderBlessedShield()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_BlessedShield;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastScreenCoordinate.X, LastScreenCoordinate.Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfTrue(ctx => S7oMacroMechanics.GetBuffCount(ctx.Hud, Hud.Sno.SnoPowers.Crusader_BlessedShield.Sno, 3) >= 100).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0,ctx => 0).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Crusader_BlessedShield, 3, 1000, 1200).ThenContinueElseNoCast()
                .IfCanCastSkill(200, 200, 3000).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IMonster monster = S7oMacroQueries.Monsters(ctx.Hud).Where(m => m.IsOnScreen && !m.Invisible && !m.Invulnerable && m.CentralXyDistanceToMe <= 50).OrderBy(m => m.NormalizedXyDistanceToMe).FirstOrDefault();
                    if (monster != null)
                    {
                        IScreenCoordinate monsterCoordinate = monster.ScreenCoordinate;
                        LastScreenCoordinate = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                        S7oMacroQueries.RequestAim(ctx.Hud, monsterCoordinate.X, monsterCoordinate.Y);
                        moveback = true;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                    
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_CrusaderBombardment : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderBombardment()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Bombardment;

            CreateCastRule()
               .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Rune == 0).ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 6 && ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_AkaratsChampion)?.Rune == 2).ThenNoCastElseContinue()
               .IfTrue(ctx=> ctx.Skill.Player.GetSetItemCount(220113) >= 4).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno)).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenContinueElseNoCast()
               .IfTrue(ctx =>
               {
                   double LeftTime = S7oMacroMechanics.GetHighestElementLeftSecondAssingedPlayer(hud, ctx.Skill.Player, 6);
                   if (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno))
                   {
                       if(!ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno))
                       {
                           return (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno) &&
                            ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno)) ||
                            (LeftTime >= 15 || LeftTime <= 1)
                             ;
                       }
                       else
                       {
                           return false;
                       }
                   }
                   else
                   {
                       return
                       ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno) &&
                       (LeftTime >= 15 || LeftTime <= 1)
                       ;
                   }
               }
               ).ThenCastElseContinue()
               ;

            CreateCastRule()
               .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfTrue(ctx=> ctx.Skill.Player.GetSetItemCount(220113) >= 4 && !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno)).ThenContinueElseNoCast()
               .IfEnoughMonstersNearbyCursor(ctx => 12, ctx => 1).ThenContinueElseNoCast()
               .IfSpecificBuffIsAboutToExpire(Hud.Sno.GetSnoPower(445639), 1, 500, 1000).ThenCastElseContinue()
               ;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Rune == 0).ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno)).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TheMortalDrama.Sno)
                   ).ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 6 && ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_AkaratsChampion)?.Rune == 2 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Crusader_IronSkin.Sno)).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenCastElseContinue()
               ;
        }
    }


    internal sealed class S7oPolicy_CrusaderCondemn : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderCondemn()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Condemn;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(ctx => (ctx.Hud.Game.Me.Powers.CurrentSkills.Any(s => s.SnoPower.Sno == ctx.Hud.Sno.SnoPowers.Crusader_Phalanx.Sno && s.Rune == 3) && ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.UnrelentingPhalanx.Sno)) ? 40 : 0, ctx => ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.FrydehrsWrath.Sno) ? 40 : 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => (int)(20 * (1 + Hud.Game.Me.Stats.MoveSpeed / 100)) * 3 + ((ctx.Skill.Rune == 1 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.FrydehrsWrath.Sno)) ? 10 : -15), ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_CrusaderFallingSword : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderFallingSword()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_FallingSword;
            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.FaithfulMemory).ThenContinueElseNoCast()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_FallingSword, 9).ThenNoCastElseContinue()
               .IfEnoughMonstersNearbyCursor(ctx => 14, ctx => 1).ThenContinueElseNoCast()
               .IfCanCastSkill(300, 500, 1000).ThenCastElseContinue()
               ;

            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.FaithfulMemory).ThenContinueElseNoCast()
               .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Crusader_FallingSword, 9, 2000, 2300, true).ThenContinueElseNoCast()
               .IfCanCastSkill(300, 500, 1000).ThenCastElseContinue()
               ;
        }
    }


    internal sealed class S7oPolicy_CrusaderFistoftheHeavens : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderFistoftheHeavens()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_FistOfTheHeavens;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => (int)Math.Ceiling(ctx.Skill.GetResourceRequirement())).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfEnoughMonstersNearbyCursor(ctx => 20, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var set = Hud.Game.Me.GetSetItemCount(192736);
                    var yongqi4 = Hud.Game.Me.Powers.GetBuff(483655);
                    return set >= 4 && (S7oMacroData.Count((yongqi4)?.IconCounts, 1) < 50 || S7oMacroData.Seconds((yongqi4)?.TimeLeftSeconds, 1) < 0.5);
                }).ThenCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var set = Hud.Game.Me.GetSetItemCount(192736);
                    var yongqi2 = Hud.Game.Me.Powers.GetBuff(483643);
                    return ((ctx.Skill.Player.Powers.CurrentSkills.Any(s => s.SnoPower.Sno == hud.Sno.SnoPowers.Crusader_HeavensFury.Sno) && set >= 2 && (S7oMacroData.Count((yongqi2)?.IconCounts, 1) < 3 || S7oMacroData.Seconds((yongqi2)?.TimeLeftSeconds, 1) < 0.5)));
                }).ThenCastElseContinue()
                ;
                

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => (int)Math.Ceiling(ctx.Skill.GetResourceRequirement())).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(956353) >= 2).ThenContinueElseNoCast()
                .IfEnoughMonstersNearbyCursor(ctx => 20, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 2, 1000, 1000).ThenCastElseContinue()
                ;
        }
    }



    internal sealed class S7oPolicy_CrusaderIronSkin : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderIronSkin()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_IronSkin;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3
                   ).ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_IronSkin, 0).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0)).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player)).ThenCastElseContinue()
                .IfHealthWarning(60, 80).ThenCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Player.Stats.CooldownReduction >= 0.5 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459))).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Crusader_IronSkin, 0, 50, 200).ThenCastElseContinue()
                ;
            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 6 && ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_AkaratsChampion)?.Rune == 2).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Rune == 3 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TheMortalDrama.Sno) &&
                   ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno)
                   ).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenContinueElseNoCast()
               .IfTrue(ctx =>
               {
                   return 
                   S7oMacroMechanics.IsElementReady(hud, 3, ctx.Skill.Player, 6) || 
                   (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno) && !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno)
                   );
               }
               ).ThenCastElseContinue()
               ;

            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno)).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Rune == 3 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TheMortalDrama.Sno)
                   ).ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 6 && ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Crusader_AkaratsChampion)?.Rune == 2).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenCastElseContinue()
               ;

            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfTrue(ctx =>ctx.Skill.Player.GetSetItemCount(220113) >= 6).ThenContinueElseNoCast()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_IronSkin, 0).ThenNoCastElseContinue()
               .IfEnoughMonstersNearby(ctx => 60, ctx => 1).ThenCastElseContinue()
               ;

        }
    }


    internal sealed class S7oPolicy_CrusaderJudgment : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderJudgment()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Judgment;
            CreateCastRule()
                .IfCanCastSkill(500, 1000, 15000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 2).ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = ctx.Hud.Window.CreateScreenCoordinate(ctx.Hud.Window.CursorX, ctx.Hud.Window.CursorY).ToWorldCoordinate();
                    bool Result = S7oMacroQueries.Monsters(ctx.Hud).Any(m => ((m.IsElite && m.Rarity != ActorRarity.RareMinion) || m.SnoMonster.Priority == MonsterPriority.goblin) && m.FloorCoordinate.XYZDistanceTo(cursor) <= 20 && !m.Invulnerable && !m.Invisible && !m.Illusion &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_None, 267600) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_A, 267600) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_B, 267600) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_C, 267600) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_D, 267600) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_0_Visual_Effect_E, 267600) != 1
                    );
                    return Result;
                }).ThenCastElseContinue()
                ;
            CreateCastRule()
               .IfCanCastSkill(100, 100, 15000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 2 && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking).ThenContinueElseNoCast()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
               .IfTrue(ctx =>
               {
                   IWorldCoordinate cursor = ctx.Hud.Window.CreateScreenCoordinate(ctx.Hud.Window.CursorX, ctx.Hud.Window.CursorY).ToWorldCoordinate();
                   bool isJudgment = S7oMacroQueries.Monsters(ctx.Hud).Any(m => m.FloorCoordinate.XYZDistanceTo(cursor) <= 20 && !m.Invulnerable && !m.Invisible && !m.Illusion
                   );
                   return isJudgment;
               }).ThenCastElseContinue()
               ;
        }
    }


    internal sealed class S7oPolicy_CrusaderJustice : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderJustice()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Justice;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersInSector(ctx => ctx.Skill.Rune == 2 ? 70 : 30, ctx => 80, ctx => 4.2f, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 1500, 1500).ThenContinueElseNoCast()
                .IfCanCastSkill(90, 90, 3000).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_CrusaderLawsOfHope : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderLawsOfHope()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_LawsOfHope;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0)).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player)).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(70, 80).ThenCastElseContinue()
                .IfNearbyPartyMemberIsInDanger(48, 40, 80, 40, true).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_X1CrusaderLawsOfHopePassive2, 6).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                ctx.Hud.Game.Me.Powers.BuffIsActive(310678) ||
                ctx.Skill.Player.Stats.CooldownReduction >= 0.5 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459)
                ).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0)).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player)).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(70, 80).ThenCastElseContinue()
                .IfNearbyPartyMemberIsInDanger(48, 40, 80, 40, true).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.SquirtsNecklace).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                ctx.Hud.Game.Me.Powers.BuffIsActive(310678) ||
                ctx.Skill.Player.Stats.CooldownReduction >= 0.5 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459)
                ).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_CrusaderLawsOfJustice : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderLawsOfJustice()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_LawsOfJustice;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0)).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player)).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(70, 80).ThenCastElseContinue()
                .IfNearbyPartyMemberIsInDanger(48, 40, 80, 40, true).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_X1CrusaderLawsOfJusticePassive2, 6).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                ctx.Hud.Game.Me.Powers.BuffIsActive(310678) ||
                ctx.Skill.Player.Stats.CooldownReduction >= 0.5 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459)
                ).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;
        }
    }




    internal sealed class S7oPolicy_CrusaderLawsOfValor : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderLawsOfValor()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }
        public HashSet<uint> ReflectMonsters = new HashSet<uint>()
        {
			
			5191, 
			5192, 
			3981, 
			3982, 
			256022, 
			344389, 
			418900, 
			297730, 

			
			116075, 
			26245, 
			116305, 
			196846, 
			26066, 
			26067, 
			57063, 
			57064, 
			418901, 
		};
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_LawsOfValor;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                (ctx.Hud.Game.Me.Powers.BuffIsActive(310678) ||
                ctx.Skill.Player.Stats.CooldownReduction >= 0.5 || ctx.Hud.Game.Me.Powers.BuffIsActive(402459)) && 
                (!isSanGuang() ||
                (isSanGuang() && (ctx.Hud.Game.RiftPercentage < 100 || !ctx.Skill.Player.InGreaterRift)))
                ).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_X1CrusaderLawsOfValorPassive2, 6, 200, 500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx=> ctx.Skill.Rune == 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                (ctx.Hud.Game.Me.Powers.BuffIsActive(402459) ? ctx.Skill.Player.Stats.CooldownReduction >= 0.5 : ctx.Skill.Player.Stats.CooldownReduction >= 0.7) && 
                (!isSanGuang() ||
                (isSanGuang() && (ctx.Hud.Game.RiftPercentage < 100 || !ctx.Skill.Player.InGreaterRift)))
                ).ThenContinueElseNoCast()
                .IfEliteIsNearby(ctx => 10).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.Monsters(Hud).Any(m =>m.CollisionCoordinate.XYDistanceTo(ctx.Skill.Player.CollisionCoordinate) < 10 && ReflectMonsters.Contains(m.SnoMonster.Sno))).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                (isSanGuang() && ctx.Hud.Game.RiftPercentage == 100 && S7oMacroQueries.EliteOrBossNear(ctx.Hud, 30) && (S7oMacroMechanics.IsElementReady (Hud, 0.5, ctx.Skill.Player, 4) || 
                (ctx.Hud.Game.Me.Powers.BuffIsActive(310678) &&
                ctx.Skill.Player.Stats.CooldownReduction >= 0.6518)))
                ).ThenCastElseContinue()
                ;
        }

        private bool isSanGuang()
        {
            bool isHeavensFury = Hud.Game.Me.Powers.UsedCrusaderPowers.HeavensFury?.Rune == 4; 
            bool isAegisofValor = Hud.Game.Me.GetSetItemCount(192736) >= 6;
            bool isFateoftheFell = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FateOfTheFell.Sno, 0);
            bool isConventionOfElements = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements.Sno, 0);
            return isAegisofValor && isHeavensFury && isFateoftheFell && isConventionOfElements;
        }
    }


    internal sealed class S7oPolicy_CrusaderPhalanx : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderPhalanx()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Phalanx;
            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(580748) >= 2 && Hud.Game.Me.AnimationState == AcdAnimationState.Attacking).ThenCastElseContinue()
               ;
        }
    }


    internal sealed class S7oPolicy_CrusaderProvoke : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderProvoke()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_Provoke;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && ctx.Skill.Player.GetSetItemCount(220113) >=2 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno) && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TheMortalDrama.Sno) && ctx.Skill.Player.Powers.CurrentSkills.Any(x => x?.SnoPower == ctx.Hud.Sno.SnoPowers.Crusader_IronSkin && x?.Rune == 3)
                ).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction >= 0.5).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var v = (1 - (30 + 5 * S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 10)) / ctx.Skill.Player.Stats.ResourceMaxWrath) * 100;
                    return
                        (ctx.Skill.Player.Stats.ResourcePctWrath <= (v > 0 ? v : 10));
                }).ThenCastElseContinue()
                .IfHealthWarning(70,80).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && ctx.Skill.Player.GetSetItemCount(220113) >= 2 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno) && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TheMortalDrama.Sno) && ctx.Skill.Player.Powers.CurrentSkills.Any(x => x?.SnoPower == ctx.Hud.Sno.SnoPowers.Crusader_IronSkin && x?.Rune == 3)
                ).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_IronSkin, 0).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_CrusaderShieldGlare : S7oImportedPolicy
	{
        public S7oPolicy_CrusaderShieldGlare()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_ShieldGlare;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    bool enemiesAround = S7oMacroQueries.Monsters(ctx.Hud).Any(m => (m.IsElite || m.SnoMonster.Priority == MonsterPriority.goblin || m.Rarity == ActorRarity.Boss) && m.Rarity != ActorRarity.RareMinion && !m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut) && !m.Blind && !m.Illusion && !m.Invisible && !m.Invulnerable && m.NormalizedXyDistanceToMe <= 30 && 
                    (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.TheFinalWitness.Sno) ? true : 
                    S7oMacroMechanics.isMobInSkillRange(ctx.Hud, m, 40, 30, 0)));
                    return 
                    ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno)
                    && S7oMacroMechanics.IsElementReady(ctx.Hud, 0.5, ctx.Skill.Player, S7oMacroMechanics.Options.CoeIndex)
                    && enemiesAround
                    ;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_CrusaderSteedCharge : S7oImportedPolicy
	{
        public bool NotWorkForDrawandQuarter { get; set; }
        private bool initialized = false;
        public S7oPolicy_CrusaderSteedCharge()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Crusader_SteedCharge;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => NotWorkForDrawandQuarter && ctx.Skill.Rune == 4).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Darklight.Sno) && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.VigilanteBelt.Sno)).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(447290)).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroQueries.Monsters(ctx.Hud).Any(m =>(m.CurHealth / m.MaxHealth) < 0.99 && m.CentralXyDistanceToMe < (ctx.Skill.Player.Powers.BuffIsActive(403468, 0) ? 60 : 15))).ThenContinueElseNoCast()
                .IfFalse(ctx => ctx.Skill.Player.Powers.BuffIsActive(447291,1)).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Crusader_SteedCharge).ThenNoCastElseContinue()
                .IfTrue(ctx => NotWorkForDrawandQuarter && ctx.Skill.Rune == 4).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Darklight.Sno) && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.VigilanteBelt.Sno)).ThenContinueElseNoCast()
                .IfTrue(ctx => 
                {
                    if(initialized == false)
                    {
                        initialized = true;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                    }).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Crusader_SteedCharge.Sno)?.LastActive.ElapsedMilliseconds > 200).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterChakramShurikenCloud : S7oImportedPolicy
	{
        public S7oPolicy_DemonHunterChakramShurikenCloud()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Chakram;
            Rune = 4;

            CreateCastRule()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(70, ctx => 10).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(20000, 50000).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_DemonHunterBolas : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterBolas()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Bolas;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersInSector(ctx => ctx.Skill.Rune == 1 ? 30 : 15, ctx => 80, ctx => 4.2f, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 30, 100).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Powers.UsedLegendaryPowers.WrapsOfClarity?.Active == true).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable, 0).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 200, ctx =>1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.WrapsOfClarity, 1, 30, 300).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterCaltrops : S7oImportedPolicy
	{
        public S7oPolicy_DemonHunterCaltrops()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Caltrops;

            CreateCastRule()
               .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(635131) >= 2 && ctx.Skill.Player.Powers.UsedDemonHunterPowers.SpikeTrap != null).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_female_cast_spiketrap_01 || ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_male_cast_spiketrap_01).ThenCastElseContinue()
               .IfEliteOrBossIsNearby(ctx => 12).ThenCastElseContinue()
               ;

            CreateCastRule()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfOnCooldown().ThenNoCastElseContinue()
               .IfCanCastSimple().ThenContinueElseNoCast()
               .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
               .IfTrue(ctx => ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_male_cast_impale_01 || ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_female_cast_impale_01).ThenContinueElseNoCast()
               .IfTrue(ctx => {
                   var Vault = Hud.Game.Me.Powers.UsedDemonHunterPowers.Vault;
                   if(Vault == null)
                   {
                       return false;
                   }
                   return S7oMacroQueries.IsActionHeld(Hud, Vault.Key);
                   }).ThenNoCastElseContinue()
               .IfCanCastSkill(500,500,1000).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => 60, ctx => 1).ThenCastElseContinue()
               ;

        }
    }



    internal sealed class S7oPolicy_DemonHunterCompanion : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterCompanion()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Companion;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction >= 0.69 || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno)).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3 && ctx.Skill.Player.Stats.ResourcePctHatred < 15
                ).ThenCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Rune == 1 || ctx.Skill.Rune == 0 || ctx.Skill.Rune == 255) && S7oMacroQueries.EliteOrBossNear(ctx.Hud, 40)
                ).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    int PartyCoeIndex = S7oMacroMechanics.Options.PartyCoeIndex;
                    if (ctx.Skill.Rune != 2 && Hud.Game.Me.GetSetItemCount(254427) < 2) return false;
                    bool _cast;
                    var DPSPlayer = ctx.Hud.Game.Players.FirstOrDefault(p => p.InGreaterRift &&
                p.Powers.UsedLegendaryPowers.ConventionOfElements?.Active == true
                );

                    if (DPSPlayer != null)
                    {
                        double CoeLeftTime = S7oMacroMechanics.GetHighestElementLeftSecondAssingedPlayer(Hud, DPSPlayer, PartyCoeIndex);
                        _cast = CoeLeftTime < 6 && CoeLeftTime > 0;
                    }
                    else if (Hud.Game.Me.Powers.BuffIsActive(430674))
                    {
                        double CoeLeftTime = S7oMacroMechanics.GetHighestElementLeftSecond(Hud, ctx.Skill.Player, CoeIndex);
                        _cast = CoeLeftTime < 6 && CoeLeftTime > 0 && S7oMacroQueries.EliteOrBossNear(ctx.Hud, 40);
                    }
                    else
                    {
                        _cast = S7oMacroQueries.EliteOrBossNear(ctx.Hud, 40, false);
                    }
                    return _cast;
                }).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && S7oMacroQueries.NearestHealthGlobe(ctx.Hud) != null && S7oMacroQueries.NearestHealthGlobe(ctx.Hud).NormalizedXyDistanceToMe <= 60 && ctx.Skill.Player.Defense.HealthPct < 60
                ).ThenCastElseContinue()
                .IfTrue(ctx => Hud.Game.Me.GetSetItemCount(254427) >= 2 && !Hud.Game.Me.Powers.BuffIsActive(430674) &&(ctx.Skill.Player.Stats.ResourcePctHatred < 15 || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 40) || (S7oMacroQueries.NearestHealthGlobe(ctx.Hud) != null && S7oMacroQueries.NearestHealthGlobe(ctx.Hud).NormalizedXyDistanceToMe <= 60 && ctx.Skill.Player.Defense.HealthPct < 60))
                ).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterEntanglingShot : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterEntanglingShot()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_EntanglingShot;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersInSector(ctx => 15, ctx => 80, ctx => 6.9f, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 30, 100).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Powers.UsedLegendaryPowers.WrapsOfClarity?.Active == true).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable, 0).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 200, ctx =>1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.WrapsOfClarity, 1, 30, 300).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterEvasiveFire : S7oImportedPolicy
	{
        private List<uint> SpiketrapIds = new List<uint>{};
        private List<uint> OldIds = new List<uint> {};
        public S7oPolicy_DemonHunterEvasiveFire()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        private int getSpikeTrapMax()
        {
            int i = 4;
            if(Hud.Game.Me.Powers.UsedDemonHunterPowers.CustomEngineering != null)
            {
                i = 5;
            }
            if(Hud.Game.Me.Powers.UsedLegendaryPowers.TragOulCoils?.Active == true)
            {
                i = i * 2;
            }

            return i;
        }



        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_EvasiveFire;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4) && ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersInSector(ctx => 50, ctx => 80, ctx => 6.9f, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 30, 100).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4) && Hud.Game.Me.Powers.UsedLegendaryPowers.WrapsOfClarity?.Active == true).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable, 0).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 200, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.WrapsOfClarity, 1, 30, 300).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting, 0).ThenNoCastElseContinue()
                .IfPrimaryResourcePercentageIsBelow(20).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4) && Hud.Game.Me.GetSetItemCount(254164) >= 2).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting, 0).ThenNoCastElseContinue()
                .IfSecondaryResourcePercentageIsBelow(33).ThenCastElseContinue()
                ;

            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0 || ctx.Skill.Rune == 4) && Hud.Game.Me.GetSetItemCount(635131) >= 2).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.UsedDemonHunterPowers.SpikeTrap?.Rune == 2).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    
                    var Actors = S7oMacroQueries.Actors(Hud).Where(A => A.SnoActor.Sno == ActorSnoEnum._demonhunter_spiketrap_proxy && A.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId);
                    var SpikeTraps = Actors.Where(A => !OldIds.Contains(A.AnnId) && !SpiketrapIds.Contains(A.AnnId));
                    foreach (var SpikeTrap in SpikeTraps)
                    {
                        SpiketrapIds.Add(SpikeTrap.AnnId);
                    }
                    if(SpiketrapIds.Count() >= getSpikeTrapMax())
                    {
                        foreach(var ids in SpiketrapIds)
                        {
                            OldIds.Add(ids);

                        }
                        SpiketrapIds.Clear();
                        for(int i = OldIds.Count - 1; i >= 0; i--)
                        {
                            if (Actors.Any(x => x.AnnId == OldIds[i]) == false)
                            {
                                OldIds.Remove(OldIds[i]);
                            }
                        }
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfCanCastSkill(500, 500 ,5000).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterFanofKnives : S7oImportedPolicy
	{
        private List<uint> SpiketrapIds = new List<uint> { };
        private List<uint> OldIds = new List<uint> { };
        public S7oPolicy_DemonHunterFanofKnives()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        private int getSpikeTrapMax()
        {
            int i = 4;
            if (Hud.Game.Me.Powers.UsedDemonHunterPowers.CustomEngineering != null)
            {
                i = 5;
            }
            if (Hud.Game.Me.Powers.UsedLegendaryPowers.TragOulCoils?.Active == true)
            {
                i = i * 2;
            }

            return i;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_FanOfKnives;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 200).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 40, ctx => 1).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction * 100 > 40).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 0) && Hud.Game.Me.GetSetItemCount(635131) >= 6).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.UsedDemonHunterPowers.SpikeTrap?.Rune != 2).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    
                    var Actors = S7oMacroQueries.Actors(Hud).Where(A => (A.SnoActor.Sno == ActorSnoEnum._demonhunter_spiketrap_proxy || A.SnoActor.Sno == ActorSnoEnum._demonhunter_spiketraprune_multitrap_proxy || A.SnoActor.Sno == ActorSnoEnum._demonhunter_spiketraprune_damage_proxy || A.SnoActor.Sno == ActorSnoEnum._demonhunter_spiketraprune_chainlightning_proxy) && A.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId);
                    var SpikeTraps = Actors.Where(A => !OldIds.Contains(A.AnnId) && !SpiketrapIds.Contains(A.AnnId));
                    foreach (var SpikeTrap in SpikeTraps)
                    {
                        SpiketrapIds.Add(SpikeTrap.AnnId);
                    }
                    if (SpiketrapIds.Count() >= getSpikeTrapMax())
                    {
                        foreach (var id in SpiketrapIds)
                        {
                            OldIds.Add(id);

                        }
                        SpiketrapIds.Clear();
                        for (int i = OldIds.Count - 1; i >= 0; i--)
                        {
                            if (Actors.Any(x => x.AnnId == OldIds[i]) == false)
                            {
                                OldIds.Remove(OldIds[i]);
                            }
                        }
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfCanCastSkill(500, 500, 5000).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_DemonHunterHungeringArrow : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterHungeringArrow()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_HungeringArrow;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersInSector(ctx => 40, ctx => 80, ctx => 6.9f , ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 100, 300).ThenCastElseContinue()
                ;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Powers.UsedLegendaryPowers.WrapsOfClarity?.Active == true).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable, 0).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 200, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.WrapsOfClarity, 1, 30, 300).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterImpale : S7oImportedPolicy
	{
        public S7oPolicy_DemonHunterImpale()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Impale;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfAttacking().ThenNoCastElseContinue()
                .IfTrue(ctx => Hud.Game.Me.Powers.UsedDemonHunterPowers.Vault != null).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ChainOfShadows, 0).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0 , ctx => 0).ThenContinueElseNoCast()
                .IfSecondaryResourcePercentageIsBelow(90).ThenContinueElseNoCast()
				.IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.ChainOfShadows, 1, 30, 100).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_DemonHunterMarkedforDeath : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterMarkedforDeath()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_MarkedForDeath;

            CreateCastRule()
                .IfCanCastSkill(300, 500, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfTrue(ctx => {
                    var cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var isAnyNotMarkedMonster = S7oMacroQueries.Monsters(Hud).Any(m => (m.Rarity == ActorRarity.Boss || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Unique || m.SnoMonster.Priority == MonsterPriority.goblin || m.SnoMonster.Priority == MonsterPriority.keywarden) && !m.Invisible && !m.Invulnerable && !m.Illusion && !m.MarkedForDeath && (m.FloorCoordinate.XYZDistanceTo(cur.ToWorldCoordinate()) - m.RadiusBottom) <= 10);
                    return isAnyNotMarkedMonster; 
                }).ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune != 2).ThenContinueElseNoCast()
                .IfTrue(ctx => {

                    var SelectedMonster = ctx.Hud.Game.SelectedMonster2;
                    if (SelectedMonster == null)
                        return false;
                    return (SelectedMonster.Rarity == ActorRarity.Boss || SelectedMonster.Rarity == ActorRarity.Rare || SelectedMonster.Rarity == ActorRarity.Champion || SelectedMonster.Rarity == ActorRarity.Unique || SelectedMonster.SnoMonster.Priority == MonsterPriority.goblin || SelectedMonster.SnoMonster.Priority == MonsterPriority.keywarden) && !SelectedMonster.Invisible && !SelectedMonster.Invulnerable && !SelectedMonster.Illusion && !SelectedMonster.MarkedForDeath;
                }).ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenCastElseContinue()
                ;
            ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterMultishot : S7oImportedPolicy
    {
        public int MinMobs { get; set; }
        public S7oPolicy_DemonHunterMultishot()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
            MinMobs = 10;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Multishot;

            CreateCastRule()
                .IfCanCastSkill(500, 1000, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.IceblinkSecondary).ThenContinueElseNoCast()
                .IfTrue(ctx => {
                    var isNotColdMonster = S7oMacroQueries.Monsters(ctx.Hud).Count(m => !m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut) && !m.Invulnerable && !m.Invisible && m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 428354) != 1 && S7oMacroMechanics.isMobInSkillRange(ctx.Hud, m, 40, 80, 6.2f));
                    return isNotColdMonster >= MinMobs;
                }).ThenCastElseContinue()
                .IfTrue(ctx => {
                    var isAnyEliteNotCold = S7oMacroQueries.Monsters(Hud).Any(m => !m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut) && (m.Rarity == ActorRarity.Boss || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Unique || m.SnoMonster.Priority == MonsterPriority.goblin || m.SnoMonster.Priority == MonsterPriority.keywarden) && !m.Invisible && !m.Invulnerable && !m.Illusion && m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 428354) != 1 && S7oMacroMechanics.isMobInSkillRange(ctx.Hud, m, 40, 80, 6.2f));
                    return isAnyEliteNotCold;
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_DemonHunterPreparation : S7oImportedPolicy
    {
        public int ResourceConsumed { get; set; }
        public S7oPolicy_DemonHunterPreparation()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
            ResourceConsumed = 30;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Preparation;

            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 0).ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 3 && ctx.Skill.Player.Defense.HealthPct <= 40).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return (ctx.Skill.Player.Stats.ResourceCurDiscipline <= ctx.Skill.Player.Stats.ResourceMaxDiscipline - ResourceConsumed);
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_DemonHunterPreparationPunishment : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterPreparationPunishment()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Preparation;
            Rune = 0;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Stats.ResourceCurPri <= Hud.Game.Me.Stats.ResourceMaxPri - 75.0f).ThenCastElseContinue()
                ;
        }
    }




    internal sealed class S7oPolicy_DemonHunterSentry : S7oImportedPolicy
	{
        public S7oPolicy_DemonHunterSentry()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Sentry;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Charges == 0)
                        return false;
                    if (ctx.Skill.Rune != 3)
                        return false;
                    IWorldCoordinate cursor = ctx.Hud.Window.CreateScreenCoordinate(ctx.Hud.Window.CursorX, ctx.Hud.Window.CursorY).ToWorldCoordinate();
                    bool BaneOfTheTrapped = ctx.Skill.Player.Powers.UsedLegendaryGems.BaneOfTheTrappedPrimary?.Active == true;
                    int Elites = S7oMacroQueries.Monsters(ctx.Hud).Where(m => m.IsElite && m.Rarity != ActorRarity.RareMinion && !m.Slow && !m.Stunned && !m.Frozen && !m.Chilled && !m.Blind && m.FloorCoordinate.XYZDistanceTo(cursor) <= 16 && !m.AffixSnoList.Any(a=> a.Affix == MonsterAffix.Juggernaut)).Count();
                    return BaneOfTheTrapped && Elites > 0;
                }).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 60, false).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Charges == 0) return false;
                    bool set = true;
                    int Sentrys = S7oMacroQueries.Actors(ctx.Hud).Where(a =>
                    a.CentralXyDistanceToMe <= 60 && a.SummonerAcdDynamicId == ctx.Hud.Game.Me.SummonerId
                    &&
                    (
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry ||
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry_addsmissiles ||
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry_addsduration ||
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry_tether ||
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry_addsheals ||
                    a.SnoActor.Sno == ActorSnoEnum._dh_sentry_addsshield
                    )
                    ).Count();
                    return set && Sentrys < GetMaxSentrys();
                }).ThenCastElseContinue()
                ;
        }

        private int GetMaxSentrys()
        {
            int Sentrys = 2;
            if(Hud.Game.Me.Powers.UsedPassives.Any(s => s.Sno == Hud.Sno.SnoPowers.DemonHunter_Passive_CustomEngineering.Sno))
            {
                Sentrys = 3;
            }
            if(Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.BombardiersRucksack.Sno) || Hud.Game.Me.Powers.BuffIsActive(318804))
            {
                Sentrys = Sentrys + 2;
            }
            return Sentrys;
        }
    }


    internal sealed class S7oPolicy_DemonHunterShadowPower : S7oImportedPolicy
    {
        public bool ElusiveRingEnabled { get; set; } = true;
        public bool LifeOnHitEnabled { get; set; } = true;
        public S7oPolicy_DemonHunterShadowPower()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
            ElusiveRingEnabled = false;
            LifeOnHitEnabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_ShadowPower;

            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.DemonHunter_ShadowPower.Sno)).ThenNoCastElseContinue()
                .IfTrue(ctx => Hud.Game.Me.GetSetItemCount(916931) >= 4).ThenCastElseContinue()
                .IfFalse(ctx =>
                {
                    var lotsOfDiscipline = (ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null) &&
                                            (ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation.Rune != 0) &&
                                            !ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation.IsOnCooldown; 

                    return (lotsOfDiscipline && (ctx.Skill.Player.Defense.HealthPct <= 65)) || LifeInWarning(ctx.Skill.Player);
                }).ThenNoCastElseContinue()
                .IfBuffIsAboutToExpire(30, 100).ThenCastElseContinue();

            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.DemonHunter_ShadowPower.Sno)).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 1 && (ctx.Skill.Player.GetSetItemCount(791249) >= 6 || ctx.Skill.Player.GetSetItemCount(254164) >= 6) && ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null).ThenContinueElseNoCast()
                .IfRunning(true).ThenCastElseContinue()
                ;

            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.DemonHunter_ShadowPower.Sno)).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 2 && (ctx.Skill.Player.GetSetItemCount(791249) >= 6 || ctx.Skill.Player.GetSetItemCount(254164) >= 6) && ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenCastElseContinue()
                ;

            
            CreateCastRule()
                .IfTrue(ctx => ElusiveRingEnabled && ctx.Skill.Player.Powers.UsedLegendaryPowers.ElusiveRing?.Active == true).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 40, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.ElusiveRing, 1, 30, 100).ThenCastElseContinue();

            
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    var buff = Hud.Game.Me.Powers.GetBuff(hud.Sno.SnoPowers.Generic_X1LegendaryGenericPotionPowerup.Sno);
                    return (LifeOnHitEnabled && buff != null && S7oMacroData.Seconds((buff)?.TimeLeftSeconds, 4) >= 4.6);
                }
                ).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenCastElseContinue();


            
            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(635131) >= 2 && ctx.Skill.Player.Powers.UsedDemonHunterPowers.SpikeTrap != null).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_female_cast_spiketrap_01 || ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_male_cast_spiketrap_01).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenCastElseContinue()
                ;

            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && ctx.Skill.Player.GetSetItemCount(916931) >= 4 && (Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_male_cast_impale_01 || Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_female_cast_impale_01)).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 30).ThenContinueElseNoCast()
                .IfCanCastSkill(4000, 5000, 10000).ThenCastElseContinue()
                ;
        }
        private bool LifeInWarning(IPlayer player)
        {
            return player.Defense.HealthPct <= 30 || (player.Powers.HealthPotionSkill.IsOnCooldown && (player.Defense.HealthPct <= 85))
                || (Hud.Game.IsEliteOnScreen && (player.Defense.HealthPct <= 75 || S7oMacroQueries.DangerCount(Hud, player) > 1));
        }
    }



    internal sealed class S7oPolicy_DemonHunterSmokeScreen : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterSmokeScreen()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_SmokeScreen;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.DangerCount(ctx.Hud, ctx.Hud.Game.Me)>0)).ThenCastElseContinue()
                .IfTrue(ctx => S7oMacroQueries.InstantDanger(ctx.Hud, ctx.Skill.Player)).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(60, 80).ThenCastElseContinue();

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.UsedLegendaryPowers.ElusiveRing?.Active == true).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.ElusiveRing, 1, 100, 150).ThenCastElseContinue();

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 3 && (ctx.Skill.Player.Stats.ResourceCostReduction > 0.8 ? true : ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null)).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 10).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 80).ThenCastElseContinue()
               ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 3 && (ctx.Skill.Player.Stats.ResourceCostReduction > 0.8 ? true : ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null)).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenContinueElseNoCast()
                .IfCanCastSkill(2800, 3000, 5000).ThenCastElseContinue()
               ;

            CreateCastRule()
                 .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                 .IfTrue(ctx =>
                 {
                     float ResourceCostReduction = ctx.Skill.Player.Stats.ResourceCostReduction;
                     return ctx.Skill.Rune == 4 && ResourceCostReduction >= 0.4 && (ResourceCostReduction > 0.8 ? true : ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null);
                     }
                 ).ThenContinueElseNoCast()
                 .IfInTown().ThenNoCastElseContinue()
                 .IfCastingIdentify().ThenNoCastElseContinue()
                 .IfCastingPortal().ThenNoCastElseContinue()
                 .IfOnCooldown().ThenNoCastElseContinue()
                 .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                 .IfCanCastSimple().ThenContinueElseNoCast()
                 .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                 .IfRunning(true).ThenCastElseContinue()
                 .IfEliteOrBossIsNearby(ctx => 80).ThenCastElseContinue()
                 ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && (ctx.Skill.Player.Stats.ResourceCostReduction > 0.8 ? true : ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null)).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfRunning(true).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 80).ThenCastElseContinue()
               ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && (ctx.Skill.Player.Stats.ResourceCostReduction > 0.8 ? true : ctx.Skill.Player.Powers.UsedDemonHunterPowers.Preparation != null)).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSecondaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfRunning(true).ThenContinueElseNoCast()
                .IfCanCastSkill(2800, 3000, 5000).ThenCastElseContinue()
               ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && ctx.Skill.Player.Powers.UsedLegendaryPowers.Ingeom?.Active == true).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 80, ctx => 1).ThenCastElseContinue()
               ;


            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(635131) >= 2 && ctx.Skill.Player.Powers.UsedDemonHunterPowers.SpikeTrap != null).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_female_cast_spiketrap_01 || ctx.Hud.Game.Me.Animation == AnimSnoEnum._demonhunter_male_cast_spiketrap_01).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 80).ThenCastElseContinue()
               ;

        }
    }


    internal sealed class S7oPolicy_DemonHunterVengeance : S7oImportedPolicy
    {
        public S7oPolicy_DemonHunterVengeance()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.DemonHunter_Vengeance;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Skill.Rune == 1 || ctx.Skill.Rune == 3) && ctx.Skill.Player.Defense.HealthPct < 60).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(50, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction * 100 > 75).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(50, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 100, false).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements).ThenContinueElseNoCast()
                .IfTrue(ctx => {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    double HighestElementLeft = S7oMacroMechanics.GetHighestElementLeftSecond(hud, ctx.Skill.Player, CoeIndex);
                    return HighestElementLeft <= 16 && HighestElementLeft >= 15;
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(402459) || ctx.Skill.Player.Powers.BuffIsActive(446146, 0)).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 60).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 60, ctx => 10).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction * 100 > 29).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(50, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(402459) || ctx.Skill.Player.Powers.BuffIsActive(446146, 0)).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction * 100 > 36).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(50, 100).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(402459) || ctx.Skill.Player.Powers.BuffIsActive(446146, 0)).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsBelow(ctx => 30).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(50, 100).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkBlindingFlash : S7oImportedPolicy
	{
        public S7oPolicy_MonkBlindingFlash()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_BlindingFlash;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfBossIsNearby(ctx => 20).ThenNoCastElseContinue()
                .IfEliteIsNearby(ctx => 20).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 20, ctx => 5).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.TheLawsOfSeph).ThenContinueElseNoCast()
                .IfPrimaryResourcePercentageIsBelow(20).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(1044700) >= 6).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroMechanics.IsElementReady(ctx.Hud, 0.1, ctx.Skill.Player, 2)).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkBreathOfHeaven : S7oImportedPolicy
	{
        public S7oPolicy_MonkBreathOfHeaven()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_BreathOfHeaven;
            
            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfHealthPercentageIsBelow(ctx => 70).ThenCastElseContinue()
                .IfNearbyPartyMemberIsInDanger(12, 70, 80, 70, true).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenCastElseContinue()
                .IfPrimaryResourcePercentageIsBelow(50).ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Hud.Game.Me.Stats.CooldownReduction > 0.65 && (ctx.Hud.Game.Me.Powers.BuffIsActive(402459) || ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.Ingeom.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.MesserschmidtsReaver.Sno))) || ctx.Hud.Game.Me.Stats.CooldownReduction > 0.73)
                ;
            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 40).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Monk_BreathOfHeaven, 0, 50, 100).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => (ctx.Hud.Game.Me.Stats.CooldownReduction > 0.65 && (ctx.Hud.Game.Me.Powers.BuffIsActive(402459) || ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.Ingeom.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.MesserschmidtsReaver.Sno))) || ctx.Hud.Game.Me.Stats.CooldownReduction > 0.73).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkCripplingWave : S7oImportedPolicy
    {
        public S7oPolicy_MonkCripplingWave()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_CripplingWave;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.GetSetItemCount(755275) >= 6 && ctx.Skill.Player.GetSetItemCount(563257) >= 2)
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (Hud.Game.Me.Animation.ToString().Contains("_rapidstrikes_"))
                    {
                        return true;
                    }
                    if (Hud.Game.Me.Animation.ToString().Contains("_debilitatingblows_"))
                    {
                        return false;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno))
                    {
                        if (S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, 3) < 1)
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()

                ;
        }
    }


    internal sealed class S7oPolicy_MonkCycloneStrike : S7oImportedPolicy
	{
        public S7oPolicy_MonkCycloneStrike()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_CycloneStrike;

            CreateCastRule()
               .IfCanCastSkill(100, 200, 1000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
               .IfEnoughMonstersNearby(ctx => ctx.Skill.Rune == 1 ? 34 : 24, ctx => 1).ThenContinueElseNoCast()
               .IfTrue(ctx =>ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.LefebvresSoliloquy.Sno, 0)).ThenContinueElseNoCast()
               .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Monk_CycloneStrike, 0, 1000, 1500).ThenCastElseContinue()
               ;
            
            CreateCastRule()
                .IfCanCastSkill(100, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 50, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var range = ctx.Skill.Rune == 1 ? 34 : 24;
                    return S7oMacroQueries.NearestBoss(ctx.Hud) != null && S7oMacroQueries.NearestBoss(ctx.Hud).NormalizedXyDistanceToMe <= range && S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 34) > 1;
                }).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return (ctx.Skill.Rune == 2);
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfCanCastSkill(1000, 1000, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(100, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 50, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return isLesserGodsOK(ctx.Skill.Rune);
                }).ThenCastElseContinue()
               .IfTrue(ctx =>
               {
                   return (ctx.Hud.Game.Items.Where(i => i.Location == ItemLocation.LeftHand).FirstOrDefault()?.SnoItem.Sno == Hud.Sno.SnoItems.Unique_CombatStaff_2H_001_x1.Sno
                   )
                   ;
               }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 50, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.Powers.GetUsedSkill(ctx.Hud.Sno.SnoPowers.Monk_ExplodingPalm) != null).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Hud.Game.Items.Where(i => i.Location == ItemLocation.LeftHand).FirstOrDefault()?.SnoItem.Sno == Hud.Sno.SnoItems.Unique_CombatStaff_2H_001_x1.Sno
                    && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno)
                    && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.FlyingDragon.Sno)
                    && ctx.Skill.Player.GetSetItemCount(707760) >= 3
                    ;
                }).ThenCastElseContinue()

                ;

            CreateCastRule()
                .IfCanCastSkill(100, 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
               .IfTrue(ctx =>
               {
                   int season = ctx.Hud.Game.Me.Hero.Season;
                   int bell = S7oMacroQueries.Actors(ctx.Hud).Count(a => a.SnoActor.Sno == ActorSnoEnum._p74_monk_bell_waveoflight_runeb);
                   return season == 34
                  && bell == 5;
                   ;
               }).ThenCastElseContinue()
                ;

        }
        private bool isLesserGodsOK(int Rune)
        {
            bool isLesserGods = Hud.Game.Me.Powers.BuffIsActive(485725); 
            var range = Rune == 1 ? 34 : 24;
            bool isBossOrEliteNearby = S7oMacroQueries.EliteOrBossNear(Hud, range, false) || S7oMacroQueries.NearestGoblin(Hud)?.NormalizedXyDistanceToMe < range || S7oMacroQueries.NearestKeywarden(Hud)?.NormalizedXyDistanceToMe < range;
            bool noLesserGodsDebuff = S7oMacroQueries.Monsters(Hud).Any(x => (isBossOrEliteNearby ? (x.Rarity == ActorRarity.Boss || x.Rarity == ActorRarity.Champion || x.Rarity == ActorRarity.Rare || x.Rarity == ActorRarity.Unique || ((Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Ingeom.Sno) || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.BaneOfThePowerfulPrimary.Sno)) ? x.Rarity == ActorRarity.RareMinion : true)) : true) && x.NormalizedXyDistanceToMe < range && x.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 485725) != 1);
            return isLesserGods && noLesserGodsDebuff;
        }

    }


    internal sealed class S7oPolicy_MonkDashingStrike : S7oImportedPolicy
    {
        private IScreenCoordinate PlayerScreenCoordinate = null;
        private IWorldCoordinate LastWorldCoordinate = null;
        private bool moveback = false;
        public S7oPolicy_MonkDashingStrike()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_DashingStrike;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastWorldCoordinate.ToScreenCoordinate().X, LastWorldCoordinate.ToScreenCoordinate().Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 2 || ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfBossIsNearby(ctx => 20).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    return cursor.XYZDistanceTo(ctx.Hud.Game.Me.FloorCoordinate) < 20;
                }
                ).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 300).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    PlayerScreenCoordinate = ctx.Hud.Game.Me.FloorCoordinate.ToScreenCoordinate();
                    LastWorldCoordinate = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    S7oMacroQueries.RequestAim(ctx.Hud, PlayerScreenCoordinate.X, PlayerScreenCoordinate.Y);
                    moveback = true;
                    return true;
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.GetSetItemCount(755275) >= 6 && ctx.Skill.Player.GetSetItemCount(563257) >= 2)
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastWorldCoordinate.ToScreenCoordinate().X, LastWorldCoordinate.ToScreenCoordinate().Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033, 2, 300, 500).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_P3ItemPassiveUniqueRing026, 1).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 12, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (Hud.Game.Me.Animation.ToString().Contains("_rapidstrikes_"))
                    {
                        return true;
                    }
                    if (Hud.Game.Me.Animation.ToString().Contains("_debilitatingblows_"))
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    PlayerScreenCoordinate = ctx.Hud.Game.Me.FloorCoordinate.ToScreenCoordinate();
                    LastWorldCoordinate = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    S7oMacroQueries.RequestAim(ctx.Hud, PlayerScreenCoordinate.X, PlayerScreenCoordinate.Y);
                    moveback = true;
                    return true;
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_MonkDeadlyReach : S7oImportedPolicy
    {
        public S7oPolicy_MonkDeadlyReach()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_DeadlyReach;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.GetSetItemCount(755275) >= 6 && ctx.Skill.Player.GetSetItemCount(563257) >= 2)
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (Hud.Game.Me.Animation.ToString().Contains("_rapidstrikes_"))
                    {
                        return true;
                    }
                    if (Hud.Game.Me.Animation.ToString().Contains("_debilitatingblows_"))
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var rune = ctx.Skill.Rune;
                    var buff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno);
                    if (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno))
                    {
                        if (S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, 2) < 1)
                        {
                            return true;
                        }
                    }
                    if (rune == 0)
                    {
                        if (buff != null)
                        {
                            if (S7oMacroData.Seconds((buff)?.TimeLeftSeconds, 1) < 1)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()

                ;
        }
    }


    internal sealed class S7oPolicy_MonkEpiphany : S7oImportedPolicy
	{
        public S7oPolicy_MonkEpiphany()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_Epiphany;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(500, 1000).ThenCastElseContinue()
                ;
            
        }
    }


    internal sealed class S7oPolicy_MonkFistsOfThunder : S7oImportedPolicy
    {
        public S7oPolicy_MonkFistsOfThunder()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_FistsOfThunder;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.GetSetItemCount(755275) >= 6 && ctx.Skill.Player.GetSetItemCount(563257) >= 2)
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (Hud.Game.Me.Animation.ToString().Contains("_rapidstrikes_"))
                    {
                        return true;
                    }
                    if (Hud.Game.Me.Animation.ToString().Contains("_debilitatingblows_"))
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno))
                    {
                        if (S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, 4) < 1)
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkInnerSanctuary : S7oImportedPolicy
	{
        public S7oPolicy_MonkInnerSanctuary()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_InnerSanctuary;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    bool isDPS = ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.BaneOfTheTrappedPrimary.Sno);
                    return isDPS ? ctx.Skill.BuffIsActive == false : true;
                }).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 50).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 50, ctx => 5).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_MonkMantraOfConviction : S7oImportedPolicy
    {
        public S7oPolicy_MonkMantraOfConviction()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = true;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_MantraOfConviction;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Monk_Passive_MantraOfConvictionV2, 2).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(50, ctx => 50).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var epiphanySkill = ctx.Skill.Player.Powers.UsedMonkPowers.Epiphany;
                    return epiphanySkill?.BuffIsActive == true;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    
                    return ctx.Hud.Game.IsEliteOnScreen || ctx.Hud.Game.IsGoblinOnScreen || ctx.Hud.Game.MaxPriorityOnScreen >= MonsterPriority.keywarden || S7oMacroQueries.NearestBoss(ctx.Hud) != null;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkMantraOfSalvation : S7oImportedPolicy
    {
        public S7oPolicy_MonkMantraOfSalvation()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower=Hud.Sno.SnoPowers.Monk_MantraOfSalvation;
            CreateCastRule().IfTrue(ctx=>Owner.ShouldMaintainSalvation(ctx.Skill,Environment.TickCount)).ThenCastElseNoCast();
        }

    }


    internal sealed class S7oPolicy_MonkMysticAlly : S7oImportedPolicy
	{
        public S7oPolicy_MonkMysticAlly()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_MysticAlly;
               
            CreateCastRule()
                .IfCanCastSkill(150, 200, 500).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Rune == 3 || (ctx.Skill.Rune == 1 && ctx.Skill.Player.GetSetItemCount(742942) >= 6)) && ctx.Skill.Player.Stats.ResourceCurPri < ctx.Skill.Player.Stats.ResourceMaxPri - 200).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4 && ctx.Skill.Player.Defense.HealthPct < 30).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfCanCastSkill(100, 200, 500).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfIdle().ThenNoCastElseContinue()
                .IfTrue(ctx => {
                    bool isBossOrEliteNearby = S7oMacroQueries.Monsters(ctx.Hud).Any(x => (x.Rarity == ActorRarity.Boss || x.Rarity == ActorRarity.Champion || x.Rarity == ActorRarity.Rare || x.Rarity == ActorRarity.Unique) && x.NormalizedXyDistanceToMe < 20 && !x.Illusion && !x.Invulnerable && !x.Invisible);
                    return (ctx.Skill.Rune == 1 || ctx.Skill.Rune == 2) && ctx.Skill.Player.GetSetItemCount(742942) >= 6 && ((S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 20) > 1 || isBossOrEliteNearby) && (getCurrentMysticAlly() - getCurrentStone()) >= getMaxMysticAlly());
                }).ThenCastElseContinue()
                .IfTrue(ctx => {
                    bool isCOE = ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno);
                    bool isLesserGods = ctx.Hud.Game.Me.Powers.BuffIsActive(485725); 
                    bool isBossOrEliteNearby = S7oMacroQueries.EliteOrBossNear(ctx.Hud, 20, false) || S7oMacroQueries.NearestGoblin(ctx.Hud)?.NormalizedXyDistanceToMe < 20 || S7oMacroQueries.NearestKeywarden(ctx.Hud)?.NormalizedXyDistanceToMe < 20;
                    bool isLesserGodsDebuff = S7oMacroQueries.Monsters(ctx.Hud).Any(x => (isBossOrEliteNearby ? (x.Rarity == ActorRarity.Boss || x.Rarity == ActorRarity.Champion || x.Rarity == ActorRarity.Rare || x.Rarity == ActorRarity.Unique) : true) && x.NormalizedXyDistanceToMe < 20 && !x.Illusion && !x.Invulnerable && !x.Invisible && x.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 485725) == 1);
                    return ctx.Skill.Rune == 0 && isLesserGods && isLesserGodsDebuff && (isCOE ? ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno, 3) : true) && getCurrentMysticAlly() >= getMaxMysticAlly();
                }).ThenCastElseContinue()
                ;
        }
        private int getCurrentStone()
        {
            return S7oMacroQueries.Actors(Hud).Where(x => x.SummonerAcdDynamicId == Hud.Game.Me.SummonerId && (x.SnoActor.Sno == ActorSnoEnum._monk_female_mystically_crimson || x.SnoActor.Sno == ActorSnoEnum._x1_projectile_mystically_runec_boulder 
           )
            ).Count();
        }
        private int getCurrentMysticAlly()
        {
            

            int mysticallymini = S7oMacroQueries.Actors(Hud).Count(x => x.SummonerAcdDynamicId == Hud.Game.Me.SummonerId && (x.SnoActor.Sno == ActorSnoEnum._x1_monk_female_mysticallymini_crimson) 
           );
            int threshold = 5;
            if (mysticallymini > 0 && mysticallymini < ((getMaxMysticAlly() * 2 - threshold) < 1 ? 1: (getMaxMysticAlly() * 2 - threshold)) && Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno, 0) == true)
            {
                return (getMaxMysticAlly() * 2);
            }
            if (mysticallymini >= ((getMaxMysticAlly() * 2 - threshold) < 1 ? 1 : (getMaxMysticAlly() * 2 - threshold)) && Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno, 0) == true) return 0;

            return  S7oMacroQueries.Actors(Hud).Where(x => x.SummonerAcdDynamicId == Hud.Game.Me.SummonerId && (x.SnoActor.Sno == ActorSnoEnum._monk_female_mystically_crimson || x.SnoActor.Sno == ActorSnoEnum._monk_male_mystically_crimson || 
           x.SnoActor.Sno == ActorSnoEnum._monk_female_mystically_indigo || x.SnoActor.Sno == ActorSnoEnum._monk_male_mystically_indigo ||
           x.SnoActor.Sno == ActorSnoEnum._monk_female_mystically_obsidian || x.SnoActor.Sno == ActorSnoEnum._monk_male_mystically_obsidian
           )
           ).Count();
        }
        private int getMaxMysticAlly()
        {
            if (Hud.Game.Me.GetSetItemCount(742942) >= 6) return 10;
            if (Hud.Game.Me.Powers.BuffIsActive(409811) || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheCrudestBoots.Sno)) return 2;
            return 1;
        }
    }


    internal sealed class S7oPolicy_MonkSerenity : S7oImportedPolicy
	{
        public S7oPolicy_MonkSerenity()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_Serenity;
            CreateCastRule()
                .IfCanCastSkill(50, 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3;
                }
                ).ThenContinueElseNoCast()
                .IfNearbyPartyMemberIsInDanger(45, 60, 80, 70, true).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction >= 0.7).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var players = ctx.Hud.Game.Players.Where(p => (p.HeroClassDefinition.HeroClass == HeroClass.WitchDoctor || p.HeroClassDefinition.HeroClass == HeroClass.Necromancer || p.HeroClassDefinition.HeroClass == HeroClass.Crusader || p.HeroClassDefinition.HeroClass == HeroClass.Wizard) && 
                    !p.IsDead && p.InGreaterRift);
                    if (players == null)
                    {
                        players = ctx.Hud.Game.Players.Where(p => !p.IsDead && p.InGreaterRift);
                    }
                    if (players == null)
                    {
                        return false;
                    }
                    bool CloseToThePlayer = false;
                    foreach (var player in players)
                    {
                        if(player.CentralXyDistanceToMe <= 45)
                        {
                            
                            CloseToThePlayer = true;
                            break;
                        }
                    }
                    return CloseToThePlayer;
                }).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfCanCastSkill(50, 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.BuffIsActive).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 60, ctx => 1).ThenContinueElseNoCast()
                .IfHealthWarning(60, 80).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInvulnerable).ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.SquirtsNecklace.Sno) || Hud.Game.Me.Stats.CooldownReduction >= 0.5 && (
                ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Ingeom.Sno) || 
                ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno) || 
                ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting.Sno) ||
                ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno))).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_MonkSweepingWind : S7oImportedPolicy
    {
        public int ActivationRange { get; set; }

        public S7oPolicy_MonkSweepingWind()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Collect, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
            ActivationRange = 40;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_SweepingWind;

            CreateCastRule()
                .IfCanCastSkill(150, 200, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(15, ctx => 75).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(300, 1000).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_MonkWayOfTheHundredFists : S7oImportedPolicy
    {
        public S7oPolicy_MonkWayOfTheHundredFists()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Skill.Player.GetSetItemCount(755275) >= 6 && ctx.Skill.Player.GetSetItemCount(563257) >= 2)
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if(Hud.Game.Me.Animation.ToString().Contains("_rapidstrikes_"))
                    {
                        return false;
                    }
                    if (Hud.Game.Me.Animation.ToString().Contains("_debilitatingblows_"))
                    {
                        return true;
                    }
                    return false;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var rune = ctx.Skill.Rune;
                    var buff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno);
                    if (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno))
                    {
                        if (S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, 5) < 1)
                        {
                            return true;
                        }
                    }
                    if (rune == 2)
                    {
                        
                        
                        if (buff != null)
                        {
                            if (S7oMacroData.Count((buff)?.IconCounts, 1) < 3 || S7oMacroData.Seconds((buff)?.TimeLeftSeconds, 1) < 1)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                    if(rune == 3)
                    {
                        if (buff != null)
                        {
                            if (S7oMacroData.Seconds((buff)?.TimeLeftSeconds, 4) < 1)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()
                
                ;
        }
    }


    internal sealed class S7oPolicy_NecArmyoftheDead : S7oImportedPolicy
    {
        public S7oPolicy_NecArmyoftheDead()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_ArmyOfTheDead;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.FuneraryPick).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    int range = 14;
                    bool IncludeMinion = false;
                    IWorldCoordinate cursor = ctx.Hud.Window.CreateScreenCoordinate(ctx.Hud.Window.CursorX, ctx.Hud.Window.CursorY).ToWorldCoordinate();
                    bool Result = S7oMacroQueries.Monsters(ctx.Hud).Any(m => ((m.IsElite && (IncludeMinion ? true : m.Rarity != ActorRarity.RareMinion)) || m.SnoMonster.Priority == MonsterPriority.goblin) && m.FloorCoordinate.XYDistanceTo(cursor) <= range && !m.Invisible && !m.Illusion);
                    return Result;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var MonsterRiftProgression = 0d;
                    IWorldCoordinate cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    var monsters = S7oMacroQueries.Monsters(ctx.Hud).Where( m => m.FloorCoordinate.XYDistanceTo(cur) <= ((ctx.Skill.Rune == 3 && S7oMacroMechanics.IsElementReady(hud, 1, ctx.Skill.Player)) ? 30 : 20)) ;
                    foreach (var monster in monsters)
                    {
                        MonsterRiftProgression += monster.SnoMonster.RiftProgression * 100.0d / Hud.Game.MaxQuestProgress;
                    }
                    return MonsterRiftProgression >= 1.3d;
                }
                ).ThenCastElseContinue()
                
                .IfTrue(ctx =>
                {
                    var Revives = S7oMacroQueries.Actors(ctx.Hud).Where(m =>
                    (m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_A, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_B, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_C, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_D, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_E, 462239) == 1) &&
                    m.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId);
                    int sets = ctx.Skill.Player.GetSetItemCount(740279);
                    return sets >= 2 && Revives.Count() < 10;
                }).ThenContinueElseNoCast()
                .IfEnoughMonstersNearbyCursor(ctx => 15, ctx => 4).ThenCastElseContinue()
                .IfEliteOrBossNearbyCursor(ctx => 15, false).ThenCastElseContinue()
                ;


            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearbyCursor(ctx => 14, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_male_hth_cast_grimscythe_leftright ||
                    Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_male_hth_cast_grimscythe_rightleft ||
                    Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_female_hth_cast_grimscythe_leftright ||
                    Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_female_hth_cast_grimscythe_rightleft ||
                    Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_male_hth_cast_bloodsiphon ||
                    Hud.Game.Me.Animation == AnimSnoEnum._p6_necro_female_hth_cast_bloodsiphon
                    ;
                }).ThenCastElseContinue()
                ;


        }

    }


    internal sealed class S7oPolicy_NecBloodRush : S7oImportedPolicy
    {
        public int SesourcesBelow { get; set; }
        private IScreenCoordinate PlayerScreenCoordinate = null;
        private IWorldCoordinate LastWorldCoordinate = null;
        private bool moveback = false;
        public S7oPolicy_NecBloodRush()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            SesourcesBelow = 80;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_BloodRush;
            Rune = 4;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastWorldCoordinate.ToScreenCoordinate().X, LastWorldCoordinate.ToScreenCoordinate().Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.CurrentSkills.Any(s => s.SnoPower.Sno == ctx.Hud.Sno.SnoPowers.Necromancer_Devour.Sno && s.Rune == 2) &&
                    ctx.Skill.Player.Powers.CurrentSkills.Any(s => s.SnoPower.Sno == ctx.Hud.Sno.SnoPowers.Necromancer_BoneSpear.Sno)
                    ;
                }).ThenContinueElseNoCast()
                
                .IfBossIsNearby(ctx => 50).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    return cursor.XYZDistanceTo(ctx.Hud.Game.Me.FloorCoordinate) < 50;
                }
                ).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Necromancer_Devour, 2, 1000, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    PlayerScreenCoordinate = ctx.Hud.Game.Me.FloorCoordinate.ToScreenCoordinate();
                    LastWorldCoordinate = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    S7oMacroQueries.RequestAim(ctx.Hud, PlayerScreenCoordinate.X, PlayerScreenCoordinate.Y);
                    moveback = true;
                    return true;
                }).ThenCastElseContinue()
                ;
            
        }
    }


    internal sealed class S7oPolicy_NecBoneArmor : S7oImportedPolicy
    {
        public int SpareResource { get; set; }
        public S7oPolicy_NecBoneArmor()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_BoneArmor;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 1 || (ctx.Skill.Rune == 2 && ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno) && ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.KrysbinsSentence.Sno))).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var monsters = S7oMacroQueries.Monsters(ctx.Hud).Where(m => !m.Invulnerable && !m.Invisible && (m.CentralXyDistanceToMe < 30 + m.RadiusBottom));
                    return monsters.Count() >= (ctx.Hud.Game.Me.Powers.BuffIsActive(476686, 0) ? 2 : 1) || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 30, false);
                })
                .ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Defense.HealthPct < 50 && ctx.Skill.Rune == 3).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(740281) >= 2 && ctx.Skill.Rune != 3).ThenCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Player.Powers.UsedNecromancerPowers.SkeletalMage?.Rune == 1 || ctx.Hud.Game.Me.Powers.BuffIsActive(484311)) && ctx.Skill.Player.Powers.BuffIsActive(476586) && ctx.Skill.Rune != 3).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_BoneArmor.Sno);
                    return S7oMacroData.NullableCount(buff, 0) < (ctx.Hud.Game.Me.Powers.BuffIsActive(476686, 0) ? 15 : 10);
                }).ThenCastElseContinue()
                .IfBuffIsAboutToExpire(10000,15000).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx =>  ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.KrysbinsSentence).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var monsters = S7oMacroQueries.Monsters(ctx.Hud).Where(m => !m.Invulnerable && !m.Invisible && m.CentralXyDistanceToMe < 30);
                    return monsters.Count() >= (ctx.Hud.Game.Me.Powers.BuffIsActive(476686, 0) ? 2 : 1) || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 30, false);
                })
                .ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    var CoeBuffLeftTime = S7oMacroMechanics.GetBuffLeftTime(hud, ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno, S7oMacroMechanics.GetHighestElement(Hud, ctx.Skill.Player, CoeIndex));
                    return CoeBuffLeftTime > 0 && CoeBuffLeftTime < 3;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_BoneArmor.Sno);
                    return S7oMacroData.NullableCount(buff, 0) < (ctx.Hud.Game.Me.Powers.BuffIsActive(476686, 0) ? 15 : 10);
                }).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(476586) && ctx.Skill.Rune != 3).ThenCastElseContinue()
                .IfBuffIsAboutToExpire(1000, 1500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Stats.CooldownReduction >= 0.6).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.GogokOfSwiftnessPrimary).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.BaneOfTheStrickenPrimary).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 30, ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_NecCommandSkeletons : S7oImportedPolicy
    {
        private bool isCast = false;
        public S7oPolicy_NecCommandSkeletons()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = true;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_CommandSkeletons;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3;
                }
                ).ThenNoCastElseContinue()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var monster = ctx.Hud.Game.SelectedMonster2;
                    if (monster == null) return false;
                    return monster?.Rarity == ActorRarity.Boss;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return !ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons.Sno);
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    bool cast = false;
                    if (ctx.Hud.Game.Me.Powers.UsedLegendaryPowers.BloodsongMail?.Active == true)
                    {
                        if (ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno))
                        {
                            if (!isCast)
                            {
                                isCast = true;
                                cast = true;
                            }
                        }
                        else
                        {
                            isCast = false;
                        }
                    }
                    else
                    {
                        isCast = false;
                    }
                    return cast;
                }).ThenCastElseContinue()
                ;


            CreateCastRule()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3;
                }
                ).ThenNoCastElseContinue()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons.Sno)).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var monster = ctx.Hud.Game.SelectedMonster2;
                    if (monster == null)
                    {
                        return false;
                    }
                    else
                    {
                        bool JessethArms = ctx.Skill.Player.Powers.BuffIsActive(476047);
                        if (JessethArms)
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()
                ;


            CreateCastRule()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3;
                }
                ).ThenNoCastElseContinue()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var monster = ctx.Hud.Game.SelectedMonster2;
                    if (monster == null)
                    {
                        return false;
                    }
                    else
                    {
                        bool ObsidianRingoftheZodiac = ctx.Skill.Player.Powers.BuffIsActive(402459);
                        if (ctx.Skill.GetResourceRequirement() <= 35 && ObsidianRingoftheZodiac)
                        {
                            return true;
                        }
                    }
                    return false;
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
               .IfTrue(ctx =>
               {
                    return ctx.Skill.Rune == 3;
               }
               ).ThenNoCastElseContinue()
               .IfCanCastSkill(2000, 2000, 2000).ThenContinueElseNoCast()
               .IfInTown().ThenNoCastElseContinue()
               .IfCastingIdentify().ThenNoCastElseContinue()
               .IfCastingPortal().ThenNoCastElseContinue()
               .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
               
               .IfTrue(ctx =>
               {
                    var monster = Hud.Game.SelectedMonster2;
                   if (monster == null)
                   {
                       return false;
                   }
                   var CommandSkeletonsTarget = S7oMacroQueries.Actors(ctx.Hud).Where(x => x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_None, 453801) == 1 || x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_A, 453801) == 1 || x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_B, 453801) == 1 || x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_C, 453801) == 1 || x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_D, 453801) == 1 || x.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_4_Visual_Effect_E, 453801) == 1).FirstOrDefault();
                   bool isCommandSkeletonsTargetOnScreen = CommandSkeletonsTarget?.IsOnScreen == true;
                   var ArmyoftheDead = ctx.Hud.Game.Me.Powers.GetUsedSkill(ctx.Hud.Sno.SnoPowers.Necromancer_ArmyOfTheDead);
                   bool AttackAnyMonster = ArmyoftheDead?.IsOnCooldown == true && isCommandSkeletonsTargetOnScreen;
                   return (monster.Rarity == ActorRarity.Boss || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Rare || AttackAnyMonster) && !monster.Illusion;
               }).ThenCastElseContinue()
               ;

        }
    }


    internal sealed class S7oPolicy_NecDecrepify : S7oImportedPolicy
    {
        public S7oPolicy_NecDecrepify()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Decrepify;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var isAnyUncursedMonster = S7oMacroQueries.Monsters(Hud).Any(m => (m.Rarity == ActorRarity.Boss || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Unique || m.SnoMonster.Priority == MonsterPriority.goblin || m.SnoMonster.Priority == MonsterPriority.keywarden)  && !m.Invisible && !m.Invulnerable && !m.Illusion && m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_None, 471738) != 1 && (m.FloorCoordinate.XYZDistanceTo(cur.ToWorldCoordinate()) - m.RadiusBottom) <= 19  && 
                    (ctx.Skill.Rune == 4 ? true : !m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut)));
                    return isAnyUncursedMonster;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_NecDevour : S7oImportedPolicy
    {
        public S7oPolicy_NecDevour()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Devour;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3;
                }).ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Hud.Game.SpecialArea == SpecialArea.PvP || S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 60) > 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno)).ThenCastElseContinue()
                .IfTrue(ctx => FleshCount() > 0).ThenContinueElseNoCast()
                .IfTrue(ctx => (ctx.Hud.Game.Me.Defense.HealthPct < 70 || ctx.Hud.Game.Me.Defense.HealthPct < 95 - (FleshCount() * 3)) && ctx.Skill.Rune == 0).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Hud.Game.Me.Stats.ResourcePctEssence < 100).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Hud.Game.Me.GetSetItemCount(740282) >= 4 && (S7oMacroMechanics.GetBuffCount(ctx.Hud, ctx.Hud.Sno.SnoPowers.Generic_P6ItemPassiveUniqueRing011.Sno, 1) < 25 || S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, ctx.Hud.Sno.SnoPowers.Generic_P6ItemPassiveUniqueRing011.Sno, 1) < 2);
                }).ThenCastElseContinue()
                ;
        }

        private int FleshCount()
        {
            return S7oMacroQueries.Actors(Hud).Count(actor => actor.SnoActor.Sno == ActorSnoEnum._p6_necro_corpse_flesh && actor.CentralXyDistanceToMe <= 60);
        }
    }


    internal sealed class S7oPolicy_NecFrailty : S7oImportedPolicy
    {
        public S7oPolicy_NecFrailty()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Frailty;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenNoCastElseContinue()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var isAnyMonsterUnhealthy = S7oMacroQueries.Monsters(Hud).Any(m => (
                    m.Rarity == ActorRarity.Boss || 
                    m.Rarity == ActorRarity.Rare || 
                    m.Rarity == ActorRarity.Champion || 
                    m.Rarity == ActorRarity.Unique ||
                    m.SnoMonster.Priority == MonsterPriority.goblin || 
                    m.SnoMonster.Priority == MonsterPriority.keywarden
                    )  && !m.Invisible && !m.Invulnerable && !m.Illusion  && (m.FloorCoordinate.XYZDistanceTo(cur.ToWorldCoordinate()) - m.RadiusBottom) <= 18  && (m.CurHealth / m.MaxHealth) < (ctx.Skill.Rune != 0 ? 0.15 : 0.18) &&
                    (ctx.Skill.Rune != 0 ? true : 
                    ctx.Skill.Player.Defense.HealthPct >= 30));
                    return isAnyMonsterUnhealthy;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_NecLandOfTheDead : S7oImportedPolicy
    {
        public float secMin { get; set; }
        public float secMax { get; set; }
        public S7oPolicy_NecLandOfTheDead()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            secMin = 0;
            secMax = 1;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_LandOfTheDead;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_PagesBuffInfiniteCasting, 0, 500, 2000, true).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead).ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.HauntedVisions).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var buff = ctx.Hud.Game.Me.Powers.GetBuff(ctx.Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno);

                    return S7oMacroData.NullableElapsed(buff, 1) > secMin && S7oMacroData.NullableElapsed(buff, 1) < secMax;
                }).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 60).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool CDR = ctx.Hud.Game.Me.Stats.CooldownReduction <= 0.6;
                    bool isSetPestilence = ctx.Hud.Game.Me.GetSetItemCount(740282) >= 6;
                    return CDR && isSetPestilence && ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Necromancer_Passive_BloodIsPower.Sno) && (ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Ingeom.Sno));
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;

                CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 60, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool CDR = ctx.Hud.Game.Me.Stats.CooldownReduction > 0.6;
                    bool isSetPestilence = ctx.Hud.Game.Me.GetSetItemCount(740282) >= 6;
                    return CDR && isSetPestilence && ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Necromancer_Passive_BloodIsPower.Sno) && (ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno) || ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Ingeom.Sno));
                }).ThenContinueElseNoCast()
                
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return ctx.Hud.Game.Me.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.MesserschmidtsReaver.Sno);
                }).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;
        }
    }



    internal sealed class S7oPolicy_NecLeech : S7oImportedPolicy
    {
        public S7oPolicy_NecLeech()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Leech;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4)
                .IfTrue(ctx => { 
                    return ctx.Skill.Player.Powers.BuffIsActive(488036) || ctx.Skill.Player.Powers.BuffIsActive(488037) || ctx.Skill.Player.Powers.BuffIsActive(488004); 
                }).ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var isAnyUncursedMonster = S7oMacroQueries.Monsters(Hud).Any(m => (m.Rarity == ActorRarity.Boss || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Unique || m.SnoMonster.Priority == MonsterPriority.goblin || m.SnoMonster.Priority == MonsterPriority.keywarden) && !m.Invisible && !m.Invulnerable && !m.Illusion && m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_None, 471869) != 1 && (m.FloorCoordinate.XYZDistanceTo(cur.ToWorldCoordinate()) - m.RadiusBottom) <= 19);
                    return isAnyUncursedMonster;
                }).ThenCastElseContinue()
                ;


        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_BoneArmor : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_BoneArmor()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_BoneArmor;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    return remaining < 0.5d;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_BoneSpear : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_BoneSpear()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_BoneSpear;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)ctx.Skill.GetResourceRequirement() + 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if (remaining < 0.5d)
                    {
                        return true;
                    }
                    else if (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        return remaining < Coe;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_BoneSpikes : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_BoneSpikes()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_BoneSpikes;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if (remaining < 0.5d)
                    {
                        return true;
                    }
                    else if (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        return remaining < Coe;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_CommandSkeletons : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_CommandSkeletons()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_CommandSkeletons;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)ctx.Skill.GetResourceRequirement() + 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    if (ctx.Hud.Game.SelectedMonster2 == null) return false;
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if (remaining < 0.5d)
                    {
                        return true;
                    }
                    else if (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        return remaining < Coe;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_DeathNova : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_DeathNova()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_DeathNova;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3 || ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)ctx.Skill.GetResourceRequirement() + 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if (remaining < 0.5d)
                    {
                        return true;
                    }
                    else if (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        return remaining < Coe;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_Revive : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_Revive()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Revive;
            
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfTrue(ctx=>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    bool NayrsBlackDeath = remaining < 8d;
                    return (remaining == 0 ? true: isNoElite()) && 
                    isCorpse() && 
                    NayrsBlackDeath;
                }).ThenCastElseContinue()
                ;
        }
        private bool isNoElite()
        {
            IWorldCoordinate cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
            bool isNoElite = S7oMacroQueries.Monsters(Hud).Any(m => m.FloorCoordinate.XYDistanceTo(cur) <= 20 && ((m.Rarity != ActorRarity.Champion && m.Rarity != ActorRarity.Rare) || m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut)) && !m.Illusion );
            return isCorpse();
        }
        private bool isCorpse()
        {
            IWorldCoordinate cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
            bool isCorpse = S7oMacroQueries.Actors(Hud).Any(a => a.SnoActor.Sno == ActorSnoEnum._p6_necro_corpse_flesh && a.FloorCoordinate.XYDistanceTo(cur) <= 20);
            return isCorpse;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_SiphonBlood : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }
        public S7oPolicy_NecNayrsBlackDeath_SiphonBlood()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_SiphonBlood;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if(remaining < 0.5d)
                    {
                        return true;
                    }
                    else if(ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        return remaining < Coe;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecNayrsBlackDeath_SkeletalMage : S7oImportedPolicy
    {
        public bool InvalidWhenForceMove { get; set; }

        public S7oPolicy_NecNayrsBlackDeath_SkeletalMage()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_SkeletalMage;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3).ThenContinueElseNoCast()
                .IfTrue(ctx => !InvalidWhenForceMove || !S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) || !S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath, 0).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)ctx.Skill.GetResourceRequirement() + 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int IconIndex = ctx.Skill.Key.GetHashCode() + 1;
                    var buff = ctx.Skill.Player.Powers.GetBuff(hud.Sno.SnoPowers.NayrsBlackDeath.Sno);
                    var remaining = buff?.Active == true ? S7oMacroData.Seconds((buff)?.TimeLeftSeconds, IconIndex) : 0.0d;
                    if (remaining < 0.5d)
                    {
                        return true;
                    }
                    else if (ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ConventionOfElements.Sno))
                    {
                        var Coe = S7oMacroMechanics.GetHighestElementLeftSecond(hud, hud.Game.Me, 2);
                        var buffSkeletalMage = S7oMacroMechanics.GetBuffLeftTime(hud, Hud.Sno.SnoPowers.Necromancer_SkeletalMage.Sno, 6);
                        return Coe > 4 && Coe < 5 && buffSkeletalMage < 5;
                    }
                    return false;
                }).ThenCastElseContinue()
            ;
        }
    }


    internal sealed class S7oPolicy_NecRevive : S7oImportedPolicy
    {
        public S7oPolicy_NecRevive()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Revive;
            
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var Revives = S7oMacroQueries.Actors(ctx.Hud).Where(m =>
                    (m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_None, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_A, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_B, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_C, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_D, 462239) == 1 ||
                    m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_1_Visual_Effect_E, 462239) == 1) && 
                    m.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId);
                    int sets = ctx.Skill.Player.GetSetItemCount(740279);
                    return sets >= 2 && Revives.Count() < 10 && isCorpse();
                }).ThenCastElseContinue()
                ;
        }
        private bool isCorpse()
        {
            IWorldCoordinate cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
            bool isCorpse = S7oMacroQueries.Actors(Hud).Any(a => a.SnoActor.Sno == ActorSnoEnum._p6_necro_corpse_flesh && a.FloorCoordinate.XYDistanceTo(cur) <= 20);
            return isCorpse;
        }
    }


    internal sealed class S7oPolicy_NecSimulacrum : S7oImportedPolicy
    {
        public float SecSpare { get; set; }
        public S7oPolicy_NecSimulacrum()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            SecSpare = 6f;
        }
        
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_Simulacrum;
            CreateCastRule()
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(472267) ? S7oMacroData.NullableCount(ctx.Skill.Player.Powers.GetBuff(472267), 1) >= 300 : true).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool isBloodandBone = ctx.Skill.Rune == 3 || ctx.Skill.Player.Powers.BuffIsActive(484301);
                    int count = S7oMacroQueries.Actors(ctx.Hud).Where(a => a.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId && (a.SnoActor.Sno == (ActorSnoEnum)467053 || a.SnoActor.Sno == (ActorSnoEnum)484304)).Count();
                    return ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.HauntedVisions.Sno)
                    && (!ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno)
                     || (isBloodandBone && count < 2))
                    ;
                }).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Necromancer_Simulacrum).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var buffLandOfTheDead = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno);

                    return (S7oMacroData.NullableElapsed(buffLandOfTheDead, 0) < 1 && S7oMacroData.NullableElapsed(buffLandOfTheDead, 0) != 0) || 
                    (
                    (120 - 
                    (ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Ingeom.Sno, 1) ? GetIngeom() : 0) 
                    ) * (1 - ctx.Skill.Player.Stats.CooldownReduction) 
                    <= ((ctx.Skill.Player.Powers.UsedLegendaryPowers.HauntedVisions?.Active == true ? 30 : 15) + SecSpare)
                    && (S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 50)> 0));
                }).ThenCastElseContinue()
                ;
        }

        private uint GetIngeom()
        {
            uint Sec = 10;
            if (Hud.Game.Me.CubeSnoItem1 != null && Hud.Game.Me.CubeSnoItem1.Sno == Hud.Sno.SnoItems.Unique_Sword_1H_113_x1.Sno)
            {
                Sec = 10;
            }
            else
            {
                var Ingeom = Hud.Game.Items.FirstOrDefault(item => (item.Location == ItemLocation.RightHand || item.Location == ItemLocation.LeftHand) && item.SnoItem.Sno == Hud.Sno.SnoItems.Unique_Sword_1H_113_x1.Sno);
                Sec = Ingeom == null ? 0 : (uint)Ingeom.Perfections.FirstOrDefault(p => p.Attribute.Code == "Item_Power_Passive").Cur;
            }
            return Sec;
        }
    }



    internal sealed class S7oPolicy_NecSiphonBlood : S7oImportedPolicy
    {
        public S7oPolicy_NecSiphonBlood()
            : base(S7oCastType.RangedChannelingSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_SiphonBlood;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 3)
                .IfTrue(ctx => S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var monster = ctx.Hud.Game.SelectedMonster2;
                    var buffCount = S7oMacroMechanics.GetBuffCount(ctx.Hud, Hud.Sno.SnoPowers.Necromancer_SiphonBlood.Sno, 10);
                    var buffTime = S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.Necromancer_SiphonBlood.Sno, 10);
                    return monster != null && (buffCount < 10 || buffTime < 0.5d);
                }).ThenCastElseNoCast()
                ;


        }
    }


    
    internal sealed class S7oPolicy_NecSkeletalMage : S7oImportedPolicy
    {
        private bool tempwalk1;
        private bool tempwalk2;
        public S7oPolicy_NecSkeletalMage()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Necromancer_SkeletalMage;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 4 && !ctx.Hud.Game.Me.Powers.BuffIsActive(484311);
                }
                ).ThenContinueElseNoCast()
                .IfCanCastSkill((int)Hud.Game.CurrentLatency + 50, (int)Hud.Game.CurrentLatency + 100, 1000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)(40 - 40 * Hud.Game.Me.Stats.ResourceCostReduction) + 1).ThenContinueElseNoCast()
                .IfBossIsNearby(ctx => 60).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var buffSimulacrum = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno);
                    var buffSkeletalMage = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_SkeletalMage.Sno);
                    return S7oMacroData.NullableCount(buffSkeletalMage, 6) < 2 && S7oMacroData.NullableElapsed(buffSimulacrum, 1) < 0.5 && S7oMacroData.NullableElapsed(buffSimulacrum, 1) != 0;
                }).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.AquilaCuirass, 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 6).ThenNoCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 3, 2000, 3000).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 1 || ctx.Hud.Game.Me.Powers.BuffIsActive(484311);
                }
                ).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Rune == 3 && ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.NayrsBlackDeath.Sno, 0);
                }
                ).ThenNoCastElseContinue()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 55, ctx => 1).ThenContinueElseNoCast()
                
                .IfCanCastSkill(100, 150, 1000).ThenContinueElseNoCast()
                .IfPrimaryResourceAmountIsAbove(ctx => (int)(40 - 40 * Hud.Game.Me.Stats.ResourceCostReduction) + 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int skeletons = 2;
                    if (S7oMacroQueries.EliteOrBossNear(ctx.Hud, 55, false))
                    {
                        skeletons = 2;
                    }
                    else if (S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 55) <= 15)
                    {
                        skeletons = 2;
                    }
                    else
                    {
                        skeletons = 8;
                    }
                    var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Necromancer_SkeletalMage.Sno);
                    return (S7oMacroData.NullableCount(buff, 6) < skeletons);
                }).ThenCastElseContinue()
                .IfPrimaryResourcePercentageIsAbove((Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead.Sno) || Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Necromancer_Simulacrum.Sno) || (S7oMacroQueries.Actors(Hud).Where(x => x.SnoActor.Kind == ActorKind.HealthGlobe && x.IsOnScreen).Count() * Hud.Game.Me.Stats.ResourceMaxEssence * GetReapersWrapspercent() + (Hud.Game.Me.Powers.UsedSkills.Any(x => x.SnoPower.Sno == 460757) ? S7oMacroQueries.Actors(Hud).Where(x => x.SnoActor.Sno == ActorSnoEnum._p6_necro_corpse_flesh && x.CentralXyDistanceToMe <= 60).Count() * 10 : 0) + Hud.Game.Me.Stats.ResourceCurEssence >= Hud.Game.Me.Stats.ResourceMaxEssence)) ? 100 : 90).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var actorsSkeletonMage = S7oMacroQueries.Actors(Hud).Where(x => (
                    x.SnoActor.Sno == ActorSnoEnum._p6_necro_skeletonmage_b ||
                    x.SnoActor.Sno == ActorSnoEnum._p6_necro_skeletonmage_c ||
                    x.SnoActor.Sno == ActorSnoEnum._p6_necro_skeletonmage_d ||
                    x.SnoActor.Sno == ActorSnoEnum._p6_necro_skeletonmage_e ||
                    x.SnoActor.Sno == ActorSnoEnum._p6_necro_skeletonmage_f_archer
                    ) && x.SummonerAcdDynamicId == ctx.Hud.Game.Me.SummonerId 
                    && (x.GetAttributeValue(Hud.Sno.Attributes.Multiplicative_Damage_Percent_Bonus, 0xFFFFF) >= (Hud.Game.Me.Stats.ResourceMaxEssence * 0.9 - (40 - 40 * Hud.Game.Me.Stats.ResourceCostReduction)) * 3 / 100 + 1));
                    return actorsSkeletonMage.Count() < 10;
                }).ThenCastElseContinue()
                .IfTrue(ctx => {
                    var monster = ctx.Hud.Game.SelectedMonster2;
                    return monster != null && monster.Invisible == false && (monster.IsElite == true || (monster.SnoMonster.Priority == MonsterPriority.goblin) || (monster.SnoMonster.Priority == MonsterPriority.boss) || (monster.SnoMonster.Priority == MonsterPriority.keywarden));
                    }
                ).ThenCastElseContinue()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 6, 100, 500).ThenCastElseContinue()
                ;
        }

        private float GetReapersWrapspercent()
        {
            float percent = 0;
            if (Hud.Game.Me.CubeSnoItem2 != null && Hud.Game.Me.CubeSnoItem2.Sno == Hud.Sno.SnoItems.Unique_Bracer_103_x1.Sno)
            {
                percent = 0.3f;
            }
            else
            {
                var Reapers = Hud.Game.Items.FirstOrDefault(item => item.Location == ItemLocation.Bracers && item.SnoItem.Sno == Hud.Sno.SnoItems.Unique_Bracer_103_x1.Sno);
                percent = Reapers == null ? 0 : (float)Reapers.Perfections.FirstOrDefault(p => p.Attribute.Code == "Item_Power_Passive").Cur;
            }
            return percent;
        }
    }


    internal sealed class S7oPolicy_WitchDoctorBigBadVoodoo : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorBigBadVoodoo()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_BigBadVoodoo;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var count = S7oMacroQueries.Actors(ctx.Hud).Count(actor => actor.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId &&
                    (actor.SnoActor.Sno == ActorSnoEnum._fetish_melee_a || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_doublestack_shaman_a || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_ranged_a || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_melee_itempassive || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_shaman_a || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_skeleton_a || 
                    actor.SnoActor.Sno == ActorSnoEnum._fetish_melee_sycophants));
                    return (count > 3 && ctx.Hud.Game.Me.Powers.BuffIsActive(318724));
                }).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 40).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return (ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.WitchDoctor_Passive_GraveInjustice.Sno) && ctx.Hud.Game.Me.Powers.BuffIsActive(484128));
                }).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.WitchDoctor_BigBadVoodoo, 4, 500, 1000).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Stats.CooldownReduction > 0.6 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.WitchDoctor_Passive_GraveInjustice.Sno);
                }).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx =>20, false).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var BigBadVoodooCount = S7oMacroQueries.Actors(ctx.Hud).Count(actor => actor.SummonerAcdDynamicId == ctx.Skill.Player.SummonerId && actor.CentralXyDistanceToMe < 30 &&
                    (actor.SnoActor.Sno == ActorSnoEnum._witchdoctor_bigbadvoodoo_fetish ||
                    actor.SnoActor.Sno == ActorSnoEnum._witchdoctor_bigbadvoodoo_fetish_blue ||
                    actor.SnoActor.Sno == ActorSnoEnum._witchdoctor_bigbadvoodoo_fetish_red ||
                    actor.SnoActor.Sno == ActorSnoEnum._witchdoctor_bigbadvoodoo_fetish_yellow ||
                    actor.SnoActor.Sno == ActorSnoEnum._witchdoctor_bigbadvoodoo_fetish_purple))
                    ;
                    return (BigBadVoodooCount == 0);
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_WitchDoctorCorpseSpiders : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorCorpseSpiders()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_CorpseSpider;
            Rune = 1;
            CreateCastRule()
                .IfCanCastSkill(1000,1500,2000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfFalse(ctx =>
                {
                    bool nearbyCorpsespider = S7oMacroQueries.Actors(ctx.Hud).Any(x => (x.SnoActor.Sno == ActorSnoEnum._witchdoctor_corpsespider_indigorune || x.SnoActor.Sno == ActorSnoEnum._p72_witchdoctor_corpsespider_indigorune) && x.SummonerAcdDynamicId == ctx.Hud.Game.Me.SummonerId);
                    return nearbyCorpsespider;
                }).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorFetishArmy : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorFetishArmy()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseTpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_FetishArmy;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .ForceCast().ForSpecificCombatRole(S7oCombatRole.Support)
                .IfTrue(ctx => ctx.Phase == S7oCastPhase.Move).ThenNoCastElseContinue().ForSpecificCombatRole(S7oCombatRole.Default)
                .IfTrue(ctx =>
                {
                    var count = S7oMacroQueries.Actors(Hud).Count(actor =>
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_melee_a ||
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_doublestack_shaman_a ||
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_ranged_a ||
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_melee_itempassive ||
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_shaman_a ||
                        actor.SnoActor.Sno == ActorSnoEnum._fetish_skeleton_a);

                    if (ctx.Phase == S7oCastPhase.UseTpStart)
                    {
                        return count <= 4;
                    }

                    switch (ctx.Skill.Rune)
                    {
                        case 1:
                            return count < 8;
                        case 2:
                        case 4:
                            return count < 7;
                    }

                    return count < 5;
                }).ThenCastElseContinue().ForSpecificCombatRole(S7oCombatRole.Default);
        }
    }


    internal sealed class S7oPolicy_WitchDoctorGargantuan : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorGargantuan()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.PreAttack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_Gargantuan;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var exists = S7oMacroQueries.Actors(Hud).Any(actor =>
                        actor.SnoActor.Sno == ActorSnoEnum._wd_gargantuan_slam ||
                        actor.SnoActor.Sno == ActorSnoEnum._wd_gargantuan_cleave ||
                        actor.SnoActor.Sno == ActorSnoEnum._wd_gargantuan_poison ||
                        actor.SnoActor.Sno == ActorSnoEnum._wd_gargantuan_cooldown);
                    return exists;
                }).ThenNoCastElseContinue()
                .IfEliteOrBossNearbyCursor(ctx => 12).ThenCastElseContinue()
                ;
        }
    }



    internal sealed class S7oPolicy_WitchDoctorHex : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorHex()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_Hex;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.BaneOfTheStrickenPrimary).ThenNoCastElseContinue()
                .IfTrue(ctx => Hud.Game.Me.GetSetItemCount(859926) >= 4).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.WitchDoctor_Hex, 5,500,1000).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorHorrify : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorHorrify()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_Horrify;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => ctx.Skill.Rune == 1 ? 24 : 18, ctx => ctx.Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.RechelsRingOfLarceny.Sno) == true ? 1 : 5).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => ctx.Skill.Rune == 1 ? 24 : 18).ThenCastElseContinue()
                .IfTrue(ctx =>
                S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, ctx.Skill.Rune == 1 ? 24 : 18) > 0 &&
                ctx.Hud.Game.Me.Powers.BuffIsActive(402459)).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 50) > 0).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.WitchDoctor_Horrify, 2, 100, 500).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WitchDoctorLocustSwarm : S7oImportedPolicy
	{
        private IScreenCoordinate LastScreenCoordinate = null;
        private bool moveback = false;
        public S7oPolicy_WitchDoctorLocustSwarm()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_LocustSwarm;
            CreateCastRule()
                .IfTrue(ctx =>
                {
                    if (moveback == true)
                    {
                        S7oMacroQueries.RequestAim(ctx.Hud, LastScreenCoordinate.X, LastScreenCoordinate.Y);
                        moveback = false;
                    }
                    return true;
                })
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.RingOfEmptiness).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wormwood).ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfCanCastSkill(1000, 1000, 3000).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IMonster monster = S7oMacroQueries.Monsters(ctx.Hud).Where(m => (!m.Locust && (m.IsElite || m.Rarity == ActorRarity.Boss)) && m.IsOnScreen && !m.Invisible && !m.Invulnerable && m.NormalizedXyDistanceToMe <= 20).OrderBy(m => m.NormalizedXyDistanceToMe).FirstOrDefault();
                    if (monster != null)
                    {
                        IScreenCoordinate monsterCoordinate = monster.ScreenCoordinate;
                        LastScreenCoordinate = ctx.Hud.Window.CreateScreenCoordinate(ctx.Hud.Window.CursorX, ctx.Hud.Window.CursorY);
                        S7oMacroQueries.RequestAim(ctx.Hud, monsterCoordinate.X, monsterCoordinate.Y);
                        moveback = true;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                    
                }).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_WitchDoctorMassConfusion : S7oImportedPolicy
    {
        public int ActivationRange { get; set; }

        public S7oPolicy_WitchDoctorMassConfusion()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack)
        {
            Enabled = false;
            ActivationRange = 20;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_MassConfusion;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 20).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 20, ctx => 5).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WitchDoctorPiranhas : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorPiranhas()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_Piranhas;
            Rune = 2;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEliteOrBossNearbyCursor(ctx => 12).ThenCastElseContinue()
                .IfAttacking().ThenContinueElseNoCast()
                .IfEnoughMonstersNearbyCursor(ctx => 12, ctx => 3).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorSacrifice : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorSacrifice()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_Sacrifice;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfCanCastSkill(300, 500, 1000).ThenContinueElseNoCast()
                .IfRunning().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0).ThenContinueElseNoCast()
                .IfEliteOrBossNearbyCursor(ctx => 12).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroMechanics.GetBuffCount (ctx.Hud, Hud.Sno.SnoPowers.WitchDoctor_Sacrifice.Sno, 0) < 5).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_WitchDoctorSoulHarvestJadeHarvester : S7oImportedPolicy
    {
        public int ActivationRange { get; set; }

        public S7oPolicy_WitchDoctorSoulHarvestJadeHarvester()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack, S7oCastPhase.Attack, S7oCastPhase.Move)
        {
            Enabled = false;
            ActivationRange = 18;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return (Hud.Game.Me.GetSetItemCount(842970) >= 6);
                }
                ).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    int Stacks = ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.SacredHarvester.Sno) == true ? 10 : 5;
                    return (
                    !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest.Sno, 5) &&
                    S7oMacroQueries.CountNear(ctx.Hud, ctx.Skill.Player, 18) >= Stacks
                    )
                    ;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var monsters = S7oMacroQueries.Monsters(Hud).Where(m => ((m.SummonerAcdDynamicId == 0 && m.IsElite) || !m.IsElite) && m.NormalizedXyDistanceToMe <= 18);
                    int Count = 0;
                    bool RingOfEmptiness = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.RingOfEmptiness.Sno, 0);
                    foreach (var monster in monsters)
                    {
                        if(RingOfEmptiness)
                        {
                            if (monster.Haunted || monster.Locust) Count++;
                        }
                       else
                        {
                            if (monster.Haunted) Count++;
                        }
                    }
                    return (Count >= 1 && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest.Sno, 5));
                }
                ).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    return (Hud.Game.Me.GetSetItemCount(842970) >= 6 && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest.Sno, 0));
                }
                ).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest, 5, 300, 500).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorSoulHarvest : S7oImportedPolicy
    {
        public int ActivationRange { get; set; }

        public S7oPolicy_WitchDoctorSoulHarvest()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.PreAttack, S7oCastPhase.Attack, S7oCastPhase.Move)
        {
            Enabled = false;
            ActivationRange = 18;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 18, ctx => 3).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(ctx => 18).ThenCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18, ctx =>
                {
                    var stack = ctx.Skill.Buff != null && ctx.Skill.Buff.Active ? S7oMacroData.Count((ctx.Skill.Buff)?.IconCounts, 0) : 0;
                    return Math.Max(2, stack);
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WitchDoctorSpiritBarragePhantasm : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorSpiritBarragePhantasm()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.Move, S7oCastPhase.Attack)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_SpiritBarrage;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfTrue(ctx =>ctx.Skill.Rune == 2).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(200, ctx => 100).ThenContinueElseNoCast()
                .IfTrue(ctx =>S7oMacroQueries.IsHotKeySet(hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(hud, ActionKey.Move)).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    var actorCount = S7oMacroQueries.Actors(Hud).Count(actor =>
                        actor.SnoActor.Sno == ActorSnoEnum._wd_spiritbarragerune_aoe_ghostmodel &&
                        actor.SummonerAcdDynamicId == Hud.Game.Me.SummonerId &&
                        actor.NormalizedXyDistanceToMe <= 60);
                    return actorCount < 3;
                }).ThenCastElseContinue();


            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(200, ctx => 100).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(300, 1000, 3).ThenCastElseContinue();

        }
    }



    internal sealed class S7oPolicy_WitchDoctorSpiritWalk : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorSpiritWalk()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_SpiritWalk;
            
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.GetSetItemCount(585361) >= 4).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_P68ItemPassiveUniqueRing009, 1,500,1000).ThenCastElseContinue()
                ;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ShukranisTriumph.Sno)).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorWallOfDeath : S7oImportedPolicy
    {
        public S7oPolicy_WitchDoctorWallOfDeath()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_WallOfDeath;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Me.GetSetItemCount(860188) >= 6).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 200, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Generic_P3ItemPassiveUniqueRing010, 1, 300, 500).ThenCastElseContinue()
                .IfTrue(ctx => {
                    bool CommuningWithSpirits = ctx.Skill.Rune == 2;
                    var cur = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY);
                    var isAnyUncursedMonster = S7oMacroQueries.Monsters(Hud).Any(m => (m.Rarity == ActorRarity.Boss || m.Rarity == ActorRarity.Rare || m.Rarity == ActorRarity.Champion || m.Rarity == ActorRarity.Unique || m.SnoMonster.Priority == MonsterPriority.goblin || m.SnoMonster.Priority == MonsterPriority.keywarden) && !m.Invisible && !m.Invulnerable && !m.Illusion && m.GetAttributeValueAsInt(Hud.Sno.Attributes.Power_Buff_2_Visual_Effect_C, 134837) != 1 && (m.FloorCoordinate.XYZDistanceTo(cur.ToWorldCoordinate()) - m.RadiusBottom) <= 19 && !m.AffixSnoList.Any(a => a.Affix == MonsterAffix.Juggernaut));
                    return CommuningWithSpirits && isAnyUncursedMonster;
                }).ThenCastElseContinue()
                ;

        }
    }



    internal sealed class S7oPolicy_WitchDoctorZombieDogs : S7oImportedPolicy
    {
        public int dog { get; set; }
        public S7oPolicy_WitchDoctorZombieDogs()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            dog = 3;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.WitchDoctor_SummonZombieDog;

            CreateCastRule()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var count = S7oMacroQueries.Actors(Hud).Count(actor => 
                    actor.SummonerAcdDynamicId == ctx.Hud.Game.Me.SummonerId &&
                    (actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedogrune_fire ||
                    actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedogrune_healthglobe ||
                    actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedogrune_healthlink ||
                    actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedogrune_lifesteal ||
                    actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedogrune_poison ||
                    actor.SnoActor.Sno == ActorSnoEnum._wd_zombiedog));
                    if (count >= dog) return false;

                    if ((count >= 1) && ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheTallMansFinger.Sno)) return false;

                    return true;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WizardArcaneOrb : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArcaneOrb()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArcaneOrb;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.Wizard_ArcaneOrb.Sno, 5) && ctx.Skill.Player.Powers.UsedWizardPowers.ArcaneOrb.Rune == 2;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardArchonArcaneBlastCold : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArchonArcaneBlastCold()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastCold;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18 , ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardArchonArcaneBlastFire : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArchonArcaneBlastFire()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastFire;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18 , ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardArchonArcaneBlastLightning : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArchonArcaneBlastLightning()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlastLightning;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18 , ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardArchonArcaneBlast : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArchonArcaneBlast()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArchonArcaneBlast;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 18 , ctx => 1).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardArchon : S7oImportedPolicy
    {
        public int buffStack { get; set; }
        public S7oPolicy_WizardArchon()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            buffStack = 20;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Archon;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool active = false;
                    var buff = Hud.Game.Me.Powers.GetBuff(440235);
                    var BlackHole = Hud.Game.Me.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Wizard_BlackHole);
                    var BlackHoleBuff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Wizard_BlackHole.Sno);
                    var cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    int count = S7oMacroQueries.Monsters(Hud).Where(m => m.FloorCoordinate.XYDistanceTo(cursor) < 15 + m.RadiusBottom).Count();
                    if (buff != null && S7oMacroData.Count((buff)?.IconCounts, 0) >= buffStack -  
                    (BlackHole != null && !BlackHole.IsOnCooldown && 
                    (!Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Wizard_BlackHole.Sno) || (BlackHoleBuff != null && ((S7oMacroData.Seconds((BlackHoleBuff)?.TimeLeftSeconds, 8) * S7oMacroData.Count((BlackHoleBuff)?.IconCounts, 8)) < (count * 10)))) 
                    ? 1:0))
                    {
                        if(BlackHole != null && !BlackHole.IsOnCooldown) S7oMacroQueries.RequestPrerequisite(hud, BlackHole.Key);
                        active = true;
                    }
                    return active;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WizardArchonSlowTime : S7oImportedPolicy
    {
        public int SpareResource { get; set; }

        public S7oPolicy_WizardArchonSlowTime()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ArchonSlowTime;
            CreateCastRule()
                .IfCanCastSkill(50,100,200)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    bool isSlowTime = Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Wizard_ArchonSlowTime.Sno);
                    return isSlowTime;
                }
                ).ThenContinueElseCast()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardBlackHole : S7oImportedPolicy
    {
        public int SpareResource { get; set; }
        private int Stacks = 0;
        public S7oPolicy_WizardBlackHole()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_BlackHole;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.StoneOfJordan).ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 80).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                ctx.Skill.Player.GetSetItemCount(84014) >= 6 && ctx.Skill.Player.Powers.UsedLegendaryPowers.Deathwish?.Active == true).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    int CoeIndex = S7oMacroMechanics.Options.CoeIndex;
                    bool IsElementReady = S7oMacroMechanics.IsElementReady(hud, ctx.Skill.Rune == 4 ? 2 : 2, ctx.Skill.Player, CoeIndex);
                    return IsElementReady;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var buff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Wizard_BlackHole.Sno);
                    if (buff != null)
                    {
                        Stacks = S7oMacroData.Count((buff)?.IconCounts, 8) != 0 ? S7oMacroData.Count((buff)?.IconCounts, 8) : S7oMacroData.Count((buff)?.IconCounts, 5);
                    }
                    return true;
                })
                .IfEnoughMonstersNearbyCursor(ctx => 15, ctx => Stacks).ThenContinueElseNoCast()
                .IfEnoughMonstersNearbyCursor(ctx => 15, ctx => 5).ThenCastElseContinue()
                .IfEliteOrBossNearbyCursor(ctx => 10).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfTrue(ctx => !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.ConventionOfElements.Sno) || ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.StoneOfJordan.Sno)).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx =>0).ThenContinueElseNoCast()
                .IfTrue(ctx =>ctx.Skill.Rune == 3 || ctx.Skill.Rune == 4).ThenContinueElseNoCast()
                .IfEliteOrBossNearbyCursor(ctx => 10).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var buff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Wizard_BlackHole.Sno);
                    if (buff != null)
                    {
                        Stacks = S7oMacroData.Count((buff)?.IconCounts, 8) != 0 ? S7oMacroData.Count((buff)?.IconCounts, 8) : S7oMacroData.Count((buff)?.IconCounts, 5);
                    }
                    return true;
                })
                .IfEnoughMonstersNearbyCursor(ctx => 15, ctx => Stacks).ThenCastElseContinue()
                .IfBuffIsAboutToExpire(1000, 1500).ThenCastElseContinue()
                ;

        }
    }


    internal sealed class S7oPolicy_WizardBlizzard : S7oImportedPolicy
    {
        public S7oPolicy_WizardBlizzard()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Blizzard;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.WinterFlurry).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    var HydraCount = S7oMacroQueries.Actors(ctx.Hud).Where(act => act.SummonerAcdDynamicId == ctx.Skill.Player.SummonerAcdDynamicId && 
                    (
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_defaultfire_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runearcane_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runefrost_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runelightning_pool
                    ) && act.FloorCoordinate.XYZDistanceTo(cursor) <= (ctx.Skill.Rune == 1 ? 30 : 12)).Count();
                    return HydraCount >= 1;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    var Monsters = S7oMacroQueries.Monsters(ctx.Hud).Where(m => 
                    
                    m.FloorCoordinate.XYZDistanceTo(cursor) <= (ctx.Skill.Rune == 1 ? 15 : 6) && !m.Invulnerable && !m.Invisible && !m.Illusion &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_None, 30680) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_C, 30680) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_E, 30680) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_D, 30680) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_B, 30680) != 1 &&
                    m.GetAttributeValue(Hud.Sno.Attributes.Power_Buff_3_Visual_Effect_A, 30680) != 1
                    );
                    return Monsters.Count() > 0;
                }).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    bool isInBlizzard = S7oMacroQueries.Actors(ctx.Hud).Any(x => (
                    x.SnoActor.Sno == ActorSnoEnum._wizard_blizzard ||
                    x.SnoActor.Sno == ActorSnoEnum._wizard_blizzard_addfreeze ||
                    x.SnoActor.Sno == ActorSnoEnum._wizard_blizzard_addtime ||
                    x.SnoActor.Sno == ActorSnoEnum._wizard_blizzard_addsize ||
                    x.SnoActor.Sno == ActorSnoEnum._wizard_blizzard_reducecost
                    ) && (x.FloorCoordinate.XYZDistanceTo(cursor) < (ctx.Skill.Rune == 1 ? 15 : 6)));
                    return isInBlizzard;
                }
                ).ThenContinueElseCast()
                .IfCanCastSkill(1500, 1500, 10000).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardDiamondSkin : S7oImportedPolicy
    {
        public S7oPolicy_WizardDiamondSkin()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_DiamondSkin;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Stats.CooldownReduction >= 0.6 && (ctx.Skill.Rune == 1 ? true : (ctx.Hud.Game.Me.Powers.BuffIsActive(402459) || ctx.Hud.Game.Me.Powers.BuffIsActive(hud.Sno.SnoPowers.MesserschmidtsReaver.Sno)));
                }).ThenCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Ingeom, 1).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(100, 500).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Rune == 3) && ((ctx.Skill.Player.AnimationState == AcdAnimationState.Attacking || ctx.Skill.Player.AnimationState == AcdAnimationState.Channeling) && (ctx.Skill.Player.Powers.UsedLegendaryPowers.AetherWalker?.Active == true ? true : (ctx.Skill.Player.Animation != AnimSnoEnum._wizard_female_hth_spellcast_teleport && ctx.Skill.Player.Animation != AnimSnoEnum._wizard_male_hth_spellcast_teleport && ctx.Skill.Player.Animation != AnimSnoEnum._wizard_female_archon_cast_teleport_01 && ctx.Skill.Player.Animation != AnimSnoEnum._wizard_male_archon_cast_teleport_01)))).ThenCastElseContinue()
                .IfTrue(ctx => (ctx.Skill.Rune == 1) && (ctx.Skill.Player.AnimationState == AcdAnimationState.Attacking || ctx.Skill.Player.AnimationState == AcdAnimationState.Channeling)).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0 && ctx.Skill.Player.AnimationState == AcdAnimationState.Running).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardDisintegrate : S7oImportedPolicy
    {
        public int Firebird { get; set; }
        public S7oPolicy_WizardDisintegrate()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
            Firebird = 44;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Disintegrate;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(84014) >= 6).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.InGreaterRiftRank > 0 && IsGuardianDead).ThenNoCastElseContinue()
                .IfTrue(ctx => (S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)) || S7oMacroQueries.IsActionHeld(ctx.Hud, ctx.Skill.Player.Powers.GetUsedSkill(Hud.Sno.SnoPowers.Wizard_Disintegrate).Key)).ThenNoCastElseContinue()
                .IfTrue(ctx => {
                    var buff = ctx.Skill.Player.Powers.GetBuff(485549);
                    if(buff != null && S7oMacroData.Count((buff)?.IconCounts, 3) >= 1 && S7oMacroData.Count((buff)?.IconCounts, 3) <= Firebird)
                    {
                        return true;
                    }
                    return false;
                    }).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.TaegukPrimary.Sno)).ThenContinueElseNoCast()
                .IfTrue(ctx => Hud.Game.Shrines.Any(x => !x.IsDisabled && !x.IsOperated && x.IsPylon) || S7oMacroQueries.EliteOrBossNear(ctx.Hud, 200, false) || S7oMacroQueries.NearestGoblin(ctx.Hud) != null).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.TaegukPrimary, 1, 550, 550).ThenCastElseContinue()
                ;
        }
        private bool IsGuardianDead
        {
            get
            {
                if (Hud.Game.Monsters.Any(m => m.Rarity == ActorRarity.Boss && !m.IsAlive))
                    return true;
                return riftQuest != null && (riftQuest.QuestStepId == 5 || riftQuest.QuestStepId == 10 || riftQuest.QuestStepId == 34 || riftQuest.QuestStepId == 46);
            }
        }
        private IQuest riftQuest
        {
            get
            {
                return Hud.Game.Quests.FirstOrDefault(q => q.SnoQuest.Sno == 337492);   
            }
        }

    }


    internal sealed class S7oPolicy_WizardElectrocute : S7oImportedPolicy
	{
        public S7oPolicy_WizardElectrocute()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Electrocute;
            
            CreateCastRule()
                .IfCanCastSkill(100, 150, 500).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue() 
                .IfTrue(ctx=> S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Player.Powers.BuffIsActive(359583, 0)).ThenContinueElseNoCast()
                .IfEnoughMonstersNearbyCursor(ctx => 10, ctx => 1).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Generic_ItemPassiveUniqueRing735x1, 1, 30, 100).ThenCastElseNoCast()
                ;
            
            CreateCastRule()
                .IfCanCastSkill(50, 50, 500).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfTrue(ctx=> isArchonMeteorBuild())
                .IfTrue(ctx => S7oMacroQueries.IsHotKeySet(ctx.Hud, ActionKey.Move) && S7oMacroQueries.IsActionHeld(ctx.Hud, ActionKey.Move)).ThenNoCastElseContinue()
                .IfRunning().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_Archon, 2).ThenNoCastElseContinue()
                .IfTrue(ctx => S7oMacroMechanics.IsElementReadySoon(ctx.Hud, 3, ctx.Skill.Player, 1)).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroMechanics.GetBuffCount(ctx.Hud, hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno, 1) < 5).ThenCastElseNoCast()
            ;
        }
        private bool isArchonMeteorBuild()
        {
            return Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno) && 
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheSwami.Sno) && 
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.NilfursBoast.Sno) &&
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FazulasImprobableChain.Sno)
                ;
        }
    }


    internal sealed class S7oPolicy_WizardEnergyArmor : S7oImportedPolicy
    {
        public S7oPolicy_WizardEnergyArmor()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_EnergyArmor;

            CreateCastRule()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(55000, 65000).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.UsedWizardPowers.Archon != null && ctx.Skill.RemainingBuffTime() < 120;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WizardExplosiveBlast : S7oImportedPolicy
    {
        public S7oPolicy_WizardExplosiveBlast()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ExplosiveBlast;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 500)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => isArchonMeteorBuild()).ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfTrue(ctx => {
                    return ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.WandOfWoh.Sno) || 
                    (ctx.Skill.Player.Stats.ResourcePctPri >= 70 && !ctx.Skill.Player.Powers.BuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno)) || 
                    (ctx.Skill.Player.GetSetItemCount(84014) >= 6 && ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.Deathwish.Sno))
                    ;
                }).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(GetBlastRange, ctx => 1).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(GetBlastRange, true).ThenCastElseContinue();

            CreateCastRule()
                
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.WandOfWoh).ThenNoCastElseContinue()
                .IfCanCastSkill(100, 150, 500)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfSpecificBuffIsActive(hud.Sno.SnoPowers.OrbOfInfiniteDepth).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(hud.Sno.SnoPowers.Wizard_ExplosiveBlast, 1, 1700 ,2000, false, 4).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroMechanics.GetBuffCount(ctx.Hud, hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno, 1) == 5).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(GetBlastRange, ctx => 1).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(GetBlastRange, true).ThenCastElseContinue();
            CreateCastRule()
                
                .IfCanCastSkill(100, 150, 500)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => isArchonMeteorBuild()).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_Archon, 2).ThenNoCastElseContinue()
                
                .IfTrue(ctx =>
                {
                    var coe = S7oMacroMechanics.GetBuffLeftTime(ctx.Hud, Hud.Sno.SnoPowers.ConventionOfElements.Sno, 5);
                    return coe >= 2 && coe <= 3;
                }).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(GetBlastRange, ctx => 1).ThenCastElseContinue()
                .IfEliteOrBossIsNearby(GetBlastRange, true).ThenCastElseContinue();
        }

        private bool isArchonMeteorBuild()
        {
            return Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.TheSwami.Sno) && 
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.OrbOfInfiniteDepth.Sno) && 
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.NilfursBoast.Sno) &&
                Hud.Game.Me.Powers.BuffIsActive(Hud.Sno.SnoPowers.FazulasImprobableChain.Sno)
                ;
        }
        private int GetBlastRange(S7oTestContext ctx)
        {
            switch (ctx.Skill.Rune)
            {
                case 0: return 12; 
                case 1: return 18 + (int)((20 * (1 + Hud.Game.Me.Stats.MoveSpeed / 100)) * 1.5); 
                default: return 12 + (int)((20 * (1 + Hud.Game.Me.Stats.MoveSpeed / 100)) * 1.5); 
            }
        }
    }


    internal sealed class S7oPolicy_WizardFamiliar : S7oImportedPolicy
    {
        public S7oPolicy_WizardFamiliar()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Familiar;

            CreateCastRule()
                .IfCanCastSkill(100, 200, 500).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(1000, 2000).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.UsedWizardPowers.Archon != null && ctx.Skill.RemainingBuffTime() < 120;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var SpeedBuff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Generic_PagesBuffRunSpeed.Sno);
                    var isSpeedActive = (SpeedBuff == null || SpeedBuff.Active == false || S7oMacroData.Elapsed((SpeedBuff)?.TimeElapsedSeconds, 0) > 0.5d) ? false : true;
                    return isSpeedActive;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardFrostNova : S7oImportedPolicy
    {
        public int SpareResource { get; set; }
        private IWatch CD;
        public S7oPolicy_WizardFrostNova()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            CD = Hud.Time.CreateWatch();
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_FrostNova;
            CreateCastRule()
                .IfCanCastSkill(150, 250, 2000).ThenContinueElseNoCast()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune != 2 && ctx.Hud.Game.Players.Any(p => p.HasValidActor && !p.IsMe) && ctx.Hud.Game.Me.InGreaterRiftRank > 0 && ctx.Hud.Game.RiftPercentage >= 100).ThenNoCastElseContinue()
                .IfEnoughMonstersNearby(ctx => 16, ctx => ctx.Skill.Rune == 4 ? 5 : 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    bool Obsidian = ctx.Skill.Player.Powers.BuffIsActive(hud.Sno.SnoPowers.ObsidianRingOfTheZodiac.Sno);
                    bool ColdSnap = ctx.Skill.Rune == 3;
                    bool FrozenMist = ctx.Skill.Rune == 2;
                    bool BoneChill = ctx.Skill.Rune == 0;
                    if (BoneChill)
                        return false;
                    if (FrozenMist)
                        return true;
                    if (!Obsidian)
                        return true;
                    if (Obsidian && (!CD.IsRunning || CD.ElapsedMilliseconds >= (ColdSnap ? 2500 : 1500)))
                    {
                        CD.Restart();
                        return true;
                    }
                    return false;
                }
                ).ThenCastElseContinue()
                .IfTrue(ctx => ctx.Skill.Rune == 0).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 16).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardHydra : S7oImportedPolicy
    {
        public S7oPolicy_WizardHydra()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Hydra;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 100, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx => ctx.Skill.Player.GetSetItemCount(59609) >=4 && ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.SerpentsSparker.Sno) && ctx.Skill.Rune != 3).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    var HydraBuff = ctx.Skill.Player.Powers.GetBuff(ctx.Hud.Sno.SnoPowers.Wizard_Hydra.Sno);
                    return !ctx.Skill.BuffIsActive || S7oMacroData.NullableCount(HydraBuff, 7) <= 8;
                }
                ).ThenCastElseContinue()
                
                .IfEnoughMonstersNearbyCursor(ctx => 15, ctx => 1).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    IWorldCoordinate cursor = Hud.Window.CreateScreenCoordinate(Hud.Window.CursorX, Hud.Window.CursorY).ToWorldCoordinate();
                    var HydraCount = S7oMacroQueries.Actors(ctx.Hud).Where(act => act.SummonerAcdDynamicId == ctx.Skill.Player.SummonerAcdDynamicId && 
                    (
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_defaultfire_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runearcane_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runefrost_pool ||
                    act.SnoActor.Sno == ActorSnoEnum._wizard_hydra_runelightning_pool
                    ) && act.FloorCoordinate.XYZDistanceTo(cursor) <= 15).Count();
                    var ArcaneDynamo = ctx.Skill.Player.Powers.GetBuff(ctx.Hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno);
                    bool isArcaneDynamoStackEnough = (ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno) ? S7oMacroData.NullableCount(ArcaneDynamo, 1) >= 5 : true);
                    return HydraCount < 2 && isArcaneDynamoStackEnough;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardIceArmor : S7oImportedPolicy
    {
        public S7oPolicy_WizardIceArmor()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_IceArmor;

            CreateCastRule()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(55000, 65000).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.UsedWizardPowers.Archon != null && ctx.Skill.RemainingBuffTime() < 120;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WizardMagicWeapon : S7oImportedPolicy
    {
        public S7oPolicy_WizardMagicWeapon()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_MagicWeapon;

            CreateCastRule()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(55000, 65000).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.UsedWizardPowers.Archon != null && ctx.Skill.RemainingBuffTime() < 120;
                }).ThenCastElseContinue();
        }
    }


    internal sealed class S7oPolicy_WizardMirrorImage : S7oImportedPolicy
    {
        public S7oPolicy_WizardMirrorImage()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_MirrorImage;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_MirrorImage, 0).ThenNoCastElseContinue()
                .IfTrue(ctx => 
                ctx.Skill.Player.GetSetItemCount(84014) >= 6).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    double coe = S7oMacroMechanics.GetHighestElementLeftSecond(Hud, ctx.Skill.Player, 1);
                    bool isCOE = ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno);
                    return (!isCOE || (isCOE && coe <= 11 && coe >= 0));
                }
                ).ThenContinueElseNoCast()
                .IfEliteOrBossIsNearby(ctx => 15, false).ThenCastElseContinue()
                ;

            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_MirrorImage, 0).ThenNoCastElseContinue()
                .IfTrue(ctx =>
                ctx.Skill.Player.Powers.BuffIsActive(ctx.Hud.Sno.SnoPowers.ConventionOfElements.Sno) &&
                ctx.Skill.Player.GetSetItemCount(84014) >= 6).ThenContinueElseNoCast()
                .IfTrue(ctx =>
                {
                    double coe = S7oMacroMechanics.GetHighestElementLeftSecond(Hud, ctx.Skill.Player, 1);
                    return (coe <= 9 && coe >= 0);
                }
                ).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 15, ctx => 10).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardStormArmor : S7oImportedPolicy
    {
        public S7oPolicy_WizardStormArmor()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_StormArmor;

            CreateCastRule()
                .IfCanCastSkill(100, 200, 500).ThenContinueElseNoCast()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(50, ctx => 25).ThenContinueElseNoCast()
                .IfBuffIsAboutToExpire(1000, 2000).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return ctx.Skill.Player.Powers.UsedWizardPowers.Archon != null && ctx.Skill.RemainingBuffTime() < 120;
                }).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    var SpeedBuff = ctx.Skill.Player.Powers.GetBuff(Hud.Sno.SnoPowers.Generic_PagesBuffRunSpeed.Sno);
                    var isSpeedActive = (SpeedBuff == null || SpeedBuff.Active == false || S7oMacroData.Elapsed((SpeedBuff)?.TimeElapsedSeconds, 0) > 0.5d) ? false : true;
                    return isSpeedActive;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardTriumvirate_Electrocute : S7oImportedPolicy
    {
        public S7oPolicy_WizardTriumvirate_Electrocute()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_Electrocute;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Triumvirate).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Triumvirate, 2, 1500, 1500).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return S7oMacroMechanics.GetBuffCount(Hud, Hud.Sno.SnoPowers.Triumvirate.Sno, 2) < 3;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardTriumvirate_MagicMissile : S7oImportedPolicy
    {
        public S7oPolicy_WizardTriumvirate_MagicMissile()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_MagicMissile;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Triumvirate).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Triumvirate, 2, 1500, 1500).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return S7oMacroMechanics.GetBuffCount(Hud, Hud.Sno.SnoPowers.Triumvirate.Sno, 2) < 3;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardTriumvirate_ShockPulse : S7oImportedPolicy
    {
        public S7oPolicy_WizardTriumvirate_ShockPulse()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_ShockPulse;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Triumvirate).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Triumvirate, 2, 1500, 1500).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return S7oMacroMechanics.GetBuffCount(Hud, Hud.Sno.SnoPowers.Triumvirate.Sno, 2) < 3;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardTriumvirate_SpectralBlade : S7oImportedPolicy
    {
        public S7oPolicy_WizardTriumvirate_SpectralBlade()
            : base(S7oCastType.BuffSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }
        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_SpectralBlade;
            CreateCastRule()
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfCanCastSimple().ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Triumvirate).ThenContinueElseNoCast()
                .IfSpecificBuffIsAboutToExpire(Hud.Sno.SnoPowers.Triumvirate, 2, 1500, 1500).ThenCastElseContinue()
                .IfTrue(ctx =>
                {
                    return S7oMacroMechanics.GetBuffCount(Hud, Hud.Sno.SnoPowers.Triumvirate.Sno, 2) < 3;
                }).ThenCastElseContinue()
                ;
        }
    }


    internal sealed class S7oPolicy_WizardWaveofForce : S7oImportedPolicy
    {
        public S7oPolicy_WizardWaveofForce()
            : base(S7oCastType.SimpleSkill, S7oCastPhase.AutoCast, S7oCastPhase.UseWpStart, S7oCastPhase.Move, S7oCastPhase.Attack, S7oCastPhase.AttackIdle)
        {
            Enabled = false;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);
            AssignedSnoPower = Hud.Sno.SnoPowers.Wizard_WaveOfForce;

            CreateCastRule()
                .IfCanCastSkill(100, 150, 500)
                .IfInTown().ThenNoCastElseContinue()
                .IfCastingIdentify().ThenNoCastElseContinue()
                .IfCastingPortal().ThenNoCastElseContinue()
                .IfOnCooldown().ThenNoCastElseContinue()
                .IfPrimaryResourceIsEnough(0, ctx => 0).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.FazulasImprobableChain).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.TheSwami).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.TheGrandVizier).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.ObsidianRingOfTheZodiac).ThenContinueElseNoCast()
                .IfSpecificBuffIsActive(Hud.Sno.SnoPowers.Wizard_Archon, 2).ThenNoCastElseContinue()
                .IfTrue(ctx => S7oMacroMechanics.GetCooldownTimerOfArchon(hud) >= 3).ThenContinueElseNoCast()
                .IfTrue(ctx => S7oMacroMechanics.GetBuffCount(ctx.Hud, hud.Sno.SnoPowers.Wizard_Passive_ArcaneDynamo.Sno,1) < 5).ThenContinueElseNoCast()
                .IfEnoughMonstersNearby(ctx => 20, ctx => 1).ThenCastElseContinue()
                ;
        }

    }

    internal static class S7oMacroMechanics
    {
        internal sealed class Configuration { public int CoeIndex=0,PartyCoeIndex=0; }
        public static readonly Configuration Options=new Configuration();
        private static readonly int[] Barb={2,3,5,6},Crus={3,4,5,6},Monk={2,3,4,5,6},Nec={2,6,7},Wd={2,3,6,7},Wiz={1,2,3,5};
        private static int[] Elements(IPlayer p)
        {
            switch(p.HeroClassDefinition.HeroClass)
            {case HeroClass.Monk:return Monk;case HeroClass.Necromancer:return Nec;case HeroClass.Crusader:return Crus;
             case HeroClass.WitchDoctor:return Wd;case HeroClass.Wizard:return Wiz;default:return Barb;}
        }
        private static double Bonus(IPlayer p,int i)
        {switch(i){case 1:return p.Offense.BonusToArcane;case 2:return p.Offense.BonusToCold;case 3:return p.Offense.BonusToFire;case 4:return p.Offense.BonusToHoly;case 5:return p.Offense.BonusToLightning;case 6:return p.Offense.BonusToPhysical;default:return p.Offense.BonusToPoison;}}
        public static int GetHighestElement(IController hud,IPlayer p,int CoeIndex=0)
        {
            if(CoeIndex>0)return CoeIndex;
            if(p==null||p.Powers==null||!p.Powers.BuffIsActive(430674u)||p.HeroClassDefinition==null)return 0;
            int best=0;double value=double.MinValue;foreach(int i in Elements(p)){double b=Bonus(p,i);if(b>value){value=b;best=i;}}return best;
        }
        public static int GetHighestElementPrevious(IController hud,IPlayer p,int CoeIndex=0)
        {int hi=GetHighestElement(hud,p,CoeIndex);var a=Elements(p);for(int i=0;i<a.Length;i++)if(a[i]==hi)return a[(i+a.Length-1)%a.Length];return 0;}
        public static double GetHighestElementLeftSecondAssingedPlayer(IController hud,IPlayer p,int CoeIndex=0)
        {
            if(p==null||p.Powers==null||p.HeroClassDefinition==null)return 0;
            var b=p.Powers.GetBuff(430674u);if(b==null||!b.Active||b.TimeLeftSeconds==null)return 0;
            var a=Elements(p);int target=GetHighestElement(hud,p,CoeIndex),current=-1,goal=-1;double left=0;
            for(int i=0;i<a.Length;i++)
            {if(a[i]==target)goal=i;if(a[i]<b.TimeLeftSeconds.Length&&S7oMacroData.Seconds((b)?.TimeLeftSeconds, a[i])>0){current=i;left=S7oMacroData.Seconds((b)?.TimeLeftSeconds, a[i]);}}
            if(current<0||goal<0)return 0;
            int turns=(goal-current+a.Length)%a.Length;
            return turns==0?(a.Length-1)*4+left:(turns-1)*4+left;
        }
        public static double GetHighestElementLeftSecond(IController hud,IPlayer player=null,int CoeIndex=0)
        {return GetHighestElementLeftSecondAssingedPlayer(hud,player??hud.Game.Me,CoeIndex);}
        public static bool IsElementReadySoon(IController hud,double second,IPlayer player,int CoeIndex=0)
        {if(player==null||player.Powers==null||player.HeroClassDefinition==null)return false;int previous=GetHighestElementPrevious(hud,player,CoeIndex);var b=player.Powers.GetBuff(430674u);return b!=null&&b.Active&&b.TimeLeftSeconds!=null&&previous>0&&previous<b.TimeLeftSeconds.Length&&S7oMacroData.Seconds((b)?.TimeLeftSeconds, previous)>0&&S7oMacroData.Seconds((b)?.TimeLeftSeconds, previous)<second;}
        public static bool IsElementReady(IController hud,double second,IPlayer player,int CoeIndex=0)
        {if(player==null||player.Powers==null)return false;int highest=GetHighestElement(hud,player,CoeIndex);var b=player.Powers.GetBuff(430674u);return b!=null&&b.Active&&b.TimeLeftSeconds!=null&&((highest>0&&highest<b.TimeLeftSeconds.Length&&S7oMacroData.Seconds((b)?.TimeLeftSeconds, highest)>0)||IsElementReadySoon(hud,second,player,CoeIndex));}
        public static double GetBuffLeftTime(IController hud,uint sno,int index)
        {var b=hud.Game.Me.Powers.GetBuff(sno);return b==null||!b.Active?0:S7oMacroData.Seconds(b.TimeLeftSeconds,index);}
        public static int GetBuffCount(IController hud,uint sno,int index)
        {var b=hud.Game.Me.Powers.GetBuff(sno);return b==null||!b.Active?0:S7oMacroData.Count(b.IconCounts,index);}
        public static double GetCooldownTimerOfArchon(IController hud)
        {var s=hud.Game.Me.Powers.GetUsedSkill(hud.Sno.SnoPowers.Wizard_Archon);return s==null?0:Math.Max(0,(s.CooldownFinishTick-hud.Game.CurrentGameTick)/60.0);}
        public static bool FindThisSkill(IController hud,IPlayer player,uint sno)
        {return player!=null&&player.Powers!=null&&player.Powers.UsedSkills.Any(s=>s!=null&&s.SnoPower!=null&&s.SnoPower.Sno==sno);}
        public static bool FindThisSkill(IController hud,IPlayer player,uint sno,byte rune)
        {return player!=null&&player.Powers!=null&&player.Powers.UsedSkills.Any(s=>s!=null&&s.SnoPower!=null&&s.SnoPower.Sno==sno&&s.Rune==rune);}
        public static bool isMobInSkillRange(IController hud,IMonster mob,float skillWidth,float skillRange,float heroChestZOffset=0)
        {
            if(mob==null||mob.FloorCoordinate==null||hud.Game.Me.FloorCoordinate==null||skillRange<=0)return false;
            var me=hud.Game.Me.FloorCoordinate;var aim=hud.Window.CreateScreenCoordinate(hud.Window.CursorX,hud.Window.CursorY).ToWorldCoordinate();
            if(aim==null)return false;
            double dx=aim.X-me.X,dy=aim.Y-me.Y,length=Math.Sqrt(dx*dx+dy*dy);if(length<0.001)return false;
            double ux=dx/length,uy=dy/length,x=mob.FloorCoordinate.X-me.X,y=mob.FloorCoordinate.Y-me.Y;
            double forward=x*ux+y*uy,lateral=Math.Abs(x*uy-y*ux),radius=Math.Max(0,mob.RadiusBottom);
            return forward>=-radius&&forward<=skillRange+radius&&lateral<=Math.Max(0,forward)*skillWidth/(2*skillRange)+radius;
        }
    }

}


namespace Turbo.Plugins.s7o
{
    // Bounded, nonblocking input lifetime. Callbacks retain exact input ownership and focus checks.
    internal sealed class S7oAutoSkillCastPulse
    {
        private Func<bool> _releaseAction, _releaseModifier;
        internal int Started { get; private set; }
        private int _duration;
        internal bool Active { get { return _releaseAction != null || _releaseModifier != null; } }
        internal bool TryStart(int now, int duration, Func<bool> down, Func<bool> up,
            Func<bool> modifierDown, Func<bool> modifierUp)
        {
            if (Active || down == null || up == null) return false;
            Started = now; _duration = Math.Max(1, duration);
            try
            {
                if (modifierDown != null)
                {
                    if (modifierUp == null || !modifierDown()) return false;
                    _releaseModifier = modifierUp;
                }
                if (!down()) { Cancel(); return false; }
                _releaseAction = up;
                return true;
            }
            catch { Cancel(); return false; }
        }
        internal void Update(int now, bool cancel)
        {
            if (Active && (cancel || unchecked((uint)(now - Started)) >= (uint)_duration)) Cancel();
        }
        internal void Cancel()
        {
            if (_releaseAction != null && TryRelease(_releaseAction)) _releaseAction = null;
            if (_releaseModifier != null && TryRelease(_releaseModifier)) _releaseModifier = null;
        }
        private static bool TryRelease(Func<bool> release)
        {
            try { return release(); } catch { return false; }
        }
    }
}
