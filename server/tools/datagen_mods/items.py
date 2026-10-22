"""
Item databases, loaded exactly the way rAthena e985006 (RENEWAL, PACKETVER 20211103) loads them at map-server start:

  itemdb.cpp  itemdb_read(): ItemDatabase (db/item_db.yml -> db/re/item_db.yml -> item_db_usable/equip/etc.yml, then
              db/import/item_db.yml) + ItemDatabase::loadingFinished, item_noequip.txt (re + import),
              RandomOptionDatabase, RandomOptionGroupDatabase, ItemGroupDatabase, ComboDatabase (+ loadingFinished,
              which attaches the combos to the items), LaphineSynthesisDatabase, LaphineUpgradeDatabase,
              ItemReformDatabase, ItemEnchantDatabase, ItemPackageDatabase, itemdb_parse_roulette_db (the `db_roulette`
              SQL table: see ROULETTE_SQL)
  cashshop.cpp CashShopDatabase (db/item_cash.yml -> db/import/item_cash.yml, Loki's own shop)
  status.cpp  RefineDatabase (db/refine.yml), EnchantgradeDatabase (db/enchantgrade.yml)
  npc.cpp     StylistDatabase (db/stylist.yml), BarterDatabase (npc/barters.yml -> npc/re/merchants/barters.yml ->
              npc/re/merchants/barters/*.yml, npc/custom/barters.yml)

Every parseBodyNode is ported statement by statement (defaults for new entries, partial updates of existing entries,
`Clear`, amount-0 removals, caps, the order in which fields are applied and where a bad node aborts an entry), on top of
a port of common/database.cpp (Header/Version checks, Header.Clear, Footer.Imports with Mode filtering) and of the
ryml/c4core scalar conversions (common/database.cpp asType/asBool: c4::atou/atoi, no overflow check, `~`/`null`
plain scalars are null).

rAthena keeps most of these tables in std::unordered_map and a few loops iterate them (combos attached to an item,
item group sub groups/entries handed out by getgroupitem, barter NPC creation order ...). Those iteration orders are
reproduced with an emulation of libstdc++'s _Hashtable (the oracle is built with g++ 13 / libstdc++.so.6; the emulation
was differentially tested against that library): every list below that mirrors an unordered_map is written in
rAthena's iteration order; std::map ones are sorted; vectors keep insertion order.

The datasets (data/json/items_*.json.z) are read by src/db/items/*.rs.
"""
import bisect
import math
import os
import re

import yaml

# --------------------------------------------------------------------------------------- rAthena constants
# Values printed by a header probe compiled against the rAthena headers (PACKETVER 20211103, RENEWAL).
IT_HEALING, IT_USABLE, IT_ETC, IT_ARMOR, IT_WEAPON, IT_CARD, IT_PETEGG, IT_PETARMOR = 0, 2, 3, 4, 5, 6, 7, 8
IT_AMMO, IT_DELAYCONSUME, IT_SHADOWGEAR, IT_MAX = 10, 11, 12, 19
W_FIST, W_MUSICAL, W_WHIP, MAX_WEAPON_TYPE = 0, 13, 14, 24
AMMO_NONE, MAX_AMMO_TYPE = 0, 10
CARD_NORMAL, MAX_CARD_TYPE = 0, 2
SEX_FEMALE, SEX_MALE, SEX_BOTH = 0, 1, 2
EQP_SHIELD, EQP_SHADOW_GEAR = 32, 4128768
MAPID_ALL = 0xFFFFFFFFFFFFFFFF
MAPID_FIRSTMASK, JOBL_2_1, JOBL_2_2 = 0xff, 0x100, 0x200
ITEMJ_ALL = 127  # RENEWAL: NORMAL|UPPER|BABY|THIRD|THIRD_UPPER|THIRD_BABY|FOURTH
DELAYCONSUME_NONE, DELAYCONSUME_TEMP, DELAYCONSUME_NOCONSUME = 0, 1, 2
DROPEFFECT_NONE, DROPEFFECT_MAX = 0, 7
SC_NONE, SC_MAX = -1, 1044
MAX_ZENY = 2147483647
MAX_CASHPOINT = 2147483647
DEFTYPE_MAX = 32767
MAX_SLOTS = 4
MAX_WEAPON_LEVEL, MAX_ARMOR_LEVEL = 5, 2
MAX_LEVEL = 275
MAX_REFINE = 20
MAX_AMOUNT = 30000
MAX_ITEM_RDM_OPT = 5
ENCHANTGRADE_NONE, MAX_ENCHANTGRADE = 0, 4
ITEM_NAME_LENGTH = 50
ITEMID_DUMMY, UNKNOWN_ITEM_ID, ITEMID_APPLE = 499, 512, 512
WEDDING_RING_M, WEDDING_RING_F = 2634, 2635
IG_BLUEBOX = 1
GROUP_ALGORITHM_ALL, GROUP_ALGORITHM_SHAREDPOOL = 2, 4
BOUND_NONE, BOUND_MAX = 0, 5
CASHSHOP_TAB_NEW, CASHSHOP_TAB_MAX = 0, 9
REFINE_COST_MAX = 3
LOOK_HAIR, LOOK_HEAD_BOTTOM, LOOK_HEAD_TOP, LOOK_HEAD_MID = 1, 3, 4, 5
LOOK_HAIR_COLOR, LOOK_CLOTHES_COLOR, LOOK_BODY2 = 6, 7, 13
DIR_NORTH, DIR_MAX = 0, 8
JT_FAKENPC = -1
NPC_RANGE1_START, NPC_RANGE1_END, JT_HIDDEN_WARP_NPC = 44, 126, 139
NPC_RANGE2_START, NPC_RANGE2_END, NPC_RANGE3_START, NPC_RANGE3_END, JT_INVISIBLE = 400, 1000, 10000, 20000, 32767
MAX_BARTER_REQUIREMENTS = 6
MAX_ROULETTE_LEVEL, MAX_ROULETTE_COLUMNS = 7, 9


def npcdb_checkid(i):
    """npc.hpp npcdb_checkid."""
    return (NPC_RANGE1_START < i < NPC_RANGE1_END) or i == JT_HIDDEN_WARP_NPC or \
        (NPC_RANGE2_START < i < NPC_RANGE2_END) or i == JT_INVISIBLE or (NPC_RANGE3_START < i < NPC_RANGE3_END)


# --------------------------------------------------------------------------------------- libstdc++ unordered_map
# std::__detail::__prime_list of the system libstdc++.so.6 (64-bit: 256 + 48 primes + sentinel).
PRIMES = [int(x) for x in (
    "2,3,5,7,11,13,17,19,23,29,31,37,41,43,47,53,59,61,67,71,73,79,83,89,97,103,109,113,127,137,139,149,157,167,179,193,"
    "199,211,227,241,257,277,293,313,337,359,383,409,439,467,503,541,577,619,661,709,761,823,887,953,1031,1109,1193,1289,"
    "1381,1493,1613,1741,1879,2029,2179,2357,2549,2753,2971,3209,3469,3739,4027,4349,4703,5087,5503,5953,6427,6949,7517,"
    "8123,8783,9497,10273,11113,12011,12983,14033,15173,16411,17749,19183,20753,22447,24281,26267,28411,30727,33223,35933,"
    "38873,42043,45481,49201,53201,57557,62233,67307,72817,78779,85229,92203,99733,107897,116731,126271,136607,147793,"
    "159871,172933,187091,202409,218971,236897,256279,277261,299951,324503,351061,379787,410857,444487,480881,520241,"
    "562841,608903,658753,712697,771049,834181,902483,976369,1056323,1142821,1236397,1337629,1447153,1565659,1693859,"
    "1832561,1982627,2144977,2320627,2510653,2716249,2938679,3179303,3439651,3721303,4026031,4355707,4712381,5098259,"
    "5515729,5967347,6456007,6984629,7556579,8175383,8844859,9569143,10352717,11200489,12117689,13109983,14183539,"
    "15345007,16601593,17961079,19431899,21023161,22744717,24607243,26622317,28802401,31160981,33712729,36473443,"
    "39460231,42691603,46187573,49969847,54061849,58488943,63278561,68460391,74066549,80131819,86693767,93793069,"
    "101473717,109783337,118773397,128499677,139022417,150406843,162723577,176048909,190465427,206062531,222936881,"
    "241193053,260944219,282312799,305431229,330442829,357502601,386778277,418451333,452718089,489790921,529899637,"
    "573292817,620239453,671030513,725980837,785430967,849749479,919334987,994618837,1076067617,1164186217,1259520799,"
    "1362662261,1474249943,1594975441,1725587117,1866894511,2019773507,2185171673,2364114217,2557710269,2767159799,"
    "2993761039,3238918481,3504151727,3791104843,4101556399,4294967291,6442450933,8589934583,12884901857,17179869143,"
    "25769803693,34359738337,51539607367,68719476731,103079215087,137438953447,206158430123,274877906899,412316860387,"
    "549755813881,824633720731,1099511627689,1649267441579,2199023255531,3298534883309,4398046511093,6597069766607,"
    "8796093022151,13194139533241,17592186044399,26388279066581,35184372088777,52776558133177,70368744177643,"
    "105553116266399,140737488355213,211106232532861,281474976710597,562949953421231,1125899906842597,2251799813685119,"
    "4503599627370449,9007199254740881,18014398509481951,36028797018963913,72057594037927931,144115188075855859,"
    "288230376151711717,576460752303423433,1152921504606846883,2305843009213693951,4611686018427387847,"
    "9223372036854775783,18446744073709551557,18446744073709551557").split(',')]
M64 = 0xFFFFFFFFFFFFFFFF


def int_hash(k):
    """std::hash<integral>: static_cast<size_t>(k) (signed keys sign-extend)."""
    return k & M64


def str_hash(s):
    """std::hash<std::string>: std::_Hash_bytes(data, len, 0xc70f6907), the 64-bit Murmur variant (hash_bytes.cc)."""
    b = s.encode('latin-1')
    mul = (0xc6a4a793 << 32) + 0x5bd1e995
    ln = len(b)
    la = ln & ~7
    h = (0xc70f6907 ^ (ln * mul)) & M64
    for i in range(0, la, 8):
        d = (int.from_bytes(b[i:i + 8], 'little') * mul) & M64
        d = ((d ^ (d >> 47)) * mul) & M64
        h = ((h ^ d) * mul) & M64
    if ln & 7:
        h = ((h ^ int.from_bytes(b[la:ln], 'little')) * mul) & M64
    h = ((h ^ (h >> 47)) * mul) & M64
    return h ^ (h >> 47)


class _Node:
    __slots__ = ('key', 'val', 'h', 'nxt')

    def __init__(self, key, val, h):
        self.key, self.val, self.h, self.nxt = key, val, h, None


class UMap:
    """std::unordered_map with unique keys as libstdc++ (GCC 13 headers) implements it: _M_insert_unique_node /
    _M_insert_bucket_begin / _M_rehash_aux / _M_erase, _Prime_rehash_policy (max_load_factor 1, growth factor 2).
    Only the iteration order is of interest; lookups use a Python dict."""
    FAST_BKT = [2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13]

    def __init__(self, hashfn=int_hash):
        self.hashfn = hashfn
        self.nb = 1
        self.buckets = [None]
        self.before = _Node(None, None, 0)
        self.count = 0
        self.next_resize = 0
        self.nodes = {}

    def _next_bkt(self, n):
        if n < len(self.FAST_BKT):
            if n == 0:
                return 1
            self.next_resize = self.FAST_BKT[n]
            return self.FAST_BKT[n]
        last = len(PRIMES) - 2
        i = bisect.bisect_left(PRIMES, n, 6, last)
        self.next_resize = M64 if i == last else PRIMES[i]
        return PRIMES[i]

    def _need_rehash(self, n_bkt, n_elt, n_ins):
        if n_elt + n_ins > self.next_resize:
            min_bkts = float(max(n_elt + n_ins, 0 if self.next_resize else 11))
            if min_bkts >= n_bkt:
                return self._next_bkt(max(math.floor(min_bkts) + 1, n_bkt * 2))
            self.next_resize = n_bkt
        return 0

    def _rehash(self, n):
        nb = [None] * n
        p = self.before.nxt
        self.before.nxt = None
        bbegin = 0
        while p is not None:
            nxt = p.nxt
            b = p.h % n
            if nb[b] is None:
                p.nxt = self.before.nxt
                self.before.nxt = p
                nb[b] = self.before
                if p.nxt is not None:
                    nb[bbegin] = p
                bbegin = b
            else:
                p.nxt = nb[b].nxt
                nb[b].nxt = p
            p = nxt
        self.buckets, self.nb = nb, n

    def __contains__(self, key):
        return key in self.nodes

    def __getitem__(self, key):
        return self.nodes[key].val

    def get(self, key, default=None):
        n = self.nodes.get(key)
        return default if n is None else n.val

    def __setitem__(self, key, val):
        n = self.nodes.get(key)
        if n is not None:
            n.val = val
            return
        h = self.hashfn(key)
        node = _Node(key, val, h)
        n_new = self._need_rehash(self.nb, self.count, 1)
        if n_new:
            self._rehash(n_new)
        b = h % self.nb
        if self.buckets[b] is not None:
            node.nxt = self.buckets[b].nxt
            self.buckets[b].nxt = node
        else:
            node.nxt = self.before.nxt
            self.before.nxt = node
            if node.nxt is not None:
                self.buckets[node.nxt.h % self.nb] = node
            self.buckets[b] = self.before
        self.count += 1
        self.nodes[key] = node

    def erase(self, key):
        """unordered_map::erase(key): the number of erased elements."""
        n = self.nodes.pop(key, None)
        if n is None:
            return 0
        b = n.h % self.nb
        prev = self.buckets[b]
        while prev.nxt is not n:
            prev = prev.nxt
        if prev is self.buckets[b]:
            nxt = n.nxt
            nbk = nxt.h % self.nb if nxt is not None else 0
            if nxt is None or nbk != b:
                if nxt is not None:
                    self.buckets[nbk] = self.buckets[b]
                if self.buckets[b] is self.before:
                    self.before.nxt = nxt
                self.buckets[b] = None
        elif n.nxt is not None:
            nbk = n.nxt.h % self.nb
            if nbk != b:
                self.buckets[nbk] = prev
        prev.nxt = n.nxt
        self.count -= 1
        return 1

    def clear(self):
        self.buckets = [None] * self.nb
        self.count = 0
        self.before.nxt = None
        self.nodes = {}

    def __len__(self):
        return self.count

    def items(self):
        p = self.before.nxt
        while p is not None:
            yield p.key, p.val
            p = p.nxt

    def keys(self):
        for k, _ in self.items():
            yield k

    def values(self):
        for _, v in self.items():
            yield v


# --------------------------------------------------------------------------------------- YAML (ryml semantics)
class _Null:
    """A null scalar as ryml sees it (plain empty, ~, null, Null, NULL): asType fails with "missing a value"."""
    def __repr__(self):
        return 'NULL'


NULL = _Null()
_HI = {i: 0xE000 + i for i in range(0x80, 0x100)}
_LO = {0xE000 + i: i for i in range(0x80, 0x100)}


class YMap:
    """A mapping node: children in file order (keys may repeat; ryml's node[key] finds the first)."""
    __slots__ = ('pairs', 'first')

    def __init__(self, pairs):
        self.pairs = pairs
        self.first = {}
        for k, v in pairs:
            self.first.setdefault(k, v)


def _conv(n):
    if isinstance(n, yaml.ScalarNode):
        v = n.value
        if n.style in (None, '') and v in ('', '~', 'null', 'Null', 'NULL'):
            return NULL
        return v.translate(_LO) if any(ord(c) >= 0xE000 for c in v) else v
    if isinstance(n, yaml.MappingNode):
        return YMap([(_conv(k), _conv(v)) for k, v in n.value])
    return [_conv(v) for v in n.value]


def read_yaml(path):
    """The file's bytes as latin-1 code points (like the rest of datagen). libyaml refuses C1 control characters, which
    UTF-8 text read byte-wise contains, so bytes >= 0x80 travel through the parser as private-use characters."""
    text = open(path, 'rb').read().decode('latin-1').translate(_HI)
    node = yaml.compose(text, Loader=yaml.CBaseLoader)
    return _conv(node) if node is not None else YMap([])


def exists(node, name):
    """YamlDatabase::nodeExists: node.num_children() > 0 && node.has_child(name)."""
    return isinstance(node, YMap) and name in node.first


def children(node):
    """Iterating a ryml node: a sequence's elements, or a map's values."""
    if isinstance(node, list):
        return node
    if isinstance(node, YMap):
        return [v for _, v in node.pairs]
    return []


def c4_atou(s, bits):
    """c4::atou (+ from_chars): decimal, 0x/0b/0o prefixes, no sign, no whitespace; overflow wraps."""
    if not s or s[0] == '-':
        return None
    v = _c4_digits(s)
    return None if v is None else v & ((1 << bits) - 1)


def c4_atoi(s, bits):
    """c4::atoi: an optional '-' then as atou; the result wraps to the type."""
    if not s:
        return None
    neg = s[0] == '-'
    if neg:
        if len(s) == 1:
            return None
        s = s[1:]
    v = _c4_digits(s)
    if v is None:
        return None
    v = (-v if neg else v) & ((1 << bits) - 1)
    return v - (1 << bits) if v >> (bits - 1) else v


def _c4_digits(s):
    base, digits = 10, s
    if s[0] == '0' and len(s) > 1:
        p = s[1]
        if p in 'xX':
            base, digits = 16, s[2:]
        elif p in 'bB':
            base, digits = 2, s[2:]
        elif p in 'oO':
            base, digits = 8, s[2:]
        else:
            digits = s.lstrip('0') or '0'
    if not digits:
        return None
    ok = {10: '0123456789', 16: '0123456789abcdefABCDEF', 2: '01', 8: '01234567'}[base]
    if any(c not in ok for c in digits):
        return None
    return int(digits, base)


def lower(s):
    """util::tolower / std::tolower in the C locale: ASCII only."""
    return s.translate(_ASCII_LOWER)


_ASCII_LOWER = {i: i + 32 for i in range(ord('A'), ord('Z') + 1)}


def strtoul10(s):
    """strtoul(s, nullptr, 10): leading whitespace, optional sign, digits; 0 if none (unsigned long, then t_itemid)."""
    m = re.match(r'[ \t\n\v\f\r]*([+-]?)(\d*)', s)
    if not m.group(2):
        return 0
    v = int(m.group(2))
    if m.group(1) == '-':
        v = -v
    return v & 0xFFFFFFFF


def atoi(s):
    m = re.match(r'[ \t\n\v\f\r]*([+-]?\d+)', s)
    return int(m.group(1)) if m else 0


# --------------------------------------------------------------------------------------- common/database.cpp
class Loader:
    """Shared state of one datagen run: path resolution, script constants, the per-file counts rAthena prints."""

    def __init__(self, ctx):
        self.ctx = ctx
        self.consts = dict(ctx['consts'])  # RandomOptionDatabase adds RDMOPT_*; other plugins keep the original table
        self.done = []  # (path, count) — "Done reading 'N' entries in 'path'"
        self.warnings = 0

    def const(self, name):
        """script_get_constant: case-insensitive; C_INT constants only."""
        return self.consts.get(name.lower())


class YamlDatabase:
    TYPE, VERSION, MIN_VERSION = '', 0, 0

    def __init__(self, ld):
        self.ld = ld
        self.current_file = ''

    # --- helpers (YamlDatabase)
    def warn(self, msg):
        self.ld.warnings += 1
        print('  [%s] %s: %s' % (self.TYPE, self.current_file, msg.rstrip()))

    def nodes_exist(self, node, names):
        missing = [n for n in names if not exists(node, n)]
        for n in missing:
            self.warn('Missing mandatory node "%s".' % n)
        return not missing

    def _scalar(self, node, name):
        if not exists(node, name):
            self.warn('Missing node "%s".' % name)
            return None
        v = node.first[name]
        if v is NULL:
            self.warn('Node "%s" is missing a value.' % name)
            return None
        if not isinstance(v, str):
            self.warn('Node "%s" cannot be parsed.' % name)
            return None
        return v

    def as_string(self, node, name):
        return self._scalar(node, name)

    def as_bool(self, node, name):
        # asBool does not check existence first; every caller does.
        v = node.first.get(name, NULL) if isinstance(node, YMap) else NULL
        if v is NULL or not isinstance(v, str):
            self.warn('Node "%s" is missing a value.' % name)
            return None
        s = lower(v)
        if s == 'true':
            return True
        if s == 'false':
            return False
        self.warn('Unknown boolean value: "%s".' % s)
        return None

    def _as_int(self, node, name, bits, signed):
        s = self._scalar(node, name)
        if s is None:
            return None
        v = c4_atoi(s, bits) if signed else c4_atou(s, bits)
        if v is None:
            self.warn('Node "%s" cannot be parsed as %s%d.' % (name, 'int' if signed else 'uint', bits))
        return v

    def as_uint16(self, node, name):
        return self._as_int(node, name, 16, False)

    def as_int16(self, node, name):
        return self._as_int(node, name, 16, True)

    def as_uint32(self, node, name):
        return self._as_int(node, name, 32, False)

    def as_uint64(self, node, name):
        return self._as_int(node, name, 64, False)

    def _as_rate(self, node, name, bits, maximum):
        v = self._as_int(node, name, bits, False)
        if v is None:
            return None
        if v > maximum:
            self.warn('Node "%s" with value %d exceeds maximum of %d.' % (name, v, maximum))
            return None
        if v == 0:
            self.warn('Node "%s" needs to be at least 1.' % name)
            return None
        return v

    def as_uint16_rate(self, node, name, maximum=10000):
        return self._as_rate(node, name, 16, maximum)

    def as_uint32_rate(self, node, name, maximum=10000):
        return self._as_rate(node, name, 32, maximum)

    # --- loading (YamlDatabase::load)
    def load(self):
        ok = self._load(self.default_location())
        self.loading_finished()
        return ok

    def _load(self, path):
        real = self.ld.ctx['resolve'](path)
        if not os.path.isfile(real):
            print('  [%s] Failed to open database file from %s' % (self.TYPE, path))
            return False
        tree = read_yaml(real)
        self.current_file = path
        if not exists(tree, 'Header'):
            self.warn('No database "Header" was found.')
            return False
        header = tree.first['Header']
        t = self.as_string(header, 'Type')
        if t is None:
            return False
        if t != self.TYPE:
            self.warn('Database type mismatch: %s != %s.' % (self.TYPE, t))
            return False
        ver = self.as_uint16(header, 'Version')
        if ver is None:
            return False
        if ver != self.VERSION:
            if ver > self.VERSION:
                self.warn('Database version %d is not supported. Maximum version is: %d' % (ver, self.VERSION))
                return False
            elif ver >= self.MIN_VERSION:
                print('  [%s] %s: database version %d is outdated (current %d)' % (self.TYPE, path, ver, self.VERSION))
            else:
                self.warn('Database version %d is not supported anymore. Minimum version is: %d' % (ver, self.MIN_VERSION))
                return False
        if exists(header, 'Clear'):
            if self.as_bool(header, 'Clear'):
                self.clear()
        if exists(tree, 'Body'):
            count = 0
            for node in children(tree.first['Body']):
                count += self.parse_body_node(node)
            self.ld.done.append((path, count))
        if exists(tree, 'Footer'):
            footer = tree.first['Footer']
            if exists(footer, 'Imports'):
                for node in children(footer.first['Imports']):
                    imp = self.as_string(node, 'Path')
                    if imp is None:
                        continue
                    if exists(node, 'Mode'):
                        mode = self.as_string(node, 'Mode')
                        if mode is None or mode != 'Renewal':
                            continue
                    if exists(node, 'Generator'):
                        gen = self.as_bool(node, 'Generator')
                        if gen is None:
                            continue
                        continue  # shouldLoadGenerator is only set by the csv2yaml/yaml2sql tools
                    self._load(imp)
                    self.current_file = path
        return True

    def loading_finished(self):
        pass

    def clear(self):
        self.data.clear()


# --------------------------------------------------------------------------------------- battle_config
def battle_config(ld):
    """battle.cpp battle_config_read(conf/battle_athena.conf): `import:` recursion, battle_set_value (config_switch,
    out-of-range values fall back to the default), defaults and bounds from the battle_data table."""
    root = ld.ctx['root']
    src = open(os.path.join(root, 'src/map/battle.cpp'), encoding='latin-1').read()
    table = {}
    for m in re.finditer(r'\{\s*"([^"]+)",\s*&battle_config\.\w+,\s*(-?\d+),\s*(-?\d+|INT_MIN),\s*(-?\d+|INT_MAX),?\s*\}', src):
        v = lambda s: {'INT_MAX': 2147483647, 'INT_MIN': -2147483648}.get(s) if s in ('INT_MAX', 'INT_MIN') else int(s)
        table.setdefault(m.group(1).lower(), (int(m.group(2)), v(m.group(3)), v(m.group(4))))
    conf = {k: d for k, (d, _, _) in table.items()}

    def config_switch(s):
        if s.lower() in ('on', 'yes', 'oui', 'ja', 'si', 'sim'):
            return 1
        if s.lower() in ('off', 'no', 'non', 'nein', 'nao'):
            return 0
        m = re.match(r'\s*([+-]?)(0[xX][0-9a-fA-F]+|0[0-7]*|[1-9]\d*)', s)
        if not m:
            return 0
        n = int(m.group(2), 0) if not re.fullmatch(r'0[0-7]+', m.group(2)) else int(m.group(2), 8)
        return -n if m.group(1) == '-' else n

    def read(rel):
        path = ld.ctx['resolve'](rel)
        if not os.path.isfile(path):
            return
        for line in open(path, encoding='latin-1'):
            if line.startswith('//'):
                continue
            m = re.match(r'([^:]+):\s*(\S+)', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2)
            if w1.lower() == 'import':
                read(w2)
            elif w1.lower() in table:
                val = config_switch(w2)
                d, lo, hi = table[w1.lower()]
                conf[w1.lower()] = d if val < lo or val > hi else val
    read('conf/battle_athena.conf')
    return conf


# --------------------------------------------------------------------------------------- ItemDatabase
def new_item(nameid):
    """std::make_shared<item_data>(): value-initialised (every field 0/false/empty)."""
    return {
        'nameid': nameid, 'name': '', 'ename': '', 'value_buy': 0, 'value_sell': 0, 'type': 0, 'subtype': 0,
        'sex': 0, 'equip': 0, 'weight': 0, 'atk': 0, 'def': 0, 'range': 0, 'slots': 0, 'look': 0, 'elv': 0,
        'weapon_level': 0, 'armor_level': 0, 'view_id': 0, 'elvmax': 0, 'matk': 0, 'class_base': [0, 0, 0],
        'class_upper': 0, 'script': None, 'equip_script': None, 'unequip_script': None,
        'flag': {'available': False, 'no_equip': 0, 'no_refine': False, 'delay_consume': 0,
                 'trade_restriction': {k: False for k in ('drop', 'trade', 'trade_partner', 'sell', 'cart', 'storage',
                                                          'guild_storage', 'mail', 'auction')},
                 'buyingstore': False, 'dead_branch': False, 'group': False, 'guid': False, 'broadcast': False,
                 'bind_on_equip': False, 'drop_effect': 0, 'gradable': False},
        'stack': {'amount': 0, 'inventory': False, 'cart': False, 'storage': False, 'guild_storage': False},
        'item_usage': {'override': 0, 'sitting': False},
        'gm_lv_trade_override': 0, 'combos': [], 'delay': {'duration': 0, 'sc': 0},
    }


def isequip2(it):
    return it['type'] in (IT_WEAPON, IT_ARMOR, IT_AMMO, IT_SHADOWGEAR)


def isstackable2(it):
    return it['type'] not in (IT_WEAPON, IT_ARMOR, IT_PETEGG, IT_PETARMOR, IT_SHADOWGEAR)


def jobid2mapid(bclass, jobmask, active):
    """itemdb.cpp itemdb_jobid2mapid."""
    temp = [0, 0, 0]
    if jobmask != MAPID_ALL:
        job = (1 << ((jobmask & MAPID_FIRSTMASK) & 63)) & M64
        if jobmask & JOBL_2_1:
            temp[1] |= job
        elif jobmask & JOBL_2_2:
            temp[2] |= job
        else:
            temp[0] |= job
    else:
        temp = [MAPID_ALL] * 3
    for i in range(3):
        if temp[i] == 0:
            continue
        if active:
            bclass[i] |= temp[i]
        else:
            bclass[i] &= ~temp[i] & M64


class ItemDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_DB', 3, 1

    def __init__(self, ld):
        super().__init__(ld)
        self.data = {}
        self.aegis_map = {}  # aegisNameToItemDataMap
        self.name_map = {}   # nameToItemDataMap
        self.has_price = {}  # hasPriceValue

    def default_location(self):
        return 'db/item_db.yml'

    def search_aegisname(self, name):
        return self.aegis_map.get(lower(name))

    def default_gender(self, it):
        """ItemDatabase::defaultGender."""
        if it['nameid'] == WEDDING_RING_M:
            return SEX_MALE
        if it['nameid'] == WEDDING_RING_F:
            return SEX_FEMALE
        if it['type'] == IT_WEAPON:
            if it['subtype'] == W_MUSICAL:
                if it['sex'] != SEX_MALE:
                    self.warn('Musical instruments are always male-only, defaulting to SEX_MALE.')
                return SEX_MALE
            if it['subtype'] == W_WHIP:
                if it['sex'] != SEX_FEMALE:
                    self.warn('Whips are always female-only, defaulting to SEX_FEMALE.')
                return SEX_FEMALE
        return it['sex']

    def parse_body_node(self, node):
        nameid = self.as_uint32(node, 'Id')
        if nameid is None:
            return 0
        item = self.data.get(nameid)
        ex = item is not None
        if not ex:
            if not self.nodes_exist(node, ['AegisName', 'Name']):
                return 0
            item = new_item(nameid)
            item['flag']['available'] = True
        fl = item['flag']

        if exists(node, 'AegisName'):
            name = self.as_string(node, 'AegisName')
            if name is None:
                return 0
            if len(name) > ITEM_NAME_LENGTH:
                self.warn('AegisName "%s" exceeds maximum of %d characters, capping...' % (name, ITEM_NAME_LENGTH - 1))
            other = self.search_aegisname(name)
            if other is not None and other['nameid'] != nameid:
                self.warn('Found duplicate item Aegis name for %s, skipping.' % name)
                return 0
            if ex:
                self.aegis_map.pop(lower(item['name']), None)
            item['name'] = name  # resize(ITEM_NAME_LENGTH) then assignment: nothing is actually capped
            self.aegis_map[lower(name)] = item

        if exists(node, 'Name'):
            name = self.as_string(node, 'Name')
            if name is None:
                return 0
            if len(name) > ITEM_NAME_LENGTH:
                self.warn('Name "%s" exceeds maximum of %d characters, capping...' % (name, ITEM_NAME_LENGTH - 1))
            if ex:
                self.name_map.pop(lower(item['ename']), None)
            item['ename'] = name
            self.name_map[lower(name)] = item

        if exists(node, 'Type'):
            t = self.as_string(node, 'Type')
            if t is None:
                return 0
            c = self.ld.const('IT_' + t)
            if c is None or c < IT_HEALING or c >= IT_MAX:
                self.warn('Invalid item type %s, defaulting to IT_ETC.' % t)
                c = IT_ETC
            item['type'] = c
        elif not ex:
            item['type'] = IT_ETC

        if exists(node, 'SubType'):
            t = self.as_string(node, 'SubType')
            if t is None:
                return 0
            if item['type'] == IT_WEAPON:
                c = self.ld.const('W_' + t)
                if c is None or c < W_FIST or c >= MAX_WEAPON_TYPE:
                    self.warn('Invalid weapon type %s, defaulting to W_FIST.' % t)
                    # rAthena then assigns `constant` anyway (unset when the lookup failed)
                    c = W_FIST if c is None else c
                item['subtype'] = c & 0xff
            elif item['type'] == IT_AMMO:
                c = self.ld.const('AMMO_' + t)
                if c is None or c <= AMMO_NONE or c >= MAX_AMMO_TYPE:
                    self.warn('Invalid ammo type %s, defaulting to AMMO_NONE.' % t)
                    c = AMMO_NONE if c is None else c
                item['subtype'] = c & 0xff
            elif item['type'] == IT_CARD:
                c = self.ld.const('CARD_' + t)
                if c is None or c < CARD_NORMAL or c >= MAX_CARD_TYPE:
                    self.warn('Invalid card type %s, defaulting to CARD_NORMAL.' % t)
                    c = CARD_NORMAL if c is None else c
                item['subtype'] = c & 0xff
            else:
                self.warn('Item sub type is not supported for this item type.')
                return 0
        elif not ex:
            item['subtype'] = 0

        has_buy = has_sell = False
        if exists(node, 'Buy'):
            v = self.as_uint32(node, 'Buy')
            if v is None:
                return 0
            if v > MAX_ZENY:
                self.warn('Buying price exceeds MAX_ZENY. Capping...')
                v = MAX_ZENY
            has_buy = True
            item['value_buy'] = v
        elif not ex:
            item['value_buy'] = 0
        if exists(node, 'Sell'):
            v = self.as_uint32(node, 'Sell')
            if v is None:
                return 0
            if v > MAX_ZENY:
                self.warn('Sell price exceeds MAX_ZENY. Capping...')
                v = MAX_ZENY
            has_sell = True
            item['value_sell'] = v
        elif not ex:
            item['value_sell'] = 0
        self.has_price[nameid] = (has_buy, has_sell)

        for key, field in (('Weight', 'weight'), ('Attack', 'atk'), ('MagicAttack', 'matk')):
            if exists(node, key):
                v = self.as_uint32(node, key)
                if v is None:
                    return 0
                item[field] = v
            elif not ex:
                item[field] = 0

        if exists(node, 'Defense'):
            v = self.as_uint32(node, 'Defense')
            if v is None:
                return 0
            if v > DEFTYPE_MAX:
                self.warn('Item defense %d exceeds DEFTYPE_MAX (%d), capping to DEFTYPE_MAX.' % (v, DEFTYPE_MAX))
                v = DEFTYPE_MAX
            item['def'] = v
        elif not ex:
            item['def'] = 0

        if exists(node, 'Range'):
            v = self.as_uint16(node, 'Range')
            if v is None:
                return 0
            if v > self.area_size:
                self.warn('Item attack range %d exceeds AREA_SIZE (%d), capping to AREA_SIZE.' % (v, self.area_size))
                v = self.area_size
            item['range'] = v
        elif not ex:
            item['range'] = 0

        if exists(node, 'Slots'):
            v = self.as_uint16(node, 'Slots')
            if v is None:
                return 0
            if v > MAX_SLOTS:
                self.warn('Item slots %d exceeds MAX_SLOTS (%d), capping to MAX_SLOTS.' % (v, MAX_SLOTS))
                v = MAX_SLOTS
            item['slots'] = v
        elif not ex:
            item['slots'] = 0

        if exists(node, 'Jobs'):
            jn = node.first['Jobs']
            cb = item['class_base'] = [0, 0, 0]
            if exists(jn, 'All'):
                a = self.as_bool(jn, 'All')
                if a is None:
                    return 0
                jobid2mapid(cb, MAPID_ALL, a)
            for k, _ in (jn.pairs if isinstance(jn, YMap) else []):
                if k == 'All':
                    continue
                c = self.ld.const('EAJ_' + k)
                if c is None:
                    self.warn('Invalid item job %s, defaulting to All.' % k)
                    jobid2mapid(cb, MAPID_ALL, True)
                    break
                a = self.as_bool(jn, k)
                if a is None:
                    return 0
                jobid2mapid(cb, c & M64, a)
        elif not ex:
            item['class_base'] = [0, 0, 0]
            jobid2mapid(item['class_base'], MAPID_ALL, True)

        if exists(node, 'Classes'):
            cn = node.first['Classes']
            if exists(cn, 'All'):
                a = self.as_bool(cn, 'All')
                if a is None:
                    return 0
                item['class_upper'] = (item['class_upper'] | ITEMJ_ALL) if a else (item['class_upper'] & ~ITEMJ_ALL & 0xffff)
            for k, _ in (cn.pairs if isinstance(cn, YMap) else []):
                if k == 'All':
                    continue
                c = self.ld.const('ITEMJ_' + k)
                if c is None:
                    self.warn('Invalid class upper %s, defaulting to All.' % k)
                    item['class_upper'] |= ITEMJ_ALL
                    break
                a = self.as_bool(cn, k)
                if a is None:
                    return 0
                item['class_upper'] = ((item['class_upper'] | c) if a else (item['class_upper'] & ~c)) & 0xffff
        elif not ex:
            item['class_upper'] = ITEMJ_ALL

        if exists(node, 'Gender'):
            g = self.as_string(node, 'Gender')
            if g is None:
                return 0
            c = self.ld.const('SEX_' + g)
            if c is None or c < SEX_FEMALE or c > SEX_BOTH:
                self.warn('Invalid item gender %s, defaulting to SEX_BOTH.' % g)
                c = SEX_BOTH
            item['sex'] = c
            item['sex'] = self.default_gender(item)
        elif not ex:
            item['sex'] = SEX_BOTH
            item['sex'] = self.default_gender(item)

        if exists(node, 'Locations'):
            ln = node.first['Locations']
            for k, _ in (ln.pairs if isinstance(ln, YMap) else []):
                c = self.ld.const('EQP_' + k)
                if c is None:
                    self.warn('Invalid equip location %s, defaulting to IT_ETC.' % k)
                    item['type'] = IT_ETC
                    break
                a = self.as_bool(ln, k)
                if a is None:
                    return 0
                if a:
                    if c & EQP_SHADOW_GEAR and item['type'] != IT_SHADOWGEAR:
                        self.warn("Invalid item equip location %s as it's not a Shadow Gear item type, defaulting to IT_ETC." % k)
                        item['type'] = IT_ETC
                    item['equip'] = (item['equip'] | c) & 0xFFFFFFFF
                else:
                    item['equip'] &= ~c & 0xFFFFFFFF
        elif not ex:
            if isequip2(item):
                self.warn('Invalid item equip location as it has no equip location, defaulting to IT_ETC.')
                item['type'] = IT_ETC
            else:
                item['equip'] = 0

        if exists(node, 'WeaponLevel'):
            v = self.as_uint16(node, 'WeaponLevel')
            if v is None:
                return 0
            if v > MAX_WEAPON_LEVEL:
                self.warn('Invalid weapon level %d, defaulting to 0.' % v)
                v = 0
            if item['type'] != IT_WEAPON:
                self.warn('Item type is not a weapon, defaulting to 0.')
                v = 0
            item['weapon_level'] = v
        elif not ex:
            item['weapon_level'] = 0

        if exists(node, 'ArmorLevel'):
            v = self.as_uint16(node, 'ArmorLevel')
            if v is None:
                return 0
            if v > MAX_ARMOR_LEVEL:
                self.warn('Invalid armor level %d, defaulting to 0.' % v)
                v = 0
            if item['type'] != IT_ARMOR:
                self.warn('Item type is not an armor, defaulting to 0.')
                v = 0
            item['armor_level'] = v
        elif not ex:
            item['armor_level'] = 0

        if exists(node, 'EquipLevelMin'):
            v = self.as_uint16(node, 'EquipLevelMin')
            if v is None:
                return 0
            if v > MAX_LEVEL:
                self.warn('Minimum equip level %d exceeds MAX_LEVEL (%d), capping to MAX_LEVEL.' % (v, MAX_LEVEL))
                v = MAX_LEVEL
            item['elv'] = v
        elif not ex:
            item['elv'] = 0

        if exists(node, 'EquipLevelMax'):
            v = self.as_uint16(node, 'EquipLevelMax')
            if v is None:
                return 0
            if v < item['elv']:
                self.warn('Maximum equip level %d is less than minimum equip level %d, capping to minimum equip level.' % (v, item['elv']))
                v = item['elv']
            if v > MAX_LEVEL:
                self.warn('Maximum equip level %d exceeds MAX_LEVEL (%d), capping to MAX_LEVEL.' % (v, MAX_LEVEL))
                v = MAX_LEVEL
            item['elvmax'] = v
        elif not ex:
            item['elvmax'] = MAX_LEVEL

        if exists(node, 'Refineable'):
            a = self.as_bool(node, 'Refineable')
            if a is None:
                return 0
            fl['no_refine'] = not a
        elif not ex:
            fl['no_refine'] = True

        if exists(node, 'Gradable'):
            a = self.as_bool(node, 'Gradable')
            if a is None:
                return 0
            fl['gradable'] = a
        elif not ex:
            fl['gradable'] = False

        if exists(node, 'View'):
            v = self.as_uint32(node, 'View')
            if v is None:
                return 0
            item['look'] = v
        elif not ex:
            item['look'] = 0

        if exists(node, 'AliasName'):
            v = self.as_string(node, 'AliasName')
            if v is None:
                return 0
            vd = self.search_aegisname(v)
            if vd is None:
                self.warn('Unable to change the alias because %s is an unknown item.' % v)
                return 0
            item['view_id'] = vd['nameid']
        elif not ex:
            item['view_id'] = 0

        if exists(node, 'Flags'):
            fn = node.first['Flags']
            for key, field, check_stack in (('BuyingStore', 'buyingstore', True), ('DeadBranch', 'dead_branch', False),
                                            ('Container', 'group', False), ('UniqueId', 'guid', True),
                                            ('BindOnEquip', 'bind_on_equip', False), ('DropAnnounce', 'broadcast', False)):
                if exists(fn, key):
                    a = self.as_bool(fn, key)
                    if a is None:
                        return 0
                    if check_stack and not isstackable2(item) and a:
                        self.warn('Non-stackable item cannot be enabled for %s.' % ('buying store' if key == 'BuyingStore' else 'UniqueId'))
                        a = False
                    fl[field] = a
                elif not ex:
                    fl[field] = False
            if exists(fn, 'NoConsume'):
                a = self.as_bool(fn, 'NoConsume')
                if a is None:
                    return 0
                if a:
                    fl['delay_consume'] |= DELAYCONSUME_NOCONSUME
                else:
                    fl['delay_consume'] &= ~DELAYCONSUME_NOCONSUME
            elif not ex:
                if not fl['delay_consume'] & DELAYCONSUME_TEMP:
                    fl['delay_consume'] = DELAYCONSUME_NONE
            if exists(fn, 'DropEffect'):
                e = self.as_string(fn, 'DropEffect')
                if e is None:
                    return 0
                c = self.ld.const('DROPEFFECT_' + e)
                if c is None or c < DROPEFFECT_NONE or c > DROPEFFECT_MAX:
                    self.warn('Invalid item drop effect %s, defaulting to DROPEFFECT_NONE.' % e)
                    c = DROPEFFECT_NONE
                fl['drop_effect'] = c
            elif not ex:
                fl['drop_effect'] = DROPEFFECT_NONE
        elif not ex:
            for f in ('buyingstore', 'dead_branch', 'group', 'guid', 'bind_on_equip', 'broadcast'):
                fl[f] = False
            if not fl['delay_consume'] & DELAYCONSUME_TEMP:
                fl['delay_consume'] = DELAYCONSUME_NONE
            fl['drop_effect'] = DROPEFFECT_NONE

        if exists(node, 'Delay'):
            dn = node.first['Delay']
            if exists(dn, 'Duration'):
                v = self.as_uint32(dn, 'Duration')
                if v is None:
                    return 0
                item['delay']['duration'] = v
            elif not ex:
                item['delay']['duration'] = 0
            if exists(dn, 'Status'):
                s = self.as_string(dn, 'Status')
                if s is None:
                    return 0
                c = self.ld.const('SC_' + s)
                if c is None or c < SC_NONE or c >= SC_MAX:
                    self.warn('Invalid item delay status %s, defaulting to SC_NONE.' % s)
                    c = SC_NONE
                item['delay']['sc'] = c
            elif not ex:
                item['delay']['sc'] = SC_NONE
        elif not ex:
            item['delay'] = {'duration': 0, 'sc': SC_NONE}

        if exists(node, 'Stack'):
            sn = node.first['Stack']
            if exists(sn, 'Amount'):
                v = self.as_uint16(sn, 'Amount')
                if v is None:
                    return 0
                if not isstackable2(item):
                    self.warn('Non-stackable item cannot be enabled for stacking.')
                    v = 0
                item['stack']['amount'] = v
            elif not ex:
                item['stack']['amount'] = 0
            for key, field in (('Inventory', 'inventory'), ('Cart', 'cart'), ('Storage', 'storage'), ('GuildStorage', 'guild_storage')):
                if exists(sn, key):
                    a = self.as_bool(sn, key)
                    if a is None:
                        return 0
                    item['stack'][field] = a
                elif not ex:
                    item['stack'][field] = False
        elif not ex:
            item['stack'] = {'amount': 0, 'inventory': False, 'cart': False, 'storage': False, 'guild_storage': False}

        if exists(node, 'NoUse'):
            nn = node.first['NoUse']
            if exists(nn, 'Override'):
                v = self.as_uint16(nn, 'Override')
                if v is None:
                    return 0
                if v > 100:
                    self.warn('Item no use override level %d exceeds 100, capping to 100.' % v)
                    v = 100
                item['item_usage']['override'] = v
            elif not ex:
                item['item_usage']['override'] = 100
            if exists(nn, 'Sitting'):
                a = self.as_bool(nn, 'Sitting')
                if a is None:
                    return 0
                item['item_usage']['sitting'] = a
            elif not ex:
                item['item_usage']['sitting'] = False
        elif not ex:
            item['item_usage'] = {'override': 100, 'sitting': False}

        tr = fl['trade_restriction']
        if exists(node, 'Trade'):
            tn = node.first['Trade']
            if exists(tn, 'Override'):
                v = self.as_uint16(tn, 'Override')
                if v is None:
                    return 0
                if v > 100:
                    self.warn('Item trade override level %d exceeds 100, capping to 100.' % v)
                    v = 100
                item['gm_lv_trade_override'] = v
            elif not ex:
                item['gm_lv_trade_override'] = 100
            for key, field in (('NoDrop', 'drop'), ('NoTrade', 'trade'), ('TradePartner', 'trade_partner'), ('NoSell', 'sell'),
                               ('NoCart', 'cart'), ('NoStorage', 'storage'), ('NoGuildStorage', 'guild_storage'),
                               ('NoMail', 'mail'), ('NoAuction', 'auction')):
                if exists(tn, key):
                    a = self.as_bool(tn, key)
                    if a is None:
                        return 0
                    tr[field] = a
                elif not ex:
                    tr[field] = False
        elif not ex:
            item['gm_lv_trade_override'] = 100
            for f in tr:
                tr[f] = False

        for key, field in (('Script', 'script'), ('EquipScript', 'equip_script'), ('UnEquipScript', 'unequip_script')):
            if exists(node, key):
                s = self.as_string(node, key)
                if s is None:
                    return 0
                item[field] = s  # parse_script(..., SCRIPT_IGNORE_EXTERNAL_BRACKETS) happens in the script engine
            elif not ex:
                item[field] = None

        if not ex:
            self.data[nameid] = item
        return 1

    def loading_finished(self):
        """ItemDatabase::loadingFinished."""
        for it in self.data.values():
            fl = it['flag']
            if it['type'] == IT_DELAYCONSUME:
                it['type'] = IT_USABLE
                fl['delay_consume'] |= DELAYCONSUME_TEMP
            else:
                fl['delay_consume'] &= ~DELAYCONSUME_TEMP
            if it['type'] == IT_WEAPON:
                if it['weapon_level'] == 0:
                    it['weapon_level'] = 1
                if it['armor_level'] != 0:
                    it['armor_level'] = 0
            elif it['type'] == IT_ARMOR:
                if it['armor_level'] == 0:
                    it['armor_level'] = 1
                if it['weapon_level'] != 0:
                    it['weapon_level'] = 0
            else:
                it['weapon_level'] = 0
                it['armor_level'] = 0
            if it['type'] not in (IT_ARMOR, IT_SHADOWGEAR) and it['def'] > 0:
                it['def'] = 0
            if it['type'] not in (IT_WEAPON, IT_AMMO) and it['atk'] > 0:
                it['atk'] = 0
            if it['type'] != IT_WEAPON:
                it['matk'] = 0
                it['range'] = 0
            hb, hs = self.has_price.get(it['nameid'], (False, False))
            if not hb and hs:
                it['value_buy'] = (it['value_sell'] * 2) & 0xFFFFFFFF
            elif hb and not hs:
                it['value_sell'] = it['value_buy'] // 2
            if it['value_buy'] / 124. < it['value_sell'] / 75.:
                it['value_sell'] = 1
            if it['type'] == IT_ARMOR and it['equip'] & EQP_SHIELD and it['look'] == 0:
                it['look'] = 1
        if ITEMID_DUMMY not in self.data:
            d = new_item(ITEMID_DUMMY)
            d.update(weight=1, value_sell=1, type=IT_ETC, name='UNKNOWN_ITEM', ename='Unknown Item', view_id=UNKNOWN_ITEM_ID)
            self.data[ITEMID_DUMMY] = d  # put() only: not in the AegisName/Name lookups
        self.has_price.clear()


def read_noequip(ld, idb):
    """itemdb_read: sv_readdb(db/re/, item_noequip.txt) then (db/import, silent); itemdb_read_noequip per row."""
    for rel in ('db/re/item_noequip.txt', 'db/import/item_noequip.txt'):
        path = ld.ctx['resolve'](rel)
        if not os.path.isfile(path):
            continue
        entries = 0
        for line in open(path, 'rb').read().decode('latin-1').splitlines(True):
            i = line.find('//')
            if i >= 0:
                line = line[:i]
            if line[:1] in ('', '\n', '\r'):
                continue
            for t in ('\r\n', '\n'):
                if t in line:
                    line = line[:line.index(t)]
            cols = line.split(',')
            if len(cols) < 2 or len(cols) > 2:
                print('  sv_readdb: bad column count in %s: %r' % (rel, line))
                continue
            nameid, flag = strtoul10(cols[0]), atoi(cols[1])
            it = idb.data.get(nameid)
            if it is None:
                print('  itemdb_read_noequip: Invalid item id %u.' % nameid)
                continue
            if flag >= 0:
                it['flag']['no_equip'] = (it['flag']['no_equip'] | flag) & 0xFFFFFFFF
            else:
                it['flag']['no_equip'] &= ~abs(flag) & 0xFFFFFFFF
            entries += 1
        ld.done.append((rel, entries))


# --------------------------------------------------------------------------------------- random options
class RandomOptionDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'RANDOM_OPTION_DB', 1, 1

    def __init__(self, ld):
        super().__init__(ld)
        self.data = UMap()
        self.constants = []

    def default_location(self):
        return 'db/item_randomopt_db.yml'

    def option_get_id(self, name):
        for k, v in self.data.items():
            if v['name'] == name:
                return k
        return None

    def parse_body_node(self, node):
        oid = self.as_uint16(node, 'Id')
        if oid is None:
            return 0
        o = self.data.get(oid)
        ex = o is not None
        if not ex:
            if not self.nodes_exist(node, ['Option', 'Script']):
                return 0
            o = {'id': oid, 'name': '', 'script': None}
        if exists(node, 'Option'):
            name = self.as_string(node, 'Option')
            if name is None:
                return 0
            if o['name'] != name and self.option_get_id(name) is not None:
                self.warn('Found duplicate random option name for %s, skipping.' % name)
                return 0
            o['name'] = name
        if exists(node, 'Script'):
            s = self.as_string(node, 'Script')
            if s is None:
                return 0
            o['script'] = s
        if not ex:
            self.data[oid] = o
        return 1

    def loading_finished(self):
        """Exports RDMOPT_<Option> = id to the script engine (script_set_constant never overwrites a name)."""
        for oid, o in self.data.items():
            name = 'RDMOPT_' + o['name']
            c = self.ld.const(name)
            if c is not None:
                if c != oid:
                    print('  script_set_constant: Attempted to overwrite existing constant %s' % name)
                continue
            self.ld.consts[name.lower()] = oid
            self.constants.append([name, oid])


class RandomOptionGroupDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'RANDOM_OPTION_GROUP', 1, 1

    def __init__(self, ld, ropt):
        super().__init__(ld)
        self.data = UMap()
        self.ropt = ropt

    def default_location(self):
        return 'db/item_randomopt_group.yml'

    def option_get_id(self, name):
        for k, v in self.data.items():
            if v['name'] == name:
                return k
        return None

    def add_option(self, node):
        if exists(node, 'Option'):
            n = self.as_string(node, 'Option')
            if n is None:
                return None
            oid = self.ropt.option_get_id(n)
            if oid is None:
                self.warn('Invalid Random Option name %s given.' % n)
                return None
        else:
            self.warn('Missing Option node.')
            return None
        e = {'id': oid, 'min_value': 0, 'max_value': 0, 'param': 0, 'chance': 0}
        for key, field in (('MinValue', 'min_value'), ('MaxValue', 'max_value')):
            if exists(node, key):
                v = self.as_int16(node, key)
                if v is None:
                    return None
                e[field] = v
        if e['min_value'] > e['max_value']:
            self.warn('MinValue %d is greater than MaxValue %d, setting MaxValue to MinValue + 1.' % (e['min_value'], e['max_value']))
            e['max_value'] = c16(e['min_value'] + 1)
        if exists(node, 'Param'):
            v = self.as_int16(node, 'Param')
            if v is None:
                return None
            e['param'] = ((v & 0xff) ^ 0x80) - 0x80  # static_cast<int8>
        if exists(node, 'Chance'):
            v = self.as_uint16_rate(node, 'Chance')
            if v is None:
                return None
            e['chance'] = v
        return e

    def parse_body_node(self, node):
        gid = self.as_uint16(node, 'Id')
        if gid is None:
            return 0
        g = self.data.get(gid)
        ex = g is not None
        if not ex:
            if not self.nodes_exist(node, ['Group']):
                return 0
            g = {'id': gid, 'name': '', 'slots': {}, 'max_random': 0, 'random_options': []}
        if exists(node, 'Group'):
            name = self.as_string(node, 'Group')
            if name is None:
                return 0
            if g['name'] != name and self.option_get_id(name) is not None:
                self.warn('Found duplicate random option group name for %s, skipping.' % name)
                return 0
            g['name'] = name
        if exists(node, 'Slots'):
            for sn in children(node.first['Slots']):
                if len(g['slots']) >= MAX_ITEM_RDM_OPT:
                    self.warn('Reached maximum of %d Random Option group options. Skipping the remaining slots...' % MAX_ITEM_RDM_OPT)
                    break
                slot = self.as_uint16(sn, 'Slot')
                if slot is None:
                    return 0
                if slot < 1 or slot > MAX_ITEM_RDM_OPT:
                    self.warn('Invalid Random Opton Slot number %d given, must be between 1~%d, skipping.' % (slot, MAX_ITEM_RDM_OPT))
                    return 0
                if not exists(sn, 'Options'):
                    self.warn('Random option slot does not contain Options node, skipping.')
                    return 0
                entries = []
                for on in children(sn.first['Options']):
                    e = self.add_option(on)
                    if e is None:
                        return 0
                    entries.append(e)
                g['slots'][slot - 1] = entries
        if exists(node, 'MaxRandom'):
            v = self.as_uint16(node, 'MaxRandom')
            if v is None:
                return 0
            if v > MAX_ITEM_RDM_OPT:
                self.warn('Exceeds the maximum of %d Random Option group options, capping to MAX_ITEM_RDM_OPT.' % MAX_ITEM_RDM_OPT)
                v = MAX_ITEM_RDM_OPT
            g['max_random'] = v
        elif not ex:
            g['max_random'] = 0
        if exists(node, 'Random'):
            g['random_options'] = []
            for rn in children(node.first['Random']):
                e = self.add_option(rn)
                if e is None:
                    return 0
                g['random_options'].append(e)
        if not ex:
            self.data[gid] = g
        return 1


def cap_value(a, lo, hi):
    """common/utils.hpp cap_value."""
    return hi if a >= hi else lo if a <= lo else a


def c16(v):
    v &= 0xffff
    return v - 0x10000 if v & 0x8000 else v


# --------------------------------------------------------------------------------------- item groups
class ItemGroupDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_GROUP_DB', 5, 1

    def __init__(self, ld, idb, rgroup, conf):
        super().__init__(ld)
        self.data = UMap()
        self.idb, self.rgroup, self.conf = idb, rgroup, conf

    def default_location(self):
        return 'db/item_group_db.yml'

    def parse_body_node(self, node):
        gname = self.as_string(node, 'Group')
        if gname is None:
            return 0
        c = self.ld.const('IG_' + gname)
        if c is None or c < IG_BLUEBOX:
            self.warn('Invalid group %s.' % gname)
            return 0
        gid = c & 0xffff
        group = self.data.get(gid)
        ex = group is not None
        if not ex:
            group = {'id': gid, 'random': UMap()}
        if exists(node, 'SubGroups'):
            for sub in children(node.first['SubGroups']):
                if exists(sub, 'Clear'):
                    cid = self.as_uint16(sub, 'Clear')
                    if cid is None:
                        continue
                    if group['random'].erase(cid) == 0:
                        self.warn("The SubGroup %d doesn't exist in the group %s. Clear failed." % (cid, gname))
                    continue
                if not self.nodes_exist(sub, ['SubGroup', 'List']):
                    continue
                subgroup = self.as_uint16(sub, 'SubGroup')
                if subgroup is None:
                    continue
                rnd = group['random'].get(subgroup)
                rex = rnd is not None
                if not rex:
                    rnd = {'algorithm': 0, 'total_rate': 0, 'data': UMap()}
                    group['random'][subgroup] = rnd
                if exists(sub, 'Algorithm'):
                    a = self.as_string(sub, 'Algorithm')
                    if a is None:
                        return 0
                    ac = self.ld.const('GROUP_ALGORITHM_' + a)
                    if ac is None:
                        self.warn('Invalid algorithm %s.' % a)
                        continue
                    rnd['algorithm'] = ac
                elif not rex:
                    rnd['algorithm'] = GROUP_ALGORITHM_SHAREDPOOL
                for li in children(sub.first['List']):
                    index = self.as_uint32(li, 'Index')
                    if index is None:
                        continue
                    if exists(li, 'Clear'):
                        a = self.as_bool(li, 'Clear')
                        if not a:
                            continue
                        if rnd['data'].erase(index) == 0:
                            self.warn("Index %d doesn't exist in the SubGroup %d (group %s). Clear failed." % (index, subgroup, gname))
                        continue
                    e = rnd['data'].get(index)
                    eex = e is not None
                    if not eex:
                        if not self.nodes_exist(li, ['Item']):
                            return 0
                        e = {'index': index, 'nameid': 0, 'rate': 0, 'adj_rate': 0, 'duration': 0, 'amount': 0,
                             'is_announced': False, 'guid': False, 'is_stacked': False, 'is_named': False, 'bound': 0,
                             'random_option_group': None, 'refine_minimum': 0, 'refine_maximum': 0,
                             'minimum_enchantgrade': 0, 'maximum_enchantgrade': 0}
                        rnd['data'][index] = e
                    it = None
                    if exists(li, 'Item'):
                        iname = self.as_string(li, 'Item')
                        if iname is None:
                            continue
                        it = self.idb.search_aegisname(iname)
                        if it is None:
                            self.warn('Unknown Item %s.' % iname)
                            continue
                    elif not eex:
                        it = self.idb.data.get(e['nameid'])
                    if it is None:
                        self.warn('Missing Item definition for Index %d.' % index)
                        continue
                    e['nameid'] = it['nameid']
                    if exists(li, 'Rate'):
                        v = self.as_uint16(li, 'Rate')
                        if v is None:
                            continue
                        e['rate'] = v
                    elif not eex:
                        e['rate'] = 0
                    if rnd['algorithm'] == GROUP_ALGORITHM_ALL and e['rate'] > 0:
                        self.warn('Item cannot have a rate with "All" algorithm. Defaulting Rate to 0.')
                        e['rate'] = 0
                    if rnd['algorithm'] != GROUP_ALGORITHM_ALL and e['rate'] == 0:
                        self.warn('Missing rate, item skipped.')
                        continue
                    adj = (e['rate'] * self.conf['item_group_rate']) // 100 & 0xffff
                    e['adj_rate'] = cap_value(adj, self.conf['item_group_drop_min'], self.conf['item_group_drop_max']) & 0xffff
                    if exists(li, 'Amount'):
                        v = self.as_uint16(li, 'Amount')
                        if v is None:
                            continue
                        e['amount'] = cap_value(v, 1, MAX_AMOUNT)
                    elif not eex:
                        e['amount'] = 1
                    if exists(li, 'Duration'):
                        v = self.as_uint16(li, 'Duration')
                        if v is None:
                            continue
                        e['duration'] = v
                    elif not eex:
                        e['duration'] = 0
                    for key, field, default in (('Announced', 'is_announced', False), ('UniqueId', 'guid', None),
                                                ('Stacked', 'is_stacked', True), ('Named', 'is_named', False)):
                        if exists(li, key):
                            a = self.as_bool(li, key)
                            if a is None:
                                break
                            e[field] = a
                        elif not eex:
                            e[field] = it['flag']['guid'] if default is None else default
                    else:
                        a = True
                    if a is None:
                        continue
                    if exists(li, 'Bound'):
                        b = self.as_string(li, 'Bound')
                        if b is None:
                            continue
                        bc = self.ld.const('BOUND_' + b)
                        if bc is None or bc < BOUND_NONE or bc >= BOUND_MAX:
                            self.warn('Invalid Bound %s.' % b)
                            continue
                        e['bound'] = bc
                    elif not eex:
                        e['bound'] = BOUND_NONE
                    if exists(li, 'RandomOptionGroup'):
                        n = self.as_string(li, 'RandomOptionGroup')
                        if n is None:
                            return 0
                        rid = self.rgroup.option_get_id(n)
                        if rid is None:
                            self.warn('Unknown random option group "%s".' % n)
                            return 0
                        e['random_option_group'] = rid
                    elif not eex:
                        e['random_option_group'] = None
                    for key, field in (('RefineMinimum', 'refine_minimum'), ('RefineMaximum', 'refine_maximum')):
                        if exists(li, key):
                            v = self.as_uint16(li, key)
                            if v is None:
                                return 0
                            if v > MAX_REFINE:
                                self.warn('%s %d is too high, capping to MAX_REFINE...' % (key, v))
                                v = MAX_REFINE
                            e[field] = v
                        elif not ex:  # sic: the group's `exists`, not the entry's
                            e[field] = 0
                    for key, field in (('GradeMinimum', 'minimum_enchantgrade'), ('GradeMaximum', 'maximum_enchantgrade')):
                        if exists(li, key):
                            g = self.as_string(li, key)
                            if g is None:
                                return 0
                            gc = self.ld.const('ENCHANTGRADE_' + g)
                            if gc is None or gc < ENCHANTGRADE_NONE or gc > MAX_ENCHANTGRADE:
                                self.warn('Invalid enchantgrade %s, defaulting to None.' % g)
                                gc = ENCHANTGRADE_NONE
                            e[field] = gc
                        elif not ex:
                            e[field] = ENCHANTGRADE_NONE
        if not ex:
            self.data[gid] = group
        return 1

    def loading_finished(self):
        for gid, group in self.data.items():
            for sub in [k for k, r in group['random'].items() if len(r['data']) == 0]:
                group['random'].erase(sub)
        for gid, group in self.data.items():
            for sub, r in group['random'].items():
                r['total_rate'] = sum(e['rate'] for e in r['data'].values()) & 0xFFFFFFFF


# --------------------------------------------------------------------------------------- combos
class ComboDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'COMBO_DB', 1, 1

    def __init__(self, ld, idb):
        super().__init__(ld)
        self.data = UMap()
        self.idb = idb
        self.combo_num = 0

    def default_location(self):
        return 'db/item_combos.yml'

    def clear(self):
        self.data.clear()
        self.combo_num = 0

    def find_combo_id(self, items):
        for k, v in self.data.items():
            if v['nameid'] == items:
                return k
        return 0

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Combos']):
            return 0
        items_list = []
        combos = node.first['Combos']
        for cit in children(combos):
            if not self.nodes_exist(cit, ['Combo']):
                return 0
            cn = cit.first['Combo']
            if not isinstance(cn, list):
                self.warn('Combo should be a sequence.')
                return 0
            items = []
            for v in cn:
                name = v if isinstance(v, str) else ''
                it = self.idb.search_aegisname(name)
                if it is None:
                    self.warn('Invalid item %s, skipping.' % name)
                    return 0
                items.append(it['nameid'])
            if not items:
                self.warn('Empty combo, skipping.')
                return 0
            if len(items) < 2:
                self.warn('Not enough item to make a combo (need at least 2). Skipping.')
                return 0
            items.sort()
            items_list.append(items)
        if not items_list:
            self.warn('No combos defined, skipping.')
            return 0
        if exists(node, 'Clear'):
            clear = self.as_bool(node, 'Clear')
            if clear is None:
                return 0
            if clear:
                for items in items_list:
                    cid = self.find_combo_id(items)
                    if cid == 0:
                        self.warn('Unable to clear the combo.')
                        return 0
                    self.data.erase(cid)
                return 1
        count = 0
        for items in items_list:
            cid = self.find_combo_id(items)
            combo = self.data.get(cid)
            ex = combo is not None
            if not ex:
                self.combo_num = (self.combo_num + 1) & 0xffff
                combo = {'id': self.combo_num, 'nameid': list(items), 'script': None}
            if exists(node, 'Script'):
                s = self.as_string(node, 'Script')
                if s is None:
                    return 0
                combo['script'] = s
            elif not ex:
                combo['script'] = None
            if not ex:
                self.data[combo['id']] = combo
            count += 1
        return count

    def loading_finished(self):
        """Attaches each combo to its items (item_data::combos), in the combo table's iteration order."""
        for cid, combo in self.data.items():
            for nameid in combo['nameid']:
                it = self.idb.data.get(nameid)
                if it is not None:
                    it['combos'].append(cid)


# --------------------------------------------------------------------------------------- laphine / reform / enchant / packages
class LaphineSynthesisDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'LAPHINE_SYNTHESIS_DB', 1, 1

    def __init__(self, ld, idb, groups):
        super().__init__(ld)
        self.data, self.idb, self.groups = UMap(), idb, groups

    def default_location(self):
        return 'db/laphine_synthesis.yml'

    def item(self, node, key):
        name = self.as_string(node, key)
        if name is None:
            return None
        it = self.idb.search_aegisname(name)
        if it is None:
            self.warn('Unknown item "%s".' % name)
        return it

    def refine(self, node, key):
        v = self.as_uint16(node, key)
        if v is not None and v > MAX_REFINE:
            self.warn('%s %d is too high, capping to MAX_REFINE...' % (key, v))
            v = MAX_REFINE
        return v

    def parse_body_node(self, node):
        it = self.item(node, 'Item')
        if it is None:
            return 0
        e = self.data.get(it['nameid'])
        ex = e is not None
        if not ex:
            if not self.nodes_exist(node, ['RewardGroup', 'Requirements']):
                return 0
            e = {'item_id': it['nameid'], 'minimum_refine': 0, 'maximum_refine': 0, 'required_requirements': 0,
                 'requirements': UMap(), 'reward_group_id': 0}
        if exists(node, 'RewardGroup'):
            name = self.as_string(node, 'RewardGroup')
            if name is None:
                return 0
            c = self.ld.const('IG_' + name)
            if c is None:
                self.warn('Unknown reward group "%s".' % name)
                return 0
            if (c & 0xffff) not in self.groups.data:
                self.warn('Unknown reward group ID "%d".' % c)
                return 0
            e['reward_group_id'] = c & 0xffff
        elif not ex:
            e['reward_group_id'] = 0
        if exists(node, 'RequiredRequirementsCount'):
            v = self.as_uint16(node, 'RequiredRequirementsCount')
            if v is None:
                return 0
            e['required_requirements'] = v
        elif not ex:
            e['required_requirements'] = 1
        if exists(node, 'Requirements'):
            for rn in children(node.first['Requirements']):
                rit = self.item(rn, 'Item')
                if rit is None:
                    return 0
                r = e['requirements'].get(rit['nameid'])
                rex = r is not None
                if not rex:
                    r = {'item_id': rit['nameid'], 'amount': 0}
                if exists(rn, 'Amount'):
                    v = self.as_uint16(rn, 'Amount')
                    if v is None:
                        return 0
                    if v > MAX_AMOUNT:
                        self.warn('Amount %d is too high, capping to MAX_AMOUNT...' % v)
                        v = MAX_AMOUNT
                    r['amount'] = v
                elif not rex:
                    r['amount'] = 1
                if not rex:
                    e['requirements'][rit['nameid']] = r
        for key, field, default in (('MinimumRefine', 'minimum_refine', 0), ('MaximumRefine', 'maximum_refine', MAX_REFINE)):
            if exists(node, key):
                v = self.refine(node, key)
                if v is None:
                    return 0
                e[field] = v
            elif not ex:
                e[field] = default
        if not ex:
            self.data[e['item_id']] = e
        return 1


class LaphineUpgradeDatabase(LaphineSynthesisDatabase):
    TYPE, VERSION, MIN_VERSION = 'LAPHINE_UPGRADE_DB', 1, 1

    def __init__(self, ld, idb, rgroup):
        YamlDatabase.__init__(self, ld)
        self.data, self.idb, self.rgroup = UMap(), idb, rgroup

    def default_location(self):
        return 'db/laphine_upgrade.yml'

    def parse_body_node(self, node):
        it = self.item(node, 'Item')
        if it is None:
            return 0
        e = self.data.get(it['nameid'])
        ex = e is not None
        if not ex:
            if not self.nodes_exist(node, ['TargetItems']):
                return 0
            e = {'item_id': it['nameid'], 'target_item_ids': [], 'minimum_refine': 0, 'maximum_refine': 0,
                 'required_random_options': 0, 'cards_allowed': False, 'random_option_group': None,
                 'result_refine': 0, 'result_refine_minimum': 0, 'result_refine_maximum': 0}
        if exists(node, 'RandomOptionGroup'):
            name = self.as_string(node, 'RandomOptionGroup')
            if name is None:
                return 0
            rid = self.rgroup.option_get_id(name)
            if rid is None:
                self.warn('Unknown random option group "%s".' % name)
                return 0
            e['random_option_group'] = rid
        elif not ex:
            e['random_option_group'] = None
        if exists(node, 'TargetItems'):
            for tn in children(node.first['TargetItems']):
                tit = self.item(tn, 'Item')
                if tit is None:
                    return 0
                if tit['nameid'] not in e['target_item_ids']:
                    e['target_item_ids'].append(tit['nameid'])
        for key, field, default in (('MinimumRefine', 'minimum_refine', 0), ('MaximumRefine', 'maximum_refine', MAX_REFINE)):
            if exists(node, key):
                v = self.refine(node, key)
                if v is None:
                    return 0
                e[field] = v
            elif not ex:
                e[field] = default
        if exists(node, 'RequiredRandomOptions'):
            v = self.as_uint16(node, 'RequiredRandomOptions')
            if v is None:
                return 0
            if v > MAX_ITEM_RDM_OPT:
                self.warn('Required random option amount %d is too high, capping to MAX_ITEM_RDM_OPT...' % v)
                v = MAX_ITEM_RDM_OPT
            e['required_random_options'] = v
        elif not ex:
            e['required_random_options'] = 0
        if exists(node, 'CardsAllowed'):
            a = self.as_bool(node, 'CardsAllowed')
            if a is None:
                return 0
            e['cards_allowed'] = a
        elif not ex:
            e['cards_allowed'] = False
        for key, field in (('ResultRefine', 'result_refine'), ('ResultRefineMinimum', 'result_refine_minimum'),
                           ('ResultRefineMaximum', 'result_refine_maximum')):
            if exists(node, key):
                v = self.refine(node, key)
                if v is None:
                    return 0
                e[field] = v
            elif not ex:
                e[field] = 0
        if not ex:
            self.data[e['item_id']] = e
        return 1


class ItemReformDatabase(LaphineSynthesisDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_REFORM_DB', 1, 1

    def __init__(self, ld, idb, rgroup):
        YamlDatabase.__init__(self, ld)
        self.data, self.idb, self.rgroup = UMap(), idb, rgroup

    def default_location(self):
        return 'db/item_reform.yml'

    def parse_body_node(self, node):
        it = self.item(node, 'Item')
        if it is None:
            return 0
        e = self.data.get(it['nameid'])
        ex = e is not None
        if not ex:
            if not self.nodes_exist(node, ['BaseItems']):
                return 0
            e = {'item_id': it['nameid'], 'base_items': UMap()}
        if exists(node, 'BaseItems'):
            for bn in children(node.first['BaseItems']):
                bit = self.item(bn, 'BaseItem')
                if bit is None:
                    return 0
                base = e['base_items'].get(bit['nameid'])
                bex = base is not None
                if not bex:
                    if not self.nodes_exist(bn, ['ResultItem']):
                        return 0
                    base = {'item_id': bit['nameid'], 'minimum_refine': 0, 'maximum_refine': 0,
                            'required_random_options': 0, 'cards_allowed': False, 'materials': UMap(),
                            'result_item_id': 0, 'refine_change': 0, 'random_option_group': None,
                            'clear_slots': False, 'remove_enchantgrade': False}
                for key, field, default in (('MinimumRefine', 'minimum_refine', 0), ('MaximumRefine', 'maximum_refine', MAX_REFINE)):
                    if exists(bn, key):
                        v = self.refine(bn, key)
                        if v is None:
                            return 0
                        base[field] = v
                    elif not bex:
                        base[field] = default
                if exists(bn, 'RequiredRandomOptions'):
                    v = self.as_uint16(bn, 'RequiredRandomOptions')
                    if v is None:
                        return 0
                    if v > MAX_ITEM_RDM_OPT:
                        self.warn('Required random option amount %d is too high, capping to MAX_ITEM_RDM_OPT...' % v)
                        v = MAX_ITEM_RDM_OPT
                    base['required_random_options'] = v
                elif not bex:
                    base['required_random_options'] = 0
                if exists(bn, 'CardsAllowed'):
                    a = self.as_bool(bn, 'CardsAllowed')
                    if a is None:
                        return 0
                    base['cards_allowed'] = a
                elif not bex:
                    base['cards_allowed'] = True
                if exists(bn, 'Materials'):
                    for mn in children(bn.first['Materials']):
                        mit = self.item(mn, 'Material')
                        if mit is None:
                            return 0
                        mid = mit['nameid']
                        mex = mid in base['materials']
                        if exists(mn, 'Amount'):
                            amount = self.as_uint16(mn, 'Amount')
                            if amount is None:
                                return 0
                            if amount > MAX_AMOUNT:
                                self.warn('Amount %d is too high, capping to MAX_AMOUNT...' % amount)
                                amount = MAX_AMOUNT
                        elif not mex:
                            amount = 1
                        else:
                            # rAthena leaves `amount` uninitialised here (undefined); keep the current amount
                            amount = base['materials'][mid]
                        if amount > 0:
                            base['materials'][mid] = amount
                        else:
                            base['materials'].erase(mid)
                if exists(bn, 'ResultItem'):
                    rit = self.item(bn, 'ResultItem')
                    if rit is None:
                        return 0
                    base['result_item_id'] = rit['nameid']
                if exists(bn, 'ChangeRefine'):
                    v = self.as_int16(bn, 'ChangeRefine')
                    if v is None:
                        return 0
                    if v > MAX_REFINE:
                        self.warn('Refine change %d is too high, capping to MAX_REFINE...' % v)
                        v = MAX_REFINE
                    elif v < -MAX_REFINE:
                        self.warn('Refine change %d is too low, capping to -MAX_REFINE...' % v)
                        v = -MAX_REFINE
                    base['refine_change'] = v
                elif not bex:
                    base['refine_change'] = 0
                if exists(bn, 'RandomOptionGroup'):
                    name = self.as_string(bn, 'RandomOptionGroup')
                    if name is None:
                        return 0
                    rid = self.rgroup.option_get_id(name)
                    if rid is None:
                        self.warn('Unknown random option group "%s".' % name)
                        return 0
                    base['random_option_group'] = rid
                elif not bex:
                    base['random_option_group'] = None
                for key, field in (('ClearSlots', 'clear_slots'), ('RemoveEnchantgrade', 'remove_enchantgrade')):
                    if exists(bn, key):
                        a = self.as_bool(bn, key)
                        if a is None:
                            return 0
                        base[field] = a
                    elif not bex:
                        base[field] = False
                if not bex:
                    e['base_items'][bit['nameid']] = base
        if not ex:
            self.data[e['item_id']] = e
        return 1


class ItemEnchantDatabase(LaphineSynthesisDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_ENCHANT_DB', 1, 1

    def __init__(self, ld, idb):
        YamlDatabase.__init__(self, ld)
        self.data, self.idb = UMap(), idb

    def default_location(self):
        return 'db/item_enchant.yml'

    def parse_materials(self, node, materials):
        """ItemEnchantDatabase::parseMaterials."""
        if exists(node, 'Materials'):
            for mn in children(node.first['Materials']):
                mit = self.item(mn, 'Material')
                if mit is None:
                    return False
                mid = mit['nameid']
                mex = mid in materials
                if exists(mn, 'Amount'):
                    amount = self.as_uint16(mn, 'Amount')
                    if amount is None:
                        return False
                    if amount > MAX_AMOUNT:
                        self.warn('Amount %d is too high, capping to MAX_AMOUNT...' % amount)
                        amount = MAX_AMOUNT
                elif not mex:
                    amount = 1
                else:
                    amount = materials[mid]
                if amount > 0:
                    materials[mid] = amount
                else:
                    materials.erase(mid)
        return True

    def zeny(self, node, key='Price'):
        v = self.as_uint32(node, key)
        if v is not None and v > MAX_ZENY:
            self.warn('Price %d exceeds MAX_ZENY. Capping...' % v)
            v = MAX_ZENY
        return v

    def parse_body_node(self, node):
        eid = self.as_uint64(node, 'Id')
        if eid is None:
            return 0
        en = self.data.get(eid)
        ex = en is not None
        if not ex:
            if not self.nodes_exist(node, ['TargetItems', 'Order', 'Slots']):
                return 0
            en = {'id': eid, 'target_item_ids': [], 'minimum_refine': 0, 'minimum_enchantgrade': 0,
                  'allow_random_options': False, 'reset': {'zeny': 0, 'materials': UMap(), 'chance': 0},
                  'order': [], 'slots': UMap()}
        if exists(node, 'TargetItems'):
            tn = node.first['TargetItems']
            for k, _ in (tn.pairs if isinstance(tn, YMap) else []):
                it = self.idb.search_aegisname(k)
                if it is None:
                    self.warn('Unknown item "%s".' % k)
                    return 0
                a = self.as_bool(tn, k)
                if a is None:
                    return 0
                if a:
                    if it['nameid'] in en['target_item_ids']:
                        self.warn('Target item "%s" is already in the list.' % k)
                    else:
                        en['target_item_ids'].append(it['nameid'])
                else:
                    if it['nameid'] not in en['target_item_ids']:
                        self.warn('Target item "%s" is not in the list.' % k)
                    else:
                        en['target_item_ids'].remove(it['nameid'])
        if exists(node, 'MinimumRefine'):
            v = self.as_uint16(node, 'MinimumRefine')
            if v is None:
                return 0
            if v > MAX_REFINE:
                self.warn('Minimum refine %d exceeds MAX_REFINE. Capping...' % v)
                v = MAX_REFINE
            en['minimum_refine'] = v
        elif not ex:
            en['minimum_refine'] = 0
        if exists(node, 'MinimumEnchantgrade'):
            v = self.as_uint16(node, 'MinimumEnchantgrade')
            if v is None:
                return 0
            if v > MAX_ENCHANTGRADE:
                self.warn('Minimum enchantgrade %d exceeds MAX_ENCHANTGRADE. Capping...' % v)
                v = MAX_ENCHANTGRADE
            en['minimum_enchantgrade'] = v
        elif not ex:
            en['minimum_enchantgrade'] = 0
        if exists(node, 'AllowRandomOptions'):
            a = self.as_bool(node, 'AllowRandomOptions')
            if a is None:
                return 0
            en['allow_random_options'] = a
        elif not ex:
            en['allow_random_options'] = True
        if exists(node, 'Reset'):
            rn = node.first['Reset']
            if exists(rn, 'Chance'):
                v = self.as_uint32_rate(rn, 'Chance', 100000)
                if v is None:
                    return 0
                en['reset']['chance'] = v
            elif not ex:
                en['reset']['chance'] = 0
            if exists(rn, 'Price'):
                v = self.zeny(rn)
                if v is None:
                    return 0
                en['reset']['zeny'] = v
            elif not ex:
                en['reset']['zeny'] = 0
            if not self.parse_materials(rn, en['reset']['materials']):
                return 0
        elif not ex:
            en['reset'] = {'zeny': 0, 'materials': UMap(), 'chance': 0}
        if exists(node, 'Order'):
            en['order'] = []
            for on in children(node.first['Order']):
                slot = self.as_uint16(on, 'Slot')
                if slot is None:
                    return 0
                if slot >= MAX_SLOTS:
                    self.warn('Slot %d exceeds MAX_SLOTS...' % slot)
                    return 0
                en['order'].append(slot)
        if exists(node, 'Slots'):
            for sn in children(node.first['Slots']):
                slot = self.as_uint16(sn, 'Slot')
                if slot is None:
                    return 0
                if slot >= MAX_SLOTS:
                    self.warn('Slot %d exceeds MAX_SLOTS...' % slot)
                    return 0
                es = en['slots'].get(slot)
                sex_ = es is not None
                if not sex_:
                    inc = UMap()
                    for g in range(MAX_ENCHANTGRADE + 1):
                        inc[g] = 0
                    es = {'slot': slot,
                          'normal': {'zeny': 0, 'materials': UMap(), 'chance': 0, 'enchantgrade_chance_increase': inc,
                                     'enchants': UMap()},
                          'perfect': {'enchants': UMap()}, 'upgrade': {'enchants': UMap()}}
                if exists(sn, 'Price'):
                    v = self.zeny(sn)
                    if v is None:
                        return 0
                    es['normal']['zeny'] = v
                elif not sex_:
                    es['normal']['zeny'] = 0
                if not self.parse_materials(sn, es['normal']['materials']):
                    return 0
                if exists(sn, 'Chance'):
                    v = self.as_uint32_rate(sn, 'Chance', 100000)
                    if v is None:
                        return 0
                    es['normal']['chance'] = v
                elif not sex_:
                    es['normal']['chance'] = 100000
                if exists(sn, 'EnchantgradeBonus'):
                    for gn in children(sn.first['EnchantgradeBonus']):
                        g = self.as_uint16(gn, 'Enchantgrade')
                        if g is None:
                            return 0
                        if g > MAX_ENCHANTGRADE:
                            self.warn('Enchant grade %d exceeds MAX_ENCHANTGRADE.' % g)
                            return 0
                        # sic: rAthena reads the slot's Chance here, not the bonus entry's
                        v = self.as_uint32_rate(sn, 'Chance', 100000)
                        if v is None:
                            return 0
                        es['normal']['enchantgrade_chance_increase'][g] = v
                if exists(sn, 'Enchants'):
                    for gn in children(sn.first['Enchants']):
                        g = self.as_uint16(gn, 'Enchantgrade')
                        if g is None:
                            return 0
                        if g > MAX_ENCHANTGRADE:
                            self.warn('Enchant grade %d exceeds MAX_ENCHANTGRADE...' % g)
                            return 0
                        eg = es['normal']['enchants'].get(g)
                        egex = eg is not None
                        if not egex:
                            eg = {'enchantgrade': g, 'enchants': UMap()}
                        if exists(gn, 'Items'):
                            for itn in children(gn.first['Items']):
                                it = self.item(itn, 'Item')
                                if it is None:
                                    return 0
                                sub = eg['enchants'].get(it['nameid'])
                                subex = sub is not None
                                if not subex:
                                    if not self.nodes_exist(itn, ['Chance']):
                                        return 0
                                    sub = {'item_id': it['nameid'], 'chance': 0}
                                if exists(itn, 'Chance'):
                                    v = self.as_uint32_rate(itn, 'Chance', 100000)
                                    if v is None:
                                        return 0
                                    sub['chance'] = v
                                if not subex:
                                    eg['enchants'][sub['item_id']] = sub
                        if not egex:
                            es['normal']['enchants'][g] = eg
                if exists(sn, 'PerfectEnchants'):
                    for pn in children(sn.first['PerfectEnchants']):
                        it = self.item(pn, 'Item')
                        if it is None:
                            return 0
                        p = es['perfect']['enchants'].get(it['nameid'])
                        pex = p is not None
                        if not pex:
                            p = {'item_id': it['nameid'], 'zeny': 0, 'materials': UMap()}
                        if exists(pn, 'Price'):
                            v = self.zeny(pn)
                            if v is None:
                                return 0
                            p['zeny'] = v
                        elif not sex_:
                            p['zeny'] = 0
                        if not self.parse_materials(pn, p['materials']):
                            return 0
                        if not pex:
                            es['perfect']['enchants'][p['item_id']] = p
                if exists(sn, 'Upgrades'):
                    for un in children(sn.first['Upgrades']):
                        it = self.item(un, 'Enchant')
                        if it is None:
                            return 0
                        u = es['upgrade']['enchants'].get(it['nameid'])
                        uex = u is not None
                        if not uex:
                            if not self.nodes_exist(un, ['Upgrade']):
                                return 0
                            u = {'enchant_item_id': it['nameid'], 'upgrade_item_id': 0, 'zeny': 0, 'materials': UMap()}
                        if exists(un, 'Upgrade'):
                            uit = self.item(un, 'Upgrade')
                            if uit is None:
                                return 0
                            u['upgrade_item_id'] = uit['nameid']
                        if exists(un, 'Price'):
                            v = self.zeny(un)
                            if v is None:
                                return 0
                            u['zeny'] = v
                        elif not uex:
                            u['zeny'] = 0
                        if not self.parse_materials(un, u['materials']):
                            return 0
                        if not uex:
                            es['upgrade']['enchants'][u['enchant_item_id']] = u
                if not sex_:
                    en['slots'][slot] = es
        if not ex:
            self.data[eid] = en
        return 1


class ItemPackageDatabase(LaphineSynthesisDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_PACKAGE_DB', 2, 1

    def __init__(self, ld, idb, rgroup):
        YamlDatabase.__init__(self, ld)
        self.data, self.idb, self.rgroup = UMap(), idb, rgroup

    def default_location(self):
        return 'db/item_packages.yml'

    def parse_body_node(self, node):
        it = self.item(node, 'Item')
        if it is None:
            return 0
        e = self.data.get(it['nameid'])
        ex = e is not None
        if not ex:
            if not self.nodes_exist(node, ['Groups']):
                return 0
            e = {'item_id': it['nameid'], 'groups': UMap()}
        if exists(node, 'Groups'):
            for gn in children(node.first['Groups']):
                gi = self.as_uint32(gn, 'Group')
                if gi is None:
                    return 0
                g = e['groups'].get(gi)
                gex = g is not None
                if not gex:
                    if not self.nodes_exist(gn, ['Items']):
                        return 0
                    g = {'group_index': gi, 'items': UMap()}
                for itn in children(gn.first.get('Items') if isinstance(gn, YMap) else None):
                    pit = self.item(itn, 'Item')
                    if pit is None:
                        return 0
                    name = itn.first['Item']
                    p = g['items'].get(pit['nameid'])
                    pex = p is not None
                    if not pex:
                        p = {'item_id': pit['nameid'], 'amount': 0, 'rentalhours': 0, 'refine': 0, 'grade': 0,
                             'random_option_group': None}
                    if exists(itn, 'Amount'):
                        v = self.as_uint16(itn, 'Amount')
                        if v is None:
                            return 0
                        if v > MAX_AMOUNT:
                            self.warn('Amount %d is too high, capping to MAX_AMOUNT...' % v)
                            v = MAX_AMOUNT
                        elif v == 0:
                            if not pex:
                                self.warn('Trying to remove non existant item "%s".' % name)
                                return 0
                            g['items'].erase(pit['nameid'])
                        p['amount'] = v
                    elif not pex:
                        p['amount'] = 1
                    if exists(itn, 'RentalHours'):
                        v = self.as_uint16(itn, 'RentalHours')
                        if v is None:
                            return 0
                        p['rentalhours'] = v
                    elif not pex:
                        p['rentalhours'] = 0
                    if exists(itn, 'Refine'):
                        v = self.refine(itn, 'Refine')
                        if v is None:
                            return 0
                        p['refine'] = v
                    elif not pex:
                        p['refine'] = 0
                    if exists(itn, 'RandomOptionGroup'):
                        n = self.as_string(itn, 'RandomOptionGroup')
                        if n is None:
                            return 0
                        rid = self.rgroup.option_get_id(n)
                        if rid is None:
                            self.warn('Unknown random option group "%s".' % n)
                            return 0
                        p['random_option_group'] = rid
                    elif not pex:
                        p['random_option_group'] = None
                    if exists(itn, 'Grade'):
                        gs = self.as_string(itn, 'Grade')
                        if gs is None:
                            return 0
                        gc = self.ld.const('ENCHANTGRADE_' + gs)
                        if gc is None or gc < ENCHANTGRADE_NONE or gc > MAX_ENCHANTGRADE:
                            self.warn('Invalid enchantgrade %s, defaulting to None.' % gs)
                            gc = ENCHANTGRADE_NONE
                        p['grade'] = gc
                    elif not pex:
                        p['grade'] = ENCHANTGRADE_NONE
                    if not pex:
                        g['items'][p['item_id']] = p
                if not gex:
                    e['groups'][g['group_index']] = g
        if not ex:
            self.data[e['item_id']] = e
        return 1


# --------------------------------------------------------------------------------------- cash shop
class CashShopDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'ITEM_CASH_DB', 1, 1

    def __init__(self, ld, idb):
        super().__init__(ld)
        self.data, self.idb = UMap(), idb

    def default_location(self):
        return 'db/item_cash.yml'

    def parse_body_node(self, node):
        name = self.as_string(node, 'Tab')
        if name is None:
            return 0
        c = self.ld.const('CASHSHOP_TAB_' + name)
        if c is None:
            self.warn('Invalid tab CASHSHOP_TAB_%s, skipping.' % name)
            return 0
        if c < CASHSHOP_TAB_NEW or c >= CASHSHOP_TAB_MAX:
            self.warn('Tab %d is out of range, skipping.' % c)
            return 0
        e = self.data.get(c)
        ex = e is not None
        if not ex:
            if not self.nodes_exist(node, ['Items']):
                return 0
            e = {'tab': c, 'items': []}
        for itn in children(node.first.get('Items')):
            iname = self.as_string(itn, 'Item')
            if iname is None:
                return 0
            it = self.idb.search_aegisname(iname)
            if it is None:
                self.warn('Cash item %s does not exist, skipping.' % iname)
                continue
            ci = next((x for x in e['items'] if x['nameid'] == it['nameid']), None)
            cex = ci is not None
            if not cex:
                ci = {'nameid': it['nameid'], 'price': 0}
            price = self.as_uint32(itn, 'Price')
            if price is None:
                return 0
            if price == 0:
                self.warn('Price has to be greater than zero.')
                return 0
            if price > MAX_CASHPOINT:
                self.warn('Price has to be lower than MAX_CASHPOINT(%d).' % MAX_CASHPOINT)
                return 0
            ci['price'] = price
            if not cex:
                e['items'].append(ci)
        if not ex:
            self.data[c] = e
        return 1


# --------------------------------------------------------------------------------------- refine / enchantgrade (status.cpp)
class RefineDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'REFINE_DB', 2, 1

    def __init__(self, ld, idb):
        super().__init__(ld)
        self.data, self.idb = UMap(), idb

    def default_location(self):
        return 'db/refine.yml'

    def parse_body_node(self, node):
        gname = self.as_string(node, 'Group')
        if gname is None:
            return 0
        c = self.ld.const('REFINE_TYPE_' + gname)
        if c is None:
            self.warn('Unknown refine group %s, skipping.' % gname)
            return 0
        gid = c & 0xffff
        info = self.data.get(gid)
        ex = info is not None
        if not ex:
            info = {'group_id': gid, 'levels': UMap()}
        if exists(node, 'Levels'):
            for ln in children(node.first['Levels']):
                level = self.as_uint16(ln, 'Level')
                if level is None:
                    return 0
                li = info['levels'].get(level)
                lex = li is not None
                if not lex:
                    li = {'level': level, 'levels': UMap()}
                if exists(ln, 'RefineLevels'):
                    for rn in children(ln.first['RefineLevels']):
                        rl = self.as_uint16(rn, 'Level')
                        if rl is None:
                            return 0
                        if rl == 0 or rl > MAX_REFINE:
                            self.warn('Refine level %d is invalid, skipping.' % rl)
                            return 0
                        rl -= 1  # the database is 1 based, the code 0 based
                        r = li['levels'].get(rl)
                        rex = r is not None
                        if not rex:
                            r = {'level': rl, 'bonus': 0, 'randombonus_max': 0, 'blessing_amount': 0,
                                 'broadcast_success': False, 'broadcast_failure': False, 'costs': UMap()}
                        for key, field in (('Bonus', 'bonus'), ('RandomBonus', 'randombonus_max')):
                            if exists(rn, key):
                                v = self.as_uint32(rn, key)
                                if v is None:
                                    return 0
                                r[field] = v
                            elif not rex:
                                r[field] = 0
                        if exists(rn, 'BlacksmithBlessingAmount'):
                            v = self.as_uint16(rn, 'BlacksmithBlessingAmount')
                            if v is None:
                                return 0
                            if v > MAX_AMOUNT:
                                self.warn('Blacksmith Blessing amount %d too high, capping to MAX_AMOUNT.' % v)
                                v = MAX_AMOUNT
                            r['blessing_amount'] = v
                        elif not rex:
                            r['blessing_amount'] = 0
                        for key, field in (('BroadcastSuccess', 'broadcast_success'), ('BroadcastFailure', 'broadcast_failure')):
                            if exists(rn, key):
                                a = self.as_bool(rn, key)
                                if a is None:
                                    return 0
                                r[field] = a
                            elif not rex:
                                r[field] = False
                        if exists(rn, 'Chances'):
                            for cn in children(rn.first['Chances']):
                                cname = self.as_string(cn, 'Type')
                                if cname is None:
                                    return 0
                                cc = self.ld.const('REFINE_COST_' + cname)
                                if cc is None:
                                    self.warn('Unknown refine cost type %s, skipping.' % cname)
                                    return 0
                                if cc >= REFINE_COST_MAX:
                                    self.warn('Refine cost type %s is unsupported, skipping.' % cname)
                                    return 0
                                idx = cc & 0xffff
                                cost = r['costs'].get(idx)
                                cex = cost is not None
                                if not cex:
                                    cost = {'index': idx, 'nameid': 0, 'chance': 0, 'zeny': 0, 'breaking_rate': 0,
                                            'downgrade_amount': 0}
                                if exists(cn, 'Rate'):
                                    v = self.as_uint16_rate(cn, 'Rate')
                                    if v is None:
                                        return 0
                                    cost['chance'] = v
                                elif not cex:
                                    cost['chance'] = 0
                                if exists(cn, 'Price'):
                                    v = self.as_uint32(cn, 'Price')
                                    if v is None:
                                        return 0
                                    if v > MAX_ZENY:
                                        self.warn('Price is above MAX_ZENY, capping...')
                                        v = MAX_ZENY
                                    cost['zeny'] = v
                                elif not cex:
                                    cost['zeny'] = 0
                                if exists(cn, 'Material'):
                                    mname = self.as_string(cn, 'Material')
                                    if mname is None:
                                        return 0
                                    it = self.idb.search_aegisname(mname)
                                    if it is None:
                                        self.warn('Unknown refine material %s, skipping.' % mname)
                                        return 0
                                    cost['nameid'] = it['nameid']
                                elif not cex:
                                    cost['nameid'] = 0
                                if exists(cn, 'BreakingRate'):
                                    v = self.as_uint16_rate(cn, 'BreakingRate')
                                    if v is None:
                                        return 0
                                    cost['breaking_rate'] = v
                                elif not cex:
                                    cost['breaking_rate'] = 0
                                if exists(cn, 'DowngradeAmount'):
                                    v = self.as_uint16(cn, 'DowngradeAmount')
                                    if v is None:
                                        return 0
                                    if v > MAX_REFINE:
                                        self.warn('Downgrade amount %d is invalid, skipping.' % v)
                                        return 0
                                    cost['downgrade_amount'] = v
                                elif not cex:
                                    cost['downgrade_amount'] = 0
                                if not cex:
                                    r['costs'][idx] = cost
                        if not rex:
                            li['levels'][rl] = r
                if not lex:
                    info['levels'][level] = li
        if not ex:
            self.data[gid] = info
        return 1


class EnchantgradeDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'ENCHANTGRADE_DB', 3, 3

    def __init__(self, ld, idb):
        super().__init__(ld)
        self.data, self.idb = UMap(), idb

    def default_location(self):
        return 'db/enchantgrade.yml'

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Type', 'Levels']):
            return 0
        tname = self.as_string(node, 'Type')
        if tname is None:
            return 0
        c = self.ld.const('IT_' + tname)
        if c is None:
            self.warn('Unknown item type "%s".' % tname)
            return 0
        itemtype = c & 0xffff
        if itemtype == IT_WEAPON:
            maxlevel = MAX_WEAPON_LEVEL
        elif itemtype == IT_ARMOR:
            maxlevel = MAX_ARMOR_LEVEL
        else:
            self.warn('Item type "%s" is not supported.' % tname)
            return 0
        eg = self.data.get(itemtype)
        ex = eg is not None
        if not ex:
            eg = {'itemtype': itemtype, 'levels': {}}
        for ln in children(node.first['Levels']):
            if not self.nodes_exist(ln, ['Level', 'Grades']):
                return 0
            level = self.as_uint16(ln, 'Level')
            if level is None:
                return 0
            if level == 0 or level > maxlevel:
                self.warn('Level %d is invalid for item type %s[1~%d].' % (level, tname, maxlevel))
                return 0
            grades = eg['levels'].setdefault(level, {})  # std::map operator[]
            for gn in children(ln.first['Grades']):
                gname = self.as_string(gn, 'Grade')
                if gname is None:
                    return 0
                gc = self.ld.const('ENCHANTGRADE_' + gname)
                if gc is None:
                    self.warn('Unknown grade "%s".' % gname)
                    return 0
                if gc >= MAX_ENCHANTGRADE:
                    self.warn('Grade %d is too high. Maximum: %d.' % (gc, MAX_ENCHANTGRADE - 1))
                    return 0
                g = grades.get(gc)
                gex = g is not None
                if not gex:
                    if not self.nodes_exist(gn, ['Chances', 'Options']):
                        return 0
                    g = {'grade': gc, 'chances': [0] * (MAX_REFINE + 1), 'bonus': 0, 'announce_success': False,
                         'announce_fail': False,
                         'catalyst': {'item': 0, 'amount_per_step': 0, 'maximum_steps': 0, 'chance_increase': 0},
                         'options': {}}
                if exists(gn, 'Chances'):
                    for cn in children(gn.first['Chances']):
                        refine = self.as_uint16(cn, 'Refine')
                        if refine is None:
                            return 0
                        if refine > MAX_REFINE:
                            self.warn('Refine %d is too high. Maximum: %d.' % (refine, MAX_REFINE))
                            return 0
                        ch = self.as_uint16_rate(cn, 'Chance')
                        if ch is None:
                            return 0
                        g['chances'][refine] = ch
                if exists(gn, 'Bonus'):
                    v = self.as_uint16(gn, 'Bonus')
                    if v is None:
                        return 0
                    g['bonus'] = v
                elif not gex:
                    g['bonus'] = 0
                for key, field, default in (('AnnounceSuccess', 'announce_success', True), ('AnnounceFail', 'announce_fail', False)):
                    if exists(gn, key):
                        a = self.as_bool(gn, key)
                        if a is None:
                            return 0
                        g[field] = a
                    elif not gex:
                        g[field] = default
                if exists(gn, 'Announce'):
                    a = self.as_bool(gn, 'Announce')
                    if a is None:
                        return 0
                    g['announce_success'] = g['announce_fail'] = a
                cat = g['catalyst']
                if exists(gn, 'Catalyst'):
                    kn = gn.first['Catalyst']
                    if exists(kn, 'Item'):
                        iname = self.as_string(kn, 'Item')
                        if iname is None:
                            return 0
                        it = self.idb.search_aegisname(iname)
                        if it is None:
                            self.warn('Unknown item "%s".' % iname)
                            return 0
                        cat['item'] = it['nameid']
                    elif not gex:
                        cat['item'] = 0
                    for key, field in (('AmountPerStep', 'amount_per_step'), ('MaximumSteps', 'maximum_steps')):
                        if exists(kn, key):
                            v = self.as_uint16(kn, key)
                            if v is None:
                                return 0
                            cat[field] = v
                        elif not gex:
                            cat[field] = 0
                    if exists(kn, 'ChanceIncrease'):
                        v = self.as_uint16_rate(kn, 'ChanceIncrease')
                        if v is None:
                            return 0
                        cat['chance_increase'] = v
                    elif not gex:
                        cat['chance_increase'] = 0
                elif not gex:
                    g['catalyst'] = {'item': 0, 'amount_per_step': 0, 'maximum_steps': 0, 'chance_increase': 0}
                if exists(gn, 'Options'):
                    for on in children(gn.first['Options']):
                        oi = self.as_uint16(on, 'Option')
                        if oi is None:
                            return 0
                        o = g['options'].get(oi)
                        oex = o is not None
                        if not oex:
                            o = {'id': oi, 'item': 0, 'amount': 0, 'zeny': 0, 'breaking_rate': 0, 'downgrade_amount': 0}
                        if exists(on, 'Amount'):
                            v = self.as_uint16(on, 'Amount')
                            if v is None:
                                return 0
                            if v > MAX_AMOUNT:
                                self.warn('Amount %d is too high, capping to %d...' % (v, MAX_AMOUNT))
                                v = MAX_AMOUNT
                            if v == 0:
                                if g['options'].pop(oi, None) is not None:
                                    continue
                                self.warn('Trying to remove invalid option %d...' % oi)
                                return 0
                            o['amount'] = v
                        elif not oex:
                            o['amount'] = 1
                        if exists(on, 'Item'):
                            iname = self.as_string(on, 'Item')
                            if iname is None:
                                return 0
                            it = self.idb.search_aegisname(iname)
                            if it is None:
                                self.warn('Unknown item "%s".' % iname)
                                return 0
                            o['item'] = it['nameid']
                        elif not oex:
                            o['item'] = 0
                        if exists(on, 'Zeny'):
                            v = self.as_uint32(on, 'Zeny')
                            if v is None:
                                return 0
                            o['zeny'] = v
                        elif not oex:
                            o['zeny'] = 0
                        if exists(on, 'BreakingRate'):
                            v = self.as_uint16_rate(on, 'BreakingRate')
                            if v is None:
                                return 0
                            o['breaking_rate'] = v
                        elif not oex:
                            o['breaking_rate'] = 0
                        if exists(on, 'DowngradeAmount'):
                            v = self.as_uint16(on, 'DowngradeAmount')
                            if v is None:
                                return 0
                            if v > MAX_REFINE:
                                self.warn('Downgrade amount %d is invalid, skipping.' % v)
                                return 0
                            o['downgrade_amount'] = v
                        elif not oex:
                            o['downgrade_amount'] = 0
                        if not oex:
                            g['options'][oi] = o
                if not gex:
                    grades[gc] = g
        if not ex:
            self.data[itemtype] = eg
        return 1

    def loading_finished(self):
        for _, eg in self.data.items():
            for _, grades in eg['levels'].items():
                for _, g in grades.items():
                    if g['catalyst']['amount_per_step'] == 0:
                        g['catalyst'].update(item=0, chance_increase=0, maximum_steps=0)


# --------------------------------------------------------------------------------------- stylist / barters (npc.cpp)
class StylistDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'STYLIST_DB', 1, 1

    def __init__(self, ld, idb, conf):
        super().__init__(ld)
        self.data, self.idb, self.conf = UMap(), idb, conf

    def default_location(self):
        return 'db/stylist.yml'

    def parse_cost_node(self, entry, doram, node):
        key = 'doram' if doram else 'human'
        costs = entry[key]
        cex = costs is not None
        if not cex:
            costs = {'price': 0, 'required_item': 0, 'required_item_box': 0}
        if exists(node, 'Price'):
            v = self.as_uint32(node, 'Price')
            if v is None:
                return False
            if v > MAX_ZENY:
                self.warn('stylist_parseCostNode: Price %d is too high, capping to MAX_ZENY...' % v)
                v = MAX_ZENY
            costs['price'] = v
        elif not cex:
            costs['price'] = 0
        for k, f in (('RequiredItem', 'required_item'), ('RequiredItemBox', 'required_item_box')):
            if exists(node, k):
                n = self.as_string(node, k)
                if n is None:
                    return False
                it = self.idb.search_aegisname(n)
                if it is None:
                    self.warn('stylist_parseCostNode: Unknown item "%s"...' % n)
                    return False
                costs[f] = it['nameid']
            elif not cex:
                costs[f] = 0
        if not cex:
            entry[key] = costs
        return True

    def parse_body_node(self, node):
        if not self.nodes_exist(node, ['Look', 'Options']):
            return 0
        ls = self.as_string(node, 'Look')
        if ls is None:
            return 0
        c = self.ld.const('LOOK_' + ls)
        if c is None:
            self.warn('stylist_parseBodyNode: Invalid look %s.' % ls)
            return 0
        if c not in (LOOK_HEAD_TOP, LOOK_HEAD_MID, LOOK_HEAD_BOTTOM, LOOK_HAIR, LOOK_HAIR_COLOR, LOOK_CLOTHES_COLOR, LOOK_BODY2):
            self.warn('stylist_parseBodyNode: Unsupported look value "%s"...' % ls)
            return 0
        lst = self.data.get(c)
        ex = lst is not None
        count = 0
        if not ex:
            lst = {'look': c, 'entries': UMap()}
        for on in children(node.first['Options']):
            index = self.as_int16(on, 'Index')
            if index is None:
                return 0
            if index == 0:
                self.warn('stylist_parseBodyNode: Unsupported index value "%d"...' % index)
                return 0
            e = lst['entries'].get(index)
            eex = e is not None
            if not eex:
                e = {'look': lst['look'], 'index': index, 'value': 0, 'human': None, 'doram': None}
                if not self.nodes_exist(on, ['Value']):
                    return 0
            if exists(on, 'Value'):
                look = lst['look']
                if look in (LOOK_HEAD_TOP, LOOK_HEAD_MID, LOOK_HEAD_BOTTOM):
                    n = self.as_string(on, 'Value')
                    if n is None:
                        return 0
                    it = self.idb.search_aegisname(n)
                    if it is None:
                        self.warn('stylist_parseBodyNode: Unknown item "%s"...' % n)
                        return 0
                    value = it['nameid']
                else:
                    value = self.as_uint32(on, 'Value')
                    if value is None:
                        return 0
                    bounds = {LOOK_HAIR: ('min_hair_style', 'max_hair_style', 'hair style'),
                              LOOK_HAIR_COLOR: ('min_hair_color', 'max_hair_color', 'hair color'),
                              LOOK_CLOTHES_COLOR: ('min_cloth_color', 'max_cloth_color', 'cloth color')}.get(look)
                    if bounds:  # LOOK_BODY2 is not range-checked ("job specific now")
                        if value < self.conf[bounds[0]]:
                            self.warn('stylist_parseBodyNode: %s "%d" is too low...' % (bounds[2], value))
                            return 0
                        if value > self.conf[bounds[1]]:
                            self.warn('stylist_parseBodyNode: %s "%d" is too high...' % (bounds[2], value))
                            return 0
                e['value'] = value
            for key, doram in (('CostsHuman', False), ('CostsDoram', True)):
                if exists(on, key):
                    if not self.parse_cost_node(e, doram, on.first[key]):
                        return 0
                elif not eex:
                    e['doram' if doram else 'human'] = None
            if not eex:
                lst['entries'][index] = e
            count += 1
        if not ex:
            self.data[c] = lst
        return count


class BarterDatabase(YamlDatabase):
    TYPE, VERSION, MIN_VERSION = 'BARTER_DB', 2, 1

    def __init__(self, ld, idb, conf, map_index, server_maps):
        super().__init__(ld)
        self.data = UMap(str_hash)
        self.idb, self.conf, self.map_index, self.server_maps = idb, conf, map_index, server_maps

    def default_location(self):
        return 'npc/barters.yml'

    def parse_body_node(self, node):
        npcname = self.as_string(node, 'Name')
        if npcname is None:
            return 0
        b = self.data.get(npcname)
        ex = b is not None
        if not ex:
            b = {'name': npcname, 'map': None, 'x': 0, 'y': 0, 'dir': 0, 'sprite': JT_FAKENPC, 'items': {}}
        if exists(node, 'Map'):
            m = self.as_string(node, 'Map')
            if m is None:
                return 0
            mi = self.map_index(m)
            if mi is None:
                self.warn('barter_parseBodyNode: Unknown mapname %s, skipping.' % m)
                return 0
            if mi not in self.server_maps:
                return 1  # skip silently: the map is not on this map-server
            b['map'] = mi
        elif not ex:
            b['map'] = None
        size = self.server_maps.get(b['map']) if b['map'] is not None else None
        for key, axis in (('X', 0), ('Y', 1)):
            if exists(node, key):
                v = self.as_uint16(node, key)
                if v is None:
                    return 0
                if size is None:
                    self.warn('barter_parseBodyNode: Barter NPC is not on a map. Ignoring %s coordinate...' % key)
                    v = 0
                elif v >= size[axis]:
                    self.warn('barter_parseBodyNode: %s coordinate %d is out of bound %d...' % (key, v, size[axis]))
                    return 0
                b[key.lower()] = v
            elif not ex:
                b[key.lower()] = 0
        if exists(node, 'Direction'):
            d = self.as_string(node, 'Direction')
            if d is None:
                return 0
            c = self.ld.const('DIR_' + d)
            if c is None:
                self.warn('barter_parseBodyNode: Unknown direction %s, skipping.' % d)
                return 0
            if c < DIR_NORTH or c >= DIR_MAX:
                self.warn('barter_parseBodyNode: Invalid direction %s, defaulting to North.' % d)
                c = DIR_NORTH
            b['dir'] = c
        elif not ex:
            b['dir'] = DIR_NORTH
        if exists(node, 'Sprite'):
            s = self.as_string(node, 'Sprite')
            if s is None:
                return 0
            c = self.ld.const(s)
            if c is None:
                self.warn('barter_parseBodyNode: Unknown sprite name %s, skipping.' % s)
                return 0
            if c != JT_FAKENPC and not npcdb_checkid(c):
                self.warn('barter_parseBodyNode: Invalid sprite name %s, skipping.' % s)
                return 0
            b['sprite'] = c16(c)
        elif not ex:
            b['sprite'] = JT_FAKENPC
        if exists(node, 'Items'):
            for itn in children(node.first['Items']):
                index = self.as_uint16(itn, 'Index')
                if index is None:
                    return 0
                it = b['items'].get(index)
                iex = it is not None
                if not iex:
                    if not self.nodes_exist(itn, ['Item']):
                        return 0
                    it = {'index': index, 'nameid': 0, 'stock_limited': False, 'stock': 0, 'price': 0, 'refine': 0,
                          'requirements': {}}
                if exists(itn, 'Item'):
                    n = self.as_string(itn, 'Item')
                    if n is None:
                        return 0
                    d = self.idb.search_aegisname(n)
                    if d is None:
                        self.warn('barter_parseBodyNode: Unknown item %s.' % n)
                        return 0
                    it['nameid'] = d['nameid']
                if exists(itn, 'Stock'):
                    v = self.as_uint32(itn, 'Stock')
                    if v is None:
                        return 0
                    it['stock'] = v
                    it['stock_limited'] = v > 0
                elif not iex:
                    it['stock'] = 0
                    it['stock_limited'] = False
                if exists(itn, 'Zeny'):
                    v = self.as_uint32(itn, 'Zeny')
                    if v is None:
                        return 0
                    if v > MAX_ZENY:
                        self.warn('barter_parseBodyNode: Zeny price %d is above MAX_ZENY (%d), capping...' % (v, MAX_ZENY))
                        v = MAX_ZENY
                    it['price'] = v
                elif not iex:
                    it['price'] = 0
                if exists(itn, 'Refine'):
                    d = self.idb.data.get(it['nameid'])
                    if d['flag']['no_refine']:
                        self.warn('barter_parseBodyNode: Item %s is not refineable.' % d['name'])
                        return 0
                    v = self.as_int16(itn, 'Refine')
                    if v is None:
                        return 0
                    if v > MAX_REFINE:
                        self.warn('barter_parseBodyNode: Refine %d is too high, capping to %d.' % (v, MAX_REFINE))
                        v = MAX_REFINE
                    it['refine'] = ((v & 0xff) ^ 0x80) - 0x80
                elif not iex:
                    it['refine'] = 0
                if exists(itn, 'RequiredItems'):
                    for rn in children(itn.first['RequiredItems']):
                        ri = self.as_uint16(rn, 'Index')
                        if ri is None:
                            return 0
                        if len(it['requirements']) >= MAX_BARTER_REQUIREMENTS:
                            self.warn('barter_parseBodyNode: Failed at Index %d. Too many requirements, Barters support up to %d.' % (ri, MAX_BARTER_REQUIREMENTS))
                            return 0
                        r = it['requirements'].get(ri)
                        rex = r is not None
                        if not rex:
                            if not self.nodes_exist(rn, ['Item']):
                                return 0
                            r = {'index': ri, 'nameid': 0, 'amount': 0, 'refine': 0}
                        if exists(rn, 'Item'):
                            n = self.as_string(rn, 'Item')
                            if n is None:
                                return 0
                            d = self.idb.search_aegisname(n)
                            if d is None:
                                self.warn('barter_parseBodyNode: Unknown required item %s.' % n)
                                return 0
                            r['nameid'] = d['nameid']
                        if exists(rn, 'Amount'):
                            v = self.as_uint16(rn, 'Amount')
                            if v is None:
                                return 0
                            if v > MAX_AMOUNT:
                                self.warn('barter_parseBodyNode: Amount %d is too high, capping to %d...' % (v, MAX_AMOUNT))
                                v = MAX_AMOUNT
                            r['amount'] = v
                        elif not rex:
                            r['amount'] = 1
                        if exists(rn, 'Refine'):
                            d = self.idb.data.get(r['nameid'])
                            if d['flag']['no_refine']:
                                self.warn('barter_parseBodyNode: Item %s is not refineable.' % d['name'])
                                return 0
                            v = self.as_int16(rn, 'Refine')
                            if v is None:
                                return 0
                            if v > MAX_REFINE:
                                self.warn('barter_parseBodyNode: Refine %d is too high, capping to %d.' % (v, MAX_REFINE))
                                v = MAX_REFINE
                            r['refine'] = ((v & 0xff) ^ 0x80) - 0x80
                        elif not rex:
                            r['refine'] = -1
                        if not rex:
                            it['requirements'][ri] = r
                if not iex:
                    b['items'][index] = it
        if not ex:
            self.data[npcname] = b
        return 1

    def loading_finished(self):
        """BarterDatabase::loadingFinished: which barters get an NPC (and whether it is an extended barter). Creating
        the NPCs and the `barter` stock table rows is the NPC module's part."""
        for _, b in self.data.items():
            b['npc'] = False
            b['extended'] = False
        for _, b in self.data.items():
            if not self.conf['feature.barter'] and not self.conf['feature.barter_extended']:
                print('  Barter system is not enabled.')
                return
            extended = False
            for _, it in sorted(b['items'].items()):
                if it['price'] > 0 or it['refine'] > 0 or not it['requirements'] or len(it['requirements']) > 1:
                    extended = True
                    break
                if any(r['refine'] >= 0 for r in it['requirements'].values()):
                    extended = True
                    break
            b['extended'] = extended
            if extended and not self.conf['feature.barter_extended']:
                print('  Barter %s uses extended mechanics but this is not enabled.' % b['name'])
                continue
            b['npc'] = True


# --------------------------------------------------------------------------------------- roulette
# The rows of rAthena's `db_roulette` table (conf/inter_athena.conf `roulette_table: db_roulette`, main.sql's CREATE
# TABLE) as a new install fills it: sql-files/README.md "New Install" imports main.sql (no rows) and
# roulette_default_data.sql (the 42 KRO rows). Loki has no SQL server: these files stand in for the table. An empty
# list is an empty table (every level Apples, as on an oracle whose database got main.sql only).
ROULETTE_SQL = ['sql-files/roulette_default_data.sql']


def roulette_rows(root):
    """`SELECT * FROM db_roulette`: MyISAM without ORDER BY returns the rows in physical order, which for a table filled
    by INSERTs only is the INSERT order. Each row: the columns as the strings Sql_GetData hands out."""
    rows = []
    pat = re.compile(r"INSERT\s+INTO\s+`db_roulette`\s*\(([^)]*)\)\s*VALUES\s*\(([^)]*)\)\s*;", re.I)
    for rel in ROULETTE_SQL:
        text = open(os.path.join(root, rel), encoding='latin-1').read()
        for m in pat.finditer(text):
            cols = [c.strip().strip('`') for c in m.group(1).split(',')]
            vals = [v.strip().strip("'") for v in m.group(2).split(',')]
            row = dict(zip(cols, vals))
            # SELECT * column order: index, level, item_id, amount, flag (amount / flag default '1')
            rows.append([row.get('index', '0'), row.get('level', ''), row.get('item_id', ''),
                         row.get('amount', '1'), row.get('flag', '1')])
    return rows


def roulette_db(ld, idb, conf):
    """itemdb.cpp itemdb_parse_roulette_db (itemdb_read, only with battle_config.feature_roulette): the rows are dealt
    out in order, MAX_ROULETTE_COLUMNS - i to level i — the `level` column only appears in the warnings. A skipped row
    (unknown item, amount outside 1..MAX_AMOUNT, flag outside 0..1) still uses up its slot. A level left short is
    filled with Apples (1, flag 0); rAthena's RECREATE leaves the new slots uninitialised and fills those whose qty
    reads 0, taken here as all of them. Returns rd as MAX_ROULETTE_LEVEL lists of [nameid, qty, flag]."""
    levels = [[] for _ in range(MAX_ROULETTE_LEVEL)]
    if not conf['feature.roulette']:
        return levels
    rows = iter(roulette_rows(ld.ctx['root']))
    count = 0
    for i in range(MAX_ROULETTE_LEVEL):
        limit = MAX_ROULETTE_COLUMNS - i
        k = 0
        while k < limit:
            row = next(rows, None)
            if row is None:
                break
            k += 1
            level = atoi(row[1])
            item_id = strtoul10(row[2])
            amount = atoi(row[3]) & 0xFFFF  # uint16 amount = atoi(data)
            flag = atoi(row[4])
            if item_id not in idb.data:
                print("  itemdb_parse_roulette_db: Unknown item ID '%d' in level '%d'" % (item_id, level))
                continue
            if amount < 1 or amount > MAX_AMOUNT:
                print("  itemdb_parse_roulette_db: Unsupported amount '%d' for item ID '%d' in level '%d'" % (amount, item_id, level))
                continue
            if flag < 0 or flag > 1:
                print("  itemdb_parse_roulette_db: Unsupported flag '%d' for item ID '%d' in level '%d'" % (flag, item_id, level))
                continue
            levels[i].append([item_id, amount, flag])
            count += 1
    for i in range(MAX_ROULETTE_LEVEL):
        limit = MAX_ROULETTE_COLUMNS - i
        if len(levels[i]) == limit:
            continue
        # (more than `limit` cannot happen: the loop above stops at `limit` rows)
        print('  itemdb_parse_roulette_db: Level %d has %d items, %d are required. Filling with Apples...' % (i + 1, len(levels[i]), limit))
        levels[i] += [[ITEMID_APPLE, 1, 0] for _ in range(limit - len(levels[i]))]
    print("  Done reading '%d' entries in 'db_roulette'." % count)
    return levels


# --------------------------------------------------------------------------------------- output
def compact(obj, defaults):
    """Drops the fields whose value is the serde default the Rust side declares."""
    return {k: v for k, v in obj.items() if k not in defaults or defaults[k] != v}


ITEM_DEFAULTS = {
    'value_buy': 0, 'value_sell': 0, 'type': IT_ETC, 'subtype': 0, 'sex': SEX_BOTH, 'equip': 0, 'weight': 0, 'atk': 0,
    'def': 0, 'range': 0, 'slots': 0, 'look': 0, 'elv': 0, 'weapon_level': 0, 'armor_level': 0, 'view_id': 0,
    'elvmax': MAX_LEVEL, 'matk': 0, 'class_base': [MAPID_ALL] * 3, 'class_upper': ITEMJ_ALL,
    'gm_lv_trade_override': 100, 'combos': [], 'flag': {}, 'stack': {}, 'item_usage': {}, 'delay': {},
}
FLAG_DEFAULTS = {'available': True, 'no_equip': 0, 'no_refine': True, 'delay_consume': 0, 'trade_restriction': {},
                 'buyingstore': False, 'dead_branch': False, 'group': False, 'guid': False, 'broadcast': False,
                 'bind_on_equip': False, 'drop_effect': 0, 'gradable': False}


def item_json(it):
    o = {k: v for k, v in it.items() if k not in ('script', 'equip_script', 'unequip_script')}
    fl = dict(it['flag'])
    fl['trade_restriction'] = {k: v for k, v in fl['trade_restriction'].items() if v}
    o['flag'] = compact(fl, FLAG_DEFAULTS)
    o['stack'] = {k: v for k, v in it['stack'].items() if v}
    o['item_usage'] = compact(it['item_usage'], {'override': 100, 'sitting': False})
    o['delay'] = compact(it['delay'], {'duration': 0, 'sc': SC_NONE})
    return compact(o, ITEM_DEFAULTS)


def ulist(m, f=lambda v: v):
    """An unordered_map as a list in libstdc++ iteration order."""
    return [f(v) for v in m.values()]


def pairs(m):
    """unordered_map<K, scalar> as [[k, v], ...] in iteration order."""
    return [[k, v] for k, v in m.items()]


def build(ctx):
    ld = Loader(ctx)
    conf = battle_config(ld)
    root = ctx['root']

    idb = ItemDatabase(ld)
    idb.area_size = conf['area_size']
    idb.load()
    read_noequip(ld, idb)
    ropt = RandomOptionDatabase(ld)
    ropt.load()
    rgroup = RandomOptionGroupDatabase(ld, ropt)
    rgroup.load()
    groups = ItemGroupDatabase(ld, idb, rgroup, conf)
    groups.load()
    combos = ComboDatabase(ld, idb)
    combos.load()
    synth = LaphineSynthesisDatabase(ld, idb, groups)
    synth.load()
    upgrade = LaphineUpgradeDatabase(ld, idb, rgroup)
    upgrade.load()
    reform = ItemReformDatabase(ld, idb, rgroup)
    reform.load()
    enchant = ItemEnchantDatabase(ld, idb)
    enchant.load()
    packages = ItemPackageDatabase(ld, idb, rgroup)
    packages.load()
    # itemdb_read's last step: `if (battle_config.feature_roulette) itemdb_parse_roulette_db();`
    roulette = roulette_db(ld, idb, conf)
    cash = CashShopDatabase(ld, idb)
    cash.load()
    refine = RefineDatabase(ld, idb)
    refine.load()
    egrade = EnchantgradeDatabase(ld, idb)
    egrade.load()
    stylist = StylistDatabase(ld, idb, conf)
    stylist.load()

    # mapindex_name2idx (db/map_index.txt) and the maps this map-server loads (conf/map_athena.conf `map:` lines that
    # have a map cache entry — the same list maps.bin holds)
    index = set()
    for line in open(ctx['resolve']('db/map_index.txt'), encoding='latin-1'):
        line = line.split('//')[0].strip()
        if line:
            index.add(line.split()[0])
    import struct
    server_maps = {}
    mb = open(os.path.join(ctx['outdir'], 'maps.bin'), 'rb').read()
    p = 12
    for _ in range(struct.unpack_from('<I', mb, 8)[0]):
        n = mb[p]
        name = mb[p + 1:p + 1 + n].decode('latin-1')
        xs, ys, ln = struct.unpack_from('<HHI', mb, p + 1 + n)
        server_maps[name] = (xs, ys)
        p += 1 + n + 8 + ln

    def map_index(m):
        m = m[:15]
        if len(m) >= 4 and m[-4:].lower() == '.gat':
            m = m[:-4]
        m = m[:11]
        return m if m in index else None
    barters = BarterDatabase(ld, idb, conf, map_index, server_maps)
    barters.load()
    # BarterDatabase::reload (@reloadbarterdb, npc_reload's barter_db.reload()): clear() keeps the unordered_map's bucket
    # count, so the same keys loaded again iterate in another order (zz_reload_orders.py explains why), the same after
    # every later reload; loadingFinished creates the barter NPCs in it. A second instance loaded twice gives it (its
    # warnings are the first load's again: not printed).
    import contextlib, io
    with contextlib.redirect_stdout(io.StringIO()):
        rb = BarterDatabase(Loader(ctx), idb, conf, map_index, server_maps)
        rb.load()
        rb.data.clear()
        rb.load()
    ctx['barter_reload_order'] = list(rb.data.keys())

    # ---- the datasets
    items = [idb.data[k] for k in sorted(idb.data)]
    derived_aegis = {id(it): lower(it['name']) for it in items}
    aegis_missing = [it['nameid'] for it in items if idb.aegis_map.get(lower(it['name'])) is not it]
    aegis_extra = [[k, v['nameid']] for k, v in idb.aegis_map.items()
                   if idb.data.get(v['nameid']) is not v or derived_aegis.get(id(v)) != k]
    name_missing = [it['nameid'] for it in items if idb.name_map.get(lower(it['ename'])) is not it]
    name_extra = [[k, v['nameid']] for k, v in idb.name_map.items()
                  if idb.data.get(v['nameid']) is not v or lower(v['ename']) != k]
    ctx['write_json']('items_db', {
        'items': [item_json(it) for it in items],
        'aegis_missing': aegis_missing, 'aegis_extra': aegis_extra,
        'name_missing': name_missing, 'name_extra': name_extra,
    })
    ctx['write_json']('items_script', [
        compact({'nameid': it['nameid'], 'script': it['script'], 'equip_script': it['equip_script'],
                 'unequip_script': it['unequip_script']}, {'script': None, 'equip_script': None, 'unequip_script': None})
        for it in items if it['script'] is not None or it['equip_script'] is not None or it['unequip_script'] is not None])
    ctx['write_json']('items_combos', [compact(c, {'script': None}) for c in combos.data.values()])

    ctx['write_json']('items_randomopt', {
        'options': [compact(o, {'script': None}) for o in ropt.data.values()],
        'constants': ropt.constants,
        'groups': [{'id': g['id'], 'name': g['name'], 'slots': [[k, v] for k, v in sorted(g['slots'].items())],
                    'max_random': g['max_random'], 'random_options': g['random_options']} for g in rgroup.data.values()],
    })
    ctx['write_json']('items_group', [
        {'id': gid, 'random': [{'subgroup': sub, 'algorithm': r['algorithm'], 'total_rate': r['total_rate'],
                                'data': [compact(e, {'random_option_group': None, 'duration': 0, 'is_announced': False,
                                                     'is_named': False, 'bound': 0, 'refine_minimum': 0,
                                                     'refine_maximum': 0, 'minimum_enchantgrade': 0,
                                                     'maximum_enchantgrade': 0, 'guid': False, 'amount': 1,
                                                     'is_stacked': True})
                                         for e in r['data'].values()]}
                               for sub, r in groups.data[gid]['random'].items()]}
        for gid in sorted(groups.data.keys())])
    ctx['write_json']('items_laphine_synthesis', [dict(e, requirements=ulist(e['requirements'])) for e in synth.data.values()])
    ctx['write_json']('items_laphine_upgrade', list(upgrade.data.values()))
    ctx['write_json']('items_reform', [
        {'item_id': e['item_id'], 'base_items': [dict(b, materials=pairs(b['materials'])) for b in e['base_items'].values()]}
        for e in reform.data.values()])
    ctx['write_json']('items_enchant', [
        dict(e, reset=dict(e['reset'], materials=pairs(e['reset']['materials'])),
             slots=[{'slot': s['slot'],
                     'normal': dict(s['normal'], materials=pairs(s['normal']['materials']),
                                    enchantgrade_chance_increase=pairs(s['normal']['enchantgrade_chance_increase']),
                                    enchants=[{'enchantgrade': g['enchantgrade'], 'enchants': ulist(g['enchants'])}
                                              for g in s['normal']['enchants'].values()]),
                     'perfect': [dict(pe, materials=pairs(pe['materials'])) for pe in s['perfect']['enchants'].values()],
                     'upgrade': [dict(u, materials=pairs(u['materials'])) for u in s['upgrade']['enchants'].values()]}
                    for s in e['slots'].values()])
        for e in enchant.data.values()])
    ctx['write_json']('items_packages', [
        {'item_id': e['item_id'], 'groups': [{'group_index': g['group_index'], 'items': ulist(g['items'])}
                                             for g in e['groups'].values()]}
        for e in packages.data.values()])
    ctx['write_json']('items_roulette', roulette)
    ctx['write_json']('items_cash', list(cash.data.values()))
    ctx['write_json']('items_refine', [
        {'group_id': r['group_id'], 'levels': [{'level': l['level'], 'levels': [dict(x, costs=ulist(x['costs']))
                                                                               for x in l['levels'].values()]}
                                               for l in r['levels'].values()]}
        for r in refine.data.values()])
    ctx['write_json']('items_enchantgrade', [
        {'itemtype': e['itemtype'], 'levels': [[lv, [dict(g, options=[g['options'][k] for k in sorted(g['options'])])
                                                     for _, g in sorted(grades.items())]]
                                               for lv, grades in sorted(e['levels'].items())]}
        for e in egrade.data.values()])
    ctx['write_json']('items_stylist', [{'look': s['look'], 'entries': ulist(s['entries'])} for s in stylist.data.values()])
    ctx['write_json']('items_barters', [
        dict(b, items=[dict(it, requirements=[it['requirements'][k] for k in sorted(it['requirements'])])
                       for _, it in sorted(b['items'].items())])
        for b in barters.data.values()])

    # ---- what rAthena would have printed
    print('items: %d entries loaded, %d warnings; per file:' % (len(items), ld.warnings))
    for path, n in ld.done:
        print("  Done reading '%d' entries in '%s'" % (n, path))
    if aegis_extra or name_extra:
        print('  AegisName/Name lookups hold entries that are not derivable from the items: %d / %d' % (len(aegis_extra), len(name_extra)))
    print('  %d items whose Name is shadowed in the Name lookup; %d RDMOPT_ constants to register' % (len(name_missing), len(ropt.constants)))
