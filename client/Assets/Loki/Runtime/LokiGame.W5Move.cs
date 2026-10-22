using Loki.Game;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5, w5move: the walking leftovers of waves 3 / 3b. The attack chase in legs (Game/AttackChase.cs, Sprites/RoChase.cs)
    /// is wired here; the fast-move look (a Monk's held attack frame during Body Relocation) lives in Sprites/FastMoveLook.cs and
    /// EntityManager's slide arm. Called once from LokiGame's setup, after InitW3Walk (it uses W3WalkRoutes).
    /// </summary>
    public sealed partial class LokiGame
    {
        public AttackChase W5MoveChase { get; private set; }

        private void InitW5Move()
        {
            W5MoveChase = gameObject.AddComponent<AttackChase>();
            W5MoveChase.Self = () => Entities.Self;
            W5MoveChase.Gat = () => _boot.Map.Current?.Gat;
            W5MoveChase.Find = id => Entities.Get(id);
            W5MoveChase.Routes = W3WalkRoutes;
            W5MoveChase.SendAttack = id => Session.Attack(id);
            W5MoveChase.Target = () => _attackTarget;
            W5MoveChase.Range = () => Mathf.Max(1, Prog.AttackRange);
            W5MoveChase.Tell = m => { Hud?.Notify(m); Chat(m, "ffb070"); }; // client wave 11: in front of the player
            Entities.Vanished += W5MoveChase.OnVanished;
            Entities.Damage += (src, dst, amount, crit) => W5MoveChase.OnDamage(src, dst);
            if (W3WalkRoutes != null) W3WalkRoutes.Ended += W5MoveChase.OnTripEnded;
        }

        /// <summary>ZC_ATTACK_FAILURE_FOR_DISTANCE 0x0139 targetAID.L targetX.W targetY.W x.W y.W range.W (clif_movetoattack, rA
        /// clif.cpp:8172-8186): our target is out of range; at most one chase per 0.35 s (rAthena answers every attack request and
        /// every step's retry, unit.cpp:439-496).</summary>
        private void W5MoveTooFar(uint target, int tx, int ty, int x, int y, int range)
        {
            if (target == 0 || target != _attackTarget || Time.time < _nextChase) return;
            _nextChase = Time.time + 0.35f;
            if (W5MoveChase != null) W5MoveChase.OnTooFar(target, tx, ty, x, y, range);
            else { Session.WalkTo(tx, ty); Session.Attack(target); } // before InitW5Move (no VR rig): the old chase
        }

        /// <summary>A click to attack a monster / an enemy player: the chase walks round first when rAthena would drop the request.</summary>
        private void W5MoveAttack(Unit u)
        {
            if (W5MoveChase == null || !W5MoveChase.OnAttackClick(u)) Session.Attack(u.Id);
        }
    }
}
