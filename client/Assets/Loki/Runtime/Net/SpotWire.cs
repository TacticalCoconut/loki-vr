#nullable disable
using System;
using System.Text;

namespace Loki.Net
{
    /// <summary>
    /// Free movement (design note 2026-10-14: "option 1, but with friends seeing other player's exact spots
    /// too"): the wire format of a player's EXACT spot on the map, on the headset relay's own connection (<see cref="PresenceWire"/>).
    /// rAthena keeps every character on a cell; a Loki client draws its own character where the player stopped, up to half a cell
    /// from that cell (Sprites/Unit.Smooth.cs), and tells the players on the relay where that is, so they draw it there too. Players
    /// not on the relay see the cell. No Unity dependency: Loki Host's relay (tools/lokihost/Core/PoseRelay.cs) and its tests compile
    /// this same file.
    ///
    /// The version rule (as voice, the data check, Horde, the settings, the casting menus and the drawings did): nothing of version 1
    /// or 2 changes; the spots ride on a capability bit and three new frames that only two new ends ever exchange.
    ///   WELCOME 2's capability bit 6 (<see cref="CapSpot"/>): this relay forwards exact spots.
    ///   client → relay (version 2 connections, only after a WELCOME 2 with that bit: an older relay closes a link on an unknown message)
    ///     0x0F SPOT_READY (no body): "I draw other players at their spots": sent once per link. Only such connections are ever sent
    ///          SPOT_OF: an older client (it never says it) is never sent one.
    ///     0x0E SPOT       body (below): my drawn spot. 15 a second while it moves, at once when it starts, stops or turns, once a
    ///          second at rest; never while my character is hidden.
    ///   relay → client
    ///     0x8B SPOT_OF    u32 AID (the sender's, from its HELLO, never from the body), then the body as received: only to connections
    ///          that said SPOT_READY, on the SPOT's own map (a desktop player sends no pose), whose own last spot is within the relay's
    ///          range of the sender's (a client learns no more than the server shows it in view).
    ///   body: u8 format (1), u8 m, map[m] (ASCII, 1..15, the pose's map rule), u16 seq (the sender's clock in 10 ms steps, wrapping: the
    ///         receiver spaces the spots by it, not by when they arrived), f32 x, f32 y (map cells in the map's frame:
    ///         the centre of cell (x, y) is (x, y), Sprites/Unit.cs DrawnCell), i16 vx, i16 vy (cells per second × <see cref="VelScale"/>),
    ///         u8 facing (the client's direction 0-7: 0 south, 2 west, 4 north, 6 east), u8 flags (<see cref="FlagMoving"/>).
    /// Every frame is far under every reader's bound (a SPOT_OF is at most 38 bytes).
    /// </summary>
    public static class SpotWire
    {
        public const byte CapSpot = 64;
        public const byte Spot = 0x0E, SpotReady = 0x0F, SpotOf = 0x8B;
        public const byte Format = 1;
        public const byte FlagMoving = 1;
        public const float VelScale = 100f;
        /// <summary>Spots beyond this many cells from the map's origin are refused (RO maps are at most 512 cells a side).</summary>
        public const float MaxCoord = 2048f;
        public const int MinBody = 1 + 1 + 1 + 2 + 4 + 4 + 2 + 2 + 1 + 1;
        public const int MaxBody = MinBody - 1 + PresenceWire.MaxMapBytes;

        /// <summary>One spot, as sent and as decoded.</summary>
        public struct State
        {
            public string Map; public ushort Seq; public float X, Y, Vx, Vy; public byte Facing, Flags;
            public bool Moving => (Flags & FlagMoving) != 0;
        }

        /// <summary>SPOT_READY: a whole frame.</summary>
        public static byte[] EncodeReady() => new byte[] { 1, 0, SpotReady };

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static short Vel(float v)
        {
            double s = Math.Round(v * VelScale);
            return (short)Math.Max(-32767, Math.Min(32767, s));
        }

        /// <summary>A SPOT frame (u16 length + payload); null when it cannot be sent (no map, a bad map name, a place out of range).</summary>
        public static byte[] Encode(in State s)
        {
            string map = s.Map ?? "";
            int m = map.Length;
            if (m < 1 || m > PresenceWire.MaxMapBytes) return null;
            if (!Finite(s.X) || !Finite(s.Y) || Math.Abs(s.X) > MaxCoord || Math.Abs(s.Y) > MaxCoord) return null;
            float vx = Finite(s.Vx) ? s.Vx : 0f, vy = Finite(s.Vy) ? s.Vy : 0f;
            int payload = 1 + MinBody - 1 + m, o = 0;
            var f = new byte[2 + payload];
            f[o++] = (byte)payload; f[o++] = (byte)(payload >> 8);
            f[o++] = Spot; f[o++] = Format; f[o++] = (byte)m;
            for (int i = 0; i < m; i++) { char c = map[i]; if (c > 127 || !PresenceWire.ValidMapChar((byte)c)) return null; f[o++] = (byte)c; }
            f[o++] = (byte)s.Seq; f[o++] = (byte)(s.Seq >> 8);
            o = PutF32(f, o, s.X); o = PutF32(f, o, s.Y);
            short qx = Vel(vx), qy = Vel(vy);
            f[o++] = (byte)qx; f[o++] = (byte)(qx >> 8); f[o++] = (byte)qy; f[o++] = (byte)(qy >> 8);
            f[o++] = (byte)(s.Facing & 7); f[o++] = (byte)(s.Flags & FlagMoving);
            return f;
        }

        private static int PutF32(byte[] f, int o, float v)
        {
            var x = BitConverter.GetBytes(v); if (!BitConverter.IsLittleEndian) Array.Reverse(x);
            Buffer.BlockCopy(x, 0, f, o, 4); return o + 4;
        }

        private static float F32(byte[] b, int o)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, o);
            var x = new byte[4]; Array.Copy(b, o, x, 0, 4); Array.Reverse(x); return BitConverter.ToSingle(x, 0);
        }

        /// <summary>A body (from its format byte), checked whole: format 1 or later (a later format keeps these fields first), a map name, a
        /// place within range, a facing 0-7. false for anything else.</summary>
        public static bool TryDecodeBody(byte[] b, int o, int n, out State s)
        {
            s = default;
            if (n < MinBody || n > MaxBody + 64 || b[o] < Format) return false;
            int m = b[o + 1];
            if (m < 1 || m > PresenceWire.MaxMapBytes || n < MinBody - 1 + m) return false;
            for (int i = 0; i < m; i++) if (!PresenceWire.ValidMapChar(b[o + 2 + i])) return false;
            if (b[o] == Format && n != MinBody - 1 + m) return false; // format 1 is exactly this long
            int p = o + 2 + m;
            s.Map = Encoding.ASCII.GetString(b, o + 2, m);
            s.Seq = (ushort)(b[p] | b[p + 1] << 8); p += 2;
            s.X = F32(b, p); s.Y = F32(b, p + 4); p += 8;
            s.Vx = (short)(b[p] | b[p + 1] << 8) / VelScale; s.Vy = (short)(b[p + 2] | b[p + 3] << 8) / VelScale; p += 4;
            s.Facing = b[p]; s.Flags = b[p + 1];
            return Finite(s.X) && Finite(s.Y) && Math.Abs(s.X) <= MaxCoord && Math.Abs(s.Y) <= MaxCoord && s.Facing <= 7;
        }

        /// <summary>The relay's look at a SPOT payload (from its type byte): the whole body checked, its map and place handed back.</summary>
        public static bool TryReadSpot(byte[] b, int o, int n, out State s)
        {
            s = default;
            return n >= 1 && b[o] == Spot && TryDecodeBody(b, o + 1, n - 1, out s);
        }

        /// <summary>The relay's forward of a SPOT payload (<paramref name="payload"/> from its type byte on) to one viewer: the sender's AID + the body.</summary>
        public static byte[] EncodeSpotOf(uint aid, byte[] payload, int offset, int length)
        {
            int body = length - 1, n = 1 + 4 + body;
            var f = new byte[2 + n];
            f[0] = (byte)n; f[1] = (byte)(n >> 8); f[2] = SpotOf;
            f[3] = (byte)aid; f[4] = (byte)(aid >> 8); f[5] = (byte)(aid >> 16); f[6] = (byte)(aid >> 24);
            Buffer.BlockCopy(payload, offset + 1, f, 7, body);
            return f;
        }

        /// <summary>A SPOT_OF payload: the sender's AID and its body, checked.</summary>
        public static bool TryReadSpotOf(byte[] b, int o, int n, out uint aid, out State s)
        {
            aid = 0; s = default;
            if (n < 5 + MinBody || b[o] != SpotOf) return false;
            aid = (uint)(b[o + 1] | b[o + 2] << 8 | b[o + 3] << 16 | b[o + 4] << 24);
            return aid != 0 && TryDecodeBody(b, o + 5, n - 5, out s);
        }

        /// <summary>Chebyshev distance between two spots, in cells.</summary>
        public static float Distance(float x0, float y0, float x1, float y1) => Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1));
    }
}
