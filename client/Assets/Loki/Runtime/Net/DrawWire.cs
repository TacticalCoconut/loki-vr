#nullable disable
using System;
using System.Collections.Generic;

namespace Loki.Net
{
    /// <summary>
    /// Client wave 20 (w20pen), the design's point 57 (the AIR PEN: "spawn a simple pen object that can be used to draw on the air ... It writes in
    /// the air in 3D space"; "They should stay on the map until you erase them, log out, or until you change maps"; "You should be able to
    /// toggle the privacy of your drawings"; design notes of 2026-10-10: "Drawings seen by" (who sees MY drawings) and "Drawings I see" (whose
    /// drawings I see), both Friends only by default): the wire format of the drawings other players see, on the headset relay's own connection
    /// (<see cref="PresenceWire"/>). No Unity dependency: Loki Host's relay (tools/lokihost/Core/PoseRelay.cs) and its tests compile this file.
    ///
    /// The version rule (as voice, the data check, Horde, the settings and the casting menus did): nothing older changes; the drawings ride on
    /// a capability bit and three new frames that only two new ends ever exchange. Taken ids (none reused): client → relay 0x01-0x0B
    /// (PresenceWire, GestureWire) and 0x10-0x1F (Horde); relay → client 0x81-0x89 and 0x90-0x9F; WELCOME bits 1, 2, 4 (Horde), 8, 16.
    ///   WELCOME 2's capability bit 32 (<see cref="CapDraw"/>): this relay forwards drawings, and filters them by the sender's choice.
    ///   client → relay (version 2 connections, only after a WELCOME 2 with that bit)
    ///     0x0D DRAW_READY (no body): "I can draw other players' drawings": sent once per link. Only such connections are ever sent DRAW_OF:
    ///          an older client (it never says it) is never sent one.
    ///     0x0C DRAW       body (below, <see cref="HeaderBytes"/>..<see cref="MaxBody"/> bytes): a part of one of my strokes, strokes of mine
    ///          erased, all of mine erased, or the list of my strokes (what a viewer keeps of mine).
    ///   relay → client
    ///     0x8A DRAW_OF    u32 AID (the sender's, from its HELLO, never from the body), u8 relation (the relay's view of the sender to THIS viewer:
    ///          <see cref="GestureWire.ToFriends"/> | <see cref="GestureWire.ToFamily"/> | <see cref="GestureWire.ToGuild"/>; 0: none, or not
    ///          known: fail closed), then the body as received. Sent only to connections that said DRAW_READY, on the sender's map (its poses
    ///          tell the relay; a sender with no map yet is not forwarded), and only to those the body's audience allows (the relay's own check,
    ///          the casting menus' rule: <c>PoseRelay.Allowed</c>). The viewer's client decides with the relation byte whether it draws them
    ///          ("Drawings I see": the client's filter, the relay's is "Drawings seen by").
    ///   body: u8 format (1), u8 audience (bits as <see cref="GestureWire"/>: 1 everyone, 2 friends, 4 family, 8 guild; 0 never travels),
    ///         u8 op, then by op:
    ///     <see cref="OpStroke"/>  u16 stroke id (1..65535), u16 first (the index of this part's first point in its stroke), u8 flags
    ///                       (<see cref="FlagLast"/>: the stroke ends with this part), u8 colour (0: the sender's own tint; others reserved), f32 width
    ///                       (the line's thickness in map units: cells), 3 x f32 origin (map cells, in the map's frame: World/RoSpace.cs, as the
    ///                       headsets' poses), f32 unit (cells per step), u8 n (1..<see cref="MaxPartPoints"/>), n x (3 x i16): point = origin +
    ///                       (x, y, z) x unit.
    ///     <see cref="OpErase"/>   u8 n (1..<see cref="MaxStrokes"/>), n x u16 stroke id: these strokes of mine are gone.
    ///     <see cref="OpClear"/>   nothing: every stroke of mine is gone.
    ///     <see cref="OpList"/>    u8 n (0..<see cref="MaxStrokes"/>), n x u16 stroke id: the strokes I have now (a viewer drops any other of mine).
    ///   The relay reads only the first three bytes (format, audience, op) and forwards the rest as it came: the receiving client checks
    ///   everything (<see cref="TryDecode"/>) and draws nothing from a body it does not understand.
    /// Every frame fits a voice connection's 274 bytes (DRAW_OF = 6 + body): no reader's bound changes on either end.
    /// </summary>
    public static class DrawWire
    {
        public const byte CapDraw = 32;
        public const byte Draw = 0x0C, DrawReady = 0x0D, DrawOf = 0x8A;
        public const byte Format = 1;
        /// <summary>The longest body: a DRAW_OF (type, AID, relation, body) of a voice connection's longest payload.</summary>
        public const int MaxBody = PresenceWire.MaxPayloadVoice - 1 - 4 - 1;
        public const int HeaderBytes = 3;
        public const byte OpStroke = 1, OpErase = 2, OpClear = 3, OpList = 4;
        public const byte FlagLast = 1;
        /// <summary>A stroke part's own header after the body's three bytes: id, first, flags, colour, width, origin, unit, n.</summary>
        public const int PartHeader = 2 + 2 + 1 + 1 + 4 + 12 + 4 + 1;
        public const int PointBytes = 6;
        /// <summary>The points one part carries at most (as many as fit a voice frame).</summary>
        public const int MaxPartPoints = (MaxBody - HeaderBytes - PartHeader) / PointBytes;
        /// <summary>A player's strokes at most, a stroke's points at most, all the player's points at most (Loki's numbers: about 4 m of line
        /// for one stroke and 16 m in all at 4 mm between points; a full refresh of the most is 103 frames).</summary>
        public const int MaxStrokes = 100, MaxStrokePoints = 1000, MaxPoints = 4000;
        /// <summary>The thinnest and thickest line, the smallest and largest step (cells), the largest place (as a pose's: PresenceWire.MaxCoord).</summary>
        public const float MinWidth = 1e-5f, MaxWidth = 50f, MinUnit = 1e-7f, MaxUnit = 10f;

        /// <summary>One body, as sent and as decoded.</summary>
        public sealed class Msg
        {
            public byte Audience = GestureWire.ToEveryone, Op;
            public int Id, First, Flags, Colour; public float Width;
            /// <summary>OpStroke: the points (x, y, z each) in the map's frame, <see cref="Count"/> of them.</summary>
            public readonly float[] Points = new float[3 * MaxPartPoints];
            public int Count;
            /// <summary>OpErase / OpList: the stroke ids.</summary>
            public readonly List<int> Ids = new List<int>();
            public bool Last => (Flags & FlagLast) != 0;
            public void Clear() { Audience = GestureWire.ToEveryone; Op = 0; Id = First = Flags = Colour = 0; Width = 0f; Count = 0; Ids.Clear(); }
        }

        // ------------------------------------------------------------------------------------------------ encoding

        private sealed class Writer
        {
            public readonly byte[] B = new byte[2 + 1 + MaxBody]; public int N = 3; // after the frame's length and the type
            public void U8(int v) { B[N++] = (byte)v; }
            public void U16(int v) { B[N++] = (byte)v; B[N++] = (byte)(v >> 8); }
            public void I16(int v) { v = Math.Max(-32767, Math.Min(32767, v)); B[N++] = (byte)v; B[N++] = (byte)(v >> 8); }
            public void F32(float v)
            {
                var x = BitConverter.GetBytes(v); if (!BitConverter.IsLittleEndian) Array.Reverse(x);
                Buffer.BlockCopy(x, 0, B, N, 4); N += 4;
            }
            public byte[] Frame()
            {
                int payload = N - 2;
                B[0] = (byte)payload; B[1] = (byte)(payload >> 8);
                var f = new byte[N]; Buffer.BlockCopy(B, 0, f, 0, N);
                return f;
            }
        }

        private static Writer Head(byte audience, byte op)
        {
            var w = new Writer(); w.B[2] = Draw;
            w.U8(Format); w.U8(audience & GestureWire.AudienceMask); w.U8(op);
            return w;
        }
        private static bool AudienceOk(byte a) => (a & GestureWire.AudienceMask) != 0 && (a & ~GestureWire.AudienceMask) == 0;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>DRAW_READY: a whole frame.</summary>
        public static byte[] EncodeReady() => new byte[] { 1, 0, DrawReady };

        /// <summary>
        /// A DRAW frame (u16 length + payload) of one part of a stroke: points <paramref name="start"/> .. start + <paramref name="n"/> - 1 of
        /// <paramref name="xyz"/> (x, y, z each, map cells), which are the stroke's points <paramref name="first"/> on. The part's origin is its
        /// first point, its step the largest offset / 32767 (so every point is kept to a 65,534th of the part's size). Null when it cannot be
        /// sent (no audience, a bad id, too many points, a number out of range).
        /// </summary>
        public static byte[] EncodeStroke(byte audience, int id, int first, bool last, int colour, float width, float[] xyz, int start, int n)
        {
            if (!AudienceOk(audience) || id < 1 || id > 65535 || first < 0 || n < 1 || n > MaxPartPoints || first + n > MaxStrokePoints) return null;
            if (!Finite(width) || width < MinWidth || width > MaxWidth || colour < 0 || colour > 255) return null;
            float ox = xyz[3 * start], oy = xyz[3 * start + 1], oz = xyz[3 * start + 2], far = 0f;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < 3; k++)
                {
                    float v = xyz[3 * (start + i) + k];
                    if (!Finite(v) || Math.Abs(v) > PresenceWire.MaxCoord) return null;
                    far = Math.Max(far, Math.Abs(v - (k == 0 ? ox : k == 1 ? oy : oz)));
                }
            float unit = Math.Max(MinUnit, far / 32767f);
            if (unit > MaxUnit) return null;
            var w = Head(audience, OpStroke);
            w.U16(id); w.U16(first); w.U8(last ? FlagLast : 0); w.U8(colour); w.F32(width);
            w.F32(ox); w.F32(oy); w.F32(oz); w.F32(unit); w.U8(n);
            for (int i = 0; i < n; i++)
            {
                int o = 3 * (start + i);
                w.I16((int)Math.Round((xyz[o] - ox) / unit)); w.I16((int)Math.Round((xyz[o + 1] - oy) / unit)); w.I16((int)Math.Round((xyz[o + 2] - oz) / unit));
            }
            return w.Frame();
        }

        /// <summary>ERASE (<paramref name="ids"/> 1..<see cref="MaxStrokes"/> of them) or LIST (0..<see cref="MaxStrokes"/>); null when it cannot be sent.</summary>
        public static byte[] EncodeIds(byte audience, byte op, IList<int> ids)
        {
            if (!AudienceOk(audience) || (op != OpErase && op != OpList) || ids == null || ids.Count > MaxStrokes || (op == OpErase && ids.Count == 0)) return null;
            var w = Head(audience, op);
            w.U8(ids.Count);
            foreach (var id in ids) { if (id < 1 || id > 65535) return null; w.U16(id); }
            return w.Frame();
        }

        /// <summary>CLEAR: every stroke of mine is gone.</summary>
        public static byte[] EncodeClear(byte audience) => AudienceOk(audience) ? Head(audience, OpClear).Frame() : null;

        /// <summary>The relay's forward of a DRAW payload (<paramref name="payload"/> from its type byte on) to one viewer: the sender's AID, the
        /// relation the relay knows of the sender to that viewer, the body.</summary>
        public static byte[] EncodeDrawOf(uint aid, byte relation, byte[] payload, int offset, int length)
        {
            int body = length - 1, n = 1 + 4 + 1 + body;
            var f = new byte[2 + n];
            f[0] = (byte)n; f[1] = (byte)(n >> 8); f[2] = DrawOf;
            f[3] = (byte)aid; f[4] = (byte)(aid >> 8); f[5] = (byte)(aid >> 16); f[6] = (byte)(aid >> 24);
            f[7] = (byte)(relation & (GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild));
            Buffer.BlockCopy(payload, offset + 1, f, 8, body);
            return f;
        }

        // ------------------------------------------------------------------------------------------------ decoding

        /// <summary>The relay's look at a DRAW payload (from its type byte): its length within bounds, format 1 or later, an audience (bits within
        /// the mask, not 0) and a known op. The rest is the receiving client's to check.</summary>
        public static bool TryReadHeader(byte[] b, int o, int n, out byte audience, out byte op)
        {
            audience = 0; op = 0;
            if (n < 1 + HeaderBytes || n > 1 + MaxBody || b[o] != Draw || b[o + 1] < Format) return false;
            audience = b[o + 2]; op = b[o + 3];
            return AudienceOk(audience) && op >= OpStroke && op <= OpList;
        }

        /// <summary>A DRAW_OF payload: the sender's AID, the relation byte and where its body is (nothing copied).</summary>
        public static bool TryReadDrawOf(byte[] b, int o, int n, out uint aid, out byte relation, out int bodyOffset, out int bodyLength)
        {
            aid = 0; relation = 0; bodyOffset = o + 6; bodyLength = n - 6;
            if (n < 6 + HeaderBytes || n > 6 + MaxBody || b[o] != DrawOf) return false;
            aid = (uint)(b[o + 1] | b[o + 2] << 8 | b[o + 3] << 16 | b[o + 4] << 24);
            relation = b[o + 5];
            return aid != 0 && (relation & ~(GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild)) == 0;
        }

        private static float F32(byte[] b, int o)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, o);
            var x = new byte[4]; Array.Copy(b, o, x, 0, 4); Array.Reverse(x); return BitConverter.ToSingle(x, 0);
        }

        /// <summary>
        /// A body (from its format byte: a DRAW_OF's after the relation), checked whole: false for anything a drawing could not be made from
        /// (another format, an unknown op, a stroke id 0, points beyond a stroke's most, a width / step / place out of range, ids out of range,
        /// bytes missing or left over).
        /// </summary>
        public static bool TryDecode(byte[] b, int o, int n, Msg m)
        {
            m.Clear();
            int end = o + n, p = o;
            if (n < HeaderBytes || b[p] != Format) return false;
            m.Audience = b[p + 1]; m.Op = b[p + 2]; p += 3;
            if (!AudienceOk(m.Audience)) return false;
            switch (m.Op)
            {
                case OpStroke:
                {
                    if (end - p < PartHeader) return false;
                    m.Id = b[p] | b[p + 1] << 8; m.First = b[p + 2] | b[p + 3] << 8; m.Flags = b[p + 4]; m.Colour = b[p + 5]; p += 6;
                    m.Width = F32(b, p); float ox = F32(b, p + 4), oy = F32(b, p + 8), oz = F32(b, p + 12), unit = F32(b, p + 16); p += 20;
                    int count = b[p++];
                    if (m.Id < 1 || (m.Flags & ~FlagLast) != 0 || count < 1 || count > MaxPartPoints || m.First + count > MaxStrokePoints) return false;
                    if (!Finite(m.Width) || m.Width < MinWidth || m.Width > MaxWidth || !Finite(unit) || unit < MinUnit || unit > MaxUnit) return false;
                    if (!Finite(ox) || !Finite(oy) || !Finite(oz) || Math.Abs(ox) > PresenceWire.MaxCoord || Math.Abs(oy) > PresenceWire.MaxCoord || Math.Abs(oz) > PresenceWire.MaxCoord) return false;
                    if (end - p != count * PointBytes) return false;
                    for (int i = 0; i < count; i++, p += PointBytes)
                    {
                        m.Points[3 * i] = ox + (short)(b[p] | b[p + 1] << 8) * unit;
                        m.Points[3 * i + 1] = oy + (short)(b[p + 2] | b[p + 3] << 8) * unit;
                        m.Points[3 * i + 2] = oz + (short)(b[p + 4] | b[p + 5] << 8) * unit;
                    }
                    m.Count = count;
                    return true;
                }
                case OpErase: case OpList:
                {
                    if (end - p < 1) return false;
                    int count = b[p++];
                    if (count > MaxStrokes || (m.Op == OpErase && count == 0) || end - p != 2 * count) return false;
                    for (int i = 0; i < count; i++, p += 2) { int id = b[p] | b[p + 1] << 8; if (id < 1) return false; m.Ids.Add(id); }
                    return true;
                }
                case OpClear: return end == p;
                default: return false;
            }
        }
    }
}
