using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 3 (w3skills): the wrist menu's Skills tab as a skill window — the job name and skill points, one tab per tier
    /// of the job's inherit chain, the tree grid at the client data's positions (skilltreeview.lub: 7 columns, row = pos / 7,
    /// as roBrowserLegacy's SkillListV2.html grid), learned / learnable / locked cells, and a detail panel (state, prerequisites
    /// met / unmet, SP and range per level, the English description with its RO colours made readable on the dark panel, and the
    /// Lv / ★ / + / Use actions). The flat list of earlier waves stays as the "Quick" tab (WristMenu.cs).
    /// Everything comes from the RO skill packets (Progression) and client data (SkillTreeDb, SkillDb): nothing server-specific.
    /// </summary>
    public sealed partial class WristMenu
    {
        /// <summary>The character's job for the tree (set by the game: the selected character's job, then ZC_SPRITE_CHANGE LOOK_BASE).</summary>
        public System.Func<int> SkillJob;

        private enum SkView { Tree, Detail, Quick }
        private SkView _skView = SkView.Tree;
        private SkillTreeModel _skModel; private int _skTab, _skRowOff, _skSel = -1, _skDescOff;
        /// <summary>The skill whose detail was open last: back in the grid its cell and every prerequisite cell (all depths, the
        /// official client's All_NeedSkillList) stay outlined until another tab is chosen.</summary>
        private int _skMark = -1;
        private RectTransform _skTree, _skDetail, _skQuick;
        private Text _skHead, _skBanner, _skLegend;
        private readonly List<UiButton> _skTabBtns = new List<UiButton>();
        private UiButton _skUp, _skDown;
        private const int SkCols = 7, SkRows = 6; private const float SkCellW = 86, SkCellH = 86;
        /// <summary>Client wave 11 (w11menu): the tree's grid is drawn as large as its area allows for the columns and rows the tab
        /// uses (a Novice's three skills are one column: before, three small cells in an empty page), up to this factor; a tab
        /// that uses all seven columns, or more than six rows (it pages with ▲ ▼), is drawn as before (factor 1, never smaller).
        /// The cells, their pictures and names grow together (the grid's own transform is scaled), in both menu styles.</summary>
        public const float SkMaxScale = 2f;
        /// <summary>Most job trees use all seven columns (the page's width is the limit: factor 1) and only three or four of the six
        /// rows: there the cells are made TALLER by up to this factor, so the lower half of the page is used: a larger picture and
        /// the skill's whole name on up to four lines at the largest size (20 px down to 13) that fits, where one line of 13 px
        /// was cut with "…". Six rows or more: cells as before.</summary>
        public const float SkMaxStretch = 2f;
        private RectTransform _skGrid; private float _skStretchLaid = 1f;
        /// <summary>The factor the grid is drawn at now, how much taller than wide its cells are, and the columns / rows of the tab it
        /// was fitted to (smoke, log).</summary>
        public float SkillGridScale { get; private set; } = 1f;
        public float SkillGridStretch { get; private set; } = 1f;
        public (int cols, int rows) SkillGridUsed { get; private set; }
        private sealed class SkCellUi { public UiButton Btn; public CanvasGroup Group; public RectTransform Outline; public Image Icon; public Text Level, Name, Plus; public string FullName; public int Skill; }
        private readonly List<SkCellUi> _skCells = new List<SkCellUi>();
        private SkLineGraphic _skLines; private int _skHoverSkill = -1; private bool _skHoverLocked;
        public const float SkDashPx = 10f, SkGapPx = 7f, SkDashSpeed = 32f;

        private sealed class SkLineGraphic : MaskableGraphic
        {
            public struct Link { public Vector2 P0, P1; public Color32 Color; }
            public readonly List<Link> Links = new List<Link>();
            public bool Dotted;
            public float DashT;
            public int DashesDrawn { get; private set; }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear(); DashesDrawn = 0;
                if (Links.Count == 0) return;
                const float halfW = 2.4f;
                for (int i = 0; i < Links.Count; i++)
                {
                    var l = Links[i];
                    var seg = l.P1 - l.P0; float span = seg.magnitude;
                    if (span < 1f) continue;
                    var dir = seg / span; var n = new Vector2(-dir.y, dir.x) * halfW;
                    if (!Dotted)
                    {
                        AddQuad(vh, l.P0, l.P1, n, l.Color);
                    }
                    else
                    {
                        float step = SkDashPx + SkGapPx, off = Mathf.Repeat(DashT * SkDashSpeed, step);
                        for (float s0 = off - step; s0 < span; s0 += step)
                        {
                            float a0 = Mathf.Max(0f, s0), a1 = Mathf.Min(span, s0 + SkDashPx);
                            if (a1 - a0 < 0.8f) continue;
                            AddQuad(vh, l.P0 + dir * a0, l.P0 + dir * a1, n, l.Color);
                            DashesDrawn++;
                        }
                    }
                }
            }

            private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 n, Color32 col)
            {
                int idx = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x - n.x, a.y - n.y, 0f), col, Vector2.zero);
                vh.AddVert(new Vector3(a.x + n.x, a.y + n.y, 0f), col, Vector2.zero);
                vh.AddVert(new Vector3(b.x + n.x, b.y + n.y, 0f), col, Vector2.zero);
                vh.AddVert(new Vector3(b.x - n.x, b.y - n.y, 0f), col, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx, idx + 2, idx + 3);
            }
        }
        // detail
        private Image _sdIcon; private Text _sdName, _sdLevel, _sdWhy, _sdNeeds, _sdPerLv, _sdDesc, _sdDescPage;
        private UiButton _sdLv, _sdPin, _sdPlus, _sdUse, _sdBack, _sdDescUp, _sdDescDown;
        private const int QuickTab = 1000; // _skTab value of the Quick tab
        // client wave 5 (w5ui): planned points (Game/SkillPlan.cs, roBrowserLegacy's plan-then-apply): the Plan button turns cell taps into
        // "one level more" (with its prerequisites), the header shows the points left / total, Apply sends the upgrades, Reset clears them
        private bool _skPlanMode; private SkillPlan _skPlan = new SkillPlan();
        private UiButton _skPlanBtn, _skPlanApply, _skPlanReset;
        private Dictionary<int, int> _skApplyTargets; private float _skApplyUntil; private int _skApplyLevels;
        /// <summary>Client wave 5 (w5ui): the character's sex (rAthena SEX_FEMALE 0 / SEX_MALE 1) for the job names' Portuguese female forms.</summary>
        public System.Func<int> SkillSex;
        private int SkSex => SkillSex != null ? SkillSex() : 1;
        private int SkOwned(int skill) => _prog.Skills.TryGetValue(skill, out var e) ? e.Level : 0;

        private void BuildSkillWindow(RectTransform p)
        {
            float top = PH / 2;
            _skHead = UiKit.Label(p, "", new Vector2(-95, top - 24), new Vector2(W - 240, 40), 24, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _skHead.resizeTextForBestFit = true; _skHead.resizeTextMinSize = 15; _skHead.resizeTextMaxSize = 24; // w5ui: the plan's "left / total" in Portuguese is longer
            for (int i = 0; i < 6; i++)
            {
                int t = i;
                var b = UiKit.Button(p, "", new Vector2(0, top - 72), new Vector2(90, 46), () => SkillTabPressed(t), 18);
                _skTabBtns.Add(b);
            }
            _skTree = UiKit.Box(p, "skilltree", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _skBanner = UiKit.Label(_skTree, "", new Vector2(0, top - 114), new Vector2(W - 50, 36), 18, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.4f));
            _skBanner.resizeTextForBestFit = true; _skBanner.resizeTextMinSize = 12; _skBanner.resizeTextMaxSize = 18; // w5ui: narrower beside the Plan button
            // client wave 11 (w11menu): the cells are children of one box over the grid's area (7 x 6 cells under the banner), which
            // RefreshSkillGrid scales and places so the tab's own columns and rows fill the area (FitSkillGrid)
            _skGrid = UiKit.Box(_skTree, "skillgrid", new Vector2(0, top - 136 - SkRows * SkCellH / 2f), new Vector2(SkCols * SkCellW, SkRows * SkCellH), new Color(0, 0, 0, 0));
            float gx0 = -(SkCols - 1) * SkCellW / 2f, gy0 = (SkRows - 1) * SkCellH / 2f;
            for (int r = 0; r < SkRows; r++)
                for (int c = 0; c < SkCols; c++)
                {
                    int idx = r * SkCols + c; var pos = new Vector2(gx0 + c * SkCellW, gy0 - r * SkCellH);
                    var ui = new SkCellUi();
                    ui.Outline = UiKit.Box(_skGrid, "outline", pos, new Vector2(SkCellW - 2, SkCellH - 2), new Color(1f, 0.85f, 0.4f));
                    ui.Btn = UiKit.Button(_skGrid, "", pos, new Vector2(SkCellW - 8, SkCellH - 8), () => SkillCellPressed(idx), 14);
                    ui.Group = ui.Btn.gameObject.AddComponent<CanvasGroup>();
                    var br = (RectTransform)ui.Btn.transform;
                    ui.Icon = UiKit.Icon(br, new Vector2(0, 19), new Vector2(38, 38));
                    ui.Level = UiKit.Label(br, "", new Vector2(0, -10), new Vector2(SkCellW - 10, 20), 17, TextAnchor.MiddleCenter);
                    ui.Name = UiKit.Label(br, "", new Vector2(0, -29), new Vector2(SkCellW - 10, 18), 13, TextAnchor.MiddleCenter, UiKit.Dim);
                    ui.Plus = UiKit.Label(br, "+", new Vector2(SkCellW / 2 - 23, SkCellH / 2 - 18), new Vector2(40, 26), 24, TextAnchor.MiddleRight, new Color(0.5f, 1f, 0.5f)); // w5ui: wider for a plan's "+10"
                    ui.Plus.resizeTextForBestFit = true; ui.Plus.resizeTextMinSize = 14; ui.Plus.resizeTextMaxSize = 24;
                    _skCells.Add(ui);
                }
            var linesGo = new GameObject("skilllines", typeof(RectTransform)); linesGo.layer = UiKit.PanelLayer;
            var linesRt = (RectTransform)linesGo.transform; linesRt.SetParent(_skGrid, false);
            linesRt.anchorMin = linesRt.anchorMax = linesRt.pivot = new Vector2(0.5f, 0.5f);
            linesRt.anchoredPosition = Vector2.zero; linesRt.sizeDelta = new Vector2(SkCols * SkCellW, SkRows * SkCellH);
            _skLines = linesGo.AddComponent<SkLineGraphic>(); _skLines.raycastTarget = false;
            _skUp = UiKit.Button(_skTree, "▲", new Vector2(-W / 2 + 60, -top + 24), new Vector2(80, 40), () => { _skRowOff = Mathf.Max(0, _skRowOff - 3); _dirty = true; }, 22);
            _skDown = UiKit.Button(_skTree, "▼", new Vector2(W / 2 - 60, -top + 24), new Vector2(80, 40), () => { _skRowOff += 3; _dirty = true; }, 22);
            _skLegend = UiKit.Label(_skTree, "", new Vector2(0, -top + 24), new Vector2(W - 230, 40), 17, TextAnchor.MiddleCenter, UiKit.Dim);
            // client wave 5 (w5ui): the plan's buttons — Plan beside the banner, Reset / Apply in the footer in place of the legend
            _skPlanBtn = UiKit.Button(_skTree, Lang.T("~skillplan~Plan"), new Vector2(W / 2 - 72, top - 114), new Vector2(116, 36), SkillPlanToggle, 18);
            _skPlanReset = UiKit.Button(_skTree, Lang.T("~skillplan~Reset"), new Vector2(-100, -top + 24), new Vector2(130, 40), SkillPlanReset, 18);
            _skPlanApply = UiKit.Button(_skTree, "", new Vector2(70, -top + 24), new Vector2(190, 40), SkillPlanApply, 18);
            _skPlanApply.Text.resizeTextForBestFit = true; _skPlanApply.Text.resizeTextMinSize = 12; _skPlanApply.Text.resizeTextMaxSize = 18;
            _skPlanReset.gameObject.SetActive(false); _skPlanApply.gameObject.SetActive(false);

            var d = _skDetail = UiKit.Box(p, "skilldetail", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _sdIcon = UiKit.Icon(d, new Vector2(-W / 2 + 60, top - 130), new Vector2(56, 56));
            _sdName = UiKit.Label(d, "", new Vector2(40, top - 118), new Vector2(W - 170, 36), 25, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _sdLevel = UiKit.Label(d, "", new Vector2(40, top - 150), new Vector2(W - 170, 30), 19, TextAnchor.MiddleLeft, UiKit.Dim);
            _sdWhy = UiKit.Label(d, "", new Vector2(0, top - 190), new Vector2(W - 60, 44), 18, TextAnchor.MiddleLeft);
            _sdNeeds = UiKit.Label(d, "", new Vector2(0, top - 245), new Vector2(W - 60, 62), 18, TextAnchor.UpperLeft);
            _sdPerLv = UiKit.Label(d, "", new Vector2(0, top - 300), new Vector2(W - 60, 44), 17, TextAnchor.UpperLeft, UiKit.Dim);
            UiKit.Box(d, "descbg", new Vector2(-25, -95), new Vector2(W - 110, 260), new Color(0, 0, 0, 0.35f));
            _sdDesc = UiKit.Label(d, "", new Vector2(-25, -95), new Vector2(W - 130, 248), 18, TextAnchor.UpperLeft);
            _sdDesc.name = "text:scrolls"; // w4lang: shows the lines from ▲ ▼'s offset on; the rest is cut on purpose (the language smoke skips it)
            _sdDescUp = UiKit.Button(d, "▲", new Vector2(W / 2 - 42, -10), new Vector2(50, 70), () => { _skDescOff = Mathf.Max(0, _skDescOff - 3); _dirty = true; }, 22);
            _sdDescDown = UiKit.Button(d, "▼", new Vector2(W / 2 - 42, -180), new Vector2(50, 70), () => { _skDescOff += 3; _dirty = true; }, 22);
            _sdDescPage = UiKit.Label(d, "", new Vector2(W / 2 - 42, -95), new Vector2(60, 40), 15, TextAnchor.MiddleCenter, UiKit.Dim);
            float ay = -top + 92, bw = (W - 90) / 4f;
            _sdLv = UiKit.Button(d, "Lv", new Vector2(-1.5f * bw - 15, ay), new Vector2(bw - 8, 58), () => SkillDetailAction("lv"), 20);
            _sdPin = UiKit.Button(d, Lang.T("★ Quick"), new Vector2(-0.5f * bw - 5, ay), new Vector2(bw - 8, 58), () => SkillDetailAction("pin"), 20);
            _sdPlus = UiKit.Button(d, Lang.T("+ Level up"), new Vector2(0.5f * bw + 5, ay), new Vector2(bw - 8, 58), () => SkillDetailAction("up"), 20);
            _sdUse = UiKit.Button(d, Lang.T("Use"), new Vector2(1.5f * bw + 15, ay), new Vector2(bw - 8, 58), () => SkillDetailAction("use"), 22);
            _sdBack = UiKit.Button(d, Lang.T("Back to the tree"), new Vector2(0, -top + 30), new Vector2(W - 90, 48), () => SkillDetailAction("back"), 21);
            d.gameObject.SetActive(false);

            _skQuick = UiKit.Box(p, "skillquick", Vector2.zero, new Vector2(W - 30, PH), new Color(0, 0, 0, 0));
            _skQuick.gameObject.SetActive(false);
        }

        private void SkillTabPressed(int i)
        {
            if (_skModel == null) return;
            int n = _skModel.Tabs.Count;
            if (i >= n) { _skView = SkView.Quick; _skTab = QuickTab; }
            else { _skTab = i; _skView = SkView.Tree; _skRowOff = 0; }
            _skSel = -1; if (i >= n) _skMark = -1; _dirty = true; // the marks stay across the tier tabs: prerequisites often sit in an earlier tab
        }

        private SkillTreeModel.Tab CurrentSkillTab => _skModel != null && _skTab >= 0 && _skTab < _skModel.Tabs.Count ? _skModel.Tabs[_skTab] : null;

        private void SkillCellPressed(int idx)
        {
            var tab = CurrentSkillTab; if (tab == null) return;
            int pos = (_skRowOff + idx / SkCols) * SkCols + idx % SkCols;
            if (!tab.Cells.TryGetValue(pos, out var c)) return;
            if (_skPlanMode && !tab.Etc) { SkillPlanStage(c); return; } // client wave 5 (w5ui): rB's click on a cell (SkillListCommon.js:222-229)
            OpenSkillDetail(c.Skill);
        }

        // ---------------------------------------------------------------- client wave 5 (w5ui): planned points
        private void SkillPlanToggle()
        {
            _skPlanMode = !_skPlanMode;
            if (!_skPlanMode && !_skPlan.Empty) { _skPlan = new SkillPlan(); Toast(Lang.T("Plan cleared.")); }
            if (_skPlanMode) _skMark = -1;
            Debug.Log($"[Skills] plan mode {(_skPlanMode ? "on" : "off")}");
            _dirty = true;
        }

        /// <summary>rB onRememberChoice: one level more of the tapped skill, with every prerequisite it needs (SkillPlan.Stage).</summary>
        private void SkillPlanStage(SkillTreeModel.Cell c)
        {
            int job = SkillJob != null ? SkillJob() : 0;
            var info = SkillDb.Get(c.Skill);
            // a cell the server's skill-up limit keeps closed (SkillTreeModel: the earlier tier's points are not spent) gets no packet through
            if (c.Why == SkillTreeModel.Why.Tier) { Toast(SkillTreeModel.WhyText(c, _skModel)); return; }
            var next = _skPlan.Stage(c.Skill, job, SkOwned, _ps.SkillPoint, out var why);
            if (next == null)
            {
                Toast(why == SkillPlan.Refusal.Points ? Lang.F("Not enough skill points for {0} with its prerequisites.", info.Name)
                    : why == SkillPlan.Refusal.Quest ? Lang.F("{0} needs a quest skill you don't have.", info.Name)
                    : why == SkillPlan.Refusal.MaxLevel ? Lang.F("{0}: a prerequisite would go past its maximum level.", info.Name)
                    : Lang.F("{0} can't be planned (its prerequisites are not in the client data).", info.Name));
                Debug.Log($"[Skills] plan: {info.Aegis} refused ({why})");
                return;
            }
            if (why == SkillPlan.Refusal.MaxLevel) Toast(Lang.F("{0} is planned at its maximum level.", info.Name));
            else if (why == SkillPlan.Refusal.Quest) Toast(Lang.F("{0} is learned through a quest, not with skill points.", info.Name));
            _skPlan = next;
            Debug.Log($"[Skills] plan: +1 {info.Aegis} → {_skPlan.Planned(c.Skill)}; plan {SkillPlanSummary()} cost {_skPlan.Cost(SkOwned)} of {_ps.SkillPoint}");
            _dirty = true;
        }

        private void SkillPlanReset() { _skPlan = new SkillPlan(); Debug.Log("[Skills] plan reset"); _dirty = true; }

        /// <summary>rB onApplyChoice: one CZ_UPGRADE_SKILLLEVEL per level, every prerequisite first (SkillPlan.UpgradeOrder); a plan that no
        /// longer fits the server's list or points is dropped, as rB does.</summary>
        private void SkillPlanApply()
        {
            int job = SkillJob != null ? SkillJob() : 0;
            var order = _skPlan.UpgradeOrder(job, SkOwned, _ps.SkillPoint);
            if (order == null)
            {
                Toast(Lang.T("The plan no longer fits your skills or points: it was cleared."));
                Debug.Log("[Skills] plan apply: no longer valid, reset");
                SkillPlanReset(); return;
            }
            _skApplyTargets = new Dictionary<int, int>();
            foreach (int s in _skPlan.Skills) if (_skPlan.Planned(s) > SkOwned(s)) _skApplyTargets[s] = _skPlan.Planned(s);
            _skApplyLevels = order.Count; _skApplyUntil = Time.time + 4f;
            foreach (int s in order) _s.SkillUp(s);
            Debug.Log($"[Skills] plan apply: {order.Count} upgrade(s) in order [{string.Join(",", order.ConvertAll(s => SkillDb.Get(s).Aegis))}]");
            Toast(order.Count == 1 ? Lang.T("Learning 1 skill level…") : Lang.F("Learning {0} skill levels…", order.Count));
            _skPlan = new SkillPlan(); _skPlanMode = false; _dirty = true;
        }

        /// <summary>After Apply: once the server's list shows every planned level (0x010e / 0x0b32), or after 4 s, say what it took.</summary>
        private void SkillPlanWatch()
        {
            if (_skApplyTargets == null) return;
            int done = 0, want = 0;
            foreach (var kv in _skApplyTargets) { want++; if (SkOwned(kv.Key) >= kv.Value) done++; }
            if (done == want)
            {
                Toast(_skApplyLevels == 1 ? Lang.T("Plan applied: 1 level learned.") : Lang.F("Plan applied: {0} levels learned.", _skApplyLevels));
                Debug.Log($"[Skills] plan applied: {_skApplyLevels} level(s), every planned skill at its level");
                _skApplyTargets = null;
            }
            else if (Time.time > _skApplyUntil)
            {
                Toast(Lang.F("The server raised {0} of {1} planned skills: it keeps the others closed (a base or job level, or its tree).", done, want), 7f);
                Debug.Log($"[Skills] plan applied in part: {done} of {want} skills at their planned level");
                _skApplyTargets = null;
            }
        }

        private string SkillPlanSummary()
        {
            var parts = new List<string>();
            foreach (int s in _skPlan.Skills) parts.Add(SkillDb.Get(s).Aegis + "=" + _skPlan.Planned(s));
            return string.Join(",", parts);
        }

        private void OpenSkillDetail(int skill) { _skSel = skill; _skView = SkView.Detail; _skDescOff = 0; _dirty = true; }

        private SkillTreeModel.Cell SelectedCell()
        {
            if (_skModel == null || _skSel < 0) return null;
            foreach (var t in _skModel.Tabs) foreach (var c in t.Cells.Values) if (c.Skill == _skSel) return c;
            return null;
        }

        /// <summary>The detail panel's buttons: "lv" (the cast level, stepping down and wrapping as the Quick list's Lv button),
        /// "pin" (★ to the quick bar), "up" (CZ_UPGRADE_SKILLLEVEL 0x0112 through RoSession.SkillUp), "use", "back".</summary>
        private void SkillDetailAction(string what)
        {
            var c = SelectedCell(); var k = c?.Entry; var info = SkillDb.Get(_skSel);
            switch (what)
            {
                case "back": _skView = SkView.Tree; _skMark = _skSel; _skSel = -1; break;
                case "lv": if (k != null && k.Level > 1) { int lv = _prog.CycleLevel(k); Toast(Lang.F("{0}: cast at level {1} of {2}.", info.Name, lv, k.Level)); } break;
                case "pin":
                    // client wave 11 (w11menu): the player chooses the slot (the design's 2026-10-06 playtest; WristMenu.W11.cs): it was the first free one
                    if (k != null && k.Level > 0 && !k.Passive && Hotkeys != null) { OpenSlotPicker(true, k.Id, _prog.CastLevel(k)); return; }
                    break;
                case "up":
                    // rAthena pc_skillup (pc.cpp:9150-9154): points left, the skill known, permanent, below the tree's max — the upFlag the
                    // server sends is that same test without the points (clif.cpp:5740-5743)
                    if (k != null && k.Upgradable && _ps.SkillPoint > 0) { _s.SkillUp(k.Id); Debug.Log($"[Skills] level up {k.Id} {info.Aegis} (Lv {k.Level})"); }
                    else Toast(_ps.SkillPoint <= 0 ? Lang.T("No skill points left.") : Lang.T("Can't raise that skill yet."));
                    break;
                case "use": if (k != null && !k.Passive && k.Level > 0) _useSkill?.Invoke(k); break;
            }
            _dirty = true;
        }

        /// <summary>Refreshes the Skills page. Returns false when the Quick tab is shown (the caller then draws the flat list).</summary>
        private bool RefreshSkillWindow()
        {
            int job = SkillJob != null ? SkillJob() : 0;
            _skModel = SkillTreeDb.Loaded ? SkillTreeModel.Build(job, _prog, SkSex) : null;
            int nTabs = _skModel != null ? _skModel.Tabs.Count : 0;
            if (_skModel == null && _skView != SkView.Quick) { _skView = SkView.Quick; _skTab = QuickTab; }
            if (_skTab != QuickTab && _skTab >= nTabs) _skTab = 0;
            SkillPlanWatch(); // client wave 5 (w5ui)
            if (_ps.SkillPoint <= 0 && _skPlanMode) { _skPlanMode = false; _skPlan = new SkillPlan(); }
            string pts = _ps.SkillPoint > 1 ? Lang.F("<color=#7fd67f>{0} skill points</color>", _ps.SkillPoint) : _ps.SkillPoint == 1 ? Lang.T("<color=#7fd67f>1 skill point</color>") : Lang.T("<color=#9aa3b5>no skill points</color>");
            // client wave 5 (w5ui): while planning, the points left / total as rB's counter (SkillListCommon.js:554-556)
            if (_skPlanMode && !_skPlan.Empty) pts = Lang.F("<color=#8fc4ff>{0} / {1} skill points left</color>", _ps.SkillPoint - _skPlan.Cost(SkOwned), _ps.SkillPoint);
            _skHead.text = Lang.F("{0}  <color=#9aa3b5>Job Lv {1}</color>  ·  {2}", SkillTreeDb.JobName(job, SkSex), _ps.JobLevel, pts);
            bool memo = Memo != null && _prog.Skills.TryGetValue(27, out var warp) && warp.Level > 0;
            _memoBtn.gameObject.SetActive(memo);
            var hr = (RectTransform)_skHead.transform; hr.sizeDelta = new Vector2(memo ? W - 240 : W - 60, 40); hr.anchoredPosition = new Vector2(memo ? -95 : 0, PH / 2 - 24);
            // tab row: the tiers, "Other" when the server sent skills outside the tree, then Quick
            int shown = nTabs + 1; float tw = (W - 40) / shown;
            for (int i = 0; i < _skTabBtns.Count; i++)
            {
                var b = _skTabBtns[i]; bool on = i < shown; b.gameObject.SetActive(on); if (!on) continue;
                bool quick = i == nTabs;
                ((RectTransform)b.transform).anchoredPosition = new Vector2(-W / 2 + 20 + tw * (i + 0.5f), PH / 2 - 72);
                b.Resize(new Vector2(tw - 6, 46));
                string label = quick ? Lang.T("~skilltab~Quick") : _skModel.Tabs[i].Etc ? Lang.T("~skilltab~Other") : $"{Lang.Again(_skModel.Tabs[i].Tier)} · {_skModel.Tabs[i].Label}"; // w4lang: "1st" … "4th" (SkillTree.TierText) in the current language
                b.SetLabel(label); b.Text.fontSize = label.Length > 22 ? 14 : label.Length > 16 ? 16 : 18;
                bool sel = quick ? _skTab == QuickTab : _skTab == i;
                b.Normal = sel ? new Color(0.45f, 0.35f, 0.15f) : !quick && _skModel.Tabs[i].Gated && _skModel.Tabs[i].Cells.Values.All(c => c.State == SkillTreeModel.State.Locked) ? new Color(0.22f, 0.22f, 0.25f) : UiKit.ButtonBg;
                b.SetHover(false);
            }
            _skTree.gameObject.SetActive(_skView == SkView.Tree);
            _skDetail.gameObject.SetActive(_skView == SkView.Detail);
            _skQuick.gameObject.SetActive(_skView == SkView.Quick);
            if (_skView == SkView.Quick) return false;
            if (_skView == SkView.Detail && SelectedCell() == null) _skView = SkView.Tree;
            if (_skView == SkView.Detail) RefreshSkillDetail(job); else RefreshSkillGrid(job);
            return true;
        }

        /// <summary>Client wave 11 (w11menu): scales and places the grid for a tab: its used columns (the leftmost to the rightmost
        /// column any of its skills is in) and rows (all of them up to the six shown) are drawn as large as the grid's area allows,
        /// from 1 to <see cref="SkMaxScale"/> times, centred sideways, starting at the area's top.</summary>
        private void FitSkillGrid(SkillTreeModel.Tab tab, int rows)
        {
            int minC = SkCols, maxC = -1;
            if (tab != null) foreach (int pos in tab.Cells.Keys) { int col = pos % SkCols; if (col < minC) minC = col; if (col > maxC) maxC = col; }
            if (maxC < 0) { minC = 0; maxC = SkCols - 1; }
            int usedC = maxC - minC + 1, usedR = Mathf.Clamp(rows, 1, SkRows);
            float s = Mathf.Clamp(Mathf.Min(SkCols / (float)usedC, SkRows / (float)usedR), 1f, SkMaxScale);
            float v = Mathf.Clamp(SkRows / (float)usedR / s, 1f, SkMaxStretch); // rows the tab leaves free make its cells taller
            SkillGridScale = s; SkillGridStretch = v; SkillGridUsed = (usedC, usedR);
            if (Mathf.Abs(v - _skStretchLaid) > 1e-3f) LaySkillCells(v);
            _skGrid.localScale = new Vector3(s, s, 1f);
            float blockX = ((minC + maxC) / 2f - (SkCols - 1) / 2f) * SkCellW; // the used columns' centre, in the grid's own pixels
            float areaY = PH / 2 - 136 - SkRows * SkCellH / 2f;                // the area's centre in the page
            _skGrid.anchoredPosition = new Vector2(-s * blockX, areaY + (1f - s) * SkRows * SkCellH / 2f); // row 0's top edge stays at the area's top
        }

        /// <summary>The cells for a stretch factor: 1 = square, exactly as built (picture 38 px, level, one line of name); above 1
        /// the rows are that much taller: the picture up to 44 px, the level line, and the rest of the cell for the name.</summary>
        private void LaySkillCells(float v)
        {
            _skStretchLaid = v;
            float hC = SkCellH * v, hB = hC - 8, gridTop = SkRows * SkCellH / 2f, gx0 = -(SkCols - 1) * SkCellW / 2f;
            bool tall = v > 1.001f;
            float icon = tall ? Mathf.Min(38f * v, 44f) : 38f, iy = tall ? hB / 2 - 5 - icon / 2 : 19f, ly = tall ? iy - icon / 2 - 11f : -10f;
            float nTop = ly - 10f, nBot = -hB / 2 + 3f;
            for (int i = 0; i < _skCells.Count; i++)
            {
                var ui = _skCells[i]; int r = i / SkCols, c = i % SkCols;
                var pos = new Vector2(gx0 + c * SkCellW, gridTop - (r + 0.5f) * hC);
                ui.Outline.anchoredPosition = pos; ui.Outline.sizeDelta = new Vector2(SkCellW - 2, hC - 2);
                ((RectTransform)ui.Btn.transform).anchoredPosition = pos; ui.Btn.Resize(new Vector2(SkCellW - 8, hB));
                ui.Icon.rectTransform.anchoredPosition = new Vector2(0, iy); ui.Icon.rectTransform.sizeDelta = new Vector2(icon, icon);
                ui.Level.rectTransform.anchoredPosition = new Vector2(0, ly);
                ui.Name.rectTransform.anchoredPosition = tall ? new Vector2(0, (nTop + nBot) / 2) : new Vector2(0, -29);
                ui.Name.rectTransform.sizeDelta = tall ? new Vector2(SkCellW - 10, nTop - nBot) : new Vector2(SkCellW - 10, 18);
                if (!tall) ui.Name.fontSize = 13;
                ui.Plus.rectTransform.anchoredPosition = new Vector2(SkCellW / 2 - 23, hB / 2 - 14);
            }
        }

        /// <summary>A name in a tall cell: the whole name, wrapped at its spaces, at the largest size from <paramref name="maxFont"/>
        /// down to <paramref name="minFont"/> at which every word fits the box's width and the lines fit its height; if it does not
        /// fit even at the smallest, it is cut with "…" there (as <see cref="FitOneLine"/> does for one line).</summary>
        private static string FitLines(Text t, string s, int maxFont, int minFont)
        {
            if (string.IsNullOrEmpty(s) || t == null) return s ?? "";
            var box = t.rectTransform.rect.size; float ppu = t.pixelsPerUnit > 0 ? t.pixelsPerUnit : 1f; var g = t.cachedTextGeneratorForLayout;
            bool Fits(string x, int f)
            {
                var wrap = t.GetGenerationSettings(new Vector2(box.x, 0f)); wrap.fontSize = f; wrap.resizeTextForBestFit = false;
                wrap.horizontalOverflow = HorizontalWrapMode.Wrap; wrap.verticalOverflow = VerticalWrapMode.Overflow;
                if (g.GetPreferredHeight(x, wrap) / ppu > box.y + 0.5f) return false;
                var one = t.GetGenerationSettings(Vector2.zero); one.fontSize = f; one.resizeTextForBestFit = false; one.horizontalOverflow = HorizontalWrapMode.Overflow;
                foreach (var word in x.Split(' ')) if (word.Length > 0 && g.GetPreferredWidth(word, one) / ppu > box.x) return false;
                return true;
            }
            for (int f = maxFont; f >= minFont; f--) if (Fits(s, f)) { t.fontSize = f; return s; }
            t.fontSize = minFont;
            for (int n = s.Length - 1; n > 1; n--) { string cut = s.Substring(0, n).TrimEnd() + "…"; if (Fits(cut, minFont)) return cut; }
            return s.Substring(0, 1) + "…";
        }

        private static Vector2 ClipCellEdge(Vector2 center, Vector2 dir, float halfW, float halfH)
        {
            float tx = Mathf.Abs(dir.x) > 1e-4f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 1e-4f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            return center + dir * Mathf.Min(tx, ty);
        }

        private void SkillHoverTick(float dt)
        {
            if (_tab != Tab.Skills || _skView != SkView.Tree || _skTree == null || !_skTree.gameObject.activeInHierarchy)
            {
                if (_skHoverSkill >= 0) { _skHoverSkill = -1; if (_skLines != null) { _skLines.Links.Clear(); _skLines.DashT = 0f; _skLines.SetVerticesDirty(); } }
                return;
            }
            int now = -1;
            if (now < 0)
            {
                for (int i = 0; i < _skCells.Count; i++)
                {
                    var ui = _skCells[i];
                    if (ui.Btn != null && ui.Btn.gameObject.activeSelf && ui.Btn.Hovered && ui.Skill > 0) { now = ui.Skill; break; }
                }
            }
            if (now != _skHoverSkill) { _dirty = true; if (now <= 0 && _skLines != null) _skLines.DashT = 0f; }
            if (_skHoverSkill > 0 && _skHoverLocked && _skLines != null && _skLines.Links.Count > 0)
            {
                _skLines.DashT += dt;
                _skLines.SetVerticesDirty();
            }
        }

        private void RefreshSkillGrid(int job)
        {
            var tab = CurrentSkillTab;
            int hoverSkill = -1;
            if (hoverSkill < 0 && tab != null)
            {
                for (int i = 0; i < _skCells.Count; i++)
                {
                    var ui = _skCells[i];
                    int pos = (_skRowOff + i / SkCols) * SkCols + i % SkCols;
                    if (ui.Btn != null && ui.Btn.Hovered && tab.Cells.TryGetValue(pos, out var hc)) { hoverSkill = hc.Skill; break; }
                }
            }
            _skHoverSkill = hoverSkill;
            var hoverNeeds = hoverSkill > 0 ? SkillTreeDb.AllNeeds(hoverSkill, job) : null;
            if (hoverNeeds != null && hoverNeeds.Count == 0) hoverNeeds = null;
            SkillTreeModel.Cell hoverCell = null;
            if (hoverSkill > 0 && tab != null)
                foreach (var c in tab.Cells.Values) if (c.Skill == hoverSkill) { hoverCell = c; break; }
            _skHoverLocked = hoverSkill > 0 && hoverCell != null && hoverCell.State == SkillTreeModel.State.Locked;

            int focusMark = hoverSkill > 0 ? hoverSkill : _skMark;
            var marked = hoverSkill > 0 ? hoverNeeds : (_skMark >= 0 ? SkillTreeDb.AllNeeds(_skMark, job) : null);
            if (marked != null && marked.Count == 0) marked = null; // nothing to point at
            int rows = tab != null ? tab.Rows : 1;
            _skRowOff = Mathf.Clamp(_skRowOff, 0, Mathf.Max(0, rows - SkRows));
            FitSkillGrid(tab, rows); // client wave 11 (w11menu): the tree uses the page
            _skUp.gameObject.SetActive(rows > SkRows && _skRowOff > 0); _skDown.gameObject.SetActive(rows > SkRows && _skRowOff + SkRows < rows);
            int learned = 0, can = 0, locked = 0;
            if (tab != null) foreach (var c in tab.Cells.Values) { if (c.State == SkillTreeModel.State.Learned) learned++; else if (c.State == SkillTreeModel.State.Learnable) can++; else locked++; }
            _skLegend.text = Lang.F("<color=#ffd060>●</color> {0} learned   <color=#7fd67f>●</color> {1} to learn   <color=#8a90a0>●</color> {2} locked", learned, can, locked) + (rows > SkRows ? "   " + Lang.F("<color=#9aa3b5>rows {0}-{1}/{2}</color>", _skRowOff + 1, Mathf.Min(rows, _skRowOff + SkRows), rows) : "");
            if (_skModel != null && !_skModel.ListReceived) _skBanner.text = Lang.T("Waiting for the server's skill list…");
            else if (tab != null && tab.Gated) _skBanner.text = Lang.T("Greyed skills open once the skill points of the earlier tier are spent.");
            else if (tab != null && tab.Etc) _skBanner.text = Lang.T("Skills you have outside your job's tree.");
            else if (marked != null)
            {
                var elsewhere = new List<string>();
                foreach (var t in _skModel.Tabs)
                    if (t != tab) foreach (var oc in t.Cells.Values) if (marked.ContainsKey(oc.Skill)) { elsewhere.Add(t.Etc ? Lang.T("~skilltab~Other") : Lang.Again(t.Tier) + " · " + t.Label); break; }
                _skBanner.text = elsewhere.Count > 0
                    ? Lang.F("<color=#ffffff>{0}</color> needs the outlined skills (<color=#7fd67f>met</color> / <color=#ffa050>not yet</color>), also in {1}.", SkillDb.Get(focusMark).Name, string.Join(", ", elsewhere))
                    : Lang.F("<color=#ffffff>{0}</color> needs the outlined skills (<color=#7fd67f>met</color> / <color=#ffa050>not yet</color>).", SkillDb.Get(focusMark).Name);
            }
            else _skBanner.text = Lang.T("<color=#9aa3b5>Tap a skill for its details.</color>");
            // client wave 5 (w5ui): the plan — its button beside the banner (only with skill points, in a tree tab), its own banner text,
            // Reset / Apply in the footer instead of the legend while a plan exists
            bool canPlan = _ps.SkillPoint > 0 && _skModel != null && _skModel.ListReceived && tab != null && !tab.Etc;
            _skPlanBtn.gameObject.SetActive(canPlan || _skPlanMode);
            _skPlanBtn.SetLabel(_skPlanMode ? Lang.T("~skillplan~Planning ✓") : Lang.T("~skillplan~Plan"));
            _skPlanBtn.Normal = _skPlanMode ? new Color(0.2f, 0.36f, 0.6f) : UiKit.ButtonBg; _skPlanBtn.SetHover(false);
            var brt = (RectTransform)_skBanner.transform; bool planBtn = _skPlanBtn.gameObject.activeSelf;
            brt.sizeDelta = new Vector2(planBtn ? W - 180 : W - 50, 36); brt.anchoredPosition = new Vector2(planBtn ? -62 : 0, PH / 2 - 114);
            int planCost = _skPlan.Cost(SkOwned);
            if (_skPlanMode && tab != null && !tab.Gated && !tab.Etc)
                _skBanner.text = _skPlan.Empty ? Lang.T("<color=#8fc4ff>Planning: tap a skill to add a level.</color>")
                                               : Lang.F("<color=#8fc4ff>Plan: {0} of {1} points. Tap for more, or Apply.</color>", planCost, _ps.SkillPoint);
            bool showPlan = _skPlanMode && !_skPlan.Empty;
            _skPlanReset.gameObject.SetActive(showPlan); _skPlanApply.gameObject.SetActive(showPlan); _skLegend.gameObject.SetActive(!showPlan);
            if (showPlan) _skPlanApply.SetLabel(planCost == 1 ? Lang.T("Apply 1 point") : Lang.F("Apply {0} points", planCost));
            for (int i = 0; i < _skCells.Count; i++)
            {
                var ui = _skCells[i];
                int pos = (_skRowOff + i / SkCols) * SkCols + i % SkCols;
                SkillTreeModel.Cell c = null;
                bool has = tab != null && tab.Cells.TryGetValue(pos, out c);
                ui.Btn.gameObject.SetActive(has); ui.Outline.gameObject.SetActive(false); ui.Skill = has ? c.Skill : 0;
                if (!has) { if (ui.Group != null) ui.Group.alpha = 1f; continue; }
                bool faded = hoverSkill > 0 && (_skHoverLocked || (hoverNeeds != null && hoverNeeds.Count > 0)) && c.Skill != hoverSkill && (hoverNeeds == null || !hoverNeeds.ContainsKey(c.Skill));
                if (ui.Group != null) ui.Group.alpha = faded ? SkillTree3D.HoverFadeAlpha : 1f;
                if (marked != null && (c.Skill == focusMark || marked.ContainsKey(c.Skill)))
                {
                    int have = c.Entry != null ? c.Entry.Level : 0;
                    ui.Outline.GetComponent<Image>().color = c.Skill == focusMark ? Color.white : have >= marked[c.Skill] ? new Color(0.5f, 0.95f, 0.5f) : new Color(1f, 0.63f, 0.31f);
                    ui.Outline.gameObject.SetActive(true);
                }
                var info = SkillDb.Get(c.Skill);
                ui.Icon.sprite = SkillIcon(info); ui.Icon.enabled = ui.Icon.sprite != null;
                int max = Mathf.Max(info.MaxLevel, c.Entry != null ? c.Entry.Level : 0);
                // w4lang: a long name ends in "…" instead of losing its last word unseen; client wave 11: a tall cell (FitSkillGrid) shows the whole name on several lines
                ui.Name.text = SkillGridStretch > 1.001f ? FitLines(ui.Name, info.Name, 20, 13) : FitOneLine(ui.Name, info.Name); ui.FullName = info.Name;
                switch (c.State)
                {
                    case SkillTreeModel.State.Learned:
                        ui.Btn.Normal = new Color(0.24f, 0.22f, 0.12f); ui.Icon.color = Color.white;
                        ui.Level.text = $"<b>{c.Entry.Level}</b><color=#b8a878>/{max}</color>"; ui.Level.color = new Color(1f, 0.88f, 0.5f); break;
                    case SkillTreeModel.State.Learnable:
                        ui.Btn.Normal = new Color(0.12f, 0.26f, 0.16f); ui.Icon.color = new Color(1, 1, 1, 0.8f);
                        ui.Level.text = $"0/{max}"; ui.Level.color = new Color(0.55f, 0.95f, 0.55f); break;
                    default:
                        ui.Btn.Normal = new Color(0.1f, 0.1f, 0.12f); ui.Icon.color = new Color(1, 1, 1, 0.3f);
                        ui.Level.text = c.Why == SkillTreeModel.Why.Tier ? Lang.T("~cell~tier") : c.Why == SkillTreeModel.Why.Quest ? Lang.T("~cell~quest") : Lang.T("~cell~locked"); ui.Level.color = new Color(0.55f, 0.57f, 0.62f); break;
                }
                ui.Plus.text = "+"; ui.Plus.color = new Color(0.5f, 1f, 0.5f);
                ui.Plus.gameObject.SetActive(c.Entry != null && c.Entry.Upgradable && _ps.SkillPoint > 0 && !_skPlanMode);
                // client wave 5 (w5ui): a planned level is drawn as rB draws it (the planned count in the level, the cell as learnable:
                // SkillListCommon.js renderRememberChoice :531-557), in the plan's blue, with the levels it adds in the corner
                int own = c.Entry != null ? c.Entry.Level : 0, planned = _skPlan.Planned(c.Skill);
                if (_skPlanMode && planned > own)
                {
                    ui.Btn.Normal = new Color(0.12f, 0.2f, 0.34f); ui.Icon.color = Color.white;
                    ui.Level.text = $"<b>{planned}</b><color=#8fa8c8>/{max}</color>"; ui.Level.color = new Color(0.56f, 0.77f, 1f);
                    ui.Plus.text = "+" + (planned - own); ui.Plus.color = new Color(0.56f, 0.77f, 1f); ui.Plus.gameObject.SetActive(true);
                }
                if (!ui.Btn.Hovered) ui.Btn.SetHover(false);
            }
            if (_skLines != null)
            {
                _skLines.Links.Clear(); _skLines.Dotted = _skHoverLocked;
                if (hoverSkill > 0 && hoverNeeds != null && tab != null)
                {
                    float halfW = (SkCellW - 18f) * 0.5f, halfH = (SkCellH * _skStretchLaid - 18f) * 0.5f;
                    for (int ib = 0; ib < _skCells.Count; ib++)
                    {
                        var ub = _skCells[ib]; if (!ub.Btn.gameObject.activeSelf || ub.Skill <= 0) continue;
                        if (ub.Skill != hoverSkill && !hoverNeeds.ContainsKey(ub.Skill)) continue;
                        var pb = ((RectTransform)ub.Btn.transform).anchoredPosition;
                        foreach (var (needSkill, needLv) in SkillTreeDb.Needs(ub.Skill, job))
                        {
                            if (!hoverNeeds.ContainsKey(needSkill)) continue;
                            for (int ia = 0; ia < _skCells.Count; ia++)
                            {
                                var ua = _skCells[ia]; if (!ua.Btn.gameObject.activeSelf || ua.Skill != needSkill) continue;
                                var pa = ((RectTransform)ua.Btn.transform).anchoredPosition;
                                var delta = pb - pa; float dist = delta.magnitude; if (dist < 4f) break;
                                var dir = delta / dist;
                                var p0 = ClipCellEdge(pa, dir, halfW, halfH);
                                var p1 = ClipCellEdge(pb, -dir, halfW, halfH);
                                bool met = SkOwned(needSkill) >= needLv;
                                _skLines.Links.Add(new SkLineGraphic.Link
                                {
                                    P0 = p0, P1 = p1,
                                    Color = met ? new Color32(140, 255, 150, 240) : new Color32(255, 170, 90, 240)
                                });
                                break;
                            }
                        }
                    }
                }
                _skLines.SetVerticesDirty();
            }
        }

        private void RefreshSkillDetail(int job)
        {
            var c = SelectedCell(); var info = SkillDb.Get(_skSel); var k = c.Entry; var ex = SkillTreeDb.ExtraOf(_skSel);
            _sdIcon.sprite = SkillIcon(info); _sdIcon.enabled = _sdIcon.sprite != null; _sdIcon.color = c.State == SkillTreeModel.State.Locked ? new Color(1, 1, 1, 0.45f) : Color.white;
            _sdName.text = info.Name;
            int max = Mathf.Max(info.MaxLevel, k != null ? k.Level : 0);
            string kind = k == null ? "" : k.Passive ? Lang.T("passive") : k.Ground ? Lang.T("ground target") : k.SelfCast ? Lang.T("on yourself") : k.Support ? Lang.T("support") : Lang.T("~kind~attack");
            _sdLevel.text = (k != null ? Lang.F("Level {0} / {1}", k.Level, max) : Lang.F("Max level {0}", max)) + (kind.Length > 0 ? $"  ·  {kind}" : "") + (ex.Type == "Quest" ? "  ·  " + Lang.T("quest skill") : "");
            switch (c.State)
            {
                case SkillTreeModel.State.Learned: _sdWhy.text = k.Upgradable ? (_ps.SkillPoint > 0 ? Lang.T("<color=#7fd67f>Learned. You can raise it.</color>") : Lang.T("<color=#ffd060>Learned.</color> <color=#9aa3b5>No skill points left to raise it.</color>")) : k.Level >= max ? Lang.T("<color=#ffd060>Learned at its maximum level.</color>") : Lang.T("<color=#ffd060>Learned.</color>"); break;
                case SkillTreeModel.State.Learnable: _sdWhy.text = _ps.SkillPoint > 0 ? Lang.T("<color=#7fd67f>Unlocked: spend a skill point to learn it.</color>") : Lang.T("<color=#7fd67f>Unlocked.</color> <color=#9aa3b5>You need a skill point to learn it.</color>"); break;
                default: _sdWhy.text = "<color=#ff9a7a>" + SkillTreeModel.WhyText(c, _skModel) + "</color>"; break;
            }
            if (c.Needs.Count == 0) _sdNeeds.text = Lang.T("<color=#9aa3b5>Prerequisites: none</color>");
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
                _sdNeeds.text = sb.ToString();
            }
            // SP (and AP, range) per level from the client data; the cast level (Lv button) is marked
            int castLv = k != null && k.Level > 0 ? _prog.CastLevel(k) : 0;
            var per = new StringBuilder();
            if (info.Sp != null && info.Sp.Length > 0 && System.Array.Exists(info.Sp, v => v > 0)) per.Append(Lang.T("SP by level  ")).Append(PerLevel(info.Sp, castLv));
            if (ex.Ap.Length > 0 && System.Array.Exists(ex.Ap, v => v > 0)) per.Append(per.Length > 0 ? "\n" : "").Append(Lang.T("AP by level  ")).Append(PerLevel(ex.Ap, castLv));
            if (ex.Range.Length > 0 && System.Array.Exists(ex.Range, v => v > 1)) per.Append(per.Length > 0 ? "   " : "").Append(RangeText(ex.Range, castLv));
            if (per.Length == 0) per.Append(k != null && k.Passive ? Lang.T("Passive: no SP cost.") : Lang.T("No SP cost in the client data."));
            _sdPerLv.text = per.ToString();
            // description: job-specific requirement lines filtered for this job, RO colours made readable
            var lines = DescriptionLines(info, job);
            int maxOff = Mathf.Max(0, lines.Count - 4);
            _skDescOff = Mathf.Clamp(_skDescOff, 0, maxOff);
            _sdDesc.text = lines.Count == 0 ? Lang.T("<color=#8890a0>No description in the client data.</color>") : string.Join("\n", lines.GetRange(_skDescOff, lines.Count - _skDescOff));
            _sdDescUp.gameObject.SetActive(_skDescOff > 0); _sdDescDown.gameObject.SetActive(_skDescOff < maxOff);
            _sdDescPage.text = lines.Count > 4 ? $"{_skDescOff + 1}/{lines.Count}" : "";
            bool learned = k != null && k.Level > 0;
            _sdLv.gameObject.SetActive(learned && !k.Passive && k.Level > 1); if (learned) { _sdLv.SetLabel("Lv " + castLv); _sdLv.Normal = castLv < k.Level ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _sdLv.SetHover(false); }
            _sdPin.gameObject.SetActive(learned && !k.Passive && Hotkeys != null);
            _sdPlus.gameObject.SetActive(k != null && k.Upgradable && _ps.SkillPoint > 0);
            _sdUse.gameObject.SetActive(learned && !k.Passive);
        }

        /// <summary>Client wave 4b (w4lang): <paramref name="s"/> as it fits one line of <paramref name="t"/>'s box, cut with "…" when it
        /// is too long (a tree cell holds about 12 characters; the detail panel shows the whole name). The language smoke measures every
        /// text against its box: a cell name used to wrap into a second line the 18-pixel box then hid.</summary>
        private static string FitOneLine(Text t, string s)
        {
            if (string.IsNullOrEmpty(s) || t == null) return s ?? "";
            var gs = t.GetGenerationSettings(Vector2.zero); gs.horizontalOverflow = HorizontalWrapMode.Overflow; gs.resizeTextForBestFit = false;
            float max = t.rectTransform.rect.width, ppu = t.pixelsPerUnit > 0 ? t.pixelsPerUnit : 1f;
            var g = t.cachedTextGeneratorForLayout;
            if (g.GetPreferredWidth(s, gs) / ppu <= max) return s;
            for (int n = s.Length - 1; n > 1; n--)
            {
                string cut = s.Substring(0, n).TrimEnd() + "…";
                if (g.GetPreferredWidth(cut, gs) / ppu <= max) return cut;
            }
            return s.Substring(0, 1) + "…";
        }

        private static string PerLevel(int[] v, int mark)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (i > 0) sb.Append(" · ");
                if (i + 1 == mark) sb.Append("<color=#ffd060><b>").Append(v[i]).Append("</b></color>"); else sb.Append(v[i]);
            }
            return sb.ToString();
        }
        private static string RangeText(int[] r, int mark)
        {
            bool same = true; foreach (int x in r) if (x != r[0]) same = false;
            return same ? Lang.F("Range {0} cells", r[0]) : Lang.T("Range by level  ") + PerLevel(r, mark);
        }

        private static readonly Regex JobLine = new Regex(@"^(\d+)(Skill Requirement.*)$", RegexOptions.Compiled);
        /// <summary>
        /// The English description as the skill window shows it. skillinfo_f.lub GetSkillDescript(…, skill, …) copies every line
        /// except requirement lines that start with a job id ("4Skill Requirement: Heal 2" for the Acolyte, "14Skill Requirement:
        /// Faith 5" for the Crusader in AL_CURE): only the line of a job in the character's inherit chain is kept, without the id.
        /// The client finds those lines by its MSI_SKILL_REQUIREMENTS message; that text is not in our data, so the English data's
        /// own requirement lines' opening words ("Skill Requirement") stand for it. The first line (the skill's name, shown above)
        /// is dropped.
        /// </summary>
        public static List<string> DescriptionLines(SkillDb.Skill info, int job)
        {
            var outl = new List<string>();
            if (string.IsNullOrEmpty(info.Description)) return outl;
            var chain = SkillTreeDb.Chain(job);
            var src = info.Description.Split('\n');
            for (int i = 0; i < src.Length; i++)
            {
                string l = src[i].TrimEnd();
                if (i == 0 && string.Equals(UiKit.StripTags(l).Trim(), info.Name.Trim(), System.StringComparison.OrdinalIgnoreCase)) continue;
                var m = JobLine.Match(l);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int j) && SkillTreeDb.GetJob(j) != null)
                {
                    if (!chain.Contains(j)) continue;
                    l = m.Groups[2].Value;
                }
                outl.Add(ReadableRo(l));
            }
            return outl;
        }

        /// <summary>
        /// RO colour codes (^RRGGBB, ^000000 = back to the default colour) as uGUI rich text, each colour lightened until it reads on
        /// the dark panel (UiKit.PanelBg ≈ #12141F): the descriptions are written for the official client's white windows, where
        /// ^777777 grey, ^000099 navy or ^993300 brown are fine but nearly vanish here. The colour is mixed towards white until its
        /// relative luminance (WCAG) is at least 0.35 — about 7:1 against the panel — so hue is kept and bright colours are left alone.
        /// A stray '&lt;' is escaped as UiKit.RoColors does.
        /// </summary>
        public static string ReadableRo(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 32); bool open = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '^' && IsHex6(s, i + 1))
                {
                    string hex = s.Substring(i + 1, 6);
                    if (open) sb.Append("</color>");
                    if (hex == "000000") open = false;
                    else { sb.Append("<color=#").Append(Lighten(hex)).Append('>'); open = true; }
                    i += 6; continue;
                }
                if (ch == '<') { sb.Append("<​"); continue; }
                sb.Append(ch);
            }
            if (open) sb.Append("</color>");
            return sb.ToString();
        }
        private static bool IsHex6(string s, int at)
        {
            if (at + 6 > s.Length) return false;
            for (int i = at; i < at + 6; i++) { char c = s[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false; }
            return true;
        }
        /// <summary>#RRGGBB → a lighter #RRGGBB with relative luminance ≥ 0.35 (sRGB → linear per WCAG 2.x), mixing towards white.
        /// Client wave 13, item 21 (w13grab): the rule now lives in UI/UiKit.cs ReadableHex, which every RO text uses (UiKit.RoColors).</summary>
        public static string Lighten(string hex) => UiKit.ReadableHex(hex);

    }
}
