using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Loki.Net;
using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// The headset pose relay (w3presence): the client's wire format (client/Assets/Loki/Runtime/Net/PresenceWire.cs, the same
/// file the client compiles) and PoseRelay with raw TCP clients on this machine.
/// </summary>
public class PresenceTests
{
    // ------------------------------------------------------------------------------------------------ wire format

    static PresenceWire.PoseBody SamplePose(string map = "prontera", float x = 150.5f)
    {
        var p = new PresenceWire.PoseBody { Map = map, Seq = 7, MetresPerCell = 0.032f, Flags = PresenceWire.FlagLeft | PresenceWire.FlagRight };
        float[] v = { x, 20f, 181.5f, 0.1f, 0.2f, 0.3f, 0.927362f, x - 4, 12f, 180f, 0, 0, 0, 1, x + 4, 12f, 180f, 0, 0.7071068f, 0, 0.7071068f };
        v.CopyTo(p.V, 0);
        return p;
    }

    static byte[] Payload(byte[] frame)
    {
        Assert.Equal(frame.Length - 2, frame[0] | frame[1] << 8);
        return frame[2..];
    }

    [Fact]
    public void Hello_pose_and_forward_round_trip()
    {
        var h = Payload(PresenceWire.EncodeHello(2000001, "Tester"));
        Assert.True(PresenceWire.TryDecodeHello(h, 0, h.Length, out var aid, out var nameUtf8, out var name));
        Assert.Equal((2000001u, "Tester"), (aid, name));

        var pose = Payload(PresenceWire.EncodePose(SamplePose()));
        Assert.True(pose.Length <= PresenceWire.MaxPayload);
        var back = new PresenceWire.PoseBody();
        Assert.True(PresenceWire.TryDecodePose(pose, 0, pose.Length, back));
        Assert.Equal("prontera", back.Map);
        Assert.Equal(7, back.Seq);
        Assert.Equal(0.032f, back.MetresPerCell);
        Assert.Equal(SamplePose().V, back.V);

        // the relay forwards the body as received, with the AID and name of the HELLO
        var fwd = Payload(PresenceWire.EncodePoseOf(aid, nameUtf8, pose, 0, pose.Length));
        var got = new PresenceWire.PoseBody();
        Assert.True(PresenceWire.TryDecodePoseOf(fwd, 0, fwd.Length, out var faid, out var fname, got));
        Assert.Equal((2000001u, "Tester"), (faid, fname));
        Assert.Equal(SamplePose().V, got.V);
        Assert.True(fwd.Length <= PresenceWire.MaxPayload);

        var g = Payload(PresenceWire.EncodeGone(42));
        Assert.True(PresenceWire.TryDecodeGone(g, 0, g.Length, out var gone) && gone == 42);
        var w = Payload(PresenceWire.EncodeWelcome());
        Assert.True(PresenceWire.TryDecodeWelcome(w, 0, w.Length, out var ver) && ver == PresenceWire.Version);
    }

    [Fact]
    public void The_longest_name_and_map_still_fit_one_frame()
    {
        var name = new string('é', 40); // 80 UTF-8 bytes: cut to 48 without splitting a character
        var n = PresenceWire.NameBytes(name);
        Assert.Equal(48, n.Length);
        Assert.Equal(new string('é', 24), Encoding.UTF8.GetString(n));
        var pose = Payload(PresenceWire.EncodePose(SamplePose("0001@tower_abc")));
        var fwd = PresenceWire.EncodePoseOf(1, n, pose, 0, pose.Length);
        Assert.True(fwd.Length - 2 <= PresenceWire.MaxPayload);
        Assert.Equal("Smoke Tester", Encoding.UTF8.GetString(PresenceWire.NameBytes("Smoke\u0001 Tester")));
    }

    [Fact]
    public void Malformed_messages_are_refused()
    {
        var body = new PresenceWire.PoseBody();
        byte[] P(PresenceWire.PoseBody p) => Payload(PresenceWire.EncodePose(p));
        var nan = SamplePose(); nan.V[4] = float.NaN;
        Assert.False(PresenceWire.TryDecodePose(P(nan), 0, P(nan).Length, body));
        var far = SamplePose(); far.V[0] = 1e7f;
        Assert.False(PresenceWire.TryDecodePose(P(far), 0, P(far).Length, body));
        var zeroQ = SamplePose(); for (int i = 3; i < 7; i++) zeroQ.V[i] = 0;
        Assert.False(PresenceWire.TryDecodePose(P(zeroQ), 0, P(zeroQ).Length, body));
        var scale = SamplePose(); scale.MetresPerCell = 0;
        Assert.False(PresenceWire.TryDecodePose(P(scale), 0, P(scale).Length, body));
        var map = SamplePose("pront era");
        Assert.False(PresenceWire.TryDecodePose(P(map), 0, P(map).Length, body));
        Assert.Throws<InvalidOperationException>(() => PresenceWire.EncodePose(SamplePose(new string('a', 300))));
        var ok = P(SamplePose());
        Assert.False(PresenceWire.TryDecodePose(ok, 0, ok.Length - 1, body)); // truncated
        var longer = ok.Concat(new byte[] { 0 }).ToArray();
        Assert.False(PresenceWire.TryDecodePose(longer, 0, longer.Length, body)); // trailing byte

        var hello = Payload(PresenceWire.EncodeHello(5, "A"));
        Assert.False(PresenceWire.TryDecodeHello(Payload(PresenceWire.EncodeHello(0, "A")), 0, hello.Length, out _, out _, out _)); // AID 0
        var wrongMagic = (byte[])hello.Clone(); wrongMagic[1] = (byte)'X';
        Assert.False(PresenceWire.TryDecodeHello(wrongMagic, 0, wrongMagic.Length, out _, out _, out _));
        var wrongVersion = (byte[])hello.Clone(); wrongVersion[5] = 99;
        Assert.False(PresenceWire.TryDecodeHello(wrongVersion, 0, wrongVersion.Length, out _, out _, out _));
        var badUtf8 = (byte[])hello.Clone(); badUtf8[^1] = 0xff;
        Assert.False(PresenceWire.TryDecodeHello(badUtf8, 0, badUtf8.Length, out _, out _, out _));
    }

    [Fact]
    public void Frames_split_anywhere_are_put_back_together_and_bad_lengths_are_errors()
    {
        var stream = PresenceWire.EncodeHello(9, "Nine").Concat(PresenceWire.EncodePose(SamplePose())).Concat(PresenceWire.EncodePing()).ToArray();
        for (int cut = 1; cut < stream.Length; cut++)
        {
            var fr = new PresenceWire.FrameReader();
            var got = new List<byte>();
            fr.Append(stream, 0, cut);
            while (fr.Next(out var p) == 1) got.Add(p[0]);
            fr.Append(stream, cut, stream.Length - cut);
            while (fr.Next(out var p) == 1) got.Add(p[0]);
            Assert.Equal(new byte[] { PresenceWire.Hello, PresenceWire.Pose, PresenceWire.Ping }, got);
        }
        var bad = new PresenceWire.FrameReader();
        bad.Append(new byte[] { 0x47, 0x45, 0x54, 0x20 }, 0, 4); // "GET " of an HTTP request: length 17735
        Assert.Equal(-1, bad.Next(out _));
        var zero = new PresenceWire.FrameReader();
        zero.Append(new byte[] { 0, 0 }, 0, 2);
        Assert.Equal(-1, zero.Next(out _));
    }

    // ------------------------------------------------------------------------------------------------ the relay

    /// <summary>A raw relay client: TCP, frames in and out.</summary>
    sealed class Client : IDisposable
    {
        public readonly TcpClient Tcp = new() { NoDelay = true };
        readonly PresenceWire.FrameReader _fr = new();
        readonly byte[] _buf = new byte[4096];
        public Client(int port) { Tcp.Connect(IPAddress.Loopback, port); }
        public void Send(byte[] frame) => Tcp.GetStream().Write(frame);
        public Client Hello(uint aid, string name)
        {
            Send(PresenceWire.EncodeHello(aid, name));
            var w = Next(2000) ?? throw new Exception("no WELCOME");
            Assert.True(PresenceWire.TryDecodeWelcome(w, 0, w.Length, out _));
            return this;
        }
        public void Pose(string map, float x = 150.5f) => Send(PresenceWire.EncodePose(SamplePose(map, x)));

        /// <summary>The next payload, or null after <paramref name="ms"/>; throws when the relay closed the connection.</summary>
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

        /// <summary>True when the relay closed this connection within <paramref name="ms"/> (anything it sent first is skipped).</summary>
        public bool ClosedWithin(int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            try { while (DateTime.UtcNow < deadline) Next((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)); }
            catch (Exception e) when (e is EndOfStreamException or IOException) { return true; }
            return false;
        }

        /// <summary>Waits for a POSE_OF (skipping nothing else: any other message fails).</summary>
        public (uint aid, string name, PresenceWire.PoseBody body) PoseOf(int ms = 2000)
        {
            var p = Next(ms) ?? throw new Exception("no POSE_OF");
            var b = new PresenceWire.PoseBody();
            Assert.True(PresenceWire.TryDecodePoseOf(p, 0, p.Length, out var aid, out var name, b), $"not a POSE_OF: 0x{p[0]:x2}");
            return (aid, name, b);
        }

        public uint GoneOf(int ms = 2000)
        {
            var p = Next(ms) ?? throw new Exception("no GONE");
            Assert.True(PresenceWire.TryDecodeGone(p, 0, p.Length, out var aid), $"not a GONE: 0x{p[0]:x2}");
            return aid;
        }

        public void Dispose() => Tcp.Dispose();
    }

    static async Task<PoseRelay> Relay(PoseRelay.Limits? limits = null, List<string>? log = null)
    {
        var r = new PoseRelay(0, limits);
        if (log != null) r.Log += l => { lock (log) log.Add(l); };
        r.Start(IPAddress.Loopback);
        await Task.Yield();
        return r;
    }

    static async Task Until(Func<bool> cond, int ms = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task Poses_reach_the_other_players_on_the_same_map_only_with_the_hellos_aid_and_name()
    {
        await using var r = await Relay();
        using var a = new Client(r.Port).Hello(2000001, "Tester");
        using var b = new Client(r.Port).Hello(2000002, "Smoke Tester");
        using var c = new Client(r.Port).Hello(2000003, "Elsewhere");
        c.Pose("izlude");
        b.Pose("prontera", 160f);
        await Until(() => r.Snapshot().Count(x => x.map != null) == 2);
        a.Pose("prontera", 151.25f);
        var got = b.PoseOf();
        Assert.Equal((2000001u, "Tester", "prontera"), (got.aid, got.name, got.body.Map));
        Assert.Equal(151.25f, got.body.V[0]);
        Assert.Null(c.Next(300)); // another map: nothing
        Assert.Null(a.Next(100)); // never its own pose back
        // b arrived on prontera before a: a sees b from b's next pose on
        b.Pose("prontera", 161f);
        Assert.Equal(2000002u, a.PoseOf().aid);
    }

    [Fact]
    public async Task A_map_change_a_leave_and_a_disconnect_each_send_gone_to_that_maps_players()
    {
        var log = new List<string>();
        await using var r = await Relay(log: log);
        using var a = new Client(r.Port).Hello(11, "A");
        using var b = new Client(r.Port).Hello(12, "B");
        b.Pose("prontera");
        await Until(() => r.Snapshot().Any(x => x.aid == 12 && x.map == "prontera"));
        a.Pose("prontera"); b.PoseOf();
        a.Pose("izlude"); // map change
        Assert.Equal(11u, b.GoneOf());
        a.Pose("prontera"); Assert.Equal(11u, b.PoseOf().aid);
        a.Send(PresenceWire.EncodeLeave());
        Assert.Equal(11u, b.GoneOf());
        a.Pose("prontera"); Assert.Equal(11u, b.PoseOf().aid);
        a.Tcp.Client.LingerState = new LingerOption(true, 0); // a killed client: the OS resets the connection
        a.Tcp.Client.Close();
        Assert.Equal(11u, b.GoneOf());
        await Until(() => r.Snapshot().All(x => x.aid != 11));
        lock (log) Assert.Contains(log, l => l.Contains("A (AID 11) disconnected"));
    }

    [Fact]
    public async Task Garbage_oversized_frames_and_a_missing_hello_close_the_connection()
    {
        await using var r = await Relay(new PoseRelay.Limits { HelloWithin = TimeSpan.FromMilliseconds(400) });
        using (var http = new Client(r.Port)) { http.Send(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n")); Assert.True(http.ClosedWithin(2000)); }
        using (var big = new Client(r.Port)) { big.Send(new byte[] { 0xff, 0x00 }.Concat(new byte[255]).ToArray()); Assert.True(big.ClosedWithin(2000)); }
        using (var poseFirst = new Client(r.Port)) { poseFirst.Pose("prontera"); Assert.True(poseFirst.ClosedWithin(2000)); }
        using (var silent = new Client(r.Port)) Assert.True(silent.ClosedWithin(2000));
        using (var bad = new Client(r.Port).Hello(5, "Bad"))
        {
            var p = SamplePose(); p.V[1] = float.PositiveInfinity;
            bad.Send(PresenceWire.EncodePose(p));
            Assert.True(bad.ClosedWithin(2000));
        }
        using (var unknown = new Client(r.Port).Hello(6, "Unknown")) { unknown.Send(new byte[] { 1, 0, 0x7f }); Assert.True(unknown.ClosedWithin(2000)); }
        await Until(() => r.Snapshot().Count == 0);
    }

    [Fact]
    public async Task A_silent_client_is_closed_and_pings_keep_one_open()
    {
        await using var r = await Relay(new PoseRelay.Limits { IdleAfter = TimeSpan.FromMilliseconds(500) });
        using var quiet = new Client(r.Port).Hello(21, "Quiet");
        using var pinger = new Client(r.Port).Hello(22, "Pinger");
        var t = Task.Run(async () => { for (int i = 0; i < 8; i++) { pinger.Send(PresenceWire.EncodePing()); await Task.Delay(150); } });
        Assert.True(quiet.ClosedWithin(2000));
        await t;
        Assert.Contains(r.Snapshot(), x => x.aid == 22);
    }

    [Fact]
    public async Task Poses_above_the_rate_limit_are_dropped()
    {
        await using var r = await Relay(new PoseRelay.Limits { MaxPosesPerSecond = 5 });
        using var a = new Client(r.Port).Hello(31, "Fast");
        using var b = new Client(r.Port).Hello(32, "B");
        b.Pose("prontera");
        await Until(() => r.Snapshot().Any(x => x.aid == 32 && x.map != null));
        for (int i = 0; i < 20; i++) a.Pose("prontera", 100 + i);
        int got = 0;
        while (b.Next(400) != null) got++;
        Assert.InRange(got, 5, 6); // one window, or the start of a second one
        Assert.True(r.Dropped >= 14);
    }

    [Fact]
    public async Task A_second_connection_with_the_same_aid_replaces_the_first()
    {
        await using var r = await Relay();
        using var first = new Client(r.Port).Hello(41, "Twice");
        using var second = new Client(r.Port).Hello(41, "Twice");
        Assert.True(first.ClosedWithin(2000));
        Assert.Single(r.Snapshot(), x => x.aid == 41);
    }

    [Fact]
    public async Task Connections_are_limited_per_address_and_in_total()
    {
        await using var r = await Relay(new PoseRelay.Limits { MaxPerAddress = 2, MaxClients = 10 });
        using var a = new Client(r.Port);
        using var b = new Client(r.Port);
        await Until(() => r.Snapshot().Count == 0); // not said HELLO yet
        using var c = new Client(r.Port);
        Assert.True(c.ClosedWithin(2000));
    }

    // ------------------------------------------------------------------------------------------------ Loki Host

    [Fact]
    public void Settings_ports_firewall_rule_and_share_line_carry_the_relay()
    {
        var old = JsonSerializer.Deserialize<HostSettings>("{\"LoginPort\":7900,\"CharPort\":7901,\"MapPort\":7902,\"SetUp\":true}")!; // a file from before the relay
        Assert.Equal((7903, true), (old.PresencePort, old.PresenceEnabled));
        Assert.Equal(new[] { 7900, 7901, 7902, 7903 }, old.Ports);
        Assert.Equal("advfirewall firewall add rule name=\"Loki Host (TCP 7900-7903)\" dir=in action=allow protocol=TCP localport=7900,7901,7902,7903", Net.FirewallAddArgs(old));
        Assert.Equal("advfirewall firewall delete rule name=\"Loki Host (TCP 7900-7903)\"", Net.FirewallDeleteArgs(old));
        old.PresenceEnabled = false;
        Assert.Equal(new[] { 7900, 7901, 7902 }, old.Ports);
        Assert.Equal("Loki Host (TCP 7900-7902)", Net.FirewallRuleName(old));
        Assert.Equal("{\"name\":\"Loki\",\"host\":\"203.0.113.7\",\"port\":7900,\"kind\":\"rathena\",\"presencePort\":7903}", Net.LokiJsonEntry("Loki", "203.0.113.7", 7900, 7903));
    }

    [Fact]
    public async Task The_host_starts_and_stops_the_relay_and_reports_it()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        var dir = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "log"));
        var h = new HostController(new HostSettings { HostDir = dir, PresencePort = port });
        var lines = new List<string>();
        h.Output += (n, l) => { lock (lines) lines.Add($"[{n}] {l}"); };
        await h.StartPresenceAsync(_ => { });
        Assert.True(h.Relay is { Running: true });
        Assert.True((await h.StatusAsync(false)).Presence);
        using (var c = new Client(port).Hello(51, "Host test")) { }
        await h.StopAsync(_ => { });
        Assert.Null(h.Relay);
        Assert.False(await Net.ListeningLocallyAsync(port));
        Assert.False((await h.StatusAsync(false)).Presence);
        lock (lines) Assert.Contains(lines, l => l.StartsWith("[presence] Host test (AID 51) connected"));
        Assert.Contains("headset relay listening on port", File.ReadAllText(Path.Combine(dir, "log", "lokihost-presence.log")));

        // a port in use: a warning, no relay, not fatal
        using var busy = new TcpListener(IPAddress.Any, port);
        busy.Start();
        var warn = new List<string>();
        await h.StartPresenceAsync(warn.Add);
        Assert.Null(h.Relay);
        Assert.Contains(warn, w => w.Contains($"port {port} is in use"));
    }
}
