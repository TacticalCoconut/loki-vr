"""
reload_orders — the iteration order of rAthena's mob_db and skill_db after a reload (@reloadmobdb / @reloadskilldb,
atcommand.cpp ACMD_FUNC(reload) → mob.cpp mob_reload / skill.cpp skill_reload).

Both databases are TypesafeCachedYamlDatabase, whose `data` is a std::unordered_map (common/database.hpp). A reload
calls clear() (mob.cpp do_final_mob: mob_db.clear(); skill.cpp skill_reload: skill_db.clear()) and then load() again.
libstdc++'s unordered_map::clear() frees the nodes and zeroes the buckets but keeps the bucket count and the rehash
policy, so the second load inserts the same keys into a table that is already big enough: no rehash happens, every
key lands at the front of its (empty) bucket, and the iteration order is not the one of the first load (which went
through every rehash on the way up). Lookups that return the first match in iteration order (mob.cpp
mobdb_searchname, atcommand.cpp @skillid, mob_reload_itemmob_data) answer differently after a reload; every later
reload gives this same order again (same keys, same bucket count).

The key order of a load is the order of the Body nodes through the Footer imports (YamlDatabase::load / parseImports,
Mode "Renewal" only, Generator files skipped, Header Clear honoured). The first load is replayed through mobs.py's UMap
and checked against the order the mobs / skills modules wrote (mobs_db, skills_db): a mismatch stops the build rather
than writing an order that was never rAthena's.

  reload_orders  {"mob_db": [mob ids], "skill_db": [skill ids], "barter_db": [barter names]}: iteration order after one
                 (or any number of) reloads
"""
import json, os, zlib, importlib.util

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location('_reload_orders_mobs', os.path.join(_here, 'mobs.py'))
_mobs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_mobs)
UMap = _mobs.UMap


def _ops(ctx, rel, key, out):
    """YamlDatabase::load(rel) as a list of ('clear',) / ('put', id) in the order they reach the unordered_map."""
    tree = ctx['load_yaml'](rel)
    if tree is None:
        return
    header = tree.get('Header') or {}
    if isinstance(header, dict) and header.get('Clear') is True:
        out.append(('clear',))
    for node in tree.get('Body') or []:
        if isinstance(node, dict) and key in node:
            out.append(('put', int(node[key])))
    footer = tree.get('Footer') or {}
    for node in (footer.get('Imports') or []) if isinstance(footer, dict) else []:
        if not isinstance(node, dict) or 'Path' not in node:
            continue
        if 'Mode' in node and node['Mode'] != 'Renewal':
            continue
        if 'Generator' in node:
            continue  # shouldLoadGenerator is false on the map server
        _ops(ctx, node['Path'], key, out)


def _clear(um):
    """unordered_map::clear(): nodes gone, buckets zeroed, bucket count and rehash policy kept."""
    um.buckets = [None] * um.nb
    um.nxt, um.head, um.vals = {}, None, {}


def _replay(um, ops):
    for op in ops:
        if op[0] == 'clear':
            _clear(um)
        else:
            um[op[1]] = True
    res, p = [], um.head
    while p is not None:
        res.append(p)
        p = um.nxt[p]
    return res


def _written(ctx, name):
    path = os.path.join(ctx['outdir'], 'json', name + '.json.z')
    return json.loads(zlib.decompress(open(path, 'rb').read()).decode('utf-8'))


def _order(ctx, rel, first_load, label):
    ops = []
    _ops(ctx, rel, 'Id', ops)
    um = UMap()
    first = _replay(um, ops)
    if first != first_load:
        raise SystemExit('reload_orders: the replayed first load of %s does not give the order %s was written in'
                         % (rel, label))
    _clear(um)
    return _replay(um, ops)


def build(ctx):
    mob_first = [m['id'] for m in _written(ctx, 'mobs_db')['mobs']]
    skill_first = [s['nameid'] for s in _written(ctx, 'skills_db')['skills']]
    ctx['write_json']('reload_orders', {
        'mob_db': _order(ctx, 'db/mob_db.yml', mob_first, 'mobs_db'),
        'skill_db': _order(ctx, 'db/skill_db.yml', skill_first, 'skills_db'),
        # barter_db (npc.cpp BarterDatabase, a std::string-keyed unordered_map): replayed by items.py, which has the
        # barter loader (the barter names in the order barter_db iterates after a reload)
        'barter_db': ctx.get('barter_reload_order', []),
    })
