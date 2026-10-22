#!/usr/bin/env python3
"""
Builds the static game data the Loki Online module embeds (loki-server/data/*.bin) from an rAthena e985006 tree —
the same files rAthena reads at start-up. Run it once whenever the rAthena data changes; the module never needs
rAthena (or anything else) at run time.

  datagen.py <rathena root> <out dir>
  datagen.py <rathena root> <out dir> --install <Ragnarok Online folder>     (the maps only: see build_install_maps)

maps.bin  — every map rAthena loads (conf/maps_athena.conf `map:` lines, looked up in db/import/, db/re/, db/ map
            caches in that order — map.cpp map_readallmaps), with its cells exactly as the map cache stores them
            (zlib-compressed gat types, map.cpp map_readfromcache). These are rAthena's STOCK maps: the module's default
            build embeds them, and the parity suites need them (they compare with rAthena copies on the stock cache).
text overhaul — (wave 28, wtexts) the server text pack Loki Host carries (tools/lokihost/Core/TextOverhaul/server-text-pack.zip:
            rewritten NPC dialogs, mob chat, map messages, motd; every file verified by tools/textoverhaul to change only what
            players read) is a LAYER between the overlay and the rAthena tree (resolve()): a pack file is used where the
            tree's own file is still the one the pack was made from (its SHA-256 with LF line ends, as the manifest says;
            the rule Loki Host follows, Core/TextOverhaul.cs), else the tree's file is kept and named. --no-textpack builds
            from the rAthena tree alone; --textpack ZIP names another pack. The module then speaks the same texts as a
            Loki Host server, and the parity rig installs the same pack in its rAthena copies (parity test harness).
install/  — (--install) the same maps for a server whose players draw the maps of a Ragnarok Online install: an import
            map cache made from that install by Loki Host's rule (tools/mapcache/build_import_cache.py), accepted only
            when Loki Host's checks are clean (tools/mapcache/check_points.py), read first as rAthena reads
            db/import/map_cache.dat; with the install's map-data stamp (tools/mapcache/data_stamp.py). The module embeds
            them with `--features install_maps`: the build a host publishes.
"""
import atexit, hashlib, json, os, re, shutil, struct, sys, tempfile, zipfile, zlib

# the text overhaul layer (load_textpack): rAthena-relative path -> the extracted pack file
TEXTPACK = {}
TEXTPACK_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tools', 'lokihost', 'Core', 'TextOverhaul', 'server-text-pack.zip')


def _sha_lf(b):
    return hashlib.sha256(b.replace(b'\r\n', b'\n')).hexdigest()


def load_textpack(root, zpath):
    """The text overhaul layer: every pack file whose rAthena original is the tree's own file (SHA-256, LF line ends), extracted
    to a temporary folder that resolve() reads between the overlay and the tree. Prints what it used and what it left."""
    TEXTPACK.clear()
    z = zipfile.ZipFile(zpath)
    man = json.loads(z.read('manifest.json'))
    tmp = tempfile.mkdtemp(prefix='datagen-textpack-')
    atexit.register(shutil.rmtree, tmp, True)  # (this run's own extraction, nothing else)
    used, kept = 0, []
    for rel, e in sorted(man['files'].items()):
        src = os.path.join(root, rel)
        if not os.path.exists(src) or _sha_lf(open(src, 'rb').read()) != e['orig_sha256_lf']:
            kept.append(rel)
            continue
        b = z.read(rel)
        if _sha_lf(b) != e['new_sha256_lf']:
            raise SystemExit('the text pack does not match its manifest: ' + rel)
        dst = os.path.join(tmp, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, 'wb').write(b)
        TEXTPACK[rel] = dst
        used += 1
    print('text overhaul: version %d, %d of %d files from %s' % (man['version'], used, len(man['files']), os.path.normpath(zpath)))
    for rel in kept[:20]:
        print('  text overhaul: the tree\'s own %s is not the pack\'s original: kept' % rel)


def conf_lines(root, rel, seen=None):
    """rAthena conf reader: `key: value` lines, `import: file` recursion (common/conf.cpp config_read semantics)."""
    seen = seen if seen is not None else set()
    path = os.path.join(root, rel)
    if rel in seen or not os.path.exists(path):
        return
    seen.add(rel)
    for line in open(path, encoding='latin-1'):
        line = line.split('//')[0].strip()
        m = re.match(r'([^:]+):\s*(.*)$', line)
        if not m:
            continue
        k, v = m.group(1).strip(), m.group(2).strip()
        if k == 'import':
            yield from conf_lines(root, v, seen)
        else:
            yield k, v


def read_mapcache(path):
    """db/*/map_cache.dat: header {uint32 file_size; uint16 map_count} then {char name[12]; int16 xs; int16 ys; int32 len} + data."""
    out = {}
    if not os.path.exists(path):
        return out
    b = open(path, 'rb').read()
    if len(b) < 6:
        return out
    size, count = struct.unpack_from('<IH', b, 0)
    p = 8  # struct map_cache_main_header is 8 bytes with padding? checked below
    # rAthena: struct map_cache_main_header { uint32 file_size; uint16 map_count; }; sizeof = 8 (natural alignment, not packed)
    for _ in range(count):
        name = b[p:p + 12].split(b'\0')[0].decode('latin-1')
        xs, ys, ln = struct.unpack_from('<hhi', b, p + 12)
        data = b[p + 20:p + 20 + ln]
        if name not in out:
            out[name] = (xs, ys, data)
        p += 20 + ln
    return out


MAX_MAP_SIZE = 512 * 512  # map.hpp:82


def maps_bin_rows(blob):
    """A maps.bin as written below: [(name, xs, ys, zlib cells)] in file order (= the module's map ids, data.rs maps())."""
    assert blob[:4] == b'LKMP', 'not a maps.bin'
    count = struct.unpack_from('<I', blob, 8)[0]
    p, rows = 12, []
    for _ in range(count):
        n = blob[p]
        name = blob[p + 1:p + 1 + n].decode('latin-1')
        xs, ys, ln = struct.unpack_from('<HHI', blob, p + 1 + n)
        p += 1 + n + 8
        rows.append((name, xs, ys, bytes(blob[p:p + ln])))
        p += ln
    return rows


def build_maps(root, outdir, import_cache=None, label='maps.bin'):
    """import_cache: the import map cache to read first, in place of the tree's own db/import/map_cache.dat (the one
    build_install_maps makes from a Ragnarok Online install). Returns the file's bytes; the file is left alone when it
    already holds the same maps with the same cells (a zlib stream's bytes belong to the zlib that wrote it,
    tools/mapcache/cache_digest.py: the stock caches' streams are copied as they are, an install's are written here)."""
    names = [v for k, v in conf_lines(root, 'conf/map_athena.conf') if k == 'map']
    paths = [import_cache or os.path.join(root, 'db', 'import', 'map_cache.dat'), os.path.join(root, 'db', 're', 'map_cache.dat'), os.path.join(root, 'db', 'map_cache.dat')]
    caches = [read_mapcache(p) for p in paths]
    index = set()
    for line in open(os.path.join(root, 'db', 'map_index.txt'), encoding='latin-1'):
        line = line.split('//')[0].strip()
        if line:
            index.add(line.split()[0])
    blob = bytearray(b'LKMP' + struct.pack('<I', 1))
    rows, missing = [], []
    seen = set()
    for n in names:
        n = n[:11]  # mapindex_getmapname: MAP_NAME_LENGTH (11 + 1)
        if n in seen:
            continue
        # map.cpp:3967-3971 map_readallmaps: each map from the first cache in which map_readfromcache finds it;
        # map.cpp:3690-3699: an entry with a size of 0 or less, or of more than MAX_MAP_SIZE cells, is "not found" there
        # (the next cache is tried)
        hit = next((c[n] for c in caches if n in c and c[n][0] > 0 and c[n][1] > 0 and c[n][0] * c[n][1] <= MAX_MAP_SIZE), None)
        if hit is None or n not in index:
            missing.append(n)
            continue
        xs, ys, data = hit
        cells = zlib.decompress(data)
        assert len(cells) == xs * ys, (n, len(cells), xs, ys)
        seen.add(n)
        rows.append((n, xs, ys, data))
    blob += struct.pack('<I', len(rows))
    for n, xs, ys, data in rows:
        nb = n.encode('latin-1')
        blob += struct.pack('<B', len(nb)) + nb + struct.pack('<HHI', xs, ys, len(data)) + data
    blob = bytes(blob)
    path = os.path.join(outdir, 'maps.bin')
    if os.path.exists(path):
        old = open(path, 'rb').read()
        content = lambda b: [(n, xs, ys, zlib.decompress(d)) for n, xs, ys, d in maps_bin_rows(b)]
        if old != blob and old[:4] == b'LKMP' and content(old) == content(blob):
            blob = old  # the same maps and cells from another zlib: the file in place stays
    open(path, 'wb').write(blob)
    print('%s: %d maps, %d bytes (%d listed maps not in a cache or map_index: %s)' % (label, len(rows), len(blob), len(missing), ' '.join(missing[:12])))
    return blob


def build_install_maps(root, overlay, outdir, install):
    """<out dir>/install/: the module's maps for a server whose players draw the maps of the Ragnarok Online install at
    `install` (only ever read). What Loki Host does for its rAthena copy at Start (tools/lokihost/Core/MapDataWatch.cs,
    design notes §2.1), with the same code Loki Host's is a port of:
      1. the import map cache: tools/mapcache/build_import_cache.py build() (Loki Host: MapCacheBuilder.Build): every map
         rAthena's caches know whose size or walkable cells differ from the install's, with the install's cells made as
         rAthena's own mapcache tool makes them;
      2. the checks: tools/mapcache/check_points.py check() (Loki Host: PointsCheck.Run) over the NPC files THIS module
         loads (the rAthena tree and the overlay, resolve()): a warp, touch area, spawn or script point that is lost or
         cut off refuses the new maps: nothing is written, the files in place stay;
      3. install/map_cache.dat = that import cache (rAthena's own format; the file an rAthena copy takes as
         db/import/map_cache.dat to walk on the same cells: a parity run of this build needs it on the oracle side);
         install/maps.bin = build_maps with it read first, as rAthena reads db/import/map_cache.dat first
         (map.cpp:3919-3925, 3967-3971);
      4. install/mapstamp.txt: the install's map-data stamp and patch label (tools/mapcache/data_stamp.py = the client's
         IO/MapDataStamp.cs), which the module answers the client's data check with (src/presence.rs), and what was done.
    Only cell types leave the install, in rAthena's map cache format: no file of the game is copied."""
    tools = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tools', 'mapcache')
    sys.path.insert(0, tools)
    import build_import_cache as bic, check_points, data_stamp, cache_digest
    from mapcache_vs_grf import cache_cells, read_cache

    # Which files an install offers is the client's rule (client Data/RoInstall.cs, IO/RoLookup.cs): DATA.INI's archive
    # order when it has one, a loose data\ folder before or after the archives. The Python reader follows data.grf alone
    # (as tools/mapcache does): an install it would read differently from the client is refused, not half-read.
    grf_path = os.path.join(install, 'data.grf')
    if not os.path.isfile(grf_path):
        raise SystemExit('--install: no data.grf in %s: not a Ragnarok Online install folder?' % install)
    if any(e.lower() == 'data.ini' for e in os.listdir(install)):
        raise SystemExit('--install: %s has a DATA.INI (its own archive order): this tool reads data.grf alone and would not read '
                         'what the client reads. Nothing written.' % install)
    loose = os.path.join(install, 'data')
    if os.path.isdir(loose) and any(f.lower().endswith(('.gat', '.rsw')) for f in os.listdir(loose)):
        raise SystemExit('--install: %s has loose map files in data\\: this tool reads data.grf alone and would not read what '
                         'the client reads. Nothing written.' % install)

    def state():
        st = os.stat(grf_path)
        return st.st_size, st.st_mtime_ns, data_stamp.patch_label(install)
    before = state()
    grf = bic.Grf(grf_path)
    result, imp, replaced, water_only, missing = bic.build(grf, root)
    stamp, stamp_maps, stamp_cells, unreadable = data_stamp.grf_stamp(grf)
    label = data_stamp.patch_label(install)
    if state() != before:
        raise SystemExit('--install: data.grf or patch.inf changed while they were read (the official patcher at work?). Nothing written.')
    for line in bic.report('install/map_cache.dat', result, imp, replaced, water_only, missing):
        print(line)
    # what rAthena would refuse in a cache it loads (map.cpp:3690-3699; Loki Host: MapCacheBuilder.ServerWouldRefuse):
    # the map then comes from rAthena's own cache, here too (build_maps)
    refused = [n for n, (xs, ys, _c) in result.items() if xs <= 0 or ys <= 0 or xs * ys > MAX_MAP_SIZE]
    for n in refused:
        print('WARNING: %s: %d x %d cells: rAthena refuses that size and keeps its own version of the map; so does maps.bin' % (n, result[n][0], result[n][1]))

    lines = []
    src = root if os.path.exists(os.path.join(root, 'src', 'map', 'atcommand.cpp')) else None
    bad, lost, checked, reach = check_points.check(root, result, src, resolve=lambda rel: resolve(root, overlay, rel.replace('\\', '/')), out=lines.append)
    for line in lines:
        print(line)
    if bad or lost:
        print('install maps NOT written: %d point(s) or area(s) lost, %d cut off with these maps (the lines above). The files in place stay.' % (bad, lost))
        raise SystemExit(3)

    inst = os.path.join(outdir, 'install')
    os.makedirs(inst, exist_ok=True)
    cache_path = os.path.join(inst, 'map_cache.dat')
    new_cache = bic.cache_bytes(result)
    digest = cache_digest.content_digest(new_cache)[1]
    if os.path.exists(cache_path) and cache_digest.content_digest(open(cache_path, 'rb').read())[1] == digest:
        print('install/map_cache.dat: unchanged (%d maps, content %s)' % (len(result), digest))
    else:
        tmp = cache_path + '.new'
        with open(tmp, 'wb') as f:
            f.write(new_cache)
        bic.verify(tmp, grf, replaced)  # read back: every map taken from the install equals the install's cells
        os.replace(tmp, cache_path)
        print('install/map_cache.dat: %d bytes, %d maps, content %s' % (len(new_cache), len(result), digest))
    back = {n: cache_cells(e) for n, e in read_cache(cache_path).items()}
    assert back == {n: (xs, ys, bytes(c)) for n, (xs, ys, c) in result.items()}, 'install/map_cache.dat does not read back as built'

    stock = maps_bin_rows(build_maps(root, outdir))
    made = maps_bin_rows(build_maps(root, inst, import_cache=cache_path, label='install/maps.bin'))
    # the two builds must agree on which maps exist and on their ids (the list's order)
    assert [r[0] for r in stock] == [r[0] for r in made], 'the install maps.bin lists other maps than the stock one'
    walkable = lambda cells: bytes(1 if t in (0, 2, 3, 4, 6) else 0 for t in cells)  # map_gat2cell, map.cpp:3280-3299
    changed, resized, other = [], [], []
    for (n, xs, ys, d), (_n, xs2, ys2, d2) in zip(stock, made):
        a, b = zlib.decompress(d), zlib.decompress(d2)
        if (xs, ys) != (xs2, ys2):
            resized.append('%s %dx%d->%dx%d' % (n, xs, ys, xs2, ys2))
        elif walkable(a) != walkable(b):
            changed.append((n, sum(1 for x, y in zip(walkable(a), walkable(b)) if x != y)))
        elif a != b:
            other.append(n)
        if n in result and n not in refused:
            assert (xs2, ys2, b) == (result[n][0], result[n][1], bytes(result[n][2])), n  # the install's cells
        else:
            assert (xs, ys, a) == (xs2, ys2, b), n                                        # rAthena's own, untouched
    not_loaded = sorted(n for n in result if n not in {r[0] for r in made})
    print('install/maps.bin against maps.bin: %d of %d maps differ (%d in walkable cells, %d in size, %d otherwise); '
          '%d map(s) of the import cache are not in conf/maps_athena.conf: %s'
          % (len(changed) + len(resized) + len(other), len(made), len(changed), len(resized), len(other), len(not_loaded), ' '.join(not_loaded) or '-'))

    text = ['stamp: %s' % stamp, 'label: %s' % label,
            'stamp_maps: %d' % stamp_maps, 'stamp_cells: %d' % stamp_cells, ('stamp_unreadable: %d %s' % (len(unreadable), ' '.join(unreadable[:8]))).rstrip(),
            'import_cache: %d maps (%d kept, %d from the install), content %s' % (len(result), len(imp), len(replaced), digest),
            'left_alone_water_only: %d' % water_only, 'not_in_the_install: %d' % missing,
            'maps_bin: %d maps, %d differ from the stock maps.bin (%d walkable cells, %d size, %d otherwise)' % (len(made), len(changed) + len(resized) + len(other), len(changed), len(resized), len(other)),
            'points_check: %d checked, %d lost; reachability %d checked, %d cut off' % (checked, bad, reach, lost),
            'walkable_cells_changed: %s' % ' '.join('%s:%d' % c for c in sorted(changed, key=lambda c: (-c[1], c[0]))),
            'size_changed: %s' % ' '.join(resized)]
    with open(os.path.join(inst, 'mapstamp.txt'), 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(text) + '\n')
    print('install/mapstamp.txt: data stamp %s (%d maps, %d cells, %d unreadable), label "%s"' % (stamp, stamp_maps, stamp_cells, len(unreadable), label))


def resolve(root, overlay, rel):
    """The overlay (the local server's own files: custom NPCs, conf imports) wins over the rAthena tree. rAthena's
    build copies db/import-tmpl and conf/import-tmpl to db/import and conf/import when those are missing, so an
    import file the local server never changed is the template."""
    o = os.path.join(overlay, rel)
    if os.path.exists(o):
        return o
    t = TEXTPACK.get(rel.replace('\\', '/'))
    if t is not None:
        return t
    r = os.path.join(root, rel)
    if not os.path.exists(r):
        for d in ('db/import/', 'conf/import/'):
            if rel.startswith(d):
                t = os.path.join(root, d[:-1] + '-tmpl', rel[len(d):])
                if os.path.exists(t):
                    return t
    return r


def npc_files(root, overlay):
    """map.cpp map_reloadnpc(RENEWAL): npc/re/scripts_main.conf, `npc:` / `delnpc:` / `import:` lines, first
    registration of a file wins (npc_addsrcfile), in order (map_reloadnpc_sub's parse and comment stripping)."""
    files = []

    def sub(cfg):
        path = resolve(root, overlay, cfg)
        if not os.path.exists(path):
            print('  missing conf', cfg)
            return
        for line in open(path, encoding='latin-1'):
            if line.startswith('//'):
                continue
            i = line.find('//')
            if i >= 0:
                line = line[:i] + '\n'
            m = re.match(r'([^:]{1,1023}): ([^\t\r\n]{1,1023})', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2).rstrip(' ')
            if w1.lower() == 'npc':
                if w2.lower() == 'clear':
                    files.clear()
                elif not os.path.isfile(resolve(root, overlay, w2)):
                    print('  npc_addsrcfile: Can\'t find source file', w2)
                elif w2 not in files:
                    files.append(w2)
            elif w1.lower() == 'delnpc':
                if w2.lower() == 'all':
                    files.clear()
                elif w2 in files:
                    files.remove(w2)
            elif w1.lower() == 'import':
                sub(w2)
    sub('npc/re/scripts_main.conf')
    return files


def npc_other_files(root, overlay, boot):
    """Every other file under npc/ (the rAthena tree and the overlay; the overlay's copy wins, as resolve() says), sorted
    by path: at run time @loadnpc / @reloadnpcfile load any file rAthena finds on its disk (npc.cpp npc_addsrcfile's
    check_filepath, npc_parsesrcfile's fopen), and the module has no disk — so it embeds everything rAthena ships under
    npc/, not only the files npc/re/scripts_main.conf registers."""
    seen = set(boot)
    out = set()
    for base in (root, overlay):
        for d, _, fs in os.walk(os.path.join(base, 'npc')):
            for f in fs:
                rel = os.path.relpath(os.path.join(d, f), base).replace(os.sep, '/')
                if rel not in seen:
                    out.add(rel)
    return sorted(out)


def build_npcs(root, overlay, outdir):
    """npcs.bin v2: 'LKNP', u32 2, u32 raw length, u32 boot count, then zlib(u32 count, {u16 path length, path, u32
    text length, text}...). The first `boot count` files are the start-up set in load order (npc_files: the world the
    server builds at start-up); the others (npc_other_files) are only loaded at run time."""
    boot = npc_files(root, overlay)
    files = boot + npc_other_files(root, overlay, boot)
    raw = bytearray(struct.pack('<I', len(files)))
    total = 0
    for f in files:
        b = open(resolve(root, overlay, f), 'rb').read()
        fb = f.encode('latin-1')
        raw += struct.pack('<H', len(fb)) + fb + struct.pack('<I', len(b)) + b
        total += len(b)
    z = zlib.compress(bytes(raw), 9)
    open(os.path.join(outdir, 'npcs.bin'), 'wb').write(b'LKNP' + struct.pack('<III', 2, len(raw), len(boot)) + z)
    print('npcs.bin: %d files (%d loaded at start-up), %d bytes of script, %d compressed' % (len(files), len(boot), total, len(z)))


def build_consts(root, overlay, outdir, tsv):
    """Script constants: rAthena's source-defined ones (script_constants.hpp, dumped by the header probe
    loki/tools/packetgen/script_consts.cpp into script_constants.tsv) then db/const.yml (+ import)."""
    import yaml
    rows = []
    for line in open(tsv, encoding='latin-1'):
        n, v, param, dep = line.rstrip('\n').split('\t')
        rows.append((n, int(v), int(param), int(dep)))
    for rel in ('db/const.yml', 'db/import/const.yml'):
        path = resolve(root, overlay, rel)
        if not os.path.exists(path):
            continue
        d = yaml.load(open(path, encoding='latin-1'), Loader=yaml.CSafeLoader) or {}
        for e in d.get('Body') or []:
            rows.append((str(e['Name']), int(e['Value']), 1 if e.get('Parameter') else 0, 0))
    blob = bytearray(b'LKCN' + struct.pack('<II', 1, len(rows)))
    for n, v, param, dep in rows:
        nb = n.encode('latin-1')
        blob += struct.pack('<B', len(nb)) + nb + struct.pack('<qBB', v, param, dep)
    open(os.path.join(outdir, 'consts.bin'), 'wb').write(blob)
    print('consts.bin: %d constants' % len(rows))


def build_mob_names(root, overlay, outdir):
    """Phase 1 needs the monster ids (mobdb_checkid) and AegisNames (npc_parseview's fallback); the full monster
    database joins in phase 3. db/re/mob_db.yml then db/import/mob_db.yml (later entries update earlier ones)."""
    import yaml
    mobs = {}
    for rel in ('db/re/mob_db.yml', 'db/import/mob_db.yml'):
        path = resolve(root, overlay, rel)
        if not os.path.exists(path):
            continue
        d = yaml.load(open(path, encoding='latin-1'), Loader=yaml.CSafeLoader) or {}
        for e in d.get('Body') or []:
            mid = int(e['Id'])
            if 'AegisName' in e:
                mobs[mid] = str(e['AegisName'])
            elif mid not in mobs:
                mobs[mid] = ''
    blob = bytearray(b'LKMN' + struct.pack('<II', 1, len(mobs)))
    for mid in sorted(mobs):
        nb = mobs[mid].encode('latin-1')
        blob += struct.pack('<IB', mid, len(nb)) + nb
    open(os.path.join(outdir, 'mob_names.bin'), 'wb').write(blob)
    print('mob_names.bin: %d monsters' % len(mobs))


C_SPACE = ' \t\n\v\f\r'  # isspace() in the C locale


def fgets_lines(data, size=1024):
    """fgets(line, size, fp) over a whole file: pieces of at most size-1 bytes, each ending after '\\n' if it has one."""
    p = 0
    while p < len(data):
        end = data.find(b'\n', p, p + size - 1)
        end = end + 1 if end >= 0 else min(len(data), p + size - 1)
        yield data[p:end]
        p = end


def scan_msg_line(line):
    """sscanf(line, "%7[^:]: %511[^\\r\\n]", w1, w2) == 2: w1 = 1-7 characters up to ':', then ':', then any amount
    of whitespace (the format's ' ' directive skips none or more, newlines included), then w2 = 1-511 characters up
    to the end of the line. None when sscanf would not fill both."""
    i = 0
    while i < len(line) and i < 7 and line[i] != ':':
        i += 1
    if i == 0 or i >= len(line) or line[i] != ':':
        return None
    j = i + 1
    while j < len(line) and line[j] in C_SPACE:
        j += 1
    k = j
    while k < len(line) and k - j < 511 and line[k] not in '\r\n':
        k += 1
    if k == j:
        return None
    return line[:i], line[j:k]


def c_atoi(s):
    """atoi: leading whitespace, an optional sign, digits (0 when there are none)."""
    m = re.match(r'[ \t\n\v\f\r]*([+-]?\d+)', s)
    return int(m.group(1)) if m else 0


def build_msgs(root, overlay, outdir):
    """conf/msg_conf/map_msg.conf (msg_txt, MAP_MAX_MSG 1550 messages): common/msg_conf.cpp _msg_config_read line by
    line (fgets into 1024 bytes, `//` comment lines, sscanf "%7[^:]: %511[^\\r\\n]", case-insensitive `import:`
    recursion, the number through atoi into a uint16, later lines win)."""
    table = {}

    def read(rel):
        path = resolve(root, overlay, rel)
        if not os.path.exists(path):
            print('  Messages file not found: %s' % rel)
            return
        for raw in fgets_lines(open(path, 'rb').read()):
            line = raw.decode('latin-1')
            if line.startswith('//'):
                continue
            r = scan_msg_line(line)
            if r is None:
                continue
            w1, w2 = r
            if w1.lower() == 'import':
                read(w2)
            else:
                n = c_atoi(w1) & 0xFFFF  # uint16 msg_number
                if n < 1550:
                    table[n] = w2
                else:
                    print("  Invalid message ID '%s' in %s" % (w1, rel))
    read('conf/msg_conf/map_msg.conf')
    blob = bytearray(b'LKMS' + struct.pack('<II', 1, len(table)))
    for n in sorted(table):
        b = table[n].encode('latin-1')
        blob += struct.pack('<HH', n, len(b)) + b
    open(os.path.join(outdir, 'msgs.bin'), 'wb').write(blob)
    print('msgs.bin: %d messages' % len(table))


def build_motd(root, overlay):
    """conf/motd.txt as both readers take it (pc_read_motd for the login lines, ACMD_FUNC(gmotd) for @gmotd): the fgets
    pieces of CHAT_SIZE_MAX (255 + 1) bytes, trailing CR / LF stripped, `//` comment pieces kept (each reader skips them
    itself: conf.rs motd / atcommand_info.rs gmotd). The text pack rewrites this file, so the module's login message
    follows it like rAthena's does."""
    path = resolve(root, overlay, 'conf/motd.txt')
    if not os.path.exists(path):
        print("  File 'conf/motd.txt' not found.")
        return []
    return [raw.rstrip(b'\r\n').decode('latin-1') for raw in fgets_lines(open(path, 'rb').read(), 255 + 1)]


def write_json(outdir, name, obj):
    """A dataset the module reads with serde (data.rs json()): zlib-compressed compact JSON, latin-1 text as code points."""
    raw = json.dumps(obj, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    z = zlib.compress(raw, 9)
    os.makedirs(os.path.join(outdir, 'json'), exist_ok=True)
    open(os.path.join(outdir, 'json', name + '.json.z'), 'wb').write(z)
    print('json/%s.json.z: %d bytes (%d raw)' % (name, len(z), len(raw)))


def load_yaml(root, overlay, rel):
    import yaml
    path = resolve(root, overlay, rel)
    if not os.path.exists(path):
        return None
    return yaml.load(open(path, encoding='latin-1'), Loader=yaml.CSafeLoader) or {}


def script_constant_table(root, overlay, tsv):
    """script_get_constant: case-insensitive, first definition wins (see build_consts)."""
    t = {}
    for line in open(tsv, encoding='latin-1'):
        n, v, param, dep = line.rstrip('\n').split('\t')
        if int(param) == 0:
            t.setdefault(n.lower(), int(v))
    for rel in ('db/const.yml', 'db/import/const.yml'):
        d = load_yaml(root, overlay, rel)
        for e in (d or {}).get('Body') or []:
            if not e.get('Parameter'):
                t.setdefault(str(e['Name']).lower(), int(e['Value']))
    return t


def sv_rows(root, overlay, rel, delim, mincols):
    """common/utils.cpp sv_readdb: `//` comment lines and blank lines skipped, fields split on the delimiter."""
    path = resolve(root, overlay, rel)
    if not os.path.exists(path):
        return
    for line in open(path, encoding='latin-1'):
        line = line.rstrip('\r\n')
        if line.lstrip().startswith('//') or not line.strip():
            continue
        i = line.find('//')
        if i >= 0:
            line = line[:i]
        cols = [c.strip() for c in line.split(delim)]
        if len(cols) >= mincols:
            yield cols


def build_atcommands(root, overlay):
    """atcommand.cpp atcommand_basecommands (the ACMD_DEF list: name, function, restriction) and conf/atcommands.yml
    (+ import) aliases (AtcommandAliasDatabase: alias -> command, case-sensitive)."""
    src = open(os.path.join(root, 'src/map/atcommand.cpp'), encoding='latin-1').read()
    start = src.index('void atcommand_basecommands(void) {')
    body = src[start:src.index('};', start)]
    restr = {'ATCMD_NOCONSOLE': 1, 'ATCMD_NOSCRIPT': 2, 'ATCMD_NOAUTOTRADE': 4}
    cmds, seen = [], set()
    for m in re.finditer(r'ACMD_DEF(2?)(R?)\(([^)]*)\)', body):
        args = [a.strip() for a in m.group(3).split(',')]
        if m.group(1):
            name, func = args[0].strip('"'), args[1]
        else:
            name, func = args[0], args[0]
        r = 0
        if m.group(2):
            for part in args[-1].split('|'):
                r |= restr[part.strip()]
        if name.lower() in seen:
            continue  # "duplicate ACMD_DEF"
        seen.add(name.lower())
        cmds.append([name, func, r])
    # Loki test rig (not rAthena): @lokirnd fixes every roll, like the oracle's patched atcommand.cpp
    # (rathena-loki-rnd.patch), so combat can be compared packet for packet. Group 99 only.
    if 'lokirnd' not in seen:
        seen.add('lokirnd')
        cmds.insert([c[0] for c in cmds].index('mobinfo') + 1, ['lokirnd', 'lokirnd', 0])
    aliases, help_ = {}, {}
    for rel in ('conf/atcommands.yml', 'conf/import/atcommands.yml'):
        d = load_yaml(root, overlay, rel)
        for e in (d or {}).get('Body') or []:
            command = str(e['Command'])
            if command.lower() not in seen:
                print('  atcommands.yml: Command %s does not exist.' % command)
                continue
            if 'Help' in e:
                help_[command] = str(e['Help'])
            for a in e.get('Aliases') or []:
                a = str(a)
                if a.lower() in seen:
                    print('  atcommands.yml: "%s" cannot be used as alias for "%s"' % (a, command))
                    break
                aliases[a] = command
    return {'commands': cmds, 'aliases': aliases, 'help': help_}


PERMISSIONS = ['can_trade', 'can_party', 'all_skill', 'all_equipment', 'skill_unconditional', 'join_chat', 'kick_chat',
               'hide_session', 'who_display_aid', 'hack_info', 'any_warp', 'view_hpmeter', 'view_equipment', 'use_check',
               'use_changemaptype', 'all_commands', 'receive_requests', 'show_bossmobs', 'disable_pvm', 'disable_pvp',
               'disable_commands_when_dead', 'channel_admin', 'can_trade_bounded', 'item_unconditional', 'command_enable',
               'bypass_stat_onclone', 'bypass_max_stat', 'attendance', 'macro_detect', 'macro_register', 'trade_unconditional']


def build_groups(root, overlay, atcmds, stock=False):
    """pc_groups.cpp PlayerGroupDatabase: conf/groups.yml then conf/import/groups.yml (import entries patch the entry with
    the same Id), then loadingFinished's inheritance (commands and permission bits of the named groups).
    stock=True: rAthena's own conf/groups.yml alone, without Loki's import (the module's "release" mode, release.rs)."""
    names = {c[0].lower() for c in atcmds['commands']}
    groups, inheritance = {}, {}

    def commands(node, lst):
        for k, allowed in node.items():
            k = str(k)
            if k.lower() not in names:
                a = atcmds['aliases'].get(k)
                if a is None:
                    raise SystemExit('groups.yml: Unknown atcommand: %s' % k)
                k = a
            k = k.lower()
            if allowed:
                assert k not in lst, k
                lst.append(k)
            else:
                assert k in lst, k
                lst.remove(k)

    for rel in (('conf/groups.yml',) if stock else ('conf/groups.yml', 'conf/import/groups.yml')):
        d = load_yaml(root, overlay, rel)
        for e in (d or {}).get('Body') or []:
            gid = int(e['Id'])
            g = groups.get(gid)
            if g is None:
                if 'Name' not in e or 'Level' not in e:
                    continue
                g = {'id': gid, 'name': '', 'level': 0, 'log_commands': False, 'commands': [], 'char_commands': [], 'permissions': 0}
            if 'Name' in e:
                g['name'] = str(e['Name'])
            if 'Level' in e:
                g['level'] = min(int(e['Level']), 99)
            if 'LogCommands' in e:
                g['log_commands'] = bool(e['LogCommands'])
            if 'Commands' in e:
                commands(e['Commands'] or {}, g['commands'])
            if 'CharCommands' in e:
                commands(e['CharCommands'] or {}, g['char_commands'])
            for k, allowed in (e.get('Permissions') or {}).items():
                k = str(k).lower()
                if k == 'all_permission':
                    g['permissions'] = (1 << len(PERMISSIONS)) - 1 if allowed else 0
                elif k in PERMISSIONS:
                    bit = 1 << PERMISSIONS.index(k)
                    g['permissions'] = (g['permissions'] | bit) if allowed else (g['permissions'] & ~bit)
                else:
                    raise SystemExit('groups.yml: Unknown permission: %s' % k)
            inh = inheritance.setdefault(gid, [])
            for k, enable in (e.get('Inherit') or {}).items():
                k = str(k).lower()
                if enable:
                    if k not in inh:
                        inh.append(k)
                else:
                    inh.remove(k)
            groups[gid] = g
    if stock and 0 in groups:
        # Loki Host (tools/lokihost/Core/Conf.cs FriendCommands): release Group 0 (Player) also has @duel and @channel commands
        commands({k: True for k in ('duel', 'invite', 'accept', 'reject', 'leave', 'join', 'channel')}, groups[0]['commands'])
    for _ in range(10):
        for gid in list(inheritance):
            lst = inheritance[gid]
            if not lst:
                del inheritance[gid]
                continue
            g = groups[gid]
            for other in list(lst):
                og = next((o for i, o in sorted(groups.items()) if o['name'].lower() == other), None)
                if og is None:
                    print('  Inherited group "%s" for group id %d does not exist.' % (other, gid))
                    lst.remove(other)
                    continue
                if inheritance.get(og['id']):
                    continue  # try again in the next cycle
                for cmd in og['commands']:
                    if cmd not in g['commands']:
                        g['commands'].append(cmd)
                for cmd in og['char_commands']:
                    if cmd not in g['char_commands']:
                        g['char_commands'].append(cmd)
                g['permissions'] |= og['permissions']
                lst.remove(other)
        if not inheritance:
            break
    if inheritance:
        print('  Could not process inheritance rules, check your config for cycles...')
    return [groups[k] for k in sorted(groups)]


def build_job_noenter_map(root, overlay, consts):
    """pc.cpp pc_readdb_job_noenter_map: db/re/job_noenter_map.txt then db/import/ (job, zone, group level)."""
    rows = {}
    for rel in ('db/re/job_noenter_map.txt', 'db/import/job_noenter_map.txt'):
        for cols in sv_rows(root, overlay, rel, ',', 3):
            j = cols[0]
            job = int(j) if j[:1].isdigit() else consts.get(j.lower())
            if job is None:
                print('  pc_readdb_job_noenter_map: Invalid job %s specified.' % j)
                continue
            rows[job] = [job, int(cols[1]), int(cols[2])]
    return [rows[k] for k in sorted(rows)]


def build_json(root, overlay, outdir, tsv):
    consts = script_constant_table(root, overlay, tsv)
    atcmds = build_atcommands(root, overlay)
    write_json(outdir, 'atcommands', atcmds)
    write_json(outdir, 'groups', build_groups(root, overlay, atcmds))
    write_json(outdir, 'groups_stock', build_groups(root, overlay, atcmds, stock=True))
    write_json(outdir, 'job_noenter_map', build_job_noenter_map(root, overlay, consts))
    write_json(outdir, 'motd', build_motd(root, overlay))


if __name__ == '__main__':
    args = sys.argv[1:]
    install = None
    if '--install' in args:
        i = args.index('--install')
        if i + 1 >= len(args):
            raise SystemExit('--install <Ragnarok Online folder>')
        install = args[i + 1]
        del args[i:i + 2]
    textpack = TEXTPACK_DEFAULT
    if '--no-textpack' in args:
        args.remove('--no-textpack'); textpack = None
    if '--textpack' in args:
        i = args.index('--textpack')
        if i + 1 >= len(args):
            raise SystemExit('--textpack <zip>')
        textpack = args[i + 1]
        del args[i:i + 2]
    if len(args) != 2:
        raise SystemExit('usage: datagen.py <rathena root> <out dir> [--install <Ragnarok Online folder>] [--no-textpack | --textpack <zip>]')
    root, outdir = args
    if textpack is not None and os.path.exists(textpack):
        load_textpack(root, textpack)
    elif textpack is not None and textpack != TEXTPACK_DEFAULT:
        raise SystemExit('no text pack ' + textpack)
    else:
        print('text overhaul: none (rAthena\'s own texts)')
    overlay = os.path.join(outdir, 'overlay')
    os.makedirs(outdir, exist_ok=True)
    if install is not None:
        # the maps only (maps.bin as always, then install/): nothing else of the data depends on the install
        build_install_maps(root, overlay, outdir, install)
        sys.exit(0)
    build_maps(root, outdir)
    build_npcs(root, overlay, outdir)
    build_consts(root, overlay, outdir, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'script_constants.tsv'))
    build_mob_names(root, overlay, outdir)
    build_msgs(root, overlay, outdir)
    build_json(root, overlay, outdir, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'script_constants.tsv'))
    # database families (tools/datagen_mods/*.py, each with build(ctx)) in name order
    import importlib.util
    ctx = {'root': root, 'overlay': overlay, 'outdir': outdir, 'resolve': lambda rel: resolve(root, overlay, rel),
           'load_yaml': lambda rel: load_yaml(root, overlay, rel), 'sv_rows': lambda rel, d, n: sv_rows(root, overlay, rel, d, n),
           'write_json': lambda name, obj: write_json(outdir, name, obj),
           'consts': script_constant_table(root, overlay, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'script_constants.tsv'))}
    mods = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'datagen_mods')
    for fn in sorted(os.listdir(mods)):
        if fn.endswith('.py') and not fn.startswith('_'):
            spec = importlib.util.spec_from_file_location(fn[:-3], os.path.join(mods, fn))
            m = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(m)
            m.build(ctx)
