#!/usr/bin/env python3
"""
Builds Assets/StreamingAssets/data/skills.tsv and efst.tsv from the client's English skill/state tables.
Inputs: skills_raw.tsv, efst_raw.tsv from `lua5.1 dumpskills.lua skills_raw.tsv efst_raw.tsv` (run next to the
64-bit-converted lubs: skillinfoz/skillid.lub, english/.../skillinfolist.lub + skilldescript.lub,
stateicon/efstids.lub, english/.../stateiconinfo.lub, stateicon/stateiconimginfo.lub).
skills.tsv: id, aegisName, name, maxLevel, spPerLevel(csv), description(\\n-joined, ^RRGGBB codes kept)
efst.tsv:   id, constName, title, kind(buff/debuff/toggle/'')
"""
import sys, os
def dec(h):
    b = bytes.fromhex(h)
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')
def clean(s): return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ')
skills_raw, efst_raw, outdir = sys.argv[1:4]
with open(os.path.join(outdir, 'skills.tsv'), 'w', encoding='utf-8', newline='\n') as o:
    n = 0
    for line in open(skills_raw, encoding='ascii'):
        c = line.rstrip('\n').split('\t')
        desc = '\\n'.join(clean(dec(x)) for x in c[5].split(',') if x) if len(c) > 5 else ''
        o.write('\t'.join([c[0], c[1], clean(dec(c[2])), c[3], c[4], desc]) + '\n'); n += 1
with open(os.path.join(outdir, 'efst.tsv'), 'w', encoding='utf-8', newline='\n') as o:
    m = 0
    for line in open(efst_raw, encoding='ascii'):
        c = line.rstrip('\n').split('\t')
        o.write('\t'.join([c[0], c[1], clean(dec(c[2])), c[3]]) + '\n'); m += 1
print('skills', n, 'efst', m)
