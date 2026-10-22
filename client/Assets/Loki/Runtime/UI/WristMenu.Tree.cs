using System.Collections.Generic;
using System.Text;
using Loki.Game;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: the wrist menu's side of the 3D skill trees (UI/SkillTree3D.cs: one tree per job of
    /// the character's line, around the player or one at a time). "A tree in front of the player replaces the flat Skills page as the
    /// default (the flat page stays as a fallback, a Setting)":
    ///   WHEN      Settings → Controls → Menus "Skill tree: 3D / Flat" (VR/Comfort.cs SkillTree3D, default 3D). With 3D, wherever the
    ///             Skills page would be shown (<see cref="Show"/>(Tab.Skills): the ring's Skills, the board's side tab, the classic
    ///             menu's Skills tab, a command) the TREE opens in front of the player instead (<see cref="TreeOpens"/>): the ring and
    ///             the board close (the one-menu-at-a-time rule: UI/MenuNav.cs, the tree is a menu of its own, "SkillTree"); the
    ///             classic wrist menu closes as for a window opened from it. The ring's Skills always opens the tree (not the group's
    ///             last page: FLAGGED). The tree's plate has "Quick bar" (the Skills board on its Quick bar page) and "Flat page" (the
    ///             flat page this once), each with the back arrow to the tree.
    ///   WHAT      everything the flat page does is done by the flat page's own code: the model (Game/SkillTree.cs), a skill's card
    ///             (<see cref="TreeCard"/>: the detail panel's texts), its buttons (<see cref="TreeAct"/>: SkillDetailAction's "up" =
    ///             CZ_UPGRADE_SKILLLEVEL, "pin" = the quick-slot picker with the pad / flick / wand layouts, "lv", "use"), the planned
    ///             points (SkillPlan*), "Memo here".
    ///   SMOKES    a -lokiSmoke run keeps the flat page (the older smokes drive it) unless it says -lokiTree 3d or sets
    ///             <see cref="SessionTree3D"/> (LokiGame.W22Tree.cs), as the menu style's SessionStyle.
    /// Hooks: WristMenu.cs Show / Hide, WristMenu.Board.cs OpenGroup / CloseNewMenu / InitNav, WristMenu.W11.cs CloseSlotPicker,
    /// UI/LokiHud.cs Build.
    /// </summary>
    public sealed partial class WristMenu
    {
        public SkillTree3D Tree { get; private set; }
        private MenuNav.Menu _navTree; private bool _treeFlatOnce, _pickFromTree; private Tab _treePickTab;
        public MenuNav.Menu NavTree => _navTree;
        /// <summary>The Settings row was pressed: the player's choice counts from now on, not a run's forced one.</summary>
        public static void ClearSessionTree()
        {
        }
        /// <summary>The Skills page is the 3D tree now (the setting; the client data's skill tree must be there).</summary>
        public bool TreeWanted
        {
            get
            {
                if (Tree == null || !SkillTreeDb.Loaded) return false;
                return VR.Comfort.SkillTree3D;
            }
        }
        public static int SkillsGroup => GroupOf(Tab.Skills).group;

        /// <summary>Builds the tree (LokiHud, once, after InitBoard) and registers it as a menu.</summary>
        public void InitTree()
        {
            Tree = SkillTree3D.Create(_rt.parent, _head, _prog, _ps);
            Tree.Model = () => SkillTreeDb.Loaded ? SkillTreeModel.Build(SkillJob != null ? SkillJob() : 0, _prog, SkSex) : null;
            Tree.Job = () => SkillJob != null ? SkillJob() : 0; Tree.Sex = () => SkSex;
            Tree.JobLevel = () => _ps.JobLevel; Tree.SkillPoints = () => _ps.SkillPoint;
            Tree.JobLevelMax = () => _ps.NextJobExp >= 999999999L; // (rAthena's "next job EXP" at the highest job level: pc.cpp:8670-8671 pc_nextjobexp, MAX_LEVEL_JOB_EXP)
            Tree.Card = TreeCard; Tree.Act = TreeAct;
            Tree.LookKey = () => LookKeyOf(SkillsGroup);
            Tree.CloseClicked = () => { if (!MenuNav.CloseButton(Tree.transform)) Tree.Close(); };
            Tree.QuickBar = () => { var (g, e) = GroupOf(Tab.Quick); Activate(g, e); };
            Tree.FlatPage = () => { _treeFlatOnce = true; var (g, e) = GroupOf(Tab.Skills); Activate(g, e); };
            Tree.Memo = () => Memo?.Invoke();
            Tree.PlanMode = () => _skPlanMode; Tree.Planned = s => _skPlan.Planned(s); Tree.PlanCost = () => _skPlan.Cost(SkOwned);
            Tree.PlanToggle = SkillPlanToggle; Tree.PlanApply = SkillPlanApply; Tree.PlanReset = SkillPlanReset;
            Tree.PlanStage = TreePlanStage;
            _navTree = new MenuNav.Menu { Name = "SkillTree", Panel = Tree.transform, IsOpen = () => Tree != null && Tree.Shown, Close = () => Tree.Close(),
                                          Capture = () => Tree.Capture(), Reopen = (o, st) => Tree.Reopen(st), Arrow = on => Tree.SetBackArrow(on) };
            MenuNav.Register(_navTree);
        }

        /// <summary>Show(Tab.Skills) with the tree wanted: the tree opens instead (true: the page is not shown).</summary>
        private bool TreeOpens()
        {
            if (!TreeWanted) return false;
            if (_treeFlatOnce) { _treeFlatOnce = false; return false; } // (the plate's "Flat page": the flat page this once)
            W11BeforeShow();
            bool fromRing = _navFromRing;
            if (_ring != null) _ring.Hide();
            bool was = Tree.Shown;
            Tree.Open();
            if (!was) MenuNav.BoardShown(_navTree, _navRing, fromRing); // (from the ring's pick: a step back to the ring; inside a press: decided when it ends; else a fresh start)
            return true;
        }

        /// <summary>A press on a medallion while planning: one more planned level (the flat page's SkillPlanStage on the tree's cell).</summary>
        private void TreePlanStage(int skill)
        {
            var model = Tree.Model(); if (model == null) return;
            _skModel = model;
            foreach (var t in model.Tabs) if (!t.Etc) foreach (var c in t.Cells.Values) if (c.Skill == skill) { SkillPlanStage(c); return; }
        }

        /// <summary>The flat page's own action on a skill, from the tree's card: "up" (learn a level: RoSession.SkillUp), "pin" (the
        /// quick-slot picker: the hotbar's rows and, by the casting gesture, the pad's / the flick's / the wand's own layout), "lv" (the
        /// cast level), "use". True: done (or asked of the server).</summary>
        public bool TreeAct(int skill, string what)
        {
            var model = Tree != null ? Tree.Model() : null; if (model == null) return false;
            _skModel = model; int keepSel = _skSel; var keepView = _skView;
            _skSel = skill;
            try
            {
                var k = SelectedCell()?.Entry;
                if (what == "pin")
                {
                    if (k == null || k.Level <= 0 || k.Passive || Hotkeys == null) return false;
                    // the picker stands on the Skills group's board (New) or on the wrist menu (Classic); closed, it goes back to the tree
                    _treePickTab = _tab; _tab = Tab.Skills; _group = SkillsGroup; _pickFromTree = true;
                    OpenSlotPicker(true, k.Id, _prog.CastLevel(k));
                    return true;
                }
                SkillDetailAction(what);
                if (what == "use" && k != null && !k.Passive && k.Level > 0 && Tree.Shown) Tree.Close(); // (so the skill can be aimed: FLAGGED; the flat page stays open)
                return true;
            }
            finally { _skSel = keepSel; _skView = keepView; }
        }

        /// <summary>The picker opened from the tree was closed (a slot chosen, or Cancel): back to the tree.</summary>
        private bool TreePickerClosed()
        {
            if (!_pickFromTree) return false;
            _pickFromTree = false; _tab = _treePickTab;
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == _tab);
            if (MenuNav.Current == _navBoard && MenuNav.Depth > 0 && MenuNav.Back()) return true; // New: the board closes, the tree comes back as it was
            HidePages(); // Classic: the wrist menu showed the picker beside the tree
            return true;
        }

        /// <summary>A skill's card: the texts of the flat page's detail panel (RefreshSkillDetail's own rules and strings), and which of
        /// its buttons it has now.</summary>
        public SkillTree3D.CardData TreeCard(int skill)
        {
            int job = SkillJob != null ? SkillJob() : 0;
            var model = Tree != null ? Tree.Model() : null; if (model == null) return null;
            SkillTreeModel.Cell c = null;
            foreach (var t in model.Tabs) foreach (var x in t.Cells.Values) if (x.Skill == skill) c = x;
            if (c == null) return null;
            var info = SkillDb.Get(skill); var k = c.Entry; var ex = SkillTreeDb.ExtraOf(skill);
            var d = new SkillTree3D.CardData { Name = info.Name, Icon = SkillIcon(info), Locked = c.State == SkillTreeModel.State.Locked, Quest = ex.Type == "Quest" };
            int max = Mathf.Max(info.MaxLevel, k != null ? k.Level : 0);
            string kind = k == null ? "" : k.Passive ? Lang.T("passive") : k.Ground ? Lang.T("ground target") : k.SelfCast ? Lang.T("on yourself") : k.Support ? Lang.T("support") : Lang.T("~kind~attack");
            d.Level = (k != null ? Lang.F("Level {0} / {1}", k.Level, max) : Lang.F("Max level {0}", max)) + (kind.Length > 0 ? $"  ·  {kind}" : "") + (d.Quest ? "  ·  " + Lang.T("quest skill") : "");
            switch (c.State)
            {
                case SkillTreeModel.State.Learned: d.Why = k.Upgradable ? (_ps.SkillPoint > 0 ? Lang.T("<color=#7fd67f>Learned. You can raise it.</color>") : Lang.T("<color=#ffd060>Learned.</color> <color=#9aa3b5>No skill points left to raise it.</color>")) : k.Level >= max ? Lang.T("<color=#ffd060>Learned at its maximum level.</color>") : Lang.T("<color=#ffd060>Learned.</color>"); break;
                case SkillTreeModel.State.Learnable: d.Why = _ps.SkillPoint > 0 ? Lang.T("<color=#7fd67f>Unlocked: spend a skill point to learn it.</color>") : Lang.T("<color=#7fd67f>Unlocked.</color> <color=#9aa3b5>You need a skill point to learn it.</color>"); break;
                default: d.Why = "<color=#ff9a7a>" + SkillTreeModel.WhyText(c, model) + "</color>"; break;
            }
            if (c.Needs.Count == 0) d.Needs = Lang.T("<color=#9aa3b5>Prerequisites: none</color>");
            else
            {
                var sb = new StringBuilder(Lang.T("Prerequisites:  "));
                for (int i = 0; i < c.Needs.Count; i++)
                {
                    var (s, lv, met) = c.Needs[i];
                    int have = _prog.Skills.TryGetValue(s, out var e) ? e.Level : 0;
                    if (i > 0) sb.Append("   ");
                    sb.Append(met ? "<color=#7fd67f>● " : "<color=#ff8a7a>● ").Append(SkillDb.Get(s).Name).Append(' ').Append(lv).Append("</color>");
                    if (!met) sb.Append(" ").Append(Lang.F("<color=#9aa3b5>(you {0})</color>", have));
                }
                d.Needs = sb.ToString();
            }
            int castLv = k != null && k.Level > 0 ? _prog.CastLevel(k) : 0;
            var per = new StringBuilder();
            if (info.Sp != null && info.Sp.Length > 0 && System.Array.Exists(info.Sp, v => v > 0)) per.Append(Lang.T("SP by level  ")).Append(PerLevel(info.Sp, castLv));
            if (ex.Ap.Length > 0 && System.Array.Exists(ex.Ap, v => v > 0)) per.Append(per.Length > 0 ? "\n" : "").Append(Lang.T("AP by level  ")).Append(PerLevel(ex.Ap, castLv));
            if (ex.Range.Length > 0 && System.Array.Exists(ex.Range, v => v > 1)) per.Append(per.Length > 0 ? "   " : "").Append(RangeText(ex.Range, castLv));
            if (per.Length == 0) per.Append(k != null && k.Passive ? Lang.T("Passive: no SP cost.") : Lang.T("No SP cost in the client data."));
            d.PerLevel = per.ToString();
            d.Lines = DescriptionLines(info, job);
            bool learned = k != null && k.Level > 0;
            d.CanLv = learned && !k.Passive && k.Level > 1; d.LvLabel = "Lv " + castLv;
            d.CanPin = learned && !k.Passive && Hotkeys != null;
            d.CanUp = k != null && k.Upgradable && _ps.SkillPoint > 0;
            d.CanUse = learned && !k.Passive;
            return d;
        }
    }
}
