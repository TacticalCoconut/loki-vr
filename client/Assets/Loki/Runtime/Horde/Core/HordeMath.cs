#nullable disable
using System;

namespace Loki.Horde
{
    /// <summary>The engine's dice: xorshift128+ seeded by the run's seed (the same seed and inputs give the same run).</summary>
    public sealed class HordeRng
    {
        private ulong _a, _b;
        public HordeRng(ulong seed)
        {
            _a = Mix(seed ^ 0x9E3779B97F4A7C15UL); _b = Mix(_a ^ 0xD1B54A32D192ED03UL);
            if (_a == 0 && _b == 0) _b = 1;
        }
        private static ulong Mix(ulong z) { z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
        public ulong NextU64()
        {
            ulong s1 = _a, s0 = _b;
            _a = s0; s1 ^= s1 << 23; _b = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return _b + s0;
        }
        /// <summary>0 .. n-1 (n &gt; 0).</summary>
        public int Next(int n) => n <= 1 ? 0 : (int)((NextU64() >> 11) % (ulong)n);
        /// <summary>0 ≤ x &lt; 1.</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);
        /// <summary>True with this chance in percent.</summary>
        public bool Chance(double pct) => pct >= 100.0 || pct > 0.0 && NextDouble() * 100.0 < pct;
        public ulong State0 => _a;
        public ulong State1 => _b;
    }

    /// <summary>
    /// Movement in the engine's fixed point: 1 cell = <see cref="Unit"/> units, <see cref="HordeLimits.TickRate"/> ticks a second.
    /// A player's step is integer arithmetic only, so the client that predicts its own movement (HordeClient) gets exactly the
    /// host's place for the same inputs. Client wave 9 (h9move): the walk has a velocity that ramps up and down
    /// (<see cref="StepRamp"/>: no jerk at a start or a stop, a short acceleration from the tables), the arena's bounds are a
    /// soft wall (the step toward a bound shrinks over the last <c>wall</c> units), and knockbacks are impulses that decay
    /// (<see cref="Decay"/>) instead of teleports.
    /// </summary>
    public static class HordeMove
    {
        public const int Unit = 256;
        /// <summary>A stick direction on the wire: each axis -127..127; the vector's length at most 127.</summary>
        public const int StickMax = 127;

        /// <summary>The stick vector clamped to length 127 (integers only).</summary>
        public static void ClampStick(ref int dx, ref int dy)
        {
            if (dx > StickMax) dx = StickMax; else if (dx < -StickMax) dx = -StickMax;
            if (dy > StickMax) dy = StickMax; else if (dy < -StickMax) dy = -StickMax;
            int m2 = dx * dx + dy * dy;
            if (m2 > StickMax * StickMax)
            {
                int m = ISqrt(m2);
                dx = dx * StickMax / m; dy = dy * StickMax / m;
            }
        }

        /// <summary>One tick of a player's walk without a ramp (wave 8's step; kept for tests): speed in units per tick, the box in
        /// units (both ends included).</summary>
        public static void Step(ref int x, ref int y, int dx, int dy, int speed, int minX, int minY, int maxX, int maxY)
        {
            ClampStick(ref dx, ref dy);
            x += dx * speed / StickMax; y += dy * speed / StickMax;
            if (x < minX) x = minX; else if (x > maxX) x = maxX;
            if (y < minY) y = minY; else if (y > maxY) y = maxY;
        }

        /// <summary>Units per tick per tick to go from 0 to <paramref name="speedPerTick"/> in <paramref name="seconds"/> (at least 1,
        /// at least a tenth of the speed so a change of direction never takes more than ten ticks). Both ends compute it the
        /// same way from the same numbers.</summary>
        public static int RampPerTick(int speedPerTick, float seconds)
        {
            if (speedPerTick <= 0) return 1;
            if (seconds <= 0 || float.IsNaN(seconds)) return speedPerTick;
            int ticks = (int)Math.Round(seconds * HordeLimits.TickRate);
            if (ticks < 1) return speedPerTick;
            int a = (speedPerTick + ticks - 1) / ticks;
            return Math.Max(1, Math.Max(a, (speedPerTick + 9) / 10));
        }

        /// <summary>
        /// One tick of a player's walk with the ramp (client wave 9). The stick (-127..127 each, clamped to length 127) names the
        /// wanted velocity (speed × stick / 127, units per tick); the held velocity (vx, vy) moves toward it by at most
        /// <paramref name="accel"/> units per tick (by <paramref name="decel"/> when the wanted velocity is zero: a stop). The step
        /// is the velocity, shrunk over the last <paramref name="wall"/> units before a bound (a soft wall), then clamped inside.
        /// Integers only: the client's prediction is the host's.
        /// </summary>
        public static void StepRamp(ref int x, ref int y, ref int vx, ref int vy, int dx, int dy, int speed, int accel, int decel, int wall, int minX, int minY, int maxX, int maxY)
        {
            ClampStick(ref dx, ref dy);
            int tx = dx * speed / StickMax, ty = dy * speed / StickMax;
            int a = dx == 0 && dy == 0 ? decel : accel;
            if (a < 1) a = 1;
            long ex = tx - vx, ey = ty - vy;
            long d2 = ex * ex + ey * ey;
            if (d2 <= (long)a * a) { vx = tx; vy = ty; }
            else
            {
                int d = ISqrt(d2);
                vx += (int)(ex * a / d); vy += (int)(ey * a / d);
            }
            int sx = vx, sy = vy;
            if (wall > 0)
            {
                // the last `wall` units before a bound: the step toward it shrinks with the distance left, never below one unit
                // while there is distance left (the bound is reached, slowly, not hovered before)
                if (sx > 0 && maxX - x < wall) sx = Math.Max(Math.Min(1, maxX - x), (int)((long)sx * Math.Max(0, maxX - x) / wall));
                else if (sx < 0 && x - minX < wall) sx = Math.Min(Math.Max(-1, minX - x), (int)((long)sx * Math.Max(0, x - minX) / wall));
                if (sy > 0 && maxY - y < wall) sy = Math.Max(Math.Min(1, maxY - y), (int)((long)sy * Math.Max(0, maxY - y) / wall));
                else if (sy < 0 && y - minY < wall) sy = Math.Min(Math.Max(-1, minY - y), (int)((long)sy * Math.Max(0, y - minY) / wall));
            }
            x += sx; y += sy;
            if (x < minX) x = minX; else if (x > maxX) x = maxX;
            if (y < minY) y = minY; else if (y > maxY) y = maxY;
        }

        /// <summary>An impulse velocity (units per tick) a tick later: it keeps <paramref name="keepPct"/> percent (integers; it
        /// dies when both axes are within 1).</summary>
        public static void Decay(ref int vx, ref int vy, int keepPct)
        {
            vx = (int)((long)vx * keepPct / 100); vy = (int)((long)vy * keepPct / 100);
            if (vx > -2 && vx < 2) vx = 0;
            if (vy > -2 && vy < 2) vy = 0;
        }

        /// <summary>The velocity (units per tick) an impulse needs to travel <paramref name="units"/> in all before it dies at
        /// <paramref name="keepPct"/> per tick: v0 = units × (100 - keep) / 100 (the geometric sum v0 / (1 - keep)).</summary>
        public static int ImpulseFor(int units, int keepPct) => (int)((long)units * (100 - Math.Max(0, Math.Min(99, keepPct))) / 100);

        public static int ISqrt(long v)
        {
            if (v <= 0) return 0;
            long r = (long)Math.Sqrt(v);
            while (r * r > v) r--;
            while ((r + 1) * (r + 1) <= v) r++;
            return (int)r;
        }

        /// <summary>A stick axis from a float in -1..1.</summary>
        public static int StickAxis(float v)
        {
            if (float.IsNaN(v)) return 0;
            int i = (int)Math.Round(v * StickMax);
            return i > StickMax ? StickMax : i < -StickMax ? -StickMax : i;
        }

        /// <summary>
        /// The eight sprite directions of a walking angle (degrees, 0 = east, 90 = north), in the client's convention
        /// (Sprites/Unit.cs DirFromDelta: 0 south, 1 south-west, 2 west, 3 north-west, 4 north, 5 north-east, 6 east, 7 south-east).
        /// </summary>
        public static int Dir8(float deg)
        {
            if (float.IsNaN(deg)) return 0;
            int k = (int)Math.Round(deg / 45.0);
            return ((6 - k) % 8 + 8) % 8;
        }

        /// <summary>The angle (degrees) at the centre of a sprite direction (the inverse of <see cref="Dir8"/>).</summary>
        public static float Dir8Centre(int dir) => ((6 - dir) % 8 + 8) % 8 * 45f;

        /// <summary>
        /// <see cref="Dir8"/> with hysteresis: the direction changes only when the angle is more than 22.5 + <paramref name="hystDeg"/>
        /// degrees from the current direction's centre, so a stick held near a boundary between two directions never makes the
        /// sprite flicker (client wave 9).
        /// </summary>
        public static int Dir8Hysteresis(int current, float deg, float hystDeg)
        {
            if (float.IsNaN(deg)) return current;
            if (current < 0 || current > 7) return Dir8(deg);
            float diff = deg - Dir8Centre(current);
            while (diff > 180f) diff -= 360f;
            while (diff < -180f) diff += 360f;
            return Math.Abs(diff) <= 22.5f + hystDeg ? current : Dir8(deg);
        }

        /// <summary>The angle (degrees) of a stick / velocity, 0 = east, 90 = north.</summary>
        public static float AngleDeg(double dx, double dy) => (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
    }

    /// <summary>How the table follows the run character in VR (a Settings row of the client; the follow code is HordeTableFollow).</summary>
    public enum HordeFollowMode : byte
    {
        /// <summary>A small dead zone, a critically damped glide and a little lead in the walking direction (the default).</summary>
        Smooth = 0,
        /// <summary>Almost centred: a tiny dead zone and a quick, still damped glide.</summary>
        Tight = 1,
        /// <summary>The table stays; it glides to the character only once it has walked far from the centre.</summary>
        Fixed = 2,
    }

    /// <summary>
    /// Client wave 9 (h9move): the table's follow of the run character. The focus (what the arena is shifted to keep at the
    /// table's pivot) chases a target = the character's place plus a small lead in its walking direction, through a dead zone
    /// (the focus does not move while the target is within it: small steps do not rock the table) and a critically damped
    /// spring (it never overshoots, never snaps: a jump of the target, as at a late join, is a quick glide). Pure C#, no
    /// Unity: Loki Host's tests drive it. Every number is Loki's own (VR comfort: no snap, no shake), per mode.
    /// </summary>
    public sealed class HordeTableFollow
    {
        public HordeFollowMode Mode = HordeFollowMode.Smooth;
        /// <summary>The focus (cells).</summary>
        public float X, Y;
        private float _vx, _vy;
        private bool _set;

        /// <summary>Seconds the spring takes to close most of a gap (Unity's SmoothDamp meaning), per mode.</summary>
        public static float SmoothTime(HordeFollowMode m) => m == HordeFollowMode.Tight ? 0.10f : m == HordeFollowMode.Fixed ? 0.70f : 0.28f;
        /// <summary>Cells the target may stray from the focus before the focus follows, per mode.</summary>
        public static float DeadZone(HordeFollowMode m) => m == HordeFollowMode.Tight ? 0.08f : m == HordeFollowMode.Fixed ? 5.0f : 0.35f;
        /// <summary>Cells of lead at full walking speed (4 cells/s), per mode.</summary>
        public static float Lead(HordeFollowMode m) => m == HordeFollowMode.Tight ? 0.25f : m == HordeFollowMode.Fixed ? 0f : 0.6f;

        /// <summary>Puts the focus on the character at once (the run's start: the table is not yet looked at).</summary>
        public void Reset(float x, float y) { X = x; Y = y; _vx = _vy = 0; _set = true; }

        /// <summary>One frame: the character at (x, y) walking at (velX, velY) cells per second.</summary>
        public void Step(float x, float y, float velX, float velY, float dt)
        {
            if (float.IsNaN(x) || float.IsNaN(y)) return;
            if (!_set) { Reset(x, y); return; }
            if (dt <= 0) return;
            if (dt > 0.1f) dt = 0.1f;
            var m = Mode;
            float lead = Lead(m);
            float tx = x, ty = y;
            if (lead > 0 && !float.IsNaN(velX) && !float.IsNaN(velY))
            {
                float sp = (float)Math.Sqrt(velX * velX + velY * velY);
                if (sp > 0.05f) { float k = lead * Math.Min(1f, sp / 4f) / sp; tx += velX * k; ty += velY * k; }
            }
            // the dead zone: the spring pulls toward its edge, not toward the target itself
            float dz = DeadZone(m);
            float gx = tx - X, gy = ty - Y, g = (float)Math.Sqrt(gx * gx + gy * gy);
            if (g <= dz) { gx = gy = 0; }
            else { float k = (g - dz) / g; gx *= k; gy *= k; }
            // critically damped spring toward (X + gx, Y + gy): Unity's Mathf.SmoothDamp (Game Programming Gems 4, 1.10)
            float st = Math.Max(0.0001f, SmoothTime(m));
            float omega = 2f / st, w = omega * dt;
            float exp = 1f / (1f + w + 0.48f * w * w + 0.235f * w * w * w);
            float cx = -gx, cy = -gy; // current - target
            float ox = cx, oy = cy;
            float tvx = (_vx + omega * cx) * dt, tvy = (_vy + omega * cy) * dt;
            _vx = (_vx - omega * tvx) * exp; _vy = (_vy - omega * tvy) * exp;
            float nx = (X + gx) + (cx + tvx) * exp, ny = (Y + gy) + (cy + tvy) * exp;
            // never past the target (SmoothDamp's guard)
            if ((ox > 0) == (nx < X + gx) && ox != 0) { nx = X + gx; _vx = 0; }
            if ((oy > 0) == (ny < Y + gy) && oy != 0) { ny = Y + gy; _vy = 0; }
            X = nx; Y = ny;
        }
    }
}
