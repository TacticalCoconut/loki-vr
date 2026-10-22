#!/usr/bin/env python3
r"""
Builds the client's message and map-name tables (Game/ClientText.cs) from the RO client's own text files in the GRF
(client wave 3b, w3data; the tool that made the earlier copies was never committed, so this one defines them anew):
  <out>/msgstrings.tsv   id <TAB> English <TAB> Portuguese          from data\msgstringtable_ml.csv
  <out>/mapnames.tsv     map <TAB> English <TAB> Portuguese         from data\english\mapnametable.txt and data\mapnametable.txt

  python build_texttables.py [<data.grf>] <out dir>
      default GRF: C:\Ragnarok Online\data.grf (the LATAM install of 2026-09-30, GRF 0x300)

msgstringtable_ml.csv (the LATAM client's multi-language table): one message per line, comma-separated base64 columns
(column 2 English, column 7 Portuguese; 0 the MSI_ name, 1 Korean, 9 Spanish). As roBrowserLegacy reads a base64 table
(DB/DBManager.js loadCSV:4164-4210: lines split on \r?\n, empty and '//' lines skipped, the message id counting the lines kept),
the id of a line is the number of kept lines before it: rAthena's ZC_MSG ids are 0-based (ClientText.cs). A column the line
lacks is ''. Values are UTF-8 (UTF-8, then windows-1252, then Latin-1, as the other tools decode), tabs and line breaks become
spaces (the file keeps \n as the two characters '\' 'n'; ClientText turns those into line breaks).
mapnametable.txt: "<map>.rsw#<name>#" records; read as roBrowserLegacy's loadTable does (DB/DBManager.js:4123-4153: '//'
lines dropped, the text split on '#', the pieces paired and trimmed); a later record of the same map wins. Keys are lower-case
without ".rsw"; every map of either file gets a row (sorted), '' where a file has none.
"""
import base64, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402

MSG = 'data\\msgstringtable_ml.csv'
MAP_EN, MAP_PT = 'data\\english\\mapnametable.txt', 'data\\mapnametable.txt'


def dec(b):
    for e in ('utf-8', 'cp1252'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')


def clean(s):
    return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ')


def js_trim(s):
    r"""String.replace(/^\s+|\s+$/g, ''): JavaScript's \s (WhiteSpace + LineTerminator)."""
    return re.sub(r'^[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+|[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+$', '', s)


def read(g, path):
    data = g.read(path)
    if data is None: raise SystemExit('missing in the GRF: ' + path)
    return data


def map_table(data):
    text = dec(data)
    content = re.sub(r'\n(//[^\n]+)', '', '\n' + text)  # rB: ('\n' + data).replace(/\n(\/\/[^\n]+)/g, '')
    el = content.split('#')
    out = {}
    # rB's loop hands a pair (el[s], el[s + 1]) to its callback when the next pair starts (i = s + 2 exists), so the last
    # piece after the final '#' closes the last record and an unpaired tail is dropped
    for s in range(0, len(el) - 2, 2):
        k = js_trim(el[s]).lower()
        if k.endswith('.rsw'): k = k[:-4]
        out[k] = clean(js_trim(el[s + 1]))
    return out


def main(argv):
    if len(argv) == 1: grf_path, out = r'C:\Ragnarok Online\data.grf', argv[0]
    elif len(argv) == 2: grf_path, out = argv
    else: print(__doc__); return 2
    g = grfmod.Grf(grf_path)
    os.makedirs(out, exist_ok=True)
    # ---- msgstrings.tsv
    rows = ['# id\tenglish\tportuguese  (built by client/tools/clientdata/build_texttables.py from %s (GRF 0x%x): '
            'data\\msgstringtable_ml.csv base64 columns 2 and 7; the id is the 0-based line number)' % (os.path.basename(grf_path), g.version)]
    n = 0
    for line in re.split(rb'\r?\n', read(g, MSG)):
        line = line.strip()
        if not line or line.startswith(b'//'): continue
        c = line.split(b',')
        cols = [clean(dec(base64.b64decode(c[k].strip(), validate=True))).replace('\u200b', '') if k < len(c) else '' for k in (2, 7)]  # wave 28: zero-width spaces out
        rows.append('\t'.join([str(n)] + cols)); n += 1
    with open(os.path.join(out, 'msgstrings.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(rows) + '\n')
    # ---- mapnames.tsv
    en, pt = map_table(read(g, MAP_EN)), map_table(read(g, MAP_PT))
    mrows = ['# map\tenglish\tportuguese  (built by client/tools/clientdata/build_texttables.py from %s (GRF 0x%x): '
             'data\\english\\mapnametable.txt and data\\mapnametable.txt)' % (os.path.basename(grf_path), g.version)]
    for m in sorted(set(en) | set(pt)):
        mrows.append('\t'.join([m, en.get(m, ''), pt.get(m, '')]))
    with open(os.path.join(out, 'mapnames.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(mrows) + '\n')
    print('msgstrings.tsv: %d messages; mapnames.tsv: %d maps (%d English, %d Portuguese)' % (n, len(mrows) - 1, len(en), len(pt)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
