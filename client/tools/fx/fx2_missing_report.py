#!/usr/bin/env python3
r"""Client wave 4c (w4fx2): the per-skill record for the active player skills that drew nothing after w4fx (the orchestrator's list,
fx_missing_active.tsv: id, aegis, name, reason) — what each draws now, from which source, and for the rest what was found and where.

  python fx2_missing_report.py <missing list .tsv> <fx_coverage --list .tsv> <data.grf> <data/fx dir> <skills.tsv> <rathena> <roBrowserLegacy src> <out .tsv>

Columns: id, aegis, name (the list's: rAthena skill_db Description), after w4fx (reason), after w4fx2, drawn (yes / sound / body spin /
no), w4fx2 source, evidence (what the sources hold for
it: the effect ids rB's SkillEffect names with rAthena's doc/effect_list.md description and rB's placeholder line in
DB/Effects/EffectTable.js when rB has no part for the id; the GRF effect files named after the skill that no rule could use and why).
Every skill was also looked up in: rB SkillEffect.js / EffectTable.js / Engine code, the GRF's ez2 tables, the GRF's skilleffectinfolist.lub,
the GRF's effect files by name, and the online sources listed in design notes (none held more).
"""
import json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'combat')); sys.path.insert(0, HERE)
from grf import Grf  # noqa: E402
import fx_coverage as fc  # noqa: E402
import grf_skill_effects as gse  # noqa: E402

BS = chr(92)


def main(a):
    miss_path, cov_path, grf_path, fx_dir, skills_tsv, rathena, rb_src, out = a[:8]
    g = Grf(grf_path)
    et = json.load(open(os.path.join(fx_dir, 'effect_table.json'), encoding='utf-8'))
    se = json.load(open(os.path.join(fx_dir, 'skill_effect.json'), encoding='utf-8'))
    aegis = fc.read_aegis(skills_tsv)  # aegis names → ids only (the name column is the list's: rAthena's skill_db Description, no GRF text)
    cov = {}
    for ln in open(cov_path, encoding='utf-8'):
        if ln.startswith('# unit id'): break
        if ln[:1].isdigit(): c = ln.rstrip('\n').split('\t'); cov[int(c[0])] = c
    desc = {}
    for ln in open(os.path.join(rathena, 'doc', 'effect_list.md'), encoding='utf-8'):
        m = re.match(r'\|(\d+)\|(\w+)\|\s*(.*)\|\s*$', ln)
        if m: desc[int(m.group(1))] = (m.group(2), m.group(3).strip())
    rbline = {}
    for i, ln in enumerate(open(os.path.join(rb_src, 'DB', 'Effects', 'EffectTable.js'), encoding='utf-8', errors='replace')):
        m = re.match(r'\s*//\s*(\d+)\s*:\s*\[\{\}\]', ln)
        if m: rbline[int(m.group(1))] = i + 1
    ez2, _, _ = fc.ez2_map(g, aegis)
    et2 = gse.with_ez2(g, et)
    files = gse.Files(g, set())            # every effect file (the taken ones too: said so below)
    taken = gse.taken_files(et2, set(g.names()))
    classless = gse.classless_map(aegis)
    aegis_of_id = {}
    for k, v in aegis.items(): aegis_of_id.setdefault(v, k)
    _, _, log = gse.resolve(g, et2, gse.skills_with_data(se, ez2), aegis)
    amb = {s: d for s, r, d in log if r == 'C-ambiguous'}

    def path_of(f):
        return (gse.EFFECT_DIR + f[1] + '.str') if f[0] == 'STR' else (gse.SPRITE_DIR + f[1] + '.spr')

    def show(p):
        try: return p.encode('latin-1').decode('cp949')
        except UnicodeError: return p

    rows = []
    for ln in open(miss_path, encoding='utf-8'):
        c = ln.rstrip('\n').split('\t')
        if len(c) < 4 or not c[0].isdigit(): continue
        sid = int(c[0]); r = cov.get(sid)
        after2 = r[4] if r else '?'; src = r[6] if r and len(r) > 6 else '-'
        drawn = 'yes' if after2 == 'draws' else 'sound' if after2 == 'sound only' else 'body spin' if after2.startswith('body spin') else 'no'
        ev = []
        s = se.get(str(sid)) or {}
        for f in fc.FIELDS + ['groundEffectId']:
            v = s.get(f)
            for x in (v if isinstance(v, list) else [v] if v is not None else []):
                if isinstance(x, (int, float)):
                    x = int(x); d = desc.get(x)
                    has = str(x) in et
                    ev.append('rB %s %d%s%s%s' % (f, x, ' (%s: %s)' % d if d else '', '' if has else ' — no part in rB', (', placeholder EffectTable.js:%d' % rbline[x]) if not has and x in rbline else ''))
                elif isinstance(x, str): ev.append('rB %s %s%s' % (f, x, '' if x in et else ' — no part in rB'))
        if 'groundEffectId' in s: ev.append('rB groundEffectId: no rB code plays it')
        nm = gse.skill_names(aegis_of_id, sid, classless)
        cand = sorted(files.named(nm, sid, {k.lower(): v for k, v in aegis.items()}))
        if cand and drawn != 'yes':
            used = [f for f in cand if path_of(f) in taken]
            free = [f for f in cand if path_of(f) not in taken]
            why = 'rB / the official table give this skill its effect ids (the file is not tied to them by any source)' if (s and any(s.get(f) is not None for f in fc.FIELDS)) else \
                  'several files: order / moments unknown' if sid in amb or len(free) > 1 else \
                  'only its class-less name matches (generic file names)' if free and not files.named([nm[0]], sid, {k.lower(): v for k, v in aegis.items()}) else 'not drawable'
            if free: ev.append('GRF files named after it, not used (%s): %s' % (why, ', '.join(show(path_of(f)) for f in free[:6]) + (' …%d' % len(free) if len(free) > 6 else '')))
            if used: ev.append('GRF files named after it already used by rB / ez2 for another effect id: %s' % ', '.join(show(path_of(f)) for f in used[:4]))
        rows.append([str(sid), c[1], c[2], c[3], after2, drawn, src, '; '.join(ev) or '-'])
    with open(out, 'w', encoding='utf-8', newline='\n') as o:
        o.write('# id\taegis\tname\tafter w4fx\tafter w4fx2\tdrawn\tw4fx2 source\tevidence\n')
        for r in rows: o.write('\t'.join(r) + '\n')
    print('%d skills: drawn %d, sound %d, nothing %d -> %s' % (len(rows), sum(r[5] == 'yes' for r in rows), sum(r[5] == 'sound' for r in rows), sum(r[5] == 'no' for r in rows), out))


if __name__ == '__main__':
    main(sys.argv[1:])
