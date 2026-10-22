using System;
using System.Collections.Generic;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 15 (w15grab): the design's points 5, 8, 11 and 28 on wave 14 (design notes) for the held panels of
    /// LokiGame.W13Grab.cs (every grab: by hand or by laser, by grip or by trigger, the bag's pull).
    ///   Point 5   (design note: "While I have panel grabbed, with my grip or trigger, allow the player to drag it closer and further away by using the
    ///             joystick on whatever hand is holding it"): the HOLDING hand's stick up pushes the panel away, down pulls it nearer,
    ///             <see cref="LokiGame.GrabPushSpeed"/> m/s. By laser it slides along the laser (<see cref="LokiGame.GrabMinDist"/>-
    ///             <see cref="LokiGame.GrabMaxDist"/> m from the hand, as before); by hand along the line from the eyes through the grab point
    ///             (no nearer than <see cref="LokiGame.GrabMinDist"/> m, no farther than <see cref="LokiGame.GrabMaxDist"/> m from the eyes by
    ///             the stick). While a hand holds a panel its stick has no other job (VR/LokiInput.cs StickValue is 0 for that hand: no walking
    ///             with the walking hand, no turning with the pointing hand, no menu stick); the other hand's stick is unchanged.
    ///   Point 8   (design note: "Give it a little bit of smoothing"): a held panel's grab point follows its target through an adaptive low-pass
    ///             (a "1 euro" style filter): at rest the time constant is <see cref="SmoothRest"/> s × the setting, on a real move it drops to
    ///             <see cref="SmoothMove"/> s × the setting (the target's speed, itself smoothed over <see cref="SmoothSpeedTau"/> s, against
    ///             <see cref="SmoothFast"/> m/s); a long lever (a laser grab, or a free tilt's grab point far from the hand) amplifies a
    ///             tremor, so beyond <see cref="SmoothArm"/> m the speed limit scales with the lever and the rest constant up to twice. The
    ///             rotation was smoothed by the same factor until client wave 19 (point 61: see below). "Panel smoothing" (Settings → Controls → Menus): 0 to 100 % in 5 % steps, default
    ///             50 % (at rest 0.10 s: a 10 Hz tremor about 6 times smaller; a 0.6 m/s move nearly unfiltered); 0 = off (the panel sits on
    ///             its target every frame, as before). Client wave 18 (w18menu), point 6: the slider goes to 200 % (VR/Comfort.cs
    ///             PanelSmoothingMax); both time constants are the setting × the numbers above, so 200 % doubles them (at rest 0.40 s: a 10 Hz
    ///             tremor about 25 times smaller, half what 100 % leaves; a 0.6 m/s move about 2.4 cm behind, within 1 mm of its place about 1.3 s after it stopped).
    ///   Point 11  (design note: "It needs to be facing me as I rotate it. When it is in the locked rotation mode. Standing up"): an UPRIGHT held panel
    ///             faces the player continuously: about the vertical only, towards the eyes' vertical axis. Wave 14's point 16 (the bag pulled
    ///             out from behind must not spin past the head) is kept: near the head (its middle within <see cref="FaceNear"/> m of the eyes'
    ///             axis) or behind the eyes' plane (beyond <see cref="FaceBack0"/> m) it faces the point <see cref="LokiGame.FacingBehind"/> m
    ///             behind the eyes as in wave 14; at <see cref="FaceFar"/> m and more, in front of <see cref="FaceBack1"/> m, it faces the eyes;
    ///             between, the two headings are blended (a smooth step of each), so it never jumps. (w15fix) The heading is solved from the
    ///             HELD point (where the hand or the laser holds it, and how far that is from the middle): the middle faces the point exactly,
    ///             and "its middle" in the zones above is where the middle is when it faces the eyes (W15EyesMiddle).
    ///             Taking a panel no longer keeps its own turn (wave 14's YawOffset): a panel opens facing the eyes, so taking it turns it at most a little, eased by point 8.
    ///   Point 28  (design note: "it needs to sit a little bit further away from my hand ... when I let go of it in front of me, it is far too close to
    ///             my face"): the bag pulled out of the backpack is held <see cref="BagReach"/> m beyond the hand on the line from the eyes
    ///             (point 5's push, so the stick changes it as for any panel); and ANY panel let go with its middle nearer than
    ///             <see cref="MinFromEyes"/> m to the eyes eases out to that distance along the same line in <see cref="EaseTime"/> s (its
    ///             owner keeps the place where it ends: a board's distance for its next opening).
    ///   Client wave 19 (w19world), point 61 (design note: "panel smoothing should still affect unlocked panels"): it did, but the rotation took
    ///             the POSITION's factor (how fast the hand moves), so a quick twist of a still hand lagged and a shaky wrist on a moving hand was
    ///             not steadied. The rotation now has its own filter of the same kind: its target's angular speed (smoothed over
    ///             <see cref="SmoothSpeedTau"/> s) against <see cref="SmoothFastDeg"/> °/s takes its time constant from <see cref="SmoothRest"/> s to
    ///             <see cref="SmoothMove"/> s, both × the setting (50 %: a ±2° 10 Hz tremor about 5 times smaller; a 90° twist in 0.3 s a few
    ///             degrees behind).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Point 8's filter (seconds, m/s, metres): see the class summary.</summary>
        public const float SmoothRest = 0.20f, SmoothMove = 0.02f, SmoothFast = 0.6f, SmoothSpeedTau = 0.03f, SmoothArm = 0.6f;
        /// <summary>Point 61: the angular speed (°/s) from which the rotation is followed with <see cref="SmoothMove"/> (Loki's own number:
        /// a deliberate twist is 200-400 °/s, a tremor's smoothed speed well under it).</summary>
        public const float SmoothFastDeg = 360f;
        /// <summary>Point 11: where an upright held panel faces the eyes (metres: its middle from the eyes' vertical axis; ahead of the eyes'
        /// plane in the body's heading).</summary>
        public const float FaceNear = 0.30f, FaceFar = 0.50f, FaceBack0 = -0.30f, FaceBack1 = -0.10f;
        /// <summary>Point 28: the bag held this far beyond the hand (m); a panel let go nearer than this to the eyes (m, its middle) eases out
        /// to it in this time (s).</summary>
        public const float BagReach = 0.15f, MinFromEyes = 0.45f, EaseTime = 0.25f;

        /// <summary>Stick pushes / pulls made (frames with the stick past its dead zone), panels eased out at a let go, the last ease's
        /// distances (smoke, log).</summary>
        public int W15StickMoves { get; private set; }
        public int W15EasedOut { get; private set; }
        public float W15LastEaseFrom { get; private set; }
        public float W15LastEaseTo { get; private set; }
        /// <summary>The filter's last factor for hand i (1 = no smoothing this frame) and its target grab point (smoke).</summary>
        public float W15SmoothAlpha(int hand) => _grabHold[hand] != null ? _grabHold[hand].Alpha : 1f;
        public Vector3 W15Target(int hand) => _grabHold[hand] != null ? _grabHold[hand].TargetWas : default;
        /// <summary>Point 61: hand i's held panel: the rotation it is easing to and the rotation filter's factor this frame (smoke, log).</summary>
        public Quaternion W15Want(int hand) => _grabHold[hand] != null ? _grabHold[hand].WantWas : Quaternion.identity;
        public float W15SmoothAlphaRot(int hand) => _grabHold[hand] != null ? _grabHold[hand].AlphaRot : 1f;
        public float W15Push(int hand) => _grabHold[hand] != null ? _grabHold[hand].Push : 0f;
        /// <summary>A panel easing out after its let go (point 28).</summary>
        public bool W15Easing(Transform t) { foreach (var e in _w15Ease) if (e.Panel == t) return true; return false; }

        private sealed class EaseOut { public RectTransform Panel; public UiGrabbable Grabbable; public Vector3 From, To; public float T0; }
        private readonly List<EaseOut> _w15Ease = new List<EaseOut>();

        private static float W15Step(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Point 11: how much an upright held panel whose middle is at <paramref name="pos"/> faces the eyes (1) rather than the point
        /// behind the player (0).</summary>
        public float W15FaceWeight(Vector3 pos)
        {
            var head = _boot.Rig.Head.transform;
            float yaw = W12Wand != null ? W12Wand.BodyYaw : head.eulerAngles.y;
            var l = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (pos - head.position);
            return W15Step(FaceNear, FaceFar, new Vector2(l.x, l.z).magnitude) * W15Step(FaceBack0, FaceBack1, l.z);
        }
        // (w15fix: the rule is taken from the HELD point, never from where the panel's middle is now. The middle of a panel held at its edge
        // moves when the panel turns (the bag: 33 cm off its middle), so a heading taken from the middle moved the middle, which changed the
        // heading again: 5 rounds a frame did not settle and the bag flipped up to 30° from one frame to the next near the eyes (smoke w14ui C,
        // merged build). Now the heading is solved in one step from the held point, which does not move when the panel turns.)
        /// <summary>Point 11: the heading (degrees about the vertical) at which a panel held at <paramref name="held"/>, that point
        /// <paramref name="side"/> m to the right of its middle (its own x), has its middle on the line from the point <paramref name="d"/> is
        /// measured from (<paramref name="d"/> = held - that point), its face away from it; NaN: right over it.</summary>
        private static float W15FaceFrom(Vector3 d, float side)
        {
            d.y = 0f; float len = d.magnitude;
            if (len < 1e-4f) return float.NaN;
            return (Mathf.Atan2(d.x, d.z) - Mathf.Asin(Mathf.Clamp(side / len, -1f, 1f))) * Mathf.Rad2Deg;
        }
        /// <summary>Point 11: where the middle of a panel held at <paramref name="held"/> (<paramref name="side"/> m right of its middle) is when
        /// it faces the eyes; null: it cannot (the eyes' axis nearer to the held point than <paramref name="side"/>).</summary>
        public Vector3? W15EyesMiddle(Vector3 held, float side)
        {
            var d = held - _boot.Rig.Head.transform.position; d.y = 0f;
            if (d.magnitude <= Mathf.Abs(side) + 1e-4f) return null;
            return held - Quaternion.Euler(0f, W15FaceFrom(d, side), 0f) * Vector3.right * side;
        }
        /// <summary>Point 11: how much a panel held at <paramref name="held"/> (<paramref name="side"/> m right of its middle) faces the eyes:
        /// <see cref="W15FaceWeight(Vector3)"/> of its middle as it is when it faces them (so at full facing that IS its middle); 0 where it cannot.</summary>
        public float W15FaceWeight(Vector3 held, float side)
        {
            var m = W15EyesMiddle(held, side);
            return m.HasValue ? W15FaceWeight(m.Value) : 0f;
        }
        /// <summary>Point 11: the heading (degrees about the vertical) an upright panel held at <paramref name="held"/> (<paramref name="side"/>
        /// m right of its middle) takes: its middle facing the point behind the player, or the eyes, or between (blended by
        /// <see cref="W15FaceWeight(Vector3, float)"/>); NaN: none.</summary>
        public float W15FaceYaw(Vector3 held, float side)
        {
            float anchor = W15FaceFrom(held - W14FacingAnchor, side), w = W15FaceWeight(held, side);
            float eyes = W15FaceFrom(held - _boot.Rig.Head.transform.position, side);
            if (float.IsNaN(anchor)) return eyes;
            if (float.IsNaN(eyes) || w <= 0f) return anchor;
            return Mathf.LerpAngle(anchor, eyes, w);
        }
        /// <summary>Point 11: upright (no pitch, no roll) with that heading; <paramref name="keep"/>'s heading when there is none.</summary>
        private Quaternion W15Upright(Vector3 held, float side, Quaternion keep)
        {
            float a = W15FaceYaw(held, side);
            if (float.IsNaN(a)) { var f = keep * Vector3.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; return Quaternion.LookRotation(f.normalized, Vector3.up); }
            return Quaternion.Euler(0f, a, 0f);
        }
        /// <summary>How far the held point lies right of the panel's middle (metres, its own x) (point 11).</summary>
        private static float W15Side(GrabHold hold) => hold.Panel != null ? hold.GrabLocal.x * hold.Panel.lossyScale.x : 0f;
        /// <summary>Hand i's held point (the grab point after point 8's filter) and its offset right of the panel's middle (smoke).</summary>
        public Vector3 W15HeldPoint(int hand) => _grabHold[hand] != null ? _grabHold[hand].SmoothAt : default;
        public float W15HeldSide(int hand) => _grabHold[hand] != null ? W15Side(_grabHold[hand]) : 0f;

        /// <summary>Point 5, a hand's grab: <paramref name="at0"/> (where the hand puts the grab point) moved along the line from the eyes by
        /// the hold's push, after <paramref name="push"/> m more from the stick (bounded: see the class summary).</summary>
        private Vector3 W15Pushed(GrabHold hold, Vector3 at0, float push)
        {
            var eyes = _boot.Rig.Head.transform.position; var d = at0 - eyes; float len = d.magnitude;
            if (len < 1e-4f) return at0;
            if (push != 0f) { float was = hold.Push; hold.Push = Mathf.Clamp(was + push, Mathf.Min(was, GrabMinDist - len), Mathf.Max(was, GrabMaxDist - len)); }
            if (hold.Push == 0f) return at0;
            return eyes + d / len * Mathf.Max(0.05f, len + hold.Push);
        }

        /// <summary>Point 8: puts the held panel's grab point on its smoothed target, turned as given (free) or upright (point 11).
        /// <paramref name="lever"/>: the grab point's distance from the hand (a laser's length).</summary>
        private void W15Place(GrabHold hold, Vector3 target, Quaternion? rot, float lever, float dt)
        {
            var t = hold.Panel; float s = Comfort.PanelSmoothing, a = 1f;
            if (!hold.SmoothInit || s <= 0.001f || dt <= 0f)
            {
                hold.SmoothAt = target; hold.TargetWas = target; hold.Speed = 0f; hold.SmoothInit = true;
            }
            else
            {
                float v = (target - hold.TargetWas).magnitude / dt; hold.TargetWas = target;
                hold.Speed += (v - hold.Speed) * (1f - Mathf.Exp(-dt / SmoothSpeedTau));
                float arm = Mathf.Max(1f, lever / SmoothArm);
                float tau = s * Mathf.Lerp(SmoothRest * Mathf.Min(arm, 2f), SmoothMove, Mathf.Clamp01(hold.Speed / (SmoothFast * arm)));
                a = 1f - Mathf.Exp(-dt / Mathf.Max(tau, 1e-4f));
                hold.SmoothAt = Vector3.Lerp(hold.SmoothAt, target, a);
            }
            hold.Alpha = a;
            var from = t.rotation;
            // (w15fix: the upright heading comes from the held point, so it is final at once: one placing is exact)
            var want = rot ?? W15Upright(hold.SmoothAt, W15Side(hold), from) * Quaternion.Euler(hold.KeepPitch, 0f, 0f); // (client wave 24, w24: the keyboard's kept tilt; 0 for every other panel: the same turn as before)
            // client wave 19 (w19world), point 61: the rotation's own speed term (the class summary); off / first frame: on its target
            float ar = 1f;
            if (a < 1f)
            {
                float w = Quaternion.Angle(hold.WantWas, want) / dt;
                hold.AngSpeed += (w - hold.AngSpeed) * (1f - Mathf.Exp(-dt / SmoothSpeedTau));
                float tr = s * Mathf.Lerp(SmoothRest, SmoothMove, Mathf.Clamp01(hold.AngSpeed / SmoothFastDeg));
                ar = 1f - Mathf.Exp(-dt / Mathf.Max(tr, 1e-4f));
            }
            else hold.AngSpeed = 0f;
            hold.WantWas = want; hold.AlphaRot = ar;
            t.rotation = ar >= 1f ? want : Quaternion.Slerp(from, want, ar);
            t.position += hold.SmoothAt - t.TransformPoint(hold.GrabLocal);
        }

        /// <summary>Point 28: a panel let go too near the eyes eases out (true: its owner is told its place when the ease ends).</summary>
        private bool W15EaseOutAtLetGo(GrabHold hold)
        {
            var t = hold.Panel; if (t == null) return false;
            if (t == _w24Login || t == _w24Kb) return false; // allow the player to manually pull the login panel or keyboard closer without pushing it back
            var eyes = _boot.Rig.Head.transform.position; var d = t.position - eyes; float len = d.magnitude;
            float minFrom = WCodexHandBook(t) ? CodexBook.HandMinFromEyes : MinFromEyes; // (wave 28, wcodex: a hand-held book is read nearer)
            if (len >= minFrom) return false;
            Vector3 dir;
            if (len > 0.02f) dir = d / len;
            else { float yaw = W12Wand != null ? W12Wand.BodyYaw : _boot.Rig.Head.transform.eulerAngles.y; dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; }
            _w15Ease.RemoveAll(e => e.Panel == t);
            _w15Ease.Add(new EaseOut { Panel = t, Grabbable = hold.Grabbable, From = t.position, To = eyes + dir * minFrom, T0 = Time.unscaledTime });
            W15EasedOut++; W15LastEaseFrom = len; W15LastEaseTo = minFrom;
            Debug.Log($"[Grab] '{t.name}' let go {len * 100f:0} cm from the eyes: it eases out to {minFrom * 100f:0} cm in {EaseTime:0.00} s");
            return true;
        }

        /// <summary>Once a frame (UpdateW13Grab): the panels easing out; a panel taken again or closed stops easing.</summary>
        private void UpdateW15Ease()
        {
            for (int k = _w15Ease.Count - 1; k >= 0; k--)
            {
                var e = _w15Ease[k]; var t = e.Panel;
                if (t == null || !PanelGrab.IsShown(t) || W13GrabSkip(t)) { _w15Ease.RemoveAt(k); continue; }
                float f = Mathf.Clamp01((Time.unscaledTime - e.T0) / EaseTime);
                t.position = Vector3.Lerp(e.From, e.To, W15Step(0f, 1f, f));
                if (f < 1f) continue;
                _w15Ease.RemoveAt(k);
                if (e.Grabbable != null) { try { e.Grabbable.Released?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); } } // (a board keeps its place)
                W13SaveMenuPlace(t);
            }
        }
    }
}
