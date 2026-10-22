#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Loki.Net
{
    /// <summary>
    /// The headset pose relay's wire format (client wave 3, w3presence). A separate channel beside the RO protocol: nothing here
    /// is an RO packet, and no RO server reads it. This file has no Unity dependency on purpose: Loki Host's relay
    /// (tools/lokihost/Core/PoseRelay.cs) and its tests compile this same file, so both ends always agree.
    ///
    /// TCP, one frame = u16 little-endian payload length + payload; payload[0] is the message type. A payload longer than
    /// <see cref="MaxPayload"/> (or empty) is a protocol error: the reader closes the connection.
    ///   client → relay
    ///     0x01 HELLO   magic "LKPR", u8 version, u32 AID, u8 n, name[n] (UTF-8, n ≤ 48)         first message, once
    ///     0x02 POSE    body (below)                                                              10-20 per second
    ///     0x03 LEAVE   (no body): not on a map any more / sharing switched off
    ///     0x04 PING    (no body): keep-alive while not sending poses
    ///   relay → client
    ///     0x81 WELCOME magic "LKPR", u8 version                                                 answer to HELLO
    ///     0x82 POSE_OF u32 AID, u8 n, name[n], body                                              another player's pose
    ///     0x83 GONE    u32 AID                                                                   that player left this map / the relay
    ///   body: u8 m, map[m] (ASCII, 1..15: rAthena MAP_NAME_LENGTH 12 / _EXT 16 with the NUL, common/mmo.hpp:163-164),
    ///         u16 seq, f32 metres per cell (the sender's own table scale), u8 flags (1 left hand tracked, 2 right),
    ///         21 × f32: head, left hand, right hand, each position x y z then rotation x y z w,
    ///         IN THE MAP'S FRAME: Loki units (1 = one GAT cell, y up, World/RoSpace.cs), relative to the map root
    ///         (MapLoader's transform), so every player's own table scale, pan and turn cancel out.
    ///
    /// Version 2 (client wave 6, w6voice): voice between friends on the same connection. A client that can do voice says HELLO
    /// with version 2; a relay that knows it answers WELCOME version 2 + one capability byte, and from then on that connection may
    /// carry the frames below (payloads up to <see cref="MaxPayloadVoice"/>). Everything of version 1 stays byte for byte as it was:
    ///   - a version-1 client (before wave 6) says HELLO 1 and gets WELCOME 1, and the relay never sends it a voice frame (its
    ///     reader closes on any payload over 200 bytes);
    ///   - a version-1 relay closes a HELLO 2 ("not a valid HELLO"): the client then connects again with HELLO 1 and has no voice.
    ///   client → relay (version 2 connections only)
    ///     0x05 VOICE      u8 m, map[m] (as in a pose: where the speaker is), u16 seq, u8 codec, u8 flags, data[1..200]
    ///                     one encoded frame (codec 1 = IMA ADPCM, 20 ms of 16 kHz mono: Voice/ImaAdpcm.cs); nothing is sent
    ///                     while the speaker's noise gate is closed; flags 1 = first frame of a talk spurt, 2 = last
    ///     0x06 VOICE_STOP (no body): voice switched off / leaving the world
    ///   relay → client (version 2 connections only, voice capability on)
    ///     0x84 VOICE_OF   u32 AID, u8 n, name[n] (from the speaker's HELLO), then the VOICE body as received
    ///                     to every other version-2 client, whatever its map (the listener decides what it plays)
    ///     0x85 VOICE_END  u32 AID: that speaker stopped sharing its voice or left the relay
    ///   WELCOME 2: magic "LKPR", u8 2, u8 capabilities (bit 0: this relay forwards voice)
    ///
    /// Client wave 7 (w7host): the game-data check, still version 2. HELLO 1, HELLO 2, WELCOME 1 and every frame above stay
    /// byte for byte as they were; the check rides on a capability bit and two new frames that only two new ends ever exchange:
    ///   WELCOME 2's capability bit 1 (<see cref="CapData"/>): this relay knows its host's data stamp and compares players' with it
    ///   client → relay (version 2 connections, only after a WELCOME 2 with that bit)
    ///     0x07 DATA       u8 n, stamp[n] (printable ASCII, 1..32), u8 m, label[m] (UTF-8, 0..48, for people to read)
    ///                     the client's data stamp (IO/MapDataStamp.cs: a hash of its install's map cells) and a label
    ///                     (its install's patch number); once per connection, as soon as the client knows it
    ///   relay → client (the answer to DATA)
    ///     0x86 DATA_HOST  u8 verdict (0 the same, 1 differs), u8 n, stamp[n], u8 m, label[m]: the host's own
    ///   Why not inside HELLO: a relay of wave 6 refuses a HELLO with one byte more (TryDecodeHello: r.Left != len) and any
    ///   version but 1 and 2, so a longer or a version-3 HELLO would cost every new client a refused connection and a second try
    ///   on every older host. As built:
    ///   - an older client (wave 3-6) ignores unknown capability bits (it tests bit 0 only), never sends DATA and is never
    ///     sent DATA_HOST: nothing changes for it;
    ///   - an older relay never sets the bit, so a new client never sends DATA (that relay would close on the unknown
    ///     message): no check, nothing else changes;
    ///   - a relay without a host stamp (a standalone relay, a Loki Host with no Ragnarok Online folder set) does not set it.
    ///
    /// Client wave 18 (w18acct), the design's point 1: the player's settings kept per ACCOUNT by the host, so a new PC gets them. Still
    /// version 2, on a capability bit and four new frames that only two new ends ever exchange (wave 7's way, above):
    ///   WELCOME 2's capability bit 3 (<see cref="CapSettings"/>): this relay keeps each account's settings (Loki Host: a table of
    ///   its database, tools/lokihost/Core/AccountSettingsDb.cs)
    ///   client → relay (version 2 connections, only after a WELCOME 2 with that bit)
    ///     0x08 SETTINGS_AUTH  u8 n, proof[n] (printable ASCII, 1..32): proof that this connection's AID is logged in now. On
    ///                         rAthena: the token its login server gave this login (AC_ACCEPT_LOGIN's token, rA:login/loginclif.cpp
    ///                         126-128), checked as rAthena's own web server checks it (rA:web/auth.cpp isAuthorized: the login row
    ///                         of that AID with that web_auth_token and web_auth_token_enabled 1). Answered by SETTINGS_OF; sent
    ///                         again it asks again (the answer is what the host holds then).
    ///     0x09 SETTINGS_PUT   i64 savedAt (ms since 1970 UTC: when the player last changed them), data[1..<see cref="MaxSettingsBytes"/>]
    ///                         (UTF-8 JSON: the client's settings without its per-PC part, World/UserSettings.cs AccountJson)
    ///   relay → client
    ///     0x87 SETTINGS_OF    u8 state (<see cref="SettingsFound"/>, <see cref="SettingsNone"/> nothing stored yet,
    ///                         <see cref="SettingsRefused"/> the proof does not match, <see cref="SettingsFailed"/> the host's store
    ///                         failed), i64 savedAt, data[...] (state Found only)
    ///     0x88 SETTINGS_ACK   u8 state (<see cref="SettingsStored"/>, <see cref="SettingsOlder"/> not stored: a newer save is stored,
    ///                         Refused, Failed), i64 savedAt (the save the host holds now; 0 when none)
    ///   Both directions may carry up to <see cref="MaxPayloadSettings"/> bytes on such a connection: the relay accepts a long
    ///   frame only from a connection whose SETTINGS_AUTH it accepted; the client reads long frames once it saw the bit.
    /// </summary>
    public static class PresenceWire
    {
        public const byte Version = 1;
        /// <summary>The version a client says HELLO with when it can do voice (client wave 6).</summary>
        public const byte VersionVoice = 2;
        public const int MaxPayload = 200;
        /// <summary>The longest codec frame a VOICE message may carry (IMA ADPCM uses 163: 3 bytes of state + 320 samples × 4 bits).</summary>
        public const int MaxVoiceData = 200;
        /// <summary>The longest payload of a version-2 connection: a VOICE_OF with the longest name and map (274 bytes).</summary>
        public const int MaxPayloadVoice = 1 + 4 + 1 + MaxNameBytes + 1 + MaxMapBytes + 2 + 1 + 1 + MaxVoiceData;
        public const byte Voice = 0x05, VoiceStop = 0x06;
        public const byte VoiceOf = 0x84, VoiceEnd = 0x85;
        /// <summary>WELCOME 2's capability bits.</summary>
        public const byte CapVoice = 1;
        /// <summary>WELCOME 2's capability bit 1 (client wave 7, w7host): this relay compares the players' game data with its
        /// host's; a client that sees it sends DATA once. Older clients ignore the bit.</summary>
        public const byte CapData = 2;
        /// <summary>The game-data check's two frames (version 2 connections; see the summary above).</summary>
        public const byte Data = 0x07, DataHost = 0x86;
        public const byte DataSame = 0, DataDiffers = 1;
        public const int MaxStampBytes = 32, MaxLabelBytes = 48;
        // ---- client wave 18 (w18acct): settings per account (see the summary)
        /// <summary>WELCOME 2's capability bit 3: this relay keeps each account's settings.</summary>
        public const byte CapSettings = 8;
        public const byte SettingsAuth = 0x08, SettingsPut = 0x09, SettingsOf = 0x87, SettingsAck = 0x88;
        public const byte SettingsFound = 0, SettingsNone = 1, SettingsRefused = 2, SettingsFailed = 3;
        public const byte SettingsStored = 0, SettingsOlder = 1;
        /// <summary>The longest settings JSON either end sends (a frame's payload is at most 65,535 bytes: u16 length).</summary>
        public const int MaxSettingsBytes = 60000;
        public const int MaxPayloadSettings = 65535;
        public const int MaxProofBytes = 32;
        /// <summary>VOICE flags: the first / the last frame of a talk spurt (the speaker's gate opened / is closing).</summary>
        public const byte VoiceFlagFirst = 1, VoiceFlagLast = 2;
        /// <summary>RO names are at most 23 bytes in the server's code page (NAME_LENGTH 24, common/mmo.hpp:154); UTF-8 of a
        /// cp1252 name is at most twice that.</summary>
        public const int MaxNameBytes = 48;
        public const int MaxMapBytes = 15;
        public const int PoseFloats = 21;
        public const byte Hello = 0x01, Pose = 0x02, Leave = 0x03, Ping = 0x04;
        public const byte Welcome = 0x81, PoseOf = 0x82, Gone = 0x83;
        public const byte FlagLeft = 1, FlagRight = 2;
        public static readonly byte[] Magic = { (byte)'L', (byte)'K', (byte)'P', (byte)'R' };
        /// <summary>Positions beyond this many cells from the map origin are refused (the biggest RO maps are 400 × 400 cells).</summary>
        public const float MaxCoord = 100000f;
        public const float MinMetresPerCell = 0.0001f, MaxMetresPerCell = 100f;

        public sealed class PoseBody
        {
            public string Map = "";
            public ushort Seq;
            public float MetresPerCell;
            public byte Flags;
            /// <summary>head pos xyz, head rot xyzw, left pos, left rot, right pos, right rot.</summary>
            public readonly float[] V = new float[PoseFloats];
        }

        // ------------------------------------------------------------------------------------------------ encoding

        private sealed class Writer
        {
            private readonly List<byte> _b = new List<byte>(128);
            public Writer U8(byte v) { _b.Add(v); return this; }
            public Writer U16(ushort v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); return this; }
            public Writer U32(uint v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 24)); return this; }
            public Writer I64(long v) { U32((uint)v); return U32((uint)((ulong)v >> 32)); } // client wave 18 (w18acct)
            public Writer F32(float v)
            {
                var x = BitConverter.GetBytes(v);
                if (!BitConverter.IsLittleEndian) Array.Reverse(x);
                _b.AddRange(x); return this;
            }
            public Writer Bytes(byte[] v) { _b.AddRange(v); return this; }
            public Writer Bytes(byte[] v, int off, int n) { for (int i = 0; i < n; i++) _b.Add(v[off + i]); return this; }
            /// <summary>The payload as a frame: u16 length + payload.</summary>
            public byte[] Frame(int max = MaxPayload)
            {
                if (_b.Count > max) throw new InvalidOperationException("presence payload too long: " + _b.Count);
                var f = new byte[_b.Count + 2];
                f[0] = (byte)_b.Count; f[1] = (byte)(_b.Count >> 8);
                _b.CopyTo(f, 2);
                return f;
            }
        }

        /// <summary>A name cut to <see cref="MaxNameBytes"/> UTF-8 bytes without splitting a character; control characters dropped.</summary>
        public static byte[] NameBytes(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "") if (!char.IsControl(c)) sb.Append(c);
            string s = sb.ToString();
            while (Encoding.UTF8.GetByteCount(s) > MaxNameBytes) s = s.Substring(0, s.Length - 1);
            return Encoding.UTF8.GetBytes(s);
        }

        public static byte[] EncodeHello(uint aid, string name) => EncodeHello(aid, name, Version);

        /// <summary>HELLO with a version: <see cref="Version"/> (poses) or <see cref="VersionVoice"/> (poses + voice). Same layout.</summary>
        public static byte[] EncodeHello(uint aid, string name, byte version)
        {
            var n = NameBytes(name);
            return new Writer().U8(Hello).Bytes(Magic).U8(version).U32(aid).U8((byte)n.Length).Bytes(n).Frame();
        }

        public static byte[] EncodeWelcome() => new Writer().U8(Welcome).Bytes(Magic).U8(Version).Frame();
        /// <summary>WELCOME for a HELLO of that version: version 1 exactly as before wave 6; version 2 adds the capability byte.</summary>
        public static byte[] EncodeWelcome(byte version, byte caps) =>
            version < VersionVoice ? EncodeWelcome() : new Writer().U8(Welcome).Bytes(Magic).U8(VersionVoice).U8(caps).Frame();
        public static byte[] EncodeVoiceStop() => new Writer().U8(VoiceStop).Frame();
        public static byte[] EncodeVoiceEnd(uint aid) => new Writer().U8(VoiceEnd).U32(aid).Frame();

        /// <summary>
        /// A VOICE frame (u16 length + payload) written into <paramref name="dst"/> without allocating (50 a second while a player
        /// talks): returns the bytes written, or 0 when the map or the data cannot be sent (no map, a bad map name, no data, too long).
        /// </summary>
        public static int WriteVoice(byte[] dst, int dstOffset, string map, ushort seq, byte codec, byte flags, byte[] data, int offset, int length)
        {
            int m = map != null ? map.Length : 0;
            if (m < 1 || m > MaxMapBytes || length < 1 || length > MaxVoiceData || codec == 0) return 0;
            int payload = 1 + 1 + m + 2 + 1 + 1 + length, o = dstOffset;
            if (dst.Length - dstOffset < 2 + payload) return 0;
            dst[o++] = (byte)payload; dst[o++] = (byte)(payload >> 8);
            dst[o++] = Voice; dst[o++] = (byte)m;
            for (int i = 0; i < m; i++) { char c = map[i]; if (c > 127 || !ValidMapChar((byte)c)) return 0; dst[o++] = (byte)c; }
            dst[o++] = (byte)seq; dst[o++] = (byte)(seq >> 8); dst[o++] = codec; dst[o++] = flags;
            Buffer.BlockCopy(data, offset, dst, o, length);
            return 2 + payload;
        }

        /// <summary>The relay's forward of a validated VOICE payload (from its type byte on) to the other voice clients: the speaker's
        /// AID and name (from its HELLO) + the body as received.</summary>
        public static byte[] EncodeVoiceOf(uint aid, byte[] nameUtf8, byte[] voice, int voiceOffset, int voiceLength)
        {
            return new Writer().U8(VoiceOf).U32(aid).U8((byte)nameUtf8.Length).Bytes(nameUtf8).Bytes(voice, voiceOffset + 1, voiceLength - 1).Frame(MaxPayloadVoice);
        }
        // ---- the game-data check (client wave 7, w7host)

        /// <summary>A data stamp: 1..<see cref="MaxStampBytes"/> printable ASCII characters, no space ("g1-0123456789abcdef").</summary>
        public static bool ValidStamp(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > MaxStampBytes) return false;
            foreach (char c in s) if (c < 0x21 || c > 0x7e) return false;
            return true;
        }

        /// <summary>A label cut to <see cref="MaxLabelBytes"/> UTF-8 bytes without splitting a character; control characters dropped.</summary>
        public static byte[] LabelBytes(string label)
        {
            var sb = new StringBuilder();
            foreach (char c in label ?? "") if (!char.IsControl(c)) sb.Append(c);
            string s = sb.ToString();
            while (Encoding.UTF8.GetByteCount(s) > MaxLabelBytes) s = s.Substring(0, s.Length - 1);
            return Encoding.UTF8.GetBytes(s);
        }

        /// <summary>DATA: the client's stamp and label. Null when the stamp is not a valid one (nothing is sent then).</summary>
        public static byte[] EncodeData(string stamp, string label)
        {
            if (!ValidStamp(stamp)) return null;
            var l = LabelBytes(label);
            return new Writer().U8(Data).U8((byte)stamp.Length).Bytes(Encoding.ASCII.GetBytes(stamp)).U8((byte)l.Length).Bytes(l).Frame();
        }

        /// <summary>DATA_HOST: the relay's verdict and the host's own stamp and label. Null when the host's stamp is not valid.</summary>
        public static byte[] EncodeDataHost(byte verdict, string hostStamp, string hostLabel)
        {
            if (!ValidStamp(hostStamp)) return null;
            var l = LabelBytes(hostLabel);
            return new Writer().U8(DataHost).U8(verdict).U8((byte)hostStamp.Length).Bytes(Encoding.ASCII.GetBytes(hostStamp)).U8((byte)l.Length).Bytes(l).Frame();
        }

        public static bool TryDecodeData(byte[] b, int o, int n, out string stamp, out string label)
        {
            stamp = null; label = null;
            var r = new Reader(b, o, n);
            if (!r.Has(1) || r.U8() != Data) return false;
            return ReadStampAndLabel(ref r, out stamp, out label);
        }

        public static bool TryDecodeDataHost(byte[] b, int o, int n, out byte verdict, out string hostStamp, out string hostLabel)
        {
            verdict = 0; hostStamp = null; hostLabel = null;
            var r = new Reader(b, o, n);
            if (!r.Has(2) || r.U8() != DataHost) return false;
            verdict = r.U8();
            if (verdict != DataSame && verdict != DataDiffers) return false;
            return ReadStampAndLabel(ref r, out hostStamp, out hostLabel);
        }

        private static bool ReadStampAndLabel(ref Reader r, out string stamp, out string label)
        {
            stamp = null; label = null;
            if (!r.Has(1)) return false;
            int n = r.U8();
            if (n < 1 || n > MaxStampBytes || !r.Has(n + 1)) return false;
            var s = r.Bytes(n);
            foreach (var c in s) if (c < 0x21 || c > 0x7e) return false;
            int m = r.U8();
            if (m > MaxLabelBytes || r.Left != m) return false;
            string l;
            try { l = new UTF8Encoding(false, true).GetString(r.Bytes(m)); } catch (ArgumentException) { return false; }
            foreach (char c in l) if (char.IsControl(c)) return false;
            stamp = Encoding.ASCII.GetString(s); label = l;
            return true;
        }

        // ---- client wave 18 (w18acct): settings per account

        /// <summary>A proof (SETTINGS_AUTH): 1..<see cref="MaxProofBytes"/> printable ASCII characters, no space (rAthena's web
        /// token: 16 hex digits, rA:login/account.cpp 651-671 LEFT(SHA2(…), WEB_AUTH_TOKEN_LENGTH - 1)).</summary>
        public static bool ValidProof(string s) => ValidStamp(s); // (the same rule: MaxProofBytes = MaxStampBytes = 32)

        /// <summary>SETTINGS_AUTH; null when the proof is not a valid one (nothing is sent then).</summary>
        public static byte[] EncodeSettingsAuth(string proof)
        {
            if (!ValidProof(proof)) return null;
            return new Writer().U8(SettingsAuth).U8((byte)proof.Length).Bytes(Encoding.ASCII.GetBytes(proof)).Frame();
        }

        public static bool TryDecodeSettingsAuth(byte[] b, int o, int n, out string proof)
        {
            proof = null;
            var r = new Reader(b, o, n);
            if (!r.Has(2) || r.U8() != SettingsAuth) return false;
            int k = r.U8();
            if (k < 1 || k > MaxProofBytes || r.Left != k) return false;
            var s = r.Bytes(k);
            foreach (var c in s) if (c < 0x21 || c > 0x7e) return false;
            proof = Encoding.ASCII.GetString(s);
            return true;
        }

        /// <summary>Settings JSON as sent: valid UTF-8, 2..<see cref="MaxSettingsBytes"/> bytes, an object ('{' first). Null when not.</summary>
        public static byte[] SettingsBytes(string json)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '{') return null;
            byte[] d;
            try { d = new UTF8Encoding(false, true).GetBytes(json); } catch (ArgumentException) { return null; }
            return d.Length >= 2 && d.Length <= MaxSettingsBytes ? d : null;
        }

        private static bool ReadSettingsJson(ref Reader r, out string json)
        {
            json = null;
            int k = r.Left;
            if (k < 2 || k > MaxSettingsBytes) return false;
            try { json = new UTF8Encoding(false, true).GetString(r.Bytes(k)); } catch (ArgumentException) { return false; }
            return json.Length > 0 && json[0] == '{';
        }

        /// <summary>SETTINGS_PUT; null when the JSON cannot be sent (empty, not an object, too long) or the time is not after 1970.</summary>
        public static byte[] EncodeSettingsPut(long savedAt, string json)
        {
            var d = SettingsBytes(json);
            if (d == null || savedAt <= 0) return null;
            return new Writer().U8(SettingsPut).I64(savedAt).Bytes(d).Frame(MaxPayloadSettings);
        }

        public static bool TryDecodeSettingsPut(byte[] b, int o, int n, out long savedAt, out string json)
        {
            savedAt = 0; json = null;
            var r = new Reader(b, o, n);
            if (!r.Has(1 + 8) || r.U8() != SettingsPut) return false;
            savedAt = r.I64();
            return savedAt > 0 && ReadSettingsJson(ref r, out json);
        }

        /// <summary>SETTINGS_OF: the host's answer to SETTINGS_AUTH (the JSON only with <see cref="SettingsFound"/>; null when it cannot be sent).</summary>
        public static byte[] EncodeSettingsOf(byte state, long savedAt, string json)
        {
            var w = new Writer().U8(SettingsOf).U8(state).I64(state == SettingsFound ? savedAt : 0);
            if (state == SettingsFound) { var d = SettingsBytes(json); if (d == null || savedAt <= 0) return null; w.Bytes(d); }
            else if (state > SettingsFailed) return null;
            return w.Frame(MaxPayloadSettings);
        }

        public static bool TryDecodeSettingsOf(byte[] b, int o, int n, out byte state, out long savedAt, out string json)
        {
            state = 0; savedAt = 0; json = null;
            var r = new Reader(b, o, n);
            if (!r.Has(1 + 1 + 8) || r.U8() != SettingsOf) return false;
            state = r.U8(); savedAt = r.I64();
            if (state == SettingsFound) return savedAt > 0 && ReadSettingsJson(ref r, out json);
            return state <= SettingsFailed && savedAt == 0 && r.Left == 0;
        }

        /// <summary>SETTINGS_ACK: the host's answer to SETTINGS_PUT, with the save it holds now (0: none).</summary>
        public static byte[] EncodeSettingsAck(byte state, long savedAt) =>
            state > SettingsFailed ? null : new Writer().U8(SettingsAck).U8(state).I64(Math.Max(0, savedAt)).Frame();

        public static bool TryDecodeSettingsAck(byte[] b, int o, int n, out byte state, out long savedAt)
        {
            state = 0; savedAt = 0;
            var r = new Reader(b, o, n);
            if (n != 1 + 1 + 8 || r.U8() != SettingsAck) return false;
            state = r.U8(); savedAt = r.I64();
            return state <= SettingsFailed && savedAt >= 0;
        }

        public static byte[] EncodeLeave() => new Writer().U8(Leave).Frame();
        public static byte[] EncodePing() => new Writer().U8(Ping).Frame();
        public static byte[] EncodeGone(uint aid) => new Writer().U8(Gone).U32(aid).Frame();

        public static byte[] EncodePose(PoseBody p)
        {
            var w = new Writer().U8(Pose);
            WriteBody(w, p);
            return w.Frame();
        }

        private static void WriteBody(Writer w, PoseBody p)
        {
            var m = Encoding.ASCII.GetBytes(p.Map ?? "");
            w.U8((byte)m.Length).Bytes(m).U16(p.Seq).F32(p.MetresPerCell).U8(p.Flags);
            for (int i = 0; i < PoseFloats; i++) w.F32(p.V[i]);
        }

        /// <summary>The relay's forward of a validated POSE payload (<paramref name="pose"/> from its type byte on) to the other
        /// players of that map: the sender's AID and name (from its HELLO, never from the pose) + the body as received.</summary>
        public static byte[] EncodePoseOf(uint aid, byte[] nameUtf8, byte[] pose, int poseOffset, int poseLength)
        {
            return new Writer().U8(PoseOf).U32(aid).U8((byte)nameUtf8.Length).Bytes(nameUtf8).Bytes(pose, poseOffset + 1, poseLength - 1).Frame();
        }

        // ------------------------------------------------------------------------------------------------ decoding

        private struct Reader
        {
            private readonly byte[] _b; private int _o; private readonly int _end;
            public Reader(byte[] b, int o, int n) { _b = b; _o = o; _end = o + n; }
            public int Left => _end - _o;
            public bool Has(int n) => _o + n <= _end;
            public byte U8() => _b[_o++];
            public ushort U16() { ushort v = (ushort)(_b[_o] | _b[_o + 1] << 8); _o += 2; return v; }
            public uint U32() { uint v = (uint)(_b[_o] | _b[_o + 1] << 8 | _b[_o + 2] << 16 | _b[_o + 3] << 24); _o += 4; return v; }
            public long I64() { ulong lo = U32(), hi = U32(); return (long)(lo | hi << 32); } // client wave 18 (w18acct)
            public float F32()
            {
                float v;
                if (BitConverter.IsLittleEndian) v = BitConverter.ToSingle(_b, _o);
                else { var x = new byte[4]; Array.Copy(_b, _o, x, 0, 4); Array.Reverse(x); v = BitConverter.ToSingle(x, 0); }
                _o += 4; return v;
            }
            public byte[] Bytes(int n) { var r = new byte[n]; Array.Copy(_b, _o, r, 0, n); _o += n; return r; }
        }

        private static bool MagicAt(ref Reader r)
        {
            if (!r.Has(Magic.Length)) return false;
            for (int i = 0; i < Magic.Length; i++) if (r.U8() != Magic[i]) return false;
            return true;
        }

        /// <summary>HELLO: false for anything malformed (wrong magic or version, AID 0, a bad name).</summary>
        public static bool TryDecodeHello(byte[] b, int o, int n, out uint aid, out byte[] nameUtf8, out string name) =>
            TryDecodeHello(b, o, n, out aid, out nameUtf8, out name, out _);

        /// <summary>HELLO of version 1 (poses) or 2 (poses + voice, client wave 6): the same layout, the version handed back.</summary>
        public static bool TryDecodeHello(byte[] b, int o, int n, out uint aid, out byte[] nameUtf8, out string name, out byte version)
        {
            aid = 0; nameUtf8 = null; name = null; version = 0;
            var r = new Reader(b, o, n);
            if (!r.Has(1) || r.U8() != Hello || !MagicAt(ref r) || !r.Has(1 + 4 + 1)) return false;
            version = r.U8();
            if (version != Version && version != VersionVoice) return false;
            aid = r.U32();
            int len = r.U8();
            if (aid == 0 || len > MaxNameBytes || r.Left != len) return false;
            nameUtf8 = r.Bytes(len);
            try { name = new UTF8Encoding(false, true).GetString(nameUtf8); } catch (ArgumentException) { return false; }
            foreach (char c in name) if (char.IsControl(c)) return false;
            return true;
        }

        public static bool TryDecodeWelcome(byte[] b, int o, int n, out byte version)
        {
            version = 0;
            var r = new Reader(b, o, n);
            if (!r.Has(1) || r.U8() != Welcome || !MagicAt(ref r) || !r.Has(1)) return false;
            version = r.U8();
            return r.Left == 0;
        }

        /// <summary>WELCOME of version 1 (no capabilities) or 2 (one capability byte): false for anything else.</summary>
        public static bool TryDecodeWelcome(byte[] b, int o, int n, out byte version, out byte caps)
        {
            version = 0; caps = 0;
            var r = new Reader(b, o, n);
            if (!r.Has(1) || r.U8() != Welcome || !MagicAt(ref r) || !r.Has(1)) return false;
            version = r.U8();
            if (version == Version) return r.Left == 0;
            if (version != VersionVoice || r.Left != 1) return false;
            caps = r.U8();
            return true;
        }

        public static bool TryDecodeGone(byte[] b, int o, int n, out uint aid)
        {
            aid = 0;
            var r = new Reader(b, o, n);
            if (n != 5 || r.U8() != Gone) return false;
            aid = r.U32();
            return aid != 0;
        }

        public static bool TryDecodeVoiceEnd(byte[] b, int o, int n, out uint aid)
        {
            aid = 0;
            var r = new Reader(b, o, n);
            if (n != 5 || r.U8() != VoiceEnd) return false;
            aid = r.U32();
            return aid != 0;
        }

        /// <summary>Where the parts of a VOICE / VOICE_OF payload are, in the buffer it was read from (nothing is copied: voice
        /// arrives 50 times a second per talking player).</summary>
        public struct VoiceHeader
        {
            /// <summary>VOICE_OF only: the speaker (from its HELLO), its name's UTF-8 bytes.</summary>
            public uint Aid; public int NameOffset, NameLength;
            /// <summary>The speaker's map (ASCII).</summary>
            public int MapOffset, MapLength;
            public ushort Seq; public byte Codec, Flags;
            public int DataOffset, DataLength;
        }

        /// <summary>A VOICE payload (from its type byte), validated: a map name, a codec, 1..<see cref="MaxVoiceData"/> bytes of data.</summary>
        public static bool TryReadVoice(byte[] b, int o, int n, out VoiceHeader h)
        {
            h = default;
            if (n < 1 || b[o] != Voice) return false;
            return ReadVoiceBody(b, o + 1, o + n, ref h);
        }

        /// <summary>A VOICE_OF payload: the speaker's AID and name, then the VOICE body.</summary>
        public static bool TryReadVoiceOf(byte[] b, int o, int n, out VoiceHeader h)
        {
            h = default;
            if (n < 1 + 4 + 1 || b[o] != VoiceOf) return false;
            h.Aid = (uint)(b[o + 1] | b[o + 2] << 8 | b[o + 3] << 16 | b[o + 4] << 24);
            h.NameLength = b[o + 5]; h.NameOffset = o + 6;
            if (h.Aid == 0 || h.NameLength > MaxNameBytes || 6 + h.NameLength > n) return false;
            return ReadVoiceBody(b, o + 6 + h.NameLength, o + n, ref h);
        }

        private static bool ReadVoiceBody(byte[] b, int p, int end, ref VoiceHeader h)
        {
            if (p >= end) return false;
            int m = b[p++];
            if (m < 1 || m > MaxMapBytes || p + m + 2 + 1 + 1 >= end) return false; // at least one byte of data after the header
            for (int i = 0; i < m; i++) if (!ValidMapChar(b[p + i])) return false;
            h.MapOffset = p; h.MapLength = m; p += m;
            h.Seq = (ushort)(b[p] | b[p + 1] << 8); h.Codec = b[p + 2]; h.Flags = b[p + 3]; p += 4;
            h.DataOffset = p; h.DataLength = end - p;
            return h.Codec != 0 && h.DataLength >= 1 && h.DataLength <= MaxVoiceData;
        }

        /// <summary>A POSE payload (from its type byte): the body, validated (see <see cref="Valid"/>).</summary>
        public static bool TryDecodePose(byte[] b, int o, int n, PoseBody p)
        {
            var r = new Reader(b, o, n);
            if (!r.Has(1) || r.U8() != Pose) return false;
            return ReadBody(ref r, p) && r.Left == 0;
        }

        /// <summary>A POSE_OF payload: the sender's AID and name, and the body, validated.</summary>
        public static bool TryDecodePoseOf(byte[] b, int o, int n, out uint aid, out string name, PoseBody p)
        {
            aid = 0; name = null;
            var r = new Reader(b, o, n);
            if (!r.Has(1 + 4 + 1) || r.U8() != PoseOf) return false;
            aid = r.U32();
            int len = r.U8();
            if (aid == 0 || len > MaxNameBytes || !r.Has(len)) return false;
            try { name = new UTF8Encoding(false, true).GetString(r.Bytes(len)); } catch (ArgumentException) { return false; }
            return ReadBody(ref r, p) && r.Left == 0;
        }

        private static bool ReadBody(ref Reader r, PoseBody p)
        {
            if (!r.Has(1)) return false;
            int m = r.U8();
            if (m < 1 || m > MaxMapBytes || !r.Has(m + 2 + 4 + 1 + 4 * PoseFloats)) return false;
            var map = r.Bytes(m);
            foreach (var c in map) if (!ValidMapChar(c)) return false;
            p.Map = Encoding.ASCII.GetString(map);
            p.Seq = r.U16();
            p.MetresPerCell = r.F32();
            p.Flags = r.U8();
            for (int i = 0; i < PoseFloats; i++) p.V[i] = r.F32();
            return Valid(p);
        }

        /// <summary>rAthena map names: letters, digits, '_', '-', '.', and '@' for instances (e.g. "1@tower", map.cpp map_addinstancemap).</summary>
        public static bool ValidMapChar(byte c) =>
            c >= (byte)'a' && c <= (byte)'z' || c >= (byte)'A' && c <= (byte)'Z' || c >= (byte)'0' && c <= (byte)'9' || c == (byte)'_' || c == (byte)'-' || c == (byte)'.' || c == (byte)'@';

        /// <summary>Finite numbers, positions within <see cref="MaxCoord"/>, rotations that are (nearly) unit quaternions, a sane scale.</summary>
        public static bool Valid(PoseBody p)
        {
            if (float.IsNaN(p.MetresPerCell) || p.MetresPerCell < MinMetresPerCell || p.MetresPerCell > MaxMetresPerCell) return false;
            for (int k = 0; k < 3; k++)
            {
                int o = k * 7;
                for (int i = 0; i < 7; i++) { float v = p.V[o + i]; if (float.IsNaN(v) || float.IsInfinity(v)) return false; }
                for (int i = 0; i < 3; i++) if (Math.Abs(p.V[o + i]) > MaxCoord) return false;
                double q = 0; for (int i = 3; i < 7; i++) q += (double)p.V[o + i] * p.V[o + i];
                if (q < 0.25 || q > 2.25) return false; // |q| between 0.5 and 1.5
            }
            return true;
        }

        /// <summary>
        /// Splits a byte stream into payloads. <see cref="Next"/> returns 1 with a payload, 0 when more bytes are needed,
        /// -1 on a protocol error (length 0 or above <see cref="MaxPayload"/>): the connection must be closed.
        /// </summary>
        public sealed class FrameReader
        {
            private byte[] _buf = new byte[1024];
            private int _start, _end;
            /// <summary>The longest payload this connection may carry: <see cref="MaxPayload"/>, or <see cref="MaxPayloadVoice"/>
            /// once both ends agreed on version 2 (the relay: after a HELLO 2; the client: for a connection it said HELLO 2 on).</summary>
            public int MaxLength = MaxPayload;

            public FrameReader() { }
            public FrameReader(int maxLength) { MaxLength = maxLength; }

            public void Append(byte[] data, int off, int n)
            {
                if (_end + n > _buf.Length)
                {
                    int live = _end - _start;
                    if (live + n > _buf.Length) { var nb = new byte[Math.Max(_buf.Length * 2, live + n)]; Array.Copy(_buf, _start, nb, 0, live); _buf = nb; }
                    else Array.Copy(_buf, _start, _buf, 0, live);
                    _start = 0; _end = live;
                }
                Array.Copy(data, off, _buf, _end, n);
                _end += n;
            }

            /// <summary>1: <paramref name="payload"/> holds one message (a copy); 0: incomplete; -1: bad length.</summary>
            public int Next(out byte[] payload)
            {
                payload = null;
                int k = NextView(out var buf, out int off, out int len);
                if (k != 1) return k;
                payload = new byte[len];
                Array.Copy(buf, off, payload, 0, len);
                return 1;
            }

            /// <summary>As <see cref="Next"/> without a copy: the message is <paramref name="length"/> bytes of
            /// <paramref name="buffer"/> from <paramref name="offset"/>, valid until the next Append or NextView (the client's link
            /// thread reads voice this way: no allocation per frame).</summary>
            public int NextView(out byte[] buffer, out int offset, out int length)
            {
                buffer = null; offset = 0; length = 0;
                if (_end - _start < 2) return 0;
                int len = _buf[_start] | _buf[_start + 1] << 8;
                if (len == 0 || len > MaxLength) return -1;
                if (_end - _start < 2 + len) return 0;
                buffer = _buf; offset = _start + 2; length = len;
                _start += 2 + len;
                // (the buffer is compacted by the next Append, never here: the view stays readable)
                return 1;
            }
        }
    }
}
