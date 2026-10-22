using UnityEngine;

namespace Loki.Effects
{
    /// <summary>
    /// Client wave 4c (w4fx2): the FUNC parts of roBrowserLegacy's effect table that spin the owner's body — its facing turned step by step
    /// with Events.setTimeout (a reimplementation, the only source; the official client could not be checked):
    ///  • EF_CASTSPIN 501 (ST_CHASEWALK's beginCastEffectId): 50 steps over 500 ms, step i adds 12 / 18 / 24 / 48 × (8 / 360) for i ≤ 15 / ≤ 30
    ///    / ≤ 40 / after, the direction being floor(direction + delta) % 8 (DB/Effects/EffectTable.js:10203-10233) — only the last steps reach
    ///    a whole direction, as in rB;
    ///  • EF_CASTSPIN2 775 (GC_ROLLINGCUTTER, EffectTable.js:13497-13517), ef_moonslasher (LG_MOONSLASHER, :15423-15446), ef_windmill
    ///    (SR_WINDMILL, :15602-15625): 8 steps of +1 over 500 ms (one full turn).
    /// Directions are RO's (0 south, counter-clockwise), the same in rB (Entity.js:1244) and Loki (Unit.FromServerDir). The owner keeps the
    /// direction the last step leaves, as rB's entity does.
    /// </summary>
    public sealed class SpinFx : FxRenderer
    {
        public static bool Handles(string key) => key == "501" || key == "775" || key == "ef_moonslasher" || key == "ef_windmill";
        private int _steps, _done; private float _stepMs; private bool _chaseWalk; private float _dir;

        public static SpinFx Create(EffectManager m, EffectManager.Instance inst)
        {
            if (!Handles(inst.Key) || inst.Owner == null || inst.Owner.Sprite == null) return null;
            var f = Make<SpinFx>(m, inst, "spin:" + inst.Key);
            f._chaseWalk = inst.Key == "501";
            f._steps = f._chaseWalk ? 50 : 8; f._stepMs = 500f / f._steps; // duration 500 ms, delay = duration / count (both FUNCs)
            f._dir = inst.Owner.Sprite.Direction;
            return f;
        }

        public override bool EndsAtEndTick => false;

        private void LateUpdate()
        {
            if (OwnerGone || Inst.Owner == null || Inst.Owner.Sprite == null) { Finished = true; return; }
            float ms = T * 1000f;
            while (_done < _steps && _done * _stepMs <= ms)
            {
                int i = _done++;
                float delta = !_chaseWalk ? 1f : i <= 15 ? 12f * (8f / 360f) : i <= 30 ? 18f * (8f / 360f) : i <= 40 ? 24f * (8f / 360f) : 48f * (8f / 360f);
                // rB: entity.direction = Math.floor(entity.direction + delta) % 8, on the entity's integer direction
                _dir = Mathf.Floor(Inst.Owner.Sprite.Direction + delta) % 8;
                Inst.Owner.Sprite.Direction = (int)_dir;
            }
            if (_done >= _steps) Finished = true;
        }
    }
}
