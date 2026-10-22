using System.Collections.Generic;
using System.Text;
using Loki.Game;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): what a Horde card, weapon, passive or job is called and drawn with, ALWAYS from the player's own
    /// install through the client's existing tables (never from the repository): a weapon or passive by its Ragnarok skill id
    /// (Game/Progression.cs SkillDb: the GRF's skill names, Portuguese when the GRF has them; UI/WristMenu.cs SkillIcon: the
    /// GRF's skill icon), a passive shown as an item by its item id (GameDb, WristMenu.ItemIcon), a job by its job id
    /// (SkillTreeDb.JobName: the GRF's job names). The mode's own words (levels, stats, healing) go through Lang.
    /// Client wave 9 (h9flow): a weapon without a Ragnarok skill (the Novice's knife) is named and drawn by its signature ITEM
    /// (<see cref="WeaponItemId"/>: no more "Weapon N"); a card says what changes from level n to n + 1 (<see cref="CardDetail"/>);
    /// a job card carries the job's own body sprite (<see cref="JobPortrait"/>, the install's SPR / ACT through the same loaders
    /// the run characters use).
    /// </summary>
    public static class HordeText
    {
        /// <summary>The Ragnarok item a weapon without a skill is shown as (rAthena item id; 0 = none). h9content's tables name it
        /// (WeaponDef.ItemId once h9move's additive field lands: nv_knife → 1201, the Knife a Novice starts with, design notes);
        /// until then the one weapon without a skill in the tables, the starting knife, is resolved here by its key.</summary>
        public static int WeaponItemId(WeaponDef w)
        {
            if (w == null || w.SkillId > 0) return 0;
            if (w.ItemId > 0) return w.ItemId; // h9move 5a8420af: the tables' own value (h9content sets it)
            return w.Key == "nv_knife" ? 1201 : 0;
        }

        public static string Weapon(HordeContent c, int id)
        {
            var own = HordeCardText.WeaponName(c, id); if (own != null) return own; // client wave 17 (h17cards): a branch, an evolved form, a hidden weapon
            var w = c?.Weapon(id);
            if (w != null && w.SkillId > 0) return SkillDb.Get(w.SkillId).Name;
            int item = WeaponItemId(w);
            if (item > 0) { var it = GameDb.GetItem(item); if (it != null && !string.IsNullOrEmpty(it.Name)) return UiKit.StripTags(it.Name); }
            return Lang.F("Weapon {0}", id);
        }
        public static Sprite WeaponIcon(HordeContent c, int id)
        {
            var w = c?.Weapon(id);
            if (w != null && w.SkillId > 0) return WristMenu.SkillIcon(SkillDb.Get(w.SkillId));
            int item = WeaponItemId(w);
            if (item > 0) { var it = GameDb.GetItem(item); if (it != null) return WristMenu.ItemIcon(it); }
            return null;
        }
        public static string Passive(HordeContent c, int id)
        {
            var ownName = HordeCardText.PassiveName(c, id); if (ownName != null) return ownName; // client wave 17 (h17cards): Mystic Focus (Loki's own)
            var p = c?.Passive(id);
            if (p != null && p.SkillId > 0) return SkillDb.Get(p.SkillId).Name;
            if (p != null && p.ItemId > 0) return UiKit.StripTags(GameDb.GetItem(p.ItemId).Name);
            return Lang.F("Passive {0}", id);
        }
        public static Sprite PassiveIcon(HordeContent c, int id)
        {
            var p = c?.Passive(id);
            if (p != null && p.SkillId > 0) return WristMenu.SkillIcon(SkillDb.Get(p.SkillId));
            if (p != null && p.ItemId > 0) return WristMenu.ItemIcon(GameDb.GetItem(p.ItemId));
            return null;
        }
        public static string Job(int jobId, int sex = 1) => SkillTreeDb.JobName(jobId, sex);
        public static string Stat(int s) => s switch { 0 => Lang.T("STR"), 1 => Lang.T("AGI"), 2 => Lang.T("VIT"), 3 => Lang.T("INT"), 4 => Lang.T("DEX"), _ => Lang.T("LUK") };

        public static string CardTitle(HordeContent c, HordeCard k)
        {
            switch (k.Kind)
            {
                case HordeCardKind.NewWeapon: case HordeCardKind.WeaponLevel: case HordeCardKind.WeaponBranch: case HordeCardKind.WeaponTranscend: return Weapon(c, k.DefId); // client wave 17 (h17cards) + wave 18 (h18trans)
                case HordeCardKind.NewPassive: case HordeCardKind.PassiveLevel: case HordeCardKind.PassiveTranscend: return Passive(c, k.DefId);
                case HordeCardKind.Stat: return Lang.F("+{0} {1}", k.Level, Stat(k.DefId));
                case HordeCardKind.Job: return Job(k.DefId);
                case HordeCardKind.Heal: return Lang.T("Healing");
                default: return "?";
            }
        }

        // ---------------------------------------------------------------- the card texts (client wave 15, w15horde: the design's point 17)
        // design note (2026-10-08): "be clear and concise ... make it much more understandable what does what". Every card reads the same way:
        //   its NAME (CardTitle), a short tag (CardLine: new or level n → n+1),
        //   WHAT IT DOES in one or two plain sentences (made from the tables: kind, aim, effects, element; never the install's long
        //   skill description),
        //   the NUMBERS, one per line, only those that change at this level ("Damage 28 → 39").

        private const string DoesColor = "#ffffff", NumColor = "#ffd27a";
        private static HordeContent _cardsContent; // client wave 17 (h17cards): the content WeaponDoes' extra sentence reads (set by CardDoes)

        /// <summary>The card's tag under its name: clearly distinguishes a NEW ability from a LEVEL UP of an existing owned skill.</summary>
        public static string CardLine(HordeContent c, HordeCard k)
        {
            switch (k.Kind)
            {
                case HordeCardKind.NewWeapon:
                    return HordeSim.IsActiveWeapon(k.DefId)
                        ? Lang.T("★ NEW ACTIVE ABILITY (Laser + Trigger)")
                        : Lang.T("★ NEW ABILITY");
                case HordeCardKind.WeaponLevel:
                    return HordeSim.IsActiveWeapon(k.DefId)
                        ? Lang.F("▲ LEVEL UP (Owned Active) · Lv {0} → {1}", k.Level - 1, k.Level)
                        : Lang.F("▲ LEVEL UP (Owned) · Lv {0} → {1}", k.Level - 1, k.Level);
                case HordeCardKind.WeaponBranch: return Lang.F("★ EVOLVE ABILITY · Lv {0} → {1}", k.Level - 1, k.Level); // client wave 17 (h17cards): a branch (both are offered side by side)
                case HordeCardKind.WeaponTranscend: return Lang.F("✦ TRANSCEND ABILITY · Lv {0} → {1}", k.Level - 1, k.Level); // client wave 18 (h18trans): point 30
                case HordeCardKind.NewPassive: return Lang.T("★ NEW PASSIVE ABILITY");
                case HordeCardKind.PassiveLevel: return Lang.F("▲ LEVEL UP (Owned) · Lv {0} → {1}", k.Level - 1, k.Level);
                case HordeCardKind.PassiveTranscend: return Lang.F("✦ TRANSCEND PASSIVE · Lv {0} → {1}", k.Level - 1, k.Level); // client wave 18 (h18trans): point 30
                case HordeCardKind.Stat: return Lang.T("Stat Bonus");
                case HordeCardKind.Job: return Lang.T("★ NEW JOB CLASS");
                case HordeCardKind.Heal: return Lang.T("Instant Heal");
                default: return "";
            }
        }

        /// <summary>Clarifies RO skills whose names mention SP or Zeny and active trigger skills so players immediately know how they work in Horde mode.</summary>
        private static string CardClarification(HordeContent c, HordeCard k)
        {
            if (c == null) return "";
            if (k.Kind == HordeCardKind.NewWeapon || k.Kind == HordeCardKind.WeaponLevel || k.Kind == HordeCardKind.WeaponBranch || k.Kind == HordeCardKind.WeaponTranscend)
            {
                var w = c.Weapon(k.DefId);
                string note = w != null && w.SkillId == 42 ? Lang.T("(Costs no Zeny in Horde.)") : "";
                if (HordeSim.IsActiveWeapon(k.DefId))
                {
                    string act = Lang.T("[Active: Point laser at ground & pull Trigger to cast directly, or auto-casts.]");
                    note = note.Length > 0 ? act + " " + note : act;
                }
                return note;
            }
            if (k.Kind == HordeCardKind.NewPassive || k.Kind == HordeCardKind.PassiveLevel || k.Kind == HordeCardKind.PassiveTranscend)
            {
                var p = c.Passive(k.DefId);
                if (p == null) return "";
                return p.SkillId switch
                {
                    9 => Lang.T("(No SP in Horde: reduces spell wait time & boosts magic damage.)"),
                    74 => Lang.T("(No SP in Horde: speeds up all skill cooldowns & EXP gain.)"),
                    271 => Lang.T("(No SP in Horde: regenerates HP every second & reduces cooldowns.)"),
                    365 => Lang.T("(No SP in Horde: spell kills heal your HP & boost magic damage.)"),
                    37 => Lang.T("(In Horde: pulls pickups from farther away & boosts run EXP.)"),
                    38 => Lang.T("(In Horde: boosts all damage & run EXP gained.)"),
                    _ => "",
                };
            }
            return "";
        }

        /// <summary>What the card does (plain sentences) and then its numbers, one per line.</summary>
        public static string CardDetail(HordeContent c, HordeCard k)
        {
            if (c == null) return "";
            string does = CardDoes(c, k);
            string note = CardClarification(c, k);
            var nums = CardNumbers(c, k);
            var sb = new StringBuilder();
            if (does.Length > 0) sb.Append("<color=").Append(DoesColor).Append('>').Append(does).Append("</color>");
            if (note.Length > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#b8e6ff>").Append(note).Append("</color>");
            }
            if (nums.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("<color=").Append(NumColor).Append('>').Append(string.Join("\n", nums)).Append("</color>");
            }
            return sb.ToString();
        }

        /// <summary>One or two plain sentences: what the card does.</summary>
        public static string CardDoes(HordeContent c, HordeCard k)
        {
            HordeCardText.Use(c); _cardsContent = c; // client wave 17 (h17cards)
            switch (k.Kind)
            {
                case HordeCardKind.WeaponBranch: return HordeCardText.BranchDoes(c, k); // client wave 17 (h17cards)
                case HordeCardKind.WeaponTranscend: return HordeTransText.WeaponTransDoes(c, c.Weapon(k.DefId)); // client wave 18 (h18trans): point 30
                case HordeCardKind.NewWeapon: case HordeCardKind.WeaponLevel:
                {
                    var w = c.Weapon(k.DefId);
                    if (w == null || w.Levels.Length == 0) return "";
                    return WeaponDoes(w, w.Levels[Mathf.Clamp(k.Level, 1, w.Levels.Length) - 1]);
                }
                case HordeCardKind.PassiveTranscend: // client wave 18 (h18trans): point 30
                {
                    var p = c.Passive(k.DefId);
                    if (p == null) return "";
                    string ks = HordeTransText.PassiveKeystoneDoes(p.Trans);
                    string pd = p.Levels.Length > 0 ? ModsDoes(p.Levels[0]) : "";
                    string joined = HordePassivesText.Join(HordePassivesText.Does(c, p, 1), pd);
                    return joined.Length > 0 ? ks + " " + joined : ks;
                }
                case HordeCardKind.NewPassive: case HordeCardKind.PassiveLevel:
                {
                    var p = c.Passive(k.DefId);
                    if (p == null || p.Levels.Length == 0) return "";
                    string pd = ModsDoes(p.Levels[Mathf.Clamp(k.Level, 1, p.Levels.Length) - 1]);
                    if (pd.Length == 0) pd = HordeCardText.PassiveDoes(c, p, k.Level); // client wave 17 (h17cards): a passive with no modifier (Falcon Mastery)
                    return HordePassivesText.Join(HordePassivesText.Does(c, p, k.Level), pd); // client wave 17 (h17pass): the trigger first
                }
                case HordeCardKind.Stat:
                    foreach (var s in c.Stats) if ((int)s.Stat == k.DefId) return ModsDoes(s.PerPoint);
                    return "";
                case HordeCardKind.Job:
                {
                    var j = c.Job(k.DefId);
                    if (j == null) return "";
                    return j.SignatureWeapon > 0 && c.Weapon(j.SignatureWeapon) != null ? Lang.F("Gives {0} and opens new cards.", Weapon(c, j.SignatureWeapon)) : Lang.T("Opens new cards.");
                }
                case HordeCardKind.Heal: return Lang.F("Heals {0} % of your HP now.", k.Level);
                default: return "";
            }
        }

        /// <summary>The card's numbers, one line each: a new card's own, or only what changes ("a → b").</summary>
        public static List<string> CardNumbers(HordeContent c, HordeCard k)
        {
            var lines = new List<string>();
            HordeCardText.Use(c); // client wave 17 (h17cards)
            switch (k.Kind)
            {
                case HordeCardKind.WeaponBranch: // client wave 17 (h17cards): the weapon's level 2 -> the branch's level 3
                {
                    var b = c.Weapon(k.DefId); var w0 = b != null ? c.Weapon(b.BranchOf) : null;
                    if (b != null && w0 != null && b.Levels.Length >= 3 && w0.Levels.Length >= 2) WeaponNumbers(b, w0.Levels[1], b.Levels[2], lines);
                    break;
                }
                case HordeCardKind.WeaponTranscend: // client wave 18 (h18trans): point 30 — level 5 -> transcended level 6
                {
                    var b = c.Weapon(k.DefId); var w0 = b != null ? c.Weapon(b.TransOf) : null;
                    if (b != null && w0 != null && b.Levels.Length >= 1 && w0.Levels.Length >= 1) WeaponNumbers(b, w0.Levels[w0.Levels.Length - 1], b.Levels[0], lines);
                    break;
                }
                case HordeCardKind.NewWeapon:
                {
                    var w = c.Weapon(k.DefId);
                    if (w != null && w.Levels.Length > 0) WeaponNumbers(w, null, w.Levels[0], lines);
                    break;
                }
                case HordeCardKind.WeaponLevel:
                {
                    var w = c.Weapon(k.DefId);
                    if (w != null && w.Levels.Length > 0)
                    {
                        int to = Mathf.Clamp(k.Level, 1, w.Levels.Length), from = Mathf.Clamp(k.Level - 1, 1, w.Levels.Length);
                        WeaponNumbers(w, w.Levels[from - 1], w.Levels[to - 1], lines);
                    }
                    break;
                }
                case HordeCardKind.NewPassive:
                {
                    var p = c.Passive(k.DefId);
                    if (p != null && p.Levels.Length > 0) ModLines(p.Levels[0], null, lines);
                    if (p != null && lines.Count == 0) HordeCardText.PassiveNumbers(c, p, 1, 0, lines); // client wave 17 (h17cards)
                    HordePassivesText.Numbers(p, 0, 1, lines); // client wave 17 (h17pass): the trigger's numbers
                    break;
                }
                case HordeCardKind.PassiveLevel:
                {
                    var p = c.Passive(k.DefId);
                    if (p != null && p.Levels.Length > 0)
                    {
                        int to = Mathf.Clamp(k.Level, 1, p.Levels.Length), from = Mathf.Clamp(k.Level - 1, 1, p.Levels.Length);
                        ModLines(p.Levels[to - 1], p.Levels[from - 1], lines);
                        if (lines.Count == 0) HordeCardText.PassiveNumbers(c, p, to, from, lines); // client wave 17 (h17cards)
                        HordePassivesText.Numbers(p, from, to, lines); // client wave 17 (h17pass): the trigger's numbers that change
                    }
                    break;
                }
                case HordeCardKind.PassiveTranscend: // client wave 18 (h18trans): point 30 — level 5 -> transcended level 6
                {
                    var p = c.Passive(k.DefId); var p0 = p != null ? c.Passive(p.TransOf) : null;
                    if (p != null && p0 != null && p.Levels.Length >= 1 && p0.Levels.Length >= 1)
                    {
                        ModLines(p.Levels[0], p0.Levels[p0.Levels.Length - 1], lines);
                        if (lines.Count == 0) HordeCardText.PassiveNumbers(c, p, 1, p0.Levels.Length, lines);
                        HordePassivesText.Numbers(p, 0, 1, lines);
                    }
                    break;
                }
                case HordeCardKind.Stat:
                    foreach (var s in c.Stats)
                        if ((int)s.Stat == k.DefId && s.PerPoint != null)
                        {
                            var scaled = new ModDef[s.PerPoint.Length];
                            for (int i = 0; i < scaled.Length; i++) scaled[i] = s.PerPoint[i] == null ? null : new ModDef(s.PerPoint[i].Kind, s.PerPoint[i].Value * k.Level, s.PerPoint[i].Arg);
                            ModLines(scaled, null, lines);
                        }
                    break;
                case HordeCardKind.Job:
                {
                    var j = c.Job(k.DefId);
                    if (j == null) break;
                    if (j.Mods != null) ModLines(j.Mods, null, lines);
                    if (j.StatBonus != null) for (int i = 0; i < 6 && i < j.StatBonus.Length; i++) if (j.StatBonus[i] != 0) lines.Add(Stat(i) + " " + Signed(j.StatBonus[i]));
                    break;
                }
            }
            return lines;
        }

        // ---- what a weapon does: its kind and aim, then its effect (or its element)

        private static string WeaponDoes(WeaponDef w, WeaponLevel l)
        {
            if (w.TransOf > 0) return HordeTransText.WeaponTransDoes(_cardsContent, w); // client wave 18 (h18trans): point 30
            string first = HordeCardText.WeaponFirst(w, l); // client wave 17 (h17cards): the new kinds and rules
            if (first == null)
            switch (w.Kind)
            {
                case WeaponKind.MeleeArc:
                    first = w.Target == Targeting.Facing ? Lang.T("Strikes in front of you, where you walk.")
                          : w.Target == Targeting.Strongest ? Lang.T("Strikes the strongest enemy close to you.")
                          : Lang.T("Strikes the nearest enemy and those beside it.");
                    break;
                case WeaponKind.Bolt:
                    first = w.Target == Targeting.Strongest ? Lang.T("Shoots the strongest enemy.")
                          : w.Target == Targeting.Random ? Lang.T("Shoots a random enemy.")
                          : w.Target == Targeting.Farthest ? Lang.T("Shoots the farthest enemy.")
                          : w.Target == Targeting.Facing ? Lang.T("Shoots where you walk.")
                          : Lang.T("Shoots the nearest enemy.");
                    if (l.Area > 0) first += " " + Lang.T("Bursts on hit.");
                    break;
                case WeaponKind.Nova: first = Lang.T("Hits every enemy around you."); break;
                case WeaponKind.AreaAtTarget:
                    first = w.Target == Targeting.Densest ? Lang.T("Strikes where enemies crowd.")
                          : w.Target == Targeting.Random ? Lang.T("Strikes random enemies.")
                          : w.Target == Targeting.Strongest ? Lang.T("Strikes the strongest enemy and those around it.")
                          : Lang.T("Strikes the nearest enemy and those around it.");
                    break;
                case WeaponKind.GroundZone:
                {
                    bool heals = l.Heal > 0 || l.HealPct > 0, hurts = l.Damage > 0;
                    first = (w.Zone & ZoneFlags.BlocksEnemies) != 0 ? Lang.T("Conjures a linear wall that blocks enemy movement.")
                          : heals && hurts ? Lang.T("Creates a ground zone that damages enemies and heals allies.")
                          : heals ? Lang.T("Creates a ground zone that heals you and allies inside it.")
                          : Lang.T("Creates a ground zone that continuously damages enemies inside it.");
                    first += " " + ((w.Zone & ZoneFlags.FollowsOwner) != 0 ? Lang.T("It moves with you.")
                          : w.Target == Targeting.Self ? Lang.T("Appears beneath you.")
                          : w.Target == Targeting.Densest ? Lang.T("Spawns where enemies crowd.")
                          : Lang.T("Spawns at a nearby enemy."));
                    break;
                }
                case WeaponKind.Orbiter: first = Lang.T("Orbiting spheres circle you and strike enemies on contact."); break;
                case WeaponKind.Chain: first = Lang.T("Strikes a target and arcs to nearby enemies."); break;
                case WeaponKind.Aura:
                {
                    bool heals = l.Heal > 0 || l.HealPct > 0, buffs = l.Buffs != null && l.Buffs.Length > 0, ally = w.Target == Targeting.LowestHpAlly;
                    first = l.Damage > 0 && !heals && !buffs ? Lang.T("Pulses damage to all enemies around you.")
                          : heals ? (ally ? Lang.T("Heals the most injured ally, or you.") : l.Area > 0 ? Lang.T("Heals you and nearby allies.") : Lang.T("Restores your HP."))
                          : ally ? Lang.T("Shields the most injured ally, or you.")
                          : l.Area > 0 ? Lang.T("Grants stat buffs to you and nearby allies.") : Lang.T("Grants stat buffs to you.");
                    break;
                }
                case WeaponKind.Summon: first = Lang.T("Summons allies that fight alongside you."); break;
                case WeaponKind.Trap: first = Lang.T("Places traps that detonate when enemies step near."); break;
                default: first = ""; break;
            }
            string second = "";
            if (l.Effects != null)
                foreach (var e in l.Effects)
                {
                    if (e == null) continue;
                    bool maybe = e.Chance < 99.5f;
                    second = e.Kind switch
                    {
                        EffectKind.Stun => maybe ? Lang.T("Can stun.") : Lang.T("Stuns."),
                        EffectKind.Freeze => maybe ? Lang.T("Can freeze.") : Lang.T("Freezes."),
                        EffectKind.Slow => maybe ? Lang.T("Can slow enemies.") : Lang.T("Slows enemies."),
                        EffectKind.Dot => w.Element == Element.Poison ? Lang.T("Poisons.") : w.Element == Element.Fire ? Lang.T("Burns.") : Lang.T("Deals damage over time."),
                        EffectKind.Knockback => Lang.T("Pushes enemies back."),
                        _ => "",
                    };
                    if (second.Length > 0) break;
                }
            if (second.Length == 0 && w.Element != Element.Neutral && (l.Damage > 0 || w.Kind == WeaponKind.Trap)) second = Lang.F("{0} damage.", ElementName(w.Element));
            string extra = HordeCardText.WeaponExtra(_cardsContent, w, l); // client wave 17 (h17cards): an evolved form's power, Turn Undead
            if (extra.Length > 0) second = second.Length > 0 ? second + " " + extra : extra;
            return second.Length > 0 ? first + " " + second : first;
        }

        // ---- a weapon's numbers

        private static string Num(float v) => v.ToString(Mathf.Abs(v - Mathf.Round(v)) < 0.0005f ? "0" : "0.##", Lang.Culture);
        private static string Signed(float v) => (v >= 0 ? "+" : "−") + Num(Mathf.Abs(v));
        private static string Secs(float v) => Lang.F("{0} s", Num(v));
        private static string Cells(float v) => Lang.F("{0} cells", Num(v));

        /// <summary>The word for a weapon's Count (shots, orbs, helpers ...).</summary>
        private static string CountName(WeaponKind k) => k switch
        {
            WeaponKind.Bolt => Lang.T("Shots"), WeaponKind.Orbiter => Lang.T("Orbs"), WeaponKind.Summon => Lang.T("Helpers"), WeaponKind.Chain => Lang.T("Jumps"),
            WeaponKind.AreaAtTarget => Lang.T("Strikes"), WeaponKind.Trap => Lang.T("Traps"), WeaponKind.GroundZone => Lang.T("Zones"), WeaponKind.Aura => Lang.T("Targets"),
            _ => Lang.T("Hits"),
        };

        /// <summary>A weapon level's numbers: all those of its kind when <paramref name="a"/> is null (a new weapon), else only those
        /// that change from <paramref name="a"/> to <paramref name="b"/>, as "a → b".</summary>
        private static void WeaponNumbers(WeaponDef w, WeaponLevel a, WeaponLevel b, List<string> lines)
        {
            if (HordeCardText.WeaponNumbers(w, a, b, lines)) return; // client wave 17 (h17cards): the new kinds' own numbers
            bool aura = w.Kind == WeaponKind.Aura, buffAura = aura && b.Buffs != null && b.Buffs.Length > 0;
            void Line(string label, float x, float y, System.Func<float, string> f, bool show)
            {
                if (a == null) { if (show) lines.Add(label + " " + f(y)); }
                else if (Mathf.Abs(x - y) > 0.0005f) lines.Add(label + " " + f(x) + " → " + f(y));
            }
            float ax(System.Func<WeaponLevel, float> g) => a != null ? g(a) : 0f;
            Line(Lang.T("Damage"), ax(l => l.Damage), b.Damage, Num, b.Damage > 0);
            Line(Lang.T("Heals"), ax(l => l.Heal), b.Heal, v => Lang.F("{0} HP", Num(v)), b.Heal > 0);
            Line(Lang.T("Heals"), ax(l => l.HealPct), b.HealPct, v => Lang.F("{0} % of max HP", Num(v)), b.HealPct > 0);
            Line(Lang.T("Every"), ax(l => l.Cooldown), b.Cooldown, Secs, b.Cooldown > 0 && w.Kind != WeaponKind.Orbiter);
            Line(CountName(w.Kind), ax(l => l.Count), b.Count, Num, b.Count > 1 || w.Kind == WeaponKind.Bolt || w.Kind == WeaponKind.Orbiter || w.Kind == WeaponKind.Summon);
            Line(Lang.T("Goes through"), ax(l => l.Pierce), b.Pierce, v => Lang.F("{0} enemies", Num(v)), b.Pierce > 0);
            Line(Lang.T("Shot speed"), ax(l => l.Speed), b.Speed, Num, false);
            Line(Lang.T("Reach"), ax(l => l.Range), b.Range, Cells, b.Range > 0 && (w.Kind == WeaponKind.MeleeArc || w.Kind == WeaponKind.Bolt || w.Kind == WeaponKind.Chain));
            Line(Lang.T("Size"), ax(l => l.Area), b.Area, Cells, b.Area > 0 && w.Kind != WeaponKind.MeleeArc);
            Line(Lang.T("Width"), ax(l => l.Arc), b.Arc, v => Num(v) + "°", b.Arc > 0 && w.Kind == WeaponKind.MeleeArc);
            Line(Lang.T("Lasts"), ax(l => l.Duration), b.Duration, Secs, b.Duration > 0 && (!aura || buffAura));
            Line(Lang.T("Lands after"), ax(l => l.Delay), b.Delay, Secs, false);
            int ne = Mathf.Max(a?.Effects?.Length ?? 0, b.Effects?.Length ?? 0);
            for (int i = 0; i < ne; i++)
            {
                var ea = a?.Effects != null && i < a.Effects.Length ? a.Effects[i] : null; var eb = b.Effects != null && i < b.Effects.Length ? b.Effects[i] : null;
                if (eb == null) continue;
                if (ea == null) lines.Add(a == null ? EffectLine(eb) : Lang.F("New: {0}", EffectLine(eb)));
                else if (Mathf.Abs(ea.Chance - eb.Chance) > 0.0005f || Mathf.Abs(ea.Power - eb.Power) > 0.0005f || Mathf.Abs(ea.Duration - eb.Duration) > 0.0005f) lines.Add(EffectLine(ea) + " → " + EffectLine(eb));
            }
            // an aura's boosts: for you (and the allies it reaches)
            if (b.Buffs != null) ModLines(b.Buffs, a?.Buffs, lines);
        }

        /// <summary>An effect in plain words: "Stun 1 s, 10 % of hits".</summary>
        private static string EffectLine(EffectDef e)
        {
            string what = e.Kind switch
            {
                EffectKind.Slow => Lang.F("Slow {0} %", Num(e.Power)),
                EffectKind.Freeze => Lang.T("Freeze"),
                EffectKind.Stun => Lang.T("Stun"),
                EffectKind.Dot => Lang.F("{0} damage a second", Num(e.Tick > 0 ? e.Power / e.Tick : e.Power)),
                EffectKind.Knockback => Lang.F("Push back {0}", Cells(e.Power)),
                _ => e.Kind.ToString(),
            };
            if (e.Kind != EffectKind.Knockback && e.Duration > 0) what += " " + Secs(e.Duration);
            return e.Chance < 99.5f ? Lang.F("{0}, {1} % of hits", what, Num(e.Chance)) : what;
        }

        // ---- modifiers (passives, stats, jobs, an aura's boosts)

        /// <summary>Modifiers one per line, "Max HP +8 %"; with <paramref name="from"/> (the level before) only those that change, "+8 % → +16 %".</summary>
        private static void ModLines(ModDef[] mods, ModDef[] from, List<string> lines)
        {
            if (mods == null) return;
            foreach (var m in mods)
            {
                if (m == null) continue;
                ModDef prev = null;
                if (from != null) foreach (var f in from) if (f != null && f.Kind == m.Kind && f.Arg == m.Arg) prev = f;
                if (from != null && prev != null && Mathf.Abs(prev.Value - m.Value) <= 0.0005f) continue; // unchanged at this level
                var ownLine = HordeCardText.ModLine(m, prev); if (ownLine != null) { if (ownLine.Length > 0) lines.Add(ownLine); continue; } // client wave 17 (h17cards)
                lines.Add(ModName(m) + " " + (prev != null ? ModValue(prev) + " → " : "") + ModValue(m));
            }
        }

        /// <summary>What modifiers do, in up to two plain sentences ("More max HP. Dodge more attacks.").</summary>
        private static string ModsDoes(ModDef[] mods)
        {
            if (mods == null) return "";
            var said = new List<string>();
            foreach (var m in mods)
            {
                if (m == null || Mathf.Abs(m.Value) < 0.0005f) continue;
                string s = ModDoes(m);
                if (s.Length > 0 && !said.Contains(s)) said.Add(s);
                if (said.Count == 2) break;
            }
            return string.Join(" ", said);
        }

        private static string ModDoes(ModDef m)
        {
            bool up = m.Value > 0;
            switch (m.Kind)
            {
                case Mod.MaxHp: case Mod.MaxHpPct: return up ? Lang.T("Increases max HP.") : Lang.T("Reduces max HP.");
                case Mod.Regen: return up ? Lang.T("Regenerates HP every second.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.MoveSpeedPct: return up ? Lang.T("Increases move speed.") : Lang.T("Reduces move speed.");
                case Mod.DamagePct: return up ? Lang.T("Boosts all damage dealt.") : Lang.T("Reduces damage dealt.");
                case Mod.MeleeDamagePct: return up ? Lang.T("Boosts melee damage.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.RangedDamagePct: return up ? Lang.T("Boosts ranged damage.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.MagicDamagePct: return up ? Lang.T("Boosts magic damage.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.CooldownPct: return up ? Lang.T("Increases skill cooldowns.") : Lang.T("Reduces skill cooldowns.");
                case Mod.AreaPct: return up ? Lang.T("Increases attack area.") : Lang.T("Reduces attack area.");
                case Mod.ProjectileCount: return up ? Lang.T("Fires additional projectiles.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.ProjectileSpeedPct: return up ? Lang.T("Increases projectile speed.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.DurationPct: return up ? Lang.T("Increases effect duration.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.PickupRadiusPct: return up ? Lang.T("Pulls in pickups from farther away.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.CritChance: return up ? Lang.T("Increases critical hit chance.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.CritDamagePct: return up ? Lang.T("Increases critical hit damage.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.ExpGainPct: return up ? Lang.T("Increases run EXP gained.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.Defence: return up ? Lang.T("Reduces incoming damage.") : Lang.T("Increases incoming damage.");
                case Mod.DamageTakenPct: return up ? Lang.T("Increases damage taken.") : Lang.T("Reduces damage taken.");
                case Mod.Dodge: return up ? Lang.T("Increases dodge chance.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.HealPowerPct: return up ? Lang.T("Increases healing power.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.ReviveSpeedPct: return up ? Lang.T("Revives fallen allies faster.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.Str: case Mod.Agi: case Mod.Vit: case Mod.Int: case Mod.Dex: case Mod.Luk: return up ? Lang.F("Raises {0}.", ModName(m)) : Lang.F("Lowers {0}.", ModName(m));
                case Mod.DamageVsRacePct: return up ? Lang.F("Deals bonus damage to {0}.", RaceName((Race)m.Arg)) : Lang.F("Lowers {0}.", ModName(m));
                case Mod.DamageVsElementPct: return up ? Lang.F("Deals bonus damage to {0} enemies.", ElementName((Element)m.Arg)) : Lang.F("Lowers {0}.", ModName(m));
                case Mod.ElementDamagePct: return up ? Lang.F("Boosts {0} element damage.", ElementName((Element)m.Arg)) : Lang.F("Lowers {0}.", ModName(m));
                case Mod.DamageVsBossPct: return up ? Lang.T("Deals bonus damage to bosses.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.EffectChancePct: return up ? Lang.T("Increases status effect chance.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.KnockbackPct: return up ? Lang.T("Increases knockback distance.") : Lang.F("Lowers {0}.", ModName(m));
                case Mod.HpOnKill: return up ? Lang.T("Restores HP on every kill.") : Lang.F("Lowers {0}.", ModName(m));
                default: return HordeCardText.ModDoes(m) ?? ""; // client wave 17 (h17cards)
            }
        }

        /// <summary>Client wave 17 (h17cards): a modifier kind shown in percent (the old ones, and the new ones: HordeCardText.IsPct).</summary>
        public static bool IsPctMod(Mod k) => ModValue(new ModDef(k, 0)).EndsWith(" %");

        private static string ModValue(ModDef m)
        {
            bool pct = HordeCardText.IsPct(m.Kind) || m.Kind switch // client wave 17 (h17cards)
            {
                Mod.MaxHpPct or Mod.MoveSpeedPct or Mod.DamagePct or Mod.MeleeDamagePct or Mod.RangedDamagePct or Mod.MagicDamagePct or Mod.CooldownPct or Mod.AreaPct
                or Mod.ProjectileSpeedPct or Mod.DurationPct or Mod.PickupRadiusPct or Mod.CritChance or Mod.CritDamagePct or Mod.ExpGainPct or Mod.DamageTakenPct or Mod.Dodge
                or Mod.HealPowerPct or Mod.ReviveSpeedPct or Mod.DamageVsRacePct or Mod.DamageVsElementPct or Mod.ElementDamagePct or Mod.DamageVsBossPct or Mod.EffectChancePct or Mod.KnockbackPct => true,
                _ => false,
            };
            return Signed(m.Value) + (pct ? " %" : "");
        }

        /// <summary>The mode's own name of a modifier kind, in plain words (Lang; the race / element named where the kind takes one).</summary>
        public static string ModName(ModDef m)
        {
            switch (m.Kind)
            {
                case Mod.MaxHp: case Mod.MaxHpPct: return Lang.T("Max HP");
                case Mod.Regen: return Lang.T("HP per second");
                case Mod.MoveSpeedPct: return Lang.T("Move speed");
                case Mod.DamagePct: return Lang.T("Damage");
                case Mod.MeleeDamagePct: return Lang.T("Melee damage");
                case Mod.RangedDamagePct: return Lang.T("Ranged damage");
                case Mod.MagicDamagePct: return Lang.T("Magic damage");
                case Mod.CooldownPct: return Lang.T("Time between attacks");
                case Mod.AreaPct: return Lang.T("Attack size");
                case Mod.ProjectileCount: return Lang.T("Extra shots");
                case Mod.ProjectileSpeedPct: return Lang.T("Shot speed");
                case Mod.DurationPct: return Lang.T("Effect time");
                case Mod.PickupRadiusPct: return Lang.T("Pickup reach");
                case Mod.CritChance: return Lang.T("Critical chance");
                case Mod.CritDamagePct: return Lang.T("Critical damage");
                case Mod.ExpGainPct: return Lang.T("Run EXP");
                case Mod.Defence: return Lang.T("Defence");
                case Mod.DamageTakenPct: return Lang.T("Damage taken");
                case Mod.Dodge: return Lang.T("Dodge");
                case Mod.HealPowerPct: return Lang.T("Healing");
                case Mod.ReviveSpeedPct: return Lang.T("Getting up");
                case Mod.Str: return Lang.T("STR"); case Mod.Agi: return Lang.T("AGI"); case Mod.Vit: return Lang.T("VIT");
                case Mod.Int: return Lang.T("INT"); case Mod.Dex: return Lang.T("DEX"); case Mod.Luk: return Lang.T("LUK");
                case Mod.DamageVsRacePct: return Lang.F("Damage vs {0}", RaceName((Race)m.Arg));
                case Mod.DamageVsElementPct: return Lang.F("Damage vs {0}", ElementName((Element)m.Arg));
                case Mod.ElementDamagePct: return Lang.F("{0} attacks", ElementName((Element)m.Arg));
                case Mod.DamageVsBossPct: return Lang.T("Damage vs bosses");
                case Mod.EffectChancePct: return Lang.T("Effect chance");
                case Mod.KnockbackPct: return Lang.T("Push back");
                case Mod.HpOnKill: return Lang.T("HP per kill");
                default: return HordeCardText.ModName(m) ?? m.Kind.ToString(); // client wave 17 (h17cards)
            }
        }

        /// <summary>rAthena's e_element names, the mode's own words (the install's property names are not in a table the client reads).</summary>
        public static string ElementName(Element e) => e switch
        {
            Element.Neutral => Lang.T("~elem~Neutral"), Element.Water => Lang.T("~elem~Water"), Element.Earth => Lang.T("~elem~Earth"), Element.Fire => Lang.T("~elem~Fire"),
            Element.Wind => Lang.T("~elem~Wind"), Element.Poison => Lang.T("~elem~Poison"), Element.Holy => Lang.T("~elem~Holy"), Element.Dark => Lang.T("~elem~Dark"),
            Element.Ghost => Lang.T("~elem~Ghost"), Element.Undead => Lang.T("~elem~Undead"), _ => e.ToString(),
        };
        public static string RaceName(Race r) => r switch
        {
            Race.Formless => Lang.T("~race~Formless"), Race.Undead => Lang.T("~race~Undead"), Race.Brute => Lang.T("~race~Brute"), Race.Plant => Lang.T("~race~Plant"),
            Race.Insect => Lang.T("~race~Insect"), Race.Fish => Lang.T("~race~Fish"), Race.Demon => Lang.T("~race~Demon"), Race.Demihuman => Lang.T("~race~Demihuman"),
            Race.Angel => Lang.T("~race~Angel"), Race.Dragon => Lang.T("~race~Dragon"), _ => r.ToString(),
        };

        public static Sprite CardIcon(HordeContent c, HordeCard k)
        {
            switch (k.Kind)
            {
                case HordeCardKind.NewWeapon: case HordeCardKind.WeaponLevel: case HordeCardKind.WeaponBranch: case HordeCardKind.WeaponTranscend: return WeaponIcon(c, k.DefId); // client wave 17 (h17cards) + wave 18 (h18trans)
                case HordeCardKind.NewPassive: case HordeCardKind.PassiveLevel: case HordeCardKind.PassiveTranscend: return PassiveIcon(c, k.DefId);
                case HordeCardKind.Job: { var j = c?.Job(k.DefId); return j != null && j.SignatureWeapon > 0 ? WeaponIcon(c, j.SignatureWeapon) : null; }
                default: return null;
            }
        }

        // ---------------------------------------------------------------- the job's own sprite (h9flow)

        private static readonly Dictionary<(int, int, int, int), Sprite> _portraits = new Dictionary<(int, int, int, int), Sprite>();
        /// <summary>Portraits drawn so far, and the layers of the last one ("Body Head": the smoke).</summary>
        public static int PortraitsMade { get; private set; }
        public static string LastPortraitLayers { get; private set; } = "";

        /// <summary>The job standing and facing the viewer (idle action, direction 0, first frame) as a UI sprite for a job card. Client
        /// wave 15 (w15hfx), point 18 of the design's wave 14 list ("all of the different preview images are headless"): wave 9 composited the
        /// BODY sprite alone; now the picture is made as the game draws a character: a hidden unit (Sprites/Unit.cs Create with the
        /// player's sex, hair style and hair colour, PlayerAppearance's paths and palettes) and its every layer (body, then the head at
        /// the body's anchor) in one composite (ActorSprite.CompositeFrame). Null when the install has no such sprite.</summary>
        public static Sprite JobPortrait(int job, int sex, int hair = 1, int hairColor = 0)
        {
            sex = sex == 0 ? 0 : 1;
            var key = (job, sex, hair, hairColor);
            if (_portraits.TryGetValue(key, out var cached)) return cached;
            Sprite s = null; Unit u = null;
            try
            {
                var boot = LokiBootstrap.Instance; var assets = boot != null ? boot.Assets : null;
                if (assets != null)
                {
                    var look = new PlayerLook { Job = job, Sex = sex, Hair = hair, HairColor = hairColor };
                    u = Unit.Create(boot.transform, null, 0xFC000000u | (uint)(job & 0xFFFF), job, sex, hair, assets, look);
                    u.gameObject.SetActive(false); // never drawn: only its composite is made
                    var gpu = u.Sprite != null ? u.Sprite.CompositeFrame((int)ActorSprite.PlayerAction.Idle, 0, 0) : null;
                    if (gpu != null && gpu.Texture != null) s = UiKit.ToSprite(gpu.Texture);
                    LastPortraitLayers = u.Sprite != null ? u.Sprite.LayerSummary() : "";
                    PortraitsMade++;
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[Horde] job portrait {job}: {e.Message}"); }
            finally { if (u != null) Object.Destroy(u.gameObject); }
            _portraits[key] = s;
            return s;
        }

        public static string Clock(float seconds) { int s = Mathf.Max(0, Mathf.FloorToInt(seconds)); return $"{s / 60:00}:{s % 60:00}"; }
    }
}
