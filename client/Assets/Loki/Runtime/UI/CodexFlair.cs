using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>Wave 28 (wcodex), design note (2026-10-15): "When an NPC reveals the name of a creature, you should place a special indicator, like a
    /// green aura that pulsates around the codex tab on the wrist menu": the theme's soft rim (UI/Theme.cs HoverRim) round the Codex button,
    /// green, its strength breathing between <see cref="Low"/> and <see cref="High"/> once every <see cref="Period"/> s (FLAGGED: a slow
    /// glow, never a flash).</summary>
    public sealed class CodexGlow : MonoBehaviour
    {
        public const float Low = 0.30f, High = 0.80f, Period = 1.6f;
        public static readonly Color Green = new Color(0.35f, 0.95f, 0.45f, 1f);
        private Image _img;
        public float Alpha => _img != null ? _img.color.a : 0f;
        public static CodexGlow Add(RectTransform button, float grow)
        {
            var size = button.rect.size + new Vector2(grow, grow);
            var img = UiKit.Icon(button, Vector2.zero, size); img.name = "codexglow"; img.sprite = Theme.HoverRim; img.preserveAspect = false; img.raycastTarget = false;
            img.enabled = true; // (waves 27-28 merge: UiKit.Icon makes its Image DISABLED until a sprite is set; the aura was never drawn: its object
                                // active and breathing, nothing on screen (found by the merge's render check, wcodex N))
            img.transform.SetAsFirstSibling();
            var g = img.gameObject.AddComponent<CodexGlow>(); g._img = img; g.Update();
            return g;
        }
        private void Update()
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / Period);
            _img.color = new Color(Green.r, Green.g, Green.b, Mathf.Lerp(Low, High, k));
        }
    }

    /// <summary>
    /// Wave 28 (wcodex), design note (2026-10-15): "Add a small flair animation when you add a new monster to the bestiary." A creature NEWLY
    /// written down (by a look or a kill) gets a small "New entry" card at it, the theme's own parchment card (UI/Theme.cs, the job variant
    /// included: UI/JobTheme.cs) with the Codex's book glyph and the creature's name: it pops in (<see cref="PopSeconds"/>, a slight
    /// overshoot), rises <see cref="RiseRadii"/> ring radii and fades out by <see cref="Seconds"/> s. Nothing flashes or covers the view
    /// (FLAGGED numbers: Loki's). Its size follows the ring's (the creature's size in the view), its letters at least
    /// <see cref="MinTextDeg"/>° from the eyes.
    /// </summary>
    public sealed class CodexFlair : MonoBehaviour
    {
        public const float Seconds = 1.3f, PopSeconds = 0.25f, FadeSeconds = 0.4f, RiseRadii = 1.2f, MinTextDeg = 1.2f;
        public const float W = 330f, H = 104f, TitlePx = 30f;
        private System.Func<Camera> _head; private CanvasGroup _group; private Text _name;
        private float _at = -1f, _mpp, _radius; private Vector3 _from; private int _mob;
        /// <summary>Flairs played; the last one's creature (smoke, log).</summary>
        public int Plays { get; private set; }
        public string LastName { get; private set; } = "";
        public bool Playing => gameObject.activeSelf && _at >= 0f;

        public static CodexFlair Create(Transform parent, System.Func<Camera> head)
        {
            var rt = UiKit.Panel("CodexFlair", parent, new Vector2(W, H), 0.001f, false, PanelKind.Hud, PanelLook.Card);
            var f = rt.gameObject.AddComponent<CodexFlair>(); f._head = head;
            f._group = rt.gameObject.AddComponent<CanvasGroup>(); f._group.blocksRaycasts = false; f._group.interactable = false;
            var glyph = UiKit.Icon(rt, new Vector2(-W / 2 + 54f, 0f), new Vector2(72f, 48f));
            var tex = CodexInkRing.BookTexture(); glyph.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f); glyph.preserveAspect = true;
            glyph.enabled = false; // replaced by the 3D miniature ornate leather & gold-tooled Codex Book below
            f._miniBook = MiniCodexBook.Create(rt, new Vector3(-W / 2 + 54f, 0f, -6f), 0.22f);
            var t = UiKit.Label(rt, "New entry", new Vector2(44f, 18f), new Vector2(W - 120f, 40f), (int)TitlePx, TextAnchor.MiddleLeft, Theme.Accent); t.fontStyle = FontStyle.Bold;
            f._name = UiKit.Label(rt, "", new Vector2(44f, -22f), new Vector2(W - 120f, 36f), 26, TextAnchor.MiddleLeft, Theme.Ink);
            f._name.resizeTextForBestFit = true; f._name.resizeTextMinSize = 18; f._name.resizeTextMaxSize = 26;
            rt.gameObject.SetActive(false);
            return f;
        }

        private MiniCodexBook _miniBook;

        /// <summary>Plays at <paramref name="at"/> (the creature's middle), a ring of <paramref name="radius"/> m being its size.</summary>
        public void Play(Vector3 at, float radius, string name, int mob = 0)
        {
            _mob = mob;
            var cam = _head != null ? _head() : null;
            _radius = Mathf.Max(0.03f, radius);
            float dist = cam != null ? Vector3.Distance(cam.transform.position, at) : 1f;
            // as wide as four ring radii, but its title never under MinTextDeg from the eyes
            _mpp = Mathf.Max(4f * _radius / W, dist * Mathf.Tan(MinTextDeg * Mathf.Deg2Rad) / TitlePx);
            _from = at + Vector3.up * (_radius * 1.6f + H * 0.5f * _mpp);
            _name.text = name ?? ""; LastName = _name.text; Plays++;
            _at = Time.unscaledTime;
            gameObject.SetActive(true);
            Place(0f);
        }

        /// <summary>A short line after the name on the card playing for <paramref name="mob"/> (e.g. "+120 EXP": wave 28, wcxp's reward, which
        /// arrives from the server while the card shows); false when no card is playing for it.</summary>
        public bool SetExtra(int mob, string text)
        {
            if (!Playing || mob != _mob || string.IsNullOrEmpty(text)) return false;
            _name.text = LastName + "  <color=#" + Theme.Hex(Theme.Good) + "><b>" + text + "</b></color>";
            return true;
        }

        private void LateUpdate()
        {
            if (_at < 0f) return;
            float t = Time.unscaledTime - _at;
            if (t >= Seconds) { _at = -1f; gameObject.SetActive(false); return; }
            Place(t);
        }

        private void Place(float t)
        {
            float pop = t < PopSeconds ? Mathf.Lerp(0.6f, 1.08f, t / PopSeconds) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((t - PopSeconds) / 0.1f));
            transform.localScale = Vector3.one * _mpp * pop;
            transform.position = _from + Vector3.up * (RiseRadii * _radius * Mathf.Clamp01(t / Seconds));
            var cam = _head != null ? _head() : null; if (cam != null) UiKit.FaceHead(transform, cam);
            _group.alpha = 1f - Mathf.Clamp01((t - (Seconds - FadeSeconds)) / FadeSeconds);
            _miniBook?.Pose(t / Seconds);
        }
    }

    /// <summary>
    /// 3D miniature Codex Book built with the exact ornate gold-tooled leather cover (<see cref="CodexBook.LeatherMaterial"/>),
    /// ribbed gold spine (<see cref="CodexBook.SpineMaterial"/>), and parchment page blocks (<see cref="CodexBook.StackMaterial"/>).
    /// Starts closed showing its gold-tooled leather front cover, then swings open smoothly as it rises.
    /// </summary>
    public sealed class MiniCodexBook
    {
        public readonly Transform Root, HingeL, HingeR;
        private MiniCodexBook(Transform root, Transform hl, Transform hr) { Root = root; HingeL = hl; HingeR = hr; }

        public static MiniCodexBook Create(Transform parent, Vector3 localPos, float scale)
        {
            var root = new GameObject("MiniCodexBook").transform;
            root.gameObject.layer = UiKit.PanelLayer;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.localScale = Vector3.one * scale;

            const float pageW = 140f, pageH = 190f, gutter = 18f, rim = 10f, stack = 5f;
            var leather = CodexBook.LeatherMaterial();
            var spine = CodexBook.SpineMaterial();
            var pages = CodexBook.StackMaterial();

            var hl = new GameObject("hingeL").transform; hl.gameObject.layer = UiKit.PanelLayer; hl.SetParent(root, false);
            var hr = new GameObject("hingeR").transform; hr.gameObject.layer = UiKit.PanelLayer; hr.SetParent(root, false);

            foreach (var (hinge, side) in new[] { (hl, -1), (hr, 1) })
            {
                float cx = side * (gutter * 0.5f + pageW * 0.5f + rim * 0.5f);
                var covSz = new Vector2(pageW + rim + 2f, pageH + 2f * rim);
                Flat(hinge, "cover", leather, new Vector3(cx, 0f, 3f), covSz, 14);
                var back = Flat(hinge, "cover:back", leather, new Vector3(cx, 0f, 3.2f), covSz, 14);
                back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                Flat(hinge, "pageblock", pages, new Vector3(side * (gutter * 0.5f + pageW * 0.5f + stack * 0.5f), -stack * 0.5f, 1.5f), new Vector2(pageW + stack, pageH + stack), 15);
            }
            var spSz = new Vector2(gutter + 24f, pageH + 2f * rim);
            Flat(root, "spine", spine, new Vector3(0f, 0f, 3.5f), spSz, 14);
            var spBack = Flat(root, "spine:back", spine, new Vector3(0f, 0f, 3.7f), spSz, 14);
            spBack.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            AttachQuillAndHalo(root);

            var mb = new MiniCodexBook(root, hl, hr);
            mb.Pose(0f);
            return mb;
        }

        /// <summary>Poses the miniature 3D Codex book along normalized progress <paramref name="u"/> in [0..1]:
        /// starts closed showing the ornate gold-tooled leather front cover, swings open, and scribes across the page with a 3D golden quill.</summary>
        public void Pose(float u)
        {
            if (Root == null) return;
            float openT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.10f) / 0.36f));
            float foldL = Mathf.Lerp(-82f, -18f, openT);
            float foldR = Mathf.Lerp(82f, 18f, openT);
            HingeL.localRotation = Quaternion.Euler(0f, foldL, 0f);
            HingeR.localRotation = Quaternion.Euler(0f, foldR, 0f);
            float tiltY = Mathf.Lerp(-28f, 10f * Mathf.Sin(u * Mathf.PI * 2f), openT);
            Root.localRotation = Quaternion.Euler(-14f, tiltY, 4f * Mathf.Cos(u * Mathf.PI * 2f));
            var quill = Root.Find("quill");
            if (quill != null)
            {
                float writeU = Mathf.Clamp01((u - 0.22f) / 0.68f);
                quill.gameObject.SetActive(u >= 0.16f);
                float sx = Mathf.Lerp(-95f, 95f, Mathf.PingPong(writeU * 2.4f, 1f));
                float sy = Mathf.Lerp(45f, -45f, writeU) + Mathf.Sin(writeU * Mathf.PI * 10f) * 14f;
                quill.localPosition = new Vector3(sx, sy, -22f);
                quill.localRotation = Quaternion.Euler(0f, 0f, -28f + Mathf.Sin(writeU * Mathf.PI * 10f) * 16f);
            }
            var halo = Root.Find("scanHalo");
            if (halo != null)
            {
                halo.localRotation = Quaternion.Euler(0f, 0f, -u * 220f);
                float hs = 0.85f + 0.25f * Mathf.Sin(u * Mathf.PI);
                halo.localScale = new Vector3(240f * hs, 240f * hs, 1f);
            }
        }

        private static Material _quillMat, _haloMat, _bladeMat, _goldMat, _crimsonMat, _emeraldMat;
        internal static Material SolidMat(ref Material cache, Color col)
        {
            if (cache != null) return cache;
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            cache = new Material(sh) { color = col };
            return cache;
        }

        private static MeshRenderer Flat(Transform parent, string name, Material mat, Vector3 pos, Vector2 size, int order)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.layer = UiKit.PanelLayer;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }

        internal static void AttachQuillAndHalo(Transform root)
        {
            var gold = SolidMat(ref _quillMat, new Color(0.98f, 0.84f, 0.32f, 1f));
            var feather = SolidMat(ref _haloMat, new Color(0.35f, 0.95f, 0.72f, 0.85f));
            var halo = Flat(root, "scanHalo", feather, new Vector3(0f, 0f, 6f), new Vector2(240f, 240f), 13).transform;
            var q = new GameObject("quill").transform; q.gameObject.layer = UiKit.PanelLayer; q.SetParent(root, false);
            Flat(q, "nib", gold, new Vector3(0f, -28f, 0f), new Vector2(18f, 56f), 18);
            Flat(q, "vane", feather, new Vector3(12f, 32f, -1f), new Vector2(42f, 96f), 18);
        }
    }

    /// <summary>
    /// Custom Codex Bounty completion &amp; claim fanfare with distinct animations for killing a bounty target vs claiming a bounty reward.
    /// </summary>
    public sealed class CodexBountyFanfare : MonoBehaviour
    {
        public enum Kind { Kill, Claim }
        public const float Seconds = 1.75f, PopSeconds = 0.28f, FadeSeconds = 0.45f, RiseMetres = 0.24f, MinTextDeg = 1.25f;
        public const float W = 380f, H = 116f, TitlePx = 28f;
        private System.Func<Camera> _head;
        private CanvasGroup _group;
        private Text _title, _sub;
        private Transform _killRoot, _claimRoot;
        private Transform _bladeL, _bladeR, _seal, _killRing;
        private Transform _sunburst, _crown;
        private readonly Transform[] _coins = new Transform[6];
        private Kind _kind = Kind.Kill;
        private float _at = -1f, _mpp = 0.001f;
        private Vector3 _from;
        public int Plays { get; private set; }
        public Kind LastKind => _kind;

        private static Material _silverMat, _hiltMat, _sealMat, _ringMat, _crownMat, _coinMat, _rayMat;

        public static CodexBountyFanfare Create(Transform parent, System.Func<Camera> head)
        {
            var rt = UiKit.Panel("CodexBountyFanfare", parent, new Vector2(W, H), 0.001f, false, PanelKind.Hud, PanelLook.Card);
            var f = rt.gameObject.AddComponent<CodexBountyFanfare>(); f._head = head;
            f._group = rt.gameObject.AddComponent<CanvasGroup>(); f._group.blocksRaycasts = false; f._group.interactable = false;
            var iconPos = new Vector3(-W / 2 + 60f, 6f, -8f);
            f.BuildKillEmblem(rt, iconPos);
            f.BuildClaimEmblem(rt, iconPos);
            f._title = UiKit.Label(rt, "Codex Bounty Complete!", new Vector2(48f, 20f), new Vector2(W - 130f, 40f), (int)TitlePx, TextAnchor.MiddleLeft, new Color(0.96f, 0.78f, 0.22f, 1f));
            f._title.fontStyle = FontStyle.Bold;
            f._sub = UiKit.Label(rt, "", new Vector2(48f, -22f), new Vector2(W - 130f, 38f), 25, TextAnchor.MiddleLeft, Theme.Ink);
            f._sub.resizeTextForBestFit = true; f._sub.resizeTextMinSize = 16; f._sub.resizeTextMaxSize = 25;
            rt.gameObject.SetActive(false);
            return f;
        }

        private void BuildKillEmblem(Transform parent, Vector3 pos)
        {
            _killRoot = new GameObject("KillEmblem").transform;
            _killRoot.gameObject.layer = UiKit.PanelLayer;
            _killRoot.SetParent(parent, false);
            _killRoot.localPosition = pos;
            _killRoot.localScale = Vector3.one * 0.28f;

            var silver = MiniCodexBook.SolidMat(ref _silverMat, new Color(0.90f, 0.93f, 0.98f, 1f));
            var hilt = MiniCodexBook.SolidMat(ref _hiltMat, new Color(0.88f, 0.68f, 0.20f, 1f));
            var seal = MiniCodexBook.SolidMat(ref _sealMat, new Color(0.88f, 0.18f, 0.22f, 1f));
            var ring = MiniCodexBook.SolidMat(ref _ringMat, new Color(1.0f, 0.50f, 0.20f, 0.90f));

            _killRing = MakePart(_killRoot, "targetRing", ring, new Vector3(0f, 0f, 4f), new Vector2(210f, 210f), 14);
            _bladeL = BuildBlade(_killRoot, "bladeL", silver, hilt);
            _bladeR = BuildBlade(_killRoot, "bladeR", silver, hilt);
            _seal = new GameObject("waxSeal").transform; _seal.gameObject.layer = UiKit.PanelLayer; _seal.SetParent(_killRoot, false);
            MakePart(_seal, "waxOuter", seal, new Vector3(0f, 0f, -3f), new Vector2(108f, 108f), 18);
            var inner = MakePart(_seal, "waxInner", hilt, new Vector3(0f, 0f, -4f), new Vector2(72f, 72f), 19);
            inner.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private static Transform BuildBlade(Transform parent, string name, Material bladeMat, Material hiltMat)
        {
            var t = new GameObject(name).transform; t.gameObject.layer = UiKit.PanelLayer; t.SetParent(parent, false);
            MakePart(t, "steel", bladeMat, new Vector3(0f, 24f, 0f), new Vector2(24f, 190f), 16);
            MakePart(t, "guard", hiltMat, new Vector3(0f, -54f, -1f), new Vector2(74f, 18f), 17);
            MakePart(t, "grip", hiltMat, new Vector3(0f, -84f, 0f), new Vector2(18f, 48f), 16);
            return t;
        }

        private void BuildClaimEmblem(Transform parent, Vector3 pos)
        {
            _claimRoot = new GameObject("ClaimEmblem").transform;
            _claimRoot.gameObject.layer = UiKit.PanelLayer;
            _claimRoot.SetParent(parent, false);
            _claimRoot.localPosition = pos;
            _claimRoot.localScale = Vector3.one * 0.28f;

            var gold = MiniCodexBook.SolidMat(ref _crownMat, new Color(1.0f, 0.84f, 0.24f, 1f));
            var coin = MiniCodexBook.SolidMat(ref _coinMat, new Color(0.98f, 0.76f, 0.16f, 1f));
            var ray = MiniCodexBook.SolidMat(ref _rayMat, new Color(0.38f, 0.96f, 0.78f, 0.92f));

            _sunburst = new GameObject("sunburst").transform; _sunburst.gameObject.layer = UiKit.PanelLayer; _sunburst.SetParent(_claimRoot, false);
            var r0 = MakePart(_sunburst, "rayA", ray, new Vector3(0f, 0f, 4f), new Vector2(185f, 185f), 14);
            var r1 = MakePart(_sunburst, "rayB", gold, new Vector3(0f, 0f, 3f), new Vector2(165f, 165f), 15);
            r1.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _crown = new GameObject("crown").transform; _crown.gameObject.layer = UiKit.PanelLayer; _crown.SetParent(_claimRoot, false);
            MakePart(_crown, "band", gold, new Vector3(0f, -26f, -2f), new Vector2(132f, 34f), 18);
            MakePart(_crown, "peakC", gold, new Vector3(0f, 18f, -2f), new Vector2(34f, 68f), 18);
            var pL = MakePart(_crown, "peakL", gold, new Vector3(-44f, 8f, -2f), new Vector2(28f, 54f), 18);
            pL.localRotation = Quaternion.Euler(0f, 0f, 16f);
            var pR = MakePart(_crown, "peakR", gold, new Vector3(44f, 8f, -2f), new Vector2(28f, 54f), 18);
            pR.localRotation = Quaternion.Euler(0f, 0f, -16f);

            for (int i = 0; i < _coins.Length; i++)
            {
                _coins[i] = MakePart(_claimRoot, "coin" + i, i % 2 == 0 ? coin : ray, new Vector3(0f, 0f, -5f), new Vector2(36f, 36f), 19);
            }
        }

        private static Transform MakePart(Transform parent, string name, Material mat, Vector3 pos, Vector2 size, int order)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.layer = UiKit.PanelLayer;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return go.transform;
        }

        public void Play(Vector3 at, float radius, string title, string subtitle, Kind kind = Kind.Kill)
        {
            _kind = kind;
            var cam = _head != null ? _head() : null;
            float r = Mathf.Max(0.05f, radius);
            float dist = cam != null ? Vector3.Distance(cam.transform.position, at) : 1.2f;
            _mpp = Mathf.Max(4.2f * r / W, dist * Mathf.Tan(MinTextDeg * Mathf.Deg2Rad) / TitlePx);
            _from = at + Vector3.up * (r * 1.45f + H * 0.5f * _mpp);
            if (_title != null)
            {
                _title.text = title ?? "Codex Bounty";
                _title.color = kind == Kind.Claim ? new Color(0.35f, 0.96f, 0.62f, 1f) : new Color(1.0f, 0.54f, 0.24f, 1f);
            }
            if (_sub != null) _sub.text = subtitle ?? "";
            if (_killRoot != null) _killRoot.gameObject.SetActive(kind == Kind.Kill);
            if (_claimRoot != null) _claimRoot.gameObject.SetActive(kind == Kind.Claim);
            Plays++;
            _at = Time.unscaledTime;
            gameObject.SetActive(true);
            Place(0f);
        }

        private void LateUpdate()
        {
            if (_at < 0f) return;
            float t = Time.unscaledTime - _at;
            if (t >= Seconds) { _at = -1f; gameObject.SetActive(false); return; }
            Place(t);
        }

        private void Place(float t)
        {
            float u = Mathf.Clamp01(t / Seconds);
            if (_kind == Kind.Kill)
            {
                // Sharp hunter's impact stamp + slight recoil shake on the card
                float slam = t < 0.20f ? Mathf.Lerp(1.32f, 0.94f, t / 0.20f) : Mathf.Lerp(0.94f, 1f, Mathf.Clamp01((t - 0.20f) / 0.15f));
                transform.localScale = Vector3.one * _mpp * slam;
                float shakeX = (t > 0.16f && t < 0.36f) ? Mathf.Sin(t * 95f) * 0.012f : 0f;
                transform.position = _from + new Vector3(shakeX, RiseMetres * 0.65f * Mathf.SmoothStep(0f, 1f, u), 0f);
                PoseKill(u);
            }
            else
            {
                // Celebratory buoyant fountain rise with gentle sway
                float pop = t < PopSeconds ? Mathf.Lerp(0.45f, 1.14f, t / PopSeconds) : Mathf.Lerp(1.14f, 1f, Mathf.Clamp01((t - PopSeconds) / 0.18f));
                transform.localScale = Vector3.one * _mpp * pop;
                float swayX = Mathf.Sin(u * Mathf.PI * 2f) * 0.018f;
                transform.position = _from + new Vector3(swayX, RiseMetres * 1.25f * u, 0f);
                PoseClaim(u);
            }
            var cam = _head != null ? _head() : null;
            if (cam != null) UiKit.FaceHead(transform, cam);
            _group.alpha = 1f - Mathf.Clamp01((t - (Seconds - FadeSeconds)) / FadeSeconds);
        }

        private void PoseKill(float u)
        {
            if (_killRoot == null) return;
            float slash = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.24f));
            if (_bladeL != null)
            {
                _bladeL.localPosition = Vector3.Lerp(new Vector3(-90f, 70f, 0f), Vector3.zero, slash);
                _bladeL.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-85f, -42f, slash));
            }
            if (_bladeR != null)
            {
                _bladeR.localPosition = Vector3.Lerp(new Vector3(90f, 70f, 0f), Vector3.zero, slash);
                _bladeR.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(85f, 42f, slash));
            }
            if (_seal != null)
            {
                float stampT = Mathf.Clamp01((u - 0.16f) / 0.18f);
                _seal.gameObject.SetActive(u >= 0.14f);
                float s = stampT < 0.7f ? Mathf.Lerp(2.1f, 0.88f, stampT / 0.7f) : Mathf.Lerp(0.88f, 1.0f, (stampT - 0.7f) / 0.3f);
                _seal.localScale = Vector3.one * s;
                _seal.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-35f, 0f, stampT));
            }
            if (_killRing != null)
            {
                _killRing.localRotation = Quaternion.Euler(0f, 0f, u * 140f);
                float rs = 0.90f + 0.14f * Mathf.Sin(u * Mathf.PI * 3f);
                _killRing.localScale = new Vector3(210f * rs, 210f * rs, 1f);
            }
        }

        private void PoseClaim(float u)
        {
            if (_claimRoot == null) return;
            if (_sunburst != null)
            {
                _sunburst.localRotation = Quaternion.Euler(0f, 0f, -u * 180f);
                float ss = 0.85f + 0.20f * Mathf.Sin(u * Mathf.PI * 2f);
                _sunburst.localScale = Vector3.one * ss;
            }
            if (_crown != null)
            {
                float cRise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.35f));
                _crown.localPosition = new Vector3(0f, Mathf.Lerp(-30f, 10f, cRise) + Mathf.Sin(u * Mathf.PI * 4f) * 8f, -2f);
                _crown.localRotation = Quaternion.Euler(0f, Mathf.Sin(u * Mathf.PI * 2f) * 28f, 0f);
            }
            for (int i = 0; i < _coins.Length; i++)
            {
                var c = _coins[i];
                if (c == null) continue;
                float phase = (u * 1.35f + i * 0.14f) % 1f;
                float ang = (i * 60f + u * 120f) * Mathf.Deg2Rad;
                float rad = Mathf.Lerp(18f, 118f, phase);
                float y = Mathf.Sin(phase * Mathf.PI) * 95f - 10f;
                c.localPosition = new Vector3(Mathf.Cos(ang) * rad, y, -5f);
                c.localRotation = Quaternion.Euler(0f, u * 540f + i * 45f, 45f);
                float cs = Mathf.Sin(phase * Mathf.PI) * 38f;
                c.localScale = new Vector3(cs, cs, 1f);
            }
        }
    }
}
