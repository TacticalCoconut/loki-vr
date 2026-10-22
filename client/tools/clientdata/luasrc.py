#!/usr/bin/env python3
r"""
Reads a plain-text Lua 5.1 data file (the client's System\itemInfo.lua) the way lua5.1's dofile() would leave its globals,
for the client-data tools (client wave 3b, w3data; lubdump.py classnum). lub51.py runs compiled .lub files; this reads source.

The lexer is Lua 5.1's (llex.c, read step for step): long brackets [==[ ... ]==] with the first newline dropped and every
newline sequence (\n, \r, \r\n, \n\r) stored as one \n; short strings with 5.1's escapes (\a \b \f \n \r \t \v \\ \" \'
\<newline> \ddd, any other escaped character kept as itself); comments (-- and --[[ ]]); numerals as read_numeral reads them.
The parser takes what a data file holds (lparser.c's rules for these parts):
  Name = exp                      a global assignment
  function Name(...) ... end      a function definition: recorded as a function the tools never call (its body is skipped
                                  by matching 'end' against function / do / if)
  exp: nil, true, false, a number, a string, a table constructor, a global name with .field / [exp] indexing, unary minus
  table constructor: [exp] = exp, Name = exp and list items; list items are stored in batches of 50 (LFIELDS_PER_FLUSH) when
  the next field starts and at the end, as the compiled code's SETLIST does, so a list item and an [n] key collide as in Lua.
Anything else stops the read with an error: a quiet misreading is worse than none.
Strings are bytes (the file's own bytes), numbers go through lub51.norm like the bytecode's constants.
"""
import lub51


class LuaSyntaxError(Exception):
    pass


KEYWORDS = {b'and', b'break', b'do', b'else', b'elseif', b'end', b'false', b'for', b'function', b'if', b'in', b'local',
            b'nil', b'not', b'or', b'repeat', b'return', b'then', b'true', b'until', b'while'}
SYMBOLS3 = [b'...']
SYMBOLS2 = [b'==', b'>=', b'<=', b'~=', b'..']
SYMBOLS1 = b'+-*/%^#=<>(){}[];:,.'


def isalpha(c): return (65 <= c <= 90) or (97 <= c <= 122)
def isdigit(c): return 48 <= c <= 57
def isalnum(c): return isalpha(c) or isdigit(c)
def isspace(c): return c in (32, 9, 10, 11, 12, 13)


class Lexer:
    """llex.c of Lua 5.1: tokens as (kind, value); kind is 'name', 'string', 'number', 'eos', or the keyword / symbol bytes."""

    def __init__(self, data):
        self.b = data
        self.p = 0
        self.line = 1

    def cur(self):
        return self.b[self.p] if self.p < len(self.b) else -1

    def newline(self):
        """inclinenumber: skip '\n' or '\r', and a following '\r' / '\n' that differs from it."""
        old = self.cur(); self.p += 1
        c = self.cur()
        if c in (10, 13) and c != old: self.p += 1
        self.line += 1

    def skip_sep(self):
        """At '[' or ']': counts the '=' after it; returns the count if the same bracket follows, else -count - 1 (llex.c skip_sep)."""
        s = self.cur(); self.p += 1
        count = 0
        while self.cur() == 61: self.p += 1; count += 1
        return count if self.cur() == s else -count - 1

    def long_string(self, sep):
        """read_long_string: the position is on the second '['."""
        self.p += 1
        if self.cur() in (10, 13): self.newline()
        out = bytearray()
        while True:
            c = self.cur()
            if c == -1: raise LuaSyntaxError('unfinished long string/comment at line %d' % self.line)
            if c == 93:  # ']'
                start = self.p
                if self.skip_sep() == sep:
                    self.p += 1
                    return bytes(out)
                out += self.b[start:self.p]
            elif c == 91:  # '[' : LUA_COMPAT_LSTR 1: nesting only refused for level 0
                start = self.p
                n = self.skip_sep()
                if n == sep:
                    self.p += 1
                    if sep == 0: raise LuaSyntaxError('nesting of [[...]] is deprecated at line %d' % self.line)
                out += self.b[start:self.p]
            elif c in (10, 13):
                out.append(10); self.newline()
            else:
                out.append(c); self.p += 1

    def short_string(self, delim):
        self.p += 1
        out = bytearray()
        while True:
            c = self.cur()
            if c == delim: self.p += 1; return bytes(out)
            if c == -1: raise LuaSyntaxError('unfinished string at line %d' % self.line)
            if c in (10, 13): raise LuaSyntaxError('unfinished string at line %d' % self.line)
            if c == 92:  # backslash
                self.p += 1
                c = self.cur()
                esc = {97: 7, 98: 8, 102: 12, 110: 10, 114: 13, 116: 9, 118: 11}
                if c in esc: out.append(esc[c]); self.p += 1
                elif c in (10, 13): out.append(10); self.newline()
                elif c == -1: continue
                elif isdigit(c):
                    v = 0; i = 0
                    while i < 3 and isdigit(self.cur()): v = v * 10 + self.cur() - 48; self.p += 1; i += 1
                    if v > 255: raise LuaSyntaxError('escape sequence too large at line %d' % self.line)
                    out.append(v)
                else: out.append(c); self.p += 1
            else:
                out.append(c); self.p += 1

    def numeral(self):
        """read_numeral + luaO_str2d: digits and '.', an exponent with its sign, then any alnum / '_' run."""
        start = self.p
        while isdigit(self.cur()) or self.cur() == 46: self.p += 1
        if self.cur() in (69, 101):  # E e
            self.p += 1
            if self.cur() in (43, 45): self.p += 1
        while isalnum(self.cur()) or self.cur() == 95: self.p += 1
        text = self.b[start:self.p]
        try:
            if text[:2] in (b'0x', b'0X'): v = float(int(text[2:], 16))
            else:
                if not all(isdigit(c) or c in (46, 69, 101, 43, 45) for c in text): raise ValueError
                v = float(text)
        except ValueError:
            raise LuaSyntaxError('malformed number %r at line %d' % (text, self.line))
        return lub51.norm(v)

    def next(self):
        while True:
            c = self.cur()
            if c == -1: return ('eos', None)
            if c in (10, 13): self.newline(); continue
            if c in (32, 9, 11, 12): self.p += 1; continue
            if c == 45 and self.b[self.p + 1:self.p + 2] == b'-':  # comment
                self.p += 2
                if self.cur() == 91:
                    sep = self.skip_sep()
                    if sep >= 0: self.long_string(sep); continue
                while self.cur() not in (-1, 10, 13): self.p += 1
                continue
            if c == 91:  # '['
                save = self.p
                sep = self.skip_sep()
                if sep >= 0: return ('string', self.long_string(sep))
                if sep != -1: raise LuaSyntaxError('invalid long string delimiter at line %d' % self.line)
                self.p = save + 1
                return (b'[', None)
            if c in (34, 39): return ('string', self.short_string(c))
            if isdigit(c) or (c == 46 and isdigit(self.b[self.p + 1] if self.p + 1 < len(self.b) else -1)):
                return ('number', self.numeral())
            if isalpha(c) or c == 95:
                start = self.p
                while isalnum(self.cur()) or self.cur() == 95: self.p += 1
                w = self.b[start:self.p]
                return (w, None) if w in KEYWORDS else ('name', w)
            for s in SYMBOLS3 + SYMBOLS2:
                if self.b[self.p:self.p + len(s)] == s: self.p += len(s); return (s, None)
            if c in SYMBOLS1: self.p += 1; return (bytes([c]), None)
            raise LuaSyntaxError('unexpected character %r at line %d' % (chr(c), self.line))


class NotCallable:
    """A function defined in the file: the data tools only read tables, so it is never run here."""
    def __init__(self, name): self.name = name
    def __call__(self, *a): raise LuaSyntaxError('luasrc.py does not run Lua functions (%s)' % self.name)


class Parser:
    def __init__(self, data, G):
        self.lx = Lexer(data)
        self.G = G
        self.tok = self.lx.next()
        self.ahead = None

    def advance(self):
        if self.ahead is not None: self.tok, self.ahead = self.ahead, None
        else: self.tok = self.lx.next()

    def peek(self):
        if self.ahead is None: self.ahead = self.lx.next()
        return self.ahead

    def expect(self, kind):
        if self.tok[0] != kind: raise LuaSyntaxError('%r expected near %r at line %d' % (kind, self.tok, self.lx.line))
        v = self.tok[1]; self.advance(); return v

    def chunk(self):
        while self.tok[0] != 'eos':
            k = self.tok[0]
            if k == b';': self.advance(); continue
            if k == b'function': self.function_stat(); continue
            if k == 'name' and self.peek()[0] == b'=':
                name = self.expect('name'); self.expect(b'=')
                self.G.set(name, self.exp()); continue
            raise LuaSyntaxError('statement not supported by luasrc.py near %r at line %d' % (self.tok, self.lx.line))

    def function_stat(self):
        self.advance()
        name = self.expect('name')
        if self.tok[0] in (b'.', b':'): raise LuaSyntaxError('method definitions are not supported (line %d)' % self.lx.line)
        depth = 1  # skip the body: 'end' closes function / do / if (while and for open with 'do')
        while depth:
            k = self.tok[0]
            if k == 'eos': raise LuaSyntaxError("'end' expected (function %s)" % name)
            if k in (b'function', b'do', b'if'): depth += 1
            elif k == b'end': depth -= 1
            self.advance()
        self.G.set(name, NotCallable(name.decode('latin-1')))

    def exp(self):
        k, v = self.tok
        if k == b'nil': self.advance(); return None
        if k == b'true': self.advance(); return True
        if k == b'false': self.advance(); return False
        if k == 'number': self.advance(); r = v
        elif k == 'string': self.advance(); r = v
        elif k == b'{': r = self.constructor()
        elif k == b'-':
            self.advance()
            x = self.exp_simple_number()
            r = -x
        elif k == 'name': r = self.primary()
        else: raise LuaSyntaxError('expression not supported by luasrc.py near %r at line %d' % (self.tok, self.lx.line))
        if self.tok[0] in (b'+', b'-', b'*', b'/', b'%', b'^', b'..', b'==', b'~=', b'<', b'<=', b'>', b'>=', b'and', b'or'):
            raise LuaSyntaxError('binary operators are not supported by luasrc.py (line %d)' % self.lx.line)
        return r

    def exp_simple_number(self):
        if self.tok[0] != 'number': raise LuaSyntaxError('unary minus on a non-number is not supported (line %d)' % self.lx.line)
        v = self.tok[1]; self.advance(); return v

    def primary(self):
        name = self.expect('name')
        v = self.G.get(name)
        while self.tok[0] in (b'.', b'['):
            if self.tok[0] == b'.':
                self.advance(); key = self.expect('name')
            else:
                self.advance(); key = self.exp(); self.expect(b']')
            v = lub51.index(v, key)
        if self.tok[0] in (b'(', b':', 'string', b'{'): raise LuaSyntaxError('function calls are not supported (line %d)' % self.lx.line)
        return v

    def constructor(self):
        self.expect(b'{')
        t = lub51.LuaTable()
        pending = []   # list items not stored yet (lparser.c ConsControl.tostore)
        stored = 0     # list items already stored (na)
        while self.tok[0] != b'}':
            if len(pending) == 50:  # closelistfield: flush when the next field starts
                for i, x in enumerate(pending): t.set(stored + i + 1, x)
                stored += len(pending); pending = []
            k = self.tok[0]
            if k == 'name' and self.peek()[0] == b'=':
                key = self.expect('name'); self.expect(b'='); t.set(key, self.exp())
            elif k == b'[':
                self.advance(); key = self.exp(); self.expect(b']'); self.expect(b'=')
                val = self.exp()
                t.set(key, val)
            else:
                pending.append(self.exp())
            if self.tok[0] in (b',', b';'): self.advance()
            elif self.tok[0] != b'}': raise LuaSyntaxError("'}' expected near %r at line %d" % (self.tok, self.lx.line))
        self.advance()
        for i, x in enumerate(pending): t.set(stored + i + 1, x)  # lastlistfield
        return t


def run(data, G=None):
    """Reads the chunk into the globals table G (lub51.GLOBALS by default)."""
    Parser(data, G if G is not None else lub51.GLOBALS).chunk()
