using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 10, w10move: smoothed movement, client only (design note, 2026-10-05: "looks and feels free"). The pieces:
    /// Sprites/Unit.Smooth.cs (every unit's drawn position: <see cref="Unit.DrawnCell"/>, while <see cref="Unit.Cell"/> stays the
    /// logical position every rule reads), Game/StickSteer.cs (the stick steers in any direction) and the setting
    /// Settings → Comfort → "Movement" (VR/Comfort.cs SmoothMove, loki-user.json smoothMove; Smooth is the default, Classic is
    /// the drawing and the stick of before). No packet, no server rule and no reader of a unit's logical cell changes.
    /// Called once from LokiGame's setup, after InitW5Move (it hands the legs' tracking of the server's unit to the stick).
    /// </summary>
    public sealed partial class LokiGame
    {
        private int _freeSkillSeen = -1; private float _freeSkillAt = -10f;

        /// <summary>Free movement (wfree): the player is doing something a walk request would end or spoil (rA unit.cpp:873-874 ends an
        /// attack; a skill being cast or walked to, a pick up, a talk, taming, sitting): the settle after the stick gives way.</summary>
        private bool FreeBusy()
        {
            if (Session != null && Session.SkillRequestsSent != _freeSkillSeen) { if (_freeSkillSeen >= 0) _freeSkillAt = Time.time; _freeSkillSeen = Session.SkillRequestsSent; }
            var self = Entities?.Self;
            return _attackTarget != 0 || _pendingPickup != 0 || _pendingNpc != 0 || _pendingCapture != 0 || _pendingSkill != null || Time.time - _freeSkillAt < 1.5f
                || (self != null && self.Sprite != null && self.Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Sit);
        }

        private void InitW10Move()
        {
            // the steering stick chooses its targets from where the SERVER's unit is (Game/RouteWalker.cs tracks it from our own
            // 0x0087 / 0x0088 packets on the server's clock), never from the drawn body
            if (Target != null)
            {
                Target.Routes = W3WalkRoutes;
                // the stick takes over as a Classic stick request does (LokiGame.cs, the walk callback handed to Targeting.Init)
                Target.StickTookOver = () => { _pendingPickup = _pendingNpc = 0; _attackTarget = 0; W3WalkCancel("stick"); if (Entities.Self != null) Entities.Self.FreeGoalCell = null; };
                // free movement (wfree): the settle after the stick (Game/StickSteer.cs) gives way to anything the player does, and puts the
                // facing others see right afterwards (CZ_CHANGE_DIRECTION2, as LokiGame.Wave2Combat.cs TurnTowards sends it)
                Target.Steer.Busy = FreeBusy;
                Target.Steer.Face = clientDir =>
                {
                    if (Session == null || Session.State != Net.RoSession.Stage.InGame) return;
                    Session.SendPacket(new Net.PacketWriter(0x0361).U16(0).U8((byte)Unit.ToServerDir(clientDir)).ToArray());
                    LastTurn = (Unit.ToServerDir(clientDir), 0);
                };
            }
        }
    }
}
