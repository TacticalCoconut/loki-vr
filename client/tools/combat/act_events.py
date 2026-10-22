#!/usr/bin/env python3
"""Surveys the frame events of the ACT files in a GRF: which event names exist ('atk', '*.wav', ...), in how many files,
and on which action groups / frames. Answers the re-audit's open question (docs/progress/2026-09-29/
audit-vr-client-rathena.md §2g / §7): do the shipped ACT files carry the 'atk' swing event that roBrowserLegacy maps to
the weapon's swing sound (rB src/Renderer/Entity/EntitySound.js:57-63)?
The ACT layout is the one of client/Assets/Loki/Runtime/Formats/ActFile.cs (tools/emotes/act_dump.py, same order);
encrypted entries are decrypted like GrfArchive.Read (grf.py).
Usage: python act_events.py <data.grf> <out.txt> [folder prefix under data\\sprite, CP949 as \\xNN escapes]
Writes UTF-8 (names converted from CP949) and prints the summary."""
import os, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'emotes'))
from grf import Grf, BACKSLASH
from act_dump import parse


def u(name): return name.encode('latin-1').decode('cp949', 'replace')


def main(path, out_path, prefix=None):
    g = Grf(path)
    root = BACKSLASH.join(['data', 'sprite']) + (BACKSLASH + prefix if prefix else '')
    acts = sorted(n for n in g.names() if n.endswith('.act') and n.startswith(root))
    files_with = collections.Counter(); frames_of = collections.defaultdict(collections.Counter)
    atk_files = []; bad = []
    for n in acts:
        try:
            ver, actions, events, exact = parse(g.read(n))
        except Exception as ex:
            bad.append((n, str(ex))); continue
        used = collections.Counter()
        for ai, a in enumerate(actions):
            for fi, f in enumerate(a['frames']):
                if 0 <= f['event'] < len(events): used[(events[f['event']], ai // 8, fi)] += 1
        for e in {k[0] for k in used}: files_with[e] += 1
        for k, c in used.items(): frames_of[k[0]][(k[1], k[2])] += c
        atk = sorted({(k[1], k[2]) for k in used if k[0] == 'atk'})
        if atk: atk_files.append((n, atk))
    with open(out_path, 'w', encoding='utf-8') as o:
        o.write('ACT files under %s: %d, unparsable %d\n' % (u(root), len(acts), len(bad)))
        o.write("files whose frames reference 'atk': %d\n\n" % len(atk_files))
        for e, c in files_with.most_common():
            where = ', '.join('group %d frame %d: %d' % (k[0], k[1], v) for k, v in frames_of[e].most_common(6))
            o.write('%6d files  %r   [%s]\n' % (c, u(e), where))
        o.write('\n-- files with atk (action group, frame) --\n')
        for n, atk in atk_files: o.write('%s  %s\n' % (u(n), ' '.join('g%d/f%d' % k for k in atk)))
        for n, why in bad: o.write('UNPARSABLE %s: %s\n' % (u(n), why))
    print('ACT files %d, unparsable %d, with atk %d -> %s' % (len(acts), len(bad), len(atk_files), out_path))


if __name__ == '__main__':
    pre = sys.argv[3].encode('latin-1').decode('unicode_escape') if len(sys.argv) > 3 else None
    main(sys.argv[1], sys.argv[2], pre)
