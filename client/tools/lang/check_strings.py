#!/usr/bin/env python3
"""Client wave 4b (w4lang): checks the string table against the code.

Every text the client writes itself is wrapped in Lang.T("English") / Lang.F("English {0}", ...) (or marked Lang.N("English") when it
is translated later from a variable). This lists:
  - texts of the code with no line in Resources/Lang/pt-BR.txt (they would show in English in Portuguese),
  - table lines no code uses any more,
  - placeholders ({0} {1:N0} ...) or rich-text tags that differ between the English and the Portuguese text,
  - Lang.T / Lang.F calls whose first argument is not one plain string literal (the table cannot be checked for them),
  - (with --unwrapped) string literals still passed to the UI unwrapped (a heuristic list to review by hand).
Usage: python check_strings.py [--unwrapped] [--quiet]   (from anywhere; paths are relative to this file)
Exit code 1 when a code text has no Portuguese line or a placeholder differs.
"""
import os, re, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
CLIENT = os.path.normpath(os.path.join(HERE, '..', '..'))
RUNTIME = os.path.join(CLIENT, 'Assets', 'Loki', 'Runtime')
TABLE = os.path.join(CLIENT, 'Assets', 'Loki', 'Resources', 'Lang', 'pt-BR.txt')


def cs_literal(s, i):
    """Parses the C# regular string literal starting at s[i] == '"'; returns (text, end index after the closing quote)."""
    assert s[i] == '"'
    out = []
    i += 1
    while i < len(s):
        c = s[i]
        if c == '"':
            return ''.join(out), i + 1
        if c == '\\':
            d = s[i + 1]
            if d == 'n': out.append('\n'); i += 2
            elif d == 't': out.append('\t'); i += 2
            elif d == 'r': out.append('\r'); i += 2
            elif d == '0': out.append('\0'); i += 2
            elif d == 'u': out.append(chr(int(s[i + 2:i + 6], 16))); i += 6
            elif d == 'x':
                j = i + 2
                while j < len(s) and j < i + 6 and s[j] in '0123456789abcdefABCDEF':
                    j += 1
                out.append(chr(int(s[i + 2:j], 16))); i = j
            else: out.append(d); i += 2
            continue
        if c == '\n':
            raise ValueError('newline in string literal')
        out.append(c)
        i += 1
    raise ValueError('unterminated string literal')


def table_unescape(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            d = s[i + 1]
            out.append({'n': '\n', 't': '\t', 'r': '\r'}.get(d, d))
            i += 2
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def esc(s):
    return s.replace('\\', '\\\\').replace('\n', '\\n').replace('\t', '\\t').replace('\r', '\\r')


CALL = re.compile(r'\bLang\.(T|F|N)\(\s*')


def line_comment_before(prefix):
    """True when the line's text before a call holds a // outside string and char literals ("file:///" is not a comment)."""
    i, n, q = 0, len(prefix), None
    while i < n:
        c = prefix[i]
        if q:
            if c == '\\':
                i += 2
                continue
            if c == q:
                q = None
        elif c in '"\'':
            q = c
        elif prefix.startswith('//', i):
            return True
        i += 1
    return False


def scan_code():
    keys = {}  # English -> [(file, line, kind)]
    bad = []
    dyn = []
    for root, _, files in os.walk(RUNTIME):
        for f in files:
            if not f.endswith('.cs'):
                continue
            p = os.path.join(root, f)
            rel = os.path.relpath(p, RUNTIME).replace('\\', '/')
            if rel == 'Lang/Lang.cs':
                continue
            s = open(p, encoding='utf-8-sig').read()
            for m in CALL.finditer(s):
                line = s.count('\n', 0, m.start()) + 1
                bol = s.rfind('\n', 0, m.start()) + 1
                if line_comment_before(s[bol:m.start()]):
                    continue  # in a comment
                j = m.end()
                if j < len(s) and s[j] == '"':
                    try:
                        text, k = cs_literal(s, j)
                    except Exception as e:
                        bad.append((rel, line, 'literal: %s' % e))
                        continue
                    rest = s[k:k + 3].lstrip()
                    if rest.startswith('+'):
                        bad.append((rel, line, 'a string built with + inside Lang.%s' % m.group(1)))
                        continue
                    keys.setdefault(text, []).append((rel, line, m.group(1)))
                else:
                    snippet = s[j:j + 60].split('\n')[0]
                    dyn.append((rel, line, 'Lang.%s(%s...)' % (m.group(1), snippet)))
    return keys, bad, dyn


def load_table():
    t = {}
    probs = []
    order = []
    if not os.path.exists(TABLE):
        return t, probs, order
    for n, raw in enumerate(open(TABLE, encoding='utf-8-sig').read().split('\n'), 1):
        line = raw.rstrip('\r')
        if not line or line.startswith('#'):
            continue
        if '\t' not in line:
            probs.append((n, 'no tab', line))
            continue
        en, pt = line.split('\t', 1)
        en = table_unescape(en)
        pt = table_unescape(pt)
        if en in t:
            if t[en][0] != pt:
                probs.append((n, 'repeated with another translation (line %d)' % t[en][1], en))
            else:
                probs.append((n, 'repeated (line %d)' % t[en][1], en))
            continue
        t[en] = (pt, n)
        order.append(en)
    return t, probs, order


PH = re.compile(r'\{(\d+)([,:][^}]*)?\}')
TAG = re.compile(r'</?(?:color|size|b|i)(?:=[^>]*)?>')


def placeholders(s):
    s2 = s.replace('{{', '').replace('}}', '')
    return sorted(m.group(0) for m in PH.finditer(s2))


def tags(s):
    return sorted(re.sub(r'=[^>]*', '=', m.group(0)) for m in TAG.finditer(s))


def load_extra(table, probs, order, path):
    """--with <file>: 'English ||| Portuguese' lines (a translator's batch not merged yet; \\s = a space)."""
    for n, raw in enumerate(open(path, encoding='utf-8-sig').read().split('\n'), 1):
        line = raw.rstrip('\r')
        if not line.strip() or line.startswith('#'):
            continue
        if ' ||| ' not in line:
            probs.append((n, 'no separator in ' + path, line))
            continue
        en, pt = line.split(' ||| ', 1)
        en = table_unescape(en.replace(r'\s', ' '))
        pt = table_unescape(pt.replace(r'\s', ' '))
        if en in table:
            if table[en][0] != pt:
                probs.append((n, '%s: already in the table with another translation "%s"' % (path, esc(table[en][0])), en))
            continue
        table[en] = (pt, n)
        order.append(en)


def main():
    keys, bad, dyn = scan_code()
    table, probs, order = load_table()
    args = sys.argv[1:]
    for i, a in enumerate(args):
        if a == '--with' and i + 1 < len(args):
            load_extra(table, probs, order, args[i + 1])
    missing = [k for k in keys if k not in table]
    unused = [k for k in order if k not in keys]
    ph_bad = []
    tag_bad = []
    empty = []
    for k, (pt, n) in table.items():
        if not pt.strip():
            empty.append((n, k))
            continue
        kinds = {x[2] for x in keys.get(k, [])}
        if 'F' in kinds or PH.search(k) or PH.search(pt):
            if placeholders(k) != placeholders(pt):
                ph_bad.append((n, k, pt))
        if tags(k) != tags(pt):
            tag_bad.append((n, k, pt))
        en_plain = k.split('~', 2)[2] if k.startswith('~') and k.count('~') >= 2 else k
        if (len(en_plain) - len(en_plain.lstrip(' ')), len(en_plain) - len(en_plain.rstrip(' '))) != (len(pt) - len(pt.lstrip(' ')), len(pt) - len(pt.rstrip(' '))):
            tag_bad.append((n, k, pt + '   <- leading / trailing spaces differ'))
    quiet = '--quiet' in sys.argv
    print('code texts: %d (T/F/N calls: %d); table lines: %d' % (len(keys), sum(len(v) for v in keys.values()), len(table)))
    print('missing in the table: %d' % len(missing))
    for k in sorted(missing, key=lambda k: keys[k][0]):
        f, l, kind = keys[k][0]
        print('  MISSING %s:%d %s "%s"' % (f, l, kind, esc(k)))
    print('placeholders differ: %d' % len(ph_bad))
    for n, k, pt in ph_bad:
        print('  PH line %d: "%s" | "%s"' % (n, esc(k), esc(pt)))
    print('rich-text tags differ: %d' % len(tag_bad))
    for n, k, pt in tag_bad:
        print('  TAG line %d: "%s" | "%s"' % (n, esc(k), esc(pt)))
    print('table problems (repeated / no tab / empty): %d' % (len(probs) + len(empty)))
    for d in probs:
        print('  TABLE line %d: %s: "%s"' % (d[0], d[1], esc(d[2])))
    for n, k in empty:
        print('  EMPTY line %d: "%s"' % (n, esc(k)))
    print('calls the table cannot check: %d' % len(bad))
    for f, l, why in bad:
        print('  CALL %s:%d %s' % (f, l, why))
    print('calls with a variable (their texts are marked with Lang.N where they are made): %d' % len(dyn))
    if not quiet:
        for f, l, what in dyn:
            print('  DYN %s:%d %s' % (f, l, what))
    print('table lines no code uses: %d' % len(unused))
    if not quiet:
        for k in unused:
            print('  UNUSED line %d "%s"' % (table[k][1], esc(k)))
    # wave 28 (wtextc), design note 2026-10-15, English only: the game shows the code's English texts (the Portuguese table stays, unused,
    # for an undo). Every code text must read as English: the words of the table's Portuguese side that its English side never has
    # (seen at least 3 times), or a word with a/o tilde or c cedilla, make a text Portuguese with 3 of them, 2 that are a fifth of its
    # words, or 1 that is half of a text of up to 4 words (the rule of LokiGame.W28TextSmoke.cs W28Pt).
    word = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ]+")
    en_w, pt_w = {}, {}
    for k, (pt, n) in table.items():
        for w in set(x.lower() for x in word.findall(k)): en_w[w] = en_w.get(w, 0) + 1
        for w in set(x.lower() for x in word.findall(pt)): pt_w[w] = pt_w.get(w, 0) + 1
    pt_only = {w for w, c in pt_w.items() if c >= 3 and w not in en_w and len(w) >= 3}
    def reads_pt(s):
        ws = [x.lower() for x in word.findall(s)]
        hits = [w for w in ws if w in pt_only or any(ch in w for ch in 'ãõç')]
        k, n = len(hits), len(ws)
        return (k >= 2 and k * 5 >= n) or k >= 3 or (k >= 1 and n <= 4 and k * 2 >= n)
    pt_code = [k for k in keys if reads_pt(k.split('~', 2)[2] if k.startswith('~') and k.count('~') >= 2 else k)]
    print('code texts that read as Portuguese (English only): %d' % len(pt_code))
    for k in pt_code:
        f, l, kind = keys[k][0]
        print('  PORTUGUESE %s:%d %s "%s"' % (f, l, kind, esc(k)))
    if '--unwrapped' in sys.argv:
        skip = set()
        for i, a in enumerate(sys.argv[1:]):
            if a == '--with':
                skip.add(i + 1)
        unwrapped([a for i, a in enumerate(sys.argv[1:]) if not a.startswith('--') and i not in skip])
    return 1 if missing or ph_bad or pt_code else 0


SINK = re.compile(r'(UiKit\.(Label|Button)\(|SetLabel\(|Toast\(|\bChat\(|Chat\.Add\(|SetTitle\(|SetInfo\(|SetFooter\(|SetStatus\(|SetArmed\(|'
                  r'ShowNotice\(|Text = |\bA = "|\bB = "|\.text = |Placeholder = |Say\(|Rows?\.Add\(|Message\?\.Invoke\(|Msg\(|Fail\(|Info\(|Line\()')
LIT = re.compile(r'\$?@?"((?:[^"\\]|\\.)*)"')


def unwrapped(only):
    print('\nUNWRAPPED (heuristic: a UI call with a literal holding a word, on a line without Lang.; smoke files skipped):')
    n = 0
    for root, _, files in os.walk(RUNTIME):
        for f in sorted(files):
            if not f.endswith('.cs') or 'Smoke' in f or f.endswith('.g.cs'):
                continue
            p = os.path.join(root, f)
            rel = os.path.relpath(p, RUNTIME).replace('\\', '/')
            if rel.startswith('Net/Stdb') or rel.startswith('Data/'):
                continue
            if only and not any(o in rel for o in only):
                continue
            for i, line in enumerate(open(p, encoding='utf-8-sig'), 1):
                t = line.strip()
                if t.startswith('//') or 'Debug.Log' in t or 'Lang.' in t:
                    continue
                if not SINK.search(t):
                    continue
                lits = [m.group(1) for m in LIT.finditer(t) if re.search(r'[A-Za-z]{2,}', m.group(1))]
                if lits:
                    n += 1
                    print('  %s:%d  %s' % (rel, i, t[:170]))
    print('unwrapped candidates: %d' % n)


if __name__ == '__main__':
    sys.exit(main())
