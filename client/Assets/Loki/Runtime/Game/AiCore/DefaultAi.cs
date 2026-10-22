using System;
using System.Collections.Generic;
using static Loki.Game.AiCore.AiConst;

namespace Loki.Game.AiCore
{
    /// <summary>
    /// The official default AI in C#: client wave 4 (w4units) ported the RO folder's AI\AI.lua (homunculus) and AI\AI_M.lua
    /// (mercenary) with AI\Util.lua — dated 2014-04-14, documented in the client's "Homunculus AI Script User Guide" — state by state;
    /// client wave 5 (w5ai) moved that port here, over <see cref="IAiWorld"/> / <see cref="IAiActions"/> (the same functions a script gets),
    /// and completed it line by line against the two files: the commands the port had left out (ATTACK_AREA_CMD, PATROL_CMD,
    /// SKILL_OBJECT_CMD, SKILL_AREA_CMD), the reserved commands of GetResMsg (ResCmdList, at most 10), the skill-range branch of
    /// IsInAttackSight, V_POSITION_APPLY_SKILLATTACKRANGE in CHASE, Util.lua's math.floor / math.sqrt in doubles. Gravity's scripts are not
    /// shipped or loaded by it: this is their logic. Where the C# reads differently from what the script would do, it says so:
    ///  • OnHOLD_CMD_ST: the script calls GetDistance(MyEnemy, MyID) — two ids where Util.lua's GetDistance takes four coordinates, so
    ///    Lua stops there with "attempt to perform arithmetic on local 'x2' (a nil value)"; the port reads the distance between the two
    ///    units (w4units' reading). A script run through Loki's Lua shows that error and the port takes over.
    /// Every quirk the script has is kept: GetOwnerEnemy reads GetV(V_MOTION, i) with the list index i (so in practice only monsters
    /// count); Lif / Amistr only defend (GetMyEnemyA), Filir / Vanilmirth attack any monster in view (GetMyEnemyB); the mercenary ignores
    /// a monster that targets someone else (CanHitEnemy) and tries MyBestSkill 8201 lv 5 while its SP is above 8.
    /// </summary>
    public sealed class DefaultAi
    {
        public const int IDLE_ST = 0, FOLLOW_ST = 1, CHASE_ST = 2, ATTACK_ST = 3, MOVE_CMD_ST = 4, STOP_CMD_ST = 5, ATTACK_OBJECT_CMD_ST = 6,
            ATTACK_AREA_CMD_ST = 7, PATROL_CMD_ST = 8, HOLD_CMD_ST = 9, SKILL_OBJECT_CMD_ST = 10, SKILL_AREA_CMD_ST = 11, FOLLOW_CMD_ST = 12;

        public readonly bool Merc;
        // the script's globals (AI.lua "global variable")
        public int MyState = IDLE_ST;
        public double MyEnemy, MyDestX, MyDestY, MyPatrolX, MyPatrolY, MySkill, MySkillLevel;
        public uint MyID;
        // AI_M.lua: MySP = 8, MyBestSkill = 8201, MyBestSkillLevel = 5
        public const double MySP = 8, MyBestSkill = 8201, MyBestSkillLevel = 5;
        /// <summary>ResCmdList (Util.lua's List: pushleft / pushright / popleft / clear / size).</summary>
        public readonly LinkedList<double[]> ResCmdList = new LinkedList<double[]>();
        /// <summary>Called with each TraceAI text the script would write.</summary>
        public Action<string> Trace;

        private IAiWorld W; private IAiActions A;

        public DefaultAi(bool merc) { Merc = merc; }

        private void TraceAI(string s) => Trace?.Invoke(s);

        // ------------------------------------------------------------------ the API calls, as the script makes them
        private double GetV1(int v, double id, double skill = 0, double level = 0) { AiGetV.Get(W, v, Id(id), (int)skill, (int)level, out double a, out _); return a; }
        private void GetV2(int v, double id, out double x, out double y, double skill = 0, double level = 0) => AiGetV.Get(W, v, Id(id), (int)skill, (int)level, out x, out y);
        private static uint Id(double d) => d >= 0 && d <= uint.MaxValue ? (uint)d : 0;
        private int IsMonster(double id) => W.IsMonster(Id(id)) ? 1 : 0;
        private void Move(double id, double x, double y) => A.Move(Id(id), (int)x, (int)y);
        private void MoveToOwner(double id) => A.MoveToOwner(Id(id));
        private void Attack(double id, double target) => A.Attack(Id(id), Id(target));
        /// <summary>SkillObject: the guide says "Return value: none"; the scripts' "1 == SkillObject(…)" is therefore never true.</summary>
        private object SkillObject(double id, double level, double skill, double target) { A.SkillObject(Id(id), (int)level, (int)skill, Id(target)); return null; }
        private void SkillGround(double id, double level, double skill, double x, double y) => A.SkillGround(Id(id), (int)level, (int)skill, (int)x, (int)y);

        // ------------------------------------------------------------------ Util.lua
        private int GetDistance2(double id1, double id2)
        {
            GetV2(V_POSITION, id1, out double x1, out double y1);
            GetV2(V_POSITION, id2, out double x2, out double y2);
            if (x1 == -1 || x2 == -1) return -1;
            return AiMath.Distance(x1, y1, x2, y2);
        }
        private int GetDistanceFromOwner(double id)
        {
            GetV2(V_POSITION, GetV1(V_OWNER, id), out double x1, out double y1);
            GetV2(V_POSITION, id, out double x2, out double y2);
            if (x1 == -1 || x2 == -1) return -1;
            return AiMath.Distance(x1, y1, x2, y2);
        }
        private bool IsOutOfSight(double id1, double id2)
        {
            GetV2(V_POSITION, id1, out double x1, out double y1);
            GetV2(V_POSITION, id2, out double x2, out double y2);
            if (x1 == -1 || x2 == -1) return true;
            return AiMath.Distance(x1, y1, x2, y2) > 20;
        }
        private bool IsInAttackSight(double id1, double id2)
        {
            GetV2(V_POSITION, id1, out double x1, out double y1);
            GetV2(V_POSITION, id2, out double x2, out double y2);
            if (x1 == -1 || x2 == -1) return false;
            int d = AiMath.Distance(x1, y1, x2, y2);
            double a = MySkill == 0 ? GetV1(V_ATTACKRANGE, id1) : GetV1(V_SKILLATTACKRANGE_LEVEL, id1, MySkill, MySkillLevel);
            return a >= d;
        }

        // ------------------------------------------------------------------ AI(myid)
        /// <summary>One AI(myid) call: <paramref name="msg"/> = GetMsg, <paramref name="rmsg"/> = GetResMsg ({0} = NONE_CMD).</summary>
        public void Tick(uint myid, double[] msg, double[] rmsg, IAiWorld world, IAiActions actions)
        {
            W = world; A = actions;
            MyID = myid;
            if (msg[0] == NONE_CMD)
            {
                if (rmsg[0] != NONE_CMD && ResCmdList.Count < 10) ResCmdList.AddLast(rmsg);
            }
            else
            {
                ResCmdList.Clear();
                ProcessCommand(msg);
            }
            switch (MyState)
            {
                case IDLE_ST: OnIDLE_ST(); break;
                case CHASE_ST: OnCHASE_ST(); break;
                case ATTACK_ST: OnATTACK_ST(); break;
                case FOLLOW_ST: OnFOLLOW_ST(); break;
                case MOVE_CMD_ST: OnMOVE_CMD_ST(); break;
                case STOP_CMD_ST: break;
                case ATTACK_OBJECT_CMD_ST: break;
                case ATTACK_AREA_CMD_ST: OnATTACK_AREA_CMD_ST(); break;
                case PATROL_CMD_ST: OnPATROL_CMD_ST(); break;
                case HOLD_CMD_ST: OnHOLD_CMD_ST(); break;
                case SKILL_OBJECT_CMD_ST: break;
                case SKILL_AREA_CMD_ST: OnSKILL_AREA_CMD_ST(); break;
                case FOLLOW_CMD_ST: OnFOLLOW_CMD_ST(); break;
            }
        }

        private static double Arg(double[] m, int i) => i < m.Length ? m[i] : 0;

        private void ProcessCommand(double[] msg)
        {
            if (Merc) CanHitEnemy(); // AI_M.lua ProcessCommand
            switch ((int)msg[0])
            {
                case MOVE_CMD: OnMOVE_CMD(Arg(msg, 1), Arg(msg, 2)); TraceAI("MOVE_CMD"); break;
                case STOP_CMD: OnSTOP_CMD(); TraceAI("STOP_CMD"); break;
                case ATTACK_OBJECT_CMD: OnATTACK_OBJECT_CMD(Arg(msg, 1)); TraceAI("ATTACK_OBJECT_CMD"); break;
                case ATTACK_AREA_CMD: OnATTACK_AREA_CMD(Arg(msg, 1), Arg(msg, 2)); TraceAI("ATTACK_AREA_CMD"); break;
                case PATROL_CMD: OnPATROL_CMD(Arg(msg, 1), Arg(msg, 2)); TraceAI("PATROL_CMD"); break;
                case HOLD_CMD: OnHOLD_CMD(); TraceAI("HOLD_CMD"); break;
                case SKILL_OBJECT_CMD: OnSKILL_OBJECT_CMD(Arg(msg, 1), Arg(msg, 2), Arg(msg, 3)); TraceAI("SKILL_OBJECT_CMD"); break;
                case SKILL_AREA_CMD: OnSKILL_AREA_CMD(Arg(msg, 1), Arg(msg, 2), Arg(msg, 3), Arg(msg, 4)); TraceAI("SKILL_AREA_CMD"); break;
                case FOLLOW_CMD: OnFOLLOW_CMD(); TraceAI("FOLLOW_CMD"); break;
            }
        }

        // ------------------------------------------------------------------ commands
        private void OnMOVE_CMD(double x, double y)
        {
            TraceAI("OnMOVE_CMD");
            if (x == MyDestX && y == MyDestY && MOTION_MOVE == GetV1(V_MOTION, MyID)) return;
            GetV2(V_POSITION, MyID, out double curX, out double curY);
            if (Math.Abs(x - curX) + Math.Abs(y - curY) > 15)
            {
                ResCmdList.AddFirst(new[] { (double)MOVE_CMD, x, y });
                x = Math.Floor((x + curX) / 2);
                y = Math.Floor((y + curY) / 2);
            }
            Move(MyID, x, y);
            MyState = MOVE_CMD_ST; MyDestX = x; MyDestY = y; MyEnemy = 0; MySkill = 0;
        }

        private void OnSTOP_CMD()
        {
            TraceAI("OnSTOP_CMD");
            if (GetV1(V_MOTION, MyID) != MOTION_STAND) { GetV2(V_POSITION, MyID, out double x, out double y); Move(MyID, x, y); }
            MyState = IDLE_ST; MyDestX = 0; MyDestY = 0; MyEnemy = 0; MySkill = 0;
        }

        private void OnATTACK_OBJECT_CMD(double id) { TraceAI("OnATTACK_OBJECT_CMD"); MySkill = 0; MyEnemy = id; MyState = CHASE_ST; }

        private void OnATTACK_AREA_CMD(double x, double y)
        {
            TraceAI("OnATTACK_AREA_CMD");
            if (x != MyDestX || y != MyDestY || MOTION_MOVE != GetV1(V_MOTION, MyID)) Move(MyID, x, y);
            MyDestX = x; MyDestY = y; MyEnemy = 0; MyState = ATTACK_AREA_CMD_ST;
        }

        private void OnPATROL_CMD(double x, double y)
        {
            TraceAI("OnPATROL_CMD");
            GetV2(V_POSITION, MyID, out MyPatrolX, out MyPatrolY);
            MyDestX = x; MyDestY = y;
            Move(MyID, x, y);
            MyState = PATROL_CMD_ST;
        }

        private void OnHOLD_CMD() { TraceAI("OnHOLD_CMD"); MyDestX = 0; MyDestY = 0; MyEnemy = 0; MyState = HOLD_CMD_ST; }

        private void OnSKILL_OBJECT_CMD(double level, double skill, double id)
        {
            TraceAI("OnSKILL_OBJECT_CMD");
            MySkillLevel = level; MySkill = skill; MyEnemy = id; MyState = CHASE_ST;
        }

        private void OnSKILL_AREA_CMD(double level, double skill, double x, double y)
        {
            TraceAI("OnSKILL_AREA_CMD");
            Move(MyID, x, y);
            MyDestX = x; MyDestY = y; MySkillLevel = level; MySkill = skill; MyState = SKILL_AREA_CMD_ST;
        }

        private void OnFOLLOW_CMD()
        {
            // the standby command switches between standby and idle (the guide's update history, item 4)
            if (MyState != FOLLOW_CMD_ST)
            {
                MoveToOwner(MyID);
                MyState = FOLLOW_CMD_ST;
                GetV2(V_POSITION, GetV1(V_OWNER, MyID), out MyDestX, out MyDestY);
                MyEnemy = 0; MySkill = 0;
                TraceAI("OnFOLLOW_CMD");
            }
            else
            {
                MyState = IDLE_ST; MyEnemy = 0; MySkill = 0;
                TraceAI("FOLLOW_CMD_ST --> IDLE_ST");
            }
        }

        // ------------------------------------------------------------------ states
        private void OnIDLE_ST()
        {
            TraceAI("OnIDLE_ST");
            if (ResCmdList.Count > 0)
            {
                var cmd = ResCmdList.First.Value; ResCmdList.RemoveFirst();
                ProcessCommand(cmd);
                return;
            }
            double obj = GetOwnerEnemy(MyID);
            if (obj != 0) { MyState = CHASE_ST; MyEnemy = obj; TraceAI("IDLE_ST -> CHASE_ST : MYOWNER_ATTACKED_IN"); return; }
            obj = GetMyEnemy(MyID);
            if (obj != 0) { MyState = CHASE_ST; MyEnemy = obj; TraceAI("IDLE_ST -> CHASE_ST : ATTACKED_IN"); return; }
            int distance = GetDistanceFromOwner(MyID);
            if (distance > 3 || distance == -1) { MyState = FOLLOW_ST; TraceAI("IDLE_ST -> FOLLOW_ST"); }
        }

        private void OnFOLLOW_ST()
        {
            TraceAI("OnFOLLOW_ST");
            if (GetDistanceFromOwner(MyID) <= 3) { MyState = IDLE_ST; TraceAI("FOLLOW_ST -> IDLW_ST"); return; }
            if (GetV1(V_MOTION, MyID) == MOTION_STAND) { MoveToOwner(MyID); TraceAI("FOLLOW_ST -> FOLLOW_ST"); }
        }

        private void OnCHASE_ST()
        {
            TraceAI("OnCHASE_ST");
            if (IsOutOfSight(MyID, MyEnemy)) { MyState = IDLE_ST; MyEnemy = 0; MyDestX = 0; MyDestY = 0; TraceAI("CHASE_ST -> IDLE_ST : ENEMY_OUTSIGHT_IN"); return; }
            if (IsInAttackSight(MyID, MyEnemy)) { MyState = ATTACK_ST; TraceAI("CHASE_ST -> ATTACK_ST : ENEMY_INATTACKSIGHT_IN"); return; }
            // AI.lua reads V_POSITION_APPLY_SKILLATTACKRANGE, AI_M.lua V_POSITION
            int pv = Merc ? V_POSITION : V_POSITION_APPLY_SKILLATTACKRANGE;
            GetV2(pv, MyEnemy, out double x, out double y, MySkill, MySkillLevel);
            if (MyDestX != x || MyDestY != y)
            {
                GetV2(pv, MyEnemy, out MyDestX, out MyDestY, MySkill, MySkillLevel);
                Move(MyID, MyDestX, MyDestY);
                TraceAI("CHASE_ST -> CHASE_ST : DESTCHANGED_IN");
            }
        }

        private void OnATTACK_ST()
        {
            TraceAI("OnATTACK_ST");
            if (IsOutOfSight(MyID, MyEnemy)) { MyState = IDLE_ST; TraceAI("ATTACK_ST -> IDLE_ST"); return; }
            if (MOTION_DEAD == GetV1(V_MOTION, MyEnemy)) { MyState = IDLE_ST; TraceAI("ATTACK_ST -> IDLE_ST"); return; }
            if (Merc) CanHitEnemy();
            if (!IsInAttackSight(MyID, MyEnemy))
            {
                MyState = CHASE_ST;
                GetV2(Merc ? V_POSITION : V_POSITION_APPLY_SKILLATTACKRANGE, MyEnemy, out MyDestX, out MyDestY, MySkill, MySkillLevel);
                Move(MyID, MyDestX, MyDestY);
                TraceAI("ATTACK_ST -> CHASE_ST  : ENEMY_OUTATTACKSIGHT_IN");
                return;
            }
            if (Merc)
            {
                double sp = GetV1(V_SP, MyID);
                if (MyBestSkill != 0 && sp > MySP)
                {
                    if (Equals1(SkillObject(MyID, MyBestSkillLevel, MyBestSkill, MyEnemy))) { MyEnemy = 0; return; }
                }
            }
            if (MySkill == 0) Attack(MyID, MyEnemy);
            else
            {
                if (Equals1(SkillObject(MyID, MySkillLevel, MySkill, MyEnemy))) MyEnemy = 0;
                MySkill = 0;
            }
            TraceAI("ATTACK_ST -> ATTACK_ST  : ENERGY_RECHARGED_IN");
        }
        private static bool Equals1(object r) => r is double d && d == 1;

        private void OnMOVE_CMD_ST()
        {
            TraceAI("OnMOVE_CMD_ST");
            GetV2(V_POSITION, MyID, out double x, out double y);
            if (x == MyDestX && y == MyDestY) MyState = IDLE_ST;
        }

        private void OnATTACK_AREA_CMD_ST()
        {
            TraceAI("OnATTACK_AREA_CMD_ST");
            double obj = GetOwnerEnemy(MyID);
            if (obj == 0) obj = GetMyEnemy(MyID);
            if (obj != 0) { MyState = CHASE_ST; MyEnemy = obj; return; }
            GetV2(V_POSITION, MyID, out double x, out double y);
            if (x == MyDestX && y == MyDestY) MyState = IDLE_ST;
        }

        private void OnPATROL_CMD_ST()
        {
            TraceAI("OnPATROL_CMD_ST");
            double obj = GetOwnerEnemy(MyID);
            if (obj == 0) obj = GetMyEnemy(MyID);
            if (obj != 0) { MyState = CHASE_ST; MyEnemy = obj; TraceAI("PATROL_CMD_ST -> CHASE_ST : ATTACKED_IN"); return; }
            GetV2(V_POSITION, MyID, out double x, out double y);
            if (x == MyDestX && y == MyDestY)
            {
                MyDestX = MyPatrolX; MyDestY = MyPatrolY;
                MyPatrolX = x; MyPatrolY = y;
                Move(MyID, MyDestX, MyDestY);
            }
        }

        private void OnHOLD_CMD_ST()
        {
            TraceAI("OnHOLD_CMD_ST");
            if (MyEnemy != 0)
            {
                // the script: GetDistance(MyEnemy, MyID) — see the class summary; read as the distance between the two units
                int d = GetDistance2(MyEnemy, MyID);
                if (d != -1 && d <= GetV1(V_ATTACKRANGE, MyID)) Attack(MyID, MyEnemy);
                else MyEnemy = 0;
                return;
            }
            double obj = GetOwnerEnemy(MyID);
            if (obj == 0)
            {
                obj = GetMyEnemy(MyID);
                if (obj == 0) return;
            }
            MyEnemy = obj;
        }

        private void OnSKILL_AREA_CMD_ST()
        {
            TraceAI("OnSKILL_AREA_CMD_ST");
            GetV2(V_POSITION, MyID, out double x, out double y);
            // AI.lua: V_SKILLATTACKRANGE_LEVEL (MySkill, MySkillLevel); AI_M.lua: V_SKILLATTACKRANGE (MySkill)
            double range = Merc ? GetV1(V_SKILLATTACKRANGE, MyID, MySkill) : GetV1(V_SKILLATTACKRANGE_LEVEL, MyID, MySkill, MySkillLevel);
            if (AiMath.Distance(x, y, MyDestX, MyDestY) <= range)
            {
                SkillGround(MyID, MySkillLevel, MySkill, MyDestX, MyDestY);
                MyState = IDLE_ST; MySkill = 0;
            }
        }

        private void OnFOLLOW_CMD_ST()
        {
            TraceAI("OnFOLLOW_CMD_ST");
            GetV2(V_POSITION, GetV1(V_OWNER, MyID), out double ownerX, out double ownerY);
            GetV2(V_POSITION, MyID, out double myX, out double myY);
            int d = AiMath.Distance(ownerX, ownerY, myX, myY);
            if (d <= 3) return;
            double motion = GetV1(V_MOTION, MyID);
            if (motion == MOTION_MOVE)
            {
                d = AiMath.Distance(ownerX, ownerY, MyDestX, MyDestY);
                if (d > 3) { MoveToOwner(MyID); MyDestX = ownerX; MyDestY = ownerY; }
            }
            else { MoveToOwner(MyID); MyDestX = ownerX; MyDestY = ownerY; }
        }

        // ------------------------------------------------------------------ enemies
        private double GetOwnerEnemy(double myid)
        {
            double result = 0;
            double owner = GetV1(V_OWNER, myid);
            var actors = W.Actors();
            var enemys = new List<double>();
            for (int i = 0; i < actors.Count; i++)
            {
                double v = actors[i];
                if (v == owner || v == myid) continue;
                double target = GetV1(V_TARGET, v);
                if (target != owner) continue;
                if (IsMonster(v) == 1) enemys.Add(v);
                else
                {
                    double motion = GetV1(V_MOTION, i + 1); // the script passes the list index i (ipairs: 1, 2, …), not the id
                    if (motion == MOTION_ATTACK || motion == MOTION_ATTACK2) enemys.Add(v);
                }
            }
            return Nearest(myid, enemys, result);
        }

        private double Nearest(double myid, List<double> enemys, double result)
        {
            int min = 100;
            foreach (var v in enemys)
            {
                int dis = GetDistance2(myid, v);
                if (dis < min) { result = v; min = dis; }
            }
            return result;
        }

        private double GetMyEnemy(double myid)
        {
            if (Merc)
            {
                // AI_M.lua: both branches of its V_MERTYPE test call GetMyEnemyB
                GetV1(V_MERTYPE, myid);
                return GetMyEnemyB(myid);
            }
            double type = GetV1(V_HOMUNTYPE, myid);
            if (type == LIF || type == LIF_H || type == AMISTR || type == AMISTR_H || type == LIF2 || type == LIF_H2 || type == AMISTR2 || type == AMISTR_H2)
                return GetMyEnemyA(myid);
            if (type == FILIR || type == FILIR_H || type == VANILMIRTH || type == VANILMIRTH_H || type == FILIR2 || type == FILIR_H2 || type == VANILMIRTH2 || type == VANILMIRTH_H2)
                return GetMyEnemyB(myid);
            return 0;
        }

        /// <summary>Not aggressive: whoever targets me.</summary>
        private double GetMyEnemyA(double myid)
        {
            double owner = GetV1(V_OWNER, myid);
            var actors = W.Actors();
            var enemys = new List<double>();
            foreach (double v in actors)
            {
                if (v == owner || v == myid) continue;
                if (GetV1(V_TARGET, v) == myid) enemys.Add(v);
            }
            return Nearest(myid, enemys, 0);
        }

        /// <summary>Aggressive: any monster (the mercenary's: one that targets me, my owner or nothing).</summary>
        private double GetMyEnemyB(double myid)
        {
            double owner = GetV1(V_OWNER, myid);
            var actors = W.Actors();
            var enemys = new List<double>();
            foreach (double v in actors)
            {
                if (v == owner || v == myid) continue;
                if (Merc)
                {
                    double target = GetV1(V_TARGET, v);
                    if (!(target == myid || target == owner || target == 0)) continue;
                }
                if (IsMonster(v) == 1) enemys.Add(v);
            }
            return Nearest(myid, enemys, 0);
        }

        /// <summary>AI_M.lua CanHitEnemy: an enemy that targets someone else than me or my owner is dropped.</summary>
        private void CanHitEnemy()
        {
            double owner = GetV1(V_OWNER, MyID);
            double target = GetV1(V_TARGET, MyEnemy);
            if (target != MyID && target != owner && target != 0) MyEnemy = 0;
        }
    }
}
