#!/usr/bin/env python3
r"""
The Lua dumpers of the client-data pipeline, ported to Python on lub51.py (client wave 3b, w3data): no lua5.1 exists on
this PC (nor in the WSL distro), and the tools that read their output (build_tables.py, build_account_tables.py,
build_qol_tables.py, ../appearance/build_appearance.py) stay unchanged. Each sub-command writes, byte for byte, what the
Lua script it names writes when a stock lua5.1 runs it (numbers as Lua's tostring / f:write print them: "%.14g").

  python lubdump.py items        <iteminfo_new.lub> <out.tsv>        dumpitems.lua
  python lubdump.py quests       <OngoingQuestInfoList_True.lub> <out.tsv>   dumpquests.lua (the KEY lines it writes to stderr: not needed)
  python lubdump.py achievements <achievement_list.lub> <out.tsv>    dump_achievements.lua
  python lubdump.py titles       <data.grf> <titletable path in the GRF> <out.tsv>   dump_titles.lua
  python lubdump.py emotions     <data.grf> <out.txt>                dump_emotions.lua (its stdout: the rows and CALL lines)
  python lubdump.py stateicons   <data.grf> <out.txt>                dump_stateicons.lua (its stdout)
  python lubdump.py lubs         <data.grf> <out.tsv>                ../appearance/dump_lubs.lua (its stdout)
  python lubdump.py classnum     <iteminfo_new.lub | itemInfo.lua> <out.tsv>   ../appearance/dump_classnum.lua (its stdout)

A .lub file is run by lub51.py; itemInfo.lua (plain Lua source) is read by luasrc.py (a Lua 5.1 lexer and the part of the
parser a data file needs). Where the Lua script walks a table with pairs() and the order could show in its output, the
comment says why it cannot here (lub51's pairs() is insertion order, Lua 5.1's is its hash order).
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import grf as grfmod  # noqa: E402
import lub51  # noqa: E402

LUA = 'data\\luafiles514\\lua files\\'
# lua5.1's base library and library tables (lbaselib.c, linit.c): a chunk that reads one of these gets it from lua5.1
LUA_STDLIB = {x.encode() for x in ('assert collectgarbage dofile error gcinfo getfenv getmetatable ipairs load loadfile loadstring '
              'module newproxy next pairs pcall print rawequal rawget rawset require select setfenv setmetatable tonumber '
              'tostring type unpack xpcall _G _VERSION coroutine debug io math os package string table').split()}


class LuaScriptError(Exception):
    pass


def fresh():
    """A new Lua state: every dumper is its own lua5.1 process."""
    lub51.GLOBALS = lub51.LuaTable()
    lub51._setup()
    return lub51.GLOBALS


def num_str(v):
    """lua5.1 tostring / io.write / print of a number: LUA_NUMBER_FMT "%.14g" (luaconf.h)."""
    return ('%.14g' % float(v)).encode()


def tostring(v):
    """lua5.1 tostring() of the values a data file holds (lbaselib.c luaB_tostring)."""
    if v is None: return b'nil'
    if v is True: return b'true'
    if v is False: return b'false'
    if isinstance(v, bytes): return v
    if isinstance(v, (int, float)): return num_str(v)
    raise LuaScriptError('tostring of %r: the Lua script would print an address here' % (v,))


def write_arg(v):
    """f:write / io.write argument (liolib.c g_write: numbers with LUA_NUMBER_FMT, strings as they are, anything else is an error)."""
    if isinstance(v, bytes): return v
    if isinstance(v, (int, float)) and not isinstance(v, bool): return num_str(v)
    raise LuaScriptError('f:write of %r' % (v,))


def hexs(s):
    """hex(s) of the scripts: every byte as %02x (a Lua pattern '.' matches every byte)."""
    if not isinstance(s, bytes): raise LuaScriptError('hex of %r (s:gsub on a non-string)' % (s,))
    return s.hex().encode()


def lua_or(v, d):
    return v if lub51.truthy(v) else d


def field(t, k):
    """t.k where the script indexes t (an error in Lua when t is not a table)."""
    if not isinstance(t, lub51.LuaTable): raise LuaScriptError('attempt to index %r' % (t,))
    return t.get(k)


def ipairs_list(t):
    """ipairs(t): t[1], t[2], ... up to the first nil (lbaselib.c ipairsaux)."""
    if not isinstance(t, lub51.LuaTable): raise LuaScriptError('ipairs over %r' % (t,))
    out, i = [], 1
    while True:
        v = t.get(i)
        if v is None: return out
        out.append(v); i += 1


def sorted_keys(t):
    """for k in pairs(t) do keys[#keys+1] = k end table.sort(keys): the default '<' (numbers, or strings; a mix is an error)."""
    keys = [k for k, _ in t.items()]
    if all(isinstance(k, (int, float)) and not isinstance(k, bool) for k in keys): return sorted(keys)
    if all(isinstance(k, bytes) for k in keys): return sorted(keys)  # strcoll in the C locale = byte order
    raise LuaScriptError('table.sort over mixed keys')


def run_file(path_or_bytes):
    data = open(path_or_bytes, 'rb').read() if isinstance(path_or_bytes, str) else path_or_bytes
    if data[:4] == b'\x1bLua': lub51.run(data)
    else:
        import luasrc
        luasrc.run(data, lub51.GLOBALS)


# ---------------------------------------------------------------- dumpitems.lua
def items(lub, out):
    G = fresh(); run_file(lub)
    t = G.get(b'tbl')
    lines = []
    for k in sorted_keys(t):
        v = t.get(k)
        desc = [hexs(d) for d in ipairs_list(lua_or(field(v, b'identifiedDescriptionName'), lub51.LuaTable()))]
        lines.append(b''.join([write_arg(k), b'\t', hexs(lua_or(field(v, b'identifiedDisplayName'), b'')), b'\t',
                               hexs(lua_or(field(v, b'identifiedResourceName'), b'')), b'\t', tostring(lua_or(field(v, b'slotCount'), 0)), b'\t',
                               tostring(lua_or(field(v, b'ClassNum'), 0)), b'\t', hexs(lua_or(field(v, b'unidentifiedDisplayName'), b'')), b'\t',
                               hexs(lua_or(field(v, b'unidentifiedResourceName'), b'')), b'\t', b','.join(desc), b'\n']))
    open(out, 'wb').write(b''.join(lines))
    print('items', len(lines))


# ---------------------------------------------------------------- dumpquests.lua
def quests(lub, out):
    G = fresh(); run_file(lub)
    Q = G.get(b'QuestInfoList')
    def hx(s): return tostring(s).hex().encode()  # dumpquests.lua hex(s): tostring(s) first
    lines = []
    for qid in sorted_keys(Q):
        v = Q.get(qid)
        d = [tostring(s).hex().encode() for s in ipairs_list(lua_or(field(v, b'Description'), lub51.LuaTable()))]
        lines.append(b''.join([write_arg(qid), b'\t', hx(lua_or(field(v, b'Title'), b'')), b'\t', hx(lua_or(field(v, b'Summary'), b'')), b'\t',
                               b','.join(d), b'\t', hx(lua_or(field(v, b'NpcSpr'), b'')), b'\t', hx(lua_or(field(v, b'NpcNavi'), b'')), b'\t',
                               tostring(lua_or(field(v, b'NpcPosX'), b'')), b'\t', tostring(lua_or(field(v, b'NpcPosY'), b'')), b'\t',
                               hx(lua_or(field(v, b'IconName'), b'')), b'\n']))
    open(out, 'wb').write(b''.join(lines))
    print('quests', len(lines))


# ---------------------------------------------------------------- dump_achievements.lua
def achievements(lub, out):
    G = fresh(); run_file(lub)
    A = G.get(b'achievement_tbl')
    def hx(s): return tostring(lua_or(s, b'')).hex().encode()
    lines = []
    for aid in sorted_keys(A):
        a = A.get(aid)
        c = lua_or(field(a, b'content'), lub51.LuaTable())
        r = lua_or(field(a, b'reward'), lub51.LuaTable())
        cols = [aid, lua_or(field(a, b'UI_Type'), 0), hx(field(a, b'group')), lua_or(field(a, b'major'), 0), lua_or(field(a, b'minor'), 0),
                lua_or(field(a, b'score'), 0), hx(field(a, b'title')), hx(field(c, b'summary')), hx(field(c, b'details')),
                lua_or(field(r, b'item'), 0), lua_or(field(r, b'title'), 0), lua_or(field(r, b'buff'), 0)]
        res = lua_or(field(a, b'resource'), lub51.LuaTable())
        for k in sorted_keys(res):
            x = res.get(k)
            cols.append(hx(field(x, b'text')) + b'|' + tostring(lua_or(field(x, b'count'), 0)) + b'|' + tostring(lua_or(field(x, b'shortcut'), 0)))
        lines.append(b'\t'.join(tostring(v) for v in cols) + b'\n')
    open(out, 'wb').write(b''.join(lines))
    print('achievements', len(lines))


# ---------------------------------------------------------------- dump_titles.lua
def titles(grf_path, lub_path, out):
    g = grfmod.Grf(grf_path)
    data = g.read(lub_path)
    if data is None: raise SystemExit('missing in the GRF: ' + lub_path)
    G = fresh(); run_file(data)
    T = G.get(b'title_tbl')
    lines = [write_arg(i) + b'\t' + tostring(lua_or(T.get(i), b'')).hex().encode() + b'\n' for i in sorted_keys(T)]
    open(out, 'wb').write(b''.join(lines))
    print('titles', len(lines))


# ---------------------------------------------------------------- dump_emotions.lua
def emotions(grf_path, out):
    """rows[] in call order: InsertEmotionListTable(...) as "a | b | c", every other undefined global called as
    "CALL name(args)" (the script's __index on _G hands back a recorder for ANY undefined global, called or not, as here).
    The TABLE / CONST lines the script prints after them walk _G with pairs(); build_qol_tables.py matches neither kind
    (its two patterns need "CALL " or "digits | digits | digits"), so they are left out."""
    g = grfmod.Grf(grf_path)
    data = g.read(LUA + 'emotion\\emotionlist.lub')
    if data is None: raise SystemExit('missing in the GRF: emotionlist.lub')
    G = fresh()
    rows = []
    G.set(b'c_GetMsgString', lambda i=None, *rest: [b'MSG' + tostring(i)])
    def insert(*a): rows.append(b' | '.join(tostring(x) for x in a)); return []
    G.set(b'InsertEmotionListTable', insert)
    meta = lub51.LuaTable()
    def index(t, k):
        if k in LUA_STDLIB:  # lua5.1 has it; lub51.py does not: the recorder would stand in for it and the rows would differ
            raise LuaScriptError('emotionlist.lub uses %r, which lub51.py does not provide' % k)
        def rec(*a):
            rows.append(b'CALL ' + tostring(k) + b'(' + b', '.join(tostring(x) for x in a) + b')')
            return []
        return [rec]
    meta.set(b'__index', index)
    G.meta = meta
    run_file(data)
    open(out, 'wb').write(b''.join(r + b'\n' for r in rows))
    print('emotion rows', len(rows))


# ---------------------------------------------------------------- dump_stateicons.lua
def stateicons(grf_path, out):
    """print(efst, byid[efst] or "?", pri, file) for every StateIconImgList[pri][efst]. Lua walks both tables with pairs();
    build_qol_tables.py keys the lines by efst, so their order shows only when an efst is under two priorities (then the
    last line wins): this tool stops if that happens (the 2026-09-30 GRF has none)."""
    g = grfmod.Grf(grf_path)
    G = fresh()
    for f in ('stateicon\\efstids.lub', 'stateicon\\stateiconimginfo.lub'):
        data = g.read(LUA + f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        run_file(data)
    byid = {}
    for name, i in G.get(b'EFST_IDs').items():
        if lub51.norm(i) in byid: raise SystemExit('EFST_IDs gives two names the id %r: Lua pairs() order would pick one' % (i,))
        byid[lub51.norm(i)] = name
    lines, seen = [], set()
    for pri, t in G.get(b'StateIconImgList').items():
        for efst, f in t.items():
            if efst in seen: raise SystemExit('efst %r under two priorities: Lua pairs() order would pick the line' % (efst,))
            seen.add(efst)
            lines.append(b'\t'.join([tostring(efst), tostring(byid.get(lub51.norm(efst), b'?')), tostring(pri), tostring(f)]) + b'\n')
    open(out, 'wb').write(b''.join(lines))
    print('stateicon lines', len(lines))


# ---------------------------------------------------------------- ../appearance/dump_lubs.lua
def lubs(grf_path, out):
    g = grfmod.Grf(grf_path)
    G = fresh()
    for f in ('datainfo\\accessoryid.lub', 'datainfo\\weapontable.lub', 'datainfo\\tb_layer_priority.lub'):
        data = g.read(LUA + f)
        if data is None: raise SystemExit('missing in the GRF: ' + f)
        run_file(data)
    lines = []
    E = G.get(b'Expansion_Weapon_IDs')
    for v in sorted_keys(E): lines.append(b'\t'.join([b'wexp', tostring(v), tostring(E.get(v))]))
    T = G.get(b'TB_Layer_Priority')
    for n in (b'Mid', b'Top', b'Bottom', b'Robe'): lines.append(b'\t'.join([b'layerdef', n, tostring(field(T, b'Default_' + n))]))
    items_list = field(T, b'Items_List')
    for i in sorted_keys(items_list):
        e = items_list.get(i)
        dirs = []
        direction = field(e, b'Direction')
        if lub51.truthy(direction):
            for d in sorted_keys(direction): dirs.append(concat(d) + b'=' + concat(direction.get(d)))
        default = field(e, b'Default')
        lines.append(b'\t'.join([b'layer', tostring(i), tostring(default) if default is not None else b'-', b','.join(dirs)]))
    open(out, 'wb').write(b''.join(x + b'\n' for x in lines))
    print('lub rows', len(lines))


def concat(v):
    """An operand of Lua's '..' (lvm.c luaV_concat: strings, numbers through LUA_NUMBER_FMT, anything else an error)."""
    if isinstance(v, bytes): return v
    if isinstance(v, (int, float)) and not isinstance(v, bool): return num_str(v)
    raise LuaScriptError('attempt to concatenate %r' % (v,))


# ---------------------------------------------------------------- ../appearance/dump_classnum.lua
def classnum(path, out):
    G = fresh(); run_file(path)
    t = G.get(b'tbl')
    lines = [write_arg(k) + b'\t' + tostring(lua_or(field(t.get(k), b'ClassNum'), 0)) + b'\n' for k in sorted_keys(t)]
    open(out, 'wb').write(b''.join(lines))
    print('classnum', len(lines))


def main(argv):
    cmds = {'items': (items, 2), 'quests': (quests, 2), 'achievements': (achievements, 2), 'titles': (titles, 3),
            'emotions': (emotions, 2), 'stateicons': (stateicons, 2), 'lubs': (lubs, 2), 'classnum': (classnum, 2)}
    if not argv or argv[0] not in cmds or len(argv) - 1 != cmds[argv[0]][1]: print(__doc__); return 2
    cmds[argv[0]][0](*argv[1:])
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
