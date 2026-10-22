using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Wave 28 (wcodex): the Codex's data (design note, 2026-10-15: a book on the wrist menu with "every item and creature you've encountered so
    /// far"). Read at run time from the TEXT PACK, a folder outside the build (wtextc owns it; design note: "Include the text pack in the friends'
    /// package"): <c>&lt;pack&gt;/codex/creatures.tsv</c> and <c>codex/items.tsv</c>, written by client/tools/codex/build_codex_pack.py from
    /// the export's bestiarium / itemarium checked against rAthena's own database (rAthena's values win; GPL like Loki; no Gravity text).
    /// The pictures are never here: the book draws them from the player's own RO data (monster sprites, item icons).
    /// Nothing is loaded until asked: the creatures (2,675 rows, ~0.7 MB) at the first look at a monster or the book's first opening, the
    /// items (29,356 rows, ~2.2 MB) when the Itemarium is first shown.
    /// </summary>
    public static class CodexData
    {
        public sealed class Creature
        {
            public int Id, Level, Hp, BaseExp, JobExp, Atk, Matk, Def, Mdef, Range, Size, Race, Element, ElementLevel, Class, Mode, Walk;
            public int[] Stats = new int[6];
            /// <summary>Damage taken from each attacking element in %, rAthena's attr_fix for this creature's element and level, in the order
            /// Neutral Water Earth Fire Wind Poison Holy Dark Ghost Undead.</summary>
            public int[] Attr = new int[10];
            public bool Mvp; public string Name = "";
            public List<(int id, int level, string name)> Skills = new List<(int, int, string)>();
            /// <summary>Drops: item id, rate in hundredths of a percent (rAthena's 10000 = 100 %), steal-protected (a card).</summary>
            public List<(int item, int rate, bool card)> Drops = new List<(int, int, bool)>(), MvpDrops = new List<(int, int, bool)>();
            public List<(string map, int count, int respawnMs)> Habitats = new List<(string, int, int)>();
            public int TameItem, FoodItem, EggItem;
            /// <summary>How it behaves, in a sentence: the export's prose for its AI type, read against rAthena's mode table (the converter
            /// gives it only when the creature's own modes change none of its AI's behavior bits); "" none.</summary>
            public string Behavior = "";
            /// <summary>Where it comes from besides the map spawns (the converter, from rAthena): "mob:&lt;ids&gt;" (monsters whose skills summon
            /// it: mob_skill_db NPC_SUMMONSLAVE / NPC_SUMMONMONSTER), "item:&lt;ids&gt;" (items that call up its summon group: mob_summon.yml),
            /// "script" (a loaded script's monster command: quests, events, instances), joined by "|"; "" none of these.</summary>
            public string Origin = "";
            /// <summary>Wave 28 (wcodex, 2026-10-16): the pack's codex/places.tsv (client/tools/codex/build_codex_pack.py): for each spawn map
            /// of <see cref="Habitats"/>, its nearest town ("" none), its kind (field / dungeon / town / place) and where on it the creature
            /// spawns ("all", a compass part such as "north-east" or "middle", or two joined by "+"). Empty with an older pack.</summary>
            public List<(string map, string town, string kind, string area)> Places = new List<(string, string, string, string)>();
            /// <summary>rAthena's boss type (mob.cpp:377-384): 2 MVP (the Mvp mode), 1 mini-boss (Class Boss), 0 none.</summary>
            public int BossType => Mvp ? 2 : Class == 1 ? 1 : 0;
        }
        public sealed class Item
        {
            public int Id, Slots, Weight, Atk, Matk, Def, WeaponLevel, ArmorLevel, ReqLevel, Buy, Sell; public uint Locations; public bool Refineable;
            public string Name = "", Type = "", Sub = "", Jobs = "";
            public List<(int mob, int rate)> DroppedBy = new List<(int, int)>();
            public List<(string npc, string map, int x, int y, int price, string kind)> SoldBy = new List<(string, string, int, int, int, string)>();
        }

        public static readonly string[] Elements = { "Neutral", "Water", "Earth", "Fire", "Wind", "Poison", "Holy", "Dark", "Ghost", "Undead" };
        public static readonly string[] Races = { "Formless", "Undead", "Brute", "Plant", "Insect", "Fish", "Demon", "Demihuman", "Angel", "Dragon", "Human", "Doram" };
        public static readonly string[] Sizes = { "Small", "Medium", "Large" };

        // ------------------------------------------------------------------ where the pack is
        /// <summary>The text pack's root folder (null: none). Waves 27-28 merge (wcodex 18:09 / wtextc 18:31 in COORD): wtextc's loader,
        /// Data/TextPack.cs: loki.json "textPack" (default "textpack" next to Loki.exe), null when there is no usable pack (no folder, no
        /// textpack.json or a refused manifest). The Codex's files are read through <see cref="Loki.Data.TextPack.Text"/>: listed in the
        /// manifest and matching its SHA-256, or not read at all (the same tamper check as the text tables).</summary>
        public static string PackRoot => Loki.Data.TextPack.Folder;
        public static bool HasPack => PackRoot != null || Resources.Load<TextAsset>("Codex/creatures") != null;

        // ------------------------------------------------------------------ the creatures
        private static Dictionary<int, Creature> _creatures; private static Dictionary<int, Item> _items;
        public static int CreatureCount { get { LoadCreatures(); return _creatures.Count; } }
        public static int ItemCount { get { LoadItems(); return _items.Count; } }
        public static float CreaturesLoadMs { get; private set; }
        public static float ItemsLoadMs { get; private set; }
        public static Creature GetCreature(int id) { LoadCreatures(); return _creatures.TryGetValue(id, out var c) ? c : null; }
        public static Item GetItem(int id) { LoadItems(); return _items.TryGetValue(id, out var i) ? i : null; }
        public static bool ItemsLoaded => _items != null;

        private static void LoadCreatures()
        {
            if (_creatures != null) return;
            _creatures = new Dictionary<int, Creature>();
            var text = PackLines("codex/creatures.tsv"); if (text == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool head = true;
                foreach (var line in text)
                {
                    if (head) { head = false; continue; }
                    var c = line.Split('\t'); if (c.Length < 31 || !int.TryParse(c[0], out int id)) continue;
                    var k = new Creature { Id = id, Name = c[1], Level = I(c[2]), Hp = I(c[3]), BaseExp = I(c[4]), JobExp = I(c[5]), Atk = I(c[6]), Matk = I(c[7]), Def = I(c[8]), Mdef = I(c[9]),
                                           Range = I(c[16]), Size = I(c[17]), Race = I(c[18]), Element = I(c[19]), ElementLevel = I(c[20]), Class = I(c[21]), Mvp = c[22] == "1", Mode = I(c[23]), Walk = I(c[24]) };
                    for (int s = 0; s < 6; s++) k.Stats[s] = I(c[10 + s]);
                    var at = c[25].Split(','); for (int e = 0; e < 10 && e < at.Length; e++) k.Attr[e] = I(at[e]);
                    foreach (var p in Items(c[26])) { var q = p.Split(new[] { ':' }, 3); if (q.Length >= 2) k.Skills.Add((I(q[0]), I(q[1]), q.Length > 2 ? q[2] : "")); }
                    foreach (var p in Items(c[27])) { var q = p.Split(':'); if (q.Length >= 2) k.Drops.Add((I(q[0]), I(q[1]), q.Length > 2 && q[2] == "1")); }
                    foreach (var p in Items(c[28])) { var q = p.Split(':'); if (q.Length >= 2) k.MvpDrops.Add((I(q[0]), I(q[1]), q.Length > 2 && q[2] == "1")); }
                    foreach (var p in Items(c[29]))
                    {
                        var q = p.Split(':');
                        if (q.Length >= 2 && !(id == 2401 || id == 2402 || id == 2403 || (q[0].StartsWith("int_land", StringComparison.OrdinalIgnoreCase) && k.Drops.Count == 0 && k.MvpDrops.Count == 0)))
                            k.Habitats.Add((q[0], I(q[1]), q.Length > 2 ? I(q[2]) : 0));
                    }
                    var pet = c[30].Split(':'); if (pet.Length >= 3) { k.TameItem = I(pet[0]); k.FoodItem = I(pet[1]); k.EggItem = I(pet[2]); }
                    if (c.Length > 31) k.Behavior = c[31];
                    if (c.Length > 32) k.Origin = c[32];
                    _creatures[id] = k;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Codex] creatures.tsv could not be read: " + e.Message); }
            // wave 28 (wcodex, 2026-10-16), the bracket hints (UI/CodexBook.Hints.cs): the places of the spawn maps and the brackets' members,
            // each a file of its own (an older pack has neither: no hints)
            int places = 0, members = 0;
            try
            {
                var pl = PackLines("codex/places.tsv");
                if (pl != null)
                    for (int i = 1; i < pl.Length; i++)
                    {
                        var c = pl[i].Split('\t'); if (c.Length < 2 || !int.TryParse(c[0], out int id) || !_creatures.TryGetValue(id, out var k)) continue;
                        if (IsExcluded(k)) continue;
                        foreach (var p in Items(c[1])) { var q = p.Split(':'); if (q.Length >= 4 && !q[0].StartsWith("int_land", StringComparison.OrdinalIgnoreCase)) k.Places.Add((q[0], q[1], q[2], q[3])); }
                        places++;
                    }
                var br = PackLines("codex/brackets.tsv");
                if (br != null)
                    for (int i = 1; i < br.Length; i++)
                    {
                        var c = br[i].Split('\t'); if (c.Length < 2 || !int.TryParse(c[0], out int id) || !int.TryParse(c[1], out int b)) continue;
                        if (id == 2401 || id == 2402 || id == 2403) continue;
                        _bracketOf[id] = b; members++;
                    }
            }
            catch (Exception e) { Debug.LogWarning("[Codex] places.tsv / brackets.tsv could not be read: " + e.Message); }
            CreaturesLoadMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Codex] {_creatures.Count} creatures read in {CreaturesLoadMs:0} ms (places of {places}, {members} bracket members)");
        }
        private static readonly Dictionary<int, int> _bracketOf = new Dictionary<int, int>();
        /// <summary>Wave 28 (wcodex): the Codex bounty bracket a creature counts in (the server's own members: the pack's codex/brackets.tsv,
        /// written with the server's script by client/tools/codex/build_bounty_script.py --members); 0: in none.</summary>
        public static int BracketOf(int id) { LoadCreatures(); return _bracketOf.TryGetValue(id, out var b) ? b : 0; }

        private static void LoadItems()
        {
            if (_items != null) return;
            _items = new Dictionary<int, Item>();
            var text = PackLines("codex/items.tsv"); if (text == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool head = true;
                foreach (var line in text)
                {
                    if (head) { head = false; continue; }
                    var c = line.Split('\t'); if (c.Length < 19 || !int.TryParse(c[0], out int id)) continue;
                    var it = new Item { Id = id, Name = c[1], Type = c[2], Sub = c[3], Slots = I(c[4]), Weight = I(c[5]), Atk = I(c[6]), Matk = I(c[7]), Def = I(c[8]), WeaponLevel = I(c[9]),
                                        ArmorLevel = I(c[10]), ReqLevel = I(c[11]), Refineable = c[12] == "1", Locations = (uint)I(c[13]), Jobs = c[14], Buy = I(c[15]), Sell = I(c[16]) };
                    foreach (var p in Items(c[17])) { var q = p.Split(':'); if (q.Length >= 2) it.DroppedBy.Add((I(q[0]), I(q[1]))); }
                    foreach (var p in Items(c[18])) { var q = p.Split(':'); if (q.Length >= 6) it.SoldBy.Add((q[0], q[1], I(q[2]), I(q[3]), I(q[4]), q[5])); }
                    _items[id] = it;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Codex] items.tsv could not be read: " + e.Message); }
            ItemsLoadMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Codex] {_items.Count} items read in {ItemsLoadMs:0} ms");
        }

        // ------------------------------------------------------------------ the items' lore (codex/item_lore.tsv)
        private static Dictionary<int, (string lore, string[] lines)> _lore;
        /// <summary>The export's English flavor text and effect lines for an item, checked by the converter against rAthena's numbers for that
        /// item (Gravity's item descriptions rewritten: they live in the text pack, never in the build); null when the pack has none.</summary>
        public static (string lore, string[] lines)? Lore(int id)
        {
            if (_lore == null)
            {
                _lore = new Dictionary<int, (string, string[])>();
                var text = PackLines("codex/item_lore.tsv");
                if (text != null)
                {
                    try
                    {
                        bool head = true;
                        foreach (var line in text)
                        {
                            if (head) { head = false; continue; }
                            var c = line.Split('\t'); if (c.Length < 3 || !int.TryParse(c[0], out int iid)) continue;
                            _lore[iid] = (c[1], c[2].Length > 0 ? c[2].Split(new[] { " | " }, StringSplitOptions.None) : new string[0]);
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("[Codex] item_lore.tsv could not be read: " + e.Message); }
                    Debug.Log($"[Codex] {_lore.Count} items' lore read");
                }
            }
            return _lore.TryGetValue(id, out var l) ? l : ((string, string[])?)null;
        }

        /// <summary>Waves 27-28 merge: a Codex file of the text pack as lines (<see cref="Loki.Data.TextPack.Text"/>: null when the pack has
        /// no such file or its SHA-256 differs from the manifest's; the loader logs why).</summary>
        private static string[] PackLines(string rel)
        {
            var t = Loki.Data.TextPack.Text(rel);
            if (t == null && !string.IsNullOrEmpty(rel))
            {
                string name = Path.GetFileNameWithoutExtension(rel);
                var asset = Resources.Load<TextAsset>("Codex/" + name);
                if (asset != null)
                {
                    t = asset.text;
                    Debug.Log($"[Codex] {rel} read from embedded Resources/Codex/{name} ({t.Length} characters)");
                }
            }
            if (t == null) return null;
            var lines = t.Split('\n'); for (int i = 0; i < lines.Length; i++) if (lines[i].EndsWith("\r")) lines[i] = lines[i].Substring(0, lines[i].Length - 1);
            return lines;
        }

        private static IEnumerable<string> Items(string s) { if (string.IsNullOrEmpty(s)) yield break; foreach (var p in s.Split(';')) if (p.Length > 0) yield return p; }
        private static int I(string s) => int.TryParse(s, out int v) ? v : 0;

        // ------------------------------------------------------------------ the look's time (FLAGGED numbers)
        /// <summary>Seconds of looking that write a creature down: <see cref="LookBase"/> + <see cref="LookPerLevel"/> per level (level capped at
        /// <see cref="LookLevelCap"/>), at most <see cref="LookMax"/>: Poring (1) 1.53 s, level 50 3.0 s, level 99 4.47 s, level 150 6.0 s.
        /// A BOSS (rAthena's Class Boss, MVPs included): <see cref="LookBossSeconds"/> whatever its level (design note, 2026-10-15: "Increase the time
        /// to scan a boss to 20s"), and attacking it shortens that (<see cref="HitOff"/>). The tutorial's ring fills in 1.0 s.</summary>
        public const float LookBase = 1.5f, LookPerLevel = 0.03f, LookBossSeconds = 20f, LookMax = 15f; public const int LookLevelCap = 200;
        public static float LookSeconds(int level, int bossType)
        {
            if (bossType >= 1) return LookBossSeconds;
            return Mathf.Min(LookMax, LookBase + LookPerLevel * Mathf.Clamp(level, 0, LookLevelCap));
        }
        /// <summary>design note: "make attacking it lower that amount" (FLAGGED rule): every hit the player or the player's own homunculus / mercenary /
        /// elemental / pet lands on a boss not yet in the book takes <see cref="HitSecondsPerHp"/> s times the hit's share of the boss's HP off
        /// its look (a hit of a tenth of its HP: 3 s); the fill then holds <see cref="HitHoldSeconds"/> s before it drains. Wave 28 (wcxp):
        /// design note, 2026-10-16, "The server enforces scan time": the bounty's server (loki_codex.txt S_Need) shortens a boss's 20 s by the
        /// same 30 s x the share of its HP it lost since it first saw the character near it; no script can count a character's hits
        /// (no buildin reads the monster's damage log), so the flat second per hit this rule had is gone: the ring never finishes
        /// before the server's clock.</summary>
        public const float HitSecondsPerHp = 30f, HitHoldSeconds = 5f;
        public static float HitOff(int damage, int maxHp) => HitSecondsPerHp * Mathf.Clamp01(damage / (float)Mathf.Max(1, maxHp));

        // ------------------------------------------------------------------ the scan's calm and its fights (FLAGGED numbers; design note, 2026-10-16)
        /// <summary>"Calmer rings": a ring starts only after the look has RESTED on a creature <see cref="LookDwell"/> s (the fill starts
        /// then: the scan takes its <see cref="LookSeconds"/> after the rest); a creature whose ring is already going goes on at once.
        /// "Scan during combat": a look that leaves a creature for less than <see cref="LookGrace"/> s empties nothing: out of a fight the
        /// ring waits, in a fight with that creature it goes on filling (the glances away a fight takes count as looking); after that it
        /// drains at twice its fill speed, as before. In a fight: the player (or the player's homunculus / mercenary / elemental / pet)
        /// hit or missed it, or it hit or missed the player, within <see cref="FightSeconds"/> s, or the player's attack is set on it; the
        /// ring then stands above its head (<see cref="FightRingScale"/> of its size round the body, its lower edge just over the head),
        /// moved there in <see cref="FightRingLiftSeconds"/> s.</summary>
        public const float LookDwell = 0.4f, LookGrace = 1.0f, FightSeconds = 5f, FightRingScale = 0.6f, FightRingLiftSeconds = 0.25f;

        // ------------------------------------------------------------------ the creatures a player can meet & canonical variant collapsing
        private static HashSet<int> _metable; private static List<int> _metableList;
        private static Dictionary<int, int> _canonicalOf;
        private static Dictionary<int, List<int>> _versionsOf;
        private static readonly Dictionary<int, int> ChampionBaseOverrides = new Dictionary<int, int>
        {
            { 2612, 1880 }, // Elusive Wood Goblin -> Leshij
            { 2626, 1882 }, // Elusive Vavayaga -> Baba-Yaga
            { 2669, 1628 }, // Furious Lamp Rey -> Sedora
            { 2704, 1402 }, // Swift Poison Toad -> Poisonous Toad
            { 2733, 2481 }, // Elusive Wraith Dead (Nig -> Nightmare Wraith Dead
            { 2734, 2480 }, // Furious Wanderer (Nightm -> Nightmare Wanderer
            { 2735, 1988 }, // Solid Nephentes -> Nepenthes
            { 2776, 2398 }, // Baby Poring Ringleader -> Little Poring
            { 2779, 1881 }, // Swift Les -> Lesavka
            { 2783, 2416 }, // Elusive Eremes -> Assassin Eremes
            { 2799, 2083 }, // Swift Uni-horn Scaraba -> One-Horned Scaraba
            { 2906, 2151 }, // Swift Rafflesia Arnoldi -> Rafflesia Alnoldi
        };
        private static readonly string[] ModifierPrefixes = { "Furious ", "Elusive ", "Swift ", "Solid " };
        private static readonly string[] RingleaderSuffixes = { " Ringleader", " Ringleade", " Ringlead", " Ringlea" };

        /// <summary>design note (2026-10-17 item 44): Poring in int_land (no drops) and tutorial immune Poporing (2401..2403) are excluded.</summary>
        public static bool IsExcluded(int id) => id == 2401 || id == 2402 || id == 2403;
        private static bool IsExcluded(Creature c) => c == null || IsExcluded(c.Id);

        private static string StripModifierName(int id, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (id >= 2603 && id <= 2913)
            {
                for (int i = 0; i < ModifierPrefixes.Length; i++)
                    if (name.StartsWith(ModifierPrefixes[i], StringComparison.OrdinalIgnoreCase))
                        return name.Substring(ModifierPrefixes[i].Length).Trim();
                for (int i = 0; i < RingleaderSuffixes.Length; i++)
                    if (name.EndsWith(RingleaderSuffixes[i], StringComparison.OrdinalIgnoreCase))
                        return name.Substring(0, name.Length - RingleaderSuffixes[i].Length).Trim();
            }
            if (name.StartsWith("Enriched ", StringComparison.OrdinalIgnoreCase))
                return name.Substring("Enriched ".Length).Trim();
            return null;
        }

        /// <summary>The creatures a player can meet (design note: "The bestiary should have all creatures listed"), by the Codex data's facts from
        /// rAthena (FLAGGED rule): a map spawn in a script the server loads, or a loaded script's monster command (quests, events,
        /// instances), or an item's summon group (Dead Branch ...), or a skill of a creature already in the set that summons it (followed
        /// until nothing more joins). Remote Island (int_land) zero-drop Poring (2401) is excluded.</summary>
        public static IReadOnlyList<int> MetableIds { get { LoadMetable(); return _metableList; } }
        public static bool Metable(int id) { if (IsExcluded(id)) return false; LoadMetable(); return _metable.Contains(id); }
        /// <summary>design note (2026-10-17 item 44): canonical base creature ID that duplicate-name creatures and modifier-prefix/suffix variants
        /// (Elusive Poring, Swift ..., Solid ..., Furious ..., ... Ringleader) collapse into. Distinct species (Baby Poring / Little Poring,
        /// Drops, Poporing, Marin, Santa Poring, etc.) keep their own canonical IDs.</summary>
        public static int CanonicalId(int id) { LoadMetable(); return _canonicalOf != null && _canonicalOf.TryGetValue(id, out int c) ? c : id; }
        /// <summary>All metable creature IDs that collapse into <paramref name="id"/>'s canonical creature entry (canonical ID first).</summary>
        public static IReadOnlyList<int> VersionsOf(int id)
        {
            LoadMetable();
            int c = CanonicalId(id);
            return _versionsOf != null && _versionsOf.TryGetValue(c, out var l) ? (IReadOnlyList<int>)l : new[] { c };
        }
        /// <summary>Human-readable label distinguishing a version in the creature page's Versions submenu.</summary>
        public static string VersionLabel(int id)
        {
            var c = GetCreature(id);
            if (c == null) return $"#{id}";
            int canon = CanonicalId(id);
            var baseCr = GetCreature(canon);
            if (id != canon && baseCr != null && string.Equals(c.Name, baseCr.Name, StringComparison.OrdinalIgnoreCase))
            {
                string tag = c.Habitats.Count > 0 ? c.Habitats[0].map : c.Origin.Contains("script") ? "Event / Script" : (c.Origin.Contains("mob:") || c.Origin.Contains("item:")) ? "Summoned" : $"Variant #{id}";
                return $"{c.Name} ({tag}, Lv {c.Level})";
            }
            return $"{c.Name} (Lv {c.Level})";
        }
        private static void LoadMetable()
        {
            if (_metable != null) return;
            LoadCreatures();
            _metable = new HashSet<int>();
            foreach (var c in _creatures.Values)
                if (!IsExcluded(c) && (c.Habitats.Count > 0 || c.Origin.Contains("script") || c.Origin.Contains("item:"))) _metable.Add(c.Id);
            for (bool more = true; more;)
            {
                more = false;
                foreach (var c in _creatures.Values)
                {
                    if (_metable.Contains(c.Id) || IsExcluded(c)) continue;
                    foreach (var part in c.Origin.Split('|'))
                        if (part.StartsWith("mob:"))
                            foreach (var s in part.Substring(4).Split(','))
                                if (int.TryParse(s, out int by) && _metable.Contains(by)) { _metable.Add(c.Id); more = true; goto next; }
                    next:;
                }
            }
            _metableList = new List<int>(_metable); _metableList.Sort();
            BuildCanonicalGroups();
            Debug.Log($"[Codex] creatures a player can meet: {_metable.Count} of {_creatures.Count}");
        }

        private static void BuildCanonicalGroups()
        {
            _canonicalOf = new Dictionary<int, int>();
            _versionsOf = new Dictionary<int, List<int>>();
            var exactByName = new Dictionary<string, List<Creature>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _creatures.Values)
            {
                if (IsExcluded(c)) continue;
                if (ChampionBaseOverrides.ContainsKey(c.Id) || StripModifierName(c.Id, c.Name) != null) continue;
                string key = c.Name.Trim();
                if (!exactByName.TryGetValue(key, out var list)) exactByName[key] = list = new List<Creature>();
                list.Add(c);
            }
            var canonByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in exactByName)
            {
                Creature best = null; int bestRank = int.MaxValue;
                foreach (var c in kv.Value)
                {
                    bool met = _metable.Contains(c.Id), hab = c.Habitats.Count > 0, drp = c.Drops.Count > 0 || c.MvpDrops.Count > 0;
                    int r = (met && hab && drp) ? 0 : (met && hab) ? 1 : met ? 2 : 3;
                    if (best == null || r < bestRank || (r == bestRank && c.Id < best.Id)) { best = c; bestRank = r; }
                }
                if (best != null) canonByName[kv.Key] = best.Id;
            }
            foreach (var c in _creatures.Values)
            {
                if (IsExcluded(c)) { _canonicalOf[c.Id] = c.Id; continue; }
                int canon = c.Id;
                if (ChampionBaseOverrides.TryGetValue(c.Id, out int ov) && _creatures.TryGetValue(ov, out var ovCr))
                    canon = canonByName.TryGetValue(ovCr.Name.Trim(), out int oc) ? oc : ov;
                else
                {
                    string stripped = StripModifierName(c.Id, c.Name);
                    if (stripped != null)
                    {
                        if (canonByName.TryGetValue(stripped, out int sc)) canon = sc;
                        else
                        {
                            int best = int.MaxValue;
                            foreach (var kv in canonByName)
                                if (kv.Key.StartsWith(stripped, StringComparison.OrdinalIgnoreCase) && kv.Value < best) best = kv.Value;
                            if (best < int.MaxValue) canon = best;
                        }
                    }
                    else if (canonByName.TryGetValue(c.Name.Trim(), out int ec)) canon = ec;
                }
                _canonicalOf[c.Id] = canon;
                if (_metable.Contains(c.Id) || c.Id == canon)
                {
                    if (!_versionsOf.TryGetValue(canon, out var vlist)) _versionsOf[canon] = vlist = new List<int>();
                    vlist.Add(c.Id);
                }
            }
            foreach (var kv in _versionsOf)
            {
                int canon = kv.Key;
                kv.Value.Sort((a, b) =>
                {
                    if (a == canon) return -1;
                    if (b == canon) return 1;
                    int la = _creatures.TryGetValue(a, out var ca) ? ca.Level : 0;
                    int lb = _creatures.TryGetValue(b, out var cb) ? cb.Level : 0;
                    int cmp = la.CompareTo(lb);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
            }
        }

        // ------------------------------------------------------------------ a set of ids as a short string (the account's settings carry it)
        /// <summary>Sorted ids as unsigned LEB128 deltas in base64: about 1.4 characters an id for close ids (Porings and their kin), 3-4 for
        /// far ones. The whole account's settings must stay under 60,000 bytes (Net/PresenceWire.cs MaxSettingsBytes).</summary>
        public static string Encode(IEnumerable<int> ids)
        {
            var l = new List<int>(); foreach (var i in ids) if (i > 0) l.Add(i);
            l.Sort();
            var b = new List<byte>(l.Count * 2); int prev = 0;
            foreach (var id in l)
            {
                if (id == prev) continue;
                uint d = (uint)(id - prev); prev = id;
                while (d >= 0x80) { b.Add((byte)(d | 0x80)); d >>= 7; }
                b.Add((byte)d);
            }
            return b.Count == 0 ? "" : Convert.ToBase64String(b.ToArray());
        }
        public static List<int> Decode(string s)
        {
            var l = new List<int>(); if (string.IsNullOrEmpty(s)) return l;
            byte[] b; try { b = Convert.FromBase64String(s); } catch (FormatException) { Debug.LogWarning("[Codex] a saved list could not be read"); return l; }
            int prev = 0, i = 0;
            while (i < b.Length)
            {
                uint d = 0; int shift = 0;
                while (i < b.Length) { byte x = b[i++]; d |= (uint)(x & 0x7f) << shift; shift += 7; if ((x & 0x80) == 0 || shift > 28) break; }
                prev += (int)d; l.Add(prev);
            }
            return l;
        }
    }
}
