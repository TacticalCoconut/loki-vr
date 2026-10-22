"""
Writes server/src/battle_config/data.rs: rAthena's battle_data[] (map/battle.cpp:8246-8913) as the module's run-time
battle_config needs it.

    python tools/gen_battle_data.py C:/LokiVR/rathena-src

Per entry, in battle_data order: the setting's name, the struct member, default / min / max as a RENEWAL build
compiles them, the line of battle.cpp, and `conf`: the value after rAthena's own conf/battle/*.conf were read
(conf/battle_athena.conf WITHOUT its last import, conf/import/battle_conf.txt) and BEFORE battle_adjust_conf. The
module applies the import (the host's file) and battle_adjust_conf itself (src/battle_config/mod.rs
battle_config_read), as rAthena does at every start.

The reference server runs rAthena with data/overlay/rathena-loki.patch, which adds one entry (loki_sit_regen_rate):
it is read from the patch and inserted where the patch inserts it.

The parser is datagen's (tools/datagen_mods/mobs.py read_battle_config: the same preprocessor handling, constant
table and config_switch); this file only keeps the columns datagen throws away.
"""
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def load_mobs():
    spec = importlib.util.spec_from_file_location('mobs', os.path.join(HERE, 'datagen_mods', 'mobs.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def load_datagen():
    spec = importlib.util.spec_from_file_location('datagen', os.path.join(HERE, 'datagen.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def patch_entries(patch_path):
    """The battle_data lines the Loki patch adds: [(the line before it, the added line)]."""
    out, prev = [], None
    in_battle_cpp = False
    for line in open(patch_path, encoding='latin-1').read().split('\n'):
        if line.startswith('diff --git'):
            in_battle_cpp = line.rstrip().endswith('src/map/battle.cpp')
            prev = None
            continue
        if not in_battle_cpp:
            continue
        if line.startswith('+') and not line.startswith('+++') and '&battle_config.' in line:
            assert prev is not None, 'patch: an added battle_data line without a line before it'
            out.append((prev, line[1:]))
        elif line.startswith(' '):
            prev = line[1:]
    return out


def main():
    root = sys.argv[1]
    server = os.path.dirname(HERE)
    mobs = load_mobs()
    datagen = load_datagen()
    K, DEFINES = mobs.K, mobs.DEFINES
    consts = datagen.script_constant_table(root, os.path.join(server, 'data', 'overlay'), os.path.join(HERE, 'script_constants.tsv'))

    src_lines = open(os.path.join(root, 'src/map/battle.cpp'), encoding='latin-1').read().split('\n')
    first = next(i for i, l in enumerate(src_lines) if '} battle_data[] = {' in l) + 1
    last = next(i for i, l in enumerate(src_lines) if '#include <custom/battle_config_init.inc>' in l)
    custom = open(os.path.join(root, 'src/custom/battle_config_init.inc'), encoding='latin-1').read()
    custom = re.sub(r'//.*', '', re.sub(r'/\*.*?\*/', '', custom, flags=re.S))
    assert '&battle_config.' not in custom, 'src/custom/battle_config_init.inc has entries: read them too'

    patch = patch_entries(os.path.join(server, 'data', 'overlay', 'rathena-loki.patch'))
    assert len(patch) == 1 and '"loki_sit_regen_rate"' in patch[0][1], patch

    # (line number in the clean battle.cpp or 0 for a patch line, text), the preprocessor applied as datagen does
    kept, stack = [], []
    for i in range(first, last):
        line = src_lines[i]
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
            kept.append((i + 1, line))
            for before, added in patch:
                if line == before:
                    kept.append((0, added))
    assert not stack

    syms = dict(K)
    syms.update({k.upper(): v for k, v in consts.items() if k.upper() not in syms})

    def ev(expr):
        expr = expr.strip()
        e = re.sub(r'[A-Za-z_][A-Za-z_0-9]*', lambda m: str(syms[m.group(0)]) if m.group(0) in syms else m.group(0), expr)
        if not re.fullmatch(r'[0-9xXa-fA-F\s|&~+\-*/()<>]*', e):
            raise SystemExit('battle_data: cannot evaluate %r' % expr)
        e = re.sub(r'(?<![0-9a-fA-FxX])0([0-7]+)\b', r'0o\1', e)
        return int(eval(e.replace('/', '//')))

    entries = []  # [name, member, defval, min, max, line]
    for lineno, line in kept:
        code = re.sub(r'/\*.*?\*/', '', line)
        code = re.sub(r'//.*', '', code)
        if not code.strip():
            continue
        m = re.fullmatch(r'\s*\{\s*"([^"]+)"\s*,\s*&battle_config\.(\w+)\s*,([^,]+),([^,]+),([^,}]+),?\s*\}\s*,?\s*', code)
        if not m:
            raise SystemExit('battle_data: line %d not understood: %r' % (lineno, line))
        d, lo, hi = ev(m.group(3)), ev(m.group(4)), ev(m.group(5))
        for v in (d, lo, hi):
            assert -2**31 <= v < 2**31, (m.group(1), v)
        entries.append([m.group(1), m.group(2), d, lo, hi, lineno])
    names = [e[0] for e in entries]
    # battle_data lists `fame_refine_lv1` twice (battle.cpp:8732-8733), both for the same member: ARR_FIND only ever
    # finds the first, battle_set_defaults writes the member twice. The table keeps both lines (ARRAYLENGTH and the
    # order are rAthena's); the second gets no Bc name and its slot is never read.
    dup = set()
    for i, e in enumerate(entries):
        j = next(k for k, f in enumerate(entries) if f[0].lower() == e[0].lower())
        if j != i:
            assert entries[j][1:5] == e[1:5], 'battle_data: %s twice with other values' % e[0]
            dup.add(i)
    print('  listed twice in battle_data: %s' % ', '.join(entries[i][0] for i in sorted(dup)))

    # conf/battle_athena.conf without conf/import/battle_conf.txt: rAthena's own files only (never the overlay)
    by_name = {}
    for i, e in enumerate(entries):
        by_name.setdefault(e[0].lower(), i)  # ARR_FIND: the first match
    conf = [e[2] for e in entries]  # battle_set_defaults
    warnings = []

    def read(rel):
        if rel.replace('\\', '/') == 'conf/import/battle_conf.txt':
            return
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            raise SystemExit('File not found: %s' % rel)
        for raw in open(path, encoding='latin-1'):
            line = raw[:1023]
            if line.startswith('//'):
                continue
            m = re.match(r'([^:]+):\s*(\S+)', line)
            if not m:
                continue
            w1, w2 = m.group(1), m.group(2)
            if w1.lower() == 'import':
                read(w2)
            elif w1.lower() in ('atcommand_symbol', 'charcommand_symbol'):
                pass
            elif w1.lower() in by_name:
                i = by_name[w1.lower()]
                v = mobs.config_switch(w2)
                if v < entries[i][3] or v > entries[i][4]:
                    warnings.append('%s: %s outside [%d, %d], default %d' % (w1, w2, entries[i][3], entries[i][4], entries[i][2]))
                    v = entries[i][2]
                conf[i] = v
            else:
                warnings.append('Unknown setting %r in %s' % (w1, rel))
    read('conf/battle_athena.conf')
    for w in warnings:
        print('  [Warning] ' + w)

    def ident(name):
        s = re.sub(r'[^A-Za-z0-9]', '_', name).upper()
        assert re.fullmatch(r'[A-Z_][A-Z0-9_]*', s), name
        return s
    idents = [ident(n) for n in names]
    assert len(set(idents)) == len(idents) - len(dup), 'two settings give one Rust name'

    def num(v):
        return 'i32::MAX' if v == 2**31 - 1 else 'i32::MIN' if v == -2**31 else str(v)

    out = []
    out.append('//! GENERATED by tools/gen_battle_data.py from rAthena e985006 src/map/battle.cpp battle_data[] (battle.cpp:%d-%d,' % (first + 1, last))
    out.append('//! as a RENEWAL build compiles it), conf/battle/*.conf and data/overlay/rathena-loki.patch. Do not edit: run')
    out.append('//! `python tools/gen_battle_data.py <rathena-src>` again.')
    out.append('//!')
    out.append('//! `conf` is the setting after rAthena\'s own conf/battle/*.conf were read, before conf/import/battle_conf.txt and')
    out.append('//! before battle_adjust_conf.')
    out.append('')
    out.append('use super::{BattleData, Bc};')
    out.append('')
    out.append('/// ARRAYLENGTH(battle_data).')
    out.append('pub const N: usize = %d;' % len(entries))
    out.append('')
    out.append('#[rustfmt::skip]')
    out.append('impl Bc {')
    for i, (name, member, d, lo, hi, lineno) in enumerate(entries):
        if i in dup:
            continue
        where = 'battle.cpp:%d' % lineno if lineno else 'rathena-loki.patch'
        out.append('    /// `%s` (battle_config.%s, %s).' % (name, member, where))
        out.append('    pub const %s: Bc = Bc(%d);' % (idents[i], i))
    out.append('}')
    out.append('')
    out.append('#[rustfmt::skip]')
    out.append('pub static BATTLE_DATA: [BattleData; N] = [')
    for i, (name, member, d, lo, hi, lineno) in enumerate(entries):
        out.append('    BattleData { name: "%s", defval: %s, min: %s, max: %s, conf: %s },' % (name, num(d), num(lo), num(hi), num(conf[by_name[name.lower()]])))
    out.append('];')
    out.append('')
    dest = os.path.join(server, 'src', 'battle_config', 'data.rs')
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(out))
    print('%s: %d settings (%d differ from their default in conf/battle)' % (dest, len(entries), sum(1 for i, e in enumerate(entries) if conf[i] != e[2])))


if __name__ == '__main__':
    main()
