"""
Achievement, title and attendance tables for Loki (research/windows9_packets.md §2-§4). The server sends only ids, counters and flags,
so the client ships the texts. Writes into <out dir>/account/:
  achievements_pt.json / achievements_en.json
      { id: {n: name, s: summary, d: details, g: group, ma: major, mi: minor, ui: UI_Type (0 checklist, 1 counter),
             sc: score, res: [[text, count], ...], item: reward item id, amt: amount, title: reward title id} }
      Texts from the client's System/achievement_list.lub (PT) and System/english/achievement_list.lub (EN), dumped by
      dump_achievements.lua. Group, rewards, title and score come from the server's db/re/achievement_db.yml (what the server
      really gives); ids only the server knows get its English Name.
  titles.tsv            id <TAB> pt <TAB> en   from datainfo/titletable.lub (PT) and data/english/.../titletable.lub, dump_titles.lua
  attendance.json       {periods: [{start, end, rewards: [[day, item, amount], ...]}]} from the server's db/import/attendance.yml
Strings in the lub dumps are hex of raw bytes: UTF-8 where valid, else CP1252 (LATAM), else CP949.
Usage: build_account_tables.py <rathena root> <ach_pt.tsv> <ach_en.tsv> <title_pt.tsv> <title_en.tsv> <attendance.yml> <out dir>
"""
import json, os, sys, yaml

def dec(h):
    b = bytes.fromhex(h)
    for e in ('utf-8', 'cp1252', 'cp949'):
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')

def aegis_items(root):
    m = {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []: m[it['AegisName'].lower()] = it['Id']
    return m

def read_lub_dump(path):
    out = {}
    for line in open(path, encoding='ascii'):
        c = line.rstrip('\n').split('\t')
        res = []
        for r in c[12:]:
            t, cnt, _short = r.split('|')
            res.append([dec(t), int(cnt)])
        out[int(c[0])] = {'ui': int(c[1]), 'lg': dec(c[2]), 'ma': int(c[3]), 'mi': int(c[4]), 'sc': int(c[5]), 'n': dec(c[6]), 's': dec(c[7]),
                          'd': dec(c[8]), 'litem': int(c[9]), 'ltitle': int(c[10]), 'lbuff': int(c[11]), 'res': res}
    return out

def main(root, ach_pt, ach_en, t_pt, t_en, att, out):
    out = os.path.join(out, 'account'); os.makedirs(out, exist_ok=True)
    items = aegis_items(root); missing = []
    srv = {}
    for f in (os.path.join(root, 'db', 're', 'achievement_db.yml'), os.path.join(root, 'db', 'import', 'achievement_db.yml')):
        if not os.path.exists(f): continue
        d = yaml.safe_load(open(f, encoding='utf-8')) or {}
        for a in d.get('Body') or []:
            r = a.get('Rewards') or {}
            it = r.get('Item'); iid = items.get(str(it).lower(), 0) if it else 0
            if it and not iid: missing.append(it)
            targets = {t.get('Id', 0): t.get('Count', 1) for t in (a.get('Targets') or [])}
            srv[a['Id']] = {'g': a.get('Group', ''), 'name': a.get('Name', ''), 'item': iid, 'amt': int(r.get('Amount', 1)) if it else 0,
                            'title': int(r.get('TitleId', 0)), 'sc': int(a.get('Score', 0)), 'targets': targets}
    for lang, path in (('pt', ach_pt), ('en', ach_en)):
        cl = read_lub_dump(path); res = {}
        for aid in sorted(set(cl) | set(srv)):
            c = cl.get(aid); s = srv.get(aid)
            e = {'n': c['n'] if c else s['name'], 's': c['s'] if c else '', 'd': c['d'] if c else '',
                 'g': s['g'] if s else c['lg'], 'ma': c['ma'] if c else 0, 'mi': c['mi'] if c else 0, 'ui': c['ui'] if c else 0,
                 'sc': s['sc'] if s else c['sc'], 'item': s['item'] if s else c['litem'], 'amt': s['amt'] if s else (1 if c['litem'] else 0),
                 'title': s['title'] if s else c['ltitle'], 'srv': s is not None}
            rl = [list(x) for x in c['res']] if c else []
            if s:  # server target counts win (what the counters are compared against); extra targets get a blank text
                for i, cnt in s['targets'].items():
                    while len(rl) <= i: rl.append(['', 1])
                    rl[i][1] = int(cnt)
            e['res'] = rl
            res[aid] = e
        json.dump(res, open(os.path.join(out, f'achievements_{lang}.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
    titles = {}
    for lang, path in (('pt', t_pt), ('en', t_en)):
        for line in open(path, encoding='ascii'):
            i, h = line.rstrip('\n').split('\t'); titles.setdefault(int(i), {})[lang] = dec(h)
    with open(os.path.join(out, 'titles.tsv'), 'w', encoding='utf-8', newline='\n') as f:
        for i in sorted(titles): f.write(f"{i}\t{titles[i].get('pt', titles[i].get('en', ''))}\t{titles[i].get('en', titles[i].get('pt', ''))}\n")
    d = yaml.safe_load(open(att, encoding='utf-8')) or {}
    periods = [{'start': int(p['Start']), 'end': int(p['End']),
                'rewards': [[int(r['Day']), int(r['ItemId']), int(r.get('Amount', 1))] for r in p.get('Rewards') or []]} for p in d.get('Body') or []]
    json.dump({'periods': periods}, open(os.path.join(out, 'attendance.json'), 'w'), separators=(',', ':'))
    print(f'achievements: client {len(read_lub_dump(ach_pt))}, server {len(srv)}; titles {len(titles)}; attendance periods {len(periods)}; unresolved items {missing[:10]}')

if __name__ == '__main__':
    main(*sys.argv[1:8])
