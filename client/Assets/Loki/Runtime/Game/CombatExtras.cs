using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Combat feedback the unit packets don't carry (client wave 2; rAthena e985006, research/items_combat.md §E), all to the area:
    ///   ZC_SPIRITS 0x01d0 aid.L num.W — spirit spheres (also homunculus spheres, servant weapons, Abyss Chaser spheres, Royal Guard spheres
    ///     at spawn); ZC_SPIRITS_ATTRIBUTE 0x08cf aid.L type.W num.W — Kagerou / Oboro charms (1 water, 2 earth, 3 fire, 4 wind; 0 none);
    ///   ZC_SOULENERGY 0x0b73 aid.L num.W — Soul Reaper souls; ZC_MILLENNIUMSHIELD 0x0440 aid.L num.W state.W — Rune Knight shields and
    ///     Royal Guard spheres when they change; ZC_TARGET_SPIRITS 0x0b68 gid.L unknown.L amount.W — Shadow Cross scars on a target (never
    ///     sent when they wear off); ZC_BLADESTOP 0x01d1 src.L dst.L flag.L (1 on, 0 off);
    ///   ZC_COMBODELAY 0x01d2 aid.L delay.L — the next combo skill may be cast within `delay` ms (+300 on the server: SC_COMBO); the
    ///     follow-up is pushed as a self skill (0x0b33) — shown here as a gold bar over the caster naming the chain's next skills.
    /// Each count is drawn as small glowing orbs circling the unit (UnitOrbs).
    /// </summary>
    public sealed class CombatExtras
    {
        public readonly Dictionary<uint, int> Spheres = new Dictionary<uint, int>(), Souls = new Dictionary<uint, int>(), Shields = new Dictionary<uint, int>(), Scars = new Dictionary<uint, int>();
        public readonly Dictionary<uint, (int type, int num)> Charms = new Dictionary<uint, (int, int)>();
        public float ComboUntil { get; private set; } = -1f; public int LastComboMs = -1; public int Combos; public string ComboNext = "";
        public int Received;
        private readonly EntityManager _ents; private readonly Func<uint> _self; private readonly Func<int> _lastSkill;
        /// <summary>Client wave 4 (w4units): the sphere texture and the Millennium Shield effect (set by LokiGame.W4Units).</summary>
        public RoAssets Assets; public Loki.Effects.EffectManager Effects;
        /// <summary>Blade Stop pairs seen (smoke): src, dst, on.</summary>
        public event Action<uint, uint, bool> BladeStop;

        public CombatExtras(EntityManager ents, Func<uint> self, Func<int> lastSkill)
        {
            _ents = ents; _self = self; _lastSkill = lastSkill;
            _ents.UnitAdded += Apply;
        }

        public void Clear() { Spheres.Clear(); Souls.Clear(); Shields.Clear(); Scars.Clear(); Charms.Clear(); ComboUntil = -1f; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x01d0: { r.Skip(2); uint aid = r.U32(); int num = r.S16(); Set(Spheres, aid, num); Log("spheres", aid, num); return true; }
                case 0x0b73: { r.Skip(2); uint aid = r.U32(); int num = r.S16(); Set(Souls, aid, num); Log("souls", aid, num); return true; }
                case 0x0440:
                {
                    // ZC_MILLENNIUMSHIELD aid.L num.W state.W: roBrowserLegacy (Skill.js:889-910 onMillenniumShield) removes the earlier
                    // EF_MILSHIELD_STR and, when num > 0 and state > 0, spams it again (749: STR mil_shield, attached; 60 keys at 60 fps = 1 s).
                    // rAthena always sends state 0 (clif_millenniumshield clif.cpp:19710-19721, the on-entry copy :1525-1542), so with it rB
                    // never draws the effect; Loki keeps the rule for other servers and shows the count as its orb ring (Apply)
                    r.Skip(2); uint aid = r.U32(); int num = r.S16(); int state = r.Remaining >= 2 ? r.S16() : 0;
                    Set(Shields, aid, num); Log("shields", aid, num);
                    if (Effects != null)
                    {
                        Effects.Remove(aid, 749);
                        if (num > 0 && state > 0) { int parts = Effects.Spam("749", aid, null, Time.time, -1f); ShieldFx++; Debug.Log($"[Combat] millennium shield effect on {aid}: {parts} drawable part(s)"); }
                    }
                    return true;
                }
                case 0x0b68: { r.Skip(2); uint gid = r.U32(); r.U32(); int num = r.S16(); Set(Scars, gid, num); Log("scars", gid, num); return true; }
                case 0x08cf:
                {
                    r.Skip(2); uint aid = r.U32(); int type = r.S16(), num = r.S16();
                    if (num <= 0 || type == 0) Charms.Remove(aid); else Charms[aid] = (type, num);
                    Received++; Apply(aid); Debug.Log($"[Combat] charms of {aid}: element {type} × {num}");
                    return true;
                }
                case 0x01d1:
                {
                    r.Skip(2); uint src = r.U32(), dst = r.U32(); bool on = r.S32() != 0; Received++;
                    // client wave 4 (w4units): as roBrowserLegacy (Engine/MapEngine/Entity.js:2580-2619 onBladeStopPacket / onBladeStopVisual,
                    // only when both units are in view): each faces the other, opt3 BLADESTOP on / off (the grey tint, EntityState.js:118-185),
                    // the battle stance (READYFIGHT, frame 0, not repeated) while it lasts, idle when it ends
                    var us = _ents.Get(src); var ud = _ents.Get(dst);
                    if (us != null && ud != null) { BladeStopLook(us, ud, on); BladeStopLook(ud, us, on); }
                    BladeStop?.Invoke(src, dst, on);
                    Debug.Log($"[Combat] blade stop {src} ↔ {dst}: {on}");
                    return true;
                }
                case 0x01d2:
                {
                    r.Skip(2); uint aid = r.U32(); int ms = r.S32(); Received++;
                    var u = _ents.Get(aid);
                    string next = aid == _self() ? NextOf(_lastSkill()) : "";
                    u?.Overhead?.SetCast(next.Length > 0 ? "Combo → " + next : "Combo!", ms / 1000f, new Color(1f, 0.8f, 0.2f));
                    if (aid == _self()) { ComboUntil = Time.time + ms / 1000f; LastComboMs = ms; Combos++; ComboNext = next; }
                    Debug.Log($"[Combat] combo window {ms} ms for {aid}{(next.Length > 0 ? " → " + next : "")}");
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>The Monk / Sura chain after the skill just used (skill_combo, skill.cpp:2398-2525); Triple Attack is a passive proc.</summary>
        public static string NextOf(int lastSkill)
        {
            switch (lastSkill)
            {
                case 272: return "Combo Finish";                    // MO_CHAINCOMBO → MO_COMBOFINISH
                case 273: return "Tiger Fist / Chain Crush / Asura"; // MO_COMBOFINISH → CH_TIGERFIST, CH_CHAINCRUSH, MO_EXTREMITYFIST
                case 371: return "Chain Crush";                     // CH_TIGERFIST → CH_CHAINCRUSH
                case 372: return "Asura Strike";                    // CH_CHAINCRUSH → MO_EXTREMITYFIST
                case 46: return "Double Strafe (Beast Strafing)";   // AC_DOUBLE with HT_POWER
                default: return "Chain Combo";                      // after a Triple Attack proc (MO_TRIPLEATTACK → MO_CHAINCOMBO)
            }
        }

        private void Set(Dictionary<uint, int> d, uint aid, int num) { if (num <= 0) d.Remove(aid); else d[aid] = num; Received++; Apply(aid); }
        private void Log(string what, uint aid, int num) => Debug.Log($"[Combat] {what} of {aid}: {num}");

        public int ShieldFx;

        private static void BladeStopLook(Unit self, Unit other, bool on)
        {
            var d = other.PositionAt(Time.time) - self.PositionAt(Time.time);
            if (d.sqrMagnitude > 0.01f) self.Sprite.Direction = Unit.DirFromDelta(d);
            UnitState.Of(self).ToggleOpt3(UnitState.Opt3BladeStop, on);
            if (on) self.Sprite.Play(self.IsPlayerType ? (int)ActorSprite.PlayerAction.Standby : (int)ActorSprite.MonsterAction.Idle, false);
            else self.Sprite.Play(self.IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, true);
        }

        private void Apply(Unit u) { if (u != null) Apply(u.Id); }
        private void Apply(uint aid)
        {
            var u = _ents.Get(aid); if (u == null) return;
            // client wave 4 (w4units): spirit spheres (and a Gunslinger's coins) as roBrowserLegacy draws them (Sprites/SpiritSpheres.cs);
            // shields, souls, charms and scars keep Loki's orbs: no reference draws a lasting count for them (roBrowserLegacy handles none
            // of 0x0b73 / 0x08cf / 0x0b68, and its shield effect needs a state rAthena never sends: case 0x0440)
            int sph = Spheres.TryGetValue(aid, out int s) ? s : 0;
            if (sph > 0 || u.GetComponent<SpiritSpheres>() != null) SpiritSpheres.Of(u).Set(sph, SpiritSpheres.IsGunslinger(u.Job), Assets);
            var orbs = u.GetComponent<UnitOrbs>() ?? u.gameObject.AddComponent<UnitOrbs>();
            orbs.Set("souls", Souls.TryGetValue(aid, out int so) ? so : 0, new Color(0.75f, 0.45f, 1f));
            orbs.Set("shields", Shields.TryGetValue(aid, out int sh) ? sh : 0, new Color(0.5f, 0.9f, 1f));
            orbs.Set("scars", Scars.TryGetValue(aid, out int sc) ? sc : 0, new Color(1f, 0.25f, 0.3f));
            if (Charms.TryGetValue(aid, out var ch)) orbs.Set("charms", ch.num, Fx.ElementColor(ch.type == 1 ? 1 : ch.type == 2 ? 2 : ch.type == 3 ? 3 : 4)); else orbs.Set("charms", 0, Color.white);
        }

        /// <summary>A line for the wrist Status page (our own counts).</summary>
        public string SelfLine()
        {
            uint me = _self(); var parts = new List<string>();
            if (Spheres.TryGetValue(me, out int s)) parts.Add(Lang.F("spirit spheres {0}", s));
            if (Charms.TryGetValue(me, out var c)) parts.Add(c.type == 1 ? Lang.F("water charms {0}", c.num) : c.type == 2 ? Lang.F("earth charms {0}", c.num) : c.type == 3 ? Lang.F("fire charms {0}", c.num) : Lang.F("wind charms {0}", c.num));
            if (Souls.TryGetValue(me, out int so)) parts.Add(Lang.F("souls {0}", so));
            if (Shields.TryGetValue(me, out int sh)) parts.Add(Lang.F("shields {0}", sh));
            if (ComboUntil > Time.time) parts.Add("<color=#ffd060>combo!</color>");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Small glowing orbs circling a unit, one ring per kind (spheres, charms, souls, shields, scars).</summary>
    public sealed class UnitOrbs : MonoBehaviour
    {
        private sealed class Channel { public int Count; public Color Color; public readonly List<Transform> Orbs = new List<Transform>(); public Material Mat; }
        private readonly Dictionary<string, Channel> _ch = new Dictionary<string, Channel>();
        private static Mesh _quad;
        public int Total { get { int t = 0; foreach (var c in _ch.Values) t += c.Count; return t; } }

        public void Set(string key, int count, Color color)
        {
            count = Mathf.Clamp(count, 0, 20);
            if (!_ch.TryGetValue(key, out var c)) { if (count == 0) return; _ch[key] = c = new Channel(); }
            c.Count = count; c.Color = color;
            if (c.Mat == null) c.Mat = new Material(Fx.Glow) { mainTexture = Fx.DotTexture(), name = "orb-" + key };
            c.Mat.color = color;
            while (c.Orbs.Count < count) c.Orbs.Add(MakeOrb(c.Mat));
            while (c.Orbs.Count > count) { var t = c.Orbs[c.Orbs.Count - 1]; c.Orbs.RemoveAt(c.Orbs.Count - 1); if (t != null) Destroy(t.gameObject); }
        }

        private Transform MakeOrb(Material m)
        {
            if (_quad == null)
            {
                _quad = new Mesh { name = "orb" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _quad.RecalculateBounds();
            }
            var go = new GameObject("orb"); go.transform.SetParent(transform, false); go.transform.localScale = Vector3.one * 0.35f;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        private void LateUpdate()
        {
            var cam = Camera.main; int ring = 0;
            foreach (var c in _ch.Values)
            {
                if (c.Count == 0) continue;
                float radius = 0.7f + ring * 0.25f, height = 1.1f + ring * 0.35f, speed = 70f + ring * 25f;
                for (int i = 0; i < c.Orbs.Count; i++)
                {
                    var t = c.Orbs[i]; if (t == null) continue;
                    float a = (Time.time * speed + i * 360f / c.Orbs.Count) * Mathf.Deg2Rad;
                    t.localPosition = new Vector3(Mathf.Cos(a) * radius, height + 0.08f * Mathf.Sin(Time.time * 3f + i), Mathf.Sin(a) * radius);
                    if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
                }
                ring++;
            }
        }
    }
}
