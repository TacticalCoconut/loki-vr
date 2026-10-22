using Loki.Effects;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5, w5looks: the looks roBrowserLegacy draws and Loki did not (design notes, scope w5looks). The pieces live in their
    /// own files; this part wires them to the game:
    ///  • Sprites/UnitEfstLooksW5.cs — a Warlock's elemental spheres (EFST_SUMMON1..5), True Sight's pose, Crimson Marker's mark;
    ///  • Fx/ScreenFlip.cs — Hallucination (EFST_ILLUSION on ourselves) turns the view upside down, only with the Comfort option;
    ///  • Fx/BodyFx.cs — rB's FUNC effects that animate a body (colour flashes, the halo, Baby / Giant size, a turn, an after-image trail),
    ///    shake the camera (Fx/CameraQuake.cs, only with the Comfort option) or play timed sounds (EffectManager starts them);
    ///  • Sprites/SpiritSpheres.cs (the Warlock's instance), Sprites/ActorSprite.cs (a start frame), Sprites/Unit.cs (an effect's body size),
    ///    Sprites/UnitVirtueFx.cs (the effects' halo and trail).
    /// Smoke: LokiGame.W5LooksSmoke.cs (-lokiSmokeOnly w5looks-host / w5looks-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW5Looks(LokiBootstrap boot)
        {
            ScreenFlip.Hallucinating = false; // a new scene (character select, log out): our status icons come again with the next map
            if (boot.Rig != null) { CameraQuake.Attach(boot.Rig); ScreenFlip.Attach(boot.Rig.Head); }
            Prog.AnyEffectChanged += OnW5LooksEfst;
        }

        private void OnW5LooksEfst(uint aid, int type, bool on, int val1)
        {
            if (type == UnitEfstLooksW5.EfstIllusion && aid == Session.AccountId)
            {
                if (ScreenFlip.Hallucinating != on) Debug.Log(on ? $"[W5Looks] hallucination starts: the view {(VR.Comfort.HallucinationFlip ? "turns upside down" : "stays upright (Comfort option off)")}" : "[W5Looks] hallucination ends: the view upright");
                ScreenFlip.Hallucinating = on;
            }
            var u = Entities.Get(aid);
            if (u == null) return; // as rB (Entity.js:1957-1963): the entry packets / 0x0984 bring what shows when the unit comes back
            UnitEfstLooksW5.OnEfst(u, type, on, val1, Effects, _boot.Assets);
        }
    }
}
