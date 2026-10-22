"""
Monster databases (rAthena e985006 map/mob.cpp, RENEWAL) for the Loki Online module, plus the loader machinery the
other families share (tools/datagen_mods/misc.py imports this file):

  * YamlDb: common/database.cpp YamlDatabase (Header check, Clear, Body, Footer Imports with Mode, parse counts) and the
    ryml conversions it uses (asBool/asInt*/asUInt*/asString/asUInt*Rate: c4 atoi/atou semantics, wrap-around on
    overflow, null values), over a YAML loader that keeps every scalar as text like ryml does.
  * UMap: libstdc++ (GCC 13, the compiler the oracle was built with) std::unordered_map<integral, T> — rAthena's
    databases are unordered_maps and several lookups return the first match *in iteration order*, so every dataset is
    written in the order rAthena iterates it. Verified against g++ 13.3 with random insert/erase/clear sequences.
  * battle_config: map/battle.cpp battle_data[] (defaults/min/max, RENEWAL branches) + conf/battle_athena.conf
    (battle_config_read / battle_set_value / config_switch) + battle_adjust_conf.
  * The parts of other databases the loaders consult: map_index.txt (mapindex_name2idx), the loaded maps (data/maps.bin,
    map_mapindex2mapid), item_db (search_aegisname, type, look), item_randomopt_group (option_get_id), skill_db
    (skill_name2id, skill_get_max, skill_get_casttype == CAST_GROUND). Their own datasets belong to other families;
    here they are only read, and their entry counts are checked against what rAthena reports.

Datasets written (data/json/mobs_*.json.z):
  mobs_db         MobDatabase after mob_load(): parseBodyNode + loadingFinished (status_calc_misc, rates), drops after
                  mob_drop_ratio_adjust, skills after mob_skill_db_set; in mob_db iteration order.
  mobs_chat       MobChatDatabase.
  mobs_summon     MobSummonDatabase (entries in iteration order: umap_random picks by position).
  mobs_map_drops  MapDropDatabase (by map name; globals/specific in iteration order).
  mobs_item_drops what mob_drop_ratio_adjust writes into item_db: maxchance and mob[MAX_SEARCH] per item (itemdb owns
                  those fields; the item family merges them).
"""
import bisect
import math
import os
import re
import struct

# --------------------------------------------------------------------------------------------------------- constants
# Values printed by a probe compiled against the rAthena headers (PACKETVER 20211103, renewal; see
# loki/tools/packetgen/build_probe.sh), not renumbered by hand.
K = dict(
    SC_MAX=1044, SC_NONE=-1, SC_COMMON_MAX=11, SCB_MAX=45, SCS_MAX=25, SCF_MAX=53, OPT1_MAX=9, OPT2_MAX=257,
    OPT3_MAX=131073, OPTION_MAX=536870913, EFST_MAX=1689, EFST_BLANK=-1, OPT1_NONE=0, OPT2_NONE=0, OPT3_NORMAL=0,
    OPTION_NOTHING=0, SCB_NONE=0, SCS_NONE=0, SCF_NONE=0, SCB_BASE=1, SCB_DYE=44,
    BL_SCEFFECT=539, BL_NUL=0, BL_PC=1, BL_MOB=2, BL_PET=4, BL_HOM=8, BL_MER=16, BL_ITEM=32, BL_SKILL=64, BL_NPC=128,
    BL_CHAT=256, BL_ELEM=512, BL_ALL=4095, BL_CHAR=539, BL_REGEN=537,
    MOBG_BRANCH_OF_DEAD_TREE=0, MOBG_MAX=6, WOE_FIRST_EDITION=1, WOE_MAX=4, HM_SKILLBASE=8001, MAX_HOMUNSKILL=59,
    MC_SKILLBASE=8201, MAX_MERCSKILL=41, EL_SKILLBASE=8401, MAX_ELEMENTALSKILL=57, SKILL_NAME_LENGTH=40,
    MAX_SKILL_LEVEL=13, ITEMID_PET_FOOD=537, ITEMID_DUMMY=499, HM_CLASS_BASE=6001, HM_CLASS_MAX=6052,
    NPC_NAME_LENGTH=50, EVENT_NAME_LENGTH=77, MAX_ZENY=2147483647, ITEM_NAME_LENGTH=50, NPC_EMOTION=197,
    NPC_EMOTION_ON=474, MAX_MOBSKILL=50, MAX_MOB_DROP=10, MAX_MVP_DROP=3, MAX_SEARCH=10, CHAT_SIZE_MAX=256,
    NAME_LENGTH=24, MAX_LEVEL=275, MAX_ASPD_NOPC=100, MIN_ASPD=8000, AMOTION_DIVIDER_NOPC=1, AMOTION_DIVIDER_PC=2,
    AMOTION_ZERO_ASPD=2000, AMOTION_INTERVAL=10, DEFAULT_WALK_SPEED=150, MIN_WALK_SPEED=20, MAX_WALK_SPEED=1000,
    DEFTYPE_MIN=-32768, DEFTYPE_MAX=32767, MAX_EXP=9223372036854775807, NATURAL_HEAL_INTERVAL=500, MAX_STORAGE=600,
    MIN_STORAGE=600, MAX_REFINE=20, MAX_PARTY=12, BOUND_NONE=0, BOUND_CHAR=4, BOUND_MAX=5, UCHAR_MAX=255,
    INT32_MAX=2147483647, SHRT_MAX=32767, INT_MAX=2147483647, INT_MIN=-2147483648, USHRT_MAX=65535, UINT16_MAX=65535,
    UINT32_MAX=4294967295, MAX_WALKPATH=32, MAX_MOBSKILL_LEVEL=100, RC_FORMLESS=0, RC_PLAYER_HUMAN=10,
    RC_PLAYER_DORAM=11, RC_ALL=12, RC_MAX=13, RC_DEMIHUMAN=7, SZ_SMALL=0, SZ_MEDIUM=1, SZ_BIG=2, SZ_ALL=3,
    RC2_NONE=0, RC2_TREASURE=9, RC2_MAX=43, ELE_NEUTRAL=0, ELE_ALL=10, ELE_MAX=11, MAX_ELE_LEVEL=4, MAX_AMOUNT=30000,
    MAX_QUEST_OBJECTIVES=3, MAX_ACHIEVEMENT_OBJECTIVES=10, MAX_ACHIEVEMENT_DEPENDENTS=20, TITLE_BASE=1000,
    TITLE_MAX=1046, MAX_BG_MEMBERS=30, LOOK_MAX=14, CLASS_NORMAL=0, CLASS_BOSS=1, CLASS_GUARDIAN=2,
    CLASS_BATTLEFIELD=4, CLASS_EVENT=5, MONSTER_TYPE_06=0, MD_NONE=0, MD_MASK=0xFFFF, MD_AGGRESSIVE=0x4,
    MD_MVP=0x80000, MD_KNOCKBACKIMMUNE=0x200000, MD_FIXEDITEMDROP=0x1000000, MD_DETECTOR=0x2000000,
    MD_STATUSIMMUNE=0x4000000, MD_SKILLIMMUNE=0x8000000, MD_IGNOREMELEE=0x10000, MD_IGNOREMAGIC=0x20000,
    MD_IGNORERANGED=0x40000, MD_IGNOREMISC=0x100000, IT_HEALING=0, IT_USABLE=2, IT_ETC=3, IT_ARMOR=4, IT_WEAPON=5,
    IT_CARD=6, IT_PETEGG=7, IT_PETARMOR=8, IT_DELAYCONSUME=11, IT_SHADOWGEAR=12, IT_CASH=18, IT_MAX=19,
    INF_GROUND_SKILL=2, OPTION_HIDE=0x2, OPTION_CLOAK=0x4, OPTION_INVISIBLE=0x40, OPTION_CHASEWALK=0x4000,
    OPTION_CART=0x8, SEX_FEMALE=0, SEX_MALE=1, W_FIST=0, W_2HSTAFF=23,
)
# mob.cpp: monster id ranges and clone range (MIN_MOB_DB 1000, MAX_MOB_DB 3999, MIN_MOB_DB2 20020, MAX_MOB_DB2 31999,
# MOB_CLONE_START MAX_MOB_DB, MOB_CLONE_END MIN_MOB_DB2), MOB_MAX_DELAY 24h.
MIN_MOB_DB, MAX_MOB_DB, MIN_MOB_DB2, MAX_MOB_DB2 = 1000, 3999, 20020, 31999
MOB_MAX_DELAY = 24 * 3600 * 1000
# config/renewal.hpp: the defines a RENEWAL build has (config/core.hpp leaves VIP_ENABLE off).
DEFINES = {'RENEWAL', 'RENEWAL_CAST', 'RENEWAL_DROP', 'RENEWAL_EXP', 'RENEWAL_LVDMG', 'RENEWAL_ASPD', 'RENEWAL_STAT'}


def warn(msg):
    print('  [warning] ' + msg)


# ------------------------------------------------------------------------------------ libstdc++ unordered_map order
_PRIMES = [17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 103, 109, 113, 127, 137, 139,
           149, 157, 167, 179, 193, 199, 211, 227, 241, 257, 277, 293, 313, 337, 359, 383, 409, 439, 467, 503, 541, 577,
           619, 661, 709, 761, 823, 887, 953, 1031, 1109, 1193, 1289, 1381, 1493, 1613, 1741, 1879, 2029, 2179, 2357,
           2549, 2753, 2971, 3209, 3469, 3739, 4027, 4349, 4703, 5087, 5503, 5953, 6427, 6949, 7517, 8123, 8783, 9497,
           10273, 11113, 12011, 12983, 14033, 15173, 16411, 17749, 19183, 20753, 22447, 24281, 26267, 28411, 30727,
           33223, 35933, 38873, 42043, 45481, 49201, 53201, 57557, 62233, 67307, 72817, 78779, 85229, 92203, 99733,
           107897, 116731, 126271, 136607, 147793, 159871, 172933, 187091, 202409, 218971, 236897, 256279, 277261,
           299951, 324503, 351061, 379787, 410857, 444487, 480881, 520241, 562841, 608903, 658753, 712697, 771049,
           834181, 902483, 976369, 1056323]
_FAST_BKT = [2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13]
_BEFORE = object()


class UMap:
    """std::unordered_map<integral key, T> as libstdc++ (bits/hashtable.h, _Prime_rehash_policy) lays it out: keys hash to
    themselves, a new key goes to the front of its bucket (or of the whole list when the bucket is empty), rehashing
    relinks in list order. Iterating yields keys in the order a C++ range-for visits them."""

    def __init__(self):
        self.nb, self.next_resize, self.buckets = 1, 0, [None]
        self.nxt, self.head, self.vals = {}, None, {}

    def _next_bkt(self, n):
        if n < len(_FAST_BKT):
            if n == 0:
                return 1
            self.next_resize = _FAST_BKT[n]
            return _FAST_BKT[n]
        p = _PRIMES[bisect.bisect_left(_PRIMES, n)]
        self.next_resize = p
        return p

    def _bkt(self, k, n=None):
        return (k % (1 << 64)) % (n or self.nb)

    def _rehash(self, n):
        nb, p, self.head, bbegin = [None] * n, self.head, None, 0
        while p is not None:
            nx, b = self.nxt[p], self._bkt(p, n)
            if nb[b] is None:
                self.nxt[p], self.head, nb[b] = self.head, p, _BEFORE
                if self.nxt[p] is not None:
                    nb[bbegin] = p
                bbegin = b
            else:
                prev = nb[b]
                if prev is _BEFORE:
                    self.nxt[p], self.head = self.head, p
                else:
                    self.nxt[p], self.nxt[prev] = self.nxt[prev], p
            p = nx
        self.buckets, self.nb = nb, n

    def __contains__(self, k):
        return k in self.vals

    def get(self, k, d=None):
        return self.vals.get(k, d)

    def __getitem__(self, k):
        return self.vals[k]

    def __len__(self):
        return len(self.vals)

    def __setitem__(self, k, v):
        if k in self.vals:
            self.vals[k] = v
            return
        n_elt = len(self.vals)
        if n_elt + 1 > self.next_resize:  # _M_need_rehash
            min_bkts = max(n_elt + 1, 0 if self.next_resize else 11) / 1.0
            if min_bkts >= self.nb:
                self._rehash(self._next_bkt(max(math.floor(min_bkts) + 1, self.nb * 2)))
            else:
                self.next_resize = self.nb
        b = self._bkt(k)
        prev = self.buckets[b]
        if prev is not None:
            if prev is _BEFORE:
                self.nxt[k], self.head = self.head, k
            else:
                self.nxt[k], self.nxt[prev] = self.nxt[prev], k
        else:
            self.nxt[k], self.head = self.head, k
            if self.nxt[k] is not None:
                self.buckets[self._bkt(self.nxt[k])] = k
            self.buckets[b] = _BEFORE
        self.vals[k] = v

    def pop(self, k, *default):
        if k not in self.vals:
            if default:
                return default[0]
            raise KeyError(k)
        b = self._bkt(k)
        prev = self.buckets[b]
        cur = self.head if prev is _BEFORE else self.nxt[prev]
        while cur != k:
            prev, cur = cur, self.nxt[cur]
        n_next = self.nxt[k]
        if prev is self.buckets[b]:  # _M_remove_bucket_begin
            nbk = self._bkt(n_next) if n_next is not None else 0
            if n_next is None or nbk != b:
                if n_next is not None:
                    self.buckets[nbk] = self.buckets[b]
                self.buckets[b] = None
        elif n_next is not None:
            nbk = self._bkt(n_next)
            if nbk != b:
                self.buckets[nbk] = prev
        if prev is _BEFORE:
            self.head = n_next
        else:
            self.nxt[prev] = n_next
        del self.nxt[k]
        return self.vals.pop(k)

    def clear(self):
        self.buckets, self.head, self.nxt, self.vals = [None] * self.nb, None, {}, {}

    def keys(self):
        out, p = [], self.head
        while p is not None:
            out.append(p)
            p = self.nxt[p]
        return out

    def __iter__(self):
        return iter(self.keys())

    def items(self):
        return [(k, self.vals[k]) for k in self.keys()]

    def values(self):
        return [self.vals[k] for k in self.keys()]


# ----------------------------------------------------------------------------------------------- C number semantics
def wrap(v, bits, signed):
    v &= (1 << bits) - 1
    if signed and v >= 1 << (bits - 1):
        v -= 1 << bits
    return v


def i16(v): return wrap(v, 16, True)
def u16(v): return wrap(v, 16, False)
def i32(v): return wrap(v, 32, True)
def u32(v): return wrap(v, 32, False)
def u8(v): return wrap(v, 8, False)


_ASCII_LOWER = {c: c + 32 for c in range(ord('A'), ord('Z') + 1)}


def lower(s):
    """C-locale tolower over a string (ASCII letters only, as util::tolower / strcmpi / stricmp fold)."""
    return s.translate(_ASCII_LOWER)


def cap_value(a, lo, hi):
    """common/utils.hpp cap_value."""
    return hi if a >= hi else (lo if a <= lo else a)


def cdiv(a, b):
    """C integer division (truncates toward zero)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def strtol(s, base=10):
    """glibc strtol on a C string (returns long, clamped to 64 bits); also yields where parsing stopped."""
    i, n = 0, len(s)
    while i < n and s[i] in ' \t\n\v\f\r':
        i += 1
    neg = False
    if i < n and s[i] in '+-':
        neg = s[i] == '-'
        i += 1
    if base == 0:
        if i + 1 < n and s[i] == '0' and s[i + 1] in 'xX' and i + 2 < n and s[i + 2] in '0123456789abcdefABCDEF':
            base, i = 16, i + 2
        elif i < n and s[i] == '0':
            base = 8
        else:
            base = 10
    elif base == 16 and i + 2 < n and s[i] == '0' and s[i + 1] in 'xX' and s[i + 2] in '0123456789abcdefABCDEF':
        i += 2
    digits = '0123456789abcdefghijklmnopqrstuvwxyz'[:base]
    v, start = 0, i
    while i < n and lower(s[i]) in digits:
        v = v * base + digits.index(lower(s[i]))
        i += 1
    if i == start:
        return 0, 0
    v = -v if neg else v
    return max(-(1 << 63), min((1 << 63) - 1, v)), i


def atoi(s):
    """atoi: (int)strtol(s, NULL, 10)."""
    return i32(strtol(s, 10)[0])


def strtoul32(s, base=0):
    """(uint32)strtoul(s, NULL, base) (negative input wraps like strtoul)."""
    v, _ = strtol(s, base)
    return u32(v)


def config_switch(s):
    """common/strlib.cpp config_switch."""
    t = lower(s)
    if t in ('on', 'yes', 'oui', 'ja', 'si', 'sim'):
        return 1
    if t in ('off', 'no', 'non', 'nein', 'nao'):
        return 0
    return i32(strtol(s, 0)[0])


def c_str(s, n=None):
    """A std::string after `resize(n)` read back through c_str(): at most n bytes, up to the first NUL."""
    if n is not None:
        s = s[:n]
    i = s.find('\0')
    return s if i < 0 else s[:i]


def safestrncpy(s, size):
    """strlib safestrncpy into char[size]."""
    return c_str(s, size - 1)


# ------------------------------------------------------------------------------------------------------ YAML loading
class YMap(dict):
    """A YAML mapping as ryml keeps it: lookups (`m[key]`, `key in m`) find the first child with that key, iteration
    visits every child in file order, repeated keys included (rAthena's `for (const auto& it : node)` loops)."""
    __slots__ = ('seq',)

    def __iter__(self):
        return iter(self.seq)


def load_yaml_text(path):
    """The rAthena tree parsed the way ryml sees it: every scalar stays text (no YAML 1.1 bool/int/octal guessing), a
    plain empty / ~ / null / Null / NULL scalar is null (ryml val_is_null) and becomes None, mappings keep repeated
    keys (YMap)."""
    import yaml

    class Loader(yaml.CBaseLoader):
        pass

    def scalar(loader, node):
        if not node.style and node.value in ('', '~', 'null', 'Null', 'NULL'):  # plain (libyaml: None or '')
            return None
        return node.value.translate(_C1_OUT)

    def mapping(loader, node):
        m = YMap()
        m.seq = []
        for kn, vn in node.value:
            k = loader.construct_object(kn, deep=True)
            v = loader.construct_object(vn, deep=True)
            if k not in m:
                dict.__setitem__(m, k, v)
            m.seq.append(k)
        return m

    def sequence(loader, node):
        return [loader.construct_object(n, deep=True) for n in node.value]
    Loader.add_constructor('tag:yaml.org,2002:str', scalar)
    Loader.add_constructor('tag:yaml.org,2002:map', mapping)
    Loader.add_constructor('tag:yaml.org,2002:seq', sequence)
    # ryml reads bytes; libyaml would take byte 0x85 (NEL) for a line break and refuses the other C1 bytes, so those
    # bytes travel through the parser as private-use code points and come back in the scalars.
    text = open(path, encoding='latin-1').read().translate(_C1_IN)
    return yaml.load(text, Loader=Loader)


_C1_IN = {c: 0xE000 + c for c in range(0x80, 0xA0)}
_C1_OUT = {0xE000 + c: c for c in range(0x80, 0xA0)}


class Fail(Exception):
    """A conversion helper returned false: the C++ caller's `return 0` / `return false`."""


def _c4_unsigned(s):
    """c4::atou digit parsing without the type width (None when the text is not a number)."""
    if not s or s[0] == '-':
        return None

    def rd(t, base, chars):
        v = 0
        for c in t:
            if c not in chars:
                return None
            v = v * base + int(c, base)
        return v
    dec, hexd = '0123456789', '0123456789abcdefABCDEF'
    if s[0] != '0':
        return rd(s, 10, dec)
    if len(s) == 1:
        return 0
    p = s[1]
    if p in 'xX':
        return rd(s[2:], 16, hexd) if len(s) > 2 else None
    if p in 'bB':
        return rd(s[2:], 2, '01') if len(s) > 2 else None
    if p in 'oO':
        return rd(s[2:], 8, '01234567') if len(s) > 2 else None
    t = s.lstrip('0')
    return 0 if not t else rd(t, 10, dec)


def c4_atou(s, bits):
    v = _c4_unsigned(s)
    return None if v is None else wrap(v, bits, False)


def c4_atoi(s, bits):
    if not s:
        return None
    sign = 1
    if s[0] == '-':
        if len(s) == 1:
            return None
        s, sign = s[1:], -1
    v = _c4_unsigned(s)
    return None if v is None else wrap(sign * wrap(v, bits, True), bits, True)


class YamlDb:
    """common/database.cpp YamlDatabase / TypesafeYamlDatabase. `data` is the unordered_map (UMap) of entries."""
    TYPE, VERSION, MIN_VERSION = '', 1, None

    def __init__(self, ctx):
        self.ctx = ctx
        self.data = UMap()
        self.counts = []  # (file, "Done reading" count), in load order
        self.current_file = ''

    # -- load / imports (YamlDatabase::load, parse, parseImports)
    def default_location(self):
        raise NotImplementedError

    def load(self):
        ok = self._load(self.default_location())
        self.loading_finished()
        return ok

    def _load(self, rel):
        path = self.ctx['resolve'](rel)
        if not os.path.exists(path):
            warn('Failed to open %s database file from \'%s\'.' % (self.TYPE, rel))
            return False
        tree = load_yaml_text(path) or {}
        self.current_file = rel
        if not self._verify(tree):
            warn('Failed to verify compatibility with %s database file from \'%s\'.' % (self.TYPE, rel))
            return False
        header = tree['Header']
        if isinstance(header, dict) and 'Clear' in header:
            try:
                if self.as_bool(header, 'Clear'):
                    self.clear()
            except Fail:
                pass
        if isinstance(tree, dict) and 'Body' in tree:
            count = 0
            for node in tree['Body'] or []:
                try:
                    count += self.parse_body_node(node)
                except Fail:
                    pass
            self.counts.append((rel, count))
        footer = tree.get('Footer') if isinstance(tree, dict) else None
        if isinstance(footer, dict) and 'Imports' in footer:
            for node in footer['Imports'] or []:
                try:
                    imp = self.as_str(node, 'Path')
                    if self.exists(node, 'Mode'):
                        # RENEWAL with RENEWAL_ASPD: compiledMode is "Renewal" for every file
                        if self.as_str(node, 'Mode') != 'Renewal':
                            continue
                    if self.exists(node, 'Generator'):
                        self.as_bool(node, 'Generator')
                        continue  # shouldLoadGenerator is false on the map server
                except Fail:
                    continue
                self._load(imp)
        return True

    def _verify(self, tree):
        if not isinstance(tree, dict) or 'Header' not in tree:
            warn('No database "Header" was found.')
            return False
        header = tree['Header']
        try:
            if self.as_str(header, 'Type') != self.TYPE:
                warn('Database type mismatch')
                return False
            v = self.as_u16(header, 'Version')
        except Fail:
            return False
        lo = self.MIN_VERSION if self.MIN_VERSION is not None else self.VERSION
        if v != self.VERSION:
            if v > self.VERSION:
                warn('Database version %d is not supported. Maximum version is: %d' % (v, self.VERSION))
                return False
            if v < lo:
                warn('Database version %d is not supported anymore. Minimum version is: %d' % (v, lo))
                return False
            warn('Database version %d is outdated and should be updated. Current version is: %d (%s)' % (v, self.VERSION, self.current_file))
        return True

    def clear(self):
        self.data.clear()

    def loading_finished(self):
        pass

    def parse_body_node(self, node):
        raise NotImplementedError

    def find(self, key):
        return self.data.get(key)

    def put(self, key, value):
        self.data[key] = value

    def invalid(self, msg):
        warn('%s (in %s)' % (msg, self.current_file))

    # -- node helpers (YamlDatabase::nodeExists / nodesExist / as*)
    @staticmethod
    def exists(node, name):
        return isinstance(node, dict) and len(node) > 0 and name in node

    def nodes_exist(self, node, names):
        missing = [n for n in names if not self.exists(node, n)]
        for n in missing:
            warn('Missing mandatory node "%s".' % n)
        if missing:
            self.invalid('At least one mandatory node did not exist.')
            return False
        return True

    def _scalar(self, node, name):
        if not self.exists(node, name):
            self.invalid('Missing node "%s".' % name)
            raise Fail()
        v = node[name]
        if v is None:
            self.invalid('Node "%s" is missing a value.' % name)
            raise Fail()
        if not isinstance(v, str):
            self.invalid('Node "%s" is not a scalar.' % name)
            raise Fail()
        return v

    def as_str(self, node, name):
        return self._scalar(node, name)

    def as_bool(self, node, name):
        v = lower(self._scalar(node, name))
        if v == 'true':
            return True
        if v == 'false':
            return False
        self.invalid('Unknown boolean value: "%s".' % v)
        raise Fail()

    def _num(self, node, name, fn, bits):
        v = fn(self._scalar(node, name), bits)
        if v is None:
            self.invalid('Node "%s" cannot be parsed as a number.' % name)
            raise Fail()
        return v

    def as_i16(self, node, name): return self._num(node, name, c4_atoi, 16)
    def as_u16(self, node, name): return self._num(node, name, c4_atou, 16)
    def as_i32(self, node, name): return self._num(node, name, c4_atoi, 32)
    def as_u32(self, node, name): return self._num(node, name, c4_atou, 32)
    def as_i64(self, node, name): return self._num(node, name, c4_atoi, 64)
    def as_u64(self, node, name): return self._num(node, name, c4_atou, 64)

    def _rate(self, v, name, maximum):
        if v > maximum:
            self.invalid('Node "%s" with value %d exceeds maximum of %d.' % (name, v, maximum))
            raise Fail()
        if v == 0:
            self.invalid('Node "%s" needs to be at least 1.' % name)
            raise Fail()
        return v

    def as_u16_rate(self, node, name, maximum=10000):
        return self._rate(self.as_u16(node, name), name, maximum)

    def as_u32_rate(self, node, name, maximum=10000):
        return self._rate(self.as_u32(node, name), name, maximum)

    def const(self, name):
        """script_get_constant: case-insensitive, parameters excluded (None when unknown)."""
        return self.ctx['consts'].get(lower(name))


# --------------------------------------------------------------------------------------------------- battle_config
def read_battle_config(ctx):
    """map/battle.cpp: battle_set_defaults (battle_data[] as a RENEWAL build compiles it), battle_config_read of
    conf/battle_athena.conf (imports, `%1023[^:]:%1023s` lines, `//` lines skipped, battle_set_value range check,
    config_switch), battle_adjust_conf. Returns {struct member: value} plus {'__names__': {setting: member}}."""
    root = ctx['root']
    src = open(os.path.join(root, 'src/map/battle.cpp'), encoding='latin-1').read()
    start = src.index('} battle_data[] = {') + len('} battle_data[] = {')
    end = src.index('#include <custom/battle_config_init.inc>', start)
    text = src[start:end]
    custom = os.path.join(root, 'src/custom/battle_config_init.inc')
    if os.path.exists(custom):
        text += '\n' + open(custom, encoding='latin-1').read()
    kept, stack = [], []
    for line in text.split('\n'):
        s = line.strip()
        m = re.match(r'#\s*(ifdef|ifndef|else|endif|if|elif)\b\s*(.*)', s)
        if m:
            d, arg = m.group(1), m.group(2).strip()
            if d == 'ifdef':
                stack.append([arg in DEFINES, arg in DEFINES])
            elif d == 'ifndef':
                stack.append([arg not in DEFINES, arg not in DEFINES])
            elif d == 'else':
                stack[-1][0] = not stack[-1][1]
            elif d == 'endif':
                stack.pop()
            else:
                raise SystemExit('battle_data: unsupported preprocessor line: ' + s)
            continue
        if all(on for on, _ in stack):
            kept.append(line)
    body = '\n'.join(kept)
    body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
    body = re.sub(r'//[^\n]*', '', body)
    syms = dict(K)
    syms.update({k.upper(): v for k, v in ctx['consts'].items() if k.upper() not in syms})

    def ev(expr):
        expr = expr.strip()
        e = re.sub(r'[A-Za-z_][A-Za-z_0-9]*', lambda m: str(syms[m.group(0)]) if m.group(0) in syms else m.group(0), expr)
        if not re.fullmatch(r'[0-9xXa-fA-F\s|&~+\-*/()<>]*', e):
            raise SystemExit('battle_data: cannot evaluate %r' % expr)
        e = re.sub(r'(?<![0-9a-fA-FxX])0([0-7]+)\b', r'0o\1', e)
        return int(eval(e.replace('/', '//')))
    table, names = {}, {}
    for m in re.finditer(r'\{\s*"([^"]+)"\s*,\s*&battle_config\.(\w+)\s*,([^,]+),([^,]+),([^,}]+),?\s*\}', body):
        name, member = m.group(1), m.group(2)
        table[lower(name)] = (name, member, ev(m.group(3)), ev(m.group(4)), ev(m.group(5)))
        names[name] = member
    conf = {member: d for (_, member, d, _, _) in table.values()}  # battle_set_defaults

    def read(rel):
        path = ctx['resolve'](rel)
        if not os.path.exists(path):
            warn('File not found: %s' % rel)
            return
        for raw in open(path, encoding='latin-1'):
            line = raw[:1023]
            if line.startswith('//'):
                continue
            m = re.match(r'([^:]+):\s*(\S+)', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2)
            if lower(w1) == 'import':
                read(w2)
            elif lower(w1) in ('atcommand_symbol', 'charcommand_symbol'):
                pass
            elif lower(w1) in table:
                _, member, d, lo, hi = table[lower(w1)]
                v = config_switch(w2)
                if v < lo or v > hi:
                    warn('Value for setting \'%s\': %s is invalid (min:%d max:%d)! Defaulting to %d...' % (w1, w2, lo, hi, d))
                    v = d
                conf[member] = v
            elif not w1.startswith('loki_'):  # the Loki patch adds loki_sit_regen_rate to the server it runs on
                warn('Unknown setting \'%s\' in file %s' % (w1, rel))
    read('conf/battle_athena.conf')
    # battle_adjust_conf
    conf['max_aspd'] = (K['AMOTION_ZERO_ASPD'] - conf['max_aspd'] * K['AMOTION_INTERVAL']) * K['AMOTION_DIVIDER_PC']
    conf['max_third_aspd'] = (K['AMOTION_ZERO_ASPD'] - conf['max_third_aspd'] * K['AMOTION_INTERVAL']) * K['AMOTION_DIVIDER_PC']
    conf['max_summoner_aspd'] = (K['AMOTION_ZERO_ASPD'] - conf['max_summoner_aspd'] * K['AMOTION_INTERVAL']) * K['AMOTION_DIVIDER_PC']
    conf['max_extended_aspd'] = (K['AMOTION_ZERO_ASPD'] - conf['max_extended_aspd'] * K['AMOTION_INTERVAL']) * K['AMOTION_DIVIDER_PC']
    conf['max_walk_speed'] = cdiv(100 * K['DEFAULT_WALK_SPEED'], conf['max_walk_speed'])
    conf['max_cart_weight'] *= 10
    if conf['max_def'] > 100 and not conf['weapon_defense_type']:
        conf['max_def'] = 100
    if conf['min_hitrate'] > conf['max_hitrate']:
        conf['min_hitrate'] = conf['max_hitrate']
    if conf['pet_max_atk1'] > conf['pet_max_atk2']:
        conf['pet_max_atk1'] = conf['pet_max_atk2']
    if conf['day_duration'] and conf['day_duration'] < 60000:
        conf['day_duration'] = 60000
    if conf['night_duration'] and conf['night_duration'] < 60000:
        conf['night_duration'] = 60000
    conf['feature_mesitemicon'] = 0  # PACKETVER < 20230302
    conf['__names__'] = names
    return conf


# --------------------------------------------------------------------------------------- maps: index and loaded maps
def mapindex_getmapname(s):
    """common/mapindex.cpp mapindex_getmapname: at most 15 chars read, ".gat" dropped, 11 kept."""
    s = s[:15]
    if len(s) >= 4 and lower(s[-4:]) == '.gat':
        s = s[:-4]
    return c_str(s, 11)


def read_mapindex(ctx):
    """mapindex_init: db/map_index.txt then db/import/map_index.txt (`%11s\\t%d`, auto index = last + 1)."""
    names, by_name = {}, {}
    for rel in ('db/map_index.txt', 'db/import/map_index.txt'):
        path = ctx['resolve'](rel)
        if not os.path.exists(path):
            continue
        last = -1
        for line in open(path, encoding='latin-1'):
            if line.startswith('//'):
                continue
            m = re.match(r'\s*(\S{1,11})(?:\s+([+-]?\d+))?', line)
            if not m:
                continue
            index = int(m.group(2)) if m.group(2) is not None else last + 1
            name = mapindex_getmapname(m.group(1))
            if 0 <= index < 1500 and name:
                if index in names:
                    by_name.pop(names[index], None)
                names[index] = name
                by_name[name] = index
            last = index
    return by_name


def read_loaded_maps(ctx):
    """The maps this map server loaded (data/maps.bin, written by datagen from conf/maps_athena.conf and the caches):
    name -> (map id, xs, ys)."""
    b = open(os.path.join(ctx['outdir'], 'maps.bin'), 'rb').read()
    assert b[:4] == b'LKMP'
    count = struct.unpack_from('<I', b, 8)[0]
    p, out = 12, {}
    for i in range(count):
        n = b[p]
        name = b[p + 1:p + 1 + n].decode('latin-1')
        p += 1 + n
        xs, ys, ln = struct.unpack_from('<HHI', b, p)
        p += 8 + ln
        out[name] = (i, xs, ys)
    return out


class Maps:
    def __init__(self, ctx):
        self.index = read_mapindex(ctx)
        self.loaded = read_loaded_maps(ctx)

    def name2idx(self, name):
        """mapindex_name2idx (strdb: case-sensitive); 0 when unknown."""
        return self.index.get(mapindex_getmapname(name), 0)

    def idx2name(self, idx):
        for n, i in self.index.items():
            if i == idx:
                return n
        return ''

    def mapindex2mapid(self, idx):
        """map_mapindex2mapid: -1 when the map is not loaded here."""
        if not idx:
            return -1
        n = self.idx2name(idx)
        return self.loaded[n][0] if n in self.loaded else -1

    def mapname2mapid(self, name):
        return self.mapindex2mapid(self.name2idx(name))

    def size(self, mapid):
        for n, (i, xs, ys) in self.loaded.items():
            if i == mapid:
                return xs, ys
        return None

    def mapid2name(self, mapid):
        for n, (i, xs, ys) in self.loaded.items():
            if i == mapid:
                return n
        return ''


# ------------------------------------------------------------------------ what the loaders read from other databases
class ItemDb(YamlDb):
    """itemdb.cpp ItemDatabase, the parts other loaders consult: AegisName (search_aegisname, lower-cased, duplicate
    check), Type (IT_DELAYCONSUME becomes IT_USABLE in loadingFinished), View (look), the dummy item. The full item
    database is the item family's; the per-file counts are checked against the oracle."""
    TYPE, VERSION, MIN_VERSION = 'ITEM_DB', 3, 1

    def __init__(self, ctx):
        super().__init__(ctx)
        self.aegis = {}

    def default_location(self):
        return 'db/item_db.yml'

    def parse_body_node(self, node):
        nameid = self.as_u32(node, 'Id')
        item = self.find(nameid)
        exists = item is not None
        if not exists:
            if not self.nodes_exist(node, ['AegisName', 'Name']):
                return 0
            item = {'nameid': nameid, 'name': '', 'type': K['IT_ETC'], 'look': 0}
        if self.exists(node, 'AegisName'):
            name = self.as_str(node, 'AegisName')
            other = self.aegis.get(lower(name))
            if other is not None and other['nameid'] != nameid:
                self.invalid('Found duplicate item Aegis name for %s, skipping.' % name)
                return 0
            if exists:
                self.aegis.pop(lower(item['name']), None)
            item['name'] = c_str(name)
            self.aegis[lower(name)] = item
        if self.exists(node, 'Type'):
            c = self.const('IT_' + self.as_str(node, 'Type'))
            if c is None or c < K['IT_HEALING'] or c >= K['IT_MAX']:
                c = K['IT_ETC']
            item['type'] = c
        if self.exists(node, 'View'):
            item['look'] = self.as_u32(node, 'View')
        if not exists:
            self.put(nameid, item)
        return 1

    def loading_finished(self):
        for item in self.data.values():
            if item['type'] == K['IT_DELAYCONSUME']:
                item['type'] = K['IT_USABLE']
        if K['ITEMID_DUMMY'] not in self.data:
            self.put(K['ITEMID_DUMMY'], {'nameid': K['ITEMID_DUMMY'], 'name': 'UNKNOWN_ITEM', 'type': K['IT_ETC'], 'look': 0})

    def search_aegisname(self, name):
        return self.aegis.get(lower(name))

    def search(self, nameid):
        """itemdb_search: the dummy item for unknown ids."""
        return self.find(nameid) or self.find(K['ITEMID_DUMMY'])

    @staticmethod
    def isstackable(item):
        return item['type'] not in (K['IT_WEAPON'], K['IT_ARMOR'], K['IT_PETEGG'], K['IT_PETARMOR'], K['IT_SHADOWGEAR'])


class RandomOptionGroupDb(YamlDb):
    """itemdb.cpp RandomOptionGroupDatabase: Id and Group name (option_get_id); count checked against the oracle."""
    TYPE, VERSION = 'RANDOM_OPTION_GROUP', 1

    def default_location(self):
        return 'db/item_randomopt_group.yml'

    def parse_body_node(self, node):
        gid = self.as_u16(node, 'Id')
        g = self.find(gid)
        exists = g is not None
        if not exists:
            if not self.nodes_exist(node, ['Group']):
                return 0
            g = {'id': gid, 'name': ''}
        if self.exists(node, 'Group'):
            name = self.as_str(node, 'Group')
            if g['name'] != name and any(o['name'] == name for o in self.data.values()):
                self.invalid('Found duplicate random option group name for %s, skipping.' % name)
                return 0
            g['name'] = name
        if not exists:
            self.put(gid, g)
        return 1

    def option_get_id(self, name):
        for k, g in self.data.items():
            if g['name'] == name:
                return k
        return None


class SkillDb(YamlDb):
    """skill.cpp SkillDatabase, the parts other loaders consult: Id, Name (skill_name2id, strcmpi, first match in
    iteration order), MaxLevel (skill_get_max), TargetType (skill_get_casttype == CAST_GROUND). Count checked."""
    TYPE, VERSION, MIN_VERSION = 'SKILL_DB', 4, 1

    def default_location(self):
        return 'db/skill_db.yml'

    def parse_body_node(self, node):
        sid = self.as_u16(node, 'Id')
        sk = self.find(sid)
        exists = sk is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Description', 'MaxLevel']):
                return 0
            sk = {'id': sid, 'name': '', 'max': 0, 'inf': 0}
        if self.exists(node, 'Name'):
            name = self.as_str(node, 'Name')
            if len(name) > K['SKILL_NAME_LENGTH']:
                return 0
            sk['name'] = c_str(name, K['SKILL_NAME_LENGTH'])
        if self.exists(node, 'MaxLevel'):
            lv = self.as_u16(node, 'MaxLevel')
            if lv == 0 or lv > K['MAX_SKILL_LEVEL']:
                return 0
            sk['max'] = lv
        if self.exists(node, 'TargetType'):
            c = self.const('INF_' + self.as_str(node, 'TargetType') + '_SKILL')
            if c is None:
                return 0
            sk['inf'] = u16(c)
        if not exists:
            self.put(sid, sk)
        return 1

    def name2id(self, name):
        for k, sk in self.data.items():
            if lower(sk['name']) == lower(name):
                return k
        return 0

    def get_max(self, sid):
        sk = self.find(sid)
        return sk['max'] if sk else 0

    def is_ground(self, sid):
        sk = self.find(sid)
        return bool(sk and sk['inf'] & K['INF_GROUND_SKILL'])


# ---------------------------------------------------------------------------------------------------- status_data
def new_status():
    """struct status_data zero-initialised (status.hpp), RENEWAL layout; rhw/lhw are struct weapon_atk."""
    w = lambda: {'atk': 0, 'atk2': 0, 'range': 0, 'ele': 0, 'matk': 0, 'wlv': 0}
    s = {k: 0 for k in ('hp', 'sp', 'ap', 'max_hp', 'max_sp', 'max_ap', 'str', 'agi', 'vit', 'int_', 'dex', 'luk',
                        'pow', 'sta', 'wis', 'spl', 'con', 'crt', 'eatk', 'batk', 'watk', 'watk2', 'matk_min',
                        'matk_max', 'speed', 'amotion', 'clientamotion', 'adelay', 'dmotion', 'mode', 'hit', 'flee',
                        'cri', 'flee2', 'def2', 'mdef2', 'aspd_rate2', 'aspd_rate', 'patk', 'smatk', 'res', 'mres',
                        'hplus', 'crate', 'def', 'mdef', 'def_ele', 'ele_lv', 'size', 'race', 'class_')}
    s['rhw'], s['lhw'] = w(), w()
    return s


def status_calc_misc(bl_type, st, level, conf):
    """status.cpp status_calc_misc (RENEWAL) for a non-player unit other than a homunculus (BL_MOB / BL_MER / BL_PET /
    BL_ELEM), with status_base_atk / status_base_atk_min/max / status_base_matk_min/max."""
    BL_PC, BL_MOB, BL_MER = K['BL_PC'], K['BL_MOB'], K['BL_MER']
    SHRT_MAX = K['SHRT_MAX']
    for f in ('batk', 'hit', 'flee', 'def2', 'mdef2', 'cri', 'flee2', 'patk', 'smatk', 'hplus', 'crate'):
        st[f] = 0
    if bl_type != BL_MOB:
        st['res'] = st['mres'] = 0
    # Hit / Flee
    st['hit'] = i16(cap_value(st['hit'] + level + st['dex'] + 150 + 2 * st['con'], 1, SHRT_MAX))
    st['flee'] = i16(cap_value(st['flee'] + level + st['agi'] + 0 + 100 + 2 * st['con'], 1, SHRT_MAX))
    # Def2 / Mdef2 (float arithmetic, truncated)
    if bl_type == BL_MER:
        stat = int(st['vit'] + (level / 10) + (st['vit'] / 5))
    else:
        stat = st['def2'] + int((level + st['vit']) / 2 + 0)
    st['def2'] = i16(cap_value(stat, 0, SHRT_MAX))
    if bl_type == BL_MER:
        stat = int((level / 10) + (st['int_'] / 5))
    else:
        stat = st['mdef2'] + int((st['int_'] + level) / 4)
    st['mdef2'] = i16(cap_value(stat, 0, SHRT_MAX))
    st['patk'] = i16(cap_value(st['patk'] + cdiv(st['pow'], 3) + cdiv(st['con'], 5), 0, SHRT_MAX))
    st['smatk'] = i16(cap_value(st['smatk'] + cdiv(st['spl'], 3) + cdiv(st['con'], 5), 0, SHRT_MAX))
    st['res'] = i16(cap_value(st['res'] + st['sta'] + cdiv(st['sta'], 3) * 5, 0, SHRT_MAX))
    st['mres'] = i16(cap_value(st['mres'] + st['wis'] + cdiv(st['wis'], 3) * 5, 0, SHRT_MAX))
    st['hplus'] = i16(cap_value(st['hplus'] + st['crt'], 0, SHRT_MAX))
    st['crate'] = i16(cap_value(st['crate'] + cdiv(st['crt'], 3), 0, SHRT_MAX))
    # ATK: status_base_atk_max then status_base_atk_min (both from the db ATK, returned as uint16)
    r = st['rhw']
    atk = r['atk']
    r['atk2'] = u16(cdiv(atk * 120, 100))
    r['atk'] = u16(cdiv(atk * 80, 100))
    # MATK
    st['matk_min'] = u16(st['int_'] + level + cdiv(r['matk'] * 70, 100))
    st['matk_max'] = u16(st['int_'] + level + cdiv(r['matk'] * 130, 100))
    # Critical / perfect dodge
    if bl_type & conf['enable_critical']:
        st['cri'] = i16(cap_value(st['cri'] + cdiv(level, 10) + 10 + st['luk'] * 3, 1, SHRT_MAX))
    else:
        st['cri'] = 0
    if bl_type & conf['enable_perfect_flee']:
        st['flee2'] = i16(cap_value(st['flee2'] + st['luk'] + 10, 0, SHRT_MAX))
    else:
        st['flee2'] = 0
    # status_base_atk (RENEWAL, non-player, non-homunculus): str = dstr + level
    if bl_type & conf['enable_baseatk_renewal']:
        base = cap_value(st['str'] + level, 0, K['USHRT_MAX'])
    else:
        base = 0
    st['batk'] = i32(st['batk'] + base)
    if st['cri']:
        if bl_type == BL_MOB:
            if conf['mob_critical_rate'] != 100:
                st['cri'] = i16(cap_value(cdiv(st['cri'] * conf['mob_critical_rate'], 100), 1, SHRT_MAX))
            if not st['cri'] and conf['mob_critical_rate']:
                st['cri'] = 10
        elif bl_type != BL_PC:
            if conf['critical_rate'] != 100:
                st['cri'] = i16(cap_value(cdiv(st['cri'] * conf['critical_rate'], 100), 1, SHRT_MAX))
            if not st['cri'] and conf['critical_rate']:
                st['cri'] = 10
    # bl->type & BL_REGEN: status_calc_regen is for PC/HOM/MER/ELEM units at spawn (their regen data), not stored here


# ------------------------------------------------------------------------------------------------------ mob_db.yml
def new_mob(mob_id):
    """s_mob_db::s_mob_db (mob.cpp)."""
    st = new_status()
    st.update(max_hp=1, max_sp=1, str=1, agi=1, vit=1, int_=1, dex=1, luk=1, ele_lv=1, speed=K['DEFAULT_WALK_SPEED'],
              adelay=K['MAX_ASPD_NOPC'], amotion=K['MAX_ASPD_NOPC'] // K['AMOTION_DIVIDER_NOPC'],
              mode=K['MONSTER_TYPE_06'])
    st['clientamotion'] = cap_value(st['amotion'], 1, K['USHRT_MAX'])
    return {'id': mob_id, 'sprite': '', 'name': '', 'jname': '', 'base_exp': 0, 'job_exp': 0, 'mexp': 0,
            'base_exp_raw': 0, 'job_exp_raw': 0, 'mexp_raw': 0, 'range2': 0,
            'range3': 0, 'race2': [], 'lv': 1, 'dropitem': [], 'mvpitem': [], 'dropitem_raw': [], 'mvpitem_raw': [],
            'status': st,
            'vd': {'look': [0] * K['LOOK_MAX'], 'sex': 0, 'dead_sit': 0}, 'option': 0, 'skill': [],
            'damagetaken': 100, 'group_id': 0, 'title': ''}


def get_bosstype(status):
    """s_mob_db::get_bosstype: 2 MVP, 1 mini boss (CLASS_BOSS), 0 none."""
    if status['mode'] & K['MD_MVP'] == K['MD_MVP']:
        return 2
    return 1 if status['class_'] == K['CLASS_BOSS'] else 0


class MobDb(YamlDb):
    """mob.cpp MobDatabase (TypesafeCachedYamlDatabase<uint32, s_mob_db>)."""
    TYPE, VERSION, MIN_VERSION = 'MOB_DB', 5, 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/mob_db.yml'

    def parse_drop_node(self, node_name, node, maximum, drops):
        """MobDatabase::parseDropNode."""
        for dropit in node[node_name] or []:
            if self.exists(dropit, 'Index'):
                index = self.as_u16(dropit, 'Index')
                if index >= maximum:
                    self.invalid('Invalid monster %s index %d. Must be between 0~%d, skipping.' % (node_name, index, maximum - 1))
                    continue
                if index == len(drops):
                    drop, exist = {}, False
                elif index > len(drops):
                    continue
                else:
                    drop, exist = drops[index], True
            else:
                if len(drops) >= maximum:
                    self.invalid('Maximum of %d monster %s met, skipping.' % (maximum, node_name))
                    continue
                drop, exist = {}, False
            item_name = self.as_str(dropit, 'Item')
            item = self.w['items'].search_aegisname(item_name)
            if item is None:
                self.invalid('Monster %s item %s does not exist, skipping.' % (node_name, item_name))
                continue
            rate = self.as_u16_rate(dropit, 'Rate')
            steal = self.as_bool(dropit, 'StealProtected') if self.exists(dropit, 'StealProtected') else False
            group = 0
            if self.exists(dropit, 'RandomOptionGroup'):
                gname = self.as_str(dropit, 'RandomOptionGroup')
                g = self.w['randomopt'].option_get_id(gname)
                if g is None:
                    self.invalid('Unknown random option group %s for monster %s, defaulting to no group.' % (gname, node_name))
                else:
                    group = g
            drop.update(nameid=item['nameid'], rate=rate, steal_protected=steal, randomopt_group=group)
            if not exist:
                drops.append(drop)

    def parse_body_node(self, node):
        conf = self.w['conf']
        mob_id = self.as_u32(node, 'Id')
        if not ((MIN_MOB_DB < mob_id < MAX_MOB_DB) or (MIN_MOB_DB2 < mob_id < MAX_MOB_DB2)):
            self.invalid('Invalid monster ID %d.' % mob_id)
            return 0
        mob = self.find(mob_id)
        exists = mob is not None
        if not exists:
            if not self.nodes_exist(node, ['AegisName', 'Name']):
                return 0
            mob = new_mob(mob_id)
            mob['vd']['look'][0] = mob_id  # vd.look[LOOK_BASE]
        st = mob['status']
        for key, field in (('AegisName', 'sprite'), ('Name', 'name'), ('JapaneseName', 'jname')):
            if self.exists(node, key):
                mob[field] = c_str(self.as_str(node, key), K['NAME_LENGTH'])
            elif key == 'JapaneseName' and not exists:
                mob['jname'] = mob['name']
        if self.exists(node, 'Level'):
            mob['lv'] = self.as_u16(node, 'Level')
        if self.exists(node, 'Hp'):
            st['max_hp'] = self.as_u32(node, 'Hp')
        if self.exists(node, 'Sp'):
            st['max_sp'] = self.as_u32(node, 'Sp')
        for key, field, rate in (('BaseExp', 'base_exp', 'base_exp_rate'), ('JobExp', 'job_exp', 'job_exp_rate'),
                                 ('MvpExp', 'mexp', 'mvp_exp_rate')):
            if self.exists(node, key):
                exp = self.as_u64(node, key)
                v = float(exp) * float(conf[rate]) / 100.
                mob[field] = int(cap_value(v, 0.0, float(K['MAX_EXP'])))  # static_cast<t_exp>(cap_value(double...))
                # the node's own value: the module applies base_exp_rate / job_exp_rate / mvp_exp_rate again when a
                # mob_reload finds other rates than the ones baked here (src/mob/rtdb.rs)
                mob[field + '_raw'] = exp
        if self.exists(node, 'Attack'):
            st['rhw']['atk'] = self.as_u16(node, 'Attack')
        if self.exists(node, 'Attack2'):
            st['rhw']['matk'] = self.as_u16(node, 'Attack2')  # RENEWAL: Attack2 is MATK
        for key, field in (('Defense', 'def'), ('MagicDefense', 'mdef')):
            if self.exists(node, key):
                d = self.as_u16(node, key)
                if d < K['DEFTYPE_MIN'] or d > K['DEFTYPE_MAX']:
                    self.invalid('Invalid monster %s %d, capping...' % (key, d))
                    d = cap_value(d, K['DEFTYPE_MIN'], K['DEFTYPE_MAX'])
                st[field] = i16(d)
        for key, field in (('Resistance', 'res'), ('MagicResistance', 'mres')):
            if self.exists(node, key):
                st[field] = i16(self.as_u16(node, key))
        for key, field in (('Str', 'str'), ('Agi', 'agi'), ('Vit', 'vit'), ('Int', 'int_'), ('Dex', 'dex'), ('Luk', 'luk')):
            if self.exists(node, key):
                st[field] = i16(max(0, self.as_u16(node, key)))
        if self.exists(node, 'AttackRange'):
            st['rhw']['range'] = self.as_u16(node, 'AttackRange')
        if self.exists(node, 'SkillRange'):
            mob['range2'] = self.as_u16(node, 'SkillRange')
        if self.exists(node, 'ChaseRange'):
            mob['range3'] = self.as_u16(node, 'ChaseRange')
        if self.exists(node, 'Size'):
            size = self.as_str(node, 'Size')
            c = self.const('Size_' + size)
            if c is None:
                self.invalid('Unknown monster size %s, defaulting to Small.' % size)
                c = K['SZ_SMALL']
            if c < K['SZ_SMALL'] or c > K['SZ_BIG']:
                self.invalid('Invalid monster size %s, defaulting to Small.' % size)
                c = K['SZ_SMALL']
            st['size'] = c
        if self.exists(node, 'Race'):
            race = self.as_str(node, 'Race')
            c = self.const('RC_' + race)
            if c is None:
                self.invalid('Unknown monster race %s, defaulting to Formless.' % race)
                c = K['RC_FORMLESS']
            if not (-1 < c < K['RC_MAX']):
                self.invalid('Invalid monster race %s, defaulting to Formless.' % race)
                c = K['RC_FORMLESS']
            st['race'] = c
        if self.exists(node, 'RaceGroups'):
            race_node = node['RaceGroups'] or {}
            for name in list(race_node):
                c = self.const('RC2_' + name)
                if c is None:
                    self.invalid('Unknown monster race group %s, skipping.' % name)
                    continue
                if c <= K['RC2_NONE'] or c >= K['RC2_MAX']:
                    self.invalid('Invalid monster race group %s, skipping.' % name)
                    continue
                if self.as_bool(race_node, name):
                    mob['race2'].append(c)
                elif c in mob['race2']:
                    mob['race2'].remove(c)
        if self.exists(node, 'Element'):
            ele = self.as_str(node, 'Element')
            c = self.const('ELE_' + ele)
            if c is None:
                self.invalid('Unknown monster element %s, defaulting to Neutral.' % ele)
                c = K['ELE_NEUTRAL']
            if not (-1 < c < K['ELE_MAX']):
                self.invalid('Invalid monster element %s, defaulting to Neutral.' % ele)
                c = K['ELE_NEUTRAL']
            st['def_ele'] = c
        if self.exists(node, 'ElementLevel'):
            lv = self.as_u16(node, 'ElementLevel')
            if not (1 <= lv <= K['MAX_ELE_LEVEL']):
                self.invalid('Invalid monster element level %d, defaulting to 1.' % lv)
                lv = 1
            st['ele_lv'] = u8(lv)
        if self.exists(node, 'WalkSpeed'):
            sp = self.as_u16(node, 'WalkSpeed')
            if sp < K['MIN_WALK_SPEED'] or sp > K['MAX_WALK_SPEED']:
                self.invalid('Invalid monster walk speed %d, capping...' % sp)
                sp = cap_value(sp, K['MIN_WALK_SPEED'], K['MAX_WALK_SPEED'])
            st['speed'] = sp
        if self.exists(node, 'AttackDelay'):
            st['adelay'] = cap_value(self.as_u16(node, 'AttackDelay'), K['MAX_ASPD_NOPC'], K['MIN_ASPD'])
        if self.exists(node, 'AttackMotion'):
            st['amotion'] = cap_value(self.as_u16(node, 'AttackMotion'), 1, K['MIN_ASPD'] // K['AMOTION_DIVIDER_NOPC'])
        if self.exists(node, 'ClientAttackMotion'):
            st['clientamotion'] = cap_value(self.as_u16(node, 'ClientAttackMotion'), 1, K['USHRT_MAX'])
        elif not exists:
            st['clientamotion'] = cap_value(st['amotion'], 1, K['USHRT_MAX'])
        if self.exists(node, 'DamageMotion'):
            sp = self.as_u16(node, 'DamageMotion')
            if conf['monster_damage_delay_rate'] != 100:
                sp = u16(cdiv(i32(sp * conf['monster_damage_delay_rate']), 100))
            st['dmotion'] = sp
        if self.exists(node, 'DamageTaken'):
            mob['damagetaken'] = self.as_u16_rate(node, 'DamageTaken', 100)
        if self.exists(node, 'GroupId'):
            mob['group_id'] = self.as_u16_rate(node, 'GroupId')
        if self.exists(node, 'Title'):
            mob['title'] = c_str(self.as_str(node, 'Title'), K['NAME_LENGTH'])
        if self.exists(node, 'Ai'):
            ai = self.as_str(node, 'Ai')
            c = self.const('MONSTER_TYPE_' + ai)
            if c is None:
                self.invalid('Unknown monster AI %s, defaulting to 06.' % ai)
                c = K['MONSTER_TYPE_06']
            if c < K['MD_NONE'] or c > K['MD_MASK']:
                self.invalid('Invalid monster AI %s, defaulting to 06.' % ai)
                c = K['MONSTER_TYPE_06']
            st['mode'] = c
        if self.exists(node, 'Class'):
            cl = self.as_str(node, 'Class')
            c = self.const('CLASS_' + cl)
            if c is None:
                self.invalid('Unknown monster class %s, defaulting to Normal.' % cl)
                c = K['CLASS_NORMAL']
            if c < K['CLASS_NORMAL'] or c > K['CLASS_EVENT']:
                self.invalid('Invalid monster class %s, defaulting to Normal.' % cl)
                c = K['CLASS_NORMAL']
            st['class_'] = u8(c)
        if self.exists(node, 'Modes'):
            mode_node = node['Modes'] or {}
            for name in list(mode_node):
                c = self.const('MD_' + name)
                if c is None:
                    self.invalid('Unknown monster mode %s, skipping.' % name)
                    continue
                if c < K['MD_NONE'] or c > K['MD_SKILLIMMUNE']:
                    self.invalid('Invalid monster mode %s, skipping.' % name)
                    continue
                if self.as_bool(mode_node, name):
                    st['mode'] = i32(st['mode'] | c)
                else:
                    st['mode'] = i32(st['mode'] & ~c)
        if self.exists(node, 'MvpDrops'):
            self.parse_drop_node('MvpDrops', node, K['MAX_MVP_DROP'], mob['mvpitem'])
        if self.exists(node, 'Drops'):
            self.parse_drop_node('Drops', node, K['MAX_MOB_DROP'], mob['dropitem'])
        if not exists:
            self.put(mob_id, mob)
        return 1

    def loading_finished(self):
        """MobDatabase::loadingFinished."""
        conf = self.w['conf']
        for mob in self.data.values():
            st = mob['status']
            c = st['class_']
            if c == K['CLASS_BOSS']:
                st['mode'] |= K['MD_DETECTOR'] | K['MD_STATUSIMMUNE'] | K['MD_KNOCKBACKIMMUNE']
            elif c == K['CLASS_GUARDIAN']:
                st['mode'] |= K['MD_STATUSIMMUNE']
            elif c == K['CLASS_BATTLEFIELD']:
                st['mode'] |= K['MD_STATUSIMMUNE'] | K['MD_SKILLIMMUNE']
            elif c == K['CLASS_EVENT']:
                st['mode'] |= K['MD_FIXEDITEMDROP']
            st['mode'] = i32(st['mode'])
            if conf['view_range_rate'] != 100:
                mob['range2'] = u16(max(1, cdiv(mob['range2'] * conf['view_range_rate'], 100)))
            if conf['chase_range_rate'] != 100:
                mob['range3'] = u16(max(mob['range2'], cdiv(mob['range3'] * conf['chase_range_rate'], 100)))
            st['adelay'] = max(st['adelay'], st['amotion'])
            st['aspd_rate'] = 1000
            if not conf['monster_active_enable']:
                st['mode'] = i32(st['mode'] & ~K['MD_AGGRESSIVE'])
            status_calc_misc(K['BL_MOB'], st, mob['lv'], conf)
            maxhp = float(st['max_hp'])
            if get_bosstype(st) == 2:
                if conf['mvp_hp_rate'] != 100:
                    maxhp = maxhp * float(conf['mvp_hp_rate']) / 100.
            elif conf['monster_hp_rate'] != 100:
                maxhp = maxhp * float(conf['monster_hp_rate']) / 100.
            st['max_hp'] = cap_value(u32(int(maxhp)), 1, K['UINT32_MAX'])
            st['max_sp'] = cap_value(st['max_sp'], 1, K['UINT32_MAX'])
            st['hp'], st['sp'] = st['max_hp'], st['max_sp']

    def search_aegisname(self, name):
        """mobdb_search_aegisname: strcmpi against every sprite, first match in iteration order."""
        low = lower(name)
        for mob in self.data.values():
            if lower(mob['sprite']) == low:
                return mob
        return None


class MobChatDb(YamlDb):
    """mob.cpp MobChatDatabase (uint16 -> s_mob_chat)."""
    TYPE, VERSION = 'MOB_CHAT_DB', 1

    def default_location(self):
        return 'db/mob_chat_db.yml'

    def parse_body_node(self, node):
        cid = self.as_u16(node, 'Id')
        chat = self.find(cid)
        exists = chat is not None
        if not exists:
            if not self.nodes_exist(node, ['Dialog']):
                return 0
            chat = {'msg_id': cid, 'color': 0, 'msg': ''}
        if self.exists(node, 'Color'):
            chat['color'] = strtoul32(self.as_str(node, 'Color'), 0)
        elif not exists:
            chat['color'] = strtoul32('0xFF0000', 0)
        if self.exists(node, 'Dialog'):
            msg = self.as_str(node, 'Dialog')
            if len(msg) > K['CHAT_SIZE_MAX'] - 1:
                self.invalid('Message too long!')
                return 0
            chat['msg'] = msg
        if not exists:
            self.put(cid, chat)
        return 1


# mob_parse_row_mobskilldb tables (mob.cpp; enum MobSkillState, e_mob_skill_condition, e_mob_skill_target in mob.hpp)
MSS = {'any': -1, 'idle': 0, 'walk': 1, 'loot': 2, 'dead': 3, 'attack': 4, 'angry': 5, 'chase': 6, 'follow': 7,
       'anytarget': 8}
MSC = ['always', 'myhpltmaxrate', 'myhpinrate', 'friendhpltmaxrate', 'friendhpinrate', 'mystatuson', 'mystatusoff',
       'friendstatuson', 'friendstatusoff', 'attackpcgt', 'attackpcge', 'slavelt', 'slavele', 'closedattacked',
       'longrangeattacked', 'afterskill', 'skillused', 'casttargeted', 'rudeattacked', 'masterhpltmaxrate',
       'masterattacked', 'alchemist', 'onspawn', 'mobnearbygt', 'groundattacked', 'damagedgt', 'trickcasting']
# the parser's own spelling -> enum value (skillused precedes afterskill in the table but not in the enum)
MSC_BY_NAME = {n: i for i, n in enumerate(MSC)}
MST = {'target': 0, 'randomtarget': 1, 'self': 2, 'friend': 3, 'master': 4, 'around5': 5, 'around6': 6, 'around7': 7,
       'around8': 8, 'around1': 9, 'around2': 10, 'around3': 11, 'around4': 12, 'around': 12}
MST_MASTER, MST_AROUND = 4, 12
COND2_NAMES = ['anybad', 'stone', 'freeze', 'stun', 'sleep', 'poison', 'curse', 'silence', 'confusion', 'blind',
               'hiding', 'sight']


def sv_readdb_rows(ctx, rel, delim, mincols, maxcols):
    """common/strlib.cpp sv_readdb + sv_split (no trimming, no escapes; `//` cuts the line; a line starting with NUL,
    LF or CR is skipped; lines with a column count outside mincols..maxcols are reported and skipped). Yields
    (line number, fields)."""
    path = ctx['resolve'](rel)
    if not os.path.exists(path):
        return
    data = open(path, 'rb').read().decode('latin-1')
    lines = data.split('\n')
    if lines and lines[-1] == '':
        lines.pop()
    for n, line in enumerate(lines, 1):
        i = line.find('//')
        if i >= 0:
            line = line[:i]
        if line == '' or line[0] in '\0\r':
            continue
        if line.endswith('\r'):
            line = line[:-1]
        fields = line.split(delim)
        if len(fields) < mincols:
            warn('sv_readdb: Insufficient columns in line %d of "%s" (found %d, need at least %d).' % (n, rel, len(fields), mincols))
            continue
        if len(fields) > maxcols:
            warn('sv_readdb: Too many columns in line %d of "%s" (found %d, maximum is %d).' % (n, rel, len(fields), maxcols))
            continue
        yield n, fields


def read_mob_skill_db(ctx, world):
    """mob.cpp mob_readskilldb (db/re/mob_skill_db.txt then db/import/mob_skill_db.txt, 19 columns) with
    mob_parse_row_mobskilldb; returns the temporary mob_skill_db (UMap mob_id -> {'mob_id', 'skill': [...]}) and the
    per-file counts."""
    conf, mob_db, chat_db, skill_db = world['conf'], world['mob_db'], world['mob_chat'], world['skills']
    mob_skill_db, counts = UMap(), []
    if conf['mob_skill_rate'] == 0:
        return mob_skill_db, counts
    for rel in ('db/re/mob_skill_db.txt', 'db/import/mob_skill_db.txt'):
        if not os.path.exists(ctx['resolve'](rel)):
            continue
        entries = 0
        for line, s in sv_readdb_rows(ctx, rel, ',', 19, 19):
            mob_id = atoi(s[0])
            mob = mob_db.find(mob_id)
            if mob_id > 0 and mob is None:
                warn('mob_parse_row_mobskilldb: Non existant Mob id %d' % mob_id)
                continue
            elif mob_id == 0:
                continue
            skill = mob_skill_db.get(mob_id) or {'mob_id': 0, 'skill': []}
            if s[1] == 'clear' and skill['mob_id'] != 0:
                mob_skill_db.pop(skill['mob_id'])
                entries += 1
                continue
            if len(skill['skill']) >= K['MAX_MOBSKILL']:
                warn('mob_parse_row_mobskilldb: Too many skills for monster %d' % mob_id)
                continue
            if s[2] not in MSS:
                warn('mob_parse_row_mobskilldb: Unrecognized state \'%s\' in line %d' % (s[2], entries))
                continue
            ms = {'state': MSS[s[2]]}
            j = atoi(s[3])
            if u16(j) not in skill_db.data:
                warn('mob_parse_row_mobskilldb: Invalid Skill ID (%d) for mob %d' % (j, mob_id))
                continue
            ms['skill_id'] = u16(j)
            j = 1 if atoi(s[4]) <= 0 else atoi(s[4])
            ms['skill_lv'] = u16(conf['mob_max_skilllvl'] if j > conf['mob_max_skilllvl'] else j)
            tmp = atoi(s[5])
            if conf['mob_skill_rate'] != 100:
                tmp = i32(cdiv(i32(tmp * conf['mob_skill_rate']), 100))
            if tmp > 10000:
                ms['permillage'] = 10000
            elif not tmp and conf['mob_skill_rate']:
                ms['permillage'] = 1
            else:
                ms['permillage'] = i16(tmp)
            ms['casttime'] = atoi(s[6])
            delay = atoi(s[7])
            if conf['mob_skill_delay'] != 100:
                delay = i32(cdiv(i32(delay * conf['mob_skill_delay']), 100))
            if delay < 0 or delay > MOB_MAX_DELAY:
                delay = MOB_MAX_DELAY
            ms['delay'] = delay
            ms['cancel'] = i16(atoi(s[8]))
            if s[8] == 'yes':
                ms['cancel'] = 1
            if s[9] in MST:
                ms['target'] = MST[s[9]]
            else:
                warn('mob_parse_row_mobskilldb: Unrecognized target %s for %d' % (s[9], mob_id))
                ms['target'] = 0
            if skill_db.is_ground(ms['skill_id']):
                if ms['target'] > MST_AROUND:
                    warn('mob_parse_row_mobskilldb: Wrong mob skill target for ground skill %d' % ms['skill_id'])
                    ms['target'] = 0
            elif ms['target'] > MST_MASTER:
                warn('mob_parse_row_mobskilldb: Wrong mob skill target \'around\' for non-ground skill %d (mob %d)' % (ms['skill_id'], mob_id))
                ms['target'] = 0
            if s[10] in MSC_BY_NAME:
                ms['cond1'] = MSC_BY_NAME[s[10]]
            else:
                warn('mob_parse_row_mobskilldb: Unrecognized condition 1 %s for %d' % (s[10], mob_id))
                ms['cond1'] = -1
            ms['cond2'] = atoi(s[11])
            if s[11] in COND2_NAMES:
                ms['cond2'] = -1 if s[11] == 'anybad' else ctx['consts']['sc_' + s[11]]
            val = [i32(strtol(s[12 + k], 0)[0]) for k in range(5)]
            if ms['skill_id'] == K['NPC_EMOTION'] and mob_id > 0 and val[1] == mob['status']['mode']:
                val[1] = 0
                val[4] = 1
            if ms['skill_id'] == K['NPC_EMOTION_ON'] and mob_id > 0 and val[1]:
                if not (val[1] & K['MD_AGGRESSIVE']):
                    val[3] |= K['MD_AGGRESSIVE']
                val[2] |= val[1]
                val[1] = 0
            ms['val'] = val
            ms['emotion'] = i16(atoi(s[17])) if s[17] else -1
            if s[18]:
                mid = u16(strtol(s[18], 10)[0])
                if mid in chat_db.data:
                    ms['msg_id'] = mid
                else:
                    ms['msg_id'] = 0
                    warn('mob_parse_row_mobskilldb: Unknown chat ID %s for monster %d.' % (s[18], mob_id))
            else:
                ms['msg_id'] = 0
            skill['skill'].append(ms)
            if not skill['mob_id']:
                skill['mob_id'] = mob_id
                mob_skill_db[mob_id] = skill
            entries += 1
        counts.append((rel, entries))
    return mob_skill_db, counts


# mob.hpp s_mob_skill field order: the dataset writes each skill as an array in this order (serde reads a struct from a
# sequence field by field), which halves the largest part of mobs_db
MOB_SKILL_FIELDS = ('state', 'skill_id', 'skill_lv', 'permillage', 'casttime', 'delay', 'cancel', 'cond1', 'cond2',
                    'target', 'val', 'emotion', 'msg_id')


class MobItemRatioDb(YamlDb):
    """mob.cpp MobItemRatioDatabase (t_itemid -> s_mob_item_drop_ratio); cleared again after mob_drop_ratio_adjust."""
    TYPE, VERSION = 'MOB_ITEM_RATIO_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/mob_item_ratio.yml'

    def parse_body_node(self, node):
        item_name = self.as_str(node, 'Item')
        item = self.w['items'].search_aegisname(item_name)
        if item is None:
            self.invalid('Item %s does not exist, skipping.' % item_name)
            return 0
        nameid = item['nameid']
        data = self.find(nameid)
        exists = data is not None
        if not exists:
            if not self.nodes_exist(node, ['Ratio']):
                return 0
            data = {'nameid': nameid, 'drop_ratio': 0, 'mob_ids': []}
        if self.exists(node, 'Ratio'):
            data['drop_ratio'] = self.as_u32(node, 'Ratio')
        if self.exists(node, 'List'):
            lst = node['List'] or {}
            for mob_name in list(lst):
                mob = self.w['mob_db'].search_aegisname(mob_name)
                if mob is None:
                    self.invalid('Unknown mob %s, skipping.' % mob_name)
                    continue
                if not self.as_bool(lst, mob_name):
                    if mob['id'] in data['mob_ids']:
                        data['mob_ids'].remove(mob['id'])
                    continue
                data['mob_ids'].append(u16(mob['id']))
        if not exists:
            self.put(nameid, data)
        return 1


def pcdb_checkid(c):
    """pc.hpp pcdb_checkid (job ids, from the constant table)."""
    c = u32(c)
    t = K['_jobs']
    return (c < t['JOB_MAX_BASIC'] or t['JOB_NOVICE_HIGH'] <= c <= t['JOB_DARK_COLLECTOR']
            or t['JOB_RUNE_KNIGHT'] <= c <= t['JOB_MECHANIC_T2'] or t['JOB_BABY_RUNE_KNIGHT'] <= c <= t['JOB_BABY_MECHANIC2']
            or t['JOB_SUPER_NOVICE_E'] <= c <= t['JOB_SUPER_BABY_E'] or t['JOB_KAGEROU'] <= c <= t['JOB_OBORO']
            or c == t['JOB_REBELLION'] or c == t['JOB_SUMMONER'] or c == t['JOB_BABY_SUMMONER']
            or t['JOB_BABY_NINJA'] <= c <= t['JOB_BABY_REBELLION']
            or t['JOB_BABY_STAR_GLADIATOR2'] <= c <= t['JOB_BABY_STAR_EMPEROR2']
            or t['JOB_DRAGON_KNIGHT'] <= c <= t['JOB_TROUVERE'] or t['JOB_WINDHAWK2'] <= c <= t['JOB_IMPERIAL_GUARD2']
            or t['JOB_SKY_EMPEROR'] <= c <= t['JOB_SPIRIT_HANDLER'] or c == t['JOB_SKY_EMPEROR2'])


def npcdb_checkid(c):
    """npc.hpp npcdb_checkid."""
    r = K['_npc']
    return ((r['NPC_RANGE1_START'] < c < r['NPC_RANGE1_END']) or c == r['JT_HIDDEN_WARP_NPC']
            or (r['NPC_RANGE2_START'] < c < r['NPC_RANGE2_END']) or c == r['JT_INVISIBLE']
            or (r['NPC_RANGE3_START'] < c < r['NPC_RANGE3_END']))


class MobAvailDb(YamlDb):
    """mob.cpp MobAvailDatabase (db/import/mob_avail.yml only; edits mob_db entries in place). pet_db is still empty
    when it loads (do_init_mob runs before do_init_pet), so PetEquip can never apply."""
    TYPE, VERSION = 'MOB_AVAIL_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/import/mob_avail.yml'

    def clear(self):
        pass

    def parse_body_node(self, node):
        mobs, items = self.w['mob_db'], self.w['items']
        mob_name = self.as_str(node, 'Mob')
        mob = mobs.search_aegisname(mob_name)
        if mob is None:
            self.invalid('Unknown mob %s.' % mob_name)
            return 0
        look = mob['vd']['look']
        if not self.exists(node, 'Sprite'):
            self.invalid('Sprite is missing.')
            return 0
        sprite = self.as_str(node, 'Sprite')
        c = self.const(sprite)
        if c is not None:
            if not npcdb_checkid(c) and not pcdb_checkid(c):
                self.invalid('Unknown sprite constant %s.' % sprite)
                return 0
        else:
            sm = mobs.search_aegisname(sprite)
            if sm is None:
                self.invalid('Unknown mob sprite constant %s.' % sprite)
                return 0
            c = sm['id']
        look[0] = i32(c)   # LOOK_BASE
        look[13] = i32(c)  # LOOK_BODY2

        def job_only(key):
            if not pcdb_checkid(look[0]):
                self.invalid('%s is only applicable to Job sprites.' % key)
                raise Fail()
        conf = self.w['conf']
        if self.exists(node, 'Sex'):
            job_only('Sex')
            sex = self.as_str(node, 'Sex')
            c = self.const('SEX_' + sex)
            if c is None or c < K['SEX_FEMALE'] or c > K['SEX_MALE']:
                self.invalid('Unknown sex constant %s.' % sex)
                return 0
            mob['vd']['sex'] = c
        for key, idx, lo, hi in (('HairStyle', 1, 'min_hair_style', 'max_hair_style'),
                                 ('HairColor', 6, 'min_hair_color', 'max_hair_color'),
                                 ('ClothColor', 7, 'min_cloth_color', 'max_cloth_color')):
            if self.exists(node, key):
                job_only(key)
                v = self.as_u16(node, key) if key != 'ClothColor' else self.as_u32(node, key)
                if v < conf[lo] or v > conf[hi]:
                    self.invalid('%s %d is out of range.' % (key, v))
                    v = conf[lo]
                look[idx] = v
        for key, idx, field in (('Weapon', 2, 'nameid'), ('Shield', 8, 'nameid'), ('HeadTop', 4, 'look'),
                                ('HeadMid', 5, 'look'), ('HeadLow', 3, 'look'), ('Robe', 12, 'look')):
            if self.exists(node, key):
                job_only(key)
                name = self.as_str(node, key)
                item = items.search_aegisname(name)
                if item is None:
                    self.invalid('%s %s is not a valid item.' % (key, name))
                    return 0
                look[idx] = i32(item[field])
        if self.exists(node, 'PetEquip'):
            self.invalid('PetEquip is only applicable to defined pets.')  # pet_db not loaded yet
            return 0
        if self.exists(node, 'Options'):
            opts = node['Options'] or {}
            for name in list(opts):
                c = self.const('OPTION_' + name)
                if c is None:
                    self.invalid('Unknown option constant %s, skipping.' % name)
                    continue
                try:
                    active = self.as_bool(opts, name)
                except Fail:
                    continue
                if c & K['OPTION_CART']:  # NEW_CARTS (PACKETVER >= 20120201)
                    self.invalid('OPTION_CART was replace by SC_PUSH_CART, skipping.')
                    continue
                mob['option'] = u32(mob['option'] | c) if active else u32(mob['option'] & ~c)
            mob['option'] &= ~(K['OPTION_HIDE'] | K['OPTION_CLOAK'] | K['OPTION_INVISIBLE'] | K['OPTION_CHASEWALK'])
        return 1


class MobSummonDb(YamlDb):
    """mob.cpp MobSummonDatabase (uint16 -> s_randomsummon_group; list is an unordered_map mob_id -> entry)."""
    TYPE, VERSION = 'MOB_SUMMONABLE_DB', 1

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/mob_summon.yml'

    def parse_body_node(self, node):
        mobs = self.w['mob_db']
        group_name = self.as_str(node, 'Group')
        c = self.const('MOBG_' + group_name)
        if c is None or c < K['MOBG_BRANCH_OF_DEAD_TREE'] or c >= K['MOBG_MAX']:
            self.invalid('Invalid monster group %s.' % group_name)
            return 0
        gid = u16(c)
        summon = self.find(gid)
        exists = summon is not None
        if not exists:
            if not self.nodes_exist(node, ['Default']):
                return 0
            summon = {'random_id': gid, 'default_mob_id': 0, 'list': UMap()}
        if self.exists(node, 'Default'):
            name = self.as_str(node, 'Default')
            mob = mobs.search_aegisname(name)
            if mob is None:
                self.invalid('Unknown mob %s.' % name)
                return 0
            summon['default_mob_id'] = mob['id']
        if self.exists(node, 'Summon'):
            for mobit in node['Summon'] or []:
                if not self.nodes_exist(mobit, ['Mob', 'Rate']):
                    continue
                try:
                    name = self.as_str(mobit, 'Mob')
                except Fail:
                    continue
                mob = mobs.search_aegisname(name)
                if mob is None:
                    self.invalid('Unknown mob %s.' % name)
                    continue
                try:
                    rate = self.as_u32(mobit, 'Rate')
                except Fail:
                    continue
                mob_id = u16(mob['id'])
                if rate == 0:
                    if summon['list'].pop(mob_id, None) is None:
                        self.invalid('Failed to remove %s, the monster doesn\'t exist in group %s.' % (name, group_name))
                    continue
                entry = summon['list'].get(mob_id)
                if entry is None:
                    entry = {'mob_id': mob_id, 'rate': 0}
                    summon['list'][mob_id] = entry
                entry['rate'] = rate
        if not exists:
            self.put(gid, summon)
        return 1


class MapDropDb(YamlDb):
    """mob.cpp MapDropDatabase (map id -> s_map_drops; globals and specific are unordered_maps)."""
    TYPE, VERSION = 'MAP_DROP_DB', 2

    def __init__(self, ctx, world):
        super().__init__(ctx)
        self.w = world

    def default_location(self):
        return 'db/map_drops.yml'

    def parse_drop(self, node, drops):
        """MapDropDatabase::parseDrop."""
        index = self.as_u16(node, 'Index')
        drop = drops.get(index)
        exists = drop is not None
        if not exists:
            if not self.nodes_exist(node, ['Item', 'Rate']):
                raise Fail()
            drop = {'nameid': 0, 'rate': 0, 'randomopt_group': 0, 'steal_protected': True}
        if self.exists(node, 'Item'):
            name = self.as_str(node, 'Item')
            item = self.w['items'].search_aegisname(name)
            if item is None:
                self.invalid('Item %s does not exist.' % name)
                raise Fail()
            drop['nameid'] = item['nameid']
        if self.exists(node, 'Rate'):
            rate = self.as_u32_rate(node, 'Rate', 100000)  # 0 already fails here, so the erase branch never runs
            if rate == 0:
                if exists:
                    drops.pop(index)
                    return
                raise Fail()
            drop['rate'] = rate
        if self.exists(node, 'RandomOptionGroup'):
            name = self.as_str(node, 'RandomOptionGroup')
            g = self.w['randomopt'].option_get_id(name)
            if g is None:
                self.invalid('Unknown random option group "%s".' % name)
                raise Fail()
            drop['randomopt_group'] = g
        elif not exists:
            drop['randomopt_group'] = 0
        if not exists:
            drops[index] = drop

    def parse_body_node(self, node):
        maps = self.w['maps']
        mapname = self.as_str(node, 'Map')
        idx = maps.name2idx(mapname)
        if idx == 0:
            self.invalid('Unknown map "%s".' % mapname)
            return 0
        mapid = maps.mapindex2mapid(idx)
        if mapid < 0:
            return 0
        md = self.find(mapid)
        exists = md is not None
        if not exists:
            md = {'mapid': mapid, 'map': maps.mapid2name(mapid), 'globals': UMap(), 'specific': UMap()}
        if self.exists(node, 'GlobalDrops'):
            for it in node['GlobalDrops'] or []:
                self.parse_drop(it, md['globals'])
        if self.exists(node, 'SpecificDrops'):
            for mn in node['SpecificDrops'] or []:
                if not self.nodes_exist(mn, ['Monster', 'Drops']):
                    return 0
                mobname = self.as_str(mn, 'Monster')
                mob = self.w['mob_db'].search_aegisname(mobname)
                if mob is None:
                    self.invalid('Unknown monster "%s".' % mobname)
                    return 0
                key = u16(mob['id'])
                if key not in md['specific']:
                    md['specific'][key] = UMap()
                for it in mn['Drops'] or []:
                    self.parse_drop(it, md['specific'][key])
        if not exists:
            self.put(mapid, md)
        return 1


# battle_config settings (battle_data names) that mob_db consumes while it loads and that the module applies again at a
# mob_reload under other values (src/battle_config/mod.rs MOBDB_MEMBERS, src/mob/rtdb.rs): the EXP rates
# (MobDatabase::parseBodyNode) and everything mob_drop_ratio_adjust / mob_drop_adjust read. The dataset says which
# values it was made with.
MOBDB_CONF_NAMES = (
    ['base_exp_rate', 'job_exp_rate', 'mvp_exp_rate', 'item_logarithmic_drops', 'drop_rateincrease',
     'item_rate_mvp', 'item_drop_mvp_min', 'item_drop_mvp_max',
     'item_rate_treasure', 'item_drop_treasure_min', 'item_drop_treasure_max']
    + [n % k for k in ('heal', 'use', 'equip', 'card', 'common')
       for n in ('item_rate_%s', 'item_rate_%s_boss', 'item_rate_%s_mvp', 'item_drop_%s_min', 'item_drop_%s_max')]
    + ['monster_damage_delay_rate', 'view_range_rate', 'chase_range_rate', 'monster_active_enable', 'mvp_hp_rate',
       'monster_hp_rate'])


def mob_drop_adjust(baserate, rate_adjust, rate_min, rate_max, conf):
    """mob.cpp mob_drop_adjust (logarithmic or linear; double math, capped, cast to uint32)."""
    rate = float(baserate)
    if conf['logarithmic_drops'] and rate_adjust > 0 and rate_adjust != 100 and baserate > 0:
        rate = rate * math.pow((5.0 - math.log10(rate)), (math.log(rate_adjust / 100.) / math.log(5.0))) + 0.5
    else:
        rate = rate * rate_adjust / 100  # double * int / int
    return u32(int(cap_value(rate, rate_min, rate_max)))


def mob_drop_ratio_adjust(world):
    """mob.cpp mob_drop_ratio_adjust: rates of every drop, entries removed, and the item_db side (maxchance, mob[])."""
    conf, items, ratio_db = world['conf'], world['items'], world['mob_item_ratio']
    item_info = {}  # nameid -> {'maxchance': int, 'mob': [[chance, id] * MAX_SEARCH]}

    def info(nameid):
        if nameid not in item_info:
            item_info[nameid] = {'maxchance': 0, 'mob': [[0, 0] for _ in range(K['MAX_SEARCH'])]}
        return item_info[nameid]

    def ratio(nameid, mob_id, rate_adjust):
        r = ratio_db.find(nameid)
        if r is not None and (not r['mob_ids'] or u16(mob_id) in r['mob_ids']):
            return r['drop_ratio']
        return rate_adjust

    for mob_id, mob in world['mob_db'].data.items():
        if MAX_MOB_DB <= mob_id <= MIN_MOB_DB2:  # mob_is_clone
            continue
        # the lists as the files gave them: the module adjusts them again under other rates (src/mob/rtdb.rs)
        raw_mvp = [dict(e) for e in mob['mvpitem']]
        raw_drop = [dict(e) for e in mob['dropitem']]
        keep = []
        for e in mob['mvpitem']:
            if e['nameid'] == 0 or e['rate'] == 0:
                continue
            rate_adjust = ratio(e['nameid'], mob_id, conf['item_rate_mvp'])
            if rate_adjust == 0:
                continue
            rate = mob_drop_adjust(e['rate'], rate_adjust, conf['item_drop_mvp_min'], conf['item_drop_mvp_max'], conf)
            if rate:
                inf = info(items.search(e['nameid'])['nameid'])  # itemdb_search never fails (dummy item)
                if inf['maxchance'] == -1 or inf['maxchance'] < rate // 10 + 1:
                    inf['maxchance'] = rate // 10 + 1
            e['rate'] = rate
            keep.append(e)
        mob['mvpitem'] = keep
        keep = []
        bosstype = get_bosstype(mob['status'])
        for e in mob['dropitem']:
            if e['nameid'] == 0 or e['rate'] == 0:
                continue
            item = items.search(e['nameid'])  # itemdb_search never fails (dummy item)
            rate = e['rate']
            if conf['drop_rateincrease'] and rate < 5000:
                rate += 1
            if K['RC2_TREASURE'] in mob['race2']:
                treasure = True
                rate_adjust, rmin, rmax = conf['item_rate_treasure'], conf['item_drop_treasure_min'], conf['item_drop_treasure_max']
            else:
                treasure = False
                mvp, boss = bosstype == 2, bosstype == 1
                t = item['type']
                if t == K['IT_HEALING']:
                    kind, rmin, rmax = 'heal', conf['item_drop_heal_min'], conf['item_drop_heal_max']
                elif t in (K['IT_USABLE'], K['IT_CASH']):
                    kind, rmin, rmax = 'use', conf['item_drop_use_min'], conf['item_drop_use_max']
                elif t in (K['IT_WEAPON'], K['IT_ARMOR'], K['IT_PETARMOR']):
                    kind, rmin, rmax = 'equip', conf['item_drop_equip_min'], conf['item_drop_equip_max']
                elif t == K['IT_CARD']:
                    kind, rmin, rmax = 'card', conf['item_drop_card_min'], conf['item_drop_card_max']
                else:
                    kind, rmin, rmax = 'common', conf['item_drop_common_min'], conf['item_drop_common_max']
                rate_adjust = conf['item_rate_%s_mvp' % kind] if mvp else (conf['item_rate_%s_boss' % kind] if boss else conf['item_rate_' + kind])
            rate_adjust = ratio(e['nameid'], mob_id, rate_adjust)
            if rate_adjust == 0:
                continue
            rate = mob_drop_adjust(rate, rate_adjust, rmin, rmax, conf)
            if rate and not treasure:
                inf = info(item['nameid'])
                if inf['maxchance'] == -1 or inf['maxchance'] < rate:
                    inf['maxchance'] = rate
                arr = inf['mob']
                k = next((k for k in range(K['MAX_SEARCH']) if arr[k][0] <= rate), K['MAX_SEARCH'])
                if k != K['MAX_SEARCH']:
                    if arr[k][1] != mob_id:
                        arr[k + 1:] = arr[k:K['MAX_SEARCH'] - 1]
                    arr[k] = [rate, mob_id]
            e['rate'] = rate
            keep.append(e)
        mob['dropitem'] = keep
        # written only where the adjustment changed something (an absent raw list is the adjusted one)
        mob['mvpitem_raw'] = raw_mvp if raw_mvp != mob['mvpitem'] else []
        mob['dropitem_raw'] = raw_drop if raw_drop != mob['dropitem'] else []
    # mob_item_ratio.yml as mob_drop_ratio_adjust used it (rAthena clears it afterwards and reads it again at a reload)
    world['item_ratio_out'] = [{'nameid': r['nameid'], 'drop_ratio': r['drop_ratio'], 'mob_ids': list(r['mob_ids'])}
                               for _, r in ratio_db.data.items()]
    ratio_db.clear()
    return item_info


def mob_skill_db_set(world, mob_skill_db):
    """mob.cpp mob_skill_db_set / mob_skill_db_set_single(_sub): each monster's skill list (MAX_MOBSKILL cap); negative
    ids are the global lists (bit 1 bosses, bit 2 normal monsters)."""
    mob_db = world['mob_db']

    def put(mob, sk):
        i = 0
        while len(mob['skill']) < K['MAX_MOBSKILL'] and i < len(sk['skill']):
            mob['skill'].append(sk['skill'][i])
            i += 1
        if i < len(sk['skill']):
            warn('Monster \'%s\' (%d, src:%d) reaches max skill limit %d.' % (mob['sprite'], mob['id'], sk['mob_id'], K['MAX_MOBSKILL']))
    for _, sk in mob_skill_db.items():
        if sk['mob_id'] >= 0:
            mob = mob_db.find(sk['mob_id'])
            if mob is not None:
                put(mob, sk)
        else:
            gid = u16(-sk['mob_id'])
            for mid, mob in mob_db.data.items():
                if MAX_MOB_DB <= mid <= MIN_MOB_DB2:
                    continue
                immune = mob['status']['mode'] & K['MD_STATUSIMMUNE'] == K['MD_STATUSIMMUNE']
                if (not (gid & 1) and immune) or (not (gid & 2) and not immune):
                    continue
                put(mob, sk)


# -------------------------------------------------------------------------------------------------------- the world
def world(ctx):
    """Everything the loaders share, built once per datagen run (mob_load order: mob_db, mob_chat_db, mob_skill_db,
    mob_item_ratio, mob_avail, mob_summon, map_drops, mob_drop_ratio_adjust, mob_skill_db_set)."""
    if 'loki_world' in ctx:
        return ctx['loki_world']
    w = {}
    ctx['loki_world'] = w
    K['_jobs'] = {n: ctx['consts'][lower(n)] for n in (
        'JOB_NOVICE_HIGH', 'JOB_DARK_COLLECTOR', 'JOB_RUNE_KNIGHT', 'JOB_MECHANIC_T2', 'JOB_BABY_RUNE_KNIGHT',
        'JOB_BABY_MECHANIC2', 'JOB_SUPER_NOVICE_E', 'JOB_SUPER_BABY_E', 'JOB_KAGEROU', 'JOB_OBORO', 'JOB_REBELLION',
        'JOB_SUMMONER', 'JOB_BABY_SUMMONER', 'JOB_BABY_NINJA', 'JOB_BABY_REBELLION', 'JOB_BABY_STAR_GLADIATOR2',
        'JOB_BABY_STAR_EMPEROR2', 'JOB_DRAGON_KNIGHT', 'JOB_TROUVERE', 'JOB_WINDHAWK2', 'JOB_IMPERIAL_GUARD2',
        'JOB_SKY_EMPEROR', 'JOB_SPIRIT_HANDLER', 'JOB_SKY_EMPEROR2')}
    # probe (loki/tools/packetgen/consts): JOB_MAX_BASIC and the npc.hpp class ranges (enum-derived, not exported)
    K['_jobs']['JOB_MAX_BASIC'] = 31
    K['_npc'] = dict(NPC_RANGE1_START=44, NPC_RANGE1_END=126, JT_HIDDEN_WARP_NPC=139, NPC_RANGE2_START=400,
                     NPC_RANGE2_END=1000, NPC_RANGE3_START=10000, NPC_RANGE3_END=20000, JT_INVISIBLE=32767)
    w['conf'] = read_battle_config(ctx)
    w['maps'] = Maps(ctx)
    w['items'] = ItemDb(ctx)
    w['items'].load()
    w['randomopt'] = RandomOptionGroupDb(ctx)
    w['randomopt'].load()
    w['skills'] = SkillDb(ctx)
    w['skills'].load()
    w['mob_db'] = MobDb(ctx, w)
    w['mob_db'].load()
    w['mob_chat'] = MobChatDb(ctx)
    w['mob_chat'].load()
    w['mob_skill_db'], w['mob_skill_counts'] = read_mob_skill_db(ctx, w)
    w['mob_item_ratio'] = MobItemRatioDb(ctx, w)
    w['mob_item_ratio'].load()
    w['mob_avail'] = MobAvailDb(ctx, w)
    w['mob_avail'].load()
    w['mob_summon'] = MobSummonDb(ctx, w)
    w['mob_summon'].load()
    w['map_drops'] = MapDropDb(ctx, w)
    w['map_drops'].load()
    w['item_drop_info'] = mob_drop_ratio_adjust(w)
    mob_skill_db_set(w, w['mob_skill_db'])
    return w


# --------------------------------------------------------------------------------------------------------- output
def compact(obj):
    """Drops the zero-like members (0, '', false, null, [], {}) of every object: the Rust side declares
    #[serde(default)], so an absent member reads back as the same value. Array elements are all kept."""
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            v = compact(v)
            if v is None or v is False or v == '' or v == [] or v == {} or (type(v) is int and v == 0):
                continue
            out[k] = v
        return out
    if isinstance(obj, list):
        return [compact(v) for v in obj]
    if isinstance(obj, UMap):
        raise TypeError('UMap must be converted before output')
    return obj


def report_counts(name, counts, expect=None):
    """Prints rAthena's "Done reading" lines; `expect` is what rAthena e985006 reported for the same files (the oracle's
    map-server log), so a loader that parses differently shows up here."""
    for rel, n in counts:
        print('  %s: Done reading \'%d\' entries in \'%s\'' % (name, n, rel))
    if expect is not None:
        got = [n for _, n in counts]
        if got != expect:
            warn('%s: counts %s differ from what rAthena reported (%s)' % (name, got, expect))


def build(ctx):
    w = world(ctx)
    report_counts('item_db', w['items'].counts, [6415, 12982, 9959])
    report_counts('item_randomopt_group', w['randomopt'].counts, [108])
    report_counts('skill_db', w['skills'].counts, [1635])
    report_counts('mob_db', w['mob_db'].counts, [2675])
    report_counts('mob_chat_db', w['mob_chat'].counts, [57])
    report_counts('mob_skill_db', w['mob_skill_counts'], [14514, 0])
    report_counts('mob_item_ratio', w['mob_item_ratio'].counts)
    report_counts('mob_avail', w['mob_avail'].counts)
    report_counts('mob_summon', w['mob_summon'].counts, [6])
    report_counts('map_drops', w['map_drops'].counts, [18])

    mobs = []
    for _, mob in w['mob_db'].data.items():
        m = dict(mob)
        m['skill'] = [[s[f] for f in MOB_SKILL_FIELDS] for s in mob['skill']]  # s_mob_skill as arrays (field order)
        mobs.append(m)
    # loaded_with: the settings the EXP and the drop lists above were made with ([name, value] pairs: a list, so that
    # compact keeps the zeros)
    names = w['conf']['__names__']
    loaded_with = [[n, w['conf'][names[n]]] for n in MOBDB_CONF_NAMES]
    out = compact({'mobs': mobs})
    out['loaded_with'] = loaded_with
    out['item_ratio'] = w.get('item_ratio_out', [])
    ctx['write_json']('mobs_db', out)
    chats = [c for _, c in w['mob_chat'].data.items()]
    ctx['write_json']('mobs_chat', compact({'chats': chats}))
    groups = []
    for _, g in w['mob_summon'].data.items():
        groups.append({'random_id': g['random_id'], 'default_mob_id': g['default_mob_id'],
                       'list': [e for _, e in g['list'].items()]})
    ctx['write_json']('mobs_summon', compact({'groups': groups}))
    drops = []
    for _, md in w['map_drops'].data.items():
        drops.append({'mapid': md['mapid'], 'map': md['map'],
                      'globals': [[k, d] for k, d in md['globals'].items()],
                      'specific': [[mid, [[k, d] for k, d in um.items()]] for mid, um in md['specific'].items()]})
    ctx['write_json']('mobs_map_drops', compact({'maps': drops}))
    info = [[nameid, v['maxchance'], [[c, i] for c, i in v['mob'] if c or i]]
            for nameid, v in sorted(w['item_drop_info'].items())]
    ctx['write_json']('mobs_item_drops', {'items': info})
