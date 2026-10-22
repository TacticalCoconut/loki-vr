# blackscan.py RUN [RUN ...]: the in-game picture form of K10 (warch2): for every before/after pair a light pass took
# (C:\LokiVR\logs\shot_<run>_<map>_before|after_<view>.png), the share of near-black pixels (every channel <= 12) before
# and after; a pair whose black share grew by 15 points or more is printed (a black wall or slab the fix put in the view: dicastes01,
# mjolnir_04). Prints "map view before after" per flagged pair, then a count.
import sys, os, glob, re
from PIL import Image
import numpy as np
LOGS = r'C:\LokiVR\logs'
flag = 0; pairs = 0
for run in sys.argv[1:]:
    pat = re.compile(r'^shot_' + re.escape(run) + r'_(.+?)_before_(.+)\.png$')
    for p in sorted(glob.glob(os.path.join(LOGS, 'shot_' + run + '_*_before_*.png'))):
        m = pat.match(os.path.basename(p))
        if not m: continue
        mp, name = m.group(1), m.group(2)
        a = os.path.join(LOGS, f'shot_{run}_{mp}_after_{name}.png')
        if not os.path.exists(a): continue
        pairs += 1
        sh = []
        for f in (p, a):
            x = np.asarray(Image.open(f).convert('RGB'))
            sh.append(float((x.max(axis=2) <= 12).mean()))
        if sh[1] - sh[0] >= 0.15:
            flag += 1
            print(f'{run}\t{mp}\t{name}\t{sh[0]*100:.1f}\t{sh[1]*100:.1f}')
print(f'# {pairs} pairs, {flag} with the black share up 15 points or more')
