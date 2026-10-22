using Loki.Game;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// A target skill out of range: walk into range first, then cast — as the official client and roBrowserLegacy do (rB
    /// Engine/MapEngine/Skill.js:664-775 onUseSkill: a path towards the target that stops within the skill's range; in range the
    /// packet goes at once, else it is kept (Session.moveAction) and the character walks, the packet sent on arrival).
    /// rAthena does not walk a player into range: unit_skilluse_id2 (unit.cpp:2339-2365) drops a request whose target is beyond the
    /// skill's range (battle_check_range, see below) and only remembers one sent while already walking (its "stepaction",
    /// :2345-2351). Before this Loki sent the cast from where the character stood, so every target skill on something further than
    /// its range did nothing (found by the test of the design's 2026-10-01 report: the Thief training's Envenom, range 2, on a Poring in the
    /// Academy cage three cells away).
    /// The cell: the nearest walkable one within range of the target that rAthena's own path search reaches (Sprites/RoPathSearch.cs,
    /// 32 steps: a cage's inside is walkable but shut in); a target further away than a path search reaches gets the nearest
    /// walkable cell in range and the walk in legs (LokiGame.W3Walk.cs) routes it. The cast is sent once within range; a target that
    /// moved is followed while the wait lasts (<see cref="SkillWalkSeconds"/>); a new click or walk cancels it.
    ///
    /// Client wave 14 (w14play), point 27 of the design's headset list (Soul Destroyer chosen on a wheel with the target looked at: "the
    /// character will simply walk up to a location relatively close to it, and then do nothing"). Two faults, both read in rAthena's
    /// source (the oracle's src/map) and in the headset log (playtest-headset.log ~10196-10240: five walk-then-casts of ASC_BREAKER,
    /// range 4, none answered; three cast without a walk, all answered):
    ///  - "in range" was the square (Chebyshev) distance. rAthena's range for a PLAYER's skill is battle_check_range (battle.cpp:
    ///    8215-8240): without CIRCULAR_AREA (config/core.hpp:39, commented out) a player gets check_distance_client_bl, i.e.
    ///    distance_client(dx, dy) = (int)(sqrt(dx² + dy²) − 0.1) &lt;= range (path.cpp:485-521), and from 2 cells on the straight line
    ///    path_search_long(CELL_CHKWALL) — exactly the attack's rule (Sprites/RoChase.cs CanAttackFrom). So the walk stopped on cells
    ///    such as (194,305) for a target on (198,309): square distance 4, circle 5 — out of range, and the request was dropped without
    ///    an answer (unit_skilluse_id2 returns 0, clif_parse_UseSkillToId sends nothing). Now the cell, and the moment, use that rule.
    ///  - the cast went when the DRAWN body reached the range, ~0.1 s before the server's unit did (walk accepted at 3004.513 for
    ///    1701 ms, the cast at 3006.127). rAthena keeps such a request while it walks (stepaction, unit.cpp:2344-2351: tried again half a
    ///    step after each step until the target is within range − 1, and once when the walk ends, unit.cpp:722-735 / 439-496), which
    ///    covers it while the walk's END is in range; it is not when the target moved meanwhile. Now the cast is sent from the
    ///    SERVER's cell (Game/RouteWalker.cs SrvCell / SrvWalking, the same tracking the legs use): standing, within the range; still
    ///    walking, within range − 1 (rAthena then casts at once, unit.cpp:2345).
    /// The look and the laser give the same unit to CastArmed (LokiGame.cs OnFreeTrigger / OnTargetClicked, LokiGame.Wheels.cs
    /// UseWheelSlot, LokiGame.W12Cast.cs W12TakeSkill): the walk-then-cast keeps the unit's id, so a look that moves away while
    /// walking does not change it.
    /// </summary>
    public sealed partial class LokiGame
    {
        public const float SkillWalkSeconds = 12f;
        private Progression.SkillEntry _pendingSkill; private int _pendingSkillLv; private uint _pendingSkillTarget; private float _pendingSkillUntil;
        private static readonly RoPathSearch.WalkPath SkillPath = new RoPathSearch.WalkPath();
        private Loki.Formats.GatFile _skillWallsOf; private RoChase.Walls _skillWalls;
        /// <summary>Casts sent after a walk into range (smoke, log).</summary>
        public int SkillWalkCasts { get; private set; }
        /// <summary>The last walk-then-cast: the server's cell it was sent from, whether that unit still walked, the target's cell
        /// (smoke, log).</summary>
        public (Vector2Int from, bool walking, Vector2Int target) SkillWalkLastCast { get; private set; }

        /// <summary>rAthena's range test for a player's skill on a unit (battle_check_range: the client's circle, and the straight line
        /// from 2 cells on), from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private bool SkillReaches(Loki.Formats.GatFile gat, Vector2Int from, Vector2Int to, int range)
        {
            if (_skillWallsOf != gat) { _skillWallsOf = gat; _skillWalls = new RoChase.Walls(gat.Width, gat.Height, (x, y) => (int)gat.Cells[x + y * gat.Width].Type); }
            return RoChase.CanAttackFrom(_skillWalls, from.x, from.y, to.x, to.y, range);
        }

        /// <summary>The range rAthena checks: the skill list's (clif_skillinfoblock: skill_get_range2(..., false), clif.cpp:5738), which
        /// unit_skilluse_id2 asks with isServer true: the db's base range is cut to 14 there (skill.cpp skill_get_range2: "Server-sided
        /// base range can't be above 14"; five skills of db/re/skill_db.yml have 15 or 18), then Vulture's Eye / Snake Eye etc. are added.
        /// The client sees only the sum, so it is cut to 14: exact for every skill without such a bonus, and only closer than needed
        /// (never out of range) for a bonus that takes a skill past 14.</summary>
        private static int SkillServerRange(Progression.SkillEntry k) => Mathf.Min(k.Range, 14);

        /// <summary>The server's unit's cell and whether it walks now (Game/RouteWalker.cs); without the walker the drawn cell.</summary>
        private Vector2Int SkillServerCell(out bool walking)
        {
            walking = false;
            if (W3WalkRoutes == null) return Vector2Int.RoundToInt(Entities.Self.Cell);
            W3WalkRoutes.SrvTrack(Entities.Self);
            walking = W3WalkRoutes.SrvWalking;
            return W3WalkRoutes.SrvCell;
        }

        /// <summary>True when the target is out of the skill's range: a walk into range started (or none is possible: said so) and
        /// the cast waits; false when in range (the caller casts now).</summary>
        private bool SkillWalkIntoRange(Progression.SkillEntry k, int lv, Unit target)
        {
            int range = SkillServerRange(k);
            if (range <= 0 || target == null || target.IsSelf || Entities.Self == null || _boot.Map.Current == null) return false;
            var gat = _boot.Map.Current.Gat;
            var me = SkillServerCell(out bool walking); var t = Vector2Int.RoundToInt(target.Cell);
            // (while the server's unit walks, rAthena casts at once only within range − 1; otherwise it keeps the request for the walk's
            // steps: a walk that goes elsewhere would end out of range, so the walk is replaced by one into range)
            if (SkillReaches(gat, me, t, walking ? range - 1 : range)) return false;
            Vector2Int? reached = null, nearest = null; int bestLen = int.MaxValue; float nearestD = float.MaxValue;
            int r1 = range + 1; // (the circle reaches range + 1 cells along a row or a column: distance_client(5, 0) = 4)
            for (int dy = -r1; dy <= r1; dy++)
                for (int dx = -r1; dx <= r1; dx++)
                {
                    int x = t.x + dx, y = t.y + dy;
                    if (!gat.InBounds(x, y) || !gat.Get(x, y).Walkable) continue;
                    var c = new Vector2Int(x, y);
                    if (!SkillReaches(gat, c, t, range)) continue;
                    float d = Vector2.Distance(new Vector2(x, y), me);
                    if (d < nearestD) { nearestD = d; nearest = c; }
                    if (RoPathSearch.Search(SkillPath, gat, me.x, me.y, x, y, 0) && SkillPath.Len < bestLen) { bestLen = SkillPath.Len; reached = c; }
                }
            // nothing in range reachable by a path search: a far target goes to the walk in legs; a near one is shut off (a cage)
            var dest = reached ?? (nearestD > 30f ? nearest : null);
            if (dest == null)
            {
                Hud?.Notify(Lang.F("{0}: can't get in range from here.", k.Info.Name)); // client wave 11: in front of the player (it was a wrist toast: unseen)
                Debug.Log($"[Skill] {k.Info.Aegis} on {target.Id} at {t}: out of range {range} and no reachable cell in range from {me}");
                return true;
            }
            _pendingSkill = k; _pendingSkillLv = lv; _pendingSkillTarget = target.Id; _pendingSkillUntil = Time.time + SkillWalkSeconds;
            Debug.Log($"[Skill] {k.Info.Aegis} on {target.Id} at {t}: out of range {range} from {me} (the server's cell{(walking ? ", walking" : "")}; circle {RoChase.DistanceClient(me.x - t.x, me.y - t.y)}), walking to {dest.Value} first (circle {RoChase.DistanceClient(dest.Value.x - t.x, dest.Value.y - t.y)})");
            W3WalkStart(dest.Value, "skill");
            return true;
        }

        private void CancelPendingSkill(string why)
        {
            if (_pendingSkill == null) return;
            Debug.Log($"[Skill] the walk-then-cast of {_pendingSkill.Info.Aegis} called off: {why}");
            _pendingSkill = null;
        }

        private void UpdatePendingSkill()
        {
            if (_pendingSkill == null || Entities.Self == null || _boot.Map.Current == null) return;
            var u = Entities.Get(_pendingSkillTarget);
            if (u == null || Player.Dead) { CancelPendingSkill(u == null ? "the target is gone" : "dead"); return; }
            var me = SkillServerCell(out bool walking); var t = Vector2Int.RoundToInt(u.Cell);
            // client wave 14 (w14play), point 27: from the server's cell with rAthena's rule; while it still walks, within range − 1
            int range = SkillServerRange(_pendingSkill);
            if (SkillReaches(_boot.Map.Current.Gat, me, t, walking ? range - 1 : range))
            {
                var k = _pendingSkill; _pendingSkill = null;
                W3WalkCancel("skill"); SkillWalkCasts++; SkillWalkLastCast = (me, walking, t);
                Debug.Log($"[Skill] in range of {u.Id} at {t} from the server's cell {me}{(walking ? " (still walking)" : "")} (circle {RoChase.DistanceClient(me.x - t.x, me.y - t.y)}, range {range}): cast {k.Info.Aegis} lv {_pendingSkillLv}");
                Session.UseSkill(k.Id, _pendingSkillLv, u.Id);
                return;
            }
            if (Time.time > _pendingSkillUntil) { CancelPendingSkill("not in range in time"); Hud?.Notify(Lang.T("Couldn't get within range.")); return; }
            // arrived and still out of range (the target moved): plan again
            if (!W3WalkActive && !walking && !Entities.Self.Walking) { var k = _pendingSkill; float until = _pendingSkillUntil; _pendingSkill = null; if (SkillWalkIntoRange(k, _pendingSkillLv, u) && _pendingSkill != null) _pendingSkillUntil = until; }
        }
    }
}
