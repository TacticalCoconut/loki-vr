#!/usr/bin/env python3
r"""
Builds <out>/hateffects.tsv (Sprites/HatEffects.cs: what a hat effect id of ZC_EQUIPMENT_EFFECT 0x0a3b draws) from the RO client's
hat-effect tables in the GRF (client wave 4, w4units). roBrowserLegacy reads the same two files the same way
(DB/DBManager.js:5991-6121 loadHatEffectInfo: hateffectids.lub, then hateffectinfo.lub, then every hatEffectTable entry).

  python build_hateffects.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

Sources: data\luafiles514\lua files\hateffectinfo\hateffectids.lub (HatEFID: name -> id) and
data\luafiles514\lua files\hateffectinfo\hateffectinfo.lub (hatEffectTable: id -> { resourceFileName, hatEffectID, hatEffectPos,
hatEffectPosX, isAttachedHead, isRenderBeforeCharacter, isIgnoreRiding, ... }), run with lub51.py in that order.
hateffects.tsv rows, one per hatEffectTable entry, sorted by id (the table's keys are numbers; no two rows share one), tab separated:
  id  resourceFileName  hatEffectID  hatEffectPos  hatEffectPosX  isAttachedHead  isRenderBeforeCharacter  isIgnoreRiding
strings as their bytes (the file is Latin-1 text whose characters are the GRF's bytes, as spritenames.txt), numbers as Lua's tostring
("%.14g"), a field the entry lacks empty (numbers: resourceFileName / hatEffectID) or 0 (hatEffectPos / hatEffectPosX), flags 1 when
the value is truthy else 0.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

BASE = 'data\\luafiles514\\lua files\\hateffectinfo\\'
FILES = [BASE + 'hateffectids.lub', BASE + 'hateffectinfo.lub']


def num(v):
    """Lua's tostring of a number: "%.14g" (luaconf.h LUA_NUMBER_FMT)."""
    if isinstance(v, bool) or not isinstance(v, (int, float)): raise SystemExit('not a number: %r' % (v,))
    return ('%.14g' % float(v)).encode()


def field(t, name):
    return lub51.index(t, name.encode())


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    for f in FILES:
        data = g.read(f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        lub51.run(data)
    T = lub51.GLOBALS.get(b'hatEffectTable')
    if not isinstance(T, lub51.LuaTable): raise SystemExit('hatEffectTable is not a table')
    entries = []
    for k, v in T.items():
        if isinstance(k, bool) or not isinstance(k, (int, float)): raise SystemExit('hatEffectTable key is not a number: %r' % (k,))
        if not isinstance(v, lub51.LuaTable): raise SystemExit('hatEffectTable[%r] is not a table' % (k,))
        entries.append((k, v))
    if len(set(k for k, _ in entries)) != len(entries): raise SystemExit('two entries share an id')
    entries.sort(key=lambda e: e[0])
    rows = []
    for k, v in entries:
        res = field(v, 'resourceFileName')
        hid = field(v, 'hatEffectID')
        pos, posx = field(v, 'hatEffectPos'), field(v, 'hatEffectPosX')
        cols = [num(k),
                res if isinstance(res, bytes) else b'',
                num(hid) if hid is not None else b'',
                num(pos) if pos is not None else b'0',
                num(posx) if posx is not None else b'0',
                b'1' if lub51.truthy(field(v, 'isAttachedHead')) else b'0',
                b'1' if lub51.truthy(field(v, 'isRenderBeforeCharacter')) else b'0',
                b'1' if lub51.truthy(field(v, 'isIgnoreRiding')) else b'0']
        for c in cols:
            if b'\t' in c or b'\n' in c or b'\r' in c: raise SystemExit('a field holds a tab or a line break: %r' % (c,))
        rows.append(b'\t'.join(cols))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'hateffects.tsv'), 'wb') as o:
        for r in rows: o.write(r + b'\n')
    print('hateffects.tsv: %d hat effects (%d STR files, %d effect-table ids)' % (
        len(rows), sum(1 for r in rows if r.split(b'\t')[1]), sum(1 for r in rows if r.split(b'\t')[2])))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
