using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 3 (w3equip): the names of the random options ("enchant options") an item carries in EQUIPITEM_INFO /
    /// ZC_ITEM_PICKUP_ACK option_data. StreamingAssets/data/randomoptions.tsv is generated from the client's own tables by
    /// client/tools/clientdata/build_equip_tables.py (datainfo\enumvar.lub + addrandomoptionnametable_enus.lub /
    /// addrandomoptionnametable.lub + addrandomoption_f.lub): id, EnumVAR name, English format, Portuguese format, SetPercentValue.
    /// An option reads as the format with "%d" replaced by the value and "%%" by "%", as roBrowserLegacy shows it
    /// (UI/Components/ItemInfo/ItemInfo.js:216-235; rB is a reimplementation, not the official client). The client's
    /// SetPercentValue flag is kept but not applied: what the official client does with it is not known here.
    /// </summary>
    public static class RandomOptionDb
    {
        public sealed class Option { public int Id; public string Enum, English, Portuguese; public bool Percent; }
        private static Dictionary<int, Option> _options;

        public static int Count { get { Load(); return _options.Count; } }
        public static Option Get(int id) { Load(); return _options.TryGetValue(id, out var o) ? o : null; }

        /// <summary>"Max HP + 100" for (VAR_MAXHPAMOUNT, 100); an id missing from the table reads "Option #id (value)".</summary>
        public static string Format(int id, int value)
        {
            var o = Get(id);
            // client wave 4b (w4lang): the table holds the GRF's English and Portuguese texts; the language setting picks the column
            // wave 28 (wtextc): English never falls back to the Portuguese column (an option with no English reads "Option #id (value)")
            string f = o == null ? null : ClientText.Portuguese && !string.IsNullOrEmpty(o.Portuguese) ? o.Portuguese : !string.IsNullOrEmpty(o.English) ? o.English : ClientText.Portuguese ? o.Portuguese : null;
            if (string.IsNullOrEmpty(f)) return Lang.F("Option #{0} ({1})", id, value);
            return f.Replace("%d", value.ToString()).Replace("%%", "%");
        }

        private static void Load()
        {
            if (_options != null) return;
            _options = new Dictionary<int, Option>();
            string p = Loki.Data.ClientData.File("randomoptions.tsv");
            try
            {
                if (!File.Exists(p)) { Debug.LogWarning("[RandomOptionDb] missing " + p + " (client/tools/clientdata/build_equip_tables.py)"); return; }
                foreach (var line in File.ReadLines(p, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 5 || !int.TryParse(c[0], out int id)) continue;
                    _options[id] = new Option { Id = id, Enum = c[1], English = c[2], Portuguese = c[3], Percent = c[4] == "1" };
                }
                Debug.Log($"[RandomOptionDb] {_options.Count} random options");
            }
            catch (System.Exception e) { Debug.LogError("[RandomOptionDb] load failed: " + e.Message); }
        }
    }
}
