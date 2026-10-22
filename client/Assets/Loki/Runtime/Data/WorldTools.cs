using System;
using System.Collections.Generic;
using System.Linq;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 4 (w4world): client/tools/clientdata/build_world_tables.py ported step for step (the Python lines it follows are
    /// quoted in the comments): mapsky.tsv (the client's per-map sky, mapskydata.lub) and navigation.tsv (the client's navigation
    /// data navi_*_br.lub and the install's town guide Towninfo.lub). Run by <see cref="ClientDataBuilder"/> ("World tables").
    /// </summary>
    public static class WorldTools
    {
        private const string Lua_ = LubDump.LuaDir;
        private const string Sky = Lua_ + "mapskydata\\mapskydata.lub";
        private const string Navi = Lua_ + "navigation\\";
        private static readonly string[] CloudFields = { "Num", "CullDist", "Color", "Size", "Size_Extra", "Expand_Rate", "Alpha_Inc_Time", "Alpha_Inc_Time_Extra",
                                                         "Alpha_Inc_Speed", "Alpha_Dec_Time", "Alpha_Dec_Time_Extra", "Alpha_Dec_Speed", "Height", "Height_Extra" };
        public const string TownPt = "System\\Towninfo.lub", TownEn = "System\\english\\Towninfo.lub";

        /// <summary>dec(b): '' for None, str() of a number, else UTF-8 → windows-1252 → Latin-1.</summary>
        private static string Dec(object b)
        {
            if (b == null) return "";
            if (b is bool bb) return bb ? "True" : "False";
            if (b is long || b is double) return T.NumStr(b);
            return T.Dec(b);
        }

        /// <summary>clean(s): tabs / newlines to spaces, "\r" removed, str.strip().</summary>
        private static string Clean(string s) => Py.Strip(Py.Clean(s)).Replace("\u200b", ""); // wave 28 (wtextc): zero-width spaces out

        public const string MapInfoEn = "System\\english\\mapinfo.lub";

        /// <summary>Wave 28 (wtextc): the install's English map names, System\english\mapinfo.lub mapTbl["map.rsw"].displayName (1093 on the
        /// LATAM install of 2026-10-15; the M rows' names are Portuguese, navi_map_br.lub). Empty when the install has no such file;
        /// a file that does not run gives none either, and the summary says why (the navigation table is still made).</summary>
        private static Dictionary<string, string> EnglishMapNames(IToolInputs inp, out string how)
        {
            var o = new Dictionary<string, string>(StringComparer.Ordinal);
            var b = inp.RoFile(MapInfoEn);
            if (b == null) { how = "none (no " + MapInfoEn + ")"; return o; }
            try
            {
                var vm = new LuaVM(); vm.Run(b);
                if (vm.Globals.Get("mapTbl") is LuaTable t)
                    foreach (var kv in t.Items())
                        if (kv.Key is LuaStr && kv.Value is LuaTable e) { string n = Name(e.Get("displayName")); if (n.Length > 0) o[MapKey(kv.Key)] = n; }
                how = o.Count + " English map names";
            }
            catch (Exception e) { o.Clear(); how = MapInfoEn + " unreadable (" + e.GetType().Name + ": " + e.Message + ")"; }
            return o;
        }

        /// <summary>name(b): '' for a non-string and for a text id (0x1C …), else clean(dec(b)).</summary>
        private static string Name(object b)
        {
            if (!(b is LuaStr s) || (s.B.Length > 0 && s.B[0] == 0x1c)) return "";
            return Clean(Dec(b));
        }

        /// <summary>num(v): '-' for a bool or a non-number, else dec(v).</summary>
        private static string Num(object v) => v is bool || !(v is long || v is double) ? "-" : Dec(v);

        /// <summary>flag(v): '1' True, '0' False, '-' otherwise.</summary>
        private static string Flag(object v) => v is bool b ? (b ? "1" : "0") : "-";

        /// <summary>mapkey(b): dec(b).lower() without ".rsw" / ".gat".</summary>
        private static string MapKey(object b)
        {
            string m = Dec(b).ToLowerInvariant();
            foreach (var ext in new[] { ".rsw", ".gat" }) if (m.EndsWith(ext, StringComparison.Ordinal)) m = m.Substring(0, m.Length - ext.Length);
            return m;
        }

        private static bool IsBytes(object v, string ascii) => v is LuaStr s && s.Equals(LuaStr.Of(ascii));

        // ================================================================== mapsky(g)
        public static ToolOutput MapSky(IToolInputs inp)
        {
            var vm = new LuaVM();
            vm.Run(T.Need(inp, Sky));
            if (!(vm.Globals.Get("MapSkyData") is LuaTable t)) throw new ToolError("mapskydata.lub: no MapSkyData table");
            var rows = new List<string>(); int nclouds = 0;
            // entries = sorted(((mapkey(k), v) for k, v in t.h.items() if isinstance(k, bytes) and isinstance(v, LuaTable)), key=kv[0])
            var entries = T.Sorted(t.Items().Where(kv => kv.Key is LuaStr && kv.Value is LuaTable).Select(kv => (m: MapKey(kv.Key), e: (LuaTable)kv.Value)), (a, b) => Py.CompareStr(a.m, b.m));
            foreach (var (m, e) in entries)
            {
                var bg = e.Get("BG_Color");
                string bgs = bg is LuaTable ? string.Join(",", T.Seq(bg).Select(Num)) : "-";
                var old = e.Get("Old_Cloud_Effect");
                var oldSeq = T.Seq(old);
                string olds = old is LuaTable && oldSeq.Count > 0 ? Num(oldSeq[0]) : "-";
                rows.Add(string.Join("\t", "S", m, bgs, Flag(e.Get("Star_Effect")), Flag(e.Get("BG_Fog")), olds));
                var clouds = T.Seq(e.Get("Cloud_Effect"));
                for (int i = 0; i < clouds.Count; i++)
                {
                    if (!(clouds[i] is LuaTable c)) continue;
                    var cols = new List<string> { "C", m, (i + 1).ToString() };
                    foreach (var f in CloudFields)
                    {
                        var v = c.Get(f);
                        if (f == "Color")
                        {
                            var col = v is LuaTable ? T.Seq(v) : new List<object>();
                            for (int k = 0; k < 3; k++) cols.Add(k < col.Count ? Num(col[k]) : "-");
                        }
                        else cols.Add(Num(v));
                    }
                    rows.Add(string.Join("\t", cols)); nclouds++;
                }
            }
            var o = new ToolOutput();
            o.AddUtf8("mapsky.tsv", string.Join("\n", new[] { "# mapsky.tsv: built by client/tools/clientdata/build_world_tables.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) + "): " + Sky + "; see that file for the rows" }.Concat(rows)) + "\n");
            o.Summary = $"mapsky.tsv: {entries.Count} maps, {nclouds} cloud layers";
            return o;
        }

        // ================================================================== navigation(g, root)
        private static bool Real(List<object> r) => r.Count > 0 && r[0] is LuaStr && !IsBytes(r[0], "NULL") && !IsBytes(r[0], "") && !IsBytes(r[0], "0");

        public static ToolOutput Navigation(IToolInputs inp)
        {
            var vm = new LuaVM(); var G = vm.Globals;
            foreach (var f in new[] { "navi_map_br.lub", "navi_link_br.lub", "navi_linkdistance_br.lub", "navi_npc_br.lub", "navi_npcdistance_br.lub", "navi_mob_br.lub" })
                vm.Run(T.Need(inp, Navi + f));
            var rows = new List<string>(); var counts = new Dictionary<string, int>();
            void Count(string k, int by = 1) { counts.TryGetValue(k, out int n); counts[k] = n + by; }

            var enMaps = EnglishMapNames(inp, out string enHow); // wave 28 (wtextc)
            foreach (var x in T.Seq(G.Get("Navi_Map")))
            {
                var r = T.Seq(x);
                if (r.Count < 5 || !Real(r)) continue;
                string mk = MapKey(r[0]);
                // wave 28 (wtextc): a seventh column, the install's English name ("" when it has none)
                rows.Add(string.Join("\t", "M", mk, Name(r[1]), Num(r[2]), Num(r[3]), Num(r[4]), enMaps.TryGetValue(mk, out var en) ? en : "")); Count("M");
            }
            foreach (var x in T.Seq(G.Get("Navi_Link")))
            {
                var r = T.Seq(x);
                if (r.Count < 11 || !Real(r)) continue;
                rows.Add(string.Join("\t", "L", Num(r[1]), MapKey(r[0]), Num(r[6]), Num(r[7]), Num(r[2]), Num(r[3]), MapKey(r[8]), Num(r[9]), Num(r[10]), Name(r[4]), Name(r[5]))); Count("L");
            }
            void Flat(object table, string key)
            {
                var s = T.Seq(table); int n = 0;
                for (int i = 0; i < s.Count - 2; i += 3)
                {
                    foreach (var rowObj in T.Seq(s[i + 2]))
                    {
                        var row = T.Seq(rowObj);
                        // if not row or isinstance(row[0], bool) or not isinstance(row[0], (int, float)) or row[0] == 0: continue
                        if (row.Count == 0 || row[0] is bool || !(row[0] is long || row[0] is double) || ZeroNum(row[0])) continue;
                        var pairs = new List<string>();
                        for (int k = 1; k < row.Count; k++)
                        {
                            var sub = T.Seq(row[k]);
                            // if len(sub) < 3 or sub[0] in (b'NULL', b'') or sub[1] == 0: continue
                            if (sub.Count < 3 || IsBytes(sub[0], "NULL") || IsBytes(sub[0], "") || (sub[1] is bool sb && !sb) || ZeroNum(sub[1])) continue; // False == 0 in Python too
                            pairs.Add(Num(sub[1]) + ":" + Num(sub[2]));
                        }
                        rows.Add(string.Join("\t", key, Num(row[0]), string.Join(",", pairs))); n++;
                    }
                }
                counts[key] = n;
            }
            Flat(G.Get("Navi_Distance"), "E");
            foreach (var x in T.Seq(G.Get("Navi_Npc")))
            {
                var r = T.Seq(x);
                if (r.Count < 8 || !Real(r)) continue;
                rows.Add(string.Join("\t", "N", Num(r[1]), MapKey(r[0]), Num(r[6]), Num(r[7]), Num(r[2]), Num(r[3]), Name(r[4]), Name(r[5]))); Count("N");
            }
            Flat(G.Get("Navi_NpcDistance"), "D");
            foreach (var x in T.Seq(G.Get("Navi_Mob")))
            {
                var r = T.Seq(x);
                if (r.Count < 8 || !Real(r)) continue;
                rows.Add(string.Join("\t", "B", MapKey(r[0]), Num(r[1]), Num(r[2]), Num(r[3]), Name(r[4]), Name(r[5]), Num(r[6]), Num(r[7]))); Count("B");
            }
            // the install's town guide: Portuguese (System\Towninfo.lub) and English (System\english\Towninfo.lub)
            var towns = new Dictionary<string, Dictionary<string, List<TownRow>>>(StringComparer.Ordinal);
            foreach (var (lang, rel) in new[] { ("pt", TownPt), ("en", TownEn) })
            {
                var data = inp.RoFile(rel) ?? throw new ToolError("missing in the install: " + rel);
                var tv = new LuaVM();
                tv.Run(data);
                if (!(tv.Globals.Get("mapNPCInfoTable") is LuaTable t)) throw new ToolError(rel + ": no mapNPCInfoTable");
                foreach (var kv in t.Items())
                {
                    if (!(kv.Key is LuaStr)) continue;
                    var lst = new List<TownRow>();
                    foreach (var e in T.Seq(kv.Value))
                        if (e is LuaTable et) lst.Add(new TownRow { ty = Num(et.Get("TYPE")), x = Num(et.Get("X")), y = Num(et.Get("Y")), nm = Name(et.Get("name")) });
                    string mk = MapKey(kv.Key);
                    if (!towns.TryGetValue(mk, out var byLang)) towns[mk] = byLang = new Dictionary<string, List<TownRow>>();
                    byLang[lang] = lst;
                }
            }
            int nt = 0;
            foreach (var m in T.Sorted(towns.Keys, Py.CompareStr))
            {
                var pt = towns[m].TryGetValue("pt", out var p1) ? p1 : new List<TownRow>();
                var en = towns[m].TryGetValue("en", out var e1) ? e1 : new List<TownRow>();
                var used = new bool[en.Count];
                foreach (var r0 in pt)
                {
                    string match = "";
                    for (int j = 0; j < en.Count; j++)
                        if (!used[j] && en[j].ty == r0.ty && en[j].x == r0.x && en[j].y == r0.y) { used[j] = true; match = en[j].nm; break; }
                    rows.Add(string.Join("\t", "T", m, r0.ty, r0.x, r0.y, match, r0.nm)); nt++;
                }
                for (int j = 0; j < en.Count; j++)
                    if (!used[j]) { rows.Add(string.Join("\t", "T", m, en[j].ty, en[j].x, en[j].y, en[j].nm, "")); nt++; }
            }
            counts["T"] = nt;
            var o = new ToolOutput();
            o.AddUtf8("navigation.tsv", string.Join("\n", new[] { "# navigation.tsv: built by client/tools/clientdata/build_world_tables.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) +
                ") navigation\\navi_*_br.lub and System\\Towninfo.lub + System\\english\\Towninfo.lub; see that file for the rows" }.Concat(rows)) + "\n");
            o.Summary = "navigation.tsv: " + string.Join(", ", "MLENDBT".Select(ch => ch.ToString()).Where(counts.ContainsKey).Select(k => k + " " + counts[k])) + "; map names in English: " + enHow;
            return o;
        }

        private sealed class TownRow { public string ty, x, y, nm; }

        private static bool ZeroNum(object v) => v is long l ? l == 0 : v is double d && d == 0.0;

        /// <summary>main(): both tables (mapsky.tsv first, as the tool writes them), with the tool's summary line.</summary>
        public static ToolOutput Tables(IToolInputs inp)
        {
            var sky = MapSky(inp); var nav = Navigation(inp);
            var o = new ToolOutput();
            foreach (var f in sky.Files) o.Add(f.Key, f.Value);
            foreach (var f in nav.Files) o.Add(f.Key, f.Value);
            o.Summary = sky.Summary.Replace("mapsky.tsv: ", "mapsky.tsv: ") + "; " + nav.Summary;
            return o;
        }
    }
}
