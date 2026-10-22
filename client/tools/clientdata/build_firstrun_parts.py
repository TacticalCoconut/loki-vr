#!/usr/bin/env python3
r"""
Writes the parts of the GRF-derived tables that do NOT come from Gravity (client wave 3b, w3data), for the client's first-run
data builder (Assets/Loki/Runtime/Data/, ClientDataBuilder): the build ships these, the player's own RO install supplies the
rest, and the tables the client makes from both equal what the Python tools make (build_tables.py, build_account_tables.py,
build_qol_tables.py, ../appearance/build_appearance.py, ../combat/build_combat_data.py). Every value below is computed with
the same expressions those tools use (quoted per part), so a part holds exactly what the tool would have taken from rAthena /
roBrowserLegacy / zrenderer at that point.

  python build_firstrun_parts.py <rathena root> <rB src> <rb_tables.json> <zrenderer> <rb_combat.json> <out dir>
      <rb_tables.json>: node ../appearance/rb_tables.mjs <rB src>;  <rb_combat.json>: node ../combat/rb_combat_tables.mjs <rB src>
      <zrenderer>: zrenderer's resolver_data folder (https://github.com/zhad3/zrenderer, b651017), or, where no checkout exists
                   (this PC), an appearance.txt made by build_appearance.py: its wjob2 / pal2 rows are those two files read back
                   (zr_rows: line i = job i for i <= 45, job - 3950 for i >= 51), so the two files are rebuilt from them first
      <out dir>: the Unity project's Assets\StreamingAssets\data\firstrun

Parts (UTF-8 unless said otherwise):
  items_rathena.tsv        id, name, type, minLevel         build_tables.py: en / typ / minlv from db/re/item_db_{usable,equip,etc}.yml
                                                            (the name already through clean(): every use of it is clean(en[..]))
  quests_rathena.tsv       id, title                        build_tables.py: qen from db/re/quest_db.yml, in its dict order
                                                            (the rows for quests the client lacks follow that order); title clean()ed
  achievements_rathena.json  { id: {g, name, item, amt, title, sc, targets: [[id, count], ...]} }
                                                            build_account_tables.py: srv from db/re/achievement_db.yml (+ db/import)
  itemviews_rathena.tsv    id, type, weaponType, leftHand   ../appearance/build_appearance.py: typ / sub / left from item_db_equip.yml
  appearance_base.txt      Latin-1: every appearance.txt line before the client-lub rows (header, wjob, pal, shield, trail,
                                                            wjob2, pal2, costume, costumename, watk), each ending in '\n'
  stateicons_rb.tsv        efst, icon hex                   build_qol_tables.py: roBrowserLegacy DB/Status/StatusInfo.js icons
                                                            of the SC names in StatusConst.js, in the order it meets them
  mob_attack_rb.json       { projectile: {...}, spawn: {...} }   rb_combat.json's two tables (rb_combat_tables.mjs), same order
  pcjobnames_hex.json      a copy of ../rodata/pcjobnames_hex.json (build_spritenames.py's pc rows)
  cp949.bin                Loki's own CP949 table, made from Python's cp949 codec (a character encoding, not game data):
                           126 lead bytes (0x81-0xFE) x 190 trail bytes (0x41-0xFE), a little-endian UInt16 code point each
                           (0 = no character). build_tables.py encodes item resource names to CP949 and build_account_tables.py
                           may decode with it; the codec is a bijection on these 17,048 pairs (checked below).
"""
import json, os, re, shutil, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'appearance'))
import yaml  # noqa: E402
import build_tables  # noqa: E402  (TYPES, clean)
import build_appearance  # noqa: E402  (W, lat, zr_rows)


def items_part(root):
    """build_tables.py main(): the en / typ / minlv loop, verbatim."""
    en, typ, minlv = {}, {}, {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []:
            en[it['Id']] = it.get('Name', '')
            typ[it['Id']] = build_tables.TYPES.get(it.get('Type', 'Etc'), 3)
            minlv[it['Id']] = int(it.get('EquipLevelMin', 0) or 0)
    rows = []
    for iid in sorted(en):
        rows.append('\t'.join([str(iid), build_tables.clean(en[iid]), str(typ[iid]), str(minlv[iid])]))
    return rows


def quests_part(root):
    q = yaml.safe_load(open(os.path.join(root, 'db', 're', 'quest_db.yml'), encoding='utf-8'))
    qen = {x['Id']: x.get('Title', '') for x in (q.get('Body') or [])}
    return ['\t'.join([str(qid), build_tables.clean(t)]) for qid, t in qen.items()]


def achievements_part(root):
    """build_account_tables.py main(): the srv loop, verbatim (the targets dict kept as [[id, count], ...] in its order)."""
    m = {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []: m[it['AegisName'].lower()] = it['Id']
    items = m
    srv = {}
    for f in (os.path.join(root, 'db', 're', 'achievement_db.yml'), os.path.join(root, 'db', 'import', 'achievement_db.yml')):
        if not os.path.exists(f): continue
        d = yaml.safe_load(open(f, encoding='utf-8')) or {}
        for a in d.get('Body') or []:
            r = a.get('Rewards') or {}
            it = r.get('Item'); iid = items.get(str(it).lower(), 0) if it else 0
            targets = {t.get('Id', 0): t.get('Count', 1) for t in (a.get('Targets') or [])}
            srv[a['Id']] = {'g': a.get('Group', ''), 'name': a.get('Name', ''), 'item': iid, 'amt': int(r.get('Amount', 1)) if it else 0,
                            'title': int(r.get('TitleId', 0)), 'sc': int(a.get('Score', 0)),
                            'targets': [[i, int(c)] for i, c in targets.items()]}
    return srv


def itemviews_part(root):
    """../appearance/build_appearance.py main(): the typ / sub / left loop, verbatim."""
    typ, sub, left = {}, {}, {}
    TYPES = {'Armor': 4, 'Weapon': 5}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'item_db_equip.yml'), encoding='utf-8'))
    for it in d.get('Body') or []:
        t = TYPES.get(it.get('Type'))
        if t is None: continue
        iid = it['Id']
        typ[iid] = t
        if t == 5:
            st = str(it.get('SubType', 'Fist')).upper()
            sub[iid] = build_appearance.W.get(st, 0)
        loc = it.get('Locations') or {}
        left[iid] = 1 if loc.get('Left_Hand') else 0
    return ['%d\t%d\t%d\t%d' % (i, typ[i], sub.get(i, -1), left.get(i, 0)) for i in sorted(typ)]


def appearance_base(rb_json, zr_dir):
    """../appearance/build_appearance.py main(): the rows before `for line in open(lubs_tsv ...)`, verbatim."""
    rb = json.load(open(rb_json))
    out = ['# Loki appearance tables. Encoding: Latin-1 (CP949 bytes preserved). Generated by tools/appearance/build_appearance.py.']
    lat = build_appearance.lat
    for kind, key in (('wjob', 'weaponjob'), ('pal', 'palname'), ('shield', 'shield'), ('trail', 'trail')):
        for k in sorted(rb[key], key=int):
            v = lat(rb[key][k]).replace('/', '\\')
            out.append(f'{kind}\t{k}\t{v}')
    for kind, fn in (('wjob2', 'job_weapon_names.txt'), ('pal2', 'job_pal_names.txt')):
        for job, v in sorted(build_appearance.zr_rows(os.path.join(zr_dir, fn)).items()):
            out.append(f'{kind}\t{job}\t{v}')
    for k in sorted(rb['costume'], key=int):
        out.append(f'costume\t{k}\t{rb["costume"][k]}')
    for k in sorted(rb['costumeName'], key=int):
        out.append(f'costumename\t{k}\t{lat(rb["costumeName"][k])}')
    for job in sorted(rb['weaponAction'], key=int):
        for sex, wt, idx in rb['weaponAction'][job]:
            out.append(f'watk\t{job}\t{sex}\t{wt}\t{idx}')
    return ''.join(x + '\n' for x in out).encode('latin-1')


def stateicons_rb(rb_src):
    """build_qol_tables.py main(): the roBrowserLegacy loop, verbatim, minus the `if efst not in icons` test (the client applies it)."""
    statusinfo_js = os.path.join(rb_src, 'DB', 'Status', 'StatusInfo.js')
    statusconst_js = os.path.join(rb_src, 'DB', 'Status', 'StatusConst.js')
    consts = {}
    for m in re.finditer(r'^\s*([A-Z0-9_]+)\s*:\s*(\d+)\s*,?', open(statusconst_js, encoding='utf-8').read(), re.M):
        consts[m.group(1)] = int(m.group(2))
    js = open(statusinfo_js, encoding='utf-8').read()
    rows = []
    for m in re.finditer(r"StatusInfo\[SC\.([A-Z0-9_]+)\]\s*=\s*\{(.*?)\n\};", js, re.S):
        name, body = m.group(1), m.group(2)
        im = re.search(r"icon:\s*'((?:\\x[0-9a-fA-F]{2}|[^'\\])*)'", body)
        if not im or name not in consts: continue
        raw = im.group(1)
        b = bytes(int(h, 16) if h else ord(ch) for h, ch in re.findall(r"\\x([0-9a-fA-F]{2})|(.)", raw))
        rows.append('%d\t%s' % (consts[name], b.hex()))
    return rows


def zr_from_appearance(appearance_txt, out_dir):
    """Rebuilds resolver_data/job_weapon_names.txt and job_pal_names.txt from an appearance.txt's wjob2 / pal2 rows, so that
    build_appearance.zr_rows() reads back exactly those rows (lines 46-50 stay empty: zr_rows skips them)."""
    rows = {'wjob2': {}, 'pal2': {}}
    for line in open(appearance_txt, 'rb').read().split(b'\n'):
        c = line.split(b'\t')
        k = c[0].decode('latin-1')
        if k in rows: rows[k][int(c[1])] = c[2].decode('latin-1')
    os.makedirs(out_dir, exist_ok=True)
    for kind, fn in (('wjob2', 'job_weapon_names.txt'), ('pal2', 'job_pal_names.txt')):
        lines = {}
        for job, v in rows[kind].items():
            i = job if job <= 45 else job - build_appearance.ADVANCED_JOB_INDEX
            if not (i <= 45 or i >= 51) or i in lines: raise SystemExit('%s row %d cannot come from zrenderer' % (kind, job))
            lines[i] = v.encode('latin-1').decode('cp949')
        open(os.path.join(out_dir, fn), 'w', encoding='utf-8', newline='\n').write('\n'.join(lines.get(i, '') for i in range(max(lines) + 1)) + '\n')
    return out_dir


def cp949_table():
    out = bytearray()
    n = 0
    for lead in range(0x81, 0xFF):
        for tr in range(0x41, 0xFF):
            try: s = bytes([lead, tr]).decode('cp949')
            except UnicodeDecodeError: s = ''
            if len(s) > 1: raise SystemExit('cp949 pair %02x%02x decodes to %d characters' % (lead, tr, len(s)))
            cp = ord(s) if s else 0
            if cp > 0xFFFF: raise SystemExit('cp949 pair %02x%02x is outside the BMP' % (lead, tr))
            if cp and chr(cp).encode('cp949') != bytes([lead, tr]): raise SystemExit('cp949 is not a bijection at %02x%02x' % (lead, tr))
            n += 1 if cp else 0
            out += struct.pack('<H', cp)
    enc = 0
    for cp in range(0x80, 0x10000):
        if 0xD800 <= cp < 0xE000: continue
        try: b = chr(cp).encode('cp949')
        except UnicodeEncodeError: continue
        if len(b) != 2 or not (0x81 <= b[0] <= 0xFE and 0x41 <= b[1] <= 0xFE): raise SystemExit('U+%04X encodes to %r' % (cp, b))
        enc += 1
    if enc != n: raise SystemExit('cp949: %d encodable characters, %d decodable pairs' % (enc, n))
    for single in range(0x80, 0x100):
        try: bytes([single]).decode('cp949'); raise SystemExit('cp949 decodes the single byte %02x' % single)
        except UnicodeDecodeError: pass
    return bytes(out), n


def main(argv):
    if len(argv) != 6: print(__doc__); return 2
    root, rb_src, rb_tables, zr_dir, rb_combat, out = argv
    os.makedirs(out, exist_ok=True)
    if os.path.isfile(zr_dir):
        import tempfile
        zr_dir = zr_from_appearance(zr_dir, tempfile.mkdtemp(prefix='zr_'))
        print('zrenderer resolver_data rebuilt from the appearance.txt given, in', zr_dir)
    def write(name, text): open(os.path.join(out, name), 'w', encoding='utf-8', newline='\n').write(text)
    items = items_part(root); write('items_rathena.tsv', ''.join(r + '\n' for r in items))
    quests = quests_part(root); write('quests_rathena.tsv', ''.join(r + '\n' for r in quests))
    ach = achievements_part(root)
    with open(os.path.join(out, 'achievements_rathena.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(ach, f, ensure_ascii=False, separators=(',', ':'))
    views = itemviews_part(root); write('itemviews_rathena.tsv', ''.join(r + '\n' for r in views))
    base = appearance_base(rb_tables, zr_dir); open(os.path.join(out, 'appearance_base.txt'), 'wb').write(base)
    si = stateicons_rb(rb_src); write('stateicons_rb.tsv', ''.join(r + '\n' for r in si))
    rc = json.load(open(rb_combat, encoding='utf-8'))
    with open(os.path.join(out, 'mob_attack_rb.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'projectile': rc['projectile'], 'spawn': rc['spawn']}, f, indent=1)
    shutil.copyfile(os.path.join(HERE, '..', 'rodata', 'pcjobnames_hex.json'), os.path.join(out, 'pcjobnames_hex.json'))
    table, pairs = cp949_table(); open(os.path.join(out, 'cp949.bin'), 'wb').write(table)
    print('items %d, quests %d, achievements %d, itemviews %d, appearance base %d bytes, rB state icons %d, mob attack %d + %d, cp949 %d pairs'
          % (len(items), len(quests), len(ach), len(views), len(base), len(si), len(rc['projectile']), len(rc['spawn']), pairs))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
