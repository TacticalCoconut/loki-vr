"""
Client wave 2 tables (research/stylist_laphine_merge_mail_marriage.md §A-B, research/booking_searchstore_cashshop_repute.md §D).
The server sends only indexes / item ids for these windows (the official client reads its own lua / bson tables built from the
same data), so Loki keeps a copy made from rAthena's db (the commit the server runs):
  account/stylist.json        { look: [ {index, value, human:{price, item, box}|null, doram:{…}|null} ] }  from db/re/stylist.yml
                              look = Hair_Color | Hair | Clothes_Color | Head_Top | Head_Mid | Head_Bottom | Body2; Head_* values are item ids
  craft/laphine_synthesis.json { recipe item id: {group, count, minRefine, maxRefine, needs:[[item, amount], …]} } from db/re/laphine_synthesis.yml
  craft/laphine_upgrade.json   { upgrade item id: {group, result, resultMin, resultMax, minRefine, maxRefine, options, cards, targets:[item…]} }
                              from db/re/laphine_upgrade.yml
  account/reputation.json      { "types": [ {id, name, min, max} ], "groups": [ {id, name, types:[…]} ] } from db/re/reputation.yml +
                              db/re/reputation_group.yml
  account/battlegrounds.json   [ {id, name, minPlayers, minLevel, maxLevel} ] from db/battleground_db.yml
AegisNames are resolved through db/re/item_db_*.yml. Usage: build_wave2_tables.py <rathena root> <StreamingAssets/data dir>
"""
import json, os, sys, yaml

MAX_REFINE = 20  # mmo.hpp MAX_REFINE (the yml defaults use it)

def aegis_map(root):
    m = {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []: m[it['AegisName']] = it['Id']; m[it['AegisName'].lower()] = it['Id']
    return m

def main(root, out):
    ids = aegis_map(root)
    missing = set()
    def need(name):
        if name is None: return 0
        v = ids.get(name, ids.get(str(name).lower(), 0))
        if v == 0: missing.add(name)
        return v
    os.makedirs(os.path.join(out, 'account'), exist_ok=True); os.makedirs(os.path.join(out, 'craft'), exist_ok=True)
    # ---- stylist (npc.cpp:126-390: Index int16 != 0, Value int32 or item for Head_*, CostsHuman / CostsDoram {Price, RequiredItem, RequiredItemBox})
    sty = {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'stylist.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        look = e['Look']
        head = look.startswith('Head_')
        lst = sty.setdefault(look, [])
        for o in e.get('Options') or []:
            def cost(c):
                if c is None: return None
                return {'price': int(c.get('Price', 0)), 'item': need(c.get('RequiredItem')) if c.get('RequiredItem') else 0, 'box': need(c.get('RequiredItemBox')) if c.get('RequiredItemBox') else 0}
            lst.append({'index': int(o['Index']), 'value': need(o['Value']) if head else int(o['Value']), 'human': cost(o.get('CostsHuman')), 'doram': cost(o.get('CostsDoram'))})
    json.dump(sty, open(os.path.join(out, 'account', 'stylist.json'), 'w'), separators=(',', ':'))
    # ---- laphine synthesis (itemdb.cpp:1531-1694)
    syn = {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'laphine_synthesis.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        syn[need(e['Item'])] = {'group': e.get('RewardGroup', ''), 'count': int(e.get('RequiredRequirementsCount', 1)),
                                'minRefine': int(e.get('MinimumRefine', 0)), 'maxRefine': int(e.get('MaximumRefine', MAX_REFINE)),
                                'needs': [[need(r['Item']), int(r.get('Amount', 1))] for r in (e.get('Requirements') or [])]}
    json.dump(syn, open(os.path.join(out, 'craft', 'laphine_synthesis.json'), 'w'), separators=(',', ':'))
    # ---- laphine upgrade (itemdb.cpp:1702-1909)
    upg = {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'laphine_upgrade.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        upg[need(e['Item'])] = {'group': e.get('RandomOptionGroup', ''), 'result': int(e.get('ResultRefine', 0)),
                                'resultMin': int(e.get('ResultRefineMinimum', 0)), 'resultMax': int(e.get('ResultRefineMaximum', 0)),
                                'minRefine': int(e.get('MinimumRefine', 0)), 'maxRefine': int(e.get('MaximumRefine', MAX_REFINE)),
                                'options': int(e.get('RequiredRandomOptions', 0)), 'cards': bool(e.get('CardsAllowed', False)),
                                'targets': [need(t['Item']) for t in (e.get('TargetItems') or [])]}
    json.dump(upg, open(os.path.join(out, 'craft', 'laphine_upgrade.json'), 'w'), separators=(',', ':'))
    # ---- reputation (pc.cpp:279-379) and its groups (map-server-generator, pc.cpp:452-520)
    rep = {'types': [], 'groups': []}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'reputation.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        rep['types'].append({'id': int(e['Id']), 'name': e.get('Name', ''), 'min': int(e.get('Minimum', 0)), 'max': int(e.get('Maximum', 0))})
    for gp in (os.path.join(root, 'db', 're', 'reputation_group.yml'), os.path.join(root, 'db', 're', 'generator', 'reputation_group.yml')):
        if not os.path.exists(gp): continue
        d = yaml.safe_load(open(gp, encoding='utf-8'))
        for e in d.get('Body') or []:
            rep['groups'].append({'id': int(e['Id']), 'name': e.get('Name', ''), 'types': [int(x) for x in (e.get('ReputeList') or e.get('Reputations') or [])]})
    json.dump(rep, open(os.path.join(out, 'account', 'reputation.json'), 'w'), separators=(',', ':'))
    # ---- battlegrounds (db/battleground_db.yml): the queue names the client must send, matched case-insensitively (battleground.cpp:410-420)
    bgs = []
    d = yaml.safe_load(open(os.path.join(root, 'db', 'battleground_db.yml'), encoding='utf-8'))
    for e in d.get('Body') or []:
        bgs.append({'id': int(e['Id']), 'name': e.get('Name', ''), 'minPlayers': int(e.get('MinPlayers', 1)), 'minLevel': int(e.get('MinLevel', 1)), 'maxLevel': int(e.get('MaxLevel', 0))})
    json.dump(bgs, open(os.path.join(out, 'account', 'battlegrounds.json'), 'w'), separators=(',', ':'))
    print(f'stylist looks {len(sty)} ({sum(len(v) for v in sty.values())} options), laphine synthesis {len(syn)}, upgrade {len(upg)}, '
          f'reputation {len(rep["types"])} types / {len(rep["groups"])} groups, battlegrounds {len(bgs)}; unresolved names {len(missing)}: {sorted(map(str, missing))[:10]}')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
