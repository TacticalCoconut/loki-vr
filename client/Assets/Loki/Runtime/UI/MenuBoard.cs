using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>Client wave 11 (w11menu): marks a panel the pointer hand can take by its grip while its laser is on it
    /// (VR/DioramaRig.Ui.cs UpdateGrab moves and resizes it).</summary>
    public sealed class UiGrabbable : MonoBehaviour
    {
        /// <summary>The panel's scale (metres per pixel) may go from here to here while both grips resize it.</summary>
        public float MinScale = 0.0003f, MaxScale = 0.002f;
        /// <summary>The grip was let go: the owner keeps where the panel is now.</summary>
        public Action Released;
        public bool Held;
        /// <summary>How often it was taken (smoke, log).</summary>
        public int Grabs;
    }

    /// <summary>Client wave 20 (w20char), point 52: marks a part of a panel turned against it (the wide Character board's wings): a button's
    /// tip faces as the part does (UI/ButtonTip.cs).</summary>
    public sealed class UiFacet : MonoBehaviour { }

    /// <summary>
    /// Client wave 11 (w11menu), part 2: the board a group of the new menu opens on (design note, 2026-10-06: "redesign it completely so that it
    /// is more organic to use instead of just being a traditional menu"). A panel in front of the player, not on the wrist: placed
    /// once when it opens (<see cref="Open"/>: ahead of the eyes at the distance, height and size the player left it at: VR/Comfort.cs
    /// BoardDistance / BoardHeight / BoardScale), turned to the head once, and then it stays where it is: it does not follow the
    /// head. The pointer hand's grip with the laser on it moves it, both grips resize it (<see cref="UiGrabbable"/>).
    /// Layout: a title and X at the top; the group's pages as large tabs down the left side (a tab may also be an action that
    /// opens a window: Cart, Guide …); the page itself on the right (<see cref="Content"/>: the wrist menu's own page objects are
    /// re-hosted here, UI/WristMenu.Board.cs); and, for the item grid, a column of item cards attached at the right
    /// (<see cref="Aside"/>). Sizes: 0.00074 m per pixel (892 × 856 px = 66.0 × 63.3 cm), so a page's 20 px text is 1.3° tall
    /// from 0.65 m, a side tab (230 × 92 px) 17.0 × 6.8 cm.
    /// Client wave 13 (w13look), item 24: the look's eye in the bottom left corner under the side tabs (<see cref="EyePx"/>,
    /// <see cref="EyeRimPx"/> inside the rim); the board grew 26 px at its bottom for it (830 px before: under seven tabs the corner was
    /// 48 px tall); everything else is placed from the top as before. Its kind is the group shown (<see cref="LookKey"/>).
    /// </summary>
    public sealed class MenuBoard : MonoBehaviour
    {
        public const float W = 892, H = 856, TabW = 230, TabH = 92, PageW = 610, PageH = 710, AsideW = 470, HeadH = 104;
        /// <summary>Client wave 13 (w13look), item 24: the eye's side and its distance from the rim, in px (3.55 cm: 2.6° from the eyes where
        /// it is: the board's middle 0.66 m away, its corner 0.78 m; 1.55 cm), its kind for the look ("Menu:" + the group's English name:
        /// UI/WristMenu.Board.cs BoardSync), the eye.</summary>
        public const float EyePx = 48f, EyeRimPx = 21f;
        public string LookKey = "Menu";
        public LookEye Eye { get; private set; }
        public const float MetresPerPx = 0.00074f;
        public const int MaxTabs = 7;
        /// <summary>Wave 11's cards' column was turned this many degrees about its left edge towards the player (client wave 13 replaced the
        /// column by panels that each face the eyes: UI/WristMenu.Board.cs LayoutPanels).</summary>
        public const float AsideTurn = 32f;
        public RectTransform Root { get; private set; }
        /// <summary>Where the page goes (its centre; a page is PageW × PageH).</summary>
        public RectTransform Content { get; private set; }
        public RectTransform Aside { get; private set; }
        public UiGrabbable Grab { get; private set; }
        public Text Toast { get; private set; }
        private Text _title; private UiButton _x, _back;
        /// <summary>Client wave 13 (w13items), item 11: the back arrow (UI/MenuNav.cs), shown while the board was opened from another menu.</summary>
        public UiButton BackButton => _back;
        public void SetBackArrow(bool on) { if (_back != null && _back.gameObject.activeSelf != on) _back.gameObject.SetActive(on); }
        private Vector3? _nextPos; private Quaternion _nextRot; private Vector3 _nextScale;
        /// <summary>Client wave 13 (w13items), item 11: the next opening puts the board here (the back arrow brings it back where it was),
        /// not in front of the eyes.</summary>
        public void PlaceNext(Vector3 position, Quaternion rotation, Vector3 scale) { _nextPos = position; _nextRot = rotation; _nextScale = scale; }
        private readonly List<UiButton> _tabs = new List<UiButton>();
        private Action<int> _onTab;
        public Action CloseClicked;
        public bool Shown => gameObject.activeSelf;
        public int TabCount { get; private set; }
        public UiButton Tab(int i) => i >= 0 && i < _tabs.Count ? _tabs[i] : null;
        public UiButton CloseButton => _x;
        public string Title => _title != null ? _title.text : "";
        /// <summary>How often the board was placed in front of the player (smoke: once per opening, never while it stays open).</summary>
        public int Placed { get; private set; }

        public static MenuBoard Create(Transform parent)
        {
            var rt = UiKit.Panel("MenuBoard", parent, new Vector2(W, H), MetresPerPx);
            var b = rt.gameObject.AddComponent<MenuBoard>();
            b.Root = rt;
            b.Grab = rt.gameObject.AddComponent<UiGrabbable>();
            b.Grab.MinScale = MetresPerPx * 0.5f; b.Grab.MaxScale = MetresPerPx * 2f;
            float top = H / 2;
            b._title = UiKit.TitlePlate(rt, "", 38, 560).Text; // client wave 21 (w21kit): the board's name on the gold plate on the frame's top edge (the same 38 px letters); wide: on the middle facet
            b._title.resizeTextForBestFit = true; b._title.resizeTextMinSize = 24; b._title.resizeTextMaxSize = 38;
            b.Toast = UiKit.Label(rt, "", new Vector2(90, top - HeadH / 2), new Vector2(500, 64), 22, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            b.Toast.resizeTextForBestFit = true; b.Toast.resizeTextMinSize = 15; b.Toast.resizeTextMaxSize = 22;
            b._x = UiKit.RoundButton(rt, "X", new Vector2(W / 2 - 14 - 55, top - HeadH / 2 + 1), new Vector2(110, 94), () => b.CloseClicked?.Invoke(), 40); // (client wave 21: the theme's round gold button, the same collider)
            b._back = MenuNav.MakeArrow(rt, new Vector2(W, H), new Vector2(120, 104)); // client wave 13 (w13items), item 11: outside the top left corner, 8.9 × 7.7 cm: 5.5° from its 0.8 m (the X 5.2°)
            UiKit.Box(rt, "rule", new Vector2(0, top - HeadH), new Vector2(W - 24, 2), UiKit.PanelBorder * 0.7f);
            for (int i = 0; i < MaxTabs; i++)
            {
                int idx = i;
                var t = UiKit.Button(rt, "", new Vector2(-W / 2 + 16 + TabW / 2, top - HeadH - 10 - TabH / 2 - i * (TabH + 4)), new Vector2(TabW, TabH), () => b._onTab?.Invoke(idx), 28);
                t.gameObject.SetActive(false);
                b._tabs.Add(t);
            }
            var c = UiKit.Box(rt, "content", new Vector2(-W / 2 + 16 + TabW + 12 + PageW / 2, top - HeadH - 6 - PageH / 2), new Vector2(PageW, PageH), new Color(0, 0, 0, 0.18f));
            b.Content = c;
            // the item panels' holder (client wave 13, w13items, item 8: in place of wave 11's bordered column of cards, 470 px turned 32° at
            // the right edge): nothing of its own is drawn and it stops nothing; the panels in it are placed by UI/WristMenu.Board.cs
            // (LayoutPanels) in the board's own pixels, each with its own background and collider (UI/ItemPanel.cs)
            b.Aside = UiKit.Box(rt, "aside", Vector2.zero, new Vector2(W, H), new Color(0, 0, 0, 0));
            b.Aside.gameObject.SetActive(false);
            b.Eye = LookEye.Add(rt, () => b.LookKey, new Vector2(-W / 2 + EyeRimPx + EyePx / 2, -H / 2 + EyeRimPx + EyePx / 2), EyePx); // client wave 13 (w13look), item 24
            b._rule = (RectTransform)rt.Find("rule"); b._border = (RectTransform)rt.Find("border"); b._bg = (RectTransform)rt.Find("bg"); b._col = rt.GetComponent<BoxCollider>();
            b.WingLeft = b.MakeWing("wing:left", -1, out var cl); b.ContentLeft = cl; // client wave 20 (w20char), point 52
            b.WingRight = b.MakeWing("wing:right", 1, out var cr); b.ContentRight = cr;
            rt.gameObject.SetActive(false);
            return b;
        }

        // ------------------------------------------------------------------ client wave 20 (w20char), point 52: the wide Character board
        // design note (2026-10-09): "All three of those should be in the same long more rectangular menu, and they all need to be visible at the same
        // time." The Character group's board shows its three pages at once, side by side: Status | Stats | Equipment, no side tabs. It is
        // three facets of FacetW x H px (a page and 16 px each side), FacetGap apart: the board itself (its centre, Stats) and two wings, its
        // children (they move, turn and resize with it: one board for the grip, Reposition and the panel kinds). Each wing is turned
        // FoldDeg towards the player about its inner edge (client wave 23, w23tree: 28°, more open than wave 20's 40.4°, at
        // which each column faced the eyes of the default place at the middle one's distance: FoldEyePx behind the board, 0.65 m ahead
        // and 0.10 m under the eyes, size x1); a flat board this wide puts its outer columns ~0.80 m from the eyes: their text ~0.8
        // times as large.
        // The title and the back arrow go to the left wing's top left, the look eye to its top left before the title (at the bottom left it
        // would cover Status's Sit button), X to the right wing's top right, the toast to the middle's header.
        /// <summary>A facet's width (a page and 16 px each side), the gap between facets, the eyes' distance at the default place (px:
        /// 0.65 m ahead and 0.10 m under the eyes, size x1).</summary>
        public const float FacetW = PageW + 32f, FacetGap = 12f, FoldEyePx = 889f;
        /// <summary>Client wave 23 (w23tree), design note 2026-10-11: "the character menu folds a bit too close to the player. The little
        /// booklet needs to be a bit more open The angles are too deep. They need to be a little bit shallower." How far each wing is
        /// turned towards the player (degrees), about its INNER edge: the hinge is the middle of the gap to the middle facet, so the
        /// inner edges stay joined and the outer edges swing away from the player. THE ONE CONSTANT. Client wave 20 turned each wing
        /// <see cref="FoldFacingDeg"/> = 40.4° (each column faced the eyes of the default place, at the middle column's distance).
        /// 28° is the design's (2026-10-11: "28⁰ is fine", told that the Equipment column's 16 px "Others may look" is then 0.89° from the
        /// eyes: it is 0.97° at 40.4°, 0.95° on the usual board; this branch first stopped at 38° to keep 0.95°).</summary>
        public const float FoldDeg = 28f;
        /// <summary>The smallest an outer column's text is from the eyes of the default place at that fold (degrees: what the one
        /// small label measures there, a little under; smoke w20char B holds every text of the board to it).</summary>
        public const float FoldTextFloorDeg = 0.88f;
        /// <summary>The smallest the board's own corner buttons (the back arrow outside the left wing, the X at the right wing's top
        /// right) are from the eyes of the default place at that fold (degrees): measured 5.0 and 4.9 at 28° (5.5 and 5.3 at 38°: the
        /// far corners swing away with the wings); smoke w20char A holds both to it. FLAGGED: it was 5° before the design's 28°.</summary>
        public const float FoldButtonFloorDeg = 4.8f;
        /// <summary>Client wave 20's fold, at which each wing faces the eyes of the default place: 2 atan((FacetW + FacetGap) / 2 / FoldEyePx) = 40.4°.</summary>
        public static float FoldFacingDeg => 2f * Mathf.Atan((FacetW + FacetGap) / 2f / FoldEyePx) * Mathf.Rad2Deg;
        /// <summary>The wide layout is shown (the Character group).</summary>
        public bool Wide { get; private set; }
        /// <summary>The wings and their page areas (centres; a page is PageW x PageH).</summary>
        public RectTransform WingLeft { get; private set; }
        public RectTransform WingRight { get; private set; }
        public RectTransform ContentLeft { get; private set; }
        public RectTransform ContentRight { get; private set; }
        /// <summary>A page is on this board now (in the page area, or on a wing's).</summary>
        public bool HostsPage(Transform page) => page != null && (page.parent == Content || page.parent == ContentLeft || page.parent == ContentRight);
        private RectTransform _rule, _border, _bg; private BoxCollider _col;

        private RectTransform MakeWing(string name, int side, out RectTransform content)
        {
            float top = H / 2, phi = FoldDeg * Mathf.Deg2Rad, hinge = (FacetW + FacetGap) * 0.5f; // (the hinge: the middle of the gap, on the middle facet's plane)
            // client wave 21 (w21kit): a wing is a framed facet of the theme, joined to the middle facet at the fold (no frame on that side:
            // the three facets read as one folding screen: UiKit.Join)
            var w = UiKit.Box(Root, name, Vector2.zero, new Vector2(FacetW, H), new Color(1, 1, 1, 0));
            w.GetComponent<Image>().enabled = false;
            // (client wave 23: about the hinge, whatever the angle; at wave 20's 40.4° this is the place it had: 576 px aside, 212 px nearer)
            w.localPosition = new Vector3(side * hinge * (1f + Mathf.Cos(phi)), 0f, -hinge * Mathf.Sin(phi));
            w.localRotation = Quaternion.Euler(0f, side * FoldDeg, 0f);
            UiKit.Dress(w, new Vector2(FacetW, H), MetresPerPx, PanelLook.Framed);
            UiKit.Join(w, side < 0 ? Sides.Right : Sides.Left);
            var col = w.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(FacetW, H, 4f); col.center = new Vector3(0, 0, 3f); // the laser stops on it (as UiKit.Panel's own)
            w.gameObject.AddComponent<UiBlocker>(); w.gameObject.AddComponent<UiFacet>();
            UiKit.Box(w, "rule", new Vector2(0, top - HeadH), new Vector2(FacetW - 24, 2), UiKit.PanelBorder * 0.7f);
            content = UiKit.Box(w, "content", new Vector2(0, top - HeadH - 6 - PageH / 2), new Vector2(PageW, PageH), new Color(0, 0, 0, 0.18f));
            w.gameObject.SetActive(false);
            return w;
        }

        private static void Put(Component c, RectTransform parent, Vector2 pos) { var t = (RectTransform)c.transform; if (t.parent != parent) t.SetParent(parent, false); t.anchoredPosition = pos; }

        /// <summary>The wide layout (the Character group) or the usual one (every other group).</summary>
        public void SetWide(bool on)
        {
            if (Wide == on || Root == null) return;
            Wide = on;
            float w = on ? FacetW : W, top = H / 2;
            Root.sizeDelta = new Vector2(w, H);
            if (_border != null) _border.sizeDelta = new Vector2(w, H);
            if (_bg != null) _bg.sizeDelta = new Vector2(w - 6, H - 6);
            if (_col != null) _col.size = new Vector3(w, H, 4f);
            if (_rule != null) _rule.sizeDelta = new Vector2(w - 24, 2);
            WingLeft.gameObject.SetActive(on); WingRight.gameObject.SetActive(on);
            UiKit.Join(Root, on ? Sides.Left | Sides.Right : Sides.None); // (client wave 21: the middle facet has no frame at the folds)
            float fo = UiKit.OutsetOf(Root); // (the back arrow stays clear of the wood frame)
            Content.anchoredPosition = new Vector2(on ? 0f : -W / 2 + 16 + TabW + 12 + PageW / 2, top - HeadH - 6 - PageH / 2);
            if (on)
            {
                Put(Eye, WingLeft, new Vector2(-FacetW / 2 + EyeRimPx + EyePx / 2, top - HeadH / 2));
                Put(_back, WingLeft, new Vector2(-FacetW / 2 - 8 - fo - 60, H / 2 - 52));
                Put(_x, WingRight, new Vector2(FacetW / 2 - 14 - 55, top - HeadH / 2 + 1));
                Put(Toast, Root, new Vector2(0, top - HeadH / 2)); Toast.rectTransform.sizeDelta = new Vector2(FacetW - 40, 64);
            }
            else
            {
                Put(Eye, Root, new Vector2(-W / 2 + EyeRimPx + EyePx / 2, -H / 2 + EyeRimPx + EyePx / 2));
                Put(_back, Root, new Vector2(-W / 2 - 8 - fo - 60, H / 2 - 52));
                Put(_x, Root, new Vector2(W / 2 - 14 - 55, top - HeadH / 2 + 1));
                Put(Toast, Root, new Vector2(90, top - HeadH / 2)); Toast.rectTransform.sizeDelta = new Vector2(500, 64);
            }
            Debug.Log($"[Menu] board {(on ? $"wide: three facets of {FacetW:0} x {H:0} px, the wings turned {FoldDeg:0.0}°" : $"usual: {W:0} x {H:0} px")}");
        }

        /// <summary>The group's name and its side tabs: (label, is an action that opens something else).</summary>
        public void SetGroup(string title, IList<(string label, bool action)> entries, int active, Action<int> onTab)
        {
            _title.text = title ?? ""; _onTab = onTab;
            TabCount = Mathf.Min(entries != null ? entries.Count : 0, _tabs.Count);
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool show = i < TabCount;
                if (_tabs[i].gameObject.activeSelf != show) _tabs[i].gameObject.SetActive(show);
                if (!show) continue;
                var (label, action) = entries[i];
                _tabs[i].SetLabel(action ? label + "  ›" : label);
                _tabs[i].Normal = i == active ? new Color(0.45f, 0.35f, 0.15f) : action ? new Color(0.14f, 0.24f, 0.22f) : UiKit.ButtonBg;
                if (!_tabs[i].Hovered) _tabs[i].SetHover(false);
            }
        }

        public void ShowAside(bool on) { if (Aside.gameObject.activeSelf != on) Aside.gameObject.SetActive(on); }

        /// <summary>Item 6: key used to persist this board's position relative to the player's vision.</summary>
        public string PlaceKey { get; set; } = "Board";
        private Camera _lastHead;
        private Vector3 _spawnPos;
        private Quaternion _spawnRot;
        private float _spawnScale = 1f;

        /// <summary>Shows the board. When it was closed it is put in front of the player once: <paramref name="distance"/> ahead of
        /// the eyes (level), <paramref name="height"/> against them, <paramref name="scale"/> times its own size, facing them.</summary>
        public void Open(Camera head, float distance, float height, float scale)
        {
            if (head != null) _lastHead = head;
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true);
            if (_nextPos.HasValue)
            {
                transform.SetPositionAndRotation(_nextPos.Value, _nextRot);
                transform.localScale = _nextScale;
                _nextPos = null;
                _spawnPos = transform.position; _spawnRot = transform.rotation; _spawnScale = transform.localScale.x / MetresPerPx;
                Placed++;
                return;
            } // client wave 13: back where it was
            if (head == null) return;
            if (PanelPlacing.TryPlaceMenu(PlaceKey, transform, head, out float savedScale))
            {
                transform.localScale = Vector3.one * Mathf.Clamp(MetresPerPx * savedScale, Grab.MinScale, Grab.MaxScale);
            }
            else
            {
                transform.localScale = Vector3.one * Mathf.Clamp(MetresPerPx * scale, Grab.MinScale, Grab.MaxScale);
                var f = head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                transform.position = head.transform.position + f * distance + Vector3.up * height;
                UiKit.FaceHead(transform, head);
            }
            _spawnPos = transform.position; _spawnRot = transform.rotation; _spawnScale = transform.localScale.x / MetresPerPx;
            Placed++;
        }

        public void Close()
        {
            if (gameObject.activeSelf)
            {
                float curScale = transform.localScale.x / MetresPerPx;
                if ((transform.position - _spawnPos).sqrMagnitude > 1e-5f || Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f || Mathf.Abs(curScale - _spawnScale) > 1e-3f)
                {
                    var cam = _lastHead != null ? _lastHead : Camera.main;
                    if (cam != null) PanelPlacing.SaveMenuPlace(PlaceKey, transform, cam, curScale);
                }
                gameObject.SetActive(false);
            }
        }
    }
}
