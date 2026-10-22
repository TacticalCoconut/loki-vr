using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loki.Data
{
    /// <summary>What a first-run tool reads (client wave 3b, w3data): the player's own RO install and the parts the build ships.</summary>
    public interface IToolInputs
    {
        /// <summary>A file of the RO data as grf.py's Grf.read gives it (RO path, any case / slash style), or null when absent.</summary>
        byte[] Grf(string roPath);
        /// <summary>A file of the install's own folder (e.g. "System\iteminfo_new.lub"), or null when absent.</summary>
        byte[] RoFile(string relPath);
        /// <summary>A shipped part (StreamingAssets/data/firstrun/&lt;name&gt;), or null when absent.</summary>
        byte[] Part(string name);
        /// <summary>os.path.basename of the GRF the tools name in their header lines ("data.grf").</summary>
        string GrfName { get; }
        /// <summary>The GRF version (0x200 / 0x300), as the tools' headers print it.</summary>
        int GrfVersion { get; }
        /// <summary>Client wave 20 (w20rooms): whether the RO data holds the file (loose or in an archive), without reading it.</summary>
        bool Exists(string roPath);
    }

    /// <summary>A tool stops (the Python tool's SystemExit / exception): its tables are not written.</summary>
    public sealed class ToolError : Exception { public ToolError(string m) : base(m) { } }

    /// <summary>What a tool made: files by path under the data folder (e.g. "account/titles.tsv"), and its summary line.</summary>
    public sealed class ToolOutput
    {
        public readonly List<KeyValuePair<string, byte[]>> Files = new List<KeyValuePair<string, byte[]>>();
        public string Summary = "";
        public void Add(string rel, byte[] data) => Files.Add(new KeyValuePair<string, byte[]>(rel, data));
        public void AddUtf8(string rel, string text) => Add(rel, Py.Utf8NoBom.GetBytes(text));
    }

    /// <summary>Python idioms the tool ports share.</summary>
    public static class T
    {
        public static bool IsInt(object v) => v is long || v is bool;                       // isinstance(v, int)
        public static bool IsNum(object v) => v is long || v is bool || v is double;          // isinstance(v, (int, float))
        public static long IntVal(object v) => v is bool b ? (b ? 1 : 0) : (long)v;
        public static string IntStr(object v) => v is bool b ? (b ? "True" : "False") : Py.IntStr((long)v);  // str() of an int
        /// <summary>str(v) of a number (int, float or bool).</summary>
        public static string NumStr(object v) => v is double d ? Py.ReprFloat(d) : IntStr(v);

        /// <summary>Python == between two values of these tools (numbers by value, bytes by content, tables by identity).</summary>
        public static bool PyEq(object a, object b)
        {
            if (IsNum(a) && IsNum(b)) return a is double || b is double ? D(a) == D(b) : IntVal(a) == IntVal(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is LuaStr sa) return sa.Equals(b);
            return ReferenceEquals(a, b);
        }
        private static double D(object v) => v is double d ? d : IntVal(v);

        public static int CompareNum(object a, object b)
        {
            if (a is double || b is double) return D(a).CompareTo(D(b));
            return IntVal(a).CompareTo(IntVal(b));
        }

        /// <summary>sorted(): a stable sort.</summary>
        public static List<X> Sorted<X>(IEnumerable<X> items, Comparison<X> cmp) => items.OrderBy(x => x, Comparer<X>.Create(cmp)).ToList();

        /// <summary>sorted(k for k in t.h if isinstance(k, int)).</summary>
        public static List<object> IntKeys(LuaTable t) => Sorted(t.H.Keys().Where(IsInt), CompareNum);

        /// <summary>seq(t): t[1] … t[#t] (lub51 length), [] for a non-table.</summary>
        public static List<object> Seq(object t)
        {
            var o = new List<object>();
            if (!(t is LuaTable lt)) return o;
            long n = lt.Length();
            for (long i = 1; i <= n; i++) o.Add(lt.Get(i));
            return o;
        }

        /// <summary>dec(b) of build_skilltree.py / build_equip_tables.py: '' for None, else UTF-8 → windows-1252 → Latin-1.</summary>
        public static string Dec(object b)
        {
            if (b == null) return "";
            if (b is LuaStr s) return Py.Dec(s.B);
            throw new ToolError("dec() of " + Lua.Repr(b) + " (Python: AttributeError)");
        }

        /// <summary>bytes.decode() (UTF-8, strict).</summary>
        public static string Utf8(LuaStr s) => Py.TryUtf8(s.B) ?? throw new ToolError("'utf-8' codec can't decode " + Lua.Repr(s));

        public static LuaTable Table(object v, string what) => v as LuaTable ?? throw new ToolError(what + " is not a table (Python: AttributeError)");

        public static byte[] Need(IToolInputs inp, string roPath)
        {
            var d = inp.Grf(roPath);
            if (d == null) throw new ToolError("missing in the GRF: " + roPath);
            return d;
        }

        public static string Hex3(int v) => v.ToString("x");
    }

    /// <summary>
    /// client/tools/clientdata/lubdump.py ported: the Lua dumpers' output bytes (what dumpitems.lua, dumpquests.lua,
    /// dump_achievements.lua, dump_titles.lua, dump_emotions.lua, dump_stateicons.lua, ../appearance/dump_lubs.lua and
    /// ../appearance/dump_classnum.lua write under lua5.1), made from the same lub runs.
    /// </summary>
    public static class LubDump
    {
        public const string LuaDir = "data\\luafiles514\\lua files\\";

        private static readonly HashSet<string> LuaStdlib = new HashSet<string>(
            ("assert collectgarbage dofile error gcinfo getfenv getmetatable ipairs load loadfile loadstring module newproxy next pairs pcall " +
             "print rawequal rawget rawset require select setfenv setmetatable tonumber tostring type unpack xpcall _G _VERSION coroutine debug " +
             "io math os package string table").Split(' '), StringComparer.Ordinal);

        /// <summary>lubdump.py num_str: "%.14g".</summary>
        private static byte[] NumStr(object v) => Encoding.ASCII.GetBytes(Py.G14(v is double d ? d : T.IntVal(v)));

        /// <summary>lubdump.py tostring (lua5.1 tostring of data values).</summary>
        public static byte[] ToString(object v)
        {
            switch (v)
            {
                case null: return Encoding.ASCII.GetBytes("nil");
                case bool b: return Encoding.ASCII.GetBytes(b ? "true" : "false");
                case LuaStr s: return s.B;
                case long _: case double _: return NumStr(v);
            }
            throw new ToolError("tostring of " + Lua.Repr(v) + ": the Lua script would print an address here");
        }

        private static byte[] WriteArg(object v)
        {
            if (v is LuaStr s) return s.B;
            if (v is long || v is double) return NumStr(v);
            throw new ToolError("f:write of " + Lua.Repr(v));
        }

        private static byte[] Hexs(object s)
        {
            if (!(s is LuaStr ls)) throw new ToolError("hex of " + Lua.Repr(s) + " (s:gsub on a non-string)");
            return Encoding.ASCII.GetBytes(Py.Hex(ls.B));
        }

        private static byte[] HexStr(object v) => Encoding.ASCII.GetBytes(Py.Hex(ToString(v)));
        private static object Or(object v, object d) => Lua.Truthy(v) ? v : d;
        private static readonly LuaStr Empty = new LuaStr(new byte[0]);

        private static object Field(object t, string k)
        {
            if (!(t is LuaTable lt)) throw new ToolError("attempt to index " + Lua.Repr(t));
            return lt.Get(k);
        }

        private static List<object> IpairsList(object t)
        {
            if (!(t is LuaTable lt)) throw new ToolError("ipairs over " + Lua.Repr(t));
            var o = new List<object>();
            for (long i = 1; ; i++) { var v = lt.Get(i); if (v == null) return o; o.Add(v); }
        }

        /// <summary>for k in pairs(t) … table.sort(keys): numbers, or strings in byte order; a mix is an error.</summary>
        private static List<object> SortedKeys(object t)
        {
            var keys = ((LuaTable)t).H.Keys();
            if (keys.All(k => k is long || k is double)) return T.Sorted(keys, T.CompareNum);
            if (keys.All(k => k is LuaStr)) return T.Sorted(keys, (a, b) => ((LuaStr)a).CompareTo((LuaStr)b));
            throw new ToolError("table.sort over mixed keys");
        }

        private static byte[] Concat(object v)
        {
            if (v is LuaStr s) return s.B;
            if (v is long || v is double) return NumStr(v);
            throw new ToolError("attempt to concatenate " + Lua.Repr(v));
        }

        private sealed class Out
        {
            private readonly List<byte> _b = new List<byte>();
            public int Lines;
            public Out W(byte[] x) { _b.AddRange(x); return this; }
            public Out W(string ascii) { _b.AddRange(Encoding.ASCII.GetBytes(ascii)); return this; }
            public Out Tab() { _b.Add(9); return this; }
            public Out Nl() { _b.Add(10); Lines++; return this; }
            public byte[] Bytes => _b.ToArray();
        }

        private static byte[] Join(List<byte[]> parts, byte sep)
        {
            var o = new List<byte>();
            for (int i = 0; i < parts.Count; i++) { if (i > 0) o.Add(sep); o.AddRange(parts[i]); }
            return o.ToArray();
        }

        private static LuaVM RunFile(byte[] data)
        {
            var vm = new LuaVM();
            if (data.Length >= 4 && data[0] == 0x1B && data[1] == (byte)'L' && data[2] == (byte)'u' && data[3] == (byte)'a') vm.Run(data);
            else LuaSource.Run(data, vm.Globals);
            return vm;
        }

        /// <summary>dumpitems.lua.</summary>
        public static byte[] Items(byte[] lub)
        {
            var t = (LuaTable)RunFile(lub).Globals.Get("tbl");
            var o = new Out();
            foreach (var k in SortedKeys(t))
            {
                var v = t.Get(k);
                var desc = new List<byte[]>();
                foreach (var d in IpairsList(Or(Field(v, "identifiedDescriptionName"), new LuaTable()))) desc.Add(Hexs(d));
                o.W(WriteArg(k)).Tab().W(Hexs(Or(Field(v, "identifiedDisplayName"), Empty))).Tab().W(Hexs(Or(Field(v, "identifiedResourceName"), Empty))).Tab()
                 .W(ToString(Or(Field(v, "slotCount"), 0L))).Tab().W(ToString(Or(Field(v, "ClassNum"), 0L))).Tab()
                 .W(Hexs(Or(Field(v, "unidentifiedDisplayName"), Empty))).Tab().W(Hexs(Or(Field(v, "unidentifiedResourceName"), Empty))).Tab()
                 .W(Join(desc, (byte)',')).Nl();
            }
            return o.Bytes;
        }

        /// <summary>dumpquests.lua (hex(s) = hex of tostring(s)).</summary>
        public static byte[] Quests(byte[] lub)
        {
            var q = (LuaTable)RunFile(lub).Globals.Get("QuestInfoList");
            var o = new Out();
            foreach (var id in SortedKeys(q))
            {
                var v = q.Get(id);
                var d = new List<byte[]>();
                foreach (var s in IpairsList(Or(Field(v, "Description"), new LuaTable()))) d.Add(HexStr(s));
                o.W(WriteArg(id)).Tab().W(HexStr(Or(Field(v, "Title"), Empty))).Tab().W(HexStr(Or(Field(v, "Summary"), Empty))).Tab()
                 .W(Join(d, (byte)',')).Tab().W(HexStr(Or(Field(v, "NpcSpr"), Empty))).Tab().W(HexStr(Or(Field(v, "NpcNavi"), Empty))).Tab()
                 .W(ToString(Or(Field(v, "NpcPosX"), Empty))).Tab().W(ToString(Or(Field(v, "NpcPosY"), Empty))).Tab()
                 .W(HexStr(Or(Field(v, "IconName"), Empty))).Nl();
            }
            return o.Bytes;
        }

        /// <summary>dump_achievements.lua (hex(s) = hex of tostring(s or '')).</summary>
        public static byte[] Achievements(byte[] lub)
        {
            var A = (LuaTable)RunFile(lub).Globals.Get("achievement_tbl");
            byte[] Hx(object s) => HexStr(Or(s, Empty));
            var o = new Out();
            foreach (var aid in SortedKeys(A))
            {
                var a = A.Get(aid);
                var c = Or(Field(a, "content"), new LuaTable());
                var r = Or(Field(a, "reward"), new LuaTable());
                var cols = new List<object>
                {
                    aid, Or(Field(a, "UI_Type"), 0L), new LuaStr(Hx(Field(a, "group"))), Or(Field(a, "major"), 0L), Or(Field(a, "minor"), 0L),
                    Or(Field(a, "score"), 0L), new LuaStr(Hx(Field(a, "title"))), new LuaStr(Hx(Field(c, "summary"))), new LuaStr(Hx(Field(c, "details"))),
                    Or(Field(r, "item"), 0L), Or(Field(r, "title"), 0L), Or(Field(r, "buff"), 0L),
                };
                var res = Or(Field(a, "resource"), new LuaTable());
                foreach (var k in SortedKeys(res))
                {
                    var x = ((LuaTable)res).Get(k);
                    var cell = new List<byte>(Hx(Field(x, "text")));
                    cell.Add((byte)'|'); cell.AddRange(ToString(Or(Field(x, "count"), 0L)));
                    cell.Add((byte)'|'); cell.AddRange(ToString(Or(Field(x, "shortcut"), 0L)));
                    cols.Add(new LuaStr(cell.ToArray()));
                }
                o.W(Join(cols.Select(ToString).ToList(), 9)).Nl();
            }
            return o.Bytes;
        }

        /// <summary>dump_titles.lua.</summary>
        public static byte[] Titles(byte[] lub)
        {
            var t = (LuaTable)RunFile(lub).Globals.Get("title_tbl");
            var o = new Out();
            foreach (var i in SortedKeys(t)) o.W(WriteArg(i)).Tab().W(HexStr(Or(t.Get(i), Empty))).Nl();
            return o.Bytes;
        }

        /// <summary>dump_emotions.lua: the rows in call order (InsertEmotionListTable as "a | b | c", every other undefined global
        /// called as "CALL name(args)"; its __index hands a recorder for ANY undefined global). The TABLE / CONST lines it prints
        /// afterwards are matched by neither of build_qol_tables.py's patterns and are left out (lubdump.py emotions).</summary>
        public static byte[] Emotions(byte[] lub)
        {
            var vm = new LuaVM();
            var rows = new List<byte[]>();
            vm.Globals.Set(LuaStr.Of("c_GetMsgString"), new LuaNative("c_GetMsgString", a =>
            {
                var b = new List<byte>(Encoding.ASCII.GetBytes("MSG")); b.AddRange(ToString(a.Length > 0 ? a[0] : null));
                return new object[] { new LuaStr(b.ToArray()) };
            }));
            vm.Globals.Set(LuaStr.Of("InsertEmotionListTable"), new LuaNative("InsertEmotionListTable", a =>
            {
                rows.Add(JoinSep(a.Select(ToString).ToList(), " | "));
                return Lua.None;
            }));
            var meta = new LuaTable();
            meta.Set(LuaStr.Of("__index"), new LuaNative("__index", a =>
            {
                var k = a.Length > 1 ? a[1] : null;
                if (k is LuaStr ks && LuaStdlib.Contains(ks.Latin1))
                    throw new ToolError("emotionlist.lub uses '" + ks.Latin1 + "', which lub51.py does not provide");
                var rec = new LuaNative("recorder", args =>
                {
                    var line = new List<byte>(Encoding.ASCII.GetBytes("CALL "));
                    line.AddRange(ToString(k)); line.Add((byte)'(');
                    line.AddRange(JoinSep(args.Select(ToString).ToList(), ", ")); line.Add((byte)')');
                    rows.Add(line.ToArray());
                    return Lua.None;
                });
                return new object[] { rec };
            }));
            vm.Globals.Meta = meta;
            vm.Run(lub);
            var o = new Out();
            foreach (var r in rows) o.W(r).Nl();
            return o.Bytes;
        }

        private static byte[] JoinSep(List<byte[]> parts, string sep)
        {
            var o = new List<byte>(); var s = Encoding.ASCII.GetBytes(sep);
            for (int i = 0; i < parts.Count; i++) { if (i > 0) o.AddRange(s); o.AddRange(parts[i]); }
            return o.ToArray();
        }

        /// <summary>dump_stateicons.lua (efstids.lub + stateiconimginfo.lub): efst, name, priority, file per StateIconImgList
        /// entry; stops where Lua's pairs() order could show (two names for one id, one efst under two priorities).</summary>
        public static byte[] StateIcons(IToolInputs inp)
        {
            var vm = new LuaVM();
            foreach (var f in new[] { "stateicon\\efstids.lub", "stateicon\\stateiconimginfo.lub" }) vm.Run(T.Need(inp, LuaDir + f));
            var byid = new Dictionary<object, object>(PyKeyComparer.I);
            foreach (var kv in T.Table(vm.Globals.Get("EFST_IDs"), "EFST_IDs").Items())
            {
                var id = Lua.Norm(kv.Value);
                if (id != null && byid.ContainsKey(id)) throw new ToolError("EFST_IDs gives two names the id " + Lua.Repr(kv.Value) + ": Lua pairs() order would pick one");
                if (id == null) throw new ToolError("EFST_IDs has a nil id");
                byid[id] = kv.Key;
            }
            var o = new Out();
            var seen = new HashSet<object>(PyKeyComparer.I);
            foreach (var pri in T.Table(vm.Globals.Get("StateIconImgList"), "StateIconImgList").Items())
                foreach (var e in T.Table(pri.Value, "StateIconImgList[pri]").Items())
                {
                    if (!seen.Add(e.Key)) throw new ToolError("efst " + Lua.Repr(e.Key) + " under two priorities: Lua pairs() order would pick the line");
                    byid.TryGetValue(Lua.Norm(e.Key), out object name);
                    o.W(ToString(e.Key)).Tab().W(ToString(name ?? LuaStr.Of("?"))).Tab().W(ToString(pri.Key)).Tab().W(ToString(e.Value)).Nl();
                }
            return o.Bytes;
        }

        /// <summary>../appearance/dump_lubs.lua (accessoryid.lub, weapontable.lub, tb_layer_priority.lub).</summary>
        public static byte[] Lubs(IToolInputs inp)
        {
            var vm = new LuaVM();
            foreach (var f in new[] { "datainfo\\accessoryid.lub", "datainfo\\weapontable.lub", "datainfo\\tb_layer_priority.lub" }) vm.Run(T.Need(inp, LuaDir + f));
            var G = vm.Globals;
            var lines = new List<byte[]>();
            byte[] Row(params byte[][] cols) => Join(cols.ToList(), 9);
            var E = G.Get("Expansion_Weapon_IDs");
            foreach (var v in SortedKeys(E)) lines.Add(Row(Encoding.ASCII.GetBytes("wexp"), ToString(v), ToString(((LuaTable)E).Get(v))));
            var Tb = G.Get("TB_Layer_Priority");
            foreach (var n in new[] { "Mid", "Top", "Bottom", "Robe" }) lines.Add(Row(Encoding.ASCII.GetBytes("layerdef"), Encoding.ASCII.GetBytes(n), ToString(Field(Tb, "Default_" + n))));
            var items = Field(Tb, "Items_List");
            foreach (var i in SortedKeys(items))
            {
                var e = ((LuaTable)items).Get(i);
                var dirs = new List<byte[]>();
                var direction = Field(e, "Direction");
                if (Lua.Truthy(direction))
                    foreach (var d in SortedKeys(direction))
                    {
                        var cell = new List<byte>(Concat(d)); cell.Add((byte)'='); cell.AddRange(Concat(((LuaTable)direction).Get(d)));
                        dirs.Add(cell.ToArray());
                    }
                var def = Field(e, "Default");
                lines.Add(Row(Encoding.ASCII.GetBytes("layer"), ToString(i), def != null ? ToString(def) : Encoding.ASCII.GetBytes("-"), Join(dirs, (byte)',')));
            }
            var o = new Out();
            foreach (var l in lines) o.W(l).Nl();
            return o.Bytes;
        }

        /// <summary>../appearance/dump_classnum.lua (a .lub, or plain Lua source through <see cref="LuaSource"/>).</summary>
        public static byte[] Classnum(byte[] file)
        {
            var t = (LuaTable)RunFile(file).Globals.Get("tbl");
            var o = new Out();
            foreach (var k in SortedKeys(t)) o.W(WriteArg(k)).Tab().W(ToString(Or(Field(t.Get(k), "ClassNum"), 0L))).Nl();
            return o.Bytes;
        }
    }
}
