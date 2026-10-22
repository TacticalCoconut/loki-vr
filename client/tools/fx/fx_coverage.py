#!/usr/bin/env python3
"""Skill-effect coverage of the Loki client (client wave 4, w4fx): which skills draw an effect, which only play a sound, which have
nothing in the client data, and why — before w4fx, after w4fx and after w4fx2 (client wave 4c), by the renderer rules of each state
(Fx/EffectManager.cs Drawable).

  python fx_coverage.py <rathena> <data.grf> <data/fx dir> <skills.tsv> [--list out.tsv] [--own loki_own_fx.txt]

--own (client wave 5, w5fxown): a fourth state "after w5fxown" that counts LOKI'S OWN effects (a design, not the official look:
client/Assets/Loki/Resources/Fx/loki_own_fx.txt, Fx/OwnSkillFx.cs) SEPARATELY from the sourced ones: a skill whose sourced effect draws
keeps it ("draws"); a skill of the own table without one draws Loki's own ("own effect"); its recipe's GRF files must be in the GRF
(else that part falls back to the code-drawn template, which always draws).

Universe: every skill of rAthena's db/re/skill_db.yml (id, name, Unit Id / AlternateId). A skill "has an effect in the client data" when
  - roBrowserLegacy's SkillEffect.js lists effect ids for it (skill_effect.json; groundEffectId excluded: no rB code plays it), or
  - one of its ground units maps to an effect (skill_unit.json, rB SkillUnit.js), or
  - (after) an entry of the GRF's ez2streffect.bson is tied to it by the exact rules of Fx/Ez2Effects.cs (aegis names from skills.tsv).
It "draws" when one of those effects has a part the client renders whose file is in the GRF (STR, texture, sprite, RSM model):
  before: STR CYLINDER 3D 2D SPR QuadHorn TRAIL, FUNC only EF_LOCKON 60 / EF_GROUNDSAMPLE 513;
  after (w4fx): + RSM (trap models) + FUNC parts whose recorded calls draw (FlatColorTile, HoveringTexture, LPEffect, SpiderWeb,
          PropertyGround, QuadHorn, MagicRing: Fx/GroundFx.cs) + the ez2 STRs;
  after w4fx2: + the GRF's official skilleffectinfolist.lub and the effect files named after skills / effect ids (grf_skill_effects.py =
          Fx/GrfSkillEffects.cs: the target / caster effect ids and sounds it ties; its cast aura id is not counted) + Auto Counter's
          EF_AUTOCOUNTER from rB's code (Engine/MapEngine/Entity.js:1774-1783, 1911-1922; Game/CombatFx.cs).
Otherwise the reason: "sound only" (the effects have only wav parts), "FUNC <what it does>" (camera quake, weather, body animation, spirit
spheres … not drawn), "file missing" (a drawable part whose file this GRF lacks), or "no effect in the client data".
"""
import json, os, re, struct, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'combat'))
from grf import Grf  # noqa: E402

BS = chr(92)
EFFECT_SPR = 'data' + BS + 'sprite' + BS + 'ÀÌÆÑÆ®' + BS  # 이팩트 as CP949 bytes (Sprites/RoNames.cs Effect)
FIELDS = ['effectId', 'effectIdOnCaster', 'hitEffectId', 'hitEffectIdOnCaster', 'beforeHitEffectId', 'beforeHitEffectIdOnCaster',
          'releaseEffectId', 'beginCastEffectId', 'successEffectId', 'successEffectIdOnCaster']
BEFORE = {'STR', 'CYLINDER', '3D', '2D', 'SPR', 'QuadHorn', 'TRAIL'}
AFTER = BEFORE | {'RSM'}
FUNC_KEYS = {'60', '513'}
DRAWN_CLS = {'FlatColorTile', 'Tiles.HoveringTexture', 'LPEffect', 'SpiderWeb', 'PropertyGround', 'QuadHorn', 'MagicRing'}


def norm(p):
    return ''.join(chr(ord(c) + 32) if 'A' <= c <= 'Z' else (BS if c == '/' else c) for c in p)


def skills_of_rathena(root):
    out = {}; cur = None; in_unit = False
    for ln in open(os.path.join(root, 'db', 're', 'skill_db.yml'), encoding='utf-8'):
        m = re.match(r'^  - Id: (\d+)', ln)
        if m: cur = int(m.group(1)); out[cur] = {'name': None, 'units': []}; in_unit = False; continue
        m = re.match(r'^    Name: (\w+)', ln)
        if m and cur is not None: out[cur]['name'] = m.group(1)
        if re.match(r'^    Unit:', ln): in_unit = True; continue
        if in_unit and re.match(r'^    \S', ln): in_unit = False
        m = re.match(r'^      (Id|AlternateId): (\w+)', ln)
        if in_unit and m and cur is not None: out[cur]['units'].append('UNT_' + m.group(2).upper())
    return out


def unit_ids(root):
    src = open(os.path.join(root, 'src', 'map', 'skill.hpp'), encoding='utf-8', errors='replace').read()
    i = src.index('enum e_skill_unit_id :'); j = src.index('};', i)
    units = {}; cur = -1
    for line in src[i:j].split('\n')[1:]:
        line = line.split('//')[0].strip().rstrip(',')
        m = re.match(r'(UNT_\w+)\s*(=\s*(\w+))?', line)
        if not m: continue
        v = m.group(3)
        cur = (int(v, 0) if re.match(r'^(0x)?[0-9a-fA-F]+$', v) else units[v]) if v else cur + 1
        units[m.group(1)] = cur
    return units


def read_bson(b):
    def doc(p):
        size = struct.unpack_from('<i', b, p)[0]; end = p + size; p += 4; o = {}
        while p < end - 1:
            t = b[p]; p += 1; z = b.index(0, p); name = b[p:z].decode('utf-8', 'replace'); p = z + 1
            if t == 1: v = struct.unpack_from('<d', b, p)[0]; p += 8
            elif t == 2: n = struct.unpack_from('<i', b, p)[0]; v = b[p + 4:p + 3 + n].decode('utf-8', 'replace'); p += 4 + n
            elif t in (3, 4): v, p = doc(p); v = list(v.values()) if t == 4 else v
            elif t == 8: v = b[p] != 0; p += 1
            elif t == 10: v = None
            elif t == 16: v = struct.unpack_from('<i', b, p)[0]; p += 4
            elif t == 18: v = struct.unpack_from('<q', b, p)[0]; p += 8
            else: raise ValueError('bson type %d' % t)
            o[name] = v
        return o, end
    out = {}; p = 0
    while p < len(b):
        d, p = doc(p)
        if len(d) == 1: d = list(d.values())[0]
        out.update(d)
    return out


SUFFIX = [('hit', 'hit'), ('hitsub', 'hit'), ('target', 'hit'), ('cast', 'effect'), ('cast_bottom', 'effect'), ('single', 'effect'),
          ('bottom', 'effect'), ('begin', 'effect'), ('start', 'effect'), ('end', 'effect'), ('loop', 'effect'), ('buff', 'effect'),
          ('aura', 'effect'), ('main', 'effect'), ('sync', 'effect')]


def ez2_map(g, aegis):
    """The exact rules of Fx/Ez2Effects.cs: {skill id: [(entry, field, part)]}, unmapped entry names."""
    entries = {}
    for path in ('data' + BS + 'contentdata' + BS + 'effectdata' + BS + 'ez2streffect.bson', 'data' + BS + 'effectdata' + BS + 'ez2streffect.bson'):
        b = g.read(path)
        if b: entries.update(read_bson(b))
    meta = {}; skill_of = {}
    classless, seen = {}, set()
    for k, v in aegis.items():
        u = k.find('_')
        if u <= 0 or u == len(k) - 1: continue
        rest = k[u + 1:]
        if rest in seen:
            if classless.get(rest) != v: classless[rest] = -1
        else: seen.add(rest); classless[rest] = v

    def find(n):
        n = n.upper()
        if n in aegis: return aegis[n]
        c = classless.get(n, 0)
        return c if c > 0 else 0
    for name, e in entries.items():
        base, suffixed, field = name, False, 'ground' if (e.get('IsFloor') or e.get('IsToGround')) else 'effect'
        for s, f in SUFFIX:
            if name.endswith('_' + s): base, suffixed, field = name[:-len(s) - 1], True, f; break
        if name == 'broken_limit_buff': field = 'effect'
        if name == 'abyss_flame_target': field = 'hit'
        meta[name] = (base, suffixed, field)
        sid = find(base)
        if not sid:
            for seg in e.get('FilePath', '').split(BS):
                sid = find(seg)
                if sid: break
        if not sid and e.get('SoundPath'):
            snd = e['SoundPath'].split(BS)[-1]
            if snd.lower().endswith('.wav'): snd = snd[:-4]
            sid = find(snd)
        if sid: skill_of[name] = sid
    for name in entries:
        if name not in skill_of and meta[name][1] and meta[name][0] in skill_of: skill_of[name] = skill_of[meta[name][0]]
    out = {}; unmapped = []
    for name, e in entries.items():
        fp = e.get('FilePath', '')
        part = {'type': 'STR', 'file': (fp[:-4] if fp.lower().endswith('.str') else fp).replace(BS, '/'), 'wav': e.get('SoundPath') or None}
        if name not in skill_of: unmapped.append(name); continue
        out.setdefault(skill_of[name], []).append((name, meta[name][2], part))
    return out, unmapped, len(entries)


class Checker:
    def __init__(self, g): self.names = set(g.names())
    def has(self, p): return norm(p) in self.names
    def tex(self, p):
        p = p.replace('/', BS)
        return self.has(p if norm(p).startswith('data' + BS) else 'data' + BS + 'texture' + BS + p)

    def part_file_ok(self, p):
        t = p.get('type', '')
        if t == 'STR':
            f = p.get('file', '')
            if '%d' in f:
                r = p.get('rand') or [1, 1]
                return any(self.has('data/texture/effect/' + f.replace('%d', str(i)) + '.str') for i in range(int(r[0]), int(r[1]) + 1))
            return self.has('data/texture/effect/' + f + '.str')
        if t == 'CYLINDER': return self.tex('effect/' + p.get('textureName', '') + '.tga')
        if t in ('3D', '2D'):
            if p.get('fileList'): return any(self.tex(x) for x in p['fileList'])
            if p.get('file'): return self.tex(p['file'])
            if p.get('shadowTexture'): return True
            if p.get('absoluteSpriteName'): return self.has(p['absoluteSpriteName'] + '.spr')
            if p.get('spriteName'): return self.has(EFFECT_SPR + p['spriteName'] + '.spr')
            return False
        if t == 'SPR': return self.has(EFFECT_SPR + p.get('file', '') + '.spr')
        if t == 'QuadHorn': return self.tex(p.get('textureFile', ''))
        if t == 'TRAIL': return any(self.part_file_ok(x) for x in (p.get('spawn') or []))
        if t == 'RSM': return self.has('data/model/' + p.get('file', '') + '.rsm')
        if t == 'FUNC':
            for c in p.get('calls') or []:
                a = c.get('add') if isinstance(c, dict) else None
                if not isinstance(a, dict) or a.get('__cls') not in DRAWN_CLS: continue
                cls, args, fac = a['__cls'], a.get('__args') or [], a.get('__factory') or []
                if cls == 'FlatColorTile': return True
                if cls == 'Tiles.HoveringTexture' and fac and self.tex(fac[0]): return True
                if cls == 'LPEffect' and self.tex('effect/aaa copy.bmp'): return True
                if cls == 'SpiderWeb' and self.tex('effect/spiderweb.tga'): return True
                if cls in ('PropertyGround', 'MagicRing') and len(args) > 4 and self.tex('effect/' + str(args[4]) + '.tga'): return True
                if cls == 'QuadHorn' and args and isinstance(args[0], dict) and self.tex(args[0].get('textureFile', '')): return True
            return True  # EF_LOCKON / EF_GROUNDSAMPLE: lockon128.tga / magic_target.tga, checked by the smoke
        return False


def func_kind(p):
    calls = p.get('calls') or []
    what = set()
    for c in calls:
        if not isinstance(c, dict): continue
        if 'add' in c and isinstance(c['add'], dict): what.add(c['add'].get('__cls', '?'))
        elif 'call' in c: what.add(c['call'].split('(')[0])
        elif 'set' in c: what.add('set ' + c['set'])
        elif 'error' in c: what.add('rB error')
    return ', '.join(sorted(what)) or 'nothing'


def read_aegis(skills_tsv):
    """skills.tsv (the client's skill table): upper-case aegis name → skill id, the first row of a name wins."""
    aegis = {}
    for ln in open(skills_tsv, encoding='utf-8'):
        c = ln.split('\t')
        if len(c) > 1 and c[0].isdigit() and c[1] and c[1].upper() not in aegis: aegis[c[1].upper()] = int(c[0])
    return aegis


def main():
    root, grf_path, fx_dir, skills_tsv = sys.argv[1:5]
    list_out = sys.argv[sys.argv.index('--list') + 1] if '--list' in sys.argv else None
    own_tsv = sys.argv[sys.argv.index('--own') + 1] if '--own' in sys.argv else None
    g = Grf(grf_path); chk = Checker(g)
    et = json.load(open(os.path.join(fx_dir, 'effect_table.json'), encoding='utf-8'))
    se = json.load(open(os.path.join(fx_dir, 'skill_effect.json'), encoding='utf-8'))
    su = json.load(open(os.path.join(fx_dir, 'skill_unit.json'), encoding='utf-8'))
    unt = unit_ids(root)
    skills = skills_of_rathena(root)
    aegis = read_aegis(skills_tsv)
    ez2, ez2_unmapped, ez2_total = ez2_map(g, aegis)

    def keys_rb(sid):
        s = se.get(str(sid)) or {}
        ks = []
        for f in FIELDS:
            v = s.get(f)
            if v is None or isinstance(v, dict): continue
            for x in (v if isinstance(v, list) else [v]):
                if isinstance(x, dict): continue
                ks.append(str(int(x)) if isinstance(x, (int, float)) else x)
        return ks

    def keys_unit(sid):
        ks = []
        for n in skills[sid]['units']:
            e = su.get(str(unt.get(n)))
            if e and e.get('effectId') not in (None, -1, '-1'): ks.append(str(e['effectId']))
        return ks

    # client wave 4c (w4fx2): the GRF's official skill effect table and the effect files named after skills / effect ids (grf_skill_effects.py
    # = Fx/GrfSkillEffects.cs), and Auto Counter's EF_AUTOCOUNTER 131 from rB's code (Entity.js:1774-1783, 1911-1922; Game/CombatFx.cs)
    import grf_skill_effects as gse
    gse_effects, gse_skills, gse_log = gse.resolve(g, gse.with_ez2(g, et), gse.skills_with_data(se, ez2), aegis)
    code_paths = {61: ['131']}
    # the rB FUNCs that spin the owner's body, played by Fx/SpinFx.cs (EffectTable.js:10203-10233, 13497-13517, 15423-15446, 15602-15625):
    # a body animation, not a drawn part — counted apart ("body spin")
    SPIN_KEYS = {'501', '775', 'ef_moonslasher', 'ef_windmill'}

    def judge(sid, state):
        after = state != 'before'
        parts = []  # (key, part)
        for k in keys_rb(sid) + keys_unit(sid):
            for p in (et.get(k) or {}).get('entries', []): parts.append((k, p))
        if after:
            for name, field, p in ez2.get(sid, []):
                if field != 'ground': parts.append((name, p))
        extra = []
        if state == 'after2':
            gs = gse_skills.get(sid, {})
            extra = gs.get('effectId', []) + gs.get('effectIdOnCaster', []) + code_paths.get(sid, [])
            for k in extra:
                for p in gse_effects.get(k) or (et.get(k) or {}).get('entries', []): parts.append((k, p))
        if not parts:
            if after and ez2.get(sid) and not (keys_rb(sid) or keys_unit(sid) or extra): return 'ez2 floor entries only (rB never plays them)', []
            has_keys = bool(keys_rb(sid) or keys_unit(sid) or extra)
            return 'no effect in the client data' if not has_keys else 'ids without effect table entries', []
        ok = AFTER if after else BEFORE
        drawn, missing, funcs, sounds, spins = [], [], [], 0, []
        for k, p in parts:
            t = p.get('type', '')
            draws = t in ok or (t == 'FUNC' and (k in FUNC_KEYS or (after and any(isinstance(c, dict) and isinstance(c.get('add'), dict) and c['add'].get('__cls') in DRAWN_CLS for c in p.get('calls') or []))))
            if draws:
                if chk.part_file_ok(p): drawn.append(k)
                else: missing.append(k)
            elif t == 'FUNC' and state == 'after2' and k in SPIN_KEYS: spins.append(k)
            elif t == 'FUNC': funcs.append(func_kind(p))
            elif t == '': sounds += 1
            else: funcs.append('type ' + t)
        if drawn: return 'draws', sorted(set(drawn))
        if missing: return 'file missing', sorted(set(missing))
        if spins: return 'body spin (rB FUNC, Fx/SpinFx.cs)', sorted(set(spins))
        if funcs: return 'FUNC not drawn: ' + '; '.join(sorted(set(funcs))), []
        return 'sound only', []

    def source_of(sid):
        """The w4fx2 source(s) of a skill's effect: the official table, an EF-named file (N1), a skill-named file (N2 / C), rB's code."""
        rules = set(r for s2, r, d in gse_log if s2 == sid and r in ('O', 'N2', 'C'))
        gs = gse_skills.get(sid, {})
        n1 = [k for k in gs.get('effectId', []) + gs.get('effectIdOnCaster', []) if any(r == 'N1' and d.startswith(k + ' ') for s2, r, d in gse_log)]
        out = (['official skilleffectinfolist'] if 'O' in rules else []) + (['EF-named file ' + ','.join(n1)] if n1 else []) + \
              (['skill-named file (N2)'] if 'N2' in rules else []) + (['skill-named file (C)'] if 'C' in rules else []) + \
              (['rB code (Entity.js)'] if sid in code_paths else []) + \
              (['rB FUNC body spin (EffectTable.js)'] if sid in rows and len(rows[sid]) > 4 and rows[sid][4].startswith('body spin') else [])
        return '; '.join(out) or '-'

    rows = {}
    for state in ('before', 'after', 'after2'):
        cnt = {}
        for sid in sorted(skills):
            why, keys = judge(sid, state)
            b = 'draws' if why == 'draws' else 'sound only' if why == 'sound only' else 'no effect in the client data' if why.startswith('no effect') else why.split(':')[0]
            cnt[b] = cnt.get(b, 0) + 1
            if state != 'before': rows.setdefault(sid, [sid, skills[sid]['name']]).extend([why, ' '.join(keys)])
        with_data = len(skills) - cnt.get('no effect in the client data', 0)
        label = {'before': 'before', 'after': 'after w4fx', 'after2': 'after w4fx2'}[state]
        print(f'{label}: {len(skills)} rAthena skills, {with_data} with an effect in the client data, {cnt.get("draws", 0)} draw; ' +
              ', '.join(f'{k} {v}' for k, v in sorted(cnt.items()) if k != 'draws'))
    own = {}
    if own_tsv:
        for ln in open(own_tsv, encoding='utf-8'):
            c = ln.rstrip('\n').split('\t')
            if c[0] != 'skill': continue
            files = [f.split(':') for f in c[15].split(';') if f]
            present = sum(1 for f in files if chk.part_file_ok({'type': f[2], 'file': f[3]}))
            own[int(c[1])] = (c[4], c[5], len(files), present)
        cnt = {}; srcd = 0; ownd = 0
        for sid in sorted(skills):
            why = rows[sid][4] if sid in rows and len(rows[sid]) > 4 else judge(sid, 'after2')[0]
            if why == 'draws': srcd += 1; b = 'draws'
            elif sid in own: ownd += 1; b = "Loki's own effect"
            else: b = 'sound only' if why == 'sound only' else 'no effect in the client data' if why.startswith('no effect') else why.split(':')[0]
            cnt[b] = cnt.get(b, 0) + 1
            if sid in rows:
                if sid in own and why != 'draws':
                    r = own[sid]
                    rows[sid].append('own: ' + r[0] + ('(' + r[1] + ')' if r[0] == 'grf' else '') + (' files %d/%d' % (r[3], r[2]) if r[2] else ''))
                else: rows[sid].append('-')
        print(f'after w5fxown: {len(skills)} rAthena skills: {srcd} draw a sourced effect, {ownd} draw LOKI\'S OWN effect (a design), ' +
              ', '.join(f'{k} {v}' for k, v in sorted(cnt.items()) if k not in ('draws', "Loki's own effect")) +
              f'; own table {len(own)} skills, {sum(1 for v in own.values() if v[2])} with GRF files ({sum(v[3] for v in own.values())} of {sum(v[2] for v in own.values())} files present)')
    print('w4fx2 (grf_skill_effects.py): ' + ', '.join('%s %d' % (r, sum(1 for x in gse_log if x[1] == r)) for r in ('O', 'N1', 'N2', 'C', 'N1-ambiguous', 'C-ambiguous')) +
          '; effects added %d' % len(gse_effects))
    print(f'ez2: {ez2_total} entries, {ez2_total - len(ez2_unmapped)} tied to {len(ez2)} skills by exact names; not tied: {", ".join(ez2_unmapped)}')
    # effect keys referenced by skills / units whose drawable part needs a file this GRF lacks
    refd = set()
    for sid in skills: refd.update(keys_rb(sid)); refd.update(keys_unit(sid))
    for e in su.values():
        if e.get('effectId') not in (None, -1, '-1'): refd.add(str(e['effectId']))
    miss = []
    for k in sorted(refd):
        for p in (et.get(k) or {}).get('entries', []):
            t = p.get('type', '')
            if (t in AFTER or t == 'FUNC') and not chk.part_file_ok(p): miss.append(f'{k}:{t}')
    print(f'effect keys whose drawable part lacks its file in this GRF: {len(miss)} [{", ".join(miss)}]')
    # ground units: every UNT id rAthena uses
    used = {}
    for sid, s in skills.items():
        for n in s['units']: used.setdefault(n, []).append(s['name'])
    code = ''
    for dp, dn, fn in os.walk(os.path.join(root, 'src', 'map')):
        for f in fn:
            if f.endswith('.cpp'): code += open(os.path.join(dp, f), encoding='utf-8', errors='replace').read()
    for n in set(re.findall(r'\b(UNT_[A-Z0-9_]+)\b', code)):
        if n in unt: used.setdefault(n, []).append('(code)')
    ucnt = {'effect': 0, 'ring': 0, 'none (UNT_DUMMYSKILL)': 0, 'text (graffiti)': 0}
    urows = []
    for n in sorted(used, key=lambda x: unt[x]):
        uid = unt[n]
        e = su.get(str(uid)); key = str(e['effectId']) if e and e.get('effectId') not in (None, -1, '-1') else None
        parts = (et.get(key) or {}).get('entries', []) if key else []
        draws = any((p.get('type') in AFTER or (p.get('type') == 'FUNC' and (key in FUNC_KEYS or any(isinstance(c, dict) and isinstance(c.get('add'), dict) and c['add'].get('__cls') in DRAWN_CLS for c in p.get('calls') or [])))) and chk.part_file_ok(p) for p in parts)
        kind = 'none (UNT_DUMMYSKILL)' if n == 'UNT_DUMMYSKILL' else 'text (graffiti)' if n == 'UNT_GRAFFITI' else 'effect' if draws else 'ring'
        ucnt[kind] += 1
        urows.append((uid, n, kind, key or '-', ','.join(sorted(set(used[n])))[:80]))
    print(f'ground units rAthena uses: {len(used)}: ' + ', '.join(f'{k} {v}' for k, v in ucnt.items()))
    if list_out:
        with open(list_out, 'w', encoding='utf-8', newline='\n') as o:
            o.write('# skill id\taegis\tafter w4fx\teffect keys drawn\tafter w4fx2\teffect keys drawn\tw4fx2 source' + ("\tafter w5fxown (Loki's own: a design)" if own_tsv else '') + '\n')
            for sid in sorted(rows):
                base = rows[sid][:6] if own_tsv else rows[sid]
                o.write('\t'.join(str(x) for x in base) + '\t' + source_of(sid) + ('\t' + str(rows[sid][6]) if own_tsv and len(rows[sid]) > 6 else '') + '\n')
            o.write('# unit id\tUNT\tdrawn as\teffect key\tskills\n')
            for r in urows: o.write('%d\t%s\t%s\t%s\t%s\n' % r)
        print('list:', list_out)


if __name__ == '__main__':
    main()
