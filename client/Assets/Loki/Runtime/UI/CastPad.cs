using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 12 (w12cast): the pad of the "pad" casting scheme (the design's combination 1): the nine slots of ONE hotkey row as a
    /// 3 x 3 in reading order (slot 1 top left … 9 bottom right), each with the slot's icon, its name, an item's amount and the
    /// cooldown's seconds; the row's bar and number over it. Shown while the casting gesture (grip + pointing finger) is held; the
    /// fingertip in a cell marks it, letting the grip go takes it (LokiGame.W12Cast.cs).
    /// AT THE HAND, client wave 13 (w13resize), the brief's fifteenth item (design note, 2026-10-07: "for the pad, it should open not on a
    /// fixed position on the view, but rather on a fixed position around where the hand was when it was opened"; it replaces wave
    /// 12's head-locked pad, which is gone, and the pads are part of no placing mode): when the gesture completes the pad opens
    /// AROUND THE FINGERTIP (<see cref="OpenAt"/>): its middle cell (slot 5) on the fingertip's place at that moment, upright and
    /// turned to face the eyes; then it STAYS where it opened, fixed in the room (a child of the rig, the tracking space: it follows
    /// neither the head nor the hand) until the grip is let go. Slot 5 is "stay": letting go without moving takes it; the other
    /// eight are one cell away. Its cells are VR/Comfort.cs PadCell wide (Settings → Controls "Pad size", 6 cm by default: the
    /// flick's stroke; a pitch of PadCell x 314 / 300). No collider: it never stops the laser or the look.
    /// OPAQUE, client wave 14 (w14cast), point 5 + the design's answer 5 ("the pad gets the flick's fixes too"): its background, its cells
    /// and its empty cells are drawn fully opaque from the first frame (wave 12: the cells at 0.88, an empty one at 0.45, the panel
    /// at 0.92); every slot of the row shown, an empty one darker with its number.
    /// Client wave 18 (w18flick), point 15 (design note: "allow the player to reposition where the pad shows up relative to my hand when I activate
    /// it"): it opens at the fingertip + that hand's offset (<see cref="Pose"/>: the offset in the pad's frame at the fingertip, then turned
    /// to face the eyes from its own place and by the hand's turn; VR/Comfort.cs PadOffset / PadTurn; zero: wave 13's place). In Reposition
    /// each hand shows its pad as a GHOST there (<see cref="ShowGhost"/>: see-through, <see cref="GhostAlpha"/>; lit while a hand or the
    /// laser is on it) for the OTHER hand to carry (LokiGame.W18Flick.cs).
    /// </summary>
    public sealed class CastPad : MonoBehaviour
    {
        public const float CellPx = 300f, PitchPx = 314f, HeadPx = 70f, W = 3 * PitchPx + 12f, H = 3 * PitchPx + 12f + HeadPx;
        /// <summary>The fingertip may be this far in front of or behind the pad's plane (metres); a marked cell is kept until the tip is
        /// this far outside it (pixels of the pad: 40 / 300 of a cell, 8 mm at 6 cm; no flicker at a border).</summary>
        public const float Depth = 0.15f, KeepPx = 40f;
        private RectTransform _rt; private Hotkeys _keys; private Inventory _inv; private Progression _prog; private QuickBar _bar;
        private sealed class CellUi { public Image Bg, Icon; public Text Name, Count, Cool, Num; public Image[] Rim; }
        private readonly List<CellUi> _cells = new List<CellUi>();
        private Text _head; private bool _dirty = true; private Image _border; private CastLook.State _look; // (client wave 21, w21tools: the look in force, UI/CastLook.cs)
        private static readonly Color CellColour = new Color(0.08f, 0.09f, 0.13f, 1f), MarkColour = new Color(0.95f, 0.75f, 0.3f, 1f), EmptyColour = new Color(0.035f, 0.04f, 0.055f, 1f);
        /// <summary>Client wave 20 (w20colors), the design's point 51 and answer ("Yes": the pad's rows too): the pad is drawn in its row's colour
        /// (UI/RowColours.cs): every cell's rim (<see cref="RimPx"/> px inside its edge: 2.4 mm at 6 cm cells) and the panel's border in it, every
        /// cell's fill tinted towards it as the flick's plates are (UI/FlickHud.cs FilledTint / EmptyTint); a marked cell's fill stays gold. A
        /// slap ("Pad rows: Slap") switches the row through <see cref="Show"/>: the colour goes with it.</summary>
        public const float RimPx = 12f;
        /// <summary>A marked cell's fill in the look in force (client wave 21, w21tools: gold in Plain, the theme's dark wood in Themed; an empty
        /// one 0.6 of it, as before).</summary>
        public static Color ChosenFill(bool empty) { var m = CastLook.C(MarkColour); return empty ? new Color(m.r * 0.6f, m.g * 0.6f, m.b * 0.6f, 1f) : m; }
        public static Color CellFill(int row, bool empty) => RowColours.Tint(CastLook.C(empty ? EmptyColour : CellColour), row, empty ? FlickHud.EmptyTint : FlickHud.FilledTint);
        /// <summary>The cells' and the background's alpha (smoke: opaque since client wave 14).</summary>
        public float CellAlpha(int i) => _cells[i].Bg.color.a;
        public float BackAlpha { get { var bg = _rt.Find("bg"); var img = bg != null ? bg.GetComponent<Image>() : null; return img != null ? img.color.a : 0f; } }
        public bool RightSide { get; private set; }
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        /// <summary>The hotkey row shown (0-7) and the cell marked (0-8 in reading order; -1 none).</summary>
        public int Row { get; private set; }
        public int Marked { get; private set; } = -1;
        /// <summary>Where the fingertip was when the pad last opened (the middle cell's place), and the cell size it opened with (metres).</summary>
        public Vector3 OpenedAt { get; private set; }
        public float CellMetres { get; private set; } = VR.Comfort.PadCellDefault;
        /// <summary>Client wave 18 (w18flick), point 15: the fingertip's place when it last opened (the middle cell is there plus the hand's
        /// offset); shown as Reposition's ghost now; its see-through alpha (lit: <see cref="GhostLitAlpha"/>).</summary>
        public Vector3 OpenedTip { get; private set; }
        public bool Ghost { get; private set; }
        public const float GhostAlpha = 0.45f, GhostLitAlpha = 0.8f;
        private CanvasGroup _group;

        /// <summary>room: the rig (the tracking space): an open pad stays fixed there.</summary>
        public static CastPad Create(Transform room, bool rightSide, Hotkeys keys, Inventory inv, Progression prog, QuickBar bar)
        {
            var rt = UiKit.Panel(rightSide ? "CastPadRight" : "CastPadLeft", room, new Vector2(W, H), VR.Comfort.PadCellDefault / CellPx);
            Destroy(rt.GetComponent<BoxCollider>()); Destroy(rt.GetComponent<UiBlocker>()); // never in the laser's or the look's way
            var bgImg = rt.Find("bg") != null ? rt.Find("bg").GetComponent<Image>() : null; if (bgImg != null) { var c0 = UiKit.PanelBg; c0.a = 1f; bgImg.color = c0; } // (client wave 14: opaque)
            var p = rt.gameObject.AddComponent<CastPad>();
            p._rt = rt; p._keys = keys; p._inv = inv; p._prog = prog; p._bar = bar; p.RightSide = rightSide;
            p._border = rt.Find("border") != null ? rt.Find("border").GetComponent<Image>() : null; // (client wave 20: in the row's colour)
            p._head = UiKit.Label(rt, "", new Vector2(0, H / 2 - HeadPx / 2 - 2), new Vector2(W - 24, HeadPx - 8), 34, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            for (int i = 0; i < 9; i++)
            {
                var c = new CellUi(); var at2 = CellPos(i);
                c.Bg = UiKit.Box(rt, "cell" + i, at2, new Vector2(CellPx, CellPx), CellColour).GetComponent<Image>();
                float h = (CellPx - RimPx) * 0.5f; // client wave 20 (w20colors): the cell's rim in the row's colour, inside its edge, under its texts
                c.Rim = new[]
                {
                    UiKit.Box(rt, "rim", at2 + new Vector2(0f, h), new Vector2(CellPx, RimPx), Color.white).GetComponent<Image>(),
                    UiKit.Box(rt, "rim", at2 + new Vector2(0f, -h), new Vector2(CellPx, RimPx), Color.white).GetComponent<Image>(),
                    UiKit.Box(rt, "rim", at2 + new Vector2(-h, 0f), new Vector2(RimPx, CellPx - 2f * RimPx), Color.white).GetComponent<Image>(),
                    UiKit.Box(rt, "rim", at2 + new Vector2(h, 0f), new Vector2(RimPx, CellPx - 2f * RimPx), Color.white).GetComponent<Image>(),
                };
                c.Icon = UiKit.Icon(rt, at2 + new Vector2(0, 36), new Vector2(150, 150));
                c.Name = UiKit.Label(rt, "", at2 + new Vector2(0, -88), new Vector2(CellPx - 16, 84), 30, TextAnchor.MiddleCenter);
                c.Name.resizeTextForBestFit = true; c.Name.resizeTextMinSize = 20; c.Name.resizeTextMaxSize = 30;
                c.Count = UiKit.Label(rt, "", at2 + new Vector2(CellPx / 2 - 62, CellPx / 2 - 30), new Vector2(110, 44), 30, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.45f));
                c.Cool = UiKit.Label(rt, "", at2 + new Vector2(0, 36), new Vector2(200, 110), 78, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.45f));
                c.Num = UiKit.Label(rt, (i + 1).ToString(), at2 + new Vector2(-CellPx / 2 + 26, CellPx / 2 - 28), new Vector2(40, 40), 28, TextAnchor.MiddleCenter, UiKit.Dim);
                p._cells.Add(c);
            }
            if (keys != null) keys.Changed += () => p._dirty = true;
            if (inv != null) inv.Changed += () => p._dirty = true;
            if (prog != null) prog.Changed += () => p._dirty = true;
            p._look = CastLook.Capture(rt); CastLook.Own(p._look, p._head, Theme.GoldLight); // client wave 21 (w21tools): built plain; drawn in the look in force from the first Show (its heading stands on the wood back: light gold, not a cell's ink)
            rt.gameObject.SetActive(false);
            return p;
        }

        /// <summary>Opens the pad around the fingertip: its middle cell (slot 5) on <paramref name="tip"/>, upright and turned to face
        /// <paramref name="eyes"/>, cells of VR/Comfort.cs PadCell; it stays there (in the room) until it is hidden.</summary>
        public void OpenAt(int row, Vector3 tip, Vector3 eyes) => OpenAt(row, tip, eyes, Vector3.zero, Quaternion.identity);
        /// <summary>Client wave 18 (w18flick), point 15: the same with the hand's offset and turn (<see cref="Pose"/>).</summary>
        public void OpenAt(int row, Vector3 tip, Vector3 eyes, Vector3 offset, Quaternion turn)
        {
            var (centre, rot) = Pose(tip, eyes, offset, turn);
            SetGhost(false);
            PlaceAt(centre, rot, VR.Comfort.PadCell);
            OpenedTip = tip;
            Show(row);
        }
        /// <summary>Client wave 18 (w18flick): the middle cell's place at <paramref name="centre"/>, turned <paramref name="rot"/>, cells of
        /// <paramref name="cell"/> metres (clamped to the "Pad size" range).</summary>
        public void PlaceAt(Vector3 centre, Quaternion rot, float cell)
        {
            cell = Mathf.Clamp(cell, VR.Comfort.PadCellMin, VR.Comfort.PadCellMax); float s = cell / CellPx;
            float parent = _rt.parent != null ? Mathf.Max(_rt.parent.lossyScale.x, 1e-6f) : 1f;
            _rt.rotation = rot; _rt.localScale = Vector3.one * (s / parent);
            _rt.position = centre - rot * ((Vector3)CellPos(4) * s);
            OpenedAt = centre; CellMetres = cell;
        }
        /// <summary>Upright and turned to face the eyes along <paramref name="face"/> (wave 13's rule; a hand straight under the eyes: still
        /// upright enough).</summary>
        public static Quaternion Facing(Vector3 face)
        {
            if (face.sqrMagnitude < 1e-6f) face = Vector3.forward;
            var up = Vector3.up; if (Mathf.Abs(Vector3.Dot(face.normalized, up)) > 0.98f) up = Vector3.ProjectOnPlane(Vector3.forward, face).normalized;
            return Quaternion.LookRotation(face, up);
        }
        /// <summary>Client wave 18 (w18flick), point 15: where a pad opened at <paramref name="tip"/> goes: its middle cell at the fingertip +
        /// <paramref name="offset"/> (metres in the frame facing the eyes at the fingertip: right, up, away from the eyes), facing the eyes
        /// from there, then turned by <paramref name="turn"/>. Zero and identity: wave 13's place (the middle cell on the fingertip).</summary>
        public static (Vector3 centre, Quaternion rot) Pose(Vector3 tip, Vector3 eyes, Vector3 offset, Quaternion turn)
        {
            var at = Facing(tip - eyes); var centre = tip + at * offset;
            var face = centre - eyes; if (face.sqrMagnitude < 1e-6f) face = tip - eyes;
            return (centre, Facing(face) * turn);
        }
        /// <summary>The inverse of <see cref="Pose"/>: the offset and turn that put a pad's middle cell at <paramref name="centre"/>, turned
        /// <paramref name="rot"/>, for a fingertip at <paramref name="tip"/>.</summary>
        public static (Vector3 offset, Quaternion turn) PlaceOf(Vector3 tip, Vector3 eyes, Vector3 centre, Quaternion rot)
        {
            var at = Facing(tip - eyes); var face = centre - eyes; if (face.sqrMagnitude < 1e-6f) face = tip - eyes;
            return (Quaternion.Inverse(at) * (centre - tip), Quaternion.Inverse(Facing(face)) * rot);
        }
        /// <summary>Client wave 18 (w18flick), point 15: shown as Reposition's ghost of <paramref name="row"/> (see-through; lit brighter).</summary>
        public void ShowGhost(int row, bool lit)
        {
            SetGhost(true); if (_group != null) _group.alpha = lit ? GhostLitAlpha : GhostAlpha;
            if (!Shown || Row != row) Show(row);
        }
        public void SetGhostLit(bool lit) { if (Ghost && _group != null) _group.alpha = lit ? GhostLitAlpha : GhostAlpha; }
        private void SetGhost(bool on)
        {
            Ghost = on;
            if (_group == null) { if (!on) return; _group = _rt.gameObject.AddComponent<CanvasGroup>(); _group.interactable = false; _group.blocksRaycasts = false; }
            _group.alpha = on ? GhostAlpha : 1f;
        }
        /// <summary>Client wave 18 (w18flick): the pad's outline in the world: its middle cell, half its width and half its height (the 3 x 3;
        /// Reposition's tests).</summary>
        public (Vector3 c, Vector3 r, Vector3 u) FrameWorld { get { float h = 1.5f * PitchPx * _rt.lossyScale.x; return (CellWorld(4), _rt.right * h, _rt.up * h); } }
        /// <summary>Cell i's middle on the pad, in pixels (0 top left … 8 bottom right).</summary>
        public static Vector2 CellPos(int i) => new Vector2((i % 3 - 1) * PitchPx, -HeadPx / 2 + (1 - i / 3) * PitchPx);
        /// <summary>Cell i's middle in the world (smoke; the fingertip goes there).</summary>
        public Vector3 CellWorld(int i) => _rt.TransformPoint(CellPos(i));

        public void Show(int row) { Row = row; Marked = -1; _dirty = true; if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true); CastLook.Sync(_look); Paint(); }
        public void Hide() { Marked = -1; if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); if (Ghost) SetGhost(false); } // (client wave 18: a ghost is over)
        public void Mark(int cell) { if (cell != Marked) { Marked = cell; _dirty = true; } }

        /// <summary>The cell the fingertip is in (-1: none: beside the pad, or too far in front of / behind it); <paramref name="current"/>
        /// is kept while the tip is within the margin of it.</summary>
        public int CellAt(Vector3 tip, int current)
        {
            var l = _rt.InverseTransformPoint(tip);
            if (Mathf.Abs(l.z) * _rt.lossyScale.x > Depth) return -1; // (metres)
            if (current >= 0)
            {
                var c = CellPos(current);
                if (Mathf.Abs(l.x - c.x) <= PitchPx / 2 + KeepPx && Mathf.Abs(l.y - c.y) <= PitchPx / 2 + KeepPx) return current;
            }
            float x = l.x + 1.5f * PitchPx, y = (-HeadPx / 2 + 1.5f * PitchPx) - l.y;
            if (x < 0 || y < 0 || x >= 3 * PitchPx || y >= 3 * PitchPx) return -1;
            return Mathf.FloorToInt(y / PitchPx) * 3 + Mathf.FloorToInt(x / PitchPx);
        }

        private void Paint()
        {
            _dirty = false;
            _head.text = Lang.F("Bar {0} · row {1}", Row / Hotkeys.RowsPerTab + 1, Row % Hotkeys.RowsPerTab + 1);
            var mark = CastLook.C(MarkColour); // (client wave 21, w21tools: the chosen cell in the look in force)
            var rowColour = RowColours.Of(Row); if (_border != null) _border.color = rowColour; // client wave 20 (w20colors): the row's colour
            for (int i = 0; i < 9; i++)
            {
                var c = _cells[i]; var h = _keys != null ? _keys.At(Row, i) : default;
                foreach (var r in c.Rim) r.color = rowColour;
                // client wave 21 (w21tools): themed, the lettering is the ink on a cream cell and pale on the chosen (dark) one; plain: never touched
                if (!CastLook.Plain) { bool m = i == Marked; c.Name.color = CastLook.Name(m); c.Count.color = CastLook.Number(m); c.Cool.color = CastLook.Wait(m); c.Num.color = CastLook.Small(m); }
                if (h.Empty) { c.Icon.enabled = false; c.Name.text = ""; c.Count.text = ""; c.Bg.color = i == Marked ? new Color(mark.r * 0.6f, mark.g * 0.6f, mark.b * 0.6f, 1f) : CellFill(Row, true); continue; }

                if (h.IsSkill) { var info = SkillDb.Get(h.Id); c.Icon.sprite = WristMenu.SkillIcon(info); c.Name.text = info.Name; c.Count.text = _prog != null && _prog.Skills.ContainsKey(h.Id) ? "" : "—"; }
                else { var info = GameDb.GetItem(h.Id); c.Icon.sprite = WristMenu.ItemIcon(info); c.Name.text = info.Name; c.Count.text = (_inv != null ? _inv.Count(h.Id) : 0).ToString(); }
                c.Icon.enabled = c.Icon.sprite != null;
                c.Bg.color = i == Marked ? mark : CellFill(Row, false);
            }
        }

        private void LateUpdate()
        {
            if (!_rt.gameObject.activeSelf) return;
            if (CastLook.Sync(_look)) _dirty = true; // client wave 21 (w21tools): "Casting look" changed while it is shown
            if (_dirty) Paint();
            for (int i = 0; i < 9; i++)
            {
                float left = _bar != null ? _bar.BlockedAt(Row, i).left : 0f;
                string t = left > 0.05f ? (left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0")) : "";
                if (_cells[i].Cool.text != t) _cells[i].Cool.text = t;
            }
        }
    }
}
