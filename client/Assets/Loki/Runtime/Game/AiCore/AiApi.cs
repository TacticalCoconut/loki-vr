using System;
using System.Collections.Generic;

namespace Loki.Game.AiCore
{
    // Client wave 5 (w5ai): the homunculus / mercenary AI the official client runs from the player's RO folder, as one interface for
    // its two implementations — the official script logic in C# (DefaultAi.cs: w4units' port of AI\AI.lua / AI_M.lua) and any
    // AI.lua run by Loki's Lua 5.1 (AiScript.cs). Unity-free: the offline harness compiles these files too.

    /// <summary>The constants of the AI API, as the RO folder's AI\Const.lua defines them (2014-04-14 copy) and the client's "Homunculus
    /// AI Script User Guide" lists them (section 4).</summary>
    public static class AiConst
    {
        public const int V_OWNER = 0, V_POSITION = 1, V_TYPE = 2, V_MOTION = 3, V_ATTACKRANGE = 4, V_TARGET = 5, V_SKILLATTACKRANGE = 6,
            V_HOMUNTYPE = 7, V_HP = 8, V_SP = 9, V_MAXHP = 10, V_MAXSP = 11, V_MERTYPE = 12, V_POSITION_APPLY_SKILLATTACKRANGE = 13,
            V_SKILLATTACKRANGE_LEVEL = 14;
        public const int MOTION_STAND = 0, MOTION_MOVE = 1, MOTION_ATTACK = 2, MOTION_DEAD = 3, MOTION_ATTACK2 = 9;
        public const int NONE_CMD = 0, MOVE_CMD = 1, STOP_CMD = 2, ATTACK_OBJECT_CMD = 3, ATTACK_AREA_CMD = 4, PATROL_CMD = 5, HOLD_CMD = 6,
            SKILL_OBJECT_CMD = 7, SKILL_AREA_CMD = 8, FOLLOW_CMD = 9;
        // homunculus types (Const.lua: LIF 1 … VANILMIRTH_H2 16)
        public const int LIF = 1, AMISTR = 2, FILIR = 3, VANILMIRTH = 4, LIF2 = 5, AMISTR2 = 6, FILIR2 = 7, VANILMIRTH2 = 8,
            LIF_H = 9, AMISTR_H = 10, FILIR_H = 11, VANILMIRTH_H = 12, LIF_H2 = 13, AMISTR_H2 = 14, FILIR_H2 = 15, VANILMIRTH_H2 = 16;
        // mercenary types (Const.lua: ARCHER01 1 … SWORDMAN10 30)
        public const int ARCHER01 = 1, SWORDMAN10 = 30;
        /// <summary>rAthena's first mercenary class: MERID_MER_ARCHER01 6017 … MERID_MER_SWORDMAN10 6046 in Const.lua's order
        /// (mercenary.hpp:22-29), so V_MERTYPE = class − 6016.</summary>
        public const int MercClassBase = 6016;
        /// <summary>Homunculus classes 6001-6016 / 6048-6052 (homunculus.hpp): V_HOMUNTYPE = class − 6000 (roBrowserLegacy, Core/AIDriver.js:183-184: job % 6000).</summary>
        public const int HomClassBase = 6000;
    }

    /// <summary>
    /// What an AI script sees: the client's view of the world at one instant (every call within one AI(id) run reads the same instant).
    /// Each member says which GetV / API function it answers.
    /// </summary>
    public interface IAiWorld
    {
        /// <summary>V_OWNER: the owner's id (the player's account id).</summary>
        uint OwnerId { get; }
        /// <summary>GetActors: the ids of everything in view, in a fixed order for this instant.</summary>
        IReadOnlyList<uint> Actors();
        /// <summary>V_POSITION: the cell, false when the id is unknown (the script gets -1, -1).</summary>
        bool Position(uint id, out int x, out int y);
        /// <summary>V_MOTION: MOTION_* (MOTION_STAND for an unknown id).</summary>
        int Motion(uint id);
        /// <summary>V_TARGET: the id the unit attacked or cast at last, 0 when none was seen.</summary>
        uint Target(uint id);
        /// <summary>IsMonster.</summary>
        bool IsMonster(uint id);
        /// <summary>The unit's class (job), -1 when unknown.</summary>
        int Class(uint id);
        /// <summary>V_ATTACKRANGE of a unit (at least 1).</summary>
        int AttackRange(uint id);
        /// <summary>V_SKILLATTACKRANGE(_LEVEL): the range of a companion's skill (its attack range when it does not have the skill).</summary>
        int SkillRange(uint id, int skill, int level);
        /// <summary>The level a companion has of a skill (0 = not learned).</summary>
        int SkillLevel(uint id, int skill);
        /// <summary>V_HP / V_SP / V_MAXHP / V_MAXSP: known for the companions and the owner (Const.lua: "applies to the homunculus and
        /// its owner only"); false otherwise.</summary>
        bool Vitals(uint id, int what, out long value);
        /// <summary>GetTick: milliseconds.</summary>
        double TickMs { get; }
    }

    /// <summary>What an AI script does: the requests of the API functions (MoveToOwner, Move, Attack, SkillObject, SkillGround).</summary>
    public interface IAiActions
    {
        void MoveToOwner(uint id);
        void Move(uint id, int x, int y);
        void Attack(uint id, uint target);
        void SkillObject(uint id, int level, int skill, uint target);
        void SkillGround(uint id, int level, int skill, int x, int y);
    }

    /// <summary>The AI's own arithmetic, as AI\Util.lua defines it.</summary>
    public static class AiMath
    {
        /// <summary>Util.lua GetDistance: math.floor(math.sqrt((x1-x2)^2+(y1-y2)^2)).</summary>
        public static int Distance(double x1, double y1, double x2, double y2) => (int)Math.Floor(Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2)));
    }

    /// <summary>
    /// The GetV function both implementations read (one place, so they cannot drift apart). Sources per value:
    ///  • V_OWNER, V_POSITION, V_MOTION, V_TARGET, V_HOMUNTYPE: Const.lua / the guide; positions -1, -1 when unknown (Util.lua tests x == -1;
    ///    roBrowserLegacy AIDriver.js:120-166 the same);
    ///  • V_TYPE: "not implemented yet" (Const.lua, the guide) — 1, as roBrowserLegacy answers (AIDriver.js:167-169, its only source);
    ///  • V_ATTACKRANGE: the companion's range from the server (the guide: "temporarily set as 1 cell"; at least 1);
    ///  • V_SKILLATTACKRANGE / _LEVEL: the skill's range from the companion's skill list (the server's range for the learned level);
    ///    the attack range otherwise (roBrowserLegacy: always the attack range, AIDriver.js:180-181, 202-207);
    ///  • V_POSITION_APPLY_SKILLATTACKRANGE: the unit's cell (roBrowserLegacy AIDriver.js:120-166; no description beyond Const.lua's name);
    ///  • V_HP … V_MAXSP: the companions' and the owner's values (Const.lua: "homunculus and owner only"), -1 for others (roBrowserLegacy);
    ///  • V_MERTYPE: class − 6016 (rAthena mercenary.hpp:22-29 numbers the 30 guild mercenaries in Const.lua's order), 0 when unknown.
    /// Results are doubles (Lua numbers); V_POSITION and V_POSITION_APPLY_SKILLATTACKRANGE have two.
    /// </summary>
    public static class AiGetV
    {
        public static int Count(int v) => v == AiConst.V_POSITION || v == AiConst.V_POSITION_APPLY_SKILLATTACKRANGE ? 2 : 1;

        public static void Get(IAiWorld w, int v, uint id, int skill, int level, out double a, out double b)
        {
            b = 0;
            switch (v)
            {
                case AiConst.V_OWNER: a = w.OwnerId; return;
                case AiConst.V_POSITION:
                case AiConst.V_POSITION_APPLY_SKILLATTACKRANGE:
                    if (w.Position(id, out int x, out int y)) { a = x; b = y; } else { a = -1; b = -1; }
                    return;
                case AiConst.V_TYPE: a = 1; return;
                case AiConst.V_MOTION: a = w.Motion(id); return;
                case AiConst.V_ATTACKRANGE: a = w.AttackRange(id); return;
                case AiConst.V_TARGET: a = w.Target(id); return;
                case AiConst.V_SKILLATTACKRANGE:
                case AiConst.V_SKILLATTACKRANGE_LEVEL: a = w.SkillRange(id, skill, level); return;
                case AiConst.V_HOMUNTYPE: { int c = w.Class(id); a = c >= 0 ? c - AiConst.HomClassBase : -1; return; }
                case AiConst.V_HP: case AiConst.V_SP: case AiConst.V_MAXHP: case AiConst.V_MAXSP:
                    a = w.Vitals(id, v, out long val) ? val : -1; return;
                case AiConst.V_MERTYPE: { int c = w.Class(id); a = c >= 0 ? c - AiConst.MercClassBase : 0; return; }
                default: a = 0; return; // an unknown V_ (roBrowserLegacy AIDriver.js:208-212 answers 0)
            }
        }
    }
}
