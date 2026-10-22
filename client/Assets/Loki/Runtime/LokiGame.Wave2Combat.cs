using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>Client wave 2, combat feedback: combos, spirit spheres / charms / souls / shields / scars, blade stop (CombatExtras.cs), and
    /// turning in place: the pointer-hand stick click turns the character towards what the laser points at (DioramaRig.TurnRequested) →
    /// CZ_CHANGE_DIRECTION2 0x0361 headDir.W dir.B (the unshuffled id of 2018+ clients, clif_shuffle.hpp:4722-4731); rAthena checks nothing
    /// and sends ZC_CHANGE_DIRECTION 0x009c to everyone else in the area — not back to us, so our sprite turns here (clif.cpp:11581-11620).</summary>
    public sealed partial class LokiGame
    {
        public CombatExtras CombatExtras { get; private set; }
        /// <summary>Last CZ_CHANGE_DIRECTION sent: (server body direction, head direction) — smoke test.</summary>
        public (int dir, int head) LastTurn = (-1, -1);

        private void BeginWave2Combat(LokiBootstrap boot)
        {
            CombatExtras = new CombatExtras(Entities, () => Session.AccountId, () => Session.LastSkillId);
            Hud.Wrist.StatusExtra = () => CombatExtras.SelfLine();
            if (boot.Rig != null) boot.Rig.TurnRequested += (t, cell) =>
            {
                if (t != null && t.Unit != null) TurnTowards(Vector2Int.RoundToInt(t.Unit.Cell));
                else if (t != null && t.Item != null) TurnTowards(t.Item.Cell);
                else if (cell.HasValue) TurnTowards(cell.Value);
                else if (Target?.GazeCell != null) TurnTowards(Target.GazeCell.Value);
            };
        }

        private bool HandleWave2Combat(ushort id, byte[] b, int o, int n) => CombatExtras != null && CombatExtras.Handle(id, b, o, n);

        private void OnMapChangeWave2Combat(string map) { CombatExtras?.Clear(); Expansion?.Reset(); }

        /// <summary>Turns our character in place to face a cell (no walking): the eight RO directions, head straight.</summary>
        public bool TurnTowards(Vector2Int cell)
        {
            if (W8HordeBlocksWorld) return false; // client wave 8 (w8game): the Horde arena is shown (LokiGame.W8Horde.cs)
            var self = Entities?.Self;
            if (self == null || Session == null || Session.State != RoSession.Stage.InGame || Player.Dead) return false;
            var d = new Vector2(cell.x, cell.y) - self.Cell;
            if (d.sqrMagnitude < 0.25f) return false;
            int clientDir = Unit.DirFromDelta(d.normalized * 2f);
            int serverDir = Unit.ToServerDir(clientDir);
            Session.SendPacket(new PacketWriter(0x0361).U16(0).U8((byte)serverDir).ToArray());
            self.Sprite.Direction = clientDir; self.Sprite.HeadDir = 0;
            LastTurn = (serverDir, 0);
            Debug.Log($"[Game] turn to face {cell}: server dir {serverDir}");
            return true;
        }
    }
}
