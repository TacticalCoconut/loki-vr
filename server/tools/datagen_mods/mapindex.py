"""
common/mapindex.cpp mapindex_init (rAthena e985006): the map indexes the servers exchange instead of map names
(db/map_index.txt, then db/import/map_index.txt). Writes data/json/mapindex.json.z for src/pc_taekwon.rs (the Star
Gladiator feel maps store a mapindex in the PC_FEEL_* registers: clif_parse_FeelSaveOk's map_id2index, pc_reg_received's
map_mapindex2mapid, clif_feel_info's mapindex_id2name).

mapindex_init reads each line that does not start with "//" with sscanf("%11s\t%d"): one field = the previous index
+ 1 (the first line of each file continues from -1), two fields = that index; mapindex_addmap normalises the name
(mapindex_getmapname), refuses indexes outside [0, MAX_MAPINDEX) and empty names, and a later line for the same index
replaces the earlier name (strdb_remove + strdb_iput). The dataset is `names`: index -> name ("" = no map), up to
max_index (the highest index + 1), as the `indexes[]` array holds them.
"""
import os, re

MAX_MAPINDEX = 2000        # mapindex.hpp
MAP_NAME_LENGTH = 12       # mmo.hpp (11 + 1)


def mapindex_getmapname(s):
    """mapindex.cpp mapindex_getmapname: at most 15 characters read, a ".gat" extension (any case) dropped, 11 kept."""
    s = s[:15]
    if len(s) >= 4 and s[-4:].lower() == '.gat':
        s = s[:-4]
    return s[:MAP_NAME_LENGTH - 1]


def build(ctx):
    names = {}
    max_index = 0
    for i, rel in enumerate(('db/map_index.txt', 'db/import/map_index.txt')):
        path = ctx['resolve'](rel)
        if not os.path.exists(path):
            if i == 0:
                raise SystemExit('Unable to read mapindex config file %s!' % rel)
            break
        last_index = -1
        for line in open(path, encoding='latin-1'):
            if line[:2] == '//':
                continue
            # sscanf(line, "%11s\t%d", map_name, &index): %11s skips leading white space and reads at most 11
            # non-white characters; "\t" in the format matches any amount (also none) of white space, then %d
            m = re.match(r'\s*(\S{1,11})\s*([+-]?\d+)?', line)
            if not m:
                continue
            index = int(m.group(2)) if m.group(2) is not None else last_index + 1
            name = mapindex_getmapname(m.group(1))
            if 0 <= index < MAX_MAPINDEX and name:
                names[index] = name
                max_index = max(max_index, index + 1)
            last_index = index
    ctx['write_json']('mapindex', {'names': [names.get(i, '') for i in range(max_index)]})
