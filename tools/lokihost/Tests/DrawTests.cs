using System.Net;
using System.Net.Sockets;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 20 (w20pen), the design's point 57 (the air pen) and the design notes of 2026-10-10 ("Drawings seen by" and "Drawings I see", both Friends
/// only by default): the drawings other players see. The client's wire file (DrawWire.cs, compiled into Core), the relay with raw TCP
/// clients of every age (a version-1 client, a version-2 client from before wave 18, a wave-18 client that draws casting menus but not
/// drawings, a wave-20 client), the relay's filter by the sender's "Drawings seen by" with a table of relations, the relation byte each viewer
/// gets for its own "Drawings I see", and the accounts' proofs (a client that only claims an account gets nothing restricted).
/// </summary>
public class DrawTests
{
    readonly ITestOutputHelper _out;
    public DrawTests(ITestOutputHelper o) { _out = o; }

    // ------------------------------------------------------------------------------------------------ the wire

    static float[] Line(int n, float x0 = 150.5f, float y0 = 12.25f, float z0 = 180.75f, float step = 0.0125f)
    {
        var p = new float[3 * n];
        for (int i = 0; i < n; i++) { p[3 * i] = x0 + i * step; p[3 * i + 1] = y0 + (float)Math.Sin(i * 0.3) * 0.02f; p[3 * i + 2] = z0 - i * step * 0.5f; }
        return p;
    }

    static byte[] Stroke(byte audience = GestureWire.ToEveryone, int id = 7, int n = 12) => DrawWire.EncodeStroke(audience, id, 0, true, 0, 0.0031f, Line(n), 0, n)!;

    static DrawWire.Msg Decode(byte[] frame)
    {
        Assert.Equal(frame.Length - 2, frame[0] | frame[1] << 8);
        Assert.Equal(DrawWire.Draw, frame[2]);
        var m = new DrawWire.Msg();
        Assert.True(DrawWire.TryDecode(frame, 3, frame.Length - 3, m));
        return m;
    }

    [Fact]
    public void A_stroke_part_comes_back_to_a_65534th_of_its_size_and_the_other_ops_the_same()
    {
        var pts = Line(DrawWire.MaxPartPoints);
        var f = DrawWire.EncodeStroke(GestureWire.ToFriends | GestureWire.ToGuild, 65535, 960, false, 0, 0.0031f, pts, 0, DrawWire.MaxPartPoints)!;
        Assert.True(f.Length - 2 + 5 <= PresenceWire.MaxPayloadVoice, $"DRAW_OF would be {f.Length - 2 + 5} bytes"); // (type + AID + relation + body: a voice frame's bound)
        var m = Decode(f);
        Assert.Equal((DrawWire.OpStroke, (byte)(GestureWire.ToFriends | GestureWire.ToGuild), 65535, 960, false, DrawWire.MaxPartPoints), (m.Op, m.Audience, m.Id, m.First, m.Last, m.Count));
        Assert.Equal(0.0031f, m.Width);
        float size = 0f; for (int i = 0; i < pts.Length; i++) size = Math.Max(size, Math.Abs(pts[i] - pts[i % 3]));
        for (int i = 0; i < 3 * m.Count; i++) Assert.InRange(Math.Abs(m.Points[i] - pts[i]), 0f, size / 65534f + 1e-4f); // (the part's own step: its size / 32767, rounded)
        // the last part of a stroke
        Assert.True(Decode(Stroke()).Last);
        // a part taken from the middle of a stroke's points
        var mid = Decode(DrawWire.EncodeStroke(GestureWire.ToEveryone, 3, 4, true, 0, 0.01f, pts, 4, 5)!);
        Assert.Equal((4, 5), (mid.First, mid.Count));
        Assert.InRange(Math.Abs(mid.Points[0] - pts[12]), 0f, 1e-4f);
        // erase, clear, list (an empty list: "I have none now")
        var er = Decode(DrawWire.EncodeIds(GestureWire.ToFriends, DrawWire.OpErase, new[] { 3, 65535 })!);
        Assert.Equal((DrawWire.OpErase, 2, 3, 65535), (er.Op, er.Ids.Count, er.Ids[0], er.Ids[1]));
        Assert.Equal(DrawWire.OpClear, Decode(DrawWire.EncodeClear(GestureWire.ToFamily)!).Op);
        var all = new List<int>(); for (int i = 1; i <= DrawWire.MaxStrokes; i++) all.Add(i);
        var li = DrawWire.EncodeIds(GestureWire.ToEveryone, DrawWire.OpList, all)!;
        Assert.True(li.Length - 2 + 5 <= PresenceWire.MaxPayloadVoice);
        Assert.Equal(DrawWire.MaxStrokes, Decode(li).Ids.Count);
        Assert.Empty(Decode(DrawWire.EncodeIds(GestureWire.ToEveryone, DrawWire.OpList, new int[0])!).Ids);
        // what never travels: "nobody", a stroke id 0, too many points, a point past the map's bound, an empty erase, too many ids
        Assert.Null(DrawWire.EncodeStroke(0, 7, 0, true, 0, 0.003f, pts, 0, 3));
        Assert.Null(DrawWire.EncodeStroke(GestureWire.ToEveryone, 0, 0, true, 0, 0.003f, pts, 0, 3));
        Assert.Null(DrawWire.EncodeStroke(GestureWire.ToEveryone, 7, 0, true, 0, 0.003f, Line(DrawWire.MaxPartPoints + 1), 0, DrawWire.MaxPartPoints + 1));
        Assert.Null(DrawWire.EncodeStroke(GestureWire.ToEveryone, 7, DrawWire.MaxStrokePoints - 2, true, 0, 0.003f, pts, 0, 3));
        var far = Line(3); far[4] = 200000f; Assert.Null(DrawWire.EncodeStroke(GestureWire.ToEveryone, 7, 0, true, 0, 0.003f, far, 0, 3));
        Assert.Null(DrawWire.EncodeIds(GestureWire.ToEveryone, DrawWire.OpErase, new int[0]));
        all.Add(DrawWire.MaxStrokes + 1); Assert.Null(DrawWire.EncodeIds(GestureWire.ToEveryone, DrawWire.OpList, all));
        Assert.Null(DrawWire.EncodeClear(0));
    }

    [Fact]
    public void A_body_a_drawing_could_not_be_made_from_is_refused()
    {
        byte[] Body(byte[] frame) => frame[3..];
        bool Ok(byte[] b) => DrawWire.TryDecode(b, 0, b.Length, new DrawWire.Msg());
        var good = Body(Stroke());
        Assert.True(Ok(good));
        var x = (byte[])good.Clone(); x[0] = 2; Assert.False(Ok(x));                 // another format
        x = (byte[])good.Clone(); x[1] = 0; Assert.False(Ok(x));                     // no audience
        x = (byte[])good.Clone(); x[1] = 16; Assert.False(Ok(x));                    // an unknown audience bit
        x = (byte[])good.Clone(); x[2] = 5; Assert.False(Ok(x));                     // an unknown op
        x = (byte[])good.Clone(); x[3] = 0; x[4] = 0; Assert.False(Ok(x));           // stroke id 0
        x = (byte[])good.Clone(); x[7] = 2; Assert.False(Ok(x));                     // an unknown flag
        x = (byte[])good.Clone(); x[29] = 13; Assert.False(Ok(x));                   // says 13 points, carries 12
        x = (byte[])good.Clone(); x[5] = 0xE6; x[6] = 0x03; Assert.False(Ok(x));     // first 998 + 12 points: past a stroke's most
        x = (byte[])good.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(x, 9); Assert.False(Ok(x));  // a width that is no number
        x = (byte[])good.Clone(); BitConverter.GetBytes(0f).CopyTo(x, 25); Assert.False(Ok(x));        // a step of 0
        x = (byte[])good.Clone(); BitConverter.GetBytes(1e9f).CopyTo(x, 13); Assert.False(Ok(x));      // an origin past the map's bound
        Assert.False(Ok(good.Concat(new byte[] { 0 }).ToArray()));                    // a byte left over
        Assert.False(Ok(good[..^1]));                                                  // one missing
        var clear = Body(DrawWire.EncodeClear(GestureWire.ToEveryone)!);
        Assert.True(Ok(clear)); Assert.False(Ok(clear.Concat(new byte[] { 1 }).ToArray()));
        var erase = Body(DrawWire.EncodeIds(GestureWire.ToEveryone, DrawWire.OpErase, new[] { 4 })!);
        Assert.True(Ok(erase)); erase[4] = 0; Assert.False(Ok(erase));                 // stroke id 0
        // the relay's look: the header only
        var frame = Stroke(GestureWire.ToFriends | GestureWire.ToFamily);
        Assert.True(DrawWire.TryReadHeader(frame, 2, frame.Length - 2, out var aud, out var op));
        Assert.Equal(((byte)(GestureWire.ToFriends | GestureWire.ToFamily), DrawWire.OpStroke), (aud, op));
        frame[4] = 0; Assert.False(DrawWire.TryReadHeader(frame, 2, frame.Length - 2, out _, out _));
        // DRAW_OF: the relation byte has only the three relations' bits
        var of = DrawWire.EncodeDrawOf(2000001, GestureWire.ToFriends | GestureWire.ToGuild, Stroke(), 2, Stroke().Length - 2);
        Assert.True(DrawWire.TryReadDrawOf(of, 2, of.Length - 2, out var aid, out var rel, out int bo, out int bl));
        Assert.Equal((2000001u, (byte)(GestureWire.ToFriends | GestureWire.ToGuild)), (aid, rel));
        Assert.True(DrawWire.TryDecode(of, bo, bl, new DrawWire.Msg()));
        of[7] = 1; Assert.False(DrawWire.TryReadDrawOf(of, 2, of.Length - 2, out _, out _, out _, out _)); // ("everyone" is no relation)
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client of a given age: version 1 (before voice), version 2 from before wave 18, wave 18 (casting menus: GESTURE_READY
    /// when <see cref="MenusReady"/> is called), wave 20 (DRAW_READY when <see cref="Ready"/> is called).</summary>
    sealed class Client : IDisposable
    {
        public readonly TcpClient Tcp = new() { NoDelay = true };
        readonly PresenceWire.FrameReader _fr = new(PresenceWire.MaxPayloadVoice);
        readonly byte[] _buf = new byte[8192];
        public byte Caps;
        public Client(int port) { Tcp.Connect(IPAddress.Loopback, port); }
        public void Send(byte[] frame) => Tcp.GetStream().Write(frame);
        public Client Hello(uint aid, string name, byte version = PresenceWire.VersionVoice)
        {
            Send(PresenceWire.EncodeHello(aid, name, version));
            var w = Next(2000) ?? throw new Exception("no WELCOME");
            Assert.True(PresenceWire.TryDecodeWelcome(w, 0, w.Length, out var v, out Caps));
            Assert.Equal(version, v);
            return this;
        }
        public Client Ready() { Send(DrawWire.EncodeReady()); return this; }
        public Client MenusReady() { Send(GestureWire.EncodeReady()); return this; }
        /// <summary>Proves its account (w18acct's SETTINGS_AUTH with its login's web token) and checks the relay took it.</summary>
        public Client Proved(string proof)
        {
            Send(PresenceWire.EncodeSettingsAuth(proof)!);
            var deadline = DateTime.UtcNow.AddMilliseconds(3000);
            while (DateTime.UtcNow < deadline)
            {
                var p = Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (p == null) break;
                if (p[0] == PresenceWire.SettingsOf) { Assert.True(PresenceWire.TryDecodeSettingsOf(p, 0, p.Length, out var st, out _, out _)); Assert.Equal(PresenceWire.SettingsNone, st); return this; }
            }
            throw new Exception("no SETTINGS_OF");
        }
        public void Pose(string map)
        {
            var p = new PresenceWire.PoseBody { Map = map, Seq = 1, MetresPerCell = 0.032f, Flags = 3 };
            float[] v = { 150f, 20f, 180f, 0, 0, 0, 1, 149f, 12f, 180f, 0, 0, 0, 1, 151f, 12f, 180f, 0, 0, 0, 1 };
            v.CopyTo(p.V, 0);
            Send(PresenceWire.EncodePose(p));
        }
        public byte[]? Next(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                if (_fr.Next(out var p) == 1) return p;
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                Tcp.Client.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);
                int n;
                try { n = Tcp.GetStream().Read(_buf, 0, _buf.Length); }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { return null; }
                if (n <= 0) throw new EndOfStreamException("closed by the relay");
                _fr.Append(_buf, 0, n);
            }
        }
        /// <summary>Everything that arrives within <paramref name="ms"/>: the DRAW_OF frames (AID, relation, body), the GESTURE_OF frames, and
        /// how many other frames.</summary>
        public (List<(uint aid, byte rel, byte[] body)> draws, int menus, int others) Drain(int ms)
        {
            var draws = new List<(uint, byte, byte[])>(); int menus = 0, others = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                var p = Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (p == null) break;
                if (p[0] == DrawWire.DrawOf) { Assert.True(DrawWire.TryReadDrawOf(p, 0, p.Length, out var aid, out var rel, out int bo, out int bl)); draws.Add((aid, rel, p[bo..(bo + bl)])); }
                else if (p[0] == GestureWire.GestureOf) menus++;
                else others++;
            }
            return (draws, menus, others);
        }
        public void Dispose() => Tcp.Dispose();
    }

    static async Task<PoseRelay> Relay(IRelationsSource? relations = null, PoseRelay.Limits? limits = null, bool drawings = true, bool menus = true, IAccountSettingsStore? accounts = null, List<string>? log = null)
    {
        var r = new PoseRelay(0, limits) { Relations = relations, DrawingsEnabled = drawings, GesturesEnabled = menus, AccountSettings = accounts };
        if (log != null) r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        await Task.Yield();
        return r;
    }

    static async Task Until(Func<bool> cond, int ms = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(cond());
    }

    /// <summary>Poses each client on <paramref name="map"/> and waits until the relay has every one of them there (each connection is read on its
    /// own: a later pose of one client may be handled before an earlier one of another), then lets their pose frames arrive and drops them.</summary>
    static async Task OnMap(PoseRelay r, string map, params (Client c, uint aid)[] cs)
    {
        foreach (var (c, _) in cs) c.Pose(map);
        await Until(() => { var snap = r.Snapshot(); return cs.All(x => snap.Any(p => p.aid == x.aid && p.map == map)); });
        await Task.Delay(100);
        foreach (var (c, _) in cs) c.Drain(100);
    }
    /// <summary>A PING from each (a client that sends nothing for Limits.IdleAfter is closed by the relay).</summary>
    static void Ping(IEnumerable<Client> cs) { foreach (var c in cs) c.Send(PresenceWire.EncodePing()); }

    sealed class Table : IRelationsSource
    {
        public readonly Dictionary<uint, CharRelations> ByAid = new();
        public int Loads;
        public Task<CharRelations?> LoadAsync(uint aid, string name)
        {
            Interlocked.Increment(ref Loads);
            lock (ByAid) return Task.FromResult(ByAid.TryGetValue(aid, out var c) ? c : null);
        }
        public void Set(uint aid, CharRelations c) { lock (ByAid) ByAid[aid] = c; }
    }
    static MemoryAccountSettings Logins(params uint[] aids)
    {
        var m = new MemoryAccountSettings();
        foreach (var a in aids) m.Proofs[a] = Token(a);
        return m;
    }
    static string Token(uint aid) => "token-" + aid;
    static CharRelations Char(uint cid, uint partner = 0, uint father = 0, uint mother = 0, uint child = 0, uint guild = 0, params uint[] friends) =>
        new(cid, partner, father, mother, child, guild, new HashSet<uint>(friends));

    [Fact]
    public async Task Welcome_says_the_relay_forwards_drawings_and_an_older_relay_closes_a_connection_that_sends_one()
    {
        await using var r = await Relay();
        using var v2 = new Client(r.Port).Hello(2000001, "Tester");
        Assert.NotEqual(0, v2.Caps & DrawWire.CapDraw);
        Assert.NotEqual(0, v2.Caps & GestureWire.CapGesture); // (both, side by side: no bit is shared)
        Assert.Equal(0, DrawWire.CapDraw & (PresenceWire.CapVoice | PresenceWire.CapData | Loki.Horde.Net.HordeWire.CapHorde | PresenceWire.CapSettings | GestureWire.CapGesture));
        using var v1 = new Client(r.Port);
        v1.Send(PresenceWire.EncodeHello(2000002, "Old", PresenceWire.Version));
        Assert.Equal(PresenceWire.EncodeWelcome()[2..], v1.Next(2000)!); // byte for byte what a client of wave 3 always got
        // a relay that does not forward drawings (switched off; a relay before wave 20 the same): no bit, and a DRAW is the unknown message it
        // is: the connection closes. The client (Net/PresenceRelayChannel.cs) sends only after the bit.
        await using var off = await Relay(drawings: false);
        using var c = new Client(off.Port).Hello(2000003, "Tester");
        Assert.Equal(0, c.Caps & DrawWire.CapDraw);
        c.Pose("prontera");
        c.Send(Stroke());
        Assert.Throws<EndOfStreamException>(() => c.Drain(1500));
    }

    [Fact]
    public async Task A_drawing_shown_to_everyone_reaches_the_new_clients_of_its_map_and_no_older_one()
    {
        await using var r = await Relay();
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester").Ready();                   // wave 20, same map
        using var w18 = new Client(r.Port).Hello(2000003, "Wave 18").MenusReady();                 // casting menus, no drawings
        using var old2 = new Client(r.Port).Hello(2000004, "Before wave 18");                       // version 2, never READY
        using var old1 = new Client(r.Port).Hello(2000005, "Wave 3", PresenceWire.Version);          // version 1
        using var far = new Client(r.Port).Hello(2000006, "Elsewhere").Ready();                    // wave 20, another map
        await OnMap(r, "prontera", (b, 2000002), (w18, 2000003), (old2, 2000004), (old1, 2000005));
        await OnMap(r, "geffen", (far, 2000006));
        // a drawing before the sender's first pose: the relay does not know its map yet: forwarded to nobody
        a.Send(Stroke());
        await Until(() => r.DrawsIn == 1);
        Assert.Equal(1, r.DrawsDropped);
        await OnMap(r, "prontera", (a, 2000001));
        foreach (var c in new[] { b, w18, old2, old1, far }) c.Drain(200);
        var frame = Stroke();
        a.Send(frame);
        var got = b.Drain(500).draws;
        Assert.Single(got);
        Assert.Equal((2000001u, (byte)0), (got[0].aid, got[0].rel)); // (no relations source: the relation is not known: 0)
        Assert.Equal(frame[3..], got[0].body); // the body as it was sent
        var m = new DrawWire.Msg();
        Assert.True(DrawWire.TryDecode(got[0].body, 0, got[0].body.Length, m));
        Assert.Equal((DrawWire.OpStroke, 7, 12), (m.Op, m.Id, m.Count));
        foreach (var c in new[] { w18, old2, old1, far }) Assert.Empty(c.Drain(300).draws); // (never sent one: the wave-18 reader would drop it, the older ones close)
        Assert.Empty(a.Drain(200).draws); // never back to the sender
        Assert.Equal((1L, 0L), (r.DrawsForwarded, r.DrawsWithheld));
        // the older clients still get the poses as before
        a.Pose("prontera");
        Assert.Equal(PresenceWire.PoseOf, old1.Next(1000)![0]);
        Assert.Equal(PresenceWire.PoseOf, w18.Next(1000)![0]);
    }

    [Fact]
    public async Task Drawings_seen_by_friends_family_and_guild_combine_whoever_is_left_out_is_never_sent_them_and_each_viewer_learns_its_relation()
    {
        // the sender (150000) and six players on its map: a friend, its spouse, a guild mate, a stranger, its adopted child, and a friend who is
        // also in its guild (the design's answers: the two settings have the casting menus' Friends / Family / Guild choices)
        var t = new Table();
        t.Set(2000001, Char(150000, partner: 150002, guild: 3, friends: new uint[] { 150001, 150006 }));
        t.Set(2000002, Char(150001));                      // on the sender's friend list (one list is enough)
        t.Set(2000003, Char(150002, partner: 150000));     // the spouse
        t.Set(2000004, Char(150003, guild: 3));             // the same guild
        t.Set(2000005, Char(150004, guild: 4, friends: 150099)); // nobody to the sender
        t.Set(2000006, Char(150005, father: 150000));      // the adopted child (on the child's row only)
        t.Set(2000007, Char(150006, guild: 3));             // a friend in the same guild
        var log = new List<string>();
        await using var r = await Relay(t, accounts: Logins(2000001, 2000002, 2000003, 2000004, 2000005, 2000006, 2000007), log: log);
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready();
        var names = new[] { "Friend", "Spouse", "Guildmate", "Stranger", "Child", "Friend in the guild" };
        var v = names.Select((n, i) => new Client(r.Port).Hello(2000002 + (uint)i, n).Proved(Token(2000002 + (uint)i)).Ready()).ToArray();
        try
        {
            await OnMap(r, "prontera", v.Select((c, i) => (c, 2000002 + (uint)i)).Append((s, 2000001u)).ToArray());
            await Until(() => t.Loads >= 7);
            await Task.Delay(100);
            foreach (var c in v.Append(s)) c.Drain(150);
            int[] clears = new int[v.Length]; // (the relay's own "drop them" to a viewer it had sent strokes and leaves out now)
            int[] Seen(byte audience, out byte[] rels)
            {
                Ping(v.Append(s)); // (the viewers say nothing else for the whole test)
                s.Send(Stroke(audience));
                var got = v.Select(c => c.Drain(250).draws).ToArray();
                for (int i = 0; i < got.Length; i++) clears[i] = got[i].Count(d => d.body[2] == DrawWire.OpClear && d.rel == 0);
                var strokes = got.Select(g => g.Where(d => d.body[2] == DrawWire.OpStroke).ToList()).ToArray();
                rels = strokes.Select(g => g.Count == 1 ? g[0].rel : (byte)255).ToArray();
                return strokes.Select(g => g.Count).ToArray();
            }
            const byte F = GestureWire.ToFriends, Fa = GestureWire.ToFamily, G = GestureWire.ToGuild;
            Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, Seen(GestureWire.ToEveryone, out var rel));
            // every viewer learns what the sender is to IT (its own "Drawings I see" decides): the stranger nothing
            Assert.Equal(new byte[] { F, Fa, G, 0, Fa, F | G }, rel);
            Assert.Equal(new[] { 1, 0, 0, 0, 0, 1 }, Seen(F, out rel));      // the default: friends only
            Assert.Equal(new byte[] { F, 255, 255, 255, 255, F | G }, rel);
            // the four who were sent the "Everyone" stroke and are left out now are told, once each, to drop this player's drawings
            Assert.Equal(new[] { 0, 1, 1, 1, 1, 0 }, clears);
            Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, Seen(Fa, out _));       // spouse and child (either row says it)
            Assert.Equal(new[] { 1, 0, 0, 0, 0, 1 }, clears);                 // (the two friends had strokes: dropped; the others had none left)
            Assert.Equal(new[] { 0, 0, 1, 0, 0, 1 }, Seen(G, out _));
            Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, clears);
            Assert.Equal(new[] { 1, 0, 1, 0, 0, 1 }, Seen(F | G, out _));
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, clears);                 // (nobody who holds strokes is left out)
            Assert.Equal(new[] { 1, 1, 1, 0, 1, 1 }, Seen(F | Fa | G, out _)); // the stranger never, whatever the mix
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, clears);
            Assert.Equal(6 + 2 + 2 + 2 + 3 + 5L, r.DrawsForwarded);
            Assert.Equal(0 + 4 + 4 + 4 + 3 + 1L, r.DrawsWithheld);
            Assert.Equal(4 + 2 + 2L, r.DrawsRevoked);
            // an erasure, a clear and a list go the same way: whoever was allowed hears it; the others hear nothing
            // (the frames of the sender itself, by op; the relay's own clears, relation 0, apart)
            int[] Got(byte op) => v.Select(c => c.Drain(250).draws.Count(d => d.body[2] == op && !(op == DrawWire.OpClear && d.rel == 0))).ToArray();
            Ping(v.Append(s));
            s.Send(DrawWire.EncodeClear(F | Fa | G)!); // (everyone who holds strokes drops them: nobody is owed a "drop them" below)
            Assert.Equal(new[] { 1, 1, 1, 0, 1, 1 }, Got(DrawWire.OpClear));
            Ping(v.Append(s));
            s.Send(DrawWire.EncodeIds(F, DrawWire.OpErase, new[] { 7 })!);
            Assert.Equal(new[] { 1, 0, 0, 0, 0, 1 }, Got(DrawWire.OpErase));
            Ping(v.Append(s));
            s.Send(DrawWire.EncodeClear(Fa)!);
            Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, Got(DrawWire.OpClear));
            Ping(v.Append(s));
            s.Send(DrawWire.EncodeIds(G, DrawWire.OpList, new int[0])!);
            Assert.Equal(new[] { 0, 0, 1, 0, 0, 1 }, Got(DrawWire.OpList));
            Assert.Equal(4 + 2 + 2L, r.DrawsRevoked); // (none since: a clear from the sender itself settles it)
            _out.WriteLine(r.DrawStatsLine());
        }
        finally { foreach (var c in v) c.Dispose(); }
    }

    /// <summary>The casting menus' rule (client wave 18 merge): a client that only CLAIMS an account in its HELLO is sent "Everyone" drawings only,
    /// with no relation (0: its own "Drawings I see" shows them only on "Everyone"), and its own restricted drawings reach nobody; a relay without
    /// relations or without the accounts' proofs sends restricted drawings to nobody.</summary>
    [Fact]
    public async Task A_client_claiming_a_friends_account_without_its_token_receives_nothing_restricted_and_no_relation()
    {
        var t = new Table();
        t.Set(2000001, Char(150000, partner: 150002, guild: 3, friends: 150001));
        t.Set(2000002, Char(150001));
        t.Set(2000003, Char(150002, partner: 150000, guild: 3, friends: 150000));
        await using var r = await Relay(t, accounts: Logins(2000001, 2000002, 2000003));
        const byte All = GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild;
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready();
        using var spoof = new Client(r.Port).Hello(2000002, "Friend").Ready(); // no token
        await OnMap(r, "prontera", (s, 2000001), (spoof, 2000002));
        await Until(() => t.Loads >= 2); await Task.Delay(100);
        spoof.Drain(150); s.Drain(150);
        foreach (var aud in new[] { GestureWire.ToFriends, All }) { s.Send(Stroke(aud)); Assert.Empty(spoof.Drain(300).draws); }
        s.Send(Stroke());
        var got = spoof.Drain(300).draws;
        Assert.Single(got);
        Assert.Equal(0, got[0].rel); // ("Everyone" reaches it, as any player on the map; what the sender is to it: not told)
        // its own restricted drawings reach nobody, not even the sender's proved spouse
        using var spouse = new Client(r.Port).Hello(2000003, "Spouse").Proved(Token(2000003)).Ready();
        await OnMap(r, "prontera", (spouse, 2000003)); await Until(() => t.Loads >= 3); await Task.Delay(100); spouse.Drain(150); spoof.Drain(150); s.Drain(150);
        Ping(new[] { s, spoof, spouse });
        spoof.Send(Stroke(All));
        Assert.Empty(spouse.Drain(300).draws);
        Assert.Empty(s.Drain(200).draws);
        // between proved accounts they travel, with the relation
        s.Send(Stroke(GestureWire.ToFamily));
        var sp = spouse.Drain(300).draws;
        Assert.Single(sp);
        Assert.Equal((byte)(GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild), sp[0].rel);
        // no relations source, or no proofs: restricted drawings go to nobody; "Everyone" still to everyone, relation 0
        foreach (var (rel, acc) in new (IRelationsSource?, IAccountSettingsStore?)[] { (null, Logins(2000001, 2000002)), (t, null) })
        {
            await using var r2 = await Relay(rel, accounts: acc);
            using var s2 = new Client(r2.Port).Hello(2000001, "Sender"); using var f2 = new Client(r2.Port).Hello(2000002, "Friend");
            if (acc != null) { s2.Proved(Token(2000001)); f2.Proved(Token(2000002)); }
            s2.Ready(); f2.Ready();
            await OnMap(r2, "prontera", (s2, 2000001), (f2, 2000002));
            s2.Send(Stroke(All));
            Assert.Empty(f2.Drain(300).draws);
            s2.Send(Stroke());
            var e = f2.Drain(300).draws;
            Assert.Single(e);
            Assert.Equal(0, e[0].rel);
        }
    }

    [Fact]
    public async Task A_stranger_becomes_a_friend_and_back_and_the_relay_follows_within_its_refresh_time()
    {
        var t = new Table();
        t.Set(2000001, Char(150000)); t.Set(2000002, Char(150001));
        await using var r = await Relay(t, new PoseRelay.Limits { RelationsRefresh = TimeSpan.FromMilliseconds(300) }, accounts: Logins(2000001, 2000002));
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready();
        using var f = new Client(r.Port).Hello(2000002, "Soon a friend").Proved(Token(2000002)).Ready();
        await OnMap(r, "prontera", (s, 2000001), (f, 2000002)); await Until(() => t.Loads >= 2); await Task.Delay(50); f.Drain(150);
        s.Send(Stroke(GestureWire.ToFriends));
        Assert.Empty(f.Drain(300).draws); // a stranger
        t.Set(2000001, Char(150000, friends: 150001)); // rAthena saved the friendship
        await Task.Delay(400);
        int loads0 = t.Loads;
        s.Send(Stroke(GestureWire.ToFriends)); // (finds them old and has them read again)
        await Until(() => t.Loads >= loads0 + 2);
        await Task.Delay(100);
        f.Drain(200);
        s.Send(Stroke(GestureWire.ToFriends));
        var got = f.Drain(500).draws;
        Assert.Single(got);
        Assert.Equal(GestureWire.ToFriends, got[0].rel);
        // removed again (the smoke's way: the friend removed in the game, both saved): back to a stranger
        t.Set(2000001, Char(150000)); t.Set(2000002, Char(150001));
        await Task.Delay(400);
        loads0 = t.Loads;
        s.Send(Stroke(GestureWire.ToFriends));
        await Until(() => t.Loads >= loads0 + 2);
        await Task.Delay(100);
        f.Drain(300);
        s.Send(Stroke(GestureWire.ToFriends));
        // the friend of a moment ago holds the stroke it was sent: left out now, it is told ONCE to drop this player's drawings (the relay's own
        // clear, no relation) and never sent the stroke
        var x = f.Drain(400).draws;
        Assert.Single(x);
        Assert.Equal((DrawWire.OpClear, (byte)0, 2000001u), (x[0].body[2], x[0].rel, x[0].aid));
        Assert.True(DrawWire.TryDecode(x[0].body, 0, x[0].body.Length, new DrawWire.Msg()));
        s.Send(Stroke(GestureWire.ToFriends));
        Assert.Empty(f.Drain(400).draws); // (once: it holds nothing now)
        Assert.Equal(1L, r.DrawsRevoked);
        s.Send(Stroke());
        var e = f.Drain(400).draws;
        Assert.Single(e);
        Assert.Equal(0, e[0].rel); // ("Everyone": sent; what the sender is to it now: nothing)
    }

    [Fact]
    public async Task A_flood_or_a_malformed_drawing_is_dropped_and_the_connection_stays()
    {
        await using var r = await Relay(limits: new PoseRelay.Limits { MaxDrawsPerSecond = 10 });
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Viewer").Ready();
        await OnMap(r, "prontera", (a, 2000001), (b, 2000002));
        var frame = Stroke();
        for (int i = 0; i < 30; i++) a.Send(frame);
        var got = b.Drain(600).draws.Count;
        Assert.InRange(got, 10, 20); // (a second's window may start inside the burst)
        Assert.True(r.DrawsDropped >= 10);
        // malformed: no audience; an unknown op; too short
        var bad = (byte[])frame.Clone(); bad[4] = 0; a.Send(bad);
        bad = (byte[])frame.Clone(); bad[5] = 9; a.Send(bad);
        a.Send(new byte[] { 3, 0, DrawWire.Draw, 1, 1 });
        await Task.Delay(1100);
        a.Pose("prontera");
        Assert.Equal(PresenceWire.PoseOf, b.Next(1000)![0]); // still connected, poses go on
        a.Send(frame);
        Assert.Single(b.Drain(400).draws);
        _out.WriteLine(r.DrawStatsLine());
    }

    [Fact]
    public void The_relation_bits_are_the_casting_menus_relations()
    {
        var tester = Char(150000, partner: 150002, guild: 3, friends: 150001);
        Assert.Equal(GestureWire.ToFriends, PoseRelay.RelationBits(tester, Char(150001)));
        Assert.Equal((byte)(GestureWire.ToFamily | GestureWire.ToGuild), PoseRelay.RelationBits(tester, Char(150002, partner: 150000, guild: 3)));
        Assert.Equal(0, PoseRelay.RelationBits(tester, Char(150009, guild: 4)));
        Assert.Equal(0, PoseRelay.RelationBits(tester, null));
        Assert.Equal(0, PoseRelay.RelationBits(null, tester));
        // the same answer as the relay's own filter for every relation alone
        foreach (var o in new[] { Char(150001), Char(150002, partner: 150000), Char(150003, guild: 3), Char(150004) })
            foreach (var bit in new[] { GestureWire.ToFriends, GestureWire.ToFamily, GestureWire.ToGuild })
                Assert.Equal(PoseRelay.Allowed(tester, o, bit), (PoseRelay.RelationBits(tester, o) & bit) != 0);
    }
}
