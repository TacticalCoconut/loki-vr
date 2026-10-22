#!/usr/bin/env python3
"""
Generates C# packet structs (with Parse/Write) from rAthena's preprocessed headers for one PACKETVER.
Usage: gen_structs.py pre_map.txt pre_common.txt out.cs NAME [NAME...]
Struct bodies are parsed textually (they are #pragma pack(1) / packed, so offsets are sequential).
"""
import re, sys

PRIM = {
    'int8': ('sbyte', 1, 'S8'), 'uint8': ('byte', 1, 'U8'), 'char': ('byte', 1, 'U8'), 'bool': ('byte', 1, 'U8'),
    'int16': ('short', 2, 'S16'), 'uint16': ('ushort', 2, 'U16'),
    'int32': ('int', 4, 'S32'), 'uint32': ('uint', 4, 'U32'), 'int': ('int', 4, 'S32'), 'unsigned int': ('uint', 4, 'U32'),
    'int64': ('long', 8, 'S64'), 'uint64': ('ulong', 8, 'U64'), 'float': ('float', 4, 'F32'),
    't_itemid': ('uint', 4, 'U32'), 't_tick': ('long', 8, 'S64'), 'e_sex': ('byte', 1, 'U8'),
}

def load(paths):
    text = ''
    for p in paths: text += open(p, errors='replace').read() + '\n'
    return text

def find_struct(text, name):
    # last definition wins (headers may define the same name in #if branches; -E leaves only the active one, but common+map may both define)
    ms = list(re.finditer(r'\bstruct\s+' + re.escape(name) + r'\s*\{(.*?)\}\s*(?:__attribute__\s*\(\(packed\)\))?\s*;', text, re.S))
    if not ms: return None
    return ms[-1].group(1)

def parse_members(body, text, cache):
    members = []
    for line in body.split(';'):
        line = line.strip()
        if not line or line.startswith('//'): continue
        line = re.sub(r'/\*.*?\*/', '', line)
        m = re.match(r'(?:struct\s+)?([A-Za-z_][A-Za-z0-9_ ]*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(\[(.*?)\])?\s*$', line)
        if not m:
            raise SystemExit('cannot parse member: %r' % line)
        typ, nm, arr, dim = m.group(1).strip(), m.group(2), m.group(3), m.group(4)
        if typ in PRIM:
            cs, size, rd = PRIM[typ]
            members.append({'name': nm, 'type': typ, 'cs': cs, 'size': size, 'reader': rd, 'array': arr is not None, 'dim': eval_dim(dim) if arr else None, 'nested': None})
        else:
            sub = struct_info(text, typ, cache)
            members.append({'name': nm, 'type': typ, 'cs': typ, 'size': sub['size'], 'reader': None, 'array': arr is not None, 'dim': eval_dim(dim) if arr else None, 'nested': sub})
    return members

def eval_dim(d):
    d = d.strip()
    if d == '': return -1  # flexible
    d = d.replace('MAX_STORAGE', '600').replace('MAX_INVENTORY', '100')
    return int(eval(d))

def struct_info(text, name, cache):
    if name in cache: return cache[name]
    body = find_struct(text, name)
    if body is None: raise SystemExit('struct not found: ' + name)
    members = parse_members(body, text, cache)
    size = 0
    for mem in members:
        if mem['array']:
            if mem['dim'] == -1: continue
            size += mem['size'] * mem['dim']
        else: size += mem['size']
    info = {'name': name, 'members': members, 'size': size}
    cache[name] = info
    return info

def emit(info, out, emitted):
    if info['name'] in emitted: return
    for mem in info['members']:
        if mem['nested']: emit(mem['nested'], out, emitted)
    emitted.add(info['name'])
    n = info['name']
    out.append(f'    /// <summary>rAthena struct {n}, fixed part {info["size"]} bytes.</summary>')
    out.append(f'    public sealed class {n}')
    out.append('    {')
    out.append(f'        public const int FixedSize = {info["size"]};')
    for mem in info['members']:
        if mem['array']:
            if mem['type'] == 'char' and mem['dim'] != -1:
                out.append(f'        public string {mem["name"]}; // char[{mem["dim"]}]')
            elif mem['dim'] == -1:
                out.append(f'        public System.Collections.Generic.List<{mem["cs"]}> {mem["name"]} = new System.Collections.Generic.List<{mem["cs"]}>(); // flexible array')
            else:
                out.append(f'        public {mem["cs"]}[] {mem["name"]} = new {mem["cs"]}[{mem["dim"]}];')
        else:
            out.append(f'        public {mem["cs"]} {mem["name"]};')
    out.append(f'        public static {n} Parse(ref PacketReader r, int end)')
    out.append('        {')
    out.append(f'            var p = new {n}();')
    for mem in info['members']:
        nm = mem['name']
        if mem['array']:
            if mem['type'] == 'char' and mem['dim'] != -1:
                out.append(f'            p.{nm} = r.Str({mem["dim"]});')
            elif mem['dim'] == -1:
                if mem['nested']:
                    out.append(f'            while (end - r.Position >= {mem["cs"]}.FixedSize) p.{nm}.Add({mem["cs"]}.Parse(ref r, end));')
                else:
                    out.append(f'            while (end - r.Position >= {mem["size"]}) p.{nm}.Add(r.{mem["reader"]}());')
            else:
                if mem['nested']:
                    out.append(f'            for (int i = 0; i < {mem["dim"]}; i++) p.{nm}[i] = {mem["cs"]}.Parse(ref r, end);')
                else:
                    out.append(f'            for (int i = 0; i < {mem["dim"]}; i++) p.{nm}[i] = r.{mem["reader"]}();')
        else:
            if mem['nested']:
                out.append(f'            p.{nm} = {mem["cs"]}.Parse(ref r, end);')
            else:
                out.append(f'            p.{nm} = r.{mem["reader"]}();')
    out.append('            return p;')
    out.append('        }')
    out.append('    }')

def main():
    pre_map, pre_common, outp, *names = sys.argv[1:]
    text = load([pre_common, pre_map])
    cache = {}
    out = ['// <auto-generated> by tools/packetgen/gen_structs.py from rAthena headers (PACKETVER 20211103). Do not edit. </auto-generated>',
           'namespace Loki.Net.Packets', '{']
    emitted = set()
    for n in names:
        emit(struct_info(text, n, cache), out, emitted)
    out.append('}')
    open(outp, 'w').write('\n'.join(out) + '\n')
    for n in names: print(n, cache[n]['size'])

if __name__ == '__main__': main()
