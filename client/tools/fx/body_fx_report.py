#!/usr/bin/env python3
"""Client wave 5 (w5looks): which skills now play one of roBrowserLegacy's body / quake / timed-sound FUNC parts (Fx/BodyFx.cs), and what
each skill's coverage was after w4fx2 (design notes).

  python body_fx_report.py <BodyFx.cs> <data/fx dir> <w4fx2-coverage.tsv> <out.tsv>

The FUNC parts Loki plays are read from Fx/BodyFx.cs's Table ("key#part" → kind; Q(...) = a camera quake); a skill plays one when an effect
key of its rB SkillEffect entry (skill_effect.json: effectId, effectIdOnCaster, hitEffectId, hitEffectIdOnCaster, beforeHitEffectId(OnCaster),
releaseEffectId, beginCastEffectId, successEffectId(OnCaster)) has that FUNC part at that index in effect_table.json. Effect ids rAthena sends
without a skill (@size's EF_BABYBODY / EF_GIANTBODY, a server's specialeffect) are listed as their own rows ("effect <key>").
Also lists every FUNC part of the effect table Loki still leaves (neither Fx/BodyFx.cs, Fx/SpinFx.cs, Fx/GroundFx.cs nor FuncFx draws it).
"""
import json, os, re, sys

FIELDS = ['effectId', 'effectIdOnCaster', 'hitEffectId', 'hitEffectIdOnCaster', 'beforeHitEffectId', 'beforeHitEffectIdOnCaster',
          'releaseEffectId', 'beginCastEffectId', 'successEffectId', 'successEffectIdOnCaster']
DRAWN_CLS = {'FlatColorTile', 'Tiles.HoveringTexture', 'LPEffect', 'SpiderWeb', 'PropertyGround', 'QuadHorn', 'MagicRing'}
SPIN_KEYS = {'501', '775', 'ef_moonslasher', 'ef_windmill'}  # Fx/SpinFx.cs
FUNC_KEYS = {'60', '513'}  # Fx/FxRenderers.cs FuncFx


def body_table(path):
    src = open(path, encoding='utf-8').read()
    t = {}
    for m in re.finditer(r'\{\s*"([^"#]+)#(\d+)",\s*new Spec \{ Kind = Kind\.(\w+)', src): t[(m.group(1), int(m.group(2)))] = m.group(3)
    for m in re.finditer(r'\{\s*"([^"#]+)#(\d+)",\s*Q\((\d+),\s*(\d+)', src): t[(m.group(1), int(m.group(2)))] = f'Quake (+{m.group(3)} ms, {"650" if m.group(4) == "0" else m.group(4)} ms)'
    return t


def keys_of(entry):
    ks = []
    for f in FIELDS:
        v = entry.get(f)
        if v is None or isinstance(v, dict): continue
        for x in (v if isinstance(v, list) else [v]):
            if isinstance(x, dict): continue
            ks.append((f, str(int(x)) if isinstance(x, (int, float)) else x))
    return ks


def main():
    bodyfx, fx_dir, cov_path, out = sys.argv[1:5]
    table = body_table(bodyfx)
    et = json.load(open(os.path.join(fx_dir, 'effect_table.json'), encoding='utf-8'))
    se = json.load(open(os.path.join(fx_dir, 'skill_effect.json'), encoding='utf-8'))
    cov = {}
    for ln in open(cov_path, encoding='utf-8'):
        c = ln.rstrip('\n').split('\t')
        if c and c[0].isdigit() and int(c[0]) not in cov: cov[int(c[0])] = (c[1], c[4] if len(c) > 4 else '')  # the skills come first; the ground units (same numbers) follow
    rows = []
    for sid in sorted(se, key=lambda s: int(s)):
        e = se[sid]; got = []
        for f, k in keys_of(e):
            for i, p in enumerate((et.get(k) or {}).get('entries', [])):
                if p.get('type') == 'FUNC' and (k, i) in table: got.append(f'{f} {k}#{i} {table[(k, i)]}')
        if got:
            name, state = cov.get(int(sid), (e.get('name') or '', ''))
            rows.append((sid, name, state, '; '.join(got)))
    skill_keys = {k for e in se.values() for _, k in keys_of(e)}
    for (k, i), kind in sorted(table.items(), key=lambda x: (x[0][0], x[0][1])):
        if k not in skill_keys: rows.append((f'effect {k}', (et.get(k) or {}).get('name') or '', '-', f'{k}#{i} {kind}'))
    left = []
    for k, v in et.items():
        for i, p in enumerate(v.get('entries', [])):
            if p.get('type') != 'FUNC' or (k, i) in table or k in SPIN_KEYS or k in FUNC_KEYS: continue
            if any(isinstance(c, dict) and isinstance(c.get('add'), dict) and c['add'].get('__cls') in DRAWN_CLS for c in p.get('calls') or []): continue
            calls = sorted({(c.get('call') or c.get('set') or (c.get('add') or {}).get('__cls') or 'error').split('(')[0] for c in p.get('calls') or [] if isinstance(c, dict)})
            left.append((k, v.get('name') or '', i, ', '.join(calls) or 'nothing'))
    with open(out, 'w', encoding='utf-8', newline='\n') as o:
        o.write('# skill id\taegis\tafter w4fx2 (w4fx2-coverage.tsv)\tthe body / quake / sound FUNC parts it now plays (Fx/BodyFx.cs)\n')
        for r in rows: o.write('\t'.join(r) + '\n')
        o.write('# FUNC parts still left: key\tname\tpart\twhat the FUNC calls\n')
        for k, n, i, c in left: o.write(f'left {k}\t{n}\t{i}\t{c}\n')
    skills = [r for r in rows if not r[0].startswith('effect')]
    newly = [r for r in skills if not r[2].startswith('draws')]
    print(f'{len(table)} FUNC parts in Fx/BodyFx.cs; {len(skills)} skills play one ({len(newly)} of them drew nothing after w4fx2); '
          f'{len(rows) - len(skills)} effect ids without a skill; {len(left)} FUNC parts left -> {out}')


if __name__ == '__main__':
    main()
