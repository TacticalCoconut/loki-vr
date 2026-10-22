using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3status: other units' ailments and looks (the re-audit's §2d). The opt1 / opt2 / opt3 arms are in
    /// EntityManager (entry packets, 0x0229, 0x028a) and feed Sprites/UnitState; this part routes the status icons of every unit in
    /// view (0x0983 / 0x0196 / 0x0984, parsed by Progression.AnyEffectChanged) to it, and plays Hiding's puff. Smoke:
    /// LokiGame.W3StatusSmoke.cs (-lokiSmokeOnly w3status-host / w3status-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW3Status(LokiBootstrap boot)
        {
            UnitState.Assets = boot.Assets;
            Prog.AnyEffectChanged += OnW3StatusEfst;
        }

        /// <summary>A status icon on any unit in view (ourselves included). A unit not in view is ignored, as roBrowserLegacy does
        /// (Entity.js:1958-1964): the server re-sends what shows on entry (0x0984 for DisplayPc statuses, the entry packet's opt bits).</summary>
        private void OnW3StatusEfst(uint aid, int type, bool on, int val1)
        {
            var u = Entities.Get(aid);
            if (u == null) return;
            // EFST_HIDING: roBrowserLegacy Entity.js:1988-1999 spams EF_BASH (16, the effect table's "Hide") when it starts and
            // EF_SUMMONSLAVE (215, the "smoke" sprite) when it ends; the status goes to the area for players (status.cpp:16419-16423)
            if (type == UnitState.EfstHiding && Effects != null)
            {
                string fx = on ? "16" : "215";
                int parts = Effects.Spam(fx, aid, null, Time.time, -1f, 0);
                Debug.Log($"[W3Status] {aid}: hiding {(on ? "starts" : "ends")} → effect {fx} ({parts} drawable part(s))");
            }
            UnitState.Of(u).OnEfst(type, on, val1);
        }
    }
}
