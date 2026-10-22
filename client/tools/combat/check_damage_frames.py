#!/usr/bin/env python3
"""Checks the client wave 3 damage-frame rule (Game/HitSchedule.cs DamageFrameMs) against rAthena's own numbers.

rAthena documents how the official client times a monster's normal-attack damage (doc/mob_db.txt:207-215; clif.cpp:5247-5255):
the damage number and the target's flinch show on the attack animation's damage frame, which is the ACT frame whose sound is
"atk" (else the second-to-last frame), one ACT delay unit being 24 ms, the animation sped up by 432 / srcSpeed when srcSpeed < 432.
rAthena's mob_db.yml carries that time at 1x speed as ClientAttackMotion (e.g. Drops: frame 12 x 24 ms = 288).
This tool computes the same time from the GRF's monster ACT files (attack = action group 2, direction 0) and compares it with
every ClientAttackMotion in db/re/mob_db.yml.
Usage: python check_damage_frames.py <data.grf> <StreamingAssets/data/spritenames.txt> <rathena>/db/re/mob_db.yml [out.txt]"""
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'emotes'))
from grf import Grf, BACKSLASH as BS
from act_dump import parse

MONSTER = '\xb8\xf3\xbd\xba\xc5\xcd'  # 몬스터 (CP949 bytes as Latin-1), Sprites/RoNames.cs Monster


def damage_frame_ms(actions, events, group=2, direction=0):
    """First 'atk' frame (else frames - 2) x delay x 24 ms, at 1x speed; None when the action has no frames."""
    i = group * 8 + direction
    if i >= len(actions): i = min(direction, len(actions) - 1)
    a = actions[i]
    n = len(a['frames'])
    if n == 0: return None, None
    atk = [k for k, f in enumerate(a['frames']) if 0 <= f['event'] < len(events) and events[f['event']] == 'atk']
    frame = atk[0] if atk else max(n - 2, 0)
    return frame * max(a['delay'], 1.0) * 24.0, ('atk' if atk else 'second-to-last')


def main(grf_path, names_path, mob_db, out=None):
    names = {}
    for line in open(names_path, encoding='latin-1'):
        f = line.rstrip('\n').split('\t')
        if len(f) >= 3 and f[0] == 'npc': names[int(f[1])] = f[2]
    mobs = []; cur = None
    for line in open(mob_db, encoding='utf-8'):
        m = re.match(r'^  - Id: (\d+)', line)
        if m: cur = {'id': int(m.group(1))}; mobs.append(cur); continue
        m = re.match(r'^    (AegisName|ClientAttackMotion|AttackMotion): (\S+)', line)
        if m and cur is not None: cur[m.group(1)] = m.group(2)
    g = Grf(grf_path)
    rows = []; same = diff = nosprite = 0
    for mob in mobs:
        if 'ClientAttackMotion' not in mob: continue
        want = int(mob['ClientAttackMotion'])
        name = names.get(mob['id'])
        data = g.read(BS.join(['data', 'sprite', MONSTER, name.lower() + '.act'])) if name else None
        if data is None: nosprite += 1; rows.append('%6d %-24s CAM %5d  no sprite (%s)' % (mob['id'], mob.get('AegisName', ''), want, name)); continue
        ver, actions, events, exact = parse(data)
        ms, how = damage_frame_ms(actions, events)
        ok = ms is not None and abs(ms - want) < 1.0
        same += ok; diff += not ok
        rows.append('%6d %-24s CAM %5d  ACT %7s (%s)%s' % (mob['id'], mob.get('AegisName', ''), want, 'n/a' if ms is None else '%.0f' % ms, how, '' if ok else '   DIFFERS'))
    summary = 'monsters with ClientAttackMotion: %d; ACT damage frame equal: %d, different: %d, no sprite: %d' % (same + diff + nosprite, same, diff, nosprite)
    print(summary)
    if out:
        with open(out, 'w', encoding='utf-8') as o: o.write(summary + '\n' + '\n'.join(r.encode('latin-1', 'replace').decode('cp949', 'replace') for r in rows) + '\n')


if __name__ == '__main__':
    main(*sys.argv[1:5])
