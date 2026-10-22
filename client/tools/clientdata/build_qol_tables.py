#!/usr/bin/env python3
r"""
Builds the QoL tables in Assets/StreamingAssets/data (UTF-8, tab separated, no header):

  emotions.tsv   id, actIndex, label, aliases        — from the client's data\luafiles514\lua files\emotion\emotionlist.lub
                 (InsertEmotionListTable(id, msgStringId, actIndex); label = msgstringtable[msgStringId], the chat command).
                 The act index is the action in data\sprite\이팩트\emotion.act that 0x00c0 type = id plays. 1000 = no sprite.
  stateicons.tsv efst, iconHex (hex of the CP949 bytes of data\texture\effect\<file>), colour (0 gold 1 red 2 blue 3 green 4 white)
                 — EFST ≥ 70 from stateiconimginfo.lub (StateIconImgList[priority][efst] = file); EFST < 70 from
                 roBrowserLegacy DB/Status/StatusInfo.js, which carries the client's hard-coded list (colour 4).
  itemflags.tsv  id, weight, tradeFlags (1 NoDrop 2 NoTrade 4 NoSell 8 NoStorage 16 NoCart 32 NoGuildStorage 64 NoMail 128 NoAuction),
                 buy price, sell price, flags (1 = BuyingStore) — from rAthena db/re/item_db_*.yml (Weight, Trade).
  warps.tsv      map, x, y, spanX, spanY, destMap, destX, destY, name — every `warp`/`warp2` NPC line the server loads
                 (npc/re/scripts_main.conf imports, recursively), for cross-map navigation routing.

Usage: build_qol_tables.py <emotionlist rows> <stateicon rows> <StatusInfo.js> <StatusConst.js> <rathena root> <outdir>
  emotionlist rows: `lua5.1 dump_emotions.lua emotionlist.lub` (after lub32to64.py), lines "id | msgId | act"
  stateicon rows:   `lua5.1 dump_stateicons.lua` in a folder with converted efstids.lub + stateiconimginfo.lub, lines "efst name pri file"
"""
import sys, os, re, yaml

def main(emo_rows, si_rows, statusinfo_js, statusconst_js, rathena, outdir):
    # ---- emotions
    msgs = {}
    for line in open(os.path.join(outdir, 'msgstrings.tsv'), encoding='utf-8'):
        c = line.rstrip('\n').split('\t')
        if c and c[0].isdigit(): msgs[int(c[0])] = c[1]
    aliases = {}
    rows = []
    for line in open(emo_rows, encoding='utf-8', errors='replace'):
        line = line.strip()
        m = re.match(r'^CALL InsertEmotionMsgListTable\((\d+), (.*)\)$', line)
        if m:
            eid, txt = int(m.group(1)), m.group(2)
            if not txt.startswith('MSG') and '�' not in txt: aliases.setdefault(eid, []).append(txt)
            continue
        m = re.match(r'^(\d+) \| (-?\d+) \| (\d+)$', line)
        if m: rows.append((int(m.group(1)), int(m.group(2)), int(m.group(3))))
    rows.sort()
    with open(os.path.join(outdir, 'emotions.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for eid, msg, act in rows:
            label = msgs.get(msg, '') if msg >= 0 else ''
            o.write(f"{eid}\t{act}\t{label}\t{' '.join(aliases.get(eid, []))}\n")
    print('emotions', len(rows))
    # ---- state icons
    icons = {}
    for line in open(si_rows, encoding='latin-1'):  # file names are raw CP949 bytes (a few are Korean)
        c = line.rstrip('\n').split('\t')
        if len(c) < 4: continue
        efst, pri, f = int(c[0]), int(c[2]), c[3]
        icons[efst] = (f.encode('latin-1').hex(), pri)
    consts = {}
    for m in re.finditer(r'^\s*([A-Z0-9_]+)\s*:\s*(\d+)\s*,?', open(statusconst_js, encoding='utf-8').read(), re.M):
        consts[m.group(1)] = int(m.group(2))
    js = open(statusinfo_js, encoding='utf-8').read()
    n_rb = 0
    for m in re.finditer(r"StatusInfo\[SC\.([A-Z0-9_]+)\]\s*=\s*\{(.*?)\n\};", js, re.S):
        name, body = m.group(1), m.group(2)
        im = re.search(r"icon:\s*'((?:\\x[0-9a-fA-F]{2}|[^'\\])*)'", body)
        if not im or name not in consts: continue
        raw = im.group(1)
        b = bytes(int(h, 16) if h else ord(ch) for h, ch in re.findall(r"\\x([0-9a-fA-F]{2})|(.)", raw))
        efst = consts[name]
        if efst not in icons: icons[efst] = (b.hex(), 4); n_rb += 1
    with open(os.path.join(outdir, 'stateicons.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for efst in sorted(icons): o.write(f"{efst}\t{icons[efst][0]}\t{icons[efst][1]}\n")
    print('stateicons', len(icons), 'from roBrowser', n_rb)
    # ---- item flags
    TRADE = {'NoDrop': 1, 'NoTrade': 2, 'NoSell': 4, 'NoStorage': 8, 'NoCart': 16, 'NoGuildStorage': 32, 'NoMail': 64, 'NoAuction': 128}
    n = 0
    with open(os.path.join(outdir, 'itemflags.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for f in ('item_db_usable.yml', 'item_db_equip.yml', 'item_db_etc.yml'):
            d = yaml.safe_load(open(os.path.join(rathena, 'db', 're', f), encoding='utf-8'))
            for it in d.get('Body') or []:
                tr = it.get('Trade') or {}
                mask = sum(v for k, v in TRADE.items() if tr.get(k))
                w = int(it.get('Weight', 0) or 0)
                buy = int(it.get('Buy', 0) or 0)          # NPC buy price; the sell price is Buy / 2 unless Sell is given (itemdb.cpp)
                sell = int(it.get('Sell', buy // 2) or 0)
                fl = (1 if (it.get('Flags') or {}).get('BuyingStore') else 0)
                if mask == 0 and w == 0 and buy == 0 and fl == 0: continue
                o.write(f"{it['Id']}\t{w}\t{mask}\t{buy}\t{sell}\t{fl}\n"); n += 1
    print('itemflags', n)
    # ---- warps
    files, seen = [], set()
    def conf(path):
        for line in open(os.path.join(rathena, path), encoding='utf-8', errors='replace'):
            line = line.split('//')[0].strip()
            if line.startswith('import:'): conf(line[7:].strip())
            elif line.startswith('npc:'):
                p = line[4:].strip()
                if p not in seen: seen.add(p); files.append(p)
    conf('npc/re/scripts_main.conf')
    warps = []
    rx = re.compile(r'^([a-z0-9_@]+),(\d+),(\d+),\d+\twarp2?\t([^\t]+)\t(\d+),(\d+),([a-z0-9_@]+),(\d+),(\d+)', re.I)
    for p in files:
        fp = os.path.join(rathena, p)
        if not os.path.isfile(fp): continue
        for line in open(fp, encoding='utf-8', errors='replace'):
            m = rx.match(line.rstrip('\n'))
            if m: warps.append(m.groups())
    with open(os.path.join(outdir, 'warps.tsv'), 'w', encoding='utf-8', newline='\n') as o:
        for w in warps: o.write('\t'.join([w[0], w[1], w[2], w[4], w[5], w[6], w[7], w[8], w[3]]) + '\n')
    print('warps', len(warps), 'from', len(files), 'script files')

if __name__ == '__main__': main(*sys.argv[1:])
