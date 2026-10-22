#!/usr/bin/env python3
"""
A small Lua 5.1 bytecode interpreter for the RO client's data files (.lub), so client-data tools run on this PC without
a lua5.1 binary (none is installed here; the earlier tools assumed one). It loads the client's own bytecode as is
(32-bit: size_t = 4, as the header says; 64-bit files load too), runs the main chunk and hands back the globals.

Written from the Lua 5.1 sources' definitions (lundump.c: the chunk layout; lopcodes.h: the 38 opcodes and their
operand encoding; lvm.c: their semantics, FPF 50 for SETLIST; ltable.c: the length border). Strings stay Python
`bytes` (RO text is CP949 / CP1252 / UTF-8 depending on the file; the caller decodes). Numbers are floats, normalised
to int when integral so table keys 1 and 1.0 are one key, as in Lua.

Only what data files need is provided as library: pairs, ipairs, next, type, tostring, tonumber, print, rawget, rawset,
setmetatable/getmetatable (stored, __index honoured on reads), table.insert, table.getn, string.format (%s %d %x),
dofile (through a loader the caller gives), select, unpack, assert, error. Anything else raises.
"""
import math, struct


class LuaError(Exception):
    pass


class LuaTable:
    __slots__ = ('h', 'meta')

    def __init__(self):
        self.h = {}
        self.meta = None

    def get(self, k):
        v = self.h.get(norm(k))
        if v is None and self.meta is not None:
            idx = self.meta.h.get(b'__index')
            if isinstance(idx, LuaTable): return idx.get(k)
            if idx is not None:
                r = call(idx, [self, k])
                return r[0] if r else None
        return v

    def set(self, k, v):
        if k is None: raise LuaError('table index is nil')
        k = norm(k)
        if v is None: self.h.pop(k, None)
        else: self.h[k] = v

    def length(self):
        n = 0
        while (n + 1) in self.h: n += 1
        return n

    def items(self):
        return list(self.h.items())

    def __repr__(self):
        return 'LuaTable(%d)' % len(self.h)


def norm(k):
    if isinstance(k, float) and k.is_integer(): return int(k)
    return k


class Proto:
    __slots__ = ('source', 'nups', 'nparams', 'vararg', 'maxstack', 'code', 'k', 'p')


class Cell:
    """An upvalue: open (points at a register of a live frame) or closed (holds the value)."""
    __slots__ = ('regs', 'idx', 'val', 'open')

    def __init__(self, regs, idx):
        self.regs, self.idx, self.open, self.val = regs, idx, True, None

    def get(self): return self.regs[self.idx] if self.open else self.val

    def set(self, v):
        if self.open: self.regs[self.idx] = v
        else: self.val = v

    def close(self):
        if self.open: self.val = self.regs[self.idx]; self.open = False; self.regs = None


class Closure:
    __slots__ = ('p', 'up')

    def __init__(self, p, up): self.p, self.up = p, up


# ---- lundump.c ----
class _Reader:
    def __init__(self, b):
        self.b, self.p = b, 0
        if b[:4] != b'\x1bLua' or b[4] != 0x51: raise LuaError('not Lua 5.1 bytecode')
        if b[6] != 1: raise LuaError('big-endian bytecode not supported')
        self.int_sz, self.size_t, self.ins_sz, self.num_sz, self.integral = b[7], b[8], b[9], b[10], b[11]
        if self.ins_sz != 4 or self.num_sz != 8 or self.integral != 0: raise LuaError('unexpected number/instruction format')
        self.p = 12

    def u8(self): v = self.b[self.p]; self.p += 1; return v
    def int(self): v = int.from_bytes(self.b[self.p:self.p + self.int_sz], 'little', signed=True); self.p += self.int_sz; return v
    def sz(self): v = int.from_bytes(self.b[self.p:self.p + self.size_t], 'little'); self.p += self.size_t; return v
    def num(self): v = struct.unpack_from('<d', self.b, self.p)[0]; self.p += 8; return v

    def string(self):
        n = self.sz()
        if n == 0: return None
        s = bytes(self.b[self.p:self.p + n - 1]); self.p += n  # the stored length counts the trailing NUL
        return s

    def function(self):
        f = Proto()
        f.source = self.string()
        self.int(); self.int()  # linedefined, lastlinedefined
        f.nups, f.nparams, f.vararg, f.maxstack = self.u8(), self.u8(), self.u8(), self.u8()
        n = self.int(); f.code = list(struct.unpack_from('<%dI' % n, self.b, self.p)); self.p += 4 * n
        n = self.int(); f.k = []
        for _ in range(n):
            t = self.u8()
            if t == 0: f.k.append(None)
            elif t == 1: f.k.append(self.u8() != 0)
            elif t == 3: f.k.append(norm(self.num()))
            elif t == 4: f.k.append(self.string())
            else: raise LuaError('bad constant type %d' % t)
        n = self.int(); f.p = [self.function() for _ in range(n)]
        n = self.int(); self.p += self.int_sz * n          # lineinfo
        n = self.int()                                      # locvars
        for _ in range(n): self.string(); self.int(); self.int()
        n = self.int()                                      # upvalue names
        for _ in range(n): self.string()
        return f


def load(data):
    r = _Reader(data)
    f = r.function()
    return Closure(f, [])


# ---- values ----
def truthy(v): return v is not None and v is not False


def tonum(v):
    if isinstance(v, (int, float)) and not isinstance(v, bool): return v
    if isinstance(v, bytes):
        try:
            s = v.strip()
            return norm(float(int(s, 16)) if s.lower().startswith(b'0x') else float(s))
        except ValueError: return None
    return None


def tostr(v):
    if v is None: return b'nil'
    if v is True: return b'true'
    if v is False: return b'false'
    if isinstance(v, bytes): return v
    if isinstance(v, int): return str(v).encode()
    if isinstance(v, float): return (('%.14g' % v)).encode()
    return repr(v).encode()


def arith(op, a, b):
    x, y = tonum(a), tonum(b)
    if x is None or y is None: raise LuaError('arithmetic on %r and %r' % (a, b))
    if op == 12: r = x + y
    elif op == 13: r = x - y
    elif op == 14: r = x * y
    elif op == 15: r = x / y if y != 0 else (math.inf if x > 0 else -math.inf if x < 0 else math.nan)
    elif op == 16: r = x - math.floor(x / y) * y
    else: r = float(x) ** y
    return norm(float(r)) if isinstance(r, float) else r


def lua_eq(a, b):
    if isinstance(a, bool) or isinstance(b, bool): return a is b
    return a == b if type(norm(a)) == type(norm(b)) or (isinstance(a, (int, float)) and isinstance(b, (int, float))) else False


def lua_lt(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)): return a < b
    if isinstance(a, bytes) and isinstance(b, bytes): return a < b
    raise LuaError('attempt to compare %r with %r' % (a, b))


def index(o, k):
    if isinstance(o, LuaTable): return o.get(k)
    if isinstance(o, bytes) and k in STRING_LIB.h: return STRING_LIB.h[k]
    raise LuaError('attempt to index %r (key %r)' % (o, k))


def call(fn, args):
    if isinstance(fn, Closure): return execute(fn, args)
    if callable(fn): return fn(*args) or []
    raise LuaError('attempt to call %r' % (fn,))


FPF = 50


def execute(cl, args):
    p = cl.p
    regs = [None] * (max(p.maxstack, len(args)) + 256)
    for i in range(min(p.nparams, len(args))): regs[i] = args[i]
    varargs = list(args[p.nparams:]) if p.vararg else []
    open_cells = {}
    code, K, G = p.code, p.k, GLOBALS
    top = 0
    pc = 0

    def rk(x): return K[x & 0xFF] if x & 0x100 else regs[x]

    def close_from(a):
        for i in [i for i in open_cells if i >= a]:
            open_cells.pop(i).close()

    while True:
        ins = code[pc]; pc += 1
        op = ins & 0x3F; a = (ins >> 6) & 0xFF; c = (ins >> 14) & 0x1FF; b = (ins >> 23) & 0x1FF
        if op == 0: regs[a] = regs[b]
        elif op == 1: regs[a] = K[(ins >> 14) & 0x3FFFF]
        elif op == 2:
            regs[a] = b != 0
            if c: pc += 1
        elif op == 3:
            for i in range(a, b + 1): regs[i] = None
        elif op == 4: regs[a] = cl.up[b].get()
        elif op == 5: regs[a] = G.get(K[(ins >> 14) & 0x3FFFF])
        elif op == 6: regs[a] = index(regs[b], rk(c))
        elif op == 7: G.set(K[(ins >> 14) & 0x3FFFF], regs[a])
        elif op == 8: cl.up[b].set(regs[a])
        elif op == 9:
            t = regs[a]
            if not isinstance(t, LuaTable): raise LuaError('attempt to index %r' % (t,))
            t.set(rk(b), rk(c))
        elif op == 10: regs[a] = LuaTable()
        elif op == 11:
            o = regs[b]; regs[a + 1] = o; regs[a] = index(o, rk(c))
        elif 12 <= op <= 17: regs[a] = arith(op, rk(b), rk(c))
        elif op == 18:
            x = tonum(regs[b])
            if x is None: raise LuaError('unm on %r' % (regs[b],))
            regs[a] = -x
        elif op == 19: regs[a] = not truthy(regs[b])
        elif op == 20:
            v = regs[b]
            regs[a] = v.length() if isinstance(v, LuaTable) else len(v) if isinstance(v, bytes) else None
            if regs[a] is None: raise LuaError('length of %r' % (v,))
        elif op == 21: regs[a] = b''.join(tostr(regs[i]) for i in range(b, c + 1))
        elif op == 22: pc += ((ins >> 14) & 0x3FFFF) - 131071
        elif op in (23, 24, 25):
            x, y = rk(b), rk(c)
            r = lua_eq(x, y) if op == 23 else lua_lt(x, y) if op == 24 else (lua_lt(x, y) or lua_eq(x, y))
            if r != (a != 0): pc += 1
            else: pc += ((code[pc] >> 14) & 0x3FFFF) - 131071 + 1
        elif op == 26:
            if truthy(regs[a]) != (c != 0): pc += 1
            else: pc += ((code[pc] >> 14) & 0x3FFFF) - 131071 + 1
        elif op == 27:
            if truthy(regs[b]) == (c != 0):
                regs[a] = regs[b]; pc += ((code[pc] >> 14) & 0x3FFFF) - 131071 + 1
            else: pc += 1
        elif op in (28, 29):
            fn = regs[a]
            argv = regs[a + 1:top] if b == 0 else regs[a + 1:a + b]
            res = list(call(fn, list(argv)))
            if op == 29:
                close_from(0); return res
            if c == 0:
                for i, v in enumerate(res): regs[a + i] = v
                top = a + len(res)
            else:
                for i in range(c - 1): regs[a + i] = res[i] if i < len(res) else None
        elif op == 30:
            close_from(0)
            return list(regs[a:top]) if b == 0 else list(regs[a:a + b - 1])
        elif op == 31:
            step = regs[a + 2]; i = regs[a] + step; regs[a] = i
            if (step > 0 and i <= regs[a + 1]) or (step <= 0 and i >= regs[a + 1]):
                pc += ((ins >> 14) & 0x3FFFF) - 131071; regs[a + 3] = i
        elif op == 32:
            regs[a] = tonum(regs[a]) - tonum(regs[a + 2]); regs[a + 1] = tonum(regs[a + 1]); regs[a + 2] = tonum(regs[a + 2])
            pc += ((ins >> 14) & 0x3FFFF) - 131071
        elif op == 33:
            res = list(call(regs[a], [regs[a + 1], regs[a + 2]]))
            for i in range(c): regs[a + 3 + i] = res[i] if i < len(res) else None
            if regs[a + 3] is not None: regs[a + 2] = regs[a + 3]
            else: pc += 1
            # TFORLOOP is followed by a JMP back to the loop body; lvm.c jumps with it when the iterator returned non-nil
            if regs[a + 3] is not None: pc += ((code[pc] >> 14) & 0x3FFFF) - 131071 + 1
        elif op == 34:
            n = b
            if c == 0: c = code[pc]; pc += 1
            if n == 0: n = top - a - 1
            t = regs[a]; base = (c - 1) * FPF
            for i in range(1, n + 1): t.set(base + i, regs[a + i])
        elif op == 35: close_from(a)
        elif op == 36:
            np = p.p[(ins >> 14) & 0x3FFFF]; ups = []
            for _ in range(np.nups):
                pi = code[pc]; pc += 1
                pop = pi & 0x3F; pb = (pi >> 23) & 0x1FF
                if pop == 0:
                    cell = open_cells.get(pb)
                    if cell is None: cell = open_cells[pb] = Cell(regs, pb)
                    ups.append(cell)
                else: ups.append(cl.up[pb])
            regs[a] = Closure(np, ups)
        elif op == 37:
            if b == 0:
                for i, v in enumerate(varargs): regs[a + i] = v
                top = a + len(varargs)
            else:
                for i in range(b - 1): regs[a + i] = varargs[i] if i < len(varargs) else None
        else:
            raise LuaError('unknown opcode %d' % op)


# ---- the small library ----
GLOBALS = LuaTable()
STRING_LIB = LuaTable()
_loader = None


def _next(t, k=None):
    items = t.items()
    if k is None: return [items[0][0], items[0][1]] if items else [None]
    keys = [x for x, _ in items]
    k = norm(k)
    i = keys.index(k) + 1
    return [items[i][0], items[i][1]] if i < len(items) else [None]


def _pairs(t):
    # a snapshot iterator instead of next(): the generic for only needs f(s, ctl) -> k, v (lvm.c OP_TFORLOOP)
    it = iter(t.items())
    def f(_s=None, _c=None):
        for k, v in it: return [k, v]
        return [None]
    return [f, t, None]


def _ipairs(t):
    def it(tt, i):
        i = int(i) + 1; v = tt.get(i)
        return [None] if v is None else [i, v]
    return [it, t, 0]


def _fmt(f, *args):
    out, ai, i = bytearray(), 0, 0
    while i < len(f):
        ch = f[i:i + 1]
        if ch != b'%': out += ch; i += 1; continue
        j = i + 1
        while j < len(f) and f[j:j + 1] in b'-+ #0123456789.': j += 1
        conv = f[j:j + 1]; spec = f[i:j].decode()
        if conv == b'%': out += b'%'
        elif conv == b's': out += tostr(args[ai]); ai += 1
        elif conv in (b'd', b'i', b'x', b'X'):
            v = int(tonum(args[ai])); ai += 1
            out += ((spec + conv.decode().replace('i', 'd')) % v).encode()
        elif conv == b'c':
            out += bytes([int(tonum(args[ai])) & 0xFF]); ai += 1
        elif conv in (b'f', b'g', b'e'):
            out += ((spec + conv.decode()) % float(tonum(args[ai]))).encode(); ai += 1
        else: raise LuaError('string.format %r' % conv)
        i = j + 1
    return [bytes(out)]


def _dofile(path):
    if _loader is None: raise LuaError('dofile without a loader')
    data = _loader(path)
    if data is None: raise LuaError('dofile: %r not found' % path)
    return call(load(data), [])


def _setup():
    g = GLOBALS
    def reg(name, fn): g.set(name.encode(), fn)
    reg('pairs', _pairs); reg('ipairs', _ipairs); reg('next', lambda t, k=None: _next(t, k))
    reg('type', lambda v=None: [b'nil' if v is None else b'boolean' if isinstance(v, bool) else b'number' if isinstance(v, (int, float)) else b'string' if isinstance(v, bytes) else b'table' if isinstance(v, LuaTable) else b'function'])
    reg('tostring', lambda v=None: [tostr(v)]); reg('tonumber', lambda v=None, base=None: [tonum(v)])
    reg('print', lambda *a: print(*[tostr(x).decode('latin-1') for x in a]) or [])
    reg('rawget', lambda t, k: [t.h.get(norm(k))]); reg('rawset', lambda t, k, v: (t.set(k, v), [t])[1])
    def setmt(t, m): t.meta = m; return [t]
    reg('setmetatable', setmt); reg('getmetatable', lambda t: [t.meta if isinstance(t, LuaTable) else None])
    reg('select', lambda n, *a: [len(a)] if n == b'#' else list(a[int(n) - 1:]))
    reg('unpack', lambda t, i=1, j=None: [t.get(k) for k in range(int(i), int(j if j is not None else t.length()) + 1)])
    def _assert(v=None, msg=None, *rest):
        if not truthy(v): raise LuaError('assertion failed: %r' % (msg,))
        return [v, msg] + list(rest)
    reg('assert', _assert)
    def _error(msg=None, *_): raise LuaError(tostr(msg).decode('latin-1'))
    reg('error', _error)
    reg('dofile', _dofile)
    tbl = LuaTable()
    def tinsert(t, a, b=None):
        if b is None: t.set(t.length() + 1, a)
        else:
            pos = int(a); n = t.length()
            for i in range(n, pos - 1, -1): t.set(i + 1, t.get(i))
            t.set(pos, b)
        return []
    tbl.set(b'insert', tinsert); tbl.set(b'getn', lambda t: [t.length()])
    g.set(b'table', tbl)
    STRING_LIB.set(b'format', _fmt)
    STRING_LIB.set(b'len', lambda s: [len(s)])
    STRING_LIB.set(b'lower', lambda s: [s.lower()]); STRING_LIB.set(b'upper', lambda s: [s.upper()])
    STRING_LIB.set(b'sub', lambda s, i=1, j=-1: [_sub(s, int(i), int(j))])
    g.set(b'string', STRING_LIB)
    mth = LuaTable(); mth.set(b'floor', lambda x: [norm(float(math.floor(x)))]); mth.set(b'ceil', lambda x: [norm(float(math.ceil(x)))])
    mth.set(b'max', lambda *a: [max(a)]); mth.set(b'min', lambda *a: [min(a)])
    g.set(b'math', mth)


def _sub(s, i, j):
    n = len(s)
    if i < 0: i = max(n + i + 1, 1)
    elif i == 0: i = 1
    if j < 0: j = n + j + 1
    elif j > n: j = n
    return s[i - 1:j] if i <= j else b''


_setup()


def run(data, loader=None):
    """Runs a chunk (bytes of a .lub) against the shared globals; returns its results."""
    global _loader
    if loader is not None: _loader = loader
    return call(load(data), [])


def reset():
    """A fresh Lua state in this process (client wave 5, w5ui: build_skilltree.py's Portuguese pass runs the client's files again
    with the Portuguese string tables): the globals emptied and the small library set up again. The same GLOBALS / STRING_LIB
    objects are kept, so tables read out of an earlier state stay valid Python objects. (The C# port, Data/Lub51.cs, has a LuaVM
    per state: there a new LuaVM() is the reset.)"""
    global _loader
    GLOBALS.h.clear(); GLOBALS.meta = None
    STRING_LIB.h.clear(); STRING_LIB.meta = None
    _loader = None
    _setup()
