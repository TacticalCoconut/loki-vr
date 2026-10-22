#!/usr/bin/env python3
"""
Builds Assets/StreamingAssets/data/items.tsv and quests.tsv for the Loki client.

Inputs (see README.md next to this file):
  items_raw.tsv  - dumped from the client's System/iteminfo_new.lub by dumpitems.lua (all strings hex-encoded, UTF-8)
  quests_raw.tsv - dumped from System/OngoingQuestInfoList_True.lub by dumpquests.lua (hex, cp1252 or UTF-8)
  items_en_raw.tsv, quests_en_raw.tsv (optional; wave 28, wtextc) - the same dumps of the install's English tables,
                   System/english/iteminfo_new.lub and System/english/OngoingQuestInfoList_True.lub (the LATAM install has both)
  rAthena db/re/item_db_*.yml and db/re/quest_db.yml (English names/titles + item Type, same commit as the server)

items.tsv columns (UTF-8, tab separated, no header):
  id, clientName, englishName, resourceName(hex of CP949 bytes, as used in GRF paths), slots, type(rAthena item type), description(\\n-joined, RO ^RRGGBB colour codes kept), minLevel(rAthena EquipLevelMin, 0 = none),
  clientEnglishName, clientEnglishDescription (wave 28: the install's English table; '' when it has none, or for "PlaceHolder")
quests.tsv columns:
  id, englishTitle, clientTitle, clientSummary, clientDescription(\\n-joined), clientEnglishTitle, clientEnglishSummary,
  clientEnglishDescription
Text fields lose tabs / newlines and zero-width spaces (U+200B: a few of Gravity's texts hold them; the game's font draws boxes).
"""
import sys, yaml, os

def dec(h, encs=('utf-8', 'cp1252')):
    b = bytes.fromhex(h)
    for e in encs:
        try: return b.decode(e)
        except UnicodeDecodeError: pass
    return b.decode('latin-1')

def clean(s): return s.replace('\t', ' ').replace('\r', '').replace('\n', ' ').replace('\u200b', '')

TYPES = {'Healing': 0, 'Usable': 2, 'Etc': 3, 'Armor': 4, 'Weapon': 5, 'Card': 6, 'PetEgg': 7, 'PetArmor': 8, 'Ammo': 10, 'DelayConsume': 11, 'ShadowGear': 12, 'Cash': 18}

def english(items_en_raw, quests_en_raw):
    """wave 28 (wtextc): id -> [name, description] / [title, summary, description] of the install's English tables ({} without them)."""
    ie, qe = {}, {}
    if items_en_raw:
        for line in open(items_en_raw, encoding='ascii'):
            c = line.rstrip('\n').split('\t')
            nm = clean(dec(c[1], ('utf-8',)))
            if nm.lower() == 'placeholder': nm = ''
            ie[int(c[0])] = [nm, '\\n'.join(clean(dec(x, ('utf-8',))) for x in c[7].split(',') if x)]
    if quests_en_raw:
        for line in open(quests_en_raw, encoding='ascii'):
            c = line.rstrip('\n').split('\t')
            qe[int(c[0])] = [clean(dec(c[1])), clean(dec(c[2])), '\\n'.join(clean(dec(x)) for x in c[3].split(',') if x)]
    return ie, qe

def main(items_raw, quests_raw, rathena_db, outdir, items_en_raw=None, quests_en_raw=None):
    ie, qe = english(items_en_raw, quests_en_raw)
    en, typ, minlv = {}, {}, {}
    for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
        d = yaml.safe_load(open(os.path.join(rathena_db, 're', f), encoding='utf-8'))
        for it in d.get('Body') or []:
            en[it['Id']] = it.get('Name', '')
            typ[it['Id']] = TYPES.get(it.get('Type', 'Etc'), 3)
            minlv[it['Id']] = int(it.get('EquipLevelMin', 0) or 0)
    n = 0
    with open(os.path.join(outdir, 'items.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for line in open(items_raw, encoding='ascii'):
            c = line.rstrip('\n').split('\t')
            iid = int(c[0])
            name = dec(c[1], ('utf-8',))
            res = dec(c[2], ('utf-8',)).encode('cp949').hex()
            desc = '\\n'.join(clean(dec(x, ('utf-8',))) for x in c[7].split(',') if x)
            o.write('\t'.join([str(iid), clean(name), clean(en.get(iid, '')), res, c[3], str(typ.get(iid, 3)), desc, str(minlv.get(iid, 0))] + ie.get(iid, ['', ''])) + '\n')
            n += 1
        # rAthena items the client table lacks still get their English name (no sprite/icon, no description)
        client_ids = set()
        for line in open(items_raw, encoding='ascii'): client_ids.add(int(line.split('\t', 1)[0]))
        for iid in sorted(en):
            if iid in client_ids: continue
            o.write('\t'.join([str(iid), clean(en[iid]), clean(en[iid]), '', '0', str(typ.get(iid, 3)), '', str(minlv.get(iid, 0)), '', '']) + '\n'); n += 1
    q = yaml.safe_load(open(os.path.join(rathena_db, 're', 'quest_db.yml'), encoding='utf-8'))
    qen = {x['Id']: x.get('Title', '') for x in (q.get('Body') or [])}
    m = 0
    seen = set()
    with open(os.path.join(outdir, 'quests.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for line in open(quests_raw, encoding='ascii'):
            c = line.rstrip('\n').split('\t')
            qid = int(c[0]); seen.add(qid)
            desc = '\\n'.join(clean(dec(x)) for x in c[3].split(',') if x)
            o.write('\t'.join([str(qid), clean(qen.get(qid, '')), clean(dec(c[1])), clean(dec(c[2])), desc] + qe.get(qid, ['', '', ''])) + '\n'); m += 1
        for qid, t in qen.items():
            if qid not in seen: o.write('\t'.join([str(qid), clean(t), '', '', '', '', '', '']) + '\n'); m += 1
    print('items', n, 'quests', m, 'en items', len(en), 'en quests', len(qen))

if __name__ == '__main__': main(*sys.argv[1:])
