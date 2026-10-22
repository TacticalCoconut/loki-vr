using System.Collections.Generic;
using Loki.Game;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17cards): the card texts of design 1 / point 15 (the new weapons and passives, the branches at level 3, the
    /// evolutions, the kinds Dash / Mark / Parry / Pet and the new modifiers), in wave 15's style (the design's point 17: the name, what it
    /// does in one or two plain lines, the numbers that change, "12 → 16"). HordeText calls in by marked hooks; everything here is
    /// short and plain (item 17), en + pt-BR (Resources/Lang/pt-BR.txt, block "client wave 17 (h17cards)").
    /// </summary>
    public static class HordeCardText
    {
        private static HordeContent _c;
        /// <summary>The tables' names shown through Lang (the branches' names: HordeTables.BranchName; the one evolved form named by Loki).</summary>
        private static readonly string[] TableNames =
        {
            Lang.N("Barrage"), Lang.N("Lance"), Lang.N("Sweep"), Lang.N("Crush"), Lang.N("Wave"), Lang.N("Burst"), Lang.N("Rain"), Lang.N("Impact"),
            Lang.N("Spread"), Lang.N("Strong"), Lang.N("Swarm"), Lang.N("Heavy"), Lang.N("Fork"), Lang.N("Wide"), Lang.N("Pack"), Lang.N("Field"),
            Lang.N("Big"), Lang.N("Long"), Lang.N("Deep"), Lang.N("Many"), Lang.N("Swift"), Lang.N("Meteor Bolt"),
        };
        /// <summary>The content the modifier names read (a weapon named by a modifier): set by HordeText's card entries.</summary>
        public static void Use(HordeContent c) { if (c != null) _c = c; }

        private static string Num(float v) => v.ToString(Mathf.Abs(v - Mathf.Round(v)) < 0.0005f ? "0" : "0.##", Lang.Culture);
        private static string Signed(float v) => (v >= 0 ? "+" : "−") + Num(Mathf.Abs(v));
        private static string Secs(float v) => Lang.F("{0} s", Num(v));
        private static string Cells(float v) => Lang.F("{0} cells", Num(v));
        private static string Pct(float v) => Num(v) + " %";

        // ------------------------------------------------------------------ names

        /// <summary>A branch ("Fire Bolt · Lance"), an evolved form, a transcended form, a hidden weapon's own name; null: HordeText's usual name.</summary>
        public static string WeaponName(HordeContent c, int id)
        {
            var tr = HordeTransText.WeaponName(c, id); if (tr != null) return tr; // client wave 18 (h18trans): point 30
            var w = c?.Weapon(id);
            if (w == null) return null;
            if (w.BranchOf > 0) return HordeText.Weapon(c, w.BranchOf) + " · " + Lang.T(w.BranchName);
            if (w.EvolveOf > 0)
            {
                string n = !string.IsNullOrEmpty(w.EvolvedName) ? Lang.T(w.EvolvedName) : w.SkillId > 0 ? SkillDb.Get(w.SkillId).Name : HordeText.Weapon(c, w.EvolveOf);
                return Lang.F("{0} · evolved", n);
            }
            if (w.Hidden && w.Kind == WeaponKind.Pet) return Lang.T("Falcon");
            if (w.Hidden && w.Kind == WeaponKind.Bolt) return Lang.T("Copied shot");
            return null;
        }

        /// <summary>A passive with neither a skill nor an item (Mystic Focus, Loki's own), or a transcended passive: its Lang name; null: HordeText's usual.</summary>
        public static string PassiveName(HordeContent c, int id)
        {
            var tr = HordeTransText.PassiveName(c, id); if (tr != null) return tr; // client wave 18 (h18trans): point 30
            var p = c?.Passive(id);
            if (p == null || p.SkillId > 0 || p.ItemId > 0) return null;
            return p.Key == "mg_mysticfocus" ? Lang.T("Mystic Focus") : null;
        }

        // ------------------------------------------------------------------ what a weapon does

        /// <summary>The first sentence of a weapon of the new kinds or rules; null: HordeText's sentence of its kind.</summary>
        public static string WeaponFirst(WeaponDef w, WeaponLevel l)
        {
            if (w.RandomOf != null && w.RandomOf.Length > 0) return w.CastOnHit ? Lang.T("Your attacks have a chance to cast a free bolt.") : Lang.T("Automatically casts a random spell at nearby enemies.");
            switch (w.Kind)
            {
                case WeaponKind.Dash:
                    if (w.DashOn == DashTrigger.Crowded) return Lang.T("When surrounded by enemies, blinks you to safety.");
                    if (w.DashOn == DashTrigger.WhenHit) return Lang.T("When hit, you dash away.");
                    return w.Target == Targeting.Farthest ? Lang.T("Dashes through a line of enemies, striking all in your path.")
                         : w.Target == Targeting.Strongest ? Lang.T("Dashes to the strongest enemy and strikes.")
                         : Lang.T("Dashes into the nearest enemy and strikes.");
                case WeaponKind.Mark:
                    if (l.Taunt) return Lang.T("Taunts nearby enemies to rush you while taking extra damage.");
                    if (l.WeakenPct >= 99.5f) return Lang.T("Blinds enemies in front of you so their attacks miss.");
                    if (l.WeakenPct > 0) return Lang.T("Weakens the strongest enemy so it deals less damage.");
                    if (l.MarkHits > 0 && l.MarkPct >= 99.5f) return Lang.T("Marks the strongest enemy so the next hit against it deals double damage.");
                    if (l.Effects != null && l.Area <= 0) return Lang.T("Binds the strongest enemy in place.");
                    if (l.Area > 0 && w.Target == Targeting.Self) return Lang.T("Marks nearby enemies to take extra damage from all attacks.");
                    return Lang.T("Marks the strongest enemy to take extra damage from all attacks.");
                case WeaponKind.Parry:
                    if ((w.Parry & ParryFlags.Returns) != 0) return Lang.T("Catches incoming enemy projectiles and fires them back.");
                    if ((w.Parry & ParryFlags.Shots) != 0) return l.Buffs != null && l.Buffs.Length > 0 ? Lang.T("Blocks enemy shots from the front while slowing your walk.") : Lang.T("Blocks incoming enemy projectiles from the front.");
                    return Lang.T("Forms a guard ring that blocks enemy melee hits.");
                case WeaponKind.Pet:
                    if (w.Hidden) return Lang.T("A falcon companion that dives at enemies automatically.");
                    return w.Pet == PetMode.Healer ? Lang.T("Summons a companion that periodically heals you and allies.")
                         : w.Pet == PetMode.Guard ? Lang.T("Summons a guardian companion that reduces damage you take.")
                         : w.Pet == PetMode.Caster ? Lang.T("Summons a companion that fires bolts at enemies.")
                         : Lang.T("Summons a companion that fights alongside you.");
                case WeaponKind.Aura:
                    if (l.RevivePct > 0) return Lang.T("Immediately revives a fallen ally in range.");
                    // client wave 18 (h18feel), the design's point 37: the Yggdrasil Berry saves a life (HordeSim.Souls.cs); its heal shows in the numbers
                    if (w.ItemId > 0 && w.ItemId == (_c?.Souls != null ? _c.Souls.SaveItemId : SoulRulesDef.YggdrasilBerry))
                    { var sr = _c?.Souls ?? new SoulRulesDef(); return Lang.F("Saves you once: full heal, clears nearby enemies, {0}s invulnerable · party: again after {1} s", Mathf.RoundToInt(sr.SaveSafeSeconds), Mathf.RoundToInt(sr.SaveCooldownSeconds)); }
                    if (l.BelowPct > 0) return Lang.T("Automatically restores a large amount of HP when your HP drops low.");
                    if (l.Damage <= 0 && l.Heal <= 0 && l.HealPct <= 0 && (l.Buffs == null || l.Buffs.Length == 0) && l.Effects != null) return Lang.T("Pulses around you to apply status effects to nearby enemies.");
                    return null;
                case WeaponKind.GroundZone:
                    if ((w.Zone & ZoneFlags.BlocksShots) != 0) return Lang.T("Creates a barrier zone around you that blocks enemy projectiles.");
                    return null;
                case WeaponKind.Summon:
                    return w.SummonBursts ? Lang.T("Summons spheres that home in on enemies and detonate.") : null;
                case WeaponKind.MeleeArc:
                    return w.HitsBySize ? Lang.T("Strikes the strongest enemy, dealing extra hits to larger foes.") : null;
            }
            return null;
        }

        /// <summary>A sentence added after a weapon's own (an evolved form's power, Turn Undead); "" none.</summary>
        public static string WeaponExtra(HordeContent c, WeaponDef w, WeaponLevel l)
        {
            if (HordeSim.IsSlide(w) && l.Damage > 0) return SlideLine(l); // client wave 18 (h18rules), point 27
            if (l.ExecutePct > 0) return Lang.T("Can instantly destroy Undead enemies.");
            if (w.EvolveOf > 0)
            {
                var b = c?.Weapon(w.EvolveOf);
                switch (b != null ? b.Key : "")
                {
                    case "sm_bash": return Lang.T("Every hit triggers an area shockwave around the target.");
                    case "mg_firebolt": return Lang.T("Bolts explode into fiery blasts on impact.");
                    case "ac_double": return Lang.T("Arrows pierce through all enemies in a line.");
                    case "al_holylight": return Lang.T("Unleashes a holy blast around each target hit.");
                    case "mc_mammonite": return Lang.T("Deals massive damage and stuns the target.");
                    case "tf_poison": return Lang.T("Spreads poison to nearby enemies on hit.");
                }
            }
            return "";
        }

        /// <summary>A branch card's sentence: what the branch changes.</summary>
        public static string BranchDoes(HordeContent c, HordeCard k)
        {
            var b = c?.Weapon(k.DefId);
            if (b == null || b.BranchOf <= 0) return "";
            bool one = b.Branch == 1;
            var l = b.Levels.Length >= 3 ? b.Levels[2] : null;
            if (b.RandomOf != null && b.RandomOf.Length > 0)
                return one ? Lang.T("Casts spells more frequently.") : b.CastOnHit ? Lang.T("Higher chance to trigger on hit.") : Lang.T("Casts higher-level spells for more damage.");
            switch (b.Kind)
            {
                case WeaponKind.Bolt: return one ? Lang.T("Fires 3 extra projectiles per volley at lower damage per shot.") : Lang.T("Fires one large heavy bolt that pierces through enemies.");
                case WeaponKind.MeleeArc: return one ? Lang.T("Swings across a wider arc with longer reach.") : Lang.T("Delivers a much heavier swing at a slower rate.");
                case WeaponKind.Nova: return one ? Lang.T("Expands the blast radius around you.") : Lang.T("Increases blast damage against nearby enemies.");
                case WeaponKind.AreaAtTarget: return one ? (l != null && l.Damage > 0 ? Lang.T("Calls down an extra strike per cast.") : Lang.T("Strikes an additional target.")) : Lang.T("Larger, higher-damage area strikes.");
                case WeaponKind.GroundZone: return one ? Lang.T("Creates larger zones that last longer.") : Lang.T("Increases zone damage per tick.");
                case WeaponKind.Orbiter: return one ? Lang.T("Adds more orbiting spheres.") : Lang.T("Larger orbiting spheres that deal more damage.");
                case WeaponKind.Chain: return one ? Lang.T("Chains to 2 additional enemies per cast.") : Lang.T("Deals higher chain damage and jumps farther.");
                case WeaponKind.Aura: return one ? Lang.T("Expands aura radius.") : Lang.T("Increases aura potency.");
                case WeaponKind.Summon: return one ? Lang.T("Summons an extra helper.") : Lang.T("Stronger, faster-attacking helpers.");
                case WeaponKind.Trap: return one ? Lang.T("Places more traps with a shorter cooldown.") : Lang.T("Larger traps with higher blast damage.");
                case WeaponKind.Dash: return one ? Lang.T("Dashes farther across the arena.") : l != null && l.Damage > 0 ? Lang.T("Deals more dash damage.") : Lang.T("Shorter dash cooldown.");
                case WeaponKind.Mark: return one ? Lang.T("Marks additional enemies at once.") : Lang.T("Stronger mark bonus that lasts longer.");
                case WeaponKind.Parry: return one ? Lang.T("Holds 1 additional guard charge.") : Lang.T("Recharges guard charges faster.");
                case WeaponKind.Pet: return one ? Lang.T("Increases companion power.") : Lang.T("Companion acts more frequently.");
                default: return "";
            }
        }

        // ------------------------------------------------------------------ a weapon's numbers (the new kinds)

        /// <summary>The numbers of a weapon of the new kinds (and a random caster), all of them (<paramref name="a"/> null: a new card) or
        /// only those that change ("a → b"). False: HordeText's numbers of its kind.</summary>
        public static bool WeaponNumbers(WeaponDef w, WeaponLevel a, WeaponLevel b, List<string> lines)
        {
            bool random = w.RandomOf != null && w.RandomOf.Length > 0;
            if (!random && w.Kind != WeaponKind.Dash && w.Kind != WeaponKind.Mark && w.Kind != WeaponKind.Parry && w.Kind != WeaponKind.Pet && b.RevivePct <= 0) return false;
            void Line(string label, float x, float y, System.Func<float, string> f, bool show)
            {
                if (a == null) { if (show) lines.Add(label + " " + f(y)); }
                else if (Mathf.Abs(x - y) > 0.0005f) lines.Add(label + " " + f(x) + " → " + f(y));
            }
            float A(System.Func<WeaponLevel, float> g) => a != null ? g(a) : 0f;
            if (random)
            {
                if (w.CastOnHit) Line(Lang.T("Chance"), A(l => l.ProcPct), b.ProcPct, Pct, true);
                Line(w.CastOnHit ? Lang.T("At most every") : Lang.T("Every"), A(l => l.Cooldown), b.Cooldown, Secs, true);
                Line(Lang.T("Spell level"), A(l => Mathf.Max(0, l.Count - 1)), Mathf.Max(0, b.Count - 1), Signed, b.Count > 1);
                return true;
            }
            if (b.RevivePct > 0)
            {
                Line(Lang.T("Gets up with"), A(l => l.RevivePct), b.RevivePct, v => Lang.F("{0} % of max HP", Num(v)), true);
                Line(Lang.T("Every"), A(l => l.Cooldown), b.Cooldown, Secs, true);
                Line(Lang.T("Reach"), A(l => l.Range), b.Range, Cells, true);
                return true;
            }
            switch (w.Kind)
            {
                case WeaponKind.Dash:
                    Line(Lang.T("Damage"), A(l => l.Damage), b.Damage, Num, b.Damage > 0);
                    Line(Lang.T("Size"), A(l => l.Area), b.Area, Cells, b.Area > 0 && HordeSim.IsSlide(w)); // client wave 18 (h18rules): Back Slide's trail areas
                    Line(Lang.T("Distance"), A(l => l.Range), b.Range, Cells, true);
                    if (w.DashOn == DashTrigger.Crowded) Line(Lang.T("Enemies touching"), A(l => l.Count), b.Count, Num, true);
                    Line(Lang.T("Every"), A(l => l.Cooldown), b.Cooldown, Secs, true);
                    break;
                case WeaponKind.Mark:
                    Line(Lang.T("Damage"), A(l => l.Damage), b.Damage, Num, b.Damage > 0);
                    Line(Lang.T("Damage taken"), A(l => l.MarkPct), b.MarkPct, v => "+" + Pct(v), b.MarkPct > 0);
                    Line(Lang.T("Damage dealt"), A(l => l.WeakenPct), b.WeakenPct, v => "−" + Pct(v), b.WeakenPct > 0);
                    Line(Lang.T("Hits"), A(l => l.MarkHits), b.MarkHits, Num, b.MarkHits > 0);
                    Line(Lang.T("Size"), A(l => l.Area), b.Area, Cells, b.Area > 0);
                    Line(Lang.T("Lasts"), A(l => l.Duration), b.Duration, Secs, b.Duration > 0);
                    Line(Lang.T("Every"), A(l => l.Cooldown), b.Cooldown, Secs, true);
                    break;
                case WeaponKind.Parry:
                    Line(Lang.T("Charges"), A(l => l.Count), b.Count, Num, true);
                    Line(Lang.T("Lasts"), A(l => l.Duration), b.Duration, Secs, b.Duration > 0);
                    Line(b.Duration > 0 ? Lang.T("Every") : Lang.T("A charge back every"), A(l => l.Cooldown), b.Cooldown, Secs, true);
                    Line(Lang.T("Throw-back damage"), A(l => l.Damage), b.Damage, Num, b.Damage > 0 && (w.Parry & ParryFlags.Returns) != 0);
                    BuffLines(a?.Buffs, b.Buffs, lines);
                    break;
                case WeaponKind.Pet:
                    Line(Lang.T("Damage"), A(l => l.Damage), b.Damage, Num, b.Damage > 0);
                    Line(Lang.T("Heals"), A(l => l.Heal), b.Heal, v => Lang.F("{0} HP", Num(v)), b.Heal > 0);
                    Line(Lang.T("Heals"), A(l => l.HealPct), b.HealPct, v => Lang.F("{0} % of max HP", Num(v)), b.HealPct > 0);
                    Line(Lang.T("Every"), A(l => l.Tick), b.Tick, Secs, true);
                    Line(Lang.T("Reach"), A(l => l.Range), b.Range, Cells, w.Pet == PetMode.Caster || w.Pet == PetMode.Healer);
                    BuffLines(a?.Buffs, b.Buffs, lines);
                    break;
            }
            return true;
        }

        private static void BuffLines(ModDef[] from, ModDef[] to, List<string> lines)
        {
            if (to == null) return;
            foreach (var m in to)
            {
                if (m == null) continue;
                ModDef prev = null; if (from != null) foreach (var f in from) if (f != null && f.Kind == m.Kind && f.Arg == m.Arg) prev = f;
                if (from != null && prev != null && Mathf.Abs(prev.Value - m.Value) <= 0.0005f) continue;
                string v(ModDef d) => Signed(d.Value) + (HordeText.IsPctMod(d.Kind) ? " %" : "");
                lines.Add(HordeText.ModName(m) + " " + (prev != null ? v(prev) + " → " : "") + v(m));
            }
        }

        /// <summary>Client wave 18 (h18rules), the design's point 27, in the design's style ("4% Heal/s for 15 seconds"): what Back Slide adds after
        /// its dash, with the level's numbers (its trail's delay, the unseen time, the speed).</summary>
        public static string SlideLine(WeaponLevel l)
        {
            float speed = 0f; if (l.Buffs != null) foreach (var m in l.Buffs) if (m != null && m.Kind == Mod.MoveSpeedPct) speed += m.Value;
            return Lang.F("Trail explodes after {0} s · {1} s invisible, invulnerable, +{2}% speed", Num(l.Delay), Num(l.Duration), Num(speed));
        }

        // ------------------------------------------------------------------ passives

        // Merge h17int (client wave 17): the ten trigger passives' placeholder texts (Auto Berserk, Mystic Focus, Making Arrow, Hiding,
        // Auto Counter, Reflect Shield, Soul Drain, Blade Stop, Cloaking, Gangster's Paradise) are dropped: h17pass's HordePassivesText
        // writes their trigger sentence and numbers (the merge brief). Falcon Mastery's text stays: it gives a weapon.

        /// <summary>What a passive with no modifiers does (Falcon Mastery: it gives a weapon); "" none.</summary>
        public static string PassiveDoes(HordeContent c, PassiveDef p, int level)
        {
            if (p == null) return "";
            if (p.GivesWeapon > 0) return Lang.T("A falcon flies with you and strikes.");
            return "";
        }

        /// <summary>The numbers of a passive with no modifiers, at <paramref name="level"/> (and from <paramref name="from"/>, 0 = a new card).</summary>
        public static void PassiveNumbers(HordeContent c, PassiveDef p, int level, int from, List<string> lines)
        {
            if (p == null) return;
            if (p.GivesWeapon > 0)
            {
                var w = c?.Weapon(p.GivesWeapon);
                if (w == null || w.Levels.Length == 0) return;
                var b = w.Levels[Mathf.Clamp(level, 1, w.Levels.Length) - 1]; var a = from > 0 ? w.Levels[Mathf.Clamp(from, 1, w.Levels.Length) - 1] : null;
                WeaponNumbers(w, a, b, lines);
            }
        }

        /// <summary>Wave 28 (item 52): formats the bonus stat attached to a card (e.g. "+3 STR (Melee damage +7.5 %)").</summary>
        public static string BonusStatLine(HordeContent c, HordeCard k)
        {
            if (!k.HasBonusStat) return "";
            int st = k.BonusStat - 1;
            var statLines = c != null ? HordeText.CardNumbers(c, new HordeCard { Kind = HordeCardKind.Stat, DefId = st, Level = k.BonusStatPoints }) : null;
            if (statLines != null && statLines.Count > 0)
                return Lang.F("+{0} {1} ({2})", k.BonusStatPoints, HordeText.Stat(st), string.Join(", ", statLines));
            return Lang.F("+{0} {1}", k.BonusStatPoints, HordeText.Stat(st));
        }

        // ------------------------------------------------------------------ the new modifiers

        public static bool IsPct(Mod k) => k switch
        {
            Mod.DamageVsSizePct or Mod.DamageTakenVsRacePct or Mod.DamageTakenVsElementPct or Mod.StunOnHitPct or Mod.ArcPct or Mod.ChestBonusPct or Mod.MovingMeleePct
            or Mod.PoisonOnHitPct or Mod.OffhandPct or Mod.TrapPct or Mod.EchoPct or Mod.HpDrainPct or Mod.WeaponDamagePct or Mod.WeaponCooldownPct or Mod.MagicVsBossPct
            or Mod.ProvidencePct or Mod.ChestDropPct or Mod.ExpPickupHealPct or Mod.StealPct or Mod.CopyShotPct => true,
            _ => false,
        };

        private static string SizeName(int s) => s == 0 ? Lang.T("~size~Small") : s == 2 ? Lang.T("~size~Large") : Lang.T("~size~Medium");
        private static string WeaponOf(int id) { var n = _c != null ? HordeText.Weapon(_c, id) : null; return string.IsNullOrEmpty(n) ? "#" + id : n; }

        /// <summary>A new modifier's name; null: not one of the new ones.</summary>
        public static string ModName(ModDef m)
        {
            switch (m.Kind)
            {
                case Mod.DamageVsSizePct: return Lang.F("Damage vs {0}", SizeName(m.Arg));
                case Mod.DamageTakenVsRacePct: return Lang.F("Damage from {0}", HordeText.RaceName((Race)m.Arg));
                case Mod.DamageTakenVsElementPct: return Lang.F("Damage from {0}", HordeText.ElementName((Element)m.Arg));
                case Mod.StunOnHitPct: return m.Arg == 1 ? Lang.T("Melee stun chance") : Lang.T("Stun chance");
                case Mod.ArcPct: return Lang.T("Swing width");
                case Mod.MovingRegen: return Lang.T("HP regen while moving");
                case Mod.EndowElement: return Lang.T("Element");
                case Mod.ChestBonusPct: return Lang.T("Extra chest upgrade chance");
                case Mod.MovingMeleePct: return Lang.T("Melee damage while moving");
                case Mod.PoisonOnHitPct: return Lang.T("Poison chance");
                case Mod.OffhandPct: return Lang.T("Double strike damage");
                case Mod.TrapPct: return m.Arg == 1 ? Lang.T("Trap blast radius") : Lang.T("Trap arm speed");
                case Mod.EchoPct: return Lang.T("Double pulse chance");
                case Mod.HpDrainPct: return Lang.T("HP drained per second");
                case Mod.WeaponDamagePct: return Lang.F("{0} damage", WeaponOf(m.Arg));
                case Mod.WeaponCooldownPct: return Lang.F("{0} cooldown", WeaponOf(m.Arg));
                case Mod.MagicVsBossPct: return Lang.T("Magic vs bosses");
                case Mod.ProvidencePct: return Lang.T("Damage from Demons and Undead");
                case Mod.ChestDropPct: return m.Arg == 1 ? Lang.T("Extra boss chest chance") : Lang.T("Chest drop per kill");
                case Mod.ExpPickupHealPct: return Lang.T("Heal per EXP pickup");
                case Mod.StealPct: return Lang.T("Bonus EXP on hit chance");
                case Mod.CancelStrikeCells: return Lang.T("Cancel reach");
                case Mod.CancelStrikeEvery: return Lang.T("Cancels once every");
                case Mod.CopyShotPct: return Lang.T("Copied shot damage");
                default: return null;
            }
        }

        /// <summary>A new modifier's whole number line (for the kinds a plain "Name +v" does not fit); "" = no line; null = the usual line.</summary>
        public static string ModLine(ModDef m, ModDef prev)
        {
            switch (m.Kind)
            {
                case Mod.EndowElement: return ""; // the sentence says it ("Your Neutral weapons become Fire.")
                case Mod.CancelStrikeCells: return Lang.T("Cancel reach") + " " + (prev != null ? Cells(prev.Value) + " → " : "") + Cells(m.Value);
                case Mod.CancelStrikeEvery: return Lang.T("Cancels once every") + " " + (prev != null ? Secs(prev.Value) + " → " : "") + Secs(m.Value);
                default: return null;
            }
        }

        /// <summary>What a new modifier does, one plain sentence; null: not one of the new ones.</summary>
        public static string ModDoes(ModDef m)
        {
            switch (m.Kind)
            {
                case Mod.DamageVsSizePct: return Lang.F("Deals bonus damage to {0} enemies.", SizeName(m.Arg));
                case Mod.DamageTakenVsRacePct: return Lang.F("Reduces damage taken from {0} enemies.", HordeText.RaceName((Race)m.Arg));
                case Mod.DamageTakenVsElementPct: return Lang.F("Reduces damage taken from {0} enemies.", HordeText.ElementName((Element)m.Arg));
                case Mod.StunOnHitPct: return m.Arg == 1 ? Lang.T("Melee hits have a chance to stun.") : Lang.T("Hits have a chance to stun.");
                case Mod.ArcPct: return Lang.T("Widens your melee swing arc.");
                case Mod.MovingRegen: return Lang.T("Regenerates HP while moving.");
                case Mod.EndowElement:
                    return m.Value >= 2.5f ? Lang.F("Your heals imbue an ally's weapons with {0} for 10 s.", HordeText.ElementName((Element)m.Arg))
                         : m.Value >= 1.5f ? Lang.F("Imbues your Neutral ranged weapons with {0}.", HordeText.ElementName((Element)m.Arg))
                         : Lang.F("Imbues your Neutral weapons with {0}.", HordeText.ElementName((Element)m.Arg));
                case Mod.ChestBonusPct: return Lang.T("Chests have a chance to grant an extra upgrade.");
                case Mod.MovingMeleePct: return Lang.T("Boosts melee damage while moving.");
                case Mod.PoisonOnHitPct: return Lang.T("Hits have a chance to poison enemies.");
                case Mod.OffhandPct: return Lang.T("Melee attacks strike a second time.");
                case Mod.TrapPct: return m.Arg == 1 ? Lang.T("Increases trap blast radius.") : Lang.T("Traps arm faster.");
                case Mod.EchoPct: return Lang.T("Auras have a chance to pulse twice.");
                case Mod.HpDrainPct: return Lang.T("Drains HP each second while active.");
                case Mod.WeaponDamagePct: return Lang.F("Boosts {0} damage.", WeaponOf(m.Arg));
                case Mod.WeaponCooldownPct: return Lang.F("Reduces {0} cooldown.", WeaponOf(m.Arg));
                case Mod.MagicVsBossPct: return Lang.T("Magic deals bonus damage to bosses.");
                case Mod.ProvidencePct: return Lang.T("Reduces damage taken from Demons and Undead.");
                case Mod.ChestDropPct: return m.Arg == 1 ? Lang.T("Elites and bosses can drop an extra chest.") : Lang.T("Kills have a chance to drop a chest.");
                case Mod.ExpPickupHealPct: return Lang.T("Collecting EXP gems restores HP.");
                case Mod.StealPct: return Lang.T("Hits have a chance to drop bonus EXP.");
                case Mod.CancelStrikeCells: return Lang.T("Periodically cancels enemy area attacks near you.");
                case Mod.CancelStrikeEvery: return "";
                case Mod.CopyShotPct: return Lang.T("Fires back the last projectile that hit you.");
                default: return null;
            }
        }
    }
}
