"""The map-data stamp of a Ragnarok Online install, in Python: the value a Loki client and its host compare ("g1-" + 16 hex
digits; client/Assets/Loki/Runtime/IO/MapDataStamp.cs Compute, which Loki Host compiles too). The C# lines are quoted beside
the code that follows them. Port wave I (imaps): the module's datagen embeds the stamp of the install its maps were made
from (server/tools/datagen.py --install), so the module answers the client's data check as Loki Host does.

What is hashed (MapDataStamp.cs:29-34), SHA-256, maps in ordinal order of their names:
  name (the entry's characters as Latin-1 bytes, without "data\\" and ".gat"), 0x00,
  then 0x01, int32 width, int32 height (little-endian), width x height bytes: each cell's type & 0xFF (the RAW type byte of the
  GAT, offset 14 + 16 + 20 i: not rAthena's water conversion)
  or, for a map file that cannot be read (damaged entry, cut short, impossible size): 0x00 alone.

Which files: every "data\\NAME.gat" directly in data\\ that the install offers (Loki Host: HostInstall.MapGatNames). This tool
reads ONE archive, as tools/mapcache/build_import_cache.py does: an install read from data.grf alone (no DATA.INI, no loose
data\\ map files: the LATAM install). Loki Host's LokiMapTool stamp follows the client's whole rule.

usage: data_stamp.py <Ragnarok Online folder>     prints "data stamp g1-... (N maps, C cells, U unreadable), label "patch N\""
"""
import hashlib, os, struct, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'client', 'tools', 'move'))
from grf import Grf

PREFIX = 'g1-'      # MapDataStamp.Prefix
MAX_SIDE = 4096     # MapDataStamp.MaxSide


def is_map_gat(n):
    """MapDataStamp.IsMapGat: "data\\NAME.gat" directly in data\\ (a normalized entry name: lower-case, backslashes).
        if (normalized == null || normalized.Length < 10) return false;
        if (!normalized.StartsWith("data\\") || !normalized.EndsWith(".gat")) return false;
        return normalized.IndexOf('\\', 5) < 0;"""
    return len(n) >= 10 and n.startswith('data\\') and n.endswith('.gat') and n.find('\\', 5) < 0


def cell_types(gat):
    """MapDataStamp.CellTypes: each cell's type byte, or None when the bytes are not a whole GAT.
        if (gat == null || gat.Length < 14) return null;
        xs = ToInt32(gat, 6); ys = ToInt32(gat, 10);
        if (xs <= 0 || ys <= 0 || xs > MaxSide || ys > MaxSide) return null;
        long n = (long)xs * ys; if (14 + 20 * n > gat.Length) return null;
        int p = 14 + 16; for (i < n; p += 20) cells[i] = gat[p];"""
    if gat is None or len(gat) < 14:
        return None
    xs, ys = struct.unpack_from('<ii', gat, 6)
    if xs <= 0 or ys <= 0 or xs > MAX_SIDE or ys > MAX_SIDE:
        return None
    n = xs * ys
    if 14 + 20 * n > len(gat):
        return None
    return xs, ys, bytes(gat[30:14 + 20 * n:20])


def data_stamp(gat_names, read):
    """MapDataStamp.Compute. gat_names: normalized "data\\NAME.gat" names (repeats and other names are ignored); read: a
    name to its bytes (None or an exception: that map is "unreadable", hashed as such, counted).
    Returns (stamp, maps, cells, unreadable names)."""
    names = sorted({n for n in gat_names if is_map_gat(n)})   # names.Sort(StringComparer.Ordinal): Latin-1 text, by code point
    sha = hashlib.sha256()
    maps = cells_total = 0
    unreadable = []
    for n in names:
        sha.update(n[5:-4].encode('latin-1') + b'\0')         # MapName(n) as Latin-1 bytes, then the 0 of nameBytes[map.Length]
        try:
            gat = read(n)
        except Exception:                                      # try { gat = read(n); } catch (Exception) { gat = null; }
            gat = None
        c = cell_types(gat)
        maps += 1
        if c is None:
            unreadable.append(n[5:-4])
            sha.update(b'\0')
            continue
        xs, ys, cells = c
        sha.update(b'\1' + struct.pack('<ii', xs, ys))
        sha.update(cells)
        cells_total += len(cells)
    return PREFIX + sha.hexdigest()[:16], maps, cells_total, unreadable


def patch_label(install_root):
    """MapDataStamp.PatchLabel / PatchNumber: patch.inf in the install folder, 4 bytes, a little-endian number: "patch 1486";
    "" when the install has no such file, or it is not 4 bytes, or it cannot be read."""
    try:
        with open(os.path.join(install_root, 'patch.inf'), 'rb') as f:
            b = f.read(5)
    except OSError:
        return ''
    return 'patch %d' % struct.unpack('<I', b)[0] if len(b) == 4 else ''


def grf_stamp(grf):
    """The stamp of an install that is read from this one archive."""
    return data_stamp(grf.entries.keys(), grf.read)


if __name__ == '__main__':
    root = sys.argv[1]
    stamp, maps, cells, unreadable = grf_stamp(Grf(os.path.join(root, 'data.grf')))
    print('data stamp %s (%d maps, %d cells, %d unreadable), label "%s"' % (stamp, maps, cells, len(unreadable), patch_label(root)))
