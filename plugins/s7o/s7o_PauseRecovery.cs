// Automatic source-only pause recovery. Install one edition for your HUD.
/* Maintenance for plugin developers and AI assistants:
 * This edition discovers bindings; an executable hash is not an enable/disable gate.
 * Inspect the user's actual TurboHUD.exe and compile with its Turbo.Plugins.dll,
 * Turbo.SNO.dll, default BasePlugin/ValueFormat sources and matching dependencies.
 * Trace IGameController.IsPaused -> collector field -> integer cache getter.
 * Use IL disassembly/ReflectionOnly metadata first; inspect Bindings and
 * Bridge.Resolve if a getter or native-reader call sequence changes.
 * FreeHUD: trace the cache setter's native read in the collector update to its
 * GameState address/offset; trace the raw tick and pre-collection event similarly.
 * LM: identify existing kernel32 imports by entry point AND exact ABI; inspect
 * direct collector Int64 fields read-only for ONE matching GameState structure.
 * Never treat a metadata token as stable across builds or execute random getters.
 * FreeHUD reads pause/tick/loaded-state offsets from the traced native reader.
 * LM's native GameState layout still has checks: paired pointers at
 * 0x118/0x120 differ by 40, zero at 0x1C, Boolean at 0x6C, pause Int32 at 0x15C.
 * If a game update changes the LM layout, trace the new reader or collect a
 * read-only diagnostic before changing these offsets; do not guess/write to test.
 * Diagnostic file: <HUD>/logs/s7o_PauseRecovery.log. Production writes no log.
 * Diagnostic log: repeated solo Escape pause -> close menu -> unpause should
 * show source/cache 1/1 -> 0/0 at the same source. Solo pauses must remain intact.
 * Solo correlation validates binding, NOT multiplayer recovery. For a real false
 * pause, require loaded multiplayer, closed blocking menus and advancing ticks;
 * verify one four-byte source clear followed by source/cache 0/0 on later
 * collections. Check automation continuity, inventory/Urshi and world transitions.
 * Do not replace Hud.Game, force the cache, reset other plugins or write every frame.
 * Unknown/ambiguous layouts must leave recovery inactive and report compatibility.
 * Rebuild BOTH editions after edits. Production must contain no log writer.
 * Test in an owned process buffer first; never use live game writes as discovery.
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Runtime.InteropServices;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_PauseRecovery : BasePlugin, IAfterCollectHandler, IInGameTopPainter, INewAreaHandler
    {
        private Bridge _bridge;
        private Recovery _recovery;
        private int _unboundSince;private bool _unbound,_unboundNotice;
        private IFont _font;
        private IBrush _background;
        private IUiElement[] _menus;
        private string _toast="";
        private int _toastUntil;

        internal static int Age(int now,int then){return unchecked(now-then);}

        public s7o_PauseRecovery(){Enabled=true;Order=-10000;}
        public override void Load(IController hud)
        {
            base.Load(hud);
            try {
                _font=Hud.Render.CreateFont("tahoma",11,255,170,255,170,false,false,true);
                _background=Hud.Render.CreateBrush(210,12,18,12,0);
            }catch{}
            try {
                _menus=new[] {
                    "Root.NormalLayer.gamemenu_dialog.gamemenu_bkgrnd",
                    "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList",
                    "Root.NormalLayer.BattleNetProfile_main.LayoutRoot.OverlayContainer",
                    "Root.NormalLayer.BattleNetLeaderboard_main.LayoutRoot.OverlayContainer",
                    "Root.NormalLayer.BattleNetAchievements_main.LayoutRoot.OverlayContainer",
                    "Root.NormalLayer.BattleNetStore_main.LayoutRoot.OverlayContainer",
                    "Root.NormalLayer.Guild_main.LayoutRoot.OverlayContainer"
                }.Select(p=>Hud.Render.RegisterUiElement(p,null,null)).ToArray();
            }catch{Notice("Pause recovery unavailable: menu detection failed.");}
            try {
                _bridge=Bridge.Bind(hud.Game);
                _recovery=new Recovery(_bridge,Notice);
                if(!_bridge.Hook(BeforeCollect))Notice("Pause recovery unavailable: collector binding failed.");
                AppDomain.CurrentDomain.DomainUnload+=OnUnload;
            }catch{Notice("Pause recovery unavailable for this FreeHUD build.");}
        }
        private void OnUnload(object sender,EventArgs e){if(_bridge!=null)_bridge.Unhook();}
        private void BeforeCollect(object sender,EventArgs args)
        {
            if(!Enabled || _recovery==null)return;
            try{_recovery.BeforeCollect(Environment.TickCount);}catch{}
        }
        public void OnNewArea(bool newGame,ISnoArea area)
        {if(newGame && _recovery!=null){_recovery.NewGame();}}
        public void AfterCollect()
        {
            try {
                int now=Environment.TickCount;

                if(_recovery==null)return;
                var g=Hud.Game;var me=g.Me;
                bool loaded=Enabled && g.IsInGame && !g.IsLoading && me!=null && me.HasValidActor;
                // Menus gate the INITIAL source-write proposal only. They never make
                // recovery restore paused=True. Inventory, chat, conversations and
                // scripted sequences are left to their existing automation owners.
                bool menusClear=_menus!=null && _menus.Length==7 && _menus.All(ui=>ui!=null&&!ui.Visible);
                var f=new Frame {
                    Now=now,Loaded=loaded,Foreground=Hud.Window.IsForeground,MenusClear=menusClear,
                    Players=g.NumberOfPlayersInGame,Hero=me==null?0:me.HeroId,World=me==null?0:me.WorldId
                };
                // Healthy callbacks need no reflection or process-memory reads.
                if(loaded && f.Players>1 && (g.IsPaused || _recovery.Mode!=Phase.Idle))f.State=_bridge.Read();
                if(loaded&&f.Players>1&&g.IsPaused&&f.State!=null&&!f.State.Valid){
                    if(!_unbound){_unbound=true;_unboundSince=now;}
                    else if(!_unboundNotice&&Age(now,_unboundSince)>=2500){
                        _unboundNotice=true;Notice("Pause recovery unavailable: source binding could not be verified.");
                    }
                }else{_unbound=false;_unboundNotice=false;}
                _recovery.Observe(f);

            }catch{if(_recovery!=null)_recovery.Suspend();}
        }
        private void Notice(string text){_toast=text;_toastUntil=unchecked(Environment.TickCount+6500);}

        public void PaintTopInGame(ClipState state)
        {
            if(state!=ClipState.AfterClip || _font==null || !Hud.Window.IsForeground || Age(Environment.TickCount,_toastUntil)>=0)return;
            var layout=_font.GetTextLayout(_toast);
            float w=layout.Metrics.Width+24,h=layout.Metrics.Height+16;
            float x=Math.Max(8,(Hud.Window.Size.Width-w)*0.5f),y=Hud.Window.Size.Height*0.17f;
            if(_background!=null)_background.DrawRectangle(x,y,w,h);
            _font.DrawText(layout,x+12,y+8);
        }

        internal enum Phase{Idle,PendingSource,VerifyingSource,Failed}
        internal sealed class Frame
        {
            internal int Now,Players;internal uint Hero,World;
            internal bool Loaded,Foreground,MenusClear;
            internal Snapshot State;
        }
        internal sealed class Snapshot
        {
            internal object Memory;internal int Cache,Native=-1,RawTick,Pid;
            internal long GameAddress,ObjectAddress;
            internal bool Valid;

        }
        internal sealed class Proposal{internal Frame Frame;}
        internal sealed class ResetResult
        {
            internal bool Deferred,Success;
            internal static ResetResult Wait(){return new ResetResult{Deferred=true};}
        }
        internal interface IPauseBridge
        {
            bool SourceCapable{get;}
            ResetResult ResetSource(Proposal proposal,int now);
        }
        internal sealed class Recovery
        {
            private readonly IPauseBridge _bridge;
            private readonly Action<string> _notice;
            internal Phase Mode{get;private set;}internal int Incidents{get;private set;}
            private bool _detecting,_announced,_suspended;
            private int _since,_lastTick,_advances,_pendingSince,_clearSince,_clearSamples,_preBusy,_verifySince;
            private int _lastAttemptAt,_lastAttemptPid;private long _lastAttemptSource;private bool _hadAttempt;
            private uint _hero,_world;private long _sourceAddress;private object _memory;
            private Proposal _proposal;private ResetResult _result;
            internal Recovery(IPauseBridge bridge,Action<string> notice){_bridge=bridge;_notice=notice;}
            internal void Suspend(){Volatile.Write(ref _proposal,null);_detecting=false;_clearSamples=0;_suspended=true;}
            internal void Reset(){Suspend();Volatile.Write(ref _result,null);Mode=Phase.Idle;_announced=false;_clearSamples=0;}
            internal void NewGame(){Reset();_hadAttempt=false;}
            internal void BeforeCollect(int now)
            {
                var p=Volatile.Read(ref _proposal);
                if(p==null || Interlocked.CompareExchange(ref _preBusy,1,0)!=0)return;
                try {
                    if(!ReferenceEquals(Volatile.Read(ref _proposal),p))return;
                    var result=_bridge.ResetSource(p,now);
                    if(!result.Deferred){
                        _hadAttempt=true;_lastAttemptAt=now;_lastAttemptPid=p.Frame.State.Pid;_lastAttemptSource=p.Frame.State.GameAddress;
                        Volatile.Write(ref _proposal,null);Volatile.Write(ref _result,result);
                    }
                }finally{Volatile.Write(ref _preBusy,0);}
            }
            internal void Observe(Frame f)
            {
                if(!f.Loaded || f.Players<=1 || f.State==null || !f.State.Valid){Suspend();return;}
                if(_suspended){
                    if(Mode==Phase.PendingSource)_pendingSince=f.Now;
                    if(Mode==Phase.VerifyingSource)_verifySince=f.Now;
                    _suspended=false;
                }
                var s=f.State;
                // A pending write must earn fresh evidence after a world transition.
                if(Mode!=Phase.Idle && (_hero!=f.Hero || _sourceAddress!=s.GameAddress || !ReferenceEquals(_memory,s.Memory)
                    || (Mode==Phase.PendingSource && _world!=f.World)))Reset();
                if(Mode==Phase.Idle){
                    if(s.Native==0){
                        _detecting=false;return;
                    }
                    if(!f.Foreground || !f.MenusClear){
                        _detecting=false;return;
                    }
                    // FreeHUD uses its remembered RAW tick, not its extrapolated public
                    // clock. A closed-menu, loaded multiplayer signal must keep advancing.
                    if(!_detecting || _hero!=f.Hero || _world!=f.World || _sourceAddress!=s.GameAddress){
                        _detecting=true;_since=f.Now;_lastTick=s.RawTick;_advances=0;
                        _hero=f.Hero;_world=f.World;_sourceAddress=s.GameAddress;return;
                    }
                    uint delta=unchecked((uint)(s.RawTick-_lastTick));
                    if(delta>600){_detecting=false;return;}
                    if(delta>0){_advances++;_lastTick=s.RawTick;}
                    if(Age(f.Now,_since)<250 || _advances<3)return;
                    Incidents++;_memory=s.Memory;_announced=false;_clearSamples=0;
                    
                    bool cooling=_hadAttempt && _lastAttemptPid==s.Pid && _lastAttemptSource==s.GameAddress && Age(f.Now,_lastAttemptAt)<30000;
                    if(_bridge.SourceCapable && s.Native==1 && !cooling){Mode=Phase.PendingSource;_pendingSince=f.Now;}
                    else if(cooling)Failed();
                    else Failed();
                }
                var result=Interlocked.Exchange(ref _result,null);
                if(result!=null){
                    
                    if(result.Success){Mode=Phase.VerifyingSource;_clearSamples=0;_clearSince=_verifySince=f.Now;}
                    else Failed();
                }
                if(Mode==Phase.PendingSource){
                    if(Age(f.Now,_pendingSince)>=1500){Volatile.Write(ref _proposal,null);Failed();}
                    else if(f.Foreground && f.MenusClear)Volatile.Write(ref _proposal,new Proposal{Frame=f});
                    else Volatile.Write(ref _proposal,null);
                }
                if(Mode==Phase.VerifyingSource || Mode==Phase.Failed){
                    if(s.Native==0 && s.Cache==0){
                        if(_clearSamples++==0)_clearSince=f.Now;
                        if(_clearSamples>=3 && Age(f.Now,_clearSince)>=250){
                            
                            _notice("False pause cleared. Recovery #"+Incidents+".");Reset();return;
                        }
                    }else{
                        _clearSamples=0;
                        if(Mode==Phase.VerifyingSource && s.Native==1)Failed();
                        else if(Mode==Phase.VerifyingSource && Age(f.Now,_verifySince)>=1500)Failed();
                    }
                }
                if(Mode==Phase.Failed && !_announced){
                    _announced=true;
                    _notice("Pause recovery could not clear the state.\nRestart HUD from the game menu when convenient.");
                    
                }
            }
            private void Failed(){Mode=Phase.Failed;_clearSamples=0;Volatile.Write(ref _proposal,null);}
        }

        private static class Bindings
        {
            internal const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
            internal sealed class Ins {internal short Code;internal object Value;}
            private static readonly System.Collections.Generic.Dictionary<short,System.Reflection.Emit.OpCode> Ops=
                typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
                .Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode)).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
            internal static bool Is(Type a,Type b){return a!=null&&b!=null&&a.AssemblyQualifiedName==b.AssemblyQualifiedName;}
            internal static Ins[] Code(MethodBase method)
            {
                var body=method.GetMethodBody();if(body==null)return new Ins[0];var bytes=body.GetILAsByteArray();
                if(bytes.Length>65536)throw new InvalidOperationException("Unsupported binding method size");
                var result=new System.Collections.Generic.List<Ins>();int p=0;
                while(p<bytes.Length){
                    short value=bytes[p++];if(value==254)value=unchecked((short)(0xFE00|bytes[p++]));
                    System.Reflection.Emit.OpCode op;if(!Ops.TryGetValue(value,out op))throw new InvalidOperationException("Unknown IL instruction");
                    int n;object operand=null;
                    switch(op.OperandType){
                        case System.Reflection.Emit.OperandType.InlineNone:n=0;break;
                        case System.Reflection.Emit.OperandType.ShortInlineBrTarget:case System.Reflection.Emit.OperandType.ShortInlineI:
                        case System.Reflection.Emit.OperandType.ShortInlineVar:n=1;break;
                        case System.Reflection.Emit.OperandType.InlineVar:n=2;break;
                        case System.Reflection.Emit.OperandType.InlineI8:case System.Reflection.Emit.OperandType.InlineR:n=8;break;
                        case System.Reflection.Emit.OperandType.InlineSwitch:n=checked(4+4*BitConverter.ToInt32(bytes,p));break;
                        default:n=4;break;
                    }
                    if(n<0||p+n>bytes.Length)throw new InvalidOperationException("Invalid IL operand");
                    if(op.OperandType==System.Reflection.Emit.OperandType.InlineField||op.OperandType==System.Reflection.Emit.OperandType.InlineMethod){
                        try{operand=method.Module.ResolveMember(BitConverter.ToInt32(bytes,p));}catch{}
                    }
                    if(op.OperandType==System.Reflection.Emit.OperandType.InlineI)operand=BitConverter.ToInt32(bytes,p);
                    if(op.OperandType==System.Reflection.Emit.OperandType.ShortInlineI)operand=(int)(sbyte)bytes[p];
                    if(value>=0x15&&value<=0x1E)operand=value-0x16;
                    if(value!=0)result.Add(new Ins{Code=value,Value=operand});p+=n;
                }
                return result.ToArray();
            }
            internal static FieldInfo Getter(MethodBase method)
            {
                if(method==null)return null;var c=Code(method);
                if(c.Length==2&&c[0].Code==0x7E&&c[1].Code==0x2A)return c[0].Value as FieldInfo;
                if(c.Length==3&&c[0].Code==2&&c[1].Code==0x7B&&c[2].Code==0x2A)return c[1].Value as FieldInfo;
                return null;
            }
            internal static FieldInfo Setter(MethodBase method)
            {
                var c=Code(method);return c.Length==4&&c[0].Code==2&&c[1].Code==3&&c[2].Code==0x7D&&c[3].Code==0x2A?c[2].Value as FieldInfo:null;
            }
            internal static bool Same(MemberInfo a,MemberInfo b){return a!=null&&b!=null&&a.Module==b.Module&&a.MetadataToken==b.MetadataToken;}
            internal static int StaticInt(FieldInfo field)
            {
                if(!field.Module.Assembly.ReflectionOnly)return (int)field.GetValue(null);
                var c=Code(field.DeclaringType.TypeInitializer);
                var values=c.Select((i,k)=>new{i,k}).Where(v=>v.k>0&&v.i.Code==0x80&&Same(v.i.Value as MemberInfo,field)&&c[v.k-1].Value is int)
                    .Select(v=>(int)c[v.k-1].Value).Distinct().ToArray();
                if(values.Length!=1)throw new InvalidOperationException("Offset initializer is ambiguous");return values[0];
            }
            internal static bool Call(Ins i){return i.Code==0x28||i.Code==0x6F;}
            internal static MethodInfo Method(Ins i){if(!Call(i)||!(i.Value is MethodInfo))throw new InvalidOperationException("Expected binding call");return (MethodInfo)i.Value;}
            internal static FieldInfo NeedGetter(MethodBase m,Type type,bool isStatic)
            {var field=Getter(m);if(field==null||field.IsStatic!=isStatic||!Is(field.FieldType,type))throw new InvalidOperationException("Source getter did not validate");return field;}
            internal static void Pause(MethodInfo api,out FieldInfo memory,out FieldInfo pause)
            {
                var c=Code(api);int start=c.Length==6&&c[0].Code==2?1:0;
                if(c.Length-start!=5||(c[start].Code!=0x7B&&c[start].Code!=0x7E)||!Call(c[start+1])
                    ||c[start+2].Code!=0x16||c[start+3].Code!=unchecked((short)0xFE03)||c[start+4].Code!=0x2A)
                    throw new InvalidOperationException("Unrecognized IsPaused getter");
                memory=c[start].Value as FieldInfo;pause=NeedGetter(Method(c[start+1]),typeof(int),false);
                if(memory==null||pause.DeclaringType!=memory.FieldType)throw new InvalidOperationException("Pause field chain changed");
            }

        }

        private sealed class Bridge : IPauseBridge
        {
            private FieldInfo _memory,_pause,_sync,_rawTick,_tickBase,_pre,_gameAddress,_objectAddress,_pauseOffset,_tickOffset,_objectOffset,_running;
            private MethodInfo _daf,_mr,_handle,_process;
            private EventHandler _handler;
            private Func<EventHandler,EventHandler,EventHandler> _exchange;
            public bool SourceCapable{get{return _handler!=null;}}
            internal static Bridge Bind(IGameController game)
            {
                var b=new Bridge();b.Resolve(game.GetType().GetProperty("IsPaused").GetGetMethod());
                b.ValidateFreeLayout();return b;
            }
            private void Resolve(MethodInfo api)
            {
                Bindings.Pause(api,out _memory,out _pause);if(!_memory.IsStatic)throw new InvalidOperationException("Expected static FreeHUD collector");
                var root=_memory.DeclaringType;var memory=_memory.FieldType;
                _sync=root.GetFields(Bindings.All).Single(f=>f.IsStatic&&Bindings.Is(f.FieldType,typeof(ReaderWriterLockSlim)));
                var setters=memory.GetMethods(Bindings.All).Where(m=>Bindings.Same(Bindings.Setter(m),_pause)).ToArray();
                if(setters.Length!=1)throw new InvalidOperationException("Pause setter is ambiguous");
                var updates=memory.GetMethods(Bindings.All).Where(m=>Bindings.Code(m).Any(i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,setters[0]))).ToArray();
                var candidates=updates.Select(m=>new{Method=m,Code=Bindings.Code(m)}).Where(x=>x.Code.Select((i,k)=>new{i,k})
                    .Any(v=>Bindings.Call(v.i)&&Bindings.Same(v.i.Value as MemberInfo,setters[0])&&v.k>=9&&x.Code[v.k-3].Code==0x58)).ToArray();
                if(candidates.Length!=1)throw new InvalidOperationException("Native pause read is ambiguous");
                var update=candidates[0].Method;var code=candidates[0].Code;
                int at=Array.FindIndex(code,i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,setters[0]));
                if(at<9||code[at-9].Code!=2||code[at-4].Code!=0x6A||code[at-3].Code!=0x58||code[at-2].Code!=0x16)
                    throw new InvalidOperationException("Native pause read sequence changed");
                _mr=Bindings.Method(code[at-8]);_daf=Bindings.Method(code[at-7]);
                var addressGetter=Bindings.Method(code[at-6]);var offsetGetter=Bindings.Method(code[at-5]);var read=Bindings.Method(code[at-1]);
                _gameAddress=Bindings.NeedGetter(addressGetter,typeof(long),false);_pauseOffset=Bindings.NeedGetter(offsetGetter,typeof(int),true);
                var dafField=Bindings.Getter(_daf);
                if(!_mr.IsStatic||_mr.GetParameters().Length!=0||dafField==null||!dafField.IsStatic||dafField.FieldType!=addressGetter.DeclaringType
                    ||!Bindings.Is(read.ReturnType,typeof(int))||!read.GetParameters().Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(new[]{"System.Int64","System.Boolean"}))
                    throw new InvalidOperationException("Native pause reader linkage changed");
                if(code.Length<9||!Bindings.Same(code[0].Value as MemberInfo,_mr)||!Bindings.Same(code[1].Value as MemberInfo,_daf))
                    throw new InvalidOperationException("Native loaded-state read changed");
                _objectAddress=Bindings.NeedGetter(Bindings.Method(code[2]),typeof(long),false);
                _objectOffset=Bindings.NeedGetter(Bindings.Method(code[3]),typeof(int),true);
                if(_objectAddress.DeclaringType!=_gameAddress.DeclaringType)
                    throw new InvalidOperationException("Native loaded-state owner changed");
                var pauseGetters=memory.GetMethods(Bindings.All).Where(m=>Bindings.Same(Bindings.Getter(m),_pause)).ToArray();
                if(pauseGetters.Length!=1)throw new InvalidOperationException("Pause getter is ambiguous");
                int p=Array.FindIndex(code,i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,pauseGetters[0]));
                if(p<11||code[p-1].Code!=2||code[p-4].Code!=0x16||code[p-5].Code!=0x58||code[p-6].Code!=0x6A
                    ||code[p+2].Code!=2||code[p+3].Code!=0x7B)throw new InvalidOperationException("Raw tick read sequence changed");
                _tickBase=Bindings.NeedGetter(Bindings.Method(code[p-8]),typeof(long),false);
                _tickOffset=Bindings.NeedGetter(Bindings.Method(code[p-7]),typeof(int),true);_rawTick=code[p+3].Value as FieldInfo;
                if(_rawTick==null||_rawTick.IsStatic||!Bindings.Is(_rawTick.FieldType,typeof(int))||_rawTick.DeclaringType!=memory)
                    throw new InvalidOperationException("Raw tick cache changed");
                _handle=Bindings.Code(read).Where(Bindings.Call).Select(Bindings.Method)
                    .Single(m=>!m.IsStatic&&m.GetParameters().Length==0&&Bindings.Is(m.ReturnType,typeof(IntPtr))&&Bindings.Getter(m)!=null);
                _process=_mr.ReturnType.GetMethods(Bindings.All).Single(m=>!m.IsStatic&&m.GetParameters().Length==0&&Bindings.Is(m.ReturnType,typeof(System.Diagnostics.Process))&&Bindings.Getter(m)!=null);
                var inner=root.GetMethods(Bindings.All).Single(m=>Bindings.Code(m).Any(i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,update)));
                var inside=Bindings.Code(inner);
                int lockAt=Array.FindIndex(inside,i=>Bindings.Call(i)&&((MethodBase)i.Value).Name=="TryEnterWriteLock"
                    &&Bindings.Is(((MethodBase)i.Value).DeclaringType,typeof(ReaderWriterLockSlim)));
                if(lockAt<0)throw new InvalidOperationException("Collector write lock changed");
                var running=inside.Select((i,k)=>new{i,k}).Where(v=>v.k>0&&v.i.Code==0x80&&inside[v.k-1].Code==0x17)
                    .Select(v=>v.i.Value as FieldInfo).Where(f=>f!=null&&f.IsStatic&&Bindings.Is(f.FieldType,typeof(bool)))
                    .Where(f=>inside.Take(lockAt).Any(i=>i.Code==0x7E&&Bindings.Same(i.Value as MemberInfo,f)))
                    .Where(f=>inside.Select((i,k)=>new{i,k}).Any(v=>v.k>0&&v.i.Code==0x80&&Bindings.Same(v.i.Value as MemberInfo,f)&&inside[v.k-1].Code==0x16)).Distinct().ToArray();
                if(running.Length!=1)throw new InvalidOperationException("Collector running guard is ambiguous");_running=running[0];
                var wrapper=root.GetMethods(Bindings.All).Single(m=>Bindings.Code(m).Any(i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,inner)));
                var outer=Bindings.Code(wrapper);int stop=Array.FindIndex(outer,i=>Bindings.Call(i)&&Bindings.Same(i.Value as MemberInfo,inner));
                var pre=outer.Take(stop).Where(Bindings.Call).Select(i=>Bindings.Getter(i.Value as MethodBase))
                    .Where(f=>f!=null&&f.IsStatic&&Bindings.Is(f.FieldType,typeof(EventHandler))).Distinct().ToArray();
                if(pre.Length!=1)throw new InvalidOperationException("Pre-collection event is ambiguous");_pre=pre[0];
            }
            private static bool OffsetOK(FieldInfo field)
            {int offset=Bindings.StaticInt(field);return offset>=0x20&&offset<=0x1000&&(offset&3)==0;}
            private void ValidateFreeLayout()
            {
                if(!_memory.IsStatic||!_sync.IsStatic||!_pre.IsStatic||!Bindings.Is(_pre.FieldType,typeof(EventHandler))
                    ||!OffsetOK(_pauseOffset)||!OffsetOK(_tickOffset)||!OffsetOK(_objectOffset))
                    throw new InvalidOperationException("Traced FreeHUD source layout did not validate");
            }
            internal bool Hook(EventHandler handler)
            {
                try{
                    var dynamic=new DynamicMethod("s7oPausePreCollectCompareExchange",typeof(EventHandler),new[]{typeof(EventHandler),typeof(EventHandler)},_pre.Module,true);
                    var il=dynamic.GetILGenerator();il.Emit(OpCodes.Ldsflda,_pre);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);
                    var compare=typeof(Interlocked).GetMethods().Single(x=>x.Name=="CompareExchange"&&x.IsGenericMethodDefinition).MakeGenericMethod(typeof(EventHandler));
                    il.Emit(OpCodes.Call,compare);il.Emit(OpCodes.Ret);
                    _exchange=(Func<EventHandler,EventHandler,EventHandler>)dynamic.CreateDelegate(typeof(Func<EventHandler,EventHandler,EventHandler>));
                    for(int i=0;i<8;i++){
                        var old=(EventHandler)_pre.GetValue(null);var next=(EventHandler)Delegate.Combine(old,handler);
                        if(ReferenceEquals(_exchange(next,old),old)){_handler=handler;return true;}
                    }
                }catch{}
                return false;
            }
            internal void Unhook()
            {
                if(_handler==null || _exchange==null)return;
                for(int i=0;i<8;i++){
                    var old=(EventHandler)_pre.GetValue(null);var next=(EventHandler)Delegate.Remove(old,_handler);
                    if(ReferenceEquals(_exchange(next,old),old)){_handler=null;return;}
                }
            }
            private object Memory(){return _memory.GetValue(null);}
            private ReaderWriterLockSlim Sync(object memory){return (ReaderWriterLockSlim)_sync.GetValue(null);}
            internal Snapshot Read()
            {
                var memory=Memory();
                var s=new Snapshot{Memory=memory};
                if(memory==null)return s;
                s.Cache=(int)_pause.GetValue(memory);
                {
                    var daf=_daf.Invoke(null,null);if(daf==null)return s;
                    s.GameAddress=(long)_gameAddress.GetValue(daf);s.ObjectAddress=(long)_objectAddress.GetValue(daf);
                    s.RawTick=(int)_rawTick.GetValue(memory);
                    if(s.GameAddress==0 || s.ObjectAddress==0)return s;
                    var reader=_mr.Invoke(null,null);if(reader==null)return s;
                    s.Native=ReadInt((IntPtr)_handle.Invoke(reader,null),checked(s.GameAddress+Bindings.StaticInt(_pauseOffset)));
                    s.Pid=((System.Diagnostics.Process)_process.Invoke(reader,null)).Id;
                }
                s.Valid=(s.Cache==0 || s.Cache==1) && (s.Native==0 || s.Native==1);
                return s;
            }

            public ResetResult ResetSource(Proposal proposal,int now)
            {
                if(!SourceCapable)return new ResetResult();
                var f=proposal.Frame;var expected=f.State;
                if(!f.Loaded || f.Players<=1 || !f.Foreground || !f.MenusClear || Age(now,f.Now)<0 || Age(now,f.Now)>500)
                    return ResetResult.Wait();
                var memory=Memory();if(memory==null)return ResetResult.Wait();
                var sync=Sync(memory);if(sync==null || sync.IsReadLockHeld || sync.IsUpgradeableReadLockHeld)return ResetResult.Wait();
                bool entered=false;IntPtr writable=IntPtr.Zero;
                try{
                    if(!sync.IsWriteLockHeld){entered=sync.TryEnterWriteLock(0);if(!entered)return ResetResult.Wait();}
                    if((bool)_running.GetValue(null))return ResetResult.Wait();
                    var current=Read();
                    if(!current.Valid || !ReferenceEquals(current.Memory,expected.Memory) || current.GameAddress!=expected.GameAddress
                        || current.ObjectAddress!=expected.ObjectAddress || current.Pid!=expected.Pid)return ResetResult.Wait();
                    var reader=_mr.Invoke(null,null);var handle=(IntPtr)_handle.Invoke(reader,null);
                    if(GetProcessId(handle)!=(uint)expected.Pid || ReadInt(handle,checked(current.ObjectAddress+Bindings.StaticInt(_objectOffset)))!=1)
                        return ResetResult.Wait();
                    long tickBase=(long)_tickBase.GetValue(memory);if(tickBase==0)return ResetResult.Wait();
                    int tick=ReadInt(handle,checked(tickBase+(int)_tickOffset.GetValue(null)));
                    if(unchecked((uint)(tick-expected.RawTick))>60)return ResetResult.Wait();
                    long address=checked(current.GameAddress+Bindings.StaticInt(_pauseOffset));int before=ReadInt(handle,address);
                    if(before==0)return new ResetResult{Success=true};
                    if(before!=1)return new ResetResult();
                    writable=OpenProcess(0x438,false,expected.Pid);
                    if(writable==IntPtr.Zero)return new ResetResult();
                    if(GetProcessId(writable)!=(uint)expected.Pid || ReadInt(writable,address)!=1)
                        return new ResetResult();
                    int zero=0;IntPtr count;
                    bool wrote=WriteProcessMemory(writable,new IntPtr(address),ref zero,4,out count);
                    int after=ReadInt(handle,address);
                    return new ResetResult{Success=wrote&&count.ToInt64()==4&&after==0};
                }catch{return new ResetResult();}
                finally{if(writable!=IntPtr.Zero)CloseHandle(writable);if(entered)sync.ExitWriteLock();}
            }
            private static int ReadInt(IntPtr process,long address)
            {
                int value;IntPtr count;
                if(process==IntPtr.Zero || address<=0 || !ReadProcessMemory(process,new IntPtr(address),out value,4,out count) || count.ToInt64()!=4)
                    throw new InvalidOperationException("Native read failed: "+Marshal.GetLastWin32Error());
                return value;
            }
            [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
            [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,out int value,int size,out IntPtr read);
            [DllImport("kernel32.dll",SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process,IntPtr address,ref int value,int size,out IntPtr written);
            [DllImport("kernel32.dll")] private static extern uint GetProcessId(IntPtr handle);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
