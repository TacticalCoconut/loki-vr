#!/usr/bin/env python3
"""Builds the client wave 3 combat data files (w3combat) from roBrowserLegacy's tables (rb_combat_tables.mjs) and checks them
against the installed client data (the GRF):
  <data>/combat_sounds.json      weaponHit / jobHit / weaponSwing wav lists (Audio/WeaponSounds.cs)
  <data>/fx/mob_attack_fx.json   projectile / spawn effect key per monster class, and `sprites`: effect key -> sprite path
                                 relative to data\\sprite\\<effect folder> where rB's path does not exist in this GRF (Game/HitSchedule.cs
                                 MobAttackFx applies it to the effect table's part at load)
Every wav must exist under data\\wav\\ in the GRF and every effect key in <data>/fx/effect_table.json (made by tools/fx); the tool
prints what it checked and exits 1 on a missing wav or effect key.
rB names the monster attack sprites '../npc/<name>' (relative to the effect sprite folder, EffectTable.js). This LATAM GRF keeps
three of them in data\\sprite\\npc and the others in data\\sprite\\<monster folder>: the tool looks a missing one up by file name
in those two folders (npc first, as rB) and records where it is. Nothing is guessed: a sprite found in neither folder stays as rB
wrote it and is reported.
Usage: python build_combat_data.py <rb_combat.json> <data.grf> <StreamingAssets/data>"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grf import Grf, BACKSLASH as BS

MONSTER = '\xb8\xf3\xbd\xba\xc5\xcd'  # 몬스터 (CP949 bytes as Latin-1), Sprites/RoNames.cs Monster
SPRITE_KEYS = ('spriteName', 'file', 'absoluteSpriteName')


def u(s): return s.encode('latin-1', 'replace').decode('cp949', 'replace')


def main(rb_json, grf_path, data_dir):
    rb = json.load(open(rb_json, encoding='utf-8'))
    g = Grf(grf_path)
    bad = 0
    # ---- sounds
    wavs = set()
    for k in ('weaponHit', 'jobHit', 'weaponSwing'):
        for v in rb[k].values(): wavs.update(v)
    missing = [w for w in sorted(wavs) if g.read(BS.join(['data', 'wav', w])) is None]
    print('wav files named by the tables: %d, missing in the GRF: %d %s' % (len(wavs), len(missing), [u(w) for w in missing]))
    bad += len(missing)
    sounds = {'source': 'roBrowserLegacy src/DB/Items/WeaponHitSoundTable.js, DB/Jobs/JobHitSoundTable.js, DB/Items/WeaponSoundTable.js '
                        '(tools/combat/rb_combat_tables.mjs + build_combat_data.py)',
              'weaponHit': rb['weaponHit'], 'jobHit': rb['jobHit'], 'weaponSwing': rb['weaponSwing']}
    # ---- monster attack effects
    table = json.load(open(os.path.join(data_dir, 'fx', 'effect_table.json'), encoding='utf-8'))
    keys = sorted(set(rb['projectile'].values()) | set(rb['spawn'].values()))
    sprites = {}
    for key in keys:
        e = table.get(key)
        if e is None: print('effect key missing from effect_table.json:', key); bad += 1; continue
        for part in e['entries']:
            for sk in SPRITE_KEYS:
                p = part.get(sk)
                if not isinstance(p, str): continue
                if sk == 'absoluteSpriteName': where = p.replace('/', BS)
                elif p.startswith('../'): where = BS.join(['data', 'sprite'] + p[3:].split('/'))
                else: continue  # a plain effect-folder name: not one of the monster attack sprites
                if g.read(where + '.spr') is not None:
                    print('%-30s %-18s %s: found' % (key, sk, p)); continue
                name = p.split('/')[-1]
                found = None
                for folder in ('npc', MONSTER):
                    cand = BS.join(['data', 'sprite', folder, name])
                    if g.read(cand + '.spr') is not None and g.read(cand + '.act') is not None: found = folder; break
                if found is None: print('%-30s %-18s %s: NOT FOUND in npc or the monster folder' % (key, sk, p)); continue
                sprites[key] = {'key': sk, 'path': '../' + found + '/' + name}
                print('%-30s %-18s %s: not in the GRF; found as %s' % (key, sk, p, u(sprites[key]['path'])))
    fx = {'source': 'roBrowserLegacy src/DB/Monsters/AttackEffectTable.js (tools/combat/rb_combat_tables.mjs + build_combat_data.py)',
          'projectile': rb['projectile'], 'spawn': rb['spawn'], 'sprites': sprites}
    with open(os.path.join(data_dir, 'combat_sounds.json'), 'w', encoding='utf-8') as o: json.dump(sounds, o, indent=1)
    with open(os.path.join(data_dir, 'fx', 'mob_attack_fx.json'), 'w', encoding='utf-8') as o: json.dump(fx, o, indent=1)
    print('wrote combat_sounds.json (%d weapon types, %d jobs, %d swing types) and fx/mob_attack_fx.json (%d projectile + %d spawn classes, %d sprite paths fixed)'
          % (len(rb['weaponHit']), len(rb['jobHit']), len(rb['weaponSwing']), len(rb['projectile']), len(rb['spawn']), len(sprites)))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(*sys.argv[1:4]))
