# playable.py (2026-10-15): the list of PLAYABLE maps for Architect's all-maps fix (design note: "generate map fixes for every playable
# (map intended to be played by players, excluding debug and dev maps) map in the game"). Read-only: reads the rAthena server copy's files
# and Architect's list of the player's map files; writes only into --out.
#
# The rule (the orchestrator's, from the design's words), facts first:
#   1. LOADED: every active "map: <name>" line of <rathena>/conf/maps_athena.conf (imported by conf/map_athena.conf:126; a line starting
#      with "//" is not loaded).
#   2. WITH DATA: of those, the maps whose data\<name>.rsw the player's game data holds (`architect list`: the install's GRFs and loose
#      files, as the game reads them). Architect then needs the RSW's GND and GAT too (a map whose files are missing fails there: listed).
#      warch2 (alias support, design note 2026-10-15): or whose rsw the game's name table (data\resnametable.txt) gives as another map's rsw that
#      the game data holds (`architect aliases`: IO/RoLookup.cs, the client's own resolution: the exact file first, then the table):
#      such a map is drawn from the real map's files, and its fix is the real map's (World/MapLoader.cs LoadFix by LoadedMap.FileName).
#   3. DEBUG / DEV, dropped only by this stated rule: the name marks it as a test or development map (it contains "test", "dev" as a
#      word part, "debug" or "sample") AND no NPC script the renewal map server loads names it (map.cpp map_reloadnpc: RENEWAL loads
#      npc/re/scripts_main.conf; its npc: / import: lines followed recursively): no warp, NPC, monster spawn or script command reaches it,
#      so only a GM's @warp does (mapflag scripts and Loki's smoke fixtures npc/custom/loki_smoke.txt do not count: a mapflag names a
#      map without making it reachable). A test-named map that a loaded script DOES name stays (e.g. monk_test: the Monk job quest).
#   Everything else stays: instances (1@ / 2@ ... source maps), guild castles, battlegrounds, arenas, job-quest and event maps. Maps no
#   loaded script names but not test-named are KEPT and flagged for review (unclear: reachable only by GM commands or events).
#
#   python playable.py --rathena "C:/rAthena" --rsw maplist.txt --aliases aliases.tsv --out playable
import argparse, os, re, sys, json

ap = argparse.ArgumentParser()
ap.add_argument('--rathena', required=True); ap.add_argument('--rsw', required=True); ap.add_argument('--out', required=True)
ap.add_argument('--aliases', default=None)
a = ap.parse_args()
R = a.rathena; os.makedirs(a.out, exist_ok=True)

def strip(line):
    i = line.find('//')
    return (line if i < 0 else line[:i]).strip()

# 1. loaded
loaded = []
for raw in open(os.path.join(R, 'conf', 'maps_athena.conf'), encoding='latin-1'):
    s = strip(raw)
    m = re.match(r'^map:\s*(\S+)', s)
    if m: loaded.append(m.group(1))
seen = set(); dup = [x for x in loaded if x in seen or seen.add(x)]
loaded_set = list(dict.fromkeys(loaded))

# 2. the player's map files (architect list: one map per line, the last line a count on stderr)
rsw = set(l.strip() for l in open(a.rsw, encoding='utf-8') if l.strip() and ' ' not in l.strip())
# the name table (architect aliases: "alias <name> <real rsw> <real gnd> <real gat> present|absent", "self <name> ... present|absent")
alias, alias_absent, self_rows = {}, {}, {}
if a.aliases:
    for line in open(a.aliases, encoding='utf-8'):
        c = line.rstrip('\n').split('\t')
        if len(c) < 6: continue
        if c[0] == 'alias' and c[5] == 'present': alias[c[1]] = c[2:5]
        elif c[0] == 'alias': alias_absent[c[1]] = c[2:5]
        elif c[0] == 'self': self_rows[c[1]] = c[5]
via_alias = [m for m in loaded_set if m not in rsw and m in alias]
with_data = [m for m in loaded_set if m in rsw or m in alias]
no_data = [m for m in loaded_set if m not in rsw and m not in alias]
data_only = sorted(m for m in rsw if m not in set(loaded_set))

# 3. the scripts the renewal map server loads
files, confs = [], []
def follow(conf):
    p = os.path.join(R, conf)
    if not os.path.exists(p) or p in confs: return
    confs.append(p)
    for raw in open(p, encoding='latin-1'):
        s = strip(raw)
        m = re.match(r'^(npc|import):\s*(\S+)', s)
        if not m: continue
        if m.group(1) == 'import': follow(m.group(2))
        else: files.append(m.group(2))
follow('npc/re/scripts_main.conf')
files = list(dict.fromkeys(files))
text = []
missing_scripts = []
# a mapflag script only sets a map's properties (npc/mapflag/*, npc/re/mapflag/*: "nomemo", "nowarpto" ...): it names nearly every map and
# makes none reachable; npc/custom/loki_smoke.txt is Loki's smoke fixtures (test NPCs): neither counts as naming a map
not_reach = [f for f in files if '/mapflag/' in f.replace(chr(92), '/') or f.replace(chr(92), '/').endswith('custom/loki_smoke.txt')]
for f in files:
    p = os.path.join(R, f)
    if not os.path.exists(p): missing_scripts.append(f); continue
    if f in not_reach: continue
    text.append(open(p, encoding='latin-1').read())
# instances enter their maps by instance_db (an NPC calls instance_create with the instance's name; its maps are listed in the db, not
# in the script): the instance db the renewal server reads counts as naming its maps
inst = [os.path.join(R, 'db', 're', 'instance_db.yml'), os.path.join(R, 'db', 'import', 'instance_db.yml')]
for p in inst:
    if os.path.exists(p): text.append(open(p, encoding='latin-1').read())
alltext = '\n'.join(text)
tokens = set(re.findall(r'[A-Za-z0-9_@\-]+', alltext))
named = set(m for m in with_data if m in tokens)

testpat = re.compile(r'(test|debug|sample|(^|_)dev(_|$|\d))', re.I)
dropped = [m for m in with_data if testpat.search(m) and m not in named]
kept_test = [m for m in with_data if testpat.search(m) and m in named]
playable = [m for m in with_data if m not in set(dropped)]
unnamed = [m for m in playable if m not in named]

def w(name, items):
    with open(os.path.join(a.out, name), 'w', encoding='utf-8') as f:
        for x in items: f.write(x + '\n')
w('playable.txt', playable); w('dropped_debug_dev.txt', dropped); w('loaded_no_data.txt', no_data); w('data_not_loaded.txt', data_only)
w('flag_unnamed_by_scripts.txt', unnamed); w('kept_test_named.txt', kept_test)
w('playable_via_alias.tsv', [m + '\t' + '\t'.join(alias[m]) for m in playable if m in alias and m not in rsw])
w('loaded_no_data_named_self.txt', [m for m in no_data if m in self_rows])
w('loaded_no_data_alias_absent.tsv', [m + '\t' + '\t'.join(alias_absent[m]) for m in no_data if m in alias_absent])
real_maps = sorted(set((alias[m][0] if m in alias and m not in rsw else m) for m in playable))
w('playable_real_maps.txt', real_maps)
summary = {
    'loaded (conf/maps_athena.conf active map: lines)': len(loaded_set), 'duplicate lines': dup,
    'with the player\'s map files (data\\<map>.rsw, or the name table\'s real rsw)': len(with_data),
    'of them through the name table (data\\resnametable.txt)': len(via_alias),
    'of those, the gnd / gat rows naming the same real map as the rsw row': sum(1 for m in via_alias if alias[m][1] in (alias[m][0], '-') and alias[m][2] in (alias[m][0], '-')),
    'loaded but no map files in the game data (nor a name-table alias with the real files)': len(no_data),
    'of them with only name-table rows naming themselves': sum(1 for m in no_data if m in self_rows),
    'of them with an alias whose real rsw is absent': sum(1 for m in no_data if m in alias_absent),
    'map files in the game data but not loaded by the server': len(data_only),
    'npc script files the renewal map server loads': len(files), 'of them missing on disk': missing_scripts,
    'of them not counted as naming a map (mapflags, Loki smoke fixtures)': len(not_reach),
    'instance db files read as naming their maps': [os.path.relpath(p, R) for p in inst if os.path.exists(p)],
    'dropped as debug / dev (test-named and named by no loaded script)': dropped,
    'test-named but named by a loaded script (kept)': kept_test,
    'PLAYABLE': len(playable), 'playable maps named by no loaded script (kept, FLAGGED)': len(unnamed),
    'of the playable, through the name table': sum(1 for m in playable if m in alias and m not in rsw),
    'real maps whose files the playable maps draw (Architect\'s fix files)': len(real_maps),
}
json.dump(summary, open(os.path.join(a.out, 'summary.json'), 'w', encoding='utf-8'), indent=1)
print(json.dumps(summary, indent=1))
