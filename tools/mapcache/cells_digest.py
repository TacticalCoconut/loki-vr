"""Every map rAthena's caches know, with the cells the GRF gives for it (grf_cells: rAthena's own conversion, the water
level included): one line per map, "name xs ys sha256(cells)" or "name missing". Client wave 7 (w7host): the same list from
Loki Host's C# port (LokiMapTool cells) must be equal line for line; it checks the GRF reading (GAT files are DES-header
encrypted in places) and the cell conversion on every map, not only on the ones the import cache takes.

usage: cells_digest.py <data.grf> <rAthena dir (holds db/)>"""
import hashlib, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'client', 'tools', 'move'))
from grf import Grf
from mapcache_vs_grf import read_cache, grf_cells

grf_path, ra = sys.argv[1], sys.argv[2]
order = [os.path.join(ra, 'db', 'import', 'map_cache.dat'), os.path.join(ra, 'db', 're', 'map_cache.dat'), os.path.join(ra, 'db', 'map_cache.dat')]
caches = [read_cache(p) for p in order if os.path.exists(p)]
grf = Grf(grf_path)
for name in sorted({m for c in caches for m in c}):
    g = grf_cells(grf, name)
    print('%s missing' % name if g is None else '%s %d %d %s' % (name, g[0], g[1], hashlib.sha256(g[2]).hexdigest()))
