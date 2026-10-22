"""What rAthena reads from a map cache, whatever zlib wrote it (client wave 7, w7host).

A map cache holds each map's cells as a zlib stream (map.cpp:162-167, 3672-3713: the server inflates them with zlib's
uncompress, grfio.cpp:245-248). The stream's bytes depend on the zlib that made them: the same cells give different streams
with CPython 3.14 on Windows (zlib-ng), CPython 3.12 on Linux (zlib 1.3) and .NET 8 / 9 / 10. So two caches are the same
for the server when their CONTENT is: every entry in file order, its 12 name bytes, int16 xs, int16 ys and its inflated cells.
This prints the SHA-256 of exactly that (Loki Host's MapCache.ContentDigest is the same value), with the file's own.

usage: cache_digest.py <map_cache.dat> [...]"""
import hashlib, struct, sys, zlib


def content_digest(b):
    h = hashlib.sha256()
    file_size, count = struct.unpack_from('<IH', b, 0)
    p = 8
    for _ in range(count):
        name = b[p:p + 12]
        xs, ys, ln = struct.unpack_from('<hhi', b, p + 12)
        cells = zlib.decompress(b[p + 20:p + 20 + ln])
        if len(cells) != xs * ys:
            raise ValueError('cache cells %d != %d x %d (%r)' % (len(cells), xs, ys, name))
        h.update(name.split(b'\0', 1)[0].ljust(12, b'\0') + struct.pack('<hh', xs, ys) + cells)
        p += 20 + ln
    return count, h.hexdigest()


if __name__ == '__main__':
    for path in sys.argv[1:]:
        with open(path, 'rb') as f:
            b = f.read()
        count, digest = content_digest(b)
        print('%s: %d bytes, %d maps, sha256 %s, content %s' % (path, len(b), count, hashlib.sha256(b).hexdigest(), digest))
