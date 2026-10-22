using System;
using System.Collections.Generic;
using Loki.Audio;
using Loki.IO;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// The client's effect system as roBrowserLegacy re-implements it (research/skill_effects.md §1): an effect id is a list of parts;
    /// <see cref="Spam"/> instantiates every part (× duplicates, staggered by timeBetweenDupli, delayed by delayOffset/delayLate/delayStart)
    /// as a renderer under the map root, plays its sound at the position, follows the owner when attachedEntity, and repeats persistent
    /// instances (ground units) until <see cref="Remove"/>. Skills go through <see cref="EffectDb"/>'s SkillEffect fields.
    /// Positions are map-local: 1 unit = 1 cell, y up. VR: screen tints are not reproduced; camera quakes only with the Comfort option
    /// (client wave 5, w5looks: Fx/CameraQuake.cs, off by default).
    /// </summary>
    public sealed class EffectManager : MonoBehaviour
    {
        public const int EF_HIT1 = 0, EF_HIT2 = 1, EF_BEGINSPELL = 12, EF_SIGHT = 22, EF_RUWACH = 33, EF_LOCKON = 60, EF_GROUNDSAMPLE = 513, EF_GUARD = 336;
        private static readonly int[] CastAuraByElement = { 12, 54, 57, 55, 56, 59, 58, 454, 58, 454 }; // ZC_USESKILL_ACK property → EF_BEGINSPELL* (Entity.js:1846-1892)

        public sealed class Instance
        {
            public string Key; public EffectPart Part; public int Dup; public uint OwnerAid, OtherAid; public Unit Owner, Other;
            public Vector3 Position, OtherPosition; public bool Attached; public float Start, End; public bool Persistent; public float RepeatDelay; public int RepeatEnd = -1;
            public int Direction; public FxRenderer Renderer; public bool Removed; public int Repeats;
            /// <summary>Client wave 5 (w5looks): the part's index in its effect's list, and the local time Spam was called (rB runs a FUNC part
            /// then, whatever its startTick: Fx/BodyFx.cs).</summary>
            public int PartIndex = -1; public float SpamTime;
        }

        private RoAssets _assets; private MapLoader _map; private EntityManager _ents; private LokiAudio _audio; private Func<Camera> _head;
        private Transform _root;
        private readonly List<Instance> _live = new List<Instance>();
        private readonly List<(float at, Instance inst)> _pending = new List<(float, Instance)>();
        private readonly List<(float at, string wav, Vector3 pos)> _sounds = new List<(float, string, Vector3)>();
        private readonly Dictionary<string, StrFile> _strs = new Dictionary<string, StrFile>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        public int LiveCount => _live.Count;
        /// <summary>
        /// Comfort "Skill effects" (item 10). 0 all. 1 fewer: effects that involve neither you nor your companions are drawn only within
        /// <see cref="NearCells"/> of you and with at most 2 copies. 2 quiet: on top of that, whatever other players and their companions
        /// do (to each other or to monsters) is not drawn at all. Always drawn: your own effects and effects on you, monsters' own effects
        /// near you, and persistent ones (ground units such as traps and Safety Wall, Sight/Ruwach) — those carry gameplay information.
        /// </summary>
        public int Density;
        public const float NearCells = 12f;
        /// <summary>True for your own companions (pet, homunculus, mercenary): their effects count as yours.</summary>
        public Func<uint, bool> IsMine;
        /// <summary>Effects not drawn because of <see cref="Density"/>.</summary>
        public int Filtered { get; private set; }
        public int Spawned { get; private set; }
        /// <summary>Effects spawned but skipped because their part type has no renderer here (RSM traps, WATERFALL…).</summary>
        public int Unsupported { get; private set; }
        public Camera Head => _head?.Invoke();
        public RoAssets Assets => _assets;
        public GatFileAccess Gat => new GatFileAccess(_map);

        public readonly struct GatFileAccess
        {
            private readonly MapLoader _m;
            public GatFileAccess(MapLoader m) { _m = m; }
            /// <summary>Map-local height (y up) of a cell's centre.</summary>
            public float Height(float x, float y)
            {
                var gat = _m?.Current?.Gat; if (gat == null) return 0f;
                int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y), 0, gat.Height - 1);
                return -gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
            }
        }

        public void Init(RoAssets assets, MapLoader map, EntityManager ents, LokiAudio audio, Func<Camera> head)
        {
            _assets = assets; _map = map; _ents = ents; _audio = audio; _head = head;
            EffectDb.Load();
            Own = new OwnSkillFx(this, id => _ents.Get(id)); // client wave 5 (w5fxown): Loki's own effects (a design), loaded after the GRF's tables
        }
        /// <summary>Client wave 5 (w5fxown): Loki's OWN skill effects for the skills without a sourced one (Fx/OwnSkillFx.cs; a design, not official).</summary>
        public OwnSkillFx Own { get; private set; }

        /// <summary>Client wave 4 (w4fx): the GRF's ez2streffect tables into the effect table, tied to skills by exact aegis names (Fx/Ez2Effects.cs).</summary>
        public void LoadEz2(IReadOnlyDictionary<string, int> skillIdOfAegis) => Ez2Effects.Load(p => _assets?.Fs.Read(p), skillIdOfAegis);
        /// <summary>Client wave 4c (w4fx2): the GRF's official skill effect table and its effect files named after skills / effect ids (Fx/GrfSkillEffects.cs);
        /// after <see cref="LoadEz2"/> (the files rB / ez2 load are left alone).</summary>
        public void LoadGrfSkillEffects(IReadOnlyDictionary<string, int> skillIdOfAegis) { if (_assets?.Fs != null) GrfSkillEffects.Load(p => _assets.Fs.Read(p), _assets.Fs.Archives, skillIdOfAegis); }

        private Transform Root
        {
            get
            {
                if (_root == null || _root.parent != _map.transform) { var go = new GameObject("fx"); go.transform.SetParent(_map.transform, false); _root = go.transform; }
                return _root;
            }
        }

        public Vector3 CellPos(float x, float y) => new Vector3(x + 0.5f, Gat.Height(x, y), y + 0.5f);

        /// <summary>Everything gone (map change).</summary>
        public void Clear()
        {
            foreach (var i in _live) if (i.Renderer != null) Destroy(i.Renderer.gameObject);
            _live.Clear(); _pending.Clear(); _sounds.Clear();
            if (_root != null) Destroy(_root.gameObject); _root = null;
        }

        // ---- the funnel: EffectManager.spam / spamEffect (EffectManager.js:537-737)
        private static bool Supported(string type) => type == "STR" || type == "CYLINDER" || type == "3D" || type == "2D" || type == "SPR" || type == "QuadHorn" || type == "TRAIL" || type == "FUNC" || type == "RSM" || type == "OWN";
        /// <summary>Whether this part draws something here (client wave 4, w4fx: RSM trap models and the ground FUNCs, Fx/GroundFx.cs).</summary>
        public static bool Drawable(string key, EffectPart part) => Supported(part.Type) && !(part.Type == "FUNC" && !FuncFx.Handles(key) && FuncCallsFx.DrawableCalls(part) == 0);
        /// <summary>Spawned instances per part type ("STR", "RSM", "FUNC:ground" …) and the effect keys spawned, for the smoke (client wave 4, w4fx).</summary>
        public readonly Dictionary<string, int> SpawnedByType = new Dictionary<string, int>();
        public event Action<string, EffectPart, uint> SpawnedEffect;
        /// <summary>A drawable-looking part whose renderer could not be made (file missing, no renderer for its type).</summary>
        public event Action<string, EffectPart> SpawnFailed;
        /// <summary>Client wave 4c (w4fx2): a part's sound was queued (effect key, wav, owner) — the official table's sound-only effects show here.</summary>
        public event Action<string, string, uint> SoundQueued;
        /// <summary>Client wave 9 (h9feel): where this manager's part sounds go instead of the world's listener (wav name, map-local position;
        /// the return value is ignored). The Horde arena's manager sets it: the arena has its own listener and sound budget
        /// (Horde/Client/HordeFeel.cs), and the world's listener is out of hearing during a run. Null = LokiAudio.PlayWav as before.</summary>
        public Func<string, Vector3, bool> SoundSink;

        /// <summary>Returns how many drawable parts were scheduled (0 = nothing visible here: unknown id, sound-only, or only RSM/WATERFALL parts).</summary>
        public int Spam(string key, uint ownerAid, Vector3? position, float startTime, float durationMs, uint otherAid = 0, bool persistent = false, int repeatEnd = -1)
        {
            var parts = EffectDb.Effect(key);
            if (parts == null) return 0;
            var owner = ownerAid != 0 ? _ents.Get(ownerAid) : null; var other = otherAid != 0 ? _ents.Get(otherAid) : null;
            if (position == null && owner == null) return 0;
            Vector3 pos = position ?? owner.transform.localPosition;
            bool mine = Mine(ownerAid, owner) || Mine(otherAid, other);
            float near = float.PositiveInfinity; var self = _ents.Self;
            if (self != null) { var d = pos - self.transform.localPosition; d.y = 0; near = d.magnitude; }
            // client wave 5 (w5looks): a body size (EF_BABYBODY / EF_GIANTBODY and their "2" forms) is the unit's look, kept like the persistent effects
            if (!Keep(Density, mine, persistent || BodyFx.IsBodySize(key), PlayerSide(owner) || PlayerSide(other), near)) { Filtered++; return 0; }
            int maxDup = Density >= 1 && !mine && !persistent ? 2 : 999;
            Vector3 otherPos = other != null ? other.transform.localPosition : pos + new Vector3(-5, 0, 5);
            int drawable = 0;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                if (Drawable(key, part)) drawable++;
                int dup = part.Has("duplicate") ? part.I("duplicate") : 1; if (dup < 0) dup = 999; dup = Mathf.Clamp(dup, 1, maxDup);
                float between = part.Has("timeBetweenDupli") ? part.F("timeBetweenDupli") : 200f;
                for (int j = 0; j < dup; j++)
                {
                    var inst = new Instance
                    {
                        Key = key, Part = part, Dup = j, OwnerAid = ownerAid, OtherAid = otherAid, Owner = owner, Other = other,
                        Position = pos, OtherPosition = otherPos, Attached = part.B("attachedEntity") && owner != null,
                        // `repeat` on an SPR part loops its frames inside its duration (EntityAttachments.js:316-320), it never re-spawns the instance
                        Persistent = part.Type == "SPR" ? persistent : part.Has("repeat") ? part.B("repeat") : persistent, RepeatDelay = part.F("repeatDelay"), RepeatEnd = repeatEnd,
                        Direction = owner != null ? owner.Sprite != null ? owner.Sprite.Direction : 0 : 0,
                        PartIndex = pi, SpamTime = Time.time,
                    };
                    Schedule(inst, startTime + between * j / 1000f, durationMs, first: true);
                }
            }
            return drawable;
        }

        private bool Mine(uint aid, Unit u) => (u != null && u.IsSelf) || (aid != 0 && ((_ents.Self != null && aid == _ents.Self.Id) || (IsMine != null && IsMine(aid))));
        private static bool PlayerSide(Unit u) => u != null && !u.IsSelf && (u.Kind == Unit.UnitKind.Player || u.Kind == Unit.UnitKind.Companion);

        /// <summary>The <see cref="Density"/> rule (public for the smoke test): draw this effect?</summary>
        public static bool Keep(int density, bool mine, bool persistent, bool otherPlayers, float cellsFromYou)
        {
            if (density <= 0 || mine || persistent) return true;
            if (density >= 2 && otherPlayers) return false;
            return cellsFromYou <= NearCells;
        }

        /// <summary>Timing per spamEffect (EffectManager.js:639-685) with the table's intent for the first play (research §1.1 caveat).</summary>
        private void Schedule(Instance inst, float start, float durationMs, bool first)
        {
            var part = inst.Part;
            float delayOffset = (part.F("delayOffset") + part.F("delayOffsetDelta") * inst.Dup) / 1000f;
            float delayLate = (part.F("delayLate") + part.F("delayLateDelta") * inst.Dup) / 1000f;
            float duration = (part.Has("duration") ? part.F("duration") : durationMs) / 1000f;
            if (part.Has("wav") && first)
            {
                string wav = part.S("wav"); if (wav.Contains("%d")) wav = wav.Replace("%d", RandFrom(part).ToString());
                _sounds.Add((start + part.F("delayWav") / 1000f, wav, inst.Position));
                SoundQueued?.Invoke(inst.Key, wav, inst.OwnerAid);
            }
            float delayStart = part.F("delayStart") / 1000f;
            inst.Start = start + (first ? delayOffset + delayLate + delayStart : delayOffset + delayLate);
            inst.End = duration > 0 ? inst.Start + duration : -1f;
            // client wave 5 (w5looks): rB calls a FUNC part at the spam (EffectManager.js:725-735): the body animations, quakes and timed sounds
            // start now and never repeat (Fx/BodyFx.cs)
            if (first && part.Type == "FUNC" && BodyFx.Handles(inst.Key, inst.PartIndex)) { inst.Persistent = false; SpawnBody(inst); return; }
            _pending.Add((inst.Start, inst));
        }

        /// <summary>Client wave 5 (w5looks): a FUNC part that animates a body, shakes the camera or plays timed sounds (Fx/BodyFx.cs).</summary>
        private void SpawnBody(Instance inst)
        {
            if (inst.Attached && (inst.Owner == null || inst.Owner.gameObject == null)) return; // rB: an attached FUNC runs only with its owner
            var r = BodyFx.Create(this, inst, inst.PartIndex);
            if (r == null) return; // the FUNC returns at once (no unit to animate): nothing to keep
            inst.Renderer = r; r.Inst = inst;
            _live.Add(inst); Spawned++;
            SpawnedByType["FUNC:body"] = (SpawnedByType.TryGetValue("FUNC:body", out var sn) ? sn : 0) + 1;
            SpawnedEffect?.Invoke(inst.Key, inst.Part, inst.OwnerAid);
        }

        private static int RandFrom(EffectPart part)
        {
            var r = part.L("rand");
            if (r == null || r.Count < 2) return 1;
            int a = MiniJson.Int(r[0]), b = MiniJson.Int(r[1]);
            return UnityEngine.Random.Range(a, b + 1);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _sounds.Count - 1; i >= 0; i--)
            {
                if (_sounds[i].at > now) continue;
                var s = _sounds[i]; _sounds.RemoveAt(i);
                string name = s.wav.Replace('/', '\\'); if (!name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) name += ".wav";
                if (SoundSink != null) SoundSink(name, s.pos); // client wave 9 (h9feel): the arena's listener and budget
                else _audio?.PlayWav(name, new Vector2(s.pos.x - 0.5f, s.pos.z - 0.5f));
            }
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].at > now) continue;
                var inst = _pending[i].inst; _pending.RemoveAt(i);
                if (inst.Removed) continue;
                Spawn(inst);
            }
            // repeats + clean-up (EffectManager.js:201-254, 498-508)
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var inst = _live[i];
                var r = inst.Renderer;
                bool done = r == null || r.Finished || (inst.End > 0 && now > inst.End + Mathf.Max(0f, inst.RepeatDelay / 1000f) && r.EndsAtEndTick);
                if (!done && inst.Persistent && inst.End > 0 && inst.RepeatDelay < 0 && now > inst.End + inst.RepeatDelay / 1000f && !inst.Removed && r.Repeated == false)
                {
                    // negative repeatDelay restarts before the end (EF_PORTAL2): overlap a fresh copy
                    r.Repeated = true;
                    Restart(inst, inst.End + inst.RepeatDelay / 1000f);
                    continue;
                }
                if (!done) continue;
                _live.RemoveAt(i);
                if (r != null) Destroy(r.gameObject);
                if (inst.Persistent && !inst.Removed && (inst.RepeatEnd < 0 || inst.Repeats < inst.RepeatEnd) && !(r != null && r.Repeated))
                    Restart(inst, inst.End > 0 ? inst.End + Mathf.Max(0f, inst.RepeatDelay / 1000f) : now + Mathf.Max(0f, inst.RepeatDelay / 1000f));
            }
        }

        private void Restart(Instance old, float at)
        {
            var inst = new Instance
            {
                Key = old.Key, Part = old.Part, Dup = old.Dup, OwnerAid = old.OwnerAid, OtherAid = old.OtherAid, Owner = old.Owner, Other = old.Other,
                Position = old.Position, OtherPosition = old.OtherPosition, Attached = old.Attached, Persistent = old.Persistent, RepeatDelay = old.RepeatDelay, RepeatEnd = old.RepeatEnd,
                Direction = old.Direction, Repeats = old.Repeats + 1, PartIndex = old.PartIndex, SpamTime = Time.time,
            };
            Schedule(inst, Mathf.Max(at, Time.time), old.End > 0 ? (old.End - old.Start) * 1000f : -1f, first: false);
        }

        private void Spawn(Instance inst)
        {
            var part = inst.Part;
            if (inst.Attached && (inst.Owner == null || inst.Owner.gameObject == null)) return;
            FxRenderer r = null;
            switch (part.Type)
            {
                case "STR": r = StrFxRenderer.Create(this, inst); break;
                case "CYLINDER": r = CylinderFx.Create(this, inst); break;
                case "3D": case "2D": r = BillboardFx.Create(this, inst, part.Type == "2D"); break;
                case "SPR": r = SprFx.Create(this, inst); break;
                case "QuadHorn": r = QuadHornFx.Create(this, inst); break;
                case "TRAIL": SpawnTrail(inst); return;
                // client wave 4c (w4fx2): the rB FUNCs that spin the owner's body (Fx/SpinFx.cs) — a body animation, not a drawn part (Drawable stays false)
                case "FUNC":
                    if (BodyFx.Handles(inst.Key, inst.PartIndex)) { SpawnBody(inst); return; } // client wave 5 (w5looks): normally started by Schedule
                    r = FuncFx.Handles(inst.Key) ? (FxRenderer)FuncFx.Create(this, inst) : SpinFx.Handles(inst.Key) ? SpinFx.Create(this, inst) : FuncCallsFx.Create(this, inst); break;
                case "RSM": r = RsmFx.Create(this, inst); break; // trap models (client wave 4, w4fx)
                case "OWN": r = OwnFxRenderer.Create(this, inst); break; // client wave 5 (w5fxown): Loki's own effects, drawn in code (Fx/OwnFxRenderer.cs)
                case "": return; // sound-only part
                default: Unsupported++; SpawnFailed?.Invoke(inst.Key, part); return;
            }
            if (r == null) { Unsupported++; SpawnFailed?.Invoke(inst.Key, part); return; }
            inst.Renderer = r; r.Inst = inst;
            _live.Add(inst); Spawned++;
            string kind = part.Type == "FUNC" && SpinFx.Handles(inst.Key) ? "FUNC:spin" : part.Type == "FUNC" && !FuncFx.Handles(inst.Key) ? "FUNC:ground" : part.Type;
            SpawnedByType[kind] = (SpawnedByType.TryGetValue(kind, out var sn) ? sn : 0) + 1;
            SpawnedEffect?.Invoke(inst.Key, part, inst.OwnerAid);
            if (Spawned <= 40) Debug.Log($"[Fx] spawn {EffectDb.EffectName(inst.Key)} ({inst.Key}) part {part} dup {inst.Dup} at {inst.Position} owner {inst.OwnerAid}{(inst.Attached ? " attached" : "")}");
        }

        /// <summary>TRAIL (Renderer/Effects/Trail.js): a cursor from the other position towards ours, spawning parts along the way.</summary>
        private void SpawnTrail(Instance inst)
        {
            var part = inst.Part; var spawn = part.L("spawn"); if (spawn == null) return;
            float speed = part.F("speed", 20f), interval = part.F("interval", 50f) / 1000f, startOffset = part.F("startOffset"), overshoot = part.F("overshoot"), spread = part.F("spread");
            Vector3 from = part.B("sourceIsOwner") ? inst.Position : inst.OtherPosition, to = part.B("sourceIsOwner") ? inst.OtherPosition : inst.Position;
            var dir = to - from; dir.y = 0; float dist = dir.magnitude; if (dist < 0.01f) return; dir /= dist;
            float total = dist + overshoot; if (part.B("stopAtTarget")) total = dist;
            float t = 0f; int n = 0;
            for (float d = startOffset; d <= total && n < 200; d += speed * interval, t += interval, n++)
            {
                var p = from + dir * d; p += new Vector3(UnityEngine.Random.Range(-spread, spread), 0, UnityEngine.Random.Range(-spread, spread)); p.y = Gat.Height(p.x - 0.5f, p.z - 0.5f);
                foreach (var so in spawn)
                {
                    var pm = MiniJson.AsObject(so); if (pm == null) continue;
                    var sp = new EffectPart { Type = MiniJson.Str(pm.TryGetValue("type", out var ty) ? ty : null, ""), P = pm };
                    var child = new Instance { Key = inst.Key, Part = sp, Dup = n, OwnerAid = inst.OwnerAid, OtherAid = inst.OtherAid, Owner = inst.Owner, Other = inst.Other, Position = p, OtherPosition = inst.OtherPosition, Attached = false, Direction = inst.Direction };
                    Schedule(child, inst.Start + t, sp.Has("duration") ? sp.F("duration") : part.F("duration", 1000f), first: true);
                }
            }
        }

        /// <summary>Drops every instance owned by an AID (ZC_SKILL_DISAPPEAR, ZC_DISPEL, state bits off); optional: only one effect key.</summary>
        public void Remove(uint ownerAid, string key = null)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var inst = _live[i];
                if (inst.OwnerAid != ownerAid || (key != null && inst.Key != key)) continue;
                inst.Removed = true; _live.RemoveAt(i);
                if (inst.Renderer != null) Destroy(inst.Renderer.gameObject);
            }
            for (int i = _pending.Count - 1; i >= 0; i--) if (_pending[i].inst.OwnerAid == ownerAid && (key == null || _pending[i].inst.Key == key)) { _pending[i].inst.Removed = true; _pending.RemoveAt(i); }
        }
        public void Remove(uint ownerAid, int effectId) => Remove(ownerAid, effectId.ToString());
        /// <summary>ZC_DISPEL: the cast auras, lock-on and ground marker of an interrupted cast (Entity.js:1900-1925).</summary>
        public void RemoveCastEffects(uint aid) { foreach (int id in new[] { 12, 54, 55, 56, 57, 58, 59, 454, 60, 513 }) Remove(aid, id); }
        public bool Has(uint ownerAid, string key) { foreach (var i in _live) if (i.OwnerAid == ownerAid && i.Key == key) return true; foreach (var p in _pending) if (p.inst.OwnerAid == ownerAid && p.inst.Key == key) return true; return false; }

        // ---- skill entry points (EffectManager.js:802-1052)
        private void SpamIds(string[] ids, uint owner, Vector3? pos, float t, uint other) { if (ids == null) return; foreach (var id in ids) Spam(id, owner, pos, t, -1f, other); }
        public void SpamSkill(int skill, uint dst, Vector3? pos, float t, uint src)
        {
            var s = EffectDb.Skill(skill); if (s == null) return;
            // client wave 5 (w5looks): an effectId rB computes from the caster (Asura Strike: EffectDb.EffectIdFor)
            var caster = src != 0 ? _ents.Get(src) : null;
            SpamIds(EffectDb.EffectIdFor(skill, caster != null ? caster.Job : -1), dst, pos, t, src); SpamIds(s.EffectIdOnCaster, src, pos, t, dst);
        }
        public void SpamSkillSuccess(int skill, uint dst, float t, uint src) { var s = EffectDb.Skill(skill); if (s == null) return; SpamIds(s.SuccessEffectId, dst, null, t, src); SpamIds(s.SuccessEffectIdOnCaster, src, null, t, dst); }
        public void SpamSkillHit(int skill, uint dst, float t, uint src) { var s = EffectDb.Skill(skill); if (s == null) return; SpamIds(s.HitEffectId, dst, null, t, src); SpamIds(s.HitEffectIdOnCaster, src, null, t, dst); }
        public void SpamSkillRelease(int skill, uint dst, float t, uint src) { var s = EffectDb.Skill(skill); if (s == null) return; SpamIds(s.ReleaseEffectId, dst, null, t, src); }
        public void SpamSkillBeforeHit(int skill, uint dst, float t, uint src) { var s = EffectDb.Skill(skill); if (s == null) return; SpamIds(s.BeforeHitEffectId, dst, null, t, src); SpamIds(s.BeforeHitEffectIdOnCaster, src, null, t, dst); }
        public void SpamSkillCast(int skill, uint caster, float t, uint target) { var s = EffectDb.Skill(skill); if (s == null) return; SpamIds(s.BeginCastEffectId, caster, null, t, target); }
        /// <summary>A ground unit appeared (ZC_SKILL_ENTRY): its effect loops until Remove(uid).</summary>
        public int SpamSkillZone(int unt, int x, int y, uint uid, uint creator)
        {
            string key = EffectDb.Unit(unt);
            Remove(uid);
            if (key == null) return 0;
            return Spam(key, uid, CellPos(x, y), Time.time, -1f, creator, persistent: true);
        }
        /// <summary>The cast-start extras of ZC_USESKILL_ACK (Entity.js:1817-1892): begin-cast effect, lock-on / ground marker, element aura.</summary>
        public void OnCastStart(int skill, uint caster, uint target, int x, int y, int element, uint castMs)
        {
            var s = EffectDb.Skill(skill);
            float now = Time.time;
            SpamSkillCast(skill, caster, now, target);
            Own?.OnCast(skill, caster, target, x, y, castMs); // client wave 5 (w5fxown): an own recipe's cast phase (Fx/OwnSkillFx.cs)
            if (castMs == 0) return;
            if (target != 0 && target != caster) Spam(EF_LOCKON.ToString(), target, null, now, castMs, caster);
            else if (x != 0 || y != 0) Spam(EF_GROUNDSAMPLE.ToString(), 0, CellPos(x, y), now, castMs, caster);
            if (s == null || !s.HideCastAura)
            {
                // client wave 4c (w4fx2): the official skilleffectinfolist's beginEffectID replaces the element's aura (GrfSkillEffects rule O);
                // an id this client has no part for (EF_BLUECASTING 441) leaves the element's aura, as before
                string aura = s?.CastAuraId != null && s.CastAuraId.Length > 0 && EffectDb.Effect(s.CastAuraId[0])?.Count > 0 ? s.CastAuraId[0] : null;
                Spam(aura ?? CastAuraByElement[Mathf.Clamp(element, 0, CastAuraByElement.Length - 1)].ToString(), caster, null, now, castMs);
            }
        }
        /// <summary>ZC_NOTIFY_EFFECT (0x019b, clif_misceffect): the client's fixed table (Skill.js:64-75).</summary>
        public void MiscEffect(uint aid, int type)
        {
            int id = type >= 0 && type < MiscEffectIds.Length ? MiscEffectIds[type] : -1;
            if (id >= 0) Spam(id.ToString(), aid, null, Time.time, -1f);
        }
        /// <summary>e_notify_effect 0..9 → EF_ANGEL 371, EF_JOBLVUP 158, EF_REFINEFAIL 155, EF_REFINEOK 154, none, EF_PHARMACY_OK 305, EF_PHARMACY_FAIL 306,
        /// EF_ANGEL2 338, EF_JOBLVUP2 (undefined in EffectConst.js → the job-level effect again), EF_ANGEL3 582 (Skill.js:64-75, EffectConst.js).</summary>
        public static readonly int[] MiscEffectIds = { 371, 158, 155, 154, -1, 305, 306, 338, 158, 582 };

        // ---- shared resources
        public StrFile Str(string file)
        {
            if (_strs.TryGetValue(file, out var s)) return s;
            var bytes = _assets.Fs.Read(RoPath.Normalize("data\\texture\\effect\\" + file + ".str"));
            try { s = bytes != null ? StrFile.Parse(bytes) : null; } catch (Exception e) { Debug.LogWarning($"[Fx] bad STR {file}: {e.Message}"); s = null; }
            if (bytes == null) Debug.LogWarning("[Fx] STR missing: " + file);
            else if (s != null && _strs.Count < 12)
            {
                int keys = 0; foreach (var l in s.Layers) keys += l.Keys.Length;
                var k0 = s.Layers.Length > 0 && s.Layers[0].Keys.Length > 0 ? s.Layers[0].Keys[0] : null;
                Debug.Log($"[Fx] STR {file}: {s.Fps} fps, {s.MaxKey} frames, {s.Layers.Length} layers, {keys} keys, first texture '{(s.Layers.Length > 0 && s.Layers[0].Textures.Length > 0 ? s.Layers[0].Textures[0] : "-")}'" +
                          (k0 != null ? $", key0 pos ({k0.PosX},{k0.PosY}) uv [{string.Join(",", k0.Uv)}] xy [{string.Join(",", k0.Xy)}] blend {k0.SrcAlpha}/{k0.DestAlpha} color [{string.Join(",", k0.Color)}]" : ""));
            }
            _strs[file] = s;
            return s;
        }
        /// <summary>An effect texture, null when this GRF lacks it (client wave 4, w4fx: RoAssets' magenta "missing" checker was drawn as the effect).</summary>
        public Texture2D Texture(string name) { var t = _assets.Texture(name.Replace('/', '\\'), false); return t == null || t == _assets.MissingTexture ? null : t; }
        /// <summary>D3DBLEND → Unity BlendMode (StrEffect.js:415-427).</summary>
        public static int D3dBlend(int d3d)
        {
            switch (d3d)
            {
                case 1: return (int)UnityEngine.Rendering.BlendMode.Zero; case 2: return (int)UnityEngine.Rendering.BlendMode.One;
                case 3: return (int)UnityEngine.Rendering.BlendMode.SrcColor; case 4: return (int)UnityEngine.Rendering.BlendMode.OneMinusSrcColor;
                case 5: return (int)UnityEngine.Rendering.BlendMode.SrcAlpha; case 6: return (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                // 7 DESTALPHA / 8 INVDESTALPHA: the client's back buffer has no alpha (reads as 1), and almost every STR uses 5/7 = SRCALPHA/ONE additive glow
                case 7: return (int)UnityEngine.Rendering.BlendMode.One; case 8: return (int)UnityEngine.Rendering.BlendMode.Zero;
                case 9: return (int)UnityEngine.Rendering.BlendMode.DstColor; case 10: return (int)UnityEngine.Rendering.BlendMode.OneMinusDstColor;
                case 11: return (int)UnityEngine.Rendering.BlendMode.SrcAlphaSaturate;
                case 14: return (int)UnityEngine.Rendering.BlendMode.One; case 15: return (int)UnityEngine.Rendering.BlendMode.Zero;
                default: return -1;
            }
        }
        /// <summary>The Cylinder/3D `blendMode` table (Cylinder.js:379-393): source is always SrcAlpha, this is the destination factor.</summary>
        public static int PartBlend(int mode)
        {
            switch (mode)
            {
                case 1: return (int)UnityEngine.Rendering.BlendMode.Zero; case 2: return (int)UnityEngine.Rendering.BlendMode.One;
                case 3: return (int)UnityEngine.Rendering.BlendMode.SrcColor; case 4: return (int)UnityEngine.Rendering.BlendMode.OneMinusSrcColor;
                case 5: return (int)UnityEngine.Rendering.BlendMode.DstColor; case 6: return (int)UnityEngine.Rendering.BlendMode.OneMinusDstColor;
                case 7: return (int)UnityEngine.Rendering.BlendMode.SrcAlpha; case 8: return (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                case 9: return (int)UnityEngine.Rendering.BlendMode.One; case 10: return (int)UnityEngine.Rendering.BlendMode.Zero; // DST_ALPHA with an alpha-less back buffer
                case 15: return (int)UnityEngine.Rendering.BlendMode.SrcAlphaSaturate;
                default: return (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
            }
        }
        private static Shader _strShader;
        public static Shader StrShader => _strShader != null ? _strShader : (_strShader = Shader.Find("Loki/Str"));
        /// <summary>A material per (texture, src, dst) — effects share them.</summary>
        public Material Material(Texture2D tex, int src, int dst)
        {
            string key = (tex != null ? tex.GetInstanceID() : 0) + ":" + src + ":" + dst;
            if (_materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(StrShader) { mainTexture = tex, name = "fx:" + (tex != null ? tex.name : "none") };
            m.SetFloat("_SrcBlend", src); m.SetFloat("_DstBlend", dst);
            _materials[key] = m;
            return m;
        }
        public Transform FxRoot => Root;
    }
}
