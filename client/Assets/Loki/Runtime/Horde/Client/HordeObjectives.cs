using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 17 (h17obj): the map objectives as the player sees them (design proposal point 24, "Seen": a pillar of light on the
    /// table, an icon on the minimap, an arrow at the edge of the view, a short banner). The engine's rules are
    /// Horde/Core/HordeSim.Objectives.cs; this draws what the snapshot says (HordeWorldView.Objectives / ObjectiveBuffs):
    ///   - on the table: the circle (a thin ring and a light fill), the claim's ring filling around it (orange while enemies inside slow
    ///     it), a pillar of light in the kind's colour (it blinks its last <see cref="FadeWarnSeconds"/> s), the kind's icon floating in
    ///     it, the guarded chest's Old Violet Box and the Emperium's own sprites, the kind's Ragnarok effect now and then; the pylons'
    ///     four fire walls while they last; a ring under each runner who holds a gift (its arc = the time left);
    ///   - the HUD's arena map (its "minimap", right of the HUD band): the runners, the elites and bosses, each objective as its icon on a
    ///     diamond of its colour, the pylons' square; it turns with the head so that "up" is away from you on the table;
    ///   - an arrow at the edge of the view (head-locked, <see cref="ArrowRingDeg"/>° from the middle) for an objective whose pillar is
    ///     out of view, pointing at it, with its icon;
    ///   - banners: "Shrine: attack speed!" when one appears (wave 17's gift banner is now the text at the runner, below).
    /// Client wave 18 (h18feel), the design's point 26 ("When I pick up a buff, from the ground, as soon as the circle is done counting down, it
    /// should pop up with a small text that quickly explains what it does", in the style "4% Heal/s for 15 seconds"): the gift is said
    /// in that style (<see cref="GiftText"/>) by a small text over MY runner on the table (HordeTag.Gift: it pops in, stays
    /// <see cref="HordeTag.GiftSeconds"/> s, fades), the moment the claim's ring is full; it replaces the gift's HUD banner.
    /// Every size and time here is Loki's own (table scale: 1 cell ≈ 2 cm).
    /// </summary>
    public sealed partial class HordeObjectives // (client wave 18, h18feel: partial: the soul shards are drawn in HordeSouls.cs, point 35)
    {
        /// <summary>The pillar: cells high and wide; the icon's height and size (cells); blink over the last seconds before fading.</summary>
        public const float PillarCells = 9f, PillarWidthCells = 1.1f, IconHeightCells = 3.6f, IconCells = 1.5f, FadeWarnSeconds = 5f;
        /// <summary>The edge arrows: shown when the pillar is more than <see cref="ArrowShowDeg"/>° from the middle of the view, drawn on a
        /// circle <see cref="ArrowRingDeg"/>° from it.</summary>
        public const float ArrowShowDeg = 30f, ArrowRingDeg = 21f, ArrowDistance = 0.65f, ArrowMetresPerPx = 0.0006f;
        /// <summary>The HUD map: its side (HUD pixels) and its middle's place right of the HUD band (HordeHud is 1240 px wide).</summary>
        public const float MapPx = 250f, MapX = 620f + 150f;
        /// <summary>Seconds between two plays of a kind's Ragnarok effect at a standing objective.</summary>
        public const float EffectEvery = 3f;

        private readonly HordeGame _g;
        private Mesh _ground, _upright; private Material _glow, _thin, _dark, _disc, _beam, _beamDark; private Texture2D _beamTex, _thinTex, _discTex;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<Mesh> _arcs = new List<Mesh>();
        private readonly Dictionary<Texture, Material> _iconMats = new Dictionary<Texture, Material>();
        private readonly Dictionary<int, float> _nextEffect = new Dictionary<int, float>();
        // the HUD map
        private RectTransform _map, _mapContent; private readonly List<Image> _dots = new List<Image>(); private readonly List<(RectTransform root, Image diamond, Image icon)> _mapIcons = new List<(RectTransform, Image, Image)>();
        private readonly List<RectTransform> _mapWalls = new List<RectTransform>();
        // the edge arrows
        private RectTransform _arrows; private readonly List<(RectTransform root, Image diamond, Image icon, Image[] chevron)> _arrowItems = new List<(RectTransform, Image, Image, Image[])>();

        // ---- counters for the log and the smoke (client wave 17 h17obj's smoke W17O)
        public int Ups { get; private set; }
        public int Claims { get; private set; }
        public int Fades { get; private set; }
        public int GiftsMine { get; private set; }
        public int PillarsDrawn { get; private set; }
        public int ClaimRingsDrawn { get; private set; }
        public int WallsDrawn { get; private set; }
        public int BuffRingsDrawn { get; private set; }
        public int SpritesDrawn { get; private set; }
        public int EffectsPlayed { get; private set; }
        /// <summary>This frame: arrows shown (and the last one's angle on the view's circle, degrees), map icons shown, the map's turn.</summary>
        public int ArrowsShown { get; private set; }
        public float LastArrowDeg { get; private set; }
        public int MapIconsShown { get; private set; }
        public float MapTurnDeg { get; private set; }
        /// <summary>On the HUD map (its own frame, before the turn): the last objective icon's place and my dot's.</summary>
        public Vector2 LastMapIcon { get; private set; }
        public Vector2 LastMapMe { get; private set; }
        /// <summary>Frames a pillar was drawn blinking (its last seconds before it fades).</summary>
        public int BlinkFrames { get; private set; }
        public string LastBanner { get; private set; } = "";
        public string LastGift { get; private set; } = "";
        /// <summary>Gift texts shown over my runner (client wave 18, h18feel, point 26).</summary>
        public int GiftTexts { get; private set; }
        /// <summary>The HUD map's root and the arrows' panel (the smoke reads where they are).</summary>
        public RectTransform Map => _map;
        public RectTransform Arrows => _arrows;

        public HordeObjectives(HordeGame g) { _g = g; }

        // ------------------------------------------------------------------------------------------------ the kinds

        /// <summary>The kind's colour (pillar, circle, map, arrow): never red, the enemies' warning colour.</summary>
        public static Color KindColor(ObjectiveKind k)
        {
            switch (k)
            {
                case ObjectiveKind.Shrine: return new Color(0.45f, 0.8f, 1f);
                case ObjectiveKind.Well: return new Color(0.45f, 1f, 0.55f);
                case ObjectiveKind.Pylons: return new Color(1f, 0.45f, 0.12f);
                case ObjectiveKind.FlameRing: return new Color(1f, 0.72f, 0.25f);
                case ObjectiveKind.Meteor: return new Color(0.8f, 0.5f, 1f);
                case ObjectiveKind.Chest: return new Color(1f, 0.88f, 0.35f);
                default: return new Color(1f, 1f, 0.92f);
            }
        }

        /// <summary>The Ragnarok skill a kind is drawn with (rAthena's skill_db ids; 0: an item / a monster instead).</summary>
        public static int KindSkill(ObjectiveKind k) => k == ObjectiveKind.Shrine ? 29 : k == ObjectiveKind.Well ? 70 : k == ObjectiveKind.Pylons ? 18 : k == ObjectiveKind.FlameRing ? 10 : k == ObjectiveKind.Meteor ? 83 : 0;
        /// <summary>The Ragnarok item a kind's icon is (Old_Violet_Box 617, Emperium 714: rAthena's item_db; 0: a skill's icon).</summary>
        public static int KindItem(ObjectiveKind k) => k == ObjectiveKind.Chest ? 617 : k == ObjectiveKind.Emperium ? 714 : 0;

        public static Sprite KindIcon(ObjectiveKind k)
        {
            int s = KindSkill(k);
            if (s > 0) return WristMenu.SkillIcon(SkillDb.Get(s));
            var it = GameDb.GetItem(KindItem(k));
            return it != null ? WristMenu.ItemIcon(it) : null;
        }

        /// <summary>The banner when one appears (item 17: a few plain words).</summary>
        public static string UpText(ObjectiveKind k)
        {
            switch (k)
            {
                case ObjectiveKind.Shrine: return Lang.T("Shrine: attack speed!");
                case ObjectiveKind.Well: return Lang.T("Well: healing!");
                case ObjectiveKind.Pylons: return Lang.T("Pylons: fire walls!");
                case ObjectiveKind.FlameRing: return Lang.T("Flame ring!");
                case ObjectiveKind.Meteor: return Lang.T("Meteor beacon!");
                case ObjectiveKind.Chest: return Lang.T("Guarded chest!");
                default: return Lang.T("Defend the Emperium!");
            }
        }

        /// <summary>What I got, in the design's style (client wave 18, h18feel, point 26: the effect and its numbers, compact, no sentence: "4% Heal/s
        /// for 15 seconds"); the numbers are the rules' (<paramref name="seconds"/>: the engine's ObjectiveGot event says how long).</summary>
        public static string GiftText(ObjectiveKind k, int seconds, ObjectiveRulesDef r)
        {
            if (r == null) r = new ObjectiveRulesDef();
            switch (k)
            {
                case ObjectiveKind.Shrine: return Lang.F("+{0}% Attack speed for {1} seconds", Mathf.RoundToInt((100f / Mathf.Max(1f, 100f + r.ShrineCooldownPct) - 1f) * 100f), seconds);
                case ObjectiveKind.Well: return Lang.F("{0}% Heal/s for {1} seconds", r.WellHealPct, seconds);
                case ObjectiveKind.Pylons: return Lang.F("Fire walls burn enemies for {0} seconds", seconds);
                case ObjectiveKind.FlameRing: return Lang.F("{0} fire balls circle you for {1} seconds", r.FlameRingWeapon != null && r.FlameRingWeapon.Levels.Length > 0 ? r.FlameRingWeapon.Levels[0].Count : 3, seconds);
                case ObjectiveKind.Meteor: return Lang.F("{0} meteors on the biggest crowds", r.MeteorCount);
                case ObjectiveKind.Chest: return Lang.F("+{0} upgrades", r.ChestUpgrades);
                default: return Lang.F("+{0} upgrades and an EXP magnet", r.EmperiumUpgrades);
            }
        }

        // ------------------------------------------------------------------------------------------------ the run

        public void OnRunStarted(HordeRunInfo run)
        {
            _nextEffect.Clear(); LastBanner = ""; LastGift = ""; _pendingBanner = null;
            ClearRises(); // (client wave 19, h18feel: no speed trail or rise banner left from the run before; HordeSouls.cs)
            EnsureHud();
            // the sprites drawn at objectives: built ahead (h9feel's pre-build), never as a hitch at their first appearance
            var r = _g.Content?.Objectives;
            if (r != null && _g.Sprites != null) { _g.Sprites.QueueKind(r.EmperiumMobId); _g.Sprites.QueueItem(r.ChestItemId); }
        }

        public void OnLeave()
        {
            if (_arrows != null) _arrows.gameObject.SetActive(false);
            ArrowsShown = 0; MapIconsShown = 0;
        }

        /// <summary>The engine's objective events: the banners, a little rumble when I got a gift.</summary>
        public void OnEvent(HordeEvent e, int mySlot)
        {
            var k = (ObjectiveKind)e.DefId; var r = _g.Content?.Objectives;
            switch (e.Kind)
            {
                case HordeEventKind.ObjectiveUp:
                    Ups++;
                    // the guarded chest's guard arrives in the same tick with its own "elite" banner: the chest's banner follows it
                    if (k == ObjectiveKind.Chest) { _pendingBanner = UpText(k); _pendingColor = KindColor(k); _pendingAt = Time.unscaledTime + ChestBannerAfter; }
                    else Banner(UpText(k), 2f, KindColor(k));
                    Debug.Log($"[Horde] objective {e.Target} up: {k} at {e.X:0.0},{e.Y:0.0} ({e.Value} s)");
                    break;
                case HordeEventKind.ObjectiveClaimed:
                    Claims++;
                    PlayKindEffect(k, e.X, e.Y, true);
                    Debug.Log($"[Horde] objective {e.Target} ({k}) claimed by slot {e.Slot}; {e.Value} player(s) got it");
                    break;
                case HordeEventKind.ObjectiveGot:
                    if (e.Slot != mySlot) break;
                    GiftsMine++; LastGift = GiftText(k, e.Value, r);
                    // client wave 18 (h18feel), point 26: said at my runner on the table (not a HUD banner any more)
                    var tag = _g.Players?.TagOf(mySlot);
                    if (tag != null) { tag.Gift(LastGift, Color.Lerp(KindColor(k), Color.white, 0.35f)); GiftTexts++; }
                    Haptics.Both(Haptics.Kind.LevelUp, 0.4f, 0.12f);
                    break;
                case HordeEventKind.ObjectiveFaded:
                    Fades++;
                    if (k == ObjectiveKind.Emperium && e.Value != 0) Banner(Lang.T("Emperium lost"), 1.6f, new Color(1f, 0.75f, 0.6f));
                    Debug.Log($"[Horde] objective {e.Target} ({k}) {(e.Value != 0 ? "lost" : "faded")}");
                    break;
                case HordeEventKind.EmperiumWave:
                    if (_g.Arena?.Root != null) World.Fx.Burst(_g.Arena.Root, _g.Arena.Local(e.X, e.Y) + Vector3.up * 0.8f, KindColor(ObjectiveKind.Emperium), 1.6f, 18);
                    break;
            }
        }

        /// <summary>Seconds the guarded chest's banner waits after its guard's own "elite" banner.</summary>
        public const float ChestBannerAfter = 1.5f;
        private string _pendingBanner; private Color _pendingColor; private float _pendingAt;

        private void Banner(string text, float seconds, Color c) { LastBanner = text; _g.Hud?.ShowBanner(text, seconds, Color.Lerp(c, Color.white, 0.25f), 50); }

        /// <summary>The kind's Ragnarok effect at a place (its skill's effect by EffectDb), at most every <see cref="EffectEvery"/> s per place.</summary>
        private void PlayKindEffect(ObjectiveKind k, float x, float y, bool now)
        {
            var fx = _g.Fx; int skill = KindSkill(k);
            if (fx == null || skill <= 0 || _g.Arena?.Root == null || fx.LiveCount > 80) return;
            var ids = Effects.EffectDb.EffectIdFor(skill, -1);
            if (ids == null || ids.Length == 0 || Effects.EffectDb.Effect(ids[0]) == null) return;
            if (fx.Spam(ids[0], 0, _g.Arena.Local(x, y), Time.time, -1f) > 0) EffectsPlayed++;
        }

        // ------------------------------------------------------------------------------------------------ every frame

        public void Draw(HordeWorldView w, HordeContent c, double now, int mySlot, Vector2 myPos)
        {
            PillarsDrawn = 0; ClaimRingsDrawn = 0; WallsDrawn = 0; BuffRingsDrawn = 0; SpritesDrawn = 0;
            if (_pendingBanner != null && Time.unscaledTime >= _pendingAt) { Banner(_pendingBanner, 2f, _pendingColor); _pendingBanner = null; }
            var root = _g.Arena != null ? _g.Arena.Root : null; var cam = Camera.main;
            if (w == null || c == null || c.Objectives == null) return;
            _soulMyPos = myPos; // (client wave 18, h18feel: the nearest shards' arrows, point 35)
            if (root != null && cam != null) DrawTable(w, c, now, mySlot, myPos, root, cam);
            UpdateMap(w, c, now, mySlot, myPos, root);
            UpdateArrows(w, c, now, root);
        }

        private void DrawTable(HordeWorldView w, HordeContent c, double now, int mySlot, Vector2 myPos, Transform root, Camera cam)
        {
            Ensure();
            var r = c.Objectives; float k = Mathf.Abs(root.lossyScale.x), t = Time.time; var camPos = cam.transform.position;
            float tick = w.DrawTick(now), rate = Mathf.Max(1, w.TickRate); float a = w.Alpha(now);
            int arc = 0;
            foreach (var o in w.Objectives)
            {
                var col = KindColor(o.Kind);
                var centre = root.TransformPoint(_g.Arena.Local(o.X, o.Y));
                if (o.State == ObjectiveState.Active) { DrawPylons(o, r, root, k, t); continue; }
                // the circle, and the claim's ring around it (orange while enemies inside slow it)
                float left = (o.EndTick - tick) / rate;
                float blink = o.State == ObjectiveState.Up && left < FadeWarnSeconds ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 7f)) : 1f;
                if (o.State == ObjectiveState.Up && left < FadeWarnSeconds) BlinkFrames++;
                float pulse = 0.85f + 0.15f * Mathf.Sin(t * 3f + o.Id);
                var fill = Color.Lerp(col, new Color(0.20f, 0.96f, 0.52f), 0.35f); fill.a = 0.14f * blink; Disc(centre, 2f * o.Radius * k, fill, root);
                var rim = col; rim.a = 0.95f * blink * pulse; Ring(centre, 2f * o.Radius * k, rim, root);
                var innerRim = Color.Lerp(col, new Color(0.35f, 1f, 0.65f), 0.55f); innerRim.a = 0.65f * blink * pulse;
                Ring(centre, 2f * o.Radius * 0.72f * k, innerRim, root);
                if (o.Progress > 0.001f)
                {
                    while (_arcs.Count <= arc) _arcs.Add(new Mesh { name = "horde-objective-arc" });
                    var ac = o.Crowd > 0 ? new Color(1f, 0.6f, 0.2f, 1f) : Color.Lerp(col, Color.white, 0.35f);
                    BuildArc(_arcs[arc], o.Radius * 1.02f, o.Radius * 1.22f, o.Progress, ac);
                    _mpb.SetColor(ColorId, Color.white);
                    Graphics.DrawMesh(_arcs[arc], Matrix4x4.TRS(centre + root.up * (0.09f * k), root.rotation, Vector3.one * k), _glow, 0, null, 0, _mpb, false, false);
                    arc++; ClaimRingsDrawn++;
                }
                // the pillar of light, and the kind's icon in it
                var flat = centre - camPos; flat -= root.up * Vector3.Dot(flat, root.up);
                var face = flat.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(flat.normalized, root.up) : root.rotation;
                // a dark shadow behind it (it reads on bright ground too), its colour (alpha-blended), then a glowing core
                _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.35f * blink));
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(PillarWidthCells * 1.7f * k, PillarCells * 1.02f * k, 1f)), _beamDark, 0, null, 0, _mpb, false, false);
                var beam = col; beam.a = (o.State == ObjectiveState.Defending ? 0.85f : 0.7f) * blink * pulse;
                _mpb.SetColor(ColorId, beam);
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(PillarWidthCells * k, PillarCells * k, 1f)), _beamDark, 0, null, 0, _mpb, false, false);
                _mpb.SetColor(ColorId, beam);
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(PillarWidthCells * k, PillarCells * k, 1f)), _beam, 0, null, 0, _mpb, false, false);
                _mpb.SetColor(ColorId, new Color(1f, 1f, 1f, 0.6f * blink));
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(PillarWidthCells * 0.35f * k, PillarCells * 0.8f * k, 1f)), _beam, 0, null, 0, _mpb, false, false);
                PillarsDrawn++;
                var icon = KindIcon(o.Kind);
                if (icon != null && icon.texture != null)
                {
                    float bob = 0.15f * Mathf.Sin(t * 2f + o.Id);
                    _mpb.SetColor(ColorId, new Color(1f, 1f, 1f, blink));
                    Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre + root.up * ((IconHeightCells + bob) * k), face, new Vector3(IconCells * k, IconCells * k, 1f)), IconMaterial(icon.texture), 0, null, 0, _mpb, false, false);
                }
                // the chest's box and the Emperium themselves (the install's sprites)
                var sp = _g.Sprites;
                if (sp != null && (o.Kind == ObjectiveKind.Chest || o.Kind == ObjectiveKind.Emperium))
                {
                    var body = o.Kind == ObjectiveKind.Chest ? sp.Item(r.ChestItemId) : sp.Mob(r.EmperiumMobId);
                    var f = sp.Get(body, 0, 0, t * 1000f, true);
                    if (f != null)
                    {
                        _mpb.SetColor(ColorId, Color.white);
                        Graphics.DrawMesh(f.Mesh, HordeSprites.Matrix(f, centre, camPos, k, o.Kind == ObjectiveKind.Chest ? 1.6f : 1f), sp.MaterialFor(f.Texture), 0, null, 0, _mpb, false, true);
                        SpritesDrawn++;
                    }
                }
                // the kind's Ragnarok effect now and then
                if (!_nextEffect.TryGetValue(o.Id, out float next) || t >= next) { _nextEffect[o.Id] = t + EffectEvery; PlayKindEffect(o.Kind, o.X, o.Y, false); }
            }
            // a ring under each runner who holds a gift: its arc is the time left
            foreach (var b in w.ObjectiveBuffs)
            {
                if (b.Slot < 0 || b.Slot >= w.Players.Length || !w.Players[b.Slot].Present) continue;
                var p = w.Players[b.Slot];
                var pos = b.Slot == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                var at = root.TransformPoint(_g.Arena.Local(pos.x, pos.y));
                float total = b.Kind == ObjectiveKind.Shrine ? c.Objectives.ShrineSeconds : b.Kind == ObjectiveKind.Well ? c.Objectives.WellSeconds : c.Objectives.FlameRingSeconds;
                float share = Mathf.Clamp01((b.Until - tick) / rate / Mathf.Max(1f, total));
                while (_arcs.Count <= arc) _arcs.Add(new Mesh { name = "horde-objective-arc" });
                var col = KindColor(b.Kind); col.a = 0.9f;
                BuildArc(_arcs[arc], 0.8f, 1.05f, share, col);
                _mpb.SetColor(ColorId, Color.white);
                Graphics.DrawMesh(_arcs[arc], Matrix4x4.TRS(at + root.up * (0.07f * k), root.rotation, Vector3.one * k), _glow, 0, null, 0, _mpb, false, false);
                arc++; BuffRingsDrawn++;
            }
            DrawSouls(w, mySlot, myPos, root, cam, k, t, ref arc); // client wave 18 (h18feel), point 35: the soul shards (HordeSouls.cs)
        }

        /// <summary>The pylons' four fire walls (and a pylon at each corner) while they last; they flicker and die down in the last 2 s.</summary>
        private void DrawPylons(HordeObjectiveView o, ObjectiveRulesDef r, Transform root, float k, float t)
        {
            var run = _g.Run; if (run == null) return;
            ObjectiveRulesDef.PylonSquare(o.X, o.Y, r.PylonHalfCells, run.X0, run.Y0, run.X1 + 1f, run.Y1 + 1f, out float x0, out float y0, out float x1, out float y1);
            var w = _g.Channel?.World; float tick = w != null ? w.DrawTick(_g.Channel.Now) : 0f, left = w != null ? (o.EndTick - tick) / Mathf.Max(1, w.TickRate) : 9f;
            float fade = Mathf.Clamp01(left / 2f);
            var corners = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            var col = KindColor(ObjectiveKind.Pylons);
            for (int s = 0; s < 4; s++)
            {
                var a = root.TransformPoint(_g.Arena.Local(corners[s].x, corners[s].y)); var b = root.TransformPoint(_g.Arena.Local(corners[(s + 1) % 4].x, corners[(s + 1) % 4].y));
                var d = b - a; float len = d.magnitude; if (len < 1e-5f) continue;
                var dir = d / len; var rot = Quaternion.LookRotation(Vector3.Cross(dir, root.up), root.up);
                var mid = (a + b) * 0.5f;
                float flick = 0.75f + 0.25f * Mathf.Sin(t * 13f + s * 1.7f) * Mathf.Sin(t * 7.3f + s);
                var c1 = col; c1.a = 0.8f * flick * fade;
                _mpb.SetColor(ColorId, c1);
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(mid, rot, new Vector3(len, 1.3f * k * (0.85f + 0.15f * flick), 1f)), _beam, 0, null, 0, _mpb, false, false);
                var c2 = new Color(1f, 0.85f, 0.4f, 0.55f * fade);
                _mpb.SetColor(ColorId, c2);
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(mid, rot, new Vector3(len, 0.7f * k, 1f)), _beam, 0, null, 0, _mpb, false, false);
                var band = col; band.a = 0.35f * fade;
                _mpb.SetColor(ColorId, band);
                Graphics.DrawMesh(_ground, Matrix4x4.TRS(mid + root.up * (0.05f * k), rot, new Vector3(len, 1f, 1.5f * r.PylonBandCells * k)), _disc, 0, null, 0, _mpb, false, false);
                // the pylon at this corner
                var face = Quaternion.LookRotation(Vector3.Cross(root.right, root.up), root.up);
                var cam = Camera.main;
                if (cam != null) { var fl = a - cam.transform.position; fl -= root.up * Vector3.Dot(fl, root.up); if (fl.sqrMagnitude > 1e-8f) face = Quaternion.LookRotation(fl.normalized, root.up); }
                _mpb.SetColor(ColorId, new Color(col.r, col.g, col.b, 0.9f * fade));
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(a, face, new Vector3(0.6f * k, 2.4f * k, 1f)), _beam, 0, null, 0, _mpb, false, false);
                WallsDrawn++;
            }
        }

        // ------------------------------------------------------------------------------------------------ the HUD's arena map

        private void EnsureHud()
        {
            var hud = _g.Hud != null ? _g.Hud.transform as RectTransform : null;
            if (_map == null && hud != null)
            {
                // client wave 21 (w21horde), THE PANEL THEME: the map's frame is the theme's see-through one (the gold line and the corner
                // scrolls) in place of the dark box behind it; it stands around the arena's ground and TURNS with it (the dark box
                // stood still and the turned ground's corners stood out of it); the ground stays, as dark as the two layers were
                // together (waves 17-20: 55 % over the box's 78 % = 90 %), so every dot reads on it as before
                _map = UiKit.Box(hud, "objective-map", new Vector2(MapX, 0f), new Vector2(MapPx + 10f, MapPx + 10f), new Color(1, 1, 1, 0)); _map.GetComponent<Image>().enabled = false;
                var inner = UiKit.Box(_map, "arena", Vector2.zero, new Vector2(MapPx - 10f, MapPx - 10f), MapGround);
                HordeLook.SeeFrame(inner, "th:mapframe", Vector2.zero, new Vector2(MapPx + 10f, MapPx + 10f), HordeHud.PxMetres);
                _mapContent = inner;
            }
            if (_arrows == null)
            {
                var head = LokiBootstrap.Instance != null && LokiBootstrap.Instance.Rig != null ? LokiBootstrap.Instance.Rig.Head : Camera.main;
                if (head != null)
                {
                    _arrows = UiKit.Panel("HordeObjectiveArrows", head.transform, new Vector2(1200, 1200), ArrowMetresPerPx, false, PanelKind.Hud);
                    _arrows.localPosition = new Vector3(0f, 0f, ArrowDistance); _arrows.localRotation = Quaternion.identity;
                    _arrows.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Client wave 21 (w21horde): the HUD map's ground: the old arena tint (0.2, 0.22, 0.18 at 55 %) over the old dark box
        /// (0.05, 0.06, 0.09 at 78 %) as ONE layer: the same colour and the same 90 % behind the dots.</summary>
        public static readonly Color MapGround = new Color(0.142f, 0.158f, 0.145f, 0.90f);
        /// <summary>Client wave 21 (w21horde): the thin gold edge behind an objective's diamond (the map's and an arrow's): this much larger, px.</summary>
        public const float DiamondEdgePx = 6f;

        private Image MapDot(int i)
        {
            while (_dots.Count <= i) _dots.Add(UiKit.Box(_mapContent, "dot", Vector2.zero, new Vector2(10, 10), Color.white).GetComponent<Image>());
            return _dots[i];
        }

        private (RectTransform root, Image diamond, Image icon) MapIcon(int i)
        {
            while (_mapIcons.Count <= i)
            {
                var rt = UiKit.Box(_mapContent, "objective", Vector2.zero, new Vector2(44, 44), new Color(0, 0, 0, 0));
                var edge = UiKit.Box(rt, "th:edge", Vector2.zero, new Vector2(34 + DiamondEdgePx, 34 + DiamondEdgePx), HordeLook.Edge); edge.localRotation = Quaternion.Euler(0, 0, 45f); // (client wave 21: the theme's thin gold edge)
                var dia = UiKit.Box(rt, "diamond", Vector2.zero, new Vector2(34, 34), Color.white); dia.localRotation = Quaternion.Euler(0, 0, 45f);
                var ic = UiKit.Icon(rt, Vector2.zero, new Vector2(30, 30));
                _mapIcons.Add((rt, dia.GetComponent<Image>(), ic));
            }
            return _mapIcons[i];
        }

        private void UpdateMap(HordeWorldView w, HordeContent c, double now, int mySlot, Vector2 myPos, Transform root)
        {
            MapIconsShown = 0;
            var run = _g.Run;
            if (_map == null || run == null) return;
            float cx = (run.X0 + run.X1 + 1) * 0.5f, cy = (run.Y0 + run.Y1 + 1) * 0.5f, span = Mathf.Max(run.X1 - run.X0 + 1, run.Y1 - run.Y0 + 1), side = MapPx - 10f;
            Vector2 P(float x, float y) => new Vector2((x - cx) / span * side, (y - cy) / span * side);
            // turned with the head: "up" on the map is away from me on the table
            var head = LokiBootstrap.Instance != null && LokiBootstrap.Instance.Rig != null ? LokiBootstrap.Instance.Rig.Head : Camera.main;
            float turn = 0f;
            if (head != null && root != null)
            {
                var north = root.TransformDirection(Vector3.forward); var fwd = head.transform.forward;
                north -= root.up * Vector3.Dot(north, root.up); fwd -= root.up * Vector3.Dot(fwd, root.up);
                if (north.sqrMagnitude > 1e-6f && fwd.sqrMagnitude > 1e-6f) turn = Vector3.SignedAngle(north, fwd, root.up);
            }
            MapTurnDeg = turn; _mapContent.localRotation = Quaternion.Euler(0, 0, turn);
            int used = 0; float a = w.Alpha(now), tick = w.DrawTick(now), rate = Mathf.Max(1, w.TickRate), t = Time.time;
            // the pylons' square while it lasts
            int walls = 0; var r = c.Objectives;
            foreach (var o in w.Objectives)
            {
                if (o.State != ObjectiveState.Active) continue;
                ObjectiveRulesDef.PylonSquare(o.X, o.Y, r.PylonHalfCells, run.X0, run.Y0, run.X1 + 1f, run.Y1 + 1f, out float x0, out float y0, out float x1, out float y1);
                var p0 = P(x0, y0); var p1 = P(x1, y1); var col = KindColor(ObjectiveKind.Pylons);
                (Vector2 pos, Vector2 size)[] sides = { (new Vector2((p0.x + p1.x) / 2, p0.y), new Vector2(p1.x - p0.x, 3)), (new Vector2((p0.x + p1.x) / 2, p1.y), new Vector2(p1.x - p0.x, 3)), (new Vector2(p0.x, (p0.y + p1.y) / 2), new Vector2(3, p1.y - p0.y)), (new Vector2(p1.x, (p0.y + p1.y) / 2), new Vector2(3, p1.y - p0.y)) };
                foreach (var s in sides)
                {
                    while (_mapWalls.Count <= walls) _mapWalls.Add(UiKit.Box(_mapContent, "wall", Vector2.zero, Vector2.one, col));
                    var wr = _mapWalls[walls++]; wr.gameObject.SetActive(true); wr.anchoredPosition = s.pos; wr.sizeDelta = s.size; wr.GetComponent<Image>().color = col;
                }
            }
            for (int i = walls; i < _mapWalls.Count; i++) if (_mapWalls[i].gameObject.activeSelf) _mapWalls[i].gameObject.SetActive(false);
            // elites and bosses (red), allies (cyan), me (white on a dark ring)
            foreach (var e in w.Enemies)
            {
                if (!e.Alive) continue; var d = c.Enemy(e.DefId); if (d == null || d.Rank < EnemyRank.Elite) continue;
                Dot(ref used, P(e.PrevX + (e.X - e.PrevX) * a, e.PrevY + (e.Y - e.PrevY) * a), d.Rank >= EnemyRank.Boss ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.6f, 0.3f), d.Rank >= EnemyRank.Boss ? 16 : 12);
            }
            foreach (var p in w.Players)
            {
                if (!p.Present || p.Slot == mySlot) continue;
                Dot(ref used, P(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a), p.Fallen ? new Color(0.6f, 0.6f, 0.65f) : new Color(0.45f, 0.9f, 1f), 12);
            }
            var me = P(myPos.x, myPos.y); LastMapMe = me;
            Dot(ref used, me, Color.black, 18); Dot(ref used, me, Color.white, 12);
            for (int i = used; i < _dots.Count; i++) if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);
            // the objectives: their icon on a diamond of their colour (upright whatever the map's turn; blinking before they fade)
            int n = 0;
            foreach (var o in w.Objectives)
            {
                if (o.State == ObjectiveState.Active) continue;
                var (rt, dia, ic) = MapIcon(n++);
                rt.gameObject.SetActive(true); rt.anchoredPosition = P(o.X, o.Y); rt.localRotation = Quaternion.Euler(0, 0, -turn); rt.SetAsLastSibling(); LastMapIcon = rt.anchoredPosition;
                rt.localScale = Vector3.one; // (client wave 18, h18feel: a shard's icon of the same pool is smaller)
                float left = (o.EndTick - tick) / rate;
                float blink = o.State == ObjectiveState.Up && left < FadeWarnSeconds ? 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 7f)) : 1f;
                var col = KindColor(o.Kind); col.a = blink; dia.color = col;
                var sp = KindIcon(o.Kind); ic.sprite = sp; ic.enabled = sp != null; ic.color = new Color(1, 1, 1, blink);
                MapIconsShown++;
            }
            n = MapSouls(w, n, P, turn, mySlot); // client wave 18 (h18feel), point 35: the soul shards on the HUD map (HordeSouls.cs)
            for (int i = n; i < _mapIcons.Count; i++) if (_mapIcons[i].root.gameObject.activeSelf) _mapIcons[i].root.gameObject.SetActive(false);
        }

        private void Dot(ref int used, Vector2 p, Color c, float size)
        {
            float half = (MapPx - 10f) * 0.5f;
            p.x = Mathf.Clamp(p.x, -half, half); p.y = Mathf.Clamp(p.y, -half, half);
            var d = MapDot(used++); d.gameObject.SetActive(true); d.color = c;
            var rt = (RectTransform)d.transform; rt.anchoredPosition = p; rt.sizeDelta = new Vector2(size, size); rt.SetAsLastSibling();
        }

        // ------------------------------------------------------------------------------------------------ the arrows at the edge of the view

        private void UpdateArrows(HordeWorldView w, HordeContent c, double now, Transform root)
        {
            ArrowsShown = 0;
            if (_arrows == null) return;
            var head = _arrows.parent; int n = 0;
            if (head != null && root != null && _g.InArena)
            {
                float ringPx = Mathf.Tan(ArrowRingDeg * Mathf.Deg2Rad) * ArrowDistance / ArrowMetresPerPx, t = Time.time;
                float k = Mathf.Abs(root.lossyScale.x);
                foreach (var o in w.Objectives)
                {
                    if (o.State == ObjectiveState.Active) continue;
                    var pos = root.TransformPoint(_g.Arena.Local(o.X, o.Y)) + root.up * (PillarCells * 0.4f * k);
                    var to = pos - head.position;
                    if (Vector3.Angle(head.forward, to) <= ArrowShowDeg) continue; // the pillar is in view
                    var local = head.InverseTransformDirection(to);
                    var dir = new Vector2(local.x, local.y);
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down; // straight behind: point down
                    dir.Normalize();
                    float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    while (_arrowItems.Count <= n) _arrowItems.Add(MakeArrow());
                    var (rt, dia, ic, chev) = _arrowItems[n++];
                    rt.gameObject.SetActive(true); rt.anchoredPosition = dir * ringPx; rt.localRotation = Quaternion.Euler(0, 0, deg - 90f);
                    var col = KindColor(o.Kind); dia.color = col;
                    ic.transform.localRotation = Quaternion.Euler(0, 0, 90f - deg); // the icon stays upright
                    var sp = KindIcon(o.Kind); ic.sprite = sp; ic.enabled = sp != null;
                    float pulse = 0.75f + 0.25f * Mathf.Sin(t * 5f);
                    foreach (var img in chev) img.color = new Color(Mathf.Lerp(col.r, 1f, 0.3f), Mathf.Lerp(col.g, 1f, 0.3f), Mathf.Lerp(col.b, 1f, 0.3f), pulse);
                    LastArrowDeg = deg; ArrowsShown++;
                }
                n = SoulArrows(w, head, root, n, ringPx, t, _soulMyPos); // client wave 18 (h18feel), point 35: the nearest shards out of view (HordeSouls.cs)
            }
            for (int i = n; i < _arrowItems.Count; i++) if (_arrowItems[i].root.gameObject.activeSelf) _arrowItems[i].root.gameObject.SetActive(false);
            bool any = n > 0;
            if (_arrows.gameObject.activeSelf != any) _arrows.gameObject.SetActive(any);
        }

        /// <summary>One arrow: a chevron pointing outward (two bars), the kind's diamond and icon behind it.</summary>
        private (RectTransform, Image, Image, Image[]) MakeArrow()
        {
            var rt = UiKit.Box(_arrows, "arrow", Vector2.zero, new Vector2(110, 130), new Color(0, 0, 0, 0));
            var lb = UiKit.Box(rt, "rim", new Vector2(-17, 34), new Vector2(60, 19), new Color(0.03f, 0.03f, 0.05f, 0.7f)); lb.localRotation = Quaternion.Euler(0, 0, 40f);
            var rb = UiKit.Box(rt, "rim", new Vector2(17, 34), new Vector2(60, 19), new Color(0.03f, 0.03f, 0.05f, 0.7f)); rb.localRotation = Quaternion.Euler(0, 0, -40f);
            var edge = UiKit.Box(rt, "th:edge", new Vector2(0, -22), new Vector2(50 + DiamondEdgePx, 50 + DiamondEdgePx), HordeLook.Edge); edge.localRotation = Quaternion.Euler(0, 0, 45f); // (client wave 21: the theme's thin gold edge)
            var dia = UiKit.Box(rt, "diamond", new Vector2(0, -22), new Vector2(50, 50), Color.white); dia.localRotation = Quaternion.Euler(0, 0, 45f);
            var ic = UiKit.Icon(rt, new Vector2(0, -22), new Vector2(42, 42));
            var l = UiKit.Box(rt, "chevron", new Vector2(-17, 34), new Vector2(54, 13), Color.white); l.localRotation = Quaternion.Euler(0, 0, 40f);
            var r = UiKit.Box(rt, "chevron", new Vector2(17, 34), new Vector2(54, 13), Color.white); r.localRotation = Quaternion.Euler(0, 0, -40f);
            return (rt, dia.GetComponent<Image>(), ic, new[] { l.GetComponent<Image>(), r.GetComponent<Image>() });
        }

        // ------------------------------------------------------------------------------------------------ meshes and materials

        private void Ensure()
        {
            if (_ground != null) return;
            _ground = new Mesh { name = "horde-objective-ground" };
            _ground.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _ground.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _ground.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _ground.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _ground.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            _upright = new Mesh { name = "horde-objective-upright" }; // a quad standing on its bottom edge (x across, y up)
            _upright.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
            _upright.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _upright.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _upright.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _upright.bounds = new Bounds(new Vector3(0, 0.5f, 0), Vector3.one * 2f);
            _beamTex = new Texture2D(32, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-objective-beam" };
            for (int y = 0; y < 128; y++) for (int x = 0; x < 32; x++)
            {
                float u = (x + 0.5f) / 32f - 0.5f, v = (y + 0.5f) / 128f;
                float across = Mathf.Exp(-u * u * 14f), up = Mathf.Pow(1f - v, 0.8f) * Mathf.Clamp01(v * 12f);
                _beamTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(across * up)));
            }
            _beamTex.Apply(true);
            _thinTex = new Texture2D(128, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-objective-ring" };
            _discTex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "horde-objective-disc" };
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                _thinTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - 0.94f) * 24f)));
            }
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32f;
                _discTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.98f - d) * 10f)));
            }
            _thinTex.Apply(true); _discTex.Apply(true);
            _glow = new Material(World.Fx.Glow) { mainTexture = World.Fx.DotTexture(), name = "horde-objective-glow" };
            _beam = new Material(World.Fx.Glow) { mainTexture = _beamTex, name = "horde-objective-beam" };
            _beamDark = new Material(Effects.EffectManager.StrShader) { mainTexture = _beamTex, name = "horde-objective-beam-blend" };
            _beamDark.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _beamDark.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _thin = new Material(World.Fx.Glow) { mainTexture = _thinTex, name = "horde-objective-ring" };
            _dark = new Material(Effects.EffectManager.StrShader) { mainTexture = _thinTex, name = "horde-objective-dark" };
            _dark.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _dark.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _disc = new Material(Effects.EffectManager.StrShader) { mainTexture = _discTex, name = "horde-objective-disc" };
            _disc.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _disc.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        private Material IconMaterial(Texture tex)
        {
            if (_iconMats.TryGetValue(tex, out var m) && m != null) return m;
            m = new Material(Effects.EffectManager.StrShader) { mainTexture = tex, name = "horde-objective-icon:" + tex.name };
            m.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _iconMats[tex] = m;
            return m;
        }

        private void Ring(Vector3 world, float diameter, Color c, Transform root)
        {
            float up = 0.06f * Mathf.Abs(root.lossyScale.x);
            _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.65f * c.a));
            Graphics.DrawMesh(_ground, Matrix4x4.TRS(world + root.up * (up * 0.8f), root.rotation, Vector3.one * (diameter * 1.05f)), _dark, 0, null, 0, _mpb, false, false);
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_ground, Matrix4x4.TRS(world + root.up * up, root.rotation, Vector3.one * diameter), _thin, 0, null, 0, _mpb, false, false);
        }

        private void Disc(Vector3 world, float diameter, Color c, Transform root)
        {
            _mpb.SetColor(ColorId, c);
            Graphics.DrawMesh(_ground, Matrix4x4.TRS(world + root.up * (0.04f * Mathf.Abs(root.lossyScale.x)), root.rotation, Vector3.one * diameter), _disc, 0, null, 0, _mpb, false, false);
        }

        /// <summary>A flat ring segment (cells, the map's own frame: x east, z north) from the top, clockwise, over <paramref name="share"/> of the turn.</summary>
        private static void BuildArc(Mesh mesh, float rIn, float rOut, float share, Color col)
        {
            share = Mathf.Clamp01(share);
            int seg = Mathf.Max(2, Mathf.CeilToInt(64 * share));
            var v = new Vector3[(seg + 1) * 2]; var c = new Color[v.Length]; var u = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int s = 0; s <= seg; s++)
            {
                float ang = (90f - 360f * share * s / seg) * Mathf.Deg2Rad; float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                v[s * 2] = new Vector3(cx * rIn, 0f, cz * rIn); v[s * 2 + 1] = new Vector3(cx * rOut, 0f, cz * rOut);
                c[s * 2] = c[s * 2 + 1] = col; u[s * 2] = u[s * 2 + 1] = new Vector2(0.5f, 0.5f);
            }
            for (int s = 0; s < seg; s++) { int o = s * 6, b = s * 2; tri[o] = b; tri[o + 1] = b + 1; tri[o + 2] = b + 3; tri[o + 3] = b; tri[o + 4] = b + 3; tri[o + 5] = b + 2; }
            mesh.Clear(); mesh.vertices = v; mesh.colors = c; mesh.uv = u; mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (rOut * 2.2f + 1f));
        }
    }
}
