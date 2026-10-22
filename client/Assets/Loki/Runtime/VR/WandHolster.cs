using System.Collections.Generic;
using UnityEngine;

namespace Loki.VR
{
    /// <summary>
    /// Client wave 12 (w12cast): the wand of the "wand" casting scheme and its holster on the player's hip (design note, 2026-10-06: "a
    /// variant to the flick method that places a magic wand on your hip (placement customizable)"). This is the object and where
    /// it hangs; who holds it and what its strokes do is LokiGame.W12Wand.cs.
    ///   The body  the holster hangs from a body anchor, not from the head's glance: the point under the head, and a heading
    ///             (<see cref="BodyYaw"/>) that starts as the head's and then stays where it is while the head looks around: it
    ///             only follows when the head is more than <see cref="GlanceLimit"/> degrees off it (a neck does not turn
    ///             further: the body has turned), or has been more than <see cref="FollowAngle"/> degrees off it for
    ///             <see cref="FollowDelay"/> s (the player turned their chair or their feet): then it comes round at
    ///             <see cref="FollowSpeed"/> degrees a second. An estimate from the head alone (no tracker on the body): a
    ///             player who leans moves the holster with their head.
    ///   The place the holster is the anchor + an offset in the body's heading (right, up, ahead; metres): by default on the
    ///             pointing hand's side at hip height for a seated or standing player (22 cm to the side, 60 cm under the eyes,
    ///             5 cm ahead); the player's own place is kept per player (VR/Comfort.cs WandOffset, loki-user.json).
    ///   The wand  a rod of <see cref="Length"/> m with a bright tip, made of Unity's own primitives; in the holster it hangs
    ///             tip down; in a hand it lies along the hand's pointing direction; let go, it flies back in
    ///             <see cref="ReturnSeconds"/> s. Its pointing line and the trail of a stroke are drawn from the tip.
    ///   Seen when hidden  client wave 13 (w13resize), the brief's second item (design note, 2026-10-07: "The wand should always be visible,
    ///             even through the ground, UI elements and etc. Because the player needs to be able to pick it up whenever"), as specified
    ///             changed it (~11:55: "the wand shouldn't be visible as in 'you see it exactly as is'. It should be visible in a way
    ///             that any part of it that is occluded shows up as a ghostly projection of it"): every part (rod, handle, tip) is
    ///             drawn twice. The NORMAL wand (Resources/Shaders/LokiWandSolid.shader) is depth-tested as wave 12 drew it, drawn
    ///             after the panels (sorting order <see cref="SolidOrder"/>; the panels 10) so a wand in front of a board is seen
    ///             whole, and left out where a panel lies between the eye and it. The GHOST (LokiWandGhost.shader, sorting order
    ///             <see cref="GhostOrder"/>, after the panels and the laser) is drawn only where the normal wand is hidden: behind
    ///             the world (the ground, units, buildings: ZTest Greater) or behind a panel (boards, windows, the classic wrist
    ///             menu, the pad: they write no depth, so each fragment asks whether one of the open panels' rectangles is between
    ///             the eye and it: PanelsToShaders): one pale blue tint (GhostColour), see-through, with a brighter edge (alpha
    ///             GhostColour.a at the silhouette, GhostCore of it facing the eye). In plain view only the normal wand is drawn;
    ///             partly covered: normal where seen, ghost where covered. The see-through wheels and the pictures with no background
    ///             (the wand's wheel, the flick's icons) are not panels here: the wand is drawn over them. Taking it never needed to
    ///             see it: a hand within <see cref="Radius"/> of the holster and a grip (LokiGame.W12Wand.cs UpdateW12Wand:
    ///             Vector3.Distance, no ray).
    ///   The cue   client wave 14 (w14cast), point 13 (design note: "require a visual indication that ... my hand is correctly positioned to grab
    ///             the wand"): while a grip NOW would take the wand (or put it back: answer 10) a ring of light stands round the holster,
    ///             facing the eyes, <see cref="CueRadius"/> m across its half, pulsing, drawn over everything (the laser's queue), and
    ///             the wand's wood and tip are drawn lit (<see cref="CueWood"/>, <see cref="CueTip"/>): <see cref="SetCue"/>.
    ///   The size  client wave 14 (w14cast), point 32 ("Add a resize option for the wand too"): the wand and the holster's marker are
    ///             drawn at VR/Comfort.cs WandScale (Settings → Controls "Wand size", Reposition's two grips): its length, its tip
    ///             and so where its strokes are measured from. The reach that takes it (<see cref="Radius"/>) stays the same.
    ///             Client wave 21 (w21wand), design note (2026-10-11): "the maximum size is its current default size, and the minimum size makes
    ///             it so that the tip of my finger is the casting tip much like the positioning of the flick casting method": the size
    ///             runs from VR/Comfort.cs WandMinScale to WandMaxScale (the default, <see cref="Length"/>). In a hand the tip lies
    ///             between that hand's FINGERTIP (the flick's own point: <see cref="Fingertip"/>, LokiGame.W12Cast.cs W12Tip) at the
    ///             smallest size and the full wand's tip at the largest, in a straight line (<see cref="HeldPose"/>): on a controller
    ///             that line is the wand's own, so the wand simply shortens; a tracked hand's index tip is off it, so the wand leans to
    ///             it as it shrinks. The LOOK: the length by the size; the thickness with it down to <see cref="MinThick"/> (wave 14's
    ///             smallest wand) and no thinner, the tip's bead kept round (<see cref="ApplySize"/>), so the smallest wand is still
    ///             seen; the marker, the cue's ring and the spot the same (<see cref="Thick"/>).
    ///   The spot  client wave 15 (w15cast), point 2 (design note: "When I have the wand equipped, require a small indicator showing where the
    ///             holster is, like a slightly transparent but dark circle ... visible through walls and uh, UI panels"): while a hand holds
    ///             the wand a dark see-through disc (<see cref="SpotRadius"/> m at size 1, <see cref="SpotFill"/> inside a slightly lighter
    ///             rim <see cref="SpotRim"/>) stands at the holster facing the eyes, drawn over everything (Loki/UIOverlay: ZTest Always, the
    ///             laser's queue, after the panels; just under the cue's ring, which is drawn over it): <see cref="SpotShown"/>.
    ///             Client wave 18 (w18wand), point 9 (design note: "Make the holster sphere completely transparent. Until you get a little bit close
    ///             to it. While holding the wand."): the spot is invisible (not drawn) while the hand that holds the wand is more than
    ///             <see cref="SpotFar"/> from the holster; nearer it fades in, fully drawn from <see cref="Radius"/> (the reach that puts the
    ///             wand back) on: <see cref="SpotAlpha"/> (a factor on the material's tint). The hand moving away fades it out the same way.
    ///   Heading   client wave 18 (w18wand), point 10 (design note: "When I look around, I have to move my head a certain amount before the wand
    ///             will actually follow ... It needs to follow it the whole time, because allow the player to pick it up without looking";
    ///             "it should follow left, right turns only. The only thing that should change is the holster"): the HOLSTER hangs from its
    ///             own heading, <see cref="HolsterYaw"/>: the head's left / right turn at every frame, no dead zone, no delay
    ///             (<see cref="HeadYaw"/>; the head's tilt up / down and its roll do not move it). <see cref="BodyYaw"/> above is kept as it was
    ///             for its other users (the upright panels, the overhead menu, the backpack zone ...): only the holster changed.
    /// </summary>
    public sealed class WandHolster : MonoBehaviour
    {
        public const float Radius = 0.12f, ReturnSeconds = 0.25f, Length = 0.30f, GlanceLimit = 70f, FollowAngle = 25f, FollowDelay = 1.5f, FollowSpeed = 60f, TrailSeconds = 0.45f;
        public static readonly Vector3 DefaultOffset = new Vector3(0.22f, -0.60f, 0.05f);
        /// <summary>The normal wand is drawn after the panels (10) and before the laser (VR/DioramaRig.Ui.cs LaserSortingOrder 30); its ghost
        /// after both. The ghost's tint (alpha: at the silhouette's edge) and its alpha facing the eye as a share of that.</summary>
        public const int SolidOrder = 11, GhostOrder = DioramaRig.LaserSortingOrder + 3;
        public static readonly Color WoodColour = new Color(0.45f, 0.28f, 0.14f, 1f), TipColour = new Color(1f, 0.9f, 0.55f, 1f), GhostColour = new Color(0.55f, 0.85f, 1f, 0.5f);
        public const float GhostCore = 0.35f;
        /// <summary>Client wave 14 (w14cast): the cue's ring (its radius in metres at size 1, its width) and the lit wand's colours.</summary>
        public const float CueRadius = 0.075f, CueWidth = 0.006f, CuePulse = 2.5f;
        public static readonly Color CueWood = new Color(0.95f, 0.72f, 0.35f, 1f), CueTip = new Color(1f, 1f, 0.85f, 1f), CueColour = new Color(1f, 0.85f, 0.35f, 0.95f), CueAwayColour = new Color(0.55f, 0.9f, 1f, 0.95f);
        /// <summary>The cue is shown now; it says "put it back" (else "take it"); the size the wand is drawn at.</summary>
        public bool CueShown => _cue != null && _cue.enabled;
        public bool CueAway { get; private set; }
        public float DrawnScale => Wand != null ? Wand.localScale.z : 1f; // (client wave 21, w21wand: its LENGTH's factor; the thickness: Thick)
        private LineRenderer _cue; private Material _wood, _glow; private bool _cueLit;
        public const int CueSegments = 40;
        /// <summary>Client wave 15 (w15cast), point 2: the holster's spot while the wand is in a hand: its radius (m, at size 1), the
        /// share of it the fill takes (the rest is the rim), its colours.</summary>
        public const float SpotRadius = 0.045f, SpotInner = 0.82f;
        public const int SpotSegments = 36;
        public static readonly Color SpotFill = new Color(0.02f, 0.02f, 0.035f, 0.55f), SpotRim = new Color(0.22f, 0.24f, 0.30f, 0.75f);
        private MeshRenderer _spot;
        /// <summary>The spot is drawn now; its renderer (smoke: the queue and the order it is drawn in).</summary>
        public bool SpotShown => _spot != null && _spot.gameObject.activeSelf;
        public Renderer SpotRenderer => _spot;
        // ---- client wave 18 (w18wand), points 9 and 10
        /// <summary>Point 9: beyond this distance (m) between the holding hand and the holster the spot is not drawn; from <see cref="Radius"/>
        /// in it is fully drawn; between, it fades linearly. Not scaled by "Wand size" (the reach is not either).</summary>
        public const float SpotFar = 0.30f;
        /// <summary>Point 9: how much of the spot is drawn now (0 none: hidden; 1 the whole tint), a factor on its colours' alpha.</summary>
        public float SpotAlpha { get; private set; }
        /// <summary>Point 10: the holster's own heading (degrees about the vertical): the head's at this frame.</summary>
        public float HolsterYaw { get; private set; }
        /// <summary>Point 10: the head's left / right heading, whatever its tilt: the heading of the line it looks along (its tilt up / down and
        /// its roll do not change it: exactly Transform.eulerAngles.y). Looking steeply down (or up), more than <see cref="SteepFrom"/> of the
        /// line straight down, that line's ground part shrinks to nothing and turns unsteady; the head's own up line, whose ground part then
        /// points where the face points (looking up: the other way), is added in, from nothing at <see cref="SteepFrom"/> to as much at
        /// straight down (there a roll of the head turns it, as it turns the face).</summary>
        public static float HeadYaw(Transform head)
        {
            var f = head.forward; var u = head.up;
            float s = f.y < 0f ? 1f : -1f, k = Mathf.Abs(f.y), w = k <= SteepFrom ? 0f : (k - SteepFrom) / (1f - SteepFrom); w *= w;
            float x = f.x + s * w * u.x, z = f.z + s * w * u.z;
            if (x * x + z * z < 1e-10f) return head.eulerAngles.y;
            return Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        }
        /// <summary>Point 10: the share of the look's line pointing straight down (or up) from which the head's up line is added in (0.9: about
        /// 64° down).</summary>
        public const float SteepFrom = 0.9f;
        public const int MaxPanels = 8;
        /// <summary>The ghost's renderers (smoke: drawn or not, for the pixel comparison).</summary>
        public readonly List<Renderer> Ghosts = new List<Renderer>();
        /// <summary>The panels passed to the shaders at the last frame (smoke, log).</summary>
        public int PanelsPassed { get; private set; }
        public float BodyYaw { get; private set; }
        public Vector3 HolsterPos { get; private set; }
        public Quaternion HolsterRot { get; private set; }
        /// <summary>The wand (its handle's end; +Z along it) and its tip.</summary>
        public Transform Wand { get; private set; }
        public Transform Tip { get; private set; }
        public Transform HeldBy { get; private set; }
        public bool Returning => _returnT >= 0f;
        public bool Shown => Wand != null && Wand.gameObject.activeSelf;
        public bool MarkerShown => _marker != null && _marker.activeSelf;
        public bool LineShown => _line != null && _line.enabled;
        public int TrailPoints => _trail != null && _trail.enabled ? _trail.positionCount : 0;
        private DioramaRig _rig; private bool _init, _following; private float _turnedFor, _returnT = -1f;
        private Vector3 _returnFrom; private Quaternion _returnRot;
        private GameObject _marker; private LineRenderer _line, _trail;
        private readonly List<(Vector3 p, float t)> _trailPts = new List<(Vector3, float)>();

        // ---- client wave 21 (w21wand): the wand's size between the fingertip and its full length
        /// <summary>The thickness never goes under this share of the full wand's (wave 14's smallest size: Comfort.PlaceMinScale then), so
        /// a wand as short as a finger is still seen; the tip's bead (metres across at size 1).</summary>
        public const float MinThick = 0.5f, TipSize = 0.026f;
        /// <summary>How thick the wand (and its marker, cue ring and spot) is drawn at a size.</summary>
        public static float Thick(float size) => Mathf.Max(size, MinThick);
        /// <summary>A wand's model (mine, or a friend's copy: UI/FriendGestures.cs) at a size: +Z, its length, by the size; X / Y, its
        /// thickness, by <see cref="Thick"/>; the bead on its tip kept round at that thickness. From <see cref="MinThick"/> up this is
        /// the plain uniform scale wave 14 drew.</summary>
        public static void ApplySize(Transform wand, float size, Transform tip = null)
        {
            if (wand == null) return;
            size = Mathf.Clamp(size, 0.02f, 8f); float t = Thick(size);
            wand.localScale = new Vector3(t, t, size);
            if (tip == null) tip = wand.Find("tip");
            if (tip != null) tip.localScale = new Vector3(TipSize, TipSize, TipSize * t / size);
        }
        /// <summary>The pointing fingertip of the hand that holds the wand, in the world (LokiGame.W12Cast.cs W12Tip: the flick's and the
        /// pad's own point; set by LokiGame.W12Wand.cs). Null: the wand's own line, LokiGame.TipAhead ahead of the hand. Client wave 23
        /// (w23wand): while that hand's trigger is held it gives the point as it stood in the hand's own frame when the pull began
        /// (LokiGame.W12Wand.cs W23WandFinger), so the wand is rigid in the hand for the whole pull.</summary>
        public System.Func<Transform, Vector3> Fingertip { get; set; }
        /// <summary>The wand in a hand at a size: its length's factor and its turn. The tip lies on the straight line from the hand's
        /// fingertip (the smallest size) to the full wand's tip (the largest); the wand runs from the hand to it. A controller: the
        /// fingertip is on the wand's own line, the turn is the hand's and the factor is the size itself.</summary>
        public (float len, Quaternion rot) HeldPose(Transform hand, float size)
        {
            float min = Comfort.WandMinScale, max = Comfort.WandMaxScale;
            if (size >= max) return (max, hand.rotation);
            var finger = Fingertip != null ? Fingertip(hand) : hand.position + hand.forward * LokiGame.TipAhead;
            var tip = Vector3.Lerp(finger, hand.position + hand.forward * (Length * max), Mathf.InverseLerp(min, max, size));
            var d = tip - hand.position; float m = d.magnitude;
            if (m < 1e-4f) return (size, hand.rotation);
            d /= m; float len = m / Length; bool along = Vector3.Cross(d, hand.forward).sqrMagnitude < 1e-8f;
            if (along && Mathf.Abs(len - size) < 1e-4f) len = size; // (a controller: the size itself, to the bit: no rounding noise in what is drawn and in what friends are sent)
            return (len, along ? hand.rotation : Quaternion.LookRotation(d, hand.up));
        }
        /// <summary>The size in force (VR/Comfort.cs WandScale within the wand's limits).</summary>
        public static float Size => Mathf.Clamp(Comfort.WandScale, Comfort.WandMinScale, Comfort.WandMaxScale);
        /// <summary>The wand put in the hand that holds it, at the size in force.</summary>
        private void PlaceHeld()
        {
            var (len, rot) = HeldPose(HeldBy, Size);
            if (Wand.localScale.z != len) ApplySize(Wand, len, Tip);
            Wand.SetPositionAndRotation(HeldBy.position, rot);
        }

        public void Init(DioramaRig rig)
        {
            _rig = rig;
            var root = new GameObject("Wand"); root.transform.SetParent(rig.transform, false); Wand = root.transform;
            var solid = Shader.Find("Loki/WandSolid"); if (solid == null) { Debug.LogWarning("[Wand] Loki/WandSolid missing"); solid = Shader.Find("Sprites/Default"); }
            var ghostSh = Shader.Find("Loki/WandGhost"); if (ghostSh == null) Debug.LogWarning("[Wand] Loki/WandGhost missing: no ghost");
            var wood = new Material(solid) { name = "WandWood", color = WoodColour };
            var glow = new Material(solid) { name = "WandTip", color = TipColour };
            _wood = wood; _glow = glow; // (client wave 14: lit while the cue shows)
            var ghost = ghostSh != null ? new Material(ghostSh) { name = "WandGhost", color = GhostColour } : null;
            if (ghost != null) ghost.SetFloat("_Core", GhostCore);
            Part(PrimitiveType.Cylinder, "rod", new Vector3(0, 0, Length / 2), new Vector3(0.014f, Length / 2, 0.014f), wood, Quaternion.Euler(90, 0, 0), ghost);
            Part(PrimitiveType.Cylinder, "handle", new Vector3(0, 0, 0.045f), new Vector3(0.022f, 0.045f, 0.022f), wood, Quaternion.Euler(90, 0, 0), ghost);
            var tip = Part(PrimitiveType.Sphere, "tip", new Vector3(0, 0, Length), Vector3.one * 0.026f, glow, Quaternion.identity, ghost);
            Tip = tip.transform;
            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); _marker.name = "WandHolsterMarker"; Destroy(_marker.GetComponent<Collider>());
            _marker.transform.SetParent(rig.transform, false); _marker.transform.localScale = Vector3.one * 0.07f;
            _marker.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.5f, 0.9f, 1f, 0.75f) };
            _marker.SetActive(false);
            _line = Line("WandLine", 0.004f, 0.0015f, LineColour, new Color(1f, 0.9f, 0.55f, 0.1f));
            _trail = Line("WandTrail", 0.010f, 0.003f, new Color(0.6f, 0.9f, 1f, 0.15f), new Color(0.75f, 0.95f, 1f, 0.95f));
            _cue = Line("WandCue", CueWidth, CueWidth, CueColour, CueColour); _cue.positionCount = CueSegments; _cue.loop = true; // client wave 14 (w14cast), point 13
            _spot = Spot(); // client wave 15 (w15cast), point 2
            root.SetActive(false);
        }

        private GameObject Part(PrimitiveType type, string name, Vector3 at, Vector3 scale, Material mat, Quaternion rot, Material ghost)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; Destroy(go.GetComponent<Collider>()); // never in the laser's way
            go.transform.SetParent(Wand, false); go.transform.localPosition = at; go.transform.localRotation = rot; go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingOrder = SolidOrder;
            if (ghost != null)
            {
                // the same shape again, drawn only where the part is hidden (Loki/WandGhost)
                var g = new GameObject(name + "Ghost"); g.transform.SetParent(go.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                var gr = g.AddComponent<MeshRenderer>(); gr.sharedMaterial = ghost; gr.sortingOrder = GhostOrder;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr.receiveShadows = false;
                Ghosts.Add(gr);
            }
            return go;
        }

        private static readonly int PanelC = Shader.PropertyToID("_LokiPanelC"), PanelR = Shader.PropertyToID("_LokiPanelR"), PanelU = Shader.PropertyToID("_LokiPanelU"), PanelN = Shader.PropertyToID("_LokiPanelCount");
        private readonly Vector4[] _pc = new Vector4[MaxPanels], _pr = new Vector4[MaxPanels], _pu = new Vector4[MaxPanels];
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly List<(float d, Vector3 c, Vector3 r, Vector3 u)> _panels = new List<(float, Vector3, Vector3, Vector3)>();
        private readonly Dictionary<Transform, (Canvas canvas, RectTransform bg, CanvasGroup group)> _panelParts = new Dictionary<Transform, (Canvas, RectTransform, CanvasGroup)>();

        /// <summary>Once a frame while the wand is shown: the open panels with a background (up to <see cref="MaxPanels"/>, nearest the
        /// eyes first) as rectangles for the wand's two shaders (Resources/Shaders/LokiWandPanels.cginc).</summary>
        private void PanelsToShaders()
        {
            _panels.Clear();
            var eye = _rig.Head.transform.position;
            foreach (var t in UI.UiKit.Panels)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (!_panelParts.TryGetValue(t, out var parts))
                {
                    var bgT = t.Find("bg") as RectTransform;
                    parts = (t.GetComponent<Canvas>(), bgT, t.GetComponent<CanvasGroup>());
                    _panelParts[t] = parts;
                }
                if (parts.bg == null || parts.canvas == null || !parts.canvas.enabled || !parts.bg.gameObject.activeInHierarchy) continue;
                if (parts.group != null && parts.group.alpha < 0.5f) continue; // (a panel fading in: the overhead menu)
                parts.bg.GetWorldCorners(_corners);
                var c = (_corners[0] + _corners[2]) * 0.5f;
                _panels.Add(((c - eye).sqrMagnitude, c, (_corners[3] - _corners[0]) * 0.5f, (_corners[1] - _corners[0]) * 0.5f));
            }
            if (_panelParts.Count > 512) _panelParts.Clear();
            _panels.Sort((x, y) => x.d.CompareTo(y.d));
            int n = Mathf.Min(_panels.Count, MaxPanels);
            for (int i = 0; i < MaxPanels; i++)
            {
                if (i < n) { _pc[i] = _panels[i].c; _pr[i] = _panels[i].r; _pu[i] = _panels[i].u; }
                else _pc[i] = _pr[i] = _pu[i] = Vector4.zero;
            }
            Shader.SetGlobalVectorArray(PanelC, _pc); Shader.SetGlobalVectorArray(PanelR, _pr); Shader.SetGlobalVectorArray(PanelU, _pu); Shader.SetGlobalFloat(PanelN, n);
            PanelsPassed = n;
        }

        // ---- client wave 18 (w18wand), point 20
        /// <summary>Point 20: the wand's wheel (set by LokiGame.W12Wand.cs): while any of it is drawn the wand is drawn as its ghost wherever it
        /// lies over it on screen, in front of it or behind it (UI/WandWheel.cs CoverDiscs; Resources/Shaders/LokiWandPanels.cginc LokiOverWheel).</summary>
        public UI.WandWheel Wheel { get; set; }
        /// <summary>The wheel's discs passed to the shaders at the last frame (0-2; smoke, log).</summary>
        public int WheelDiscsPassed { get; private set; }
        private static readonly int WheelC = Shader.PropertyToID("_LokiWheelC"), WheelN = Shader.PropertyToID("_LokiWheelN");
        private readonly Vector4[] _wc = new Vector4[2];
        /// <summary>After every Update (the wheel is opened and drawn in LokiGame's): the wheel's discs to the wand's two shaders, none while the
        /// wheel is closed or the wand is not shown.</summary>
        private void LateUpdate() => WheelToShaders();
        private void WheelToShaders()
        {
            Vector3 c0 = default, c1 = default, n = Vector3.forward; float r0 = 0f, r1 = 0f;
            bool on = Wheel != null && Wand != null && Wand.gameObject.activeInHierarchy && Wheel.CoverDiscs(out c0, out r0, out c1, out r1, out n);
            if (!on) { r0 = r1 = 0f; }
            _wc[0] = new Vector4(c0.x, c0.y, c0.z, r0); _wc[1] = new Vector4(c1.x, c1.y, c1.z, r1);
            Shader.SetGlobalVectorArray(WheelC, _wc); Shader.SetGlobalVector(WheelN, n);
            WheelDiscsPassed = (r0 > 0f ? 1 : 0) + (r1 > 0f ? 1 : 0);
        }

        /// <summary>Client wave 15 (w15cast), point 2: the holster's spot: a flat disc (the fill) inside a ring (the rim), in its XY plane.</summary>
        private MeshRenderer Spot()
        {
            int n = SpotSegments; var v = new Vector3[1 + 3 * n]; var c = new Color[v.Length]; var tri = new int[9 * n];
            v[0] = Vector3.zero; c[0] = SpotFill;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[1 + i] = d * SpotInner; c[1 + i] = SpotFill;               // the fill's edge
                v[1 + n + i] = d * SpotInner; c[1 + n + i] = SpotRim;        // the rim's inner edge
                v[1 + 2 * n + i] = d; c[1 + 2 * n + i] = SpotRim;             // the rim's outer edge
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n, t = 9 * i;
                tri[t] = 0; tri[t + 1] = 1 + j; tri[t + 2] = 1 + i;
                tri[t + 3] = 1 + n + i; tri[t + 4] = 1 + n + j; tri[t + 5] = 1 + 2 * n + i;
                tri[t + 6] = 1 + n + j; tri[t + 7] = 1 + 2 * n + j; tri[t + 8] = 1 + 2 * n + i;
            }
            var mesh = new Mesh { name = "WandSpot", vertices = v, colors = c, triangles = tri }; mesh.RecalculateBounds();
            var go = new GameObject("WandHolsterSpot"); go.transform.SetParent(_rig.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var sh = Shader.Find("Loki/UIOverlay");
            mr.sharedMaterial = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "WandSpot", renderQueue = DioramaRig.LaserQueue };
            mr.sortingOrder = DioramaRig.LaserSortingOrder - 1; // (over the panels, 10; under the cue's ring)
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            go.SetActive(false);
            return mr;
        }

        private LineRenderer Line(string name, float w0, float w1, Color c0, Color c1)
        {
            var go = new GameObject(name); go.transform.SetParent(_rig.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.startWidth = w0; lr.endWidth = w1; lr.numCapVertices = 2;
            var sh = Shader.Find("Loki/UIOverlay");
            lr.material = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = name, renderQueue = DioramaRig.LaserQueue };
            lr.sortingOrder = DioramaRig.LaserSortingOrder; lr.startColor = c0; lr.endColor = c1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.enabled = false;
            return lr;
        }

        /// <summary>The holster's offset in force: the player's own, or the default on the pointing hand's side.</summary>
        public static Vector3 Offset => Comfort.WandPlaced ? Comfort.WandOffset : new Vector3(Comfort.LeftHanded ? -DefaultOffset.x : DefaultOffset.x, DefaultOffset.y, DefaultOffset.z);

        /// <summary>Once a frame: the body's heading, the holster's place, and the wand when no hand has it. show: the wand scheme is
        /// in use (else everything is hidden); marker: the placement mode's marker.</summary>
        public void Tick(bool show, bool marker, float dt)
        {
            var head = _rig.Head.transform; float headYaw = head.eulerAngles.y;
            if (!_init) { _init = true; BodyYaw = headYaw; }
            float diff = Mathf.DeltaAngle(BodyYaw, headYaw);
            if (Mathf.Abs(diff) > GlanceLimit) { BodyYaw = headYaw - Mathf.Sign(diff) * GlanceLimit; diff = Mathf.Sign(diff) * GlanceLimit; }
            if (Mathf.Abs(diff) > FollowAngle) _turnedFor += dt; else { _turnedFor = 0f; if (Mathf.Abs(diff) < 5f) _following = false; }
            if (_turnedFor >= FollowDelay) _following = true;
            if (_following) BodyYaw = Mathf.MoveTowardsAngle(BodyYaw, headYaw, FollowSpeed * dt);
            // client wave 18 (w18wand), point 10: the holster turns with the head at once (its own heading; BodyYaw above is the others')
            HolsterYaw = HeadYaw(head);
            var body = Quaternion.Euler(0f, HolsterYaw, 0f);
            HolsterPos = head.position + body * Offset;
            HolsterRot = body * Quaternion.Euler(75f, 0f, 0f); // hanging: the tip down, a little ahead
            if (Wand.gameObject.activeSelf != show) Wand.gameObject.SetActive(show);
            if (_marker.activeSelf != (show && marker)) _marker.SetActive(show && marker);
            if (_marker.activeSelf) _marker.transform.position = HolsterPos;
            // client wave 14 (w14cast), point 32: the wand's size (and the marker's)
            // client wave 21 (w21wand): within the wand's own limits (the fingertip .. the default); the thickness never under MinThick
            float size = Size, thick = Thick(size);
            if (HeldBy == null && Wand.localScale.z != size) ApplySize(Wand, size, Tip); // (in a hand: PlaceHeld, below)
            if (Mathf.Abs(_marker.transform.localScale.x - 0.07f * thick) > 1e-6f) _marker.transform.localScale = Vector3.one * (0.07f * thick);
            if (!show) { HeldBy = null; _returnT = -1f; _line.enabled = false; _trail.enabled = false; SetCue(false, false); SpotAlpha = 0f; if (_spot.gameObject.activeSelf) _spot.gameObject.SetActive(false); return; }
            PanelsToShaders(); // client wave 13 (w13resize): what the ghost and the normal wand need to know about the panels
            if (HeldBy != null) PlaceHeld(); // (client wave 21, w21wand: its tip between the fingertip and the full wand's)
            else if (_returnT >= 0f)
            {
                _returnT += dt; float x = Mathf.Clamp01(_returnT / ReturnSeconds); x = x * x * (3f - 2f * x);
                Wand.SetPositionAndRotation(Vector3.Lerp(_returnFrom, HolsterPos, x), Quaternion.Slerp(_returnRot, HolsterRot, x));
                if (_returnT >= ReturnSeconds) _returnT = -1f;
            }
            else Wand.SetPositionAndRotation(HolsterPos, HolsterRot);
            // client wave 15 (w15cast), point 2: the holster's spot while a hand holds the wand, facing the eyes
            // client wave 18 (w18wand), point 9: only while that hand is near: none beyond SpotFar, fading in to the whole tint at the reach
            SpotAlpha = HeldBy == null ? 0f : Mathf.Clamp01((SpotFar - Vector3.Distance(HeldBy.position, HolsterPos)) / (SpotFar - Radius));
            bool spot = SpotAlpha > 0.5f / 255f;
            if (_spot.gameObject.activeSelf != spot) _spot.gameObject.SetActive(spot);
            if (spot)
            {
                var eye = _rig.Head.transform.position; var n = HolsterPos - eye; if (n.sqrMagnitude < 1e-6f) n = Vector3.forward;
                _spot.transform.SetPositionAndRotation(HolsterPos, Quaternion.LookRotation(n.normalized, Vector3.up));
                _spot.transform.localScale = Vector3.one * (SpotRadius * thick);
                var tint = _spot.sharedMaterial.color; if (Mathf.Abs(tint.a - SpotAlpha) > 1e-4f) _spot.sharedMaterial.color = new Color(1f, 1f, 1f, SpotAlpha); // (Loki/UIOverlay: vertex colour x _Color)
            }
            // the trail fades behind the tip
            if (_trail.enabled)
            {
                float now = Time.unscaledTime; _trailPts.RemoveAll(q => now - q.t > TrailSeconds);
                _trail.positionCount = _trailPts.Count; for (int i = 0; i < _trailPts.Count; i++) _trail.SetPosition(i, _trailPts[i].p);
            }
        }

        /// <summary>Client wave 14 (w14cast), point 13: the cue on (a grip now would take the wand; away: would put it back) or off. The ring
        /// stands round the holster facing the eyes; it pulses; the wand is drawn lit meanwhile.</summary>
        public void SetCue(bool on, bool away)
        {
            if (_cue == null) return;
            if (_cue.enabled != on) _cue.enabled = on;
            if (on != _cueLit) { _cueLit = on; if (_wood != null) _wood.color = on ? CueWood : WoodColour; if (_glow != null) _glow.color = on ? CueTip : TipColour; }
            CueAway = on && away;
            if (!on) return;
            var c = away ? CueAwayColour : CueColour; c.a *= 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * CuePulse * 2f * Mathf.PI));
            _cue.startColor = _cue.endColor = c;
            var eye = _rig.Head.transform.position; var n = HolsterPos - eye; if (n.sqrMagnitude < 1e-6f) n = Vector3.forward; n.Normalize();
            var a = Vector3.Cross(n, Vector3.up); if (a.sqrMagnitude < 1e-4f) a = Vector3.right; a.Normalize(); var b = Vector3.Cross(a, n);
            float r = CueRadius * Thick(Size);
            for (int i = 0; i < CueSegments; i++) { float t = i * 2f * Mathf.PI / CueSegments; _cue.SetPosition(i, HolsterPos + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * r); }
        }

        public void Hold(Transform hand) { HeldBy = hand; _returnT = -1f; if (hand != null) PlaceHeld(); }
        /// <summary>Client wave 21 (w21wand): the size changed while a hand holds the wand: drawn at it in this frame.</summary>
        public void Refresh() { if (HeldBy != null) PlaceHeld(); }

        /// <summary>Let go: it flies back to the hip.</summary>
        public void Release() { if (HeldBy == null) return; _returnFrom = Wand.position; _returnRot = Wand.rotation; _returnT = 0f; HeldBy = null; _line.enabled = false; TrailEnd(); }
        /// <summary>The wand's own pointing line, from its tip to where it points.</summary>
        public void SetLine(bool on, Vector3 to) => SetLine(on, to, 1f);
        /// <summary>Client wave 18 (w18wand), point 11: the line at a strength (0 to 1, the laser's fade: LokiGame.W12Wand.cs); under the
        /// laser's least strength it is not drawn at all.</summary>
        public void SetLine(bool on, Vector3 to, float alpha)
        {
            on &= alpha > DioramaRig.LaserAlphaMin; LineAlpha = on ? alpha : 0f;
            _line.enabled = on; if (!on) return;
            _line.SetPosition(0, Tip.position); _line.SetPosition(1, to);
            _line.startColor = new Color(LineColour.r, LineColour.g, LineColour.b, LineColour.a * alpha); _line.endColor = new Color(LineColour.r, LineColour.g, LineColour.b, 0.1f * alpha);
        }
        /// <summary>Client wave 18 (w18wand), point 11: the line's colour (alpha at the tip) and its strength now (0: not drawn).</summary>
        public static readonly Color LineColour = new Color(1f, 0.9f, 0.55f, 0.9f);
        public float LineAlpha { get; private set; }
        public void TrailBegin() { _trailPts.Clear(); _trail.positionCount = 0; _trail.enabled = true; }
        public void TrailAdd(Vector3 p) { _trailPts.Add((p, Time.unscaledTime)); }
        public void TrailEnd() { _trail.enabled = false; _trailPts.Clear(); }

        /// <summary>The placement mode: the holster put where a hand holds it (kept as an offset from the body anchor).</summary>
        public void PlaceAt(Vector3 world)
        {
            var body = Quaternion.Euler(0f, HolsterYaw, 0f); // (client wave 18, point 10: the holster's own heading)
            var o = Quaternion.Inverse(body) * (world - _rig.Head.transform.position);
            Comfort.WandOffset = new Vector3(Mathf.Clamp(o.x, -0.8f, 0.8f), Mathf.Clamp(o.y, -1.4f, 0.3f), Mathf.Clamp(o.z, -0.6f, 0.8f)); Comfort.WandPlaced = true;
        }
    }
}
