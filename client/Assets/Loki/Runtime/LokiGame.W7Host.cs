using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Loki.IO;
using Loki.Net;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 7, w7host: the game-data check. A player whose Ragnarok Online data differs from the host's is told once
    /// per session: the host's server walks on the maps of the host's own install, each client on its own, and where they
    /// differ a walk can be refused or drawn through a wall (design notes §1). The game's side of
    /// <see cref="IDataCheckChannel"/>: once the headset channel exists (LokiGame.W3Presence.cs: the relay's connection carries
    /// the check too), this client's data stamp is worked out on a worker thread (<see cref="W7HostData"/>: a hash of the cells
    /// of every map file the game's own file system offers, IO/MapDataStamp.cs) and handed to the channel, and the host's
    /// answer becomes one notice in the chat. Outside the RO protocol: nothing here reads or writes an RO packet, so the same
    /// code works on the SpacetimeDB module once it has an IDataCheckChannel. A host that does not compare (an older Loki
    /// Host) never answers: no notice, nothing else changes.
    /// Hook in LokiGame.cs: EnterMap, right after OnMapChangeW6Voice (the channel exists from the first map on).
    /// </summary>
    public sealed partial class LokiGame
    {
        private IDataCheckChannel _w7hostChannel;
        /// <summary>The hosts this run already told the player about (static: a scene reload or a new link is the same session).</summary>
        private static readonly HashSet<string> _w7hostTold = new HashSet<string>();
        /// <summary>The host's answers so far in this game (each new link asks again), and what the last one said.</summary>
        public int W7HostAnswers { get; private set; }
        public bool W7HostDiffers { get; private set; }
        public string W7HostHostStamp { get; private set; } = "";
        public string W7HostHostLabel { get; private set; } = "";
        /// <summary>How often the notice was shown by this game (at most once per session and host).</summary>
        public int W7HostNotices { get; private set; }

        /// <summary>Hook: EnterMap, after OnMapChangeW3Presence made the channel (first map only; later maps do nothing).</summary>
        private void OnMapChangeW7Host(string map)
        {
            if (_w7hostChannel != null) return;
            var ch = _presence as IDataCheckChannel;
            if (ch == null || _boot == null || _boot.Assets == null) return;
            _w7hostChannel = ch;
            ch.DataChecked += W7HostDataChecked;
            W7HostData.Begin(_boot.Assets.Fs, UserSettings.Dir, (stamp, label) => ch.SetDataStamp(stamp, label));
        }

        private void W7HostDataChecked(bool differs, string hostStamp, string hostLabel)
        {
            W7HostAnswers++; W7HostDiffers = differs; W7HostHostStamp = hostStamp ?? ""; W7HostHostLabel = hostLabel ?? "";
            string mine = W7HostData.Stamp ?? "", myLabel = W7HostData.Label ?? "";
            Debug.Log($"[Data] game data check: mine {mine}{(myLabel != "" ? " (" + myLabel + ")" : "")}, the host's {hostStamp}{(string.IsNullOrEmpty(hostLabel) ? "" : " (" + hostLabel + ")")}: {(differs ? "DIFFERENT" : "the same")}");
            if (!differs) return;
            string key = (Session != null ? Session.Host + ":" + Session.Port : "") + "|" + hostStamp + "|" + mine;
            if (!_w7hostTold.Add(key)) return; // told once per session
            W7HostNotices++;
            Chat(Lang.T("Your game data differs from the host's: walking may not match."), "ffb070");
            long a = MapDataStamp.PatchOf(myLabel), b = MapDataStamp.PatchOf(hostLabel);
            if (a >= 0 && b >= 0 && a < b) Chat(Lang.F("Yours: patch {0}. The host's: patch {1}. Run the official patcher.", a, b), "ffb070");
            else if (a >= 0 && b >= 0 && a > b) Chat(Lang.F("Yours: patch {0}. The host's: patch {1}. Ask the host to patch.", a, b), "ffb070");
            else Chat(Lang.T("Run the official patcher, or ask the host to."), "ffb070");
        }
    }

    /// <summary>
    /// This client's data stamp (IO/MapDataStamp.cs), worked out once per run on a worker thread: every map file the game's own
    /// file system offers is read through that file system (<see cref="RoFileSystem.Read"/>: whatever rule it follows for GRF
    /// order and loose files, the stamp follows), about 1.7 GB of cells on the LATAM install, a few seconds. The result is kept
    /// in the per-user folder (mapstamp.txt) with what it was made from (each archive's path, size and time, the loose map
    /// files): a later start with the same files reads the stamp back instead of the maps. Nothing is written into the
    /// Ragnarok Online folder.
    /// </summary>
    public static class W7HostData
    {
        /// <summary>The stamp and its label ("patch 1486") once known; null before.</summary>
        public static volatile string Stamp, Label;
        /// <summary>How the stamp was got, for the log and the smoke: "read from N maps in S s" / "kept from mapstamp.txt".</summary>
        public static volatile string How = "";
        public static volatile int Maps, Unreadable;
        public const string CacheName = "mapstamp.txt";

        private static readonly object Gate = new object();
        private static bool _started;
        private static RoFileSystem _for; // the file system the stamp belongs to (another data folder picked in game: another stamp)
        private static readonly List<Action<string, string>> Waiting = new List<Action<string, string>>();

        /// <summary>Main thread. <paramref name="then"/> gets the stamp and label: at once when they are known, else from the
        /// worker thread when it is done (the channel's SetDataStamp takes any thread).</summary>
        public static void Begin(RoFileSystem fs, string userDir, Action<string, string> then)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_for, fs)) { _for = fs; Stamp = null; Label = null; How = ""; _started = false; Waiting.Clear(); }
                if (Stamp != null) { then(Stamp, Label ?? ""); return; }
                Waiting.Add(then);
                if (_started) return;
                _started = true;
            }
            var th = new Thread(() => Work(fs, userDir)) { IsBackground = true, Name = "loki-data-stamp", Priority = System.Threading.ThreadPriority.BelowNormal };
            th.Start();
        }

        private static void Work(RoFileSystem fs, string userDir)
        {
            string stamp = null, label = "";
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                label = MapDataStamp.PatchLabel(fs.Root);
                var names = new List<string>();
                string made = MadeFrom(fs, names);
                string cache = Path.Combine(userDir, CacheName);
                if (ReadCache(cache, made, out stamp, out int maps, out int unreadable)) { Maps = maps; Unreadable = unreadable; How = "kept from " + CacheName; }
                else
                {
                    var r = MapDataStamp.Compute(names, n => fs.Read(n));
                    stamp = r.Stamp; Maps = r.Maps; Unreadable = r.Unreadable;
                    How = $"read from {r.Maps} maps in {sw.Elapsed.TotalSeconds:0.0} s";
                    if (r.Unreadable > 0) Debug.LogWarning($"[Data] {r.Unreadable} map file(s) of your Ragnarok Online data cannot be read ({string.Join(", ", r.UnreadableNames)}{(r.Unreadable > r.UnreadableNames.Count ? ", …" : "")})");
                    // kept only when the files are as they were before the maps were read (a patcher at work: next start reads again)
                    if (MadeFrom(fs, new List<string>()) == made) WriteCache(cache, made, stamp, r.Maps, r.Unreadable);
                }
                Debug.Log($"[Data] game data stamp {stamp}{(label != "" ? " (" + label + ")" : "")}: {How}");
            }
            catch (Exception e) { Debug.LogWarning("[Data] the game data stamp could not be made (" + e.GetType().Name + ": " + e.Message + "): this run's game data is not compared with the host's"); stamp = null; }
            Action<string, string>[] todo;
            lock (Gate)
            {
                if (!ReferenceEquals(_for, fs)) return; // the data folder changed meanwhile: that file system's own run answers
                Label = label; Stamp = stamp; todo = Waiting.ToArray(); Waiting.Clear(); if (stamp == null) _started = false;
            }
            if (stamp != null) foreach (var t in todo) { try { t(stamp, label); } catch (Exception e) { Debug.LogException(e); } }
        }

        /// <summary>What the stamp is made from, as text: every archive (path, size, time) in the file system's order and the
        /// loose map files; <paramref name="names"/> gets every "data\NAME.gat" on offer.</summary>
        private static string MadeFrom(RoFileSystem fs, List<string> names)
        {
            var sb = new StringBuilder("v1\n");
            foreach (var a in fs.Archives)
            {
                var fi = new FileInfo(a.Path);
                sb.Append("grf ").Append(a.Path).Append(' ').Append(fi.Exists ? fi.Length + " " + fi.LastWriteTimeUtc.Ticks : "missing").Append('\n');
                foreach (var n in a.Names) if (MapDataStamp.IsMapGat(n)) names.Add(n);
            }
            string data = Path.Combine(fs.Root ?? "", "data");
            if (Directory.Exists(data))
            {
                var loose = new List<FileInfo>();
                foreach (var f in Directory.GetFiles(data, "*.gat")) loose.Add(new FileInfo(f));
                loose.Sort((x, y) => string.CompareOrdinal(x.Name.ToLowerInvariant(), y.Name.ToLowerInvariant()));
                foreach (var f in loose)
                {
                    sb.Append("loose ").Append(f.Name.ToLowerInvariant()).Append(' ').Append(f.Length).Append(' ').Append(f.LastWriteTimeUtc.Ticks).Append('\n');
                    string n = RoPath.Normalize("data\\" + f.Name);
                    if (MapDataStamp.IsMapGat(n)) names.Add(n);
                }
            }
            return sb.ToString();
        }

        private static bool ReadCache(string path, string made, out string stamp, out int maps, out int unreadable)
        {
            stamp = null; maps = unreadable = 0;
            try
            {
                if (!File.Exists(path)) return false;
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                int cut = text.IndexOf("--\n", StringComparison.Ordinal);
                if (cut < 0 || text.Substring(0, cut) != made) return false;
                foreach (var line in text.Substring(cut + 3).Split('\n'))
                {
                    if (line.StartsWith("stamp ", StringComparison.Ordinal)) stamp = line.Substring(6).Trim();
                    else if (line.StartsWith("maps ", StringComparison.Ordinal)) int.TryParse(line.Substring(5), out maps);
                    else if (line.StartsWith("unreadable ", StringComparison.Ordinal)) int.TryParse(line.Substring(11), out unreadable);
                }
                return PresenceWire.ValidStamp(stamp) && stamp.StartsWith(MapDataStamp.Prefix, StringComparison.Ordinal);
            }
            catch (Exception) { stamp = null; return false; }
        }

        private static void WriteCache(string path, string made, string stamp, int maps, int unreadable)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, made + "--\nstamp " + stamp + "\nmaps " + maps + "\nunreadable " + unreadable + "\n");
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("[Data] " + CacheName + " could not be written (" + e.Message + "): the maps are read again at the next start"); }
        }
    }
}
