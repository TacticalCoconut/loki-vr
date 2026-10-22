using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Loki.Data
{
    /// <summary>
    /// Client wave 3b (w3data): where a data table is read from. The tables made from Gravity's data
    /// (<see cref="ClientDataBuilder.GeneratedFiles"/>) are built on the player's PC into the per-user data folder
    /// (&lt;UserSettings.Dir&gt;\data: %USERPROFILE%\AppData\LocalLow\Project Loki\Loki\data, or -lokiUserDir's) and read from there
    /// first; a development build may still carry a copy in StreamingAssets/data (the fallback), a release build carries none.
    /// Every other table (rAthena's, roBrowserLegacy's, Loki's own) ships in StreamingAssets/data.
    /// </summary>
    public static class ClientData
    {
        private static readonly HashSet<string> Generated = new HashSet<string>(ClientDataBuilder.GeneratedFiles, StringComparer.Ordinal);
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The per-user data folder the builder writes.</summary>
        public static string UserDataDir => Path.Combine(World.UserSettings.Dir, "data");

        /// <summary>StreamingAssets/data.</summary>
        public static string ShippedDir => Path.Combine(Application.streamingAssetsPath, "data");

        /// <summary>False while the first-run builder has not finished: a table read then would miss this run's files.</summary>
        public static volatile bool Ready;

        /// <summary>The file to read for a table path under data/ ("items.tsv", "account/titles.tsv", "fx/mob_attack_fx.json").</summary>
        public static string File(string rel)
        {
            string shipped = Path.Combine(ShippedDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Generated.Contains(rel)) return shipped;
            string user = Path.Combine(UserDataDir, rel.Replace('/', Path.DirectorySeparatorChar));
            bool mine = System.IO.File.Exists(user);
            string use = mine ? user : shipped;
            bool first;
            lock (Logged) first = Logged.Add(rel);
            if (first)
            {
                if (!Ready) Debug.LogWarning($"[Data] {rel} read before the game data was prepared");
                Debug.Log($"[Data] {rel} from {(mine ? "your data folder" : System.IO.File.Exists(shipped) ? "the build (StreamingAssets)" : "nowhere: missing")}: {use}");
            }
            return use;
        }
    }
}
