using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): the client-data tools that read only the GRF, ported step for step (the Python lines they follow
    /// are quoted in the comments): build_skilltree.py, build_equip_tables.py, build_worldmap.py, build_texttables.py,
    /// build_spritenames.py and build_efst.py (all in client/tools/clientdata).
    /// </summary>
    public static class GrfTools
    {
        private const string Lua_ = LubDump.LuaDir;

        // ================================================================== build_skilltree.py
        private static readonly string[] SkillFiles =
        {
            Lua_ + "skillinfoz\\skillid.lub", Lua_ + "skillinfoz\\jobinheritlist.lub", Lua_ + "skillinfoz\\skillinfo_f.lub",
            Lua_ + "skillinfoz\\skilltreeview.lub", Lua_ + "skillinfoz\\skillinfolist_enus.lub", Lua_ + "skillinfoz\\skillinfolist_data.lub",
            Lua_ + "datainfo\\pcjobnamegender_enus.lub",
        };
        private const string Descript = "data\\english\\luafiles514\\lua files\\skillinfoz\\skilldescript.lub";
        // client wave 5 (w5ui): FILES_PT = FILES with the Portuguese string tables (skillinfolist_ptbr.lub, pcjobnamegender_ptbr.lub);
        // DESCRIPT_PT = LUA + 'skillinfoz\\skilldescript.lub' (the Portuguese descriptions; the English ones are under data\english\)
        private static readonly string[] SkillFilesPt = SkillFiles.Select(f => f.Replace("skillinfolist_enus.lub", "skillinfolist_ptbr.lub").Replace("pcjobnamegender_enus.lub", "pcjobnamegender_ptbr.lub")).ToArray();
        private const string DescriptPt = Lua_ + "skillinfoz\\skilldescript.lub";

        /// <summary>need_list(t): '%d:%d' % (e[1], e[2] if int else 0) for { {skill, level}, ... }.</summary>
        private static string NeedList(object t)
        {
            var o = new List<string>();
            foreach (var e in T.Seq(t))
                if (e is LuaTable et && T.IsInt(et.Get(1L)))
                {
                    var lv = et.Get(2L);
                    o.Add(T.IntVal(et.Get(1L)) + ":" + (T.IsInt(lv) ? T.IntVal(lv) : 0));
                }
            return string.Join(",", o);
        }

        /// <summary>nums(t): ','.join(str(v) for v in seq(t) if isinstance(v, (int, float))).</summary>
        private static string Nums(object t) => string.Join(",", T.Seq(t).Where(T.IsNum).Select(T.NumStr));

        public static ToolOutput SkillTree(IToolInputs inp)
        {
            var vm = new LuaVM();
            foreach (var f in SkillFiles) vm.Run(T.Need(inp, f));
            var G = vm.Globals;
            var SKID = T.Table(G.Get("SKID"), "SKID"); var JOBID = T.Table(G.Get("JOBID"), "JOBID");
            var inherit = G.Get("JOB_INHERIT_LIST"); var inherit2 = G.Get("JOB_INHERIT_LIST2");
            var tree = T.Table(G.Get("SKILL_TREEVIEW_FOR_JOB"), "SKILL_TREEVIEW_FOR_JOB"); var info = T.Table(G.Get("SKILL_INFO_LIST"), "SKILL_INFO_LIST");
            var tabs = new Dictionary<object, List<string>>(PyKeyComparer.I);
            foreach (var e in T.Seq(G.Get("JobSkillTab")))
                if (e is LuaTable et && T.IsInt(et.Get("job")))
                {
                    var names = new List<string>();
                    foreach (var k in new[] { "TabName1st", "TabName2nd", "TabName3rd", "TabName4th" }) if (et.Get(k) != null) names.Add(T.Dec(et.Get(k)));
                    tabs[et.Get("job")] = names;
                }
            var jobNames = G.Get("PCJobNameTableMan");
            vm.Run(T.Need(inp, Descript));
            var descr = T.Table(G.Get("SKILL_DESCRIPT"), "SKILL_DESCRIPT");

            // client wave 5 (w5ui): the Portuguese pass in a fresh Lua state (Python: lub51.reset(), then FILES_PT and DESCRIPT_PT)
            var vmPt = new LuaVM();
            foreach (var f in SkillFilesPt) vmPt.Run(T.Need(inp, f));
            var GP = vmPt.Globals;
            var infoPt = GP.Get("SKILL_INFO_LIST");
            var namesM = GP.Get("PCJobNameTableMan"); var namesW = GP.Get("PCJobNameTableWoman");
            vmPt.Run(T.Need(inp, DescriptPt));
            var descrPt = GP.Get("SKILL_DESCRIPT");

            // jobs = sorted((v, k) for k, v in JOBID.h.items() if isinstance(v, int) and isinstance(k, bytes))
            var jobs = T.Sorted(JOBID.Items().Where(kv => T.IsInt(kv.Value) && kv.Key is LuaStr).Select(kv => (v: kv.Value, k: (LuaStr)kv.Key)),
                                (a, b) => { int c = T.CompareNum(a.v, b.v); return c != 0 ? c : a.k.CompareTo(b.k); });
            var rows = new List<string> { "# skilltree.tsv: built by client/tools/clientdata/build_skilltree.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) + "); see that file for the row kinds" };
            int nj = 0, nt = 0, nn = 0, ns = 0;
            var inh = T.Table(inherit, "JOB_INHERIT_LIST"); var inh2 = T.Table(inherit2, "JOB_INHERIT_LIST2");
            foreach (var (jid, jname) in jobs)
            {
                var p = inh.Get(jid); var p2 = inh2.Get(jid);
                var nm = jobNames != null ? T.Table(jobNames, "PCJobNameTableMan").Get(jid) : null;
                var pm = namesM != null ? T.Table(namesM, "PCJobNameTableMan").Get(jid) : null;   // client wave 5 (w5ui)
                var pw = namesW != null ? T.Table(namesW, "PCJobNameTableWoman").Get(jid) : null;
                tabs.TryGetValue(jid, out var tl);
                rows.Add(string.Join("\t", "J", T.IntStr(jid), T.Utf8(jname), T.IsInt(p) ? T.IntStr(p) : "-1", T.IsInt(p2) ? T.IntStr(p2) : "-1",
                                     Py.Clean(T.Dec(nm)), string.Join(",", tl ?? new List<string>()), Py.Clean(T.Dec(pm)), Py.Clean(T.Dec(pw))));
                nj++;
            }
            foreach (var jid in T.IntKeys(tree))
            {
                var t = T.Table(tree.Get(jid), "SKILL_TREEVIEW_FOR_JOB[job]");
                foreach (var pos in T.IntKeys(t))
                {
                    var sk = t.Get(pos);
                    if (T.IsInt(sk)) { rows.Add(string.Join("\t", "T", T.IntStr(jid), T.IntStr(pos), T.IntStr(sk))); nt++; }
                }
            }
            foreach (var sid in T.IntKeys(info))
            {
                if (!(info.Get(sid) is LuaTable e)) continue;
                var gen = e.Get("_NeedSkillList");
                if (gen is LuaTable) { rows.Add(string.Join("\t", "N", T.IntStr(sid), "-1", NeedList(gen))); nn++; }
                if (e.Get("NeedSkillList") is LuaTable per)
                    foreach (var jid in T.IntKeys(per)) { rows.Add(string.Join("\t", "N", T.IntStr(sid), T.IntStr(jid), NeedList(per.Get(jid)))); nn++; }
                var typ = e.Get("Type"); var sep = e.Get("bSeperateLv");
                rows.Add(string.Join("\t", "S", T.IntStr(sid), typ is LuaStr ? Py.Clean(T.Dec(typ)) : "", sep is bool sb && sb ? "1" : "0", Nums(e.Get("AttackRange")), Nums(e.Get("ApAmount"))));
                ns++;
            }
            var o = new ToolOutput();
            o.AddUtf8("skilltree.tsv", string.Join("\n", rows) + "\n");

            // skills.tsv: every SKID (id, aegis, name, max level, SP, description), then (client wave 5, w5ui) the Portuguese name and description
            var ids = T.Sorted(SKID.Items().Where(kv => T.IsInt(kv.Value) && kv.Key is LuaStr).Select(kv => (v: kv.Value, k: (LuaStr)kv.Key)),
                               (a, b) => { int c = T.CompareNum(a.v, b.v); return c != 0 ? c : a.k.CompareTo(b.k); });
            var sbk = new StringBuilder(); int n = 0, withDesc = 0, npn = 0, npd = 0;
            foreach (var (sid, aegis) in ids)
            {
                var e = info.Get(sid); var d = descr.Get(sid);
                if (d != null) withDesc++;
                var lines = T.Seq(d).Where(x => x is LuaStr).Select(x => Py.Clean(T.Dec(x))).ToList();
                var nm = e is LuaTable et ? et.Get("SkillName") : null;
                string name = nm is LuaStr ns2 && ns2.B.Length > 0 ? Py.Clean(T.Dec(nm)) : (lines.Count > 0 ? lines[0] : "");
                object maxlv = e is LuaTable et2 ? et2.Get("MaxLv") : 0L;
                string sp = e is LuaTable et3 ? Nums(et3.Get("SpAmount")) : "";
                // client wave 5 (w5ui): ep = info_pt.get(sid) if info_pt is not None else None; dp likewise; pname as name above
                var ep = infoPt != null ? T.Table(infoPt, "SKILL_INFO_LIST").Get(sid) : null;
                var dp = descrPt != null ? T.Table(descrPt, "SKILL_DESCRIPT").Get(sid) : null;
                var plines = T.Seq(dp).Where(x => x is LuaStr).Select(x => Py.Clean(T.Dec(x))).ToList();
                var pn = ep is LuaTable ept ? ept.Get("SkillName") : null;
                string pname = pn is LuaStr pns && pns.B.Length > 0 ? Py.Clean(T.Dec(pn)) : (plines.Count > 0 ? plines[0] : "");
                if (pname.Length > 0) npn++;
                if (plines.Count > 0) npd++;
                // wave 28 (wtextc): zero-width spaces (U+200B) out of the texts (build_skilltree.py does the same)
                sbk.Append(string.Join("\t", T.IntStr(sid), T.Utf8(aegis), name.Replace("\u200b", ""), T.IsInt(maxlv) ? T.IntStr(maxlv) : "0", sp, string.Join("\\n", lines).Replace("\u200b", ""),
                                       pname.Replace("\u200b", ""), string.Join("\\n", plines).Replace("\u200b", ""))).Append('\n');
                n++;
            }
            o.AddUtf8("skills.tsv", sbk.ToString());
            o.Summary = $"skilltree.tsv: {nj} jobs, {nt} tree cells, {nn} need lists, {ns} skills; skills.tsv: {n} skills ({withDesc} with a description; Portuguese: {npn} names, {npd} descriptions)";
            return o;
        }

        // ================================================================== build_equip_tables.py
        public static ToolOutput EquipTables(IToolInputs inp)
        {
            const string DI = Lua_ + "datainfo\\";
            var vm = new LuaVM(); var G = vm.Globals;
            vm.Run(T.Need(inp, DI + "enumvar.lub"));
            var ids = new Dictionary<object, string>(PyKeyComparer.I);
            foreach (var kv in T.Table(G.Get("EnumVAR"), "EnumVAR").Items())
            {
                var vid = kv.Value is LuaTable vt ? vt.Get(1L) : kv.Value;
                if (kv.Key is LuaStr name && T.IsInt(vid)) ids[vid] = T.Utf8(name);
            }
            vm.Run(T.Need(inp, DI + "addrandomoptionnametable_enus.lub"));
            var en = new Dictionary<object, object>(PyKeyComparer.I); var enOrder = new List<object>();
            foreach (var kv in T.Table(G.Get("NameTable_VAR"), "NameTable_VAR").Items()) if (T.IsInt(kv.Key)) { if (!en.ContainsKey(kv.Key)) enOrder.Add(kv.Key); en[kv.Key] = kv.Value; }
            vm.Run(T.Need(inp, DI + "addrandomoptionnametable.lub"));
            var pt = new Dictionary<object, object>(PyKeyComparer.I); var ptOrder = new List<object>();
            foreach (var kv in T.Table(G.Get("NameTable_VAR"), "NameTable_VAR").Items()) if (T.IsInt(kv.Key)) { if (!pt.ContainsKey(kv.Key)) ptOrder.Add(kv.Key); pt[kv.Key] = kv.Value; }
            vm.Run(T.Need(inp, DI + "addrandomoption_f.lub"));
            var pct = G.Get("SetPercentValue");
            var rows = new List<string> { "# randomoptions.tsv: built by client/tools/clientdata/build_equip_tables.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) + "): id, EnumVAR name, English format, Portuguese format, SetPercentValue" };
            var all = new HashSet<object>(PyKeyComparer.I); var keys = new List<object>();
            foreach (var k in enOrder.Concat(ptOrder)) if (all.Add(k)) keys.Add(k);
            int n = 0, pctN = 0;
            foreach (var vid in T.Sorted(keys, T.CompareNum))
            {
                string name = ids.TryGetValue(vid, out var nm) ? nm : "";
                if (name == "EnumVAR_LAST") continue;
                object[] p = pct != null ? Lua.Call(pct, new[] { vid }) : new object[] { Lua.False };
                en.TryGetValue(vid, out var ev); pt.TryGetValue(vid, out var pv);
                bool on = p.Length > 0 && p[0] is bool pb && pb;
                rows.Add(string.Join("\t", T.IntStr(vid), name, Py.Clean(T.Dec(ev)), Py.Clean(T.Dec(pv)), on ? "1" : "0"));
                n++; if (on) pctN++;
            }
            var o = new ToolOutput();
            o.AddUtf8("randomoptions.tsv", string.Join("\n", rows) + "\n");
            o.Summary = $"randomoptions.tsv: {n} options ({ids.Count} EnumVAR names, {en.Count} English, {pt.Count} Portuguese, {pctN} percent)";
            return o;
        }

        // ================================================================== build_worldmap.py
        private const string WvBase = Lua_ + "worldviewdata\\";
        private const string WvEnglish = "data\\english\\luafiles514\\lua files\\worldviewdata\\";

        /// <summary>build_worldmap.py dec(): '' for None, str() of a number, else UTF-8 → windows-1252 → Latin-1.</summary>
        private static string WDec(object b)
        {
            if (b == null) return "";
            if (T.IsNum(b)) return T.NumStr(b);
            return T.Dec(b);
        }

        private static string WClean(string s) => Py.Strip(Py.Clean(s)).Replace("\u200b", ""); // wave 28 (wtextc): zero-width spaces out (build_worldmap.py too)

        private static LuaTable WLoad(IToolInputs inp, params string[] files)
        {
            var vm = new LuaVM();
            foreach (var f in files) vm.Run(T.Need(inp, f));
            return vm.Globals;
        }

        private sealed class World { public string Name, Image; public object Table, Dun; public List<List<object>> Rows, Duns; }

        private static List<World> Worlds(LuaTable G)
        {
            var o = new List<World>();
            foreach (var w in T.Seq(G.Get("World_List")))
            {
                var s = T.Seq(w);
                if (s.Count < 4) throw new ToolError("a World_List entry has " + s.Count + " values (Python: ValueError)");
                o.Add(new World
                {
                    Name = WDec(s[0]), Table = s[1], Dun = s[2], Image = WDec(s[3]),
                    Rows = T.Seq(G.Get(s[1])).Select(T.Seq).ToList(), Duns = T.Seq(G.Get(s[2])).Select(T.Seq).ToList(),
                });
            }
            return o;
        }

        private static bool ListEq(List<object> a, int from, int to, List<object> b)
        {
            // a[from:to] != b[from:to] as Python compares two slices
            var x = a.Skip(from).Take(Math.Max(0, Math.Min(to, a.Count) - from)).ToList();
            var y = b.Skip(from).Take(Math.Max(0, Math.Min(to, b.Count) - from)).ToList();
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (!T.PyEq(x[i], y[i])) return false;
            return true;
        }

        private static object At(List<object> l, int i) => i < l.Count ? l[i] : throw new ToolError("a world map row is too short (Python: IndexError)");

        public static ToolOutput WorldMap(IToolInputs inp)
        {
            var en = Worlds(WLoad(inp, WvEnglish + "worldviewdata_language.lub", WvBase + "worldviewdata_table.lub", WvEnglish + "worldviewdata_list.lub"));
            var pt = Worlds(WLoad(inp, WvBase + "worldviewdata_language.lub", WvBase + "worldviewdata_ptbr.lub", WvBase + "worldviewdata_table.lub", WvBase + "worldviewdata_list.lub"));
            var info = WLoad(inp, WvBase + "worldviewdata_info.lub").Get("worldview_info");
            if (en.Count != pt.Count) throw new ToolError($"the English and Portuguese world lists differ: {en.Count} vs {pt.Count}");
            var rows = new List<string> { "# worldmap.tsv: built by client/tools/clientdata/build_worldmap.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) + "); see that file for the rows" };
            int nm = 0, nd = 0;
            for (int wi = 0; wi < en.Count; wi++)
            {
                var e = en[wi]; var p = pt[wi];
                if (!T.PyEq(e.Table, p.Table) || !T.PyEq(e.Dun, p.Dun) || e.Image != p.Image || e.Rows.Count != p.Rows.Count || e.Duns.Count != p.Duns.Count)
                    throw new ToolError("world " + wi + " differs between the languages");
                rows.Add(string.Join("\t", "W", wi.ToString(), e.Image, WClean(e.Name), WClean(p.Name)));
                for (int r = 0; r < e.Rows.Count; r++)
                {
                    var a = e.Rows[r]; var b = p.Rows[r];
                    if (!ListEq(a, 0, 6, b) || !ListEq(a, 7, int.MaxValue, b)) throw new ToolError("row differs between the languages");
                    string m = WDec(At(a, 1)).ToLowerInvariant();
                    if (m.EndsWith(".rsw", StringComparison.Ordinal)) m = m.Substring(0, m.Length - 4);
                    var cols = new List<string> { "M", wi.ToString(), WDec(At(a, 0)), m };
                    for (int k = 2; k < 6 && k < a.Count; k++) cols.Add(WDec(a[k]));
                    cols.Add(WClean(WDec(At(a, 6)))); cols.Add(WClean(WDec(At(b, 6)))); cols.Add(WClean(WDec(At(a, 7))));
                    rows.Add(string.Join("\t", cols)); nm++;
                }
                for (int r = 0; r < e.Duns.Count; r++)
                {
                    var a = e.Duns[r]; var b = p.Duns[r];
                    if (!ListEq(a, 0, 5, b)) throw new ToolError("dungeon marker differs between the languages");
                    var cols = new List<string> { "D", wi.ToString(), WDec(At(a, 0)) };
                    for (int k = 1; k < 5 && k < a.Count; k++) cols.Add(WDec(a[k]));
                    cols.Add(WClean(WDec(At(a, 5)))); cols.Add(WClean(WDec(At(b, 5)))); cols.Add(WClean(WDec(At(a, 6))));
                    rows.Add(string.Join("\t", cols)); nd++;
                }
            }
            if (info is LuaTable it)
                foreach (var k in T.Sorted(it.H.Keys(), (x, y) => Py.CompareStr(WDec(x), WDec(y))))
                {
                    var v = it.H[k];
                    if (T.IsNum(v)) rows.Add(string.Join("\t", "I", WDec(k), WDec(v)));
                }
            var o = new ToolOutput();
            o.AddUtf8("worldmap.tsv", string.Join("\n", rows) + "\n");
            o.Summary = $"worldmap.tsv: {en.Count} worlds, {nm} map rows, {nd} dungeon markers";
            return o;
        }

        // ================================================================== build_texttables.py
        private static readonly Regex JsTrimRx = new Regex("^[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+|[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+$");
        private static readonly Regex CommentRx = new Regex("\n(//[^\n]+)");

        private static Dictionary<string, string> MapTable(byte[] data, out int count)
        {
            string content = CommentRx.Replace("\n" + Py.Dec(data), "");
            var el = content.Split('#');
            var o = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int s = 0; s < el.Length - 2; s += 2)
            {
                string k = JsTrimRx.Replace(el[s], "").ToLowerInvariant();
                if (k.EndsWith(".rsw", StringComparison.Ordinal)) k = k.Substring(0, k.Length - 4);
                o[k] = Py.Clean(JsTrimRx.Replace(el[s + 1], ""));
            }
            count = o.Count;
            return o;
        }

        private static readonly Regex B64 = new Regex("^[A-Za-z0-9+/]*={0,2}$");

        public static ToolOutput TextTables(IToolInputs inp)
        {
            var o = new ToolOutput();
            var rows = new List<string> { "# id\tenglish\tportuguese  (built by client/tools/clientdata/build_texttables.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) +
                                          "): data\\msgstringtable_ml.csv base64 columns 2 and 7; the id is the 0-based line number)" };
            int n = 0;
            var csv = T.Need(inp, "data\\msgstringtable_ml.csv");
            foreach (var raw in SplitLines(csv))
            {
                var line = Py.StripBytes(raw);
                if (line.Length == 0 || (line.Length >= 2 && line[0] == '/' && line[1] == '/')) continue;
                var c = SplitBytes(line, (byte)',');
                var cols = new List<string>();
                foreach (int k in new[] { 2, 7 })
                {
                    if (k >= c.Count) { cols.Add(""); continue; }
                    string b = Py.Latin1(Py.StripBytes(c[k]));
                    if (!B64.IsMatch(b)) throw new ToolError("msgstringtable_ml.csv: not base64 at line " + n);
                    cols.Add(Py.Clean(Py.Dec(Convert.FromBase64String(b))).Replace("\u200b", "")); // wave 28 (wtextc): zero-width spaces out (4 in the LATAM table; build_texttables.py too)
                }
                rows.Add(n + "\t" + cols[0] + "\t" + cols[1]); n++;
            }
            o.AddUtf8("msgstrings.tsv", string.Join("\n", rows) + "\n");
            var en = MapTable(T.Need(inp, "data\\english\\mapnametable.txt"), out int ne);
            var pt = MapTable(T.Need(inp, "data\\mapnametable.txt"), out int np);
            var mrows = new List<string> { "# map\tenglish\tportuguese  (built by client/tools/clientdata/build_texttables.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) +
                                           "): data\\english\\mapnametable.txt and data\\mapnametable.txt)" };
            var maps = new HashSet<string>(en.Keys, StringComparer.Ordinal); maps.UnionWith(pt.Keys);
            foreach (var m in T.Sorted(maps, Py.CompareStr))
                mrows.Add(m + "\t" + (en.TryGetValue(m, out var a) ? a : "") + "\t" + (pt.TryGetValue(m, out var b) ? b : ""));
            o.AddUtf8("mapnames.tsv", string.Join("\n", mrows) + "\n");
            o.Summary = $"msgstrings.tsv: {n} messages; mapnames.tsv: {mrows.Count - 1} maps ({ne} English, {np} Portuguese)";
            return o;
        }

        /// <summary>re.split(rb'\r?\n', data).</summary>
        private static List<byte[]> SplitLines(byte[] d)
        {
            var o = new List<byte[]>(); int st = 0;
            for (int i = 0; i < d.Length; i++)
                if (d[i] == 10)
                {
                    int end = i > st && d[i - 1] == 13 ? i - 1 : i;
                    var x = new byte[end - st]; Array.Copy(d, st, x, 0, x.Length); o.Add(x); st = i + 1;
                }
            var last = new byte[d.Length - st]; Array.Copy(d, st, last, 0, last.Length); o.Add(last);
            return o;
        }

        private static List<byte[]> SplitBytes(byte[] d, byte sep)
        {
            var o = new List<byte[]>(); int st = 0;
            for (int i = 0; i <= d.Length; i++)
                if (i == d.Length || d[i] == sep) { var x = new byte[i - st]; Array.Copy(d, st, x, 0, x.Length); o.Add(x); st = i + 1; }
            return o;
        }

        // ================================================================== build_spritenames.py
        private static readonly string[] SpriteOrder = { "jobidentity", "npcidentity", "jobname", "pcjobname", "pcjobnamegender", "accessoryid", "accname", "weapontable", "shadowtable", "spriterobeid", "spriterobename" };
        private static readonly (string kind, string table)[] SpriteKinds = { ("npc", "JobNameTable"), ("acc", "AccNameTable"), ("robe", "RobeNameTable"), ("wpn", "WeaponNameTable") };

        public static ToolOutput SpriteNames(IToolInputs inp)
        {
            var vm = new LuaVM();
            var errors = new List<string>();
            foreach (var f in SpriteOrder)
            {
                var data = inp.Grf(Lua_ + "datainfo\\" + f + ".lub");
                if (data == null) { errors.Add(f + ": not in the GRF"); continue; }
                try { vm.Run(data); }
                catch (LuaError e) { errors.Add(f + ": " + e.Message); }
            }
            var G = vm.Globals;
            var rows = new List<byte[]>();
            void Line(string ascii) => rows.Add(Encoding.ASCII.GetBytes(ascii));
            Line("# Loki sprite name tables. Encoding: Latin-1 (EUC-KR bytes preserved). Fields are tab-separated.");
            Line("# built by client/tools/clientdata/build_spritenames.py from " + inp.GrfName + " (GRF 0x" + T.Hex3(inp.GrfVersion) + ")");
            Line("# pc <jobid> <bodySpriteName>  - PC job -> data\\sprite\\<human>\\<body>\\<sex>\\<name>_<sex> (Gravity file names; ids per roBrowserLegacy JobConst)");
            Line("# npc <id> <spriteName>        - mob/NPC id -> sprite name (client jobname.lub + npcidentity.lub)");
            Line("# acc <viewid> <spriteName>    - headgear view id (accname.lub)");
            Line("# robe <viewid> <spriteName>   - garment view id (spriterobename.lub)");
            Line("# wpn <viewid> <name>          - weapon view (weapontable.lub)");
            var counts = new List<string>();
            var pcJson = inp.Part("pcjobnames_hex.json") ?? throw new ToolError("missing part: pcjobnames_hex.json");
            var pc = (PyJson.Obj)PyJson.Parse(Py.Utf8NoBom.GetString(pcJson));
            var pcIds = T.Sorted(pc.Map.Keys().Where(k => k.Length > 0 && k.All(ch => ch >= '0' && ch <= '9')).Select(k => long.Parse(k, System.Globalization.CultureInfo.InvariantCulture)), (a, b) => a.CompareTo(b));
            foreach (var i in pcIds)
            {
                var b = new List<byte>(Encoding.ASCII.GetBytes("pc\t" + i + "\t"));
                b.AddRange(Py.FromHex((string)pc.Map[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
                rows.Add(b.ToArray());
            }
            counts.Add("pc " + pcIds.Count);
            foreach (var (kind, table) in SpriteKinds)
            {
                if (!(G.Get(table) is LuaTable t)) throw new ToolError(table + " was not made by the lubs (" + string.Join("; ", errors) + ")");
                var keys = t.H.Keys();
                if (!keys.All(k => k is long)) throw new ToolError(table + " has a key that is not an id");
                foreach (var k in T.Sorted(keys, T.CompareNum))
                {
                    if (!(t.Get(k) is LuaStr v) || v.B.Any(c => c == 9 || c == 13 || c == 10)) throw new ToolError(table + "[" + k + "] is not a plain name");
                    var b = new List<byte>(Encoding.ASCII.GetBytes(kind + "\t" + T.IntStr(k) + "\t")); b.AddRange(v.B);
                    rows.Add(b.ToArray());
                }
                counts.Add(kind + " " + keys.Count);
            }
            var outb = new List<byte>();
            for (int i = 0; i < rows.Count; i++) { if (i > 0) outb.Add(10); outb.AddRange(rows[i]); }
            outb.Add(10);
            var o = new ToolOutput();
            o.Add("spritenames.txt", outb.ToArray());
            o.Summary = "spritenames.txt: " + string.Join(", ", counts) + "; lub errors (as dump.lua's pcall): " + (errors.Count > 0 ? string.Join("; ", errors) : "none");
            return o;
        }

        // ================================================================== build_efst.py
        public static ToolOutput Efst(IToolInputs inp)
        {
            var vm = new LuaVM(); var G = vm.Globals;
            // dumpskills.lua: JOBID = setmetatable({}, { __index = function() return 0 end })
            var jobid = new LuaTable(); var meta = new LuaTable();
            meta.Set(LuaStr.Of("__index"), new LuaNative("jobid_index", _ => new object[] { 0L })); jobid.Meta = meta;
            G.Set(LuaStr.Of("JOBID"), jobid);
            foreach (var f in new[] { Lua_ + "stateicon\\efstids.lub", "data\\english\\luafiles514\\lua files\\stateicon\\stateiconinfo.lub", Lua_ + "stateicon\\stateiconimginfo.lub" })
                vm.Run(T.Need(inp, f));
            var E = T.Table(G.Get("EFST_IDs"), "EFST_IDs"); var S = G.Get("StateIconList");
            object buff = G.Get("COLOR_TITLE_BUFF"), debuff = G.Get("COLOR_TITLE_DEBUFF"), toggle = G.Get("COLOR_TITLE_TOGGLE");
            // wave 28 (wtextc): the Portuguese titles too (column 5), from the GRF's own data\luafiles514\...\stateiconinfo.lub (the
            // LATAM GRF has both: 704 Portuguese titles on 2026-10-15); run after the English pass, in the same state (it names EFST_IDs)
            object Spt = null;
            var ptInfo = inp.Grf(Lua_ + "stateicon\\stateiconinfo.lub");
            if (ptInfo != null) { vm.Run(ptInfo); Spt = G.Get("StateIconList"); if (ReferenceEquals(Spt, S)) Spt = null; }
            var eids = E.Items().Where(kv => kv.Value is long || kv.Value is double).Select(kv => (i: kv.Value, name: kv.Key)).ToList();
            if (new HashSet<object>(eids.Select(x => x.i), PyKeyComparer.I).Count != eids.Count) throw new ToolError("EFST_IDs gives two names one id: table.sort would order them at random");
            eids = T.Sorted(eids, (a, b) => T.CompareNum(a.i, b.i));
            var sb = new StringBuilder(); int titled = 0, titledPt = 0;
            foreach (var (i, name) in eids)
            {
                var s = S is LuaTable st ? st.Get(i) : null;
                object title = new LuaStr(new byte[0]); string kind = "";
                var d = Lua.Truthy(s) ? Lua.Index(s, LuaStr.Of("descript")) : null;
                var d1 = Lua.Truthy(d) ? Lua.Index(d, 1L) : null;
                if (Lua.Truthy(s) && Lua.Truthy(d) && Lua.Truthy(d1))
                {
                    var t = Lua.Index(d1, 1L);
                    title = Lua.Truthy(t) ? t : new LuaStr(new byte[0]);
                    var c = Lua.Index(d1, 2L);
                    if (Lua.Eq(c, debuff)) kind = "debuff"; else if (Lua.Eq(c, buff)) kind = "buff"; else if (Lua.Eq(c, toggle)) kind = "toggle";
                }
                string ttl = Py.Clean(Py.Dec(EfstToString(title))).Replace("\u200b", "");
                if (ttl.Length > 0) titled++;
                string pt = Spt is LuaTable spt ? EfstTitle(spt.Get(i)) : "";
                if (pt.Length > 0) titledPt++;
                sb.Append(Py.Latin1(EfstToString(i))).Append('\t').Append(AsciiOf(EfstToString(name))).Append('\t').Append(ttl).Append('\t').Append(kind).Append('\t').Append(pt).Append('\n');
            }
            var o = new ToolOutput();
            o.AddUtf8("efst.tsv", sb.ToString());
            o.Summary = $"efst.tsv: {eids.Count} status icons ({titled} with a title, {titledPt} with a Portuguese title)";
            return o;
        }

        /// <summary>Wave 28 (wtextc): StateIconList[id].descript[1][1] of the Portuguese table, cleaned as the English one ("" when none).</summary>
        private static string EfstTitle(object s)
        {
            var d = Lua.Truthy(s) ? Lua.Index(s, LuaStr.Of("descript")) : null;
            var d1 = Lua.Truthy(d) ? Lua.Index(d, 1L) : null;
            var t = Lua.Truthy(d1) ? Lua.Index(d1, 1L) : null;
            return Lua.Truthy(t) ? Py.Clean(Py.Dec(EfstToString(t))).Replace("\u200b", "") : "";
        }

        private static byte[] EfstToString(object v)
        {
            if (v is LuaStr s) return s.B;
            if (v is long || v is double) return Encoding.ASCII.GetBytes(Py.G14(v is double d ? d : (long)v));
            throw new ToolError("dumpskills.lua would write " + Lua.Repr(v));
        }

        private static string AsciiOf(byte[] b)
        {
            foreach (var x in b) if (x > 0x7F) throw new ToolError("an EFST name is not ASCII (build_skilltables.py reads the dump as ascii)");
            return Py.Latin1(b);
        }
    }
}
