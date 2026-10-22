#!/usr/bin/env python3
r"""
Builds <out>/spritenames.txt (Sprites/RoNames.cs: which sprite file a job / monster / NPC / headgear / garment / weapon view
uses) from the RO client's own datainfo tables in the GRF (client wave 3b, w3data). The earlier copy was made with
../rodata/dump.lua + a conversion step that was never committed; this tool writes the same rows from the same tables.

  python build_spritenames.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

Rows (tab separated; the file is Latin-1 text whose characters are the CP949 bytes of Gravity's file names, as RoNames reads it):
  pc   <job>   <body sprite name>   ../rodata/pcjobnames_hex.json (roBrowserLegacy's JobNameTable.js + JobConst.js evaluated:
                                    the LATAM pcjobname.lub only carries display names; the file names are in Ragexe); numeric
                                    keys only (rB has one 'undefined' key)
  npc  <id>    <sprite name>        JobNameTable  (datainfo\jobname.lub, keyed through jobidentity.lub / npcidentity.lub's jobtbl)
  acc  <view>  <sprite name>        AccNameTable  (datainfo\accname.lub, keyed through accessoryid.lub's ACCESSORY_IDs)
  robe <view>  <sprite name>        RobeNameTable (datainfo\spriterobename.lub, keyed through spriterobeid.lub)
  wpn  <view>  <name>               WeaponNameTable (datainfo\weapontable.lub)
Each kind sorted by id. The lubs run in ../rodata/dump.lua's order, each as dump.lua's pcall(dofile, ...) does: a chunk that
stops with an error keeps what it set before (this GRF: pcjobnamegender.lub needs a JOBID table no datainfo file makes, and
shadowtable.lub indexes a nil key; neither table is used here).
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

DI = 'data\\luafiles514\\lua files\\datainfo\\'
ORDER = ['jobidentity', 'npcidentity', 'jobname', 'pcjobname', 'pcjobnamegender', 'accessoryid', 'accname', 'weapontable',
         'shadowtable', 'spriterobeid', 'spriterobename']
KINDS = [(b'npc', b'JobNameTable'), (b'acc', b'AccNameTable'), (b'robe', b'RobeNameTable'), (b'wpn', b'WeaponNameTable')]
PCJOBNAMES = os.path.join(HERE, '..', 'rodata', 'pcjobnames_hex.json')


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    errors = []
    for f in ORDER:
        data = g.read(DI + f + '.lub')
        if data is None: errors.append('%s: not in the GRF' % f); continue
        try: lub51.run(data)
        except lub51.LuaError as e: errors.append('%s: %s' % (f, e))
    G = lub51.GLOBALS
    rows = [b'# Loki sprite name tables. Encoding: Latin-1 (EUC-KR bytes preserved). Fields are tab-separated.',
            ('# built by client/tools/clientdata/build_spritenames.py from %s (GRF 0x%x)' % (os.path.basename(grf_path), g.version)).encode(),
            b'# pc <jobid> <bodySpriteName>  - PC job -> data\\sprite\\<human>\\<body>\\<sex>\\<name>_<sex> (Gravity file names; ids per roBrowserLegacy JobConst)',
            b'# npc <id> <spriteName>        - mob/NPC id -> sprite name (client jobname.lub + npcidentity.lub)',
            b'# acc <viewid> <spriteName>    - headgear view id (accname.lub)',
            b'# robe <viewid> <spriteName>   - garment view id (spriterobename.lub)',
            b'# wpn <viewid> <name>          - weapon view (weapontable.lub)']
    counts = []
    pc = json.load(open(PCJOBNAMES, encoding='utf-8'))
    ids = sorted(int(k) for k in pc if k.isdigit() and k.isascii())
    for i in ids: rows.append(b'pc\t%d\t' % i + bytes.fromhex(pc[str(i)]))
    counts.append('pc %d' % len(ids))
    for kind, name in KINDS:
        t = G.get(name)
        if not isinstance(t, lub51.LuaTable): raise SystemExit('%s was not made by the lubs (%s)' % (name.decode(), '; '.join(errors)))
        keys = [k for k, _ in t.items()]
        if not all(isinstance(k, int) and not isinstance(k, bool) for k in keys): raise SystemExit('%s has a key that is not an id' % name.decode())
        for k in sorted(keys):
            v = t.get(k)
            if not isinstance(v, bytes) or any(c in v for c in b'\t\r\n'): raise SystemExit('%s[%d] is not a plain name: %r' % (name.decode(), k, v))
            rows.append(kind + b'\t%d\t' % k + v)
        counts.append('%s %d' % (kind.decode(), len(keys)))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'spritenames.txt'), 'wb') as f:
        f.write(b'\n'.join(rows) + b'\n')
    print('spritenames.txt: %s; lub errors (as dump.lua\'s pcall): %s' % (', '.join(counts), '; '.join(errors) or 'none'))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
