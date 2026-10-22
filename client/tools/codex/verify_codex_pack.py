#!/usr/bin/env python3
"""Wave 28 (wcodex): an independent check of the Codex pack against rAthena (the orchestrator's 19:30 order: "Verify EVERY reverse drop
table and every 'dropped by' against rAthena's mob_db ... never against the export's own claims. Same for 'Lives in' (spawn counts) and
taming items. Report how many rows were wrong.").

Written apart from build_codex_pack.py (no code shared): it reads rAthena's renewal db and the scripts the map server loads again, by
its own parsing, and compares every row of codex/creatures.tsv and codex/items.tsv:
  drops / MVP drops  each creature's list = mob_db.yml's (item, rate, steal-protected), in order
  dropped by         each item's list = the 10 first of ALL mob_db sources of that item (rate as rAthena's), monsters with a map spawn
                     first, then by rate: every row's (monster, rate) must be a real drop of that monster
  lives in           each creature's (map, count, respawn) = the sum of the loaded scripts' monster / boss_monster lines
  taming             each creature's tame / food / egg item = pet_db.yml's
It also measures the EXPORT's own claims the same way (bestiarium.json drops / habitats / pet_info, itemarium.json dropped_by) and
reports how many of its rows rAthena does not back.
Usage: python -I verify_codex_pack.py --pack <pack root> --rathena <rathena src> [--snapshot <dir with bestiarium.json>]
"""
import argparse, collections, json, os, re, sys

import yaml


def body(path):
    with open(path, encoding="utf-8") as f:
        return (yaml.safe_load(f) or {}).get("Body") or []


def tsv(path):
    with open(path, encoding="utf-8") as f:
        rows = [l.rstrip("\n").split("\t") for l in f]
    head = rows[0]
    return [dict(zip(head, r)) for r in rows[1:]]


def conf_files(ra):
    """npc/re/scripts_main.conf and its imports, npc: lines minus delnpc: lines (rAthena npc.cpp npc_read_conf)."""
    seen, out, gone = set(), [], set()
    stack = ["npc/re/scripts_main.conf"]
    def read(conf):
        if conf in seen: return
        seen.add(conf)
        full = os.path.join(ra, conf)
        if not os.path.exists(full): return
        for line in open(full, encoding="utf-8", errors="replace"):
            m = re.match(r"\s*(npc|delnpc|import)\s*:\s*(\S+)", line)
            if not m: continue
            if m.group(1) == "import": read(m.group(2))
            elif m.group(1) == "npc": out.append(m.group(2))
            else: gone.add(m.group(2))
    read(stack[0])
    return [x for x in out if x not in gone]


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--pack", required=True); ap.add_argument("--rathena", required=True); ap.add_argument("--snapshot", default="")
    a = ap.parse_args(); ra = a.rathena
    items_by_aegis = {}
    for f in ("item_db_usable.yml", "item_db_equip.yml", "item_db_etc.yml"):
        for it in body(os.path.join(ra, "db", "re", f)): items_by_aegis[str(it["AegisName"]).lower()] = int(it["Id"])
    drops, mvp, aegis = {}, {}, {}
    for m in body(os.path.join(ra, "db", "re", "mob_db.yml")):
        mid = int(m["Id"]); aegis[str(m["AegisName"]).lower()] = mid
        def lst(k): return [(items_by_aegis.get(str(d["Item"]).lower(), -1), int(d.get("Rate", 0)), bool(d.get("StealProtected", False))) for d in (m.get(k) or [])]
        drops[mid] = lst("Drops"); mvp[mid] = lst("MvpDrops")
    # every source of every item (normal and MVP drops)
    sources = collections.defaultdict(list)
    for mid in drops:
        for iid, rate, _ in drops[mid] + mvp[mid]:
            if iid > 0: sources[iid].append((mid, rate))
    # the map spawns of the loaded scripts: "<map>,<x>,<y>[,<xs>,<ys>]<TAB>monster|boss_monster<TAB><name><TAB><id>,<amount>[,<delay1>,...]"
    spawn = collections.defaultdict(lambda: collections.defaultdict(lambda: [0, None]))
    # (the coordinates may be left out: "ra_fild11<TAB>monster<TAB>..." is valid, npc.cpp npc_parse_mob: sscanf(w1, ...) < 1 refuses only
    # an empty map field; sscanf also skips the blank some lines have before the id: ein_dun.txt:57 "ein_d02_i<TAB>monster<TAB>Red Teddybear<TAB> 20255,25,...")
    line_re = re.compile(r"^([A-Za-z0-9_@\-.]+)(?:,[^\t]*)?\t(?:boss_)?monster\t[^\t]*\t\s*(-?\d+)\s*,\s*(\d+)(?:\s*,\s*(\d+))?")
    for rel in conf_files(ra):
        full = os.path.join(ra, rel)
        if not os.path.exists(full): continue
        for line in open(full, encoding="utf-8", errors="replace"):
            if line.startswith("//"): continue
            m = line_re.match(line)
            if not m: continue
            if int(m.group(2)) in (2401, 2402, 2403) or m.group(1).startswith("int_land"): continue
            e = spawn[int(m.group(2))][m.group(1)]
            e[0] += int(m.group(3))
            if e[1] is None: e[1] = int(m.group(4) or 0)
    pets = {}
    for p in body(os.path.join(ra, "db", "re", "pet_db.yml")):
        mid = aegis.get(str(p["Mob"]).lower())
        if mid: pets[mid] = (items_by_aegis.get(str(p.get("TameItem", "")).lower(), 0), items_by_aegis.get(str(p.get("FoodItem", "")).lower(), 0), items_by_aegis.get(str(p.get("EggItem", "")).lower(), 0))

    bad = collections.Counter(); seen = collections.Counter(); ex = collections.defaultdict(list)
    def wrong(k, what):
        bad[k] += 1
        if len(ex[k]) < 3: ex[k].append(what)
    cre = tsv(os.path.join(a.pack, "codex", "creatures.tsv"))
    for r in cre:
        mid = int(r["id"])
        def parse(s): return [(int(x.split(":")[0]), int(x.split(":")[1]), x.split(":")[2] == "1") for x in s.split(";") if x]
        for col, src in (("drops", drops), ("mvp_drops", mvp)):
            seen[col + " rows"] += len(parse(r[col]))
            want = [d for d in src.get(mid, []) if d[0] > 0]
            if parse(r[col]) != want: wrong(col + " (a creature's list)", f"{mid}: pack {parse(r[col])[:3]} rAthena {want[:3]}")
        habs = [(x.split(":")[0], int(x.split(":")[1]), int(x.split(":")[2])) for x in r["habitats"].split(";") if x]
        seen["habitat rows"] += len(habs)
        for mp, n, resp in habs:
            e = spawn.get(mid, {}).get(mp)
            if e is None or e[0] != n or (e[1] or 0) != resp: wrong("habitat rows", f"{mid} {mp}: pack {n}/{resp}, scripts {e}")
        top = sorted(spawn.get(mid, {}).items(), key=lambda kv: -kv[1][0])[:10]
        if len(habs) != min(10, len(spawn.get(mid, {}))) or (top and habs and habs[0][1] != top[0][1][0]): wrong("habitat lists (count / order)", f"{mid}: pack {len(habs)}, scripts {len(spawn.get(mid, {}))}")
        tame = tuple(int(x) for x in r["pet"].split(":"))
        seen["pet rows"] += 1
        if tame != pets.get(mid, (0, 0, 0)): wrong("taming (tame / food / egg)", f"{mid}: pack {tame} pet_db {pets.get(mid)}")
    items = tsv(os.path.join(a.pack, "codex", "items.tsv"))
    for r in items:
        iid = int(r["id"])
        rows = [(int(x.split(":")[0]), int(x.split(":")[1])) for x in r["dropped_by"].split(";") if x]
        seen["dropped-by rows"] += len(rows)
        real = set(sources.get(iid, []))
        for mr in rows:
            if mr not in real: wrong("dropped-by rows", f"item {iid}: {mr} is no drop of that monster in mob_db")
        want = sorted(sources.get(iid, []), key=lambda t: (0 if spawn.get(t[0]) else 1, -t[1], t[0]))[:10]
        if rows != want: wrong("dropped-by lists (which / order)", f"item {iid}: pack {rows[:3]} rAthena {want[:3]}")
    print("THE PACK against rAthena (wrong / checked):")
    for k in ("drops rows", "mvp_drops rows", "habitat rows", "pet rows", "dropped-by rows"): print(f"  {k}: checked {seen[k]}")
    for k, n in sorted(bad.items()): print(f"  WRONG {k}: {n}   e.g. {' | '.join(ex[k])}")
    if not bad: print("  0 wrong")
    if a.snapshot:
        b = json.load(open(os.path.join(a.snapshot, "bestiarium.json"), encoding="utf-8"))["creatures"]
        it = json.load(open(os.path.join(a.snapshot, "itemarium.json"), encoding="utf-8"))["items"]
        xb = collections.Counter(); xs = collections.Counter(); xe = collections.defaultdict(list)
        def xw(k, what):
            xb[k] += 1
            if len(xe[k]) < 3: xe[k].append(what)
        for k, v in b.items():
            mid = int(k)
            for d in v.get("drops", []):
                xs["drops"] += 1
                if (d["item_id"], d["rate_raw"]) not in {(x[0], x[1]) for x in drops.get(mid, [])}: xw("drops", f"{mid}: {d['item_id']}@{d['rate_raw']}")
            for h in v.get("habitats", []):
                xs["habitats"] += 1
                e = spawn.get(mid, {}).get(h["map"])
                if e is None or e[0] != h["count"]: xw("habitats", f"{mid} {h['map']}: export {h['count']}, scripts {e[0] if e else 'none'}")
            pi = v.get("pet_info") or {}
            if pi.get("is_tameable"):
                xs["pets"] += 1
                if (pi.get("tame_item_id", 0), pi.get("food_item_id", 0), pi.get("egg_item_id", 0)) != pets.get(mid, (0, 0, 0)): xw("pets", f"{mid}")
        for k, v in it.items():
            iid = int(k); real = {(m, r) for m, r in sources.get(iid, [])}
            for d in v.get("dropped_by", []):
                xs["dropped_by"] += 1
                if (d["mob_id"], int(round(d["rate_pct"] * 100))) not in real: xw("dropped_by", f"item {iid}: {d['mob_id']}@{d['rate_pct']}%")
        print("THE EXPORT's own claims against rAthena (wrong / checked):")
        for k in ("drops", "habitats", "pets", "dropped_by"): print(f"  {k}: {xb[k]} wrong of {xs[k]}" + (f"   e.g. {' | '.join(xe[k])}" if xe[k] else ""))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
