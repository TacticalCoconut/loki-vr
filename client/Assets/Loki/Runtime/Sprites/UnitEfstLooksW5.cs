using Loki.World;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 5 (w5looks): the status looks roBrowserLegacy draws and wave 4 left (design notes "Left"), from the status
    /// icons of the units in view (ZC_MSG_STATE_CHANGE 0x0983 / ZC_STATE_CHANGE 0x0196 when a status starts or ends — clif_status_change
    /// sends a player's to the area, clif.cpp:6535-6561 —, ZC_EFST_SET_ENTER 0x0984 for the DisplayPc ones when the unit comes into view,
    /// clif.cpp:6598-6658). Ids: rAthena's efst_type (status.hpp:1453…), which the client's efstids follow. Looks: roBrowserLegacy
    /// Engine/MapEngine/Entity.js onEntityStatusChange (a reimplementation; the official client could not be checked):
    ///  • EFST_SUMMON1..5 363-367 (a Warlock's elemental spheres): each slot keeps its val1 (the element, never 0 while on) and the spheres
    ///    drawn are the non-zero slots (Entity.js:2168-2211, updateWarlockSpheres 2417-2432) — Sprites/SpiritSpheres.cs <see cref="SpiritSpheres.OfWarlock"/>;
    ///  • EFST_TRUESIGHT 115 (SN_SIGHT, a player only: status.yml:1312-1329 has no BlEffect): rB's handler, for the start and the end alike
    ///    (no state test), plays IDLE looping from frame 3 (Entity.js:2118-2126) — <see cref="ActorSprite.PlayFrom"/>;
    ///  • EFST_C_MARKER 755 (RL_C_MARKER; BlEffect + DisplayPc: players, monsters and the other unit kinds, status.yml:5950-5960, status.cpp:16419-
    ///    16420): when it starts, 'ef_c_marker2' on the unit (Entity.js:2315-2324): two 2D parts of effect\rl_c_maker\ome.bmp (size 50 → 100, then
    ///    100 → 50, 500 ms each, posz 5, alpha up to 0.8, blend mode 2), attached (DB/Effects/EffectTable.js:16167-16191);
    ///  • EFST_ILLUSION 34 on ourselves: Fx/ScreenFlip.cs (LokiGame.W5Looks).
    /// Not drawn: the Werewolf / Wereraptor forms — rB's arms compare the icon with StatusConst.WEREWOLF / WERERAPTOR (Entity.js:1944-1953,
    /// 2278-2293), which DB/Status/StatusConst.js does not define (both undefined: the arms never run), and rAthena e985006 has no such status
    /// (no WEREWOLF in src/ or db/, e_job ends at JOB_SECOND_JOB_END 4350 + JOB_MAX, mmo.hpp:1110-1112, while rB's JobId.WEREWOLF is 4356).
    /// </summary>
    public sealed class UnitEfstLooksW5 : MonoBehaviour
    {
        public const int EfstIllusion = 34, EfstTrueSight = 115, EfstSummon1 = 363, EfstSummon5 = 367, EfstCMarker = 755;
        public const string CMarkerEffect = "ef_c_marker2";

        /// <summary>The val1 of EFST_SUMMON1..5 (0 = no sphere in that slot).</summary>
        public readonly int[] Summon = new int[5];
        public int SphereCount { get { int n = 0; foreach (int v in Summon) if (v != 0) n++; return n; } }
        public string LastLook = "";
        /// <summary>True Sight's start-frame looks played (smoke), and C_Marker's effect spams with their drawable parts.</summary>
        public int TrueSightPlays, CMarkers, CMarkerParts;

        public static UnitEfstLooksW5 Of(Unit u) => u.GetComponent<UnitEfstLooksW5>() ?? u.gameObject.AddComponent<UnitEfstLooksW5>();
        public static UnitEfstLooksW5 Find(Unit u) => u != null ? u.GetComponent<UnitEfstLooksW5>() : null;

        /// <summary>A status icon started / ended on a unit in view.</summary>
        public static void OnEfst(Unit u, int type, bool on, int val1, Effects.EffectManager fx, RoAssets assets)
        {
            if (u == null) return;
            if (type >= EfstSummon1 && type <= EfstSummon5)
            {
                // `if (pkt.state == 1) entity.SummonN = pkt.val[0]; else entity.SummonN = 0; updateWarlockSpheres(entity)` (Entity.js:2168-2211)
                var l = Of(u);
                l.Summon[type - EfstSummon1] = on ? val1 : 0;
                SpiritSpheres.OfWarlock(u).Set(l.SphereCount, false, assets);
                l.LastLook = $"warlock spheres {l.SphereCount}";
                Log(u, $"EFST_SUMMON{type - EfstSummon1 + 1} {(on ? "on, element 0x" + val1.ToString("x") : "off")} → {l.SphereCount} sphere(s) [{string.Join(",", l.Summon)}]");
                return;
            }
            switch (type)
            {
                case EfstTrueSight:
                {
                    var l = Of(u);
                    // rB acts on the start and the end alike; a walking unit keeps walking here (Loki has no walk restart to fall back on)
                    if (u.Sprite != null && !u.Walking) { u.Sprite.PlayFrom(u.IsPlayerType ? (int)ActorSprite.PlayerAction.Idle : (int)ActorSprite.MonsterAction.Idle, 3); l.TrueSightPlays++; }
                    l.LastLook = "true sight " + (on ? "on" : "off");
                    Log(u, $"true sight {(on ? "starts" : "ends")}: idle from frame 3 (walking {u.Walking})");
                    return;
                }
                case EfstCMarker:
                {
                    if (!on) return; // `if (pkt.state == 1)` (Entity.js:2316)
                    var l = Of(u);
                    int parts = fx != null ? fx.Spam(CMarkerEffect, u.Id, null, Time.time, -1f) : 0;
                    l.CMarkers++; l.CMarkerParts = parts; l.LastLook = "crimson marker";
                    Log(u, $"crimson marker: {CMarkerEffect} ({parts} drawable part(s))");
                    return;
                }
            }
        }

        private static void Log(Unit u, string what) => Debug.Log($"[W5Looks] {u.Id} ({u.Name}): {what}");
    }
}
