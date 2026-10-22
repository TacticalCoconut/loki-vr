using System.Collections.Generic;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 5 (w5move): how a player looks during a fast move, ZC_FASTMOVE 0x08d2 (clif_snap, rA clif.cpp:19940-19955; sent
    /// by Body Relocation, rA skills/acolyte/snap.cpp:18-20, Fallen Angel, skills/gunslinger/fallenangel.cpp:19, and All in the Sky,
    /// skills/taekwon/allinthesky.cpp:18). rAthena says nothing about the look; the only source is roBrowserLegacy, a reimplementation
    /// of the official client: onEntityFastMove (Engine/MapEngine/Entity.js:461-484) moves the unit with fastMoveTo (15 ms per
    /// cell, Renderer/Entity/EntityWalk.js:512-586) and, for a PC (objecttype TYPE_PC = 0),
    ///  - of the Monk line (DBManager.js:1004-1024 S_JOBS_MONK) shows the ATTACK action's frame 0 without playing it
    ///    (setAction { action: ATTACK, frame: 0, repeat: false, play: false }); ATTACK is the weapon's attack group
    ///    (EntityAction.js:94-113: getWeaponAction → ATTACK1 / 2 / 3; Loki: Unit.AttackActionGroup, the same table), and a
    ///    Monk's attack slice starts at frame 0 too (DBManager.js:2232-2247 getAttackSlice);
    ///  - of the Gunslinger line (DBManager.js:1105-1121 S_JOBS_GS; Loki: SpiritSpheres.IsGunslinger, the same ids) shows the
    ///    SKILL action's frame 0 the same way;
    ///  - in both cases draws after-images while the move runs (_fastMoveTrail, EntityRender.js:877-890 → the MakeBlur trail
    ///    :651-760, Sprites/FastMoveTrail.cs).
    /// When the move ends the unit stands idle (finishWalk, EntityWalk.js:646-701; Unit.Slide's endIdle).
    /// The ids below are rB's JobConst.js; the base classes are rAthena's (common/mmo.hpp e_job: JOB_MONK 15, JOB_CHAMPION 4016,
    /// JOB_BABY_MONK 4038, JOB_SURA 4070, JOB_SURA_T 4077, JOB_BABY_SURA 4106, JOB_INQUISITOR 4262, JOB_SURA_2ND 4342); the others
    /// are the client's riding / costume classes rB lists with them.
    /// </summary>
    public static class FastMoveLook
    {
        private static readonly HashSet<int> MonkJobs = new HashSet<int>
        {
            15, 4016, 4070, 4077, 4342, 4262, // MONK, MONK_H, SURA, SURA_H, SURA_2ND, INQUISITOR
            4038, 4106,                       // MONK_B, SURA_B
            4125, 4139, 4127, 4166, 4179, 4275, // SHEEP_MONK, SHEEP_CHAMP, SHEEP_SURA, SHEEP_MONK_B, SHEEP_SURA_B, INQUISITOR_RIDING
        };

        public static bool IsMonk(int job) => MonkJobs.Contains(job);

        /// <summary>The action group whose first frame <paramref name="u"/> holds during a 0x08d2 fast move, or -1.</summary>
        public static int HoldGroup(Unit u)
        {
            if (u == null || !u.IsPlayerType || u.ObjectType != 0) return -1; // rB: entity.objecttype === TYPE_PC
            if (IsMonk(u.Job)) return u.AttackActionGroup;
            if (SpiritSpheres.IsGunslinger(u.Job)) return (int)ActorSprite.PlayerAction.Skill;
            return -1;
        }
    }

    /// <summary>
    /// Client wave 5 (w5move): roBrowserLegacy's after-images while a Monk / Gunslinger fast-moves (Renderer/Entity/EntityRender.js:
    /// renderSecondBody :594-760 with enableTrail from _fastMoveTrail :877-890; a reimplementation): blur type 1 — a snapshot of the
    /// drawn frame every 30 ms while the unit fast-moves (:669, "Fast moves capture at 30ms interval"), at most 5 (:600
    /// trailLength), each drawn for 400 ms at alpha 0.4 × (1 − age / 400) in the unit's current colour (:715-728); nothing is drawn
    /// once the move ends (_fastMoveTrail false, finishWalk EntityWalk.js:657-660, and renderSecondBody returns, :602-604).
    /// Drawn like w3status's after-images (Sprites/UnitVirtueFx.cs): Graphics.DrawMesh after the units, ghosts kept in the map's
    /// space; not drawn while the unit's own sprite is hidden (our character in first person, a hidden unit). It runs after the
    /// sprites' LateUpdate (execution order 100), so a snapshot is the frame drawn now, as rB's is ("layers: … current animation frames", EntityRender.js:692-701).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FastMoveTrail : MonoBehaviour
    {
        private const float IntervalS = 0.03f, LifeMs = 400f, Alpha = 0.4f;
        private const int MaxGhosts = 5;
        private struct Ghost { public Mesh Mesh; public Texture Tex; public Matrix4x4 M; public float T; }
        private readonly List<Ghost> _ghosts = new List<Ghost>();
        private Unit _u; private bool _on; private float _last = -10f;
        private static Material _mat;
        private MaterialPropertyBlock _mpb;

        /// <summary>Snapshots taken during the last fast move, and ghosts drawn in the last frame (smoke).</summary>
        public int Snapshots { get; private set; }
        public int GhostsDrawn { get; private set; }
        public int MaxGhostsDrawn { get; private set; }

        /// <summary>Starts the trail for the fast move <paramref name="u"/> has just begun (Unit.Slide); it ends with the slide.</summary>
        public static FastMoveTrail Begin(Unit u)
        {
            var t = u.GetComponent<FastMoveTrail>();
            if (t == null) { t = u.gameObject.AddComponent<FastMoveTrail>(); t._u = u; }
            t._ghosts.Clear(); t._on = true; t._last = -10f; t.Snapshots = 0; t.MaxGhostsDrawn = 0;
            return t;
        }

        private void LateUpdate()
        {
            GhostsDrawn = 0;
            if (!_on) return;
            if (_u == null || !_u.Sliding) { _on = false; _ghosts.Clear(); return; } // finishWalk: the trail goes with the move
            var s = _u.Sprite; var quad = s != null ? s.CurrentQuad : null; var mesh = s != null ? s.CurrentMesh : null; var tex = s != null ? s.CurrentTexture : null;
            if (quad == null || mesh == null || tex == null) return;
            var rend = quad.GetComponent<Renderer>();
            var tint = s.Tint;
            if ((rend != null && !rend.enabled) || tint.a <= 0.01f) return; // our own body in first person, a hidden unit
            var map = transform.parent;
            if (Time.time - _last >= IntervalS)
            {
                var local = map != null ? map.worldToLocalMatrix * quad.localToWorldMatrix : quad.localToWorldMatrix;
                _ghosts.Insert(0, new Ghost { Mesh = mesh, Tex = tex, M = local, T = Time.time });
                if (_ghosts.Count > MaxGhosts) _ghosts.RemoveAt(_ghosts.Count - 1);
                _last = Time.time; Snapshots++;
            }
            _mpb ??= new MaterialPropertyBlock();
            if (_mat == null) { _mat = new Material(Shader.Find("Loki/Sprite")) { renderQueue = 3000 }; _mat.SetFloat("_Relief", 0f); }
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var gh = _ghosts[i];
                float age = (Time.time - gh.T) * 1000f;
                if (age > LifeMs) continue;
                float a = Mathf.Max(0f, Alpha * (1f - age / LifeMs));
                if (a <= 0.05f) continue;
                _mpb.Clear(); _mpb.SetTexture("_MainTex", gh.Tex); _mpb.SetColor("_Color", new Color(tint.r, tint.g, tint.b, a));
                Graphics.DrawMesh(gh.Mesh, map != null ? map.localToWorldMatrix * gh.M : gh.M, _mat, 0, null, 0, _mpb);
                GhostsDrawn++;
            }
            if (GhostsDrawn > MaxGhostsDrawn) MaxGhostsDrawn = GhostsDrawn;
        }
    }
}
