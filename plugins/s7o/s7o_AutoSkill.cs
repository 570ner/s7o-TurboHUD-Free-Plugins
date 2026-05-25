using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SharpDX.Direct2D1;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_AutoSkill : BasePlugin, IAfterCollectHandler, IInGameTopPainter, IMouseClickHandler, INewAreaHandler
    {
        #region Settings

        public bool EnableManualSlotAutoCast = true;
        public bool EnableAutomaticBuffUpkeep = true;
        public bool EnableConditionalAutoCastProfiles = true;

        public int ConditionalProfileRecheckMs = 100;
        public int ConditionalProfileDelayMinMs = 250;
        public int ConditionalProfileDelayMaxMs = 600;

        public bool EnableHoverToggle = true;
        public bool EnableClickToggle = false;
        public int HoverToggleMs = 300;

        public bool UseForceStandstillForCasts = true;
        public bool AllowBuffUpkeepInTown = true;
        public bool BlockLeftSkillOnSelectedClickableActor = true;
        public ushort ForceStandstillVirtualKey = 0x10; // Shift

        public ushort Skill1VirtualKey = 0x31; // 1
        public ushort Skill2VirtualKey = 0x32; // 2
        public ushort Skill3VirtualKey = 0x33; // 3
        public ushort Skill4VirtualKey = 0x34; // 4
        public ushort HealVirtualKey = 0x51;   // Q

        public int GlobalCastDelayMs = 90;
        public int ManualSlotDelayMinMs = 120;
        public int ManualSlotDelayMaxMs = 220;
        public int BuffCastDelayMinMs = 350;
        public int BuffCastDelayMaxMs = 700;
        public int BuffRecheckMs = 150;

        public int BuffRefreshJitterChangeMs = 5000;
        public int ToggleFlashMs = 150;
        public int SkillCacheRefreshMs = 250;

        public float ToggleDotMinSize = 12.0f;
        public float ToggleDotMaxSize = 18.0f;
        public float ToggleDotScale = 0.24f;

        public bool PersistUserSettings = true;
        public bool DebugLogging = false;
        public int MaxDebugLogBytes = 1024 * 1024;

        #endregion

        #region Runtime State

        private readonly bool[] _enabledSlots = new bool[7];
        private readonly RectangleF[] _slotToggleRects = new RectangleF[7];
        private readonly int[] _slotFlashUntilTick = new int[7];

        private readonly Dictionary<uint, int> _lastCastTickByPower = new Dictionary<uint, int>();
        private readonly Dictionary<string, RandomNode> _randomNodes = new Dictionary<string, RandomNode>();
        private readonly Dictionary<string, int> _nextSkipLogTickByKey = new Dictionary<string, int>();

        private readonly List<SkillSlotState> _skillSlots = new List<SkillSlotState>();
        private readonly List<AutoBuffProfile> _buffProfiles = new List<AutoBuffProfile>();
        private readonly HashSet<uint> _buffProfileSnos = new HashSet<uint>();
        private readonly List<ConditionalCastProfile> _conditionalProfiles = new List<ConditionalCastProfile>();
        private readonly HashSet<uint> _conditionalProfileSnos = new HashSet<uint>();
        private readonly Dictionary<string, int> _lastConditionalProfileCastTickByCode = new Dictionary<string, int>();
        private readonly HashSet<uint> _channelingPowerSnos = new HashSet<uint>();

        private int _nextSkillCacheRefreshTick;
        private int _nextBuffCheckTick;
        private int _nextConditionalProfileCheckTick;
        private int _lastGlobalCastTick;
        private int _lastActMapVisibleTick;
        private int _lastWorldMapVisibleTick;
        private int _nextBlockedLogTick;

        private int _hoverSlotIndex = -1;
        private int _hoverStartTick;
        private int _hoverCompletedSlot = -1;

        private string _lastBlockedReason;
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
        private IUiElement _vendorMainPage;
        private IUiElement _shopMainPanel;

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
            LoadUserSettings();
            BuildChannelingSkillSet();
            RefreshSkillCache(true);

            if (PersistUserSettings && !File.Exists(_settingsPath))
                SaveUserSettings();

            LogDebug("s7o_AutoSkill loaded. BuffProfiles="
                + _buffProfiles.Count.ToString(CultureInfo.InvariantCulture)
                + ", ConditionalProfiles="
                + _conditionalProfiles.Count.ToString(CultureInfo.InvariantCulture));
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            _lastGlobalCastTick = 0;
            _lastCastTickByPower.Clear();
            _nextSkipLogTickByKey.Clear();
            _nextSkillCacheRefreshTick = 0;
            _nextBuffCheckTick = 0;
            _nextConditionalProfileCheckTick = 0;
            _lastConditionalProfileCastTickByCode.Clear();
            _lastActMapVisibleTick = 0;
            _lastWorldMapVisibleTick = 0;
            _lastBlockedReason = null;
            _nextBlockedLogTick = 0;
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
            _vendorMainPage = RegisterOrGetUiElement("Root.NormalLayer.vendor_dialog_mainPage");
            _shopMainPanel = RegisterOrGetUiElement("Root.NormalLayer.shop_dialog_mainPage.panel");
        }

        #endregion

        #region AfterCollect Main Loop

        public void AfterCollect()
        {
            int now = Environment.TickCount;

            UpdateRecentMapTicks(now);
            RefreshSkillCacheIfNeeded(now);
            UpdateHoverToggle(now);

            if (EnableAutomaticBuffUpkeep && IsTickReached(now, _nextBuffCheckTick))
            {
                string buffBlockedReason;
                if (IsValidAutomationContext(AllowBuffUpkeepInTown, out buffBlockedReason))
                {
                    _nextBuffCheckTick = now + Math.Max(50, BuffRecheckMs);

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
                string conditionalBlockedReason;
                if (IsValidAutomationContext(false, out conditionalBlockedReason))
                {
                    _nextConditionalProfileCheckTick = now + Math.Max(50, ConditionalProfileRecheckMs);

                    if (TryRunConditionalAutoCastProfiles(now))
                        return;
                }
                else
                {
                    LogContextBlockedThrottled("conditional: " + conditionalBlockedReason, now);
                }
            }

            string manualBlockedReason;
            if (!IsValidAutomationContext(false, out manualBlockedReason))
            {
                LogContextBlockedThrottled("manual: " + manualBlockedReason, now);
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

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
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

        private bool IsValidAutomationContext(out string reason)
        {
            return IsValidAutomationContext(false, out reason);
        }

        private bool IsValidAutomationContext(bool allowTown, out string reason)
        {
            reason = null;

            if (!Enabled) { reason = "plugin disabled"; return false; }
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null) { reason = "hud/game/me null"; return false; }
            if (!Hud.Game.IsInGame) { reason = "not in game"; return false; }
            if (Hud.Game.IsLoading) { reason = "loading"; return false; }
            if (Hud.Game.IsPaused) { reason = "paused"; return false; }
            if (Hud.Game.IsInTown && !allowTown) { reason = "in town"; return false; }
            if (Hud.Window == null || !Hud.Window.IsForeground) { reason = "window not foreground"; return false; }
            if (Hud.Render == null || Hud.Render.MinimapUiElement == null || !Hud.Render.MinimapUiElement.Visible) { reason = "minimap hidden"; return false; }

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
                if (Hud == null || Hud.Game == null || Hud.Render == null)
                    return;

                if (Hud.Render.ActMapUiElement != null && Hud.Render.ActMapUiElement.Visible)
                    _lastActMapVisibleTick = now;

                if (Hud.Render.WorldMapUiElement != null && Hud.Render.WorldMapUiElement.Visible)
                    _lastWorldMapVisibleTick = now;

                if (Hud.Game.MapMode == MapMode.ActMap)
                    _lastActMapVisibleTick = now;

                if (Hud.Game.MapMode == MapMode.Map || Hud.Game.MapMode == MapMode.PermaMap || Hud.Game.MapMode == MapMode.WaypointMap)
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

        private bool IsUiVisible(IUiElement ui)
        {
            try
            {
                return ui != null && ui.Visible;
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
            var slots = powers.SkillSlots;

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
                    AddOrUpdateSkillSlot(6, ActionKey.Heal, potion);
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

                if (slot.SlotIndex == 6)
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
                case ActionKey.Heal: return 6;
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
                case 6: return ActionKey.Heal;
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

        private bool TryRunManualSlotAutoCast(int now)
        {
            for (int slotIndex = 0; slotIndex < _enabledSlots.Length; slotIndex++)
            {
                if (!_enabledSlots[slotIndex])
                    continue;

                var slot = FindSlotState(slotIndex);
                if (slot == null || !slot.IsValid || slot.Skill == null)
                    continue;

                var skill = ResolveSkillForSlot(slotIndex);
                if (skill == null || skill.SnoPower == null)
                    continue;

                int delay = GetRandomizedDelay("manual_" + skill.SnoPower.Sno.ToString(CultureInfo.InvariantCulture), ManualSlotDelayMinMs, ManualSlotDelayMaxMs, 1000);

                string reason;
                if (!CanCastSkill(skill, slot.ActionKey, now, delay, out reason))
                {
                    LogDebugCastSkipThrottled("manual", skill, reason, now);
                    continue;
                }

                if (DoAction(slot.ActionKey, UseForceStandstillForCasts))
                {
                    MarkSkillCast(skill, now);
                    LogDebug("Manual auto-cast: slot=" + slotIndex.ToString(CultureInfo.InvariantCulture) + ", skill=" + SafeSkillCode(skill));
                    return true;
                }
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
            for (int i = 0; i < _buffProfiles.Count; i++)
            {
                var profile = _buffProfiles[i];
                if (profile == null || !profile.Enabled)
                    continue;

                var slot = FindEquippedSkillBySno(profile.Sno);
                if (slot == null || slot.Skill == null)
                    continue;

                if (!IsBuffMissingOrExpiring(slot.Skill, profile, now))
                    continue;

                int delay = GetRandomizedDelay("buff_cast_" + profile.Sno.ToString(CultureInfo.InvariantCulture), profile.CastDelayMinMs, profile.CastDelayMaxMs, 1000);

                string reason;
                if (!CanCastSkill(slot.Skill, slot.ActionKey, now, delay, out reason))
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
                    MarkSkillCast(slot.Skill, now);
                    LogDebug("Buff upkeep cast: " + profile.Code + ", remaining=" + GetRemainingBuffSeconds(slot.Skill, profile.IconIndex).ToString("0.0", CultureInfo.InvariantCulture));
                    return true;
                }
            }

            return false;
        }

        private bool IsBuffMissingOrExpiring(IPlayerSkill skill, AutoBuffProfile profile, int now)
        {
            bool active;
            double remaining;
            if (!TryGetBuffRemainingSeconds(skill, profile.IconIndex, out active, out remaining))
                return true;

            if (!active)
                return true;

            if (double.IsPositiveInfinity(remaining) || remaining == double.MaxValue)
                return false;

            int thresholdMs = GetRandomizedDelay("buff_refresh_" + profile.Sno.ToString(CultureInfo.InvariantCulture), profile.RefreshMinMs, profile.RefreshMaxMs, Math.Max(profile.RefreshMaxMs, BuffRefreshJitterChangeMs));
            return remaining <= thresholdMs / 1000.0d;
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
                remaining = buff.TimeLeftSeconds[iconIndex];
                return true;
            }

            double max = 0;
            for (int i = 0; i < buff.TimeLeftSeconds.Length; i++)
            {
                if (buff.TimeLeftSeconds[i] > max)
                    max = buff.TimeLeftSeconds[i];
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

        private bool TryRunConditionalAutoCastProfiles(int now)
        {
            for (int i = 0; i < _conditionalProfiles.Count; i++)
            {
                var profile = _conditionalProfiles[i];
                if (profile == null || !profile.Enabled || profile.Sno == 0)
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
                    continue;

                int delay = GetRandomizedDelay(
                    "conditional_cast_" + profile.Code,
                    profile.CastDelayMinMs > 0 ? profile.CastDelayMinMs : ConditionalProfileDelayMinMs,
                    profile.CastDelayMaxMs > 0 ? profile.CastDelayMaxMs : ConditionalProfileDelayMaxMs,
                    1000);

                string reason;
                if (!CanCastSkill(slot.Skill, slot.ActionKey, now, delay, out reason))
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

                if (DoAction(slot.ActionKey, profile.UseStandstill && UseForceStandstillForCasts))
                {
                    MarkSkillCast(slot.Skill, now);
                    _lastConditionalProfileCastTickByCode[profile.Code] = now;

                    LogDebug("Conditional profile cast: " + profile.Code + ", skill=" + SafeSkillCode(slot.Skill));
                    return true;
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
            int recheckMs = 100,
            int reservePrimaryResource = 0,
            int reserveSecondaryResource = 0,
            float basePrimaryRequirement = 0,
            float baseSecondaryRequirement = 0,
            bool useStandstill = true,
            bool allowInTown = false)
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
                AllowInTown = allowInTown
            };

            _conditionalProfiles.Add(profile);
            _conditionalProfileSnos.Add(sno);
        }

        // ── Public API for s7o_HUD_MENU integration ───────────────────────────────

        /// <summary>Get whether a conditional cast profile is enabled by code.</summary>
        public bool GetConditionalProfileEnabled(string code)
        {
            var p = FindConditionalProfile(code);
            return p != null && p.Enabled;
        }

        /// <summary>Toggle a conditional cast profile on/off by code. Returns new state, or false if not found.</summary>
        public bool ToggleConditionalProfile(string code)
        {
            var p = FindConditionalProfile(code);
            if (p == null) return false;
            p.Enabled = !p.Enabled;
            if (PersistUserSettings) try { SaveUserSettings(); } catch { }
            return p.Enabled;
        }

        /// <summary>Set a conditional cast profile enabled state by code.</summary>
        public void SetConditionalProfileEnabled(string code, bool enabled)
        {
            var p = FindConditionalProfile(code);
            if (p != null)
            {
                p.Enabled = enabled;
                if (PersistUserSettings) try { SaveUserSettings(); } catch { }
            }
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
                }, 350, 700, 250);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Preparation, 129212, "DH_Preparation_Punishment_Hatred", "Preparation: Punishment Hatred", "Demon Hunter", "Resource", "Casts Punishment rune when hatred is missing by about 75.", true, ctx => ctx.Skill.Rune == 0 && PrimaryResourceMissingAtLeast(75), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Companion, 365311, "DH_Companion_Bat_Hatred", "Companion: Bat Hatred", "Demon Hunter", "Resource", "Casts Bat Companion when hatred is very low.", true, ctx => ctx.Skill.Rune == 3 && PrimaryResourcePctBelow(15), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Companion, 365311, "DH_Companion_EliteOrBoss", "Companion: Elite/Boss", "Demon Hunter", "Combat", "Casts non-Bat Companion active when an elite, boss, or goblin is nearby.", true, ctx => ctx.Skill.Rune != 3 && IsEliteOrBossNearby(40, false), 350, 700, 250);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Vengeance, 302846, "DH_Vengeance_KeepCombatBuff", "Vengeance: Combat Buff", "Demon Hunter", "Buff", "Casts Vengeance in combat when missing or almost expired. Also supports Dark Heart/Seethe style resource/danger use.", true,
                ctx =>
                {
                    if (!IsEliteOrBossNearby(100, true) && CountAliveMonstersWithin(100) < 1) return false;
                    if ((ctx.Skill.Rune == 1 || ctx.Skill.Rune == 3) && !HealthPctBelow(60)) return false;
                    return SkillBuffRemainingBelow(ctx.Skill, 50, 100);
                }, 350, 700, 250);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_Vengeance, 302846, "DH_Vengeance_HatredBurst", "Vengeance: Hatred Burst", "Demon Hunter", "Resource", "For hatred-return rune, casts Vengeance when hatred is below 30.", true, ctx => ctx.Skill.Rune == 4 && PrimaryResourceBelow(30) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_ShadowPower, 130830, "DH_ShadowPower_Defensive", "Shadow Power: Defensive", "Demon Hunter", "Defense", "Casts Shadow Power when missing and health/danger conditions are met.", true,
                ctx =>
                {
                    if (!SkillBuffMissing(ctx.Skill)) return false;
                    if (HealthPctBelow(65)) return true;
                    if (Hud.Game.IsEliteOnScreen && HealthPctBelow(75)) return true;
                    return false;
                }, 350, 700, 250);

            AddConditionalProfile(Hud.Sno.SnoPowers.DemonHunter_ShadowPower, 130830, "DH_ShadowPower_ElusiveRing", "Shadow Power: Elusive Ring", "Demon Hunter", "Defense", "Best-effort Elusive Ring upkeep using Shadow Power when enemies are nearby.", true,
                ctx =>
                {
                    if (!IsEliteOrBossNearby(40, true) && CountAliveMonstersWithin(40) < 1) return false;
                    return SkillBuffRemainingBelow(ctx.Skill, 30, 100);
                }, 350, 700, 250);
        }

        private void BuildBarbarianConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_BattleRage, 79076, "Barbarian_BattleRage_KeepUp", "Battle Rage: Keep Up", "Barbarian", "Buff", "Keeps Battle Rage active.", true, ctx => SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_WarCry, 375483, "Barbarian_WarCry_KeepUp", "War Cry: Keep Up", "Barbarian", "Buff", "Keeps War Cry active.", true, ctx => SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_Sprint, 78551, "Barbarian_Sprint_KeepUpMoving", "Sprint: Keep Up", "Barbarian", "Movement", "Keeps Sprint up while monsters are nearby.", false, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffRemainingBelow(ctx.Skill, 500, 1000), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_WrathOfTheBerserker, 79607, "Barbarian_WOTB_EliteOrDensity", "Wrath of the Berserker: Elite/Density", "Barbarian", "Cooldown", "Casts WOTB when elite/boss nearby or high monster density.", true, ctx => (IsEliteOrBossNearby(80, false) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_CallOfTheAncients, 80049, "Barbarian_COTA_Combat", "Call of the Ancients: Combat", "Barbarian", "Pet", "Casts Call of the Ancients in combat when missing.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(80) >= 1) && SkillBuffMissing(ctx.Skill), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Barbarian_IgnorePain, 79528, "Barbarian_IgnorePain_Defensive", "Ignore Pain: Defensive", "Barbarian", "Defense", "Casts Ignore Pain when health is low or elites are nearby.", true, ctx => (HealthPctBelow(65) || IsEliteOrBossNearby(50, true)) && SkillBuffRemainingBelow(ctx.Skill, 100, 250), 350, 700, 250);
        }

        private void BuildCrusaderConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_AkaratsChampion, 269032, "Crusader_AkaratsChampion_Combat", "Akarat's Champion: Combat", "Crusader", "Cooldown", "Casts Akarat's Champion when elites/bosses or density are nearby.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfValor, 342281, "Crusader_LawsOfValor_Elite", "Laws of Valor: Elite", "Crusader", "Law", "Activates Law of Valor when elites or bosses are nearby.", true, ctx => IsEliteOrBossNearby(60, true), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfJustice, 342280, "Crusader_LawsOfJustice_Defensive", "Laws of Justice: Defensive", "Crusader", "Law", "Activates Law of Justice when health is low or elites are nearby.", true, ctx => HealthPctBelow(70) || IsEliteOrBossNearby(50, true), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_LawsOfHope, 342279, "Crusader_LawsOfHope_Defensive", "Laws of Hope: Defensive", "Crusader", "Law", "Activates Law of Hope when health is low.", true, ctx => HealthPctBelow(70), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_IronSkin, 291804, "Crusader_IronSkin_Defensive", "Iron Skin: Defensive", "Crusader", "Defense", "Casts Iron Skin when health is low or elites are nearby.", true, ctx => (HealthPctBelow(70) || IsEliteOrBossNearby(45, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_Provoke, 290545, "Crusader_Provoke_ResourceOrDensity", "Provoke: Resource/Density", "Crusader", "Resource", "Casts Provoke when wrath is missing and enemies are nearby.", true, ctx => PrimaryResourceMissingAtLeast(40) && CountAliveMonstersWithin(30) >= 3, 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Crusader_Bombardment, 284876, "Crusader_Bombardment_EliteOrDensity", "Bombardment: Elite/Density", "Crusader", "Cooldown", "Casts Bombardment on elite/boss or dense packs.", false, ctx => IsEliteOrBossNearby(60, true) || CountAliveMonstersWithin(50) >= 10, 350, 700, 250);
        }

        private void BuildMonkConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_Epiphany, 312307, "Monk_Epiphany_Combat", "Epiphany: Combat", "Monk", "Cooldown", "Casts Epiphany when elite/boss or density is nearby.", true, ctx => (IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_SweepingWind, 96090, "Monk_SweepingWind_KeepUp", "Sweeping Wind: Keep Up", "Monk", "Buff", "Keeps Sweeping Wind active near enemies.", true, ctx => CountAliveMonstersWithin(60) > 0 && SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfConviction, 375088, "Monk_MantraConviction_Elite", "Mantra of Conviction: Elite", "Monk", "Mantra", "Activates Mantra of Conviction near elites or density.", true, ctx => IsEliteOrBossNearby(50, true) || CountAliveMonstersWithin(35) >= 5, 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfSalvation, 375049, "Monk_MantraSalvation_Defensive", "Mantra of Salvation: Defensive", "Monk", "Mantra", "Activates Mantra of Salvation when health is low or elites are nearby.", true, ctx => HealthPctBelow(70) || IsEliteOrBossNearby(45, true), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MantraOfHealing, 373143, "Monk_MantraHealing_Defensive", "Mantra of Healing: Defensive", "Monk", "Mantra", "Activates Mantra of Healing when health is low.", true, ctx => HealthPctBelow(75), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_MysticAlly, 362102, "Monk_MysticAlly_ResourceOrElite", "Mystic Ally: Resource/Elite", "Monk", "Resource", "Casts Mystic Ally when spirit is low or elite/boss is nearby.", true, ctx => PrimaryResourcePctBelow(35) || IsEliteOrBossNearby(50, true), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Monk_Serenity, 96215, "Monk_Serenity_Defensive", "Serenity: Defensive", "Monk", "Defense", "Casts Serenity when health is low or elites are close.", true, ctx => (HealthPctBelow(55) || IsEliteOrBossNearby(30, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
        }

        private void BuildNecromancerConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_BoneArmor, 466857, "Necro_BoneArmor_NearEnemies", "Bone Armor: Near Enemies", "Necromancer", "Buff", "Casts Bone Armor near enemies when missing/expiring.", true, ctx => CountAliveMonstersWithin(25) >= 1 && SkillBuffRemainingBelow(ctx.Skill, 1000, 2000), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_Simulacrum, 465350, "Necro_Simulacrum_EliteOrDensity", "Simulacrum: Elite/Density", "Necromancer", "Cooldown", "Casts Simulacrum near elites or dense packs.", true, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 10, 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_LandOfTheDead, 465839, "Necro_LandOfTheDead_EliteOrDensity", "Land of the Dead: Elite/Density", "Necromancer", "Cooldown", "Casts Land of the Dead on elites or dense packs.", false, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 12, 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_CommandSkeletons, 453801, "Necro_CommandSkeletons_Elite", "Command Skeletons: Elite", "Necromancer", "Targeting", "Best-effort casts Command Skeletons near elites/bosses.", true, ctx => IsEliteOrBossNearby(60, true), 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Necromancer_SkeletalMage, 462089, "Necro_SkeletalMage_EssenceHigh", "Skeletal Mage: Essence High", "Necromancer", "Resource", "Casts Skeletal Mage when essence is high.", false, ctx => !PrimaryResourcePctBelow(80), 350, 700, 250);
        }

        private void BuildWitchDoctorConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_FetishArmy, 72785, "WD_FetishArmy_Combat", "Fetish Army: Combat", "Witch Doctor", "Pet", "Casts Fetish Army near elites or dense packs.", true, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 8, 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_Gargantuan, 30624, "WD_Gargantuan_KeepUp", "Gargantuan: Keep Up", "Witch Doctor", "Pet", "Casts Gargantuan when missing near enemies.", true, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffMissing(ctx.Skill), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SummonZombieDog, 102573, "WD_ZombieDogs_KeepUp", "Zombie Dogs: Keep Up", "Witch Doctor", "Pet", "Summons Zombie Dogs when missing near enemies.", true, ctx => CountAliveMonstersWithin(80) > 0 && SkillBuffMissing(ctx.Skill), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SoulHarvest, 67616, "WD_SoulHarvest_NearEnemies", "Soul Harvest: Near Enemies", "Witch Doctor", "Buff", "Casts Soul Harvest when enemies are close and the buff needs upkeep.", true, ctx => CountAliveMonstersWithin(18) >= 1 && SkillBuffRemainingBelow(ctx.Skill, 500, 1500), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_Horrify, 67668, "WD_Horrify_Defensive", "Horrify: Defensive", "Witch Doctor", "Defense", "Casts Horrify when enemies are close or health is low.", true, ctx => HealthPctBelow(70) || CountAliveMonstersWithin(24) >= 1, 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_SpiritWalk, 106237, "WD_SpiritWalk_Defensive", "Spirit Walk: Defensive", "Witch Doctor", "Defense", "Casts Spirit Walk defensively. Movement use stays manual.", true, ctx => HealthPctBelow(55) || IsEliteOrBossNearby(30, true), 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.WitchDoctor_BigBadVoodoo, 117402, "WD_BigBadVoodoo_EliteOrDensity", "Big Bad Voodoo: Elite/Density", "Witch Doctor", "Cooldown", "Casts Big Bad Voodoo on elites or dense packs.", false, ctx => IsEliteOrBossNearby(60, true) || CountAliveMonstersWithin(50) >= 10, 350, 700, 250);
        }

        private void BuildWizardConditionalProfiles()
        {
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_DiamondSkin, 75599, "Wizard_DiamondSkin_Defensive", "Diamond Skin: Defensive", "Wizard", "Defense", "Casts Diamond Skin when health is low or elites are close.", true, ctx => (HealthPctBelow(70) || IsEliteOrBossNearby(35, true)) && SkillBuffRemainingBelow(ctx.Skill, 50, 100), 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_Archon, 134872, "Wizard_Archon_EliteOrDensity", "Archon: Elite/Density", "Wizard", "Cooldown", "Casts Archon near elites or dense packs.", false, ctx => IsEliteOrBossNearby(80, true) || CountAliveMonstersWithin(60) >= 10, 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_MirrorImage, 98027, "Wizard_MirrorImage_Defensive", "Mirror Image: Defensive", "Wizard", "Defense", "Casts Mirror Image when health is low or elites are nearby.", true, ctx => HealthPctBelow(65) || IsEliteOrBossNearby(40, true), 350, 700, 250);
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_FrostNova, 30718, "Wizard_FrostNova_CloseEnemies", "Frost Nova: Close Enemies", "Wizard", "Defense", "Casts Frost Nova when enemies are close.", true, ctx => CountAliveMonstersWithin(18) >= 1, 350, 700, 250);
            // Default false until we add a proper menu toggle and more build-specific guards.
            AddConditionalProfile(Hud.Sno.SnoPowers.Wizard_ExplosiveBlast, 87525, "Wizard_ExplosiveBlast_CloseEnemies", "Explosive Blast: Close Enemies", "Wizard", "Offense", "Casts Explosive Blast when enemies are close.", false, ctx => CountAliveMonstersWithin(15) >= 1, 250, 500, 100);
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

                if (iconIndex >= 0 && iconIndex < buff.TimeLeftSeconds.Length) return buff.TimeLeftSeconds[iconIndex];

                double max = 0;
                for (int i = 0; i < buff.TimeLeftSeconds.Length; i++)
                    if (buff.TimeLeftSeconds[i] > max) max = buff.TimeLeftSeconds[i];
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

        private bool SkillBuffRemainingBelow(IPlayerSkill skill, int minMs, int maxMs, int iconIndex = -1)
        {
            if (skill == null || skill.SnoPower == null) return true;
            double remaining = GetOwnBuffRemainingSeconds(skill.SnoPower.Sno, iconIndex);
            if (remaining <= 0) return true;
            int thresholdMs = GetRandomizedDelay("conditional_buff_refresh_" + skill.SnoPower.Sno.ToString(CultureInfo.InvariantCulture), minMs, maxMs, Math.Max(maxMs, BuffRefreshJitterChangeMs));
            return remaining <= thresholdMs / 1000.0d;
        }

        #endregion

        #region Cast Rules

        private bool CanCastSkill(IPlayerSkill skill, ActionKey actionKey, int now, int minDelayMs, out string reason)
        {
            reason = null;

            if (skill == null || skill.SnoPower == null)
            {
                reason = "skill null";
                return false;
            }

            if (skill.IsOnCooldown)
            {
                reason = "cooldown";
                return false;
            }

            if (!HasEnoughPrimaryResource(skill, 0, 0))
            {
                reason = "resource";
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

            if ((uint)(now - _lastGlobalCastTick) < (uint)Math.Max(0, GlobalCastDelayMs))
            {
                reason = "global delay";
                return false;
            }

            if (IsChannelingSkill(skill))
            {
                reason = "channeling unsupported";
                return false;
            }

            return true;
        }

        private bool HasEnoughPrimaryResource(IPlayerSkill skill, int spareResource, float baseRequirement)
        {
            try
            {
                if (skill == null || skill.Player == null || skill.Player.Stats == null)
                    return false;

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

            if (actionKey == ActionKey.LeftSkill)
            {
                if (!IsCursorInsideGroundRect())
                    return false;

                if (BlockLeftSkillOnSelectedClickableActor && IsSelectedClickableWorldObject())
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
                int left = Hud.Window.Offset.X;
                int top = Hud.Window.Offset.Y;
                int right = left + Hud.Window.Size.Width;
                int bottom = top + Hud.Window.Size.Height;

                return x >= left && x < right && y >= top && y < bottom;
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

        private void MarkSkillCast(IPlayerSkill skill, int now)
        {
            if (skill != null && skill.SnoPower != null)
                _lastCastTickByPower[skill.SnoPower.Sno] = now;

            _lastGlobalCastTick = now;
        }

        #endregion

        #region Input Layer

        private bool DoAction(ActionKey actionKey, bool standStill)
        {
            // Force Standstill (Shift) is only meaningful for LeftSkill: without it, a simulated LMB
            // click moves the character to the cursor instead of casting in place.  Every other slot
            // (RMB, keyboard 1-4, Heal) fires directly from a key press and has no movement behavior
            // to suppress, so injecting Shift around them serves no purpose and risks interfering with
            // whatever modifier keys the user is physically holding.
            bool shouldStandStill = standStill && actionKey == ActionKey.LeftSkill && ForceStandstillVirtualKey != 0;

            // If the user is already physically holding the standstill key (e.g. Shift), do NOT inject
            // a synthetic key-down/key-up pair around our cast.  Sending a synthetic KeyUp would stomp
            // the OS-level "key is held" state even though the user's finger never moved, which is
            // exactly what caused Black Hole to stop working after EB autocasted while Shift was held.
            bool userAlreadyHolding = shouldStandStill && IsKeyPhysicallyDown(ForceStandstillVirtualKey);

            try
            {
                if (shouldStandStill && !userAlreadyHolding)
                    SendKeyDown(ForceStandstillVirtualKey);

                bool result = false;

                switch (actionKey)
                {
                    case ActionKey.LeftSkill:
                        SendMouse(LeftDown);
                        SendMouse(LeftUp);
                        result = true;
                        break;

                    case ActionKey.RightSkill:
                        SendMouse(RightDown);
                        SendMouse(RightUp);
                        result = true;
                        break;

                    case ActionKey.Skill1:
                        PressKey(Skill1VirtualKey);
                        result = true;
                        break;

                    case ActionKey.Skill2:
                        PressKey(Skill2VirtualKey);
                        result = true;
                        break;

                    case ActionKey.Skill3:
                        PressKey(Skill3VirtualKey);
                        result = true;
                        break;

                    case ActionKey.Skill4:
                        PressKey(Skill4VirtualKey);
                        result = true;
                        break;

                    case ActionKey.Heal:
                        PressKey(HealVirtualKey);
                        result = true;
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

        private static void PressKey(ushort virtualKey)
        {
            if (virtualKey == 0)
                return;

            SendKeyDown(virtualKey);
            SendKeyUp(virtualKey);
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

        #region Overlay UI

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.BeforeClip)
                return;

            if (!Enabled || Hud == null || Hud.Window == null)
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

            if (Hud == null || Hud.Window == null || !Hud.Window.IsForeground)
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
            string debugFile = "s7o_AutoSkill.debug.log";

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

                    // Preserve existing debug log behavior.
                    _debugLogPath = Path.Combine(pluginDir, debugFile);
                    return;
                }
            }
            catch
            {
            }

            _settingsPath = Path.Combine(relativeSettingsDir, settingsFile);
            _legacySettingsPath = Path.Combine(relativePluginDir, legacySettingsFile);

            // Preserve existing debug log behavior.
            _debugLogPath = Path.Combine(relativePluginDir, debugFile);

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

                    TryApplyBoolSetting(key, value);
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
                sb.AppendLine("DebugLogging=" + DebugLogging.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _enabledSlots.Length; i++)
                    sb.AppendLine("Slot" + i.ToString(CultureInfo.InvariantCulture) + "=" + _enabledSlots[i].ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < _buffProfiles.Count; i++)
                {
                    var profile = _buffProfiles[i];
                    if (profile == null || string.IsNullOrEmpty(profile.Code))
                        continue;

                    sb.AppendLine("BuffProfile_" + profile.Code + "=" + profile.Enabled.ToString(CultureInfo.InvariantCulture));
                }

                for (int i = 0; i < _conditionalProfiles.Count; i++)
                {
                    var profile = _conditionalProfiles[i];
                    if (profile == null || string.IsNullOrEmpty(profile.Code))
                        continue;

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
                var profile = FindConditionalProfile(code);

                if (profile != null)
                {
                    profile.Enabled = parsed;
                    return;
                }
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

        private void LogDebug(string message)
        {
            if (!DebugLogging)
                return;

            try
            {
                if (string.IsNullOrEmpty(_debugLogPath))
                    return;

                RotateDebugLogIfNeeded();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | " + message + Environment.NewLine;
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
            if (!DebugLogging)
                return;

            if (string.IsNullOrEmpty(reason))
                reason = "unknown";

            bool changed = !StringEquals(reason, _lastBlockedReason);
            if (!changed && !IsTickReached(now, _nextBlockedLogTick))
                return;

            _lastBlockedReason = reason;
            _nextBlockedLogTick = now + 3000;
            LogDebug("Context blocked: " + reason);
        }

        private void LogDebugCastSkipThrottled(string category, IPlayerSkill skill, string reason, int now)
        {
            if (!DebugLogging)
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
            return (int)(now - targetTick) >= 0;
        }

        private bool IsTickInFuture(int targetTick, int now)
        {
            return targetTick != 0 && (int)(targetTick - now) > 0;
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

        private static bool IsKeyPhysicallyDown(ushort virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        #endregion
    }
}
