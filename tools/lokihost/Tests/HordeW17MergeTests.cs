using System.Net;
using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Loki.Net;
using LokiHost.Core;
using Xunit;
using Xunit.Abstractions;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 17, the merge (h17int): the rules where two branches meet. "Who hit whom" has ONE source, h17read's HurtPlayer
/// parameters (fromEnemy, via): h17pass's touch triggers read them (Toucher), h17boss's strikes fill them. Test tables and test
/// numbers (the engine tests' own content plus a few test enemies); deterministic.
/// </summary>
public class HordeW17MergeTests
{
    readonly ITestOutputHelper _out;
    public HordeW17MergeTests(ITestOutputHelper o) { _out = o; }

    const int Turret = 60, Biter = 61, TestPassive = 901;
    const int U = HordeSim.U, T = HordeSim.TickRate;

    /// <summary>The engine tests' tables, no waves, no criticals, nobody falls; a turret (Still, shoots 10 every second from where it
    /// stands, never touches), a biter (Still, 10 a touch every 0.5 s) and one test passive with <paramref name="proc"/>.</summary>
    static HordeContent Content(PassiveProcDef proc)
    {
        var c = HordeTestContent.Build();
        c.Waves.Clear();
        c.Rules.CritChance = 0;
        c.Rules.StartHp = 1000;
        c.Rules.ExpTable = new[] { 100000000 }; c.Rules.ExpGrowthPct = 0;
        c.Enemies.Add(new EnemyDef { Id = Turret, MobId = 1016, Key = "TURRET", Behavior = EnemyBehavior.Still, Hp = 1000000, Damage = 0, Radius = 0.45f, Exp = 0,
            ShotDamage = 10, ShotRange = 8f, ShotSpeed = 7f, AttackInterval = 1f });
        c.Enemies.Add(new EnemyDef { Id = Biter, MobId = 1002, Key = "BITER", Behavior = EnemyBehavior.Still, Hp = 1000000, Damage = 10, AttackInterval = 0.5f, Exp = 0, Radius = 0.5f });
        c.Passives.Add(new PassiveDef { Id = TestPassive, Key = "test_passive", Levels = new[] { new ModDef[0], new ModDef[0], new ModDef[0] }, Proc = proc });
        return c;
    }

    static HordeSim Sim(HordeContent c, out SimPlayer me)
    {
        var sim = new HordeSim(c, c.Arenas[0], 17, 900);
        sim.AddPlayer(5000001, "runner");
        me = sim.Player(0)!;
        Assert.True(sim.GivePassive(0, TestPassive, 1));
        me.Weapons.Clear();
        me.SafeUntil = 0;
        return sim;
    }

    static int Put(HordeSim sim, int enemyId, double dxCells, double dyCells)
    {
        var p = sim.Player(0)!;
        return sim.Spawn(sim.C.Enemy(enemyId)!, p.X + (int)Math.Round(dxCells * U), p.Y + (int)Math.Round(dyCells * U), true);
    }

    static List<HordeEvent> Run(HordeSim sim, int ticks)
    {
        var all = new List<HordeEvent>();
        for (int i = 0; i < ticks && !sim.Over; i++) { sim.Step(); all.AddRange(sim.Events); all.AddRange(sim.Hits); sim.ClearOutputs(); }
        return all;
    }

    [Fact]
    public void Auto_Counter_and_Reflect_answer_a_touch_but_never_a_shot_fired_from_where_its_shooter_stands()
    {
        foreach (var proc in new[]
        {
            new PassiveProcDef { Kind = ProcKind.HurtCounter, Body = ProcBody.Slash, Damage = 30, Cooldown = 1f },
            new PassiveProcDef { Kind = ProcKind.Reflect, Body = ProcBody.Shield, Power = 50 },
        })
        {
            // a turret 4 cells away: every hit is a shot, fired from the turret's own (unchanging) place
            var sim = Sim(Content(proc), out _);
            int t = Put(sim, Turret, 4, 0);
            var ev = Run(sim, T * 6);
            var shots = ev.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0).ToList();
            var back = ev.Where(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == t && e.DefId == 0).ToList();
            _out.WriteLine($"{proc.Kind} vs the turret: {shots.Count} shot hits (via {string.Join(",", shots.Select(e => (HordeHitVia)(int)e.A).Distinct())}), {back.Count} answers");
            Assert.True(shots.Count >= 3);
            Assert.All(shots, e => { Assert.Equal(HordeHitVia.Shot, (HordeHitVia)(int)e.A); Assert.Equal(t, e.Target); });
            Assert.Empty(back);
            Assert.Equal(0, sim.ProcsFired(0, proc.Kind));

            // a biter touching: answered
            sim = Sim(Content(proc), out _);
            int b = Put(sim, Biter, 0.7, 0);
            ev = Run(sim, T * 4);
            var touches = ev.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Value > 0).ToList();
            back = ev.Where(e => e.Kind == HordeEventKind.Hit && e.Slot == 0 && e.Target == b && e.DefId == 0).ToList();
            _out.WriteLine($"{proc.Kind} vs the biter: {touches.Count} touches, {back.Count} answers");
            Assert.All(touches, e => { Assert.Equal(HordeHitVia.Touch, (HordeHitVia)(int)e.A); Assert.Equal(b, e.Target); });
            Assert.True(back.Count >= 3);
        }
    }

    [Fact]
    public void A_boss_strike_names_its_caster_and_comes_as_an_area_and_h17cards_reads_the_same_source()
    {
        // the game's tables: Ork Hero's Ground Slam (a cone, h17boss) at a runner standing 1.5 cells in front of it
        var c = HordeTables.Build();
        c.Waves.Clear(); c.Objectives.Enabled = false;
        var ork = c.Enemies.First(e => e.Key == "ORK_HERO");
        ork.Speed = 0.01f; ork.Damage = 0;
        ork.Abilities = ork.Abilities.Where(a => a.Kind == AbilityKind.Cone).ToArray();
        Assert.NotEmpty(ork.Abilities);
        foreach (var a in ork.Abilities) { a.FirstAfter = 0.1f; a.Cooldown = 3f; }
        var sim = new HordeSim(c, c.Arenas[0], 23, 900);
        sim.AddPlayer(5000001, "runner");
        var me = sim.Player(0)!; me.SafeUntil = 0; me.Weapons.Clear(); me.Hp = me.MaxHp = 100000;
        int slot = sim.Spawn(ork, me.X + (int)(1.5 * U), me.Y, true);
        var ev = Run(sim, T * 8);
        var hits = ev.Where(e => e.Kind == HordeEventKind.PlayerHit && e.Slot == 0).ToList();
        Assert.NotEmpty(hits);
        var h = hits[0];
        _out.WriteLine($"Ground Slam hits: {hits.Count}; first: target {h.Target} (the ork: {slot}), via {(HordeHitVia)(int)h.A}, from ({h.X2}, {h.Y2})");
        Assert.All(hits, e => { Assert.Equal(slot, e.Target); Assert.Equal(HordeHitVia.Area, (HordeHitVia)(int)e.A); });
    }

    [Fact]
    public async Task A_protocol_2_client_and_a_protocol_2_host_refuse_this_wave_cleanly()
    {
        Assert.Equal(4, HordeWire.Version); // the wave's ONE bump (main was 2); client wave 18 (h18feel): 3 -> 4 (the souls' block, point 35)
        Assert.StartsWith("h4-", HordeWire.ContentStamp(HordeTables.Build()));
        // a client of this wave on a protocol-2 host (client waves 9-16): OtherProtocol, nothing of a run is sent
        var st = new HordeStatus { Enabled = true, Phase = HordePhase.Idle };
        var full = HordeWire.EncodeStatus(st, true);
        full = full.Take(full.Length - 1).ToArray(); full[0] = (byte)(full.Length - 2); full[1] = (byte)((full.Length - 2) >> 8); // (client wave 18, h18rules: H_STATUS's new tail, the invitable list (empty: one byte), left out as such a relay writes it)
        full[full.Length - 1] = 2; // the trailing protocol byte, as a protocol-2 relay writes it
        var c = new HordeClient(HordeTestContent.Build(), () => 0.0);
        var sent = new List<byte[]>(); c.Send = sent.Add;
        c.OnLinked(true); c.Poll();
        Assert.Single(sent); // its H_HELLO
        c.OnFrame(full, 2, full.Length - 2); c.Poll();
        Assert.Equal(HordeLinkState.OtherProtocol, c.LinkState);
        c.RequestStart(); c.Poll();
        Assert.Single(sent);

        // a protocol-2 client on this wave's host: told "not ok", never sent a run; its connection (headsets, voice) stays
        var content = HordeTestContent.Build();
        var horde = new HordeService(content, new HordeSettings { InviteSeconds = 1 }) { TickMs = 10 };
        var relay = new PoseRelay(0) { Horde = horde };
        horde.Transport = relay;
        var log = new List<string>(); horde.Log += l => { lock (log) log.Add(l); };
        relay.Start(IPAddress.Loopback); horde.Start();
        try
        {
            using var old = new System.Net.Sockets.TcpClient(); old.Connect(IPAddress.Loopback, relay.Port);
            var s = old.GetStream();
            s.Write(PresenceWire.EncodeHello(3300011, "Sixteen", PresenceWire.VersionVoice));
            s.Write(new HordeWire.W().U8(HordeWire.HHello).U8(2).U8(1).Str(HordeWire.ContentStamp(content), HordeWire.MaxStampBytes).Frame());
            s.Write(HordeWire.EncodeStart());
            var buf = new byte[65536]; int n = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1500) { if (old.Client.Poll(100000, System.Net.Sockets.SelectMode.SelectRead)) { int k = s.Read(buf, n, buf.Length - n); if (k <= 0) break; n += k; } }
            var frames = new PresenceWire.FrameReader(HordeWire.MaxPayload);
            frames.Append(buf, 0, n);
            int statuses = 0, runs = 0;
            while (true)
            {
                int k = frames.NextView(out var p, out int o, out int len);
                if (k <= 0) break;
                if (p[o] == HordeWire.HStatus) { statuses++; var hs = new HordeStatus(); Assert.True(HordeWire.DecodeStatus(p, o, len, hs, out bool ok)); Assert.False(ok); Assert.Equal(HordeWire.Version, hs.Protocol); } // (the host's own protocol: 3 in wave 17, 4 since client wave 18 h18feel)
                if (p[o] == HordeWire.HRun || p[o] == HordeWire.HSnap) runs++;
            }
            _out.WriteLine($"protocol-2 client: {statuses} statuses (not ok, protocol {HordeWire.Version}), {runs} run frames");
            Assert.True(statuses >= 1); Assert.Equal(0, runs); Assert.True(old.Connected);
            lock (log) Assert.Contains(log, l => l.Contains("another version of Horde") && l.Contains($"its protocol 2, this host's {HordeWire.Version}")); // (3 in wave 17, 4 since client wave 18 h18feel)
        }
        finally { horde.Stop(); await relay.StopAsync(); }
    }

    [Fact]
    public void The_objectives_guarded_chest_is_a_big_chest_for_h17cards_it_may_evolve_a_weapon()
    {
        // the game's tables; only the guarded chest is drawn; nobody is hurt; a runner holding an evolvable weapon at its top level and its partner
        var c = HordeTables.Build();
        c.Waves.Clear(); foreach (var e in c.Enemies) e.Damage = 0;
        var w = new int[8]; w[(int)ObjectiveKind.Chest] = 10; c.Objectives.KindWeights = w;
        var evo = c.Weapons.First(x => x.EvolveOf > 0 && x.NeedsPassive > 0);
        var root = c.Weapon(evo.EvolveOf)!;
        var sim = new HordeSim(c, c.Arenas[0], 11, 900);
        sim.AddPlayer(5000001, "runner");
        var me = sim.Player(0)!; me.SafeUntil = 0; me.Hp = me.MaxHp = 100000; me.Weapons.Clear();
        sim.Give(0, new HordeCard { Kind = HordeCardKind.NewWeapon, DefId = root.Id, Level = 1 });
        for (int l = 2; l <= root.Levels.Length; l++)
        {
            var ow = me.Weapons.First(o => HordeSim.RootId(o.Def) == root.Id);
            sim.Give(0, new HordeCard { Kind = HordeCardKind.WeaponLevel, DefId = ow.Def.Id, Level = l });
        }
        sim.Give(0, new HordeCard { Kind = HordeCardKind.NewPassive, DefId = evo.NeedsPassive, Level = 1 });
        Assert.NotNull(sim.EvolutionFor(me, me.Weapons.First(o => HordeSim.RootId(o.Def) == root.Id)));
        int evolved = -1, claimed = -1;
        for (int t = 0; t < HordeSim.Ticks(200) && claimed < 0 && !sim.Over; t++)
        {
            foreach (var q in sim.Players) if (q.Present && q.Open != null) sim.Choose(q.Slot, q.Open.Id, 0);
            if (sim.Objectives.Count > 0) { var o = sim.Objectives[0]; me.X = o.X; me.Y = o.Y; me.Vx = me.Vy = 0; }
            sim.Step();
            foreach (var e in sim.Events)
            {
                if (e.Kind == HordeEventKind.ObjectiveClaimed) claimed = sim.Tick;
                if (e.Kind == HordeEventKind.Evolved && e.Slot == 0) evolved = e.DefId;
            }
            sim.ClearOutputs();
        }
        _out.WriteLine($"{root.Key} + passive {evo.NeedsPassive}: chest claimed at {claimed / (double)HordeSim.TickRate:0.0} s, evolved into {(evolved > 0 ? c.Weapon(evolved)!.Key : "nothing")}");
        Assert.True(claimed > 0);
        Assert.Equal(evo.Id, evolved);
    }
}
