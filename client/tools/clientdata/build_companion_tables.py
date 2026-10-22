"""
Pet and homunculus tables for Loki (research/pet_homun_merc_packets.md §1.6, §2.4). The server never sends pet_db / homunculus_db data,
so the client ships its own copy. Writes Assets/StreamingAssets/data/companions/*.json from rAthena's db:
  pets.json   { mob id: {name, tame, egg, equip, food, fullness, hungryDelay, hungerInc, autoFeed, special,
                          evo: [{target: mob id, egg: target's egg item id, items: [[item, amount], …]}]} }     from db/re/pet_db.yml
  homun.json  { class id: {name, food, evolution: class id or 0} }                                           from db/re/homunculus_db.yml
Monster AegisNames come from db/re/mob_db.yml, item AegisNames from db/re/item_db_*.yml, homunculus classes from the
e_homun_classid enum in src/map/homunculus.hpp ("MER_" + Class, homunculus.cpp:1515). Defaults follow pet.cpp's parser
(HungryDelay 60 s, HungerIncrease 20, SpecialPerformance true, AllowAutoFeed false; homunculus Food Pet_Food).
Usage: build_companion_tables.py <rathena root> <out dir>
"""
import json, os, re, sys, yaml

def aegis_items(root):
    m = {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(root, 'db', 're', f), encoding='utf-8'))
        for it in d.get('Body') or []: m[it['AegisName'].lower()] = it['Id']
    return m

def aegis_mobs(root):
    m, names = {}, {}
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'mob_db.yml'), encoding='utf-8'))
    for mb in d.get('Body') or []:
        m[mb['AegisName'].lower()] = mb['Id']; names[mb['Id']] = mb.get('Name', mb['AegisName'])
    return m, names

def homun_classes(root):
    src = open(os.path.join(root, 'src', 'map', 'homunculus.hpp'), encoding='utf-8').read()
    body = src[src.index('enum e_homun_classid'):]; body = body[:body.index('};')]
    out, cur = {}, None
    for name, val in re.findall(r'(MER_\w+)\s*(?:=\s*(\d+))?\s*,', body):
        cur = int(val) if val else cur + 1
        out[name[4:].lower()] = cur
    return out

def main(root, out):
    os.makedirs(out, exist_ok=True)
    items = aegis_items(root); mobs, mobnames = aegis_mobs(root); classes = homun_classes(root)
    missing = set()
    def item(n):
        if not n: return 0
        v = items.get(str(n).lower(), 0)
        if v == 0: missing.add(n)
        return v
    def mob(n):
        v = mobs.get(str(n).lower(), 0)
        if v == 0: missing.add('mob:' + str(n))
        return v
    # ---- pets
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'pet_db.yml'), encoding='utf-8'))
    body = d.get('Body') or []
    egg_of = {mob(p['Mob']): item(p.get('EggItem')) for p in body}
    pets = {}
    for p in body:
        mid = mob(p['Mob'])
        pets[mid] = {
            'name': mobnames.get(mid, p['Mob']), 'tame': item(p.get('TameItem')), 'egg': item(p.get('EggItem')), 'equip': item(p.get('EquipItem')),
            'food': item(p.get('FoodItem')), 'fullness': int(p.get('Fullness', 0)), 'hungryDelay': int(p.get('HungryDelay', 60)),
            'hungerInc': int(p.get('HungerIncrease', 20)), 'autoFeed': bool(p.get('AllowAutoFeed', False)), 'special': bool(p.get('SpecialPerformance', True)),
            'evo': [{'target': mob(e['Target']), 'egg': egg_of.get(mob(e['Target']), 0),
                     'items': [[item(i['Item']), int(i.get('Amount', 1))] for i in (e.get('ItemRequirements') or [])]} for e in (p.get('Evolution') or [])],
        }
    json.dump(pets, open(os.path.join(out, 'pets.json'), 'w'), separators=(',', ':'))
    # ---- homunculi
    d = yaml.safe_load(open(os.path.join(root, 'db', 're', 'homunculus_db.yml'), encoding='utf-8'))
    hom = {}
    for h in d.get('Body') or []:
        cid = classes.get(str(h['Class']).lower(), 0)
        if cid == 0: missing.add('class:' + str(h['Class'])); continue
        hom[cid] = {'name': h.get('Name', h['Class']), 'food': item(h.get('Food', 'Pet_Food')), 'evolution': classes.get(str(h.get('EvolutionClass', '')).lower(), 0)}
    json.dump(hom, open(os.path.join(out, 'homun.json'), 'w'), separators=(',', ':'))
    print(f'pets {len(pets)} ({sum(len(p["evo"]) for p in pets.values())} evolutions), homunculi {len(hom)}; unresolved {len(missing)}: {sorted(missing)[:12]}')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
