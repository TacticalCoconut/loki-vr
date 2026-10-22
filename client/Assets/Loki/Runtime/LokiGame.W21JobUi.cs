using Loki.Game;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 21 (w21jobui; design note, 2026-10-11): the panel theme's JOB VARIANTS (UI/JobTheme.cs) are told here whose job is whose.
    /// OURS: the skill window's job (LokiGame.W3Skills.cs W3SkillJob: the character's class as the server last sent it, a riding
    /// look mapped back to its job), -1 while no character of ours stands on a map: the login and data panels wear the base look.
    /// A job change and a rebirth reach us as the server's own sprite change (ZC_SPRITE_CHANGE 0x01d7 LOOK_BASE: rAthena
    /// pc_jobchange, pc.cpp, sends it through clif_changelook), which W3SkillsListen already reads: UI/JobTheme.cs asks every frame
    /// and re-dresses every open panel the moment the answer changes.
    /// A FRIEND'S (the owner of shared casting menus, UI/FriendGestures.cs): their unit's class while it is in view
    /// (Sprites/EntityManager.cs), else what our party list says (Game/Party.cs), else the job seen last; never seen: -1, the base look.
    /// </summary>
    public sealed partial class LokiGame
    {
        private readonly System.Collections.Generic.Dictionary<uint, int> _w21JobSeen = new System.Collections.Generic.Dictionary<uint, int>();

        private void StartW21JobUi()
        {
            JobTheme.Job = W21JobUiJob;
            JobTheme.JobOfAccount = W21JobUiJobOf;
        }

        private int W21JobUiJob()
        {
            if (Session == null || Session.SelectedChar == null || Entities == null || Entities.Self == null) return -1;
            return W3SkillJob();
        }

        private int W21JobUiJobOf(uint aid)
        {
            int job = -1;
            var u = Entities != null ? Entities.Get(aid) : null;
            if (u != null) job = SkillTreeDb.JobFromLook(u.Job);
            if (job < 0 && Party != null && Party.Members.TryGetValue(aid, out var m)) job = SkillTreeDb.JobFromLook(m.Job);
            if (job >= 0) { _w21JobSeen[aid] = job; return job; }
            return _w21JobSeen.TryGetValue(aid, out int seen) ? seen : -1;
        }
    }
}
