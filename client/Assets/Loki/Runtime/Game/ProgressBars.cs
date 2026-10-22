using System;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// NPC script progress bars (rAthena e985006, PACKETVER 20211103):
    ///   0x02f0 ZC_PROGRESS color.L seconds.L — script command `progressbar` (script.cpp:22520-22540): the script STOPs, the player
    ///          is blocked (WIP_DISABLE_ALL) and the server waits for CZ_PROGRESS 0x02f1 (2 bytes). The client draws a cast-like bar
    ///          over its own character and sends 0x02f1 when it is full (clif.cpp:11321-11352: sent at or after the timeout the
    ///          script goes on; sent early, the script is closed).
    ///   0x02f2 ZC_PROGRESS_CANCEL — the server aborts it when the player walks, uses an item or is hit (clif.cpp:11390-11392,
    ///          pc.cpp:6465-6468, 9697-9698). The client removes the bar and answers 0x02f1 at once; the server then ends the script
    ///          (the "closing" branch). roBrowserLegacy does the same: EntityCast.clean() runs the completion callback.
    ///   0x09d1 ZC_PROGRESS_ACTOR id.L color.L seconds.L — `progressbar_npc`: a bar over an NPC, sent to the area and to players
    ///          who see the NPC later with the time left (clif.cpp:11354-11373). The script sleeps; nothing is answered.
    /// Colours: the scripts pass "RRGGBB" to strtol(…, 0), which reads plain hex as decimal and stops at the first letter, and
    /// rAthena's documentation says the client ignores the colour and draws green (doc/script_commands.txt progressbar) — green it is.
    /// </summary>
    public sealed class ProgressBars
    {
        private readonly RoSession _s; private readonly Func<Unit> _self; private readonly Func<uint, Unit> _unit;
        private float _end = -1f;
        public static readonly Color Green = new Color(0.3f, 0.95f, 0.35f, 1f);
        /// <summary>A bar of ours is running (the server waits for CZ_PROGRESS).</summary>
        public bool Active => _end > 0;
        public float Remaining => Active ? Mathf.Max(0f, _end - Time.realtimeSinceStartup) : 0f;
        /// <summary>CZ_PROGRESS goes this long after the bar's time, measured in real time from the packet: the server compares its own
        /// clock strictly (gettick() < timeout = the script's tick + seconds, clif.cpp:11333) and an answer even a frame early ends the
        /// script as a cancel. Smoke 2026-09-27: answered at the frame the bar filled, rAthena closed the script.</summary>
        public const float CompleteMargin = 0.25f;
        /// <summary>For the smoke: what happened last ("done", "cancelled") and how many CZ_PROGRESS were sent.</summary>
        public string LastOutcome { get; private set; } = ""; public int Answers { get; private set; }
        public int ActorBars { get; private set; }
        public event Action<string> Message;

        public ProgressBars(RoSession s, Func<Unit> self, Func<uint, Unit> unit) { _s = s; _self = self; _unit = unit; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x02f0:
                {
                    r.Skip(2); uint color = r.U32(); int secs = r.S32();
                    _end = Time.realtimeSinceStartup + Mathf.Max(0, secs) + CompleteMargin;
                    _self()?.Overhead?.SetCast("", Mathf.Max(0.06f, secs), Green);
                    Debug.Log($"[Progress] bar for {secs} s (colour 0x{color:x6})");
                    Message?.Invoke(Lang.T("Working… (moving cancels it)"));
                    return true;
                }
                case 0x02f2:
                {
                    Debug.Log($"[Progress] cancelled by the server{(Active ? $" with {Remaining:F1} s left" : " (no bar running)")}");
                    _self()?.Overhead?.ClearCast();
                    if (Active) Finish("cancelled");
                    return true;
                }
                case 0x09d1:
                {
                    r.Skip(2); uint aid = r.U32(); uint color = r.U32(); int secs = r.S32();
                    var u = _unit(aid);
                    u?.Overhead?.SetCast("", Mathf.Max(0.06f, secs), Green);
                    ActorBars++;
                    Debug.Log($"[Progress] bar over {aid} ({u?.Name ?? "not in view"}) for {secs} s");
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>Call every frame: a full bar answers the server.</summary>
        public void Tick() { if (Active && Time.realtimeSinceStartup >= _end) Finish("done"); }

        private void Finish(string outcome)
        {
            _end = -1f; LastOutcome = outcome; Answers++;
            _s.SendPacket(new PacketWriter(0x02f1).ToArray()); // CZ_PROGRESS
            Debug.Log($"[Progress] {outcome}: CZ_PROGRESS sent");
        }
    }
}
