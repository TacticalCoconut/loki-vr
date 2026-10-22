using System.Collections.Generic;
using Loki.Sprites;
using Loki.VR;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Look-to-target and stick movement:
    ///  • Gaze lock: the monster, NPC, portal, item, player or own companion nearest the centre of your view (within a small
    ///    cone, close to your character) is locked and ringed on the ground — red monster (and attackable player), green NPC,
    ///    cyan portal, yellow item, violet player, teal companion. Pulling the trigger with the laser off any UI/unit acts on
    ///    it, so you never have to aim the laser (a player opens the player menu). Client wave 19 (w19small), point 39: not while the
    ///    laser is on the ground with "Laser walking" On: that pull walks there (LokiGame.cs OnFreeTrigger).
    ///  • Gaze aim: where your view meets the ground is the aim point for ground spells (a ring shows it while one is armed).
    ///  • Left stick: walks the character in the direction you push, relative to where you look (up to 4 cells ahead, never
    ///    round a wall; a push cancels a far walk in legs, Game/RouteWalker.cs). Client wave 10 (w10move): with Smooth movement
    ///    (the default, Settings → Comfort) the stick steers in ANY direction (Game/StickSteer.cs); Classic keeps the cell target.
    /// </summary>
    public sealed class Targeting : MonoBehaviour
    {
        public float ConeDegrees = 11f, MaxCells = 18f, StickLookAhead = 4f, StickResend = 0.25f;
        /// <summary>A unit the look may lock farther away than <see cref="MaxCells"/> (client wave 23, the merge: the tutorial's practice
        /// Poring is the client's own and stands well ahead, 24 cells on the tutorial's own map in the Table view; the server's units
        /// are never that far).</summary>
        public System.Func<Unit, bool> NoRangeLimit;
        public Targetable Locked { get; private set; }
        public Vector2Int? GazeCell { get; private set; }
        public bool ShowGroundAim;
        /// <summary>Test hook: when set, used instead of the left stick.</summary>
        public Vector2? DebugStick;

        private DioramaRig _rig; private EntityManager _ents; private GroundItems _ground; private MapLoader _map; private RoomMask _room;
        private System.Func<bool> _canMove; private System.Action<int, int> _walk;
        private GameObject _lockRing, _aimRing; private Targetable _ringFor;
        private float _nextStick; private Vector2Int _lastSent, _classicDir; private bool _stickWalking;
        private readonly List<Vector2Int> _classicPlan = new List<Vector2Int>();
        private static readonly RoPathSearch.WalkPath StickPath = new RoPathSearch.WalkPath();
        /// <summary>Client wave 10 (w10move): the server's own unit as the legs track it (LokiGame.W10Move.cs sets it); with it and
        /// Smooth movement the stick steers freely (<see cref="Steer"/>), without it the Classic stick is used.</summary>
        public RouteWalker Routes;
        public readonly StickSteer Steer = new StickSteer();
        /// <summary>Smooth movement: the stick began to steer (what a Classic stick request does besides asking for a cell: it ends
        /// a far walk, a pending talk / pick up, an attack chase; LokiGame.W10Move.cs).</summary>
        public System.Action StickTookOver;
        /// <summary>Classic stick requests sent (the w10move smoke compares the two sticks).</summary>
        public int ClassicRequests { get; private set; }

        /// <summary>Players that can be attacked here (War of Emperium / PvP): their lock ring is red like a monster's.</summary>
        public System.Func<Unit, bool> IsHostile;
        /// <summary>Our own pet / homunculus / mercenary (the only companions the gaze locks: the trigger opens their window).</summary>
        public System.Func<uint, bool> IsOwnCompanion;
        public void Init(DioramaRig rig, EntityManager ents, GroundItems ground, MapLoader map, RoomMask room, System.Func<bool> canMove, System.Action<int, int> walk)
        { _rig = rig; _ents = ents; _ground = ground; _map = map; _room = room; _canMove = canMove; _walk = walk; }

        private void Update()
        {
            if (_rig == null || _map == null || _map.Current == null) return;
            UpdateGaze();
            UpdateStick();
        }

        // ------------------------------------------------------------------ gaze
        /// <summary>Client wave 11 (w11menu): the gaze ray was on a panel this frame: nothing of the world is locked and there is no
        /// gaze cell (the design's 2026-10-06 playtest: "anything that is behind the menu should not be considered as a valid target for
        /// sight choosing"). Units the cone would take but a panel hides are counted in <see cref="HiddenByPanel"/>.</summary>
        public bool GazeOnPanel { get; private set; }
        public int HiddenByPanel { get; private set; }

        private void UpdateGaze()
        {
            // client wave 11 (w11menu): the rig's calibrated gaze ray (Settings → Comfort: its start between the eyes, its tilt), the
            // same one the buttons are chosen with (VR/DioramaRig.Ui.cs)
            var head = _rig.GazeRay;
            var self = _ents.Self;
            Targetable best = null; float bestAng = float.MaxValue;
            float cone = ConeDegrees, sticky = ConeDegrees * 1.6f;
            GazeOnPanel = _rig.RayOnUi(head, 50f, out _); HiddenByPanel = 0;
            // client wave 13 (w13look), item 23: "Look select: off": the look locks nothing and gives no ground cell (UI/LookSelect.cs WorldOn), unless "Aim spells with vision" is on
            if (!UI.LookSelect.WorldOn && !VR.Comfort.VisionAimSpells)
            {
                SetLocked(null); GazeCell = null;
                if (_aimRing != null && _aimRing.activeSelf) _aimRing.SetActive(false);
                return;
            }
            if (self != null && !GazeOnPanel)
            {
                foreach (var u in _ents.Units.Values)
                {
                    if (u == null || u.IsSelf) continue;
                    bool companion = u.Kind == Unit.UnitKind.Companion && IsOwnCompanion != null && IsOwnCompanion(u.Id);
                    if (u.Kind != Unit.UnitKind.Monster && u.Kind != Unit.UnitKind.Npc && u.Kind != Unit.UnitKind.Warp && u.Kind != Unit.UnitKind.Player && !companion) continue;
                    if ((Vector2.Distance(u.Cell, self.Cell) > MaxCells && !(NoRangeLimit != null && NoRangeLimit(u))) || (_room != null && !_room.Visible(u.Cell))) continue;
                    if ((u.Sprite != null && u.Sprite.Tint.a <= 0.01f) || u.Unseen) continue; // hidden / cloaked: drawn with alpha 0 (EntityManager.ApplyOption, w3status), or black by intravision (w4units)
                    var t = u.GetComponent<Targetable>(); if (t == null) continue;
                    Consider(t, u.transform.position + Vector3.up * (0.9f * u.transform.lossyScale.y), head, cone, sticky, ref best, ref bestAng);
                }
                foreach (var g in _ground.Items.Values)
                {
                    if (g == null || Vector2.Distance(g.Cell, self.Cell) > MaxCells) continue;
                    var t = g.GetComponent<Targetable>(); if (t == null) continue;
                    Consider(t, g.transform.position + Vector3.up * (0.3f * g.transform.lossyScale.y), head, cone, sticky, ref best, ref bestAng);
                }
            }
            SetLocked(best);

            // Where the gaze meets the ground (for ground spells)
            GazeCell = null;
            var ground = _map.Current.Ground != null ? _map.Current.Ground.GetComponent<Collider>() : null;
            if (!GazeOnPanel && ground != null && ground.Raycast(head, out var hit, 500f))
            {
                var local = _map.transform.InverseTransformPoint(hit.point);
                int cx = Mathf.FloorToInt(local.x), cy = Mathf.FloorToInt(local.z);
                if (_map.Current.Gat.InBounds(cx, cy)) GazeCell = new Vector2Int(cx, cy);
            }
            if (ShowGroundAim && GazeCell.HasValue)
            {
                if (_aimRing == null) { _aimRing = Fx.Ring(_map.transform, Vector3.zero, 1.2f, new Color(0.5f, 0.8f, 1f), -1f, "gaze-aim"); _aimRing.AddComponent<FxSpin>().Speed = -60f; }
                var c = GazeCell.Value; var gat = _map.Current.Gat;
                _aimRing.transform.localPosition = new Vector3(c.x + 0.5f, -gat.Get(c.x, c.y).AverageHeight * RoSpace.Scale + 0.08f, c.y + 0.5f);
                _aimRing.SetActive(true);
            }
            else if (_aimRing != null) _aimRing.SetActive(false);
        }

        private void Consider(Targetable t, Vector3 world, Ray head, float cone, float sticky, ref Targetable best, ref float bestAng)
        {
            var to = world - head.origin;
            if (to.sqrMagnitude < 1e-6f) return;
            float ang = Vector3.Angle(head.direction, to);
            float limit = t == Locked ? sticky : cone;       // keep the current lock a little longer (hysteresis)
            if (ang > limit) return;
            // client wave 11 (w11menu): a panel between the eyes and the unit hides it: not a target (the panels are drawn over the world)
            if (_rig.UiBetween(head.origin, world)) { HiddenByPanel++; return; }
            if (t.Unit != null && t.Unit.Kind == Unit.UnitKind.Monster) ang *= 0.85f; // prefer monsters when things overlap
            else if (t.Unit != null && t.Unit.Kind == Unit.UnitKind.Player && (IsHostile == null || !IsHostile(t.Unit))) ang *= 1.15f; // then NPCs / portals before a passer-by
            else if (t.Unit != null && t.Unit.Kind == Unit.UnitKind.Companion) ang *= 1.25f; // our own pet / homunculus / mercenary walks at our heels: last
            if (ang < bestAng) { bestAng = ang; best = t; }
        }

        private void SetLocked(Targetable t)
        {
            if (t == Locked && _lockRing != null && _ringFor == t) { FollowRing(); return; }
            Locked = t;
            if (_lockRing != null) Destroy(_lockRing);
            _lockRing = null; _ringFor = null;
            if (t == null) return;
            bool hostile = t.Unit != null && (t.Unit.Kind == Unit.UnitKind.Monster || (IsHostile != null && IsHostile(t.Unit)));
            Color c = t.Item != null ? new Color(1f, 0.9f, 0.3f) : hostile ? new Color(1f, 0.3f, 0.25f) : t.Unit.Kind == Unit.UnitKind.Warp ? new Color(0.3f, 0.9f, 1f)
                    : t.Unit.Kind == Unit.UnitKind.Player ? new Color(0.85f, 0.6f, 1f) : t.Unit.Kind == Unit.UnitKind.Companion ? new Color(0.6f, 1f, 0.9f) : new Color(0.35f, 1f, 0.45f);
            _lockRing = Fx.Ring(_map.transform, Vector3.zero, t.Item != null ? 0.7f : 1.0f, c, -1f, "gaze-lock");
            _lockRing.AddComponent<FxSpin>().Speed = 120f;
            _ringFor = t;
            FollowRing();
        }

        private void FollowRing()
        {
            if (_lockRing == null || Locked == null) return;
            var tr = Locked.transform;
            _lockRing.transform.localPosition = tr.localPosition + Vector3.up * 0.07f;
        }

        public void ClearOnMapChange() { if (_lockRing != null) Destroy(_lockRing); _lockRing = null; Locked = null; _ringFor = null; if (_aimRing != null) Destroy(_aimRing); _aimRing = null; }

        // ------------------------------------------------------------------ stick movement
        private void UpdateStick()
        {
            var input = _rig.Input; var self = _ents.Self;
            if (input == null || self == null) return;
            var v = DebugStick ?? _rig.Off.StickValue; // the walking hand: left, or right in left-handed mode
            // grip + stick moves the table instead (on the Valve Index a firm squeeze: a hand only touching its grip walks, asked by
            // design note after the 2026-10-01 playtest; VR/LokiInput.cs TableGripHeld)
            // client wave 13 (w13resize): only in RESIZE MODE (LokiInput.Hand.CameraGrip); outside it grip + stick walks
            bool grip = !DebugStick.HasValue && _rig.Off.CameraGrip;
            if (_rig.InputHeld) v = Vector2.zero; // the radial skill wheels have the sticks (LokiGame.Wheels.cs)
            if (_rig.WalkStickTaken) v = Vector2.zero; // client wave 11 (w11menu): the launcher ring has the walking stick (UI/MenuRing.cs)
            // client wave 10 (w10move): Smooth movement — the stick steers in any direction (Game/StickSteer.cs)
            if (Unit.Smooth && Routes != null) { UpdateStickSmooth(self, v, grip); return; }
            if (Steer.Active) Steer.Release(self, Routes, _walk, false); // the setting went to Classic while steering: the Classic stick takes over
            if (Steer.Settling) Steer.CancelSettle(self, "Classic"); // free movement (wfree): the body goes back to its cell (Sprites/Unit.Smooth.cs)
            if (Routes != null) Routes.SrvTrack(self);
            if (grip || v.magnitude < 0.3f || !_canMove())
            {
                if (_stickWalking && v.magnitude < 0.2f)
                {
                    _stickWalking = false; _classicDir = Vector2Int.zero;
                    if (Routes != null && Routes.SrvWalking)
                    {
                        if (Routes.SrvStopTarget(out var stopCell)) _walk(stopCell.x, stopCell.y);
                    }
                    else
                    {
                        var here = Vector2Int.RoundToInt(self.Cell);
                        _walk(here.x, here.y); // stop where we are
                    }
                }
                return;
            }
            var head = _rig.Head.transform;
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            var dirWorld = yaw * new Vector3(v.x, 0, v.y);
            var dirMap = _map.transform.InverseTransformDirection(dirWorld); dirMap.y = 0;
            if (dirMap.sqrMagnitude < 1e-6f) return;
            dirMap.Normalize();

            // 8-direction quantization with cardinal cone (±28.7° cardinal, ±16.3° diagonal) and hysteresis so Classic movement
            // follows the aimed direction cleanly instead of snapping to 45° diagonals or jiggling across sector boundaries.
            float ax = Mathf.Abs(dirMap.x), az = Mathf.Abs(dirMap.z);
            bool wasDiag = _stickWalking && _classicDir.x != 0 && _classicDir.y != 0;
            float ratio = wasDiag ? 0.36f : 0.48f;
            int stepX = ax >= az * ratio ? (dirMap.x >= 0f ? 1 : -1) : 0;
            int stepY = az >= ax * ratio ? (dirMap.z >= 0f ? 1 : -1) : 0;
            var stepDir = new Vector2Int(stepX, stepY);
            bool dirChanged = !_stickWalking || stepDir != _classicDir;
            if (!dirChanged && Time.time < _nextStick) return;

            var gat = _map.Current.Gat;
            bool srvWalking = Routes != null && Routes.SrvWalking;
            Vector2Int origin = srvWalking ? Routes.SrvRepathCell : Vector2Int.RoundToInt(self.Cell);
            if (!dirChanged && srvWalking)
            {
                Routes.SrvCellsAfterRepath(_classicPlan);
                if (_classicPlan.Count >= 2 && _classicPlan[0] == origin + stepDir && _classicPlan[1] == origin + stepDir * 2)
                {
                    _nextStick = Time.time + 0.08f;
                    return;
                }
            }

            Vector2Int? target = ClassicTarget(gat, origin, stepDir, srvWalking ? Routes.SrvCell : origin);
            if (!target.HasValue && stepX != 0 && stepY != 0)
            {
                // Diagonal blocked by a wall: slide along the stronger cardinal axis, then the secondary axis
                var pri = ax >= az ? new Vector2Int(stepX, 0) : new Vector2Int(0, stepY);
                var sec = ax >= az ? new Vector2Int(0, stepY) : new Vector2Int(stepX, 0);
                target = ClassicTarget(gat, origin, pri, srvWalking ? Routes.SrvCell : origin)
                      ?? ClassicTarget(gat, origin, sec, srvWalking ? Routes.SrvCell : origin);
            }
            else if (!target.HasValue && (stepX == 0 || stepY == 0))
            {
                // Cardinal blocked by a wall: if the stick has a clear sideways tilt, slide along the wall
                if (stepX == 0 && ax >= 0.25f) target = ClassicTarget(gat, origin, new Vector2Int(dirMap.x >= 0f ? 1 : -1, 0), srvWalking ? Routes.SrvCell : origin);
                else if (stepY == 0 && az >= 0.25f) target = ClassicTarget(gat, origin, new Vector2Int(0, dirMap.z >= 0f ? 1 : -1), srvWalking ? Routes.SrvCell : origin);
            }

            _nextStick = Time.time + StickResend;
            if (!target.HasValue) return;
            if (_stickWalking && !dirChanged && target.Value == _lastSent && (self.Walking || srvWalking)) return;
            _lastSent = target.Value; _classicDir = stepDir; _stickWalking = true; ClassicRequests++;
            _walk(target.Value.x, target.Value.y);
        }

        private Vector2Int? ClassicTarget(Formats.GatFile gat, Vector2Int origin, Vector2Int step, Vector2Int srvCell)
        {
            int maxSteps = Mathf.Max(1, Mathf.RoundToInt(StickLookAhead));
            for (int k = maxSteps; k >= 1; k--)
            {
                int x = origin.x + step.x * k, y = origin.y + step.y * k;
                if (!gat.InBounds(x, y) || !gat.Get(x, y).Walkable) continue;
                if (!RoPathSearch.Search(StickPath, gat, origin.x, origin.y, x, y, 0) || StickPath.Len > k) continue;
                if (srvCell != origin && (!RoPathSearch.Search(StickPath, gat, srvCell.x, srvCell.y, x, y, 0) || StickPath.Len > RoRoute.MaxWalkPath)) continue;
                if (x == srvCell.x - 1 && y == srvCell.y && Occupied(x, y)) continue;
                return new Vector2Int(x, y);
            }
            return null;
        }

        /// <summary>
        /// Client wave 10 (w10move), Smooth movement: the stick's direction (any angle, relative to where you look, as the Classic
        /// stick's) steers our character. From 0.3 of its travel the stick walks; it keeps walking down to 0.2 (the same dead
        /// zone as the Classic stick); letting go stops the character on the first cell it can stop on. Walking impossible (a
        /// dialog, dead …) or the table grip held: the steering ends without a request, as the Classic stick sends none then.
        /// </summary>
        private void UpdateStickSmooth(Unit self, Vector2 v, bool grip)
        {
            _stickWalking = false;
            float mag = v.magnitude;
            // free movement (wfree): after the stick was let go the server's unit is brought to the cell under the held body
            if (Steer.Settling && !Steer.Active && (mag < 0.3f || grip)) Steer.SettleTick(self, _map.Current.Gat, Routes, _walk, Occupied, _canMove());
            if (grip || !_canMove()) { if (Steer.Active || self.Steer.HasValue) Steer.Release(self, Routes, _walk, false); self.Steer = null; return; }
            if (mag < 0.2f || (mag < 0.3f && !Steer.Active)) { if (Steer.Active) Steer.Release(self, Routes, _walk); self.Steer = null; return; }
            var head = _rig.Head.transform;
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            var dirWorld = yaw * new Vector3(v.x, 0, v.y);
            var dirMap = _map.transform.InverseTransformDirection(dirWorld); dirMap.y = 0;
            if (dirMap.sqrMagnitude < 1e-6f) return;
            dirMap.Normalize();
            bool was = Steer.Active;
            Steer.Hold(self, _map.Current.Gat, Routes, new Vector2(dirMap.x, dirMap.z), Time.deltaTime, _walk, Occupied);
            // the stick takes over at once, also when it asks for nothing yet because the walk the server runs already goes its way
            // (a far walk in legs would otherwise go on asking for its next leg: found by w3walk's "stick cancels")
            if (!was && Steer.Active) StickTookOver?.Invoke();
        }

        /// <summary>A character, monster or NPC (a portal too) stands on the cell, as far as the client sees: rAthena drops a walk
        /// request for the cell one to the west of our unit then (clif.cpp:11402-11405; map_count_oncell counts what is not
        /// walking, map.cpp:641-683), so the steering never sends that one.</summary>
        private bool Occupied(int x, int y)
        {
            foreach (var u in _ents.Units.Values)
                if (u != null && !u.IsSelf && !u.Walking && Mathf.RoundToInt(u.Cell.x) == x && Mathf.RoundToInt(u.Cell.y) == y) return true;
            return false;
        }
    }
}
