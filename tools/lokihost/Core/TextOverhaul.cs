using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LokiHost.Core;

/// <summary>
/// Wave 28 (wtexts): the text overhaul of the game's server texts (design note, 2026-10-15): rewritten NPC dialogs (397 rAthena scripts),
/// monster chat (db/mob_chat_db.yml), the server's messages (conf/msg_conf/map_msg.conf) and the message of the day
/// (conf/motd.txt). Every file was checked by tools/textoverhaul (verify_npc.py, verify_db.py: only what players read differs from
/// rAthena e985006; the few logic differences were read one by one, tools/textoverhaul/decisions.json) and packed with a manifest by
/// build_pack.py into TextOverhaul/server-text-pack.zip, which Loki Host carries inside itself (an embedded resource).
///
/// At every Start (<see cref="HostSetup.ApplyConf"/>), only in Loki Host's OWN rAthena copy (HostSettings.HostDir), when the host's
/// setting TextOverhaul is on (the default):
///   - each file of the manifest whose copy in HostDir is still rAthena's original (its SHA-256, line ends read as LF) is MOVED to
///     HostDir\textpack-originals\&lt;same path&gt; (kept: Loki Host never deletes it) and the pack's file is written in its place;
///   - a file that is already the pack's (or that this step wrote for an earlier version of the pack) is left / updated;
///   - a file that is neither (changed in this copy by someone) is left as it is and counted in the log;
///   - HostDir\lokihost-textpack.json (the stamp) records the version and every file written; with the same version and the
///     setting on, a later Start does nothing (once per version).
/// With the setting off, every file the stamp lists that still holds what this step wrote gets its original back from
/// textpack-originals. rAthena reads these files when its map-server starts, so a change takes effect at the next Start.
/// </summary>
public static class TextOverhaul
{
    public const string ResourceName = "LokiHost.Core.TextOverhaul.server-text-pack.zip";
    public const string HoldDir = "textpack-originals";
    public const string StampFile = "lokihost-textpack.json";

    /// <summary>The pack carried inside Loki Host (null only if the build left it out).</summary>
    public static Stream? OpenEmbedded() => typeof(TextOverhaul).Assembly.GetManifestResourceStream(ResourceName);

    public sealed class Entry
    {
        public string orig_sha256_lf { get; set; } = "";
        public string new_sha256_lf { get; set; } = "";
        public string status { get; set; } = "";
        public string reason { get; set; } = "";
    }

    public sealed class Manifest
    {
        public int version { get; set; }
        public string source { get; set; } = "";
        public Dictionary<string, Entry> files { get; set; } = new();
    }

    public sealed class Stamp
    {
        public int Version { get; set; }
        public bool On { get; set; }
        /// <summary>Every file this step wrote, with the SHA-256 (LF) of what it wrote.</summary>
        public Dictionary<string, string> Written { get; set; } = new();
    }

    /// <summary>SHA-256 of the bytes with every CR LF read as LF, lowercase hex (as tools/textoverhaul writes it).</summary>
    public static string ShaLf(byte[] b)
    {
        using var ms = new MemoryStream(b.Length);
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] == (byte)'\r' && i + 1 < b.Length && b[i + 1] == (byte)'\n') continue;
            ms.WriteByte(b[i]);
        }
        return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
    }

    public static Manifest ReadManifest(ZipArchive z)
    {
        var e = z.GetEntry("manifest.json") ?? throw new InvalidDataException("the text pack has no manifest.json");
        using var s = e.Open();
        return JsonSerializer.Deserialize<Manifest>(s) ?? throw new InvalidDataException("the text pack's manifest is empty");
    }

    static byte[] ReadEntry(ZipArchive z, string rel)
    {
        var e = z.GetEntry(rel) ?? throw new InvalidDataException("the text pack lacks " + rel);
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static string Full(string dir, string rel) => Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));

    public static Stamp? ReadStamp(string hostDir)
    {
        var p = Path.Combine(hostDir, StampFile);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<Stamp>(File.ReadAllText(p)); }
        catch (JsonException) { return null; }
    }

    static void WriteStamp(string hostDir, Stamp st) =>
        File.WriteAllText(Path.Combine(hostDir, StampFile), JsonSerializer.Serialize(st, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Puts the pack into <paramref name="hostDir"/> (<paramref name="on"/>) or takes it out; the lines say what it did. A copy that
    /// is not set up (no npc folder) is left alone. <paramref name="pack"/>: the zip (default: the one inside Loki Host).
    /// </summary>
    public static List<string> Apply(string hostDir, bool on, Stream? pack = null)
    {
        var said = new List<string>();
        if (!Directory.Exists(Path.Combine(hostDir, "npc"))) return said;
        using var stream = pack ?? OpenEmbedded();
        if (stream == null) { said.Add("Text overhaul: not in this build of Loki Host."); return said; }
        using var z = new ZipArchive(stream, ZipArchiveMode.Read);
        var m = ReadManifest(z);
        var st = ReadStamp(hostDir) ?? new Stamp();
        if (on)
        {
            if (st.On && st.Version == m.version) { said.Add($"Text overhaul: version {m.version} in place ({st.Written.Count} files)."); return said; }
            int put = 0, already = 0, changed = 0, missing = 0;
            var written = new Dictionary<string, string>();
            foreach (var (rel, e) in m.files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string dst = Full(hostDir, rel), keep = Full(Path.Combine(hostDir, HoldDir), rel);
                if (!File.Exists(dst)) { missing++; continue; }
                string cur = ShaLf(File.ReadAllBytes(dst));
                if (cur == e.new_sha256_lf) { already++; written[rel] = cur; continue; }
                bool ours = st.Written.TryGetValue(rel, out var w) && w == cur && File.Exists(keep); // written by an earlier version
                if (cur != e.orig_sha256_lf && !ours) { changed++; continue; }
                byte[] b = ReadEntry(z, rel);
                if (ShaLf(b) != e.new_sha256_lf) throw new InvalidDataException("the text pack does not match its manifest: " + rel);
                if (!ours)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(keep)!);
                    if (!File.Exists(keep)) File.Move(dst, keep); // the original, kept
                }
                string tmp = dst + ".lokihost-tmp";
                File.WriteAllBytes(tmp, b);
                File.Move(tmp, dst, overwrite: true);
                written[rel] = e.new_sha256_lf;
                put++;
            }
            WriteStamp(hostDir, new Stamp { Version = m.version, On = true, Written = written });
            said.Add($"Text overhaul: version {m.version}: {put} file(s) put in, {already} already in place"
                     + (changed > 0 ? $", {changed} left as they are (changed in this copy)" : "") + (missing > 0 ? $", {missing} not in this copy" : "")
                     + $"; the originals are kept in {Path.Combine(hostDir, HoldDir)}.");
        }
        else
        {
            if (!st.On) return said;
            int back = 0, left = 0;
            foreach (var (rel, w) in st.Written.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string dst = Full(hostDir, rel), keep = Full(Path.Combine(hostDir, HoldDir), rel);
                if (File.Exists(dst) && ShaLf(File.ReadAllBytes(dst)) == w && File.Exists(keep)) { File.Move(keep, dst, overwrite: true); back++; }
                else left++;
            }
            WriteStamp(hostDir, new Stamp { Version = st.Version, On = false });
            said.Add($"Text overhaul: off: {back} original file(s) back" + (left > 0 ? $", {left} left as they are (changed in this copy)" : "") + ".");
        }
        return said;
    }
}
