using System;
using System.Collections.Generic;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 5 (w5ui): the skill window's planned points — plan several levels (each with the prerequisites it needs), see the
    /// points it costs, then apply it in one go. A port of roBrowserLegacy's plan-then-apply (a reimplementation, not the official
    /// client; the only source for this behaviour): UI/Components/SkillList/SkillRequirements.js stageSkillPlan (:88-137),
    /// calculatePlanCost (:69-77), validateSkillPlan (:139-159) and createSkillUpgradeOrder (:166-213), used by SkillListCommon.js
    /// onRememberChoice (:505-529: a click stages one level more), onApplyChoice (:1134-1150: every upgrade packet, prerequisites first)
    /// and onResetChoice (:1152-1183).
    /// What rB reads from its own tables comes from the client's data here: the prerequisites are <see cref="SkillTreeDb.Needs"/> (the
    /// official client's own skillinfo_f.lub rule; rB's resolveSkillRequirements, SkillRequirements.js:34-63, approximates it), MaxLv
    /// and Type are SKILL_INFO_LIST's (skills.tsv / skilltree.tsv S rows), the owned levels are the server's list (Progression.Skills).
    /// The plan is a Map in insertion order (createSkillUpgradeOrder walks plan.keys() in that order): an ordered list here.
    /// rAthena takes the packets one by one, in order: CZ_UPGRADE_SKILLLEVEL 0x0112 → pc_skillup (pc.cpp:9128-9178) raises one level when
    /// points are left and the skill is known, then pc_check_skilltree / status_calc_pc unlock what the new level allows before the next
    /// packet is read — so a prerequisite sent first opens its dependant for the packet after it.
    /// </summary>
    public sealed class SkillPlan
    {
        private readonly List<int> _order = new List<int>();
        private readonly Dictionary<int, int> _count = new Dictionary<int, int>();

        public bool Empty => _order.Count == 0;
        public IReadOnlyList<int> Skills => _order;
        /// <summary>The planned level of a skill in the plan (0 = not in the plan).</summary>
        public int Planned(int skill) => _count.TryGetValue(skill, out int c) ? c : 0;

        private SkillPlan Clone() { var p = new SkillPlan(); p._order.AddRange(_order); foreach (var kv in _count) p._count[kv.Key] = kv.Value; return p; }
        private void Set(int skill, int count) { if (!_count.ContainsKey(skill)) _order.Add(skill); _count[skill] = count; }

        /// <summary>calculatePlanCost: the levels above what is owned, summed (rB skips its quest entries, which it never adds).</summary>
        public int Cost(Func<int, int> owned)
        {
            int cost = 0;
            foreach (int s in _order) cost += Math.Max(0, _count[s] - owned(s));
            return cost;
        }

        /// <summary>The level a skill has with the plan applied (getPlannedLevel: the plan's or the owned one, whichever is higher).</summary>
        public int LevelWith(int skill, Func<int, int> owned) => Math.Max(Planned(skill), owned(skill));

        public enum Refusal { None, NoData, Quest, MaxLevel, Prerequisite, Points }

        /// <summary>
        /// stageSkillPlan: a candidate plan with one more level of <paramref name="skill"/> and every prerequisite raised to the level it
        /// needs (recursively); this plan is not changed. Null when the chain is invalid (a skill without data, a quest skill not owned that
        /// a prerequisite needs, a level above the maximum, a loop) or costs more than <paramref name="points"/>.
        /// </summary>
        public SkillPlan Stage(int skill, int job, Func<int, int> owned, int points, out Refusal why)
        {
            var cand = Clone(); var visiting = new HashSet<int>();
            Refusal fail = Refusal.None;
            bool StageOne(int s, int? requiredLevel)
            {
                // const info = skillInfo[currentSkillId]; if (!info || visiting.has(currentSkillId)) return false;
                if (!SkillDb.Has(s) || visiting.Contains(s)) { if (fail == Refusal.None) fail = Refusal.NoData; return false; }
                int own = owned(s);
                // const isUnownedQuest = info.Type === 'Quest' && ownedLevel <= 0; if (isUnownedQuest) return requiredLevel == null;
                if (SkillTreeDb.ExtraOf(s).Type == "Quest" && own <= 0) { if (fail == Refusal.None) fail = Refusal.Quest; return requiredLevel == null; }
                int max = SkillDb.Get(s).MaxLevel;
                int current = Math.Max(cand.Planned(s), own);
                int target = requiredLevel == null ? Math.Min(current + 1, max) : Math.Max(current, requiredLevel.Value);
                if (target > max) { if (fail == Refusal.None) fail = Refusal.MaxLevel; return false; }
                cand.Set(s, target);
                visiting.Add(s);
                foreach (var (req, lv) in SkillTreeDb.Needs(s, job))
                    if (!StageOne(req, lv)) { visiting.Remove(s); if (fail == Refusal.None) fail = Refusal.Prerequisite; return false; }
                visiting.Remove(s);
                return true;
            }
            if (!StageOne(skill, null)) { why = fail == Refusal.None ? Refusal.Prerequisite : fail; return null; }
            if (cand.Cost(owned) > points) { why = Refusal.Points; return null; }
            // rB keeps the candidate even when it adds nothing (an unowned quest skill, a skill already at its maximum): the caller is told
            why = cand.Cost(owned) == Cost(owned) ? (fail != Refusal.None ? fail : Refusal.MaxLevel) : Refusal.None;
            return cand;
        }

        /// <summary>validateSkillPlan: within the points; every planned level between the owned one and the maximum; every prerequisite met
        /// by the planned or owned levels.</summary>
        public bool Valid(int job, Func<int, int> owned, int points)
        {
            if (Cost(owned) > points) return false;
            foreach (int s in _order)
            {
                if (!SkillDb.Has(s)) return false;
                int own = owned(s), c = _count[s];
                if (c < own || c > SkillDb.Get(s).MaxLevel) return false;
                foreach (var (req, lv) in SkillTreeDb.Needs(s, job)) if (LevelWith(req, owned) < lv) return false;
            }
            return true;
        }

        /// <summary>createSkillUpgradeOrder: one skill id per upgrade packet, every prerequisite in the plan before its dependants, the plan's
        /// own order otherwise. Null when the plan is no longer valid against the server's levels (rB then resets it).</summary>
        public List<int> UpgradeOrder(int job, Func<int, int> owned, int points)
        {
            if (!Valid(job, owned, points)) return null;
            var order = new List<int>(); var visited = new HashSet<int>(); var visiting = new HashSet<int>();
            bool Visit(int s)
            {
                if (visited.Contains(s)) return true;
                if (visiting.Contains(s)) return false;
                if (!SkillDb.Has(s)) return false;
                visiting.Add(s);
                foreach (var (req, _) in SkillTreeDb.Needs(s, job))
                    if (_count.ContainsKey(req) && !Visit(req)) return false;
                visiting.Remove(s); visited.Add(s);
                int count = _count.TryGetValue(s, out int c) ? c : owned(s);
                for (int i = 0, n = Math.Max(0, count - owned(s)); i < n; i++) order.Add(s);
                return true;
            }
            foreach (int s in _order) if (!Visit(s)) return null;
            return order;
        }
    }
}
