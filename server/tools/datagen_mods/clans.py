"""
The default clans (rAthena e985006, sql-files/main.sql): the `clan` and `clan_alliance` rows the char server loads at
start (char/int_clan.cpp inter_clan_init → inter_clan_fromsql) and sends to the map server (mapif_clan_info).
Writes data/json/misc_clan.json.z for src/clan.rs.

inter_clan_init reads `SELECT clan_id FROM clan` (MyISAM, physical order = the INSERT order of main.sql) and
inter_clan_fromsql each clan's row and up to MAX_CLANALLIANCE of its `clan_alliance` rows (`WHERE clan_id = ?`, in
the order MariaDB returns them: the INSERT order). Text columns are cut the way the memcpy calls cut them (name /
master NAME_LENGTH, mapname MAP_NAME_LENGTH_EXT); max_member above MAX_CLAN is lowered to MAX_CLAN.
"""
import os, re

NAME_LENGTH = 24          # mmo.hpp
MAP_NAME_LENGTH_EXT = 16  # mmo.hpp MAP_NAME_LENGTH (11 + 1) + 4
MAX_CLAN = 500            # mmo.hpp
MAX_CLANALLIANCE = 6      # mmo.hpp


def sql_values(text, table):
    """The value tuples of `INSERT INTO `table` VALUES (...)` lines, as lists of unquoted strings."""
    out = []
    pat = re.compile(r"INSERT INTO `%s` VALUES \((.*)\);" % re.escape(table))
    for line in text.splitlines():
        m = pat.match(line.strip())
        if not m:
            continue
        vals = re.findall(r"'((?:[^'\\]|\\.)*)'", m.group(1))
        out.append([v.replace("\\'", "'") for v in vals])
    return out


def build(ctx):
    path = os.path.join(ctx['root'], 'sql-files', 'main.sql')
    text = open(path, encoding='latin-1').read()
    alliances = sql_values(text, 'clan_alliance')
    clans = []
    for row in sql_values(text, 'clan'):
        clan_id, name, master, mapname, max_member = int(row[0]), row[1], row[2], row[3], int(row[4])
        if max_member > MAX_CLAN:
            print('Clan %d:%s specifies higher capacity (%d) than MAX_CLAN (%d)' % (clan_id, name, max_member, MAX_CLAN))
            max_member = MAX_CLAN
        al = []
        for a in alliances:
            if int(a[0]) != clan_id or len(al) >= MAX_CLANALLIANCE:
                continue
            al.append({'opposition': int(a[1]), 'clan_id': int(a[2]), 'name': a[3][:NAME_LENGTH]})
        clans.append({'id': clan_id, 'name': name[:NAME_LENGTH], 'master': master[:NAME_LENGTH],
                      'map': mapname[:MAP_NAME_LENGTH_EXT], 'max_member': max_member, 'alliances': al})
    print('clans: %d clans, %d alliances' % (len(clans), sum(len(c['alliances']) for c in clans)))
    ctx['write_json']('misc_clan', {'clans': clans})


if __name__ == '__main__':
    # `python3 clans.py <rathena> <server/data>`: this dataset alone
    import sys, json, zlib
    root, outdir = sys.argv[1], sys.argv[2]

    def write_json(name, obj):
        raw = json.dumps(obj, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
        z = zlib.compress(raw, 9)
        os.makedirs(os.path.join(outdir, 'json'), exist_ok=True)
        open(os.path.join(outdir, 'json', name + '.json.z'), 'wb').write(z)
        print('json/%s.json.z: %d bytes (%d raw)' % (name, len(z), len(raw)))

    build({'root': root, 'write_json': write_json})
