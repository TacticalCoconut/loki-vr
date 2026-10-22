#!/usr/bin/env python3
r"""
Builds the skill window's client data from the RO client's own Lua tables, read straight out of the GRF (0x200 or 0x300):
  <out>/skilltree.tsv  the job tree (inherit chain, tab names, English job names), each job's grid positions, the prerequisite
                       lists (the generic one and the per-job ones) and the per-level data the window shows
  <out>/skills.tsv     id, aegis name, English name, max level, SP per level, English description (the 6 columns of before),
                       then (client wave 5, w5ui) the Portuguese name and the Portuguese description

  python build_skilltree.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

Sources inside the GRF (the files the client itself loads; lub51.py runs their Lua 5.1 bytecode):
  data\luafiles514\lua files\skillinfoz\skillid.lub             SKID (aegis name -> id)
  data\luafiles514\lua files\skillinfoz\jobinheritlist.lub      JOBID, JOB_INHERIT_LIST (job -> the job it grows from),
                                                                JOB_INHERIT_LIST2 (transcendent 3rd jobs -> their plain 3rd)
  data\luafiles514\lua files\skillinfoz\skillinfo_f.lub         the client's own skill-window functions (JobSkillTab etc.)
  data\luafiles514\lua files\skillinfoz\skilltreeview.lub       SKILL_TREEVIEW_FOR_JOB[job][pos] = skill, and the tab names
                                                                (JobSkillTab.ChangeSkillTabName(job, '1a', '3a', '4a'))
  data\luafiles514\lua files\skillinfoz\skillinfolist_data.lub  SKILL_INFO_LIST (MaxLv, SpAmount, AttackRange, _NeedSkillList,
  data\luafiles514\lua files\skillinfoz\skillinfolist_enus.lub  NeedSkillList[job], Type, bSeperateLv, ApAmount) merged with
                                                                the English SkillName strings, as skillinfolist_data.lub does
  data\english\luafiles514\lua files\skillinfoz\skilldescript.lub  SKILL_DESCRIPT (English description lines)
  data\luafiles514\lua files\datainfo\pcjobnamegender_enus.lub  PCJobNameTableMan (English job names)
Client wave 5 (w5ui): the Portuguese columns. The same files are run again in a fresh Lua state with the LATAM client's Portuguese
string tables in place of the English ones (skillinfolist_ptbr.lub, pcjobnamegender_ptbr.lub: PCJobNameTableMan / ...Woman, the
male and female job names) and data\luafiles514\lua files\skillinfoz\skilldescript.lub (the Portuguese descriptions; the English
ones live under data\english\).
Not used: data\english\...\skillinfolist.lub (stale: it names SKID.BA_FROSTJOKE and SKID.MH_SONIC_CRAW, which the current
skillid.lub no longer has, so the client could not load it) and newskillinfo\jobinheritlist.lub (older: no 4th jobs).

skilltree.tsv rows (tab separated; '#' lines are comments):
  J  job  JT_name  parent|-1  parent2|-1  english_name  tabnames(csv, '' = none registered)  portuguese_name_male  portuguese_name_female
  T  job  pos  skill                         (pos: the grid index, row = pos / 7, column = pos % 7)
  N  skill  job|-1  need_skill:level,...     (-1 = _NeedSkillList, the list for every job without its own)
  S  skill  type  seplv(0/1)  range(csv per level)  ap(csv per level)
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

LUA = 'data\\luafiles514\\lua files\\'
FILES = [
    LUA + 'skillinfoz\\skillid.lub',
    LUA + 'skillinfoz\\jobinheritlist.lub',
    LUA + 'skillinfoz\\skillinfo_f.lub',
    LUA + 'skillinfoz\\skilltreeview.lub',
    LUA + 'skillinfoz\\skillinfolist_enus.lub',
    LUA + 'skillinfoz\\skillinfolist_data.lub',
    LUA + 'datainfo\\pcjobnamegender_enus.lub',
]
DESCRIPT = 'data\\english\\luafiles514\\lua files\\skillinfoz\\skilldescript.lub'
# client wave 5 (w5ui): the Portuguese pass — FILES with the Portuguese string tables, then the Portuguese descriptions
FILES_PT = [f.replace('skillinfolist_enus.lub', 'skillinfolist_ptbr.lub').replace('pcjobnamegender_enus.lub', 'pcjobnamegender_ptbr.lub')
            for f in FILES]
DESCRIPT_PT = LUA + 'skillinfoz\\skilldescript.lub'


def dec(b):
    if b is None: return ''
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ')


def seq(t):
    """A Lua sequence (1..n) as a Python list."""
    if not isinstance(t, lub51.LuaTable): return []
    return [t.get(i) for i in range(1, t.length() + 1)]


def need_list(t):
    """{ {skill, level}, ... } -> 'skill:level,...' (the client's _NeedSkillList / NeedSkillList[job] entries)."""
    out = []
    for e in seq(t):
        if isinstance(e, lub51.LuaTable) and isinstance(e.get(1), int):
            out.append('%d:%d' % (e.get(1), e.get(2) if isinstance(e.get(2), int) else 0))
    return ','.join(out)


def nums(t):
    return ','.join(str(v) for v in seq(t) if isinstance(v, (int, float)))


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    for f in FILES:
        data = g.read(f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        lub51.run(data)
    G = lub51.GLOBALS
    SKID, JOBID = G.get(b'SKID'), G.get(b'JOBID')
    inherit, inherit2 = G.get(b'JOB_INHERIT_LIST'), G.get(b'JOB_INHERIT_LIST2')
    tree, info = G.get(b'SKILL_TREEVIEW_FOR_JOB'), G.get(b'SKILL_INFO_LIST')
    tabs = {}
    jst = G.get(b'JobSkillTab')
    for e in seq(jst):  # skillinfo_f.lub: JobSkillTab.ChangeSkillTabName stores {job, TabName1st..4th}
        if isinstance(e, lub51.LuaTable) and isinstance(e.get(b'job'), int):
            tabs[e.get(b'job')] = [dec(e.get(k)) for k in (b'TabName1st', b'TabName2nd', b'TabName3rd', b'TabName4th') if e.get(k) is not None]
    names = G.get(b'PCJobNameTableMan')
    ddata = g.read(DESCRIPT)
    if ddata is None: raise SystemExit('missing in the GRF: ' + DESCRIPT)
    lub51.run(ddata)
    descr = G.get(b'SKILL_DESCRIPT')

    # client wave 5 (w5ui): the Portuguese pass in a fresh Lua state (the tables above stay as they are)
    lub51.reset()
    for f in FILES_PT:
        data = g.read(f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        lub51.run(data)
    GP = lub51.GLOBALS
    info_pt = GP.get(b'SKILL_INFO_LIST')
    names_m, names_w = GP.get(b'PCJobNameTableMan'), GP.get(b'PCJobNameTableWoman')
    pdata = g.read(DESCRIPT_PT)
    if pdata is None: raise SystemExit('missing in the GRF: ' + DESCRIPT_PT)
    lub51.run(pdata)
    descr_pt = GP.get(b'SKILL_DESCRIPT')

    os.makedirs(out, exist_ok=True)
    jobs = sorted((v, k) for k, v in JOBID.h.items() if isinstance(v, int) and isinstance(k, bytes))
    rows = ['# skilltree.tsv: built by client/tools/clientdata/build_skilltree.py from ' + os.path.basename(grf_path) +
            ' (GRF 0x%x); see that file for the row kinds' % g.version]
    nj = nt = nn = ns = 0
    for jid, jname in jobs:
        p = inherit.get(jid); p2 = inherit2.get(jid)
        nm = names.get(jid) if names is not None else None
        pm = names_m.get(jid) if names_m is not None else None
        pw = names_w.get(jid) if names_w is not None else None
        rows.append('\t'.join(['J', str(jid), jname.decode(), str(p if isinstance(p, int) else -1), str(p2 if isinstance(p2, int) else -1),
                               clean(dec(nm)), ','.join(tabs.get(jid, [])), clean(dec(pm)), clean(dec(pw))])); nj += 1
    for jid in sorted(k for k in tree.h if isinstance(k, int)):
        t = tree.get(jid)
        for pos in sorted(k for k in t.h if isinstance(k, int)):
            sk = t.get(pos)
            if isinstance(sk, int): rows.append('\t'.join(['T', str(jid), str(pos), str(sk)])); nt += 1
    for sid in sorted(k for k in info.h if isinstance(k, int)):
        e = info.get(sid)
        if not isinstance(e, lub51.LuaTable): continue
        gen = e.get(b'_NeedSkillList')
        if isinstance(gen, lub51.LuaTable): rows.append('\t'.join(['N', str(sid), '-1', need_list(gen)])); nn += 1
        per = e.get(b'NeedSkillList')
        if isinstance(per, lub51.LuaTable):
            for jid in sorted(k for k in per.h if isinstance(k, int)):
                rows.append('\t'.join(['N', str(sid), str(jid), need_list(per.get(jid))])); nn += 1
        typ = e.get(b'Type'); sep = e.get(b'bSeperateLv')
        rows.append('\t'.join(['S', str(sid), clean(dec(typ)) if isinstance(typ, bytes) else '', '1' if sep is True else '0',
                               nums(e.get(b'AttackRange')), nums(e.get(b'ApAmount'))])); ns += 1
    with open(os.path.join(out, 'skilltree.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        o.write('\n'.join(rows) + '\n')

    # skills.tsv: every SKID, as build_skilltables.py wrote it from the old install (id, aegis, name, max level, SP, description),
    # then (client wave 5, w5ui) the Portuguese name and description, found the same way
    ids = sorted((v, k) for k, v in SKID.h.items() if isinstance(v, int) and isinstance(k, bytes))
    n = npn = npd = 0
    with open(os.path.join(out, 'skills.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for sid, aegis in ids:
            e = info.get(sid); d = descr.get(sid)
            lines = [clean(dec(x)) for x in seq(d) if isinstance(x, bytes)]
            nm = e.get(b'SkillName') if isinstance(e, lub51.LuaTable) else None
            name = clean(dec(nm)) if isinstance(nm, bytes) and nm else (lines[0] if lines else '')
            maxlv = e.get(b'MaxLv') if isinstance(e, lub51.LuaTable) else 0
            sp = nums(e.get(b'SpAmount')) if isinstance(e, lub51.LuaTable) else ''
            ep = info_pt.get(sid) if info_pt is not None else None
            dp = descr_pt.get(sid) if descr_pt is not None else None
            plines = [clean(dec(x)) for x in seq(dp) if isinstance(x, bytes)]
            pn = ep.get(b'SkillName') if isinstance(ep, lub51.LuaTable) else None
            pname = clean(dec(pn)) if isinstance(pn, bytes) and pn else (plines[0] if plines else '')
            if pname: npn += 1
            if plines: npd += 1
            # wave 28 (wtextc): zero-width spaces (U+200B) out of the texts (Data/GrfTools.cs does the same)
            o.write('\t'.join([str(sid), aegis.decode(), name.replace('\u200b', ''), str(maxlv if isinstance(maxlv, int) else 0), sp, '\\n'.join(lines).replace('\u200b', ''),
                               pname.replace('\u200b', ''), '\\n'.join(plines).replace('\u200b', '')]) + '\n'); n += 1
    print('skilltree.tsv: %d jobs, %d tree cells, %d need lists, %d skills; skills.tsv: %d skills (%d with a description; '
          'Portuguese: %d names, %d descriptions)' % (nj, nt, nn, ns, n, sum(1 for sid, _ in ids if descr.get(sid) is not None), npn, npd))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
