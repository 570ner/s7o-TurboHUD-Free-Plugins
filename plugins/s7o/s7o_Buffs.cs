using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.DirectInput;
using Turbo.Plugins.Default;
using Keys = System.Windows.Forms.Keys;

namespace Turbo.Plugins.s7o
{
    public class s7o_buffs : BasePlugin, IAfterCollectHandler, IInGameTopPainter, IKeyEventHandler
    {
        public bool ShowPlayerBuffs { get; set; } = false;
        public bool ShowShrines { get; set; } = true;
        public bool ShowPylons { get; set; } = true;
        public bool ShowLegendary { get; set; } = true;
        public bool ShowCheatDeath { get; set; } = true;
        public bool ShowClassSkills { get; set; } = true;
        public float IconSize { get; set; } = 0.85f;
        public float Opacity { get; set; } = 0.85f;
        public Key MoveHotkey { get; set; } = Key.D0;
        // Optional native rules for additional effects; labels never control detection.
        public List<BuffRule> AdditionalPowerRules { get; } = new List<BuffRule>();
        private readonly float[] _x = { -0.13f, 0f, 0.13f };
        private readonly float[] _y = { -0.02f, 0.12f, -0.02f };
        private readonly RectangleF[] _bounds = new RectangleF[3];
        private readonly BuffRuleCalculator[] _groups = new BuffRuleCalculator[3];
        private sealed class PlayerBuffPainter : BuffPainter
        {
            public PlayerBuffPainter(IController hud) : base(hud,true) { }
            public void PaintOne(BuffPaintInfo info,RectangleF rect) { Paint(info,rect); }
        }
        private PlayerBuffPainter _painter;
        private IFont _label;
        private IBrush _outline, _back, _recovery;
        private IUiElement _chat;
        private s7o_HUD_MENU _menu;
        private string _settings;
        private bool _held, _positionDirty;
        private int _drag = -1;
        private float _grabX, _grabY, _styleSize = -1, _styleOpacity = -1;
        private static readonly uint[] ShrinePowers = {278269,30477,278271,30479,278270,30478,278268,30476,260349,260348};
        private static readonly uint[] PylonPowers = {263029,403404,266258,266254,262935,266271};
        // Native identifiers from ISnoPowerList; no localized labels or cooldown guesses.
        private static readonly HashSet<uint> ClassSkillPowers = new HashSet<uint> {
            79607,80049,79528, // Berserker, Ancients, Ignore Pain
            269032,291804, // Akarat, Iron Skin
            302846,130831, // Vengeance, Rain of Vengeance
            312307,317076,96215, // Epiphany, Inner Sanctuary, Serenity
            465839,465350,460358, // Land of the Dead, Simulacrum, Army of the Dead
            117402, // Big Bad Voodoo
            134872,75599 // Archon, Diamond Skin
        };
        private static readonly uint[] ClassDeathPowers = {156484,208474,359580,324770,218501,309830,217819,465952};
        private static readonly uint[] FollowerDeathPowers = {485531,485530,485532};
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        public s7o_buffs() { Enabled = true; }
        private static float Clamp(float value,float min,float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? min : Math.Max(min,Math.Min(max,value)); }
        private static string T(string key,string text) { return s7o_Localization.Get("playerbuffs."+key,text); }
        public override void Load(IController hud)
        {
            base.Load(hud);
            _settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plugins","s7o","settings","s7o_buffs.ini");
            LoadSettings();
            for(int i=0;i<3;i++) _groups[i]=new BuffRuleCalculator(Hud);
            _painter = new PlayerBuffPainter(Hud) { ShowTooltips=false,ShowTimeLeftNumbers=true };
            _label=Hud.Render.CreateFont("tahoma",8,255,255,235,150,true,false,255,0,0,0,true);
            _outline=Hud.Render.CreateBrush(230,255,210,70,1);
            _back=Hud.Render.CreateBrush(160,0,0,0,0);
            _recovery=Hud.Render.CreateBrush(255,55,235,90,2f);
            _chat=Hud.Render.RegisterUiElement("Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline",null,null);
        }
        private bool CanDisplay()
        {
            return Enabled && ShowPlayerBuffs && Hud.Game.IsInGame && !Hud.Game.IsLoading
                && Hud.Game.Me!=null && !Hud.Render.UiHidden && Hud.Window.IsForeground
                && !Hud.Render.WorldMapUiElement.Visible && !Hud.Render.ActMapUiElement.Visible;
        }
        private bool CanMove()
        {
            if(!CanDisplay() || Hud.Game.Me.IsDead || (_chat!=null && _chat.Visible))return false;
            if(_menu==null) _menu=Hud.AllPlugins.OfType<s7o_HUD_MENU>().FirstOrDefault();
            return (_menu==null || !_menu.PlayerBuffsEditingBlocked)
                && (Hud.Inventory.InventoryMainUiElement==null || !Hud.Inventory.InventoryMainUiElement.Visible);
        }
        private bool KeyHeld()
        {
            uint scan=(uint)MoveHotkey;
            uint vk=MapVirtualKey(scan>=128 ? 0xE000u|(scan&127u) : scan,3);
            return vk!=0 && Hud.Input.IsKeyDown((Keys)vk);
        }
        public void OnKeyEvent(IKeyEvent e)
        {
            if(e==null || e.Key!=MoveHotkey)return;
            if(!e.IsPressed) { EndDrag(); return; }
            if(_held || e.ControlPressed || e.AltPressed || e.ShiftPressed || !CanMove())return;
            _held=true;
            PickDrag();
        }
        private void PickDrag()
        {
            if(_drag>=0)return;
            for(int i=0;i<3;i++)
                if(GroupEnabled(i) && _bounds[i].Contains(Hud.Window.CursorX,Hud.Window.CursorY))
                {
                    _drag=i;
                    _grabX=Hud.Window.CursorX-(_bounds[i].X+_bounds[i].Width*0.5f);
                    _grabY=Hud.Window.CursorY-(_bounds[i].Y+_bounds[i].Height*0.5f);
                    return;
                }
        }
        private void EndDrag()
        {
            _held=false;_drag=-1;
            if(_positionDirty){_positionDirty=false;SaveSettings();}
        }
        public void ResetPositions()
        {
            EndDrag();_x[0]=-0.13f;_x[1]=0;_x[2]=0.13f;
            _y[0]=_y[2]=-0.02f;_y[1]=0.12f;SaveSettings();
        }
        private bool GroupEnabled(int i)
        { return i==0 ? ShowShrines : i==1 ? ShowPylons||ShowClassSkills||ShowLegendary : ShowCheatDeath; }
        public void AfterCollect()
        {
            if(_held && (!CanMove() || !KeyHeld()))EndDrag();
            if(!CanDisplay())return;
            if(_held)
            {
                PickDrag();
                if(_drag>=0)
                {
                    float h=Math.Max(1,Hud.Window.Size.Height);
                    _x[_drag]=(Clamp(Hud.Window.CursorX-_grabX,16,Hud.Window.Size.Width-16)-Hud.Window.Size.Width*0.5f)/h;
                    _y[_drag]=Clamp(Hud.Window.CursorY-_grabY,16,h-16)/h-0.5f;
                    _positionDirty=true;
                }
            }
            foreach(var group in _groups)group.Rules.Clear();
            if(ShowShrines)foreach(uint sno in ShrinePowers)AddActiveRules(_groups[0],new BuffRule(sno));
            if(ShowPylons)foreach(uint sno in PylonPowers)AddActiveRules(_groups[1],new BuffRule(sno));
            if(ShowLegendary)
            {
                AddActiveRules(_groups[1],new BuffRule(359583){IconIndex=1});AddActiveRules(_groups[1],new BuffRule(359583){IconIndex=2});
                AddActiveRules(_groups[1],new BuffRule(403471){ShowStacks=true});
                AddActiveRules(_groups[1],new BuffRule(246562));
                AddActiveRules(_groups[1],new BuffRule(483552){ShowStacks=true});
                foreach(var rule in AdditionalPowerRules)if(rule!=null)AddActiveRules(_groups[1],rule);
            }
            if(ShowCheatDeath)foreach(uint sno in ClassDeathPowers)AddActiveRules(_groups[2],new BuffRule(sno){IconIndex=1});
            if(ShowCheatDeath)foreach(uint sno in FollowerDeathPowers)
            {
                var b=Hud.Game.Me.Powers.GetBuff(sno);
                int index=FollowerTimerIndex(b);
                if(index>=0)AddActiveRules(_groups[2],new BuffRule(sno){IconIndex=index});
            }
            IconSize=Clamp(IconSize,0.5f,2f);Opacity=Clamp(Opacity,0.1f,1f);
            foreach(var group in _groups){group.SizeMultiplier=IconSize;group.CalculatePaintInfo(Hud.Game.Me);}
            if(ShowClassSkills)AddClassSkillIcons();
        }
        private void AddClassSkillIcons()
        {
            var group=_groups[1];
            foreach(var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if(skill==null || skill.SnoPower==null || !ClassSkillPowers.Contains(skill.SnoPower.Sno))continue;
                var power=skill.SnoPower;
                var buff=skill.Buff;
                int index=-1; double longest=0;
                if(buff!=null && buff.TimeLeftSeconds!=null)
                    for(int i=0;i<buff.TimeLeftSeconds.Length;i++)
                        if(LiveIcon(buff,i) && !buff.SnoPower.Icons[i].Harmful)
                        {
                            double left=buff.TimeLeftSeconds[i];
                            if(double.IsInfinity(left)||double.IsNaN(left)||left<0)left=0;
                            if(index<0 || left>longest)
                            {index=i;longest=left;}
                        }
                bool active=index>=0;
                int start=skill.CooldownStartTick,finish=skill.CooldownFinishTick,now=Hud.Game.CurrentGameTick;
                bool cooling=skill.IsOnCooldown && start>=0 && finish>start && finish>now;
                if(!active && !cooling)continue;
                if(group.PaintInfoList.Any(x=>x.SnoPower!=null && x.SnoPower.Sno==power.Sno))continue;
                var texture=power.NormalIconTextureId>0?Hud.Texture.GetTexture(power.NormalIconTextureId):null;
                if(texture==null)continue;
                double leftTime=active?Math.Max(0,buff.TimeLeftSeconds[index]):Math.Max(0,((long)finish-now)/60.0);
                double elapsed=active?Math.Max(0,buff.TimeElapsedSeconds[index]):Math.Max(0,((long)now-start)/60.0);
                if(double.IsInfinity(leftTime)||double.IsNaN(leftTime))leftTime=0;
                if(double.IsInfinity(elapsed)||double.IsNaN(elapsed))elapsed=0;
                var icon=active?buff.SnoPower.Icons[index]:new SnoPowerIcon {Exists=true,TextureId=power.NormalIconTextureId};
                group.PaintInfoList.Add(new BuffPaintInfo {
                    Id=power.Sno,SnoPower=power,Icons=new List<SnoPowerIcon>{icon},Texture=texture,
                    TimeLeft=leftTime,Elapsed=elapsed,Stacks=-1,Size=group.StandardIconSize,
                    Rule=new BuffRule(power.Sno){UsePowersTexture=true,ShowTimeLeft=true},TimeLeftNumbersOverride=true
                });
            }
        }
        private static bool LiveIcon(IBuff buff,int i)
        {
            return buff!=null && buff.Active && buff.SnoPower!=null && buff.SnoPower.Icons!=null
                && buff.IconCounts!=null && buff.TimeLeftSeconds!=null && buff.TimeElapsedSeconds!=null
                && i>=0 && i<buff.SnoPower.Icons.Length && i<buff.IconCounts.Length
                && i<buff.TimeLeftSeconds.Length && i<buff.TimeElapsedSeconds.Length
                && buff.IconCounts[i]>0 && buff.SnoPower.Icons[i].Exists && !buff.SnoPower.Icons[i].IsHidden;
        }
        private static int FollowerTimerIndex(IBuff buff)
        {
            int best=-1;double left=0;
            if(buff==null || buff.TimeLeftSeconds==null)return best;
            // Cooldown is native remaining time, never a guessed two-minute countdown.
            for(int i=0;i<buff.TimeLeftSeconds.Length;i++)
                if(LiveIcon(buff,i) && buff.TimeLeftSeconds[i]>left)
                {best=i;left=buff.TimeLeftSeconds[i];}
            return best;
        }
        private void AddActiveRules(BuffRuleCalculator group,BuffRule source)
        {
            var buff=Hud.Game.Me.Powers.GetBuff(source.PowerSno);
            if(buff==null || buff.TimeLeftSeconds==null)return;
            for(int i=0;i<buff.TimeLeftSeconds.Length;i++)
            {
                if(!LiveIcon(buff,i) || (source.IconIndex.HasValue && source.IconIndex.Value!=i)
                    || (source.NoIconIndex.HasValue && source.NoIconIndex.Value==i)
                    || buff.IconCounts[i]<source.MinimumIconCount || group.Rules.Any(r=>r.PowerSno==source.PowerSno&&r.IconIndex==i))continue;
                group.Rules.Add(new BuffRule(source.PowerSno){IconIndex=i,MinimumIconCount=source.MinimumIconCount,
                    ShowTimeLeft=source.ShowTimeLeft,ShowStacks=source.ShowStacks,AllowInGameMergeRules=false,
                    UsePowersTexture=source.UsePowersTexture,UseLegendaryItemTexture=source.UseLegendaryItemTexture,
                    IconSizeMultiplier=source.IconSizeMultiplier});
            }
        }
        public void PaintTopInGame(ClipState clipState)
        {
            if(clipState!=ClipState.BeforeClip || !CanDisplay())return;
            if(_held && !CanMove()){EndDrag();return;}
            if(_styleSize!=IconSize || _styleOpacity!=Opacity)
            {
                _styleSize=IconSize;_styleOpacity=Opacity;
                _painter.Opacity=Opacity;
                _recovery.Opacity=Opacity;_recovery.StrokeWidth=Math.Max(1.5f,2f*IconSize);
                _painter.TimeLeftFont=Hud.Render.CreateFont("tahoma",Math.Max(5,8*IconSize),255,255,255,255,false,false,255,0,0,0,true);
                _painter.StackFont=Hud.Render.CreateFont("tahoma",Math.Max(5,8*IconSize),255,255,255,255,false,false,255,0,0,0,true);
                _painter.TimeLeftFont.Opacity=Opacity;_painter.StackFont.Opacity=Opacity;
                _painter.TimeLeftClockBrush=Hud.Render.CreateBrush((int)(180*Opacity),0,0,0,0);
            }
            for(int i=0;i<3;i++)
            {
                if(!GroupEnabled(i)){_bounds[i]=RectangleF.Empty;continue;}
                var group=_groups[i];var list=group.PaintInfoList;
                float size=group.StandardIconSize, gap=group.StandardIconSpacing;
                int count=Math.Max(1,list.Count);
                int cols=i==1?Math.Min(6,count):(int)Math.Ceiling(count/6.0);
                int rows=i==1?(int)Math.Ceiling(count/(double)cols):Math.Min(6,count);
                float w=cols*(size+gap)-gap,h=rows*(size+gap)-gap;
                float x=Clamp(Hud.Window.Size.Width*0.5f+Hud.Window.Size.Height*_x[i]-w/2,2,Math.Max(2,Hud.Window.Size.Width-w-2));
                float y=Clamp(Hud.Window.Size.Height*(0.5f+_y[i])-h/2,2,Math.Max(2,Hud.Window.Size.Height-h-2));
                _bounds[i]=new RectangleF(x,y,w,h);
                for(int j=0;j<list.Count;j++)
                {
                    int col=i==1?j%cols:j/rows, row=i==1?j/cols:j%rows;
                    var iconRect=new RectangleF(x+col*(size+gap),y+row*(size+gap),list[j].Size,list[j].Size);
                    _painter.PaintOne(list[j],iconRect);
                    float recovery=NativeRecoveryProgress(list[j]);
                    if(recovery>=0)DrawRecoveryOutline(iconRect,recovery);
                }
                if(_held)
                {
                    if(list.Count==0)_back.DrawRectangle(x,y,w,h);
                    _outline.DrawRectangle(x-3,y-3,w+6,h+6);
                    string label=i==0?T("shrines","Shrines"):i==1?T("powers","Pylons / class skills / legendary powers"):T("cheatdeath","Cheat Death Icons");
                    _label.DrawText(label,x,y-19,true);
                }
            }
        }
        private static float RecoveryFraction(double elapsed,double remaining)
        {
            if(double.IsNaN(elapsed)||double.IsInfinity(elapsed)||elapsed<0
                ||double.IsNaN(remaining)||double.IsInfinity(remaining)||remaining<0)return -1;
            double total=elapsed+remaining;
            return total>0&&!double.IsInfinity(total)?(float)Math.Max(0,Math.Min(1,elapsed/total)):-1;
        }
        private float NativeRecoveryProgress(BuffPaintInfo info)
        {
            if(info==null || info.SnoPower==null)return -1;
            uint sno=info.SnoPower.Sno;
            if(ClassDeathPowers.Contains(sno)||FollowerDeathPowers.Contains(sno))
                return RecoveryFraction(info.Elapsed,info.TimeLeft);
            // An effect's lifetime is not its skill cooldown. Use native skill timestamps.
            foreach(var skill in Hud.Game.Me.Powers.UsedSkills)
            {
                if(skill==null || !skill.IsOnCooldown || skill.SnoPower==null)continue;
                if(skill.SnoPower.Sno!=sno && (skill.CurrentSnoPower==null || skill.CurrentSnoPower.Sno!=sno))continue;
                int start=skill.CooldownStartTick,finish=skill.CooldownFinishTick,now=Hud.Game.CurrentGameTick;
                if(start<0 || finish<=start)return -1;
                return RecoveryFraction(Math.Max(0,(long)now-start),Math.Max(0,(long)finish-now));
            }
            return -1;
        }
        private void DrawRecoveryOutline(RectangleF rect,float progress)
        {
            if(progress<=0 || rect.Width<=2 || rect.Height<=2)return;
            float left=rect.Left+1,top=rect.Top+1,right=rect.Right-1,bottom=rect.Bottom-1;
            float middle=(left+right)*0.5f;
            float remaining=2*(right-left+bottom-top)*Math.Min(1,progress);
            DrawRecoveryEdge(middle,top,right,top,ref remaining);
            DrawRecoveryEdge(right,top,right,bottom,ref remaining);
            DrawRecoveryEdge(right,bottom,left,bottom,ref remaining);
            DrawRecoveryEdge(left,bottom,left,top,ref remaining);
            DrawRecoveryEdge(left,top,middle,top,ref remaining);
        }
        private void DrawRecoveryEdge(float x1,float y1,float x2,float y2,ref float remaining)
        {
            if(remaining<=0)return;
            float length=Math.Abs(x2-x1)+Math.Abs(y2-y1);
            if(length<=0)return;
            float fraction=Math.Min(1,remaining/length);
            _recovery.DrawLine(x1,y1,x1+(x2-x1)*fraction,y1+(y2-y1)*fraction);
            remaining-=length*fraction;
        }

        private ITexture _powerPylonPreview;
        public float PreviewIconSize => 55f/1200f*Hud.Window.Size.Height*Clamp(IconSize,0.5f,2f);
        public void PaintPowerPylonPreview(RectangleF rect)
        {
            if(_powerPylonPreview==null)
            {
                var power=Hud.Sno.GetSnoPower(262935);
                if(power==null)return;
                uint texture=power.NormalIconTextureId;
                if(power.Icons!=null)
                    foreach(var icon in power.Icons)
                        if(icon.Exists&&!icon.IsHidden&&icon.TextureId>0){texture=icon.TextureId;break;}
                if(texture>0)_powerPylonPreview=Hud.Texture.GetTexture(texture);
            }
            float opacity=Clamp(Opacity,0.1f,1f);
            if(_powerPylonPreview!=null)
            {
                _powerPylonPreview.Draw(rect,opacity);
                Hud.Texture.BuffFrameTexture.Draw(rect,opacity);
            }
        }

        public void SaveSettings()
        {
            if(string.IsNullOrEmpty(_settings))return;
            try
            {
                var lines=new List<string>();
                foreach(var name in BoolNames)lines.Add(name+"="+GetFlag(name));
                lines.Add("IconSize="+IconSize.ToString(CultureInfo.InvariantCulture));
                lines.Add("Opacity="+Opacity.ToString(CultureInfo.InvariantCulture));lines.Add("MoveHotkey="+MoveHotkey);
                for(int i=0;i<3;i++){lines.Add("X"+i+"="+_x[i].ToString(CultureInfo.InvariantCulture));lines.Add("Y"+i+"="+_y[i].ToString(CultureInfo.InvariantCulture));}
                Directory.CreateDirectory(Path.GetDirectoryName(_settings));File.WriteAllLines(_settings,lines);
            }catch { }
        }
        private static readonly string[] BoolNames={"ShowPlayerBuffs","ShowShrines","ShowPylons","ShowClassSkills","ShowCheatDeath","ShowLegendary"};
        public bool GetFlag(string name)
        {
            switch(name){case "ShowPlayerBuffs":return ShowPlayerBuffs;case "ShowShrines":return ShowShrines;
            case "ShowPylons":return ShowPylons;case "ShowClassSkills":return ShowClassSkills;
            case "ShowCheatDeath":return ShowCheatDeath;case "ShowLegendary":return ShowLegendary;default:return false;}
        }
        public void SetFlag(string name,bool value)
        {
            switch(name){case "ShowPlayerBuffs":ShowPlayerBuffs=value;break;case "ShowShrines":ShowShrines=value;break;
            case "ShowPylons":ShowPylons=value;break;case "ShowClassSkills":ShowClassSkills=value;break;
            case "ShowCheatDeath":ShowCheatDeath=value;break;case "ShowLegendary":ShowLegendary=value;break;}
        }
        private void LoadSettings()
        {
            try
            {
                if(!File.Exists(_settings))return;
                bool? legacyClass=null,legacyFollower=null;bool explicitCheatDeath=false;
                foreach(string line in File.ReadAllLines(_settings))
                {
                    int split=line.IndexOf('=');if(split<=0)continue;string key=line.Substring(0,split),value=line.Substring(split+1);
                    bool flag;float number;Key hotkey;
                    if(BoolNames.Contains(key)&&bool.TryParse(value,out flag))
                    {SetFlag(key,flag);if(key=="ShowCheatDeath")explicitCheatDeath=true;continue;}
                    if((key=="ShowClassDeath"||key=="ShowFollowerDeath")&&bool.TryParse(value,out flag))
                    {if(key=="ShowClassDeath")legacyClass=flag;else legacyFollower=flag;continue;}
                    if(key=="MoveHotkey"&&Enum.TryParse(value,out hotkey)&&Enum.IsDefined(typeof(Key),hotkey)&&hotkey!=Key.Unknown)MoveHotkey=hotkey;
                    if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)||float.IsNaN(number)||float.IsInfinity(number))continue;
                    if(key=="IconSize")IconSize=Clamp(number,0.5f,2f);else if(key=="Opacity")Opacity=Clamp(number,0.1f,1f);
                    for(int i=0;i<3;i++){if(key=="X"+i)_x[i]=Clamp(number,-4,4);if(key=="Y"+i)_y[i]=Clamp(number,-0.48f,0.48f);}
                }
                if(!explicitCheatDeath && (legacyClass.HasValue||legacyFollower.HasValue))
                    ShowCheatDeath=legacyClass.GetValueOrDefault(true)||legacyFollower.GetValueOrDefault(true);
            }catch { }
        }
    }
}
