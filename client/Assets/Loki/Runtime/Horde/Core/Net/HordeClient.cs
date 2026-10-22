#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Loki.Net;

namespace Loki.Horde.Net
{
    /// <summary>What a byte transport (the relay channel's link thread) tells the Horde client. Any thread.</summary>
    public interface IHordeLink
    {
        /// <summary>A link is up; <paramref name="hostHasHorde"/>: its WELCOME carried <see cref="HordeWire.CapHorde"/>.</summary>
        void OnLinked(bool hostHasHorde);
        void OnUnlinked();
        /// <summary>One Horde payload (type 0x90..0x9F) as read: copy it before returning.</summary>
        void OnFrame(byte[] buffer, int offset, int length);
    }

    /// <summary>
    /// <see cref="IHordeChannel"/> over any byte transport (client wave 8, w8core). Today the transport is Loki Host's relay
    /// connection (Net/PresenceRelayChannel.cs: <c>AttachHorde(client)</c> + <c>client.Send = channel.SendHorde</c>); Loki
    /// Host's headless bots use it over their own TCP connection. Pure C#: no Unity type.
    /// Frames arrive on the transport's thread and are queued; everything else happens on the thread that calls Poll.
    ///
    /// My own movement (client wave 9, h9move): <see cref="SetMove"/> takes the stick (a radial dead zone, rescaled so that
    /// speed starts at zero at its edge; the length clamped to 1: a diagonal is no faster) and, when a tick is due, steps the
    /// prediction with the host's very integers (HordeMove.StepRamp with the velocity ramp) and sends that tick's input; Poll
    /// does the same only when SetMove was not called since the last Poll (a caller that never moves). So an input given right
    /// after Poll is never a frame late. Each snapshot acknowledges my inputs; the prediction is rebuilt from the host's
    /// place and velocity plus the inputs not yet acknowledged, and whatever the rebuilt place differs by is NOT shown at
    /// once: the difference is an offset that is spread over <see cref="CorrectionSeconds"/> (a jump of more than
    /// <see cref="SnapCells"/> cells, a late join or a far rise, is shown at once). Between two ticks the drawn place runs from
    /// the last tick's place to this one's, and a correction shifts both ends alike, so the drawn motion never stalls or steps
    /// at a snapshot (wave 8 reset the two ends at every snapshot: a step and a few frozen frames ten times a second).
    /// </summary>
    public sealed class HordeClient : IHordeChannel, IHordeLink
    {
        public readonly HordeContent Content;
        public readonly string ContentStamp;
        /// <summary>Writes one whole frame (u16 length + payload) to the link. Set by the transport.</summary>
        public Action<byte[]> Send;
        /// <summary>The real character's sex for jobs that depend on it (1 male, 2 female, 0 not known). Set before the link.</summary>
        public byte Sex;
        /// <summary>Optional log (one line per notable thing, never per frame).</summary>
        public Action<string> Log;
        /// <summary>The stick's radial dead zone (0..1 of its throw); below it the character stands, above it the speed rises
        /// from zero. Loki's own default; a client may set it from a setting.</summary>
        public float DeadZone = 0.15f;
        /// <summary>Seconds over which a prediction correction is spread into the drawn place (0.15-0.25 feels right: never a step).</summary>
        public float CorrectionSeconds = 0.2f;
        /// <summary>A correction larger than this (cells) is a jump (a late join, a rise elsewhere): shown at once.</summary>
        public float SnapCells = 4f;

        private readonly Func<double> _clock;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly ConcurrentQueue<byte[]> _in = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<int> _linkNotes = new ConcurrentQueue<int>(); // 1 linked with Horde, 2 linked without, 3 unlinked

        private HordeLinkState _state = HordeLinkState.NoLink;
        private bool _hostEnabled, _contentOk, _protocolOk, _helloSent;
        private readonly HordeStatus _status = new HordeStatus();
        private HordeRunInfo _run;
        private readonly HordeWorldView _world = new HordeWorldView();
        private readonly SnapshotDecoder _dec = new SnapshotDecoder();
        private HordeOffer _offer;
        public HordeResults LastResults { get; private set; }
        public HordeOffer OpenOffer => _offer;

        // prediction
        private float _stickX, _stickY;
        private bool _stickFresh;
        private ushort _seq;
        private double _acc, _lastNow = -1;
        private int _predX, _predY, _prevPredX, _prevPredY, _predVx, _predVy;
        private float _offX, _offY, _offRate;
        private float _facing = 270f; private int _dir8;
        private struct Sent { public ushort Seq; public int Dx, Dy; }
        private readonly List<Sent> _history = new List<Sent>();
        /// <summary>For tests and logs: corrections of my predicted place (in cells) larger than 1/16 cell, their sum, and the largest.</summary>
        public int Corrections; public float LargestCorrection, CorrectionsTotal;
        public int InputsSent, SnapshotsApplied, SnapshotsRefused;
        public long BytesIn;

        public HordeClient(HordeContent content, Func<double> clock = null)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            ContentStamp = HordeWire.ContentStamp(content);
            _clock = clock ?? (() => _sw.Elapsed.TotalSeconds);
        }

        // ------------------------------------------------------------------------------------------- IHordeLink (any thread)

        public void OnLinked(bool hostHasHorde) => _linkNotes.Enqueue(hostHasHorde ? 1 : 2);
        public void OnUnlinked() => _linkNotes.Enqueue(3);
        public void OnFrame(byte[] buffer, int offset, int length)
        {
            if (length < 1) return;
            var c = new byte[length]; Buffer.BlockCopy(buffer, offset, c, 0, length);
            _in.Enqueue(c);
        }

        // ------------------------------------------------------------------------------------------- IHordeChannel

        public HordeLinkState LinkState => _state;
        public HordeStatus Status => _status;
        public bool InRun => _run != null;
        public HordeRunInfo Run => _run;
        public HordeWorldView World => _world;
        public double Now => _clock();
        public int MySlot => _run != null ? _run.Slot : -1;

        public event Action StatusChanged;
        public event Action<HordeInvite> Invited;
        public event Action<HordeRunInfo> RunStarted;
        public event Action SnapshotArrived;
        public event Action<HordeEvent> Happened;
        public event Action<HordeFxView> FxBegan;
        public event Action<HordeFxView, bool> FxEnded;
        public event Action<HordeOffer> Offered;
        public event Action<int> OfferClosed;
        public event Action<HordeResults> Ended;
        public event Action<HordeRewardNote> Rewarded;
        public event Action Lost;

        private void Out(byte[] f) { try { Send?.Invoke(f); } catch (Exception e) { Log?.Invoke("[Horde] send failed: " + e.Message); } }

        public void RequestStart() { if (_state == HordeLinkState.Ready) Out(HordeWire.EncodeStart()); }
        /// <summary>Client wave 18 (h18rules), point 38: "Start a run" inviting only these players (empty: alone).</summary>
        public void RequestStart(System.Collections.Generic.IList<uint> invite) { if (_state == HordeLinkState.Ready) Out(HordeWire.EncodeStart(invite)); }
        public void Answer(uint runId, bool join) { if (_state == HordeLinkState.Ready) Out(HordeWire.EncodeAnswer(runId, join)); }
        public void Choose(int offerId, int index) { if (_state == HordeLinkState.Ready) Out(HordeWire.EncodeChoose(offerId, index)); }
        public void LeaveRun() { if (_state == HordeLinkState.Ready && _run != null) Out(HordeWire.EncodeLeave()); }
        /// <summary>Client wave 14 (w14kit), point 15: ends run <paramref name="runId"/> for everyone (H_END; the host checks that I was in it).</summary>
        public void EndRun(uint runId) { if (_state == HordeLinkState.Ready && runId != 0) Out(HordeWire.EncodeEnd(runId)); }
        /// <summary>Client wave 15 (w15hfx), point 19: pause (true) or resume (false) the run I am in, for everyone (H_PAUSE; the host decides).</summary>
        public void Pause(bool pause) { if (_state == HordeLinkState.Ready && _run != null) Out(HordeWire.EncodePause(pause)); }
        /// <summary>Casts an owned active laser-targeted weapon at the given map cell coordinates (cellX, cellY).</summary>
        public void CastActive(int weaponId, float cellX, float cellY)
        {
            if (_state != HordeLinkState.Ready || _run == null || weaponId <= 0) return;
            int tx = (int)Math.Round(cellX * HordeMove.Unit);
            int ty = (int)Math.Round(cellY * HordeMove.Unit);
            Out(HordeWire.EncodeCast(weaponId, tx, ty));
        }

        /// <summary>My stick (x east, y north, each -1..1). The radial dead zone is taken off and the rest rescaled to 0..1;
        /// a tick that is due is stepped with it at once.</summary>
        public void SetMove(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y)) { x = y = 0; }
            float m = (float)Math.Sqrt(x * x + y * y);
            float dz = Math.Max(0f, Math.Min(0.9f, DeadZone));
            if (m <= dz) { x = y = 0; }
            else
            {
                float k = Math.Min(1f, (m - dz) / (1f - dz)) / m; // the length past the dead zone, rescaled to 0..1 (a diagonal no faster)
                x *= k; y *= k;
            }
            _stickX = x; _stickY = y; _stickFresh = true;
            StepPrediction(_clock());
        }

        /// <summary>Where to draw MY character now (cells): the prediction, between the last two ticks, plus the fading correction.</summary>
        public float MyX => (_prevPredX + (_predX - _prevPredX) * (float)Math.Min(1.0, _acc / Tick)) / HordeSim.U + _offX;
        public float MyY => (_prevPredY + (_predY - _prevPredY) * (float)Math.Min(1.0, _acc / Tick)) / HordeSim.U + _offY;
        /// <summary>My walking direction (degrees, 0 = east, 90 = north; kept while standing) and its sprite direction with
        /// hysteresis, from my own stick (not the host's lagging word). Also written into World.Players[MySlot].</summary>
        public float MyFacing => _facing;
        public int MyDir8 => _dir8;
        /// <summary>My predicted velocity, cells per second.</summary>
        public float MyVx => _predVx * (float)HordeSim.TickRate / HordeSim.U;
        public float MyVy => _predVy * (float)HordeSim.TickRate / HordeSim.U;
        public bool MyMoving => _run != null && !MeFallen() && (_predVx != 0 || _predVy != 0);
        private const double Tick = 1.0 / HordeSim.TickRate;

        public void Poll()
        {
            double now = _clock();
            while (_linkNotes.TryDequeue(out int note))
            {
                if (note == 3)
                {
                    bool wasIn = _run != null;
                    _state = HordeLinkState.NoLink; _helloSent = false; _offer = null;
                    if (wasIn) { _run = null; Lost?.Invoke(); }
                    StatusChanged?.Invoke();
                }
                else if (note == 2) { _state = HordeLinkState.OldHost; _helloSent = false; StatusChanged?.Invoke(); }
                else
                {
                    _helloSent = true;
                    Out(HordeWire.EncodeHello(ContentStamp, Sex));
                    // Ready once the host's first status says the mode is on and our tables agree
                    _state = HordeLinkState.SwitchedOffByHost;
                }
            }
            while (_in.TryDequeue(out var p)) { BytesIn += p.Length + 2; Handle(p, now); }
            // the prediction steps in SetMove (with the frame's fresh stick); here only for a caller that gave no stick since the last Poll
            if (_stickFresh) _stickFresh = false; else StepPrediction(now);
        }

        private void Handle(byte[] p, double now)
        {
            switch (p[0])
            {
                case HordeWire.HStatus:
                    if (!HordeWire.DecodeStatus(p, 0, p.Length, _status, out bool ok)) return;
                    _hostEnabled = _status.Enabled; _contentOk = ok; _protocolOk = _status.Protocol == HordeWire.Version;
                    if (_helloSent)
                    {
                        var was = _state;
                        _state = !_protocolOk ? HordeLinkState.OtherProtocol : !_contentOk ? HordeLinkState.OtherVersion : !_hostEnabled ? HordeLinkState.SwitchedOffByHost : HordeLinkState.Ready;
                        if (_state != was && _state == HordeLinkState.OtherProtocol) Log?.Invoke($"[Horde] the host runs another version of Horde (its protocol {_status.Protocol}, mine {HordeWire.Version}): the mode is off here until one of us updates Loki; headsets and voice go on");
                    }
                    StatusChanged?.Invoke();
                    break;
                case HordeWire.HTowns:
                    HordeWire.DecodeTowns(p, 0, p.Length, _status.Towns);
                    StatusChanged?.Invoke();
                    break;
                case HordeWire.HInvite:
                {
                    if (_state != HordeLinkState.Ready) return;
                    var v = new HordeInvite();
                    if (HordeWire.DecodeInvite(p, 0, p.Length, v)) Invited?.Invoke(v);
                    break;
                }
                case HordeWire.HRun:
                {
                    if (_state != HordeLinkState.Ready) return; // another protocol: never a garbled run
                    var v = new HordeRunInfo();
                    if (!HordeWire.DecodeRun(p, 0, p.Length, v, out int tickNow)) return;
                    _run = v; _offer = null; _world.Clear(); _world.TickRate = v.TickRate; _world.SnapshotEvery = v.SnapshotEvery; _world.MySlot = v.Slot; _dec.Reset(v);
                    _history.Clear(); _acc = 0; _offX = _offY = 0; _offRate = 0; _predVx = _predVy = 0; _facing = 270f; _dir8 = 0;
                    _predX = _prevPredX = (v.X0 + v.X1) * HordeSim.U / 2; _predY = _prevPredY = (v.Y0 + v.Y1) * HordeSim.U / 2;
                    _awaitFirst = true;
                    Log?.Invoke($"[Horde] in run {v.RunId} as slot {v.Slot} on {v.Map} ({(v.Late ? "joined late" : "from the start")}; {v.TickRate} ticks, a snapshot every {v.SnapshotEvery})");
                    break;
                }
                case HordeWire.HSnap:
                {
                    if (_run == null || p.Length < 3) return;
                    ushort ack = (ushort)(p[1] | p[2] << 8);
                    if (!_dec.Apply(p, 3, p.Length - 3, _world, _run.Slot, now)) { SnapshotsRefused++; return; }
                    SnapshotsApplied++;
                    Reconcile(ack);
                    if (_awaitFirst) { _awaitFirst = false; RunStarted?.Invoke(_run); }
                    foreach (var kv in _dec.Ended) FxEnded?.Invoke(kv.Key, kv.Value);
                    foreach (var f in _dec.Begun) FxBegan?.Invoke(f);
                    SnapshotArrived?.Invoke();
                    foreach (var e in _dec.Events) Happened?.Invoke(e);
                    break;
                }
                case HordeWire.HOffer:
                    if (HordeWire.DecodeOffer(p, 0, p.Length, out var o)) { _offer = o; Offered?.Invoke(o); }
                    break;
                case HordeWire.HOfferClosed:
                    if (p.Length == 3) { int id = p[1] | p[2] << 8; if (_offer != null && _offer.Id == id) _offer = null; OfferClosed?.Invoke(id); }
                    break;
                case HordeWire.HResults:
                    if (HordeWire.DecodeResults(p, 0, p.Length, out var res))
                    {
                        LastResults = res;
                        if (_run != null && _run.RunId == res.RunId) { _run = null; _offer = null; }
                        Ended?.Invoke(res);
                    }
                    break;
                case HordeWire.HReward:
                    if (HordeWire.DecodeReward(p, 0, p.Length, out var rw)) Rewarded?.Invoke(rw);
                    break;
            }
        }

        private bool _awaitFirst;

        // ------------------------------------------------------------------------------------------- my own movement

        private bool MeFallen() { var me = _run != null ? _world.Players[_run.Slot] : null; return me == null || !me.Present || me.Fallen; }

        private void StepPrediction(double now)
        {
            if (_lastNow < 0) _lastNow = now;
            double dt = now - _lastNow; _lastNow = now;
            if (dt < 0) dt = 0; if (dt > 0.25) dt = 0.25; // a long hitch: do not flood the host with catch-up inputs
            // the correction's offset is spread evenly over CorrectionSeconds (set when it was taken)
            if (_offX != 0 || _offY != 0)
            {
                float m = (float)Math.Sqrt(_offX * _offX + _offY * _offY);
                float d = (float)(_offRate * dt);
                if (d >= m) { _offX = _offY = 0; } else { float k = (m - d) / m; _offX *= k; _offY *= k; }
            }
            if (_run == null || _awaitFirst || _state != HordeLinkState.Ready) { _acc = 0; return; }
            if (_world.Paused) { _acc = 0; return; } // the world waits while someone chooses: no tick, no input (the host applies none)
            _acc += dt;
            int steps = 0;
            while (_acc >= Tick && steps < 5)
            {
                _acc -= Tick; steps++;
                if (MeFallen())
                {
                    _predVx = _predVy = 0;
                    _prevPredX = _predX; _prevPredY = _predY;
                    _history.Clear();
                    continue;
                }
                int dx = HordeMove.StickAxis(_stickX), dy = HordeMove.StickAxis(_stickY);
                HordeMove.ClampStick(ref dx, ref dy);
                _seq++;
                Out(HordeWire.EncodeInput(_seq, dx, dy)); InputsSent++;
                _prevPredX = _predX; _prevPredY = _predY;
                Step(ref _predX, ref _predY, ref _predVx, ref _predVy, dx, dy, _world.Tick + _history.Count + 1); // (client wave 17, h17boss: the input's tick, for a boss's pull)
                if (dx != 0 || dy != 0) { _facing = HordeMove.AngleDeg(dx, dy); _dir8 = HordeMove.Dir8Hysteresis(_dir8, _facing, HordeWorldView.Dir8HysteresisDeg); }
                _history.Add(new Sent { Seq = _seq, Dx = dx, Dy = dy });
                if (_history.Count > 200) _history.RemoveAt(0);
            }
            if (_acc > Tick) _acc = Tick;
            // my slot's view carries my own facing and motion (the host's word about me lags a round trip)
            var me = _world.Players[_run.Slot];
            if (me.Present) { me.Facing = _facing; me.Dir8 = _dir8; me.Moving = MyMoving; me.Vx = MyVx; me.Vy = MyVy; }
        }

        private void Step(ref int x, ref int y, ref int vx, ref int vy, int dx, int dy, int tick = -1)
        {
            var r = _run;
            int speed = Math.Max(1, _dec.MySpeed);
            var rules = Content.Rules;
            HordeMove.StepRamp(ref x, ref y, ref vx, ref vy, dx, dy, speed, HordeMove.RampPerTick(speed, rules.MoveAccelSeconds), HordeMove.RampPerTick(speed, rules.MoveDecelSeconds),
                Math.Max(0, HordeSim.Units(rules.WallSoftCells)), r.X0 * HordeSim.U, r.Y0 * HordeSim.U, (r.X1 + 1) * HordeSim.U - 1, (r.Y1 + 1) * HordeSim.U - 1);
            // client wave 17 (h17boss): a boss's pull, as the host applies it after this input's walk (HordeSim.PullStep); the tick is the one
            // the host will apply the input at (the snapshot's tick + its place among the inputs not yet acknowledged)
            if (tick >= 0) HordeSim.PullView(_world, tick, ref x, ref y, r.X0 * HordeSim.U, r.Y0 * HordeSim.U, (r.X1 + 1) * HordeSim.U - 1, (r.Y1 + 1) * HordeSim.U - 1);
        }

        private void Reconcile(ushort ack)
        {
            // drop what the host has applied, replay the rest on top of its place and velocity
            _history.RemoveAll(s => (short)(s.Seq - ack) <= 0);
            int x = _dec.MyX, y = _dec.MyY, vx = _dec.MyVx, vy = _dec.MyVy;
            if (!MeFallen()) { int k = 0; foreach (var s in _history) Step(ref x, ref y, ref vx, ref vy, s.Dx, s.Dy, _world.Tick + ++k); } else { _history.Clear(); vx = vy = 0; } // (client wave 17, h17boss: each input's tick)
            float ex = (_predX - x) / (float)HordeSim.U, ey = (_predY - y) / (float)HordeSim.U;
            float err = (float)Math.Sqrt(ex * ex + ey * ey);
            if (err > 1f / 16) { Corrections++; CorrectionsTotal += err; if (err > LargestCorrection) LargestCorrection = err; }
            if (err > SnapCells) { _offX = _offY = 0; _offRate = 0; } // a jump (a late join, a rise elsewhere): snap
            else if (err > 0)
            {
                _offX += ex; _offY += ey;
                float m = (float)Math.Sqrt(_offX * _offX + _offY * _offY);
                _offRate = Math.Max(_offRate, m / Math.Max(0.05f, CorrectionSeconds));
            }
            // the prediction moves to the rebuilt place; the drawn place keeps its run between the last two ticks (both ends shift alike)
            int sx = x - _predX, sy = y - _predY;
            _predX = x; _predY = y; _prevPredX += sx; _prevPredY += sy; _predVx = vx; _predVy = vy;
        }
    }
}
