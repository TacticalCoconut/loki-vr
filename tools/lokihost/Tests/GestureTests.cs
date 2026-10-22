using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 18 (w18share), the design's point 33 and its addition: the casting menus friends see. The client's wire file (GestureWire.cs,
/// compiled into Core), the relay with raw TCP clients of every age (a version-1 client, a version-2 client from before wave 18, a
/// wave-18 client), the privacy filter with a table of relations, and RelationsDb against rAthena's own tables in a private MariaDB.
/// </summary>
public class GestureTests
{
    readonly ITestOutputHelper _out;
    public GestureTests(ITestOutputHelper o) { _out = o; }

    // ------------------------------------------------------------------------------------------------ the wire

    static GestureWire.State Pad(byte audience = GestureWire.ToEveryone)
    {
        var s = new GestureWire.State { Audience = audience, Menu = GestureWire.MenuRight, Kind = GestureWire.KindPad, Anchor = GestureWire.AnchorMap,
            X = 151.25f, Y = 9.5f, Z = 180.75f, Qx = 0f, Qy = 0.7071068f, Qz = 0f, Qw = 0.7071068f, Scale = 0.0002f, Row = 5, Lit = 4 };
        s.Entries.Add(new GestureWire.Entry { Row = 5, Slot = 0, Skill = true, Listed = true, Learned = true, Id = 28 });
        s.Entries.Add(new GestureWire.Entry { Row = 5, Slot = 4, Skill = false, HasCount = true, Id = 501, Count = 30000 });
        s.Entries.Add(new GestureWire.Entry { Row = 5, Slot = 8, Skill = false, HasCount = true, Id = 1000123, Count = 0 });
        return s;
    }

    static GestureWire.State Decode(byte[] frame)
    {
        Assert.Equal(frame.Length - 2, frame[0] | frame[1] << 8);
        Assert.Equal(GestureWire.Gesture, frame[2]);
        var d = new GestureWire.State();
        Assert.True(GestureWire.TryDecode(frame, 3, frame.Length - 3, d));
        return d;
    }

    [Fact]
    public void Every_kind_of_menu_travels_and_comes_back_the_same()
    {
        var pad = Decode(GestureWire.Encode(Pad())!);
        Assert.Equal((GestureWire.MenuRight, GestureWire.KindPad, GestureWire.AnchorMap, 5, 4), (pad.Menu, pad.Kind, pad.Anchor, pad.Row, pad.Lit));
        Assert.Equal((151.25f, 9.5f, 180.75f, 0.0002f), (pad.X, pad.Y, pad.Z, pad.Scale));
        Assert.InRange(pad.Qy, 0.7070f, 0.7072f);
        Assert.Equal(3, pad.Entries.Count);
        Assert.Equal((5, 0, true, true, true, 28), (pad.Entries[0].Row, pad.Entries[0].Slot, pad.Entries[0].Skill, pad.Entries[0].Listed, pad.Entries[0].Learned, pad.Entries[0].Id));
        Assert.Equal((false, true, 501, 30000), (pad.Entries[1].Skill, pad.Entries[1].HasCount, pad.Entries[1].Id, pad.Entries[1].Count));
        Assert.Equal((1000123, 0), (pad.Entries[2].Id, pad.Entries[2].Count)); // an item id over 65,535 (rAthena's are 32 bits)

        var fl = new GestureWire.State { Menu = GestureWire.MenuLeft, Kind = GestureWire.KindFlick, Flags = GestureWire.FlagFront, Anchor = GestureWire.AnchorHead,
            X = 0f, Y = -0.1145f, Z = 0.589f, Scale = 0.00021f, Phase = 2, Dir1 = 3, Dir2 = 6, CornerX = 0.0712f, CornerY = -0.031f, CursorX = 0.1203f, CursorY = -0.0045f };
        var f = Decode(GestureWire.Encode(fl)!);
        Assert.Equal((GestureWire.KindFlick, GestureWire.FlagFront, 2, 3, 6), (f.Kind, f.Flags, f.Phase, f.Dir1, f.Dir2));
        foreach (var (got, want) in new[] { (f.CornerX, 0.0712f), (f.CornerY, -0.031f), (f.CursorX, 0.1203f), (f.CursorY, -0.0045f) })
            Assert.InRange(Math.Abs(got - want), 0f, GestureWire.CursorUnit * 0.5001f); // (half millimetres)

        var wh = new GestureWire.State { Menu = GestureWire.MenuWheel, Kind = GestureWire.KindWheel, Anchor = GestureWire.AnchorMap, Scale = 0.0003f,
            Tab = 1, Part = GestureWire.PartCell, Index = 6, Sub = 2, Visible = true, InPlace = true, Slices = 6, CursorX = 0.02f, CursorY = 0.035f };
        wh.Entries.Add(new GestureWire.Entry { Row = GestureWire.RowWand, Slot = 5, Skill = true, Listed = true, Learned = true, Id = 2008 });
        var w = Decode(GestureWire.Encode(wh)!);
        Assert.Equal((1, GestureWire.PartCell, 6, 2, true, true, 6), (w.Tab, w.Part, w.Index, w.Sub, w.Visible, w.InPlace, w.Slices));
        Assert.Equal((GestureWire.RowWand, 5, 2008), (w.Entries[0].Row, w.Entries[0].Slot, w.Entries[0].Id));

        var bar = Decode(GestureWire.Encode(new GestureWire.State { Menu = GestureWire.MenuBar, Kind = GestureWire.KindBar, Anchor = GestureWire.AnchorLeft, Scale = 0.0003f, Row = 2, Lit = -1 })!);
        Assert.Equal((GestureWire.KindBar, GestureWire.AnchorLeft, 2, -1, 0), (bar.Kind, bar.Anchor, bar.Row, bar.Lit, bar.Entries.Count));
        var wand = Decode(GestureWire.Encode(new GestureWire.State { Menu = GestureWire.MenuWand, Kind = GestureWire.KindWand, Anchor = GestureWire.AnchorRight, Scale = 1.25f })!);
        Assert.Equal((GestureWire.KindWand, GestureWire.AnchorRight, 1.25f), (wand.Kind, wand.Anchor, wand.Scale));
        var closed = GestureWire.Encode(new GestureWire.State { Menu = GestureWire.MenuWheel, Kind = GestureWire.KindClosed, Audience = GestureWire.ToFriends | GestureWire.ToGuild })!;
        Assert.Equal(2 + 1 + GestureWire.HeaderBytes, closed.Length);
        var c = Decode(closed);
        Assert.Equal((GestureWire.KindClosed, (byte)(GestureWire.ToFriends | GestureWire.ToGuild)), (c.Kind, c.Audience));
        // "nobody" never travels
        Assert.Null(GestureWire.Encode(new GestureWire.State { Audience = 0, Menu = GestureWire.MenuLeft, Kind = GestureWire.KindClosed }));
    }

    [Fact]
    public void Entries_that_do_not_fit_a_voice_frame_are_left_out_last_first()
    {
        var s = new GestureWire.State { Menu = GestureWire.MenuWheel, Kind = GestureWire.KindWheel, Scale = 0.0003f, Slices = 8, Visible = true };
        for (int i = 0; i < 64; i++) s.Entries.Add(new GestureWire.Entry { Row = i % 8, Slot = i / 8, HasCount = true, Id = 2000000000 - i, Count = GestureWire.MaxCount });
        var f = GestureWire.Encode(s)!;
        Assert.True(f.Length - 2 + 4 <= PresenceWire.MaxPayloadVoice, $"GESTURE_OF would be {f.Length - 2 + 4} bytes");
        Assert.True(s.Left > 0);
        var d = Decode(f);
        Assert.Equal(64 - s.Left, d.Entries.Count);
        Assert.Equal(2000000000, d.Entries[0].Id); // the first (the most needed) are the ones kept
    }

    [Fact]
    public void A_body_a_picture_could_not_be_drawn_from_is_refused()
    {
        byte[] Body(GestureWire.State s) => GestureWire.Encode(s)![3..];
        bool Ok(byte[] b) => GestureWire.TryDecode(b, 0, b.Length, new GestureWire.State());
        var good = Body(Pad());
        Assert.True(Ok(good));
        var x = (byte[])good.Clone(); x[0] = 2; Assert.False(Ok(x));                    // another format
        x = (byte[])good.Clone(); x[1] = 0; Assert.False(Ok(x));                        // no audience
        x = (byte[])good.Clone(); x[1] = 16; Assert.False(Ok(x));                       // an unknown audience bit
        x = (byte[])good.Clone(); x[3] = GestureWire.KindWheel; Assert.False(Ok(x));    // a hand's menu is never a wheel
        Assert.False(Ok(good.Concat(new byte[] { 0 }).ToArray()));                       // a byte left over
        Assert.False(Ok(good[..^1]));                                                     // one missing
        var nan = Pad(); nan.X = float.NaN; Assert.False(Ok(Body(nan)));
        var far = Pad(); far.Z = 200000f; Assert.False(Ok(Body(far)));
        var notTurn = Pad(); notTurn.Qx = notTurn.Qy = notTurn.Qz = notTurn.Qw = 0f; Assert.False(Ok(Body(notTurn)));
        var lit = Pad(); lit.Lit = 9; Assert.False(Ok(Body(lit)));
        var slot = Pad(); slot.Entries.Add(new GestureWire.Entry { Row = 5, Slot = 9, Id = 1 }); Assert.False(Ok(Body(slot)));
        var wandRow = Pad(); wandRow.Entries.Add(new GestureWire.Entry { Row = GestureWire.RowWand, Slot = 0, Id = 1 }); Assert.False(Ok(Body(wandRow))); // only a wheel has the wand's ring
        var wh = new GestureWire.State { Menu = GestureWire.MenuWheel, Kind = GestureWire.KindWheel, Scale = 0.0003f, Slices = 4, Part = GestureWire.PartInner, Index = 4 };
        Assert.False(Ok(Body(wh)));                                                       // slice 5 of a four-slice ring
        wh.Index = 3; Assert.True(Ok(Body(wh)));
        // the relay's look: the header only
        var frame = GestureWire.Encode(Pad(GestureWire.ToFriends | GestureWire.ToFamily))!;
        Assert.True(GestureWire.TryReadHeader(frame, 2, frame.Length - 2, out var aud, out var menu));
        Assert.Equal((GestureWire.ToFriends | GestureWire.ToFamily, GestureWire.MenuRight), (aud, menu));
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client of a given age: version 1 (before voice), version 2 from before wave 18 (never says GESTURE_READY),
    /// or wave 18 (READY when <see cref="Ready"/> is called).</summary>
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
        public Client Ready() { Send(GestureWire.EncodeReady()); return this; }
        /// <summary>Client wave 18 merge: proves its account (w18acct's SETTINGS_AUTH with its login's web token) and returns the relay's
        /// answer (SETTINGS_OF's state: None / Found = proved, Refused = not this account's login).</summary>
        public byte Prove(string proof)
        {
            Send(PresenceWire.EncodeSettingsAuth(proof)!);
            var deadline = DateTime.UtcNow.AddMilliseconds(3000);
            while (DateTime.UtcNow < deadline)
            {
                var p = Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (p == null) break;
                if (p[0] == PresenceWire.SettingsOf) { Assert.True(PresenceWire.TryDecodeSettingsOf(p, 0, p.Length, out var st, out _, out _)); return st; }
            }
            throw new Exception("no SETTINGS_OF");
        }
        /// <summary>Proves its account and checks that the relay took it (no settings stored yet in these tests).</summary>
        public Client Proved(string proof) { Assert.Equal(PresenceWire.SettingsNone, Prove(proof)); return this; }
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
        /// <summary>Everything that arrives within <paramref name="ms"/>: the GESTURE_OF frames (AID, body) and how many other frames.</summary>
        public (List<(uint aid, byte[] body)> menus, int others) Drain(int ms)
        {
            var menus = new List<(uint, byte[])>(); int others = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                var p = Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (p == null) break;
                if (p[0] == GestureWire.GestureOf) { Assert.True(GestureWire.TryReadGestureOf(p, 0, p.Length, out var aid, out int bo, out int bl)); menus.Add((aid, p[bo..(bo + bl)])); }
                else others++;
            }
            return (menus, others);
        }
        public void Dispose() => Tcp.Dispose();
    }

    static async Task<PoseRelay> Relay(IRelationsSource? relations = null, PoseRelay.Limits? limits = null, bool gestures = true, List<string>? log = null, IAccountSettingsStore? accounts = null)
    {
        var r = new PoseRelay(0, limits) { Relations = relations, GesturesEnabled = gestures, AccountSettings = accounts }; // (client wave 18 merge: + the accounts' proofs)
        if (log != null) r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        await Task.Yield();
        return r;
    }

    /// <summary>Client wave 20 (w20pen): the relay has the two players of these tests (AIDs 2000001 and 2000002) on prontera. It replaces a wait for
    /// two forwarded poses: each connection is read on its own, so the second pose of one client could be handled before the other's
    /// first, and only one pose was ever forwarded (seen once in a full run beside DrawTests: the wait ran out).</summary>
    static bool BothOn(PoseRelay r) { var s = r.Snapshot(); return s.Any(p => p.aid == 2000001 && p.map == "prontera") && s.Any(p => p.aid == 2000002 && p.map == "prontera"); }

    static async Task Until(Func<bool> cond, int ms = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task Welcome_says_the_relay_forwards_menus_and_a_version_1_welcome_is_unchanged()
    {
        await using var r = await Relay();
        using var v2 = new Client(r.Port).Hello(2000001, "Tester");
        Assert.NotEqual(0, v2.Caps & GestureWire.CapGesture);
        using var v1 = new Client(r.Port);
        v1.Send(PresenceWire.EncodeHello(2000002, "Old", PresenceWire.Version));
        var w = v1.Next(2000)!;
        Assert.Equal(PresenceWire.EncodeWelcome()[2..], w); // byte for byte what a client of wave 3 always got
        await using var off = await Relay(gestures: false);
        using var c = new Client(off.Port).Hello(2000003, "Tester");
        Assert.Equal(0, c.Caps & GestureWire.CapGesture);
    }

    [Fact]
    public async Task A_menu_shown_to_everyone_reaches_the_new_clients_of_its_map_and_no_older_one()
    {
        await using var r = await Relay();
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester").Ready();          // wave 18, same map
        using var old2 = new Client(r.Port).Hello(2000003, "Before wave 18");              // version 2, never READY
        using var old1 = new Client(r.Port).Hello(2000004, "Wave 3", PresenceWire.Version); // version 1
        using var far = new Client(r.Port).Hello(2000005, "Elsewhere").Ready();           // wave 18, another map
        foreach (var c in new[] { b, old2, old1 }) c.Pose("prontera");
        far.Pose("geffen");
        // a menu before the sender's first pose: the relay does not know its map yet: forwarded to nobody
        a.Send(GestureWire.Encode(Pad())!);
        await Until(() => r.GesturesIn == 1);
        Assert.Equal(1, r.GesturesDropped);
        a.Pose("prontera");
        await Until(() => r.Forwarded >= 3);
        foreach (var c in new[] { b, old2, old1, far }) c.Drain(200);
        var frame = GestureWire.Encode(Pad())!;
        a.Send(frame);
        var got = b.Drain(500).menus;
        Assert.Single(got);
        Assert.Equal(2000001u, got[0].aid);
        Assert.Equal(frame[3..], got[0].body); // the body as it was sent
        var st = new GestureWire.State();
        Assert.True(GestureWire.TryDecode(got[0].body, 0, got[0].body.Length, st));
        Assert.Equal(GestureWire.KindPad, st.Kind);
        Assert.Empty(old2.Drain(300).menus); // (its reader would take it, but it would draw nothing: it is never sent one)
        Assert.Empty(old1.Drain(300).menus); // (a version-1 reader closes on unknown long frames: never sent one)
        Assert.Empty(far.Drain(300).menus);
        Assert.Empty(a.Drain(200).menus);    // never back to the sender
        Assert.Equal((1L, 0L), (r.GesturesForwarded, r.GesturesWithheld));
        // the older clients still get the poses as before
        a.Pose("prontera");
        Assert.Equal(PresenceWire.PoseOf, old1.Next(1000)![0]);
        Assert.Equal(PresenceWire.PoseOf, old2.Next(1000)![0]);
    }

    [Fact]
    public async Task An_older_relay_closes_a_connection_that_sends_a_menu_so_a_client_sends_none_without_the_bit()
    {
        // a relay that does not forward menus (here: switched off; a relay before wave 18 the same) answers without the bit, and treats
        // a GESTURE as the unknown message it is: the connection closes. The client (Net/PresenceRelayChannel.cs) sends only after the bit.
        await using var r = await Relay(gestures: false);
        using var a = new Client(r.Port).Hello(2000001, "Tester");
        Assert.Equal(0, a.Caps & GestureWire.CapGesture);
        a.Pose("prontera");
        a.Send(GestureWire.Encode(Pad())!);
        Assert.Throws<EndOfStreamException>(() => a.Drain(1500));
    }

    /// <summary>A table of relations (a test's rAthena), changeable while the relay runs.</summary>
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

    /// <summary>Client wave 18 merge: a settings store whose logins' tokens are "token-AID" (the proof of a login of each account).</summary>
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
    public async Task Friends_family_and_guild_combine_and_whoever_is_left_out_is_never_sent_the_menu()
    {
        // the sender (150000) and five players on its map: a friend, its spouse, a guild mate, a stranger, an adopted child (client wave 18 merge:
        // design note, "I meant my Guild": the third relation is the guild)
        var t = new Table();
        t.Set(2000001, Char(150000, partner: 150002, guild: 3, friends: 150001));
        t.Set(2000002, Char(150001));                      // on the sender's friend list (one list is enough)
        t.Set(2000003, Char(150002, partner: 150000));     // the spouse
        t.Set(2000004, Char(150003, guild: 3));             // the same guild
        t.Set(2000005, Char(150004, guild: 4, friends: 150099)); // nobody to the sender
        t.Set(2000006, Char(150005, father: 150000));      // the sender's adopted child (set on the child's row only)
        var log = new List<string>();
        // (client wave 18 merge: restricted menus travel only between accounts proved by their login's token: every client here proves its own)
        await using var r = await Relay(t, log: log, accounts: Logins(2000001, 2000002, 2000003, 2000004, 2000005, 2000006));
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready();
        var names = new[] { "Friend", "Spouse", "Guildmate", "Stranger", "Child" };
        var v = names.Select((n, i) => new Client(r.Port).Hello(2000002 + (uint)i, n).Proved(Token(2000002 + (uint)i)).Ready()).ToArray();
        try
        {
            s.Pose("prontera"); foreach (var c in v) c.Pose("prontera");
            await Until(() => t.Loads >= 6 && r.Players().Count == 6);
            await Task.Delay(100);
            foreach (var c in v.Append(s)) c.Drain(150);
            int[] Seen(byte audience)
            {
                s.Send(GestureWire.Encode(Pad(audience))!);
                return v.Select(c => c.Drain(250).menus.Count).ToArray();
            }
            _out.WriteLine("relations: " + string.Join("; ", log.Where(l => l.Contains("is character"))));
            Assert.Equal(new[] { 1, 1, 1, 1, 1 }, Seen(GestureWire.ToEveryone));
            Assert.Equal(new[] { 1, 0, 0, 0, 0 }, Seen(GestureWire.ToFriends));
            Assert.Equal(new[] { 0, 1, 0, 0, 1 }, Seen(GestureWire.ToFamily));    // spouse and child (either row says it)
            Assert.Equal(new[] { 0, 0, 1, 0, 0 }, Seen(GestureWire.ToGuild));
            Assert.Equal(new[] { 1, 0, 1, 0, 0 }, Seen(GestureWire.ToFriends | GestureWire.ToGuild)); // the design's "and/or": friends + guild, family off
            Assert.Equal(new[] { 1, 1, 1, 0, 1 }, Seen(GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild));
            Assert.Equal(5 + 1 + 2 + 1 + 2 + 4L, r.GesturesForwarded);
            Assert.Equal(0 + 4 + 3 + 4 + 3 + 1L, r.GesturesWithheld);
            // a closed menu goes the same way: whoever saw it open is told it closed; the others hear nothing
            s.Send(GestureWire.Encode(new GestureWire.State { Audience = GestureWire.ToGuild, Menu = GestureWire.MenuRight, Kind = GestureWire.KindClosed })!);
            Assert.Equal(new[] { 0, 0, 1, 0, 0 }, v.Select(c => c.Drain(250).menus.Count).ToArray());
        }
        finally { foreach (var c in v) c.Dispose(); }
    }

    [Fact]
    public async Task Without_relations_a_restricted_menu_goes_to_nobody_and_new_relations_are_read_again()
    {
        // no source: only "Everyone" travels
        await using (var r0 = await Relay())
        {
            using var s0 = new Client(r0.Port).Hello(2000001, "Sender").Ready(); using var f0 = new Client(r0.Port).Hello(2000002, "Friend").Ready();
            s0.Pose("prontera"); f0.Pose("prontera"); s0.Pose("prontera"); await Until(() => BothOn(r0)); f0.Drain(150);
            s0.Send(GestureWire.Encode(Pad(GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild))!);
            Assert.Empty(f0.Drain(300).menus);
            s0.Send(GestureWire.Encode(Pad())!);
            Assert.Single(f0.Drain(300).menus);
        }
        // a stranger becomes a friend (rAthena saved it): within the refresh time the relay reads it and the menu reaches them
        var t = new Table();
        t.Set(2000001, Char(150000)); t.Set(2000002, Char(150001));
        await using var r = await Relay(t, new PoseRelay.Limits { RelationsRefresh = TimeSpan.FromMilliseconds(300) }, accounts: Logins(2000001, 2000002, 2000009)); // (client wave 18 merge: proved accounts)
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready(); using var f = new Client(r.Port).Hello(2000002, "Soon a friend").Proved(Token(2000002)).Ready();
        s.Pose("prontera"); f.Pose("prontera"); s.Pose("prontera"); await Until(() => t.Loads >= 2 && BothOn(r)); await Task.Delay(50); f.Drain(150);
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!);
        Assert.Empty(f.Drain(300).menus);
        t.Set(2000001, Char(150000, friends: 150001));
        await Task.Delay(400);
        int loads0 = t.Loads; // (client wave 18 merge: counted from here: under load an earlier menu may already have had them read, before the change)
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!); // (this one finds them old and has them read again)
        await Until(() => t.Loads >= loads0 + 2);
        await Task.Delay(100);
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!);
        Assert.NotEmpty(f.Drain(500).menus);
        // a player the database does not know (no such character): nothing restricted to or from them
        int loads1 = t.Loads;
        using var ghost = new Client(r.Port).Hello(2000009, "Nobody known").Proved(Token(2000009)).Ready(); // (proved: only its unknown character keeps it out)
        ghost.Pose("prontera"); await Until(() => t.Loads >= loads1 + 1); await Task.Delay(100); ghost.Drain(200);
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild))!);
        Assert.Empty(ghost.Drain(300).menus);
    }

    /// <summary>Client wave 18 merge (the orchestrator): the privacy filter trusts no account a client only CLAIMS in its HELLO. A restricted
    /// menu goes only from a proved account to a proved account (w18acct's proof: the login's web token, checked by the settings store);
    /// a client claiming a friend's account without that token is sent "Everyone" menus only, and its own restricted menus reach nobody.</summary>
    [Fact]
    public async Task A_client_claiming_a_friends_account_without_its_token_receives_nothing_restricted()
    {
        var t = new Table();
        t.Set(2000001, Char(150000, partner: 150002, guild: 3, friends: 150001));            // the sender: a friend, a spouse, a guild
        t.Set(2000002, Char(150001));                                                       // its friend
        t.Set(2000003, Char(150002, partner: 150000, guild: 3, friends: 150000));            // its spouse, in its guild, on its friend list
        await using var r = await Relay(t, accounts: Logins(2000001, 2000002, 2000003));
        const byte All = GestureWire.ToFriends | GestureWire.ToFamily | GestureWire.ToGuild;
        using var s = new Client(r.Port).Hello(2000001, "Sender").Proved(Token(2000001)).Ready();
        // a stranger claims the friend's account: no token at all, then a wrong one (refused)
        using var spoof = new Client(r.Port).Hello(2000002, "Friend").Ready();
        s.Pose("prontera"); spoof.Pose("prontera"); s.Pose("prontera");
        await Until(() => t.Loads >= 2 && BothOn(r)); await Task.Delay(100);
        spoof.Drain(150); s.Drain(150);
        foreach (var aud in new[] { GestureWire.ToFriends, All }) { s.Send(GestureWire.Encode(Pad(aud))!); Assert.Empty(spoof.Drain(300).menus); }
        Assert.Equal(PresenceWire.SettingsRefused, spoof.Prove("token-guessed"));
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!);
        Assert.Empty(spoof.Drain(300).menus);
        Assert.Equal(3L, r.GesturesWithheld);
        // "Everyone" still reaches it (as any player on the map)
        s.Send(GestureWire.Encode(Pad())!);
        Assert.Single(spoof.Drain(300).menus);
        // its own restricted menus reach nobody: neither the sender (whose friend it claims to be) nor the sender's proved spouse
        using var spouse = new Client(r.Port).Hello(2000003, "Spouse").Proved(Token(2000003)).Ready();
        spouse.Pose("prontera"); await Until(() => t.Loads >= 3); await Task.Delay(100); spouse.Drain(150); spoof.Drain(150); s.Drain(150);
        spoof.Send(GestureWire.Encode(Pad(All))!);
        Assert.Empty(spouse.Drain(300).menus);
        Assert.Empty(s.Drain(200).menus);
        // the same restricted menus between proved accounts do travel: the spouse sees the sender's family menu, the sender the spouse's friends' one
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFamily))!);
        Assert.Single(spouse.Drain(300).menus);
        spouse.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!);
        Assert.Single(s.Drain(300).menus);
        // the real friend logs in with its token (taking the claimed connection over): from then on it is sent the friends' menu
        using var friend = new Client(r.Port).Hello(2000002, "Friend").Proved(Token(2000002)).Ready();
        friend.Pose("prontera"); await Task.Delay(150); friend.Drain(150);
        s.Send(GestureWire.Encode(Pad(GestureWire.ToFriends))!);
        Assert.Single(friend.Drain(300).menus);
        _out.WriteLine(r.GestureStatsLine());
        // a relay that verifies nobody (no settings store) sends restricted menus to nobody, even with every relation known
        await using var r2 = await Relay(t);
        using var s2 = new Client(r2.Port).Hello(2000001, "Sender").Ready(); using var f2 = new Client(r2.Port).Hello(2000002, "Friend").Ready();
        s2.Pose("prontera"); f2.Pose("prontera"); s2.Pose("prontera"); await Until(() => BothOn(r2)); await Task.Delay(100); f2.Drain(150);
        s2.Send(GestureWire.Encode(Pad(All))!);
        Assert.Empty(f2.Drain(300).menus);
        s2.Send(GestureWire.Encode(Pad())!);
        Assert.Single(f2.Drain(300).menus);
    }

    [Fact]
    public async Task A_flood_or_a_malformed_menu_is_dropped_and_the_connection_stays()
    {
        await using var r = await Relay(limits: new PoseRelay.Limits { MaxGesturesPerSecond = 10 });
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Viewer").Ready();
        a.Pose("prontera"); b.Pose("prontera"); a.Pose("prontera"); await Until(() => BothOn(r)); b.Drain(150);
        var frame = GestureWire.Encode(Pad())!;
        for (int i = 0; i < 30; i++) a.Send(frame);
        var got = b.Drain(600).menus.Count;
        Assert.InRange(got, 10, 20); // (a second's window may start inside the burst)
        Assert.True(r.GesturesDropped >= 10);
        // malformed: no audience; too short
        var bad = (byte[])frame.Clone(); bad[4] = 0; a.Send(bad);
        a.Send(new byte[] { 3, 0, GestureWire.Gesture, 1, 1 });
        await Task.Delay(300);
        a.Pose("prontera");
        Assert.Equal(PresenceWire.PoseOf, b.Next(1000)![0]); // still connected, poses go on
    }

    // ------------------------------------------------------------------------------------------------ rAthena's tables

    static string? RathenaMainSql()
    {
        foreach (var d in new[] { Environment.GetEnvironmentVariable("LOKIHOST_TEST_RATHENA"), @"C:\rAthena" })
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "sql-files", "main.sql"))) return Path.Combine(d, "sql-files", "main.sql");
        return null;
    }

    /// <summary>RelationsDb on rAthena's own `char` and `friends` tables (their CREATE TABLE statements read from rAthena's
    /// sql-files/main.sql), in a private MariaDB in a temporary folder on a free port. Runs when MariaDB's programs and an rAthena
    /// checkout are found (LOKIHOST_TEST_MARIADB_BIN / PrivateDb.FindBin, LOKIHOST_TEST_RATHENA); otherwise passes doing nothing.</summary>
    [Fact]
    public async Task RelationsDb_reads_friends_family_and_guild_from_rathenas_own_tables()
    {
        var bin = PrivateDb.FindBin(Environment.GetEnvironmentVariable("LOKIHOST_TEST_MARIADB_BIN"));
        var mainSql = RathenaMainSql();
        if (bin == null || mainSql == null) { _out.WriteLine("MariaDB's programs or rAthena's main.sql not found: skipped"); return; }
        var sql = File.ReadAllText(mainSql);
        string Create(string table) => Regex.Match(sql, @"CREATE TABLE IF NOT EXISTS `" + table + @"` \(.*?\) ENGINE=\w+[^;]*;", RegexOptions.Singleline).Value;
        Assert.NotEqual("", Create("char")); Assert.NotEqual("", Create("friends"));
        var root = Path.Combine(Path.GetTempPath(), "lokihost-w18share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var s = new HostSettings { HostDir = Path.Combine(root, "rathena"), FilePath = Path.Combine(root, "lokihost.json"), PrivateDb = true,
            PrivateDbDir = Path.Combine(root, "mariadb"), PrivateDbPort = await PrivateDb.FreePortAsync(17341), MariaDbBin = bin };
        void Log(string l) => _out.WriteLine(l);
        var db = new PrivateDb(s);
        db.Init(Log);
        await db.StartAsync(Log);
        try
        {
            await using (var c = new MySqlConnector.MySqlConnection(db.RootConnStr()))
            {
                await c.OpenAsync();
                async Task Run(string q) { await using var cmd = new MySqlConnector.MySqlCommand(q, c); await cmd.ExecuteNonQueryAsync(); }
                await Run("CREATE DATABASE w18share"); await Run("USE w18share");
                await Run(Create("char")); await Run(Create("friends"));
                // Tester (150000) married to Mae (150002), in guild 3 (and Royal Clan 1); Lou (150001) asked Tester and was accepted (on Lou's list
                // only), in Royal Clan 1 but no guild; Kid (150003) adopted by Tester and Mae; Tess (another character of Tester's account, offline);
                // Zed online, no relation but guild 3 (client wave 18 merge: the guild, `char`.guild_id; a Royal Clan alone no longer counts)
                await Run("INSERT INTO `char` (char_id, account_id, char_num, name, partner_id, father, mother, child, guild_id, clan_id, online) VALUES " +
                          "(150000, 2000001, 0, 'Tester', 150002, 0, 0, 150003, 3, 1, 1), (150001, 2000002, 0, 'Lou', 0, 0, 0, 0, 0, 1, 1), " +
                          "(150002, 2000003, 0, 'Mae', 150000, 0, 0, 150003, 3, 0, 1), (150003, 2000004, 0, 'Kid', 0, 150000, 150002, 0, 0, 0, 1), " +
                          "(150004, 2000001, 1, 'Tess', 0, 0, 0, 0, 0, 0, 0), (150005, 2000005, 0, 'Zed', 0, 0, 0, 0, 3, 0, 1)");
                await Run("INSERT INTO `friends` (char_id, friend_id) VALUES (150001, 150000), (150005, 150099)");
            }
            var src = new RelationsDb(db.RootConnStr("w18share"));
            var tester = (await src.LoadAsync(2000001, "Tester"))!;
            Assert.Equal((150000u, 150002u, 150003u, 3u), (tester.CharId, tester.Partner, tester.Child, tester.Guild));
            Assert.Contains(150001u, tester.Friends); // from Lou's list
            var lou = (await src.LoadAsync(2000002, "Lou"))!; var mae = (await src.LoadAsync(2000003, "Mae"))!;
            var kid = (await src.LoadAsync(2000004, "Kid"))!; var zed = (await src.LoadAsync(2000005, "Zed"))!;
            Assert.True(PoseRelay.Allowed(tester, lou, GestureWire.ToFriends));
            Assert.False(PoseRelay.Allowed(tester, mae, GestureWire.ToFriends));
            Assert.True(PoseRelay.Allowed(tester, mae, GestureWire.ToFamily) && PoseRelay.Allowed(tester, kid, GestureWire.ToFamily) && PoseRelay.Allowed(kid, mae, GestureWire.ToFamily));
            Assert.False(PoseRelay.Allowed(tester, zed, GestureWire.ToFriends | GestureWire.ToFamily));
            Assert.True(PoseRelay.Allowed(tester, zed, GestureWire.ToGuild));
            Assert.False(PoseRelay.Allowed(tester, lou, GestureWire.ToFamily | GestureWire.ToGuild)); // (the same Royal Clan, no guild: not a guild mate)
            // the account's other character, by its name; a name the database spells otherwise: the account's online character
            Assert.Equal(150004u, (await src.LoadAsync(2000001, "Tess"))!.CharId);
            Assert.Equal(150000u, (await src.LoadAsync(2000001, "Somebody Else"))!.CharId);
            Assert.Null(await src.LoadAsync(2000099, "Nobody"));
        }
        finally { await db.StopAsync(Log); }
    }
}
