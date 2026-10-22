using System.Collections.Generic;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The forearm quick bar: one row of 9 slots of the server-saved hotkey bar (Game/Hotkeys.cs: 2 tabs × 4 rows of 9) as buttons
    /// floating over the walking hand's forearm, pressed with the laser — potions and skills without opening the wrist menu. Hidden
    /// while the wrist menu is open (its Quick tab shows the same row) and when switched off in Settings.
    /// Client wave 4 (w4vr): all 9 slots of the row, the bar switcher (▲ / ▼ step through the 8 rows; 0x0b22 saves the row shift on
    /// the server) and cooldown overlays: a skill's own cooldown from ZC_SKILL_POSTDELAY 0x043d (clif_skill_cooldown clif.cpp:6036-6046,
    /// sent by skill_blockpc_start skill.cpp:13915-13931 when display_status_timers is on, conf/battle/client.conf:108; tick 0 clears
    /// it, skill.cpp:13937-13945) and the global after-cast delay on every skill slot from the EFST_POSTDELAY (46) status the server
    /// sends after each cast (skill.cpp:5107-5108; status.hpp:1457-1503 numbers it 46). A dark sector shrinks clockwise as the time
    /// runs out, the seconds in the middle — roBrowserLegacy draws the same (UI/Components/ShortCut/ShortCut.js:642-738, a
    /// reimplementation: the official client's look is not documented here).
    /// </summary>
    public sealed class QuickBar : MonoBehaviour
    {
        public const int Slots = Hotkeys.QuickSlots;
        private const float SlotW = 72, SwitchW = 92, W = SwitchW + Slots * SlotW + 14, H = 104, Scale = 0.00030f;
        private Transform _hand; private Camera _head; private Hotkeys _keys; private Inventory _inv; private Progression _prog;
        private System.Action<int> _use; private System.Func<bool> _hidden;
        private sealed class SlotUi { public UiButton Btn; public Image Icon, Cool; public Text Count, CoolText; }
        private readonly List<SlotUi> _slots = new List<SlotUi>();
        private Text _rowLabel; private UiButton _up, _down;
        private string _rowLabelEn = ""; // w4lang: the switcher's text in English, as RowLabel gives it to the smoke; the label shows the current language
        private bool _dirty = true, _shown = true; private RectTransform _rt; private BoxCollider _col; // _shown starts true: children start active, the first frame syncs them
        // cooldown totals seen per skill (0x043d gives the length once; the overlay needs it for the fill)
        private readonly Dictionary<int, (float until, float total)> _cd = new Dictionary<int, (float, float)>();
        private (float until, float total) _post;
        private static Sprite _white;

        public static QuickBar Create(Transform parent, Transform hand, Camera head, Hotkeys keys, Inventory inv, Progression prog, System.Action<int> use, System.Func<bool> hidden)
        {
            var rt = UiKit.Panel("QuickBar", parent, new Vector2(W, H), Scale, true, PanelKind.ByName, PanelLook.SeeThrough); HudLook.Backing(rt); // client wave 21 (w21tools): see-through HUD: the theme's gold line and corner scrolls, its dark backing kept (UI/HudLook.cs)
            var q = rt.gameObject.AddComponent<QuickBar>();
            q._rt = rt; q._col = rt.GetComponent<BoxCollider>(); q._hand = hand; q._head = head; q._keys = keys; q._inv = inv; q._prog = prog; q._use = use; q._hidden = hidden;
            if (_white == null) _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(0.5f, 0.5f), 100f);
            float x0 = -W / 2 + 7;
            // the bar switcher: ▲ previous row, ▼ next row, "bar b / row r" between them
            q._up = UiKit.Button(rt, "▲", new Vector2(x0 + SwitchW / 2, 35), new Vector2(SwitchW - 8, 30), () => keys?.SwitchRow(-1), 18);
            q._rowLabel = UiKit.Label(rt, "", new Vector2(x0 + SwitchW / 2, 0), new Vector2(SwitchW, 42), 14, TextAnchor.MiddleCenter, HudLook.Heading);
            q._rowLabel.lineSpacing = 0.85f;
            q._down = UiKit.Button(rt, "▼", new Vector2(x0 + SwitchW / 2, -35), new Vector2(SwitchW - 8, 30), () => keys?.SwitchRow(1), 18);
            q._up.Tip = () => Lang.T("Previous row"); q._down.Tip = () => Lang.T("Next row"); // client wave 14 (w14menu), point 7
            for (int i = 0; i < Slots; i++)
            {
                int slot = i; float x = x0 + SwitchW + SlotW / 2 + i * SlotW;
                var s = new SlotUi();
                s.Btn = UiKit.Button(rt, "", new Vector2(x, 0), new Vector2(SlotW - 6, 88), () => { q.Presses++; q.LastPressed = slot; q._use?.Invoke(slot); }, 18); // (client wave 18, w18share: a press is counted: friends see it)
                s.Icon = UiKit.Icon(rt, new Vector2(x, 6), new Vector2(46, 46));
                s.Cool = UiKit.Icon(rt, new Vector2(x, 0), new Vector2(SlotW - 8, 86));
                s.Cool.sprite = _white; s.Cool.preserveAspect = false; s.Cool.type = Image.Type.Filled; s.Cool.fillMethod = Image.FillMethod.Radial360;
                s.Cool.fillOrigin = (int)Image.Origin360.Top; s.Cool.fillClockwise = false; s.Cool.color = new Color(0f, 0f, 0f, 0.68f);
                s.CoolText = UiKit.Label(rt, "", new Vector2(x, 6), new Vector2(SlotW - 8, 40), 24, TextAnchor.MiddleCenter, HudLook.Text);
                s.Count = UiKit.Label(rt, "", new Vector2(x + 4, -28), new Vector2(SlotW - 6, 26), 17, TextAnchor.LowerRight, HudLook.Heading);
                UiKit.Label(rt, (i + 1).ToString(), new Vector2(x - 24, 30), new Vector2(20, 22), 15, TextAnchor.UpperLeft, HudLook.Dim);
                q._slots.Add(s);
            }
            if (keys != null) keys.Changed += () => q._dirty = true;
            inv.Changed += () => q._dirty = true; prog.Changed += () => q._dirty = true;
            return q;
        }

        public void SetHand(Transform hand) { _hand = hand; }
        /// <summary>Client wave 13 (w13view): the hand the bar is on (the elbow button sits on that arm).</summary>
        public Transform Hand => _hand;

        // smoke-test helpers
        public bool Shown => _shown;
        public bool SlotHasIcon(int i) => i >= 0 && i < Slots && _slots[i].Icon.enabled;
        /// <summary>The slot holds an item or skill (its button can be pressed).</summary>
        public bool SlotFilled(int i) => i >= 0 && i < Slots && _slots[i].Btn.Interactable;
        /// <summary>The icon's texture still exists (a destroyed one draws white).</summary>
        public bool SlotIconAlive(int i) => SlotHasIcon(i) && _slots[i].Icon.sprite != null && _slots[i].Icon.sprite.texture != null;
        public string SlotCount(int i) => i >= 0 && i < Slots ? _slots[i].Count.text : "";
        public void Press(int i) { if (i >= 0 && i < Slots) _slots[i].Btn.Click(); }
        /// <summary>The cooldown overlay of slot i: its fill (0 = none) and the seconds shown.</summary>
        public (float fill, string text) SlotCooldown(int i) => i >= 0 && i < Slots && _slots[i].Cool.enabled ? (_slots[i].Cool.fillAmount, _slots[i].CoolText.text) : (0f, "");
        public string RowLabel => _rowLabel != null ? _rowLabelEn.Replace("\n", " ") : "";

        private void LateUpdate()
        {
            // client wave 13 (w13view), the brief's thirteenth item: "always on / always off / elbow button" (VR/Comfort.cs QuickBarMode;
            // the elbow button: LokiGame.W13View.cs). Hidden: every part off, its canvas off, its collider off (it blocks no look and no
            // click); the object itself stays to keep the cooldowns' lengths the wheels and the pad read (BlockedAt)
            bool show = ShownBy != null ? ShownBy() : VR.Comfort.QuickBarShown && (_hidden == null || !_hidden()) && _keys != null && _keys.Loaded; // (client wave 18, w18share: a friend's bar by its own rule)
            if (show != _shown)
            {
                _shown = show;
                for (int i = 0; i < _rt.childCount; i++) _rt.GetChild(i).gameObject.SetActive(show);
                if (_col != null) _col.enabled = show;
                var cv = _rt.GetComponent<Canvas>(); if (cv != null) cv.enabled = show;
                if (show) _dirty = true;
            }
            TrackCooldowns();
            if (!show) return;
            if (_hand != null && _head != null) ForearmPlace(transform, _hand.position, _hand.forward, _head.transform.position, 1f); // (client wave 18, w18share: the rule shared with a friend's bar)
            if (_dirty)
            {
                _dirty = false;
                _rowLabelEn = $"bar {_keys.CurrentTab + 1}\nrow {_keys.CurrentRowInTab + 1}";
                _rowLabel.text = Lang.F("bar {0}\nrow {1}", _keys.CurrentTab + 1, _keys.CurrentRowInTab + 1);
                for (int i = 0; i < Slots; i++)
                {
                    var s = _slots[i];
                    var h = _keys.Quick(i);
                    if (h.Empty) { s.Icon.enabled = false; s.Count.text = ""; s.Btn.Interactable = false; continue; }
                    s.Btn.Interactable = true;
                    if (h.IsSkill) { var info = SkillDb.Get(h.Id); s.Icon.sprite = WristMenu.SkillIcon(info); s.Count.text = _prog.Skills.TryGetValue(h.Id, out var k) ? "" : "—"; }
                    else { var info = GameDb.GetItem(h.Id); s.Icon.sprite = WristMenu.ItemIcon(info); int n = _inv.Count(h.Id); s.Count.text = n > 0 ? n.ToString() : "0"; }
                    s.Icon.enabled = s.Icon.sprite != null;
                }
            }
            DrawCooldowns();
        }

        // ---- client wave 18 (w18share), the design's point 33: a friend's forearm bar is this same class (UI/FriendGestures.cs: its own Hotkeys,
        // Inventory and Progression copies written from the friend's menu, no hand, no head, no colliders)
        /// <summary>A friend's bar: shown by this rule (their menu's state), never by my own settings; null: my own bar.</summary>
        public System.Func<bool> ShownBy;
        /// <summary>The slot the laser (or the look) is on now (-1 none); a friend's copy lights theirs.</summary>
        public int HoveredSlot { get { for (int i = 0; i < Slots; i++) if (_slots[i].Btn.Hovered) return i; return -1; } }
        public void SetHovered(int slot) { for (int i = 0; i < Slots; i++) if (_slots[i].Btn.Hovered != (i == slot)) _slots[i].Btn.SetHover(i == slot); }
        /// <summary>The slots pressed so far and the last one (-1 none): a friend sees the press.</summary>
        public int Presses { get; private set; }
        public int LastPressed { get; private set; } = -1;
        /// <summary>Where the bar rides: along the forearm, a little behind the hand (towards the elbow), just above it, facing the eyes;
        /// <paramref name="size"/>: a friend's bar at the size their headset is drawn (VR/HeadsetAvatars.cs Avatar.Size).</summary>
        public static void ForearmPlace(Transform bar, Vector3 hand, Vector3 handForward, Vector3 eye, float size)
        {
            var back = -handForward; back.y = 0; if (back.sqrMagnitude > 1e-4f) back.Normalize();
            bar.position = hand + (back * 0.13f + Vector3.up * 0.07f) * size;
            var d = bar.position - eye; if (d.sqrMagnitude >= 1e-6f) bar.rotation = Quaternion.LookRotation(d.normalized, Vector3.up); // (UiKit.FaceHead's rule)
        }

        /// <summary>Remembers each cooldown's full length when a new one starts (the overlay's fill is the part still to run).</summary>
        private void TrackCooldowns()
        {
            float now = Time.time;
            foreach (var kv in _prog.Skills)
            {
                float until = kv.Value.CooldownUntil;
                if (until <= now) continue;
                if (!_cd.TryGetValue(kv.Key, out var c) || Mathf.Abs(c.until - until) > 0.01f) _cd[kv.Key] = (until, until - now);
            }
            if (_prog.Effects.TryGetValue(PostDelayEfst, out var e) && e.Timed && e.EndsAt > now && Mathf.Abs(_post.until - e.EndsAt) > 0.01f) _post = (e.EndsAt, e.EndsAt - now);
        }

        /// <summary>EFST_POSTDELAY (map/status.hpp:1503; 46 counted from EFST_PROVOKE = 0 at :1457).</summary>
        public const int PostDelayEfst = 46;

        /// <summary>The remaining time and length of what blocks slot <paramref name="i"/>'s skill: its cooldown or the global after-cast delay.</summary>
        public (float left, float total) Blocked(int i) => BlockedAt(_keys != null ? _keys.CurrentRow : 0, i);

        /// <summary>Client wave 12 (w12cast): the same for slot i of any row (the casting pad shows other rows than the bar's).</summary>
        public (float left, float total) BlockedAt(int row, int i)
        {
            var h = _keys != null ? _keys.At(row, i) : default;
            if (h.Empty || !h.IsSkill) return (0f, 0f);
            return BlockedSkill(h.Id);
        }

        /// <summary>Client wave 12 (w12cast): the same for a skill by its id (the wand's own quick slots are not on the bar).</summary>
        public (float left, float total) BlockedSkill(int skillId)
        {
            float now = Time.time, left = 0f, total = 0f;
            if (_prog.Skills.TryGetValue(skillId, out var k) && k.CooldownUntil > now && _cd.TryGetValue(skillId, out var c)) { left = k.CooldownUntil - now; total = Mathf.Max(c.total, left); }
            if (_post.until > now && _post.until - now > left) { left = _post.until - now; total = Mathf.Max(_post.total, left); }
            return (left, total);
        }

        private void DrawCooldowns()
        {
            for (int i = 0; i < Slots; i++)
            {
                var s = _slots[i];
                var (left, total) = Blocked(i);
                bool on = left > 0.02f && total > 0f;
                s.Cool.enabled = on;
                if (!on) { if (s.CoolText.text.Length > 0) s.CoolText.text = ""; continue; }
                s.Cool.fillAmount = Mathf.Clamp01(left / total);
                s.CoolText.text = left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0");
            }
        }
    }

    /// <summary>
    /// Client wave 4 (w4vr): the button strip over the walking wrist, for hands and controllers without the face buttons (Vive
    /// wands, the Khronos simple controller, tracked hands: ControllerProfiles.LacksFaceButtons), or always / never (Settings,
    /// Controls page). Menu (wrist menu), Map (minimap), Back (cancel a skill / stop / chat), Next (NPC next / sit), Centre (bring
    /// the table back) and View (Table → Follow → First person) — the same actions as X / Y / B / A and the walking stick's click
    /// (UI/LokiHud.cs). Stays shown while the wrist menu is open, so Menu can close it again.
    /// </summary>
    public sealed class ButtonStrip : MonoBehaviour
    {
        private const float W = 6 * 118 + 16, H = 88, Scale = 0.00030f;
        private Transform _hand; private Camera _head; private System.Func<bool> _wanted;
        private RectTransform _rt; private BoxCollider _col; private bool _shown = true;
        private readonly List<UiButton> _buttons = new List<UiButton>();
        public bool Shown => _shown;
        public int ButtonCount => _buttons.Count;
        public string Label(int i) => i >= 0 && i < _buttons.Count ? _buttons[i].Text.text : "";
        public void Press(int i) { if (i >= 0 && i < _buttons.Count) _buttons[i].Click(); }

        public static ButtonStrip Create(Transform parent, Transform hand, Camera head, System.Func<bool> wanted, params (string label, System.Action action)[] buttons)
        {
            var rt = UiKit.Panel("ButtonStrip", parent, new Vector2(W, H), Scale, true, PanelKind.ByName, PanelLook.SeeThrough); HudLook.Backing(rt); // client wave 21 (w21tools): see-through HUD: the theme's gold line and corner scrolls, its dark backing kept (UI/HudLook.cs)
            var b = rt.gameObject.AddComponent<ButtonStrip>();
            b._rt = rt; b._col = rt.GetComponent<BoxCollider>(); b._hand = hand; b._head = head; b._wanted = wanted;
            for (int i = 0; i < buttons.Length; i++)
                b._buttons.Add(UiKit.Button(rt, buttons[i].label, new Vector2(-W / 2 + 8 + 59 + i * 118, 0), new Vector2(110, 74), buttons[i].action, 22));
            return b;
        }

        public void SetHand(Transform hand) { _hand = hand; }

        private void LateUpdate()
        {
            bool show = _wanted != null && _wanted();
            if (show != _shown)
            {
                _shown = show;
                for (int i = 0; i < _rt.childCount; i++) _rt.GetChild(i).gameObject.SetActive(show);
                if (_col != null) _col.enabled = show;
            }
            if (!show || _hand == null || _head == null) return;
            // under the quick bar on the forearm (QuickBar: 13 cm back, 7 cm up), facing the head
            var back = -_hand.forward; back.y = 0; if (back.sqrMagnitude > 1e-4f) back.Normalize();
            transform.position = _hand.position + back * 0.13f + Vector3.up * 0.03f;
            UiKit.FaceHead(transform, _head);
        }
    }
}
