// FreeHUD standalone map overlay. Install ONE variant as s7o_Minimap.cs.
// The physical key left of 1 toggles the expanded view. +/- zooms that view.
// Purple seams mark walkable borders beyond which terrain data is unavailable.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SharpDX;
using SharpDX.Direct2D1;
using Turbo.Plugins;
using DIKey = SharpDX.DirectInput.Key;
using Keys = System.Windows.Forms.Keys;
using RectangleF = System.Drawing.RectangleF;
using Point = System.Drawing.Point;


namespace Turbo.Plugins.s7o
{
    public class s7o_Minimap : IPlugin, IAfterCollectHandler, IKeyEventHandler, IInGameTopPainter, IInGameWorldPainter, INewAreaHandler
    {
        // HOTKEY: physical key below Esc, immediately LEFT of 1. Its legend may be
        // ~, `, | or another symbol. No Shift is needed; HUD language is irrelevant.
        // Change DIKey.Grave to DIKey.A ... DIKey.Z, DIKey.F1 ... DIKey.F12,
        // DIKey.D0 ... DIKey.D9 (top row), or DIKey.NumberPad0 ... NumberPad9.
        // Choose a key that does not conflict with your game/plugin bindings.
        public DIKey ExpandKey = DIKey.Grave;
        public bool MapEnabled = true;
        public float CenteredOpacityPercent = 100f; // Overall expanded layer, 0..100 percent.
        public Keys ZoomOutKey = Keys.OemMinus, ZoomInKey = Keys.Oemplus;
        public Keys MoveLeftKey = Keys.Left, MoveRightKey = Keys.Right;
        public Keys MoveUpKey = Keys.Up, MoveDownKey = Keys.Down;
        // Expanded map offsets are fractions of the game window height.
        // The default moves the player marker 300 pixels left at 1080p.
        public float OverlayOffsetX = -300f / 1080f, OverlayOffsetY = 0f;

        // APPEARANCE: RGB values each range 0..255. Examples:
        // cyan=80,185,220; blue=70,130,255; green=90,210,120;
        // yellow=235,205,80; purple=190,110,230; white=220,220,220.
        // Opacity is 0..100 PERCENT: lower = MORE transparent. 0 hides the effect.
        // These are defaults. Valid entries in settings/s7o_Minimap.ini override
        // them on reload. Arrow keys save the expanded map position to that file.
        public bool ShowOutlines = true;
        public int OutlineRed = 80, OutlineGreen = 185, OutlineBlue = 220;
        public float OutlineOpacityPercent = 75f;
        public float OutlineThicknessPixels = 1.2f; // Try 1f thin, 1.5f medium, 2f thicker.
        public int ExploredOutlineRed = 230, ExploredOutlineGreen = 235, ExploredOutlineBlue = 240;
        public float ExploredOutlineOpacityPercent = 85f;
        public bool ShowOutlineRim = true;
        public float OutlineRimOpacityPercent = 65f;
        public float OutlineRimWidthPixels = .9f; // Width of the dark edge on each side.
        public bool ShowUnexploredFill = true;
        public int FillRed = 80, FillGreen = 185, FillBlue = 220;
        public float FillOpacityPercent = 8f; // 4=subtle, 8=default, 15=stronger.
        // Known walkable layouts start with the unexplored tint. Native reveal
        // masks switch confirmed explored cells to the independent gray tint.
        public bool ShowExploredFill = true;
        public int ExploredFillRed = 42, ExploredFillGreen = 46, ExploredFillBlue = 50;
        public float ExploredFillOpacityPercent = 6f;
        public float TrashDotRadiusPixels = 1.75f;
        public bool HighlightNormalMinimap = true;
        public bool ScaleMarkersWithZoom = true; // Half the terrain zoom exponent.
        public IController Hud { get; private set; }
        public int Order { get; set; } = -1000; // Map outlines precede ordinary monster/door decorators.
        public Dictionary<string, IPerfCounter> PerformanceCounters { get; private set; } = new Dictionary<string, IPerfCounter>();
        private bool _enabled = true, _center, _toggleHeld, _outHeld, _inHeld;
        private int _exitCueStarted=int.MinValue;
        public bool Enabled { get { return _enabled; } set { lock(_model){if(_enabled!=value){_positionDirty=true;}_enabled=MapEnabled=value;SavePosition(); if(!value){ClearGraphics();} } } }
        private float _zoom;
        private NativeMapSource _source;
        private readonly MapModel _model = new MapModel();
        private readonly Dictionary<string, CachedPaths> _graphics = new Dictionary<string, CachedPaths>();
        private readonly List<IUiElement> _panels = new List<IUiElement>();
        private IPlugin _menu, _standard, _portalOwner, _markerOwner, _cursorOwner;
        private readonly List<IPlugin> _mapPainters=new List<IPlugin>();
        private Action<IMonster> _bossPainter,_keywardenPainter;
        private bool _paintingMarkers,_ownersBound;
        private float _replayNativeScale;
        private PropertyInfo _menuVisible, _menuBusy;
        private IBrush[] _walls;
        private IBrush _rim;
        private IBrush[] _fillBrushes;
        private SolidColorBrush _connectorEntranceFill,_connectorExitFill;
        private readonly Dictionary<Tile,PathGeometry> _connectorFillGraphics=new Dictionary<Tile,PathGeometry>();
        private readonly Dictionary<Point,CachedPaths> _fillGraphics=new Dictionary<Point,CachedPaths>();
        private readonly List<CachedPaths> _visiblePaths = new List<CachedPaths>();
        private IBrush _trash, _player, _entrance, _exit;
        private readonly Dictionary<ulong,ITexture> _markerTextures=new Dictionary<ulong,ITexture>();
        private PathGeometry _expandedClip;
        private Layer _expandedLayer;
        private RectangleF _clipScreen,_clipCorner;
        private IntPtr _clipTarget,_graphicsTarget;
        private IFont _font,_portalLabel;
        private string _notice = "";
        private int _noticeAt;
        private uint _world, _graphicsWorld;
        private string _areaGroup="";
        private int _lastLayout,_lastFog,_lastPortals,_lastBind,_lastMenuProbe;
        private string _settingsPath;
        private bool _positionDirty;
        private int _moveX, _moveY, _lastMove, _saveAfter;
        

        public void Load(IController hud)
        {
            Hud=hud;
            
            LoadSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plugins","s7o","settings","s7o_Minimap.ini"));
            _enabled = MapEnabled;
            _model.GenerateUnexploredFill=ShowUnexploredFill&&Alpha(FillOpacityPercent)>0;
            _model.GenerateExploredFill=ShowExploredFill&&Alpha(ExploredFillOpacityPercent)>0;
            _walls = new[] { hud.Render.CreateBrush(Alpha(ExploredOutlineOpacityPercent),Rgb(ExploredOutlineRed),Rgb(ExploredOutlineGreen),Rgb(ExploredOutlineBlue),1.2f),
                hud.Render.CreateBrush(Alpha(OutlineOpacityPercent),Rgb(OutlineRed),Rgb(OutlineGreen),Rgb(OutlineBlue),1.2f),
                hud.Render.CreateBrush(Alpha(OutlineOpacityPercent*.76f),Rgb(OutlineRed),Rgb(OutlineGreen),Rgb(OutlineBlue),1.1f),
                hud.Render.CreateBrush(235,200,110,255,1.2f,DashStyle.Dash) };
            _rim=hud.Render.CreateBrush(Alpha(OutlineRimOpacityPercent),0,0,0,1);
            _fillBrushes=new[] {
                hud.Render.CreateBrush(Alpha(ExploredFillOpacityPercent),Rgb(ExploredFillRed),Rgb(ExploredFillGreen),Rgb(ExploredFillBlue),0),
                hud.Render.CreateBrush(Alpha(FillOpacityPercent),Rgb(FillRed),Rgb(FillGreen),Rgb(FillBlue),0) };
            _trash=hud.Render.CreateBrush(145,210,215,220,0);
            _player=hud.Render.CreateBrush(255,255,245,180,0);
            _entrance=hud.Render.CreateBrush(230,80,230,125,1.4f);
            _exit=hud.Render.CreateBrush(230,245,80,65,1.4f);
            _font=hud.Render.CreateFont("tahoma",9f,255,210,235,255,true,false,true);
            _portalLabel=hud.Render.CreateFont("tahoma",6f,200,255,255,0,false,false,128,0,0,0,true);
            foreach(var path in new[] { "Root.NormalLayer.chatentry_dialog_backgroundScreen.chatentry_content.chat_editline",
                "Root.NormalLayer.SkillPane_main.LayoutRoot.SkillsList", "Root.NormalLayer.vendor_dialog_mainPage",
                "Root.NormalLayer.shop_dialog_mainPage.panel", "Root.NormalLayer.conversation_dialog_main",
                "Root.TopLayer.scripted_sequence", "Root.NormalLayer.gamemenu_dialog",
                "Root.NormalLayer.profile_main", "Root.NormalLayer.leaderboards_dialog" })
            { try { var ui=hud.Render.RegisterUiElement(path,null,null); if(ui!=null)_panels.Add(ui); } catch {} }
            AppDomain.CurrentDomain.DomainUnload += (s,e)=>{lock(_model){ SavePosition(); ClearGraphics();  }};
        }
        private void BindOwners()
        {
            // Load runs before the native plugin registry has been fully populated.
            foreach(var p in Hud.AllPlugins)
            {
                if(p.GetType().Name=="s7o_HUD_MENU") { _menu=p;_menuVisible=p.GetType().GetProperty("HoverTipsMenuVisible");_menuBusy=p.GetType().GetProperty("HoverTipsBlocked"); }
                if(p.GetType().Name=="StandardMonsterPlugin")
                {
                    _standard=p;
                    _bossPainter=BindDefaultMarker(p,"BossDecorator");
                    _keywardenPainter=BindDefaultMarker(p,"KeywardenDecorator");
                }
                switch(p.GetType().FullName)
                {
                    case "Turbo.Plugins.Default.PortalPlugin": _portalOwner=p;break;
                    case "Turbo.Plugins.Default.MarkerPlugin": _markerOwner=p;break;
                    case "Turbo.Plugins.s7o.MapCursor": _cursorOwner=p;break;
                    case "Turbo.Plugins.s7o.s7o_DoorsMinimap":
                    case "Turbo.Plugins.s7o.s7o_EliteHealthBars":
                        if(p is IInGameWorldPainter)_mapPainters.Add(p);
                        break;
                }
                if(_model.Fallback==null && p.GetType().FullName=="Turbo.Plugins.s7o.s7o_MapViewer"
                    && p is IEnumerable<KeyValuePair<string,Func<string,object[],object>>> bridge)
                    foreach(var entry in bridge)if(entry.Key=="s7o.MapViewer.v1"&&entry.Value!=null)
                    {_model.Fallback=new MapGeometryFallback(p,entry.Value);break;}
            }
            if(_markerOwner is IInGameWorldPainter)_mapPainters.Add(_markerOwner);
            _mapPainters.Sort((a,b)=>a.Order.CompareTo(b.Order));
            _ownersBound=true;
            
        }
        private bool Ready { get { return Enabled && Hud!=null && Hud.Game.IsInGame && !Hud.Game.IsLoading && Hud.Game.Me!=null; } }
        private static Action<IMonster> BindDefaultMarker(IPlugin plugin,string name)
        {
            var property=plugin.GetType().GetProperty(name);
            if(property==null)return null;
            var paint=property.PropertyType.GetMethod("Paint",new[]{typeof(WorldLayer),typeof(IActor),typeof(IWorldCoordinate),typeof(string)});
            if(paint==null)return null;
            var monster=System.Linq.Expressions.Expression.Parameter(typeof(IMonster),"monster");
            var collection=System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(plugin),property);
            var coordinate=System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(monster,typeof(IActor)),"FloorCoordinate");
            var sno=System.Linq.Expressions.Expression.Property(monster,"SnoMonster");
            var label=System.Linq.Expressions.Expression.Property(sno,"NameLocalized");
            return System.Linq.Expressions.Expression.Lambda<Action<IMonster>>(
                System.Linq.Expressions.Expression.IfThen(System.Linq.Expressions.Expression.NotEqual(collection,
                    System.Linq.Expressions.Expression.Constant(null,property.PropertyType)),
                    System.Linq.Expressions.Expression.Call(collection,paint,System.Linq.Expressions.Expression.Constant(WorldLayer.Map),
                        System.Linq.Expressions.Expression.Convert(monster,typeof(IActor)),coordinate,label)),monster).Compile();
        }
        private static int Rgb(int value){return Math.Max(0,Math.Min(255,value));}
        private static int Alpha(float percent){return float.IsNaN(percent)?0:(int)Math.Round(Math.Max(0f,Math.Min(100f,percent))*2.55f);}
        private static bool Visible(IUiElement e) { return e!=null && e.Visible; }
        private static string SettingValue(object value)
        {
            return value is float ? ((float)value).ToString("R",CultureInfo.InvariantCulture) : value.ToString();
        }
        private void LoadSettings(string path)
        {
            _settingsPath=path;
            var fields=typeof(s7o_Minimap).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                .ToDictionary(field=>field.Name,StringComparer.OrdinalIgnoreCase);
            try
            {
                if(!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var file=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write)))
                    {
                        file.WriteLine("; s7o Minimap. Reload HUD after editing. Offsets are fractions of window height.");
                        file.WriteLine("; Remove a setting to use its .cs default. Key names are documented in the plugin.");
                        foreach(var field in fields.Values)file.WriteLine(field.Name+"="+SettingValue(field.GetValue(this)));
                    }
                    return;
                }
                foreach(var line in File.ReadLines(path))
                {
                    int equals=line.IndexOf('=');
                    FieldInfo field;
                    if(equals<1||!fields.TryGetValue(line.Substring(0,equals).Trim(),out field))continue;
                    string value=line.Substring(equals+1).Trim();
                    int comment=value.IndexOfAny(new[]{';','#'});
                    if(comment>=0)value=value.Substring(0,comment).Trim();
                    bool flag; int integer; float number;
                    if(field.FieldType==typeof(bool)&&bool.TryParse(value,out flag))field.SetValue(this,flag);
                    if(field.FieldType==typeof(int)&&int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out integer))field.SetValue(this,integer);
                    if(field.FieldType==typeof(float)&&float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&MapModel.Finite(number))field.SetValue(this,number);
                    if(field.FieldType.IsEnum&&Enum.GetNames(field.FieldType).Contains(value,StringComparer.OrdinalIgnoreCase))
                        field.SetValue(this,Enum.Parse(field.FieldType,value,true));
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        // HUD Menu invokes this optional entry point. Geometry stays cached on color changes.
        public void ConfigureMenu(bool enabled, DIKey hotkey, System.Drawing.Color color, float opacityPercent)
        {
            if (!Enum.IsDefined(typeof(DIKey), hotkey) || hotkey == DIKey.Unknown
                || !MapModel.Finite(opacityPercent)) return;
            lock (_model)
            {
                bool colorChanged = OutlineRed != color.R || OutlineGreen != color.G || OutlineBlue != color.B
                    || FillRed != color.R || FillGreen != color.G || FillBlue != color.B;
                ExpandKey = hotkey;
                OutlineRed = FillRed = color.R;
                OutlineGreen = FillGreen = color.G;
                OutlineBlue = FillBlue = color.B;
                CenteredOpacityPercent = Math.Max(0f, Math.Min(100f, opacityPercent));
                if (colorChanged && Hud != null && _walls != null)
                {
                    _walls[1] = Hud.Render.CreateBrush(Alpha(OutlineOpacityPercent), color.R, color.G, color.B, 1.2f);
                    _walls[2] = Hud.Render.CreateBrush(Alpha(OutlineOpacityPercent * .76f), color.R, color.G, color.B, 1.1f);
                    _fillBrushes[1] = Hud.Render.CreateBrush(Alpha(FillOpacityPercent), color.R, color.G, color.B, 0);
                }
                _positionDirty = true;
                Enabled = enabled;
            }
        }

        private void SavePosition()
        {
            if(!_positionDirty||_settingsPath==null)return;
            string temp=_settingsPath+".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                var lines=File.Exists(_settingsPath)?File.ReadAllLines(_settingsPath).ToList():new List<string>();
                foreach(var name in new[]{nameof(OverlayOffsetX),nameof(OverlayOffsetY),nameof(MapEnabled),nameof(ExpandKey),
                    nameof(OutlineRed),nameof(OutlineGreen),nameof(OutlineBlue),nameof(FillRed),nameof(FillGreen),nameof(FillBlue),
                    nameof(CenteredOpacityPercent)})
                {
                    string value=name+"="+SettingValue(typeof(s7o_Minimap).GetField(name).GetValue(this));
                    int index=lines.FindLastIndex(line=>{
                        int equals=line.IndexOf('=');
                        return equals>0&&line.Substring(0,equals).Trim().Equals(name,StringComparison.OrdinalIgnoreCase);
                    });
                    if(index<0)lines.Add(value);else lines[index]=value;
                }
                File.WriteAllLines(temp,lines);
                if(File.Exists(_settingsPath))File.Replace(temp,_settingsPath,null);else File.Move(temp,_settingsPath);
                _positionDirty=false;
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        private static RectangleF OverlayViewport(Size size)
        {
            return RectangleF.FromLTRB(size.Height*.011f,size.Height*.032f,size.Width-size.Height*.011f,size.Height*.875f);
        }
        private Vector2 OverlayAnchor(Size size)
        {
            var view=OverlayViewport(size);
            float margin=Math.Min(size.Height*.03f,Math.Min(view.Width,view.Height)*.5f);
            float x=size.Width*.5f+OverlayOffsetX*size.Height,y=size.Height*.5f+OverlayOffsetY*size.Height;
            return new Vector2(Math.Max(view.Left+margin,Math.Min(view.Right-margin,x)),
                Math.Max(view.Top+margin,Math.Min(view.Bottom-margin,y)));
        }
        private void AdjustPosition(int now)
        {
            int x=0,y=0;
            if(_center&&!Blocked()&&!Hud.Input.IsKeyDown(Keys.ControlKey)&&!Hud.Input.IsKeyDown(Keys.Menu))
            {
                x=(Hud.Input.IsKeyDown(MoveRightKey)?1:0)-(Hud.Input.IsKeyDown(MoveLeftKey)?1:0);
                y=(Hud.Input.IsKeyDown(MoveDownKey)?1:0)-(Hud.Input.IsKeyDown(MoveUpKey)?1:0);
            }
            bool changed=x!=_moveX||y!=_moveY;
            _moveX=x;_moveY=y;
            if((x!=0||y!=0)&&(changed||unchecked((uint)(now-_lastMove))>=75))
            {
                var size=Hud.Window.Size;
                if(size.Width<=0||size.Height<=0)return;
                _lastMove=now;
                var anchor=OverlayAnchor(size);
                OverlayOffsetX=(anchor.X-size.Width*.5f)/size.Height+x*.0125f;
                OverlayOffsetY=(anchor.Y-size.Height*.5f)/size.Height+y*.0125f;
                anchor=OverlayAnchor(size);
                OverlayOffsetX=(anchor.X-size.Width*.5f)/size.Height;
                OverlayOffsetY=(anchor.Y-size.Height*.5f)/size.Height;
                _positionDirty=true;_saveAfter=unchecked(now+500);
            }
            if(_positionDirty&&unchecked(now-_saveAfter)>=0)
            {
                SavePosition();
                _saveAfter=unchecked(now+5000);
            }
        }
        private bool Blocked()
        {
            if(!Ready || !Hud.Window.IsForeground || Hud.Render.UiHidden || !Hud.Render.IsRenderEnabled || Hud.Game.MapMode!=MapMode.Minimap)return true;
            if(_panels.Any(Visible))return true;
            if(_menu==null && unchecked((uint)(Environment.TickCount-_lastMenuProbe))>5000u)
            {
                _lastMenuProbe=Environment.TickCount;
                _menu=Hud.AllPlugins.FirstOrDefault(p=>p.GetType().Name=="s7o_HUD_MENU");
                if(_menu!=null){_menuVisible=_menu.GetType().GetProperty("HoverTipsMenuVisible");_menuBusy=_menu.GetType().GetProperty("HoverTipsBlocked");}
            }
            if(Hud.Inventory!=null && (Visible(Hud.Inventory.InventoryMainUiElement)||Visible(Hud.Inventory.StashMainUiElement)||Visible(Hud.Inventory.FollowerMainUiElement)))return true;
            try { if(_menu!=null && _menu.Enabled && ((_menuVisible!=null&&(bool)_menuVisible.GetValue(_menu,null))||(_menuBusy!=null&&(bool)_menuBusy.GetValue(_menu,null))))return true; }
            catch { return true; }
            return false;
        }
        public void OnKeyEvent(IKeyEvent e){lock(_model)HandleKey(e);}
        private void HandleKey(IKeyEvent e)
        {
            if(e.Key!=ExpandKey)return;
            if(!e.IsPressed){_toggleHeld=false;return;}
            if(_toggleHeld)return; _toggleHeld=true;
            if(e.ControlPressed||e.AltPressed||Blocked())return;
            _center=!_center;
            if(!_center)SavePosition();
            
        }
        public void AfterCollect()
        {
            lock(_model)
            {
                
                Collect();
                
                
            }
        }
        private void Collect()
        {
            if(Hud==null||!Enabled)return;
            int now=Environment.TickCount;
            AdjustPosition(now);
            if(!Hud.Window.IsForeground)_toggleHeld=false;
            bool minus=Hud.Window.IsForeground&&(Hud.Input.IsKeyDown(ZoomOutKey)||Hud.Input.IsKeyDown(Keys.Subtract));
            bool plus=Hud.Window.IsForeground&&(Hud.Input.IsKeyDown(ZoomInKey)||Hud.Input.IsKeyDown(Keys.Add));
            bool edgeOut=minus&&!_outHeld, edgeIn=plus&&!_inHeld;_outHeld=minus;_inHeld=plus;
            if((edgeOut||edgeIn)&&minus!=plus&&!Blocked()&&!Hud.Input.IsKeyDown(Keys.ControlKey)&&!Hud.Input.IsKeyDown(Keys.Menu))
            {
                float next=Math.Max(-2f,Math.Min(2f,_zoom+(edgeOut?-.25f:.25f)));
                if(next!=_zoom)
                {
                    _zoom=next;
                    Notice("Overlay zoom "+_zoom.ToString("0.00",CultureInfo.InvariantCulture));
                    
                }
            }
            if(!Ready){if(!Hud.Game.IsInGame)ResetWorld();return;}
            if(!_ownersBound)
            {
                try {BindOwners();}
                catch(Exception){_ownersBound=true;}
            }
            uint world=Hud.Game.Me.WorldId;
            string group=Hud.Game.Me.SnoArea==null?"":Hud.Game.Me.SnoArea.AreaGroupInWorld;
            if(_world!=world||_areaGroup!=group){ResetWorld(_world==world);_world=world;_areaGroup=group;}
            
            if(Due(ref _lastPortals,now,250))
            {
                var me=Hud.Game.Me;
                bool rift=Hud.Game.SpecialArea==SpecialArea.GreaterRift||Hud.Game.SpecialArea==SpecialArea.Rift;
                int level=rift?FloorLinks.RiftLevel(me.WorldSno,me.SnoArea?.Code):0;
                _model.Links.Observe(Hud.Game.Portals,world,level);
                
            }
            if(_source==null && unchecked((uint)(now-_lastBind))>=5000u)
            {
                _lastBind=now;
                try {_source=NativeMapSource.Bind(Hud.Render); }
                catch(Exception){}
            }
            if(_source!=null)
            {
                bool mapVisible=(_center||HighlightNormalMinimap)&&Hud.Window.IsForeground&&Hud.Game.MapMode==MapMode.Minimap
                    &&Hud.Render.IsRenderEnabled&&!Hud.Render.UiHidden;
                bool layout=Due(ref _lastLayout,now,500),fog=Due(ref _lastFog,now,mapVisible?125:500);
                if(layout||fog)
                {
                    try {_model.Update(_source,Hud.Game.Me,now,layout,fog);}
                    catch(Exception){}
                }
            }
        }
        private static bool Due(ref int last,int now,int interval)
        {
            if(last!=0&&unchecked((uint)(now-last))<(uint)interval)return false;
            last=now;return true;
        }
        public void OnNewArea(bool newGame, ISnoArea area){lock(_model){if(newGame)ResetWorld();}}
        private void ResetWorld(bool preserveLinks=false)
        {
            
            _world=0;_areaGroup="";_lastLayout=_lastFog=_lastPortals=0;_graphicsWorld=uint.MaxValue;
            _model.Clear();
            if(!preserveLinks){_model.Links.Clear();_exitCueStarted=int.MinValue;}
            
        }
        private void Notice(string value){_notice=value;_noticeAt=Environment.TickCount;}

        public void PaintTopInGame(ClipState state){lock(_model)Paint(state);}
        public void PaintWorld(WorldLayer layer)
        {
            if(layer!=WorldLayer.Map||!HighlightNormalMinimap)return;
            lock(_model)
            {
                if(_paintingMarkers||Blocked()||_source==null)return;
                var target=_source.RenderTarget;
                if(target==null)return;
                var me=Hud.Game.Me.FloorCoordinate;float ax,ay;
                Hud.Render.GetMinimapCoordinates(me.X,me.Y,out ax,out ay);
                float scale=Hud.Render.MinimapScale;
                if(!MapModel.Finite(scale)||scale<=0)return;
                var clip=Hud.Render.MinimapUiElement.Rectangle;
                var view=new MapView(me.X,me.Y,ax,ay,scale);
                
                PrepareGraphics(target);
                var old=target.Transform;bool pushed=false;
                try
                {
                    target.Transform=Matrix3x2.Identity;
                    target.PushAxisAlignedClip(new SharpDX.Mathematics.Interop.RawRectangleF(clip.Left,clip.Top,clip.Right,clip.Bottom),AntialiasMode.Aliased);
                    pushed=true;
                    target.Transform=view.Matrix;
                    DrawTerrain(view,clip);
                }
                catch(Exception){}
                finally {target.Transform=Matrix3x2.Identity;if(pushed)target.PopAxisAlignedClip();target.Transform=old;}
                
            }
        }
        private void Paint(ClipState state)
        {
            if(state!=ClipState.AfterClip || Blocked())return;
            var nativeRect=Hud.Render.MinimapUiElement.Rectangle;
            if(_standard==null||!_standard.Enabled)DrawMobs(null,nativeRect,RectangleF.Empty,1f);
            if(_center && _source!=null)
            {
                var size=Hud.Window.Size;
                var anchor=OverlayAnchor(size);
                var view=new MapView(Hud.Game.Me.FloorCoordinate.X,Hud.Game.Me.FloorCoordinate.Y,
                    anchor.X,anchor.Y,size.Height/1200f*ZoomFactor);
                DrawExpanded(view,OverlayViewport(size),nativeRect);
            }
            if(_notice.Length>0 && unchecked((uint)(Environment.TickCount-_noticeAt))<1500u)
                _font.DrawText(_notice,nativeRect.Left+6,nativeRect.Top+6);
        }
        private float ZoomFactor {get{return (float)Math.Pow(2,_zoom);}}
        private float MarkerFactor()
        {
            return ScaleMarkersWithZoom?(float)Math.Pow(2,_zoom*.5f):1f;
        }
        private void PrepareGraphics(RenderTarget target)
        {
            if(_graphicsWorld==_world&&_graphicsTarget==target.NativePointer)return;
            ClearGraphics();_graphicsWorld=_world;_graphicsTarget=target.NativePointer;
        }
        private void DrawTerrain(MapView view,RectangleF clip,RenderTarget connectorTarget=null)
        {
            if(_model.GenerateExploredFill||_model.GenerateUnexploredFill)DrawFills(view,clip);
            if(connectorTarget!=null)DrawConnectorTints(connectorTarget,view,clip);
            if(ShowOutlines)DrawOutlines(view,clip);
        }
        private void DrawExpanded(MapView view,RectangleF clip,RectangleF corner)
        {
            var target=_source.RenderTarget;
            if(target==null||!MapModel.Finite(view.Scale)||view.Scale<=0||clip.Width<=0||clip.Height<=0)return;
            
            PrepareGraphics(target);
            var transform=target.Transform;bool layered=false;
            try
            {
                target.Transform=Matrix3x2.Identity;
                EnsureExpandedClip(target,clip,corner);
                var parameters=new LayerParameters {
                    ContentBounds=new SharpDX.Mathematics.Interop.RawRectangleF(clip.Left,clip.Top,clip.Right,clip.Bottom),
                    GeometricMask=_expandedClip,MaskAntialiasMode=AntialiasMode.Aliased,
                    MaskTransform=Matrix3x2.Identity,Opacity=Alpha(CenteredOpacityPercent)/255f,LayerOptions=LayerOptions.None };
                target.PushLayer(ref parameters,_expandedLayer);
                layered=true;
                target.Transform=view.Matrix;
                bool rift=Hud.Game.SpecialArea==SpecialArea.GreaterRift||Hud.Game.SpecialArea==SpecialArea.Rift
                    || FloorLinks.RiftLevel(Hud.Game.Me.WorldSno,null)>0;
                DrawTerrain(view,clip,rift?target:null);
                target.Transform=Matrix3x2.Identity;
                float markerSize=MarkerFactor();
                DrawMobs(view,clip,corner,markerSize);
                ReplayMapPainters(view,clip,corner,markerSize);
            }
            catch(Exception){}
            finally
            {
                target.Transform=Matrix3x2.Identity;
                if(layered)target.PopLayer();
                target.Transform=transform;
            }
            
        }
        private void PaintCenteredMarkers(MapView view,RectangleF clip,RectangleF corner,float markerSize)
        {
            if(clip.Width<=0||clip.Height<=0)return;
            DrawFallbackMarkers(view,clip,markerSize);
            DrawPortals(view,clip,corner,markerSize);
            foreach(var p in Hud.Game.Players)if(p.WorldId==_world&&p.CoordinateKnown&&p.FloorCoordinate!=null)
            {
                var point=view.Project(p.FloorCoordinate.X,p.FloorCoordinate.Y);
                float radius=(p.IsMe?3.5f:2.5f)*markerSize;
                if(clip.Contains(point.X,point.Y))_player.DrawEllipse(point.X,point.Y,radius,radius);
            }
        }
        private void ReplayMapPainters(MapView view,RectangleF clip,RectangleF corner,float markerSize)
        {
            Matrix3x2 original=_source.MapMatrix;
            float scale=_source.MapScale;
            if(!MapModel.Finite(scale)||scale<=0||!MapModel.Finite(view.Scale)||view.Scale<=0)
            {
                PaintCenteredMarkers(view,clip,corner,markerSize);
                return;
            }
            
            var projection=MapView.Remap(original,Hud.Game.Me.FloorCoordinate,Hud.Render,view);
            try
            {
                _paintingMarkers=true;
                _replayNativeScale=scale;
                _source.MapMatrix=projection;
                _source.MapScale=scale*MarkerFactor();
                foreach(var plugin in _mapPainters)
                {
                    if(!plugin.Enabled)continue;
                    try {((IInGameWorldPainter)plugin).PaintWorld(WorldLayer.Map);}
                    catch(Exception){}
                }
                if(_standard!=null&&_standard.Enabled)
                    foreach(var monster in Hud.Game.AliveMonsters)
                    {
                        if(monster.WorldId!=_world||monster.SnoMonster==null||monster.FloorCoordinate==null)continue;
                        if(monster.SnoMonster.Priority==MonsterPriority.boss&&_bossPainter!=null)_bossPainter(monster);
                        if(monster.SnoMonster.Priority==MonsterPriority.keywarden&&_keywardenPainter!=null)_keywardenPainter(monster);
                    }
                PaintCenteredMarkers(view,clip,corner,markerSize);
                if(_cursorOwner!=null&&_cursorOwner.Enabled&&_cursorOwner is IInGameWorldPainter cursor)
                {
                    try {cursor.PaintWorld(WorldLayer.Map);}
                    catch(Exception){}
                }
            }
            finally
            {
                try {_source.MapMatrix=original;_source.MapScale=scale;}
                finally {_paintingMarkers=false;_replayNativeScale=0;}
            }
            
        }
        private void EnsureExpandedClip(RenderTarget target,RectangleF screen,RectangleF corner)
        {
            if(_expandedClip!=null&&_clipScreen==screen&&_clipCorner==corner&&_clipTarget==target.NativePointer)return;
            DisposeExpandedClip();
            _expandedClip=Hud.Render.CreateGeometry();
            using(var sink=_expandedClip.Open())
            {
                foreach(var r in MapView.Exclude(screen,corner))
                {
                    sink.BeginFigure(new Vector2(r.Left,r.Top),FigureBegin.Filled);
                    sink.AddLine(new Vector2(r.Right,r.Top));sink.AddLine(new Vector2(r.Right,r.Bottom));
                    sink.AddLine(new Vector2(r.Left,r.Bottom));sink.EndFigure(FigureEnd.Closed);
                }
                sink.Close();
            }
            _expandedLayer=new Layer(target);
            _clipScreen=screen;_clipCorner=corner;_clipTarget=target.NativePointer;
        }
        private void DisposeExpandedClip()
        {
            if(_expandedLayer!=null)_expandedLayer.Dispose();
            if(_expandedClip!=null)_expandedClip.Dispose();
            _expandedLayer=null;_expandedClip=null;
        }
        private void DrawFallbackMarkers(MapView view,RectangleF clip,float factor)
        {
            if(_markerOwner==null)
                foreach(var marker in Hud.Game.Markers)
                    if(marker.WorldId==_world&&marker.FloorCoordinate!=null&&marker.TextureSno!=0)
                    {
                        var p=view.Project(marker.FloorCoordinate.X,marker.FloorCoordinate.Y);
                        if(clip.Contains(p.X,p.Y))DrawTextureIcon(marker.TextureSno,marker.TextureFrameIndex,p,factor);
                    }
        }
        private static bool PortalOnMap(IPortal portal)
        {
            var area=portal.Scene?.SnoArea;
            return area!=null&&!area.IsTown
                &&!(portal.TargetArea.Code??"").StartsWith("p76_nesting",StringComparison.OrdinalIgnoreCase)
                &&!(area.Code??"").StartsWith("_p76_nesting",StringComparison.OrdinalIgnoreCase);
        }
        private float DrawTextureIcon(uint sno,int frame,Vector2 point,float factor)
        {
            ulong key=((ulong)sno<<32)|(uint)frame;
            ITexture texture;
            if(!_markerTextures.TryGetValue(key,out texture))
            {
                texture=Hud.Texture.GetTexture(sno,frame);
                _markerTextures[key]=texture;
            }
            if(texture==null)return 0;
            float scale=(_paintingMarkers?_replayNativeScale:Hud.Render.MinimapScale)*factor,w=texture.Width*scale,h=texture.Height*scale;
            texture.Draw(point.X-w*.5f,point.Y-h*.5f,w,h,1f);
            return h;
        }
        private void DrawPortals(MapView view,RectangleF clip,RectangleF corner,float factor)
        {
            
            if(_portalOwner!=null&&!_portalOwner.Enabled)return;
            var links=_model.Links;
            var exit=links.Exit;
            foreach(var portal in links.Live)
            {
                if(exit!=null&&FloorLinks.SamePoint(portal,exit))continue;
                var p=view.Project(portal.X,portal.Y);
                if(VisiblePoint(p,clip,corner))DrawPortal(portal,p,factor);
            }
            var entry=links.Entrance;
            if(entry!=null)
            {
                var p=view.Project(entry.X,entry.Y);
                if(VisiblePoint(p,clip,corner)&&!links.Live.Any(live=>FloorLinks.SamePoint(live,entry)))DrawPortal(entry,p,factor);
            }
            else if(links.EntryTile!=null&&!links.Live.Any(p=>links.EntryTile.Bounds.Contains(p.X,p.Y)))
            {
                var p=view.Project(links.EntryTile.X,links.EntryTile.Y);
                if(VisiblePoint(p,clip,corner))DrawTileCue(p,_entrance,"Entrance tile",factor);
            }
            var tile=links.ExitTile;
            if(exit==null&&(tile==null||links.ExitConflict))return;
            var point=exit!=null?view.Project(exit.X,exit.Y):view.Project(tile.X,tile.Y);
            bool visible=VisiblePoint(point,clip,corner);
            if(visible)
            {
                if(exit!=null)DrawPortal(exit,point,factor);
                else DrawTileCue(point,_exit,"Exit tile",factor);
            }
            DrawExitCue(view,clip,corner,point,visible,exit!=null?"Exit":"Exit tile");
        }
        private void DrawExitCue(MapView view,RectangleF clip,RectangleF corner,Vector2 point,bool visible,string label)
        {
            const int travelMs=550;
            int now=Environment.TickCount;
            if(_exitCueStarted==int.MinValue)_exitCueStarted=now;
            uint age=unchecked((uint)(now-_exitCueStarted));
            if(visible&&age>=travelMs)return;

            Vector2 edge=point,direction=default(Vector2);
            if(!visible)
            {
                if(!MapView.TryEdgeCue(clip,corner,point,out edge,out direction))return;
                
            }
            if(age<travelMs)
            {
                var me=Hud.Game.Me.FloorCoordinate;
                var start=view.Project(me.X,me.Y);
                var travel=edge-start;
                float length=travel.Length();
                if(length>24&&VisiblePoint(start,clip,corner))
                {
                    direction=travel/length;
                    float end=visible?length-12:length;
                    float remaining=1f-age/(float)travelMs;
                    edge=start+direction*(end-(end-12)*remaining*remaining);
                }
                else if(visible)return;
            }
            if(!VisiblePoint(edge,clip,corner))return;
            var normal=new Vector2(-direction.Y,direction.X);
            var tail=edge-direction*6;
            _exit.DrawLine(edge.X,edge.Y,tail.X+normal.X*3,tail.Y+normal.Y*3);
            _exit.DrawLine(edge.X,edge.Y,tail.X-normal.X*3,tail.Y-normal.Y*3);
            DrawPortalLabel(label,edge.X-direction.X*18,edge.Y-direction.Y*18);
        }
        private static bool VisiblePoint(Vector2 point,RectangleF clip,RectangleF corner)
        {
            return clip.Contains(point.X,point.Y)&&!corner.Contains(point.X,point.Y);
        }
        private void DrawPortal(PortalPoint portal,Vector2 point,float factor)
        {
            float height=DrawTextureIcon(448992u,0,point,factor);
            DrawPortalLabel(portal.Label,point.X,point.Y+Math.Max(3,height*.5f));
        }
        private void DrawTileCue(Vector2 point,IBrush brush,string label,float factor)
        {
            float size=5*factor;
            brush.DrawEllipse(point.X,point.Y,size,size);
            DrawPortalLabel(label,point.X,point.Y+size);
        }
        private void DrawPortalLabel(string text,float x,float y)
        {
            if(string.IsNullOrEmpty(text))return;
            var layout=_portalLabel.GetTextLayout(text);
            _portalLabel.DrawText(layout,x-layout.Metrics.Width*.5f,y);
        }
        private void DrawOutlines(MapView view,RectangleF clip)
        {
            _visiblePaths.Clear();
            long rebuildUntil=System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency/500;
            int built=0;
            foreach(var t in _model.Tiles.Values)
            {
                if(t.Edges.Length==0||!view.Intersects(t,clip))continue;
                
                CachedPaths paths;
                if(!_graphics.TryGetValue(t.Key,out paths)||paths.Version!=t.Version)
                {
                    if(built==0||System.Diagnostics.Stopwatch.GetTimestamp()<rebuildUntil)
                    {
                        var next=CachedPaths.Build(Hud,t);
                        if(paths!=null)paths.Dispose();
                        _graphics[t.Key]=paths=next;
                        built++;
                        
                    }
                }
                if(paths!=null)_visiblePaths.Add(paths);
            }
            float thickness=Math.Max(.25f,Math.Min(8f,OutlineThicknessPixels));
            float rimWidth=Math.Max(0f,Math.Min(4f,OutlineRimWidthPixels));
            if(ShowOutlineRim&&rimWidth>0&&Alpha(OutlineRimOpacityPercent)>0)
            {
                _rim.StrokeWidth=(thickness+2*rimWidth)/view.Scale;
                foreach(var paths in _visiblePaths)
                    for(int i=0;i<3;i++)if(paths.Paths[i]!=null){_rim.DrawGeometry(paths.Paths[i]);  }
            }
            for(int i=0;i<_walls.Length;i++)_walls[i].StrokeWidth=thickness/view.Scale;
            foreach(var paths in _visiblePaths)
                for(int i=0;i<_walls.Length;i++)if(paths.Paths[i]!=null){_walls[i].DrawGeometry(paths.Paths[i]);  }
        }
        private void DrawMobs(MapView view,RectangleF clip,RectangleF corner,float factor)
        {
            float radius=Math.Max(1f,Math.Min(2.8f,TrashDotRadiusPixels)*factor);
            
            foreach(var m in Hud.Game.AliveMonsters)
            {
                if(m.WorldId!=_world||m.Hidden||m.Invisible)continue;
                bool trash=IsTrashDot(m);
                if(!trash )continue;
                var at=m.FloorCoordinate;
                if(at==null||!MapModel.Finite(at.X)||!MapModel.Finite(at.Y))continue;
                float x,y;
                if(view==null)Hud.Render.GetMinimapCoordinates(at.X,at.Y,out x,out y);
                else {var point=view.Project(at.X,at.Y);x=point.X;y=point.Y;}
                if(!clip.Contains(x,y)||corner.Contains(x,y))continue;
                if(trash){_trash.DrawEllipse(x,y,radius,radius); }
                
            }
        }
        private static bool IsTrashDot(IMonster monster)
        {
            if(monster.IsElite||monster.Rarity!=ActorRarity.Normal)return false;
            var sno=monster.SnoMonster;
            return sno==null||(sno.Priority!=MonsterPriority.boss&&sno.Priority!=MonsterPriority.keywarden&&sno.Priority!=MonsterPriority.goblin);
        }
        private void DrawConnectorTints(RenderTarget target,MapView view,RectangleF clip)
        {
            foreach(var tile in _model.ConnectorTiles)
            {
                if(!view.Intersects(tile.Bounds,clip))continue;
                PathGeometry path;
                if(!_connectorFillGraphics.TryGetValue(tile,out path))
                {
                    path=CachedPaths.BuildConnector(Hud,tile.ConnectorFills);
                    _connectorFillGraphics.Add(tile,path);
                }
                SolidColorBrush brush;
                if(tile.ConnectorRole==1)
                    brush=_connectorEntranceFill??(_connectorEntranceFill=new SolidColorBrush(target,
                        new SharpDX.Mathematics.Interop.RawColor4(.24f,.76f,.37f,.28f)));
                else
                    brush=_connectorExitFill??(_connectorExitFill=new SolidColorBrush(target,
                        new SharpDX.Mathematics.Interop.RawColor4(.88f,.25f,.22f,.30f)));
                target.FillGeometry(path,brush,null);
                
            }
        }
        private void DrawFills(MapView view,RectangleF clip)
        {
            long rebuildUntil=System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency/500;
            int built=0;
            foreach(var item in _model.FillPages)
            {
                var page=item.Value;
                if(!view.Intersects(page.Bounds,clip))continue;
                
                CachedPaths paths;
                if((!_fillGraphics.TryGetValue(item.Key,out paths)||paths.Version!=page.Version)
                    && (built==0||System.Diagnostics.Stopwatch.GetTimestamp()<rebuildUntil))
                {
                    var next=CachedPaths.BuildFills(Hud,page);
                    if(paths!=null)paths.Dispose();
                    _fillGraphics[item.Key]=paths=next;
                    built++;
                    
                }
                if(paths!=null)for(int i=0;i<paths.Paths.Length;i++)if(paths.Paths[i]!=null){_fillBrushes[i].DrawGeometry(paths.Paths[i]);  }
            }
        }
        private void ClearGraphics()
        {
            foreach(var g in _graphics.Values)g.Dispose();
            _graphics.Clear();
            foreach(var g in _fillGraphics.Values)g.Dispose();
            _fillGraphics.Clear();
            foreach(var g in _connectorFillGraphics.Values)g.Dispose();
            _connectorFillGraphics.Clear();
            _connectorEntranceFill?.Dispose();_connectorEntranceFill=null;
            _connectorExitFill?.Dispose();_connectorExitFill=null;
            
            _markerTextures.Clear();
            _visiblePaths.Clear();
            DisposeExpandedClip();
        }
        

        // View math is independent of HUD's shared minimap projection and global fields.
        internal sealed class MapView
        {
            internal readonly float Scale;
            internal readonly Matrix3x2 Matrix;
            internal MapView(float x,float y,float ax,float ay,float scale)
            {Scale=scale;Matrix=Matrix3x2.Translation(-x,-y)*Matrix3x2.Scaling(-scale,scale)*Matrix3x2.Rotation(-.7853982f)*Matrix3x2.Translation(ax,ay);}
            internal Vector2 Project(float x,float y){return new Vector2(x*Matrix.M11+y*Matrix.M21+Matrix.M31,x*Matrix.M12+y*Matrix.M22+Matrix.M32);}
            internal static Matrix3x2 Remap(Matrix3x2 original,IWorldCoordinate player,IRenderController render,MapView view)
            {
                float ax,ay;render.GetMinimapCoordinates(player.X,player.Y,out ax,out ay);
                var anchor=view.Project(player.X,player.Y);
                float factor=view.Scale/render.MinimapScale;
                return original*Matrix3x2.Translation(-ax,-ay)*Matrix3x2.Scaling(factor)*Matrix3x2.Translation(anchor.X,anchor.Y);
            }
            internal bool Intersects(Tile t,System.Drawing.RectangleF r)
            {
                return Intersects(new RectangleF(t.X,t.Y,t.W,t.H),r);
            }
            internal bool Intersects(RectangleF area,RectangleF r)
            {
                float halfW=area.Width*.5f,halfH=area.Height*.5f;
                var center=Project(area.X+halfW,area.Y+halfH);
                float extentX=Math.Abs(Matrix.M11)*halfW+Math.Abs(Matrix.M21)*halfH;
                float extentY=Math.Abs(Matrix.M12)*halfW+Math.Abs(Matrix.M22)*halfH;
                // A subpixel margin absorbs rounding at a clipped edge.
                const float margin=.01f;
                return center.X-extentX<=r.Right+margin&&center.X+extentX>=r.Left-margin
                    &&center.Y-extentY<=r.Bottom+margin&&center.Y+extentY>=r.Top-margin;
            }
            internal static IEnumerable<System.Drawing.RectangleF> Exclude(System.Drawing.RectangleF r,System.Drawing.RectangleF hole)
            {
                var h=System.Drawing.RectangleF.Intersect(r,hole);
                if(h.Width<=0||h.Height<=0){yield return r;yield break;}
                if(h.Top>r.Top)yield return new System.Drawing.RectangleF(r.Left,r.Top,r.Width,h.Top-r.Top);
                if(h.Bottom<r.Bottom)yield return new System.Drawing.RectangleF(r.Left,h.Bottom,r.Width,r.Bottom-h.Bottom);
                if(h.Left>r.Left)yield return new System.Drawing.RectangleF(r.Left,h.Top,h.Left-r.Left,h.Height);
                if(h.Right<r.Right)yield return new System.Drawing.RectangleF(h.Right,h.Top,r.Right-h.Right,h.Height);
            }
            internal static bool TryEdgeCue(RectangleF clip,RectangleF corner,Vector2 point,out Vector2 edge,out Vector2 direction)
            {
                edge=direction=default(Vector2);
                if(!MapModel.Finite(point.X)||!MapModel.Finite(point.Y))return false;
                float best=float.PositiveInfinity;
                foreach(var piece in Exclude(clip,corner))
                {
                    if(piece.Width<=24||piece.Height<=24)continue;
                    var p=new Vector2(Math.Max(piece.Left+12,Math.Min(piece.Right-12,point.X)),
                        Math.Max(piece.Top+12,Math.Min(piece.Bottom-12,point.Y)));
                    var delta=point-p;
                    float distance=delta.LengthSquared();
                    if(distance<=0||distance>=best)continue;
                    best=distance;edge=p;direction=delta/(float)Math.Sqrt(distance);
                }
                return !float.IsInfinity(best);
            }
        }
        internal sealed class PortalPoint
        {
            internal float X,Y;
            internal int Destination;
            internal string Label;
            
        }
        internal sealed class ConnectorTile
        {
            internal uint Sno;
            internal RectangleF Bounds;
            internal float X {get{return Bounds.X+Bounds.Width*.5f;}}
            internal float Y {get{return Bounds.Y+Bounds.Height*.5f;}}
        }
        private sealed class FloorLinks
        {
            private const string LevelPrefix="x1_lr_level_";
            internal readonly List<PortalPoint> Live=new List<PortalPoint>();
            internal uint World;
            internal int Level;
            internal PortalPoint Entrance,Exit;
            internal ConnectorTile EntryTile,ExitTile;
            internal bool ExitConflict;
            internal void Clear()
            {
                World=0;Level=0;Live.Clear();Entrance=Exit=null;EntryTile=ExitTile=null;ExitConflict=false;
            }
            internal static int RiftLevel(string code)
            {
                int level;
                return code!=null&&code.StartsWith(LevelPrefix,StringComparison.OrdinalIgnoreCase)
                    &&int.TryParse(code.Substring(LevelPrefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out level)
                    &&level>=1&&level<=10?level:0;
            }
            internal static int RiftLevel(uint worldSno,string areaCode)
            {
                switch((WorldsSnoEnum)worldSno)
                {
                    case WorldsSnoEnum._x1_lr_level_01:return 1;
                    case WorldsSnoEnum._x1_lr_level_02:return 2;
                    case WorldsSnoEnum._x1_lr_level_03:return 3;
                    case WorldsSnoEnum._x1_lr_level_04:return 4;
                    case WorldsSnoEnum._x1_lr_level_05:return 5;
                    case WorldsSnoEnum._x1_lr_level_06:return 6;
                    case WorldsSnoEnum._x1_lr_level_07:return 7;
                    case WorldsSnoEnum._x1_lr_level_08:return 8;
                    case WorldsSnoEnum._x1_lr_level_09:return 9;
                    case WorldsSnoEnum._x1_lr_level_10:return 10;
                    default:return RiftLevel(areaCode);
                }
            }
            internal static bool SamePoint(PortalPoint a,PortalPoint b)
            {
                return a.Destination==b.Destination&&Math.Abs(a.X-b.X)<=.5f&&Math.Abs(a.Y-b.Y)<=.5f;
            }
            internal void Observe(IEnumerable<IPortal> portals,uint world,int level)
            {
                // The rift phase can disappear before the player leaves this world.
                if(level==0 && World==world)level=Level;
                if(World!=world||Level!=level){Clear();World=world;Level=level;}
                Live.Clear();
                PortalPoint entry=null,exit=null;
                bool conflict=false,entryConflict=false;
                foreach(var actor in portals)
                {
                    if(actor==null||actor.WorldId!=world||actor.TargetArea==null)continue;
                    var at=actor.FloorCoordinate;
                    if(at==null||!MapModel.Finite(at.X)||!MapModel.Finite(at.Y))continue;
                    int destination=RiftLevel(actor.TargetArea.Code);
                    var point=new PortalPoint{X=at.X,Y=at.Y,Destination=destination,
                        Label=actor.TargetArea.NameLocalized??actor.TargetArea.Code};
                    
                    if(PortalOnMap(actor)&&!Live.Any(p=>SamePoint(p,point)))Live.Add(point);
                    if(level<=0||actor.IsDisabled||actor.TargetWorldId==world)continue;
                    if(destination==level+1)
                    {
                        if(exit!=null&&!SamePoint(exit,point))conflict=true;
                        else exit=point;
                    }
                    else if((destination>0&&destination==level-1)
                        ||(level==1&&actor.TargetArea.IsTown&&EntryTile!=null&&EntryTile.Bounds.Contains(at.X,at.Y)))
                    {
                        if(entry!=null&&!SamePoint(entry,point))entryConflict=true;
                        else entry=point;
                    }
                }
                if(exit!=null)
                {
                    ExitConflict=conflict;
                    Exit=conflict?null:exit;
                }
                if(entry!=null)Entrance=entryConflict?null:entry;
            }
            internal void ObserveTiles(NativeMapSource.Placement[] placements)
            {
                if(Level==0)return;
                ConnectorTile entry=null,exit=null;
                bool entryConflict=false,exitConflict=false;
                foreach(var pose in placements)
                {
                    if(!pose.Selected||!MapModel.ValidPlacement(pose))continue;
                    int role=VerifiedRouteRole(pose.SnoScene);
                    if(role==0)continue;
                    var bounds=new RectangleF(pose.PosX,pose.PosY,pose.W,pose.H);
                    if(role==1)ChooseTile(ref entry,ref entryConflict,pose.SnoScene.Sno,bounds);
                    else ChooseTile(ref exit,ref exitConflict,pose.SnoScene.Sno,bounds);
                }
                if(entry!=null)EntryTile=entryConflict?null:entry;
                if(exit!=null)ExitTile=exitConflict?null:exit;
            }
            private static void ChooseTile(ref ConnectorTile tile,ref bool conflict,uint sno,RectangleF bounds)
            {
                if(tile==null){tile=new ConnectorTile{Sno=sno,Bounds=bounds};return;}
                if(!MapModel.SameBounds(tile.Bounds,bounds))conflict=true;
            }
            private static int VerifiedRouteRole(ISnoScene scene)
            {
                if(scene.Sno==234966 && string.Equals(scene.Code,"x1_bogcave_w_exit_01",StringComparison.OrdinalIgnoreCase))return 2;
                string suffix;
                int role;
                switch(scene.Sno)
                {
                    case 409650:suffix="NE_Entrance_01";role=1;break;
                    case 409789:suffix="NW_Entrance_01";role=1;break;
                    case 410335:suffix="SE_Entrance_01";role=1;break;
                    case 409962:suffix="SW_Entrance_01";role=1;break;
                    case 415346:suffix="NE_Exit_01";role=2;break;
                    case 415337:suffix="NW_Exit_01";role=2;break;
                    case 415327:suffix="SE_Exit_01";role=2;break;
                    case 415276:suffix="SW_Exit_01";role=2;break;
                    default:return 0;
                }
                return string.Equals(scene.Code,"px_TristramFields_"+suffix,StringComparison.OrdinalIgnoreCase)?role:0;
            }
        }
        internal struct Edge : IEquatable<Edge>
        {
            internal float X1,Y1,X2,Y2;internal int Kind;
            public bool Equals(Edge other)
            {return X1==other.X1&&Y1==other.Y1&&X2==other.X2&&Y2==other.Y2&&Kind==other.Kind;}
        }
        internal struct FillRect : IEquatable<FillRect>
        {
            internal float X,Y,W,H;internal int Kind;
            internal RectangleF Bounds {get{return new RectangleF(X,Y,W,H);}}
            public bool Equals(FillRect other)
            {return X==other.X&&Y==other.Y&&W==other.W&&H==other.H&&Kind==other.Kind;}
        }
        internal sealed class Tile
        {
            internal string Key;internal uint Sno;internal float X,Y,W,H;
            internal int ConnectorRole;
            internal FillRect[] ConnectorFills;
            
            internal int Width,Height,FogWidth,FogHeight,Version;internal bool[] Walk;internal byte[] Fog;
            internal bool[][] WalkBorders;
            internal RectangleF Bounds {get{return new RectangleF(X,Y,W,H);}}
            internal Edge[] Edges=new Edge[0];
            internal FillRect[] Fills=new FillRect[0];
            internal bool At(int x,int y){return x>=0&&y>=0&&x<Width&&y<Height&&Walk[y*Width+x];}
        }
        internal sealed class FillPage
        {
            internal const int Size=240;
            internal readonly RectangleF Bounds;
            internal readonly HashSet<Tile> Tiles=new HashSet<Tile>();
            internal int Version;
            internal FillPage(Point at){Bounds=new RectangleF(at.X*Size,at.Y*Size,Size,Size);}
        }
        private sealed class CachedPaths : IDisposable
        {
            internal readonly int Version;
            internal readonly PathGeometry[] Paths;
            private CachedPaths(int version,int count){Version=version;Paths=new PathGeometry[count];}
            internal static CachedPaths Build(IController hud,Tile t)
            {
                var result=new CachedPaths(t.Version,4);
                try
                {
                    for(int kind=0;kind<result.Paths.Length;kind++)
                    {
                        if(!t.Edges.Any(e=>e.Kind==kind))continue;
                        var path=hud.Render.CreateGeometry();result.Paths[kind]=path;
                        using(var sink=path.Open())
                        {foreach(var e in t.Edges)if(e.Kind==kind){sink.BeginFigure(new Vector2(e.X1,e.Y1),FigureBegin.Hollow);sink.AddLine(new Vector2(e.X2,e.Y2));sink.EndFigure(FigureEnd.Open);}sink.Close();}
                    }
                    return result;
                }
                catch{result.Dispose();throw;}
            }
            internal static PathGeometry BuildConnector(IController hud,FillRect[] fills)
            {
                var path=hud.Render.CreateGeometry();
                try
                {
                    using(var sink=path.Open())
                    {
                        sink.SetFillMode(FillMode.Winding);
                        foreach(var fill in fills)WriteFill(sink,fill.Bounds);
                        sink.Close();
                    }
                    return path;
                }
                catch{path.Dispose();throw;}
            }
            private static void WriteFill(GeometrySink sink,RectangleF r)
            {
                sink.BeginFigure(new Vector2(r.Left,r.Top),FigureBegin.Filled);
                sink.AddLine(new Vector2(r.Right,r.Top));sink.AddLine(new Vector2(r.Right,r.Bottom));
                sink.AddLine(new Vector2(r.Left,r.Bottom));sink.EndFigure(FigureEnd.Closed);
            }
            internal static CachedPaths BuildFills(IController hud,FillPage page)
            {
                var result=new CachedPaths(page.Version,2);
                var sinks=new GeometrySink[result.Paths.Length];
                try
                {
                    foreach(var tile in page.Tiles)foreach(var fill in tile.Fills)
                    {
                        var r=RectangleF.Intersect(fill.Bounds,page.Bounds);
                        if(r.Width<=0||r.Height<=0)continue;
                        int kind=fill.Kind;
                        var sink=sinks[kind];
                        if(sink==null)
                        {
                            var path=hud.Render.CreateGeometry();
                            result.Paths[kind]=path;
                            sinks[kind]=sink=path.Open();sink.SetFillMode(FillMode.Winding);
                        }
                        WriteFill(sink,r);
                    }
                    foreach(var sink in sinks)if(sink!=null)sink.Close();
                    return result;
                }
                catch{result.Dispose();throw;}
                finally{foreach(var sink in sinks)if(sink!=null)sink.Dispose();}
            }
            public void Dispose(){foreach(var p in Paths)if(p!=null)p.Dispose();}
        }
        private sealed class MapModel
        {
            private const int MaxCachedCells=8000000;
            private const int MaxCachedTiles=1024;
            private const int Explored=0, Unexplored=1, Frontier=2, UnknownSeam=3, Uncertain=4;
            internal readonly Dictionary<string,Tile> Tiles=new Dictionary<string,Tile>();
            internal readonly FloorLinks Links=new FloorLinks();
            
            internal readonly Dictionary<Point,FillPage> FillPages=new Dictionary<Point,FillPage>();
            internal readonly List<Tile> ConnectorTiles=new List<Tile>(8);
            internal MapGeometryFallback Fallback;
            private readonly HashSet<Tile> _dirtyTiles=new HashSet<Tile>();
            private readonly HashSet<string> _unusable=new HashSet<string>();
            private readonly HashSet<string> _nonMappable=new HashSet<string>();
            private readonly HashSet<RectangleF> _resolvedVoids=new HashSet<RectangleF>();
            private readonly List<Tile> _maskTiles=new List<Tile>();
            private readonly Dictionary<string,int> _retryLayoutAfter=new Dictionary<string,int>();
            private readonly List<TrailPoint> _trail=new List<TrailPoint>();
            private readonly HashSet<string> _scored=new HashSet<string>();
            private readonly long[] _scores=new long[8];
            private int _serial,_informative,_cursor,_version,_cachedCells;
            internal bool GenerateExploredFill=true,GenerateUnexploredFill=true;
            
            
            internal int Orientation=-1;
            internal void Clear()
            {
                Tiles.Clear();FillPages.Clear();ConnectorTiles.Clear();
                _dirtyTiles.Clear();_unusable.Clear();_nonMappable.Clear();_retryLayoutAfter.Clear();
                _resolvedVoids.Clear();_maskTiles.Clear();
                if(Fallback!=null)Fallback.Reset();
                _trail.Clear();_scored.Clear();Array.Clear(_scores,0,8);
                _informative=0;_cursor=0;_cachedCells=0;Orientation=-1;
                
            }
            private sealed class TrailPoint {internal float X,Y;internal int Id,Time;}
            internal static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
            internal static string Key(uint sno,float x,float y){return sno+":"+x.ToString("R",CultureInfo.InvariantCulture)+":"+y.ToString("R",CultureInfo.InvariantCulture);}
            internal void Update(NativeMapSource source,IPlayer me,int now,bool layout,bool fog)
            {
                var p=me.FloorCoordinate;
                if(p==null||!Finite(p.X)||!Finite(p.Y))return;
                
                if(_trail.Count==0||Math.Abs(_trail[_trail.Count-1].X-p.X)+Math.Abs(_trail[_trail.Count-1].Y-p.Y)>5)
                {_trail.Add(new TrailPoint{X=p.X,Y=p.Y,Id=++_serial,Time=now});if(_trail.Count>128)_trail.RemoveAt(0);}
                source.Refresh(me.WorldId);
                if(layout)AddLayouts(source,me,p.X,p.Y,now);
                int oldOrientation=Orientation;
                var scene=fog?me.Scene:null;
                string current=scene?.SnoScene==null?null:Key(scene.SnoScene.Sno,scene.PosX,scene.PosY);
                Tile first=null,second=null;
                float firstDistance=float.PositiveInfinity,secondDistance=float.PositiveInfinity;
                _maskTiles.Clear();
                foreach(var tile in Tiles.Values)
                {
                    if(!source.HasMask(tile.Key))continue;
                    if(layout)_maskTiles.Add(tile);
                    if(!fog)continue;
                    float distance=tile.Key==current?-1f:DistanceToBounds(tile,p.X,p.Y);
                    if(distance<firstDistance)
                    {
                        second=first;secondDistance=firstDistance;
                        first=tile;firstDistance=distance;
                    }
                    else if(distance<secondDistance)
                    {
                        second=tile;secondDistance=distance;
                    }
                }
                if(first!=null)RefreshMask(source,first,now,true);
                if(second!=null)RefreshMask(source,second,now,true);
                if(layout)
                {
                    int attempts=0;
                    for(int n=0;n<_maskTiles.Count&&attempts<2;n++)
                    {
                        var tile=_maskTiles[_cursor%_maskTiles.Count];
                        _cursor=(_cursor+1)%_maskTiles.Count;
                        if(tile==first||tile==second)continue;
                        ++attempts;RefreshMask(source,tile,now,false);
                    }
                }
                if(oldOrientation!=Orientation)
                    foreach(var tile in Tiles.Values)_dirtyTiles.Add(tile);
                RebuildLayouts(p.X,p.Y);
                
                if(_scored.Count>8192)_scored.Clear();
            }
            private void RefreshMask(NativeMapSource source,Tile tile,int now,bool local)
            {
                
                NativeMapSource.Mask mask;
                if(!source.TryMask(tile.Key,out mask))return;
                
                if(AcceptMask(tile,mask.Bytes,mask.Width,mask.Height))
                {
                    _dirtyTiles.Add(tile);
                    foreach(var other in Tiles.Values)
                        if(other.Fog==null&&SameBounds(tile,other))_dirtyTiles.Add(other);
                    
                }
                Calibrate(tile,now);
            }
            private static bool AcceptMask(Tile tile,byte[] buffer,int width,int height)
            {
                long count=(long)width*height;
                if(buffer==null||width<=0||height<=0||count>buffer.Length)return false;
                int length=(int)count;
                if(tile.Fog!=null&&tile.FogWidth==width&&tile.FogHeight==height)
                {
                    int i=0;
                    while(i<length&&tile.Fog[i]==buffer[i])++i;
                    if(i==length)return false;
                }
                var copy=new byte[length];Array.Copy(buffer,copy,length);
                tile.Fog=copy;tile.FogWidth=width;tile.FogHeight=height;
                return true;
            }
            private static float DistanceToBounds(Tile t,float x,float y)
            {
                float dx=Math.Max(t.X-x,Math.Max(0,x-t.X-t.W));
                float dy=Math.Max(t.Y-y,Math.Max(0,y-t.Y-t.H));
                return dx*dx+dy*dy;
            }
            internal static bool ValidPlacement(NativeMapSource.Placement scene)
            {
                return Finite(scene.PosX)&&Finite(scene.PosY)&&scene.W>0&&scene.H>0&&scene.W<=1500&&scene.H<=1500;
            }
            private void AddLayouts(NativeMapSource source,IPlayer me,float x,float y,int now)
            {
                
                var placements=source.Placements(me).ToArray();
                Links.ObserveTiles(placements);
                
                if(Fallback!=null)Fallback.Begin(me.WorldId,me.SnoWorld?.Sno??0);
                if(Tiles.Count>=MaxCachedTiles||_cachedCells>=MaxCachedCells)
                {  return;}
                var pending=placements.Where(ValidPlacement)
                    .Select(s=>new{Scene=s,Key=Key(s.SnoScene.Sno,s.PosX,s.PosY)})
                    .Where(p=>{
                        int retryAfter;
                        return !Tiles.ContainsKey(p.Key)&&!_unusable.Contains(p.Key)&&!_nonMappable.Contains(p.Key)
                            &&(!_retryLayoutAfter.TryGetValue(p.Key,out retryAfter)||unchecked(now-retryAfter)>=0);
                    })
                    .OrderBy(p=>Math.Abs(p.Scene.PosX+p.Scene.W*.5f-x)+Math.Abs(p.Scene.PosY+p.Scene.H*.5f-y));
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var attempted=new HashSet<string>();
                foreach(var entry in pending)
                {
                    if(!attempted.Add(entry.Key))continue;
                    if(attempted.Count>24||(attempted.Count>1&&clock.ElapsedMilliseconds>=8)||Tiles.Count>=MaxCachedTiles)break;
                    var scene=entry.Scene;
                    var tile=new Tile{Key=entry.Key,Sno=scene.SnoScene.Sno,X=scene.PosX,Y=scene.PosY,W=scene.W,H=scene.H,
                        ConnectorRole=NativeConnectorRole(scene.SnoScene.Code),
                        Width=Math.Max(8,Math.Min(256,(int)Math.Ceiling(scene.W/1.5f))),Height=Math.Max(8,Math.Min(256,(int)Math.Ceiling(scene.H/1.5f)))};
                    int size=tile.Width*tile.Height;
                    if(_cachedCells+size>MaxCachedCells){_unusable.Add(entry.Key);continue;}
                    tile.Walk=new bool[size];
                    if(!Rasterize(tile,source.NavCells(scene.SnoScene)))
                    {
                        var cached=source.VerifiedNonMappable(scene.SnoScene)?Array.Empty<NativeMapSource.NavRect>():Fallback?.Cells(tile);
                        if(cached!=null&&cached.Length==0)
                        {
                            _nonMappable.Add(entry.Key);_retryLayoutAfter.Remove(entry.Key);
                            if(_resolvedVoids.Add(tile.Bounds))
                                foreach(var other in Tiles.Values)
                                    if(NearBounds(other,tile))_dirtyTiles.Add(other);
                            continue;
                        }
                        if(!Rasterize(tile,cached))
                        {
                            _retryLayoutAfter[entry.Key]=unchecked(now+2500);
                            continue;
                        }
                        
                    }
                    _retryLayoutAfter.Remove(entry.Key);
                    Tiles.Add(entry.Key,tile);
                    if(tile.ConnectorRole!=0)
                    {
                        tile.ConnectorFills=BuildFill(tile,connector:true);
                        if(tile.ConnectorFills.Length!=0)ConnectorTiles.Add(tile);
                    }
                    _cachedCells+=size;
                    _dirtyTiles.Add(tile);
                    foreach(var other in Tiles.Values)
                        if(other!=tile&&NearBounds(other,tile))_dirtyTiles.Add(other);
                }
                
                
            }
            internal static int NativeConnectorRole(string code)
            {
                if(string.IsNullOrEmpty(code))return 0;
                if(code.IndexOf("exit",StringComparison.OrdinalIgnoreCase)>=0)return 2;
                return code.IndexOf("entrance",StringComparison.OrdinalIgnoreCase)>=0?1:0;
            }
            private static bool Rasterize(Tile tile,NativeMapSource.NavRect[] cells)
            {
                if(cells==null)return false;
                const float edgeTolerance=.05f;
                bool hasWalk=false;
                foreach(var c in cells)
                {
                    if(!Finite(c.MinX)||!Finite(c.MinY)||!Finite(c.MaxX)||!Finite(c.MaxY)
                        ||c.MinX>=c.MaxX||c.MinY>=c.MaxY||c.MaxX<=0||c.MaxY<=0||c.MinX>=tile.W||c.MinY>=tile.H)continue;
                    int x0=(int)Math.Floor(Math.Max(0,c.MinX)/tile.W*tile.Width);
                    int x1=(int)Math.Ceiling(Math.Min(tile.W,c.MaxX)/tile.W*tile.Width);
                    int y0=(int)Math.Floor(Math.Max(0,c.MinY)/tile.H*tile.Height);
                    int y1=(int)Math.Ceiling(Math.Min(tile.H,c.MaxY)/tile.H*tile.Height);
                    if(x0>=x1||y0>=y1)continue;
                    hasWalk=true;
                    for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)tile.Walk[y*tile.Width+x]=true;
                    // A rounded raster cell at the border is insufficient. Use the
                    // original walkable rectangle against the actual scene bounds.
                    if(c.MinY<=edgeTolerance)RecordWalkBorder(tile,0,c.MinX,c.MaxX);
                    if(c.MaxY>=tile.H-edgeTolerance)RecordWalkBorder(tile,1,c.MinX,c.MaxX);
                    if(c.MinX<=edgeTolerance)RecordWalkBorder(tile,2,c.MinY,c.MaxY);
                    if(c.MaxX>=tile.W-edgeTolerance)RecordWalkBorder(tile,3,c.MinY,c.MaxY);
                }
                if(tile.WalkBorders!=null)
                    foreach(var border in tile.WalkBorders)
                    {
                        if(border==null)continue;
                        for(int i=0;i<border.Length;i++)
                            if(border[i]&&(i==0||!border[i-1])&&(i+1==border.Length||!border[i+1]))
                                border[i]=false;
                    }
                return hasWalk;
            }
            private static void RecordWalkBorder(Tile tile,int side,float start,float end)
            {
                int count=side<2?tile.Width:tile.Height;
                float length=side<2?tile.W:tile.H,step=length/count;
                int first=(int)Math.Ceiling(Math.Max(0,start)/step-.5f);
                int last=Math.Min(count,(int)Math.Ceiling(Math.Min(length,end)/step-.5f));
                if(first>=last)return;
                if(tile.WalkBorders==null)tile.WalkBorders=new bool[4][];
                var border=tile.WalkBorders[side];
                if(border==null)tile.WalkBorders[side]=border=new bool[count];
                for(int i=first;i<last;i++)border[i]=true;
            }
            
            private void RebuildLayouts(float x,float y)
            {
                if(_dirtyTiles.Count==0)return;
                
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var batch=_dirtyTiles.OrderBy(t=>DistanceToBounds(t,x,y)).Take(16).ToArray();
                for(int i=0;i<batch.Length;i++)
                {
                    if(i>0&&clock.ElapsedMilliseconds>=3)break;
                    var tile=batch[i];
                    var edges=BuildEdges(tile);
                    if(!tile.Edges.SequenceEqual(edges))
                    {
                        tile.Edges=edges;
                        tile.Version=++_version;
                    }
                    if(GenerateExploredFill||GenerateUnexploredFill)UpdateFills(tile,BuildFill(tile));
                    _dirtyTiles.Remove(tile);
                }
                
            }
            private void UpdateFills(Tile tile,FillRect[] next)
            {
                if(tile.Fills.SequenceEqual(next))return;
                tile.Fills=next;
                int x0=(int)Math.Floor(tile.X/FillPage.Size),x1=(int)Math.Ceiling((tile.X+tile.W)/FillPage.Size);
                int y0=(int)Math.Floor(tile.Y/FillPage.Size),y1=(int)Math.Ceiling((tile.Y+tile.H)/FillPage.Size);
                for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
                {
                    var key=new Point(x,y);FillPage page;
                    if(!FillPages.TryGetValue(key,out page))FillPages.Add(key,page=new FillPage(key));
                    page.Tiles.Add(tile);
                    ++page.Version;
                }
            }
            private void Calibrate(Tile t,int now)
            {
                if(t.Fog==null)return;
                int before=_informative;
                foreach(var p in _trail)
                {
                    if(unchecked((uint)(now-p.Time))<1000||p.X<t.X||p.Y<t.Y||p.X>=t.X+t.W||p.Y>=t.Y+t.H)continue;
                    string key=t.Key+":"+p.Id;if(_scored.Contains(key))continue;
                    var values=new int[8];for(int i=0;i<8;i++)values[i]=Alpha(t,(p.X-t.X)/t.W,(p.Y-t.Y)/t.H,i);
                    if(values.Max()-values.Min()<100)continue;
                    _scored.Add(key);++_informative;
                    for(int i=0;i<8;i++)_scores[i]+=values[i];
                }
                if(_informative==before)return;
                var order=Enumerable.Range(0,8).OrderByDescending(i=>_scores[i]).ToArray();
                Orientation=_informative>=8 && _scores[order[0]]-_scores[order[1]]>40L*_informative?order[0]:-1;
            }
            internal static int Alpha(Tile t,float x,float y,int orientation)
            {
                if((orientation&4)!=0){float swap=x;x=y;y=swap;}
                if((orientation&1)!=0)x=1-x;if((orientation&2)!=0)y=1-y;
                int ix=Math.Max(0,Math.Min(t.FogWidth-1,(int)(x*t.FogWidth))),iy=Math.Max(0,Math.Min(t.FogHeight-1,(int)(y*t.FogHeight)));
                return t.Fog[iy*t.FogWidth+ix];
            }
            private static bool SameBounds(Tile a,Tile b)
            {
                return a.X==b.X&&a.Y==b.Y&&a.W==b.W&&a.H==b.H;
            }
            internal static bool SameBounds(RectangleF a,RectangleF b)
            {
                return Math.Abs(a.Left-b.Left)<=.1f&&Math.Abs(a.Top-b.Top)<=.1f
                    &&Math.Abs(a.Right-b.Right)<=.1f&&Math.Abs(a.Bottom-b.Bottom)<=.1f;
            }
            private Tile RevealTile(Tile tile)
            {
                // A static placement and its live counterpart can share a footprint.
                // Do not tint the static copy cyan over a mask covering the same area.
                if(tile.Fog!=null)return tile;
                return Tiles.Values.FirstOrDefault(t=>t.Fog!=null&&SameBounds(tile,t))??tile;
            }
            private static int RevealKind(int alpha)
            {
                return alpha<=32?Unexplored:alpha>=192?Explored:Frontier;
            }
            private int Kind(Tile mask,float u,float v)
            {
                if(mask.Fog==null)return Unexplored;
                if(Orientation>=0)return RevealKind(Alpha(mask,u,v,Orientation));
                int kind=RevealKind(Alpha(mask,u,v,0));
                if(kind==Frontier)return Uncertain;
                for(int i=1;i<8;i++)if(RevealKind(Alpha(mask,u,v,i))!=kind)return Uncertain;
                return kind;
            }
            private static bool NearBounds(Tile a,Tile b)
            {
                float dx=a.W/a.Width*.5f,dy=a.H/a.Height*.5f;
                return a.X-dx<=b.X+b.W&&b.X<=a.X+a.W+dx&&a.Y-dy<=b.Y+b.H&&b.Y<=a.Y+a.H+dy;
            }
            private static bool WalkableAt(Tile[] neighbors,float x,float y)
            {
                foreach(var t in neighbors)if(x>=t.X&&y>=t.Y&&x<t.X+t.W&&y<t.Y+t.H)
                {int ix=(int)((x-t.X)/t.W*t.Width),iy=(int)((y-t.Y)/t.H*t.Height);if(t.At(ix,iy))return true;}
                return false;
            }
            private static bool WalksThroughSubscene(Tile[] subscenes,int side,float insideX,float insideY,
                float outsideX,float outsideY)
            {
                foreach(var subscene in subscenes)
                {
                    var bounds=subscene.Bounds;
                    if(!bounds.Contains(insideX,insideY)||bounds.Contains(outsideX,outsideY))continue;
                    // The child must actually open onto this side. A raster cell
                    // rounded up to the edge cannot erase a wall at the join.
                    var border=subscene.WalkBorders?[side^1];
                    if(border==null)continue;
                    int x=(int)((insideX-subscene.X)/subscene.W*subscene.Width);
                    int y=(int)((insideY-subscene.Y)/subscene.H*subscene.Height);
                    if(subscene.At(x,y)&&border[side<2?x:y])return true;
                }
                return false;
            }
            private FillRect[] BuildFill(Tile t,bool connector=false)
            {
                var rects=new List<FillRect>();
                var active=new Dictionary<int,int>();var next=new Dictionary<int,int>();
                float dx=t.W/t.Width,dy=t.H/t.Height;
                var mask=connector?null:RevealTile(t);
                for(int y=0;y<t.Height;y++)
                {
                    next.Clear();int start=0,previous=Uncertain;
                    float v=(y+.5f)/t.Height;
                    for(int x=0;x<=t.Width;x++)
                    {
                        int kind=t.At(x,y)?(connector?Explored:Kind(mask,(x+.5f)/t.Width,v)):Uncertain;
                        if(kind==previous)continue;
                        if((previous==Explored&&(connector||GenerateExploredFill))||(previous==Unexplored&&GenerateUnexploredFill))
                        {
                            int key=(start*(t.Width+1)+x)*2+previous,index;
                            if(active.TryGetValue(key,out index))
                            {var r=rects[index];r.H+=dy;rects[index]=r;}
                            else
                            {
                                index=rects.Count;
                                rects.Add(new FillRect{X=t.X+start*dx,Y=t.Y+y*dy,W=(x-start)*dx,H=dy,Kind=previous});
                            }
                            next[key]=index;
                        }
                        start=x;previous=kind;
                    }
                    var swap=active;active=next;next=swap;
                }
                return rects.ToArray();
            }
            private Edge[] BuildEdges(Tile t)
            {
                var edges=new List<Edge>();float dx=t.W/t.Width,dy=t.H/t.Height;
                var neighbors=Tiles.Values.Where(other=>other!=t&&NearBounds(t,other)).ToArray();
                var bounds=t.Bounds;
                var subscenes=neighbors.Where(other=>(other.W<t.W||other.H<t.H)&&bounds.Contains(other.Bounds)).ToArray();
                var adjacent=bounds;adjacent.Inflate(dx,dy);
                var voids=t.WalkBorders!=null
                    ?_resolvedVoids.Where(r=>adjacent.IntersectsWith(r)).ToArray():Array.Empty<RectangleF>();
                var mask=RevealTile(t);
                for(int side=0;side<4;side++)
                {
                    bool horizontal=side<2;int outer=horizontal?t.Height:t.Width,inner=horizontal?t.Width:t.Height;
                    var border=t.WalkBorders?[side];
                    for(int row=0;row<outer;row++)
                    {
                        int start=-1,kind=-1;
                        for(int col=0;col<=inner;col++)
                        {
                            int current=-1,x=horizontal?col:row,y=horizontal?row:col;
                            if(col<inner&&t.At(x,y))
                            {
                                int nx=x+(side==2?-1:side==3?1:0),ny=y+(side==0?-1:side==1?1:0);
                                bool neighbor=t.At(nx,ny);
                                bool missing=false;
                                if(!neighbor&&(nx<0||ny<0||nx>=t.Width||ny>=t.Height))
                                {
                                    float wx=t.X+(nx+.5f)*dx,wy=t.Y+(ny+.5f)*dy;
                                    neighbor=WalkableAt(neighbors,wx,wy);
                                    if(!neighbor&&border!=null&&border[col])
                                        missing=!ContainsPoint(voids,wx,wy)&&!HasTileCoverage(neighbors,wx,wy);
                                }
                                else if(!neighbor&&subscenes.Length!=0)
                                {
                                    // Composite scenes leave a rectangular cutout for an
                                    // embedded scene. Both maps share these cached edges.
                                    neighbor=WalksThroughSubscene(subscenes,side,t.X+(nx+.5f)*dx,t.Y+(ny+.5f)*dy,
                                        t.X+(2*x-nx+.5f)*dx,t.Y+(2*y-ny+.5f)*dy);
                                }
                                if(!neighbor)
                                {
                                    current=missing?UnknownSeam:Kind(mask,(x+.5f)/t.Width,(y+.5f)/t.Height);
                                    if(current==Uncertain)current=Explored;
                                }
                                else if(Orientation>=0&&mask.Fog!=null&&t.At(nx,ny)
                                    &&Alpha(mask,(x+.5f)/t.Width,(y+.5f)/t.Height,Orientation)>=128
                                    &&Alpha(mask,(nx+.5f)/t.Width,(ny+.5f)/t.Height,Orientation)<128)current=Frontier;
                            }
                            if(current!=kind)
                            {
                                if(start>=0)
                                {
                                    if(horizontal){float yy=t.Y+(row+(side==1?1:0))*dy;edges.Add(new Edge{X1=t.X+start*dx,Y1=yy,X2=t.X+col*dx,Y2=yy,Kind=kind});}
                                    else{float xx=t.X+(row+(side==3?1:0))*dx;edges.Add(new Edge{X1=xx,Y1=t.Y+start*dy,X2=xx,Y2=t.Y+col*dy,Kind=kind});}
                                }
                                kind=current;start=current<0?-1:col;
                            }
                        }
                    }
                }
                return edges.ToArray();
            }
            private static bool ContainsPoint(RectangleF[] bounds,float x,float y)
            {
                foreach(var area in bounds)if(area.Contains(x,y))return true;
                return false;
            }
            private static bool HasTileCoverage(Tile[] tiles,float x,float y)
            {
                foreach(var tile in tiles)if(tile.Bounds.Contains(x,y))return true;
                return false;
            }
        }
        private sealed class MapGeometryFallback
        {
            private const string Api="s7o.MapViewer.v1";
            private readonly IPlugin _owner;
            private readonly Func<string,object[],object> _get;
            private IDictionary<string,object> _status;
            private object[] _scenes;
            private uint _world,_worldSno;
            private int _remaining;
            private bool _requested;
            internal MapGeometryFallback(IPlugin owner,Func<string,object[],object> get){_owner=owner;_get=get;}
            internal void Reset(){_status=null;_scenes=null;_requested=false;_remaining=0;}
            internal void Begin(uint world,uint worldSno)
            {
                Reset();_world=world;_worldSno=worldSno;_remaining=2;
            }
            private static T Field<T>(IDictionary<string,object> reply,string key)
            {
                object value;
                return reply!=null&&reply.TryGetValue(key,out value)&&value is T typed?typed:default(T);
            }
            private bool IsCurrent(IDictionary<string,object> reply)
            {
                return Field<bool>(reply,"ok")&&Field<string>(reply,"api")==Api
                    &&Field<uint>(reply,"worldId")==_world&&Field<uint>(reply,"worldSno")==_worldSno
                    &&!string.IsNullOrEmpty(Field<string>(reply,"session"))
                    &&reply.TryGetValue("epoch",out var value)&&value is long epoch&&epoch>=0;
            }
            private bool SameContext(IDictionary<string,object> reply)
            {
                return IsCurrent(reply)&&Field<long>(reply,"epoch")==Field<long>(_status,"epoch")
                    &&Field<string>(reply,"session")==Field<string>(_status,"session");
            }
            private IDictionary<string,object> Query(string method,params object[] args)
            {
                try{return _get(method,args) as IDictionary<string,object>;}
                catch(Exception){return null;}
            }
            internal NativeMapSource.NavRect[] Cells(Tile tile)
            {
                if(!_owner.Enabled||_worldSno==0||_remaining<=0)return null;
                if(!_requested)
                {
                    _requested=true;
                    _status=Query("status");
                    if(IsCurrent(_status))
                    {
                        var reply=Query("scenes",Field<long>(_status,"epoch"));
                        if(SameContext(reply))_scenes=Field<object[]>(reply,"scenes");
                    }
                }
                if(_scenes==null)return null;
                foreach(var raw in _scenes)
                {
                    var row=raw as IDictionary<string,object>;
                    if(Field<uint>(row,"sno")!=tile.Sno||!Field<bool>(row,"templateAvailable")
                        ||Field<bool>(row,"originConflict"))continue;
                    var b=Field<float[]>(row,"xyBounds");var origin=Field<float[]>(row,"origin");
                    if(b==null||b.Length!=4||origin==null||origin.Length!=3
                        ||!Matches(b[0],tile.X)||!Matches(b[1],tile.Y)||!Matches(b[2],tile.X+tile.W)||!Matches(b[3],tile.Y+tile.H)
                        ||!Matches(origin[0],tile.X)||!Matches(origin[1],tile.Y))continue;
                    uint scene=Field<uint>(row,"sceneId");
                    if(scene==0)continue;
                    --_remaining;
                    var reply=Query("cells",Field<long>(_status,"epoch"),scene);
                    if(!SameContext(reply)||Field<uint>(reply,"sceneId")!=scene)return null;
                    var cells=Field<object[]>(reply,"cells");
                    if(cells==null||cells.Length>16384)return null;
                    var result=new List<NativeMapSource.NavRect>();
                    foreach(var cell in cells)
                    {
                        var value=cell as IDictionary<string,object>;
                        object flag;
                        if(value==null||!value.TryGetValue("walk",out flag)||!(flag is bool walk))return null;
                        if(!walk)continue;
                        var xy=Field<float[]>(value,"bounds");
                        if(xy==null||xy.Length!=6)return null;
                        result.Add(new NativeMapSource.NavRect{MinX=xy[0]-origin[0],MinY=xy[1]-origin[1],
                            MaxX=xy[3]-origin[0],MaxY=xy[4]-origin[1]});
                    }
                    return result.ToArray();
                }
                return null;
            }
            private static bool Matches(float a,float b){return Math.Abs(a-b)<=.1f;}
        }
        // Scene and reveal data reuse the host's collected records and bounded reads.
        // Marker replay temporarily maps the managed renderer, restoring it in finally.
        // No game-memory writes or renderer-event hooks are used.
        private sealed class NativeMapSource
        {
            private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
            private object _overlay;
            private FieldInfo _target,_collector,_cache,_sno,_world,_x,_y,_matrix,_scale;
            private MethodInfo _scenes,_items,_reader,_read;
            private readonly Dictionary<string,Record> _rows=new Dictionary<string,Record>();
            private readonly byte[] _rowScratch=new byte[88];
            private byte[] _maskScratch=Array.Empty<byte>();
            private readonly Dictionary<uint,NavRect[]> _navCache=new Dictionary<uint,NavRect[]>();
            private readonly Dictionary<Type,NavReader> _navReaders=new Dictionary<Type,NavReader>();
            private readonly Dictionary<Type,StaticReader> _staticReaders=new Dictionary<Type,StaticReader>();
            private Type _worldType;private Func<object,IEnumerable> _worldScenes;
            private bool? _knownSceneCatalog;
            
            
            
            
            internal struct NavRect {internal float MinX,MinY,MaxX,MaxY;}
            internal bool VerifiedNonMappable(ISnoScene scene)
            {
                string code;
                switch(scene.Sno)
                {
                    case 245619:code="x1_BogCave_Buffer_Template";break;
                    case 31129:code="a3dun_keep_FillScene_01";break;
                    case 172880:code="a3dun_vendors_A_W01_S02";break;
                    case 172868:code="a3dun_vendors_A_W01_S03";break;
                    default:return false;
                }
                if(!string.Equals(scene.Code,code,StringComparison.OrdinalIgnoreCase))return false;
                return HasVerifiedCatalog();
            }
            private bool HasVerifiedCatalog()
            {
                if(!_knownSceneCatalog.HasValue)
                {
                    _knownSceneCatalog=false;
                    try
                    {
                        using(var file=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","scene.bin")))
                        using(var hash=System.Security.Cryptography.SHA256.Create())
                            _knownSceneCatalog=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","")
                                =="B4C7AAF87A98854348FFA81FACF692100E374C71795B9C496E5CD7EE058579AC";
                    }
                    catch(IOException){}
                    catch(UnauthorizedAccessException){}
                }
                return _knownSceneCatalog.Value;
            }
            // The host loads concrete SNO types, but does not reference their assembly when
            // compiling plugins. Cache runtime accessors instead of naming those types here.
            private static Func<object,T> Property<T>(Type type,string name)
            {
                var p=type.GetProperty(name,All);
                if(p==null||p.GetGetMethod(true)==null||p.PropertyType!=typeof(T))throw new InvalidOperationException("Missing native property: "+name);
                var arg=System.Linq.Expressions.Expression.Parameter(typeof(object),"item");
                return System.Linq.Expressions.Expression.Lambda<Func<object,T>>(
                    System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Convert(arg,type),p),arg).Compile();
            }
            private sealed class NavReader
            {
                internal Action<object> Load;internal Func<object,Array> Cells;
                internal Func<object,float> MinX,MinY,MaxX,MaxY;
                internal static NavReader Build(Type type)
                {
                    var method=type.GetMethod("LoadNavCells",All,null,Type.EmptyTypes,null);
                    var field=type.GetField("NavCells",All);
                    if(method==null||method.IsStatic||method.ReturnType!=typeof(void)||field==null||field.IsStatic||!field.FieldType.IsArray)
                        throw new InvalidOperationException("Native navigation layout changed");
                    var arg=System.Linq.Expressions.Expression.Parameter(typeof(object),"scene");
                    var value=System.Linq.Expressions.Expression.Convert(arg,type);
                    var cell=field.FieldType.GetElementType();
                    return new NavReader {
                        Load=System.Linq.Expressions.Expression.Lambda<Action<object>>(System.Linq.Expressions.Expression.Call(value,method),arg).Compile(),
                        Cells=System.Linq.Expressions.Expression.Lambda<Func<object,Array>>(System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Field(value,field),typeof(Array)),arg).Compile(),
                        MinX=Property<float>(cell,"MinX"),MinY=Property<float>(cell,"MinY"),MaxX=Property<float>(cell,"MaxX"),MaxY=Property<float>(cell,"MaxY") };
                }
            }
            private sealed class StaticReader
            {
                internal Func<object,ISnoScene> Scene;internal Func<object,ISnoArea> Area;
                internal Func<object,IEnumerable> Candidates;
                internal Func<object,float> X,Y,W,H;
                internal static StaticReader Build(Type type)
                {
                    var result=new StaticReader {Area=Property<ISnoArea>(type,"SnoArea"),
                        X=Property<float>(type,"PosX"),Y=Property<float>(type,"PosY"),W=Property<float>(type,"W"),H=Property<float>(type,"H")};
                    if(type.GetProperty("SnoScene",All)!=null)result.Scene=Property<ISnoScene>(type,"SnoScene");
                    var candidates=type.GetProperty("SnoScenes",All);
                    if(candidates!=null&&typeof(IEnumerable).IsAssignableFrom(candidates.PropertyType))
                    {
                        var arg=System.Linq.Expressions.Expression.Parameter(typeof(object),"slot");
                        result.Candidates=System.Linq.Expressions.Expression.Lambda<Func<object,IEnumerable>>(
                            System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(
                                System.Linq.Expressions.Expression.Convert(arg,type),candidates),typeof(IEnumerable)),arg).Compile();
                    }
                    return result;
                }
            }
            internal NavRect[] NavCells(ISnoScene scene)
            {
                NavRect[] saved;if(_navCache.TryGetValue(scene.Sno,out saved))return saved;
                var type=scene.GetType();NavReader reader;
                if(!_navReaders.TryGetValue(type,out reader))_navReaders[type]=reader=NavReader.Build(type);
                reader.Load(scene);var cells=reader.Cells(scene);
                if(cells==null)return null;
                saved=new NavRect[cells.Length];int at=0;
                foreach(var cell in cells)saved[at++]=new NavRect {MinX=reader.MinX(cell),MinY=reader.MinY(cell),MaxX=reader.MaxX(cell),MaxY=reader.MaxY(cell)};
                if(_navCache.Count>=512)_navCache.Clear();_navCache[scene.Sno]=saved;return saved;
            }
            private struct Record{internal long Address;internal int Width,Height;}
            internal sealed class Placement
            {
                internal ISnoScene SnoScene;internal float PosX,PosY,W,H;internal bool Selected;
                
            }
            internal struct Mask{internal byte[] Bytes;internal int Width,Height;}
            internal RenderTarget RenderTarget {get{return _target.GetValue(_overlay) as RenderTarget;}}
            internal Matrix3x2 MapMatrix {get{return (Matrix3x2)_matrix.GetValue(_overlay);}set{_matrix.SetValue(_overlay,value);}}
            internal float MapScale {get{return (float)_scale.GetValue(_overlay);}set{_scale.SetValue(_overlay,value);}}
            internal static NativeMapSource Bind(IRenderController render)
            {
                var map=render.GetType().GetInterfaceMap(typeof(IRenderController));
                int slot=Array.FindIndex(map.InterfaceMethods,m=>m.Name=="get_MinimapScale");
                if(slot<0)throw new InvalidOperationException("Missing renderer getter");
                var method=map.TargetMethods[slot];
                if(method.Module.ModuleVersionId!=new Guid("1db46bb6-50fb-4bcc-b806-403966e9f0c6"))throw new InvalidOperationException("Unreviewed native data layout");
                var il=method.GetMethodBody().GetILAsByteArray();
                if(il.Length!=11||il[0]!=0x7e||il[5]!=0x7b||il[10]!=0x2a)throw new InvalidOperationException("Renderer getter changed");
                var instance=method.Module.ResolveField(BitConverter.ToInt32(il,1));
                var result=new NativeMapSource{_overlay=instance.GetValue(null)};
                if(result._overlay==null)throw new InvalidOperationException("Renderer not initialized");
                result._target=Need(result._overlay.GetType(),"renderTarget_0",typeof(RenderTarget),false);
                result._matrix=Need(result._overlay.GetType(),"matrix3x2_0",typeof(Matrix3x2),false);
                result._scale=Need(result._overlay.GetType(),"float_3",typeof(float),false);
                var assembly=method.Module.Assembly;
                result._collector=assembly.GetType("work.CoreCollector",true).GetField("class374_0",All);
                var ct=result._collector.FieldType;
                result._cache=ct.GetField("class352_0",All);
                result._scenes=ct.GetMethod("method_0",All,null,new[]{typeof(uint)},null);
                result._items=result._cache.FieldType.GetMethod("GetItems",All,null,Type.EmptyTypes,null);
                var row=assembly.GetType("structs.r_SceneRevealInfo",true);
                result._sno=Need(row,"uint_0",typeof(uint),false);result._world=Need(row,"uint_2",typeof(uint),false);
                result._x=Need(row,"float_0",typeof(float),false);result._y=Need(row,"float_1",typeof(float),false);
                if((int)row.GetField("int_0",All).GetRawConstantValue()!=88)throw new InvalidOperationException("Reveal row size changed");
                result._reader=assembly.GetType("work.MR",true).GetMethod("get_Class117_0",All,null,Type.EmptyTypes,null);
                result._read=assembly.GetType("MemoryReader",true).GetMethod("ReadMem",All,null,new[]{typeof(long),typeof(object),typeof(int),typeof(bool)},null);
                if(result._scenes==null||result._items==null||result._reader==null||result._read==null||result._read.ReturnType!=typeof(bool))throw new InvalidOperationException("Read-only map service unavailable");
                return result;
            }
            private static FieldInfo Need(Type type,string name,Type expected,bool isStatic)
            {var f=type.GetField(name,All);if(f==null||f.IsStatic!=isStatic||f.FieldType!=expected)throw new InvalidOperationException("Native field changed: "+name);return f;}
            internal IEnumerable<IScene> Scenes(uint world)
            {return _scenes.Invoke(_collector.GetValue(null),new object[]{world}) as IEnumerable<IScene> ?? Enumerable.Empty<IScene>();}
            internal IEnumerable<Placement> Placements(IPlayer me)
            {
                string group=me.SnoArea==null?null:me.SnoArea.AreaGroupInWorld;
                bool rift=me.SnoArea!=null&&(me.SnoArea.Code??"").StartsWith("x1_lr_level_",StringComparison.OrdinalIgnoreCase);
                uint worldSno=me.SnoWorld?.Sno??0;
                
                
                foreach(var s in Scenes(me.WorldId))if(s!=null&&s.SnoScene!=null&&(worldSno==0||s.WorldSno==worldSno)
                    &&(rift||s.SnoArea==null||string.IsNullOrEmpty(group)||string.IsNullOrEmpty(s.SnoArea.AreaGroupInWorld)||s.SnoArea.AreaGroupInWorld==group))
                {
                    
                    yield return new Placement{SnoScene=s.SnoScene,PosX=s.PosX,PosY=s.PosY,W=s.W,H=s.H,Selected=true };
                }
                // Same static SNO placements used by FreeHUD's own reveal rasterizer.
                // These are floor-plan assets, not a source of unloaded live monsters.
                var world=me.SnoWorld;if(world==null)yield break;
                if(_worldType!=world.GetType())
                {
                    var type=world.GetType();var field=type.GetField("SceneInfos",All);
                    if(field==null||field.IsStatic||!typeof(IEnumerable).IsAssignableFrom(field.FieldType))throw new InvalidOperationException("Native world placements changed");
                    var arg=System.Linq.Expressions.Expression.Parameter(typeof(object),"world");
                    _worldScenes=System.Linq.Expressions.Expression.Lambda<Func<object,IEnumerable>>(System.Linq.Expressions.Expression.Convert(
                        System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Convert(arg,type),field),typeof(IEnumerable)),arg).Compile();
                    _worldType=type;
                }
                var infos=_worldScenes(world);if(infos==null)yield break;
                foreach(var raw in infos)
                {
                    if(raw==null)continue;var type=raw.GetType();StaticReader reader;
                    if(!_staticReaders.TryGetValue(type,out reader))_staticReaders[type]=reader=StaticReader.Build(type);
                    var area=reader.Area(raw);
                    if(!rift&&area!=null&&!string.IsNullOrEmpty(group)&&!string.IsNullOrEmpty(area.AreaGroupInWorld)&&area.AreaGroupInWorld!=group)
                    {  continue; }
                    var scene=reader.Scene==null?null:reader.Scene(raw);
                    if(scene==null&&reader.Candidates!=null)
                    {
                        var candidates=reader.Candidates(raw);
                        var options=candidates==null?Array.Empty<ISnoScene>():candidates.Cast<object>().OfType<ISnoScene>().Take(2).ToArray();
                        if(options.Length==1)scene=options[0];
                    }
                    float x=reader.X(raw),y=reader.Y(raw),w=reader.W(raw),h=reader.H(raw);
                    if(!MapModel.Finite(x)||!MapModel.Finite(y)||w<=0||h<=0||w>1500||h>1500)continue;
                    if(scene==null)
                    {
                        
                        continue;
                    }
                    
                    yield return new Placement{SnoScene=scene,PosX=x,PosY=y,W=w,H=h,Selected=reader.Scene!=null};
                }
            }
            internal void Refresh(uint world)
            {
                
                _rows.Clear();var collector=_collector.GetValue(null);if(collector==null)return;
                var cache=_cache.GetValue(collector);if(cache==null)return;
                var items=_items.Invoke(cache,null) as IEnumerable;if(items==null)return;int count=0;
                foreach(var row in items)
                {
                    if(++count>4096)break;if((uint)_world.GetValue(row)!=world)continue;
                    
                    float x=(float)_x.GetValue(row),y=(float)_y.GetValue(row);if(!MapModel.Finite(x)||!MapModel.Finite(y))continue;
                    var pin=System.Runtime.InteropServices.GCHandle.Alloc(row,System.Runtime.InteropServices.GCHandleType.Pinned);
                    try{System.Runtime.InteropServices.Marshal.Copy(pin.AddrOfPinnedObject(),_rowScratch,0,_rowScratch.Length);}finally{pin.Free();}
                    long address=BitConverter.ToInt64(_rowScratch,40);int w=BitConverter.ToInt32(_rowScratch,48),h=BitConverter.ToInt32(_rowScratch,52);
                    if(address<0x10000||address>=0x0000800000000000L||(address&7)!=0||w<8||h<8||w>2048||h>2048||(long)w*h>131072)continue;
                    _rows[MapModel.Key((uint)_sno.GetValue(row),x,y)]=new Record{Address=address,Width=w,Height=h};
                }
                
                
            }
            internal bool HasMask(string key){return _rows.ContainsKey(key);}
            internal bool TryMask(string key,out Mask mask)
            {
                mask=default(Mask);Record r;if(!_rows.TryGetValue(key,out r))return false;
                int length=r.Width*r.Height;
                
                if(_maskScratch.Length<length)_maskScratch=new byte[length];
                var reader=_reader.Invoke(null,null);if(reader==null)return false;
                try
                {
                    if(!(bool)_read.Invoke(reader,new object[]{r.Address,_maskScratch,length,false}))
                    {return false;}
                }
                catch(Exception){return false;}
                mask=new Mask{Bytes=_maskScratch,Width=r.Width,Height=r.Height};return true;
                
            }
        }
    }
}
