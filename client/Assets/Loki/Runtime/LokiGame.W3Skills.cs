using Loki.Game;
using Loki.Net;

namespace Loki
{
    /// <summary>
    /// Client wave 3 (w3skills): wires the wrist menu's skill window to the character's job. The job starts as the selected
    /// character's (CHARACTER_INFO.job = status.class_) and follows ZC_SPRITE_CHANGE 0x01d7 LOOK_BASE for our own AID, which
    /// rAthena sends to the area, us included, on every job change (pc_jobchange → clif_changelook(LOOK_BASE), pc.cpp:10999;
    /// clif_changelook → clif_sprite_change, clif.cpp:3935-3960 / 4090-4108). Riding sprite classes are mapped back to the job
    /// (SkillTreeDb.JobFromLook); a disguise's monster class is ignored. Only RO packets: the same on any server.
    /// </summary>
    public sealed partial class LokiGame
    {
        private int _w3SkillJob = -1; private uint _w3SkillJobChar;

        private void BeginW3Skills(LokiBootstrap boot)
        {
            if (Hud != null && Hud.Wrist != null) Hud.Wrist.SkillJob = W3SkillJob;
            Session.OnMapPacket += W3SkillsListen;
        }

        private int W3SkillJob()
        {
            var ch = Session?.SelectedChar;
            if (ch == null) return 0;
            return _w3SkillJob >= 0 && _w3SkillJobChar == ch.CharId ? _w3SkillJob : ch.Job;
        }

        private void W3SkillsListen(RoSession s, ushort id, byte[] b, int o, int n)
        {
            if (id != 0x01d7 || n < 15 || s.SelectedChar == null) return; // ZC_SPRITE_CHANGE: AID.L type.B val.L val2.L (packets_struct.hpp:2591-2597, clif.cpp:3950-3958)
            var r = new PacketReader(b, o, n); r.Skip(2);
            uint aid = r.U32(); int type = r.U8(); int val = r.S32();
            if (aid != s.AccountId || type != 0) return; // LOOK_BASE = 0 (map.hpp:595-596 enum _look)
            int job = SkillTreeDb.JobFromLook(val);
            if (job < 0) return;
            if (job != _w3SkillJob) UnityEngine.Debug.Log($"[Skills] job {job} {SkillTreeDb.JobName(job)} (LOOK_BASE {val})");
            _w3SkillJob = job; _w3SkillJobChar = s.SelectedChar.CharId;
            Hud?.Wrist?.MarkDirty();
        }
    }
}
