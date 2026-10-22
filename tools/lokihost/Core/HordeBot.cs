using System.Net.Sockets;
using Loki.Horde;
using Loki.Horde.Net;
using Loki.Net;

namespace LokiHost.Core;

/// <summary>
/// A headless Horde player (client wave 8, w8core): one TCP connection to a relay, HELLO 2 as a Loki client says it, and the
/// client's own <see cref="HordeClient"/> on top (the very code the Unity client runs). Its brain is simple and deterministic
/// enough for tests and balance runs: it walks circles around the arena's centre, steps away from the nearest enemy when one
/// is close, and takes a card by a rule (<see cref="Pick"/>). Several bots through the real relay are the w8core smoke.
/// </summary>
public sealed class HordeBot : IDisposable
{
    public readonly uint Aid;
    public readonly string Name;
    public readonly HordeClient Client;
    public bool AutoJoin = true;
    /// <summary>Which card to take (offer, cards) → index; default: the first.</summary>
    public Func<HordeOffer, int> Pick = _ => 0;
    /// <summary>Stand still (a bot that lets itself fall).</summary>
    public bool Idle;
    public readonly List<string> Seen = new();
    public int Invites, LevelUps, JobChanges, Falls, Rises, OffersTaken, HitsSeen, Snapshots;
    public HordeResults? Results;
    public HordeRewardNote? Reward;
    readonly TcpClient _tcp;
    readonly NetworkStream _s;
    readonly Thread _reader;
    readonly object _write = new();
    volatile bool _stop;
    public bool Welcomed { get; private set; }
    public byte WelcomeCaps { get; private set; }
    public long BytesIn;

    public HordeBot(string host, int port, uint aid, string name, HordeContent content, byte sex = 1)
    {
        Aid = aid; Name = name;
        Client = new HordeClient(content) { Sex = sex };
        _tcp = new TcpClient { NoDelay = true };
        _tcp.Connect(host, port);
        _s = _tcp.GetStream();
        Client.Send = f => { lock (_write) { try { _s.Write(f, 0, f.Length); } catch (Exception) { } } };
        Client.Invited += inv => { Invites++; Seen.Add($"invite {inv.RunId} from {inv.StarterName}"); if (AutoJoin) Client.Answer(inv.RunId, true); };
        Client.Offered += o => { int i = Math.Clamp(Pick(o), 0, Math.Max(0, o.Cards.Length - 1)); Client.Choose(o.Id, i); OffersTaken++; };
        Client.Happened += e =>
        {
            int me = Client.MySlot;
            if (e.Kind == HordeEventKind.LevelUp && e.Slot == me) LevelUps++;
            else if (e.Kind == HordeEventKind.JobChanged && e.Slot == me) { JobChanges++; Seen.Add($"job {e.Value}"); }
            else if (e.Kind == HordeEventKind.Fell && e.Slot == me) Falls++;
            else if (e.Kind == HordeEventKind.Rose && e.Slot == me) Rises++;
            else if (e.Kind == HordeEventKind.Hit) HitsSeen++;
        };
        Client.SnapshotArrived += () => Snapshots++;
        Client.Ended += r => { Results = r; Seen.Add($"results {r.Outcome}"); };
        Client.Rewarded += r => { Reward = r; Seen.Add($"reward {r.BaseExp}/{r.JobExp}"); };
        var hello = PresenceWire.EncodeHello(aid, name, PresenceWire.VersionVoice);
        lock (_write) _s.Write(hello, 0, hello.Length);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "horde-bot-" + name };
        _reader.Start();
    }

    void ReadLoop()
    {
        var frames = new PresenceWire.FrameReader(PresenceWire.MaxPayloadVoice);
        var buf = new byte[65536];
        long lastPing = Environment.TickCount64;
        try
        {
            while (!_stop)
            {
                if (Environment.TickCount64 - lastPing > 2000) { var pg = PresenceWire.EncodePing(); lock (_write) _s.Write(pg, 0, pg.Length); lastPing = Environment.TickCount64; }
                if (!_tcp.Client.Poll(20000, SelectMode.SelectRead)) continue;
                int n = _s.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                BytesIn += n;
                frames.Append(buf, 0, n);
                while (true)
                {
                    int k = frames.NextView(out var p, out int o, out int len);
                    if (k == 0) break;
                    if (k < 0) throw new IOException("bad frame from the relay");
                    if (!Welcomed)
                    {
                        if (!PresenceWire.TryDecodeWelcome(p, o, len, out _, out var caps)) throw new IOException("no WELCOME");
                        Welcomed = true; WelcomeCaps = caps;
                        bool horde = (caps & HordeWire.CapHorde) != 0;
                        if (horde) frames.MaxLength = HordeWire.MaxPayload;
                        Client.OnLinked(horde);
                        continue;
                    }
                    if (HordeWire.IsRelayType(p[o])) Client.OnFrame(p, o, len);
                }
            }
        }
        catch (Exception) { }
        Client.OnUnlinked();
    }

    /// <summary>One frame of the bot: the client's Poll, then the brain's stick.</summary>
    public void Update()
    {
        Client.Poll();
        if (!Client.InRun) { Client.SetMove(0, 0); return; }
        var w = Client.World;
        var me = w.Players[Client.MySlot];
        if (Idle || !me.Present) { Client.SetMove(0, 0); return; }
        var run = Client.Run!;
        float cx = (run.X0 + run.X1) / 2f + 0.5f, cy = (run.Y0 + run.Y1) / 2f + 0.5f;
        float mx = Client.MyX, my = Client.MyY;
        // around the centre at ~6 cells, away from the nearest enemy within 3 cells
        double ang = Math.Atan2(my - cy, mx - cx) + 0.6 + Aid % 3 * 0.2;
        float tx = cx + (float)Math.Cos(ang) * 6, ty = cy + (float)Math.Sin(ang) * 6;
        float dx = tx - mx, dy = ty - my;
        float best = 9f; float ex = 0, ey = 0; bool near = false;
        foreach (var e in w.Enemies)
        {
            if (!e.Alive) continue;
            float d = (e.X - mx) * (e.X - mx) + (e.Y - my) * (e.Y - my);
            if (d < best) { best = d; ex = e.X; ey = e.Y; near = true; }
        }
        if (near) { dx = mx - ex; dy = my - ey; }
        float m = (float)Math.Sqrt(dx * dx + dy * dy);
        if (m > 0.01f) Client.SetMove(dx / m, dy / m); else Client.SetMove(0, 0);
    }

    /// <summary>Closes the connection the way a killed client's does (the relay sees it gone).</summary>
    public void Drop() { try { _tcp.Client.Close(); } catch (Exception) { } }

    public void Dispose()
    {
        _stop = true;
        try { _tcp.Close(); } catch (Exception) { }
        _reader.Join(1000);
    }
}
