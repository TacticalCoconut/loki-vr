using Loki.Game;


namespace Loki
{
    /// <summary>
    /// Client wave 26 (w26skill), the design's item 25 of wave 23: the skill tree's preview shows what a passive or a buff gains the player
    /// (Game/SkillGains.cs, drawn by UI/SkillDiorama.cs). Here: the character's numbers as the client knows them now, for that table.
    ///   The stats   STR..LUK base + bonus (Game/Progression.cs 0x0141), HIT, FLEE, CRIT (0x00b0: rAthena's battle_status hit, flee,
    ///               cri / 10: clif.cpp:3668-3675, :3724-3725), the weight limit (max_weight / 10: clif.cpp:3643-3644), Max HP / SP,
    ///               the base level, the job (the line's chain for a Thief's second job).
    ///   The skill   its learned level and the level a cast uses (the flat page's "Lv": Progression.CastLevel); whether a status icon
    ///               (EFST) is on the player now (Progression.Effects).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The character's numbers now, for Game/SkillGains.cs.</summary>
        private SkillGains.Who W26Who()
        {
            var w = new SkillGains.Who();
            for (int i = 0; i < 6; i++) w.Stats[i] = Prog.Base[i] + Prog.Bonus[i];
            w.Hit = Prog.Hit; w.Flee = Prog.Flee1; w.Crit = Prog.Crit;
            w.WeightLimit = Player.MaxWeight / 10; w.MaxHp = Player.MaxHp; w.MaxSp = Player.MaxSp; w.BaseLevel = Player.BaseLevel;
            w.Job = W3SkillJob();
            w.Learned = s => Prog.Skills.TryGetValue(s, out var k) ? k.Level : 0;
            w.CastLevel = s => Prog.Skills.TryGetValue(s, out var k) && k.Level > 0 ? Prog.CastLevel(k) : 0;
            w.IconOn = e => Prog.Effects.ContainsKey(e);
            // wfollow (2026-10-14): the job level, the trait stats (0x0141 SP_POW..SP_CRT base + bonus) and their numbers (0x00b0 225-230),
            // the hands (the worn items' rAthena weapon types: Inventory.WornAt EQP_HAND_R 0x02 / EQP_HAND_L 0x20, a two-handed weapon at both)
            w.JobLevel = Player.JobLevel;
            for (int i = 0; i < 6; i++) w.Traits[i] = Prog.TraitBase[i] + Prog.TraitBonus[i];
            w.PAtk = Prog.PAtk; w.SMatk = Prog.SMatk; w.Res = Prog.Res; w.MRes = Prog.MRes; w.HPlus = Prog.HPlus; w.CRate = Prog.CRate;
            var right = Inventory?.WornAt(0x02); var left = Inventory?.WornAt(0x20);
            w.Right = right != null ? System.Math.Max(0, Sprites.AppearanceDb.WeaponSubType(right.ItemId)) : 0;
            w.Left = -1; w.Shield = false;
            if (left != null && left != right)
            {
                if (left.Type == 5) w.Left = System.Math.Max(0, Sprites.AppearanceDb.WeaponSubType(left.ItemId)); // (IT_WEAPON: dual wield)
                else w.Shield = true;
            }
            // wfollow (item 4): the status page's ASPD (Progression.AspdDisplay: (2000 - amotion) / 10) and a mount (rAthena status.hpp:3031-3062:
            // OPTION_RIDING 0x20, OPTION_DRAGON1..5, OPTION_WUGRIDER 0x200000, OPTION_MADOGEAR 0x400000: their ASPD penalties are not modelled)
            w.Aspd = Prog.Aspd > 0 ? Prog.AspdDisplay : 0;
            w.Riding = Entities?.Self != null && (Entities.Self.Option & (0x20 | 0x80000 | 0x200000 | 0x400000 | 0x800000 | 0x1000000 | 0x2000000 | 0x4000000)) != 0;
            return w;
        }
    }
}
