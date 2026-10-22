#!/usr/bin/env python3
"""Rewrites Lua 5.1 bytecode compiled with size_t=4 (32-bit RO client) into size_t=8 so a 64-bit lua5.1 can load it."""
import struct, sys

class R:
    def __init__(s, b): s.b = b; s.p = 0
    def u8(s): v = s.b[s.p]; s.p += 1; return v
    def i32(s): v = struct.unpack_from('<i', s.b, s.p)[0]; s.p += 4; return v
    def raw(s, n): v = s.b[s.p:s.p+n]; s.p += n; return v

def convert(data, in_size_t=4, out_size_t=8):
    r = R(data); out = bytearray()
    hdr = r.raw(12)
    assert hdr[:4] == b'\x1bLua' and hdr[4] == 0x51, 'not Lua 5.1 bytecode'
    assert hdr[8] == in_size_t, 'unexpected size_t %d' % hdr[8]
    h = bytearray(hdr); h[8] = out_size_t; out += h
    def wr_size(n): out.extend(struct.pack('<q' if out_size_t == 8 else '<i', n))
    def rd_string():
        n = r.i32()
        s = r.raw(n)
        wr_size(n); out.extend(s)
    def rd_function():
        rd_string()                       # source
        out.extend(r.raw(4 + 4 + 1 + 1 + 1 + 1))  # linedefined, lastlinedefined, nups, nparams, is_vararg, maxstacksize
        n = r.i32(); out.extend(struct.pack('<i', n)); out.extend(r.raw(4 * n))       # code
        n = r.i32(); out.extend(struct.pack('<i', n))                                  # constants
        for _ in range(n):
            t = r.u8(); out.append(t)
            if t == 0: pass
            elif t == 1: out.extend(r.raw(1))
            elif t == 3: out.extend(r.raw(8))
            elif t == 4: rd_string()
            else: raise SystemExit('bad constant type %d' % t)
        n = r.i32(); out.extend(struct.pack('<i', n))                                  # protos
        for _ in range(n): rd_function()
        n = r.i32(); out.extend(struct.pack('<i', n)); out.extend(r.raw(4 * n))       # lineinfo
        n = r.i32(); out.extend(struct.pack('<i', n))                                  # locvars
        for _ in range(n): rd_string(); out.extend(r.raw(8))
        n = r.i32(); out.extend(struct.pack('<i', n))                                  # upvalues
        for _ in range(n): rd_string()
    rd_function()
    assert r.p == len(data), 'trailing bytes: %d' % (len(data) - r.p)
    return bytes(out)

if __name__ == '__main__':
    for path in sys.argv[1:]:
        data = open(path, 'rb').read()
        if data[8] == 8: continue
        open(path, 'wb').write(convert(data))
        print('converted', path)
