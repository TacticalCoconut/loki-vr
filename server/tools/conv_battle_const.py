"""conv_conf.py <file>... [--exclude NAME,NAME] [--only NAME,NAME] [--log FILE]

Turns each module-level `const NAME: T = V;` of the files that is a battle_config member with the reference value into
`fn name() -> T` reading battle_config::get, then lets the compiler say where each name is used: every
"cannot find value `NAME`" (E0425) span becomes `name()`, every unresolved import (E0432) item becomes `name`; a
module-level constant that now calls one of them (E0015: an alias or a derived value) becomes a function too.
Reports anything else. Run from server/. Appends "file NAME Bc::IDENT" lines to the log.
"""
import json
import os
import re
import subprocess
import sys

args = sys.argv[1:]
exclude, only, logf = set(), None, None
files = []
i = 0
while i < len(args):
    if args[i] == '--exclude':
        exclude |= set(x for x in args[i + 1].split(',') if x)
        i += 2
    elif args[i] == '--only':
        only = set(x for x in args[i + 1].split(',') if x)
        i += 2
    elif args[i] == '--log':
        logf = args[i + 1]
        i += 2
    else:
        files.append(args[i])
        i += 1

data = open('src/battle_config/data.rs', encoding='utf-8').read()
members = {}
for m in re.finditer(r'/// `([^`]+)` \(battle_config\.(\w+), [^)]*\)\.\n\s+pub const (\w+): Bc = Bc\((\d+)\);', data):
    members[m.group(2)] = (m.group(3), int(m.group(4)), m.group(1))
names = {v[2].replace('.', '_'): k for k, v in members.items()}
rows = re.findall(r'BattleData \{ name: "([^"]+)", defval: ([^,]+), min: ([^,]+), max: ([^,]+), conf: ([^ ]+) \}', data)


def num(x):
    return 2**31 - 1 if x == 'i32::MAX' else -2**31 if x == 'i32::MIN' else int(x)


flags = open('src/script/battle_flags.rs', encoding='utf-8').read()
defaults = [int(m.group(1)) for m in re.finditer(r'^\s+\("[^"]+", (-?\d+)\),', flags, re.M)]
TY = {'u8': (0, 255), 'u16': (0, 65535), 'i16': (-32768, 32767), 'u32': (0, 2**32 - 1), 'i32': (-2**31, 2**31 - 1),
      'i64': (-2**63, 2**63 - 1), 'u64': (0, 2**64 - 1), 'usize': (0, 2**32 - 1), 'i8': (-128, 127)}
CONST = re.compile(r'^((?:pub(?:\([a-z:]+\))? )?)const (\w+): ([\w:<>&\[\]\' ]+) = ([^;]+);', re.M)
GET = 'crate::battle_config::get(crate::battle_config::Bc::%s)'

converted = {}  # NAME -> lower
report = []
log = []
for f in files:
    t = open(f, encoding='utf-8').read()
    done = []

    def repl(m, f=f, done=done):
        vis, name, ty, val = m.group(1), m.group(2), m.group(3).strip(), m.group(4).strip()
        if name in exclude or (only is not None and name not in only):
            return m.group(0)
        low = name.lower()
        member = low if low in members else names.get(low)
        if member is None:
            return m.group(0)
        ident, idx, sname = members[member]
        vs = val.replace('_', '')
        try:
            v = 1 if vs == 'true' else 0 if vs == 'false' else int(vs, 0)
        except ValueError:
            try:
                v = int(eval(vs, {'__builtins__': {}}))
            except Exception:
                return m.group(0)
        if v != defaults[idx]:
            return m.group(0)
        _, d, lo, hi, _ = rows[idx]
        d, lo, hi = num(d), num(lo), num(hi)
        lo, hi = min(lo, d), max(hi, d)  # what battle_set_value can leave in the member
        note = ''
        if ty == 'bool':
            body = (GET % ident) + ' != 0'
        elif ty == 'i32':
            body = GET % ident
        elif ty in TY:
            tlo, thi = TY[ty]
            if lo >= tlo and hi <= thi:
                body = (GET % ident) + ' as ' + ty
            else:
                clo, chi = max(tlo, -2**31), min(thi, 2**31 - 1)
                body = (GET % ident) + '.clamp(%s, %s) as %s' % (clo if clo != -2**31 else 'i32::MIN', chi if chi != 2**31 - 1 else 'i32::MAX', ty)
                note = ' (saturated to ' + ty + ')'
        else:
            return m.group(0)
        converted[name] = low
        done.append(name + note)
        log.append('%s %s Bc::%s%s' % (f, name, ident, note))
        return '%sfn %s() -> %s {\n    %s\n}' % (vis, low, ty, body)

    t2 = CONST.sub(repl, t)
    if t2 != t:
        open(f, 'w', encoding='utf-8', newline='\n').write(t2)
    report.append((f, done))

env = dict(os.environ)
env['CARGO_BUILD_JOBS'] = env.get('LCARGO_JOBS', '6')
problems = []
for rnd in range(16):
    p = subprocess.run(['cargo', 'check', '--lib', '--tests', '--message-format=json'], capture_output=True, text=True, env=env, encoding='utf-8', errors='replace')
    fixes = {}  # file -> [(line, c0, c1, new)]
    aliases = {}  # file -> set(line)
    nerr = 0
    problems = []
    for line in p.stdout.split('\n'):
        if not line.startswith('{'):
            continue
        try:
            j = json.loads(line)
        except ValueError:
            continue
        if j.get('reason') != 'compiler-message':
            continue
        msg = j['message']
        level = msg['level']
        if level not in ('error', 'warning'):
            continue
        code = (msg.get('code') or {}).get('code')
        text = msg['message']
        if level == 'error':
            nerr += 1
        handled = False
        for sp in msg['spans']:
            if not sp.get('is_primary'):
                continue
            if sp['line_start'] != sp['line_end'] or not sp['text']:
                continue
            src = sp['text'][0]['text']
            seg = src[sp['column_start'] - 1:sp['column_end'] - 1]
            last = seg.split('::')[-1]
            fn = sp['file_name'].replace('\\', '/')
            if code == 'E0425' and last in converted:
                c0 = sp['column_end'] - 1 - len(last)
                fixes.setdefault(fn, []).append((sp['line_start'], c0, sp['column_end'] - 1, converted[last] + '()'))
                handled = True
            elif code == 'E0432' and last in converted:
                c0 = sp['column_end'] - 1 - len(last)
                fixes.setdefault(fn, []).append((sp['line_start'], c0, sp['column_end'] - 1, converted[last]))
                handled = True
            elif code == 'E0015' and CONST.match(src):
                aliases.setdefault(fn, set()).add(sp['line_start'])
                handled = True
        if not handled:
            where = ''
            for sp in msg['spans']:
                if sp.get('is_primary'):
                    where = '%s:%d' % (sp['file_name'].replace('\\', '/'), sp['line_start'])
            problems.append('%s %s %s: %s' % (level, code, where, text[:200]))
    if not fixes and not aliases:
        break
    n_alias = 0
    for fn in set(fixes) | set(aliases):
        lines = open(fn, encoding='utf-8').read().split('\n')
        for (ln, c0, c1, new) in sorted(set(fixes.get(fn, [])), key=lambda x: (x[0], -x[1])):
            s = lines[ln - 1]
            lines[ln - 1] = s[:c0] + new + s[c1:]
        for ln in aliases.get(fn, ()):
            m = CONST.match(lines[ln - 1])
            if not m:
                continue
            vis, name, ty, expr = m.group(1), m.group(2), m.group(3).strip(), m.group(4).strip()
            lines[ln - 1] = '%sfn %s() -> %s {\n    %s\n}' % (vis, name.lower(), ty, expr)
            converted[name] = name.lower()
            log.append('%s %s alias: %s' % (fn, name, expr))
            n_alias += 1
        open(fn, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
    print('round %d: %d errors, %d spans fixed, %d constants that read one became functions' % (rnd + 1, nerr, sum(len(v) for v in fixes.values()), n_alias))

total = 0
for f, done in report:
    if done:
        total += len(done)
        print('%s: %d: %s' % (f, len(done), ' '.join(done)))
print('converted %d constants' % total)
if logf:
    with open(logf, 'a', encoding='utf-8', newline='\n') as fh:
        for l in log:
            fh.write(l + '\n')
print('left: %d problems' % len(problems))
for pr in sorted(set(problems))[:80]:
    print('  ' + pr)
