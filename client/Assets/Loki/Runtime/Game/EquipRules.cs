using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 15 (w15gear), point 29 of the design's wave 14 list ("if I am a thief and an item is only meant for apprentices, it shouldn't be
    /// considered as a valid option"): whether THIS character may wear an item, the item part of rAthena's pc_isequip (pc.cpp:1850-1968)
    /// run on the client, so the equipment page's "N in the bag fit" and its picker list only what the server would accept.
    /// The data is rAthena's own (Resources/Items/equip_rules.txt, made by client/tools/w15/build_equip_rules.py from the oracle's
    /// e985006: item_db Jobs / Classes / Gender / EquipLevelMin / EquipLevelMax / SubType, pc_jobid2mapid, the MAPID / ITEMJ constants).
    /// Checked, in pc_isequip's order:
    ///   base level      item->elv / item->elvmax against status.base_level (pc.cpp:1880-1883)
    ///   sex             item->sex != SEX_BOTH and != status.sex (1884-1885)
    ///   broken          attribute 1 (1888-1889): the inventory's Damaged flag
    ///   ammunition      arrows need a bow / instrument / whip in hand, bullets and shells a gun (ammo_check_weapon: yes,
    ///                   conf/battle/battle.conf:177; 1898-1918), throwing daggers Venom Knife (AS_VENOMKNIFE, 1904-1907); the hand's type
    ///                   is pc_calcweapontype's (pc.cpp): the right hand's weapon, else the left's
    ///   job             pc_job_can_use_item (class_base[2-1 / 2-2 / base] has the job's bit; pc.cpp)
    ///   class           pc_isItemClass, the RENEWAL branch (normal / trans / baby / third / trans-third / baby-third / fourth)
    /// Not checked (the client cannot know them or they pass in a moment; the server still decides): the GM permission
    /// PC_PERM_USE_ALL_EQUIPMENT (1857-1858), the Super Novice spirit's extras (1863-1878), a map's no-equip rule (1892-1893), the
    /// cannonball's Mado Gear / cart rule (1919-1931), and the strip statuses (1941-1958).
    /// An item without a row fits anyone (the tool leaves out the default rule); an unknown job (before the map) filters nothing.
    /// </summary>
    public static class EquipRules
    {
        /// <summary>The character's job id (status.class_) and sex (0 female, 1 male), the skill level of a skill id: set by LokiGame.</summary>
        public static Func<int> Job = () => -1;
        public static Func<int> Sex = () => -1;
        public static Func<int, int> SkillLevel = _ => 0;

        private struct Rule { public int Mask, Sex, Elv, ElvMax, Sub; }
        private static Dictionary<int, Rule> _items;
        private static Dictionary<int, ulong> _jobs;
        private static List<(ulong b0, ulong b1, ulong b2, int upper)> _masks;
        private static Dictionary<string, ulong> _c;
        private const int AS_VENOMKNIFE = 1004; // rAthena db/re/skill_db.yml Id 1004 AS_VENOMKNIFE

        public static bool Loaded { get { Load(); return _items.Count > 0; } }
        public static int RuleCount { get { Load(); return _items.Count; } }

        private static ulong C(string n) => _c.TryGetValue(n, out var v) ? v : 0;

        private static void Load()
        {
            if (_items != null) return;
            _items = new Dictionary<int, Rule>(); _jobs = new Dictionary<int, ulong>(); _masks = new List<(ulong, ulong, ulong, int)>(); _c = new Dictionary<string, ulong>();
            var ta = Resources.Load<TextAsset>("Items/equip_rules");
            if (ta == null) { Debug.LogWarning("[EquipRules] Resources/Items/equip_rules is missing: every item counts as wearable"); return; }
            int bad = 0;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(' ');
                try
                {
                    switch (f[0])
                    {
                        case "C": _c[f[1]] = ulong.Parse(f[2]); break;
                        case "J": _jobs[int.Parse(f[1])] = Convert.ToUInt64(f[2], 16); break;
                        case "M": _masks.Add((Convert.ToUInt64(f[2], 16), Convert.ToUInt64(f[3], 16), Convert.ToUInt64(f[4], 16), Convert.ToInt32(f[5], 16))); break;
                        case "I": _items[int.Parse(f[1])] = new Rule { Mask = int.Parse(f[2]), Sex = int.Parse(f[3]), Elv = int.Parse(f[4]), ElvMax = int.Parse(f[5]), Sub = int.Parse(f[6]) }; break;
                        default: bad++; break;
                    }
                }
                catch (Exception) { bad++; }
            }
            Debug.Log($"[EquipRules] {_items.Count} item rules, {_masks.Count} job rules, {_jobs.Count} jobs{(bad > 0 ? $" ({bad} malformed rows left out)" : "")}");
        }

        /// <summary>Client wave 18 (w18gear): a job's MAPID (pc_jobid2mapid; -1 unknown) and a constant of the data (JOBL_*, MAPID_FIRSTMASK ...:
        /// 0 unknown), for the Character presets' path (LokiGame.W14Kit.cs W14KChangeLevel).</summary>
        public static long MapIdOf(int job) { Load(); return _jobs.TryGetValue(job, out ulong m) ? (long)m : -1; }
        public static ulong Const(string name) { Load(); return C(name); }

        /// <summary>rAthena's weapon type of an item (W_*; 0 for none / not a weapon) and its ammunition type (AMMO_*) for ammunition.</summary>
        public static int SubType(int itemId) { Load(); return _items.TryGetValue(itemId, out var r) ? r.Sub : 0; }

        /// <summary>Whether this character may wear <paramref name="e"/> now (null) or why not (a short reason, for the log and the smoke).</summary>
        public static string WhyNot(Inventory.Entry e, Inventory inv, int baseLevel) =>
            e == null ? "no item" : WhyNot(e.ItemId, e.Info.Type, e.Damaged, Job(), Sex(), baseLevel, inv != null ? HandWeaponType(inv) : 0, SkillLevel(AS_VENOMKNIFE));

        /// <summary>pc_isequip's item part (see the class comment) for an item id, a job id, a sex, a base level and the weapon type in hand.</summary>
        public static string WhyNot(int itemId, int itemType, bool broken, int job, int sex, int baseLevel, int handWeapon, int venomKnife)
        {
            Load();
            if (!_items.TryGetValue(itemId, out var r)) return broken ? "broken" : null;
            if (r.Elv > 0 && baseLevel < r.Elv) return "base level " + r.Elv;
            if (r.ElvMax > 0 && baseLevel > r.ElvMax) return "base level " + r.ElvMax + " or lower";
            if (r.Sex != 2 && sex >= 0 && sex != r.Sex) return r.Sex == 0 ? "female only" : "male only";
            if (broken) return "broken";
            if (itemType == 10) // IT_AMMO
            {
                ulong w = (ulong)handWeapon;
                if (r.Sub == (int)C("AMMO_ARROW") && w != C("W_BOW") && w != C("W_MUSICAL") && w != C("W_WHIP")) return "needs a bow";
                if (r.Sub == (int)C("AMMO_DAGGER") && venomKnife <= 0) return "needs Venom Knife";
                if ((r.Sub == (int)C("AMMO_BULLET") || r.Sub == (int)C("AMMO_SHELL")) && w != C("W_REVOLVER") && w != C("W_RIFLE") && w != C("W_GATLING")
                    && w != C("W_SHOTGUN") && w != C("W_GRENADE")) return "needs a gun";
            }
            if (job < 0 || !_jobs.TryGetValue(job, out ulong mapid) || r.Mask < 0 || r.Mask >= _masks.Count) return null;
            var m = _masks[r.Mask];
            // pc_job_can_use_item: the job's bit in class_base[2-1 / 2-2 / base]
            ulong bit = 1UL << (int)(mapid & C("MAPID_FIRSTMASK"));
            ulong cb = (mapid & C("JOBL_2_1")) != 0 ? m.b1 : (mapid & C("JOBL_2_2")) != 0 ? m.b2 : m.b0;
            if ((cb & bit) == 0) return "not for this job";
            if (!ItemClass(mapid, (ulong)m.upper)) return "not for this class";
            return null;
        }

        /// <summary>pc_isItemClass, the RENEWAL branch (pc.cpp): which job tiers the item's Classes allow.</summary>
        private static bool ItemClass(ulong c, ulong up)
        {
            ulong UPPER = C("JOBL_UPPER"), BABY = C("JOBL_BABY"), THIRD = C("JOBL_THIRD"), FOURTH = C("JOBL_FOURTH");
            if ((up & C("ITEMJ_NORMAL")) != 0 && (c & (UPPER | BABY | THIRD | FOURTH)) == 0) return true;
            if ((up & C("ITEMJ_UPPER")) != 0 && (c & UPPER) != 0 && (c & THIRD) == 0) return true;
            if ((up & C("ITEMJ_BABY")) != 0 && (c & BABY) != 0 && (c & THIRD) == 0) return true;
            if ((up & C("ITEMJ_THIRD")) != 0 && (c & THIRD) != 0 && (c & (UPPER | BABY)) == 0 && (c & FOURTH) == 0) return true;
            if ((up & C("ITEMJ_THIRD_UPPER")) != 0 && (c & THIRD) != 0 && (c & UPPER) != 0 && (c & FOURTH) == 0) return true;
            if ((up & C("ITEMJ_THIRD_BABY")) != 0 && (c & THIRD) != 0 && (c & BABY) != 0 && (c & FOURTH) == 0) return true;
            if ((up & C("ITEMJ_FOURTH")) != 0 && (c & FOURTH) != 0) return true;
            return false;
        }

        /// <summary>status.weapon for the ammunition rule (pc_calcweapontype): the right hand's weapon type, else the left hand's.</summary>
        public static int HandWeaponType(Inventory inv)
        {
            var r = inv.WornAt(0x02); var l = inv.WornAt(0x20);
            int wr = r != null && r.Info.Type == 5 ? SubType(r.ItemId) : 0, wl = l != null && l.Info.Type == 5 && l != r ? SubType(l.ItemId) : 0;
            return wl == 0 ? wr : wr == 0 ? wl : wr; // two weapons: a W_DOUBLE_* type, never a bow or a gun; the right hand's stands for it
        }
    }
}
