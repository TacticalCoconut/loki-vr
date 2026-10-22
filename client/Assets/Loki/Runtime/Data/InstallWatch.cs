using System;
using System.Collections.Generic;
using System.IO;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 7 (w7data): is the Ragnarok Online install still being written? Loki never reads half-written game files: when
    /// an archive or an install file it reads was written within the last <see cref="QuietSeconds"/>, changes between two looks,
    /// or is held open for writing by another program, the start waits (Data/FirstRunData.cs: the panel says which file and why)
    /// until every file has been still for that long — for at most <see cref="MaxWaitSeconds"/>, after which the player is asked
    /// to let the patcher finish.
    ///
    /// What a patch looks like on disk (the orchestrator's watch of the official patcher on 2026-09-30,
    /// and the install's file times): the patcher downloads for many minutes and unpacks the loose files (System\*.lub …) one by
    /// one, then merges into data.grf and writes patch.inf in the same minute (both 00:55:07 that day). Each loose file is whole
    /// once written, so the one-minute rule covers the unpacking, and the merge shows as a data.grf that cannot be opened or was
    /// just written.
    /// Pure C# (no UnityEngine).
    /// </summary>
    public static class InstallWatch
    {
        /// <summary>A file written less than this long ago counts as possibly still being written (the brief: "written in the last minute").</summary>
        public const int QuietSeconds = 60;
        /// <summary>The longest a start waits for the install to be still (five quiet periods): after it the player decides.</summary>
        public const int MaxWaitSeconds = 300;

        public struct FileState { public string Path; public bool Exists; public long Size, Ticks; }

        public sealed class Sample
        {
            public readonly List<FileState> Files = new List<FileState>();
            public DateTime TakenUtc;
        }

        public static Sample Take(IEnumerable<string> paths)
        {
            var s = new Sample { TakenUtc = DateTime.UtcNow };
            foreach (var p in paths)
            {
                var st = new FileState { Path = p };
                try { var fi = new FileInfo(p); if (fi.Exists) { st.Exists = true; st.Size = fi.Length; st.Ticks = fi.LastWriteTimeUtc.Ticks; } } catch { }
                s.Files.Add(st);
            }
            return s;
        }

        /// <summary>Why the install is not still, or null when it is.</summary>
        public sealed class Busy
        {
            /// <summary>The file's name (no folder).</summary>
            public string File;
            public enum Kind { WrittenRecently, Changed, InUse }
            public Kind Why;
            /// <summary>Seconds since the file was written (WrittenRecently / Changed).</summary>
            public double SecondsAgo;
            /// <summary>Seconds until the file has been still for the quiet period.</summary>
            public double SecondsLeft;
            public string English => Why == Kind.InUse ? $"{File} is held open by another program (the patcher?)"
                                   : Why == Kind.Changed ? $"{File} changed while Loki was looking"
                                   : $"{File} was written {SecondsAgo:0} s ago";
        }

        /// <summary>Compares two looks at the files (<paramref name="before"/> may be null on the first look). A file dated in the
        /// future (a wrong clock) is not counted as recent: it would hold every start.</summary>
        public static Busy Check(Sample before, Sample now, int quietSeconds, IEnumerable<string> archives = null)
        {
            Busy worst = null;
            for (int i = 0; i < now.Files.Count; i++)
            {
                var f = now.Files[i];
                string name = System.IO.Path.GetFileName(f.Path);
                if (before != null && i < before.Files.Count)
                {
                    var b = before.Files[i];
                    if (b.Exists != f.Exists || b.Size != f.Size || b.Ticks != f.Ticks)
                        return new Busy { File = name, Why = Busy.Kind.Changed, SecondsAgo = 0, SecondsLeft = quietSeconds };
                }
                if (!f.Exists) continue;
                double ago = (now.TakenUtc - new DateTime(f.Ticks, DateTimeKind.Utc)).TotalSeconds;
                if (ago >= 0 && ago < quietSeconds && (worst == null || quietSeconds - ago > worst.SecondsLeft))
                    worst = new Busy { File = name, Why = Busy.Kind.WrittenRecently, SecondsAgo = ago, SecondsLeft = quietSeconds - ago };
            }
            if (worst != null) return worst;
            if (archives != null)
                foreach (var a in archives)
                    if (InUse(a)) return new Busy { File = System.IO.Path.GetFileName(a), Why = Busy.Kind.InUse, SecondsLeft = 1 };
            return null;
        }

        /// <summary>True when another program holds the file open for writing (Loki's own way of opening an archive fails with a
        /// sharing violation). A file that does not exist or cannot be reached for another reason is not "in use".</summary>
        public static bool InUse(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }
        }
    }
}
