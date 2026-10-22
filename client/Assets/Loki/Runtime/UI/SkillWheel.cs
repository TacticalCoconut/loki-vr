using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// One radial wheel of the quick bar (specified after the 2026-10-01 playtest): the 9 slots of one hotkey row (Game/Hotkeys.cs:
    /// 2 bars × 4 rows of 9) on a circle floating over a controller, slot 1 at the top and the others clockwise, the row's "bar / row"
    /// in the middle; the stick of that hand points at a slot (LokiGame.Wheels.cs drives two of them). Not pressed with the laser: no
    /// collider, so it never stands in the laser's way. Icons and counts as the forearm quick bar draws them (UI/QuickBar.cs).
    /// </summary>
    public sealed class SkillWheel : MonoBehaviour
    {
        public const int Slots = Hotkeys.QuickSlots;
        private const float Size = 420, Radius = 150, SlotPx = 92, Scale = 0.00030f;
        /// <summary>The stick has to be pushed this far before it points at a slot.</summary>
        public const float Deadzone = 0.5f;
        private Transform _hand; private Camera _head; private Hotkeys _keys; private Inventory _inv; private Progression _prog;
        private RectTransform _rt;
        private readonly List<Image> _bg = new List<Image>(), _icons = new List<Image>();
        private readonly List<Text> _counts = new List<Text>();
        private Text _label, _hint;
        private bool _dirty = true;
        private static readonly Color SlotColour = new Color(0.08f, 0.09f, 0.13f, 0.85f), PickColour = new Color(0.35f, 0.45f, 0.75f, 0.95f), ActiveColour = new Color(0.95f, 0.75f, 0.3f, 1f);

        /// <summary>The hotkey row shown (0 .. Hotkeys.Rows - 1), the slot pointed at (-1: none) and whether this wheel's pick is the one
        /// the trigger uses (the other wheel's stick moved last: dimmed).</summary>
        public int Row { get; private set; }
        public int Selected { get; private set; } = -1;
        public bool Active { get; private set; }
        public bool Shown => _rt != null && _rt.gameObject.activeSelf;

        public static SkillWheel Create(string name, Transform parent, Transform hand, Camera head, Hotkeys keys, Inventory inv, Progression prog, string hint)
        {
            var rt = UiKit.Panel(name, parent, new Vector2(Size, Size), Scale, false);
            var col = rt.GetComponent<BoxCollider>(); if (col != null) col.enabled = false;
            var w = rt.gameObject.AddComponent<SkillWheel>();
            w._rt = rt; w._hand = hand; w._head = head; w._keys = keys; w._inv = inv; w._prog = prog;
            for (int i = 0; i < Slots; i++)
            {
                var p = SlotPos(i);
                w._bg.Add(UiKit.Box(rt, "slot" + i, p, new Vector2(SlotPx, SlotPx), SlotColour).GetComponent<Image>());
                w._icons.Add(UiKit.Icon(rt, p + new Vector2(0, 6), new Vector2(54, 54)));
                w._counts.Add(UiKit.Label(rt, "", p + new Vector2(4, -30), new Vector2(SlotPx - 8, 26), 18, TextAnchor.LowerRight, new Color(1f, 0.85f, 0.45f)));
                UiKit.Label(rt, (i + 1).ToString(), p + new Vector2(-30, 32), new Vector2(24, 22), 15, TextAnchor.UpperLeft, UiKit.Dim);
            }
            w._label = UiKit.Label(rt, "", Vector2.zero, new Vector2(150, 60), 22, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.45f));
            w._label.lineSpacing = 0.85f;
            if (!string.IsNullOrEmpty(hint)) { w._hint = UiKit.Label(rt, hint, new Vector2(0, -Size / 2 - 10), new Vector2(Size + 120, 40), 18, TextAnchor.MiddleCenter, UiKit.Dim); }
            if (keys != null) keys.Changed += () => w._dirty = true;
            if (inv != null) inv.Changed += () => w._dirty = true;
            if (prog != null) prog.Changed += () => w._dirty = true;
            rt.gameObject.SetActive(false);
            return w;
        }

        /// <summary>Slot i's place on the circle: 1 at the top, then clockwise, 40° apart.</summary>
        public static Vector2 SlotPos(int i) { float a = i * (360f / Slots) * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Radius; }

        /// <summary>The slot a stick points at (-1 inside the dead zone): the nearest of the 9 directions, slot 1 straight ahead.</summary>
        public static int SlotAt(Vector2 stick)
        {
            if (stick.magnitude < Deadzone) return -1;
            float a = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg; if (a < 0) a += 360f;
            return Mathf.RoundToInt(a / (360f / Slots)) % Slots;
        }

        public void SetHand(Transform hand) { _hand = hand; }

        public void Show(int row)
        {
            Row = row; Selected = -1; Active = false; _dirty = true;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            Place();
        }

        public void Hide() { Selected = -1; Active = false; if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        public void Select(int i) { if (i != Selected) { Selected = i; _dirty = true; } }
        public void SetActive(bool a) { if (a != Active) { Active = a; _dirty = true; } }

        private void Place()
        {
            if (_hand == null || _head == null) return;
            // over the controller, a little towards the eyes, facing them
            var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
            transform.position = _hand.position + Vector3.up * 0.13f + toHead * 0.04f;
            UiKit.FaceHead(transform, _head);
        }

        private void LateUpdate()
        {
            if (!_rt.gameObject.activeSelf) return;
            Place();
            if (!_dirty || _keys == null) return;
            _dirty = false;
            _label.text = Lang.F("bar {0}\nrow {1}", Row / Hotkeys.RowsPerTab + 1, Row % Hotkeys.RowsPerTab + 1);
            for (int i = 0; i < Slots; i++)
            {
                var h = _keys.At(Row, i);
                if (h.Empty) { _icons[i].enabled = false; _counts[i].text = ""; }
                else if (h.IsSkill) { _icons[i].sprite = WristMenu.SkillIcon(SkillDb.Get(h.Id)); _counts[i].text = _prog != null && _prog.Skills.ContainsKey(h.Id) ? "" : "—"; }
                else { _icons[i].sprite = WristMenu.ItemIcon(GameDb.GetItem(h.Id)); _counts[i].text = (_inv != null ? _inv.Count(h.Id) : 0).ToString(); }
                if (!h.Empty) _icons[i].enabled = _icons[i].sprite != null;
                _bg[i].color = i == Selected ? (Active ? ActiveColour : PickColour) : SlotColour;
            }
        }
    }
}
