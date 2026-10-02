using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    // v1.5.0: keep synthetic LMB and assist aim away from native/HUD controls.
    // Shift retains SelfDash. Unshifted LMB uses the native attacked melee target when known.
    // Space assist owns its attacks; suspend them for secondary casts, then rearm at the target.
    // The held generator stays manual; only the other buffs and Dashing Strike are automated.
    // WotHF retains the S27 stage-2 Foresight primer path. Other generators verify their
    // live Combination Strike timer before maintenance, without that seasonal assumption.
    // Manual input ownership is never released or wrapped in synthetic Stand Still.
    public class s7o_GenMonk : BasePlugin, IAfterCollectHandler, INewAreaHandler, IInGameTopPainter
    {
        public bool AutoDash = true;
        public bool SelfDash = true;
        public bool ShowCombinationCount = true;
        public bool RequireRaimentSet = true;
        // Hold this unbound key for optional targeting/attacks; releasing it ends the assist.
        public bool MeleeAssistEnabled = true;
        public ushort MeleeAssistVirtualKey = 0x20; // Space, unbound in Diablo.
        public double MeleeAssistRange = 12.0;
        public int AssistApproachRetryMs = 800;
        // Primary generators can be ignored while WotHF is inside a fast attack animation.
        // Hold the injected generator through roughly one high-APS attack window, but release
        // immediately once the intended buff refresh proves the cast registered.
        public int GeneratorPulseMs = 250;
        public int GeneratorPulseMinMs = 35;
        public int DashPulseMs = 55;
        public int GeneratorRetryGapMs = 250;
        // S27 WotHF sanctification forces every manual WotHF attack to combo stage 2.
        // Deadly Reach/Foresight is therefore pulsed only from a live WotHF attack window
        // so its one automated hit lands as stage 3 instead of walking 1->2->3 itself.
        public bool RequireHundredFistsMain = false; // Optional strict seasonal-only mode.
        // Combination Strike lasts 10 seconds. Refresh only near expiry, never continuously.
        public double CombinationRefreshAtSeconds = 2.0;
        public double ForesightRefreshAtSeconds = 2.0;
        // Automation is allowed only during a real generator attack with a nearby target.
        public double AttackTargetRange = 12.0;
        public int ForesightRetryGapMs = 350;
        private const string Owner = "GenMonk";
        private ActionKey _mainKey = ActionKey.Unknown;
        private ActionKey _candidateKey = ActionKey.Unknown;
        private int _candidateSinceTick;
        private ActionKey _pulse = ActionKey.Unknown;
        private ActionKey _targetKey = ActionKey.Unknown;
        private uint _targetSno;
        private int _targetIcon;
        private double _targetBefore;
        private int _attempts;
        private int _pulseStartedTick;
        private int _pulseReleaseTick;
        private bool _pulseIsMaintenance;
        private int _nextAttemptTick;
        private bool _targetForesight;
        private bool _hasCombinationStrike;
        private double _targetForesightBefore;
        private bool _wasHundredFistsAttackWindow;
        private bool _freshHundredFistsPrime;
        private readonly Dictionary<uint, int> _retryAfter = new Dictionary<uint, int>();
        private readonly List<IPlayerSkill> _generators = new List<IPlayerSkill>(4);
        private int _lastDashTick = int.MinValue;
        private bool _dashAwaiting;
        private bool _restoreCursor;
        private ActionKey _selfDashKey = ActionKey.Unknown;
        private int _selfDashAimTick, _selfDashGameTick;
        private int _selfDashCastTick = int.MinValue;
        private int _selfDashPreviousDashTick = int.MinValue;
        private string _selfDashAnimationBeforePress;
        private int _selfDashAnimationStartBeforePress, _selfDashPressGameTick;
        private double _selfDashRaimentBefore;
        private double[] _selfDashBuffBefore;
        private int _selfDashSerial;
        private string _selfDashResult = "none";
        private int _cursorX, _cursorY, _dashTargetX, _dashTargetY;
        private string _settingsPath;
        private IFont _combinationFont;
        private IUiElement _combinationIcon;
        private IUiElement _chatEditLine;
        private readonly List<IUiElement> _clickUiElements = new List<IUiElement>();
        private readonly List<RectangleF> _clickUiRects = new List<RectangleF>();
        private s7o_HUD_MENU _hudMenu;
        private bool _clickUiReadable;
        private static readonly ActionKey[] ClickUiActions = {
            ActionKey.LeftSkill, ActionKey.RightSkill, ActionKey.Skill1, ActionKey.Skill2,
            ActionKey.Skill3, ActionKey.Skill4, ActionKey.Heal, ActionKey.TownPortal,
            ActionKey.Inventory, ActionKey.SkillsWindow, ActionKey.ParagonWindow,
            ActionKey.Map, ActionKey.WaypointMap, ActionKey.Social, ActionKey.Close
        };
        private ActionKey _combinationDisplayKey = ActionKey.Unknown;
        private int _combinationDrawCount;
        private int _nextDashAimTick;
        private bool _assistActive, _assistEnding;
        private ActionKey _assistAttackKey = ActionKey.Unknown;
        private ActionKey _assistMainKey = ActionKey.Unknown;
        private uint _assistTargetAcd;
        private uint _assistTargetAnn;
        private int _assistAimTick, _assistAimGameTick;
        private int _assistCursorX, _assistCursorY, _assistAimX, _assistAimY;
        private bool _assistCursorOwned;
        private bool _assistAimPending, _assistCaptureSuppressed, _assistWaitForRelease;
        private bool _assistHitVerified;
        private bool _assistUiBlockedTargets;
        private int _assistAttackStartedTick;
        private int _assistIdleSinceTick = int.MinValue;
        private int _assistRecoveries;
        private string _assistStatus = "off";
        private bool _selfDashAimIsMonster, _selfDashObserved;
        private uint _selfDashTargetAcd;
        private bool _selfDashApproach, _assistWasApproaching, _assistApproachPending;
        private bool _assistDashRetryNeedsAttack;
        private int _lastApproachDashTick = int.MinValue;
        private uint _lastApproachDashTargetAcd;
        private static readonly uint[] AttackIdentityModifiers = { 0u, 0xFFFFFu, uint.MaxValue, 2147483647u };

        public s7o_GenMonk() { Enabled = true; Order = 21011; }

        public override void Load(IController hud)
        {
            base.Load(hud);
            s7o_GenMonkInputContext.Hud = hud;
            s7o_GenMonkInputContext.CanPressLeftSkill = IsLeftClickSafe;
            RegisterClickUi();
            _chatEditLine = Hud.Render.RegisterUiElement(
                "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline", null, null);
            _combinationFont = Hud.Render.CreateFont("tahoma", 9, 255, 255, 255, 255,
                false, false, 255, 0, 0, 0, true);
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "plugins", "s7o", "settings", "s7o_GenMonk.ini");
            try
            {
                string loadPath = _settingsPath;
                if (!File.Exists(loadPath))
                {
                    string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "plugins", "s7o", "settings", "s7o_SanctifiedMonk.ini");
                    if (File.Exists(legacyPath)) loadPath = legacyPath;
                }
                if (File.Exists(loadPath))
                    foreach (string line in File.ReadAllLines(loadPath))
                    {
                        string[] pair = line.Split(new[] { '=' }, 2);
                        ushort assistKey;
                        if (pair.Length == 2 && pair[0] == "MeleeAssistVirtualKey"
                            && ushort.TryParse(pair[1], out assistKey) && assistKey != 0)
                        { MeleeAssistVirtualKey = assistKey; continue; }
                        bool value;
                        if (pair.Length != 2 || !bool.TryParse(pair[1], out value)) continue;
                        if (pair[0] == "AutoDash") AutoDash = value;
                        if (pair[0] == "SelfDash") SelfDash = value;
                        if (pair[0] == "MeleeAssistEnabled") MeleeAssistEnabled = value;
                    }
            }
            catch { }
        }

        public void SetAutoDash(bool value) { AutoDash = value; SaveOptions(); }
        public void SetSelfDash(bool value) { SelfDash = value; SaveOptions(); }
        public void SetMeleeAssistEnabled(bool value)
        {
            MeleeAssistEnabled = value;
            if (!value) ReleaseAssistAttack();
            SaveOptions();
        }
        public void SetMeleeAssistVirtualKey(ushort value)
        {
            if (value == 0) return;
            MeleeAssistVirtualKey = value;
            _assistWaitForRelease = true;
            ReleaseAssistAttack();
            SaveOptions();
        }
        public void SetMeleeAssistCapture(bool value)
        {
            _assistCaptureSuppressed = value;
            _assistWaitForRelease = true;
            if (value) ReleaseAssistAttack();
        }

        private void SaveOptions()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, "AutoDash=" + AutoDash + Environment.NewLine
                    + "SelfDash=" + SelfDash + Environment.NewLine
                    + "MeleeAssistEnabled=" + MeleeAssistEnabled + Environment.NewLine
                    + "MeleeAssistVirtualKey=" + MeleeAssistVirtualKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + Environment.NewLine);
            }
            catch { }
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            Stop();
            if (newGame) _combinationDisplayKey = ActionKey.Unknown;
        }
        public void ForceStopForDisable() { Stop(); }

        public void AfterCollect()
        {
            int now = Environment.TickCount;
            if (Hud.Window.IsForeground) s7o_InputReleaseArbiter.RetryPending(now);
            if (!Enabled || !CanRun()) { Stop(); return; }
            RefreshClickUiRects();
            // Stop only our LMB when a held assist/pulse reaches UI. Never release manual input.
            if (s7o_GenMonkInput.Owns(ActionKey.LeftSkill) && !IsLeftClickSafe())
            {
                _restoreCursor = false; // Keep the user's current UI position.
                ReleaseAssistAttack();
                if (_pulse == ActionKey.LeftSkill) CancelTarget();
                _assistStatus = "ui-blocked";
                return;
            }
            FinishPulse(now);
            if (_assistActive && !AssistRequested())
            {
                ReleaseAssistAttack();
                _assistEnding = true;
                // A submitted Dash must finish its aim transaction before the final restore.
                if (_selfDashKey == ActionKey.Unknown || _selfDashCastTick == int.MinValue)
                {
                    CancelTarget();
                    EndAssist();
                    return;
                }
            }
            if (_selfDashKey != ActionKey.Unknown)
            {
                bool assistDash = _selfDashApproach && _assistActive;
                AdvanceSelfDash(now);
                if (_assistEnding && _selfDashKey == ActionKey.Unknown)
                { EndAssist(); return; }
                if (_selfDashKey != ActionKey.Unknown || !assistDash) return;
                // The assist transaction finished: acquire/attack in this same update.
                // Manual Dash retains its separate confirmation/restore behavior.
            }

            _hasCombinationStrike = HasCombinationStrikePassive();
            if (!_hasCombinationStrike && _targetKey != ActionKey.Unknown && !_targetForesight)
                CancelTarget();
            _generators.Clear();
            IPlayerSkill dash = null;
            try
            {
                foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
                {
                    if (skill == null || skill.SnoPower == null) continue;
                    if (GeneratorIcon(skill) != 0) _generators.Add(skill);
                    if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_DashingStrike.Sno) dash = skill;
                }
            }
            catch { Stop(); return; }

            try { if (UpdateMeleeAssist(dash, now)) return; }
            catch { Stop(); return; }

            IPlayerSkill held = FindHeldGenerator(_generators);
            if (held == null) { Stop(); return; }
            if (RequireHundredFistsMain
                && held.SnoPower.Sno != Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
            {
                CancelTarget();
                _mainKey = ActionKey.Unknown;
                _candidateKey = ActionKey.Unknown;
                return;
            }
            if (_candidateKey != held.Key)
            {
                _candidateKey = held.Key;
                _candidateSinceTick = now;
                CancelTarget();
                return;
            }
            if ((uint)(now - _candidateSinceTick) < 75u) return;
            if (_mainKey != held.Key && _targetKey != ActionKey.Unknown) CancelTarget();
            _mainKey = held.Key;
            _combinationDisplayKey = held.Key;

            // LMB can mean either move or attack. Never infer combat from the physical button
            // alone. Keep the WotHF guard unchanged; other generators must also have just
            // refreshed their own Combination Strike timer while actually attacking.
            bool hundredFistsMain = held.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno;
            bool hundredFistsAttack = hundredFistsMain && HundredFistsAttackWindow();
            bool generatorAttack = hundredFistsMain ? hundredFistsAttack : OtherGeneratorAttackWindow(held);
            // Generator pulses never steer the cursor or attempt to reacquire a monster.
            if (_pulse != ActionKey.Unknown) return;

            // Under the S27/S40 sanctification every real WotHF attack is the stage-2 prime.
            UpdateHundredFistsPrime(hundredFistsAttack);
            if (!generatorAttack || !HasAttackableTargetNearby())
            {
                CancelTarget();
                _dashAwaiting = false;
                return;
            }

            // Dash is the damage prerequisite. Service it before any generator setup/retry.
            if (_dashAwaiting && (uint)(now - _lastDashTick) >= 220u)
            {
                double raiment = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
                if (raiment > 1.0 || (uint)(now - _lastDashTick) >= 700u)
                    _dashAwaiting = false;
            }
            if (!_assistActive && AutoDash && dash != null && !dash.IsOnCooldown && !_dashAwaiting && DashDue(dash, now))
            {
                CancelTarget();
                MaintainDash(dash, now);
                if (_selfDashKey != ActionKey.Unknown || _pulse != ActionKey.Unknown) return;
            }

            if (_targetKey != ActionKey.Unknown)
            {
                if (_targetKey == held.Key) { CancelTarget(); return; }

                double comboLeft = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, _targetIcon);
                if (_targetForesight)
                {
                    double foresightLeft = ForesightLeft();
                    // Confirm the effect that actually needed refreshing. A normal Deadly Reach
                    // hit can refresh Combination Strike without being combo stage 3, so when
                    // Foresight itself was due we require its own timer to jump before declaring
                    // success. This prevents a mistimed hit from being mistaken for the prime.
                    bool foresightWasDue = _targetForesightBefore <= Math.Max(0.25, ForesightRefreshAtSeconds);
                    bool comboWasDue = _hasCombinationStrike && _targetBefore <= CombinationRefreshAtSeconds;
                    bool foresightConfirmed = !foresightWasDue
                        || (foresightLeft >= 4.0 && foresightLeft > _targetForesightBefore + 1.0);
                    bool comboConfirmed = !comboWasDue
                        || (comboLeft >= 4.0 && comboLeft > _targetBefore + 1.0);
                    if (foresightConfirmed && comboConfirmed)
                    {
                        CancelTarget();
                        return;
                    }

                    if (_pulse != ActionKey.Unknown || !Due(now, _nextAttemptTick)) return;
                    if (_attempts >= 3)
                    {
                        _retryAfter[_targetSno] = unchecked(now + 1200);
                        CancelTarget();
                        return;
                    }

                    // Foresight is a stage-3 effect. One freshly observed manual WotHF
                    // attack is one stage-2 prime and may be consumed exactly once. If the
                    // attempt misses, wait for the next real WotHF attack before retrying.
                    if (hundredFistsMain && !_freshHundredFistsPrime)
                    {
                        CancelTarget();
                        return;
                    }
                    // Preserve Diablo's native held-LMB target lock. During a confirmed manual
                    // WotHF attack, pulse only the secondary generator key; never synthesize
                    // Stand Still/Shift around it because a Shift transition can make the still-held
                    // LMB revert from its locked monster attack to cursor movement.
                    if (StartMaintenancePulse(_targetKey, now))
                    {
                        ConsumeHundredFistsPrime();
                        _attempts++;
                    }
                    else _nextAttemptTick = unchecked(now + 90);
                    return;
                }

                if (comboLeft >= 5.0 && comboLeft > _targetBefore + 1.0)
                {
                    CancelTarget();
                    return;
                }
                if (_pulse != ActionKey.Unknown || !Due(now, _nextAttemptTick)) return;
                if (_attempts >= 4)
                {
                    _retryAfter[_targetSno] = unchecked(now + 1200);
                    CancelTarget();
                    return;
                }
                // Keep the user's physical attack button semantically untouched. The native
                // WotHF animation already proves we are standing and attacking a real target.
                if (StartMaintenancePulse(_targetKey, now))
                {
                    // Any other generator consumes the current combo position too.
                    // The next stage-3 effect must wait for a fresh WotHF prime.
                    ConsumeHundredFistsPrime();
                    _attempts++;
                }
                else _nextAttemptTick = unchecked(now + 90);
                return;
            }

            // Stage-sensitive work owns priority. A direct generator refresh below can
            // consume combo stage 3, so Foresight is always serviced first when due.
            IPlayerSkill foresight = FindForesight();
            if (foresight != null && foresight.Key != held.Key)
            {
                int retry;
                uint sno = foresight.SnoPower.Sno;
                bool retryReady = !_retryAfter.TryGetValue(sno, out retry) || Due(now, retry);
                double foresightLeft = ForesightLeft();
                int icon = GeneratorIcon(foresight);
                double comboLeft = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, icon);
                if (retryReady && (foresightLeft <= Math.Max(0.25, ForesightRefreshAtSeconds)
                    || (_hasCombinationStrike && comboLeft <= CombinationRefreshAtSeconds)))
                {
                    // Do not let a stage-insensitive refresh steal the combo slot while
                    // Foresight is waiting for a fresh stage-2 WotHF prime.
                    if (hundredFistsMain && !_freshHundredFistsPrime) return;
                    SelectTarget(foresight, now, true, foresightLeft);
                    return;
                }
            }

            if (!_hasCombinationStrike) return;
            foreach (var generator in _generators)
            {
                if (generator.Key == held.Key || IsForesight(generator)) continue;
                int retry;
                uint sno = generator.SnoPower.Sno;
                if (_retryAfter.TryGetValue(sno, out retry) && !Due(now, retry)) continue;
                int icon = GeneratorIcon(generator);
                double left = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, icon);
                if (left > CombinationRefreshAtSeconds) continue;
                SelectTarget(generator, now, false, 0.0);
                return;
            }

        }

        private void SelectTarget(IPlayerSkill generator, int now, bool foresight, double foresightBefore)
        {
            if (generator == null || generator.Key == ActionKey.Unknown) return;
            _targetKey = generator.Key;
            _targetSno = generator.SnoPower.Sno;
            _targetIcon = GeneratorIcon(generator);
            _targetBefore = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, _targetIcon);
            _targetForesight = foresight;
            _targetForesightBefore = foresightBefore;
            _attempts = 0;
            _nextAttemptTick = now;
        }

        private IPlayerSkill FindForesight()
        {
            foreach (var generator in _generators)
                if (IsForesight(generator)) return generator;
            return null;
        }

        private bool HasCombinationStrikePassive()
        {
            try
            {
                var powers = Hud.Game.Me.Powers;
                if (powers == null || powers.UsedPassives == null) return false;
                uint sno = Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno;
                foreach (var passive in powers.UsedPassives)
                    if (passive != null && passive.Sno == sno) return true;
            }
            catch { }
            return false;
        }

        private bool IsForesight(IPlayerSkill skill)
        {
            // LightningMOD v7.6's MonkDeadlyReachPlugin verifies rune 0 as Foresight.
            return skill != null && skill.SnoPower != null
                && skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno
                && skill.Rune == 0;
        }

        private double ForesightLeft()
        {
            try
            {
                // LightningMOD's native handler reads Deadly Reach buff icon 1 for Foresight.
                var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno);
                return buff != null && buff.TimeLeftSeconds != null && buff.TimeLeftSeconds.Length > 1
                    ? Math.Max(0, buff.TimeLeftSeconds[1]) : 0;
            }
            catch { return 0; }
        }

        private bool HundredFistsAttackWindow()
        {
            try
            {
                if (_mainKey == ActionKey.Unknown || !s7o_GenMonkInput.IsDown(_mainKey)) return false;
                string animation = Hud.Game.Me.Animation.ToString();
                // LightningMOD v7.6 uses this same native animation family as its guard.
                // Do not fall back to generic Attacking or LMB-down: both also occur while moving.
                return !string.IsNullOrEmpty(animation)
                    && animation.IndexOf("rapidstrikes", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void UpdateHundredFistsPrime(bool attackActive)
        {
            if (attackActive && !_wasHundredFistsAttackWindow)
                _freshHundredFistsPrime = true;
            else if (!attackActive)
                _freshHundredFistsPrime = false;
            _wasHundredFistsAttackWindow = attackActive;
        }

        private bool OtherGeneratorAttackWindow(IPlayerSkill held)
        {
            // A held mouse can also be movement, and a different skill can briefly occupy
            // the attack animation. Require this held generator's own fresh 10-second buff
            // as evidence of recent attacks; never treat idle/running/dashing as a primer.
            if (held == null || !s7o_GenMonkInput.IsDown(held.Key)
                || Hud.Game.Me.AnimationState != AcdAnimationState.Attacking) return false;
            string animation = Hud.Game.Me.Animation.ToString();
            if (animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno,
                GeneratorIcon(held)) >= 9.0;
        }

        private void ConsumeHundredFistsPrime()
        {
            _freshHundredFistsPrime = false;
            // Our injected generator interrupts WotHF. Force the next observed rapidstrikes
            // animation to be treated as a new sanctified stage-2 prime even if one HUD sample was missed.
            _wasHundredFistsAttackWindow = false;
        }

        private bool HasAttackableTargetNearby()
        {
            try
            {
                double range = Math.Max(8.0, AttackTargetRange);
                foreach (var monster in Hud.Game.AliveMonsters)
                {
                    if (monster == null || !monster.Attackable) continue;
                    if (monster.NormalizedXyDistanceToMe <= range) return true;
                }
            }
            catch { }
            return false;
        }

        private IPlayerSkill FindHeldGenerator(List<IPlayerSkill> generators)
        {
            // Keep the sustained user input, including Shift+LMB, ahead of our pulses.
            foreach (var skill in generators)
                if (skill.Key == _mainKey && skill.Key != _pulse && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            foreach (var skill in generators)
                if (skill.Key == ActionKey.LeftSkill && skill.Key != _pulse
                    && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            foreach (var skill in generators)
                if (skill.Key != _pulse && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key)) return skill;
            return null;
        }

        private bool CanRun()
        {
            try
            {
                return Hud.Game.IsInGame && !Hud.Game.IsLoading && !Hud.Game.IsPaused
                    && !Hud.Game.IsInTown && Hud.Window.IsForeground
                    && !InputUiBlocked()
                    && Hud.Game.Me != null && !Hud.Game.Me.IsDead
                    && (Hud.Inventory == null || Hud.Inventory.InventoryMainUiElement == null
                        || !Hud.Inventory.InventoryMainUiElement.Visible)
                    && Hud.Game.Me.HeroClassDefinition != null
                    && Hud.Game.Me.HeroClassDefinition.HeroClass == HeroClass.Monk
                    && (!RequireRaimentSet || Hud.Game.Me.GetSetItemCount(755275u) >= 6);
            }
            catch { return false; }
        }

        private bool InputUiBlocked()
        {
            return IsInputUiVisible(_chatEditLine) || IsInputUiVisible(Hud.Render.WorldMapUiElement)
                || IsInputUiVisible(Hud.Render.ActMapUiElement);
        }

        private static bool IsInputUiVisible(IUiElement element)
        {
            if (element == null) return false;
            try { element.Refresh(); return element.Visible; }
            catch { return true; }
        }

        private void RegisterClickUi()
        {
            // Reuse Pestilence's native controls, registering once rather than scanning per target.
            foreach (var key in ClickUiActions)
                try { AddClickUi(Hud.Render.GetPlayerSkillUiElement(key)); } catch { }
            AddClickUi(Hud.Render.ParagonLevelUpSplashTextUiElement);
            if (Hud.Inventory != null) AddClickUi(Hud.Inventory.FollowerMainUiElement);
            foreach (string path in new[] {
                "Root.NormalLayer.Paragon_main.LayoutRoot.ParagonPointSelect",
                "Root.NormalLayer.game_notify_dialog_backgroundScreen.dialog_new_paragon_button",
                "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList",
                "Root.TopLayer.follower_swap",
                "Root.NormalLayer.BattleNetProfile_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetLeaderboard_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetAchievements_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.BattleNetStore_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.Guild_main.LayoutRoot.OverlayContainer",
                "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd",
                "Root.NormalLayer.conversation_dialog_main",
                "Root.TopLayer.BattleNetSocialDialogs_main.LayoutRoot.DialogWriteNote"
            })
                try { AddClickUi(Hud.Render.RegisterUiElement(path, null, null)); } catch { }
        }

        private void AddClickUi(IUiElement element)
        {
            if (element != null && !_clickUiElements.Contains(element)) _clickUiElements.Add(element);
        }

        private void RefreshClickUiRects()
        {
            _clickUiReadable = false;
            _clickUiRects.Clear();
            try
            {
                if (_hudMenu == null)
                    foreach (var plugin in Hud.AllPlugins)
                        if (plugin is s7o_HUD_MENU) { _hudMenu = (s7o_HUD_MENU)plugin; break; }
                if (!Hud.Render.UiHidden)
                {
                    // Native UI objects are refreshed by collection; copy their current bounds once.
                    foreach (var element in _clickUiElements)
                        if (element.Visible && element.Rectangle.Width > 0 && element.Rectangle.Height > 0)
                            _clickUiRects.Add(element.Rectangle);
                    foreach (var player in Hud.Game.Players)
                        if (player != null && player.IsInGame && player.PortraitUiElement != null
                            && player.PortraitUiElement.Visible)
                            _clickUiRects.Add(player.PortraitUiElement.Rectangle);
                    // Small fallbacks for buttons whose native ActionKey element may be absent.
                    // Anchor to center/edges with height-based scale; do not stretch across ultrawide.
                    float w = Hud.Window.Size.Width, h = Hud.Window.Size.Height, s = h / 1080f;
                    _clickUiRects.Add(new RectangleF(w / 2f - 350f * s, h - 205f * s, 715f * s, 205f * s));
                    _clickUiRects.Add(new RectangleF(0, h - 107f * s, 93f * s, 107f * s));
                    _clickUiRects.Add(new RectangleF(w - 166f * s, h - 130f * s, 166f * s, 130f * s));
                    _clickUiRects.Add(new RectangleF(w - 80f * s, 338f * s, 72f * s, 72f * s));
                    _clickUiRects.Add(new RectangleF(w - 292f * s, 10f * s, 84f * s, 54f * s));
                }
                _clickUiReadable = true;
            }
            catch { } // A failed read must not permit a synthetic left click.
        }

        private bool IsLeftClickPointSafe(double x, double y)
        {
            if (!_clickUiReadable || x < 0 || y < 0
                || x >= Hud.Window.Size.Width || y >= Hud.Window.Size.Height
                || double.IsNaN(x) || double.IsNaN(y)) return false;
            if (_hudMenu != null && _hudMenu.IsAutomationLeftClickBlocked((float)x, (float)y)) return false;
            foreach (var rect in _clickUiRects)
                if (x >= rect.Left - 2 && x <= rect.Right + 2
                    && y >= rect.Top - 2 && y <= rect.Bottom + 2) return false;
            return true;
        }

        private bool IsLeftClickSafe()
        {
            try
            {
                CursorPoint cursor;
                if (!Hud.Window.IsForeground || !GetCursorPos(out cursor)) return false;
                return IsLeftClickPointSafe((long)cursor.X - Hud.Window.Offset.X,
                    (long)cursor.Y - Hud.Window.Offset.Y);
            }
            catch { return false; }
        }

        private bool TryGetAssistAim(IMonster target, out int x, out int y)
        {
            x = y = 0;
            var screen = target.FloorCoordinate.ToScreenCoordinate(true, true);
            if (screen == null || double.IsNaN(screen.X) || double.IsNaN(screen.Y)
                || double.IsInfinity(screen.X) || double.IsInfinity(screen.Y)) return false;
            double cx = Math.Round(screen.X), cy = Math.Round(screen.Y);
            // IsOnScreen can still project outside the client; SetCursorPos would clamp that point.
            if (cx < 0 || cy < 0 || cx >= Hud.Window.Size.Width || cy >= Hud.Window.Size.Height
                || (_assistMainKey == ActionKey.LeftSkill && !IsLeftClickPointSafe(cx, cy))) return false;
            long sx = (long)cx + Hud.Window.Offset.X, sy = (long)cy + Hud.Window.Offset.Y;
            if (sx < int.MinValue || sx > int.MaxValue || sy < int.MinValue || sy > int.MaxValue) return false;
            x = (int)sx; y = (int)sy;
            return true;
        }

        private int GeneratorIcon(IPlayerSkill skill)
        {
            uint sno = skill.SnoPower.Sno;
            if (sno == Hud.Sno.SnoPowers.Monk_DeadlyReach.Sno) return 2;
            if (sno == Hud.Sno.SnoPowers.Monk_CripplingWave.Sno) return 3;
            if (sno == Hud.Sno.SnoPowers.Monk_FistsOfThunder.Sno) return 4;
            if (sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno) return 5;
            return 0;
        }

        private double BuffLeft(uint sno, int icon)
        {
            try
            {
                var buff = Hud.Game.Me.Powers.GetBuff(sno);
                return buff != null && buff.TimeLeftSeconds != null
                    && icon >= 0 && icon < buff.TimeLeftSeconds.Length
                    ? Math.Max(0, buff.TimeLeftSeconds[icon]) : 0;
            }
            catch { return 0; }
        }

        private bool StartMaintenancePulse(ActionKey key, int now)
        {
            // Only the hotkey assist owns its primary input. Suspend it while the secondary
            // cast executes, then UpdateMeleeAssist sends a fresh primary DOWN at its target.
            // A user's manual held generator is never released here.
            if (_assistActive) ReleaseAssistAttack();
            return StartPulse(key, now, Math.Max(80, GeneratorPulseMs), true);
        }

        private bool StartPulse(ActionKey key, int now, int holdMs, bool maintenance)
        {
            if (!s7o_GenMonkInput.Down(Owner, key)) return false;
            _pulse = key;
            _pulseStartedTick = now;
            _pulseReleaseTick = unchecked(now + Math.Max(1, holdMs));
            _pulseIsMaintenance = maintenance;
            return true;
        }

        private bool MaintenanceEffectConfirmed(int now)
        {
            if (!_pulseIsMaintenance || _targetKey == ActionKey.Unknown) return false;
            if (ElapsedMs(now, _pulseStartedTick) < Math.Max(1, GeneratorPulseMinMs)) return false;

            double comboLeft = BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, _targetIcon);
            if (_targetForesight)
            {
                double foresightLeft = ForesightLeft();
                bool foresightWasDue = _targetForesightBefore <= Math.Max(0.25, ForesightRefreshAtSeconds);
                bool comboWasDue = _hasCombinationStrike && _targetBefore <= CombinationRefreshAtSeconds;
                bool foresightConfirmed = !foresightWasDue
                    || (foresightLeft >= 4.0 && foresightLeft > _targetForesightBefore + 1.0);
                bool comboConfirmed = !comboWasDue
                    || (comboLeft >= 4.0 && comboLeft > _targetBefore + 1.0);
                return foresightConfirmed && comboConfirmed;
            }

            return comboLeft >= 5.0 && comboLeft > _targetBefore + 1.0;
        }

        private void FinishPulse(int now)
        {
            if (_pulse == ActionKey.Unknown) return;

            bool release = Due(now, _pulseReleaseTick);
            if (!release && _pulseIsMaintenance && MaintenanceEffectConfirmed(now))
                release = true;
            if (!release) return;

            s7o_GenMonkInput.Up(Owner, _pulse);
            _pulse = ActionKey.Unknown;
            _pulseIsMaintenance = false;
            _nextAttemptTick = unchecked(now + Math.Max(50, _targetForesight
                ? ForesightRetryGapMs : GeneratorRetryGapMs));
            // A self-dash must not restore its aim just because the keypress ended:
            // Diablo can still be consuming the queued cast. AdvanceSelfDash observes it.
            if (_selfDashKey == ActionKey.Unknown) RestoreCursor();
        }

        public int ActiveCombinationCount()
        {
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null || Hud.Game.Me.Powers == null) return 0;
            var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno);
            if (buff == null || !buff.Active || buff.TimeLeftSeconds == null) return 0;
            int count = 0;
            for (int icon = 2; icon <= 5; icon++)
                if (icon < buff.TimeLeftSeconds.Length && buff.TimeLeftSeconds[icon] > 0) count++;
            return count;
        }

        public void PaintTopInGame(ClipState clipState)
        {
            if (clipState != ClipState.AfterClip) return;
            _combinationDrawCount = 0;
            _combinationIcon = null;
            if (!ShowCombinationCount || Hud.Render.UiHidden
                || !Hud.Game.IsInGame || Hud.Game.IsLoading || Hud.Game.Me == null
                || Hud.Game.Me.HeroClassDefinition == null
                || Hud.Game.Me.HeroClassDefinition.HeroClass != HeroClass.Monk) return;
            int count = ActiveCombinationCount();
            if (count == 0) return;
            var skill = CombinationDisplaySkill();
            if (skill == null) return;
            var skillUi = Hud.Render.GetPlayerSkillUiElement(skill.Key);
            if (skillUi == null || !skillUi.Visible) return;
            var skillBox = skillUi.Rectangle;
            if (skillBox.Width <= 0 || skillBox.Height <= 0) return;
            // Draw only text on the actual skill button; its rectangle follows UI scale,
            // monitor layout and skill reassignment. Never steer the cursor while painting.
            var layout = _combinationFont.GetTextLayout(count.ToString());
            _combinationFont.DrawText(layout,
                skillBox.Right - skillBox.Width / 8.0f - layout.Metrics.Width,
                skillBox.Bottom - layout.Metrics.Height - skillBox.Width / 15.0f);
            _combinationIcon = skillUi;
            _combinationDrawCount = count;
        }

        private IPlayerSkill CombinationDisplaySkill()
        {
            IPlayerSkill held = null, previous = null;
            foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if (skill == null || skill.SnoPower == null || GeneratorIcon(skill) == 0) continue;
                if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno) return skill;
                if (skill.Key == _combinationDisplayKey) previous = skill;
                if (skill.Key != _pulse && skill.Key != _targetKey && s7o_GenMonkInput.IsDown(skill.Key))
                    if (held == null || skill.Key == _mainKey || skill.Key == ActionKey.LeftSkill) held = skill;
            }
            if (held != null) _combinationDisplayKey = held.Key;
            // Keep the last manual generator's count visible while its buffs wind down.
            return held ?? previous;
        }

        private void CancelTarget()
        {
            if (_selfDashKey != ActionKey.Unknown)
            {
                _selfDashResult = "cancelled";
                // A moving mouse must not cause repeated aim/restore attempts every frame.
                _nextDashAimTick = unchecked(Environment.TickCount + 250);
            }
            _selfDashKey = ActionKey.Unknown;
            _selfDashCastTick = int.MinValue;
            _selfDashBuffBefore = null;
            if (_pulse != ActionKey.Unknown)
                s7o_GenMonkInput.Up(Owner, _pulse);
            _pulse = ActionKey.Unknown;
            _pulseIsMaintenance = false;
            RestoreCursor();
            _targetKey = ActionKey.Unknown;
            _targetSno = 0;
            _targetForesight = false;
            _targetForesightBefore = 0;
            _attempts = 0;
        }

        private bool AssistRequested()
        {
            if (_assistCaptureSuppressed) return false;
            if (_assistWaitForRelease)
            {
                if (s7o_GenMonkInput.IsVirtualKeyDown(MeleeAssistVirtualKey)) return false;
                _assistWaitForRelease = false;
            }
            return MeleeAssistEnabled && s7o_GenMonkInput.IsVirtualKeyDown(MeleeAssistVirtualKey);
        }

        private bool IsMeleeTarget(IMonster monster, double range)
        {
            return monster != null && monster.IsAlive && monster.Attackable && monster.IsOnScreen
                && !monster.Illusion && !monster.Invulnerable && !monster.Untargetable
                && !monster.Hidden && !monster.Invisible && monster.FloorCoordinate != null
                && monster.NormalizedXyDistanceToMe <= range && monster.ZDistanceToMeAbsolute <= 8;
        }

        private uint AttackedIdentity()
        {
            foreach (uint modifier in AttackIdentityModifiers)
            {
                try
                {
                    uint identity = Hud.Game.Me.GetAttributeValueAsUInt(Hud.Sno.Attributes.Last_ACD_Attacked, modifier, 0u);
                    if (identity != 0u && identity != uint.MaxValue) return identity;
                }
                catch { }
            }
            return 0u;
        }

        private bool AttackedIdentityMatches(uint acd, uint ann)
        {
            uint identity = AttackedIdentity();
            if (identity == 0u) return false;
            if (identity == acd || (ann != 0u && identity == ann)) return true;
            foreach (var monster in Hud.Game.AliveMonsters)
                if (monster != null && monster.AcdId == acd && monster.AnnId == identity) return true;
            return false;
        }

        private IMonster ManualAttackTarget()
        {
            uint identity = AttackedIdentity();
            double range = Math.Max(1.0, Math.Min(15.0, AttackTargetRange));
            foreach (var monster in Hud.Game.AliveMonsters)
                if (IsMeleeTarget(monster, range) && identity != 0u
                    && (monster.AcdId == identity || monster.AnnId == identity)) return monster;
            // Only fall back to an actually highlighted melee monster during a real attack.
            var selected = Hud.Game.SelectedActor as IMonster;
            return IsMeleeTarget(selected, range) && selected.IsSelected ? selected : null;
        }

        private bool MainGeneratorAnimation()
        {
            if (Hud.Game.Me.AnimationState != AcdAnimationState.Attacking) return false;
            string animation = Hud.Game.Me.Animation.ToString();
            foreach (var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if (skill == null || skill.Key != _mainKey || skill.SnoPower == null) continue;
                if (skill.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
                    return HundredFistsAttackWindow();
                int icon = GeneratorIcon(skill);
                return icon != 0 && animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) < 0
                    && BuffLeft(Hud.Sno.SnoPowers.Monk_Passive_CombinationStrike.Sno, icon) >= 9.0;
            }
            return false;
        }

        private static int AssistTargetTier(IMonster monster)
        {
            if (monster.Rarity == ActorRarity.Boss || monster.Rarity == ActorRarity.Rare
                || monster.Rarity == ActorRarity.Champion || monster.Rarity == ActorRarity.Unique) return 2;
            return monster.Rarity == ActorRarity.RareMinion ? 1 : 0;
        }

        private IMonster AssistTarget()
        {
            IMonster current = null, nearest = null;
            int bestTier = -1;
            _assistUiBlockedTargets = false;
            foreach (var monster in Hud.Game.AliveMonsters)
            {
                if (!IsMeleeTarget(monster, double.MaxValue)) continue;
                int tier = AssistTargetTier(monster);
                if (tier < bestTier || (tier == bestTier && current != null)) continue;
                bool retained = monster.AcdId == _assistTargetAcd;
                if (tier == bestTier && !retained && nearest != null
                    && monster.NormalizedXyDistanceToMe >= nearest.NormalizedXyDistanceToMe) continue;
                // Project only a possible winner, not every monster every frame.
                int aimX, aimY;
                if (!TryGetAssistAim(monster, out aimX, out aimY))
                { _assistUiBlockedTargets = true; continue; }
                if (tier > bestTier) { bestTier = tier; current = nearest = null; }
                if (retained) current = monster;
                if (nearest == null || monster.NormalizedXyDistanceToMe < nearest.NormalizedXyDistanceToMe) nearest = monster;
            }
            return current ?? nearest;
        }

        private bool UpdateMeleeAssist(IPlayerSkill dash, int now)
        {
            if (!AssistRequested()) return false;
            if (!_assistActive)
            {
                // Do not adopt a mouse/key the user was already holding.
                foreach (var generator in _generators)
                    if (s7o_GenMonkInput.IsDown(generator.Key)) return false;
                IPlayerSkill primary = null;
                foreach (var generator in _generators)
                    if (primary == null || generator.SnoPower.Sno == Hud.Sno.SnoPowers.Monk_WayOfTheHundredFists.Sno)
                        primary = generator;
                CursorPoint cursor;
                if (primary == null || !GetCursorPos(out cursor)) return true;
                CancelTarget();
                _assistMainKey = primary.Key;
                _assistCursorX = cursor.X; _assistCursorY = cursor.Y;
                _assistActive = true; _assistEnding = false;
                _assistTargetAcd = 0u;
                _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
                _assistStatus = "acquire";
            }
            var target = AssistTarget();
            // A maintenance pulse no longer has work to verify after its combat target dies
            // or is replaced. Release our own pulse and acquire the next target this update.
            if (_pulse != ActionKey.Unknown)
            {
                if (target == null || target.AcdId != _assistTargetAcd) CancelTarget();
                else { _assistStatus = "secondary"; return true; }
            }
            if (_assistAttackKey != ActionKey.Unknown && ElapsedMs(now, _assistAttackStartedTick) >= 35
                && MainGeneratorAnimation() && AttackedIdentityMatches(_assistTargetAcd, _assistTargetAnn))
            {
                _assistHitVerified = true;
                _assistDashRetryNeedsAttack = false;
                _assistApproachPending = false; // Already attacking: no late approach Dash is needed.
            }
            if (target == null)
            {
                ReleaseAssistAttack();
                CancelTarget();
                _assistTargetAcd = 0u;
                _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
                _assistStatus = _assistUiBlockedTargets ? "ui-blocked" : "no-target";
                return true;
            }
            if (target.AcdId != _assistTargetAcd)
            {
                ReleaseAssistAttack();
                CancelTarget();
                _assistTargetAcd = target.AcdId; _assistTargetAnn = target.AnnId;
                _assistAimTick = now;
                _assistAimGameTick = Hud.Game.CurrentGameTick;
                _assistAimPending = true;
                _assistHitVerified = false;
                _assistApproachPending = true;
                _assistDashRetryNeedsAttack = false;
                _nextDashAimTick = now; // A previous target's cancelled aim must not delay this target.
            }
            _mainKey = _assistMainKey;
            _combinationDisplayKey = _mainKey;
            double meleeRange = Math.Max(1.0, Math.Min(15.0, MeleeAssistRange));
            bool approaching = target.NormalizedXyDistanceToMe > meleeRange;
            if (approaching && !_assistWasApproaching)
            {
                // A teleport/knockback can invalidate native LMB follow. Reacquire using
                // only the assist-owned attack, even when the small gap will be walked.
                ReleaseAssistAttack();
                CancelTarget();
            }
            _assistWasApproaching = approaching;
            // Approach every target category, including a new target inside attack range.
            // Once a Dash or native attack engages it, do not keep Dashing at a settled target.
            if (target.NormalizedXyDistanceToMe <= 0) _assistApproachPending = false;
            bool approachDash = approaching || _assistApproachPending;
            bool refreshDash = dash != null && DashDue(dash, now);
            if ((approachDash || refreshDash) && AutoDash && CanApproachDash(dash)
                // After an unconfirmed close Dash, let our primary actually attack before
                // another refresh suspends it. Remote approach retries remain available.
                && (!_assistDashRetryNeedsAttack || approaching)
                && Due(now, _nextDashAimTick)
                && (_lastApproachDashTargetAcd != target.AcdId || _lastApproachDashTick == int.MinValue
                    || ElapsedMs(now, _lastApproachDashTick) >= Math.Max(350, AssistApproachRetryMs)))
            {
                ReleaseAssistAttack();
                CancelTarget();
                MaintainDash(dash, now, target);
                if (_selfDashKey != ActionKey.Unknown)
                {
                    _lastApproachDashTick = now;
                    _lastApproachDashTargetAcd = target.AcdId;
                    _assistStatus = "target-dash";
                    return true;
                }
            }
            // A held DOWN can lose native attack continuity after a cast. Recover only the
            // assist-owned primary, with a bounded idle grace; never reclick manual input.
            if (_assistAttackKey != ActionKey.Unknown)
            {
                // Native LMB follow can also keep running beside an already reachable target.
                // Recover after the same bounded grace, but allow normal running at range.
                bool closeFollow = Hud.Game.Me.AnimationState == AcdAnimationState.Running
                    && target.NormalizedXyDistanceToMe <= Math.Min(2.0, meleeRange);
                bool idle = (Hud.Game.Me.AnimationState == AcdAnimationState.Idle || closeFollow)
                    && ElapsedMs(now, _assistAttackStartedTick) >= 150;
                if (idle && _assistIdleSinceTick == int.MinValue) _assistIdleSinceTick = now;
                if (!idle) _assistIdleSinceTick = int.MinValue;
                if (!s7o_GenMonkInput.IsDown(_assistAttackKey)
                    || (idle && ElapsedMs(now, _assistIdleSinceTick) >= 250))
                {
                    ReleaseAssistAttack();
                    CancelTarget();
                    _assistHitVerified = false;
                    _assistApproachPending = true;
                    _assistDashRetryNeedsAttack = false;
                    _assistRecoveries++;
                    _assistStatus = closeFollow ? "rearm-follow" : "rearm-idle";
                    return true;
                }
            }
            int x, y;
            // Recheck before moving a held LMB; a control/overlay may have appeared since selection.
            if (!TryGetAssistAim(target, out x, out y))
            { _assistStatus = "ui-blocked"; ReleaseAssistAttack(); return true; }
            CursorPoint cursorNow;
            if (!GetCursorPos(out cursorNow)
                || ((cursorNow.X != (int)x || cursorNow.Y != (int)y) && !SetCursorPos((int)x, (int)y)))
            { _assistStatus = "cursor-failed"; ReleaseAssistAttack(); return true; }
            _assistAimX = (int)x; _assistAimY = (int)y; _assistCursorOwned = true;
            if (_assistAttackKey == ActionKey.Unknown)
            {
                _assistStatus = "aim";
                var hovered = Hud.Game.SelectedActor as IMonster;
                // Reuse an aim Diablo already consumed at this unchanged cursor position.
                // Otherwise wait only for the mouse move to reach the next game update;
                // there is no additional fixed 20 ms delay and no indefinite hover gate.
                bool aimReady = hovered != null && hovered.AcdId == target.AcdId
                    && cursorNow.X == _assistAimX && cursorNow.Y == _assistAimY;
                if (_assistAimPending)
                {
                    _assistAimPending = false;
                    _assistAimTick = now; _assistAimGameTick = Hud.Game.CurrentGameTick;
                    if (!aimReady) return true;
                }
                if (!aimReady && Hud.Game.CurrentGameTick == _assistAimGameTick) return true;
                if (!s7o_GenMonkInput.Down(Owner, _assistMainKey))
                { _assistStatus = "input-busy"; return true; }
                _assistAttackKey = _assistMainKey;
                _assistAttackStartedTick = now;
                _assistIdleSinceTick = int.MinValue;
                _candidateKey = _mainKey; _candidateSinceTick = now;
            }
            _assistStatus = approaching ? "follow" : "attack";
            if (approaching) return true; // Native owned attack follows; generator maintenance waits for melee.
            return false; // Existing verified generator maintenance handles buffs unchanged.
        }

        private void ReleaseAssistAttack()
        {
            _assistAimPending = true;
            _assistIdleSinceTick = int.MinValue;
            if (_assistAttackKey == ActionKey.Unknown) return;
            s7o_GenMonkInput.Up(Owner, _assistAttackKey);
            _assistAttackKey = ActionKey.Unknown;
        }

        private void EndAssist()
        {
            ReleaseAssistAttack();
            CursorPoint current;
            if (_assistCursorOwned && Hud.Window.IsForeground && GetCursorPos(out current)
                && Math.Abs((long)current.X - _assistAimX) <= 128
                && Math.Abs((long)current.Y - _assistAimY) <= 128)
                SetCursorPos(_assistCursorX, _assistCursorY);
            _assistActive = _assistEnding = _assistCursorOwned = false;
            _assistAimPending = false;
            _assistTargetAcd = _assistTargetAnn = 0u;
            _assistMainKey = ActionKey.Unknown;
            _assistHitVerified = false;
            _assistWasApproaching = _assistApproachPending = _assistDashRetryNeedsAttack = false;
            _lastApproachDashTargetAcd = 0u;
            _lastApproachDashTick = int.MinValue;
            _assistStatus = "off";
        }

        private bool CanApproachDash(IPlayerSkill dash)
        {
            if (dash == null || dash.IsOnCooldown) return false;
            try
            {
                return Hud.Game.Me.Stats.ResourceCurPri + 0.1f >= Math.Max(0f, dash.GetResourceRequirement());
            }
            catch { return false; }
        }

        private bool DashDue(IPlayerSkill dash, int now)
        {
            if (!Due(now, _nextDashAimTick)) return false;
            bool radiance = (dash.RuneNameEnglish != null
                && dash.RuneNameEnglish.IndexOf("Radiance", StringComparison.OrdinalIgnoreCase) >= 0)
                || dash.Rune == 4;
            int interval = radiance ? 3500 : 5500;
            if (_lastDashTick == int.MinValue) return true;
            // With Raiment equipped, a missing damage buff overrides the normal cadence
            // after giving the previous cast time to appear in collected game data.
            double raiment = BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2);
            if ((uint)(now - _lastDashTick) >= 700u && Hud.Game.Me.GetSetItemCount(755275u) >= 6
                && raiment <= 0.6) return true;
            if ((uint)(now - _lastDashTick) < (uint)interval) return false;
            return radiance || raiment <= 0.6;
        }

        private void MaintainDash(IPlayerSkill dash, int now, IMonster approachTarget = null)
        {
            try
            {
                if (SelfDash || approachTarget != null)
                {
                    if (s7o_GenMonkInput.IsDown(dash.Key)
                        || s7o_InputReleaseArbiter.HasPendingRelease) return;
                    // Own attacks can be suspended. Never send UP for the user's held attack.
                    if (_assistActive) ReleaseAssistAttack();
                    var self = Hud.Game.Me.FloorCoordinate.ToScreenCoordinate(true, true);
                    IMonster attackTarget = approachTarget ?? (!_assistActive && _mainKey == ActionKey.LeftSkill
                        && !s7o_GenMonkInput.IsStandstillDown() ? ManualAttackTarget() : null);
                    if (attackTarget != null)
                        self = attackTarget.FloorCoordinate.ToScreenCoordinate(true, true);
                    if (self == null || double.IsNaN(self.X) || double.IsNaN(self.Y)
                        || double.IsInfinity(self.X) || double.IsInfinity(self.Y)) return;

                    CursorPoint current;
                    if (!GetCursorPos(out current)) return;

                    // Projected game coordinates are client-relative; retain the window offset
                    // so negative desktop coordinates on additional monitors remain valid.
                    var offset = Hud.Window.Offset;
                    long targetX = (long)Math.Round(self.X) + offset.X;
                    long targetY = (long)Math.Round(self.Y) + offset.Y;
                    if (targetX < int.MinValue || targetX > int.MaxValue
                        || targetY < int.MinValue || targetY > int.MaxValue) return;

                    _cursorX = current.X;
                    _cursorY = current.Y;
                    _dashTargetX = (int)targetX;
                    _dashTargetY = (int)targetY;
                    if (!SetCursorPos(_dashTargetX, _dashTargetY)) return;
                    _restoreCursor = true;
                    _selfDashKey = dash.Key;
                    _selfDashApproach = approachTarget != null;
                    _selfDashAimIsMonster = attackTarget != null && !_selfDashApproach;
                    _selfDashTargetAcd = attackTarget != null ? attackTarget.AcdId : 0u;
                    _selfDashObserved = false;
                    _selfDashSerial++;
                    _selfDashResult = "aim";
                    _selfDashAimTick = now;
                    _selfDashGameTick = Hud.Game.CurrentGameTick;
                    _selfDashRaimentBefore = _selfDashApproach
                        ? BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2) : 0;
                    _selfDashCastTick = int.MinValue;
                    var buff = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
                    _selfDashBuffBefore = buff != null && buff.TimeLeftSeconds != null
                        ? (double[])buff.TimeLeftSeconds.Clone() : new double[0];
                    return;
                }

                if (StartPulse(dash.Key, now, Math.Max(20, DashPulseMs), false))
                {
                    // Dash interrupts WotHF; wait for a fresh manual attack before maintenance.
                    // Do not move onto a monster to rebuild the native LMB lock.
                    _wasHundredFistsAttackWindow = false;
                    _freshHundredFistsPrime = false;
                    _lastDashTick = now;
                    _dashAwaiting = true;
                }
                else
                {
                    RestoreCursor();
                }
            }
            catch
            {
                _selfDashKey = ActionKey.Unknown;
                RestoreCursor();
            }
        }

        private void AdvanceSelfDash(int now)
        {
            CursorPoint current;
            bool attackRequested = _assistActive ? AssistRequested() || _assistEnding
                : s7o_GenMonkInput.IsDown(_mainKey);
            if (!AutoDash || (!SelfDash && !_selfDashApproach) || !attackRequested
                || !GetCursorPos(out current))
            {
                CancelTarget(); // Preserve mouse movement; RestoreCursor will not overwrite it.
                return;
            }

            if (_selfDashCastTick == int.MinValue)
            {
                if (_selfDashTargetAcd != 0u)
                {
                    bool valid = false;
                    foreach (var monster in Hud.Game.AliveMonsters)
                        if (monster != null && monster.AcdId == _selfDashTargetAcd
                            && IsMeleeTarget(monster, double.MaxValue)) { valid = true; break; }
                    if (!valid) { CancelTarget(); return; }
                }
                if (Math.Abs((long)current.X - _dashTargetX) > 12
                    || Math.Abs((long)current.Y - _dashTargetY) > 12)
                {
                    CancelTarget();
                    return;
                }
                // Let Diablo consume the mouse move before it receives the key press.
                if (ElapsedMs(now, _selfDashAimTick) > 250) { CancelTarget(); return; }
                if (ElapsedMs(now, _selfDashAimTick) < 20
                    || Hud.Game.CurrentGameTick == _selfDashGameTick) return;
                // A same-update DOWN/UP is not a reliable game-frame keypress. Reuse the
                // bounded pulse/cleanup path, keeping aim until the cast is observed.
                _selfDashAnimationBeforePress = Hud.Game.Me.Animation.ToString();
                _selfDashAnimationStartBeforePress = Hud.Game.Me.LoopingAnimationStartTick;
                _selfDashPressGameTick = Hud.Game.CurrentGameTick;
                if (!StartPulse(_selfDashKey, now, Math.Max(20, Math.Min(80, DashPulseMs)), false))
                { CancelTarget(); return; }
                _selfDashCastTick = now;
                _selfDashResult = "press";
                _selfDashPreviousDashTick = _lastDashTick;
                _lastDashTick = now;
                _dashAwaiting = true;
                _wasHundredFistsAttackWindow = false;
                _freshHundredFistsPrime = false;
                return;
            }

            // Manual Dash retains confirmation before restoring the user's cursor.
            // Assist Dash only waits for its own key release and an executing Dash animation.
            string animation = Hud.Game.Me.Animation.ToString();
            // A retained end animation from an earlier Dash is not evidence for this press.
            bool observed = animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0
                && (!string.Equals(animation, _selfDashAnimationBeforePress, StringComparison.Ordinal)
                    || (_selfDashApproach
                        && Hud.Game.Me.LoopingAnimationStartTick > _selfDashAnimationStartBeforePress
                        && Hud.Game.Me.LoopingAnimationStartTick >= _selfDashPressGameTick));
            // Fast recasts can reuse the same animation and refresh by less than one second.
            // Compare the known six-second Raiment timer with its decay on the game clock.
            // Other Dash buff indices retain their existing conservative confirmation rule.
            double assistTimerAge = _selfDashApproach
                ? Math.Max(0, Hud.Game.CurrentGameTick - _selfDashGameTick) / 60.0 : 0;
            if (_selfDashApproach
                && BuffLeft(Hud.Sno.SnoPowers.Generic_P2ItemPassiveUniqueRing033.Sno, 2)
                    > Math.Max(0, _selfDashRaimentBefore - assistTimerAge) + 0.1) observed = true;
            var buffNow = Hud.Game.Me.Powers.GetBuff(Hud.Sno.SnoPowers.Monk_DashingStrike.Sno);
            if (buffNow != null && buffNow.TimeLeftSeconds != null)
                for (int i = 0; i < buffNow.TimeLeftSeconds.Length; i++)
                {
                    double before = _selfDashBuffBefore != null && i < _selfDashBuffBefore.Length
                        ? _selfDashBuffBefore[i] : 0;
                    if (buffNow.TimeLeftSeconds[i] > before + 1.0) observed = true;
                }
            _selfDashObserved |= observed;
            bool approachInFlight = _selfDashApproach
                && Hud.Game.Me.AnimationState != AcdAnimationState.Idle
                && Hud.Game.Me.AnimationState != AcdAnimationState.Running
                && animation.IndexOf("dashing", StringComparison.OrdinalIgnoreCase) >= 0
                && animation.IndexOf("_end", StringComparison.OrdinalIgnoreCase) < 0;
            if (_selfDashApproach && _assistActive)
            {
                // Once the cast has finished, release its owned key immediately even if the
                // original pulse budget remains. Missing confirmation adds no idle/frame wait.
                if (_selfDashObserved && !approachInFlight && _pulse != ActionKey.Unknown)
                {
                    _pulseReleaseTick = now;
                    FinishPulse(now);
                }
                if (_pulse != ActionKey.Unknown || s7o_InputReleaseArbiter.HasPendingRelease
                    || approachInFlight) return;
                _selfDashResult = _selfDashObserved ? "confirmed" : "released-unconfirmed";
                _dashAwaiting = false;
                if (_selfDashTargetAcd == _assistTargetAcd)
                {
                    _assistApproachPending = false;
                    _assistDashRetryNeedsAttack = !_selfDashObserved;
                }
                if (!_selfDashObserved) _lastDashTick = _selfDashPreviousDashTick;
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                RestoreCursor();
                return;
            }
            bool manualAttackResumed = _selfDashAimIsMonster && MainGeneratorAnimation()
                && AttackedIdentityMatches(_selfDashTargetAcd, 0u);
            // Do not retain the cursor indefinitely if any shared release remains pending.
            // The arbiter continues retrying independently of this dash state.
            if (s7o_InputReleaseArbiter.HasPendingRelease && ElapsedMs(now, _selfDashCastTick) >= 350)
            {
                _selfDashResult = "release-pending";
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                RestoreCursor();
                return;
            }
            // Wait for release too: the shared arbiter retries any failed synthetic UP.
            if (_pulse == ActionKey.Unknown && !s7o_InputReleaseArbiter.HasPendingRelease
                && ((_selfDashObserved && (!_selfDashAimIsMonster || manualAttackResumed))
                    || ElapsedMs(now, _selfDashCastTick) >= (_selfDashAimIsMonster ? 700 : 350)))
            {
                _selfDashResult = _selfDashObserved
                    ? (_selfDashAimIsMonster && !manualAttackResumed ? "resume-timeout" : "confirmed")
                    : "timeout";
                if (!_selfDashObserved)
                {
                    // An unconfirmed manual press must not postpone the next required refresh
                    // for a full buff interval. Retain the existing manual aim backoff.
                    _lastDashTick = _selfDashPreviousDashTick;
                    _dashAwaiting = false;
                    _nextDashAimTick = unchecked(now + 250);
                }
                _selfDashKey = ActionKey.Unknown;
                _selfDashBuffBefore = null;
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            if (!_restoreCursor) return;
            CursorPoint current;
            if (Hud != null && Hud.Window != null && Hud.Window.IsForeground
                && GetCursorPos(out current)
                && Math.Abs((long)current.X - _dashTargetX) <= 128
                && Math.Abs((long)current.Y - _dashTargetY) <= 128)
            {
                // Preserve relative mouse movement during the brief self-target transaction.
                // Returning to the old point discards fine steering; leaving it near the hero
                // after a small movement makes the cursor appear to snap toward combat.
                long x = (long)_cursorX + current.X - _dashTargetX;
                long y = (long)_cursorY + current.Y - _dashTargetY;
                if (x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue)
                    SetCursorPos((int)x, (int)y);
            }
            _restoreCursor = false;
        }

        private void Stop()
        {
            ReleaseAssistAttack();
            CancelTarget();
            EndAssist();
            _mainKey = ActionKey.Unknown;
            _candidateKey = ActionKey.Unknown;
            _wasHundredFistsAttackWindow = false;
            _freshHundredFistsPrime = false;
            _dashAwaiting = false;
            RestoreCursor();
        }

        private static int ElapsedMs(int now, int then)
        {
            if (then == int.MinValue) return int.MaxValue;
            return unchecked((int)(uint)(now - then));
        }

        private static bool Due(int now, int target) { return unchecked(now - target) >= 0; }
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);
    }

    internal static class s7o_GenMonkInput
    {
        private static readonly Dictionary<ActionKey, int> OwnedActions = new Dictionary<ActionKey, int>();
        private static readonly int InputSize = Marshal.SizeOf(typeof(Input));
        private static s7o_AutoSkill _autoSkill;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public Data U; }
        [StructLayout(LayoutKind.Explicit)]
        private struct Data
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public IntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyInput
        {
            public ushort VirtualKey, ScanCode;
            public uint Flags, Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        private static s7o_AutoSkill ResolveAutoSkill()
        {
            if (_autoSkill != null) return _autoSkill;
            try
            {
                var hud = s7o_GenMonkInputContext.Hud;
                if (hud == null) return null;
                foreach (var plugin in hud.AllPlugins)
                {
                    var autoSkill = plugin as s7o_AutoSkill;
                    if (autoSkill == null) continue;
                    _autoSkill = autoSkill;
                    return autoSkill;
                }
            }
            catch { }
            return null;
        }

        public static bool IsDown(ActionKey key)
        {
            int code = VirtualKey(key);
            return code != 0 && (GetAsyncKeyState(code) & 0x8000) != 0;
        }

        public static bool IsVirtualKeyDown(ushort key)
        {
            return key != 0 && (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        public static bool IsStandstillDown()
        {
            var autoSkill = ResolveAutoSkill();
            return IsVirtualKeyDown(autoSkill != null ? autoSkill.ForceStandstillVirtualKey : (ushort)0x10);
        }

        public static bool Owns(ActionKey key) { return OwnedActions.ContainsKey(key); }
        public static bool Down(string owner, ActionKey key) { return Send(owner, key, false); }
        public static bool Up(string owner, ActionKey key) { return Send(owner, key, true); }

        private static int VirtualKey(ActionKey key)
        {
            switch (key)
            {
                case ActionKey.LeftSkill: return 0x01;
                case ActionKey.RightSkill: return 0x02;
                case ActionKey.Skill1: return BoundKey(ActionKey.Skill1, 0x31);
                case ActionKey.Skill2: return BoundKey(ActionKey.Skill2, 0x32);
                case ActionKey.Skill3: return BoundKey(ActionKey.Skill3, 0x33);
                case ActionKey.Skill4: return BoundKey(ActionKey.Skill4, 0x34);
                default: return 0;
            }
        }

        private static int BoundKey(ActionKey key, int fallback)
        {
            try
            {
                var autoSkill = ResolveAutoSkill();
                if (autoSkill == null) return fallback;
                ushort value = autoSkill.GetCastVirtualKey(key);
                return value == 0 ? fallback : value;
            }
            catch { return fallback; }
        }

        private static bool Send(string owner, ActionKey key, bool up)
        {
            int code;
            if (up)
            {
                if (!OwnedActions.TryGetValue(key, out code)) return true;
            }
            else
            {
                code = VirtualKey(key);
            }

            if (code == 0) return false;
            if (!up && IsDown(key)) return false; // The user's held input keeps ownership.
            // Final boundary: also covers a secondary generator or Dash equipped on LMB.
            if (!up && key == ActionKey.LeftSkill
                && (s7o_GenMonkInputContext.CanPressLeftSkill == null
                    || !s7o_GenMonkInputContext.CanPressLeftSkill())) return false;

            Input input = new Input();
            bool mouse = key == ActionKey.LeftSkill || key == ActionKey.RightSkill;
            input.Type = mouse ? 0u : 1u;
            if (mouse)
                input.U.Mouse.Flags = key == ActionKey.LeftSkill
                    ? (up ? 0x0004u : 0x0002u) : (up ? 0x0010u : 0x0008u);
            else
            {
                input.U.Keyboard.VirtualKey = (ushort)code;
                input.U.Keyboard.Flags = up ? 2u : 0u;
            }

            int identity = mouse ? (key == ActionKey.LeftSkill ? 0x10001 : 0x10002) : code;
            Input[] packet = new[] { input };
            Func<bool> emit = () => SendInput(1u, packet, InputSize) == 1u;

            if (up)
            {
                bool released = s7o_InputReleaseArbiter.Up(owner, identity, emit);
                OwnedActions.Remove(key); // Arbiter owns retries after a failed UP.
                return released;
            }

            bool acquired = s7o_InputReleaseArbiter.Down(owner, identity, emit);
            if (acquired) OwnedActions[key] = code;
            return acquired;
        }
    }

    internal static class s7o_GenMonkInputContext
    {
        public static IController Hud;
        public static Func<bool> CanPressLeftSkill;
    }
}
