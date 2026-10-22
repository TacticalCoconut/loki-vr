using System;
using System.Collections.Generic;
using Loki.Formats;
using Loki.Game;
using Loki.Sprites;
using Loki.VR;
using Loki.World;
using UnityEngine;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 22 (w22clips), point 53 of the design's notes after wave 17 (design note: "Above every page of the tutorial, add a small, looping clip
    /// showing/explaining the action to be performed. This clip should have 1.5s of inaction at the start and at the end of it ... Inaction
    /// does not mean PAUSE THE VIDEO. It means nothing is being moved. So if a poring is in view, it continues playing its idle animation
    /// because it is a video recording"): the clip's STAGE. It plays a <see cref="ClipTimeline"/> (the recorded script of a step's action)
    /// LIVE into a small picture (<see cref="Texture"/>, <see cref="WidthPx"/> x <see cref="HeightPx"/>) through a second camera.
    ///   Why live  frames captured from the game would be the game's own art: they could not be shipped. Here nothing is stored but
    ///             numbers; what is seen is drawn now, from the player's own files.
    ///   The eye   the clip's camera stands where the recorded head stood RELATIVE TO THE MAP: the stage's "rig" is a child of the map
    ///             whose place undoes the recorded map's place in the recorded rig (<see cref="PlaceRig"/>), shifted so that the recorded
    ///             character's first cell lies on the player's own character (taken again at every loop). So the clip shows the player's
    ///             own map around their own character from the recorded eye, a recorded turn, move, resize or change of view moves only
    ///             the clip's eye, and nothing of the player's world is touched. It works on any map (the tutorial's map may change).
    ///   Actors    drawn only for the clip's camera (layer <see cref="StageLayer"/>, which the head camera, the screenshot camera and the
    ///             desktop spectator leave out): the two hands as the player's own controller model (UI/ControllerGhost.cs) at the
    ///             recorded places with the recorded buttons; the lasers; the wand; a copy of the player's own character and a practice Poring
    ///             (units of the real kind, made from the player's files, walking and idling by themselves: the Poring's idle animation
    ///             never stops); the cell cursor, the look's ring, a hit's flash; every recorded panel as plain shapes in the panel
    ///             theme's own colours (frames as outlines, boxes, discs, a wheel's pieces, bars where a text stood); the filling
    ///             rings (the real ones: UI/LookFillRing.cs), the dots, the check marks (the clip's own picture of UI/CheckMarkFx.cs's:
    ///             silent, never counted among the game's).
    ///   Hidden    from the clip's camera only: the player's real panels (their layer), real hands, lasers and whatever else hangs on the
    ///             real rig, the real character and the real practice Poring (put on <see cref="HiddenLayer"/> for the one picture and
    ///             back on their own layers right after it: no other camera ever sees a change). Sprites are turned to face the clip's
    ///             camera for its picture and back.
    ///   Cost      ONE extra camera at <see cref="WidthPx"/> x <see cref="HeightPx"/>, drawn <see cref="Fps"/> times a second (not every
    ///             frame), only while a clip is shown (<see cref="Play"/> … <see cref="Stop"/>): <see cref="MeanRenderMs"/> says what a
    ///             picture costs on the main thread. The casting ways' columns (UI/CastingWays.cs) each have a stage of their own, smaller
    ///             and slower (<see cref="PictureW"/>, <see cref="PicturesPerSecond"/>), staggered, drawn only while in view.
    /// </summary>
    [DefaultExecutionOrder(600)] // after the views (VR/ViewController.cs, 500) placed the map for this frame, and after the sprites faced the head
    public sealed class TutorialClip : MonoBehaviour
    {
        public const int StageLayer = 9, HiddenLayer = 10;
        public const int WidthPx = 448, HeightPx = 252;
        public const float Fps = 30f, FieldOfView = 52f;
        public const int UnitIdSelf = 0x7FFFFE01, UnitIdPoring = 0x7FFFFE02, UnitIdOther = 0x7FFFFE03;

        public RenderTexture Texture { get; private set; }
        public Camera Camera => _cam;
        /// <summary>Client wave 22 (the casting ways' columns: several clips at once): this clip's picture in pixels, how many pictures a
        /// second it draws, whether it draws at all now (a column out of view), and the share of a picture's period its first one waits
        /// (so several clips do not all draw in the same frame).</summary>
        public int PictureW { get; private set; } = WidthPx;
        public int PictureH { get; private set; } = HeightPx;
        public float PicturesPerSecond { get; set; } = Fps;
        public bool Drawing { get; set; } = true;
        public float Stagger { get; set; }
        /// <summary>Client wave 22 (the casting ways): what the picture is FRAMED ON. Null: the recorded eyes' own view (a step's clip: the
        /// world matters). The beginnings of the names of the script's panels that are the way's tool (the pad, the wheel, the bar …): the
        /// picture's eye stays where the recorded eyes were but looks at the tool while it is shown, and at the hands the script uses, as
        /// close as holds them (<see cref="FocusMinHalf"/>° to <see cref="FocusMaxHalf"/>° up and down from its middle). It follows them
        /// smoothly (<see cref="FocusSmooth"/> s), and does not move in the clip's two stills. Set before <see cref="Play"/>.</summary>
        public string[] Focus { get; set; }
        public const float FocusStep = 0.1f, FocusSmooth = 0.5f, FocusMargin = 1.3f, FocusMinHalf = 9f, FocusMaxHalf = FieldOfView * 0.5f, FocusHandM = 0.10f, FocusHandMaxHalf = 25f;
        /// <summary>The picture's field of view now, how far its eye is turned from the recorded look (degrees), and whether the point
        /// <paramref name="rigPoint"/> of the stage's rig is inside the picture (smoke).</summary>
        public float ViewFov => _cam != null ? _cam.fieldOfView : FieldOfView;
        public float ViewOffDegrees { get; private set; }
        public bool Framed => _focusDir != null;
        private Vector3[] _focusDir; private float[] _focusHalf; private bool _focused;
        /// <summary>The script's panels whose names begin with one of <see cref="Focus"/> that are drawn now, and how many of their
        /// middles are inside the picture (smoke).</summary>
        public (int shown, int inPicture) FocusPanelsNow()
        {
            int shown = 0, inside = 0; if (Focus == null || _cam == null) return (0, 0);
            foreach (var p in _panels)
            {
                if (!p.T.gameObject.activeSelf || !IsFocus(p.Data.Name)) continue; shown++;
                var v = _cam.WorldToViewportPoint(p.T.position); if (v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f) inside++;
            }
            return (shown, inside);
        }
        private bool IsFocus(string name) { if (Focus != null) foreach (var f in Focus) if (name.StartsWith(f, StringComparison.Ordinal)) return true; return false; }
        /// <summary>Clips playing now (all stages): while any plays the head camera leaves the stage's layer out.</summary>
        public static int PlayingCount { get; private set; }
        public ClipTimeline Clip { get; private set; }
        public Profile Profile { get; private set; }
        public bool Playing => Clip != null;
        /// <summary>The clip's own time now (0 … its length), loops finished, pictures drawn.</summary>
        public float ClipTime { get; private set; }
        public int Loops { get; private set; }
        public int Renders { get; private set; }
        /// <summary>The main thread's time for one picture: the last, the mean of this clip, the worst of this clip (ms).</summary>
        public double LastRenderMs { get; private set; }
        public double MeanRenderMs => _renderCount > 0 ? _renderSum / _renderCount : 0.0;
        public double MaxRenderMs { get; private set; }
        private double _renderSum; private int _renderCount;
        public ControllerGhost HandLeft => _ghostL;
        public ControllerGhost HandRight => _ghostR;
        public Unit ActorSelf => _self;
        public Unit ActorPoring => _poring;
        /// <summary>Panels and shapes drawn now, check marks shown so far (smoke, log).</summary>
        public int PanelsShown { get; private set; }
        public int ShapesShown { get; private set; }
        public int Checks { get; private set; }
        public bool Paused { get; set; }

        // the world (given by LokiGame.W22Clips.cs)
        public Func<Transform> Map; public Func<GatFile> Gat; public Func<RoAssets> Assets;
        public Func<Unit> Self, RealPoring; public Func<IEnumerable<Unit>> Units;
        /// <summary>Wave 28 (wcodex): the real second practice creature (the Codex step's practice Lunatic), out of the clip's pictures as the
        /// real Poring is.</summary>
        public Func<Unit> RealOther;
        /// <summary>Wave 28 (wcodex): the clip's second practice creature (smoke).</summary>
        public Unit ActorOther => _other;

        private DioramaRig _real; private Transform _rig, _headT, _handLT, _handRT; private Camera _cam;
        private ControllerGhost _ghostL, _ghostR; private Unit _self, _poring, _other; private int _selfAct = -1, _poringAct = -1, _otherAct = -1;
        private Vector3 _anchor; private float _renderAt; private Transform _cursorReal;
        private readonly float[] _v = new float[16];
        private static Material _flat; private static Texture2D _atlas;
        private Mesh _lineMesh; private MeshRenderer _lineR; private Transform _cursor, _lookRing, _pulse; private MaterialPropertyBlock _mpb;
        private Transform _check; private int _checkShown = -1;

        private sealed class PanelView
        {
            public ClipTimeline.Panel Data; public Transform T; public Mesh Mesh; public MeshRenderer R; public int Cursor; public bool Dirty; public float Alpha = -1f;
            public readonly Dictionary<int, ClipTimeline.GraphicKey> State = new Dictionary<int, ClipTimeline.GraphicKey>();
        }
        private sealed class ShapeView { public ClipTimeline.Shape Data; public Transform T; public MeshRenderer R; public LookFillRing Ring; public CodexInkRing Codex; public bool CodexDone; public Color Colour = Color.clear; }
        private readonly List<PanelView> _panels = new List<PanelView>(); private readonly List<ShapeView> _shapes = new List<ShapeView>();

        public static TutorialClip Create(DioramaRig rig, int width = WidthPx, int height = HeightPx)
        {
            var go = new GameObject("TutorialClip"); go.transform.SetParent(rig.transform, false);
            var c = go.AddComponent<TutorialClip>(); c._real = rig; c._mpb = new MaterialPropertyBlock(); c.PictureW = width; c.PictureH = height;
            return c;
        }

        /// <summary>The shared flat material (Loki/UIOverlay: drawn over the world, as the panels and the laser are) with a small picture
        /// made in code: a solid square, a disc and a ring side by side.</summary>
        public static Material Flat
        {
            get
            {
                if (_flat != null) return _flat;
                const int n = 32; _atlas = new Texture2D(n * 3, n, TextureFormat.RGBA32, false) { name = "LokiClipShapes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * 3 * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n * 3; x++)
                    {
                        int cell = x / n; float r = new Vector2((x % n) - (n - 1) * 0.5f, y - (n - 1) * 0.5f).magnitude / (n * 0.5f);
                        float a = cell == 0 ? 1f : cell == 1 ? Mathf.Clamp01((1f - r) * n * 0.5f) : Mathf.Clamp01((1f - r) * n * 0.5f) * Mathf.Clamp01((r - 0.72f) * n * 0.5f);
                        px[y * n * 3 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _atlas.SetPixels32(px); _atlas.Apply(false, true);
                var sh = Shader.Find("Loki/UIOverlay");
                _flat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "LokiClipFlat", mainTexture = _atlas, renderQueue = DioramaRig.LaserQueue };
                return _flat;
            }
        }
        /// <summary>The corners (u0, v0, u1, v1) of the solid square (0), the disc (1) or the ring (2) in <see cref="Flat"/>'s picture.</summary>
        private static Vector4 Uv(int cell) => cell == 0 ? new Vector4(0.16f, 0.5f, 0.17f, 0.51f) : new Vector4(cell / 3f, 0f, (cell + 1) / 3f, 1f);

        private void Build()
        {
            if (_rig != null) return;
            var map = Map?.Invoke();
            _rig = new GameObject("ClipStageRig").transform; _rig.gameObject.layer = StageLayer; _rig.SetParent(map, false);
            _headT = Node("head", _rig); _handLT = Node("handL", _rig); _handRT = Node("handR", _rig);
            _cam = _headT.gameObject.AddComponent<Camera>();
            _cam.CopyFrom(_real.Head); _cam.enabled = false; _cam.stereoTargetEye = StereoTargetEyeMask.None; _cam.depth = _real.Head.depth - 5f;
            _cam.fieldOfView = FieldOfView; _cam.aspect = PictureW / (float)PictureH; _cam.allowMSAA = false; _cam.allowHDR = false; _cam.useOcclusionCulling = false;
            // the world and the stage; never the real panels, the real character's own layer in first person, what is hidden for the clip, the laser-only colliders
            // (client wave 22, several clips at once: nor the stage's own layer: every stage is parked there, and this camera's own stage
            // comes to PictureLayer for its one picture: Render)
            _cam.cullingMask = ~((1 << UiKit.PanelLayer) | (1 << ViewController.SelfLayer) | (1 << HiddenLayer) | (1 << LookSelect.LaserOnlyLayer) | (1 << StageLayer));
            Texture = new RenderTexture(PictureW, PictureH, 16, RenderTextureFormat.ARGB32) { name = "LokiTutorialClip", antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Texture.Create(); _cam.targetTexture = Texture;
            // the lasers: one small mesh in the rig's space, rebuilt every picture
            var lgo = new GameObject("lasers"); lgo.layer = StageLayer; lgo.transform.SetParent(_rig, false);
            _lineMesh = new Mesh { name = "ClipLasers" }; _lineMesh.MarkDynamic(); lgo.AddComponent<MeshFilter>().sharedMesh = _lineMesh;
            _lineR = lgo.AddComponent<MeshRenderer>(); _lineR.sharedMaterial = Flat; _lineR.sortingOrder = DioramaRig.LaserSortingOrder; NoShadows(_lineR);
            _cursor = Quad("cursor", map, 0, 1); _lookRing = Quad("lookRing", map, 2, 1); _pulse = Quad("pulse", map, 2, 2);
            // the check mark: a green disc with a dark rim and a white tick (UI/CheckMarkFx.cs's colours), 1 across
            Bv.Clear(); Bc.Clear(); Bu.Clear(); Bt.Clear();
            AddQuad(Vector2.zero, Vector2.one, 0f, CheckMarkFx.Rim, 1); AddQuad(Vector2.zero, Vector2.one * 0.88f, 0f, CheckMarkFx.Disc, 1);
            AddQuad(new Vector2(-0.15f, -0.07f), new Vector2(0.27f, 0.10f), -45f, CheckMarkFx.Tick, 0); AddQuad(new Vector2(0.09f, 0.03f), new Vector2(0.50f, 0.10f), 50f, CheckMarkFx.Tick, 0);
            var cm = new Mesh { name = "ClipCheck" }; cm.SetVertices(Bv); cm.SetColors(Bc); cm.SetUVs(0, Bu); cm.SetTriangles(Bt, 0); cm.RecalculateBounds();
            var cgo = new GameObject("clip-check"); cgo.layer = StageLayer; cgo.transform.SetParent(_rig, false); cgo.AddComponent<MeshFilter>().sharedMesh = cm;
            var cr = cgo.AddComponent<MeshRenderer>(); cr.sharedMaterial = Flat; cr.sortingOrder = CheckMarkFx.Order; NoShadows(cr); cgo.SetActive(false); _check = cgo.transform;
        }
        private static Transform Node(string name, Transform parent) { var go = new GameObject(name); go.layer = StageLayer; go.transform.SetParent(parent, false); return go.transform; }
        private static void NoShadows(Renderer r) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        private static readonly Mesh[] Quads = new Mesh[3];
        /// <summary>A unit quad (1 x 1, in its own x-y plane) showing the solid square, the disc or the ring.</summary>
        private static Mesh QuadMesh(int cell)
        {
            if (Quads[cell] != null) return Quads[cell];
            var uv = Uv(cell);
            var m = new Mesh { name = "ClipQuad" + cell }; Quads[cell] = m;
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            m.uv = new[] { new Vector2(uv.x, uv.y), new Vector2(uv.x, uv.w), new Vector2(uv.z, uv.w), new Vector2(uv.z, uv.y) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return m;
        }
        private Transform Quad(string name, Transform parent, int cell, int order)
        {
            var go = new GameObject("clip-" + name); go.layer = StageLayer; go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(cell);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Flat; r.sortingOrder = order; NoShadows(r);
            go.SetActive(false); return go.transform;
        }
        private void Tint(Transform t, Color c) { _mpb.SetColor("_Color", c); t.GetComponent<MeshRenderer>().SetPropertyBlock(_mpb); }

        // ------------------------------------------------------------------ play / stop
        /// <summary>Starts <paramref name="clip"/> from its beginning, the hands drawn as <paramref name="profile"/>'s controller.</summary>
        public void Play(ClipTimeline clip, Profile profile)
        {
            if (clip == null) { Stop(); return; }
            Build();
            var map = Map?.Invoke();
            if (map != null && _rig.parent != map) { _rig.SetParent(map, false); _cursor.SetParent(map, false); _lookRing.SetParent(map, false); _pulse.SetParent(map, false); }
            Clear();
            // while a clip plays the head camera leaves the stage out (the screenshot camera copies its mask; the desktop spectator's own
            // mask never has it: World/SpectatorView.cs); with no clip there is nothing on that layer and the mask is as it always was
            if (Clip == null) PlayingCount++;
            _real.Head.cullingMask &= ~(1 << StageLayer);
            Clip = clip; Profile = profile; ClipTime = 0f; Loops = 0; _renderSum = 0; _renderCount = 0; MaxRenderMs = 0; _renderAt = Stagger / Mathf.Max(1f, PicturesPerSecond);
            _ghostL = ControllerGhost.Create(_handLT, profile, true, StageLayer, DioramaRig.LaserSortingOrder, true);
            _ghostR = ControllerGhost.Create(_handRT, profile, false, StageLayer, DioramaRig.LaserSortingOrder, true);
            foreach (var p in clip.Panels)
            {
                if (!clip.ShowGuide && p.Name == "TutorialGuide") continue;
                var parent = p.On == ClipTimeline.Anchor.Head ? _headT : p.On == ClipTimeline.Anchor.LeftHand ? _handLT : p.On == ClipTimeline.Anchor.RightHand ? _handRT : _rig;
                var go = new GameObject("panel:" + p.Name); go.layer = StageLayer; go.transform.SetParent(parent, false);
                var pv = new PanelView { Data = p, T = go.transform, Mesh = new Mesh { name = "ClipPanel" } };
                pv.Mesh.MarkDynamic(); go.AddComponent<MeshFilter>().sharedMesh = pv.Mesh;
                pv.R = go.AddComponent<MeshRenderer>(); pv.R.sharedMaterial = Flat; pv.R.sortingOrder = Mathf.Clamp(p.Sort, 2, DioramaRig.LaserSortingOrder - 1); NoShadows(pv.R);
                go.SetActive(false); _panels.Add(pv);
            }
            foreach (var s in clip.Shapes)
            {
                var sv = new ShapeView { Data = s };
                if (s.Kind == ClipTimeline.ShapeKind.FillRing)
                {
                    sv.Ring = LookFillRing.Create(_rig, () => _cam, "clip-fillring"); sv.Ring.gameObject.layer = StageLayer; sv.T = sv.Ring.transform;
                }
                else if (s.Kind == ClipTimeline.ShapeKind.CodexRing) // (wave 28, wcodex: the Codex's own scan ring, the real one)
                {
                    sv.Codex = CodexInkRing.Create(_rig, () => _cam, "clip-codexring"); sv.T = sv.Codex.transform; SetLayer(sv.T, StageLayer);
                }
                else { sv.T = Quad("shape", _rig, s.Kind == ClipTimeline.ShapeKind.Ring ? 2 : s.Kind == ClipTimeline.ShapeKind.Disc ? 1 : 0, DioramaRig.LaserSortingOrder - 1); sv.R = sv.T.GetComponent<MeshRenderer>(); }
                _shapes.Add(sv);
            }
            MakeActors();
            Restart();
            BuildFocus();
            Apply();
        }

        // ------------------------------------------------------------------ the framing (client wave 22, the casting ways)
        /// <summary>Where the picture's eye looks and how wide, every <see cref="FocusStep"/> s of the clip, in the rig's space: at the
        /// shown tool panels (each a ball around its middle as wide as its parts reach) and at the hands the script uses, when the picture
        /// still holds them at <see cref="FocusHandMaxHalf"/>°; with no tool shown, at those hands. Averaged over
        /// <see cref="FocusSmooth"/> s each way, less towards the ends of the action: in the two stills nothing is averaged, so the eye
        /// stands still there as everything else does.</summary>
        private void BuildFocus()
        {
            _focusDir = null; _focusHalf = null;
            var clip = Clip; if (Focus == null || Focus.Length == 0 || clip == null || clip.Length < 0.2f) return;
            int n = Mathf.CeilToInt(clip.Length / FocusStep) + 1; var dir = new Vector3[n]; var half = new float[n];
            var tools = new List<ClipTimeline.Panel>(); var reach = new List<float>();
            foreach (var p in clip.Panels)
            {
                if (!IsFocus(p.Name)) continue; float r = 0f;
                foreach (var g in p.Graphics) if (g.Kind != ClipTimeline.GraphicKind.Off) r = Mathf.Max(r, new Vector2(Mathf.Abs(g.X) + g.W * 0.5f, Mathf.Abs(g.Y) + g.H * 0.5f).magnitude);
                tools.Add(p); reach.Add(r);
            }
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Min(i * FocusStep, clip.Length);
                if (!clip.PoseAt(clip.Head, t, out var hp, out var hq)) { hp = Vector3.zero; hq = Quaternion.identity; }
                bool hasL = clip.PoseAt(clip.HandL, t, out var lp, out var lq), hasR = clip.PoseAt(clip.HandR, t, out var rp, out var rq);
                bool any = false; Vector3 c = Vector3.zero; float r = 0f;
                void Add(Vector3 c2, float r2)
                {
                    if (!any) { any = true; c = c2; r = r2; return; }
                    float d = Vector3.Distance(c, c2);
                    if (d + r2 <= r) return; if (d + r <= r2) { c = c2; r = r2; return; }
                    float R = (d + r + r2) * 0.5f; c += (c2 - c) * ((R - r) / d); r = R;
                }
                float HalfOf(Vector3 centre, float radius) { float d = Vector3.Distance(centre, hp); return d < 1e-4f ? 90f : Mathf.Asin(Mathf.Min(0.99f, radius / d)) * Mathf.Rad2Deg * FocusMargin; }
                for (int k = 0; k < tools.Count; k++)
                {
                    var p = tools[k]; if (!p.Pose.Sample(t, _v) || _v[9] < 0.5f || _v[8] < 0.15f) continue;
                    var pc = new Vector3(_v[0], _v[1], _v[2]); float pr = reach[k] * _v[7];
                    if (p.On == ClipTimeline.Anchor.Head) pc = hp + hq * pc; else if (p.On == ClipTimeline.Anchor.LeftHand && hasL) pc = lp + lq * pc; else if (p.On == ClipTimeline.Anchor.RightHand && hasR) pc = rp + rq * pc;
                    Add(pc, pr);
                }
                bool tool = any;
                for (int h = 0; h < 2; h++)
                {
                    if (!(h == 0 ? clip.UsesLeft && hasL : clip.UsesRight && hasR)) continue; var hc = h == 0 ? lp : rp;
                    if (!tool) { Add(hc, FocusHandM); continue; }
                    var c0 = c; float r0 = r; Add(hc, FocusHandM);
                    if (HalfOf(c, r) > FocusHandMaxHalf) { c = c0; r = r0; } // (the tool alone: with this hand the picture would be too wide to show it)
                }
                if (!any) { dir[i] = hq * Vector3.forward; half[i] = FocusMaxHalf; continue; }
                var to = c - hp; dir[i] = to.sqrMagnitude > 1e-8f ? to.normalized : hq * Vector3.forward; half[i] = Mathf.Clamp(HalfOf(c, r), FocusMinHalf, FocusMaxHalf);
            }
            var (first, last) = clip.ActionSpan(); _focusDir = new Vector3[n]; _focusHalf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i * FocusStep; int w = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Min(FocusSmooth, Mathf.Min(t - first, last - t))) / FocusStep);
                var sum = Vector3.zero; float hs = 0f; int cnt = 0;
                for (int j = Mathf.Max(0, i - w); j <= Mathf.Min(n - 1, i + w); j++) { sum += dir[j]; hs += half[j]; cnt++; }
                _focusDir[i] = sum.sqrMagnitude > 1e-8f ? sum.normalized : dir[i]; _focusHalf[i] = hs / Mathf.Max(1, cnt);
            }
        }

        private void ApplyFocus(float t)
        {
            if (_cam == null) return;
            if (_focusDir == null)
            {
                if (_focused) { _focused = false; _cam.ResetWorldToCameraMatrix(); _cam.fieldOfView = FieldOfView; }
                ViewOffDegrees = 0f; return;
            }
            int n = _focusDir.Length; float x = Mathf.Clamp(t / FocusStep, 0f, n - 1); int i = Mathf.Min((int)x, n - 1), j = Mathf.Min(i + 1, n - 1); float k = x - i;
            var d = Vector3.Slerp(_focusDir[i], _focusDir[j], k); float half = Mathf.Lerp(_focusHalf[i], _focusHalf[j], k);
            // the eye stays the recorded head's place; only where it looks and how wide is the framing's (the camera's own transform is
            // the head still: the sprites turn to its place, the head-held panels hang on it)
            var rot = _rig.rotation * Quaternion.LookRotation(d, Vector3.up);
            _focused = true; _cam.fieldOfView = 2f * half; ViewOffDegrees = Quaternion.Angle(rot, _headT.rotation);
            _cam.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(_headT.position, rot, Vector3.one).inverse;
        }

        /// <summary>The clip at its moment <paramref name="t"/> (a test; a pause button later): the panels' parts as they were then, no
        /// moment replayed.</summary>
        public void Seek(float t)
        {
            if (Clip == null) return;
            ClipTime = Mathf.Clamp(t, 0f, Clip.Length);
            foreach (var p in _panels) { p.Cursor = 0; p.State.Clear(); p.Dirty = true; }
            Apply();
        }

        public void Stop()
        {
            if (Clip == null) return;
            Clip = null; Clear();
            PlayingCount = Mathf.Max(0, PlayingCount - 1);
            if (PlayingCount == 0 && _real != null && _real.Head != null) _real.Head.cullingMask |= 1 << StageLayer;
        }

        private void Clear()
        {
            foreach (var p in _panels) { if (p.T != null) Destroy(p.T.gameObject); if (p.Mesh != null) Destroy(p.Mesh); }
            _panels.Clear();
            foreach (var s in _shapes) if (s.T != null) Destroy(s.T.gameObject);
            _shapes.Clear();
            if (_ghostL != null) Destroy(_ghostL.gameObject); if (_ghostR != null) Destroy(_ghostR.gameObject); _ghostL = _ghostR = null;
            if (_self != null) Destroy(_self.gameObject); if (_poring != null) Destroy(_poring.gameObject); _self = _poring = null; _selfAct = _poringAct = -1;
            if (_other != null) Destroy(_other.gameObject); _other = null; _otherAct = -1;
            if (_cursor != null) _cursor.gameObject.SetActive(false); if (_lookRing != null) _lookRing.gameObject.SetActive(false); if (_pulse != null) _pulse.gameObject.SetActive(false);
            if (_check != null) _check.gameObject.SetActive(false);
            if (_lineMesh != null) _lineMesh.Clear();
            PanelsShown = ShapesShown = 0;
        }

        /// <summary>The player's own character and a practice Poring as units of the real kind (Sprites/Unit.cs), drawn for the clip only:
        /// not in the game's unit list (the look and the laser never find them: no collider, no name plate), silent.</summary>
        private void MakeActors()
        {
            var map = Map?.Invoke(); var gat = Gat?.Invoke(); var assets = Assets?.Invoke(); var real = Self?.Invoke();
            if (map == null || gat == null || assets == null) return;
            try
            {
                if (Clip.Self.Count > 0 && real != null)
                {
                    _self = Unit.Create(map, gat, UnitIdSelf, real.Job, real.Sex, real.Hair, assets, real.Look); _self.name = "clip-actor-self"; _self.Option = real.Option;
                    _self.SetCell(Mathf.RoundToInt(real.Cell.x), Mathf.RoundToInt(real.Cell.y), real.Sprite != null ? real.Sprite.Direction : 0);
                }
                if (Clip.Poring.Count > 0)
                {
                    _poring = Unit.Create(map, gat, UnitIdPoring, 1002, 0, 0, assets); _poring.name = "clip-actor-poring";
                    var c = real != null ? real.Cell : Vector2.zero; _poring.SetCell(Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y), 0);
                }
                if (Clip.Other.Count > 0 && Clip.OtherJob > 0) // (wave 28, wcodex: the Codex step's practice Lunatic)
                {
                    _other = Unit.Create(map, gat, UnitIdOther, Clip.OtherJob, 0, 0, assets); _other.name = "clip-actor-other";
                    var c = real != null ? real.Cell : Vector2.zero; _other.SetCell(Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y), 0);
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A loop begins: the recorded character's first cell is laid on the player's own character as it stands now; the panels'
        /// parts and the moments start over.</summary>
        private void Restart()
        {
            var real = Self?.Invoke();
            _anchor = Vector3.zero;
            if (Clip.Self.Sample(0f, _v) && real != null) _anchor = real.transform.localPosition - new Vector3(_v[0], _v[1], _v[2]);
            foreach (var p in _panels) { p.Cursor = 0; p.State.Clear(); p.Dirty = true; }
            _checkShown = -1;
        }

        // ------------------------------------------------------------------ every frame
        private void LateUpdate()
        {
            if (Clip == null || _rig == null) return;
            if (!Paused)
            {
                ClipTime += Time.unscaledDeltaTime;
                if (Clip.Length > 0.01f && ClipTime >= Clip.Length) { ClipTime %= Clip.Length; Loops++; Restart(); }
            }
            Apply();
            _renderAt -= Time.unscaledDeltaTime;
            float period = 1f / Mathf.Max(1f, PicturesPerSecond);
            if (_renderAt <= 0f) { _renderAt += period; if (_renderAt < -0.5f * period) _renderAt = 0f; if (Drawing) Render(); }
        }

        /// <summary>The stage's rig in the map's space: the recorded rig as the recorded map saw it, moved so the recorded character's first
        /// cell is the player's character's. (The map's place in the rig was M: a point p of the rig is the cell M⁻¹ p.)</summary>
        private void PlaceRig(float t)
        {
            if (!Clip.Map.Sample(t, _v)) { _rig.localPosition = _anchor; _rig.localRotation = Quaternion.identity; _rig.localScale = Vector3.one; return; }
            var pm = new Vector3(_v[0], _v[1], _v[2]); var qm = new Quaternion(_v[3], _v[4], _v[5], _v[6]).normalized; float sm = Mathf.Max(1e-5f, _v[7]);
            var inv = Quaternion.Inverse(qm);
            _rig.localRotation = inv; _rig.localScale = Vector3.one / sm; _rig.localPosition = _anchor - (inv * pm) / sm;
        }

        private void Apply()
        {
            float t = ClipTime; var clip = Clip;
            PlaceRig(t);
            if (clip.PoseAt(clip.Head, t, out var hp, out var hq)) { _headT.localPosition = hp; _headT.localRotation = hq; }
            bool left = clip.PoseAt(clip.HandL, t, out var lp, out var lq), right = clip.PoseAt(clip.HandR, t, out var rp, out var rq);
            if (left) { _handLT.localPosition = lp; _handLT.localRotation = lq; } if (right) { _handRT.localPosition = rp; _handRT.localRotation = rq; }
            bool mainLeft = clip.IsMirrored; // (a mirrored clip is a left-handed player's: the pointing hand is the left one)
            if (_ghostL != null) { _ghostL.gameObject.SetActive(left); _ghostL.Apply(clip.InputAt(true, t), mainLeft); }
            if (_ghostR != null) { _ghostR.gameObject.SetActive(right); _ghostR.Apply(clip.InputAt(false, t), !mainLeft); }
            ApplyUnit(clip.Self, _self, ref _selfAct, t); ApplyUnit(clip.Poring, _poring, ref _poringAct, t); ApplyUnit(clip.Other, _other, ref _otherAct, t);
            // the cell cursor and the look's ring lie on the ground (the map's cells)
            if (clip.Cursor.Sample(t, _v) && _v[0] > 0.5f) { _cursor.gameObject.SetActive(true); _cursor.localPosition = Ground(new Vector3(_v[1], _v[2], _v[3]) + _anchor, 0.06f); _cursor.localRotation = Quaternion.Euler(90f, 0f, 0f); _cursor.localScale = new Vector3(0.9f, 0.9f, 1f); Tint(_cursor, new Color(0.4f, 1f, 0.5f, 0.6f)); }
            else _cursor.gameObject.SetActive(false);
            if (clip.LookRing.Sample(t, _v) && _v[0] > 0.5f) { _lookRing.gameObject.SetActive(true); _lookRing.localPosition = Ground(new Vector3(_v[1], _v[2], _v[3]) + _anchor, 0.08f); _lookRing.localRotation = Quaternion.Euler(90f, 0f, 0f); _lookRing.localScale = new Vector3(_v[4] * 2f, _v[4] * 2f, 1f); Tint(_lookRing, new Color(_v[5], _v[6], _v[7], 0.9f)); }
            else _lookRing.gameObject.SetActive(false);
            int shown = 0;
            foreach (var p in _panels) if (ApplyPanel(p, t)) shown++;
            PanelsShown = shown; shown = 0;
            foreach (var s in _shapes) if (ApplyShape(s, t)) shown++;
            ShapesShown = shown;
            // moments, read from the clip's time alone: the last hit's flash on the Poring, the last check mark (the clip's own picture of
            // UI/CheckMarkFx.cs's: its size, its pop, its hold, its fade and rise; silent, and never counted among the game's check marks)
            int hit = -1, check = -1;
            for (int i = 0; i < clip.Moments.Count && clip.Moments[i].T <= t; i++) { if (clip.Moments[i].Type == "hit") hit = i; else if (clip.Moments[i].Type == "check") check = i; }
            float hk = hit >= 0 ? (t - clip.Moments[hit].T) / 0.45f : 2f;
            if (hk > 1f) { if (_pulse.gameObject.activeSelf) _pulse.gameObject.SetActive(false); }
            else
            {
                var m = clip.Moments[hit]; _pulse.localPosition = new Vector3(m.X, m.Y, m.Z) + _anchor;
                _pulse.gameObject.SetActive(true); float d = Mathf.Lerp(0.6f, 2.6f, hk); _pulse.localScale = new Vector3(d, d, 1f);
                _pulse.rotation = Quaternion.LookRotation(_pulse.position - _headT.position, _headT.up); Tint(_pulse, new Color(1f, 0.6f, 0.75f, 1f - hk));
            }
            float ck = check >= 0 ? t - clip.Moments[check].T : 99f, total = CheckMarkFx.PopSeconds + CheckMarkFx.HoldSeconds + CheckMarkFx.FadeSeconds;
            if (ck >= total) { if (_check.gameObject.activeSelf) _check.gameObject.SetActive(false); }
            else
            {
                if (check != _checkShown) { _checkShown = check; Checks++; }
                var m = clip.Moments[check]; var at = new Vector3(m.X, m.Y, m.Z); float dist = Mathf.Max(0.2f, (at - _headT.localPosition).magnitude);
                float pop = Mathf.Clamp01(ck / CheckMarkFx.PopSeconds), grow = pop < 0.6f ? Mathf.Lerp(0f, 1.2f, pop / 0.6f) : Mathf.Lerp(1.2f, 1f, (pop - 0.6f) / 0.4f);
                float fade = Mathf.Clamp01((ck - CheckMarkFx.PopSeconds - CheckMarkFx.HoldSeconds) / CheckMarkFx.FadeSeconds);
                float size = 2f * dist * Mathf.Tan(CheckMarkFx.SizeDeg * 0.5f * Mathf.Deg2Rad) * grow;
                _check.gameObject.SetActive(true);
                _check.localPosition = at + (_headT.localRotation * Vector3.up) * (dist * Mathf.Tan(CheckMarkFx.RiseDeg * Mathf.Deg2Rad) * fade);
                _check.localRotation = Quaternion.LookRotation(_check.localPosition - _headT.localPosition, _headT.localRotation * Vector3.up); _check.localScale = new Vector3(size, size, 1f);
                Tint(_check, new Color(1f, 1f, 1f, 1f - fade));
            }
            Lasers(t);
            ApplyFocus(t);
        }

        /// <summary>A place given in the map's cells, on the player's map's ground there (the recorded height where the map has no cell).</summary>
        private Vector3 Ground(Vector3 local, float lift)
        {
            var gat = Gat?.Invoke(); int cx = Mathf.FloorToInt(local.x), cy = Mathf.FloorToInt(local.z);
            if (gat != null && gat.InBounds(cx, cy)) local.y = -gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
            local.y += lift; return local;
        }

        private void ApplyUnit(ClipTimeline.Track tr, Unit u, ref int act, float t)
        {
            if (u == null) return;
            if (!tr.Sample(t, _v)) { if (u.gameObject.activeSelf) u.gameObject.SetActive(false); return; }
            bool shown = _v[5] > 0.5f;
            if (u.gameObject.activeSelf != shown) { u.gameObject.SetActive(shown); act = -1; }
            if (!shown) return;
            u.transform.localPosition = Ground(new Vector3(_v[0], _v[1], _v[2]) + _anchor, 0f);
            if (u.Sprite != null)
            {
                u.Sprite.Direction = Mathf.RoundToInt(_v[3]) & 7;
                int a = Mathf.RoundToInt(_v[4]); if (a != act) { act = a; u.Sprite.Play(a, a <= 1 || (u.IsPlayerType && a == (int)ActorSprite.PlayerAction.Standby)); }
            }
            u.SetHover(_v[6] > 0.5f);
            SetLayer(u.transform, StageLayer);
        }

        private static void SetLayer(Transform t, int layer)
        {
            if (t.gameObject.layer != layer) t.gameObject.layer = layer;
            for (int i = 0, c = t.childCount; i < c; i++) SetLayer(t.GetChild(i), layer);
        }

        private bool ApplyPanel(PanelView p, float t)
        {
            var keys = p.Data.Graphics;
            while (p.Cursor < keys.Count && keys[p.Cursor].T <= t) { var k = keys[p.Cursor++]; if (k.Kind == ClipTimeline.GraphicKind.Off) p.State.Remove(k.Id); else p.State[k.Id] = k; p.Dirty = true; }
            bool on = p.Data.Pose.Sample(t, _v) && _v[9] > 0.5f && _v[8] > 0.01f && p.State.Count > 0;
            if (p.T.gameObject.activeSelf != on) p.T.gameObject.SetActive(on);
            if (!on) return false;
            p.T.localPosition = new Vector3(_v[0], _v[1], _v[2]); p.T.localRotation = new Quaternion(_v[3], _v[4], _v[5], _v[6]).normalized; p.T.localScale = Vector3.one * _v[7];
            if (Mathf.Abs(p.Alpha - _v[8]) > 0.003f) { p.Alpha = _v[8]; _mpb.SetColor("_Color", new Color(1f, 1f, 1f, p.Alpha)); p.R.SetPropertyBlock(_mpb); }
            if (p.Dirty) { p.Dirty = false; BuildPanel(p); }
            return true;
        }

        private static readonly List<Vector3> Bv = new List<Vector3>(); private static readonly List<Color> Bc = new List<Color>(); private static readonly List<Vector2> Bu = new List<Vector2>(); private static readonly List<int> Bt = new List<int>();
        /// <summary>A shape's colour as a mesh must carry it: the game draws in linear colour space, and a canvas turns its vertex colours into
        /// it by itself; a mesh's are taken as they are (they would come out too bright).</summary>
        private static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        private static readonly List<ClipTimeline.GraphicKey> Bk = new List<ClipTimeline.GraphicKey>();
        private static void AddQuad(Vector2 centre, Vector2 size, float rotDeg, Color32 c, int cell)
        {
            float cs = 1f, sn = 0f; if (Mathf.Abs(rotDeg) > 0.01f) { cs = Mathf.Cos(rotDeg * Mathf.Deg2Rad); sn = Mathf.Sin(rotDeg * Mathf.Deg2Rad); }
            Vector3 P(float x, float y) => new Vector3(centre.x + (x * cs - y * sn), centre.y + (x * sn + y * cs), 0f);
            float hx = size.x * 0.5f, hy = size.y * 0.5f; int i = Bv.Count; var uv = Uv(cell);
            Bv.Add(P(-hx, -hy)); Bv.Add(P(-hx, hy)); Bv.Add(P(hx, hy)); Bv.Add(P(hx, -hy));
            Bu.Add(new Vector2(uv.x, uv.y)); Bu.Add(new Vector2(uv.x, uv.w)); Bu.Add(new Vector2(uv.z, uv.w)); Bu.Add(new Vector2(uv.z, uv.y));
            Color lc = Lin(c); for (int k = 0; k < 4; k++) Bc.Add(lc);
            Bt.Add(i); Bt.Add(i + 1); Bt.Add(i + 2); Bt.Add(i); Bt.Add(i + 2); Bt.Add(i + 3);
        }

        /// <summary>A panel's parts as they are now, in their drawing order: rectangles, discs, rings; a text as bars (one per line, the last
        /// one shorter: no words); a picture as a plain tile.</summary>
        private static void BuildPanel(PanelView p)
        {
            Bv.Clear(); Bc.Clear(); Bu.Clear(); Bt.Clear(); Bk.Clear();
            foreach (var kv in p.State) Bk.Add(kv.Value);
            Bk.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Id.CompareTo(b.Id));
            foreach (var g in Bk)
            {
                if (g.W <= 0.01f || g.H <= 0.01f || g.Colour.a == 0) continue;
                switch (g.Kind)
                {
                    case ClipTimeline.GraphicKind.Disc: AddQuad(new Vector2(g.X, g.Y), new Vector2(g.W, g.H), g.Rot, g.Colour, 1); break;
                    case ClipTimeline.GraphicKind.Ring: AddQuad(new Vector2(g.X, g.Y), new Vector2(g.W, g.H), g.Rot, g.Colour, 2); break;
                    case ClipTimeline.GraphicKind.Picture:
                        {
                            var c = new Color32((byte)(g.Colour.r * 0.62f), (byte)(g.Colour.g * 0.68f), (byte)(g.Colour.b * 0.78f), g.Colour.a);
                            AddQuad(new Vector2(g.X, g.Y), new Vector2(g.W, g.H), g.Rot, c, Mathf.RoundToInt(g.P) == 1 ? 1 : 0); break;
                        }
                    case ClipTimeline.GraphicKind.Text:
                        {
                            int lines = Mathf.Clamp(Mathf.RoundToInt(g.P), 1, 40); int align = Mathf.Clamp(Mathf.FloorToInt(g.Q / 10f), 0, 2); float frac = Mathf.Clamp(g.Q - align * 10f, 0.04f, 1f);
                            float lh = g.H / lines, bar = Mathf.Min(lh * 0.46f, g.H); var c = g.Colour; c.a = (byte)(c.a * 0.8f);
                            float cs = Mathf.Cos(g.Rot * Mathf.Deg2Rad), sn = Mathf.Sin(g.Rot * Mathf.Deg2Rad);
                            for (int i = 0; i < lines; i++)
                            {
                                float w = g.W * (i == lines - 1 ? frac : lines == 1 ? frac : 0.96f - 0.06f * (i % 3));
                                float x = align == 0 ? -g.W * 0.5f + w * 0.5f : align == 2 ? g.W * 0.5f - w * 0.5f : 0f, y = g.H * 0.5f - (i + 0.5f) * lh;
                                AddQuad(new Vector2(g.X + x * cs - y * sn, g.Y + x * sn + y * cs), new Vector2(w, bar), g.Rot, c, 0);
                            }
                            break;
                        }
                    case ClipTimeline.GraphicKind.Outline:
                        {
                            // a frame: four strips along the rectangle's edges, the line g.P px wide
                            float w = Mathf.Clamp(g.P, 1f, Mathf.Min(g.W, g.H) * 0.5f); float cs = Mathf.Cos(g.Rot * Mathf.Deg2Rad), sn = Mathf.Sin(g.Rot * Mathf.Deg2Rad);
                            void Strip(float x, float y, float sw, float sh) => AddQuad(new Vector2(g.X + x * cs - y * sn, g.Y + x * sn + y * cs), new Vector2(sw, sh), g.Rot, g.Colour, 0);
                            Strip(0f, g.H * 0.5f - w * 0.5f, g.W, w); Strip(0f, -g.H * 0.5f + w * 0.5f, g.W, w);
                            Strip(-g.W * 0.5f + w * 0.5f, 0f, w, g.H - 2f * w); Strip(g.W * 0.5f - w * 0.5f, 0f, w, g.H - 2f * w);
                            break;
                        }
                    case ClipTimeline.GraphicKind.Sector:
                        {
                            // a piece of a wheel: from g.P to g.Q degrees clockwise from straight up, between the radii g.H / 2 and g.W / 2
                            float a0 = g.P, a1 = g.Q, r0 = g.H * 0.5f, r1 = g.W * 0.5f; int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 12f), 1, 30); var uv = Uv(0);
                            for (int i = 0; i < n; i++)
                            {
                                float b0 = (Mathf.Lerp(a0, a1, i / (float)n) - g.Rot) * Mathf.Deg2Rad, b1 = (Mathf.Lerp(a0, a1, (i + 1) / (float)n) - g.Rot) * Mathf.Deg2Rad; int k = Bv.Count;
                                Bv.Add(new Vector3(g.X + Mathf.Sin(b0) * r0, g.Y + Mathf.Cos(b0) * r0, 0f)); Bv.Add(new Vector3(g.X + Mathf.Sin(b0) * r1, g.Y + Mathf.Cos(b0) * r1, 0f));
                                Bv.Add(new Vector3(g.X + Mathf.Sin(b1) * r1, g.Y + Mathf.Cos(b1) * r1, 0f)); Bv.Add(new Vector3(g.X + Mathf.Sin(b1) * r0, g.Y + Mathf.Cos(b1) * r0, 0f));
                                for (int c = 0; c < 4; c++) { Bc.Add(Lin(g.Colour)); Bu.Add(new Vector2(uv.x, uv.y)); }
                                Bt.Add(k); Bt.Add(k + 1); Bt.Add(k + 2); Bt.Add(k); Bt.Add(k + 2); Bt.Add(k + 3);
                            }
                            break;
                        }
                    default: AddQuad(new Vector2(g.X, g.Y), new Vector2(g.W, g.H), g.Rot, g.Colour, 0); break;
                }
            }
            p.Mesh.Clear(); p.Mesh.SetVertices(Bv); p.Mesh.SetColors(Bc); p.Mesh.SetUVs(0, Bu); p.Mesh.SetTriangles(Bt, 0); p.Mesh.RecalculateBounds();
        }

        private bool ApplyShape(ShapeView s, float t)
        {
            bool on = s.Data.Pose.Sample(t, _v) && _v[14] > 0.5f && _v[12] > 0.01f;
            if (s.Codex != null) // (wave 28, wcodex: the scan ring eases its recorded real progress itself, and plays its flourish once)
            {
                if (!on) { if (s.Codex.Shown) s.Codex.Hide(); s.CodexDone = false; return false; }
                bool finishing = _v[9] > 0.5f;
                if (finishing && s.CodexDone && !s.Codex.Shown) return false; // (its flourish played and hid it)
                s.Codex.Fill = _v[13];
                if (!s.T.gameObject.activeSelf) { s.T.gameObject.SetActive(true); s.Codex.Snap(); }
                s.T.localPosition = new Vector3(_v[0], _v[1], _v[2]);
                if (!s.Codex.Finishing) s.Codex.Radius = _v[7] * 0.5f;
                if (finishing && !s.CodexDone) { s.CodexDone = true; s.Codex.Done(_v[10] > 0.5f); }
                else if (!finishing) s.CodexDone = false;
                return true;
            }
            if (s.Ring != null)
            {
                if (!on) { s.Ring.Hide(); return false; }
                s.T.localPosition = new Vector3(_v[0], _v[1], _v[2]); s.T.localRotation = new Quaternion(_v[3], _v[4], _v[5], _v[6]).normalized;
                s.Ring.Radius = _v[7] * 0.5f; s.Ring.Fill = _v[13]; s.Ring.Alpha = _v[12];
                if (!s.T.gameObject.activeSelf) s.T.gameObject.SetActive(true);
                return true;
            }
            if (s.T.gameObject.activeSelf != on) s.T.gameObject.SetActive(on);
            if (!on) return false;
            s.T.localPosition = new Vector3(_v[0], _v[1], _v[2]); s.T.localRotation = new Quaternion(_v[3], _v[4], _v[5], _v[6]).normalized; s.T.localScale = new Vector3(_v[7], _v[8], 1f);
            var c = new Color(_v[9], _v[10], _v[11], _v[12]); if (c != s.Colour) { s.Colour = c; _mpb.SetColor("_Color", c); s.R.SetPropertyBlock(_mpb); }
            return true;
        }

        /// <summary>The two lasers as thin strips facing the clip's eye, from the hand to where the recorded laser ended.</summary>
        private void Lasers(float t)
        {
            Bv.Clear(); Bc.Clear(); Bu.Clear(); Bt.Clear();
            var eye = _headT.localPosition;
            for (int which = 0; which < 3; which++)
            {
                // (the third strip is the wand: a stick as thick as the real one, VR/WandHolster.cs, solid from its grip to its tip)
                var tr = which == 0 ? Clip.Laser : which == 1 ? Clip.OffLaser : Clip.Wand; bool wand = which == 2;
                if (!tr.Sample(t, _v) || _v[0] < 0.03f) continue;
                var a = new Vector3(_v[1], _v[2], _v[3]); var b = new Vector3(_v[4], _v[5], _v[6]);
                var side = Vector3.Cross(b - a, eye - a); if (side.sqrMagnitude < 1e-10f) continue; side.Normalize();
                var c0 = Lin(new Color(_v[7], _v[8], _v[9], (wand ? 1f : 0.9f) * _v[0])); var c1 = wand ? Lin(new Color(1f, 0.9f, 0.55f, _v[0])) : Lin(new Color(_v[7], _v[8], _v[9], 0.25f * _v[0]));
                int i = Bv.Count; var uv = Uv(0); float w0 = wand ? 0.0075f : 0.0030f, w1 = wand ? 0.0045f : 0.0018f;
                Bv.Add(a - side * w0); Bv.Add(a + side * w0); Bv.Add(b + side * w1); Bv.Add(b - side * w1);
                Bc.Add(c0); Bc.Add(c0); Bc.Add(c1); Bc.Add(c1);
                for (int k = 0; k < 4; k++) Bu.Add(new Vector2(uv.x, uv.y));
                Bt.Add(i); Bt.Add(i + 1); Bt.Add(i + 2); Bt.Add(i); Bt.Add(i + 2); Bt.Add(i + 3);
            }
            _lineMesh.Clear(); _lineMesh.SetVertices(Bv); _lineMesh.SetColors(Bc); _lineMesh.SetUVs(0, Bu); _lineMesh.SetTriangles(Bt, 0); _lineMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ the picture
        private readonly List<Transform> _turned = new List<Transform>(); private readonly List<Quaternion> _turnedBack = new List<Quaternion>();

        private void Render()
        {
            if (_cam == null || Texture == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!Texture.IsCreated()) Texture.Create();
            float k = Mathf.Abs(_rig.lossyScale.x);
            _cam.nearClipPlane = Mathf.Max(1e-4f, 0.03f * k); _cam.farClipPlane = Mathf.Max(20f * k, 1500f * k);
            HideReal();
            // the sprites face the head camera (Sprites/ActorSprite.cs LateUpdate): for this picture they face the clip's eye, then back
            _turned.Clear(); _turnedBack.Clear();
            var units = Units?.Invoke();
            if (units != null) foreach (var u in units) Turn(u);
            Turn(_self); Turn(_poring); Turn(_other);
            // client wave 22 (several clips at once: the casting ways' columns): every stage is parked on StageLayer, which no camera
            // draws; THIS stage's things come to PictureLayer for this one picture and go back right after it, so a picture never shows
            // another clip's hands, panels or actors
            _mine.Clear();
            Mine(_rig); Mine(_cursor); Mine(_lookRing); Mine(_pulse); if (_self != null) Mine(_self.transform); if (_poring != null) Mine(_poring.transform); if (_other != null) Mine(_other.transform);
            DrawnLast = _mine.Count;
            try { _cam.Render(); }
            finally
            {
                for (int i = 0; i < _mine.Count; i++) if (_mine[i] != null) _mine[i].layer = StageLayer;
                _mine.Clear();
                for (int i = 0; i < _turned.Count; i++) if (_turned[i] != null) _turned[i].rotation = _turnedBack[i];
                ShowReal();
            }
            Renders++;
            LastRenderMs = sw.Elapsed.TotalMilliseconds; _renderSum += LastRenderMs; _renderCount++; if (LastRenderMs > MaxRenderMs) MaxRenderMs = LastRenderMs;
        }

        /// <summary>The layer this stage's things are on for its own picture only (the world's: every camera draws it, and none but this
        /// one draws while they are there).</summary>
        public const int PictureLayer = 0;
        private readonly List<GameObject> _mine = new List<GameObject>();
        /// <summary>How many of this stage's things were brought into its last picture (smoke).</summary>
        public int DrawnLast { get; private set; }
        private void Mine(Transform t)
        {
            if (t == null) return; var go = t.gameObject; if (!go.activeSelf) return;
            if (go.layer == StageLayer) { go.layer = PictureLayer; _mine.Add(go); }
            for (int i = 0, c = t.childCount; i < c; i++) Mine(t.GetChild(i));
        }

        private void Turn(Unit u)
        {
            if (u == null || u.Sprite == null || !u.gameObject.activeInHierarchy) return;
            var t = u.Sprite.transform; var to = t.position - _cam.transform.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-10f) return;
            _turned.Add(t); _turnedBack.Add(t.rotation); t.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        /// <summary>For ONE picture of the clip only: what hangs on the player's real rig (hands, lasers, the wand …; the panels have their own
        /// layer already), the real character, the real practice Poring, the real cell cursor and the real look's rings on the ground go on
        /// <see cref="HiddenLayer"/>, which only this camera leaves out; <see cref="ShowReal"/> puts each back on its own layer right after
        /// the picture, so no other camera (the head, a screenshot, the desktop spectator) ever sees a change.</summary>
        private void HideReal()
        {
            _hid.Clear(); _hidWas.Clear(); _hidIds.Clear();
            var rt = _real.transform;
            for (int i = 0, c = rt.childCount; i < c; i++) Hide(rt.GetChild(i));
            var s = Self?.Invoke(); if (s != null) Hide(s.transform);
            var p = RealPoring?.Invoke(); if (p != null) Hide(p.transform);
            var o = RealOther?.Invoke(); if (o != null) Hide(o.transform); // (wave 28, wcodex)
            if (_cursorReal == null) { var go = GameObject.Find("CellCursor"); if (go != null) _cursorReal = go.transform; }
            if (_cursorReal != null) Hide(_cursorReal);
            var map = Map?.Invoke();
            if (map != null) foreach (string ring in RealRings) { var t = map.Find(ring); if (t != null) Hide(t); }
        }
        private static readonly string[] RealRings = { "gaze-lock", "gaze-aim" }; // (Game/Targeting.cs: under what the player's own look has chosen now)
        private readonly List<GameObject> _hid = new List<GameObject>(); private readonly List<int> _hidWas = new List<int>(); private readonly HashSet<int> _hidIds = new HashSet<int>();
        /// <summary>The objects that were out of the clip's last picture (their instance ids; smoke).</summary>
        public bool WasHidden(GameObject go) => go != null && _hidIds.Contains(go.GetInstanceID());
        public int HiddenLast => _hidIds.Count;
        private void Hide(Transform t)
        {
            var go = t.gameObject; int l = go.layer;
            if (l == UiKit.PanelLayer || l == StageLayer || l == LookSelect.LaserOnlyLayer) return; // a panel and all of it (never in this camera's view); the stage; a collider only the laser meets
            if (l != HiddenLayer) { _hid.Add(go); _hidWas.Add(l); go.layer = HiddenLayer; }
            _hidIds.Add(go.GetInstanceID());
            for (int i = 0, c = t.childCount; i < c; i++) { var ch = t.GetChild(i); if (ch.gameObject.activeSelf) Hide(ch); }
        }
        private void ShowReal()
        {
            for (int i = 0; i < _hid.Count; i++) if (_hid[i] != null) _hid[i].layer = _hidWas[i];
            _hid.Clear(); _hidWas.Clear();
        }

        private void OnDestroy()
        {
            if (Clip != null) { Clip = null; PlayingCount = Mathf.Max(0, PlayingCount - 1); }
            Clear();
            if (_rig != null) Destroy(_rig.gameObject);
            foreach (var t in new[] { _cursor, _lookRing, _pulse }) if (t != null) Destroy(t.gameObject);
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
    }

    /// <summary>
    /// Client wave 22 (w22clips), point 59 (design note: "when you're showing the video clip of what to do, also add a ghostly 3D projection of the
    /// player's current controller ... and have it simulate what you're meant to do"): the GHOST beside the clip, on the tutorial's guide. It
    /// reads the same <see cref="ClipTimeline"/> at the clip's own time (<see cref="Show"/>), so it is always in step with the picture.
    ///   What     the controller(s) of the hand(s) the step uses (ClipTimeline.UsesLeft / UsesRight; a left-handed player's clip is
    ///            mirrored, so it is their own hand), as the player's own profile's model (UI/ControllerGhost.cs); a step done with the
    ///            look alone shows a ghost HEADSET that turns as the head does.
    ///   Moves    the buttons, the stick, the trigger, the grip and the finger from the clip's inputs; the WRIST from the recorded hand:
    ///            its turn as the body sees it (the recorded head's heading at the clip's start is "ahead") and its QUICK travel (a flick,
    ///            a shake, a push: its place less its own average over <see cref="AverageSeconds"/> s before and after, so the long reach
    ///            to where a gesture is made does not shrink the model), all scaled by ONE factor so the model and its travel fit its
    ///            place (<see cref="FitScale"/>; never larger than life).
    ///   Where    in the guide's clip band, right of the picture, floating <see cref="FloatM"/> m in front of the panel; one controller in
    ///            the middle of the plate, two side by side; seen a little from the side (<see cref="PresentYaw"/>,
    ///            <see cref="PresentPitch"/>) so the trigger is not hidden behind the handle.
    /// </summary>
    public sealed class ClipGhostStand : MonoBehaviour
    {
        public const float FloatM = 0.05f, PresentYaw = 28f, PresentPitch = -30f;
        /// <summary>The hand's travel shown is its place less its own average over this long before and after (so a long reach to where a
        /// gesture is made does not shrink the model: the flick, the shake, the push stay); sampled this many times a second.</summary>
        public const float AverageSeconds = 0.5f, SampleHz = 30f;
        /// <summary>The model never fills more than this share of its place; a headset's size across (metres).</summary>
        public const float Fill = 0.86f, HeadsetAcross = 0.27f;
        public const int Order = TutorialGuide.SortingOrder + 1; // (right after the guide's canvas: UI/TutorialGuide.cs; client wave 23, w23place: with it under the panels' 10, so a casting tool or a window is drawn over the ghost too; it was 19)
        /// <summary>Client wave 23 (w23place): the ghost's opacity (1: as always): it fades with the guide while a casting tool's box meets
        /// the guide's (LokiGame.W23Place.cs). Every part keeps its own tint (lit or not), its alpha times this; called again every
        /// frame while it is under 1, because a part that lights up is tinted anew by UI/ControllerGhost.cs.</summary>
        public float Fade { get; private set; } = 1f;
        private MaterialPropertyBlock _fadeMpb;
        public void SetFade(float k)
        {
            Fade = Mathf.Clamp01(k);
            if (_fadeMpb == null) _fadeMpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetPropertyBlock(_fadeMpb);
                var c = _fadeMpb.HasColor("_Color") ? _fadeMpb.GetColor("_Color") : ControllerGhost.Base;
                bool lit = Mathf.Abs(c.r - ControllerGhost.Lit.r) < 0.05f && Mathf.Abs(c.b - ControllerGhost.Lit.b) < 0.05f;
                var want = lit ? ControllerGhost.Lit : ControllerGhost.Base; want.a *= Fade;
                _fadeMpb.SetColor("_Color", want); r.SetPropertyBlock(_fadeMpb);
            }
        }
        public ControllerGhost Left { get; private set; }
        public ControllerGhost Right { get; private set; }
        public Transform Headset { get; private set; }
        public float FitScale { get; private set; } = 1f;
        public Profile Profile { get; private set; }
        public ClipTimeline Clip { get; private set; }
        /// <summary>How far the shown controllers are from their places at the clip's start now: degrees turned, metres moved (of the hand,
        /// before the fit's scale): the smoke reads the wrist's motion here.</summary>
        public float TurnedDeg { get; private set; }
        public float MovedM { get; private set; }
        private Transform _root; private Quaternion _body = Quaternion.identity, _left0, _right0, _head0; private Vector3 _leftP0, _rightP0, _slotL, _slotR;
        private Vector3[] _travelL, _travelR; private Vector2 _boxM;

        /// <param name="parent">The guide's clip band (its units are the panel's pixels).</param>
        /// <param name="centrePx">The middle of the ghost's place in the band, pixels.</param>
        /// <param name="sizePx">Its width and height, pixels.</param>
        public static ClipGhostStand Create(RectTransform parent, Vector2 centrePx, Vector2 sizePx, float metresPerPx)
        {
            var go = new GameObject("ClipGhostStand"); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var s = go.AddComponent<ClipGhostStand>();
            s._boxM = sizePx * metresPerPx;
            go.transform.localPosition = new Vector3(centrePx.x, centrePx.y, -FloatM / metresPerPx); // (the panel's −z is towards the eyes)
            go.transform.localScale = Vector3.one / metresPerPx; // metres inside
            var root = new GameObject("turn"); root.layer = go.layer; root.transform.SetParent(go.transform, false); s._root = root.transform;
            return s;
        }

        public void Clear()
        {
            if (Left != null) Destroy(Left.gameObject); if (Right != null) Destroy(Right.gameObject); if (Headset != null) Destroy(Headset.gameObject);
            Left = Right = null; Headset = null; Clip = null;
        }

        /// <summary>A hand's quick travel in the body's frame, <see cref="SampleHz"/> times a second: its place less its own average over
        /// <see cref="AverageSeconds"/> before and after.</summary>
        private Vector3[] Travel(ClipTimeline clip, ClipTimeline.Track tr, Quaternion inv)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(clip.Length * SampleHz) + 1), w = Mathf.Max(1, Mathf.RoundToInt(AverageSeconds * SampleHz));
            var p = new Vector3[n]; var r = new Vector3[n];
            for (int i = 0; i < n; i++) { clip.PoseAt(tr, i / SampleHz, out var pos, out _); p[i] = inv * pos; }
            for (int i = 0; i < n; i++)
            {
                var sum = Vector3.zero; int m = 0;
                for (int k = Mathf.Max(0, i - w); k <= Mathf.Min(n - 1, i + w); k++) { sum += p[k]; m++; }
                r[i] = p[i] - sum / m;
            }
            return r;
        }
        private static Vector3 At(Vector3[] r, float t)
        {
            if (r == null || r.Length == 0) return Vector3.zero;
            float f = Mathf.Clamp(t * SampleHz, 0f, r.Length - 1); int i = Mathf.Min(r.Length - 2, Mathf.FloorToInt(f)); if (i < 0) return r[0];
            return Vector3.Lerp(r[i], r[i + 1], f - i);
        }

        public void Show(ClipTimeline clip, Profile profile)
        {
            Clear(); if (clip == null) return;
            Clip = clip; Profile = profile; int layer = gameObject.layer;
            // "ahead" for the ghost: the recorded head's heading at the clip's start
            _body = Quaternion.identity;
            if (clip.PoseAt(clip.Head, 0f, out _, out var hq)) { var f = hq * Vector3.forward; f.y = 0f; if (f.sqrMagnitude > 1e-4f) _body = Quaternion.LookRotation(f.normalized, Vector3.up); _head0 = hq; }
            var inv = Quaternion.Inverse(_body);
            if (clip.UsesLeft) Left = ControllerGhost.Create(_root, profile, true, layer, Order, false);
            if (clip.UsesRight) Right = ControllerGhost.Create(_root, profile, false, layer, Order, false);
            clip.PoseAt(clip.HandL, 0f, out _leftP0, out _left0); clip.PoseAt(clip.HandR, 0f, out _rightP0, out _right0);
            _travelL = Left != null ? Travel(clip, clip.HandL, inv) : null; _travelR = Right != null ? Travel(clip, clip.HandR, inv) : null;
            if (Left == null && Right == null)
            {
                // a step done with the look alone: the headset, seen from behind and a little above
                Headset = ControllerGhost.CreateHeadset(_root, layer, Order).transform;
                _root.localRotation = Quaternion.Euler(-18f, 0f, 0f);
                FitScale = Mathf.Min(1f, Fill * Mathf.Min(_boxM.x, _boxM.y) / HeadsetAcross);
            }
            else
            {
                // each controller in its own place (side by side when the step uses both); ONE scale: the model and its quick travel fit the place
                bool two = Left != null && Right != null; float placeW = two ? _boxM.x * 0.5f : _boxM.x;
                _slotL = two ? new Vector3(-_boxM.x * 0.25f, 0f, 0f) : Vector3.zero; _slotR = two ? new Vector3(_boxM.x * 0.25f, 0f, 0f) : Vector3.zero;
                _root.localRotation = Quaternion.Euler(PresentPitch, PresentYaw, 0f);
                float across = 0f, ex = 0f, ey = 0f;
                foreach (var g in new[] { Left, Right }) if (g != null) across = Mathf.Max(across, g.LocalBounds.size.magnitude * 0.78f);
                foreach (var r in new[] { _travelL, _travelR }) if (r != null) foreach (var v in r) { ex = Mathf.Max(ex, Mathf.Abs(v.x)); ey = Mathf.Max(ey, Mathf.Abs(v.y)); }
                FitScale = Mathf.Min(1f, Fill * placeW / (across + 2f * ex), Fill * _boxM.y / (across + 2f * ey));
            }
            _root.localScale = Vector3.one * FitScale;
            At(0f);
        }

        /// <summary>The ghost at the clip's time <paramref name="t"/>.</summary>
        public void At(float t)
        {
            var clip = Clip; if (clip == null) return;
            var inv = Quaternion.Inverse(_body); bool mainLeft = clip.IsMirrored; float turned = 0f, moved = 0f, k = Mathf.Max(0.01f, FitScale);
            var rootInv = Quaternion.Inverse(_root.localRotation);
            if (Left != null && clip.PoseAt(clip.HandL, t, out var lp, out var lq))
            {
                var rot = inv * lq; Left.transform.localRotation = rot; Left.transform.localPosition = rootInv * (_slotL / k) + At(_travelL, t) - rot * Left.LocalBounds.center;
                Left.Apply(clip.InputAt(true, t), mainLeft);
                turned = Mathf.Max(turned, Quaternion.Angle(lq, _left0)); moved = Mathf.Max(moved, (lp - _leftP0).magnitude);
            }
            if (Right != null && clip.PoseAt(clip.HandR, t, out var rp, out var rq))
            {
                var rot = inv * rq; Right.transform.localRotation = rot; Right.transform.localPosition = rootInv * (_slotR / k) + At(_travelR, t) - rot * Right.LocalBounds.center;
                Right.Apply(clip.InputAt(false, t), !mainLeft);
                turned = Mathf.Max(turned, Quaternion.Angle(rq, _right0)); moved = Mathf.Max(moved, (rp - _rightP0).magnitude);
            }
            if (Headset != null && clip.PoseAt(clip.Head, t, out _, out var hq)) { Headset.localRotation = inv * hq; turned = Quaternion.Angle(hq, _head0); }
            TurnedDeg = turned; MovedM = moved;
        }
    }

    /// <summary>
    /// A 3D floating controller/headset ghost instructor that floats directly in front of the player in world space
    /// (instead of being confined to the flat tutorial guide panel) and demonstrates the current tutorial step and phase.
    /// Placed on <see cref="TutorialClip.HiddenLayer"/> so the head camera and desktop spectator draw it in 3D while the
    /// 2D clip render-texture camera ignores it.
    /// </summary>
    public sealed class FloatingGhostStand : MonoBehaviour
    {
        public enum DemoKind
        {
            None,
            TriggerTap,
            LaserSweep,
            LaserWalk,
            HeadCalibrate,
            HeadLook,
            HeadLookUp,
            WalkStick,
            TurnStick,
            ViewHoldStick,
            MapPanGripStick,
            MapScaleTwoGrips,
            ShakeHand,
            CarryGrip,
            GrabAndShake,
            GrabAndTiltWrist,
            MenuButtonTap,
            RingStickSelect,
            RingWristTwist,
            HoverSteady,
            CastPadOpenTouch,
            CastPadPalmFlip,
            CastPadSlap,
            CastFlickTwoStep,
            WandTakeFromHip,
            WandWheelSweep,
            WandReturnToHip,
            ElbowTap,
            ForearmLaser,
            SkillWheelsHoldAndPick
        }

        public const float DistanceM = 0.52f, DownM = 0.19f, MenuUpM = 0.22f, Scale = 0.92f;
        public const int Order = TutorialGuide.SortingOrder + 1;

        public ControllerGhost Left { get; private set; }
        public ControllerGhost Right { get; private set; }
        public Transform Headset { get; private set; }
        public DemoKind CurrentDemo { get; private set; }
        public Profile Profile { get; private set; } = (Profile)(-1);
        public bool LeftHanded { get; private set; }

        private Transform _root;
        private Func<Camera> _head;
        private Vector3 _lastHeadPos;
        private float _yaw;
        private bool _hasAnchor;
        private float _time;

        public static FloatingGhostStand Create(Transform parent, Func<Camera> head)
        {
            var go = new GameObject("FloatingGhostStand");
            go.layer = TutorialClip.HiddenLayer;
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<FloatingGhostStand>();
            s._head = head;
            var root = new GameObject("turn");
            root.layer = go.layer;
            root.transform.SetParent(go.transform, false);
            s._root = root.transform;
            s._root.localScale = Vector3.one * Scale;
            return s;
        }

        public void Clear()
        {
            if (Left != null) Destroy(Left.gameObject);
            if (Right != null) Destroy(Right.gameObject);
            if (Headset != null) Destroy(Headset.gameObject);
            Left = Right = null;
            Headset = null;
            CurrentDemo = DemoKind.None;
            _hasAnchor = false;
        }

        private static bool NeedsHands(DemoKind d, bool leftHanded, out bool useL, out bool useR, out bool useHead)
        {
            useL = false; useR = false; useHead = false;
            if (d == DemoKind.None) return false;
            if (d == DemoKind.HeadCalibrate || d == DemoKind.HeadLook || d == DemoKind.HeadLookUp)
            {
                useHead = true;
                return true;
            }
            if (d == DemoKind.MapScaleTwoGrips || d == DemoKind.ElbowTap || d == DemoKind.ForearmLaser || d == DemoKind.SkillWheelsHoldAndPick)
            {
                useL = true; useR = true;
                return true;
            }
            bool offHand = d == DemoKind.WalkStick || d == DemoKind.ViewHoldStick || d == DemoKind.MapPanGripStick
                || d == DemoKind.MenuButtonTap || d == DemoKind.RingStickSelect || d == DemoKind.RingWristTwist;
            bool left = offHand ? !leftHanded : leftHanded;
            useL = left;
            useR = !left;
            return true;
        }

        public void Step(DemoKind demo, Profile profile, bool leftHanded, bool atMenu, float sideOffsetDeg, float dt)
        {
            if (demo == DemoKind.None)
            {
                if (CurrentDemo != DemoKind.None || Left != null || Right != null || Headset != null) Clear();
                return;
            }
            var cam = _head != null ? _head() : null;
            if (cam == null) return;

            NeedsHands(demo, leftHanded, out bool wantL, out bool wantR, out bool wantH);
            if (demo != CurrentDemo || profile != Profile || leftHanded != LeftHanded
                || (Left != null) != wantL || (Right != null) != wantR || (Headset != null) != wantH)
            {
                Clear();
                CurrentDemo = demo;
                Profile = profile;
                LeftHanded = leftHanded;
                _time = 0f;
                int layer = TutorialClip.HiddenLayer;
                if (wantL)
                {
                    Left = ControllerGhost.Create(_root, profile, true, layer, Order, true);
                    Left.gameObject.name = "FloatingGhostLeft";
                }
                if (wantR)
                {
                    Right = ControllerGhost.Create(_root, profile, false, layer, Order, true);
                    Right.gameObject.name = "FloatingGhostRight";
                }
                if (wantH)
                {
                    Headset = ControllerGhost.CreateHeadset(_root, layer, Order).transform;
                    Headset.gameObject.name = "FloatingGhostHeadset";
                }
            }

            _time += dt;
            PlaceInFrontOfPlayer(cam.transform, atMenu, sideOffsetDeg, dt);
            Animate(demo, leftHanded, _time);
        }

        private void PlaceInFrontOfPlayer(Transform head, bool atMenu, float sideOffsetDeg, float dt)
        {
            float headYaw = VR.WandHolster.HeadYaw(head);
            if (!_hasAnchor)
            {
                _yaw = headYaw;
                _lastHeadPos = head.position;
                _hasAnchor = true;
            }
            else
            {
                var delta = head.position - _lastHeadPos;
                if (delta.sqrMagnitude > 1e-8f) transform.position += delta;
                _lastHeadPos = head.position;
                float diff = Mathf.DeltaAngle(_yaw, headYaw);
                if (Mathf.Abs(diff) > 6f)
                    _yaw = Mathf.LerpAngle(_yaw, headYaw, 1f - Mathf.Exp(-8f * dt));
            }

            float yaw = _yaw + sideOffsetDeg;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float yOff = atMenu ? MenuUpM : -DownM;
            var targetPos = head.position + fwd * DistanceM + Vector3.up * yOff;
            transform.position = _hasAnchor ? Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-12f * dt)) : targetPos;
            var toEye = head.position - transform.position;
            toEye.y = 0f;
            if (toEye.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(-toEye.normalized, Vector3.up);
        }

        private void Animate(DemoKind d, bool leftHanded, float t)
        {
            float sign = leftHanded ? -1f : 1f;
            var main = leftHanded ? Left : Right;
            var off = leftHanded ? Right : Left;

            // Default presentation pose facing the player so triggers, sticks, and face buttons are clearly seen
            Quaternion BaseRot(bool isLeft, float pitch = -28f, float yaw = 24f, float roll = 0f)
                => Quaternion.Euler(pitch, isLeft ? -yaw : yaw, isLeft ? -roll : roll);

            if (Headset != null)
            {
                switch (d)
                {
                    case DemoKind.HeadCalibrate:
                    {
                        // Turn toward a dot, slow down smoothly, and hold completely still
                        float cyc = t % 2.4f;
                        float yaw = cyc < 0.7f ? Mathf.SmoothStep(-18f, 14f, cyc / 0.7f) : 14f;
                        float pitch = cyc < 0.7f ? Mathf.SmoothStep(6f, -10f, cyc / 0.7f) : -10f;
                        Headset.localPosition = Vector3.zero;
                        Headset.localRotation = Quaternion.Euler(pitch - 14f, yaw, 0f);
                        break;
                    }
                    case DemoKind.HeadLook:
                    {
                        float cyc = t % 2.2f;
                        float yaw = cyc < 0.6f ? Mathf.SmoothStep(-16f, 0f, cyc / 0.6f) : 0f;
                        float pitch = cyc < 0.6f ? Mathf.SmoothStep(-4f, 18f, cyc / 0.6f) : 18f;
                        Headset.localPosition = Vector3.zero;
                        Headset.localRotation = Quaternion.Euler(pitch - 12f, yaw, 0f);
                        break;
                    }
                    case DemoKind.HeadLookUp:
                    {
                        float cyc = t % 2.2f;
                        float k = cyc < 0.8f ? Mathf.SmoothStep(0f, 1f, cyc / 0.8f) : cyc < 1.7f ? 1f : Mathf.SmoothStep(1f, 0f, (cyc - 1.7f) / 0.5f);
                        Headset.localPosition = new Vector3(0f, k * 0.04f, 0f);
                        Headset.localRotation = Quaternion.Euler(Mathf.Lerp(-12f, -52f, k), 0f, 0f);
                        break;
                    }
                }
                return;
            }

            switch (d)
            {
                case DemoKind.TriggerTap:
                {
                    if (main == null) break;
                    float cyc = t % 1.6f;
                    float trig = cyc >= 0.35f && cyc <= 1.05f ? Mathf.Sin((cyc - 0.35f) / 0.70f * Mathf.PI) : 0f;
                    var rot = BaseRot(main.Left, -26f + trig * 5f, 28f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(0f, 0f, trig * 0.018f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Trigger = trig, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.LaserSweep:
                {
                    if (main == null) break;
                    float wave = Mathf.Sin(t * 2.2f);
                    var rot = BaseRot(main.Left, -16f, 18f + wave * 18f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(wave * 0.05f, 0f, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.LaserWalk:
                {
                    if (main == null) break;
                    float cyc = t % 1.8f;
                    float aim = cyc < 0.5f ? Mathf.SmoothStep(0f, 1f, cyc / 0.5f) : cyc < 1.3f ? 1f : Mathf.SmoothStep(1f, 0f, (cyc - 1.3f) / 0.5f);
                    float trig = cyc >= 0.55f && cyc <= 1.15f ? 1f : 0f;
                    var rot = BaseRot(main.Left, Mathf.Lerp(-18f, 16f, aim), 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(0f, -aim * 0.025f, trig * 0.015f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Trigger = trig, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.WalkStick:
                {
                    if (off == null) break;
                    float cyc = t % 1.8f;
                    float push = cyc >= 0.3f && cyc <= 1.3f ? 1f : 0f;
                    var rot = BaseRot(off.Left, -42f, 20f);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = -(rot * off.LocalBounds.center);
                    off.Apply(new ClipTimeline.Input { Stick = new Vector2(0f, push), Flags = ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.TurnStick:
                {
                    if (main == null) break;
                    float cyc = t % 2.0f;
                    float sx = cyc < 0.8f ? -1f : cyc >= 1.0f && cyc < 1.8f ? 1f : 0f;
                    var rot = BaseRot(main.Left, -42f, 20f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = -(rot * main.LocalBounds.center);
                    main.Apply(new ClipTimeline.Input { Stick = new Vector2(sx, 0f), Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.ViewHoldStick:
                {
                    if (off == null) break;
                    float cyc = t % 2.0f;
                    bool hold = cyc >= 0.3f && cyc <= 1.45f;
                    var rot = BaseRot(off.Left, -44f, 22f);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = new Vector3(0f, hold ? -0.008f : 0f, 0f) - rot * off.LocalBounds.center;
                    off.Apply(new ClipTimeline.Input { Flags = (hold ? ClipTimeline.StickClick : 0) | ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.MapPanGripStick:
                {
                    if (off == null) break;
                    float cyc = t % 2.0f;
                    bool act = cyc >= 0.25f && cyc <= 1.55f;
                    float slide = act ? Mathf.Sin((cyc - 0.25f) / 1.3f * Mathf.PI * 2f) : 0f;
                    var rot = BaseRot(off.Left, -34f, 24f);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = new Vector3(slide * 0.08f, 0f, 0f) - rot * off.LocalBounds.center;
                    off.Apply(new ClipTimeline.Input { Grip = act ? 1f : 0f, Stick = act ? new Vector2(slide, 0.6f) : Vector2.zero, Flags = ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.MapScaleTwoGrips:
                {
                    float cyc = t % 2.2f;
                    bool grip = cyc >= 0.25f && cyc <= 1.75f;
                    float spread = grip ? Mathf.Sin((cyc - 0.25f) / 1.5f * Mathf.PI) * 0.11f : 0f;
                    if (Left != null)
                    {
                        var rotL = BaseRot(true, -28f, 22f);
                        Left.transform.localRotation = rotL;
                        Left.transform.localPosition = new Vector3(-0.09f - spread, 0f, 0f) - rotL * Left.LocalBounds.center;
                        Left.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, leftHanded);
                    }
                    if (Right != null)
                    {
                        var rotR = BaseRot(false, -28f, 22f);
                        Right.transform.localRotation = rotR;
                        Right.transform.localPosition = new Vector3(0.09f + spread, 0f, 0f) - rotR * Right.LocalBounds.center;
                        Right.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, !leftHanded);
                    }
                    break;
                }
                case DemoKind.ShakeHand:
                {
                    if (main == null) break;
                    float cyc = t % 1.6f;
                    float shake = cyc < 1.1f ? Mathf.Sin(cyc * Mathf.PI * 6f) : 0f;
                    var rot = BaseRot(main.Left, -26f, 20f, shake * 22f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(shake * 0.06f, 0f, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.CarryGrip:
                {
                    if (main == null) break;
                    float cyc = t % 2.2f;
                    bool grip = cyc >= 0.3f && cyc <= 1.6f;
                    float move = cyc < 0.3f ? 0f : cyc <= 1.6f ? Mathf.SmoothStep(0f, 1f, (cyc - 0.3f) / 1.3f) : Mathf.SmoothStep(1f, 0f, (cyc - 1.6f) / 0.6f);
                    var rot = BaseRot(main.Left, -26f, 26f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3((move - 0.5f) * 0.14f * sign, Mathf.Sin(move * Mathf.PI) * 0.03f, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.GrabAndShake:
                {
                    if (main == null) break;
                    float cyc = t % 1.8f;
                    bool grip = cyc >= 0.2f && cyc <= 1.5f;
                    float shake = cyc >= 0.4f && cyc <= 1.4f ? Mathf.Sin((cyc - 0.4f) * Mathf.PI * 6f) : 0f;
                    var rot = BaseRot(main.Left, -26f, 22f, shake * 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(shake * 0.055f, 0f, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.GrabAndTiltWrist:
                {
                    if (main == null) break;
                    float cyc = t % 2.0f;
                    float tilt = Mathf.Sin(cyc / 2.0f * Mathf.PI * 2f) * 34f;
                    var rot = BaseRot(main.Left, -26f + tilt * 0.5f, 22f, tilt);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = -(rot * main.LocalBounds.center);
                    main.Apply(new ClipTimeline.Input { Grip = 1f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.MenuButtonTap:
                {
                    if (off == null) break;
                    float cyc = t % 1.6f;
                    bool press = cyc >= 0.35f && cyc <= 0.95f;
                    var rot = BaseRot(off.Left, -44f, 24f);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = new Vector3(0f, press ? -0.006f : 0f, 0f) - rot * off.LocalBounds.center;
                    off.Apply(new ClipTimeline.Input { Flags = (press ? ClipTimeline.Secondary | ClipTimeline.Menu : 0) | ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.RingStickSelect:
                {
                    if (off == null) break;
                    float cyc = t % 1.8f;
                    float push = cyc >= 0.25f && cyc <= 1.15f ? 1f : 0f;
                    var rot = BaseRot(off.Left, -42f, 22f);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = -(rot * off.LocalBounds.center);
                    off.Apply(new ClipTimeline.Input { Stick = new Vector2(0f, push), Flags = ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.RingWristTwist:
                {
                    if (off == null) break;
                    float twist = Mathf.Sin(t * 2.6f) * 36f;
                    var rot = BaseRot(off.Left, -30f, 22f, twist);
                    off.transform.localRotation = rot;
                    off.transform.localPosition = -(rot * off.LocalBounds.center);
                    off.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, false);
                    break;
                }
                case DemoKind.HoverSteady:
                {
                    if (main == null) break;
                    var rot = BaseRot(main.Left, -22f, 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = -(rot * main.LocalBounds.center);
                    main.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.CastPadOpenTouch:
                {
                    if (main == null) break;
                    float cyc = t % 2.2f;
                    bool grip = cyc >= 0.25f && cyc <= 1.65f;
                    float reach = cyc >= 0.6f && cyc <= 1.4f ? Mathf.Sin((cyc - 0.6f) / 0.8f * Mathf.PI) : 0f;
                    var rot = BaseRot(main.Left, -24f, 28f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(0f, reach * 0.02f, reach * 0.07f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = 0 }, true); // Flags = 0: index finger extended!
                    break;
                }
                case DemoKind.CastPadPalmFlip:
                {
                    if (main == null) break;
                    float cyc = t % 2.6f;
                    float flip = cyc < 0.7f ? Mathf.SmoothStep(0f, 155f, cyc / 0.7f) : cyc < 1.9f ? 155f : Mathf.SmoothStep(155f, 0f, (cyc - 1.9f) / 0.7f);
                    bool grip = cyc >= 0.75f && cyc <= 1.75f;
                    var rot = BaseRot(main.Left, -22f, 24f, flip);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = -(rot * main.LocalBounds.center);
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = 0 }, true);
                    break;
                }
                case DemoKind.CastPadSlap:
                {
                    if (main == null) break;
                    float cyc = t % 2.0f;
                    bool grip = cyc >= 0.2f && cyc <= 1.5f;
                    float slap = cyc >= 0.6f && cyc <= 1.2f ? Mathf.Sin((cyc - 0.6f) / 0.6f * Mathf.PI) : 0f;
                    var rot = BaseRot(main.Left, -20f + slap * 28f, 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(0f, -slap * 0.08f, slap * 0.04f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = 0 }, true);
                    break;
                }
                case DemoKind.CastFlickTwoStep:
                {
                    if (main == null) break;
                    float cyc = t % 2.4f;
                    bool grip = cyc >= 0.2f && cyc <= 1.85f;
                    // Flick 1: right/outward (row); Flick 2: upward (slot)
                    float fx = cyc >= 0.45f && cyc < 1.05f ? Mathf.Sin((cyc - 0.45f) / 0.6f * Mathf.PI) * 0.07f * sign : 0f;
                    float fy = cyc >= 1.10f && cyc < 1.70f ? Mathf.Sin((cyc - 1.10f) / 0.6f * Mathf.PI) * 0.07f : 0f;
                    var rot = BaseRot(main.Left, -24f - fy * 260f, 24f + fx * 260f * sign);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(fx, fy, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = 0 }, true);
                    break;
                }
                case DemoKind.WandTakeFromHip:
                {
                    if (main == null) break;
                    float cyc = t % 2.2f;
                    float lift = cyc < 0.5f ? 0f : cyc < 1.4f ? Mathf.SmoothStep(0f, 1f, (cyc - 0.5f) / 0.9f) : 1f;
                    bool grip = cyc >= 0.35f;
                    var rot = BaseRot(main.Left, Mathf.Lerp(15f, -26f, lift), 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(Mathf.Lerp(0.11f * sign, 0f, lift), Mathf.Lerp(-0.14f, 0.02f, lift), 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.WandWheelSweep:
                {
                    if (main == null) break;
                    float cyc = t % 2.2f;
                    bool trig = cyc >= 0.25f && cyc <= 1.65f;
                    float sweep = trig ? Mathf.Sin((cyc - 0.25f) / 1.4f * Mathf.PI) : 0f;
                    var rot = BaseRot(main.Left, -26f - sweep * 18f, 22f + sweep * 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(sweep * 0.06f * sign, sweep * 0.04f, 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = 1f, Trigger = trig ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.WandReturnToHip:
                {
                    if (main == null) break;
                    float cyc = t % 2.2f;
                    float drop = cyc < 0.9f ? Mathf.SmoothStep(0f, 1f, cyc / 0.9f) : 1f;
                    bool grip = cyc < 1.15f;
                    var rot = BaseRot(main.Left, Mathf.Lerp(-26f, 15f, drop), 24f);
                    main.transform.localRotation = rot;
                    main.transform.localPosition = new Vector3(Mathf.Lerp(0f, 0.11f * sign, drop), Mathf.Lerp(0.02f, -0.14f, drop), 0f) - rot * main.LocalBounds.center;
                    main.Apply(new ClipTimeline.Input { Grip = grip ? 1f : 0f, Flags = ClipTimeline.OnTrigger }, true);
                    break;
                }
                case DemoKind.ElbowTap:
                {
                    float cyc = t % 2.0f;
                    float tap = cyc >= 0.35f && cyc <= 1.35f ? Mathf.Sin((cyc - 0.35f) / 1.0f * Mathf.PI) : 0f;
                    if (off != null)
                    {
                        var rotO = BaseRot(off.Left, -18f, 35f);
                        off.transform.localRotation = rotO;
                        off.transform.localPosition = new Vector3(-0.09f * sign, -0.03f, 0f) - rotO * off.LocalBounds.center;
                        off.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, false);
                    }
                    if (main != null)
                    {
                        var rotM = BaseRot(main.Left, -22f, -18f);
                        main.transform.localRotation = rotM;
                        main.transform.localPosition = Vector3.Lerp(new Vector3(0.09f * sign, 0.02f, 0f), new Vector3(-0.06f * sign, -0.02f, 0.02f), tap) - rotM * main.LocalBounds.center;
                        main.Apply(new ClipTimeline.Input { Flags = 0 }, true);
                    }
                    break;
                }
                case DemoKind.ForearmLaser:
                {
                    float cyc = t % 1.8f;
                    float trig = cyc >= 0.45f && cyc <= 1.15f ? 1f : 0f;
                    if (off != null)
                    {
                        var rotO = BaseRot(off.Left, -15f, 42f);
                        off.transform.localRotation = rotO;
                        off.transform.localPosition = new Vector3(-0.08f * sign, -0.04f, 0f) - rotO * off.LocalBounds.center;
                        off.Apply(new ClipTimeline.Input { Flags = ClipTimeline.OnTrigger }, false);
                    }
                    if (main != null)
                    {
                        var rotM = BaseRot(main.Left, 12f, -24f);
                        main.transform.localRotation = rotM;
                        main.transform.localPosition = new Vector3(0.07f * sign, 0.04f, 0f) - rotM * main.LocalBounds.center;
                        main.Apply(new ClipTimeline.Input { Trigger = trig, Flags = ClipTimeline.OnTrigger }, true);
                    }
                    break;
                }
                case DemoKind.SkillWheelsHoldAndPick:
                {
                    float cyc = t % 2.4f;
                    bool holdOff = cyc >= 0.2f && cyc <= 1.9f;
                    float stickPush = cyc >= 0.55f && cyc <= 1.65f ? 1f : 0f;
                    float mainTrig = cyc >= 1.0f && cyc <= 1.55f ? 1f : 0f;
                    if (off != null)
                    {
                        var rotO = BaseRot(off.Left, -38f, 22f);
                        off.transform.localRotation = rotO;
                        off.transform.localPosition = new Vector3(-0.085f * sign, 0f, 0f) - rotO * off.LocalBounds.center;
                        off.Apply(new ClipTimeline.Input { Trigger = holdOff ? 1f : 0f, Stick = new Vector2(stickPush * 0.7f, stickPush * 0.7f), Flags = ClipTimeline.OnTrigger }, false);
                    }
                    if (main != null)
                    {
                        var rotM = BaseRot(main.Left, -28f, 22f);
                        main.transform.localRotation = rotM;
                        main.transform.localPosition = new Vector3(0.085f * sign, 0f, 0f) - rotM * main.LocalBounds.center;
                        main.Apply(new ClipTimeline.Input { Trigger = mainTrig, Flags = ClipTimeline.OnTrigger }, true);
                    }
                    break;
                }
            }
        }
    }
}
