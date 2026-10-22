using System;
using System.Collections.Generic;
using Loki.Game.AiCore;
using Loki.Net;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The homunculus and mercenary AI the official client runs itself. rAthena runs no AI for them: it only executes the owner client's
    /// requests — CZ_REQUEST_MOVETOOWNER 0x0234 (clif.cpp:15697-15714: walk next to the owner, unit_calc_pos), CZ_REQUEST_MOVENPC 0x0232
    /// (clif.cpp:15717-15741: walk to a cell), CZ_REQUEST_ACTNPC 0x0233 (clif.cpp:15744-15766: "action: always 0", unit_attack) — and the
    /// owner's skill packets for the companion's skills (clif.cpp:12762-12868: a skill the companion lacks does nothing, a level above its
    /// own is lowered to it).
    ///
    /// Client wave 4 (w4units) ported the RO folder's default AI\AI.lua / AI_M.lua to C#; client wave 5 (w5ai) moved the decisions to
    /// Game/AiCore/DefaultAi.cs (Unity-free, completed against the two files) and added the custom AI (Companions.W5Ai.cs: the player's
    /// AI\USER_AI\AI.lua / AI_M.lua run by Loki's Lua 5.1 when /hoai / /merai is on). This file is what both read and send in the game:
    ///  • the world (<see cref="IAiWorld"/>): V_POSITION = the unit's cell (rounded from where it is drawn); V_MOTION: walking →
    ///    MOTION_MOVE, an attack action → MOTION_ATTACK (9 is never reported), dead → MOTION_DEAD, else MOTION_STAND; V_TARGET = the target
    ///    of the unit's last attack (ZC_NOTIFY_ACT, EntityManager.Damage) or skill (ZC_NOTIFY_SKILL, CombatFx.SkillDamage), 0 when none —
    ///    roBrowserLegacy keeps the same (Engine/MapEngine/Entity.js:587); V_ATTACKRANGE = the range the server sent (0x0ba4 / 0x029b), at
    ///    least 1; skill ranges / levels from the companion's skill list (0x0235 / 0x029d); HP / SP of the companions (0x0ba4 / 0x0ba5,
    ///    0x029b / 0x02a2) and of the owner; IsMonster: a monster in view, alive and visible; GetActors: every unit in view, then the
    ///    ground items and visible skill units (the guide 3-5: "characters, NPCs, monsters, items and skills"; their V_POSITION is
    ///    their cell, everything else of them reads as nothing);
    ///  • the requests (<see cref="IAiActions"/>): 0x0234, 0x0232 (negative cells are not sent), 0x0233 (action 0), and the skill packets
    ///    0x0438 / 0x0366 only for a skill the companion has, at most at its level — what rAthena does with them anyway, and a skill id the
    ///    companion does not have would be taken as the player's own skill (clif.cpp:12884-12895).
    /// AI(id) runs every 100 ms (roBrowserLegacy UI/Components/HomunInformations/HomunInformations.js:537-548 setInterval(…, 100); the
    /// official rate is not documented), only while the companion is in view (the same file: "if (entity)"). The AI starts again (a new
    /// state, the script run again) whenever the companion appears (the guide: "Each time before a Homunculus spawns": call, revive,
    /// map change, …).
    /// </summary>
    public sealed partial class Companions
    {
        public enum W4AiCmd { None = 0, Move = 1, Stop = 2, AttackObject = 3, AttackArea = 4, Patrol = 5, Hold = 6, SkillObject = 7, SkillArea = 8, Follow = 9 }
        public enum W4AiState { Idle = 0, Follow = 1, Chase = 2, Attack = 3, MoveCmd = 4, StopCmd = 5, AttackObjectCmd = 6, AttackAreaCmd = 7, PatrolCmd = 8, HoldCmd = 9, SkillObjectCmd = 10, SkillAreaCmd = 11, FollowCmd = 12, Custom = -1 }
        public const float W4AiPeriod = 0.1f;

        /// <summary>One companion's AI: the default AI (the C# port), or the player's script while it runs.</summary>
        public sealed class W4Brain
        {
            public bool Merc; public uint MyId;
            public readonly DefaultAi Port;
            /// <summary>The custom script (Companions.W5Ai.cs), null when the default AI runs.</summary>
            public AiScript Script;
            /// <summary>Why the custom script is not running (missing file, an error), shown in the companion window.</summary>
            public string ScriptProblem;
            public double[] Msg, ResMsg; // the commands for the next AI(id) call (GetMsg / GetResMsg), null = NONE_CMD
            public int Attacks, Moves, Follows, SkillsSent; // requests sent (smoke)
            public W4Brain(bool merc, uint id) { Merc = merc; MyId = id; Port = new DefaultAi(merc); }
            /// <summary>MyState: the port's, or the script's own global MyState when it keeps one (−1 otherwise).</summary>
            public W4AiState State
            {
                get
                {
                    if (Script == null) return (W4AiState)Port.MyState;
                    double s = Script.Global("MyState");
                    return double.IsNaN(s) || s < 0 || s > 12 || s != Math.Floor(s) ? W4AiState.Custom : (W4AiState)(int)s;
                }
            }
            public uint Enemy
            {
                get
                {
                    double e = Script != null ? Script.Global("MyEnemy") : Port.MyEnemy;
                    return double.IsNaN(e) || e <= 0 || e > uint.MaxValue ? 0 : (uint)e;
                }
            }
        }

        private W4Brain _homAi, _mercAi; private float _aiNext;
        /// <summary>The target the unit attacked or cast at last (V_TARGET), per unit id.</summary>
        private readonly Dictionary<uint, uint> _aiTargets = new Dictionary<uint, uint>();
        /// <summary>AI transitions and requests, for the log (smoke).</summary>
        public static bool W4AiLog = true;
        public W4Brain HomAi => _homAi; public W4Brain MercAi => _mercAi;
        private static readonly double[] NoCmd = { 0 };

        /// <summary>V_TARGET bookkeeping: a unit acted on another (an attack or a skill).</summary>
        public void W4AiNoteAction(uint src, uint dst) { if (src != 0) _aiTargets[src] = dst; }

        private void W4AiReset() { _homAi = null; _mercAi = null; }
        private void W4AiOnVanished(uint id) => _aiTargets.Remove(id);
        private uint W4AiEnemy() => _homAi != null && _homAi.Enemy != 0 ? _homAi.Enemy : _mercAi != null ? _mercAi.Enemy : 0;
        /// <summary>FOLLOW_CMD_ST (standby) on the homunculus, else the mercenary.</summary>
        public bool W4AiStandby => (_homAi ?? _mercAi)?.State == W4AiState.FollowCmd;

        /// <summary>A command from the owner's interface, to both companions (GetMsg of the next AI call; the guide 4-4's shapes:
        /// {MOVE_CMD, x, y}, {ATTACK_OBJECT_CMD, id}, {FOLLOW_CMD} …).</summary>
        public void W4AiCommand(W4AiCmd cmd, uint target, int x = 0, int y = 0)
        {
            foreach (var b in new[] { EnsureBrain(false), EnsureBrain(true) })
            {
                if (b == null) continue;
                b.Msg = cmd == W4AiCmd.AttackObject ? new double[] { (int)cmd, target } : cmd == W4AiCmd.Move ? new double[] { (int)cmd, x, y } : new double[] { (int)cmd };
            }
        }

        private W4Brain EnsureBrain(bool merc)
        {
            uint id = merc ? (HasMerc ? MercId : 0) : (HomActive ? HomId : 0);
            ref W4Brain b = ref merc ? ref _mercAi : ref _homAi;
            if (id == 0) { b = null; return null; }
            if (b == null || b.MyId != id)
            {
                b = new W4Brain(merc, id);
                Trace(b, "AI starts (the companion appeared)");
                W5AiStart(b); // the custom script when /hoai or /merai is on (Companions.W5Ai.cs)
            }
            return b;
        }

        private void W4AiTick()
        {
            if (_self() == null) return;
            if (Time.time < _aiNext) return;
            _aiNext = Time.time + W4AiPeriod;
            _world.Begin(this); _actions.C = this;
            foreach (bool merc in new[] { false, true })
            {
                var b = EnsureBrain(merc);
                if (b == null || _unit(b.MyId) == null) continue; // not in view: the client has no entity to run AI(id) for
                RunAi(b);
            }
        }

        // ------------------------------------------------------------------ AI(myid)
        private void RunAi(W4Brain b)
        {
            var msg = b.Msg ?? NoCmd; var rmsg = b.ResMsg ?? NoCmd; b.Msg = null; b.ResMsg = null;
            var before = b.State;
            _actions.Brain = b;
            if (b.Script != null) W5AiRunScript(b, msg, rmsg); // Companions.W5Ai.cs: falls back to the port on any error
            else b.Port.Tick(b.MyId, msg, rmsg, _world, _actions);
            var after = b.State;
            if (after != before)
            {
                Trace(b, $"{before} → {after}");
                if ((before == W4AiState.FollowCmd) != (after == W4AiState.FollowCmd)) Refresh(); // the window's Standby / Resume label
            }
        }

        // ------------------------------------------------------------------ the world as the AI reads it
        /// <summary>GetActors: every unit in view (set by the game: EntityManager.Units).</summary>
        public Func<IEnumerable<Unit>> W4AiActors;
        /// <summary>GetActors' items and skills ("Views the IDs of characters, NPCs, monsters, items and skills within the character's
        /// screen", the guide 3-5): the ground items and the visible skill units in view, with their cells (set by the game).</summary>
        public Func<IEnumerable<(uint id, int x, int y)>> W5AiOtherActors;
        /// <summary>The owner's HP / max HP / SP / max SP (V_HP … V_MAXSP of the owner), set by the game.</summary>
        public Func<(long hp, long maxHp, long sp, long maxSp)> W5OwnerVitals;
        private readonly LiveWorld _world = new LiveWorld();
        private readonly LiveActions _actions = new LiveActions();

        private sealed class LiveWorld : IAiWorld
        {
            private Companions _c; private readonly List<uint> _actors = new List<uint>(); private float _t;
            private readonly Dictionary<uint, Vector2Int> _others = new Dictionary<uint, Vector2Int>();
            public void Begin(Companions c)
            {
                _c = c; _t = Time.time; _actors.Clear(); _others.Clear();
                var all = c.W4AiActors?.Invoke();
                if (all != null) foreach (var u in all) if (u != null) _actors.Add(u.Id);
                var more = c.W5AiOtherActors?.Invoke();
                if (more != null) foreach (var o in more) if (o.id != 0 && !_others.ContainsKey(o.id)) { _others[o.id] = new Vector2Int(o.x, o.y); _actors.Add(o.id); }
            }
            private Unit U(uint id) => id != 0 ? _c._unit(id) : null;
            public uint OwnerId => _c._self()?.Id ?? 0;
            public IReadOnlyList<uint> Actors() => _actors;
            public bool Position(uint id, out int x, out int y)
            {
                var u = U(id);
                if (u == null)
                {
                    if (id != 0 && _others.TryGetValue(id, out var c)) { x = c.x; y = c.y; return true; } // an item or a skill on the ground
                    x = y = -1; return false;
                }
                var p = u.PositionAt(_t);
                x = Mathf.RoundToInt(p.x); y = Mathf.RoundToInt(p.y);
                return true;
            }
            public int Motion(uint id)
            {
                var u = U(id);
                if (u == null) return AiConst.MOTION_STAND;
                if (u.Walking) return AiConst.MOTION_MOVE;
                int g = u.Sprite != null ? u.Sprite.ActionGroup : 0;
                if (u.IsPlayerType)
                {
                    if (g == (int)ActorSprite.PlayerAction.Dead) return AiConst.MOTION_DEAD;
                    if (g == (int)ActorSprite.PlayerAction.Attack1 || g == (int)ActorSprite.PlayerAction.Attack2 || g == (int)ActorSprite.PlayerAction.Attack3) return AiConst.MOTION_ATTACK;
                    return AiConst.MOTION_STAND;
                }
                if (g == (int)ActorSprite.MonsterAction.Dead) return AiConst.MOTION_DEAD;
                if (g == (int)ActorSprite.MonsterAction.Attack) return AiConst.MOTION_ATTACK;
                return AiConst.MOTION_STAND;
            }
            public uint Target(uint id) => _c._aiTargets.TryGetValue(id, out var t) ? t : 0;
            public bool IsMonster(uint id)
            {
                var u = U(id);
                return u != null && u.Kind == Unit.UnitKind.Monster && u.Sprite != null && u.Sprite.ActionGroup != (int)ActorSprite.MonsterAction.Dead && u.Sprite.Tint.a > 0.01f;
            }
            public int Class(uint id) { var u = U(id); return u != null ? u.Job : -1; }
            public int AttackRange(uint id) => Mathf.Max(1, id != 0 && id == _c.HomId ? _c.HomRange : id != 0 && id == _c.MercId ? _c.MercRange : 1);
            private CSkill Skill(uint id, int skill)
            {
                var list = id != 0 && id == _c.HomId ? _c.HomSkills : id != 0 && id == _c.MercId ? _c.MercSkills : null;
                return list?.Find(k => k.Id == skill);
            }
            public int SkillRange(uint id, int skill, int level) { var k = Skill(id, skill); return k != null && k.Range > 0 ? k.Range : AttackRange(id); }
            public int SkillLevel(uint id, int skill) { var k = Skill(id, skill); return k != null ? k.Level : 0; }
            public bool Vitals(uint id, int what, out long value)
            {
                value = 0;
                if (id == 0) return false;
                if (id == _c.HomId) { value = what == AiConst.V_HP ? _c.HomHp : what == AiConst.V_SP ? _c.HomSp : what == AiConst.V_MAXHP ? _c.HomMaxHp : _c.HomMaxSp; return true; }
                if (id == _c.MercId) { value = what == AiConst.V_HP ? _c.MercHp : what == AiConst.V_SP ? _c.MercSp : what == AiConst.V_MAXHP ? _c.MercMaxHp : _c.MercMaxSp; return true; }
                if (id == OwnerId && _c.W5OwnerVitals != null)
                {
                    var v = _c.W5OwnerVitals();
                    value = what == AiConst.V_HP ? v.hp : what == AiConst.V_SP ? v.sp : what == AiConst.V_MAXHP ? v.maxHp : v.maxSp;
                    return true;
                }
                return false;
            }
            /// <summary>GetTick: the Korean manual (AI\Homunculus AI script Manual.htm, section 3, 6): "this value starts at 0 when the
            /// computer starts and grows by 1 every 1/1000 second" — the Windows tick count.</summary>
            public double TickMs => (uint)Environment.TickCount;
        }

        private sealed class LiveActions : IAiActions
        {
            public Companions C; public W4Brain Brain;
            public void MoveToOwner(uint id) { C._s.SendPacket(new PacketWriter(0x0234).U32(id).ToArray()); if (Brain != null) Brain.Follows++; C.TraceReq(Brain, "MoveToOwner"); C.W5AiRecord("MoveToOwner " + id); }
            public void Move(uint id, int x, int y)
            {
                if (x < 0 || y < 0) return; // rAthena drops them (clif.cpp:15733)
                C.MoveTo(id, x, y); if (Brain != null) Brain.Moves++; C.TraceReq(Brain, $"Move ({x},{y})"); C.W5AiRecord($"Move {id} {x} {y}");
            }
            public void Attack(uint id, uint target) { C._s.SendPacket(new PacketWriter(0x0233).U32(id).U32(target).U8(0).ToArray()); if (Brain != null) Brain.Attacks++; C.W5AiRecord($"Attack {id} {target}"); }
            public void SkillObject(uint id, int level, int skill, uint target)
            {
                int have = C._world.SkillLevel(id, skill);
                if (have <= 0 || level <= 0) return;
                int lv = Math.Min(level, have);
                C._s.UseSkill(skill, lv, target); if (Brain != null) Brain.SkillsSent++; C.TraceReq(Brain, $"SkillObject {skill} lv {lv} → {target}"); C.W5AiRecord($"SkillObject {id} {lv} {skill} {target}");
            }
            public void SkillGround(uint id, int level, int skill, int x, int y)
            {
                int have = C._world.SkillLevel(id, skill);
                if (have <= 0 || level <= 0) return;
                int lv = Math.Min(level, have);
                C._s.UseSkillOnGround(skill, lv, x, y); if (Brain != null) Brain.SkillsSent++; C.TraceReq(Brain, $"SkillGround {skill} lv {lv} at ({x},{y})"); C.W5AiRecord($"SkillGround {id} {lv} {skill} {x} {y}");
            }
        }

        private void TraceReq(W4Brain b, string what) { if (b != null) Trace(b, what); }
        private static void Trace(W4Brain b, string what) { if (W4AiLog) Debug.Log($"[W4Ai] {(b.Merc ? "mercenary" : "homunculus")} {b.MyId}{(b.Script != null ? " (custom AI)" : "")}: {what}"); }
    }
}
