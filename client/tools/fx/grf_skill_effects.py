#!/usr/bin/env python3
r"""Client wave 4c (w4fx2): the skill effects the player's GRF itself gives beyond roBrowserLegacy's tables and the ez2 tables — the reference
for client/Assets/Loki/Runtime/Fx/GrfSkillEffects.cs (the client does the same at run time from the player's own GRF; nothing of it is
shipped). Used by fx_coverage.py (state "after w4fx2") and runnable alone for the per-skill report:

  python grf_skill_effects.py <data.grf> <data/fx dir> <skills.tsv> [--report out.tsv]

Sources, all in the player's GRF:
  O  the official client's own skill → effect table: data\luafiles514\lua files\skilleffectinfo\skilleffectinfolist.lub
     (SKILL_EFFECT_INFO_LIST[SKID.x] = { effectID = {EFID…}, targetEffectID, groundEffectID, toTargetEffectID, beginEffectID, waveFileName,
     targetWaveFileName, beginMotionType, onTarget, LaunchZC_USE_SKILL }), run after skillinfoz\skillid.lub (SKID), skilleffectinfo\
     effectid.lub (EFID) and actorstate.lub (ACTOR_STATE); its getters are skilleffectinfo_f.lub (GetEffectID / GetTargetEffectID / …).
     No reader documents the fields' moments (roBrowserLegacy does not read this file); Loki takes their names: targetEffectID and
     targetWaveFileName on the skill's target (rB's effectId moment), effectID and waveFileName on the caster (rB's effectIdOnCaster),
     beginEffectID as the cast aura (the aura rB draws from the cast packet's element, Entity.js:1846-1892). groundEffectID and
     toTargetEffectID are kept but never spawned (none of their ids has a drawable part in this GRF); beginMotionType (a body action)
     and onTarget are not used (their meaning is not documented anywhere found).
  N1 an effect id the official EFID table names (effectid.lub) and the effect table lacks: the GRF's one effect file named exactly
     after it (EF_FULLMOON_KICK → effect\fullmoon_kick\fullmoon_kick.str, EF_SOUL_REAPER → sprite\이팩트\soul_reaper\soul_reaper.spr).
  N2 an official entry with exactly one referenced effect id still without parts: the one effect file named after its skill (rule S).
  C  a skill with no effect id in rB, ez2 or the official table: the one effect file named after its FULL aegis name (rule S without the
     class-less form: the GRF's file names are generic words — miss, owl, aura — that class-less forms of passives hit), played as rB
     plays an ez2 entry without a suffix (effectId: on the skill's target, the caster for a self skill; DBManager.js mergeEz2Effects).
  S  (the name rule) the skill's aegis name and (N2 only) its name without the class prefix when exactly one skill has that remainder
     (the rule of Fx/Ez2Effects.cs Find); effect files: a file named so (any depth), and every effect file under a folder named so,
     except a file named after another skill or under another skill's folder (it belongs to that skill). The union must be exactly ONE
     file, else nothing is tied (the order / moments of several files are the client's code).
Effect files: data\texture\effect\**\*.str (STRM 0x94 with at least one texture where the renderer loads it, effect\<folder>\<name>;
.ezv alone is not drawn) and data\sprite\이팩트\**\*.spr with its .act. A file an rB / ez2 effect already loads is never taken.
Parts: STR {type STR, file, texturePath = the file's folder, attachedEntity} (the ez2 part, Ez2Effects.cs), SPR {type SPR, file,
attachedEntity}; sounds {wav, attachedEntity}.
"""
import json, os, re, struct, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'clientdata'))
import lub51  # noqa: E402

BS = chr(92)
LUA = 'data' + BS + 'luafiles514' + BS + 'lua files' + BS
LUBS = [LUA + 'skillinfoz' + BS + 'skillid.lub', LUA + 'skilleffectinfo' + BS + 'effectid.lub', LUA + 'skilleffectinfo' + BS + 'actorstate.lub',
        LUA + 'skilleffectinfo' + BS + 'skilleffectinfolist.lub']
EFFECT_DIR = 'data' + BS + 'texture' + BS + 'effect' + BS
SPRITE_DIR = 'data' + BS + 'sprite' + BS + '\xc0\xcc\xc6\xd1\xc6\xae' + BS  # 이팩트 (CP949 bytes as Latin-1, Sprites/RoNames.cs Effect)
FIELDS_TARGET = ('targetEffectID',)
FIELDS_CASTER = ('effectID',)


def _s(v): return v.decode('latin-1') if isinstance(v, bytes) else v


def _ids(t):
    """A Lua list {EFID…} → its numbers in index order."""
    if not isinstance(t, lub51.LuaTable): return []
    out = []
    for k, v in sorted(((k, v) for k, v in t.h.items() if isinstance(k, int)), key=lambda kv: kv[0]):
        if isinstance(v, (int, float)): out.append(int(v))
    return out


def official_table(g):
    """{skill id: {field: value}} from the GRF's skilleffectinfo lubs, the EFID table {name: id}; (None, None) when a file is missing."""
    for p in LUBS:
        if g.read(p) is None: return None, None
    lub51.GLOBALS.h.clear(); lub51._setup()
    for p in LUBS: lub51.run(g.read(p))
    lst = lub51.GLOBALS.get(b'SKILL_EFFECT_INFO_LIST'); efid = lub51.GLOBALS.get(b'EFID')
    table = {}
    for k, v in lst.h.items():
        if not isinstance(k, int) or not isinstance(v, lub51.LuaTable): continue
        e = {}
        for f in ('effectID', 'targetEffectID', 'groundEffectID', 'toTargetEffectID'):
            ids = _ids(v.get(f.encode()))
            if ids: e[f] = ids
        for f in ('beginEffectID', 'beginMotionType'):
            x = v.get(f.encode())
            if isinstance(x, (int, float)): e[f] = int(x)
        for f in ('waveFileName', 'targetWaveFileName'):
            x = v.get(f.encode())
            if isinstance(x, bytes) and x: e[f] = _s(x)
        for f in ('onTarget', 'LaunchZC_USE_SKILL'):
            x = v.get(f.encode())
            if x is not None: e[f] = bool(x)
        table[k] = e
    names = {_s(k): int(v) for k, v in efid.h.items() if isinstance(v, (int, float))}
    return table, names


class Files:
    """The GRF's effect files by base name and by folder (names as the GRF holds them: Latin-1, lower-case ASCII, backslashes); the files
    rB's table or an ez2 entry already loads (`taken`) are left out."""
    def __init__(self, g, taken):
        self.g = g; self.by_base = {}; self.by_folder = {}; self._ok = {}
        names = self.names = set(g.names())
        for n in names:
            if n in taken: continue
            if n.startswith(EFFECT_DIR) and n.endswith('.str'): kind, rel = 'STR', n[len(EFFECT_DIR):-4]
            elif n.startswith(SPRITE_DIR) and n.endswith('.spr') and (n[:-4] + '.act') in names: kind, rel = 'SPR', n[len(SPRITE_DIR):-4]
            else: continue
            f = (kind, rel)
            self.by_base.setdefault(rel.split(BS)[-1], set()).add(f)
            for seg in rel.split(BS)[:-1]: self.by_folder.setdefault(seg, set()).add(f)

    def drawable(self, f):
        if f in self._ok: return self._ok[f]
        kind, rel = f; ok = False
        if kind == 'SPR': ok = True
        else:
            b = self.g.read(EFFECT_DIR + rel + '.str')
            if b and b[:4] == b'STRM' and struct.unpack_from('<I', b, 4)[0] == 0x94:
                folder = rel.rsplit(BS, 1)[0] + BS if BS in rel else ''
                p = 36; layers = struct.unpack_from('<I', b, 16)[0]
                try:
                    for _ in range(layers):
                        nt = struct.unpack_from('<I', b, p)[0]; p += 4
                        for t in range(nt):
                            tex = norm(b[p:p + 128].split(b'\0')[0].decode('latin-1')); p += 128  # RoPath.Normalize (ASCII only)
                            if (EFFECT_DIR + folder + tex) in self.names: ok = True  # as the renderer loads it: effect\<texturePath><name>
                        nk = struct.unpack_from('<I', b, p)[0]; p += 4 + 124 * nk
                except struct.error: pass
        self._ok[f] = ok
        return ok

    def named(self, names, sid, skill_of_name):
        """Rule S: every effect file named after one of `names`, or under a folder named so — except a file named after another skill or
        under another skill's folder (skill_of_name: lower-case aegis name → id), which belongs to that skill."""
        out = set()
        for n in names:
            out |= self.by_base.get(n, set())
            out |= self.by_folder.get(n, set())
        return set(f for f in out if all(skill_of_name.get(seg, sid) == sid for seg in f[1].split(BS)))


def norm(p):
    """RoPath.Normalize: A-Z lower-cased, '/' to backslash."""
    return ''.join(chr(ord(c) + 32) if 'A' <= c <= 'Z' else (BS if c == '/' else c) for c in p)


def taken_files(et, names):
    r"""The GRF paths rB's table and the ez2 entries (in et after the ez2 merge) load: STR parts effect\<file>.str (a %d file: every
    number), SPR parts sprite\이팩트\<file>.spr, 3D / 2D spriteName (이팩트) and absoluteSpriteName."""
    t = set(); pats = []
    def add(path):
        path = norm(path)
        if '%d' in path: pats.append(re.compile(re.escape(path).replace('%d', r'\d+') + '$'))
        else: t.add(path)
    def walk(o):
        if isinstance(o, dict):
            ty = o.get('type')
            f = o.get('file')
            if isinstance(f, str) and f:
                if ty == 'STR': add(EFFECT_DIR + f + '.str')
                elif ty == 'SPR': add(SPRITE_DIR + f + '.spr')
            if isinstance(o.get('spriteName'), str) and o['spriteName']: add(SPRITE_DIR + o['spriteName'] + '.spr')
            if isinstance(o.get('absoluteSpriteName'), str) and o['absoluteSpriteName']: add(o['absoluteSpriteName'] + '.spr')
            for v in o.values(): walk(v)
        elif isinstance(o, list):
            for x in o: walk(x)
    walk(et)
    if pats: t |= set(n for n in names if any(p.match(n) for p in pats))
    return t


def part_of(f):
    kind, rel = f
    if kind == 'STR':
        folder = rel.rsplit(BS, 1)[0] + BS if BS in rel else ''
        return {'type': 'STR', 'file': rel.replace(BS, '/'), 'texturePath': folder, 'attachedEntity': True}
    return {'type': 'SPR', 'file': rel, 'attachedEntity': True}


def skill_names(aegis_of_id, sid, classless):
    a = aegis_of_id.get(sid)
    if not a: return []
    out = [a.lower()]
    u = a.find('_')
    if 0 < u < len(a) - 1 and classless.get(a[u + 1:]) == sid: out.append(a[u + 1:].lower())
    return out


def classless_map(aegis):
    c, seen = {}, set()
    for k, v in aegis.items():
        u = k.find('_')
        if u <= 0 or u == len(k) - 1: continue
        rest = k[u + 1:]
        if rest in seen:
            if c.get(rest) != v: c[rest] = -1
        else: seen.add(rest); c[rest] = v
    return c


def resolve(g, et, skill_ids_with_data, aegis):
    """Everything this module adds. et: the effect table (rB + ez2 keys); skill_ids_with_data: skills that have an effect id in rB or ez2;
    aegis: upper-case aegis name → id. Returns (effects {key: [parts]}, skills {sid: {field: [keys]}}, log [(sid, rule, detail)])."""
    table, efid = official_table(g)
    files = Files(g, taken_files(et, set(g.names())))
    classless = classless_map(aegis)
    aegis_of_id = {}
    for k, v in aegis.items(): aegis_of_id.setdefault(v, k)
    skill_of_name = {k.lower(): v for k, v in aegis.items()}
    effects, skills, log = {}, {}, []
    has = lambda key: key in et or key in effects
    if table is None: return effects, skills, [(0, 'O', 'skilleffectinfo lubs not in this GRF')]
    # N1: official EF names → files
    for name, eid in sorted(efid.items(), key=lambda x: x[1]):
        if has(str(eid)) or not name.startswith('EF_'): continue
        fs = sorted(f for f in files.by_base.get(name[3:].lower(), set()) if files.drawable(f))  # the file's own name, not a folder
        if len(fs) == 1:
            effects[str(eid)] = [part_of(fs[0])]
            log.append((0, 'N1', '%d %s -> %s %s' % (eid, name, fs[0][0], fs[0][1])))
        elif len(fs) > 1: log.append((0, 'N1-ambiguous', '%d %s -> %s' % (eid, name, fs)))
    # O + N2
    for sid in sorted(table):
        e = table[sid]; s = skills.setdefault(sid, {})
        refd = [i for f in FIELDS_TARGET + FIELDS_CASTER for i in e.get(f, [])]
        bare = sorted(set(i for i in refd if not has(str(i))))
        if len(bare) == 1:
            fs = sorted(f for f in files.named(skill_names(aegis_of_id, sid, classless), sid, skill_of_name) if files.drawable(f))
            if len(fs) == 1:
                effects[str(bare[0])] = [part_of(fs[0])]
                log.append((sid, 'N2', '%d <- %s %s' % (bare[0], fs[0][0], fs[0][1])))
        for i in e.get('targetEffectID', []): s.setdefault('effectId', []).append(str(i))
        for i in e.get('effectID', []): s.setdefault('effectIdOnCaster', []).append(str(i))
        for i in e.get('groundEffectID', []): s.setdefault('groundEffectId', []).append(str(i))
        if 'beginEffectID' in e: s['castAuraId'] = [str(e['beginEffectID'])]
        if sid not in skill_ids_with_data:  # the official sounds only where rB / ez2 give none (no double sound)
            if e.get('waveFileName'):
                k = 'sei_%d_wav' % sid; effects[k] = [{'wav': e['waveFileName'], 'attachedEntity': True}]
                s.setdefault('effectIdOnCaster', []).append(k)
            if e.get('targetWaveFileName'):
                k = 'sei_%d_twav' % sid; effects[k] = [{'wav': e['targetWaveFileName'], 'attachedEntity': True}]
                s.setdefault('effectId', []).append(k)
        log.append((sid, 'O', json.dumps(e, ensure_ascii=False)))
    # C: skills with no effect data at all
    for a, sid in sorted(aegis.items(), key=lambda x: x[1]):
        if sid in skill_ids_with_data or sid in table or aegis_of_id.get(sid) != a: continue
        fs = sorted(f for f in files.named([a.lower()], sid, skill_of_name) if files.drawable(f))
        if len(fs) == 1:
            k = 'grf_%s' % a.lower(); effects[k] = [part_of(fs[0])]
            skills.setdefault(sid, {})['effectId'] = [k]
            log.append((sid, 'C', '%s %s' % fs[0]))
        elif len(fs) > 1: log.append((sid, 'C-ambiguous', '%d files: %s' % (len(fs), ', '.join('%s %s' % f for f in fs[:6]))))
    return effects, skills, log


def with_ez2(g, et):
    """The effect table plus every ez2 entry's STR part (all of them, tied or not: Ez2Effects.Load adds each to EffectDb)."""
    import fx_coverage as fc
    et2 = dict(et)
    for path in (fc.BS.join(['data', 'contentdata', 'effectdata', 'ez2streffect.bson']), fc.BS.join(['data', 'effectdata', 'ez2streffect.bson'])):
        b = g.read(path)
        if not b: continue
        for name, e in fc.read_bson(b).items():
            fp = e.get('FilePath', '') if isinstance(e, dict) else ''
            et2.setdefault(name, {'entries': [{'type': 'STR', 'file': (fp[:-4] if fp.lower().endswith('.str') else fp).replace(BS, '/')}]})
    return et2


def skills_with_data(se, ez2):
    """Skills with an effect id in rB's SkillEffect (any field, groundEffectId too) or an ez2 entry (EffectDb.Skill(id) has an id list)."""
    import fx_coverage as fc
    return set(int(k) for k, v in se.items() if any(v.get(f) is not None and not isinstance(v.get(f), dict) for f in fc.FIELDS + ['groundEffectId'])) | set(ez2)


def main():
    grf_path, fx_dir, skills_tsv = sys.argv[1:4]
    report = sys.argv[sys.argv.index('--report') + 1] if '--report' in sys.argv else None
    sys.path.insert(0, os.path.join(HERE, '..', 'combat'))
    from grf import Grf
    import fx_coverage as fc
    g = Grf(grf_path)
    et = json.load(open(os.path.join(fx_dir, 'effect_table.json'), encoding='utf-8'))
    se = json.load(open(os.path.join(fx_dir, 'skill_effect.json'), encoding='utf-8'))
    aegis = fc.read_aegis(skills_tsv)
    ez2, _, _ = fc.ez2_map(g, aegis)
    et2 = with_ez2(g, et)
    with_data = skills_with_data(se, ez2)
    effects, skills, log = resolve(g, et2, with_data, aegis)
    print('effects added %d, skills touched %d' % (len(effects), len(skills)))
    for r in ('O', 'N1', 'N2', 'C', 'N1-ambiguous', 'C-ambiguous'): print(r, sum(1 for x in log if x[1] == r))
    if report:
        with open(report, 'w', encoding='utf-8', newline='\n') as o:
            for sid, rule, d in log: o.write('%d\t%s\t%s\n' % (sid, rule, d.encode('latin-1', 'replace').decode('cp949', 'replace') if isinstance(d, str) else d))
        print('report:', report)


if __name__ == '__main__':
    main()
