using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Skill feedback on the map (layouts: research/skills_stats_packets.md, rAthena e985006, PACKETVER 20211103):
    ///   0x0b1a ZC_USESKILL_ACK cast bar (29)      0x01b9 ZC_DISPEL cast cancelled (6)      0x0a41 ZC_SKILL_SCALE (18, ignored)
    ///   0x01de ZC_NOTIFY_SKILL2 damage (33)       0x09cb ZC_USE_SKILL2 support (17)        0x0117 ZC_NOTIFY_GROUNDSKILL (18)
    ///   0x0110 ZC_ACK_TOUSESKILL failure (14)     0x09ca skill unit on the ground (23)     0x0120 ZC_SKILL_DISAPPEAR (6)
    ///   0x01f3 / 0x0284 special effects (10 / 14)
    /// With <see cref="Effects"/> set, the client's own effect tables play (Loki.Effects); the procedural bursts below are the fallback.
    /// </summary>
    public sealed class CombatFx
    {
        private readonly EntityManager _ents; private readonly MapLoader _map;
        private readonly Dictionary<uint, int> _lastElement = new Dictionary<uint, int>();
        private readonly Dictionary<uint, GameObject> _units = new Dictionary<uint, GameObject>();
        public event System.Action<string> Message;
        public event System.Action<Unit, Unit, int, int> SkillDamage; // src, dst, damage, skill
        /// <summary>Client wave 25 (w25misc), item 8: a skill without damage was used (0x09cb): skill, level, target, source (0 = the target), ok.</summary>
        public event System.Action<int, int, uint, uint, bool> SkillNoDamage;
        /// <summary>The client's real effect tables and renderers; when null the procedural placeholders below stand in.</summary>
        public Loki.Effects.EffectManager Effects;
        private uint _self;
        private static readonly HashSet<int> HealSkills = new HashSet<int> { 28 /*AL_HEAL*/, 142 /*NV_FIRSTAID*/, 2043 /*AB_HIGHNESSHEAL*/, 2044 /*AB_CHEAL*/ };

        /// <summary>
        /// Client wave 4c (w4fx2): Auto Counter's counter hit, roBrowserLegacy's hard-coded animation (Engine/MapEngine/Entity.js:1774-1783 marks
        /// the local player when its ZC_USESKILL_ACK names KN_AUTOCOUNTER, :1911-1922 spams EF_AUTOCOUNTER 131 — autocounter.str + knight_autocounter.wav,
        /// EffectTable.js:3645-3653 — on its next ZC_DISPEL). rAthena sends that ZC_DISPEL exactly when the counter fires (battle.cpp:7215-7226:
        /// clif_skillcastcancel, then the counter as a normal hit) and announces every skill use with ZC_USESKILL_ACK even without a cast time
        /// (unit.cpp:2485-2486), both to the area: Loki keeps the mark per unit (any caster, not only us), ended by the dispel or by the unit's
        /// next skill use (no skill is usable during Auto Counter, status.cpp:2116).
        /// </summary>
        private readonly HashSet<uint> _autoCounter = new HashSet<uint>();
        public const int KnAutoCounter = 61, EfAutoCounter = 131;
        /// <summary>Client wave 4c (w4fx2): a unit's Auto Counter fired (the effect was spammed on it).</summary>
        public event System.Action<uint> AutoCounterShown;

        /// <summary>Client wave 3 (w3combat): the per-hit schedule skill damage lands on (set by LokiGame.W3Combat).</summary>
        public HitScheduler Hits;

        public CombatFx(EntityManager ents, MapLoader map) { _ents = ents; _map = map; }
        /// <summary>Client wave 11 (w11menu): a player unit shown dead (also Play Dead's pose) or sitting keeps that pose when it is
        /// the source of a no-damage skill packet; how often that happened (smoke).</summary>
        private static bool LiesOrSits(Unit u) => u.Sprite != null && (u.Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Dead || u.Sprite.ActionGroup == (int)ActorSprite.PlayerAction.Sit);
        public int CastPoseSkipped { get; private set; }
        public void SetSelf(uint aid) { _self = aid; }
        public int GroundUnitCount => _units.Count;
        public void Clear() { foreach (var g in _units.Values) if (g != null) Object.Destroy(g); _units.Clear(); _ground.Clear(); _lastElement.Clear(); _autoCounter.Clear(); Hits?.Clear(); }

        private Transform Root => _map.transform;
        private Vector3 At(Unit u, float up = 1.2f) => u.transform.localPosition + Vector3.up * up;

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0b1a:
                {
                    r.Skip(2); uint src = r.U32(), dst = r.U32(); int x = r.U16(), y = r.U16(); int skill = r.U16(); int elem = (int)r.U32(); uint ms = r.U32();
                    _lastElement[src] = elem;
                    if (skill == KnAutoCounter) _autoCounter.Add(src); else _autoCounter.Remove(src); // client wave 4c (w4fx2): see _autoCounter
                    var u = _ents.Get(src);
                    if (u != null)
                    {
                        var sfx = Loki.Effects.EffectDb.Skill(skill);
                        if (ms > 0) { if (sfx == null || !sfx.HideCastBar) u.Overhead?.SetCast(SkillDb.Get(skill).Name, ms / 1000f); if (u.IsPlayerType) u.Sprite.Play((int)ActorSprite.PlayerAction.Skill, true); }
                        if (Effects != null) Effects.OnCastStart(skill, src, dst, x, y, elem, ms);
                        else if (dst == 0 && (x != 0 || y != 0) && ms > 0) Fx.Ring(Root, CellPos(x, y), 1.6f, Fx.ElementColor(elem), ms / 1000f, "fx-cast-area");
                    }
                    return true;
                }
                case 0x01b9:
                {
                    r.Skip(2); uint aid = r.U32(); var u = _ents.Get(aid); if (u != null) { u.Overhead?.SetCast("", 0); u.Sprite.Play(0, true); } Effects?.RemoveCastEffects(aid);
                    if (_autoCounter.Remove(aid) && u != null) { Effects?.Spam(EfAutoCounter.ToString(), aid, null, Time.time, -1f); AutoCounterShown?.Invoke(aid); } // client wave 4c (w4fx2)
                    return true;
                }
                case 0x0a41: return true;
                case 0x01de:
                {
                    // SKID.W AID.L targetID.L startTime.L attackMT.L attackedMT.L damage.L level.W count.W action.B (packets_struct.hpp:4658-4671)
                    r.Skip(2); int skill = r.U16(); uint src = r.U32(), dst = r.U32(); int tick = r.S32(); int attackMT = r.S32(); r.Skip(4); int dmg = r.S32(); int lv = r.S16(); int count = r.S16(); int action = r.S8();
                    var s = _ents.Get(src); var d = _ents.Get(dst);
                    if (s != null) { s.Overhead?.SetCast("", 0); s.PlayAttack(); } // PCs: 5/10/11 by weapon then the battle stance (weapon/shield layers only exist in those groups), monsters: 2 then idle
                    int elem = _lastElement.TryGetValue(src, out var e) ? e : 0;
                    if (Effects != null)
                    {
                        // Entity.js:1645-1706: before-hit now + 200·i, hit at now + attackMT + 200·i (damage only), release now, the skill's own effect at impact
                        float now = Time.time, mt = Mathf.Clamp(attackMT, 1, 9999) / 1000f;
                        if (d != null && dmg != 0 && dmg != -30000)
                            for (int i = 0; i < Mathf.Max(1, count); i++) { Effects.SpamSkillBeforeHit(skill, dst, now + 0.2f * i, src); if (dmg > 0) Effects.SpamSkillHit(skill, dst, now + mt + 0.2f * i, src); }
                        if (s != null && d != null && action != 5) { Effects.SpamSkillRelease(skill, dst, now, src); Effects.SpamSkill(skill, dst, null, now + mt, src); }
                        Effects.Own?.OnDamage(skill, lv, src, dst, dmg, count, mt, action); // client wave 5 (w5fxown): Loki's own effect (a design), skills without a sourced one
                    }
                    if (d != null)
                    {
                        if (Effects == null) Fx.Burst(Root, At(d), Fx.ElementColor(elem), 1f + Mathf.Min(count, 5) * 0.15f, 18 + 4 * Mathf.Min(count, 6));
                        if (dmg != HitScheduler.DmgIgnore)
                        {
                            // client wave 3 (w3combat): hit i of count lands at +attackMT + 200·i with damage / count — number, flinch, hit
                            // sound, combo total and rumble on the HitScheduler (Game/HitSchedule.cs); SkillDamage stays the arrival event
                            if (Hits != null) Hits.OnSkillDamage(skill, src, dst, s, d, tick, attackMT, dmg, count, action);
                            else if (dmg > 0 && action != 4 && action != 9 && action != 14) d.PlayHurt();
                            SkillDamage?.Invoke(s, d, dmg, skill);
                        }
                    }
                    return true;
                }
                case 0x09cb:
                {
                    r.Skip(2); int skill = r.U16(); int level = r.S32(); uint target = r.U32(), src = r.U32(); bool ok = r.S8() != 0;
                    var t = _ents.Get(target); var s = src != 0 ? _ents.Get(src) : null;
                    // client wave 11 (w11menu): not while the caster lies dead or sits (roBrowserLegacy Engine/MapEngine/Entity.js:1431-1432,
                    // onEntityUseSkill: "if (srcEntity.action !== srcEntity.ACTION.DIE && srcEntity.action !== srcEntity.ACTION.SIT)"). Play
                    // Dead (NV_TRICKDEAD) is a status skill: rAthena starts the status first and sends this packet second
                    // (skills/skill_impl.cpp:57-73 StatusSkillImpl: clif_skill_nodamage(..., sc_start(...)); skill_factory_novice.cpp:56-57),
                    // so the cast pose replaced the lying pose EFST_TRICKDEAD had just set (Sprites/UnitEfstLooks.cs) and the character
                    // stood there: the design's 2026-10-06 playtest, "the animation for Play Dead is not working"
                    if (s != null) { s.Overhead?.SetCast("", 0); if (s.IsPlayerType && !LiesOrSits(s)) s.Sprite.Play((int)ActorSprite.PlayerAction.Skill, false); else if (s.IsPlayerType) CastPoseSkipped++; }
                    if (Effects != null) { Effects.SpamSkill(skill, target, null, Time.time, src); if (ok) Effects.SpamSkillSuccess(skill, target, Time.time, src); if (ok) Effects.Own?.OnNoDamage(skill, level, target, src, ok); } // w5fxown: Loki's own effect
                    SkillNoDamage?.Invoke(skill, level, target, src, ok); // client wave 25 (w25misc), item 8: Play Dead's sound (LokiGame.W25Misc.cs)
                    if (t != null && ok)
                    {
                        bool heal = HealSkills.Contains(skill);
                        if (Effects == null) Fx.Burst(Root, At(t, 1.0f), heal ? new Color(0.4f, 1f, 0.5f) : new Color(0.7f, 0.85f, 1f), 1.1f, 20);
                        if (heal && level > 0) t.Overhead?.PopNumber("+" + level, new Color(0.4f, 1f, 0.5f));
                        else if (Effects == null) t.Overhead?.PopNumber(SkillDb.Get(skill).Name, new Color(0.75f, 0.9f, 1f));
                    }
                    return true;
                }
                case 0x0117:
                {
                    r.Skip(2); int skill = r.U16(); uint src = r.U32(); int glv = r.S16(); int x = r.S16(), y = r.S16(); // SKID.W AID.L level.W x.W y.W (clif_skill_poseffect)
                    int elem = _lastElement.TryGetValue(src, out var e) ? e : 0;
                    if (Effects != null) { Effects.SpamSkill(skill, src, CellPos(x, y), Time.time, src); Effects.Own?.OnGround(skill, glv, src, x, y); } // w5fxown: Loki's own effect
                    else Fx.Burst(Root, CellPos(x, y) + Vector3.up * 0.5f, Fx.ElementColor(elem), 1.6f, 30);
                    var s = _ents.Get(src); if (s != null) s.Overhead?.SetCast("", 0);
                    return true;
                }
                case 0x0110:
                {
                    // ZC_ACK_TOUSESKILL skillId.W btype.L itemId.L flag.B cause.B (clif_skill_fail, clif.cpp:6008-6031); the text: Game/SkillFail.cs (client wave 4, w4fx)
                    r.Skip(2); int skill = r.U16(); int btype = r.S32(); int itemId = r.S32(); int flag = r.U8(); int cause = r.U8();
                    if (flag != 0) return true; // success (rB Skill.js:210)
                    _ents.Self?.Overhead?.SetCast("", 0);
                    string text = SkillFail.Text(cause, skill, btype, itemId);
                    SkillFailed?.Invoke(skill, cause, btype, itemId, text);
                    if (!string.IsNullOrEmpty(text)) Message?.Invoke(text);
                    return true;
                }
                case 0x09ca:
                {
                    // ZC_SKILL_ENTRY5 len.W id.L creator.L x.W y.W unitId.L range.B visible.B level.B (clif_getareachar_skillunit, clif.cpp:5426-5507)
                    r.Skip(4); uint uid = r.U32(); uint creator = r.U32(); int x = r.U16(), y = r.U16(); uint unitId = r.U32(); int range = r.U8(); bool visible = r.U8() != 0; int level = r.U8();
                    ShowUnit(uid, creator, x, y, (int)unitId, range, visible, level);
                    return true;
                }
                case 0x01c9:
                {
                    // ZC_SKILL_ENTRY2 id.L creator.L x.W y.W unitId.B visible.B hasMsg.B msg[80] (clif_graffiti, clif.cpp:5407-5424; struct
                    // packet_graffiti_entry, packets_struct.hpp:1457-1467): Graffiti (UNT_GRAFFITI 0xb0) with the player's text
                    r.Skip(2); uint uid = r.U32(); uint creator = r.U32(); int x = r.S16(), y = r.S16(); int unitId = r.U8(); bool visible = r.U8() != 0; bool hasMsg = r.U8() != 0;
                    string msg = hasMsg && r.Remaining > 0 ? r.Str(System.Math.Min(80, r.Remaining)) : "";
                    ShowGraffiti(uid, creator, x, y, unitId, visible, msg);
                    return true;
                }
                case 0x0120: { r.Skip(2); uint uid = r.U32(); RemoveUnit(uid); return true; }
                case 0x01f3: case 0x0284:
                {
                    // ZC_NOTIFY_EFFECT2 aid.L effectId.L / ZC_NOTIFY_EFFECT3 aid.L effectId.L num.L: an EF_* id verbatim (Skill.js:92-129)
                    r.Skip(2); uint aid = r.U32(); int effect = r.S32(); int num = id == 0x0284 ? r.S32() : 1;
                    var u = _ents.Get(aid);
                    if (Effects != null) { if (!(id == 0x0284 && num <= 0)) Effects.Spam(effect.ToString(), aid, u == null ? (Vector3?)null : null, Time.time, -1f); }
                    else if (u != null) Fx.Burst(Root, At(u, 1.0f), new Color(1f, 0.95f, 0.8f), 0.8f, 14);
                    return true;
                }
                default: return false;
            }
        }

        // ---- ground skill units (client wave 4, w4fx)
        /// <summary>A unit on the ground: what the server said, what was drawn (effect parts, or the fallback ring), for the morph and the smoke.</summary>
        public sealed class GroundUnit { public uint Uid, Creator; public int X, Y, UnitId, Range, Level, Parts; public bool Visible, Ring; public string Text; }
        private readonly Dictionary<uint, GroundUnit> _ground = new Dictionary<uint, GroundUnit>();
        public IReadOnlyDictionary<uint, GroundUnit> Ground => _ground;
        /// <summary>A unit appeared or changed (what is drawn now).</summary>
        public event System.Action<GroundUnit> UnitShown;
        /// <summary>A skill failed: skill, cause, btype, itemId, the text shown.</summary>
        public event System.Action<int, int, int, int, string> SkillFailed;
        /// <summary>UNT_DUMMYSKILL (rAthena skill.hpp:2619: "These show no effect on the client"): the invisible units of ranged single-hit
        /// skills and hidden traps (clif.cpp:5441-5447). UNT_GRAFFITI comes as 0x01c9.</summary>
        public const int UntDummySkill = 0x86, UntGraffiti = 0xb0;

        private void ShowUnit(uint uid, uint creator, int x, int y, int unitId, int range, bool visible, int level)
        {
            RemoveUnit(uid);
            var gu = new GroundUnit { Uid = uid, Creator = creator, X = x, Y = y, UnitId = unitId, Range = range, Visible = visible, Level = level };
            _ground[uid] = gu;
            if (!visible || unitId == UntDummySkill) { UnitShown?.Invoke(gu); return; }
            if (Effects != null) gu.Parts = Effects.SpamSkillZone(unitId, x, y, uid, creator);
            if (gu.Parts == 0)
            {
                // no drawable effect in the client data (rB SkillUnit.js EF_NONE / Todo, or no entry): Loki's fallback, a ring in the caster's
                // last cast element sized by the unit's range (not the official look)
                int elem = _lastElement.TryGetValue(creator, out var e) ? e : 0;
                var g = Fx.Ring(Root, CellPos(x, y), 0.6f + range * 0.5f, Fx.ElementColor(elem) * 0.9f, -1f, "fx-unit");
                g.AddComponent<FxSpin>().Speed = 40f;
                _units[uid] = g; gu.Ring = true;
            }
            UnitShown?.Invoke(gu);
        }

        /// <summary>A unit changed its look (ZC_SPRITE_CHANGE 0x01d7 LOOK_BASE on a skill unit's id: the Warp Portal opening turns
        /// UNT_WARP_ACTIVE into UNT_WARP_WAITING, rAthena skill.cpp:12281-12284 clif_changelook). False when the id is not a unit.</summary>
        public bool MorphUnit(uint uid, int unitId)
        {
            if (!_ground.TryGetValue(uid, out var gu)) return false;
            ShowUnit(uid, gu.Creator, gu.X, gu.Y, unitId, gu.Range, gu.Visible, gu.Level);
            return true;
        }

        public void RemoveUnit(uint uid)
        {
            if (_units.TryGetValue(uid, out var g)) { if (g != null) Object.Destroy(g); _units.Remove(uid); }
            _ground.Remove(uid);
            Effects?.Remove(uid);
        }

        /// <summary>Graffiti: the official client writes the text on the ground; roBrowserLegacy draws nothing (UNT_GRAFFITI → EF_NONE,
        /// SkillUnit.js). Loki lays the text flat on the cell (its own look: no reference shows it).</summary>
        private void ShowGraffiti(uint uid, uint creator, int x, int y, int unitId, bool visible, string msg)
        {
            RemoveUnit(uid);
            var gu = new GroundUnit { Uid = uid, Creator = creator, X = x, Y = y, UnitId = unitId, Visible = visible, Text = msg };
            _ground[uid] = gu;
            if (visible)
            {
                var go = new GameObject("fx-graffiti");
                go.transform.SetParent(Root, false);
                go.transform.localPosition = CellPos(x, y) + Vector3.up * 0.03f;
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var tm = go.AddComponent<TextMesh>();
                tm.font = UI.UiKit.Font; tm.fontSize = 48; tm.characterSize = 0.06f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                tm.color = new Color(1f, 0.95f, 0.75f, 0.95f); tm.text = msg;
                go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
                _units[uid] = go; gu.Parts = 1;
            }
            UnitShown?.Invoke(gu);
        }

        private Vector3 CellPos(int x, int y)
        {
            float h = 0f;
            var m = _map.Current;
            if (m != null && m.Gat.InBounds(x, y)) h = -m.Gat.Get(x, y).AverageHeight * RoSpace.Scale;
            return new Vector3(x + 0.5f, h, y + 0.5f);
        }
    }
}
