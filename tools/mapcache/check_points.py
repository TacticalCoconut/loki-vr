"""Every place rAthena puts a player or fires a trigger on the maps the import map cache replaces, with rAthena's cache and with
the new one: warp touch areas and destinations, OnTouch script areas, literal "map",x,y in scripts (warp, savepoint,
areawarp, monster...), fixed monster spawn areas, start points, @go, pc.cpp's fallback. Walkable = map_gat2cell (0/2/3/4/6).
A touch area is the walkable cells of (x-xs..x+xs, y-ys..y+ys) (npc_setcells, npc.cpp:4972-5001). Also: each of them is
reachable from the map's largest walkable region (steps as path.cpp:385-393) with rAthena's cache and with the new one.
usage: check_points.py <rAthena dir> <new import map_cache.dat> [rAthena source dir: @go from src/map/atcommand.cpp]
As a module: check(ra, new, src) is the whole check (Loki Host's PointsCheck.Run is this function in C#; the module's
datagen calls it: server/tools/datagen.py --install); a new cache is used only when it returns bad == 0 and lost == 0."""
import os, re, sys
from collections import deque
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'client', 'tools', 'move'))
from mapcache_vs_grf import read_cache, cache_cells, WALKABLE


def check(ra, new, src=None, resolve=None, out=print):
    """ra: the rAthena folder (db/, npc/, conf/); new: {map: (xs, ys, cells)}, the new import cache's maps; src: an
    rAthena source folder for the @go points, or None (they are then not checked); resolve: where a server file
    ("npc/re/scripts_main.conf", "conf/import/char_conf.txt") is read from (default: under ra; the module's datagen gives
    its overlay-first lookup, so the module's own NPC files are checked too); out: gets every line.
    Returns (bad, lost, checked, reachability): bad = NEWLY BLOCKED points + AREA LOST, lost = CUT OFF."""
    if resolve is None:
        resolve = lambda path: os.path.join(ra, path)
    old = {}
    for p in (os.path.join(ra, 'db', 're', 'map_cache.dat'), os.path.join(ra, 'db', 'map_cache.dat')):
        for n, e in read_cache(p).items():
            old.setdefault(n, cache_cells(e))
    maps = set(new)

    def walk(cells, m, x, y):
        xs, ys, c = cells[m]
        return 0 <= x < xs and 0 <= y < ys and c[x + y * xs] in WALKABLE

    def area(cells, m, x, y, rx, ry):
        return sum(walk(cells, m, i, j) for j in range(y - ry, y + ry + 1) for i in range(x - rx, x + rx + 1))

    # the NPC files the map-server loads (map.cpp:4264 npc/re/scripts_main.conf; npc: / import: lines)
    files, seen = [], set()
    def conf(path):
        full = resolve(path)
        if full in seen or not os.path.exists(full): return
        seen.add(full)
        for line in open(full, encoding='latin-1'):
            line = line.split('//')[0].strip()
            m = re.match(r'(npc|import):\s*(\S+)', line)
            if not m: continue
            if m.group(1) == 'import': conf(m.group(2))
            else: files.append(m.group(2))
    conf('npc/re/scripts_main.conf')

    points, areas = [], []  # (what, map, x, y) / (what, map, x, y, rx, ry)
    lit = re.compile(r'"([a-z0-9_@]+)"\s*,\s*(\d+)\s*,\s*(\d+)', re.I)
    for f in files:
        full = resolve(f)
        if not os.path.exists(full): continue
        for no, line in enumerate(open(full, encoding='latin-1'), 1):
            if line.lstrip().startswith('//'): continue
            where = f'{f}:{no}'
            cols = line.rstrip('\r\n').split('\t')
            if len(cols) >= 4 and re.match(r'warp\d?$', cols[1].strip()):
                a = re.match(r'([^,]+),(\d+),(\d+)', cols[0]); b = re.match(r'(-?\d+),(-?\d+),([^,]+),(\d+),(\d+)', cols[3])
                if a and b:
                    m, x, y = a.group(1), int(a.group(2)), int(a.group(3)); rx, ry = int(b.group(1)), int(b.group(2))
                    areas.append((f'warp {cols[2]} touch area ({where})', m, x, y, rx, ry))
                    points.append((f'warp {cols[2]} lands ({where})', b.group(3), int(b.group(4)), int(b.group(5))))
            elif len(cols) >= 4 and (cols[1].strip() == 'script' or cols[1].startswith('duplicate(')):
                a = re.match(r'([^,]+),(\d+),(\d+)', cols[0]); b = re.match(r'[^,{]+,(\d+),(\d+)', cols[3])
                if a and b and a.group(1) != '-' and (int(b.group(1)) or int(b.group(2))):
                    areas.append((f'OnTouch {cols[2]} ({where})', a.group(1), int(a.group(2)), int(a.group(3)), int(b.group(1)), int(b.group(2))))
            elif len(cols) >= 3 and re.match(r'(boss_)?monster$', cols[1].strip()):
                a = re.match(r'([^,]+),(\d+),(\d+)(?:,(\d+),(\d+))?', cols[0])
                if a and (int(a.group(2)) or int(a.group(3))):
                    areas.append((f'spawn {cols[2]} ({where})', a.group(1), int(a.group(2)), int(a.group(3)), int(a.group(4) or 0), int(a.group(5) or 0)))
            for m, x, y in lit.findall(line):
                if int(x) or int(y): points.append((f'script "{m}",{x},{y} ({where})', m, int(x), int(y)))

    # start points (char_athena.conf / import), @go (atcommand.cpp), pc.cpp's fallback
    for p in (resolve(os.path.join('conf', 'char_athena.conf')), resolve(os.path.join('conf', 'import', 'char_conf.txt'))):
        if os.path.exists(p):
            for line in open(p, encoding='latin-1'):
                if line.startswith('start_point'):
                    for m, x, y in re.findall(r'([a-z0-9_@]+),(\d+),(\d+)', line.split(':', 1)[1]): points.append((f'start_point ({os.path.basename(p)})', m, int(x), int(y)))
    acmd = os.path.join(src, 'src', 'map', 'atcommand.cpp') if src else ''
    if os.path.exists(acmd):
        # MAP_* are defined in src/common/mapindex.hpp (MAP_MORROC is "morocc": the constant's name is not the map's)
        names = dict(re.findall(r'#define\s+MAP_([A-Z0-9_]+)\s+"([^"]+)"', open(os.path.join(src, 'src', 'common', 'mapindex.hpp'), encoding='latin-1').read()))
        for m, x, y in re.findall(r'\{\s*MAP_([A-Z0-9_]+)\s*,\s*(\d+)\s*,\s*(\d+)', open(acmd, encoding='latin-1').read()):
            points.append(('@go (atcommand.cpp)', names[m], int(x), int(y)))
    else:
        out('(no rAthena source dir given: the @go points are not checked)')
    points.append(('pc.cpp:2231 fallback', 'prontera', 273, 354))

    out(f'{len(files)} NPC files; {len(points)} points, {len(areas)} areas in all maps')
    kinds = {}
    for what, m, *_ in points + areas:
        if m in maps: k = what.split(' ')[0]; kinds[k] = kinds.get(k, 0) + 1
    out('on the replaced maps: ' + ', '.join(f'{v} {k}' for k, v in sorted(kinds.items())))
    bad = 0; checked = 0
    for what, m, x, y in points:
        if m not in maps: continue
        checked += 1
        o, n = walk(old, m, x, y), walk(new, m, x, y)
        if o and not n: bad += 1; out(f'NEWLY BLOCKED point {m} {x},{y}: {what}')
        elif not o and n: out(f'  (newly walkable point {m} {x},{y}: {what})')
    for what, m, x, y, rx, ry in areas:
        if m not in maps: continue
        checked += 1
        o, n = area(old, m, x, y, rx, ry), area(new, m, x, y, rx, ry)
        if n == 0 and o > 0: bad += 1; out(f'AREA LOST {m} {x},{y} +-{rx},{ry}: walkable cells {o} -> {n}: {what}')
        elif n < o: out(f'  (area smaller {m} {x},{y} +-{rx},{ry}: {o} -> {n}: {what})')
    out(f'checked on the {len(maps)} replaced maps: {checked}; unreachable with the new cache (and reachable before): {bad}')

    # ---- reachability: the largest walkable region of each map (steps as path.cpp:385-393: a diagonal needs both sides walkable)
    def regions(cells, m):
        xs, ys, c = cells[m]
        ok = lambda x, y: 0 <= x < xs and 0 <= y < ys and c[x + y * xs] in WALKABLE
        lab = [-1] * (xs * ys); sizes = []
        for s in range(xs * ys):
            if lab[s] != -1 or c[s] not in WALKABLE: continue
            k = len(sizes); lab[s] = k; n = 1; q = deque([s])
            while q:
                i = q.popleft(); x, y = i % xs, i // xs
                for dx, dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
                    nx, ny = x + dx, y + dy
                    if not ok(nx, ny) or lab[nx + ny * xs] != -1: continue
                    if dx and dy and not (ok(x + dx, y) and ok(x, y + dy)): continue
                    lab[nx + ny * xs] = k; n += 1; q.append(nx + ny * xs)
            sizes.append(n)
        main = max(range(len(sizes)), key=lambda k: sizes[k]) if sizes else -1
        return lab, main, xs, ys
    R = {}
    def in_main(cells, tag, m, x, y, rx, ry):
        key = (tag, m)
        if key not in R: R[key] = regions(cells, m)
        lab, main, xs, ys = R[key]
        return any(0 <= i < xs and 0 <= j < ys and lab[i + j * xs] == main for j in range(y - ry, y + ry + 1) for i in range(x - rx, x + rx + 1))
    lost = 0; n = 0
    for what, m, x, y, *r in [(a, b, c, d, 1, 1) for a, b, c, d in points] + areas:  # a point: itself or a neighbour (you can step off it)
        if m not in maps: continue
        rx, ry = r; n += 1
        o, w = in_main(old, 'old', m, x, y, rx, ry), in_main(new, 'new', m, x, y, rx, ry)
        if o and not w: lost += 1; out(f'CUT OFF from the main region {m} {x},{y} +-{rx},{ry}: {what}')
    out(f'reachability: {n} checked; reachable from the main region before and not with the new cache: {lost}')
    for m in sorted(maps):
        a, b = R.get(('old', m)), R.get(('new', m))
        if a and b:
            so = sum(1 for v in a[0] if v == a[1]); sn = sum(1 for v in b[0] if v == b[1])
            if abs(sn - so) > 0.2 * so: out(f'  main region of {m}: {so} -> {sn} cells')
    return bad, lost, checked, n


if __name__ == '__main__':
    check(sys.argv[1], {n: cache_cells(e) for n, e in read_cache(sys.argv[2]).items()}, sys.argv[3] if len(sys.argv) > 3 else None)
