#!/usr/bin/env python3
r"""
Builds the equipment window's client data (client wave 3, w3equip) from the RO client's own Lua tables, read straight out
of the GRF (0x200 or 0x300) like build_skilltree.py:
  <out>/randomoptions.tsv   the random option ("enchant option") names the item details show

  python build_equip_tables.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

Sources inside the GRF (the files the client itself loads; lub51.py runs their Lua 5.1 bytecode):
  data\luafiles514\lua files\datainfo\enumvar.lub                         EnumVAR[name] = { id, n } (the option ids: the
                                                                          ItemOptions.index of EQUIPITEM_INFO / 0x0b41)
  data\luafiles514\lua files\datainfo\addrandomoptionnametable_enus.lub   NameTable_VAR[EnumVAR.X[1]] = "Max HP + %d" (English)
  data\luafiles514\lua files\datainfo\addrandomoptionnametable.lub        the same table in Portuguese (the LATAM default)
  data\luafiles514\lua files\datainfo\addrandomoption_f.lub               GetVarOptionName(id), SetPercentValue(id)
roBrowserLegacy loads the same pair (DB/DBManager.js:449-455: enumvar.lub + addrandomoptionnametable.lub -> NameTable_VAR)
and shows an option as NameTable_VAR[index] with "%d" replaced by the value and "%%" by "%" (UI/Components/ItemInfo/
ItemInfo.js:216-235, only for identified items). rB is a reimplementation, not the official client.

randomoptions.tsv rows (tab separated, UTF-8; '#' lines are comments):
  id  enum_name  format_en  format_pt  percent
    percent = 1 where the client's own SetPercentValue(id) (addrandomoption_f.lub) returns true: VAR_CRITICALSUCCESSVALUE (24) and
    the RACE_CRI_PERCENT_* range (117-126, whose English formats read "Crit + (%d / 10) ..."). What the official client does with it is not known here (no official source; rB ignores it),
    so the game shows the value as sent. rAthena's item_randomopt_db.yml divides RACE_CRI_PERCENT_* values by 10
    (bCriticalAddRace ...,getrandomoptinfo(ROA_VALUE)/10) and not VAR_CRITICALSUCCESSVALUE.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

DI = 'data\\luafiles514\\lua files\\datainfo\\'
ENUM, NAMES_EN, NAMES_PT, FUNCS = DI + 'enumvar.lub', DI + 'addrandomoptionnametable_enus.lub', DI + 'addrandomoptionnametable.lub', DI + 'addrandomoption_f.lub'


def dec(b):
    if b is None: return ''
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ')


def read(g, path):
    data = g.read(path)
    if data is None: raise SystemExit('missing in the GRF: ' + path)
    return data


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    G = lub51.GLOBALS
    lub51.run(read(g, ENUM))
    enum = G.get(b'EnumVAR')
    ids = {}
    for name, v in enum.h.items():
        vid = v.get(1) if isinstance(v, lub51.LuaTable) else v
        if isinstance(name, bytes) and isinstance(vid, int): ids[vid] = name.decode()
    lub51.run(read(g, NAMES_EN))
    en = {k: v for k, v in G.get(b'NameTable_VAR').h.items() if isinstance(k, int)}
    lub51.run(read(g, NAMES_PT))  # redefines NameTable_VAR (same keys, Portuguese strings)
    pt = {k: v for k, v in G.get(b'NameTable_VAR').h.items() if isinstance(k, int)}
    lub51.run(read(g, FUNCS))
    pct = G.get(b'SetPercentValue')
    rows = ['# randomoptions.tsv: built by client/tools/clientdata/build_equip_tables.py from ' + os.path.basename(grf_path) +
            ' (GRF 0x%x): id, EnumVAR name, English format, Portuguese format, SetPercentValue' % g.version]
    n = 0
    for vid in sorted(set(en) | set(pt)):
        name = ids.get(vid, '')
        if name == 'EnumVAR_LAST': continue
        p = lub51.call(pct, [vid]) if pct is not None else [False]
        rows.append('\t'.join([str(vid), name, clean(dec(en.get(vid))), clean(dec(pt.get(vid))), '1' if p and p[0] is True else '0'])); n += 1
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'randomoptions.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        o.write('\n'.join(rows) + '\n')
    print('randomoptions.tsv: %d options (%d EnumVAR names, %d English, %d Portuguese, %d percent)'
          % (n, len(ids), len(en), len(pt), sum(1 for r in rows[1:] if r.endswith('\t1'))))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
