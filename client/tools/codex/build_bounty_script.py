#!/usr/bin/env python3
"""Wave 28 (wcxp): the Codex's bounties, the server's NPC script (design note, 2026-10-15, through the orchestrator):

  "Scanning a creature gives it a 'Codex Bounty'. Killing it once claims the bounty and gives you the EXP reward."
  "The minimum amount of EXP granted ... is 2% of the total exp bar to the next level ... The maximum is 4%."
  "Claiming every creature from a 10 level range (so from level 1 to 10, 11 to 20 for example), gives you a ... reward":
  "75% of the total amount needed for the highest bracket level to progress to its next level + a flat 5% of the Player's
  current level's necessary progress."

This writes ONE rAthena NPC script (the same file runs on Loki Host's rAthena copy and on the SpacetimeDB module, which runs
rAthena's scripts): its rules are fixed text below, its TABLES are generated from the data:
  - the creatures the Codex can meet (the book's own rule, Game/CodexData.cs LoadMetable, on the text pack's creatures.tsv: a map
    spawn of a loaded script, a loaded script's monster command, an item's summon group, or a summon of one already in the set),
    each with its level and danger (0 normal, 1 boss = rAthena's Class Boss, 2 MVP = the Mvp mode: mob.cpp:377-384);
  - the 10-level brackets (1-10, 11-20, ...): the creatures with a REGULAR map spawn (not on quest / job-change / instance maps
    only: quest_maps), i.e. a `monster` / `boss_monster` line of a
    script the renewal map server loads (npc/re/scripts_main.conf and its imports), on a map it loads (conf/maps_athena.conf),
    WITHOUT an event label (npc.cpp:5235 w4's 5th field: such a monster runs its own event instead of OnNPCKillEvent, mob.cpp:
    3591-3610, so its bounty could never be claimed); creatures of the same name in one bracket count once;
  - the highest base level of every job's EXP table (db/re/job_exp.yml MaxBaseLevel; job ids from server/tools/script_constants.tsv).

Usage: python -I build_bounty_script.py --pack <text pack root> --rathena <rathena src> --consts <script_constants.tsv> --out <file>
       [--report]   (prints the brackets: creatures, groups, and the ones left out)
       [--members <pack root>/codex/brackets.tsv]   (wave 28, wcodex: the members for the book's bracket hints, then
                    client/tools/textpack/build_textpack.py --manifest <pack root>)
"""
import argparse, collections, hashlib, os, re, statistics, sys

import yaml  # PyYAML (the PC's Python has 6.0.3)

CLASSES = ["Normal", "Boss", "Guardian", "Battlefield", "Event"]
# a training dummy: no Base EXP and at least this much HP (rAthena's "Dummy 10" 2408 / 2413: 99,999,999; the training grounds'
# dummies 21064-22668: 2,000,000,000): no player can be expected to kill one, so it never stands in a bracket (FLAGGED rule)
DUMMY_HP = 99999999
QUEST_MAP = re.compile(r"^(que_|job_|job\d|.*_q$|.*_i$)")


def read_pack(pack):
    """creatures.tsv of the Codex's text pack (client/tools/codex/build_codex_pack.py): id -> dict."""
    path = os.path.join(pack, "codex", "creatures.tsv")
    rows = {}
    with open(path, encoding="utf-8") as f:
        head = f.readline().rstrip("\n").split("\t")
        col = {n: i for i, n in enumerate(head)}
        for line in f:
            c = line.rstrip("\n").split("\t")
            if len(c) < len(head) - 1: continue
            mid = int(c[col["id"]])
            rows[mid] = dict(id=mid, name=c[col["name"]], level=int(c[col["level"]]), cls=int(c[col["class"]]), mvp=c[col["mvp"]] == "1",
                             hp=int(c[col["hp"]]), bexp=int(c[col["base_exp"]]),
                             habitats=c[col["habitats"]], origin=c[col["origin"]] if col["origin"] < len(c) else "")
    return rows, hashlib.sha256(open(path, "rb").read()).hexdigest()


CHAMPION_BASE_OVERRIDES = {
    2612: 1880,  # Elusive Wood Goblin -> Leshij
    2626: 1882,  # Elusive Vavayaga -> Baba-Yaga
    2669: 1628,  # Furious Lamp Rey -> Sedora
    2704: 1402,  # Swift Poison Toad -> Poisonous Toad
    2733: 2481,  # Elusive Wraith Dead (Nig -> Nightmare Wraith Dead
    2734: 2480,  # Furious Wanderer (Nightm -> Nightmare Wanderer
    2735: 1988,  # Solid Nephentes -> Nepenthes
    2776: 2398,  # Baby Poring Ringleader -> Little Poring
    2779: 1881,  # Swift Les -> Lesavka
    2783: 2416,  # Elusive Eremes -> Assassin Eremes
    2799: 2083,  # Swift Uni-horn Scaraba -> One-Horned Scaraba
    2906: 2151,  # Swift Rafflesia Arnoldi -> Rafflesia Alnoldi
}


def is_excluded(m, r):
    """design note (2026-10-17 item 44): Poring in int_land (no drops) should not be scannable or in the Bestiarium."""
    if m in (2401, 2402, 2403): return True
    habs = [h.split(":")[0] for h in r["habitats"].split(";") if h]
    if habs and all(h.startswith("int_land") for h in habs) and not r.get("drops", "1"):
        return True
    return False


def strip_modifier(mid, name):
    if 2603 <= mid <= 2913:
        for p in ("Furious ", "Elusive ", "Swift ", "Solid "):
            if name.startswith(p): return name[len(p):].strip()
        s = re.sub(r"\s+Ringlea(?:d(?:e(?:r)?)?)?$", "", name)
        if s != name: return s.strip()
    if name.startswith("Enriched "):
        return name[len("Enriched "):].strip()
    return None


def canonical_group_name(mid, rows, base_names):
    if mid in CHAMPION_BASE_OVERRIDES and CHAMPION_BASE_OVERRIDES[mid] in rows:
        return rows[CHAMPION_BASE_OVERRIDES[mid]]["name"].strip().lower()
    name = rows[mid]["name"].strip()
    base = strip_modifier(mid, name)
    if base is not None:
        bl = base.lower()
        if bl in base_names: return bl
        cands = sorted(k for k in base_names if k.startswith(bl))
        if cands: return cands[0]
    return name.lower()


def metable(rows):
    """Game/CodexData.cs LoadMetable, the same rule on the same columns (the book lists exactly these)."""
    s = {m for m, r in rows.items() if not is_excluded(m, r) and (r["habitats"] or "script" in r["origin"] or "item:" in r["origin"])}
    more = True
    while more:
        more = False
        for m, r in rows.items():
            if m in s or is_excluded(m, r): continue
            for part in r["origin"].split("|"):
                if part.startswith("mob:") and any(x.isdigit() and int(x) in s for x in part[4:].split(",")):
                    s.add(m); more = True; break
    return s


def loaded_npc_files(ra):
    """npc/re/scripts_main.conf and what it imports (npc: / delnpc: / import:), first registration wins (npc_addsrcfile)."""
    files, seen = [], set()

    def walk(conf):
        p = os.path.join(ra, conf)
        if conf in seen or not os.path.isfile(p): return
        seen.add(conf)
        for line in open(p, encoding="utf-8", errors="replace"):
            if line.startswith("//"): continue
            i = line.find("//")
            if i >= 0: line = line[:i]
            m = re.match(r"^(npc|delnpc|import):\s*(\S+)", line.strip())
            if not m: continue
            kind, path = m.group(1), m.group(2)
            if kind == "import": walk(path)
            elif kind == "npc":
                if path not in files: files.append(path)
            elif path in files: files.remove(path)
    walk("npc/re/scripts_main.conf")
    return files


def loaded_maps(ra):
    """conf/maps_athena.conf (and its imports): the maps the server loads (npc_parse_mob skips a spawn on any other: npc.cpp:5244-5250)."""
    maps, seen = set(), set()

    def walk(conf):
        p = os.path.join(ra, conf)
        if conf in seen or not os.path.isfile(p): return
        seen.add(conf)
        for line in open(p, encoding="utf-8", errors="replace"):
            line = line.split("//")[0].strip()
            m = re.match(r"^(map|delmap|import):\s*(\S+)", line)
            if not m: continue
            if m.group(1) == "map": maps.add(m.group(2))
            elif m.group(1) == "delmap": maps.discard(m.group(2))
            else: walk(m.group(2))
    walk("conf/maps_athena.conf")
    return maps


def regular_spawn_maps(ra, aegis_to_id):
    """mob id -> the maps of its regular spawns (no event label: mob.cpp:484-485), as regular_spawns reads them."""
    maps = loaded_maps(ra); out = collections.defaultdict(set)
    for rel in loaded_npc_files(ra):
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        for line in open(p, encoding="utf-8", errors="replace"):
            if line.lstrip().startswith("//"): continue
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 4 or c[1].strip() not in ("monster", "boss_monster"): continue
            mapname = c[0].split(",")[0].strip()
            if mapname not in maps: continue
            w4 = c[3].split(","); sprite = w4[0].strip()
            try: mid = int(sprite, 0)
            except ValueError: mid = aegis_to_id.get(sprite.lower())
            ev = w4[4].strip() if len(w4) > 4 else ""
            if mid is not None and len(ev) < 4: out[mid].add(mapname)
    return out


def regular_spawns(ra, aegis_to_id):
    """mob id -> (spawn lines without an event, lines with one), over the loaded scripts, as npc.cpp npc_parse_mob reads them."""
    maps = loaded_maps(ra)
    plain, evented = collections.Counter(), collections.Counter()
    for rel in loaded_npc_files(ra):
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        for line in open(p, encoding="utf-8", errors="replace"):
            if line.lstrip().startswith("//"): continue
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 4 or c[1].strip() not in ("monster", "boss_monster"): continue
            mapname = c[0].split(",")[0].strip()
            if mapname not in maps: continue
            w4 = c[3].split(",")
            sprite = w4[0].strip()
            try: mid = int(sprite, 0)
            except ValueError: mid = aegis_to_id.get(sprite.lower())
            if mid is None: continue
            # mob.cpp:484-485: an event name shorter than 4 characters is no event (the MVPs' lines end in ",1")
            ev = w4[4].strip() if len(w4) > 4 else ""
            (evented if len(ev) >= 4 else plain)[mid] += 1
    return plain, evented


QUEST_SCRIPTS = re.compile(r"^npc/(re/)?(quests|jobs|instances|events)/", re.I)


def quest_maps(ra):
    """design note: "Quest-only creatures do not count towards the brackets." A map is a quest / job-change / instance map when it is an
    instance's map (db/re/instance_db.yml Enter / AdditionalMaps: instance.cpp makes copies of them) or when every NPC, warp and
    shop the loaded scripts place ON it comes from npc/(re/)quests, jobs, instances or events (so only those stories lead there)."""
    placers = collections.defaultdict(set)
    for rel in loaded_npc_files(ra):
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        for line in open(p, encoding="utf-8", errors="replace"):
            if line.lstrip().startswith("//"): continue
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 3: continue
            kind = c[1].strip(); mp = c[0].split(",")[0].strip()
            if mp != "-" and (kind in ("warp", "warp2", "script", "shop", "cashshop", "itemshop", "pointshop", "marketshop") or kind.startswith("duplicate")):
                placers[mp].add(rel)
    out = {}
    with open(os.path.join(ra, "db", "re", "instance_db.yml"), encoding="utf-8") as f:
        for e in (yaml.safe_load(f) or {}).get("Body") or []:
            if e.get("Enter"): out[str(e["Enter"]["Map"])] = "instance_db"
            for m in e.get("AdditionalMaps") or {}: out[str(m)] = "instance_db"
    for mp, rels in placers.items():
        if mp not in out and rels and all(QUEST_SCRIPTS.match(r) for r in rels): out[mp] = sorted(rels)[0]
    return out


CONFLICT_FLAGS = ("gvg", "gvg_castle", "gvg_dungeon", "gvg_te", "gvg_te_castle", "pvp", "battleground")


def conflict_maps(ra):
    """design note: "Creatures only on guild castles, guild dungeons, PvP or WoE maps" leave the brackets. A map is one of those when a loaded
    script (npc/re/scripts_main.conf's imports: npc/mapflag/gvg.txt, pvp.txt, battleground.txt, npc/re/mapflag/gvg.txt ...) sets
    one of CONFLICT_FLAGS on it and not "off" (npc.cpp npc_parse_mapflag: a 4th field "off" clears it; npc/mapflag/nopvp.txt is
    such a list), or db/(re/)castle_db.yml names it as a castle."""
    out = {}
    for rel in loaded_npc_files(ra):
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        for line in open(p, encoding="utf-8", errors="replace"):
            if line.lstrip().startswith("//"): continue
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 3 or c[1].strip() != "mapflag": continue
            flag = c[2].strip().lower(); val = c[3].strip().lower() if len(c) > 3 else ""
            if flag in CONFLICT_FLAGS and val != "off": out.setdefault(c[0].strip(), flag)
    for f in ("castle_db.yml", os.path.join("re", "castle_db.yml")):
        q = os.path.join(ra, "db", f)
        if not os.path.isfile(q): continue
        with open(q, encoding="utf-8") as fh:
            for e in (yaml.safe_load(fh) or {}).get("Body") or []:
                if e.get("Map"): out.setdefault(str(e["Map"]), "castle_db")
    return out


def zeny_ratios(ra, meet, rows, plain, maxes):
    """design note: at the job's highest base level "The EXP is converted to an appropriate amount of Zennys instead." The rule (FLAGGED):
    the Zeny the kills that EXP stands for would bring by selling their drops: per job group's highest level L, the median over the
    normal creatures of levels L-25..L with a regular spawn of (expected NPC sell value of one kill's drops: rate x item_db Sell, else
    Buy / 2) / (its mob_db BaseExp). Zeny per 1,000,000 base EXP (the script divides by the server's base_exp_rate)."""
    sell = {}
    for f in ("item_db_usable.yml", "item_db_equip.yml", "item_db_etc.yml"):
        with open(os.path.join(ra, "db", "re", f), encoding="utf-8") as fh:
            for it in (yaml.safe_load(fh) or {}).get("Body") or []:
                buy = int(it.get("Buy", 0) or 0); sv = it.get("Sell")
                sell[str(it["AegisName"]).lower()] = int(sv) if sv is not None else buy // 2
    with open(os.path.join(ra, "db", "re", "mob_db.yml"), encoding="utf-8") as fh:
        mobs = {int(m["Id"]): m for m in (yaml.safe_load(fh) or {}).get("Body") or []}
    out = {}
    for top in sorted(set(maxes)):
        r = []
        for mid in meet:
            m = mobs.get(mid); row = rows[mid]
            if not m or plain.get(mid, 0) < 1 or row["mvp"] or row["cls"] != 0: continue
            lv, be = int(m.get("Level", 1)), int(m.get("BaseExp", 0))
            if top - 25 <= lv <= top and be > 0:
                r.append(sum(int(d.get("Rate", 0)) / 10000.0 * sell.get(str(d["Item"]).lower(), 0) for d in (m.get("Drops") or [])) / be)
        if r: out[top] = (int(round(statistics.median(r) * 1000000)), len(r))
    return out


def aegis_ids(ra):
    out = {}
    with open(os.path.join(ra, "db", "re", "mob_db.yml"), encoding="utf-8") as f:
        for m in (yaml.safe_load(f) or {}).get("Body") or []:
            out[str(m["AegisName"]).lower()] = int(m["Id"])
    return out


def job_max_levels(ra, consts):
    """job id -> MaxBaseLevel (db/re/job_exp.yml; pc.cpp:14029-14043: a group without it keeps MAX_LEVEL; job names are JOB_<NAME>)."""
    ids = {}
    for line in open(consts, encoding="utf-8"):
        c = line.rstrip("\n").split("\t")
        if len(c) >= 2 and c[0].startswith("JOB_") and c[1].lstrip("-").isdigit(): ids[c[0].upper()] = int(c[1])
    out, unknown = {}, []
    with open(os.path.join(ra, "db", "re", "job_exp.yml"), encoding="utf-8") as f:
        body = (yaml.safe_load(f) or {}).get("Body") or []
    for g in body:
        mx = g.get("MaxBaseLevel")
        for name, on in (g.get("Jobs") or {}).items():
            if not on: continue
            jid = ids.get("JOB_" + str(name).upper())
            if jid is None: unknown.append(str(name)); continue
            if mx is not None: out[jid] = int(mx)
            else: out.setdefault(jid, 0)
    return {j: m for j, m in out.items() if m}, unknown


SCRIPT_HEAD = r"""//===== Loki: the Codex's bounties (wave 28, wcxp) ================================
//= GENERATED by client/tools/codex/build_bounty_script.py (do not edit: run it again). The same file runs on Loki Host's
//= rAthena copy (tools/lokihost/Core/CodexBounty.cs installs it at every Start) and on the SpacetimeDB module (its overlay).
//= Data: {source}
//=
//= design note (2026-10-15/16): a SCAN places a bounty; a KILL completes it and FIXES its amount (the level then); the CHARACTER claims it
//= in the Codex (a button): Base EXP, or Zeny when it is at its job's highest base level at the claim. Bounties belong to the
//= CHARACTER (the Codex's entries stay the account's, on the client); a 10-level bracket whose every name is claimed fixes a bonus.
//= A Loki client whispers "NPC:LokiCodex" (clif.cpp clif_parse_WisMessage runs OnWhisperGlobal; '#' splits @whispervarN$):
//=   "s"        the character's state: every bounty open / completed (its fixed amount) / claimed, every bracket; "LCX1 ..." lines.
//=   "n#<id>"   a creature of kind <id> came into the client's sight. The server's own check: a LIVING one within the player's
//=              sight (area_size + 4 cells: getareaunits, getunitdata). A quest / event creature (.q) is placed at once ("P") and
//=              watched; any other kind's FIRST SIGHT is recorded (the scan's clock starts: @cxt, per character, until logout).
//=   "b#<id>"   a finished scan: placed (state 1) only when the server saw this character near a living one at least the scan's
//=              time ago (S_Need: the client's CodexData.LookSeconds: 1.5 s + 0.03 s x level (level capped at 200, 15 s at most);
//=              a boss 20 s, less 30 s x the share of its HP it lost since that first sight), and one is still near (or the one
//=              it watched died). Too early, never seen, already placed: refused without a word.
//=   "x#<gid>"  the client saw a monster die: the server's own record decides: rAthena's killedgid (this character's own last
//=              kill, set before a monster's own event label runs instead of OnNPCKillEvent: mob.cpp:3591-3604).
//=   "c#<id>"   the Codex's Claim button: a COMPLETED bounty pays its stored amount once (S_Pay).
//=   "k#<b>"    a bracket's Claim button: its fixed bonus, once.
//=   "p"        nothing but the watch below (the client sends it when the book opens).
//= A KILL completes an open bounty: OnNPCKillEvent (rAthena runs it for the player with the most damage, its homunculus /
//= mercenary / pet / elemental included: mob.cpp:3046-3057, 3605-3608), the killedgid report above, and (S_Party) every online
//= member of the killer's party within the killer's sight, alive, whose base level is at least the creature's level - 10.
//= Quest and event creatures (.q): watched units (the ones near the character when it saw them) that are gone complete it,
//= whoever killed them (S_Sweep, at every request of that character).
//= Amount at the kill (S_Complete): NextBaseExp (at the job's highest level the table's last real requirement) x share / 10000,
//= at least 1; the share (S_Share, 1/100 %): floor 200 normal, 300 boss, 350 MVP; creature level - player level >= -6:
//= floor + (400 - floor) x min(level - player level + 10, 20) / 20; 7 or more below: from the value at 6 below down to floor / 20
//= in 10 steps (16 or more below: 0.10 / 0.15 / 0.17 %).
//= Bracket bonus, fixed when its last name is claimed: getbaseexp_ratio(75, top level) + 5 % of the character's bar.
//= Paid with getexp2 (script.cpp:24713-24735, pc_gainexp flag 2: no rate, no tax, no boost: the exact number), or at the job's
//= highest base level its Zeny (S_Zeny; never over MAX_ZENY). A claim waits while the character is dead or the Zeny would overflow.
//= Character variables (char-server's char_reg_num), packed: LOKI_CXF[<id> / 31] 2 bits per creature (1 open, 2 completed,
//= 3 claimed); LOKI_CXE[<id>] amount x 1000 + share (only while completed); LOKI_CXB 2 bits per bracket (1 complete, bonus fixed;
//= 2 paid); LOKI_CXK[<b>] the fixed bonus (only while unpaid); LOKI_CXV[<i>] / LOKI_CXU[<i>] / LOKI_CXVN the watched units, valid
//= while LOKI_CXVB is this run's .boot. A bracket complete or paid never opens again (creatures added to it later still get their
//= own bounties; an unfinished bracket takes in new members).
//= Lines to the client (dispbottom, 0x02c1 to the player): "LCX1 H 4 <creatures> <zeny per 1,000,000 EXP now>" (state begins),
//= "O ,<id>..." (open), "R ,<id>:<exp>..." (completed), "D ,<id>..." (claimed), "G ;<b>:<claimed>:<names>:<state>:<bonus>...",
//= "E" (state ends); "B <id>" (placed by a scan), "P <id>" (placed at first sight), "W <id> <exp> <share>" (completed: amount fixed),
//= "C <id> <exp> <share> <zeny>" (paid), "K <b> <exp> <zeny>" (bracket paid).
//================================================================================
-	script	LokiCodex	-1,{
	end;

OnInit:
"""

SCRIPT_RULES = r"""	.boot = gettimetick(2); // (this run: the watched units of an earlier run are not this run's)
	end;

// a creature's state, 2 bits in LOKI_CXF[id / 31]: 0 none, 1 open, 2 completed, 3 claimed
S_Get:
	.@m = getarg(0);
	return (LOKI_CXF[.@m / 31] >> ((.@m % 31) * 2)) & 3;

S_Set:
	.@m = getarg(0);
	.@w = .@m / 31;
	.@s = (.@m % 31) * 2;
	.@o = LOKI_CXF[.@w];
	LOKI_CXF[.@w] = .@o - (((.@o >> .@s) & 3) << .@s) + (getarg(1) << .@s);
	return;

// a bracket's state, 2 bits in LOKI_CXB: 0 open, 1 complete (bonus fixed), 2 paid
S_BGet:
	return (LOKI_CXB >> (getarg(0) * 2)) & 3;

S_BSet:
	.@s = getarg(0) * 2;
	.@o = LOKI_CXB;
	LOKI_CXB = .@o - (((.@o >> .@s) & 3) << .@s) + (getarg(1) << .@s);
	return;

// the CLAIMED names of bracket getarg(0) (a name counts once); callsub keeps its own .@ variables (script.cpp buildin_callsub)
S_Count:
	.@b = getarg(0);
	.@c = 0;
	freeloop(1);
	for (.@i = .bs[.@b]; .@i < .be[.@b]; .@i++) {
		.@k = .bi[.@i];
		if (((LOKI_CXF[.@k / 31] >> ((.@k % 31) * 2)) & 3) == 3 && !.@seen[.bg[.@k]]) {
			.@seen[.bg[.@k]] = 1;
			.@c++;
		}
	}
	freeloop(0);
	return .@c;

// bracket getarg(0) as the client reads it: "<b>:<claimed>:<names>:<state>:<bonus>" (a finished one: all of its names)
S_GLine:
	.@b = getarg(0);
	.@st = callsub(S_BGet, .@b);
	.@c = .bn[.@b];
	if (.@st == 0)
		.@c = callsub(S_Count, .@b);
	return .@b + ":" + .@c + ":" + .bn[.@b] + ":" + .@st + ":" + LOKI_CXK[.@b];

// the flat Base EXP a creature's bounty pays, by its level getarg(0) and its danger getarg(1) (0 normal: 1x, 1 boss: 2x, 2 MVP: 4x)
S_Exp:
	.@lv = getarg(0);
	if (.@lv < 1)
		.@lv = 1;
	.@dg = getarg(1);
	.@mult = 1;
	if (.@dg == 1)
		.@mult = 2;
	else if (.@dg == 2)
		.@mult = 4;
	return (.@lv * .@lv * 15 + .@lv * 35) * .@mult;

// a bounty of creature getarg(0) completed by a kill credited to this character: flat EXP based on creature level and danger
S_Complete:
	.@m = getarg(0);
	.@v = .t[.@m] - 1;
	.@dg = .@v % 10;
	.@lv = .@v / 10;
	.@exp = callsub(S_Exp, .@lv, .@dg);
	if (.@exp < 1)
		.@exp = 1;
	.@share = 0;
	LOKI_CXE[.@m] = .@exp * 1000;
	callsub S_Set, .@m, 2;
	dispbottom "LCX1 W " + .@m + " " + .@exp + " " + .@share;
	return;

// design note: "anyone in the party who CONTRIBUTED to the kill also completes the bounty, provided their base level is NOT more than 10
// levels below the monster's". A script cannot read the monster's damage log (no buildin reads md->dmglog); rAthena's own party
// share pays every member on the monster's map who is alive (party.cpp:1250-1256), healers included. Here: every online member of
// the killer's party, alive, on the killer's map within its sight (area_size + 4 cells) at the kill, base level >= level - 10.
S_Party:
	.@m = getarg(0);
	.@pid = getcharid(1);
	if (.@pid < 1)
		return;
	.@me = getcharid(3);
	.@mc = getcharid(0);
	if (getmapxy(.@map$, .@x, .@y) != 0)
		return;
	.@r = getbattleflag("area_size") + 4;
	.@lv = (.t[.@m] - 1) / 10;
	.@n = getpartymember(.@pid, 1, .@pc);
	getpartymember .@pid, 2, .@pa;
	.@moved = 0;
	for (.@i = 0; .@i < .@n; .@i++) {
		if (.@pc[.@i] == .@mc || !isloggedin(.@pa[.@i], .@pc[.@i]))
			continue;
		if (!attachrid(.@pa[.@i], 0))
			continue; // (a member in an NPC's dialogue right now is left alone)
		.@moved = 1;
		if (getmapxy(.@m2$, .@x2, .@y2) != 0 || .@m2$ != .@map$ || isdead() || BaseLevel < .@lv - 10)
			continue;
		if (.@x2 < .@x - .@r || .@x2 > .@x + .@r || .@y2 < .@y - .@r || .@y2 > .@y + .@r)
			continue;
		if (callsub(S_Get, .@m) == 1)
			callsub S_Complete, .@m;
	}
	if (.@moved)
		attachrid .@me, 1; // (back to the killer)
	return;

// the living monsters of kind getarg(0) within this character's sight: their unit ids in @cxn[], their count returned (8 at most)
S_Near:
	.@m = getarg(0);
	if (getmapxy(.@map$, .@x, .@y) != 0)
		return 0;
	.@r = getbattleflag("area_size") + 4;
	.@n = getareaunits(BL_MOB, .@map$, .@x - .@r, .@y - .@r, .@x + .@r, .@y + .@r, .@u);
	.@k = 0;
	freeloop(1);
	for (.@i = 0; .@i < .@n && .@k < 8; .@i++) {
		getunitdata .@u[.@i], .@d;
		if (.@d[UMOB_CLASS] == .@m && .@d[UMOB_HP] > 0) {
			@cxn[.@k] = .@u[.@i];
			.@k++;
		}
	}
	freeloop(0);
	return .@k;

// 1 when unit getarg(0) is a living monster of kind getarg(1) (each test in its own if: the script engine evaluates both sides of
// an && and getunittype complains of a unit that is gone)
S_Alive:
	.@g = getarg(0);
	if (.@g < 1)
		return 0;
	if (!unitexists(.@g))
		return 0;
	if (getunittype(.@g) != BL_MOB)
		return 0;
	getunitdata .@g, .@d;
	if (.@d[UMOB_CLASS] != getarg(1))
		return 0;
	return .@d[UMOB_HP] > 0;

// the scan's clock of kind getarg(0) (temporary character variables: until logout): @cxt first sight (ms), @cxu the unit it
// follows, @cxh that unit's HP when last seen, @cxx its max HP, @cxl the HP it lost since the first sight (a boss's scan is
// shortened by it: the server cannot tell whose damage it was, so all of it counts)
S_Track:
	.@m = getarg(0);
	.@g = @cxu[.@m];
	if (callsub(S_Alive, .@g, .@m)) {
		getunitdata .@g, .@d;
		if (@cxh[.@m] > .@d[UMOB_HP])
			@cxl[.@m] += @cxh[.@m] - .@d[UMOB_HP];
		@cxh[.@m] = .@d[UMOB_HP];
		return;
	}
	if (.@g > 0) {
		// the one it followed is gone (dead): every HP it still had was lost
		@cxl[.@m] += @cxh[.@m];
		@cxu[.@m] = 0;
		@cxh[.@m] = 0;
	}
	if (callsub(S_Near, .@m) > 0) {
		getunitdata @cxn[0], .@d;
		@cxu[.@m] = @cxn[0];
		@cxh[.@m] = .@d[UMOB_HP];
		if (@cxx[.@m] < 1)
			@cxx[.@m] = .@d[UMOB_MAXHP];
	}
	return;

// the scan's time of kind getarg(0) in ms (the client's CodexData.LookSeconds and HitOff)
S_Need:
	.@m = getarg(0);
	.@v = .t[.@m] - 1;
	if (.@v % 10 > 0) {
		.@x = @cxx[.@m];
		if (.@x < 1)
			.@x = 1;
		.@need = 20000 - 30000 * @cxl[.@m] / .@x;
		if (.@need < 0)
			.@need = 0;
		return .@need;
	}
	.@lv = .@v / 10;
	if (.@lv > 200)
		.@lv = 200;
	.@need = 1500 + 30 * .@lv;
	if (.@need > 15000)
		.@need = 15000;
	return .@need;

// a quest / event creature of kind getarg(0) with getarg(1) living ones near (@cxn): placed at once, its units watched
S_Special:
	.@m = getarg(0);
	.@n = getarg(1);
	if (LOKI_CXVB != .boot) {
		callsub S_WClear;
		LOKI_CXVB = .boot;
	}
	for (.@i = 0; .@i < .@n; .@i++) {
		.@g = @cxn[.@i];
		.@f = 0;
		for (.@j = 0; .@j < LOKI_CXVN; .@j++) {
			if (LOKI_CXV[.@j] == .@g)
				.@f = 1;
		}
		if (.@f)
			continue;
		if (LOKI_CXVN >= 16) {
			// full: the oldest goes
			for (.@j = 1; .@j < 16; .@j++) {
				LOKI_CXV[.@j - 1] = LOKI_CXV[.@j];
				LOKI_CXU[.@j - 1] = LOKI_CXU[.@j];
			}
			LOKI_CXVN = 15;
		}
		LOKI_CXV[LOKI_CXVN] = .@g;
		LOKI_CXU[LOKI_CXVN] = .@m;
		LOKI_CXVN++;
	}
	if (callsub(S_Get, .@m) == 0) {
		callsub S_Set, .@m, 1;
		dispbottom "LCX1 P " + .@m;
	}
	return;

S_WClear:
	for (.@j = 0; .@j < LOKI_CXVN; .@j++) {
		LOKI_CXV[.@j] = 0;
		LOKI_CXU[.@j] = 0;
	}
	LOKI_CXVN = 0;
	return;

// the watched quest / event units: one that is gone (whoever killed it) completes its open bounty (design note: "If it dies to anyone,
// the spawner's bounty completes"); a unit id of an earlier run (a restart, @reloadscript) is no proof: the list is dropped
S_Sweep:
	if (LOKI_CXVN < 1)
		return;
	if (LOKI_CXVB != .boot) {
		callsub S_WClear;
		return;
	}
	.@k = 0;
	for (.@i = 0; .@i < LOKI_CXVN; .@i++) {
		.@g = LOKI_CXV[.@i];
		.@m = LOKI_CXU[.@i];
		if (callsub(S_Alive, .@g, .@m)) {
			LOKI_CXV[.@k] = .@g;
			LOKI_CXU[.@k] = .@m;
			.@k++;
			continue;
		}
		if (callsub(S_Get, .@m) == 1)
			callsub S_Complete, .@m;
	}
	for (.@i = .@k; .@i < LOKI_CXVN; .@i++) {
		LOKI_CXV[.@i] = 0;
		LOKI_CXU[.@i] = 0;
	}
	LOKI_CXVN = .@k;
	return;

// pays getarg(0) Base EXP, or at the job's highest base level (now, at the claim) its Zeny (S_Zeny); returns the Zeny paid (0: EXP),
// -1 refused
S_Pay:
	.@amount = getarg(0);
	if (isdead())
		return -1; // the dead gain nothing (pc.cpp:8459): it waits
	if (NextBaseExp == 99999999) {
		.@z = callsub(S_Zeny, .@amount);
		if (Zeny + .@z > 2147483647)
			return -1; // over MAX_ZENY (INT_MAX: mmo.hpp:82, pc_setparam cuts it): it waits
		Zeny += .@z;
		return .@z;
	}
	getexp2 .@amount, 0;
	return 0;

// the Zeny an amount of Base EXP is worth to a character at its job's highest base level: what selling the drops of the kills
// that EXP stands for brings (.zr: Zeny per 1,000,000 base EXP, the median of the job group's top 25 levels' normal creatures,
// from rAthena's mob_db and item_db), at this server's EXP rate (battle_config base_exp_rate); at least 1
S_Zeny:
	.@mx = .mx[Class];
	if (.@mx < 2)
		.@mx = 99;
	.@r = .zr[.@mx];
	if (.@r < 1)
		.@r = .zr[99];
	.@rate = getbattleflag("base_exp_rate");
	if (.@rate < 1)
		.@rate = 100;
	.@z = getarg(0) * .@r / 1000000 * 100 / .@rate;
	if (.@z < 1)
		.@z = 1;
	return .@z;

// bracket getarg(0)'s flat EXP bonus: 5x the normal bounty at the bracket's top level (10 x bracket)
S_Bonus:
	.@b = getarg(0);
	if (.@b < 1)
		.@b = 1;
	return callsub(S_Exp, .@b * 10, 0) * 5;

OnWhisperGlobal:
	callsub S_Sweep;
	if (@whispervar0$ == "s") {
		freeloop(1);
		.@zp = 0;
		if (NextBaseExp == 99999999)
			.@zp = callsub(S_Zeny, 1000000);
		dispbottom "LCX1 H 4 " + .idn + " " + .@zp;
		.@o$ = "";
		.@r$ = "";
		.@d$ = "";
		.@lw = -1;
		.@word = 0;
		for (.@i = 0; .@i < .idn; .@i++) {
			.@m = .ids[.@i];
			if (.@m / 31 != .@lw) {
				.@lw = .@m / 31;
				.@word = LOKI_CXF[.@lw];
			}
			if (!.@word)
				continue;
			.@v = (.@word >> ((.@m % 31) * 2)) & 3;
			if (.@v == 1) {
				.@o$ = .@o$ + "," + .@m;
				if (getstrlen(.@o$) > 180) {
					dispbottom "LCX1 O " + .@o$;
					.@o$ = "";
				}
			} else if (.@v == 2) {
				.@r$ = .@r$ + "," + .@m + ":" + (LOKI_CXE[.@m] / 1000);
				if (getstrlen(.@r$) > 180) {
					dispbottom "LCX1 R " + .@r$;
					.@r$ = "";
				}
			} else if (.@v == 3) {
				.@d$ = .@d$ + "," + .@m;
				if (getstrlen(.@d$) > 180) {
					dispbottom "LCX1 D " + .@d$;
					.@d$ = "";
				}
			}
		}
		if (.@o$ != "")
			dispbottom "LCX1 O " + .@o$;
		if (.@r$ != "")
			dispbottom "LCX1 R " + .@r$;
		if (.@d$ != "")
			dispbottom "LCX1 D " + .@d$;
		.@g$ = "";
		for (.@b = 1; .@b <= .bmax; .@b++) {
			if (.bn[.@b] < 1)
				continue;
			// every name claimed and no bonus fixed yet (the data changed: a member left the bracket): fixed now
			if (callsub(S_BGet, .@b) == 0 && callsub(S_Count, .@b) >= .bn[.@b]) {
				LOKI_CXK[.@b] = callsub(S_Bonus, .@b);
				callsub S_BSet, .@b, 1;
			}
			.@g$ = .@g$ + ";" + callsub(S_GLine, .@b);
			if (getstrlen(.@g$) > 180) {
				dispbottom "LCX1 G " + .@g$;
				.@g$ = "";
			}
		}
		if (.@g$ != "")
			dispbottom "LCX1 G " + .@g$;
		dispbottom "LCX1 E";
		end;
	}
	if (@whispervar0$ == "n" || @whispervar0$ == "b") {
		.@m = atoi(@whispervar1$);
		if (.@m < 1 || .@m > 65535)
			end;
		if (.t[.@m] < 1)
			end; // not a creature the Codex can meet: refused
		.@v = callsub(S_Get, .@m);
		if (.q[.@m]) {
			// a quest / event creature: placed at FIRST SIGHT, no scan time (some are seen once), while a living one is near
			if (.@v >= 2)
				end;
			.@n = callsub(S_Near, .@m);
			if (.@n > 0)
				callsub S_Special, .@m, .@n;
			end;
		}
		if (@whispervar0$ == "n") {
			// the scan's clock starts at the first sight of a living one near (kept until logout)
			if (.@v > 0)
				end;
			if (@cxt[.@m] > 0) {
				callsub S_Track, .@m;
				end;
			}
			if (callsub(S_Near, .@m) < 1)
				end;
			getunitdata @cxn[0], .@d;
			@cxt[.@m] = gettimetick(0);
			@cxu[.@m] = @cxn[0];
			@cxh[.@m] = .@d[UMOB_HP];
			@cxx[.@m] = .@d[UMOB_MAXHP];
			@cxl[.@m] = 0;
			end;
		}
		if (.@v == 3) {
			dispbottom "LCX1 D ," + .@m;
			end;
		}
		if (.@v == 2) {
			dispbottom "LCX1 R ," + .@m + ":" + (LOKI_CXE[.@m] / 1000);
			end;
		}
		if (.@v == 1) {
			dispbottom "LCX1 B " + .@m;
			end;
		}
		// design note: the server enforces the scan's time: first seen near a living one at least S_Need ago (500 ms for the network)
		if (@cxt[.@m] < 1)
			end; // never seen near: refused
		callsub S_Track, .@m;
		if (gettimetick(0) - @cxt[.@m] + 500 < callsub(S_Need, .@m))
			end; // too early: refused
		if (callsub(S_Near, .@m) < 1 && @cxu[.@m] > 0)
			end; // none near, and the one it followed still lives elsewhere: refused
		callsub S_Set, .@m, 1;
		@cxt[.@m] = 0;
		@cxu[.@m] = 0;
		@cxh[.@m] = 0;
		@cxx[.@m] = 0;
		@cxl[.@m] = 0;
		dispbottom "LCX1 B " + .@m;
		end;
	}
	if (@whispervar0$ == "x") {
		// a monster with its own event label (quests, events, scripts) runs that label INSTEAD of OnNPCKillEvent (mob.cpp:3591-3604),
		// but rAthena sets this character's killedgid / killedrid first: the report must name that very monster, and it must be gone
		.@g = atoi(@whispervar1$);
		if (.@g < 1 || killedgid != .@g)
			end; // not this character's last kill: refused (the watch above has run)
		.@m = killedrid;
		if (.@m < 1 || .@m > 65535 || .t[.@m] < 1)
			end;
		if (unitexists(.@g)) {
			getunitdata .@g, .@d;
			if (.@d[UMOB_HP] > 0)
				end; // still alive (a respawn in the same object): refused
		}
		if (callsub(S_Get, .@m) == 1)
			callsub S_Complete, .@m;
		callsub S_Party, .@m;
		end;
	}
	if (@whispervar0$ == "c") {
		.@m = atoi(@whispervar1$);
		if (.@m < 1 || .@m > 65535)
			end;
		if (.t[.@m] < 1 || callsub(S_Get, .@m) != 2)
			end; // not a completed bounty of this character: refused
		// design note: the amount was fixed by the kill (the level then): paid as stored, whatever the level now
		.@e = LOKI_CXE[.@m];
		.@exp = .@e / 1000;
		.@share = .@e % 1000;
		if (.@exp < 1)
			.@exp = 1;
		// its bracket complete with this claim: the bonus is fixed now (the level now) and waits for its own button
		.@b = .bk[.@m];
		.@bx = 0;
		if (.@b > 0 && callsub(S_BGet, .@b) == 0) {
			callsub S_Set, .@m, 3;
			if (callsub(S_Count, .@b) >= .bn[.@b])
				.@bx = callsub(S_Bonus, .@b);
			callsub S_Set, .@m, 2;
		}
		.@z = callsub(S_Pay, .@exp);
		if (.@z < 0)
			end; // it waits
		callsub S_Set, .@m, 3;
		LOKI_CXE[.@m] = 0;
		if (.@bx > 0) {
			LOKI_CXK[.@b] = .@bx;
			callsub S_BSet, .@b, 1;
		}
		dispbottom "LCX1 C " + .@m + " " + .@exp + " " + .@share + " " + .@z;
		if (.@b > 0)
			dispbottom "LCX1 G ;" + callsub(S_GLine, .@b);
		end;
	}
	if (@whispervar0$ == "k") {
		.@b = atoi(@whispervar1$);
		if (.@b < 1 || .@b > .bmax)
			end;
		if (.bn[.@b] < 1 || callsub(S_BGet, .@b) != 1)
			end; // no such bracket, not complete, or its bonus paid: refused
		.@bx = LOKI_CXK[.@b];
		if (.@bx < 1)
			.@bx = 1;
		.@z = callsub(S_Pay, .@bx);
		if (.@z < 0)
			end; // it waits
		callsub S_BSet, .@b, 2;
		LOKI_CXK[.@b] = 0;
		dispbottom "LCX1 K " + .@b + " " + .@bx + " " + .@z;
		dispbottom "LCX1 G ;" + callsub(S_GLine, .@b);
		end;
	}
	end;

OnNPCKillEvent:
	.@m = killedrid;
	if (.@m < 1 || .@m > 65535)
		end;
	if (.t[.@m] < 1)
		end;
	if (callsub(S_Get, .@m) == 1)
		callsub S_Complete, .@m;
	callsub S_Party, .@m;
	end;
}
"""


def chunks(seq, n):
    for i in range(0, len(seq), n): yield seq[i:i + n]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack", required=True)
    ap.add_argument("--rathena", required=True)
    ap.add_argument("--consts", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--members", help="also write the brackets' members for the book's hints (wave 28, wcodex): <pack>/codex/brackets.tsv")
    a = ap.parse_args()
    rows, sha = read_pack(a.pack)
    meet = metable(rows)
    aeg = aegis_ids(a.rathena)
    plain, evented = regular_spawns(a.rathena, aeg)
    jobs, unknown_jobs = job_max_levels(a.rathena, a.consts)

    def danger(r): return 2 if r["mvp"] else 1 if r["cls"] == 1 else 0

    # the brackets: creatures the Codex can meet with a regular map spawn; by level (1-10 = 1, 11-20 = 2, ...; level 0 with 1-10)
    members = collections.defaultdict(list)
    def dummy(r): return r["bexp"] == 0 and r["hp"] >= DUMMY_HP
    qmaps = quest_maps(a.rathena)
    spawn_maps = regular_spawn_maps(a.rathena, aeg)
    def quest_only(m): return bool(spawn_maps.get(m)) and all(mp in qmaps for mp in spawn_maps[m])
    cmaps = conflict_maps(a.rathena)
    def conflict_only(m):
        rest = [mp for mp in spawn_maps.get(m, ()) if mp not in qmaps]
        return bool(rest) and all(mp in cmaps for mp in rest)
    # design note: quest and event creatures (no regular spawn outside quest / job-change / instance maps: script, summon or item only)
    # have bounties but no bracket; the server places theirs at first sight and completes it when the creature is gone (.q)
    special = sorted(m for m in meet if not dummy(rows[m]) and (plain.get(m, 0) < 1 or quest_only(m)))
    for m in sorted(meet):
        if plain.get(m, 0) < 1 or dummy(rows[m]) or quest_only(m) or conflict_only(m): continue
        lv = rows[m]["level"]
        members[max(1, (lv - 1) // 10 + 1)].append(m)
    base_names = {r["name"].strip().lower() for m, r in rows.items() if m not in CHAMPION_BASE_OVERRIDES and strip_modifier(m, r["name"]) is None}
    groups = {}
    bi, bs, be, bn, bk, bg = [], {}, {}, {}, {}, {}
    for b in sorted(members):
        names = collections.OrderedDict()
        for m in members[b]: names.setdefault(canonical_group_name(m, rows, base_names), []).append(m)
        bs[b] = len(bi)
        for g, (name, ids) in enumerate(names.items(), 1):
            for m in ids: bi.append(m); bk[m] = b; bg[m] = g
        be[b] = len(bi); bn[b] = len(names); groups[b] = names

    out = []
    out.append(SCRIPT_HEAD.replace("{source}", f"the text pack's codex/creatures.tsv (sha256 {sha[:16]}), rAthena's "
                                   f"npc/re/scripts_main.conf spawns, conf/maps_athena.conf, db/re/job_exp.yml"))
    out.append(f"\t// the creatures the Codex can meet ({len(meet)}): .t[<id>] = 1 + danger + 10 x level (danger 0 normal, 1 boss, 2 MVP)\n")
    for ch in chunks(sorted(meet), 8):
        out.append("\t" + " ".join(f".t[{m}] = {1 + danger(rows[m]) + 10 * max(0, rows[m]['level'])};" for m in ch) + "\n")
    out.append(f"\t// every one of them, in id order (the state's list)\n")
    ids = sorted(meet)
    for i, ch in enumerate(chunks(ids, 24)):
        out.append(f"\tsetarray .ids[{i * 24}], " + ", ".join(str(m) for m in ch) + ";\n")
    out.append(f"\t.idn = {len(ids)};\n")
    out.append("\t// the brackets: members (a regular map spawn), flat by bracket: .bi[.bs[b] .. .be[b] - 1]; .bn[b] names (a name counts once);\n"
               "\t// .bk[<id>] its bracket, .bg[<id>] its name's number in the bracket\n")
    for i, ch in enumerate(chunks(bi, 24)):
        out.append(f"\tsetarray .bi[{i * 24}], " + ", ".join(str(m) for m in ch) + ";\n")
    for b in sorted(members):
        out.append(f"\t.bs[{b}] = {bs[b]}; .be[{b}] = {be[b]}; .bn[{b}] = {bn[b]}; // levels {10 * b - 9}-{10 * b}\n")
    out.append(f"\t.bmax = {max(members) if members else 0};\n")
    for ch in chunks(bi, 8):
        out.append("\t" + " ".join(f".bk[{m}] = {bk[m]}; .bg[{m}] = {bg[m]};" for m in ch) + "\n")
    out.append(f"\t// the quest and event creatures ({len(special)}: no regular spawn outside quest / job-change / instance maps): placed at\n"
               "\t// first sight, completed when the creature is gone, in no bracket\n")
    for ch in chunks(special, 12):
        out.append("\t" + " ".join(f".q[{m}] = 1;" for m in ch) + "\n")
    out.append("\t// each job's highest base level (db/re/job_exp.yml MaxBaseLevel)\n")
    for ch in chunks(sorted(jobs), 10):
        out.append("\t" + " ".join(f".mx[{j}] = {jobs[j]};" for j in ch) + "\n")
    zr = zeny_ratios(a.rathena, meet, rows, plain, jobs.values())
    out.append("\t// Zeny per 1,000,000 base EXP at each job table's highest level (the drops' NPC value of the kills that EXP stands for)\n")
    out.append("\t" + " ".join(f".zr[{t}] = {v};" for t, (v, n) in sorted(zr.items())) + "\n")
    out.append(SCRIPT_RULES)
    text = "".join(out)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f: f.write(text)
    print(f"wrote {a.out}: {len(text)} bytes, sha256 {hashlib.sha256(text.encode()).hexdigest()}")
    if a.members:
        # wave 28 (wcodex), the book's bracket hints (UI/CodexBook.Hints.cs): the SAME members the server's script counts, "id<TAB>bracket"
        os.makedirs(os.path.dirname(os.path.abspath(a.members)), exist_ok=True)
        with open(a.members, "w", encoding="utf-8", newline="\n") as f:
            f.write("id\tbracket\n")
            for m in sorted(bk): f.write(f"{m}\t{bk[m]}\n")
        print(f"wrote {a.members}: {len(bk)} members, sha256 {hashlib.sha256(open(a.members, 'rb').read()).hexdigest()}")
    print(f"creatures the Codex can meet: {len(meet)} (of {len(rows)}); bracket members (a regular map spawn): {len(bi)}; brackets {len(members)}; jobs {len(jobs)}"
          + (f"; job names without a constant: {', '.join(unknown_jobs)}" if unknown_jobs else ""))
    print("Zeny per 1,000,000 base EXP by highest level: " + ", ".join(f"{t}: {v} ({n} creatures)" for t, (v, n) in sorted(zr.items())))
    if a.report:
        only_ev = sorted(m for m in meet if plain.get(m, 0) < 1 and evented.get(m, 0) > 0)
        print(f"left out of the brackets: {len(meet) - len(bi)} creatures the Codex can meet without a regular map spawn "
              f"({len(only_ev)} of them spawn only with an event label: " + ", ".join(f"{m} {rows[m]['name']}" for m in only_ev[:12]) + ")")
        print("bracket  levels   members  names  bosses  mvps  same-name groups")
        for b in sorted(members):
            ms = members[b]
            dup = [f"{n} x{len(v)}" for n, v in groups[b].items() if len(v) > 1]
            print(f"{b:7d}  {10 * b - 9:3d}-{10 * b:<4d} {len(ms):7d}  {bn[b]:5d}  {sum(1 for m in ms if danger(rows[m]) == 1):6d}  "
                  f"{sum(1 for m in ms if danger(rows[m]) == 2):4d}  " + ", ".join(dup[:6]) + (" ..." if len(dup) > 6 else ""))
        qo = sorted((m for m in meet if plain.get(m, 0) > 0 and not dummy(rows[m]) and quest_only(m)), key=lambda m: (rows[m]["level"], m))
        print(f"quest-only creatures left out of the brackets (every regular spawn on a quest / job-change / instance map): {len(qo)}")
        for m in qo: print(f"  lv {rows[m]['level']} {m} {rows[m]['name']}: " + ", ".join(f"{mp} ({qmaps[mp]})" for mp in sorted(spawn_maps[m])))
        co = sorted((m for m in meet if plain.get(m, 0) > 0 and not dummy(rows[m]) and not quest_only(m) and conflict_only(m)), key=lambda m: (rows[m]["level"], m))
        print(f"castle / guild dungeon / PvP / WoE-only creatures left out of the brackets (every regular spawn outside quest maps on such a map): {len(co)}")
        for m in co: print(f"  lv {rows[m]['level']} {m} {rows[m]['name']}: " + ", ".join(f"{mp} ({cmaps[mp]})" for mp in sorted(spawn_maps[m]) if mp not in qmaps))
        print(f"quest and event creatures (placed at first sight, completed when gone): {len(special)}")
        dm = sorted(m for m in meet if plain.get(m, 0) > 0 and dummy(rows[m]))
        print(f"training dummies left out of the brackets: {len(dm)} (" + ", ".join(f"{m}" for m in dm[:6]) + " ...)")
        empty = [b for b in range(1, (max(members) if members else 0) + 1) if b not in members]
        print("brackets with no member (nothing to complete): " + (", ".join(f"{10 * b - 9}-{10 * b}" for b in empty) or "none"))


if __name__ == "__main__":
    main()
