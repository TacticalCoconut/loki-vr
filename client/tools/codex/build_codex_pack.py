#!/usr/bin/env python3
"""Wave 28 (wcodex): the Codex's data for the text pack (design note, 2026-10-15: the Codex book's Bestiarium and Itemarium).

The source is the frozen export snapshot (bestiarium.json / itemarium.json, made by another AI). Every field the book shows is
checked against rAthena's own database in the read-only reference source (C:\\rAthena, e985006): where the two differ,
rAthena's value is written (the server the players meet is rAthena) and the difference is counted in the report. Only
rAthena-derived fields are written (GPL like Loki): no item description, no flavour text, no card lore (those are Gravity's
texts: the book shows the game's own item description from the player's RO data at run time).

Output (UTF-8, tab separated, one header line, lists ';' separated, fields in a list item ':' separated):
  <out>/codex/creatures.tsv   one row per monster of rAthena's renewal mob_db
  <out>/codex/items.tsv       one row per item of rAthena's renewal item_db
  <out>/codex/places.tsv      one row per creature with a map spawn: its spawn maps' nearest town, kind and area (2026-10-16)
  <out>/codex/README.txt      what the files are and where they come from
Usage: python -I build_codex_pack.py --snapshot <dir with bestiarium.json> --rathena <rathena src> --out <pack root>
"""
import argparse, collections, hashlib, json, os, re, struct, sys

import yaml  # PyYAML (the PC's Python has 6.0.3)

ELEMENTS = ["Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Dark", "Ghost", "Undead"]
RACES = ["Formless", "Undead", "Brute", "Plant", "Insect", "Fish", "Demon", "Demihuman", "Angel", "Dragon", "Player_Human", "Player_Doram"]
SIZES = ["Small", "Medium", "Large"]
CLASSES = ["Normal", "Boss", "Guardian", "Battlefield", "Event"]
# rAthena src/map/mob.hpp:151-181 (e_aegis_monstertype) and doc/mob_db_mode_list.txt
AI = {"01": 0x81, "02": 0x83, "03": 0x1089, "04": 0x3885, "05": 0x2085, "06": 0, "07": 0x108B, "08": 0x7085, "09": 0x3095,
      "10": 0x84, "11": 0x84, "12": 0x2085, "13": 0x308D, "17": 0x91, "19": 0x3095, "20": 0x3295, "21": 0x3695, "24": 0xA1,
      "25": 0x1, "26": 0xB695, "27": 0x8084, "ABR_PASSIVE": 0x21, "ABR_OFFENSIVE": 0xA5}
MODES = {"CanMove": 0x1, "Looter": 0x2, "Aggressive": 0x4, "Assist": 0x8, "CastSensorIdle": 0x10, "NoRandomWalk": 0x20,
         "NoCast": 0x40, "CanAttack": 0x80, "CastSensorChase": 0x200, "ChangeChase": 0x400, "Angry": 0x800,
         "ChangeTargetMelee": 0x1000, "ChangeTargetChase": 0x2000, "TargetWeak": 0x4000, "RandomTarget": 0x8000,
         "IgnoreMelee": 0x10000, "IgnoreMagic": 0x20000, "IgnoreRanged": 0x40000, "Mvp": 0x80000, "IgnoreMisc": 0x100000,
         "KnockBackImmune": 0x200000, "TeleportBlock": 0x400000, "FixedItemDrop": 0x1000000, "Detector": 0x2000000,
         "StatusImmune": 0x4000000, "SkillImmune": 0x8000000}
# rAthena src/common/mmo.hpp:336-358 (equip_pos) and src/map/script_constants.hpp:911-918 (the yml names)
EQP = {"Head_Low": 0x1, "Head_Mid": 0x200, "Head_Top": 0x100, "Right_Hand": 0x2, "Left_Hand": 0x20, "Armor": 0x10, "Shoes": 0x40,
       "Garment": 0x4, "Right_Accessory": 0x8, "Left_Accessory": 0x80, "Costume_Head_Top": 0x400, "Costume_Head_Mid": 0x800,
       "Costume_Head_Low": 0x1000, "Costume_Garment": 0x2000, "Ammo": 0x8000, "Shadow_Armor": 0x10000, "Shadow_Weapon": 0x20000,
       "Shadow_Shield": 0x40000, "Shadow_Shoes": 0x80000, "Shadow_Right_Accessory": 0x100000, "Shadow_Left_Accessory": 0x200000,
       "Both_Hand": 0x22, "Both_Accessory": 0x88}
ITEM_TYPES = ["Healing", "Usable", "Etc", "Armor", "Weapon", "Card", "PetEgg", "PetArmor", "Ammo", "DelayConsume", "ShadowGear", "Cash"]
DEFAULT_WALK = 150  # rAthena DEFAULT_WALK_SPEED (src/map/status.hpp)


def load_yaml_body(path):
    with open(path, encoding="utf-8") as f:
        d = yaml.safe_load(f)
    return (d or {}).get("Body") or []


def ci_get(d, key, default=None):
    for k, v in (d or {}).items():
        if str(k).lower() == key.lower():
            return v
    return default


def build_mobs(ra):
    """rAthena's renewal mob_db, parsed as mob.cpp parses it (defaults: mob.cpp:4940-4964; Ai / Class / Modes: 5446-5523;
    class modes added after loading: 5537-5554)."""
    mobs = {}
    for m in load_yaml_body(os.path.join(ra, "db", "re", "mob_db.yml")):
        mid = int(m["Id"])
        ai = str(m.get("Ai", "06")).upper()
        if ai.isdigit(): ai = ai.zfill(2)  # (YAML reads "Ai: 02" as the number 2)
        mode = AI.get(ai, 0)
        if ai not in AI: print(f"unknown Ai {ai!r} for {mid}", file=sys.stderr)
        cls = str(m.get("Class", "Normal"))
        for k, v in (m.get("Modes") or {}).items():
            bit = next((b for n, b in MODES.items() if n.lower() == str(k).lower()), 0)
            mode = (mode | bit) if v else (mode & ~bit)
        if cls == "Boss": mode |= 0x2000000 | 0x4000000 | 0x200000
        elif cls == "Guardian": mode |= 0x4000000
        elif cls == "Battlefield": mode |= 0x4000000 | 0x8000000
        elif cls == "Event": mode |= 0x1000000
        def drops(key):
            out = []
            for d in m.get(key) or []:
                out.append((d["Item"], int(d.get("Rate", 0)), bool(d.get("StealProtected", False))))
            return out
        mobs[mid] = dict(id=mid, aegis=m["AegisName"], name=m.get("Name", m["AegisName"]), level=int(m.get("Level", 1)),
                         hp=max(1, int(m.get("Hp", 1))),  # (mob.cpp loadingFinished: cap_value(maxhp, 1, UINT32_MAX))
                          bexp=int(m.get("BaseExp", 0)), jexp=int(m.get("JobExp", 0)), mexp=int(m.get("MvpExp", 0)),
                         atk=int(m.get("Attack", 0)), matk=int(m.get("Attack2", 0)), deff=int(m.get("Defense", 0)), mdef=int(m.get("MagicDefense", 0)),
                         stats=[int(m.get(s, 1)) for s in ("Str", "Agi", "Vit", "Int", "Dex", "Luk")],
                         range=int(m.get("AttackRange", 0)), size=str(m.get("Size", "Small")), race=str(m.get("Race", "Formless")),
                         element=str(m.get("Element", "Neutral")), elv=int(m.get("ElementLevel", 1)), walk=int(m.get("WalkSpeed", DEFAULT_WALK)),
                         cls=cls, ai=ai, mode=mode, mvp=bool(mode & 0x80000), drops=drops("Drops"), mvpdrops=drops("MvpDrops"))
    return mobs


def build_items(ra):
    items, by_aegis = {}, {}
    for f in ("item_db_usable.yml", "item_db_equip.yml", "item_db_etc.yml"):
        for it in load_yaml_body(os.path.join(ra, "db", "re", f)):
            iid = int(it["Id"])
            loc = 0
            for k, v in (it.get("Locations") or {}).items():
                if v: loc |= EQP.get(str(k), 0)
            jobs = [str(k) for k, v in (it.get("Jobs") or {}).items() if v]
            nojobs = [str(k) for k, v in (it.get("Jobs") or {}).items() if not v]
            tname = next((t for t in ITEM_TYPES if t.lower() == str(it.get("Type", "Etc")).lower()), "Etc")  # (constants are case-insensitive: "Petegg")
            rec = dict(id=iid, aegis=it["AegisName"], name=it.get("Name", it["AegisName"]), type=tname,
                       sub=str(it.get("SubType", "")), slots=int(it.get("Slots", 0)), weight=int(it.get("Weight", 0)),
                       atk=int(it.get("Attack", 0)), matk=int(it.get("MagicAttack", 0)), deff=int(it.get("Defense", 0)),
                       wlv=int(it.get("WeaponLevel", 0)), alv=int(it.get("ArmorLevel", 0)), req=int(it.get("EquipLevelMin", 0)),
                       refine=1 if it.get("Refineable") else 0, loc=loc, buy=int(it.get("Buy", 0)), sell=int(it.get("Sell", it.get("Buy", 0) // 2 if it.get("Buy") else 0)),
                       jobs=("All" if "All" in jobs and not nojobs else ",".join(sorted(jobs)) if jobs else ("All-" + ",".join(sorted(nojobs)) if nojobs else "")),
                       script=" ".join(str(it.get(k, "") or "") for k in ("Script", "EquipScript", "UnEquipScript")))
            items[iid] = rec; by_aegis[rec["aegis"].lower()] = iid
    return items, by_aegis


def mob_skills(ra):
    """db/re/mob_skill_db.txt: MobID,Info,State,SkillID,SkillLv,... (the highest level per skill, first-seen order)."""
    out = collections.defaultdict(collections.OrderedDict)
    with open(os.path.join(ra, "db", "re", "mob_skill_db.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("//"): continue
            c = line.split(",")
            if len(c) < 5 or not c[0].lstrip("-").isdigit(): continue
            mid, sid, lv = int(c[0]), int(c[3]), int(c[4])
            if mid < 0: continue  # (negative ids: groups of monsters, not one)
            d = out[mid]
            d[sid] = max(d.get(sid, 0), lv)
    return out


def skill_names(ra):
    names = {}
    for s in load_yaml_body(os.path.join(ra, "db", "re", "skill_db.yml")):
        names[int(s["Id"])] = str(s.get("Description", s.get("Name", "")))
    return names


def loaded_npc_files(ra):
    """The script files the renewal map server loads: npc/re/scripts_main.conf and what it imports (npc: / delnpc: / import:)."""
    files, gone, seen = [], set(), set()
    def walk(conf):
        p = os.path.join(ra, conf)
        if conf in seen or not os.path.isfile(p): return
        seen.add(conf)
        with open(p, encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.strip()
                m = re.match(r"^(npc|delnpc|import):\s*(\S+)", line)
                if not m: continue
                kind, path = m.group(1), m.group(2)
                if kind == "import": walk(path)
                elif kind == "npc": files.append(path)
                else: gone.add(path)
    walk("npc/re/scripts_main.conf")
    return [f for f in files if f not in gone]


def spawns_and_shops(ra, items_by_aegis, areas=None):
    spawns = collections.defaultdict(lambda: collections.OrderedDict())  # mob -> map -> [count, respawn]
    # (2026-10-16, the bracket hints) areas: mob -> map -> [(x, y, xs, ys, count)] as the spawn lines give them
    shops = collections.defaultdict(list)  # item -> [(npc, map, x, y, price, kind)]
    for rel in loaded_npc_files(ra):
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        with open(p, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("//"): continue
                c = line.rstrip("\r\n").split("\t")
                if len(c) < 4: continue
                kind = c[1].strip()
                if kind in ("monster", "boss_monster"):
                    head = c[0].split(","); mapname = head[0]
                    v = c[3].split(",")
                    try: mid, n = int(v[0]), int(v[1])
                    except ValueError: continue
                    if mid in (2401, 2402, 2403) or mapname.startswith("int_land"): continue  # design note (2026-10-17 item 44): int_land no-drop Poring excluded
                    resp = int(v[2]) if len(v) > 2 and v[2].strip().lstrip("-").isdigit() else 0
                    e = spawns[mid].setdefault(mapname, [0, resp])
                    e[0] += n
                    if areas is not None:
                        num = [int(t) if t.strip().lstrip("-").isdigit() else 0 for t in (head + ["0"] * 5)[1:5]]
                        areas[mid].setdefault(mapname, []).append((num[0], num[1], num[2], num[3], n))
                elif kind in ("shop", "cashshop", "itemshop", "pointshop", "marketshop"):
                    head = c[0].split(",")
                    if head[0] == "-" or len(head) < 3: continue  # a floating shop (opened by a script): no place to show
                    mapname, x, y = head[0], head[1], head[2]
                    npc = c[2].split("#")[0].split("::")[0]
                    body = c[3].replace("{", ",")
                    parts = body.split(",")
                    start = 1
                    if kind in ("itemshop", "pointshop"): start = 2  # sprite,currency,... (npc.cpp npc_parse_shop)
                    for tok in parts[start:]:
                        tok = tok.strip().rstrip("}")
                        if not tok: continue
                        q = tok.split(":")
                        name = q[0]
                        iid = int(name) if name.isdigit() else items_by_aegis.get(name.lower())
                        if iid is None: continue
                        price = int(q[1]) if len(q) > 1 and q[1].lstrip("-").isdigit() else -1
                        shops[iid].append((npc, mapname, int(x), int(y), price, kind))
    return spawns, shops


def attr_table(ra):
    t = {}
    for lvl in load_yaml_body(os.path.join(ra, "db", "re", "attr_fix.yml")):
        L = int(lvl["Level"])
        for atk, row in lvl.items():
            if atk == "Level": continue
            for de, v in row.items():
                t[(L, atk, de)] = int(v)
    return t


def pets(ra, items_by_aegis):
    out = {}
    for p in load_yaml_body(os.path.join(ra, "db", "re", "pet_db.yml")):
        out[str(p["Mob"]).lower()] = (items_by_aegis.get(str(p.get("TameItem", "")).lower(), 0), items_by_aegis.get(str(p.get("FoodItem", "")).lower(), 0), items_by_aegis.get(str(p.get("EggItem", "")).lower(), 0))
    return out


# The export's behavior prose, one per Aegis AI type (bestiarium.json ai_and_behavior.behavior_summary), each read against rAthena's mode
# table (doc/mob_db_mode_list.txt; src/map/mob.hpp:151-181) and kept where it says what the bits do. Edited (FLAGGED): 06 loses "typically
# taking 1 HP damage per strike via Plant protocol" (that is the Ignore* modes, not the AI: the book says it from the bits), 21 loses
# "(standard for MVPs and elite bosses)" (not a fact about the creature), 26 "unpredictability" -> "unpredictably"; ABR_* ("Exhibits
# standard monster combat behavior") says nothing: left out.
BEHAVIOR = {
    "01": "Docile unless provoked; wanders peacefully and only retaliates when directly struck.",
    "02": "Peaceful scavenger that wanders toward dropped items on the ground and consumes them into its inventory; retaliates if attacked.",
    "03": "Non-aggressive alone, but immediately rushes to aid nearby members of its own species when one is attacked.",
    "04": "Hyper-alert predator that actively stalks the nearest adventurer and uses specialized pre-combat skills before taking its first hit.",
    "05": "Hostile predator that attacks adventurers on sight and may switch targets if struck while pursuing prey.",
    "06": "Rooted or stationary: it neither moves nor attacks.",
    "07": "Social scavenger that loots dropped items on the ground and swarms any attacker who harms a member of its pack.",
    "08": "Territorial hunter that only initiates attacks against adventurers at least 5 levels below its own level, ignoring seasoned warriors unless provoked.",
    "10": "Stationary hazard that cannot move from its spot but attacks any adventurer who steps within its attack or spell range.",
    "12": "Mobile fortress defender that aggressively intercepts intruders and shifts focus when intercepted.",
    "13": "Coordinated pack hunter that attacks on sight and rallies nearby kin to overwhelm targets together.",
    "17": "Calm to physical passersby, but senses magical resonance and immediately attacks anyone who begins casting a spell nearby.",
    "19": "Hostile magic-sensitive hunter that attacks on sight and locks onto spellcasters the instant they begin a chant.",
    "20": "Relentless anti-mage predator that detects spellcasting even mid-pursuit and pivots to crush the caster.",
    "21": "Highly intelligent apex combatant that senses spellcasting at all times and dynamically switches targets to anyone within strike range.",
    "24": "Bound minion that holds position unless commanded by its master or directly attacked.",
    "26": "Chaotic high-threat combatant that senses magic and unpredictably shifts attacks to random targets within reach.",
    "27": "Stationary artillery entity that fires at random targets within its range.",
}
BEHAVIOR_BITS = 0xFEBF  # every behavior bit of the low 16 (not NoCast 0x40, not the free 0x100)


def behavior_of(m):
    """The AI's prose when the creature behaves as its AI says (its own Modes change none of the behavior bits), else ""."""
    text = BEHAVIOR.get(m["ai"], "")
    if not text: return ""
    return text if (m["mode"] & BEHAVIOR_BITS) == (AI.get(m["ai"], -1) & BEHAVIOR_BITS) else ""


_DROP_SENTENCE = re.compile(r"double-click|double click|right-click|drag (?:it|this|the)|equipment window|ctrl\+|alt\+", re.I)
# the stat lines the book's left page already shows (from rAthena's own fields); every other line stays, in order (a line ending in ":"
# is a condition such as "Refine +7 or higher:" and the lines under it depend on it: dropping it would change what they say)
_SHOWN = re.compile(r"^(weight|type|classes?|equip on|equipped on|level required|required level|weapon level|armor level|slots?|def|atk|matk|attack|attack strength|magic attack|location)\s*:\s*[\w ,.+\-/()]*$", re.I)
_NUM = re.compile(r"\d+")


def item_numbers(it):
    """Every number rAthena's item record holds (its fields, its scripts, its name): the lore may only say these."""
    pool = {0, 1, 100}
    for k in ("slots", "weight", "atk", "matk", "deff", "wlv", "alv", "req", "buy", "sell"):
        pool.add(it[k])
    pool.add(it["weight"] // 10)
    for n in _NUM.findall(it.get("script", "") + " " + it["name"]): pool.add(int(n))
    return pool


def numbers_ok(text, pool):
    for n in _NUM.findall(text):
        v = int(n)
        if v in pool or v * 10 in pool or v * 100 in pool: continue
        return False
    return True


def item_lore(it, snap):
    """The export's English flavor lore and effect lines for an item, cleaned (the PC client's mouse hints and the stat fragments the
    export left in its flavor text go; the lines the book already shows go) and checked: every number in them must be one of rAthena's
    for that item. Returns (lore, lines, why-not)."""
    if snap is None: return "", [], "not in the export"
    flavor = re.split(r"\s+Classes:\s", snap.get("flavor_lore", "") or "")[0].strip()
    sentences = [x for x in re.split(r"(?<=[.!?])\s+", flavor) if x and not _DROP_SENTENCE.search(x)]
    lore = " ".join(sentences).strip()
    lines = [l.strip() for l in snap.get("stats_and_effects_lines", []) or [] if l.strip() and not _SHOWN.match(l.strip())]
    pool = item_numbers(it)
    bad = []
    if lore and not numbers_ok(lore, pool): bad.append("lore"); lore = ""
    # all the lines or none: a line left out could be the condition the next ones hang on ("Refine +7 or higher:")
    if any(not numbers_ok(l, pool) for l in lines): bad.append("lines"); lines = []
    return lore, lines, ",".join(bad)


SUMMON_SKILLS = {196, 209}  # NPC_SUMMONSLAVE, NPC_SUMMONMONSTER (db/re/skill_db.yml)


# ---------------------------------------------------------------------------------------------------- the places (2026-10-16)
# design note: "The closer you are to completing a bracket, more specific tips get unlocked": each map a creature spawns on, its nearest
# town (by warps: the map server's own warp lines), what kind of map it is and where on it the spawn lines put the creature.
TOWN_NAMES = {"prontera": "Prontera", "izlude": "Izlude", "izlude_a": "Izlude", "izlude_b": "Izlude", "izlude_c": "Izlude", "izlude_d": "Izlude",
              "moc_ruins": "Morroc", "morocc": "Morroc", "geffen": "Geffen", "payon": "Payon", "pay_arche": "Payon", "alberta": "Alberta",
              "aldebaran": "Al De Baran", "yuno": "Juno", "lighthalzen": "Lighthalzen", "einbroch": "Einbroch", "einbech": "Einbech",
              "hugel": "Hugel", "rachel": "Rachel", "veins": "Veins", "comodo": "Comodo", "umbala": "Umbala", "amatsu": "Amatsu",
              "gonryun": "Kunlun", "louyang": "Louyang", "ayothaya": "Ayothaya", "brasilis": "Brasilis", "dewata": "Dewata",
              "malangdo": "Malangdo", "malaya": "Port Malaya", "mora": "Mora", "eclage": "Eclage", "dicastes01": "El Dicastes",
              "lasagna": "Lasagna", "wolfvill": "Wolf Village", "xmas": "Lutie", "niflheim": "Niflheim", "moscovia": "Moscovia",
              "manuk": "Manuk", "splendide": "Splendide", "mid_camp": "Midgard Camp", "jawaii": "Jawaii", "nameless_n": "Nameless Island"}
# a map named after a town's region belongs to that town whatever the warps say (FLAGGED: rAthena's map names; the longest prefix wins)
PREFIX_TOWN = {"prt_": "prontera", "pay_": "payon", "gef_": "geffen", "moc_": "moc_ruins", "alb": "alberta", "ald": "aldebaran",
               "alde_": "aldebaran", "c_tower": "aldebaran", "yuno": "yuno", "ein_": "einbroch", "lhz_": "lighthalzen", "hu_": "hugel",
               "ra_": "rachel", "ve_": "veins", "cmd_": "comodo", "um_": "umbala", "ama_": "amatsu", "gon_": "gonryun", "lou_": "louyang",
               "ayo_": "ayothaya", "bra_": "brasilis", "dew_": "dewata", "mal_": "malangdo", "ma_": "malaya", "ecl_": "eclage",
               "dic_": "dicastes01", "lasa_": "lasagna", "iz_": "izlude", "xmas": "xmas", "nif_": "niflheim", "mosk_": "moscovia",
               "man_": "manuk", "spl_": "splendide", "mid_": "mid_camp", "jaw": "jawaii", "abbey": "nameless_n", "tha_": "rachel",
               "odin_": "rachel", "ice_": "rachel", "juperos": "yuno", "mag_": "yuno", "gl_": "geffen", "glast": "geffen",
               "anthell": "moc_ruins", "in_sphinx": "moc_ruins", "orcsdun": "geffen", "beach_dun": "comodo", "tur_dun": "alberta",
               "treasure": "alberta", "kh_": "einbroch", "thor_": "veins", "lou_dun": "louyang", "gefenia": "geffen", "abyss_": "hugel",
               "moro_": "moc_ruins", "jupe_": "yuno", "arug_": "rachel", "schg_": "yuno", "tur_d": "alberta"}


def map_sizes(ra):
    """Every map's width and height in cells from rAthena's map caches (db/map_cache.dat, then db/re/map_cache.dat over it:
    map_cache_main_header {uint32 file_size, uint16 map_count} padded to 8 bytes, then per map {char name[12], int16 xs, int16 ys, int32 len} + len bytes)."""
    sizes = {}
    for rel in ("db/map_cache.dat", "db/re/map_cache.dat"):
        path = os.path.join(ra, rel)
        if not os.path.isfile(path): continue
        b = open(path, "rb").read(); o = 8  # (the header is padded to 8 bytes)
        for _ in range(struct.unpack_from("<H", b, 4)[0]):
            name = b[o:o + 12].split(b"\0")[0].decode("ascii", "replace"); xs, ys, ln = struct.unpack_from("<hhi", b, o + 12)
            sizes[name] = (xs, ys); o += 20 + ln
    return sizes


def towns_and_warps(ra):
    """The town maps (a "mapflag town" line of the loaded scripts, as listed) and the warp graph between maps (warp / warp2 lines)."""
    towns, links = set(), collections.defaultdict(set)
    for rel in loaded_npc_files(ra) + ["npc/mapflag/town.txt", "npc/re/mapflag/town.txt"]:
        p = os.path.join(ra, rel)
        if not os.path.isfile(p): continue
        with open(p, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("//"): continue
                c = line.rstrip("\r\n").split("\t")
                if len(c) >= 3 and c[1].strip() == "mapflag" and c[2].strip() == "town": towns.add(c[0].strip())
                if len(c) >= 4 and c[1].strip() in ("warp", "warp2"):
                    a, t = c[0].split(",")[0], c[3].split(",")
                    if len(t) >= 3 and a and t[2]: links[a].add(t[2]); links[t[2]].add(a)
    towns |= set(TOWN_NAMES)  # (towns rAthena has no town mapflag for: Lutie, Niflheim, Moscovia ...)
    return towns, links


def nearest_town(mp, towns, links, max_hops=12):
    """The map itself when it is a town; a map named after a town's region: that town (PREFIX_TOWN); else the town fewest warps away
    (a tie: the first by name); "" when none within max_hops."""
    if mp in towns: return mp
    pre = [k for k in PREFIX_TOWN if mp.startswith(k)]
    if pre: return PREFIX_TOWN[max(pre, key=len)]
    seen, frontier = {mp}, [mp]
    for _ in range(max_hops):
        nxt = []
        for m in frontier:
            for n in sorted(links.get(m, ())):
                if n in seen: continue
                seen.add(n); nxt.append(n)
        hit = sorted(n for n in nxt if n in towns)
        if hit: return hit[0]
        frontier = nxt
        if not frontier: break
    return ""


def map_kind(mp, towns):
    """field / dungeon / town / place, from the map's name as rAthena's files name them (FLAGGED rule)."""
    if mp in towns: return "town"
    if "fild" in mp: return "field"
    if re.search(r"(dun|sew|_d\d|tower|cave|mine|anthell|gl_|abbey|lab|tomb|room|temple|tem\d)", mp): return "dungeon"
    return "place"


def area_word(spots, size):
    """Where on its map the spawn lines put a creature: "all" (anywhere: a line with no centre or no range, rAthena mob.cpp:1142-1150
    searches the whole map; or a range over half the map each way), else the compass part of the line's centre in thirds of the map
    ("north-east", "middle" ...; north = larger y), two parts at most joined by "+" (more: "all")."""
    w, h = size if size else (0, 0)
    words = collections.OrderedDict()
    for x, y, xs, ys, n in sorted(spots, key=lambda t: -t[4]):
        if (x == 0 and y == 0) or xs + ys < 1 or w <= 0 or h <= 0 or (2 * xs >= w * 0.5 and 2 * ys >= h * 0.5): return "all"
        fx, fy = x / float(w), y / float(h)
        ns = "north" if fy > 2 / 3.0 else "south" if fy < 1 / 3.0 else ""
        ew = "east" if fx > 2 / 3.0 else "west" if fx < 1 / 3.0 else ""
        words[(ns + "-" + ew).strip("-") or "middle"] = 1
    return "+".join(words) if 0 < len(words) <= 2 else "all"


def summoners(ra):
    """Who calls a monster up besides the map spawns: other monsters (mob_skill_db.txt rows of NPC_SUMMONSLAVE / NPC_SUMMONMONSTER: their
    val1..val5 columns are monster ids) and the summon groups of db/re/mob_summon.yml (Bloody Dead Branch, Poring Box ...)."""
    by_mob = collections.defaultdict(set)
    with open(os.path.join(ra, "db", "re", "mob_skill_db.txt"), encoding="utf-8", errors="replace") as f:
        for line in f:
            c = line.strip().split(",")
            if len(c) < 17 or line.lstrip().startswith("//") or not c[0].lstrip("-").isdigit() or not c[3].isdigit(): continue
            if int(c[3]) not in SUMMON_SKILLS or int(c[0]) < 0: continue
            for v in c[12:17]:
                if v.strip().isdigit(): by_mob[int(v)].add(int(c[0]))
    groups = collections.defaultdict(set)
    for g in load_yaml_body(os.path.join(ra, "db", "re", "mob_summon.yml")):
        for m in g.get("Summon") or []: groups[str(m.get("Mob", "")).lower()].add(str(g["Group"]))
        if g.get("Default"): groups[str(g["Default"]).lower()].add(str(g["Group"]))
    return by_mob, groups


def group_items(ra, items):
    """The items whose script calls a summon group (MOBG_<group>): the group's names for the book."""
    out = collections.defaultdict(list)
    for iid, it in sorted(items.items()):
        for g in re.findall(r"MOBG_([A-Za-z0-9_]+)", it.get("script", "")):
            if iid not in out[g.upper()]: out[g.upper()].append(iid)
    return out


def script_monsters(ra, mobs_by_aegis):
    """Monsters a loaded script puts on a map with the monster / areamonster commands (events, quests, instances)."""
    found = set()
    cmd = re.compile(r"\b(?:area)?monster\b[^;]*?\"[^\"]*\"\s*,\s*(-?[A-Za-z0-9_]+)\s*,", re.I)
    for rel in loaded_npc_files(ra):
        pth = os.path.join(ra, rel)
        if not os.path.isfile(pth): continue
        with open(pth, encoding="utf-8", errors="replace") as f:
            for line in f:
                if "monster" not in line or "\tmonster\t" in line or "\tboss_monster\t" in line: continue
                for tok in cmd.findall(line):
                    if tok.lstrip("-").isdigit(): found.add(int(tok))
                    elif tok.lower() in mobs_by_aegis: found.add(mobs_by_aegis[tok.lower()])
    return found


def clean(s):
    return str(s).replace("\t", " ").replace("\r", " ").replace("\n", " ")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshot", required=True)
    ap.add_argument("--rathena", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    snap_b = json.load(open(os.path.join(a.snapshot, "bestiarium.json"), encoding="utf-8"))["creatures"]
    snap_i = json.load(open(os.path.join(a.snapshot, "itemarium.json"), encoding="utf-8"))["items"]
    mobs = build_mobs(a.rathena)
    items, by_aegis = build_items(a.rathena)
    skills, snames = mob_skills(a.rathena), skill_names(a.rathena)
    areas = collections.defaultdict(dict)
    spawns, shops = spawns_and_shops(a.rathena, by_aegis, areas)
    sizes = map_sizes(a.rathena); towns, links = towns_and_warps(a.rathena); place_rows = []
    if not sizes: diff["(info) no map cache read: every place's area 'all'"] += 1
    attr = attr_table(a.rathena)
    pet = pets(a.rathena, by_aegis)
    summoned_by, summon_groups = summoners(a.rathena)
    gitems = group_items(a.rathena, items)
    mobs_by_aegis = {m["aegis"].lower(): mid for mid, m in mobs.items()}
    in_scripts = script_monsters(a.rathena, mobs_by_aegis)
    diff = collections.Counter(); examples = collections.defaultdict(list)

    def note(field, key, ours, theirs):
        diff[field] += 1
        if len(examples[field]) < 3: examples[field].append(f"{key}: rAthena {ours!r} / export {theirs!r}")

    def origin_of(mid, m):
        """Where a creature comes from besides the map spawns: "mob:<ids>" (other monsters summon it), "item:<ids>" (an item's summon
        group: mob_summon.yml), "script" (a loaded script's monster command), joined by "|"; "" none."""
        parts = []
        by = sorted(x for x in summoned_by.get(mid, ()) if x != mid and x in mobs)
        if by: parts.append("mob:" + ",".join(str(x) for x in by[:6]))
        its = []
        for g in sorted(summon_groups.get(m["aegis"].lower(), ())):
            for iid in gitems.get(g.upper(), []):
                if iid not in its: its.append(iid)
        if its: parts.append("item:" + ",".join(str(x) for x in its[:6]))
        if mid in in_scripts: parts.append("script")
        return "|".join(parts)

    # ---- creatures: the export's records checked field by field against rAthena
    cre_rows, dropped_by = [], collections.defaultdict(list)
    for mid in sorted(mobs):
        m = mobs[mid]; s = snap_b.get(str(mid))
        if s is None: diff["creature missing from the export"] += 1
        else:
            st = s["stats"]
            for f, ours, theirs in (("level", m["level"], st["level"]), ("hp", m["hp"], st["hp"]), ("base_exp", m["bexp"], st["base_exp"]),
                                    ("job_exp", m["jexp"], st["job_exp"]), ("defense", m["deff"], st["defense"]), ("mdef", m["mdef"], st["magic_defense"]),
                                    ("size", m["size"], st["size"]), ("race", m["race"], st["race"]), ("element", m["element"], st["element"]),
                                    ("element_level", m["elv"], st["element_level"]), ("class", m["cls"], s["class"]), ("mvp", m["mvp"], s["is_mvp"]),
                                    ("str..luk", m["stats"], [st[k] for k in ("str", "agi", "vit", "int", "dex", "luk")])):
                if ours != theirs: note("creature " + f, mid, ours, theirs)
            ex = [(d["item_id"], d["rate_raw"]) for d in s.get("drops", [])]
            ours = [(by_aegis.get(d[0].lower(), -1), d[1]) for d in m["drops"]]
            if ex != ours: note("creature drops", mid, ours[:3], ex[:3])
        if s is not None:
            exh = {h["map"] for h in s.get("habitats", [])}; ourh = set(spawns.get(mid, {}).keys())
            if exh != ourh: note("creature habitat maps", mid, sorted(ourh)[:4], sorted(exh)[:4])
        mult = [attr.get((m["elv"], atk, m["element"]), 100) for atk in ELEMENTS]
        if s is not None:
            exm = [s["elemental_profile"]["multipliers_pct"].get(e, 100) for e in ELEMENTS]
            if exm != mult: note("creature element multipliers", mid, mult, exm)
        sk = ";".join(f"{sid}:{lv}:{clean(snames.get(sid, ''))}" for sid, lv in list(skills.get(mid, {}).items())[:12])
        def dl(lst):
            out = []
            for name, rate, steal in lst:
                iid = by_aegis.get(name.lower())
                if iid is None: diff["drop item unknown to item_db"] += 1; continue
                out.append(f"{iid}:{rate}:{1 if steal else 0}")
            return ";".join(out)
        for name, rate, steal in m["drops"] + m["mvpdrops"]:
            iid = by_aegis.get(name.lower())
            if iid is not None: dropped_by[iid].append((mid, rate))
        hab = sorted(spawns.get(mid, {}).items(), key=lambda kv: -kv[1][0])[:10]
        habs = ";".join(f"{mp}:{n}:{r}" for mp, (n, r) in hab)
        # (2026-10-16) the same maps' places for the bracket hints, in a file of their own (codex/places.tsv: creatures.tsv, which
        # the bounties' server script is built from, stays as it was): map:nearest town:kind:area
        if hab: place_rows.append([mid, ";".join(f"{mp}:{TOWN_NAMES.get(nearest_town(mp, towns, links), '')}:{map_kind(mp, towns)}:{area_word(areas[mid].get(mp, []), sizes.get(mp))}" for mp, _ in hab)])
        tame, food, egg = pet.get(m["aegis"].lower(), (0, 0, 0))
        cre_rows.append([mid, clean(m["name"]), m["level"], m["hp"], m["bexp"], m["jexp"], m["atk"], m["matk"], m["deff"], m["mdef"],
                         *m["stats"], m["range"], SIZES.index(m["size"]) if m["size"] in SIZES else 0,
                         RACES.index(m["race"]) if m["race"] in RACES else 0, ELEMENTS.index(m["element"]) if m["element"] in ELEMENTS else 0,
                         m["elv"], CLASSES.index(m["cls"]) if m["cls"] in CLASSES else 0, 1 if m["mvp"] else 0, m["mode"], m["walk"],
                         ",".join(str(x) for x in mult), sk, dl(m["drops"]), dl(m["mvpdrops"]), habs, f"{tame}:{food}:{egg}", clean(behavior_of(m)), origin_of(mid, m)])
        if behavior_of(m): diff["(info) creatures with the export's behavior prose"] += 1
    # ---- items
    item_rows = []
    for iid in sorted(items):
        it = items[iid]; s = snap_i.get(str(iid))
        if s is None: diff["item missing from the export"] += 1
        else:
            st = s["stats"]
            for f, ours, theirs in (("type", it["type"], s["type"]), ("slots", it["slots"], s["slots"]), ("weight", it["weight"], st["weight_tenths"]),
                                    ("attack", it["atk"], st["attack"]), ("magic_attack", it["matk"], st["magic_attack"]), ("defense", it["deff"], st["defense"]),
                                    ("weapon_level", it["wlv"], st["weapon_level"]), ("equip_level_min", it["req"], st["equip_level_min"])):
                if ours != theirs: note("item " + f, iid, ours, theirs)
        if s is not None:
            exs = {(x["map"], x["npc_name"]) for x in s.get("sold_by_shops", [])}; ours_s = {(mp, npc) for npc, mp, *_ in shops.get(iid, [])}
            if exs != ours_s: note("item shops", iid, sorted(ours_s)[:2], sorted(exs)[:2])
        db = sorted(dropped_by.get(iid, []), key=lambda t: (0 if spawns.get(t[0]) else 1, -t[1], t[0]))[:10]
        sh = shops.get(iid, [])
        seen = set(); shl = []
        for npc, mp, x, y, price, kind in sh:
            k = (npc, mp)
            if k in seen: continue
            seen.add(k); shl.append(f"{clean(npc).replace(':', ' ').replace(';', ' ')}:{mp}:{x}:{y}:{price}:{kind}")
            if len(shl) >= 8: break
        item_rows.append([iid, clean(it["name"]), it["type"], it["sub"], it["slots"], it["weight"], it["atk"], it["matk"], it["deff"], it["wlv"], it["alv"],
                          it["req"], it["refine"], it["loc"], clean(it["jobs"]), it["buy"], it["sell"], ";".join(f"{m}:{r}" for m, r in db), ";".join(shl)])
    os.makedirs(os.path.join(a.out, "codex"), exist_ok=True)
    def write(name, header, rows):
        p = os.path.join(a.out, "codex", name)
        with open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write("\t".join(header) + "\n")
            for r in rows: f.write("\t".join(str(x) for x in r) + "\n")
        h = hashlib.sha256(open(p, "rb").read()).hexdigest()
        print(f"wrote {p}: {len(rows)} rows, {os.path.getsize(p)} bytes, sha256 {h}")
    write("creatures.tsv", ["id", "name", "level", "hp", "base_exp", "job_exp", "atk", "matk", "def", "mdef", "str", "agi", "vit", "int", "dex", "luk",
                            "range", "size", "race", "element", "element_level", "class", "mvp", "mode", "walk", "attr", "skills", "drops", "mvp_drops", "habitats", "pet", "behavior", "origin"], cre_rows)
    write("places.tsv", ["id", "places"], place_rows)
    write("items.tsv", ["id", "name", "type", "sub", "slots", "weight", "atk", "matk", "def", "weapon_level", "armor_level", "req_level", "refineable",
                        "locations", "jobs", "buy", "sell", "dropped_by", "sold_by"], item_rows)
    lore_rows = []
    for iid in sorted(items):
        lore, lines, bad = item_lore(items[iid], snap_i.get(str(iid)))
        if bad: diff["(info) item lore parts left out: a number not rAthena's (" + bad + ")"] += 1
        if lore or lines: lore_rows.append([iid, clean(lore), clean(" | ".join(lines))])
    diff["(info) items with the export's lore or effect lines (checked)"] = len(lore_rows)
    write("item_lore.tsv", ["id", "lore", "lines"], lore_rows)
    with open(os.path.join(a.out, "codex", "README.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("Loki's Codex data (wave 28, wcodex). creatures.tsv and items.tsv: every field from rAthena's renewal database and NPC scripts\n"
                "(GPL v3, like Loki): db/re/mob_db.yml, mob_skill_db.txt, skill_db.yml, item_db_*.yml, attr_fix.yml, pet_db.yml and the shops /\n"
                "spawns of the scripts npc/re/scripts_main.conf loads (mob_summon.yml and the scripts' monster commands for the origin column);\n"
                "places.tsv: for each spawn map of a creature (creatures.tsv's habitats, the same order), its nearest town by the scripts' warps\n"
                "and the town mapflags, its kind by its name, and where on it the spawn lines put the creature, in thirds of the map's size from\n"
                "db/map_cache.dat and db/re/map_cache.dat (\"map:town:kind:area\", the book's bracket hints);\n"
                "creatures.tsv's behavior column is the export's prose per AI type, kept only where rAthena's modes agree with it.\n"
                "item_lore.tsv: the export's English item flavor text and effect lines (rewritten from Gravity's item descriptions: they ship\n"
                "in the text pack only, never in the build), cleaned and checked against rAthena's numbers for each item.\n"
                "Built by client/tools/codex/build_codex_pack.py; checked by client/tools/codex/verify_codex_pack.py (its own parse of rAthena).\n")
    print("rAthena vs the export (rAthena written):")
    for k, n in sorted(diff.items()):
        print(f"  {k}: {n}" + ("" if not examples[k] else "   e.g. " + " | ".join(examples[k])))
    print(f"creatures {len(cre_rows)}, items {len(item_rows)}, spawns for {len(spawns)} creatures, shop items {len(shops)}")


if __name__ == "__main__":
    main()
