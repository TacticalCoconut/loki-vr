"""The source of a friends' package (GPL-3.0: whoever receives Loki may have its source): the client's committed source at
one commit, as the RELEASE build compiles it, zipped. the design's decision 2026-10-01: Loki is GPL-3.0.

What goes in: the repository's LICENSE and THIRD_PARTY.md, client/Assets and client/Packages (the Unity project's code), the
tools that make the first-run parts and tables (client/tools, except the smoke / playtest runners and test-only scripts),
client/release (the package scripts and guides), and SOURCE-README.md (how to build it).
What stays out: the smoke code — the files wrapped whole in `#if LOKI_SMOKE` (smoke_wrap.py's "smoke-only files") and every
`#if LOKI_SMOKE` branch (their `#else` branches kept), i.e. what a release build does not compile — the smoke / playtest runners,
the progress notes. They hold the test accounts' passwords of test servers.
It stops (nothing written) when an included file still holds a password value or a command-line password, or matches one of the
private patterns (the test accounts' passwords, the host's address, personal details: one regular expression a line, # comments)
in <private-patterns>, a file kept OUTSIDE the repository (never committed, never shipped) and required.
With --unity <Unity copy> it adds the Unity project's own files that git does not hold (Packages/manifest.json and its lock,
ProjectSettings, Assets/XR); with --release <release build> the data files that build ships (StreamingAssets: rAthena /
roBrowserLegacy-derived, made by client/tools/clientdata; never a table made from Gravity's GRF, which a release build does not
carry). Both from the copy and the build of the same commit's code.
Nothing is uploaded; an existing zip of the same name stops it.
usage: python make_source_package.py [--unity <dir>] [--release <dir>] <commit> <out.zip> <private-patterns>"""
import io, os, re, subprocess, sys, tarfile, zipfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT_PREFIX = 'Loki-source/'

EXCLUDE = [
    re.compile(r'^client/tools/smoke-[^/]+/'),
    re.compile(r'^client/tools/playtest/'),
    re.compile(r'^client/tools/online/'),  # port wave H's smoke runners for both servers (test accounts, the Unity slot): test only
    re.compile(r'^client/tools/w3data/(release-pass|stamp-and-pick)\.ps1$'),
    re.compile(r'^client/tools/w4release/release-check\.ps1$'),
    re.compile(r'^client/wave\d+/'),
    re.compile(r'^client/research/'),
    re.compile(r'^client/CLIENT_WAVE\d+\.md$'),
    re.compile(r'^client/sync\.sh$'),
    # wave 28 (wtextc): the text pack's source tables hold Gravity's own texts (the Portuguese they translate, the item texts they
    # rewrite): data, not Loki's code, and never shipped in the source (the pack itself is shipped only as configured)
    re.compile(r'^client/tools/textpack/.*\.tsv$'),
]
FORBIDDEN = [
    (re.compile(r'"password"\s*:\s*"[^"]+"'), 'a password value in JSON'),
    (re.compile(r'-lokiPass\s+\S'), 'a password on a command line'),
]


def private_patterns(path):
    if not os.path.isfile(path): raise SystemExit(f'STOPPED: no private-patterns file {path}')
    if os.path.abspath(path).lower().startswith(REPO.lower() + os.sep): raise SystemExit('STOPPED: the private-patterns file must be outside the repository')
    pats = []
    for i, ln in enumerate(open(path, encoding='utf-8'), 1):
        ln = ln.strip()
        if ln and not ln.startswith('#'): pats.append((re.compile(ln, re.I), f'private pattern on line {i} of the patterns file'))
    if not pats: raise SystemExit(f'STOPPED: {path} holds no pattern')
    return pats

SOURCE_README = """# Loki — source (GPL-3.0)

The source of the Loki client in this package, at commit {commit}: the code its release build compiles. Loki is free software
under the GNU General Public License version 3 (LICENSE). Code by others keeps its own licence (THIRD_PARTY.md and each package's
own LICENSE file under client/Packages).

Not in here: the test code (development builds only: every `#if LOKI_SMOKE` part, removed, as a release build leaves it out), the
test runners and progress notes, and no file of Gravity's — the client makes its tables from the player's own Ragnarok Online.

To build: Unity 2022.3.22f1. client/ is the Unity project (Assets, Packages with manifest.json and the packages it names in
Packages/local, ProjectSettings). First make the scene and the XR / build settings (Assets/Loki/Editor/LokiSetup.cs), then build:
    Unity.exe -batchmode -projectPath client -executeMethod Loki.EditorTools.LokiSetup.ConfigureAll -quit
    Unity.exe -batchmode -projectPath client -executeMethod Loki.EditorTools.LokiSetup.BuildWindows -lokiBuildOut <folder> -lokiRelease -quit
(Checked 2026-10-01: these two steps on this source give the shipped build's 177 files, the code byte for byte the same but for
the build's own ids — time stamp, module id, the debug file's path and hash.)
client/Assets/StreamingAssets holds the data files the release build ships, made by client/tools/clientdata (Python 3) from
rAthena's db and roBrowserLegacy: see client/tools/clientdata/README.md. The tables made from Gravity's GRF are not here: the
client makes them on the player's PC at its first start.
"""


def strip_smoke(text, path):
    """Removes what a build without LOKI_SMOKE leaves out. Returns (new text, removed lines, smoke-only file)."""
    lines = text.splitlines(keepends=True)
    stack = []  # one frame per #if: [is_smoke, in_else]
    out, removed = [], 0
    directive = re.compile(r'^\s*#\s*(if|elif|else|endif)\b(.*)$')
    for ln in lines:
        m = directive.match(ln)
        hidden = any(f[0] and not f[1] for f in stack)
        if m:
            kw, rest = m.group(1), m.group(2)
            if kw == 'if':
                smoke = 'LOKI_SMOKE' in rest
                if smoke and not re.match(r'^\s*LOKI_SMOKE\b(\s*//.*)?\s*$', rest):
                    raise SystemExit(f'STOPPED: {path}: a LOKI_SMOKE condition this script does not handle: {ln.strip()}')
                stack.append([smoke, False])
                if smoke or hidden: removed += 1
                else: out.append(ln)
                continue
            if kw == 'elif':
                if not stack: raise SystemExit(f'STOPPED: {path}: #elif without #if')
                if stack[-1][0] or 'LOKI_SMOKE' in rest: raise SystemExit(f'STOPPED: {path}: #elif in a LOKI_SMOKE block: {ln.strip()}')
                if hidden: removed += 1
                else: out.append(ln)
                continue
            if kw == 'else':
                if not stack: raise SystemExit(f'STOPPED: {path}: #else without #if')
                if stack[-1][0]:
                    stack[-1][1] = True; removed += 1
                else:
                    if hidden: removed += 1
                    else: out.append(ln)
                continue
            if kw == 'endif':
                if not stack: raise SystemExit(f'STOPPED: {path}: #endif without #if')
                f = stack.pop()
                if f[0] or any(g[0] and not g[1] for g in stack): removed += 1
                else: out.append(ln)
                continue
        if hidden: removed += 1
        else: out.append(ln)
    if stack: raise SystemExit(f'STOPPED: {path}: unclosed #if')
    new = ''.join(out)
    return new, removed, new.strip() == ''


RO_TYPES = ('.grf', '.gpf', '.thor', '.spr', '.act', '.gat', '.gnd', '.rsw', '.rsm', '.rsm2', '.gr2', '.str', '.pal', '.bmp', '.tga',
            '.wav', '.mp3', '.ogg', '.lub', '.lua', '.imf', '.ebm')


def from_disk(root, rels):
    """Files of a Unity copy / a release build (not in git), as {path under client/: bytes}."""
    out = {}
    for rel, dest in rels:
        src = os.path.join(root, rel)
        if os.path.isfile(src):
            out[dest] = open(src, 'rb').read(); continue
        if not os.path.isdir(src): raise SystemExit(f'STOPPED: {src} not found')
        for d, _, fs in os.walk(src):
            for f in fs:
                p = os.path.join(d, f)
                out[dest + '/' + os.path.relpath(p, src).replace(os.sep, '/')] = open(p, 'rb').read()
    for name in out:
        if name.lower().endswith(RO_TYPES): raise SystemExit(f'STOPPED: {name} is of an RO data type')
    return out


def main():
    args = sys.argv[1:]
    unity = release = None
    while args and args[0] in ('--unity', '--release'):
        if len(args) < 2: raise SystemExit(__doc__)
        if args[0] == '--unity': unity = args[1]
        else: release = args[1]
        args = args[2:]
    if len(args) != 3: raise SystemExit(__doc__)
    commit, out_zip = args[0], args[1]
    forbidden = FORBIDDEN + private_patterns(args[2])
    if os.path.exists(out_zip): raise SystemExit(f'STOPPED: {out_zip} exists already (nothing is overwritten)')
    sha = subprocess.check_output(['git', '-C', REPO, 'rev-parse', commit], text=True).strip()
    tar = subprocess.check_output(['git', '-C', REPO, 'archive', '--format=tar', sha, '--', 'LICENSE', 'THIRD_PARTY.md',
                                   'client/Assets', 'client/Packages', 'client/tools', 'client/release'])
    files, dropped, smoke_files, smoke_lines = {}, [], [], 0
    with tarfile.open(fileobj=io.BytesIO(tar)) as t:
        for m in t.getmembers():
            if not m.isfile(): continue
            if any(r.search(m.name) for r in EXCLUDE): dropped.append(m.name); continue
            files[m.name] = t.extractfile(m).read()
    for name in sorted(files):
        if not name.endswith('.cs'): continue
        data = files[name]
        text = data.decode('utf-8-sig') if data.startswith(b'\xef\xbb\xbf') else data.decode('utf-8')
        if 'LOKI_SMOKE' not in text: continue
        new, n, empty = strip_smoke(text, name)
        smoke_lines += n
        if empty:
            smoke_files.append(name); del files[name]
            if name + '.meta' in files: del files[name + '.meta']
        else:
            if re.search(r'^\s*#\s*(if|elif)\b[^\n]*LOKI_SMOKE', new, re.M): raise SystemExit(f'STOPPED: {name}: a LOKI_SMOKE block left after stripping')
            files[name] = (b'\xef\xbb\xbf' if data.startswith(b'\xef\xbb\xbf') else b'') + new.encode('utf-8')
    extra = {}
    if unity:  # the Unity project's own files (not in git): its package list and settings, the XR settings assets
        extra.update(from_disk(unity, [('Packages/manifest.json', 'client/Packages/manifest.json'),
                                       ('Packages/packages-lock.json', 'client/Packages/packages-lock.json'),
                                       ('ProjectSettings', 'client/ProjectSettings'), ('Assets/XR', 'client/Assets/XR'),
                                       ('Assets/XR.meta', 'client/Assets/XR.meta')]))
    if release:  # the data files a release build ships (rAthena / roBrowserLegacy-derived, made by client/tools/clientdata)
        sa = os.path.join(release, 'Loki_Data', 'StreamingAssets')
        extra.update(from_disk(sa, [(f, 'client/Assets/StreamingAssets/' + f) for f in sorted(os.listdir(sa))]))
    for name in list(extra):
        if name in files:
            if files[name] != extra.pop(name): raise SystemExit(f'STOPPED: {name} differs between git and the copy on disk')
    files.update(extra)
    files['SOURCE-README.md'] = SOURCE_README.format(commit=sha).encode('utf-8')
    problems = []
    for name, data in files.items():
        text = data.decode('utf-8', errors='ignore')
        for rx, what in forbidden:
            if rx.search(text): problems.append(f'{name}: {what}')
    if problems:
        raise SystemExit('STOPPED: private data in the source:\n  ' + '\n  '.join(problems[:40]))
    os.makedirs(os.path.dirname(os.path.abspath(out_zip)), exist_ok=True)
    with zipfile.ZipFile(out_zip, 'x', zipfile.ZIP_DEFLATED) as z:
        for name in sorted(files):
            z.writestr(OUT_PREFIX + name, files[name])
    print(f'source {out_zip}: commit {sha}, {len(files)} files, {os.path.getsize(out_zip) / 1048576:.1f} MB')
    print(f'smoke code removed: {len(smoke_files)} smoke-only files, {smoke_lines} lines in all; left out: {len(dropped)} runner / note files')
    print(f'from disk: {len(extra)} files (Unity project {unity or "-"}, release build data {release or "-"})')
    print(f'checked for password values and the {len(forbidden) - len(FORBIDDEN)} private patterns: none')


if __name__ == '__main__':
    main()
