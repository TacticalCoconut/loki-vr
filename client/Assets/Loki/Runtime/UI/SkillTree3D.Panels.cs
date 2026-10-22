using System;
using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: the panels of the 3D skill trees (UI/SkillTree3D.cs).
    ///   THE PLATE (left)  the job, its job level and the skill points; the TAG FILTERS ("tag filter buttons beside the tree": one per
    ///                     tag, its name in its colour, lit while chosen; Clear; they hold for every tree); Plan / Reset / Apply (the
    ///                     flat page's planned points); Memo here (with Warp Portal); Quick bar, Flat page, X.
    ///   THE CARD (right)  the skill selected (else the one a pointer rests on). Client wave 23 (w23tree), design note 2026-10-11: "what the
    ///                     skill does should be first and how to get it should be underneath it": under its icon, name, level and kind,
    ///                     FIRST WHAT IT DOES: its tags in their colours, the description (it scrolls), SP / range by level; a thin rule;
    ///                     THEN HOW TO GET IT: its state (learned, unlocked, why it is not), its prerequisites, a quest skill's quest,
    ///                     how to start it and what it asks for; and for the selected skill the flat page's four buttons: + Level up,
    ///                     ★ Quick, Lv, Use (UI/WristMenu.Tree.cs TreeAct: the flat page's own code).
    ///   THE PREVIEW       (UI/SkillDiorama.cs) of the skill a pointer rests on, else of the LOCKED skill (the one selected): client
    ///                     wave 23, design note: "I shouldn't need to keep mousing it over just locking onto the skill should keep it playing
    ///                     also don't let it get in front of panels": it stands at the first of <see cref="PreviewSpots"/> places round
    ///                     its medallion (beside it away from the trunk as before, beside it on the trunk's side, above it, below it)
    ///                     where its box, as the eyes see it, is <see cref="PreviewClearDeg"/> degrees clear of the plate, the card, the
    ///                     strip and every open window, and not in the game's table; it leaves a place only when a panel comes there.
    ///   THE STRIP (above) one at a time only: the round "&lt;" and "&gt;" buttons (the job before / the next job), the job's name and a
    ///                     dot per tree between them, over the top of the tree shown; a button at an end is washed out and does nothing.
    ///   A tree grown out of a node at the first job's base (the Novice's sapling, the stake "Other": UI/SkillTree3D.cs ToggleSprout)
    ///                     stands left of that tree: the plate, while it serves that tree, stands left of what has grown.
    /// The plate and the card stand on the circle round the eyes at the trees' distance, turned to the eyes, past the left and the
    /// right edge of the tree they serve (one tree shown: one at a time, or a character with one tree): the plate left, the card
    /// right. They are drawn in the boards' pixels (<see cref="Px"/> mm each) so their text is as large as a board's (20 px = 1.3°
    /// from 0.65 m).
    ///   TWO TREES OR MORE, AROUND YOU  client wave 23 (w23tree, item 23): the trees stand close together now (UI/SkillTree3D.cs
    ///                     ArcGapDeg: 6°), and wave 22's quarter turn between two trees was the room these two panels take (the card
    ///                     38°, the plate 30 to 39°). So (FLAGGED): THE PLATE has the arc's RIGHT END, right of the current job's
    ///                     tree: it is the arc's last piece (trees and plate together are centred straight ahead) and stands over no
    ///                     tree, whichever tree is faced. THE CARD opens LEFT of the tree of ITS skill (the one selected, else the one
    ///                     a pointer rests on): over the tree before it (left of the first tree: over none). There it is drawn OVER
    ///                     that tree (each panel has its own canvas, one order above the trees) and the medallions it covers, as the
    ///                     eyes see it, take no laser, no look and no fingertip (<see cref="Cover"/>) until the card closes (unlock:
    ///                     its X, or a pull on nothing). First tried and measured (smoke run 1): the plate left of the tree faced, as
    ///                     in wave 22: it covered 34° of the 52° wide tree before it, trunk and all, and looking at that tree was
    ///                     looking at the plate, so the plate could not be looked away.
    /// </summary>
    public sealed partial class SkillTree3D
    {
        /// <summary>A panel pixel in the trees' millimetres (the boards' 0.00074 m), the panels' sizes (px), the gap to the tree
        /// (degrees), the height of the plate's and the card's top over the trees' middle (mm), the strip over the trunk's top (mm).</summary>
        public const float Px = 0.74f, PlateW = 400f, CardW = 540f, CardH = 820f, NavW = 600f, NavH = 104f, PanelGapDeg = 1.6f, PanelTop = 290f, NavGap = 30f;
        /// <summary>A tag button's height and its row's pitch on the plate (px).</summary>
        public const float TagH = 52f, TagPitch = 58f;
        /// <summary>The Element column right of the tags (only when the line can inflict an element): a button's width, the gap to
        /// the tags. The plate is that much wider (FLAGGED: under the tags it would be taller, into the game's table).</summary>
        public const float EleW = 136f, EleGap = 8f;
        /// <summary>What the card shows of a skill: made by the flat page's own code (UI/WristMenu.Tree.cs TreeCard).</summary>
        public sealed class CardData
        {
            public string Name = "", Level = "", Why = "", Needs = "", PerLevel = "", LvLabel = "Lv";
            public List<string> Lines = new List<string>(); public Sprite Icon; public bool Locked, CanUp, CanPin, CanLv, CanUse, Quest;
        }
        public Func<int, CardData> Card;
        /// <summary>The look's kind of this menu (UI/LookSelect.cs): the Skills board's own.</summary>
        public Func<string> LookKey = () => "Menu:Skills";

        private RectTransform _plate, _card, _nav;
        private Text _plJob, _plPoints, _plFilters, _navName, _navDots;
        private readonly List<UiButton> _tagBtns = new List<UiButton>(), _panelBtns = new List<UiButton>();
        private readonly List<string> _tagIds = new List<string>();
        private readonly List<UiButton> _eleBtns = new List<UiButton>(); private readonly List<string> _eleIds = new List<string>();
        private Text _plElement; private string _eleKey;
        /// <summary>The plate's width now: with the Element column when the line has an element.</summary>
        public float PlateWidth => PlateW + (_eleBtns.Count > 0 ? EleW + EleGap : 0f);
        private UiButton _plClear, _plPlan, _plReset, _plApply, _plMemo, _plQuick, _plFlat, _plX, _back, _navBack, _navFwd;
        private float _plateH = 700f; private int _tagsBuilt = -1;
        private Image _cdIcon; private Text _cdName, _cdLevel, _cdWhy, _cdNeeds, _cdTags, _cdQuest, _cdPer, _cdHint;
        private UiScrollText _cdDesc; private UiButton _cdUp, _cdPin, _cdLv, _cdUse, _cdX; private RectTransform _cdRule;
        private bool _cardScrollReset; private int _cardSkill;
        /// <summary>THE PREVIEW'S PLACE (client wave 23, w23tree): how far its box keeps from a panel as the eyes see both (degrees),
        /// how often a standing preview's place is looked at again (s), its gap to its medallion (mm): three numbers design note did not
        /// give (FLAGGED). The places round a medallion, in the order they are tried.</summary>
        public const float PreviewClearDeg = 1f, PreviewCheckSeconds = 0.2f, PreviewGap = 14f;
        public const int PreviewSpots = 4;
        /// <summary>Where the preview stands now: 0 beside its medallion away from the trunk, 1 beside it on the trunk's side, 2 above
        /// it, 3 below it (-1: none shown); whether that place is free of panels (false: none of the places is: it stands at the
        /// first); the gap to the nearest panel there (degrees; under 0: over it; 999: no panel near); how often a standing preview
        /// was moved to another place.</summary>
        public int PreviewSpot { get; private set; } = -1;
        public bool PreviewFree { get; private set; }
        public float PreviewGapDeg { get; private set; } = 999f;
        public int PreviewMoves { get; private set; }
        /// <summary>THE FIRST PLAY OF A LOCK: the one moment of a locked skill's preview at which a sound may be made (the brief: "a
        /// sound only on the first play of a lock"). Nobody listens: the preview is silent, as it always was (FLAGGED); a replay
        /// after the rest never asks. How often it was reached (one per lock).</summary>
        public Action<int> PreviewSound;
        public int LockPlays { get; private set; }
        private int _lockPlayed; private float _previewCheck; private bool _previewDirty;
        /// <summary>MEDALLIONS BEHIND A PANEL (client wave 23, item 23): how far round a panel a medallion still counts as covered
        /// (degrees: the laser comes from the hand, not from the eyes); medallions that take no pointer now because a panel stands
        /// over them; the tree the card stands at.</summary>
        public const float CoverMarginDeg = 1.5f;
        public int Covered { get; private set; }
        private struct PanelSpan { public float RefAz, A0, A1, E0, E1; }
        private readonly List<PanelSpan> _coverSpans = new List<PanelSpan>();
        private bool _coverDirty = true; private float _coverCheck; private JobTree _cardTree;
        private readonly List<Transform> _windows = new List<Transform>();
        public LookEye Eye { get; private set; }
        public UiButton BackButton => _back;
        public void SetBackArrow(bool on) { if (_back != null && _back.gameObject.activeSelf != on) _back.gameObject.SetActive(on); }
        /// <summary>The plate, the card and the strip (smoke: where they stand, what they show).</summary>
        public RectTransform Plate => _plate;
        public RectTransform CardPanel => _card;
        public RectTransform NavStrip => _nav;
        public bool CardShown => _card != null && _card.gameObject.activeSelf;
        public bool NavShown => _nav != null && _nav.gameObject.activeSelf;
        public int CardSkill => _cardSkill;
        public UiButton NavBack => _navBack;
        public UiButton NavForward => _navFwd;

        private RectTransform PanelBox(string name, Vector2 size)
        {
            // the panel theme (client wave 21, w21kit: UI/Theme.cs): a framed canvas, as every window; its frame stands outside this rectangle
            var p = UiKit.Box(Root, name, Vector2.zero, size, new Color(0, 0, 0, 0));
            p.localScale = Vector3.one * Px;
            UiKit.Dress(p, size, MetresPerUnit * Px, PanelLook.Framed);
            var col = p.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(size.x, size.y, 4f); col.center = new Vector3(0, 0, 3f); // the laser stops on it (as UiKit.Panel's own)
            p.gameObject.AddComponent<UiBlocker>(); p.gameObject.AddComponent<UiFacet>();
            // client wave 23 (item 23): a canvas of its own, one order above the trees: where it stands over a neighbour tree it is drawn
            // over that tree, not under its medallions
            var cv = p.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = SortingOrder + 1;
            return p;
        }
        /// <summary>The panels' own canvases keep their order above the trees (said again whenever the panels are refreshed: a canvas
        /// switched on again may have lost it).</summary>
        private void PanelsOrder()
        {
            foreach (var p in new[] { _plate, _card, _nav })
            {
                var cv = p != null ? p.GetComponent<Canvas>() : null;
                if (cv != null && (!cv.overrideSorting || cv.sortingOrder != SortingOrder + 1)) { cv.overrideSorting = true; cv.sortingOrder = SortingOrder + 1; }
            }
        }
        private UiButton Btn(RectTransform parent, string label, Vector2 pos, Vector2 size, Action act, int font = 22)
        { var b = UiKit.Button(parent, label, pos, size, act, font); b.Text.resizeTextMinSize = Mathf.Min(font, 20); _panelBtns.Add(b); return b; }

        private void BuildPanels()
        {
            // ---- the plate (its tag buttons are made when the tags are known: BuildTags)
            _plate = PanelBox("plate", new Vector2(PlateW, _plateH));
            _plJob = UiKit.Label(_plate, "", Vector2.zero, new Vector2(PlateW - 40, 44), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _plJob.resizeTextForBestFit = true; _plJob.resizeTextMinSize = 20; _plJob.resizeTextMaxSize = 28;
            _plPoints = UiKit.Label(_plate, "", Vector2.zero, new Vector2(PlateW - 40, 34), 22, TextAnchor.MiddleLeft);
            _plPoints.resizeTextForBestFit = true; _plPoints.resizeTextMinSize = 20; _plPoints.resizeTextMaxSize = 22;
            _plFilters = UiKit.Label(_plate, Lang.T("Filters"), Vector2.zero, new Vector2(PlateW - 190, 40), 22, TextAnchor.MiddleLeft, UiKit.Dim);
            _plClear = Btn(_plate, Lang.T("Clear"), Vector2.zero, new Vector2(130, 48), ClearFilters, 21);
            _plElement = UiKit.Label(_plate, Lang.T("Element"), Vector2.zero, new Vector2(EleW, 40), 22, TextAnchor.MiddleCenter, UiKit.Dim);
            _plElement.gameObject.SetActive(false);
            _plPlan = Btn(_plate, Lang.T("~skillplan~Plan"), Vector2.zero, new Vector2(110, 60), () => { PlanToggle?.Invoke(); _dirty = true; }, 21);
            _plReset = Btn(_plate, Lang.T("~skillplan~Reset"), Vector2.zero, new Vector2(110, 60), () => { PlanReset?.Invoke(); _dirty = true; }, 21);
            _plApply = Btn(_plate, "", Vector2.zero, new Vector2(130, 60), () => { PlanApply?.Invoke(); _dirty = true; }, 21);
            _plMemo = Btn(_plate, Lang.T("Memo here"), Vector2.zero, new Vector2(PlateW - 40, 56), () => Memo?.Invoke(), 21);
            _plQuick = Btn(_plate, Lang.T("Quick bar"), Vector2.zero, new Vector2(140, 64), () => QuickBar?.Invoke(), 21);
            _plFlat = Btn(_plate, Lang.T("Flat page"), Vector2.zero, new Vector2(140, 64), () => FlatPage?.Invoke(), 21);
            _plX = UiKit.RoundButton(_plate, "X", Vector2.zero, new Vector2(72, 64), () => CloseClicked?.Invoke(), 30); _panelBtns.Add(_plX);
            _back = MenuNav.MakeArrow(_plate, new Vector2(PlateW, _plateH), new Vector2(120, 104)); _panelBtns.Add(_back);
            Eye = LookEye.Add(Root, () => LookKey(), Vector2.zero, 48f);
            Eye.Button.transform.SetParent(_plate, false); _panelBtns.Add(Eye.Button);

            // ---- the card
            _card = PanelBox("card", new Vector2(CardW, CardH));
            float top = CardH / 2, w = CardW - 40;
            _cdIcon = UiKit.Icon(_card, new Vector2(-CardW / 2 + 52, top - 52), new Vector2(64, 64));
            _cdName = UiKit.Label(_card, "", new Vector2(8, top - 40), new Vector2(w - 170, 44), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _cdName.resizeTextForBestFit = true; _cdName.resizeTextMinSize = 20; _cdName.resizeTextMaxSize = 28;
            _cdX = UiKit.RoundButton(_card, "X", new Vector2(CardW / 2 - 50, top - 46), new Vector2(72, 64), () => Select(0), 30); _panelBtns.Add(_cdX);
            _cdLevel = UiKit.Label(_card, "", new Vector2(8, top - 78), new Vector2(w - 170, 30), 21, TextAnchor.MiddleLeft, UiKit.Dim);
            _cdLevel.resizeTextForBestFit = true; _cdLevel.resizeTextMinSize = 18; _cdLevel.resizeTextMaxSize = 21;
            // client wave 23 (w23tree), design note: "what the skill does should be first and how to get it should be underneath it": the same
            // parts, the same sizes, in the other order: tags, description, SP / range by level; a rule; state, prerequisites, quest
            _cdTags = UiKit.Label(_card, "", new Vector2(0, top - 124), new Vector2(w, 56), 21, TextAnchor.UpperLeft);
            UiKit.Box(_card, "descbg", new Vector2(0, top - 283), new Vector2(w, 250), new Color(0, 0, 0, 0.35f));
            _cdDesc = UiScrollText.Create(_card, new Vector2(0, top - 283), new Vector2(w - 8, 240), 21);
            _cdPer = UiKit.Label(_card, "", new Vector2(0, top - 441), new Vector2(w, 54), 20, TextAnchor.UpperLeft, UiKit.Dim);
            _cdRule = UiKit.Box(_card, "rule", new Vector2(0, top - 475), new Vector2(w, 2), UiKit.PanelBorder * 0.7f);
            _cdWhy = UiKit.Label(_card, "", new Vector2(0, top - 510), new Vector2(w, 56), 21, TextAnchor.MiddleLeft);
            _cdNeeds = UiKit.Label(_card, "", new Vector2(0, top - 584), new Vector2(w, 84), 21, TextAnchor.UpperLeft);
            _cdQuest = UiKit.Label(_card, "", new Vector2(0, top - 680), new Vector2(w, 100), 21, TextAnchor.UpperLeft, new Color(0.75f, 0.9f, 1f));
            foreach (var t in new[] { _cdWhy, _cdNeeds, _cdTags, _cdQuest, _cdPer }) { t.resizeTextForBestFit = true; t.resizeTextMinSize = 17; t.resizeTextMaxSize = t.fontSize; }
            _cdDesc.Text.name = "text:scrolls";
            float bw = (w - 3 * 8) / 4f, by = -top + 52; float X(int i) => -w / 2 + bw / 2 + i * (bw + 8);
            _cdUp = Btn(_card, Lang.T("+ Level up"), new Vector2(X(0), by), new Vector2(bw, 72), () => Do("up"), 21);
            _cdPin = Btn(_card, Lang.T("★ Quick"), new Vector2(X(1), by), new Vector2(bw, 72), () => Do("pin"), 21);
            _cdLv = Btn(_card, "Lv", new Vector2(X(2), by), new Vector2(bw, 72), () => Do("lv"), 21);
            _cdUse = Btn(_card, Lang.T("Use"), new Vector2(X(3), by), new Vector2(bw, 72), () => Do("use"), 22);
            _cdHint = UiKit.Label(_card, Lang.T("Trigger: select"), new Vector2(0, by), new Vector2(w, 60), 22, TextAnchor.MiddleCenter, UiKit.Dim);
            _panelBtns.Add(_cdDesc.Area);
            _card.gameObject.SetActive(false);

            // ---- the strip over the tree (one at a time): back, the job's name and a dot per tree, forward
            _nav = PanelBox("nav", new Vector2(NavW, NavH));
            _navBack = UiKit.RoundButton(_nav, "<", new Vector2(-NavW / 2 + 56, 0), new Vector2(80, 72), () => Flip(-1), 34); _panelBtns.Add(_navBack);
            _navFwd = UiKit.RoundButton(_nav, ">", new Vector2(NavW / 2 - 56, 0), new Vector2(80, 72), () => Flip(1), 34); _panelBtns.Add(_navFwd);
            _navName = UiKit.Label(_nav, "", new Vector2(0, 18), new Vector2(NavW - 230, 44), 28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            _navName.resizeTextForBestFit = true; _navName.resizeTextMinSize = 20; _navName.resizeTextMaxSize = 28;
            _navDots = UiKit.Label(_nav, "", new Vector2(0, -24), new Vector2(NavW - 230, 32), 21, TextAnchor.MiddleCenter, UiKit.Dim);
            _nav.gameObject.SetActive(false);
        }

        /// <summary>The selected skill's button: the flat page's own action.</summary>
        private void Do(string what)
        {
            if (Selected == 0 || Act == null) return;
            Debug.Log($"[Tree] {what}: {Selected} {SkillDb.Get(Selected).Aegis}");
            Act(Selected, what); _dirty = true;
        }

        /// <summary>The plate's tag buttons, for the tags as they are now (once; again if the list changed).</summary>
        private void BuildTags()
        {
            var all = SkillTags.All; if (all.Count == _tagsBuilt) return;
            foreach (var b in _tagBtns) { _panelBtns.Remove(b); if (b != null) DestroyObj(b.gameObject); }
            _tagBtns.Clear(); _tagIds.Clear(); _tagsBuilt = all.Count;
            foreach (var t in all)
            {
                string id = t.Id;
                var b = Btn(_plate, Lang.T(t.NameKey), Vector2.zero, new Vector2((PlateW - 48) / 2f, TagH), () => ToggleFilter(id), 21);
                b.name = "btn:tag:" + id; b.Text.color = TagText(t.Color);
                _tagBtns.Add(b); _tagIds.Add(id);
            }
            LayPlate();
        }
        /// <summary>The plate's Element buttons: one for each element the character's line can inflict, no other (a line that inflicts
        /// none, a Novice: no column). Made again when the line's elements change (a job change).</summary>
        private void BuildElements()
        {
            var offered = OfferedElements();
            string key = string.Join(",", offered.ConvertAll(e => e.Id));
            if (key == _eleKey) return;
            _eleKey = key;
            foreach (var b in _eleBtns) { _panelBtns.Remove(b); if (b != null) DestroyObj(b.gameObject); }
            _eleBtns.Clear(); _eleIds.Clear();
            foreach (var e in offered)
            {
                string id = e.Id;
                var b = Btn(_plate, Lang.T(e.NameKey), Vector2.zero, new Vector2(EleW, TagH), () => ToggleFilter(id), 21);
                b.name = "btn:" + id; b.Text.color = TagText(e.Color);
                _eleBtns.Add(b); _eleIds.Add(id);
            }
            // an element chosen before that the line no longer has: not left chosen with no button to take it off
            if (_filters.RemoveWhere(x => x.StartsWith("ele:", StringComparison.Ordinal) && !_eleIds.Contains(x)) > 0) Redraw();
            _plElement.gameObject.SetActive(_eleBtns.Count > 0);
            Debug.Log($"[Tree] the Element filter offers: {(offered.Count == 0 ? "nothing (no column)" : string.Join(", ", offered.ConvertAll(e => e.Name)))}");
            LayPlate(); LayoutPanels();
        }
        /// <summary>A tag's colour as its text on the dark panel (lightened until it reads: UI/UiKit.cs ReadableLuminance, 7 : 1).</summary>
        public static Color TagText(Color c)
        {
            for (int i = 0; i <= 20 && UiKit.Luminance(c) < UiKit.ReadableLuminance; i++) c = Color.Lerp(c, Color.white, 0.08f);
            c.a = 1f; return c;
        }

        /// <summary>Places the plate's parts from its top and gives it the height they need.</summary>
        private void LayPlate()
        {
            int rows = Mathf.Max((_tagBtns.Count + 1) / 2, _eleBtns.Count); bool memo = _plMemo.gameObject.activeSelf;
            float h = 20 + 44 + 34 + 12 + 48 + 8 + rows * TagPitch + 10 + 60 + 10 + (memo ? 66 : 0) + 64 + 20;
            float W = PlateWidth, x0 = -(W - PlateW) / 2f; // (the tags and the rows under them keep the left 400; the Element column is right of them)
            _plateH = h; _plate.sizeDelta = new Vector2(W, h);
            // (the theme's frame and canvas are made for a size: dressed again for the new height)
            foreach (var part in new[] { "border", "bg" }) { var old = _plate.Find(part); if (old != null) { old.name = "old"; old.gameObject.SetActive(false); DestroyObj(old.gameObject); } }
            UiKit.Dress(_plate, new Vector2(W, h), MetresPerUnit * Px, PanelLook.Framed);
            var col = _plate.GetComponent<BoxCollider>(); col.size = new Vector3(W, h, 4f);
            float y = h / 2 - 20; float w = PlateW - 40;
            void Put(Component c, float x, float hh) { ((RectTransform)c.transform).anchoredPosition = new Vector2(x0 + x, y - hh / 2); }
            Put(_plJob, 0, 44); y -= 44; Put(_plPoints, 0, 34); y -= 34 + 12;
            float ex = W / 2 - 20 - EleW / 2 - x0; // (the Element column's middle, as Put counts)
            Put(_plFilters, -75, 48); Put(_plClear, w / 2 - 65, 48); Put(_plElement, ex, 48); y -= 48 + 8;
            for (int i = 0; i < _tagBtns.Count; i++)
            {
                int c = i % 2, r = i / 2; float bw = (PlateW - 48) / 2f;
                ((RectTransform)_tagBtns[i].transform).anchoredPosition = new Vector2(x0 + (c == 0 ? -bw / 2 - 4 : bw / 2 + 4), y - r * TagPitch - TagH * 0.5f);
            }
            for (int i = 0; i < _eleBtns.Count; i++)
                ((RectTransform)_eleBtns[i].transform).anchoredPosition = new Vector2(x0 + ex, y - i * TagPitch - TagH * 0.5f);
            y -= rows * TagPitch + 10;
            Put(_plPlan, -w / 2 + 55, 60); Put(_plReset, -w / 2 + 55 + 118, 60); Put(_plApply, w / 2 - 65, 60); y -= 60 + 10;
            if (memo) { Put(_plMemo, 0, 56); y -= 66; }
            Put(_plQuick, -w / 2 + 70, 64); Put(_plFlat, -w / 2 + 70 + 148, 64); Put(_plX, w / 2 - 36, 64);
            ((RectTransform)_back.transform).anchoredPosition = new Vector2(-W / 2 - 8 - 60, h / 2 - 52);
            ((RectTransform)Eye.Button.transform).anchoredPosition = new Vector2(W / 2 - 21 - 24, h / 2 - 21 - 24);
            ((RectTransform)_plJob.transform).sizeDelta = new Vector2(w - 64, 44); ((RectTransform)_plJob.transform).anchoredPosition += new Vector2(-32, 0);
        }

        /// <summary>Where the plate and the card stand: on the circle round the eyes at the trees' distance, past the left and the right
        /// edge of the tree they serve, turned to the eyes; the strip over that tree's top (one at a time).</summary>
        private void LayoutPanels()
        {
            if (_plate == null) return;
            _previewDirty = true; // (client wave 23: a panel may now stand where the preview is)
            { float before = ArcHalf; LayArc(); if (Mathf.Abs(ArcHalf - before) > 0.01f) LayoutTrees(); } // (the plate is a piece of the arc: its width changed, so the trees stand anew)
            var ft = FocusTree; float D = _dist;
            float a0 = Around && ft != null ? ft.Azimuth * Mathf.Deg2Rad : 0f;
            float edge = Mathf.Atan((ft != null && ft.Shape != null ? ft.Shape.HalfWidth : 150f) / D), gap = PanelGapDeg * Mathf.Deg2Rad;
            float grown = ft != null && ft.Ring == 0 ? SproutsWidth() * Mathf.Deg2Rad : 0f; // (what has grown out of a node stands left of the first job's tree)
            void Stand(RectTransform p, float wPx, float hPx, int side, float mid0, float reach, float extra)
            {
                float half = Mathf.Atan((wPx * 0.5f + UiKit.OutsetOf(p)) * Px / D), a = mid0 + side * (reach + extra + gap + half); // (with the theme's frame, which stands outside the rectangle)
                p.localPosition = new Vector3(D * Mathf.Sin(a), PanelTop - hPx * Px * 0.5f, D * Mathf.Cos(a));
                p.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
            }
            // client wave 23 (item 23): around you with two trees or more THE PLATE has the arc's right end (right of the last tree, the
            // current job's: over no tree); else, as in wave 22, it stands left of the tree shown
            bool atEnd = PlateAtEnd; var lastTree = atEnd ? RingTree(RingCount - 1) : null;
            if (lastTree != null && lastTree.Shape != null) Stand(_plate, PlateWidth, _plateH, 1, lastTree.Azimuth * Mathf.Deg2Rad, Mathf.Atan(lastTree.Shape.HalfWidth / D), 0f);
            else Stand(_plate, PlateWidth, _plateH, -1, a0, edge, grown);
            // THE CARD stands at the tree of ITS skill (selected, else under a pointer): right of it as in wave 22, but, with the plate at
            // the arc's end, LEFT of it: over the tree before it (never over the tree its own medallion hangs on, nor over the plate),
            // left of what has grown out of a node when it is the first job's tree
            var ct = Around && _cardTree != null && _cardTree.Active && !_cardTree.Extra && _cardTree.Shape != null ? _cardTree : ft;
            float cMid = Around && ct != null ? ct.Azimuth * Mathf.Deg2Rad : 0f, cReach = Mathf.Atan((ct != null && ct.Shape != null ? ct.Shape.HalfWidth : 150f) / D);
            if (lastTree != null) Stand(_card, CardW, CardH, -1, cMid, cReach, ct != null && ct.Ring == 0 ? SproutsWidth() * Mathf.Deg2Rad : 0f);
            else Stand(_card, CardW, CardH, 1, cMid, cReach, 0f);
            _coverDirty = true;
            bool nav = !Around && RingCount > 1 && ft != null;
            if (_nav.gameObject.activeSelf != nav) _nav.gameObject.SetActive(nav);
            if (nav)
            {
                _nav.localPosition = new Vector3(0f, ft.Lift + ft.Shape.Top + NavGap + NavH * Px * 0.5f, D);
                _nav.localRotation = Quaternion.identity;
            }
        }

        /// <summary>The plate, the card and the strip for the trees as they are now.</summary>
        public void RefreshPanels()
        {
            if (_plate == null) return;
            BuildTags(); BuildElements();
            int job = Job != null ? Job() : 0, sex = Sex != null ? Sex() : 1, pts = SkillPoints != null ? SkillPoints() : 0;
            bool planning = PlanMode != null && PlanMode(); int cost = PlanCost != null ? PlanCost() : 0;
            _plJob.text = SkillTreeDb.JobName(job, sex);
            string points = pts > 1 ? Lang.F("<color=#7fd67f>{0} skill points</color>", pts) : pts == 1 ? Lang.T("<color=#7fd67f>1 skill point</color>") : Lang.T("<color=#9aa3b5>no skill points</color>");
            if (planning && cost > 0) points = Lang.F("<color=#8fc4ff>{0} / {1} skill points left</color>", pts - cost, pts);
            _plPoints.text = Lang.F("Job Lv {0}", JobLevel != null ? JobLevel() : 1) + "  ·  " + points;
            void Paint(UiButton b, Color c, bool on)
            {
                b.Normal = on ? new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f) : new Color(0.11f, 0.13f, 0.19f, 1f);
                b.Text.color = on ? Color.white : TagText(c);
                if (!b.Hovered) b.SetHover(false);
            }
            for (int i = 0; i < _tagBtns.Count; i++) { var tag = TagOf(_tagIds[i]); Paint(_tagBtns[i], tag != null ? tag.Color : Color.gray, _filters.Contains(_tagIds[i])); }
            for (int i = 0; i < _eleBtns.Count; i++) { var e = SkillElements.Get(_eleIds[i]); Paint(_eleBtns[i], e != null ? e.Color : Color.gray, _filters.Contains(_eleIds[i])); }
            bool clear = _filters.Count > 0; if (_plClear.gameObject.activeSelf != clear) _plClear.gameObject.SetActive(clear);
            _plFilters.text = _filters.Count == 0 ? Lang.T("Filters") : Lang.F("Filters: {0}", _filters.Count);
            // the planned points (the flat page's: only with skill points)
            bool canPlan = PlanToggle != null && (pts > 0 || planning);
            if (_plPlan.gameObject.activeSelf != canPlan) _plPlan.gameObject.SetActive(canPlan);
            _plPlan.SetLabel(planning ? Lang.T("~skillplan~Planning ✓") : Lang.T("~skillplan~Plan"));
            _plPlan.Normal = planning ? new Color(0.2f, 0.36f, 0.6f) : UiKit.ButtonBg; if (!_plPlan.Hovered) _plPlan.SetHover(false);
            bool plan = planning && cost > 0;
            if (_plReset.gameObject.activeSelf != plan) _plReset.gameObject.SetActive(plan);
            if (_plApply.gameObject.activeSelf != plan) _plApply.gameObject.SetActive(plan);
            if (plan) _plApply.SetLabel(cost == 1 ? Lang.T("Apply 1 point") : Lang.F("Apply {0} points", cost));
            bool memo = Memo != null && _prog != null && _prog.Skills.TryGetValue(27, out var warp) && warp.Level > 0;
            if (_plMemo.gameObject.activeSelf != memo) { _plMemo.gameObject.SetActive(memo); LayPlate(); LayoutPanels(); }
            RefreshNav();
            RefreshCard();
            PanelsOrder();
        }

        /// <summary>The strip: the job of the tree shown, a dot per tree (the one shown filled) and its place ("2 / 3"); the button at
        /// an end cannot be pressed.</summary>
        private void RefreshNav()
        {
            var ft = FocusTree; if (_nav == null || ft == null) return;
            _navName.text = ft.Name;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < RingCount; i++) sb.Append(i == ft.Ring ? "●" : "○").Append(' ');
            sb.Append("  ").Append(ft.Ring + 1).Append(" / ").Append(RingCount);
            _navDots.text = sb.ToString();
            _navBack.Interactable = ft.Ring > 0; _navFwd.Interactable = ft.Ring < RingCount - 1;
            _navBack.SetHover(_navBack.Hovered); _navFwd.SetHover(_navFwd.Hovered);
        }

        private void RefreshCard()
        {
            int skill = Selected != 0 ? Selected : Hover;
            bool show = skill != 0 && Card != null && TreeOf(skill) != null;
            var cardTree = show ? TreeOf(skill) : null;
            if (cardTree != _cardTree) { _cardTree = cardTree; LayoutPanels(); } // (client wave 23: the card stands at its own skill's tree)
            if (_card.gameObject.activeSelf != show) { _card.gameObject.SetActive(show); _previewDirty = true; _coverDirty = true; }
            if (!show) { _cardSkill = 0; return; }
            var d = Card(skill); if (d == null) { _card.gameObject.SetActive(false); _cardSkill = 0; return; }
            bool sel = skill == Selected;
            _cdIcon.sprite = d.Icon; _cdIcon.enabled = d.Icon != null; _cdIcon.color = d.Locked ? new Color(1, 1, 1, 0.45f) : Color.white;
            _cdName.text = d.Name; _cdLevel.text = d.Level; _cdWhy.text = d.Why; _cdNeeds.text = d.Needs; _cdPer.text = d.PerLevel;
            // its tags, each in its colour
            var sb = new System.Text.StringBuilder();
            foreach (var id in SkillTags.For(skill))
            {
                var t = TagOf(id); if (t == null) continue;
                if (sb.Length > 0) sb.Append("   ");
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(TagText(t.Color))).Append(">● ").Append(Lang.T(t.NameKey)).Append("</color>");
            }
            _cdTags.text = sb.ToString();
            // a quest skill: its quest and how to start it
            var q = SkillQuests.For(skill);
            _cdQuest.text = q != null ? Lang.F("Quest: {0}", Lang.T(q.QuestName)) + "\n" + SkillQuests.Text(skill) : ""; // (how to start it and what it asks for: Game/SkillQuests.cs)
            _cdDesc.Text.text = d.Lines.Count == 0 ? Lang.T("<color=#8890a0>No description in the client data.</color>") : string.Join("\n", d.Lines);
            if (_cardSkill != skill || _cardScrollReset) { _cdDesc.ScrollTo(0f); _cardScrollReset = false; } else _cdDesc.Measure();
            _cardSkill = skill;
            void Show(UiButton b, bool on) { if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on); }
            Show(_cdUp, sel && d.CanUp); Show(_cdPin, sel && d.CanPin); Show(_cdLv, sel && d.CanLv); Show(_cdUse, sel && d.CanUse); Show(_cdX, sel);
            if (sel && d.CanLv) _cdLv.SetLabel(d.LvLabel);
            if (_cdHint.gameObject.activeSelf == sel) _cdHint.gameObject.SetActive(!sel);
        }

        /// <summary>The plate's, the card's or the strip's button a hand's fingertip is on (null: none): inside its face, within 3 cm in
        /// front of it or 2 cm behind.</summary>
        private UiButton TipButton(int hand)
        {
            if (Tip == null) return null;
            var tip = Tip(hand); if (!tip.HasValue) return null;
            UiButton best = null;
            foreach (var b in _panelBtns)
            {
                if (b == null || !b.isActiveAndEnabled || !b.Interactable) continue;
                var rt = (RectTransform)b.transform; var l = rt.InverseTransformPoint(tip.Value);
                float z = l.z * Mathf.Abs(rt.lossyScale.x);
                if (!rt.rect.Contains(new Vector2(l.x, l.y)) || z < -0.03f || z > 0.02f) continue;
                if (UiButton.Outranks(b, best)) best = b;
            }
            return best;
        }

        private void PanelsTick(float dt)
        {
            // client wave 23 (item 23): the medallions a panel stands over take no pointer (looked at again when something moved, and a few times a second)
            float now = Time.unscaledTime;
            if (_coverDirty || now >= _coverCheck) { _coverDirty = false; _coverCheck = now + PreviewCheckSeconds; Cover(); }
            // THE PREVIEW: only shown when the skill medallion is clicked / selected (design note: "make it so that the preview only happens when I click on the image itself, otherwise the character preview doesn't appear")
            int want = Selected;
            JobTree tree = null; var n = want != 0 ? NodeOf(want, out tree) : null;
            if (n == null || tree == null || !tree.Active) want = 0;
            // the first play of a lock: told once (a sound's one moment; the preview makes none)
            if (Selected == 0) _lockPlayed = 0;
            else if (want == Selected && _lockPlayed != Selected) { _lockPlayed = Selected; LockPlays++; PreviewSound?.Invoke(Selected); }
            if (want == 0)
            {
                if (_shownPreview != 0 && Diorama != null) Diorama.Hide();
                _shownPreview = 0; PreviewSpot = -1; PreviewGapDeg = 999f;
                return;
            }
            if (want != _shownPreview)
            {
                // another skill: its scene from its start, at the first free place round its medallion
                _shownPreview = want; PreviewSpot = FreeSpot(tree, n, -1); _previewCheck = now + PreviewCheckSeconds; _previewDirty = false;
                if (Diorama != null) Diorama.Show(want, tree.Holder, PreviewAt(n, PreviewSpot));
                Debug.Log($"[Tree] the preview of {want} stands {SpotName(PreviewSpot)} its medallion ({(PreviewFree ? $"{PreviewGapDeg:0.0}° clear of every panel" : "NO place round it is free of panels")}){(want == Selected ? ": locked, it plays on without a pointer" : "")}");
                return;
            }
            if (!_previewDirty && now < _previewCheck) return;
            _previewCheck = now + PreviewCheckSeconds; _previewDirty = false;
            // the same skill: it stays where it stands while that place is free; else the first free place, the scene playing on
            int spot = FreeSpot(tree, n, PreviewSpot);
            if (spot == PreviewSpot && (Diorama == null || Diorama.transform.parent == tree.Holder)) return;
            PreviewSpot = spot; PreviewMoves++;
            if (Diorama != null) Diorama.MoveTo(tree.Holder, PreviewAt(n, spot));
            Debug.Log($"[Tree] the preview of {want} moved {SpotName(spot)} its medallion: a panel came to stand at its place");
        }
        private static string SpotName(int spot) => spot == 0 ? "beside" : spot == 1 ? "beside (the trunk's side of)" : spot == 2 ? "above" : "below";

        /// <summary>Place <paramref name="spot"/> of the preview round a medallion, in its tree's frame (the ground's middle; the scene
        /// stands 70 mm in front of the tree's plane): 0 beside it away from the trunk and a little under it (the place of client
        /// wave 22), 1 the same on the trunk's side, 2 above it, 3 under its level plaque.</summary>
        public Vector3 PreviewAt(TreeNode n, int spot)
        {
            float side = n.Pos.x >= 0f ? 1f : -1f, beside = TreeLayout.NodeR + SkillDiorama.GroundR + PreviewGap;
            switch (spot)
            {
                case 1: return n.Pos + new Vector3(-side * beside, -22f, -70f);
                case 2: return n.Pos + new Vector3(0f, TreeLayout.NodeR + PreviewGap - SkillDiorama.BoxBottom, -70f);
                case 3: return n.Pos + new Vector3(0f, -(TreeLayout.NodeR + TreeLayout.PlaqueGap + TreeLayout.PlaqueH + PreviewGap) - SkillDiorama.BoxTop, -70f);
                default: return n.Pos + new Vector3(side * beside, -22f, -70f);
            }
        }

        /// <summary>The place the preview takes: <paramref name="current"/> while it is free (a standing preview does not hop to a
        /// place that is merely earlier in the order), else the first free one of the order; none free: the current one, else the first.</summary>
        private int FreeSpot(JobTree tree, TreeNode n, int current)
        {
            PanelKinds.OpenMenus(_windows, transform); // (every open window but the trees themselves)
            if (current >= 0 && PreviewFreeAt(tree, PreviewAt(n, current), out float gap)) { PreviewFree = true; PreviewGapDeg = gap; return current; }
            for (int s = 0; s < PreviewSpots; s++)
                if (s != current && PreviewFreeAt(tree, PreviewAt(n, s), out gap)) { PreviewFree = true; PreviewGapDeg = gap; return s; }
            int keep = current >= 0 ? current : 0;
            PreviewFree = false; PreviewFreeAt(tree, PreviewAt(n, keep), out gap); PreviewGapDeg = gap;
            return keep;
        }

        /// <summary>Where the player's eyes are now (the head; without one, where they were when the trees opened).</summary>
        private Vector3 EyesNow => _head != null ? _head.transform.position : Root.position - Vector3.up * VR.Comfort.BoardHeight;

        /// <summary>What a rectangle of <paramref name="space"/> takes as the eyes see it: degrees right of the direction
        /// <paramref name="refAz"/> (a0 to a1) and above eye level (e0 to e1), from nine of its points (corners, the edges' middles,
        /// the middle). Measured about its own middle first, so a rectangle behind the player does not wrap round.</summary>
        private void EyeSpan(Transform space, float x0, float x1, float y0, float y1, float z, Vector3 eye, float refAz, out float a0, out float a1, out float e0, out float e1)
        {
            var mid = Root.InverseTransformDirection(space.TransformPoint(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z)) - eye);
            float midAz = Mathf.Atan2(mid.x, mid.z) * Mathf.Rad2Deg, shift = Mathf.DeltaAngle(refAz, midAz);
            a0 = e0 = float.MaxValue; a1 = e1 = float.MinValue;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    var d = Root.InverseTransformDirection(space.TransformPoint(new Vector3(Mathf.Lerp(x0, x1, i * 0.5f), Mathf.Lerp(y0, y1, j * 0.5f), z)) - eye);
                    float az = shift + Mathf.DeltaAngle(midAz, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), el = Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg;
                    if (az < a0) a0 = az; if (az > a1) a1 = az; if (el < e0) e0 = el; if (el > e1) e1 = el;
                }
        }

        /// <summary>The preview's box (UI/SkillDiorama.cs BoxHalfW / BoxBottom / BoxTop) with its ground's middle at
        /// <paramref name="at"/> of a tree's frame is FREE: as the eyes see it, it is <see cref="PreviewClearDeg"/> clear of the plate,
        /// the card, the strip (while shown) and of every open window (with their frames), and its bottom is not in the game's table
        /// (under the clearance the trees' floors keep: <see cref="Clearance"/>). <paramref name="gap"/>: how far the nearest panel
        /// is (degrees; under 0: the box is over it; 999: no panel near).</summary>
        private bool PreviewFreeAt(JobTree tree, Vector3 at, out float gap)
        {
            gap = 999f; var eye = EyesNow; var h = tree.Holder; bool free = true;
            if (GroundNow.HasValue && h.TransformPoint(new Vector3(at.x, at.y + SkillDiorama.BoxBottom, at.z)).y < GroundNow.Value + Clearance - 0.001f) free = false;
            var mid = Root.InverseTransformDirection(h.TransformPoint(new Vector3(at.x, at.y + (SkillDiorama.BoxBottom + SkillDiorama.BoxTop) * 0.5f, at.z)) - eye);
            float refAz = Mathf.Atan2(mid.x, mid.z) * Mathf.Rad2Deg;
            EyeSpan(h, at.x - SkillDiorama.BoxHalfW, at.x + SkillDiorama.BoxHalfW, at.y + SkillDiorama.BoxBottom, at.y + SkillDiorama.BoxTop, at.z, eye, refAz, out float b0, out float b1, out float c0, out float c1);
            void Against(RectTransform p, ref float nearest, ref bool ok)
            {
                if (p == null || !p.gameObject.activeInHierarchy) return;
                float o = UiKit.OutsetOf(p); var r = p.rect;
                EyeSpan(p, r.xMin - o, r.xMax + o, r.yMin - o, r.yMax + o, 0f, eye, refAz, out float p0, out float p1, out float q0, out float q1);
                // apart sideways or apart in height by this much: the larger of the two is the gap (under 0: over it both ways)
                float g = Mathf.Max(Mathf.Max(p0 - b1, b0 - p1), Mathf.Max(q0 - c1, c0 - q1));
                if (g < nearest) nearest = g;
                if (g < PreviewClearDeg) ok = false;
            }
            Against(_plate, ref gap, ref free); Against(_card, ref gap, ref free); Against(_nav, ref gap, ref free);
            foreach (var w in _windows) Against(w as RectTransform, ref gap, ref free);
            return free;
        }

        /// <summary>THE LOCK IS TAKEN OFF (client wave 23, w23tree; design note 2026-10-11: "if I click on nothing while this skill tree is
        /// open, it should remove any locked in skill. Like it should remove the lock"): nothing is selected, so the card closes and
        /// the preview ends (a pointer still resting on a medallion keeps that medallion's). False: nothing was locked.</summary>
        public bool Unlock(string by)
        {
            if (Selected == 0) return false;
            int was = Selected; Unlocks++;
            Select(0);
            Debug.Log($"[Tree] unlocked {was} {SkillDb.Get(was).Aegis}: {by}");
            return true;
        }
        /// <summary>Locks taken off by a pull on nothing (smoke, log).</summary>
        public int Unlocks { get; private set; }

        /// <summary>A point of the world is, as the eyes see it, on the plate, the card or the strip (within its rectangle and frame;
        /// a panel may stand over a neighbour tree: what the player sees there is the panel): a pull there is that panel's own (its
        /// empty canvas takes no lock off: FLAGGED).</summary>
        public bool OnPanel(Vector3 world)
        {
            var eye = EyesNow; var d = Root.InverseTransformDirection(world - eye);
            float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, el = Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg;
            foreach (var p in new[] { _plate, _card, _nav })
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                float o = UiKit.OutsetOf(p); var r = p.rect;
                EyeSpan(p, r.xMin - o, r.xMax + o, r.yMin - o, r.yMax + o, 0f, eye, az, out float a0, out float a1, out float e0, out float e1);
                if (a0 <= 0f && a1 >= 0f && el >= e0 && el <= e1) return true;
            }
            return false;
        }

        /// <summary>MEDALLIONS BEHIND A PANEL (client wave 23, item 23): a medallion, or a node at the first job's base, whose face
        /// the plate, the card or the strip covers as the eyes see it (<see cref="CoverMarginDeg"/> round the panel counts) takes no
        /// laser, no look and no fingertip while the panel stands there: its button is off (its level text with it: the panel is
        /// drawn over both).</summary>
        private void Cover()
        {
            var eye = EyesNow; _coverSpans.Clear();
            foreach (var p in new[] { _plate, _card, _nav })
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                var mid = Root.InverseTransformDirection(p.position - eye); float refAz = Mathf.Atan2(mid.x, mid.z) * Mathf.Rad2Deg, o = UiKit.OutsetOf(p); var r = p.rect;
                EyeSpan(p, r.xMin - o, r.xMax + o, r.yMin - o, r.yMax + o, 0f, eye, refAz, out float a0, out float a1, out float e0, out float e1);
                _coverSpans.Add(new PanelSpan { RefAz = refAz, A0 = a0 - CoverMarginDeg, A1 = a1 + CoverMarginDeg, E0 = e0 - CoverMarginDeg, E1 = e1 + CoverMarginDeg });
            }
            int covered = 0; var first = RingTree(0);
            foreach (var t in _trees)
            {
                if (t.Shape == null) continue;
                bool shown = t.Active;
                for (int i = 0; i < t.Shape.Nodes.Count && i < t.Btns.Count; i++)
                {
                    if (t.Btns[i] == null) continue;
                    bool hid = shown && Behind(t.Holder, t.Shape.Nodes[i].Pos, TreeLayout.NodeR, eye); if (hid) covered++;
                    if (t.Btns[i].gameObject.activeSelf == hid) t.Btns[i].gameObject.SetActive(!hid);
                }
                if (t.Extra && t.Bud != null && first != null)
                {
                    bool budHid = first.Active && Behind(first.Holder, t.BudAt, BudR, eye);
                    if (t.Bud.gameObject.activeSelf == budHid) t.Bud.gameObject.SetActive(!budHid);
                }
            }
            if (covered != Covered) { Covered = covered; Debug.Log($"[Tree] {covered} medallion(s) stand behind a panel now: they take no pointer"); }
        }
        private bool Behind(Transform space, Vector3 at, float r, Vector3 eye)
        {
            foreach (var s in _coverSpans)
            {
                EyeSpan(space, at.x - r, at.x + r, at.y - r, at.y + r, at.z, eye, s.RefAz, out float b0, out float b1, out float c0, out float c1);
                if (b0 < s.A1 && s.A0 < b1 && c0 < s.E1 && s.E0 < c1) return true;
            }
            return false;
        }

    }
}
