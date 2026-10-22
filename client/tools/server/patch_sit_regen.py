#!/usr/bin/env python3
"""
Project Loki server patch: faster HP/SP recovery while sitting.
Adds battle config `loki_sit_regen_rate` (default 1 = unchanged). When a player is sitting (view data dead_sit == 2),
every natural HP/SP recovery tick heals `loki_sit_regen_rate` times its normal amount (skill-based regen such as
Increase HP Recovery is not multiplied). Tick timing is left untouched (the 500 ms natural-heal timer caps frequency, so the
multiplier is applied to the amount). Idempotent: running it twice changes nothing.
Usage: patch_sit_regen.py <rathena root>
"""
import sys, os

root = sys.argv[1]
def patch(rel, old, new, marker):
    p = os.path.join(root, rel)
    s = open(p, encoding='utf-8', newline='').read()
    if marker in s: print('already patched:', rel); return
    if s.count(old) != 1: raise SystemExit(f'{rel}: anchor found {s.count(old)} times, refusing')
    s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('patched:', rel)

nl = '\r\n' if b'\r\n' in open(os.path.join(root, 'src/map/battle.hpp'), 'rb').read() else '\n'
def L(*lines): return nl.join(lines)

patch('src/map/battle.hpp',
      L('\tint32 natural_healhp_interval;'),
      L('\tint32 natural_healhp_interval;', '\tint32 loki_sit_regen_rate; // Project Loki: natural regen multiplier while sitting'),
      'loki_sit_regen_rate')

patch('src/map/battle.cpp',
      L('\t{ "natural_heal_skill_interval",        &battle_config.natural_heal_skill_interval,     10000,  NATURAL_HEAL_INTERVAL, INT_MAX, },'),
      L('\t{ "natural_heal_skill_interval",        &battle_config.natural_heal_skill_interval,     10000,  NATURAL_HEAL_INTERVAL, INT_MAX, },',
        '\t{ "loki_sit_regen_rate",                &battle_config.loki_sit_regen_rate,             1,      1,      1000,           }, // Project Loki'),
      'loki_sit_regen_rate')

# status.cpp: compute the multiplier once, then scale the amounts.
patch('src/map/status.cpp',
      L('\tif (flag&(RGN_HP|RGN_SP)) {', '\t\tif(!vd)', '\t\t\tvd = status_get_viewdata(bl);', '\t\tif(vd && vd->dead_sit == 2)', '\t\t\tmulti += 1; //This causes the interval to be halved'),
      L('\t// Project Loki: sitting players recover loki_sit_regen_rate times the normal amount per tick',
        '\tint64 loki_sitmul = 1;',
        '\tif (sd) {',
        '\t\tstruct view_data* loki_vd = vd ? vd : status_get_viewdata(bl);',
        '\t\tif (loki_vd && loki_vd->dead_sit == 2)',
        '\t\t\tloki_sitmul = battle_config.loki_sit_regen_rate;',
        '\t}',
        '',
        '\tif (flag&(RGN_HP|RGN_SP)) {', '\t\tif(!vd)', '\t\t\tvd = status_get_viewdata(bl);', '\t\tif(vd && vd->dead_sit == 2)', '\t\t\tmulti += 1; //This causes the interval to be halved'),
      'loki_sitmul = 1')
patch('src/map/status.cpp',
      L('\t\t\telse if (status_heal(bl, regen->hp, 0, 1) < regen->hp)'),
      L('\t\t\telse if (status_heal(bl, regen->hp * loki_sitmul, 0, 1) < regen->hp * loki_sitmul)'),
      'regen->hp * loki_sitmul')
patch('src/map/status.cpp',
      L('\t\t\telse if (status_heal(bl, 0, regen->sp, 1) < regen->sp)'),
      L('\t\t\telse if (status_heal(bl, 0, regen->sp * loki_sitmul, 1) < regen->sp * loki_sitmul)'),
      'regen->sp * loki_sitmul')
