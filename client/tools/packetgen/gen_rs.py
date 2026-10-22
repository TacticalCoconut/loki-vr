#!/usr/bin/env python3
"""
Generates the Loki Online module's packet layer (loki-server/src/net/packets.rs) from rAthena's preprocessed headers
(pre_common.txt + pre_map.txt, rAthena e985006 at PACKETVER 20211103 — the same dumps gen_structs.py reads for the client).

Every `struct PACKET_* / packet_*` definition becomes a Rust struct with `write` (little-endian, packed, the
PacketLength field patched for variable packets) and `read`. Header ids come from the `const int16 HEADER_X = ...;`
lines (DEFINE_PACKET_HEADER) and the `packet_headers` enum (`xType = ...`). The client→server length table comes from
packets_20211103.json (rAthena's packet_db after packetdb_addpacket, last registration wins).

Usage: gen_rs.py pre_map.txt pre_common.txt packets_20211103.json out.rs
"""
import json, re, sys

PRIM = {
    'int8': ('i8', 1), 'uint8': ('u8', 1), 'char': ('u8', 1), 'bool': ('u8', 1), 'unsigned char': ('u8', 1),
    'int16': ('i16', 2), 'uint16': ('u16', 2), 'short': ('i16', 2), 'unsigned short': ('u16', 2),
    'int32': ('i32', 4), 'uint32': ('u32', 4), 'int': ('i32', 4), 'unsigned int': ('u32', 4),
    'int64': ('i64', 8), 'uint64': ('u64', 8), 'float': ('f32', 4),
    't_itemid': ('u32', 4), 't_tick': ('i64', 8), 'e_sex': ('u8', 1),
}
KEYWORDS = {'type', 'move', 'ref', 'self', 'fn', 'use', 'loop', 'match', 'mod', 'impl', 'where', 'in', 'as', 'box', 'final',
            'override', 'priv', 'virtual', 'yield', 'unsized', 'macro', 'abstract', 'become', 'do', 'typeof', 'try', 'async',
            'await', 'dyn', 'struct', 'enum', 'trait', 'let', 'const', 'static', 'mut', 'pub', 'crate', 'super', 'extern',
            'return', 'break', 'continue', 'if', 'else', 'while', 'for', 'true', 'false', 'unsafe'}


def load(paths):
    return '\n'.join(open(p, errors='replace').read() for p in paths)


def flatten_bitfields(text):
    """`struct { uint32 a : 1; ... } flag;` (bit-field groups inside packets) -> `uint32 flag;` (callers set the bits)."""
    def rep(m):
        types = re.findall(r'([A-Za-z_][A-Za-z0-9_ ]*?)\s+[A-Za-z_]\w*\s*:\s*\d+\s*;', m.group(1))
        if not types or len(set(t.strip() for t in types)) != 1:
            return m.group(0)
        return '\n ' + types[0].strip() + ' ' + m.group(2) + ';'
    return re.sub(r'\n\s+struct\s*\{([^{}]*)\}\s*([A-Za-z_]\w*)\s*;', rep, text)


def all_structs(text):
    text = flatten_bitfields(text)
    out = {}
    for m in re.finditer(r'\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{([^{}]*)\}\s*(__attribute__\s*\(\(packed\)\))?\s*;', text, re.S):
        out[m.group(1)] = (m.group(2), m.group(3) is not None)  # last definition wins
    return out


def headers(text):
    ids = {}
    for m in re.finditer(r'const\s+int16\s+HEADER_([A-Za-z0-9_]+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*;', text):
        ids['PACKET_' + m.group(1)] = int(m.group(2), 0)
    em = re.search(r'enum\s+packet_headers\s*\{(.*?)\}\s*;', text, re.S)
    if em:
        for m in re.finditer(r'([A-Za-z0-9_]+)Type\s*=\s*(0x[0-9a-fA-F]+|\d+)', em.group(1)):
            ids['packet_' + m.group(1)] = int(m.group(2), 0)
    return ids


def eval_dim(d):
    d = d.strip()
    if d == '':
        return -1
    return int(eval(d.replace('MAX_STORAGE', '600').replace('MAX_INVENTORY', '100')))


def members(body):
    out = []
    body = re.sub(r'/\*.*?\*/', '', body, flags=re.S)
    body = re.sub(r'//[^\n]*', '', body)
    for line in body.split(';'):
        line = ' '.join(line.split())
        if not line:
            continue
        m = re.match(r'(?:struct\s+)?([A-Za-z_][A-Za-z0-9_ ]*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*((?:\[[^\]]*\])*)\s*$', line)
        if not m:
            raise ValueError('member %r' % line)
        dims = re.findall(r'\[([^\]]*)\]', m.group(3))
        dim = None
        for d in dims:  # char names[10][24] -> 240 bytes
            v = eval_dim(d)
            dim = v if dim is None else (-1 if v == -1 or dim == -1 else dim * v)
        out.append((m.group(1).strip(), m.group(2), dim))
    return out


class Gen:
    def __init__(self, text):
        self.structs = all_structs(text)
        self.info = {}

    def get(self, name):
        if name in self.info:
            return self.info[name]
        if name not in self.structs:
            raise ValueError('no struct ' + name)
        body, packed = self.structs[name]
        mems = []
        size = 0
        flex = False
        for typ, nm, dim in members(body):
            if typ in PRIM:
                rs, sz = PRIM[typ]
                mem = {'c': typ, 'name': nm, 'rs': rs, 'size': sz, 'dim': dim, 'nested': None}
            else:
                sub = self.get(typ)
                mem = {'c': typ, 'name': nm, 'rs': typ, 'size': sub['size'], 'dim': dim, 'nested': sub}
            if dim == -1:
                flex = True
            else:
                size += mem['size'] * (dim if dim is not None else 1)
            mems.append(mem)
        info = {'name': name, 'members': mems, 'size': size, 'flex': flex, 'packed': packed}
        self.info[name] = info
        return info


def field(nm):
    return 'r#' + nm if nm in KEYWORDS else nm


def rust_type(mem):
    d, rs = mem['dim'], mem['rs']
    if d is None:
        return rs
    if mem['c'] == 'char':
        return 'String' if d != -1 else 'Vec<u8>'
    if d == -1:
        return 'Vec<%s>' % rs
    if mem['nested'] is None and d <= 32:
        return '[%s; %d]' % (rs, d)
    return 'Vec<%s>' % rs


def emit_struct(info, ids, out):
    n = info['name']
    mems = info['members']
    out.append('/// rAthena `struct %s` (%d bytes%s).' % (n, info['size'], ' + variable part' if info['flex'] else ''))
    out.append('#[derive(Clone, Debug, Default)]')
    out.append('pub struct %s {' % n)
    for mem in mems:
        out.append('    pub %s: %s,' % (field(mem['name']), rust_type(mem)))
    out.append('}')
    out.append('impl %s {' % n)
    out.append('    pub const SIZE: usize = %d;' % info['size'])
    if n in ids:
        out.append('    pub const ID: u16 = 0x%04x;' % ids[n])
    # write
    out.append('    pub fn write(&self, w: &mut Vec<u8>) {')
    out.append('        let _start = w.len();')
    for mem in mems:
        f, d = field(mem['name']), mem['dim']
        if d is None:
            if mem['nested']:
                out.append('        self.%s.write(w);' % f)
            else:
                out.append('        w.extend_from_slice(&self.%s.to_le_bytes());' % f)
        elif mem['c'] == 'char':
            if d == -1:
                out.append('        w.extend_from_slice(&self.%s);' % f)
            else:
                out.append('        put_str(w, &self.%s, %d);' % (f, d))
        elif d == -1:
            if mem['nested']:
                out.append('        for e in &self.%s { e.write(w); }' % f)
            else:
                out.append('        for e in &self.%s { w.extend_from_slice(&e.to_le_bytes()); }' % f)
        else:
            if mem['nested']:
                out.append('        for i in 0..%d { match self.%s.get(i) { Some(e) => e.write(w), None => %s::default().write(w) } }' % (d, f, mem['rs']))
            elif rust_type(mem).startswith('['):
                out.append('        for e in &self.%s { w.extend_from_slice(&e.to_le_bytes()); }' % f)
            else:
                out.append('        for i in 0..%d { w.extend_from_slice(&self.%s.get(i).copied().unwrap_or_default().to_le_bytes()); }' % (d, f))
    # variable packets: the u16 at offset 2 is the whole length
    if len(mems) >= 2 and mems[1]['rs'] in ('i16', 'u16') and mems[1]['dim'] is None and re.search(r'len', mems[1]['name'], re.I):
        out.append('        let _len = (w.len() - _start) as u16;')
        out.append('        w[_start + 2.._start + 4].copy_from_slice(&_len.to_le_bytes());')
    out.append('    }')
    out.append('    pub fn to_vec(&self) -> Vec<u8> { let mut w = Vec::with_capacity(Self::SIZE); self.write(&mut w); w }')
    # read
    out.append('    pub fn read(r: &mut Reader) -> Option<Self> {')
    out.append('        let mut p = Self::default();')
    for mem in mems:
        f, d = field(mem['name']), mem['dim']
        if d is None:
            if mem['nested']:
                out.append('        p.%s = %s::read(r)?;' % (f, mem['rs']))
            else:
                out.append('        p.%s = r.%s()?;' % (f, mem['rs']))
        elif mem['c'] == 'char':
            if d == -1:
                out.append('        p.%s = r.rest().to_vec();' % f)
            else:
                out.append('        p.%s = r.string(%d)?;' % (f, d))
        elif d == -1:
            if mem['nested']:
                out.append('        while r.remaining() >= %s::SIZE { p.%s.push(%s::read(r)?); }' % (mem['rs'], f, mem['rs']))
            else:
                out.append('        while r.remaining() >= %d { p.%s.push(r.%s()?); }' % (mem['size'], f, mem['rs']))
        else:
            if mem['nested']:
                out.append('        for _ in 0..%d { p.%s.push(%s::read(r)?); }' % (d, f, mem['rs']))
            elif rust_type(mem).startswith('['):
                out.append('        for i in 0..%d { p.%s[i] = r.%s()?; }' % (d, f, mem['rs']))
            else:
                out.append('        for _ in 0..%d { p.%s.push(r.%s()?); }' % (d, f, mem['rs']))
    out.append('        Some(p)')
    out.append('    }')
    out.append('}')


def main():
    pre_map, pre_common, pdb, outp = sys.argv[1:5]
    text = load([pre_common, pre_map])
    g = Gen(text)
    ids = headers(text)
    names = sorted(n for n, (_, packed) in g.structs.items() if (n.startswith('PACKET_') or n.startswith('packet_')))
    ok, skipped, order, seen = [], [], [], set()

    def visit(info):
        if info['name'] in seen:
            return
        for m in info['members']:
            if m['nested']:
                visit(m['nested'])
        seen.add(info['name'])
        order.append(info)

    for n in names:
        try:
            visit(g.get(n))
            ok.append(n)
        except (ValueError, SyntaxError, NameError) as e:
            skipped.append((n, str(e)))
    out = ['// @generated by loki/tools/packetgen/gen_rs.py from rAthena e985006 headers at PACKETVER 20211103. Do not edit.',
           '#![allow(non_snake_case, non_camel_case_types, dead_code, clippy::all)]',
           'use super::wire::{put_str, Reader};', '']
    for info in order:
        emit_struct(info, ids, out)
        out.append('')
    # client -> server lengths (packet_db): 0 = unknown, -1 = variable
    db = {}
    funcs = {}
    for p in json.load(open(pdb))['packets']:
        db[p['id']] = p['len']
        if p['func']:
            funcs[p['id']] = p['func']
    out.append('/// rAthena packet_db lengths for PACKETVER 20211103 (clif_packetdb.hpp; last registration wins). -1 = variable.')
    out.append('pub fn packet_len(id: u16) -> Option<i16> {')
    out.append('    Some(match id {')
    for k in sorted(db):
        out.append('        0x%04x => %d,' % (k, db[k]))
    out.append('        _ => return None,')
    out.append('    })')
    out.append('}')
    out.append('')
    pos = {}
    try:
        for p in json.load(open(pdb.replace('packets_', 'packets_pos_')))['packets']:
            pos[p['id']] = p['pos']
    except OSError:
        pass
    out.append('/// rAthena packet_db field offsets (`pos[]`, dump5.cpp) for packets parsed by offset rather than by struct.')
    out.append("pub fn packet_pos(id: u16) -> &'static [u16] {")
    out.append('    match id {')
    for k in sorted(pos):
        if pos[k]:
            out.append('        0x%04x => &[%s],' % (k, ', '.join(str(v) for v in pos[k])))
    out.append('        _ => &[],')
    out.append('    }')
    out.append('}')
    out.append('')
    out.append('/// The rAthena parser bound to each client packet (packet_db func), for logs.')
    out.append('pub fn parser_name(id: u16) -> &\'static str {')
    out.append('    match id {')
    for k in sorted(funcs):
        out.append('        0x%04x => "%s",' % (k, funcs[k]))
    out.append('        _ => "",')
    out.append('    }')
    out.append('}')
    open(outp, 'w').write('\n'.join(out) + '\n')
    print('structs', len(ok), 'skipped', len(skipped))
    # cross-check against the sizes a compiler measured (dump3.cpp / g++ probe): every mismatch is a generator bug
    bad = 0
    for fn in ('sizes_20211103.json', 'sizes_extra.json'):
        try:
            sizes = json.load(open(fn))['sizes']
        except OSError:
            continue
        for e in sizes:
            if e['name'] in g.info and g.info[e['name']]['size'] != e['size']:
                bad += 1
                print('  SIZE MISMATCH', e['name'], 'gen', g.info[e['name']]['size'], 'compiler', e['size'])
    print('size mismatches:', bad)
    for s in skipped[:40]:
        print('  skip', s)


if __name__ == '__main__':
    main()
