#!/usr/bin/env python3
r"""
Builds <out>/efst.tsv (Game/Progression.cs SkillDb: a status icon's name and kind) from the RO client's state-icon tables in
the GRF (client wave 3b, w3data). It is the efst half of dumpskills.lua + build_skilltables.py, run with lub51.py: those two
need lua5.1 (not on this PC), and dumpskills.lua's first half loads data\english\...\skillinfolist.lub, which the current GRF
can no longer load (it names SKID.BA_FROSTJOKE and SKID.MH_SONIC_CRAW, gone from skillid.lub: see build_skilltree.py), so the
script would stop before it writes efst.tsv. The efst half reads nothing the skill files make.

  python build_efst.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

Sources (dumpskills.lua's order): data\luafiles514\lua files\stateicon\efstids.lub (EFST_IDs), the English
data\english\luafiles514\lua files\stateicon\stateiconinfo.lub (StateIconList, COLOR_TITLE_BUFF / _DEBUFF / _TOGGLE),
data\luafiles514\lua files\stateicon\stateiconimginfo.lub (StateIconImgList: dumpskills.lua writes it in a fifth column
that build_skilltables.py never reads, so it is loaded and not written).
efst.tsv rows, as build_skilltables.py writes them: id, EFST_ name, title (the first descript line: UTF-8 / windows-1252 /
Latin-1, tabs and line breaks made spaces), kind ('buff' / 'debuff' / 'toggle' when descript[1][2] is that COLOR_TITLE_ table,
the same table object as in Lua's ==, else ''); sorted by id (dumpskills.lua's table.sort; no two names share an id here,
the tool stops if they do: Lua's sort is not stable). Wave 28 (wtextc): a fifth column, the Portuguese title from the GRF's
data\luafiles514\lua files\stateicon\stateiconinfo.lub ('' when it has none); zero-width spaces (U+200B) removed from titles.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

FILES = ['data\\luafiles514\\lua files\\stateicon\\efstids.lub',
         'data\\english\\luafiles514\\lua files\\stateicon\\stateiconinfo.lub',
         'data\\luafiles514\\lua files\\stateicon\\stateiconimginfo.lub']


def dec(b):
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ').replace('\u200b', '')


PT_INFO = 'data\\luafiles514\\lua files\\stateicon\\stateiconinfo.lub'  # wave 28 (wtextc): the Portuguese titles


def title_of(s):
    """wave 28 (wtextc): StateIconList[id].descript[1][1] of the Portuguese table, cleaned as the English one ('' when none)."""
    d = lub51.index(s, b'descript') if lub51.truthy(s) else None
    d1 = lub51.index(d, 1) if lub51.truthy(d) else None
    t = lub51.index(d1, 1) if lub51.truthy(d1) else None
    return clean(dec(lua_tostring(t))) if lub51.truthy(t) else ''


def lua_tostring(v):
    """tostring() / f:write of what the script writes: strings as they are, numbers "%.14g" (luaconf.h LUA_NUMBER_FMT)."""
    if isinstance(v, bytes): return v
    if isinstance(v, (int, float)) and not isinstance(v, bool): return ('%.14g' % float(v)).encode()
    raise SystemExit('dumpskills.lua would write %r' % (v,))


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    G = lub51.GLOBALS
    # dumpskills.lua sets JOBID = setmetatable({}, { __index = function() return 0 end }) before its dofiles
    jobid = lub51.LuaTable(); meta = lub51.LuaTable(); meta.set(b'__index', lambda t, k: [0]); jobid.meta = meta
    G.set(b'JOBID', jobid)
    for f in FILES:
        data = g.read(f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        lub51.run(data)
    E, S = G.get(b'EFST_IDs'), G.get(b'StateIconList')
    buff, debuff, toggle = G.get(b'COLOR_TITLE_BUFF'), G.get(b'COLOR_TITLE_DEBUFF'), G.get(b'COLOR_TITLE_TOGGLE')
    # wave 28 (wtextc): the Portuguese titles too (a fifth column), from the GRF's own data\luafiles514\...\stateiconinfo.lub,
    # run after the English pass in the same state (it names EFST_IDs); Data/GrfTools.cs Efst does the same
    Spt = None
    pt_info = g.read(PT_INFO)
    if pt_info is not None:
        lub51.run(pt_info)
        Spt = G.get(b'StateIconList')
        if Spt is S: Spt = None
    eids = [(i, name) for name, i in E.items() if isinstance(i, (int, float)) and not isinstance(i, bool)]  # type(id) == "number"
    if len(set(i for i, _ in eids)) != len(eids): raise SystemExit('EFST_IDs gives two names one id: table.sort would order them at random')
    eids.sort(key=lambda p: p[0])
    rows = []
    for i, name in eids:
        s = S.get(i) if isinstance(S, lub51.LuaTable) else None  # StateIconList and StateIconList[id]
        title, kind = b'', ''
        d = lub51.index(s, b'descript') if lub51.truthy(s) else None
        d1 = lub51.index(d, 1) if lub51.truthy(d) else None
        if lub51.truthy(s) and lub51.truthy(d) and lub51.truthy(d1):
            t = lub51.index(d1, 1)
            title = t if lub51.truthy(t) else b''
            c = lub51.index(d1, 2)
            if lub51.lua_eq(c, debuff): kind = 'debuff'
            elif lub51.lua_eq(c, buff): kind = 'buff'
            elif lub51.lua_eq(c, toggle): kind = 'toggle'
        # build_skilltables.py: c[0], c[1], clean(dec(c[2])), c[3] of the line dumpskills.lua wrote (c[2] = hex(title))
        pt = title_of(Spt.get(i)) if isinstance(Spt, lub51.LuaTable) else ''
        rows.append('\t'.join([lua_tostring(i).decode('ascii'), lua_tostring(name).decode('ascii'), clean(dec(lua_tostring(title))), kind, pt]))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'efst.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for r in rows: o.write(r + '\n')
    print('efst.tsv: %d status icons (%d with a title, %d with a Portuguese title)' % (len(rows), sum(1 for r in rows if r.split('\t')[2]), sum(1 for r in rows if r.split('\t')[4])))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
