using System;
using System.Collections.Generic;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 23 (w23place): the design's headset feedback after Suite 39-65 (playtest notes, answers 36, 38, 47, 49,
    /// 50): WHERE the tutorial's things stand, in which ORDER they are drawn and what FADES. (The texts and the steps' parts are w23lines'.)
    ///   Item 4   (38: "when the pouring is placed ... it is placed directly under the player ... It needs to be placed much further ahead
    ///            so that the player can very clearly see it. Ideally, you should place a small light underneath the pouring that emits a
    ///            humming sound") The practice Poring stood 3 cells from the character on the side nearest the eyes: in the Follow view
    ///            56.6° below eye level (the character itself is 47.9° down: VR/ViewController.cs), under the lower edge of a headset's
    ///            view. Now it stands where the HEAD faces when its step begins, <see cref="W23PoringDownMin"/> to
    ///            <see cref="W23PoringDownMax"/>° below eye level straight ahead (<see cref="W23PoringCell"/>: chosen by its angles from the
    ///            eyes, so the rule holds in every view: about 13 cells beyond the character in Follow, 26 on the table at its first size,
    ///            4 in first person), on a walkable cell of the room drawn. Its hops (LokiGame.W20Tut.cs W20PoringHop) stay in that band,
    ///            within <see cref="W23PoringBearingMax"/>° of the heading. Under it a soft glowing DISC on the ground
    ///            (<see cref="W23PoringLight"/>: made in code, it follows the Poring) while the Poring exists, and a quiet steady HUM from
    ///            it (a tone made in code, 3D at the Poring, under the effects volume) only while a step wants the player on the Poring
    ///            (<see cref="W23PoringWanted"/>: the laser, the look and the "act" steps).
    ///   Item 12  (47: "The practice panel ... gets occluded by the tutorial panel. So I can't even read that it says hold me by my edge.")
    ///            The guide's canvas was drawn AFTER every panel (order 18 against 10) though it stands behind them (0.9 m; a window
    ///            0.62 m): the practice panel's line lay 2° above eye level, 4° left to 7° right of the middle, inside the guide's box
    ///            (18° left to 18° right, 8° up to 18° down). Now the guide is drawn UNDER every panel (UI/TutorialGuide.cs
    ///            SortingOrder) and stands BESIDE what the player works on (<see cref="W23GuideSide"/>); the practice panel opens a
    ///            little to the pointing hand's side (<see cref="W23PanelPlace"/>), the guide on the other.
    ///   Item 14  (49: "the casting options panel is way too close") UI/CastingWays.cs: 1.2 m. And the fault behind "the tutorial page
    ///            also gets occluded by the casting options": a step that ENDED into the casting step drew the guide once more in that
    ///            frame (LokiGame.W18Tutorial.cs UpdateW18Tut), and nothing hid it again: an empty page "Casting" with "Skip step" stood
    ///            in the middle of the Casting panel at its distance and order. Fixed there; <see cref="UpdateW23Place"/> also keeps
    ///            the guide away while the columns are the page.
    ///   Item 15  (50: "The tutorial page is also occluding the different casting options, such as a pad, which means that I can't use
    ///            them") A pad raised to be looked at (0.4 m ahead, 0.15 m under the eyes) spans 7° to 34° down: its top third was
    ///            painted over by the guide. Now a tool is drawn over the guide (the order above), the guide stands beside the hands
    ///            during a casting way's lesson, and while a casting tool's box still meets the guide's from the eyes
    ///            (<see cref="W23GuideFade"/>) the guide fades to <see cref="W23GuideFadeAlpha"/> and its buttons take no press (so a
    ///            trigger meant for the tool never presses "End tutorial" behind it); both come back when the tool is gone.
    /// Hooks (marked "client wave 23 (w23place)"): LokiGame.W18Tutorial.cs (W18TDummyEnsure, W18TPanelOpen, W18TutDraw, UpdateW18Tut),
    /// LokiGame.W20Tut.cs (UpdateW20Tut calls <see cref="UpdateW23Place"/>; W20PoringHop; W20CalEnd), UI/TutorialGuide.cs (SortingOrder,
    /// Alpha, Side), UI/TutorialClip.cs (the ghost's order and fade), UI/CastingWays.cs (the distance, the order), UI/LookCalibrator.cs (item
    /// 3: the dots).
    /// </summary>
    public sealed partial class LokiGame
    {
        // ---- every number here is Loki's unless it says "design note" / "the brief" (FLAGGED in design notes)
        /// <summary>The practice Poring's place from the eyes: degrees below eye level (the brief: "about 20 to 35"), the angle aimed
        /// at; a hop lands within <see cref="W23PoringBearingMax"/>° of the head's level heading; a cell more than
        /// <see cref="W23PoringAheadDeg"/>° off it is not "ahead"; at a step's start a Poring outside the band by more than
        /// <see cref="W23PoringKeepDeg"/>° or more than <see cref="W23PoringKeepBearing"/>° off the heading is placed anew.</summary>
        public const float W23PoringDownMin = 20f, W23PoringDownMax = 35f, W23PoringDownAim = 27f, W23PoringBearingMax = 22f, W23PoringAheadDeg = 60f, W23PoringKeepDeg = 3f, W23PoringKeepBearing = 35f;
        /// <summary>Cells around the character searched for the Poring's place; the longest hop (cells).</summary>
        public const int W23PoringSearchCells = 40, W23PoringHopMaxCells = 12;
        /// <summary>The light under the Poring: its radius and its height over the ground (cells), its slow breathing (seconds a cycle,
        /// the dimmest share of its opacity).</summary>
        public const float W23DiscRadiusCells = 1.4f, W23DiscLiftCells = 0.05f, W23DiscPulseSeconds = 2f, W23DiscDim = 0.7f;
        /// <summary>The light's hum: on at all; its volume (a share of the effects volume); its pitch (Hz: 110, with two overtones);
        /// seconds to fade in and out; full within / silent beyond these metres from the head (Unity's linear rolloff).</summary>
        public const bool W23HumOn = true;
        public const float W23HumVolume = 0.15f, W23HumHz = 110f, W23HumFadeSeconds = 0.4f, W23HumFullM = 1f, W23HumFarM = 10f;
        /// <summary>Where the guide stands beside the player's work, degrees from straight ahead: beside the hands (a casting way's
        /// lesson, the practice panel); beside a window or the menu's board (0.66 m wide at 0.65 m: 27° to each side, with its frame 29°).</summary>
        public const float W23GuideSideDeg = 38f, W23GuideWindowSideDeg = 52f;
        /// <summary>client wave 23 (the merge): the guide's side angle in the lessons of the two WIDE tools, the flick's rows (27 cm) and
        /// the wand's wheel in front of the eyes (50 cm at 0.6 m). At <see cref="W23GuideSideDeg"/> their boxes still met the guide's by
        /// 1.7° and 5.8° from the eyes (smoke w23place E on the first build), so the guide faded to 20 % whenever they were up. The
        /// window angle was 48: the menu's board (66 cm wide at its usual place) met the guide by 0.9°.</summary>
        public const float W23GuideWideSideDeg = 47f;
        /// <summary>The guide's opacity while a casting tool's box meets its own (the brief: "about 20 %"), and the seconds the change takes.</summary>
        public const float W23GuideFadeAlpha = 0.2f, W23GuideFadeSeconds = 0.2f;
        /// <summary>The practice panel: degrees to the pointing hand's side of straight ahead; metres ahead and against the eyes (a list
        /// window's own: UI/ListWindow.cs Open).</summary>
        public const float W23PanelSideDeg = 14f, W23PanelDistance = 0.62f, W23PanelUp = -0.10f;
        /// <summary>The panels that are a casting way's own tool (their names: UI/UiKit.cs PanelKinds): the pads, the flick's rows, the
        /// wand's wheel, the forearm bar, the skill wheels, the Quick Use Bubbles.</summary>
        public static readonly string[] W23ToolNames = { "CastPadLeft", "CastPadRight", "FlickLeft", "FlickRight", "WandWheel", "QuickBar", "SkillWheelOff", "SkillWheelMain", "QuickBubble" };

        private string _w23PoringStep = "";
        private Transform _w23Disc; private Material _w23DiscMat; private AudioSource _w23Hum; private float _w23HumLevel, _w23DiscAlpha;
        private bool _w23GuideLocked;
        /// <summary>The light under the practice Poring (null before the first Poring), its opacity now, its hum's source (smoke, log).</summary>
        public Transform W23Disc => _w23Disc;
        public float W23DiscAlpha => _w23DiscAlpha;
        public AudioSource W23Hum => _w23Hum;
        public bool W23HumPlaying => _w23Hum != null && _w23Hum.isPlaying;
        /// <summary>A step wants the player on the Poring now (the laser, the look, the "act" step): the hum plays.</summary>
        public bool W23PoringWanted { get { if (!W18TutRunning || _w18tDummy == null || _w22Lesson != null) return false; string k = W18TutStepKey; return k == "laser" || k == "look" || k == "act"; } }
        /// <summary>The Poring placed by <see cref="W23PoringCell"/> so far; the casting tool whose box meets the guide's now ("": none),
        /// how often one began to, the guide's buttons locked meanwhile (smoke, log).</summary>
        public int W23PoringPlacings { get; private set; }
        public string W23GuideTool { get; private set; } = "";
        public int W23GuideFades { get; private set; }
        public bool W23GuideLocked => _w23GuideLocked;
        /// <summary>One more thing that counts as a casting tool for the guide's fade (a test's stand-in panel; null: none).</summary>
        public RectTransform W23ExtraTool { get; set; }

        /// <summary>Once a frame (LokiGame.W20Tut.cs UpdateW20Tut, after the tutorial drew its guide): the Poring's light and hum, the
        /// guide's side given back outside the tutorial, no guide beside the casting ways' columns, the guide's fade.</summary>
        private void UpdateW23Place()
        {
            float dt = Time.unscaledDeltaTime;
            W23PoringLight(dt);
            var g = W18TGuide; if (g == null) return;
            if (!W18TutRunning && Mathf.Abs(g.Side) > 0.01f) g.SetSide(0f);
            if (W18TutRunning && _w22Lesson == null && W18TutStepKey == "casting" && g.Visible) g.Hide(); // (the columns are this step's page: UI/CastingWays.cs)
            W23GuideFade(dt);
        }

        // ------------------------------------------------------------------ item 4: the practice Poring's place, its light, its hum
        /// <summary>A point as the eyes see it: degrees BELOW eye level (the horizon through the eyes) and degrees to the RIGHT of the
        /// head's level heading. False: no head, or the point is straight above / below the eyes.</summary>
        public bool W23Angles(Vector3 world, out float down, out float bearing)
        {
            down = 0f; bearing = 0f;
            var rig = _boot != null ? _boot.Rig : null; if (rig == null || rig.Head == null) return false;
            var head = rig.Head.transform; var f = head.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            var v = world - head.position; var flat = new Vector3(v.x, 0f, v.z); float dh = flat.magnitude; if (dh < 1e-4f) return false;
            down = Mathf.Atan2(-v.y, dh) * Mathf.Rad2Deg; bearing = Vector3.SignedAngle(f, flat, Vector3.up);
            return true;
        }

        /// <summary>The cell for the practice Poring: walkable, in the room drawn, at least 2 cells from the character, and of those
        /// the one nearest <see cref="W23PoringDownAim"/>° below eye level straight ahead of the head. A cell inside the band
        /// (<see cref="W23PoringDownMin"/> - <see cref="W23PoringDownMax"/>°) always wins over one outside it, a cell ahead (within
        /// <see cref="W23PoringAheadDeg"/>° of the heading) over one that is not: where the floor ends before the band, it is the
        /// walkable cell ahead that comes nearest to it (never the cell at the feet while another is free).</summary>
        private bool W23PoringCell(out int bx, out int by, out float bestDown, out float bestBearing)
        {
            bx = -1; by = -1; bestDown = 0f; bestBearing = 0f;
            var self = Entities != null ? Entities.Self : null; var gat = _boot.Map != null && _boot.Map.Current != null ? _boot.Map.Current.Gat : null;
            if (self == null || gat == null) return false;
            var map = _boot.Map.transform; float y = self.transform.localPosition.y, best = float.MaxValue;
            int sx = Mathf.RoundToInt(self.Cell.x), sy = Mathf.RoundToInt(self.Cell.y);
            for (int dx = -W23PoringSearchCells; dx <= W23PoringSearchCells; dx++)
                for (int dy = -W23PoringSearchCells; dy <= W23PoringSearchCells; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) < 2) continue;
                    int x = sx + dx, yy = sy + dy;
                    if (!gat.InBounds(x, yy) || !gat.Get(x, yy).Walkable) continue;
                    if (Room != null && !Room.Visible(new Vector2(x, yy))) continue; // (client wave 20, point 54: only the room drawn)
                    if (!W23Angles(map.TransformPoint(new Vector3(x + 0.5f, y, yy + 0.5f)), out float down, out float bearing)) continue;
                    float off = down < W23PoringDownMin ? W23PoringDownMin - down : down > W23PoringDownMax ? down - W23PoringDownMax : 0f;
                    float cost = (Mathf.Abs(bearing) > W23PoringAheadDeg ? 100000f : 0f) + off * 100f + Mathf.Abs(down - W23PoringDownAim) + Mathf.Abs(bearing) * 2f;
                    if (cost < best) { best = cost; bx = x; by = yy; bestDown = down; bestBearing = bearing; }
                }
            return bx >= 0;
        }

        /// <summary>The Poring is where a step can use it: in the band (with <see cref="W23PoringKeepDeg"/>° to spare) and within
        /// <see cref="W23PoringKeepBearing"/>° of the heading.</summary>
        private bool W23PoringInView(Unit u)
        {
            if (u == null || !W23Angles(u.transform.position, out float down, out float bearing)) return false;
            return down >= W23PoringDownMin - W23PoringKeepDeg && down <= W23PoringDownMax + W23PoringKeepDeg && Mathf.Abs(bearing) <= W23PoringKeepBearing;
        }

        /// <summary>The practice Poring's light and hum, once a frame: the disc lies on the ground under the Poring (a child of the map,
        /// so it turns and grows with it; on the layer the tutorial's clips leave out, so no clip's picture shows it) and follows its
        /// hops; the hum plays from it while <see cref="W23PoringWanted"/>.</summary>
        private void W23PoringLight(float dt)
        {
            var u = _w18tDummy; var map = _boot != null && _boot.Map != null ? _boot.Map.transform : null;
            bool on = W18TutRunning && u != null && map != null && Entities != null && Entities.Get(W18TDummyId) == u;
            if (!on)
            {
                _w23HumLevel = 0f; _w23DiscAlpha = 0f;
                if (_w23Disc != null && _w23Disc.gameObject.activeSelf) { if (_w23Hum != null && _w23Hum.isPlaying) _w23Hum.Stop(); _w23Disc.gameObject.SetActive(false); }
                return;
            }
            if (_w23Disc == null) W23MakeDisc();
            if (_w23Disc.parent != map) _w23Disc.SetParent(map, false);
            if (!_w23Disc.gameObject.activeSelf) _w23Disc.gameObject.SetActive(true);
            _w23Disc.localPosition = u.transform.localPosition + Vector3.up * W23DiscLiftCells;
            _w23Disc.localRotation = Quaternion.identity; _w23Disc.localScale = Vector3.one * (2f * W23DiscRadiusCells);
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / W23DiscPulseSeconds);
            _w23DiscAlpha = Mathf.Lerp(W23DiscDim, 1f, wave);
            if (_w23DiscMat != null) _w23DiscMat.SetColor("_Color", new Color(1f, 1f, 1f, _w23DiscAlpha));
            if (_w23Hum == null) return;
            bool want = W23HumOn && W23PoringWanted;
            _w23HumLevel = Mathf.MoveTowards(_w23HumLevel, want ? 1f : 0f, dt / W23HumFadeSeconds);
            var audio = global::Loki.Audio.LokiAudio.Instance;
            _w23Hum.volume = W23HumVolume * (audio != null ? audio.EffectsVolume : 0f) * _w23HumLevel;
            if (_w23HumLevel > 0f && !_w23Hum.isPlaying) _w23Hum.Play();
            else if (_w23HumLevel <= 0f && _w23Hum.isPlaying) _w23Hum.Stop();
        }

        private void W23MakeDisc()
        {
            var go = new GameObject("PracticePoringLight"); go.layer = TutorialClip.HiddenLayer; // (drawn by the head, left out by the clips' own camera: UI/TutorialClip.cs)
            var mesh = new Mesh { name = "PracticePoringLight" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            if (_w23DiscMat == null)
            {
                // Loki/Str: texture x colour, depth-tested (the Poring's own sprite hides what is behind it), never written, drawn a hair
                // towards the eyes (no flicker against the ground), cut by the rooms of an indoor map; here with plain alpha blending
                var sh = Shader.Find("Loki/Str"); bool str = sh != null; if (!str) sh = Shader.Find("Sprites/Default");
                _w23DiscMat = new Material(sh) { name = "PracticePoringLight", mainTexture = W23DiscTexture() };
                if (str) { _w23DiscMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); _w23DiscMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
            }
            mr.sharedMaterial = _w23DiscMat;
            var src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.loop = true; src.clip = W23HumClip(); src.volume = 0f;
            src.spatialBlend = 1f; src.dopplerLevel = 0f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = W23HumFullM; src.maxDistance = W23HumFarM;
            _w23Hum = src; _w23Disc = go.transform;
        }

        /// <summary>The light's picture, 128 x 128: a warm glow that is brightest in the middle, then a dark outline with a soft outer
        /// edge (as the calibration's dots: seen on a light floor and on a dark one). Never pure black (Loki/Str drops black texels).</summary>
        private static Texture2D W23DiscTexture()
        {
            const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "PracticePoringLight", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            Color core = new Color(1f, 0.95f, 0.70f), glow = new Color(1f, 0.80f, 0.32f), rim = new Color(0.22f, 0.12f, 0.03f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f).magnitude; Color c;
                    if (r >= 1f) { c = rim; c.a = 0f; }
                    else if (r > 0.88f) { c = rim; c.a = Mathf.Lerp(0.75f, 0f, (r - 0.88f) / 0.12f); }
                    else if (r > 0.74f) { c = rim; c.a = 0.75f; }
                    else if (r > 0.62f) { c = glow; c.a = Mathf.Lerp(0.55f, 0.35f, (r - 0.62f) / 0.12f); }
                    else { c = Color.Lerp(core, glow, r / 0.62f); c.a = Mathf.Lerp(0.85f, 0.55f, r / 0.62f); }
                    px[y * n + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true, true);
            return tex;
        }

        /// <summary>The hum: two seconds that loop without a seam (every part a whole number of cycles): <see cref="W23HumHz"/> Hz with
        /// its second and third overtones, swelling a little once every two seconds.</summary>
        private static AudioClip W23HumClip()
        {
            const int rate = 44100; int len = rate * 2; var s = new float[len]; double hz = Math.Round(W23HumHz);
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)rate, w = 2.0 * Math.PI * hz * t;
                double swell = 0.85 + 0.15 * Math.Sin(2.0 * Math.PI * 0.5 * t);
                s[i] = (float)((Math.Sin(w) * 0.55 + Math.Sin(2.0 * w) * 0.28 + Math.Sin(3.0 * w) * 0.12) * swell * 0.6);
            }
            var clip = AudioClip.Create("practice-poring-hum", len, 1, rate, false); clip.SetData(s, 0); return clip;
        }

        // ------------------------------------------------------------------ items 12, 14, 15: the guide beside the work, under it, fading
        /// <summary>The guide's place for this step (LokiGame.W18Tutorial.cs W18TutDraw, before the guide is shown): in front, as always,
        /// unless the step has the player work on something that stands there. A casting way's tool at the pointing hand (the pad, the
        /// flick, the wand; the wheels, the bubbles) and the practice panel: the guide <see cref="W23GuideSideDeg"/>° to the WALKING
        /// hand's side. The forearm bar (on the walking arm): to the pointing hand's side. A window or the menu's board in front (the
        /// menu step, Settings, the Skills page; the ring is at the walking hand): <see cref="W23GuideWindowSideDeg"/>° to the pointing
        /// hand's side.</summary>
        private void W23GuideSide(W18TStep st)
        {
            var g = W18TGuide; if (g == null) return;
            float p = Comfort.LeftHanded ? -1f : 1f, side = 0f; // (+ is right: the pointing hand's side of a right-handed player)
            switch (st != null ? st.Key : "")
            {
                case "panel": case "cast-pad": case "cast-wheels": side = -p * W23GuideSideDeg; break;
                case "cast-flick": case "cast-wand": side = -p * W23GuideWideSideDeg; break; // (client wave 23, the merge: the two wide tools)
                case "cast-elbow": side = p * W23GuideSideDeg; break;
                case "menu": case "codex": case "settings": case "cast-skills": side = p * W23GuideWindowSideDeg; break; // (wave 28, wcodex: the Codex step: the ring, then the book)
            }
            g.SetSide(side);
        }

        /// <summary>The practice panel's place (LokiGame.W18Tutorial.cs W18TPanelOpen): a list window's own distance and height,
        /// <see cref="W23PanelSideDeg"/>° to the pointing hand's side, facing the eyes: with the guide on the other side
        /// (<see cref="W23GuideSide"/>) the whole panel and the band round its edge are free.</summary>
        private void W23PanelPlace(Transform panel)
        {
            var rig = _boot != null ? _boot.Rig : null; if (rig == null || rig.Head == null || panel == null) return;
            var head = rig.Head.transform; var f = head.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            float p = Comfort.LeftHanded ? -1f : 1f;
            panel.position = head.position + Quaternion.AngleAxis(p * W23PanelSideDeg, Vector3.up) * f * W23PanelDistance + Vector3.up * W23PanelUp;
            UiKit.FaceHead(panel, rig.Head);
        }

        /// <summary>A thing's box as the eyes see it, degrees: from <see cref="L"/> to <see cref="R"/> sideways (+ right) and from
        /// <see cref="B"/> to <see cref="T"/> up (+), about the level line from the eyes to a reference point (the guide's middle).</summary>
        public struct W23Box
        {
            public float L, R, B, T;
            public bool Meets(W23Box o, float margin = 0f) => L < o.R + margin && o.L < R + margin && B < o.T + margin && o.B < T + margin;
            public void Add(W23Box o) { L = Mathf.Min(L, o.L); R = Mathf.Max(R, o.R); B = Mathf.Min(B, o.B); T = Mathf.Max(T, o.T); }
            public override string ToString() => $"{L:0.0}° to {R:0.0}° sideways, {B:0.0}° to {T:0.0}° up";
        }
        private static readonly Vector3[] W23Corners = new Vector3[4];

        /// <summary>The box of <paramref name="n"/> points from <paramref name="eye"/>, about the level axis <paramref name="axis"/>.
        /// A point behind the eyes widens the box to that side's 90°. False: every point is behind.</summary>
        public static bool W23BoxOf(Vector3 eye, Vector3 axis, Vector3[] pts, int n, out W23Box box)
        {
            box = new W23Box { L = float.MaxValue, R = float.MinValue, B = float.MaxValue, T = float.MinValue };
            axis.y = 0f; if (axis.sqrMagnitude < 1e-6f) return false; axis.Normalize();
            var right = Vector3.Cross(Vector3.up, axis); int ahead = 0; bool behindLeft = false, behindRight = false;
            for (int i = 0; i < n; i++)
            {
                var v = pts[i] - eye; float z = Vector3.Dot(v, axis), x = Vector3.Dot(v, right);
                if (z <= 0.01f) { if (x < 0f) behindLeft = true; else behindRight = true; continue; }
                ahead++;
                float az = Mathf.Atan2(x, z) * Mathf.Rad2Deg, el = Mathf.Atan2(v.y, Mathf.Sqrt(x * x + z * z)) * Mathf.Rad2Deg;
                box.L = Mathf.Min(box.L, az); box.R = Mathf.Max(box.R, az); box.B = Mathf.Min(box.B, el); box.T = Mathf.Max(box.T, el);
            }
            if (ahead == 0) return false;
            if (behindLeft) box.L = -90f; if (behindRight) box.R = 90f;
            return true;
        }
        /// <summary>The box of a panel's rectangle (every corner <paramref name="outsetM"/> metres further out in the panel's own plane:
        /// its frame, its grab band).</summary>
        public static bool W23BoxOf(Vector3 eye, Vector3 axis, RectTransform rt, float outsetM, out W23Box box)
        {
            box = default; if (rt == null) return false;
            rt.GetWorldCorners(W23Corners); // (bottom left, top left, top right, bottom right)
            if (outsetM != 0f)
            {
                Vector3 r = rt.right * outsetM, u = rt.up * outsetM;
                W23Corners[0] += -r - u; W23Corners[1] += -r + u; W23Corners[2] += r + u; W23Corners[3] += r - u;
            }
            return W23BoxOf(eye, axis, W23Corners, 4, out box);
        }

        /// <summary>The guide GROUP's box from the eyes (the guide's panel with its clip, and the ghost controller that floats in front of
        /// its plate), the eyes and the level axis towards the guide's middle that it is measured about. False: no guide shown.</summary>
        public bool W23GuideBox(out Vector3 eye, out Vector3 axis, out W23Box box)
        {
            eye = Vector3.zero; axis = Vector3.forward; box = default;
            var g = W18TGuide; var rig = _boot != null ? _boot.Rig : null;
            if (g == null || !g.Visible || rig == null || rig.Head == null) return false;
            eye = rig.Head.transform.position; axis = g.Panel.position - eye; axis.y = 0f;
            if (axis.sqrMagnitude < 1e-6f) return false;
            if (!W23BoxOf(eye, axis, (RectTransform)g.Panel, 0f, out box)) return false;
            var plate = W22GhostPlate;
            if (plate != null && plate.gameObject.activeInHierarchy)
            {
                // the ghost floats ClipGhostStand.FloatM in front of its plate and is a few centimetres deep: the plate's corners that much nearer
                plate.GetWorldCorners(W23Corners);
                var near = -plate.forward * (2f * ClipGhostStand.FloatM);
                for (int i = 0; i < 4; i++) W23Corners[i] += near;
                if (W23BoxOf(eye, axis, W23Corners, 4, out var ghost)) box.Add(ghost);
            }
            return true;
        }

        /// <summary>A panel is drawn now: its object active, its canvas on, not faded out by a group of its own.</summary>
        private static bool W23Drawn(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var cv = t.GetComponent<Canvas>(); if (cv != null && !cv.enabled) return false;
            var cg = t.GetComponent<CanvasGroup>(); return cg == null || cg.alpha > 0.05f;
        }
        private static bool W23IsTool(string name) { for (int i = 0; i < W23ToolNames.Length; i++) if (W23ToolNames[i] == name) return true; return false; }

        /// <summary>Item 15, once a frame: while a casting tool that is drawn meets the guide group's box from the eyes, the guide (and
        /// its ghost controller) fades to <see cref="W23GuideFadeAlpha"/> in <see cref="W23GuideFadeSeconds"/> s and its buttons take no
        /// press; when no tool does, both come back. (The tool is drawn over the guide whatever the fade: UI/TutorialGuide.cs SortingOrder.)</summary>
        private void W23GuideFade(float dt)
        {
            var g = W18TGuide; if (g == null) return;
            string tool = null;
            if (W23GuideBox(out var eye, out var axis, out var gb))
            {
                var panels = UiKit.Panels;
                for (int i = 0; i < panels.Count && tool == null; i++)
                {
                    var t = panels[i]; if (t == null || !W23IsTool(t.name) || (W18TutRunning && t.name == "QuickBubble") || !W23Drawn(t)) continue;
                    if (W23BoxOf(eye, axis, t as RectTransform, 0f, out var b) && gb.Meets(b)) tool = t.name;
                }
                var extra = W23ExtraTool;
                if (tool == null && extra != null && W23Drawn(extra) && W23BoxOf(eye, axis, extra, 0f, out var eb) && gb.Meets(eb)) tool = extra.name;
            }
            if ((tool ?? "") != W23GuideTool)
            {
                W23GuideTool = tool ?? "";
                if (tool != null) { W23GuideFades++; Debug.Log($"[Tutorial] the casting tool '{tool}' meets the guide from the eyes: the guide fades to {W23GuideFadeAlpha * 100f:0} %, its buttons take no press"); }
                else Debug.Log("[Tutorial] no casting tool meets the guide any more: the guide comes back");
            }
            float a = Mathf.MoveTowards(g.Alpha, tool != null ? W23GuideFadeAlpha : 1f, (1f - W23GuideFadeAlpha) * dt / Mathf.Max(0.01f, W23GuideFadeSeconds));
            g.Alpha = a;
            var ghost = W22Ghost; if (ghost != null && (a < 1f || ghost.Fade < 1f)) ghost.SetFade(a);
            bool locked = tool != null;
            if (locked != _w23GuideLocked)
            {
                _w23GuideLocked = locked;
                W23Lock(g.LeftButton, locked); W23Lock(g.RightButton, locked); W23Lock(g.MiddleButton, locked);
            }
        }
        private static void W23Lock(UiButton b, bool locked) { if (b == null) return; b.Interactable = !locked; b.SetHover(b.Hovered && !locked); }
    }
}
