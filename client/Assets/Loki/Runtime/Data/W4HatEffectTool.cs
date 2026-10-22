using System.Collections.Generic;
using System.IO;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 4 (w4units): client/tools/clientdata/build_hateffects.py ported step for step — hateffects.tsv (Sprites/HatEffects.cs)
    /// from the GRF's data\luafiles514\lua files\hateffectinfo\hateffectids.lub then hateffectinfo.lub (roBrowserLegacy
    /// DB/DBManager.js:5991-6121 reads the same two in the same order), one row per hatEffectTable entry sorted by id:
    /// id, resourceFileName, hatEffectID, hatEffectPos, hatEffectPosX, isAttachedHead, isRenderBeforeCharacter, isIgnoreRiding —
    /// strings as their bytes (the file is Latin-1 text of the GRF's bytes), numbers as Lua's tostring ("%.14g"), a missing string /
    /// id empty, a missing position 0, flags 1 / 0 by truthiness.
    /// </summary>
    public static class W4HatEffectTool
    {
        private const string Base = "data\\luafiles514\\lua files\\hateffectinfo\\";

        public static ToolOutput HatEffects(IToolInputs inp)
        {
            var vm = new LuaVM(); var G = vm.Globals;
            foreach (var f in new[] { Base + "hateffectids.lub", Base + "hateffectinfo.lub" }) vm.Run(T.Need(inp, f));
            var tbl = T.Table(G.Get("hatEffectTable"), "hatEffectTable");
            var entries = new List<(object k, LuaTable v)>();
            foreach (var kv in tbl.Items())
            {
                if (!(kv.Key is long || kv.Key is double)) throw new ToolError("hatEffectTable key is not a number: " + Lua.Repr(kv.Key));
                if (!(kv.Value is LuaTable lt)) throw new ToolError("hatEffectTable[" + Lua.Repr(kv.Key) + "] is not a table");
                entries.Add((kv.Key, lt));
            }
            if (new HashSet<object>(entries.ConvertAll(e => e.k), PyKeyComparer.I).Count != entries.Count) throw new ToolError("two entries share an id");
            entries = T.Sorted(entries, (a, b) => T.CompareNum(a.k, b.k));
            var outb = new MemoryStream(); int strs = 0, ids = 0;
            foreach (var (k, v) in entries)
            {
                object res = v.Get("resourceFileName"), hid = v.Get("hatEffectID"), pos = v.Get("hatEffectPos"), posx = v.Get("hatEffectPosX");
                var cols = new List<byte[]>
                {
                    Num(k),
                    res is LuaStr rs ? rs.B : new byte[0],
                    hid != null ? Num(hid) : new byte[0],
                    pos != null ? Num(pos) : new[] { (byte)'0' },
                    posx != null ? Num(posx) : new[] { (byte)'0' },
                    Flag(v.Get("isAttachedHead")), Flag(v.Get("isRenderBeforeCharacter")), Flag(v.Get("isIgnoreRiding")),
                };
                foreach (var c in cols) foreach (byte x in c) if (x == 9 || x == 10 || x == 13) throw new ToolError("a field holds a tab or a line break");
                if (cols[1].Length > 0) strs++;
                if (cols[2].Length > 0) ids++;
                for (int i = 0; i < cols.Count; i++) { if (i > 0) outb.WriteByte(9); outb.Write(cols[i], 0, cols[i].Length); }
                outb.WriteByte(10);
            }
            var o = new ToolOutput();
            o.Add("hateffects.tsv", outb.ToArray());
            o.Summary = $"hateffects.tsv: {entries.Count} hat effects ({strs} STR files, {ids} effect-table ids)";
            return o;
        }

        /// <summary>'%.14g' % float(v) (luaconf.h LUA_NUMBER_FMT).</summary>
        private static byte[] Num(object v)
        {
            if (v is long l) return System.Text.Encoding.ASCII.GetBytes(Py.G14(l));
            if (v is double d) return System.Text.Encoding.ASCII.GetBytes(Py.G14(d));
            throw new ToolError("not a number: " + Lua.Repr(v));
        }

        private static byte[] Flag(object v) => new[] { Lua.Truthy(v) ? (byte)'1' : (byte)'0' };
    }
}
