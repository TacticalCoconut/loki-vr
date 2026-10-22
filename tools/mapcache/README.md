# Map cache from the client's maps (optional; the design's decision)

rAthena walks every unit by its map cache (`db/.../map_cache.dat`, built by rAthena's developers from Korean game data).
The fresh LATAM client of 2026-09-30 draws 57 of rAthena's maps with different walkable cells (morocc 1780 cells,
prontera 1275, amicitia1 1267, …; payon and the dungeons checked are identical). On those maps a unit can be drawn
crossing a prop the server walks around, and far walks meet refused legs (client wave 3b, w3walk: 21-622 of
~5,000-7,000 leg requests per differing map; the walker retries, far walks still arrive 183-200 times of 200).
NPCs and warps stay reachable (`check_points.py`, below: none of the 1064 warps, touch areas, spawns and script
coordinates on those maps is lost; nine warp / touch areas get smaller, e.g. Morocc's moc08 at 85,55: its centre cell is
blocked in the new map, 4 of its 7 walkable touch cells remain, and walking into it works).

`build_import_cache.py` writes an IMPORT map cache with the client's version of exactly those maps:

```
python tools/mapcache/build_import_cache.py "D:/Ragnarok Online/Ragnarok/data.grf" <rAthena dir> <out>/map_cache.dat
```

- Cells are made the way rAthena's own `tool/mapcache.cpp` makes them (the GAT cell type; a walkable cell under the RSW
  water level becomes walkable water; the water level is read only for RSW 1.4-2.5, as `common/grfio.cpp` does), so
  only the source data changes. rAthena's `mapcache` tool itself cannot read the LATAM GRF (GRF 0x300, `grfio.cpp:546-660`).
- rAthena loads `db/import/map_cache.dat` first and takes each map from the first cache that holds it
  (`map/map.cpp:3920-3972`): the file overrides only those maps. Entries already in the import cache are kept.
- **To use it** (in Loki Host's copy only, `C:\LokiHost\rathena`): stop the servers, move the copy's
  `db\import\map_cache.dat` (rAthena's empty 8-byte template) aside, put the new file there, start the servers.
  **To undo**: stop, move the new file away, put the template back.
- Checked on 2026-09-30 in a test copy of rAthena e985006: "Successfully loaded '1265' maps" (as without it); a guest
  walking prontera 148,171 → 158,173 stepped on 154,173 (on the client's planned route; an announcing NPC there fired) and
  no longer on 158,172 (the cell the server crossed with rAthena's cache, off the client's route).
- What is lost: maps whose RSW is newer than 2.5 get no water level (rAthena's tool does the same with such files);
  61 maps that differ only in water flags are left as rAthena has them.

## Installed in Loki Host (2026-09-30, the design's decision)

`C:\LokiHost\rathena\db\import\map_cache.dat` = the file above (57 maps, sha256 2663ddcc…a86, the same bytes as the one
tested in the test copy). rAthena's 8-byte template sits next to it as `map_cache.dat.rathena-template`, with
`map_cache.LOKI-README.txt` saying how to undo (stop the servers, move `map_cache.dat` away, rename the template back,
start). Checked before and after installing:

- `check_points.py <rAthena dir> <map_cache.dat> [rAthena source]` reads every NPC file the map-server loads
  (`npc/re/scripts_main.conf`, map.cpp:4264) and checks, with rAthena's cache and the new one: warp touch areas (their
  walkable cells, npc_setcells npc.cpp:4972-5001) and destinations, OnTouch areas, `"map",x,y` in scripts (warp,
  savepoint, …), fixed spawns, start points, `@go`, pc.cpp's fallback; each also reachable from the map's largest region
  (steps as path.cpp:385-393). On the 57 maps: 482 warp, 227 OnTouch, 340 script, 10 @go, 4 spawn, 1 fallback: none
  lost, none cut off. A player placed on a blocked cell can still walk off it (path_search skips the start cell,
  path.cpp:284-285; pc_setpos does not check explicit coordinates, pc.cpp:7086-7101).
- The warpwalk smoke on Loki Host (`-lokiSmokeOnly warpwalk -lokiWarpWalk "map,sx,sy,tx,ty,dest;…"`, a GM account):
  walking from outside onto a remaining touch cell of the six shrunken warps (moc08, moc03, moc004, moc07, moc005,
  alb06) arrived on each destination: 6/6.
- Loki Host's map-server: "Successfully loaded '1265' maps" (as before), no new warning or error; stop and start clean.

## The module (Loki Online) uses the same rule and the same checks (port wave I, 2026-10-05)

The SpacetimeDB module embeds its maps at build time (`server/data/maps.bin`, rAthena's stock cache). For a server whose
players draw the maps of their Ragnarok Online install, `server/tools/datagen.py <rAthena dir> data --install <Ragnarok
Online folder>` calls this folder's code and nothing else for the rule:

- `build_import_cache.py build()` (the rule; Loki Host's `MapCacheBuilder.Build` is its port), then
  `check_points.py check()` (the checks; Loki Host's `PointsCheck.Run`) over the NPC files the MODULE loads (the rAthena
  tree and `server/data/overlay`); a lost or cut-off point refuses the new maps and nothing is written;
- `server/data/install/map_cache.dat` = the import cache (the file above: on patch 1486 the same bytes, sha256
  2663ddcc…a86), `install/maps.bin` = the module's maps with that cache read first (as `map/map.cpp:3919-3925,
  3967-3971`), `install/mapstamp.txt` = the install's map-data stamp and patch label (`data_stamp.py`: the client's
  `IO/MapDataStamp.cs` in Python; `g1-603785af4ecfea8d`, "patch 1486", as `LokiMapTool stamp` prints).

The module's default build keeps the stock maps (the parity suites compare with rAthena copies on the stock cache);
`--features install_maps` embeds `install/` (the build a host publishes). Details: `server/ARCHITECTURE.md`.
`build_import_cache.py` and `check_points.py` are importable since then (`build`, `cache_bytes`, `verify`, `report`,
`check`); their command lines and every line they print are unchanged (old against new on the install of 2026-09-30:
the cache byte for byte, the check's 19 / 20 lines identical).
