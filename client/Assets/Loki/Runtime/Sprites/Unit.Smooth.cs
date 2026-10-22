using Loki.Formats;
using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 10 (w10move): where a unit is DRAWN. the design chose "looks and feels free, client only": the server still decides
    /// everything in cells (rA unit.cpp:542-790 walks a unit cell by cell; nothing of that changes, on rAthena or on the module),
    /// and <see cref="Cell"/> stays exactly what it was — the unit on the server's own path, step by step on the server's clock
    /// (client wave 3, w3move). Every rule of the game keeps reading <see cref="Cell"/>. Only the body's transform follows
    /// <see cref="DrawnCell"/>, which in Smooth mode is
    ///  - for every unit: the logical position averaged over a window of time centred on now (one cell's walk time each way; the
    ///    past from the knots the unit really passed, the future from the plan the server sent), so a corner of the cell path is
    ///    drawn round and a staircase of steps straight. The window closes to nothing at the start of a walk from rest and at
    ///    the plan's end, so a unit leaves and arrives when and where it did before;
    ///  - for our own character while the stick steers it (<see cref="Steer"/>, Game/StickSteer.cs): a point that moves in a
    ///    straight line in the stick's direction at the server's pace along that line;
    ///  - never further than <see cref="MaxOffset"/> (half a cell) from <see cref="Cell"/> — the unit on the server's path as the
    ///    client follows it, which moves without a jump; not from the raw position of the last walk packet at the server
    ///    clock's time, which jumps by the client's catch-up whenever a packet arrives (a limit against it would snap the
    ///    body: tried and measured, design notes) —, never inside a cell the server
    ///    would not walk (CELL_CHKNOPASS on the client's GAT, the check the path itself uses): pulled back towards the logical
    ///    position, which is on the path, so the body slides along a wall as the path does.
    /// A change of plan (a new walk packet, a stop, a knockback, the setting) never moves the body at once: what the change
    /// would have jumped is carried as an offset that fades (<see cref="OffsetSeconds"/>). Classic mode draws <see cref="Cell"/>.
    /// Facing in Smooth mode comes from the drawn movement, with hysteresis for the 8-direction sprites and a continuous yaw for
    /// 3-D bodies (<see cref="BodyYaw"/>, Sprites/GrannyUnit.cs).
    /// Every number here is Loki's own choice (the official client draws cell to cell); nothing is rAthena-specific.
    /// </summary>
    public sealed partial class Unit
    {
        /// <summary>Settings → Comfort → "Movement": true = Smooth (the default), false = Classic (cell to cell, as before client
        /// wave 10). VR/Comfort.cs sets it from loki-user.json.</summary>
        public static bool Smooth = true;
        /// <summary>The drawn body is never further than this from the logical position, in cells ("you can stand up to half a
        /// cell from where the server thinks you are").</summary>
        public const float MaxOffset = 0.49f;
        /// <summary>Half the averaging window, in cells walked (so in time: this × the unit's speed per cell). With 1 cell a
        /// 45° corner is drawn 0.19 cell inside the cell path, a 90° corner 0.35, a 135° one 0.46: all within half a cell.</summary>
        public const float WindowCells = 1f;
        /// <summary>How fast an offset left by a change of plan fades (a critically damped ease: about 3× this to settle).</summary>
        public const float OffsetSeconds = 0.1f;
        /// <summary>How long the body takes to swing onto a new path when a unit already walking is given one (a chase re-pathing, a
        /// steering player seen by others): it keeps its velocity at the change and eases over (a critically damped ease). At
        /// walking speed 150 a 45° change of course carries the body at most 0.19 cell off the new path, a 90° one 0.35. (0.25 s was
        /// tried: on the module with packets 0.1 s late the larger swing ran into the half-cell limit and a chase read sharper, not smoother.)</summary>
        public const float SwingSeconds = 0.2f;
        /// <summary>8-direction sprites turn only when the drawn heading is this many degrees past the middle between two
        /// directions (no flicker when walking close to 22.5°).</summary>
        public const float FacingHysteresis = 12f;
        /// <summary>3-D bodies turn at most this fast, degrees per second.</summary>
        public const float YawSpeed = 540f;

        // ---- free movement (design note 2026-10-14: "client-authoritative sub-tile stop", the player stops anywhere and is drawn there, rAthena
        // keeps the character on the nearest cell; "friends seeing other player's exact spots too"). Smooth mode only; Classic is unchanged.
        /// <summary>Our own character: the drawn body is never further than this from the logical position (a safety net only: a push from
        /// rest begins up to 0.71 cell from the cell's centre, where the server's unit stands, which <see cref="MaxOffset"/> would snap).</summary>
        public const float FreeMaxOffset = 1.2f;
        /// <summary>A rest offset (where in its cell the body stands) stays within the cell's square: per axis at most this far from its centre.</summary>
        public const float RestSquare = 0.499f;
        /// <summary>A laser / trigger walk to an exact point: the point's offset from its cell is eased in over this many cells' walk time
        /// before the arrival (none of it at the start of a longer walk; all of it at the arrival).</summary>
        public const float EndRampCells = 1.5f;
        /// <summary>Another player drawn at the spot its own client sends over the relay (UI/RemoteSpots.cs): not when that spot is further
        /// than this from where the server says the unit is (a knockback seen before their spot arrives, a stale spot): the server's
        /// drawing then, eased; and the ease between the two drawings, seconds.
        /// (wmove: 1.5 -> 2.5. A steering body now leads its server unit by up to <see cref="FreeLeadHard"/> plus half a round trip's walk
        /// (2.1 cells at 100 ms) right after a reversal; the spot is drawn 0.1 s late, which shortens that while walking. A knockback of more
        /// than 2.5 cells still falls back at once; a shorter one is drawn at the old spot until the new one arrives, sent at once.)</summary>
        public const float RemoteMaxOffset = 2.5f, RemoteBlendSeconds = 0.15f;

        // ---- wave 27 (wfix): another player steering, seen without the relay (w10move E). Measured (the guest's plans, logged): a player who
        // steers gets a new server walk every step, 2-4 cells long, aimed alternately 0°, 27° and 45-53° for a 33° stick (the server's cell
        // re-paths toward a spot ahead of the steering body, StickSteer.AimAt); each plan's own future zigzags, so the window above (which
        // reads the plan's future) followed the staircase: 0.40-0.43 cell off the line against the cells' 0.51-0.53 (76-83 %).
        /// <summary>Another PLAYER's walk given under way, at most <see cref="SteerPlanSteps"/> steps long and heading the way it went over the
        /// last window (a player steering), is drawn from the PAST alone (monsters keep the window above: a chase drawn this way met the half
        /// cell in a dev 11 run, w10move E chase 0.49 cell, 50 deg): the logical position averaged over the last
        /// <see cref="SteerWindowCells"/> cells' walk time plus half that window's displacement (no lag on a straight walk; a staircase whose
        /// pattern repeats within the window is drawn straight: 3 steps, diagonal-diagonal-straight, take 0.57 s at speed 150, the window
        /// 0.6 s). The guest's plans replayed: a 33° steer 0.11 cell off its line (cells 0.51), 57° / 123° / 213° about 0.2 of the cells';
        /// 8°, 22°, 40° and 78° within 3 % of before (their patterns are longer than the window).</summary>
        public const float SteerWindowCells = 4f;
        public const int SteerPlanSteps = 5;
        private bool _lead; // the plan in hand is drawn by the rule above

        // ---- the stick leads, the server follows (design note 2026-10-15, "smooth movement specification": the free-movement showcase
        // showed the steering body bent off the stick's line on curves, wobbling on tight circles, hooking on reversals, ~10 % slower than
        // Classic. Cause: the body was held within half a cell of the server's cell-by-cell walk (MaxOffset) and paced at that walk's own
        // speed along the line (the old SteerSpeed), so every lag of the server's re-paths bent or slowed it.)
        /// <summary>While the stick steers, our own body walks straight along the stick at the character's own pace (1000 / Speed cells a
        /// second) and the server's unit is steered after it (Game/StickSteer.cs aims its requests ahead of the body). The body is held back
        /// only when it is further than <see cref="FreeLeadSoft"/> from the logical position (the server's unit as the client follows it):
        /// the part of its step that leads away from it shrinks with the square of how far it is into the band, to nothing at
        /// <see cref="FreeLeadHard"/> (both plus half a round trip's walk, at most 0.1 s of it). A staircase of cells is up to 7 % slower
        /// than a straight line 22° off an axis (1 / (cos + 0.4 sin), rA unit.cpp:226-229), so on a long straight walk at such an angle the
        /// body ends up walking at the server's pace about 1.3 cells ahead; on curves the requests aimed ahead let the server cut inside and
        /// keep up (modelled: circles of radius 1.4-5.5 at full pace, leads under 0.85 cell). After a reversal the server walks on for up to
        /// a step before it turns: the band lets the body slow a little instead of stopping (a hard limit of 1.4 with a straight band stopped
        /// it for ~0.15 s once at 34 frames a second; 1.8 with the squared band: modelled, at worst 6.3 cells/s for 0.1 s at 60 frames a
        /// second). When the stick is let go the settle brings the server's unit to the cell under the body (half a cell).</summary>
        public const float FreeLeadSoft = 1.6f, FreeLeadHard = 3.0f;
        /// <summary>A safety net only: the steering body is never further than this beyond <see cref="FreeLeadHard"/> from the logical position.</summary>
        public const float FreeLeadSafety = 0.6f;
        /// <summary>Diagnostics (the smokes): frames in which the lead held the steering body back, and the largest lead seen.</summary>
        public int LeadHolds { get; private set; }
        private float _steerSpeed;

        /// <summary>Updates walk speed without a discontinuous jump in DrawnCell, EndTerm, or FreeLead.</summary>
        public void SetSpeed(int newSpeed)
        {
            if (newSpeed <= 0 || newSpeed == Speed) return;
            int oldSpeed = Speed;
            if (!_drawnInit) { Speed = newSpeed; _steerSpeed = newSpeed; return; }
            float now = Time.time;
            Vector2 before = SmoothBefore(now);
            Speed = newSpeed;
            if (_motion == Motion.Walk && _segs > 0 && oldSpeed > 0)
            {
                float scale = (float)newSpeed / oldSpeed;
                float at = now;
                for (int i = 0; i < _segs; i++)
                {
                    float end = _segStart[i] + _segDur[i];
                    if (end <= now) { at = end; continue; }
                    if (_segStart[i] < now)
                    {
                        // Current step in progress on the server finishes at its originally scheduled timer; future steps use the new speed
                        at = end;
                    }
                    else
                    {
                        _segStart[i] = at;
                        _segDur[i] = Mathf.Max(0.0005f, _segDur[i] * scale);
                        at += _segDur[i];
                    }
                }
            }
            if (!_steering && !(FreeSelf && Held.HasValue))
            {
                Vector2 a = Smooth ? Averaged(now) : Cell;
                Vector2 end = EndTerm(now);
                _lastEnd = end;
                _off = before - a - _swing - end;
            }
        }

        /// <summary>The lead limits now for this unit (cells): soft and hard, with round trip walk allowance added.</summary>
        public void FreeLead(out float soft, out float hard)
        {
            float effSpeed = _steerSpeed > 0f ? _steerSpeed : Speed;
            float rtt = ServerClock.LastRttMs > 0 ? (float)ServerClock.LastRttMs / 1000f : 0.1f;
            float add = 0.75f * Mathf.Min(0.3f, rtt) * 1000f / Mathf.Max(effSpeed, 1f);
            soft = FreeLeadSoft + add; hard = FreeLeadHard + add;
        }
        private Vector2 _steerCell;
        /// <summary>A slide that carries a steering body (a knockback / fast move, <see cref="Slide"/>'s carry): its moves of the logical position
        /// are added to the body, the last one too (the frame in which it ended).</summary>
        private bool _slideCarry;

        /// <summary>One frame of the steering body: <paramref name="d"/> × the walking pace, less the part that would lead away from the
        /// logical position beyond the soft limit; along a wall when the step would enter a cell the server does not walk.</summary>
        private Vector2 FreeSteerStep(Vector2 p, Vector2 d, float dt)
        {
            if (_steerSpeed <= 0f) _steerSpeed = Speed;
            else _steerSpeed = Mathf.MoveTowards(_steerSpeed, Speed, dt * 180f);
            float pace = 1000f / Mathf.Max(_steerSpeed, 1f);
            FreeLead(out float soft, out float hard);
            Vector2 o = p - Cell; float dist = o.magnitude, f = 1f;
            if (dist > 1e-4f) { float k = Mathf.Clamp01((dist - soft) / (hard - soft)); f = 1f - k * k * Mathf.Max(0f, Vector2.Dot(d, o / dist)); }
            if (f < 1f) LeadHolds++;
            if (f <= 0f) return p;
            return FreeSlide(p, d * (pace * dt * f));
        }

        /// <summary>The body's step <paramref name="step"/> from <paramref name="p"/>: whole when it stays on walkable cells (a diagonal crossing
        /// only past two free orthogonal cells, as the server's path, rA path.cpp:385-405); else as far as it goes, and the rest along the wall
        /// at the same pace on the free axis (the server's unit slides along a wall at its full pace too), unless that axis is more than ~69°
        /// off the step (the server takes no such step: StickSteer.SlideCos; pushed straight into a wall nothing moves).</summary>
        private Vector2 FreeSlide(Vector2 p, Vector2 step)
        {
            var q = FreeReach(p, step);
            if (q == p + step) return q;
            WallClamps++;
            var rest = step - (q - p); float len = rest.magnitude, full = step.magnitude;
            if (len < 1e-6f) return q;
            bool ax = Mathf.Abs(step.x) >= 0.35f * full, ay = Mathf.Abs(step.y) >= 0.35f * full;
            var alongX = new Vector2(Mathf.Sign(step.x) * len, 0f); var alongY = new Vector2(0f, Mathf.Sign(step.y) * len);
            var qx = ax ? FreeReach(q, alongX) : q; var qy = ay ? FreeReach(q, alongY) : q;
            float gx = (qx - q).sqrMagnitude, gy = (qy - q).sqrMagnitude;
            if (gx <= 1e-10f && gy <= 1e-10f) return q;
            return gx > gy || (gx == gy && Mathf.Abs(step.x) >= Mathf.Abs(step.y)) ? qx : qy;
        }
        /// <summary>The farthest point of <paramref name="p"/> + s·<paramref name="step"/> (0 ≤ s ≤ 1) the body may stand on, going on from p.</summary>
        private Vector2 FreeReach(Vector2 p, Vector2 step)
        {
            if (FreeCanEnter(p, p + step)) return p + step;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++) { float mid = (lo + hi) * 0.5f; if (FreeCanEnter(p, p + step * mid)) lo = mid; else hi = mid; }
            return p + step * lo;
        }
        private bool FreeCanEnter(Vector2 p, Vector2 q)
        {
            if (Blocked(q)) return false;
            int cx = CellOf(p.x), cy = CellOf(p.y), nx = CellOf(q.x), ny = CellOf(q.y);
            return nx == cx || ny == cy || _gat == null || (!_gat.Check(nx, cy) && !_gat.Check(cx, ny));
        }

        /// <summary>Our own character, free movement: the stick was let go here and the server's unit is being brought to the cell under
        /// it (Game/StickSteer.cs, the settle): the body stays exactly here, and the server's correction is not drawn (no walk, no turn).</summary>
        public Vector2? Held { get; private set; }
        /// <summary>Our own character, free movement: where in its cell the body stands at rest (the drawn body = the cell + this). Set by a
        /// settle, a walk to an exact point, a stop the server made; kept through a knockback; 0 after a warp or a walk to a cell.</summary>
        public Vector2 RestOffset => _rho;
        /// <summary>Our own character: the exact point of a laser / trigger walk (LokiGame.W3Walk.cs): a walk whose destination is
        /// <see cref="FreeGoalCell"/> ends on <see cref="FreeGoalPoint"/> instead of the cell's centre. Null: none.</summary>
        public Vector2Int? FreeGoalCell; public Vector2 FreeGoalPoint;
        private Vector2 _rho, _endOff; private bool _endOffActive;
        /// <summary>Our own body moving again from a spot off its cell's centre: how much more than <see cref="MaxOffset"/> it may be from the
        /// logical position now (what it stood off by), absorbed within the first step (<see cref="_freeExcessRate"/> cells a second).</summary>
        private float _freeExcess, _freeExcessRate;
        private void FreeExcessFrom(Vector2 drawn, float seconds = -1f)
        {
            float e = Mathf.Max(0f, (drawn - Cell).magnitude - MaxOffset);
            if (e <= _freeExcess) return;
            _freeExcess = e; _freeExcessRate = Mathf.Max(0.5f, e / (seconds > 0f ? seconds : Mathf.Max(0.05f, Mathf.Max(Speed, 1) / 1000f))); // (wmove: seconds, the steering's lead eased out with the offset's fade)
        }
        /// <summary>Free movement applies to this unit: our own character in Smooth mode.</summary>
        private bool FreeSelf => Smooth && IsSelf;

        // another player drawn at its relayed spot
        private Vector2? _remoteTarget; private Vector2 _remoteLast; private int _remoteFacing = -1; private bool _remoteMoving; private float _remoteW;
        private Vector2 _srvDrawn; // the server's drawing (what DrawnCell is without a relayed spot)
        private bool _remoteSnap; private Vector2 _lastEnd;
        /// <summary>Another player: drawn (partly or fully) at the spot its client sends over the relay (UI/RemoteSpots.cs).</summary>
        public bool RemoteDriven => _remoteW > 0f;
        /// <summary>How much of the drawing is the relayed spot (0 the server's drawing, 1 the spot).</summary>
        public float RemoteWeight => _remoteW;
        /// <summary>The look (facing, walk / idle) follows the drawn body, not the server's walks: our own character held after the stick,
        /// another player drawn at its relayed spot.</summary>
        private bool DrawnOwnsLook => (FreeSelf && (Held.HasValue || _steering)) || (_remoteW > 0f && !IsSelf); // (wmove: and while the stick steers our own body: it walks on while the server's unit pauses between re-paths)
        private bool _lookWalking; private float _lookStillSince = -1f;

        /// <summary>The receiver of relayed spots (UI/RemoteSpots.cs) every frame, before the units move: where the other player's own client
        /// draws it now (null: no spot), its facing (client direction 0-7) and whether it moves.</summary>
        public void SetRemote(Vector2? spot, int facing, bool moving)
        {
            _remoteTarget = spot; if (spot.HasValue) { _remoteFacing = facing & 7; _remoteMoving = moving; }
        }

        /// <summary>The stick was let go (Game/StickSteer.cs): the body stays exactly at <paramref name="at"/> while the server is brought
        /// to the cell under it.</summary>
        public void Hold(Vector2 at)
        {
            if (!FreeSelf) return;
            Held = at; DrawnCell = at; _srvDrawn = at; _steering = false;
            _off = Vector2.zero; _offVel = Vector2.zero; _swing = Vector2.zero; _swingVel = Vector2.zero; _vel = Vector2.zero; _velNow = Vector2.zero;
            _endOffActive = false; _yawMoving = false;
            if (Sprite != null && Sprite.ActionGroup == 1) Sprite.Play(0, true); // the body stopped: idle at once, whatever the server's unit still walks
            _lookWalking = false;
        }

        /// <summary>The settle ended: the server's unit stands on <paramref name="serverCell"/> (the cell under the held spot when it went
        /// well). The body stands at the held spot if that is within the cell's square, else at the nearest point of it (eased).</summary>
        public void Unhold(Vector2Int serverCell)
        {
            if (!Held.HasValue) return;
            var h = Held.Value; Held = null;
            FreeExcessFrom(h, 3f * OffsetSeconds); // (wmove: a settle that gave up may leave the server's unit up to the steering's lead away: eased, not snapped)
            _rho = ClampSquare(h - serverCell);
            _endOffActive = false;
            float now = Time.time;
            Vector2 a = Smooth ? Averaged(now) : Cell;
            _off = h - a - _swing - EndTerm(now); _offVel = Vector2.zero;
            if (_off.sqrMagnitude < 1e-8f) _off = Vector2.zero;
            DrawnCell = h; _srvDrawn = h;
        }

        /// <summary>The settle gave way to another action of the player at once (the stick again: the body stays where it is).</summary>
        public void DropHold() { if (Held.HasValue) { DrawnCell = Held.Value; _srvDrawn = DrawnCell; Held = null; _rho = Vector2.zero; } }

        private static Vector2 ClampSquare(Vector2 v) => new Vector2(Mathf.Clamp(v.x, -RestSquare, RestSquare), Mathf.Clamp(v.y, -RestSquare, RestSquare));

        /// <summary>What is added to the averaged logical position at time <paramref name="t"/>: while a walk to an exact point runs, that
        /// point's offset from its cell, eased in over the last <see cref="EndRampCells"/>; otherwise the rest offset (0 for every unit but
        /// our own character in free movement).</summary>
        private Vector2 EndTerm(float t)
        {
            if (!FreeSelf) return Vector2.zero;
            if (_motion == Motion.Walk && _endOffActive && _segs > 0)
            {
                float end = _segStart[_segs - 1] + _segDur[_segs - 1];
                float ramp = Mathf.Max(0.001f, EndRampCells * Mathf.Max(Speed, 1) / 1000f);
                return _endOff * Mathf.Clamp01(1f - (end - t) / ramp);
            }
            return _rho;
        }

        /// <summary>A new walk of our own character (not held): where in its destination cell it ends (a laser / trigger walk's exact
        /// point, else the centre). Called from WalkTo before the plan's offset is taken.</summary>
        private void FreeWalkBegins(int x1, int y1)
        {
            if (!FreeSelf || Held.HasValue) return;
            FreeExcessFrom(DrawnCell); // (the body as drawn before this walk: SmoothBefore just brought it to now)
            _rho = Vector2.zero;
            _endOffActive = FreeGoalCell.HasValue && FreeGoalCell.Value.x == x1 && FreeGoalCell.Value.y == y1;
            _endOff = _endOffActive ? ClampSquare(FreeGoalPoint - new Vector2(x1, y1)) : Vector2.zero;
        }

        /// <summary>Our own character's walk reached its end: a walk to an exact point stands there from now on.</summary>
        private void FreeWalkEnded()
        {
            if (!FreeSelf || Held.HasValue) return;
            if (_endOffActive) { _rho = _endOff; _endOffActive = false; }
        }

        /// <summary>Our own character, standing: the laser pointed at <paramref name="p"/> in the very cell the server's unit stands on
        /// (LokiGame.W3Walk.cs): nothing is asked; the body goes there, facing it (the offset's ease, about 0.3 s).</summary>
        public void GlideTo(Vector2 p)
        {
            if (!FreeSelf || Held.HasValue || _motion != Motion.None) return;
            var cell = new Vector2(CellOf(Cell.x), CellOf(Cell.y));
            Vector2 before = DrawnCell;
            _rho = ClampSquare(p - cell); _endOffActive = false;
            _off = before - Cell - _swing - _rho; _offVel = Vector2.zero;
            var d = p - before;
            if (d.sqrMagnitude > 0.01f && Sprite != null) Sprite.Direction = DirFromDelta(d.normalized * 2f);
        }

        /// <summary>A move at once that is not a warp (a snap knockback): our own body keeps where in its cell it stood.</summary>
        private void KeepRest(Vector2 rho)
        {
            if (!FreeSelf || rho == Vector2.zero) return;
            _rho = rho; DrawnCell = KeepWalkable(Cell + rho); _srvDrawn = DrawnCell; _lastEnd = rho;
        }

        /// <summary>The server stopped our own character on <paramref name="cell"/> (ZC_STOPMOVE: an attack, a cast, sitting while it walked,
        /// a failed re-path): the body moves only as far as it must, into that cell's square.</summary>
        private void FreeStopped(Vector2 drawnBefore, Vector2 cell)
        {
            if (!FreeSelf || Held.HasValue) return;
            _rho = ClampSquare(drawnBefore - cell); _endOffActive = false;
        }

        /// <summary>Where the body is drawn, in cells (transform.localPosition is this + 0.5 and the ground's height). Game
        /// logic never reads it: <see cref="Cell"/> is the unit's place for every rule.</summary>
        public Vector2 DrawnCell { get; private set; }
        /// <summary>Our own character only (Game/StickSteer.cs, Smooth mode): the stick's direction in cell space, a unit vector,
        /// while the stick steers; null otherwise.</summary>
        public Vector2? Steer;
        /// <summary>The body's ground position in map space as game logic sees it (the logical cell, the cell's mean height): for
        /// anything that must not follow the drawn body.</summary>
        public Vector3 LogicalLocal
        {
            get
            {
                float h = 0f;
                if (_gat != null)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(Cell.x + 0.5f), 0, _gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(Cell.y + 0.5f), 0, _gat.Height - 1);
                    h = -_gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
                }
                return new Vector3(Cell.x + 0.5f, h, Cell.y + 0.5f);
            }
        }

        /// <summary>Diagnostics / the w10move smoke: the drawn body was held back this frame by the half-cell limit / by an
        /// unwalkable cell; totals since the unit was made.</summary>
        public int OffsetClamps { get; private set; }
        public int WallClamps { get; private set; }
        /// <summary>The drawn velocity, cells per second (smoothed over ~60 ms; the facing reads it).</summary>
        public Vector2 DrawnVelocity => _vel;

        // the logical position's recent past: the points where its straight runs began (a new plan, a step's end), oldest first
        private const int KnotMax = 16;
        private readonly float[] _knT = new float[KnotMax]; private readonly Vector2[] _knP = new Vector2[KnotMax];
        private int _knN, _knSeg;
        private float _episodeStart;          // local time the unit last began to walk from rest
        private Vector2 _off, _offVel;        // what a change of plan would have jumped, fading
        private Vector2 _swing, _swingVel;    // the velocity a unit had when its plan changed under way, easing onto the new path
        private float _smoothT = -1f;
        private bool _drawnInit, _steering, _segFacing = true, _wasSmooth = Smooth;
        private Vector2 _vel, _velNow;
        private float _yaw, _yawT = -1f, _yawHeading; private bool _yawMoving;

        /// <summary>Anything left to do while the unit stands (an offset fading, the stick steering, the body not yet on its cell).</summary>
        private bool SmoothBusy => _steering || (IsSelf && Steer.HasValue) || _off != Vector2.zero || _offVel != Vector2.zero || _swing != Vector2.zero || _swingVel != Vector2.zero || (DrawnCell - Cell).sqrMagnitude > 1e-8f
            || Held.HasValue || _remoteTarget.HasValue || _remoteW > 0f || _lookWalking; // free movement (wfree): a body held off its cell, a relayed spot, the look's walk to end


        private void AddKnot(float t, Vector2 p)
        {
            if (_knN > 0 && t <= _knT[_knN - 1]) { if (_knN == 1 || t > _knT[_knN - 2]) { _knT[_knN - 1] = t; _knP[_knN - 1] = p; } return; }
            if (_knN == KnotMax) { System.Array.Copy(_knT, 1, _knT, 0, KnotMax - 1); System.Array.Copy(_knP, 1, _knP, 0, KnotMax - 1); _knN--; }
            _knT[_knN] = t; _knP[_knN] = p; _knN++;
        }

        /// <summary>The logical position at a time <paramref name="s"/> before the plan in hand began: between the knots (the last
        /// knot is that plan's own start).</summary>
        private Vector2 HistAt(float s)
        {
            if (_knN == 0) return _pts[0];
            if (s <= _knT[0]) return _knP[0];
            for (int i = _knN - 1; i >= 0; i--)
            {
                if (s < _knT[i]) continue;
                if (i + 1 >= _knN) return _knP[i];
                float span = _knT[i + 1] - _knT[i];
                return span > 1e-5f ? Vector2.Lerp(_knP[i], _knP[i + 1], (s - _knT[i]) / span) : _knP[i + 1];
            }
            return _knP[0];
        }

        /// <summary>The logical position averaged over [t − w, t + w]; w is the window, closed at a walk's start from rest and at
        /// the plan's end. Not walking: the logical position itself.
        /// Tried and taken out again (commit 048d8833, measured on rAthena): for a unit given a new plan every step, the window's
        /// future beyond the step in progress taken straight on at the unit's own mean velocity instead of from the plan. As the
        /// second client drew a steering player it was worse, not better (0.86 cell off its line against 0.39 with the plan's
        /// own future; the cell path 0.50), and our own steering lost its straight line (heading error 5.2° against 0.5°).</summary>
        private Vector2 Averaged(float t)
        {
            if (_motion != Motion.Walk || _segs == 0) return Cell;
            if (_lead) return LeadAveraged(t); // wave 27 (wfix)
            float end = _segStart[_segs - 1] + _segDur[_segs - 1];
            float w = Mathf.Min(WindowCells * Mathf.Max(Speed, 1) / 1000f, Mathf.Min(t - _episodeStart, end - t));
            if (w <= 0.0005f) return Cell;
            const int n = 10;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i <= n; i++)
            {
                float s = t + w * (2f * i / n - 1f);
                Vector2 p = s >= _segStart[0] ? PositionAt(s) : HistAt(s); // the plan in hand (its walked part too), before it the knots
                sum += i == 0 || i == n ? p * 0.5f : p;
            }
            return sum / n;
        }

        /// <summary>Wave 27 (wfix): the logical position at a time <paramref name="s"/> not after now (the plan in hand, before it the knots).</summary>
        private Vector2 PastAt(float s) => s >= _segStart[0] ? PositionAt(s) : HistAt(s);

        /// <summary>Wave 27 (wfix): <see cref="SteerWindowCells"/>' rule: the mean of the logical position over the past window plus half the
        /// window's displacement.</summary>
        private Vector2 LeadAveraged(float t)
        {
            float w = Mathf.Min(SteerWindowCells * Mathf.Max(Speed, 1) / 1000f, t - _episodeStart);
            if (w <= 0.0005f) return Cell;
            const int n = 12;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i <= n; i++) { var p = PastAt(t - w + w * i / n); sum += i == 0 || i == n ? p * 0.5f : p; }
            return sum / n + (PositionAt(t) - PastAt(t - w)) * 0.5f;
        }

        /// <summary>The cell a point in cell coordinates lies in (cell (x,y) spans x ± 0.5).</summary>
        private static int CellOf(float v) => Mathf.FloorToInt(v + 0.5f);

        /// <summary>true when the server would not walk the cell under <paramref name="p"/> (CELL_CHKNOPASS, Formats/GatFile.Check).</summary>
        public bool Blocked(Vector2 p) => _gat != null && _gat.Check(CellOf(p.x), CellOf(p.y));

        /// <summary>A point inside an unwalkable cell is pulled back towards the logical position (which is on the server's path)
        /// until the cell under it is walkable. When the logical position itself is on a blocked cell (a knockback into a wall,
        /// map data that differ: design notes §1) the body is drawn there, as Classic draws it.</summary>
        private Vector2 KeepWalkable(Vector2 p)
        {
            if (!Blocked(p)) return p;
            WallClamps++;
            Vector2 c = Cell;
            if (Blocked(c)) return c;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++) { float mid = (lo + hi) * 0.5f; if (Blocked(c + (p - c) * mid)) hi = mid; else lo = mid; }
            return c + (p - c) * lo;
        }

        /// <summary>A frame in which nothing moves: the next step, whenever it comes, starts from this frame's time (else the first
        /// frame of steering would move the body by the whole time it stood).</summary>
        private void SmoothIdle(float t) { _smoothT = t; _velNow = Vector2.zero; _vel = Vector2.zero; }

        /// <summary>Brings <see cref="DrawnCell"/> to local time <paramref name="t"/>. Safe to call more than once in a frame.</summary>
        private void SmoothStep(float t)
        {
            if (!_drawnInit) { _drawnInit = true; DrawnCell = Cell; _srvDrawn = Cell; _smoothT = t; }
            float dt = Mathf.Clamp(t - _smoothT, 0f, 0.1f);
            _smoothT = t;
            Vector2 prev = DrawnCell;
            Vector2 a = Smooth ? Averaged(t) : Cell;
            if (_wasSmooth != Smooth)
            {
                // the setting changed (also in the middle of a walk): the body stays where it is drawn, the difference fades
                // (free movement, wfree: our own body held off its cell, or standing off its centre, goes back to its cell the same way)
                if (Held.HasValue) { _off = Held.Value - a - _swing; Held = null; }
                else if (!_steering) _off += (_wasSmooth ? Averaged(t) : Cell) - a + _lastEnd;
                if (!Smooth) { _rho = Vector2.zero; _endOffActive = false; }
                _wasSmooth = Smooth;
            }
            Vector2 end = EndTerm(t); _lastEnd = end; // free movement (wfree): our own body's place in its cell (0 for every other unit)
            bool steer = Smooth && IsSelf && Steer.HasValue;
            if (steer)
            {
                if (Held.HasValue) Held = null; _rho = Vector2.zero; _endOffActive = false; // free movement (wfree): the stick again, from where the body stands
                // the stick steers (wmove: it leads; was: at the server's pace along the line, waiting SteerLead 0.3 ahead of the averaged
                // logical position and never more than MaxOffset from it): straight on along it at the walking pace, the server's unit steered
                // after the body (FreeSteerStep); a knockback / fast move of the server's unit (a slide that is not a stop) carries the body along
                Vector2 d = Steer.Value;
                if (!_steering) _steerCell = Cell;
                _steering = true;
                if (dt > 0f)
                {
                    if (_slideCarry) { DrawnCell = KeepWalkable(DrawnCell + (Cell - _steerCell)); if (_motion != Motion.Slide) _slideCarry = false; }
                    DrawnCell = FreeSteerStep(DrawnCell, d, dt);
                }
                _steerCell = Cell;
                _off = Vector2.zero; _offVel = Vector2.zero; _swing = Vector2.zero; _swingVel = Vector2.zero;
            }
            else if (FreeSelf && Held.HasValue)
            {
                // free movement (wfree): the stick was let go here: the body stays, whatever the server's unit walks (Game/StickSteer.cs settle)
                _steering = false; DrawnCell = Held.Value;
                _off = Vector2.zero; _offVel = Vector2.zero; _swing = Vector2.zero; _swingVel = Vector2.zero;
            }
            else
            {
                if (_steering) { _steering = false; _off = DrawnCell - a - end; _offVel = Vector2.zero; _swing = Vector2.zero; _swingVel = Vector2.zero; FreeExcessFrom(DrawnCell, 3f * OffsetSeconds); _lookWalking = false; _lookStillSince = -1f; } // the stick let go without a stop (a dialog, death, Classic): from here to the path, then the cell (wmove: the lead, up to FreeLeadHard, eased in as an excess over the half cell, not snapped)
                if (_off != Vector2.zero || _offVel != Vector2.zero)
                {
                    if (dt > 0f) _off = Vector2.SmoothDamp(_off, Vector2.zero, ref _offVel, OffsetSeconds, Mathf.Infinity, dt);
                    if (_off.sqrMagnitude < 1e-7f && _offVel.sqrMagnitude < 1e-4f) { _off = Vector2.zero; _offVel = Vector2.zero; }
                }
                if (_swing != Vector2.zero || _swingVel != Vector2.zero)
                {
                    if (dt > 0f) _swing = Vector2.SmoothDamp(_swing, Vector2.zero, ref _swingVel, SwingSeconds, Mathf.Infinity, dt);
                    if (_swing.sqrMagnitude < 1e-7f && _swingVel.sqrMagnitude < 1e-4f) { _swing = Vector2.zero; _swingVel = Vector2.zero; }
                }
                DrawnCell = a + _off + _swing + end;
            }
            if (!(FreeSelf && Held.HasValue))
            {
                // never more than half a cell from where the server's unit is on its path (free movement, wfree: our own body moving
                // may start further off, from where it stood, absorbed within the first step, and a walk to an exact point ends that
                // far off its cell; at rest it stands where it stopped (FreeMaxOffset, a safety net only); another player at its relayed
                // spot: RemoteMaxOffset, below)
                if (_freeExcess > 0f && dt > 0f) _freeExcess = Mathf.MoveTowards(_freeExcess, 0f, _freeExcessRate * dt);
                // (wmove) steering: the lead's own limit (FreeSteerStep) plus a safety net; at rest, and in Classic chosen while the stick led, at
                // least what is still being eased in (_freeExcess: only ever set for our own unit)
                float hardLead = 0f; if (FreeSelf && steer) FreeLead(out _, out hardLead);
                float lim = !FreeSelf ? MaxOffset + _freeExcess : steer ? hardLead + FreeLeadSafety : _motion != Motion.None ? MaxOffset + _freeExcess + end.magnitude : Mathf.Max(FreeMaxOffset, MaxOffset + _freeExcess);
                Vector2 o = DrawnCell - Cell; float m = o.magnitude;
                if (m > lim) { DrawnCell = Cell + o * (lim / m); OffsetClamps++; }
                // never inside a cell the server would not walk
                DrawnCell = KeepWalkable(DrawnCell);
            }
            if (IsSelf) _srvDrawn = DrawnCell;
            else
            {
                // free movement (wfree): another player drawn at the spot its own client sends over the relay (UI/RemoteSpots.cs), eased
                // in and out; the server's drawing (kept in _srvDrawn) wherever that spot is missing, stale or too far from the server's cell
                _srvDrawn = DrawnCell;
                bool valid = Smooth && _remoteTarget.HasValue && (_remoteTarget.Value - Cell).magnitude <= RemoteMaxOffset && !Blocked(_remoteTarget.Value);
                if (valid) _remoteLast = _remoteTarget.Value;
                if (_remoteSnap) { _remoteSnap = false; _remoteW = valid ? 1f : 0f; }
                else if (dt > 0f) _remoteW = Mathf.MoveTowards(_remoteW, valid ? 1f : 0f, dt / RemoteBlendSeconds);
                if (!Smooth) _remoteW = 0f;
                if (_remoteW > 0f) DrawnCell = Vector2.Lerp(_srvDrawn, _remoteLast, _remoteW);
            }
            if (dt > 0f)
            {
                _velNow = (DrawnCell - prev) / dt;
                _vel = Vector2.Lerp(_vel, _velNow, 1f - Mathf.Exp(-dt / 0.06f));
                Face();
            }
        }

        /// <summary>Smooth mode: the 8-direction facing from the drawn movement, with hysteresis; and the heading for 3-D bodies.</summary>
        private void Face()
        {
            _yawMoving = false;
            if (Smooth && Sprite != null && DrawnOwnsLook) { LookFromDrawn(); if (!(FreeSelf && _steering)) return; } // free movement (wfree); (wmove) steering: walk / idle by the drawn body, the facing by the stick below
            if (!Smooth || Sprite == null || !(_motion == Motion.Walk || _steering)) return;
            float walk = 1000f / Mathf.Max(Speed, 1);
            if (_vel.sqrMagnitude < 0.09f * walk * walk) return; // under 30 % of the walking pace: keep the facing
            // while the stick steers the body faces where the stick points (the drawn movement is that direction, but for the
            // frames in which the half cell holds the body back)
            Vector2 h = _steering && Steer.HasValue ? Steer.Value : _vel;
            float ang = Mathf.Atan2(-h.x, -h.y) * Mathf.Rad2Deg; if (ang < 0f) ang += 360f; // client directions × 45°: 0 south (−y), 90 west, 180 north, 270 east
            _yawMoving = true; _yawHeading = ang;
            _segFacing = false; // the first step from rest faced its own direction (Advance) until the body moved: now the drawn heading
            if (Mathf.Abs(Mathf.DeltaAngle((Sprite.Direction & 7) * 45f, ang)) > 22.5f + FacingHysteresis) Sprite.Direction = Mathf.RoundToInt(ang / 45f) & 7;
        }

        /// <summary>Free movement (wfree): the look of a body the server's walks do not draw (our own held after the stick; another player at
        /// its relayed spot): walking while it moves (over 30 % of the walking pace, or the relayed "moving"), idle 0.08 s after it stopped; the
        /// relayed facing for another player, the last one for our own.</summary>
        private void LookFromDrawn()
        {
            float walk = 1000f / Mathf.Max(Speed, 1), sp = _vel.magnitude;
            bool remote = !IsSelf;
            bool moving = sp > 0.2f * walk || (_steering && Steer.HasValue) || (remote && _remoteMoving && sp > 0.1f * walk);
            int g = Sprite.ActionGroup;
            if (moving)
            {
                _lookStillSince = -1f;
                _lookWalking = true;
                if (!Dead && (g == 0 || (IsPlayerType && g == (int)ActorSprite.PlayerAction.Standby)))
                    Sprite.Play(1, true, 150f / Mathf.Max(Speed, 20));
            }
            else
            {
                if (_lookStillSince < 0f) _lookStillSince = Time.time;
                if (_lookWalking && Time.time - _lookStillSince > 0.08f) { _lookWalking = false; if (Sprite.ActionGroup == 1) Sprite.Play(0, true); }
            }
            if (remote && _remoteFacing >= 0)
            {
                if ((Sprite.Direction & 7) != _remoteFacing) Sprite.Direction = _remoteFacing;
                _yawMoving = _lookWalking; _yawHeading = _remoteFacing * 45f;
            }
        }

        /// <summary>The yaw of a 3-D body (Sprites/GrannyUnit.cs), degrees = client direction × 45: in Smooth mode it turns
        /// towards the drawn heading while walking and towards the facing otherwise, at most <see cref="YawSpeed"/>; in
        /// Classic it is the facing itself, as before. Call once a frame.</summary>
        public float BodyYaw()
        {
            float facing = Sprite != null ? (Sprite.Direction & 7) * 45f : 0f;
            float now = Time.time;
            if (!Smooth || _yawT < 0f) _yaw = facing;
            else _yaw = Mathf.MoveTowardsAngle(_yaw, _yawMoving && (_motion == Motion.Walk || _steering || DrawnOwnsLook) ? _yawHeading : facing, YawSpeed * Mathf.Clamp(now - _yawT, 0f, 0.1f));
            _yawT = now;
            return _yaw;
        }

        /// <summary>Before the plan changes (a walk packet, a slide, a stop): the body as drawn now.</summary>
        private Vector2 SmoothBefore(float now)
        {
            // a packet may be handled before this unit's own Update in the frame: the logical position is brought to now first
            // (else the body's place would be taken one frame's walk behind and that much would be carried as an offset)
            if (_motion != Motion.None && _segs > 0)
            {
                int i = 0; while (i < _segs && now >= _segStart[i] + _segDur[i]) i++;
                SmoothKnots(i);
                Cell = PositionAt(now);
            }
            SmoothStep(now);
            return _srvDrawn; // (free movement, wfree: the server's drawing; another player's relayed spot is laid over it, not part of its plan)
        }

        /// <summary>After the plan changed and <see cref="Cell"/> is the logical position at <paramref name="now"/>: the body
        /// stays where it was drawn (<paramref name="before"/>); the difference to the new plan's drawing fades.
        /// <paramref name="knot"/>: the logical position at the change, the start of a new straight run.</summary>
        private void SmoothAfter(float now, Vector2 before, Vector2 knot, bool fromRest)
        {
            AddKnot(now, knot); _knSeg = 0;
            if (fromRest) _episodeStart = now;
            // wave 27 (wfix): another player's short walk given under way, heading the way it went: drawn from its past (SteerWindowCells)
            _lead = false;
            if (Smooth && !IsSelf && IsPlayerType && !fromRest && _motion == Motion.Walk && _segs > 0 && _segs <= SteerPlanSteps)
            {
                var went = Cell - PastAt(Mathf.Max(now - SteerWindowCells * Mathf.Max(Speed, 1) / 1000f, _episodeStart));
                _lead = Vector2.Dot(went, _pts[_segs] - _pts[0]) > 0f;
            }
            // the first step of a walk from rest faces its own direction (as Classic); a body already moving (the stick steers it,
            // or it is still easing from the last walk) keeps facing where it moves
            float walk = 1000f / Mathf.Max(Speed, 1);
            _segFacing = fromRest && !_steering && _vel.sqrMagnitude < 0.09f * walk * walk;
            if (_steering || (FreeSelf && Held.HasValue)) return; // (free movement, wfree: a held body stays where it is)
            Vector2 a = Smooth ? Averaged(now) : Cell;
            _off = before - a - _swing - EndTerm(now); // (free movement, wfree: the new plan's own end offset or rest offset is part of its drawing)
            if (Smooth && !fromRest && _motion == Motion.Walk)
            {
                // a new plan for a unit already walking (a chase re-pathing, a player steering): the plan beyond the old one was not
                // known, so the corner could not be rounded ahead; the body keeps the velocity it had and swings onto the new
                // path instead of turning on the spot (the fading offset starts with the difference of the two velocities)
                // only for a change of COURSE: a new plan that stops the unit (the answer to letting the stick go, a walk of no
                // steps) must not carry the body past its cell and back
                Vector2 va = (Averaged(now + 0.01f) - a) / 0.01f;
                if (va.sqrMagnitude >= 0.25f * walk * walk && _velNow.sqrMagnitude >= 0.25f * walk * walk)
                {
                    Vector2 dv = _velNow - va - _offVel;
                    if (Vector2.Dot(_velNow, va) > 0f)
                    {
                        Vector2 dir = va.normalized;
                        dv -= dir * Vector2.Dot(dv, dir);
                    }
                    _swingVel = Vector2.ClampMagnitude(dv, 2f * walk);
                }
            }
            else if (_off.sqrMagnitude < 1e-7f && _offVel.sqrMagnitude < 1e-4f) { _off = Vector2.zero; _offVel = Vector2.zero; }
        }

        /// <summary>The unit was placed somewhere at once (it appeared, a teleport, a walk seen from far away): the body is there too.</summary>
        private void SmoothTeleport()
        {
            _knN = 0; _knSeg = 0; _off = Vector2.zero; _offVel = Vector2.zero; _swing = Vector2.zero; _swingVel = Vector2.zero; _vel = Vector2.zero; _velNow = Vector2.zero; _steering = false;
            DrawnCell = Cell; _drawnInit = true; _smoothT = Time.time; _episodeStart = Time.time; _yawT = -1f;
            // free movement (wfree): placed at once on the cell's centre (a warp, appearing); another player's relayed spot takes over at once
            Held = null; _rho = Vector2.zero; _endOffActive = false; _lastEnd = Vector2.zero; _srvDrawn = Cell; _remoteSnap = true;
            _slideCarry = false; // (wmove)
            _lead = false; // (wave 27, wfix)
        }

        /// <summary>Wave 27 (wfix): the walk in hand ends at time <paramref name="t"/> (called before the unit stands on its last point): a body
        /// drawn by the past rule keeps where it is drawn, the difference to the cell fades (no jump).</summary>
        private void LeadEnds(float t)
        {
            if (!_lead) return;
            if (_motion == Motion.Walk && _segs > 0) _off += LeadAveraged(t) - _pts[_segs];
            _lead = false;
        }

        /// <summary>The logical walk passed the ends of its segments up to <paramref name="i"/>: their corners join the past.</summary>
        private void SmoothKnots(int i)
        {
            while (_knSeg < i && _knSeg < _segs) { _knSeg++; AddKnot(_segStart[_knSeg - 1] + _segDur[_knSeg - 1], _pts[_knSeg]); }
        }

        /// <summary>The ground's height under a point in cell coordinates: the GAT cell's four corner heights (Formats/GatFile.cs:
        /// SW, SE, NW, NE), interpolated — at a cell's centre this is the cell's mean height, where Classic draws a unit.</summary>
        private float GroundHeight(Vector2 p)
        {
            int cx = Mathf.Clamp(CellOf(p.x), 0, _gat.Width - 1), cy = Mathf.Clamp(CellOf(p.y), 0, _gat.Height - 1);
            var c = _gat.Get(cx, cy);
            float fx = Mathf.Clamp01(p.x + 0.5f - cx), fy = Mathf.Clamp01(p.y + 0.5f - cy);
            return -Mathf.Lerp(Mathf.Lerp(c.H0, c.H1, fx), Mathf.Lerp(c.H2, c.H3, fx), fy) * RoSpace.Scale;
        }
    }
}
