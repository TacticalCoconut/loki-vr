using Loki.Game;
using Loki.Net;
using Loki.VR;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 18 (w18share), the design's point 33 ("whenever one opens up one of their cast gesture menus, so for example, the arm menu that
    /// stays on the elbow, the flick motion, the wand menu, all of those need to be visible to everyone. So when I open the flick menu, my
    /// friend needs to also be able to see what I'm seeing") and its addition ("can be toggled for privacy to only be visible to friends,
    /// family, or clan members", "and/or"; "I meant my Guild"). The SENDING side: once a frame it looks at my own casting pictures as they are drawn (it changes
    /// nothing in them) and sends each one's state on <see cref="IGestureChannel"/> (Net/GestureWire.cs): only on change, at most
    /// <see cref="MaxRate"/> a second per menu, and again every <see cref="KeepSeconds"/> s while it stays open; a closed one once more a
    /// second later. A friend's client draws the same picture with the same class (UI/FriendGestures.cs).
    ///   The menus   0 / 1  the left / right hand's casting picture: its pad (UI/CastPad.cs) or its flick (UI/FlickHud.cs; the wand's
    ///                      plain strokes draw on the holding hand's flick: the same picture);
    ///               2      the wand's wheel (UI/WandWheel.cs; not while Reposition shows it for placing);
    ///               3      the forearm bar (UI/QuickBar.cs: while it shows: always on, or opened at the elbow);
    ///               4      the wand in a hand (VR/WandHolster.cs: friends see no wand at all before this wave).
    ///   The place   a picture fixed in the room (the pad, the flick and the wheel where they opened) travels in the MAP's frame (where
    ///               the floating headsets' poses are: VR/HeadsetAvatars.cs), so it stands at my fingertip on every table; one before my
    ///               eyes (the head's child) in my head's frame; the bar and the wand by the hand that carries them (a friend's copy is
    ///               placed by the bar's own rule from my head and hand: QuickBar.ForearmPlace).
    ///   What        the rows / slots as skill and item ids (with an item's amount and whether a skill is learned: the picture shows both),
    ///               the cursor (the flick's hand, the wheel's tip) and the lit choice; when a gesture ends with a slot taken (the game's
    ///               take counter moved in that frame: LokiGame.W12Takes) its last state goes once more marked TAKE. Cooldown seconds are
    ///               not sent (they count down by themselves on my side only).
    ///   Who         Settings → Headsets "Casting menus shown" (on) and "Seen by" (Everyone; or any of Friends, Family, Guild): the audience
    ///               rides in every frame and the RELAY sends it to nobody else (tools/lokihost/Core/PoseRelay.cs Allowed). Off, or none of
    ///               the three: nothing is sent. Nothing either while my headset is not shared (HeadsetAvatars.Sharing: a desktop client,
    ///               "Share my headset" off, off the map), or on a relay that does not forward menus. A change of the choice closes every
    ///               open menu for the old audience first.
    /// </summary>
    [DefaultExecutionOrder(620)] // after the casting (LokiGame.Update) and the pictures' own LateUpdate, after HeadsetAvatars (600)
    public sealed class GestureShare : MonoBehaviour
    {
        public const float MaxRate = 15f, KeepSeconds = 1f;
        private IGestureChannel _ch; private LokiGame _game; private DioramaRig _rig; private World.MapLoader _map; private HeadsetAvatars _heads;
        private sealed class Out
        {
            public readonly GestureWire.State S = new GestureWire.State();
            public byte[] Last, LastOpen; public float LastAt = -99f, BuiltAt = -99f, CloseAgainAt = -1f; public bool Open; public byte Audience; public byte Kind;
        }
        private readonly Out[] _out = new Out[GestureWire.Menus];
        private int _links = -1, _takes, _presses; private string _onMap; private bool _wasOn;
        /// <summary>Frames handed to the channel, menus opened, takes sent (smoke, log).</summary>
        public int Frames { get; private set; }
        public int Opened { get; private set; }
        public int TakesSent { get; private set; }
        /// <summary>Per menu: open now, the last frame's kind (smoke).</summary>
        public bool IsOpen(int menu) => _out[menu].Open;
        public byte AudienceNow => Comfort.MenusAudience;
        public IGestureChannel Channel => _ch;

        public static GestureShare Create(LokiGame game, IGestureChannel ch, DioramaRig rig, World.MapLoader map, HeadsetAvatars heads)
        {
            var g = new GameObject("GestureShare").AddComponent<GestureShare>();
            g._game = game; g._ch = ch; g._rig = rig; g._map = map; g._heads = heads;
            for (int i = 0; i < g._out.Length; i++) g._out[i] = new Out();
            return g;
        }

        private void LateUpdate()
        {
            if (_ch == null || _game == null || _rig == null || _map == null || _heads == null) return;
            float now = Time.unscaledTime;
            bool on = _ch.CanShare && _heads.Sharing;
            if (!on)
            {
                // the relay has hidden my headset (LEAVE / GONE) or the link is down: friends draw nothing of mine; all is sent again later
                if (_wasOn) foreach (var o in _out) { o.Last = null; o.Open = false; o.CloseAgainAt = -1f; }
                _wasOn = false;
                (_ch as Net.Stdb.StdbPresenceChannel)?.Flush();
                return;
            }
            if (!_wasOn || _ch.Links != _links || _heads.CurrentMap != _onMap) { foreach (var o in _out) o.Last = null; _links = _ch.Links; _onMap = _heads.CurrentMap; _wasOn = true; }
            byte aud = Comfort.MenusAudience;
            int takes = _game.W12Takes; bool took = takes != _takes; _takes = takes;
            var bar = _game.Quick; int presses = bar != null ? bar.Presses : 0; bool pressed = presses != _presses; _presses = presses;
            for (byte m = 0; m < GestureWire.Menus; m++)
            {
                var o = _out[m];
                // the choice changed while a menu was open: closed for those who saw it, then (below) open again for the new audience
                if (o.Open && o.Audience != aud) { Send(o, Closed(m, o.Audience)); o.Open = false; o.Last = null; o.CloseAgainAt = -1f; }
                o.Audience = aud;
                if (aud == 0) continue;
                // the forearm bar and the wand stay open for minutes: while open they are read at most MaxRate times a second (a press at
                // once); the gestures are short and read every frame (their last picture is the one a take shows)
                if (o.Open && (m == GestureWire.MenuBar ? !pressed && bar != null && bar.Shown : m == GestureWire.MenuWand && _game.W12Wand != null && _game.W12Wand.HeldBy != null)
                    && now - o.BuiltAt < 1f / MaxRate) continue;
                o.BuiltAt = now;
                var s = o.S; s.Clear(); s.Audience = aud; s.Menu = m;
                bool open = m <= GestureWire.MenuRight ? Hand(m == GestureWire.MenuLeft, s) : m == GestureWire.MenuWheel ? Wheel(s) : m == GestureWire.MenuBar ? Bar(s) : Wand(s);
                if (open)
                {
                    var f = GestureWire.Encode(s);
                    if (f == null) continue;
                    if (!o.Open) { Opened++; Debug.Log($"[Menus] shared: {Describe(s)} (to {Who(aud)})"); }
                    bool changed = o.Last == null || !Same(f, o.Last);
                    if (changed && now - o.LastAt >= 1f / MaxRate || now - o.LastAt >= KeepSeconds) Send(o, f);
                    // the bar stays open: a press of one of its slots goes as a take of that slot, at once
                    if (m == GestureWire.MenuBar && pressed && bar.LastPressed >= 0) { s.Lit = bar.LastPressed; s.Flags |= GestureWire.FlagTake; var t = GestureWire.Encode(s); if (t != null) { Send(o, t); TakesSent++; o.Last = null; } }
                    o.Open = true; o.Kind = s.Kind; o.LastOpen = f; o.CloseAgainAt = -1f;
                }
                else if (o.Open)
                {
                    // closed in this frame: with a slot taken (the take counter moved), its last picture goes once more marked TAKE
                    if (took && m <= GestureWire.MenuWheel && o.LastOpen != null)
                    {
                        var t = (byte[])o.LastOpen.Clone(); t[7] |= GestureWire.FlagTake; Send(o, t); TakesSent++; took = false;
                        Debug.Log($"[Menus] shared: menu {m} taken");
                    }
                    else Send(o, Closed(m, aud));
                    o.Open = false; o.LastOpen = null; o.CloseAgainAt = now + KeepSeconds;
                }
                else if (o.CloseAgainAt >= 0f && now >= o.CloseAgainAt) { Send(o, Closed(m, aud)); o.CloseAgainAt = -1f; } // (a frame a full queue dropped)
            }
            (_ch as Net.Stdb.StdbPresenceChannel)?.Flush();
        }

        private void Send(Out o, byte[] f) { _ch.Send(f); o.Last = f; o.LastAt = Time.unscaledTime; Frames++; }
        private static byte[] Closed(byte menu, byte audience) => audience == 0 ? null : GestureWire.Encode(new GestureWire.State { Audience = audience, Menu = menu, Kind = GestureWire.KindClosed });
        private static bool Same(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

        public static string Who(byte audience)
        {
            if ((audience & GestureWire.ToEveryone) != 0) return "everyone";
            var l = new System.Collections.Generic.List<string>();
            if ((audience & GestureWire.ToFriends) != 0) l.Add("friends");
            if ((audience & GestureWire.ToFamily) != 0) l.Add("family");
            if ((audience & GestureWire.ToGuild) != 0) l.Add("guild");
            return l.Count == 0 ? "nobody" : string.Join(" + ", l);
        }
        private static string Describe(GestureWire.State s) =>
            (s.Menu == GestureWire.MenuLeft ? "left hand " : s.Menu == GestureWire.MenuRight ? "right hand " : "")
            + (s.Kind == GestureWire.KindPad ? $"pad, row {s.Row + 1}" : s.Kind == GestureWire.KindFlick ? "flick" : s.Kind == GestureWire.KindWheel ? "wand wheel" : s.Kind == GestureWire.KindBar ? $"forearm bar, row {s.Row + 1}" : "wand")
            + (s.Kind != GestureWire.KindWand ? $", {s.Entries.Count} slots" : "") + ((s.Flags & GestureWire.FlagFront) != 0 ? ", before the eyes" : "");

        // ------------------------------------------------------------------ the pictures, as drawn now

        /// <summary>Where a picture stands: before my eyes (the head's child) in the head's frame (metres), else in the map's frame (cells);
        /// its size in metres per pixel.</summary>
        private void Place(Transform t, GestureWire.State s)
        {
            var head = _rig.Head.transform; Vector3 p; Quaternion q;
            if (t.IsChildOf(head)) { s.Anchor = GestureWire.AnchorHead; s.Flags |= GestureWire.FlagFront; var inv = Quaternion.Inverse(head.rotation); p = inv * (t.position - head.position); q = inv * t.rotation; }
            else { var m = _map.transform; s.Anchor = GestureWire.AnchorMap; p = m.InverseTransformPoint(t.position); q = Quaternion.Inverse(m.rotation) * t.rotation; }
            q.Normalize();
            s.X = p.x; s.Y = p.y; s.Z = p.z; s.Qx = q.x; s.Qy = q.y; s.Qz = q.z; s.Qw = q.w; s.Scale = t.lossyScale.x;
        }

        /// <summary>Slot <paramref name="slot"/> of hotkey row <paramref name="row"/> as an entry (nothing for an empty slot: its absence is
        /// what an empty slot is): a skill with whether it is in the skill list and learned, an item with the amount in the bag.</summary>
        private void Slot(GestureWire.State s, int row, int slot)
        {
            var h = _game.Hotkeys.At(row, slot);
            if (h.Empty) return;
            Item(s, row, slot, h.IsSkill, h.Id);
        }
        private void Item(GestureWire.State s, int row, int slot, bool skill, int id)
        {
            var e = new GestureWire.Entry { Row = row, Slot = slot, Skill = skill, Id = id };
            if (skill) { e.Listed = _game.Prog.Skills.TryGetValue(id, out var k); e.Learned = e.Listed && k.Level > 0; }
            else { e.HasCount = true; e.Count = _game.Inventory.Count(id); }
            s.Entries.Add(e);
        }

        private bool Hand(bool left, GestureWire.State s)
        {
            if (_game.W13Reposition) return false; // (Reposition shows the pads as its own ghosts: not a casting menu)
            var keys = _game.Hotkeys; if (keys == null) return false;
            var pad = _game.W12Pad(left);
            if (pad != null && pad.Shown)
            {
                s.Kind = GestureWire.KindPad; Place(pad.transform, s); s.Row = Mathf.Clamp(pad.Row, 0, Hotkeys.Rows - 1); s.Lit = pad.Marked;
                for (int i = 0; i < Hotkeys.QuickSlots; i++) Slot(s, s.Row, i);
                return true;
            }
            var hud = _game.W12FlickHud(left);
            if (hud != null && hud.Shown)
            {
                s.Kind = GestureWire.KindFlick; Place(hud.transform, s);
                s.Phase = Mathf.Clamp(hud.ShownPhase, 0, 2); s.Dir1 = Mathf.Clamp(hud.ShownDir1, -1, 7); s.Dir2 = Mathf.Clamp(hud.ShownDir2, -1, 7);
                var c = hud.ShownCorner; var d = hud.ShownHand; s.CornerX = c.x; s.CornerY = c.y; s.CursorX = d.x; s.CursorY = d.y;
                if (s.Phase == 0 || s.Dir1 < 0)
                    // the eight rows: each row's icon slot (the first filled flick slot: FlickHud.RowIconSlot): the same slot in a friend's copy
                    for (int r = 0; r < Hotkeys.Rows; r++) { int at = FlickHud.RowIconSlot(keys, r); if (at >= 0) Slot(s, r, at); }
                else
                {
                    int row = FlickRecognizer.RowOfDir(s.Dir1);
                    for (int i = 0; i < 8; i++) Slot(s, row, FlickRecognizer.CellOfDir(i));
                }
                return true;
            }
            return false;
        }

        private bool Wheel(GestureWire.State s)
        {
            var w = _game.W12Wheel; var keys = _game.Hotkeys;
            if (w == null || keys == null || !w.Shown || w.Placing) return false;
            s.Kind = GestureWire.KindWheel; Place(w.transform, s);
            s.Tab = Mathf.Clamp(w.ShownTab, 0, 1);
            int region = w.LitRegion, sub = w.SubShown ? w.SubDir : -1;
            if (region < 0) s.Part = GestureWire.PartNone;
            else if (region < WandWheelGeo.Outer) { s.Part = GestureWire.PartInner; s.Index = region - WandWheelGeo.Inner; }
            else if (region < WandWheelGeo.Cell) { s.Part = GestureWire.PartOuter; s.Index = region - WandWheelGeo.Outer; }
            else if (sub >= 0) { s.Part = GestureWire.PartCell; s.Index = region - WandWheelGeo.Cell; }
            s.Sub = sub; s.Visible = w.DotShown; s.InPlace = w.InPlace; s.Slices = Mathf.Clamp(w.ShownSlices, 2, 8);
            var dot = w.DotPlane; s.CursorX = dot.x; s.CursorY = dot.y;
            // most needed first (GestureWire.Encode leaves out the last ones when they do not fit): the inner ring, the open row, the rows' icons
            for (int i = 0; i < s.Slices; i++) { int id = w.ShownQuick(i); if (id > 0) Item(s, GestureWire.RowWand, i, !w.ShownQuickItem(i), id); }
            if (sub >= 0) for (int c = 0; c < 8; c++) Slot(s, s.Tab * Hotkeys.RowsPerTab + sub, c);
            if (w.MainShown)
                for (int d = 0; d < 4; d++)
                {
                    if (d == sub) continue;
                    int row = s.Tab * Hotkeys.RowsPerTab + d, n = 0;
                    for (int c = 0; c < 8 && n < 4; c++) if (!keys.At(row, c).Empty) { Slot(s, row, c); n++; } // (the outer ring shows a row's first four)
                }
            return true;
        }

        private bool Bar(GestureWire.State s)
        {
            var q = _game.Quick; var keys = _game.Hotkeys;
            if (q == null || keys == null || !keys.Loaded || !q.Shown) return false;
            s.Kind = GestureWire.KindBar; s.Anchor = q.Hand == _rig.LeftHand ? GestureWire.AnchorLeft : GestureWire.AnchorRight;
            s.Scale = q.transform.lossyScale.x; s.Row = keys.CurrentRow; s.Lit = q.HoveredSlot;
            for (int i = 0; i < Hotkeys.QuickSlots; i++) Slot(s, s.Row, i);
            return true;
        }

        private bool Wand(GestureWire.State s)
        {
            var w = _game.W12Wand;
            if (w == null || !w.Shown || w.HeldBy == null) return false;
            s.Kind = GestureWire.KindWand; s.Anchor = w.HeldBy == _rig.LeftHand ? GestureWire.AnchorLeft : GestureWire.AnchorRight; s.Scale = Mathf.Round(w.DrawnScale * 1000f) / 1000f; // (client wave 21, w21wand: to a thousandth: a size that only trembles sends no frames)
            // client wave 21 (w21wand): the wand's lean in the hand's frame, in the turn the wire always carried (identity until now, and still
            // on a controller: the wand lies along the hand; a tracked hand's small wand leans to its fingertip: VR/WandHolster.cs HeldPose)
            var q = Quaternion.Inverse(w.HeldBy.rotation) * w.Wand.rotation; q.Normalize(); s.Qx = q.x; s.Qy = q.y; s.Qz = q.z; s.Qw = q.w;
            return true;
        }
    }
}
