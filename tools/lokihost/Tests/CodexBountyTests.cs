using System.Text.RegularExpressions;
using Loki.Net;
using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>Wave 28 (wcxp): the Codex bounties on Loki Host (Core/CodexBounty.cs): the generated script it installs into its rAthena copy
/// (the same file the SpacetimeDB module runs from server/data/overlay), its line in npc/scripts_custom.conf, the host's switch, and the
/// words the client reads (client Net/CodexBountyWire.cs, Game/CodexBounty.cs). That rAthena runs the script as these rules say is the
/// client smoke wcxp's part and the rocli scenario's (client/tools/smoke-wcxp).</summary>
public class CodexBountyTests
{
    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>A copy as the setup leaves it, as far as the custom scripts go.</summary>
    static string Copy(string scriptsCustom = "// custom scripts\nnpc: npc/custom/other.txt\n")
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "npc", "custom")); Directory.CreateDirectory(Path.Combine(d, "conf", "import")); Directory.CreateDirectory(Path.Combine(d, "db", "import"));
        File.WriteAllText(Path.Combine(d, "npc", "scripts_custom.conf"), scriptsCustom);
        return d;
    }

    /// <summary>The repository's root (the test runs from tools/lokihost/Tests/bin/...).</summary>
    static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "server", "data", "overlay"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("the repository root above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_installed_script_is_the_overlays_file_the_module_runs()
    {
        string overlay = File.ReadAllText(Path.Combine(Repo(), "server", "data", "overlay", "npc", "custom", "loki_codex.txt")).Replace("\r\n", "\n");
        Assert.Equal(overlay, CodexBounty.Script());
        // the module loads it at start (the overlay's own custom script list)
        Assert.Contains(File.ReadAllLines(Path.Combine(Repo(), "server", "data", "overlay", "npc", "scripts_custom.conf")), l => l.Trim() == CodexBounty.NpcLine);
    }

    [Fact]
    public void The_script_is_one_NPC_the_client_whispers_whose_kills_fix_amounts_that_only_its_claims_pay()
    {
        string s = CodexBounty.Script();
        Assert.Equal(CodexBounty.Npc, CodexBountyWire.Npc.Substring("NPC:".Length));
        Assert.Contains("\n-\tscript\t" + CodexBounty.Npc + "\t-1,{\n", s);
        Assert.Single(Regex.Matches(s, @"^-\tscript\t", RegexOptions.Multiline));
        foreach (var label in new[] { "OnInit:", "OnWhisperGlobal:", "OnNPCKillEvent:", "S_Get:", "S_Set:", "S_BGet:", "S_BSet:", "S_Count:", "S_Exp:",
                                      "S_Complete:", "S_Party:", "S_Near:", "S_Track:", "S_Need:", "S_Special:", "S_Sweep:", "S_Pay:", "S_Zeny:", "S_Bonus:" })
            Assert.Contains("\n" + label + "\n", s);
        int code = s.IndexOf("\nOnInit:\n", StringComparison.Ordinal); // (the header's comments name the buildins too)
        string body = s.Substring(code);
        // EXP and Zeny are paid in ONE place (S_Pay), only with getexp2 (no rate, no tax, no boost: script.cpp:24713-24735) or the Zeny
        // parameter; S_Pay is called only by the two Claim requests ("c", "k"), never by a kill
        Assert.DoesNotMatch(@"\bgetexp\s", body);
        Assert.Single(Regex.Matches(body, @"\bgetexp2\b"));
        Assert.Contains("getexp2 .@amount, 0;", body);
        Assert.Single(Regex.Matches(body, @"Zeny \+= "));
        Assert.Equal(2, Regex.Matches(body, @"callsub\(S_Pay,").Count);
        int kill = body.IndexOf("\nOnNPCKillEvent:\n", StringComparison.Ordinal);
        Assert.DoesNotContain("S_Pay", body.Substring(kill));
        Assert.Contains("if (Zeny + .@z > 2147483647)", body); // (MAX_ZENY is INT_MAX: the claim waits rather than lose Zeny)
        Assert.Contains("if (NextBaseExp == 99999999) {\n\t\t.@z = callsub(S_Zeny, .@amount);", body); // (Zeny decided at the claim: the level then)
        // design note (2026-10-16): the CHARACTER's bounties, packed: no account variable, 2 bits per creature in LOKI_CXF[id / 31], 2 per bracket in LOKI_CXB
        Assert.DoesNotMatch(@"#LOKI_", body);
        Assert.Contains("return (LOKI_CXF[.@m / 31] >> ((.@m % 31) * 2)) & 3;", body);
        Assert.Contains("LOKI_CXF[.@w] = .@o - (((.@o >> .@s) & 3) << .@s) + (getarg(1) << .@s);", body);
        Assert.Contains("LOKI_CXB = .@o - (((.@o >> .@s) & 3) << .@s) + (getarg(1) << .@s);", body);
        Assert.Contains("LOKI_CXE[.@m] = .@exp * 1000;", body); Assert.Contains(".@exp = .@e / 1000;", body);
        Assert.Contains("getareaunits(BL_MOB, .@map$", body); Assert.Contains(".@d[UMOB_CLASS] == .@m && .@d[UMOB_HP] > 0", body);
        Assert.Contains(".@r = getbattleflag(\"area_size\") + 4;", body);
        // the server's scan clock: first sight near a living one ("n"), a scan ("b") only after S_Need (the client's look times)
        Assert.Contains("@cxt[.@m] = gettimetick(0);", body);
        Assert.Contains("if (gettimetick(0) - @cxt[.@m] + 500 < callsub(S_Need, .@m))", body);
        Assert.Contains(".@need = 20000 - 30000 * @cxl[.@m] / .@x;", body); Assert.Contains(".@need = 1500 + 30 * .@lv;", body); // (CodexData.LookSeconds / HitOff)
        // a kill report counts only for the player's own last kill as rAthena recorded it (killedgid / killedrid, set before an event label runs)
        Assert.Contains("if (.@g < 1 || killedgid != .@g)", body); Assert.Contains(".@m = killedrid;", body);
        // quest / event creatures: placed at first sight and watched; a watched unit gone completes it; a unit of an earlier run is no proof
        Assert.Contains("dispbottom \"LCX1 P \" + .@m;", body); Assert.Contains("if (LOKI_CXVB != .boot) {", body); Assert.Contains(".boot = gettimetick(2);", body);
        // the party: members within the killer's sight, alive, not more than 10 levels below the creature; never a member in a dialogue
        Assert.Contains("if (!attachrid(.@pa[.@i], 0))", body); Assert.Contains("BaseLevel < .@lv - 10", body); Assert.Contains("attachrid .@me, 1;", body);
        Assert.Equal(2, Regex.Matches(body, @"callsub S_Party, \.@m;").Count); // (OnNPCKillEvent and the killedgid report)
        // flat EXP fixed by creature level and danger (S_Exp), independent of player BaseLevel or NextBaseExp
        foreach (var line in new[] { ".@mult = 1;", ".@mult = 2;", ".@mult = 4;", "return (.@lv * .@lv * 15 + .@lv * 35) * .@mult;",
                                     ".@exp = callsub(S_Exp, .@lv, .@dg);" })
            Assert.Contains(line, body);
        // the bracket: flat EXP bonus (5x the normal bounty at the bracket's top level); a fixed or paid bracket never opens again
        Assert.Contains("return callsub(S_Exp, .@b * 10, 0) * 5;", body);
        Assert.Contains("LOKI_CXK[.@b] = .@bx;", body); Assert.Contains(".@bx = LOKI_CXK[.@b];", body);
        Assert.Contains("if (callsub(S_BGet, .@b) == 0 && callsub(S_Count, .@b) >= .bn[.@b]) {", body);
        Assert.DoesNotContain("callsub S_BSet, .@b, 0;", body);
        // Zeny: the drops' NPC value of the kills that EXP stands for, at this server's EXP rate
        Assert.Contains(".@z = getarg(0) * .@r / 1000000 * 100 / .@rate;", body);
        Assert.Contains(".@rate = getbattleflag(\"base_exp_rate\");", body);
    }

    [Fact]
    public void The_scripts_tables_agree_with_each_other()
    {
        string s = CodexBounty.Script();
        var t = Regex.Matches(s, @"\.t\[(\d+)\] = (\d+);").ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));
        var ids = Regex.Matches(s, @"setarray \.ids\[\d+\], ([\d, ]+);").SelectMany(m => m.Groups[1].Value.Split(',').Select(x => int.Parse(x.Trim()))).ToList();
        var bi = Regex.Matches(s, @"setarray \.bi\[\d+\], ([\d, ]+);").SelectMany(m => m.Groups[1].Value.Split(',').Select(x => int.Parse(x.Trim()))).ToList();
        var range = Regex.Matches(s, @"\.bs\[(\d+)\] = (\d+); \.be\[\d+\] = (\d+); \.bn\[\d+\] = (\d+);").Select(m => (b: int.Parse(m.Groups[1].Value), s: int.Parse(m.Groups[2].Value), e: int.Parse(m.Groups[3].Value), n: int.Parse(m.Groups[4].Value))).ToList();
        var bk = Regex.Matches(s, @"\.bk\[(\d+)\] = (\d+); \.bg\[\d+\] = (\d+);").ToDictionary(m => int.Parse(m.Groups[1].Value), m => (b: int.Parse(m.Groups[2].Value), g: int.Parse(m.Groups[3].Value)));
        var mx = Regex.Matches(s, @"\.mx\[(\d+)\] = (\d+);").ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));
        // the creatures the Codex can meet (1,781 with int_land zero-drop Poring 2401 excluded): every one once, in id order, and .idn says how many
        Assert.Equal(1781, t.Count);
        Assert.False(t.ContainsKey(2401));
        Assert.Equal(t.Keys.OrderBy(x => x).ToList(), ids);
        Assert.Contains($".idn = {ids.Count};", s);
        Assert.All(t.Values, v => Assert.InRange((v - 1) % 10, 0, 2)); // (danger 0 / 1 / 2)
        Assert.Equal(11, t[1002]); // Poring: level 1, normal
        Assert.Equal(813, t[1039]); // Baphomet: level 81, MVP
        // the brackets: flat by bracket, each member once, a creature the Codex meets, of that bracket's levels, its names counted once
        Assert.Equal(bi.Count, bi.Distinct().Count());
        Assert.Equal(bi.Count, bk.Count);
        int at = 0;
        foreach (var r in range.OrderBy(r => r.b))
        {
            Assert.Equal(at, r.s); at = r.e;
            var members = bi.Skip(r.s).Take(r.e - r.s).ToList();
            Assert.NotEmpty(members);
            Assert.All(members, m => { Assert.True(t.ContainsKey(m)); Assert.Equal(r.b, bk[m].b); Assert.Equal(r.b, CodexBountyWire.BracketOf((t[m] - 1) / 10)); });
            Assert.Equal(r.n, members.Select(m => bk[m].g).Distinct().Count());
        }
        Assert.Equal(bi.Count, at);
        Assert.Contains($".bmax = {range.Max(r => r.b)};", s);
        Assert.Equal(25, range.First(r => r.b == 1).n); // levels 1-10: modifier variants (Elusive Poring, Swift Poring...) grouped with base creature
        Assert.Equal(bk[1002].g, bk[2698].g); // Elusive Poring shares Poring's group in bracket 1
        Assert.True(bk.ContainsKey(1002) && !bk.ContainsKey(2401) && !bk.ContainsKey(2408));
        Assert.True(t.ContainsKey(1182) && !bk.ContainsKey(1182)); // the Thief Mushroom: a bounty, but quest-only (job_monk): in no bracket
        Assert.True(t.ContainsKey(1725) && !bk.ContainsKey(1725)); // the Monster Race's Poring: script-spawned only: a bounty, in no bracket
        Assert.True(t.ContainsKey(2415) && !bk.ContainsKey(2415)); // Knight Seyren: only in the guild dungeon teg_dun01: a bounty, in no bracket
        // the quest and event creatures (placed at first sight): met, never in a bracket, never a training dummy
        var q = Regex.Matches(s, @"\.q\[(\d+)\] = 1;").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Contains(1725, q); Assert.Contains(1182, q); Assert.DoesNotContain(1002, q); Assert.DoesNotContain(2415, q); Assert.DoesNotContain(2408, q);
        Assert.All(q, m => { Assert.True(t.ContainsKey(m)); Assert.False(bk.ContainsKey(m)); });
        var zr = Regex.Matches(s, @"\.zr\[(\d+)\] = (\d+);").ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));
        Assert.True(zr.ContainsKey(99) && zr.ContainsKey(200) && zr.ContainsKey(275) && zr.Values.All(v => v > 0));
        // the job tables' highest base levels (db/re/job_exp.yml): Novice 99, Rune Knight 200
        Assert.Equal(99, mx[0]); Assert.Equal(200, mx[4054]);
    }

    [Fact]
    public void Write_puts_the_script_and_its_line_in_the_copy_and_changes_nothing_the_second_time()
    {
        string d = Copy();
        string conf = Path.Combine(d, "npc", "scripts_custom.conf"), script = Path.Combine(d, "npc", "custom", CodexBounty.ScriptFile);
        Assert.Equal(new[] { "Codex bounties: on (a scan places a bounty, a kill completes it, the Codex claims it)." }, CodexBounty.Write(d, true));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\nnpc: npc/custom/loki_codex.txt\n", File.ReadAllText(conf));
        Assert.Equal(CodexBounty.Script(), File.ReadAllText(script));
        var t0 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var f in new[] { conf, script }) File.SetLastWriteTimeUtc(f, t0);
        CodexBounty.Write(d, true);
        foreach (var f in new[] { conf, script }) Assert.Equal(t0, File.GetLastWriteTimeUtc(f));
        // a script someone edited is put back; the line stays one
        File.AppendAllText(script, "// edited\n");
        CodexBounty.Write(d, true);
        Assert.Equal(CodexBounty.Script(), File.ReadAllText(script));
        Assert.Single(File.ReadAllLines(conf), l => l == CodexBounty.NpcLine);
        // the test settings' NPC switch-off (RathenaCopy.RemoveTestSettings) leaves our line alone
        Assert.Equal(0, Conf.DisableTestNpcs(conf));
        Assert.Single(File.ReadAllLines(conf), l => l == CodexBounty.NpcLine);
    }

    [Fact]
    public void Off_comments_the_line_out_and_on_brings_it_back_once()
    {
        string d = Copy();
        string conf = Path.Combine(d, "npc", "scripts_custom.conf");
        // never on: off touches nothing
        Assert.Empty(CodexBounty.Write(d, false));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\n", File.ReadAllText(conf));
        CodexBounty.Write(d, true);
        Assert.Equal(new[] { "Codex bounties: off." }, CodexBounty.Write(d, false));
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\n// (Loki Host: Codex bounties off) npc: npc/custom/loki_codex.txt\n", File.ReadAllText(conf));
        Assert.True(File.Exists(Path.Combine(d, "npc", "custom", CodexBounty.ScriptFile))); // (the file stays; rAthena reads only the list)
        CodexBounty.Write(d, true);
        Assert.Equal("// custom scripts\nnpc: npc/custom/other.txt\nnpc: npc/custom/loki_codex.txt\n", File.ReadAllText(conf));
        // a folder that is not a set-up copy (no custom script list): nothing is made
        string empty = Temp();
        Assert.Empty(CodexBounty.Write(empty, true));
        Assert.False(Directory.Exists(Path.Combine(empty, "npc")));
    }

    [Fact]
    public void ApplyConf_installs_the_bounties_at_every_start_and_the_setting_switches_them_off()
    {
        string d = Copy();
        var said = new List<string>();
        HostSetup.ApplyConf(new HostSettings { HostDir = d }, "203.0.113.7", "192.168.1.20", "255.255.255.0", said.Add);
        Assert.Contains("Codex bounties: on (a scan places a bounty, a kill completes it, the Codex claims it).", said);
        Assert.Contains(File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")), l => l == CodexBounty.NpcLine);
        Assert.True(new HostSettings().CodexBounties); // (on by default: a settings file from before the setting gets it on)
        HostSetup.ApplyConf(new HostSettings { HostDir = d, CodexBounties = false }, "203.0.113.7", "192.168.1.20", "255.255.255.0");
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(d, "npc", "scripts_custom.conf")), l => l == CodexBounty.NpcLine);
    }

    [Fact]
    public void The_clients_words_read_the_scripts_lines_and_nothing_else()
    {
        Assert.False(CodexBountyWire.TryParse("Codex: hello", out _));
        Assert.False(CodexBountyWire.TryParse("LCX1", out _));
        Assert.True(CodexBountyWire.TryParse("LCX1 H 4 1782 19384", out var h)); Assert.Equal((CodexBountyWire.Kind.StateBegin, 4, 1782, 19384L), (h.Kind, h.Id, h.Count, h.ZenyPerMillion));
        Assert.True(CodexBountyWire.TryParse("LCX1 P 1725", out var pl)); Assert.Equal((CodexBountyWire.Kind.PlacedAtSight, 1725), (pl.Kind, pl.Id));
        Assert.Equal("n#1063", CodexBountyWire.Seen(1063)); Assert.Equal("p", CodexBountyWire.Ping()); Assert.Equal("b#1063", CodexBountyWire.Place(1063));
        Assert.True(CodexBountyWire.TryParse("LCX1 O ,1002,1063", out var o)); Assert.Equal(new[] { 1002, 1063 }, o.Ids);
        Assert.True(CodexBountyWire.TryParse("LCX1 R ,1009:1165,1089:1683", out var r)); Assert.Equal(new[] { (1009, 1165L), (1089, 1683L) }, r.Amounts);
        Assert.True(CodexBountyWire.TryParse("LCX1 D ,1092", out var dd)); Assert.Equal(CodexBountyWire.Kind.Claimed, dd.Kind); Assert.Equal(new[] { 1092 }, dd.Ids);
        Assert.True(CodexBountyWire.TryParse("LCX1 G ;1:3:38:0:0;2:36:36:1:6807", out var g));
        Assert.Equal(new[] { (1, 3, 38, 0, 0L), (2, 36, 36, 1, 6807L) }, g.Brackets);
        Assert.True(CodexBountyWire.TryParse("LCX1 W 1002 25 290", out var w)); Assert.Equal((CodexBountyWire.Kind.Completed, 1002, 25L, 290), (w.Kind, w.Id, w.Exp, w.Share));
        Assert.True(CodexBountyWire.TryParse("LCX1 C 1002 25055 200 485", out var c)); Assert.Equal((CodexBountyWire.Kind.Paid, 1002, 25055L, 200, 485L), (c.Kind, c.Id, c.Exp, c.Share, c.Zeny));
        Assert.True(CodexBountyWire.TryParse("LCX1 K 1 6807 0", out var k)); Assert.Equal((CodexBountyWire.Kind.BracketPaid, 1, 6807L, 0L), (k.Kind, k.Id, k.Exp, k.Zeny));
        Assert.True(CodexBountyWire.TryParse("LCX1 Z what", out var z)); Assert.Equal(CodexBountyWire.Kind.None, z.Kind); // (a newer server's line: ours, not understood)
        Assert.Equal("x#110028013", CodexBountyWire.Report(110028013)); Assert.Equal("c#1002", CodexBountyWire.Claim(1002)); Assert.Equal("k#1", CodexBountyWire.ClaimBracket(1));
        // the client's state of them
        var st = new Loki.Game.CodexBounty(); int done = 0, paid = 0, ready = 0, sight = 0;
        st.Completed += (m, e, sh) => done++; st.Paid += (m, e, sh, zz) => paid++; st.BracketReady += (b, e) => ready++; st.PlacedAtSight += m => sight++;
        foreach (var l in new[] { "LCX1 H 4 1782 0", "LCX1 O ,1002,1063", "LCX1 R ,1009:1165", "LCX1 D ,1092", "LCX1 G ;1:37:38:0:0", "LCX1 E" }) { CodexBountyWire.TryParse(l, out var x); st.Take(x); }
        Assert.True(st.Supported && !st.Reading && st.IsOpen(1002) && st.IsReady(1009) && st.ReadyAmount(1009) == 1165 && st.IsClaimed(1092) && st.Bracket(1) == (37, 38, 0, 0L));
        CodexBountyWire.TryParse("LCX1 P 1725", out var px); st.Take(px);
        Assert.True(st.IsOpen(1725) && sight == 1);
        foreach (var l in new[] { "LCX1 W 1002 25 290", "LCX1 C 1002 25 290 0", "LCX1 G ;1:38:38:1:6807" }) { CodexBountyWire.TryParse(l, out var x); st.Take(x); }
        Assert.True(st.IsClaimed(1002) && !st.IsReady(1002) && done == 1 && paid == 1 && ready == 1 && st.Bracket(1) == (38, 38, 1, 6807L));
        CodexBountyWire.TryParse("LCX1 K 1 6807 0", out var kp); st.Take(kp);
        Assert.Equal(2, st.Bracket(1).state);
        st.Reset(); Assert.False(st.Supported || st.IsClaimed(1002));
    }

    [Fact]
    public void The_share_is_2_to_4_percent_near_and_above_and_falls_7_levels_below()
    {
        // (creature level, danger, player level) -> share in 1/100 %
        Assert.Equal(300, CodexBountyWire.Share(1, 0, 1));    // the player's own level: 3 %
        Assert.Equal(400, CodexBountyWire.Share(60, 0, 50));  // 10 levels or more above: 4 %
        Assert.Equal(240, CodexBountyWire.Share(1, 0, 7));    // 6 below: 2.4 %
        Assert.Equal(217, CodexBountyWire.Share(1, 0, 8));    // design note (2026-10-16): 7 below, it falls (the rocli scenario's "W 1002 96 217")
        Assert.Equal(148, CodexBountyWire.Share(1, 0, 11));   // 10 below
        Assert.Equal(10, CodexBountyWire.Share(1, 0, 17));    // 16 below and more: 0.10 % (the scenario's "W 1002 9 10" at level 20)
        Assert.Equal(10, CodexBountyWire.Share(1, 0, 99));
        Assert.Equal(15, CodexBountyWire.Share(27, 1, 60));   // a boss 33 below: 0.15 % (the scenario's Toad "W 1089 86 15")
        Assert.Equal(17, CodexBountyWire.Share(1, 2, 99));    // an MVP far below: 0.17 %
        Assert.Equal(350, CodexBountyWire.Share(50, 1, 50));  // a boss at the player's level: 3.5 %
        Assert.Equal(375, CodexBountyWire.Share(99, 2, 99));  // an MVP: 3.75 %
        for (int c = 0; c <= 200; c += 7) for (int p = 1; p <= 200; p += 9) for (int dg = 0; dg <= 2; dg++)
            {
                int v = CodexBountyWire.Share(c, dg, p);
                Assert.InRange(v, CodexBountyWire.Low(dg), 400);
                if (c - p >= -6) Assert.InRange(v, 200, 400); // (design note: the 2 % minimum only near or above)
                Assert.True(CodexBountyWire.Share(c + 1, dg, p) >= v); // (never less for a stronger creature)
            }
        Assert.Equal(1L, CodexBountyWire.Amount(9, 200)); // (at least 1)
        Assert.Equal(26L, CodexBountyWire.Amount(894, 300)); // 894 x 3 % = 26.82 -> 26 (the script's integer division)
        Assert.Equal("3%", CodexBountyWire.Percent(300)); Assert.Equal("3.5%", CodexBountyWire.Percent(350)); Assert.Equal("3.75%", CodexBountyWire.Percent(375));
        Assert.Equal(1, CodexBountyWire.BracketOf(0)); Assert.Equal(1, CodexBountyWire.BracketOf(10)); Assert.Equal(2, CodexBountyWire.BracketOf(11));
        Assert.Equal(485L, CodexBountyWire.Zeny(25055, 19384)); // the Zeny label: 25,055 EXP at 19,384 Zeny per 1,000,000 (5x rate, the 99 table)
        // Flat EXP rewards by creature level and danger (0 normal: 1x, 1 boss: 2x, 2 MVP: 4x) and flat bracket bonuses
        Assert.Equal(50L, CodexBountyWire.FlatExp(1, 0));
        Assert.Equal(100L, CodexBountyWire.FlatExp(1, 1));
        Assert.Equal(200L, CodexBountyWire.FlatExp(1, 2));
        Assert.Equal(1850L, CodexBountyWire.FlatExp(10, 0));
        Assert.Equal(9250L, CodexBountyWire.FlatBracketBonus(1));
        Assert.Equal(33500L, CodexBountyWire.FlatBracketBonus(2));
    }
}
