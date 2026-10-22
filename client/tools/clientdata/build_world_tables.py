#!/usr/bin/env python3
r"""
Client wave 4 (w4world): the two GRF-derived tables of the world systems, built from the player's own install. The client makes them
itself on first run (Assets/Loki/Runtime/Data/WorldTools.cs is this file ported step for step, ClientDataBuilder step "World tables");
this tool is the reference the port is compared with (byte for byte: ../w3data/compare.py).

  python build_world_tables.py [<RO install folder>] <out dir>
      default install: C:\Ragnarok Online (reads its data.grf and System\ folder)
      writes <out dir>\mapsky.tsv and <out dir>\navigation.tsv (UTF-8, "\n")

mapsky.tsv (World/SkyController.cs): data\luafiles514\lua files\mapskydata\mapskydata.lub, MapSkyData["<map>.rsw"] (80 maps in the
2026-09-30 LATAM GRF; korangar world/library/map_sky_data.rs reads the same file). Rows sorted by map:
  S  map  bg ("r,g,b" or -)  star (1/0/-)  fog (1/0/-)  old cloud type (-)          BG_Color, Star_Effect, BG_Fog, Old_Cloud_Effect[1]
  C  map  i  Num CullDist R G B Size Size_Extra Expand_Rate Alpha_Inc_Time Alpha_Inc_Time_Extra Alpha_Inc_Speed
            Alpha_Dec_Time Alpha_Dec_Time_Extra Alpha_Dec_Speed Height Height_Extra   Cloud_Effect[i] (a missing field: -)

navigation.tsv (Game/Navigation.cs): data\luafiles514\lua files\navigation\navi_*_br.lub (the client's navigation data) and the
install's town guide System\Towninfo.lub (Portuguese) + System\english\Towninfo.lub. Rows in the tables' own order (the NULL rows
that end each table are left out):
  M  map  name  kind  w  h  name_en                          Navi_Map {map, name, kind, w, h} (Portuguese); name_en (wave 28): the
                                                             install's System\english\mapinfo.lub mapTbl[map].displayName, or ''
  L  id  map  x  y  type  class  dest  dx  dy  name  label   Navi_Link {map, id, type, class, name, label, x, y, dest, dx, dy}
  E  id  id2:dist,...                                        Navi_Distance: after link id, the links on its destination map and the walk to each
  N  id  map  x  y  type  class  name  label                 Navi_Npc {map, id, type, class, name, label, x, y}
  D  id  link:dist,...                                       Navi_NpcDistance: the links into the NPC's map and the walk from each to it
  B  map  id  type  class  name  aegis  level  info          Navi_Mob {map, id, type, class, name, aegis, level, info}
  T  map  type  x  y  name_en  name_pt                       mapNPCInfoTable[map] {name, X, Y, TYPE} (sorted by map)
A name that starts with byte 0x1C is a text id the install cannot resolve (7904 of the 7947 NPC names): written empty.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

LUA = 'data\\luafiles514\\lua files\\'
SKY = LUA + 'mapskydata\\mapskydata.lub'
NAVI = LUA + 'navigation\\'
CLOUD_FIELDS = [b'Num', b'CullDist', b'Color', b'Size', b'Size_Extra', b'Expand_Rate', b'Alpha_Inc_Time', b'Alpha_Inc_Time_Extra',
                b'Alpha_Inc_Speed', b'Alpha_Dec_Time', b'Alpha_Dec_Time_Extra', b'Alpha_Dec_Speed', b'Height', b'Height_Extra']


def fresh():
    lub51.GLOBALS = lub51.LuaTable()
    lub51._setup()
    return lub51.GLOBALS


def dec(b):
    """'' for None, str() of a number, else UTF-8 -> windows-1252 -> Latin-1 (as build_worldmap.py)."""
    if b is None: return ''
    if isinstance(b, bool): return str(b)
    if isinstance(b, (int, float)): return str(b) if isinstance(b, int) else repr(b)
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ').strip().replace('\u200b', '')  # wave 28 (wtextc): zero-width spaces out


def name(b):
    """A display name, or '' for a text id (0x1C ... 0x1C) and for a non-string."""
    if not isinstance(b, bytes) or b[:1] == b'\x1c': return ''
    return clean(dec(b))


def num(v):
    if isinstance(v, bool) or not isinstance(v, (int, float)): return '-'
    return dec(v)


def flag(v):
    if v is True: return '1'
    if v is False: return '0'
    return '-'


def seq(t):
    return [t.get(i) for i in range(1, t.length() + 1)] if isinstance(t, lub51.LuaTable) else []


def need(g, path):
    data = g.read(path)
    if data is None: raise SystemExit('missing in the GRF: ' + path)
    return data


def mapkey(b):
    m = dec(b).lower()
    for ext in ('.rsw', '.gat'):
        if m.endswith(ext): m = m[:-len(ext)]
    return m


def mapsky(g):
    G = fresh()
    lub51.run(need(g, SKY))
    t = G.get(b'MapSkyData')
    if not isinstance(t, lub51.LuaTable): raise SystemExit('mapskydata.lub: no MapSkyData table')
    rows, nclouds = [], 0
    entries = sorted(((mapkey(k), v) for k, v in t.h.items() if isinstance(k, bytes) and isinstance(v, lub51.LuaTable)), key=lambda kv: kv[0])
    for m, e in entries:
        bg = e.get(b'BG_Color')
        bgs = ','.join(num(x) for x in seq(bg)) if isinstance(bg, lub51.LuaTable) else '-'
        old = e.get(b'Old_Cloud_Effect')
        olds = num(seq(old)[0]) if isinstance(old, lub51.LuaTable) and seq(old) else '-'
        rows.append('\t'.join(['S', m, bgs, flag(e.get(b'Star_Effect')), flag(e.get(b'BG_Fog')), olds]))
        for i, c in enumerate(seq(e.get(b'Cloud_Effect'))):
            if not isinstance(c, lub51.LuaTable): continue
            cols = ['C', m, str(i + 1)]
            for f in CLOUD_FIELDS:
                v = c.get(f)
                if f == b'Color':
                    col = seq(v) if isinstance(v, lub51.LuaTable) else []
                    cols += [num(col[k]) if k < len(col) else '-' for k in range(3)]
                else: cols.append(num(v))
            rows.append('\t'.join(cols)); nclouds += 1
    return rows, len(entries), nclouds


MAPINFO_EN = 'System\\english\\mapinfo.lub'


def english_map_names(root):
    """wave 28 (wtextc): the install's English map names, System\\english\\mapinfo.lub mapTbl['map.rsw'].displayName ({} without the
    file; a file that does not run gives none, and the reason is printed). Data/WorldTools.cs EnglishMapNames does the same."""
    p = os.path.join(root, MAPINFO_EN)
    if not os.path.exists(p): return {}, 'none (no %s)' % MAPINFO_EN
    with open(p, 'rb') as f: data = f.read()
    G = fresh(); out = {}
    try:
        lub51.run(data)
        t = G.get(b'mapTbl')
        if isinstance(t, lub51.LuaTable):
            for k, v in t.h.items():
                if isinstance(k, bytes) and isinstance(v, lub51.LuaTable):
                    n = name(v.get(b'displayName'))
                    if n: out[mapkey(k)] = n
    except Exception as e:
        return {}, '%s unreadable (%s: %s)' % (MAPINFO_EN, type(e).__name__, e)
    return out, '%d English map names' % len(out)


def navigation(g, root):
    en_maps, en_how = english_map_names(root)  # before the navigation state: fresh() empties the shared globals
    G = fresh()
    for f in ('navi_map_br.lub', 'navi_link_br.lub', 'navi_linkdistance_br.lub', 'navi_npc_br.lub', 'navi_npcdistance_br.lub', 'navi_mob_br.lub'):
        lub51.run(need(g, NAVI + f))
    rows = []
    counts = {'en_how': en_how}

    def real(r):
        return len(r) > 0 and isinstance(r[0], bytes) and r[0] not in (b'NULL', b'') and r[0] != b'0'

    for r in seq(G.get(b'Navi_Map')):
        r = seq(r)
        if len(r) < 5 or not real(r): continue
        mk = mapkey(r[0])  # wave 28 (wtextc): a seventh column, the install's English name ('' when it has none)
        rows.append('\t'.join(['M', mk, name(r[1]), num(r[2]), num(r[3]), num(r[4]), en_maps.get(mk, '')])); counts['M'] = counts.get('M', 0) + 1
    for r in seq(G.get(b'Navi_Link')):
        r = seq(r)
        if len(r) < 11 or not real(r): continue
        rows.append('\t'.join(['L', num(r[1]), mapkey(r[0]), num(r[6]), num(r[7]), num(r[2]), num(r[3]), mapkey(r[8]), num(r[9]), num(r[10]), name(r[4]), name(r[5])]))
        counts['L'] = counts.get('L', 0) + 1

    def flat(t, row_key):
        s = seq(t)
        n = 0
        for i in range(0, len(s) - 2, 3):
            for row in seq(s[i + 2]):
                row = seq(row)
                if not row or isinstance(row[0], bool) or not isinstance(row[0], (int, float)) or row[0] == 0: continue
                pairs = []
                for sub in row[1:]:
                    sub = seq(sub)
                    if len(sub) < 3 or sub[0] in (b'NULL', b'') or sub[1] == 0: continue
                    pairs.append(num(sub[1]) + ':' + num(sub[2]))
                rows.append('\t'.join([row_key, num(row[0]), ','.join(pairs)])); n += 1
        counts[row_key] = n

    flat(G.get(b'Navi_Distance'), 'E')
    for r in seq(G.get(b'Navi_Npc')):
        r = seq(r)
        if len(r) < 8 or not real(r): continue
        rows.append('\t'.join(['N', num(r[1]), mapkey(r[0]), num(r[6]), num(r[7]), num(r[2]), num(r[3]), name(r[4]), name(r[5])]))
        counts['N'] = counts.get('N', 0) + 1
    flat(G.get(b'Navi_NpcDistance'), 'D')
    for r in seq(G.get(b'Navi_Mob')):
        r = seq(r)
        if len(r) < 8 or not real(r): continue
        rows.append('\t'.join(['B', mapkey(r[0]), num(r[1]), num(r[2]), num(r[3]), name(r[4]), name(r[5]), num(r[6]), num(r[7])]))
        counts['B'] = counts.get('B', 0) + 1
    # the install's town guide: Portuguese (System\Towninfo.lub) and English (System\english\Towninfo.lub)
    towns = {}
    for lang, rel in (('pt', 'System\\Towninfo.lub'), ('en', 'System\\english\\Towninfo.lub')):
        p = os.path.join(root, rel)
        if not os.path.exists(p): raise SystemExit('missing in the install: ' + rel)
        with open(p, 'rb') as f: data = f.read()
        G = fresh()
        lub51.run(data)
        t = G.get(b'mapNPCInfoTable')
        if not isinstance(t, lub51.LuaTable): raise SystemExit(rel + ': no mapNPCInfoTable')
        for k, v in t.h.items():
            if not isinstance(k, bytes): continue
            lst = []
            for e in seq(v):
                if isinstance(e, lub51.LuaTable): lst.append((num(e.get(b'TYPE')), num(e.get(b'X')), num(e.get(b'Y')), name(e.get(b'name'))))
            towns.setdefault(mapkey(k), {})[lang] = lst
    nt = 0
    for m in sorted(towns):
        pt, en = towns[m].get('pt', []), list(towns[m].get('en', []))
        used = [False] * len(en)
        for ty, x, y, nm in pt:
            match = ''
            for j, (ty2, x2, y2, nm2) in enumerate(en):
                if not used[j] and (ty2, x2, y2) == (ty, x, y): used[j] = True; match = nm2; break
            rows.append('\t'.join(['T', m, ty, x, y, match, nm])); nt += 1
        for j, (ty, x, y, nm) in enumerate(en):
            if not used[j]: rows.append('\t'.join(['T', m, ty, x, y, nm, ''])); nt += 1
    counts['T'] = nt
    return rows, counts


def main(argv):
    if len(argv) == 1: root, out = r'C:\Ragnarok Online', argv[0]
    elif len(argv) == 2: root, out = argv
    else: print(__doc__); return 2
    grf_path = os.path.join(root, 'data.grf')
    g = grfmod.Grf(grf_path)
    base = os.path.basename(grf_path)
    sky, nmaps, nclouds = mapsky(g)
    nav, counts = navigation(g, root)
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'mapsky.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(['# mapsky.tsv: built by client/tools/clientdata/build_world_tables.py from %s (GRF 0x%x): %s; see that file for the rows'
                           % (base, g.version, SKY)] + sky) + '\n')
    with open(os.path.join(out, 'navigation.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(['# navigation.tsv: built by client/tools/clientdata/build_world_tables.py from %s (GRF 0x%x) navigation\\navi_*_br.lub and '
                           'System\\Towninfo.lub + System\\english\\Towninfo.lub; see that file for the rows' % (base, g.version)] + nav) + '\n')
    print('mapsky.tsv: %d maps, %d cloud layers; navigation.tsv: %s; map names in English: %s' % (nmaps, nclouds, ', '.join('%s %d' % (k, counts[k]) for k in 'MLENDBT' if k in counts), counts['en_how']))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
