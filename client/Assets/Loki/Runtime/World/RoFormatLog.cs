using System;
using System.Collections.Generic;
using Loki.Formats;
using Loki.IO;
using UnityEngine;

namespace Loki.World
{
    /// <summary>
    /// Client wave 7 (w7data): the one place a game file that could not be read is written down — once per file, with its
    /// reason — and counted, so the startup health check and the login screen can say "N game files skipped" instead of the
    /// player finding out by a missing sprite. A file of a version Loki does not know is counted apart from a damaged one.
    /// </summary>
    public static class RoFormatLog
    {
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Kept = new List<string>();
        public static int UnknownVersions { get; private set; }
        public static int Damaged { get; private set; }
        /// <summary>The first lines reported (at most 40), newest last: "kind path: reason".</summary>
        public static List<string> Lines { get { lock (Seen) return new List<string>(Kept); } }

        /// <summary>Logs a file that was skipped (once per path) and counts it. <paramref name="what"/>: "map", "sprite", "model" …</summary>
        public static void Report(string what, string path, Exception e)
        {
            bool unknown = e is RoFormatException f && f.UnknownVersion;
            lock (Seen)
            {
                if (!Seen.Add(what + "|" + path)) return;
                if (unknown) UnknownVersions++; else Damaged++;
                if (Kept.Count < 40) Kept.Add($"{what} {path}: {e.Message}");
            }
            Debug.LogWarning($"[RoFormat] {what} skipped ({(unknown ? "unknown version" : "damaged")}): {path}: {e.Message}");
        }

        public static bool Reported(string what, string path) { lock (Seen) return Seen.Contains(what + "|" + path); }
    }
}
