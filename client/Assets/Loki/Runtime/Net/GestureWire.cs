#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Net
{
    /// <summary>
    /// Client wave 18 (w18share), the design's point 33 ("whenever one opens up one of their cast gesture menus ... all of those need to be
    /// visible to everyone. So when I open the flick menu, my friend needs to also be able to see what I'm seeing") and its addition
    /// ("can be toggled for privacy to only be visible to friends, family, or clan members", "And/or"; "I meant my Guild"): the wire format of the casting
    /// menus friends see, on the headset relay's own connection (<see cref="PresenceWire"/>). No Unity dependency: Loki Host's relay
    /// (tools/lokihost/Core/PoseRelay.cs) and its tests compile this same file.
    ///
    /// The version rule (as voice, the data check, Horde and the settings did): nothing of version 1 or 2 changes; the menus ride on a
    /// capability bit and three new frames that only two new ends ever exchange.
    ///   WELCOME 2's capability bit 4 (<see cref="CapGesture"/>): this relay forwards casting menus, and filters them by the sender's choice.
    ///   client → relay (version 2 connections, only after a WELCOME 2 with that bit)
    ///     0x0B GESTURE_READY (no body): "I can draw friends' menus": sent once per link. Only such connections are ever sent GESTURE_OF:
    ///          an older client (it never says it) is never sent one, whatever it is.
    ///     0x0A GESTURE       body (below, 5..<see cref="MaxBody"/> bytes): one of my menus changed (opened, moved, a choice lit, taken,
    ///          closed). Only on change, at most 15 a second per menu, and again every second while it stays open (a friend whose
    ///          relay dropped a frame from a full queue is right again a second later).
    ///   relay → client
    ///     0x89 GESTURE_OF    u32 AID (the sender's, from its HELLO, never from the body), then the body as received: sent only to
    ///          connections that said GESTURE_READY, on the sender's map (its poses tell the relay; a sender with no map yet is not
    ///          forwarded), and only to those the body's audience allows (the relay's own check: <c>PoseRelay.Allowed</c>). A viewer
    ///          the sender's choice leaves out is never sent the frame: privacy is the relay's, not the viewer's client's.
    ///   body: u8 format (1), u8 audience (bits <see cref="ToEveryone"/>, <see cref="ToFriends"/>, <see cref="ToFamily"/>,
    ///         <see cref="ToGuild"/>; 0 never travels: "nobody" sends nothing), u8 menu (<see cref="MenuLeft"/> .. <see cref="MenuWand"/>),
    ///         u8 kind (<see cref="KindClosed"/> .. <see cref="KindWand"/>), u8 flags (<see cref="FlagTake"/>, <see cref="FlagFront"/>);
    ///         then, unless closed: u8 anchor, 3 x f32 place (map cells in the map's frame for <see cref="AnchorMap"/>; metres in the
    ///         head's / hand's frame for the others), 4 x i16 turn (a quaternion x 32767), f32 scale (metres per pixel of the picture;
    ///         the wand: its size), the kind's own fields (<see cref="State"/>), u8 n and n entries: u8 row &lt;&lt; 4 | slot (row
    ///         <see cref="RowWand"/>: the wand's inner ring, slot = its slice), u8 bits (1 skill, 2 in the skill list, 4 learned, 8 an
    ///         amount follows), LEB128 id (1..2^31-1), [LEB128 amount (0..<see cref="MaxCount"/>)].
    ///   The relay reads only the first three bytes (format, audience, menu) and forwards the rest as it came: the receiving client
    ///   checks everything (<see cref="TryDecode"/>) and draws nothing from a body it does not understand.
    /// Every frame fits a voice connection's 274 bytes (GESTURE_OF = 5 + body): no reader's bound changes on either end.
    /// </summary>
    public static class GestureWire
    {
        public const byte CapGesture = 16;
        public const byte Gesture = 0x0A, GestureReady = 0x0B, GestureOf = 0x89;
        public const byte Format = 1;
        /// <summary>The longest body: a GESTURE_OF (type, AID, body) of a voice connection's longest payload.</summary>
        public const int MaxBody = PresenceWire.MaxPayloadVoice - 1 - 4;
        public const int HeaderBytes = 5;
        public const byte ToEveryone = 1, ToFriends = 2, ToFamily = 4, ToGuild = 8, AudienceMask = 15;
        public const byte MenuLeft = 0, MenuRight = 1, MenuWheel = 2, MenuBar = 3, MenuWand = 4, Menus = 5;
        public const byte KindClosed = 0, KindPad = 1, KindFlick = 2, KindWheel = 3, KindBar = 4, KindWand = 5;
        /// <summary>Take: the choice lit in this body was just taken (the gesture is over: a friend sees it lit a moment, then gone).
        /// Front: the picture stands before the sender's eyes (the head's child).</summary>
        public const byte FlagTake = 1, FlagFront = 2;
        public const byte AnchorMap = 0, AnchorHead = 1, AnchorLeft = 2, AnchorRight = 3;
        public const int RowWand = 15, MaxEntries = 64, MaxCount = 9999999;
        /// <summary>The wheel's region as it travels: part (0 none, 1 inner slice, 2 outer segment, 3 cell of a row's wheel) x 16 + index:
        /// independent of how a build numbers its regions (Game/WandWheelGeo.cs).</summary>
        public const int PartNone = 0, PartInner = 1, PartOuter = 2, PartCell = 3;
        /// <summary>Cursor and corner positions travel in half millimetres (i16: ±16 m).</summary>
        public const float CursorUnit = 0.0005f;

        public struct Entry
        {
            public int Row, Slot; public bool Skill, Listed, Learned, HasCount; public int Id, Count;
        }

        /// <summary>One menu's state, as sent and as decoded.</summary>
        public sealed class State
        {
            public byte Audience = ToEveryone, Menu, Kind, Flags, Anchor;
            public float X, Y, Z, Qx, Qy, Qz, Qw = 1f, Scale;
            /// <summary>Pad: its row (0-7) and the marked cell (-1 none). Bar: its row and the slot under the laser (-1 none).</summary>
            public int Row, Lit = -1;
            /// <summary>Flick: the recogniser's phase (0-2), the two strokes' directions (-1 none, 0-7), the corner and the hand (metres
            /// in the strokes' plane).</summary>
            public int Phase, Dir1 = -1, Dir2 = -1; public float CornerX, CornerY;
            /// <summary>Wheel: the shown bar (0-1), the region drawn as under the tip (<see cref="PartNone"/>.. and its index), the open
            /// row (-1 none, 0-3), drawn yet, the row's wheel in the main wheel's place, the inner ring's slices (2-8).</summary>
            public int Tab, Part, Index, Sub = -1, Slices = 4; public bool Visible, InPlace;
            /// <summary>Flick: the hand's place; wheel: the tip's (metres in the picture's plane).</summary>
            public float CursorX, CursorY;
            public readonly List<Entry> Entries = new List<Entry>();
            /// <summary>Encode: entries that did not fit and were left out (the least needed come last).</summary>
            public int Left;

            public void Clear()
            {
                Audience = ToEveryone; Menu = Kind = Flags = Anchor = 0; X = Y = Z = Qx = Qy = Qz = 0f; Qw = 1f; Scale = 0f;
                Row = 0; Lit = -1; Phase = 0; Dir1 = Dir2 = -1; CornerX = CornerY = 0f; Tab = 0; Part = PartNone; Index = 0; Sub = -1; Slices = 4;
                Visible = InPlace = false; CursorX = CursorY = 0f; Entries.Clear(); Left = 0;
            }
        }

        // ------------------------------------------------------------------------------------------------ encoding

        private sealed class Writer
        {
            public readonly byte[] B = new byte[2 + 1 + MaxBody]; public int N = 3; // after the frame's length and the type
            public bool Room(int n) => N + n <= B.Length;
            public void U8(int v) { B[N++] = (byte)v; }
            public void I16(int v) { v = Math.Max(-32767, Math.Min(32767, v)); B[N++] = (byte)v; B[N++] = (byte)(v >> 8); }
            public void F32(float v)
            {
                var x = BitConverter.GetBytes(v); if (!BitConverter.IsLittleEndian) Array.Reverse(x);
                Buffer.BlockCopy(x, 0, B, N, 4); N += 4;
            }
            public void Var(uint v) { while (v >= 0x80) { B[N++] = (byte)(v | 0x80); v >>= 7; } B[N++] = (byte)v; }
            public static int VarLen(uint v) { int n = 1; while (v >= 0x80) { v >>= 7; n++; } return n; }
        }

        private static int Cursor(float metres) => (int)Math.Round(metres / CursorUnit);

        /// <summary>GESTURE_READY: a whole frame.</summary>
        public static byte[] EncodeReady() => new byte[] { 1, 0, GestureReady };

        /// <summary>
        /// A GESTURE frame (u16 length + payload) of <paramref name="s"/>; null when the state cannot be sent (no audience, a bad menu or
        /// kind). Entries are written in their order while they fit; those that do not are counted in <see cref="State.Left"/> (so the
        /// most needed go first). The place and the numbers are written as they are: the receiver checks them.
        /// </summary>
        public static byte[] Encode(State s)
        {
            if ((s.Audience & AudienceMask) == 0 || s.Menu >= Menus || s.Kind > KindWand) return null;
            var w = new Writer();
            w.B[2] = Gesture;
            w.U8(Format); w.U8(s.Audience & AudienceMask); w.U8(s.Menu); w.U8(s.Kind); w.U8(s.Flags);
            s.Left = 0;
            if (s.Kind != KindClosed)
            {
                w.U8(s.Anchor); w.F32(s.X); w.F32(s.Y); w.F32(s.Z);
                w.I16((int)Math.Round(s.Qx * 32767f)); w.I16((int)Math.Round(s.Qy * 32767f)); w.I16((int)Math.Round(s.Qz * 32767f)); w.I16((int)Math.Round(s.Qw * 32767f));
                w.F32(s.Scale);
                switch (s.Kind)
                {
                    case KindPad: case KindBar: w.U8(s.Row); w.U8((sbyte)s.Lit); break;
                    case KindFlick:
                        w.U8(s.Phase); w.U8((sbyte)s.Dir1); w.U8((sbyte)s.Dir2);
                        w.I16(Cursor(s.CornerX)); w.I16(Cursor(s.CornerY)); w.I16(Cursor(s.CursorX)); w.I16(Cursor(s.CursorY)); break;
                    case KindWheel:
                        w.U8(s.Tab); w.U8(s.Part == PartNone ? 0 : s.Part * 16 + s.Index); w.U8((sbyte)s.Sub);
                        w.U8((s.Visible ? 1 : 0) | (s.InPlace ? 2 : 0)); w.U8(s.Slices);
                        w.I16(Cursor(s.CursorX)); w.I16(Cursor(s.CursorY)); break;
                }
                int nAt = w.N; w.U8(0); int n = 0;
                foreach (var e in s.Entries)
                {
                    uint id = (uint)Math.Max(0, e.Id), count = (uint)Math.Max(0, Math.Min(MaxCount, e.Count));
                    int len = 2 + Writer.VarLen(id) + (e.HasCount ? Writer.VarLen(count) : 0);
                    if (n >= MaxEntries || !w.Room(len)) { s.Left++; continue; }
                    w.U8(e.Row << 4 | e.Slot);
                    w.U8((e.Skill ? 1 : 0) | (e.Listed ? 2 : 0) | (e.Learned ? 4 : 0) | (e.HasCount ? 8 : 0));
                    w.Var(id); if (e.HasCount) w.Var(count);
                    n++;
                }
                w.B[nAt] = (byte)n;
            }
            int payload = w.N - 2;
            w.B[0] = (byte)payload; w.B[1] = (byte)(payload >> 8);
            var f = new byte[w.N]; Buffer.BlockCopy(w.B, 0, f, 0, w.N);
            return f;
        }

        /// <summary>The relay's forward of a GESTURE payload (<paramref name="payload"/> from its type byte on): the sender's AID + the body.</summary>
        public static byte[] EncodeGestureOf(uint aid, byte[] payload, int offset, int length)
        {
            int body = length - 1, n = 1 + 4 + body;
            var f = new byte[2 + n];
            f[0] = (byte)n; f[1] = (byte)(n >> 8); f[2] = GestureOf;
            f[3] = (byte)aid; f[4] = (byte)(aid >> 8); f[5] = (byte)(aid >> 16); f[6] = (byte)(aid >> 24);
            Buffer.BlockCopy(payload, offset + 1, f, 7, body);
            return f;
        }

        // ------------------------------------------------------------------------------------------------ decoding

        /// <summary>The relay's look at a GESTURE payload (from its type byte): its length within bounds, format 1 or later, an audience
        /// (bits within <see cref="AudienceMask"/>, not 0) and a menu. The rest is the receiving client's to check.</summary>
        public static bool TryReadHeader(byte[] b, int o, int n, out byte audience, out byte menu)
        {
            audience = 0; menu = 0;
            if (n < 1 + HeaderBytes || n > 1 + MaxBody || b[o] != Gesture || b[o + 1] < Format) return false;
            audience = b[o + 2]; menu = b[o + 3];
            return audience != 0 && (audience & ~AudienceMask) == 0 && menu < Menus;
        }

        /// <summary>A GESTURE_OF payload: the sender's AID and where its body is (nothing copied).</summary>
        public static bool TryReadGestureOf(byte[] b, int o, int n, out uint aid, out int bodyOffset, out int bodyLength)
        {
            aid = 0; bodyOffset = o + 5; bodyLength = n - 5;
            if (n < 5 + HeaderBytes || n > 5 + MaxBody || b[o] != GestureOf) return false;
            aid = (uint)(b[o + 1] | b[o + 2] << 8 | b[o + 3] << 16 | b[o + 4] << 24);
            return aid != 0;
        }

        private struct Reader
        {
            private readonly byte[] _b; private int _o; private readonly int _end;
            public Reader(byte[] b, int o, int n) { _b = b; _o = o; _end = o + n; }
            public int Left => _end - _o;
            public bool Has(int n) => _o + n <= _end;
            public int U8() => _b[_o++];
            public int I8() => (sbyte)_b[_o++];
            public int I16() { int v = (short)(_b[_o] | _b[_o + 1] << 8); _o += 2; return v; }
            public float F32()
            {
                float v;
                if (BitConverter.IsLittleEndian) v = BitConverter.ToSingle(_b, _o);
                else { var x = new byte[4]; Array.Copy(_b, _o, x, 0, 4); Array.Reverse(x); v = BitConverter.ToSingle(x, 0); }
                _o += 4; return v;
            }
            /// <summary>LEB128, at most 5 bytes; false past the end or beyond 2^31-1.</summary>
            public bool Var(out int v)
            {
                v = 0; long x = 0;
                for (int i = 0; i < 5; i++)
                {
                    if (_o >= _end) return false;
                    int c = _b[_o++]; x |= (long)(c & 0x7f) << (7 * i);
                    if ((c & 0x80) == 0) { if (x > int.MaxValue) return false; v = (int)x; return true; }
                }
                return false;
            }
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>
        /// A body (from its format byte: a GESTURE_OF's after the AID), checked whole: false for anything a friend's picture could not be
        /// drawn from (another format, a menu with a kind it never shows, numbers out of their ranges, a place not finite or farther than
        /// <see cref="PresenceWire.MaxCoord"/>, a turn that is no rotation, entries out of their rows and slots, bytes left over).
        /// </summary>
        public static bool TryDecode(byte[] b, int o, int n, State s)
        {
            s.Clear();
            var r = new Reader(b, o, n);
            if (!r.Has(HeaderBytes) || r.U8() != Format) return false;
            s.Audience = (byte)r.U8(); s.Menu = (byte)r.U8(); s.Kind = (byte)r.U8(); s.Flags = (byte)r.U8();
            if (s.Audience == 0 || (s.Audience & ~AudienceMask) != 0 || s.Menu >= Menus || (s.Flags & ~(FlagTake | FlagFront)) != 0) return false;
            if (s.Kind != KindClosed && s.Kind != KindOf(s.Menu) && !(s.Menu <= MenuRight && s.Kind == KindFlick)) return false;
            if (s.Kind == KindClosed) return r.Left == 0;
            if (!r.Has(1 + 12 + 8 + 4)) return false;
            s.Anchor = (byte)r.U8(); s.X = r.F32(); s.Y = r.F32(); s.Z = r.F32();
            s.Qx = r.I16() / 32767f; s.Qy = r.I16() / 32767f; s.Qz = r.I16() / 32767f; s.Qw = r.I16() / 32767f; s.Scale = r.F32();
            if (s.Anchor > AnchorRight || !Finite(s.X) || !Finite(s.Y) || !Finite(s.Z) || !Finite(s.Scale)) return false;
            if (Math.Abs(s.X) > PresenceWire.MaxCoord || Math.Abs(s.Y) > PresenceWire.MaxCoord || Math.Abs(s.Z) > PresenceWire.MaxCoord) return false;
            double q = (double)s.Qx * s.Qx + (double)s.Qy * s.Qy + (double)s.Qz * s.Qz + (double)s.Qw * s.Qw;
            if (q < 0.25 || q > 2.25 || s.Scale < 1e-6f || s.Scale > 100f) return false;
            switch (s.Kind)
            {
                case KindPad: case KindBar:
                    if (!r.Has(2)) return false;
                    s.Row = r.U8(); s.Lit = r.I8();
                    if (s.Row > 7 || s.Lit < -1 || s.Lit > 8) return false;
                    break;
                case KindFlick:
                    if (!r.Has(3 + 8)) return false;
                    s.Phase = r.U8(); s.Dir1 = r.I8(); s.Dir2 = r.I8();
                    s.CornerX = r.I16() * CursorUnit; s.CornerY = r.I16() * CursorUnit; s.CursorX = r.I16() * CursorUnit; s.CursorY = r.I16() * CursorUnit;
                    if (s.Phase > 2 || s.Dir1 < -1 || s.Dir1 > 7 || s.Dir2 < -1 || s.Dir2 > 7) return false;
                    break;
                case KindWheel:
                {
                    if (!r.Has(5 + 4)) return false;
                    s.Tab = r.U8(); int region = r.U8(); s.Sub = r.I8(); int bits = r.U8(); s.Slices = r.U8();
                    s.CursorX = r.I16() * CursorUnit; s.CursorY = r.I16() * CursorUnit;
                    s.Visible = (bits & 1) != 0; s.InPlace = (bits & 2) != 0;
                    s.Part = region >> 4; s.Index = region & 15;
                    if (s.Tab > 1 || s.Sub < -1 || s.Sub > 3 || (bits & ~3) != 0 || s.Slices < 2 || s.Slices > 8) return false;
                    if (region == 0) { s.Part = PartNone; s.Index = 0; }
                    else if (s.Part == PartInner ? s.Index >= s.Slices : s.Part == PartOuter ? s.Index > 3 : s.Part == PartCell ? s.Index > 7 || s.Sub < 0 : true) return false;
                    break;
                }
            }
            if (!r.Has(1)) return false;
            int count = r.U8();
            if (count > MaxEntries) return false;
            for (int i = 0; i < count; i++)
            {
                if (!r.Has(2)) return false;
                int at = r.U8(), bits = r.U8();
                var e = new Entry { Row = at >> 4, Slot = at & 15, Skill = (bits & 1) != 0, Listed = (bits & 2) != 0, Learned = (bits & 4) != 0, HasCount = (bits & 8) != 0 };
                if ((bits & ~15) != 0) return false;
                if (e.Row == RowWand ? s.Kind != KindWheel || e.Slot > 7 : e.Row > 7 || e.Slot > 8) return false;
                if (!r.Var(out e.Id) || e.Id < 1) return false;
                if (e.HasCount && (!r.Var(out e.Count) || e.Count > MaxCount)) return false;
                s.Entries.Add(e);
            }
            return r.Left == 0;
        }

        /// <summary>The kind each menu shows (a hand's menu: the pad, or the flick: both allowed).</summary>
        public static byte KindOf(byte menu) => menu <= MenuRight ? KindPad : menu == MenuWheel ? KindWheel : menu == MenuBar ? KindBar : KindWand;
    }
}
