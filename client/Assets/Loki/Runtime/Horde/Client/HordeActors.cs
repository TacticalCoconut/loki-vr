using System.Collections.Generic;
using Loki.Sprites;
using Loki.UI;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): the run characters (at most 8), drawn as the world draws a player: a <see cref="Unit"/> with the
    /// real job's sprites (Sprites/PlayerAppearance.cs through Unit.ApplyLook: the 219 job bodies, hair, hair colour), its
    /// walking / standing / lying actions, a tag over its head (<see cref="HordeTag"/> since client wave 15: the HP bar and the
    /// stats that matter) and the damage numbers of UI/HitNumbers.cs's colours. A job change swaps the body at once (Unit.SetLook).
    /// The units are made with Unit.Create only: no collider, no Targetable (the laser never acts on them), not in the world's EntityManager.
    /// Looks: my own character keeps the real character's sex, hair style and colour (no equipment: the run is its own);
    /// another player's come from its RO character when the world has it in view (Entities), else a plain look.
    ///
    /// Client wave 15 (w15hfx), point 20 of the design's wave 14 list: ATTACK ANIMATIONS, "be very selective" (design note: "not ... a ridiculous
    /// amount of animations on the character that all badly interact with one another"). The rule (<see cref="Attack"/>, decided in
    /// <see cref="Update"/>, once a frame per character):
    ///   - ONE animation at a time: while one plays, every other request is dropped (none is queued, none restarts or cuts it); only a
    ///     fall ends it.
    ///   - a GAP of <see cref="MinGapSeconds"/> after one ends before the next may start.
    ///   - PRIORITY: of the requests of one frame the highest wins. 3 = a big attack (its cooldown at the weapon's level is at least
    ///     <see cref="BigCooldownSeconds"/>: Meteor Storm, Bowling Bash ...), 2 = a melee swing (the visible slice goes with it),
    ///     1 = any other auto-fire (bolts, chains, novas, zones with short cooldowns): these only when nothing played for
    ///     <see cref="AutoFireGapSeconds"/>. Never animated: orbiters, auras, summons, traps (lasting things, not attacks).
    ///   - SHORT: an action is sped up to last at most <see cref="MaxActionSeconds"/>; after it the character walks or stands as it
    ///     moves. Fallen, choosing, the count-in: none.
    ///   - the pose: a melee or ranged weapon plays RO's attack action of the job (Sprites/PlayerAppearance.cs AttackActionGroup: a
    ///     melee weapon as bare hands, a ranged one as a bow); a magic weapon, a heal, a zone or an area plays the casting pose (group 12).
    /// Numbers: Loki's own.
    /// </summary>
    public sealed class HordePlayers
    {
        public const float MinGapSeconds = 0.6f, AutoFireGapSeconds = 5f, BigCooldownSeconds = 2.5f, MaxActionSeconds = 0.5f;
        private sealed class Actor
        {
            public Unit Unit; public int Job; public uint Aid; public bool Fallen; public int Dir; public bool Moving; public HordeTag Tag; public int Sex = 1;
            // the attack animation (w15hfx, point 20)
            public float AnimUntil, AnimEnd = -100f; public int ReqPri, ReqGroup; public string ReqWhat;
        }
        private readonly Actor[] _actors = new Actor[HordeLimits.MaxPlayers];
        private readonly HordeArena _arena; private readonly RoAssets _assets;
        private readonly System.Func<uint, PlayerLook> _lookOf;
        public HordePlayers(HordeArena arena, RoAssets assets, System.Func<uint, PlayerLook> lookOf) { _arena = arena; _assets = assets; _lookOf = lookOf; }

        /// <summary>My run character's last job change as drawn: the job and the body sprite it got (log, smoke).</summary>
        public int MyBodyJob { get; private set; } = -1;
        public string MyBodyPath { get; private set; } = "";
        /// <summary>Attack animations asked, played and dropped by the rule (busy / gap / auto-fire), and the longest overlap seen
        /// (an animation started while another still played: must stay 0) (the smoke, the log).</summary>
        public int AnimAsked { get; private set; }
        public int AnimPlayed { get; private set; }
        public int AnimDropped { get; private set; }
        public int AnimOverlaps { get; private set; }
        /// <summary>The shortest time seen between one animation's end and the next one's start (s; the smoke checks the gap).</summary>
        public float AnimMinGapSeen { get; private set; } = float.MaxValue;
        public string LastAnim { get; private set; } = "";
        public Unit UnitOf(int slot) => slot >= 0 && slot < _actors.Length && _actors[slot] != null ? _actors[slot].Unit : null;
        public HordeTag TagOf(int slot) => slot >= 0 && slot < _actors.Length && _actors[slot] != null ? _actors[slot].Tag : null;
        public bool Animating(int slot) => slot >= 0 && slot < _actors.Length && _actors[slot] != null && Time.time < _actors[slot].AnimUntil;

        public void Clear()
        {
            for (int i = 0; i < _actors.Length; i++) { if (_actors[i]?.Unit != null) Object.Destroy(_actors[i].Unit.gameObject); _actors[i] = null; }
        }

        private PlayerLook LookFor(uint aid, int job)
        {
            var src = _lookOf?.Invoke(aid);
            var l = new PlayerLook { Job = job, Sex = src != null ? src.Sex : 1, Hair = src != null ? src.Hair : 1, HairColor = src != null ? src.HairColor : 0 };
            return l;
        }

        /// <summary>The attack animation of a weapon that fired for <paramref name="slot"/> (see the class summary for the rule); the
        /// request is decided at this frame's <see cref="Update"/>.</summary>
        public void Attack(int slot, WeaponDef w, int level)
        {
            if (w == null || slot < 0 || slot >= _actors.Length || _actors[slot] == null) return;
            if (w.Kind == WeaponKind.Orbiter || w.Kind == WeaponKind.Aura || w.Kind == WeaponKind.Summon || w.Kind == WeaponKind.Trap) return;
            if (w.Kind == WeaponKind.Dash) return; // client wave 18 (h18rules): Back Slide's trail areas are not attacks (a dash never asked before)
            var ac = _actors[slot];
            AnimAsked++;
            var lv = w.Levels != null && w.Levels.Length > 0 ? w.Levels[Mathf.Clamp(level, 1, w.Levels.Length) - 1] : null;
            float cd = lv != null ? lv.Cooldown : 1f;
            int pri = cd >= BigCooldownSeconds ? 3 : w.Kind == WeaponKind.MeleeArc ? 2 : 1;
            bool heals = lv != null && lv.Damage <= 0 && (lv.Heal > 0 || lv.HealPct > 0f);
            bool swingPose = !heals && w.Class != DamageClass.Magic && (w.Kind == WeaponKind.MeleeArc || w.Kind == WeaponKind.Bolt || w.Kind == WeaponKind.Chain || w.Kind == WeaponKind.Nova);
            int group = swingPose ? PlayerAppearanceDbGroup(ac, w.Class == DamageClass.Ranged ? 11 /* W_BOW */ : 0) : (int)ActorSprite.PlayerAction.Skill;
            if (pri > ac.ReqPri) { ac.ReqPri = pri; ac.ReqGroup = group; ac.ReqWhat = w.Key; }
            else AnimDropped++; // a lower request of the same frame
        }

        private static int PlayerAppearanceDbGroup(Actor ac, int weaponType) => AppearanceDb.AttackActionGroup(ac.Job, ac.Sex, weaponType);

        /// <summary>Places every run character for this frame. <paramref name="myPos"/>: my own, predicted.</summary>
        public void Update(HordeWorldView w, double now, int mySlot, Vector2 myPos, uint myAid) => Update(w, now, mySlot, myPos, myAid, false);

        /// <param name="still">The count-in or a pause: no attack animation starts.</param>
        public void Update(HordeWorldView w, double now, int mySlot, Vector2 myPos, uint myAid, bool still)
        {
            var root = _arena.Root; if (root == null || w == null) return;
            var gat = _arena.Loaded?.Gat;
            float a = w.Alpha(now); float t = Time.time;
            for (int i = 0; i < w.Players.Length; i++)
            {
                var p = w.Players[i]; var ac = _actors[i];
                if (!p.Present) { if (ac?.Unit != null) Object.Destroy(ac.Unit.gameObject); _actors[i] = null; continue; }
                int job = p.JobId;
                job = HordePassivesDraw.DrawnJob(p); // client wave 17 (h17pass): a Knight / Crusader with Riding rides a Peco Peco
                if (ac == null || ac.Unit == null || ac.Aid != p.Aid)
                {
                    if (ac?.Unit != null) Object.Destroy(ac.Unit.gameObject);
                    var look = LookFor(p.Aid, job);
                    var u = Unit.Create(root, gat, 0xFD000000u | (uint)i, job, look.Sex, look.Hair, _assets, look);
                    u.Sprite.Play((int)ActorSprite.PlayerAction.Idle, true);
                    var tag = HordeTag.Attach(u.transform, p.Name, i == mySlot); // (w15hfx, point 16: wave 8's Overhead plate never showed its HP bar)
                    ac = _actors[i] = new Actor { Unit = u, Job = job, Aid = p.Aid, Tag = tag, Sex = look.Sex };
                    Debug.Log($"[Horde] run character of slot {i} '{p.Name}' (AID {p.Aid}) drawn as job {job}");
                }
                var unit = ac.Unit;
                if (ac.Job != job)
                {
                    ac.Job = job;
                    var look = LookFor(p.Aid, job);
                    unit.SetLook(look, _assets); ac.Sex = look.Sex;
                    if (i == mySlot) { MyBodyJob = job; MyBodyPath = unit.Sprite.BodyPath ?? ""; }
                    Debug.Log($"[Horde] slot {i} now drawn as job {job} ({Game.SkillTreeDb.JobName(job)}): body {unit.Sprite.BodyPath}");
                }
                Vector2 pos = i == mySlot ? myPos : new Vector2(p.PrevX + (p.X - p.PrevX) * a, p.PrevY + (p.Y - p.PrevY) * a);
                unit.Cell = pos - new Vector2(0.5f, 0.5f);
                unit.transform.localPosition = _arena.Local(pos.x, pos.y);
                // protocol 2 (h9move): the sprite direction with hysteresis (World.Prepare's Dir8; the own slot's is the prediction's) when the view has history
                float rad = p.Facing * Mathf.Deg2Rad;
                int dir = w.HasHistory ? p.Dir8 : Unit.DirFromDelta(new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)));
                bool moving = p.Moving && !p.Fallen;
                if (dir != ac.Dir) { ac.Dir = dir; unit.Sprite.Direction = dir; }
                if (p.Fallen != ac.Fallen)
                {
                    ac.Fallen = p.Fallen;
                    if (p.Fallen) unit.Sprite.Play((int)ActorSprite.PlayerAction.Dead, false); else unit.Sprite.Play((int)ActorSprite.PlayerAction.Idle, true);
                    ac.Moving = false; ac.AnimUntil = 0f; // a fall ends an attack animation
                }
                // the attack animation (w15hfx, point 20): one at a time, never cut, a gap after it, the auto-fire rarely
                if (ac.ReqPri > 0)
                {
                    bool busy = t < ac.AnimUntil, gap = t < ac.AnimEnd + MinGapSeconds, rare = ac.ReqPri == 1 && t < ac.AnimEnd + AutoFireGapSeconds;
                    bool blocked = p.Fallen || still || (p.Flags & HordePlayerFlags.Choosing) != 0;
                    if (busy || gap || rare || blocked) AnimDropped++;
                    else
                    {
                        float len = unit.Sprite.GroupLengthMs(ac.ReqGroup, (dir + ActorSprite.CameraDirection) & 7) / 1000f;
                        if (len > 0.01f)
                        {
                            float speed = Mathf.Max(1f, len / MaxActionSeconds);
                            if (ac.AnimUntil > t) AnimOverlaps++;
                            if (ac.AnimEnd > 0f) AnimMinGapSeen = Mathf.Min(AnimMinGapSeen, t - ac.AnimEnd);
                            unit.Sprite.Play(ac.ReqGroup, false, speed);
                            ac.AnimUntil = t + len / speed; ac.AnimEnd = ac.AnimUntil;
                            AnimPlayed++; LastAnim = $"slot {i} {ac.ReqWhat} group {ac.ReqGroup} priority {ac.ReqPri} {len / speed:0.00} s";
                        }
                        else AnimDropped++;
                    }
                    ac.ReqPri = 0;
                }
                if (!p.Fallen && t >= ac.AnimUntil)
                {
                    bool ended = ac.AnimUntil > 0f; if (ended) ac.AnimUntil = 0f;
                    if (moving != ac.Moving || ended) { ac.Moving = moving; unit.Sprite.Play(moving ? (int)ActorSprite.PlayerAction.Walk : (int)ActorSprite.PlayerAction.Idle, true, 1.2f); }
                }
                unit.Sprite.SetTint(ActorSprite.TintFactor.Effect, p.Fallen ? new Color(0.6f, 0.6f, 0.65f, 1f) : (p.Flags & HordePlayerFlags.Safe) != 0 ? new Color(1f, 1f, 1f, 0.75f) : Color.white);
                unit.Sprite.SetTint(ActorSprite.TintFactor.Stealth, HordeRulesDraw.StealthTint(p, i == mySlot)); // client wave 18 (h18rules), point 27: see-through after a Back Slide
                // the tag over the head (w15hfx, point 16): HP, mine the party's EXP, the status that matters
                if (ac.Tag != null)
                {
                    var soul = w.SoulCountOf(i); // (client wave 18, h18feel, point 35: a fallen player's soul count, "Soul 3 / 5")
                    string status = p.Fallen ? (soul.Total > 0 ? Lang.F("Soul {0} / {1}", soul.Total - soul.Need, soul.Total) : Lang.F("<color=#ff8a7a>fallen</color> {0} %", Mathf.RoundToInt(p.Revive * 100)))
                                  : (p.Flags & HordePlayerFlags.Held) != 0 ? Lang.T("~horde~paused")
                                  : (p.Flags & HordePlayerFlags.Away) != 0 ? Lang.T("~horde~away")
                                  : i != mySlot && (p.Flags & HordePlayerFlags.Choosing) != 0 ? Lang.T("~horde~choosing…") : "";
                    ac.Tag.Set(p.Name, p.Hp, p.MaxHp, p.Fallen, p.Revive, w.PartyExpToNext > 0 ? w.PartyExp / (float)w.PartyExpToNext : p.ExpToNext > 0 ? p.Exp / (float)p.ExpToNext : 0f, status);
                }
            }
        }

        /// <summary>A number over a run character (hurt red, healed green: HitNumbers' colours).</summary>
        public void Pop(int slot, string text, Color c) { var tg = TagOf(slot); if (tg != null) tg.Pop(text, c); }
    }

    /// <summary>
    /// Client wave 8 (w8game): damage numbers over enemies, in UI/Overhead.cs PopNumber's look (40 px text with a black outline,
    /// rising 90 px a second, fading in its last 0.6 of 1.2 s) and HitNumbers' colours, from a pool of world-space labels instead
    /// of one canvas per enemy. At most <see cref="PoolSize"/> show at once (the oldest is reused: a busy screen keeps the newest).
    /// Client wave 9 (h9feel): a number has a size (<see cref="Show(Vector3, string, Color, float)"/>): HordeFeel scales it by the hit's
    /// size against the run's typical hit, by a critical, and colours it by the weapon's element; a big number also pops in (1.3 →
    /// 1.0 in its first 120 ms) and rises a little slower, so it stays readable.
    /// </summary>
    public sealed class HordeNumbers
    {
        public const int PoolSize = 48;
        private sealed class Pop { public RectTransform Rt; public Text Text; public float Born; public Vector3 Local; public bool On; public float Scale = 1f; }
        /// <summary>The largest scale shown so far (the smoke).</summary>
        public float MaxScaleShown { get; private set; }
        private readonly List<Pop> _pool = new List<Pop>();
        private int _next;
        private Transform _root;
        public int Shown { get; private set; }

        public void Attach(Transform arenaRoot)
        {
            _root = arenaRoot;
            foreach (var p in _pool) if (p.Rt != null) Object.Destroy(p.Rt.gameObject);
            _pool.Clear(); _next = 0;
            if (_root == null) return;
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("horde-number", typeof(RectTransform));
                go.transform.SetParent(_root, false);
                var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 6;
                var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(300, 60);
                var t = UiKit.Label(rt, "", Vector2.zero, new Vector2(300, 60), 40, TextAnchor.MiddleCenter, Color.white);
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(2, -2);
                go.SetActive(false);
                _pool.Add(new Pop { Rt = rt, Text = t });
            }
        }

        public void Show(Vector3 local, string text, Color c) => Show(local, text, c, 1f);

        /// <param name="scale">The number's size against the plain 40 px look (0.7 small … 2.2 a huge critical).</param>
        public void Show(Vector3 local, string text, Color c, float scale)
        {
            if (_pool.Count == 0) return;
            var p = _pool[_next]; _next = (_next + 1) % _pool.Count;
            p.Local = local + new Vector3(Random.Range(-0.4f, 0.4f), 1.6f, 0f); p.Born = Time.time; p.On = true; p.Scale = Mathf.Clamp(scale, 0.5f, 2.5f);
            p.Text.text = text; p.Text.color = c; p.Rt.gameObject.SetActive(true);
            if (p.Scale > MaxScaleShown) MaxScaleShown = p.Scale;
            Shown++;
        }

        public void LateUpdate()
        {
            if (_root == null) return;
            var head = Camera.main; float ps = Mathf.Abs(_root.lossyScale.x); if (ps < 1e-6f) return;
            foreach (var p in _pool)
            {
                if (!p.On) continue;
                float age = Time.time - p.Born;
                if (age > 1.2f) { p.On = false; p.Rt.gameObject.SetActive(false); continue; }
                p.Rt.localPosition = p.Local + Vector3.up * (age * (p.Scale > 1.3f ? 0.9f : 1.3f));
                float k = VR.Comfort.NameScale * p.Scale * (1f + 0.3f * Mathf.Clamp01(1f - age / 0.12f) * Mathf.Clamp01(p.Scale - 1f)); // a big one pops in
                if (head != null)
                {
                    var d = p.Rt.position - head.transform.position; float dist = d.magnitude;
                    if (dist > 1e-3f) p.Rt.rotation = Quaternion.LookRotation(d / dist, Vector3.up);
                    k *= Mathf.Clamp(dist / Overhead.FarMetres, 1f, 4f);
                }
                p.Rt.localScale = Vector3.one * (0.00035f * k / ps);
                var c = p.Text.color; c.a = 1f - Mathf.Clamp01((age - 0.6f) / 0.6f); p.Text.color = c;
            }
        }
    }
}
