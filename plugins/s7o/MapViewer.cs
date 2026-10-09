// MapViewer: read-only map service + diagnostic overlay. It never moves/clicks the character.
// Pipeline: current local CASC -> build-specific local templates -> live XYZ placement -> GET queries.
// Contract: unknown is not a wall; terrain Walk and placed collision boxes are distinct layers. Neither guarantees Dash passage.
// Native posed boxes use marker/live matches. Unmatched live props use labelled native rotation envelopes.
// Routes are advisory and never replace native movement recovery. No model bounds are precise solids.
// NativeGrid stores source heights; entities retain FreeHUD XYZ. Player Z is never a terrain-height fallback.
// Keep cache decoding, live placement, queries and drawing isolated. See MapViewerData/README.md for the API and maintenance.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;
using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class s7o_MapViewer : BasePlugin, IAfterCollectHandler, IInGameWorldPainter,
        IEnumerable<KeyValuePair<string, Func<string, object[], object>>>
    {
        public const string BridgeName = "s7o.MapViewer.v1";
        public string DiabloInstallPath = ""; // Optional override; otherwise discover the running game.
        public string CacheDirectory = ""; // Default: <TurboHUD>/plugins/s7o/MapViewerData.
        public float InteriorGridSpacing = 5f; // Display only; native boundaries stay exact.
        public bool ShowGrid = false; // Display master only; GET geometry stays active.
        public bool ShowWorldCells = true;
        public bool ShowMinimapCells = true;
        public bool ShowObstacleCandidates = true;
        public bool ShowUnknownCandidates = false; // Optional diagnostics; unknown footprints remain in GET data.
        public bool ShowReportedRadiusCandidates = false; // Optional diagnostics only; reported radii are not collision geometry.
        public bool ShowNativeNoWalkCells = false; // Optional diagnostic only. Default terrain without Walk permission stays blank.
        public bool Diagnostics = false; // State changes and bounded two-second summaries; no per-frame log.
        public float DrawRadius = 60f;
        public int MaximumDrawCells = 1200;
        public float HeightPreloadRadius = 120f; // Service availability is independent of drawing settings.
        public bool ShowHeightSamplePreview = false; // Optional height preview; native cell boundaries are the default.
        public int NativeGridDrawStride = 2; // Preview spacing only; never decimate cell boundaries.

        private const int MaximumPlacements = 128;
        private const int MaximumActorScan = 2048;
        private const int MaximumQueryCells = 16384;
        private const float GroundFootprintLiftTolerance = 1f; // Ground projection policy only; never modify native box dimensions.
        private const float FloorZTolerance = 0.15f; // Observed floor-coordinate offset; not a stair/step allowance.
        private readonly string _session = Guid.NewGuid().ToString("N");
        private readonly Dictionary<uint, Placement> _placements = new Dictionary<uint, Placement>();
        private readonly List<Obstacle> _obstacles = new List<Obstacle>();
        private volatile TemplateCache _cache;
        private volatile string _cacheState = "NotLoaded";
        private TemplateCache _placedCache;
        private int _workerPending;
        private string _checkedCacheSignature = "";
        private long _lastCacheCheck, _lastCollectMs, _sampleMs, _epoch, _version;
        private int _lastTick;
        private uint _worldId, _worldSno, _currentSceneId;
        private float _playerX, _playerY, _playerZ;
        private bool _valid;
        private Func<uint,List<IScene>> _loadedSceneReader;
        private Func<uint,uint,IScene> _loadedSceneFind;
        private long _lastLoadedSceneStamp,_lastSceneBindStamp;
        private string _loadedSceneState="NotBound";
        private int _loadedSceneCount,_loadedSceneNearby;
        private long _suspendedTimestamp = -1; // Short sampling interruptions only; never retain an unverified session indefinitely.
        private string _lastLoggedState = "";
        private long _lastHeightLogMs;
        private string _lastPlacementChange = "none";
        private readonly List<TargetSample> _targets = new List<TargetSample>();
        private readonly object _heightGate = new object();
        private readonly Queue<GridRequest> _heightRequests = new Queue<GridRequest>();
        private bool _heightWorkerRunning;
        private int _lastNativeNodesDrawn, _lastNativeCellsDrawn, _lastNoWalkCellsDrawn;
        private readonly List<NativeBox> _nativeBoxes = new List<NativeBox>();
        private readonly List<Obstacle> _footprintCandidates = new List<Obstacle>();
        private readonly List<Interval> _edgeCuts = new List<Interval>();
        private IBrush _walkBrush, _noWalkBrush, _obstacleBrush, _unknownBrush;

        // Reusable templates contain local coordinates only. No player/world identity belongs here.
        private struct Cell
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public ushort Flags, NeighbourCount;
            public int NeighbourIndex;
        }
        private sealed class Template
        {
            public uint Sno;
            public string Code, ContentHash, AssetHash;
            public Cell[] Cells;
            public int GridWidth,GridHeight,GridRawBytes,GridCompressedBytes;
            public float StepX,StepY;
            public long GridOffset;
            public byte[] GridHash;
            public volatile NativeGrid Grid;
            public volatile string GridState = "Unavailable";
            public bool GridQueued;
        }
        private sealed class TemplateCache
        {
            public string BuildInfoHash, BuildKey, RootKey, GameVersion;
            public readonly Dictionary<uint, Template> Templates = new Dictionary<uint, Template>();
            public int CellCount;
            public string ShapeState="Missing";
            public readonly Dictionary<uint,BoxDefinition> BoxDefinitions=new Dictionary<uint,BoxDefinition>();
            public readonly Dictionary<uint,BoxPose[]> BoxPoses=new Dictionary<uint,BoxPose[]>();
            public string Path;
            public readonly Queue<Template> LoadedHeights = new Queue<Template>();
            public long HeightBytes;
        }
        private sealed class Placement
        {
            public uint SceneId, NavMeshId, Sno;
            public string Code;
            public float X, Y, Z, MaxX, MaxY;
            public Template Template;
            public long SeenVersion;
            public bool OriginConflict;
            public string Source;
        }
        private struct Obstacle
        {
            public uint AcdId, Sno, SceneId;
            public string Kind, Code, RadiusSource;
            public float X, Y, Z, FloorX, FloorY, FloorZ, Radius;
            public bool Operated, Clickable, FloorZKnown;
        }
        private sealed class BoxDefinition
        {
            public uint Sno;public string Code,Kind,ContentHash,AssetHash;public int Flags;public bool Cylinder;
            public float X,Y,Z,HX,HY,HZ,BaseRadius;
        }
        private struct BoxPose { public uint Sno;public float X,Y,Z,Cos,Sin; }
        private struct NativeBox
        {
            public BoxDefinition Definition;public uint SceneId,AcdId;public string Presence;
            public float X,Y,MinZ,MaxZ,HX,HY,Cos,Sin,FloorZ;
        }
        private struct Interval { public double Start, End; }
        private sealed class NativeGrid { public float[] Heights;public uint[] Flags; }
        private sealed class GridRequest { public TemplateCache Cache;public Template Template; }
        private struct TargetSample { public uint AcdId,SceneId;public float X,Y,Z;public bool Elite; }


        public s7o_MapViewer() { Enabled = true; Order = 552; }
        public override void Load(IController hud)
        {
            base.Load(hud);
            if(String.IsNullOrEmpty(CacheDirectory)) {
                CacheDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plugins","s7o","MapViewerData");
            }
            LoadControls();
            _walkBrush=Hud.Render.CreateBrush(135,55,220,180,1f);
            _obstacleBrush=Hud.Render.CreateBrush(190,255,165,65,1.5f);
            _noWalkBrush=Hud.Render.CreateBrush(165,245,75,75,1.2f);
            _unknownBrush=Hud.Render.CreateBrush(190,230,100,230,1.5f);
            ScheduleCacheCheck();
        }

        // No shared plugin-specific interface: optional consumers discover this BCL delegate once.
        // This leaves consumers compilable if MapViewer is removed. GET never reads files or sends input.
        public IEnumerator<KeyValuePair<string, Func<string, object[], object>>> GetEnumerator()
        {
            yield return new KeyValuePair<string, Func<string, object[], object>>(BridgeName, Get);
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        // Cache I/O stays off the HUD thread. The worker never calls any Hud/API member.
        // A new game build invalidates this cache; retaining yesterday's cells is never a fallback.
        private void ScheduleCacheCheck()
        {
            if(Interlocked.CompareExchange(ref _workerPending,1,0)!=0)return;
            string install=DiabloInstallPath, directory=CacheDirectory;
            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    install=DiscoverGameInstall(install);
                    string hash=CurrentBuildKey(install);
                    string path=Path.Combine(directory,"MapViewer.nav");var stamp=new FileInfo(path);var propsStamp=new FileInfo(Path.Combine(directory,"MapViewer.props"));
                    string signature=hash+":"+path+":"+(stamp.Exists?stamp.Length+":"+stamp.LastWriteTimeUtc.Ticks:"missing")+":"+(propsStamp.Exists?propsStamp.Length+":"+propsStamp.LastWriteTimeUtc.Ticks:"propsMissing");
                    if(signature==_checkedCacheSignature)return; // Do not reread a large unchanged failed cache every five seconds.
                    _checkedCacheSignature=signature;_cache=null;_cacheState="Loading";
                    TemplateCache next=ReadCache(path);
                    if(!String.Equals(next.BuildKey,hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("BuildMismatch: install updated MapViewerData for this game build");
                    try {ReadProps(Path.Combine(directory,"MapViewer.props"),next);}catch(Exception ex){next.BoxDefinitions.Clear();next.BoxPoses.Clear();next.ShapeState="Unavailable: "+ex.Message;}
                    if(CurrentBuildKey(install)!=hash)throw new InvalidDataException("BuildChangedDuringRead");
                    _cache=next; _cacheState="Ready";
                } catch(Exception ex) { _cache=null; _cacheState=ex.GetType().Name+": "+ex.Message; }
                finally { Interlocked.Exchange(ref _workerPending,0); }
            });
        }
        // Build keys identify content across install locations and languages.
        private static string CurrentBuildKey(string install)
        {
            var rows=File.ReadAllLines(Path.Combine(install,".build.info"));
            if(rows.Length<2)throw new InvalidDataException("BuildInfoEmpty");
            string[] head=rows[0].Split('|');int active=-1,key=-1;
            for(int i=0;i<head.Length;i++) {
                if(head[i].StartsWith("Active!",StringComparison.Ordinal))active=i;
                if(head[i].StartsWith("Build Key!",StringComparison.Ordinal))key=i;
            }
            if(active<0||key<0)throw new InvalidDataException("BuildInfoColumns");
            for(int i=1;i<rows.Length;i++) {
                string[] row=rows[i].Split('|');
                if(row.Length<=Math.Max(active,key)||row[active]!="1")continue;
                string value=row[key].Trim().ToLowerInvariant();
                if(value.Length!=32)throw new InvalidDataException("BuildKey");
                foreach(char c in value)if(!Uri.IsHexDigit(c))throw new InvalidDataException("BuildKey");
                return value;
            }
            throw new InvalidDataException("NoActiveBuild");
        }
        private static string DiscoverGameInstall(string configured)
        {
            if(!String.IsNullOrEmpty(configured))return configured;
            foreach(string name in new[]{"Diablo III64","Diablo III"}) {
                foreach(var process in Process.GetProcessesByName(name))using(process) {
                    try {
                        string path=Path.GetDirectoryName(process.MainModule.FileName);
                        for(int i=0;i<4&&!String.IsNullOrEmpty(path);i++) {
                            if(File.Exists(Path.Combine(path,".build.info")))return path;
                            var parent=Directory.GetParent(path);path=parent==null?null:parent.FullName;
                        }
                    }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                }
            }
            throw new InvalidDataException("GameInstallNotAvailable: start Diablo III");
        }


        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant(); }
        private static string FileHash(string path)
        {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var hash=SHA256.Create())return Hex(hash.ComputeHash(file));
        }
        private static string ReadString(BinaryReader reader)
        {
            // BinaryWriter string format with a bounded allocation before UTF-8 decoding.
            int length=0,shift=0;
            while(true) {
                byte b=reader.ReadByte();if(shift>21)throw new InvalidDataException("StringLengthEncoding");
                length|=(b&127)<<shift;shift+=7;if((b&128)==0)break;
            }
            if(length<0||length>2048)throw new InvalidDataException("StringLengthLimit");
            byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();
            return new UTF8Encoding(false,true).GetString(bytes);
        }
        private static bool Finite(float value) { return !Single.IsNaN(value)&&!Single.IsInfinity(value)&&Math.Abs(value)<1000000; }
        private static TemplateCache ReadCache(string path)
        {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
                if(file.Length<128||file.Length>128L*1024*1024+32)throw new InvalidDataException("CacheSizeLimit");
                long payload=file.Length-32;byte[] buffer=new byte[32768],actual;
                using(var hash=SHA256.Create()) {
                    long remaining=payload;
                    while(remaining>0) {
                        int read=file.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));
                        if(read==0)throw new EndOfStreamException();hash.TransformBlock(buffer,0,read,buffer,0);remaining-=read;
                    }
                    hash.TransformFinalBlock(new byte[0],0,0);actual=hash.Hash;
                }
                byte[] expected=new byte[32];if(file.Read(expected,0,32)!=32||Hex(actual)!=Hex(expected))throw new InvalidDataException("CacheChecksum");
                file.Position=0;
                using(var reader=new BinaryReader(file,Encoding.UTF8)) {
                    string magic=Encoding.ASCII.GetString(reader.ReadBytes(8));int schema=reader.ReadInt32();
                    if(!((magic=="S7OMAP01"&&schema==1)||(magic=="S7OMAP02"&&schema==2)))throw new InvalidDataException("CacheSchema");
                    var cache=new TemplateCache { BuildInfoHash=ReadString(reader),BuildKey=ReadString(reader),RootKey=ReadString(reader),GameVersion=ReadString(reader),Path=path };
                    if(cache.BuildInfoHash.Length!=64||cache.BuildKey.Length!=32||cache.RootKey.Length!=32)throw new InvalidDataException("CacheBuildIdentity");
                    int count=reader.ReadInt32();if(count<1||count>30000)throw new InvalidDataException("TemplateCountLimit");
                    for(int i=0;i<count;i++) {
                        var template=new Template { Sno=reader.ReadUInt32(),Code=ReadString(reader),ContentHash=ReadString(reader),AssetHash=ReadString(reader) };
                        int cells=reader.ReadInt32();
                        if(template.Sno==0||template.Code.Length==0||template.ContentHash.Length!=32||template.AssetHash.Length!=64
                            ||cells<1||cells>65536||cache.CellCount+cells>1000000||file.Position+(long)cells*32>payload)
                            throw new InvalidDataException("TemplateIdentityOrCellsLimit");
                        template.Cells=new Cell[cells];
                        for(int j=0;j<cells;j++) {
                            var cell=new Cell { MinX=reader.ReadSingle(),MinY=reader.ReadSingle(),MinZ=reader.ReadSingle(),
                                MaxX=reader.ReadSingle(),MaxY=reader.ReadSingle(),MaxZ=reader.ReadSingle(),Flags=reader.ReadUInt16(),
                                NeighbourCount=reader.ReadUInt16(),NeighbourIndex=reader.ReadInt32() };
                            if(!Finite(cell.MinX)||!Finite(cell.MinY)||!Finite(cell.MinZ)||!Finite(cell.MaxX)||!Finite(cell.MaxY)||!Finite(cell.MaxZ)
                                ||cell.MinX>=cell.MaxX||cell.MinY>=cell.MaxY||cell.MinZ>cell.MaxZ)throw new InvalidDataException("CellBounds");
                            template.Cells[j]=cell;
                        }
                        if(schema==2) {
                            template.GridWidth=reader.ReadInt32();template.GridHeight=reader.ReadInt32();
                            template.StepX=reader.ReadSingle();template.StepY=reader.ReadSingle();
                            template.GridRawBytes=reader.ReadInt32();template.GridCompressedBytes=reader.ReadInt32();
                            template.GridHash=reader.ReadBytes(16);template.GridOffset=file.Position;
                            bool empty=template.GridWidth==0&&template.GridHeight==0&&template.GridRawBytes==0&&template.GridCompressedBytes==0;
                            if(!empty&&(template.GridWidth<1||template.GridHeight<1||template.GridWidth>512||template.GridHeight>512
                                ||template.GridRawBytes!=template.GridWidth*template.GridHeight*8||!Finite(template.StepX)||!Finite(template.StepY)
                                ||template.StepX<=0||template.StepY<=0||template.StepX>20||template.StepY>20
                                ||template.GridCompressedBytes<1||template.GridCompressedBytes>3*1024*1024))throw new InvalidDataException("HeightGridDescriptor");
                            if(template.GridHash.Length!=16||file.Position+template.GridCompressedBytes>payload)throw new InvalidDataException("HeightGridRange");
                            file.Position+=template.GridCompressedBytes;template.GridState=empty?"Unavailable":"NotLoaded";
                        }
                        cache.Templates.Add(template.Sno,template);cache.CellCount+=cells;
                    }
                    if(file.Position!=payload)throw new InvalidDataException("CacheTrailingData");return cache;
                }
            }
        }


        // Props are independently checksummed/build-bound. A missing/unsupported layer never turns into guessed holes.
        private static void ReadProps(string path,TemplateCache cache)
        {
            if(!File.Exists(path))return;
            var info=new FileInfo(path);if(info.Length<128||info.Length>16*1024*1024)throw new InvalidDataException("PropsSize");
            byte[] bytes=File.ReadAllBytes(path);if(bytes.Length!=info.Length)throw new InvalidDataException("PropsChangedDuringRead");
            int payload=bytes.Length-32;byte[] digest;using(var h=SHA256.Create())digest=h.ComputeHash(bytes,0,payload);
            for(int i=0;i<32;i++)if(digest[i]!=bytes[payload+i])throw new InvalidDataException("PropsChecksum");
            using(var stream=new MemoryStream(bytes,0,payload,false))using(var r=new BinaryReader(stream,Encoding.UTF8)) {
                if(Encoding.ASCII.GetString(r.ReadBytes(8))!="S7OPRP01"||r.ReadInt32()!=1)throw new InvalidDataException("PropsSchema");
                if(ReadString(r)!=cache.BuildInfoHash||ReadString(r)!=cache.BuildKey||ReadString(r)!=cache.GameVersion)throw new InvalidDataException("PropsBuildMismatch");
                int n=r.ReadInt32();if(n<0||n>4096)throw new InvalidDataException("PropsDefinitionLimit");
                for(int i=0;i<n;i++) {
                    var d=new BoxDefinition {Sno=r.ReadUInt32(),Code=ReadString(r),ContentHash=ReadString(r),AssetHash=ReadString(r),Kind=ReadString(r),Flags=r.ReadInt32(),
                        X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle(),HX=r.ReadSingle(),HY=r.ReadSingle(),HZ=r.ReadSingle(),BaseRadius=r.ReadSingle()};
                    if(d.Sno==0||d.ContentHash.Length!=32||d.AssetHash.Length!=64||(d.Flags&1)==0||!Finite(d.X)||!Finite(d.Y)||!Finite(d.Z)
                        ||!Finite(d.HX)||!Finite(d.HY)||!Finite(d.HZ)||d.HX<=0||d.HY<=0||d.HZ<=0||d.HX>50||d.HY>50||d.HZ>200
                        ||!Finite(d.BaseRadius)||d.BaseRadius<0)throw new InvalidDataException("PropsBoxDefinition");
                    if(d.Kind.EndsWith(":Cylinder",StringComparison.Ordinal)) {d.Cylinder=true;d.Kind=d.Kind.Substring(0,d.Kind.Length-9);}
                    cache.BoxDefinitions.Add(d.Sno,d);
                }
                n=r.ReadInt32();if(n<0||n>30000)throw new InvalidDataException("PropsSceneLimit");int total=0;
                for(int i=0;i<n;i++) {
                    uint sno=r.ReadUInt32();int count=r.ReadInt32();if(sno==0||count<0||count>4096||(total+=count)>200000)throw new InvalidDataException("PropsPoseLimit");
                    var values=new BoxPose[count];for(int j=0;j<count;j++) {
                        var p=new BoxPose {Sno=r.ReadUInt32(),X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle(),Cos=r.ReadSingle(),Sin=r.ReadSingle()};
                        if(!cache.BoxDefinitions.ContainsKey(p.Sno)||!Finite(p.X)||!Finite(p.Y)||!Finite(p.Z)||!Finite(p.Cos)||!Finite(p.Sin)
                            ||Math.Abs(p.Cos*p.Cos+p.Sin*p.Sin-1)>0.002)throw new InvalidDataException("PropsMarkerPose");
                        values[j]=p;
                    }
                    cache.BoxPoses.Add(sno,values);
                }
                if(stream.Position!=payload)throw new InvalidDataException("PropsTrailingData");cache.ShapeState="Ready";
            }
        }
        private void CollectNativeBoxes()
        {
            _nativeBoxes.Clear();TemplateCache cache=_placedCache;if(cache==null||cache.ShapeState!="Ready")return;
            int inspected=0;
            foreach(var scene in _placements.Values) {
                BoxPose[] poses;if(scene.Template==null||scene.OriginConflict||!cache.BoxPoses.TryGetValue(scene.Sno,out poses))continue;
                foreach(var pose in poses) {
                    if(inspected++>=4096||_nativeBoxes.Count>=256)return;BoxDefinition d=cache.BoxDefinitions[pose.Sno];
                    float px=scene.X+pose.X,py=scene.Y+pose.Y,pz=scene.Z+pose.Z;
                    if(DistanceSquared(px,py,_playerX,_playerY)>140f*140f)continue;
                    uint acd=0;bool observed=false;
                    foreach(var o in _obstacles)if(o.Sno==pose.Sno&&o.FloorZKnown&&DistanceSquared(o.FloorX,o.FloorY,px,py)<0.25f*0.25f&&Math.Abs(o.FloorZ-pz)<0.25f) {
                        observed=true;acd=o.AcdId;
                        break;
                    }
                    // Conditional chests/racks/doors/shrines need live confirmation. Pit fixtures may be absent from FreeHUD's Actors list.
                    // Marker-only pits stay explicitly unverified candidates until checked in-game; never declare all event markers solid.
                    if(!observed&&d.Kind!="TrapPit")continue;
                    float cx=px+pose.Cos*d.X-pose.Sin*d.Y,cy=py+pose.Sin*d.X+pose.Cos*d.Y;
                    float ground;uint groundFlags;int groundIndex;float floor=HeightAt(scene,px,py,out ground,out groundFlags,out groundIndex)?ground:pz;
                    _nativeBoxes.Add(new NativeBox {Definition=d,SceneId=scene.SceneId,AcdId=acd,Presence=observed?"LiveActorMatched":"TrapMarkerCandidate",
                        X=cx,Y=cy,MinZ=pz+d.Z-d.HZ,MaxZ=pz+d.Z+d.HZ,HX=d.HX,HY=d.HY,Cos=pose.Cos,Sin=pose.Sin,FloorZ=floor});
                }
            }
        }
        private void CollectUnmatchedNativeProps()
        {
            if(_placedCache==null||_placedCache.ShapeState!="Ready")return;
            foreach(var o in _obstacles) {
                if(_nativeBoxes.Count>=256)return;
                if(HasNativeBox(o)||!o.FloorZKnown)continue;
                BoxDefinition d;if(!_placedCache.BoxDefinitions.TryGetValue(o.Sno,out d))continue;
                // FreeHUD exposes identity/floor but no yaw. Enclose every possible
                // rotation of the native collider; label it instead of claiming an exact pose.
                float extent=(d.Cylinder?d.HX:(float)Math.Sqrt(d.HX*d.HX+d.HY*d.HY))+(float)Math.Sqrt(d.X*d.X+d.Y*d.Y);
                if(extent<=0||extent>50)continue;
                _nativeBoxes.Add(new NativeBox {Definition=d,SceneId=o.SceneId,AcdId=o.AcdId,
                    Presence=d.Cylinder?"LiveActorNativeCylinderBounds":"LiveActorNativeRotationEnvelope",X=o.FloorX,Y=o.FloorY,
                    MinZ=o.FloorZ+d.Z-d.HZ,MaxZ=o.FloorZ+d.Z+d.HZ,HX=extent,HY=extent,
                    Cos=1,Sin=0,FloorZ=o.FloorZ});
            }
        }
        private bool HasNativeBox(Obstacle o)
        {
            foreach(var b in _nativeBoxes)if(o.AcdId!=0&&b.AcdId==o.AcdId)return true;return false;
        }
        private static bool BoxInterval(NativeBox b,float x,float y,float z,float tx,float ty,float tz,float radius,out Interval interval)
        {
            // Point/segment XYZ are floor positions. A slightly raised box still obstructs a standing actor.
            // Keep its exact volume separate from the small native-ground projection used by footprint queries.
            double low=b.MinZ;if(b.MinZ>=b.FloorZ&&b.MinZ-b.FloorZ<=GroundFootprintLiftTolerance)low=b.FloorZ;
            double a=0,end=1,dx=x-b.X,dy=y-b.Y,ex=tx-b.X,ey=ty-b.Y;
            bool hit=Slab(dx*b.Cos+dy*b.Sin,(ex-dx)*b.Cos+(ey-dy)*b.Sin,-b.HX-radius,b.HX+radius,ref a,ref end)
                &&Slab(-dx*b.Sin+dy*b.Cos,-(ex-dx)*b.Sin+(ey-dy)*b.Cos,-b.HY-radius,b.HY+radius,ref a,ref end)
                &&Slab(z,tz-z,low-FloorZTolerance,b.MaxZ+FloorZTolerance,ref a,ref end);
            interval=new Interval {Start=a,End=end};return hit;
        }
        private Dictionary<string,object> BoxValue(NativeBox b)
        {
            var d=b.Definition;var vertices=new float[12];
            for(int i=0;i<4;i++){float x=i==0||i==3?-b.HX:b.HX,y=i<2?-b.HY:b.HY;vertices[i*3]=b.X+b.Cos*x-b.Sin*y;vertices[i*3+1]=b.Y+b.Sin*x+b.Cos*y;vertices[i*3+2]=b.FloorZ;}
            return new Dictionary<string,object> {{"sno",d.Sno},{"acdId",b.AcdId},{"sceneId",b.SceneId},{"code",d.Code},{"kind",d.Kind},{"shape",d.Cylinder?"NativeCylinderBoundingBoxCandidate":b.Presence=="LiveActorNativeRotationEnvelope"?"NativeRotationEnvelopeCandidate":"NativeOrientedBoxCandidate"},
                {"source","CASC_ActorCollisionData_MarkerSet"},{"presence",b.Presence},{"position",new float[]{b.X,b.Y,b.FloorZ}},{"footprintXYZ",vertices},
                {"halfExtents",new float[]{b.HX,b.HY}},{"rotationCosSin",new float[]{b.Cos,b.Sin}},{"zRange",new float[]{b.MinZ,b.MaxZ}},
                {"queryZRange",new float[]{b.MinZ>=b.FloorZ&&b.MinZ-b.FloorZ<=GroundFootprintLiftTolerance?b.FloorZ:b.MinZ,b.MaxZ}},{"orientationKnown",b.Presence!="LiveActorNativeRotationEnvelope"},{"scaleValidated",false},{"assetHash",d.AssetHash},{"collisionFlags",d.Flags},{"collisionValidated",false}};
        }
        private object Shapes()
        {
            if(_placedCache==null||_placedCache.ShapeState!="Ready")return Error("ShapeLayerUnavailable");
            var r=Envelope();r["ok"]=true;r["shapeState"]=_placedCache.ShapeState;
            var values=new object[_nativeBoxes.Count];for(int i=0;i<values.Length;i++)values[i]=BoxValue(_nativeBoxes[i]);r["shapes"]=values;
            r["completeCollisionCoverage"]=false;return r;
        }

        // Only nearby height grids are inflated. Keep all templates small; never decode terrain on a render/query call.
        private void RequestHeight(Template template)
        {
            TemplateCache cache=_placedCache;
            if(template==null||cache==null||template.GridWidth==0||template.Grid!=null||template.GridState=="LoadError")return;
            bool start=false;
            lock(_heightGate) {
                if(!template.GridQueued) {
                    if(_heightRequests.Count>=64)return;
                    template.GridQueued=true;template.GridState="Pending";
                    _heightRequests.Enqueue(new GridRequest {Cache=cache,Template=template});
                }
                if(!_heightWorkerRunning){_heightWorkerRunning=true;start=true;}
            }
            if(start)ThreadPool.QueueUserWorkItem(delegate { HeightWorker(); });
        }
        private void HeightWorker()
        {
            while(true) {
                GridRequest request;
                lock(_heightGate) {
                    if(_heightRequests.Count==0){_heightWorkerRunning=false;return;}
                    request=_heightRequests.Dequeue();
                }
                Template template=request.Template;TemplateCache cache=request.Cache;
                try {
                    if(cache!=_cache)continue;
                    byte[] compressed=new byte[template.GridCompressedBytes];
                    using(var file=new FileStream(cache.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
                        if(template.GridOffset<0||template.GridOffset+compressed.Length>file.Length-32)throw new InvalidDataException("HeightRange");
                        file.Position=template.GridOffset;int at=0;
                        while(at<compressed.Length){int n=file.Read(compressed,at,compressed.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}
                    }
                    using(var hash=MD5.Create())if(Hex(hash.ComputeHash(compressed))!=Hex(template.GridHash))throw new InvalidDataException("HeightChecksum");
                    int count=template.GridWidth*template.GridHeight;var grid=new NativeGrid {Heights=new float[count],Flags=new uint[count]};
                    using(var source=new MemoryStream(compressed,false))
                    using(var inflate=new DeflateStream(source,CompressionMode.Decompress))
                    using(var reader=new BinaryReader(inflate)) {
                        for(int i=0;i<count;i++) {
                            grid.Heights[i]=reader.ReadSingle();grid.Flags[i]=reader.ReadUInt32();
                            if((grid.Flags[i]&1)!=0&&!Finite(grid.Heights[i]))throw new InvalidDataException("HeightNonfinite");
                        }
                        if(inflate.ReadByte()!=-1)throw new InvalidDataException("HeightTrailingData");
                    }
                    if(cache!=_cache)continue;
                    template.Grid=grid;template.GridState="Ready";cache.LoadedHeights.Enqueue(template);cache.HeightBytes+=count*8L;
                    while(cache.LoadedHeights.Count>32||cache.HeightBytes>16L*1024*1024) {
                        Template old=cache.LoadedHeights.Dequeue();
                        if(old.Grid!=null){cache.HeightBytes-=old.Grid.Heights.Length*8L;old.Grid=null;old.GridState="NotLoaded";}
                    }
                } catch { template.GridState="LoadError"; }
                finally { lock(_heightGate)template.GridQueued=false; }
            }
        }
        private void PreloadHeight(Placement p)
        {
            float x=Math.Max(p.X,Math.Min(_playerX,p.MaxX)),y=Math.Max(p.Y,Math.Min(_playerY,p.MaxY));
            if(DistanceSquared(x,y,_playerX,_playerY)<=HeightPreloadRadius*HeightPreloadRadius)RequestHeight(p.Template);
        }
        private static bool HeightAt(Placement p,float x,float y,out float z,out uint flags,out int index)
        {
            z=0;flags=0;index=-1;
            if(p==null||p.Template==null||p.OriginConflict)return false;
            Template t=p.Template;NativeGrid grid=t.Grid;if(grid==null)return false;
            // Native grid/Scene span disagreement is unknown, not an inferred scale from the player.
            if(Math.Abs((p.MaxX-p.X)-t.GridWidth*t.StepX)>0.1||Math.Abs((p.MaxY-p.Y)-t.GridHeight*t.StepY)>0.1)return false;
            int gx=(int)Math.Floor((x-p.X)/t.StepX),gy=(int)Math.Floor((y-p.Y)/t.StepY);
            if(gx<0||gy<0||gx>=t.GridWidth||gy>=t.GridHeight)return false;
            index=gy*t.GridWidth+gx;flags=grid.Flags[index];
            if((flags&1)==0||!Finite(grid.Heights[index]))return false;
            z=grid.Heights[index]+p.Z;return Finite(z);
        }

        // Placement identity is current world INSTANCE + Scene/NavMesh IDs + XYZ, never just SNO.
        // FreeHUD exposes player/actor-linked Scenes, not a complete loaded-Scene enumeration.
        public void AfterCollect()
        {
            if(!Enabled||Hud.Game==null||!Hud.Game.IsInGame||Hud.Game.IsLoading||Hud.Game.Me==null) {
                SuspendCoverage(!Enabled?"Disabled":Hud.Game==null?"GameUnavailable":
                    !Hud.Game.IsInGame?"NotInGame":Hud.Game.IsLoading?"Loading":"PlayerUnavailable");return;
            }
            var me=Hud.Game.Me;var pos=me.FloorCoordinate;
            if(pos==null||!pos.IsValid||me.WorldId==0||me.WorldSno==0||me.Scene==null) {
                SuspendCoverage("NativeContextUnavailable");return;
            }
            int tick=Hud.Game.CurrentGameTick;long now=Hud.Game.CurrentRealTimeMilliseconds;
            bool suspensionExpired=_suspendedTimestamp>=0
                &&Stopwatch.GetTimestamp()-_suspendedTimestamp>5*Stopwatch.Frequency;
            if(me.WorldId!=_worldId||me.WorldSno!=_worldSno||_placedCache!=_cache||suspensionExpired) {
                Invalidate(me.WorldId!=_worldId||me.WorldSno!=_worldSno?"WorldChanged":
                    _placedCache!=_cache?"DataChanged":"SuspensionExpired");
                _worldId=me.WorldId;_worldSno=me.WorldSno;_placedCache=_cache;
            } else if(tick<_lastTick) SuspendCoverage("TickRollback");
            _suspendedTimestamp=-1;
            _lastTick=tick;
            if(_lastCacheCheck==0||now<_lastCacheCheck||now-_lastCacheCheck>=5000) { _lastCacheCheck=now;ScheduleCacheCheck(); }
            if(_valid&&now>=_lastCollectMs&&now-_lastCollectMs<100&&_currentSceneId==me.Scene.SceneId)return;
            _lastCollectMs=now;_sampleMs=now;_version++;_valid=true;
            _playerX=pos.X;_playerY=pos.Y;_playerZ=pos.Z;_currentSceneId=me.Scene.SceneId;
            ObserveScene(me.Scene,true,"Player");
            ObserveLoadedScenes();
            _targets.Clear();int monsters=0;
            foreach(var monster in Hud.Game.AliveMonsters) {
                if(monsters++>=1024)break;
                var coordinate=monster.FloorCoordinate;
                if(monster.WorldId!=_worldId||coordinate==null||!coordinate.IsValid
                    ||DistanceSquared(coordinate.X,coordinate.Y,_playerX,_playerY)>160f*160f)continue;
                ObserveScene(monster.Scene,false,"Monster");
                if(monster.Scene!=null&&_targets.Count<128)_targets.Add(new TargetSample {AcdId=monster.AcdId,SceneId=monster.Scene.SceneId,
                    X=coordinate.X,Y=coordinate.Y,Z=coordinate.Z,Elite=monster.IsElite});
            }
            _obstacles.Clear();_footprintCandidates.Clear();int scanned=0;
            foreach(var actor in Hud.Game.Actors) {
                if(scanned++>=MaximumActorScan)break;
                if(actor==null||actor.WorldId!=_worldId)continue;
                ObserveScene(actor.Scene,false,"Actor");ObserveObstacle(actor);
            }
            // Look ahead across scene boundaries using every native placement source
            // exposed by FreeHUD. Empty unreported scenes remain unknown, never guessed.
            int lookahead=0;
            foreach(var monster in Hud.Game.Monsters) {
                if(lookahead++>=2048)break;ObserveLookaheadActor(monster,"AllMonsters");
            }
            lookahead=0;
            foreach(var item in Hud.Game.Items) {
                if(lookahead++>=2048)break;
                if(item.Location==ItemLocation.Floor)ObserveLookaheadActor(item,"GroundItem");
            }
            foreach(var player in Hud.Game.Players)ObserveLookaheadActor(player,"Party");
            foreach(var portal in Hud.Game.Portals)ObserveLookaheadActor(portal,"Portal");
            // Dedicated lists cover scenes and props even when missing from Actors.
            foreach(var shrine in Hud.Game.Shrines){ObserveLookaheadActor(shrine,"Shrine");ObserveObstacle(shrine);}
            foreach(var door in Hud.Game.Doors){ObserveLookaheadActor(door,"Door");ObserveObstacle(door);}
            foreach(var chest in Hud.Game.NormalChests){ObserveLookaheadActor(chest,"Chest");ObserveObstacle(chest);}
            foreach(var chest in Hud.Game.ResplendentChests){ObserveLookaheadActor(chest,"Chest");ObserveObstacle(chest);}
            // Retained scene placements preload by nearest boundary distance, not the
            // player's current SceneId, drawing toggle, or distance to a scene's centre.
            foreach(var placement in _placements.Values)PreloadHeight(placement);
            RefreshDrawingPlacements();
            CollectNativeBoxes();CollectUnmatchedNativeProps();LogState();LogHeightSnapshot();
        }
        // Draw the nearest known scenes first. A distant remembered scene must not
        // consume the fixed rendering budget and hide an adjacent scene. GET uses all placements.
        private readonly List<Placement> _drawingPlacements=new List<Placement>(MaximumPlacements);
        private static double PlacementGapSquared(Placement p,float x,float y)
        {return DistanceSquared(x,y,Math.Max(p.X,Math.Min(x,p.MaxX)),Math.Max(p.Y,Math.Min(y,p.MaxY)));}
        private void RefreshDrawingPlacements()
        {
            _drawingPlacements.Clear();
            foreach(var p in _placements.Values)
                if(PlacementGapSquared(p,_playerX,_playerY)<=DrawRadius*DrawRadius)_drawingPlacements.Add(p);
            _drawingPlacements.Sort((a,b)=>PlacementGapSquared(a,_playerX,_playerY).CompareTo(PlacementGapSquared(b,_playerX,_playerY)));
        }
        private void ObserveLookaheadActor(IActor actor,string source)
        {
            if(actor==null||actor.WorldId!=_worldId||actor.FloorCoordinate==null||!actor.FloorCoordinate.IsValid)return;
            if(DistanceSquared(actor.FloorCoordinate.X,actor.FloorCoordinate.Y,_playerX,_playerY)>180f*180f)return;
            ObserveScene(actor.Scene,false,source);
        }
        // Temporary sampling loss invalidates queries/routes, not verified placement data.
        // Resume checks native world INSTANCE and cache identity before using retained scenes.
        // Only a changed world/data source discards them; never guess an unreported neighbor.
        private void SuspendCoverage(string reason)
        {
            if(_valid) {
                LogContextChange("Suspend",reason);
                _suspendedTimestamp=Stopwatch.GetTimestamp();
                Interlocked.Increment(ref _routeSerial);_route=null;_routeRequest=null;_corridorSnapshot=null;_epoch++;
            }
            _valid=false;_drawingPlacements.Clear();_obstacles.Clear();_footprintCandidates.Clear();
            _nativeBoxes.Clear();_targets.Clear();_lastCollectMs=0;
        }
        private void Invalidate(string reason)
        {
            LogContextChange("Discard",reason);
            _suspendedTimestamp=-1;
            Interlocked.Increment(ref _routeSerial);_route=null;_routeRequest=null;_corridorSnapshot=null;
            if(_valid||_placements.Count>0)_epoch++;
            _valid=false;_placements.Clear();_drawingPlacements.Clear();_sceneCoverageLogged.Clear();_obstacles.Clear();_footprintCandidates.Clear();_nativeBoxes.Clear();_targets.Clear();_worldId=0;_worldSno=0;_lastCollectMs=0;
        }
        private void LogContextChange(string action,string reason)
        {
            if(!Diagnostics||Hud==null||Hud.TextLog==null)return;
            try { Hud.TextLog.Log("s7o_MapViewer","CONTEXT|session="+_session+";epoch="+_epoch
                +";action="+action+";reason="+reason+";world="+_worldId+";lastTick="+_lastTick
                +";tick="+(Hud.Game==null?0:Hud.Game.CurrentGameTick)+";rememberedScenes="+_placements.Count,false,true); }
            catch { /* Optional diagnostics cannot interrupt geometry collection. */ }
        }
        // FreeHUD collects loaded terrain before actors enter it. Its public API omits this list.
        // Bind once; the hot path uses compiled delegates, never reflection or process memory.
        // Read only live native rows, not remembered reveal records (their Z may be zero).
        // Any binding/context failure falls back to the existing public placement sources.
        private void ObserveLoadedScenes()
        {
            long stamp=Stopwatch.GetTimestamp();
            if(_lastLoadedSceneStamp!=0&&stamp-_lastLoadedSceneStamp<Stopwatch.Frequency/2)return;
            _lastLoadedSceneStamp=stamp;
            try {
                if(_loadedSceneReader==null) {
                    if(_lastSceneBindStamp!=0&&stamp-_lastSceneBindStamp<5*Stopwatch.Frequency)return;
                    _lastSceneBindStamp=stamp;
                    _loadedSceneReader=BindLoadedSceneReader(Hud.Game.GetType().Assembly,out _loadedSceneFind);
                }
                IScene current=Hud.Game.Me.Scene;
                IScene anchor=current==null?null:_loadedSceneFind(_worldId,current.NavMeshId);
                if(anchor==null||current==null||anchor.SceneId!=current.SceneId||anchor.NavMeshId!=current.NavMeshId
                    ||anchor.WorldSno!=_worldSno||anchor.SnoScene==null||current.SnoScene==null
                    ||anchor.SnoScene.Sno!=current.SnoScene.Sno||anchor.PosX!=current.PosX
                    ||anchor.PosY!=current.PosY||anchor.Z!=current.Z) {
                    _loadedSceneReader=null;_loadedSceneFind=null;_loadedSceneState="ContextMismatch";return;
                }
                List<IScene> scenes=_loadedSceneReader(_worldId);
                _loadedSceneCount=scenes.Count;_loadedSceneNearby=0;
                foreach(IScene scene in scenes) {
                    if(scene==null||scene.WorldSno!=_worldSno||!Finite(scene.PosX)||!Finite(scene.PosY)
                        ||!Finite(scene.MaxX)||!Finite(scene.MaxY)||scene.MaxX<=scene.PosX||scene.MaxY<=scene.PosY)continue;
                    float dx=Math.Max(scene.PosX-_playerX,Math.Max(0,_playerX-scene.MaxX));
                    float dy=Math.Max(scene.PosY-_playerY,Math.Max(0,_playerY-scene.MaxY));
                    if(dx*dx+dy*dy>120f*120f)continue;
                    _loadedSceneNearby++;ObserveScene(scene,false,"LoadedScene");
                }
                _loadedSceneState="Ready";
            } catch {
                _loadedSceneReader=null;_loadedSceneFind=null;_loadedSceneCount=0;_loadedSceneNearby=0;
                _loadedSceneState="Unavailable"; // Never disable the map or public API fallback.
            }
        }
        private static Func<uint,List<IScene>> BindLoadedSceneReader(System.Reflection.Assembly assembly,
            out Func<uint,uint,IScene> find)
        {
            find=null;
            var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            Type core=assembly.GetType("work.CoreCollector",false),type=assembly.GetType("collectors.SceneCollector",false);
            if(core==null||type==null)throw new InvalidOperationException("LoadedSceneCollectorMissing");
            System.Reflection.FieldInfo root=null;
            foreach(var field in core.GetFields(flags|System.Reflection.BindingFlags.Static))
                if(field.FieldType==type) {if(root!=null)throw new InvalidOperationException("AmbiguousSceneCollector");root=field;}
            if(root==null)throw new InvalidOperationException("LoadedSceneRootMissing");
            object collector=root.GetValue(null);
            if(collector==null)throw new InvalidOperationException("LoadedSceneCollectorNotReady");
            System.Reflection.MethodInfo lookup=null;
            foreach(var method in type.GetMethods(flags|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly)) {
                var args=method.GetParameters();
                if(method.ReturnType==typeof(IScene)&&args.Length==2&&args[0].ParameterType==typeof(uint)&&args[1].ParameterType==typeof(uint)) {
                    if(lookup!=null)throw new InvalidOperationException("AmbiguousSceneLookup");lookup=method;
                }
            }
            var buffer=type.GetProperty("Buffer_Scenes",flags|System.Reflection.BindingFlags.Instance);
            if(lookup==null||buffer==null||!buffer.PropertyType.IsArray||buffer.GetGetMethod(true)==null)
                throw new InvalidOperationException("LoadedSceneContractMissing");
            find=(Func<uint,uint,IScene>)Delegate.CreateDelegate(typeof(Func<uint,uint,IScene>),collector,lookup,true);
            Type rowType=buffer.PropertyType.GetElementType();
            var worldField=rowType.GetField("SWorldID",flags|System.Reflection.BindingFlags.Instance);
            var idField=rowType.GetField("Id",flags|System.Reflection.BindingFlags.Instance);
            var sceneField=rowType.GetField("SSceneID",flags|System.Reflection.BindingFlags.Instance);
            var snoField=rowType.GetField("SceneSno",flags|System.Reflection.BindingFlags.Instance);
            foreach(var field in new[]{worldField,idField,sceneField,snoField})
                if(field==null||field.FieldType!=typeof(uint))throw new InvalidOperationException("LoadedSceneRowContractChanged");
            // Compile direct array-field access: no boxing a large native row on every scan.
            var world=System.Linq.Expressions.Expression.Parameter(typeof(uint),"world");
            var rows=System.Linq.Expressions.Expression.Variable(buffer.PropertyType,"rows");
            var index=System.Linq.Expressions.Expression.Variable(typeof(int),"index");
            var scene=System.Linq.Expressions.Expression.Variable(typeof(IScene),"scene");
            var result=System.Linq.Expressions.Expression.Variable(typeof(List<IScene>),"result");
            var end=System.Linq.Expressions.Expression.Label("end");
            var row=System.Linq.Expressions.Expression.ArrayIndex(rows,index);
            var rowWorld=System.Linq.Expressions.Expression.Field(row,worldField);
            var rowId=System.Linq.Expressions.Expression.Field(row,idField);
            var rowScene=System.Linq.Expressions.Expression.Field(row,sceneField);
            var rowSno=System.Linq.Expressions.Expression.Field(row,snoField);
            var valid=System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.Equal(rowWorld,world),
                System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.NotEqual(rowId,System.Linq.Expressions.Expression.Constant(uint.MaxValue)),
                System.Linq.Expressions.Expression.NotEqual(rowScene,System.Linq.Expressions.Expression.Constant(uint.MaxValue))));
            // FreeHUD names differ from native fields: NavMeshId = SSceneID (lookup key), SceneId = Id.
            // Preserve and validate both identities; do not key the collector by public SceneId.
            var matching=System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.NotEqual(scene,System.Linq.Expressions.Expression.Constant(null,typeof(IScene))),
                System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(scene,"SceneId"),rowId),
                    System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(scene,"NavMeshId"),rowScene)),
                System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.NotEqual(System.Linq.Expressions.Expression.Property(scene,"SnoScene"),System.Linq.Expressions.Expression.Constant(null,typeof(ISnoScene))),
                System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Property(scene,"SnoScene"),"Sno"),rowSno))));
            var body=System.Linq.Expressions.Expression.Block(new[]{rows,index,scene,result},
                System.Linq.Expressions.Expression.Assign(result,System.Linq.Expressions.Expression.New(typeof(List<IScene>))),
                System.Linq.Expressions.Expression.Assign(rows,System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Constant(collector,type),buffer.GetGetMethod(true))),
                System.Linq.Expressions.Expression.Assign(index,System.Linq.Expressions.Expression.Constant(0)),
                System.Linq.Expressions.Expression.IfThen(System.Linq.Expressions.Expression.NotEqual(rows,System.Linq.Expressions.Expression.Constant(null,buffer.PropertyType)),
                    System.Linq.Expressions.Expression.Loop(System.Linq.Expressions.Expression.Block(
                        System.Linq.Expressions.Expression.IfThen(System.Linq.Expressions.Expression.OrElse(
                            System.Linq.Expressions.Expression.GreaterThanOrEqual(index,System.Linq.Expressions.Expression.ArrayLength(rows)),
                            System.Linq.Expressions.Expression.GreaterThanOrEqual(index,System.Linq.Expressions.Expression.Constant(4096))),System.Linq.Expressions.Expression.Break(end)),
                        System.Linq.Expressions.Expression.IfThen(valid,System.Linq.Expressions.Expression.Block(
                            System.Linq.Expressions.Expression.Assign(scene,System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(find),world,rowScene)),
                            System.Linq.Expressions.Expression.IfThen(matching,System.Linq.Expressions.Expression.Call(result,typeof(List<IScene>).GetMethod("Add"),scene)))),
                        System.Linq.Expressions.Expression.PostIncrementAssign(index)),end)),result);
            return System.Linq.Expressions.Expression.Lambda<Func<uint,List<IScene>>>(body,world).Compile();
        }

        private void ObserveScene(IScene scene, bool primary = false, string source = "Actor")
        {
            if(scene==null||scene.SnoScene==null||scene.WorldSno!=_worldSno||scene.SceneId==0||scene.NavMeshId==0
                ||!Finite(scene.PosX)||!Finite(scene.PosY)||!Finite(scene.Z)||!Finite(scene.MaxX)||!Finite(scene.MaxY)
                ||scene.MaxX<=scene.PosX||scene.MaxY<=scene.PosY)return;
            Placement existing;
            if(_placements.TryGetValue(scene.SceneId,out existing)&&existing.NavMeshId==scene.NavMeshId
                &&existing.Sno==scene.SnoScene.Sno&&existing.X==scene.PosX&&existing.Y==scene.PosY) {
                if(Math.Abs(existing.Z-scene.Z)<=0.001f) {
                    if(existing.SeenVersion!=_version)existing.OriginConflict=false;
                    existing.SeenVersion=_version;PreloadHeight(existing);return;
                }
                _lastPlacementChange="scene="+scene.SceneId+",oldZ="+F(existing.Z)+",newZ="+F(scene.Z)+",source="+source;
                if(existing.SeenVersion==_version) {
                    existing.OriginConflict=true;return; // Conflicting same-frame native placements must not publish invented heights.
                }
            }
            if(_placements.Count>=MaximumPlacements&&!_placements.ContainsKey(scene.SceneId)) {
                if(!primary)return; // Actor-linked coverage is partial; never lose the player's current Scene.
                uint remove=0;double farthest=-1;
                foreach(var p in _placements.Values) {
                    double distance=DistanceSquared((p.X+p.MaxX)*0.5f,(p.Y+p.MaxY)*0.5f,_playerX,_playerY);
                    if(distance>farthest){farthest=distance;remove=p.SceneId;}
                }
                _placements.Remove(remove);
            }
            if(existing!=null)_epoch++; // A reused Scene ID with changed placement invalidates saved routes.
            Template template=null;TemplateCache cache=_placedCache;
            if(cache!=null)cache.Templates.TryGetValue(scene.SnoScene.Sno,out template);
            if(template!=null&&!String.Equals(template.Code,scene.SnoScene.Code,StringComparison.OrdinalIgnoreCase))template=null;
            _placements[scene.SceneId]=new Placement { SceneId=scene.SceneId,NavMeshId=scene.NavMeshId,Sno=scene.SnoScene.Sno,
                Code=scene.SnoScene.Code,X=scene.PosX,Y=scene.PosY,Z=scene.Z,MaxX=scene.MaxX,MaxY=scene.MaxY,Template=template,
                Source=source,SeenVersion=_version };
            PreloadHeight(_placements[scene.SceneId]);
        }
        private void ObserveObstacle(IActor actor)
        {
            if(actor==null||actor.WorldId!=_worldId||_obstacles.Count>=256)return;
            // Activated shrines/pylons can still be physical blockers.
            if(actor.IsDisabled&&!(actor is IShrine))return;
            string kind=null;
            uint sno=actor.SnoActor==null?0:(uint)actor.SnoActor.Sno;
            // Identified native actors, not a guessed radius or a blacklist of dungeon floors.
            if(sno==108266)kind="TrapPit"; // a1dun_Leor_Spike_TrapDoor
            else if(sno==105478||sno==454181)kind="TrapLever";
            var shrine=actor as IShrine;
            if(shrine!=null)kind=shrine.IsPylon?"Pylon":"Shrine";
            else if(kind==null)switch(actor.GizmoType) {
                case GizmoType.Door: kind="Door";break;
                case GizmoType.Gate: kind="Gate";break;
                case GizmoType.Switch:kind="Switch";break;
                case GizmoType.BreakableDoor: kind="BreakableDoor";break;
                case GizmoType.Chest: case GizmoType.BreakableChest: case GizmoType.LoreChest:kind="Chest";break;
                case GizmoType.DestroyableObject:case GizmoType.ReformingDestroyableObject:kind="Destroyable";break;
                case GizmoType.Portal:case GizmoType.DungeonPortal:case GizmoType.SecretPortal:case GizmoType.BossPortal:
                case GizmoType.ReturnPortal:case GizmoType.TownPortal:kind="Portal";break;
            }
            string actorCode=actor.SnoActor==null?"":actor.SnoActor.Code;
            bool rack=actorCode!=null&&(actorCode.IndexOf("armorrack",StringComparison.OrdinalIgnoreCase)>=0
                ||actorCode.IndexOf("armor_rack",StringComparison.OrdinalIgnoreCase)>=0
                ||actorCode.IndexOf("weaponrack",StringComparison.OrdinalIgnoreCase)>=0
                ||actorCode.IndexOf("weapon_rack",StringComparison.OrdinalIgnoreCase)>=0);
            // Label actual interactive racks; never promote similarly named visual effects to solid areas.
            if(rack&&(kind=="Chest"||actor.IsClickable))kind="Rack";
            if(kind=="Switch"&&actorCode!=null&&actorCode.IndexOf("CursedChest",StringComparison.OrdinalIgnoreCase)>=0)kind="Chest";
            if(kind==null&&_placedCache!=null) {
                BoxDefinition known;if(_placedCache.BoxDefinitions.TryGetValue(sno,out known))kind=known.Kind;
            }
            if(kind==null)return;
            if(actor.IsOperated&&(kind=="Door"||kind=="Gate"||kind=="BreakableDoor"))return;
            var pos=actor.CollisionCoordinate;
            if(pos==null||!pos.IsValid)pos=actor.FloorCoordinate;
            if(pos==null||!pos.IsValid||DistanceSquared(pos.X,pos.Y,_playerX,_playerY)>180f*180f)return;
            foreach(var old in _obstacles)if(old.AcdId==actor.AcdId)return;
            string radiusSource;float radius=GroundRadius(actor,out radiusSource);
            var obstacle=new Obstacle { AcdId=actor.AcdId,Sno=sno,SceneId=actor.Scene==null?0:actor.Scene.SceneId,Code=actor.SnoActor==null?"":actor.SnoActor.Code,
                Kind=kind,X=pos.X,Y=pos.Y,Z=pos.Z,Radius=radius,RadiusSource=radiusSource,Operated=actor.IsOperated,Clickable=actor.IsClickable,
                FloorZKnown=actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid,
                FloorX=actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid?actor.FloorCoordinate.X:0,FloorY=actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid?actor.FloorCoordinate.Y:0,
                FloorZ=actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid?actor.FloorCoordinate.Z:0 };
            _obstacles.Add(obstacle);
            // Reported circles are conservative candidates, not reconstructed collision polygons.
            if(kind=="TrapLever"||kind=="TrapPit"||kind=="Pylon"||kind=="Shrine"||kind=="Chest"||kind=="Rack") {
                if(_footprintCandidates.Count<32)_footprintCandidates.Add(obstacle);
                else {
                    int farthest=0;double distance=-1;
                    for(int i=0;i<_footprintCandidates.Count;i++){var old=_footprintCandidates[i];double d=DistanceSquared(old.X,old.Y,_playerX,_playerY);if(d>distance){distance=d;farthest=i;}}
                    if(DistanceSquared(pos.X,pos.Y,_playerX,_playerY)<distance)_footprintCandidates[farthest]=obstacle;
                }
            }
        }
        private static float GroundRadius(IActor actor,out string source)
        {
            // Live evidence: RadiusScaled can equal world X (e.g. 759), not a radius.
            // Ground candidates use the reported bottom radius first; never convert rejected data to a fake footprint.
            float radius=actor.RadiusBottom;
            if(Finite(radius)&&radius>0&&radius<=50){source="RadiusBottom";return radius;}
            radius=actor.RadiusScaled;
            var pos=actor.CollisionCoordinate;
            bool coordinateValue=pos!=null&&pos.IsValid&&Math.Abs(radius-pos.X)<0.01f;
            if(Finite(radius)&&radius>0&&radius<=50&&!coordinateValue){source="RadiusScaled";return radius;}
            source="Unknown";return 0;
        }
        private bool Current()
        {
            return Enabled&&_valid&&_placedCache!=null&&_placedCache==_cache&&Hud.Game.IsInGame&&!Hud.Game.IsLoading
                &&Hud.Game.Me!=null&&Hud.Game.Me.WorldId==_worldId&&Hud.Game.Me.WorldSno==_worldSno
                &&Hud.Game.CurrentGameTick>=_lastTick&&Hud.Game.CurrentRealTimeMilliseconds>=_sampleMs
                &&Hud.Game.CurrentRealTimeMilliseconds-_sampleMs<=500;
        }
        private void LogState()
        {
            if(!Diagnostics||Hud.TextLog==null)return;
            string state=_cacheState+";world="+_worldId+";scene="+_currentSceneId;
            if(state==_lastLoggedState)return;_lastLoggedState=state;
            int found=0;foreach(var placement in _placements.Values)if(placement.Template!=null)found++;
            try { Hud.TextLog.Log("s7o_MapViewer","MAP|session="+_session+";epoch="+_epoch+";"+state
                +";observedScenes="+_placements.Count+";mappedScenes="+found+";input=NO;authority=CANDIDATE",false,true); }
            catch { /* Diagnostics must never interrupt map collection. */ }
        }

        // API v1 uses BCL dictionaries/arrays: no reflection, network listener, or consumer dependency.
        // Query args begin with the epoch returned by status/player. Values are copied, never internal mutable arrays.
        public object Get(string method, object[] args)
        {
            try {
                if(method=="controls")return Controls();
                if(method=="configure") {
                    if(args==null||args.Length!=2||!(args[0] is bool)||!(args[1] is bool))return Error("ExpectedGridAndDebugBooleans");
                    ShowGrid=(bool)args[0];Diagnostics=(bool)args[1];
                    _lastLoggedState="";_routeLogged="";_sceneCoverageLogged.Clear();SaveControls();return Controls();
                }
                if(method=="status"||method=="player")return Status();
                if(args==null||args.Length==0||args[0]==null)return Error("EpochRequired");
                long expected=Convert.ToInt64(args[0]);if(expected!=_epoch||!Current())return Error("ContextUnavailableOrChanged");
                switch(method) {
                    case "scenes":if(args.Length!=1)return Error("ExpectedEpoch");return Scenes();
                    case "cells":if(args.Length!=2)return Error("ExpectedEpochSceneId");return Cells(Convert.ToUInt32(args[1]));
                    case "height":if(args.Length!=4)return Error("ExpectedEpochXYReferenceZ");return Height(Number(args[1]),Number(args[2]),Number(args[3]));
                    case "monsters":if(args.Length!=1)return Error("ExpectedEpoch");return Monsters();
                    case "point":if(args.Length!=4)return Error("ExpectedEpochXYZ");return Point(Number(args[1]),Number(args[2]),Number(args[3]));
                    case "segment":if(args.Length!=7&&args.Length!=8)return Error("ExpectedEpochFromXYZToXYZ");
                        return Segment(Number(args[1]),Number(args[2]),Number(args[3]),Number(args[4]),Number(args[5]),Number(args[6]),args.Length==8?Number(args[7]):0);
                    case "shapes":if(args.Length!=1)return Error("ExpectedEpoch");return Shapes();
                    case "obstacles":if(args.Length!=1)return Error("ExpectedEpoch");return Obstacles();
                    case "corridor":if(args.Length!=8)return Error("ExpectedEpochFromXYZToXYZRadius");
                        return Corridor(Number(args[1]),Number(args[2]),Number(args[3]),Number(args[4]),Number(args[5]),Number(args[6]),Number(args[7]));
                    case "resetpath":if(args.Length!=1)return Error("ExpectedEpoch");
                        _route=null;_routeRequest=null;Interlocked.Increment(ref _routeSerial);return Status();
                    case "feedback":if(args.Length!=7)return Error("ExpectedEpochConsumerEventTargetXYZ");
                        LogConsumer(args);return Status();
                    case "dash":if(args.Length!=8)return Error("ExpectedEpochFromXYZToXYZRadius");
                        return DashHop(Number(args[1]),Number(args[2]),Number(args[3]),Number(args[4]),Number(args[5]),Number(args[6]),Number(args[7]));
                    case "path":if(args.Length!=8)return Error("ExpectedEpochFromXYZToXYZRadius");
                        return Route(Number(args[1]),Number(args[2]),Number(args[3]),Number(args[4]),Number(args[5]),Number(args[6]),Number(args[7]));
                    default:return Error("UnknownMethod");
                }
            } catch(FormatException){return Error("InvalidArguments");}
              catch(InvalidCastException){return Error("InvalidArguments");}
              catch(OverflowException){return Error("InvalidArguments");}
              catch(ArgumentException){return Error("InvalidArguments");}
        }
        // Optional HUD controls use the same BCL bridge, never a typed module reference.
        // Persist only user choices; these settings never validate or replace map data.
        private bool _dataPresent;
        private void LoadControls()
        {
            _dataPresent=File.Exists(Path.Combine(CacheDirectory,"MapViewer.nav"))&&File.Exists(Path.Combine(CacheDirectory,"MapViewer.props"));
            try {
                foreach(string line in File.ReadAllLines(Path.Combine(CacheDirectory,"MapViewer.settings.ini"))) {
                    if(line=="ShowGrid=true")ShowGrid=true;else if(line=="ShowGrid=false")ShowGrid=false;
                    else if(line=="Diagnostics=true")Diagnostics=true;else if(line=="Diagnostics=false")Diagnostics=false;
                }
            }catch { /* Missing preferences retain defaults. */ }
        }
        private void SaveControls()
        {
            try { File.WriteAllText(Path.Combine(CacheDirectory,"MapViewer.settings.ini"),
                "ShowGrid="+(ShowGrid?"true":"false")+Environment.NewLine+"Diagnostics="+(Diagnostics?"true":"false")+Environment.NewLine,new UTF8Encoding(false)); }
            catch { /* A read-only install still permits this session's controls. */ }
        }
        private Dictionary<string,object> Controls()
        { return new Dictionary<string,object>{{"api",BridgeName},{"ok",true},{"dataPresent",_dataPresent},{"showGrid",ShowGrid},{"diagnostics",Diagnostics}}; }
        private long _feedbackMs;
        private string _feedbackKey="";
        private void LogConsumer(object[] args)
        {
            if(!Diagnostics||Hud.TextLog==null)return;
            string consumer=Convert.ToString(args[1]),action=Convert.ToString(args[2]);
            if(consumer.Length>40||action.Length>60||consumer.Contains(";")||action.Contains(";")||consumer.Contains("\n")||action.Contains("\n"))return;
            uint target=Convert.ToUInt32(args[3]);long now=Hud.Game.CurrentRealTimeMilliseconds;
            string key=consumer+":"+action+":"+target;
            if(key==_feedbackKey&&now-_feedbackMs<1000)return;
            _feedbackKey=key;_feedbackMs=now;
            try { Hud.TextLog.Log("s7o_MapViewer","CONSUMER|tick="+Hud.Game.CurrentGameTick+";epoch="+_epoch
                +";consumer="+consumer+";event="+action+";target="+target+";xyz="+F(Number(args[4]))+","+F(Number(args[5]))+","+F(Number(args[6])),false,true); }catch { }
        }
        private static float Number(object value) { if(value==null||value is bool||value is string)throw new ArgumentException();float n=Convert.ToSingle(value);if(!Finite(n))throw new ArgumentException();return n; }
        private Dictionary<string,object> Envelope()
        {
            return new Dictionary<string,object> { {"api",BridgeName},{"session",_session},{"epoch",_epoch},{"version",_version},
                {"worldId",_worldId},{"worldSno",_worldSno},{"sampleMs",_sampleMs},{"authoritative",false},{"coverage","ObservedScenesOnly"},{"completeCollisionCoverage",false},{"shapeState",_placedCache==null?"Missing":_placedCache.ShapeState} };
        }
        private Dictionary<string,object> Error(string reason)
        {
            var result=Envelope();result["ok"]=false;result["state"]="Unknown";result["reason"]=reason;return result;
        }
        private Dictionary<string,object> Status()
        {
            var result=Envelope();TemplateCache cache=_cache;
            result["ok"]=Current();result["state"]=_cacheState;result["sceneId"]=_currentSceneId;
            result["position"]=new float[] {_playerX,_playerY,_playerZ};result["observedScenes"]=_placements.Count;
            result["loadedSceneSource"]=_loadedSceneState;result["loadedSceneCount"]=_loadedSceneCount;result["loadedSceneNearby"]=_loadedSceneNearby;
            result["templates"]=cache==null?0:cache.Templates.Count;result["cells"]=cache==null?0:cache.CellCount;
            result["buildKey"]=cache==null?"":cache.BuildKey;result["gameVersion"]=cache==null?"":cache.GameVersion;
            result["shapeState"]=cache==null?"Missing":cache.ShapeState;result["nativeBoxCandidates"]=_nativeBoxes.Count;result["completeCollisionCoverage"]=false;
            result["capabilities"]=new string[] {"boundedRouteCandidates","bufferedStraightCorridors","nativeCollisionBoxCandidates","player","scenes","cells","pointCandidate","segmentCoverage","obstacleCandidates","nativeHeightSamples","nativeMonsterXYZ"};
            return result;
        }
        private object Scenes()
        {
            var values=new List<object>();
            foreach(var p in _placements.Values)values.Add(new Dictionary<string,object> { {"sceneId",p.SceneId},{"navMeshId",p.NavMeshId},
                {"sno",p.Sno},{"code",p.Code},{"origin",new float[] {p.X,p.Y,p.Z}},{"xyBounds",new float[] {p.X,p.Y,p.MaxX,p.MaxY}},
                {"templateAvailable",p.Template!=null},{"originConflict",p.OriginConflict},{"heightState",p.Template==null?"Unavailable":p.Template.GridState},{"cellCount",p.Template==null?0:p.Template.Cells.Length} });
            var result=Envelope();result["ok"]=true;result["scenes"]=values.ToArray();return result;
        }
        private object Cells(uint sceneId)
        {
            Placement p;if(!_placements.TryGetValue(sceneId,out p)||p.Template==null||p.OriginConflict)return Error("SceneTemplateOrOriginUnknown");
            var cells=p.Template.Cells;if(cells.Length>MaximumQueryCells)return Error("QueryBudget: request a smaller Scene");
            var values=new object[cells.Length];
            for(int i=0;i<cells.Length;i++) {
                var c=cells[i];values[i]=new Dictionary<string,object> { {"index",i},{"bounds",new float[] {c.MinX+p.X,c.MinY+p.Y,c.MinZ+p.Z,c.MaxX+p.X,c.MaxY+p.Y,c.MaxZ+p.Z}},
                    {"flags",c.Flags},{"walk",(c.Flags&1)!=0},{"fly",(c.Flags&2)!=0},{"neighbourCount",c.NeighbourCount},{"neighbourIndex",c.NeighbourIndex} };
            }
            var result=Envelope();result["ok"]=true;result["sceneId"]=sceneId;result["cells"]=values;
            result["neighbourGraphValidated"]=false;return result;
        }
        private object Point(float x,float y,float z)
        {
            foreach(var b in _nativeBoxes) {
                Interval span;if(!BoxInterval(b,x,y,z,x,y,z,0,out span))continue;
                var blocked=Error("NativeCollisionBoxCandidate");blocked["state"]="BlockedCandidate";blocked["obstacleCandidate"]=BoxValue(b);return blocked;
            }
            Obstacle footprint;
            if(FootprintAt(x,y,z,out footprint)) {
                var blocked=Error("ObstacleFootprintCandidate");blocked["obstacleCandidate"]=ObstacleValue(footprint);return blocked;
            }
            bool nonWalk=false;int scanned=0;
            foreach(var p in _placements.Values) {
                if(p.Template==null||p.OriginConflict||x<p.X||x>p.MaxX||y<p.Y||y>p.MaxY)continue;
                for(int i=0;i<p.Template.Cells.Length;i++) {
                    if(scanned++>=MaximumQueryCells)return Error("QueryBudget");var c=p.Template.Cells[i];
                    if(x<c.MinX+p.X||x>c.MaxX+p.X||y<c.MinY+p.Y||y>c.MaxY+p.Y||z<c.MinZ+p.Z-FloorZTolerance||z>c.MaxZ+p.Z+FloorZTolerance)continue;
                    if((c.Flags&1)==0){nonWalk=true;continue;}
                    var result=Envelope();result["ok"]=true;result["state"]="WalkCandidate";result["sceneId"]=p.SceneId;
                    result["cellIndex"]=i;result["flags"]=c.Flags;result["floorZRange"]=new float[] {c.MinZ+p.Z,c.MaxZ+p.Z};float hz;uint hf;int hi;if(HeightAt(p,x,y,out hz,out hf,out hi)){result["nativeFloorZ"]=hz;result["heightSource"]="CASC_NavMeshSquare";result["gridIndex"]=hi;}
                    return result;
                }
            }
            var missing=Error(nonWalk?"NativeCellHasNoWalkFlag":"NoWalkCellCoverage");missing["state"]=nonWalk?"NoWalkCandidate":"Unknown";return missing;
        }

        private object Height(float x,float y,float referenceZ)
        {
            Placement best=null;float bestZ=0;uint bestFlags=0;int bestIndex=-1;float gap=Single.MaxValue;bool pending=false;
            foreach(var p in _placements.Values) {
                if(x<p.X||x>=p.MaxX||y<p.Y||y>=p.MaxY||p.OriginConflict)continue;
                float z;uint flags;int index;
                if(HeightAt(p,x,y,out z,out flags,out index)) {
                    float delta=Math.Abs(z-referenceZ);
                    if(delta<gap){gap=delta;best=p;bestZ=z;bestFlags=flags;bestIndex=index;}
                } else if(p.Template!=null&&p.Template.GridWidth>0&&p.Template.Grid==null){RequestHeight(p.Template);pending=true;}
            }
            if(best==null)return Error(pending?"NativeHeightPending":"NativeHeightUnknown");
            Template t=best.Template;int gx=bestIndex%t.GridWidth,gy=bestIndex/t.GridWidth;
            var result=Envelope();result["ok"]=true;result["state"]="NativeHeightSample";result["sceneId"]=best.SceneId;
            result["height"]=bestZ;result["queryXY"]=new float[] {x,y};result["referenceZGap"]=gap;result["gridIndex"]=bestIndex;
            result["samplePosition"]=new float[] {best.X+(gx+0.5f)*t.StepX,best.Y+(gy+0.5f)*t.StepY,bestZ};
            result["sampleSpacing"]=new float[] {t.StepX,t.StepY};result["flags"]=bestFlags;result["source"]="CASC_NavMeshSquare";
            result["referenceMatchesSample"]=gap<=0.25f;result["surfaceInterpolationValidated"]=false;return result;
        }
        private object Monsters()
        {
            var values=new object[_targets.Count];
            for(int i=0;i<values.Length;i++) {
                TargetSample t=_targets[i];Placement p;_placements.TryGetValue(t.SceneId,out p);
                var value=new Dictionary<string,object> {{"acdId",t.AcdId},{"sceneId",t.SceneId},{"elite",t.Elite},
                    {"position",new float[] {t.X,t.Y,t.Z}},{"positionSource","FreeHUD_FloorCoordinate"}};
                float z;uint flags;int index;
                if(HeightAt(p,t.X,t.Y,out z,out flags,out index)) {value["nativeFloorZ"]=z;value["entityVsGridZGap"]=t.Z-z;value["gridIndex"]=index;}
                else value["nativeHeightState"]="UnknownOrPending";
                values[i]=value;
            }
            var result=Envelope();result["ok"]=true;result["monsters"]=values;return result;
        }
        private static string F(float value) { return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
        private void LogHeightSnapshot()
        {
            if(!Diagnostics||Hud.TextLog==null)return;
            long now=Hud.Game.CurrentRealTimeMilliseconds;
            if(_lastHeightLogMs>0&&now>=_lastHeightLogMs&&now-_lastHeightLogMs<2000)return;
            _lastHeightLogMs=now;Placement current;_placements.TryGetValue(_currentSceneId,out current);
            int ready=0,conflicts=0;foreach(var p in _placements.Values){if(p.OriginConflict)conflicts++;if(p.Template!=null&&p.Template.Grid!=null)ready++;}
            float playerHeight;uint flags;int index;bool known=HeightAt(current,_playerX,_playerY,out playerHeight,out flags,out index);
            int above=0,below=0;TargetSample best=new TargetSample();int tier=99;double distance=Double.MaxValue;
            foreach(var t in _targets) {
                if(t.Z>_playerZ+2)above++;if(t.Z<_playerZ-2)below++;
                int priority=Math.Abs(t.Z-_playerZ)>2?(t.Elite?0:1):(t.Elite?2:3);double d=DistanceSquared(t.X,t.Y,_playerX,_playerY);
                if(priority<tier||priority==tier&&d<distance){best=t;tier=priority;distance=d;}
            }
            string target="none";
            if(tier<99) {
                Placement p;_placements.TryGetValue(best.SceneId,out p);float z;uint f;int i;bool heightKnown=HeightAt(p,best.X,best.Y,out z,out f,out i);
                target="acd="+best.AcdId+",elite="+best.Elite+",scene="+best.SceneId+",nativeXYZ="+F(best.X)+","+F(best.Y)+","+F(best.Z)
                    +",deltaZ="+F(best.Z-_playerZ)+",gridFloorZ="+(heightKnown?F(z):"UNKNOWN")+",gap="+(heightKnown?F(best.Z-z):"UNKNOWN")
                    +",sceneIsPlayerScene="+(best.SceneId==_currentSceneId)+",sceneSno="+(p==null?0:p.Sno)+",originZ="+(p==null?"UNKNOWN":F(p.Z))
                    +",heightState="+(p==null||p.Template==null?"Unavailable":p.Template.GridState)+",originConflict="+(p!=null&&p.OriginConflict);
            }
            string line="HEIGHT|utc="+DateTime.UtcNow.ToString("o")+";tick="+Hud.Game.CurrentGameTick+";epoch="+_epoch+";world="+_worldId
                +";scene="+_currentSceneId+";sno="+(current==null?0:current.Sno)+";sceneOriginZ="+(current==null?"UNKNOWN":F(current.Z))+";sceneOriginXYZ="+(current==null?"UNKNOWN":F(current.X)+","+F(current.Y)+","+F(current.Z))
                +";playerXYZ="+F(_playerX)+","+F(_playerY)+","+F(_playerZ)+";playerGridFloorZ="+(known?F(playerHeight):"UNKNOWN")
                +";playerGap="+(known?F(_playerZ-playerHeight):"UNKNOWN")+";observedScenes="+_placements.Count+";heightReadyScenes="+ready
                +";loadedSceneSource="+_loadedSceneState+";loadedSceneCount="+_loadedSceneCount+";loadedSceneNearby="+_loadedSceneNearby
                +";originConflicts="+conflicts+";targetsAbove="+above+";targetsBelow="+below+";target="+target
                +";lastPlacementChange="+_lastPlacementChange+";nativeNodesDrawn="+_lastNativeNodesDrawn+";nativeCellsDrawn="+_lastNativeCellsDrawn
                +";nativeNoWalkCellsDrawn="+_lastNoWalkCellsDrawn+";shapeState="+(_placedCache==null?"Missing":_placedCache.ShapeState)+";nativeBoxCandidates="+_nativeBoxes.Count+";footprintCandidates="+_footprintCandidates.Count+";source=LOCAL_NATIVE;input=NO";
            try { Hud.TextLog.Log("s7o_MapViewer",line,false,true);LogSceneCoverage();LogFootprintProbe();LogNativeBoxProbe(); }catch { }
        }

        // Optional boundary diagnostics run only with the existing two-second snapshot.
        // First/current scene IDs separate discovery from a height/drawing gap; never invent a neighbor.
        private readonly Dictionary<uint,string> _sceneCoverageLogged=new Dictionary<uint,string>();
        private void LogSceneCoverage()
        {
            foreach(var p in _drawingPlacements) {
                string height=p.Template==null?"MissingTemplate":p.OriginConflict?"OriginConflict":p.Template.GridState;
                string key=p.NavMeshId+":"+p.X+":"+p.Y+":"+p.Z+":"+height;
                string previous;if(_sceneCoverageLogged.TryGetValue(p.SceneId,out previous)&&previous==key)continue;
                if(!_sceneCoverageLogged.ContainsKey(p.SceneId)&&_sceneCoverageLogged.Count>=MaximumPlacements)_sceneCoverageLogged.Clear();
                _sceneCoverageLogged[p.SceneId]=key;
                Hud.TextLog.Log("s7o_MapViewer","SCENE|tick="+Hud.Game.CurrentGameTick+";epoch="+_epoch
                    +";world="+_worldId+";scene="+p.SceneId+";playerScene="+_currentSceneId+";source="+p.Source
                    +";sno="+p.Sno+";navMeshId="+p.NavMeshId+";code="+p.Code+";bounds="+F(p.X)+","+F(p.Y)+","+F(p.MaxX)+","+F(p.MaxY)
                    +";originZ="+F(p.Z)+";boundaryYards="+F((float)Math.Sqrt(PlacementGapSquared(p,_playerX,_playerY)))
                    +";heightState="+height,false,true);
            }
            int missing=0;TargetSample closest=new TargetSample();bool hasClosest=false;double nearest=Double.MaxValue;string reason="none";
            foreach(var t in _targets) {
                double distance=DistanceSquared(t.X,t.Y,_playerX,_playerY);if(distance>DrawRadius*DrawRadius)continue;
                Placement p;float z;uint flags;int index;
                if(_placements.TryGetValue(t.SceneId,out p)&&HeightAt(p,t.X,t.Y,out z,out flags,out index))continue;
                missing++;if(distance>=nearest)continue;nearest=distance;closest=t;hasClosest=true;
                reason=p==null?"MissingPlacement":p.Template==null?"MissingTemplate":p.OriginConflict?"OriginConflict"
                    :t.X<p.X||t.X>=p.MaxX||t.Y<p.Y||t.Y>=p.MaxY?"ActorOutsideSceneBounds"
                    :p.Template.Grid==null?p.Template.GridState:"HeightUnavailable";
            }
            Hud.TextLog.Log("s7o_MapViewer","COVERAGE|tick="+Hud.Game.CurrentGameTick+";epoch="+_epoch
                +";world="+_worldId+";playerScene="+_currentSceneId+";nearbyScenes="+_drawingPlacements.Count
                +";targetsWithoutHeight="+missing+";nearest="+(!hasClosest?"none":"acd="+closest.AcdId+",scene="+closest.SceneId
                    +",xyz="+F(closest.X)+","+F(closest.Y)+","+F(closest.Z)+",reason="+reason),false,true);
        }

        private static bool Slab(double from,double delta,double minimum,double maximum,ref double a,ref double b)
        {
            if(Math.Abs(delta)<1e-9)return from>=minimum&&from<=maximum;
            double first=(minimum-from)/delta,last=(maximum-from)/delta;if(first>last){double swap=first;first=last;last=swap;}
            a=Math.Max(a,first);b=Math.Min(b,last);return a<=b;
        }
        private object Segment(float x,float y,float z,float tx,float ty,float tz,float radius)
        {
            if(radius<0||radius>10)return Error("ClearanceRadiusRange");
            var spans=new List<Interval>();int scanned=0;bool truncated=false;
            double minX=Math.Min(x,tx),maxX=Math.Max(x,tx),minY=Math.Min(y,ty),maxY=Math.Max(y,ty);
            foreach(var p in _placements.Values) {
                if(p.Template==null||p.OriginConflict||p.MaxX<minX||p.X>maxX||p.MaxY<minY||p.Y>maxY)continue;
                foreach(var c in p.Template.Cells) {
                    if(scanned++>=MaximumQueryCells){truncated=true;break;}if((c.Flags&1)==0)continue;
                    double a=0,b=1;
                    if(Slab(x,tx-x,c.MinX+p.X,c.MaxX+p.X,ref a,ref b)&&Slab(y,ty-y,c.MinY+p.Y,c.MaxY+p.Y,ref a,ref b)
                        &&Slab(z,tz-z,c.MinZ+p.Z-FloorZTolerance,c.MaxZ+p.Z+FloorZTolerance,ref a,ref b))spans.Add(new Interval {Start=a,End=b});
                }
                if(truncated)break;
            }
            if(truncated)return Error("QueryBudget");
            spans.Sort((a,b)=>a.Start.CompareTo(b.Start));double reached=0;
            foreach(var span in spans){if(span.Start>reached+1e-7)break;reached=Math.Max(reached,span.End);}
            var result=Envelope();result["ok"]=true;result["state"]=spans.Count>0&&reached>=1-1e-7?"CoveredByWalkCells":"UnknownGap";
            result["coveredFractionFromStart"]=reached;result["obstacleCandidates"]=ObstaclesOnSegment(x,y,z,tx,ty,tz);
            var hits=new List<object>();foreach(var box in _nativeBoxes){Interval span;if(BoxInterval(box,x,y,z,tx,ty,tz,radius,out span))hits.Add(BoxValue(box));}
            result["nativeCollisionCandidates"]=hits.ToArray();result["terrainCoverageState"]=result["state"];result["propClearanceRadius"]=radius;
            if(hits.Count>0)result["state"]="BlockedByCollisionBoxCandidate";
            result["clearanceValidated"]=false;result["dashPassabilityValidated"]=false;return result;
        }
        private static double DistanceSquared(float x,float y,float tx,float ty) { double dx=x-tx,dy=y-ty;return dx*dx+dy*dy; }
        private Dictionary<string,object> ObstacleValue(Obstacle o)
        {
            var value=new Dictionary<string,object> { {"acdId",o.AcdId},{"sno",o.Sno},{"sceneId",o.SceneId},{"code",o.Code},{"kind",o.Kind},{"position",new float[] {o.X,o.Y,o.Z}},
                {"radius",o.Radius},{"radiusSource",o.RadiusSource},{"shape","ReportedCircleCandidate"},{"radiusKnown",o.Radius>0},{"floorZKnown",o.FloorZKnown},{"nativeFloorZ",o.FloorZKnown?(object)o.FloorZ:null},{"operated",o.Operated},{"clickable",o.Clickable},{"collisionValidated",false} };
            BoxDefinition d;if(_placedCache!=null&&_placedCache.BoxDefinitions.TryGetValue(o.Sno,out d)) {
                value["nativeLocalBox"]=new float[]{d.X,d.Y,d.Z,d.HX,d.HY,d.HZ};value["boxPlacementKnown"]=HasNativeBox(o);
            }else value["boxPlacementKnown"]=false;
            return value;
        }
        private object Obstacles()
        {
            var values=new List<object>();foreach(var o in _obstacles)if(!HasNativeBox(o))values.Add(ObstacleValue(o));foreach(var b in _nativeBoxes)values.Add(BoxValue(b));
            var result=Envelope();result["ok"]=true;result["obstacles"]=values.ToArray();return result;
        }
        private object[] ObstaclesOnSegment(float x,float y,float z,float tx,float ty,float tz)
        {
            var values=new List<object>();double dx=tx-x,dy=ty-y,length=dx*dx+dy*dy;
            foreach(var o in _obstacles) {
                if(HasNativeBox(o))continue;
                double t=length==0?0:Math.Max(0,Math.Min(1,((o.X-x)*dx+(o.Y-y)*dy)/length));
                double px=x+t*dx,py=y+t*dy,pz=z+t*(tz-z);
                // Same-floor candidates only; a circular actor footprint is not a validated collision polygon.
                if(Math.Abs((o.FloorZKnown?o.FloorZ:o.Z)-pz)>2||o.Radius<=0)continue;
                double ox=o.X-px,oy=o.Y-py;if(ox*ox+oy*oy<=o.Radius*o.Radius)values.Add(ObstacleValue(o));
            }
            return values.ToArray();
        }


        // Native NavCells own exact XY boundaries; samples supply only their elevation.
        // Never replace boundary rectangles with centre-to-centre height wires: pillars/pits lose detail.
        public void PaintWorld(WorldLayer layer)
        {
            if(!Current()||Hud.Render.UiHidden)return;
            bool ground=layer==WorldLayer.Ground;
            if(!ShowGrid||ground&&!ShowWorldCells||!ground&&!ShowMinimapCells)return;
            if(!ground&&(Hud.Render.MinimapUiElement==null||!Hud.Render.MinimapUiElement.Visible))return;
            int drawn=0,inspected=0,edges=0;
            foreach(var p in _drawingPlacements) {
                if(p.Template==null||p.OriginConflict)continue;
                foreach(var c in p.Template.Cells) {
                    if(drawn>=MaximumDrawCells||edges>=4800||inspected++>=8192)break;
                    if((c.Flags&1)==0)continue;
                    float minX=c.MinX+p.X,minY=c.MinY+p.Y,maxX=c.MaxX+p.X,maxY=c.MaxY+p.Y;
                    float nx=Math.Max(minX,Math.Min(_playerX,maxX)),ny=Math.Max(minY,Math.Min(_playerY,maxY));
                    if(DistanceSquared(nx,ny,_playerX,_playerY)>DrawRadius*DrawRadius)continue;
                    int before=edges;
                    DrawCellEdge(ground,p,c,minX,minY,maxX,minY,ref edges);
                    DrawCellEdge(ground,p,c,maxX,minY,maxX,maxY,ref edges);
                    DrawCellEdge(ground,p,c,maxX,maxY,minX,maxY,ref edges);
                    DrawCellEdge(ground,p,c,minX,maxY,minX,minY,ref edges);
                    if(edges>before)drawn++;
                }
                if(drawn>=MaximumDrawCells||edges>=4800||inspected>=8192)break;
            }
            if(ground){_lastNativeCellsDrawn=drawn;_lastNativeNodesDrawn=0;}
            DrawInteriorGrid(ground);
            if(ShowNativeNoWalkCells)DrawNoWalkCells(ground);else if(ground)_lastNoWalkCellsDrawn=0;
            if(ShowHeightSamplePreview)DrawHeightPreview(ground);
            if(ShowObstacleCandidates)DrawNativeBoxes(ground);
            if(ground&&ShowObstacleCandidates)foreach(var o in _obstacles) {
                if(HasNativeBox(o))continue;
                if(DistanceSquared(o.X,o.Y,_playerX,_playerY)>DrawRadius*DrawRadius)continue;
                float z=(o.FloorZKnown?o.FloorZ:o.Z)+0.1f;
                if(ShowReportedRadiusCandidates&&o.Radius>0&&o.FloorZKnown)_obstacleBrush.DrawWorldEllipse(o.Radius,20,o.X,o.Y,z);
                else if(ShowUnknownCandidates) {
                    // A cross marks an unresolved actor; its display size is not collision evidence.
                    _unknownBrush.DrawLineWorld(o.X-0.6f,o.Y-0.6f,z,o.X+0.6f,o.Y+0.6f,z);
                    _unknownBrush.DrawLineWorld(o.X-0.6f,o.Y+0.6f,z,o.X+0.6f,o.Y-0.6f,z);
                }
            }
        }
        // Subdivide native walk rectangles only. Display spacing never changes topology.
        private void DrawInteriorGrid(bool ground)
        {
            float step=Math.Max(2.5f,Math.Min(10f,InteriorGridSpacing));int edges=0,scanned=0;
            foreach(var p in _drawingPlacements) {
                if(p.Template==null||p.OriginConflict)continue;
                foreach(var c in p.Template.Cells) {
                    if(edges>=1800||scanned++>=8192)return;if((c.Flags&1)==0)continue;
                    float left=Math.Max(p.X+c.MinX,_playerX-DrawRadius),right=Math.Min(p.X+c.MaxX,_playerX+DrawRadius);
                    float top=Math.Max(p.Y+c.MinY,_playerY-DrawRadius),bottom=Math.Min(p.Y+c.MaxY,_playerY+DrawRadius);
                    if(right<=left||bottom<=top)continue;
                    for(float x=(float)Math.Ceiling(left/step)*step;x<right&&edges<1800;x+=step)
                        if(x>p.X+c.MinX+0.01f&&x<p.X+c.MaxX-0.01f)DrawCellEdge(ground,p,c,x,top,x,bottom,ref edges);
                    for(float y=(float)Math.Ceiling(top/step)*step;y<bottom&&edges<1800;y+=step)
                        if(y>p.Y+c.MinY+0.01f&&y<p.Y+c.MaxY-0.01f)DrawCellEdge(ground,p,c,left,y,right,y,ref edges);
                }
            }
        }


        private void DrawNativeBoxes(bool ground)
        {
            foreach(var b in _nativeBoxes) {
                if(DistanceSquared(b.X,b.Y,_playerX,_playerY)>DrawRadius*DrawRadius)continue;
                float z=b.FloorZ+0.09f,firstX=0,firstY=0,lastX=0,lastY=0;
                for(int i=0;i<4;i++) {
                    float x=i==0||i==3?-b.HX:b.HX,y=i<2?-b.HY:b.HY;
                    float wx=b.X+b.Cos*x-b.Sin*y,wy=b.Y+b.Sin*x+b.Cos*y;
                    if(i==0){firstX=wx;firstY=wy;}else DrawMapLine(_noWalkBrush,ground,lastX,lastY,z,wx,wy,z);
                    lastX=wx;lastY=wy;
                }
                DrawMapLine(_noWalkBrush,ground,lastX,lastY,z,firstX,firstY,z);
            }
        }
        private void DrawNoWalkCells(bool ground)
        {
            int drawn=0,inspected=0;
            foreach(var p in _drawingPlacements) {
                if(p.Template==null||p.OriginConflict)continue;
                foreach(var c in p.Template.Cells) {
                    if(drawn>=400||inspected++>=8192)break;
                    if((c.Flags&1)!=0)continue;
                    float x=p.X+c.MinX,y=p.Y+c.MinY,tx=p.X+c.MaxX,ty=p.Y+c.MaxY;
                    float nx=Math.Max(x,Math.Min(_playerX,tx)),ny=Math.Max(y,Math.Min(_playerY,ty));
                    if(DistanceSquared(nx,ny,_playerX,_playerY)>DrawRadius*DrawRadius)continue;
                    // Show actual source bounds, not a new surface inferred from player Z or an actor radius.
                    float z=p.Z+c.MinZ+0.07f,tz=p.Z+c.MaxZ+0.07f;
                    DrawNoWalkRectangle(ground,x,y,tx,ty,z);
                    if(ground&&tz-z>0.001f)DrawNoWalkRectangle(ground,x,y,tx,ty,tz);
                    float cx=(x+tx)*0.5f,cy=(y+ty)*0.5f;
                    if(DistanceSquared(cx,cy,_playerX,_playerY)<=DrawRadius*DrawRadius) {
                        float r=Math.Min(1.25f,Math.Min(tx-x,ty-y)*0.2f);
                        DrawMapLine(_noWalkBrush,ground,cx-r,cy-r,z,cx+r,cy+r,z);
                        DrawMapLine(_noWalkBrush,ground,cx-r,cy+r,z,cx+r,cy-r,z);
                    }
                    drawn++;
                }
                if(drawn>=400||inspected>=8192)break;
            }
            if(ground)_lastNoWalkCellsDrawn=drawn;
        }
        private void DrawNoWalkRectangle(bool ground,float x,float y,float tx,float ty,float z)
        {
            DrawMapLine(_noWalkBrush,ground,x,y,z,tx,y,z);DrawMapLine(_noWalkBrush,ground,tx,y,z,tx,ty,z);
            DrawMapLine(_noWalkBrush,ground,tx,ty,z,x,ty,z);DrawMapLine(_noWalkBrush,ground,x,ty,z,x,y,z);
        }
        private void DrawCellEdge(bool ground,Placement p,Cell c,float x,float y,float tx,float ty,ref int edges)
        {
            if(edges>=4800)return;
            // Flat cells already provide a native surface Z; no player-Z fallback is involved.
            if(c.MaxZ-c.MinZ<=0.001f) {
                float z=p.Z+c.MinZ+0.05f;DrawTerrainEdge(ground,x,y,z,tx,ty,z);edges++;return;
            }
            Template t=p.Template;if(t.Grid==null||t.StepX<=0||t.StepY<=0)return;
            float length=Math.Max(Math.Abs(tx-x),Math.Abs(ty-y)),step=tx==x?t.StepY:t.StepX;
            int pieces=Math.Max(1,Math.Min(512,(int)Math.Ceiling(length/step)));
            for(int i=0;i<pieces&&edges<4800;i++) {
                float a=(float)i/pieces,b=(float)(i+1)/pieces,m=(a+b)*0.5f;
                float px=x+(tx-x)*m,py=y+(ty-y)*m;
                // Sample just inside this cell; keep the rendered XY exactly on its native edge.
                float qx=Math.Max(p.X+c.MinX+0.01f,Math.Min(px,p.X+c.MaxX-0.01f));
                float qy=Math.Max(p.Y+c.MinY+0.01f,Math.Min(py,p.Y+c.MaxY-0.01f));
                float z;uint flags;int index;
                if(!HeightAt(p,qx,qy,out z,out flags,out index)||z<c.MinZ+p.Z-0.15f||z>c.MaxZ+p.Z+0.15f)continue;
                DrawTerrainEdge(ground,x+(tx-x)*a,y+(ty-y)*a,z+0.05f,x+(tx-x)*b,y+(ty-y)*b,z+0.05f);edges++;
            }
        }
        private void DrawHeightPreview(bool ground)
        {
            int drawn=0,inspected=0,stride=Math.Max(1,Math.Min(8,NativeGridDrawStride));
            foreach(var p in _drawingPlacements) {
                if(p.Template==null||p.OriginConflict)continue;
                Template t=p.Template;NativeGrid grid=t.Grid;if(grid==null)continue;
                if(Math.Abs((p.MaxX-p.X)-t.GridWidth*t.StepX)>0.1||Math.Abs((p.MaxY-p.Y)-t.GridHeight*t.StepY)>0.1)continue;
                int minX=Math.Max(0,(int)Math.Floor((_playerX-DrawRadius-p.X)/t.StepX)),maxX=Math.Min(t.GridWidth-1,(int)Math.Ceiling((_playerX+DrawRadius-p.X)/t.StepX));
                int minY=Math.Max(0,(int)Math.Floor((_playerY-DrawRadius-p.Y)/t.StepY)),maxY=Math.Min(t.GridHeight-1,(int)Math.Ceiling((_playerY+DrawRadius-p.Y)/t.StepY));
                if(minX>maxX||minY>maxY)continue;
                minX=((minX+stride-1)/stride)*stride;minY=((minY+stride-1)/stride)*stride;
                for(int gy=minY;gy<=maxY;gy+=stride) {
                    for(int gx=minX;gx<=maxX;gx+=stride) {
                        if(inspected++>=8192||drawn>=MaximumDrawCells)break;
                        int index=gy*t.GridWidth+gx;if((grid.Flags[index]&1)==0)continue;
                        float x=p.X+(gx+0.5f)*t.StepX,y=p.Y+(gy+0.5f)*t.StepY,z=p.Z+grid.Heights[index]+0.05f;
                        if(!Finite(z)||DistanceSquared(x,y,_playerX,_playerY)>DrawRadius*DrawRadius)continue;
                        if(!NativeWalkAt(p,x,y,z-0.05f))continue;
                        drawn++;
                        DrawGridLink(ground,p,grid,gx,gy,gx+stride,gy,x,y,z);
                        DrawGridLink(ground,p,grid,gx,gy,gx,gy+stride,x,y,z);
                    }
                    if(inspected>=8192||drawn>=MaximumDrawCells)break;
                }
                if(inspected>=8192||drawn>=MaximumDrawCells)break;
            }
            if(ground)_lastNativeNodesDrawn=drawn;
        }
        private static bool NativeWalkAt(Placement p,float x,float y,float z)
        {
            if(p==null||p.Template==null||p.OriginConflict)return false;
            foreach(var c in p.Template.Cells)if((c.Flags&1)!=0&&x>=p.X+c.MinX&&x<=p.X+c.MaxX&&y>=p.Y+c.MinY&&y<=p.Y+c.MaxY
                &&z>=p.Z+c.MinZ-0.15f&&z<=p.Z+c.MaxZ+0.15f)return true;
            return false;
        }
        private bool FootprintAt(float x,float y,float z,out Obstacle found)
        {
            foreach(var o in _footprintCandidates)if(!HasNativeBox(o)&&o.Radius>0&&o.FloorZKnown&&Math.Abs(o.FloorZ-z)<=2&&DistanceSquared(x,y,o.X,o.Y)<o.Radius*o.Radius){found=o;return true;}
            found=new Obstacle();return false;
        }
        private void DrawTerrainEdge(bool ground,float x,float y,float z,float tx,float ty,float tz)
        {
            // Clip only native placed boxes. Unknown circles cannot carve false holes or erase narrow gaps.
            _edgeCuts.Clear();double dx=tx-x,dy=ty-y,length=dx*dx+dy*dy;if(length<=0)return;
            foreach(var box in _nativeBoxes) {
                float bx=Math.Abs(box.Cos)*box.HX+Math.Abs(box.Sin)*box.HY,by=Math.Abs(box.Sin)*box.HX+Math.Abs(box.Cos)*box.HY;
                if(Math.Max(x,tx)<box.X-bx||Math.Min(x,tx)>box.X+bx||Math.Max(y,ty)<box.Y-by||Math.Min(y,ty)>box.Y+by)continue;
                Interval cut;if(BoxInterval(box,x,y,z,tx,ty,tz,0,out cut)&&cut.Start<cut.End)_edgeCuts.Add(cut);
            }
            if(_edgeCuts.Count==0){DrawEdge(ground,x,y,z,tx,ty,tz);return;}
            _edgeCuts.Sort((a,b)=>a.Start.CompareTo(b.Start));double at=0;
            foreach(var cut in _edgeCuts) {
                if(cut.Start>at)DrawEdgePart(ground,x,y,z,tx,ty,tz,at,cut.Start);
                at=Math.Max(at,cut.End);if(at>=1)return;
            }
            if(at<1)DrawEdgePart(ground,x,y,z,tx,ty,tz,at,1);
        }
        private void DrawEdgePart(bool ground,float x,float y,float z,float tx,float ty,float tz,double a,double b)
        {
            DrawEdge(ground,(float)(x+(tx-x)*a),(float)(y+(ty-y)*a),(float)(z+(tz-z)*a),
                (float)(x+(tx-x)*b),(float)(y+(ty-y)*b),(float)(z+(tz-z)*b));
        }
        private void LogFootprintProbe()
        {
            IActor selected=Hud.Game.SelectedActor;
            if(selected!=null&&selected.WorldId==_worldId&&selected.GizmoType!=GizmoType.Invalid)LogActorProbe(selected);
            int count=0;
            foreach(var o in _footprintCandidates) {
                if(count++>=4)break;
                Placement p;_placements.TryGetValue(o.SceneId,out p);
                float z;uint flags;int index;bool known=HeightAt(p,o.X,o.Y,out z,out flags,out index);
                Hud.TextLog.Log("s7o_MapViewer","FOOTPRINT|tick="+Hud.Game.CurrentGameTick+";world="+_worldId+";acd="+o.AcdId+";sno="+o.Sno+";code="+o.Code
                    +";kind="+o.Kind+";scene="+o.SceneId+";sceneSno="+(p==null?0:p.Sno)+";xyz="+F(o.X)+","+F(o.Y)+","+F(o.Z)+";radius="+F(o.Radius)+";radiusSource="+o.RadiusSource+";radiusKnown="+(o.Radius>0)+";nativeFloorZ="+(o.FloorZKnown?F(o.FloorZ):"UNKNOWN")
                    +";gridZ="+(known?F(z):"UNKNOWN")+";gridFlags="+(index>=0?flags.ToString("X"):"UNKNOWN")
                    +";nativeCellWalk="+(known&&NativeWalkAt(p,o.X,o.Y,z))+";collisionValidated=False",false,true);
            }
        }
        private void LogNativeBoxProbe()
        {
            int count=0;foreach(var b in _nativeBoxes) {
                if(DistanceSquared(b.X,b.Y,_playerX,_playerY)>45f*45f||count++>=6)continue;
                Interval playerSpan;bool inside=BoxInterval(b,_playerX,_playerY,_playerZ,_playerX,_playerY,_playerZ,0,out playerSpan);
                Hud.TextLog.Log("s7o_MapViewer","BOX|tick="+Hud.Game.CurrentGameTick+";scene="+b.SceneId+";sno="+b.Definition.Sno+";kind="+b.Definition.Kind
                    +";xy="+F(b.X)+","+F(b.Y)+";halfExtents="+F(b.HX)+","+F(b.HY)+";floorZ="+F(b.FloorZ)+";cosSin="+F(b.Cos)+","+F(b.Sin)
                    +";presence="+b.Presence+";playerInside="+inside+";source=CASC_ActorCollisionData_MarkerSet;collisionValidated=False",false,true);
            }
        }
        private void LogActorProbe(IActor actor)
        {
            var pos=actor.CollisionCoordinate;if(pos==null||!pos.IsValid)pos=actor.FloorCoordinate;if(pos==null||!pos.IsValid)return;
            Hud.TextLog.Log("s7o_MapViewer","HOVER|tick="+Hud.Game.CurrentGameTick+";world="+_worldId+";acd="+actor.AcdId
                +";sno="+(actor.SnoActor==null?0:(uint)actor.SnoActor.Sno)+";code="+(actor.SnoActor==null?"":actor.SnoActor.Code)
                +";gizmo="+actor.GizmoType+";xyz="+F(pos.X)+","+F(pos.Y)+","+F(pos.Z)+";radiusScaled="+F(actor.RadiusScaled)
                +";radiusBottom="+F(actor.RadiusBottom)+";floorXYZ="+(actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid?F(actor.FloorCoordinate.X)+","+F(actor.FloorCoordinate.Y)+","+F(actor.FloorCoordinate.Z):"UNKNOWN")+";nativeFloorZ="+(actor.FloorCoordinate!=null&&actor.FloorCoordinate.IsValid?F(actor.FloorCoordinate.Z):"UNKNOWN")+";disabled="+actor.IsDisabled+";operated="+actor.IsOperated+";clickable="+actor.IsClickable,false,true);
        }
        private void DrawGridLink(bool ground,Placement p,NativeGrid grid,int gx,int gy,int tx,int ty,float x,float y,float z)
        {
            Template t=p.Template;if(tx>=t.GridWidth||ty>=t.GridHeight)return;
            int dx=tx==gx?0:1,dy=ty==gy?0:1;
            for(int xx=gx,yy=gy;xx!=tx||yy!=ty;xx+=dx,yy+=dy)if((grid.Flags[yy*t.GridWidth+xx]&1)==0||!NativeWalkAt(p,p.X+(xx+0.5f)*t.StepX,p.Y+(yy+0.5f)*t.StepY,p.Z+grid.Heights[yy*t.GridWidth+xx]))return;
            int index=ty*t.GridWidth+tx;if((grid.Flags[index]&1)==0||!NativeWalkAt(p,p.X+(tx+0.5f)*t.StepX,p.Y+(ty+0.5f)*t.StepY,p.Z+grid.Heights[index]))return;
            float endZ=p.Z+grid.Heights[index]+0.05f;
            if(!Finite(endZ)||Math.Abs(endZ-z)>3)return; // Preview must not join a large vertical drop as a ramp.
            DrawTerrainEdge(ground,x,y,z,p.X+(tx+0.5f)*t.StepX,p.Y+(ty+0.5f)*t.StepY,endZ);
        }
        private void DrawEdge(bool ground,float x,float y,float z,float tx,float ty,float tz)
        {
            DrawMapLine(_walkBrush,ground,x,y,z,tx,ty,tz);
        }
        private void DrawMapLine(IBrush brush,bool ground,float x,float y,float z,float tx,float ty,float tz)
        {
            if(ground) {brush.DrawLineWorld(x,y,z,tx,ty,tz);return;}
            float sx,sy,ex,ey;Hud.Render.GetMinimapCoordinates(x,y,out sx,out sy);Hud.Render.GetMinimapCoordinates(tx,ty,out ex,out ey);
            var rect=Hud.Render.MinimapUiElement.Rectangle;double a=0,b=1;
            if(!Slab(sx,ex-sx,rect.Left,rect.Right,ref a,ref b)||!Slab(sy,ey-sy,rect.Top,rect.Bottom,ref a,ref b))return;
            brush.DrawLine((float)(sx+(ex-sx)*a),(float)(sy+(ey-sy)*a),(float)(sx+(ex-sx)*b),(float)(sy+(ey-sy)*b));
        }

        // Routing is a bounded advisory layer over exact NavCells and native heights.
        // It never reads files, calls Hud or sends input on a worker. Returned shortcuts
        // require a continuous walk corridor; unknown terrain keeps native recovery.
        // Door traversal is planning-only: the actual dash stops before a closed door.
        private const float RouteStep=1.25f,RouteMaxHop=42f;
        private int _routeSerial,_routeWorker;
        private RouteRequest _routeRequest;
        private volatile RouteResult _route;
        private string _routeLogged="";
        private sealed class RouteScene
        {
            public float X,Y,Z,MaxX,MaxY,StepX,StepY;public int Width,Height;
            public Cell[] Cells;public NativeGrid Grid;
            public Dictionary<long,List<Cell>> Buckets;
        }
        private sealed class RouteRequest
        {
            public int Serial;public long Epoch,Fingerprint;
            public float X,Y,Z,TX,TY,TZ,Radius;
            public RouteScene[] Scenes;public NativeBox[] Boxes;public Obstacle[] Props;
        }
        private sealed class RouteResult
        {
            public RouteRequest Request;public float[][] Points;public string State;
            public int Expanded;public long ElapsedMs;
        }
        private static long RouteBucket(float x,float y)
        { return ((long)(int)Math.Floor(x/10f)<<32)|(uint)(int)Math.Floor(y/10f); }
        private long RouteFingerprint()
        {
            long hash=17;
            foreach(var p in _placements.Values) {
                hash^=(long)p.SceneId*397+(p.Template!=null&&p.Template.Grid!=null?1:0);
            }
            foreach(var b in _nativeBoxes)
                hash^=(long)b.AcdId*31+b.Definition.Sno+b.X.GetHashCode()*17L+b.Y.GetHashCode()*13L;
            foreach(var o in _obstacles)if(!HasNativeBox(o)&&RoutePropBlocks(o))
                hash^=(long)o.AcdId*19+o.FloorX.GetHashCode()*11L+o.FloorY.GetHashCode()*7L;
            return hash;
        }
        private static bool RoutePropBlocks(Obstacle o)
        {
            // Portals and decorative selection bounds are not automatically solid.
            return o.FloorZKnown&&o.Radius>0&&o.Kind!="Portal";
        }
        private RouteRequest CaptureRoute(float x,float y,float z,float tx,float ty,float tz,float radius,long fingerprint,bool tracked=true)
        {
            var scenes=new List<RouteScene>();
            float left=Math.Min(x,tx)-20,top=Math.Min(y,ty)-20,right=Math.Max(x,tx)+20,bottom=Math.Max(y,ty)+20;
            foreach(var p in _placements.Values) {
                if(p.Template==null||p.OriginConflict||p.MaxX<left||p.X>right||p.MaxY<top||p.Y>bottom)continue;
                var t=p.Template;
                NativeGrid grid=t.Grid;
                if(grid==null) {RequestHeight(t);continue;}
                if(Math.Abs(p.MaxX-p.X-t.GridWidth*t.StepX)>0.1||Math.Abs(p.MaxY-p.Y-t.GridHeight*t.StepY)>0.1)continue;
                scenes.Add(new RouteScene {X=p.X,Y=p.Y,Z=p.Z,MaxX=p.MaxX,MaxY=p.MaxY,
                    StepX=t.StepX,StepY=t.StepY,Width=t.GridWidth,Height=t.GridHeight,Cells=t.Cells,Grid=grid});
            }
            scenes.Sort((a,b)=>DistanceSquared((a.X+a.MaxX)*0.5f,(a.Y+a.MaxY)*0.5f,(x+tx)*0.5f,(y+ty)*0.5f)
                .CompareTo(DistanceSquared((b.X+b.MaxX)*0.5f,(b.Y+b.MaxY)*0.5f,(x+tx)*0.5f,(y+ty)*0.5f)));
            if(scenes.Count>32)scenes.RemoveRange(32,scenes.Count-32);
            var props=new List<Obstacle>();
            foreach(var o in _obstacles)if(!HasNativeBox(o)&&RoutePropBlocks(o))props.Add(o);
            return new RouteRequest {Serial=tracked?Interlocked.Increment(ref _routeSerial):0,Epoch=_epoch,Fingerprint=fingerprint,
                X=x,Y=y,Z=z,TX=tx,TY=ty,TZ=tz,Radius=radius,Scenes=scenes.ToArray(),Boxes=_nativeBoxes.ToArray(),Props=props.ToArray()};
        }
        private object Route(float x,float y,float z,float tx,float ty,float tz,float radius)
        {
            if(radius<0||radius>3||DistanceSquared(x,y,tx,ty)>120*120)return Error("RouteBounds");
            long stamp=RouteFingerprint();var request=_routeRequest;
            bool same=request!=null&&request.Epoch==_epoch&&request.Fingerprint==stamp
                &&DistanceSquared(tx,ty,request.TX,request.TY)<4&&Math.Abs(tz-request.TZ)<1
                &&Math.Abs(radius-request.Radius)<0.01;
            if(same&&_route!=null&&_route.State!="Ready"&&DistanceSquared(x,y,request.X,request.Y)>16)same=false;
            if(!same) {_route=null;request=CaptureRoute(x,y,z,tx,ty,tz,radius,stamp);_routeRequest=request;}
            var result=Envelope();result["ok"]=false;result["state"]="Pending";
            if(request.Scenes.Length==0){result["state"]="Unavailable";return result;}
            if(_route==null||_route.Request!=request) {
                if(Interlocked.CompareExchange(ref _routeWorker,1,0)==0) {
                    var work=request;
                    ThreadPool.QueueUserWorkItem(delegate {
                        try {
                            var next=BuildRoute(work);
                            if(work.Serial==Volatile.Read(ref _routeSerial))_route=next;
                        }catch {if(work.Serial==Volatile.Read(ref _routeSerial))_route=new RouteResult {Request=work,State="NoRoute"};}
                        finally {Interlocked.Exchange(ref _routeWorker,0);}
                    });
                }
                LogRoute(result,request,null);return result;
            }
            var route=_route;
            result["state"]=route.State;result["expanded"]=route.Expanded;result["planningMs"]=route.ElapsedMs;
            if(route.Points==null||route.Points.Length==0){LogRoute(result,request,route);return result;}
            int nearest;float[] waypoint=RouteShortcut(request,route,x,y,z,out nearest);
            uint door=0;
            for(int i=nearest;i+1<route.Points.Length&&i<nearest+48;i++) {
                var a=route.Points[i];var b=route.Points[i+1];
                foreach(var box in request.Boxes)if(RouteIsDoor(box.Definition.Kind)) {
                    Interval span;
                    if(BoxInterval(box,a[0],a[1],a[2],b[0],b[1],b[2],radius,out span)){door=box.AcdId;break;}
                }
                if(door!=0)break;
                foreach(var o in request.Props)if(RouteIsDoor(o.Kind)&&RouteCircleHit(o,a[0],a[1],a[2],b[0],b[1],b[2],radius)){door=o.AcdId;break;}
                if(door!=0)break;
            }
            result["doorAcd"]=door;
            if(waypoint!=null&&DistanceSquared(x,y,waypoint[0],waypoint[1])>1) {
                result["ok"]=true;result["waypoint"]=(float[])waypoint.Clone();
                result["state"]="Ready";result["clearanceRadius"]=radius;
            }else result["state"]=door!=0?"DoorApproach":"NativeRecovery";
            LogRoute(result,request,route);return result;
        }
        private static float[] RouteShortcut(RouteRequest request,RouteResult route,float x,float y,float z,out int nearest)
        {
            nearest=0;double distance=Double.MaxValue;
            for(int i=0;i<route.Points.Length;i++) {
                var p=route.Points[i];double d=DistanceSquared(x,y,p[0],p[1])+Math.Abs(z-p[2])*4;
                if(d<distance){distance=d;nearest=i;}
            }
            float[] waypoint=null;
            // Advance along the route, then smooth only where the whole corridor is covered.
            // A collision/new live actor invalidates the snapshot before reaching this code.
            for(int i=nearest;i<route.Points.Length;i++) {
                var p=route.Points[i];
                double hop=Math.Sqrt(DistanceSquared(x,y,p[0],p[1]));
                if(hop>RouteMaxHop) {
                    float a=(float)(RouteMaxHop/hop),hx=x+(p[0]-x)*a,hy=y+(p[1]-y)*a,hz;
                    if(RouteFloor(request,hx,hy,z+(p[2]-z)*a,out hz)&&RouteClear(request,x,y,z,hx,hy,hz,false))
                        waypoint=new[]{hx,hy,hz};
                    break;
                }
                if(RouteClear(request,x,y,z,p[0],p[1],p[2],false))waypoint=p;
            }
            return waypoint;
        }
        private void LogRoute(Dictionary<string,object> result,RouteRequest request,RouteResult route)
        {
            if(!Diagnostics||Hud.TextLog==null)return;
            string key=request.Serial+":"+result["state"]+":"+(result.ContainsKey("doorAcd")?result["doorAcd"]:0);
            if(key==_routeLogged)return;_routeLogged=key;
            Hud.TextLog.Log("s7o_MapViewer","ROUTE|tick="+Hud.Game.CurrentGameTick+";epoch="+_epoch
                +";state="+result["state"]+";goal="+F(request.TX)+","+F(request.TY)+","+F(request.TZ)
                +";door="+(result.ContainsKey("doorAcd")?result["doorAcd"]:0)
                +";from="+F(_playerX)+","+F(_playerY)+","+F(_playerZ)+";radius="+F(request.Radius)
                +";waypoint="+(result.ContainsKey("waypoint")?F(((float[])result["waypoint"])[0])+","+F(((float[])result["waypoint"])[1])+","+F(((float[])result["waypoint"])[2]):"none")
                +";nodes="+(route==null?0:route.Expanded)+";planningMs="+(route==null?0:route.ElapsedMs),false,true);
        }
        private static bool RouteIsDoor(string kind)
        {return kind=="Door"||kind=="Gate"||kind=="BreakableDoor";}
        private static bool RouteCircleHit(Obstacle o,float x,float y,float z,float tx,float ty,float tz,float radius)
        {
            double dx=tx-x,dy=ty-y,n=dx*dx+dy*dy;
            double a=n>0?Math.Max(0,Math.Min(1,((o.FloorX-x)*dx+(o.FloorY-y)*dy)/n)):0;
            if(Math.Abs(o.FloorZ-(z+(tz-z)*a))>2)return false;
            return DistanceSquared(o.FloorX,o.FloorY,(float)(x+dx*a),(float)(y+dy*a))<(o.Radius+radius)*(o.Radius+radius);
        }
        private static void RouteIndex(RouteRequest request)
        {
            int inspected=0;
            foreach(var scene in request.Scenes) {
                scene.Buckets=new Dictionary<long,List<Cell>>();
                foreach(var c in scene.Cells) {
                    if(inspected++>100000)throw new InvalidDataException("RouteCellBudget");
                    if((c.Flags&1)==0)continue;
                    int left=(int)Math.Floor((scene.X+c.MinX)/10),right=(int)Math.Floor((scene.X+c.MaxX)/10);
                    int top=(int)Math.Floor((scene.Y+c.MinY)/10),bottom=(int)Math.Floor((scene.Y+c.MaxY)/10);
                    if((right-left+1)*(bottom-top+1)>4096)continue;
                    for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++) {
                        long key=((long)x<<32)|(uint)y;List<Cell> list;
                        if(!scene.Buckets.TryGetValue(key,out list))scene.Buckets.Add(key,list=new List<Cell>());
                        list.Add(c);
                    }
                }
            }
        }
        private static bool RouteFloor(RouteRequest r,float x,float y,float reference,out float height)
        {
            height=0;bool found=false;float gap=Single.MaxValue;
            foreach(var s in r.Scenes) {
                if(x<s.X||x>=s.MaxX||y<s.Y||y>=s.MaxY||s.Buckets==null)continue;
                int ix=(int)Math.Floor((x-s.X)/s.StepX),iy=(int)Math.Floor((y-s.Y)/s.StepY);
                if(ix<0||iy<0||ix>=s.Width||iy>=s.Height)continue;
                int index=iy*s.Width+ix;
                if((s.Grid.Flags[index]&1)==0)continue;
                float z=s.Grid.Heights[index]+s.Z;if(!Finite(z))continue;
                List<Cell> cells;if(!s.Buckets.TryGetValue(RouteBucket(x,y),out cells))continue;
                bool walk=false;
                foreach(var c in cells)if(x>=s.X+c.MinX&&x<=s.X+c.MaxX&&y>=s.Y+c.MinY&&y<=s.Y+c.MaxY
                    &&z>=s.Z+c.MinZ-FloorZTolerance&&z<=s.Z+c.MaxZ+FloorZTolerance){walk=true;break;}
                float d=Math.Abs(z-reference);
                if(walk&&d<gap){found=true;height=z;gap=d;}
            }
            return found&&gap<=4f;
        }
        private static bool RouteAt(RouteRequest r,float x,float y,float reference,bool doors,out float z,bool originSlack=false)
        {
            if(!RouteFloor(r,x,y,reference,out z))return false;
            float radius=r.Radius;
            float terrainRadius=originSlack?Math.Min(radius,(float)Math.Sqrt(DistanceSquared(x,y,r.X,r.Y))*0.75f):radius;
            float unused;
            for(int i=0;i<ClearanceX.Length&&terrainRadius>0;i++)
                if(!RouteFloor(r,x+terrainRadius*ClearanceX[i],y+terrainRadius*ClearanceY[i],z,out unused)||Math.Abs(unused-z)>2.5f)return false;
            foreach(var box in r.Boxes) {
                if(doors&&RouteIsDoor(box.Definition.Kind))continue;
                Interval span;if(BoxInterval(box,x,y,z,x,y,z,radius,out span))return false;
            }
            foreach(var o in r.Props)if(!(doors&&RouteIsDoor(o.Kind))&&RouteCircleHit(o,x,y,z,x,y,z,radius))return false;
            return true;
        }
        private static bool RouteClear(RouteRequest r,float x,float y,float z,float tx,float ty,float tz,bool doors)
        {
            int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(DistanceSquared(x,y,tx,ty))/0.625));
            if(steps>192)return false;
            // Only an outward segment near the measured starting position may shed
            // the initial terrain buffer. Returning into a wall edge never gets slack.
            bool originSlack=DistanceSquared(x,y,r.X,r.Y)<Math.Pow(r.Radius/0.75f,2)
                &&DistanceSquared(tx,ty,r.X,r.Y)>=DistanceSquared(x,y,r.X,r.Y);
            float previous=z;
            for(int i=0;i<=steps;i++) {
                float a=(float)i/steps,ground;
                if(!RouteAt(r,x+(tx-x)*a,y+(ty-y)*a,z+(tz-z)*a,doors,out ground,originSlack)||Math.Abs(ground-previous)>2.5f)return false;
                previous=ground;
            }
            return Math.Abs(previous-tz)<1f;
        }
        private static readonly float[] ClearanceX={1f,0.707107f,0f,-0.707107f,-1f,-0.707107f,0f,0.707107f};
        private static readonly float[] ClearanceY={0f,0.707107f,1f,0.707107f,0f,-0.707107f,-1f,-0.707107f};
        private RouteRequest _corridorSnapshot;
        // Cached immutable native data supports bounded straight corridors. A spear is not a path.
        // A known gap/edge blocks the suggestion; absent heights/scenes remain Unknown.
        private RouteRequest CorridorSnapshot()
        {
            long stamp=RouteFingerprint();var r=_corridorSnapshot;
            if(r==null||r.Epoch!=_epoch||r.Fingerprint!=stamp||DistanceSquared(_playerX,_playerY,r.X+150,r.Y+150)>100) {
                r=CaptureRoute(_playerX-150,_playerY-150,_playerZ,_playerX+150,_playerY+150,_playerZ,0,stamp,false);
                RouteIndex(r);_corridorSnapshot=r;
            }
            return r;
        }
        // Dash may skip static walls: validate the endpoint, not a walk ray.
        // Each suggestion has known native floor/full clearance within 50 yards;
        // closed doors still veto crossing. Consumers must verify actual arrival.
        private object DashHop(float x,float y,float z,float tx,float ty,float tz,float radius)
        {
            if(radius<0||radius>3||DistanceSquared(x,y,tx,ty)>120*120)return Error("DashBounds");
            var result=Envelope();result["ok"]=false;result["state"]="Unknown";
            var r=CorridorSnapshot();if(r.Scenes.Length==0)return result;
            double distance=Math.Sqrt(DistanceSquared(x,y,tx,ty));
            if(distance<0.01)return result;
            // Eight bounded endpoint samples; crossed static terrain need not be walkable.
            float far=(float)Math.Min(49.5,distance);
            for(int sample=0;sample<8;sample++) {
                float travel=far-sample*5f;if(travel<=1f)break;
                float a=(float)(travel/distance),px=x+(tx-x)*a,py=y+(ty-y)*a,ground;
                if(!RouteFloor(r,px,py,z+(tz-z)*a,out ground))continue;
                bool clear=true;
                foreach(var box in r.Boxes) {
                    Interval span;
                    if(BoxInterval(box,px,py,ground,px,py,ground,radius,out span)
                        ||(RouteIsDoor(box.Definition.Kind)&&BoxInterval(box,x,y,z,px,py,ground,radius,out span)))
                    {clear=false;break;}
                }
                if(!clear)continue;
                foreach(var o in r.Props)
                    if(RouteCircleHit(o,px,py,ground,px,py,ground,radius)
                        ||(RouteIsDoor(o.Kind)&&RouteCircleHit(o,x,y,z,px,py,ground,radius)))
                    {clear=false;break;}
                if(!clear)continue;
                for(int d=0;d<ClearanceX.Length&&radius>0;d++) {
                    float edge;
                    if(!RouteFloor(r,px+radius*ClearanceX[d],py+radius*ClearanceY[d],ground,out edge)
                        ||Math.Abs(edge-ground)>2.5f){clear=false;break;}
                }
                if(!clear)continue;
                result["ok"]=true;result["state"]="Ready";result["waypoint"]=new[]{px,py,ground};
                result["maxHopYards"]=50f;result["clearanceRadius"]=radius;return result;
            }
            result["state"]="BlockedCandidate";return result;
        }
        private object Corridor(float x,float y,float z,float tx,float ty,float tz,float radius)
        {
            if(radius<0||radius>3||DistanceSquared(x,y,tx,ty)>150*150)return Error("CorridorBounds");
            var r=CorridorSnapshot();
            var result=Envelope();result["ok"]=false;result["state"]="Unknown";result["reason"]="MissingHeightOrScene";
            result["clearanceRadius"]=radius;result["clearanceValidated"]=false;
            // Known props veto even while a neighboring height grid is unavailable.
            foreach(var b in r.Boxes){Interval span;if(BoxInterval(b,x,y,z,tx,ty,tz,radius,out span)) {
                result["state"]="BlockedCandidate";result["reason"]="NativeCollisionBox";return result;}}
            foreach(var o in r.Props)if(RouteCircleHit(o,x,y,z,tx,ty,tz,radius)) {
                result["state"]="BlockedCandidate";result["reason"]="ObstacleFootprint";return result;}
            if(r.Scenes.Length==0)return result;
            float length=(float)Math.Sqrt(DistanceSquared(x,y,tx,ty)),previous=z;
            int steps=Math.Max(1,(int)Math.Ceiling(length/0.625f));
            for(int i=0;i<=steps;i++) {
                float a=(float)i/steps,px=x+(tx-x)*a,py=y+(ty-y)*a,reference=z+(tz-z)*a,ground;
                if(!RouteFloor(r,px,py,reference,out ground)) {
                    if(KnownRouteFloor(r,px,py,reference)){result["state"]="BlockedCandidate";result["reason"]="NativeWalkGap";}
                    return result;
                }
                if(Math.Abs(ground-previous)>2.5f){result["state"]="BlockedCandidate";result["reason"]="NativeElevationStep";return result;}
                previous=ground;
                float edgeRadius=length<=0.001f?radius:Math.Min(radius,length*a*0.75f);
                for(int d=0;d<ClearanceX.Length&&edgeRadius>0;d++) {
                    float qx=px+edgeRadius*ClearanceX[d],qy=py+edgeRadius*ClearanceY[d],qz;
                    if(!RouteFloor(r,qx,qy,ground,out qz)) {
                        if(KnownRouteFloor(r,qx,qy,ground)){result["state"]="BlockedCandidate";result["reason"]="NativeWallClearance";}
                        return result;
                    }
                    if(Math.Abs(qz-ground)>2.5f){result["state"]="BlockedCandidate";result["reason"]="NativeWallClearance";return result;}
                }
            }
            result["ok"]=true;result["state"]="ClearCandidate";result["reason"]="NativeCoveredCorridor";return result;
        }
        private static bool KnownRouteFloor(RouteRequest r,float x,float y,float reference)
        {
            foreach(var s in r.Scenes) {
                if(x<s.X||x>=s.MaxX||y<s.Y||y>=s.MaxY)continue;
                int ix=(int)Math.Floor((x-s.X)/s.StepX),iy=(int)Math.Floor((y-s.Y)/s.StepY);
                if(ix<0||iy<0||ix>=s.Width||iy>=s.Height)continue;
                float z=s.Grid.Heights[iy*s.Width+ix]+s.Z;
                if(Finite(z)&&Math.Abs(z-reference)<=4f)return true;
            }
            return false;
        }
        private sealed class RouteHeap
        {
            public readonly List<int> Nodes=new List<int>();public readonly List<float> Scores=new List<float>();
            public void Push(int node,float score) {
                int at=Nodes.Count;Nodes.Add(node);Scores.Add(score);
                while(at>0){int parent=(at-1)/2;if(Scores[parent]<=score)break;Nodes[at]=Nodes[parent];Scores[at]=Scores[parent];at=parent;}
                Nodes[at]=node;Scores[at]=score;
            }
            public int Pop() {
                int result=Nodes[0],last=Nodes.Count-1,node=Nodes[last];float score=Scores[last];
                Nodes.RemoveAt(last);Scores.RemoveAt(last);if(last==0)return result;
                int at=0;
                while(at*2+1<last) {
                    int child=at*2+1;if(child+1<last&&Scores[child+1]<Scores[child])child++;
                    if(Scores[child]>=score)break;Nodes[at]=Nodes[child];Scores[at]=Scores[child];at=child;
                }
                Nodes[at]=node;Scores[at]=score;return result;
            }
        }
        private static RouteResult BuildRoute(RouteRequest request)
        {
            var watch=Stopwatch.StartNew();RouteIndex(request);
            if(RouteClear(request,request.X,request.Y,request.Z,request.TX,request.TY,request.TZ,false))
                return new RouteResult {Request=request,State="Ready",ElapsedMs=watch.ElapsedMilliseconds,
                    Points=new[]{new[]{request.X,request.Y,request.Z},new[]{request.TX,request.TY,request.TZ}}};
            var result=SearchRoute(request,false,watch);
            if(result.Points==null&&watch.ElapsedMilliseconds<120)result=SearchRoute(request,true,watch);
            result.ElapsedMs=watch.ElapsedMilliseconds;return result;
        }
        private static RouteResult SearchRoute(RouteRequest r,bool doors,Stopwatch watch)
        {
            float left=(float)Math.Floor((Math.Min(r.X,r.TX)-20)/RouteStep)*RouteStep;
            float top=(float)Math.Floor((Math.Min(r.Y,r.TY)-20)/RouteStep)*RouteStep;
            int width=(int)Math.Ceiling((Math.Max(r.X,r.TX)+20-left)/RouteStep)+1;
            int height=(int)Math.Ceiling((Math.Max(r.Y,r.TY)+20-top)/RouteStep)+1,n=width*height;
            var result=new RouteResult {Request=r,State="NoRoute"};
            if(n>20000)return result;
            var cost=new float[n];var floors=new float[n];var parent=new int[n];var state=new byte[n];var closed=new bool[n];
            for(int i=0;i<n;i++){cost[i]=Single.MaxValue;parent[i]=-1;}
            int start=(int)Math.Round((r.X-left)/RouteStep)+(int)Math.Round((r.Y-top)/RouteStep)*width;
            int end=(int)Math.Round((r.TX-left)/RouteStep)+(int)Math.Round((r.TY-top)/RouteStep)*width;
            float startX=left+(start%width)*RouteStep,startY=top+(start/width)*RouteStep,startZ;
            if(!RouteAt(r,startX,startY,r.Z,doors,out startZ,true)||!RouteClear(r,r.X,r.Y,r.Z,startX,startY,startZ,doors))return result;
            var heap=new RouteHeap();cost[start]=0;floors[start]=startZ;state[start]=1;heap.Push(start,0);
            int[] dx={1,0,-1,0,1,-1,-1,1},dy={0,1,0,-1,1,1,-1,-1};
            while(heap.Nodes.Count>0&&result.Expanded<12000&&watch.ElapsedMilliseconds<150) {
                int current=heap.Pop();if(closed[current])continue;closed[current]=true;result.Expanded++;
                int cx=current%width,cy=current/width;float x=left+cx*RouteStep,y=top+cy*RouteStep;
                if(current==end&&RouteClear(r,x,y,floors[current],r.TX,r.TY,r.TZ,doors)) {
                    var reverse=new List<float[]>();reverse.Add(new[]{r.TX,r.TY,r.TZ});
                    for(int p=current;p!=-1;p=parent[p])reverse.Add(new[]{left+(p%width)*RouteStep,top+(p/width)*RouteStep,floors[p]});
                    reverse.Add(new[]{r.X,r.Y,r.Z});reverse.Reverse();
                    result.Points=reverse.ToArray();result.State="Ready";return result;
                }
                for(int d=0;d<8;d++) {
                    int nx=cx+dx[d],ny=cy+dy[d];if(nx<0||ny<0||nx>=width||ny>=height)continue;
                    int next=ny*width+nx;if(closed[next])continue;
                    float px=left+nx*RouteStep,py=top+ny*RouteStep,z;
                    if(state[next]==0){state[next]=RouteAt(r,px,py,floors[current],doors,out z,true)?(byte)1:(byte)2;floors[next]=z;}
                    if(state[next]!=1||Math.Abs(floors[next]-floors[current])>2.5f
                        ||!RouteClear(r,x,y,floors[current],px,py,floors[next],doors))continue;
                    float score=cost[current]+(d<4?RouteStep:RouteStep*1.414214f)+Math.Abs(floors[next]-floors[current])*0.1f;
                    if(score>=cost[next])continue;cost[next]=score;parent[next]=current;
                    float heuristic=(float)Math.Sqrt(DistanceSquared(px,py,r.TX,r.TY));heap.Push(next,score+heuristic);
                }
            }
            return result;
        }


    }
}
