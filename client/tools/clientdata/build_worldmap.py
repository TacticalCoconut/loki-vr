#!/usr/bin/env python3
r"""
Builds data/worldmap.tsv for Loki's world map window (UI/WorldMapWindow.cs, client wave 3 w3social) from the client's own
world-view data in the GRF (the LATAM install's data.grf is GRF 0x300: grf.py reads it; lub51.py runs the Lua 5.1 bytecode).

  python build_worldmap.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30)
      writes <out dir>\worldmap.tsv (UTF-8); run it into the Unity project's Assets\StreamingAssets\data

Sources (data\luafiles514\lua files\worldviewdata\ in the GRF, the files the client's world map window reads):
  worldviewdata_list.lub      World_List: { name, table, dungeon table, image } per world (7 in this install); the image is
                              data\texture\<UI>\<image> (1280 x 1024: worldmap.jpg, midgard_north.jpg, ...), read by the client
                              from the GRF at run time, never copied here.
  worldviewdata_table.lub     per world, <table> = { group, "<map>.rsw", x1, y1, x2, y2, label, level } rows in image pixels
                              (origin top-left): group < 100 = a dungeon floor (the rectangle is its dungeon's name box, the
                              label its floor name), group >= 100 = a town or field tile (the label is a town's name, or a
                              field's notable monster with its level); <table>_Dun = { group, x1, y1, x2, y2, name, levels } the
                              red dungeon markers.
  worldviewdata_language.lub  WORLD_MSGID: the labels the tables use. English: the data\english\ copies of
                              worldviewdata_language.lub and worldviewdata_list.lub (the client's English overrides, as
                              data\english\mapnametable.txt is for map names); Portuguese: the base worldviewdata_language.lub
                              + worldviewdata_ptbr.lub + the base worldviewdata_list.lub.
  worldviewdata_info.lub      colours (DungeonBox 255,0,0 alpha 180 ...): written as I rows.

Rows (tab-separated):
  W  world  image  name_en  name_pt
  M  world  group  map  x1  y1  x2  y2  label_en  label_pt  level
  D  world  group  x1  y1  x2  y2  name_en  name_pt  levels
  I  key  value
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

BASE = 'data\\luafiles514\\lua files\\worldviewdata\\'
ENGLISH = 'data\\english\\luafiles514\\lua files\\worldviewdata\\'


def dec(b):
    if b is None: return ''
    if isinstance(b, (int, float)): return str(b)
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ').strip().replace('\u200b', '')  # wave 28 (wtextc): zero-width spaces out


def seq(t):
    return [t.get(i) for i in range(1, t.length() + 1)] if isinstance(t, lub51.LuaTable) else []


def load(g, files):
    lub51.GLOBALS = lub51.LuaTable()
    lub51._setup()
    for f in files:
        data = g.read(f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        lub51.run(data)
    return lub51.GLOBALS


def worlds(G):
    out = []
    for w in seq(G.get(b'World_List')):
        name, table, dun, image = seq(w)[:4]
        rows = [seq(e) for e in seq(G.get(table))]
        duns = [seq(e) for e in seq(G.get(dun))]
        out.append((dec(name), table, dun, dec(image), rows, duns))
    return out


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    en = worlds(load(g, [ENGLISH + 'worldviewdata_language.lub', BASE + 'worldviewdata_table.lub', ENGLISH + 'worldviewdata_list.lub']))
    pt = worlds(load(g, [BASE + 'worldviewdata_language.lub', BASE + 'worldviewdata_ptbr.lub', BASE + 'worldviewdata_table.lub', BASE + 'worldviewdata_list.lub']))
    info = load(g, [BASE + 'worldviewdata_info.lub']).get(b'worldview_info')
    if len(en) != len(pt): raise SystemExit('the English and Portuguese world lists differ: %d vs %d' % (len(en), len(pt)))
    rows = ['# worldmap.tsv: built by client/tools/clientdata/build_worldmap.py from %s (GRF 0x%x); see that file for the rows'
            % (os.path.basename(grf_path), g.version)]
    nm = nd = 0
    for wi, (e, p) in enumerate(zip(en, pt)):
        ename, etab, edun, eimg, erows, eduns = e
        pname, ptab, pdun, pimg, prows, pduns = p
        if (etab, edun, eimg) != (ptab, pdun, pimg) or len(erows) != len(prows) or len(eduns) != len(pduns):
            raise SystemExit('world %d differs between the languages' % wi)
        rows.append('\t'.join(['W', str(wi), eimg, clean(ename), clean(pname)]))
        for a, b in zip(erows, prows):
            if a[:6] != b[:6] or a[7:] != b[7:]: raise SystemExit('row differs between the languages: %r / %r' % (a, b))
            m = dec(a[1]).lower()
            if m.endswith('.rsw'): m = m[:-4]
            rows.append('\t'.join(['M', str(wi), dec(a[0]), m] + [dec(v) for v in a[2:6]] + [clean(dec(a[6])), clean(dec(b[6])), clean(dec(a[7]))]))
            nm += 1
        for a, b in zip(eduns, pduns):
            if a[:5] != b[:5]: raise SystemExit('dungeon marker differs between the languages: %r / %r' % (a, b))
            rows.append('\t'.join(['D', str(wi), dec(a[0])] + [dec(v) for v in a[1:5]] + [clean(dec(a[5])), clean(dec(b[5])), clean(dec(a[6]))]))
            nd += 1
    if isinstance(info, lub51.LuaTable):
        for k in sorted(info.h, key=lambda x: dec(x)):
            v = info.h[k]
            if isinstance(v, (int, float)): rows.append('\t'.join(['I', dec(k), dec(v)]))
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, 'worldmap.tsv')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(rows) + '\n')
    print('%s: %d worlds, %d map rows, %d dungeon markers' % (path, len(en), nm, nd))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
