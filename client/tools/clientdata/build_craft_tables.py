"""
Crafting / enchant / reform tables for Loki (research/refine_enchant_craft_packets.md §3.5, §3.6, §3.8). The server sends only item ids for
these windows, so the client needs its own copy of the recipes. Writes Assets/StreamingAssets/data/craft/*.json from rAthena's db:
  produce.json       [ {id, product, lv, skill, skillLv, mats:[[item, amount], …]} ]      from db/re/produce_db.txt (amount 0 = must own, not consumed)
  arrows.json        { source item id: [[product, amount], …] }                           from db/create_arrow_db.yml
  item_enchant.json  [ {id, targets:[item…], minRefine, minGrade, allowRandomOptions, reset:{chance, price, mats}, order:[slot…],
                        slots:{slot:{price, mats, chance, gradeBonus:[[grade, chance]], enchants:{grade:[[item, chance]]},
                                    perfect:[{item, price, mats}], upgrades:[{enchant, upgrade, price, mats}]}}} ]   from db/re/item_enchant.yml
  item_reform.json   { reform item id: [ {base, minRefine, maxRefine, randomOptions, cardsAllowed, mats, result, changeRefine, clearSlots, removeGrade} ] }
                                                                                           from db/re/item_reform.yml
AegisNames are resolved through db/re/item_db_*.yml. Usage: build_craft_tables.py <rathena root> <out dir>
"""
import json, os, sys, yaml

def aegis_map(root):
    m = {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []: m[it['AegisName']] = it['Id']; m[it['AegisName'].lower()] = it['Id']
    return m

def main(root, out):
    os.makedirs(out, exist_ok=True)
    ids = aegis_map(root)
    def iid(name):
        if name is None: return 0
        return ids.get(name, ids.get(str(name).lower(), 0))
    missing = set()
    def need(name):
        v = iid(name)
        if v == 0: missing.add(name)
        return v
    def mats(lst):
        return [[need(x['Material']), int(x.get('Amount', 1))] for x in (lst or [])]
    # ---- produce
    prod = []
    for line in open(os.path.join(root, 'db', 're', 'produce_db.txt'), encoding='utf-8'):
        line = line.split('//')[0].strip()
        if not line: continue
        c = [int(x) for x in line.split(',') if x.strip() != '']
        if len(c) < 5 or c[1] == 0: continue
        prod.append({'id': c[0], 'product': c[1], 'lv': c[2], 'skill': c[3], 'skillLv': c[4], 'mats': [[c[i], c[i + 1]] for i in range(5, len(c) - 1, 2)]})
    json.dump(prod, open(os.path.join(out, 'produce.json'), 'w'), separators=(',', ':'))
    # ---- arrows
    arr = {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 'create_arrow_db.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        arr[need(e['Source'])] = [[need(m['Item']), int(m.get('Amount', 1))] for m in (e.get('Make') or []) if int(m.get('Amount', 1)) > 0]
    json.dump(arr, open(os.path.join(out, 'arrows.json'), 'w'), separators=(',', ':'))
    # ---- item enchant
    ench = []
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'item_enchant.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        o = {'id': e['Id'], 'targets': [need(k) for k, v in (e.get('TargetItems') or {}).items() if v],
             'minRefine': int(e.get('MinimumRefine', 0)), 'minGrade': int(e.get('MinimumEnchantgrade', 0)),
             'allowRandomOptions': bool(e.get('AllowRandomOptions', True)), 'order': [int(s['Slot']) for s in (e.get('Order') or [])], 'slots': {}}
        r = e.get('Reset')
        if r: o['reset'] = {'chance': int(r.get('Chance', 0)), 'price': int(r.get('Price', 0)), 'mats': mats(r.get('Materials'))}
        for s in e.get('Slots') or []:
            so = {'price': int(s.get('Price', 0)), 'mats': mats(s.get('Materials')), 'chance': int(s.get('Chance', 100000)),
                  'gradeBonus': [[int(g['Enchantgrade']), int(g.get('Chance', 0))] for g in (s.get('EnchantgradeBonus') or [])],
                  'enchants': {str(int(g['Enchantgrade'])): [[need(i['Item']), int(i.get('Chance', 0))] for i in (g.get('Items') or [])] for g in (s.get('Enchants') or [])},
                  'perfect': [{'item': need(p['Item']), 'price': int(p.get('Price', 0)), 'mats': mats(p.get('Materials'))} for p in (s.get('PerfectEnchants') or [])],
                  'upgrades': [{'enchant': need(u['Enchant']), 'upgrade': need(u['Upgrade']), 'price': int(u.get('Price', 0)), 'mats': mats(u.get('Materials'))} for u in (s.get('Upgrades') or [])]}
            o['slots'][str(int(s['Slot']))] = so
        ench.append(o)
    json.dump(ench, open(os.path.join(out, 'item_enchant.json'), 'w'), separators=(',', ':'))
    # ---- item reform
    ref = {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'item_reform.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        lst = []
        for b in e.get('BaseItems') or []:
            lst.append({'base': need(b['BaseItem']), 'minRefine': int(b.get('MinimumRefine', 0)), 'maxRefine': int(b.get('MaximumRefine', 20)),
                        'randomOptions': int(b.get('RequiredRandomOptions', 0)), 'cardsAllowed': bool(b.get('CardsAllowed', True)),
                        'mats': mats(b.get('Materials')), 'result': need(b['ResultItem']), 'changeRefine': int(b.get('ChangeRefine', 0)),
                        'clearSlots': bool(b.get('ClearSlots', False)), 'removeGrade': bool(b.get('RemoveEnchantgrade', False))})
        ref[need(e['Item'])] = lst
    json.dump(ref, open(os.path.join(out, 'item_reform.json'), 'w'), separators=(',', ':'))
    print(f'produce {len(prod)}, arrow sources {len(arr)}, enchant tables {len(ench)}, reform items {len(ref)}; unresolved names {len(missing)}: {sorted(missing)[:10]}')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
