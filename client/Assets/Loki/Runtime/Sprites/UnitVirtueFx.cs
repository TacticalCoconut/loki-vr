using System.Collections.Generic;
using UnityEngine;

namespace Loki.Sprites
{
    /// <summary>
    /// Client wave 3 (w3status): the opt3 looks besides the tint, after roBrowserLegacy (a reimplementation; the official client could
    /// not be checked) Renderer/Entity/EntityRender.js:
    ///  • Assumptio's halo (855-890 enableHalo → renderSecondBody 613-648): the body drawn a second time, 1.15× its size, white at alpha
    ///    0.6, additive (blend mode ONE), behind the body. Loki scales the one composite image around its centre (rB scales every layer
    ///    around its own centre: the same outline for a single-layer body, close for a composite).
    ///  • After-images (MakeBlur, 650-760) while EnergyCoat, Bunsin, Quicken, OverThrust, ExplosionSpirits or Berserk is on:
    ///    blur type 1 — a snapshot of the frame every 80 ms while the unit walks, up to 5, each drawn for 400 ms at alpha
    ///    0.4 × (1 − age / 400) in the unit's current colour; Bunsin: blur type 5 — a snapshot every 560 ms while it attacks or casts,
    ///    drawn white. (Hallucination Walk's type 3 is not an opt3 rAthena sends.)
    /// Driven by UnitState (its Virtue); drawn with Graphics.DrawMesh after the units (render queue 3000), so nothing is kept per ghost.
    /// </summary>
    public sealed class UnitVirtueFx : MonoBehaviour
    {
        private const int TrailBits = UnitState.Opt3EnergyCoat | UnitState.Opt3Bunsin | UnitState.Opt3Quicken | UnitState.Opt3OverThrust | UnitState.Opt3ExplosionSpirits | UnitState.Opt3Berserk;
        private const float HaloScale = 1.15f, HaloAlpha = 0.6f, TrailAlpha = 0.4f;
        private const int TrailMax = 5;

        private struct Ghost { public Mesh Mesh; public Texture Tex; public Matrix4x4 M; public float T; }

        private Unit _u;
        private int _virtue;
        private readonly List<Ghost> _ghosts = new List<Ghost>();
        private float _lastSnap = -10f;
        private static Material _alphaMat, _addMat;
        private MaterialPropertyBlock _mpb;

        /// <summary>Client wave 5 (w5looks): the halo an effect's body animation turns on (roBrowserLegacy's entity._enableHalo, read with
        /// Assumptio's opt3 at EntityRender.js:878: EF_BASH3D, EF_BASH3D2-4, EF_TRANSBLUEBODY, EF_BASH3D6 — Fx/BodyFx.cs).</summary>
        public bool FuncHalo;
        /// <summary>Client wave 5 (w5looks): EF_MAKEBLUR's after-images (rB entity._enableTrail with _blurType 1 for 2 s, EffectTable.js:4066-4084,
        /// read at EntityRender.js:887-890) run until this local time.</summary>
        public float FuncTrailUntil = -1f;
        public bool Halo => (_virtue & UnitState.Opt3Assumptio) != 0 || FuncHalo;
        public bool Trail => (_virtue & TrailBits) != 0 || Time.time < FuncTrailUntil;
        /// <summary>Snapshots taken since the trail started (smoke), and those drawn now.</summary>
        public int Snapshots { get; private set; }
        public int GhostsDrawn { get; private set; }

        public static UnitVirtueFx Of(Unit u)
        {
            var f = u.GetComponent<UnitVirtueFx>();
            if (f == null) { f = u.gameObject.AddComponent<UnitVirtueFx>(); f._u = u; }
            return f;
        }

        /// <summary>Client wave 5 (w5looks): EF_MAKEBLUR — blur type 1 after-images for <paramref name="seconds"/> (Fx/BodyFx.cs).</summary>
        public void StartFuncTrail(float seconds)
        {
            if (!Trail) Snapshots = 0;
            FuncTrailUntil = Mathf.Max(FuncTrailUntil, Time.time + seconds);
        }

        public void SetVirtue(int virtue)
        {
            if (((virtue ^ _virtue) & TrailBits) != 0 && (virtue & TrailBits) == 0) _ghosts.Clear();
            if ((virtue & TrailBits) != 0 && (_virtue & TrailBits) == 0) Snapshots = 0;
            _virtue = virtue;
        }

        private static Material Mat(ref Material m, string shader, bool additive)
        {
            if (m != null) return m;
            m = new Material(Shader.Find(shader)) { renderQueue = 3000 };
            if (!additive) m.SetFloat("_Relief", 0f);
            return m;
        }

        private void LateUpdate()
        {
            GhostsDrawn = 0;
            if (_u == null || _u.Sprite == null || !(Halo || Trail)) return;
            var s = _u.Sprite; var quad = s.CurrentQuad; var mesh = s.CurrentMesh; var tex = s.CurrentTexture;
            if (quad == null || mesh == null || tex == null) return;
            _mpb ??= new MaterialPropertyBlock();
            var tint = s.Tint;
            if (tint.a <= 0.01f) return; // hidden: no halo, no ghosts

            if (Halo)
            {
                // behind the body (+z is away from the viewer), 1.15× around the composite's centre (the quad's origin)
                var m = quad.localToWorldMatrix * Matrix4x4.TRS(new Vector3(0f, 0f, 2f), Quaternion.identity, Vector3.one * HaloScale);
                _mpb.Clear(); _mpb.SetTexture("_MainTex", tex); _mpb.SetColor("_Color", new Color(1f, 1f, 1f, HaloAlpha));
                Graphics.DrawMesh(mesh, m, Mat(ref _addMat, "Loki/SpriteAdd", true), 0, null, 0, _mpb);
            }

            if (!Trail) return;
            bool bunsin = (_virtue & UnitState.Opt3Bunsin) != 0 && (_virtue & (TrailBits & ~UnitState.Opt3Bunsin)) == 0; // rB: isBUNSIN ? 5 : …
            int g = s.ActionGroup;
            bool attacking = _u.IsPlayerType ? g == (int)ActorSprite.PlayerAction.Attack1 || g == (int)ActorSprite.PlayerAction.Attack2 || g == (int)ActorSprite.PlayerAction.Attack3 || g == (int)ActorSprite.PlayerAction.Skill
                                             : g == (int)ActorSprite.MonsterAction.Attack;
            // w5looks: rB also snapshots while the unit is pushed or fast-moved (isFastMoving, every 30 ms: EntityRender.js:669-690; set by
            // fastMoveTo for 0x08d2 and knockbacks, Engine/MapEngine/Entity.js:463, 495, 1697) — Loki's slides (Unit.Sliding) are those moves
            bool fast = _u.Sliding;
            float interval = fast ? 0.03f : bunsin ? 0.56f : 0.08f;
            bool capture = (bunsin ? attacking || fast : _u.Walking || fast) && Time.time - _lastSnap > interval;
            // the diorama's map root moves (pan, recentre, scale): a ghost is kept in the map's space, where the unit walked
            var map = transform.parent;
            if (capture)
            {
                var local = map != null ? map.worldToLocalMatrix * quad.localToWorldMatrix : quad.localToWorldMatrix;
                _ghosts.Insert(0, new Ghost { Mesh = mesh, Tex = tex, M = local, T = Time.time });
                if (_ghosts.Count > TrailMax) _ghosts.RemoveAt(_ghosts.Count - 1);
                _lastSnap = Time.time; Snapshots++;
            }
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var gh = _ghosts[i];
                float age = (Time.time - gh.T) * 1000f;
                if (age > 400f) continue;
                float a = Mathf.Max(0f, TrailAlpha * (1f - age / 400f));
                if (a <= 0.05f) continue;
                var c = bunsin ? new Color(1f, 1f, 1f, a) : new Color(tint.r, tint.g, tint.b, a);
                _mpb.Clear(); _mpb.SetTexture("_MainTex", gh.Tex); _mpb.SetColor("_Color", c);
                Graphics.DrawMesh(gh.Mesh, map != null ? map.localToWorldMatrix * gh.M : gh.M, Mat(ref _alphaMat, "Loki/Sprite", false), 0, null, 0, _mpb);
                GhostsDrawn++;
            }
        }
    }
}
