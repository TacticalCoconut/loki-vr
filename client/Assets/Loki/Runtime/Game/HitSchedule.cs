using System;
using System.Collections.Generic;
using System.IO;
using Loki.Formats;
using Loki.IO;
using Loki.Net.Packets;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    [Flags]
    public enum HitFlags
    {
        None = 0,
        Crit = 1,        // e_damage_type 10 / 13 (rAthena clif.hpp:691-707)
        Sp = 2,          // ZC_NOTIFY_ACT3 isSPDamage: "Displays blue digits" (clif.cpp:5286)
        LeftHand = 4,    // damage2: the left hand of a dual wield or a katar's second hit (battle.cpp:5034-5067)
        Skill = 8,       // from ZC_NOTIFY_SKILL2 0x01de
        Miss = 16,       // a normal attack that did no damage
        Lucky = 32,      // e_damage_type 11 (DMG_LUCY_DODGE)
        Endure = 64,     // e_damage_type 4 / 9 / 14: the target keeps its pose (clif_calc_delay, clif.cpp:5144-5176)
        ComboFinal = 128,
    }

    /// <summary>One damage pop: when it lands, what it shows, and (once landed) what sound and flinch it played — the smoke logs these.</summary>
    public sealed class ScheduledHit
    {
        /// <summary>The packet it came from: a running number, its id, fields, and the delay rules applied (ms).</summary>
        public int Seq; public int Type, Div, SrcSpeed, DmgSpeed, SrcJob = -1; public float DelayMs, BowMs; public string Rule;
        public ushort PacketId; public int ServerTick; public float Arrived, Due;
        public uint SrcId, DstId; public Unit Src, Dst;
        /// <summary>This pop's amount (0 for a miss / lucky dodge).</summary>
        public int Amount;
        /// <summary>Hit i of Count for the right hand / the skill; the left-hand pop has Index == Count.</summary>
        public int Index, Count;
        /// <summary>damage + damage2 of the packet (0x08c8) or its damage (0x01de).</summary>
        public int PacketTotal;
        /// <summary>Running total shown above a monster (0 = no combo line).</summary>
        public int Combo;
        public int Skill;
        public HitFlags Flags;
        /// <summary>The attacker's right-hand weapon type when the packet came (rB passes srcWeapon to Damage.add at that moment).</summary>
        public int SrcWeaponType;
        /// <summary>The first pop of a normal attack that did damage: EF_HIT1 plays on a monster there (rB Entity.js:702-716).</summary>
        public bool FirstDamage;
        // filled when it lands
        public string Sound; public bool Hurt; public float Landed;
        public bool Has(HitFlags f) => (Flags & f) != 0;
        public float DueMs => (Due - Arrived) * 1000f;
        public float LandedMs => (Landed - Arrived) * 1000f;
    }

    /// <summary>
    /// The one schedule every hit-timed thing shares (client wave 3, w3combat; re-audit §2e / §2f / §2g): damage numbers, the target's
    /// flinch, the hit sound, EF_HIT1, bow arrows and monster projectiles, and (through <see cref="Landed"/>) the controller rumble.
    /// Packets: ZC_NOTIFY_ACT3 0x08c8 (rAthena clif_damage, clif.cpp:5236-5310; packets.hpp:1412-1426) and ZC_NOTIFY_SKILL2 0x01de
    /// (clif_skill_damage, clif.cpp:6051-6103). Protocol-generic: packet fields and client data only.
    ///
    /// When the first hit lands (ms after the packet):
    ///  • srcSpeed ≤ 0 → at once: the server applies such damage immediately (status damage: status.cpp:13832-13833, 14269-14270;
    ///    Reject Sword battle.cpp:5188-5189). (rB would reuse the attacker's last attack speed.)
    ///  • a monster (any non-player, non-mercenary sprite): on its attack animation's damage frame — the frame whose ACT sound is "atk",
    ///    else the second-to-last frame, one ACT delay unit being 24 ms (rAthena doc/mob_db.txt:207-215), the animation sped up by
    ///    432 / srcSpeed when srcSpeed &lt; 432 (clif.cpp:5247-5255: the client takes srcSpeed as an inverted speed factor). This is the
    ///    official client as rAthena documents it; tools/combat/check_damage_frames.py finds it equal to rAthena's ClientAttackMotion
    ///    for 2,170 of 2,325 monsters (the rest are event / guardian copies whose LATAM sprite differs).
    ///  • a player (or an unknown attacker): min(srcSpeed, 864) — roBrowserLegacy Entity.js:604-611 (MAX_ATTACKMT = 2 × 432). rAthena
    ///    documents no damage frame for players and their ACTs carry no "atk" event (tools/combat/act_events.py: 0 of 548 body ACTs).
    ///  • a player with a bow: + (m + 8 / s) · s · 24 ms, s = attackMT / 432, m = PcAttackMotion (rB Entity.js:644-656, 6 for bows);
    ///    the arrow (ef_arrow_projectile) leaves at that moment, as in rB. Monster projectiles / hit sprites (rB AttackEffectTable) leave
    ///    when the monster's damage shows.
    ///  • a skill: clamp(attackMT, 1, 9999) (rB Entity.js:1544-1546).
    /// Then: normal-attack types 8 / 9 / 13 split the total over max(div, 2) pops 200 ms apart (rB C_MULTIHIT_DELAY; rB always
    /// shows 2, the packet's div is the server's hit count: battle.cpp:4276-4330); with a left hand those two are 100 ms apart
    /// (Entity.js:740-812), and one that cannot be split (damage 0 or 1) shows a single pop one step later, as rB does; damage2 pops
    /// at +350 (C_MULTIHIT_DELAY × 1.75); a skill's hit i at +200·i with damage / count.
    /// The pops always sum to the packet's total (the remainder goes to the last one). Monsters show a running combo total.
    /// The attack swing itself is paced to match (<see cref="PaceAttack"/>): a player's frames last attackMT / m (rB), a monster's
    /// delay × 24 ms / (432 / min(srcSpeed, 432)) (rAthena), so swing sounds and the monster's strike meet their numbers.
    /// </summary>
    public sealed class HitScheduler : MonoBehaviour
    {
        public const float MultiHitDelay = 0.2f;     // rB Engine/MapEngine/Entity.js:96 C_MULTIHIT_DELAY (PLUSATTACKED_MOTIONTIME)
        public const float LeftHandDelay = 0.35f;    // C_MULTIHIT_DELAY * 1.75 (Entity.js:730, 771, 803)
        public const int AvgAttackSpeed = 432;       // Entity.js:98; rAthena clif.cpp:5215 DEFAULT_ANIMATION_SPEED
        public const int MaxAttackMT = 864;          // Entity.js:100 MAX_ATTACKMT = AVG_ATTACK_SPEED * 2
        public const float ActDelayUnitMs = 24f;     // rAthena doc/mob_db.txt:212: "Every 25ms in Act Editor is 24ms in reality"
        public const int DmgIgnore = -30000;         // rAthena clif.hpp:58 DMGVAL_IGNORE: a display-only preamble (skill.cpp:3341-3342)

        private EntityManager _ents; private Loki.Effects.EffectManager _fx; private RoAssets _assets; private Audio.LokiAudio _audio;
        private readonly List<ScheduledHit> _pending = new List<ScheduledHit>();
        /// <summary>Every pop, when it lands (after the flinch, sound and number): the rumble and the smoke listen here.</summary>
        public event Action<ScheduledHit> Landed;
        /// <summary>A projectile / hit sprite was scheduled: effect key, attacker, target, start (Time.time), packet number (ScheduledHit.Seq).</summary>
        public event Action<string, uint, uint, float, int> Projectile;
        /// <summary>Smoke: log every packet and pop ([Hit] lines).</summary>
        public static bool Trace;
        public int Scheduled { get; private set; }
        public int LandedCount { get; private set; }
        public int PendingCount => _pending.Count;
        /// <summary>The number of the last packet scheduled (ScheduledHit.Seq).</summary>
        public int LastSeq => _seq;
        /// <summary>True while a pop of packet number seq (ScheduledHit.Seq) has not landed yet.</summary>
        public bool IsPending(int seq) { foreach (var h in _pending) if (h.Seq == seq) return true; return false; }
        /// <summary>Client wave 4 (w4fx): when the last damaging pop still pending on this target lands (Time.time), or −1. Damaging as rB's death
        /// sync counts it (Entity.js:2700-2708: damage &gt; 0, not an endure type, not a lucky dodge). Used by Game/DeathSync.cs.</summary>
        public float LastDamagingDue(uint dstId)
        {
            float last = -1f;
            foreach (var h in _pending)
                if (h.DstId == dstId && h.Amount > 0 && !h.Has(HitFlags.Miss) && !h.Has(HitFlags.Lucky) && !h.Has(HitFlags.Endure) && h.Due > last) last = h.Due;
            return last;
        }

        public void Init(EntityManager ents, Loki.Effects.EffectManager fx, RoAssets assets, Audio.LokiAudio audio)
        {
            _ents = ents; _fx = fx; _assets = assets; _audio = audio;
            Audio.WeaponSounds.Load();
            MobAttackFx.Load();
        }

        /// <summary>Map change: nothing pending survives (the units are gone).</summary>
        public void Clear() { _pending.Clear(); }

        // ------------------------------------------------------------------ timing rules (pure, for the smoke's self-checks)
        /// <summary>Players: min(srcSpeed, 864) ms; srcSpeed ≤ 0 → 0.</summary>
        public static float PlayerDelayMs(int srcSpeed) => srcSpeed <= 0 ? 0f : Mathf.Min(srcSpeed, MaxAttackMT);

        /// <summary>The damage frame of a sprite's attack action (group 2) drawn in direction dir: first "atk" frame, else frames − 2,
        /// × delay × 24 ms, divided by the speed factor 432 / min(srcSpeed, 432). −1 when the ACT has no such action.</summary>
        public static float DamageFrameMs(ActFile act, int dir, int srcSpeed, out int frame, out bool atkEvent)
        {
            frame = -1; atkEvent = false;
            if (srcSpeed <= 0) return 0f;
            if (act == null || act.Actions == null || act.Actions.Length == 0) return -1f;
            int ai = (int)Sprites.ActorSprite.MonsterAction.Attack * 8 + (dir & 7);
            if (ai >= act.Actions.Length) ai = Mathf.Min(dir & 7, act.Actions.Length - 1); // as ActorSprite.BuildFrame
            var a = act.Actions[ai];
            int n = a.Frames.Length; if (n == 0) return -1f;
            var ev = act.Events;
            for (int k = 0; k < n && frame < 0; k++)
            {
                int e = a.Frames[k].EventId;
                if (ev != null && e >= 0 && e < ev.Length && ev[e] == "atk") { frame = k; atkEvent = true; }
            }
            if (frame < 0) frame = Mathf.Max(n - 2, 0);
            float ms = frame * Mathf.Max(a.Delay, 1f) * ActDelayUnitMs;      // Mathf.Max(delay, 1) as ActorSprite plays it
            float speed = AvgAttackSpeed / (float)Mathf.Min(srcSpeed, AvgAttackSpeed);
            return ms / speed;
        }

        /// <summary>rB's bow extension (Entity.js:644-656): (m + 8 / s) · s · 24 with s = attackMT / 432.</summary>
        public static float BowExtensionMs(float attackMT, float m) => m * 24f * attackMT / AvgAttackSpeed + 8f * 24f;

        /// <summary>
        /// rB DBManager.getPCAttackMotion(job, sex, weapon, isDualWeapon(job, sex, weapon)) (DBManager.js:1467-1512, 1515-1990) as rB runs
        /// it: the frames a player's attack takes to land. rB hands both functions the unit's weapon VALUE; isDualWeapon compares it with
        /// WeaponType constants, so only a W_* value (&lt; 103, char select) can make it true. The rows that change the result: Thief (6,
        /// 4007) + W_BOW 11 → 5.75, Merchant (5, 4006) + W_2HAXE 7 → 5.85 (dual rows); otherwise a male novice class → 5.85 unless it holds
        /// a W_* two-hander (3 / 7 / 9 / 23); an assassin class whose weapon's type is a katar or a dual-wield pair → 3; everything else 6.
        /// </summary>
        public static float PcAttackMotion(int job, int sex, int weaponValue)
        {
            int raw = weaponValue > 0 && weaponValue < AppearanceDb.WeaponViewMax ? weaponValue : -1;
            bool thief = job == 6 || job == 4007, merchant = job == 5 || job == 4006;
            bool novice = job == 0 || job == 4001 || job == 4023 || job == 23 || job == 4045 || job == 4190 || job == 4191 || job == 4307;
            bool assassin = job == 12 || job == 4013 || job == 4035 || job == 4059 || job == 4065 || job == 4101 || job == 4254;
            bool dual = (thief && raw == 11) || (merchant && raw == 7)
                        || (novice && (sex == 0 ? raw == 1 : raw == 3 || raw == 7 || raw == 9 || raw == 23))
                        || (assassin && (raw == 16 || (raw >= 25 && raw <= 30)));
            if (dual) return thief ? 5.75f : merchant ? 5.85f : 6f;
            if (novice && sex == 1) return 5.85f;
            if (assassin) { int t = Audio.WeaponSounds.WeaponType(weaponValue); if (t == 16 || (t >= 25 && t <= 30)) return 3f; }
            return 6f;
        }

        /// <summary>
        /// Paces the attack swing a unit just started (Unit.PlayAttack plays it at 1x and cuts it at srcSpeed): a player's frames last
        /// min(srcSpeed, 864) / m ms (rB Entity.js:625-642 animSpeed = attackMT / m_attackMotion, EntityRender.js:976-978), so the
        /// swing sounds its body ACT carries on frames 4-5 of groups 10 / 11 are heard before the cut (e.g. the Swordman's attack_sword.wav
        /// at frame 5 = 500 ms at 1x against srcSpeed 510); a monster's frames last delay × 24 ms / (432 / min(srcSpeed, 432)) (rAthena
        /// clif.cpp:5247-5255, doc/mob_db.txt:212), so its drawn "atk" frame is the moment its number shows.
        /// </summary>
        public void PaceAttack(Unit src, int srcSpeed)
        {
            if (src == null || src.Sprite == null || srcSpeed <= 0) return;
            float units = src.Sprite.CurrentDelay, frameMs;
            if (src.IsPlayerType) frameMs = PlayerDelayMs(srcSpeed) / PcAttackMotion(src.Job, src.Sex, src.Look != null ? src.Look.Weapon : 0);
            else if (!RoNames.IsMercenary(src.Job)) frameMs = units * ActDelayUnitMs * Mathf.Min(srcSpeed, AvgAttackSpeed) / AvgAttackSpeed;
            else return;
            if (frameMs > 0f) src.Sprite.SetSpeed(units * 25f / frameMs); // ActorSprite draws a frame for delay × 25 ms / speed
        }

        /// <summary>Right-hand pop count of a normal attack.</summary>
        public static int NormalHits(int type, int div, int damage)
        {
            bool multi = type == 8 || type == 9 || type == 13;
            int hits = multi ? Mathf.Max(div, 2) : 1;
            if (damage > 0) hits = Mathf.Min(hits, damage); // a pop shows at least 1 (rB: "Can't divide 1 damage")
            return Mathf.Max(hits, 1);
        }

        private static bool IsDead(Unit u) => u.Sprite != null && u.Sprite.ActionGroup == (u.IsPlayerType ? (int)ActorSprite.PlayerAction.Dead : (int)ActorSprite.MonsterAction.Dead);

        /// <summary>When a unit's normal attack lands, in ms after its packet, and which rule said so.</summary>
        public float AttackDelayMs(Unit src, int srcSpeed, out string how)
        {
            if (srcSpeed <= 0) { how = "srcSpeed ≤ 0: at once"; return 0f; }
            if (src != null && !src.IsPlayerType && !RoNames.IsMercenary(src.Job) && _assets != null)
            {
                string body = RoNames.BodyPath(src.Job, src.Sex);
                var act = body != null ? _assets.SpriteAct(body) : null;
                int dir = (src.Sprite.Direction + ActorSprite.CameraDirection) & 7;
                float ms = DamageFrameMs(act, dir, srcSpeed, out int frame, out bool atk);
                // the drawn swing is paced to this frame by PaceAttack (called right after Unit.PlayAttack in the 0x08c8 arm)
                if (ms >= 0f) { how = $"damage frame {frame} ({(atk ? "atk" : "second to last")}) dir {dir}"; return ms; }
            }
            how = src == null ? "unknown attacker: min(srcSpeed, 864)" : "player: min(srcSpeed, 864)";
            return PlayerDelayMs(srcSpeed);
        }

        // ------------------------------------------------------------------ packets
        /// <summary>ZC_NOTIFY_ACT3 damage types (0, 4, 8, 9, 10, 13) and the lucky dodge (11); the caller already started the swing.</summary>
        public void OnNotifyAct(PACKET_ZC_NOTIFY_ACT p, Unit src, Unit dst)
        {
            int type = p.type;
            bool multi = type == 8 || type == 9 || type == 13;
            if (!(type == 0 || type == 4 || type == 10 || multi || type == 11)) return;
            float now = Time.time; int seq = ++_seq;
            uint srcId = (uint)p.srcID, dstId = (uint)p.targetID;
            // the attacker faces its target: rAthena turns it without telling anyone (unit.cpp:3292, unit_setdir(..., false)); rB lookTo (Entity.js:684)
            if (src != null && dst != null && src != dst) { var d = dst.Cell - src.Cell; if (d.sqrMagnitude > 0.01f) src.Sprite.Direction = Unit.DirFromDelta(d); }
            float delay = AttackDelayMs(src, p.srcSpeed, out string how);
            float ext = 0f;
            int srcWeapon = Audio.WeaponSounds.WeaponTypeOf(src);
            if (src != null && src.IsPlayerType && srcWeapon == 11) ext = BowExtensionMs(delay, PcAttackMotion(src.Job, src.Sex, src.Look.Weapon)); // DB.isBow(getWeaponType(weapon, true)): W_BOW
            float t0 = now + (delay + ext) / 1000f;
            // arrows and monster attack sprites leave when the damage shows, whatever the result (rB Entity.js:644-690, before the damage switch)
            if (src != null && dst != null && _fx != null)
            {
                string key = null;
                if (src.IsPlayerType) { if (ext > 0f) key = "ef_arrow_projectile"; }
                else key = MobAttackFx.Projectile(src.Job) ?? MobAttackFx.Spawn(src.Job);
                if (key != null)
                {
                    int drawn = _fx.Spam(key, dstId, null, t0, -1f, srcId);
                    Projectile?.Invoke(key, srcId, dstId, t0, seq);
                    if (Trace) Debug.Log($"[Hit] projectile {key} {srcId} → {dstId} at +{(t0 - now) * 1000f:0} ms (parts {drawn})");
                }
            }
            var baseHit = new ScheduledHit
            {
                Seq = seq, Type = type, Div = p.div, SrcSpeed = p.srcSpeed, DmgSpeed = p.dmgSpeed, SrcJob = src != null ? src.Job : -1, DelayMs = delay, BowMs = ext, Rule = how,
                PacketId = 0x08c8, ServerTick = p.serverTick, Arrived = now, SrcId = srcId, DstId = dstId, Src = src, Dst = dst,
                PacketTotal = p.damage + Mathf.Max(0, p.damage2), SrcWeaponType = srcWeapon,
                Flags = (type == 10 || type == 13 ? HitFlags.Crit : 0) | (p.isSPDamage != 0 ? HitFlags.Sp : 0) | (type == 4 || type == 9 ? HitFlags.Endure : 0),
            };
            var times = new List<float>();
            if (type == 11)
            {
                // the "lucky" sprite goes up at once (rB Entity.js:817-826 adds the attachment when the packet arrives)
                var h = Clone(baseHit); h.Due = now; h.Flags |= HitFlags.Lucky; h.Count = 1; Add(h); times.Add(0f);
            }
            else
            {
                int dmg = p.damage, dmg2 = p.damage2;
                float spacing = multi && dmg2 > 0 ? MultiHitDelay / 2f : MultiHitDelay;
                // a multi-hit type that cannot be split (damage 0 or 1: "Can't divide 1 damage") shows its one pop a step later, at +200
                // (+100 with a left hand): rB Entity.js:786-812 skips the first Damage.add and keeps the second
                float single = multi && dmg <= 1 ? spacing : 0f;
                if (dmg == 0) { var h = Clone(baseHit); h.Due = t0 + single; h.Flags |= HitFlags.Miss; h.Count = 1; Add(h); times.Add(h.DueMs); } // rB Damage.add(0) → MISS
                else if (dmg > 0)
                {
                    int hits = NormalHits(type, p.div, dmg);
                    bool combo = multi && dst != null && dst.Kind == Unit.UnitKind.Monster; // rB: combo only on TYPE_MOB / NPC_ABR / NPC_BIONIC targets
                    int per = dmg / hits, rest = dmg - per * hits, running = 0;
                    for (int i = 0; i < hits; i++)
                    {
                        var h = Clone(baseHit); h.Amount = per + (i == hits - 1 ? rest : 0); running += h.Amount;
                        h.Index = i; h.Count = hits; h.Due = t0 + single + spacing * i; h.FirstDamage = i == 0;
                        if (combo) { h.Combo = running; if (i == hits - 1 && dmg2 <= 0) h.Flags |= HitFlags.ComboFinal; }
                        Add(h); times.Add(h.DueMs);
                    }
                }
                // a negative damage is a disguise's placeholder (-1, clif.cpp:5299-5305): no number (rB Damage.add returns for < 0)
                if (dmg2 > 0)
                {
                    var h = Clone(baseHit); h.Amount = dmg2; h.Flags |= HitFlags.LeftHand; h.Due = t0 + LeftHandDelay;
                    h.Index = NormalHits(type, p.div, dmg); h.Count = h.Index;
                    if (multi && dst != null && dst.Kind == Unit.UnitKind.Monster && dmg > 0) { h.Combo = dmg + dmg2; h.Flags |= HitFlags.ComboFinal; }
                    Add(h); times.Add(h.DueMs);
                }
            }
            if (Trace)
                Debug.Log($"[Hit] 0x08c8 src {srcId}{(src != null ? $" (job {src.Job}, weapon type {srcWeapon})" : " (not in view)")} dst {dstId} serverTick {p.serverTick} " +
                          $"srcSpeed {p.srcSpeed} dmgSpeed {p.dmgSpeed} type {type} div {p.div} damage {p.damage} damage2 {p.damage2} sp {p.isSPDamage} → " +
                          $"delay {delay:0} ms ({how}){(ext > 0 ? $" + bow {ext:0} ms" : "")}; pops at +[{string.Join(", ", times.ConvertAll(t => t.ToString("0")))}] ms");
        }

        /// <summary>ZC_NOTIFY_SKILL2: one packet per target per cast, the total damage and its hit count (skill.cpp:3140).</summary>
        public void OnSkillDamage(int skill, uint srcId, uint dstId, Unit src, Unit dst, int serverTick, int attackMT, int dmg, int count, int action)
        {
            float now = Time.time;
            // rB Entity.js:1648: only `if (pkt.damage && target && !(srcEntity == dstEntity && pkt.action == SKILL))`
            if (dst == null || dmg == 0 || dmg == DmgIgnore || (src != null && src == dst && action == 6)) return;
            if (dmg < 0) return; // a disguise's placeholder (clif.cpp:6093-6101): no number, no flinch (rB Damage.add / onEntityWillBeHitSub)
            float mt = Mathf.Clamp(attackMT, 1, 9999) / 1000f; // rB Entity.js:1544-1546
            int n = Mathf.Max(1, count); if (dmg < n) n = dmg;
            bool combo = !dst.IsPlayerType && count > 1;         // rB isCombo: target not TYPE_PC and count > 1 (Entity.js:1652)
            int per = dmg / n, rest = dmg - per * n, running = 0;
            var times = new List<float>();
            HitFlags endure = action == 4 || action == 9 || action == 14 ? HitFlags.Endure : 0;
            int seq = ++_seq;
            for (int i = 0; i < n; i++)
            {
                var h = new ScheduledHit
                {
                    Seq = seq, Type = action, Div = count, SrcSpeed = attackMT, SrcJob = src != null ? src.Job : -1, DelayMs = mt * 1000f, Rule = "skill: clamp(attackMT, 1, 9999)",
                    PacketId = 0x01de, ServerTick = serverTick, Arrived = now, SrcId = srcId, DstId = dstId, Src = src, Dst = dst,
                    PacketTotal = dmg, Skill = skill, SrcWeaponType = Audio.WeaponSounds.WeaponTypeOf(src), Flags = HitFlags.Skill | endure,
                    Amount = per + (i == n - 1 ? rest : 0), Index = i, Count = n, Due = now + mt + MultiHitDelay * i,
                };
                running += h.Amount;
                if (combo) { h.Combo = running; if (i == n - 1) h.Flags |= HitFlags.ComboFinal; }
                Add(h); times.Add(h.DueMs);
            }
            if (Trace)
                Debug.Log($"[Hit] 0x01de skill {skill} src {srcId} dst {dstId} serverTick {serverTick} attackMT {attackMT} damage {dmg} count {count} action {action} → " +
                          $"pops at +[{string.Join(", ", times.ConvertAll(t => t.ToString("0")))}] ms");
        }

        private int _seq;

        private static ScheduledHit Clone(ScheduledHit b) => new ScheduledHit
        {
            Seq = b.Seq, Type = b.Type, Div = b.Div, SrcSpeed = b.SrcSpeed, DmgSpeed = b.DmgSpeed, SrcJob = b.SrcJob, DelayMs = b.DelayMs, BowMs = b.BowMs, Rule = b.Rule,
            PacketId = b.PacketId, ServerTick = b.ServerTick, Arrived = b.Arrived, SrcId = b.SrcId, DstId = b.DstId, Src = b.Src, Dst = b.Dst,
            PacketTotal = b.PacketTotal, SrcWeaponType = b.SrcWeaponType, Flags = b.Flags,
        };

        private void Add(ScheduledHit h)
        {
            int i = _pending.Count;
            while (i > 0 && _pending[i - 1].Due > h.Due) i--;
            _pending.Insert(i, h);
            Scheduled++;
        }

        private void Update()
        {
            float now = Time.time;
            while (_pending.Count > 0 && _pending[0].Due <= now)
            {
                var h = _pending[0]; _pending.RemoveAt(0);
                Land(h, now);
            }
        }

        private void Land(ScheduledHit h, float now)
        {
            h.Landed = now; LandedCount++;
            var dst = h.Dst;
            if (dst != null) // Unity null once the unit has left view / been destroyed
            {
                bool damage = h.Amount > 0 && !h.Has(HitFlags.Miss) && !h.Has(HitFlags.Lucky);
                // the flinch, per hit (rB onEntityWillBeHitSub, Entity.js:2700-2759; not for endure types or a unit already dying)
                if (damage && !h.Has(HitFlags.Endure) && !IsDead(dst)) { dst.PlayHurt(); h.Hurt = true; }
                // EF_HIT1 on a monster, once per normal attack (rB Entity.js:702-716)
                if (h.FirstDamage && dst.Kind == Unit.UnitKind.Monster && _fx != null) _fx.Spam(Loki.Effects.EffectManager.EF_HIT1.ToString(), dst.Id, null, now, -1f, h.SrcId);
                // the hit sound plays when the number shows (rB Damage.js:612-622): a player target by its job, else the attacker's weapon
                if (damage)
                {
                    h.Sound = dst.IsPlayerType ? Audio.WeaponSounds.JobHit(dst.Job) : Audio.WeaponSounds.WeaponHit(h.SrcWeaponType);
                    if (h.Sound != null) _audio?.PlayWav(h.Sound, dst.Cell, 0.8f);
                }
                UI.HitNumbers.Show(h);
            }
            if (Trace)
                Debug.Log($"[Hit] land {(h.PacketId == 0x01de ? "skill " + h.Skill : "attack")} {h.SrcId} → {h.DstId} +{h.LandedMs:0} ms (due +{h.DueMs:0}) " +
                          $"{(h.Has(HitFlags.Miss) ? "Miss" : h.Has(HitFlags.Lucky) ? "Lucky" : h.Amount.ToString())} [{h.Flags}] hit {h.Index + 1}/{h.Count}" +
                          $"{(h.Combo > 0 ? $" combo {h.Combo}" : "")} sound {h.Sound ?? "-"} hurt {h.Hurt}{(dst == null ? " (target gone)" : "")}");
            Landed?.Invoke(h);
        }
    }

    /// <summary>
    /// Monster normal-attack effects (rB DB/Monsters/AttackEffectTable.js): a projectile flying from the monster to its target, or a
    /// sprite played on the target. StreamingAssets/data/fx/mob_attack_fx.json (tools/combat/rb_combat_tables.mjs + build_combat_data.py);
    /// its `sprites` rows give where this GRF keeps a sprite rB names '../npc/…' (15 of the 17 sit in the monster folder) and are applied
    /// to the effect table's part at load.
    /// </summary>
    public static class MobAttackFx
    {
        private static Dictionary<int, string> _projectile, _spawn;
        public static int Count => (_projectile?.Count ?? 0) + (_spawn?.Count ?? 0);

        public static void Load()
        {
            if (_projectile != null) return;
            _projectile = new Dictionary<int, string>(); _spawn = new Dictionary<int, string>();
            string p = Loki.Data.ClientData.File("fx/mob_attack_fx.json");
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(p)));
                Fill(o, "projectile", _projectile); Fill(o, "spawn", _spawn);
                int fixedPaths = 0;
                if (o.TryGetValue("sprites", out var s) && MiniJson.AsObject(s) is Dictionary<string, object> sprites)
                    foreach (var kv in sprites)
                    {
                        var row = MiniJson.AsObject(kv.Value); if (row == null) continue;
                        string key = MiniJson.Str(row.TryGetValue("key", out var k) ? k : null, null), path = MiniJson.Str(row.TryGetValue("path", out var pa) ? pa : null, null);
                        var parts = Loki.Effects.EffectDb.Effect(kv.Key);
                        if (parts == null || key == null || path == null) continue;
                        foreach (var part in parts) if (part.P != null && part.P.ContainsKey(key)) { part.P[key] = path; fixedPaths++; }
                    }
                Debug.Log($"[MobAttackFx] {_projectile.Count} projectile + {_spawn.Count} hit-sprite monsters, {fixedPaths} sprite path(s) set from the data");
            }
            catch (Exception e) { Debug.LogError("[MobAttackFx] failed to load " + p + ": " + e.Message); }
        }

        private static void Fill(Dictionary<string, object> o, string name, Dictionary<int, string> into)
        {
            if (o == null || !o.TryGetValue(name, out var t) || !(MiniJson.AsObject(t) is Dictionary<string, object> m)) return;
            foreach (var kv in m) if (int.TryParse(kv.Key, out int id)) { var v = MiniJson.Str(kv.Value, null); if (!string.IsNullOrEmpty(v)) into[id] = v; }
        }

        public static string Projectile(int job) { Load(); return _projectile.TryGetValue(job, out var k) ? k : null; }
        public static string Spawn(int job) { Load(); return _spawn.TryGetValue(job, out var k) ? k : null; }
    }
}
