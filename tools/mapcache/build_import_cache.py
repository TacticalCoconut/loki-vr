"""Builds an rAthena IMPORT map cache (db/import/map_cache.dat) holding the maps whose walkability differs between rAthena's
map cache and the client's GRF, with their cells taken from the GRF by rAthena's own rules (tool/mapcache.cpp read_map: the GAT
cell type, 0 under the RSW water level becomes 3; the RSW water level only for RSW 1.4-2.5, grfio.cpp:473-490). rAthena's
map-server reads db/import/map_cache.dat first and takes each map from the first cache that has it (map.cpp:3920-3972), so this
file overrides exactly those maps; moving it away restores rAthena's own. Entries already in the import cache are kept unless
replaced. File layout: map.cpp:156-167 (header uint32 file_size + uint16 map_count, 8 bytes with padding; per map char name[12],
int16 xs, int16 ys, int32 len, then zlib cells: tool/mapcache.cpp encode_zip).

usage: build_import_cache.py <data.grf> <rAthena dir (holds db/)> <out map_cache.dat> [map ...]
   (no map names: every map whose walkability differs)"""
import os, struct, sys, zlib
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'client', 'tools', 'move'))
from grf import Grf
from mapcache_vs_grf import read_cache, cache_cells, grf_cells, WALKABLE


def cache_bytes(maps):
    """The map cache file of these maps ({name: (xs, ys, cells)}, in this order), as rAthena lays it out."""
    body = bytearray()
    for name, (xs, ys, cells) in maps.items():
        comp = zlib.compress(bytes(cells))
        nm = name.encode('latin-1')
        assert len(nm) < 12, name
        body += nm.ljust(12, b'\0') + struct.pack('<hhi', xs, ys, len(comp)) + comp
    head = struct.pack('<IHxx', 8 + len(body), len(maps))
    return head + body


def write_cache(path, maps):
    with open(path, 'wb') as f:
        f.write(cache_bytes(maps))


def cache_order(ra):
    """The three caches in the server's order (map.cpp:3919-3925)."""
    return [os.path.join(ra, 'db', 'import', 'map_cache.dat'), os.path.join(ra, 'db', 're', 'map_cache.dat'), os.path.join(ra, 'db', 'map_cache.dat')]


def build(grf, ra, wanted=()):
    """THE RULE (Loki Host's MapCacheBuilder.Build is this function in C#; the module's datagen calls it: server/tools/
    datagen.py --install): every map the server's caches know is compared with the install's cells; a map is taken from
    the install when its size or the walkability of a cell differs, left as the cache has it when only water flags
    differ or the install has no such map; entries already in the import cache are kept unless replaced.
    grf: an open Grf; ra: the rAthena folder (holds db/); wanted: map names (none: every map whose walkability differs).
    Returns (result {name: (xs, ys, cells)} in file order, imp, replaced [(name, cells whose walkability changes; -1:
    the size differs)], water_only, missing)."""
    wanted = set(wanted)
    order = cache_order(ra)
    caches = [(p, read_cache(p)) for p in order if os.path.exists(p)]
    imp = dict(caches[0][1]) if caches and caches[0][0] == order[0] else {}
    known = sorted({m for _, c in caches for m in c})
    result = {name: cache_cells(entry) for name, entry in imp.items()}  # kept as they are
    replaced = []; water_only = 0; missing = 0
    for name in known:
        if wanted and name not in wanted:
            continue
        src = next((c[name] for _, c in caches if name in c))
        xs, ys, cc = cache_cells(src)
        g = grf_cells(grf, name)
        if g is None:
            missing += 1; continue
        gx, gy, gc = g[0], g[1], g[2]
        if (gx, gy) != (xs, ys):
            walk_diff = -1
        else:
            walk_diff = sum(1 for i in range(xs * ys) if (cc[i] in WALKABLE) != (gc[i] in WALKABLE))
        if walk_diff == 0 and not wanted:
            if cc != gc: water_only += 1
            continue
        result[name] = (gx, gy, gc)
        replaced.append((name, walk_diff))
    return result, imp, replaced, water_only, missing


def verify(out, grf, replaced):
    """read back, every replaced map equals the GRF's cells"""
    back = read_cache(out)
    for name, _ in replaced:
        xs, ys, cells = cache_cells(back[name])
        g = grf_cells(grf, name)
        assert (xs, ys, cells) == (g[0], g[1], g[2]), name


def report(out, result, imp, replaced, water_only, missing):
    """The tool's own lines (Loki Host logs the same ones)."""
    lines = [f'wrote {out}: {len(result)} maps ({len(imp)} kept from the import cache, {len(replaced)} from the GRF); '
             f'maps differing only in water flags (left alone): {water_only}; maps not in the GRF: {missing}']
    for name, d in sorted(replaced, key=lambda x: -x[1])[:12]:
        lines.append(f'  {name}: {d} cells change walkability' if d >= 0 else f'  {name}: size differs')
    return lines


def main():
    grf_path, ra, out = sys.argv[1], sys.argv[2], sys.argv[3]
    grf = Grf(grf_path)
    result, imp, replaced, water_only, missing = build(grf, ra, sys.argv[4:])
    write_cache(out, result)
    verify(out, grf, replaced)
    for line in report(out, result, imp, replaced, water_only, missing):
        print(line)


if __name__ == '__main__':
    main()
