using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 18 (h18feel), the design's point 35: soul retrieval as the players see it (the engine: Horde/Core/HordeSim.Souls.cs; the
    /// snapshot: HordeWorldView.Souls / SoulCounts). Drawn like the wave 17 map objectives (this is a part of HordeObjectives, with its
    /// helpers), in the soul's colour (pale lavender, never red):
    ///   - on the table: each shard's circle (a thin ring, a light fill), the claim's ring filling around it (orange while enemies inside
    ///     slow it), a small pillar of light with RO's Yggdrasil Leaf (item 610, the revive) floating in it, and over it the names of
    ///     whom it counts for (gold when it counts for me); a shield ring pulsing round a player who rose and cannot be hurt yet;
    ///   - the HUD's arena map: each shard as the leaf on a lavender diamond;
    ///   - arrows at the edge of the view for the <see cref="SoulArrowsMax"/> nearest shards out of view.
    /// The fallen player's count ("Soul 3 / 5") is in its tag and the HUD's status (HordeGame / HordePlayers). Sizes Loki's own.
    /// </summary>
    public sealed partial class HordeObjectives
    {
        /// <summary>The shards' pillar (cells high, wide), the leaf's height and size, the names' height over the leaf (cells).</summary>
        public const float SoulPillarCells = 5f, SoulPillarWidthCells = 0.7f, SoulIconHeightCells = 2.2f, SoulIconCells = 1.1f, SoulNamesCells = 1.1f;
        /// <summary>At most this many edge arrows for shards out of view (the nearest).</summary>
        public const int SoulArrowsMax = 3;
        /// <summary>RO's Yggdrasil Leaf (item_db 610): a shard's icon.</summary>
        public const int SoulItemId = 610;
        public static readonly Color SoulColor = new Color(0.82f, 0.78f, 1f), ShieldColor = new Color(1f, 0.95f, 0.7f);

        // ---- counters for the log and the smoke
        public int SoulPillarsDrawn { get; private set; }
        public int SoulMapIcons { get; private set; }
        public int SoulArrowsShown { get; private set; }
        public int ShieldsDrawn { get; private set; }
        /// <summary>The names shown over the shards this frame (one string per shard, in the snapshot's order).</summary>
        public readonly List<string> SoulNamesShown = new List<string>();

        private readonly List<(RectTransform rt, Text text)> _soulLabels = new List<(RectTransform, Text)>();
        private Vector2 _soulMyPos;
        private Transform _soulLabelsOf;

        // ---- client wave 19 (h18feel), the revive follow-up (design note: "A soul shard revive should give 5s, as should a class-change revive.
        // During this time, the player also moves and attacks much faster."): the banner of both rises and a risen runner's speed trail.
        // The engine's rule: HordeSim.Souls.cs Rise (InvulnerableSeconds unhurt and, as long, the buff RiseBuffId). The client knows a rise
        // by its Rose event (Value 1 soul shards / 2 the class-change level) and counts the seconds in the ENGINE's ticks (a card choice
        // stops them, as it stops the buff). The trail: fading discs flat on the ground (the shards' own disc, under every sprite: it never
        // hides an enemy) along the runner's last places, in the soul's colour (mine full, an ally's paler: the readability rule). Not
        // Back Slide's trail (h18rules: gold-white dots at chest height, 0.35 s): two effects, two looks. Nothing is done in a frame while
        // nobody is risen (_riseLive 0).
        /// <summary>The speed trail (Loki's own numbers): a disc stays this long (s); one is left every this many cells of the way; a step
        /// longer than the jump (cells, a frame) is not a walk (the rise's own move to the claimer): the trail starts again; a disc's
        /// size (cells) and what is left of it at its end; its alpha when fresh (mine).</summary>
        public const float RiseTrailSeconds = 0.5f, RiseTrailStepCells = 0.3f, RiseTrailJumpCells = 2f, RiseTrailDiscCells = 0.6f, RiseTrailEndShare = 0.4f, RiseTrailAlpha = 0.55f;
        /// <summary>At most this many discs behind one runner.</summary>
        public const int RiseTrailMax = 20;
        /// <summary>Counters for the log and the smoke: rises seen with the rule; this frame's runners with a trail drawn and their discs.</summary>
        public int RisesSeen { get; private set; }
        public int RiseTrailsDrawn { get; private set; }
        public int RiseTrailDiscs { get; private set; }
        /// <summary>The discs drawn behind one runner this frame.</summary>
        public int RiseTrailDiscsOf(int slot) => _riseDiscs != null && slot >= 0 && slot < _riseDiscs.Length ? _riseDiscs[slot] : 0;

        private struct RiseSample { public Vector2 Pos; public float At; }
        private int[] _riseUntil, _riseDiscs;      // per slot: the engine tick its speed ends at (0: none); the discs drawn this frame
        private List<RiseSample>[] _riseTrail;
        private int _riseLive;                     // runners risen or with discs still fading (0: nothing to do)
        private int _riseBannerTick = -1;          // the world's tick when my rise was seen, while its banner waits for the run to go on

        /// <summary>"Back! 5s unhurt, faster": what both rises give, in the design's style (the seconds from the rule).</summary>
        public static string RiseText(HordeContent c) => Lang.F("Back! {0}s unhurt, faster", Mathf.RoundToInt(c?.Souls != null ? c.Souls.InvulnerableSeconds : 5f));

        private void RiseBanner() { _riseBannerTick = -1; _g.Hud?.ShowBanner(RiseText(_g.Content), 2f, SoulColor, 50); }

        private void ClearRises()
        {
            _riseLive = 0; _riseBannerTick = -1; RiseTrailsDrawn = 0; RiseTrailDiscs = 0;
            if (_riseUntil == null) return;
            for (int i = 0; i < _riseUntil.Length; i++) { _riseUntil[i] = 0; _riseDiscs[i] = 0; _riseTrail[i].Clear(); }
        }

        /// <summary>A runner rose by its soul shards or at a class-change level (HordeGame's Rose, Value 1 / 2): its trail from now until
        /// the rule's seconds are over; mine: the banner, once the run goes on (a class-change level opens the class choice first: the
        /// seconds, and the banner, wait for it).</summary>
        public void Risen(HordeEvent e, int mySlot)
        {
            var w = _g.Channel?.World; if (w == null || e.Slot < 0 || e.Slot >= HordeLimits.MaxPlayers) return;
            if (_riseUntil == null)
            {
                _riseUntil = new int[HordeLimits.MaxPlayers]; _riseDiscs = new int[HordeLimits.MaxPlayers]; _riseTrail = new List<RiseSample>[HordeLimits.MaxPlayers];
                for (int i = 0; i < _riseTrail.Length; i++) _riseTrail[i] = new List<RiseSample>();
            }
            float seconds = _g.Content?.Souls != null ? _g.Content.Souls.InvulnerableSeconds : 5f;
            _riseUntil[e.Slot] = e.Tick + Mathf.Max(1, Mathf.RoundToInt(seconds * Mathf.Max(1, w.TickRate)));
            _riseTrail[e.Slot].Clear(); _riseLive = Mathf.Max(1, _riseLive); RisesSeen++;
            if (e.Slot == mySlot) _riseBannerTick = w.Tick; // (shown by DrawRises at the first tick after this one)
            Debug.Log($"[Horde] rise: slot {e.Slot} ({(e.Value == 2 ? "the class-change level" : "its soul gathered")}) at tick {e.Tick}: {seconds:0.#} s unhurt and faster, its speed trail until tick {_riseUntil[e.Slot]}");
        }

        /// <summary>Every frame: my rise's banner once the run goes on; the risen runners' trails.</summary>
        private void DrawRises(HordeWorldView w, int mySlot, Vector2 myPos, Transform root, float k, float t)
        {
            RiseTrailsDrawn = 0; RiseTrailDiscs = 0;
            if (_riseBannerTick >= 0 && w.Tick > _riseBannerTick) RiseBanner();
            if (_riseLive == 0) return;
            int live = 0; float a = w.Alpha(_g.Channel.Now);
            for (int i = 0; i < _riseUntil.Length && i < w.Players.Length; i++)
            {
                var tr = _riseTrail[i]; _riseDiscs[i] = 0;
                if (_riseUntil[i] == 0 && tr.Count == 0) continue;
                var p = w.Players[i];
                bool on = _riseUntil[i] != 0 && p.Present && !p.Fallen && w.Tick < _riseUntil[i];
                if (!on) _riseUntil[i] = 0;
                for (int j = tr.Count - 1; j >= 0; j--) if (t - tr[j].At > RiseTrailSeconds) tr.RemoveAt(j);
                if (on)
                {
                    var pos = i == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                    float step = tr.Count > 0 ? (tr[tr.Count - 1].Pos - pos).magnitude : float.MaxValue;
                    if (tr.Count > 0 && step > RiseTrailJumpCells) tr.Clear();
                    if (tr.Count == 0 || step > RiseTrailStepCells) tr.Add(new RiseSample { Pos = pos, At = t });
                    if (tr.Count > RiseTrailMax) tr.RemoveAt(0);
                }
                if (on || tr.Count > 0) live++;
                // (the newest place is the runner's own feet: not drawn)
                for (int j = 0; j < tr.Count - 1; j++)
                {
                    float fade = Mathf.Clamp01(1f - (t - tr[j].At) / RiseTrailSeconds);
                    var col = SoulColor; col.a = RiseTrailAlpha * fade;
                    if (i != mySlot) col = HordeReadability.AllyTint(col);
                    Disc(root.TransformPoint(_g.Arena.Local(tr[j].Pos.x, tr[j].Pos.y)), RiseTrailDiscCells * (RiseTrailEndShare + (1f - RiseTrailEndShare) * fade) * k, col, root);
                    _riseDiscs[i]++;
                }
                if (_riseDiscs[i] > 0) { RiseTrailsDrawn++; RiseTrailDiscs += _riseDiscs[i]; }
            }
            _riseLive = live;
        }

        public static Sprite SoulIcon()
        {
            var it = GameDb.GetItem(SoulItemId);
            return it != null ? WristMenu.ItemIcon(it) : WristMenu.SkillIcon(SkillDb.Get(54)); // (ALL_RESURRECTION's icon without the item table)
        }

        /// <summary>The names of whom a shard counts for ("Ann · Bo"; "you" for me).</summary>
        public static string SoulNames(HordeWorldView w, int owners, int mySlot)
        {
            var parts = new List<string>();
            for (int s = 0; s < w.Players.Length; s++)
                if ((owners & (1 << s)) != 0) parts.Add(s == mySlot ? Lang.T("~soul~you") : (w.Players[s].Name ?? "?"));
            return string.Join(" · ", parts);
        }

        /// <summary>The table: the shards, their claims, pillars, leaves and names; the shields of players who rose.</summary>
        private void DrawSouls(HordeWorldView w, int mySlot, Vector2 myPos, Transform root, Camera cam, float k, float t, ref int arc)
        {
            SoulPillarsDrawn = 0; ShieldsDrawn = 0; SoulNamesShown.Clear();
            var camPos = cam.transform.position; int label = 0;
            foreach (var s in w.Souls)
            {
                var centre = root.TransformPoint(_g.Arena.Local(s.X, s.Y));
                float pulse = 0.85f + 0.15f * Mathf.Sin(t * 3f + s.Id);
                bool mine = s.CountsFor(mySlot);
                var fill = SoulColor; fill.a = 0.12f; Disc(centre, 2f * s.Radius * k, fill, root);
                var rim = SoulColor; rim.a = 0.95f * pulse; Ring(centre, 2f * s.Radius * k, rim, root);
                if (s.Progress > 0.001f)
                {
                    while (_arcs.Count <= arc) _arcs.Add(new Mesh { name = "horde-objective-arc" });
                    var ac = s.Crowd > 0 ? new Color(1f, 0.6f, 0.2f, 1f) : Color.Lerp(SoulColor, Color.white, 0.35f);
                    BuildArc(_arcs[arc], s.Radius * 1.02f, s.Radius * 1.22f, s.Progress, ac);
                    _mpb.SetColor(ColorId, Color.white);
                    Graphics.DrawMesh(_arcs[arc], Matrix4x4.TRS(centre + root.up * (0.09f * k), root.rotation, Vector3.one * k), _glow, 0, null, 0, _mpb, false, false);
                    arc++;
                }
                var flat = centre - camPos; flat -= root.up * Vector3.Dot(flat, root.up);
                var face = flat.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(flat.normalized, root.up) : root.rotation;
                _mpb.SetColor(ColorId, new Color(0.03f, 0.03f, 0.05f, 0.3f));
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(SoulPillarWidthCells * 1.7f * k, SoulPillarCells * 1.02f * k, 1f)), _beamDark, 0, null, 0, _mpb, false, false);
                var beam = SoulColor; beam.a = 0.7f * pulse;
                _mpb.SetColor(ColorId, beam);
                Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre, face, new Vector3(SoulPillarWidthCells * k, SoulPillarCells * k, 1f)), _beam, 0, null, 0, _mpb, false, false);
                SoulPillarsDrawn++;
                var icon = SoulIcon();
                float bob = 0.12f * Mathf.Sin(t * 2.4f + s.Id);
                if (icon != null && icon.texture != null)
                {
                    _mpb.SetColor(ColorId, Color.white);
                    Graphics.DrawMesh(_upright, Matrix4x4.TRS(centre + root.up * ((SoulIconHeightCells + bob) * k), face, new Vector3(SoulIconCells * k, SoulIconCells * k, 1f)), IconMaterial(icon.texture), 0, null, 0, _mpb, false, false);
                }
                // whom it counts for, over the leaf
                string names = SoulNames(w, s.Owners, mySlot); SoulNamesShown.Add(names);
                var (lrt, ltext) = SoulLabel(label++, root);
                lrt.gameObject.SetActive(true);
                lrt.position = centre + root.up * ((SoulIconHeightCells + SoulIconCells + SoulNamesCells * 0.5f) * k);
                var d = lrt.position - camPos; if (d.sqrMagnitude > 1e-8f) lrt.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                float ns = VR.Comfort.NameScale * Mathf.Clamp(d.magnitude / Overhead.FarMetres, 1f, 4f);
                lrt.localScale = Vector3.one * (HordeTag.MetresPerPx * ns / Mathf.Max(1e-6f, Mathf.Abs(root.lossyScale.x)));
                if (ltext.text != names) ltext.text = names;
                ltext.color = mine ? new Color(1f, 0.85f, 0.35f) : new Color(0.92f, 0.9f, 1f);
            }
            for (int i = label; i < _soulLabels.Count; i++) if (_soulLabels[i].rt != null && _soulLabels[i].rt.gameObject.activeSelf) _soulLabels[i].rt.gameObject.SetActive(false);
            // a shield round a player who rose and cannot be hurt yet (Safe, not choosing a card)
            float a = w.Alpha(_g.Channel.Now);
            foreach (var p in w.Players)
            {
                if (!p.Present || p.Fallen || (p.Flags & HordePlayerFlags.Safe) == 0 || (p.Flags & HordePlayerFlags.Choosing) != 0) continue;
                var pos = p.Slot == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                var at = root.TransformPoint(_g.Arena.Local(pos.x, pos.y));
                float sp = 0.5f + 0.5f * Mathf.Sin(t * 6f + p.Slot);
                var c = ShieldColor; c.a = 0.55f + 0.35f * sp;
                Ring(at, (1.6f + 0.25f * sp) * k, c, root);
                ShieldsDrawn++;
            }
            DrawRises(w, mySlot, myPos, root, k, t); // client wave 19 (h18feel): a risen runner's speed trail, my rise's banner
        }

        /// <summary>A world-space label of the pool (under the arena's root; made again for a new arena).</summary>
        private (RectTransform, Text) SoulLabel(int i, Transform root)
        {
            if (_soulLabelsOf != root)
            {
                foreach (var l in _soulLabels) if (l.rt != null) Object.Destroy(l.rt.gameObject);
                _soulLabels.Clear(); _soulLabelsOf = root;
            }
            while (_soulLabels.Count <= i)
            {
                var go = new GameObject("horde-soul-names", typeof(RectTransform));
                go.transform.SetParent(root, false);
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 5;
                var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(520, 40);
                var tx = UiKit.Label(rt, "", Vector2.zero, new Vector2(520, 40), 24, TextAnchor.MiddleCenter, Color.white);
                var o = tx.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(2, -2);
                _soulLabels.Add((rt, tx));
            }
            return _soulLabels[i];
        }

        /// <summary>The HUD map's shard icons, from icon index <paramref name="n"/>; returns the next free index.</summary>
        private int MapSouls(HordeWorldView w, int n, System.Func<float, float, Vector2> P, float turn, int mySlot)
        {
            SoulMapIcons = 0;
            var sp = SoulIcon();
            foreach (var s in w.Souls)
            {
                var (rt, dia, ic) = MapIcon(n++);
                rt.gameObject.SetActive(true); rt.anchoredPosition = P(s.X, s.Y); rt.localRotation = Quaternion.Euler(0, 0, -turn); rt.SetAsLastSibling();
                rt.localScale = Vector3.one * 0.75f;
                var col = s.CountsFor(mySlot) ? Color.Lerp(SoulColor, new Color(1f, 0.85f, 0.35f), 0.5f) : SoulColor; dia.color = col;
                ic.sprite = sp; ic.enabled = sp != null; ic.color = Color.white;
                SoulMapIcons++;
            }
            return n;
        }

        /// <summary>Edge arrows for the nearest shards out of view (at most SoulArrowsMax), from arrow index <paramref name="n"/>; returns the next.</summary>
        private int SoulArrows(HordeWorldView w, Transform head, Transform root, int n, float ringPx, float t, Vector2 myPos)
        {
            SoulArrowsShown = 0;
            if (w.Souls.Count == 0) return n;
            var order = new List<HordeSoulView>(w.Souls);
            order.Sort((x, y) => (new Vector2(x.X, x.Y) - myPos).sqrMagnitude.CompareTo((new Vector2(y.X, y.Y) - myPos).sqrMagnitude));
            float k = Mathf.Abs(root.lossyScale.x); var sp = SoulIcon();
            foreach (var s in order)
            {
                if (SoulArrowsShown >= SoulArrowsMax) break;
                var pos = root.TransformPoint(_g.Arena.Local(s.X, s.Y)) + root.up * (SoulPillarCells * 0.4f * k);
                var to = pos - head.position;
                if (Vector3.Angle(head.forward, to) <= ArrowShowDeg) continue;
                var local = head.InverseTransformDirection(to);
                var dir = new Vector2(local.x, local.y); if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down; dir.Normalize();
                float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                while (_arrowItems.Count <= n) _arrowItems.Add(MakeArrow());
                var (rt, dia, ic, chev) = _arrowItems[n++];
                rt.gameObject.SetActive(true); rt.anchoredPosition = dir * ringPx; rt.localRotation = Quaternion.Euler(0, 0, deg - 90f);
                dia.color = SoulColor; ic.transform.localRotation = Quaternion.Euler(0, 0, 90f - deg); ic.sprite = sp; ic.enabled = sp != null;
                float pulse = 0.75f + 0.25f * Mathf.Sin(t * 5f);
                foreach (var img in chev) img.color = new Color(0.9f, 0.88f, 1f, pulse);
                SoulArrowsShown++;
            }
            return n;
        }
    }
}
