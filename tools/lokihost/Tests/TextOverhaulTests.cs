using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LokiHost.Core;
using Xunit;

namespace LokiHost.Tests;

/// <summary>Wave 28 (wtexts): the text overhaul step (Core/TextOverhaul.cs) in temporary copies: what it puts in, what it keeps,
/// what it leaves, once per version, off and on again, a new version; and the pack Loki Host carries. (That rAthena loads the
/// files with 0 errors is the copy 233 run of design notes)</summary>
public class TextOverhaulTests
{
    static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "lokihost-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static byte[] B(string s) => Encoding.Latin1.GetBytes(s);

    /// <summary>A pack like build_pack.py's: the files and a manifest (originals' and new files' SHA-256, LF).</summary>
    static MemoryStream Pack(int version, Dictionary<string, (string orig, string now)> files)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var man = new TextOverhaul.Manifest { version = version, source = "test" };
            foreach (var (rel, (orig, now)) in files)
            {
                man.files[rel] = new TextOverhaul.Entry { orig_sha256_lf = TextOverhaul.ShaLf(B(orig)), new_sha256_lf = TextOverhaul.ShaLf(B(now)), status = "CLEAN" };
                using var s = z.CreateEntry(rel).Open(); s.Write(B(now));
            }
            using var m = z.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(m, man);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>A host copy with these files (CR LF line ends, as a Windows rAthena checkout has them).</summary>
    static string Copy(Dictionary<string, string> files)
    {
        var d = Temp();
        Directory.CreateDirectory(Path.Combine(d, "npc"));
        foreach (var (rel, text) in files)
        {
            var p = Path.Combine(d, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, B(text.Replace("\n", "\r\n")));
        }
        return d;
    }

    static string Read(string d, string rel) => Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(d, rel.Replace('/', Path.DirectorySeparatorChar)))).Replace("\r\n", "\n");

    const string A0 = "prontera,1,1,4\tscript\tA\t1,{\n\tmes \"Hello there\";\n\tmes \"friend.\";\n\tclose;\n}\n";
    const string A1 = "prontera,1,1,4\tscript\tA\t1,{\n\tmes \"Hello there, friend.\";\n\tclose;\n}\n";
    const string M0 = "1: Old message %s\n";
    const string M1 = "1: New message %s\n";

    static Dictionary<string, (string, string)> Two => new()
    {
        ["npc/cities/a.txt"] = (A0, A1),
        ["conf/msg_conf/map_msg.conf"] = (M0, M1),
    };

    [Fact]
    public void Puts_the_files_in_keeps_the_originals_and_stamps_the_version()
    {
        var d = Copy(new() { ["npc/cities/a.txt"] = A0, ["conf/msg_conf/map_msg.conf"] = M0 });
        var said = TextOverhaul.Apply(d, true, Pack(1, Two));
        Assert.Equal(A1, Read(d, "npc/cities/a.txt"));
        Assert.Equal(M1, Read(d, "conf/msg_conf/map_msg.conf"));
        // the originals are moved, byte for byte (CR LF kept), never deleted
        Assert.Equal(A0.Replace("\n", "\r\n"), Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(d, TextOverhaul.HoldDir, "npc", "cities", "a.txt"))));
        Assert.Equal(M0, Read(d, TextOverhaul.HoldDir + "/conf/msg_conf/map_msg.conf"));
        var st = TextOverhaul.ReadStamp(d)!;
        Assert.True(st.On); Assert.Equal(1, st.Version); Assert.Equal(2, st.Written.Count);
        Assert.Contains("2 file(s) put in", said.Single());
    }

    [Fact]
    public void Runs_once_per_version()
    {
        var d = Copy(new() { ["npc/cities/a.txt"] = A0, ["conf/msg_conf/map_msg.conf"] = M0 });
        TextOverhaul.Apply(d, true, Pack(1, Two));
        // someone edits a file afterwards: the same version never touches it again
        File.WriteAllText(Path.Combine(d, "npc", "cities", "a.txt"), "edited");
        var said = TextOverhaul.Apply(d, true, Pack(1, Two));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(d, "npc", "cities", "a.txt")));
        Assert.Contains("version 1 in place", said.Single());
    }

    [Fact]
    public void A_file_changed_in_the_copy_is_left_as_it_is()
    {
        var d = Copy(new() { ["npc/cities/a.txt"] = "someone's own version\n", ["conf/msg_conf/map_msg.conf"] = M0 });
        var said = TextOverhaul.Apply(d, true, Pack(1, Two));
        Assert.Equal("someone's own version\n", Read(d, "npc/cities/a.txt"));
        Assert.False(File.Exists(Path.Combine(d, TextOverhaul.HoldDir, "npc", "cities", "a.txt")));
        Assert.Equal(M1, Read(d, "conf/msg_conf/map_msg.conf"));
        Assert.Contains("1 left as they are", said.Single());
        Assert.DoesNotContain("npc/cities/a.txt", TextOverhaul.ReadStamp(d)!.Written.Keys);
    }

    [Fact]
    public void Off_puts_the_originals_back_and_on_again_puts_the_pack_in_again()
    {
        var d = Copy(new() { ["npc/cities/a.txt"] = A0, ["conf/msg_conf/map_msg.conf"] = M0 });
        TextOverhaul.Apply(d, true, Pack(1, Two));
        var said = TextOverhaul.Apply(d, false, Pack(1, Two));
        Assert.Equal(A0, Read(d, "npc/cities/a.txt"));
        Assert.Equal(M0, Read(d, "conf/msg_conf/map_msg.conf"));
        Assert.False(TextOverhaul.ReadStamp(d)!.On);
        Assert.Contains("2 original file(s) back", said.Single());
        Assert.Empty(TextOverhaul.Apply(d, false, Pack(1, Two))); // off and already off: nothing
        TextOverhaul.Apply(d, true, Pack(1, Two));
        Assert.Equal(A1, Read(d, "npc/cities/a.txt"));
        Assert.Equal(A0.Replace("\n", "\r\n"), Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(d, TextOverhaul.HoldDir, "npc", "cities", "a.txt"))));
    }

    [Fact]
    public void A_new_version_replaces_what_the_old_one_wrote_and_keeps_the_first_original()
    {
        var d = Copy(new() { ["npc/cities/a.txt"] = A0, ["conf/msg_conf/map_msg.conf"] = M0 });
        TextOverhaul.Apply(d, true, Pack(1, Two));
        const string A2 = "prontera,1,1,4\tscript\tA\t1,{\n\tmes \"Hi, friend.\";\n\tclose;\n}\n";
        var v2 = new Dictionary<string, (string, string)> { ["npc/cities/a.txt"] = (A0, A2), ["conf/msg_conf/map_msg.conf"] = (M0, M1) };
        var said = TextOverhaul.Apply(d, true, Pack(2, v2));
        Assert.Equal(A2, Read(d, "npc/cities/a.txt"));
        Assert.Equal(A0.Replace("\n", "\r\n"), Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(d, TextOverhaul.HoldDir, "npc", "cities", "a.txt"))));
        Assert.Equal(2, TextOverhaul.ReadStamp(d)!.Version);
        Assert.Contains("1 file(s) put in, 1 already in place", said.Single());
        TextOverhaul.Apply(d, false, Pack(2, v2));
        Assert.Equal(A0, Read(d, "npc/cities/a.txt")); // off after an upgrade: still the true original
    }

    [Fact]
    public void A_copy_that_is_not_set_up_is_left_alone()
    {
        var d = Temp();
        Assert.Empty(TextOverhaul.Apply(d, true, Pack(1, Two)));
        Assert.False(File.Exists(Path.Combine(d, TextOverhaul.StampFile)));
    }

    [Fact]
    public void The_setting_is_on_by_default()
    {
        Assert.True(new HostSettings().TextOverhaul);
        var s = JsonSerializer.Deserialize<HostSettings>("{\"HostDir\": \"x\"}")!; // a settings file from before the line
        Assert.True(s.TextOverhaul);
    }

    /// <summary>The pack inside Loki Host: every file matches its manifest; the scripts, mob chat, map messages and motd; the two
    /// quarantined files (quests_veins.txt, quest_db.yml) are not in it.</summary>
    [Fact]
    public void The_pack_Loki_Host_carries_matches_its_manifest()
    {
        using var s = TextOverhaul.OpenEmbedded();
        Assert.NotNull(s);
        using var z = new ZipArchive(s!, ZipArchiveMode.Read);
        var m = TextOverhaul.ReadManifest(z);
        Assert.True(m.version >= 1);
        Assert.Equal(401, m.files.Count);
        foreach (var (rel, e) in m.files)
        {
            using var es = z.GetEntry(rel)!.Open(); using var ms = new MemoryStream(); es.CopyTo(ms);
            Assert.Equal(e.new_sha256_lf, TextOverhaul.ShaLf(ms.ToArray()));
            Assert.NotEqual(e.orig_sha256_lf, e.new_sha256_lf);
        }
        Assert.Contains("db/mob_chat_db.yml", m.files.Keys);
        Assert.Contains("conf/msg_conf/map_msg.conf", m.files.Keys);
        Assert.Contains("conf/motd.txt", m.files.Keys);
        Assert.Contains("npc/re/jobs/novice/academy.txt", m.files.Keys);
        Assert.DoesNotContain("npc/quests/quests_veins.txt", m.files.Keys);
        Assert.DoesNotContain("db/re/quest_db.yml", m.files.Keys);
        Assert.Equal(398, m.files.Keys.Count(k => k.StartsWith("npc/")));
        Assert.Equal(3, m.version); // 2: the English names / cipher; 3: priest.txt's line for male characters (decisions.json)
        Assert.Equal("fix", m.files["npc/jobs/2-1/priest.txt"].status);
        Assert.Contains("npc/re/warps/cities/dicastes.txt", m.files.Keys);
    }

    /// <summary>The pack in a real rAthena layout, the host's way: ApplyConf runs the step (a copy as Loki Host's setup leaves it,
    /// with rAthena's real originals of three of the pack's files).</summary>
    [Fact]
    public void ApplyConf_runs_the_step_with_the_carried_pack()
    {
        string src = Environment.GetEnvironmentVariable("LOKI_RATHENA_SRC") ?? @"C:\rAthena";
        if (!Directory.Exists(Path.Combine(src, "npc"))) return; // (the reference source is on the project's PCs only)
        var d = Temp();
        foreach (var rel in new[] { "npc/re/jobs/novice/novice.txt", "conf/motd.txt", "db/mob_chat_db.yml" })
        {
            var p = Path.Combine(d, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.Copy(Path.Combine(src, rel.Replace('/', Path.DirectorySeparatorChar)), p);
        }
        foreach (var f in new[] { "inter_conf.txt", "login_conf.txt", "char_conf.txt", "map_conf.txt", "battle_conf.txt", "log_conf.txt" })
        {
            Directory.CreateDirectory(Path.Combine(d, "conf", "import"));
            File.WriteAllText(Path.Combine(d, "conf", "import", f), "");
        }
        var s = new HostSettings { HostDir = d, TutorialMap = "" };
        var log = new List<string>();
        HostSetup.ApplyConf(s, "203.0.113.5", "192.168.1.15", "255.255.255.0", log.Add);
        Assert.Contains(log, l => l.StartsWith("Text overhaul: version") && l.Contains("3 file(s) put in"));
        Assert.Contains("Welcome to Project Loki", File.ReadAllText(Path.Combine(d, "conf", "motd.txt")));
        Assert.True(File.Exists(Path.Combine(d, TextOverhaul.HoldDir, "npc", "re", "jobs", "novice", "novice.txt")));
        s.TextOverhaul = false;
        log.Clear();
        HostSetup.ApplyConf(s, "203.0.113.5", "192.168.1.15", "255.255.255.0", log.Add);
        Assert.Contains(log, l => l.StartsWith("Text overhaul: off: 3 original file(s) back"));
        Assert.Contains("Welcome to rAthena", File.ReadAllText(Path.Combine(d, "conf", "motd.txt")));
    }
}
