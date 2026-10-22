"""
Skill databases (rAthena e985006, RENEWAL): skill.cpp skill_readdb — SkillDatabase (skill_db.yml), skill_nocast_db.txt,
produce_db.txt, skill_changematerial_db.txt, skill_damage_db.txt, AbraDatabase, MagicMushroomDatabase,
ReadingSpellbookDatabase, SkillArrowDatabase, skill_init_unit_layout / skill_init_nounit_layout — and guild.cpp
GuildSkillTreeDatabase. Writes data/json/skills_*.json.z, read by src/db/skills.rs.

The first half of this file is shared with jobs.py (which imports it by path):
  * UMap — std::unordered_map<integral key> exactly as libstdc++ 13 orders it, so every list below that rAthena keeps
    in an unordered_map is written in the order rAthena iterates it (skill_db, abra/mushroom random picks, skill tree
    loops, ...). Verified against g++ 13.3 (the oracle's compiler) with random insert/erase sequences.
  * common/database.cpp YamlDatabase::load / parse / parseImports / asType / asBool with rapidyaml's value rules
    (c4::atoi / atou: no whitespace, 0x/0b/0o prefixes, wrap on overflow; plain '', ~, null are null values), and
    duplicate keys kept (has_child finds the first, iteration visits all).
  * common/strlib.cpp sv_readdb, C atoi / strtoul / strtol.
  * battle_config / inter_config values rAthena reads at load time.
  * the item_db subset the skill loaders need (Id, AegisName; ItemDatabase::parseBodyNode's rules for those two).
"""
import bisect, math, os, re, sys

import yaml

MAX_SKILL_LEVEL = 13          # skill.hpp
MAX_SKILL_ITEM_REQUIRE = 10   # skill.hpp
MAX_PRODUCE_RESOURCE = 12     # skill.hpp
MAX_SKILL_PRODUCE_DB = 300    # skill.hpp
MAX_SKILL_CHANGEMATERIAL_DB = 75   # skill.cpp
MAX_SKILL_CHANGEMATERIAL_SET = 3   # skill.cpp
SKILLDMG_MAX = 4              # map.hpp e_skill_damage_type
MAX_SKILL = 1641              # mmo.hpp
MAX_AMOUNT = 30000            # mmo.hpp
MAX_LEVEL = 275               # map.hpp
SKILL_NAME_LENGTH = 40        # skill.hpp
SKILL_DESC_LENGTH = 40        # skill.hpp
ITEM_NAME_LENGTH = 50         # mmo.hpp
SC_NONE, SC_MAX = -1, 1044    # status.hpp enum sc_type (SC_MAX follows SC_GROUND_CHARM_POWER = 1043)
BCT_ALL = 0x3F0000            # battle.hpp e_battle_check_target
BCT_NOENEMY = 0x3D0000
BL_PC, BL_MOB, BL_HOM, BL_MER, BL_SKILL, BL_ELEM = 0x1, 0x2, 0x8, 0x10, 0x40, 0x200   # map.hpp bl_type
BL_CHAR = BL_PC | BL_MOB | BL_HOM | BL_MER | BL_ELEM
BF_NONE, BF_MISC = 0, 0x4     # battle.hpp e_battle_flag
DMG_NORMAL = 0                # clif.hpp e_damage_type
ELE_NONE, ELE_NEUTRAL = -1, 0 # map.hpp e_element (int8)
INF_PASSIVE_SKILL = 0
UF_NOENEMY, UF_NOPC, UF_NOMOB, UF_SKILL = 1, 6, 7, 8   # skill.hpp e_skill_unit_flag bit numbers
SKILL_COPY_PLAGIARISM, SKILL_COPY_REPRODUCE = 0x1, 0x2
GD_SKILLBASE, MAX_GUILDSKILL = 10000, 20   # mmo.hpp (RENEWAL)
MAX_SQUARE_LAYOUT = 7
MAX_SKILL_UNIT_LAYOUT = 48 + MAX_SQUARE_LAYOUT
MAX_SKILL_UNIT_LAYOUT2 = 17

HERE = os.path.dirname(os.path.abspath(__file__))


# ============================================================================================ libstdc++ unordered_map

# std::__detail::__prime_list as _Prime_rehash_policy::_M_next_bkt returns it (probed from libstdc++ 13.3).
_PRIMES = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 103, 109, 113,
           127, 137, 139, 149, 157, 167, 179, 193, 199, 211, 227, 241, 257, 277, 293, 313, 337, 359, 383, 409, 439, 467, 503,
           541, 577, 619, 661, 709, 761, 823, 887, 953, 1031, 1109, 1193, 1289, 1381, 1493, 1613, 1741, 1879, 2029, 2179,
           2357, 2549, 2753, 2971, 3209, 3469, 3739, 4027, 4349, 4703, 5087, 5503, 5953, 6427, 6949, 7517, 8123, 8783, 9497,
           10273, 11113, 12011, 12983, 14033, 15173, 16411, 17749, 19183, 20753, 22447, 24281, 26267, 28411, 30727, 33223,
           35933, 38873, 42043, 45481, 49201, 53201, 57557, 62233, 67307, 72817, 78779, 85229, 92203, 99733, 107897,
           116731, 126271, 136607, 147793, 159871, 172933, 187091, 202409]
_FAST_BKT = [2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13]
_BB = object()   # _M_before_begin


class UMap:
    """std::unordered_map<K integral, V> (std::hash identity, _Prime_rehash_policy, max_load_factor 1): the singly linked
    node list and bucket array of libstdc++'s _Hashtable, so iteration order is rAthena's. Insert = _M_insert_unique /
    operator[] on a new key; erase = _M_erase; rehash = _M_rehash_aux (unique keys)."""

    def __init__(self):
        self.nb, self.buckets, self.nxt, self.head, self.vals, self.next_resize = 1, [None], {}, None, {}, 0

    def _bidx(self, k, n=None):
        return (k & 0xFFFFFFFFFFFFFFFF) % (n or self.nb)

    def _getn(self, p):
        return self.head if p is _BB else self.nxt[p]

    def _setn(self, p, v):
        if p is _BB:
            self.head = v
        else:
            self.nxt[p] = v

    def _next_bkt(self, n):
        if n < len(_FAST_BKT):
            if n == 0:
                return 1
            self.next_resize = math.floor(_FAST_BKT[n] * 1.0)
            return _FAST_BKT[n]
        i = bisect.bisect_left(_PRIMES, n, 6, len(_PRIMES) - 1)
        if i == len(_PRIMES) - 1:
            raise OverflowError('UMap: prime table exhausted')
        self.next_resize = math.floor(_PRIMES[i] * 1.0)
        return _PRIMES[i]

    def _need_rehash(self, n_bkt, n_elt, n_ins):
        if n_elt + n_ins > self.next_resize:
            min_bkts = max(n_elt + n_ins, 0 if self.next_resize else 11) / 1.0
            if min_bkts >= n_bkt:
                return True, self._next_bkt(max(math.floor(min_bkts) + 1, n_bkt * 2))
            self.next_resize = math.floor(n_bkt * 1.0)
        return False, 0

    def _rehash(self, n):
        newb, p, self.head, bbegin = [None] * n, self.head, None, 0
        while p is not None:
            nx, b = self.nxt[p], self._bidx(p, n)
            if newb[b] is None:
                self.nxt[p] = self.head
                self.head = p
                newb[b] = _BB
                if self.nxt[p] is not None:
                    newb[bbegin] = p
                bbegin = b
            else:
                q = newb[b]
                self.nxt[p] = self._getn(q)
                self._setn(q, p)
            p = nx
        self.buckets, self.nb = newb, n

    def __contains__(self, k):
        return k in self.vals

    def __len__(self):
        return len(self.vals)

    def __getitem__(self, k):
        return self.vals[k]

    def get(self, k, d=None):
        return self.vals.get(k, d)

    def insert(self, k, v):
        """Returns False (and changes nothing) when the key exists."""
        if k in self.vals:
            return False
        do, n = self._need_rehash(self.nb, len(self.vals), 1)
        if do:
            self._rehash(n)
        bkt = self._bidx(k)
        if self.buckets[bkt] is not None:
            q = self.buckets[bkt]
            self.nxt[k] = self._getn(q)
            self._setn(q, k)
        else:
            self.nxt[k] = self.head
            self.head = k
            if self.nxt[k] is not None:
                self.buckets[self._bidx(self.nxt[k])] = k
            self.buckets[bkt] = _BB
        self.vals[k] = v
        return True

    def __setitem__(self, k, v):
        if k in self.vals:
            self.vals[k] = v
        else:
            self.insert(k, v)

    def erase(self, k):
        if k not in self.vals:
            return 0
        bkt = self._bidx(k)
        prev = self.buckets[bkt]
        while self._getn(prev) != k:
            prev = self._getn(prev)
        nn = self.nxt[k]
        if prev is self.buckets[bkt]:
            nbk = self._bidx(nn) if nn is not None else 0
            if nn is None or nbk != bkt:
                if nn is not None:
                    self.buckets[nbk] = self.buckets[bkt]
                if self.buckets[bkt] is _BB:
                    self.head = nn
                self.buckets[bkt] = None
        elif nn is not None:
            nbk = self._bidx(nn)
            if nbk != bkt:
                self.buckets[nbk] = prev
        self._setn(prev, nn)
        del self.nxt[k]
        del self.vals[k]
        return 1

    def keys(self):
        p = self.head
        while p is not None:
            yield p
            p = self.nxt[p]

    __iter__ = keys

    def items(self):
        return [(k, self.vals[k]) for k in self.keys()]

    def values(self):
        return [self.vals[k] for k in self.keys()]


# ============================================================================================ integers as C/C++ read them

def wrap(v, bits, signed):
    v &= (1 << bits) - 1
    if signed and v >= 1 << (bits - 1):
        v -= 1 << bits
    return v


def _c4_read(s, base):
    digits = '0123456789abcdef'[:base]
    if not s:
        return None
    v = 0
    for ch in s.lower():
        d = digits.find(ch)
        if d < 0:
            return None
        v = v * base + d
    return v


def c4_atox(s, bits, signed):
    """c4::atoi / c4::atou (rapidyaml's `node >> integer`): the whole scalar must be the number; '-' only for signed;
    0x / 0b / 0o prefixes; leading zeroes are decimal; overflow wraps. None when it does not parse."""
    if s is None or len(s) == 0:
        return None
    sign, start = 1, 0
    if signed and s[0] == '-':
        if len(s) == 1:
            return None
        sign, start = -1, 1
    body = s[start:]
    if body[0] != '0':
        v = _c4_read(body, 10)
    elif len(body) == 1:
        v = 0
    elif body[1] in 'xX':
        v = _c4_read(body[2:], 16) if len(body) > 2 else None
    elif body[1] in 'bB':
        v = _c4_read(body[2:], 2) if len(body) > 2 else None
    elif body[1] in 'oO':
        v = _c4_read(body[2:], 8) if len(body) > 2 else None
    else:
        rest = body.lstrip('0')
        v = 0 if not rest else _c4_read(rest, 10)
    if v is None:
        return None
    return wrap(sign * v, bits, signed)


def c_strtol(s, base=10, bits=64, signed=True):
    """C strtol/strtoul: leading whitespace, sign, digits until the first non-digit; (value, rest)."""
    i, n = 0, len(s)
    while i < n and s[i] in ' \t\n\r\f\v':
        i += 1
    neg = False
    if i < n and s[i] in '+-':
        neg = s[i] == '-'
        i += 1
    if base == 0:
        if s[i:i + 2].lower() == '0x' and i + 2 < n and s[i + 2].lower() in '0123456789abcdef':
            base, i = 16, i + 2
        elif i < n and s[i] == '0':
            base = 8
        else:
            base = 10
    elif base == 16 and s[i:i + 2].lower() == '0x':
        i += 2
    digits = '0123456789abcdefghijklmnopqrstuvwxyz'[:base]
    j, v = i, 0
    while j < n and s[j].lower() in digits:
        v = v * base + digits.index(s[j].lower())
        j += 1
    if j == i:
        return 0, s
    v = -v if neg else v
    if signed:
        v = max(-(1 << (bits - 1)), min((1 << (bits - 1)) - 1, v))
    else:
        if v > (1 << bits) - 1 or v < -((1 << bits) - 1):
            v = (1 << bits) - 1
        v &= (1 << bits) - 1
    return v, s[j:]


def c_atoi(s):
    """glibc atoi = (int)strtol(s, NULL, 10)."""
    return wrap(c_strtol(s, 10)[0], 32, True)


def c_strtoul(s):
    """strtoul(s, nullptr, 10) stored in a 32-bit unsigned (t_itemid)."""
    return wrap(c_strtol(s, 10, 64, False)[0], 32, False)


# ============================================================================================ rapidyaml-style YAML nodes

_C1 = {c: 0xE000 + c for c in range(0x80, 0xA0)}     # libyaml refuses C1 controls; ryml reads bytes. Mapped out and back.
_C1_BACK = {0xE000 + c: c for c in range(0x80, 0xA0)}


def compose_file(path):
    text = open(path, encoding='latin-1').read().translate(_C1)
    return yaml.compose(text, Loader=getattr(yaml, 'CBaseLoader', yaml.BaseLoader))


def is_map(n):
    return isinstance(n, yaml.MappingNode)


def is_seq(n):
    return isinstance(n, yaml.SequenceNode)


def sval(n):
    """The scalar's text (ryml val()), byte-for-byte (latin-1 code points)."""
    return n.value.translate(_C1_BACK)


def val_is_null(n):
    if not isinstance(n, yaml.ScalarNode):
        return True   # a container has no val: ryml's val.scalar is null
    return n.style is None and n.value in ('', '~', 'null', 'Null', 'NULL')


def kids(n):
    """ryml child iteration: (key text or None, child node) in document order (duplicates kept)."""
    if is_map(n):
        return [(sval(k), v) for k, v in n.value]
    if is_seq(n):
        return [(None, v) for v in n.value]
    return []


def node_exists(n, name):
    """YamlDatabase::nodeExists: num_children() > 0 && has_child(name) (exact key)."""
    return is_map(n) and len(n.value) > 0 and any(sval(k) == name for k, _ in n.value)


def child(n, name):
    for k, v in n.value:
        if sval(k) == name:
            return v
    return None


class YamlDatabase:
    """common/database.cpp YamlDatabase + TypesafeYamlDatabase (data in a UMap keyed like rAthena's)."""
    type_ = ''
    version = 1
    min_version = None
    location = ''

    def __init__(self, ctx):
        self.ctx = ctx
        self.data = UMap()
        self.current_file = ''
        self.counts = []       # (file, entries) as "Done reading '%d' entries in '%s'" reports them
        self.warnings = []

    # --- TypesafeYamlDatabase
    def clear(self):
        self.data = UMap()

    def find(self, key):
        return self.data.get(key)

    def put(self, key, v):
        self.data[key] = v

    # --- reporting
    def warn(self, msg):
        self.warnings.append('%s: %s' % (self.current_file, msg))

    # --- YamlDatabase
    def load(self):
        ret = self.load_path(self.location)
        self.loading_finished()
        return ret

    def loading_finished(self):
        pass

    def load_path(self, rel):
        path = self.ctx['resolve'](rel)
        if not os.path.exists(path):
            self.warn('Failed to open %s database file from %s' % (self.type_, rel))
            return False
        tree = compose_file(path)
        self.current_file = rel
        if not self.verify_compatibility(tree):
            return False
        header = child(tree, 'Header')
        if node_exists(header, 'Clear'):
            ok, clear = self.as_bool(header, 'Clear')
            if ok and clear:
                self.clear()
        self.parse(tree)
        self.parse_imports(tree)
        return True

    def verify_compatibility(self, tree):
        if not node_exists(tree, 'Header'):
            self.warn('No database "Header" was found.')
            return False
        header = child(tree, 'Header')
        ok, t = self.as_string(header, 'Type')
        if not ok:
            return False
        if t != self.type_:
            self.warn('Database type mismatch: %s != %s.' % (self.type_, t))
            return False
        ok, v = self.as_int(header, 'Version', 16, False)
        if not ok:
            return False
        minimum = self.version if self.min_version is None else self.min_version
        if v != self.version:
            if v > self.version:
                return False
            elif v >= minimum:
                self.warn('Database version %d is outdated and should be updated. Current version is: %d' % (v, self.version))
            else:
                return False
        return True

    def parse(self, tree):
        if node_exists(tree, 'Body'):
            count = 0
            for _, node in kids(child(tree, 'Body')):
                count += self.parse_body_node(node)
            self.counts.append((self.current_file, count))

    def parse_imports(self, tree):
        if not node_exists(tree, 'Footer'):
            return
        footer = child(tree, 'Footer')
        if not node_exists(footer, 'Imports'):
            return
        for _, node in kids(child(footer, 'Imports')):
            ok, import_file = self.as_string(node, 'Path')
            if not ok:
                continue
            if node_exists(node, 'Mode'):
                ok, mode = self.as_string(node, 'Mode')
                if not ok:
                    continue
                # RENEWAL with RENEWAL_ASPD defined (config/renewal.hpp): every Renewal import is taken.
                if mode != 'Renewal':
                    continue
            if node_exists(node, 'Generator'):
                ok, gen = self.as_bool(node, 'Generator')
                if not ok:
                    continue
                if not (False and gen):   # shouldLoadGenerator is false in map-server
                    continue
            self.load_path(import_file)

    # --- asType family: (ok, value)
    def as_int(self, node, name, bits, signed):
        if not node_exists(node, name):
            self.warn('Missing node "%s".' % name)
            return False, 0
        d = child(node, name)
        if val_is_null(d):
            self.warn('Node "%s" is missing a value.' % name)
            return False, 0
        v = c4_atox(sval(d), bits, signed)
        if v is None:
            self.warn('Node "%s" cannot be parsed as an integer: %r.' % (name, sval(d)))
            return False, 0
        return True, v

    def as_string(self, node, name):
        if not node_exists(node, name):
            self.warn('Missing node "%s".' % name)
            return False, ''
        d = child(node, name)
        if val_is_null(d):
            self.warn('Node "%s" is missing a value.' % name)
            return False, ''
        return True, sval(d)

    def as_bool(self, node, name):
        d = child(node, name) if is_map(node) else None
        if d is None or val_is_null(d):
            self.warn('Node "%s" is missing a value.' % name)
            return False, False
        s = sval(d).lower()
        if s == 'true':
            return True, True
        if s == 'false':
            return True, False
        self.warn('Unknown boolean value: "%s".' % s)
        return False, False

    def as_uint16_rate(self, node, name, maximum=10000):
        ok, v = self.as_int(node, name, 16, False)
        if not ok:
            return False, 0
        if v > maximum or v == 0:
            self.warn('Node "%s" rate %d out of 1~%d.' % (name, v, maximum))
            return False, v
        return True, v

    def nodes_exist(self, node, names):
        missing = [n for n in names if not node_exists(node, n)]
        for n in missing:
            self.warn('Missing mandatory node "%s".' % n)
        return not missing


def script_get_constant(ctx, name):
    """script.cpp script_get_constant: case-insensitive, C_INT entries only."""
    return ctx['consts'].get(name.lower())


# ============================================================================================ sv_readdb

def sv_readdb(ctx, directory, filename, delim, mincols, maxcols, maxrows, parseproc, silent, log):
    """common/strlib.cpp sv_readdb: `//` starts a comment anywhere, empty lines skipped, fields split on delim (not
    trimmed), rows with too few/many columns skipped, `maxrows` successful rows at most. Returns rows parsed."""
    rel = '%s/%s' % (directory, filename)
    path = ctx['resolve'](rel)
    if not os.path.exists(path):
        if not silent:
            log.append('sv_readdb: can\'t read %s' % rel)
        return None
    entries = 0
    with open(path, encoding='latin-1', newline='') as f:
        for line in f:
            i = line.find('//')
            if i >= 0:
                line = line[:i]
            if line == '' or line[0] in '\n\r':
                continue
            if line.endswith('\r\n'):
                line = line[:-2]
            elif line.endswith('\n'):
                line = line[:-1]
            fields = line.split(delim)
            columns = len(fields)
            if columns < mincols or columns > maxcols:
                continue
            if entries == maxrows:
                break
            if not parseproc(fields, columns, entries):
                continue
            entries += 1
    return entries


# ============================================================================================ configuration

def _conf_path(ctx, rel):
    return ctx['resolve'](rel)


def battle_config(ctx):
    """battle.cpp battle_config_read(conf/battle_athena.conf): `%[^:]:%s` lines, `import:` recursion,
    battle_set_value (config_switch, min/max check against battle_data). Only the settings the job/skill loaders read."""
    if '_battle_config' in ctx:
        return ctx['_battle_config']
    # battle.cpp battle_data: name -> (default, min, max)
    table = {
        'defunit_not_enemy': (0, 0, 1),
        'max_parameter': (99, 10, 32767),
        'max_baby_parameter': (80, 10, 32767),
        'max_third_parameter': (135, 10, 32767),
        'max_baby_third_parameter': (108, 10, 32767),
        'max_trans_parameter': (99, 10, 32767),
        'max_third_trans_parameter': (135, 10, 32767),
        'max_extended_parameter': (125, 10, 32767),
        'max_summoner_parameter': (120, 10, 32767),
        'max_fourth_parameter': (135, 10, 32767),
        'max_trait_parameter': (100, 10, 32767),
        'use_statpoint_table': (1, 0, 1),
        'use_traitpoint_table': (1, 0, 1),
        'require_glory_guild': (0, 0, 1),
    }
    conf = {k: d for k, (d, lo, hi) in table.items()}

    def config_switch(s):
        l = s.lower()
        if l in ('on', 'yes', 'oui', 'ja', 'si', 'sim'):
            return 1
        if l in ('off', 'no', 'non', 'nein', 'nao'):
            return 0
        return wrap(c_strtol(s, 0)[0], 32, True)

    def read(rel, depth=0):
        path = _conf_path(ctx, rel)
        if not os.path.exists(path) or depth > 16:
            return
        for line in open(path, encoding='latin-1'):
            if line.startswith('//'):
                continue
            m = re.match(r'([^:\n]+):\s*(\S+)', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2)
            if w1.lower() == 'import':
                read(w2, depth + 1)
                continue
            k = w1.lower()
            if k in table:
                d, lo, hi = table[k]
                v = config_switch(w2)
                conf[k] = d if (v < lo or v > hi) else v

    read('conf/battle_athena.conf')
    ctx['_battle_config'] = conf
    return conf


def inter_config(ctx):
    """map.cpp inter_config_read(conf/inter_athena.conf): `%[^:]: %[^\\r\\n]`, `renewal-` prefix kept under RENEWAL,
    `import:` recursion. Only start_status_points (default 48, map.cpp do_init)."""
    conf = {'start_status_points': 48}

    def read(rel, depth=0):
        path = _conf_path(ctx, rel)
        if not os.path.exists(path) or depth > 16:
            return
        for line in open(path, encoding='latin-1'):
            if line.startswith('//'):
                continue
            m = re.match(r'([^:\n]+):\s*([^\r\n]+)', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2)
            if w1.lower().startswith('renewal-'):
                w1 = w1[len('renewal-'):]
            if w1.lower() == 'start_status_points':
                conf['start_status_points'] = wrap(c_atoi(w2), 32, False)
            if w1.lower() == 'import':
                read(w2, depth + 1)

    read('conf/inter_athena.conf')
    return conf


# ============================================================================================ item ids (item_db subset)

class ItemIdDatabase(YamlDatabase):
    """itemdb.cpp ItemDatabase::parseBodyNode, only Id and AegisName (the item agent ports the rest). search_aegisname
    lowers the name; a duplicate AEGIS name on another id rejects the entry. The oracle's map.log shows no item_db
    parse errors, so every entry that gets this far is stored."""
    type_, version, min_version, location = 'ITEM_DB', 3, 1, 'db/item_db.yml'

    def __init__(self, ctx):
        super().__init__(ctx)
        self.aegis = {}   # lowered AEGIS name -> id

    def parse_body_node(self, node):
        ok, nameid = self.as_int(node, 'Id', 32, False)
        if not ok:
            return 0
        item = self.find(nameid)
        exists = item is not None
        if not exists:
            if not self.nodes_exist(node, ['AegisName', 'Name']):
                return 0
            item = {'id': nameid, 'name': ''}
        if node_exists(node, 'AegisName'):
            ok, name = self.as_string(node, 'AegisName')
            if not ok:
                return 0
            other = self.aegis.get(name.lower())
            if other is not None and other != nameid:
                self.warn('Found duplicate item Aegis name for %s, skipping.' % name)
                return 0
            if exists:
                self.aegis.pop(item['name'].lower(), None)
            item['name'] = name
            self.aegis[name.lower()] = nameid
        if not exists:
            self.put(nameid, item)
        return 1

    def exists(self, nameid):
        return nameid in self.data

    def search_aegisname(self, name):
        return self.aegis.get(name.lower())


def item_db(ctx):
    if '_item_ids' not in ctx:
        db = ItemIdDatabase(ctx)
        db.load()
        ctx['_item_ids'] = db
    return ctx['_item_ids']


# ============================================================================================ SkillDatabase

def skill_enum(ctx):
    """skill.hpp enum e_skill + mmo.hpp enum e_guild_skill: name -> id (for the ids skill.cpp names in code)."""
    out = {}
    for rel, ename in (('src/map/skill.hpp', 'e_skill'), ('src/common/mmo.hpp', 'e_guild_skill')):
        src = open(os.path.join(ctx['root'], rel), encoding='latin-1').read()
        m = re.search(r'enum\s+%s\s*(?::\s*\w+)?\s*\{(.*?)\};' % ename, src, re.S)
        body = re.sub(r'//[^\n]*', '', m.group(1))
        body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
        v = -1
        for part in body.split(','):
            part = part.strip()
            if not part:
                continue
            if '=' in part:
                n, e = [x.strip() for x in part.split('=', 1)]
                v = int(e, 0) if re.match(r'^(0x[0-9a-fA-F]+|\d+)$', e) else out[e]
            else:
                n = part
                v += 1
            out[n] = v
    return out


def new_skill(skill_id):
    """make_shared<s_skill_db>(): value-initialised (all zero)."""
    z = lambda: [0] * MAX_SKILL_LEVEL
    return {
        'nameid': skill_id, 'name': '', 'desc': '', 'range': z(), 'hit': 0, 'inf': 0, 'element': z(), 'nk': 0,
        'splash': z(), 'max': 0, 'num': z(), 'castcancel': False, 'cast_def_rate': 0, 'skill_type': 0,
        'blewcount': z(), 'inf2': 0, 'maxcount': z(), 'castnodex': 0, 'delaynodex': 0, 'nocast': 0, 'giveap': z(),
        'unit_id': 0, 'unit_id2': 0, 'unit_layout_type': z(), 'unit_range': z(), 'unit_interval': 0,
        'unit_target': 0, 'unit_flag': 0, 'cast': z(), 'delay': z(), 'walkdelay': z(), 'upkeep_time': z(),
        'upkeep_time2': z(), 'cooldown': z(), 'fixed_cast': z(),
        'require': {'hp': z(), 'mhp': z(), 'sp': z(), 'ap': z(), 'hp_rate': z(), 'sp_rate': z(), 'ap_rate': z(),
                    'zeny': z(), 'weapon': 0, 'ammo': 0, 'ammo_qty': z(), 'state': 0, 'spiritball': z(),
                    'itemid': [0] * MAX_SKILL_ITEM_REQUIRE, 'amount': [0] * MAX_SKILL_ITEM_REQUIRE, 'eq_item': [],
                    'status': [], 'itemid_level_dependent': False},
        'unit_nonearnpc_range': 0, 'unit_nonearnpc_type': 0,
        'damage': {'map': 0, 'caster': 0, 'rate': [0] * SKILLDMG_MAX},
        'copyable': {'option': 0, 'req_opt': 0}, 'sc': 0,
    }


class SkillDatabase(YamlDatabase):
    """skill.cpp SkillDatabase (TypesafeCachedYamlDatabase<uint16, s_skill_db>, "SKILL_DB" v4)."""
    type_, version, location = 'SKILL_DB', 4, 'db/skill_db.yml'

    def __init__(self, ctx, items, conf):
        super().__init__(ctx)
        self.items, self.conf = items, conf
        self.clear()

    def clear(self):
        """SkillDatabase::clear: the index table is zeroed and skill_num restarts at 1."""
        super().clear()
        self.id2idx = {}
        self.skill_num = 1
        self._names = None

    def put(self, key, v):
        super().put(key, v)
        self._names = None

    def const(self, name):
        return script_get_constant(self.ctx, name)

    def parse_node(self, node_name, sub_name, node, arr):
        """SkillDatabase::parseNode: a scalar fills every level; a Level list sets those levels, then extends the
        trend (the smallest step whose differences repeat back to the start) to MAX_SKILL_LEVEL, or repeats the last
        value. int32 arithmetic."""
        sk = child(node, node_name)
        S = len(arr)
        if not is_seq(sk):
            ok, value = self.as_int(node, node_name, 32, True)
            if not ok:
                return False
            for i in range(S):
                arr[i] = value
            return True
        max_level = 0
        for _, it in kids(sk):
            ok, skill_lv = self.as_int(it, 'Level', 16, False)
            if not ok:
                continue
            if skill_lv > MAX_SKILL_LEVEL:
                self.warn('%s Level exceeds the maximum skill level of %d, skipping.' % (node_name, MAX_SKILL_LEVEL))
                return False
            ok, value = self.as_int(it, sub_name, 32, True)
            if not ok:
                continue
            arr[skill_lv - 1] = value   # skill_lv 0 writes arr[-1] in rAthena (undefined); never in the data
            max_level = max(max_level, skill_lv)
        i = max_level
        step = 1
        while step <= i // 2:
            diff = wrap(arr[i - 1] - arr[i - step - 1], 32, True)
            j = i - 1
            while j >= step:
                if wrap(arr[j] - arr[j - step], 32, True) != diff:
                    break
                j -= 1
            if j >= step:
                step += 1
                continue
            while i < MAX_SKILL_LEVEL:
                arr[i] = wrap(arr[i - step] + diff, 32, True)
                if arr[i] < 1 and arr[i - 1] >= 0:
                    arr[i] = 1
                    diff = 0
                    step = 1
                i += 1
            return True
        while i < S:
            arr[i] = arr[max_level - 1]
            i += 1
        return True

    def parse_body_node(self, node):
        self._names = None
        ok, skill_id = self.as_int(node, 'Id', 16, False)
        if not ok:
            return 0
        skill = self.find(skill_id)
        exists = skill is not None
        if not exists:
            if not self.nodes_exist(node, ['Name', 'Description', 'MaxLevel']):
                return 0
            skill = new_skill(skill_id)
        C = self.const

        if node_exists(node, 'Name'):
            ok, name = self.as_string(node, 'Name')
            if not ok:
                return 0
            if len(name) > SKILL_NAME_LENGTH:
                self.warn('Name "%s" exceeds maximum length of %d.' % (name, SKILL_NAME_LENGTH))
                return 0
            skill['name'] = name   # memcpy of the name resized to 40 bytes: a 40-character name has no terminator
        if node_exists(node, 'Description'):
            ok, desc = self.as_string(node, 'Description')
            if not ok:
                return 0
            if len(desc) > SKILL_DESC_LENGTH:
                self.warn('Description "%s" exceeds maximum length of %d.' % (desc, SKILL_DESC_LENGTH))
                return 0
            skill['desc'] = desc
        if node_exists(node, 'MaxLevel'):
            ok, lv = self.as_int(node, 'MaxLevel', 16, False)
            if not ok:
                return 0
            if lv == 0 or lv > MAX_SKILL_LEVEL:
                self.warn('MaxLevel %d does not meet the bounds of 1~%d.' % (lv, MAX_SKILL_LEVEL))
                return 0
            skill['max'] = lv
        if node_exists(node, 'Type'):
            ok, t = self.as_string(node, 'Type')
            if not ok:
                return 0
            c = C('BF_' + t)
            if c is None:
                self.warn('Type %s is invalid.' % t)
                return 0
            if c < BF_NONE or c > BF_MISC:
                self.warn('Constant Type BF_%s is not a supported skill type.' % t)
                return 0
            skill['skill_type'] = wrap(c, 16, False)
        elif not exists:
            skill['skill_type'] = BF_NONE
        if node_exists(node, 'TargetType'):
            ok, inf = self.as_string(node, 'TargetType')
            if not ok:
                return 0
            c = C('INF_' + inf + '_SKILL')
            if c is None:
                self.warn('TargetType %s is invalid.' % inf)
                return 0
            skill['inf'] = wrap(c, 16, False)
        if node_exists(node, 'DamageFlags'):
            dn = child(node, 'DamageFlags')
            for name, _ in kids(dn):
                c = C('NK_' + name)
                if c is None:
                    self.warn('DamageFlags %s is invalid.' % name)
                    return 0
                ok, active = self.as_bool(dn, name)
                if not ok:
                    return 0
                bit = 1 << wrap(c, 8, False)
                skill['nk'] = skill['nk'] | bit if active else skill['nk'] & ~bit
        if node_exists(node, 'Flags'):
            fn = child(node, 'Flags')
            for name, _ in kids(fn):
                c = C('INF2_' + name)
                if c is None:
                    self.warn('Flag %s is invalid, skipping.' % name)
                    continue
                ok, active = self.as_bool(fn, name)
                if not ok:
                    return 0
                bit = 1 << wrap(c, 8, False)
                skill['inf2'] = skill['inf2'] | bit if active else skill['inf2'] & ~bit
        if node_exists(node, 'Range'):
            if not self.parse_node('Range', 'Size', node, skill['range']):
                return 0
        elif not exists:
            skill['range'][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(node, 'Hit'):
            ok, hit = self.as_string(node, 'Hit')
            if not ok:
                return 0
            c = C('DMG_' + hit)
            if c is None:
                self.warn('Hit %s is invalid.' % hit)
                return 0
            skill['hit'] = wrap(c, 8, False)
        elif not exists:
            skill['hit'] = DMG_NORMAL
        if node_exists(node, 'HitCount'):
            if not self.parse_node('HitCount', 'Count', node, skill['num']):
                return 0
        elif not exists:
            skill['num'][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(node, 'Element'):
            en = child(node, 'Element')
            if not is_seq(en):
                ok, element = self.as_string(node, 'Element')
                if not ok:
                    return 0
                c = C('ELE_' + element)
                if c is None:
                    self.warn('Element %s is invalid.' % element)
                    return 0
                if c == ELE_NONE:
                    self.warn('%s is not a valid element.' % element)
                    return 0
                skill['element'][:] = [wrap(c, 8, True)] * MAX_SKILL_LEVEL   # memset: every byte
            else:
                for _, it in kids(en):
                    ok, lv = self.as_int(it, 'Level', 16, False)
                    if not ok:
                        continue
                    if lv > MAX_SKILL_LEVEL:
                        self.warn('Element Level exceeds the maximum skill level of %d, skipping.' % MAX_SKILL_LEVEL)
                        return 0
                    ok, element = self.as_string(it, 'Element')
                    if not ok:
                        continue
                    c = C('ELE_' + element)
                    if c is None:
                        self.warn('Element %s is invalid.' % element)
                        return 0
                    if c == ELE_NONE:
                        self.warn('%s is not a valid element.' % element)
                        return 0
                    skill['element'][lv - 1] = wrap(c, 8, True)
        elif not exists:
            skill['element'][:] = [ELE_NEUTRAL] * MAX_SKILL_LEVEL
        for key, sub, field in (('SplashArea', 'Area', 'splash'), ('ActiveInstance', 'Max', 'maxcount'),
                                ('Knockback', 'Amount', 'blewcount')):
            if node_exists(node, key):
                if not self.parse_node(key, sub, node, skill[field]):
                    return 0
            elif not exists:
                skill[field][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(node, 'CopyFlags'):
            cn = child(node, 'CopyFlags')
            if node_exists(cn, 'Skill'):
                sn = child(cn, 'Skill')
                for key, bit in (('Plagiarism', SKILL_COPY_PLAGIARISM), ('Reproduce', SKILL_COPY_REPRODUCE)):
                    if node_exists(sn, key):
                        ok, active = self.as_bool(sn, key)
                        if not ok:
                            return 0
                        if active:
                            skill['copyable']['option'] |= bit
                        else:
                            skill['copyable']['option'] &= bit   # sic: rAthena keeps only this bit
            else:
                self.warn('CopyFlags requires a Skill copy type.')
                return 0
            if node_exists(cn, 'RemoveRequirement'):
                rn = child(cn, 'RemoveRequirement')
                for name, _ in kids(rn):
                    c = C('SKILL_REQ_' + name)
                    if c is None:
                        self.warn('CopyFlags RemoveRequirement %s is invalid.' % name)
                        return 0
                    skill['copyable']['req_opt'] = wrap(skill['copyable']['req_opt'] | c, 16, False)   # value ignored
            elif not exists:
                skill['copyable']['req_opt'] = 0
        if node_exists(node, 'NoNearNPC'):
            nn = child(node, 'NoNearNPC')
            if node_exists(nn, 'AdditionalRange'):
                ok, r = self.as_int(nn, 'AdditionalRange', 16, False)
                if not ok:
                    return 0
                skill['unit_nonearnpc_range'] = r
            elif not exists:
                skill['unit_nonearnpc_range'] = 0
            if node_exists(nn, 'Type'):
                tn = child(nn, 'Type')
                for name, _ in kids(tn):
                    c = C('SKILL_NONEAR_' + name)
                    if c is None:
                        self.warn('NoNearNPC Type %s is invalid.' % name)
                        return 0
                    ok, active = self.as_bool(tn, name)
                    if not ok:
                        return 0
                    v = skill['unit_nonearnpc_type']
                    skill['unit_nonearnpc_type'] = wrap(v | c if active else v & ~c, 16, False)
            elif not exists:
                skill['unit_nonearnpc_type'] = 0
        if node_exists(node, 'CastCancel'):
            ok, active = self.as_bool(node, 'CastCancel')
            if not ok:
                return 0
            skill['castcancel'] = active
        elif not exists:
            skill['castcancel'] = True
        if node_exists(node, 'CastDefenseReduction'):
            ok, r = self.as_int(node, 'CastDefenseReduction', 16, False)
            if not ok:
                return 0
            skill['cast_def_rate'] = r
        elif not exists:
            skill['cast_def_rate'] = 0
        for key, field in (('CastTime', 'cast'), ('AfterCastActDelay', 'delay'), ('AfterCastWalkDelay', 'walkdelay'),
                           ('Duration1', 'upkeep_time'), ('Duration2', 'upkeep_time2'), ('Cooldown', 'cooldown'),
                           ('FixedCastTime', 'fixed_cast')):   # FixedCastTime: RENEWAL_CAST
            if node_exists(node, key):
                if not self.parse_node(key, 'Time', node, skill[field]):
                    return 0
            elif not exists:
                skill[field][:] = [0] * MAX_SKILL_LEVEL
        for key, field in (('CastTimeFlags', 'castnodex'), ('CastDelayFlags', 'delaynodex')):
            if node_exists(node, key):
                fn = child(node, key)
                for name, _ in kids(fn):
                    c = C('SKILL_CAST_' + name)
                    if c is None:
                        self.warn('%s %s option is invalid.' % (key, name))
                        return 0
                    ok, active = self.as_bool(fn, name)
                    if not ok:
                        return 0
                    v = skill[field]
                    skill[field] = wrap(v | c if active else v & ~c, 8, False)
        if node_exists(node, 'Requires'):
            if not self.parse_requires(node, skill, exists):
                return 0
        if node_exists(node, 'GiveAp'):
            if not self.parse_node('GiveAp', 'Amount', node, skill['giveap']):
                return 0
        elif not exists:
            skill['giveap'][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(node, 'Unit'):
            if not self.parse_unit(node, skill, exists):
                return 0
        if node_exists(node, 'Status'):
            ok, status = self.as_string(node, 'Status')
            if not ok:
                return 0
            c = C('SC_' + status)
            if c is None:
                self.warn('Status %s is invalid.' % status)
                return 0
            if c < SC_NONE or c >= SC_MAX:
                self.warn('Status %s is unknown. Defaulting to SC_NONE.' % status)
                c = SC_NONE
            skill['sc'] = wrap(c, 16, True)
        elif not exists:
            skill['sc'] = SC_NONE
        if not exists:
            self.put(skill_id, skill)
            self.id2idx[skill_id] = self.skill_num
            self.skill_num = wrap(self.skill_num + 1, 16, False)
        return 1

    def parse_requires(self, node, skill, exists):
        rn = child(node, 'Requires')
        req = skill['require']
        C = self.const
        for key, field in (('HpCost', 'hp'), ('SpCost', 'sp'), ('ApCost', 'ap'), ('HpRateCost', 'hp_rate'),
                           ('SpRateCost', 'sp_rate'), ('ApRateCost', 'ap_rate'), ('MaxHpTrigger', 'mhp'),
                           ('ZenyCost', 'zeny')):
            if node_exists(rn, key):
                if not self.parse_node(key, 'Amount', rn, req[field]):
                    return False
            elif not exists:
                req[field][:] = [0] * MAX_SKILL_LEVEL
        for key, prefix, field, allkey in (('Weapon', 'W_', 'weapon', 'All'), ('Ammo', 'AMMO_', 'ammo', 'None')):
            if node_exists(rn, key):
                wn = child(rn, key)
                if node_exists(wn, allkey):
                    ok, active = self.as_bool(wn, allkey)
                    if not ok:
                        return False
                    if active:
                        req[field] = 0
                else:
                    for name, _ in kids(wn):
                        c = C(prefix + name)
                        if c is None:
                            self.warn('Requires %s %s is invalid.' % (key, name))
                            return False
                        ok, active = self.as_bool(wn, name)
                        if not ok:
                            return False
                        bit = wrap(1 << (c & 31), 32, True)
                        req[field] = wrap(req[field] | bit if active else req[field] & ~bit, 32, True)
            elif not exists:
                req[field] = 0
            if key == 'Ammo':
                if node_exists(rn, 'AmmoAmount'):
                    if req['ammo'] == 0:
                        self.warn('An ammo type is required before specifying ammo amount.')
                        return False
                    if not self.parse_node('AmmoAmount', 'Amount', rn, req['ammo_qty']):
                        return False
                elif not exists:
                    req['ammo_qty'][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(rn, 'State'):
            ok, state = self.as_string(rn, 'State')
            if not ok:
                return False
            c = C('ST_' + state)
            if c is None:
                self.warn('Requires State %s is invalid.' % state)
                return False
            req['state'] = wrap(c, 32, True)
        if node_exists(rn, 'Status'):
            sn = child(rn, 'Status')
            for name, _ in kids(sn):
                c = C('SC_' + name)
                if c is None:
                    self.warn('Requires Status %s is invalid.' % name)
                    return False
                ok, active = self.as_bool(sn, name)
                if not ok:
                    return False
                c = wrap(c, 16, True)
                if active and c not in req['status']:
                    req['status'].append(c)
                elif not active and c in req['status']:
                    req['status'].remove(c)
        if node_exists(rn, 'SpiritSphereCost'):
            if not self.parse_node('SpiritSphereCost', 'Amount', rn, req['spiritball']):
                return False
        elif not exists:
            req['spiritball'][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(rn, 'ItemCost'):
            count = 0
            for _, it in kids(child(rn, 'ItemCost')):
                ok, item_name = self.as_string(it, 'Item')
                if not ok:
                    continue
                nameid = self.items.search_aegisname(item_name)
                if nameid is None:
                    self.warn('Requires ItemCost Item %s does not exist.' % item_name)
                    return False
                ok, amount = self.as_int(it, 'Amount', 32, True)
                if not ok:
                    continue
                if node_exists(it, 'Level'):
                    ok, cost_level = self.as_int(it, 'Level', 16, False)
                    if not ok:
                        continue
                    if cost_level < 1 or cost_level > skill['max']:
                        self.warn('Requires ItemCost Level %d is not within %s\'s level range of 1~%d.' % (cost_level, skill['name'], skill['max']))
                        return False
                    count = cost_level - 1
                    req['itemid_level_dependent'] = True
                req['itemid'][count] = nameid
                req['amount'][count] = amount
                count += 1
        if node_exists(rn, 'Equipment'):
            en = child(rn, 'Equipment')
            for name, _ in kids(en):
                nameid = self.items.search_aegisname(name)
                if nameid is None:
                    self.warn('Requires Equipment %s does not exist.' % name)
                    return False
                ok, active = self.as_bool(en, name)
                if not ok:
                    return False
                if active and nameid not in req['eq_item']:
                    req['eq_item'].append(nameid)
                elif not active and nameid in req['eq_item']:
                    req['eq_item'].remove(nameid)
        return True

    def parse_unit(self, node, skill, exists):
        un = child(node, 'Unit')
        C = self.const
        if node_exists(un, 'Id'):
            ok, unit = self.as_string(un, 'Id')
            if not ok:
                return False
            c = C('UNT_' + unit)
            if c is None:
                self.warn('Unit Id %s is invalid.' % unit)
                return False
            skill['unit_id'] = wrap(c, 16, False)
        else:
            self.warn('Unit requires an Id.')
            return False
        if node_exists(un, 'AlternateId'):
            ok, unit = self.as_string(un, 'AlternateId')
            if not ok:
                return False
            c = C('UNT_' + unit)
            if c is None:
                self.warn('Alternate Unit Id %s is invalid.' % unit)
                return False
            skill['unit_id2'] = wrap(c, 16, False)
        elif not exists:
            skill['unit_id2'] = 0
        for key, field in (('Layout', 'unit_layout_type'), ('Range', 'unit_range')):
            if node_exists(un, key):
                if not self.parse_node(key, 'Size', un, skill[field]):
                    return False
            elif not exists:
                skill[field][:] = [0] * MAX_SKILL_LEVEL
        if node_exists(un, 'Interval'):
            ok, interval = self.as_int(un, 'Interval', 16, True)
            if not ok:
                return False
            skill['unit_interval'] = interval
        elif not exists:
            skill['unit_interval'] = 0
        if node_exists(un, 'Target'):
            ok, target = self.as_string(un, 'Target')
            if not ok:
                return False
            c = C('BCT_' + target)
            if c is None:
                self.warn('Unit Target %s is invalid.' % target)
                return False
            skill['unit_target'] = wrap(c, 32, True)
        elif not exists:
            skill['unit_target'] = BCT_ALL
        if node_exists(un, 'Flag'):
            fn = child(un, 'Flag')
            for name, _ in kids(fn):
                c = C('UF_' + name)
                if c is None:
                    self.warn('Skill Unit Flag %s is invalid.' % name)
                    return False
                ok, active = self.as_bool(fn, name)
                if not ok:
                    return False
                bit = 1 << wrap(c, 8, False)
                skill['unit_flag'] = skill['unit_flag'] | bit if active else skill['unit_flag'] & ~bit
            if skill['unit_flag'] >> UF_NOENEMY & 1 and self.conf['defunit_not_enemy']:
                skill['unit_target'] = BCT_NOENEMY
            skill['unit_target'] |= BL_CHAR
            if skill['unit_flag'] >> UF_NOPC & 1:
                skill['unit_target'] &= ~BL_PC
            if skill['unit_flag'] >> UF_NOMOB & 1:
                skill['unit_target'] &= ~BL_MOB
            if skill['unit_flag'] >> UF_SKILL & 1:
                skill['unit_target'] |= BL_SKILL
        elif not exists:
            skill['unit_flag'] = 0
            skill['unit_target'] |= BL_CHAR
        return True

    def loading_finished(self):
        if self.skill_num > MAX_SKILL:
            self.warn('There are more skills defined in the skill database (%d) than the MAX_SKILL (%d) define.' % (self.skill_num, MAX_SKILL))

    # --- lookups the other loaders use
    def name2id(self, name):
        """skill_name2id: first skill (in skill_db iteration order) whose AEGIS name matches case-insensitively."""
        if name is None:
            return 0
        if self._names is None:   # the first match in iteration order, precomputed (skill_db is not changed after load)
            self._names = {}
            for sid, sk in self.data.items():
                self._names.setdefault(sk['name'].lower(), sid)
        return self._names.get(name.lower(), 0)

    def get_index(self, skill_id):
        return self.id2idx.get(skill_id, 0)

    def check(self, skill_id):
        return skill_id != 0 and self.get_index(skill_id) != 0

    def get_inf(self, skill_id):
        return self.data[skill_id]['inf'] if self.check(skill_id) else 0

    def get_max(self, skill_id):
        return self.data[skill_id]['max'] if self.check(skill_id) else 0


# ============================================================================================ text databases

def read_text_dbs(ctx, sdb, items, e, log):
    """skill_readdb's sv_readdb loop: for "" then "/import": skill_nocast_db.txt and produce_db.txt from db/re/ (then
    db/import/), skill_changematerial_db.txt and skill_damage_db.txt from db/ (then db/import/)."""
    produce = [None] * MAX_SKILL_PRODUCE_DB
    changemat = [None] * MAX_SKILL_CHANGEMATERIAL_DB
    state = {'produce_count': 0, 'changematerial_count': 0}
    counts = []

    def blank_produce():
        return {'nameid': 0, 'req_skill': 0, 'req_skill_lv': 0, 'itemlv': 0,
                'mat_id': [0] * MAX_PRODUCE_RESOURCE, 'mat_amount': [0] * MAX_PRODUCE_RESOURCE}

    def blank_changemat():
        return {'nameid': 0, 'rate': 0, 'qty': [0] * MAX_SKILL_CHANGEMATERIAL_SET, 'qty_rate': [0] * MAX_SKILL_CHANGEMATERIAL_SET}

    def nocast(split, columns, current):
        skill = sdb.find(wrap(c_atoi(split[0]), 16, False))
        if skill is None:
            return False
        skill['nocast'] = wrap(skill['nocast'] | wrap(c_atoi(split[1]), 32, False), 32, False)
        return True

    def producedb(split, columns, current):
        pid = wrap(c_atoi(split[0]), 16, False)
        if pid >= MAX_SKILL_PRODUCE_DB:
            log.append('skill_parse_row_producedb: Maximum db entries reached.')
            return False
        produce[pid] = blank_produce()
        nameid = c_strtoul(split[1])
        if not nameid:
            return True
        if not items.exists(nameid):
            log.append('skill_parse_row_producedb: Invalid item %u.' % nameid)
            return False
        p = produce[pid]
        p['nameid'] = nameid
        p['itemlv'] = wrap(c_atoi(split[2]), 8, False)
        p['req_skill'] = wrap(c_atoi(split[3]), 16, False)
        p['req_skill_lv'] = wrap(c_atoi(split[4]), 8, False)
        x, y = 5, 0
        while x + 1 < columns and y < MAX_PRODUCE_RESOURCE:
            p['mat_id'][y] = c_strtoul(split[x])
            p['mat_amount'][y] = wrap(c_atoi(split[x + 1]), 16, False)
            x += 2
            y += 1
        state['produce_count'] += 1   # `found` is never set: every stored row counts
        return True

    def changematerialdb(split, columns, current):
        cid = wrap(c_atoi(split[0]), 16, False)
        nameid = c_strtoul(split[1])
        rate = wrap(c_atoi(split[2]), 16, True)
        found = False
        if cid >= MAX_SKILL_CHANGEMATERIAL_DB:
            log.append('skill_parse_row_changematerialdb: Maximum amount of entries reached')
            return False
        if changemat[cid] is not None and changemat[cid]['nameid'] > 0:
            found = True
            changemat[cid] = blank_changemat()
        if nameid == 0:
            changemat[cid] = blank_changemat()
            return True
        if not any(p is not None and p['nameid'] == nameid and p['req_skill'] == e['GN_CHANGEMATERIAL'] for p in produce):
            log.append('skill_parse_row_changematerialdb: Not supported item ID (%u) for Change Material.' % nameid)
            return False
        if changemat[cid] is None:
            changemat[cid] = blank_changemat()
        c = changemat[cid]
        c['nameid'] = nameid
        c['rate'] = wrap(rate, 16, False)
        x, y = 3, 0
        while x + 1 < columns and y < MAX_SKILL_CHANGEMATERIAL_SET:
            c['qty'][y] = wrap(c_atoi(split[x]), 16, False)
            c['qty_rate'][y] = wrap(c_atoi(split[x + 1]), 16, False)
            x += 2
            y += 1
        if not found:
            state['changematerial_count'] += 1
        return True

    def skilldamage(split, columns, current):
        s0 = split[0].strip()
        if s0[:1].isdigit():
            value, rest = c_strtol(s0, 10)
            if rest:
                log.append('skill_parse_row_skilldamage: Invalid skill %s given, skipping.' % rest)
                return False
            sid = wrap(value, 16, False)
        else:
            sid = sdb.name2id(s0)
        skill = sdb.find(sid)
        if skill is None:
            return False
        skill['damage'] = {'map': 0, 'caster': 0, 'rate': [0] * SKILLDMG_MAX}
        s1 = split[1].strip()
        if s1[:1].isdigit():
            value, rest = c_strtol(s1, 10)
            if rest:
                log.append('skill_parse_row_skilldamage: Invalid caster %s given for skill %d, skipping.' % (rest, sid))
                return False
            caster = wrap(value, 32, True)
        else:
            c = script_get_constant(ctx, s1)
            if c is None:
                log.append('skill_parse_row_skilldamage: Invalid caster constant given for skill %d, skipping.' % sid)
                return False
            caster = wrap(c, 16, False)
        skill['damage']['caster'] = wrap(skill['damage']['caster'] | caster, 16, False)
        value, rest = c_strtol(split[2], 10)
        if rest:
            log.append('skill_parse_row_skilldamage: Invalid map %s given for skill %d, skipping.' % (rest, sid))
            return False
        skill['damage']['map'] = wrap(skill['damage']['map'] | wrap(value, 32, True), 32, False)
        offset, i = 3, 0
        while i < SKILLDMG_MAX and offset < columns:
            value, rest = c_strtol(split[offset], 10)
            value = wrap(value, 32, True)
            if rest and rest[0] != ' ':
                log.append('skill_parse_row_skilldamage: Invalid damage %s given for skill %d, defaulting to 0.' % (rest, sid))
                value = 0
            skill['damage']['rate'][i] = max(-100, min(100000, value))
            offset += 1
            i += 1
        return True

    for i, sub in enumerate(('', '/import')):
        d1 = 'db' + sub
        d2 = 'db/re' if i == 0 else 'db' + sub
        for d, fn, mn, mx, rows, proc in ((d2, 'skill_nocast_db.txt', 2, 2, -1, nocast),
                                          (d2, 'produce_db.txt', 5, 5 + 2 * MAX_PRODUCE_RESOURCE, MAX_SKILL_PRODUCE_DB, producedb),
                                          (d1, 'skill_changematerial_db.txt', 5, 5 + 2 * MAX_SKILL_CHANGEMATERIAL_SET, MAX_SKILL_CHANGEMATERIAL_DB, changematerialdb),
                                          (d1, 'skill_damage_db.txt', 4, 3 + SKILLDMG_MAX, -1, skilldamage)):
            n = sv_readdb(ctx, d, fn, ',', mn, mx, rows, proc, i > 0, log)
            if n is not None:
                counts.append(('%s/%s' % (d, fn), n))
    return produce, changemat, counts


# ============================================================================================ small YAML databases

class AbraDatabase(YamlDatabase):
    type_, location = 'ABRA_DB', 'db/abra_db.yml'

    def __init__(self, ctx, sdb):
        super().__init__(ctx)
        self.sdb = sdb

    def parse_body_node(self, node):
        ok, name = self.as_string(node, 'Skill')
        if not ok:
            return 0
        sid = self.sdb.name2id(name)
        if not sid:
            self.warn('Invalid Abra skill name "%s", skipping.' % name)
            return 0
        if not self.sdb.get_inf(sid):
            self.warn('Passive skill %s cannot be casted by Abra.' % name)
            return 0
        abra = self.find(sid)
        exists = abra is not None
        if not exists:
            abra = {'skill_id': sid, 'per': [0] * MAX_SKILL_LEVEL}
        if node_exists(node, 'Probability'):
            pn = child(node, 'Probability')
            if not is_seq(pn):
                # rAthena reads "Probability" as a child of the scalar node itself, which never exists: a scalar
                # Probability always fails the entry (asUInt16Rate -> "Missing node").
                ok, prob = self.as_uint16_rate(pn, 'Probability')
                if not ok:
                    return 0
                abra['per'] = [prob] * MAX_SKILL_LEVEL
            else:
                abra['per'] = [0] * MAX_SKILL_LEVEL
                for _, it in kids(pn):
                    ok, lv = self.as_int(it, 'Level', 16, False)
                    if not ok:
                        continue
                    if lv > MAX_SKILL_LEVEL:
                        self.warn('Probability Level exceeds the maximum skill level of %d, skipping.' % MAX_SKILL_LEVEL)
                        return 0
                    ok, prob = self.as_uint16_rate(it, 'Probability')
                    if not ok:
                        continue
                    abra['per'][lv - 1] = prob
        elif not exists:
            abra['per'] = [500] * MAX_SKILL_LEVEL
        if not exists:
            self.put(sid, abra)
        return 1


class MagicMushroomDatabase(YamlDatabase):
    type_, location = 'MAGIC_MUSHROOM_DB', 'db/magicmushroom_db.yml'

    def __init__(self, ctx, sdb):
        super().__init__(ctx)
        self.sdb = sdb

    def parse_body_node(self, node):
        ok, name = self.as_string(node, 'Skill')
        if not ok:
            return 0
        sid = self.sdb.name2id(name)
        if not sid:
            self.warn('Invalid Magic Mushroom skill name "%s", skipping.' % name)
            return 0
        if not self.sdb.get_inf(sid):
            self.warn('Passive skill %s cannot be casted by Magic Mushroom.' % name)
            return 0
        if self.find(sid) is None:
            self.put(sid, {'skill_id': sid})
        return 1


class ReadingSpellbookDatabase(YamlDatabase):
    type_, location = 'READING_SPELLBOOK_DB', 'db/spellbook_db.yml'

    def __init__(self, ctx, sdb, items):
        super().__init__(ctx)
        self.sdb, self.items = sdb, items

    def parse_body_node(self, node):
        ok, name = self.as_string(node, 'Skill')
        if not ok:
            return 0
        sid = self.sdb.name2id(name)
        if sid == 0:
            self.warn('Invalid skill name "%s", skipping.' % name)
            return 0
        if self.sdb.get_inf(sid) == INF_PASSIVE_SKILL:
            self.warn('Passive skill %s cannot be memorized in a Spell Book.' % name)
            return 0
        spell = self.find(sid)
        exists = spell is not None
        if not exists:
            if not self.nodes_exist(node, ['Book', 'PreservePoints']):
                return 0
            spell = {'skill_id': sid, 'points': 0, 'nameid': 0}
        if node_exists(node, 'Book'):
            ok, book = self.as_string(node, 'Book')
            if not ok:
                return 0
            nameid = self.items.search_aegisname(book)
            if nameid is None:
                self.warn('Book item %s does not exist.' % book)
                return 0
            spell['nameid'] = nameid
        if node_exists(node, 'PreservePoints'):
            ok, pts = self.as_int(node, 'PreservePoints', 16, False)
            if not ok:
                return 0
            spell['points'] = pts
        if not exists:
            self.put(sid, spell)
        return 1


class SkillArrowDatabase(YamlDatabase):
    type_, location = 'CREATE_ARROW_DB', 'db/create_arrow_db.yml'

    def __init__(self, ctx, items):
        super().__init__(ctx)
        self.items = items

    def parse_body_node(self, node):
        ok, source = self.as_string(node, 'Source')
        if not ok:
            return 0
        nameid = self.items.search_aegisname(source)
        if nameid is None:
            self.warn('Item %s does not exist.' % source)
            return 0
        arrow = self.find(nameid)
        exists = arrow is not None
        if not exists:
            arrow = {'nameid': nameid, 'created': UMap()}
        make = child(node, 'Make') if is_map(node) else None
        for _, it in kids(make) if make is not None else []:
            ok, item_name = self.as_string(it, 'Item')
            if not ok:
                return 0
            iid = self.items.search_aegisname(item_name)
            if iid is None:
                self.warn('Item %s does not exist.' % item_name)
                return 0
            ok, amount = self.as_int(it, 'Amount', 16, False)
            if not ok:
                return 0
            if amount == 0:
                if arrow['created'].erase(iid) == 0:
                    self.warn('Failed to remove %s, the entry doesn\'t exist in Source %s.' % (item_name, source))
                continue
            if amount > MAX_AMOUNT:
                self.warn('Amount %d exceeds %d, skipping.' % (amount, MAX_AMOUNT))
                continue
            arrow['created'][iid] = amount
        if not exists:
            self.put(nameid, arrow)
        return 1


class GuildSkillTreeDatabase(YamlDatabase):
    """guild.cpp GuildSkillTreeDatabase ("GUILD_SKILL_TREE_DB" v1)."""
    type_, location = 'GUILD_SKILL_TREE_DB', 'db/guild_skill_tree.yml'

    def __init__(self, ctx, sdb, conf, e):
        super().__init__(ctx)
        self.sdb, self.conf, self.e = sdb, conf, e

    def chk_guild(self, sid):
        return GD_SKILLBASE <= sid < GD_SKILLBASE + MAX_GUILDSKILL

    def parse_body_node(self, node):
        ok, name = self.as_string(node, 'Id')
        if not ok:
            return 0
        sid = self.sdb.name2id(name)
        if not sid:
            self.warn('Invalid guild skill name "%s", skipping.' % name)
            return 0
        if not self.chk_guild(sid):
            self.warn('Guild skill "%s" with Id %u is out of the guild skill range, skipping.' % (name, sid))
            return 0
        skill = self.find(sid)
        exists = skill is not None
        if not exists:
            if not node_exists(node, 'MaxLevel'):
                self.warn('Missing node "MaxLevel", skipping.')
                return 0
            skill = {'id': sid, 'max': 0, 'need': UMap()}
        if node_exists(node, 'MaxLevel'):
            ok, level = self.as_int(node, 'MaxLevel', 16, False)
            if not ok:
                return 0
            if sid == self.e['GD_GLORYGUILD'] and self.conf['require_glory_guild'] and level == 0:
                level = 1
            skill['max'] = level
        if node_exists(node, 'Required'):
            for _, rn in kids(child(node, 'Required')):
                ok, rname = self.as_string(rn, 'Id')
                if not ok:
                    return 0
                rid = self.sdb.name2id(rname)
                if not rid:
                    self.warn('Invalid required guild skill name "%s", skipping.' % rname)
                    return 0
                if not self.chk_guild(rid):
                    self.warn('Required guild skill "%s" with Id %u is out of the guild skill range, skipping.' % (rname, rid))
                    return 0
                req = skill['need'].get(rid)
                req_exists = req is not None
                if not req_exists:
                    if not node_exists(rn, 'Level'):
                        self.warn('Missing node "Level", skipping.')
                        return 0
                    req = {'id': rid, 'lv': 0}
                if node_exists(rn, 'Level'):
                    ok, rlv = self.as_int(rn, 'Level', 16, False)
                    if not ok:
                        return 0
                    if rlv == 0:
                        continue   # a new requirement is dropped, an existing one is left as it was
                    req['lv'] = rlv
                if not req_exists:
                    skill['need'][rid] = req
        if not exists:
            self.put(sid, skill)
        return 1


# ============================================================================================ skill_init_unit_layout

def unit_layouts(sdb, e):
    """skill.cpp skill_init_unit_layout / skill_init_nounit_layout. Special layouts are handed out in skill_db
    iteration order (libstdc++ unordered_map order, reproduced by UMap) and written back into the skill's
    unit_layout_type for every level."""
    lay = [{'count': 0, 'dx': [], 'dy': []} for _ in range(MAX_SKILL_UNIT_LAYOUT)]

    def put(pos, dx, dy):
        lay[pos] = {'count': len(dx), 'dx': list(dx), 'dy': list(dy)}

    for i in range(MAX_SQUARE_LAYOUT + 1):
        size = i * 2 + 1
        put(i, [j % size - i for j in range(size * size)], [j // size - i for j in range(size * size)])
    pos = MAX_SQUARE_LAYOUT + 1
    special = {
        'PR_SANCTUARY': ([-1, 0, 1, -2, -1, 0, 1, 2, -2, -1, 0, 1, 2, -2, -1, 0, 1, 2, -1, 0, 1],
                         [-2, -2, -2, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2]),
        'PR_MAGNUS': ([-1, 0, 1, -1, 0, 1, -3, -2, -1, 0, 1, 2, 3, -3, -2, -1, 0, 1, 2, 3, -3, -2, -1, 0, 1, 2, 3, -1, 0, 1, -1, 0, 1],
                      [-3, -3, -3, -2, -2, -2, -1, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3]),
        'AS_VENOMDUST': ([-1, 0, 0, 0, 1], [0, -1, 0, 1, 0]),
        'CR_GRANDCROSS': ([0, 0, -1, 0, 1, -2, -1, 0, 1, 2, -4, -3, -2, -1, 0, 1, 2, 3, 4, -2, -1, 0, 1, 2, -1, 0, 1, 0, 0],
                          [-4, -3, -2, -2, -2, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 3, 4]),
        'PF_FOGWALL': ([-2, -1, 0, 1, 2, -2, -1, 0, 1, 2, -2, -1, 0, 1, 2], [-1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1]),
        'PA_GOSPEL': ([-1, 0, 1, -1, 0, 1, -3, -2, -1, 0, 1, 2, 3, -3, -2, -1, 0, 1, 2, 3, -3, -2, -1, 0, 1, 2, 3, -1, 0, 1, -1, 0, 1],
                      [-3, -3, -3, -2, -2, -2, -1, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3]),
        'NJ_KAENSIN': ([-2, -1, 0, 1, 2, -2, -1, 0, 1, 2, -2, -1, 1, 2, -2, -1, 0, 1, 2, -2, -1, 0, 1, 2],
                       [2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 0, 0, 0, 0, -1, -1, -1, -1, -1, -2, -2, -2, -2, -2]),
        'GN_WALLOFTHORN': ([-1, -2, -2, -2, -2, -2, -1, 0, 1, 2, 2, 2, 2, 2, 1, 0], [2, 2, 1, 0, -1, -2, -2, -2, -2, -2, -1, 0, 1, 2, 2, 2]),
        'NPC_FLAMECROSS': ([-2, -1, 1, 2, 0, 0, 0, 0], [0, 0, 0, 0, -2, -1, 1, 2]),
    }
    special['NPC_EVILLAND'] = special['PR_SANCTUARY']
    special['NPC_GRANDDARKNESS'] = special['CR_GRANDCROSS']
    by_id = {e[k]: v for k, v in special.items()}
    later = {e['MG_FIREWALL'], e['WZ_ICEWALL'], e['WL_EARTHSTRAIN'], e['RL_FIRE_RAIN']}
    log = []
    for sid, skill in sdb.data.items():
        if not skill['unit_id'] or skill['unit_layout_type'][0] != -1:
            continue
        if sid == e['EL_FIRE_MANTLE']:
            put(pos, [-1, 0, 1, 1, 1, 0, -1, -1], [1, 1, 1, 0, -1, -1, -1, 0])
        elif sid in later:
            pass
        elif sid in by_id:
            put(pos, *by_id[sid])
        elif sid == e['NJ_TATAMIGAESHI']:
            put(pos, [-1, 1, 0, 0], [0, 0, -1, 1])
            skill['unit_layout_type'][0] = pos
            pos += 1
            put(pos, [-2, -1, 1, 2, 0, 0, 0, 0], [0, 0, 0, 0, -2, -1, 1, 2])
            skill['unit_layout_type'][1] = pos
            skill['unit_layout_type'][2] = pos
            pos += 1
            put(pos, [-3, -2, -1, 1, 2, 3, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, -3, -2, -1, 1, 2, 3])
            for j in range(3, MAX_SKILL_LEVEL):
                skill['unit_layout_type'][j] = pos
            pos += 1
        else:
            log.append('unknown unit layout at skill %d' % sid)
        if not lay[pos]['count']:
            continue
        skill['unit_layout_type'][:] = [pos] * MAX_SKILL_LEVEL
        pos += 1
    firewall = pos
    for i in range(8):
        if i & 1:
            put(pos, [-1, -1, 0, 0, 1] if i & 2 else [1, 1, 0, 0, -1], [1, 0, 0, -1, -1])
        else:
            put(pos, [-1, 0, 1], [0, 0, 0]) if i % 4 == 0 else put(pos, [0, 0, 0], [-1, 0, 1])
        pos += 1
    icewall = pos
    for i in range(8):
        if i & 1:
            put(pos, [-2, -1, 0, 1, 2] if i & 2 else [2, 1, 0, -1, -2], [2, 1, 0, -1, -2])
        else:
            put(pos, [-2, -1, 0, 1, 2], [0] * 5) if i % 4 == 0 else put(pos, [0] * 5, [-2, -1, 0, 1, 2])
        pos += 1
    earthstrain = pos
    for i in range(8):
        line = list(range(-7, 8))
        put(pos, [0] * 15, line) if i in (2, 6) else put(pos, line, [0] * 15)
        pos += 1
    firerain = pos
    for i in range(8):
        put(pos, [0, 0, 0], [-1, 0, 1]) if i in (2, 6) else put(pos, [-1, 0, 1], [0, 0, 0])
        pos += 1
    if pos >= MAX_SKILL_UNIT_LAYOUT:
        log.append('skill_init_unit_layout: The skill_unit_layout has met the limit or overflowed (pos=%d)' % pos)

    # skill_init_nounit_layout: 8 directions of a 21/33-cell field, then 8 of a 44/74-cell field
    nolay = []
    for i in range(8):
        if i & 1:
            if i & 2:
                if i & 4:
                    dx = [5, 6, 7, 5, 6, 4, 5, 6, 4, 5, 3, 4, 5, 3, 4, 2, 3, 4, 2, 3, 1, 2, 3, 1, 2, 0, 1, 2, 0, 1, -1, 0, 1]
                    dy = [7, 6, 5, 6, 5, 6, 5, 4, 5, 4, 5, 4, 3, 4, 3, 4, 3, 2, 3, 2, 3, 2, 1, 2, 1, 2, 1, 0, 1, 0, 1, 0, -1]
                else:
                    dx = [-5, -6, -7, -5, -6, -4, -5, -6, -4, -5, -3, -4, -5, -3, -4, -2, -3, -4, -2, -3, -1, -2, -3, -1, -2, 0, -1, -2, 0, -1, 1, 0, -1]
                    dy = [-7, -6, -5, -6, -5, -6, -5, -4, -5, -4, -5, -4, -3, -4, -3, -4, -3, -2, -3, -2, -3, -2, -1, -2, -1, -2, -1, 0, -1, 0, -1, 0, 1]
            else:
                if i & 4:
                    dx = [7, 6, 5, 6, 5, 6, 5, 4, 5, 4, 5, 4, 3, 4, 3, 4, 3, 2, 3, 2, 3, 2, 1, 2, 1, 2, 1, 0, 1, 0, 1, 0, -1]
                    dy = [-5, -6, -7, -5, -6, -4, -5, -6, -4, -5, -3, -4, -5, -3, -4, -2, -3, -4, -2, -3, -1, -2, -3, -1, -2, 0, -1, -2, 0, -1, 1, 0, -1]
                else:
                    dx = [-7, -6, -5, -6, -5, -6, -5, -4, -5, -4, -5, -4, -3, -4, -3, -4, -3, -2, -3, -2, -3, -2, -1, -2, -1, -2, -1, 0, -1, 0, -1, 0, 1]
                    dy = [5, 6, 7, 5, 6, 4, 5, 6, 4, 5, 3, 4, 5, 3, 4, 2, 3, 4, 2, 3, 1, 2, 3, 1, 2, 0, 1, 2, 0, 1, -1, 0, 1]
        else:
            if i & 2:
                if i & 4:
                    dx = [0, 1, 2, 3, 4, 5, 6] * 3
                    dy = [1] * 7 + [0] * 7 + [-1] * 7
                else:
                    dx = [-6, -5, -4, -3, -2, -1, 0] * 3
                    dy = [1] * 7 + [0] * 7 + [-1] * 7
            else:
                if i & 4:
                    dx = [-1, 0, 1] * 7
                    dy = [0, 0, 0, -1, -1, -1, -2, -2, -2, -3, -3, -3, -4, -4, -4, -5, -5, -5, -6, -6, -6]
                else:
                    dx = [-1, 0, 1] * 7
                    dy = [6, 6, 6, 5, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 1, 1, 1, 0, 0, 0]
        nolay.append({'count': len(dx), 'dx': dx, 'dy': dy})
    for i in range(8):
        if i & 1:
            if i & 2:
                if i & 4:
                    dx = [-2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7,
                          -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6,
                          -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5,
                          -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5]
                    dy = [8, 7, 6, 5, 4, 3, 2, 1, 0, -1, -2, 7, 6, 5, 4, 3, 2, 1, 0, -1, -2,
                          7, 6, 5, 4, 3, 2, 1, 0, -1, -2, -3, 6, 5, 4, 3, 2, 1, 0, -1, -2, -3,
                          6, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4,
                          5, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5]
                else:
                    dx = [2, 1, 0, -1, -2, -3, -4, -5, -6, -7, -8, 2, 1, 0, -1, -2, -3, -4, -5, -6, -7,
                          3, 2, 1, 0, -1, -2, -3, -4, -5, -6, -7, 3, 2, 1, 0, -1, -2, -3, -4, -5, -6,
                          4, 3, 2, 1, 0, -1, -2, -3, -4, -5, -6, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5,
                          5, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5]
                    dy = [-8, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2,
                          -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3,
                          -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4,
                          -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5]
            else:
                if i & 4:
                    dx = [8, 7, 6, 5, 4, 3, 2, 1, 0, -1, -2, 7, 6, 5, 4, 3, 2, 1, 0, -1, -2,
                          7, 6, 5, 4, 3, 2, 1, 0, -1, -2, -3, 6, 5, 4, 3, 2, 1, 0, -1, -2, -3,
                          6, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4,
                          5, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5]
                    dy = [2, 1, 0, -1, -2, -3, -4, -5, -6, -7, -8, 2, 1, 0, -1, -2, -3, -4, -5, -6, -7,
                          3, 2, 1, 0, -1, -2, -3, -4, -5, -6, -7, 3, 2, 1, 0, -1, -2, -3, -4, -5, -6,
                          4, 3, 2, 1, 0, -1, -2, -3, -4, -5, -6, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5,
                          5, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5]
                else:
                    dx = [-8, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2,
                          -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3,
                          -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4,
                          -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5]
                    dy = [-2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7,
                          -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6,
                          -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5,
                          -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5]
        else:
            if i & 2:
                if i & 4:
                    dx = [0, 1, 2, 3] * 11
                    dy = [v for v in range(5, -6, -1) for _ in range(4)]
                else:
                    dx = [-3, -2, -1, 0] * 11
                    dy = [v for v in range(5, -6, -1) for _ in range(4)]
            else:
                if i & 4:
                    dx = [5, 4, 3, 2, 1, 0, -1, -2, -3, -4, -5] * 4
                    dy = [v for v in (-3, -2, -1, 0) for _ in range(11)]
                else:
                    dx = [-5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5] * 4
                    dy = [v for v in (3, 2, 1, 0) for _ in range(11)]
        nolay.append({'count': len(dx), 'dx': dx, 'dy': dy})
    while len(nolay) < MAX_SKILL_UNIT_LAYOUT2:
        nolay.append({'count': 0, 'dx': [], 'dy': []})
    return {'layout': lay, 'firewall_unit_pos': firewall, 'icewall_unit_pos': icewall,
            'earthstrain_unit_pos': earthstrain, 'firerain_unit_pos': firerain, 'wallofthorn_unit_pos': 0,
            'nounit_layout': nolay}, log


# ============================================================================================ driver

def load_all(ctx):
    """skill_readdb (do_init_skill) then guild_skill_tree_db.load (do_init_guild); cached in ctx for jobs.py."""
    if '_skills' in ctx:
        return ctx['_skills']
    items = item_db(ctx)
    conf = battle_config(ctx)
    e = skill_enum(ctx)
    sdb = SkillDatabase(ctx, items, conf)
    sdb.load()
    log = []
    produce, changemat, txt_counts = read_text_dbs(ctx, sdb, items, e, log)
    abra = AbraDatabase(ctx, sdb)
    abra.load()
    mushroom = MagicMushroomDatabase(ctx, sdb)
    mushroom.load()
    spellbook = ReadingSpellbookDatabase(ctx, sdb, items)
    spellbook.load()
    arrow = SkillArrowDatabase(ctx, items)
    arrow.load()
    layouts, lay_log = unit_layouts(sdb, e)
    guild = GuildSkillTreeDatabase(ctx, sdb, conf, e)
    guild.load()
    st = {'items': items, 'conf': conf, 'enum': e, 'sdb': sdb, 'produce': produce, 'changemat': changemat,
          'txt_counts': txt_counts, 'abra': abra, 'mushroom': mushroom, 'spellbook': spellbook, 'arrow': arrow,
          'layouts': layouts, 'guild': guild, 'log': log + lay_log}
    ctx['_skills'] = st
    return st


def report(name, db):
    for f, n in db.counts:
        print("  %s: Done reading '%d' entries in '%s'" % (name, n, f))
    for w in db.warnings[:20]:
        print('  %s warning: %s' % (name, w))


def build(ctx):
    st = load_all(ctx)
    sdb = st['sdb']
    for name in ('sdb', 'abra', 'mushroom', 'spellbook', 'arrow', 'guild'):
        report(name, st[name])
    for f, n in st['txt_counts']:
        print("  Done reading '%d' entries in '%s'." % (n, f))
    for l in st['log'][:20]:
        print('  ' + l)

    # skill_db: the entries in skill_db iteration order; `index` is skilldb_id2idx.
    skills = []
    for sid, s in sdb.data.items():
        r = dict(s)
        r['index'] = sdb.id2idx[sid]
        skills.append(r)
    # `loaded`: (file, entries) as rAthena's "Done reading" lines report them
    ctx['write_json']('skills_db', {'skills': skills, 'skill_num': sdb.skill_num,
                                    'loaded': sdb.counts + [c for c in st['txt_counts'] if 'nocast' in c[0] or 'damage' in c[0]]})
    ctx['write_json']('skills_produce', {'produce': [p or {'nameid': 0, 'req_skill': 0, 'req_skill_lv': 0, 'itemlv': 0,
                                                            'mat_id': [0] * MAX_PRODUCE_RESOURCE, 'mat_amount': [0] * MAX_PRODUCE_RESOURCE}
                                                     for p in st['produce']],
                                         'changematerial': [c or {'nameid': 0, 'rate': 0, 'qty': [0] * MAX_SKILL_CHANGEMATERIAL_SET,
                                                                  'qty_rate': [0] * MAX_SKILL_CHANGEMATERIAL_SET}
                                                            for c in st['changemat']],
                                         'loaded': [c for c in st['txt_counts'] if 'produce' in c[0] or 'changematerial' in c[0]]})
    ctx['write_json']('skills_misc', {
        'abra': [a for a in st['abra'].data.values()],
        'magic_mushroom': [m['skill_id'] for m in st['mushroom'].data.values()],
        'spellbook': [s for s in st['spellbook'].data.values()],
        'arrow': [{'nameid': a['nameid'], 'created': [[k, v] for k, v in a['created'].items()]} for a in st['arrow'].data.values()],
        'guild_skill_tree': [{'id': g['id'], 'max': g['max'], 'need': [[r['id'], r['lv']] for r in g['need'].values()]}
                             for g in st['guild'].data.values()],
        'loaded': st['abra'].counts + st['mushroom'].counts + st['spellbook'].counts + st['arrow'].counts + st['guild'].counts,
    })
    ctx['write_json']('skills_unit_layout', st['layouts'])


def gen_rust_ids(root, out):
    """`python3 skills.py --gen-ids <rathena> <file.rs>`: e_skill / e_guild_skill as Rust constants, and
    skill.cpp skill_dummy2skill_id's switch."""
    e = skill_enum({'root': root})
    lines = ['//! skill.hpp `enum e_skill`, mmo.hpp `enum e_guild_skill` and skill.cpp skill_dummy2skill_id (rAthena e985006),',
             '//! generated by `python3 tools/datagen_mods/skills.py --gen-ids <rathena> src/db/skills/ids.rs`. Do not edit.', '',
             '#![allow(dead_code)]', '']
    for n, v in e.items():
        lines.append('pub const %s: u16 = %d;' % (n, v))
    src = open(os.path.join(root, 'src/map/skill.cpp'), encoding='latin-1').read()
    i = src.index('uint16 skill_dummy2skill_id(uint16 skill_id) {')
    body = re.sub(r'//[^\n]*', '', src[i:src.index('\n}\n', i)])
    lines += ['', '/// skill.cpp skill_dummy2skill_id: the skill a dummy (copy referral) skill stands for.',
              'pub fn skill_dummy2skill_id(skill_id: u16) -> u16 {', '    match skill_id {']
    labels = []
    for m in re.finditer(r'case\s+(\w+)\s*:|return\s+(\w+)\s*;', body):
        if m.group(1):
            labels.append(m.group(1))
        elif labels:
            lines.append('        %s => %s,' % (' | '.join(labels), m.group(2)))
            labels = []
    lines += ['        _ => skill_id,', '    }', '}']
    open(out, 'w').write('\n'.join(lines) + '\n')


if __name__ == '__main__' and len(sys.argv) == 4 and sys.argv[1] == '--gen-ids':
    gen_rust_ids(sys.argv[2], sys.argv[3])
