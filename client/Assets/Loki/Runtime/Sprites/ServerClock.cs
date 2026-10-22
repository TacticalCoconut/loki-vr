using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// The map server's clock as the client estimates it (client wave 3, w3move), so a move packet's moveStartTime can tell how
    /// long the unit has already been walking.
    ///  - The server's tick: ZC_NOTIFY_TIME 0x007f time.L = gettick() at the moment it answers CZ_REQUEST_TIME2 0x0360
    ///    (rA clif.cpp:11188-11196 clif_notify_time, :11203-11209 clif_parse_TickSend, which only stores the client's tick;
    ///    0x0360 is TickSend for PACKETVER &gt; 20180307, clif_shuffle.hpp:4723-4730). The client sends 0x0360 every 10 s
    ///    (Net/RoSession.cs, Update) and <see cref="ServerClockPinger"/> every 2 s in between.
    ///  - moveStartTime of 0x0087 / 0x09fd is the same gettick() truncated to 32 bits (rA clif.cpp:64-66 client_tick,
    ///    :2056, :1395), so differences are taken modulo 2^32.
    /// Each answer gives one sample: server tick = local time half-way between the request and the answer (the round trip is
    /// split evenly; roBrowserLegacy does the same, Engine/MapEngine.js:473 — a reimplementation, not the official client).
    /// The server's clock may run at another rate than ours and wander (the test rig's WSL clock runs ~4 % slow and moves by up
    /// to ±0.2 s between two samples 10 s apart: HANDOFF.md "The oracle's clock runs slow", and the w3move smoke's clock
    /// lines), so the rate is a least-squares slope over the samples of the last 10 s (kept within ±10 %) and the line goes
    /// through the newest sample; a sample more than 1 s off the line (a clock step, another process) starts the fit again.
    /// Nothing here is rAthena-specific: the SpacetimeDB module answers 0x0360 with the same packet.
    /// </summary>
    public static class ServerClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private const int MaxSamples = 12;
        private const double WindowMs = 10000;
        private static readonly double[] SLocal = new double[MaxSamples], SServer = new double[MaxSamples];
        private static int _count, _next;
        private static readonly Queue<double> Requests = new Queue<double>();
        private static double _a, _b = 1, _lastRtt, _lastResidual, _lastSampleAt = -1e9;
        private static bool _valid, _entryOnly;
        private static uint _serverBase; // the first sample's tick: server values are kept relative to it (unwrapped)

        /// <summary>Local monotonic time in ms.</summary>
        public static double LocalMs => Watch.Elapsed.TotalMilliseconds;
        public static bool Valid => _valid;
        public static int Samples => _count;
        public static double LastRttMs => _lastRtt;
        /// <summary>How far the last sample was from the line fitted before it (ms): the estimate's error at that moment.</summary>
        public static double LastResidualMs => _lastResidual;
        public static double Rate => _b;
        /// <summary>A request is on its way (its answer has not come yet).</summary>
        public static bool Outstanding => Requests.Count > 0 && LocalMs - Requests.Peek() < 5000;
        /// <summary>ms since the last answer.</summary>
        public static double SinceLastSampleMs => LocalMs - _lastSampleAt;

        /// <summary>Forget everything (another map server: another process, another gettick origin).</summary>
        public static void Reset() { _count = 0; _next = 0; _valid = false; _entryOnly = false; Requests.Clear(); _b = 1; }

        /// <summary>
        /// Entering a map server: ZC_ACCEPT_ENTER 0x02eb startTime = gettick() (rA clif.cpp:754-759 clif_authok). A first sample
        /// without a round trip (late by the one-way delay), replaced by the first 0x007f answer; a new map server is another
        /// process with another tick origin, so everything before is forgotten.
        /// </summary>
        public static void OnEnter(uint serverTick)
        {
            Reset();
            _serverBase = serverTick;
            SLocal[0] = LocalMs; SServer[0] = 0; _count = 1; _next = 1;
            Fit(); _valid = true; _entryOnly = true;
        }

        /// <summary>CZ_REQUEST_TIME2 0x0360 has just been sent (the answers come back in order).</summary>
        public static void RequestSent() { Requests.Enqueue(LocalMs); while (Requests.Count > 8) Requests.Dequeue(); }

        /// <summary>ZC_NOTIFY_TIME 0x007f arrived with the server's tick.</summary>
        public static void OnNotifyTime(uint serverTick)
        {
            double now = LocalMs;
            double local = now;
            while (Requests.Count > 0 && now - Requests.Peek() >= 5000) Requests.Dequeue(); // lost
            if (Requests.Count > 0) { double at = Requests.Dequeue(); _lastRtt = now - at; local = at + _lastRtt * 0.5; }
            _lastSampleAt = now;
            if (_entryOnly) { _entryOnly = false; _count = 0; _next = 0; }
            if (_count == 0) _serverBase = serverTick;
            double server = unchecked((int)(serverTick - _serverBase)); // modulo 2^32 around the first sample
            _lastResidual = _valid ? server - (_a + _b * local) : 0;
            if (_valid && System.Math.Abs(_lastResidual) > 1000) { _count = 0; _next = 0; _serverBase = serverTick; server = 0; }
            SLocal[_next] = local; SServer[_next] = server;
            _next = (_next + 1) % MaxSamples; if (_count < MaxSamples) _count++;
            Fit();
            _valid = true;
        }

        private static void Fit()
        {
            int newest = (_next + MaxSamples - 1) % MaxSamples;
            double newestAt = SLocal[newest];
            // the samples of the last 20 s
            double ml = 0, ms = 0; int n = 0;
            for (int i = 0; i < _count; i++) if (newestAt - SLocal[i] <= WindowMs) { ml += SLocal[i]; ms += SServer[i]; n++; }
            if (n >= 2)
            {
                ml /= n; ms /= n;
                double sxx = 0, sxy = 0, minL = double.MaxValue, maxL = double.MinValue;
                for (int i = 0; i < _count; i++)
                {
                    if (newestAt - SLocal[i] > WindowMs) continue;
                    double dl = SLocal[i] - ml; sxx += dl * dl; sxy += dl * (SServer[i] - ms);
                    if (SLocal[i] < minL) minL = SLocal[i]; if (SLocal[i] > maxL) maxL = SLocal[i];
                }
                // the slope needs samples 5+ s apart; until then the last rate (1 at first) is kept
                if (maxL - minL >= 5000 && sxx > 0) _b = System.Math.Max(0.9, System.Math.Min(1.1, sxy / sxx));
                // the line goes through the newest sample: a wandering clock is best known by its last word (replaying the
                // smoke's samples, this halved the error of the next sample against a line through the mean: rms 45-49 ms
                // against 67-92 ms on the test rig's WSL clock; w3move.md §4)
                _a = SServer[newest] - _b * SLocal[newest];
                return;
            }
            _a = SServer[newest] - _b * SLocal[newest];
        }

        /// <summary>The server's tick now (32-bit, like moveStartTime). Only meaningful when <see cref="Valid"/>.</summary>
        public static uint Now => unchecked(_serverBase + (uint)(long)System.Math.Round(_a + _b * LocalMs));

        /// <summary>How many ms ago (server time) the given server tick was; 0 without a clock. Negative = in the future.</summary>
        public static int MsSince(uint serverTick) => _valid ? unchecked((int)(Now - serverTick)) : 0;

        /// <summary>Server milliseconds → local seconds at the current rate estimate.</summary>
        public static float ToLocalSeconds(double serverMs) => (float)(serverMs / _b / 1000.0);
    }

    /// <summary>
    /// Asks the map server for its tick every <see cref="Seconds"/> s between the session's own 10 s requests, one request at
    /// a time (CZ_REQUEST_TIME2 0x0360 time.L = our tick, which rAthena only stores, rA clif.cpp:11203-11209). Added by
    /// LokiGame.W3Move.cs; the send and the "in the world" test come from there.
    /// </summary>
    public sealed class ServerClockPinger : MonoBehaviour
    {
        public const float Seconds = 2f;
        public System.Func<bool> Ready;
        public System.Action Send;
        private float _next;

        private void Update()
        {
            if (Ready == null || Send == null || !Ready()) return;
            if (Time.unscaledTime < _next || ServerClock.Outstanding || ServerClock.SinceLastSampleMs < Seconds * 1000f) return;
            _next = Time.unscaledTime + Seconds;
            Send();
            ServerClock.RequestSent();
        }
    }
}
