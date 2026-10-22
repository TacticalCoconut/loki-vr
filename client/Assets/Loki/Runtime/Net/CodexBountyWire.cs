#nullable disable // (Loki Host's tests compile this file with nullable on: tools/lokihost/Tests)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Loki.Net
{
    /// <summary>
    /// Wave 28 (wcxp): the Codex bounties' words between the client and the server's NPC "LokiCodex" (the generated rAthena script
    /// server/data/overlay/npc/custom/loki_codex.txt; Loki Host installs the same file: tools/lokihost/Core/CodexBounty.cs). design note,
    /// 2026-10-15/16: a SCAN places a bounty (the server times it from its own first sight of the creature near the character); a
    /// KILL completes it and fixes its amount ("the % is linked to the level you were at when you killed it"); the CHARACTER claims it
    /// in the Codex with a button; at the job's highest level at the claim it pays Zeny; a bracket whose every bounty is claimed has
    /// its own bonus and button; quest and event creatures are placed the moment they are seen and complete when they are gone.
    ///
    /// The client asks with a whisper to <see cref="Npc"/> (CZ_WHISPER 0x0096; rAthena runs the NPC's OnWhisperGlobal). The server
    /// answers with lines (dispbottom: ZC_NPC_CHAT 0x02c1 with the player's own id) that start with <see cref="Prefix"/>: the client
    /// keeps them out of the chat. The server alone decides and pays. Pure C#: Loki Host's tests link this file.
    /// </summary>
    public static class CodexBountyWire
    {
        /// <summary>The whisper's target: "NPC:" + Loki Host's CodexBounty.Npc (the two must stay equal).</summary>
        public const string Npc = "NPC:LokiCodex";
        public const string Prefix = "LCX1 ";
        public static string AskState() => "s";
        /// <summary>A creature of kind <paramref name="mob"/> came into sight: the server starts its scan clock (or places a quest / event
        /// creature's bounty at once: <see cref="Kind.PlacedAtSight"/>).</summary>
        public static string Seen(int mob) => "n#" + mob.ToString(CultureInfo.InvariantCulture);
        /// <summary>Nothing but the server's watch of quest / event creatures (the book opens).</summary>
        public static string Ping() => "p";
        /// <summary>A finished scan of creature <paramref name="mob"/>.</summary>
        public static string Place(int mob) => "b#" + mob.ToString(CultureInfo.InvariantCulture);
        /// <summary>A monster the player hit died (its unit id): the server checks it is the player's own last kill (killedgid).</summary>
        public static string Report(uint gid) => "x#" + gid.ToString(CultureInfo.InvariantCulture);
        /// <summary>The Codex's Claim button on a completed bounty.</summary>
        public static string Claim(int mob) => "c#" + mob.ToString(CultureInfo.InvariantCulture);
        /// <summary>The Codex's Claim button on a complete bracket.</summary>
        public static string ClaimBracket(int bracket) => "k#" + bracket.ToString(CultureInfo.InvariantCulture);

        public enum Kind { None, StateBegin, Open, Ready, Claimed, Brackets, StateEnd, Placed, Completed, Paid, BracketPaid, PlacedAtSight }
        public sealed class Line
        {
            public Kind Kind;
            /// <summary>Placed / PlacedAtSight / Completed / Paid: the creature; BracketPaid: the bracket; StateBegin: the protocol version.</summary>
            public int Id;
            /// <summary>StateBegin: how many creatures the server's Codex can meet.</summary>
            public int Count;
            /// <summary>StateBegin: the Zeny 1,000,000 Base EXP pays now (the character is at its job's highest level); 0: EXP is paid.</summary>
            public long ZenyPerMillion;
            /// <summary>Completed / Paid / BracketPaid: the amount (Base EXP, fixed by the kill or the bracket's completion).</summary>
            public long Exp;
            /// <summary>Paid / BracketPaid: the Zeny paid instead (the character at its job's highest level); 0: the EXP was paid.</summary>
            public long Zeny;
            /// <summary>Completed / Paid: the share of the bar, in 1/100 %.</summary>
            public int Share;
            public readonly List<int> Ids = new List<int>();
            /// <summary>Ready: each completed bounty's fixed amount.</summary>
            public readonly List<(int id, long exp)> Amounts = new List<(int, long)>();
            /// <summary>Brackets: claimed names, names, state (0 open, 1 complete: its bonus fixed, 2 paid), the fixed bonus.</summary>
            public readonly List<(int bracket, int claimed, int members, int state, long bonus)> Brackets = new List<(int, int, int, int, long)>();
        }

        /// <summary>Reads one server line; false when it is not one of ours (then it is an ordinary chat line).</summary>
        public static bool TryParse(string text, out Line line)
        {
            line = null;
            if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length < Prefix.Length + 1) return false;
            line = new Line();
            string rest = text.Substring(Prefix.Length + 1).Trim();
            var f = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (text[Prefix.Length])
            {
                case 'H': line.Kind = Kind.StateBegin; line.Id = Int(f, 0); line.Count = Int(f, 1); line.ZenyPerMillion = Long(f, 2); break;
                case 'O': line.Kind = Kind.Open; Ids(rest, line.Ids); break;
                case 'R':
                    line.Kind = Kind.Ready;
                    foreach (var t in rest.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = t.Split(':');
                        if (int.TryParse(p[0].Trim(), out int id) && id > 0) { line.Ids.Add(id); line.Amounts.Add((id, p.Length > 1 && long.TryParse(p[1].Trim(), out long e) ? e : 0)); }
                    }
                    break;
                case 'D': line.Kind = Kind.Claimed; Ids(rest, line.Ids); break;
                case 'E': line.Kind = Kind.StateEnd; break;
                case 'B': line.Kind = Kind.Placed; line.Id = Int(f, 0); break;
                case 'P': line.Kind = Kind.PlacedAtSight; line.Id = Int(f, 0); break;
                case 'W': line.Kind = Kind.Completed; line.Id = Int(f, 0); line.Exp = Long(f, 1); line.Share = Int(f, 2); break;
                case 'C': line.Kind = Kind.Paid; line.Id = Int(f, 0); line.Exp = Long(f, 1); line.Share = Int(f, 2); line.Zeny = Long(f, 3); break;
                case 'K': line.Kind = Kind.BracketPaid; line.Id = Int(f, 0); line.Exp = Long(f, 1); line.Zeny = Long(f, 2); break;
                case 'G':
                    line.Kind = Kind.Brackets;
                    foreach (var g in rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = g.Split(':');
                        if (p.Length >= 4 && int.TryParse(p[0], out int b) && int.TryParse(p[1], out int c) && int.TryParse(p[2], out int n) && int.TryParse(p[3], out int st))
                            line.Brackets.Add((b, c, n, st, p.Length > 4 && long.TryParse(p[4], out long bonus) ? bonus : 0));
                    }
                    break;
                default: line.Kind = Kind.None; break; // (a newer server's line: ours, kept out of the chat, not understood)
            }
            return true;
        }
        private static void Ids(string s, List<int> into) { foreach (var t in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(t.Trim(), out int v) && v > 0) into.Add(v); }
        private static int Int(string[] f, int i) => i < f.Length && int.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static long Long(string[] f, int i) => i < f.Length && long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

        // ------------------------------------------------------------------ the server's numbers (loki_codex.txt S_Exp), for the book
        /// <summary>Danger multiplier for flat bounty EXP (0 normal: 1x, 1 boss: 2x, 2 MVP: 4x).</summary>
        public static int DangerMultiplier(int danger) => danger == 2 ? 4 : danger == 1 ? 2 : 1;
        /// <summary>The flat Base EXP a creature's bounty pays, determined solely by creature level and danger rank (0 normal, 1 boss, 2 MVP),
        /// independent of the player's level or NextBaseExp bar (design note, 2026-10-17 item 57).</summary>
        public static long FlatExp(int creatureLevel, int danger)
        {
            long lv = Math.Max(1, creatureLevel);
            return (lv * lv * 15L + lv * 35L) * DangerMultiplier(danger);
        }
        /// <summary>The flat Base EXP bonus a 10-level bracket pays when every creature in it is claimed (5x the normal bounty at the bracket's top level).</summary>
        public static long FlatBracketBonus(int bracket) => FlatExp(Math.Max(1, bracket) * 10, 0) * 5L;

        /// <summary>Legacy share floor by danger (0 normal, 1 boss, 2 MVP), in 1/100 %.</summary>
        public static int Floor(int danger) => danger == 2 ? 350 : danger == 1 ? 300 : 200;
        public const int Cap = 400;
        /// <summary>Legacy share helper kept for backwards compatibility.</summary>
        public static int Share(int creatureLevel, int danger, int playerLevel)
        {
            int f = Floor(danger), g = creatureLevel - playerLevel;
            if (g >= -6) return f + (Cap - f) * Math.Min(20, g + 10) / 20;
            int s6 = f + (Cap - f) * 4 / 20, k = Math.Min(10, -g - 6);
            return s6 - (s6 - Low(danger)) * k / 10;
        }
        public static int Low(int danger) => Floor(danger) / 20;
        public static long Amount(long bar, int share) => Math.Max(1L, bar * share / 10000);
        /// <summary>The Zeny an amount pays at the job's highest level, as the state's ZenyPerMillion tells it (the book's label).</summary>
        public static long Zeny(long exp, long zenyPerMillion) => Math.Max(1L, exp * zenyPerMillion / 1000000);
        /// <summary>The bracket of a creature level: 1 for 1-10 (and level 0), 2 for 11-20, ...</summary>
        public static int BracketOf(int level) => Math.Max(1, (level - 1) / 10 + 1);
        public static int BracketFrom(int bracket) => 10 * bracket - 9;
        public static int BracketTo(int bracket) => 10 * bracket;
        /// <summary>"3%" / "3.5%" / "3.25%" (a share in 1/100 %).</summary>
        public static string Percent(int share) => (share / 100.0).ToString(share % 100 == 0 ? "0" : share % 10 == 0 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + "%";
    }
}
