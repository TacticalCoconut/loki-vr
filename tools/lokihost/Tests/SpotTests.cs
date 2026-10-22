using System.Net;
using System.Net.Sockets;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Free movement (design note 2026-10-14: "exact sub-tile position sync across players"): the players' exact spots
/// on the relay. The client's wire file (SpotWire.cs, compiled into Core); the relay with raw TCP clients of every age (a version-1 client, a
/// version-2 client from before the spots, a new client); the map and range rule; what an older relay does with a SPOT.
/// </summary>
public class SpotTests
{
    readonly ITestOutputHelper _out;
    public SpotTests(ITestOutputHelper o) { _out = o; }

    static SpotWire.State S(float x, float y, string map = "prontera", ushort seq = 1, float vx = 0f, float vy = 0f, byte facing = 2, byte flags = 0) =>
        new() { Map = map, Seq = seq, X = x, Y = y, Vx = vx, Vy = vy, Facing = facing, Flags = flags };

    // ------------------------------------------------------------------------------------------------ the wire

    [Fact]
    public void A_spot_comes_back_as_sent_and_what_cannot_be_a_spot_never_travels()
    {
        var f = SpotWire.Encode(S(150.37f, 98.62f, "1@tower", 65535, -6.67f, 3.21f, 5, SpotWire.FlagMoving))!;
        Assert.Equal(f.Length - 2, f[0] | f[1] << 8);
        Assert.Equal(SpotWire.Spot, f[2]);
        Assert.True(SpotWire.TryReadSpot(f, 2, f.Length - 2, out var s));
        Assert.Equal(("1@tower", (ushort)65535, 150.37f, 98.62f, (byte)5, true), (s.Map, s.Seq, s.X, s.Y, s.Facing, s.Moving));
        Assert.InRange(Math.Abs(s.Vx + 6.67f), 0f, 0.006f); Assert.InRange(Math.Abs(s.Vy - 3.21f), 0f, 0.006f);
        // the longest: a 15-letter map; its SPOT_OF far under a version-1 reader's 200 bytes
        var lng = SpotWire.Encode(S(1f, 2f, new string('a', PresenceWire.MaxMapBytes)))!;
        Assert.Equal(1 + SpotWire.MaxBody, lng.Length - 2);
        var of = SpotWire.EncodeSpotOf(2000001, lng, 2, lng.Length - 2);
        Assert.True(of.Length - 2 <= 38 && of.Length - 2 <= PresenceWire.MaxPayload);
        Assert.True(SpotWire.TryReadSpotOf(of, 2, of.Length - 2, out var aid, out var s2));
        Assert.Equal((2000001u, 1f, 2f), (aid, s2.X, s2.Y));
        // a speed beyond the field's range is cut, never wrapped
        Assert.True(SpotWire.TryReadSpot(SpotWire.Encode(S(1f, 1f, vx: 1000f))!, 2, SpotWire.Encode(S(1f, 1f, vx: 1000f))!.Length - 2, out var fast));
        Assert.InRange(fast.Vx, 327f, 328f);
        // what never travels: no map, a map too long or with a space, a place that is no number or beyond any map
        Assert.Null(SpotWire.Encode(S(1f, 1f, "")));
        Assert.Null(SpotWire.Encode(S(1f, 1f, new string('a', 16))));
        Assert.Null(SpotWire.Encode(S(1f, 1f, "pron tera")));
        Assert.Null(SpotWire.Encode(S(float.NaN, 1f)));
        Assert.Null(SpotWire.Encode(S(1f, float.PositiveInfinity)));
        Assert.Null(SpotWire.Encode(S(5000f, 1f)));
    }

    [Fact]
    public void A_body_that_is_no_spot_is_refused()
    {
        var good = SpotWire.Encode(S(150.5f, 98.25f))![3..]; // the body, from its format byte
        bool Ok(byte[] b) => SpotWire.TryDecodeBody(b, 0, b.Length, out _);
        Assert.True(Ok(good));
        var x = (byte[])good.Clone(); x[0] = 0; Assert.False(Ok(x));                                          // format 0
        x = (byte[])good.Clone(); x[1] = 0; Assert.False(Ok(x));                                              // no map
        x = (byte[])good.Clone(); x[2] = (byte)' '; Assert.False(Ok(x));                                       // a bad map letter
        x = (byte[])good.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(x, 12); Assert.False(Ok(x));          // x is no number
        x = (byte[])good.Clone(); BitConverter.GetBytes(1e7f).CopyTo(x, 16); Assert.False(Ok(x));              // y beyond any map
        x = (byte[])good.Clone(); x[^2] = 8; Assert.False(Ok(x));                                              // facing 8
        Assert.False(Ok(good.Concat(new byte[] { 0 }).ToArray()));                                            // format 1 with a byte left over
        Assert.False(Ok(good[..^1]));                                                                          // one missing
        x = good.Concat(new byte[] { 9, 9 }).ToArray(); x[0] = 2; Assert.True(Ok(x));                         // a later format may add fields after these
        var f = SpotWire.Encode(S(1f, 1f))!; f[2] = SpotWire.SpotReady;
        Assert.False(SpotWire.TryReadSpot(f, 2, f.Length - 2, out _));                                       // not a SPOT
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client: version 1 (before voice), version 2 from before the spots (never SPOT_READY), or a new one.</summary>
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
        public Client Ready() { Send(SpotWire.EncodeReady()); return this; }
        public void Spot(float x, float y, string map = "prontera") => Send(SpotWire.Encode(S(x, y, map))!);
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
        /// <summary>Everything that arrives within <paramref name="ms"/>: the SPOT_OF frames (AID, spot) and how many other frames.</summary>
        public (List<(uint aid, SpotWire.State s)> spots, int others) Drain(int ms)
        {
            var spots = new List<(uint, SpotWire.State)>(); int others = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                var p = Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (p == null) break;
                if (p[0] == SpotWire.SpotOf) { Assert.True(SpotWire.TryReadSpotOf(p, 0, p.Length, out var aid, out var s)); spots.Add((aid, s)); }
                else others++;
            }
            return (spots, others);
        }
        public void Dispose() => Tcp.Dispose();
    }

    static async Task<PoseRelay> Relay(bool spots = true, PoseRelay.Limits? limits = null, List<string>? log = null)
    {
        var r = new PoseRelay(0, limits) { SpotsEnabled = spots, GesturesEnabled = true, DrawingsEnabled = true };
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

    [Fact]
    public async Task Welcome_says_the_relay_forwards_spots_and_an_older_relay_closes_a_connection_that_sends_one()
    {
        await using var r = await Relay();
        using var v2 = new Client(r.Port).Hello(2000001, "Tester");
        Assert.NotEqual(0, v2.Caps & SpotWire.CapSpot);
        Assert.Equal(0, SpotWire.CapSpot & (PresenceWire.CapVoice | PresenceWire.CapData | Loki.Horde.Net.HordeWire.CapHorde | PresenceWire.CapSettings | GestureWire.CapGesture | DrawWire.CapDraw));
        // no message id is shared with an older feature
        var older = new byte[] { PresenceWire.Hello, PresenceWire.Pose, PresenceWire.Leave, PresenceWire.Ping, PresenceWire.Voice, PresenceWire.VoiceStop, PresenceWire.Data, PresenceWire.SettingsAuth, PresenceWire.SettingsPut,
            GestureWire.Gesture, GestureWire.GestureReady, DrawWire.Draw, DrawWire.DrawReady, PresenceWire.Welcome, PresenceWire.PoseOf, PresenceWire.Gone, PresenceWire.VoiceOf, PresenceWire.VoiceEnd, PresenceWire.DataHost,
            PresenceWire.SettingsOf, PresenceWire.SettingsAck, GestureWire.GestureOf, DrawWire.DrawOf };
        foreach (var id in new[] { SpotWire.Spot, SpotWire.SpotReady, SpotWire.SpotOf })
        {
            Assert.DoesNotContain(id, older);
            Assert.False(id >= Loki.Horde.Net.HordeWire.ClientFirst && id <= Loki.Horde.Net.HordeWire.ClientLast || id >= Loki.Horde.Net.HordeWire.RelayFirst && id <= Loki.Horde.Net.HordeWire.RelayLast);
        }
        using var v1 = new Client(r.Port);
        v1.Send(PresenceWire.EncodeHello(2000002, "Old", PresenceWire.Version));
        Assert.Equal(PresenceWire.EncodeWelcome()[2..], v1.Next(2000)!); // byte for byte what a client of wave 3 always got
        // a relay that does not forward spots (switched off; any relay before them the same): no bit, and a SPOT is the unknown message it is:
        // the connection closes. The client (Net/PresenceRelayChannel.cs) sends only after the bit.
        await using var off = await Relay(spots: false);
        using var c = new Client(off.Port).Hello(2000003, "Tester");
        Assert.Equal(0, c.Caps & SpotWire.CapSpot);
        c.Pose("prontera");
        c.Spot(150.3f, 98.7f);
        Assert.Throws<EndOfStreamException>(() => c.Drain(1500));
    }

    [Fact]
    public async Task A_spot_reaches_the_new_clients_on_its_map_within_range_and_no_older_one()
    {
        var log = new List<string>();
        await using var r = await Relay(log: log);
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester").Ready();          // new, same map, near
        using var old2 = new Client(r.Port).Hello(2000003, "Before spots");                // version 2, never READY
        using var old1 = new Client(r.Port).Hello(2000004, "Wave 3", PresenceWire.Version); // version 1
        using var away = new Client(r.Port).Hello(2000005, "Far away").Ready();           // new, same map, 40 cells off
        using var other = new Client(r.Port).Hello(2000006, "Elsewhere").Ready();         // new, another map
        using var quiet = new Client(r.Port).Hello(2000007, "No spot yet").Ready();       // new, READY, no spot of its own yet
        foreach (var c in new[] { old2, old1 }) c.Pose("prontera");
        b.Spot(152f, 99f); away.Spot(150f, 140f); other.Spot(150f, 98f, "geffen");
        await Until(() => r.SpotsIn == 3);
        foreach (var c in new[] { a, b, old2, old1, away, other, quiet }) c.Drain(150);
        a.Spot(150.37f, 98.62f);
        await Until(() => r.SpotsIn == 4);
        var gotB = b.Drain(400);
        Assert.Single(gotB.spots);
        Assert.Equal((2000001u, "prontera", 150.37f, 98.62f), (gotB.spots[0].aid, gotB.spots[0].s.Map, gotB.spots[0].s.X, gotB.spots[0].s.Y));
        foreach (var c in new[] { old2, old1, away, other, quiet }) Assert.Empty(c.Drain(200).spots);
        Assert.Empty(a.Drain(200).spots); // never back to the sender
        Assert.Equal(1, r.SpotsForwarded);
        Assert.Equal(2, r.SpotsFar); // "Far away" and "Smoke Tester" 41 cells apart when they placed themselves, then "Far away" 41.4 cells from the sender: beyond 32
        // the far one comes within range: from its next spot on, both see each other
        away.Spot(151f, 120f);
        await Until(() => r.SpotsIn == 5);
        Assert.Single(a.Drain(300).spots); Assert.Single(b.Drain(300).spots);
        a.Spot(150.5f, 98.5f);
        await Until(() => r.SpotsIn == 6);
        Assert.Single(away.Drain(300).spots); Assert.Single(b.Drain(300).spots);
        // a spot on another map moves the sender there: the first map's players are sent no more
        a.Spot(150f, 98.5f, "geffen");
        await Until(() => r.SpotsIn == 7);
        Assert.Single(other.Drain(300).spots);
        Assert.Empty(b.Drain(200).spots);
        lock (log) Assert.Contains(log, l => l.Contains("Tester (AID 2000001) sends its exact spot on geffen"));
        _out.WriteLine(r.SpotStatsLine());
    }

    [Fact]
    public async Task A_bad_or_too_frequent_spot_is_dropped_and_the_connection_stays()
    {
        await using var r = await Relay(limits: new PoseRelay.Limits { MaxSpotsPerSecond = 5 });
        using var a = new Client(r.Port).Hello(2000001, "Tester").Ready();
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester").Ready();
        b.Spot(150f, 100f);
        await Until(() => r.SpotsIn == 1);
        var bad = SpotWire.Encode(S(1f, 1f))!; bad[^2] = 9; // facing 9
        a.Send(bad);
        await Until(() => r.SpotsDropped == 1);
        for (int i = 0; i < 12; i++) a.Spot(150f + i * 0.1f, 100f);
        await Until(() => r.SpotsIn == 1 + 5 && r.SpotsDropped == 1 + 7);
        Assert.Equal(5, b.Drain(400).spots.Count);
        // the connection is alive: a spot a second later is forwarded again
        await Task.Delay(1050);
        a.Spot(151f, 100f);
        await Until(() => r.SpotsIn == 7);
        Assert.Single(b.Drain(300).spots);
    }
}
