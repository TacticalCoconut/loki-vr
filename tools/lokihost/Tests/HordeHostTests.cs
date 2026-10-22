using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>Client wave 8 (w8core): what Loki Host writes into its rAthena copy for Horde (the reward NPC, its import line,
/// the town list).</summary>
public class HordeHostTests
{
    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-horde-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void The_reward_npc_is_installed_once_and_taken_out_when_the_reward_is_off()
    {
        var host = TempDir();
        var import = Path.Combine(host, "conf", "import", "map_conf.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(import)!);
        File.WriteAllLines(import, new[] { "map_port: 5121", "npc: npc/custom/something_else.txt" });
        HordeNpc.Install(host, true);
        HordeNpc.Install(host, true);
        var lines = File.ReadAllLines(import);
        Assert.Equal(1, lines.Count(l => l == HordeNpc.ImportLine));
        Assert.Contains("npc: npc/custom/something_else.txt", lines);
        Assert.Contains("map_port: 5121", lines);
        var script = File.ReadAllText(Path.Combine(host, "npc", "custom", "loki_horde.txt"));
        Assert.Contains("LokiHordeReward", script);
        Assert.DoesNotContain("\r", script);
        Assert.Contains("getexp .@base, .@job;", script);
        HordeNpc.Install(host, false);
        lines = File.ReadAllLines(import);
        Assert.DoesNotContain(HordeNpc.ImportLine, lines);
        Assert.Contains("npc: npc/custom/something_else.txt", lines);
        // for the reward smoke on a private rAthena copy: install into that copy's folder
        var target = Environment.GetEnvironmentVariable("LOKIHOST_TEST_HORDE_INSTALL_DIR");
        if (!string.IsNullOrEmpty(target))
        {
            HordeNpc.Install(target, true);
            File.WriteAllText(Path.Combine(target, "loki_horde_reward.sql"), HordeRewardDb.CreateSql + ";\n");
        }
    }

    /// <summary>The script's order (port wave I, ihorde): whether the character can gain EXP now (alive, on its map, no PvP
    /// map without pvp_exp) is decided BEFORE getexp; the row is taken before getexp and nothing gives it back, so getexp is
    /// called at most once per row (a character at its level's cap is done; its homunculus gets its share once). What the
    /// map-server does with it is the smoke's business (tools/lokihost/smoke/w8reward).</summary>
    [Fact]
    public void The_reward_npc_decides_before_getexp_and_never_gives_a_row_back()
    {
        var s = HordeNpc.Script.Replace("\r\n", "\n"); // as Install writes it
        int At(string what)
        {
            var i = s.IndexOf(what, StringComparison.Ordinal);
            Assert.True(i >= 0, "the script has no: " + what);
            Assert.True(s.IndexOf(what, i + 1, StringComparison.Ordinal) < 0, "the script has more than one: " + what);
            return i;
        }
        var dead = At(".@can = !isdead();");
        var place = At("getmapxy(.@m$, .@x, .@y)");
        var onMap = At("getareausers(.@m$, .@x, .@y, .@x, .@y)");
        var sessions = At("getareaunits(BL_PC, .@m$, .@x, .@y, .@x, .@y)");
        var pvp = At("getmapflag(.@m$, MF_PVP) && !getbattleflag(\"pvp_exp\")");
        var gate = At("if (.@can) {\n\t\t\t\t// NextBaseExp"); // (client wave 15: the share's lines follow the comment)
        var take = At("SET `granted` = 1");
        var give = At("getexp .@base, .@job;");
        Assert.True(dead < place && place < onMap && onMap < sessions && sessions < pvp && pvp < gate && gate < take && take < give);
        // client wave 15 (w15horde), the design's point 27: at the class's highest level NextBaseExp / NextJobExp are the cap a character
        // can hold (99,999,999 / 999,999,999, pc.cpp:8644-8645, 8670-8671), not a level's EXP: no share is computed from them
        Assert.Contains(".@base = 0;\n\t\t\t\t.@job = 0;\n\t\t\t\tif (.@nb > 0 && .@nb != 99999999)\n\t\t\t\t\t.@base = .@nb * .@bs[.@i] / 10000;\n\t\t\t\tif (.@nj > 0 && .@nj != 999999999)\n\t\t\t\t\t.@job = .@nj * .@js[.@i] / 10000;", s);
        Assert.True(At(".@nb = NextBaseExp;") > gate && At(".@nj = NextJobExp;") < take);
        // getareaunits ends the script on an unknown map name: it is asked only after getareausers found someone there
        Assert.Contains("if (.@on < 1)\n\t\t\t\t\t.@can = 0;\n\t\t\t\telse if (.@on != getareaunits(", s);
        // only one statement writes `granted`, and it writes 1: a row is never given back to be tried again
        Assert.DoesNotContain("`granted` = 0 WHERE", s);
        Assert.DoesNotContain("`granted_at` = NULL", s);
        Assert.Equal(1, s.Split("UPDATE `loki_horde_reward`").Length - 1);
        // the timer always starts again (no path ends the script before it)
        Assert.EndsWith("\tdetachrid;\n\tinitnpctimer;\n\tend;\n}\n", s);
    }

    [Fact]
    public void The_town_list_is_read_from_the_copys_town_map_flags()
    {
        var host = TempDir();
        var a = Path.Combine(host, "npc", "mapflag"); Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "town.txt"), "//===== comment\nprontera\tmapflag\ttown\nizlude\tmapflag\ttown\n// geffen\tmapflag\ttown\nprt_fild08\tmapflag\tnomemo\n");
        var b = Path.Combine(host, "npc", "re", "mapflag"); Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, "town.txt"), "dicastes01    mapflag    town\nPRONTERA\tmapflag\ttown\n");
        var towns = HordeNpc.Towns(host);
        Assert.Equal(new[] { "dicastes01", "izlude", "prontera" }, towns);
    }

    [Fact]
    public void Rathenas_own_town_flags_are_read()
    {
        // rAthena e985006's own files, when the source is on this PC (C:\rAthena)
        var src = @"C:\rAthena";
        if (!File.Exists(Path.Combine(src, "npc", "mapflag", "town.txt"))) return;
        var towns = HordeNpc.Towns(src);
        Assert.Contains("prontera", towns);
        Assert.Contains("izlude", towns);
        Assert.DoesNotContain("prt_fild08", towns);
    }
}
