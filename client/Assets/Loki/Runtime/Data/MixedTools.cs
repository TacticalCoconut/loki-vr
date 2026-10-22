using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): the client-data tools whose tables mix Gravity's data with rAthena / roBrowserLegacy / zrenderer,
    /// ported step for step. The Gravity half comes from the player's install through the Lua dumpers (<see cref="LubDump"/>,
    /// their text output parsed as the Python tool parses it); the other half from the parts the build ships
    /// (StreamingAssets/data/firstrun, made by build_firstrun_parts.py with the tools' own expressions):
    /// build_tables.py (items.tsv, quests.tsv), build_account_tables.py (achievements_*.json, titles.tsv), build_qol_tables.py
    /// (emotions.tsv, stateicons.tsv), ../appearance/build_appearance.py (appearance.txt, itemviews.tsv),
    /// ../combat/build_combat_data.py (fx/mob_attack_fx.json).
    /// </summary>
    public static class MixedTools
    {
        private static byte[] NeedFile(IToolInputs inp, string rel) => inp.RoFile(rel) ?? throw new ToolError("missing in the RO folder: " + rel);
        private static byte[] NeedPart(IToolInputs inp, string name) => inp.Part(name) ?? throw new ToolError("missing part of the build: firstrun/" + name);

        /// <summary>open(path, encoding='ascii') iterated, each line rstrip('\n')ed.</summary>
        private static List<string> AsciiLines(byte[] data, string what)
        {
            foreach (var b in data) if (b > 0x7F) throw new ToolError(what + ": 'ascii' codec can't decode byte");
            return Py.Lines(Py.Latin1(data)).Select(Py.RstripNewlines).ToList();
        }

        private static List<string> Utf8Lines(byte[] data, string what)
        {
            var s = Py.TryUtf8(data) ?? throw new ToolError(what + ": not UTF-8");
            return Py.Lines(s).Select(Py.RstripNewlines).ToList();
        }

        /// <summary>dec(h, encs) of build_tables.py: bytes.fromhex, each encoding in turn, else Latin-1.</summary>
        private static string DecHex(string h, bool utf8Only)
        {
            var b = Py.FromHex(h);
            return utf8Only ? (Py.TryUtf8(b) ?? Py.Latin1(b)) : Py.Dec(b);
        }

        /// <summary>Wave 28 (wtextc): clean(s) of build_tables.py: tabs / newlines to spaces, "\r" and zero-width spaces (U+200B, in a
        /// few of Gravity's item texts and messages: the game's font draws them as boxes) removed.</summary>
        private static string Txt(string s) => Py.Clean(s).Replace("\u200b", "");

        /// <summary>Wave 28 (wtextc): the install's English item / quest tables (System\english\, the LATAM install has both languages:
        /// the root System\ ones are Portuguese) as id → (name, description) / (title, summary, description); empty when the install
        /// has none. An English name "PlaceHolder" (Gravity's stub) counts as none.</summary>
        private static Dictionary<long, string[]> EnglishItems(IToolInputs inp)
        {
            var o = new Dictionary<long, string[]>();
            var b = inp.RoFile(EnglishItemsFile);
            if (b == null) return o;
            foreach (var line in AsciiLines(LubDump.Items(b), "items_en_raw.tsv"))
            {
                var c = line.Split('\t');
                string nm = Txt(DecHex(c[1], true));
                if (string.Equals(nm, "PlaceHolder", StringComparison.OrdinalIgnoreCase)) nm = "";
                o[Py.ParseInt(c[0])] = new[] { nm, string.Join("\\n", c[7].Split(',').Where(x => x.Length > 0).Select(x => Txt(DecHex(x, true)))) };
            }
            return o;
        }

        private static Dictionary<long, string[]> EnglishQuests(IToolInputs inp)
        {
            var o = new Dictionary<long, string[]>();
            var b = inp.RoFile(EnglishQuestsFile);
            if (b == null) return o;
            foreach (var line in AsciiLines(LubDump.Quests(b), "quests_en_raw.tsv"))
            {
                var c = line.Split('\t');
                o[Py.ParseInt(c[0])] = new[] { Txt(DecHex(c[1], false)), Txt(DecHex(c[2], false)), string.Join("\\n", c[3].Split(',').Where(x => x.Length > 0).Select(x => Txt(DecHex(x, false)))) };
            }
            return o;
        }

        public const string EnglishItemsFile = "System\\english\\iteminfo_new.lub", EnglishQuestsFile = "System\\english\\OngoingQuestInfoList_True.lub";

        // ================================================================== build_tables.py
        public static ToolOutput ItemsQuests(IToolInputs inp, Py.Cp949 cp949)
        {
            var itemsRaw = LubDump.Items(NeedFile(inp, "System\\iteminfo_new.lub"));
            var questsRaw = LubDump.Quests(NeedFile(inp, "System\\OngoingQuestInfoList_True.lub"));
            var itemsEn = EnglishItems(inp); var questsEn = EnglishQuests(inp); // wave 28 (wtextc)
            // en / typ / minlv (the part holds the tool's values: id, clean(name), type, minLevel, sorted by id)
            var en = new Dictionary<long, string>(); var typ = new Dictionary<long, string>(); var minlv = new Dictionary<long, string>(); var enIds = new List<long>();
            foreach (var line in Utf8Lines(NeedPart(inp, "items_rathena.tsv"), "items_rathena.tsv"))
            {
                if (line.Length == 0) continue;
                var c = line.Split('\t');
                long id = Py.ParseInt(c[0]); en[id] = c[1]; typ[id] = c[2]; minlv[id] = c[3]; enIds.Add(id);
            }
            var o = new ToolOutput();
            var sb = new StringBuilder(); int n = 0, nEn = 0;
            var itemLines = AsciiLines(itemsRaw, "items_raw.tsv");
            foreach (var line in itemLines)
            {
                var c = line.Split('\t');
                long iid = Py.ParseInt(c[0]);
                string name = DecHex(c[1], true);
                string res = Py.Hex(cp949.Encode(DecHex(c[2], true)));
                string desc = string.Join("\\n", c[7].Split(',').Where(x => x.Length > 0).Select(x => Txt(DecHex(x, true))));
                // wave 28 (wtextc): columns 9 and 10, the install's English name and description ("" when it has none)
                string[] ie = itemsEn.TryGetValue(iid, out var ie0) ? ie0 : new[] { "", "" };
                if (ie[0].Length > 0 || ie[1].Length > 0) nEn++;
                sb.Append(string.Join("\t", Py.IntStr(iid), Txt(name), Txt(en.TryGetValue(iid, out var e) ? e : ""), res, c[3],
                                      typ.TryGetValue(iid, out var t) ? t : "3", desc, minlv.TryGetValue(iid, out var m) ? m : "0", ie[0], ie[1])).Append('\n');
                n++;
            }
            var clientIds = new HashSet<long>(itemLines.Select(l => Py.ParseInt(l.Split('\t')[0])));
            foreach (var iid in enIds.OrderBy(x => x))
            {
                if (clientIds.Contains(iid)) continue;
                sb.Append(string.Join("\t", Py.IntStr(iid), Txt(en[iid]), Txt(en[iid]), "", "0", typ[iid], "", minlv[iid], "", "")).Append('\n'); n++;
            }
            o.AddUtf8("items.tsv", sb.ToString());
            // qen: the part lists quest_db.yml's titles in the tool's dict order
            var qen = new OrderedMap<long, string>();
            foreach (var line in Utf8Lines(NeedPart(inp, "quests_rathena.tsv"), "quests_rathena.tsv"))
            {
                if (line.Length == 0) continue;
                int tab = line.IndexOf('\t');
                qen.Set(Py.ParseInt(line.Substring(0, tab)), line.Substring(tab + 1));
            }
            var qb = new StringBuilder(); int mq = 0, mqEn = 0; var seen = new HashSet<long>();
            foreach (var line in AsciiLines(questsRaw, "quests_raw.tsv"))
            {
                var c = line.Split('\t');
                long qid = Py.ParseInt(c[0]); seen.Add(qid);
                string desc = string.Join("\\n", c[3].Split(',').Where(x => x.Length > 0).Select(x => Txt(DecHex(x, false))));
                // wave 28 (wtextc): columns 6-8, the install's English title, summary and description ("" when it has none)
                string[] qe = questsEn.TryGetValue(qid, out var qe0) ? qe0 : new[] { "", "", "" };
                if (questsEn.ContainsKey(qid)) mqEn++;
                qb.Append(string.Join("\t", Py.IntStr(qid), Txt(qen.TryGetValue(qid, out var tt) ? tt : ""), Txt(DecHex(c[1], false)), Txt(DecHex(c[2], false)), desc, qe[0], qe[1], qe[2])).Append('\n');
                mq++;
            }
            foreach (var kv in qen.Items())
                if (!seen.Contains(kv.Key)) { qb.Append(string.Join("\t", Py.IntStr(kv.Key), Txt(kv.Value), "", "", "", "", "", "")).Append('\n'); mq++; }
            o.AddUtf8("quests.tsv", qb.ToString());
            o.Summary = $"items {n} quests {mq} en items {en.Count} en quests {qen.Count}; the install's English table: {nEn} items, {mqEn} quests";
            return o;
        }

        // ================================================================== build_account_tables.py (achievements, titles)
        private static string DecAcc(string h, Py.Cp949 cp949)
        {
            var b = Py.FromHex(h);
            return Py.TryUtf8(b) ?? Py.TryCp1252(b) ?? cp949.TryDecode(b) ?? Py.Latin1(b);
        }

        private sealed class ClientAch
        {
            public long Ui, Ma, Mi, Sc, LItem, LTitle, LBuff; public string Lg, N, S, D;
            public List<List<object>> Res = new List<List<object>>();
        }

        private static Dictionary<long, ClientAch> ReadLubDump(byte[] raw, Py.Cp949 cp949)
        {
            var o = new Dictionary<long, ClientAch>();
            foreach (var line in AsciiLines(raw, "achievement dump"))
            {
                var c = line.Split('\t');
                var a = new ClientAch
                {
                    Ui = Py.ParseInt(c[1]), Lg = DecAcc(c[2], cp949), Ma = Py.ParseInt(c[3]), Mi = Py.ParseInt(c[4]), Sc = Py.ParseInt(c[5]),
                    N = DecAcc(c[6], cp949), S = DecAcc(c[7], cp949), D = DecAcc(c[8], cp949), LItem = Py.ParseInt(c[9]), LTitle = Py.ParseInt(c[10]), LBuff = Py.ParseInt(c[11]),
                };
                for (int i = 12; i < c.Length; i++)
                {
                    var r = c[i].Split('|');
                    if (r.Length != 3) throw new ToolError("an achievement resource is not text|count|shortcut (Python: ValueError)");
                    a.Res.Add(new List<object> { DecAcc(r[0], cp949), Py.ParseInt(r[1]) });
                }
                o[Py.ParseInt(c[0])] = a;
            }
            return o;
        }

        public static ToolOutput Account(IToolInputs inp, Py.Cp949 cp949)
        {
            var o = new ToolOutput();
            var srvJ = (PyJson.Obj)PyJson.Parse(Py.Utf8NoBom.GetString(NeedPart(inp, "achievements_rathena.json")));
            var srv = new Dictionary<long, PyJson.Obj>();
            foreach (var kv in srvJ.Map.Items()) srv[Py.ParseInt(kv.Key)] = (PyJson.Obj)kv.Value;
            int clientCount = 0;
            foreach (var (lang, path) in new[] { ("pt", "System\\achievement_list.lub"), ("en", "System\\english\\achievement_list.lub") })
            {
                var cl = ReadLubDump(LubDump.Achievements(NeedFile(inp, path)), cp949);
                if (lang == "pt") clientCount = cl.Count;
                var res = new PyJson.Obj();
                var aids = new HashSet<long>(cl.Keys); aids.UnionWith(srv.Keys);
                foreach (var aid in aids.OrderBy(x => x))
                {
                    cl.TryGetValue(aid, out var c); srv.TryGetValue(aid, out var s);
                    var e = new PyJson.Obj();
                    e.Map.Set("n", c != null ? c.N : s.Get("name"));
                    e.Map.Set("s", c != null ? c.S : "");
                    e.Map.Set("d", c != null ? c.D : "");
                    e.Map.Set("g", s != null ? s.Get("g") : c.Lg);
                    e.Map.Set("ma", c != null ? c.Ma : 0L);
                    e.Map.Set("mi", c != null ? c.Mi : 0L);
                    e.Map.Set("ui", c != null ? c.Ui : 0L);
                    e.Map.Set("sc", s != null ? s.Get("sc") : c.Sc);
                    e.Map.Set("item", s != null ? s.Get("item") : c.LItem);
                    e.Map.Set("amt", s != null ? s.Get("amt") : (c.LItem != 0 ? 1L : 0L));
                    e.Map.Set("title", s != null ? s.Get("title") : c.LTitle);
                    e.Map.Set("srv", s != null);
                    var rl = c != null ? c.Res.Select(x => new List<object>(x)).ToList() : new List<List<object>>();
                    if (s != null)
                        foreach (var tg in (List<object>)s.Get("targets"))
                        {
                            var pair = (List<object>)tg;
                            long i = Convert.ToInt64(pair[0], CultureInfo.InvariantCulture);
                            while (rl.Count <= i) rl.Add(new List<object> { "", 1L });
                            long idx = i >= 0 ? i : rl.Count + i;
                            if (idx < 0) throw new ToolError("an achievement target index is out of range (Python: IndexError)");
                            rl[(int)idx][1] = Convert.ToInt64(pair[1], CultureInfo.InvariantCulture);
                        }
                    e.Map.Set("res", rl.Cast<object>().ToList());
                    res.Map.Set(Py.IntStr(aid), e);
                }
                o.AddUtf8("account/achievements_" + lang + ".json", PyJson.Dump(res, false, ",", ":"));
            }
            // titles.tsv
            var titles = new Dictionary<long, Dictionary<string, string>>();
            foreach (var (lang, path) in new[] { ("pt", "data\\luafiles514\\lua files\\datainfo\\titletable.lub"), ("en", "data\\english\\luafiles514\\lua files\\datainfo\\titletable.lub") })
                foreach (var line in AsciiLines(LubDump.Titles(T.Need(inp, path)), "title dump"))
                {
                    var c = line.Split('\t');
                    if (c.Length != 2) throw new ToolError("a title line is not id<TAB>hex (Python: ValueError)");
                    long i = Py.ParseInt(c[0]);
                    if (!titles.TryGetValue(i, out var d)) titles[i] = d = new Dictionary<string, string>();
                    d[lang] = DecAcc(c[1], cp949);
                }
            var sb = new StringBuilder();
            foreach (var i in titles.Keys.OrderBy(x => x))
            {
                var d = titles[i];
                string en = d.TryGetValue("en", out var e1) ? e1 : "", pt = d.TryGetValue("pt", out var p1) ? p1 : "";
                sb.Append(Py.IntStr(i)).Append('\t').Append(d.ContainsKey("pt") ? pt : en).Append('\t').Append(d.ContainsKey("en") ? en : pt).Append('\n');
            }
            o.AddUtf8("account/titles.tsv", sb.ToString());
            o.Summary = $"achievements: client {clientCount}, server {srv.Count}; titles {titles.Count}";
            return o;
        }

        // ================================================================== build_qol_tables.py (emotions, state icons)
        private static readonly Regex CallRx = new Regex(@"^CALL InsertEmotionMsgListTable\((\d+), (.*)\)$");
        private static readonly Regex RowRx = new Regex(@"^(\d+) \| (-?\d+) \| (\d+)$");

        /// <param name="msgstrings">the msgstrings.tsv made by build_texttables.py (the tool reads it from its out dir).</param>
        public static ToolOutput Qol(IToolInputs inp, byte[] msgstrings)
        {
            var o = new ToolOutput();
            // ---- emotions
            var msgs = new Dictionary<long, string>();
            foreach (var line in Utf8Lines(msgstrings, "msgstrings.tsv"))
            {
                var c = line.Split('\t');
                if (c.Length > 0 && c[0].Length > 0 && c[0].All(char.IsDigit)) msgs[Py.ParseInt(c[0])] = c[1];
            }
            var aliases = new Dictionary<long, List<string>>();
            var rows = new List<(long eid, long msg, long act)>();
            var emo = LubDump.Emotions(T.Need(inp, LubDump.LuaDir + "emotion\\emotionlist.lub"));
            foreach (var raw in Py.Lines(Py.Utf8Replace(emo)))
            {
                string line = Py.Strip(raw);
                var m = CallRx.Match(line);
                if (m.Success)
                {
                    long eid = Py.ParseInt(m.Groups[1].Value); string txt = m.Groups[2].Value;
                    if (!txt.StartsWith("MSG", StringComparison.Ordinal) && txt.IndexOf('\ufffd') < 0)
                    {
                        if (!aliases.TryGetValue(eid, out var l)) aliases[eid] = l = new List<string>();
                        l.Add(txt);
                    }
                    continue;
                }
                m = RowRx.Match(line);
                if (m.Success) rows.Add((Py.ParseInt(m.Groups[1].Value), Py.ParseInt(m.Groups[2].Value), Py.ParseInt(m.Groups[3].Value)));
            }
            rows = T.Sorted(rows, (a, b) => a.eid != b.eid ? a.eid.CompareTo(b.eid) : a.msg != b.msg ? a.msg.CompareTo(b.msg) : a.act.CompareTo(b.act));
            var sb = new StringBuilder();
            foreach (var (eid, msg, act) in rows)
            {
                string label = msg >= 0 && msgs.TryGetValue(msg, out var lb) ? lb : "";
                sb.Append(Py.IntStr(eid)).Append('\t').Append(Py.IntStr(act)).Append('\t').Append(label).Append('\t')
                  .Append(string.Join(" ", aliases.TryGetValue(eid, out var al) ? al : new List<string>())).Append('\n');
            }
            o.AddUtf8("emotions.tsv", sb.ToString());
            // ---- state icons: the client's StateIconImgList, then roBrowserLegacy's list for the EFSTs it lacks (colour 4)
            var icons = new Dictionary<long, (string hex, long pri)>();
            foreach (var line in Py.Lines(Py.Latin1(LubDump.StateIcons(inp))).Select(Py.RstripNewlines))
            {
                var c = line.Split('\t');
                if (c.Length < 4) continue;
                long efst = Py.ParseInt(c[0]), pri = Py.ParseInt(c[2]);
                icons[efst] = (Py.Hex(Py.Latin1Bytes(c[3])), pri);
            }
            int nrb = 0;
            foreach (var line in Utf8Lines(NeedPart(inp, "stateicons_rb.tsv"), "stateicons_rb.tsv"))
            {
                if (line.Length == 0) continue;
                var c = line.Split('\t');
                long efst = Py.ParseInt(c[0]);
                if (!icons.ContainsKey(efst)) { icons[efst] = (c[1], 4); nrb++; }
            }
            var si = new StringBuilder();
            foreach (var efst in icons.Keys.OrderBy(x => x)) si.Append(Py.IntStr(efst)).Append('\t').Append(icons[efst].hex).Append('\t').Append(Py.IntStr(icons[efst].pri)).Append('\n');
            o.AddUtf8("stateicons.tsv", si.ToString());
            o.Summary = $"emotions {rows.Count}; stateicons {icons.Count} from roBrowser {nrb}";
            return o;
        }

        // ================================================================== ../appearance/build_appearance.py
        public static ToolOutput Appearance(IToolInputs inp)
        {
            var o = new ToolOutput();
            var outb = new List<byte>(NeedPart(inp, "appearance_base.txt"));   // every line before the lub rows, each with its '\n'
            var lubLines = AsciiLines(LubDump.Lubs(inp), "lubs.tsv").Where(l => l.Length > 0).ToList();
            // '\n'.join(out) + '\n' where out = base lines + lub lines: the base already ends each of its lines with '\n'
            if (lubLines.Count > 0) outb.AddRange(Py.Latin1Bytes(string.Join("\n", lubLines) + "\n"));
            if (lubLines.Count == 0 && outb.Count == 0) outb.Add(10);
            o.Add("appearance.txt", outb.ToArray());
            // ---- itemviews.tsv
            Dictionary<long, long> Classnums(byte[] raw)
            {
                var d = new Dictionary<long, long>();
                foreach (var line in AsciiLines(raw, "classnum dump"))
                {
                    var c = line.Split('\t');
                    if (c.Length != 2) throw new ToolError("a classnum line is not id<TAB>ClassNum (Python: ValueError)");
                    d[Py.ParseInt(c[0])] = Py.ParseInt(c[1]);
                }
                return d;
            }
            var neu = Classnums(LubDump.Classnum(NeedFile(inp, "System\\iteminfo_new.lub")));
            var old = Classnums(LubDump.Classnum(NeedFile(inp, "System\\itemInfo.lua")));
            var cn = new Dictionary<long, long>(old);
            foreach (var kv in neu) cn[kv.Key] = kv.Value;
            var typ = new Dictionary<long, long>(); var sub = new Dictionary<long, long>(); var left = new Dictionary<long, long>();
            foreach (var line in Utf8Lines(NeedPart(inp, "itemviews_rathena.tsv"), "itemviews_rathena.tsv"))
            {
                if (line.Length == 0) continue;
                var c = line.Split('\t');
                long id = Py.ParseInt(c[0]); typ[id] = Py.ParseInt(c[1]);
                long s = Py.ParseInt(c[2]); if (s != -1) sub[id] = s;
                left[id] = Py.ParseInt(c[3]);
            }
            var ids = new HashSet<long>(typ.Keys); ids.UnionWith(cn.Where(kv => kv.Value != 0).Select(kv => kv.Key));
            var sb = new StringBuilder(); int n = 0;
            foreach (var i in ids.OrderBy(x => x))
            {
                sb.Append(Py.IntStr(i)).Append('\t').Append(Py.IntStr(typ.TryGetValue(i, out var t) ? t : -1)).Append('\t').Append(Py.IntStr(cn.TryGetValue(i, out var c) ? c : -1))
                  .Append('\t').Append(Py.IntStr(sub.TryGetValue(i, out var s) ? s : -1)).Append('\t').Append(Py.IntStr(left.TryGetValue(i, out var l) ? l : 0)).Append('\n');
                n++;
            }
            o.Add("itemviews.tsv", Py.AsciiBytes(sb.ToString()));
            o.Summary = $"appearance lub rows {lubLines.Count}, itemviews {n}, classnum new {neu.Count} old {old.Count}";
            return o;
        }

        // ================================================================== ../combat/build_combat_data.py (fx/mob_attack_fx.json)
        private const string Monster = "\u00b8\u00f3\u00bd\u00ba\u00c5\u00cd"; // 몬스터 as CP949 bytes in Latin-1 (build_combat_data.py MONSTER)
        private static readonly string[] SpriteKeys = { "spriteName", "file", "absoluteSpriteName" };

        /// <param name="effectTable">StreamingAssets/data/fx/effect_table.json (tools/fx, roBrowserLegacy's effect table: ships).</param>
        public static ToolOutput CombatFx(IToolInputs inp, byte[] effectTable)
        {
            var rb = (PyJson.Obj)PyJson.Parse(Py.Utf8NoBom.GetString(NeedPart(inp, "mob_attack_rb.json")));
            var table = (PyJson.Obj)PyJson.Parse(Py.Utf8NoBom.GetString(effectTable));
            var proj = (PyJson.Obj)rb.Get("projectile"); var spawn = (PyJson.Obj)rb.Get("spawn");
            var keySet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in proj.Map.Items().Concat(spawn.Map.Items())) keySet.Add((string)v.Value);
            var sprites = new PyJson.Obj(); int bad = 0; var log = new List<string>();
            foreach (var key in T.Sorted(keySet, Py.CompareStr))
            {
                if (!(table.Get(key) is PyJson.Obj e)) { log.Add("effect key missing from effect_table.json: " + key); bad++; continue; }
                foreach (var partO in (List<object>)e.Get("entries"))
                {
                    var part = (PyJson.Obj)partO;
                    foreach (var sk in SpriteKeys)
                    {
                        if (!(part.Get(sk) is string p)) continue;
                        string where;
                        if (sk == "absoluteSpriteName") where = p.Replace('/', '\\');
                        else if (p.StartsWith("../", StringComparison.Ordinal)) where = "data\\sprite\\" + string.Join("\\", p.Substring(3).Split('/'));
                        else continue;
                        if (inp.Grf(where + ".spr") != null) continue;
                        string name = p.Split('/').Last();
                        string found = null;
                        foreach (var folder in new[] { "npc", Monster })
                        {
                            string cand = "data\\sprite\\" + folder + "\\" + name;
                            if (inp.Grf(cand + ".spr") != null && inp.Grf(cand + ".act") != null) { found = folder; break; }
                        }
                        if (found == null) { log.Add(key + " " + sk + " " + p + ": NOT FOUND in npc or the monster folder"); continue; }
                        var s = new PyJson.Obj(); s.Map.Set("key", sk); s.Map.Set("path", "../" + found + "/" + name);
                        sprites.Map.Set(key, s);
                    }
                }
            }
            // build_combat_data.py writes the file even when an effect key is missing (then it exits 1): so does this port, and says so
            var fx = new PyJson.Obj();
            fx.Map.Set("source", "roBrowserLegacy src/DB/Monsters/AttackEffectTable.js (tools/combat/rb_combat_tables.mjs + build_combat_data.py)");
            fx.Map.Set("projectile", proj); fx.Map.Set("spawn", spawn); fx.Map.Set("sprites", sprites);
            // json.dump(fx, open(path, 'w', encoding='utf-8'), indent=1): text mode on Windows writes '\n' as "\r\n"
            string text = PyJson.Dump(fx, true, ",", ": ", 1).Replace("\n", "\r\n");
            var o = new ToolOutput();
            o.AddUtf8("fx/mob_attack_fx.json", text);
            o.Summary = $"fx/mob_attack_fx.json ({proj.Map.Count} projectile + {spawn.Map.Count} spawn classes, {sprites.Map.Count} sprite paths fixed)" +
                        (bad > 0 ? $"; {bad} effect key(s) missing (the Python tool exits 1)" : "") + (log.Count > 0 ? "; " + string.Join("; ", log) : "");
            return o;
        }
    }
}
