using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 22 (w22clips), points 53 and 59 of the design's notes after wave 17 (playtest notes): one tutorial
    /// step's CLIP as data. design note: "Above every page of the tutorial, add a small, looping clip showing/explaining the action to be performed.
    /// This clip should have 1.5s of inaction at the start and at the end of it ... Inaction does not mean PAUSE THE VIDEO"; "also add a
    /// ghostly 3D projection of the player's current controller ... and have it simulate what you're meant to do".
    ///   Why data   a clip saved as video frames of the game would carry the game's own art (maps, sprites): it could be neither committed
    ///              nor shipped (the release build checks for it). So a clip is the SCRIPT OF THE ACTION: what the head and the hands did,
    ///              which buttons, and what came of it, recorded once from a scripted run of the real game (LokiGame.W22ClipsRec.cs, a
    ///              development-build tool) and stored as a small text file of Loki's own (Resources/Clips/&lt;step&gt;.txt). The client plays
    ///              it LIVE in a small second view (UI/TutorialClip.cs) over the player's own world, and the ghost controller
    ///              (UI/ControllerGhost.cs) reads the same buttons from it, so the two are always in step.
    ///   What       keys over time (seconds from the clip's start), each track holding its last value between keys or moving linearly:
    ///              the head and the two hands (the rig's space, metres); each hand's inputs (trigger, grip, stick, buttons, the index
    ///              finger on the trigger); the map's place in the rig's space (so a turn, a move, a resize and the three views replay
    ///              by moving the clip's eye, never the player's world); the character and the practice Poring (the map's cells, kept
    ///              RELATIVE to where the character stood, so no clip depends on a map); the lasers, the wand, the cell cursor, the look's ring;
    ///              every panel that was open as plain shapes (rectangles, discs, outlines, bars where text stood: no words, no pictures); the
    ///              filling rings and dots; moments (a hit, a check mark).
    ///   Stills     the recorder leaves <see cref="StillSeconds"/> s with no hand moving and no button changing at both ends; the world goes
    ///              on living through them because the clip is played over the live world.
    ///   Mirror     <see cref="Mirrored"/>: the same clip for a left-handed player (everything reflected left to right, the hands swapped).
    /// No Unity object in here: only numbers (the smoke reads the tracks to prove the stills and that the ghost moves when they say so).
    /// </summary>
    public sealed class ClipTimeline
    {
        public const int FormatVersion = 1;
        public const float StillSeconds = 1.5f;
        /// <summary>Input flags (the fifth value of an input key).</summary>
        public const int StickClick = 1, Primary = 2, Secondary = 4, Menu = 8, OnTrigger = 16;

        /// <summary>One track: keys of <see cref="Width"/> numbers. Values from <see cref="StepFrom"/> on hold their key's value; the ones
        /// before it move linearly; a quaternion at <see cref="Quat"/> (-1: none) turns the short way.</summary>
        public sealed class Track
        {
            public readonly int Width, StepFrom, Quat;
            private readonly List<float> _t = new List<float>(), _v = new List<float>();
            public Track(int width, int stepFrom, int quat = -1) { Width = width; StepFrom = stepFrom; Quat = quat; }
            public int Count => _t.Count;
            public float TimeAt(int i) => _t[i];
            public float ValueAt(int i, int channel) => _v[i * Width + channel];
            public void SetValue(int i, int channel, float v) { _v[i * Width + channel] = v; }
            public void Add(float t, IList<float> values, int offset = 0)
            {
                _t.Add(t); for (int k = 0; k < Width; k++) _v.Add(offset + k < values.Count ? values[offset + k] : 0f);
            }
            /// <summary>The track's values at <paramref name="t"/> into <paramref name="o"/> (at least <see cref="Width"/> long). False: no key.</summary>
            public bool Sample(float t, float[] o)
            {
                int n = _t.Count; if (n == 0) return false;
                int hi = n - 1;
                if (t <= _t[0]) { Copy(0, o); return true; }
                if (t >= _t[hi]) { Copy(hi, o); return true; }
                int lo = 0;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_t[mid] <= t) lo = mid; else hi = mid; }
                float span = _t[hi] - _t[lo], k = span > 1e-6f ? (t - _t[lo]) / span : 0f;
                int a = lo * Width, b = hi * Width;
                for (int c = 0; c < Width; c++) o[c] = c >= StepFrom ? _v[a + c] : _v[a + c] + (_v[b + c] - _v[a + c]) * k;
                if (Quat >= 0 && Quat + 3 < StepFrom)
                {
                    var qa = new Quaternion(_v[a + Quat], _v[a + Quat + 1], _v[a + Quat + 2], _v[a + Quat + 3]);
                    var qb = new Quaternion(_v[b + Quat], _v[b + Quat + 1], _v[b + Quat + 2], _v[b + Quat + 3]);
                    var q = Quaternion.Slerp(Norm(qa), Norm(qb), k);
                    o[Quat] = q.x; o[Quat + 1] = q.y; o[Quat + 2] = q.z; o[Quat + 3] = q.w;
                }
                return true;
            }
            private void Copy(int i, float[] o) { int a = i * Width; for (int c = 0; c < Width; c++) o[c] = _v[a + c]; }
            public Track Clone() { var t = new Track(Width, StepFrom, Quat); t._t.AddRange(_t); t._v.AddRange(_v); return t; }
        }

        private static Quaternion Norm(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// <summary>What a panel hangs on: the rig (the room), the head, a hand.</summary>
        public enum Anchor { Rig = 0, Head = 1, LeftHand = 2, RightHand = 3 }
        /// <summary>A panel's parts as plain shapes: a rectangle, a disc, a ring, bars where a text stood, a rectangle where a picture stood.</summary>
        public enum GraphicKind { Off = -1, Rect = 0, Disc = 1, Ring = 2, Text = 3, Picture = 4, Outline = 5, Sector = 6 }
        public struct GraphicKey
        {
            public float T; public int Id, Order; public GraphicKind Kind;
            /// <summary>The middle, the size and the turn (degrees) in the panel's own pixels.</summary>
            public float X, Y, W, H, Rot;
            public Color32 Colour;
            /// <summary>A text: its lines; and 10 x its alignment (0 left, 1 middle, 2 right) plus the last line's share of the width. A
            /// picture: 1 = round. An outline (a frame, an edge's highlight, stitching): its line's width in pixels. A sector (a piece of the
            /// wand's wheel): from and to in degrees clockwise from straight up, W the outer and H the inner diameter.</summary>
            public float P, Q;
        }
        public sealed class Panel
        {
            public int Id, Sort; public Anchor On; public string Name = "";
            /// <summary>px py pz, qx qy qz qw, metres per pixel, opacity, shown (0 / 1), in its anchor's space.</summary>
            public readonly Track Pose = new Track(10, 9, 3);
            public readonly List<GraphicKey> Graphics = new List<GraphicKey>();
        }
        /// <summary>A shape that is no panel (a calibration dot, a filling ring, the look's dot, an edge highlight).</summary>
        public enum ShapeKind { Rect = 0, Disc = 1, FillRing = 2, Ring = 3, CodexRing = 4 }
        public sealed class Shape
        {
            public int Id; public ShapeKind Kind;
            /// <summary>px py pz, qx qy qz qw, width, height (metres), r g b a, fill (a ring's, 0-1), shown (0 / 1), in the rig's space. Wave 28
            /// (wcodex): the Codex's scan ring (UI/CodexInkRing.cs) is a shape of its own kind: its fill is the scan's REAL progress (the
            /// stage's ring eases it as the game's does), r 1 while its written flourish plays, g 1 when that flourish is gold (in full).</summary>
            public readonly Track Pose = new Track(15, 14, 3);
        }
        public struct Moment { public float T; public string Type; public float X, Y, Z; }

        public string Key = "", RecordedWith = "";
        public float Length;
        /// <summary>The hands the step uses (the ghost shows these); neither: the head alone acts (a look step).</summary>
        public bool UsesLeft, UsesRight;
        /// <summary>The tutorial's own guide panel is part of the clip (the welcome and the last step: its button is what is pressed).</summary>
        public bool ShowGuide;
        public readonly Track Head = new Track(7, 7, 3), HandL = new Track(7, 7, 3), HandR = new Track(7, 7, 3);
        /// <summary>trigger, grip, stick x, stick y (0-1 / -1..1), flags.</summary>
        public readonly Track InL = new Track(5, 4), InR = new Track(5, 4);
        /// <summary>The map in the rig's space: px py pz, qx qy qz qw, metres per cell.</summary>
        public readonly Track Map = new Track(8, 8, 3);
        /// <summary>x y z in the map's cells (Unity's axes: y up), the facing (0-7), the action group, shown, lit by the laser or the look.</summary>
        public readonly Track Self = new Track(7, 3), Poring = new Track(7, 3);
        /// <summary>Wave 28 (wcodex): a second practice creature (the tutorial's Codex step: the practice Lunatic), as the Poring's track;
        /// its monster id is <see cref="OtherJob"/> (0: none).</summary>
        public readonly Track Other = new Track(7, 3);
        public int OtherJob;
        /// <summary>opacity, from (3) and to (3) in the rig's space, r g b.</summary>
        public readonly Track Laser = new Track(10, 10), OffLaser = new Track(10, 10);
        /// <summary>The wand (in its holster at the hip or in a hand): shown, its grip end (3) and its tip (3) in the rig's space, r g b.</summary>
        public readonly Track Wand = new Track(10, 10);
        /// <summary>shown, x y z in the map's cells.</summary>
        public readonly Track Cursor = new Track(4, 0);
        /// <summary>shown, x y z in the map's cells, radius (cells), r g b.</summary>
        public readonly Track LookRing = new Track(8, 0);
        public readonly List<Panel> Panels = new List<Panel>();
        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly List<Moment> Moments = new List<Moment>();
        /// <summary>The clip was mirrored for a left-handed player.</summary>
        public bool IsMirrored { get; private set; }

        public struct Input
        {
            public float Trigger, Grip; public Vector2 Stick; public int Flags;
            public bool Has(int flag) => (Flags & flag) != 0;
        }
        private static readonly float[] Tmp = new float[16];
        public Input InputAt(bool left, float t)
        {
            var tr = left ? InL : InR;
            if (!tr.Sample(t, Tmp)) return default;
            return new Input { Trigger = Tmp[0], Grip = Tmp[1], Stick = new Vector2(Tmp[2], Tmp[3]), Flags = Mathf.RoundToInt(Tmp[4]) };
        }
        public bool PoseAt(Track tr, float t, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (tr == null || !tr.Sample(t, Tmp)) return false;
            pos = new Vector3(Tmp[0], Tmp[1], Tmp[2]); rot = Norm(new Quaternion(Tmp[3], Tmp[4], Tmp[5], Tmp[6]));
            return true;
        }

        // ------------------------------------------------------------------ what the stills are (the smoke proves them from here)
        /// <summary>The first and the last moment at which a hand or the head moves more than <paramref name="posEps"/> m / <paramref name="degEps"/>
        /// degrees from where it was at the clip's start (its end), or an input differs from the start's (the end's): the action's span.
        /// (-1, -1): nothing ever moves.</summary>
        public (float first, float last) ActionSpan(float posEps = 0.004f, float degEps = 1.0f, float inputEps = 0.03f)
        {
            float first = float.MaxValue, last = -1f;
            foreach (var tr in new[] { Head, HandL, HandR })
            {
                if (tr.Count == 0) continue; int n = tr.Count - 1;
                for (int i = 0; i <= n; i++) if (PoseDiffers(tr, i, 0, posEps, degEps)) { first = Mathf.Min(first, i > 0 ? tr.TimeAt(i - 1) : tr.TimeAt(i)); break; }
                for (int i = n; i >= 0; i--) if (PoseDiffers(tr, i, n, posEps, degEps)) { last = Mathf.Max(last, i < n ? tr.TimeAt(i + 1) : tr.TimeAt(i)); break; }
            }
            foreach (var tr in new[] { InL, InR })
            {
                if (tr.Count == 0) continue; int n = tr.Count - 1;
                for (int i = 0; i <= n; i++) if (InputDiffers(tr, i, 0, inputEps)) { first = Mathf.Min(first, i > 0 ? tr.TimeAt(i - 1) : tr.TimeAt(i)); break; }
                for (int i = n; i >= 0; i--) if (InputDiffers(tr, i, n, inputEps)) { last = Mathf.Max(last, i < n ? tr.TimeAt(i + 1) : tr.TimeAt(i)); break; }
            }
            return first == float.MaxValue ? (-1f, -1f) : (first, last);
        }
        private static bool PoseDiffers(Track tr, int i, int j, float posEps, float degEps)
        {
            var a = new Vector3(tr.ValueAt(i, 0), tr.ValueAt(i, 1), tr.ValueAt(i, 2)); var b = new Vector3(tr.ValueAt(j, 0), tr.ValueAt(j, 1), tr.ValueAt(j, 2));
            if ((a - b).magnitude > posEps) return true;
            var qa = Norm(new Quaternion(tr.ValueAt(i, 3), tr.ValueAt(i, 4), tr.ValueAt(i, 5), tr.ValueAt(i, 6))); var qb = Norm(new Quaternion(tr.ValueAt(j, 3), tr.ValueAt(j, 4), tr.ValueAt(j, 5), tr.ValueAt(j, 6)));
            return Quaternion.Angle(qa, qb) > degEps;
        }
        private static bool InputDiffers(Track tr, int i, int j, float eps)
        {
            for (int c = 0; c < 4; c++) if (Mathf.Abs(tr.ValueAt(i, c) - tr.ValueAt(j, c)) > eps) return true;
            return Mathf.RoundToInt(tr.ValueAt(i, 4)) != Mathf.RoundToInt(tr.ValueAt(j, 4));
        }

        /// <summary>The times at which <paramref name="left"/>'s inputs change (a button, the finger, or a trigger / grip / stick crossing
        /// half its travel): what the ghost's parts must follow.</summary>
        public List<float> InputChanges(bool left)
        {
            var tr = left ? InL : InR; var r = new List<float>();
            for (int i = 1; i < tr.Count; i++)
            {
                bool ch = Mathf.RoundToInt(tr.ValueAt(i, 4)) != Mathf.RoundToInt(tr.ValueAt(i - 1, 4));
                for (int c = 0; c < 4 && !ch; c++) ch = (Mathf.Abs(tr.ValueAt(i, c)) > 0.5f) != (Mathf.Abs(tr.ValueAt(i - 1, c)) > 0.5f);
                if (ch) r.Add(tr.TimeAt(i));
            }
            return r;
        }

        // ------------------------------------------------------------------ the mirror (a left-handed player)
        /// <summary>The same clip reflected left to right: every place and turn in the rig's space mirrored (x → −x), the two hands
        /// swapped, the sticks' sideways push reversed, the map's cells mirrored about the character's first cell (so the map itself is
        /// only seen from the mirrored side, never drawn mirrored), each facing mirrored. A panel's own picture is not flipped.</summary>
        public ClipTimeline Mirrored()
        {
            var m = new ClipTimeline { Key = Key, RecordedWith = RecordedWith, Length = Length, UsesLeft = UsesRight, UsesRight = UsesLeft, ShowGuide = ShowGuide, IsMirrored = !IsMirrored };
            CopyInto(m.Head, MirrorPose(Head)); CopyInto(m.HandL, MirrorPose(HandR)); CopyInto(m.HandR, MirrorPose(HandL));
            CopyInto(m.InL, MirrorInput(InR)); CopyInto(m.InR, MirrorInput(InL));
            CopyInto(m.Map, MirrorPose(Map));
            CopyInto(m.Self, MirrorUnit(Self)); CopyInto(m.Poring, MirrorUnit(Poring)); CopyInto(m.Other, MirrorUnit(Other)); m.OtherJob = OtherJob;
            CopyInto(m.Laser, MirrorLaser(Laser)); CopyInto(m.OffLaser, MirrorLaser(OffLaser)); CopyInto(m.Wand, MirrorLaser(Wand));
            CopyInto(m.Cursor, NegateX(Cursor, 1)); CopyInto(m.LookRing, NegateX(LookRing, 1));
            foreach (var p in Panels)
            {
                var q = new Panel { Id = p.Id, Sort = p.Sort, Name = p.Name, On = p.On == Anchor.LeftHand ? Anchor.RightHand : p.On == Anchor.RightHand ? Anchor.LeftHand : p.On };
                CopyInto(q.Pose, MirrorPose(p.Pose)); q.Graphics.AddRange(p.Graphics); m.Panels.Add(q);
            }
            foreach (var s in Shapes) { var q = new Shape { Id = s.Id, Kind = s.Kind }; CopyInto(q.Pose, MirrorPose(s.Pose)); m.Shapes.Add(q); }
            foreach (var e in Moments) m.Moments.Add(new Moment { T = e.T, Type = e.Type, X = -e.X, Y = e.Y, Z = e.Z }); // (a hit: the map's cells; a check mark: the rig's space; both mirror in x)
            return m;
        }
        private static void CopyInto(Track to, Track from) { var o = new float[from.Width]; for (int i = 0; i < from.Count; i++) { for (int c = 0; c < from.Width; c++) o[c] = from.ValueAt(i, c); to.Add(from.TimeAt(i), o); } }
        /// <summary>px → −px; the turn (x, y, z, w) → (x, −y, −z, w): the reflection of a rigid place in the plane x = 0.</summary>
        private static Track MirrorPose(Track t)
        {
            var r = t.Clone();
            for (int i = 0; i < r.Count; i++) { r.SetValue(i, 0, -r.ValueAt(i, 0)); r.SetValue(i, 4, -r.ValueAt(i, 4)); r.SetValue(i, 5, -r.ValueAt(i, 5)); }
            return r;
        }
        private static Track MirrorInput(Track t) { var r = t.Clone(); for (int i = 0; i < r.Count; i++) r.SetValue(i, 2, -r.ValueAt(i, 2)); return r; }
        private static Track NegateX(Track t, int channel) { var r = t.Clone(); for (int i = 0; i < r.Count; i++) r.SetValue(i, channel, -r.ValueAt(i, channel)); return r; }
        private static Track MirrorUnit(Track t)
        {
            var r = t.Clone();
            for (int i = 0; i < r.Count; i++) { r.SetValue(i, 0, -r.ValueAt(i, 0)); r.SetValue(i, 3, (8 - (Mathf.RoundToInt(r.ValueAt(i, 3)) & 7)) & 7); }
            return r;
        }
        private static Track MirrorLaser(Track t) { var r = t.Clone(); for (int i = 0; i < r.Count; i++) { r.SetValue(i, 1, -r.ValueAt(i, 1)); r.SetValue(i, 4, -r.ValueAt(i, 4)); } return r; }

        // ------------------------------------------------------------------ the text form
        // loki-clip 1 / key <step> / len <seconds> / hands <L|R|LR|-> / guide <0|1> / with <controller layout>
        // panel <id> <anchor> <sort> <name> ; shape <id> <kind>
        // <ms> H|L|R  px py pz qx qy qz qw          <ms> IL|IR trigger grip sx sy flags          <ms> M px py pz qx qy qz qw scale
        // <ms> S|P|N x y z dir act shown lit (N: the second practice creature, "other <job>")          <ms> Z|Y|W a fx fy fz tx ty tz r g b         <ms> C shown x y z     <ms> K shown x y z radius r g b
        // <ms> UP <id> px py pz qx qy qz qw scale a shown          <ms> UG <panel> <id> <order> <kind> x y w h rot rrggbbaa p q   (kind -1: gone)
        // <ms> Q <id> px py pz qx qy qz qw w h r g b a fill shown   <ms> E <type> x y z
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ClipTimeline Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var tl = new ClipTimeline(); var vals = new List<float>(16);
            var panels = new Dictionary<int, Panel>(); var shapes = new Dictionary<int, Shape>();
            var lines = text.Split('\n'); bool head = false;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li].Trim(); if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');
                if (!head) { if (p[0] != "loki-clip" || p.Length < 2 || p[1] != FormatVersion.ToString(Inv)) return null; head = true; continue; }
                switch (p[0])
                {
                    case "key": tl.Key = p.Length > 1 ? p[1] : ""; continue;
                    case "len": tl.Length = F(p, 1); continue;
                    case "hands": tl.UsesLeft = p.Length > 1 && p[1].Contains("L"); tl.UsesRight = p.Length > 1 && p[1].Contains("R"); continue;
                    case "guide": tl.ShowGuide = p.Length > 1 && p[1] == "1"; continue;
                    case "with": tl.RecordedWith = p.Length > 1 ? p[1] : ""; continue;
                    case "other": tl.OtherJob = I(p, 1); continue; // (wave 28, wcodex)
                    case "panel":
                        { var made = new Panel { Id = I(p, 1), On = (Anchor)I(p, 2), Sort = I(p, 3), Name = p.Length > 4 ? p[4] : "" }; panels[made.Id] = made; tl.Panels.Add(made); continue; }
                    case "shape":
                        { var sh = new Shape { Id = I(p, 1), Kind = (ShapeKind)I(p, 2) }; shapes[sh.Id] = sh; tl.Shapes.Add(sh); continue; }
                }
                if (p.Length < 2) continue;
                float t = F(p, 0) / 1000f; string code = p[1];
                if (code == "UG")
                {
                    if (!panels.TryGetValue(I(p, 2), out var owner)) continue;
                    var g = new GraphicKey { T = t, Id = I(p, 3) };
                    if (p.Length <= 5) g.Kind = GraphicKind.Off;
                    else
                    {
                        g.Order = I(p, 4); g.Kind = (GraphicKind)I(p, 5); g.X = F(p, 6); g.Y = F(p, 7); g.W = F(p, 8); g.H = F(p, 9); g.Rot = F(p, 10);
                        uint c = p.Length > 11 ? Convert.ToUInt32(p[11], 16) : 0xffffffffu;
                        g.Colour = new Color32((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
                        g.P = F(p, 12); g.Q = F(p, 13);
                    }
                    owner.Graphics.Add(g); continue;
                }
                if (code == "E") { tl.Moments.Add(new Moment { T = t, Type = p.Length > 2 ? p[2] : "", X = F(p, 3), Y = F(p, 4), Z = F(p, 5) }); continue; }
                int from = 2; Track tr = null;
                switch (code)
                {
                    case "H": tr = tl.Head; break; case "L": tr = tl.HandL; break; case "R": tr = tl.HandR; break;
                    case "IL": tr = tl.InL; break; case "IR": tr = tl.InR; break; case "M": tr = tl.Map; break;
                    case "S": tr = tl.Self; break; case "P": tr = tl.Poring; break; case "N": tr = tl.Other; break; case "Z": tr = tl.Laser; break; case "Y": tr = tl.OffLaser; break; case "W": tr = tl.Wand; break;
                    case "C": tr = tl.Cursor; break; case "K": tr = tl.LookRing; break;
                    case "UP": if (panels.TryGetValue(I(p, 2), out var pp)) tr = pp.Pose; from = 3; break;
                    case "Q": if (shapes.TryGetValue(I(p, 2), out var ss)) tr = ss.Pose; from = 3; break;
                }
                if (tr == null) continue;
                vals.Clear(); for (int k = from; k < p.Length; k++) vals.Add(F(p, k));
                tr.Add(t, vals);
            }
            if (!head) return null;
            foreach (var pn in tl.Panels) pn.Graphics.Sort((a, b) => a.T.CompareTo(b.T));
            tl.Moments.Sort((a, b) => a.T.CompareTo(b.T));
            return tl;
        }
        private static float F(string[] p, int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, Inv, out float v) ? v : 0f;
        private static int I(string[] p, int i) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, Inv, out int v) ? v : 0;
    }
}
