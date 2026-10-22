using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Wave 28 (wcodex): THE CODEX, a physical book (design note, 2026-10-15: "a Codex Menu on the wrist menu called "Codex", that spawns a
    /// physical book in front of you for you to read that contains every item and creature you've encountered so far"; "The book should
    /// open on an index, and opening another page should play a quick animation of the pages flipping through quickly and opening on the
    /// chosen page. I can toggle this animation on and off on the settings, and reopening the same page again quickly skips the
    /// animation"; "The codex should remember what page it was when you last closed it for 30s, and reopen to it if you reopen the codex").
    ///   The book   two parchment pages open in a shallow V (<see cref="FoldDeg"/> each, toward the reader) on leather covers, a spine
    ///              between them; it appears <see cref="Distance"/> m ahead of the eyes and <see cref="Below"/> m under them, facing them
    ///              (as a board is placed), and it stays where it is: the laser with the grip, or a hand at its edge, takes it
    ///              (UiGrabbable, UI/PanelGrab.cs). Its pages are one panel ("CodexBook": a menu for the casting and the look rules).
    ///   Opening    the closed book swells in, its cover swings open and the pages come up (<see cref="OpenSeconds"/>; the menu
    ///              animations' switch "Codex" and their 5 s rule: UI/MenuAnim.cs). Closing swings the cover shut (<see cref="CloseSeconds"/>).
    ///   Pages      the INDEX: a title page (the two parts with their counts, the last entries written) and lists of the part's entries
    ///              (<see cref="Rows"/> a page, as many pages as needed); an ENTRY: a creature's or an item's two pages. ‹ and › turn one
    ///              page (one sheet lifts and turns over, <see cref="TurnSeconds"/>); a list row, a part, a recent entry, a drop or a source,
    ///              and "Index" JUMP: several sheets riffle past (<see cref="RiffleSheets"/> over <see cref="RiffleSeconds"/>) and the book
    ///              lands on the chosen page, unless Settings → Comfort → Effects "Codex flip-through" is off, or that page was shown
    ///              less than <see cref="SameWindow"/> s ago (index → entry → index → the same entry: at once).
    ///   Reopening  within <see cref="RememberSeconds"/> s of closing: the page it was on, at once; later: the index (design note, 2026-10-15: "Lower
    ///              the reopen grace period duration to 10s from 30s"). A creature NEWLY written (by a look or a kill): opened within
    ///              <see cref="NewEntrySeconds"/> s, the book opens on its page ("When you scan a new creature, if you open the codex within 7s,
    ///              it opens directly on that creature's page"); when both apply, the newer event wins (FLAGGED).
    ///   The Bestiarium lists EVERY creature a player can meet (CodexData.MetableIds) and those written down, by level then name; one not
    ///              written down is obscured (UI/CodexBook.Pages.cs DrawList) and cannot be opened.
    ///   Two sizes  (design note, 2026-10-15: "Provide the option to spawn the Codex as an actual book-sized book that I grab with my hands. This
    ///              should be the default version"; Settings → Comfort → Effects "Codex book": Hand-held / Large, World/UserSettings.cs
    ///              codexLarge, read at each opening). HAND-HELD (the default): the same pages at <see cref="HandMetresPerPx"/> (a page
    ///              20.0 x 31.1 cm, the two pages 41 cm across, its smallest text 8.9 mm: 1.3° at 39 cm), <see cref="HandAhead"/> m ahead
    ///              of the eyes and <see cref="HandBelow"/> m under them (in front of the chest, 34 cm from the eyes), facing them; a hand
    ///              takes it by its edge and it follows that hand with the hand's full turn (a book in the hand), the other hand takes it
    ///              over, let go it floats where it is (LokiGame.W13Grab.cs; LokiGame.WCodex.cs WCodexPassAt), and the free hand's
    ///              fingertip presses what it touches on the pages (the corner arrows turn a page: LokiGame.WCodex.cs UpdateWCodexTouch).
    ///              LARGE: the book as it was (<see cref="MetresPerPx"/>, <see cref="Distance"/> m ahead).
    /// Every number here is Loki's (no source says how a VR book reads): FLAGGED in design notes
    /// </summary>
    public sealed partial class CodexBook : MonoBehaviour
    {
        public const float PageW = 540f, PageH = 840f, Gutter = 28f, MetresPerPx = 0.00056f, FoldDeg = 9f;
        public const float Distance = 0.83f, Below = 0.12f;
        /// <summary>The hand-held book (FLAGGED): metres per pixel, where it appears (ahead of / under the eyes), and how near the eyes it may
        /// be let go before it eases out (LokiGame.W15Grab.cs: other panels 0.45 m; design note 2026-10-17 item 26: pushed further out from the face;
        /// item 60: pushed out further by 1.5x of that previous push).</summary>
        public const float HandMetresPerPx = 0.00037f, HandAhead = 0.65f, HandBelow = 0.20f, HandMinFromEyes = 0.58f;
        /// <summary>The player chose the hand-held book (the default) / the large one: Settings → Comfort → Effects "Codex book".</summary>
        public static bool HandHeldChosen => !World.UserSettings.Prefs.codexLarge;
        /// <summary>The book out now is the hand-held one (set at each opening).</summary>
        public bool HandMode { get; private set; }
        /// <summary>Metres per pixel of the book out now.</summary>
        public float Scale => HandMode ? HandMetresPerPx : MetresPerPx;
        public RectTransform LeftPage => _left.Box;
        public RectTransform RightPage => _right.Box;
        public const float OpenGrow = 0.08f, OpenSwing = 0.30f, OpenFade = 0.10f, CloseSwing = 0.25f, CloseShrink = 0.06f;
        public static float OpenSeconds => OpenGrow + OpenSwing + OpenFade;
        public static float CloseSeconds => CloseSwing + CloseShrink;
        public const float TurnSeconds = 0.32f;
        public const int RiffleSheets = 7; public const float RiffleStagger = 0.055f, RiffleSheet = 0.24f;
        public static float RiffleSeconds => (RiffleSheets - 1) * RiffleStagger + RiffleSheet;
        public const float SameWindow = 8f, RememberSeconds = 10f, NewEntrySeconds = 60f;
        /// <summary>Rows a list page: 8 of <see cref="RowH"/> px every <see cref="RowPitch"/> px (9 of 58 before the pictures grew: the
        /// orchestrator, 22:15, after the design's concept: a picture that reads as the creature's shape).</summary>
        public const int Rows = 8; public const float RowH = 66f, RowPitch = 72f;
        public const string AnimKey = "codex";
        public const float Margin = 34f;

        public enum Kind { Index, Creature, Item }
        public enum Section { Bestiarium, Itemarium }
        /// <summary>A spread of the book: an index spread of a part, or an entry's.</summary>
        public struct PageRef : IEquatable<PageRef>
        {
            public Kind Kind; public Section Section; public int Spread; public int Id;
            public static PageRef Index(Section s, int spread) => new PageRef { Kind = Kind.Index, Section = s, Spread = Mathf.Max(0, spread) };
            public static PageRef Creature(int id) => new PageRef { Kind = Kind.Creature, Section = Section.Bestiarium, Id = CodexData.CanonicalId(id) };
            public static PageRef Item(int id) => new PageRef { Kind = Kind.Item, Section = Section.Itemarium, Id = id };
            public string Key => Kind == Kind.Index ? $"index:{Section}:{Spread}" : Kind == Kind.Creature ? $"creature:{Id}" : $"item:{Id}";
            public bool Equals(PageRef o) => Kind == o.Kind && Section == o.Section && Spread == o.Spread && Id == o.Id;
            public override bool Equals(object o) => o is PageRef p && Equals(p);
            public override int GetHashCode() => Key.GetHashCode();
            public override string ToString() => Key;
        }

        private sealed class Page { public RectTransform Box, Content; public CanvasGroup Group; }
        public RectTransform Root { get; private set; }
        public UiGrabbable Grab { get; private set; }
        private Page _left, _right;
        private Transform _hingeL, _hingeR;
        private Func<Camera> _head;
        private CodexJournal _journal;
        private UiButton _x, _prev, _next, _index, _bestiarium, _back;
        public UiButton CloseButton => _x;
        public UiButton PrevButton => _prev;
        public UiButton NextButton => _next;
        public UiButton IndexButton => _index;
        public UiButton BestiariumButton => _bestiarium;
        public UiButton BackArrow => _back;
        public LookEye Eye { get; private set; }

        /// <summary>The spread shown (or being turned to).</summary>
        public PageRef Current { get; private set; } = PageRef.Index(Section.Bestiarium, 0);
        /// <summary>The book is out (opening, open or closing) / fully open.</summary>
        public bool Shown => gameObject.activeSelf;
        public bool IsOpen => gameObject.activeSelf && _phase != Phase.Closing;
        public bool Opening => _phase == Phase.Opening;
        public bool Closing => _phase == Phase.Closing;
        /// <summary>A page turn or a flip-through is playing.</summary>
        public bool Animating => _turn != null;
        /// <summary>Counts and the last jump's way (smoke, log): single turns, flip-throughs, jumps shown at once, openings and how.</summary>
        public int Turns { get; private set; }
        public int Riffles { get; private set; }
        public int Instants { get; private set; }
        public int Opens { get; private set; }
        public string LastJump { get; private set; } = "";
        public string LastOpen { get; private set; } = "";
        public float LastOpenSeconds { get; private set; }
        /// <summary>The book was closed (the owner: the one-menu rule, saving the journal).</summary>
        public Action ClosedNow;
        /// <summary>A creature's name for the book (the game's: the server's name it last saw; null: the data's).</summary>
        public Func<int, string> CreatureName;
        /// <summary>The player's base level now (the obscured rows: a creature 10 levels or more above it shows no level).</summary>
        public Func<int> PlayerLevel;
        /// <summary>The opening's own picture scales this, never the root (the menu animations' rule: a panel's own pose is unchanged).</summary>
        private Transform _body;
        private PageRef _newRef; private float _newAt = float.NegativeInfinity;
        /// <summary>A creature was newly written down (or scanned): opened within <see cref="NewEntrySeconds"/> s, the book opens on its page.</summary>
        public void NoteNew(int mob)
        {
            int cid = CodexData.CanonicalId(mob);
            if (mob != cid && mob > 0) _activeVersion[cid] = mob;
            _newRef = PageRef.Creature(cid);
            _newAt = Time.unscaledTime;
        }
        private float _heardAt = float.NegativeInfinity;
        /// <summary>A creature was heard of in this session (the journal keeps the pending list; this is when, for the newest-wins rule).</summary>
        public void NoteHeard() { _heardAt = Time.unscaledTime; }
        /// <summary>The next opening lands on this page before every other rule, once (the tutorial's Codex step: its worked example,
        /// LokiGame.WCodexTut.cs); null: none.</summary>
        public PageRef? OpenOnce;
        /// <summary>The rows marked "New" (heard of, the book opened on them): marked until the book closes after their row was drawn.</summary>
        private readonly HashSet<int> _markedNew = new HashSet<int>(), _markDrawn = new HashSet<int>();
        public bool MarkedNew(int mob) => _markedNew.Contains(CodexData.CanonicalId(mob));
        /// <summary>The share of the opening done (0-1; the menu animations' smoke reads a book that plays its own opening).</summary>
        public float Open01 => _phase == Phase.Opening ? (_animOpen ? Mathf.Clamp01((Time.unscaledTime - _phaseAt) / OpenSeconds) : 1f) : _phase == Phase.Open ? 1f : 0f;
        /// <summary>The creature is in the book (at any tier: a boss only looked at or a collapsed variant included).</summary>
        public bool Discovered(int mob) => _journal != null && (_journal.Tier(mob) != CodexJournal.TierNone || _journal.EncounteredVersions(mob).Count > 0);

        // design note (2026-10-17 item 44): the version selected on a collapsed creature's page, and whether its in-page Versions submenu is open
        private readonly Dictionary<int, int> _activeVersion = new Dictionary<int, int>();
        private int _versionsOpenFor;
        public bool VersionsMenuOpen => Current.Kind == Kind.Creature && _versionsOpenFor == Current.Id;
        public int DisplayedVersion(int canonicalId)
        {
            int cid = CodexData.CanonicalId(canonicalId);
            if (_activeVersion.TryGetValue(cid, out int v) && v > 0) return v;
            if (_journal != null)
            {
                var enc = _journal.EncounteredVersions(cid);
                if (enc.Count > 0 && !enc.Contains(cid)) return enc[0];
            }
            return cid;
        }
        public void SelectVersion(int canonicalId, int versionId)
        {
            int cid = CodexData.CanonicalId(canonicalId);
            _activeVersion[cid] = versionId;
            _versionsOpenFor = 0;
            ShowSpread(Current);
        }
        public void ToggleVersionsMenu(int canonicalId)
        {
            int cid = CodexData.CanonicalId(canonicalId);
            _versionsOpenFor = _versionsOpenFor == cid ? 0 : cid;
            ShowSpread(Current);
        }

        private enum Phase { Closed, Opening, Open, Closing }
        private Phase _phase = Phase.Closed; private float _phaseAt;
        private PageRef _remember; private float _closedAt = float.NegativeInfinity;
        private readonly Dictionary<string, float> _shownAt = new Dictionary<string, float>();
        private bool _animOpen;
        private float _pageAlpha = 1f;

        // ------------------------------------------------------------------ making it
        public static CodexBook Create(Transform parent, Func<Camera> head, CodexJournal journal)
        {
            var size = new Vector2(2f * PageW + Gutter, PageH);
            var rt = UiKit.Panel("CodexBook", parent, size, MetresPerPx, true, PanelKind.ByName, PanelLook.None);
            var b = rt.gameObject.AddComponent<CodexBook>();
            b.Root = rt; b._head = head; b._journal = journal;
            b.Grab = rt.gameObject.AddComponent<UiGrabbable>();
            b.Grab.MinScale = HandMetresPerPx * 0.5f; b.Grab.MaxScale = MetresPerPx * 2f;
            b._body = Hinge(rt, "body", 0f);
            b._hingeL = Hinge((RectTransform)b._body, "hinge:left", -FoldDeg); b._hingeR = Hinge((RectTransform)b._body, "hinge:right", FoldDeg);
            b.BuildCovers();
            b._left = b.MakePage(b._hingeL, -1); b._right = b.MakePage(b._hingeR, 1);
            // the fixed buttons: X (just outside the right page's top corner, as the back arrow is outside the left page's: on the page it
            // covered the stat table's third column), ‹ › (bottom corners), Index and Bestiarium (top left of the left page, item 50), the look's eye
            b._x = UiKit.RoundButton(b._right.Box, "X", new Vector2(PageW / 2 + 24 + 52, PageH / 2 - 56), new Vector2(104, 104), () => b.Close("its X"), 44);
            b._x.Tip = () => "Close";
            b._prev = UiKit.RoundButton(b._left.Box, "‹", new Vector2(-PageW / 2 + 150, -PageH / 2 + 60), new Vector2(124, 104), () => b.Turn(-1, "‹"), 58);
            b._next = UiKit.RoundButton(b._right.Box, "›", new Vector2(PageW / 2 - 76, -PageH / 2 + 60), new Vector2(124, 104), () => b.Turn(1, "›"), 58);
            b._prev.Tip = () => "Previous page"; b._next.Tip = () => "Next page";
            b._index = UiKit.Button(b._left.Box, "Index", new Vector2(-PageW / 2 + 96, PageH / 2 - 52), new Vector2(144, 88), () => b.ToIndex("Index"), 28);
            b._index.name = "btn:index";
            b._bestiarium = UiKit.Button(b._left.Box, "Bestiarium", new Vector2(-2f, PageH / 2 - 52), new Vector2(184, 88), () => b.ToBestiarium("Bestiarium"), 28);
            b._bestiarium.name = "btn:bestiarium";
            // the look's eye (UI/LookSelect.cs, item 24): 52 px (2.9 cm: 2.7° from the eyes at its corner), 30 px (1.7 cm) inside the left
            // page's corner, on the page (the root's flat rectangle is behind the turned-in page)
            b.Eye = LookEye.Add(rt, () => "CodexBook", Vector2.zero, 52);
            var et = (RectTransform)b.Eye.transform; et.SetParent(b._left.Box, false); et.anchoredPosition = new Vector2(-PageW / 2 + 30 + 26, -PageH / 2 + 30 + 26);
            // the back arrow (UI/MenuNav.cs: shown while the book was opened from another menu): the theme's gold disc just left of the left
            // page, in its plane (MenuNav.MakeArrow puts it on the root, flat behind the page's turned-in edge)
            b._back = UiKit.RoundButton(b._left.Box, MenuNav.ArrowGlyph, new Vector2(-PageW / 2 - 24 - 60, PageH / 2 - 56), new Vector2(120, 104), () => MenuNav.Back(), 64);
            b._back.name = "btn:back"; b._back.Tip = () => "Back"; b._back.gameObject.SetActive(false);
            b.BuildSheets();
            rt.gameObject.SetActive(false);
            return b;
        }

        private static Transform Hinge(RectTransform root, string name, float deg)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = root.gameObject.layer;
            var t = (RectTransform)go.transform; t.SetParent(root, false);
            t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f); t.sizeDelta = Vector2.zero; t.anchoredPosition = Vector2.zero;
            t.localRotation = Quaternion.Euler(0f, deg, 0f);
            return t;
        }

        private Page MakePage(Transform hinge, int side)
        {
            var box = UiKit.Box((RectTransform)hinge, side < 0 ? "page:left" : "page:right", new Vector2(side * (Gutter / 2 + PageW / 2), 0f), new Vector2(PageW, PageH), new Color(1, 1, 1, 0));
            box.GetComponent<Image>().enabled = false;
            UiKit.Dress(box, new Vector2(PageW, PageH), MetresPerPx, PanelLook.Card);
            var col = box.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(PageW, PageH, 4f); col.center = new Vector3(0, 0, 3f);
            box.gameObject.AddComponent<UiBlocker>(); box.gameObject.AddComponent<UiFacet>();
            var g = box.gameObject.AddComponent<CanvasGroup>();
            var content = UiKit.Box(box, "content", Vector2.zero, new Vector2(PageW, PageH), new Color(1, 1, 1, 0));
            content.GetComponent<Image>().enabled = false;
            return new Page { Box = box, Content = content, Group = g };
        }

        // ------------------------------------------------------------------ opening and closing
        public string PlaceKey => HandMode ? "CodexBook:Hand" : "CodexBook:Large";
        private Vector3 _spawnPos;
        private Quaternion _spawnRot;

        /// <summary>Spawns the book in front of the eyes. Within <see cref="RememberSeconds"/> s of its last closing: on the page it was on
        /// (unless it was in the Itemarium, item 50); otherwise on the first page of the Codex.</summary>
        public void Open(string why)
        {
            var cam = _head != null ? _head() : null;
            if (_phase == Phase.Open || _phase == Phase.Opening) { Debug.Log($"[Codex] the book is open already ({why})"); return; }
            gameObject.SetActive(true);
            HandMode = HandHeldChosen;
            if (cam != null)
            {
                if (!PanelPlacing.TryPlaceMenu(PlaceKey, transform, cam, out _))
                {
                    var f = cam.transform.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                    transform.position = cam.transform.position + f * (HandMode ? HandAhead : Distance) + Vector3.down * (HandMode ? HandBelow : Below);
                    UiKit.FaceHead(transform, cam);
                }
            }
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
            transform.localScale = Vector3.one * Scale;
            float since = Time.unscaledTime - _closedAt, sinceNew = Time.unscaledTime - _newAt;
            // design note (2026-10-17 item 50): "The codex shouldn't open on the itemarium"
            bool remember = since <= RememberSeconds && _remember.Section != Section.Itemarium && _remember.Kind != Kind.Item;
            bool fresh = sinceNew <= NewEntrySeconds && (_closedAt <= _newAt || since <= RememberSeconds) && Discovered(_newRef.Id);
            // a creature heard of since the book was last opened on it (no time limit; heard in an earlier session: older than anything of this
            // one): the list page that holds its row (design note: "it will open directly on the page of the list itself that has that entity's
            // information"); the newest event of the three wins (FLAGGED: the orchestrator's default)
            var pend = _journal != null ? _journal.PendingHeard : null; bool heard = pend != null && pend.Count > 0;
            float tHeard = heard ? (_heardAt > float.NegativeInfinity ? _heardAt : -1e9f) : float.NegativeInfinity, tNew = fresh ? _newAt : float.NegativeInfinity, tRem = remember ? _closedAt : float.NegativeInfinity;
            var once = OpenOnce; OpenOnce = null; bool useOnce = once.HasValue && (once.Value.Kind != Kind.Creature || Discovered(once.Value.Id));
            bool useHeard = !useOnce && heard && tHeard >= tNew && tHeard >= tRem;
            bool useNew = !useOnce && !useHeard && fresh && (!remember || _newAt > _closedAt); // (both: the newer event wins)
            int heardMob = heard ? pend[pend.Count - 1] : 0;
            var to = useOnce ? once.Value : useHeard ? PageRef.Index(Section.Bestiarium, Mathf.Max(0, SpreadOf(heardMob))) : useNew ? _newRef : remember ? _remember : PageRef.Index(Section.Bestiarium, 0);
            if (useHeard) { foreach (var m in pend) _markedNew.Add(CodexData.CanonicalId(m)); _journal.ClearPending(); _journal.SaveNow("the book opened on a creature heard of"); }
            _versionsOpenFor = 0;
            ShowSpread(to); _turn = null; HideSheets();
            Opens++;
            _animOpen = MenuAnim.ShouldPlay(AnimKey);
            _phase = Phase.Opening; _phaseAt = Time.unscaledTime;
            LastOpen = useOnce ? $"the page asked for ({to}: the tutorial's example)" : useHeard ? $"the list page of a creature heard of ({NameOf(heardMob)} #{heardMob}: {to}; {_markedNew.Count} marked new)" : useNew ? $"the new entry ({to}, written {sinceNew:0.0} s ago{(remember ? $"; closed {since:0.0} s ago on {_remember}: the newer wins" : "")})"
                     : remember ? $"the page it was on ({to}, closed {since:0.0} s ago{(fresh ? $"; a new entry {sinceNew:0.0} s ago: the newer wins" : "")})" : $"the index ({to})";
            Debug.Log($"[Codex] the {(HandMode ? "hand-held" : "large")} book opens ({why}) on {LastOpen}; its opening {(_animOpen ? $"plays ({OpenSeconds:0.00} s)" : "shown at once")}");
            ApplyPhase();
        }

        /// <summary>Closes the book (its cover swings shut, then it is gone); it remembers its page for <see cref="RememberSeconds"/> s.</summary>
        public void Close(string why)
        {
            if (_phase == Phase.Closed || _phase == Phase.Closing) return;
            if ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f)
            {
                var cam = _head != null ? _head() : Camera.main;
                if (cam != null) PanelPlacing.SaveMenuPlace(PlaceKey, transform, cam);
            }
            if (_turn != null) FinishTurn();
            _remember = Current; _closedAt = Time.unscaledTime;
            _markedNew.ExceptWith(_markDrawn); _markDrawn.Clear(); // (a "New" mark goes once its row was seen)
            _phase = Phase.Closing; _phaseAt = Time.unscaledTime;
            MenuAnim.Closed(AnimKey);
            Debug.Log($"[Codex] the book closes ({why}) on {Current}");
            try { ClosedNow?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            ApplyPhase();
        }
        /// <summary>Gone at once (the game closes every menu, a map change).</summary>
        public void CloseNow(string why)
        {
            if (_phase == Phase.Closed) return;
            if (_phase != Phase.Closing && ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f))
            {
                var cam = _head != null ? _head() : Camera.main;
                if (cam != null) PanelPlacing.SaveMenuPlace(PlaceKey, transform, cam);
            }
            _markedNew.ExceptWith(_markDrawn); _markDrawn.Clear();
            if (_phase != Phase.Closing) { _remember = Current; _closedAt = Time.unscaledTime; MenuAnim.Closed(AnimKey); try { ClosedNow?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
            _phase = Phase.Closed; HideSheets(); _turn = null;
            gameObject.SetActive(false);
            Debug.Log($"[Codex] the book is put away ({why})");
        }
        public void SetBackArrow(bool on) { if (_back != null && _back.gameObject.activeSelf != on) _back.gameObject.SetActive(on); }

        private void ApplyPhase()
        {
            float t = Time.unscaledTime - _phaseAt;
            float open; float scale = 1f, alpha = 1f;
            if (_phase == Phase.Opening)
            {
                if (!_animOpen) { _phase = Phase.Open; LastOpenSeconds = 0f; open = 1f; }
                else
                {
                    scale = Mathf.SmoothStep(0.55f, 1f, Mathf.Clamp01(t / OpenGrow));
                    open = Ease(Mathf.Clamp01((t - OpenGrow) / OpenSwing));
                    alpha = Mathf.Clamp01((t - OpenGrow - OpenSwing) / OpenFade);
                    if (t >= OpenSeconds) { _phase = Phase.Open; LastOpenSeconds = t; MenuAnim.Played(AnimKey, t); alpha = 1f; }
                }
            }
            else if (_phase == Phase.Closing)
            {
                alpha = 0f;
                open = 1f - Ease(Mathf.Clamp01(t / CloseSwing));
                scale = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((t - CloseSwing) / CloseShrink));
                if (t >= CloseSeconds) { _phase = Phase.Closed; gameObject.SetActive(false); return; }
            }
            else { open = 1f; }
            // the left half swings about the spine from lying on the right half (closed: -(180 - fold)) to open (-fold)
            _hingeL.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-(180f - FoldDeg), -FoldDeg, open), 0f);
            transform.localScale = Vector3.one * Scale; _body.localScale = Vector3.one * scale;
            if (_phase == Phase.Open) alpha = 1f;
            _pageAlpha = alpha;
            UpdatePageFacing();
            SetCoverAlpha(1f);
        }
        /// <summary>design note (2026-10-17 item 27): hides the pages and page blocks when viewed from behind the book so only the ornate leather cover shows.</summary>
        private void UpdatePageFacing()
        {
            var cam = _head != null ? _head() : null;
            bool frontL = cam == null || _left.Box.InverseTransformPoint(cam.transform.position).z <= 0f;
            bool frontR = cam == null || _right.Box.InverseTransformPoint(cam.transform.position).z <= 0f;
            float al = frontL ? _pageAlpha : 0f, ar = frontR ? _pageAlpha : 0f;
            _left.Group.alpha = al; _right.Group.alpha = ar;
            _left.Group.blocksRaycasts = al > 0.99f;
            _right.Group.blocksRaycasts = ar > 0.99f;
            UpdateCoverFacing(frontL, frontR);
        }
        private static float Ease(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        // ------------------------------------------------------------------ the spreads, the turns
        /// <summary>The book's order of spreads (a turn's direction): the Bestiarium's index, its entries, the Itemarium's index, its entries.</summary>
        private int Order(PageRef p)
        {
            int part = p.Section == Section.Bestiarium ? 0 : 2;
            if (p.Kind == Kind.Index) return part * 100000 + p.Spread;
            int i = p.Kind == Kind.Creature ? CreatureList().IndexOf(p.Id) : ItemList().IndexOf(p.Id);
            return (part + 1) * 100000 + Mathf.Max(0, i);
        }

        /// <summary>‹ / ›: the spread before / after in the same part (an index's next list pages; an entry's neighbour in the list).</summary>
        public bool Turn(int dir, string why)
        {
            if (_phase != Phase.Open && _phase != Phase.Opening) return false;
            var to = Neighbour(Current, dir); if (!to.HasValue) return false;
            StartTurn(to.Value, dir, 1, why);
            Turns++;
            return true;
        }

        /// <summary>A jump to another spread (a list row, a part, "Index", a drop, a recent entry): the flip-through, unless it is off or
        /// that spread was shown within <see cref="SameWindow"/> s (then at once).</summary>
        public void Jump(PageRef to, string why)
        {
            if (_phase == Phase.Closed || _phase == Phase.Closing) return;
            if (to.Equals(Current) && _turn == null) return;
            float since = _shownAt.TryGetValue(to.Key, out var at) ? Time.unscaledTime - at : float.PositiveInfinity;
            bool off = World.UserSettings.Prefs.codexFlipsOff;
            if (off || since <= SameWindow || _phase == Phase.Opening)
            {
                if (_turn != null) FinishTurn();
                ShowSpread(to); Instants++;
                LastJump = off ? "at once (the flip-through is off)" : _phase == Phase.Opening ? "at once (the book is opening)" : $"at once (shown {since:0.0} s ago)";
                Debug.Log($"[Codex] jump to {to} ({why}): {LastJump}");
                return;
            }
            int dir = Order(to) >= Order(Current) ? 1 : -1;
            StartTurn(to, dir, RiffleSheets, why);
            Riffles++; LastJump = $"flip-through ({RiffleSheets} sheets, {RiffleSeconds:0.00} s)";
            Debug.Log($"[Codex] jump to {to} ({why}): {LastJump}, {(dir > 0 ? "forward" : "back")}");
        }

        /// <summary>design note (2026-10-17 item 50): "Index" always takes the player all the way back to the first page of the Codex.</summary>
        public void ToIndex(string why)
        {
            var first = PageRef.Index(Section.Bestiarium, 0);
            if (!Current.Equals(first)) Jump(first, why);
        }

        /// <summary>design note (2026-10-17 item 50): "Bestiarium" button takes the player to the Bestiarium list spread.</summary>
        public void ToBestiarium(string why)
        {
            var c = Current;
            int spread = c.Kind == Kind.Creature ? Mathf.Max(0, SpreadOf(c.Id)) : 0;
            Jump(PageRef.Index(Section.Bestiarium, spread), why);
        }

        private PageRef? Neighbour(PageRef p, int dir)
        {
            if (p.Kind == Kind.Index)
            {
                int n = SpreadCount(p.Section), s = p.Spread + dir;
                return s >= 0 && s < n ? PageRef.Index(p.Section, s) : (PageRef?)null;
            }
            var list = p.Kind == Kind.Creature ? CreatureList() : ItemList();
            if (p.Kind == Kind.Creature)
            {
                for (int k = list.IndexOf(p.Id) + dir; k >= 0 && k < list.Count; k += dir) if (Discovered(list[k])) return PageRef.Creature(list[k]); // (a creature not written down has no page to turn to)
                return null;
            }
            int i = list.IndexOf(p.Id) + dir;
            if (i < 0 || i >= list.Count) return null;
            return PageRef.Item(list[i]);
        }

        // the turn in progress: the sheets, their start times, the spread it lands on
        private sealed class TurnRun { public PageRef To; public int Dir, Sheets; public float At; public bool LeftDone; public float Seconds; }
        private TurnRun _turn;
        private void StartTurn(PageRef to, int dir, int sheets, string why)
        {
            if (_turn != null) FinishTurn();
            if (!to.Equals(Current)) _versionsOpenFor = 0;
            var cam = _head != null ? _head() : null;
            _turn = new TurnRun { To = to, Dir = dir, Sheets = sheets, At = Time.unscaledTime, Seconds = sheets > 1 ? RiffleSeconds : TurnSeconds };
            // forward: the page under the lifted sheet (the new right page) shows at once, the new left page when the last sheet lands;
            // back: the new left page at once, the new right one at the end
            var target = Build(to);
            if (dir > 0) { DrawSide(_right, target.right); } else { DrawSide(_left, target.left); }
            _pendingOther = target;
            Current = to;
            UpdateFixedButtons();
            if (sheets == 1) Debug.Log($"[Codex] page turn {(dir > 0 ? "forward" : "back")} to {to} ({why})");
        }
        private (Action<RectTransform> left, Action<RectTransform> right) _pendingOther;
        private void FinishTurn()
        {
            if (_turn == null) return;
            var t = _turn; _turn = null;
            if (t.Dir > 0) DrawSide(_left, _pendingOther.left); else DrawSide(_right, _pendingOther.right);
            _shownAt[t.To.Key] = Time.unscaledTime;
            HideSheets();
        }

        /// <summary>Shows a spread at once (no turn).</summary>
        private void ShowSpread(PageRef to)
        {
            if (!to.Equals(Current)) _versionsOpenFor = 0;
            var b = Build(to);
            DrawSide(_left, b.left); DrawSide(_right, b.right);
            Current = to; _shownAt[to.Key] = Time.unscaledTime;
            UpdateFixedButtons();
        }

        private void DrawSide(Page p, Action<RectTransform> draw)
        {
            var c = p.Content;
            for (int i = c.childCount - 1; i >= 0; i--) { var ch = c.GetChild(i).gameObject; ch.SetActive(false); Destroy(ch); }
            _portraits.RemoveAll(x => x.Image == null || !x.Image.transform.IsChildOf(transform) || x.Image.transform.IsChildOf(c));
            RowTargets.RemoveAll(r => r.button == null || r.button.transform.IsChildOf(c));
            try { draw?.Invoke(c); } catch (Exception e) { Debug.LogException(e); }
        }

        private void UpdateFixedButtons()
        {
            var c = Current;
            _prev.gameObject.SetActive(Neighbour(c, -1).HasValue);
            _next.gameObject.SetActive(Neighbour(c, 1).HasValue);
            bool onFirstIndex = c.Kind == Kind.Index && c.Section == Section.Bestiarium && c.Spread == 0;
            _index.gameObject.SetActive(!onFirstIndex);
            if (_bestiarium != null) _bestiarium.gameObject.SetActive(c.Kind != Kind.Index || c.Section != Section.Bestiarium);
        }

        /// <summary>The spread's two pages as drawing calls.</summary>
        private (Action<RectTransform> left, Action<RectTransform> right) Build(PageRef p)
        {
            if (p.Kind == Kind.Creature) return (c => DrawCreatureLeft(c, p.Id), c => DrawCreatureRight(c, p.Id));
            if (p.Kind == Kind.Item) return (c => DrawItemLeft(c, p.Id), c => DrawItemRight(c, p.Id));
            if (p.Spread == 0) return (c => DrawTitle(c, p.Section), c => DrawList(c, p.Section, 0, true));
            return (c => DrawList(c, p.Section, 2 * p.Spread - 1, false), c => DrawList(c, p.Section, 2 * p.Spread, true));
        }

        /// <summary>The list pages of a part: page 0 is the title spread's right page, then two a spread.</summary>
        private int ListPages(Section s) => Mathf.Max(1, (Count(s) + Rows - 1) / Rows);
        public int SpreadCount(Section s) { int lp = ListPages(s); return 1 + (lp - 1 + 1) / 2; }
        private static int SpreadOfRow(int row) { int lp = row / Rows; return lp == 0 ? 0 : (lp + 1) / 2; }
        /// <summary>The Bestiarium's list spread that holds this creature's row (-1: not listed).</summary>
        public int SpreadOf(int mob) { int i = CreatureList().IndexOf(CodexData.CanonicalId(mob)); return i < 0 ? -1 : SpreadOfRow(i); }
        private int Count(Section s) => s == Section.Bestiarium ? CreatureList().Count : ItemList().Count;

        // ------------------------------------------------------------------ every frame
        private void Update()
        {
            if (_phase == Phase.Opening || _phase == Phase.Closing) ApplyPhase();
            else if (_phase == Phase.Open) UpdatePageFacing();
            if (_turn != null) TickTurn();
            TickPortraits();
        }

        // ------------------------------------------------------------------ the journal's lists, sorted as the index shows them
        private List<int> _creatures, _items;
        /// <summary>The journal changed (new entries): the lists are made again when next asked.</summary>
        public void JournalChanged() { _creatures = null; _items = null; }
        /// <summary>The Bestiarium's list: every canonical creature a player can meet and every one written down, by level then name (design note, 2026-10-15:
        /// "Order creatures first by level, then by name. The bestiary should have all creatures listed"; 2026-10-17 item 44: duplicate names and
        /// modifier variants collapsed into their canonical base creature, tutorial Poring excluded).</summary>
        public List<int> CreatureList()
        {
            if (_creatures != null) return _creatures;
            var set = new HashSet<int>();
            foreach (var id in CodexData.MetableIds) if (!CodexData.IsExcluded(id)) set.Add(CodexData.CanonicalId(id));
            if (_journal != null) foreach (var id in _journal.CreatureIds) if (!CodexData.IsExcluded(id)) set.Add(CodexData.CanonicalId(id));
            var keys = new Dictionary<int, (int level, string name)>();
            foreach (var id in set) { var c = CodexData.GetCreature(id); keys[id] = (c != null ? c.Level : 0, c != null && c.Name.Length > 0 ? c.Name : NameOf(id)); }
            var l = new List<int>(set);
            l.Sort((a, b) => { var ka = keys[a]; var kb = keys[b]; int c = ka.level.CompareTo(kb.level); if (c == 0) c = string.Compare(ka.name, kb.name, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : a.CompareTo(b); });
            return _creatures = l;
        }
        /// <summary>The canonical creatures written down (smoke, the part's count).</summary>
        public int CreaturesWritten => _journal != null ? CreatureList().Count(id => _journal.Tier(id) != CodexJournal.TierNone) : 0;
        public List<int> ItemList()
        {
            if (_items != null) return _items;
            var l = new List<int>(_journal != null ? _journal.ItemIds : new int[0]);
            l.Sort((a, b) => { int ta = TypeRank(a), tb = TypeRank(b); return ta != tb ? ta.CompareTo(tb) : string.CompareOrdinal(ItemName(a), ItemName(b)); });
            return _items = l;
        }
        private static int TypeRank(int item)
        {
            switch (GameDb.GetItem(item).Type) { case 0: return 0; case 2: case 11: case 18: return 1; case 5: return 2; case 4: return 3; case 12: return 4; case 10: return 5; case 6: return 6; case 7: case 8: return 7; default: return 8; }
        }
        public string NameOf(int mob)
        {
            string n = CreatureName != null ? CreatureName(mob) : null;
            if (!string.IsNullOrEmpty(n)) return n;
            var c = CodexData.GetCreature(mob); return c != null && c.Name.Length > 0 ? c.Name : string.Format("Creature #{0}", mob);
        }
        /// <summary>An item's name in English (design note: everything in English): the game's English column, else the Codex data's (rAthena's), else
        /// the game's name.</summary>
        public static string ItemName(int item)
        {
            var g = GameDb.GetItem(item);
            if (!string.IsNullOrEmpty(g.English)) return UiKit.StripTags(g.English);
            var c = CodexData.ItemsLoaded ? CodexData.GetItem(item) : null;
            return c != null && c.Name.Length > 0 ? c.Name : UiKit.StripTags(g.Name);
        }
    }
}
