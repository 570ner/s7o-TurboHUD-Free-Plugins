# Map Viewer

Optional map geometry for FreeHUD plugins. Install `s7o_MapViewer.cs` and the
`MapViewerData` folder together in `plugins/s7o`, then restart TurboHUD. Keep only
one MapViewer source file loaded. No scripts, extra runtime or root-folder files
are needed.

In HUD Menu, **Map Viewer** is the first VISUAL entry. The grid and **Debug
Logging** start off; saved preferences are retained. Hiding the grid keeps map
queries available. Debug output is `logs/s7o_MapViewer.txt`.

Remove the plugin and its folder to uninstall. HUD Menu, GenMonk and ZB AutoSnap
work without it. GenMonk uses buffered routes and verifies actual dash movement;
ZB AutoSnap checks straight pull corridors. Neither requires the MapViewer class
at compile time. Native navigation and input safety remain the consumer's job.

## Use from another plugin

Discover and cache the exported delegate using BCL types; no reflection or shared
interface file is needed. Retry discovery at a bounded rate if it is absent.

```csharp
Func<string, object[], object> get = null;
foreach (var plugin in Hud.AllPlugins)
{
    if (plugin == null || !plugin.Enabled) continue;
    var exports = plugin as IEnumerable<KeyValuePair<string,
        Func<string, object[], object>>>;
    if (exports == null) continue;
    foreach (var entry in exports)
        if (entry.Key == "s7o.MapViewer.v1") { get = entry.Value; break; }
    if (get != null) break;
}
if (get == null) return; // Continue your own fallback instead.
var context = get("player", new object[0]) as Dictionary<string, object>;
if (context == null || !(bool)context["ok"]
    || Convert.ToUInt32(context["worldId"]) != Hud.Game.Me.WorldId) return;
var from = Hud.Game.Me.FloorCoordinate;
var to = target.FloorCoordinate; // Your validated current-world target.
var ray = get("corridor", new object[] { context["epoch"],
    from.X, from.Y, from.Z, to.X, to.Y, to.Z, 1.25f })
    as Dictionary<string, object>;
// BlockedCandidate: choose another target. Unknown: use native safeguards.
var route = get("path", new object[] { context["epoch"],
    from.X, from.Y, from.Z, to.X, to.Y, to.Z, 2f })
    as Dictionary<string, object>;
var next = route != null && route.ContainsKey("waypoint")
    ? route["waypoint"] as float[] : null;
// Ready: validate next against your UI, hazards and skill constraints.
// Pending/unavailable: continue native navigation; do not wait motionless.
```

Import `System`, `System.Collections.Generic` and `Turbo.Plugins`. Keep the
provider plugin alongside its delegate; detach if disabled or a call fails.
Call on the HUD thread, catch failures, and fetch a fresh epoch before queries.
An epoch changes when world/placement/cache context changes.

| GET method | Arguments after method name | Purpose |
|---|---|---|
| `player`, `status`, `controls` | empty object array | Context, availability and display settings |
| `scenes`, `shapes`, `obstacles`, `monsters` | epoch | Observed geometry, collision candidates and live samples |
| `cells` | epoch, sceneId | Cells of an observed scene |
| `height`, `point` | epoch, X, Y, Z | Native floor or point candidate |
| `corridor`, `path` | epoch, fromXYZ, toXYZ, radius | Straight clearance or advisory walk route |
| `dash` | epoch, fromXYZ, toXYZ, radius | Buffered landing within 50 yards; can skip static walls, blocks closed doors |
| `resetpath` | epoch | Invalidate a route after confirmed failed travel |
| `configure` | grid boolean, logging boolean | Persist display/logging choices |
| `feedback` | epoch, consumer, event, targetAcd, X, Y, Z | Bounded optional diagnostics |

Corridors allow 150 yards; paths allow 120 yards. Radius is 0–3 yards. Paths
return bounded walkable hops. `dash` separately suggests a known clear endpoint
within 50 yards; it cannot guarantee the game will allow the shortcut. GenMonk
acquires projected visible targets up to 90 yards, verifies actual displacement,
and continues with another bounded hop or native navigation. GenMonk uses
a two-yard margin around terrain and detected solid props (pylons, shrines,
chests, levers and closed doors); a destination never inherits the cramped-source
escape exception. Check closed doors, hazards and actual displacement separately. `point` describes
the underlying footprint; use `corridor` with a radius for buffered clearance.

## Coverage and maintenance

Native Scene NavCells and grid heights provide fine edges and elevation. Nearby
reported scenes are preloaded and drawn by distance to their bounds, without
requiring the player to enter them. Verified placements are retained through temporary
collection interruptions; queries stay unavailable until native world/data identity
is rechecked. `CONTEXT` debug rows explain suspensions and world/data resets.
Unsupported or unreported geometry stays
**Unknown**. FreeHUD exposes no complete loaded-scene list; some actor scene
references lack usable terrain even when a different player scene at the same
bounds has it. A wider radius cannot resolve that identity mismatch. Never guess
neighbor templates or treat Unknown as confirmed walkable space.

This plugin does not scan process memory or use memory pointers. Data was decoded
from the local Diablo III CASC assets, with native SNO identity, bounds, heights
and integrity checks. After a game update, regenerate the offline data using the
same validated asset layouts; if layouts change, revalidate them before publishing.
Runtime placement comes from FreeHUD's public scene references. Logs distinguish
missing templates, unready heights and origin conflicts. Preserve those checks,
bounded caches, verified movement and native recovery when maintaining consumers.

## Nearby scenes

The provider discovers neighboring loaded terrain through FreeHUD's existing
scene collector. A one-time internal binding creates cached delegates; normal
scans run at most twice a second, with no reflection in the scan loop and no new
process-memory reader or pointer offsets. Only current-world native scene rows
are used; remembered reveal records with incomplete height are excluded.
If the internal API changes or the current scene cannot be verified, public
player/actor discovery continues unchanged. Unknown areas remain unknown.
Debug summaries report `loadedSceneSource`, `loadedSceneCount` and
`loadedSceneNearby`; newly discovered tiles have `source=LoadedScene`.
