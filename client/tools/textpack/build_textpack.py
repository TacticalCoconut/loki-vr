#!/usr/bin/env python3
r"""
Wave 28 (wtextc): builds the TEXT PACK — the folder of rewritten game texts the client reads at run time from outside the build
(Assets/Loki/Runtime/Data/TextPack.cs) — from the 2026-10-15 text overhaul's tables, and checks every row of it against the
player's own first-run tables (the ones Data/ClientDataBuilder.cs makes from the install; the w3data harness makes the same files:
client/tools/w3data/harness "build").

  python build_textpack.py <overhaul client-data\tables> <overhaul client-csharp-and-account\account> <player's first-run tables>
                           <out pack folder> <report folder> [--fixes F] [--by-text F] [--name NAME] [--version VERSION]
  (--fixes / --by-text default to english_fixes.tsv / english_by_text.tsv next to this file)

The pack holds only fields that differ from the player's data and pass every check (a pack field replaces the player's; an empty
one keeps it). Since the design's 2026-10-15 decision the game is English only: the English fields are what players see; the Portuguese
ones are kept (checked) so the choice can be undone.

Checks, per field (a failed field is left out and listed in <report>\rejects.tsv with its reason):
  key        the row's id / map / (world, map) is in the player's table (and the skill's aegis / the status's EFST name match)
  empty      a name, title or label is empty
  encoding   U+FFFD, a control character, a zero-width space, a C1 character, CP949 bytes read as Windows-1252 (mojibake), "#NOME?"
  portuguese an English field whose words are Portuguese (the detector below)
  english-copy  a Portuguese field that is the English text copied
  colours    a "^" not followed by six hex digits where the player's text had none broken
  printf     a message's %d / %s / ... differ from the player's English (or Portuguese) message
  cut        a description or summary under half the player's (the player's over 60 characters)
  floors / monster-label / renamed   world map labels: two floors of one dungeon made the same, a field's monster label replaced by
             the map's name, a label rewritten beyond a spelling fix (similarity under 0.8 or other numbers)
The Portuguese detector: the words of the player's Portuguese columns that are not words of its English columns (seen at least 3
times there, and English only when the English columns hold it at least twice and at least a tenth as often), plus any word with ã,
õ or ç; a text is Portuguese with 3 such words, or 2 that are a fifth of its words, or 1 that is half of a text of up to 4 words,
unless every hit is in ALLOW (proper names the data shares between the languages: <report>\residual_pt.tsv lists what is left).
After the merge, <report>\residual_pt.tsv lists every English text a player would see that still reads as Portuguese, and
<report>\gaps.tsv every Portuguese text with no English at all; english_fixes.tsv (Loki's own English for those, "table<TAB>key
<TAB>field<TAB>English") is applied last. The manifest textpack.json lists every file with its SHA-256 and size, other tools'
files in the folder too (the Codex's codex/*.tsv: wcodex's build_codex_pack.py --out <pack folder>, then this tool or):

  python build_textpack.py --manifest <pack folder>      re-hashes every file of the folder into textpack.json (name, version
                                                         and row counts kept), after another tool added or changed its files
"""
import difflib, hashlib, json, os, re, sys, io, collections

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
WORD = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ]+")
COLOR = re.compile(r"\^[0-9a-fA-F]{6}")
BADCOLOR = re.compile(r"\^(?![0-9a-fA-F]{6})")
PRINTF = re.compile(r"%(?:%|[-+#0]*\d*(?:\.\d+)?(?:hh|h|ll|l|I64)?[diuxXfcs])")
DIGITS = re.compile(r"\d+")
# proper names and game words the data shares between the languages (reviewed by hand on the residual list, 2026-10-15)
ALLOW = set('''
aloe vera sol lex divina tempo down aqua ortus para zenhai bonito gemma fez othila uruz wyrd cauda daemonica mora mandar
dano carga lula kaupe kaizel kaite eske suffragium offertorium vituperatum reparatio praefatio benedictum flamen candidus canto
fidus animus judex dilectio sob com kis swt carnaval boas dono terrian babayaga pura mccurdy shawn chique usrc procure silvestre vitatas luc tomwa roborings morroc nifflheim glastheim berkana raido
lantana makulrata celatom scoforia lumen mahou ira infernal vida planta clava bougainvillea doe alma mate vertical
'''.split())


def tsv(path):
    rows = []
    for line in open(path, encoding='utf-8').read().split('\n'):
        if line and not line.startswith('#'): rows.append(line.split('\t'))
    return rows


def words(s):
    return [w.lower() for w in WORD.findall(COLOR.sub(' ', s.replace('\\n', ' ')))]


class Detector:
    def __init__(self):
        self.en, self.pt = collections.Counter(), collections.Counter()

    def add_en(self, s, weight=1):
        for w in set(words(s)): self.en[w] += weight

    def add_pt(self, s):
        for w in set(words(s)): self.pt[w] += 1

    def freeze(self):
        english = {w for w, n in self.en.items() if n >= 2 and n * 10 >= self.pt[w]}
        self.ptonly = {w for w, n in self.pt.items() if n >= 3 and w not in english and len(w) >= 3}

    def hits(self, s):
        ws = words(s)
        # a word with ã / õ / ç of two letters or more (a lone "ç" is a keyboard key, not a Portuguese text; LokiGame.W28TextSmoke.cs W28Pt the same)
        h = [w for w in ws if (w in self.ptonly or (len(w) >= 2 and any(ch in w for ch in 'ãõç'))) and w not in ALLOW]
        return h, len(ws)

    def is_pt(self, s):
        h, n = self.hits(s)
        k = len(h)
        return (k >= 2 and k * 5 >= n) or k >= 3 or (k >= 1 and n <= 4 and k * 2 >= n)


def mojibake(s):
    """CP949 bytes read as Windows-1252: two or more Latin-1 letters / signs that read back as Hangul (a typographic quote or
    dash alone is not)."""
    if sum(1 for ch in s if '\u00a1' <= ch <= '\u00ff') < 2: return False
    try: b = s.encode('cp1252')
    except UnicodeEncodeError: return False
    try: k = b.decode('cp949')
    except UnicodeDecodeError: return False
    return sum('\uac00' <= ch <= '\ud7a3' for ch in k) >= 1 and not any(ch in s for ch in 'ãõçáéíóúâêôàÁÉÍÓÚÃÕÇ')


def bad_encoding(s):
    for ch in s:
        if ch == '\uFFFD': return 'U+FFFD'
        if ch == '\u200b': return 'zero-width space'
        if ord(ch) < 0x20: return 'control character U+%04X' % ord(ch)
        if 0x80 <= ord(ch) < 0xa0: return 'C1 character U+%04X' % ord(ch)
        if '\uac00' <= ch <= '\ud7a3' or '\u3131' <= ch <= '\u318e': return 'Hangul'
    if '#NOME?' in s: return '#NOME? (a spreadsheet error)'
    if mojibake(s): return 'CP949 read as Windows-1252'
    return None


def printf_ok(new, en, pt):
    """A message keeps every placeholder of the player's (English or Portuguese) message: none lost, no new kind."""
    n = collections.Counter(PRINTF.findall(new))
    for ref in (en, pt):
        r = collections.Counter(PRINTF.findall(ref))
        if all(n[x] >= c for x, c in r.items()) and all(x in r or x in ('%s', '%d') for x in n): return True
    return False


class Report:
    def __init__(self):
        self.rejects = []
        self.stats = collections.defaultdict(collections.Counter)

    def reject(self, table, key, field, why, value):
        self.rejects.append((table, key, field, why, value[:160].replace('\t', ' ')))
        self.stats[table]['rejected ' + field + ': ' + why.split(' (')[0]] += 1

    def count(self, table, what, n=1):
        self.stats[table][what] += n


class Builder:
    def __init__(self, export, account, player, fixes):
        self.X, self.A, self.P = export, account, player
        self.R = Report()
        self.det = Detector()
        self.fixes = collections.defaultdict(dict)
        if fixes and os.path.exists(fixes):
            for r in tsv(fixes):
                if len(r) >= 5 and r[3] == '~': self.fixes[r[0]][(r[1], r[2])] = ('~', r[4])
                elif len(r) >= 4: self.fixes[r[0]][(r[1], r[2])] = r[3]
        self.load_player()
        self.out = {}

    # ------------------------------------------------------------------ the player's tables and the detector
    def load_player(self):
        P, d = self.P, self.det
        self.items = {r[0]: r + [''] * (10 - len(r)) for r in tsv(os.path.join(P, 'items.tsv'))}
        self.quests = {r[0]: r + [''] * (8 - len(r)) for r in tsv(os.path.join(P, 'quests.tsv'))}
        self.skills = {r[0]: r + [''] * (8 - len(r)) for r in tsv(os.path.join(P, 'skills.tsv'))}
        self.efst = {r[0]: r + [''] * (5 - len(r)) for r in tsv(os.path.join(P, 'efst.tsv'))}
        self.msgs = {r[0]: r for r in tsv(os.path.join(P, 'msgstrings.tsv'))}
        self.maps = {r[0].lower(): r for r in tsv(os.path.join(P, 'mapnames.tsv'))}
        self.world = tsv(os.path.join(P, 'worldmap.tsv'))
        self.nav = tsv(os.path.join(P, 'navigation.tsv'))
        self.ach = {lang: json.load(open(os.path.join(P, 'account', 'achievements_%s.json' % lang), encoding='utf-8')) for lang in ('en', 'pt')}
        self.titles = {r[0]: r for r in tsv(os.path.join(P, 'account', 'titles.tsv'))}
        self.jobs = {r[1]: r + [''] * (9 - len(r)) for r in tsv(os.path.join(P, 'skilltree.tsv')) if r[0] == 'J'}  # job names (no overhaul table)
        for r in self.items.values(): d.add_pt(r[1]); d.add_pt(r[6]); d.add_en(r[2]); d.add_en(r[8]); d.add_en(r[9])
        for r in self.quests.values(): d.add_en(r[1]); d.add_pt(r[2]); d.add_pt(r[3]); d.add_pt(r[4]); d.add_en(r[5]); d.add_en(r[6]); d.add_en(r[7])
        for r in self.msgs.values(): d.add_en(r[1]); d.add_pt(r[2])
        for r in self.maps.values(): d.add_en(r[1]); d.add_pt(r[2])
        for r in self.skills.values(): d.add_en(r[2]); d.add_en(r[5]); d.add_pt(r[6]); d.add_pt(r[7])
        for r in self.efst.values(): d.add_en(r[2]); d.add_pt(r[4])
        here = os.path.dirname(os.path.abspath(__file__))
        table = os.path.normpath(os.path.join(here, '..', '..', 'Assets', 'Loki', 'Resources', 'Lang', 'pt-BR.txt'))
        for line in open(table, encoding='utf-8'):
            if line.startswith('#') or '\t' not in line: continue
            e, p = line.rstrip('\n').split('\t', 1)
            d.add_en(e, 5); d.add_pt(p)
        d.freeze()

    # ------------------------------------------------------------------ field checks
    def en_field(self, table, key, field, new, old, name=False, desc=False):
        """An English field of the overhaul: returns the value to write ('' = keep the player's)."""
        R = self.R
        if new == old: R.count(table, field + ' same as yours'); return ''
        if not new:
            if name: R.reject(table, key, field, 'empty', new)
            else: R.count(table, field + ' empty in the overhaul (yours kept)')
            return ''
        why = bad_encoding(new)
        if why: R.reject(table, key, field, 'encoding (%s)' % why, new); return ''
        if self.det.is_pt(new): R.reject(table, key, field, 'portuguese (%s)' % ' '.join(self.det.hits(new)[0][:6]), new); return ''
        if BADCOLOR.search(new) and not BADCOLOR.search(old): R.reject(table, key, field, 'colours', new); return ''
        if desc and len(old) > 60 and len(new) * 2 < len(old) and not self.det.is_pt(old): R.reject(table, key, field, 'cut (%d of %d characters)' % (len(new), len(old)), new); return ''
        R.count(table, field + ' taken')
        return new

    def pt_field(self, table, key, field, new, old, en_new, en_old, desc=False):
        R = self.R
        if new == old or not new: return ''
        why = bad_encoding(new)
        if why: R.reject(table, key, field, 'encoding (%s)' % why, new); return ''
        if (new == en_new or new == en_old) and old and old != new: R.reject(table, key, field, 'english-copy', new); return ''
        if desc and len(old) > 60 and len(new) * 2 < len(old): R.reject(table, key, field, 'cut (%d of %d characters)' % (len(new), len(old)), new); return ''
        R.count(table, field + ' taken')
        return new

    def emit(self, table, key_cols, fields, cols):
        """One pack row when some field is set."""
        if any(fields): self.out.setdefault(table, []).append(key_cols + fields)

    # ------------------------------------------------------------------ the tables
    def do_items(self):
        t = 'items.tsv'
        for r in tsv(os.path.join(self.X, 'items_bilingual.tsv')):
            k = r[0]; p = self.items.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such item in your data)', '\t'.join(r[1:3])); continue
            en_old_name = p[8] or p[2]
            ne = self.en_field(t, k, 'name_en', r[1], en_old_name, name=True)
            de = self.en_field(t, k, 'desc_en', r[6], p[9], desc=True)
            npt = self.pt_field(t, k, 'name_pt', r[2], p[1], r[1], en_old_name)
            dpt = self.pt_field(t, k, 'desc_pt', r[7], p[6], r[6], p[9], desc=True)
            self.emit(t, [k], [ne, npt, de, dpt], 5)

    def do_quests(self):
        t = 'quests.tsv'
        for r in tsv(os.path.join(self.X, 'quests_bilingual.tsv')):
            k = r[0]; p = self.quests.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such quest in your data)', r[1]); continue
            te = self.en_field(t, k, 'title_en', r[1], p[5] or p[1], name=True)
            se = self.en_field(t, k, 'summary_en', r[2], p[6], desc=True)
            de = self.en_field(t, k, 'desc_en', r[3], p[7], desc=True)
            tp = self.pt_field(t, k, 'title_pt', r[4], p[2], r[1], p[5])
            sp = self.pt_field(t, k, 'summary_pt', r[5], p[3], r[2], p[6], desc=True)
            dp = self.pt_field(t, k, 'desc_pt', r[6], p[4], r[3], p[7], desc=True)
            self.emit(t, [k], [te, tp, se, sp, de, dp], 7)

    def do_skills(self):
        t = 'skills.tsv'
        for r in tsv(os.path.join(self.X, 'skills.tsv')):
            k = r[0]; p = self.skills.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such skill in your data)', r[2]); continue
            if r[1] != p[1]: self.R.reject(t, k, 'row', 'key (aegis %s, yours %s)' % (r[1], p[1]), r[2]); continue
            ne = self.en_field(t, k, 'name_en', r[2], p[2], name=True)
            de = self.en_field(t, k, 'desc_en', r[5], p[5], desc=True)
            npt = self.pt_field(t, k, 'name_pt', r[6], p[6], r[2], p[2])
            dpt = self.pt_field(t, k, 'desc_pt', r[7], p[7], r[5], p[5], desc=True)
            self.emit(t, [k], [ne, npt, de, dpt], 5)

    def do_efst(self):
        t = 'efst.tsv'
        for r in tsv(os.path.join(self.X, 'efst_bilingual.tsv')):
            k = r[0]; p = self.efst.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such status in your data)', r[2]); continue
            if r[1] != p[1]: self.R.reject(t, k, 'row', 'key (%s, yours %s)' % (r[1], p[1]), r[2]); continue
            te = self.en_field(t, k, 'title_en', r[2], p[2])
            tp = self.pt_field(t, k, 'title_pt', r[4] if len(r) > 4 else '', p[4], r[2], p[2])
            self.emit(t, [k], [te, tp], 3)

    def do_msgs(self):
        t = 'msgstrings.tsv'
        for r in tsv(os.path.join(self.X, 'msgstrings.tsv')):
            k = r[0]; p = self.msgs.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such message in your data)', r[1]); continue
            en = r[1]
            if en != p[1] and en and not printf_ok(en, p[1], p[2]): self.R.reject(t, k, 'en', 'printf (%s, yours %s)' % (' '.join(PRINTF.findall(en)), ' '.join(PRINTF.findall(p[1]))), en); en = p[1]
            pt = r[2] if len(r) > 2 else ''
            if pt != p[2] and pt and not printf_ok(pt, p[1], p[2]): self.R.reject(t, k, 'pt', 'printf', pt); pt = p[2]
            e = self.en_field(t, k, 'en', en, p[1])
            q = self.pt_field(t, k, 'pt', pt, p[2], en, p[1])
            self.emit(t, [k], [e, q], 3)

    def do_maps(self):
        t = 'mapnames.tsv'
        for r in tsv(os.path.join(self.X, 'mapnames.tsv')):
            k = r[0].lower(); p = self.maps.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such map in your data)', r[1]); continue
            e = self.en_field(t, k, 'en', r[1], p[1], name=True)
            q = self.pt_field(t, k, 'pt', r[2], p[2], r[1], p[1])
            self.emit(t, [k], [e, q], 3)

    def label_ok(self, t, key, new, old, siblings_old, siblings_new, field_row, pt_col=''):
        """World map labels: a spelling fix is taken; information lost is not."""
        if new == old or not new: return ''
        if re.sub(r'\s', '', new.lower()) == re.sub(r'\s', '', old.lower()): self.R.count(t, 'en formatting only (yours kept)'); return ''
        if old and not self.det.is_pt(old) and not (old == pt_col and self.det.hits(old)[0]):
            if field_row and difflib.SequenceMatcher(None, old.lower(), new.lower()).ratio() < 0.8:
                self.R.reject(t, key, 'en', 'monster-label (the field\'s label "%s" names its monster)' % old, new); return ''
            if DIGITS.findall(new) != DIGITS.findall(old): self.R.reject(t, key, 'en', 'floors (numbers %s, yours %s)' % (DIGITS.findall(new), DIGITS.findall(old)), new); return ''
            if siblings_new.count(new) > siblings_old.count(old): self.R.reject(t, key, 'en', 'floors (two places made the same: "%s")' % new, new); return ''
            if difflib.SequenceMatcher(None, old.lower(), new.lower()).ratio() < 0.8: self.R.reject(t, key, 'en', 'renamed ("%s")' % old, new); return ''
        return self.en_field(t, key, 'en', new, old, name=True)

    def do_world(self):
        t = 'worldmap.tsv'
        xs = tsv(os.path.join(self.X, 'worldmap.tsv'))
        mine = {}
        for r in self.world:
            if r[0] == 'W': mine[('W', r[1], '')] = (r[3], r[4], None)
            elif r[0] == 'M': mine[('M', r[1], r[3])] = (r[8], r[9], r[2])
            elif r[0] == 'D': mine[('D', r[1], r[2])] = (r[7], r[8], None)
        groups_old, groups_new = collections.defaultdict(list), collections.defaultdict(list)
        for r in self.world:
            if r[0] == 'M': groups_old[(r[1], r[2])].append(r[8])
        for r in xs:
            if r[0] == 'M': groups_new[(r[1], r[2])].append(r[8])
        for r in xs:
            if r[0] not in 'WMD': continue
            self.R.count(t, 'overhaul rows')
            key = ('W', r[1], '') if r[0] == 'W' else ('M', r[1], r[3]) if r[0] == 'M' else ('D', r[1], r[2])
            p = mine.get(key)
            if p is None: self.R.reject(t, ' '.join(key), 'row', 'key (not in your world map)', ''); continue
            en_new, pt_new = (r[3], r[4]) if r[0] == 'W' else (r[8], r[9]) if r[0] == 'M' else (r[7], r[8])
            grp = (r[1], r[2])
            e = self.label_ok(t, ' '.join(key), en_new, p[0], groups_old.get(grp, []) if r[0] == 'M' else [], groups_new.get(grp, []) if r[0] == 'M' else [],
                              r[0] == 'M' and int(r[2]) >= 100, p[1])
            q = self.pt_field(t, ' '.join(key), 'pt', pt_new, p[1], en_new, p[0])
            self.emit(t, list(key), [e, q], 5)

    def do_nav(self):
        t = 'navigation.tsv'
        mineM = {r[1]: r for r in self.nav if r[0] == 'M'}
        mineN = {r[1]: r for r in self.nav if r[0] == 'N'}
        mineL = {r[1]: r for r in self.nav if r[0] == 'L'}
        mineB = {(r[1], r[2]): r for r in self.nav if r[0] == 'B'}
        mineT = {(r[1], r[2], r[3], r[4]): r for r in self.nav if r[0] == 'T'}
        for r in tsv(os.path.join(self.X, 'navigation.tsv')):
            k = r[0]
            if k not in 'MNLBT': continue
            self.R.count(t, 'overhaul rows ' + k)
            if k == 'M':
                p = mineM.get(r[1])
                if p is None: self.R.reject(t, 'M ' + r[1], 'row', 'key', r[2]); continue
                mine_en = p[6] if len(p) > 6 else ''
                e = self.en_field(t, 'M ' + r[1], 'M name_en', r[2], mine_en, name=True) if not (r[2] == p[2] and mine_en) else ''
                self.emit(t, ['M', r[1]], [e, ''], 4)
            elif k in 'NL':
                p = (mineN if k == 'N' else mineL).get(r[1])
                if p is None: self.R.reject(t, k + ' ' + r[1], 'row', 'key', ''); continue
                ni, li = (7, 8) if k == 'N' else (10, 11)
                name = self.en_field(t, k + ' ' + r[1], k + ' name', r[ni], p[ni])
                # labels: only where yours is unreadable (CP949 read as Windows-1252): the overhaul's English label
                lab = r[li] if p[li] != r[li] and p[li] and mojibake(p[li]) else ''
                if lab: lab = self.en_field(t, k + ' ' + r[1], k + ' label', lab, p[li])
                self.emit(t, [k, r[1]], [name, lab], 4)
            elif k == 'B':
                p = mineB.get((r[1], r[2]))
                if p is None: self.R.reject(t, 'B %s %s' % (r[1], r[2]), 'row', 'key', r[5]); continue
                name = self.en_field(t, 'B %s %s' % (r[1], r[2]), 'B name', r[5], p[5])
                self.emit(t, ['B', r[1], r[2]], [name], 4)
            elif k == 'T':
                p = mineT.get((r[1], r[2], r[3], r[4]))
                if p is None: self.R.reject(t, 'T ' + ' '.join(r[1:5]), 'row', 'key', r[5]); continue
                e = self.en_field(t, 'T ' + ' '.join(r[1:5]), 'T name_en', r[5], p[5], name=True)
                q = self.pt_field(t, 'T ' + ' '.join(r[1:5]), 'T name_pt', r[6], p[6], r[5], p[5])
                self.emit(t, ['T', ':'.join(r[1:5])], [e, q], 4)

    def do_ach(self):
        for lang in ('en', 'pt'):
            t = 'account/achievements_%s.json' % lang
            path = os.path.join(self.A, 'achievements_%s.json' % lang)
            if not os.path.exists(path): continue
            xs = json.load(open(path, encoding='utf-8')); mine = self.ach[lang]; out = {}
            for k, e in xs.items():
                self.R.count(t, 'overhaul rows')
                p = mine.get(k)
                if p is None: self.R.reject(t, k, 'row', 'key (no such achievement in your data)', e.get('n', '')); continue
                o = {}
                for f in ('n', 's', 'd'):
                    if lang == 'en': v = self.en_field(t, k, f, e.get(f, ''), p.get(f, ''), name=(f == 'n'), desc=(f != 'n'))
                    else: v = self.pt_field(t, k, f, e.get(f, ''), p.get(f, ''), self.ach['en'].get(k, {}).get(f, ''), '')
                    if v: o[f] = v
                res = []
                for i, x in enumerate(e.get('res', [])):
                    old = p['res'][i][0] if i < len(p.get('res', [])) else None
                    if old is None: res.append(''); continue
                    v = self.en_field(t, k, 'res', x[0], old, desc=True) if lang == 'en' else self.pt_field(t, k, 'res', x[0], old, '', '')
                    res.append(v)
                if any(res): o['res'] = [[v, 0] for v in res]
                if o: out[k] = o
            if out: self.out[t] = out

    def do_titles(self):
        t = 'account/titles.tsv'
        for r in tsv(os.path.join(self.A, 'titles.tsv')):
            k = r[0]; p = self.titles.get(k)
            self.R.count(t, 'overhaul rows')
            if p is None: self.R.reject(t, k, 'row', 'key (no such title in your data)', r[2]); continue
            e = self.en_field(t, k, 'en', r[2], p[2], name=True)
            q = self.pt_field(t, k, 'pt', r[1], p[1], r[2], p[2])
            self.emit(t, [k], [q, e], 3)

    # ------------------------------------------------------------------ Loki's own English (english_fixes.tsv), last
    COLS = {'items.tsv': {'name_en': 1, 'desc_en': 3}, 'quests.tsv': {'title_en': 1, 'summary_en': 3, 'desc_en': 5},
            'skills.tsv': {'name_en': 1, 'desc_en': 3}, 'efst.tsv': {'title_en': 1}, 'msgstrings.tsv': {'en': 1},
            'mapnames.tsv': {'en': 1}, 'account/titles.tsv': {'en': 2}, 'worldmap.tsv': {'en': 3}, 'navigation.tsv': {'T name_en': 2},
            'skilltree.tsv': {'name_en': 1}}
    WIDTH = {'items.tsv': 5, 'quests.tsv': 7, 'skills.tsv': 5, 'efst.tsv': 3, 'msgstrings.tsv': 3, 'mapnames.tsv': 3, 'account/titles.tsv': 3,
             'worldmap.tsv': 5, 'navigation.tsv': 4, 'skilltree.tsv': 3}

    def pack_key(self, table, k):
        if table == 'worldmap.tsv': return tuple((k.split(' ') + ['', '', ''])[:3])
        if table == 'navigation.tsv': return tuple(k.split(' ', 1))
        return (k,)

    def set_pack(self, table, k, f, v):
        if table.startswith('account/achievements'):
            e = self.out.setdefault(table, {}).setdefault(k, {})
            if f.startswith('res'):
                i = int(f[3:]); r = e.setdefault('res', []); r += [['', 0]] * (i + 1 - len(r)); r[i] = [v, 0]
            else: e[f] = v
            return
        rows = self.out.setdefault(table, [])
        key = self.pack_key(table, k)
        for r in rows:
            if tuple(r[:len(key)]) == key: break
        else:
            r = list(key) + [''] * (self.WIDTH[table] - len(key)); rows.append(r)
        r[self.COLS[table][f]] = v

    def apply_fixes(self, by_text):
        """Last: Loki's own English. english_by_text.tsv for every field a player would see in Portuguese or with no English (looked up
        by its Portuguese text, or the English field's own text), then english_fixes.tsv row by row (a full text, or "~" replacements
        in the text a player would see)."""
        texts = {}
        if by_text and os.path.exists(by_text):
            for r in tsv(by_text):
                if len(r) >= 2: texts[r[0]] = r[1]
        res, gaps = self.residual()
        for t, k, f, _, en in res:
            v = texts.get(en)
            if v: self.set_pack(t, k, f, v); self.R.count(t, 'Loki english (by text)')
        for t, k, f, pt in gaps:
            v = texts.get(pt)
            if v: self.set_pack(t, k, f, v); self.R.count(t, 'Loki english (by text)')
        for table, fx in self.fixes.items():
            for (k, f), v in fx.items():
                if isinstance(v, tuple):  # "~" replacements in what a player would see now
                    cur = self.seen_en(table, k, f)
                    for pair in v[1].split(' || '):
                        old, new = pair.split('|', 1)
                        if old not in cur: raise SystemExit('english_fixes.tsv: "%s" is not in %s %s %s' % (old, table, k, f))
                        cur = cur.replace(old, new)
                    v = cur
                self.set_pack(table, k, f, v)
                self.R.count(table, 'Loki english fixes')

    def seen_en(self, table, k, f):
        """The English text a player would see now for one field (the pack's, else the player's)."""
        if table.startswith('account/achievements'):
            o = self.out.get(table, {}).get(k, {}); p = self.ach['en'][k]
            if f.startswith('res'):
                i = int(f[3:]); return (o.get('res', [])[i][0] if i < len(o.get('res', [])) and o['res'][i][0] else p['res'][i][0])
            return o.get(f) or p.get(f, '')
        key = self.pack_key(table, k)
        for r in self.out.get(table, []):
            if tuple(r[:len(key)]) == key and r[self.COLS[table][f]]: return r[self.COLS[table][f]]
        mine = {'items.tsv': lambda: {'name_en': self.items[k][8] or self.items[k][2], 'desc_en': self.items[k][9]},
                'quests.tsv': lambda: {'title_en': self.quests[k][5] or self.quests[k][1], 'summary_en': self.quests[k][6], 'desc_en': self.quests[k][7]},
                'skills.tsv': lambda: {'name_en': self.skills[k][2], 'desc_en': self.skills[k][5]},
                'efst.tsv': lambda: {'title_en': self.efst[k][2]}, 'msgstrings.tsv': lambda: {'en': self.msgs[k][1]},
                'mapnames.tsv': lambda: {'en': self.maps[k][1]}, 'account/titles.tsv': lambda: {'en': self.titles[k][2]},
                'skilltree.tsv': lambda: {'name_en': self.jobs[k][5]}}
        return mine[table]()[f]

    # ------------------------------------------------------------------ after the merge: what a player sees in English
    def residual(self):
        res, gaps = [], []
        pack = {t: {tuple(r[:3 if t == 'worldmap.tsv' else 2 if t == 'navigation.tsv' else 1]): r for r in rows} for t, rows in self.out.items() if isinstance(rows, list)}
        def P(t, key, i, mine):
            r = pack.get(t, {}).get(key)
            return r[i] if r is not None and len(r) > i and r[i] else mine
        def chk(t, key, field, en, pt):
            if en and self.det.is_pt(en): res.append((t, key, field, ' '.join(self.det.hits(en)[0][:6]), en[:200]))
            if not en and pt: gaps.append((t, key, field, pt[:200]))
        for k, p in self.items.items():
            chk('items.tsv', k, 'name_en', P('items.tsv', (k,), 1, p[8] or p[2]), p[1])
            chk('items.tsv', k, 'desc_en', P('items.tsv', (k,), 3, p[9]), p[6])
        for k, p in self.quests.items():
            chk('quests.tsv', k, 'title_en', P('quests.tsv', (k,), 1, p[5] or p[1]), p[2])
            chk('quests.tsv', k, 'summary_en', P('quests.tsv', (k,), 3, p[6]), p[3])
            chk('quests.tsv', k, 'desc_en', P('quests.tsv', (k,), 5, p[7]), p[4])
        for k, p in self.skills.items():
            chk('skills.tsv', k, 'name_en', P('skills.tsv', (k,), 1, p[2]), p[6])
            chk('skills.tsv', k, 'desc_en', P('skills.tsv', (k,), 3, p[5]), p[7])
        for k, p in self.efst.items():
            if p[2] or p[4]: chk('efst.tsv', k, 'title_en', P('efst.tsv', (k,), 1, p[2]), p[4])
        for k, p in self.msgs.items(): chk('msgstrings.tsv', k, 'en', P('msgstrings.tsv', (k,), 1, p[1]), p[2])
        for k, p in self.maps.items(): chk('mapnames.tsv', k, 'en', P('mapnames.tsv', (k,), 1, p[1]), p[2])
        for r in self.world:
            if r[0] == 'W': chk('worldmap.tsv', 'W ' + r[1], 'en', P('worldmap.tsv', ('W', r[1], ''), 3, r[3]), r[4])
            elif r[0] == 'M': chk('worldmap.tsv', 'M %s %s' % (r[1], r[3]), 'en', P('worldmap.tsv', ('M', r[1], r[3]), 3, r[8]), r[9])
            elif r[0] == 'D': chk('worldmap.tsv', 'D %s %s' % (r[1], r[2]), 'en', P('worldmap.tsv', ('D', r[1], r[2]), 3, r[7]), r[8])
        for r in self.nav:
            if r[0] == 'T': chk('navigation.tsv', 'T ' + ':'.join(r[1:5]), 'T name_en', P('navigation.tsv', ('T', ':'.join(r[1:5])), 2, r[5]), r[6])
        packach = self.out.get('account/achievements_en.json', {})
        for k, p in self.ach['en'].items():
            o = packach.get(k, {})
            for f in ('n', 's', 'd'): chk('account/achievements_en.json', k, f, o.get(f) or p.get(f, ''), self.ach['pt'].get(k, {}).get(f, ''))
            for i, x in enumerate(p.get('res', [])):
                v = o.get('res', [])[i][0] if i < len(o.get('res', [])) and o['res'][i][0] else x[0]
                ptr = self.ach['pt'].get(k, {}).get('res', [])
                chk('account/achievements_en.json', k, 'res%d' % i, v, ptr[i][0] if i < len(ptr) else '')
        for k, p in self.titles.items(): chk('account/titles.tsv', k, 'en', P('account/titles.tsv', (k,), 2, p[2]), p[1] if p[1] != p[2] else '')
        for k, p in self.jobs.items(): chk('skilltree.tsv', k, 'name_en', P('skilltree.tsv', (k,), 1, p[5]), p[7])
        return res, gaps

    # ------------------------------------------------------------------ writing
    def write(self, outdir, repdir, name, version):
        os.makedirs(outdir, exist_ok=True); os.makedirs(repdir, exist_ok=True)
        heads = {'items.tsv': 'id\tname_en\tname_pt\tdesc_en\tdesc_pt', 'quests.tsv': 'id\ttitle_en\ttitle_pt\tsummary_en\tsummary_pt\tdesc_en\tdesc_pt',
                 'skills.tsv': 'id\tname_en\tname_pt\tdesc_en\tdesc_pt', 'efst.tsv': 'id\ttitle_en\ttitle_pt', 'msgstrings.tsv': 'id\ten\tpt',
                 'mapnames.tsv': 'map\ten\tpt', 'worldmap.tsv': 'kind\tworld\tkey\ten\tpt', 'navigation.tsv': 'kind\tkey\ta\tb', 'skilltree.tsv': 'job\tname_en\tname_pt',
                 'account/titles.tsv': 'id\tpt\ten'}
        files = []
        for t, rows in sorted(self.out.items()):
            p = os.path.join(outdir, t.replace('/', os.sep)); os.makedirs(os.path.dirname(p), exist_ok=True)
            if isinstance(rows, dict):
                data = json.dumps(rows, ensure_ascii=False, separators=(',', ':'), sort_keys=True).encode('utf-8'); n = len(rows)
            else:
                lines = ['# ' + heads[t] + '  (Loki text pack: a non-empty field replaces the game data\'s; \\n is a line break)']
                for r in rows: lines.append('\t'.join(r))
                data = ('\n'.join(lines) + '\n').encode('utf-8'); n = len(rows)
            open(p, 'wb').write(data)
            files.append({'path': t, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data), 'rows': n})
        readme = ('Loki text pack: %s %s\r\n\r\nRewritten game texts (item, quest, skill, map, status, message, world map, navigation, achievement and title '
                  'texts) that Loki reads at run time over the tables it makes from your Ragnarok Online data. Put this folder next to Loki.exe '
                  '(loki.json "textPack": "textpack"). textpack.json lists every file with its SHA-256: a changed file is not used.\r\n' % (name, version)).encode('utf-8')
        open(os.path.join(outdir, 'README.txt'), 'wb').write(readme)
        files.append({'path': 'README.txt', 'sha256': hashlib.sha256(readme).hexdigest(), 'bytes': len(readme), 'rows': 0})
        files = write_manifest(outdir, name, version, {f['path']: f['rows'] for f in files})
        with open(os.path.join(repdir, 'rejects.tsv'), 'w', encoding='utf-8', newline='\n') as f:
            f.write('table\tkey\tfield\treason\tthe overhaul\'s text\n')
            for r in self.R.rejects: f.write('\t'.join(r) + '\n')
        res, gaps = self.residual()
        with open(os.path.join(repdir, 'residual_pt.tsv'), 'w', encoding='utf-8', newline='\n') as f:
            f.write('table\tkey\tfield\tportuguese words\tthe English text a player sees\n')
            for r in res: f.write('\t'.join(r) + '\n')
        with open(os.path.join(repdir, 'gaps.tsv'), 'w', encoding='utf-8', newline='\n') as f:
            f.write('table\tkey\tfield\tthe Portuguese text (no English anywhere)\n')
            for r in gaps: f.write('\t'.join(r) + '\n')
        summ = []
        for t in sorted(self.R.stats):
            summ.append(t + ': ' + ', '.join('%s %d' % (k, v) for k, v in sorted(self.R.stats[t].items())))
        summ.append('pack files: ' + ', '.join('%s %d rows' % (f['path'], f['rows']) for f in files))
        rc = collections.Counter(r[0] for r in res); gc = collections.Counter(r[0] for r in gaps)
        summ.append('residual Portuguese in English after the merge: %d (%s)' % (len(res), ', '.join('%s %d' % kv for kv in sorted(rc.items()))))
        summ.append('gaps (Portuguese, no English): %d (%s)' % (len(gaps), ', '.join('%s %d' % kv for kv in sorted(gc.items()))))
        summ.append('rejected fields: %d' % len(self.R.rejects))
        open(os.path.join(repdir, 'summary.txt'), 'w', encoding='utf-8', newline='\n').write('\n'.join(summ) + '\n')
        print('\n'.join(summ))


def write_manifest(outdir, name, version, rows):
    """textpack.json for EVERY file of the pack folder (a file the manifest does not list is refused by the friends' package
    step; one whose SHA-256 differs is not used by the game): this tool's tables, its README.txt and other tools' files."""
    files = []
    for root, dirs, names in os.walk(outdir):
        dirs.sort()
        for n in sorted(names):
            rel = os.path.relpath(os.path.join(root, n), outdir).replace(os.sep, '/')
            if rel == 'textpack.json': continue
            data = open(os.path.join(root, n), 'rb').read()
            r = rows.get(rel)
            if r is None: r = sum(1 for l in data.decode('utf-8', 'replace').split('\n') if l.strip() and not l.startswith('#')) if rel.endswith('.tsv') else 0
            files.append({'path': rel, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data), 'rows': r})
    manifest = {'format': 1, 'name': name, 'version': version, 'made_by': 'client/tools/textpack/build_textpack.py', 'files': files}
    open(os.path.join(outdir, 'textpack.json'), 'w', encoding='utf-8', newline='\n').write(json.dumps(manifest, ensure_ascii=False, indent=1) + '\n')
    return files


def main(argv):
    if len(argv) == 2 and argv[0] == '--manifest':
        old = json.load(open(os.path.join(argv[1], 'textpack.json'), encoding='utf-8'))
        files = write_manifest(argv[1], old['name'], old['version'], {f['path']: f.get('rows', 0) for f in old['files']})
        print('textpack.json: %d files (%s)' % (len(files), ', '.join(f['path'] for f in files)))
        return 0
    args = [a for a in argv if not a.startswith('--')]
    opts = dict(zip(argv[::1], argv[1::1]))
    if len(args) < 5: print(__doc__); return 2
    here = os.path.dirname(os.path.abspath(__file__))
    fixes = opts.get('--fixes', os.path.join(here, 'english_fixes.tsv')); by_text = opts.get('--by-text', os.path.join(here, 'english_by_text.tsv'))
    name = opts.get('--name', 'Loki text overhaul'); version = opts.get('--version', '2026-10-15')
    args = [a for i, a in enumerate(argv) if not a.startswith('--') and (i == 0 or not argv[i - 1].startswith('--'))]
    b = Builder(args[0], args[1], args[2], fixes)
    b.do_items(); b.do_quests(); b.do_skills(); b.do_efst(); b.do_msgs(); b.do_maps(); b.do_world(); b.do_nav(); b.do_ach(); b.do_titles()
    b.apply_fixes(by_text)
    b.write(args[3], args[4], name, version)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
