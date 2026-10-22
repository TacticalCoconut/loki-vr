using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.VR;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 18 (w18share), the design's point 33: the RECEIVING side. A friend's casting menus (UI/GestureShare.cs on their client, through
    /// the relay, which sends me only what their choice allows me) drawn at their floating headset's hands and head (VR/HeadsetAvatars.cs),
    /// the SAME picture by the SAME classes: a pad is a UI/CastPad.cs, a flick a UI/FlickHud.cs, the wand's wheel a UI/WandWheel.cs, the
    /// forearm bar a UI/QuickBar.cs, each made for that friend's menu with copies of the game's data classes (a Hotkeys made with no session,
    /// an Inventory, a Progression) written from the menu's state, so the icons, names, amounts, the greyed unlearned skills, the lit choice
    /// and the cursor are theirs; the wand is a copy of the wand's own model (VR/WandHolster.cs) in their hand.
    ///   Where      a picture fixed in their room at the same place of the map (so at their fingertip), one before their eyes in their
    ///              headset's frame, the bar on their forearm by the bar's own rule (QuickBar.ForearmPlace), the wand in their hand; at the
    ///              size their headset is drawn (HeadsetAvatars Avatar.Size: my table's scale against theirs).
    ///   How        see-through (<see cref="FriendAlpha"/>), read-only: no collider, no UiBlocker (the laser, the look and the panel grab
    ///              only ever meet colliders: VR/DioramaRig.Laser.cs, VR/DioramaRig.Ui.cs LaserOnPanel, UI/PanelGrab.cs by name), and of
    ///              the casting's own panel kinds (UI/UiKit.cs PanelKinds: CastPad*, Flick*, WandWheel are Own, QuickBar Hud), never a
    ///              menu: my casting is never switched off by a friend's picture. A take: the taken choice lit, fading out in
    ///              <see cref="TakeSeconds"/> s.
    ///   When       while their headset is shown here (it hides with their map, their silence, "Friends' headsets" off), their hand is
    ///              tracked (for what rides on it), and their client said so within <see cref="StaleSeconds"/> s (it repeats an open menu
    ///              every second: a frame lost on the way is right again a second later).
    /// </summary>
    [DefaultExecutionOrder(610)] // after HeadsetAvatars (600): the avatars are where this frame draws them
    public sealed class FriendGestures : MonoBehaviour
    {
        public const float FriendAlpha = 0.6f, TakeSeconds = 0.45f, StaleSeconds = 3f, ForgetSeconds = 120f;
        private IGestureChannel _ch; private HeadsetAvatars _heads; private World.MapLoader _map; private WandHolster _wand;

        public sealed class View
        {
            public readonly GestureWire.State S = new GestureWire.State();
            public float At = -99f, TakeAt = -1f; public bool Has, Shown, Fresh;
            public readonly Hotkeys Keys = new Hotkeys(null); public readonly Inventory Inv = new Inventory(); public readonly Progression Prog = new Progression();
            public readonly int[] Quick = new int[8]; public readonly bool[] QuickItem = new bool[8]; public string Sig = ""; public byte DrawnKind; public bool Reopen, HasMirrored;
            public CastPad Pad; public FlickHud Flick; public WandWheel Wheel; public QuickBar Bar; public Transform Wand, WandTip;
            public CanvasGroup PadG, FlickG, WheelG, BarG;
        }
        private sealed class Friend { public uint Aid; public Transform Root; public readonly View[] Views = new View[GestureWire.Menus]; public float SeenAt; }
        private readonly Dictionary<uint, Friend> _friends = new Dictionary<uint, Friend>();
        private readonly GestureWire.State _in = new GestureWire.State();
        /// <summary>Bodies taken from the channel, refused as malformed, pictures made (smoke, log).</summary>
        public int Received { get; private set; }
        public int Refused { get; private set; }
        public int Made { get; private set; }
        /// <summary>Takes seen (a menu's last picture marked TAKE: shown lit a moment).</summary>
        public int TakesSeen { get; private set; }

        public static FriendGestures Create(IGestureChannel ch, HeadsetAvatars heads, World.MapLoader map, WandHolster wand)
        {
            var f = new GameObject("FriendGestures").AddComponent<FriendGestures>();
            f._ch = ch; f._heads = heads; f._map = map; f._wand = wand;
            return f;
        }

        public View Get(uint aid, int menu) => _friends.TryGetValue(aid, out var f) && menu >= 0 && menu < GestureWire.Menus ? f.Views[menu] : null;

        private void LateUpdate()
        {
            if (_ch == null || _heads == null || _map == null) return;
            float now = Time.unscaledTime;
            while (_ch.TryTake(out uint aid, out var body))
            {
                Received++;
                if (!GestureWire.TryDecode(body, 0, body.Length, _in)) { Refused++; if (Refused <= 5) Debug.LogWarning($"[Menus] a casting menu of AID {aid} could not be read ({body.Length} bytes): not drawn"); continue; }
                Take(aid, _in, now);
            }
            var forget = (List<uint>)null;
            foreach (var f in _friends.Values)
            {
                bool seen = _heads.TryGet(f.Aid, out var a) && a.Visible;
                if (seen) f.SeenAt = now;
                else if (now - f.SeenAt > ForgetSeconds) (forget ??= new List<uint>()).Add(f.Aid);
                for (int m = 0; m < GestureWire.Menus; m++) if (f.Views[m] != null) Draw(f, m, f.Views[m], seen ? a : null, now);
            }
            if (forget != null) foreach (var id in forget) { if (_friends[id].Root != null) Destroy(_friends[id].Root.gameObject); _friends.Remove(id); }
        }

        /// <summary>A friend's menu arrived: kept, its data copies rewritten when its slots changed.</summary>
        private void Take(uint aid, GestureWire.State s, float now)
        {
            if (!_friends.TryGetValue(aid, out var f))
            {
                f = new Friend { Aid = aid, SeenAt = now };
                f.Root = new GameObject($"friend menus:{aid}").transform; f.Root.SetParent(transform, false);
                JobTheme.Own(f.Root, aid); // client wave 21 (w21jobui): a friend's copies wear THEIR job's variant of the panel theme, not ours (UI/JobTheme.cs)
                _friends[aid] = f;
                Debug.Log($"[Menus] first casting menu of AID {aid}");
            }
            var v = f.Views[s.Menu] ??= new View();
            bool wasHas = v.Has && (v.S.Flags & GestureWire.FlagTake) == 0;
            bool sameSlots = v.HasMirrored && SameSlots(v.S, s);
            Copy(s, v.S); v.At = now;
            if (s.Kind == GestureWire.KindClosed) { v.Has = false; v.TakeAt = -1f; return; }
            v.Has = true;
            v.TakeAt = (s.Flags & GestureWire.FlagTake) != 0 ? now : -1f;
            if (v.TakeAt >= 0f) { TakesSeen++; Debug.Log($"[Menus] AID {aid}: menu {s.Menu} took its lit choice"); }
            // the data copies: rewritten (and the picture made again from them) only when what the menu shows changed
            if (!sameSlots || !wasHas || v.DrawnKind != s.Kind) { v.HasMirrored = true; Mirror(v, s); v.Reopen = true; }
        }

        private static bool SameSlots(GestureWire.State a, GestureWire.State b)
        {
            if (a.Kind != b.Kind || a.Row != b.Row || a.Tab != b.Tab || a.Slices != b.Slices || a.Entries.Count != b.Entries.Count) return false;
            for (int i = 0; i < a.Entries.Count; i++)
            {
                var x = a.Entries[i]; var y = b.Entries[i];
                if (x.Row != y.Row || x.Slot != y.Slot || x.Skill != y.Skill || x.Listed != y.Listed || x.Learned != y.Learned || x.Id != y.Id || x.Count != y.Count)
                    return false;
            }
            return true;
        }

        private static void Copy(GestureWire.State a, GestureWire.State b)
        {
            b.Clear();
            b.Audience = a.Audience; b.Menu = a.Menu; b.Kind = a.Kind; b.Flags = a.Flags; b.Anchor = a.Anchor;
            b.X = a.X; b.Y = a.Y; b.Z = a.Z; b.Qx = a.Qx; b.Qy = a.Qy; b.Qz = a.Qz; b.Qw = a.Qw; b.Scale = a.Scale;
            b.Row = a.Row; b.Lit = a.Lit; b.Phase = a.Phase; b.Dir1 = a.Dir1; b.Dir2 = a.Dir2; b.CornerX = a.CornerX; b.CornerY = a.CornerY;
            b.Tab = a.Tab; b.Part = a.Part; b.Index = a.Index; b.Sub = a.Sub; b.Slices = a.Slices; b.Visible = a.Visible; b.InPlace = a.InPlace;
            b.CursorX = a.CursorX; b.CursorY = a.CursorY; b.Entries.AddRange(a.Entries);
        }

        /// <summary>The friend's data as their menu showed it: every slot not in it empty; skills in the skill list (learned at level 1, else 0);
        /// items in a bag of their amounts; the wand's inner ring; the row shown (Hotkeys.ShowMirrored: no packet, nothing saved).</summary>
        private static void Mirror(View v, GestureWire.State s)
        {
            System.Array.Clear(v.Keys.Slots, 0, v.Keys.Slots.Length); v.Inv.Items.Clear(); v.Prog.Skills.Clear(); System.Array.Clear(v.Quick, 0, v.Quick.Length); System.Array.Clear(v.QuickItem, 0, v.QuickItem.Length);
            int index = 2;
            foreach (var e in s.Entries)
            {
                if (e.Row == GestureWire.RowWand) { v.Quick[e.Slot] = e.Id; v.QuickItem[e.Slot] = !e.Skill; }
                else v.Keys.Slots[Hotkeys.SlotIndex(e.Row, e.Slot)] = new Hotkeys.Slot { IsSkill = e.Skill, Id = e.Id, Count = 1 };
                if (e.Skill && e.Listed && !v.Prog.Skills.ContainsKey(e.Id)) v.Prog.Skills[e.Id] = new Progression.SkillEntry { Id = e.Id, Level = e.Learned ? 1 : 0 };
                if (!e.Skill && e.HasCount && e.Count > 0 && v.Inv.Count(e.Id) == 0) v.Inv.Items[index] = new Inventory.Entry { Index = index++, ItemId = e.Id, Amount = e.Count, Identified = true };
            }
            v.Keys.ShowMirrored(s.Kind == GestureWire.KindPad || s.Kind == GestureWire.KindBar ? s.Row : s.Tab * Hotkeys.RowsPerTab);
        }

        private void Draw(Friend f, int menu, View v, HeadsetAvatars.Avatar a, float now)
        {
            var s = v.S;
            bool taking = v.TakeAt >= 0f && now - v.TakeAt < TakeSeconds;
            if (v.TakeAt >= 0f && !taking) { if (s.Kind != GestureWire.KindBar) v.Has = false; v.TakeAt = -1f; } // (the bar stays: a press lights its slot a moment)
            v.Fresh = now - v.At <= StaleSeconds;
            Transform anchor = null; bool ok = a != null && v.Has && v.Fresh && s.Kind != GestureWire.KindClosed;
            if (ok && s.Anchor != GestureWire.AnchorMap)
            {
                anchor = s.Anchor == GestureWire.AnchorHead ? a.Head : s.Anchor == GestureWire.AnchorLeft ? a.Left : a.Right;
                ok = anchor != null && anchor.gameObject.activeInHierarchy; // (their hand is not tracked: what rides on it is not drawn)
            }
            if (ok && anchor == null && (s.Kind == GestureWire.KindBar || s.Kind == GestureWire.KindWand)) ok = false; // (they ride on a hand)
            byte kind = ok ? s.Kind : GestureWire.KindClosed;
            if (kind != v.DrawnKind) { HideAll(v); v.Reopen = true; }
            v.Shown = ok; v.DrawnKind = kind;
            if (!ok) return;
            float alpha = FriendAlpha * (taking && s.Kind != GestureWire.KindBar ? 1f - (now - v.TakeAt) / TakeSeconds : 1f), size = a.Size;
            Vector3 pos; Quaternion rot;
            var q = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw); q.Normalize();
            if (anchor == null) { var m = _map.transform; pos = m.TransformPoint(new Vector3(s.X, s.Y, s.Z)); rot = m.rotation * q; }
            else { pos = anchor.TransformPoint(new Vector3(s.X, s.Y, s.Z)); rot = anchor.rotation * q; } // (the avatar's parts are drawn at Size: metres x Size)
            bool front = (s.Flags & GestureWire.FlagFront) != 0;
            switch (kind)
            {
                case GestureWire.KindPad:
                {
                    if (v.Pad == null) { v.Pad = CastPad.Create(f.Root, menu == GestureWire.MenuRight, v.Keys, v.Inv, v.Prog, null); v.PadG = Strip(v.Pad.gameObject); Made++; }
                    if (v.Reopen || v.Pad.Row != s.Row || !v.Pad.Shown) { v.Pad.Show(s.Row); v.Reopen = false; }
                    v.Pad.Mark(s.Lit);
                    Pose(v.Pad.transform, pos, rot, s.Scale * size); v.PadG.alpha = alpha;
                    break;
                }
                case GestureWire.KindFlick:
                {
                    if (v.Flick == null) { v.Flick = FlickHud.Create(f.Root, menu == GestureWire.MenuLeft ? "FlickLeft" : "FlickRight", v.Keys, v.Inv, v.Prog); v.FlickG = Strip(v.Flick.gameObject); Made++; }
                    if (v.Reopen || !v.Flick.Shown) { v.Flick.Begin(pos, rot); v.Reopen = false; }
                    Pose(v.Flick.transform, pos, rot, s.Scale * size);
                    v.Flick.SetDotScale(front ? WandWheel.FrontDot : 1f); Order(v.Flick.transform, front ? WandWheel.FrontSortingOrder : WandWheel.WandSortingOrder);
                    v.Flick.Draw(s.Phase, s.Dir1, s.Dir2, new Vector2(s.CornerX, s.CornerY), new Vector2(s.CursorX, s.CursorY));
                    v.FlickG.alpha = alpha;
                    break;
                }
                case GestureWire.KindWheel:
                {
                    if (v.Wheel == null) { v.Wheel = WandWheel.Create(f.Root, v.Keys, v.Inv, v.Prog, null); v.WheelG = Strip(v.Wheel.gameObject); Made++; }
                    if (v.Reopen || !v.Wheel.Shown) { v.Wheel.OpenAsFriend(pos, rot, v.Quick, v.QuickItem, s.Tab, s.Slices); v.Reopen = false; } // (the friend's slices and items: WandWheel.OpenAsFriend)
                    Pose(v.Wheel.transform, pos, rot, s.Scale * size);
                    v.Wheel.ShowAsFront(front);
                    int region = s.Part == GestureWire.PartInner ? WandWheelGeo.Inner + s.Index : s.Part == GestureWire.PartOuter ? WandWheelGeo.Outer + s.Index : s.Part == GestureWire.PartCell ? WandWheelGeo.Cell + s.Index : WandWheelGeo.None;
                    v.Wheel.Draw(region, s.Sub, new Vector2(s.CursorX, s.CursorY), s.Visible || taking, s.InPlace);
                    v.WheelG.alpha = alpha;
                    break;
                }
                case GestureWire.KindBar:
                {
                    if (v.Bar == null) { var b = QuickBar.Create(f.Root, null, null, v.Keys, v.Inv, v.Prog, null, null); v.Bar = b; b.ShownBy = () => v.Shown; v.BarG = Strip(b.gameObject); Made++; }
                    if (v.Reopen) { v.Keys.ShowMirrored(s.Row); v.Reopen = false; }
                    QuickBar.ForearmPlace(v.Bar.transform, anchor.position, anchor.forward, a.Head.position, size);
                    v.Bar.transform.localScale = Vector3.one * (s.Scale * size);
                    v.Bar.SetHovered(s.Lit);
                    v.BarG.alpha = alpha;
                    break;
                }
                case GestureWire.KindWand:
                {
                    if (v.Wand == null && _wand != null && _wand.Wand != null) { v.Wand = Instantiate(_wand.Wand.gameObject, f.Root, false).transform; v.Wand.name = "friend wand"; v.WandTip = v.Wand.Find("tip"); StripWand(v.Wand); Made++; }
                    if (v.Wand == null) break;
                    if (v.Wand.parent != anchor) v.Wand.SetParent(anchor, false);
                    // (the hand is drawn at Size) client wave 21 (w21wand): the friend's wand at THEIR size (its length; the thickness and the
                    // tip's bead as mine are drawn: VR/WandHolster.cs ApplySize) and its lean in the hand (identity from an older client)
                    v.Wand.localPosition = Vector3.zero; v.Wand.localRotation = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw).normalized;
                    if (v.Wand.localScale.z != s.Scale) WandHolster.ApplySize(v.Wand, s.Scale, v.WandTip);
                    if (!v.Wand.gameObject.activeSelf) v.Wand.gameObject.SetActive(true);
                    break;
                }
            }
        }

        private static void Pose(Transform t, Vector3 pos, Quaternion rot, float scale) { t.SetPositionAndRotation(pos, rot); t.localScale = Vector3.one * scale; } // (the friend's root: scale 1)
        private static void Order(Transform t, int order) { var c = t.GetComponent<Canvas>(); if (c != null && c.sortingOrder != order) c.sortingOrder = order; }

        private void HideAll(View v)
        {
            if (v.Pad != null) v.Pad.Hide();
            if (v.Flick != null) v.Flick.Hide();
            if (v.Wheel != null) v.Wheel.Close();
            if (v.Wand != null && v.Wand.gameObject.activeSelf) v.Wand.gameObject.SetActive(false);
            // (the bar hides by its own rule: ShownBy reads v.Shown)
        }

        /// <summary>A friend's copy is read-only: every collider and UiBlocker off and gone (the laser, the look and the panel grab meet
        /// nothing of it), drawn see-through by a CanvasGroup that takes no raycast.</summary>
        private static CanvasGroup Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (var b in go.GetComponentsInChildren<UiBlocker>(true)) Destroy(b);
            var g = go.AddComponent<CanvasGroup>(); g.interactable = false; g.blocksRaycasts = false; g.alpha = FriendAlpha;
            return g;
        }

        /// <summary>A copy of the wand's model: its own materials (my wand's cue never lights theirs), no ghost (that one is drawn for the
        /// wand's owner, who must find it through the world), no collider.</summary>
        private static void StripWand(Transform w)
        {
            foreach (var c in w.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (var r in w.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.EndsWith("Ghost")) { r.enabled = false; Destroy(r.gameObject); continue; }
                r.sharedMaterial = new Material(r.sharedMaterial);
            }
        }
    }
}
