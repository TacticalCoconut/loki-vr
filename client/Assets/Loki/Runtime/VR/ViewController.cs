using System;
using Loki.Sprites;
using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Three ways to see the world, switched from the wrist menu (View tab) or by holding the left stick click:
    ///   Table        — the miniature diorama on a table in front of you (pan/scale/turn freely).
    ///   Follow       — a larger miniature that keeps your character in the middle of the table as it walks.
    ///   First person — life size, your eyes where the character's eyes are; the world moves as you walk.
    /// All three only move/scale the diorama root (the rig and your real body never move), which keeps the UI panels,
    /// hands and pointer working the same in every mode.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class ViewController : MonoBehaviour
    {
        public enum ViewMode { Table, Follow, FirstPerson }
        public ViewMode Mode { get; private set; } = ViewMode.Table;
        /// <summary>Client wave 19 (w19small), point 55 (design note: "That's the default view mode to be followed."): the view the first map of a
        /// launch opens in (LokiGame.cs EnterMap) for a NEW player. Client wave 19 (w19view41), point 55: a player's chosen view
        /// (<see cref="Chosen"/>) is kept with the account (VR/Comfort.cs LastView) and the first map opens in it instead.</summary>
        public const ViewMode DefaultMode = ViewMode.Follow;
        public float FollowMetresPerCell = 0.06f, FirstPersonMetresPerCell = 0.7f, EyeHeight = 1.5f;
        /// <summary>Follow: the character is seen this many degrees more steeply from above than a spot 0.75 m ahead at table height
        /// would give (specified after the 2026-10-01 playtest: "raise the angle of the camera by about 7 degrees").</summary>
        public float FollowExtraPitch = 7f;
        public event Action<ViewMode> Changed;

        // ---- client wave 11 (w11menu), design specification: "When I am in follow mode, allow the player to
        // also control the angle of the camera by moving my hands up and down, while rotating the camera." While the turn stick is
        // deflected (smooth turning: past its dead zone; snap turning: while it is still held over after the snap; DioramaRig.
        // TurnStickHeld), the turning hand's height moves the character's spot along its arc about the eyes: the spot goes up and
        // down with the hand, one to one (hand up = the spot up = a flatter look; hand down = a steeper one), i.e. the extra pitch
        // changes by (hand movement) / (the spot's distance x cos(pitch)): 1.50 rad per metre = 0.86 degrees per centimetre at the
        // default spot (0.99 m from eyes 1.60 m up, 47.9 degrees down). Only the spot moves, as PlaceAnchor always did: the world is
        // never tilted. The first FollowHandDeadZone of hand travel does nothing (a hand that only turns the stick does not drift
        // the view), the pitch eases to its target, stays between FollowMinDown and FollowMaxDown degrees below the horizon and
        // above the floor, and is kept per player (VR/Comfort.cs FollowPitch, loki-user.json followPitch).
        // Client wave 14 (w14laser), point 11 (design note, 2026-10-07: "The way that it is implemented right now, where you need to hold the turn stick
        // and then raise or lower my hand is very bad. require that to be instead done through the resizing mode by raising or lowering my hand
        // while I have a grip held"; answer 9: either hand's single grip, two grips still resize): the hand that steers is the one whose grip
        // ALONE is the camera's (DioramaRig.PitchHand: resize mode, no board held); the turn stick no longer does. The rest is wave 11's.
        public const float FollowHandDeadZone = 0.015f, FollowMinDown = 12f, FollowMaxDown = 80f, FollowFloorMargin = 0.10f, FollowEase = 14f;
        /// <summary>The hand is steering the pitch now, and the extra pitch it is easing to (smoke, log).</summary>
        public bool PitchHeld { get; private set; }
        public float PitchTarget { get; private set; }
        private float _pitchGrabY, _pitchGrabDown; private Vector3 _anchorHead, _anchorFwd; private bool _pitchDirty; private Transform _pitchHand;

        /// <summary>First person: our own character (every object under it: body, headgears, weapon, name plate, overlays, spheres…) is
        /// put on this layer right before the head camera draws, and the head camera leaves the layer out, so it never stands in front of
        /// our eyes (the design's 2026-10-01 playtest: "the character's model gets in the way of the camera"). Before this the renderers were
        /// switched off once, when the view changed, and every part made later — the body itself after a map change, a status overlay,
        /// a new headgear — showed again. A user layer nothing else in Loki uses (UI.UiKit.PanelLayer is 5); the desktop spectator camera
        /// and screenshots still draw it, raycasts still hit it (they test every layer).</summary>
        public const int SelfLayer = 8;
        /// <summary>Objects moved onto <see cref="SelfLayer"/> by the last sweep (for the log and checks).</summary>
        public int LastSelfSweep { get; private set; }

        private LokiBootstrap _boot; private DioramaRig _rig; private Func<Unit> _self;
        private float _tableScale = 1f; private Vector3 _anchor; private bool _anchorSet;
        public float LastFollowPitch { get; private set; }

        public void Init(LokiBootstrap boot, DioramaRig rig, Func<Unit> self)
        {
            _boot = boot; _rig = rig; _self = self;
            FollowExtraPitch = PitchTarget = Comfort.FollowPitch; // client wave 11 (w11menu): the player's own angle
            Camera.onPreCull -= BeforeCull; Camera.onPreCull += BeforeCull;
        }

        private void OnDestroy() { Camera.onPreCull -= BeforeCull; }

        /// <summary>Client wave 18 (w18acct): the account's settings came in: the Follow pitch eases to the player's own angle
        /// (VR/Comfort.cs FollowPitch), unless a hand is steering it right now.</summary>
        public void TakeFollowPitch(float extra) { if (!PitchHeld) PitchTarget = extra; }

        /// <summary>Right before the head camera culls, in first person: our character's whole hierarchy goes on <see cref="SelfLayer"/>
        /// (a part made since the last frame included; inactive branches draw nothing and are done when they appear).</summary>
        private void BeforeCull(Camera cam)
        {
            if (_rig == null || cam != _rig.Head || Mode != ViewMode.FirstPerson) return;
            var s = _self?.Invoke();
            LastSelfSweep = s != null ? Sweep(s.transform, SelfLayer) : 0;
        }

        private static int Sweep(Transform t, int layer)
        {
            int n = 1;
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0, c = t.childCount; i < c; i++) { var ch = t.GetChild(i); if (ch.gameObject.activeSelf) n += Sweep(ch, layer); }
            return n;
        }

        /// <summary>The head camera draws <see cref="SelfLayer"/> in every view but first person.</summary>
        private void ApplySelfLayerMask()
        {
            if (_rig == null || _rig.Head == null) return;
            int bit = 1 << SelfLayer;
            _rig.Head.cullingMask = Mode == ViewMode.FirstPerson ? _rig.Head.cullingMask & ~bit : _rig.Head.cullingMask | bit;
        }

        public void Cycle() => Choose((ViewMode)(((int)Mode + 1) % 3));
        /// <summary>Client wave 19 (w19view41), point 55: the PLAYER chose this view (the view button's hold: <see cref="Cycle"/>; the menu's
        /// View page): <see cref="Chosen"/> then keeps it (LokiGame.cs). A view the game sets (<see cref="SetMode"/>) is not a choice.</summary>
        public void Choose(ViewMode m) { SetMode(m); Chosen?.Invoke(m); }
        public event Action<ViewMode> Chosen;

        public void SetMode(ViewMode m)
        {
            if (_rig == null) return;
            var d = _rig.Diorama;
            if (Mode == ViewMode.Table) _tableScale = d.localScale.x; // remember the user's table zoom
            Mode = m;
            float perCellTable = _boot.DioramaScale; // metres per cell at diorama scale 1
            switch (m)
            {
                case ViewMode.Table:
                    d.localScale = Vector3.one * _tableScale;
                    _rig.RecentreDiorama();
                    _rig.Head.farClipPlane = 200f;
                    break;
                case ViewMode.Follow:
                    d.localScale = Vector3.one * (FollowMetresPerCell / perCellTable);
                    PlaceAnchor();
                    _rig.Head.farClipPlane = 300f;
                    break;
                case ViewMode.FirstPerson:
                    d.localScale = Vector3.one * (FirstPersonMetresPerCell / perCellTable);
                    _rig.Head.farClipPlane = 1500f;
                    break;
            }
            ApplySelfLayerMask();
            // client wave 14 (w14play), point 21: names at least 1.3° tall in first person (UI/Overhead.cs MinNameDegrees)
            UI.Overhead.MinNameDegrees = m == ViewMode.FirstPerson ? UI.Overhead.FirstPersonNameDegrees : 0f;
            Debug.Log($"[View] mode {m} (diorama scale {d.localScale.x:F2}, {perCellTable * d.localScale.x:F3} m per cell)");
            Changed?.Invoke(m);
        }

        /// <summary>Follow mode: the character's spot is in front of you — 0.75 m ahead at table height, then turned
        /// <see cref="FollowExtraPitch"/> degrees further down about your eyes (the same distance from them), so you look down at it
        /// that much more steeply.</summary>
        public void PlaceAnchor()
        {
            var head = _rig.Head.transform;
            var f = head.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            _anchorHead = head.position; _anchorFwd = f; // client wave 11: the pitch can change later without the spot moving sideways
            var v = ApplyPitch();
            _anchorSet = true;
            Debug.Log($"[View] follow spot {FollowExtraPitch:0.#}° steeper: looking {LastFollowPitch:0.0}° down at it, {v.magnitude:0.00} m from the eyes, {_anchor.y - _rig.TableHeight:+0.00;-0.00} m against the table height");
        }

        /// <summary>The spot for the eyes and heading remembered by <see cref="PlaceAnchor"/> at the extra pitch of now; returns the
        /// line from the eyes to it.</summary>
        private Vector3 ApplyPitch()
        {
            var level = new Vector3(_anchorHead.x, 0, _anchorHead.z) + _anchorFwd * 0.75f + Vector3.up * _rig.TableHeight;
            var right = Vector3.Cross(Vector3.up, _anchorFwd); // Unity: a positive turn about +right tips a forward line down
            var v = Quaternion.AngleAxis(FollowExtraPitch, right) * (level - _anchorHead);
            _anchor = _anchorHead + v;
            LastFollowPitch = Mathf.Atan2(_anchorHead.y - _anchor.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
            return v;
        }

        /// <summary>Degrees below the horizon of the level spot (0.75 m ahead at table height) from the eyes PlaceAnchor used, and
        /// the spot's distance from them.</summary>
        private void LevelSpot(out float levelDown, out float dist)
        {
            float dy = _anchorHead.y - _rig.TableHeight;
            levelDown = Mathf.Atan2(dy, 0.75f) * Mathf.Rad2Deg; dist = Mathf.Sqrt(0.75f * 0.75f + dy * dy);
        }

        /// <summary>The flattest and the steepest look allowed, as degrees below the horizon: never above eye level, never under the floor.</summary>
        public (float min, float max) FollowDownRange()
        {
            LevelSpot(out _, out float dist);
            float floor = _rig.transform.position.y + FollowFloorMargin;
            float maxByFloor = Mathf.Asin(Mathf.Clamp((_anchorHead.y - floor) / Mathf.Max(dist, 0.1f), 0f, 1f)) * Mathf.Rad2Deg;
            return (FollowMinDown, Mathf.Max(FollowMinDown, Mathf.Min(FollowMaxDown, maxByFloor)));
        }

        /// <summary>Follow only: while one grip alone is the camera's (resize mode), that hand's height steers the pitch (see the constants above).</summary>
        private void UpdateFollowPitch()
        {
            var hand = _rig.PitchHand; // client wave 14 (w14laser), point 11
            bool held = hand != null && _anchorSet;
            float handY = held ? _rig.transform.InverseTransformPoint(hand.position).y : 0f;
            LevelSpot(out float levelDown, out float dist);
            var (minDown, maxDown) = FollowDownRange();
            // (the other hand's grip alone takes over: its own height from here, no jump)
            if (held && PitchHeld && hand != _pitchHand) { _pitchHand = hand; _pitchGrabY = handY; _pitchGrabDown = levelDown + FollowExtraPitch; PitchTarget = FollowExtraPitch; }
            if (held && !PitchHeld) { PitchHeld = true; _pitchHand = hand; _pitchGrabY = handY; _pitchGrabDown = levelDown + FollowExtraPitch; PitchTarget = FollowExtraPitch; }
            else if (!held && PitchHeld)
            {
                PitchHeld = false;
                if (_pitchDirty) { _pitchDirty = false; Comfort.FollowPitch = PitchTarget; Comfort.Save(); Debug.Log($"[View] follow pitch kept: {PitchTarget:0.0}° extra ({levelDown + PitchTarget:0.0}° down)"); }
            }
            if (PitchHeld)
            {
                float dy = handY - _pitchGrabY;
                dy = Mathf.Abs(dy) <= FollowHandDeadZone ? 0f : dy - Mathf.Sign(dy) * FollowHandDeadZone;
                // the spot's height follows the hand's: its height below the eyes = dist x sin(down)
                float below = dist * Mathf.Sin(_pitchGrabDown * Mathf.Deg2Rad) - dy;
                float down = Mathf.Asin(Mathf.Clamp(below / Mathf.Max(dist, 0.1f), -1f, 1f)) * Mathf.Rad2Deg;
                down = Mathf.Clamp(down, minDown, maxDown);
                float target = down - levelDown;
                if (Mathf.Abs(target - PitchTarget) > 0.01f) { PitchTarget = target; _pitchDirty = true; }
            }
            else PitchTarget = Mathf.Clamp(levelDown + PitchTarget, minDown, maxDown) - levelDown;
            if (Mathf.Abs(FollowExtraPitch - PitchTarget) > 0.005f)
            {
                FollowExtraPitch = Mathf.Lerp(FollowExtraPitch, PitchTarget, 1f - Mathf.Exp(-FollowEase * Time.deltaTime));
                if (Mathf.Abs(FollowExtraPitch - PitchTarget) <= 0.02f) FollowExtraPitch = PitchTarget;
                ApplyPitch();
            }
        }

        /// <summary>After the character is recreated (map change): first person needs nothing (the sweep finds the new one before the
        /// next frame is drawn); Follow places its spot if it has none.</summary>
        public void OnSelfRecreated() { if (Mode == ViewMode.Follow && !_anchorSet) PlaceAnchor(); }

        private void LateUpdate()
        {
            if (_rig == null || Mode == ViewMode.Table) return;
            var self = _self?.Invoke();
            if (self == null || _boot.Map == null) return;
            var d = _rig.Diorama;
            // keep the character at the diorama pivot: the DRAWN body (client wave 10, w10move: Unit.DrawnCell, smoothed; the logical cell
            // would move the whole world in cell-to-cell zigzags under the player)
            _boot.FocusCell(self.DrawnCell, self.transform.localPosition.y);
            if (Mode == ViewMode.Follow)
            {
                if (!_anchorSet) PlaceAnchor();
                UpdateFollowPitch(); // client wave 11 (w11menu): a hand's height steers the pitch (client wave 14, w14laser: while its grip alone is held in resize mode)
                d.position = _anchor;
            }
            else
            {
                // First person: your eyes are the character's eyes (EyeHeight above its feet), wherever your real head is —
                // seated or standing gives the same view.
                var hp = _rig.Head.transform.position;
                d.position = new Vector3(hp.x, hp.y - EyeHeight, hp.z);
                d.localScale = Vector3.one * (FirstPersonMetresPerCell / _boot.DioramaScale); // two-hand scaling is off in first person
            }
        }
    }
}
