#nullable disable // (Loki Host's tests compile this file with nullable on: tools/lokihost/Tests)
using System;
using System.Collections.Generic;
using Loki.Net;

namespace Loki.Game
{
    /// <summary>
    /// Wave 28 (wcxp): what the SERVER says of this CHARACTER's Codex bounties (Net/CodexBountyWire.cs; design note, 2026-10-16: bounties belong
    /// to the character, the Codex's entries to the account): open (scanned, or a quest / event creature seen), completed (killed: its
    /// amount fixed, waiting for the Claim button), claimed; each 10-level bracket's count and bonus; whether this server has bounties
    /// at all, and whether a claim now pays Zeny (the character at its job's highest level). The client keeps no bounty of its own:
    /// each entry into the world asks again ("s").
    /// </summary>
    public sealed class CodexBounty
    {
        /// <summary>The server answered a state request: it has the NPC (Loki Host, or the module with its overlay).</summary>
        public bool Supported { get; private set; }
        /// <summary>The whisper was answered "not online": this server has no bounties (any other rAthena). Until the next login.</summary>
        public bool Unsupported { get; private set; }
        /// <summary>A state reply is being read ("H" seen, "E" not yet).</summary>
        public bool Reading { get; private set; }
        public int ServerCreatures { get; private set; }
        /// <summary>The Zeny 1,000,000 Base EXP pays now; 0 while the character can still gain EXP.</summary>
        public long ZenyPerMillion { get; private set; }
        private readonly HashSet<int> _open = new HashSet<int>(), _claimed = new HashSet<int>();
        private readonly Dictionary<int, long> _ready = new Dictionary<int, long>();
        private readonly Dictionary<int, (int claimed, int members, int state, long bonus)> _brackets = new Dictionary<int, (int, int, int, long)>();

        public bool IsOpen(int mob) => _open.Contains(mob);
        public bool IsReady(int mob) => _ready.ContainsKey(mob);
        public bool IsClaimed(int mob) => _claimed.Contains(mob);
        /// <summary>A completed bounty's fixed amount (Base EXP); 0 when it is not completed.</summary>
        public long ReadyAmount(int mob) => _ready.TryGetValue(mob, out var e) ? e : 0;
        public bool Any(int mob) => IsOpen(mob) || IsReady(mob) || IsClaimed(mob);
        public int OpenCount => _open.Count;
        public int ReadyCount => _ready.Count;
        public int ClaimedCount => _claimed.Count;
        /// <summary>A bracket as the server last told it: claimed names, names (0 = no such bracket), state (0 open, 1 complete: its bonus
        /// fixed and waiting for its button, 2 paid), the fixed bonus.</summary>
        public (int claimed, int members, int state, long bonus) Bracket(int b) => _brackets.TryGetValue(b, out var v) ? v : (0, 0, 0, 0);
        public IEnumerable<int> Brackets => _brackets.Keys;

        /// <summary>Anything changed (the book redraws).</summary>
        public event Action Changed;
        /// <summary>A bounty was placed (the answer to a scan): its creature.</summary>
        public event Action<int> Placed;
        /// <summary>A quest / event creature's bounty was placed the moment it was seen (no scan): its creature.</summary>
        public event Action<int> PlacedAtSight;
        /// <summary>A kill completed a bounty: creature, the fixed EXP, share (1/100 %).</summary>
        public event Action<int, long, int> Completed;
        /// <summary>A Claim paid a bounty: creature, EXP, share, Zeny (paid instead of the EXP when not 0).</summary>
        public event Action<int, long, int, long> Paid;
        /// <summary>A bracket became complete (its bonus fixed): bracket, bonus.</summary>
        public event Action<int, long> BracketReady;
        /// <summary>A bracket's Claim paid: bracket, EXP, Zeny (paid instead when not 0).</summary>
        public event Action<int, long, long> BracketPaid;
        /// <summary>Lines taken (smoke, log).</summary>
        public int Lines { get; private set; }

        /// <summary>A new login: everything forgotten until the server's state comes.</summary>
        public void Reset() { Supported = false; Unsupported = false; Reading = false; ZenyPerMillion = 0; _open.Clear(); _ready.Clear(); _claimed.Clear(); _brackets.Clear(); Changed?.Invoke(); }
        public void MarkUnsupported() { if (Supported) return; Unsupported = true; Changed?.Invoke(); }

        public void Take(CodexBountyWire.Line l)
        {
            if (l == null) return;
            Lines++;
            switch (l.Kind)
            {
                case CodexBountyWire.Kind.StateBegin:
                    Supported = true; Unsupported = false; Reading = true; ServerCreatures = l.Count; ZenyPerMillion = l.ZenyPerMillion;
                    _open.Clear(); _ready.Clear(); _claimed.Clear(); _brackets.Clear();
                    break;
                case CodexBountyWire.Kind.Open: foreach (var i in l.Ids) { _open.Add(i); _ready.Remove(i); _claimed.Remove(i); } break;
                case CodexBountyWire.Kind.Ready: foreach (var (i, e) in l.Amounts) { _ready[i] = e; _open.Remove(i); _claimed.Remove(i); } break;
                case CodexBountyWire.Kind.Claimed: foreach (var i in l.Ids) { _claimed.Add(i); _open.Remove(i); _ready.Remove(i); } break;
                case CodexBountyWire.Kind.Brackets:
                    foreach (var (b, c, n, st, bonus) in l.Brackets)
                    {
                        int was = Bracket(b).state;
                        _brackets[b] = (c, n, st, bonus);
                        if (!Reading && st == 1 && was == 0) BracketReady?.Invoke(b, bonus);
                    }
                    break;
                case CodexBountyWire.Kind.StateEnd: Reading = false; break;
                case CodexBountyWire.Kind.Placed: Supported = true; if (_open.Add(l.Id)) Placed?.Invoke(l.Id); break;
                case CodexBountyWire.Kind.PlacedAtSight: Supported = true; if (_open.Add(l.Id)) PlacedAtSight?.Invoke(l.Id); break;
                case CodexBountyWire.Kind.Completed: Supported = true; _open.Remove(l.Id); _ready[l.Id] = l.Exp; Completed?.Invoke(l.Id, l.Exp, l.Share); break;
                case CodexBountyWire.Kind.Paid: Supported = true; _open.Remove(l.Id); _ready.Remove(l.Id); _claimed.Add(l.Id); Paid?.Invoke(l.Id, l.Exp, l.Share, l.Zeny); break;
                case CodexBountyWire.Kind.BracketPaid:
                    { var b = Bracket(l.Id); _brackets[l.Id] = (b.claimed, b.members, 2, 0); BracketPaid?.Invoke(l.Id, l.Exp, l.Zeny); }
                    break;
            }
            if (!Reading || l.Kind == CodexBountyWire.Kind.StateEnd) Changed?.Invoke();
        }
    }
}
