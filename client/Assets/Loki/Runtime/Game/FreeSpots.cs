using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Free movement (design note 2026-10-14: "exact sub-tile position sync across players"): the
    /// players on the relay see each other where each one's own client draws its character, not on the cell rAthena keeps it on.
    ///  - Sending: my drawn spot (<see cref="Unit.DrawnCell"/>), its velocity and facing, <see cref="SendHz"/> times a second while it
    ///    moves, at once when it starts, stops or turns more than <see cref="TurnDegrees"/>, once a second at rest; never while my character
    ///    is hidden (hide, cloak, chase walk, invisible, a hiding status: nobody learns a hidden player's place) or in Classic movement.
    ///  - Receiving: per player the spots of the last <see cref="KeepSeconds"/>, spaced by the SENDER's clock (the SPOT's seq, 10 ms
    ///    steps) mapped onto mine by the least-delayed arrival (network jitter does not shake the body); every frame the unit of that account
    ///    is given the spot <see cref="Delay"/> in the past, interpolated (past the last one: extrapolated by its velocity for at most
    ///    <see cref="Extrapolate"/>). The unit eases between that and the server's drawing (Sprites/Unit.Smooth.cs SetRemote): a spot older
    ///    than <see cref="StaleSeconds"/> (<see cref="StaleWalking"/> while the server walks the unit: a moving player sends 15 a second), on
    ///    another map, or too far from the unit's server position is not used.
    /// Runs before the units move (execution order -20), through <see cref="ISpotChannel"/> (Loki Host's relay; nothing on Loki Online yet).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class FreeSpots : MonoBehaviour
    {
        public const float SendHz = 15f, RestEvery = 1f, TurnDegrees = 20f, Delay = 0.1f, Extrapolate = 0.2f, StaleSeconds = 3f, StaleWalking = 0.5f, KeepSeconds = 1.5f;

        private ISpotChannel _ch; private System.Func<Unit> _self; private System.Func<uint, Unit> _unit; private System.Func<string> _map; private System.Func<bool> _playing;
        /// <summary>Spots sent / taken from the channel / units drawn at a relayed spot this frame.</summary>
        public int Sent, Received, Driven;
        /// <summary>Smokes: stop sending (a client that went silent); the last spot sent.</summary>
        public bool PauseSending { get; set; }
        public SpotWire.State LastSent { get; private set; }
        public ISpotChannel Channel => _ch;

        private struct Sample { public double SenderT; public float Recv; public Vector2 P, V; public byte Facing; public bool Moving; }
        private sealed class Track
        {
            public readonly List<Sample> S = new List<Sample>(); public string Map; public ushort LastRaw; public long Unwrapped; public bool Any;
            public float LastRecv, WalkSince = -1f; public Unit DrivenUnit;
        }
        private readonly Dictionary<uint, Track> _tracks = new Dictionary<uint, Track>();
        private readonly List<uint> _drop = new List<uint>();

        private float _lastSend = -10f; private Vector2 _lastDir; private bool _lastMoving; private int _links = -1; private string _lastMap;

        public static FreeSpots Create(GameObject host, ISpotChannel ch, System.Func<Unit> self, System.Func<uint, Unit> unit, System.Func<string> map, System.Func<bool> playing)
        {
            var f = host.AddComponent<FreeSpots>();
            f._ch = ch; f._self = self; f._unit = unit; f._map = map; f._playing = playing;
            return f;
        }

        /// <summary>The relayed spot this client would draw <paramref name="aid"/> at now (smokes; null: none).</summary>
        public Vector2? SpotOf(uint aid) => _tracks.TryGetValue(aid, out var t) && t.S.Count > 0 ? Target(t, Time.time) : (Vector2?)null;
        /// <summary>The last spot received from <paramref name="aid"/> as sent (smokes).</summary>
        public bool LastOf(uint aid, out Vector2 p, out float age)
        {
            p = default; age = float.MaxValue;
            if (!_tracks.TryGetValue(aid, out var t) || t.S.Count == 0) return false;
            var s = t.S[t.S.Count - 1]; p = s.P; age = Time.time - t.LastRecv; return true;
        }

        private void Update()
        {
            if (_ch == null) return;
            float now = Time.time;
            string map = _map?.Invoke();
            // ---- the others' spots
            while (_ch.TryTake(out var aid, out var s))
            {
                Received++;
                if (!_tracks.TryGetValue(aid, out var t)) { t = new Track(); _tracks[aid] = t; }
                if (t.Map != s.Map) { t.S.Clear(); t.Map = s.Map; t.Any = false; }
                if (!t.Any) { t.Any = true; t.LastRaw = s.Seq; t.Unwrapped = 0; }
                else { int d = (short)(ushort)(s.Seq - t.LastRaw); t.Unwrapped += d; t.LastRaw = s.Seq; }
                double senderT = t.Unwrapped * 0.01;
                if (t.S.Count > 0 && senderT < t.S[t.S.Count - 1].SenderT - 1.0) t.S.Clear(); // its clock went back (the player's game started again): from here
                if (t.S.Count > 0 && senderT <= t.S[t.S.Count - 1].SenderT) continue; // late or repeated: the newer one is in hand
                t.S.Add(new Sample { SenderT = senderT, Recv = now, P = new Vector2(s.X, s.Y), V = new Vector2(s.Vx, s.Vy), Facing = s.Facing, Moving = s.Moving });
                t.LastRecv = now;
                while (t.S.Count > 2 && t.S[t.S.Count - 1].SenderT - t.S[1].SenderT > KeepSeconds) t.S.RemoveAt(0);
            }
            Driven = 0; _drop.Clear();
            foreach (var kv in _tracks)
            {
                var t = kv.Value; var u = _unit?.Invoke(kv.Key);
                bool stale = t.S.Count == 0 || now - t.LastRecv > StaleSeconds || t.Map != map;
                // the server has walked it for a while and its client says nothing (a moving player sends 15 a second; at rest one a second, so a
                // walk that just began is given that long for its first spot)
                if (u != null && u.Walking) { if (t.WalkSince < 0f) t.WalkSince = now; } else t.WalkSince = -1f;
                if (u != null && !u.IsSelf && !stale && u.Walking && now - t.WalkSince > StaleWalking && now - t.LastRecv > StaleWalking) stale = true;
                if (u != t.DrivenUnit && t.DrivenUnit != null) t.DrivenUnit.SetRemote(null, 0, false);
                t.DrivenUnit = u;
                if (u == null || u.IsSelf) { if (now - t.LastRecv > StaleSeconds) _drop.Add(kv.Key); continue; }
                if (stale || !Unit.Smooth) { u.SetRemote(null, 0, false); if (now - t.LastRecv > StaleSeconds) _drop.Add(kv.Key); continue; }
                var last = t.S[t.S.Count - 1];
                u.SetRemote(Target(t, now), last.Facing, last.Moving);
                Driven++;
            }
            foreach (var k in _drop) { if (_tracks.TryGetValue(k, out var t) && t.DrivenUnit != null) t.DrivenUnit.SetRemote(null, 0, false); _tracks.Remove(k); }
            // ---- mine
            Send(now, map);
        }

        /// <summary>Where a track's player is drawn now: <see cref="Delay"/> in the past, on the sender's clock mapped onto mine by the
        /// least-delayed arrival among the spots in hand.</summary>
        private static Vector2 Target(Track t, float now)
        {
            var s = t.S; int n = s.Count;
            double off = double.MaxValue;
            for (int i = 0; i < n; i++) off = System.Math.Min(off, s[i].Recv - s[i].SenderT);
            double rt = now - Delay - off; // the render time on the sender's clock
            if (rt <= s[0].SenderT) return s[0].P;
            for (int i = 1; i < n; i++)
            {
                if (rt > s[i].SenderT) continue;
                var a = s[i - 1]; var b = s[i];
                double span = b.SenderT - a.SenderT;
                // a stop or start in between: no interpolation across a rest of more than a second (the sender sends at least 1 a second)
                float k = span > 1e-6 ? (float)((rt - a.SenderT) / span) : 1f;
                return Vector2.Lerp(a.P, b.P, Mathf.Clamp01(k));
            }
            var l = s[n - 1];
            return l.Moving ? l.P + l.V * Mathf.Min((float)(rt - l.SenderT), Extrapolate) : l.P;
        }

        private void Send(float now, string map)
        {
            var self = _self?.Invoke();
            if (self == null || string.IsNullOrEmpty(map) || !Unit.Smooth || PauseSending || !_ch.CanShare || (_playing != null && !_playing())) return;
            const int Hide = 0x2, Cloak = 0x4, Invisible = 0x40, ChaseWalk = 0x4000;
            if ((self.Option & (Hide | Cloak | Invisible | ChaseWalk)) != 0 || UnitEfstLooks.IsHiddenByStatus(self)) return; // never a hidden player's place
            float walk = 1000f / Mathf.Max(self.Speed, 1);
            Vector2 v = self.DrawnVelocity;
            bool moving = v.magnitude > 0.3f * walk || self.Steer.HasValue && v.magnitude > 0.05f * walk;
            Vector2 dir = v.sqrMagnitude > 1e-6f ? v.normalized : _lastDir;
            bool due;
            if (_ch.Links != _links || map != _lastMap) due = true;                       // a new link, a new map: at once
            else if (moving != _lastMoving) due = now - _lastSend >= 0.03f;               // a start or a stop
            else if (moving) due = now - _lastSend >= 1f / SendHz || (Vector2.Angle(dir, _lastDir) > TurnDegrees && now - _lastSend >= 0.03f);
            else due = now - _lastSend >= RestEvery;
            if (!due) return;
            var st = new SpotWire.State
            {
                Map = map, Seq = (ushort)((long)System.Math.Floor(Time.realtimeSinceStartupAsDouble * 100.0) & 0xffff),
                X = self.DrawnCell.x, Y = self.DrawnCell.y, Vx = moving ? v.x : 0f, Vy = moving ? v.y : 0f,
                Facing = (byte)(self.Sprite != null ? self.Sprite.Direction & 7 : 0), Flags = moving ? SpotWire.FlagMoving : (byte)0,
            };
            var f = SpotWire.Encode(st);
            if (f == null) return;
            _ch.Send(f); Sent++; LastSent = st;
            _lastSend = now; _lastMoving = moving; _lastDir = dir; _links = _ch.Links; _lastMap = map;
        }

        private void OnDestroy()
        {
            foreach (var t in _tracks.Values) if (t.DrivenUnit != null) t.DrivenUnit.SetRemote(null, 0, false);
        }
    }
}
